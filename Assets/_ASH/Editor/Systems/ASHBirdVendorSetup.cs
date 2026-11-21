#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.EditorTools.Systems;
using ASH.Wildlife.Birds;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Part of ASH/Systems/Wildlife/Setup Birds: builds ASH bird prefabs from the vendor packs (never edits them) into
    /// Assets/_ASH/Generated/Birds (git-ignored, rebuilt on any machine that has the packs):
    ///  - Bird Flocks crow + seagull (legacy Animation: Flap / Soar / Idle) and Birds/Birds Misc duck, eagle, seagull, sparrow,
    ///    swallow (+ stork from Birds/Models) with an Animator per rig (idle_01/02, idle_eat, walk, take_off, fly, fly_idle, land, death).
    ///  - URP Lit materials from the vendor textures (the vendor materials use Built-in shaders and render pink in URP).
    ///  - Fills ASH_BirdSettings: per-type prefab / calls (once per type, vendorApplied), the 12 Bird Flocks flock prefabs for the sky
    ///    tier, the vendor roost prefabs and the material swaps.
    /// </summary>
    public static class ASHBirdVendorSetup
    {
        public const string GenDir = "Assets/_ASH/Generated/Birds";
        const string BF = "Assets/Bird Flocks";
        const string BM = "Assets/Birds";

        struct Misc { public string id, fbx, tex; }

        public static void Run(ASHBirdSettings s)
        {
            if (!s) return;
            bool hasBF = AssetDatabase.IsValidFolder(BF), hasBM = AssetDatabase.IsValidFolder(BM);
            if (!hasBF && !hasBM) { Debug.Log("[ASH] Birds: vendor bird packs not found (Assets/Bird Flocks, Assets/Birds) - using the built-in crow only."); return; }
            ASHMenuInstaller.Folder("Assets/_ASH/Generated");
            ASHMenuInstaller.Folder(GenDir);
            var built = new Dictionary<string, GameObject>();

            if (hasBF)
            {
                var crowMat = Mat("ASH_Gen_M_BF_Crow", BF + "/Bird Flock Crow/Textures/Crow TEX.tif");
                var gullMat = Mat("ASH_Gen_M_BF_Gull", BF + "/Bird Flock Seagull/Textures/Gull TEX.tif");
                var c = BuildLegacy(BF + "/Bird Flock Crow/Prefabs/Crow.prefab", "ASH_Bird_BF_Crow", crowMat, "crow");
                var g = BuildLegacy(BF + "/Bird Flock Seagull/Prefabs/Seagull.prefab", "ASH_Bird_BF_Gull", gullMat, "gull");
                if (c) built["bf_crow"] = c;
                if (g) built["bf_gull"] = g;
                // sky tier: every Bird Flocks flock preset (both styles)
                var sky = new List<GameObject>(); var ids = new List<string>();
                foreach (var style in new[] { ("Crow", "crow"), ("Seagull", "gull") })
                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { $"{BF}/Bird Flock {style.Item1}/Prefabs" }))
                    {
                        var p = AssetDatabase.GUIDToAssetPath(guid);
                        if (!Path.GetFileName(p).StartsWith(style.Item1 + " Flock")) continue;
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        if (go && go.GetComponent("FlockController")) { sky.Add(go); ids.Add(style.Item2); }
                    }
                s.skyFlockPrefabs = sky.ToArray();
                s.skyFlockTypeIds = ids.ToArray();
                s.roostCrowFlock = Load(BF + "/Bird Flock Crow/Prefabs/Crow Flock - Wild Few.prefab");
                s.roostCrowLanding = Load(BF + "/Bird Flock Crow/Prefabs/Landing Spot Controller.prefab");
                s.roostGullFlock = Load(BF + "/Bird Flock Seagull/Prefabs/Seagull Flock - Wild Few.prefab");
                s.roostGullLanding = Load(BF + "/Bird Flock Seagull/Prefabs/Seagull - Landing Spots.prefab");
                var from = new List<Material>(); var to = new List<Material>();
                void Swap(string path, Material m) { var f = AssetDatabase.LoadAssetAtPath<Material>(path); if (f && m) { from.Add(f); to.Add(m); } }
                Swap(BF + "/Bird Flock Crow/Materials/Crow MAT.mat", crowMat);
                Swap(BF + "/Bird Flock Seagull/Materials/Seagull TEX.mat", gullMat);
                s.vendorMaterialFrom = from.ToArray();
                s.vendorMaterialTo = to.ToArray();
            }
            if (hasBM)
            {
                var misc = new[]
                {
                    new Misc { id = "duck", fbx = BM + "/Birds Misc/duck_anim_fbx.FBX", tex = BM + "/Textures/duck.tga" },
                    new Misc { id = "eagle", fbx = BM + "/Birds Misc/eagle_anim_fbx.FBX", tex = BM + "/Textures/eagle.tga" },
                    new Misc { id = "seagull", fbx = BM + "/Birds Misc/seagul_anim_fbx.FBX", tex = BM + "/Textures/seagull.tga" },
                    new Misc { id = "sparrow", fbx = BM + "/Birds Misc/sparrow_anim_fbx.FBX", tex = BM + "/Textures/sparrow.tga" },
                    new Misc { id = "swallow", fbx = BM + "/Birds Misc/swallow_anim_fbx.FBX", tex = BM + "/Textures/swallow.tga" },
                    new Misc { id = "stork", fbx = BM + "/Models/storkl_anim_fbx.FBX", tex = BM + "/Textures/stork.tga" },
                };
                foreach (var m in misc)
                {
                    var fbx = m.fbx;
                    if (!File.Exists(fbx)) fbx = BM + "/Models/" + Path.GetFileName(m.fbx);
                    if (!File.Exists(fbx)) continue;
                    var mat = Mat("ASH_Gen_M_" + m.id, m.tex);
                    var p = BuildMisc(fbx, "ASH_Bird_" + char.ToUpper(m.id[0]) + m.id.Substring(1), mat, m.id);
                    if (p) built[m.id] = p;
                }
            }
            ApplyTypes(s, built);
            EditorUtility.SetDirty(s);
            Debug.Log($"[ASH] Birds (vendor): {built.Count} rigs in {GenDir}, {s.skyFlockPrefabs.Length} sky flock presets, roosts {(s.roostCrowLanding ? "crow" : "-")}/{(s.roostGullLanding ? "gull" : "-")}.");
        }

        static GameObject Load(string p) => AssetDatabase.LoadAssetAtPath<GameObject>(p);

        static Material Mat(string name, string texPath)
        {
            string path = GenDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (!m) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex); }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.12f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Cull")) { m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; } // thin wings: render both faces
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ Bird Flocks rigs (legacy Animation)
        static GameObject BuildLegacy(string vendorPrefab, string name, Material mat, string typeId)
        {
            var src = Load(vendorPrefab);
            if (!src) return null;
            var root = (GameObject)PrefabUtility.InstantiatePrefab(src);
            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = name;
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                // strip the vendor behaviour: ASH drives these birds
                foreach (var c in root.GetComponents<Component>())
                    if (c && !(c is Transform) && (c.GetType().Name == "FlockChild" || c.GetType().Name == "FlockChildSound" || c is AudioSource)) Object.DestroyImmediate(c);
                var legacy = root.GetComponentInChildren<Animation>(true);
                if (!legacy) return null;
                var model = legacy.transform;
                var clips = AnimationUtility.GetAnimationClips(legacy.gameObject).Where(c => c).Select(c => c.name).Distinct().ToList();
                string Pick(string key) => clips.FirstOrDefault(n => n.ToLowerInvariant().Contains(key)) ?? "";
                Normalise(root, model, null);
                return Finish(root, mat, typeId, name, null, legacy, new ASHBird.AnimSet
                {
                    use = true, manualLoop = false,
                    idle = new[] { Pick("idle") }.Where(x => x != "").ToArray(),
                    peck = new[] { Pick("idle") }.Where(x => x != "").ToArray(),
                    fly = Pick("flap"), glide = Pick("soar"),
                });
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ------------------------------------------------------------------ Birds Misc rigs (generic Animator; clips import without loop: ASHBird loops them)
        static GameObject BuildMisc(string fbx, string name, Material mat, string typeId)
        {
            var model = Load(fbx);
            if (!model) return null;
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
            if (clips.Count == 0) return null;
            var ctrl = Controller(GenDir + "/" + name + ".controller", clips);
            var root = new GameObject(name);
            try
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                inst.transform.SetParent(root.transform, false);
                var anim = inst.GetComponentInChildren<Animator>();
                if (!anim) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;
                // pose the rig in its stand pose before measuring, so wings don't count as body length
                var stand = clips.FirstOrDefault(c => c.name == "stand_pose") ?? clips.FirstOrDefault(c => c.name == "idle_01");
                if (stand) stand.SampleAnimation(inst, 0f);
                Normalise(root, inst.transform, inst);
                bool Has(string n) => clips.Any(c => c.name == n);
                string H(string n) => Has(n) ? n : "";
                return Finish(root, mat, typeId, name, anim, null, new ASHBird.AnimSet
                {
                    use = true, manualLoop = true,
                    idle = new[] { "idle_01", "idle_02", "idle_01", "stand_pose" }.Where(Has).ToArray(),
                    peck = new[] { "idle_eat" }.Where(Has).ToArray(),
                    walk = H("walk"), fly = H("fly"), glide = H("fly_idle"), takeOff = H("take_off"), land = H("land"), death = H("death"),
                });
            }
            finally { Object.DestroyImmediate(root); }
        }

        static AnimatorController Controller(string path, List<AnimationClip> clips)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path) ?? AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var clip in clips)
            {
                var st = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == clip.name) ?? sm.AddState(clip.name);
                st.motion = clip;
                st.writeDefaultValues = true;
            }
            var idle = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == "idle_01");
            if (idle) sm.defaultState = idle;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        /// <summary>Faces the rig along +Z (head ahead of pelvis), scales it to the shared 0.6 m body size (type scale does the rest), feet on the root.</summary>
        static void Normalise(GameObject root, Transform model, GameObject rig)
        {
            if (rig)
            {
                Transform head = null, pelvis = null;
                foreach (var t in rig.GetComponentsInChildren<Transform>(true))
                {
                    var n = t.name.ToLowerInvariant();
                    if (!head && n.EndsWith("head")) head = t;
                    if (!pelvis && (n.EndsWith("pelvis") || n == "hips")) pelvis = t;
                }
                if (head && pelvis)
                {
                    var d = root.transform.InverseTransformPoint(head.position) - root.transform.InverseTransformPoint(pelvis.position);
                    d.y = 0f;
                    if (d.sqrMagnitude > 1e-8f) model.localRotation = Quaternion.AngleAxis(Vector3.SignedAngle(d.normalized, Vector3.forward, Vector3.up), Vector3.up) * model.localRotation;
                }
            }
            var b = Bounds(root);
            float len = Mathf.Max(b.size.z, b.size.y);
            if (len > 0.0001f) { model.localScale *= 0.6f / len; b = Bounds(root); }
            model.localPosition += new Vector3(-b.center.x, -b.min.y, -b.center.z);
        }

        static Bounds Bounds(GameObject go)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var rb = r.bounds;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh)
                {
                    var baked = new Mesh();
                    smr.BakeMesh(baked, true);
                    var mb = baked.bounds;
                    rb = new Bounds(smr.transform.TransformPoint(mb.center), Vector3.zero);
                    for (int i = 0; i < 8; i++)
                        rb.Encapsulate(smr.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    Object.DestroyImmediate(baked);
                }
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            return b;
        }

        static GameObject Finish(GameObject root, Material mat, string typeId, string name, Animator anim, Animation legacy, ASHBird.AnimSet set)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                if (mats.Length == 0) mats = new[] { mat };
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) { smr.updateWhenOffscreen = false; smr.quality = SkinQuality.Bone2; }
            }
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) Object.DestroyImmediate(ps.gameObject);
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            var bird = root.GetComponent<ASHBird>(); if (!bird) bird = root.AddComponent<ASHBird>();
            bird.animator = anim;
            bird.legacy = legacy;
            bird.anims = set;
            bird.placedTypeId = typeId;
            var col = root.AddComponent<SphereCollider>();
            var b = Bounds(root);
            col.center = new Vector3(0f, Mathf.Max(0.08f, b.center.y), 0f);
            col.radius = 0.17f;
            bird.body = col;
            bird.bodyRadius = col.radius;
            bird.harvestRadius = 0.35f;
            var rb = root.GetComponent<Rigidbody>(); if (!rb) rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            int enemy = LayerMask.NameToLayer("Enemy");
            if (enemy >= 0) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = enemy;
            return PrefabUtility.SaveAsPrefabAsset(root, GenDir + "/" + name + ".prefab");
        }

        // ------------------------------------------------------------------ settings: types on the vendor rigs (once per type), clean audio
        static void ApplyTypes(ASHBirdSettings s, Dictionary<string, GameObject> rig)
        {
            AudioClip[] A(params string[] p) => p.Select(x => AssetDatabase.LoadAssetAtPath<AudioClip>(x)).Where(c => c).ToArray();
            var crowCaw = A(BF + "/Bird Flock Crow/Sounds/Crow 001.wav", BF + "/Bird Flock Crow/Sounds/Crow 002.wav");
            var crowScared = A(BF + "/Bird Flock Crow/Sounds/Crow Scared 001.wav");
            var gull = A(BF + "/Bird Flock Seagull/Sounds/Seagull 001.wav", BF + "/Bird Flock Seagull/Sounds/Seagull 002.wav", BF + "/Bird Flock Seagull/Sounds/Seagull 003.wav", BF + "/Bird Flock Seagull/Sounds/Seagull 004.wav");
            var gullScared = A(BF + "/Bird Flock Seagull/Sounds/Seagull Scared 001.wav");
            var raven = A("Assets/_ASH/Audio/Birds/ASH_Bird_Raven_05.ogg", "Assets/_ASH/Audio/Birds/ASH_Bird_Raven_06.ogg", "Assets/_ASH/Audio/Birds/ASH_Bird_Raven_07.ogg", "Assets/_ASH/Audio/Birds/ASH_Bird_Raven_08.ogg");
            var twitter = A("Assets/Audio/QP01 0067 Coniferous forest bird twitter.wav");
            if (twitter.Length == 0) twitter = A("Assets/_ASH/Audio/Birds/ASH_Bird_Robin_01.ogg", "Assets/_ASH/Audio/Birds/ASH_Bird_Robin_02.ogg");
            GameObject R(string k) => rig.TryGetValue(k, out var g) ? g : null;

            ASHBirdSettings.BirdType T(string id, string name, float weight, float scale, float solo, int fMin, int fMax, float speed, float takeOff, float peck, float objects, float skyOnly, int fea0, int fea1, int meat0, int meat1)
            {
                var t = s.types.FirstOrDefault(x => x != null && x.id == id);
                if (t != null) return t;
                t = new ASHBirdSettings.BirdType
                {
                    id = id, displayName = name, weight = weight, tint = Color.white, scale = scale, soloChance = solo, flockMin = fMin, flockMax = fMax,
                    flySpeed = speed, takeOffDistance = takeOff, peckChance = peck, perchOnObjectsChance = objects, skyOnlyChance = skyOnly,
                    feathersMin = fea0, feathersMax = fea1, meatMin = meat0, meatMax = meat1,
                    altitudeMin = scale > 0.8f ? 9f : 5f, altitudeMax = scale > 0.8f ? 28f : 16f, glideShare = scale > 0.8f ? 0.5f : 0.15f, maxAccel = scale > 0.8f ? 7f : 12f,
                    attackDefaultsApplied = true,
                };
                s.types.Add(t);
                return t;
            }
            //   id         name                weight scale solo  min max speed takeoff peck objects skyOnly  feathers meat
            if (R("bf_crow")) T("hoodedcrow", "Hooded Crow", 0.4f, 1.0f, 0.3f, 2, 5, 8.5f, 14f, 0.55f, 0.5f, 0f, 2, 4, 1, 1);
            if (R("bf_gull")) T("gull", "Gull", 0.5f, 1.1f, 0.3f, 2, 6, 9f, 16f, 0.4f, 0.5f, 0.3f, 2, 4, 1, 1);
            if (R("seagull")) T("seagull", "Herring Gull", 0.3f, 1.1f, 0.4f, 1, 4, 9f, 16f, 0.4f, 0.5f, 0.3f, 2, 4, 1, 1);
            if (R("eagle")) T("eagle", "Golden Eagle", 0.15f, 1.7f, 0.9f, 1, 2, 11f, 26f, 0.2f, 0.6f, 0.6f, 4, 7, 1, 3);
            if (R("stork")) T("stork", "Stork", 0.1f, 1.8f, 0.6f, 1, 3, 9f, 22f, 0.5f, 0.3f, 0.5f, 3, 6, 1, 2);
            if (R("duck")) T("duck", "Duck", 0f, 0.95f, 0.2f, 2, 5, 10f, 15f, 0.65f, 0.05f, 0f, 2, 4, 1, 2);
            if (R("swallow")) T("swallow", "Swallow", 0.5f, 0.32f, 0.2f, 3, 8, 9f, 8f, 0.3f, 0.6f, 0.4f, 1, 1, 1, 1);

            foreach (var t in s.types)
            {
                if (t == null || t.vendorApplied) continue;
                GameObject p = null;
                switch (t.id)
                {
                    case "crow": p = R("bf_crow"); if (crowCaw.Length > 0) { t.calls = crowCaw; t.callPitch = 1f; } if (crowScared.Length > 0) t.alarmCalls = crowScared; t.canSwarm = true; break;
                    case "raven": p = R("bf_crow"); if (raven.Length > 0) { t.calls = raven; t.callPitch = 0.95f; } t.alarmCalls = raven; t.canAttack = true; t.attackerChance = 0.35f; break;
                    case "hoodedcrow": p = s.birdPrefab; t.calls = crowCaw.Length > 0 ? crowCaw : raven; t.callPitch = 1.05f; t.alarmCalls = crowScared; t.canAttack = true; t.attackerChance = 0.15f; t.canSwarm = true; break;
                    case "gull": p = R("bf_gull"); t.calls = gull; t.alarmCalls = gullScared; t.canAttack = true; t.attackerChance = 0.1f; t.canSwarm = true; break;
                    case "seagull": p = R("seagull"); t.calls = gull; t.callPitch = 1.08f; t.alarmCalls = gullScared; t.canSwarm = true; break;
                    case "eagle": p = R("eagle"); t.calls = new AudioClip[0]; t.canAttack = true; t.attackerChance = 0.5f; break;
                    case "stork": p = R("stork"); t.calls = new AudioClip[0]; break;
                    case "duck": p = R("duck"); t.calls = new AudioClip[0]; break;
                    case "swallow": p = R("swallow"); t.calls = twitter; t.callPitch = 1.15f; break;
                    case "sparrow": p = R("sparrow"); if (twitter.Length > 0) t.calls = twitter; t.scale = Mathf.Min(t.scale, 0.32f); break;
                    case "blackbird": case "robin": case "bluebird": case "nutcracker":
                        p = R("sparrow"); t.tint = Color.Lerp(t.tint, Color.white, 0.55f); break;
                }
                if (!p) continue;
                t.prefab = p == s.birdPrefab ? null : p;
                t.vendorApplied = true;
            }
            // drop any clip that is no longer on disk (removed voice-like raven recordings)
            foreach (var t in s.types)
            {
                if (t == null) continue;
                if (t.calls != null) t.calls = t.calls.Where(c => c).ToArray();
                if (t.alarmCalls != null) t.alarmCalls = t.alarmCalls.Where(c => c).ToArray();
                if (t.calls != null && t.calls.Length == 0 && (t.id == "crow" || t.id == "raven")) t.calls = raven;
            }
        }
    }
}
#endif
