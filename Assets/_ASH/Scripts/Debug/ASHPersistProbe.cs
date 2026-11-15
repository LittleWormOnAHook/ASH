using ASH.Gameplay;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Play Mode harness for "Keep Player Play Changes": nudges AimAnchor_Pistol 1 cm up and sets
    /// ASHWeaponAim.headLookWeight to 0.55, so leaving Play should write exactly those to the player prefab. Logs "PERSISTPROBE done".</summary>
    public static class ASHPersistProbe
    {
        public static void Run()
        {
            var wa = Object.FindAnyObjectByType<ASHWeaponAim>(); var rig = Object.FindAnyObjectByType<ASHHeldRig>();
            var a = rig && rig.AimPivot ? ASHHeldRig.Child(rig.AimPivot, ASHHeldRig.AimAnchorPrefix + "Pistol") : null;
            if (a) a.localPosition += Vector3.up * 0.01f;
            if (wa) wa.headLookWeight = 0.55f;
            Debug.Log($"[ASH] PERSISTPROBE done anchor={(a ? a.localPosition.ToString("F4") : "none")} headLookWeight={(wa ? wa.headLookWeight : -1f)}");
        }
    }
}
