using System.Collections;
using ASH.Combat;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>t353 check (Play): bow drawn, Right Hand Helper on and moved, hand follows (Invector IK and the paused preview IK), bake reproduces it.</summary>
    public sealed class ASHT353Test : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_T353Test").AddComponent<ASHT353Test>(); }
        IEnumerator Start()
        {
            yield return null;
            var inv = FindAnyObjectByType<ASHInventory>(); var eq = FindAnyObjectByType<ASHEquipment>(); var input = FindAnyObjectByType<ASHInput>();
#if UNITY_EDITOR
            var bow = UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_WoodenBow.asset");
            var arrows = UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_WoodenArrow.asset");
            if (arrows && inv.Count(arrows) < 5) inv.AddItem(arrows, 5); if (bow && inv.Count(bow) == 0) inv.AddItem(bow, 1);
            eq.Equip(bow);
#endif
            yield return new WaitForSeconds(0.6f);
            input.SimSecondaryHeld = true; yield return new WaitForSeconds(1.5f); input.SimDraw = 1f; yield return new WaitForSeconds(1.5f);
            var rig = eq.MainVisual ? eq.MainVisual.GetComponent<ASHBowDrawRig>() : null; var anim = eq.Animator; var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (!rig) { Debug.Log("[ASH] T353 no rig"); yield break; }
            yield return new WaitForEndOfFrame();
            float d0 = Vector3.Distance(rh.position, rig.handTarget.position);
            var h = rig.EnsureHelper(true); rig.useRightHandHelper = true;
            yield return new WaitForSeconds(0.3f); yield return new WaitForEndOfFrame();
            float d1 = Vector3.Distance(rh.position, rig.handTarget.position); var p1 = rh.position;
            h.localPosition += new Vector3(0f, 0.04f, 0.03f);
            yield return new WaitForSeconds(0.4f); yield return new WaitForEndOfFrame();
            float moved = Vector3.Distance(rh.position, p1); float d2 = Vector3.Distance(rh.position, rig.handTarget.position);
            rig.PreviewArm(anim); float dPrev = Vector3.Distance(rh.position, rig.handTarget.position);
            var want = h.localPosition; rig.BakeHelper(); float bakeErr = Vector3.Distance(rig.HandFullEffective, want);
            Debug.Log($"[ASH] T353 hand->target before={d0:F3} helperOn={d1:F3} afterMove={d2:F3} handMoved={moved:F3}m previewIK={dPrev:F3} bakeErr={bakeErr:F4} helper={h.name} parent={h.parent.name}");
            rig.useRightHandHelper = false; Destroy(h.gameObject);
            input.SimDraw = -1f; yield return new WaitForSeconds(0.2f); input.SimDraw = 0f; input.SimSecondaryHeld = false;
            Debug.Log("[ASH] T353 done"); Destroy(gameObject);
        }
    }
}
