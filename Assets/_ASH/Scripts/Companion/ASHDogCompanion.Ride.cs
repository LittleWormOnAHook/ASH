using UnityEngine;

namespace ASH.Companion
{
    /// <summary>Riding in the pickup (t356, t389): on BeginRide the dog runs to the tailgate (or the passenger door), jumps in (scripted arc),
    /// then either lives on the bed (stands on the "Bed collider" top: random stand / sit / lie (t391: no pacing, no paws up),
    /// all settings in ASHCompanionSettings "Truck ride") or sits in the passenger seat facing forward, and jumps out when the player gets out.</summary>
    public sealed partial class ASHDogCompanion
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHDogCompanion_Ride()
        {
            ForcePassenger = default;
            ForceBedAct = default;
        }

        enum RidePhase { None, ToTailgate, JumpIn, Riding, JumpOut }
        public enum BedAct { Stand, Sit, Lie }
        RidePhase _ride; Transform _rideSeat, _rideGate; float _rideT, _rideRange; Vector3 _jumpFrom, _jumpTo; bool _rideFromSave;
        Transform _rideVeh, _passSeat, _passDoor; Collider _bed; bool _passenger;
        Vector2 _bedMin, _bedMax; float _bedTop; Vector2 _bedPos; float _bedYaw, _bedGoalYaw, _bedUntil; BedAct _bedAct;
        [Header("Ride (pickup bed)")]
        [Tooltip("Seconds of the scripted jump in / out.")] [Range(0.2f, 2f)] public float rideJumpSeconds = 0.7f;
        [Tooltip("Height of the jump arc above the higher end, metres.")] [Range(0f, 2f)] public float rideJumpHeight = 0.6f;
        [Tooltip("Give up running to the tailgate after this long and jump from where it is.")] [Range(1f, 30f)] public float rideRunTimeout = 8f;
        [Tooltip("Old single pose (used only when no Bed collider is found): lie down while riding, else sit.")] public bool rideLieDown = true;
        /// <summary>Test hook: force (true) / forbid (false) the passenger seat on the next ride; null = random (passengerSeatChance).</summary>
        public static bool? ForcePassenger;
        /// <summary>Test hook: force the next bed behaviour.</summary>
        public static BedAct? ForceBedAct;

        public bool Riding => _ride != RidePhase.None && _ride != RidePhase.JumpOut;
        public bool InVehicle => _ride == RidePhase.Riding;
        public bool InPassengerSeat => InVehicle && _passenger;
        public BedAct CurrentBedAct => _bedAct;
        /// <summary>The next BeginRide may be instant (prologue start: the dog is already in the car with the player).</summary>
        public void AllowInstantRide() { _rideFromSave = true; }
        public Collider BedCollider => _bed;

        public bool BeginRide(Transform seat, Transform tailgate, float range, bool instant = false) => BeginRide(seat, tailgate, null, null, range, instant);

        /// <summary>Called when the player gets in. False when the dog stays (Stay mode, down, too far).</summary>
        public bool BeginRide(Transform seat, Transform tailgate, Transform passengerSeat, Transform passengerDoor, float range, bool instant = false)
        {
            if (!seat || Dead || Downed) return false;
            bool fromSave = _rideFromSave; _rideFromSave = false;
            if (instant && !fromSave) return false;
            if (!tailgate) tailgate = seat;
            if (!instant && (Mode == ASHDogMode.Stay || Vector3.Distance(transform.position, tailgate.position) > range)) return false;
            _rideSeat = seat; _rideGate = tailgate; _rideRange = range; _rideT = 0f;
            var vrb = seat.GetComponentInParent<Rigidbody>(); _rideVeh = vrb ? vrb.transform : seat.root;
            _passSeat = passengerSeat; _passDoor = passengerDoor;
            bool wantPass = ForcePassenger ?? (Random.value < S.passengerSeatChance); ForcePassenger = null;
            _passenger = _passSeat && _passDoor && wantPass;
            FindBed();
            if (instant) { StartRiding(); return true; }
            _ride = RidePhase.ToTailgate; return true;
        }

        /// <summary>Called when the player gets out: jump out onto clear ground and resume following.</summary>
        public void EndRide(Vector3 playerSpot)
        {
            if (_ride == RidePhase.None || _ride == RidePhase.JumpOut) return;
            if (_ride == RidePhase.ToTailgate) { _ride = RidePhase.None; return; }
            var gate = _passenger && _passDoor ? _passDoor : _rideGate;
            _jumpFrom = transform.position; _jumpTo = RideGround(gate ? gate.position : playerSpot, playerSpot);
            _ride = RidePhase.JumpOut; _rideT = 0f; SetPose(Pose.None);
        }

        bool _vehicleWait;
        /// <summary>t378: while the player drives a vehicle the dog is not riding in, it sits and waits where it is: no following and no
        /// auto-teleport to the player (teleporting next to / under the moving car put its body trigger in the wheel raycasts and launched the car).
        /// Its body trigger is off meanwhile. Normal follow (and catch-up teleport) resumes once the player is out.</summary>
        bool VehicleWait()
        {
            bool wait = _ride == RidePhase.None && ASH.Vehicles.ASHVehicle.Driving && !Dead && !Downed;
            if (wait != _vehicleWait)
            {
                _vehicleWait = wait;
                if (_body) _body.enabled = !wait;
                if (wait) { _speed = 0f; _moving = false; _turningInPlace = false; _stuckT = 0f; _find = null; SetPose(Pose.Sit); Activity = "Waiting by the road"; }
                else SetPose(Pose.None);
            }
            if (wait) { _speed = 0f; _moving = false; _stuckT = 0f; }
            return wait;
        }
        bool RideUpdate(float dt)
        {
            switch (_ride)
            {
                case RidePhase.None: return false;
                case RidePhase.ToTailgate:
                {
                    var gate = _passenger && _passDoor ? _passDoor : _rideGate;
                    if (!_rideSeat || !gate) { _ride = RidePhase.None; return false; }
                    _rideT += dt;
                    if (Vector3.Distance(transform.position, gate.position) > _rideRange * 1.6f) { _ride = RidePhase.None; return false; }
                    if (MoveTo(gate.position, S.runSpeed, 0.5f) || _rideT > rideRunTimeout)
                    { _jumpFrom = transform.position; _ride = RidePhase.JumpIn; _rideT = 0f; InitBedSpot(); }
                    return true;
                }
                case RidePhase.JumpIn:
                    if (!_rideSeat) { _ride = RidePhase.None; return false; }
                    if (Arc(_jumpFrom, RideTarget(), dt)) StartRiding();
                    return true;
                case RidePhase.Riding:
                    if (!_rideSeat) { _ride = RidePhase.None; return false; }
                    RideTick(dt); return true;
                case RidePhase.JumpOut:
                    if (Arc(_jumpFrom, _jumpTo, dt)) { _ride = RidePhase.None; _up = Vector3.up; _teleported = true; _lastPos = transform.position; SetPose(Pose.None); }
                    return true;
            }
            return false;
        }

        // the vehicle moves in FixedUpdate (interpolated): re-pin after it so the dog never lags into the bed
        void LateUpdate() { if (_ride == RidePhase.Riding && _rideSeat) Hold(0f); }

        void StartRiding() { _ride = RidePhase.Riding; InitBedSpot(); PickBedAct(true); Hold(0f); }

        void RideTick(float dt)
        {
            if (_passenger || !_bed) { Hold(dt); return; }
            if (Time.time >= _bedUntil || ForceBedAct.HasValue) PickBedAct(false);
            // t391: no walking around the bed: it only changes pose (stand / sit / lie, blended by the animator) and turns in place
            _actualSpeed = 0f; _moving = false;
            _bedYaw = Mathf.MoveTowardsAngle(_bedYaw, _bedGoalYaw, S.bedTurnSpeed * dt);
            _bedPos = Vector2.MoveTowards(_bedPos, ClampBed(_bedPos, 0f), 0.3f * dt);   // keep the whole body inside the walls as it turns
            var want = _bedAct == BedAct.Sit ? Pose.Sit : _bedAct == BedAct.Lie ? Pose.Lie : Pose.None;
            if (_pose != want) SetPose(want);
            Hold(dt);
        }

        void PickBedAct(bool first)
        {
            BedAct a;
            if (ForceBedAct.HasValue) { a = ForceBedAct.Value; ForceBedAct = null; }
            else
            {
                float ws = S.bedWeightStand, wsi = S.bedWeightSit, wl = S.bedWeightLie;
                float tot = ws + wsi + wl; if (tot <= 0f) { ws = 1f; tot = 1f; }
                float r = Random.value * tot;
                a = (r -= ws) < 0f ? BedAct.Stand : (r -= wsi) < 0f ? BedAct.Sit : BedAct.Lie;
                if (!first && a == _bedAct) a = (BedAct)(((int)a + 1 + Random.Range(0, 2)) % 3);   // always a visible change
            }
            _bedAct = a; var dur = S.bedBehaviourSeconds; _bedUntil = Time.time + Random.Range(Mathf.Min(dur.x, dur.y), Mathf.Max(dur.x, dur.y));
            // lying turns lengthways along the bed; standing / sitting face a random way (mostly forward / outward)
            _bedGoalYaw = a == BedAct.Lie ? (Random.value < 0.5f ? 0f : 180f) + Random.Range(-15f, 15f) : Random.Range(-S.bedMaxTurn, S.bedMaxTurn);
        }

        Vector3 RideTarget()
        {
            if (_passenger && _passSeat) return _passSeat.position;
            if (_bed && _rideVeh) return BedWorld(_bedPos, 0f);
            return _rideSeat.position;
        }

        void InitBedSpot()
        {
            if (!_bed || !_rideVeh) return;
            var gate = _rideGate ? (Vector2)Flat2(_rideVeh.InverseTransformPoint(_rideGate.position)) : new Vector2((_bedMin.x + _bedMax.x) * 0.5f, _bedMin.y);
            _bedPos = ClampBed(new Vector2((_bedMin.x + _bedMax.x) * 0.5f, gate.y), 0f); _bedYaw = 0f; _bedGoalYaw = 0f;
        }
        static Vector2 Flat2(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>Clamp a bed point so the whole body (half length + radius along the current heading) stays inside the bed walls.</summary>
        Vector2 ClampBed(Vector2 p, float extra)
        {
            float half = S.bodyLength * 0.5f + S.bodyRadius, rad = S.bodyRadius;
            float yr = _bedYaw * Mathf.Deg2Rad, s = Mathf.Abs(Mathf.Sin(yr)), c = Mathf.Abs(Mathf.Cos(yr));
            float ex = half * s + rad * c + S.bedEdgeMargin + extra, ez = half * c + rad * s + S.bedEdgeMargin + extra;
            float cx = (_bedMin.x + _bedMax.x) * 0.5f, cz = (_bedMin.y + _bedMax.y) * 0.5f;
            float hx = Mathf.Max(0f, (_bedMax.x - _bedMin.x) * 0.5f - ex), hz = Mathf.Max(0f, (_bedMax.y - _bedMin.y) * 0.5f - ez);
            return new Vector2(Mathf.Clamp(p.x, cx - hx, cx + hx), Mathf.Clamp(p.y, cz - hz, cz + hz));
        }


        /// <summary>World point on the top of the Bed collider at bed-space p (ray straight down onto that collider only).</summary>
        Vector3 BedWorld(Vector2 p, float lift)
        {
            var up = _rideVeh.up;
            var w = _rideVeh.TransformPoint(new Vector3(p.x, _bedTop, p.y));
            if (_bed.Raycast(new Ray(w + up * 1.5f, -up), out var h, 3f)) w = h.point;
            return w + up * (S.bedHeightOffset + lift);
        }

        void Hold(float dt)
        {
            _lastPos = transform.position; _teleported = true; _turningInPlace = false; _stuckT = 0f;
            if (!_moving) _speed = 0f;
            if (_passenger || !_bed || !_rideVeh)
            {
                var seatT = _passenger && _passSeat ? _passSeat : _rideSeat;
                transform.position = seatT.position; _speed = 0f; _moving = false; _actualSpeed = 0f;
                var f = Vector3.ProjectOnPlane(seatT.forward, Vector3.up);
                if (f.sqrMagnitude > 1e-4f) _yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                _animYaw = _yaw; _up = seatT.up; ApplyRotation();
                var want = _passenger ? Pose.Sit : rideLieDown ? Pose.Lie : Pose.Sit; if (_pose != want) SetPose(want);
                _lastPos = transform.position; return;
            }
            var pos = BedWorld(_bedPos, 0f);
            var fwd = _rideVeh.rotation * Quaternion.Euler(0f, _bedYaw, 0f) * Vector3.forward;
            var fl = Vector3.ProjectOnPlane(fwd, Vector3.up); if (fl.sqrMagnitude > 1e-4f) _yaw = Mathf.Atan2(fl.x, fl.z) * Mathf.Rad2Deg;
            _up = _rideVeh.up; transform.position = pos; ApplyRotation();
            if (dt <= 0f) _animYaw = _yaw;
            _lastPos = transform.position;
        }

        void FindBed()
        {
            _bed = null;
            if (_rideVeh) foreach (var c in _rideVeh.GetComponentsInChildren<Collider>(true)) if (c && c.name == S.bedColliderName && !c.isTrigger) { _bed = c; break; }
            if (!_bed && !string.IsNullOrEmpty(S.bedColliderName))
            {
                var go = GameObject.Find(S.bedColliderName); var c = go ? go.GetComponent<Collider>() : null;
                if (c && _rideVeh && c.transform.IsChildOf(_rideVeh)) _bed = c;
            }
            if (!_bed || !_rideVeh) return;
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            void Add(Vector3 w) { var l = _rideVeh.InverseTransformPoint(w); mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l); }
            if (_bed is BoxCollider b)
            {
                for (int i = 0; i < 8; i++)
                {
                    var o = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                    Add(b.transform.TransformPoint(b.center + Vector3.Scale(o, b.size)));
                }
            }
            else { var bb = _bed.bounds; for (int i = 0; i < 8; i++) Add(new Vector3((i & 1) == 0 ? bb.min.x : bb.max.x, (i & 2) == 0 ? bb.min.y : bb.max.y, (i & 4) == 0 ? bb.min.z : bb.max.z)); }
            _bedMin = new Vector2(mn.x, mn.z); _bedMax = new Vector2(mx.x, mx.z); _bedTop = mx.y;
        }

        bool Arc(Vector3 a, Vector3 b, float dt)
        {
            _rideT += dt / Mathf.Max(0.1f, rideJumpSeconds);
            float t = Mathf.Clamp01(_rideT);
            var p = Vector3.Lerp(a, b, t); float top = Mathf.Max(a.y, b.y) + rideJumpHeight - Mathf.Lerp(a.y, b.y, t);
            p.y += 4f * t * (1f - t) * Mathf.Max(0f, top);
            var d = b - a; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) { _yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; _animYaw = _yaw; }
            _up = Vector3.up; transform.position = p; ApplyRotation(); _lastPos = p; _teleported = true;
            return t >= 1f;
        }
        Vector3 RideGround(Vector3 prefer, Vector3 fallback)
        {
            Transform veh = null; if (_rideSeat) { var vrb = _rideSeat.GetComponentInParent<Rigidbody>(); veh = vrb ? vrb.transform : _rideSeat.root; }
            foreach (var c in new[] { prefer, prefer + (prefer - (veh ? veh.position : prefer)).normalized * 0.8f, fallback + (fallback - prefer).normalized * 0.9f, fallback })
            {
                float best = float.MaxValue; Vector3 g = default; bool ok = false;
                foreach (var h in Physics.RaycastAll(c + Vector3.up * 2.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (veh && h.collider.transform.IsChildOf(veh)) { ok = false; best = -1f; break; }
                    if (h.collider.transform.IsChildOf(transform) || h.collider.CompareTag("Player")) continue;
                    if (h.distance < best) { best = h.distance; g = h.point; ok = true; }
                }
                if (ok) return g;
            }
            return fallback;
        }
    }
}
