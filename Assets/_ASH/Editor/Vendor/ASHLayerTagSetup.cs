using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Vendor
{
    /// <summary>Idempotent: adds the approved vendor layers (empty slots only) and tags through the TagManager API.
    /// Existing ASH layers and tags are never renamed, moved or removed.</summary>
    public static class ASHLayerTagSetup
    {
        static readonly KeyValuePair<int, string>[] Layers =
        {
            new KeyValuePair<int, string>(18, "CoverPoint"), new KeyValuePair<int, string>(19, "CompanionAI"),
            new KeyValuePair<int, string>(20, "Animal"), new KeyValuePair<int, string>(21, "BlockAIRayCast"),
            new KeyValuePair<int, string>(22, "Pushable"), new KeyValuePair<int, string>(24, "Building"),
        };
        static readonly string[] Tags = { "Ragdoll", "ExitWater", "FreeClimb", "ClimbDirection", "Zipline", "CoverPoint", "AIAction", "PickUp", "Walkable", "Bullet", "Climbable" };

        [MenuItem("ASH/Systems/Vendor Layers + Tags (add missing)")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Layers+Tags: stop Play mode first."); return; }
            var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = so.FindProperty("layers"); var tags = so.FindProperty("tags");
            var log = new List<string>();
            foreach (var kv in Layers)
            {
                var p = layers.GetArrayElementAtIndex(kv.Key);
                if (p.stringValue == kv.Value) { log.Add($"layer {kv.Key} {kv.Value}: already"); continue; }
                int at = -1; for (int i = 0; i < layers.arraySize; i++) if (layers.GetArrayElementAtIndex(i).stringValue == kv.Value) at = i;
                if (at >= 0) { log.Add($"layer {kv.Value}: exists at {at}, left alone"); continue; }
                if (!string.IsNullOrEmpty(p.stringValue)) { log.Add($"layer {kv.Key}: slot used by '{p.stringValue}', skipped {kv.Value}"); continue; }
                p.stringValue = kv.Value; log.Add($"layer {kv.Key} {kv.Value}: ADDED");
            }
            foreach (var t in Tags)
            {
                bool found = false; for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == t) { found = true; break; }
                if (found) { log.Add($"tag {t}: already"); continue; }
                tags.InsertArrayElementAtIndex(tags.arraySize); tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = t; log.Add($"tag {t}: ADDED");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Layers+Tags: " + string.Join("; ", log));
        }
    }
}
