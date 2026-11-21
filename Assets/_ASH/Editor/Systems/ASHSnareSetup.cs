#if UNITY_EDITOR
using System.IO;
using System.Text;
using ASH.EditorTools.Systems;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: snare settings, item, recipe (rope 1 + deadwood 3, page 4), placed prefab from primitives. Does not save the open scene.</summary>
    public static class ASHSnareSetup
    {
        const string ItemPath = "Assets/_ASH/Data/Items/ASH_Item_RabbitSnare.asset";
        const string RecipePath = "Assets/_ASH/Data/Recipes/ASH_Recipe_RabbitSnare.asset";
        const string SettingsPath = "Assets/_ASH/Resources/ASH_SnareSettings.asset";
        const string PrefabPath = "Assets/_ASH/Prefabs/World/ASH_Placed_RabbitSnare.prefab";
        const string LetterPath = "Assets/_ASH/Art/Icons Placeholder/ASH_Letter_RS.png";

        [MenuItem("ASH/Systems/Traps/Setup Rabbit Snare (idempotent)", false, 222)]
        public static void Setup()
        {
            var log = new StringBuilder();
            var settings = ASHMenuInstaller.Ensure<ASHSnareSettings>(SettingsPath);
            var letter = EnsureLetter(LetterPath);
            var item = EnsureItem(letter, settings);
            var rope = FindItem("rope");
            var deadwood = FindItem("deadwood");
            if (rope && deadwood)
                EnsureRecipe(item, letter, rope, deadwood);
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
            Debug.Log("[ASH] Rabbit snare set up. " + log);
        }

        [MenuItem("ASH/Systems/Traps/Select Snare Settings", false, 223)]
        static void Select()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHSnareSettings>(SettingsPath);
            if (!s) Setup();
            else Selection.activeObject = s;
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
            it.itemId = "rabbitsnare";
            it.displayName = "Rabbit Snare";
            it.description = "A simple loop of rope on a deadwood frame. Place it in the woods and wait; it can catch a rabbit over a few game hours. Pack it back up with hold-E.";
            it.category = ASHItemCategory.Placeable;
            it.useVerb = "Place";
            it.maxStack = 5;
            it.sellValue = 4;
            if (icon && !it.icon) it.icon = icon;
            it.placementFootprint = new Vector3(s.footprintX, s.footprintY, s.footprintZ);
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
            r.recipeId = "rabbitsnare";
            r.displayName = "Rabbit Snare";
            r.description = "Rope and a few sticks. Set it on a game trail and wait.";
            r.category = ASHRecipeCategory.Tools;
            r.outputItem = output;
            r.outputAmount = 1;
            if (icon && !r.icon) r.icon = icon;
            r.craftSeconds = 2f;
            r.requiredCraftingLevel = 1;
            r.station = ASHCraftStation.Anywhere;
            r.ingredients = new System.Collections.Generic.List<ASHRecipeIngredient>
            {
                new ASHRecipeIngredient { item = rope, amount = 1 },
                new ASHRecipeIngredient { item = deadwood, amount = 3 }
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
                e = new ASHFieldManualEntry { recipe = r, basic = false, column = 3, row = 6, requireAll = true, hint = "A note in the hunting shack (page 4) shows how to set a snare." };
                var rope = FindRecipe("rope");
                if (rope) e.parents.Add(rope);
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
            var root = new GameObject("ASH_Placed_RabbitSnare");
            try
            {
                // two leaning sticks + a loop
                Stick(root.transform, "StickA", new Vector3(-0.08f, 0.28f, 0f), Quaternion.Euler(0f, 0f, 18f), new Vector3(0.04f, 0.56f, 0.04f), new Color(0.45f, 0.3f, 0.16f));
                Stick(root.transform, "StickB", new Vector3(0.1f, 0.22f, 0.04f), Quaternion.Euler(12f, 40f, -25f), new Vector3(0.035f, 0.48f, 0.035f), new Color(0.4f, 0.26f, 0.14f));
                var loop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                loop.name = "Loop";
                loop.transform.SetParent(root.transform, false);
                loop.transform.localPosition = new Vector3(0.02f, 0.12f, 0.12f);
                loop.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
                loop.transform.localScale = new Vector3(0.22f, 0.015f, 0.22f);
                var col = loop.GetComponent<Collider>(); if (col) Object.DestroyImmediate(col);
                var lr = loop.GetComponent<Renderer>();
                if (lr) lr.sharedMaterial = new Material(lr.sharedMaterial) { color = new Color(0.35f, 0.28f, 0.2f) };

                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.2f, 0f);
                box.size = new Vector3(0.7f, 0.45f, 0.7f);
                box.isTrigger = true;
                var snare = root.AddComponent<ASHSnare>();

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

        static Sprite EnsureLetter(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite) return sprite;
            ASHMenuInstaller.Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
            const int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;
            tex.SetPixels(px);
            var gold = new Color(0.83f, 0.69f, 0.32f, 1f);
            // R
            Fill(tex, 40, 48, 16, 160, gold);
            Fill(tex, 40, 192, 70, 16, gold);
            Fill(tex, 40, 120, 58, 16, gold);
            Fill(tex, 94, 136, 16, 56, gold);
            Fill(tex, 70, 48, 16, 72, gold);
            // S
            Fill(tex, 140, 192, 76, 16, gold);
            Fill(tex, 140, 120, 76, 16, gold);
            Fill(tex, 140, 48, 76, 16, gold);
            Fill(tex, 140, 136, 16, 56, gold);
            Fill(tex, 200, 64, 16, 56, gold);
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
