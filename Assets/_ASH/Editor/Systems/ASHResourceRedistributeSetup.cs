#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using ASH.EditorTools.Systems;
using ASH.Interaction;
using ASH.World;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// C9: assign stable ids, disable (never delete) auto-scattered resource nodes, and spawn pooled clones
    /// under ASH_RedistributedResources. Hand-placed nodes are marked keep and left alone.
    /// Menus never save the open scene and never touch Gaia Water, lights, the tire, porch, truck-bed or hands.
    /// Do not auto-run Redistribute — Setup only stamps ids.
    /// </summary>
    public static class ASHResourceRedistributeSetup
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_ResourceRedistributeSettings.asset";
        public const string CloneRoot = "ASH_RedistributedResources";
        static readonly string[] ScatterRoots = { "ASH_ResourceScatter", "ASH_ScatteredResources", "ASH_PoiResources" };
        static readonly string[] ProtectedName = {
            "Tire replacement", "Gaia Water", "Gaia Planar", "ASH_Player", "Grandpa Cabin", "Truck", "truck"
        };
        static readonly string[] ProtectedContains = {
            "porch", "tire replacement", "gaia water", "truck-bed", "truckbed", "held_"
        };

        [MenuItem("ASH/Systems/Resources/Setup Resource IDs (idempotent)", false, 300)]
        public static void SetupIds()
        {
            var settings = ASHMenuInstaller.Ensure<ASHResourceRedistributeSettings>(SettingsPath);
            int stamped = 0, keep = 0, skipped = 0;
            foreach (var node in Object.FindObjectsByType<ASHResourceNode>(FindObjectsInactive.Include))
            {
                if (!node || IsProtected(node.transform)) { skipped++; continue; }
                var id = node.GetComponent<ASHResourceId>() ?? Undo.AddComponent<ASHResourceId>(node.gameObject);
                if (string.IsNullOrEmpty(id.id))
                {
                    id.id = "res_" + ShortGuid(node.gameObject);
                    EditorUtility.SetDirty(id);
                    stamped++;
                }
                bool underScatter = UnderScatterRoot(node.transform);
                if (id.keep != !underScatter)
                {
                    id.keep = !underScatter;
                    EditorUtility.SetDirty(id);
                }
                if (id.keep) keep++;
            }
            Selection.activeObject = settings;
            Debug.Log($"[ASH] Resource IDs: stamped {stamped}, keep (hand-placed) {keep}, protected skipped {skipped}. Scene not saved. Redistribute is a separate menu and was not run.");
        }

        [MenuItem("ASH/Systems/Resources/Select Redistribute Settings", false, 301)]
        public static void SelectSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHResourceRedistributeSettings>(SettingsPath);
            if (!s) SetupIds();
            s = AssetDatabase.LoadAssetAtPath<ASHResourceRedistributeSettings>(SettingsPath);
            Selection.activeObject = s;
            EditorGUIUtility.PingObject(s);
        }

        [MenuItem("ASH/Systems/Resources/Redistribute Scattered Resources", false, 302)]
        public static void Redistribute()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHResourceRedistributeSettings>(SettingsPath);
            if (!s) { SetupIds(); s = AssetDatabase.LoadAssetAtPath<ASHResourceRedistributeSettings>(SettingsPath); }
            var terrain = Terrain.activeTerrain;
            if (!terrain) { Debug.LogWarning("[ASH] Redistribute: no terrain."); return; }

            var movable = new List<ASHResourceNode>();
            foreach (var node in Object.FindObjectsByType<ASHResourceNode>(FindObjectsInactive.Include))
            {
                if (!node || IsProtected(node.transform)) continue;
                var id = node.GetComponent<ASHResourceId>() ?? node.gameObject.AddComponent<ASHResourceId>();
                if (string.IsNullOrEmpty(id.id)) id.id = "res_" + ShortGuid(node.gameObject);
                if (id.keep) continue;
                if (!UnderScatterRoot(node.transform) && !id.disabledByRedistribute) { id.keep = true; continue; }
                movable.Add(node);
            }

            var root = GameObject.Find(CloneRoot);
            if (root) Undo.DestroyObjectImmediate(root);
            root = new GameObject(CloneRoot);
            Undo.RegisterCreatedObjectUndo(root, "ASH redistribute");
            root.AddComponent<ASHResourceRedistributeRoot>();

            int seed = s.seed != 0 ? s.seed : 91033;
            var rnd = new System.Random(seed);
            var blocked = CollectBlocked();
            var placed = new List<Vector3>();
            int n = 0;
            int want = Mathf.Max(0, Mathf.RoundToInt(movable.Count * Mathf.Clamp01(s.relocateFraction)));
            var td = terrain.terrainData; var origin = terrain.GetPosition(); var size = td.size;

            foreach (var src in movable)
            {
                if (!src) continue;
                var id = src.GetComponent<ASHResourceId>();
                if (!src.gameObject.activeSelf && id && id.disabledByRedistribute) { /* already off, we will still clone a fresh one */ }
                if (src.gameObject.activeSelf)
                {
                    Undo.RecordObject(src.gameObject, "ASH redistribute");
                    src.gameObject.SetActive(false);
                }
                if (id)
                {
                    id.disabledByRedistribute = true;
                    EditorUtility.SetDirty(id);
                }
            }

            for (int i = 0; i < want; i++)
            {
                var src = movable[i % movable.Count];
                bool found = false;
                Vector3 p = Vector3.zero;
                for (int t = 0; t < 40; t++)
                {
                    p = new Vector3(origin.x + 30f + (float)rnd.NextDouble() * (size.x - 60f), 0f, origin.z + 30f + (float)rnd.NextDouble() * (size.z - 60f));
                    p.y = terrain.SampleHeight(p) + origin.y;
                    if (!ClearOf(p, s.campExclusionMeters, blocked)) continue;
                    if (!ClearOf(p, s.minSpacing, placed)) continue;
                    var nrm = td.GetInterpolatedNormal((p.x - origin.x) / size.x, (p.z - origin.z) / size.z);
                    if (nrm.y < 0.82f) continue;
                    found = true; break;
                }
                if (!found) continue;
                var prefab = PrefabUtility.GetCorrespondingObjectFromSource(src.gameObject);
                GameObject go;
                if (prefab) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
                else go = Object.Instantiate(src.gameObject);
                go.SetActive(true);
                go.transform.SetParent(root.transform, true);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
                var cloneId = go.GetComponent<ASHResourceId>() ?? go.AddComponent<ASHResourceId>();
                cloneId.id = (src.GetComponent<ASHResourceId>() != null ? src.GetComponent<ASHResourceId>().id : "res") + "_c" + n;
                cloneId.keep = false;
                cloneId.disabledByRedistribute = false;
                Undo.RegisterCreatedObjectUndo(go, "ASH redistribute");
                placed.Add(p);
                n++;
            }
            Debug.Log($"[ASH] Redistribute: disabled {movable.Count} auto-scattered original(s), placed {n} clone(s) under {CloneRoot}. Hand-placed (keep) and protected objects were not touched. Scene not saved.");
        }

        [MenuItem("ASH/Systems/Resources/Revert Redistributed Resources", false, 303)]
        public static void Revert()
        {
            var root = GameObject.Find(CloneRoot);
            int clones = root ? root.transform.childCount : 0;
            if (root) Undo.DestroyObjectImmediate(root);
            int restored = 0;
            foreach (var id in Object.FindObjectsByType<ASHResourceId>(FindObjectsInactive.Include))
            {
                if (!id || !id.disabledByRedistribute) continue;
                Undo.RecordObject(id.gameObject, "ASH redistribute revert");
                id.gameObject.SetActive(true);
                id.disabledByRedistribute = false;
                EditorUtility.SetDirty(id);
                restored++;
            }
            Debug.Log($"[ASH] Redistribute reverted: removed {clones} clone(s), re-enabled {restored} original(s). Scene not saved.");
        }

        static bool UnderScatterRoot(Transform t)
        {
            for (var p = t; p; p = p.parent)
                foreach (var n in ScatterRoots)
                    if (p.name == n) return true;
            return false;
        }

        public static bool IsProtected(Transform t)
        {
            for (var p = t; p; p = p.parent)
            {
                var n = p.name;
                for (int i = 0; i < ProtectedName.Length; i++)
                    if (n.IndexOf(ProtectedName[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                var lower = n.ToLowerInvariant();
                for (int i = 0; i < ProtectedContains.Length; i++)
                    if (lower.Contains(ProtectedContains[i])) return true;
            }
            return false;
        }

        static List<Vector3> CollectBlocked()
        {
            var list = new List<Vector3>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t && IsProtected(t)) list.Add(t.position);
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (l && l.type != LightType.Directional) list.Add(l.transform.position);
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player) list.Add(player.transform.position);
            return list;
        }

        static bool ClearOf(Vector3 p, float radius, List<Vector3> blocked)
        {
            float r2 = radius * radius;
            for (int i = 0; i < blocked.Count; i++)
            {
                var d = blocked[i] - p; d.y = 0f;
                if (d.sqrMagnitude < r2) return false;
            }
            return true;
        }

        static string ShortGuid(GameObject go)
        {
            return Mathf.Abs(go.GetHashCode()).ToString("x");
        }
    }
}
#endif
