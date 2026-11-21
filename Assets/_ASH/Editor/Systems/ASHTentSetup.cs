#if UNITY_EDITOR
using System.IO;
using System.Text;
using ASH.EditorTools.Systems;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Survival;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Idempotent: tent settings, Built Tent item + recipes, wires placeable prefabs from Assets/_ASH/Models/day1-tent
    /// (that folder is untracked — do not commit it). Does not save the open scene.
    /// </summary>
    public static class ASHTentSetup
    {
        const string PackedSrc = "Assets/_ASH/Models/day1-tent/Packed Tent.prefab";
        const string BuiltSrc = "Assets/_ASH/Models/day1-tent/Built Tent Shelter.prefab";
        const string PackedDst = "Assets/_ASH/Prefabs/World/ASH_Placed_PackedTent.prefab";
        const string BuiltDst = "Assets/_ASH/Prefabs/World/ASH_Placed_BuiltTent.prefab";
        const string PackedItem = "Assets/_ASH/Data/Items/ASH_Item_PackedTent.asset";
        const string BuiltItem = "Assets/_ASH/Data/Items/ASH_Item_BuiltTent.asset";
        const string RecipeA = "Assets/_ASH/Data/Recipes/ASH_Recipe_BuiltTent.asset";
        const string RecipeB = "Assets/_ASH/Data/Recipes/ASH_Recipe_BuiltTentFirewood.asset";
        const string SettingsPath = "Assets/_ASH/Resources/ASH_TentSettings.asset";
        const string LetterPath = "Assets/_ASH/Art/Icons Placeholder/ASH_Letter_BT.png";

        [MenuItem("ASH/Systems/Tents/Setup Tents (idempotent)", false, 220)]
        public static void Setup()
        {
            var log = new StringBuilder();
            var settings = ASHMenuInstaller.Ensure<ASHTentSettings>(SettingsPath);

            var packed = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(PackedItem);
            if (!packed) { Debug.LogError("[ASH] Missing " + PackedItem); return; }
            packed.category = ASHItemCategory.Placeable;
            packed.useVerb = "Place";
            packed.maxStack = 1;
            if (string.IsNullOrEmpty(packed.description) || packed.description.Contains("comes later"))
                packed.description = "Grandpa's old canvas tent, rolled tight with its poles. Place it for leaky shelter from the ash. Pack it back up with hold-E. Not safe in a quake.";
            EditorUtility.SetDirty(packed);

            var letter = EnsureLetter(LetterPath);
            var built = EnsureItem(BuiltItem, "builttent", "Built Tent", "A stitched canvas shelter. Place it for leaky cover. Cloth, rope and wood. Pack it back up with hold-E. Not safe in a quake.", letter);
            built.category = ASHItemCategory.Placeable;
            built.useVerb = "Place";
            built.maxStack = 1;
            built.sellValue = 20;
            built.placementFootprint = new Vector3(2.6f, 1.4f, 2.6f);
            built.maxPlacementSlope = 22f;
            EditorUtility.SetDirty(built);

            var cloth = FindItem("cloth");
            var rope = FindItem("rope");
            var deadwood = FindItem("deadwood");
            var firewood = FindItem("firewood");
            if (cloth && rope && deadwood)
                EnsureRecipe(RecipeA, "builttent", "Built Tent", "Cloth, rope and deadwood stitched into a shelter you can pitch and pack up.", built, letter, cloth, 4, rope, 2, deadwood, 8);
            if (cloth && rope && firewood)
                EnsureRecipe(RecipeB, "builttent_firewood", "Built Tent (firewood)", "The same built tent, using split firewood instead of deadwood.", built, letter, cloth, 4, rope, 2, firewood, 4);

            bool haveModels = File.Exists(PackedSrc) && File.Exists(BuiltSrc);
            if (!haveModels)
            {
                log.AppendLine("day1-tent models are missing (Assets/_ASH/Models/day1-tent is untracked). Placeable prefabs were not wired. Drop the models in and re-run this menu.");
            }
            else
            {
                var packedPf = BuildPlacedPrefab(PackedSrc, PackedDst, ASHTentSettings.Kind.Packed, packed, settings, log);
                var builtPf = BuildPlacedPrefab(BuiltSrc, BuiltDst, ASHTentSettings.Kind.Built, built, settings, log);
                if (packedPf)
                {
                    packed.placeablePrefab = packedPf;
                    packed.placementFootprint = FootprintOf(packedPf);
                    EditorUtility.SetDirty(packed);
                }
                if (builtPf)
                {
                    built.placeablePrefab = builtPf;
                    built.placementFootprint = FootprintOf(builtPf);
                    EditorUtility.SetDirty(built);
                }
            }

            TryAddManual(RecipeA, 0, 13);
            TryAddManual(RecipeB, 1, 13);
            ASHMenuInstaller.RefreshRegistry();
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            Debug.Log("[ASH] Tents set up. " + (haveModels ? "Packed / Built placeable prefabs wired." : "Models missing.") + " " + log);
        }

        [MenuItem("ASH/Systems/Tents/Select Tent Settings", false, 221)]
        static void Select()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHTentSettings>(SettingsPath);
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

        static ASHItemDefinition EnsureItem(string path, string id, string name, string desc, Sprite icon)
        {
            var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(path);
            if (!it)
            {
                ASHMenuInstaller.Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
                it = ScriptableObject.CreateInstance<ASHItemDefinition>();
                AssetDatabase.CreateAsset(it, path);
            }
            it.itemId = id;
            it.displayName = name;
            it.description = desc;
            if (icon && !it.icon) it.icon = icon;
            EditorUtility.SetDirty(it);
            return it;
        }

        static void EnsureRecipe(string path, string id, string name, string desc, ASHItemDefinition output, Sprite icon,
            ASHItemDefinition a, int na, ASHItemDefinition b, int nb, ASHItemDefinition c, int nc)
        {
            var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(path);
            if (!r)
            {
                ASHMenuInstaller.Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
                r = ScriptableObject.CreateInstance<ASHRecipe>();
                AssetDatabase.CreateAsset(r, path);
            }
            r.recipeId = id;
            r.displayName = name;
            r.description = desc;
            r.category = ASHRecipeCategory.Materials;
            r.outputItem = output;
            r.outputAmount = 1;
            if (icon && !r.icon) r.icon = icon;
            r.craftSeconds = 3f;
            r.requiredCraftingLevel = 1;
            r.station = ASHCraftStation.Anywhere;
            r.ingredients = new System.Collections.Generic.List<ASHRecipeIngredient>
            {
                new ASHRecipeIngredient { item = a, amount = na },
                new ASHRecipeIngredient { item = b, amount = nb },
                new ASHRecipeIngredient { item = c, amount = nc }
            };
            EditorUtility.SetDirty(r);
        }

        static void TryAddManual(string recipePath, int col, int row)
        {
            var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(recipePath);
            var d = AssetDatabase.LoadAssetAtPath<ASHFieldManualData>("Assets/_ASH/Resources/ASH_FieldManual.asset");
            if (!r || !d || d.Find(r) != null) return;
            d.entries.Add(new ASHFieldManualEntry { recipe = r, basic = true, column = col, row = row, requireAll = true });
            EditorUtility.SetDirty(d);
        }

        static GameObject BuildPlacedPrefab(string srcPath, string dstPath, ASHTentSettings.Kind kind, ASHItemDefinition packItem, ASHTentSettings settings, StringBuilder log)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(srcPath);
            if (!src) { log.AppendLine("Missing " + srcPath); return null; }

            ASHMenuInstaller.Folder(Path.GetDirectoryName(dstPath).Replace('\\', '/'));

            var root = new GameObject(kind == ASHTentSettings.Kind.Built ? "ASH_Placed_BuiltTent" : "ASH_Placed_PackedTent");
            try
            {
                var vis = (GameObject)PrefabUtility.InstantiatePrefab(src);
                vis.name = src.name;
                vis.transform.SetParent(root.transform, false);
                vis.transform.localPosition = Vector3.zero;
                vis.transform.localRotation = Quaternion.identity;
                // keep the source's local scale (the packed tent FBX is authored huge / tiny)
                foreach (var t in vis.GetComponentsInChildren<Transform>(true))
                {
                    // flatten any baked world position on nested roots; keep scale
                    if (t == vis.transform) continue;
                }
                vis.isStatic = false;
                foreach (var tr in vis.GetComponentsInChildren<Transform>(true))
                {
                    var so = new SerializedObject(tr.gameObject);
                    var st = so.FindProperty("m_StaticEditorFlags");
                    if (st != null) { st.intValue = 0; so.ApplyModifiedPropertiesWithoutUndo(); }
                }

                var bounds = Encapsulate(root);
                if (bounds.size.sqrMagnitude < 0.01f) bounds = new Bounds(root.transform.position + Vector3.up, new Vector3(2f, 1.6f, 2f));

                // Move so the bounds sit on the ground at the root origin (place at the player's aim point).
                Vector3 delta = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                vis.transform.localPosition += delta;
                bounds = Encapsulate(root);

                var box = root.GetComponent<BoxCollider>();
                if (!box) box = root.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = root.transform.InverseTransformPoint(bounds.center);
                box.size = Vector3.Max(bounds.size, new Vector3(1.2f, 1.2f, 1.2f));

                var zone = root.GetComponent<ASHShelterZone>() ?? root.AddComponent<ASHShelterZone>();
                zone.volume = box;
                zone.structural = false;
                zone.sealing = settings.Sealing(kind);
                zone.label = kind == ASHTentSettings.Kind.Built ? settings.builtLabel : settings.packedLabel;

                var tent = root.GetComponent<ASHTent>() ?? root.AddComponent<ASHTent>();
                tent.kind = kind;
                tent.packItem = packItem;

                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(dstPath);
                GameObject prefab;
                if (existing) prefab = PrefabUtility.SaveAsPrefabAsset(root, dstPath);
                else prefab = PrefabUtility.SaveAsPrefabAsset(root, dstPath);
                log.AppendLine("Wrote " + dstPath + " bounds " + bounds.size);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static Bounds Encapsulate(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds();
            bool init = false;
            foreach (var r in rs)
            {
                if (!r || r is ParticleSystemRenderer) continue;
                if (!init) { b = r.bounds; init = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static Vector3 FootprintOf(GameObject prefab)
        {
            // Approximate from the prefab's box collider (local).
            var box = prefab.GetComponent<BoxCollider>();
            if (box)
                return new Vector3(Mathf.Max(1.2f, box.size.x), Mathf.Max(0.6f, box.size.y * 0.5f), Mathf.Max(1.2f, box.size.z));
            return new Vector3(2.4f, 1.2f, 2.4f);
        }

        static Sprite EnsureLetter(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite) return sprite;
            ASHMenuInstaller.Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
            const int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var clear = new Color(0, 0, 0, 0);
            var gold = new Color(0.83f, 0.69f, 0.32f, 1f);
            var px = new Color[s * s];
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            tex.SetPixels(px);
            // crude block letters B T, centred, padded
            StampB(tex, 48, 48, gold);
            StampT(tex, 140, 48, gold);
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

        static void StampB(Texture2D tex, int x, int y, Color c)
        {
            Fill(tex, x, y, 16, 160, c);
            Fill(tex, x, y + 144, 70, 16, c);
            Fill(tex, x, y + 72, 62, 16, c);
            Fill(tex, x, y, 70, 16, c);
            Fill(tex, x + 62, y + 88, 16, 56, c);
            Fill(tex, x + 62, y + 16, 16, 56, c);
        }

        static void StampT(Texture2D tex, int x, int y, Color c)
        {
            Fill(tex, x, y + 144, 80, 16, c);
            Fill(tex, x + 32, y, 16, 160, c);
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
