#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using ASH.Combat;
using ASH.EditorTools.Systems;
using ASH.Inventory;
using Invector;
using Invector.vMelee;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: torch weapon data, settings, vMeleeWeapon + hit box on Held_Torch.
    /// Never moves hand placements. Edits ASH_Player.prefab and ASH_Held_Torch.prefab only; does not save the open scene.</summary>
    public static class ASHTorchStrikeSetup
    {
        const string PlayerPath = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        const string HeldPath = "Assets/_ASH/Prefabs/Held/ASH_Held_Torch.prefab";
        const string SettingsPath = "Assets/_ASH/Resources/ASH_TorchSettings.asset";
        const string WeaponsDir = "Assets/_ASH/Resources/ASH_Weapons/";
        const string WeaponPath = WeaponsDir + "ASH_Weapon_Torch.asset";

        [MenuItem("ASH/Systems/Combat/Setup Torch Strike (idempotent)", false, 14)]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Torch strike: stop Play mode first."); return; }
            var log = new StringBuilder();
            var torch = FindItem("torch");
            if (!torch) { Debug.LogError("[ASH] Torch item missing."); return; }

            var settings = ASHMenuInstaller.Ensure<ASHTorchSettings>(SettingsPath);
            EnsureWeaponData(torch, settings, log);

            WirePrefab(HeldPath, log);
            if (System.IO.File.Exists(PlayerPath.Replace('/', System.IO.Path.DirectorySeparatorChar)) || AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath))
                WirePlayer(log);

            if (string.IsNullOrEmpty(torch.description) || !torch.description.Contains("strike"))
            {
                torch.description = "Cloth soaked in alcohol, wrapped on a stick. Burns about five minutes. Toggle with [L]. With nothing in the main hand, [LMB] is a short strike; a lit torch burns and makes wolves back off.";
                EditorUtility.SetDirty(torch);
            }
            ASHWeaponData.ClearCache();
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            Debug.Log("[ASH] Torch strike set up. " + log);
        }

        [MenuItem("ASH/Systems/Combat/Select Torch Settings", false, 15)]
        static void Select()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHTorchSettings>(SettingsPath);
            if (!s) Setup();
            else Selection.activeObject = s;
        }

        static void EnsureWeaponData(ASHItemDefinition torch, ASHTorchSettings settings, StringBuilder log)
        {
            ASHMenuInstaller.Folder(WeaponsDir.TrimEnd('/'));
            var d = AssetDatabase.LoadAssetAtPath<ASHWeaponData>(WeaponPath);
            bool fresh = !d;
            if (!d)
            {
                d = ScriptableObject.CreateInstance<ASHWeaponData>();
                AssetDatabase.CreateAsset(d, WeaponPath);
            }
            if (fresh || !d.item)
            {
                d.item = torch;
                d.kind = ASHWeaponKind.Melee;
                d.damage = settings ? settings.fallbackDamage : 6f;
                d.damageRandom = 2f;
                d.critChance = 0.08f;
                d.critMultiplier = 1.5f;
                d.poiseFromDamage = 0.3f;
                d.bodyWeight = 0.6f;
                d.knockback = 0.8f;
                d.reach = settings ? settings.fallbackReach : 1.6f;
                d.staminaCost = settings ? settings.fallbackStamina : 8f;
                d.staminaRecoveryDelay = 0.8f;
                d.attackID = 1;
                d.movesetID = 2;
                d.hitBoxSize = new Vector3(0.12f, 0.12f, 0.18f);
                d.hitBoxAlong = 0.9f;
                EditorUtility.SetDirty(d);
                log.Append(fresh ? "weapon data created; " : "weapon data item wired; ");
            }
            else log.Append("weapon data kept; ");
        }

        static void WirePlayer(StringBuilder log)
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var held = FindChild(root.transform, "Held_Torch");
                if (!held) { log.Append("ASH_Player has no Held_Torch (placements left alone); "); return; }
                var pos = held.transform.localPosition;
                var rot = held.transform.localRotation;
                var sca = held.transform.localScale;
                WireGo(held, log, "ASH_Player/Held_Torch");
                // never keep a placement change
                held.transform.localPosition = pos;
                held.transform.localRotation = rot;
                held.transform.localScale = sca;
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void WirePrefab(string path, StringBuilder log)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!go) { log.Append(path + " missing; "); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var pos = root.transform.localPosition;
                var rot = root.transform.localRotation;
                var sca = root.transform.localScale;
                WireGo(root, log, "ASH_Held_Torch");
                root.transform.localPosition = pos;
                root.transform.localRotation = rot;
                root.transform.localScale = sca;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void WireGo(GameObject go, StringBuilder log, string label)
        {
            // Adds components only. Does not move this transform.
            var strike = go.GetComponent<ASHTorchStrike>();
            if (!strike) strike = go.AddComponent<ASHTorchStrike>();
            var w = go.GetComponent<vMeleeWeapon>();
            bool fresh = !w;
            if (!w) w = go.AddComponent<vMeleeWeapon>();
            w.meleeType = vMeleeType.OnlyAttack;
            w.useStrongAttack = false;
            w.meleeManager = null;
            w.overrideDamageSender = null;
            var data = AssetDatabase.LoadAssetAtPath<ASHWeaponData>(WeaponPath);
            var hb = go.transform.Find("ASH_HitBox");
            if (!hb)
            {
                hb = new GameObject("ASH_HitBox").transform;
                hb.SetParent(go.transform, false);
                hb.gameObject.layer = go.layer;
                var box = hb.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                var size = data ? data.hitBoxSize : new Vector3(0.12f, 0.12f, 0.18f);
                var ls = go.transform.lossyScale;
                box.size = new Vector3(
                    size.x / Mathf.Max(0.01f, Mathf.Abs(ls.x)),
                    size.y / Mathf.Max(0.01f, Mathf.Abs(ls.y)),
                    size.z / Mathf.Max(0.01f, Mathf.Abs(ls.z)));
                hb.localPosition = new Vector3(0f, 0f, data ? data.hitBoxAlong * 0.35f : 0.3f);
            }
            var hit = hb.GetComponent<vHitBox>();
            if (!hit) hit = hb.gameObject.AddComponent<vHitBox>();
            hit.attackObject = w;
            hit.damagePercentage = 100;
            hit.triggerType = vHitBoxType.Damage | vHitBoxType.Recoil;
            var trig = hb.GetComponent<Collider>();
            if (trig) { trig.isTrigger = true; trig.enabled = false; }
            w.hitBoxes = new List<vHitBox> { hit };
            if (w.damage == null) w.damage = new vDamage(data ? Mathf.RoundToInt(data.damage) : 6);
            log.Append(label + (fresh ? " melee added; " : " melee kept; "));
        }

        static GameObject FindChild(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all) if (t && t.name == name) return t.gameObject;
            return null;
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
    }
}
#endif
