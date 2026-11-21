using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>t486: applies Assets/_ASH/Art/ASH_ICON_FINAL (Inventory + Recipes + NewIcons) to every item and recipe by name,
    /// keeping all older icon files. Journal / Field Manual page images in this folder are not assigned (page art is untouched).
    /// Import settings are left as they are (Anthony converts them to sprites). Every change is logged to
    /// ASH_ICON_FINAL/ASH_IconFinalMapping.csv and can be undone with ASH/Art/Revert Final Icons. Idempotent.
    /// Logs unmatched assets and unused final icons.</summary>
    public static class ASHIconFinalApply
    {
        public const string Root = "Assets/_ASH/Art/ASH_ICON_FINAL";
        const string MapPath = Root + "/ASH_IconFinalMapping.csv";
        const string PagesFolder = "/FieldManual";

        [MenuItem("ASH/Art/Apply Final Icons (ASH_ICON_FINAL)")]
        public static void Apply()
        {
            var rows = ReadMap(); var done = new HashSet<string>(rows.Select(r => r[0]));
            var noArt = new List<string>(); var used = new HashSet<string>(); int n = 0, already = 0;
            n += Do<ASHItemDefinition>("t:ASHItemDefinition", "ASH_Item_", true, rows, done, noArt, used, ref already);
            n += Do<ASHRecipe>("t:ASHRecipe", "ASH_Recipe_", false, rows, done, noArt, used, ref already);
            WriteMap(rows);
            AssetDatabase.SaveAssets();
            var unused = AssetDatabase.FindAssets("t:Texture2D", new[] { Root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !used.Contains(p) && !p.Replace("\\", "/").Contains(PagesFolder))
                .Select(Path.GetFileNameWithoutExtension).ToList();
            int skippedPages = AssetDatabase.FindAssets("t:Texture2D", new[] { Root + PagesFolder }).Length;
            Debug.Log($"[ASH] FINAL ICONS (ASH_ICON_FINAL) applied {n} (already final {already}); no final icon: {string.Join(", ", noArt)}; unused final icons: {string.Join(", ", unused)}; skipped page art: {skippedPages}; mapping -> {MapPath}");
        }

        static Sprite FindArt(string name, bool item, HashSet<string> used)
        {
            string[] paths = item
                ? new[] { Root + "/Inventory/ASH_Icon_" + name + ".png", Root + "/NewIcons/" + name + ".png", Root + "/NewIcons/ASH_Icon_" + name + ".png", Root + "/Inventory/" + name + ".png" }
                : new[] { Root + "/Recipes/ASH_Recipe_" + name + ".png", Root + "/NewIcons/" + name + ".png", Root + "/NewIcons/ASH_Recipe_" + name + ".png", Root + "/Recipes/" + name + ".png" };
            foreach (var artPath in paths)
            {
                var art = AssetDatabase.LoadAssetAtPath<Sprite>(artPath);
                if (!art) continue;
                used.Add(artPath);
                return art;
            }
            return null;
        }

        static int Do<T>(string filter, string prefix, bool item, List<string[]> rows, HashSet<string> done, List<string> noArt, HashSet<string> used, ref int already) where T : ScriptableObject
        {
            int n = 0;
            foreach (var g in AssetDatabase.FindAssets(filter, new[] { "Assets/_ASH" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g); var a = AssetDatabase.LoadAssetAtPath<T>(p); if (!a) continue;
                string key = Path.GetFileNameWithoutExtension(p); if (!key.StartsWith(prefix)) continue;
                string name = key.Substring(prefix.Length);
                var art = FindArt(name, item, used);
                if (!art) { noArt.Add(name); continue; }
                var so = new SerializedObject(a); var ip = so.FindProperty("icon"); if (ip == null) continue;
                var old = ip.objectReferenceValue as Sprite;
                if (old == art) { already++; continue; }
                if (!done.Contains(p))
                {
                    string op = old ? AssetDatabase.GetAssetPath(old) : "";
                    rows.Add(new[] { p, op.Length > 0 ? AssetDatabase.AssetPathToGUID(op) : "", op, AssetDatabase.GetAssetPath(art) }); done.Add(p);
                }
                ip.objectReferenceValue = art; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(a); n++;
            }
            return n;
        }

        [MenuItem("ASH/Art/Revert Final Icons (restore previous)")]
        public static void Revert()
        {
            int n = 0;
            foreach (var r in ReadMap())
            {
                var a = AssetDatabase.LoadAssetAtPath<ScriptableObject>(r[0]); if (!a) continue;
                var so = new SerializedObject(a); var ip = so.FindProperty("icon"); if (ip == null) continue;
                string op = r[1].Length > 0 ? AssetDatabase.GUIDToAssetPath(r[1]) : "";
                Sprite old = op.Length > 0 ? AssetDatabase.LoadAllAssetsAtPath(op).OfType<Sprite>().FirstOrDefault() : null;
                ip.objectReferenceValue = old; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(a); n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] FINAL ICONS reverted {n} assets to their previous icons (mapping kept in {MapPath})");
        }

        static List<string[]> ReadMap()
        {
            var l = new List<string[]>(); if (!File.Exists(MapPath)) return l;
            foreach (var line in File.ReadAllLines(MapPath).Skip(1)) { var c = SplitCsv(line); if (c.Length >= 4) l.Add(c); }
            return l;
        }
        static string[] SplitCsv(string line)
        {
            // mapping paths have no commas today; keep a simple split and accept 4+ columns
            return line.Split(',');
        }
        static void WriteMap(List<string[]> rows)
        {
            File.WriteAllLines(MapPath, new[] { "asset,oldIconGuid,oldIconPath,newIconPath" }.Concat(rows.Select(r => string.Join(",", r))));
            AssetDatabase.ImportAsset(MapPath);
        }
    }
}
