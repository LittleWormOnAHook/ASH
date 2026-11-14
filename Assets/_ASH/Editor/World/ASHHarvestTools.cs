using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.EditorTools.Build;
using ASH.Interaction;
using ASH.Inventory;
using ASH.World;
using ASH.World.Accumulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// Wood + plant resources from vendor art (vendor files untouched; wrappers nest the vendor prefab):
    /// - Pickups (Assets/_ASH/Prefabs/Pickups): ASH_Deadwood_Branches01-03 (HQ branches; same node data as ASH_Deadwood),
    ///   ASH_Fiber_* (ANGRY MESH plants/grass; same node data as ASH_FiberGrass, foliage accumulation look).
    /// - Axe harvestables (Assets/_ASH/Prefabs/Harvest): dead firs (fall -> logs -> stump), HQ downed dead tree (-> logs), HQ stump, log.
    /// - Scene swaps, additive resource scatter, foliage materials, wood items/recipes, icons.
    /// Menus under ASH/World/Resources.
    /// </summary>
    public static class ASHHarvestTools
    {
        public const string PickupDir = ASHContentBuilder.Root + "/Prefabs/Pickups";
        public const string HarvestDir = ASHContentBuilder.Root + "/Prefabs/Harvest";
        public const string PlainMatDir = ASHContentBuilder.Root + "/Materials/Plain";
        public const string AudioDir = ASHContentBuilder.Root + "/Audio/Harvest";
        public const string SettingsPath = ASHContentBuilder.Root + "/Data/World/ASH_ResourceScatterSettings.asset";
        const string HQ = "Assets/HQ Survival Pack/Prefabs/";
        const string AM = "Assets/ANGRY MESH/Nature Pack - PBR Rocks/Prefabs Nature/Plants and Grass/";
        const string NM = "Assets/NatureManufacture Assets/Dynamic Nature - Mountain Tree Pack/Prefabs/Standard/";
        public const string ScatterRoot = "ASH_ResourceScatter";

        static readonly string[] Branches = { "Branches01", "Branches02", "Branches03" };
        static readonly string[] Plants = { "Fern_01", "Grass_Group_01", "Grass_Group_02" };
        static string P(string dir, string name) => $"{dir}/{name}.prefab";
        public static string BranchPickup(int i) => P(PickupDir, "ASH_Deadwood_" + Branches[i]);
        public static string PlantPickup(int i) => P(PickupDir, "ASH_Fiber_" + Plants[i].Replace("_", ""));
        public static readonly string LogPath = P(HarvestDir, "ASH_Harvest_Log"), StumpPath = P(HarvestDir, "ASH_Harvest_Stump"),
            DownedPath = P(HarvestDir, "ASH_Harvest_DeadTreeDowned"), FirAPath = P(HarvestDir, "ASH_Harvest_DeadFir_A"), FirBPath = P(HarvestDir, "ASH_Harvest_DeadFir_B");

        // ------------------------------------------------------------------ menus
        [MenuItem("ASH/World/Resources/1. Build Wood + Plant Content (items, prefabs, foliage mats, icons)", priority = 70)]
        public static void BuildContent()
        {
            var log = new StringBuilder("[ASH] Resources content: ");
            ASHItemCatalog.BuildWoodContent();
            log.Append(FoliageMaterials());
            BuildPrefabs(log);
            Settings();
            BakeIcons(log);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        [MenuItem("ASH/World/Resources/2. Replace Scene Deadwood, Fiber Grass, Dead Trees", priority = 71)]
        public static void ReplaceInSceneMenu() { Debug.Log(ReplaceInScene()); }

        [MenuItem("ASH/World/Resources/Scatter All (additive)", priority = 80)] public static void ScatterAll() => Debug.Log(Scatter(null));
        [MenuItem("ASH/World/Resources/Scatter Wood (additive)", priority = 81)] public static void ScatterWood() => Debug.Log(Scatter(ASHResourceScatterSettings.Group.Wood));
        [MenuItem("ASH/World/Resources/Scatter Plants (additive)", priority = 82)] public static void ScatterPlants() => Debug.Log(Scatter(ASHResourceScatterSettings.Group.Plants));
        [MenuItem("ASH/World/Resources/Clear Scattered Wood", priority = 90)] public static void ClearWood() => Clear(ASHResourceScatterSettings.Group.Wood);
        [MenuItem("ASH/World/Resources/Clear Scattered Plants", priority = 91)] public static void ClearPlants() => Clear(ASHResourceScatterSettings.Group.Plants);
        [MenuItem("ASH/World/Resources/Clear All Scattered", priority = 92)] public static void ClearAll() { Clear(ASHResourceScatterSettings.Group.Wood); Clear(ASHResourceScatterSettings.Group.Plants); }
        [MenuItem("ASH/World/Resources/Select Scatter Settings", priority = 95)] public static void SelectSettings() { Selection.activeObject = Settings(); EditorGUIUtility.PingObject(Selection.activeObject); }
        [MenuItem("ASH/World/Resources/Scatter Preset/Low", priority = 96)] public static void PresetLow() => Preset(ASHResourceScatterSettings.Preset.Low);
        [MenuItem("ASH/World/Resources/Scatter Preset/Default", priority = 97)] public static void PresetDefault() => Preset(ASHResourceScatterSettings.Preset.Default);
        [MenuItem("ASH/World/Resources/Scatter Preset/Extreme", priority = 98)] public static void PresetExtreme() => Preset(ASHResourceScatterSettings.Preset.Extreme);
        static void Preset(ASHResourceScatterSettings.Preset p) { var s = Settings(); s.ApplyPreset(p); EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s); Debug.Log($"[ASH] Resource scatter preset {p}: " + string.Join(", ", s.entries.Select(e => (e.prefab ? e.prefab.name : "?") + " " + e.count))); }

        /// <summary>exec: everything in order (content, scene swaps, one default scatter, save scene).</summary>
        public static void RunAll()
        {
            BuildContent();
            Debug.Log(ReplaceInScene());
            ClearAll();
            Debug.Log(Scatter(null));
            var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.SaveScene(scene);
            Debug.Log("[ASH] RESOURCES DONE (scene saved: " + scene.path + ")");
        }

        // ------------------------------------------------------------------ foliage materials (ANGRY MESH plants, in place: approved)
        public const string FoliageBackupNote = "C:/Users/Teabagger/ash_xfer/backup_am_plant_mats";
        static string FoliageMaterials()
        {
            var sh = Shader.Find(ASHAccumulation.ShaderName); if (!sh) return "ASH shader missing; ";
            var sb = new StringBuilder("foliage mats: ");
            var done = new HashSet<Material>();
            foreach (var name in Plants)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AM + name + ".prefab"); if (!go) { sb.Append($"missing {name}; "); continue; }
                float h = 0.6f; var rs = go.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); h = Mathf.Max(0.15f, b.size.y); }
                foreach (var r in rs) foreach (var m in r.sharedMaterials)
                {
                    if (!m || !done.Add(m)) continue;
                    var path = AssetDatabase.GetAssetPath(m); if (!path.StartsWith("Assets/ANGRY MESH")) { sb.Append($"{m.name} skipped (not ANGRY MESH); "); continue; }
                    string was = m.shader.name;
                    bool clip = m.IsKeywordEnabled("_ALPHATEST_ON") || (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) || (m.renderQueue >= 2450 && m.renderQueue < 2900);
                    float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
                    var albA = m.HasProperty("_AlbedoAOpacity") ? m.GetTexture("_AlbedoAOpacity") : SavedTex(m, "_AlbedoAOpacity");
                    var nrm = m.HasProperty("_NormalMap") ? m.GetTexture("_NormalMap") : SavedTex(m, "_NormalMap"); // read before conversion (CopyPropertiesFromMaterial drops them)
                    if (was != ASHAccumulation.ShaderName)
                    {
                        if (was == ASHAccumulation.SourceShaderName) m.shader = sh;
                        else { var v = ASHAccumulation.CreateVariant(m); if (!v) { sb.Append($"{m.name} ({was}) not convertible; "); continue; } m.shader = sh; m.CopyPropertiesFromMaterial(v); Object.DestroyImmediate(v); }
                    }
                    // ANGRY MESH grass shader: albedo+opacity in _AlbedoAOpacity, normal in _NormalMap (read from the saved properties, also after conversion)
                    if (albA) { m.SetTexture("_BaseMap", albA); m.SetTexture("_MainTex", albA); clip = true; }
                    if (nrm) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
                    clip = true; // foliage cards always need the albedo alpha cut (vendor grass shader clipped implicitly)
                    { m.SetFloat("_Smoothness", 0.12f); m.SetFloat("_Metallic", 0f); m.SetTexture("_MetallicGlossMap", null); m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.SetTexture("_OcclusionMap", null); m.DisableKeyword("_OCCLUSIONMAP"); }
                    if (clip) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", cutoff); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450; m.SetOverrideTag("RenderType", "TransparentCutout"); }
                    m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; m.enableInstancing = true;
                    m.SetFloat("_ClearCoatMask", 1f);          // ASH foliage mode: top-facing coverage on blades (see ASHLitForwardPass)
                    m.SetFloat("_ClearCoatSmoothness", h);    // foliage height (m): coverage grows towards the tips
                    EditorUtility.SetDirty(m);
                    sb.Append($"{m.name} ({was} -> ASH foliage, clip {clip} cutoff {cutoff:F2}, albedo {(albA ? albA.name : "base")}, normal {(nrm ? nrm.name : "-")}, h {h:F2}); ");
                }
            }
            return sb.ToString();
        }

        /// <summary>Texture saved on the material asset under a property the current shader may not have (reads the .mat YAML).</summary>
        static Texture SavedTex(Material m, string name)
        {
            var path = AssetDatabase.GetAssetPath(m); if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var mt = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), "- " + name + @":\s*\n\s*m_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})");
            return mt.Success ? AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(mt.Groups[1].Value)) : null;
        }
        static float SavedFloat(Material m, string name, float def)
        {
            var fl = new SerializedObject(m).FindProperty("m_SavedProperties.m_Floats");
            for (int i = 0; fl != null && i < fl.arraySize; i++) { var e = fl.GetArrayElementAtIndex(i); if (e.FindPropertyRelative("first").stringValue == name) return e.FindPropertyRelative("second").floatValue; }
            return def;
        }

        /// <summary>exec: re-run the foliage material setup and re-bake the plant fiber icon.</summary>
        public static void FixFoliage()
        {
            var log = new StringBuilder("[ASH] Foliage fix: "); log.Append(FoliageMaterials());
            var saved = Shader.GetGlobalVector("_ASH_Layers"); Shader.SetGlobalVector("_ASH_Layers", Vector4.zero);
            try { ASHIconBaker.BakeAll(new[] { "ASH_Item_PlantFiber" }); } finally { Shader.SetGlobalVector("_ASH_Layers", saved); }
            AssetDatabase.SaveAssets(); Debug.Log(log.ToString());
        }

        // ------------------------------------------------------------------ prefabs
        static void BuildPrefabs(StringBuilder log)
        {
            ASHBuildUtil.Folder(PickupDir); ASHBuildUtil.Folder(HarvestDir); ASHBuildUtil.Folder(PlainMatDir);
            var deadwoodNode = AssetDatabase.LoadAssetAtPath<GameObject>(ASHContentBuilder.WorldPrefabs + "/ASH_Deadwood.prefab")?.GetComponent<ASHResourceNode>();
            var fiberNode = AssetDatabase.LoadAssetAtPath<GameObject>(ASHContentBuilder.WorldPrefabs + "/ASH_FiberGrass.prefab")?.GetComponent<ASHResourceNode>();
            if (!deadwoodNode || !fiberNode) { log.Append("ERROR: ASH_Deadwood / ASH_FiberGrass node missing; "); return; }
            var chops = Enumerable.Range(1, 4).Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/ASH_Chop_0{i}.wav")).Where(c => c).ToArray();
            var fall = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/ASH_TreeFall_01.wav");
            var firewood = ASHContentBuilder.LoadItem("ASH_Item_Firewood"); var kindling = ASHContentBuilder.LoadItem("ASH_Item_Kindling");
            ASHRecipeIngredient Y(ASHItemDefinition it, int n) => new ASHRecipeIngredient { item = it, amount = n };

            for (int i = 0; i < Branches.Length; i++)
                Wrap(HQ + "Environment/Branches/" + Branches[i] + ".prefab", BranchPickup(i), true, true, true, true, (root, vis) =>
                { var n = root.AddComponent<ASHResourceNode>(); EditorUtility.CopySerialized(deadwoodNode, n); ASHBuildUtil.Set(n, "visualRoot", vis); }, log);
            for (int i = 0; i < Plants.Length; i++)
                Wrap(AM + Plants[i] + ".prefab", PlantPickup(i), false, false, true, true, (root, vis) =>
                { var n = root.AddComponent<ASHResourceNode>(); EditorUtility.CopySerialized(fiberNode, n); ASHBuildUtil.Set(n, "visualRoot", vis); }, log);

            Wrap(HQ + "Decoration/Logs/Static/Log_Static.prefab", LogPath, true, true, false, true, (root, vis) =>
            { var c = root.AddComponent<ASHChoppable>(); c.Configure(ASHChopKind.Log, "Log", 3, 0.7f, kindling, 0.3f, Y(firewood, 3), Y(kindling, 1)); c.ConfigurePresentation(vis, chops, fall); }, log);
            Wrap(HQ + "Environment/Branches/TreeStump.prefab", StumpPath, true, true, false, true, (root, vis) =>
            { var c = root.AddComponent<ASHChoppable>(); c.Configure(ASHChopKind.Stump, "Stump", 4, 0.7f, kindling, 0.3f, Y(firewood, 2), Y(kindling, 2)); c.ConfigurePresentation(vis, chops, fall); }, log);
            var logPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LogPath); var stumpPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StumpPath);
            Wrap(HQ + "Environment/Branches/DeadTree.prefab", DownedPath, true, true, false, true, (root, vis) =>
            {
                var c = root.AddComponent<ASHChoppable>(); c.Configure(ASHChopKind.DownedTree, "Dead Tree", 5, 0.7f, kindling, 0.3f, Y(kindling, 2));
                c.ConfigureSplit(logPrefab, 2.5f, 2, 1f, null, 2f); c.ConfigurePresentation(vis, chops, fall);
            }, log);
            foreach (var (src, dst) in new[] { (NM + "Fir_08_Dead_Forest.prefab", FirAPath), (NM + "Fir_08_Dead_Forest_cheap.prefab", FirBPath) })
                Wrap(src, dst, false, true, false, false, (root, vis) =>
                {
                    var c = root.AddComponent<ASHChoppable>(); c.Configure(ASHChopKind.StandingTree, "Dead Fir", 8, 0.75f, kindling, 0.2f, Y(kindling, 1));
                    c.ConfigureSplit(logPrefab, 2.5f, 4, 0.6f, stumpPrefab, 2.6f); c.ConfigurePresentation(vis, chops, fall);
                }, log);
        }

        /// <summary>Root (Resource layer, interaction component) / Visual / nested vendor prefab instance.</summary>
        static void Wrap(string vendor, string outPath, bool stripAccum, bool plainMats, bool trigger, bool baseToGround, System.Action<GameObject, GameObject> configure, StringBuilder log)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(vendor); if (!src) { log.Append($"MISSING {vendor}; "); return; }
            var root = new GameObject(Path.GetFileNameWithoutExtension(outPath));
            var vis = new GameObject("Visual"); vis.transform.SetParent(root.transform, false);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src); inst.transform.SetParent(vis.transform, false);
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
            int stripped = 0, plain = 0;
            if (stripAccum) foreach (var a in inst.GetComponentsInChildren<ASHAccumulation>(true)) { a.Revert(); Object.DestroyImmediate(a); stripped++; }
            if (plainMats) foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials; bool ch = false;
                for (int i = 0; i < ms.Length; i++) if (ms[i] && ms[i].shader.name == ASHAccumulation.ShaderName) { ms[i] = PlainCopy(ms[i]); ch = true; plain++; }
                if (ch) r.sharedMaterials = ms;
            }
            var rs = inst.GetComponentsInChildren<Renderer>(true); Bounds b = new Bounds(root.transform.position, Vector3.one * 0.3f);
            if (rs.Length > 0) { b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); }
            if (baseToGround) { vis.transform.localPosition = new Vector3(0, -b.min.y, 0); b.center += new Vector3(0, -b.min.y, 0); }
            bool solid = inst.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger);
            if (trigger)
            {
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) c.enabled = false; // pickups: one light trigger only
                var bc = root.AddComponent<BoxCollider>(); bc.isTrigger = true;
                bc.size = new Vector3(Mathf.Max(0.35f, b.size.x), Mathf.Max(0.35f, b.size.y), Mathf.Max(0.35f, b.size.z)); bc.center = new Vector3(b.center.x, bc.size.y * 0.5f, b.center.z);
            }
            else if (!solid) { var bc = root.AddComponent<BoxCollider>(); bc.size = Vector3.Max(b.size, Vector3.one * 0.3f); bc.center = b.center; }
            configure(root, vis);
            int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) ASHBuildUtil.SetLayerRecursive(root, layer);
            foreach (var r in rs) { r.shadowCastingMode = trigger && !stripAccum ? UnityEngine.Rendering.ShadowCastingMode.Off : r.shadowCastingMode; }
            ASHBuildUtil.SavePrefab(root, outPath);
            log.Append($"{Path.GetFileName(outPath)} <- {Path.GetFileName(vendor)} (accum stripped {stripped}, plain mats {plain}, {(trigger ? "trigger" : solid ? "vendor collider" : "box")}); ");
        }

        /// <summary>Plain URP Lit copy of an ASH accumulation material (interactables never get buildup).</summary>
        static Material PlainCopy(Material m)
        {
            var path = $"{PlainMatDir}/{m.name.Replace("_Accum", "")}_Plain.mat";
            var ex = AssetDatabase.LoadAssetAtPath<Material>(path); if (ex) return ex;
            var c = new Material(m) { shader = Shader.Find(ASHAccumulation.SourceShaderName) }; c.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(c, path); return c;
        }

        // ------------------------------------------------------------------ icons
        static void BakeIcons(StringBuilder log)
        {
            var only = new[] { "ASH_Item_Deadwood", "ASH_Item_Firewood", "ASH_Item_Kindling", "ASH_Item_PlantFiber" };
            var saved = Shader.GetGlobalVector("_ASH_Layers");
            Shader.SetGlobalVector("_ASH_Layers", Vector4.zero); // clean icons (no snow on the foliage material)
            try { ASHIconBaker.BakeAll(only); } finally { Shader.SetGlobalVector("_ASH_Layers", saved); }
            log.Append("icons: " + string.Join(", ", only.Select(f => f + "=" + (ASHContentBuilder.LoadItem(f) && ASHContentBuilder.LoadItem(f).icon ? ASHContentBuilder.LoadItem(f).icon.name : "none"))) + "; ");
        }

        // ------------------------------------------------------------------ scene swaps
        static List<GameObject> Instances(string prefabPath) =>
            Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(t => t.gameObject)
                .Where(g => g.scene.IsValid() && PrefabUtility.IsOutermostPrefabInstanceRoot(g) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == prefabPath)
                .OrderBy(g => g.transform.position.x).ThenBy(g => g.transform.position.z).ToList();

        static GameObject Swap(GameObject old, string prefabPath, Vector3? pos = null, Quaternion? rot = null, Vector3? scale = null)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath); if (!p) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p, old.scene);
            Undo.RegisterCreatedObjectUndo(go, "Replace resources");
            go.transform.SetParent(old.transform.parent, false);
            go.transform.SetPositionAndRotation(pos ?? old.transform.position, rot ?? old.transform.rotation);
            go.transform.localScale = scale ?? old.transform.localScale;
            go.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
            return go;
        }

        static float Ground(Vector3 p) { var t = Terrain.activeTerrain; return t ? t.SampleHeight(p) + t.GetPosition().y : p.y; }

        public static string ReplaceInScene()
        {
            var sb = new StringBuilder("[ASH] Resource scene swaps: "); var rnd = new System.Random(68);
            float Yaw() => (float)rnd.NextDouble() * 360f;
            int n = 0;
            foreach (var old in Instances(ASHContentBuilder.WorldPrefabs + "/ASH_Deadwood.prefab"))
            { var g = Swap(old, BranchPickup(n % 3), null, Quaternion.Euler(0, Yaw(), 0), Vector3.one); if (g) { Undo.DestroyObjectImmediate(old); n++; } }
            sb.Append($"deadwood -> branches {n}; ");
            n = 0;
            foreach (var old in Instances(ASHContentBuilder.WorldPrefabs + "/ASH_FiberGrass.prefab"))
            { var g = Swap(old, PlantPickup(n % 3), null, Quaternion.Euler(0, Yaw(), 0), Vector3.one); if (g) { Undo.DestroyObjectImmediate(old); n++; } }
            sb.Append($"fiber grass -> ANGRY MESH plants {n}; ");
            // ASH placeholder dead trees (primitives) -> harvestable dead firs, every 4th a downed dead tree
            n = 0; int downed = 0;
            foreach (var old in Instances(ASHContentBuilder.WorldPrefabs + "/ASH_DeadTree.prefab"))
            {
                var pos = old.transform.position; pos.y = Ground(pos);
                bool down = n % 4 == 3; float s = 0.48f + (float)rnd.NextDouble() * 0.17f;
                var g = Swap(old, down ? DownedPath : (n % 2 == 0 ? FirAPath : FirBPath), pos, Quaternion.Euler(0, Yaw(), 0), down ? Vector3.one : Vector3.one * s);
                if (g) { Undo.DestroyObjectImmediate(old); n++; if (down) downed++; }
            }
            sb.Append($"placeholder dead trees -> harvestable {n} ({downed} downed); ");
            // vendor HQ dead tree / stump placed in the scene -> harvestable at the same spot; the dead tree also gets 2 standing dead firs nearby
            foreach (var (vendor, dst) in new[] { (HQ + "Environment/Branches/DeadTree.prefab", DownedPath), (HQ + "Environment/Trees/DeadTree.prefab", DownedPath), (HQ + "Environment/Branches/TreeStump.prefab", StumpPath), (HQ + "Environment/Trees/TreeStump.prefab", StumpPath) })
                foreach (var old in Instances(vendor))
                {
                    var t = old.transform; string where = t.parent ? t.parent.name : "(root)";
                    var g = Swap(old, dst); if (!g) continue;
                    sb.Append($"{Path.GetFileName(vendor)} at {t.position} (parent {where}) -> {Path.GetFileName(dst)}; ");
                    if (dst == DownedPath)
                        for (int k = 0; k < 2; k++)
                        {
                            float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = 4.5f + (float)rnd.NextDouble() * 3f;
                            var p = t.position + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r); p.y = Ground(p);
                            var f = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(k == 0 ? FirAPath : FirBPath), old.scene);
                            Undo.RegisterCreatedObjectUndo(f, "Replace resources"); f.transform.SetParent(t.parent, false);
                            f.transform.SetPositionAndRotation(p, Quaternion.Euler(0, Yaw(), 0)); f.transform.localScale = Vector3.one * (0.5f + 0.15f * k);
                            sb.Append($"+dead fir at {p}; ");
                        }
                    Undo.DestroyObjectImmediate(old);
                }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return sb.ToString();
        }

        // ------------------------------------------------------------------ scatter
        public static ASHResourceScatterSettings Settings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHResourceScatterSettings>(SettingsPath);
            if (s) return s;
            s = ScriptableObject.CreateInstance<ASHResourceScatterSettings>();
            void E(string path, ASHResourceScatterSettings.Group g, int count, float ridge, float gully, float prairie, float creek, float min, float max, float slope, bool align)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!p) return;
                s.entries.Add(new ASHResourceScatterSettings.Entry { prefab = p, group = g, count = count, defaultCount = count, ridge = ridge, gully = gully, prairie = prairie, creek = creek, minScale = min, maxScale = max, maxSlope = slope, alignToGround = align });
            }
            for (int i = 0; i < 3; i++) E(BranchPickup(i), ASHResourceScatterSettings.Group.Wood, 80, 0.8f, 1f, 0.15f, 0.5f, 0.85f, 1.2f, 32f, true);
            E(StumpPath, ASHResourceScatterSettings.Group.Wood, 40, 1f, 0.8f, 0.1f, 0.3f, 0.85f, 1.25f, 28f, false);
            E(DownedPath, ASHResourceScatterSettings.Group.Wood, 20, 0.9f, 1f, 0.1f, 0.4f, 0.9f, 1.15f, 20f, true);
            for (int i = 0; i < 3; i++) E(PlantPickup(i), ASHResourceScatterSettings.Group.Plants, 120, 0.4f, 1f, 0.6f, 1f, 0.8f, 1.25f, 35f, true);
            ASHBuildUtil.Folder(Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(s, SettingsPath); AssetDatabase.SaveAssetIfDirty(s);
            return s;
        }

        static Transform GroupRoot(ASHResourceScatterSettings.Group g, bool create)
        {
            var scene = EditorSceneManager.GetActiveScene();
            var root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == ScatterRoot);
            if (!root) { if (!create) return null; root = new GameObject(ScatterRoot); Undo.RegisterCreatedObjectUndo(root, "Resource scatter"); }
            var t = root.transform.Find(g.ToString());
            if (!t && create) { t = new GameObject(g.ToString()).transform; t.SetParent(root.transform, false); Undo.RegisterCreatedObjectUndo(t.gameObject, "Resource scatter"); }
            return t;
        }

        public static void Clear(ASHResourceScatterSettings.Group g)
        {
            var t = GroupRoot(g, false); if (!t) return;
            int n = t.childCount; Undo.DestroyObjectImmediate(t.gameObject);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[ASH] Resource scatter: cleared {g} ({n})");
        }

        public static string Scatter(ASHResourceScatterSettings.Group? only)
        {
            var terrain = Terrain.activeTerrain; if (!terrain) return "[ASH] Resource scatter: no terrain";
            var s = Settings(); var d = terrain.terrainData; var origin = terrain.GetPosition(); var size = d.size;
            var ctx = new ASHVegetationScatter.Ctx(terrain, ASHVegetationScatter.Settings());
            var vs = ctx.s; var rnd = new System.Random(s.seed != 0 ? s.seed : System.Environment.TickCount);
            // terrain trees (Anthony's + ASH): 8 m hash of trunk positions
            var trees = new Dictionary<long, List<Vector2>>(); const float TB = 8f;
            long Key(int x, int z) => ((long)z << 32) ^ (uint)x;
            foreach (var ti in d.treeInstances)
            {
                var w = new Vector2(origin.x + ti.position.x * size.x, origin.z + ti.position.z * size.z);
                var k = Key(Mathf.FloorToInt(w.x / TB), Mathf.FloorToInt(w.y / TB)); if (!trees.TryGetValue(k, out var l)) trees[k] = l = new List<Vector2>(); l.Add(w);
            }
            float TreeDist(Vector2 p, float max)
            {
                float best = 1e9f; int r = Mathf.CeilToInt(max / TB), bx = Mathf.FloorToInt(p.x / TB), bz = Mathf.FloorToInt(p.y / TB);
                for (int z = bz - r; z <= bz + r; z++) for (int x = bx - r; x <= bx + r; x++)
                    if (trees.TryGetValue(Key(x, z), out var l)) foreach (var t in l) { float dd = (t - p).magnitude; if (dd < best) best = dd; }
                return best;
            }
            // spacing against everything already scattered + scene resources
            var placed = new Dictionary<long, List<Vector2>>(); const float SB = 4f;
            void Mark(Vector2 p) { var k = Key(Mathf.FloorToInt(p.x / SB), Mathf.FloorToInt(p.y / SB)); if (!placed.TryGetValue(k, out var l)) placed[k] = l = new List<Vector2>(); l.Add(p); }
            bool Free(Vector2 p, float r)
            {
                int bx = Mathf.FloorToInt(p.x / SB), bz = Mathf.FloorToInt(p.y / SB), rr = Mathf.CeilToInt(r / SB);
                for (int z = bz - rr; z <= bz + rr; z++) for (int x = bx - rr; x <= bx + rr; x++)
                    if (placed.TryGetValue(Key(x, z), out var l)) foreach (var q in l) if ((q - p).sqrMagnitude < r * r) return false;
                return true;
            }
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) if (mb is IASHUsable) Mark(new Vector2(mb.transform.position.x, mb.transform.position.z));

            var sb = new StringBuilder("[ASH] Resource scatter: "); int total = 0;
            foreach (var e in s.entries)
            {
                if (!e.prefab || e.count <= 0 || (only.HasValue && e.group != only.Value)) continue;
                var parent = GroupRoot(e.group, true); int got = 0, tries = 0, maxTries = e.count * s.attemptsPerItem;
                int rRoute = 0, rTree = 0, rSlope = 0, rBlock = 0;
                while (got < e.count && tries++ < maxTries)
                {
                    var p = new Vector2(origin.x + 20f + (float)rnd.NextDouble() * (size.x - 40f), origin.z + 20f + (float)rnd.NextDouble() * (size.z - 40f));
                    int k = ctx.Cell(p);
                    if (ctx.blockG[k]) { rBlock++; continue; }
                    if (ctx.Slope(p) > e.maxSlope) { rSlope++; continue; }
                    if (ctx.soil[k] >= vs.routeSplatThreshold) { rRoute++; continue; }
                    ctx.RouteDist(p, out var dr, out var dt);
                    if (dr < vs.roadHalfWidth + s.roadEdgeClear || dt < vs.trailHalfWidth + s.trailClear) { rRoute++; continue; }
                    float td = TreeDist(p, s.nearTreeRadius);
                    if (td < s.minTreeDistance) { rTree++; continue; }
                    var b = ctx.Biome(p); float w = b.x * e.ridge + b.y * e.gully + b.z * e.prairie + ctx.CreekNear(p) * e.creek;
                    w *= td <= s.nearTreeRadius ? 1f : s.openGroundWeight;
                    if (rnd.NextDouble() > Mathf.Clamp01(w)) continue;
                    if (!Free(p, s.minSpacing)) continue;
                    var pos = new Vector3(p.x, 0, p.y); pos.y = terrain.SampleHeight(pos) + origin.y;
                    var yaw = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
                    var rot = yaw;
                    if (e.alignToGround) { var nrm = d.GetInterpolatedNormal((p.x - origin.x) / size.x, (p.y - origin.z) / size.z); rot = Quaternion.FromToRotation(Vector3.up, nrm) * yaw; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab, parent.gameObject.scene);
                    go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(pos, rot);
                    go.transform.localScale = Vector3.one * Mathf.Lerp(e.minScale, e.maxScale, (float)rnd.NextDouble());
                    Undo.RegisterCreatedObjectUndo(go, "Resource scatter");
                    Mark(p); got++;
                }
                total += got;
                sb.Append($"{e.prefab.name} {got}/{e.count} (rejects: route {rRoute}, tree {rTree}, slope {rSlope}, blocked {rBlock}); ");
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            sb.Insert(0, $"placed {total}. ");
            return sb.ToString();
        }
    }
}
