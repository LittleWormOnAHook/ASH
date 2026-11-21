#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Applies Assets/_ASH/Art/ASH_M2_42_Icons to matching items (by item id / filename) and to recipes that produce them.
    /// Import settings are never changed. Mapping is logged to ASH_M2_IconMapping.csv and can be undone with Revert M2 Icons. Idempotent.</summary>
    public static class ASHIconM2Apply
    {
        public const string Root = "Assets/_ASH/Art/ASH_M2_42_Icons";
        const string MapPath = Root + "/ASH_M2_IconMapping.csv";

        /// <summary>Normalized icon stem (after ASH_Icon_) -> itemId, for names that do not match the id exactly.</summary>
        static readonly Dictionary<string, string> StemToItemId = new Dictionary<string, string>
        {
            { "gametrap", "largetrap" },
            { "bonefishhooks", "fishhook" },
            { "wolftoothcharm", "wolfcharm" },
            { "bonearrowheads", "bonearrowhead" },
            { "huntersstew", "hunterstew" }
        };

        [MenuItem("ASH/Art/Apply M2 Icons (ASH_M2_42_Icons)", false, 210)]
        public static void Apply()
        {
            var icons = LoadIcons(out var unused, out var notSprite);
            if (notSprite.Count > 0)
            {
                Debug.LogWarning("[ASH] M2 ICONS: these files did not load as Sprite (import settings were not changed): " + string.Join(", ", notSprite));
            }

            var itemsById = new Dictionary<string, ASHItemDefinition>();
            var itemPath = new Dictionary<ASHItemDefinition, string>();
            foreach (var g in AssetDatabase.FindAssets("t:ASHItemDefinition", new[] { "Assets/_ASH" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var item = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(p);
                if (!item) continue;
                itemPath[item] = p;
                var id = Norm(item.itemId);
                if (id.Length > 0 && !itemsById.ContainsKey(id)) itemsById[id] = item;
            }

            var rows = ReadMap();
            var done = new HashSet<string>(rows.Select(r => r[0]));
            var usedIcons = new HashSet<string>();
            var unmatchedIcons = new List<string>();
            int applied = 0, already = 0;
            var assigned = new Dictionary<ASHItemDefinition, Sprite>();

            foreach (var kv in icons)
            {
                var stem = kv.Key;
                var sprite = kv.Value.sprite;
                var iconPath = kv.Value.path;
                var item = ResolveItem(stem, itemsById, itemPath);
                if (!item)
                {
                    unmatchedIcons.Add(Path.GetFileNameWithoutExtension(iconPath));
                    continue;
                }
                usedIcons.Add(iconPath);
                assigned[item] = sprite;
                if (Assign(itemPath[item], item, "icon", sprite, rows, done, ref already)) applied++;
            }

            int recipes = 0, recipesAlready = 0;
            foreach (var g in AssetDatabase.FindAssets("t:ASHRecipe", new[] { "Assets/_ASH" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var rec = AssetDatabase.LoadAssetAtPath<ASHRecipe>(p);
                if (!rec || !rec.outputItem) continue;
                if (!assigned.TryGetValue(rec.outputItem, out var sprite)) continue;
                int before = already;
                if (Assign(p, rec, "icon", sprite, rows, done, ref already)) recipes++;
                else if (already > before) recipesAlready++;
            }

            foreach (var kv in icons)
                if (!usedIcons.Contains(kv.Value.path))
                    unused.Add(Path.GetFileNameWithoutExtension(kv.Value.path));

            WriteMap(rows);
            AssetDatabase.SaveAssets();

            var log = new StringBuilder();
            log.Append("[ASH] M2 ICONS applied ").Append(applied).Append(" items, ").Append(recipes).Append(" recipes");
            log.Append(" (already ").Append(already).Append(" item, ").Append(recipesAlready).Append(" recipe); ");
            log.Append("icons: ").Append(icons.Count).Append("; ");
            log.Append("unmatched icons: ").Append(unmatchedIcons.Count == 0 ? "(none)" : string.Join(", ", unmatchedIcons)).Append("; ");
            log.Append("unused icons: ").Append(unused.Count == 0 ? "(none)" : string.Join(", ", unused)).Append("; ");
            log.Append("mapping -> ").Append(MapPath);
            Debug.Log(log.ToString());
        }

        [MenuItem("ASH/Art/Revert M2 Icons (restore previous)", false, 211)]
        public static void Revert()
        {
            int n = 0;
            foreach (var r in ReadMap())
            {
                var a = AssetDatabase.LoadAssetAtPath<ScriptableObject>(r[0]);
                if (!a) continue;
                var so = new SerializedObject(a);
                var ip = so.FindProperty("icon");
                if (ip == null) continue;
                string op = r[1].Length > 0 ? AssetDatabase.GUIDToAssetPath(r[1]) : "";
                Sprite old = op.Length > 0 ? AssetDatabase.LoadAllAssetsAtPath(op).OfType<Sprite>().FirstOrDefault() : null;
                ip.objectReferenceValue = old;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ASH] M2 ICONS reverted " + n + " assets to their previous icons (mapping kept in " + MapPath + ")");
        }

        struct IconEntry { public string path; public Sprite sprite; }

        static Dictionary<string, IconEntry> LoadIcons(out List<string> unused, out List<string> notSprite)
        {
            unused = new List<string>();
            notSprite = new List<string>();
            var dict = new Dictionary<string, IconEntry>();
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (!p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p)
                    ?? AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().FirstOrDefault();
                if (!sprite)
                {
                    notSprite.Add(Path.GetFileName(p));
                    continue;
                }
                var stem = Path.GetFileNameWithoutExtension(p);
                if (stem.StartsWith("ASH_Icon_", System.StringComparison.OrdinalIgnoreCase))
                    stem = stem.Substring("ASH_Icon_".Length);
                var key = Norm(stem);
                if (key.Length == 0 || dict.ContainsKey(key)) continue;
                dict[key] = new IconEntry { path = p, sprite = sprite };
            }
            return dict;
        }

        static ASHItemDefinition ResolveItem(string iconStem, Dictionary<string, ASHItemDefinition> itemsById, Dictionary<ASHItemDefinition, string> itemPath)
        {
            if (StemToItemId.TryGetValue(iconStem, out var aliasId) && itemsById.TryGetValue(Norm(aliasId), out var aliased))
                return aliased;
            if (itemsById.TryGetValue(iconStem, out var exact))
                return exact;

            // asset file name or display name
            foreach (var kv in itemPath)
            {
                var item = kv.Key;
                var file = Path.GetFileNameWithoutExtension(kv.Value);
                if (file.StartsWith("ASH_Item_")) file = file.Substring("ASH_Item_".Length);
                if (Norm(file) == iconStem || Norm(item.displayName) == iconStem) return item;
            }
            return null;
        }

        static bool Assign(string assetPath, Object asset, string prop, Sprite art, List<string[]> rows, HashSet<string> done, ref int already)
        {
            var so = new SerializedObject(asset);
            var ip = so.FindProperty(prop);
            if (ip == null) return false;
            var old = ip.objectReferenceValue as Sprite;
            if (old == art) { already++; return false; }
            if (!done.Contains(assetPath))
            {
                string op = old ? AssetDatabase.GetAssetPath(old) : "";
                rows.Add(new[] { assetPath, op.Length > 0 ? AssetDatabase.AssetPathToGUID(op) : "", op, AssetDatabase.GetAssetPath(art) });
                done.Add(assetPath);
            }
            ip.objectReferenceValue = art;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return true;
        }

        static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            foreach (var c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        static List<string[]> ReadMap()
        {
            var l = new List<string[]>();
            if (!File.Exists(MapPath)) return l;
            foreach (var line in File.ReadAllLines(MapPath).Skip(1))
            {
                var c = line.Split(',');
                if (c.Length >= 4) l.Add(c);
            }
            return l;
        }

        static void WriteMap(List<string[]> rows)
        {
            File.WriteAllLines(MapPath, new[] { "asset,oldIconGuid,oldIconPath,newIconPath" }.Concat(rows.Select(r => string.Join(",", r))));
            AssetDatabase.ImportAsset(MapPath);
        }
    }
}
#endif
