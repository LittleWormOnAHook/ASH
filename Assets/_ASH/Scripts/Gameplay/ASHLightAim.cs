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
    /// restored on release). All poses blend with ikBlendSpeed.
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
        [Tooltip("Aiming: hand distance from the shoulder along the aim direction.")]
        [Range(0.2f, 0.9f)] public float aimReach = 0.55f;
        [Range(-0.5f, 0.5f)] public float aimRaise = 0.05f;
        [Tooltip("Elbow hint from the left shoulder, character space.")]
        public Vector3 elbowHintOffset = new Vector3(-0.35f, -0.2f, -0.15f);
        [Tooltip("How far a torch tilts toward the aim direction (0 = straight up).")]
        [Range(0f, 1f)] public float torchAimTilt = 0.35f;
        [Tooltip("How fully the held light turns to the pose (torch upright / beam on the aim line).")]
        [Range(0f, 1f)] public float itemAlignWeight = 1f;

        public bool Aiming { get; private set; }
        public float Weight => _w;
        public bool TorchRaised => _torch && _w > 0.5f;
        public int IKCalls { get; private set; }

        Animator _anim; ASHEquipment _eq; ASHInput _in; vThirdPersonController _cc; Camera _cam;
        float _w; bool _torch, _flash;
        GameObject _visual; Quaternion _visualLocal;
        vThirdPersonMotor.LocomotionType _sLoco; bool _sStrafe, _sRotCam; float _sRotSpeed; bool _saved;

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
            if (on)
            {
                _sLoco = _cc.locomotionType; _sStrafe = _cc.isStrafing; _sRotCam = _cc.strafeSpeed.rotateWithCamera; _sRotSpeed = _cc.strafeSpeed.rotationSpeed; _saved = true;
                _cc.locomotionType = vThirdPersonMotor.LocomotionType.FreeWithStrafe; _cc.isStrafing = true;
                _cc.strafeSpeed.rotateWithCamera = true; _cc.strafeSpeed.rotationSpeed = aimRotationSpeed;
            }
            else if (_saved)
            {
                _cc.locomotionType = _sLoco; _cc.isStrafing = _sStrafe; _cc.strafeSpeed.rotateWithCamera = _sRotCam; _cc.strafeSpeed.rotationSpeed = _sRotSpeed; _saved = false;
            }
        }

        void OnDisable() { if (Aiming) SetAim(false); }

        void OnAnimatorIK(int layer)
        {
            IKCalls++;
            if (layer != 0 || !_anim || !_anim.isHuman || _w <= 0.001f) return;
            var shoulderT = _anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); if (!shoulderT) return;
            Vector3 sh = shoulderT.position, aim = AimDir;
            Vector3 goal = Aiming ? sh + aim * aimReach + Vector3.up * aimRaise : sh + Root.TransformDirection(torchRaisedOffset);
            if (Aiming && _torch) goal += Vector3.up * Mathf.Max(0f, torchRaisedOffset.y * 0.5f);
            _anim.SetIKPositionWeight(AvatarIKGoal.LeftHand, _w);
            _anim.SetIKPosition(AvatarIKGoal.LeftHand, goal);
            _anim.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, _w);
            _anim.SetIKHintPosition(AvatarIKHint.LeftElbow, sh + Root.TransformDirection(elbowHintOffset));
        }

        void LateUpdate()
        {
            var v = _eq ? _eq.OffVisual : null;
            if (v != _visual) { _visual = v; if (v) _visualLocal = v.transform.localRotation; }
            if (!v || !v.transform.parent) return;
            var attached = v.transform.parent.rotation * _visualLocal;
            float k = Mathf.Clamp01(_w) * itemAlignWeight;
            if (k <= 0.001f) { v.transform.rotation = attached; return; }
            Vector3 aim = AimDir, up = Vector3.up, fwd = Vector3.ProjectOnPlane(Root.forward, up).normalized;
            Quaternion desired = _flash ? Quaternion.LookRotation(aim, up)
                : Quaternion.LookRotation(Vector3.Slerp(up, aim, Aiming ? torchAimTilt : 0f).normalized, fwd);
            v.transform.rotation = Quaternion.Slerp(attached, desired, k);
        }
    }
}
