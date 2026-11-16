using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.Interaction;
using ASH.Inventory;
using ASH.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASH.EditorTools.Systems
{
    /// <summary>
    /// Idempotent scatter of stone, berries, wood, kindling, food pickups and openable supply crates around POI_* objects.
    /// Scene is marked dirty, never saved. Menus: ASH/Systems/Resources/Scatter Around POIs and Clear Scattered.
    /// </summary>
    public static class ASHPoiResourceInstaller
    {
        public const string SettingsPath = "Assets/_ASH/Data/World/ASH_PoiResourceSettings.asset";
        public const string CratePrefabPath = "Assets/_ASH/Prefabs/World/ASH_SupplyCrate.prefab";
        public const string ScatterRoot = "ASH_ScatteredResources";
        const string SalvagePath = "Assets/_ASH/Prefabs/World/ASH_SalvageCrate.prefab";
        const string WoodMat = "Assets/_ASH/Materials/ASH_M_CrateWood.mat";

        [MenuItem("ASH/Systems/Resources/Scatter Around POIs", false, 300)]
        public static void ScatterMenu() => Debug.Log(Scatter());

        [MenuItem("ASH/Systems/Resources/Clear Scattered", false, 301)]
        public static void ClearMenu() => Debug.Log(Clear());

        [MenuItem("ASH/Systems/Resources/Select POI Resource Settings", false, 305)]
        public static void SelectSettings()
        {
            var s = Settings(); Selection.activeObject = s; EditorGUIUtility.PingObject(s);
        }

        public static string Clear()
        {
            var root = FindRoot();
            if (!root) return "[ASH] POI scatter: nothing to clear.";
            int n = root.GetComponentsInChildren<Transform>(true).Length - 1;
            Undo.DestroyObjectImmediate(root);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return $"[ASH] POI scatter: cleared {n} objects under {ScatterRoot}. Scene not saved.";
        }

        public static ASHPoiResourceSettings Settings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHPoiResourceSettings>(SettingsPath);
            if (!s)
            {
                s = ScriptableObject.CreateInstance<ASHPoiResourceSettings>();
                EnsureFolder("Assets/_ASH/Data/World");
                AssetDatabase.CreateAsset(s, SettingsPath);
            }
            bool dirty = BindPrefabs(s) | BindLoot(s);
            if (dirty) { EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s); }
            return s;
        }

        public static string Scatter()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var terrain = Terrain.activeTerrain;
            if (!terrain) return "[ASH] POI scatter: no terrain.";
            var s = Settings();
            var pois = FindPois();
            if (pois.Count == 0) return "[ASH] POI scatter: no POI_* objects in the scene.";

            // idempotent: replace the previous scatter
            var existing = FindRoot();
            if (existing) Undo.DestroyObjectImmediate(existing);
            var root = new GameObject(ScatterRoot);
            Undo.RegisterCreatedObjectUndo(root, "POI resource scatter");

            var d = terrain.terrainData; var origin = terrain.GetPosition(); var size = d.size;
            var trees = BuildTrees(d, origin, size);
            var placed = new Dictionary<long, List<Vector2>>();
            const float SB = 4f;
            long Key(int x, int z) => ((long)z << 32) ^ (uint)x;
            void Mark(Vector2 p) { var k = Key(Mathf.FloorToInt(p.x / SB), Mathf.FloorToInt(p.y / SB)); if (!placed.TryGetValue(k, out var l)) placed[k] = l = new List<Vector2>(); l.Add(p); }
            bool Free(Vector2 p, float r)
            {
                int bx = Mathf.FloorToInt(p.x / SB), bz = Mathf.FloorToInt(p.y / SB), rr = Mathf.CeilToInt(r / SB);
                for (int z = bz - rr; z <= bz + rr; z++) for (int x = bx - rr; x <= bx + rr; x++)
                    if (placed.TryGetValue(Key(x, z), out var l)) foreach (var q in l) if ((q - p).sqrMagnitude < r * r) return false;
                return true;
            }
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb is IASHUsable && mb && mb.gameObject != root) Mark(new Vector2(mb.transform.position.x, mb.transform.position.z));

            var rnd = new System.Random(s.seed != 0 ? s.seed : System.Environment.TickCount);
            var missing = new List<string>();
            var report = new StringBuilder();
            int total = 0;

            foreach (var poi in pois)
            {
                var parent = new GameObject(poi.name);
                parent.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(parent, "POI resource scatter");
                var mul = Multipliers(poi.name);
                var counts = new[]
                {
                    Scale(s.stonePerPoi, mul[0]), Scale(s.berriesPerPoi, mul[1]), Scale(s.woodPerPoi, mul[2]),
                    Scale(s.kindlingPerPoi, mul[3]), Scale(s.foodPerPoi, mul[4]), Scale(s.cratesPerPoi, mul[5])
                };
                var labels = new[] { "stone", "berries", "wood", "kindling", "food", "crates" };
                var got = new int[6];
                var center = new Vector2(poi.transform.position.x, poi.transform.position.z);
                for (int kind = 0; kind < 6; kind++)
                {
                    int want = counts[kind];
                    int tries = 0, maxTries = Mathf.Max(want, 1) * Mathf.Max(8, s.attemptsPerItem);
                    while (got[kind] < want && tries++ < maxTries)
                    {
                        if (!TrySample(rnd, terrain, origin, d, size, s, center, trees, Free, out var pos, out var hit)) continue;
                        var prefab = PickPrefab(s, kind, rnd, missing);
                        GameObject go;
                        if (prefab)
                        {
                            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        }
                        else
                        {
                            go = Placeholder(kind, labels[kind]);
                            if (!missing.Contains(labels[kind] + " prefab")) missing.Add(labels[kind] + " prefab");
                        }
                        go.name = $"{labels[kind]}_{got[kind] + 1}";
                        go.transform.SetParent(parent.transform, true);
                        var yaw = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                        bool align = kind == 2; // wood / branches follow the ground a little
                        var rot = align ? Quaternion.FromToRotation(Vector3.up, hit.normal) * yaw : yaw;
                        go.transform.SetPositionAndRotation(pos, rot);
                        if (kind == 5)
                        {
                            var crate = go.GetComponent<ASHSupplyCrate>(); if (!crate) crate = go.AddComponent<ASHSupplyCrate>();
                            crate.Configure(s);
                        }
                        Undo.RegisterCreatedObjectUndo(go, "POI resource scatter");
                        Mark(new Vector2(pos.x, pos.z));
                        got[kind]++; total++;
                    }
                }
                report.Append($"  {poi.name}: stone {got[0]}/{counts[0]}, berries {got[1]}/{counts[1]}, wood {got[2]}/{counts[2]}, kindling {got[3]}/{counts[3]}, food {got[4]}/{counts[4]}, crates {got[5]}/{counts[5]}\n");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            var sb = new StringBuilder();
            sb.Append($"[ASH] POI scatter: {pois.Count} POIs, placed {total} under {ScatterRoot}. Scene not saved.\n");
            sb.Append(report);
            if (missing.Count > 0) sb.Append("  placeholders used for: ").Append(string.Join(", ", missing.Distinct())).Append('\n');
            return sb.ToString().TrimEnd();
        }

        static int Scale(int n, float m) => Mathf.Max(0, Mathf.RoundToInt(n * m));

        // stone, berries, wood, kindling, food, crates
        static float[] Multipliers(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            float st = 1f, be = 1f, wo = 1f, ki = 1f, fo = 1f, cr = 1f;
            if (n.Contains("quarry")) { st = 3f; wo = 0.6f; be = 0.5f; }
            else if (n.Contains("cache")) { cr = 2f; fo = 2f; st = 0.5f; be = 0.5f; ki = 1.2f; }
            else if (n.Contains("camp") || n.Contains("ranger") || n.Contains("homestead")) { cr = 1.5f; fo = 1.5f; wo = 1.2f; ki = 1.2f; }
            else if (n.Contains("lookout") || n.Contains("overlook") || n.Contains("ruin")) { fo = 1.3f; cr = 1.2f; wo = 0.8f; }
            else if (n.Contains("creek")) { be = 2f; wo = 1.2f; st = 0.6f; cr = 1f; }
            else if (n.Contains("hollow") || n.Contains("coyote") || n.Contains("prairie") || n.Contains("flats")) { be = 1.3f; wo = 1.3f; ki = 1.2f; }
            return new[] { st, be, wo, ki, fo, cr };
        }

        static List<GameObject> FindPois()
        {
            var list = new List<GameObject>();
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid()) return list;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("POI_") && t.parent != null && t.parent.name == ScatterRoot) continue;
                    if (t.name.StartsWith("POI_")) list.Add(t.gameObject);
                }
            }
            return list.OrderBy(g => g.name).ToList();
        }

        static GameObject FindRoot()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;
            return scene.GetRootGameObjects().FirstOrDefault(r => r.name == ScatterRoot);
        }

        static Dictionary<long, List<Vector2>> BuildTrees(TerrainData d, Vector3 origin, Vector3 size)
        {
            var trees = new Dictionary<long, List<Vector2>>(); const float TB = 8f;
            long Key(int x, int z) => ((long)z << 32) ^ (uint)x;
            foreach (var ti in d.treeInstances)
            {
                var w = new Vector2(origin.x + ti.position.x * size.x, origin.z + ti.position.z * size.z);
                var k = Key(Mathf.FloorToInt(w.x / TB), Mathf.FloorToInt(w.y / TB));
                if (!trees.TryGetValue(k, out var l)) trees[k] = l = new List<Vector2>();
                l.Add(w);
            }
            return trees;
        }

        static float TreeDist(Vector2 p, Dictionary<long, List<Vector2>> trees, float max)
        {
            const float TB = 8f;
            long Key(int x, int z) => ((long)z << 32) ^ (uint)x;
            float best = 1e9f; int r = Mathf.CeilToInt(max / TB), bx = Mathf.FloorToInt(p.x / TB), bz = Mathf.FloorToInt(p.y / TB);
            for (int z = bz - r; z <= bz + r; z++) for (int x = bx - r; x <= bx + r; x++)
                if (trees.TryGetValue(Key(x, z), out var l)) foreach (var t in l) { float dd = (t - p).magnitude; if (dd < best) best = dd; }
            return best;
        }

        static bool TrySample(System.Random rnd, Terrain terrain, Vector3 origin, TerrainData d, Vector3 size, ASHPoiResourceSettings s, Vector2 center, Dictionary<long, List<Vector2>> trees, System.Func<Vector2, float, bool> free, out Vector3 pos, out RaycastHit hit)
        {
            pos = default; hit = default;
            float keep = Mathf.Min(s.poiKeepout, s.radius * 0.85f);
            float t = (float)rnd.NextDouble();
            float rad = Mathf.Sqrt(keep * keep + t * (s.radius * s.radius - keep * keep));
            float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
            var p = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
            if (TreeDist(p, trees, s.minTreeDistance + 0.1f) < s.minTreeDistance) return false;
            if (!free(p, s.minSpacing)) return false;

            var from = new Vector3(p.x, origin.y + d.size.y + 50f, p.y);
            if (!Physics.Raycast(from, Vector3.down, out hit, d.size.y + 120f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (IsWater(hit)) return false;
            if (Vector3.Angle(hit.normal, Vector3.up) > s.maxSlope) return false;
            // keep off buildings / non-terrain colliders (the ground hit should be the terrain, or a thin floor)
            if (hit.collider && !(hit.collider is TerrainCollider) && !hit.collider.GetComponentInParent<Terrain>())
            {
                // allow already-scattered resources only after clear; reject solid scene props
                var root = hit.collider.transform.root;
                if (!root || root.name != ScatterRoot) return false;
            }
            if (!free(p, s.minObjectDistance)) return false;
            pos = hit.point;
            return true;
        }

        static bool IsWater(RaycastHit hit)
        {
            if (!hit.collider) return false;
            if (hit.collider.gameObject.layer == 4) return true;
            
            var n = hit.collider.gameObject.name + " " + (hit.collider.transform.root ? hit.collider.transform.root.name : "");
            n = n.ToLowerInvariant();
            if (n.Contains("water") || n.Contains("river") || n.Contains("lake") || n.Contains("pond") || n.Contains("stream")) return true;
            var r = hit.collider.GetComponent<Renderer>();
            if (r && r.sharedMaterial && r.sharedMaterial.name.ToLowerInvariant().Contains("water")) return true;
            return false;
        }

        static GameObject PickPrefab(ASHPoiResourceSettings s, int kind, System.Random rnd, List<string> missing)
        {
            GameObject Pick(GameObject[] arr)
            {
                if (arr == null || arr.Length == 0) return null;
                var ok = arr.Where(x => x).ToArray();
                return ok.Length == 0 ? null : ok[rnd.Next(ok.Length)];
            }
            switch (kind)
            {
                case 0: return s.stonePrefab;
                case 1: return s.berryPrefab;
                case 2: return Pick(s.woodPrefabs);
                case 3: return s.kindlingPrefab;
                case 4: return Pick(s.foodPrefabs);
                default: return s.cratePrefab ? s.cratePrefab : EnsureCratePrefab();
            }
        }

        static GameObject Placeholder(int kind, string label)
        {
            PrimitiveType pt = kind == 5 ? PrimitiveType.Cube : kind == 0 ? PrimitiveType.Cylinder : kind == 1 ? PrimitiveType.Sphere : PrimitiveType.Capsule;
            var go = GameObject.CreatePrimitive(pt);
            go.name = "ASH_Placeholder_" + label;
            go.transform.localScale = kind == 5 ? new Vector3(0.7f, 0.4f, 0.5f) : kind == 0 ? new Vector3(0.5f, 0.25f, 0.5f) : Vector3.one * 0.3f;
            return go;
        }

        static bool BindPrefabs(ASHPoiResourceSettings s)
        {
            bool d = false;
            GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!s.stonePrefab) { s.stonePrefab = Load("Assets/_ASH/Prefabs/World/ASH_StonePile.prefab"); d = s.stonePrefab; }
            if (!s.berryPrefab) { s.berryPrefab = Load("Assets/_ASH/Prefabs/World/ASH_ChokecherryBush.prefab"); d |= s.berryPrefab; }
            if (!s.kindlingPrefab) { s.kindlingPrefab = Load("Assets/_ASH/Prefabs/Pickups/ASH_Pickup_Kindling.prefab"); d |= s.kindlingPrefab; }
            if (s.woodPrefabs == null || s.woodPrefabs.Length == 0 || s.woodPrefabs.All(x => !x))
            {
                var wood = new[]
                {
                    Load("Assets/_ASH/Prefabs/World/ASH_Deadwood.prefab"),
                    Load("Assets/_ASH/Prefabs/Pickups/ASH_Deadwood_Branches01.prefab"),
                    Load("Assets/_ASH/Prefabs/Pickups/ASH_Deadwood_Branches02.prefab"),
                    Load("Assets/_ASH/Prefabs/Pickups/ASH_Deadwood_Branches03.prefab"),
                    Load("Assets/_ASH/Prefabs/Pickups/ASH_Pickup_Firewood.prefab")
                }.Where(x => x).ToArray();
                if (wood.Length > 0) { s.woodPrefabs = wood; d = true; }
            }
            if (s.foodPrefabs == null || s.foodPrefabs.Length == 0 || s.foodPrefabs.All(x => !x))
            {
                var food = new[] { "EnergyBar", "CannedBeans", "CannedPeaches", "DriedChokecherries", "SodaCan", "BottledWater" }
                    .Select(n => Load($"Assets/_ASH/Prefabs/Pickups/ASH_Pickup_{n}.prefab")).Where(x => x).ToArray();
                if (food.Length > 0) { s.foodPrefabs = food; d = true; }
            }
            if (!s.cratePrefab)
            {
                s.cratePrefab = EnsureCratePrefab();
                d |= s.cratePrefab;
            }
            return d;
        }

        static bool BindLoot(ASHPoiResourceSettings s)
        {
            if (s.crateLoot != null && s.crateLoot.Count > 0 && s.crateLoot.Any(e => e != null && e.item)) return false;
            s.crateLoot = new List<ASHPoiResourceSettings.LootEntry>();
            void Add(string file, float w, int a, int b)
            {
                var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"Assets/_ASH/Data/Items/ASH_Item_{file}.asset");
                if (it) s.crateLoot.Add(new ASHPoiResourceSettings.LootEntry { item = it, weight = w, amountMin = a, amountMax = b });
            }
            Add("EnergyBar", 3f, 1, 3);
            Add("CannedBeans", 3f, 1, 2);
            Add("CannedPeaches", 2f, 1, 2);
            Add("DriedChokecherries", 2f, 1, 3);
            Add("SodaCan", 2f, 1, 2);
            Add("BottledWater", 3f, 1, 2);
            Add("Bandage", 2f, 1, 2);
            Add("Rags", 2f, 1, 3);
            Add("Cloth", 1.5f, 1, 2);
            Add("Kindling", 2f, 2, 4);
            Add("Painkillers", 1.2f, 1, 1);
            Add("CanOpener", 1f, 1, 1);
            Add("ScrapMetal", 1.5f, 1, 3);
            Add("Rope", 1.2f, 1, 1);
            Add("PistolRounds", 1f, 4, 10);
            return s.crateLoot.Count > 0;
        }

        static GameObject EnsureCratePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CratePrefabPath);
            if (existing && existing.GetComponent<ASHSupplyCrate>()) return existing;
            EnsureFolder("Assets/_ASH/Prefabs/World");
            var salvage = AssetDatabase.LoadAssetAtPath<GameObject>(SalvagePath);
            GameObject go;
            if (salvage)
            {
                go = Object.Instantiate(salvage);
                go.name = "ASH_SupplyCrate";
                foreach (var n in go.GetComponents<ASHResourceNode>()) Object.DestroyImmediate(n);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "ASH_SupplyCrate";
                go.transform.localScale = new Vector3(0.85f, 0.45f, 0.6f);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(WoodMat);
                if (mat) go.GetComponent<Renderer>().sharedMaterial = mat;
            }
            if (!go.GetComponent<ASHSupplyCrate>()) go.AddComponent<ASHSupplyCrate>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, CratePrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}

