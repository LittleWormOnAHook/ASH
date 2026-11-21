using System.Collections.Generic;
using ASH.Inventory;
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
        }

        readonly List<ASHBird> _all = new List<ASHBird>();
        readonly Stack<ASHBird> _pool = new Stack<ASHBird>();
        readonly List<Flock> _flocks = new List<Flock>();
        readonly List<ASHBird> _dead = new List<ASHBird>();
        System.Random _rng;
        Transform _player;
        Camera _cam;
        float _spawnAt, _findAt, _gunSeen = -999f;
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
            if (!s || !s.birdsEnabled || !s.autoSpawn || !s.birdPrefab) return;
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

            GunshotCheck(s);
            if (Time.time >= _spawnAt) { _spawnAt = Time.time + s.spawnInterval; TrySpawn(s); }

            for (int i = _flocks.Count - 1; i >= 0; i--)
            {
                var f = _flocks[i];
                f.m.RemoveAll(b => !b || b.IsDead || b.state == ASHBirdState.Pooled);
                if (f.m.Count == 0) { _flocks.RemoveAt(i); continue; }
                if (Flat(Centroid(f), _player.position) > s.despawnDistance) { foreach (var b in f.m) Release(b); _flocks.RemoveAt(i); continue; }
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
            foreach (var f in _flocks)
                if ((f.landed || f.landing) && Flat(Centroid(f), at) <= range) TakeOff(f, at, true);
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
            var f = new Flock { type = t, home = c, callAt = Time.time + Rng(1f, 6f) };
            bool landed = R01() < s.startLandedChance;

            for (int i = 0; i < n; i++)
            {
                var b = Get(s);
                if (!b) break;
                b.Apply(t, _rng);
                b.flock = f;
                f.m.Add(b);
                if (landed && FindPerch(c, s.landingSearchRadius * 0.6f, t, s, out var p, out var fwd, out bool ground))
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
            if (f.m.Count == 0) return;
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
            _flocks.Add(f);
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
        }

        // ------------------------------------------------------------------ flock brain
        void FlockTick(Flock f, ASHBirdSettings s)
        {
            var t = f.type;
            if (f.landed || f.landing)
            {
                if (Time.time >= f.scareAt)
                {
                    f.scareAt = Time.time + 0.15f;
                    float dist = t.takeOffDistance;
                    var b = ASH.Combat.ASHCombatBridge.Player;
                    if (b) { if (b.Crouching) dist *= s.crouchTakeOffMul; else if (b.Sprinting) dist *= s.sprintTakeOffMul; }
                    foreach (var m in f.m)
                        if ((m.transform.position - _player.position).sqrMagnitude < dist * dist) { TakeOff(f, _player.position, true); return; }
                }
                if (f.landed && Time.time >= f.landedUntil) { TakeOff(f, Vector3.zero, false); return; }
                if (f.landing)
                {
                    bool all = true, any = false;
                    foreach (var m in f.m) { if (m.state == ASHBirdState.Landed) any = true; else all = false; }
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
                    var w = f.home + Random3(s.wanderRadius);
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
            var area = c + (v.sqrMagnitude > 0.1f ? v.normalized : Random3(1f).normalized) * Rng(10f, 25f);
            // don't pick a landing area right next to the player
            if (_player && Flat(area, _player.position) < f.type.takeOffDistance * 1.8f)
                area = _player.position + (area - _player.position).normalized * f.type.takeOffDistance * 2.2f;
            int ok = 0;
            foreach (var m in f.m) if (AssignPerch(m, area, s.landingSearchRadius, s)) ok++;
            if (ok == 0)
            {
                foreach (var m in f.m) m.state = ASHBirdState.Fly;
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

        void TakeOff(Flock f, Vector3 threat, bool scared)
        {
            var s = S;
            var t = f.type;
            var c = Centroid(f);
            Vector3 away = threat.sqrMagnitude > 0.01f ? c - threat : Random3(1f);
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            var dest = c + away * (scared ? s.fleeDistance : Rng(s.fleeDistance * 0.4f, s.fleeDistance));
            f.target = new Vector3(dest.x, GroundY(dest, s) + Rng(t.altitudeMin, t.altitudeMax), dest.z);
            f.home = f.target;
            f.landed = false; f.landing = false;
            f.flyUntil = Time.time + Rng(s.flyMinSeconds, Mathf.Max(s.flyMinSeconds, s.flyMaxSeconds));
            bool any = false;
            foreach (var b in f.m)
            {
                if (b.state != ASHBirdState.Landed && b.state != ASHBirdState.Approach) { b.state = ASHBirdState.Fly; continue; }
                bool wasLanded = b.state == ASHBirdState.Landed;
                any |= wasLanded;
                b.state = ASHBirdState.Fly;
                b.stateAt = Time.time;
                b.launchAt = wasLanded ? Time.time + (scared ? Rng(0f, 0.3f) : Rng(0f, 1.2f)) : Time.time;
                var side = (away + Random3(0.6f)); side.y = 0f;
                if (wasLanded) b.vel = Vector3.up * s.takeOffUpSpeed * Rng(0.8f, 1.2f) + side.normalized * t.flySpeed * 0.35f;
                b.gliding = false;
                b.nextAnimSwap = Time.time + Rng(1.2f, 2.2f);
                b.groundCheckAt = 0f;
            }
            if (any && s.takeOffClips != null && s.takeOffClips.Length > 0)
                PlayAt(s.takeOffClips, c, s.takeOffVolume * Mathf.Clamp(0.6f + f.m.Count * 0.12f, 0.6f, 1.3f), 1f / Mathf.Max(0.4f, Mathf.Sqrt(t.scale)));
            if (scared && t.calls != null && t.calls.Length > 0) { PlayAt(t.calls, c, t.callVolume, t.callPitch * 1.05f); f.callAt = Time.time + Rng(1.5f, 4f); }
        }

        // ------------------------------------------------------------------ per-bird motion
        void BirdTick(ASHBird b, Flock f, ASHBirdSettings s, float dt)
        {
            var t = b.type ?? f.type;
            switch (b.state)
            {
                case ASHBirdState.Fly:
                    if (Time.time < b.launchAt) { b.Play(s.idleState, s.groundAnimSpeed); return; }
                    Boids(b, f, s, t, dt, f.target);
                    FlapOrGlide(b, s, t);
                    return;
                case ASHBirdState.Approach:
                    ApproachTick(b, f, s, t, dt);
                    return;
                case ASHBirdState.Landed:
                    LandedTick(b, s, t, dt);
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

        void LandedTick(ASHBird b, ASHBirdSettings s, ASHBirdSettings.BirdType t, float dt)
        {
            var tr = b.transform;
            if (Time.time >= b.nextAnimSwap)
            {
                bool peck = b.perchGround && R01() < t.peckChance;
                b.Play(peck ? s.peckState : s.idleState, s.groundAnimSpeed * Rng(0.9f, 1.1f));
                b.nextAnimSwap = Time.time + (peck ? Rng(2.2f, 5f) : Rng(2f, 6f));
                if (!peck && R01() < 0.35f) b.targetYaw = tr.eulerAngles.y + Rng(-70f, 70f);
            }
            var e = tr.eulerAngles;
            float y = Mathf.MoveTowardsAngle(e.y, b.targetYaw, 120f * dt);
            if (Mathf.Abs(Mathf.DeltaAngle(e.y, y)) > 0.01f) tr.rotation = Quaternion.Euler(0f, y, 0f);
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
            _dead.Add(b);
            var s = S;
            if (s && s.deathClips != null && s.deathClips.Length > 0) PlayAt(s.deathClips, b.transform.position, 0.8f, 1f);
            if (f != null && f.m.Count > 0) TakeOff(f, b.transform.position, true);
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
