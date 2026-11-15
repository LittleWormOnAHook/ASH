using ASH.Core;
using ASH.Inventory;
using ASH.Player;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Left-hand light poses (on the player Animator; humanoid OnAnimatorIK, IK Pass on the base layer):
    /// - Torch: held raised in the air (left-arm IK), RMB raises it further toward the aim direction.
    /// - Flashlight: carried at the side; hold RMB to aim: the arm points the beam where the camera looks.
    /// While aiming the body faces the camera like Invector strafe/weapon aiming (FreeWithStrafe + rotate with camera,
    /// restored on release). All poses blend with ikBlendSpeed. The hand-placed light (Held_Torch / Held_Flashlight) is never moved:
    /// the upright/aim alignment turns the left hand BONE after animation (re-posed by the Animator each frame).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1700)]
    public sealed class ASHLightAim : MonoBehaviour
    {
        [Header("Aim (hold Secondary / RMB)")]
        public bool aimWithTorch = true;
        public bool aimWithFlashlight = true;
        [Tooltip("Body turn speed toward the camera while aiming (Invector strafe rotation speed).")]
        [Range(1f, 60f)] public float aimRotationSpeed = 16f;

        [Header("Left arm IK")]
        [Range(0f, 1f)] public float torchRaisedWeight = 0.9f;
        [Range(0f, 1f)] public float aimWeight = 1f;
        [Range(0.5f, 30f)] public float ikBlendSpeed = 6f;
        [Tooltip("Raised torch hand position from the left shoulder, character space (x right, y up, z forward; metres).")]
        public Vector3 torchRaisedOffset = new Vector3(-0.1f, 0.38f, 0.2f);
        [Tooltip("Lowers the raised torch straight down (metres) from torchRaisedOffset; also applies while aiming the torch.")]
        [Range(0f, 0.5f)] public float torchHeightDrop = 0.2f;
        [Tooltip("Aiming: hand distance from the shoulder along the aim direction.")]
        [Range(0.2f, 0.9f)] public float aimReach = 0.55f;
        [Range(-0.5f, 0.5f)] public float aimRaise = 0.05f;
        [Tooltip("Elbow hint from the left shoulder, character space.")]
        public Vector3 elbowHintOffset = new Vector3(-0.35f, -0.2f, -0.15f);
        [Tooltip("How far a torch tilts toward the aim direction (0 = straight up).")]
        [Range(0f, 1f)] public float torchAimTilt = 0.35f;
        [Tooltip("How fully the held light turns to the pose (torch upright / beam on the aim line) by turning the hand. 0 = exactly your hand placement.")]
        [Range(0f, 1f)] public float itemAlignWeight = 1f;
        [Header("Flashlight wrist (aim)")]
        [Tooltip("How far the wrist straightens while aiming the flashlight (1 = hand in line with the forearm, 0 = animation/IK wrist). The beam direction is unchanged: the arm turns instead.")]
        [Range(0f, 1f)] public float flashWristStraighten = 0.85f;
        [Tooltip("Wrist angle while aiming the flashlight (deg, + = hand bent up from the forearm, - = down). The beam direction is kept: the arm compensates.")]
        [Range(-30f, 30f)] public float flashWristAngle = 0f;

        public bool Aiming { get; private set; }
        public float Weight => _w;
        public bool TorchRaised => _torch && _w > 0.5f;
        public int IKCalls { get; private set; }
        /// <summary>Angle (deg) between the forearm and the hand (wrist bend) after the light pose this frame.</summary>
        public float WristBendDeg { get; private set; } = -1f;
        /// <summary>Angle (deg) between the light's forward and the aim direction after the pose (flashlight aim).</summary>
        public float BeamErrorDeg { get; private set; } = -1f;

        Animator _anim; ASHEquipment _eq; ASHInput _in; vThirdPersonController _cc; Camera _cam;
        float _w; bool _torch, _flash;

        void Awake()
        {
            _anim = GetComponent<Animator>();
            _eq = GetComponentInParent<ASHEquipment>(); _in = GetComponentInParent<ASHInput>(); _cc = GetComponentInParent<vThirdPersonController>();
        }

        Transform Root => _cc ? _cc.transform : transform;
        Vector3 AimDir { get { if (!_cam) _cam = Camera.main; return _cam ? _cam.transform.forward : Root.forward; } }

        void Update()
        {
            var off = _eq ? _eq.Off : null;
            _flash = off && off.IsLightSource && off.gripStyle == ASHGripStyle.Beam;
            _torch = off && off.IsLightSource && !_flash;
            bool weaponAims = _eq && ASHWeaponAim.KindOf(_eq.Main) != ASHWeaponAim.Kind.None; // RMB aims the gun/bow instead
            bool want = !weaponAims && _in && ((_torch && aimWithTorch) || (_flash && aimWithFlashlight)) && _in.SecondaryHeld && !ASHUIState.BlocksGameplay;
            if (want != Aiming) SetAim(want);
            float target = Aiming ? aimWeight : _torch ? torchRaisedWeight : 0f;
            _w = Mathf.MoveTowards(_w, target, ikBlendSpeed * Time.deltaTime);
        }

        void SetAim(bool on)
        {
            Aiming = on;
            if (!_cc) return;
            if (on) ASHStrafeLock.Acquire(_cc, this, aimRotationSpeed); else ASHStrafeLock.Release(_cc, this);
        }

        void OnDisable() { if (Aiming) SetAim(false); }

        void OnAnimatorIK(int layer)
        {
            IKCalls++;
            if (layer != 0 || !_anim || !_anim.isHuman || _w <= 0.001f) return;
            var shoulderT = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); if (!shoulderT) return;
            Vector3 sh = shoulderT.position, aim = AimDir;
            Vector3 goal = Aiming ? sh + aim * aimReach + Vector3.up * aimRaise : sh + Root.TransformDirection(torchRaisedOffset) - Vector3.up * torchHeightDrop;
            if (Aiming && _torch) goal += Vector3.up * (Mathf.Max(0f, torchRaisedOffset.y * 0.5f) - torchHeightDrop);
            _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, _w);
            _anim.SetIKPosition(AvatarIKGoal.LeftHand, goal);
            _anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, _w);
            _anim.SetIKHintPosition(AvatarIKHint.LeftElbow, sh + Root.TransformDirection(elbowHintOffset));
        }

        void LateUpdate()
        {
            var v = _eq ? _eq.OffVisual : null;
            var hand = _anim && _anim.isHuman ? _anim.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            WristBendDeg = -1f; BeamErrorDeg = -1f;
            if (!v || !v.activeInHierarchy || !hand || !v.transform.IsChildOf(hand)) return;
            float k = Mathf.Clamp01(_w) * itemAlignWeight;
            if (k <= 0.001f) return;
            Vector3 aim = AimDir, up = Vector3.up, fwd = Vector3.ProjectOnPlane(Root.forward, up).normalized;
            Quaternion desired = _flash ? Quaternion.LookRotation(aim, up)
                : Quaternion.LookRotation(Vector3.Slerp(up, aim, Aiming ? torchAimTilt : 0f).normalized, fwd);
            var delta = Quaternion.Slerp(Quaternion.identity, desired * Quaternion.Inverse(v.transform.rotation), k);
            var ua = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); var la = _anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            var mid0 = _anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            if (_flash && ua && la && mid0 && flashWristStraighten + Mathf.Abs(flashWristAngle) > 0.001f)
            {
                // straighten the wrist without changing where the light points: final hand rotation (light on the aim line) is fixed;
                // turn the forearm toward the hand's own direction and re-seat shoulder->elbow->hand near the IK'd hand position
                var Hd = delta * hand.rotation;
                var fingerLocal = Quaternion.Inverse(hand.rotation) * (mid0.position - hand.position).normalized;
                var fFull = (Hd * fingerLocal).normalized; var fNow = (hand.position - la.position).normalized;
                var f = Vector3.Slerp(fNow, fFull, flashWristStraighten * k).normalized;
                var side = Vector3.Cross(f, Vector3.up);
                if (side.sqrMagnitude > 1e-6f) f = Quaternion.AngleAxis(-flashWristAngle * k, side.normalized) * f;
                Vector3 S = ua.position, G = hand.position; float lu = Vector3.Distance(S, la.position), lf = Vector3.Distance(la.position, hand.position);
                var E = S + (G - f * lf - S).normalized * lu;
                ua.rotation = Quaternion.FromToRotation(la.position - S, E - S) * ua.rotation;
                la.rotation = Quaternion.FromToRotation(hand.position - la.position, f) * la.rotation;
                hand.rotation = Hd; delta = Quaternion.identity;
            }
            hand.rotation = delta * hand.rotation;
            if (la)
            {
                var mid = _anim.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
                if (mid) WristBendDeg = Vector3.Angle(hand.position - la.position, mid.position - hand.position);
            }
            if (_flash) BeamErrorDeg = Vector3.Angle(v.transform.forward, aim);
        }
    }
}
