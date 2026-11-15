using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Keeps after-animation bone edits (aim alignment, bow body turn, rifle blade, head look, recoil, light wrist) from stacking.
    /// The player Animator runs in the physics step (Invector sets AnimatePhysics), so on rendered frames without a physics step
    /// it does not re-pose the bones and last frame's edits are still there; relative edits (e.g. rotate the spine 35 deg) would
    /// then apply twice and the held item visibly jitters. Before the edit scripts run (order 1600) this puts back the raw
    /// animated pose on any bone the Animator did not overwrite; ASHPoseGuardCapture (order 1900) records the final pose.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1600)]
    public sealed class ASHPoseGuard : MonoBehaviour
    {
        static readonly HumanBodyBones[] Ids =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
        };
        Transform[] _b; Quaternion[] _animR, _finalR; Vector3[] _animP, _finalP; bool _hasFinal;
        /// <summary>Bones restored this frame (the Animator skipped them) - diagnostics.</summary>
        public int RestoredLastFrame { get; private set; }
        public int RestoredTotal { get; private set; }

        void Awake()
        {
            var a = GetComponent<Animator>();
            if (!a || !a.isHuman) { enabled = false; return; }
            int n = 0; var tmp = new Transform[Ids.Length];
            foreach (var id in Ids) { var t = a.GetBoneTransform(id); if (t) tmp[n++] = t; }
            _b = new Transform[n]; System.Array.Copy(tmp, _b, n);
            _animR = new Quaternion[n]; _finalR = new Quaternion[n]; _animP = new Vector3[n]; _finalP = new Vector3[n];
            if (!GetComponent<ASHPoseGuardCapture>()) gameObject.AddComponent<ASHPoseGuardCapture>();
        }

        void LateUpdate()
        {
            RestoredLastFrame = 0;
            for (int i = 0; i < _b.Length; i++)
            {
                var t = _b[i];
                if (_hasFinal && t.localRotation == _finalR[i] && t.localPosition == _finalP[i]) { t.localRotation = _animR[i]; t.localPosition = _animP[i]; RestoredLastFrame++; }
                _animR[i] = t.localRotation; _animP[i] = t.localPosition;
            }
            RestoredTotal += RestoredLastFrame;
        }

        internal void Capture()
        {
            if (_b == null) return;
            for (int i = 0; i < _b.Length; i++) { _finalR[i] = _b[i].localRotation; _finalP[i] = _b[i].localPosition; }
            _hasFinal = true;
        }
    }

    /// <summary>Records the final bone pose after every after-animation edit (see ASHPoseGuard).</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1900)]
    public sealed class ASHPoseGuardCapture : MonoBehaviour
    {
        ASHPoseGuard _g;
        void Awake() { _g = GetComponent<ASHPoseGuard>(); }
        void LateUpdate() { if (_g && _g.enabled) _g.Capture(); }
    }
}
