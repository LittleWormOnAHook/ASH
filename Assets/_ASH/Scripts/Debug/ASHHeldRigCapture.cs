using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Play Mode (exec ASH.DebugTools.ASHHeldRigCapture.Run): poses each handheld item the way it looks now (aim / full
    /// draw / raised / chop impact) and records its aim/use anchor (item pose in the pivot) and grips (hand bone poses in item
    /// space) to AshBridge/heldrig_capture.json. ASH/Build/Held Item Rig then ADDS the missing transforms to the player prefab
    /// from it (existing ones are never moved). Logs "[ASH] RIGCAPTURE ...".</summary>
    public sealed class ASHHeldRigCapture : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_RigCapture").AddComponent<ASHHeldRigCapture>(); }
        [System.Serializable] public class Pose { public string name, parent; public Vector3 p; public Quaternion r; }
        [System.Serializable] public class Entry { public string item, visual, key; public List<Pose> poses = new List<Pose>(); }
        [System.Serializable] public class Capture { public List<Entry> items = new List<Entry>(); }
        static ASHItemDefinition Item(string file)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"Assets/_ASH/Data/Items/ASH_Item_{file}.asset");
#else
            return null;
#endif
        }
        static Pose Local(string name, string parent, Transform space, Vector3 wp, Quaternion wr) => new Pose { name = name, parent = parent, p = space.InverseTransformPoint(wp), r = Quaternion.Inverse(space.rotation) * wr };

        IEnumerator Start()
        {
            yield return null;
            var inv = FindAnyObjectByType<ASHInventory>(); var eq = FindAnyObjectByType<ASHEquipment>(); var input = FindAnyObjectByType<ASHInput>();
            var wa = FindAnyObjectByType<ASHWeaponAim>(); var rig = FindAnyObjectByType<ASHHeldRig>(); var layer = FindAnyObjectByType<ASHActionLayer>();
            var log = new StringBuilder(); var cap = new Capture();
            if (!(inv && eq && input && wa && rig)) { Debug.Log("[ASH] RIGCAPTURE missing components"); Destroy(gameObject); yield break; }
            var a = eq.Animator; var rh = a.GetBoneTransform(HumanBodyBones.RightHand); var lh = a.GetBoneTransform(HumanBodyBones.LeftHand);
            bool rig0 = rig.rigEnabled; rig.rigEnabled = false; // capture the current look, not the rig's
            foreach (var am in new[] { "RifleRounds", "PistolRounds", "WoodenArrow" }) { var ai = Item(am); if (ai && inv.Count(ai) < 5) inv.AddItem(ai, 5); }
            input.SimSecondaryHeld = false; eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand);
            foreach (var c in new[] { "HuntingRifle", "Pistol", "WoodenBow", "Hatchet", "Torch", "Flashlight" })
            {
                var it = Item(c); if (!it) { log.Append($"{c}: no item; "); continue; }
                if (inv.Count(it) == 0) inv.AddItem(it, 1);
                eq.Equip(it); yield return new WaitForSeconds(0.6f);
                bool off = it.equipSlot == ASHEquipSlot.OffHand; var v = off ? eq.OffVisual : eq.MainVisual;
                if (!v) { log.Append($"{c}: no visual; "); continue; }
                var e = new Entry { item = c, visual = v.name, key = ASHHeldRig.ItemKey(v) };
                var item = v.transform; bool leftHolds = ASHEquipment.InLeftHand(it);
                var hold = leftHolds ? lh : rh; var sup = leftHolds ? rh : lh;
                // rest grips first (hand bone in item space)
                if (c == "Hatchet")
                {
                    yield return new WaitForEndOfFrame();
                    e.poses.Add(Local(ASHHeldRig.GripR, e.visual, item, rh.position, rh.rotation));
                    float until = Time.time + 0.6f; while (Time.time < until) { layer.PlayAt("ChopHigh", 0.45f); yield return null; }
                    yield return new WaitForEndOfFrame();
                    e.poses.Add(Local(ASHHeldRig.GripL, e.visual, item, lh.position, lh.rotation)); // two-hand option (chopLeftHandWeight)
                    layer.Release();
                }
                else if (c == "Torch")
                {
                    yield return new WaitForSeconds(1.4f); yield return new WaitForEndOfFrame(); // torch rides raised while held
                    rig.UpdatePivots();
                    e.poses.Add(Local(ASHHeldRig.GripL, e.visual, item, lh.position, lh.rotation));
                    e.poses.Add(Local(ASHHeldRig.UseAnchorPrefix + e.key, ASHHeldRig.BodyPivotName, rig.BodyPivot, item.position, item.rotation));
                }
                else
                {
                    input.SimSecondaryHeld = true; yield return new WaitForSeconds(c == "WoodenBow" ? 2.2f : 1.4f); yield return new WaitForEndOfFrame();
                    rig.UpdatePivots();
                    e.poses.Add(Local(leftHolds ? ASHHeldRig.GripL : ASHHeldRig.GripR, e.visual, item, hold.position, hold.rotation));
                    if (c == "WoodenBow") e.poses.Add(Local(ASHHeldRig.StringR, e.visual, item, rh.position, rh.rotation));
                    else if (!off) e.poses.Add(Local(ASHHeldRig.GripL, e.visual, item, sup.position, sup.rotation));
                    if (c == "HuntingRifle") e.poses.Add(Local(ASHHeldRig.Stock, e.visual, item, wa.ButtWorld(v), item.rotation));
                    e.poses.Add(Local(ASHHeldRig.AimAnchorPrefix + e.key, ASHHeldRig.AimPivotName, rig.AimPivot, item.position, item.rotation));
                    input.SimSecondaryHeld = false; yield return new WaitForSeconds(0.5f);
                }
                cap.items.Add(e); log.Append($"{c} ({e.visual}): {e.poses.Count} poses; ");
                eq.Unequip(off ? ASHEquipSlot.OffHand : ASHEquipSlot.MainHand); yield return new WaitForSeconds(0.3f);
            }
            // nocked arrow in the right hand: grip only (it rides the string)
            var arrow = rh ? ASHHeldRig.Child(rh, "Held_HandArrow") : null;
            if (arrow) { var e = new Entry { item = "WoodenArrow", visual = arrow.name, key = "HandArrow" }; e.poses.Add(Local(ASHHeldRig.GripR, arrow.name, arrow, rh.position, rh.rotation)); cap.items.Add(e); log.Append("arrow grip; "); }
            rig.rigEnabled = rig0; input.SimSecondaryHeld = null;
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../AshBridge/heldrig_capture.json"));
            File.WriteAllText(path, JsonUtility.ToJson(cap, true));
            Debug.Log($"[ASH] RIGCAPTURE wrote {path}: {log}");
            Destroy(gameObject);
        }
    }
}
