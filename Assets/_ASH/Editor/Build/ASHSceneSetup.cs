using System.Collections.Generic;
using ASH.EditorTools.World;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using ASH.Survival;
using ASH.UI;
using ASH.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.EditorTools.Build
{
    /// <summary>
    /// Populates the OPEN prototype scene (Anthony's ASH scene, moved to Assets/_ASH/Scenes/ASH_Prototype.unity)
    /// without touching terrain heights or his objects, except disabling his default Main Camera (the player
    /// brings the gameplay camera). Owns and rebuilds only the roots "ASH_World", "ASH_Systems" and "ASH_Player".
    /// </summary>
    public static class ASHSceneSetup
    {
        public const string PrototypeScene = "Assets/_ASH/Scenes/ASH_Prototype.unity";
        public const string TestScene = "Assets/_ASH/Scenes/Tests/ASH_ControllerTest.unity";
        public const string SkyPath = "Assets/_ASH/Materials/ASH_Sky_Procedural.mat";

        [MenuItem("ASH/Build/3. Set Up Prototype Scene (open scene)")]
        public static void SetupPrototype()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != PrototypeScene) throw new System.Exception("Open " + PrototypeScene + " first (active: " + scene.path + ")");
            var terrain = Terrain.activeTerrain;
            if (!terrain) throw new System.Exception("No terrain in scene.");

            foreach (var n in new[] { "ASH_World", "ASH_Systems" })
            { var old = GameObject.Find(n); if (old) Object.DestroyImmediate(old); }

            // His default camera -> disabled (kept in scene).
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (cam.name == "Main Camera" && cam.transform.parent == null) cam.gameObject.SetActive(false);

            terrain.drawInstanced = true;
            terrain.detailObjectDistance = 140f;
            terrain.detailObjectDensity = 1f;

            // Anthony's placements are authoritative: the player (placed by him at Grandpa Cabin) and the cabin are
            // never moved. Spawn/respawn, resources and the route are derived from them.
            var cabin = FindRoot("Grandpa Cabin");
            Bounds cabinBounds = cabin ? WorldBounds(cabin) : new Bounds(Vector3.zero, Vector3.zero);
            var player = GameObject.Find("ASH_Player");
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ASHPlayerBuilder.PrefabPath);
            if (!playerPrefab) playerPrefab = ASHPlayerBuilder.Build();
            if (!player)
            {
                player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                player.name = "ASH_Player";
                Vector3 start = cabin ? cabinBounds.center + Vector3.forward * (cabinBounds.extents.z + 3f) : ASHWorldPoints.Ground(terrain, ASHWorldPoints.Spawn);
                start.y = terrain.SampleHeight(start) + terrain.transform.position.y + 0.1f;
                player.transform.position = start;
                Debug.LogWarning("[ASH] ASH_Player was missing; placed beside the cabin.");
            }

            else if (cabin && Vector2.Distance(new Vector2(player.transform.position.x, player.transform.position.z), ASHWorldPoints.Spawn) < 3f)
            {
                // An earlier ASH rebuild reset the player to the old default spawn; restore the cabin start.
                var fwd = cabin.transform.forward; fwd.y = 0; fwd.Normalize();
                float ext = Mathf.Max(cabinBounds.extents.x, cabinBounds.extents.z);
                Vector3 start = cabinBounds.center + fwd * (ext + 2.5f);
                start.y = terrain.SampleHeight(start) + terrain.transform.position.y + 0.05f;
                player.transform.SetPositionAndRotation(start, Quaternion.LookRotation(fwd));
                Debug.LogWarning($"[ASH] Player was at the old default spawn; restored beside Grandpa Cabin at {start}. Nudge it in the scene if the door is elsewhere.");
            }

            var world = new GameObject("ASH_World").transform;
            var anchors = new GameObject("Anchors").transform; anchors.SetParent(world);
            Vector3 spawn = player.transform.position;
            Vector2 S = new Vector2(spawn.x, spawn.z);
            Vector2 overlookXZ = ChooseOverlook(terrain, S, out string why);
            Vector2 campXZ = ChooseCampsite(terrain, overlookXZ, S);
            Vector3 overlookP = ASHWorldPoints.Ground(terrain, overlookXZ);
            Vector3 campP = ASHWorldPoints.Ground(terrain, campXZ);
            var spawnT = Anchor(anchors, "ASH_PlayerSpawn", spawn, player.transform.rotation);
            var overlookT = Anchor(anchors, "ASH_Objective_RidgeOverlook", overlookP, Quaternion.identity);
            var campT = Anchor(anchors, "ASH_Objective_OldCampsite", campP, Quaternion.identity);
            Debug.Log($"[ASH] Start (player at cabin) {spawn}; overlook {overlookP} ({Vector2.Distance(S, overlookXZ):0} m, climb {overlookP.y - spawn.y:0} m, {why}); campsite {campP}");

            Dress(terrain, world, spawn, overlookP, campP, cabin ? cabinBounds : (Bounds?)null);
            ASHWorldPoints.CampsiteOverride = campXZ;
            ASHTerrainPainter.Paint(terrain.terrainData);
            PaintGrass(terrain, spawn, overlookP, cabin ? cabinBounds : (Bounds?)null);

            ASHBuildUtil.Set(player.GetComponent<ASHPlayerDeath>(), "respawnPoint", spawnT);

            // Systems
            var systems = new GameObject("ASH_Systems").transform;
            var sun = RenderSettings.sun ? RenderSettings.sun : Object.FindAnyObjectByType<Light>();
            var clockGo = new GameObject("DayNightClock"); clockGo.transform.SetParent(systems);
            var clock = clockGo.AddComponent<ASHDayNightClock>();
            ASHBuildUtil.Set(clock, "sun", sun);
            clock.timeOfDay = 17.2f; clock.ashHaze = 0.45f; clock.Apply();
            if (sun) { sun.shadows = LightShadows.Soft; RenderSettings.sun = sun; }

            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (!sky) { ASHBuildUtil.Folder("Assets/_ASH/Materials"); sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, SkyPath); }
            sky.SetFloat("_SunSize", 0.035f); sky.SetFloat("_AtmosphereThickness", 1.9f); sky.SetColor("_SkyTint", new Color(0.58f, 0.5f, 0.45f)); sky.SetColor("_GroundColor", new Color(0.33f, 0.3f, 0.27f)); sky.SetFloat("_Exposure", 1.1f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.0016f;

            var ashFall = (GameObject)PrefabUtility.InstantiatePrefab(ASHContentBuilder.LoadPrefab("ASH_AshFall"));
            ashFall.transform.SetParent(systems); ashFall.transform.position = spawn + Vector3.up * 16f;

            var objGo = new GameObject("Objective"); objGo.transform.SetParent(systems);
            var obj = objGo.AddComponent<ASHObjectiveTracker>();
            var inv = player.GetComponent<ASHInventory>();
            ASHBuildUtil.Set(obj, "inventory", inv);
            ASHBuildUtil.Set(obj, "crafting", player.GetComponent<ASHCrafting>());
            ASHBuildUtil.Set(obj, "placer", player.GetComponent<ASHPlacer>());
            ASHBuildUtil.Set(obj, "player", player.transform);
            ASHBuildUtil.Set(obj, "campfireRecipe", ASHContentBuilder.LoadRecipe());
            ASHBuildUtil.Set(obj, "overlook", overlookT);
            ASHBuildUtil.Set(obj, "campsite", campT);
            var so = new SerializedObject(obj);
            var goals = so.FindProperty("gatherGoals");
            var defs = new (string file, int req)[] { ("ASH_Item_Deadwood", 4), ("ASH_Item_Stone", 3), ("ASH_Item_Chokecherries", 2) };
            goals.arraySize = defs.Length;
            for (int i = 0; i < defs.Length; i++)
            {
                var e = goals.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("item").objectReferenceValue = ASHContentBuilder.LoadItem(defs[i].file);
                e.FindPropertyRelative("required").intValue = defs[i].req;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            AddHud(systems, player, obj, clock);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ASH] Prototype scene set up. spawn={spawn} overlook={overlookP} campsite={campP}");
        }

        static void AddHud(Transform parent, GameObject player, ASHObjectiveTracker obj, ASHDayNightClock clock)
        {
            var hudGo = new GameObject("HUD"); hudGo.transform.SetParent(parent);
            var hud = hudGo.AddComponent<ASHHud>();
            ASHBuildUtil.Set(hud, "input", player.GetComponent<ASHInput>());
            ASHBuildUtil.Set(hud, "stats", player.GetComponent<ASHSurvivalStats>());
            ASHBuildUtil.Set(hud, "inventory", player.GetComponent<ASHInventory>());
            ASHBuildUtil.Set(hud, "crafting", player.GetComponent<ASHCrafting>());
            ASHBuildUtil.Set(hud, "interactor", player.GetComponent<ASHInteractor>());
            ASHBuildUtil.Set(hud, "placer", player.GetComponent<ASHPlacer>());
            if (obj) ASHBuildUtil.Set(hud, "objective", obj);
            if (clock) ASHBuildUtil.Set(hud, "clock", clock);
        }

        static Transform Anchor(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            var t = new GameObject(name).transform; t.SetParent(parent); t.SetPositionAndRotation(pos, rot); return t;
        }

        // ------------------------------------------------------------------ start / route helpers
        static GameObject FindRoot(string name)
        {
            foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects())
                if (string.Equals(r.name.Trim(), name, System.StringComparison.OrdinalIgnoreCase)) return r;
            foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects())
                if (r.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0) return r;
            return null;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 8f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        /// <summary>
        /// Picks a ridge overlook 350-900 m from the start: a local high point with clear prominence, a 40-180 m climb
        /// and a walkable straight-line route (max sampled slope). Falls back to the authored point.
        /// </summary>
        static Vector2 ChooseOverlook(Terrain t, Vector2 s, out string why)
        {
            var size = t.terrainData.size;
            float hs = t.SampleHeight(new Vector3(s.x, 0, s.y));
            float best = float.NegativeInfinity; Vector2 bestP = ASHWorldPoints.Overlook; why = "authored fallback";
            for (float z = s.y - 900f; z <= s.y + 900f; z += 16f)
                for (float x = s.x - 900f; x <= s.x + 900f; x += 16f)
                {
                    if (x < 60 || z < 60 || x > size.x - 60 || z > size.z - 60) continue;
                    var p = new Vector2(x, z); float d = Vector2.Distance(p, s);
                    if (d < 350f || d > 900f) continue;
                    float h = t.SampleHeight(new Vector3(x, 0, z));
                    float climb = h - hs; if (climb < 40f || climb > 180f) continue;
                    float ring = 0; for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; ring += t.SampleHeight(new Vector3(x + Mathf.Cos(a) * 80f, 0, z + Mathf.Sin(a) * 80f)); }
                    float prominence = h - ring / 8f; if (prominence < 8f) continue;
                    float maxSlope = 0; for (int i = 1; i < 30; i++) { var q = Vector2.Lerp(s, p, i / 30f); maxSlope = Mathf.Max(maxSlope, t.terrainData.GetSteepness(q.x / size.x, q.y / size.z)); }
                    if (maxSlope > 34f) continue;
                    float score = prominence * 1.5f + Mathf.Min(climb, 120f) * 0.5f - Mathf.Abs(d - 600f) * 0.03f - maxSlope * 0.6f;
                    if (score > best) { best = score; bestP = p; why = $"prominence {prominence:0} m, route max slope {maxSlope:0} deg"; }
                }
            return bestP;
        }

        static Vector2 ChooseCampsite(Terrain t, Vector2 overlook, Vector2 start)
        {
            var size = t.terrainData.size; float best = float.PositiveInfinity; Vector2 bestP = overlook + (start - overlook).normalized * 16f;
            for (float dz = -40; dz <= 40; dz += 3)
                for (float dx = -40; dx <= 40; dx += 3)
                {
                    var p = overlook + new Vector2(dx, dz); float d = Vector2.Distance(p, overlook);
                    if (d < 12f || d > 40f) continue;
                    float slope = 0; for (int i = 0; i < 9; i++) { var q = p + new Vector2((i % 3 - 1) * 3f, (i / 3 - 1) * 3f); slope = Mathf.Max(slope, t.terrainData.GetSteepness(q.x / size.x, q.y / size.z)); }
                    float facing = Vector2.Dot((p - overlook).normalized, (start - overlook).normalized); // prefer the start-facing side
                    float score = slope - facing * 3f;
                    if (score < best) { best = score; bestP = p; }
                }
            return bestP;
        }

        static Bounds? _exclude;

        // ------------------------------------------------------------------ dressing
        static void Dress(Terrain terrain, Transform world, Vector3 spawn, Vector3 overlook, Vector3 camp, Bounds? cabin)
        {
            _exclude = cabin.HasValue ? new Bounds(cabin.Value.center, cabin.Value.size + new Vector3(6f, 40f, 6f)) : (Bounds?)null;
            var rnd = new System.Random(20261114);
            var env = new GameObject("Environment").transform; env.SetParent(world);
            var deadwood = ASHContentBuilder.LoadPrefab("ASH_Deadwood");
            var stones = ASHContentBuilder.LoadPrefab("ASH_StonePile");
            var bush = ASHContentBuilder.LoadPrefab("ASH_ChokecherryBush");
            var tree = ASHContentBuilder.LoadPrefab("ASH_DeadTree");
            var sage = ASHContentBuilder.LoadPrefab("ASH_Sagebrush");
            var tank = ASHContentBuilder.LoadPrefab("ASH_StockTank");
            var campsite = ASHContentBuilder.LoadPrefab("ASH_OldCampsite");
            var bigRock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Procedural Worlds/Packages - Install/Asset Samples/Procedural Worlds/Prefabs/PW_Stone_01.prefab");

            Vector2 S = new Vector2(spawn.x, spawn.z);
            var creek = ASHTerrainPainter.CreekPolyline;
            bool creekNear = ASHTerrainPainter.DistanceToPolyline(S, creek) < 150f;

            // Guaranteed starter resources around the cabin start (objective must be finishable fast).
            Ring(terrain, env, deadwood, rnd, S, 8f, 32f, 6, true, 22f);
            Ring(terrain, env, stones, rnd, S, 8f, 32f, 4, true, 24f);
            Ring(terrain, env, bush, rnd, S, 10f, 35f, 3, true, 22f);
            if (creekNear)
            {
                for (int i = 0; i < 5; i++) PlaceOnLine(terrain, env, stones, rnd, creek, 1.5f, 18f, near: S, maxFromNear: 160f);
                for (int i = 0; i < 4; i++) PlaceOnLine(terrain, env, bush, rnd, creek, 5f, 14f, near: S, maxFromNear: 160f);
            }
            else { Ring(terrain, env, stones, rnd, S, 30f, 90f, 4, true, 26f); Ring(terrain, env, bush, rnd, S, 30f, 90f, 3, true, 24f); }

            // Wider start area
            Ring(terrain, env, deadwood, rnd, S, 30f, 140f, 10, true, 24f);
            Ring(terrain, env, tree, rnd, S, 25f, 260f, 18, false, 20f, preferHollow: true);
            Ring(terrain, env, sage, rnd, S, 6f, 220f, 160, true, 26f, scaleMin: 0.7f, scaleMax: 1.6f);
            if (bigRock) Ring(terrain, env, bigRock, rnd, S, 18f, 320f, 45, false, 40f, scaleMin: 0.8f, scaleMax: 3.2f, minSlope: 8f);

            // Route breadcrumbs toward the ridge
            Vector2 O = new Vector2(overlook.x, overlook.z);
            int crumbs = Mathf.Clamp(Mathf.RoundToInt(Vector2.Distance(S, O) / 50f), 6, 18);
            for (int i = 1; i <= crumbs; i++)
            {
                Vector2 p = Vector2.Lerp(S, O, i / (crumbs + 1f)) + new Vector2((float)rnd.NextDouble() * 40f - 20f, (float)rnd.NextDouble() * 40f - 20f);
                Place(terrain, env, i % 3 == 0 ? tree : sage, rnd, p, false, 30f, 0.8f, 1.4f);
                if (i % 2 == 0) Place(terrain, env, deadwood, rnd, p + new Vector2(6, -4), true, 25f, 1f, 1f);
                if (bigRock && i % 4 == 1) Place(terrain, env, bigRock, rnd, p + new Vector2(-8, 5), false, 40f, 1.2f, 2.6f);
            }
            Ring(terrain, env, sage, rnd, O, 10f, 120f, 50, true, 28f, scaleMin: 0.6f, scaleMax: 1.3f);
            Ring(terrain, env, tree, rnd, O, 25f, 100f, 6, false, 22f);

            // Stock tank (water) within a short walk of the start, on flat ground
            var tankGo = (GameObject)PrefabUtility.InstantiatePrefab(tank, env);
            Vector2 tp2 = S + new Vector2(30f, 0f);
            for (int k = 0; k < 60; k++)
            {
                float a = k * 2.4f; var cand = S + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (35f + k * 0.8f);
                if (Slope(terrain, cand) < 6f && !Excluded(terrain, cand)) { tp2 = cand; break; }
            }
            var tp = ASHWorldPoints.Ground(terrain, tp2);
            tankGo.transform.SetPositionAndRotation(tp - Vector3.up * 0.05f, Quaternion.Euler(0, 20, 0));

            // Old campsite on the ridge shoulder, facing the start
            var campGo = (GameObject)PrefabUtility.InstantiatePrefab(campsite, world);
            var face = spawn - camp; face.y = 0;
            campGo.transform.SetPositionAndRotation(camp - Vector3.up * 0.03f, Quaternion.LookRotation(face.normalized));
            _exclude = null;
        }

        static bool Excluded(Terrain t, Vector2 p)
        {
            if (!_exclude.HasValue) return false;
            var b = _exclude.Value; return p.x > b.min.x && p.x < b.max.x && p.y > b.min.z && p.y < b.max.z;
        }

        static void Ring(Terrain t, Transform parent, GameObject prefab, System.Random rnd, Vector2 c, float rMin, float rMax, int count, bool alignToNormal, float maxSlope, float scaleMin = 0.9f, float scaleMax = 1.15f, float minSlope = 0f, bool preferHollow = false)
        {
            if (!prefab) return;
            int placed = 0, tries = 0;
            while (placed < count && tries++ < count * 30)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Mathf.Lerp(rMin * rMin, rMax * rMax, (float)rnd.NextDouble()));
                Vector2 p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                float slope = Slope(t, p);
                if (slope > maxSlope || slope < minSlope) continue;
                if (preferHollow && rnd.NextDouble() < 0.5 && Relief(t, p) > -0.5f) continue;
                if (Place(t, parent, prefab, rnd, p, alignToNormal, maxSlope, scaleMin, scaleMax)) placed++;
            }
        }

        static void PlaceOnLine(Terrain t, Transform parent, GameObject prefab, System.Random rnd, Vector2[] line, float offsetMin, float offsetMax, Vector2? near = null, float maxFromNear = 1e9f)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                int i = rnd.Next(line.Length - 1);
                Vector2 p = Vector2.Lerp(line[i], line[i + 1], (float)rnd.NextDouble());
                Vector2 dir = (line[i + 1] - line[i]).normalized; Vector2 side = new Vector2(-dir.y, dir.x) * (rnd.NextDouble() < 0.5 ? -1 : 1);
                p += side * Mathf.Lerp(offsetMin, offsetMax, (float)rnd.NextDouble());
                if (near.HasValue && Vector2.Distance(p, near.Value) > maxFromNear) continue;
                if (Place(t, parent, prefab, rnd, p, true, 28f, 0.9f, 1.15f)) return;
            }
        }

        static bool Place(Terrain t, Transform parent, GameObject prefab, System.Random rnd, Vector2 p, bool align, float maxSlope, float sMin, float sMax)
        {
            if (!prefab) return false;
            if (Excluded(t, p)) return false;
            var size = t.terrainData.size;
            if (p.x < 5 || p.y < 5 || p.x > size.x - 5 || p.y > size.z - 5) return false;
            if (Slope(t, p) > maxSlope) return false;
            var pos = ASHWorldPoints.Ground(t, p);
            var n = t.terrainData.GetInterpolatedNormal(p.x / size.x, p.y / size.z);
            var rot = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
            if (align) rot = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, n, 0.8f)) * rot;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos - Vector3.up * 0.04f, rot);
            go.transform.localScale *= Mathf.Lerp(sMin, sMax, (float)rnd.NextDouble());
            return true;
        }

        static float Slope(Terrain t, Vector2 p) { var s = t.terrainData.size; return t.terrainData.GetSteepness(p.x / s.x, p.y / s.z); }
        static float Relief(Terrain t, Vector2 p)
        {
            float h = t.SampleHeight(new Vector3(p.x, 0, p.y)); float ring = 0;
            for (int i = 0; i < 4; i++) { float a = i * Mathf.PI / 2; ring += t.SampleHeight(new Vector3(p.x + Mathf.Cos(a) * 30, 0, p.y + Mathf.Sin(a) * 30)); }
            return h - ring / 4f;
        }

        // ------------------------------------------------------------------ grass
        public static void PaintGrass(Terrain terrain, Vector3 spawn, Vector3 overlook, Bounds? keepClear = null)
        {
            var data = terrain.terrainData;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Procedural Worlds/Packages - Install/Asset Samples/Procedural Worlds/Content Resources/Terrain Details/PW_WildGrass_00_D Sample.png");
            if (!tex) { Debug.LogWarning("[ASH] grass texture not found; skipping details"); return; }
            var protos = new List<DetailPrototype>();
            protos.Add(new DetailPrototype { prototypeTexture = tex, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false, minWidth = 0.6f, maxWidth = 1.1f, minHeight = 0.45f, maxHeight = 0.95f, healthyColor = new Color(0.78f, 0.72f, 0.48f), dryColor = new Color(0.66f, 0.55f, 0.36f), noiseSpread = 0.3f, useInstancing = false });
            protos.Add(new DetailPrototype { prototypeTexture = tex, renderMode = DetailRenderMode.GrassBillboard, usePrototypeMesh = false, minWidth = 0.5f, maxWidth = 0.9f, minHeight = 0.3f, maxHeight = 0.6f, healthyColor = new Color(0.55f, 0.55f, 0.38f), dryColor = new Color(0.5f, 0.45f, 0.32f), noiseSpread = 0.5f, useInstancing = false });
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.detailPrototypes = protos.ToArray();
            int res = data.detailResolution;
            float texel = data.size.x / res;
            var l0 = new int[res, res]; var l1 = new int[res, res];
            Vector2 S = new Vector2(spawn.x, spawn.z), O = new Vector2(overlook.x, overlook.z);
            var alpha = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            int aRes = data.alphamapWidth;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) * texel, (y + 0.5f) * texel);
                    float d = Mathf.Min(Vector2.Distance(p, S), DistSeg(p, S, O), Vector2.Distance(p, O));
                    if (d > 450f) continue;
                    if (keepClear.HasValue) { var kb = keepClear.Value; if (p.x > kb.min.x - 1f && p.x < kb.max.x + 1f && p.y > kb.min.z - 1f && p.y < kb.max.z + 1f) continue; }
                    int ax = Mathf.Clamp((int)(p.x / data.size.x * aRes), 0, aRes - 1), ay = Mathf.Clamp((int)(p.y / data.size.z * aRes), 0, aRes - 1);
                    float grassW = alpha[ay, ax, 0] + alpha[ay, ax, 1] * 0.8f;
                    float ashW = alpha.GetLength(2) > 5 ? alpha[ay, ax, 5] : 0f;
                    float w = grassW * (1f - ashW) * Mathf.Clamp01((450f - d) / 80f);
                    float n = Mathf.PerlinNoise(p.x * 0.05f, p.y * 0.05f);
                    l0[y, x] = Mathf.RoundToInt(w * (6f + 10f * n));
                    l1[y, x] = Mathf.RoundToInt(w * 8f * (1f - n));
                }
            data.SetDetailLayer(0, 0, 0, l0);
            data.SetDetailLayer(0, 0, 1, l1);
            data.wavingGrassAmount = 0.35f; data.wavingGrassSpeed = 0.4f; data.wavingGrassStrength = 0.4f; data.wavingGrassTint = new Color(0.8f, 0.75f, 0.6f);
            EditorUtility.SetDirty(data);
        }

        static float DistSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------ test scene
        [MenuItem("ASH/Build/4. Controller Test Scene")]
        public static void BuildControllerTestScene()
        {
            if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var cam in Object.FindObjectsByType<Camera>()) Object.DestroyImmediate(cam.gameObject);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Ground"; ground.transform.localScale = new Vector3(30, 1, 30);
            ground.GetComponent<Renderer>().sharedMaterial = ASHBuildUtil.Lit("Assets/_ASH/Materials/ASH_M_TestGround.mat", new Color(0.45f, 0.42f, 0.36f), 0.1f);
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.name = "Ramp20"; ramp.transform.position = new Vector3(0, 1.4f, 18); ramp.transform.eulerAngles = new Vector3(-20, 0, 0); ramp.transform.localScale = new Vector3(6, 0.4f, 9);
            for (int i = 0; i < 4; i++) { var s = GameObject.CreatePrimitive(PrimitiveType.Cube); s.name = "Step" + i; s.transform.position = new Vector3(8, 0.1f + i * 0.2f, 6 + i * 0.6f); s.transform.localScale = new Vector3(3, 0.2f + i * 0.4f, 0.6f); }
            var low = GameObject.CreatePrimitive(PrimitiveType.Cube); low.name = "CrouchBar"; low.transform.position = new Vector3(-8, 1.35f, 8); low.transform.localScale = new Vector3(3, 0.3f, 3);
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ASHPlayerBuilder.PrefabPath);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab); player.name = "ASH_Player"; player.transform.position = Vector3.zero;
            Put("ASH_Deadwood", new Vector3(3, 0, 4)); Put("ASH_StonePile", new Vector3(-3, 0, 4)); Put("ASH_ChokecherryBush", new Vector3(0, 0, 7)); Put("ASH_StockTank", new Vector3(6, 0, -5));
            var hudRoot = new GameObject("ASH_Systems").transform;
            AddHud(hudRoot, player, null, null);
            RenderSettings.fog = false;
            ASHBuildUtil.Folder("Assets/_ASH/Scenes/Tests");
            EditorSceneManager.SaveScene(scene, TestScene);
            Debug.Log("[ASH] Controller test scene saved: " + TestScene);
        }

        static void Put(string prefab, Vector3 pos)
        {
            var p = ASHContentBuilder.LoadPrefab(prefab); if (!p) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p); go.transform.position = pos;
        }
    }
}
