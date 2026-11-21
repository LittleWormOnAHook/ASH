#if UNITY_EDITOR
using System.IO;
using System.Text;
using ASH.Combat;
using ASH.EditorTools.Systems;
using ASH.Inventory;
using ASH.Wildlife;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASH.EditorTools
{
    /// <summary>
    /// Idempotent wildlife setup: items, Play-editable drop tables, wolf/bear wrapper prefabs (Malbers, not committed).
    /// Place / Clear menus never parent to or write on existing scene objects, and never save the open scene.
    /// </summary>
    public static class ASHWildlifeSetup
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_WildlifeSettings.asset";
        const string ItemDir = "Assets/_ASH/Data/Items";
        const string PrefabDir = "Assets/_ASH/Prefabs/Wildlife";
        const string LetterDir = "Assets/_ASH/Art/Icons Placeholder";
        const string WolfSrcA = "Assets/Malbers Animations/Animal Controller/Wolf Lite/Wolf Lite.prefab";
        const string WolfSrcB = "Assets/Malbers Animations/Animal Controller/Wolf Lite/Wolf Lite AI.prefab";
        const string WolfSrcC = "Assets/Malbers Animations/Animal Controller/Wolf Lite/Wolf Lite AI Enemy.prefab";
        const string BearSrcA = "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Bear/Bear Black.prefab";
        const string BearSrcB = "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Bear/Models/Bear.prefab";
        const string WolfPrefab = PrefabDir + "/ASH_Wildlife_Wolf.prefab";
        const string BearPrefab = PrefabDir + "/ASH_Wildlife_Bear.prefab";
        const string PlayerPrefab = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        public const string SpawnRoot = "ASH_Wildlife";

        static readonly string[] ProtectedName = {
            "Tire replacement", "Gaia Water", "Gaia Planar", "ASH_Player", "ASH_Wildlife",
            "Grandpa Cabin", "Truck", "truck"
        };
        static readonly string[] ProtectedContains = {
            "porch", "tire replacement", "gaia water", "truck-bed", "truckbed", "held_"
        };

        [MenuItem("ASH/Systems/Wildlife/Setup Wolves and Bears (idempotent)", false, 260)]
        public static void Setup()
        {
            var log = new StringBuilder("[ASH] Wildlife setup: ");
            var settings = ASHMenuInstaller.Ensure<ASHWildlifeSettings>(SettingsPath);
            var raw = FindItem("rawmeat");
            var bone = EnsureItem("animalbone", "Animal Bones", "Boiled or carved bones. Crafting and trade.", "AB", 0, 20, 2);
            var fat = EnsureItem("animalfat", "Animal Fat", "Rendered later for cooking, light and trade.", "AF", 0, 20, 3);
            var pelt = EnsureItem("wolfpelt", "Wolf Pelt", "A rough grey pelt. Clothing, trade.", "WP", 0, 10, 8);
            var hide = EnsureItem("bearhide", "Bear Hide", "A heavy hide. Bedrolls, clothing, trade.", "BH", 0, 8, 12);
            FillDrops(settings.wolf, raw, pelt, bone, fat, true);
            FillDrops(settings.bear, raw, hide, bone, fat, false);
            EditorUtility.SetDirty(settings);

            bool wolf = BuildWolf(log);
            bool bear = BuildBear(log);
            WirePlayerPrefab(log);
            ASHMenuInstaller.RefreshRegistry();
            AssetDatabase.SaveAssets();
            log.Append(wolf ? "wolf prefab ready; " : "wolf prefab skipped (Malbers missing); ");
            log.Append(bear ? "bear prefab ready; " : "bear prefab skipped (Malbers missing); ");
            log.Append("items animalbone, animalfat, wolfpelt, bearhide; drop tables on ASH_WildlifeSettings. ");
            log.Append("Run Place Far From Camp to put them in the scene (not saved). Prefabs that reference Malbers stay untracked.");
            Debug.Log(log.ToString());
        }

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
            var wolf = AssetDatabase.LoadAssetAtPath<GameObject>(WolfPrefab);
            var bear = AssetDatabase.LoadAssetAtPath<GameObject>(BearPrefab);
            if (!wolf && !bear) { Debug.LogWarning("[ASH] Wildlife: run Setup Wolves and Bears first (Malbers pack required for prefabs)."); return; }

            var terrain = Terrain.activeTerrain;
            if (!terrain) { Debug.LogWarning("[ASH] Wildlife: no terrain."); return; }

            var root = GameObject.Find(SpawnRoot);
            if (!root) root = new GameObject(SpawnRoot);
            Undo.RegisterCreatedObjectUndo(root, "ASH wildlife");

            int haveW = CountKind(root, ASHWildlifeKind.Wolf), haveB = CountKind(root, ASHWildlifeKind.Bear);
            int needW = Mathf.Max(0, s.wolfCount - haveW), needB = Mathf.Max(0, s.bearCount - haveB);
            var blocked = CollectBlocked();
            var rnd = new System.Random(s.spawnSeed != 0 ? s.spawnSeed : 17041);
            int w = PlaceKind(root, wolf, ASHWildlifeKind.Wolf, needW, s, terrain, blocked, rnd);
            int b = PlaceKind(root, bear, ASHWildlifeKind.Bear, needB, s, terrain, blocked, rnd);
            Debug.Log($"[ASH] Wildlife: placed {w} wolf(s), {b} bear(s) under {SpawnRoot} (≥ {s.campExclusionMeters:0} m from camp / protected objects). Scene not saved.");
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

        static int CountKind(GameObject root, ASHWildlifeKind kind)
        {
            int n = 0;
            foreach (var a in root.GetComponentsInChildren<ASHAnimal>(true)) if (a.kind == kind) n++;
            return n;
        }

        static int PlaceKind(GameObject root, GameObject prefab, ASHWildlifeKind kind, int need, ASHWildlifeSettings s, Terrain terrain, System.Collections.Generic.List<Vector3> blocked, System.Random rnd)
        {
            if (!prefab || need <= 0) return 0;
            var td = terrain.terrainData; var origin = terrain.GetPosition(); var size = td.size;
            int got = 0, tries = 0, max = need * 40;
            while (got < need && tries++ < max)
            {
                var p = new Vector3(origin.x + 30f + (float)rnd.NextDouble() * (size.x - 60f), 0f, origin.z + 30f + (float)rnd.NextDouble() * (size.z - 60f));
                p.y = terrain.SampleHeight(p) + origin.y;
                if (!ClearOf(p, s.campExclusionMeters, blocked)) continue;
                if (!ClearOfChildren(root, p, s.packSpacing)) continue;
                var nrm = td.GetInterpolatedNormal((p.x - origin.x) / size.x, (p.z - origin.z) / size.z);
                if (nrm.y < 0.82f) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
                go.transform.SetParent(root.transform, true);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
                var an = go.GetComponent<ASHAnimal>() ?? go.AddComponent<ASHAnimal>();
                an.kind = kind; an.settings = s;
                Undo.RegisterCreatedObjectUndo(go, "ASH wildlife");
                got++;
            }
            return got;
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

        static bool ClearOf(Vector3 p, float radius, System.Collections.Generic.List<Vector3> blocked)
        {
            float r2 = radius * radius;
            for (int i = 0; i < blocked.Count; i++)
            {
                var d = blocked[i] - p; d.y = 0f;
                if (d.sqrMagnitude < r2) return false;
            }
            return true;
        }

        static System.Collections.Generic.List<Vector3> CollectBlocked()
        {
            var list = new System.Collections.Generic.List<Vector3>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (!t) continue;
                if (IsProtected(t)) list.Add(t.position);
            }
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

        static bool BuildWolf(StringBuilder log)
        {
            var src = LoadFirst(WolfSrcA, WolfSrcB, WolfSrcC);
            if (!src) { log.Append("Malbers wolf prefab missing; "); return false; }
            return BuildWrapper(src, WolfPrefab, ASHWildlifeKind.Wolf, false, log);
        }

        static bool BuildBear(StringBuilder log)
        {
            var src = LoadFirst(BearSrcA, BearSrcB);
            if (!src) { log.Append("Malbers bear prefab missing; "); return false; }
            return BuildWrapper(src, BearPrefab, ASHWildlifeKind.Bear, true, log);
        }

        static GameObject LoadFirst(params string[] paths)
        {
            foreach (var p in paths) { var g = AssetDatabase.LoadAssetAtPath<GameObject>(p); if (g) return g; }
            return null;
        }

        static bool BuildWrapper(GameObject src, string dest, ASHWildlifeKind kind, bool addMotor, StringBuilder log)
        {
            ASHMenuInstaller.Folder(PrefabDir);
            var root = PrefabUtility.InstantiatePrefab(src) as GameObject;
            if (!root) return false;
            root.name = kind == ASHWildlifeKind.Bear ? "ASH_Wildlife_Bear" : "ASH_Wildlife_Wolf";
            var animal = root.GetComponent<ASHAnimal>() ?? root.AddComponent<ASHAnimal>();
            animal.kind = kind;
            animal.settings = AssetDatabase.LoadAssetAtPath<ASHWildlifeSettings>(SettingsPath);
            if (addMotor && !root.GetComponent<CharacterController>())
            {
                var cc = root.AddComponent<CharacterController>();
                cc.height = 1.6f; cc.radius = 0.55f; cc.center = new Vector3(0f, 0.85f, 0f);
            }
            if (!HasCollider(root))
            {
                var cap = root.AddComponent<CapsuleCollider>();
                cap.height = 1.6f; cap.radius = 0.55f; cap.center = new Vector3(0f, 0.85f, 0f);
            }
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                if (tr) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject);
            }
            PrefabUtility.SaveAsPrefabAsset(root, dest);
            Object.DestroyImmediate(root);
            log.Append(Path.GetFileName(dest) + " written; ");
            return true;
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

        static void FillDrops(ASHWildlifeSettings.Species sp, ASHItemDefinition meat, ASHItemDefinition hide, ASHItemDefinition bone, ASHItemDefinition fat, bool wolf)
        {
            if (sp.drops == null) sp.drops = new System.Collections.Generic.List<ASHWildlifeSettings.Drop>();
            if (sp.drops.Count > 0) return;
            if (wolf)
            {
                AddDrop(sp, meat, 1, 2, 1f);
                AddDrop(sp, hide, 1, 1, 0.9f);
                AddDrop(sp, bone, 1, 2, 1f);
                AddDrop(sp, fat, 0, 1, 0.7f);
            }
            else
            {
                AddDrop(sp, meat, 2, 4, 1f);
                AddDrop(sp, hide, 1, 1, 1f);
                AddDrop(sp, bone, 2, 4, 1f);
                AddDrop(sp, fat, 1, 3, 1f);
            }
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

        static ASHItemDefinition EnsureItem(string id, string name, string desc, string letters, int category, int stack, int sell)
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
            if (id == "animalbone") return "AnimalBones";
            if (id == "animalfat") return "AnimalFat";
            if (id == "wolfpelt") return "WolfPelt";
            if (id == "bearhide") return "BearHide";
            return id;
        }

        static Sprite EnsureLetter(string path, string letters)
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
            // very small block-letter glyphs so Rook can replace them later
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
                case 'F': B(0, 0, 16, 176); B(0, 160, 76, 16); B(0, 88, 56, 16); break;
                case 'H': B(0, 0, 16, 176); B(64, 0, 16, 176); B(0, 80, 80, 16); break;
                case 'P': B(0, 0, 16, 176); B(0, 160, 64, 16); B(0, 88, 64, 16); B(64, 104, 16, 56); break;
                case 'W': B(0, 0, 16, 176); B(72, 0, 16, 176); B(24, 0, 16, 80); B(48, 0, 16, 80); break;
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
