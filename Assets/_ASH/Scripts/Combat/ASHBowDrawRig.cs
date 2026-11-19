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

        [System.NonSerialized] public float draw01;

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
    }
}
