using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.EditorTools.Build;
using ASH.World;
using ASH.World.Accumulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// ASH/World/Place Path Markers: small visible markers at the edge of ASH_RegionLayout roads/trails (never in the path,
    /// never on terrain trees), under one parent "ASH_PathMarkers". Prefabs are swappable in ASH_PathMarkerSettings.
    /// </summary>
    public static class ASHPathMarkers
    {
        public const string SettingsPath = ASHContentBuilder.Root + "/Data/World/ASH_PathMarkerSettings.asset";
        public const string PrefabPath = ASHContentBuilder.Root + "/Prefabs/World/ASH_PathMarker_Post.prefab";
        const string MatDir = ASHContentBuilder.Root + "/Art/Markers";
        public const string RootName = "ASH_PathMarkers";

        [MenuItem("ASH/World/Place Path Markers", priority = 70)] public static void PlaceMenu() => Place();
        [MenuItem("ASH/World/Clear Path Markers", priority = 71)] public static void ClearMenu() { int n = Clear(); Debug.Log($"[ASH] Path markers cleared ({n})."); }
        [MenuItem("ASH/World/Path Marker Settings", priority = 72)] public static void SelectSettings() { Selection.activeObject = Settings(); EditorGUIUtility.PingObject(Selection.activeObject); }
        [MenuItem("ASH/World/Path Marker Preset/Sparse", priority = 73)] static void PS() => SetPreset(ASHPathMarkerSettings.Preset.Sparse);
        [MenuItem("ASH/World/Path Marker Preset/Default", priority = 74)] static void PD() => SetPreset(ASHPathMarkerSettings.Preset.Default);
        [MenuItem("ASH/World/Path Marker Preset/Dense", priority = 75)] static void PN() => SetPreset(ASHPathMarkerSettings.Preset.Dense);
        [MenuItem("ASH/World/Path Marker Preset/Extreme", priority = 76)] static void PE() => SetPreset(ASHPathMarkerSettings.Preset.Extreme);
        static void SetPreset(ASHPathMarkerSettings.Preset p) { var s = Settings(); Undo.RecordObject(s, "Marker preset"); s.ApplyPreset(p); EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s); }

        public static ASHPathMarkerSettings Settings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHPathMarkerSettings>(SettingsPath);
            if (!s)
            {
                ASHBuildUtil.Folder(ASHContentBuilder.Root + "/Data/World");
                s = ScriptableObject.CreateInstance<ASHPathMarkerSettings>(); AssetDatabase.CreateAsset(s, SettingsPath);
            }
            if (s.prefabs.Count == 0 || s.prefabs.All(p => !p)) { s.prefabs.RemoveAll(p => !p); s.prefabs.Add(PlaceholderPrefab()); EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s); }
            return s;
        }

        /// <summary>Simple placeholder: 1.2 m wooden stake (URP/Lit, takes ash) with an orange ribbon (Simple Lit, stays visible).</summary>
        public static GameObject PlaceholderPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath); if (existing) return existing;
            ASHBuildUtil.Folder(MatDir); ASHBuildUtil.Folder(System.IO.Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            var wood = Mat(MatDir + "/ASH_MarkerWood.mat", "Universal Render Pipeline/Lit", new Color(0.36f, 0.26f, 0.17f), 0.12f);
            var ribbon = Mat(MatDir + "/ASH_MarkerRibbon.mat", "Universal Render Pipeline/Simple Lit", new Color(1f, 0.33f, 0.05f), 0.2f);
            var root = new GameObject("ASH_PathMarker_Post");
            try
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder); post.name = "Stake"; post.transform.SetParent(root.transform, false);
                post.transform.localPosition = new Vector3(0f, 0.6f, 0f); post.transform.localScale = new Vector3(0.09f, 0.6f, 0.09f);
                post.GetComponent<Renderer>().sharedMaterial = wood;
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube); cap.name = "Ribbon"; cap.transform.SetParent(root.transform, false);
                cap.transform.localPosition = new Vector3(0.05f, 1.02f, 0f); cap.transform.localScale = new Vector3(0.015f, 0.32f, 0.09f); cap.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
                Object.DestroyImmediate(cap.GetComponent<Collider>()); cap.GetComponent<Renderer>().sharedMaterial = ribbon;
                var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder); band.name = "Band"; band.transform.SetParent(root.transform, false);
                band.transform.localPosition = new Vector3(0f, 1.1f, 0f); band.transform.localScale = new Vector3(0.1f, 0.04f, 0.1f);
                Object.DestroyImmediate(band.GetComponent<Collider>()); band.GetComponent<Renderer>().sharedMaterial = ribbon;
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Material Mat(string path, string shader, Color c, float smooth)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m) return m;
            m = new Material(Shader.Find(shader)); m.SetColor("_BaseColor", c); if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            AssetDatabase.CreateAsset(m, path); return m;
        }

        public static int Clear()
        {
            int n = 0;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == RootName).ToArray())
            { n += root.transform.childCount; Undo.DestroyObjectImmediate(root); }
            return n;
        }

        struct Line { public string id, name; public bool road; public float hw; public List<Vector2> pts; public int index; }

        public static string Place()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play Mode first.");
            var s = Settings(); var vs = ASHVegetationScatter.Settings(); var terrain = Terrain.activeTerrain; if (!terrain) throw new Exception("No terrain.");
            var td = terrain.terrainData; var o = terrain.GetPosition(); var size = td.size;
            var L = ASHRegionBuilder.Load(); var lines = new List<Line>(); int idx = 0;
            foreach (var r in L.roads ?? new ASHRegionBuilder.Route[0]) lines.Add(new Line { id = r.id ?? r.name, name = r.name, road = true, hw = vs.roadHalfWidth, pts = ASHRegionBuilder.Smooth(r.pts, 8), index = idx++ });
            foreach (var r in L.trails ?? new ASHRegionBuilder.Route[0]) lines.Add(new Line { id = r.id ?? r.name, name = r.name, road = false, hw = vs.trailHalfWidth, pts = ASHRegionBuilder.Smooth(r.pts, 8), index = idx++ });

            // tree hash (all terrain trees: Anthony's + scattered)
            const float C = 4f; var trees = new Dictionary<long, List<Vector2>>();
            foreach (var ti in td.treeInstances)
            {
                var p = new Vector2(o.x + ti.position.x * size.x, o.z + ti.position.z * size.z); long k = Key(p, C);
                if (!trees.TryGetValue(k, out var l)) trees[k] = l = new List<Vector2>(); l.Add(p);
            }
            Clear();
            var root = new GameObject(RootName); Undo.RegisterCreatedObjectUndo(root, "Place path markers");
            var rng = new Random(s.seed); int placed = 0, skipTree = 0, skipPath = 0, skipSlope = 0, skipCol = 0, seq = 0; var per = new StringBuilder();
            foreach (var ln in lines)
            {
                if (ln.road ? !s.includeRoads : !s.includeTrails) continue;
                var ov = s.overrides.FirstOrDefault(x => x != null && !string.IsNullOrEmpty(x.route) && (x.route == ln.id || x.route == ln.name));
                if (ov != null && ov.skip) continue;
                float spacing = ov != null && ov.spacing > 0f ? ov.spacing : s.spacing;
                float edge = ov != null && ov.edgeOffset >= 0f ? ov.edgeOffset : (ln.road ? s.roadEdgeOffset : s.trailEdgeOffset);
                var list = (ov != null && ov.prefabs.Any(p => p) ? ov.prefabs : s.prefabs).Where(p => p).ToList(); if (list.Count == 0) continue;
                var parent = new GameObject(ln.id); parent.transform.SetParent(root.transform, false); int here = 0, sideFlip = 0;
                float next = spacing * 0.5f, acc = 0f;
                for (int i = 0; i + 1 < ln.pts.Count; i++)
                {
                    Vector2 a = ln.pts[i], b = ln.pts[i + 1]; float seg = Vector2.Distance(a, b); if (seg < 1e-3f) continue;
                    while (next <= acc + seg)
                    {
                        float t = (next - acc) / seg; var c = Vector2.Lerp(a, b, t); var dir = (b - a) / seg; var nrm = new Vector2(-dir.y, dir.x);
                        next += spacing + (float)(rng.NextDouble() * 2 - 1) * s.spacingJitter;
                        var sides = s.side == ASHPathMarkerSettings.SideMode.Both ? new[] { 1, -1 } : new[] { s.side == ASHPathMarkerSettings.SideMode.Left ? 1 : s.side == ASHPathMarkerSettings.SideMode.Right ? -1 : ((sideFlip++ & 1) == 0 ? 1 : -1) };
                        foreach (int sd0 in sides)
                        {
                            bool ok = false; string why = "";
                            foreach (int sd in s.side == ASHPathMarkerSettings.SideMode.Both ? new[] { sd0 } : new[] { sd0, -sd0 })
                            foreach (float shift in new[] { 0f, 3f, -3f, 6f, -6f })
                            {
                                var p = c + dir * shift + nrm * sd * (ln.hw + edge);
                                float u = (p.x - o.x) / size.x, v = (p.y - o.z) / size.z; if (u < 0 || v < 0 || u > 1 || v > 1) { why = "out"; continue; }
                                if (NearPath(p, lines, 0.4f)) { why = "path"; continue; }
                                if (NearTree(p, trees, C, s.treeClearance)) { why = "tree"; continue; }
                                if (td.GetSteepness(u, v) > s.maxSlope) { why = "slope"; continue; }
                                float y = terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + o.y;
                                if (s.avoidColliders && Physics.OverlapSphere(new Vector3(p.x, y + 1.0f, p.y), 0.45f, ~0, QueryTriggerInteraction.Ignore).Any(col => !(col is TerrainCollider) && !col.transform.IsChildOf(root.transform))) { why = "col"; continue; }
                                var pf = s.pickMode == ASHPathMarkerSettings.PickMode.PerRoute ? list[ln.index % list.Count] : s.pickMode == ASHPathMarkerSettings.PickMode.Sequential ? list[seq++ % list.Count] : list[rng.Next(list.Count)];
                                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, parent.scene); go.transform.SetParent(parent.transform, true);
                                float yaw = (float)(rng.NextDouble() * 2 - 1) * s.randomYaw, tiltA = (float)rng.NextDouble() * 360f, tilt = (float)rng.NextDouble() * s.randomTilt;
                                go.transform.SetPositionAndRotation(new Vector3(p.x, y - s.sink, p.y), Quaternion.AngleAxis(tilt, Quaternion.Euler(0f, tiltA, 0f) * Vector3.right) * Quaternion.Euler(0f, yaw, 0f));
                                go.transform.localScale *= Mathf.Lerp(s.scaleMin, s.scaleMax, (float)rng.NextDouble());
                                if (s.accumulate && !go.GetComponent<ASHAccumulation>()) go.AddComponent<ASHAccumulation>().Apply();
                                ok = true; placed++; here++; break;
                            }
                            if (!ok) { if (why == "tree") skipTree++; else if (why == "path") skipPath++; else if (why == "slope") skipSlope++; else if (why == "col") skipCol++; }
                        }
                    }
                    acc += seg;
                }
                per.Append($"{ln.id}:{here} ");
            }
            EditorSceneManager.MarkSceneDirty(root.scene); AssetDatabase.SaveAssets();
            var msg = $"[ASH] Path markers: {placed} placed (spacing {s.spacing} m, {s.prefabs.Count} prefab(s), {s.pickMode}); skipped tree {skipTree}, path {skipPath}, slope {skipSlope}, collider {skipCol}. Per route: {per}";
            Debug.Log(msg); return msg;
        }

        static long Key(Vector2 p, float c) => ((long)Mathf.FloorToInt(p.x / c) << 32) ^ (uint)Mathf.FloorToInt(p.y / c);
        static bool NearTree(Vector2 p, Dictionary<long, List<Vector2>> h, float c, float r)
        {
            int cx = Mathf.FloorToInt(p.x / c), cz = Mathf.FloorToInt(p.y / c), n = Mathf.CeilToInt(r / c);
            for (int dz = -n; dz <= n; dz++) for (int dx = -n; dx <= n; dx++)
                if (h.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l)) foreach (var q in l) if ((q - p).sqrMagnitude < r * r) return true;
            return false;
        }
        static bool NearPath(Vector2 p, List<Line> lines, float margin)
        {
            foreach (var ln in lines)
            {
                float lim = ln.hw + margin, lim2 = lim * lim;
                for (int i = 0; i + 1 < ln.pts.Count; i++)
                {
                    Vector2 a = ln.pts[i], b = ln.pts[i + 1];
                    if (p.x < Mathf.Min(a.x, b.x) - lim || p.x > Mathf.Max(a.x, b.x) + lim || p.y < Mathf.Min(a.y, b.y) - lim || p.y > Mathf.Max(a.y, b.y) + lim) continue;
                    var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    if ((a + ab * t - p).sqrMagnitude < lim2) return true;
                }
            }
            return false;
        }
    }
}
