using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>t466: assigns the 12 new Field Manual page images (Assets/_ASH/Art/Icons Misc, ASH_FieldManual_NN_*) to the matching
    /// pages of Resources/ASH_FieldManual by page number. Import settings are copied from the current page art (Sprite 2D/UI).
    /// Old images stay in Art/FieldManual; old -> new is written to Icons Misc/ASH_FieldManualArtMapping.csv. Text untouched.</summary>
    public static class ASHFieldManualArtApply
    {
        const string NewDir = "Assets/_ASH/Art/Icons Misc", MapPath = NewDir + "/ASH_FieldManualArtMapping.csv", ManualPath = "Assets/_ASH/Resources/ASH_FieldManual.asset";

        [MenuItem("ASH/Art/Apply New Field Manual Page Images (Icons Misc)")]
        public static void Apply()
        {
            var manual = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ManualPath); if (!manual) { Debug.LogWarning("[ASH] FM ART: no " + ManualPath); return; }
            var so = new SerializedObject(manual); var pages = so.FindProperty("pages"); if (pages == null || !pages.isArray) { Debug.LogWarning("[ASH] FM ART: no pages list"); return; }
            var files = AssetDatabase.FindAssets("t:Texture2D", new[] { NewDir }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            var rows = new List<string> { "page,title,oldGuid,oldPath,newPath" }; var log = new List<string>(); int n = 0;
            for (int i = 0; i < pages.arraySize; i++)
            {
                var e = pages.GetArrayElementAtIndex(i); int num = e.FindPropertyRelative("number").intValue; string title = e.FindPropertyRelative("title").stringValue;
                var sk = e.FindPropertyRelative("sketch"); var old = sk.objectReferenceValue as Sprite; string oldPath = old ? AssetDatabase.GetAssetPath(old) : "";
                string path = files.FirstOrDefault(p => Path.GetFileName(p).StartsWith($"ASH_FieldManual_{num:00}_"));
                if (path == null) { log.Add($"page {num}: no new image"); continue; }
                var ti = AssetImporter.GetAtPath(path) as TextureImporter; var oldTi = oldPath.Length > 0 ? AssetImporter.GetAtPath(oldPath) as TextureImporter : null;
                if (ti)
                {
                    if (oldTi) { var ts = new TextureImporterSettings(); oldTi.ReadTextureSettings(ts); ti.SetTextureSettings(ts); ti.textureCompression = oldTi.textureCompression; ti.maxTextureSize = Mathf.Max(oldTi.maxTextureSize, 512); }
                    ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true; ti.mipmapEnabled = false;
                    ti.SaveAndReimport();
                }
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path); if (!sp) { log.Add($"page {num}: {path} is not a sprite"); continue; }
                rows.Add($"{num},\"{title}\",{(oldPath.Length > 0 ? AssetDatabase.AssetPathToGUID(oldPath) : "")},{oldPath},{path}");
                if (old != sp) { sk.objectReferenceValue = sp; n++; }
                log.Add($"page {num} '{title}': {Path.GetFileName(oldPath)} -> {Path.GetFileName(path)}");
            }
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(manual);
            File.WriteAllLines(MapPath, rows); AssetDatabase.ImportAsset(MapPath); AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] FM ART assigned {n} new page images (mapping {MapPath}):\n - " + string.Join("\n - ", log));
        }

        /// <summary>t472: the 8 new Grandpa's Journal page images (Art/Gpa's Pages, ASH_GrandpasJournal_NN_*) onto gj_NN of
        /// Resources/ASH_GrandpasJournal (replacing the borrowed Field Manual art / letter placeholders, which stay on disk).
        /// Import settings copied from the Field Manual page art. Mapping: Gpa's Pages/ASH_GrandpasPagesArtMapping.csv. Text untouched.</summary>
        [MenuItem("ASH/Art/Apply New Grandpa's Page Images (Gpa's Pages)")]
        public static void ApplyGrandpa()
        {
            const string dirG = "Assets/_ASH/Art/Gpa's Pages", map = dirG + "/ASH_GrandpasPagesArtMapping.csv", col = "Assets/_ASH/Resources/ASH_GrandpasJournal.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(col); if (!asset) { Debug.LogWarning("[ASH] GJ ART: no " + col); return; }
            var refTi = AssetImporter.GetAtPath(NewDir + "/ASH_FieldManual_01_Air_First.png") as TextureImporter;
            var so = new SerializedObject(asset); var pages = so.FindProperty("pages");
            var files = AssetDatabase.FindAssets("t:Texture2D", new[] { dirG }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            var rows = new List<string> { "page,id,title,oldGuid,oldPath,newPath" }; var log = new List<string>(); int n = 0;
            for (int i = 0; i < pages.arraySize; i++)
            {
                var e = pages.GetArrayElementAtIndex(i); string id = e.FindPropertyRelative("id").stringValue; int num = e.FindPropertyRelative("number").intValue; string title = e.FindPropertyRelative("title").stringValue;
                var sk = e.FindPropertyRelative("sketch"); var old = sk.objectReferenceValue as Sprite; string oldPath = old ? AssetDatabase.GetAssetPath(old) : "";
                string path = files.FirstOrDefault(p => Path.GetFileName(p).StartsWith($"ASH_GrandpasJournal_{num:00}_"));
                if (path == null) { log.Add($"{id}: no new image"); continue; }
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti)
                {
                    if (refTi) { var ts = new TextureImporterSettings(); refTi.ReadTextureSettings(ts); ti.SetTextureSettings(ts); ti.textureCompression = refTi.textureCompression; ti.maxTextureSize = refTi.maxTextureSize; }
                    ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.SaveAndReimport();
                }
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path); if (!sp) { log.Add($"{id}: {path} is not a sprite"); continue; }
                rows.Add($"{num},{id},\"{title}\",{(oldPath.Length > 0 ? AssetDatabase.AssetPathToGUID(oldPath) : "")},{oldPath},{path}");
                if (old != sp) { sk.objectReferenceValue = sp; n++; }
                log.Add($"{id} '{title}': {Path.GetFileName(oldPath)} -> {Path.GetFileName(path)}");
            }
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset);
            File.WriteAllLines(map, rows); AssetDatabase.ImportAsset(map); AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] GJ ART assigned {n} new Grandpa's page images (mapping {map}):\n - " + string.Join("\n - ", log));
        }
    }
}