using ASH.Interaction;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>Fills ASHItemDefinition.worldPickupPrefab from the ASH_Pickup_* prefabs (by the item each pickup holds), so dropped
    /// items spawn their real world pickup. Only fills empty fields; a prefab set by hand is never replaced.</summary>
    public static class ASHItemPickupLinker
    {
        [MenuItem("ASH/Build/Link Item Pickups (drop prefabs)")]
        public static void Run()
        {
            int linked = 0, kept = 0, scanned = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ASHItemCatalog.PickupDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!go) continue;
                var pk = go.GetComponent<ASHItemPickup>(); if (!pk || !pk.Item) continue;
                scanned++;
                var item = pk.Item;
                if (item.worldPickupPrefab) { kept++; continue; }
                item.worldPickupPrefab = go; EditorUtility.SetDirty(item); linked++;
            }
            AssetDatabase.SaveAssets();
            int items = 0, without = 0; var sb = new System.Text.StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:ASHItemDefinition"))
            {
                var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(guid)); if (!it) continue;
                items++; if (!it.worldPickupPrefab) { without++; sb.Append(it.name).Append(' '); }
            }
            Debug.Log($"[ASH] PickupLinker: pickups {scanned}, linked {linked}, kept {kept}; items {items}, without pickup prefab {without} (runtime fallback): {sb}");
        }
    }
}
