#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.EditorTools.Build;
using ASH.EditorTools.Systems;
using ASH.Inventory;
using ASH.Wildlife.Birds;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// ASH/Systems/Wildlife/Setup Birds (idempotent): imports the CC0 hooded-crow rig (idle / fly / peck), builds the bird material,
    /// animator and pooled prefab, creates Resources/ASH_BirdSettings with seven types (tinted / scaled variants of the one rig) and
    /// their PD calls, and adds Feathers / Raw + Cooked Small Meat with Cook Small Meat and Fletched Arrows recipes.
    /// Never touches the scene: the bird manager spawns itself at runtime. Re-run any time; existing tuning is kept.
    /// </summary>
    public static class ASHBirdSetup
    {
        const string ArtDir = "Assets/_ASH/Art/Wildlife/Birds";
        const string FbxPath = ArtDir + "/ASH_Bird_Crow.fbx";
        const string ColorPath = ArtDir + "/ASH_Bird_Crow_color.png";
        const string NormalPath = ArtDir + "/ASH_Bird_Crow_normal.png";
        const string RoughPath = ArtDir + "/ASH_Bird_Crow_rough.png";
        const string MatPath = ArtDir + "/ASH_M_Bird_Crow.mat";
        const string CtrlPath = ArtDir + "/ASH_Bird.controller";
        const string PrefabDir = "Assets/_ASH/Prefabs/Birds";
        const string PrefabPath = PrefabDir + "/ASH_Bird.prefab";
        const string AudioDir = "Assets/_ASH/Audio/Birds";
        const string SettingsPath = "Assets/_ASH/Resources/ASH_BirdSettings.asset";
        static readonly string[] Clips = { "idle", "fly", "peck" };

        [MenuItem("ASH/Systems/Wildlife/Setup Birds (idempotent)", false, 266)]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }
            if (!File.Exists(FbxPath)) { Debug.LogError("[ASH] Birds: missing " + FbxPath); return; }
            ImportTextures();
            ImportModel();
            var mat = EnsureMaterial();
            var ctrl = EnsureController();
            var prefab = BuildPrefab(mat, ctrl);
            var items = EnsureItemsAndRecipes();
            var s = EnsureSettings(prefab, items.feather, items.meat);
            EnsureSpotPrefab("ASH_Bird_AggressiveCrowSpot", "crow");
            EnsureSpotPrefab("ASH_Bird_AggressiveRavenSpot", "raven");
            EnsureSpotPrefab("ASH_Bird_FlockSpot", "");
            EnsureRoostPrefab("ASH_Bird_CrowRoostSpot", ASHBirdRoostSpot.Style.Crow);
            EnsureRoostPrefab("ASH_Bird_GullRoostSpot", ASHBirdRoostSpot.Style.Seagull);
            ASHBirdVendorSetup.Run(s);
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] Birds ready: prefab {PrefabPath}, {s.types.Count} types, settings {SettingsPath}. Items: feather, rawsmallmeat, cookedsmallmeat. Recipes: cooksmallmeat, fletchedarrows. Spots: {PrefabDir}/ASH_Bird_FlockSpot, AggressiveCrowSpot / RavenSpot, CrowRoostSpot / GullRoostSpot.");
        }

        // ------------------------------------------------------------------ import
        static void ImportTextures()
        {
            void Tex(string path, bool normal, bool srgb)
            {
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!ti) return;
                bool dirty = false;
                var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != want) { ti.textureType = want; dirty = true; }
                if (ti.sRGBTexture != srgb && !normal) { ti.sRGBTexture = srgb; dirty = true; }
                if (ti.maxTextureSize > 1024) { ti.maxTextureSize = 1024; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            Tex(ColorPath, false, true);
            Tex(NormalPath, true, false);
            Tex(RoughPath, false, false);
        }

        static void ImportModel()
        {
            var mi = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (!mi) return;
            bool dirty = false;
            if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; dirty = true; }
            if (mi.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
            if (!mi.importAnimation) { mi.importAnimation = true; dirty = true; }
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (mi.importCameras || mi.importLights) { mi.importCameras = false; mi.importLights = false; dirty = true; }
            var src = mi.clipAnimations != null && mi.clipAnimations.Length > 0 ? mi.clipAnimations : mi.defaultClipAnimations;
            var list = new List<ModelImporterClipAnimation>();
            foreach (var c in src)
            {
                string want = null;
                foreach (var n in Clips) if ((c.takeName ?? c.name ?? "").ToLowerInvariant().EndsWith(n)) want = n;
                if (want == null) continue;
                if (c.name != want || !c.loopTime || c.lockRootPositionXZ == false)
                {
                    c.name = want; c.loopTime = true; c.loopPose = want != "peck";
                    c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;
                    dirty = true;
                }
                list.Add(c);
            }
            if (list.Count > 0 && (mi.clipAnimations == null || mi.clipAnimations.Length != list.Count)) dirty = true;
            if (dirty) { mi.clipAnimations = list.ToArray(); mi.SaveAndReimport(); }
        }

        static Material EnsureMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (!m) { m = new Material(sh) { name = "ASH_M_Bird_Crow" }; AssetDatabase.CreateAsset(m, MatPath); }
            var col = AssetDatabase.LoadAssetAtPath<Texture2D>(ColorPath);
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
            if (col) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", col); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", col); }
            if (nrm && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static AnimatorController EnsureController()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(CtrlPath) ?? AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
            var clips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            var sm = ctrl.layers[0].stateMachine;
            foreach (var n in Clips)
            {
                var clip = clips.FirstOrDefault(c => c.name == n);
                var st = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == n) ?? sm.AddState(n);
                if (clip) st.motion = clip;
                st.writeDefaultValues = true;
            }
            var idle = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == "idle");
            if (idle) sm.defaultState = idle;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static GameObject BuildPrefab(Material mat, AnimatorController ctrl)
        {
            ASHMenuInstaller.Folder(PrefabDir);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var root = new GameObject("ASH_Bird");
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                inst.transform.SetParent(root.transform, false);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    if (mats.Length == 0) mats = new[] { mat };
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    if (r is SkinnedMeshRenderer smr) { smr.updateWhenOffscreen = false; smr.quality = SkinQuality.Bone2; }
                }
                var anim = inst.GetComponentInChildren<Animator>(); if (!anim) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;

                // face +Z: head bone in front of the tail bone
                Transform head = null, tail = null, hips = null;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                {
                    var n = t.name.ToLowerInvariant();
                    if (!head && n == "head") head = t;
                    if (!tail && n == "tail") tail = t;
                    if (!hips && n == "hips") hips = t;
                }
                if (head && tail)
                {
                    var d = root.transform.InverseTransformPoint(head.position) - root.transform.InverseTransformPoint(tail.position);
                    d.y = 0f;
                    if (d.sqrMagnitude > 1e-6f) inst.transform.localRotation = Quaternion.AngleAxis(Vector3.SignedAngle(d.normalized, Vector3.forward, Vector3.up), Vector3.up) * inst.transform.localRotation;
                }
                // real crow size (~0.6 m mesh extent) and feet on the root
                var b = Bounds(inst);
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (size > 0.0001f && (size < 0.3f || size > 1.2f)) { inst.transform.localScale *= 0.6f / size; b = Bounds(inst); }
                inst.transform.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);

                var bird = root.AddComponent<ASHBird>();
                bird.animator = anim;
                bird.placedTypeId = "crow";
                var col = root.AddComponent<SphereCollider>();
                float hy = hips ? root.transform.InverseTransformPoint(hips.position).y : 0.13f;
                col.center = new Vector3(0f, Mathf.Max(0.08f, hy + 0.03f), 0f);
                col.radius = 0.17f;
                bird.body = col;
                bird.bodyRadius = col.radius;
                bird.harvestRadius = 0.35f;
                var rb = root.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.useGravity = false;
                int enemy = LayerMask.NameToLayer("Enemy");
                if (enemy >= 0) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = enemy;
                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                return saved;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Bounds Bounds(GameObject go)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                Bounds rb;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh)
                {
                    var mb = smr.sharedMesh.bounds;
                    rb = TransformBounds(mb, smr.rootBone ? smr.transform : smr.transform, go.transform.parent);
                }
                else rb = r.bounds;
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            return b;
        }

        static Bounds TransformBounds(Bounds local, Transform from, Transform to)
        {
            var b = new Bounds();
            bool first = true;
            for (int i = 0; i < 8; i++)
            {
                var c = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var w = from.TransformPoint(c);
                var p = to ? to.InverseTransformPoint(w) : w;
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
            return b;
        }

        /// <summary>Drag-in roost spot: spawns a resident group of attacker birds of one type around itself (no scene edits by setup).</summary>
        static void EnsureSpotPrefab(string name, string typeId)
        {
            ASHMenuInstaller.Folder(PrefabDir);
            string path = PrefabDir + "/" + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path)) return; // keep his tuning
            var go = new GameObject(name);
            try
            {
                var spot = go.AddComponent<ASHBirdSpot>();
                spot.typeId = typeId;
                bool normal = string.IsNullOrEmpty(typeId); // ASH_Bird_FlockSpot: a calm flock of a random ground type
                spot.countMin = normal ? 3 : 2; spot.countMax = normal ? 7 : (typeId == "raven" ? 3 : 4);
                spot.aggressiveFraction = normal ? 0f : 1f;
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Drag-in vendor roost: a Bird Flocks flock that lands on real perches around the spot and scatters when you come close.</summary>
        static void EnsureRoostPrefab(string name, ASHBirdRoostSpot.Style style)
        {
            ASHMenuInstaller.Folder(PrefabDir);
            string path = PrefabDir + "/" + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path)) return;
            var go = new GameObject(name);
            try
            {
                var spot = go.AddComponent<ASHBirdRoostSpot>();
                spot.style = style;
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ------------------------------------------------------------------ items + recipes
        static (ASHItemDefinition feather, ASHItemDefinition meat) EnsureItemsAndRecipes()
        {
            var feather = ASHWildlifeCraftSetup.NewItem("feather", "Feathers", "Flight feathers from a bird. Fletch arrows with them.", "FE", ASHItemCategory.Resource, 40, 1);
            var raw = ASHWildlifeCraftSetup.NewItem("rawsmallmeat", "Raw Small Meat", "A scrap of bird or small-game meat. Cook it before eating.", "RM", ASHItemCategory.Consumable, 10, 0);
            var cooked = ASHWildlifeCraftSetup.NewItem("cookedsmallmeat", "Cooked Small Meat", "A small, safe bite of roasted bird or small game.", "CM", ASHItemCategory.Consumable, 10, 1);
            if (raw.useVerb != "Eat" || raw.restoreHunger <= 0f)
            {
                raw.useVerb = "Eat"; raw.restoreHunger = 4f; raw.restoreHealth = -2f;
                raw.sicknessChance = 0.3f; raw.sicknessHealthLoss = 5f; raw.sicknessThirstLoss = 6f; raw.sicknessHungerLoss = 4f;
                raw.uiTint = new Color(0.72f, 0.3f, 0.3f);
                EditorUtility.SetDirty(raw);
            }
            if (cooked.useVerb != "Eat" || cooked.restoreHunger <= 0f)
            {
                cooked.useVerb = "Eat"; cooked.restoreHunger = 12f; cooked.restoreHealth = 2f; cooked.sicknessChance = 0f;
                cooked.uiTint = new Color(0.62f, 0.42f, 0.28f);
                EditorUtility.SetDirty(cooked);
            }
            if (feather.uiTint == new Color(0.85f, 0.8f, 0.7f)) { feather.uiTint = new Color(0.32f, 0.32f, 0.36f); EditorUtility.SetDirty(feather); }

            var wood = ASHWildlifeCraftSetup.I("deadwood") ?? ASHWildlifeCraftSetup.I("firewood");
            var heads = ASHWildlifeCraftSetup.I("bonearrowhead");
            var arrows = ASHWildlifeCraftSetup.I("woodenarrow");
            var recipes = new List<ASHRecipe>
            {
                ASHWildlifeCraftSetup.Rec("cooksmallmeat", "Cook Small Meat", "Roast small meat over a lit campfire.", ASHRecipeCategory.Food, cooked, 1, 0f, ASHCraftStation.Campfire, 15,
                    ASHWildlifeCraftSetup.First(raw), 4, 11, ASHWildlifeCraftSetup.Ing(raw, 1)),
                ASHWildlifeCraftSetup.Rec("fletchedarrows", "Fletched Arrows", "Fletch wooden shafts with feathers and bone points. Flies truer; makes more arrows.", ASHRecipeCategory.Tools, arrows, 6, 3f, ASHCraftStation.Anywhere, 0,
                    ASHWildlifeCraftSetup.First(feather), 7, 6, ASHWildlifeCraftSetup.Ing(feather, 2), ASHWildlifeCraftSetup.Ing(wood, 2), ASHWildlifeCraftSetup.Ing(heads, 1)),
            };
            var cleaned = recipes.Where(r => r).ToList();
            ASHWildlifeCraftSetup.WireManual(cleaned);
            ASHItemCatalog.AppendRecipesToPlayer(cleaned);
            ASHMenuInstaller.RefreshRegistry();
            return (feather, raw);
        }

        // ------------------------------------------------------------------ settings asset
        static ASHBirdSettings EnsureSettings(GameObject prefab, ASHItemDefinition feather, ASHItemDefinition meat)
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHBirdSettings>(SettingsPath);
            if (!s) { s = ScriptableObject.CreateInstance<ASHBirdSettings>(); AssetDatabase.CreateAsset(s, SettingsPath); }
            s.birdPrefab = prefab;
            if (!s.featherItem) s.featherItem = feather;
            if (!s.meatItem) s.meatItem = meat;

            AudioClip[] A(params string[] names) => names.Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>(n)).Where(c => c).ToArray();
            string[] Seq(string prefix, int n) => Enumerable.Range(1, n).Select(i => $"{AudioDir}/{prefix}_{i:00}.ogg").ToArray();
            // Raven_01..04 (voice-like low recording) were removed; only the real caws 05..08 remain
            var raven = A(Enumerable.Range(5, 4).Select(i => $"{AudioDir}/ASH_Bird_Raven_{i:00}.ogg").ToArray());
            var robin = A(Seq("ASH_Bird_Robin", 4));
            var blue = A(Seq("ASH_Bird_Bluebird", 4));
            var black = A(Seq("ASH_Bird_Blackbird", 4));
            var nut = A(Seq("ASH_Bird_Nutcracker", 3));
            var twitter = A("Assets/Audio/QP01 0067 Coniferous forest bird twitter.wav");
            if (twitter.Length == 0) twitter = robin;

            if (s.types == null) s.types = new List<ASHBirdSettings.BirdType>();
            ASHBirdSettings.BirdType T(string id, string name, float weight, Color tint, float scale, float solo, int fMin, int fMax, float speed, float takeOff, float peck, float objects,
                                        AudioClip[] calls, float pitch, float vol, float callMin, float callMax, int fea0, int fea1, int meat0, int meat1)
            {
                var t = s.types.FirstOrDefault(x => x != null && x.id == id);
                if (t != null)
                {
                    if (t.calls == null || t.calls.Length == 0 || t.calls.All(c => !c)) t.calls = calls;
                    return t;
                }
                t = new ASHBirdSettings.BirdType
                {
                    id = id, displayName = name, weight = weight, tint = tint, scale = scale, soloChance = solo, flockMin = fMin, flockMax = fMax,
                    flySpeed = speed, takeOffDistance = takeOff, peckChance = peck, perchOnObjectsChance = objects,
                    calls = calls, callPitch = pitch, callVolume = vol, callIntervalMin = callMin, callIntervalMax = callMax,
                    feathersMin = fea0, feathersMax = fea1, meatMin = meat0, meatMax = meat1,
                    altitudeMin = scale > 0.8f ? 9f : 5f, altitudeMax = scale > 0.8f ? 28f : 16f, glideShare = scale > 0.8f ? 0.45f : 0.15f,
                    maxAccel = scale > 0.8f ? 8f : 12f,
                };
                s.types.Add(t);
                return t;
            }
            //  id           name                   weight tint                                   scale solo  min max speed takeoff peck objects calls  pitch vol  callMin callMax  feathers meat
            T("crow",       "Crow",                  1.4f, Color.white,                           1.0f, 0.3f, 2, 6, 8.5f, 14f, 0.55f, 0.5f, raven, 1.22f, 0.85f, 4f, 12f,  2, 4, 1, 1);
            T("raven",      "Raven",                 0.6f, new Color(0.28f, 0.28f, 0.32f),        1.3f, 0.6f, 1, 3, 9f,   18f, 0.35f, 0.6f, raven, 0.95f, 0.9f,  6f, 16f,  3, 5, 1, 2);
            T("blackbird",  "Red-winged Blackbird",  0.8f, new Color(0.3f, 0.28f, 0.28f),         0.5f, 0.2f, 3, 8, 7f,   9f,  0.5f,  0.7f, black, 1f,    0.7f,  3f, 10f,  1, 2, 1, 1);
            T("robin",      "Robin",                 0.9f, new Color(0.78f, 0.5f, 0.36f),         0.5f, 0.6f, 1, 3, 7f,   8f,  0.75f, 0.35f, robin, 1f,   0.7f,  3f, 9f,   1, 2, 1, 1);
            T("bluebird",   "Mountain Bluebird",     0.7f, new Color(0.5f, 0.7f, 1f),             0.45f, 0.4f, 2, 5, 7f,  8f,  0.6f,  0.6f, blue,  1f,    0.65f, 3f, 9f,   1, 2, 1, 1);
            T("nutcracker", "Nutcracker",            0.6f, new Color(0.85f, 0.85f, 0.82f),        0.7f, 0.5f, 1, 3, 8f,   11f, 0.6f,  0.55f, nut,  1f,    0.8f,  4f, 12f,  1, 3, 1, 1);
            T("sparrow",    "Sparrow",               1.0f, new Color(0.72f, 0.56f, 0.4f),         0.36f, 0.15f, 4, 10, 6.5f, 7f, 0.8f,  0.4f, twitter, 1.1f, 0.55f, 2.5f, 8f, 1, 1, 1, 1);

            if (s.takeOffClips == null || s.takeOffClips.Length == 0 || s.takeOffClips.All(c => !c))
                s.takeOffClips = A("Assets/Malbers Animations/Common/Audio/Wings/Flap Wing 1.wav", "Assets/Malbers Animations/Common/Audio/Wings/Flap Wing 2.wav",
                                   "Assets/Malbers Animations/Common/Audio/Wings/Wing Flap 02.wav");
            foreach (var t in s.types)
            {
                if (t == null || t.attackDefaultsApplied) continue;
                t.canAttack = t.id == "crow" || t.id == "raven";
                t.attackerChance = t.id == "raven" ? 0.35f : (t.id == "crow" ? 0.2f : 0f);
                t.attackDefaultsApplied = true;
            }
            if (s.swoopClips == null || s.swoopClips.Length == 0 || s.swoopClips.All(c => !c))
                s.swoopClips = A("Assets/Malbers Animations/Common/Audio/Wings/Wing Flap Big1.wav", "Assets/Malbers Animations/Common/Audio/Wings/wing-flap-Big.wav");
            if (s.deathClips == null || s.deathClips.Length == 0 || s.deathClips.All(c => !c))
                s.deathClips = A("Assets/Malbers Animations/Common/Audio/Wings/Wing Flap 02.wav");
            EditorUtility.SetDirty(s);
            return s;
        }
    }
}
#endif
