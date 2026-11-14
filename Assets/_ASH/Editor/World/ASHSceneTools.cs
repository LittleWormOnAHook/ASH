using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Read-only reports + safe asset moves used by the bridge. Never edits terrain heights.</summary>
    public static class ASHSceneTools
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AshBridge"));
        static void Write(string file, string text) { Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root, file), text); }
        public static string Arg() { var p = Path.Combine(Root, "args.txt"); return File.Exists(p) ? File.ReadAllText(p).Trim() : ""; }

        public static void ReportScene()
        {
            var sb = new StringBuilder();
            var s = EditorSceneManager.GetActiveScene();
            sb.AppendLine("scene=" + s.path);
            foreach (var go in s.GetRootGameObjects())
                sb.AppendLine("root: " + go.name + " active=" + go.activeSelf + " pos=" + go.transform.position + " comps=" + string.Join(",", go.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name)) + " children=" + go.transform.childCount);
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                var d = t.terrainData;
                sb.AppendLine($"terrain '{t.name}' pos={t.transform.position} data={AssetDatabase.GetAssetPath(d)}");
                if (!d) continue;
                sb.AppendLine($"  size={d.size} heightmapRes={d.heightmapResolution} alphaRes={d.alphamapResolution} baseRes={d.baseMapResolution} detailRes={d.detailResolution} pixelError={t.heightmapPixelError} basemapDist={t.basemapDistance} drawInstanced={t.drawInstanced}");
                var h = d.GetHeights(0, 0, d.heightmapResolution, d.heightmapResolution);
                float mn = 1, mx = 0; foreach (var v in h) { if (v < mn) mn = v; if (v > mx) mx = v; }
                sb.AppendLine($"  heights01 min={mn:F4} max={mx:F4} -> m min={mn * d.size.y:F1} max={mx * d.size.y:F1}");
                // probe a few known rows/cols (no-flip convention: row r -> z = r*res step)
                int R = d.heightmapResolution;
                sb.AppendLine($"  sample h[0,0]={h[0, 0]:F4} h[0,R-1]={h[0, R - 1]:F4} h[R-1,0]={h[R - 1, 0]:F4} h[1655,1905]={(R > 1905 ? h[1655, 1905] : -1):F4}");
                foreach (var l in d.terrainLayers) sb.AppendLine("  layer " + (l ? l.name + " diffuse=" + AssetDatabase.GetAssetPath(l.diffuseTexture) + " tile=" + l.tileSize : "null"));
                sb.AppendLine($"  trees={d.treeInstanceCount} protos={d.treePrototypes.Length} details={d.detailPrototypes.Length}");
            }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                sb.AppendLine($"light '{l.name}' {l.type} rot={l.transform.eulerAngles} int={l.intensity} col={l.color}");
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                sb.AppendLine($"camera '{c.name}' pos={c.transform.position} far={c.farClipPlane} tag={c.tag}");
            sb.AppendLine($"fog={RenderSettings.fog} mode={RenderSettings.fogMode} dens={RenderSettings.fogDensity} col={RenderSettings.fogColor} sky={AssetDatabase.GetAssetPath(RenderSettings.skybox)} sun={(RenderSettings.sun ? RenderSettings.sun.name : "none")}");
            sb.AppendLine("buildScenes=" + string.Join(";", EditorBuildSettings.scenes.Select(x => x.path)));
            Write("scene_report.txt", sb.ToString());
            Debug.Log("[ASH] scene report written");
        }

        /// <summary>args.txt = search filter (AssetDatabase.FindAssets syntax) [| folder]. Writes asset paths.</summary>
        public static void FindAssets()
        {
            var a = Arg().Split('|');
            var folders = a.Length > 1 ? new[] { a[1] } : new[] { "Assets" };
            var paths = AssetDatabase.FindAssets(a[0], folders).Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(x => x).ToArray();
            Write("find_report.txt", paths.Length + " results\n" + string.Join("\n", paths.Take(400)));
            Debug.Log("[ASH] find '" + a[0] + "' -> " + paths.Length);
        }

        /// <summary>args.txt = "from|to". AssetDatabase.MoveAsset keeps GUIDs.</summary>
        public static void MoveAsset()
        {
            var a = Arg().Split('|');
            ASH.EditorTools.World.ASHTerrainImporter.EnsureFolder(Path.GetDirectoryName(a[1]).Replace('\\', '/'));
            var err = AssetDatabase.MoveAsset(a[0], a[1]);
            if (!string.IsNullOrEmpty(err)) throw new System.Exception("MoveAsset failed: " + err);
            Debug.Log("[ASH] moved " + a[0] + " -> " + a[1]);
        }

        /// <summary>Replace a scene path in build settings (keeps order), adding if missing.</summary>
        public static void FixBuildScenes()
        {
            var a = Arg().Split('|');
            var list = EditorBuildSettings.scenes.Where(x => x.path != a[0] && x.path != a[1]).ToList();
            list.Insert(0, new EditorBuildSettingsScene(a[1], true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[ASH] build scenes: " + string.Join(";", list.Select(x => x.path)));
        }
            /// <summary>args.txt = new terrain height (Y size). Only rescales; height samples are not modified.</summary>
        public static void SetTerrainHeightScale()
        {
            float y = float.Parse(Arg(), System.Globalization.CultureInfo.InvariantCulture);
            var t = Terrain.activeTerrain; if (!t) throw new System.Exception("no active terrain");
            var d = t.terrainData; Undo.RecordObject(d, "ASH terrain height");
            var s = d.size; s.y = y; d.size = s; EditorUtility.SetDirty(d); AssetDatabase.SaveAssets();
            Debug.Log("[ASH] terrain size now " + d.size);
        }

        /// <summary>Adds tags Invector expects (e.g. "Enemy") if missing. Layers are untouched.</summary>
        public static void EnsureTags()
        {
            var tm = new UnityEditor.SerializedObject(UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tags = tm.FindProperty("tags");
            var added = new System.Collections.Generic.List<string>();
            foreach (var want in new[] { "Enemy", "CompanionAI", "Weapon" })
            {
                bool found = false;
                for (int i = 0; i < tags.arraySize; i++) if (tags.GetArrayElementAtIndex(i).stringValue == want) found = true;
                if (found) continue;
                tags.InsertArrayElementAtIndex(tags.arraySize); tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = want; added.Add(want);
            }
            tm.ApplyModifiedProperties(); UnityEditor.AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[ASH] Tags ensured; added: " + (added.Count > 0 ? string.Join(", ", added) : "none"));
        }
    }
}
