#if UNITY_EDITOR
using System.IO;
using System.Text;
using ASH.EditorTools.Systems;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Wildlife;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: Game Trap item, recipe (rope 2 + deadwood 6, page 4), placed prefab. Does not save the scene.
    /// Leaves the five Survival Guide starter recipes unchanged.</summary>
    public static class ASHTrapSetup
    {
        const string ItemPath = "Assets/_ASH/Data/Items/ASH_Item_LargeTrap.asset";
        const string RecipePath = "Assets/_ASH/Data/Recipes/ASH_Recipe_LargeTrap.asset";
        const string SettingsPath = "Assets/_ASH/Resources/ASH_SnareSettings.asset";
        const string PrefabPath = "Assets/_ASH/Prefabs/World/ASH_Placed_LargeTrap.prefab";
        const string LetterPath = "Assets/_ASH/Art/Icons Placeholder/ASH_Letter_LT.png";

        [MenuItem("ASH/Systems/Traps/Setup Game Trap (idempotent)", false, 224)]
        public static void Setup()
        {
            var log = new StringBuilder();
            var settings = ASHMenuInstaller.Ensure<ASHSnareSettings>(SettingsPath);
            var letter = ASHWildlifeSetup.EnsureLetter(LetterPath, "LT");
            var item = EnsureItem(letter, settings);
            var rope = FindItem("rope");
            var deadwood = FindItem("deadwood");
            if (rope && deadwood) EnsureRecipe(item, letter, rope, deadwood);
            var prefab = BuildPrefab(settings, item, log);
            if (prefab)
            {
                item.placeablePrefab = prefab;
                EditorUtility.SetDirty(item);
            }
            TryAddManual();
            ASHMenuInstaller.RefreshRegistry();
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            Debug.Log("[ASH] Game Trap set up. " + log);
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

        static ASHItemDefinition EnsureItem(Sprite icon, ASHSnareSettings s)
        {
            var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(ItemPath);
            if (!it)
            {
                ASHMenuInstaller.Folder(Path.GetDirectoryName(ItemPath).Replace('\\', '/'));
                it = ScriptableObject.CreateInstance<ASHItemDefinition>();
                AssetDatabase.CreateAsset(it, ItemPath);
            }
            it.itemId = "largetrap";
            it.displayName = "Game Trap";
            it.description = "A heavier deadwood frame and a stronger loop. Place it on a game trail to catch a fawn or a wolf. A trapped wolf can still bite, and may break free. Harvest the catch with a knife.";
            it.category = ASHItemCategory.Placeable;
            it.useVerb = "Place";
            it.maxStack = 3;
            it.sellValue = ASH.Gameplay.ASHEconomy.LegacyToCopper(8);
            if (icon && !it.icon) it.icon = icon;
            it.placementFootprint = new Vector3(s.largeFootprintX, s.largeFootprintY, s.largeFootprintZ);
            it.maxPlacementSlope = s.maxSlope;
            EditorUtility.SetDirty(it);
            return it;
        }

        static void EnsureRecipe(ASHItemDefinition output, Sprite icon, ASHItemDefinition rope, ASHItemDefinition deadwood)
        {
            var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(RecipePath);
            if (!r)
            {
                ASHMenuInstaller.Folder(Path.GetDirectoryName(RecipePath).Replace('\\', '/'));
                r = ScriptableObject.CreateInstance<ASHRecipe>();
                AssetDatabase.CreateAsset(r, RecipePath);
            }
            r.recipeId = "largetrap";
            r.displayName = "Game Trap";
            r.description = "Heavier rope and a sturdier frame than a rabbit snare. For fawns and wolves.";
            r.category = ASHRecipeCategory.Tools;
            r.outputItem = output;
            r.outputAmount = 1;
            if (icon && !r.icon) r.icon = icon;
            r.craftSeconds = 3f;
            r.requiredCraftingLevel = 1;
            r.station = ASHCraftStation.Anywhere;
            r.ingredients = new System.Collections.Generic.List<ASHRecipeIngredient>
            {
                new ASHRecipeIngredient { item = rope, amount = 2 },
                new ASHRecipeIngredient { item = deadwood, amount = 6 }
            };
            EditorUtility.SetDirty(r);
        }

        static void TryAddManual()
        {
            var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(RecipePath);
            var d = AssetDatabase.LoadAssetAtPath<ASHFieldManualData>("Assets/_ASH/Resources/ASH_FieldManual.asset");
            if (!r || !d) return;
            var e = d.Find(r);
            if (e == null)
            {
                e = new ASHFieldManualEntry { recipe = r, basic = false, column = 3, row = 8, requireAll = true, hint = "A heavier trap, shown with the snare in the hunting shack (page 4)." };
                var snare = FindRecipe("rabbitsnare");
                if (snare) e.parents.Add(snare);
                d.entries.Add(e);
            }
            e.basic = false;
            if (e.conditions == null) e.conditions = new System.Collections.Generic.List<ASHUnlockCondition>();
            bool hasPage = false;
            foreach (var c in e.conditions) if (c != null && c.kind == ASHUnlockKind.ManualPage && c.page == 4) hasPage = true;
            if (!hasPage) e.conditions.Add(new ASHUnlockCondition { kind = ASHUnlockKind.ManualPage, page = 4 });
            EditorUtility.SetDirty(d);
        }

        static ASHRecipe FindRecipe(string id)
        {
            foreach (var g in AssetDatabase.FindAssets("t:ASHRecipe"))
            {
                var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(AssetDatabase.GUIDToAssetPath(g));
                if (r && r.recipeId == id) return r;
            }
            return null;
        }

        static GameObject BuildPrefab(ASHSnareSettings settings, ASHItemDefinition item, StringBuilder log)
        {
            ASHMenuInstaller.Folder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            var root = new GameObject("ASH_Placed_LargeTrap");
            try
            {
                Stick(root.transform, "PostA", new Vector3(-0.28f, 0.4f, -0.1f), Quaternion.Euler(0f, 0f, 8f), new Vector3(0.06f, 0.8f, 0.06f), new Color(0.4f, 0.26f, 0.14f));
                Stick(root.transform, "PostB", new Vector3(0.3f, 0.38f, 0.12f), Quaternion.Euler(8f, -20f, -10f), new Vector3(0.06f, 0.76f, 0.06f), new Color(0.38f, 0.24f, 0.12f));
                Stick(root.transform, "Beam", new Vector3(0f, 0.7f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.05f, 0.55f, 0.05f), new Color(0.42f, 0.28f, 0.15f));
                var loop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                loop.name = "Loop";
                loop.transform.SetParent(root.transform, false);
                loop.transform.localPosition = new Vector3(0f, 0.18f, 0.15f);
                loop.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
                loop.transform.localScale = new Vector3(0.42f, 0.02f, 0.42f);
                var col = loop.GetComponent<Collider>(); if (col) Object.DestroyImmediate(col);
                var lr = loop.GetComponent<Renderer>();
                if (lr) lr.sharedMaterial = new Material(lr.sharedMaterial) { color = new Color(0.32f, 0.26f, 0.18f) };

                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.3f, 0f);
                box.size = new Vector3(1.1f, 0.7f, 1.1f);
                box.isTrigger = true;
                var snare = root.AddComponent<ASHSnare>();
                snare.trapSize = ASHTrapSize.Large;
                snare.itemId = "largetrap";

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                log.Append("Wrote " + PrefabPath);
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void Stick(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>(); if (col) Object.DestroyImmediate(col);
            var r = go.GetComponent<Renderer>();
            if (r) r.sharedMaterial = new Material(r.sharedMaterial) { color = color };
        }
    }
}
#endif
