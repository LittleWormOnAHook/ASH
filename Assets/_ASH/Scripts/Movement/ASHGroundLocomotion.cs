using UnityEngine;

namespace ASH.Movement
{
    /// <summary>
    /// Same walk / turn / run motor as the ASH dog: code movement (no root motion), speed acceleration,
    /// turn-in-place vs arcing turns, whisker steering, capsule collide-and-slide, ground alignment,
    /// depenetration, and measured ground speed for a speed-driven anim blend + playback-rate foot sync.
    /// Wildlife uses this with each species' own Malbers clips. The dog keeps its own copy of this logic
    /// so its behaviour is unchanged.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHGroundLocomotion : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Tune
        {
            public float walkSpeed = 2.2f, trotSpeed = 4f, runSpeed = 7f;
            public float walkClipSpeed = 1.0f, trotClipSpeed = 2.0f, runClipSpeed = 4.0f;
            public float turnSpeed = 300f, turnSmoothTime = 0.18f, acceleration = 6f;
            public float turnInPlaceAngle = 100f, turnInPlaceDone = 30f, arcTurnSlow = 0.55f, turnBlendFullRate = 160f;
            public float animSpeedScale = 1f, animRateMin = 0.6f, animRateMax = 2.6f;
            public float maxSlope = 45f, maxStepUp = 0.45f;
            public float bodyRadius = 0.28f, bodyLength = 0.7f, bodyHeight = 0.5f;
            public float whiskerLength = 1.3f, whiskerAngle = 35f;
            public bool depenetrate = true;
            public LayerMask environmentLayers = ~((1 << 2) | (1 << 5));
            [Tooltip("Animator 'Speed' parameter = ground speed x this (blend thresholds built for other gait speeds).")]
            public float speedParamScale = 1f;
            [Tooltip("Vertical ground-follow smoothing (seconds); 0 = snap.")]
            public float groundSmoothTime = 0f;
            [Tooltip("Ground probe skips creatures (same layer as this body, the Player tag, non-static rigidbodies).")]
            public bool groundIgnoreCreatures;

            // t560/t561 avoidance (defaults = off, wildlife turns them on through ASH_WildlifeSettings)
            [Tooltip("Stand only on terrain, or on colliders at most Max Above Terrain over it (roads, low logs). Taller things (large rocks, buildings, vehicles) are obstacles.")]
            public bool terrainOnlyGround;
            public float maxAboveTerrain = 0.6f;
            [Tooltip("Standing this high above the terrain (on a rock...) = hop down to the nearest clear terrain spot.")]
            public float offRockHeight = 0.9f;
            public float hopSeconds = 0.55f, hopHeight = 0.6f;
            [Tooltip("Stuck this long = back up and turn away (0 = off).")]
            public float stuckBackupSeconds;
            public float backupSeconds = 0.6f, backupSpeedMul = 0.5f;
            [Tooltip("Keep this much space from other animals (0 = off).")]
            public float separationRadius;
            public float separationWeight = 0.8f;
            [Tooltip("Treat a drop deeper than this ahead as a cliff (0 = off).")]
            public float maxDrop;
            [Tooltip("Look-ahead multiplier for whiskers and the blocked check.")]
            public float lookAheadMul = 1f;
            [System.NonSerialized] public System.Func<Vector3, bool> isWater;
        }

        static readonly System.Collections.Generic.List<ASHGroundLocomotion> All = new System.Collections.Generic.List<ASHGroundLocomotion>();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetAll() => All.Clear();

        /// <summary>Runtime look-ahead scale set by the AI (shorter while charging so it still commits to the target).</summary>
        public float LookAheadScale { get; set; } = 1f;
        /// <summary>True while hopping down off a rock.</summary>
        public bool Hopping => _hopT >= 0f;
        float LA => Mathf.Max(0.2f, tune.lookAheadMul * LookAheadScale);

        float _hopT = -1f, _backupUntil, _backupCooldown, _rockCheckAt;
        Vector3 _hopFrom, _hopTo;

        public Tune tune = new Tune();
        public Animator animator;

        static readonly int HSpeed = Animator.StringToHash("Speed");
        static readonly int HTurn = Animator.StringToHash("Turn");
        static readonly RaycastHit[] Hits = new RaycastHit[24];
        static readonly Collider[] Pen = new Collider[32];

        float _speed, _yaw, _yawVel, _animYaw, _turnRate, _actualSpeed, _animRate = 1f, _stuckT, _detourUntil;
        bool _turningInPlace, _moving, _teleported;
        Vector3 _up = Vector3.up, _lastPos, _detour;
        CapsuleCollider _body;
        Transform _ignoreRoot;

        public float Speed => _speed;
        public float ActualSpeed => _actualSpeed;
        public float TurnRate => _turnRate;
        public bool TurningInPlace => _turningInPlace;
        public bool Moving => _moving;
        public float StuckTime => _stuckT;
        public float Yaw => _yaw;

        public void Init(Transform ignoreRoot, Animator anim, Tune t)
        {
            if (t != null) tune = t;
            animator = anim ? anim : GetComponentInChildren<Animator>();
            _ignoreRoot = ignoreRoot ? ignoreRoot : transform;
            _yaw = transform.eulerAngles.y;
            _animYaw = _yaw;
            _up = Vector3.up;
            _lastPos = transform.position;
            if (animator) animator.applyRootMotion = false;
        }

        public void TickMeasure(float dt)
        {
            if (dt <= 1e-5f) return;
            var pos = transform.position;
            float v = _teleported ? 0f : Mathf.Min(Flat(pos, _lastPos) / dt, tune.runSpeed * 1.5f);
            _teleported = false;
            _lastPos = pos;
            _actualSpeed = Mathf.Lerp(_actualSpeed, v, 1f - Mathf.Exp(-dt * 12f));
        }

        public bool MoveTo(Vector3 target, float want, float arrive)
        {
            float dt = Time.deltaTime;
            if (TickHop(dt)) return false;
            var to = target - transform.position; to.y = 0f; float d = to.magnitude;
            if (d <= arrive) { _turningInPlace = false; Stop(); return true; }
            var dir = to / d;
            if (d < 1.2f && _speed < 0.3f && !_turningInPlace && Mathf.Abs(Mathf.DeltaAngle(_yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg)) > 100f)
            { Stop(); return true; }
            // t560: stuck -> back up a little and turn away
            if (tune.stuckBackupSeconds > 0f && _stuckT > tune.stuckBackupSeconds && Time.time > _backupCooldown && Time.time >= _backupUntil)
            {
                _backupUntil = Time.time + tune.backupSeconds;
                _backupCooldown = _backupUntil + 1.5f;
                float side = Random.value < 0.5f ? 1f : -1f;
                var alt = Quaternion.Euler(0f, side * Random.Range(100f, 140f), 0f) * dir;
                if (Blocked(alt, 1.2f)) alt = Quaternion.Euler(0f, -side * Random.Range(100f, 140f), 0f) * dir;
                _detour = alt; _detourUntil = _backupUntil + 1f;
            }
            if (Time.time < _backupUntil)
            {
                _turningInPlace = false;
                _speed = Mathf.MoveTowards(_speed, tune.walkSpeed * tune.backupSpeedMul, tune.acceleration * dt);
                var back = -(Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward);
                Step(back, dt);
                Face(transform.position + _detour, dt);
                _stuckT = Mathf.Max(0f, _stuckT - dt);
                return false;
            }
            if (Time.time < _detourUntil) dir = _detour;
            else if (Blocked(dir, Mathf.Min(d, 0.9f * LA)))
            {
                bool found = false;
                foreach (float a in new[] { 40f, -40f, 75f, -75f, 110f, -110f, 150f, -150f })
                {
                    var alt = Quaternion.Euler(0f, a, 0f) * dir;
                    if (!Blocked(alt, 1.2f)) { dir = alt; _detour = alt; _detourUntil = Time.time + 0.7f; found = true; break; }
                }
                if (!found) { _stuckT += dt; _speed = 0f; _turningInPlace = false; Face(transform.position + dir, dt); Depenetrate(); return false; }
            }
            dir = Separate(Whiskers(dir));
            float yawTo = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; float diff = Mathf.DeltaAngle(_yaw, yawTo);
            if (!_turningInPlace && Mathf.Abs(diff) > tune.turnInPlaceAngle && _speed < tune.trotSpeed) _turningInPlace = true;
            else if (_turningInPlace && Mathf.Abs(diff) < tune.turnInPlaceDone) _turningInPlace = false;
            _yaw = Mathf.SmoothDampAngle(_yaw, yawTo, ref _yawVel, tune.turnSmoothTime, tune.turnSpeed * (_turningInPlace ? 0.7f : 1f), dt);
            float turnSlow = _turningInPlace ? 0f : Mathf.Lerp(1f, tune.arcTurnSlow, Mathf.Clamp01(Mathf.Abs(diff) / 90f));
            want = Mathf.Min(want, d / Mathf.Max(0.2f, dt * 8f) + tune.walkSpeed * 0.5f);
            _speed = Mathf.MoveTowards(_speed, want * turnSlow, tune.acceleration * dt);
            var before = transform.position;
            Step(Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward, dt);
            float moved = Flat(before, transform.position);
            if (!_turningInPlace && want > 0.5f && moved < want * dt * 0.2f) _stuckT += dt; else _stuckT = Mathf.Max(0f, _stuckT - dt * 2f);
            return false;
        }

        public void Stop()
        {
            float dt = Time.deltaTime;
            if (TickHop(dt)) return;
            _speed = Mathf.MoveTowards(_speed, 0f, tune.acceleration * 2f * dt);
            if (_speed > 0.01f) Step(transform.forward, dt);
            else { _moving = false; Depenetrate(); }
        }

        public void Face(Vector3 p, float dt)
        {
            var to = p - transform.position; to.y = 0f; if (to.sqrMagnitude < 0.01f) return;
            float yawTo = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _yaw = Mathf.SmoothDampAngle(_yaw, yawTo, ref _yawVel, tune.turnSmoothTime, tune.turnSpeed * 0.6f, dt);
            ApplyRotation();
        }

        /// <summary>Turns toward p only past a 35 degree deadzone, then until nearly facing.</summary>
        bool _facing;
        public void FaceLoose(Vector3 p, float dt)
        {
            var to = p - transform.position; to.y = 0f; if (to.sqrMagnitude < 0.25f) return;
            float diff = Mathf.Abs(Mathf.DeltaAngle(_yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg));
            if (!_facing && diff > 35f) _facing = true;
            if (_facing) { Face(p, dt); if (diff < 6f) { _facing = false; _yawVel = 0f; } }
        }

        public void MarkTeleported()
        {
            _teleported = true; _lastPos = transform.position; _speed = 0f; _stuckT = 0f;
            _turningInPlace = false; _yawVel = 0f; _yaw = transform.eulerAngles.y; _animYaw = _yaw; _up = Vector3.up;
        }

        public void Animate(float dt)
        {
            if (!animator || !animator.runtimeAnimatorController || dt <= 1e-5f) return;
            float rate = Mathf.DeltaAngle(_animYaw, _yaw) / dt; _animYaw = _yaw;
            _turnRate = Mathf.Lerp(_turnRate, rate, 1f - Mathf.Exp(-dt * 10f));
            float turn = Mathf.Clamp(_turnRate / Mathf.Max(1f, tune.turnBlendFullRate), -1f, 1f);
            float v = _actualSpeed, play = 1f;
            float gw = Mathf.Max(0.1f, tune.walkSpeed), gt = Mathf.Max(gw + 0.01f, tune.trotSpeed), gr = Mathf.Max(gt + 0.01f, tune.runSpeed);
            float cw = Mathf.Max(0.05f, tune.walkClipSpeed), ct = Mathf.Max(0.05f, tune.trotClipSpeed), cr = Mathf.Max(0.05f, tune.runClipSpeed);
            if (v > 0.05f)
            {
                float nat = v <= gw ? cw : v <= gt ? Mathf.Lerp(cw, ct, (v - gw) / (gt - gw)) : v <= gr ? Mathf.Lerp(ct, cr, (v - gt) / (gr - gt)) : cr;
                float spd = v <= gw ? gw : v;
                play = Mathf.Clamp(spd / nat, tune.animRateMin, tune.animRateMax);
            }
            _animRate = Mathf.Lerp(_animRate, play, 1f - Mathf.Exp(-dt * 8f));
            animator.speed = _animRate * tune.animSpeedScale;
            if (HasParam(HSpeed)) animator.SetFloat(HSpeed, v * Mathf.Max(0.05f, tune.speedParamScale), 0.08f, dt);
            if (HasParam(HTurn)) animator.SetFloat(HTurn, turn, 0.12f, dt);
        }

        bool HasParam(int hash)
        {
            if (!animator) return false;
            var ps = animator.parameters;
            for (int i = 0; i < ps.Length; i++) if (ps[i].nameHash == hash) return true;
            return false;
        }

        bool Ignore(Collider c)
        {
            if (!c || c.isTrigger) return true;
            var t = c.transform;
            if (t.IsChildOf(transform) || (_ignoreRoot && t.IsChildOf(_ignoreRoot))) return true;
            return c.attachedRigidbody && !c.attachedRigidbody.isKinematic && c.attachedRigidbody.mass < 20f;
        }

        bool Creature(Collider c)
        {
            if (!c) return false;
            if (c.gameObject.layer == gameObject.layer && gameObject.layer != 0) return true;
            if (c.CompareTag("Player")) return true;
            var rb = c.attachedRigidbody;
            return rb && !rb.isKinematic;
        }

        bool Ground(Vector3 from, float dist, out Vector3 point, out Vector3 normal)
        {
            point = from; normal = Vector3.up;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, dist, tune.environmentLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool ok = false;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i]; if (Ignore(h.collider) || (tune.groundIgnoreCreatures && Creature(h.collider))) continue;
                if (tune.terrainOnlyGround && !(h.collider is TerrainCollider) && TerrainY(h.point, out var ty) && h.point.y - ty > tune.maxAboveTerrain) continue; // t561: not on rock tops
                if (h.distance < best) { best = h.distance; point = h.point; normal = h.normal; ok = true; }
            }
            return ok;
        }

        void EnsureBody()
        {
            if (_body) return;
            var go = transform.Find("ASH_LocoBody");
            if (!go)
            {
                var g = new GameObject("ASH_LocoBody");
                g.transform.SetParent(transform, false);
                g.layer = gameObject.layer;
                go = g.transform;
            }
            float ls = Mathf.Max(1e-4f, transform.lossyScale.x);
            go.localScale = Vector3.one / ls;
            _body = go.GetComponent<CapsuleCollider>();
            if (!_body) _body = go.gameObject.AddComponent<CapsuleCollider>();
            _body.isTrigger = true;
            _body.direction = 2;
        }

        void SyncBody()
        {
            EnsureBody();
            _body.radius = tune.bodyRadius;
            _body.height = tune.bodyLength + 2f * tune.bodyRadius;
            _body.center = new Vector3(0f, tune.bodyHeight, 0f);
        }

        void Ends(Vector3 pos, Vector3 fwd, out Vector3 a, out Vector3 b)
        {
            var c = pos + Vector3.up * tune.bodyHeight; var h = fwd * (tune.bodyLength * 0.5f); a = c + h; b = c - h;
        }

        bool Sweep(Vector3 pos, Vector3 fwd, Vector3 dir, float dist, out RaycastHit hit)
        {
            hit = default; Ends(pos, fwd, out var a, out var b);
            int n = Physics.CapsuleCastNonAlloc(a, b, tune.bodyRadius * 0.95f, dir, Hits, dist, tune.environmentLayers, QueryTriggerInteraction.Ignore);
            float cos = Mathf.Cos(tune.maxSlope * Mathf.Deg2Rad), best = float.MaxValue; bool ok = false;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i]; if (!IsObstacle(h, cos)) continue;
                if (h.distance < best) { best = h.distance; hit = h; ok = true; }
            }
            return ok;
        }

        bool Blocked(Vector3 dir, float dist) => Sweep(transform.position, dir, dir, dist, out _) || Hazard(dir);

        /// <summary>Wall-like hit, or (terrain-only ground) any collider that rises more than maxAboveTerrain over the terrain: large rocks, buildings, vehicles, fences.</summary>
        bool IsObstacle(RaycastHit h, float cos)
        {
            if (h.distance <= 0f || Ignore(h.collider)) return false;
            if (tune.terrainOnlyGround && !(h.collider is TerrainCollider) && TerrainY(h.point, out var ty) && h.collider.bounds.max.y - ty > tune.maxAboveTerrain) return true;
            return h.normal.y < cos;
        }

        /// <summary>Cliff edge (drop deeper than maxDrop or no ground) or water just ahead.</summary>
        bool Hazard(Vector3 dir)
        {
            if (tune.maxDrop <= 0f && tune.isWater == null) return false;
            var pos = transform.position;
            var ahead = pos + dir * Mathf.Max(tune.bodyLength * 0.5f + 0.6f, tune.whiskerLength * 0.8f * LA);
            if (tune.isWater != null)
            {
                bool wet = false;
                try { wet = tune.isWater(ahead); } catch { }
                if (wet) return true;
            }
            if (tune.maxDrop > 0f)
            {
                float up = tune.maxStepUp + 0.5f;
                if (!Ground(ahead + Vector3.up * up, up + tune.maxDrop + 0.5f, out var g, out _)) return true;
                if (pos.y - g.y > tune.maxDrop) return true;
            }
            return false;
        }

        /// <summary>Steer away from nearby animals (same locomotor).</summary>
        Vector3 Separate(Vector3 dir)
        {
            if (tune.separationRadius <= 0f) return dir;
            var pos = transform.position; var sep = Vector3.zero;
            for (int i = 0; i < All.Count; i++)
            {
                var o = All[i]; if (!o || o == this) continue;
                var d = pos - o.transform.position; d.y = 0f;
                float R = tune.bodyRadius + o.tune.bodyRadius + tune.separationRadius, m = d.magnitude;
                if (m < R && m > 1e-3f) sep += d / m * (1f - m / R);
            }
            if (sep.sqrMagnitude < 1e-4f) return dir;
            var r = dir + sep * tune.separationWeight; r.y = 0f;
            return r.sqrMagnitude > 1e-4f ? r.normalized : dir;
        }

        static bool TerrainY(Vector3 p, out float y)
        {
            var ts = Terrain.activeTerrains;
            for (int i = 0; i < ts.Length; i++)
            {
                var t = ts[i]; if (!t || !t.terrainData) continue;
                var o = t.GetPosition(); var sz = t.terrainData.size;
                if (p.x >= o.x && p.z >= o.z && p.x <= o.x + sz.x && p.z <= o.z + sz.z) { y = t.SampleHeight(p) + o.y; return true; }
            }
            y = 0f; return false;
        }

        // ------------------------------------------------------------------ t561: off-rock hop
        /// <summary>Runs the hop if one is active, or starts one when the animal stands on something tall. True = hop in progress (skip normal movement).</summary>
        bool TickHop(float dt)
        {
            if (_hopT < 0f)
            {
                if (!tune.terrainOnlyGround || Time.time < _rockCheckAt) return false;
                _rockCheckAt = Time.time + 0.25f;
                var p = transform.position;
                if (!TerrainY(p, out var ty) || p.y - ty <= tune.offRockHeight) return false;
                _hopFrom = p; _hopTo = ClearTerrainPoint(p); _hopT = 0f; _speed = 0f; _turningInPlace = false;
            }
            _hopT += dt / Mathf.Max(0.1f, tune.hopSeconds);
            float k = Mathf.Clamp01(_hopT);
            var pos = Vector3.Lerp(_hopFrom, _hopTo, k) + Vector3.up * (4f * tune.hopHeight * k * (1f - k));
            transform.position = pos;
            var flat = _hopTo - _hopFrom; flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) { _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 720f * dt); ApplyRotation(); }
            _moving = true;
            if (k >= 1f) { _hopT = -1f; _up = Vector3.up; _teleported = true; _lastPos = transform.position; _stuckT = 0f; _rockCheckAt = Time.time + 1f; }
            return true;
        }

        /// <summary>Nearest terrain spot around p with room for the body (no rock / building there, not water).</summary>
        Vector3 ClearTerrainPoint(Vector3 p)
        {
            var fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            foreach (float r in new[] { 1.2f, 2f, 3f, 4.5f, 6.5f, 9f, 13f })
                for (int i = 0; i < 12; i++)
                {
                    var dir = Quaternion.Euler(0f, (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 30f, 0f) * fwd;
                    var q = p + dir * (r + tune.bodyRadius);
                    if (!TerrainY(q, out var ty)) continue;
                    q.y = ty;
                    if (tune.isWater != null) { bool wet = false; try { wet = tune.isWater(q); } catch { } if (wet) continue; }
                    if (Occupied(q, dir)) continue;
                    return q;
                }
            TerrainY(p, out var y0); p.y = y0; return p;
        }

        bool Occupied(Vector3 q, Vector3 fwd)
        {
            Ends(q + Vector3.up * 0.05f, fwd, out var a, out var b);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, tune.bodyRadius * 1.15f, Pen, tune.environmentLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) { var c = Pen[i]; if (!Ignore(c) && !(c is TerrainCollider)) return true; }
            return false;
        }

        Vector3 Whiskers(Vector3 dir)
        {
            var o = transform.position + Vector3.up * tune.bodyHeight; float len = tune.whiskerLength * LA, r = tune.bodyRadius * 0.6f;
            float L = Feel(o, Quaternion.Euler(0f, -tune.whiskerAngle, 0f) * dir, len, r);
            float R = Feel(o, Quaternion.Euler(0f, tune.whiskerAngle, 0f) * dir, len, r);
            if (L >= len && R >= len) return dir;
            float push = (len - Mathf.Min(L, R)) / len; float sign = L < R ? 1f : -1f;
            return Quaternion.Euler(0f, sign * tune.whiskerAngle * push, 0f) * dir;
        }

        float Feel(Vector3 o, Vector3 d, float len, float r)
        {
            int n = Physics.SphereCastNonAlloc(o, r, d, Hits, len, tune.environmentLayers, QueryTriggerInteraction.Ignore);
            float cos = Mathf.Cos(tune.maxSlope * Mathf.Deg2Rad), best = len;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i]; if (!IsObstacle(h, cos)) continue;
                best = Mathf.Min(best, h.distance);
            }
            return best;
        }

        void Step(Vector3 fwd, float dt)
        {
            _moving = _speed > 0.05f;
            var pos = transform.position; var delta = fwd * (_speed * dt); float len = delta.magnitude; const float skin = 0.03f;
            var bodyFwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            if (len > 1e-5f)
            {
                var dir = delta / len;
                if (Sweep(pos, bodyFwd, dir, len + skin, out var hit))
                {
                    float go = Mathf.Max(0f, hit.distance - skin); pos += dir * go;
                    var n = hit.normal; n.y = 0f; n.Normalize();
                    var slide = Vector3.ProjectOnPlane(delta - dir * go, n); slide.y = 0f; float sl = slide.magnitude;
                    if (sl > 1e-5f)
                    {
                        if (Sweep(pos, bodyFwd, slide / sl, sl + skin, out var h2)) sl = Mathf.Max(0f, h2.distance - skin);
                        pos += slide.normalized * sl;
                    }
                    if (go < len * 0.3f) _stuckT += dt * 0.5f;
                }
                else pos += delta;
            }
            float up = tune.maxStepUp;
            if (Ground(pos + Vector3.up * (up + 0.05f), up + 1.6f, out var g, out var nrm) && Vector3.Angle(nrm, Vector3.up) <= tune.maxSlope + 10f && g.y - transform.position.y <= up)
            {
                float cy = transform.position.y, ty = g.y;
                if (tune.groundSmoothTime > 0.001f && Mathf.Abs(ty - cy) < 0.6f)
                {
                    ty = Mathf.Lerp(cy, ty, 1f - Mathf.Exp(-dt / tune.groundSmoothTime));
                    if (g.y - ty > 0.08f) ty = g.y - 0.08f; // never sink visibly into rising ground
                }
                pos.y = ty; transform.position = pos;
                _up = Vector3.Slerp(_up, Vector3.Slerp(Vector3.up, nrm, 0.6f), 1f - Mathf.Exp(-dt * 8f));
            }
            else { _speed = 0f; _stuckT += dt; }
            ApplyRotation();
            Depenetrate();
        }

        void Depenetrate()
        {
            if (!tune.depenetrate) return;
            SyncBody();
            Ends(transform.position, Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward, out var a, out var b);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, tune.bodyRadius, Pen, tune.environmentLayers, QueryTriggerInteraction.Ignore);
            var push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var c = Pen[i]; if (Ignore(c)) continue;
                if (Physics.ComputePenetration(_body, _body.transform.position, _body.transform.rotation, c, c.transform.position, c.transform.rotation, out var d, out var dist))
                { if (d.y > 0.7f) continue; d.y = 0f; push += d.normalized * dist; }
            }
            if (push.sqrMagnitude > 1e-6f) transform.position += Vector3.ClampMagnitude(push, 0.25f);
        }

        void ApplyRotation()
        {
            var f = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            var proj = Vector3.ProjectOnPlane(f, _up);
            if (proj.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(proj.normalized, _up);
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    }
}
