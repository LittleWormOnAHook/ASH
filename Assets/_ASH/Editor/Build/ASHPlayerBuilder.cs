using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using ASH.Survival;
using Invector.vCamera;
using Invector.vCharacterController;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace ASH.EditorTools.Build
{
    /// <summary>
    /// Builds Assets/_ASH/Prefabs/Player/ASH_Player.prefab the same way Invector's "Create Basic Controller"
    /// window does (template + humanoid model under "3D Model", avatar moved to the template Animator, Player
    /// tag/layer, Main Camera under the vThirdPersonCamera), then swaps vThirdPersonInput for ASHInvectorInput
    /// (in-place script swap, fields kept), applies the Genesis Invector tuning and adds the ASH gameplay
    /// components. Invector source files are not modified.
    /// </summary>
    public static class ASHPlayerBuilder
    {
        public const string TemplatePath = "Assets/Invector-3rdPersonController/Basic Locomotion/Prefabs/vBasicController_Template.prefab";
        public const string ModelPath = "Assets/_ASH/Art/Characters/RuggedMale/rugged_male_character_3d_model.fbx";
        public const string AvatarPath = "Assets/_ASH/Art/Characters/RuggedMale/Player_rugged_male_character_3d_model_HumanoidAvatar.asset";
        public const string TexDir = "Assets/_ASH/Art/Characters/RuggedMale/rugged_male_character_3d_model.fbm/";
        public const string MatPath = "Assets/_ASH/Art/Characters/RuggedMale/Materials/ASH_M_RuggedMale_URP.mat";
        public const string PrefabPath = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        public const float TargetHeight = 1.8f;

        [MenuItem("ASH/Build/2. Player Prefab (Invector + rugged male)")]
        public static GameObject Build()
        {
            // Never wipe hand placements: a full rebuild would erase the pre-placed Held_* weapons/items (and Holder_Hip).
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing && existing.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith(ASHEquipment.HeldPrefix) || t.name == ASHEquipment.HipHolderName))
            {
                Debug.LogError("[ASH] Player Prefab rebuild refused: ASH_Player.prefab has hand-placed held items (Held_*). A rebuild would erase them. Edit the prefab directly, or move/rename it first if you really want a fresh build.");
                return existing;
            }
            var templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!templateAsset || !modelAsset) throw new System.Exception("Missing Invector template or rugged male model.");

            var root = (GameObject)PrefabUtility.InstantiatePrefab(templateAsset);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "ASH_Player";
            root.transform.position = Vector3.zero;

            // Invector's demo UI/tutorial canvases are replaced by the ASH HUD.
            foreach (var n in new[] { "UI", "TutorialCanvas" }) { var t = FindDeep(root.transform, n); if (t) Object.DestroyImmediate(t.gameObject); }

            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var modelParent = root.transform.Find("3D Model");
            if (!modelParent) { modelParent = new GameObject("3D Model").transform; modelParent.SetParent(root.transform, false); }
            model.transform.SetParent(modelParent, false);
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;

            var modelAnimator = model.GetComponent<Animator>();
            var rootAnimator = root.GetComponent<Animator>();
            Avatar avatar = modelAnimator && modelAnimator.avatar && modelAnimator.avatar.isValid ? modelAnimator.avatar : AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
            rootAnimator.avatar = avatar;
            if (modelAnimator) Object.DestroyImmediate(modelAnimator);

            // Scale to ~1.8 m.
            var bounds = RendererBounds(model);
            float h = bounds.size.y;
            if (h > 0.1f) model.transform.localScale = Vector3.one * (TargetHeight / h);

            // URP material from the Kade texture set (source material was authored for HDRP).
            var mat = BuildMaterial();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = mat; r.sharedMaterials = mats;
            }

            int playerLayer = LayerMask.NameToLayer("Player");
            root.tag = "Player"; model.tag = "Player";
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.GetComponent<vThirdPersonCamera>() == null && t.GetComponentInParent<vThirdPersonCamera>() == null && t.GetComponent<RectTransform>() == null)
                    t.gameObject.layer = playerLayer;

            // Camera
            var tp = root.GetComponentInChildren<vThirdPersonCamera>(true);
            var camGo = new GameObject("ASH_PlayerCamera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>(); cam.nearClipPlane = 0.08f; cam.farClipPlane = 4500f; cam.fieldOfView = 60f;
            camGo.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            camGo.transform.SetParent(tp ? tp.transform : root.transform, false);
            if (tp) ASHBuildUtil.Set(tp, "cullingLayer", ~(1 << playerLayer) & ~(1 << 2) & ~(1 << 5)); // camera clipping ignores player, IgnoreRaycast, UI

            // Swap Invector input for the ASH adapter (keeps serialized fields; Invector code unchanged).
            var tpInput = root.GetComponent<vThirdPersonInput>();
            var so = new SerializedObject(tpInput);
            so.FindProperty("m_Script").objectReferenceValue = FindScript("ASHInvectorInput");
            so.ApplyModifiedPropertiesWithoutUndo();
            var ashInvInput = root.GetComponent<ASHInvectorInput>();
            if (ashInvInput == null) throw new System.Exception("Script swap to ASHInvectorInput failed.");

            // Genesis Invector tuning (Player_Kade_v2 / PlayerInvectorRuntimeSetup), reduced to the slice.
            var cc = root.GetComponent<vThirdPersonController>();
            cc.freeSpeed.walkSpeed = 3f; cc.freeSpeed.runningSpeed = 4f; cc.freeSpeed.sprintSpeed = 6f; cc.freeSpeed.crouchSpeed = 2f;
            cc.freeSpeed.movementSmooth = 6f; cc.freeSpeed.animationSmooth = 0.2f; cc.freeSpeed.rotationSpeed = 10f;
            cc.useRootMotion = false; cc.useContinuousSprint = true;
            cc.jumpHeight = 2f; cc.airSpeed = 5f; cc.extraGravity = -10f;
            cc.useSnapGround = true; cc.snapPower = 0.5f; cc.groundMinDistance = 0.1f; cc.groundMaxDistance = 0.5f;
            cc.slopeLimit = 75f;
            cc.maxStamina = 100f; cc.staminaRecovery = 1.2f; cc.sprintStamina = 12f; cc.jumpStamina = 12f;
            cc.groundLayer = LayerMask.GetMask("Default", "PW_Object_Medium", "PW_Object_Large");
            cc.autoCrouchLayer = LayerMask.GetMask("Default", "PW_Object_Medium", "PW_Object_Large");
            cc.fallDamage = 0f; // no fall damage in the slice (stats own health)
            EditorUtility.SetDirty(cc);

            // ASH gameplay components
            var input = root.AddComponent<ASHInput>();
            ASHBuildUtil.Set(input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(ASHContentBuilder.InputPath));
            ASHBuildUtil.Set(ashInvInput, "ashInput", input);
            var inv = root.AddComponent<ASHInventory>();
            var stats = root.AddComponent<ASHSurvivalStats>();
            ASHBuildUtil.Set(stats, "tuning", AssetDatabase.LoadAssetAtPath<ASHSurvivalTuning>(ASHContentBuilder.TuningPath));
            var link = root.AddComponent<ASHInvectorStaminaLink>();
            ASHBuildUtil.Set(link, "controller", cc); ASHBuildUtil.Set(link, "stats", stats);
            var ix = root.AddComponent<ASHInteractor>();
            ASHBuildUtil.Set(ix, "input", input); ASHBuildUtil.Set(ix, "viewCamera", cam); ASHBuildUtil.Set(ix, "inventory", inv); ASHBuildUtil.Set(ix, "stats", stats);
            ASHBuildUtil.Set(ix, "interactMask", ~(1 << playerLayer) & ~(1 << 2));
            var craft = root.AddComponent<ASHCrafting>();
            ASHBuildUtil.Set(craft, "inventory", inv);
            ASHBuildUtil.SetList(craft, "recipes", new Object[] { ASHContentBuilder.LoadRecipe() });
            var placer = root.AddComponent<ASHPlacer>();
            ASHBuildUtil.Set(placer, "input", input); ASHBuildUtil.Set(placer, "inventory", inv); ASHBuildUtil.Set(placer, "interactor", ix); ASHBuildUtil.Set(placer, "viewCamera", cam);
            ASHBuildUtil.Set(placer, "groundMask", LayerMask.GetMask("Default"));
            ASHBuildUtil.Set(placer, "blockMask", ~(1 << 2) & ~LayerMask.GetMask("Triggers"));
            var death = root.AddComponent<ASHPlayerDeath>();
            ASHBuildUtil.Set(death, "stats", stats); ASHBuildUtil.Set(death, "controller", cc);

            var prefab = ASHBuildUtil.SavePrefab(root, PrefabPath);
            Debug.Log($"[ASH] Player prefab built: model height {h:F2} -> scale {(h > 0.1f ? TargetHeight / h : 1f):F3}, avatar={(avatar ? avatar.name : "none")}, layer Player={playerLayer}");
            return prefab;
        }

        static MonoScript FindScript(string className)
        {
            foreach (var g in AssetDatabase.FindAssets(className + " t:MonoScript"))
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(g));
                if (ms != null && ms.GetClass() != null && ms.GetClass().Name == className) return ms;
            }
            throw new System.Exception("MonoScript not found: " + className);
        }

        static Material BuildMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, MatPath); }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "Kade_Color.jpg");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "Kade_Normal.jpg");
            var metal = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "Kade_metallic_Smoothness.png");
            var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "Kade_AO.png");
            if (normal)
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(normal));
                if (imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; imp.SaveAndReimport(); }
            }
            m.SetTexture("_BaseMap", baseMap); m.SetColor("_BaseColor", Color.white);
            if (normal) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            if (metal) { m.SetTexture("_MetallicGlossMap", metal); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 0.6f); m.SetFloat("_SmoothnessTextureChannel", 0f); }
            else { m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.3f); }
            if (ao) { m.SetTexture("_OcclusionMap", ao); m.EnableKeyword("_OCCLUSIONMAP"); m.SetFloat("_OcclusionStrength", 1f); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
            return null;
        }
    }
}
