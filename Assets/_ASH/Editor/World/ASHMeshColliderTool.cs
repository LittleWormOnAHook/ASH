using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ASH.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// Adds a MeshCollider on the LOD0 / Level00 mesh renderer (or the first mesh when those are missing).
    /// Inspired by Genesis PCG's LOD0-child collider idea (DmRockCombiner.SyncLod0Collider /
    /// DmRockColliderRepair), rewritten without PCG combiner dependencies so it works on vendor rocks.
    /// Vendor prefab edits stay local and out of git. The open scene is never saved.
    /// </summary>
    public static class ASHMeshColliderTool
    {
        const string MenuRoot = "ASH/Tools/Mesh Colliders/";
        const string StaticMenu = MenuRoot + "Mark Added Objects Static";
        const string PrefsKey = "ASH.MeshColliderTool.MarkStatic";
        const string UndoName = "Add mesh colliders";

        public static bool MarkStaticDefault
        {
            get => EditorPrefs.GetBool(PrefsKey, true);
            set => EditorPrefs.SetBool(PrefsKey, value);
        }

        [MenuItem(StaticMenu, false, 50)]
        static void ToggleMarkStatic() => MarkStaticDefault = !MarkStaticDefault;

        [MenuItem(StaticMenu, true)]
        static bool ToggleMarkStaticValidate()
        {
            Menu.SetChecked(StaticMenu, MarkStaticDefault);
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [MenuItem(MenuRoot + "Add to Selection", false, 20)]
        public static void AddToSelection()
        {
            if (!CanRun()) return;
            var roots = Selection.gameObjects;
            if (roots == null || roots.Length == 0)
            {
                Debug.LogWarning("[ASH] Select one or more objects first.");
                return;
            }
            var r = new Report();
            Undo.SetCurrentGroupName(UndoName);
            int g = Undo.GetCurrentGroup();
            foreach (var go in roots) ApplyToRoot(go, r, allowFallback: true, applyStatic: MarkStaticDefault);
            Undo.CollapseUndoOperations(g);
            FinishScene(r, "selection");
        }

        [MenuItem(MenuRoot + "Add to Open Scene", false, 21)]
        public static void AddToOpenScene()
        {
            if (!CanRun()) return;
            var r = new Report();
            Undo.SetCurrentGroupName(UndoName);
            int g = Undo.GetCurrentGroup();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    ApplyToNamedLodMeshes(root, r, applyStatic: MarkStaticDefault);
            }
            Undo.CollapseUndoOperations(g);
            FinishScene(r, "open scene (Level00/LOD0 only, no first-mesh fallback)");
        }

        [MenuItem(MenuRoot + "Add to Prefabs in Folder...", false, 22)]
        public static void AddToPrefabsInFolder()
        {
            if (!CanRun()) return;
            string abs = EditorUtility.OpenFolderPanel("Add MeshColliders to prefabs in folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(abs)) return;
            string rel = ToAssetPath(abs);
            if (string.IsNullOrEmpty(rel) || !rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[ASH] Pick a folder inside this project's Assets/.");
                return;
            }
            ApplyToPrefabFolder(rel, allowFallback: true);
        }

        [MenuItem("ASH/World/Rocks and Cliffs/Add Mesh Colliders to Kit Prefabs", false, 50)]
        public static void AddToKitPrefabsMenu() => ApplyToKitPrefabs();

        [MenuItem("ASH/World/Rocks and Cliffs/Add Mesh Colliders to Placed Rocks", false, 51)]
        public static void AddToPlacedRocksMenu() => ApplyToPlacedRocks();

        [MenuItem("ASH/World/Rocks and Cliffs/Add Mesh Colliders and Mark Static (kit + placed)", false, 52)]
        public static void ApplyToRockKitAndPlaced()
        {
            if (!CanRun()) return;
            ApplyToKitPrefabs();
            ApplyToPlacedRocks();
        }

        public static Report ApplyToKitPrefabs()
        {
            var r = new Report();
            if (!CanRun()) return r;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prefab in KitPrefabs())
            {
                if (!prefab) continue;
                string path = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                ApplyToPrefabAsset(path, r, allowFallback: true, applyStatic: true);
            }
            LogReport(r, "rock kit prefabs (vendor edits are local and not committed)");
            return r;
        }

        public static Report ApplyToPlacedRocks()
        {
            var r = new Report();
            if (!CanRun()) return r;
            var root = GameObject.Find(ASHPlacedRock.RootName);
            if (!root)
            {
                Debug.Log("[ASH] No ASH_PlacedRocks root in the open scene.");
                return r;
            }
            Undo.SetCurrentGroupName(UndoName);
            int g = Undo.GetCurrentGroup();
            r.markedStatic += MarkStaticRecursive(root);
            foreach (var placed in root.GetComponentsInChildren<ASHPlacedRock>(true))
            {
                r.markedStatic += MarkStaticRecursive(placed.gameObject);
                if (placed.transform.childCount == 0)
                {
                    r.skippedNoMesh++;
                    r.notes.Add(placed.name + " (empty wrapper, no pieces)");
                    continue;
                }
                for (int i = 0; i < placed.transform.childCount; i++)
                    ApplyToRoot(placed.transform.GetChild(i).gameObject, r, allowFallback: true, applyStatic: true);
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(g);
            FinishScene(r, "ASH_PlacedRocks instances");
            return r;
        }

        public static int MarkStaticRecursive(GameObject go)
        {
            if (!go) return 0;
            var flags = AllStaticFlags();
            int changed = 0;
            var transforms = go.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (!t) continue;
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) == flags) continue;
                Undo.RecordObject(t.gameObject, "Mark rock static");
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
                EditorUtility.SetDirty(t.gameObject);
                changed++;
            }
            return changed;
        }

        public static StaticEditorFlags AllStaticFlags()
        {
            StaticEditorFlags flags = 0;
            foreach (StaticEditorFlags f in Enum.GetValues(typeof(StaticEditorFlags)))
                flags |= f;
            return flags;
        }

        static bool CanRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ASH] Exit Play mode first.");
                return false;
            }
            return true;
        }

        static IEnumerable<GameObject> KitPrefabs()
        {
            var s = ASHRockCliffTool.EnsureSettings();
            if (!s) yield break;
            if (s.boulders != null) foreach (var p in s.boulders) yield return p;
            if (s.rubble != null) foreach (var p in s.rubble) yield return p;
            if (s.cliffs != null) foreach (var p in s.cliffs) yield return p;
            if (s.sharp != null) foreach (var p in s.sharp) yield return p;
        }

        static void ApplyToPrefabFolder(string folder, bool allowFallback)
        {
            var r = new Report();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                ApplyToPrefabAsset(path, r, allowFallback, applyStatic: MarkStaticDefault);
            }
            LogReport(r, "prefabs in " + folder + " (vendor edits stay local and are not committed)");
        }

        static void ApplyToPrefabAsset(string path, Report r, bool allowFallback, bool applyStatic)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int addedBefore = r.added;
                int repairedBefore = r.repaired;
                int staticBefore = r.markedStatic;
                ApplyToRoot(contents, r, allowFallback, applyStatic);
                if (r.added != addedBefore || r.repaired != repairedBefore || r.markedStatic != staticBefore)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    r.prefabsTouched++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        static void ApplyToNamedLodMeshes(GameObject root, Report r, bool applyStatic)
        {
            if (!root) return;
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            var seen = new HashSet<Transform>();
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (!mf || !IsLod0Name(mf.name)) continue;
                var unit = PrefabRootOrSelf(mf.gameObject);
                if (!seen.Add(unit.transform)) continue;
                ApplyToRoot(unit, r, allowFallback: false, applyStatic: applyStatic);
            }
        }

        static void ApplyToRoot(GameObject root, Report r, bool allowFallback, bool applyStatic)
        {
            if (!root) return;
            r.visited++;
            if (applyStatic)
                r.markedStatic += MarkStaticRecursive(root);

            var existing = FirstLiveCollider(root);
            if (existing != null && existing.sharedMesh != null && existing.sharedMesh.vertexCount >= 3)
            {
                r.skippedExisting++;
                r.notes.Add(root.name + " (already had MeshCollider on " + existing.name + ")");
                return;
            }

            var mf = FindCollisionMesh(root, allowFallback, out string how);
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 3)
            {
                r.skippedNoMesh++;
                r.notes.Add(root.name + " (no Level00/LOD0 or usable mesh)");
                return;
            }

            var host = mf.gameObject;
            var mc = host.GetComponent<MeshCollider>();
            if (mc == null && existing != null)
            {
                mc = existing;
                host = mc.gameObject;
            }

            if (mc == null)
            {
                mc = Undo.AddComponent<MeshCollider>(host);
                mc.convex = false;
                mc.isTrigger = false;
                mc.sharedMesh = mf.sharedMesh;
                EditorUtility.SetDirty(mc);
                EditorUtility.SetDirty(host);
                r.added++;
                r.notes.Add(root.name + " +MeshCollider on " + host.name + " (" + how + ")");
                if (how.StartsWith("fallback", StringComparison.OrdinalIgnoreCase))
                {
                    r.fallback++;
                    r.fallbackNotes.Add(root.name + " (no Level00/LOD0, used " + host.name + ")");
                }
                return;
            }

            Undo.RecordObject(mc, UndoName);
            if (mc.convex) { mc.sharedMesh = null; mc.convex = false; }
            mc.isTrigger = false;
            if (mc.sharedMesh != mf.sharedMesh) mc.sharedMesh = mf.sharedMesh;
            if (!mc.enabled) mc.enabled = true;
            EditorUtility.SetDirty(mc);
            r.repaired++;
            r.notes.Add(root.name + " repaired MeshCollider on " + host.name + " (" + how + ")");
        }

        static MeshCollider FirstLiveCollider(GameObject root)
        {
            var cols = root.GetComponentsInChildren<MeshCollider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                var mc = cols[i];
                if (mc && mc.sharedMesh != null && mc.sharedMesh.vertexCount >= 3) return mc;
            }
            for (int i = 0; i < cols.Length; i++)
                if (cols[i]) return cols[i];
            return null;
        }

        static MeshFilter FindCollisionMesh(GameObject root, bool allowFallback, out string how)
        {
            how = "";
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter named = null;
            MeshFilter fallback = null;
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (!Usable(mf)) continue;
                if (IsLevel00Name(mf.name)) { how = "Level00"; return mf; }
                if (named == null && IsLod0Name(mf.name)) named = mf;
                if (fallback == null) fallback = mf;
            }

            foreach (var group in root.GetComponentsInChildren<LODGroup>(true))
            {
                if (!group) continue;
                var lods = group.GetLODs();
                if (lods == null || lods.Length == 0 || lods[0].renderers == null) continue;
                foreach (var rend in lods[0].renderers)
                {
                    if (!rend) continue;
                    var mf = rend.GetComponent<MeshFilter>();
                    if (!Usable(mf)) continue;
                    how = "LODGroup LOD0 (" + mf.name + ")";
                    return mf;
                }
            }

            if (named != null) { how = "LOD0 (" + named.name + ")"; return named; }
            if (allowFallback && fallback != null) { how = "fallback first mesh (" + fallback.name + ")"; return fallback; }
            how = "none";
            return null;
        }

        static bool Usable(MeshFilter mf) =>
            mf && mf.sharedMesh && mf.sharedMesh.vertexCount >= 3;

        static bool IsLevel00Name(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.Equals("Level00", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("Level00", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsLod0Name(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (IsLevel00Name(name)) return true;
            return name.Equals("LOD0", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase)
                || name.IndexOf("_LOD0", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static GameObject PrefabRootOrSelf(GameObject go)
        {
            var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            return nearest ? nearest : go;
        }

        static string ToAssetPath(string abs)
        {
            if (string.IsNullOrEmpty(abs)) return null;
            abs = abs.Replace('\\', '/');
            string data = Application.dataPath.Replace('\\', '/');
            if (abs.StartsWith(data, StringComparison.OrdinalIgnoreCase))
                return "Assets" + abs.Substring(data.Length);
            return null;
        }

        static void FinishScene(Report r, string where)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            LogReport(r, where + " (scene not saved)");
        }

        static void LogReport(Report r, string where)
        {
            var sb = new StringBuilder();
            sb.Append("[ASH] Mesh colliders on ").Append(where)
                .Append(": visited=").Append(r.visited)
                .Append(" added=").Append(r.added)
                .Append(" repaired=").Append(r.repaired)
                .Append(" skipped-existing=").Append(r.skippedExisting)
                .Append(" skipped-no-mesh=").Append(r.skippedNoMesh)
                .Append(" fallback-no-Level00=").Append(r.fallback)
                .Append(" marked-static=").Append(r.markedStatic)
                .Append(" prefabs-saved=").Append(r.prefabsTouched)
                .Append('.');
            if (r.fallbackNotes.Count > 0)
            {
                sb.Append(" Fallback (no Level00/LOD0 child): ");
                sb.Append(string.Join("; ", r.fallbackNotes));
                sb.Append('.');
            }
            foreach (var n in r.notes) sb.Append("\n  ").Append(n);
            Debug.Log(sb.ToString());
        }

        public sealed class Report
        {
            public int visited, added, repaired, skippedExisting, skippedNoMesh, fallback, markedStatic, prefabsTouched;
            public readonly List<string> notes = new List<string>();
            public readonly List<string> fallbackNotes = new List<string>();
        }
    }
}
