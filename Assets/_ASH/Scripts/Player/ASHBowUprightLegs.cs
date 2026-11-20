using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// Bow stance fix (t323/t327). Invector's bow move set drops the base layer into a bent-knee strafe idle while aiming
    /// (hips ~10 cm lower, knees ~50 deg); the bow's aim animation and IK need that set (its side-on chest), so it stays,
    /// and only the legs are stood up: while bow-aiming and standing still, the hips height and the leg bones are put back to
    /// the standing pose (cached while not aiming). Runs before Invector's aim IK (order -90), from the raw animated pose
    /// every frame (the Animator is on Fixed, so stale frames would otherwise stack). Added at runtime by ASHCombatBridge.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-90)]
    public sealed class ASHBowUprightLegs : MonoBehaviour
    {
        static readonly HumanBodyBones[] Legs =
        {
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
        };
        /// <summary>Set by ASHCombatBridge each frame: 0..1 (bow aimed and standing still, times the tuning weight).</summary>
        [System.NonSerialized] public float targetWeight;
        /// <summary>Set by ASHCombatBridge: the character is standing still (the standing reference is only cached then).</summary>
        [System.NonSerialized] public bool standingStill;
        [Range(0.5f, 30f)] public float blendSpeed = 8f;
        public float Weight => _w; public bool HasStand => _hasStand; public float StandHipsHeight => _standHipsY;

        Animator _a; Transform _hips; Transform[] _b; Quaternion[] _stand, _raw; Vector3 _rawHips; float _standHipsY, _w;
        bool _hasStand; int _ticks, _seen = -1;

        void Awake()
        {
            _a = GetComponent<Animator>();
            if (!_a || !_a.isHuman) { enabled = false; return; }
            _hips = _a.GetBoneTransform(HumanBodyBones.Hips);
            int n = 0; var tmp = new Transform[Legs.Length];
            foreach (var id in Legs) { var t = _a.GetBoneTransform(id); if (t) tmp[n++] = t; }
            _b = new Transform[n]; System.Array.Copy(tmp, _b, n); _stand = new Quaternion[n]; _raw = new Quaternion[n];
            if (!_hips) enabled = false;
        }

        void FixedUpdate() { _ticks++; }

        void LateUpdate()
        {
            bool fresh = _ticks != _seen || _a.updateMode != AnimatorUpdateMode.Fixed; _seen = _ticks;
            if (fresh)
            {
                for (int i = 0; i < _b.Length; i++) _raw[i] = _b[i].localRotation;
                _rawHips = _hips.localPosition;
                // standing reference: whenever nothing is pulling the legs up and the bow is not aimed
                if (standingStill && targetWeight <= 0.001f && _w <= 0.001f)
                {
                    for (int i = 0; i < _b.Length; i++) _stand[i] = _raw[i];
                    _standHipsY = _hips.position.y - transform.position.y; _hasStand = true;
                }
            }
            _w = Mathf.MoveTowards(_w, _hasStand ? Mathf.Clamp01(targetWeight) : 0f, blendSpeed * Time.deltaTime);
            if (_w <= 0.001f || !_hasStand) return;
            for (int i = 0; i < _b.Length; i++) _b[i].localRotation = Quaternion.Slerp(_raw[i], _stand[i], _w);
            _hips.localPosition = _rawHips;
            float lift = Mathf.Max(0f, _standHipsY - (_hips.position.y - transform.position.y));
            if (lift > 0f) _hips.position += Vector3.up * (lift * _w);
        }
    }
}