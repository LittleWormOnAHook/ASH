using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Play Mode helper (exec ASH.DebugTools.ASHHeldCapture.Run): equips every held item and writes its current grip pose
    /// relative to the bone it is held from to Library/ASH_HeldCapture.txt. ASH/Build/Held Items On Player uses it as the
    /// starting placement for items it adds (it never moves items that already exist on the prefab).</summary>
    public sealed class ASHHeldCapture : MonoBehaviour
    {
        public static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/ASH_HeldCapture.txt"));
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_HeldCapture").AddComponent<ASHHeldCapture>(); }

        IEnumerator Start()
        {
            var eq = FindAnyObjectByType<ASHEquipment>(); var inv = FindAnyObjectByType<ASHInventory>(); var wa = FindAnyObjectByType<ASHWeaponAim>();
            var sb = new StringBuilder(); var anim = eq ? eq.Animator : null;
            if (!eq || !inv || !anim) { Debug.Log("[ASH] HELDCAP failed: no player"); Destroy(gameObject); yield break; }
            eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand); eq.Unequip(ASHEquipSlot.Hip);
            yield return null;
            foreach (var it in Resources.FindObjectsOfTypeAll<ASHItemDefinition>())
            {
                if (!it || !it.IsEquippable || !it.heldPrefab) continue;
                if (inv.Count(it) <= 0) inv.AddItem(it, 1);
                if (it.ammoItem && inv.Count(it.ammoItem) <= 0) inv.AddItem(it.ammoItem, 3);
                eq.Equip(it);
                var go = it.equipSlot == ASHEquipSlot.MainHand ? eq.MainVisual : it.equipSlot == ASHEquipSlot.OffHand ? eq.OffVisual : eq.HipVisual;
                if (!go) { sb.Append($"#{it.itemId} no visual\n"); continue; }
                if (it.equipSlot == ASHEquipSlot.Hip)
                {
                    var hips = anim.GetBoneTransform(HumanBodyBones.Hips); var hook = go.transform.parent;
                    sb.Append(Line("holder_hip", "Hips", hips.InverseTransformPoint(hook.position), Quaternion.Inverse(hips.rotation) * hook.rotation, hook.lossyScale));
                    sb.Append(Line(it.itemId, "Holder", go.transform.localPosition, go.transform.localRotation, go.transform.lossyScale));
                }
                else sb.Append(Line(it.itemId, BoneName(anim, go.transform.parent), go.transform.localPosition, go.transform.localRotation, go.transform.lossyScale));
                if (KindIsBow(it))
                {
                    yield return null; yield return null;
                    var ha = wa ? wa.HandArrow : null;
                    if (ha) sb.Append(Line("handarrow", BoneName(anim, ha.transform.parent), ha.transform.localPosition, ha.transform.localRotation, ha.transform.lossyScale));
                }
                eq.Unequip(it.equipSlot);
                yield return null;
            }
            File.WriteAllText(FilePath, sb.ToString());
            Debug.Log("[ASH] HELDCAP wrote " + FilePath + "\n" + sb);
            Destroy(gameObject);
        }

        static bool KindIsBow(ASHItemDefinition it) => ASHWeaponAim.KindOf(it) == ASHWeaponAim.Kind.Bow;
        static string BoneName(Animator a, Transform t)
        {
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++) if (a.GetBoneTransform((HumanBodyBones)i) == t) return ((HumanBodyBones)i).ToString();
            return t ? t.name : "-";
        }
        static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
        static string Line(string id, string bone, Vector3 p, Quaternion q, Vector3 s) =>
            $"{id}|{bone}|{F(p.x)},{F(p.y)},{F(p.z)}|{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}|{F(s.x)},{F(s.y)},{F(s.z)}\n";
    }
}
