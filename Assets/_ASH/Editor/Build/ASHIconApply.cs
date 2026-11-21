using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Applies Rook's art-pass icons (Assets/_ASH/Art/Icons Inventory) to items and recipes, keeping the original icon files.
    /// Every change is logged to ASH_IconMapping.csv (asset, old icon GUID + path, new icon path) so it can be reverted with
    /// ASH/Art/Revert Art Icons. Icons whose drawing shows a different object (shared placeholder art) are skipped and listed.
    /// Idempotent: re-running only touches assets whose icon is not already the art icon.
    /// </summary>
    public static class ASHIconApply
    {
        const string Root = "Assets/_ASH/Art/Icons Inventory";
        const string MapPath = Root + "/ASH_IconMapping.csv";

        // shared drawings that show a different object (checked by eye, 20 Nov): left on the old icon
        static readonly HashSet<string> SkipItems = new HashSet<string> {
            "Battery", "CarBattery", "BoiledWater", "Whiskey", "CannedPeaches", "Cloth", "DryRag", "OldShirt", "Rags",
            "PocketWatch", "SilverRing", "FanBelt", "DeerSkin", "ReclaimedWood", "Shotgun", "ScrapMetal" };
        static readonly HashSet<string> SkipRecipes = new HashSet<string> { "ClothFromShirt", "BoilWater", "AmmoPouch1", "AmmoPouch2", "AmmoPouch3", "AmmoPouch4" };

        public static bool IsArtIcon(Sprite s) => s && AssetDatabase.GetAssetPath(s).StartsWith(Root + "/");

        [MenuItem("ASH/Art/Apply Art Icons (Icons Inventory)")]
        public static void Apply()
        {
            // 1. importer: Sprite (2D and UI), alpha is transparency, no compression, no mips
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
            var skipped = new List<string>(); var noArt = new List<string>(); int n = 0;
            n += Do<ASHItemDefinition>("t:ASHItemDefinition", "ASH_Item_", Root + "/Inventory/ASH_Icon_", SkipItems, rows, done, skipped, noArt);
            n += Do<ASHRecipe>("t:ASHRecipe", "ASH_Recipe_", Root + "/Recipes/ASH_Recipe_", SkipRecipes, rows, done, skipped, noArt);
            WriteMap(rows);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] ICONS applied {n} (importers fixed {imp}); kept old icon (art shows another object): {string.Join(", ", skipped)}; no art file: {string.Join(", ", noArt)}; mapping -> {MapPath}");
        }

        static int Do<T>(string filter, string prefix, string artPrefix, HashSet<string> skip, List<string[]> rows, HashSet<string> done, List<string> skipped, List<string> noArt) where T : ScriptableObject
        {
            int n = 0;
            foreach (var g in AssetDatabase.FindAssets(filter, new[] { "Assets/_ASH" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g); var a = AssetDatabase.LoadAssetAtPath<T>(p); if (!a) continue;
                string key = Path.GetFileNameWithoutExtension(p); if (!key.StartsWith(prefix)) continue;
                string name = key.Substring(prefix.Length);
                if (skip.Contains(name)) { skipped.Add(name); continue; }
                var art = AssetDatabase.LoadAssetAtPath<Sprite>(artPrefix + name + ".png");
                if (!art) { noArt.Add(name); continue; }
                var so = new SerializedObject(a); var ip = so.FindProperty("icon"); if (ip == null) continue;
                var old = ip.objectReferenceValue as Sprite;
                if (old == art) continue;
                if (!done.Contains(p))
                {
                    string op = old ? AssetDatabase.GetAssetPath(old) : "";
                    rows.Add(new[] { p, op.Length > 0 ? AssetDatabase.AssetPathToGUID(op) : "", op, artPrefix + name + ".png" }); done.Add(p);
                }
                ip.objectReferenceValue = art; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(a); n++;
            }
            return n;
        }

        [MenuItem("ASH/Art/Revert Art Icons (restore originals)")]
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
            Debug.Log($"[ASH] ICONS reverted {n} assets to their original icons (mapping kept in {MapPath})");
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
