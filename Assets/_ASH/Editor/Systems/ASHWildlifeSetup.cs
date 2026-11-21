#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using ASH.Combat;
using ASH.EditorTools.Systems;
using ASH.Inventory;
using ASH.Movement;
using ASH.Wildlife;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASH.EditorTools
{
    /// <summary>
    /// Idempotent wildlife setup for every species: items, drop tables, dog-style locomotion controllers
    /// driven by each animal's own Malbers clips, wrapper prefabs (Malbers refs, not committed),
    /// and optional coat variants under Assets/_ASH/Art/Wildlife. Place / Clear never save the scene.
    /// </summary>
    public static class ASHWildlifeSetup
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_WildlifeSettings.asset";
        const string ItemDir = "Assets/_ASH/Data/Items";
        const string PrefabDir = "Assets/_ASH/Prefabs/Wildlife";
        const string LetterDir = "Assets/_ASH/Art/Icons Placeholder";
        const string AnimDir = "Assets/_ASH/Art/Wildlife/Anim";
        const string ArtDir = "Assets/_ASH/Art/Wildlife";
        const string PlayerPrefab = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        public const string SpawnRoot = "ASH_Wildlife";

        static readonly string[] ProtectedName = {
            "Tire replacement", "Gaia Water", "Gaia Planar", "ASH_Player", "ASH_Wildlife",
            "Grandpa Cabin", "Truck", "truck"
        };
        static readonly string[] ProtectedContains = {
            "porch", "tire replacement", "gaia water", "truck-bed", "truckbed", "held_"
        };

        [MenuItem("ASH/Systems/Wildlife/Setup Wildlife (idempotent)", false, 260)]
        public static void Setup()
        {
            var log = new StringBuilder("[ASH] Wildlife setup: ");
            var settings = ASHMenuInstaller.Ensure<ASHWildlifeSettings>(SettingsPath);
            settings.EnsureDefaults();

            var raw = FindItem("rawmeat");
            var bone = EnsureItem("animalbone", "Animal Bones", "Boiled or carved bones. Crafting and trade.", "AB", 0, 20, 2);
            var fat = EnsureItem("animalfat", "Animal Fat", "Rendered later for cooking, light and trade.", "AF", 0, 20, 3);
            var sinew = EnsureItem("sinew", "Sinew", "Tough connective tissue. Thread, bowstrings, bindings.", "SW", 0, 20, 2);
            var antler = EnsureItem("antler", "Antler", "A shed or harvested deer antler. Handles and knives.", "AN", 0, 10, 6);
            var tooth = EnsureItem("wolftooth", "Wolf Tooth", "A long canine. Charms and trade.", "WT", 0, 20, 4);
            var tusk = EnsureItem("boartusk", "Boar Tusk", "A curved tusk. Trophies and trade.", "TK", 0, 10, 6);
            var claw = EnsureItem("predatorclaw", "Predator Claw", "A hooked claw from a bear or cougar.", "PC", 0, 20, 5);

            var pelt = EnsureItem("wolfpelt", "Wolf Pelt", "A rough grey pelt. Clothing, trade.", "WP", 0, 10, 8);
            var hide = EnsureItem("bearhide", "Bear Hide", "A heavy hide. Bedrolls, clothing, trade.", "BH", 0, 8, 12);
            var deer = FindItem("deer_skin") ?? EnsureItem("deer_skin", "Deer Skin", "A soft hide. Pouches, clothing, trade.", "DS", 0, 20, 6);
            var fox = EnsureItem("foxpelt", "Fox Pelt", "A rust-red pelt. Hats and trim.", "FP", 0, 10, 7);
            var rabbit = EnsureItem("rabbitfur", "Rabbit Fur", "A small soft pelt. Lining and trim.", "RF", 0, 20, 3);
            var coon = EnsureItem("raccoonpelt", "Raccoon Pelt", "A ringed pelt. Trim and trade.", "RP", 0, 10, 5);
            var boar = EnsureItem("boarhide", "Boar Hide", "A bristly hide. Tough clothing and bags.", "BO", 0, 8, 7);
            var cougar = EnsureItem("cougarhide", "Cougar Hide", "A tawny hide. Coats and trade.", "CH", 0, 8, 14);

            FillDrops(settings.wolf, raw, pelt, bone, fat, sinew, tooth, null, null, 1, 2, 0.9f);
            FillDrops(settings.bear, raw, hide, bone, fat, sinew, claw, null, null, 2, 4, 1f);
            FillDrops(settings.fox, raw, fox, bone, fat, sinew, null, null, null, 1, 1, 0.8f);
            FillDrops(settings.rabbit, raw, rabbit, bone, fat, sinew, null, null, null, 1, 1, 0.5f);
            FillDrops(settings.raccoon, raw, coon, bone, fat, sinew, null, null, null, 1, 2, 0.7f);
            FillDrops(settings.deer, raw, deer, bone, fat, sinew, null, antler, null, 2, 3, 0.9f);
            FillDrops(settings.fawn, raw, deer, bone, fat, sinew, null, null, null, 1, 2, 0.6f);
            FillDrops(settings.boar, raw, boar, bone, fat, sinew, tusk, null, null, 2, 3, 1f);
            FillDrops(settings.cougar, raw, cougar, bone, fat, sinew, claw, null, null, 2, 3, 1f);
            EditorUtility.SetDirty(settings);

            int built = 0, missing = 0;
            var standIns = new StringBuilder();
            foreach (ASHWildlifeKind kind in System.Enum.GetValues(typeof(ASHWildlifeKind)))
            {
                if (BuildSpecies(kind, settings, log, standIns)) built++;
                else { missing++; log.Append(kind + " prefab skipped (Malbers missing); "); }
            }
            WirePlayerPrefab(log);
            ASHMenuInstaller.RefreshRegistry();
            AssetDatabase.SaveAssets();
            log.Append("built " + built + " wrappers; ");
            if (missing > 0) log.Append(missing + " species missing Malbers prefabs; ");
            if (standIns.Length > 0) log.Append("stand-ins: " + standIns + " ");
            log.Append("drop tables on ASH_WildlifeSettings. Run Place Far From Camp to spawn. Scene not saved.");
            Debug.Log(log.ToString());
        }

        [MenuItem("ASH/Systems/Wildlife/Setup Wolves and Bears (idempotent)", false, 259)]
        public static void SetupLegacy() => Setup();

        [MenuItem("ASH/Systems/Wildlife/Select Wildlife Settings", false, 261)]
        public static void SelectSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHWildlifeSettings>(SettingsPath);
            if (!s) Setup();
            s = AssetDatabase.LoadAssetAtPath<ASHWildlifeSettings>(SettingsPath);
            Selection.activeObject = s;
            EditorGUIUtility.PingObject(s);
        }

        [MenuItem("ASH/Systems/Wildlife/Place Far From Camp", false, 262)]
        public static void PlaceFarFromCamp()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHWildlifeSettings>(SettingsPath);
            if (!s) { Setup(); s = AssetDatabase.LoadAssetAtPath<ASHWildlifeSettings>(SettingsPath); }
            s.EnsureDefaults();
            var terrain = Terrain.activeTerrain;
            if (!terrain) { Debug.LogWarning("[ASH] Wildlife: no terrain."); return; }
            var root = GameObject.Find(SpawnRoot);
            if (!root) root = new GameObject(SpawnRoot);
            Undo.RegisterCreatedObjectUndo(root, "ASH wildlife");
            var blocked = CollectBlocked();
            var rnd = new System.Random(s.spawnSeed != 0 ? s.spawnSeed : 17041);
            var log = new StringBuilder("[ASH] Wildlife placed: ");
            int total = 0;
            foreach (var sp in s.All())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(sp.kind));
                if (!prefab) { log.Append(sp.kind + " missing prefab; "); continue; }
                int have = CountKind(root, sp.kind);
                int need = Mathf.Max(0, sp.placeCount - have);
                int n = PlaceKind(root, prefab, sp, need, s, terrain, blocked, rnd);
                total += n;
                if (n > 0) log.Append(n + " " + sp.displayName + "(s); ");
            }
            log.Append("under " + SpawnRoot + " (≥ " + s.campExclusionMeters.ToString("0") + " m from camp / protected). Scene not saved.");
            Debug.Log(log.ToString());
        }

        [MenuItem("ASH/Systems/Wildlife/Clear Placed Wildlife", false, 263)]
        public static void ClearPlaced()
        {
            var root = GameObject.Find(SpawnRoot);
            if (!root) { Debug.Log("[ASH] Wildlife: nothing to clear."); return; }
            int n = root.transform.childCount;
            Undo.DestroyObjectImmediate(root);
            Debug.Log($"[ASH] Wildlife: cleared {n} placed animal(s) under {SpawnRoot}. Scene not saved. Existing scene objects were not touched.");
        }

        static string PrefabPath(ASHWildlifeKind kind) => PrefabDir + "/ASH_Wildlife_" + kind + ".prefab";
        static string ControllerPath(ASHWildlifeKind kind) => AnimDir + "/ASH_" + kind + "_Locomotion.controller";

        static int CountKind(GameObject root, ASHWildlifeKind kind)
        {
            int n = 0;
            foreach (var a in root.GetComponentsInChildren<ASHAnimal>(true)) if (a.kind == kind) n++;
            return n;
        }

        static int PlaceKind(GameObject root, GameObject prefab, ASHWildlifeSettings.Species sp, int need, ASHWildlifeSettings s, Terrain terrain, List<Vector3> blocked, System.Random rnd)
        {
            if (!prefab || need <= 0 || sp == null) return 0;
            var td = terrain.terrainData; var origin = terrain.GetPosition(); var size = td.size;
            int got = 0, tries = 0, max = need * 50;
            int pack = Mathf.Max(1, sp.packSize);
            int packSerial = 1000 + (int)sp.kind * 10;
            while (got < need && tries < max)
            {
                var p = new Vector3(origin.x + 30f + (float)rnd.NextDouble() * (size.x - 60f), 0f, origin.z + 30f + (float)rnd.NextDouble() * (size.z - 60f));
                p.y = terrain.SampleHeight(p) + origin.y;
                if (!ClearOf(p, s.campExclusionMeters, blocked)) { tries++; continue; }
                if (!ClearOfChildren(root, p, s.packSpacing)) { tries++; continue; }
                var nrm = td.GetInterpolatedNormal((p.x - origin.x) / size.x, (p.z - origin.z) / size.z);
                if (nrm.y < 0.82f) { tries++; continue; }
                int group = Mathf.Min(pack, need - got);
                int packId = group > 1 ? packSerial++ : 0;
                for (int i = 0; i < group; i++)
                {
                    var at = p;
                    if (i > 0)
                    {
                        float ang = (float)rnd.NextDouble() * 360f;
                        at += Quaternion.Euler(0f, ang, 0f) * Vector3.forward * (3f + i * 1.2f);
                        at.y = terrain.SampleHeight(at) + origin.y;
                    }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
                    go.transform.SetParent(root.transform, true);
                    go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
                    var an = go.GetComponent<ASHAnimal>() ?? go.AddComponent<ASHAnimal>();
                    an.kind = sp.kind; an.settings = s; an.packId = packId;
                    an.size = RollSize(s, rnd);
                    Undo.RegisterCreatedObjectUndo(go, "ASH wildlife");
                    got++;
                    if (got >= need) break;
                }
            }
            return got;
        }

        static ASHWildlifeSize RollSize(ASHWildlifeSettings s, System.Random rnd)
        {
            double u = rnd.NextDouble();
            if (u < s.youngChance) return ASHWildlifeSize.Young;
            if (u > 1.0 - s.alphaChance) return ASHWildlifeSize.Alpha;
            return ASHWildlifeSize.Adult;
        }

        static bool ClearOfChildren(GameObject root, Vector3 p, float spacing)
        {
            float r2 = spacing * spacing;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var c = root.transform.GetChild(i);
                var d = c.position - p; d.y = 0f;
                if (d.sqrMagnitude < r2) return false;
            }
            return true;
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

        static bool BuildSpecies(ASHWildlifeKind kind, ASHWildlifeSettings settings, StringBuilder log, StringBuilder standIns)
        {
            var src = FindSpeciesPrefab(kind, out string usedPath, out bool standIn);
            if (!src) return false;
            log.Append(kind + " src " + Path.GetFileName(usedPath) + "; ");
            if (standIn) standIns.Append(kind + " used " + Path.GetFileName(usedPath) + "; ");
            var ctrlSrc = FindSpeciesController(kind, usedPath);
            var loco = BuildLocoController(kind, ctrlSrc, settings, log);
            return BuildWrapper(src, PrefabPath(kind), kind, settings, loco, ctrlSrc, log);
        }

        static readonly string[] Forest = { "Assets/Malbers Animations/Animals Packs/01 Forest Pack/" };
        static readonly string[] WolfLite = {
            "Assets/Malbers Animations/Animal Controller/Wolf Lite/Wolf Lite.prefab",
            "Assets/Malbers Animations/Animal Controller/Wolf Lite/Wolf Lite AI.prefab"
        };

        static GameObject FindSpeciesPrefab(ASHWildlifeKind kind, out string path, out bool standIn)
        {
            path = null; standIn = false;
            string folder, prefer;
            switch (kind)
            {
                case ASHWildlifeKind.Wolf: folder = "Wolf"; prefer = "Wolf"; break;
                case ASHWildlifeKind.Bear: folder = "Bear"; prefer = "Bear Black"; break;
                case ASHWildlifeKind.Fox: folder = "Fox"; prefer = "Fox Snow"; break;
                case ASHWildlifeKind.Rabbit: folder = "Rabbit"; prefer = "Rabbit PA"; break;
                case ASHWildlifeKind.Raccoon: folder = "Raccoon"; prefer = "Raccoon PA"; break;
                case ASHWildlifeKind.Deer: folder = "Deer"; prefer = "Deer"; break;
                case ASHWildlifeKind.Fawn: folder = "Deer"; prefer = "Fawn"; break;
                case ASHWildlifeKind.Boar: folder = "Boar"; prefer = "Boar"; break;
                case ASHWildlifeKind.Cougar: folder = "Cougar"; prefer = "Cougar"; break;
                default: folder = kind.ToString(); prefer = kind.ToString(); break;
            }
            // exact well-known paths first
            string[] exact = {
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/" + folder + "/" + prefer + ".prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/" + folder + "/Models/" + prefer + ".prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Rabbit/Models/Rabbit PA.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Fox/Models/Fox Snow.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Raccoon/Models/Raccoon PA.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Wolf/Models/Wolf.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Bear/Bear Black.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Raccoon/Raccoon.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Raccon/Raccoon.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Deer/Models/Fawn.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Deer/Models/Deer.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Deer/Fawn.prefab",
                "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Deer/Deer.prefab"
            };
            foreach (var p in exact)
            {
                var g = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (g && NameFits(g.name, prefer, kind)) { path = p; return g; }
            }
            var hits = FindPrefabsIn("Assets/Malbers Animations", folder, prefer, kind);
            if (kind == ASHWildlifeKind.Raccoon && hits.Count == 0)
                hits = FindPrefabsIn("Assets/Malbers Animations", "Raccon", prefer, kind);
            if (hits.Count > 0) { path = hits[0]; return AssetDatabase.LoadAssetAtPath<GameObject>(path); }
            if (kind == ASHWildlifeKind.Wolf)
            {
                foreach (var p in WolfLite)
                {
                    var g = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (g) { path = p; standIn = true; return g; }
                }
            }
            return null;
        }

        static bool IsNonAnimalPrefab(string path, string name)
        {
            var p = (path ?? "").Replace('\\', '/').ToLowerInvariant();
            var n = (name ?? "").ToLowerInvariant();
            if (p.Contains("/tracks/") || p.Contains("/fx/") || p.Contains("/vfx/")) return true;
            if (n.Contains("track") || n.Contains("footprint") || n.Contains("foot print") || n.Contains("decal") || n.Contains("particle") || n.Contains("vfx")) return true;
            return false;
        }

        static bool NameFits(string name, string prefer, ASHWildlifeKind kind)
        {
            var n = name.ToLowerInvariant();
            if (IsNonAnimalPrefab("", name)) return false;
            if (n.Contains("ragdoll") || n.Contains("dead") || n.Contains("demo scene") || n.Contains("enemy")) return false;
            if (kind == ASHWildlifeKind.Fawn) return n.Contains("fawn");
            if (kind == ASHWildlifeKind.Deer) return n.Contains("deer") && !n.Contains("fawn");
            return n.Contains(prefer.ToLowerInvariant()) || n.Contains(kind.ToString().ToLowerInvariant());
        }

        static int PrefabScore(string path, string prefer)
        {
            var n = Path.GetFileNameWithoutExtension(path ?? "").ToLowerInvariant();
            var preferL = (prefer ?? "").ToLowerInvariant();
            int s = 0;
            var p = (path ?? "").Replace('\\', '/');
            if (p.IndexOf("/Models/", System.StringComparison.OrdinalIgnoreCase) >= 0) s += 8;
            if (n == preferL) s += 10;
            else if (preferL.Length > 0 && n.StartsWith(preferL)) s += 4;
            if (n.Contains("baby") || n.Contains("cub") || n.Contains("corgi") || n.Contains("fennec") || n.Contains("panda") || n.Contains("mane wolf")) s -= 6;
            if (n.Contains("showcase") || n.Contains("demo")) s -= 5;
            return s;
        }

        static List<string> FindPrefabsIn(string root, string folderToken, string prefer, ASHWildlifeKind kind)
        {
            var list = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.IndexOf(folderToken, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (IsNonAnimalPrefab(p, Path.GetFileNameWithoutExtension(p))) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (!go || !NameFits(go.name, prefer, kind)) continue;
                list.Add(p);
            }
            list.Sort((a, b) =>
            {
                int d = PrefabScore(b, prefer).CompareTo(PrefabScore(a, prefer));
                return d != 0 ? d : string.CompareOrdinal(a, b);
            });
            return list;
        }

        static RuntimeAnimatorController FindSpeciesController(ASHWildlifeKind kind, string prefabPath)
        {
            string folder = Path.GetDirectoryName(prefabPath ?? "").Replace('\\', '/');
            var cands = new List<string>();
            if (!string.IsNullOrEmpty(folder))
                foreach (var g in AssetDatabase.FindAssets("t:AnimatorController", new[] { folder }))
                    cands.Add(AssetDatabase.GUIDToAssetPath(g));
            foreach (var g in AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/Malbers Animations" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.IndexOf(kind == ASHWildlifeKind.Fawn ? "Deer" : kind.ToString(), System.StringComparison.OrdinalIgnoreCase) >= 0
                    || (kind == ASHWildlifeKind.Raccoon && p.IndexOf("Raccon", System.StringComparison.OrdinalIgnoreCase) >= 0))
                    cands.Add(p);
            }
            RuntimeAnimatorController best = null;
            int score = -1;
            foreach (var p in cands)
            {
                int s = 0;
                var n = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (n.Contains("eye")) continue;
                if (n.Contains("showcase")) s -= 5;
                if (n.Contains("animation")) s += 3;
                if (n == "wolf animations" || n.EndsWith(" animations")) s += 5;
                if (n.Contains("demo") && kind != ASHWildlifeKind.Cougar) s -= 1;
                if (kind == ASHWildlifeKind.Cougar && n.Contains("demo")) s += 2;
                if (s > score) { score = s; best = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(p); }
            }
            return best;
        }

        static RuntimeAnimatorController BuildLocoController(ASHWildlifeKind kind, RuntimeAnimatorController src, ASHWildlifeSettings settings, StringBuilder log)
        {
            if (src == null) return null;
            ASHMenuInstaller.Folder(AnimDir);
            var path = ControllerPath(kind);
            var clips = CollectClips(kind, src);
            var idle = PickClip(clips, true, new[] { "idle", "relax" }, new[] { "to ", "walk", "run", "trot", "eat", "sleep", "attack" });
            var walk = PickClip(clips, true, new[] { "walk" }, new[] { "to ", "back", "left", "right", "turn", "idle" });
            var trot = PickClip(clips, true, new[] { "trot" }, new[] { "to ", "left", "right", "turn" });
            var run = PickClip(clips, true, new[] { "run", "sprint", "gallop" }, new[] { "to ", "left", "right", "turn" });
            var turnL = PickClip(clips, true, new[] { "turnl", "turn_l", "turn l", "turnleft", "bturn l" }, new[] { "to " });
            var turnR = PickClip(clips, true, new[] { "turnr", "turn_r", "turn r", "turnright", "bturn r" }, new[] { "to " });
            var attack = PickClip(clips, false, new[] { "attack", "bite", "claw", "swipe", "charge" }, new[] { "to ", "idle" });
            var hit = PickClip(clips, false, new[] { "hit", "hurt", "damage", "react", "stun" }, new[] { "to ", "death" });
            var death = PickClip(clips, false, new[] { "death", "die", "dead" }, new[] { "to " });
            var eat = PickClip(clips, true, new[] { "eat", "graze", "drink" }, new[] { "to " });
            var sleep = PickClip(clips, true, new[] { "sleep" }, new[] { "to " });
            var call = PickClip(clips, false, new[] { "howl", "roar", "bark", "call" }, new[] { "to " });
            if (walk == null && trot == null && run == null) { log.Append(kind + " no gait clips; "); return src; }

            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Turn", AnimatorControllerParameterType.Float);
            var sm = ac.layers[0].stateMachine;
            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(tree, ac);
            float gt = 2f, gr = 4f;
            var sp = settings.Of(kind);
            if (sp != null) { gt = Mathf.Max(sp.walkSpeed * 0.5f, 0.2f); var mid = sp.trotSpeed > 0.2f ? sp.trotSpeed : (sp.walkSpeed + sp.runSpeed) * 0.5f; gr = Mathf.Max(mid, sp.runSpeed * 0.7f); }
            if (idle) tree.AddChild(idle, 0f);
            if (walk) tree.AddChild(walk, Mathf.Max(0.15f, sp != null ? sp.walkSpeed : 1.5f));
            if (trot) tree.AddChild(trot, sp != null ? (sp.trotSpeed > 0.2f ? sp.trotSpeed : (sp.walkSpeed + sp.runSpeed) * 0.5f) : 3f);
            if (run) tree.AddChild(run, sp != null ? sp.runSpeed : 6f);
            var loco = sm.AddState("Locomotion");
            loco.motion = tree;
            sm.defaultState = loco;
            void Add(string name, AnimationClip clip) { if (!clip) return; var st = sm.AddState(name); st.motion = clip; }
            Add("Idle", idle);
            Add("Walk", walk); Add("Trot", trot); Add("Run", run);
            Add("TurnL", turnL); Add("TurnR", turnR);
            Add("Attack", attack); Add("Hit", hit); Add("Death", death);
            Add("Eat", eat); Add("Sleep", sleep); Add("Call", call);
            if (!death) log.Append(kind + " still has no death clip; ");
            else log.Append(kind + " death=" + death.name + "; ");
            if (idle) { var st = sm.AddState("Idle"); st.motion = idle; }

            if (sp != null)
            {
                if (walk) sp.walkClipSpeed = ClipSpeed(walk);
                if (trot) sp.trotClipSpeed = ClipSpeed(trot);
                if (run) sp.runClipSpeed = ClipSpeed(run);
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            log.Append(kind + " loco controller; ");
            return ac;
        }

        static float ClipSpeed(AnimationClip clip)
        {
            if (!clip) return 1f;
            var v = clip.averageSpeed;
            float s = new Vector2(v.x, v.z).magnitude;
            return Mathf.Clamp(s > 0.05f ? s : 1f, 0.05f, 20f);
        }

        static AnimationClip PickClip(AnimationClip[] clips, bool loopPrefer, string[] want, string[] reject)
        {
            AnimationClip best = null; int score = -1;
            foreach (var c in clips)
            {
                if (!c) continue;
                var n = c.name.ToLowerInvariant().Replace(" ", "").Replace("-", "");
                if (n.StartsWith("to")) continue;
                bool bad = false;
                foreach (var r in reject)
                {
                    var rr = r.ToLowerInvariant().Replace(" ", "");
                    if (rr.Length > 0 && n.Contains(rr) && !ContainsAny(n, want)) { bad = true; break; }
                    if (rr.Length > 0 && n.Contains(rr) && (rr == "left" || rr == "right" || rr == "turn" || rr == "back")) { bad = true; break; }
                }
                int s = 0;
                foreach (var w in want)
                {
                    var ww = w.ToLowerInvariant().Replace(" ", "").Replace("_", "");
                    if (n.Contains(ww)) s += 3;
                }
                if (s == 0) continue;
                if (bad && s < 6) continue;
                if (loopPrefer && c.isLooping) s += 1;
                if (s > score) { score = s; best = c; }
            }
            return best;
        }

        static bool ContainsAny(string n, string[] want)
        {
            foreach (var w in want)
            {
                var ww = w.ToLowerInvariant().Replace(" ", "").Replace("_", "");
                if (ww.Length > 0 && n.Contains(ww)) return true;
            }
            return false;
        }

        static bool BuildWrapper(GameObject src, string dest, ASHWildlifeKind kind, ASHWildlifeSettings settings, RuntimeAnimatorController loco, RuntimeAnimatorController srcCtrl, StringBuilder log)
        {
            ASHMenuInstaller.Folder(PrefabDir);
            var root = PrefabUtility.InstantiatePrefab(src) as GameObject;
            if (!root) return false;
            root.name = "ASH_Wildlife_" + kind;
            var animal = root.GetComponent<ASHAnimal>() ?? root.AddComponent<ASHAnimal>();
            animal.kind = kind;
            animal.settings = settings;
            var locoCmp = root.GetComponent<ASHGroundLocomotion>() ?? root.AddComponent<ASHGroundLocomotion>();
            var vis = root.GetComponent<ASHWildlifeAnim>() ?? root.AddComponent<ASHWildlifeAnim>();
            var anim = root.GetComponentInChildren<Animator>();
            if (anim)
            {
                anim.applyRootMotion = false;
                if (loco) anim.runtimeAnimatorController = loco;
                vis.Bind(anim);
                vis.locoState = "Locomotion";
                vis.idleState = "Idle";
                vis.walkState = "Walk";
                vis.trotState = "Trot";
                vis.runState = "Run";
                vis.turnLState = "TurnL";
                vis.turnRState = "TurnR";
                vis.attackState = "Attack";
                vis.hitState = "Hit";
                vis.deathState = "Death";
                vis.eatState = "Eat";
                vis.sleepState = "Sleep";
                vis.callState = "Call";
            }
            WireAudio(vis, srcCtrl, dest);
            MaybeCoat(root, kind, settings);
            StripStrayParticles(root);
            if (!HasCollider(root))
            {
                var body = settings.Body(settings.Of(kind));
                var cap = root.AddComponent<CapsuleCollider>();
                cap.height = Mathf.Max(0.4f, body.y + body.z);
                cap.radius = Mathf.Max(0.12f, body.x);
                cap.center = new Vector3(0f, Mathf.Max(0.2f, body.z), 0f);
            }
            var cc = root.GetComponent<CharacterController>();
            if (cc) Object.DestroyImmediate(cc);
            PrepareDamage(root, settings, kind);
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (tr) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, dest);
            Object.DestroyImmediate(root);
            if (saved && settings) { settings.SetPrefab(kind, saved); EditorUtility.SetDirty(settings); }
            log.Append(Path.GetFileName(dest) + " written; ");
            return true;
        }

        static void WireAudio(ASHWildlifeAnim vis, RuntimeAnimatorController srcCtrl, string hintPath)
        {
            var clips = new List<AudioClip>();
            string[] roots = { "Assets/Malbers Animations" };
            foreach (var g in AssetDatabase.FindAssets("t:AudioClip", roots))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var n = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                var folder = p.ToLowerInvariant();
                var kind = vis ? vis.gameObject.name : "";
                // collect broadly then sort by name tokens; assignment happens by token
                clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(p));
            }
            var calls = new List<AudioClip>();
            var atk = new List<AudioClip>();
            var hurt = new List<AudioClip>();
            var death = new List<AudioClip>();
            var steps = new List<AudioClip>();
            string species = vis.transform ? vis.name.ToLowerInvariant() : "";
            foreach (var c in clips)
            {
                if (!c) continue;
                var n = c.name.ToLowerInvariant();
                var p = AssetDatabase.GetAssetPath(c).ToLowerInvariant();
                bool mine = p.Contains("wolf") && species.Contains("wolf")
                    || p.Contains("bear") && species.Contains("bear")
                    || p.Contains("fox") && species.Contains("fox")
                    || p.Contains("rabbit") && species.Contains("rabbit")
                    || p.Contains("raccoon") && species.Contains("raccoon")
                    || p.Contains("raccon") && species.Contains("raccoon")
                    || p.Contains("deer") && (species.Contains("deer") || species.Contains("fawn"))
                    || p.Contains("boar") && species.Contains("boar")
                    || p.Contains("cougar") && species.Contains("cougar");
                if (!mine) continue;
                if (n.Contains("howl") || n.Contains("roar") || n.Contains("bark") || n.Contains("whin")) calls.Add(c);
                else if (n.Contains("attack") || n.Contains("growl")) atk.Add(c);
                else if (n.Contains("hurt") || n.Contains("hit") || n.Contains("pain") || n.Contains("whine")) hurt.Add(c);
                else if (n.Contains("death") || n.Contains("die")) death.Add(c);
                else if (n.Contains("step") || n.Contains("foot")) steps.Add(c);
                else if (n.Contains("wolf") || n.Contains("bear") || n.Contains("roar")) calls.Add(c);
            }
            vis.callClips = calls.ToArray();
            vis.attackClips = atk.ToArray();
            vis.hurtClips = hurt.ToArray();
            vis.deathClips = death.ToArray();
            vis.stepClips = steps.ToArray();
        }

        static void MaybeCoat(GameObject root, ASHWildlifeKind kind, ASHWildlifeSettings settings)
        {
            // Only generate a tinted material copy for wolf (ash), bear (dust), deer (winter), cougar (darker alpha).
            Color tint;
            string tag;
            switch (kind)
            {
                case ASHWildlifeKind.Wolf: tint = new Color(0.55f, 0.55f, 0.58f, 1f); tag = "ash"; break;
                case ASHWildlifeKind.Bear: tint = new Color(0.62f, 0.58f, 0.52f, 1f); tag = "ash"; break;
                case ASHWildlifeKind.Deer: tint = new Color(0.82f, 0.82f, 0.85f, 1f); tag = "winter"; break;
                case ASHWildlifeKind.Cougar: tint = new Color(0.45f, 0.38f, 0.32f, 1f); tag = "dark"; break;
                default: return;
            }
            var rend = root.GetComponentInChildren<Renderer>();
            if (!rend || !rend.sharedMaterial) return;
            var srcMat = rend.sharedMaterial;
            var srcTex = srcMat.HasProperty("_BaseMap") ? srcMat.GetTexture("_BaseMap") as Texture2D : srcMat.mainTexture as Texture2D;
            ASHMenuInstaller.Folder(ArtDir);
            string matPath = ArtDir + "/ASH_" + kind + "_" + tag + ".mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(matPath))
            {
                var mat = new Material(srcMat);
                mat.name = "ASH_" + kind + "_" + tag;
                if (srcTex)
                {
                    // Don't edit the vendor texture; a slight tint on a copied material is enough.
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                    else if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
                }
                AssetDatabase.CreateAsset(mat, matPath);
            }
            // Keep the vendor material on the default wrapper. Coat mats are available for size=Alpha at place time later.
        }

        static void StripStrayParticles(GameObject root)
        {
            if (!root) return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!ps) continue;
                var n = ps.gameObject.name.ToLowerInvariant();
                bool track = n.Contains("track") || n.Contains("footprint") || n.Contains("foot print") || n.Contains("decal") || n.Contains("print");
                if (track)
                {
                    if (ps.gameObject != root) Object.DestroyImmediate(ps.gameObject);
                    else Object.DestroyImmediate(ps);
                    continue;
                }
                var main = ps.main;
                main.playOnAwake = false;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        static AnimationClip[] CollectClips(ASHWildlifeKind kind, RuntimeAnimatorController src)
        {
            var list = new List<AnimationClip>();
            var seen = new HashSet<string>();
            void Add(AnimationClip c)
            {
                if (!c || seen.Contains(c.name)) return;
                seen.Add(c.name);
                list.Add(c);
            }
            if (src != null && src.animationClips != null)
                foreach (var c in src.animationClips) Add(c);
            string token = kind == ASHWildlifeKind.Fawn ? "Deer" : kind.ToString();
            string rac = kind == ASHWildlifeKind.Raccoon ? "Raccon" : null;
            var folders = new List<string>();
            if (AssetDatabase.IsValidFolder("Assets/Malbers Animations")) folders.Add("Assets/Malbers Animations");
            foreach (var folder in folders)
            {
                foreach (var g in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    if (path.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) < 0
                        && (rac == null || path.IndexOf(rac, System.StringComparison.OrdinalIgnoreCase) < 0))
                        continue;
                    Add(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
                }
                foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    if (path.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) < 0
                        && (rac == null || path.IndexOf(rac, System.StringComparison.OrdinalIgnoreCase) < 0))
                        continue;
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (o is AnimationClip clip) Add(clip);
                }
            }
            return list.ToArray();
        }

        static void PrepareDamage(GameObject root, ASHWildlifeSettings settings, ASHWildlifeKind kind)
        {
            try { root.tag = "Enemy"; } catch { /* tag missing */ }
            int layer = LayerMask.NameToLayer("Enemy");
            if (layer >= 0) SetLayer(root.transform, layer);
            var animal = root.GetComponent<ASHAnimal>() ?? root.AddComponent<ASHAnimal>();
            bool solid = false;
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (!c || c is CharacterController) continue;
                if (!c.isTrigger) solid = true;
                if (layer >= 0) c.gameObject.layer = layer;
                try { c.gameObject.tag = "Enemy"; } catch { }
                if (!c.GetComponent<ASHWildlifeHitbox>())
                    c.gameObject.AddComponent<ASHWildlifeHitbox>().Bind(animal);
            }
            if (!solid)
            {
                var body = settings ? settings.Body(settings.Of(kind)) : new Vector3(0.3f, 0.7f, 0.5f);
                Transform ht = root.transform.Find("ASH_Hitbox");
                GameObject hg = ht ? ht.gameObject : new GameObject("ASH_Hitbox");
                if (!ht) hg.transform.SetParent(root.transform, false);
                if (layer >= 0) hg.layer = layer;
                try { hg.tag = "Enemy"; } catch { }
                var cap = hg.GetComponent<CapsuleCollider>() ?? hg.AddComponent<CapsuleCollider>();
                cap.isTrigger = false;
                cap.height = Mathf.Max(0.4f, body.y + body.z);
                cap.radius = Mathf.Max(0.12f, body.x);
                cap.center = new Vector3(0f, Mathf.Max(0.2f, body.z), 0f);
                cap.direction = 1;
                if (!hg.GetComponent<ASHWildlifeHitbox>()) hg.AddComponent<ASHWildlifeHitbox>().Bind(animal);
            }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        static bool HasCollider(GameObject root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
                if (c && !(c is CharacterController)) return true;
            return false;
        }

        static void WirePlayerPrefab(StringBuilder log)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (!go) { log.Append("ASH_Player.prefab missing (relay will Ensure at runtime); "); return; }
            var inst = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                if (!inst.GetComponent<ASHPlayerCombatRelay>() && !inst.GetComponentInChildren<ASHPlayerCombatRelay>(true))
                {
                    inst.AddComponent<ASHPlayerCombatRelay>();
                    PrefabUtility.SaveAsPrefabAsset(inst, PlayerPrefab);
                    log.Append("combat relay on ASH_Player.prefab; ");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(inst); }
        }

        static void FillDrops(ASHWildlifeSettings.Species sp, ASHItemDefinition meat, ASHItemDefinition hide, ASHItemDefinition bone, ASHItemDefinition fat, ASHItemDefinition sinew, ASHItemDefinition special, ASHItemDefinition antler, ASHItemDefinition unused, int meatMin, int meatMax, float hideChance)
        {
            if (sp.drops == null) sp.drops = new List<ASHWildlifeSettings.Drop>();
            if (sp.drops.Count > 0) return;
            AddDrop(sp, meat, meatMin, meatMax, 1f);
            AddDrop(sp, hide, 1, 1, hideChance);
            AddDrop(sp, bone, Mathf.Max(1, meatMin), meatMax + 1, 1f);
            AddDrop(sp, fat, 0, Mathf.Max(1, meatMin), 0.75f);
            AddDrop(sp, sinew, 0, 2, 0.65f);
            AddDrop(sp, special, 1, 1, 0.7f);
            AddDrop(sp, antler, 1, 2, 0.8f);
        }

        static void AddDrop(ASHWildlifeSettings.Species sp, ASHItemDefinition item, int min, int max, float chance)
        {
            if (!item) return;
            sp.drops.Add(new ASHWildlifeSettings.Drop { item = item, min = min, max = max, chance = chance });
        }

        static ASHItemDefinition FindItem(string id)
        {
            foreach (var g in AssetDatabase.FindAssets("t:ASHItemDefinition"))
            {
                var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g));
                if (it && it.itemId == id) return it;
            }
            return null;
        }

        public static ASHItemDefinition EnsureItem(string id, string name, string desc, string letters, int category, int stack, int sell)
        {
            var existing = FindItem(id);
            var icon = EnsureLetter($"{LetterDir}/ASH_Letter_{letters}.png", letters);
            if (existing)
            {
                if (icon && !existing.icon) { existing.icon = icon; EditorUtility.SetDirty(existing); }
                return existing;
            }
            ASHMenuInstaller.Folder(ItemDir);
            var it = ScriptableObject.CreateInstance<ASHItemDefinition>();
            it.itemId = id;
            it.displayName = name;
            it.description = desc;
            it.icon = icon;
            it.category = (ASHItemCategory)category;
            it.maxStack = stack;
            it.sellValue = sell;
            it.useVerb = "Use";
            AssetDatabase.CreateAsset(it, $"{ItemDir}/ASH_Item_{ToAsset(id)}.asset");
            return it;
        }

        static string ToAsset(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Item";
            var parts = id.Replace('-', '_').Split('_');
            var sb = new StringBuilder();
            foreach (var p in parts) if (p.Length > 0) sb.Append(char.ToUpperInvariant(p[0])).Append(p.Substring(1));
            return sb.ToString();
        }

        public static Sprite EnsureLetter(string path, string letters)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite) return sprite;
            ASHMenuInstaller.Folder(LetterDir);
            const int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;
            tex.SetPixels(px);
            var gold = new Color(0.83f, 0.69f, 0.32f, 1f);
            DrawLetters(tex, letters, gold);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp)
            {
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void DrawLetters(Texture2D tex, string letters, Color gold)
        {
            if (string.IsNullOrEmpty(letters)) return;
            int count = Mathf.Min(2, letters.Length);
            for (int i = 0; i < count; i++)
            {
                int ox = count == 1 ? 80 : (i == 0 ? 28 : 140);
                DrawGlyph(tex, letters[i], ox, 48, gold);
            }
        }

        static void DrawGlyph(Texture2D tex, char ch, int x, int y, Color c)
        {
            void B(int ox, int oy, int w, int h) { Fill(tex, x + ox, y + oy, w, h, c); }
            switch (char.ToUpperInvariant(ch))
            {
                case 'A': B(0, 0, 16, 160); B(64, 0, 16, 160); B(0, 160, 80, 16); B(0, 80, 80, 16); break;
                case 'B': B(0, 0, 16, 176); B(0, 0, 60, 16); B(0, 80, 60, 16); B(0, 160, 60, 16); B(60, 16, 16, 64); B(60, 96, 16, 64); break;
                case 'C': B(0, 16, 16, 144); B(0, 160, 72, 16); B(0, 0, 72, 16); break;
                case 'D': B(0, 0, 16, 176); B(0, 0, 56, 16); B(0, 160, 56, 16); B(56, 16, 16, 144); break;
                case 'E': B(0, 0, 16, 176); B(0, 160, 76, 16); B(0, 80, 60, 16); B(0, 0, 76, 16); break;
                case 'F': B(0, 0, 16, 176); B(0, 160, 76, 16); B(0, 88, 56, 16); break;
                case 'G': B(0, 16, 16, 144); B(0, 160, 76, 16); B(0, 0, 76, 16); B(60, 0, 16, 80); B(44, 80, 32, 16); break;
                case 'H': B(0, 0, 16, 176); B(64, 0, 16, 176); B(0, 80, 80, 16); break;
                case 'I': B(16, 0, 16, 176); B(0, 0, 48, 16); B(0, 160, 48, 16); break;
                case 'K': B(0, 0, 16, 176); B(16, 80, 24, 16); B(40, 96, 16, 80); B(40, 0, 16, 80); break;
                case 'L': B(0, 0, 16, 176); B(0, 0, 76, 16); break;
                case 'M': B(0, 0, 16, 176); B(72, 0, 16, 176); B(16, 128, 16, 32); B(48, 128, 16, 32); B(32, 96, 16, 32); break;
                case 'N': B(0, 0, 16, 176); B(64, 0, 16, 176); B(16, 112, 16, 32); B(32, 80, 16, 32); B(48, 48, 16, 32); break;
                case 'O': B(0, 16, 16, 144); B(64, 16, 16, 144); B(0, 160, 80, 16); B(0, 0, 80, 16); break;
                case 'P': B(0, 0, 16, 176); B(0, 160, 64, 16); B(0, 88, 64, 16); B(64, 104, 16, 56); break;
                case 'R': B(0, 0, 16, 176); B(0, 160, 64, 16); B(0, 88, 64, 16); B(64, 104, 16, 56); B(40, 0, 16, 88); break;
                case 'S': B(0, 160, 76, 16); B(0, 80, 76, 16); B(0, 0, 76, 16); B(0, 96, 16, 64); B(60, 16, 16, 64); break;
                case 'T': B(0, 160, 80, 16); B(32, 0, 16, 160); break;
                case 'U': B(0, 16, 16, 160); B(64, 16, 16, 160); B(0, 0, 80, 16); break;
                case 'W': B(0, 0, 16, 176); B(72, 0, 16, 176); B(24, 0, 16, 80); B(48, 0, 16, 80); break;
                case 'Y': B(0, 112, 16, 64); B(64, 112, 16, 64); B(32, 0, 16, 112); B(8, 160, 16, 16); B(56, 160, 16, 16); break;
                default: B(0, 0, 16, 176); B(0, 0, 64, 16); B(0, 160, 64, 16); break;
            }
        }

        static void Fill(Texture2D tex, int x, int y, int w, int h, Color c)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    int xx = x + i, yy = y + j;
                    if ((uint)xx < (uint)tex.width && (uint)yy < (uint)tex.height) tex.SetPixel(xx, yy, c);
                }
        }
    }
}
#endif
