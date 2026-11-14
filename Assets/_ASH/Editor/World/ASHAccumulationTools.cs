using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.EditorTools.Build;
using ASH.World;
using ASH.World.Accumulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// Phase 1 of the ash/snow accumulation system (buildup shader). Menus under ASH/World/Accumulation.
    /// Materials are ASH copies (vendor files untouched); pickups/interactables are always skipped.
    /// </summary>
    [InitializeOnLoad]
    public static class ASHAccumulationTools
    {
        public const string Dir = ASHContentBuilder.Root + "/Art/Accumulation";
        public const string MatDir = Dir + "/Materials";
        public const string TreeDir = Dir + "/Trees";
        public const string DriftPath = ASHContentBuilder.Root + "/Art/Terrain/ASH_DriftMask.png";
        public const string TerrainMatPath = Dir + "/ASH_TerrainAccum.mat";
        const string AshTexPath = ASHContentBuilder.Root + "/Art/Terrain/Textures/ASH_TL_Ash_D.png";
        const string SnowTexPath = "Assets/Procedural Worlds/Packages - Install/Asset Samples/NatureManufacture/T_Ground_Snow_1_A_Sm.tga";

        static ASHAccumulationTools() { ASHAccumulation.VariantProvider = VariantAsset; }

        // ------------------------------------------------------------------ material variants (assets)
        public const string VariantRoot = ASHContentBuilder.Root + "/Materials/Accumulation";

        /// <summary>Persistent ASH/Accumulation Lit copy of a material, cached at Assets/_ASH/Materials/Accumulation/&lt;source&gt;/ (vendor files untouched).</summary>
        public static Material VariantAsset(Material src)
        {
            if (!ASHAccumulation.IsSupported(src)) return src;
            if (!Shader.Find(ASHAccumulation.ShaderName)) { Debug.LogError("[ASH] Shader ASH/Accumulation Lit missing."); return src; }
            var srcPath = AssetDatabase.GetAssetPath(src);
            string key = string.IsNullOrEmpty(srcPath) ? ((uint)San(src.name).GetHashCode()).ToString("x8") : AssetDatabase.AssetPathToGUID(srcPath).Substring(0, 8);
            if (AssetDatabase.IsSubAsset(src)) key += "_" + ((uint)src.name.GetHashCode()).ToString("x4");
            var legacy = $"{MatDir}/{San(src.name)}_{key}_Accum.mat"; // phase-1 location: reuse if present
            var old = AssetDatabase.LoadAssetAtPath<Material>(legacy); if (old) { _reused++; return old; }
            var dir = $"{VariantRoot}/{SourceFolder(srcPath)}"; ASHBuildUtil.Folder(dir);
            var path = $"{dir}/{San(src.name)}_{key}_Accum.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing) { _reused++; return existing; }
            var m = ASHAccumulation.CreateVariant(src); if (!m) return src; m.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(m, path); _created++; CreatedPaths.Add(path); return m;
        }
        static int _created, _reused;
        internal static readonly List<string> CreatedPaths = new List<string>();
        /// <summary>Top folder under Assets (vendor pack name, e.g. "HQ Survival Pack"); "_Scene" for unsaved materials.</summary>
        static string SourceFolder(string srcPath)
        {
            if (string.IsNullOrEmpty(srcPath) || !srcPath.StartsWith("Assets/")) return "_Scene";
            var parts = srcPath.Split('/'); return parts.Length > 2 ? San(parts[1]) : "_Assets";
        }
        static string San(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }

        /// <summary>exec ASH.EditorTools.World.ASHAccumulationTools.Diagnose: shader compile messages + scene state.</summary>
        public static void Diagnose()
        {
            var sb = new StringBuilder("[ASH] ACCUM diagnose: ");
            foreach (var n in new[] { "ASH/Accumulation Lit", "ASH/Terrain Lit Accum", "Hidden/ASH/Terrain Lit Accum (Add Pass)", "Hidden/ASH/Terrain Lit Accum (Base Pass)" })
            {
                var s = Shader.Find(n); if (!s) { sb.Append($"{n}: MISSING; "); continue; }
                var msgs = ShaderUtil.GetShaderMessages(s); sb.Append($"{n}: {(ShaderUtil.ShaderHasError(s) ? "ERROR" : "ok")} ({msgs.Length} msgs); ");
                foreach (var m in msgs) sb.Append($"\n   [{m.severity}] {m.message} ({Path.GetFileName(m.file)}:{m.line} {m.platform})");
            }
            var t = Terrain.activeTerrain; sb.Append($"\n terrain mat: {(t && t.materialTemplate ? t.materialTemplate.name + " / " + t.materialTemplate.shader.name : "none")}");
            var c = Object.FindAnyObjectByType<ASHAccumulationController>(); sb.Append($"; controller: {(c ? c.name : "none")}; layers {Shader.GetGlobalVector("_ASH_Layers")}");
            Debug.Log(sb.ToString());
        }

        /// <summary>exec ASH.EditorTools.World.ASHAccumulationTools.Report: per-object list of converted vs skipped renderers (and why).</summary>
        public static void Report()
        {
            var sb = new StringBuilder("[ASH] ACCUM report:");
            foreach (var a in Object.FindObjectsByType<ASHAccumulation>(FindObjectsInactive.Include))
            {
                int conv = 0; var skip = new List<string>();
                foreach (var r in a.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial; string sh = m && m.shader ? m.shader.name : "none";
                    if (sh == ASHAccumulation.ShaderName) { conv++; continue; }
                    skip.Add($"{r.name} [{(ASHAccumulation.IsExcluded(r.transform) ? "pickup/interactable" : sh)}]");
                }
                sb.Append($"\n {a.name}: {conv} converted; skipped: {string.Join(", ", skip)}");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>exec ASH.EditorTools.World.ASHAccumulationTools.ResetLook: ash colour + path reduction back to script defaults.</summary>
        public static void ResetLook()
        {
            var c = Object.FindAnyObjectByType<ASHAccumulationController>(); if (!c) return;
            var tmp = new GameObject("tmp"); tmp.SetActive(false); var d = tmp.AddComponent<ASHAccumulationController>();
            Undo.RecordObject(c, "Reset accumulation look");
            c.ashColor = d.ashColor; c.snowColor = d.snowColor; c.ashTextureMean = d.ashTextureMean; c.pathDepthReduction = d.pathDepthReduction; c.pathCoverageReduction = d.pathCoverageReduction;
            Object.DestroyImmediate(tmp); c.PushGlobals(); EditorUtility.SetDirty(c);
            Debug.Log($"[ASH] Accumulation look reset: ash {c.ashColor}, path depth -{c.pathDepthReduction:P0}, path coverage -{c.pathCoverageReduction:P0}");
        }

        // ------------------------------------------------------------------ scene setup
        [MenuItem("ASH/World/Accumulation/1. Setup Scene (controller + terrain material)", priority = 100)]
        public static void SetupScene()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play Mode first.");
            var terrain = Terrain.activeTerrain; if (!terrain) throw new Exception("No terrain.");
            if (!AssetDatabase.LoadAssetAtPath<Texture2D>(DriftPath)) GenerateDriftMask();
            var ctrl = Object.FindAnyObjectByType<ASHAccumulationController>();
            if (!ctrl)
            {
                var go = new GameObject("ASH_Accumulation"); ctrl = go.AddComponent<ASHAccumulationController>();
                Undo.RegisterCreatedObjectUndo(go, "ASH Accumulation");
            }
            ctrl.terrain = terrain;
            ctrl.ashTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AshTexPath);
            ctrl.snowTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SnowTexPath);
            if (!ctrl.snowTexture) Debug.LogWarning("[ASH] Vendor snow texture not found; snow uses flat white. " + SnowTexPath);
            ctrl.driftMask = AssetDatabase.LoadAssetAtPath<Texture2D>(DriftPath);

            // terrain material: ASH copy of the current one on the accumulation terrain shader
            var tsh = Shader.Find("ASH/Terrain Lit Accum"); if (!tsh) throw new Exception("Shader ASH/Terrain Lit Accum missing (compile errors?).");
            var tm = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath);
            if (!tm)
            {
                ASHBuildUtil.Folder(Dir);
                var cur = terrain.materialTemplate;
                tm = cur && cur.shader && cur.shader.name.Contains("Terrain") ? new Material(cur) : new Material(tsh);
                tm.shader = tsh; tm.name = "ASH_TerrainAccum"; AssetDatabase.CreateAsset(tm, TerrainMatPath);
            }
            if (terrain.materialTemplate != tm) { ctrl.originalTerrainMaterial = terrain.materialTemplate; terrain.materialTemplate = tm; }
            ctrl.terrainMaterial = tm; ctrl.applyTerrainMaterial = true;
            ctrl.PushGlobals();
            EditorUtility.SetDirty(ctrl); EditorUtility.SetDirty(terrain); EditorSceneManager.MarkSceneDirty(ctrl.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] Accumulation scene setup: controller '{ctrl.name}', terrain material {TerrainMatPath} (was {(ctrl.originalTerrainMaterial ? ctrl.originalTerrainMaterial.name : "built-in default")}), drift mask {DriftPath}. Scene marked dirty (not saved).");
        }

        [MenuItem("ASH/World/Accumulation/Preset/Light", priority = 140)] static void PL() => Preset(ASHAccumulationController.Preset.Light);
        [MenuItem("ASH/World/Accumulation/Preset/Default", priority = 141)] static void PD() => Preset(ASHAccumulationController.Preset.Default);
        [MenuItem("ASH/World/Accumulation/Preset/Heavy", priority = 142)] static void PH() => Preset(ASHAccumulationController.Preset.Heavy);
        [MenuItem("ASH/World/Accumulation/Preset/Extreme", priority = 143)] static void PE() => Preset(ASHAccumulationController.Preset.Extreme);
        static void Preset(ASHAccumulationController.Preset p)
        {
            var c = Object.FindAnyObjectByType<ASHAccumulationController>(); if (!c) { Debug.LogWarning("[ASH] No ASHAccumulationController; run Setup Scene."); return; }
            Undo.RecordObject(c, "Accumulation preset"); c.ApplyPreset(p); EditorUtility.SetDirty(c);
        }

        // ------------------------------------------------------------------ drift mask
        [MenuItem("ASH/World/Accumulation/2. Generate Drift Mask", priority = 110)]
        public static void GenerateDriftMask()
        {
            var terrain = Terrain.activeTerrain; if (!terrain) throw new Exception("No terrain.");
            var ctrl = Object.FindAnyObjectByType<ASHAccumulationController>();
            Vector2 wind = ctrl ? ctrl.prevailingWind : new Vector2(1f, 0f); if (wind.sqrMagnitude < 1e-4f) wind = Vector2.right; wind.Normalize();
            var d = terrain.terrainData; var size = d.size; var o = terrain.GetPosition();
            const int N = 1024; float cell = size.x / N; var t0 = DateTime.Now;
            var h = new float[N * N]; var slope = new float[N * N]; var nrm = new Vector3[N * N];
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N, v = (z + 0.5f) / N; int k = z * N + x;
                h[k] = d.GetInterpolatedHeight(u, v); slope[k] = d.GetSteepness(u, v); nrm[k] = d.GetInterpolatedNormal(u, v);
            }
            // hollows: blurred height minus height (separable box blur, radius 8 cells ~ 31 m)
            var blur = BoxBlur(h, N, 8);
            // lee shelter: max upwind horizon tangent within ~230 m
            var shelter = new float[N * N];
            for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
            {
                int k = z * N + x; float maxTan = 0f;
                for (int s = 1; s <= 40; s++)
                {
                    float dist = s * 1.5f * cell; int ux = Mathf.RoundToInt(x - wind.x * s * 1.5f), uz = Mathf.RoundToInt(z - wind.y * s * 1.5f);
                    if (ux < 0 || uz < 0 || ux >= N || uz >= N) break;
                    float tan = (h[uz * N + ux] - h[k]) / dist; if (tan > maxTan) maxTan = tan;
                }
                shelter[k] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.5f, maxTan));
            }
            // compaction along roads/trails
            var vs = ASHVegetationScatter.Settings(); float extra = ctrl ? ctrl.pathCorridorExtra : 0.5f, fall = ctrl ? ctrl.pathFalloffMetres : 5f;
            float roadHW = vs.roadHalfWidth + extra, trailHW = vs.trailHalfWidth + extra;
            var roadD = RouteDistance(o, size, N, true, false); var trailD = RouteDistance(o, size, N, false, true); int onPath = 0;
            var px = new Color32[N * N]; double sumD = 0; int knee = 0, ankle = 0;
            for (int k = 0; k < N * N; k++)
            {
                int x = k % N, z = k / N;
                float hollow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 9f, blur[k] - h[k]));
                float ridge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 6f, h[k] - blur[k]));
                float windward = Mathf.Clamp01(-(nrm[k].x * wind.x + nrm[k].z * wind.y) * 3f);
                float exposure = Mathf.Clamp01((ridge * 0.6f + windward * 0.6f) * (1f - shelter[k]));
                float slopeKeep = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(32f, 55f, slope[k]));
                float drift = Mathf.Max(shelter[k], hollow * 0.85f); drift *= drift; // mostly ankle-deep; knee-deep only in strong lee/hollows
                float depth = 0.35f + 0.65f * drift - 0.25f * exposure;
                depth *= Mathf.Lerp(0.85f, 1.15f, Mathf.PerlinNoise(x * 0.05f + 3.3f, z * 0.05f + 8.1f));
                // main paths: factor 1 on the painted surface, falling to 0 over pathFalloffMetres (reduction applied at runtime from A)
                float pf = Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(roadHW, roadHW + fall, roadD[k])), 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(trailHW, trailHW + fall, trailD[k])));
                if (pf > 0.5f) onPath++;
                depth = Mathf.Clamp(depth * slopeKeep, 0.03f, 1f);
                sumD += depth; if (depth > 0.75f) knee++; else if (depth > 0.25f) ankle++;
                px[k] = new Color32((byte)(depth * 255f), (byte)(exposure * 255f), (byte)(slopeKeep * 255f), (byte)(pf * 255f));
            }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true); tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(DriftPath, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(DriftPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(DriftPath);
            imp.sRGBTexture = false; imp.isReadable = true; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed; imp.filterMode = FilterMode.Bilinear; imp.SaveAndReimport();
            if (ctrl) { ctrl.driftMask = AssetDatabase.LoadAssetAtPath<Texture2D>(DriftPath); EditorUtility.SetDirty(ctrl); }
            Debug.Log($"[ASH] Drift mask {DriftPath} ({N}x{N}, wind toward {wind}) in {(DateTime.Now - t0).TotalSeconds:F1}s: avg depth {sumD / (N * N):F2}, knee-deep {100f * knee / (N * N):F1}%, ankle {100f * ankle / (N * N):F1}%, main-path cells {100f * onPath / (N * N):F2}% (road hw {roadHW}, trail hw {trailHW}, falloff {fall} m).");
        }

        static float[] BoxBlur(float[] src, int N, int r)
        {
            var tmp = new float[N * N]; var dst = new float[N * N];
            for (int z = 0; z < N; z++) { float acc = 0; int cnt = 0; for (int x = -r; x < N + r; x++) { if (x + r < N && x + r >= 0) { acc += src[z * N + x + r]; cnt++; } if (x - r - 1 >= 0) { acc -= src[z * N + x - r - 1]; cnt--; } if (x >= 0 && x < N) tmp[z * N + x] = acc / cnt; } }
            for (int x = 0; x < N; x++) { float acc = 0; int cnt = 0; for (int z = -r; z < N + r; z++) { if (z + r < N && z + r >= 0) { acc += tmp[(z + r) * N + x]; cnt++; } if (z - r - 1 >= 0) { acc -= tmp[(z - r - 1) * N + x]; cnt--; } if (z >= 0 && z < N) dst[z * N + x] = acc / cnt; } }
            return dst;
        }

        static float[] RouteDistance(Vector3 o, Vector3 size, int N, bool roads, bool trails)
        {
            var f = new float[N * N]; for (int i = 0; i < f.Length; i++) f[i] = 1e9f;
            if (!File.Exists(ASHRegionBuilder.LayoutPath)) return f;
            var L = ASHRegionBuilder.Load(); float cell = size.x / N; const float R = 50f;
            var lines = new List<List<Vector2>>();
            if (roads) foreach (var r in L.roads ?? new ASHRegionBuilder.Route[0]) lines.Add(ASHRegionBuilder.Smooth(r.pts, 8));
            if (trails) foreach (var r in L.trails ?? new ASHRegionBuilder.Route[0]) lines.Add(ASHRegionBuilder.Smooth(r.pts, 8));
            foreach (var line in lines)
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    Vector2 a = line[i], b = line[i + 1], ab = b - a; float l2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                    int x0 = Mathf.Max(0, (int)((Mathf.Min(a.x, b.x) - R - o.x) / cell)), x1 = Mathf.Min(N - 1, (int)((Mathf.Max(a.x, b.x) + R - o.x) / cell));
                    int z0 = Mathf.Max(0, (int)((Mathf.Min(a.y, b.y) - R - o.z) / cell)), z1 = Mathf.Min(N - 1, (int)((Mathf.Max(a.y, b.y) + R - o.z) / cell));
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(o.x + (x + 0.5f) * cell, o.z + (z + 0.5f) * cell);
                        float dd = Vector2.Distance(p, a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2)); int k = z * N + x; if (dd < f[k]) f[k] = dd;
                    }
                }
            return f;
        }

        // ------------------------------------------------------------------ trees (ASH-scattered prototypes only)
        [MenuItem("ASH/World/Accumulation/3. Apply To ASH Tree Prototypes", priority = 120)]
        public static void ApplyToTrees()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play Mode first.");
            var terrain = Terrain.activeTerrain; var data = terrain.terrainData;
            var rec = AssetDatabase.LoadAssetAtPath<ASHVegetationRecord>(ASHVegetationScatter.RecordPath);
            if (!rec) throw new Exception("No vegetation record; ASH trees not scattered.");
            ASHVegetationScatter.Backup(AssetDatabase.GetAssetPath(data));
            ASHBuildUtil.Folder(TreeDir);
            var protos = data.treePrototypes; var map = new Dictionary<GameObject, GameObject>(); var sb = new StringBuilder();
            int before = data.treeInstanceCount;
            for (int i = 0; i < rec.treeIndices.Count; i++)
            {
                int k = rec.treeIndices[i]; if (k >= protos.Length || protos[k].prefab != rec.treePrefabs[i]) continue;
                var src = protos[k].prefab; var v = TreeVariant(src); if (!v || v == src) continue;
                map[src] = v; protos[k].prefab = v; rec.treePrefabs[i] = v; sb.Append($"{src.name}->{v.name}; ");
            }
            if (map.Count > 0)
            {
                data.treePrototypes = protos; data.RefreshPrototypes();
                if (data.treeInstanceCount != before) Debug.LogError($"[ASH] Tree count changed {before} -> {data.treeInstanceCount}! Restore from backup.");
                var s = ASHVegetationScatter.Settings();
                foreach (var e in s.trees) { if (e.prefab && map.TryGetValue(e.prefab, out var a)) e.prefab = a; if (e.slopeVariant && map.TryGetValue(e.slopeVariant, out var b)) e.slopeVariant = b; }
                EditorUtility.SetDirty(s); EditorUtility.SetDirty(rec); EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(s); AssetDatabase.SaveAssetIfDirty(rec); AssetDatabase.SaveAssetIfDirty(data);
            }
            var others = Enumerable.Range(0, protos.Length).Where(i => !rec.treeIndices.Contains(i)).Select(i => protos[i].prefab ? protos[i].prefab.name : "null");
            Debug.Log($"[ASH] Accumulation trees: {map.Count} ASH prototypes swapped to variants ({sb}). Untouched (Anthony's): {string.Join(", ", others)}. Instances {data.treeInstanceCount}.");
        }

        static GameObject TreeVariant(GameObject src)
        {
            if (AssetDatabase.GetAssetPath(src).StartsWith(TreeDir)) return src;
            var path = $"{TreeDir}/{San(src.name)}_Accum.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing) return existing;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
                    r.sharedMaterials = r.sharedMaterials.Select(VariantAsset).ToArray();
                var v = PrefabUtility.SaveAsPrefabAsset(inst, path);
                if (!ASHVegetationScatter.ValidTree(v, out var why)) { Debug.LogWarning($"[ASH] Variant {path} not a valid terrain tree ({why}); keeping original."); return src; }
                return v;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // ------------------------------------------------------------------ structures (drop-on)
        [MenuItem("ASH/World/Apply Accumulation To Selected", priority = 60)]
        public static void ApplyToSelected() => ApplyToTargets(Selection.objects, true);

        [MenuItem("ASH/World/Remove Accumulation From Selected", priority = 61)]
        public static void RemoveFromSelected() => ApplyToTargets(Selection.objects, false);

        /// <summary>
        /// Selection -> targets: prefab assets (Project), folders (all prefabs inside), prefab instances (scene: edits the outermost
        /// instance's source prefab; a variant instance edits the variant), plain scene objects (edited in the scene with Undo).
        /// Model (FBX) instances can't be saved as prefabs, so they are done in the scene.
        /// </summary>
        public static string ApplyToTargets(IEnumerable<Object> objs, bool apply)
        {
            var prefabs = new List<string>(); var sceneObjs = new List<GameObject>(); var instances = new Dictionary<string, List<GameObject>>();
            foreach (var o in objs)
            {
                if (!o) continue;
                var path = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path)) { prefabs.AddRange(AssetDatabase.FindAssets("t:Prefab", new[] { path }).Select(AssetDatabase.GUIDToAssetPath)); continue; }
                var go = o as GameObject; if (!go) continue;
                if (!string.IsNullOrEmpty(path)) // asset in the Project window
                {
                    var root = go.transform.root.gameObject; var type = PrefabUtility.GetPrefabAssetType(root);
                    if (type == PrefabAssetType.Regular || type == PrefabAssetType.Variant) prefabs.Add(path);
                    else Debug.LogWarning($"[ASH] Accumulation: {path} is a {type} asset (not a saveable prefab); drop it in the scene or make a prefab first.");
                    continue;
                }
                if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                    var src = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outer);
                    var type = PrefabUtility.GetPrefabAssetType(outer);
                    if (!string.IsNullOrEmpty(src) && (type == PrefabAssetType.Regular || type == PrefabAssetType.Variant))
                    { prefabs.Add(src); if (!instances.TryGetValue(src, out var l)) instances[src] = l = new List<GameObject>(); l.Add(outer); continue; }
                }
                sceneObjs.Add(go);
            }
            var sb = new StringBuilder(); _created = 0; _reused = 0;
            foreach (var p in prefabs.Distinct())
            {
                if (instances.TryGetValue(p, out var insts)) foreach (var i in insts) CleanInstance(i, sb);
                sb.Append(apply ? ApplyPrefab(p) : RemovePrefab(p));
            }
            if (sceneObjs.Count > 0) sb.Append(apply ? ApplyTo(sceneObjs) : RemoveScene(sceneObjs));
            AssetDatabase.SaveAssets();
            var msg = $"[ASH] Accumulation {(apply ? "apply" : "remove")}: {prefabs.Distinct().Count()} prefab(s), {sceneObjs.Count} scene object(s); material copies created {_created}, reused {_reused}. {sb}";
            Debug.Log(msg); return msg;
        }

        /// <summary>Instance-level leftovers (an ASHAccumulation added on the scene instance and its material overrides) would mask
        /// the prefab: restore and remove them so the instance shows the prefab's setup.</summary>
        static void CleanInstance(GameObject inst, StringBuilder sb)
        {
            foreach (var a in inst.GetComponentsInChildren<ASHAccumulation>(true))
            {
                if (!PrefabUtility.IsAddedComponentOverride(a)) continue;
                var rs = a.slots.Select(s => s.renderer).Where(r => r).ToList();
                Undo.RecordObject(a, "Clean accumulation"); foreach (var r in rs) Undo.RecordObject(r, "Clean accumulation");
                a.Revert(); Undo.DestroyObjectImmediate(a);
                foreach (var r in rs) { var so = new SerializedObject(r); var prop = so.FindProperty("m_Materials"); if (prop != null && prop.prefabOverride) PrefabUtility.RevertPropertyOverride(prop, InteractionMode.UserAction); }
                sb.Append($"(instance '{inst.name}': removed scene-only accumulation, reverted {rs.Count} material overrides) ");
            }
        }

        static string ApplyPrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (IsPickupLike(root) || path.Contains("/Pickups/") || path.Contains("/Items/") || path.Contains("Pickup")) return $"REFUSED {path} (pickup/interactable); ";
                var a = root.GetComponent<ASHAccumulation>(); bool added = !a; if (!a) a = root.AddComponent<ASHAccumulation>();
                int n = a.Apply(out int skipped, out int sc, out int sk);
                var kept = new List<string>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) { var why = ASHAccumulation.UnsupportedReason(m); if (why != null && why != "already ASH" && m) kept.Add($"{m.name} ({why})"); }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                var type = PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                return $"{path} [{type}]: component {(added ? "added" : "kept")}, {n} renderers / {sc} slots -> ASH ({skipped} renderers skipped{(kept.Count > 0 ? ", kept: " + string.Join(", ", kept.Distinct()) : "")}); ";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static string RemovePrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var list = root.GetComponentsInChildren<ASHAccumulation>(true); if (list.Length == 0) return $"{path}: no accumulation; ";
                int restored = 0; foreach (var a in list) { restored += a.slots.Count; a.Revert(); Object.DestroyImmediate(a); }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"{path}: removed, {restored} renderers restored to original materials; ";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static string RemoveScene(IEnumerable<GameObject> gos)
        {
            var sb = new StringBuilder();
            foreach (var go in gos)
            {
                var a = go.GetComponent<ASHAccumulation>(); if (!a) continue;
                Undo.RecordObject(a, "Remove accumulation"); foreach (var s in a.slots) if (s.renderer) Undo.RecordObject(s.renderer, "Remove accumulation");
                int n = a.slots.Count; a.Revert(); Undo.DestroyObjectImmediate(a);
                if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
                sb.Append($"{go.name}: removed, {n} renderers restored; ");
            }
            return sb.ToString();
        }

        /// <summary>Scene objects (non-prefab): component + material copies in the scene, undoable.</summary>
        public static string ApplyTo(IEnumerable<GameObject> gos)
        {
            var sb = new StringBuilder();
            foreach (var go in gos)
            {
                if (!go) continue;
                if (IsPickupLike(go)) { sb.Append($"REFUSED {go.name} (pickup/interactable); "); continue; }
                var a = go.GetComponent<ASHAccumulation>(); if (!a) a = Undo.AddComponent<ASHAccumulation>(go);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Undo.RecordObject(r, "Apply accumulation");
                Undo.RecordObject(a, "Apply accumulation");
                int n = a.Apply(out int skipped, out int sc, out _); EditorUtility.SetDirty(a);
                if (go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
                sb.Append($"{go.name} (scene): {n} renderers / {sc} slots ({skipped} skipped); ");
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        /// <summary>exec test: copy a vendor prefab to Assets/_ASH/Tests, make a variant, apply/remove via asset, variant and scene instance; logs results and cleans up.</summary>
        public static void SelfTest()
        {
            const string src = "Assets/HQ Survival Pack/Prefabs/Building/BuildingParts/Wood/PlankParts/Doorway_Planks.prefab";
            const string dir = ASHContentBuilder.Root + "/Tests"; ASHBuildUtil.Folder(dir);
            string copy = dir + "/ASH_TestDoorwayPlanks.prefab", variant = dir + "/ASH_TestDoorwayPlanks_Variant.prefab";
            AssetDatabase.DeleteAsset(copy); AssetDatabase.DeleteAsset(variant); CreatedPaths.Clear();
            if (!AssetDatabase.CopyAsset(src, copy)) { Debug.LogError("[ASH] ACCUMTEST copy failed"); return; }
            var baseGo = AssetDatabase.LoadAssetAtPath<GameObject>(copy);
            var vi = (GameObject)PrefabUtility.InstantiatePrefab(baseGo); PrefabUtility.SaveAsPrefabAsset(vi, variant); Object.DestroyImmediate(vi);
            string Check(string p)
            {
                var g = AssetDatabase.LoadAssetAtPath<GameObject>(p); var a = g.GetComponent<ASHAccumulation>();
                var mats = g.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).ToList();
                return $"{Path.GetFileName(p)}: comp {(bool)a} slots {(a ? a.slots.Count : 0)}, materials {mats.Count(m => m.shader.name == ASHAccumulation.ShaderName)}/{mats.Count} ASH ({string.Join(", ", mats.Select(m => AssetDatabase.GetAssetPath(m).Replace(VariantRoot, "~")).Distinct().Take(4))})";
            }
            Debug.Log("[ASH] ACCUMTEST before: " + Check(copy));
            ApplyToTargets(new Object[] { baseGo }, true); Debug.Log("[ASH] ACCUMTEST asset apply: " + Check(copy) + " | variant inherits: " + Check(variant));
            ApplyToTargets(new Object[] { baseGo }, true); Debug.Log("[ASH] ACCUMTEST re-apply (idempotent): " + Check(copy));
            ApplyToTargets(new Object[] { baseGo }, false); Debug.Log("[ASH] ACCUMTEST asset remove: " + Check(copy) + " | variant: " + Check(variant));
            ApplyToTargets(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(variant) }, true); Debug.Log("[ASH] ACCUMTEST variant apply: " + Check(variant) + " | base untouched: " + Check(copy));
            ApplyToTargets(new Object[] { AssetDatabase.LoadAssetAtPath<GameObject>(variant) }, false); Debug.Log("[ASH] ACCUMTEST variant remove: " + Check(variant));
            // scene instance (child selected) -> edits the source prefab; then a plain scene object
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(baseGo, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            var child = inst.GetComponentInChildren<Renderer>(true).gameObject;
            ApplyToTargets(new Object[] { child }, true);
            var ir = inst.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).ToList();
            Debug.Log("[ASH] ACCUMTEST instance apply -> prefab: " + Check(copy) + $" | instance shows {ir.Count(m => m.shader.name == ASHAccumulation.ShaderName)}/{ir.Count} ASH, instance added comp override {inst.GetComponent<ASHAccumulation>() && PrefabUtility.IsAddedComponentOverride(inst.GetComponent<ASHAccumulation>())}");
            ApplyToTargets(new Object[] { inst }, false); Debug.Log("[ASH] ACCUMTEST instance remove -> prefab: " + Check(copy));
            Object.DestroyImmediate(inst);
            var plain = new GameObject("ASH_TestPlain"); var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(plain.transform);
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "ASH_TestLit" }; cube.GetComponent<Renderer>().sharedMaterial = lit;
            ApplyToTargets(new Object[] { plain }, true); var sm = cube.GetComponent<Renderer>().sharedMaterial;
            Debug.Log($"[ASH] ACCUMTEST plain scene apply: comp {(bool)plain.GetComponent<ASHAccumulation>()}, material {sm.shader.name}");
            ApplyToTargets(new Object[] { plain }, false); Debug.Log($"[ASH] ACCUMTEST plain scene remove: comp {(bool)plain.GetComponent<ASHAccumulation>()}, material {cube.GetComponent<Renderer>().sharedMaterial.name}");
            Object.DestroyImmediate(plain);
            AssetDatabase.DeleteAsset(variant); AssetDatabase.DeleteAsset(copy); AssetDatabase.DeleteAsset(dir);
            foreach (var cp in CreatedPaths.Distinct()) AssetDatabase.DeleteAsset(cp);
            Debug.Log($"[ASH] ACCUMTEST cleanup: deleted test prefabs + {CreatedPaths.Distinct().Count()} generated copies");
            Debug.Log("[ASH] ACCUMTEST DONE");
        }

        /// <summary>Guard: never pickups. Component checks (ASHItemPickup / IASHUsable in parents) + pickup folders for prefab assets.</summary>
        public static bool IsPickupLike(GameObject go)
        {
            if (ASHAccumulation.IsExcluded(go.transform)) return true;
            // child pickups/interactables inside a structure are skipped per renderer by ASHAccumulation.IsExcluded
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go); var p = src ? AssetDatabase.GetAssetPath(src) : AssetDatabase.GetAssetPath(go);
            return !string.IsNullOrEmpty(p) && (p.Contains("/Pickups/") || p.Contains("/Items/") || p.Contains("Pickup"));
        }

        /// <summary>Applies to Grandpa Cabin and ASH_Region POI structures (exec). Pickups/interactables skipped.</summary>
        [MenuItem("ASH/World/Accumulation/4. Apply To Structures (Cabin + POIs)", priority = 130)]
        public static void ApplyToStructures()
        {
            var list = new List<GameObject>();
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name.StartsWith("Grandpa Cabin")) list.Add(root);
            var region = GameObject.Find("ASH_Region"); var pois = region ? region.transform.Find("POIs") : null;
            if (pois) foreach (Transform c in pois) list.Add(c.gameObject);
            ApplyTo(list);
        }
    }
}
