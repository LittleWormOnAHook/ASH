using System.Collections.Generic;
using System.IO;
using ASH.EditorTools.Build;
using ASH.Interaction;
using ASH.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools.Systems
{
    /// <summary>
    /// Idempotent setup for the t303-t307 batch: missing item icons, Deer Skin (stack 20, 2-5 yield pickups
    /// on the cabin porch), and the 4-tier Ammo Pouch crafting upgrades. Placement edits the open scene but
    /// never saves it — save ASH_Prototype yourself to keep the porch drops.
    /// </summary>
    public static class ASHDeerSkinPouchSetup
    {
        const string ItemDir = "Assets/_ASH/Data/Items";
        const string RecipeDir = "Assets/_ASH/Data/Recipes";
        const string IconDir = "Assets/_ASH/Art/Icons";
        const string PickupDir = "Assets/_ASH/Prefabs/Pickups";
        const string PickupPath = PickupDir + "/ASH_Pickup_DeerSkin.prefab";
        const string LeatherSrc = "Assets/HQ Survival Pack/Prefabs/Resources/Leather.prefab";
        const string GroupName = "ASH_DeerSkinPickups";
        const string TestRoot = "ASH_TestPickups";

        [MenuItem("ASH/Systems/Items/Deer Skin, Ammo Pouch and Missing Icons")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }
            var sb = new System.Text.StringBuilder("[ASH] Deer skin / pouch / icons: ");
            ImportIcons(sb);
            var deer = EnsureDeerSkin(sb);
            AssignIcons(sb, deer);
            var pouchIcon = LoadIcon("ASH_Icon_AmmoPouch");
            var recipes = EnsurePouchRecipes(sb, deer, pouchIcon);
            WireManual(sb, deer, recipes);
            ASHItemCatalog.AppendRecipesToPlayer(recipes);
            EnsurePickupPrefab(sb, deer);
            PlacePorchPickups(sb, deer);
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
        }

        [MenuItem("ASH/Systems/Testing/Place Deer Skin Pickups at Cabin")]
        public static void PlaceOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }
            var deer = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{ItemDir}/ASH_Item_DeerSkin.asset");
            if (!deer) { Debug.LogWarning("[ASH] Deer Skin item missing. Run ASH/Systems/Items/Deer Skin, Ammo Pouch and Missing Icons first."); return; }
            var sb = new System.Text.StringBuilder("[ASH] Deer skin pickups: ");
            EnsurePickupPrefab(sb, deer);
            PlacePorchPickups(sb, deer);
            Debug.Log(sb.ToString());
        }

        static Sprite LoadIcon(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{IconDir}/{name}.png");
        static ASHItemDefinition Item(string file) => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{ItemDir}/ASH_Item_{file}.asset");

        static void SpriteImport(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter; if (!imp) return;
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.maxTextureSize = 256;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
        }

        static void ImportIcons(System.Text.StringBuilder sb)
        {
            string[] names = { "ASH_Icon_WaterBottle", "ASH_Icon_WetRag", "ASH_Icon_DryRag", "ASH_Icon_FilterCartridge", "ASH_Icon_GasMask", "ASH_Icon_DeerSkin", "ASH_Icon_AmmoPouch" };
            int n = 0;
            foreach (var name in names)
            {
                string path = $"{IconDir}/{name}.png";
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), path))) { sb.Append($"{name} PNG missing; "); continue; }
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                SpriteImport(path);
                n++;
            }
            sb.Append($"{n} icons imported; ");
        }

        static void Assign(ASHItemDefinition it, string iconName, System.Text.StringBuilder sb)
        {
            if (!it) { sb.Append($"{iconName} item missing; "); return; }
            var sp = LoadIcon(iconName);
            if (!sp) { sb.Append($"{iconName} sprite missing; "); return; }
            if (it.icon != sp) { it.icon = sp; EditorUtility.SetDirty(it); }
            sb.Append($"{it.displayName} icon ok; ");
        }

        static void AssignIcons(System.Text.StringBuilder sb, ASHItemDefinition deer)
        {
            Assign(Item("WaterBottle"), "ASH_Icon_WaterBottle", sb);
            Assign(Item("WetRag"), "ASH_Icon_WetRag", sb);
            Assign(Item("DryRag"), "ASH_Icon_DryRag", sb);
            Assign(Item("FilterCartridge"), "ASH_Icon_FilterCartridge", sb);
            Assign(Item("GasMask"), "ASH_Icon_GasMask", sb);
            Assign(deer, "ASH_Icon_DeerSkin", sb);
        }

        static ASHItemDefinition EnsureDeerSkin(System.Text.StringBuilder sb)
        {
            ASHMenuInstaller.Folder(ItemDir);
            string path = $"{ItemDir}/ASH_Item_DeerSkin.asset";
            var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(path);
            if (!it)
            {
                it = ScriptableObject.CreateInstance<ASHItemDefinition>();
                AssetDatabase.CreateAsset(it, path);
            }
            it.itemId = "deer_skin";
            it.displayName = "Deer Skin";
            it.description = "A cured hide. Sewn into pouches, wraps and later clothing. Stacks to 20.";
            it.category = ASHItemCategory.Resource;
            it.maxStack = 20;
            it.uiTint = new Color(0.62f, 0.42f, 0.26f);
            it.useVerb = "Use";
            EditorUtility.SetDirty(it);
            sb.Append("Deer Skin item ok; ");
            return it;
        }

        static List<ASHRecipe> EnsurePouchRecipes(System.Text.StringBuilder sb, ASHItemDefinition deer, Sprite icon)
        {
            var rope = Item("Rope"); var cloth = Item("Cloth"); var scrap = Item("ScrapMetal");
            var list = new List<ASHRecipe>
            {
                UpsertPouch(1, 1, 4f, icon, "Sew a belt pouch from deer skin and rope. Ammo stacks +25%.", (deer, 2), (rope, 1)),
                UpsertPouch(2, 3, 6f, icon, "A deeper pouch. Ammo stacks +50% in total. Needs Crafting level 3.", (deer, 4), (rope, 2), (cloth, 2)),
                UpsertPouch(3, 5, 8f, icon, "Reinforced pouch. Ammo stacks +75% in total. Needs Crafting level 5.", (deer, 6), (rope, 3), (cloth, 4)),
                UpsertPouch(4, 8, 12f, icon, "A full ammo harness. Ammo stacks +100% in total. Needs Crafting level 8.", (deer, 10), (rope, 4), (cloth, 6), (scrap, 2))
            };
            sb.Append($"ammo pouch recipes {list.Count}; ");
            return list;
        }

        static ASHRecipe UpsertPouch(int tier, int level, float secs, Sprite icon, string desc, params (ASHItemDefinition item, int n)[] ings)
        {
            ASHMenuInstaller.Folder(RecipeDir);
            string name = $"AmmoPouch{tier}";
            string path = $"{RecipeDir}/ASH_Recipe_{name}.asset";
            var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(path);
            if (!r) { r = ScriptableObject.CreateInstance<ASHRecipe>(); AssetDatabase.CreateAsset(r, path); }
            r.recipeId = $"ammo_pouch_{tier}";
            r.displayName = tier == 1 ? "Ammo Pouch" : "Ammo Pouch " + new[] { "", "I", "II", "III", "IV" }[tier];
            r.description = desc;
            r.category = ASHRecipeCategory.Tools;
            r.outputItem = null;
            r.outputAmount = 1;
            r.icon = icon;
            r.requiredCraftingLevel = level;
            r.upgradeId = ASHUpgrades.AmmoPouch;
            r.upgradeTier = tier;
            r.craftSeconds = secs;
            r.station = ASHCraftStation.Anywhere;
            r.requiredTool = ASHToolType.None;
            r.waterPercent = 0f;
            r.ingredients = new List<ASHRecipeIngredient>();
            foreach (var (item, n) in ings) if (item) r.ingredients.Add(new ASHRecipeIngredient { item = item, amount = n });
            EditorUtility.SetDirty(r);
            return r;
        }

        static void WireManual(System.Text.StringBuilder sb, ASHItemDefinition deer, List<ASHRecipe> recipes)
        {
            var d = AssetDatabase.LoadAssetAtPath<ASHFieldManualData>("Assets/_ASH/Resources/ASH_FieldManual.asset");
            if (!d) { sb.Append("field manual asset missing; "); return; }
            var arrows = AssetDatabase.LoadAssetAtPath<ASHRecipe>($"{RecipeDir}/ASH_Recipe_WoodenArrows.asset");
            ASHRecipe prev = arrows;
            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i]; if (!r) continue;
                var e = d.Find(r);
                if (e == null) { e = new ASHFieldManualEntry(); d.entries.Add(e); }
                e.recipe = r;
                e.basic = false;
                e.requireAll = true;
                e.column = 3;
                e.row = 8 + i;
                e.hint = i == 0 ? "Find a deer skin." : $"Craft Ammo Pouch{(i == 1 ? "" : " " + new[] { "", "", "II", "III" }[i])} first.";
                e.parents = new List<ASHRecipe>();
                if (prev) e.parents.Add(prev);
                e.conditions = new List<ASHUnlockCondition>();
                if (i == 0) e.conditions.Add(new ASHUnlockCondition { kind = ASHUnlockKind.FirstPickup, item = deer });
                else e.conditions.Add(new ASHUnlockCondition { kind = ASHUnlockKind.UpgradeTier, upgradeId = ASHUpgrades.AmmoPouch, upgradeTier = i });
                prev = r;
            }
            EditorUtility.SetDirty(d);
            sb.Append("field manual entries ok; ");
        }

        static void EnsurePickupPrefab(System.Text.StringBuilder sb, ASHItemDefinition deer)
        {
            ASHMenuInstaller.Folder(PickupDir);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath);
            if (!existing)
            {
                var root = new GameObject("ASH_Pickup_DeerSkin");
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(LeatherSrc);
                if (src)
                {
                    var vis = (GameObject)PrefabUtility.InstantiatePrefab(src);
                    vis.name = "Visual";
                    vis.transform.SetParent(root.transform, false);
                    vis.transform.localPosition = Vector3.zero;
                    vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    // hide-sized: cap the visual at about half a metre
                    var rs = vis.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0)
                    {
                        var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                        if (longest > 0.55f) vis.transform.localScale *= 0.55f / longest;
                    }
                    foreach (var c in vis.GetComponentsInChildren<Collider>()) c.enabled = false;
                }
                else
                {
                    var vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    vis.name = "Visual"; Object.DestroyImmediate(vis.GetComponent<Collider>());
                    vis.transform.SetParent(root.transform, false);
                    vis.transform.localScale = new Vector3(0.42f, 0.03f, 0.28f);
                    var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    var mat = new Material(sh);
                    var col = new Color(0.55f, 0.36f, 0.22f);
                    mat.color = col; if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                    vis.GetComponent<Renderer>().sharedMaterial = mat;
                }
                var box = root.AddComponent<BoxCollider>(); box.isTrigger = true;
                var rs2 = root.GetComponentsInChildren<Renderer>();
                if (rs2.Length > 0)
                {
                    var b = rs2[0].bounds; for (int i = 1; i < rs2.Length; i++) b.Encapsulate(rs2[i].bounds);
                    box.center = root.transform.InverseTransformPoint(b.center);
                    box.size = Vector3.Max(b.size, new Vector3(0.35f, 0.2f, 0.35f)) + new Vector3(0.1f, 0.15f, 0.1f);
                }
                else { box.center = Vector3.up * 0.1f; box.size = new Vector3(0.5f, 0.3f, 0.4f); }
                int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                root.AddComponent<ASHItemPickup>();
                existing = PrefabUtility.SaveAsPrefabAsset(root, PickupPath);
                Object.DestroyImmediate(root);
            }
            var pk = existing.GetComponent<ASHItemPickup>();
            if (pk)
            {
                var so = new SerializedObject(pk);
                so.FindProperty("item").objectReferenceValue = deer;
                var mn = so.FindProperty("amountMin"); if (mn != null) mn.intValue = 2;
                var mx = so.FindProperty("amountMax"); if (mx != null) mx.intValue = 5;
                var am = so.FindProperty("amount"); if (am != null) am.intValue = 2;
                var sk = so.FindProperty("harvestSkill01"); if (sk != null) sk.floatValue = 0f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            if (deer.worldPickupPrefab != existing) { deer.worldPickupPrefab = existing; EditorUtility.SetDirty(deer); }
            sb.Append("pickup prefab ok; ");
        }

        static void PlacePorchPickups(System.Text.StringBuilder sb, ASHItemDefinition deer)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath);
            if (!prefab) { sb.Append("pickup prefab missing, skipped place; "); return; }
            var door = GameObject.Find("ASH_CabinDoor");
            var fire = ASH.EditorTools.ASHTestMeatPickups.FindCabinFire(out _);
            Vector3 origin;
            Vector3 along;
            if (door)
            {
                origin = door.transform.position;
                Vector3 toFire = fire ? (fire.transform.position - origin) : door.transform.forward;
                toFire.y = 0f;
                if (toFire.sqrMagnitude < 0.01f) toFire = door.transform.forward; toFire.y = 0f;
                if (toFire.sqrMagnitude < 0.01f) toFire = Vector3.forward;
                along = toFire.normalized;
                origin += along * 1.15f;
            }
            else if (fire)
            {
                origin = fire.transform.position + Vector3.forward * 1.6f;
                along = Vector3.forward;
            }
            else { sb.Append("no cabin door or fire in the open scene (placement skipped); "); return; }

            var scene = door ? door.scene : fire.gameObject.scene;
            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects()) if (go.name == TestRoot) { root = go; break; }
            if (!root) { root = new GameObject(TestRoot); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene); Undo.RegisterCreatedObjectUndo(root, "ASH deer skin pickups"); }
            Transform group = root.transform.Find(GroupName);
            if (!group)
            {
                var g = new GameObject(GroupName);
                g.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(g, "ASH deer skin pickups");
                group = g.transform;
            }
            for (int i = group.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(group.GetChild(i).gameObject);

            Vector3 side = Vector3.Cross(Vector3.up, along).normalized;
            Vector3[] spots = { origin + side * 0.4f, origin - side * 0.4f };
            for (int i = 0; i < spots.Length; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(go, "ASH deer skin pickups");
                go.name = $"ASH_Pickup_DeerSkin_{i + 1}";
                go.transform.SetParent(group, true);
                go.transform.SetPositionAndRotation(spots[i], Quaternion.Euler(0f, 30f + i * 80f, 0f));
                if (fire) ASH.EditorTools.ASHTestMeatPickups.Snap(go, fire.transform);
                else SnapGround(go);
                var pk = go.GetComponentInChildren<ASHItemPickup>();
                if (pk)
                {
                    var so = new SerializedObject(pk);
                    so.FindProperty("item").objectReferenceValue = deer;
                    var mn = so.FindProperty("amountMin"); if (mn != null) mn.intValue = 2;
                    var mx = so.FindProperty("amountMax"); if (mx != null) mx.intValue = 5;
                    var am = so.FindProperty("amount"); if (am != null) am.intValue = 2;
                    var sk = so.FindProperty("harvestSkill01"); if (sk != null) sk.floatValue = 0f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            sb.Append($"2 deer skin pickups on the porch (scene NOT saved); ");
        }

        static void SnapGround(GameObject go)
        {
            var p = go.transform.position;
            float ground = p.y; bool found = false;
            var hits = Physics.RaycastAll(p + Vector3.up * 5f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            float best = 0f, bestD = float.PositiveInfinity;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(go.transform)) continue;
                if (h.point.y < bestD) { bestD = h.point.y; best = h.point.y; found = true; }
            }
            if (found) ground = best;
            else { var t = Terrain.activeTerrain; if (t) ground = t.SampleHeight(p) + t.transform.position.y; }
            go.transform.position = new Vector3(p.x, ground, p.z);
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                go.transform.position += Vector3.up * (ground + 0.02f - b.min.y);
            }
        }
    }
}
