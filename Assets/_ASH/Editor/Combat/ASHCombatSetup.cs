using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.Combat;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using ASH.World;
using Invector;
using Invector.vItemManager;
using Invector.vMelee;
using Invector.vShooter;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// ASH/Systems/Combat: switches the ASH player to the full Invector shooter + melee (Option B) without moving any hand
    /// placement. Idempotent: re-running keeps hand-tuned muzzle / aim / support-hand markers and edited data assets.
    /// 1 Set Up Player Combat: backup ASH_Player_PreCombat, ASH_ShooterMelee animator (Invector ShooterMelee + ASH action
    ///   layers), Invector managers, Invector weapon setups on Held_ models (Hatchet melee; Pistol = Invector handgun,
    ///   Hunting Rifle = Invector sniper, Shotgun = Invector shotgun model + setup, Wooden Bow = Invector bow/archery),
    ///   ASH arrow projectile, weapon data (Genesis numbers), combat tuning, Shotgun + Shotgun Shells items, pickups and crate loot.
    /// 2 Place Training Dummy at Cabin Campfire (scene left unsaved).
    /// </summary>
    public static class ASHCombatSetup
    {
        const string Inv = "Assets/Invector-3rdPersonController/";
        const string PlayerPath = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        const string BackupPath = "Assets/_ASH/Prefabs/Player/ASH_Player_PreCombat.prefab";
        const string CtrlPath = "Assets/_ASH/Animation/ASH_ShooterMelee.controller";
        const string InvCtrl = Inv + "Shooter/Animator/Invector@ShooterMelee.controller";
        const string TemplatePlayer = Inv + "Shooter/Prefabs/Player/vShooterMelee_NoInventory.prefab";
        const string TplHandgun = Inv + "Shooter/Prefabs/Weapon/_weapons_WITHOUT_Inventory/_weaponsPrefabs/vHandgun_NO_Inventory Variant.prefab";
        const string TplSniper = Inv + "Shooter/Prefabs/Weapon/_weapons_WITHOUT_Inventory/_weaponsPrefabs/vSniper_NO_Inventory Variant.prefab";
        const string TplShotgun = Inv + "Shooter/Prefabs/Weapon/_weapons_WITHOUT_Inventory/_weaponsPrefabs/vShotgun_NO_Inventory Variant.prefab";
        const string TplBow = Inv + "Shooter/Scripts/ArcherySystem/Prefabs/NoInventory/vBow_NoInventory.prefab";
        const string TplArrow = Inv + "Shooter/Scripts/ArcherySystem/Prefabs/NoInventory/ArrowProjectile_NoInventory.prefab";
        const string TplAxe = Inv + "Melee Combat/Prefabs/Weapons/NoInventory/Axe_NOInventory.prefab";
        const string ShooterUI = Inv + "Shooter/Prefabs/UI/ShooterUI.prefab";
        const string ShooterCam = Inv + "Basic Locomotion/Scripts/CharacterCreator/CameraStates/vShooterMelee@CameraState.asset";
        const string ArrowOut = "Assets/_ASH/Prefabs/Combat/ASH_ArrowProjectile.prefab";
        const string ItemsDir = "Assets/_ASH/Data/Items/";
        const string PickupsDir = "Assets/_ASH/Prefabs/Pickups/";
        const string WeaponsDir = "Assets/_ASH/Resources/ASH_Weapons/";
        const string TuningPath = "Assets/_ASH/Resources/ASH_CombatTuning.asset";
        const string DummyDir = "Assets/_ASH/Art/Combat/DummyTarget/";
        const string DummyOut = "Assets/_ASH/Prefabs/Combat/ASH_TrainingDummy.prefab";

        // Invector ammo ids used by ASH weapons (vAmmoManager reserve is backed by the ASH pack)
        const int AmmoRifle = 1, AmmoPistol = 2, AmmoArrow = 3, AmmoShells = 4;

        [MenuItem("ASH/Systems/Combat/1 Set Up Player Combat (Invector Shooter + Melee)", priority = 10)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Combat: stop Play mode first."); return; }
            var sb = new StringBuilder();
            try { Build(sb); }
            catch (System.Exception e) { Debug.LogError("[ASH] Combat setup failed: " + e); }
            Debug.Log("[ASH] Combat: " + sb);
        }

        static void Build(StringBuilder sb)
        {
            Dir("Assets/_ASH/Prefabs/Combat"); Dir("Assets/_ASH/Resources/ASH_Weapons");
            // 1 backup (full copy, not a variant, so it never follows later changes)
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(BackupPath)) { AssetDatabase.CopyAsset(PlayerPath, BackupPath); sb.Append("backup ASH_Player_PreCombat made; "); }
            // 2 animator
            if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(CtrlPath)) { AssetDatabase.CopyAsset(InvCtrl, CtrlPath); sb.Append("ASH_ShooterMelee.controller copied; "); }
            AssetDatabase.Refresh();
            sb.Append("action layers: ").Append(ASH.EditorTools.Build.ASHActionAnimSetup.Build(CtrlPath)).Append("; ");
            // 3 tuning + refs
            var tuning = AssetDatabase.LoadAssetAtPath<ASHCombatTuning>(TuningPath);
            if (!tuning) { tuning = ScriptableObject.CreateInstance<ASHCombatTuning>(); tuning.ApplyPreset(ASHCombatTuning.Preset.Default); AssetDatabase.CreateAsset(tuning, TuningPath); sb.Append("tuning created; "); }
            tuning.shooterUiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShooterUI);
            tuning.shooterCameraStates = AssetDatabase.LoadAssetAtPath<vThirdPersonCameraListData>(ShooterCam);
            if (!tuning.fleshHit) tuning.fleshHit = FindPrefab("WFX_BImpact SoftBody", "WFX_BImpact Blood", "vBloodSplatter");
            if (!tuning.woodHit) tuning.woodHit = FindPrefab("WFX_BImpact Wood");
            if (!tuning.stoneHit) tuning.stoneHit = FindPrefab("WFX_BImpact Concrete", "WFX_BImpact Dirt");
            if (!tuning.defaultHit) tuning.defaultHit = FindPrefab("WFX_BImpact Dirt", "WFX_BImpact Concrete");
            if (!tuning.muzzleFlash) tuning.muzzleFlash = FindPrefab("WFX_MF 4P RIFLE1", "WFX_MF Spr RIFLE1");
            EditorUtility.SetDirty(tuning);
            sb.Append($"fx flesh={N(tuning.fleshHit)} wood={N(tuning.woodHit)} stone={N(tuning.stoneHit)} muzzle={N(tuning.muzzleFlash)}; ");

            // 4 items: shotgun + shells, pickups, registry, crate loot
            var rifle = Item("ASH_Item_HuntingRifle"); var pistol = Item("ASH_Item_Pistol"); var bow = Item("ASH_Item_WoodenBow"); var hatchet = Item("ASH_Item_Hatchet");
            var shells = EnsureShells(sb); var shotgun = EnsureShotgun(sb, shells);
            EnsureLoot(sb, shells);

            // 5 weapon data (created once; edits are kept)
            Data("ASH_Weapon_Hatchet", hatchet, ASHWeaponKind.Melee, d => { d.damage = 26; d.damageRandom = 4; d.critChance = 0.12f; d.critMultiplier = 2; d.reach = 2.4f; d.staminaCost = 10; d.knockback = 2.5f; d.bodyWeight = 1.1f; d.invectorTemplate = TplAxe; CopyAxeAnimIds(d); }, sb);
            Data("ASH_Weapon_HuntingRifle", rifle, ASHWeaponKind.Gun, d => { d.damage = 18; d.damageRandom = 4; d.range = 55; d.projectileSpeed = 220; d.cameraKick = 1.6f; d.invectorTemplate = TplSniper; d.critChance = 0; }, sb);
            Data("ASH_Weapon_Pistol", pistol, ASHWeaponKind.Gun, d => { d.damage = 14; d.damageRandom = 4; d.range = 32; d.projectileSpeed = 180; d.cameraKick = 0.9f; d.invectorTemplate = TplHandgun; d.critChance = 0; }, sb);
            Data("ASH_Weapon_Shotgun", shotgun, ASHWeaponKind.Gun, d => { d.useInvectorDamage = true; d.range = 20; d.cameraKick = 2.2f; d.invectorTemplate = TplShotgun; d.critChance = 0; }, sb);
            Data("ASH_Weapon_WoodenBow", bow, ASHWeaponKind.Bow, d => { d.damage = 14; d.damageRandom = 3; d.range = 60; d.projectileSpeed = 70; d.drawSeconds = 1.1f; d.fullDrawDamageMultiplier = 2.5f; d.fullDrawSpeedMultiplier = 2f; d.invectorTemplate = TplBow; d.critChance = 0; }, sb);
            ASHWeaponData.ClearCache();

            // 6 arrow projectile (ASH Wooden Arrow model on Invector's archery projectile)
            var arrowPrefab = EnsureArrowProjectile(sb, AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath));

            // 7 player prefab
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                SetupPlayer(root, sb, arrowPrefab, shotgun);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            // 8 scene instance: animator controller override (if any) follows the prefab
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CtrlPath);
            foreach (var inp in Object.FindObjectsByType<ASH.Player.ASHInvectorInput>(FindObjectsInactive.Include))
            {
                var an = inp.GetComponent<Animator>();
                if (an && an.runtimeAnimatorController != ctrl) { Undo.RecordObject(an, "ASH combat"); an.runtimeAnimatorController = ctrl; EditorSceneManager.MarkSceneDirty(inp.gameObject.scene); sb.Append("scene player animator override updated (scene not saved); "); }
            }
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- player

        static void SetupPlayer(GameObject root, StringBuilder sb, GameObject arrowPrefab, ASHItemDefinition shotgunItem)
        {
            var an = root.GetComponentInChildren<Animator>(true);
            if (an) an.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CtrlPath);
            var host = root.GetComponent<ASH.Player.ASHInvectorInput>() ? root : root.GetComponentInChildren<ASH.Player.ASHInvectorInput>(true).gameObject;
            var tpl = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePlayer);

            var sm = host.GetComponent<vShooterManager>(); bool newSm = !sm; if (!sm) sm = host.AddComponent<vShooterManager>();
            if (newSm && tpl) CopyFrom(tpl.GetComponent<vShooterManager>(), sm, TemplatePlayer);
            sm.useAmmoDisplay = false; sm.AllAmmoInfinity = false;
            int playerLayer = host.layer;
            sm.damageLayer = ~((1 << playerLayer) | (1 << 2) | (1 << 5));
            if (!sm.ignoreTags.Contains("Player")) sm.ignoreTags.Add("Player");

            var mm = host.GetComponent<vMeleeManager>(); bool newMm = !mm; if (!mm) mm = host.AddComponent<vMeleeManager>();
            if (newMm && tpl) CopyFrom(tpl.GetComponent<vMeleeManager>(), mm, TemplatePlayer);
            mm.Members.Clear(); mm.leftWeapon = null; mm.rightWeapon = null;
            if (mm.hitProperties == null) mm.hitProperties = new HitProperties();
            mm.hitProperties.hitDamageTags = new List<string> { "Enemy", "Animal" };

            var am = host.GetComponent<vAmmoManager>(); if (!am) am = host.AddComponent<vAmmoManager>();
            am.ammoListData = null; am.ammos.Clear();
            if (!host.GetComponent<ASHCombatBridge>()) host.AddComponent<ASHCombatBridge>();
            sb.Append($"player: managers on '{host.name}', damageLayer excludes layer {playerLayer}; ");

            var all = root.GetComponentsInChildren<Transform>(true);
            GameObject Held(string n) { var t = all.FirstOrDefault(x => x.name == n); return t ? t.gameObject : null; }
            var rightHand = an && an.isHuman ? an.GetBoneTransform(HumanBodyBones.RightHand) : null;

            // melee: hatchet
            var hatchet = Held("Held_Hatchet");
            if (hatchet) SetupMelee(hatchet, sb); else sb.Append("Held_Hatchet missing; ");
            // guns on ASH models
            var rifleGo = Held("Held_HuntingRifle"); if (rifleGo) SetupGun(rifleGo, TplSniper, AmmoRifle, false, sb); else sb.Append("Held_HuntingRifle missing; ");
            var pistolGo = Held("Held_Pistol"); if (pistolGo) SetupGun(pistolGo, TplHandgun, AmmoPistol, true, sb); else sb.Append("Held_Pistol missing; ");
            // shotgun: Invector model + setup (swap the model later: put the new mesh under Held_Shotgun/Model)
            var sg = Held("Held_Shotgun");
            if (!sg && rightHand) sg = MakeInvectorShotgun(rightHand, rifleGo, sb);
            if (sg) SetupGun(sg, TplShotgun, AmmoShells, false, sb, keepInvectorMarkers: true);
            // bow
            var bowGo = Held("Held_WoodenBow"); if (bowGo) SetupBow(bowGo, arrowPrefab, sb); else sb.Append("Held_WoodenBow missing; ");
        }

        static GameObject MakeInvectorShotgun(Transform rightHand, GameObject rifleRef, StringBuilder sb)
        {
            var tpl = AssetDatabase.LoadAssetAtPath<GameObject>(TplShotgun);
            var w = tpl ? tpl.GetComponentInChildren<vShooterWeapon>(true) : null;
            if (!w) { sb.Append("Invector shotgun template missing; "); return null; }
            var inst = Object.Instantiate(w.gameObject);
            inst.name = "Held_Shotgun";
            inst.transform.SetParent(rightHand, false);
            // start from the hunting rifle's hand pose (a long gun held the same way); hand-place it after
            if (rifleRef && rifleRef.transform.parent == rightHand) { inst.transform.localPosition = rifleRef.transform.localPosition; inst.transform.localRotation = rifleRef.transform.localRotation; }
            else { inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; }
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) if (mb && !(mb is vShooterWeapon) && mb.GetType().Namespace != null && mb.GetType().Name.Contains("Collect")) Object.DestroyImmediate(mb);
            // group the visible mesh under "Model" so it can be swapped without touching the weapon setup
            var model = new GameObject("Model").transform; model.SetParent(inst.transform, false);
            foreach (Transform ch in inst.transform.Cast<Transform>().ToList())
                if (ch != model && ch.GetComponentInChildren<Renderer>(true) && !ch.GetComponentInChildren<vShooterWeapon>(true)) ch.SetParent(model, true);
            var mr = inst.GetComponent<MeshRenderer>(); if (mr) sb.Append("(shotgun mesh is on the weapon root; swap via its MeshFilter) ");
            inst.SetActive(false);
            sb.Append("Held_Shotgun created from Invector shotgun at the rifle's hand pose (hand-place it); ");
            return inst;
        }

        static void SetupMelee(GameObject go, StringBuilder sb)
        {
            var w = go.GetComponent<vMeleeWeapon>(); bool fresh = !w; if (!w) w = go.AddComponent<vMeleeWeapon>();
            var tpl = AssetDatabase.LoadAssetAtPath<GameObject>(TplAxe); var tw = tpl ? tpl.GetComponentInChildren<vMeleeWeapon>(true) : null;
            if (fresh && tw) CopyFrom(tw, w, TplAxe);
            w.meleeType = vMeleeType.OnlyAttack; w.useStrongAttack = false; w.meleeManager = null; w.overrideDamageSender = null;
            var data = AssetDatabase.LoadAssetAtPath<ASHWeaponData>(WeaponsDir + "ASH_Weapon_Hatchet.asset");
            var hb = go.transform.Find("ASH_HitBox");
            if (!hb)
            {
                hb = new GameObject("ASH_HitBox").transform; hb.SetParent(go.transform, false); hb.gameObject.layer = go.layer;
                LocalBounds(go, out var b);
                int ax = Longest(b.size);
                var handL = go.transform.InverseTransformPoint(go.transform.parent ? go.transform.parent.position : go.transform.position);
                Vector3 lo = b.center, hi = b.center; lo[ax] = b.min[ax]; hi[ax] = b.max[ax];
                bool hiIsHead = (hi - handL).sqrMagnitude >= (lo - handL).sqrMagnitude;
                var grip = hiIsHead ? lo : hi; var head = hiIsHead ? hi : lo;
                hb.localPosition = Vector3.Lerp(grip, head, data ? data.hitBoxAlong : 0.82f);
                var size = data ? data.hitBoxSize : new Vector3(0.18f, 0.2f, 0.22f);
                var box = hb.gameObject.AddComponent<BoxCollider>(); box.isTrigger = true;
                var ls = go.transform.lossyScale; box.size = new Vector3(size.x / Mathf.Abs(ls.x), size.y / Mathf.Abs(ls.y), size.z / Mathf.Abs(ls.z));
            }
            var hit = hb.GetComponent<vHitBox>(); if (!hit) hit = hb.gameObject.AddComponent<vHitBox>();
            hit.attackObject = w; hit.damagePercentage = 100; hit.triggerType = vHitBoxType.Damage | vHitBoxType.Recoil;
            var trig = hb.GetComponent<Collider>(); if (trig) { trig.isTrigger = true; trig.enabled = false; } // Invector enables it during the swing
            w.hitBoxes = new List<vHitBox> { hit };
            if (w.damage == null) w.damage = new vDamage(26);
            ClearEvents(w);
            sb.Append($"hatchet melee ok ({(fresh ? "new" : "kept")}, attackID {w.attackID}); ");
        }

        static void SetupGun(GameObject go, string templatePath, int ammoId, bool pistol, StringBuilder sb, bool keepInvectorMarkers = false)
        {
            var tpl = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            var tw = tpl ? tpl.GetComponentInChildren<vShooterWeapon>(true) : null;
            var w = go.GetComponent<vShooterWeapon>(); bool fresh = !w;
            if (!w) w = go.AddComponent<vShooterWeapon>();
            if (fresh && tw && !keepInvectorMarkers) CopyFrom(tw, w, templatePath);
            w.ammoID = ammoId; w.isLeftWeapon = false; w.dontUseReload = false; w.isInfinityAmmo = false; w.root = null;
            if (!keepInvectorMarkers) { w.lightOnShot = null; w.emittShurykenParticle = new ParticleSystem[0]; w.zoomScopeCamera = null; w.scopeTarget = null; }
            if (tw && !w.projectile) w.projectile = tw.projectile;
            if (!keepInvectorMarkers)
            {
                LocalBounds(go, out var b);
                // ASH held guns: barrel along local +Z (as ASHWeaponAim aims them), butt at min Z
                var muzzlePos = new Vector3(b.center.x, b.center.y + b.extents.y * 0.35f, b.max.z);
                w.muzzle = Marker(go, "ASH_Muzzle", muzzlePos);
                w.aimReference = Marker(go, "ASH_AimReference", muzzlePos);
                w.handIKTarget = Marker(go, "ASH_SupportHandIK", pistol ? new Vector3(b.center.x, b.center.y - b.extents.y * 0.2f, Mathf.Lerp(b.min.z, b.max.z, 0.3f)) : new Vector3(b.center.x, b.center.y - b.extents.y * 0.25f, Mathf.Lerp(b.min.z, b.max.z, 0.62f)));
            }
            var src = go.GetComponent<AudioSource>(); if (!src) { src = go.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f; src.maxDistance = 120f; }
            w.source = src; if (!w.reloadSource) w.reloadSource = src;
            ClearEvents(w);
            sb.Append($"{go.name}: Invector {System.IO.Path.GetFileNameWithoutExtension(templatePath)} ({(fresh ? "new" : "kept")}, category {w.weaponCategory}, clip {w.clipSize}); ");
        }

        static void SetupBow(GameObject go, GameObject arrowPrefab, StringBuilder sb)
        {
            var tpl = AssetDatabase.LoadAssetAtPath<GameObject>(TplBow);
            var tw = tpl ? tpl.GetComponentInChildren<vShooterWeapon>(true) : null;
            var w = go.GetComponent<vShooterWeapon>(); bool fresh = !w;
            if (!w) w = go.AddComponent<vShooterWeapon>();
            if (fresh && tw) CopyFrom(tw, w, TplBow);
            var bc = go.GetComponent<vBowControl>(); if (!bc) bc = go.AddComponent<vBowControl>();
            var tbc = tpl ? tpl.GetComponentInChildren<vBowControl>(true) : null;
            if (fresh && tbc) CopyFrom(tbc, bc, TplBow);
            w.ammoID = AmmoArrow; w.isLeftWeapon = true; w.dontUseReload = true; w.isInfinityAmmo = false; w.chargeWeapon = true; w.root = null;
            w.lightOnShot = null; w.emittShurykenParticle = new ParticleSystem[0]; w.zoomScopeCamera = null; w.scopeTarget = null;
            if (arrowPrefab) w.projectile = arrowPrefab;
            // arrow rest: riser side away from the string, a little above the grip; arrows fly away from the string
            float sign = BowStringSign(go, out var b);
            var fwdL = new Vector3(-sign, 0f, 0f);
            var rest = new Vector3(b.center.x - sign * b.extents.x * 0.6f, 0f, b.center.z + b.size.z * 0.03f);
            w.muzzle = Marker(go, "ASH_ArrowRest", rest, fwdL);
            w.aimReference = Marker(go, "ASH_AimReference", rest, fwdL);
            w.handIKTarget = null;
            var src = go.GetComponent<AudioSource>(); if (!src) { src = go.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f; }
            w.source = src; w.fireClip = null; w.emptyClip = null;
            ClearEvents(w); ClearEvents(bc);
            sb.Append($"Held_WoodenBow: Invector bow ({(fresh ? "new" : "kept")}, category {w.weaponCategory}, string side {(sign > 0 ? "+X" : "-X")}); ");
        }

        // ---------------------------------------------------------------- arrow projectile

        static GameObject EnsureArrowProjectile(StringBuilder sb, GameObject playerPrefab)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowOut);
            if (existing) return existing;
            var tpl = AssetDatabase.LoadAssetAtPath<GameObject>(TplArrow);
            if (!tpl) { sb.Append("Invector arrow projectile missing; "); return null; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(tpl);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "ASH_ArrowProjectile";
            var arrow = inst.GetComponent<vArrow>();
            var detach = arrow ? arrow.detachObject : null;
            if (!detach || detach == inst.transform) { detach = new GameObject("ArrowVisual").transform; detach.SetParent(inst.transform, false); if (arrow) arrow.detachObject = detach; }
            if (arrow) arrow.alignToNormal = false; // keep the flight direction when it sticks
            // remove the Invector arrow look and its own pickup / UI, keep the projectile logic on the root
            foreach (Transform ch in detach.Cast<Transform>().ToList()) Object.DestroyImmediate(ch.gameObject);
            foreach (var c in detach.GetComponents<Component>().Reverse()) if (!(c is Transform)) Object.DestroyImmediate(c);
            foreach (Transform ch in inst.transform.Cast<Transform>().ToList()) if (!detach.IsChildOf(ch) && !ch.GetComponentInChildren<TrailRenderer>(true)) Object.DestroyImmediate(ch.gameObject);
            // ASH Wooden Arrow model: from ASHWeaponAim.arrowPrefab on the player, else the arrow pickup model
            GameObject model = null;
            var aim = playerPrefab ? playerPrefab.GetComponentInChildren<ASHWeaponAim>(true) : null;
            var so = aim ? new SerializedObject(aim) : null; var ap = so != null ? so.FindProperty("arrowPrefab") : null;
            if (ap != null && ap.objectReferenceValue is GameObject g1) model = g1;
            if (!model) { var it = Item("ASH_Item_WoodenArrow"); if (it) model = it.worldPickupPrefab; }
            if (model)
            {
                var m = Object.Instantiate(model); m.name = "WoodenArrow";
                foreach (var c in m.GetComponentsInChildren<Component>(true)) if (c && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)) Object.DestroyImmediate(c);
                m.transform.SetParent(detach, false); m.transform.localPosition = Vector3.zero; m.transform.localRotation = Quaternion.identity;
                // orient so the tip points +Z with the tip at the origin (where vArrow sticks it)
                if (ASHWeaponAim.ArrowEndsWorld(m, out var tip, out var tail))
                {
                    var dirW = (tip - tail).normalized;
                    m.transform.rotation = Quaternion.FromToRotation(dirW, detach.forward) * m.transform.rotation;
                    ASHWeaponAim.ArrowEndsWorld(m, out tip, out tail);
                    m.transform.position += detach.position - tip;
                }
            }
            else sb.Append("no ASH arrow model found (projectile has no visual); ");
            var rec = detach.gameObject.AddComponent<ASHArrowRecover>(); rec.arrowItem = Item("ASH_Item_WoodenArrow");
            var saved = PrefabUtility.SaveAsPrefabAsset(inst, ArrowOut);
            Object.DestroyImmediate(inst);
            sb.Append("ASH_ArrowProjectile built; ");
            return saved;
        }

        // ---------------------------------------------------------------- items / loot

        static ASHItemDefinition EnsureShells(StringBuilder sb)
        {
            var path = ItemsDir + "ASH_Item_ShotgunShells.asset";
            var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(path);
            if (!it)
            {
                AssetDatabase.CopyAsset(ItemsDir + "ASH_Item_RifleRounds.asset", path);
                it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(path);
                it.itemId = "shotgunshells"; it.displayName = "12 Gauge Shells";
                var icon = FindSprite("shotgun_shell", "shotgunshell", "shell", "shotgunammo", "ammo_shotgun");
                if (icon) it.icon = icon;
                sb.Append($"Shotgun Shells item created (icon {(icon ? icon.name : "= rifle rounds icon")}); ");
            }
            // pickup
            var pp = PickupsDir + "ASH_Pickup_ShotgunShells.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(pp)) { AssetDatabase.CopyAsset(PickupsDir + "ASH_Pickup_RifleRounds.prefab", pp); sb.Append("ASH_Pickup_ShotgunShells created; "); }
            var pg = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
            SetPickupItem(pp, it, 8);
            it.worldPickupPrefab = pg; EditorUtility.SetDirty(it);
            Register(it);
            return it;
        }

        static ASHItemDefinition EnsureShotgun(StringBuilder sb, ASHItemDefinition shells)
        {
            var path = ItemsDir + "ASH_Item_Shotgun.asset";
            var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(path);
            if (!it)
            {
                AssetDatabase.CopyAsset(ItemsDir + "ASH_Item_HuntingRifle.asset", path);
                it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(path);
                it.itemId = "shotgun"; it.displayName = "Pump Shotgun"; it.heldPrefab = null;
                var icon = FindSprite("shotgun_icon", "icon_shotgun", "shotgun");
                if (icon) it.icon = icon;
                sb.Append($"Shotgun item created (icon {(icon ? icon.name : "= rifle icon")}); ");
            }
            it.ammoItem = shells;
            // world pickup: Invector shotgun model with the ASH pickup (copy of the rifle pickup with its model swapped)
            var pp = PickupsDir + "ASH_Pickup_Shotgun.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(pp))
            {
                var baseP = AssetDatabase.LoadAssetAtPath<GameObject>(PickupsDir + "ASH_Pickup_HuntingRifle.prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>(PickupsDir + "ASH_Pickup_Pistol.prefab");
                var tpl = AssetDatabase.LoadAssetAtPath<GameObject>(TplShotgun); var tw = tpl ? tpl.GetComponentInChildren<vShooterWeapon>(true) : null;
                if (baseP && tw)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(baseP); PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    inst.name = "ASH_Pickup_Shotgun";
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    var m = Object.Instantiate(tw.gameObject); m.name = "Model";
                    foreach (var c in m.GetComponentsInChildren<Component>(true)) if (c && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer) && !(c is SkinnedMeshRenderer)) Object.DestroyImmediate(c);
                    m.transform.SetParent(inst.transform, false); m.transform.localPosition = Vector3.up * 0.05f; m.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    PrefabUtility.SaveAsPrefabAsset(inst, pp); Object.DestroyImmediate(inst);
                    sb.Append("ASH_Pickup_Shotgun created; ");
                }
            }
            SetPickupItem(pp, it, 1);
            var pg = AssetDatabase.LoadAssetAtPath<GameObject>(pp); if (pg) it.worldPickupPrefab = pg;
            EditorUtility.SetDirty(it);
            Register(it);
            return it;
        }

        static void SetPickupItem(string prefabPath, ASHItemDefinition it, int amount)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var pk = root.GetComponentInChildren<ASHItemPickup>(true);
                if (pk) { var so = new SerializedObject(pk); so.FindProperty("item").objectReferenceValue = it; var a = so.FindProperty("amount"); if (a != null) a.intValue = amount; so.ApplyModifiedPropertiesWithoutUndo(); }
                root.name = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void Register(ASHItemDefinition it)
        {
            var reg = Resources.Load<ASHItemRegistry>("ASH_ItemRegistry");
            if (reg && !reg.items.Contains(it)) { reg.items.Add(it); EditorUtility.SetDirty(reg); }
        }

        static void EnsureLoot(StringBuilder sb, ASHItemDefinition shells)
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHPoiResourceSettings>("Assets/_ASH/Data/World/ASH_PoiResourceSettings.asset");
            if (s)
            {
                if (s.crateLoot == null) s.crateLoot = new List<ASHPoiResourceSettings.LootEntry>();
                if (!s.crateLoot.Any(e => e != null && e.item == shells)) { s.crateLoot.Add(new ASHPoiResourceSettings.LootEntry { item = shells, weight = 1f, amountMin = 2, amountMax = 6 }); EditorUtility.SetDirty(s); sb.Append("shells added to crate loot table; "); }
            }
            int n = 0;
            foreach (var c in Object.FindObjectsByType<ASHSupplyCrate>(FindObjectsInactive.Include))
            {
                if (!c.gameObject.scene.IsValid()) continue;
                var so = new SerializedObject(c); var loot = so.FindProperty("loot"); bool has = false;
                for (int i = 0; i < loot.arraySize; i++) if (loot.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == shells) has = true;
                if (has) continue;
                loot.arraySize++; var e = loot.GetArrayElementAtIndex(loot.arraySize - 1);
                e.FindPropertyRelative("item").objectReferenceValue = shells; e.FindPropertyRelative("weight").floatValue = 1f;
                e.FindPropertyRelative("amountMin").intValue = 2; e.FindPropertyRelative("amountMax").intValue = 6;
                so.ApplyModifiedProperties(); EditorSceneManager.MarkSceneDirty(c.gameObject.scene); n++;
            }
            if (n > 0) sb.Append($"shells added to {n} placed crates (scene not saved); ");
        }

        // ---------------------------------------------------------------- data

        static void Data(string name, ASHItemDefinition item, ASHWeaponKind kind, System.Action<ASHWeaponData> init, StringBuilder sb)
        {
            if (!item) { sb.Append($"{name}: item missing; "); return; }
            var path = WeaponsDir + name + ".asset";
            var d = AssetDatabase.LoadAssetAtPath<ASHWeaponData>(path);
            if (d) { if (d.item != item) { d.item = item; EditorUtility.SetDirty(d); } return; }
            d = ScriptableObject.CreateInstance<ASHWeaponData>(); d.item = item; d.kind = kind; init(d);
            AssetDatabase.CreateAsset(d, path); sb.Append($"{name} created; ");
        }

        static void CopyAxeAnimIds(ASHWeaponData d)
        {
            var tpl = AssetDatabase.LoadAssetAtPath<GameObject>(TplAxe); var w = tpl ? tpl.GetComponentInChildren<vMeleeWeapon>(true) : null;
            if (w) { d.attackID = w.attackID; d.movesetID = w.movesetID; }
        }

        // ---------------------------------------------------------------- dummy

        [MenuItem("ASH/Systems/Combat/2 Place Training Dummy at Cabin Campfire", priority = 11)]
        public static void PlaceDummy()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Dummy: stop Play mode first."); return; }
            var prefab = EnsureDummyPrefab();
            if (!prefab) { Debug.LogWarning("[ASH] Dummy: DummyTarget model missing under " + DummyDir); return; }
            var fire = ASHTestMeatPickups.FindCabinFire(out string how);
            if (!fire) { Debug.LogWarning("[ASH] Dummy: no campfire found."); return; }
            var scene = fire.gameObject.scene;
            foreach (var old in Object.FindObjectsByType<ASHTrainingDummy>(FindObjectsInactive.Include))
                if (old && old.gameObject.scene == scene && old.name == "ASH_TrainingDummy") Undo.DestroyObjectImmediate(old.gameObject);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(go, "ASH dummy");
            go.name = "ASH_TrainingDummy";
            var door = GameObject.Find("ASH_CabinDoor");
            var away = fire.transform.position - (door ? door.transform.position : fire.transform.position - Vector3.forward); away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward; away.Normalize();
            var p = fire.transform.position + away * 6f;
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-away, Vector3.up));
            ASHTestMeatPickups.Snap(go, fire.transform);
            EditorSceneManager.MarkSceneDirty(scene); Selection.activeGameObject = go;
            Debug.Log($"[ASH] Dummy: ASH_TrainingDummy placed 6 m past '{fire.name}' (away from the cabin door, facing it) at {go.transform.position}. Scene NOT saved.");
        }

        static GameObject EnsureDummyPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DummyOut); if (existing) return existing;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(DummyDir + "DummyTarget.prefab");
            if (!model) return null;
            // Genesis material is HDRP: make an ASH URP copy
            var mat = AssetDatabase.LoadAssetAtPath<Material>(DummyDir + "DummyTargetMaterial.mat");
            if (mat && !mat.shader.name.StartsWith("Universal"))
            {
                var baseTex = AssetDatabase.LoadAssetAtPath<Texture>(DummyDir + "DummyTarget_BaseColor.png");
                var nrm = AssetDatabase.LoadAssetAtPath<Texture>(DummyDir + "DummyTarget_Normal.png");
                mat.shader = Shader.Find("Universal Render Pipeline/Lit");
                if (baseTex) mat.SetTexture("_BaseMap", baseTex);
                if (nrm) { var ti = AssetImporter.GetAtPath(DummyDir + "DummyTarget_Normal.png") as TextureImporter; if (ti && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); } mat.SetTexture("_BumpMap", nrm); mat.EnableKeyword("_NORMALMAP"); }
                mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Smoothness", 0.25f); EditorUtility.SetDirty(mat);
            }
            var root = new GameObject("ASH_TrainingDummy");
            var vis = (GameObject)PrefabUtility.InstantiatePrefab(model); vis.name = "Visual"; vis.transform.SetParent(root.transform, false);
            foreach (var c in vis.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            root.tag = "Enemy";
            var box = root.AddComponent<BoxCollider>(); ASHTrainingDummy.FitBox(box);
            root.AddComponent<ASHTrainingDummy>();
            var saved = PrefabUtility.SaveAsPrefabAsset(root, DummyOut); Object.DestroyImmediate(root);
            return saved;
        }

        // ---------------------------------------------------------------- helpers

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = System.IO.Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(p)); } }
        static string N(Object o) => o ? o.name : "none";
        static ASHItemDefinition Item(string n) => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(ItemsDir + n + ".asset");

        static GameObject FindPrefab(params string[] names)
        {
            foreach (var n in names)
                foreach (var g in AssetDatabase.FindAssets(n + " t:Prefab"))
                { var p = AssetDatabase.GUIDToAssetPath(g); if (System.IO.Path.GetFileNameWithoutExtension(p) == n) return AssetDatabase.LoadAssetAtPath<GameObject>(p); }
            return null;
        }

        static Sprite FindSprite(params string[] names)
        {
            foreach (var n in names)
                foreach (var g in AssetDatabase.FindAssets(n + " t:Sprite"))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (!System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains(n.ToLowerInvariant())) continue;
                    var s = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().FirstOrDefault(); if (s) return s;
                }
            return null;
        }

        /// <summary>Copies a component's values from a vendor template, then drops every reference that pointed inside that
        /// template prefab (bones, children) and every persistent event listener. Asset references (projectile prefabs, IK lists,
        /// audio clips) are kept.</summary>
        static void CopyFrom(Component src, Component dst, string templatePath)
        {
            if (!src || !dst) return;
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(src), dst);
            var so = new SerializedObject(dst); var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.name == "m_Script") continue;
                var o = it.objectReferenceValue; if (!o) continue;
                if ((o is GameObject || o is Component) && !AssetDatabase.IsMainAsset(o) && AssetDatabase.GetAssetPath(o) != "") it.objectReferenceValue = null; // inside some prefab
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            ClearEvents(dst);
        }

        static void ClearEvents(Component c)
        {
            if (!c) return;
            var so = new SerializedObject(c); var it = so.GetIterator(); var paths = new List<string>();
            while (it.Next(true)) if (it.name == "m_Calls" && it.isArray && it.propertyPath.EndsWith("m_PersistentCalls.m_Calls")) paths.Add(it.propertyPath);
            foreach (var p in paths) so.FindProperty(p).arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Transform Marker(GameObject go, string name, Vector3 localPos, Vector3? localFwd = null)
        {
            var t = go.transform.Find(name);
            if (t) return t; // already there: keep any hand tuning
            t = new GameObject(name).transform; t.SetParent(go.transform, false);
            t.localPosition = localPos; t.localRotation = localFwd.HasValue ? Quaternion.LookRotation(localFwd.Value, Vector3.forward == localFwd.Value ? Vector3.up : Vector3.forward) : Quaternion.identity;
            return t;
        }

        static bool LocalBounds(GameObject go, out Bounds b)
        {
            b = new Bounds(Vector3.zero, Vector3.one * 0.1f); bool has = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.name.StartsWith("ASH_")) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var l = go.transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!has) { b = new Bounds(l, Vector3.zero); has = true; } else b.Encapsulate(l);
                }
            }
            return has;
        }

        static int Longest(Vector3 s) => s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;

        /// <summary>Which local X side the bow string is on (limb tips sit near the string, the riser far from it); same rule as ASHWeaponAim.</summary>
        static float BowStringSign(GameObject go, out Bounds b)
        {
            LocalBounds(go, out b);
            var pts = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.name.IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(go.transform.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            if (pts.Count < 20) return 1f;
            float zMin = pts.Min(p => p.z), zMax = pts.Max(p => p.z), half = (zMax - zMin) * 0.5f, zc = (zMax + zMin) * 0.5f;
            double tip = 0, mid = 0; int nt = 0, nm = 0;
            foreach (var p in pts) { float u = Mathf.Abs(p.z - zc) / Mathf.Max(1e-4f, half); if (u > 0.9f) { tip += p.x; nt++; } else if (u < 0.08f) { mid += p.x; nm++; } }
            if (nt == 0 || nm == 0) return 1f;
            return tip / nt >= mid / nm ? 1f : -1f;
        }
    }
}
