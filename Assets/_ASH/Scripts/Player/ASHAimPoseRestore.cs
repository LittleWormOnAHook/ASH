using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// Per-frame aim IK support (aim jitter fix). Invector runs the player Animator in the physics step, so on rendered frames
    /// without a physics step the bones still hold last frame's final pose (animation + aim IK). ASHInvectorInput now runs
    /// Invector's aim IK every rendered frame while a gun or the bow is out; before it does (order -100), this puts the raw
    /// animated pose from the last physics step back on the upper-body bones, so the IK never stacks on its own result.
    /// Added at runtime by ASHInvectorInput to the Animator object. Does nothing while restoreOnStaleFrames is off.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class ASHAimPoseRestore : MonoBehaviour
    {
        static readonly HumanBodyBones[] Ids =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
        };
        [System.NonSerialized] public bool restoreOnStaleFrames;
        Animator _a; Transform[] _b; Quaternion[] _r; Vector3[] _p; int _ticks, _seen = -1; bool _has;
        /// <summary>True when the Animator evaluated since the last LateUpdate (a physics step ran, or it is not on Fixed).</summary>
        public bool FreshThisFrame { get; private set; }

        void Awake()
        {
            _a = GetComponent<Animator>();
            if (!_a || !_a.isHuman) { enabled = false; return; }
            int n = 0; var tmp = new Transform[Ids.Length];
            foreach (var id in Ids) { var t = _a.GetBoneTransform(id); if (t) tmp[n++] = t; }
            _b = new Transform[n]; System.Array.Copy(tmp, _b, n); _r = new Quaternion[n]; _p = new Vector3[n];
        }

        void FixedUpdate() { _ticks++; }

        void LateUpdate()
        {
            FreshThisFrame = _ticks != _seen || _a.updateMode != AnimatorUpdateMode.Fixed; _seen = _ticks;
            if (FreshThisFrame || !_has)
            {
                for (int i = 0; i < _b.Length; i++) { _r[i] = _b[i].localRotation; _p[i] = _b[i].localPosition; }
                _has = true; return;
            }
            if (!restoreOnStaleFrames) return;
            for (int i = 0; i < _b.Length; i++) { _b[i].localRotation = _r[i]; _b[i].localPosition = _p[i]; }
        }
    }
}
