using System;
using System.Collections.Generic;
using System.IO;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Map;
using ASH.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ASH.EditorTools.Build
{
    /// <summary>
    /// Builds the region from Assets/_ASH/Data/Map/ASH_RegionLayout.json (shared with Tools/MapArt, which renders
    /// the GPS map art): region + survey ScriptableObjects, placeholder POI structures (the Redstone Camp lookout
    /// tower is the full tutorial build), survey interactables, pickups and salvage around Redstone Camp and the
    /// routes, GPS/journal systems and the Survey objective. Owns only the scene root "ASH_Region" and the
    /// "ASH_Systems/GPS" child; never moves the player, the cabin or anything else of Anthony's.
    /// </summary>
    public static class ASHRegionBuilder
    {
        public const string LayoutPath = ASHContentBuilder.Root + "/Data/Map/ASH_RegionLayout.json";
        public const string MapTexPath = ASHContentBuilder.Root + "/Art/Map/ASH_RegionMap.png";
        public const string DraftPath = ASHContentBuilder.Root + "/Art/Map/ASH_map_draft.png";
        public const string RegionPath = ASHContentBuilder.Root + "/Data/Map/ASH_RegionMap.asset";
        public const string SurveyDir = ASHContentBuilder.Root + "/Data/Map/Surveys";
        const string M = ASHContentBuilder.MatDir + "/Region";

        // ---------------------------------------------------------------- json
        [Serializable] public class XZ { public float x, z; public Vector2 V => new Vector2(x, z); }
        [Serializable] public class Poi { public string id, name, kind; public float x, z; public Vector2 V => new Vector2(x, z); }
        [Serializable] public class Route { public string id, name, kind; public XZ[] pts; }
        [Serializable] public class Circ { public float x, z, r; }
        [Serializable] public class PathJ { public string name; public float width; public XZ[] pts; }
        [Serializable] public class Surv { public string id, name, structure, kind, poi, journal, next; public int order; public Circ[] circles; public PathJ[] paths; public string[] unlocks; public bool revealAll; }
        [Serializable] public class Layout
        {
            public string regionName; public float worldMinX, worldMinZ, worldSize, startRevealRadius, walkRevealRadius, gpsUncertainty, gpsDrift;
            public Poi[] pois; public Route[] roads, trails; public Surv[] surveys;
            public Poi P(string id) => Array.Find(pois, p => p.id == id);
        }
        public static Layout Load() => JsonUtility.FromJson<Layout>(File.ReadAllText(LayoutPath));

        // ---------------------------------------------------------------- data
        [MenuItem("ASH/Build/7a. Region Map Data (SOs only)")]
        public static ASHRegionMap BuildData()
        {
            var L = Load();
            ImportTexture(MapTexPath, 2048, true);
            ImportTexture(DraftPath, 1024, false);
            var region = ASHBuildUtil.Asset<ASHRegionMap>(RegionPath);
            region.regionName = L.regionName;
            region.mapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(MapTexPath);
            region.worldMin = new Vector2(L.worldMinX, L.worldMinZ); region.worldSize = new Vector2(L.worldSize, L.worldSize);
            region.startRevealRadius = L.startRevealRadius; region.walkRevealRadius = L.walkRevealRadius;
            region.gpsUncertainty = L.gpsUncertainty; region.gpsDrift = L.gpsDrift;
            region.pois = new List<ASHPoi>();
            foreach (var p in L.pois)
                region.pois.Add(new ASHPoi { id = p.id, displayName = p.name, kind = Enum.TryParse(p.kind, out ASHPoiKind k) ? k : ASHPoiKind.Landmark, worldXZ = p.V });
            region.surveys = new List<ASHSurveyPointDef>();
            foreach (var s in L.surveys)
            {
                var d = ASHBuildUtil.Asset<ASHSurveyPointDef>($"{SurveyDir}/ASH_Survey_{s.id}.asset");
                d.surveyId = s.id; d.displayName = s.name; d.structureName = s.structure; d.order = s.order;
                d.kind = Enum.TryParse(s.kind, out ASHSurveyKind sk) ? sk : ASHSurveyKind.TopoPanel;
                var poi = L.P(s.poi); if (poi != null && d.worldXZ == Vector2.zero) d.worldXZ = poi.V;
                d.journalEntry = s.journal; d.nextHint = s.next; d.revealAll = s.revealAll;
                d.unlocksPois = new List<string>(s.unlocks ?? new string[0]);
                d.circles = new List<ASHRevealCircle>();
                foreach (var c in s.circles ?? new Circ[0]) d.circles.Add(new ASHRevealCircle { center = new Vector2(c.x, c.z), radius = c.r });
                d.paths = new List<ASHRevealPath>();
                foreach (var p in s.paths ?? new PathJ[0])
                {
                    var rp = new ASHRevealPath { name = p.name, width = p.width, points = new List<Vector2>() };
                    foreach (var q in Smooth(p.pts, 4)) rp.points.Add(q);
                    d.paths.Add(rp);
                }
                EditorUtility.SetDirty(d); region.surveys.Add(d);
            }
            region.surveys.Sort((a, b) => a.order.CompareTo(b.order));
            EditorUtility.SetDirty(region); ASHBuildUtil.SaveOwn();
            Debug.Log($"[ASH] Region data: {region.pois.Count} POIs, {region.surveys.Count} surveys");
            return region;
        }

        static void ImportTexture(string path, int max, bool mips)
        {
            if (!File.Exists(path)) { Debug.LogWarning("[ASH] missing " + path); return; }
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) return;
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.mipmapEnabled = mips; imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = max; imp.textureCompression = TextureImporterCompression.CompressedHQ; imp.anisoLevel = 4; imp.isReadable = false;
            imp.SaveAndReimport();
        }

        /// <summary>Catmull-Rom through the points (same curve the map art draws).</summary>
        public static List<Vector2> Smooth(XZ[] pts, int seg)
        {
            var o = new List<Vector2>(); if (pts == null || pts.Length == 0) return o;
            if (pts.Length < 3) { foreach (var p in pts) o.Add(p.V); return o; }
            var P = new List<Vector2> { pts[0].V }; foreach (var p in pts) P.Add(p.V); P.Add(pts[pts.Length - 1].V);
            for (int i = 1; i < P.Count - 2; i++)
                for (int k = 0; k < seg; k++)
                {
                    float t = k / (float)seg, t2 = t * t, t3 = t2 * t; Vector2 p0 = P[i - 1], p1 = P[i], p2 = P[i + 1], p3 = P[i + 2];
                    o.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            o.Add(P[P.Count - 2]); return o;
        }

        // ---------------------------------------------------------------- scene
        static Terrain _t; static Transform _root;
        static Material mWood, mDark, mSteel, mRust, mCharred, mConcrete, mRoof, mPanel, mBrass, mRock, mTank, mPaint;

        [MenuItem("ASH/Build/7. Region + Survey Points (open prototype scene)")]
        public static void BuildScene()
        {
            if (EditorApplication.isPlaying) throw new Exception("Stop Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ASHSceneSetup.PrototypeScene) throw new Exception("Open " + ASHSceneSetup.PrototypeScene + " first (active: " + scene.path + ")");
            _t = Terrain.activeTerrain; if (!_t) throw new Exception("No terrain.");
            var region = BuildData(); var L = Load();
            Mats();
            int treeProtos = _t.terrainData.treePrototypes.Length, treeCount = _t.terrainData.treeInstanceCount;
            var old = GameObject.Find("ASH_Region"); if (old) Object.DestroyImmediate(old);
            Physics.SyncTransforms();
            _root = new GameObject("ASH_Region").transform;
            var poiRoot = Child(_root, "POIs"); var pickRoot = Child(_root, "Pickups");

            ASHSurveyPoint first = null; var report = new List<string>();
            foreach (var p in L.pois)
            {
                var surv = Array.Find(L.surveys, s => s.poi == p.id);
                var def = surv != null ? region.surveys.Find(d => d.surveyId == surv.id) : null;
                Vector2 xz = p.id == "redstone_camp" || p.id == "the_overlook" ? p.V : Clear(p.V, p.kind == "Survey" ? 6f : 4f);
                var go = new GameObject("POI_" + p.id); go.transform.SetParent(poiRoot); go.transform.position = Ground(xz);
                ASHSurveyPoint sp = null;
                switch (p.id)
                {
                    case "redstone_lookout": sp = Tower(go.transform, 8f, 3.1f, cab: true, ruined: false, tank: false); break;
                    case "ranger_station": sp = RangerStation(go.transform); break;
                    case "ash_hollow": sp = Tower(go.transform, 10f, 3.6f, cab: false, ruined: false, tank: true); break;
                    case "coyote_flats": sp = RadioHut(go.transform); break;
                    case "lookout_ruin": sp = Tower(go.transform, 6f, 2.8f, cab: false, ruined: true, tank: false); break;
                    case "burned_homestead": Homestead(go.transform); break;
                    case "creek_crossing": Bridge(go.transform); break;
                    case "old_quarry": Quarry(go.transform); break;
                    case "redstone_camp": case "the_overlook": break;
                    default: if (p.kind == "Supply") Shack(go.transform, p.id); break;
                }
                if (sp != null && def != null)
                {
                    sp.Configure(def);
                    def.worldXZ = new Vector2(sp.transform.position.x, sp.transform.position.z); EditorUtility.SetDirty(def);
                    if (def.order == 1) first = sp;
                }
                var poiEntry = region.FindPoi(p.id); if (poiEntry != null) poiEntry.worldXZ = new Vector2(go.transform.position.x, go.transform.position.z);
                report.Add($"{p.id} ({go.transform.position.x:0},{go.transform.position.y:0},{go.transform.position.z:0})");
            }
            EditorUtility.SetDirty(region);
            PlacePickups(L, pickRoot);
            int painted = 0; // terrain splat is a separate step: ASH/World/Paint Region Roads + Trails
            if (_t.terrainData.treePrototypes.Length != treeProtos || _t.terrainData.treeInstanceCount != treeCount)
                throw new Exception($"[ASH] Terrain trees changed during region build ({treeProtos}/{treeCount} -> {_t.terrainData.treePrototypes.Length}/{_t.terrainData.treeInstanceCount}); aborting before save.");

            // systems
            var systems = GameObject.Find("ASH_Systems")?.transform ?? new GameObject("ASH_Systems").transform;
            var oldGps = systems.Find("GPS"); if (oldGps) Object.DestroyImmediate(oldGps.gameObject);
            var gpsGo = new GameObject("GPS"); gpsGo.transform.SetParent(systems);
            var gps = gpsGo.AddComponent<ASHGpsMap>(); gpsGo.AddComponent<ASHJournalLog>();
            var player = GameObject.Find("ASH_Player");
            ASHBuildUtil.Set(gps, "region", region);
            if (player) ASHBuildUtil.Set(gps, "player", player.transform);
            foreach (var hud in Object.FindObjectsByType<ASHHud>(FindObjectsInactive.Include)) ASHBuildUtil.Set(hud, "gps", gps);
            foreach (var obj in Object.FindObjectsByType<ASHObjectiveTracker>(FindObjectsInactive.Include))
            {
                ASHBuildUtil.Set(obj, "gps", gps);
                if (first) { ASHBuildUtil.Set(obj, "firstSurvey", first.Definition); ASHBuildUtil.Set(obj, "firstSurveyPoint", first.transform); }
            }
            ASHBuildUtil.SaveOwn();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ASH] Region built: {string.Join("; ", report)}. Pickups {pickRoot.childCount}. Route texels painted {painted}. First survey {(first ? first.name : "none")}.");
        }

        static void Mats()
        {
            mWood = ASHBuildUtil.Lit(M + "/ASH_M_WeatheredWood.mat", new Color(0.46f, 0.39f, 0.31f), 0.15f);
            mDark = ASHBuildUtil.Lit(M + "/ASH_M_DarkTimber.mat", new Color(0.27f, 0.21f, 0.16f), 0.15f);
            mSteel = ASHBuildUtil.Lit(M + "/ASH_M_TowerSteel.mat", new Color(0.42f, 0.43f, 0.44f), 0.4f, null, 0.7f);
            mRust = ASHBuildUtil.Lit(M + "/ASH_M_TankRust.mat", new Color(0.43f, 0.27f, 0.18f), 0.3f, null, 0.4f);
            mCharred = ASHBuildUtil.Lit(M + "/ASH_M_CharredTimber.mat", new Color(0.08f, 0.07f, 0.065f), 0.1f);
            mConcrete = ASHBuildUtil.Lit(M + "/ASH_M_Concrete.mat", new Color(0.55f, 0.53f, 0.5f), 0.1f);
            mRoof = ASHBuildUtil.Lit(M + "/ASH_M_RoofTin.mat", new Color(0.35f, 0.33f, 0.31f), 0.35f, null, 0.5f);
            mPanel = ASHBuildUtil.Lit(M + "/ASH_M_TopoPanel.mat", new Color(0.78f, 0.72f, 0.58f), 0.2f);
            mBrass = ASHBuildUtil.Lit(M + "/ASH_M_Brass.mat", new Color(0.7f, 0.55f, 0.25f), 0.6f, null, 0.9f);
            mRock = ASHBuildUtil.Lit(M + "/ASH_M_QuarryRock.mat", new Color(0.5f, 0.42f, 0.36f), 0.15f);
            mTank = ASHBuildUtil.Lit(M + "/ASH_M_WaterTank.mat", new Color(0.5f, 0.52f, 0.5f), 0.35f, null, 0.6f);
            mPaint = ASHBuildUtil.Lit(M + "/ASH_M_ForestGreen.mat", new Color(0.22f, 0.3f, 0.22f), 0.2f);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MapTexPath); if (tex) { mPanel.SetTexture("_BaseMap", tex); mPanel.SetColor("_BaseColor", Color.white); EditorUtility.SetDirty(mPanel); }
        }

        static Transform Child(Transform p, string n) { var t = new GameObject(n).transform; t.SetParent(p, false); return t; }
        static Vector3 Ground(Vector2 xz) { var p = new Vector3(xz.x, 0, xz.y); p.y = _t.SampleHeight(p) + _t.transform.position.y; return p; }
        static float H(Vector2 xz) => _t.SampleHeight(new Vector3(xz.x, 0, xz.y)) + _t.transform.position.y;

        /// <summary>Nudges a footprint away from any existing non-terrain collider (Anthony's objects stay put).</summary>
        static Vector2 Clear(Vector2 xz, float r)
        {
            for (float d = 0; d <= 40f; d += 4f)
                for (int i = 0; i < (d == 0 ? 1 : 12); i++)
                {
                    float a = i * Mathf.PI / 6f; var q = xz + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    var c = Ground(q) + Vector3.up * 3f; bool hit = false;
                    foreach (var col in Physics.OverlapBox(c, new Vector3(r, 2.8f, r)))
                        if (!(col is TerrainCollider) && !col.transform.IsChildOf(_root)) { hit = true; break; }
                    if (!hit) return q;
                }
            return xz;
        }

        static GameObject Box(Transform p, string n, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default, bool col = true)
            => ASHBuildUtil.Prim(PrimitiveType.Cube, n, p, pos, euler, scale, m, col);

        /// <summary>Walkable plank ramp between two local points (top surface on the line).</summary>
        static void Ramp(Transform p, string n, Vector3 a, Vector3 b, float width, bool railLeft, bool railRight)
        {
            var dir = b - a; float len = dir.magnitude; var rot = Quaternion.LookRotation(dir / len, Vector3.up);
            var g = Box(p, n, (a + b) * 0.5f, new Vector3(width, 0.2f, len + 0.15f), mWood);
            g.transform.localRotation = rot; g.transform.localPosition -= rot * Vector3.up * 0.1f;
            var side = rot * Vector3.right;
            if (railLeft) Rail(p, n + "_RailL", a - side * width * 0.5f, b - side * width * 0.5f);
            if (railRight) Rail(p, n + "_RailR", a + side * width * 0.5f, b + side * width * 0.5f);
        }
        static void Rail(Transform p, string n, Vector3 a, Vector3 b)
        {
            var dir = b - a; float len = dir.magnitude; if (len < 0.05f) return; var rot = Quaternion.LookRotation(dir / len, Vector3.up);
            var top = Box(p, n, (a + b) * 0.5f + Vector3.up * 1.0f, new Vector3(0.06f, 0.06f, len), mDark); top.transform.localRotation = rot;
            var mid = Box(p, n + "_Mid", (a + b) * 0.5f + Vector3.up * 0.55f, new Vector3(0.04f, 0.04f, len), mDark, default, false); mid.transform.localRotation = rot;
            var wall = top.GetComponent<BoxCollider>(); wall.size = new Vector3(1f, 18f, 1f); wall.center = new Vector3(0, -8f, 0); // 1.1 m invisible guard
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 1.6f) + 1);
            for (int i = 0; i < posts; i++) Box(p, n + "_Post" + i, Vector3.Lerp(a, b, i / (float)(posts - 1)) + Vector3.up * 0.5f, new Vector3(0.07f, 1.0f, 0.07f), mDark, default, false);
        }

        static ASHSurveyPoint Panel(Transform p, Vector3 local, float yaw, string kind)
        {
            var root = new GameObject("SurveyPoint_" + kind).transform; root.SetParent(p, false); root.localPosition = local; root.localEulerAngles = new Vector3(0, yaw, 0);
            Box(root, "Post", new Vector3(0, 0.5f, 0), new Vector3(0.1f, 1f, 0.1f), mDark, default, false);
            if (kind == "SurveyMarker") { ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Disc", root, new Vector3(0, 1.02f, 0), Vector3.zero, new Vector3(0.35f, 0.02f, 0.35f), mBrass); }
            else if (kind == "TrailRegister") { Box(root, "RegisterBox", new Vector3(0, 1.15f, 0), new Vector3(0.45f, 0.35f, 0.25f), mPaint, default, false); }
            else if (kind == "RadioRelay") { Box(root, "RelayCabinet", new Vector3(0, 1.1f, 0), new Vector3(0.5f, 0.6f, 0.3f), mSteel, default, false); }
            var board = Box(root, "TopoBoard", new Vector3(0, 1.1f, 0.12f), new Vector3(0.9f, 0.04f, 0.65f), mPanel, new Vector3(-35f, 0, 0), false);
            if (kind != "TopoPanel") board.transform.localPosition = new Vector3(0, 0.95f, 0.35f);
            var col = root.gameObject.AddComponent<BoxCollider>(); col.isTrigger = true; col.center = new Vector3(0, 1f, 0.1f); col.size = new Vector3(1.4f, 1.6f, 1.2f);
            int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) ASHBuildUtil.SetLayerRecursive(root.gameObject, layer);
            return root.gameObject.AddComponent<ASHSurveyPoint>();
        }

        /// <summary>
        /// Square tower with a ramp spiral around it: four flights (one per side) and corner landings, platform at
        /// height h. Ramps are 1.4 m wide, roughly 15-25 degrees, railed on the outside.
        /// </summary>
        static ASHSurveyPoint Tower(Transform p, float h, float half, bool cab, bool ruined, bool tank)
        {
            Vector2 c = new Vector2(p.position.x, p.position.z);
            float R = half + 0.8f, w = 1.4f;
            Vector2[] corners = { new Vector2(-R, -R), new Vector2(R, -R), new Vector2(R, R), new Vector2(-R, R) };
            float baseY = float.MaxValue; foreach (var k in corners) baseY = Mathf.Min(baseY, H(c + k)); baseY = Mathf.Min(baseY, H(c));
            p.position = new Vector3(c.x, baseY, c.y);
            float h0 = Mathf.Clamp(H(c + corners[0]) - baseY, 0f, h * 0.4f);
            // legs + bracing
            foreach (var k in new[] { new Vector2(-half, -half), new Vector2(half, -half), new Vector2(half, half), new Vector2(-half, half) })
            {
                Box(p, "Leg", new Vector3(k.x, h * 0.5f - 1.5f, k.y), new Vector3(0.3f, h + 3f, 0.3f), ruined ? mCharred : mSteel);
            }
            for (float y = 2.5f; y < h - 0.5f; y += 3f)
            {
                Box(p, "BraceS", new Vector3(0, y, -half), new Vector3(half * 2, 0.12f, 0.12f), mSteel, default, false);
                Box(p, "BraceN", new Vector3(0, y, half), new Vector3(half * 2, 0.12f, 0.12f), mSteel, default, false);
                Box(p, "BraceW", new Vector3(-half, y, 0), new Vector3(0.12f, 0.12f, half * 2), mSteel, default, false);
                Box(p, "BraceE", new Vector3(half, y, 0), new Vector3(0.12f, 0.12f, half * 2), mSteel, default, false);
            }
            // platform reaches the ramps' inner edge
            float ph = R - w * 0.5f;
            Box(p, "Platform", new Vector3(0, h - 0.12f, 0), new Vector3(ph * 2f + 0.02f, 0.24f, ph * 2f + 0.02f), mWood);
            // spiral: flight i along side i, from corner i to corner i+1
            for (int i = 0; i < 4; i++)
            {
                float y0 = h0 + (h - h0) * i / 4f, y1 = h0 + (h - h0) * (i + 1) / 4f;
                Vector2 a = corners[i], b = corners[(i + 1) % 4]; Vector2 dir = (b - a).normalized;
                Vector3 A = new Vector3(a.x + dir.x * w * 0.5f, y0, a.y + dir.y * w * 0.5f), B = new Vector3(b.x - dir.x * w * 0.5f, y1, b.y - dir.y * w * 0.5f);
                Ramp(p, "Flight" + i, A, B, w, railLeft: false, railRight: !(ruined && i == 2));
                if (i < 3) Box(p, "Landing" + i, new Vector3(b.x, y1 - 0.1f, b.y), new Vector3(w, 0.2f, w), mWood);
                if (i > 0 && i < 4) Box(p, "LandingPost" + i, new Vector3(corners[i].x, (y0 - 1.5f) * 0.5f, corners[i].y), new Vector3(0.18f, y0 + 1.5f, 0.18f), mDark, default, false);
            }
            Box(p, "Landing0", new Vector3(corners[0].x, h0 - 0.1f, corners[0].y), new Vector3(w, 0.2f, w), mWood);
            // top arrival: a strip along the south band so it meets the platform edge (not just its corner)
            Box(p, "LandingTop", new Vector3(corners[0].x + 0.8f, h - 0.1f, corners[0].y), new Vector3(w + 1.6f, 0.2f, w), mWood);
            // platform rails on its own edge; opening on the south edge beside the arrival strip
            Rail(p, "TopRailS", new Vector3(-ph + 1.7f, h, -ph), new Vector3(ph, h, -ph));
            Rail(p, "TopRailE", new Vector3(ph, h, -ph), new Vector3(ph, h, ph));
            if (!ruined) Rail(p, "TopRailN", new Vector3(ph, h, ph), new Vector3(-ph, h, ph));
            Rail(p, "TopRailW", new Vector3(-ph, h, ph), new Vector3(-ph, h, -ph));
            Rail(p, "ArrivalRailEnd", new Vector3(-R + w * 0.5f + 1.6f, h, -R - w * 0.5f), new Vector3(-R + w * 0.5f + 1.6f, h, -R + w * 0.5f));
            Rail(p, "ArrivalRailS", new Vector3(-R - w * 0.5f, h, -R - w * 0.5f), new Vector3(-R + w * 0.5f + 1.6f, h, -R - w * 0.5f));
            if (cab)
            {
                foreach (var k in new[] { new Vector2(-ph, -ph), new Vector2(ph, -ph), new Vector2(ph, ph), new Vector2(-ph, ph) })
                    Box(p, "CabPost", new Vector3(k.x * 0.95f, h + 1.25f, k.y * 0.95f), new Vector3(0.14f, 2.5f, 0.14f), mDark, default, false);
                Box(p, "CabRoof", new Vector3(0, h + 2.6f, 0), new Vector3(ph * 2 + 0.8f, 0.15f, ph * 2 + 0.8f), mRoof, default, false);
            }
            if (ruined)
            {
                Box(p, "FallenRoof", new Vector3(0.6f, h + 0.3f, 0.8f), new Vector3(ph * 1.6f, 0.12f, ph * 1.2f), mCharred, new Vector3(14, 20, 8), false);
                Box(p, "BrokenPost", new Vector3(ph * 0.9f, h + 0.7f, -ph * 0.9f), new Vector3(0.14f, 1.4f, 0.14f), mCharred, new Vector3(0, 0, 12), false);
            }
            if (tank)
            {
                ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Tank", p, new Vector3(0, h + 1.6f, 0), Vector3.zero, new Vector3(ph * 1.3f, 1.6f, ph * 1.3f), mTank, true);
                ASHBuildUtil.Prim(PrimitiveType.Sphere, "TankCap", p, new Vector3(0, h + 3.2f, 0), Vector3.zero, new Vector3(ph * 1.3f, 0.8f, ph * 1.3f), mRust);
                return Panel(p, new Vector3(ph - 0.5f, h, 0.2f), -90f, "TrailRegister");
            }
            return Panel(p, ruined ? new Vector3(-ph * 0.4f, h, 0) : new Vector3(0, h, ph * 0.45f), 0f, ruined ? "SurveyMarker" : "TopoPanel");
        }

        static ASHSurveyPoint RangerStation(Transform p)
        {
            Vector2 c = new Vector2(p.position.x, p.position.z); float hx = 4f, hz = 3f, hgt = 3.2f;
            float baseY = Mathf.Min(H(c + new Vector2(-hx, -hz)), H(c + new Vector2(hx, -hz)), H(c + new Vector2(hx, hz)), H(c + new Vector2(-hx, hz)), H(c));
            p.position = new Vector3(c.x, baseY, c.y);
            Box(p, "Foundation", new Vector3(0, -0.6f, 0), new Vector3(hx * 2 + 0.4f, 1.6f, hz * 2 + 0.4f), mConcrete);
            Box(p, "Building", new Vector3(0, hgt * 0.5f, 0), new Vector3(hx * 2, hgt, hz * 2), mPaint);
            Box(p, "Roof", new Vector3(0, hgt + 0.1f, 0), new Vector3(hx * 2 + 0.3f, 0.2f, hz * 2 + 0.3f), mRoof);
            Box(p, "Door", new Vector3(-1.5f, 1.05f, hz + 0.01f), new Vector3(1f, 2.1f, 0.05f), mDark, default, false);
            Box(p, "Window1", new Vector3(1.2f, 1.6f, hz + 0.01f), new Vector3(1.2f, 0.9f, 0.05f), mCharred, default, false);
            Box(p, "Window2", new Vector3(hx + 0.01f, 1.6f, 0), new Vector3(0.05f, 0.9f, 1.4f), mCharred, default, false);
            // ramp along the south wall to the roof (about 22 degrees), railed outside
            float w = 1.4f, z = -hz - w * 0.5f - 0.05f;
            float y0 = Mathf.Clamp(H(c + new Vector2(-hx - 3f, z)) - baseY, 0f, 1f);
            Ramp(p, "RoofRamp", new Vector3(-hx - 3.5f, y0, z), new Vector3(hx - 0.2f, hgt + 0.2f, z), w, railLeft: false, railRight: true);
            Box(p, "RampLanding", new Vector3(hx + 0.5f, hgt + 0.1f, z), new Vector3(1.4f, 0.2f, w), mWood);
            Rail(p, "RoofRailN", new Vector3(-hx, hgt + 0.2f, hz + 0.1f), new Vector3(hx, hgt + 0.2f, hz + 0.1f));
            Rail(p, "RoofRailE", new Vector3(hx + 1.2f, hgt + 0.2f, z - w * 0.5f), new Vector3(hx + 1.2f, hgt + 0.2f, hz + 0.1f));
            Rail(p, "RoofRailW", new Vector3(-hx - 0.1f, hgt + 0.2f, -hz + 0.2f), new Vector3(-hx - 0.1f, hgt + 0.2f, hz + 0.1f));
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Antenna", p, new Vector3(hx - 0.6f, hgt + 2.2f, hz - 0.6f), Vector3.zero, new Vector3(0.06f, 2f, 0.06f), mSteel);
            return Panel(p, new Vector3(0.5f, hgt + 0.2f, 0.6f), 0f, "SurveyMarker");
        }

        static ASHSurveyPoint RadioHut(Transform p)
        {
            Vector2 c = new Vector2(p.position.x, p.position.z); float hx = 2.2f, hgt = 2.8f;
            float baseY = Mathf.Min(H(c + new Vector2(-hx, -hx)), H(c + new Vector2(hx, hx)), H(c + new Vector2(hx, -hx)), H(c + new Vector2(-hx, hx)));
            p.position = new Vector3(c.x, baseY, c.y);
            Box(p, "Foundation", new Vector3(0, -0.5f, 0), new Vector3(hx * 2 + 0.3f, 1.2f, hx * 2 + 0.3f), mConcrete);
            Box(p, "Hut", new Vector3(0, hgt * 0.5f, 0), new Vector3(hx * 2, hgt, hx * 2), mConcrete);
            Box(p, "Roof", new Vector3(0, hgt + 0.08f, 0), new Vector3(hx * 2 + 0.2f, 0.16f, hx * 2 + 0.2f), mRoof);
            Box(p, "Door", new Vector3(0, 1f, hx + 0.01f), new Vector3(0.9f, 2f, 0.05f), mRust, default, false);
            float w = 1.4f, z = -hx - w * 0.5f - 0.05f;
            Ramp(p, "RoofRamp", new Vector3(-hx - 4.5f, 0f, z), new Vector3(hx - 0.1f, hgt + 0.16f, z), w, railLeft: false, railRight: true);
            Box(p, "RampLanding", new Vector3(hx + 0.5f, hgt + 0.06f, z), new Vector3(1.4f, 0.2f, w), mWood);
            Rail(p, "RoofRailN", new Vector3(-hx, hgt + 0.16f, hx + 0.05f), new Vector3(hx, hgt + 0.16f, hx + 0.05f));
            Rail(p, "RoofRailW", new Vector3(-hx - 0.05f, hgt + 0.16f, -hx + 0.1f), new Vector3(-hx - 0.05f, hgt + 0.16f, hx));
            Rail(p, "RoofRailE", new Vector3(hx + 1.2f, hgt + 0.16f, z - w * 0.5f), new Vector3(hx + 1.2f, hgt + 0.16f, hx));
            // mast + guys
            var mast = new Vector3(hx + 3f, 0, hx + 1f);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Mast", p, mast + new Vector3(0, 10f, 0), Vector3.zero, new Vector3(0.3f, 10f, 0.3f), mSteel, true);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "MastLight", p, mast + new Vector3(0, 20.2f, 0), Vector3.zero, Vector3.one * 0.3f, mRust);
            foreach (var a in new[] { 30f, 150f, 270f })
            {
                var anchor = mast + Quaternion.Euler(0, a, 0) * Vector3.forward * 7f; var top = mast + new Vector3(0, 16f, 0);
                var g = ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Guy", p, (anchor + top) * 0.5f, Vector3.zero, new Vector3(0.03f, Vector3.Distance(anchor, top) * 0.5f, 0.03f), mSteel);
                g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (top - anchor).normalized);
            }
            return Panel(p, new Vector3(0.3f, hgt + 0.16f, 0.4f), 0f, "RadioRelay");
        }

        static void Homestead(Transform p)
        {
            Box(p, "WallS", new Vector3(0, 0.9f, -3f), new Vector3(7f, 1.8f, 0.25f), mCharred);
            Box(p, "WallW", new Vector3(-3.5f, 0.6f, 0), new Vector3(0.25f, 1.2f, 6f), mCharred);
            Box(p, "WallNStub", new Vector3(2f, 0.4f, 3f), new Vector3(3f, 0.8f, 0.25f), mCharred);
            Box(p, "Chimney", new Vector3(3.2f, 2.5f, 2.6f), new Vector3(1f, 5f, 1f), mRock);
            Box(p, "FallenBeam", new Vector3(0.5f, 0.25f, 0.5f), new Vector3(5.5f, 0.25f, 0.25f), mCharred, new Vector3(0, 25, 8));
            Box(p, "Floor", new Vector3(0, 0.02f, 0), new Vector3(7f, 0.08f, 6f), mCharred);
        }

        static void Bridge(Transform p)
        {
            for (int i = -1; i <= 1; i += 2) ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Log", p, new Vector3(i * 0.6f, 0.3f, 0), new Vector3(90, 0, 0), new Vector3(0.35f, 4f, 0.35f), mDark);
            for (int i = 0; i < 12; i++) Box(p, "Plank" + i, new Vector3(0, 0.5f, -3.3f + i * 0.6f), new Vector3(1.8f, 0.07f, 0.5f), mWood);
        }

        static void Quarry(Transform p)
        {
            var r = new System.Random(5);
            for (int i = 0; i < 9; i++)
                Box(p, "Block" + i, new Vector3((float)r.NextDouble() * 12 - 6, 0.5f, (float)r.NextDouble() * 12 - 6), new Vector3(1 + (float)r.NextDouble() * 2, 0.8f + (float)r.NextDouble() * 1.5f, 1 + (float)r.NextDouble() * 2), mRock, new Vector3(0, (float)r.NextDouble() * 90, (float)r.NextDouble() * 10));
            Box(p, "CutFace", new Vector3(0, 2f, 8f), new Vector3(14f, 6f, 2f), mRock);
            Box(p, "SpoilHeap", new Vector3(-7f, 0.3f, -2f), new Vector3(5f, 1.2f, 4f), mRock, new Vector3(0, 30, 6));
        }

        static void Shack(Transform p, string id)
        {
            Box(p, "Floor", new Vector3(0, 0.05f, 0), new Vector3(3f, 0.1f, 3f), mWood);
            Box(p, "WallBack", new Vector3(0, 1.2f, 1.45f), new Vector3(3f, 2.4f, 0.1f), mWood);
            Box(p, "WallL", new Vector3(-1.45f, 1.2f, 0), new Vector3(0.1f, 2.4f, 3f), mWood);
            Box(p, "WallR", new Vector3(1.45f, 1.2f, 0), new Vector3(0.1f, 2.4f, 3f), mWood);
            Box(p, "Roof", new Vector3(0, 2.5f, 0), new Vector3(3.4f, 0.12f, 3.4f), mRoof, new Vector3(6, 0, 0));
            var crate = ASHContentBuilder.LoadPrefab("ASH_SalvageCrate");
            if (crate) { var g = (GameObject)PrefabUtility.InstantiatePrefab(crate, p); g.transform.localPosition = new Vector3(0.6f, 0.1f, 0.7f); }
        }

        // ---------------------------------------------------------------- pickups
        static void PlacePickups(Layout L, Transform root)
        {
            var camp = L.P("redstone_camp").V; var rnd = new System.Random(11);
            // around Redstone Camp (ring 10-45 m, avoids the cabin and Anthony's objects)
            (string item, int n)[] campItems = { ("Hatchet", 1), ("OldShirt", 1), ("Rags", 2), ("CannedBeans", 1), ("EmptyTin", 1), ("DeadRabbit", 1), ("Rags", 1), ("RubbingAlcohol", 1) };
            for (int i = 0; i < campItems.Length; i++) Pick(root, campItems[i].item, campItems[i].n, Ring(camp, 10f + 4f * i, i * 2.4f));
            for (int i = 0; i < 3; i++) Node(root, "ASH_FiberGrass", Ring(camp, 18f + i * 9f, 1.2f + i * 2.1f));
            Node(root, "ASH_SalvageCrate", Ring(camp, 14f, 4.0f)); Node(root, "ASH_BrokenChair", Ring(camp, 16f, 5.3f));
            // survey rewards on the structures
            foreach (var sp in Object.FindObjectsByType<ASHSurveyPoint>(FindObjectsInactive.Include))
            {
                var d = sp.Definition; if (!d) continue;
                var at = sp.transform.position + sp.transform.right * 0.9f + Vector3.up * 0.05f;
                switch (d.order)
                {
                    case 1: Pick(root, "HuntingKnife", 1, at); break;
                    case 2: Pick(root, "Bandage", 1, at); Pick(root, "RubbingAlcohol", 1, at - sp.transform.right * 1.8f); break;
                    case 3: Pick(root, "CannedPeaches", 2, at); Pick(root, "Whiskey", 1, at - sp.transform.right * 1.8f); break;
                    case 4: Pick(root, "CanOpener", 1, at); Pick(root, "Painkillers", 1, at - sp.transform.right * 1.8f); break;
                }
            }
            // along routes
            var R = new Dictionary<string, List<Vector2>>();
            foreach (var r in L.roads) R[r.id] = Smooth(r.pts, 8);
            foreach (var r in L.trails) R[r.id] = Smooth(r.pts, 8);
            Along(root, R["mule_trail"], 0.3f, "Rags", 1, 4f); Along(root, R["mule_trail"], 0.6f, "CannedBeans", 1, -5f); Along(root, R["mule_trail"], 0.85f, "EmptyTin", 1, 3f);
            Along(root, R["county_road_south"], 0.08f, "OldShirt", 1, 5f); Along(root, R["county_road_south"], 0.16f, "ASH_SalvageCrate", 0, 7f);
            Along(root, R["county_road_south"], 0.25f, "CannedPeaches", 1, -4f); Along(root, R["county_road_south"], 0.3f, "ASH_FiberGrass", 0, 6f);
            Along(root, R["descent_route"], 0.25f, "Rags", 1, 3f); Along(root, R["descent_route"], 0.5f, "ASH_FiberGrass", 0, -4f);
            var cc = L.P("creek_crossing").V; Pick(root, "WholeTrout", 1, Ring(cc, 6f, 0.5f)); Pick(root, "WholeTrout", 1, Ring(cc, 9f, 2.5f));
            var hs = L.P("burned_homestead").V; Pick(root, "OldShirt", 1, Ring(hs, 7f, 1f)); Node(root, "ASH_BrokenChair", Ring(hs, 6f, 3f)); Pick(root, "CannedBeans", 1, Ring(hs, 8f, 4.5f));
            foreach (var p in L.pois)
                if (p.kind == "Supply") { Pick(root, "CannedPeaches", 1, Ring(p.V, 0.8f, 3.9f)); Pick(root, "Cloth", 2, Ring(p.V, 0.6f, 1.0f)); Pick(root, rnd.NextDouble() < 0.5 ? "RubbingAlcohol" : "Whiskey", 1, Ring(p.V, 0.9f, 5.5f)); }
        }

        static Vector2 Ring(Vector2 c, float r, float a) => c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;

        static void Along(Transform root, List<Vector2> pts, float f, string item, int n, float lateral)
        {
            float total = 0; for (int i = 1; i < pts.Count; i++) total += Vector2.Distance(pts[i - 1], pts[i]);
            float want = total * f, acc = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                float d = Vector2.Distance(pts[i - 1], pts[i]);
                if (acc + d >= want)
                {
                    var q = Vector2.Lerp(pts[i - 1], pts[i], (want - acc) / Mathf.Max(d, 0.01f)); var dir = (pts[i] - pts[i - 1]).normalized;
                    q += new Vector2(-dir.y, dir.x) * lateral;
                    if (n == 0) Node(root, item, q); else Pick(root, item, n, q);
                    return;
                }
                acc += d;
            }
        }

        static void Pick(Transform root, string item, int n, Vector2 xz) => Pick(root, item, n, FreeSpot(xz));
        static void Pick(Transform root, string item, int n, Vector3 at)
        {
            var prefab = ASHItemCatalog.LoadPickup(item); if (!prefab) { Debug.LogWarning("[ASH] no pickup prefab " + item); return; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root); g.transform.position = at; g.transform.rotation = Quaternion.Euler(0, (at.x * 37f + at.z * 11f) % 360f, 0);
            if (n > 1) { var pu = g.GetComponent<ASHItemPickup>(); pu.Configure(AssetDatabase.LoadAssetAtPath<ASH.Inventory.ASHItemDefinition>($"{ASHItemCatalog.ItemDir}/ASH_Item_{item}.asset"), n); EditorUtility.SetDirty(pu); }
        }
        static void Node(Transform root, string prefabName, Vector2 xz)
        {
            var prefab = ASHContentBuilder.LoadPrefab(prefabName); if (!prefab) return;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root); var at = FreeSpot(xz);
            g.transform.position = at; g.transform.rotation = Quaternion.Euler(0, (at.x * 53f) % 360f, 0);
        }
        static Vector3 FreeSpot(Vector2 xz)
        {
            for (float d = 0; d <= 12f; d += 1.5f)
                for (int i = 0; i < (d == 0 ? 1 : 8); i++)
                {
                    var q = xz + new Vector2(Mathf.Cos(i * 0.785f), Mathf.Sin(i * 0.785f)) * d; var g = Ground(q);
                    var n = _t.terrainData.GetInterpolatedNormal(q.x / _t.terrainData.size.x, q.y / _t.terrainData.size.z);
                    if (Vector3.Angle(n, Vector3.up) > 30f) continue;
                    bool hit = false;
                    foreach (var col in Physics.OverlapSphere(g + Vector3.up * 0.6f, 0.5f)) if (!(col is TerrainCollider)) { hit = true; break; }
                    if (!hit) return g + Vector3.up * 0.02f;
                }
            return Ground(xz) + Vector3.up * 0.02f;
        }

        // ---------------------------------------------------------------- terrain routes
        [MenuItem("ASH/World/Paint Region Roads + Trails")]
        public static void PaintRoutesMenu() { _t = Terrain.activeTerrain; if (_t) PaintRoutes(Load()); }

        /// <summary>
        /// Blends the soil layer along roads (3.5 m) and trails (1.1 m). Splat only: one GetAlphamaps/SetAlphamaps per
        /// route over its bounding box. Never touches heights, trees, tree prototypes or detail layers, and takes
        /// no whole-terrain undo snapshot. Saves only the terrain data asset itself (splat change).
        /// </summary>
        public static int PaintRoutes(Layout L)
        {
            var data = _t.terrainData; int soil = -1;
            var tl = data.terrainLayers;
            for (int i = 0; i < tl.Length; i++) if (tl[i] && tl[i].name == "ASH_TL_Soil") soil = i;
            if (soil < 0) { Debug.LogWarning("[ASH] ASH_TL_Soil layer not on terrain; roads not painted."); return 0; }
            int res = data.alphamapResolution; float cell = data.size.x / res; int painted = 0;
            var routes = new List<(List<Vector2> pts, float half)>();
            foreach (var r in L.roads) routes.Add((Smooth(r.pts, 8), 3.5f));
            foreach (var r in L.trails) routes.Add((Smooth(r.pts, 8), 1.1f));
            foreach (var (pts, half) in routes)
            {
                if (pts.Count < 2) continue;
                float edge = half + 1.5f;
                Vector2 mn = pts[0], mx = pts[0]; foreach (var q in pts) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                int x0 = Mathf.Clamp(Mathf.FloorToInt((mn.x - edge) / cell), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((mx.x + edge) / cell), 0, res - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((mn.y - edge) / cell), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((mx.y + edge) / cell), 0, res - 1);
                int w = x1 - x0 + 1, h = z1 - z0 + 1; if (w <= 0 || h <= 0) continue;
                var al = data.GetAlphamaps(x0, z0, w, h); int layers = al.GetLength(2); bool any = false;
                for (int s = 1; s < pts.Count; s++)
                {
                    Vector2 a = pts[s - 1], b = pts[s];
                    int sx0 = Mathf.Max(x0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - edge) / cell)), sx1 = Mathf.Min(x1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + edge) / cell));
                    int sz0 = Mathf.Max(z0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - edge) / cell)), sz1 = Mathf.Min(z1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + edge) / cell));
                    Vector2 ab = b - a; float ab2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                    for (int zi = sz0; zi <= sz1; zi++)
                        for (int xi = sx0; xi <= sx1; xi++)
                        {
                            var q = new Vector2((xi + 0.5f) * cell, (zi + 0.5f) * cell);
                            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab2);
                            float d = Vector2.Distance(q, a + ab * t);
                            float n = Mathf.PerlinNoise(q.x * 0.15f, q.y * 0.15f) * 0.8f;
                            float wgt = Mathf.Clamp01((edge - d + n) / (edge - half)) * 0.9f;
                            int zz = zi - z0, xx = xi - x0;
                            if (wgt <= 0.01f || al[zz, xx, soil] >= wgt) continue;
                            for (int l = 0; l < layers; l++) al[zz, xx, l] *= (1f - wgt);
                            al[zz, xx, soil] += wgt; any = true; painted++;
                        }
                }
                if (any) data.SetAlphamaps(x0, z0, al);
            }
            if (painted > 0) { EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data); }
            return painted;
        }
    }
}
