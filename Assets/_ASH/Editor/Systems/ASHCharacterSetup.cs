#if UNITY_EDITOR
using ASH.EditorTools.Systems;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: assigns wear slots on known items (face = rag / mask). Does not save the open scene.</summary>
    public static class ASHCharacterSetup
    {
        [MenuItem("ASH/Systems/Journal/Setup Character Tab (idempotent)", false, 216)]
        public static void Setup()
        {
            int n = 0;
            n += Assign("wet_rag", ASHWearSlot.Face, true);
            n += Assign("gas_mask", ASHWearSlot.Face, true);
            n += Assign("oldshirt", ASHWearSlot.Torso, false);
            AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Character tab: wear slots assigned on " + n + " item(s). Face = wet rag / gas mask (hotbar Wear still works). Old shirt can go in Torso if it had no wear slot.");
        }

        static int Assign(string id, ASHWearSlot slot, bool force)
        {
            foreach (var g in AssetDatabase.FindAssets("t:ASHItemDefinition"))
            {
                var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g));
                if (!it || it.itemId != id) continue;
                if (!force && it.wearSlot != ASHWearSlot.None) return 0;
                if (it.wearSlot == slot) return 0;
                it.wearSlot = slot;
                EditorUtility.SetDirty(it);
                return 1;
            }
            Debug.LogWarning("[ASH] Character tab: item missing: " + id);
            return 0;
        }
    }
}
#endif
