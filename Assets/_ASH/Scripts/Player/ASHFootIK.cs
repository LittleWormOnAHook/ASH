using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// Per-leg foot IK for slopes and uneven ground (Humanoid, OnAnimatorIK; needs IK Pass on the base layer of the ASH
    /// animator copy). Each foot raycasts the ground (terrain/colliders, under the visual ash surface), is placed at the ground
    /// height plus its animated lift and rotated to the ground normal; the pelvis drops to the lower foot. Weights come from
    /// animator float parameters (LeftFootIK/RightFootIK) when present, otherwise a stance-phase heuristic (foot height).
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(1500)]
    public sealed class ASHFootIK : MonoBehaviour
    {
        public enum Preset { Subtle, Default, Extreme }

        [Tooltip("Master toggle.")] public bool ikEnabled = true;
        [Tooltip("Ground layers (Player and Ignore Raycast excluded by default).")] public LayerMask groundLayers = ~((1 << 13) | (1 << 2));
        [Header("Rays")]
        [Range(0.1f, 2f)] public float rayStartAbove = 0.6f;
        [Range(0.1f, 3f)] public float rayLengthBelow = 0.9f;
        [Header("Placement")]
        [Tooltip("Max pelvis drop to reach the lower foot, metres.")] [Range(0f, 1f)] public float maxPelvisDrop = 0.4f;
        [Tooltip("Max pelvis raise (root sunk below ground), metres.")] [Range(0f, 0.3f)] public float maxPelvisRaise = 0.05f;
        [Tooltip("Extra sole offset above the hit point (+ lifts feet, - sinks).")] [Range(-0.1f, 0.2f)] public float footOffset = 0f;
        [Range(0f, 1f)] public float positionWeight = 1f;
        [Range(0f, 1f)] public float rotationWeight = 1f;
        [Tooltip("Ignore ground normals steeper than this for foot rotation.")] [Range(0f, 75f)] public float maxFootAngle = 45f;
        [Header("Smoothing (higher = snappier)")]
        [Range(1f, 60f)] public float footSmoothing = 18f;
        [Range(1f, 60f)] public float pelvisSmoothing = 10f;
        [Range(1f, 60f)] public float rotationSmoothing = 16f;
        [Header("Weights")]
        [Tooltip("Animator float parameters used as IK weights if they exist.")] public string leftCurve = "LeftFootIK", rightCurve = "RightFootIK";
        [Tooltip("Heuristic: foot lifted this high (m) by the animation = swing, IK weight 0.")] [Range(0.02f, 0.6f)] public float swingHeight = 0.2f;
        [Tooltip("Fade IK out while airborne / sliding.")] public bool onlyWhenGrounded = true;
        public bool debugDraw;

        Animator _a; vThirdPersonMotor _motor; bool _hasL, _hasR;
        float _pelvis, _lOff, _rOff, _master; Vector3 _lAnim, _rAnim; Quaternion _lAnimRot, _rAnimRot; Quaternion _lRot = Quaternion.identity, _rRot = Quaternion.identity;
        public float PelvisOffset => _pelvis;

        public void ApplyPreset(Preset p)
        {
            switch (p)
            {
                case Preset.Subtle: maxPelvisDrop = 0.2f; maxFootAngle = 25f; rotationWeight = 0.6f; footSmoothing = 12f; pelvisSmoothing = 7f; break;
                case Preset.Default: maxPelvisDrop = 0.4f; maxFootAngle = 45f; rotationWeight = 1f; footSmoothing = 18f; pelvisSmoothing = 10f; break;
                case Preset.Extreme: maxPelvisDrop = 0.8f; maxFootAngle = 70f; rotationWeight = 1f; footSmoothing = 40f; pelvisSmoothing = 25f; rayLengthBelow = 1.6f; rayStartAbove = 1f; break;
            }
        }
        [ContextMenu("Preset/Subtle")] void PS() => ApplyPreset(Preset.Subtle);
        [ContextMenu("Preset/Default")] void PD() => ApplyPreset(Preset.Default);
        [ContextMenu("Preset/Extreme")] void PE() => ApplyPreset(Preset.Extreme);

        void Awake()
        {
            _a = GetComponent<Animator>(); _motor = GetComponent<vThirdPersonMotor>();
            foreach (var p in _a.parameters) { if (p.type != AnimatorControllerParameterType.Float) continue; if (p.name == leftCurve) _hasL = true; if (p.name == rightCurve) _hasR = true; }
        }

        void OnAnimatorIK(int layer)
        {
            if (layer != 0 || !_a || !_a.isHuman) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            bool active = ikEnabled && (!onlyWhenGrounded || !_motor || (_motor.isGrounded && !_motor.isSliding));
            _master = Mathf.MoveTowards(_master, active ? 1f : 0f, dt * 5f);
            if (_master <= 0.001f) { _pelvis = Mathf.Lerp(_pelvis, 0f, dt * pelvisSmoothing); return; }

            _lAnim = _a.GetIKPosition(AvatarIKGoal.LeftFoot); _lAnimRot = _a.GetIKRotation(AvatarIKGoal.LeftFoot);
            _rAnim = _a.GetIKPosition(AvatarIKGoal.RightFoot); _rAnimRot = _a.GetIKRotation(AvatarIKGoal.RightFoot);
            Solve(_lAnim, _hasL ? _a.GetFloat(leftCurve) : -1f, _a.leftFeetBottomHeight, ref _lOff, ref _lRot, out float lDelta, out float lW, out bool lHit);
            Solve(_rAnim, _hasR ? _a.GetFloat(rightCurve) : -1f, _a.rightFeetBottomHeight, ref _rOff, ref _rRot, out float rDelta, out float rW, out bool rHit);

            float target = 0f;
            if (lHit || rHit) target = Mathf.Min(lHit ? lDelta : float.MaxValue, rHit ? rDelta : float.MaxValue);
            target = Mathf.Clamp(target, -maxPelvisDrop, maxPelvisRaise) * _master;
            _pelvis = Mathf.Lerp(_pelvis, target, dt * pelvisSmoothing);
            _a.bodyPosition += transform.up * _pelvis;

            Apply(AvatarIKGoal.LeftFoot, _lAnim, _lAnimRot, _lOff, _lRot, lW);
            Apply(AvatarIKGoal.RightFoot, _rAnim, _rAnimRot, _rOff, _rRot, rW);
        }

        // delta = how far the foot must move vertically (world) to sit on the ground; also used for the pelvis
        void Solve(Vector3 animPos, float curve, float bottom, ref float off, ref Quaternion rotCorr, out float delta, out float weight, out bool hit)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var up = transform.up; float rootH = Vector3.Dot(transform.position, up);
            float lift = Mathf.Max(0f, Vector3.Dot(animPos, up) - rootH - bottom);
            weight = curve >= 0f ? Mathf.Clamp01(curve) : 1f - Mathf.Clamp01((lift - 0.02f) / Mathf.Max(swingHeight, 0.01f));
            var origin = animPos - up * (Vector3.Dot(animPos, up) - rootH) + up * rayStartAbove;
            hit = Physics.Raycast(origin, -up, out var h, rayStartAbove + rayLengthBelow, groundLayers, QueryTriggerInteraction.Ignore);
            delta = 0f; var targetRot = Quaternion.identity;
            if (hit)
            {
                float groundH = Vector3.Dot(h.point, up);
                float want = groundH + bottom + lift + footOffset;
                delta = want - Vector3.Dot(animPos, up);
                var n = h.normal; float ang = Vector3.Angle(up, n);
                if (ang <= maxFootAngle) targetRot = Quaternion.FromToRotation(up, n);
                if (debugDraw) { Debug.DrawLine(origin, h.point, Color.yellow); Debug.DrawRay(h.point, n * 0.3f, Color.cyan); }
            }
            off = Mathf.Lerp(off, delta, dt * footSmoothing);
            rotCorr = Quaternion.Slerp(rotCorr, targetRot, dt * rotationSmoothing);
            if (!hit) weight = 0f;
        }

        // absolute world target (animated goal + ground correction); the pelvis moves independently and the legs bend to reach
        void Apply(AvatarIKGoal goal, Vector3 animPos, Quaternion animRot, float off, Quaternion rotCorr, float w)
        {
            var pos = animPos + transform.up * off;
            float pw = w * positionWeight * _master, rw = w * rotationWeight * _master;
            _a.SetIKPositionWeight(goal, pw); _a.SetIKPosition(goal, pos);
            _a.SetIKRotationWeight(goal, rw); _a.SetIKRotation(goal, rotCorr * animRot);
        }
    }
}
