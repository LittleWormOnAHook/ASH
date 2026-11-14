using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.EditorTools.Build;
using ASH.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// ASH/World/Scatter Vegetation: mass terrain trees + grass detail by biome (ASH_RegionLayout.json bands),
    /// slope and creek proximity, with clear radii around roads, trails, POIs, survey points, Grandpa Cabin,
    /// spawn and pickups. ADDITIVE: Anthony's tree prototypes/instances and existing detail layers are kept;
    /// ASH's own prototypes are listed in ASH_VegetationRecord so a re-run or "Clear ASH-scattered" replaces only
    /// those. Backs up the TerrainData first (Art/Terrain/Backups, gitignored). One batched write per array.
    /// Prototypes, densities and draw distances live in ASH_VegetationSettings (presets Low/Default/Extreme).
    /// </summary>
    public static class ASHVegetationScatter
    {
        public const string SettingsPath = ASHContentBuilder.Root + "/Data/World/ASH_VegetationSettings.asset";
        public const string RecordPath = ASHContentBuilder.Root + "/Data/World/ASH_VegetationRecord.asset";
        public const string BackupDir = ASHContentBuilder.Root + "/Art/Terrain/Backups";
        const float G = 4f; // mask grid cell (m)

        // ------------------------------------------------------------------ menus
        [MenuItem("ASH/World/Scatter Vegetation", priority = 10)]
        public static void Scatter() => Run(true);

        [MenuItem("ASH/World/Clear ASH-scattered Vegetation", priority = 11)]
        public static void Clear() => Run(false);

        [MenuItem("ASH/World/Vegetation Settings", priority = 30)]
        public static void SelectSettings() { Selection.activeObject = Settings(); EditorGUIUtility.PingObject(Selection.activeObject); }

        [MenuItem("ASH/World/Vegetation Preset/Low", priority = 40)] public static void PresetLow() => SetPreset(ASHVegetationSettings.Preset.Low);
        [MenuItem("ASH/World/Vegetation Preset/Default", priority = 41)] public static void PresetDefault() => SetPreset(ASHVegetationSettings.Preset.Default);
        [MenuItem("ASH/World/Vegetation Preset/Extreme", priority = 42)] public static void PresetExtreme() => SetPreset(ASHVegetationSettings.Preset.Extreme);

        [MenuItem("ASH/World/Reset Vegetation Settings to Defaults", priority = 32)]
        public static void ResetSettings() { var s = Settings(); FillDefaults(s); EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s); Debug.Log($"[ASH] Vegetation settings reset: {s.trees.Count} trees, {s.grass.Count} grasses."); }

        [MenuItem("ASH/World/Vegetation Report", priority = 31)]
        public static void Report()
        {
            var t = Terrain.activeTerrain; var d = t.terrainData; int res = d.detailResolution; var dp = d.detailPrototypes;
            var sb = new System.Text.StringBuilder($"[ASH] VEG report: trees {d.treeInstanceCount}/{d.treePrototypes.Length} protos; detail res {res} perPatch {d.detailResolutionPerPatch} mode {d.detailScatterMode}; "
                + $"terrain draw {t.drawTreesAndFoliage} detailDist {t.detailObjectDistance} density {t.detailObjectDensity} instanced {t.drawInstanced}; mat {(t.materialTemplate ? t.materialTemplate.shader.name : "none")}");
            for (int i = 0; i < dp.Length; i++)
            {
                var l = d.GetDetailLayer(0, 0, res, res, i); long sum = 0; int nz = 0; foreach (var v in l) { sum += v; if (v > 0) nz++; }
                var p = dp[i]; sb.Append($"\n  layer {i}: {(p.usePrototypeMesh ? (p.prototype ? p.prototype.name : "null mesh") : (p.prototypeTexture ? p.prototypeTexture.name + (p.prototypeTexture.isReadable ? " R" : " nonR") : "null tex"))} mode {p.renderMode} inst {p.useInstancing} density {p.density} w {p.minWidth}-{p.maxWidth} h {p.minHeight}-{p.maxHeight} sum {sum} cells {nz} valid {p.Validate(out var err)} {err}");
            }
            string Counts(TerrainData td) { var c = new int[td.treePrototypes.Length]; foreach (var ti in td.treeInstances) if (ti.prototypeIndex < c.Length) c[ti.prototypeIndex]++; return string.Join(", ", td.treePrototypes.Select((tp, i) => $"{(tp.prefab ? tp.prefab.name : "null")}={c[i]}")); }
            sb.Append("\n  live: " + Counts(d));
            foreach (var g in AssetDatabase.FindAssets("_backup_", new[] { BackupDir }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x))
            { var bd = AssetDatabase.LoadAssetAtPath<TerrainData>(g); if (bd) sb.Append($"\n  {Path.GetFileNameWithoutExtension(g)}: " + Counts(bd)); }
            Debug.Log(sb.ToString());
        }

        static void SetPreset(ASHVegetationSettings.Preset p)
        {
            var s = Settings(); s.Apply(p); EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s);
            Debug.Log($"[ASH] Vegetation preset {p} set (density x{s.treeDensityScale}, grass {s.grassDensity}). Run ASH/World/Scatter Vegetation to apply.");
        }

        // ------------------------------------------------------------------ settings
        public static ASHVegetationSettings Settings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHVegetationSettings>(SettingsPath);
            if (s) return s;
            ASHBuildUtil.Folder(Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
            s = ScriptableObject.CreateInstance<ASHVegetationSettings>();
            FillDefaults(s);
            AssetDatabase.CreateAsset(s, SettingsPath); AssetDatabase.SaveAssetIfDirty(s);
            return s;
        }

        /// <summary>Placeholder picks from installed vendor packs (swap freely in the settings asset).</summary>
        public static void FillDefaults(ASHVegetationSettings s)
        {
            s.trees.Clear(); s.grass.Clear();
            void T(string name, float ridge, float gully, float prairie, float creek, float min = 0.8f, float max = 1.25f)
            {
                var p = FindAsset<GameObject>(name, "t:Prefab", "/Prefabs/Standard/");
                if (p) s.trees.Add(new ASHVegetationSettings.TreeEntry { prefab = p, ridge = ridge, gully = gully, prairie = prairie, creek = creek, minScale = min, maxScale = max });
                else Debug.LogWarning("[ASH] Vegetation default tree not found: " + name);
            }
            T("Fir_06_Forest_cheap", 1f, 0.3f, 0f, 0.3f);
            T("Fir_07_Forest_cheap", 1f, 0.3f, 0f, 0.3f);
            T("Fir_03_Medium", 0.5f, 0.8f, 0.3f, 1f, 0.7f, 1.2f);
            T("Fir_02_Small", 0.35f, 0.6f, 0.4f, 0.7f, 0.7f, 1.3f);
            T("Fir_05_Standalone_cheap", 0.1f, 0.4f, 1f, 0.3f, 0.8f, 1.2f);
            T("Fir_08_Dead_Forest_cheap", 0.3f, 0.25f, 0.3f, 0.1f, 0.8f, 1.2f); // ash-killed snags

            void Gr(string name, float wMin, float wMax, float hMin, float hMax, Color healthy, Color dry, float ridge, float gully, float prairie, float creek)
            {
                var t = FindAsset<Texture2D>(name, "t:Texture2D", "RUSTIC Grass/");
                if (t) s.grass.Add(new ASHVegetationSettings.GrassEntry { texture = t, minWidth = wMin, maxWidth = wMax, minHeight = hMin, maxHeight = hMax, healthy = healthy, dry = dry, ridge = ridge, gully = gully, prairie = prairie, creek = creek });
                else Debug.LogWarning("[ASH] Vegetation default grass not found: " + name);
            }
            Color ashTan = new Color(0.74f, 0.69f, 0.55f), ashGrey = new Color(0.6f, 0.58f, 0.52f), straw = new Color(0.8f, 0.71f, 0.5f), dust = new Color(0.62f, 0.55f, 0.42f);
            Gr("Dry Grass_03", 0.9f, 1.6f, 0.5f, 0.9f, ashTan, ashGrey, 0.3f, 0.6f, 1f, 0.2f);    // prairie shortgrass
            Gr("Dune Grass_02", 0.9f, 1.5f, 0.55f, 1f, straw, dust, 0.1f, 0.4f, 0.7f, 0.1f);    // bunchgrass
            Gr("Dry Grass_07", 1f, 1.8f, 0.8f, 1.4f, straw, ashGrey, 0.4f, 0.8f, 0.2f, 0.8f);    // taller gully grass
            Gr("Rustic Grass_05", 0.9f, 1.5f, 0.5f, 0.9f, ashGrey, dust, 0.6f, 0.3f, 0.1f, 0.2f); // grey understory
            Gr("Dry Cattails_01", 0.8f, 1.3f, 1.1f, 1.8f, straw, dust, 0f, 0.05f, 0f, 1.2f);     // creek banks
            s.Apply(ASHVegetationSettings.Preset.Default);
        }

        static T FindAsset<T>(string name, string filter, string pathHint) where T : Object
        {
            var paths = AssetDatabase.FindAssets(name + " " + filter).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p) == name).OrderByDescending(p => p.Contains(pathHint)).ToList();
            return paths.Count > 0 ? AssetDatabase.LoadAssetAtPath<T>(paths[0]) : null;
        }

        /// <summary>Terrain trees need MeshRenderer/BillboardRenderer only (root or LODGroup); others are skipped.</summary>
        public static bool ValidTree(GameObject p, out string why)
        {
            why = null; if (!p) { why = "missing"; return false; }
            var lod = p.GetComponent<LODGroup>();
            if (lod)
            {
                foreach (var l in lod.GetLODs())
                    foreach (var r in l.renderers)
                    {
                        if (!r) { why = "null LOD renderer"; return false; }
                        if (!(r is MeshRenderer) && !(r is BillboardRenderer)) { why = r.GetType().Name + " in LOD"; return false; }
                        if (r is MeshRenderer && (!r.GetComponent<MeshFilter>() || !r.GetComponent<MeshFilter>().sharedMesh)) { why = "LOD without mesh"; return false; }
                    }
                return true;
            }
            var mf = p.GetComponent<MeshFilter>();
            if (p.GetComponent<MeshRenderer>() && mf && mf.sharedMesh) return true;
            why = "no LODGroup and no root MeshRenderer"; return false;
        }

        // ------------------------------------------------------------------ run
        static void Run(bool scatter)
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play Mode first.");
            var terrain = Terrain.activeTerrain; if (!terrain) throw new Exception("No active terrain (open the prototype scene).");
            var data = terrain.terrainData; var dataPath = AssetDatabase.GetAssetPath(data);
            var s = Settings(); var rec = Record(); var t0 = DateTime.Now;

            Backup(dataPath);

            // ---- current state, minus ASH's previous prototypes (validated against the record)
            var protos = data.treePrototypes.ToList(); var inst = data.treeInstances;
            var ashTree = new HashSet<int>();
            if (rec.terrainDataGuid == AssetDatabase.AssetPathToGUID(dataPath))
                for (int i = 0; i < rec.treeIndices.Count; i++)
                {
                    int k = rec.treeIndices[i];
                    if (k < protos.Count && protos[k].prefab == rec.treePrefabs[i]) ashTree.Add(k);
                    else Debug.LogWarning($"[ASH] Vegetation record tree #{k} no longer matches; left alone.");
                }
            var remap = new int[protos.Count]; var keptProtos = new List<TreePrototype>();
            for (int i = 0; i < protos.Count; i++) { if (ashTree.Contains(i)) remap[i] = -1; else { remap[i] = keptProtos.Count; keptProtos.Add(protos[i]); } }
            var kept = new List<TreeInstance>(inst.Length);
            foreach (var ti in inst) { if (ti.prototypeIndex >= remap.Length || remap[ti.prototypeIndex] < 0) continue; var c = ti; c.prototypeIndex = remap[ti.prototypeIndex]; kept.Add(c); }
            int anthonyTrees = kept.Count, removedTrees = inst.Length - kept.Count;

            var dprotos = data.detailPrototypes.ToList(); var ashDetail = new HashSet<int>();
            if (rec.terrainDataGuid == AssetDatabase.AssetPathToGUID(dataPath))
                for (int i = 0; i < rec.detailIndices.Count; i++)
                {
                    int k = rec.detailIndices[i];
                    if (k < dprotos.Count && (dprotos[k].prototypeTexture == rec.detailSources[i] || dprotos[k].prototype == rec.detailSources[i])) ashDetail.Add(k);
                }
            int dres = data.detailResolution;
            var keptLayers = new List<int[,]>(); var keptD = new List<DetailPrototype>();
            for (int i = 0; i < dprotos.Count; i++) if (!ashDetail.Contains(i)) { keptD.Add(dprotos[i]); keptLayers.Add(data.GetDetailLayer(0, 0, dres, dres, i)); }

            var newTrees = new List<TreeInstance>(); var newProtos = new List<TreePrototype>(); var newProtoPrefabs = new List<GameObject>();
            var newD = new List<DetailPrototype>(); var newLayers = new List<int[,]>(); var newDSrc = new List<Object>();
            var skipped = new List<string>(); long grass = 0; string routeReport = "";

            if (scatter)
            {
                var ctx = new Ctx(terrain, s);
                // tree prototypes (ASH-owned entries, appended after Anthony's)
                var entries = new List<ASHVegetationSettings.TreeEntry>();
                foreach (var e in s.trees)
                {
                    if (!ValidTree(e.prefab, out var why)) { skipped.Add($"{(e.prefab ? e.prefab.name : "null")} ({why})"); continue; }
                    entries.Add(e); newProtos.Add(new TreePrototype { prefab = e.prefab, bendFactor = 0.2f }); newProtoPrefabs.Add(e.prefab);
                }
                if (entries.Count > 0) ScatterTrees(ctx, entries, keptProtos.Count, newTrees);
                float minRoadEdge = 1e9f, minTrail = 1e9f; int bad = 0;
                foreach (var ti in newTrees)
                {
                    var wp = new Vector2(ctx.origin.x + ti.position.x * ctx.size.x, ctx.origin.z + ti.position.z * ctx.size.z);
                    ctx.RouteDist(wp, out var dr, out var dt); minRoadEdge = Mathf.Min(minRoadEdge, dr - s.roadHalfWidth); minTrail = Mathf.Min(minTrail, dt);
                    if (dr < s.roadHalfWidth + s.roadEdgeClearTrees || dt < s.trailClearTrees) bad++;
                }
                routeReport = $"route check: nearest ASH tree {minRoadEdge:F1} m from a road edge, {minTrail:F1} m from a trail centerline, violations {bad}";
                if (bad > 0) Debug.LogError("[ASH] Vegetation " + routeReport);

                var gEntries = new List<ASHVegetationSettings.GrassEntry>();
                foreach (var e in s.grass)
                {
                    var dp = new DetailPrototype
                    {
                        prototypeTexture = e.mesh ? null : e.texture, prototype = e.mesh, usePrototypeMesh = e.mesh,
                        renderMode = e.mesh ? DetailRenderMode.VertexLit : DetailRenderMode.GrassBillboard, useInstancing = e.mesh,
                        minWidth = e.minWidth, maxWidth = e.maxWidth, minHeight = e.minHeight, maxHeight = e.maxHeight,
                        healthyColor = e.healthy, dryColor = e.dry, noiseSpread = 0.35f,
                    };
                    if ((!e.mesh && !e.texture) || !dp.Validate(out var err)) { skipped.Add($"grass {(e.texture ? e.texture.name : e.mesh ? e.mesh.name : "null")} ({(e.mesh || e.texture ? "invalid" : "missing")})"); continue; }
                    gEntries.Add(e); newD.Add(dp); newDSrc.Add(e.mesh ? (Object)e.mesh : e.texture);
                }
                if (gEntries.Count > 0) grass = ScatterGrass(ctx, gEntries, dres, newLayers);
            }

            // ---- batched writes
            if (removedTrees > 0 || ashTree.Count > 0) data.SetTreeInstances(kept.ToArray(), false); // valid for old and new prototype arrays
            data.treePrototypes = keptProtos.Concat(newProtos).ToArray();
            data.SetTreeInstances(kept.Concat(newTrees).ToArray(), true);

            data.detailPrototypes = keptD.Concat(newD).ToArray();
            for (int i = 0; i < keptLayers.Count; i++) data.SetDetailLayer(0, 0, i, keptLayers[i]);
            for (int i = 0; i < newLayers.Count; i++) data.SetDetailLayer(0, 0, keptD.Count + i, newLayers[i]);

            // ---- guard: Anthony's trees intact
            int check = data.treeInstances.Count(ti => ti.prototypeIndex < keptProtos.Count);
            if (check != anthonyTrees) Debug.LogError($"[ASH] Vegetation guard: non-ASH tree count changed {anthonyTrees} -> {check}. Restore from {BackupDir}.");

            // ---- record
            rec.terrainDataGuid = AssetDatabase.AssetPathToGUID(dataPath);
            rec.treeIndices = Enumerable.Range(keptProtos.Count, newProtos.Count).ToList(); rec.treePrefabs = newProtoPrefabs;
            rec.detailIndices = Enumerable.Range(keptD.Count, newD.Count).ToList(); rec.detailSources = newDSrc;
            rec.treesPlaced = newTrees.Count; rec.grassPlaced = grass; rec.lastRun = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            EditorUtility.SetDirty(rec); AssetDatabase.SaveAssetIfDirty(rec);

            // ---- terrain render settings (scene)
            if (scatter)
            {
                terrain.treeDistance = s.treeDistance; terrain.treeBillboardDistance = s.billboardStart; terrain.treeCrossFadeLength = s.treeCrossFade;
                terrain.treeMaximumFullLODCount = s.maxFullLodTrees; terrain.detailObjectDistance = s.detailDistance; terrain.detailObjectDensity = s.detailObjectDensity;
                terrain.drawInstanced = true; terrain.drawTreesAndFoliage = true;
                EditorUtility.SetDirty(terrain); EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene); // scene not auto-saved (may hold Anthony's unsaved edits)
            }
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data);

            Debug.Log($"[ASH] Vegetation {(scatter ? "scattered" : "cleared")} in {(DateTime.Now - t0).TotalSeconds:F1}s: removed {removedTrees} old ASH trees; "
                + $"Anthony trees kept {anthonyTrees} ({keptProtos.Count} prototypes); ASH trees {newTrees.Count} ({newProtos.Count} prototypes); "
                + $"grass instances {grass} ({newD.Count} ASH layers, {keptD.Count} kept, detail res {dres}); total trees {data.treeInstanceCount}; {routeReport}."
                + (skipped.Count > 0 ? " Skipped: " + string.Join(", ", skipped) : ""));
        }

        static ASHVegetationRecord Record()
        {
            var r = AssetDatabase.LoadAssetAtPath<ASHVegetationRecord>(RecordPath); if (r) return r;
            ASHBuildUtil.Folder(Path.GetDirectoryName(RecordPath).Replace('\\', '/'));
            r = ScriptableObject.CreateInstance<ASHVegetationRecord>(); AssetDatabase.CreateAsset(r, RecordPath); return r;
        }

        static void Backup(string dataPath)
        {
            ASHBuildUtil.Folder(BackupDir);
            var dst = $"{BackupDir}/{Path.GetFileNameWithoutExtension(dataPath)}_backup_{DateTime.Now:yyyyMMdd_HHmmss}.asset";
            if (!AssetDatabase.CopyAsset(dataPath, dst)) throw new Exception("TerrainData backup failed; nothing changed.");
            var old = AssetDatabase.FindAssets("_backup_", new[] { BackupDir }).Select(AssetDatabase.GUIDToAssetPath).OrderByDescending(p => p).Skip(3).ToList();
            foreach (var p in old) AssetDatabase.DeleteAsset(p);
            Debug.Log("[ASH] TerrainData backed up to " + dst);
        }

        // ------------------------------------------------------------------ context: biome, creeks, clearances
        sealed class Ctx
        {
            public Terrain t; public TerrainData d; public ASHVegetationSettings s; public Vector3 origin, size;
            public int N; public float[] road, trail, creek, soil; public bool[] blockT, blockG; public Random rnd;
            readonly Dictionary<long, List<(Vector2 a, Vector2 b, bool road)>> _seg = new Dictionary<long, List<(Vector2, Vector2, bool)>>();
            const float B = 32f; // segment hash bucket (m)

            public Ctx(Terrain terrain, ASHVegetationSettings settings)
            {
                t = terrain; d = terrain.terrainData; s = settings; origin = terrain.GetPosition(); size = d.size; rnd = new Random(s.seed);
                N = Mathf.CeilToInt(Mathf.Max(size.x, size.z) / G);
                road = Field(); trail = Field(); creek = Field(); blockT = new bool[N * N]; blockG = new bool[N * N];
                var L = ASHRegionBuilder.Load();
                float band = Mathf.Max(s.routeSplatBand, s.roadHalfWidth + s.roadEdgeClearTrees, s.trailClearTrees, s.roadHalfWidth + s.routeGrassFade) + 8f;
                foreach (var r in L.roads ?? new ASHRegionBuilder.Route[0]) { var l = ASHRegionBuilder.Smooth(r.pts, 8); Rasterize(l, road, band); Hash(l, true); }
                foreach (var r in L.trails ?? new ASHRegionBuilder.Route[0]) { var l = ASHRegionBuilder.Smooth(r.pts, 8); Rasterize(l, trail, band); Hash(l, false); }
                soil = RouteSoil(band);
                var creeks = JsonUtility.FromJson<CreekJson>(File.ReadAllText(ASHRegionBuilder.LayoutPath)).creeks;
                if (creeks != null) foreach (var c in creeks) Rasterize(ASHRegionBuilder.Smooth(c.pts, 4), creek, s.creekBand * 2.5f);
                else Debug.LogWarning("[ASH] No creeks in layout JSON; creek-lining trees skipped.");

                foreach (var p in L.pois ?? new ASHRegionBuilder.Poi[0]) { Disc(p.V, s.poiClearTrees, blockT); Disc(p.V, s.poiClearGrass, blockG); }
                foreach (var sv in L.surveys ?? new ASHRegionBuilder.Surv[0]) { var p = L.P(sv.poi); if (p != null) { Disc(p.V, s.poiClearTrees, blockT); Disc(p.V, s.poiClearGrass, blockG); } }
                var region = GameObject.Find("ASH_Region");
                if (region)
                {
                    var pois = region.transform.Find("POIs");
                    if (pois) foreach (Transform c in pois) { BoundsBlock(c.gameObject, 6f, blockT); BoundsBlock(c.gameObject, 1f, blockG); }
                    var picks = region.transform.Find("Pickups");
                    if (picks) foreach (Transform c in picks) { Disc(XZ(c.position), s.pickupClearTrees, blockT); Disc(XZ(c.position), s.pickupClearGrass, blockG); }
                }
                var world = GameObject.Find("ASH_World");
                if (world) foreach (Transform c in world.transform) { BoundsBlock(c.gameObject, 3f, blockT); }
                foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                    if (go.name.StartsWith("Grandpa Cabin")) { BoundsBlock(go, s.cabinClearTrees, blockT); BoundsBlock(go, s.cabinClearGrass, blockG); }
                var player = GameObject.FindWithTag("Player");
                if (player) { Disc(XZ(player.transform.position), s.spawnClearTrees, blockT); Disc(XZ(player.transform.position), 2f, blockG); }
                else Debug.LogWarning("[ASH] No Player tag found; spawn clearance skipped.");
            }


            // ---- exact route distances (same Catmull-Rom splines PaintRoutes uses)
            void Hash(List<Vector2> line, bool isRoad)
            {
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    Vector2 a = line[i], b = line[i + 1];
                    int bx0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / B), bx1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / B);
                    int bz0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / B), bz1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / B);
                    for (int z = bz0; z <= bz1; z++) for (int x = bx0; x <= bx1; x++)
                    {
                        long key = ((long)z << 32) ^ (uint)x;
                        if (!_seg.TryGetValue(key, out var lst)) _seg[key] = lst = new List<(Vector2, Vector2, bool)>();
                        lst.Add((a, b, isRoad));
                    }
                }
            }

            /// <summary>Exact distance to the nearest road and trail centerline (searches 3x3 buckets: valid to 32 m).</summary>
            public void RouteDist(Vector2 p, out float dRoad, out float dTrail)
            {
                dRoad = dTrail = 1e9f; int bx = Mathf.FloorToInt(p.x / B), bz = Mathf.FloorToInt(p.y / B);
                for (int z = bz - 1; z <= bz + 1; z++) for (int x = bx - 1; x <= bx + 1; x++)
                {
                    if (!_seg.TryGetValue(((long)z << 32) ^ (uint)x, out var lst)) continue;
                    foreach (var (a, b, isRoad) in lst)
                    {
                        Vector2 ab = b - a; float tt = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                        float dd = Vector2.Distance(p, a + ab * tt);
                        if (isRoad) { if (dd < dRoad) dRoad = dd; } else if (dd < dTrail) dTrail = dd;
                    }
                }
            }

            /// <summary>Max route-soil splat weight per mask cell, only inside the route band (soil is also a biome layer).</summary>
            float[] RouteSoil(float band)
            {
                var o = new float[N * N]; int soilIdx = -1; var tl = d.terrainLayers;
                for (int i = 0; i < tl.Length; i++) if (tl[i] && tl[i].name == "ASH_TL_Soil") soilIdx = i;
                if (soilIdx < 0) { Debug.LogWarning("[ASH] ASH_TL_Soil not on terrain; route splat check skipped (splines still enforced)."); return o; }
                int res = d.alphamapResolution; float ac = size.x / res; const int T = 256;
                for (int tz = 0; tz < res; tz += T) for (int tx = 0; tx < res; tx += T)
                {
                    int w = Mathf.Min(T, res - tx), h = Mathf.Min(T, res - tz); bool near = false;
                    for (int z = 0; z < h && !near; z += 4) for (int x = 0; x < w; x += 4)
                    { int k = Cell(new Vector2(origin.x + (tx + x + 0.5f) * ac, origin.z + (tz + z + 0.5f) * ac)); if (Mathf.Min(road[k], trail[k]) < band) { near = true; break; } }
                    if (!near) continue;
                    var al = d.GetAlphamaps(tx, tz, w, h);
                    for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
                    {
                        int k = Cell(new Vector2(origin.x + (tx + x + 0.5f) * ac, origin.z + (tz + z + 0.5f) * ac));
                        if (Mathf.Min(road[k], trail[k]) >= s.routeSplatBand) continue;
                        float v = al[z, x, soilIdx]; if (v > o[k]) o[k] = v;
                    }
                }
                return o;
            }

            /// <summary>Hard rule: false if a tree here would be on/near a road or trail.</summary>
            public bool TreeClearOfRoutes(Vector2 p, int k)
            {
                if (soil[k] >= s.routeSplatThreshold) return false;
                if (Mathf.Min(road[k], trail[k]) > Mathf.Max(s.roadHalfWidth + s.roadEdgeClearTrees, s.trailClearTrees) + G) return true;
                RouteDist(p, out var dr, out var dt);
                return dr >= s.roadHalfWidth + s.roadEdgeClearTrees && dt >= s.trailClearTrees;
            }

            /// <summary>Grass multiplier: ~0 on the route surface, ramps to 1 past the edge; thinned on painted soil.</summary>
            public float GrassRouteFactor(Vector2 p, int k)
            {
                float f = 1f;
                if (Mathf.Min(road[k], trail[k]) < s.roadHalfWidth + s.routeGrassFade + G)
                {
                    RouteDist(p, out var dr, out var dt);
                    float er = dr - s.roadHalfWidth, et = dt - s.trailHalfWidth; float e = Mathf.Min(er, et);
                    f = e <= 0f ? s.grassOnRoute : Mathf.Lerp(s.grassOnRoute, 1f, Mathf.Clamp01(e / Mathf.Max(0.1f, s.routeGrassFade)));
                }
                return f * (1f - 0.9f * soil[k]);
            }

            float[] Field() { var f = new float[N * N]; for (int i = 0; i < f.Length; i++) f[i] = 1e9f; return f; }
            public static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
            public int Cell(Vector2 w) { int x = Mathf.Clamp((int)((w.x - origin.x) / G), 0, N - 1), z = Mathf.Clamp((int)((w.y - origin.z) / G), 0, N - 1); return z * N + x; }
            Vector2 CellCenter(int x, int z) => new Vector2(origin.x + (x + 0.5f) * G, origin.z + (z + 0.5f) * G);

            void Rasterize(List<Vector2> line, float[] f, float R)
            {
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    Vector2 a = line[i], b = line[i + 1];
                    int x0 = Mathf.Max(0, (int)((Mathf.Min(a.x, b.x) - R - origin.x) / G)), x1 = Mathf.Min(N - 1, (int)((Mathf.Max(a.x, b.x) + R - origin.x) / G));
                    int z0 = Mathf.Max(0, (int)((Mathf.Min(a.y, b.y) - R - origin.z) / G)), z1 = Mathf.Min(N - 1, (int)((Mathf.Max(a.y, b.y) + R - origin.z) / G));
                    Vector2 ab = b - a; float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                    {
                        var p = CellCenter(x, z); float tt = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                        float dd = Vector2.Distance(p, a + ab * tt); int k = z * N + x; if (dd < f[k]) f[k] = dd;
                    }
                }
            }

            void Disc(Vector2 c, float r, bool[] m)
            {
                int x0 = Mathf.Max(0, (int)((c.x - r - origin.x) / G)), x1 = Mathf.Min(N - 1, (int)((c.x + r - origin.x) / G));
                int z0 = Mathf.Max(0, (int)((c.y - r - origin.z) / G)), z1 = Mathf.Min(N - 1, (int)((c.y + r - origin.z) / G));
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) if ((CellCenter(x, z) - c).sqrMagnitude <= (r + G * 0.5f) * (r + G * 0.5f)) m[z * N + x] = true;
            }

            void BoundsBlock(GameObject go, float pad, bool[] m)
            {
                var rs = go.GetComponentsInChildren<Renderer>(); var cs = go.GetComponentsInChildren<Collider>();
                if (rs.Length == 0 && cs.Length == 0) { Disc(XZ(go.transform.position), pad + 2f, m); return; }
                var b = rs.Length > 0 ? rs[0].bounds : cs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds); foreach (var c in cs) b.Encapsulate(c.bounds);
                if (b.size.x > 400f || b.size.z > 400f) { Disc(XZ(go.transform.position), pad + 4f, m); return; } // ignore huge (fx volumes etc.)
                int x0 = Mathf.Max(0, (int)((b.min.x - pad - origin.x) / G)), x1 = Mathf.Min(N - 1, (int)((b.max.x + pad - origin.x) / G));
                int z0 = Mathf.Max(0, (int)((b.min.z - pad - origin.z) / G)), z1 = Mathf.Min(N - 1, (int)((b.max.z + pad - origin.z) / G));
                for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) m[z * N + x] = true;
            }

            /// <summary>Biome weights (ridge, gully, prairie) with noise-warped boundaries, like the map art.</summary>
            public Vector3 Biome(Vector2 w)
            {
                float warp = s.boundaryWarp * (Mathf.PerlinNoise(w.y * 0.0012f + 11.3f, 3.7f) - 0.5f) + 0.35f * s.boundaryWarp * (Mathf.PerlinNoise(w.y * 0.004f, w.x * 0.004f + 5.1f) - 0.5f);
                float xw = w.x + warp;
                float ridge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.ridgeToGully - 160f, s.ridgeToGully + 160f, xw));
                float prairie = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.gullyToPrairie - 220f, s.gullyToPrairie + 220f, xw));
                return new Vector3(ridge, Mathf.Clamp01(1f - ridge - prairie), prairie);
            }

            public float Slope(Vector2 w) => d.GetSteepness((w.x - origin.x) / size.x, (w.y - origin.z) / size.z);
            public float CreekNear(Vector2 w) { float c = creek[Cell(w)]; return c > 1e8f ? 0f : Mathf.Exp(-(c / s.creekBand) * (c / s.creekBand)); }
        }

        [Serializable] class CreekLine { public float acc; public ASHRegionBuilder.XZ[] pts; }
        [Serializable] class CreekJson { public CreekLine[] creeks; }

        // ------------------------------------------------------------------ trees
        static void ScatterTrees(Ctx c, List<ASHVegetationSettings.TreeEntry> entries, int protoOffset, List<TreeInstance> outTrees)
        {
            var s = c.s; var rnd = c.rnd;
            float clumpMax = 1f + s.clumping;
            float dMax = Mathf.Max(s.ridgeTrees, s.gullyTrees + s.creekTrees, s.prairieTrees + s.creekTrees * 0.6f) * s.treeDensityScale * clumpMax;
            if (dMax <= 0f) return;
            float cell = Mathf.Sqrt(10000f / dMax);
            int nx = Mathf.FloorToInt(c.size.x / cell), nz = Mathf.FloorToInt(c.size.z / cell);
            var w = new float[entries.Count];
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    var p = new Vector2(c.origin.x + (ix + (float)rnd.NextDouble()) * cell, c.origin.z + (iz + (float)rnd.NextDouble()) * cell);
                    int k = c.Cell(p);
                    if (c.blockT[k] || !c.TreeClearOfRoutes(p, k)) { rnd.NextDouble(); continue; }
                    var b = c.Biome(p); float cr = c.CreekNear(p);
                    float clump = Mathf.Lerp(1f, 2f * Mathf.Clamp01(Mathf.PerlinNoise(p.x * 0.009f + 3.1f, p.y * 0.009f + 7.7f) * 1.3f - 0.15f), s.clumping);
                    float dens = (b.x * s.ridgeTrees + b.y * s.gullyTrees + b.z * s.prairieTrees + cr * s.creekTrees * (b.x * 0.4f + b.y + b.z * 0.6f)) * s.treeDensityScale * clump;
                    float slope = c.Slope(p); dens *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.maxTreeSlope - 8f, s.maxTreeSlope, slope));
                    if (rnd.NextDouble() * dMax >= dens) continue;

                    float sum = 0f;
                    for (int i = 0; i < entries.Count; i++) { var e = entries[i]; w[i] = b.x * e.ridge + b.y * e.gully + b.z * e.prairie + cr * e.creek; sum += w[i]; }
                    if (sum <= 0f) continue;
                    float pick = (float)rnd.NextDouble() * sum; int sel = 0;
                    for (; sel < entries.Count - 1; sel++) { pick -= w[sel]; if (pick <= 0f) break; }
                    var en = entries[sel]; float sc = Mathf.Lerp(en.minScale, en.maxScale, (float)rnd.NextDouble());
                    outTrees.Add(new TreeInstance
                    {
                        position = new Vector3((p.x - c.origin.x) / c.size.x, 0f, (p.y - c.origin.z) / c.size.z),
                        prototypeIndex = protoOffset + sel, widthScale = sc * Mathf.Lerp(0.9f, 1.1f, (float)rnd.NextDouble()), heightScale = sc,
                        rotation = (float)(rnd.NextDouble() * Math.PI * 2.0), color = Color.Lerp(Color.white, new Color(0.85f, 0.85f, 0.82f), (float)rnd.NextDouble()), lightmapColor = Color.white,
                    });
                }
        }

        // ------------------------------------------------------------------ grass
        static long ScatterGrass(Ctx c, List<ASHVegetationSettings.GrassEntry> entries, int res, List<int[,]> layers)
        {
            var s = c.s; var rnd = new Random(s.seed + 7); long total = 0;
            bool coverage = c.d.detailScatterMode == DetailScatterMode.CoverageMode;
            for (int i = 0; i < entries.Count; i++) layers.Add(new int[res, res]);
            float cx = c.size.x / res, cz = c.size.z / res; var w = new float[entries.Count];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var p = new Vector2(c.origin.x + (x + 0.5f) * cx, c.origin.z + (z + 0.5f) * cz); int k = c.Cell(p);
                    if (c.blockG[k]) continue;
                    float routeF = c.GrassRouteFactor(p, k); if (routeF <= 0.001f) continue;
                    var b = c.Biome(p); float cr = c.CreekNear(p); float slope = c.Slope(p);
                    float f = s.grassDensity * (1f - s.rockReduction * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.maxGrassSlope * 0.55f, s.maxGrassSlope, slope)));
                    if (slope > s.maxGrassSlope) f = 0f;
                    f *= Mathf.Lerp(0.35f, 1.45f, Mathf.PerlinNoise(p.x * 0.025f + 1.7f, p.y * 0.025f + 9.2f)) * (1f - 0.35f * b.x) * routeF; // patchy; thinner under timber; thinned on routes
                    if (f <= 0.01f) continue;
                    float sum = 0f;
                    for (int i = 0; i < entries.Count; i++) { var e = entries[i]; w[i] = b.x * e.ridge + b.y * e.gully + b.z * e.prairie + cr * e.creek; sum += w[i]; }
                    if (sum <= 0f) continue;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        float v = f * w[i] / sum * Mathf.Max(1f, sum); // creek boost raises total density near water
                        if (coverage) v = v / 16f * 255f;
                        int n = (int)v; if (rnd.NextDouble() < v - n) n++;
                        n = Mathf.Clamp(n, 0, coverage ? 255 : 64);
                        layers[i][z, x] = n; total += n;
                    }
                }
            return total;
        }
    }
}
