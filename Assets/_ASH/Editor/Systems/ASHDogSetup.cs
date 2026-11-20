using System.Collections.Generic;
using System.IO;
using System.Text;
using ASH.Companion;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// ASH/Systems/Companion/Setup Dog (idempotent, t328): makes the scene's RedDeer Shepherd the ASH dog companion.
    /// - Resources/ASH_CompanionSettings (created once; presets under ASH/Systems/Companion/Preset ...).
    /// - Art/Companion/ASH_Dog.controller built from the pack's in-place clips (Shepherd_IP.fbx; vendor files untouched):
    ///   locomotion blend (idle / walk / trot / run by Speed), Pose int (1 sit, 2 lie, 3 alert crouch, 4 eat, 5 down),
    ///   Attack / Hit / Happy triggers. Rebuilt by "Rebuild Dog Animator".
    /// - On the dog (scene overrides only): that controller, no root motion, a trigger capsule for E, ASHDogCompanion with
    ///   bark / whine / whistle clips from the Malbers wolf pack. No Shepherd in the scene: one is placed next to the player.
    /// The scene is left UNSAVED.
    /// </summary>
    public static class ASHDogSetup
    {
        const string Fbx = "Assets/Realistic/Dogs/Shepherd/FBX/Anim/Shepherd_IP.fbx";
        const string FbxRM = "Assets/Realistic/Dogs/Shepherd/FBX/Anim/Shepherd_RM.fbx";
        const string StepDir = "Assets/Audio/Foot Steps/Snow";
        const string DogPrefab = "Assets/Realistic/Dogs/Shepherd/Prefabs/Shepherd_HighPoly.prefab";
        const string Dir = "Assets/_ASH/Art/Companion";
        const string Ctrl = Dir + "/ASH_Dog.controller";
        const string SettingsPath = "Assets/_ASH/Resources/ASH_CompanionSettings.asset";
        const string Bark = "Assets/Malbers Animations/Animal Controller/Wolf Lite/Audio/Wolf Bark.wav";
        const string Bark2 = "Assets/Malbers Animations/Animal Controller/Wolf Lite/Audio/Wolf Bark Double.wav";
        const string Whine = "Assets/Malbers Animations/Animals Packs/01 Forest Pack/Wolf/Audio/Whining.wav";
        const string Whistle = "Assets/Malbers Animations/Common/Audio/Whistle/Whistle Quick.wav";

        [MenuItem("ASH/Systems/Companion/Setup Dog")]
        public static void Setup()
        {
            var log = new StringBuilder("[ASH] Dog setup:");
            var s = EnsureSettings(log);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctrl);
            if (!ctrl) { ctrl = BuildController(s, log); } else log.Append(" controller kept;");
            var dog = FindDog();
            if (!dog)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(DogPrefab);
                var pl = Object.FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>();
                if (!pf) { Debug.LogError("[ASH] Dog setup: no Shepherd in the scene and " + DogPrefab + " missing."); return; }
                dog = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                if (pl) dog.transform.position = pl.transform.position + pl.transform.right * 2f;
                Undo.RegisterCreatedObjectUndo(dog, "Place dog"); log.Append(" placed a Shepherd next to the player;");
            }
            else log.Append($" dog '{dog.name}' at {dog.transform.position};");

            var anim = dog.GetComponentInChildren<Animator>();
            if (anim) { Undo.RecordObject(anim, "Dog animator"); anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            var col = dog.GetComponent<CapsuleCollider>();
            if (col == null) { col = Undo.AddComponent<CapsuleCollider>(dog); log.Append(" capsule added;"); }
            var b = Bounds(dog);
            col.isTrigger = true; col.direction = 2;
            var ls = dog.transform.lossyScale.x > 1e-4f ? dog.transform.lossyScale.x : 1f;
            col.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.y) * 0.4f, 0.15f, 0.6f) / ls;
            col.height = Mathf.Clamp(b.size.z * 0.9f, 0.6f, 2f) / ls;
            col.center = dog.transform.InverseTransformPoint(b.center);
            var comp = dog.GetComponent<ASHDogCompanion>();
            if (comp == null) { comp = Undo.AddComponent<ASHDogCompanion>(dog); log.Append(" ASHDogCompanion added;"); } else log.Append(" ASHDogCompanion kept;");
            var so = new SerializedObject(comp);
            so.FindProperty("animator").objectReferenceValue = anim;
            if (s.growlClips == null || s.growlClips.Length == 0) AssignGrowls(s, log);
            if (s.pawStepClips == null || s.pawStepClips.Length == 0)
            {
                var steps = new List<AudioClip>();
                foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { StepDir })) { var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g)); if (c) steps.Add(c); }
                if (steps.Count > 0) { s.pawStepClips = steps.ToArray(); EditorUtility.SetDirty(s); AssetDatabase.SaveAssets(); log.Append($" {steps.Count} paw step clips;"); }
            }
            Clip(so, "barkClip", Bark, log); Clip(so, "barkDoubleClip", Bark2, log); Clip(so, "whineClip", Whine, log); Clip(so, "whistleClip", Whistle, log);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(dog);
            EditorSceneManager.MarkSceneDirty(dog.scene);
            Selection.activeGameObject = dog;
            Debug.Log(log.Append(" done (scene not saved).").ToString());
        }

        [MenuItem("ASH/Systems/Companion/Report Colliders Near Dog")] public static void ReportColliders() => Colliders(false);
        /// <summary>Adds a MeshCollider (scene override, unsaved) to every mesh within 30 m of the dog that has no collider of its own or above it.</summary>
        [MenuItem("ASH/Systems/Companion/Add Missing Colliders Near Dog")] public static void AddColliders() => Colliders(true);

        static void Colliders(bool add)
        {
            var dog = FindDog(); var c = dog ? dog.transform.position : (Object.FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>()?.transform.position ?? Vector3.zero);
            var sb = new StringBuilder(); int total = 0, missing = 0, added = 0;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mr.enabled || !mf.sharedMesh) continue;
                if ((mr.bounds.center - c).sqrMagnitude > 30f * 30f || mr.bounds.size.magnitude < 0.35f) continue;
                if (dog && mf.transform.IsChildOf(dog.transform)) continue;
                if (mf.GetComponentInParent<ASH.Interaction.ASHItemPickup>() || mf.GetComponentInParent<ASH.Survival.ASHSurvivalStats>() || mf.GetComponentInParent<ParticleSystem>()) continue;
                total++;
                bool has = false; for (var t = mf.transform; t && !has; t = t.parent) foreach (var col in t.GetComponents<Collider>()) if (col.enabled && !col.isTrigger) { has = true; break; }
                if (!has && mf.GetComponentInChildren<Collider>() is Collider cc && cc.enabled && !cc.isTrigger) has = true;
                // an LOD / prefab sibling with a collider already covers this mesh (rocks, trees)
                var lod = mf.GetComponentInParent<LODGroup>(); var owner = lod ? lod.transform : PrefabUtility.GetOutermostPrefabInstanceRoot(mf.gameObject)?.transform;
                if (!has && owner && owner != mf.transform) foreach (var oc in owner.GetComponentsInChildren<Collider>()) if (oc.enabled && !oc.isTrigger) { has = true; break; }
                if (!has && (mf.name.Contains("Grass") || mf.name.Contains("LOD1") || mf.name.Contains("LOD2") || mf.name.Contains("LOD3"))) continue;
                if (has) continue;
                missing++; if (missing <= 40) sb.Append("\n  ").Append(HierPath(mf.transform)).Append($"  ({mf.sharedMesh.name}, {mr.bounds.size.x:0.0}x{mr.bounds.size.y:0.0}x{mr.bounds.size.z:0.0} m)");
                if (add) { var mc = Undo.AddComponent<MeshCollider>(mf.gameObject); mc.sharedMesh = mf.sharedMesh; added++; }
            }
            if (add && added > 0 && dog) EditorSceneManager.MarkSceneDirty(dog.scene);
            Debug.Log($"[ASH] Colliders near the dog: {total} meshes within 30 m, {missing} without a solid collider" + (add ? $", {added} MeshColliders added (scene not saved)" : "") + sb);
        }

        static string HierPath(Transform t) { var s = t.name; for (var p = t.parent; p && s.Length < 140; p = p.parent) s = p.name + "/" + s; return s; }

        [MenuItem("ASH/Systems/Companion/Rebuild Dog Animator")]
        public static void Rebuild() { AssetDatabase.DeleteAsset(Ctrl); Setup(); }

        [MenuItem("ASH/Systems/Companion/Preset Relaxed")] static void PR() { var s = EnsureSettings(new StringBuilder()); s.PresetRelaxed(); AssetDatabase.SaveAssets(); Debug.Log("[ASH] Companion preset: Relaxed"); }
        [MenuItem("ASH/Systems/Companion/Preset Default")] static void PD() { var s = EnsureSettings(new StringBuilder()); s.PresetDefault(); AssetDatabase.SaveAssets(); Debug.Log("[ASH] Companion preset: Default"); }
        [MenuItem("ASH/Systems/Companion/Preset Extreme")] static void PE() { var s = EnsureSettings(new StringBuilder()); s.PresetExtreme(); AssetDatabase.SaveAssets(); Debug.Log("[ASH] Companion preset: Extreme"); }

        const string GrowlDir = "Assets/_ASH/Audio/Companion";
        /// <summary>Fills the growl clip lists from Assets/_ASH/Audio/Companion (ASH_DogGrowl_Warn_* / ASH_DogGrowl_Play_*). Idempotent.</summary>
        [MenuItem("ASH/Systems/Companion/Assign Growl Clips")]
        public static void AssignGrowls()
        {
            var log = new StringBuilder("[ASH] Dog growls:"); var s = EnsureSettings(log);
            AssignGrowls(s, log); Debug.Log(log.ToString());
        }
        static void AssignGrowls(ASHCompanionSettings s, StringBuilder log)
        {
            var warn = new List<AudioClip>(); var play = new List<AudioClip>();
            if (AssetDatabase.IsValidFolder(GrowlDir))
                foreach (var g in AssetDatabase.FindAssets("ASH_DogGrowl t:AudioClip", new[] { GrowlDir }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(g); var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path); if (!c) continue;
                    (path.Contains("_Play_") ? play : warn).Add(c);
                }
            warn.Sort((a, b) => string.CompareOrdinal(a.name, b.name)); play.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            s.growlClips = warn.ToArray(); s.playGrowlClips = play.ToArray();
            var hiss = AssetDatabase.LoadAssetAtPath<AudioClip>(GrowlDir + "/ASH_DogPee_FireHiss.wav"); if (hiss) s.peeHissClip = hiss; EditorUtility.SetDirty(s); AssetDatabase.SaveAssets();
            log.Append($" {warn.Count} warning + {play.Count} playful growl clips assigned;");
        }

        static ASHCompanionSettings EnsureSettings(StringBuilder log)
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHCompanionSettings>(SettingsPath);
            if (s) return s;
            s = ScriptableObject.CreateInstance<ASHCompanionSettings>(); AssetDatabase.CreateAsset(s, SettingsPath); AssetDatabase.SaveAssets(); log.Append(" settings created;");
            return s;
        }

        static void Clip(SerializedObject so, string prop, string path, StringBuilder log)
        {
            var p = so.FindProperty(prop); if (p == null || p.objectReferenceValue) return;
            var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path); if (c) p.objectReferenceValue = c; else log.Append($" missing audio {Path.GetFileName(path)};");
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(go.transform.position + Vector3.up * 0.4f, new Vector3(0.4f, 0.8f, 1.1f));
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            // a rough local-axis size: the dog's length lies along its forward
            var t = go.transform; var size = new Vector3(Mathf.Abs(Vector3.Dot(b.size, t.right)), Mathf.Abs(Vector3.Dot(b.size, t.up)), Mathf.Abs(Vector3.Dot(b.size, t.forward)));
            return new Bounds(b.center, size);
        }

        static GameObject FindDog()
        {
            var c = Object.FindAnyObjectByType<ASHDogCompanion>(FindObjectsInactive.Include); if (c) return c.gameObject;
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(a.gameObject) ?? a.gameObject;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(root);
                string sp = src ? AssetDatabase.GetAssetPath(src) : "";
                if (sp.Contains("/Realistic/Dogs/") || root.name.StartsWith("Shepherd")) return root;
            }
            return null;
        }

        /// <summary>Ground speed of the pack's root-motion Walk/Trot/Run (Shepherd_RM.fbx averageSpeed x the dog's scale) -> settings.</summary>
        static void NativeSpeeds(ASHCompanionSettings s, StringBuilder log)
        {
            var rm = new Dictionary<string, AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FbxRM)) if (o is AnimationClip ac && !ac.name.StartsWith("__preview")) rm[ac.name.Replace("Arm_Dog|", "")] = ac;
            var dog = FindDog(); float sc = dog ? dog.transform.lossyScale.x : 1f;
            float Sp(string n, float fb)
            {
                AnimationClip c = null; foreach (var k in new[] { n + "_RM", n, n + "_IP" }) if (rm.TryGetValue(k, out c)) break;
                if (!c) foreach (var kv in rm) if (kv.Key.StartsWith(n)) { c = kv.Value; break; }
                float v = c ? Travel(c) * sc : 0f; // the RM import has no motion node, so averageSpeed is unreliable: sample the clip
                log.Append($" {n} {(c ? c.name : "missing")} {v:F2} m/s;");
                return v > 0.05f ? v : fb;
            }
            s.walkClipSpeed = Sp("Walk_F", 1.3f); s.trotClipSpeed = Sp("Trot_F", 3.4f); s.runClipSpeed = Sp("Run_F", 7.2f);
            EditorUtility.SetDirty(s); AssetDatabase.SaveAssets();
        }

        /// <summary>Horizontal metres per second the clip's root / pelvis bone travels over the clip (sampled on a hidden temp dog).</summary>
        static float Travel(AnimationClip c)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(DogPrefab); if (!pf || c.length <= 0f) return 0f;
            var go = Object.Instantiate(pf); go.hideFlags = HideFlags.HideAndDontSave; go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            try
            {
                float best = 0f;
                foreach (var n in new[] { "Root_bone", "Spine_base" })
                {
                    Transform b = null; foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == n) { b = t; break; }
                    if (!b) continue;
                    c.SampleAnimation(go, 0f); var p0 = b.position; c.SampleAnimation(go, c.length); var p1 = b.position;
                    var d = p1 - p0; d.y = 0f; best = Mathf.Max(best, d.magnitude / c.length);
                }
                return best;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Diagnostics: RM root travel vs the in-place clips' planted-paw speed (foot_f.L moving backward while lowest).</summary>
        [MenuItem("ASH/Systems/Companion/Report Dog Clip Speeds")]
        public static void ReportClipSpeeds()
        {
            var ip = new Dictionary<string, AnimationClip>(); var rm = new Dictionary<string, AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx)) if (o is AnimationClip ac && !ac.name.StartsWith("__preview")) ip[ac.name.Replace("Arm_Dog|", "")] = ac;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FbxRM)) if (o is AnimationClip ac && !ac.name.StartsWith("__preview")) rm[ac.name.Replace("Arm_Dog|", "")] = ac;
            var sb = new StringBuilder("[ASH] Dog clip speeds:");
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(DogPrefab);
            var go = Object.Instantiate(pf); go.hideFlags = HideFlags.HideAndDontSave; go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
            try
            {
                Transform foot = null; foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "foot_f.L") foot = t;
                var bb = new Bounds(); bool hb = false; foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!hb) { bb = r.bounds; hb = true; } else bb.Encapsulate(r.bounds); }
                sb.Append($" model size {bb.size};");
                foreach (var n in new[] { "Walk_F", "Trot_F", "Run_F" })
                {
                    rm.TryGetValue(n + "_RM", out var r); ip.TryGetValue(n + "_IP", out var c);
                    float rmv = r ? Travel(r) : -1f;
                    float stance = -1f;
                    if (c && foot)
                    {
                        int N = 120; var zs = new float[N]; var ys = new float[N]; float miny = 99f;
                        for (int i = 0; i < N; i++) { c.SampleAnimation(go, c.length * i / N); var lp = go.transform.InverseTransformPoint(foot.position); zs[i] = lp.z; ys[i] = lp.y; miny = Mathf.Min(miny, lp.y); }
                        var v = new List<float>(); float dt = c.length / N;
                        for (int i = 0; i < N; i++) { int j = (i + 1) % N; if (ys[i] < miny + 0.03f && ys[j] < miny + 0.03f) v.Add(-(zs[j] - zs[i]) / dt); }
                        v.Sort(); if (v.Count > 0) stance = v[v.Count / 2];
                    }
                    sb.Append($" {n}: RM {rmv:F2} m/s, IP planted paw {stance:F2} m/s (len {(c ? c.length : 0):F2}s);");
                }
            }
            finally { Object.DestroyImmediate(go); }
            Debug.Log(sb.ToString());
        }

        static AnimatorController BuildController(ASHCompanionSettings s, StringBuilder log)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_ASH/Art")) AssetDatabase.CreateFolder("Assets/_ASH", "Art");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_ASH/Art", "Companion");
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx)) if (o is AnimationClip ac && !ac.name.StartsWith("__preview")) clips[ac.name.Replace("Arm_Dog|", "")] = ac;
            AnimationClip C(string n) { if (clips.TryGetValue(n, out var c)) return c; log.Append($" missing clip {n};"); return null; }

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(Ctrl);
            NativeSpeeds(s, log);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Turn", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Pose", AnimatorControllerParameterType.Int);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Happy", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Fidget", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("FidgetId", AnimatorControllerParameterType.Int);
            var sm = ctrl.layers[0].stateMachine;

            var loco = ctrl.CreateBlendTreeInController("Locomotion", out var tree, 0);
            // 2D freeform: x = Turn (-1 left .. 1 right, from the yaw rate), y = Speed (m/s, thresholds = the dog's walk / trot / run speeds; playback rate syncs the paws)
            tree.blendType = BlendTreeType.FreeformCartesian2D; tree.blendParameter = "Turn"; tree.blendParameterY = "Speed";
            void Row(string l, string f, string r, float y) { if (C(l)) tree.AddChild(C(l), new Vector2(-1f, y)); if (C(f)) tree.AddChild(C(f), new Vector2(0f, y)); if (C(r)) tree.AddChild(C(r), new Vector2(1f, y)); }
            Row("Turn_L_IP", "Idle_1", "Turn_R_IP", 0f);
            Row("Walk_L_IP", "Walk_F_IP", "Walk_R_IP", s.walkSpeed);
            Row("Trot_L_IP", "Trot_F_IP", "Trot_R_IP", s.trotSpeed);
            Row("Run_L_IP", "Run_F_IP", "Run_R_IP", s.runSpeed);
            sm.defaultState = loco;

            void PoseLoop(int pose, string start, string loop, string end, Vector3 at)
            {
                var a = sm.AddState("P" + pose + "_" + start, at); a.motion = C(start);
                var l = sm.AddState("P" + pose + "_" + loop, at + new Vector3(220, 0)); l.motion = C(loop);
                var e = sm.AddState("P" + pose + "_" + end, at + new Vector3(440, 0)); e.motion = C(end);
                var t0 = loco.AddTransition(a); t0.hasExitTime = false; t0.duration = 0.2f; t0.AddCondition(AnimatorConditionMode.Equals, pose, "Pose");
                var t1 = a.AddTransition(l); t1.hasExitTime = true; t1.exitTime = 0.95f; t1.duration = 0.05f;
                var t2 = l.AddTransition(e); t2.hasExitTime = false; t2.duration = 0.15f; t2.AddCondition(AnimatorConditionMode.NotEqual, pose, "Pose");
                var t3 = a.AddTransition(e); t3.hasExitTime = false; t3.duration = 0.15f; t3.AddCondition(AnimatorConditionMode.NotEqual, pose, "Pose");
                var t4 = e.AddTransition(loco); t4.hasExitTime = true; t4.exitTime = 0.9f; t4.duration = 0.15f;
            }
            PoseLoop(1, "Sitting_start", "Sitting_loop", "Sitting_end", new Vector3(300, 100));
            PoseLoop(2, "Lie_start", "Lie_loop", "Lie_end", new Vector3(300, 180));
            PoseLoop(3, "Crouch_Idle_start", "Crouch_Idle_loop", "Crouch_Idle_end", new Vector3(300, 260));
            PoseLoop(4, "EatDrink_start", "Eat_loop", "EatDrink_end", new Vector3(300, 340));
            PoseLoop(6, "Digging_start", "Digging_loop", "Digging_end", new Vector3(300, 520));
            PoseLoop(7, "Lie_Sleep_start", "Lie_Sleep_loop", "Lie_Sleep_end", new Vector3(300, 600));
            PoseLoop(8, "Idle_5_start", "Idle_5_loop", "Idle_5_end", new Vector3(300, 680));

            var down = sm.AddState("Down", new Vector3(300, 440)); down.motion = C("Death_L");
            var td = sm.AddAnyStateTransition(down); td.hasExitTime = false; td.duration = 0.2f; td.canTransitionToSelf = false; td.AddCondition(AnimatorConditionMode.Equals, 5, "Pose");
            var tu = down.AddTransition(loco); tu.hasExitTime = false; tu.duration = 0.6f; tu.AddCondition(AnimatorConditionMode.NotEqual, 5, "Pose");

            void OneShot(string name, string clip, string trig, Vector3 at, bool any)
            {
                var st = sm.AddState(name, at); st.motion = C(clip);
                AnimatorStateTransition t = any ? sm.AddAnyStateTransition(st) : loco.AddTransition(st);
                t.hasExitTime = false; t.duration = 0.1f; t.AddCondition(AnimatorConditionMode.If, 0, trig); t.AddCondition(AnimatorConditionMode.NotEqual, 5, "Pose");
                if (any) t.canTransitionToSelf = false;
                var back = st.AddTransition(loco); back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.15f;
                var go = st.AddTransition(loco); go.hasExitTime = false; go.duration = 0.2f; go.AddCondition(AnimatorConditionMode.Greater, 0.4f, "Speed"); // moving cancels the one-shot
            }
            OneShot("Attack", "Attack_F", "Attack", new Vector3(-250, 100), true);
            OneShot("Hit", "Hit_F", "Hit", new Vector3(-250, 180), true);
            OneShot("Happy", "Idle_2", "Happy", new Vector3(-250, 260), false);
            // idle fidgets (FidgetId 2-4 = Idle_2/3/4, 5 = Pissing), only from standing locomotion
            foreach (var (id, clip) in new[] { (2, "Idle_2"), (3, "Idle_3"), (4, "Idle_4"), (5, "Pissing") })
            {
                var st = sm.AddState("Fidget_" + clip, new Vector3(-250, 260 + 80 * id)); st.motion = C(clip);
                var t = loco.AddTransition(st); t.hasExitTime = false; t.duration = 0.2f;
                t.AddCondition(AnimatorConditionMode.If, 0, "Fidget"); t.AddCondition(AnimatorConditionMode.Equals, id, "FidgetId"); t.AddCondition(AnimatorConditionMode.Less, 0.3f, "Speed");
                var back = st.AddTransition(loco); back.hasExitTime = true; back.exitTime = 0.92f; back.duration = 0.2f;
                var go = st.AddTransition(loco); go.hasExitTime = false; go.duration = 0.2f; go.AddCondition(AnimatorConditionMode.Greater, 0.4f, "Speed");
            }
            AssetDatabase.SaveAssets();
            log.Append(" controller built;");
            return ctrl;
        }
    }
}