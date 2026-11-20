using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>t389: the player sits visibly in the driver seat. Swaps the player's Animator to Resources/ASH_DriverAnimator (Props Animations
    /// Driver.fbx drive / turn blend on "Steer", Malbers sit-down / stand-up for getting in / out), IKs the hands onto the steering wheel rim,
    /// turns the steering wheel (split out of the dashboard by ASH/Systems/Vehicles/Setup Driver) with the steering, and gives the
    /// first-person camera a point at the driver's head. Everything is restored on exit.</summary>
    [DefaultExecutionOrder(4000)]
    public sealed class ASHVehicleDriver : MonoBehaviour
    {
        [Header("Seated driver (t389)")]
        [Tooltip("Show the player in the seat (off = hidden like before).")] public bool showDriver = true;
        [Tooltip("Player root offset from the seat point, in seat space (x right, y up, z forward), metres.")] public Vector3 seatOffset = Vector3.zero;
        [Tooltip("Player rotation offset in the seat, degrees.")] public Vector3 seatEuler = Vector3.zero;
        [Tooltip("t392: raise (+) / lower (-) the seated player, metres. Moves only the player, never the seat.")] [Range(-0.3f, 0.5f)] public float playerHeightOffset = 0.1f;
        [Tooltip("Line the hips up on the seat automatically (seatOffset is added on top).")] public bool autoFitHips = true;
        [Tooltip("Hip height above the seat point, metres (auto fit; playerHeightOffset is added on top).")] [Range(-0.6f, 0.8f)] public float hipHeight = -0.26f;
        [Tooltip("Hip forward / back from the seat point, metres (auto fit).")] [Range(-0.6f, 0.6f)] public float hipForward = 0f;
        [Tooltip("Use the driving animations (else the player's animator just freezes in place).")] public bool useDriverAnimator = true;
        [Header("Getting in / out")]
        [Tooltip("Seconds from the door into the seat (sit-down clip plays meanwhile). 0 = snap.")] [Range(0f, 4f)] public float enterSeconds = 1.0f;
        [Tooltip("Seconds from the seat out to the door (stand-up clip). 0 = snap.")] [Range(0f, 4f)] public float exitSeconds = 0.8f;
        [Tooltip("Cross-fade into the drive pose, seconds.")] [Range(0f, 1f)] public float poseBlendSeconds = 0.25f;
        [Header("Steering wheel")]
        public Transform steeringWheel;
        [Tooltip("Steering wheel local axis the column runs along (filled by Setup Driver: z).")] public Vector3 wheelAxis = Vector3.forward;
        [Tooltip("Steering wheel rotation at full lock, degrees each way (t391: 90).")] [Range(0f, 540f)] public float wheelMaxAngle = 90f;
        [Tooltip("Reverse the steering wheel's turn direction (t396: on = correct for both vehicles).")] public bool invertWheel = true;
        [HideInInspector] public int tuningVersion;
        [Tooltip("Rim radius where the hands grip, metres (filled by Setup Driver).")] [Range(0.05f, 0.4f)] public float gripRadius = 0.17f;
        [Header("Hands (IK)")]
        public bool handIK = true;
        [Range(0f, 1f)] public float handWeight = 1f;
        [Range(0f, 1f)] public float handRotationWeight = 0.6f;
        [Tooltip("Grip angles on the rim, degrees clockwise from the top as the driver sees it.")] [Range(-180f, 180f)] public float leftGripAngle = -90f;
        [Range(-180f, 180f)] public float rightGripAngle = 90f;
        [Tooltip("Wrist sits this far behind the rim toward the driver (the palm wraps the rim ahead of it), metres.")] [Range(-0.1f, 0.2f)] public float wristBack = 0.06f;
        [Tooltip("Wrist sits this far outside the rim, metres.")] [Range(-0.1f, 0.1f)] public float wristOut = 0.015f;
        [Tooltip("Slide the player toward the wheel until the hands can reach the rim (max metres).")] [Range(0f, 0.5f)] public float autoReachMax = 0.3f;
        [Tooltip("t411: hands turn with the wheel up to this many degrees, then slide along the rim (keeps them reachable at full lock).")] [Range(0f, 180f)] public float handFollowMaxAngle = 45f;
        [Tooltip("t411: arms may stretch to this share of their length before the player slides toward the wheel.")] [Range(0.8f, 1f)] public float reachMargin = 0.92f;
        float? _forceAng;
        [Tooltip("How much the hands travel round with the wheel (1 = glued to the rim).")] [Range(0f, 1f)] public float handsFollowWheel = 1f;
        [Tooltip("Hand target offsets in wheel space (x right, y up, z toward the driver), metres.")] public Vector3 leftHandOffset = new Vector3(0f, 0f, 0.02f);
        public Vector3 rightHandOffset = new Vector3(0f, 0f, 0.02f);
        public Vector3 leftHandEuler = new Vector3(0f, 0f, 0f), rightHandEuler = new Vector3(0f, 0f, 0f);
        [Header("Steer animation")]
        [Tooltip("How much the DriveTurnLeft / Right clips blend in with steering.")] [Range(0f, 1f)] public float steerAnimWeight = 1f;
        [Header("First person (Tab)")]
        [Tooltip("Camera at the driver's head bone (else the vehicle's head point).")] public bool firstPersonAtHeadBone = true;
        [Tooltip("Camera offset from the head, in vehicle space (x right, y up, z forward), metres.")] public Vector3 headOffset = new Vector3(0f, 0.08f, 0.1f);
        [Tooltip("Shrink the head while in first person so it doesn't block the view.")] public bool hideHeadInFirstPerson = true;
        [Range(0.01f, 0.5f)] public float firstNearClip = 0.05f;

        public bool Seated { get; private set; }
        public Animator Anim => _anim;
        ASHVehicle _veh; ASHVehicleCamera _cam; Animator _anim; RuntimeAnimatorController _origCtrl; bool _origRoot; AnimatorCullingMode _origCull;
        Quaternion _wheelBase; bool _haveWheelBase; Transform _headCam, _headBone; Vector3 _headScale; bool _headHidden;
        Camera _mainCam; float _savedNear = -1f; float _steer; readonly List<Renderer> _heldHidden = new List<Renderer>(); readonly List<Behaviour> _ikOff = new List<Behaviour>();
        ASHDriverIK _ik;
        static RuntimeAnimatorController _ctrl;
        static readonly int HSteer = Animator.StringToHash("Steer");
        public static RuntimeAnimatorController DriverController => _ctrl ? _ctrl : _ctrl = Resources.Load<RuntimeAnimatorController>("ASH_DriverAnimator");
        public float SteerAngle { get; private set; }

        void Awake()
        {
            _veh = GetComponent<ASHVehicle>(); _cam = GetComponent<ASHVehicleCamera>();
            if (!steeringWheel) { var t = FindDeep(transform, "ASH_SteeringWheel"); if (t) steeringWheel = t; }
            if (steeringWheel) { _wheelBase = steeringWheel.localRotation; _haveWheelBase = true; }
        }
        static Transform FindDeep(Transform r, string n) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }

        Vector3 _fit; float _reach, _reachUntil; Transform _pt;
        [Tooltip("t409: hard-lock the seated player to the seat every frame (no bounce over bumps).")] public bool lockToSeat = true;
        bool _sitting; public float MaxSeatDrift { get; set; }
        public float SeatError => _pt ? (_pt.localPosition - SeatLocalPos).magnitude : 0f;
        bool _fitPending; float _fitUntil; public Vector3 FitCorrection { get; private set; }
        Vector3 SeatLocalPos => _fit + seatOffset + Vector3.up * playerHeightOffset + Vector3.forward * _reach; Quaternion SeatLocalRot => Quaternion.Euler(seatEuler);

        /// <summary>Player is parented to the seat already. Plays sit-down while sliding from where it stood into the seat.</summary>
        public IEnumerator SitIn(GameObject player, Animator anim, Transform seat)
        {
            Seated = true; _anim = anim;
            var pt = player.transform;
            Vector3 fromL = pt.localPosition; Quaternion fromR = pt.localRotation; _reach = 0f;
            if (anim)
            {
                _origCtrl = anim.runtimeAnimatorController; _origRoot = anim.applyRootMotion; _origCull = anim.cullingMode;
                var c = useDriverAnimator ? DriverController : null;
                anim.enabled = true; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (c) { anim.runtimeAnimatorController = c; anim.Rebind(); anim.Update(0f); }
                _fit = Vector3.zero;
                if (c && autoFitHips && anim.isHuman && anim.GetBoneTransform(HumanBodyBones.Hips))
                {
                    // sample the drive pose once, seated at the seat point, and shift so the hips land on the seat
                    Vector3 sp = pt.localPosition; Quaternion sr = pt.localRotation;
                    pt.localPosition = Vector3.zero; pt.localRotation = SeatLocalRot;
                    anim.Play("Drive", 0, 0f); anim.Update(0f);
                    var hips = seat.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Hips).position);
                    _fit = new Vector3(-hips.x, hipHeight - hips.y, hipForward - hips.z);
                    pt.localPosition = sp; pt.localRotation = sr;
                }
                if (c) { anim.Play(enterSeconds > 0.05f ? "Enter" : "Drive", 0, 0f); anim.Update(0f); }
                _ik = anim.GetComponent<ASHDriverIK>(); if (!_ik) _ik = anim.gameObject.AddComponent<ASHDriverIK>();
                _ik.driver = this; _ik.enabled = true;
                HideHeld(anim, true);
                _ikOff.Clear();
                // anything that drives the player's own animator layers / IK would fight the 1-layer drive controller ("Invalid Layer Index")
                foreach (var b in player.GetComponentsInChildren<Behaviour>())
                {
                    if (!b || !b.enabled || b == _ik || b == anim) continue;
                    var tn = b.GetType().Name;
                    if (tn.Contains("HeadTrack") || tn.Contains("FootStep") || tn == "ASHActionLayer" || tn == "ASHBowDrawRig" || tn == "ASHStanceProbe") { b.enabled = false; _ikOff.Add(b); }
                }
                _headBone = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Head) : null; if (_headBone) _headScale = _headBone.localScale;
            }
            if (!_headCam) { _headCam = new GameObject("ASH_DriverHeadCam").transform; _headCam.SetParent(transform, false); }
            if (_cam && firstPersonAtHeadBone && _headBone) _cam.head = _headCam;
            float t = 0f, dur = enterSeconds; _sitting = true;
            while (dur > 0.01f && t < dur)
            {
                t += Time.deltaTime; float k = Mathf.SmoothStep(0f, 1f, t / dur);
                pt.localPosition = Vector3.Lerp(fromL, SeatLocalPos, k); pt.localRotation = Quaternion.Slerp(fromR, SeatLocalRot, Mathf.Clamp01(k * 1.6f));
                yield return null;
            }
            pt.localPosition = SeatLocalPos; pt.localRotation = SeatLocalRot; _sitting = false;
            _pt = pt; _reachUntil = Time.time + poseBlendSeconds + 0.8f; _fitPending = autoFitHips; _fitUntil = _reachUntil + 1.5f; FitCorrection = Vector3.zero;
            if (anim && anim.runtimeAnimatorController == DriverController && DriverController) anim.CrossFadeInFixedTime("Drive", poseBlendSeconds, 0);
        }

        /// <summary>Stand-up clip while sliding out to the exit spot, then restore the player's own animator.</summary>
        public IEnumerator SitOut(GameObject player, Vector3 spot, Quaternion face)
        {
            var pt = player ? player.transform : null; _pt = null; // release the seat lock for the slide out
            if (_anim && _anim.runtimeAnimatorController == DriverController && DriverController && exitSeconds > 0.05f) _anim.CrossFadeInFixedTime("Exit", 0.15f, 0);
            if (_ik) _ik.fadeOut = true;
            float t = 0f, dur = exitSeconds;
            if (pt)
            {
                Vector3 a = pt.position; Quaternion ar = pt.rotation;
                while (dur > 0.01f && t < dur)
                {
                    t += Time.deltaTime; float k = Mathf.SmoothStep(0f, 1f, t / dur);
                    pt.SetPositionAndRotation(Vector3.Lerp(a, spot, k), Quaternion.Slerp(ar, face, k));
                    yield return null;
                }
            }
            Restore();
        }

        public void Restore()
        {
            if (!Seated) return;
            Seated = false; _pt = null; _sitting = false;
            ShowHead(true);
            if (_mainCam && _savedNear > 0f) _mainCam.nearClipPlane = _savedNear; _savedNear = -1f; _mainCam = null;
            if (_ik) { _ik.enabled = false; _ik.fadeOut = false; }
            HideHeld(_anim, false);
            foreach (var b in _ikOff) if (b) b.enabled = true; _ikOff.Clear();
            if (_anim)
            {
                if (_origCtrl && _anim.runtimeAnimatorController != _origCtrl) { _anim.runtimeAnimatorController = _origCtrl; _anim.Rebind(); }
                _anim.applyRootMotion = _origRoot; _anim.cullingMode = _origCull;
            }
            if (_cam && _veh) _cam.head = _veh.head;
            _anim = null; _origCtrl = null;
            if (steeringWheel && _haveWheelBase) steeringWheel.localRotation = _wheelBase;
        }

        void HideHeld(Animator anim, bool hide)
        {
            if (!hide) { foreach (var r in _heldHidden) if (r) r.enabled = true; _heldHidden.Clear(); return; }
            _heldHidden.Clear(); if (!anim || !anim.isHuman) return;
            foreach (var hb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
            {
                var h = anim.GetBoneTransform(hb); if (!h) continue;
                foreach (var r in h.GetComponentsInChildren<Renderer>()) if (r.enabled && !(r is SkinnedMeshRenderer)) { r.enabled = false; _heldHidden.Add(r); }
            }
        }

        void ShowHead(bool show)
        {
            if (!_headBone) return;
            if (show && _headHidden) { _headBone.localScale = _headScale; _headHidden = false; }
            else if (!show && !_headHidden) { _headScale = _headBone.localScale; _headBone.localScale = _headScale * 0.001f; _headHidden = true; }
        }

        void Update()
        {
            float target = _veh && ASHVehicle.Driving == _veh ? _veh.SteerInput : 0f;
            _steer = target;
            SteerAngle = _steer * wheelMaxAngle * (invertWheel ? -1f : 1f);
            if (steeringWheel && _haveWheelBase)
            {
                var ax = wheelAxis.sqrMagnitude > 1e-4f ? wheelAxis.normalized : Vector3.forward;
                steeringWheel.localRotation = _wheelBase * Quaternion.AngleAxis(-SteerAngle, ax);
            }
            if (Seated && _anim && _anim.runtimeAnimatorController == DriverController && DriverController) _anim.SetFloat(HSteer, _steer * steerAnimWeight, 0.1f, Time.deltaTime);
        }

        void LateUpdate()
        {
            if (!Seated) return;
            if (_fitPending && _pt && _anim && _anim.isHuman && autoFitHips && _pt.parent)
            {
                // t405: the one-frame hip sample at sit-in is unreliable (sometimes the standing pose), so once the Drive pose is
                // actually playing, measure the real hips and correct once: hips land at (hipHeight + playerHeightOffset) above the seat.
                var st = _anim.GetCurrentAnimatorStateInfo(0); var hb = _anim.GetBoneTransform(HumanBodyBones.Hips);
                if (hb && st.IsName("Drive") && !_anim.IsInTransition(0))
                {
                    var h = _pt.parent.InverseTransformPoint(hb.position);
                    var target = new Vector3(seatOffset.x, hipHeight + playerHeightOffset + seatOffset.y, hipForward + seatOffset.z + _reach);
                    var corr = target - h; corr.x = Mathf.Clamp(corr.x, -0.3f, 0.3f);
                    // ease in over ~1.5 s of the settled Drive pose (averages the idle sway instead of trusting one frame)
                    var step = corr * Mathf.Clamp01(Time.deltaTime * 4f); _fit += step; FitCorrection += step; _pt.localPosition = SeatLocalPos;
                    if (Time.time > _fitUntil) _fitPending = false;
                }
                else if (Time.time > _fitUntil + 2f) _fitPending = false;
            }
            if (_pt && _anim && _anim.isHuman && Time.time < _reachUntil && handIK && steeringWheel && autoReachMax > 0f)
            {
                // arms too short for the rim: slide toward the wheel (shoulder -> grip vs. arm length, both hands)
                float excess = 0f;
                foreach (var left in new[] { true, false })
                {
                    var up = _anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                    var lo = _anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                    var hd = _anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                    if (!up || !lo || !hd) continue;
                    float arm = Vector3.Distance(up.position, lo.position) + Vector3.Distance(lo.position, hd.position);
                    // t411: check the grip at rest and at both ends of the hand travel, so full lock is reachable too
                    foreach (var fa in new[] { 0f, -handFollowMaxAngle, handFollowMaxAngle })
                    {
                        _forceAng = fa; bool ok = HandTarget(left, out var tp, out _); _forceAng = null;
                        if (ok) excess = Mathf.Max(excess, Vector3.Distance(up.position, tp) - arm * reachMargin);
                    }
                }
                if (excess > 0.005f) { _reach = Mathf.Clamp(_reach + excess * 0.5f, 0f, autoReachMax); _pt.localPosition = SeatLocalPos; }
            }
            // t409: rigid seat lock - re-snap after everything else moved the player (no smoothing while seated, once sat in)
            if (_pt && lockToSeat && !_sitting) { float dr = (_pt.localPosition - SeatLocalPos).magnitude; if (dr > MaxSeatDrift) MaxSeatDrift = dr; _pt.localPosition = SeatLocalPos; _pt.localRotation = SeatLocalRot; }
            SolveHands();
            if (_headCam && _headBone) _headCam.position = (_headHidden ? _headBone.position : _headBone.position) + transform.rotation * headOffset;
            if (_headCam) _headCam.rotation = transform.rotation;
            bool fp = _cam && _cam.Active && _cam.view == ASHVehicleCamera.View.FirstPerson;
            if (hideHeadInFirstPerson) ShowHead(!fp); else ShowHead(true);
            var mc = Camera.main;
            if (fp && mc) { if (_savedNear < 0f) { _savedNear = mc.nearClipPlane; _mainCam = mc; } mc.nearClipPlane = firstNearClip; }
            else if (_mainCam && _savedNear > 0f) { _mainCam.nearClipPlane = _savedNear; _savedNear = -1f; }
        }

        [Tooltip("t411: exact two-bone arm solve in LateUpdate (after the wheel turned and the seat lock) so the hands stay on the rim.")] public bool exactArmIK = true;
        public float LastHandErr { get; private set; } public float ReachUsed => _reach;
        void SolveHands()
        {
            LastHandErr = 0f;
            if (!exactArmIK || !handIK || !_anim || !_anim.isHuman || !_ik || !_ik.enabled || _ik.fadeOut || _pt == null || _sitting) return;
            float w = Mathf.Clamp01(handWeight * _ik.Weight); if (w <= 0.001f) return;
            foreach (var left in new[] { true, false })
            {
                var up = _anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var lo = _anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var hd = _anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (!up || !lo || !hd || !HandTarget(left, out var tp, out var tr)) continue;
                var goal = Vector3.Lerp(hd.position, tp, w);
                Vector3 a = up.position, b = lo.position, c = hd.position;
                float lab = Vector3.Distance(a, b), lbc = Vector3.Distance(b, c);
                var at = goal - a; float lat = Mathf.Clamp(at.magnitude, 0.02f, lab + lbc - 0.002f); var dir = at.normalized;
                // keep the animated elbow's bend direction (perpendicular part of shoulder->elbow); fall back to down / outward
                var bend = Vector3.ProjectOnPlane(b - a, dir);
                if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(-transform.up + (left ? -transform.right : transform.right) * 0.5f, dir);
                bend.Normalize();
                float cosA = Mathf.Clamp((lab * lab + lat * lat - lbc * lbc) / (2f * lab * lat), -1f, 1f);
                var elbow = a + dir * (lab * cosA) + bend * (lab * Mathf.Sqrt(1f - cosA * cosA));
                up.rotation = Quaternion.FromToRotation(b - a, elbow - a) * up.rotation;
                lo.rotation = Quaternion.FromToRotation(hd.position - lo.position, goal - lo.position) * lo.rotation;
                var want = HandBoneRotation(_anim, left, tr);
                hd.rotation = Quaternion.Slerp(hd.rotation, want, Mathf.Clamp01(handRotationWeight * _ik.Weight));
                LastHandErr = Mathf.Max(LastHandErr, Vector3.Distance(hd.position, tp));
            }
        }

        /// <summary>Wheel frame: centre, axis toward the driver, the rim's (rotated) up and right as the driver sees them.</summary>
        bool WheelFrame(float follow, out Vector3 c, out Vector3 toDriver, out Vector3 up, out Vector3 right)
        {
            c = toDriver = up = right = default;
            if (!steeringWheel) return false;
            c = steeringWheel.position;
            var ax = wheelAxis.sqrMagnitude > 1e-4f ? wheelAxis.normalized : Vector3.forward;
            var parentRot = steeringWheel.parent ? steeringWheel.parent.rotation : Quaternion.identity;
            var baseRot = parentRot * (_haveWheelBase ? _wheelBase : steeringWheel.localRotation);
            toDriver = baseRot * ax;
            var seatP = _veh && _veh.seat ? _veh.seat.position + transform.up * 1f : transform.position;
            if (Vector3.Dot(toDriver, seatP - c) < 0f) toDriver = -toDriver;
            // the rim's real up (the wheel mesh's own up), blended from rest by how much the hands follow it round
            var baseUp = Vector3.ProjectOnPlane(baseRot * Vector3.up, toDriver).normalized;
            var nowUp = Vector3.ProjectOnPlane(steeringWheel.rotation * Vector3.up, toDriver).normalized;
            // t411: hands ride round with the rim up to handFollowMaxAngle, then the grip slides along the rim (still on it) so the
            // arms never have to reach past their length at full lock
            float ang = _forceAng ?? Mathf.Clamp(Vector3.SignedAngle(baseUp, nowUp, toDriver), -handFollowMaxAngle, handFollowMaxAngle) * follow;
            up = (Quaternion.AngleAxis(ang, toDriver) * baseUp).normalized;
            right = Vector3.Cross(up, -toDriver).normalized;
            return true;
        }

        public bool HandTarget(bool left, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (!WheelFrame(handsFollowWheel, out var c, out var td, out var up, out var right)) return false;
            float a = (left ? leftGripAngle : rightGripAngle) * Mathf.Deg2Rad;
            var radial = (up * Mathf.Cos(a) + right * Mathf.Sin(a)).normalized;
            var off = left ? leftHandOffset : rightHandOffset;
            var rim = c + radial * gripRadius;
            pos = rim + td * wristBack + radial * wristOut + right * off.x + up * off.y + td * off.z;
            // fingers reach forward over the rim (away from the driver), palm faces the wheel centre
            rot = Quaternion.LookRotation(-td, -radial) * Quaternion.Euler(left ? leftHandEuler : rightHandEuler);
            return true;
        }

        /// <summary>Hand bone rotation that puts the hand's own finger / palm frame onto the wanted (finger, palm) frame.</summary>
        public Quaternion HandBoneRotation(Animator a, bool left, Quaternion want)
        {
            var hand = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var mid = a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var idx = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var lit = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            if (!hand || !mid || !idx || !lit) return want;
            var finger = (mid.position - hand.position).normalized; var thumbSide = (idx.position - lit.position).normalized;
            var palm = (left ? Vector3.Cross(thumbSide, finger) : Vector3.Cross(finger, thumbSide)).normalized;
            if (finger.sqrMagnitude < 1e-6f || palm.sqrMagnitude < 1e-6f) return want;
            var cur = Quaternion.LookRotation(finger, palm);
            return want * Quaternion.Inverse(cur) * hand.rotation;
        }
    }

    /// <summary>Lives on the player's Animator object while driving; forwards OnAnimatorIK to the vehicle driver (needs IK Pass on the layer).</summary>
    public sealed class ASHDriverIK : MonoBehaviour
    {
        public ASHVehicleDriver driver; public bool fadeOut; float _w; public float Weight => _w;
        Animator _a;
        void OnEnable() { _w = 0f; }
        void OnAnimatorIK(int layer)
        {
            if (!_a) _a = GetComponent<Animator>();
            if (!driver || !_a || !driver.handIK) return;
            _w = Mathf.MoveTowards(_w, fadeOut ? 0f : 1f, Time.deltaTime * 3f);
            foreach (var left in new[] { true, false })
            {
                var g = left ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
                if (!driver.HandTarget(left, out var p, out var r)) { _a.SetIKPositionWeight(g, 0f); _a.SetIKRotationWeight(g, 0f); continue; }
                _a.SetIKPositionWeight(g, driver.handWeight * _w); _a.SetIKRotationWeight(g, driver.handRotationWeight * _w);
                _a.SetIKPosition(g, p); _a.SetIKRotation(g, driver.HandBoneRotation(_a, left, r));
            }
        }
    }
}
