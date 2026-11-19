using UnityEngine;

namespace ASH.Combat
{
    /// <summary>On Held_WoodenBow (made by ASH/Systems/Combat/3 Fix Ranged Grips, tuned by
    /// ASH/Systems/Combat/Tune Bow Draw). The ASH bow has no string bone, so this stands in
    /// for Invector's bow rig: the drawing-hand IK target (Invector vShooterWeapon.handIKTarget) slides from the string at rest
    /// to the full-draw hand pose (handFullLocal from the authored String_R, pulled in by anchorIn) with the bow power,
    /// and the nock rides the string back by the same amount (ASHWeaponAim lays the nocked arrow from the nock to the rest
    /// and pulls the string mesh to it). All values are in the bow's local space and can be tuned in the Inspector.
    /// The pack bow is about 1.48 m tip-to-tip at scale 1 (tall end of a longbow); bowScale 0.88 makes it ~1.30 m so a
    /// cheek-anchor string pull fits the character's arms. Scale is applied around Grip_L so the left-hand placement is kept.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHBowDrawRig : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Invector handIKTarget of this bow (right hand on the string).")]
        public Transform handTarget;

        [Header("Authored poses (bow local; written by the grip / tune menus)")]
        [Tooltip("Right-hand bone pose at the authored full-draw String_R (bow local). The live target adds anchorIn.")]
        public Vector3 handFullLocal;
        public Quaternion handRotLocal = Quaternion.identity;
        [Tooltip("Nocking point on the string at rest (bow local).")]
        public Vector3 nockRestLocal;
        [Tooltip("Arrow rest on the riser (bow local). Should share Y with the nock so the arrow sits in the string plane.")]
        public Vector3 restLocal;
        [Tooltip("Draw direction (bow local, from the bow toward the archer).")]
        public Vector3 backLocal = Vector3.right;

        [Header("Draw (tune in the Inspector; wide ranges)")]
        [Tooltip("World scale of the held bow. 1 = pack size (~1.48 m longbow). 0.88 (~1.30 m) shortens the string pull to a cheek anchor.")]
        [Range(0.5f, 1.5f)] public float bowScale = 0.88f;
        [Tooltip("How far the string and the nock travel from rest to full draw (metres, bow local).")]
        [Range(0.05f, 1f)] public float drawLength = 0.30f;
        [Tooltip("Metres the drawing hand is pulled toward the bow from the authored String_R pose. Increase to stop the hand earlier (cheek / jaw), decrease to draw further back.")]
        [Range(-0.25f, 0.5f)] public float anchorIn = 0.10f;
        [Tooltip("Extra full-draw hand offset (bow local) after anchorIn. Y = sideways off the string plane, Z = along the limbs.")]
        public Vector3 anchorOffsetLocal;

        [Header("Arrow nock and rest offsets (bow local, added to the computed points)")]
        [Tooltip("Nock point offset. X = along the draw (toward the archer), Y = sideways off the string plane, Z = along the limbs.")]
        public Vector3 nockOffsetLocal;
        [Tooltip("Arrow rest offset. X = along the draw, Y = sideways off the string plane, Z = along the limbs.")]
        public Vector3 restOffsetLocal;

        [Header("Idle (equipped, not aiming)")]
        [Tooltip("Relax Invector bow-ready animator layers while idle so both arms hang at the sides. The left hand still holds the bow.")]
        public bool relaxIdleAnimator = true;
        [Tooltip("Upper-body / arm layer weight while idle. 0 = arms at the sides.")]
        [Range(0f, 1f)] public float idleUpperBodyWeight = 0f;
        [Tooltip("Layer names to relax (substring, case-insensitive). Leave empty to auto-pick Upper / Arm / Bow / Overlay / Shooter layers.")]
        public string[] idleRelaxLayers;
        [Tooltip("Hang the Model child so the bow points down beside the leg. Grip_L is not moved.")]
        public bool idleHangVertical = true;
        [Tooltip("Bow-local axis that should point world-down when hanging (limb axis).")]
        public Vector3 idleHangAxis = Vector3.up;
        [Tooltip("Extra Euler on the Model after hanging (degrees).")]
        public Vector3 idleHangEuler;

        [System.NonSerialized] public float draw01;

        Animator _idleAnim;
        float[] _baseWeights;
        bool _weightsCaptured, _idleOn;
        Transform _model;
        Quaternion _modelBaseLocal;
        bool _modelCaptured;

        Vector3 Back => backLocal.sqrMagnitude > 1e-8f ? backLocal.normalized : Vector3.right;

        /// <summary>Full-draw hand bone in bow local (authored String_R, pulled in to the cheek by anchorIn).</summary>
        public Vector3 HandFullEffective => handFullLocal - Back * anchorIn + anchorOffsetLocal;
        public Vector3 NockRestEffective => nockRestLocal + nockOffsetLocal;
        public Vector3 RestEffective => restLocal + restOffsetLocal;

        public Vector3 NockWorld => transform.TransformPoint(NockRestEffective + Back * (drawLength * draw01));
        public Vector3 RestWorld => transform.TransformPoint(RestEffective);

        /// <summary>Moves the hand IK target for the current draw (call before Invector's IK, i.e. in Update).</summary>
        public void Apply()
        {
            if (!handTarget) return;
            handTarget.localPosition = HandFullEffective - Back * (drawLength * (1f - Mathf.Clamp01(draw01)));
            handTarget.localRotation = handRotLocal;
        }

        public void RestoreIdle(Animator anim)
        {
            if (_idleOn && anim && _weightsCaptured && _baseWeights != null)
            {
                int n = Mathf.Min(_baseWeights.Length, anim.layerCount);
                for (int i = 0; i < n; i++) anim.SetLayerWeight(i, _baseWeights[i]);
            }
            if (_model && _modelCaptured) _model.localRotation = _modelBaseLocal;
            _idleOn = false;
        }

        public void ApplyIdle(Animator anim, bool aiming)
        {
            if (aiming || !isActiveAndEnabled) { RestoreIdle(anim); return; }
            if (relaxIdleAnimator && anim)
            {
                if (!_weightsCaptured || _idleAnim != anim) CaptureWeights(anim);
                for (int i = 1; i < anim.layerCount; i++)
                    if (ShouldRelax(anim.GetLayerName(i)))
                        anim.SetLayerWeight(i, idleUpperBodyWeight);
            }
            HangModel();
            _idleOn = true;
        }

        void OnDisable() { if (_idleOn) RestoreIdle(_idleAnim); }

        void CaptureWeights(Animator anim)
        {
            _idleAnim = anim;
            _baseWeights = new float[anim.layerCount];
            for (int i = 0; i < anim.layerCount; i++) _baseWeights[i] = anim.GetLayerWeight(i);
            _weightsCaptured = true;
        }

        bool ShouldRelax(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (idleRelaxLayers != null && idleRelaxLayers.Length > 0)
            {
                foreach (var s in idleRelaxLayers)
                    if (!string.IsNullOrEmpty(s) && name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            }
            string n = name.ToLowerInvariant();
            if (n.Contains("base") || n.Contains("locomotion") || n.Contains("full body") || n.Contains("fullbody")) return false;
            return n.Contains("upper") || n.Contains("arm") || n.Contains("bow") || n.Contains("overlay")
                || n.Contains("shooter") || n.Contains("aim") || n.Contains("item") || n.Contains("hand") || n.Contains("ik");
        }

        void HangModel()
        {
            if (!idleHangVertical) { if (_model && _modelCaptured) _model.localRotation = _modelBaseLocal; return; }
            if (!_model)
            {
                _model = transform.Find("Model");
                if (!_model)
                {
                    foreach (Transform t in transform)
                    {
                        string n = t.name;
                        if (n.StartsWith("Grip") || n.StartsWith("String") || n.StartsWith("Arrow") || n.StartsWith("Nock") || (handTarget && t == handTarget)) continue;
                        _model = t; break;
                    }
                }
                if (_model && !_modelCaptured) { _modelBaseLocal = _model.localRotation; _modelCaptured = true; }
            }
            if (!_model) return;
            if (_modelCaptured) _model.localRotation = _modelBaseLocal;
            Vector3 axis = idleHangAxis.sqrMagnitude > 1e-8f ? idleHangAxis.normalized : Vector3.up;
            Vector3 axisWorld = _model.TransformDirection(axis);
            _model.rotation = Quaternion.FromToRotation(axisWorld, Vector3.down) * _model.rotation;
            if (idleHangEuler.sqrMagnitude > 1e-8f) _model.Rotate(idleHangEuler, Space.Self);
        }
    }
}
