using UnityEngine;

namespace ASH.Combat
{
    /// <summary>On Held_WoodenBow (made by ASH/Systems/Combat/3 Fix Ranged Grips). The ASH bow has no string bone, so this stands in
    /// for Invector's bow rig: the drawing-hand IK target (Invector vShooterWeapon.handIKTarget) slides from the string at rest
    /// to the full-draw hand pose (handFullLocal, from the authored String_R grip) with the bow power, and the nock rides the
    /// string back by the same amount (ASHWeaponAim lays the nocked arrow from the nock to the rest and pulls the string mesh to it).
    /// All values are in the bow's local space and can be tuned in the Inspector.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHBowDrawRig : MonoBehaviour
    {
        [Tooltip("Invector handIKTarget of this bow (right hand on the string).")]
        public Transform handTarget;
        [Tooltip("Right-hand bone pose at full draw (bow local).")]
        public Vector3 handFullLocal;
        public Quaternion handRotLocal = Quaternion.identity;
        [Tooltip("Nocking point on the string at rest (bow local).")]
        public Vector3 nockRestLocal;
        [Tooltip("Arrow rest on the riser (bow local).")]
        public Vector3 restLocal;
        [Tooltip("Draw direction (bow local, from the bow toward the archer).")]
        public Vector3 backLocal = Vector3.right;
        [Tooltip("How far the string and the drawing hand travel from rest to full draw (m).")]
        [Range(0.05f, 1f)] public float drawLength = 0.44f;
        [System.NonSerialized] public float draw01;

        public Vector3 NockWorld => transform.TransformPoint(nockRestLocal + backLocal.normalized * (drawLength * draw01));
        public Vector3 RestWorld => transform.TransformPoint(restLocal);

        /// <summary>Moves the hand IK target for the current draw (call before Invector's IK, i.e. in Update).</summary>
        public void Apply()
        {
            if (!handTarget) return;
            handTarget.localPosition = handFullLocal - backLocal.normalized * (drawLength * (1f - Mathf.Clamp01(draw01)));
            handTarget.localRotation = handRotLocal;
        }
    }
}
