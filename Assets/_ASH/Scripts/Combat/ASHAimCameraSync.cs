using ASH.Gameplay;
using ASH.Player;
using UnityEngine;

namespace ASH.Combat
{
    /// <summary>
    /// Rifle / pistol aim jitter fix (t336; toggle ASHCombatTuning.aimPoseInterpolation; the bow is never touched).
    /// Invector runs the player Animator and its aim IK in the physics step, and the body Rigidbody is interpolated, so the body
    /// is drawn one physics step behind, blended smoothly, while the arms and gun showed the newest solved pose and stepped
    /// whenever a rendered frame had zero or two physics steps (measured: jerk 3x larger at 64 fps than at 56 fps with 60 Hz
    /// physics). This keeps the last two solved upper-body poses (after the IK, as ASHPoseGuard restores them at 1600) and
    /// draws them at the same interpolated time as the body, so body, arms and gun move together every frame.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1605)]
    public sealed class ASHAimCameraSync : MonoBehaviour
    {
        /// <summary>Test override: -1 = tuning toggle, 0 = off, 1 = on (rifle / pistol), 2 = every state (empty hands, held items; not the aimed bow).</summary>
        public static int ForceMode = -1;
        /// <summary>Test override: -1 = tuning, 1 = every ranged weapon held (aiming or not), 0 = rifle / pistol aiming only.</summary>
        public static int ForceWhileHeld = -1;
        public float Weight => _w;
        public int Solves { get; private set; }

        static readonly HumanBodyBones[] Ids =
        {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
        };
        Animator _a; Transform[] _b; Quaternion[] _r0, _r1; Vector3[] _p0, _p1; float _t0 = -1f, _t1 = -1f; int _fixedFrame = -1; float _w;
        ASHWeaponAim _aim; ASHInvectorInput _in;

        void Awake()
        {
            _a = GetComponent<Animator>();
            if (!_a || !_a.isHuman) { enabled = false; return; }
            int n = 0; var tmp = new Transform[Ids.Length];
            foreach (var id in Ids) { var t = _a.GetBoneTransform(id); if (t) tmp[n++] = t; }
            _b = new Transform[n]; System.Array.Copy(tmp, _b, n);
            _r0 = new Quaternion[n]; _r1 = new Quaternion[n]; _p0 = new Vector3[n]; _p1 = new Vector3[n];
        }

        void FixedUpdate() { _fixedFrame = Time.frameCount; }

        void LateUpdate()
        {
            if (_b == null || _b.Length == 0) return;
            bool fixedAnim = _a.updateMode == AnimatorUpdateMode.Fixed;
            if (!fixedAnim) { _t0 = _t1 = -1f; return; }
            if (_fixedFrame == Time.frameCount)
            {
                // new solved pose this frame (Animator + Invector aim IK ran): shift the history
                for (int i = 0; i < _b.Length; i++) { _r0[i] = _r1[i]; _p0[i] = _p1[i]; _r1[i] = _b[i].localRotation; _p1[i] = _b[i].localPosition; }
                _t0 = _t1; _t1 = Time.fixedTime; Solves++;
                if (_t0 < 0f) return;
            }
            if (_t0 < 0f || _t1 <= _t0) return;

            if (!_aim) { _aim = GetComponentInParent<ASHWeaponAim>(); if (!_aim) _aim = transform.root.GetComponentInChildren<ASHWeaponAim>(); }
            if (!_in) { _in = GetComponentInParent<ASHInvectorInput>(); if (!_in) _in = transform.root.GetComponentInChildren<ASHInvectorInput>(); }
            var t = ASHCombatTuning.Live;
            bool on = ForceMode >= 0 ? ForceMode == 1 : t.aimPoseInterpolation;
            var k = _aim ? _aim.Current : ASHWeaponAim.Kind.None;
            bool gun = k == ASHWeaponAim.Kind.Rifle || k == ASHWeaponAim.Kind.Pistol;
            bool aiming = _in && _in.IsAiming;
            bool held = ForceWhileHeld >= 0 ? ForceWhileHeld == 1 : t.aimPoseInterpolationWhileHeld;
            bool all = ForceMode == 2 || (ForceMode < 0 && t.poseInterpolationAlways);
            bool bow = k == ASHWeaponAim.Kind.Bow;
            bool want = all ? !bow || !aiming : on && gun && (aiming || held);
            _w = Mathf.MoveTowards(_w, want ? 1f : 0f, Time.deltaTime * 6f);
            if (_w <= 0f) return;

            // the body Rigidbody is drawn at (Time.time - fixedDeltaTime): draw the solved poses at the same moment
            float at = Time.time - Time.fixedDeltaTime;
            float a = Mathf.Clamp01((at - _t0) / (_t1 - _t0));
            for (int i = 0; i < _b.Length; i++)
            {
                var r = Quaternion.Slerp(_r0[i], _r1[i], a); var p = Vector3.Lerp(_p0[i], _p1[i], a);
                if (_w < 1f) { r = Quaternion.Slerp(_r1[i], r, _w); p = Vector3.Lerp(_p1[i], p, _w); }
                _b[i].localRotation = r; _b[i].localPosition = p;
            }
        }
    }
}
