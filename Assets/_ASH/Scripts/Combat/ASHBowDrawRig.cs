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
        [Tooltip("Pulls the full-draw hand sideways onto the arrow line (string plane, behind the nock). 0 = authored hand position, 1 = on the line.")]
        [Range(0f, 1f)] public float handOnArrowLine = 1f;
        [Tooltip("Sideways offset of the draw hand from the arrow line after handOnArrowLine (metres, bow local Y; flip the sign for the other side).")]
        [Range(-0.2f, 0.2f)] public float handLineSide = 0.07f;

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
        [Tooltip("Model-local axis that should point world-down when hanging. The pack bow's limbs run along Z (0,0,1); Y is the sideways axis off the string plane (that left the bow horizontal).")]
        public Vector3 idleHangAxis = Vector3.forward;
        [Tooltip("Use whichever end of idleHangAxis is already lower, so the bow takes the smallest turn to hang down.")]
        public bool idleHangAutoFlip = true;
        [Tooltip("Extra Euler on the Model after hanging (degrees).")]
        public Vector3 idleHangEuler;
        [Tooltip("Authored local rotation of the Model child (the bow mesh). The aim pose puts the Model back to this; the idle hang starts from it. Never captured at runtime, so a hung pose saved by mistake cannot leak into the aim pose.")]
        public Vector3 modelRestEuler = Vector3.zero;

        [System.NonSerialized] public float draw01;

        /// <summary>Set by the editor (ASHBowTuning): keeps this rig's values after Play by writing them to ASH_Player.prefab.</summary>
        public static System.Action<ASHBowDrawRig> SaveTuningHook;
        /// <summary>Tune the fields above live in Play, then use this (component menu) or ASH/Systems/Combat/Bow Tuning Window.</summary>
        [ContextMenu("Save Bow Tuning (keep after Play)")]
        void SaveBowTuning() { if (SaveTuningHook != null) SaveTuningHook(this); else Debug.LogWarning("[ASH] Save Bow Tuning works in the Editor only."); }

        Animator _idleAnim;
        float[] _baseWeights;
        bool _weightsCaptured, _idleOn;
        Transform _model;
        Quaternion ModelRest => Quaternion.Euler(modelRestEuler);

        Vector3 Back => backLocal.sqrMagnitude > 1e-8f ? backLocal.normalized : Vector3.right;

        /// <summary>Full-draw hand bone in bow local (authored String_R, pulled in to the cheek by anchorIn).</summary>
        public Vector3 HandFullEffective { get { var h = handFullLocal - Back * anchorIn + anchorOffsetLocal; h.y = Mathf.Lerp(h.y, NockRestEffective.y + handLineSide, Mathf.Clamp01(handOnArrowLine)); return h; } }
        public Vector3 NockRestEffective => nockRestLocal + nockOffsetLocal;
        public Vector3 RestEffective => restLocal + restOffsetLocal;

        public Vector3 NockWorld => transform.TransformPoint(NockRestEffective + Back * (drawLength * draw01));
        public Vector3 RestWorld => transform.TransformPoint(RestEffective);

        /// <summary>Moves the hand IK target for the current draw (call before Invector's IK, i.e. in Update).</summary>
        public void Apply()
        {
            if (!handTarget) return;
            bool helper = useRightHandHelper && helperTransform && helperTransform.parent == handTarget.parent;
            var full = helper ? helperTransform.localPosition : HandFullEffective;
            handTarget.localPosition = full - Back * (drawLength * (1f - Mathf.Clamp01(draw01)));
            handTarget.localRotation = helper ? helperTransform.localRotation : handRotLocal;
        }

        // ---- Right-hand helper (t353): a draggable full-draw hand pose, tuned in Play (paused or running) ----
        /// <summary>While on, the draw-hand IK target follows RightHand_Helper (full-draw pose; the draw still slides it along the string).</summary>
        [System.NonSerialized] public bool useRightHandHelper;
        [System.NonSerialized] public Transform helperTransform;

        /// <summary>Creates (or re-places at the current rig values when reset) the helper next to the hand IK target.</summary>
        public Transform EnsureHelper(bool reset = false)
        {
            if (!handTarget) return null;
            if (!helperTransform) { helperTransform = new GameObject("RightHand_Helper").transform; reset = true; }
            if (helperTransform.parent != handTarget.parent) { helperTransform.SetParent(handTarget.parent, false); reset = true; }
            if (reset) { helperTransform.localPosition = HandFullEffective; helperTransform.localRotation = handRotLocal; helperTransform.localScale = Vector3.one; }
            return helperTransform;
        }

        /// <summary>Writes the helper pose into the rig fields (anchorOffsetLocal / handLineSide, handRotLocal) so the rig reproduces it without the helper.</summary>
        public void BakeHelper()
        {
            if (!helperTransform) return;
            var want = helperTransform.localPosition;
            var baseP = handFullLocal - Back * anchorIn;
            float w = Mathf.Clamp01(handOnArrowLine);
            var off = want - baseP;
            if (w > 0.999f) { handLineSide = want.y - NockRestEffective.y; off.y = anchorOffsetLocal.y; }
            else off.y = (want.y - w * (NockRestEffective.y + handLineSide)) / (1f - w) - baseP.y;
            anchorOffsetLocal = off;
            handRotLocal = helperTransform.localRotation;
        }

        /// <summary>Paused-Play preview: moves the hand IK target and solves the right arm (two-bone) to it so the pose updates live.</summary>
        public void PreviewArm(Animator anim)
        {
            Apply();
            if (!anim || !anim.isHuman || !handTarget) return;
            var a = anim.GetBoneTransform(HumanBodyBones.RightUpperArm); var b = anim.GetBoneTransform(HumanBodyBones.RightLowerArm); var c = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (!a || !b || !c) return;
            var t = handTarget.position; float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
            float d = Mathf.Clamp(Vector3.Distance(a.position, t), Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
            float want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - d * d) / (2f * la * lb), -1f, 1f)) * Mathf.Rad2Deg;
            var axis = Vector3.Cross(a.position - b.position, c.position - b.position);
            if (axis.sqrMagnitude < 1e-8f) axis = anim.transform.up; axis.Normalize();
            float cur = Vector3.Angle(a.position - b.position, c.position - b.position);
            var r0 = b.rotation; float best = float.MaxValue; Quaternion bestR = r0;
            foreach (var sgn in new[] { 1f, -1f })
            {
                b.rotation = Quaternion.AngleAxis(sgn * (want - cur), axis) * r0;
                float e = Mathf.Abs(Vector3.Distance(a.position, c.position) - d); if (e < best) { best = e; bestR = b.rotation; }
            }
            b.rotation = bestR;
            a.rotation = Quaternion.FromToRotation(c.position - a.position, t - a.position) * a.rotation;
            c.rotation = handTarget.rotation;
        }

        void OnDrawGizmos()
        {
            if (!useRightHandHelper || !helperTransform) return;
            var h = helperTransform; float s = 0.06f;
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f); Gizmos.DrawWireSphere(h.position, 0.03f);
            Gizmos.color = Color.red; Gizmos.DrawLine(h.position, h.position + h.right * s);
            Gizmos.color = Color.green; Gizmos.DrawLine(h.position, h.position + h.up * s);
            Gizmos.color = Color.blue; Gizmos.DrawLine(h.position, h.position + h.forward * s);
            if (handTarget) { Gizmos.color = new Color(1f, 1f, 1f, 0.5f); Gizmos.DrawLine(h.position, handTarget.position); }
#if UNITY_EDITOR
            UnityEditor.Handles.Label(h.position + Vector3.up * 0.05f, "Right Hand Helper (full draw)");
#endif
        }

        public void RestoreIdle(Animator anim)
        {
            if (_idleOn && anim && _weightsCaptured && _baseWeights != null)
            {
                int n = Mathf.Min(_baseWeights.Length, anim.layerCount);
                for (int i = 0; i < n; i++) anim.SetLayerWeight(i, _baseWeights[i]);
            }
            if (FindModel()) _model.localRotation = ModelRest;
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

        bool FindModel()
        {
            if (_model) return true;
            _model = transform.Find("Model");
            if (!_model)
                foreach (Transform t in transform)
                {
                    string n = t.name;
                    if (n.StartsWith("Grip") || n.StartsWith("String") || n.StartsWith("Arrow") || n.StartsWith("Nock") || n.StartsWith("ASH_") || (handTarget && t == handTarget)) continue;
                    _model = t; break;
                }
            return _model;
        }

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
            if (!FindModel()) return;
            _model.localRotation = ModelRest;
            if (!idleHangVertical) return;
            Vector3 axis = idleHangAxis.sqrMagnitude > 1e-8f ? idleHangAxis.normalized : Vector3.forward;
            Vector3 axisWorld = _model.TransformDirection(axis);
            if (idleHangAutoFlip && Vector3.Dot(axisWorld, Vector3.down) < 0f) axisWorld = -axisWorld;
            _model.rotation = Quaternion.FromToRotation(axisWorld, Vector3.down) * _model.rotation;
            if (idleHangEuler.sqrMagnitude > 1e-8f) _model.Rotate(idleHangEuler, Space.Self);
        }
    }
}
