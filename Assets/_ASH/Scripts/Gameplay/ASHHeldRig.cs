using System.Collections.Generic;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Item-driven hands. Every held item can carry grip children (Grip_R / Grip_L, bow String_R, rifle Stock) and has an aim/use
    /// anchor under the player (ASH_AimPivot/AimAnchor_X for aimed items, ASH_BodyPivot/UseAnchor_X for the raised torch).
    /// While aiming/using, the item is put on its anchor and both hands are pulled onto its grips (two-bone IK, position +
    /// rotation, adjustable weights) on top of the animation and the other after-animation edits. Move an anchor or a grip in
    /// the editor (prefab, or live in Play mode) and the arms follow. At rest only the support hand may be pulled (restSupportWeight).
    /// Runs after ASHWeaponAim / ASHLightAim (order 1800), before ASHPoseGuardCapture (1900). Hierarchy is made by
    /// ASH/Build/Held Item Rig (adds missing transforms only, never moves existing ones).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1800)]
    public sealed class ASHHeldRig : MonoBehaviour
    {
        public const string AimPivotName = "ASH_AimPivot", BodyPivotName = "ASH_BodyPivot", AimAnchorPrefix = "AimAnchor_", UseAnchorPrefix = "UseAnchor_";
        public const string GripR = "Grip_R", GripL = "Grip_L", StringR = "String_R", Stock = "Stock";

        public bool rigEnabled = true;
        [Tooltip("Smoothing of the aim pivot toward the crosshair (1/s, 0 = none).")]
        [Range(0f, 60f)] public float aimPivotSmoothing = 25f;
        Quaternion _aimRot; bool _aimRotOk;
        [Header("Aim / use (item on its anchor)")]
        [Range(0f, 1f)] public float anchorWeight = 1f;
        [Range(0f, 1f)] public float holdHandPositionWeight = 1f;
        [Range(0f, 1f)] public float holdHandRotationWeight = 1f;
        [Range(0f, 1f)] public float supportPositionWeight = 1f;
        [Range(0f, 1f)] public float supportRotationWeight = 0.8f;
        [Tooltip("Bow: drawing hand onto String_R (scaled by the draw).")]
        [Range(0f, 1f)] public float bowStringPositionWeight = 1f;
        [Range(0f, 1f)] public float bowStringRotationWeight = 0.3f;
        [Header("Rest / swing")]
        [Tooltip("Support hand onto its grip while just holding the item (0 = animation reads as is).")]
        [Range(0f, 1f)] public float restSupportWeight = 0f;
        [Tooltip("Axe: left hand onto Grip_L during chop swings (0 = chop clip's own hand).")]
        [Range(0f, 1f)] public float chopLeftHandWeight = 0f;
        [Range(1f, 20f)] public float blendSpeed = 6f;

        Animator _a; ASHEquipment _eq; ASHWeaponAim _wa; ASHLightAim _la; ASHChopAnimator _chop; vThirdPersonController _cc;
        Transform _aimPivot, _bodyPivot;
        readonly Dictionary<Transform, float> _w = new Dictionary<Transform, float>();
        // item local pose kept stable (the rig moves the item each frame; the authored local pose comes back next frame)
        Transform _forcedT; Vector3 _restLp, _forcedLp; Quaternion _restLr, _forcedLr; Transform _forcedT2; Vector3 _restLp2, _forcedLp2; Quaternion _restLr2, _forcedLr2;

        /// <summary>Diagnostics: hand-to-grip error (m) after the last solve, per hand; -1 = not driven.</summary>
        public float HoldError { get; private set; } = -1f;
        public float SupportError { get; private set; } = -1f;
        public string Driving { get; private set; } = "";

        public Transform AimPivot => _aimPivot; public Transform BodyPivot => _bodyPivot;
        Transform Root => _cc ? _cc.transform : transform.root;

        void Awake()
        {
            _a = GetComponent<Animator>(); _eq = GetComponentInParent<ASHEquipment>(); _wa = GetComponent<ASHWeaponAim>(); _la = GetComponent<ASHLightAim>();
            _chop = GetComponent<ASHChopAnimator>(); _cc = GetComponentInParent<vThirdPersonController>();
            _aimPivot = FindOrMake(AimPivotName); _bodyPivot = FindOrMake(BodyPivotName);
            if (!GetComponent<ASHHeldRigRestore>()) gameObject.AddComponent<ASHHeldRigRestore>();
        }

        Transform FindOrMake(string n)
        {
            var t = Root.Find(n); if (t) return t;
            t = new GameObject(n).transform; t.SetParent(Root, false); return t;
        }

        public static Transform Child(Transform item, string name)
        {
            if (!item) return null;
            foreach (var t in item.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
        public static string ItemKey(GameObject v) => v ? v.name.Replace("Held_", "").Replace("(Clone)", "").Trim() : "";

        /// <summary>Places the pivots: aim pivot at the upper chest facing the crosshair, body pivot at the upper chest with the body's yaw.</summary>
        public void UpdatePivots()
        {
            if (!_a || !_a.isHuman) return;
            var chest = _a.GetBoneTransform(HumanBodyBones.UpperChest); if (!chest) chest = _a.GetBoneTransform(HumanBodyBones.Chest);
            var p = chest ? chest.position : Root.position + Vector3.up * 1.4f;
            _bodyPivot.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.ProjectOnPlane(Root.forward, Vector3.up), Vector3.up));
            var aim = _wa ? _wa.AimPoint(out _) - p : Root.forward; if (aim.sqrMagnitude < 1e-4f) aim = Root.forward;
            var want = Quaternion.LookRotation(aim.normalized, Vector3.up);
            // smoothed: a crosshair hit jumping between near and far surfaces must not snap the held item
            _aimRot = _aimRotOk && aimPivotSmoothing > 0f && Time.deltaTime > 0f ? Quaternion.Slerp(_aimRot, want, 1f - Mathf.Exp(-aimPivotSmoothing * Time.deltaTime)) : want; _aimRotOk = true;
            _aimPivot.SetPositionAndRotation(p, _aimRot);
        }

        public void RestoreItems()
        {
            Restore(ref _forcedT, ref _restLp, ref _restLr, _forcedLp, _forcedLr);
            Restore(ref _forcedT2, ref _restLp2, ref _restLr2, _forcedLp2, _forcedLr2);
        }
        /// <summary>Authored (edit-time) local pose of t: if the rig has t forced onto its aim/use anchor this frame, the pose it will restore; else t's own.</summary>
        public void AuthoredLocal(Transform t, ref Vector3 lp, ref Quaternion lr)
        {
            if (!t) return;
            if (t == _forcedT && t.localPosition == _forcedLp && t.localRotation == _forcedLr) { lp = _restLp; lr = _restLr; }
            else if (t == _forcedT2 && t.localPosition == _forcedLp2 && t.localRotation == _forcedLr2) { lp = _restLp2; lr = _restLr2; }
        }
        static void Restore(ref Transform t, ref Vector3 rlp, ref Quaternion rlr, Vector3 flp, Quaternion flr)
        {
            if (!t) { t = null; return; }
            // untouched since we forced it -> put the authored pose back; changed (edited in the Inspector) -> keep the edit as the new rest
            if (t.localPosition == flp && t.localRotation == flr) { t.localPosition = rlp; t.localRotation = rlr; }
            t = null;
        }

        void LateUpdate()
        {
            HoldError = SupportError = -1f; Driving = "";
            if (!rigEnabled || !_a || !_a.isHuman || !_eq) return;
            UpdatePivots();
            Drive(_eq.MainVisual, false);
            Drive(_eq.OffVisual, true);
            if (_wa) _wa.RefreshAfterRig();
        }

        void Drive(GameObject v, bool offSlot)
        {
            if (!v || !v.activeInHierarchy) return;
            var item = v.transform; string key = ItemKey(v);
            bool leftHolds = offSlot || ASHEquipment.InLeftHand(_eq.Main);
            var holdHand = _a.GetBoneTransform(leftHolds ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (!holdHand || !item.IsChildOf(holdHand)) return;
            bool bow = !offSlot && _wa && _wa.Current == ASHWeaponAim.Kind.Bow;
            bool gun = !offSlot && _wa && (_wa.Current == ASHWeaponAim.Kind.Rifle || _wa.Current == ASHWeaponAim.Kind.Pistol);
            bool axe = !offSlot && _chop && _chop.Chopping;
            bool torchUp = offSlot && _la && _la.TorchRaised && !_la.Aiming;
            bool lightAim = offSlot && _la && _la.Aiming;
            // Invector shooter owns ranged items (ASH.Combat.ASHCombatBridge sets ExternalControl): its grip, support-hand IK
            // and aim alignment place the weapon and both hands, so the ASH anchors / Grip IK stay off for them.
            if ((bow || gun) && _wa.ExternalControl) return;

            Transform anchor = null; float want = 0f, draw = 1f;
            if (bow && _wa.Aiming) { anchor = Child(_aimPivot, AimAnchorPrefix + key); draw = Mathf.SmoothStep(0f, 1f, _wa.Draw01); want = draw; }
            else if (gun && _wa.Aiming) { anchor = Child(_aimPivot, AimAnchorPrefix + key); want = 1f; }
            else if (lightAim) { anchor = Child(_aimPivot, AimAnchorPrefix + key); want = 1f; }
            else if (torchUp) { anchor = Child(_bodyPivot, UseAnchorPrefix + key); want = 1f; }
            if (!anchor) want = 0f;
            float w = _w.TryGetValue(item, out var cur) ? cur : 0f; w = Mathf.MoveTowards(w, want, blendSpeed * Time.deltaTime); _w[item] = w;
            float sw = Mathf.SmoothStep(0f, 1f, w) * anchorWeight;

            Transform gHold = Child(item, leftHolds ? GripL : GripR);
            Transform gSup = bow ? Child(item, StringR) : Child(item, leftHolds ? GripR : GripL);
            var supHand = _a.GetBoneTransform(leftHolds ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);

            // target item pose: the anchor (blended from where the hand has it now)
            Vector3 tPos = item.position; Quaternion tRot = item.rotation;
            if (sw > 0.001f && anchor) { tPos = Vector3.Lerp(item.position, anchor.position, sw); tRot = Quaternion.Slerp(item.rotation, anchor.rotation, sw); }
            var TM = Matrix4x4.TRS(tPos, tRot, item.lossyScale); var inv = item.worldToLocalMatrix;
            if (sw > 0.001f)
            {
                Driving += key + " ";
                // holding hand: onto Grip (or the authored hand->item offset)
                var holdM = gHold ? TM * (inv * gHold.localToWorldMatrix) : TM * (inv * holdHand.localToWorldMatrix);
                SolveHand(leftHolds, holdM.GetColumn(3), Rot(holdM), holdHandPositionWeight * sw, holdHandRotationWeight * sw);
                HoldError = Vector3.Distance(holdHand.position, (Vector3)holdM.GetColumn(3));
                Force(item, tPos, tRot, offSlot);
            }
            // support hand
            float supW = bow ? bowStringPositionWeight * sw * draw : sw > 0.001f ? supportPositionWeight * sw : axe ? chopLeftHandWeight : restSupportWeight;
            float supR = bow ? bowStringRotationWeight * sw * draw : sw > 0.001f ? supportRotationWeight * sw : axe ? chopLeftHandWeight : restSupportWeight;
            if (gSup && supHand && supW > 0.001f)
            {
                SolveHand(!leftHolds, gSup.position, gSup.rotation, supW, supR);
                SupportError = Vector3.Distance(supHand.position, gSup.position);
            }
        }

        static Quaternion Rot(Matrix4x4 m) { var f = (Vector3)m.GetColumn(2); var u = (Vector3)m.GetColumn(1); return f.sqrMagnitude > 1e-8f && u.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(f, u) : Quaternion.identity; }

        void Force(Transform item, Vector3 pos, Quaternion rot, bool second)
        {
            if (second) { if (_forcedT2 != item) { _restLp2 = item.localPosition; _restLr2 = item.localRotation; } item.SetPositionAndRotation(pos, rot); _forcedT2 = item; _forcedLp2 = item.localPosition; _forcedLr2 = item.localRotation; }
            else { if (_forcedT != item) { _restLp = item.localPosition; _restLr = item.localRotation; } item.SetPositionAndRotation(pos, rot); _forcedT = item; _forcedLp = item.localPosition; _forcedLr = item.localRotation; }
        }

        /// <summary>Two-bone IK (shoulder-elbow-hand), keeps the current elbow bend side; then the hand rotation.</summary>
        public void SolveHand(bool left, Vector3 target, Quaternion targetRot, float posW, float rotW)
        {
            var up = _a.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var lo = _a.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = _a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (!up || !lo || !hand) return;
            if (posW > 0.001f)
            {
                var goal = Vector3.Lerp(hand.position, target, posW);
                float a = Vector3.Distance(up.position, lo.position), b = Vector3.Distance(lo.position, hand.position);
                var t = goal - up.position; float d = Mathf.Clamp(t.magnitude, 0.01f, a + b - 0.001f); var tn = t.sqrMagnitude > 1e-8f ? t.normalized : Root.forward;
                var side = Vector3.ProjectOnPlane(lo.position - up.position, tn);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.ProjectOnPlane(-Vector3.up, tn);
                side.Normalize();
                float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f), sinA = Mathf.Sqrt(1f - cosA * cosA);
                var elbow = up.position + (tn * cosA + side * sinA) * a;
                var hRot = hand.rotation;
                up.rotation = Quaternion.FromToRotation(lo.position - up.position, elbow - up.position) * up.rotation;
                lo.rotation = Quaternion.FromToRotation(hand.position - lo.position, up.position + tn * d - lo.position) * lo.rotation;
                hand.rotation = hRot; // keep the hand's world rotation through the arm move
            }
            if (rotW > 0.001f) hand.rotation = Quaternion.Slerp(hand.rotation, targetRot, rotW);
        }
    }

    /// <summary>Puts the held items' authored local poses back before the after-animation edits (order 1590).</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1590)]
    public sealed class ASHHeldRigRestore : MonoBehaviour
    {
        ASHHeldRig _r;
        void Awake() { _r = GetComponent<ASHHeldRig>(); }
        void LateUpdate() { if (_r) _r.RestoreItems(); }
    }
}
