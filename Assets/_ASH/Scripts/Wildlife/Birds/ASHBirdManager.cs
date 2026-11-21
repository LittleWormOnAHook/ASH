using System.Collections.Generic;
using ASH.Companion;
using ASH.Inventory;
using Invector;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.Wildlife.Birds
{
    /// <summary>
    /// Spawns, flocks, lands and culls pooled birds around the player. Created automatically at runtime in scenes with a Terrain
    /// (no scene edits). Solo birds and small flocks fly with boids rules, pick perches by raycasting onto terrain or the top edges of
    /// colliders, idle / peck, and flush when the player comes close or a shot is fired. All tunables: Resources/ASH_BirdSettings.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class ASHBirdManager : MonoBehaviour
    {
        public static ASHBirdManager Active { get; private set; }

        public sealed class Flock
        {
            public ASHBirdSettings.BirdType type;
            public readonly List<ASHBird> m = new List<ASHBird>();
            public bool landed, landing;
            public Vector3 target, home;
            public float flyUntil, landedUntil, callAt, scareAt;
            public ASHBirdSpot spot;
            // noise / alert
            public bool alert;
            public float alertUntil, flushAt;
            public Vector3 threat;
        }

        struct PendingCall { public float at; public AudioClip[] clips; public Vector3 pos; public float vol, pitch; }
        readonly List<PendingCall> _calls = new List<PendingCall>();
        Vector3 _pPrev, _dPrev;
        float _pSpeed, _dSpeed;
        bool _pHave, _dHave;

        readonly List<ASHBird> _all = new List<ASHBird>();
        readonly Stack<ASHBird> _pool = new Stack<ASHBird>();
        readonly List<Flock> _flocks = new List<Flock>();
        readonly List<ASHBird> _dead = new List<ASHBird>();
        System.Random _rng;
        Transform _player;
        Camera _cam;
        float _spawnAt, _findAt, _gunSeen = -999f, _spotAt;
        AudioSource[] _voices;
        int _voice;
        int _mask = ~0;
        static bool _hooked;

        static ASHBirdSettings S => ASHBirdSettings.I;
        public int ActiveBirds => _all.Count - _pool.Count;
        public int ActiveGroups => _flocks.Count;

        // ------------------------------------------------------------------ bootstrap (no scene edits)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _hooked = false; Active = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!_hooked) { SceneManager.sceneLoaded += (s, m) => TryCreate(); _hooked = true; }
            TryCreate();
        }

        static void TryCreate()
        {
            if (Active) return;
            var s = S;
            if (!s || !s.birdsEnabled || (!s.autoSpawn && ASHBirdSpot.All.Count == 0) || !s.birdPrefab) return;
            if (!Terrain.activeTerrain) return;
            if (s.sceneNames != null && s.sceneNames.Count > 0 && !s.sceneNames.Contains(SceneManager.GetActiveScene().name)) return;
            new GameObject("ASH_Birds").AddComponent<ASHBirdManager>();
        }

        void Awake()
        {
            if (Active && Active != this) { Destroy(gameObject); return; }
            Active = this;
            var s = S;
            _rng = s && s.seed != 0 ? new System.Random(s.seed) : new System.Random(System.Environment.TickCount);
            _spawnAt = Time.time + 1.5f;
        }

        void OnDestroy() { if (Active == this) Active = null; }

        float R01() => (float)_rng.NextDouble();
        float Rng(float a, float b) => Mathf.Lerp(a, b, R01());

        // ------------------------------------------------------------------ main loop
        void Update()
        {
            var s = S;
            if (!s || !s.birdsEnabled || !s.birdPrefab) { if (_flocks.Count > 0 || _dead.Count > 0) DespawnAll(); return; }
            if ((!_player || !_player.gameObject.activeInHierarchy) && Time.time >= _findAt) { _findAt = Time.time + 1f; FindPlayer(); }
            if (!_player) return;
            if (!_cam || !_cam.isActiveAndEnabled) _cam = Camera.main;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _mask = s.PerchMask();
            Vector3 eye = _cam ? _cam.transform.position : _player.position;

            TrackNoiseSources(Time.deltaTime);
            for (int i = _calls.Count - 1; i >= 0; i--)
                if (Time.time >= _calls[i].at) { var c = _calls[i]; _calls.RemoveAt(i); PlayAt(c.clips, c.pos, c.vol, c.pitch); }
            GunshotCheck(s);
            if (Time.time >= _spawnAt) { _spawnAt = Time.time + s.spawnInterval; if (s.autoSpawn) TrySpawn(s); }
            if (Time.time >= _spotAt) { _spotAt = Time.time + s.spotCheckInterval; SpotTick(s); }

            for (int i = _flocks.Count - 1; i >= 0; i--)
            {
                var f = _flocks[i];
                f.m.RemoveAll(b => !b || b.IsDead || b.state == ASHBirdState.Pooled);
                if (f.m.Count == 0) { if (f.spot) { f.spot.flock = null; f.spot.respawnAt = Time.time + f.spot.respawnSeconds; } _flocks.RemoveAt(i); continue; }
                if (Flat(Centroid(f), _player.position) > Mathf.Max(s.despawnDistance, f.spot ? f.spot.activateDistance * 1.2f : 0f))
                { foreach (var b in f.m) Release(b); if (f.spot) { f.spot.flock = null; f.spot.respawnAt = 0f; } _flocks.RemoveAt(i); continue; }
                FlockTick(f, s);
                for (int k = 0; k < f.m.Count; k++)
                {
                    var b = f.m[k];
                    float dCam = (b.transform.position - eye).magnitude;
                    // LOD: landed birds far from the camera freeze their animator; far birds tick every Nth frame
                    b.SetAnimatorOn(b.state != ASHBirdState.Landed || dCam < s.animCullDistance);
                    b.tickAcc += dt;
                    if (dCam > s.tickLodDistance && ((Time.frameCount + b.GetHashCode()) % Mathf.Max(1, s.farTickEvery)) != 0) continue;
                    float step = Mathf.Min(b.tickAcc, 0.25f);
                    b.tickAcc = 0f;
                    BirdTick(b, f, s, step);
                }
            }

            for (int i = _dead.Count - 1; i >= 0; i--)
            {
                var b = _dead[i];
                if (!b || b.state == ASHBirdState.Pooled) { _dead.RemoveAt(i); continue; }
                if (b.state == ASHBirdState.Falling) FallTick(b, s, dt);
                if (Time.time - b.deadAt > s.deadLifetime || Flat(b.transform.position, _player.position) > s.despawnDistance * 1.2f)
                { Release(b); _dead.RemoveAt(i); }
            }
        }

        void FindPlayer()
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            _player = p ? p.transform : null;
            if (!_player && ASH.Combat.ASHCombatBridge.Player) _player = ASH.Combat.ASHCombatBridge.Player.transform;
        }

        void GunshotCheck(ASHBirdSettings s)
        {
            float t = ASHWildlifeStimulus.LastGunshotTime;
            if (t <= _gunSeen || Time.time - t > 0.5f) return;
            _gunSeen = t;
            var at = ASHWildlifeStimulus.LastGunshotPos;
            float range = ASHWildlifeStimulus.LastGunshotLoud ? s.gunshotScareMeters : Mathf.Min(s.gunshotScareMeters, ASHWildlifeStimulus.LastGunshotRange * 1.5f);
            for (int i = 0; i < _flocks.Count; i++)
            {
                var f = _flocks[i];
                // shot at / near miss: attackers within trigger range go for the shooter (or the dog)
                if (s.attackBirdsEnabled)
                    foreach (var b in f.m)
                        if (b.aggressive && b.state != ASHBirdState.Attack && (b.transform.position - at).magnitude <= s.attackTriggerMeters) TryStartAttack(b, s);
                if ((f.landed || f.landing) && Flat(Centroid(f), at) <= range) TakeOff(f, at, true, Nearest(f, at), s.gunshotCascadeMul);
            }
        }

        // ------------------------------------------------------------------ spawning / pooling
        void TrySpawn(ASHBirdSettings s)
        {
            if (_flocks.Count >= s.maxGroups) return;
            int free = s.maxBirds - ActiveBirds;
            if (free <= 0) return;
            var t = s.Pick(_rng);
            if (t == null) return;
            int n = R01() < t.soloChance ? 1 : _rng.Next(Mathf.Max(1, t.flockMin), Mathf.Max(t.flockMin, t.flockMax) + 1);
            n = Mathf.Min(n, free);
            if (n <= 0) return;

            float ang = R01() * Mathf.PI * 2f;
            float r = Rng(s.spawnRadiusMin, Mathf.Max(s.spawnRadiusMin, s.spawnRadiusMax));
            var c = _player.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
            c.y = GroundY(c, s);
            SpawnFlock(s, t, c, n, R01() < s.startLandedChance, null, -1f, s.landingSearchRadius * 0.6f);
        }

        Flock SpawnFlock(ASHBirdSettings s, ASHBirdSettings.BirdType t, Vector3 c, int n, bool landed, ASHBirdSpot spot, float aggressiveShare, float perchRadius)
        {
            var f = new Flock { type = t, home = c, callAt = Time.time + Rng(1f, 6f), spot = spot };

            for (int i = 0; i < n; i++)
            {
                var b = Get(s);
                if (!b) break;
                b.Apply(t, _rng, aggressiveShare);
                b.flock = f;
                f.m.Add(b);
                if (landed && FindPerch(c, perchRadius, t, s, out var p, out var fwd, out bool ground))
                {
                    Place(b, p, fwd, ground);
                    continue;
                }
                var pos = c + Random3(4f) + Vector3.up * Rng(t.altitudeMin, t.altitudeMax);
                b.transform.position = pos;
                var dir = (_player.position - c); dir.y = 0f; dir = (dir.normalized + Random3(0.5f)).normalized;
                b.vel = dir * t.flySpeed;
                b.transform.rotation = Quaternion.LookRotation(b.vel);
                b.state = ASHBirdState.Fly;
                b.stateAt = b.launchAt = Time.time;
                b.groundCheckAt = 0f;
            }
            if (f.m.Count == 0) return null;
            bool anyLanded = false;
            foreach (var b in f.m) if (b.state == ASHBirdState.Landed) anyLanded = true;
            if (anyLanded)
            {
                // stragglers that found no perch drop to the ground near the group
                foreach (var b in f.m)
                    if (b.state == ASHBirdState.Fly) { var g = c + Random3(3f); g.y = GroundY(g, s); Place(b, g, Random3(1f), true); }
                f.landed = true;
                f.landedUntil = Time.time + Rng(t.landedMinSeconds, Mathf.Max(t.landedMinSeconds, t.landedMaxSeconds));
            }
            else
            {
                // fly across the player's area
                var across = _player.position + Random3(25f);
                f.target = new Vector3(across.x, GroundY(across, s) + Rng(t.altitudeMin, t.altitudeMax), across.z);
                f.home = f.target;
                f.flyUntil = Time.time + Rng(s.flyMinSeconds, Mathf.Max(s.flyMinSeconds, s.flyMaxSeconds));
            }
            if (spot) f.home = c;
            _flocks.Add(f);
            return f;
        }

        // ------------------------------------------------------------------ resident spots (e.g. aggressive crow roosts)
        void SpotTick(ASHBirdSettings s)
        {
            foreach (var spot in ASHBirdSpot.All)
            {
                if (!spot || !spot.isActiveAndEnabled) continue;
                if (spot.flock != null) { if (!_flocks.Contains(spot.flock)) spot.flock = null; else continue; }
                if (Time.time < spot.respawnAt) continue;
                var pos = spot.transform.position;
                float d = Flat(pos, _player.position);
                var t = s.Find(spot.typeId);
                if (t == null || d > spot.activateDistance || d < t.takeOffDistance * 1.5f) continue;
                int free = s.maxBirds - ActiveBirds;
                int n = Mathf.Min(free, _rng.Next(Mathf.Max(1, spot.countMin), Mathf.Max(spot.countMin, spot.countMax) + 1));
                if (n <= 0) return;
                spot.flock = SpawnFlock(s, t, pos, n, true, spot, spot.aggressiveFraction, spot.radius);
            }
        }

        ASHBird Get(ASHBirdSettings s)
        {
            if (_pool.Count > 0)
            {
                var p = _pool.Pop();
                if (p) { p.gameObject.SetActive(true); return p; }
            }
            if (_all.Count >= s.maxBirds) return null;
            var go = Instantiate(s.birdPrefab, transform);
            go.name = "ASH_Bird_" + _all.Count;
            var b = go.GetComponent<ASHBird>() ?? go.AddComponent<ASHBird>();
            b.mgr = this;
            int layer = LayerMask.NameToLayer("Enemy");
            if (layer >= 0) go.layer = layer;
            if (s.cullScreenSize > 0f)
            {
                var lod = go.GetComponent<LODGroup>() ?? go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(s.cullScreenSize, go.GetComponentsInChildren<Renderer>(true)) });
                lod.RecalculateBounds();
            }
            _all.Add(b);
            return b;
        }

        void Release(ASHBird b)
        {
            if (!b || b.state == ASHBirdState.Pooled) return;
            b.state = ASHBirdState.Pooled;
            b.flock = null;
            b.gameObject.SetActive(false);
            _pool.Push(b);
        }

        void DespawnAll()
        {
            foreach (var f in _flocks) foreach (var b in f.m) Release(b);
            _flocks.Clear();
            foreach (var b in _dead) Release(b);
            _dead.Clear();
        }

        void Place(ASHBird b, Vector3 p, Vector3 fwd, bool ground)
        {
            b.transform.position = p;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Random3(1f);
            fwd.y = 0f;
            b.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            b.targetYaw = b.transform.eulerAngles.y;
            b.perch = p; b.perchFwd = fwd; b.perchGround = ground;
            b.vel = Vector3.zero;
            b.state = ASHBirdState.Landed;
            b.stateAt = Time.time;
            b.nextAnimSwap = Time.time + Rng(0.2f, 2f);
            b.groundHome = p;
            b.gMode = 0;
            b.alertTarget = 0f;
            b.surface = Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var sh, 0.7f, _mask, QueryTriggerInteraction.Ignore) ? sh.collider : null;
        }

        // ------------------------------------------------------------------ flock brain
        void FlockTick(Flock f, ASHBirdSettings s)
        {
            var t = f.type;
            if (f.landed || f.landing)
            {
                if (Time.time >= f.scareAt)
                {
                    f.scareAt = Time.time + s.noiseCheckInterval;
                    if (NoiseCheck(f, s, t)) return;
                }
                if (f.landed && Time.time >= f.landedUntil) { TakeOff(f, Vector3.zero, false); return; }
                if (f.landing)
                {
                    bool all = true, any = false;
                    foreach (var m in f.m) { if (m.state == ASHBirdState.Attack) continue; if (m.state == ASHBirdState.Landed) any = true; else all = false; }
                    if (all)
                    {
                        f.landing = false; f.landed = true;
                        f.landedUntil = Time.time + Rng(t.landedMinSeconds, Mathf.Max(t.landedMinSeconds, t.landedMaxSeconds));
                    }
                    else if (any)
                    {
                        // birds that lost their perch try again next to a mate that made it
                        foreach (var m in f.m)
                            if (m.state == ASHBirdState.Fly && Time.time - m.stateAt > 1f) AssignPerch(m, LandedMate(f), s.landingSearchRadius * 0.5f, s);
                    }
                }
            }
            else
            {
                var c = Centroid(f);
                if ((c - f.target).sqrMagnitude < 100f)
                {
                    var w = f.home + Random3(f.spot ? Mathf.Max(10f, f.spot.radius * 3f) : s.wanderRadius);
                    f.target = new Vector3(w.x, GroundY(w, s) + Rng(t.altitudeMin, t.altitudeMax), w.z);
                }
                if (Time.time >= f.flyUntil) BeginLanding(f, s);
            }

            if (Time.time >= f.callAt && t.calls != null && t.calls.Length > 0)
            {
                var caller = f.m[_rng.Next(f.m.Count)];
                PlayAt(t.calls, caller.transform.position, t.callVolume, t.callPitch);
                float k = f.landed ? 1f : 1.6f;
                f.callAt = Time.time + Rng(t.callIntervalMin, Mathf.Max(t.callIntervalMin, t.callIntervalMax)) * k;
            }
        }

        Vector3 LandedMate(Flock f)
        {
            foreach (var m in f.m) if (m.state == ASHBirdState.Landed) return m.transform.position;
            return Centroid(f);
        }

        void BeginLanding(Flock f, ASHBirdSettings s)
        {
            var c = Centroid(f);
            var v = AvgVel(f); v.y = 0f;
            var area = f.spot ? f.spot.transform.position : c + (v.sqrMagnitude > 0.1f ? v.normalized : Random3(1f).normalized) * Rng(10f, 25f);
            float searchR = f.spot ? f.spot.radius : s.landingSearchRadius;
            // don't pick a landing area right next to the player
            if (_player && Flat(area, _player.position) < f.type.takeOffDistance * 1.8f)
                area = _player.position + (area - _player.position).normalized * f.type.takeOffDistance * 2.2f;
            int ok = 0;
            foreach (var m in f.m) { if (m.state == ASHBirdState.Attack) continue; if (AssignPerch(m, area, searchR, s)) ok++; }
            if (ok == 0)
            {
                foreach (var m in f.m) if (m.state != ASHBirdState.Attack) m.state = ASHBirdState.Fly;
                f.flyUntil = Time.time + 5f;
                return;
            }
            f.landing = true;
        }

        bool AssignPerch(ASHBird b, Vector3 area, float radius, ASHBirdSettings s)
        {
            if (FindPerch(area, radius, b.type, s, out var p, out var fwd, out bool ground))
            {
                b.perch = p; b.perchFwd = fwd; b.perchGround = ground;
                b.state = ASHBirdState.Approach;
                b.stateAt = Time.time;
                return true;
            }
            b.state = ASHBirdState.Fly;
            b.stateAt = Time.time;
            return false;
        }

        void TakeOff(Flock f, Vector3 threat, bool scared, Vector3 origin = default, float cascadeMul = 1f)
        {
            var s = S;
            var t = f.type;
            var c = Centroid(f);
            f.alert = false; f.flushAt = 0f;
            if (scared && origin.sqrMagnitude < 0.0001f) origin = threat.sqrMagnitude > 0.01f ? Nearest(f, threat) : c;
            bool firstCall = true;
            if (scared && t.calls != null && t.calls.Length > 0) { PlayAt(t.calls, origin, t.callVolume, t.callPitch * s.alarmPitchMul); firstCall = false; }
            Vector3 away = threat.sqrMagnitude > 0.01f ? c - threat : Random3(1f);
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            var dest = c + away * (scared ? s.fleeDistance : Rng(s.fleeDistance * 0.4f, s.fleeDistance));
            f.target = new Vector3(dest.x, GroundY(dest, s) + Rng(t.altitudeMin, t.altitudeMax), dest.z);
            f.home = f.spot ? f.spot.transform.position : f.target;
            f.landed = false; f.landing = false;
            f.flyUntil = Time.time + Rng(s.flyMinSeconds, Mathf.Max(s.flyMinSeconds, s.flyMaxSeconds)) * (f.spot ? 0.5f : 1f);
            bool any = false;
            foreach (var b in f.m)
            {
                if (b.state == ASHBirdState.Attack) continue;
                if (b.state != ASHBirdState.Landed && b.state != ASHBirdState.Approach) { b.state = ASHBirdState.Fly; continue; }
                bool wasLanded = b.state == ASHBirdState.Landed;
                any |= wasLanded;
                b.state = ASHBirdState.Fly;
                b.stateAt = Time.time;
                float delay = 0f;
                if (wasLanded)
                {
                    if (scared)
                    {
                        // startle cascade: the bird nearest the noise goes first, mates follow by distance with a little jitter
                        delay = Flat(b.transform.position, origin) * s.startleSpreadPerMeter * cascadeMul + Rng(0f, s.startleJitter * Mathf.Max(0.3f, cascadeMul));
                        delay = Mathf.Min(delay, s.startleMaxDelay);
                        b.alertTarget = 1f;
                        if (t.calls != null && t.calls.Length > 0 && (firstCall || R01() < s.alarmCallChance))
                        {
                            _calls.Add(new PendingCall { at = Time.time + delay, clips = t.calls, pos = b.transform.position, vol = t.callVolume * (firstCall ? 1f : 0.8f), pitch = t.callPitch * s.alarmPitchMul });
                            firstCall = false;
                        }
                    }
                    else delay = Rng(0f, 1.2f);
                }
                b.launchAt = Time.time + delay;
                b.gMode = 0;
                var side = (away + Random3(0.6f)); side.y = 0f;
                if (wasLanded) b.vel = Vector3.up * s.takeOffUpSpeed * Rng(0.8f, 1.2f) + side.normalized * t.flySpeed * 0.35f;
                b.gliding = false;
                b.nextAnimSwap = Time.time + Rng(1.2f, 2.2f);
                b.groundCheckAt = 0f;
            }
            if (any && s.takeOffClips != null && s.takeOffClips.Length > 0)
            {
                // wing burst follows the cascade: first flaps at the origin, a second (bigger groups) as the rest lift
                float vol = s.takeOffVolume * Mathf.Clamp(0.6f + f.m.Count * 0.12f, 0.6f, 1.3f);
                float pitch = 1f / Mathf.Max(0.4f, Mathf.Sqrt(t.scale));
                if (scared && f.m.Count > 2)
                {
                    PlayAt(s.takeOffClips, origin, vol * 0.7f, pitch);
                    _calls.Add(new PendingCall { at = Time.time + Mathf.Min(s.startleMaxDelay, 0.25f + 0.3f * cascadeMul), clips = s.takeOffClips, pos = c, vol = vol * 0.8f, pitch = pitch * 0.97f });
                }
                else PlayAt(s.takeOffClips, c, vol, pitch);
            }
            if (scared && t.calls != null && t.calls.Length > 0)
            {
                f.callAt = Time.time + Rng(1.5f, 4f);
            }
        }

        // ------------------------------------------------------------------ per-bird motion
        void BirdTick(ASHBird b, Flock f, ASHBirdSettings s, float dt)
        {
            var t = b.type ?? f.type;
            switch (b.state)
            {
                case ASHBirdState.Fly:
                    if (Time.time < b.launchAt)
                    {
                        // about to flush: frozen head-up for its share of the cascade delay
                        b.Play(s.idleState, b.alertTarget > 0.5f ? s.alertAnimSpeed : s.groundAnimSpeed);
                        b.alertW = Mathf.MoveTowards(b.alertW, b.alertTarget, dt * 8f);
                        GroundPose(b, s, dt, 0f, 0f);
                        return;
                    }
                    b.alertW = b.alertTarget = 0f;
                    Boids(b, f, s, t, dt, f.target);
                    FlapOrGlide(b, s, t);
                    return;
                case ASHBirdState.Approach:
                    ApproachTick(b, f, s, t, dt);
                    return;
                case ASHBirdState.Landed:
                    LandedTick(b, s, t, dt);
                    return;
                case ASHBirdState.Attack:
                    AttackTick(b, f, s, t, dt);
                    return;
            }
        }

        void Boids(ASHBird b, Flock f, ASHBirdSettings s, ASHBirdSettings.BirdType t, float dt, Vector3 target)
        {
            var tr = b.transform;
            var pos = tr.position;
            Vector3 sep = Vector3.zero, ali = Vector3.zero, coh = Vector3.zero;
            int n = 0;
            float sepR = s.separationRadius * Mathf.Max(0.3f, t.scale);
            foreach (var o in f.m)
            {
                if (o == b || o.state != ASHBirdState.Fly) continue;
                var d = pos - o.transform.position;
                float dist = d.magnitude;
                n++;
                ali += o.vel;
                coh += o.transform.position;
                if (dist < sepR) sep += d / (dist * dist + 0.01f);
            }
            if (n > 0) { ali = ali / n - b.vel; coh = coh / n - pos; }
            float cruise = t.flySpeed * b.speedMul;
            var seek = target - pos;
            seek = seek.normalized * cruise - b.vel;

            if (Time.time >= b.groundCheckAt) { b.groundY = GroundY(pos, s); b.groundCheckAt = Time.time + Rng(0.35f, 0.6f); }
            float h = pos.y - b.groundY;
            float alt = h < t.altitudeMin ? (t.altitudeMin - h) : (h > t.altitudeMax ? -(h - t.altitudeMax) * 0.5f : 0f);
            alt = Mathf.Clamp(alt, -3f, 6f);

            if (Time.time >= b.avoidAt)
            {
                b.avoidAt = Time.time + 0.25f;
                b.avoidDir = Vector3.zero;
                var v = b.vel.sqrMagnitude > 0.01f ? b.vel.normalized : tr.forward;
                if (Physics.SphereCast(pos, 0.3f * Mathf.Max(0.3f, t.scale), v, out var hit, s.avoidLookAhead, _mask, QueryTriggerInteraction.Ignore))
                    b.avoidDir = (hit.normal + Vector3.up * 0.6f).normalized * (1f - hit.distance / s.avoidLookAhead);
            }

            var acc = sep * s.separationWeight + ali * (s.alignmentWeight * 0.5f) + coh * (s.cohesionWeight * 0.3f) + seek * (s.seekWeight * 0.5f)
                      + Vector3.up * (alt * s.altitudeWeight) + b.avoidDir * (s.avoidWeight * cruise * 0.5f);
            acc = Vector3.ClampMagnitude(acc, t.maxAccel);
            b.vel += acc * dt;
            float spd = Mathf.Clamp(b.vel.magnitude, cruise * 0.6f, cruise * 1.3f);
            b.vel = b.vel.sqrMagnitude > 0.0001f ? b.vel.normalized * spd : tr.forward * spd;
            b.vel.y = Mathf.Clamp(b.vel.y, -cruise * 0.55f, cruise * 0.6f);
            pos += b.vel * dt;
            float floor = b.groundY + 0.8f;
            if (pos.y < floor) { pos.y = floor; if (b.vel.y < 0f) b.vel.y = 0f; }
            tr.position = pos;
            Orient(b, acc, s, dt);
        }

        void Orient(ASHBird b, Vector3 acc, ASHBirdSettings s, float dt)
        {
            var tr = b.transform;
            if (b.vel.sqrMagnitude < 0.01f) return;
            var flatV = b.vel;
            float lateral = Vector3.Dot(acc, tr.right);
            float roll = Mathf.Clamp(-lateral * 4f, -s.maxBankDegrees, s.maxBankDegrees);
            var look = Quaternion.LookRotation(Vector3.Lerp(flatV.normalized, new Vector3(flatV.x, 0f, flatV.z).normalized, 0.4f), Vector3.up) * Quaternion.Euler(0f, 0f, roll);
            tr.rotation = Quaternion.Slerp(tr.rotation, look, 1f - Mathf.Exp(-6f * dt));
        }

        void FlapOrGlide(ASHBird b, ASHBirdSettings s, ASHBirdSettings.BirdType t)
        {
            if (Time.time >= b.nextAnimSwap)
            {
                b.gliding = b.vel.y < 0.6f && R01() < t.glideShare;
                b.nextAnimSwap = Time.time + (b.gliding ? Rng(0.6f, 1.6f) : Rng(0.9f, 2.4f));
            }
            if (b.vel.y > 1.2f) b.gliding = false;
            b.Play(s.flyState, b.gliding ? s.glideAnimSpeed : s.flapAnimSpeed * Mathf.Lerp(1.15f, 0.9f, Mathf.Clamp01(t.scale)));
        }

        void ApproachTick(ASHBird b, Flock f, ASHBirdSettings s, ASHBirdSettings.BirdType t, float dt)
        {
            var tr = b.transform;
            var pos = tr.position;
            var to = b.perch - pos;
            float d = to.magnitude;
            if (d < 0.18f || (d < 0.5f && b.vel.magnitude < s.touchdownSpeed * 1.2f))
            {
                Place(b, b.perch, b.perchFwd.sqrMagnitude > 0.01f ? b.perchFwd : tr.forward, b.perchGround);
                b.Play(s.idleState, s.groundAnimSpeed);
                return;
            }
            if (Time.time - b.stateAt > s.approachTimeout)
            {
                if (++b.perchFails > 2 || !AssignPerch(b, pos, s.landingSearchRadius * 0.6f, s)) { b.state = ASHBirdState.Fly; b.stateAt = Time.time; }
                return;
            }
            float cruise = t.flySpeed * b.speedMul;
            // come in from above so it drops onto the perch instead of skimming the ground
            var aim = b.perch + Vector3.up * Mathf.Clamp(d * 0.35f, 0f, 4f);
            var dir = (aim - pos).normalized;
            float want = d > s.approachDistance ? cruise : Mathf.Lerp(s.touchdownSpeed, cruise, d / s.approachDistance);
            var desired = dir * want;
            // light separation so mates don't merge
            foreach (var o in f.m)
            {
                if (o == b || o.state == ASHBirdState.Landed) continue;
                var off = pos - o.transform.position;
                float od = off.magnitude;
                if (od < 0.8f * Mathf.Max(0.3f, t.scale)) desired += off / (od + 0.05f) * 0.6f;
            }
            b.vel = Vector3.MoveTowards(b.vel, desired, t.maxAccel * 1.6f * dt);
            pos += b.vel * dt;
            tr.position = pos;
            Orient(b, desired - b.vel, s, dt);
            if (d < s.approachDistance * 0.6f) { var e = tr.eulerAngles; tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.Euler(0f, e.y, 0f), 1f - Mathf.Exp(-4f * dt)); }
            b.Play(s.flyState, d < s.approachDistance ? s.flapAnimSpeed * 1.15f : s.glideAnimSpeed);
        }

        // ------------------------------------------------------------------ ground behaviour: idle / peck / hop-walk, alert freeze
        void LandedTick(ASHBird b, ASHBirdSettings s, ASHBirdSettings.BirdType t, float dt)
        {
            var f = b.flock;
            float sc = Mathf.Max(0.3f, t.scale);
            bool alert = f != null && f.alert && Time.time >= b.alertFrom;
            b.alertTarget = alert ? 1f : 0f;
            b.alertW = Mathf.MoveTowards(b.alertW, b.alertTarget, dt * (alert ? 7f : 2.5f));
            float bob = 0f, nod = 0f;
            if (alert)
            {
                // freeze head-up, one eye on the noise
                if (b.gMode != 0) { b.gMode = 0; b.walkPhase = 0f; }
                b.Play(s.idleState, s.alertAnimSpeed);
                var to = f.threat - b.perch; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) b.targetYaw = Quaternion.LookRotation(to).eulerAngles.y + ((b.GetHashCode() & 1) == 0 ? 35f : -35f);
                b.nextAnimSwap = Time.time + Rng(0.3f, 1.2f);
            }
            else if (b.gMode == 2)
            {
                if (!WalkStep(b, f, s, t, dt, sc, out bob, out nod)) EndWalk(b, s, t);
            }
            else if (Time.time >= b.nextAnimSwap) ChooseGround(b, f, s, t, sc);
            GroundPose(b, s, dt, bob, nod);
        }

        void GroundPose(ASHBird b, ASHBirdSettings s, float dt, float bob, float nod)
        {
            var tr = b.transform;
            float y = Mathf.MoveTowardsAngle(tr.eulerAngles.y, b.targetYaw, (b.gMode == 2 ? 320f : 120f) * dt);
            float pitch = -s.alertBodyPitch * b.alertW + nod;
            tr.SetPositionAndRotation(b.perch + Vector3.up * bob, Quaternion.Euler(pitch, y, 0f));
        }

        void ChooseGround(ASHBird b, Flock f, ASHBirdSettings s, ASHBirdSettings.BirdType t, float sc)
        {
            bool canWalk = s.groundWalkEnabled && (b.perchGround || (s.walkOnObjects && b.surface));
            bool strayed = f != null && LandedCentroid(f, b, out var lc) && Flat(b.perch, lc) > s.groupCohesionMeters * Mathf.Sqrt(sc);
            if (canWalk && (strayed || R01() < s.walkChance) && PickWalk(b, f, s, sc))
            {
                b.gMode = 2;
                b.walkPhase = 0f;
                b.Play(HasWalkClip(b, s) ? s.walkState : s.idleState, s.groundAnimSpeed);
                b.nextAnimSwap = Time.time + 8f; // safety: walk ends on arrival or block
                return;
            }
            bool peck = b.perchGround && R01() < t.peckChance;
            SetGroundMode(b, s, peck);
            if (!peck && R01() < 0.35f) b.targetYaw = b.transform.eulerAngles.y + Rng(-70f, 70f);
        }

        void SetGroundMode(ASHBird b, ASHBirdSettings s, bool peck)
        {
            b.gMode = peck ? 1 : 0;
            b.Play(peck ? s.peckState : s.idleState, s.groundAnimSpeed * Rng(0.9f, 1.1f));
            b.nextAnimSwap = Time.time + (peck ? Rng(s.peckSecondsMin, Mathf.Max(s.peckSecondsMin, s.peckSecondsMax))
                                              : Rng(s.idleSecondsMin, Mathf.Max(s.idleSecondsMin, s.idleSecondsMax)));
        }

        void EndWalk(ASHBird b, ASHBirdSettings s, ASHBirdSettings.BirdType t)
        {
            b.walkPhase = 0f;
            SetGroundMode(b, s, b.perchGround && R01() < s.peckAfterWalkChance);
        }

        static bool HasWalkClip(ASHBird b, ASHBirdSettings s)
            => !string.IsNullOrEmpty(s.walkState) && b.animator && b.animator.isActiveAndEnabled && b.animator.HasState(0, Animator.StringToHash(s.walkState));

        bool PickWalk(ASHBird b, Flock f, ASHBirdSettings s, float sc)
        {
            var pos = b.perch;
            var fwd = b.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 dir;
            bool back = false;
            if (f != null && LandedCentroid(f, b, out var lc) && Flat(pos, lc) > s.groupCohesionMeters * Mathf.Sqrt(sc)) { dir = lc - pos; back = true; }
            else dir = Quaternion.Euler(0f, Rng(-110f, 110f), 0f) * fwd;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return false;
            dir.Normalize();
            float len = Rng(s.walkBoutMin, Mathf.Max(s.walkBoutMin, s.walkBoutMax)) * sc;
            var to = pos + dir * len;
            if (!back)
            {
                // stay near where it landed
                float leash = s.maxWanderFromLanding * sc;
                if (!b.perchGround) leash = Mathf.Min(leash, 1.5f * sc);
                var off = to - b.groundHome; off.y = 0f;
                if (off.magnitude > leash) { to = b.groundHome + off.normalized * leash * 0.7f; to.y = pos.y; }
            }
            if (Flat(to, pos) < 0.05f) return false;
            b.walkTo = to;
            var d = to - pos; d.y = 0f;
            b.targetYaw = Quaternion.LookRotation(d.normalized).eulerAngles.y;
            return true;
        }

        bool WalkStep(ASHBird b, Flock f, ASHBirdSettings s, ASHBirdSettings.BirdType t, float dt, float sc, out float bob, out float nod)
        {
            bob = 0f; nod = 0f;
            if (Time.time >= b.nextAnimSwap) return false;
            bool clip = HasWalkClip(b, s);
            float rate = s.hopRate / Mathf.Sqrt(sc);
            float hopLen = s.hopDistance * sc;
            var to = b.walkTo - b.perch; to.y = 0f;
            float d = to.magnitude;
            if (d < 0.03f) return false;
            var dir = to / d;
            b.targetYaw = Quaternion.LookRotation(dir).eulerAngles.y;
            // turn in place first (small hops), then move
            bool facing = Mathf.Abs(Mathf.DeltaAngle(b.transform.eulerAngles.y, b.targetYaw)) < 45f;
            b.walkPhase += rate * dt;
            float ph = b.walkPhase - Mathf.Floor(b.walkPhase);
            float sn = Mathf.Sin(ph * Mathf.PI);
            float pulse = clip ? 1f : 2f * sn * sn; // hop: the body moves in bursts, one per hop
            float step = facing ? Mathf.Min(hopLen * rate * pulse * dt, d) : 0f;
            if (step > 0f)
            {
                var next = b.perch + dir * step;
                float space = s.groundSpacing * sc;
                if (f != null)
                    foreach (var m in f.m)
                    {
                        if (m == b || m.state != ASHBirdState.Landed) continue;
                        float nd = Flat(m.perch, next);
                        if (nd < space && nd < Flat(m.perch, b.perch)) return false;
                    }
                if (!GroundAt(b, next, dir, s, sc, out float gy)) return false;
                next.y = gy;
                b.perch = next;
            }
            if (!clip)
            {
                bob = sn * s.hopHeight * sc * (facing ? 1f : 0.5f);
                nod = Mathf.Sin(ph * Mathf.PI * 2f) * s.hopNodDegrees;
            }
            return true;
        }

        /// <summary>Ground-snaps a walk step. Ground birds stay on terrain / their landing surface; perched birds stay on the same
        /// collider top and never step past its edge.</summary>
        bool GroundAt(ASHBird b, Vector3 p, Vector3 dir, ASHBirdSettings s, float sc, out float y)
        {
            y = p.y;
            float up = s.maxStepUp + 0.25f;
            if (!Physics.Raycast(p + Vector3.up * up, Vector3.down, out var h, up + s.maxStepUp + 0.25f, _mask, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(h.point.y - b.perch.y) > s.maxStepUp) return false;
            if (h.normal.y < Mathf.Cos(s.maxPerchSlope * Mathf.Deg2Rad)) return false;
            bool sameSurface = b.surface && h.collider == b.surface;
            if (b.perchGround)
            {
                if (!sameSurface && !(h.collider is TerrainCollider)) return false;
            }
            else
            {
                if (!sameSurface) return false;
                var ahead = p + dir * Mathf.Max(s.edgeInset, 0.05f * sc);
                if (!Physics.Raycast(ahead + Vector3.up * up, Vector3.down, out var h2, up + s.maxStepUp + 0.25f, _mask, QueryTriggerInteraction.Ignore)
                    || h2.collider != b.surface || Mathf.Abs(h2.point.y - h.point.y) > s.maxStepUp) return false;
            }
            y = h.point.y;
            return true;
        }

        static bool LandedCentroid(Flock f, ASHBird except, out Vector3 c)
        {
            c = Vector3.zero; int n = 0;
            foreach (var m in f.m) { if (m == except || m.state != ASHBirdState.Landed) continue; c += m.perch; n++; }
            if (n == 0) return false;
            c /= n;
            return true;
        }

        static Vector3 Nearest(Flock f, Vector3 p)
        {
            Vector3 best = p; float bd = float.MaxValue;
            foreach (var m in f.m) { float d = (m.transform.position - p).sqrMagnitude; if (d < bd) { bd = d; best = m.transform.position; } }
            return best;
        }

        // ------------------------------------------------------------------ noise: footsteps (player speed / stance), the dog, alert band
        void TrackNoiseSources(float dt)
        {
            dt = Mathf.Max(dt, 0.0001f);
            float k = 1f - Mathf.Exp(-8f * dt);
            if (_player)
            {
                var p = _player.position;
                if (_pHave) { float v = Flat(p, _pPrev) / dt; if (v > 40f) v = _pSpeed; _pSpeed = Mathf.Lerp(_pSpeed, v, k); }
                _pPrev = p; _pHave = true;
            }
            var dog = ASHDogCompanion.Active;
            if (dog && dog.isActiveAndEnabled)
            {
                var p = dog.transform.position;
                if (_dHave) { float v = Flat(p, _dPrev) / dt; if (v > 40f) v = _dSpeed; _dSpeed = Mathf.Lerp(_dSpeed, v, k); }
                _dPrev = p; _dHave = true;
            }
            else { _dHave = false; _dSpeed = 0f; }
        }

        /// <summary>0 = nothing, 1 = in the alert band, 2 = moderate noise inside flush distance, 3 = loud noise inside flush distance.</summary>
        static int NoiseLevel(Flock f, Vector3 src, float flushDist, bool loud, float alertMul)
        {
            float bd = float.MaxValue;
            foreach (var m in f.m)
            {
                if (m.state != ASHBirdState.Landed && m.state != ASHBirdState.Approach) continue;
                float d = (m.transform.position - src).sqrMagnitude;
                if (d < bd) bd = d;
            }
            if (bd == float.MaxValue) return 0;
            if (bd < flushDist * flushDist) return loud ? 3 : 2;
            float a = flushDist * alertMul;
            return bd < a * a ? 1 : 0;
        }

        /// <summary>Returns true if the flock flushed.</summary>
        bool NoiseCheck(Flock f, ASHBirdSettings s, ASHBirdSettings.BirdType t)
        {
            int level = 0;
            Vector3 src = _player.position;
            var br = ASH.Combat.ASHCombatBridge.Player;
            float dist = t.takeOffDistance;
            bool loud = false;
            if (br && br.Crouching) { dist *= s.crouchTakeOffMul; loud = !s.crouchAlertsFirst; }
            else if ((br && br.Sprinting) || _pSpeed > s.loudSpeed) { dist *= s.sprintTakeOffMul; loud = true; }
            else if (_pSpeed < s.stillSpeed) dist *= s.stillTakeOffMul;
            level = NoiseLevel(f, _player.position, dist, loud, s.alertDistanceMul);

            var dog = ASHDogCompanion.Active;
            if (dog && dog.isActiveAndEnabled && s.dogNoiseMeters > 0f)
            {
                float dd = s.dogNoiseMeters;
                bool dl = false;
                if (_dSpeed > s.dogRunSpeed) { dd *= s.dogRunMul; dl = true; }
                else if (_dSpeed < s.stillSpeed) dd *= s.dogStillMul;
                int lv = NoiseLevel(f, dog.transform.position, dd, dl, s.alertDistanceMul);
                if (lv > level) { level = lv; src = dog.transform.position; }
            }

            if (level == 3 || (level >= 2 && !f.landed))
            {
                TakeOff(f, src, true, Nearest(f, src), 1f);
                return true;
            }
            if (level >= 1 && f.landed)
            {
                f.threat = src;
                if (!f.alert)
                {
                    f.alert = true;
                    var first = Nearest(f, src);
                    foreach (var m in f.m)
                        m.alertFrom = Time.time + Mathf.Min(s.startleMaxDelay, Flat(m.transform.position, first) * s.startleSpreadPerMeter * 0.6f + Rng(0f, s.startleJitter));
                }
                f.alertUntil = Time.time + s.alertCalmSeconds;
                f.landedUntil = Mathf.Max(f.landedUntil, Time.time + 2f);
                if (level == 2)
                {
                    if (f.flushAt <= 0f) f.flushAt = Time.time + Rng(s.alertFreezeMin, Mathf.Max(s.alertFreezeMin, s.alertFreezeMax));
                    else if (Time.time >= f.flushAt) { TakeOff(f, src, true, Nearest(f, src), 1f); return true; }
                }
                else f.flushAt = 0f;
            }
            else if (f.alert && Time.time >= f.alertUntil) { f.alert = false; f.flushAt = 0f; }
            return false;
        }

        // ------------------------------------------------------------------ attack birds: climb, dive-bomb the player (or harass the dog), a few passes, then leave
        void TryStartAttack(ASHBird b, ASHBirdSettings s)
        {
            if (!b.aggressive || b.IsDead || Time.time < b.attackReadyAt || R01() > s.attackOnShotChance) return;
            Transform target = null;
            bool dogOk = ASHDogCompanion.IsValidTarget && ASHDogCompanion.Active;
            if (dogOk && R01() < s.dogTargetChance) target = ASHDogCompanion.Active.transform;
            if (!target) target = _player;
            if (!target) return;
            b.atkTarget = target;
            b.alertW = b.alertTarget = 0f; b.gMode = 0;
            b.atkPasses = _rng.Next(Mathf.Max(1, s.passesMin), Mathf.Max(s.passesMin, s.passesMax) + 1);
            b.state = ASHBirdState.Attack;
            b.stateAt = Time.time;
            b.attackReadyAt = Time.time + s.attackCooldown;
            if (b.vel.sqrMagnitude < 1f) b.vel = Vector3.up * s.takeOffUpSpeed;
            BeginClimb(b, s);
            var t = b.type;
            if (t != null && t.calls != null && t.calls.Length > 0) PlayAt(t.calls, b.transform.position, t.callVolume, t.callPitch * 1.08f);
        }

        void BeginClimb(ASHBird b, ASHBirdSettings s)
        {
            b.atkPhase = 0;
            b.atkPhaseAt = Time.time;
            var tp = b.atkTarget ? b.atkTarget.position : b.transform.position;
            var side = Random3(1f); side.y = 0f;
            side = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.forward;
            b.atkPoint = tp + side * s.climbRadius * Rng(0.7f, 1.2f) + Vector3.up * s.climbHeight * Rng(0.8f, 1.2f);
        }

        void AttackTick(ASHBird b, Flock f, ASHBirdSettings s, ASHBirdSettings.BirdType t, float dt)
        {
            var tr = b.transform;
            var tgt = b.atkTarget;
            bool isDog = tgt && ASHDogCompanion.Active && tgt == ASHDogCompanion.Active.transform;
            if (!tgt || !tgt.gameObject.activeInHierarchy || (isDog && !ASHDogCompanion.IsValidTarget) || !s.attackBirdsEnabled) { EndAttack(b, f, s); return; }
            float cruise = t.flySpeed * b.speedMul * s.attackSpeedMul;
            var pos = tr.position;
            Vector3 aim;
            float floorAbove = 0.8f;
            switch (b.atkPhase)
            {
                case 0: // climb to a point above and to the side of the target
                    aim = b.atkPoint;
                    if ((aim - pos).sqrMagnitude < 9f || Time.time - b.atkPhaseAt > 4f)
                    {
                        b.atkPhase = 1; b.atkPhaseAt = Time.time;
                        var sw = s.swoopClips != null && s.swoopClips.Length > 0 ? s.swoopClips : s.takeOffClips;
                        if (sw != null && sw.Length > 0) PlayAt(sw, pos, s.swoopVolume, 1.1f);
                        if (t.calls != null && t.calls.Length > 0) PlayAt(t.calls, pos, t.callVolume, t.callPitch * 1.12f);
                    }
                    break;
                case 1: // dive at head height (dog: back height)
                {
                    aim = tgt.position + Vector3.up * (isDog ? 0.45f : 1.5f);
                    floorAbove = 0.25f;
                    if ((aim - pos).magnitude <= s.hitRadius)
                    {
                        HitTarget(b, tgt, isDog, s);
                        b.atkPasses--;
                        PullUp(b, s);
                    }
                    else if (Time.time - b.atkPhaseAt > s.diveTimeout) { b.atkPasses--; PullUp(b, s); }
                    cruise *= 1.25f;
                    break;
                }
                default: // pull up past the target, then climb again or leave
                    aim = b.atkPoint;
                    if ((aim - pos).sqrMagnitude < 9f || Time.time - b.atkPhaseAt > 2.5f)
                    {
                        if (b.atkPasses > 0) BeginClimb(b, s); else { EndAttack(b, f, s); return; }
                    }
                    break;
            }
            var desired = (aim - pos).normalized * cruise;
            var acc = Vector3.ClampMagnitude((desired - b.vel) * 3f, t.maxAccel * 2.2f);
            b.vel += acc * dt;
            if (b.vel.magnitude > cruise * 1.15f) b.vel = b.vel.normalized * cruise * 1.15f;
            pos += b.vel * dt;
            if (Time.time >= b.groundCheckAt) { b.groundY = GroundY(pos, s); b.groundCheckAt = Time.time + 0.25f; }
            float floor = b.groundY + floorAbove;
            if (pos.y < floor) { pos.y = floor; if (b.vel.y < 0f) b.vel.y = 0f; }
            tr.position = pos;
            Orient(b, acc, s, dt);
            b.Play(s.flyState, b.atkPhase == 1 && b.vel.y < -1f ? s.glideAnimSpeed : s.flapAnimSpeed * 1.3f);
        }

        void PullUp(ASHBird b, ASHBirdSettings s)
        {
            b.atkPhase = 2;
            b.atkPhaseAt = Time.time;
            var v = b.vel; v.y = 0f;
            v = v.sqrMagnitude > 0.01f ? v.normalized : b.transform.forward;
            b.atkPoint = b.transform.position + v * 10f + Vector3.up * s.climbHeight * 0.6f;
        }

        void HitTarget(ASHBird b, Transform tgt, bool isDog, ASHBirdSettings s)
        {
            var hitPos = tgt.position + Vector3.up * (isDog ? 0.45f : 1.5f);
            if (isDog)
            {
                var dog = ASHDogCompanion.Active;
                if (dog && s.dogDamage > 0f) dog.TakeDamage(new vDamage(Mathf.Max(1, Mathf.RoundToInt(s.dogDamage))) { sender = b.transform, hitPosition = hitPos, damageType = "claw" });
            }
            else
            {
                var br = ASH.Combat.ASHCombatBridge.Player;
                if (br && s.playerDamage > 0f) br.HurtFromWorld(s.playerDamage, b.transform, hitPos);
            }
            var t = b.type;
            if (t != null && t.calls != null && t.calls.Length > 0) PlayAt(t.calls, hitPos, t.callVolume, t.callPitch * 1.15f);
        }

        void EndAttack(ASHBird b, Flock f, ASHBirdSettings s)
        {
            b.atkTarget = null;
            b.state = ASHBirdState.Fly;
            b.stateAt = Time.time;
            b.launchAt = Time.time;
            // done harassing: the whole group leaves the area
            if (f != null && (f.landed || f.landing)) TakeOff(f, _player ? _player.position : b.transform.position, true);
            else if (f != null && _player)
            {
                var away = Centroid(f) - _player.position; away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Random3(1f).normalized;
                var dest = (f.spot ? f.spot.transform.position : Centroid(f)) + away * s.fleeDistance * (f.spot ? 0.3f : 1f);
                f.target = new Vector3(dest.x, GroundY(dest, s) + Rng(f.type.altitudeMin, f.type.altitudeMax), dest.z);
            }
        }

        // ------------------------------------------------------------------ death / harvest
        internal void Kill(ASHBird b, Vector3 push)
        {
            if (!b || b.IsDead) return;
            var f = b.flock;
            b.state = ASHBirdState.Falling;
            b.deadAt = Time.time;
            b.flock = null;
            if (f != null) f.m.Remove(b);
            push.y = Mathf.Max(push.y, 0.2f);
            b.vel = b.vel * 0.5f + push.normalized * 2.5f;
            b.SetAnimatorOn(false);
            b.alertW = b.alertTarget = 0f;
            _dead.Add(b);
            var s = S;
            if (s && s.deathClips != null && s.deathClips.Length > 0) PlayAt(s.deathClips, b.transform.position, 0.8f, 1f);
            if (f != null && f.m.Count > 0)
            {
                if (s && s.attackBirdsEnabled) foreach (var m in f.m) if (m.aggressive && m.state != ASHBirdState.Attack) TryStartAttack(m, s);
                TakeOff(f, b.transform.position, true, b.transform.position, s ? s.gunshotCascadeMul : 0.5f);
            }
        }

        void FallTick(ASHBird b, ASHBirdSettings s, float dt)
        {
            var tr = b.transform;
            b.vel += Vector3.down * s.gravity * dt;
            b.vel *= 1f - Mathf.Clamp01(0.6f * dt);
            var pos = tr.position;
            var step = b.vel * dt;
            float len = step.magnitude + 0.25f;
            if (Physics.Raycast(pos + Vector3.up * 0.1f, step.sqrMagnitude > 1e-6f ? step.normalized : Vector3.down, out var hit, len, _mask, QueryTriggerInteraction.Ignore))
            {
                var fwd = tr.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                tr.SetPositionAndRotation(hit.point + hit.normal * 0.04f, Quaternion.LookRotation(fwd.normalized, hit.normal) * Quaternion.Euler(0f, 0f, R01() < 0.5f ? 80f : -80f));
                b.vel = Vector3.zero;
                b.state = ASHBirdState.Dead;
                b.BecomeCorpse();
                return;
            }
            tr.position = pos + step;
            tr.Rotate(new Vector3(220f, 90f, 160f) * dt, Space.Self);
            float gy = GroundY(tr.position, s);
            if (tr.position.y < gy - 0.5f) tr.position = new Vector3(tr.position.x, gy + 0.05f, tr.position.z);
        }

        internal void Harvest(ASHBird b, ASHInventory inv)
        {
            if (!b || b.state != ASHBirdState.Dead || inv == null) return;
            var s = S;
            var t = b.type;
            if (s && t != null)
            {
                if (s.featherItem && t.feathersMax > 0) { int n = ASH.Gameplay.ASHHarvest.Yield(Mathf.Max(1, t.feathersMin), t.feathersMax, ASH.Gameplay.ASHHarvest.HuntingSkill01()); if (n > 0) inv.AddItem(s.featherItem, n); }
                if (s.meatItem && t.meatMax > 0) { int n = ASH.Gameplay.ASHHarvest.Yield(Mathf.Max(1, t.meatMin), t.meatMax, ASH.Gameplay.ASHHarvest.HuntingSkill01()); if (n > 0) inv.AddItem(s.meatItem, n); }
            }
            _dead.Remove(b);
            Release(b);
        }

        // ------------------------------------------------------------------ perches (terrain or the top edge of a collider)
        bool FindPerch(Vector3 area, float radius, ASHBirdSettings.BirdType t, ASHBirdSettings s, out Vector3 perch, out Vector3 fwd, out bool ground)
        {
            perch = area; fwd = Vector3.forward; ground = true;
            int mask = _mask;
            float minNy = Mathf.Cos(s.maxPerchSlope * Mathf.Deg2Rad);
            float sc = Mathf.Max(0.3f, t != null ? t.scale : 1f);
            for (int i = 0; i < Mathf.Max(1, s.landingTries); i++)
            {
                var o = Random.insideUnitCircle * radius;
                var x = new Vector3(area.x + o.x, 0f, area.z + o.y);
                float terrY = TerrainY(x);
                var top = new Vector3(x.x, Mathf.Max(terrY, area.y) + s.maxPerchHeight + 20f, x.z);
                if (!Physics.Raycast(top, Vector3.down, out var hit, s.maxPerchHeight + 80f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < minNy) continue;
                if (hit.point.y - terrY > s.maxPerchHeight) continue;
                if (_player && Flat(hit.point, _player.position) < (t != null ? t.takeOffDistance : 10f) * 1.2f) continue;
                bool isTerrain = hit.collider is TerrainCollider;
                if (!isTerrain && t != null && R01() > t.perchOnObjectsChance) continue;
                var p = hit.point;
                var face = Random3(1f);
                if (!isTerrain)
                {
                    // walk out to the top edge of the object and stand just inside it, facing out
                    var dir = Random3(1f); dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                    var ext = hit.collider.bounds.extents;
                    float maxRun = Mathf.Clamp(Mathf.Max(ext.x, ext.z) * 2f, 0.3f, 8f);
                    Vector3 last = p; bool edge = false;
                    for (float k = 0.15f; k <= maxRun; k += 0.15f)
                    {
                        var q = p + dir * k;
                        if (Physics.Raycast(new Vector3(q.x, p.y + 0.6f, q.z), Vector3.down, out var h2, 1.2f, mask, QueryTriggerInteraction.Ignore)
                            && h2.collider == hit.collider && Mathf.Abs(h2.point.y - last.y) < 0.25f && h2.normal.y >= minNy)
                        { last = h2.point; continue; }
                        edge = true; break;
                    }
                    p = edge ? last - dir * s.edgeInset : last;
                    face = dir;
                }
                if (Physics.CheckSphere(p + Vector3.up * (0.22f * sc + 0.05f), 0.15f * sc, mask, QueryTriggerInteraction.Ignore)) continue;
                perch = p; fwd = face; ground = isTerrain;
                return true;
            }
            return false;
        }

        float GroundY(Vector3 p, ASHBirdSettings s)
        {
            float ty = TerrainY(p);
            if (Physics.Raycast(new Vector3(p.x, ty + 120f, p.z), Vector3.down, out var hit, 240f, _mask, QueryTriggerInteraction.Ignore))
                return Mathf.Max(ty, hit.point.y);
            return ty;
        }

        static float TerrainY(Vector3 p)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                if (!t || !t.terrainData) continue;
                var o = t.GetPosition(); var sz = t.terrainData.size;
                if (p.x >= o.x && p.z >= o.z && p.x <= o.x + sz.x && p.z <= o.z + sz.z) return t.SampleHeight(p) + o.y;
            }
            var a = Terrain.activeTerrain;
            return a ? a.SampleHeight(p) + a.GetPosition().y : p.y - 10f;
        }

        // ------------------------------------------------------------------ audio (own small voice pool so calls never steal weapon sounds)
        void PlayAt(AudioClip[] clips, Vector3 pos, float vol, float pitch)
        {
            var clip = ASHSfxPick(clips);
            if (!clip) return;
            var s = S;
            int voices = Mathf.Clamp(s ? s.audioVoices : 6, 1, 16);
            if (_voices == null || _voices.Length != voices)
            {
                if (_voices != null) foreach (var v in _voices) if (v) Destroy(v.gameObject);
                _voices = new AudioSource[voices];
            }
            int i = _voice; _voice = (_voice + 1) % voices;
            if (!_voices[i])
            {
                var go = new GameObject("ASH_BirdVoice_" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 3f; src.dopplerLevel = 0f;
                _voices[i] = src;
            }
            var a = _voices[i];
            a.transform.position = pos;
            a.maxDistance = s ? s.audioMaxDistance : 70f;
            a.pitch = pitch * Rng(0.94f, 1.06f);
            a.PlayOneShot(clip, Mathf.Clamp01(vol * ASH.Core.ASHGameSettings.CachedSfx));
        }

        AudioClip ASHSfxPick(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            for (int tries = 0; tries < 4; tries++) { var c = clips[_rng.Next(clips.Length)]; if (c) return c; }
            foreach (var c in clips) if (c) return c;
            return null;
        }

        // ------------------------------------------------------------------ helpers
        static Vector3 Centroid(Flock f)
        {
            if (f.m.Count == 0) return f.home;
            var c = Vector3.zero;
            foreach (var b in f.m) c += b.transform.position;
            return c / f.m.Count;
        }

        static Vector3 AvgVel(Flock f)
        {
            var v = Vector3.zero;
            foreach (var b in f.m) v += b.vel;
            return f.m.Count > 0 ? v / f.m.Count : v;
        }

        Vector3 Random3(float r) => new Vector3(Rng(-r, r), 0f, Rng(-r, r));
        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    }
}
