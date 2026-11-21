using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>t457: applies the FINAL icon set (Assets/_ASH/Art/ICON FINAL, Inventory + Recipes) to every item and recipe by name,
    /// keeping all older icon files. Journal page images are not touched (t458). Importers are set to Sprite (Single, alpha,
    /// uncompressed, no mips). Every change is logged to ICON FINAL/ASH_IconFinalMapping.csv (asset, old icon GUID + path, new
    /// icon path) and can be undone with ASH/Art/Revert Final Icons. Idempotent. Logs unmatched assets and unused final icons.</summary>
    public static class ASHIconFinalApply
    {
        public const string Root = "Assets/_ASH/Art/ICON FINAL";
        const string MapPath = Root + "/ASH_IconFinalMapping.csv";

        [MenuItem("ASH/Art/Apply Final Icons (ICON FINAL)")]
        public static void Apply()
        {
            int imp = 0;
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { Root }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter; if (!ti) continue;
                if (ti.textureType == TextureImporterType.Sprite && ti.spriteImportMode == SpriteImportMode.Single && ti.alphaIsTransparency
                    && ti.textureCompression == TextureImporterCompression.Uncompressed && !ti.mipmapEnabled) continue;
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = false; ti.SaveAndReimport(); imp++;
            }
            var rows = ReadMap(); var done = new HashSet<string>(rows.Select(r => r[0]));
            var noArt = new List<string>(); var used = new HashSet<string>(); int n = 0, already = 0;
            n += Do<ASHItemDefinition>("t:ASHItemDefinition", "ASH_Item_", Root + "/Inventory/ASH_Icon_", rows, done, noArt, used, ref already);
            n += Do<ASHRecipe>("t:ASHRecipe", "ASH_Recipe_", Root + "/Recipes/ASH_Recipe_", rows, done, noArt, used, ref already);
            WriteMap(rows);
            AssetDatabase.SaveAssets();
            var unused = AssetDatabase.FindAssets("t:Texture2D", new[] { Root }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !used.Contains(p)).Select(Path.GetFileNameWithoutExtension).ToList();
            Debug.Log($"[ASH] FINAL ICONS applied {n} (already final {already}, importers fixed {imp}); no final icon: {string.Join(", ", noArt)}; unused final icons: {string.Join(", ", unused)}; mapping -> {MapPath}");
        }

        static int Do<T>(string filter, string prefix, string artPrefix, List<string[]> rows, HashSet<string> done, List<string> noArt, HashSet<string> used, ref int already) where T : ScriptableObject
        {
            int n = 0;
            foreach (var g in AssetDatabase.FindAssets(filter, new[] { "Assets/_ASH" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g); var a = AssetDatabase.LoadAssetAtPath<T>(p); if (!a) continue;
                string key = Path.GetFileNameWithoutExtension(p); if (!key.StartsWith(prefix)) continue;
                string name = key.Substring(prefix.Length), artPath = artPrefix + name + ".png";
                var art = AssetDatabase.LoadAssetAtPath<Sprite>(artPath);
                if (!art) { noArt.Add(name); continue; }
                used.Add(artPath);
                var so = new SerializedObject(a); var ip = so.FindProperty("icon"); if (ip == null) continue;
                var old = ip.objectReferenceValue as Sprite;
                if (old == art) { already++; continue; }
                if (!done.Contains(p))
                {
                    string op = old ? AssetDatabase.GetAssetPath(old) : "";
                    rows.Add(new[] { p, op.Length > 0 ? AssetDatabase.AssetPathToGUID(op) : "", op, artPath }); done.Add(p);
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
            foreach (var line in File.ReadAllLines(MapPath).Skip(1)) { var c = line.Split(','); if (c.Length >= 4) l.Add(c); }
            return l;
        }
        static void WriteMap(List<string[]> rows)
        {
            File.WriteAllLines(MapPath, new[] { "asset,oldIconGuid,oldIconPath,newIconPath" }.Concat(rows.Select(r => string.Join(",", r))));
            AssetDatabase.ImportAsset(MapPath);
        }
    }
}