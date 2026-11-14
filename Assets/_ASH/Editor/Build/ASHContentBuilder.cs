using System.IO;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using ASH.Survival;
using ASH.World;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>
    /// Creates the slice's data and world prefabs: input actions, items, campfire recipe, survival tuning,
    /// resource nodes (deadwood, stones, chokecherries), stock tank, campfire, old campsite, dead trees, brush
    /// and the falling-ash FX. Placeholder geometry is built from primitives + the Gaia sample stone until vendor
    /// art replaces it. Safe to re-run (assets are updated in place, GUIDs kept).
    /// </summary>
    public static class ASHContentBuilder
    {
        public const string Root = "Assets/_ASH";
        public const string InputPath = Root + "/Settings/Input/ASH_InputActions.inputactions";
        public const string ItemDir = Root + "/Data/Items";
        public const string RecipeDir = Root + "/Data/Recipes";
        public const string TuningPath = Root + "/Data/Tuning/ASH_SurvivalTuning.asset";
        public const string WorldPrefabs = Root + "/Prefabs/World";
        public const string FxPrefabs = Root + "/Prefabs/FX";
        public const string MatDir = Root + "/Materials";
        public const string PWStone = "Assets/Procedural Worlds/Packages - Install/Asset Samples/Procedural Worlds/Content Resources/PW_Stone_01/PW_Stone_01.fbx";
        public const string PWStoneMat = "Assets/Procedural Worlds/Packages - Install/Asset Samples/Procedural Worlds/Content Resources/PW_Stone_01/PW_Stone_01.mat";

        [MenuItem("ASH/Build/1. Content (data + world prefabs)")]
        public static void BuildAll()
        {
            BuildInputActions();
            var tuning = ASHBuildUtil.Asset<ASHSurvivalTuning>(TuningPath);
            EditorUtility.SetDirty(tuning);

            // materials
            var dot = ASHBuildUtil.SoftDot(Root + "/Art/FX/ASH_SoftDot.png", 64, 0.3f);
            var puff = ASHBuildUtil.SoftDot(Root + "/Art/FX/ASH_SoftPuff.png", 128, 0.0f);
            var bark = ASHBuildUtil.Lit(MatDir + "/ASH_M_DeadBark.mat", new Color(0.33f, 0.29f, 0.25f), 0.12f);
            var wood = ASHBuildUtil.Lit(MatDir + "/ASH_M_Deadwood.mat", new Color(0.52f, 0.45f, 0.37f), 0.15f);
            var charred = ASHBuildUtil.Lit(MatDir + "/ASH_M_Charred.mat", new Color(0.09f, 0.08f, 0.075f), 0.1f);
            var ashMat = ASHBuildUtil.Lit(MatDir + "/ASH_M_AshPile.mat", new Color(0.42f, 0.41f, 0.39f), 0.05f);
            var leaf = ASHBuildUtil.Lit(MatDir + "/ASH_M_ChokecherryLeaf.mat", new Color(0.24f, 0.3f, 0.16f), 0.25f);
            var berry = ASHBuildUtil.Lit(MatDir + "/ASH_M_Chokecherry.mat", new Color(0.18f, 0.03f, 0.06f), 0.75f);
            var sage = ASHBuildUtil.Lit(MatDir + "/ASH_M_Sagebrush.mat", new Color(0.43f, 0.47f, 0.4f), 0.1f);
            var steel = ASHBuildUtil.Lit(MatDir + "/ASH_M_GalvanizedSteel.mat", new Color(0.52f, 0.53f, 0.52f), 0.55f, null, 0.8f);
            var water = ASHBuildUtil.Lit(MatDir + "/ASH_M_TankWater.mat", new Color(0.07f, 0.1f, 0.09f), 0.95f);
            var canvas = ASHBuildUtil.Lit(MatDir + "/ASH_M_OldCanvas.mat", new Color(0.45f, 0.4f, 0.31f), 0.1f);
            var rust = ASHBuildUtil.Lit(MatDir + "/ASH_M_Rust.mat", new Color(0.36f, 0.2f, 0.12f), 0.3f, null, 0.4f);
            var flameMat = ASHBuildUtil.Particle(MatDir + "/FX/ASH_P_Flame.mat", dot, true, Color.white);
            var smokeMat = ASHBuildUtil.Particle(MatDir + "/FX/ASH_P_Smoke.mat", puff, false, Color.white);
            var flakeMat = ASHBuildUtil.Particle(MatDir + "/FX/ASH_P_AshFlake.mat", dot, false, Color.white);
            var emberMat = ASHBuildUtil.Particle(MatDir + "/FX/ASH_P_Ember.mat", dot, true, Color.white);
            var stoneMesh = LoadStoneMesh();
            var stoneMat = AssetDatabase.LoadAssetAtPath<Material>(PWStoneMat) ?? ASHBuildUtil.Lit(MatDir + "/ASH_M_Stone.mat", new Color(0.5f, 0.48f, 0.45f), 0.2f);

            // prefabs that items reference first
            var campfire = BuildCampfire(stoneMesh, stoneMat, wood, charred, flameMat, smokeMat, emberMat);

            // items
            var deadwood = Item("ASH_Item_Deadwood", "deadwood", "Deadwood", "Dry, grey branches. Burns fast.", ASHItemCategory.Resource, 20, new Color(0.6f, 0.5f, 0.38f));
            var stone = Item("ASH_Item_Stone", "stone", "Stone", "Fist-sized creek stone. Good for a fire ring.", ASHItemCategory.Resource, 20, new Color(0.6f, 0.6f, 0.6f));
            var cherries = Item("ASH_Item_Chokecherries", "chokecherries", "Chokecherries", "Tart and astringent, but food. Restores hunger.", ASHItemCategory.Consumable, 10, new Color(0.55f, 0.1f, 0.18f));
            cherries.restoreHunger = 18f; cherries.restoreThirst = 4f; cherries.restoreHealth = 2f; EditorUtility.SetDirty(cherries);
            var fireItem = Item("ASH_Item_Campfire", "campfire", "Campfire", "A ring of stones and deadwood. Place it to make camp.", ASHItemCategory.Placeable, 1, new Color(0.95f, 0.55f, 0.25f));
            fireItem.placeablePrefab = campfire; fireItem.placementFootprint = new Vector3(1.3f, 0.5f, 1.3f); fireItem.maxPlacementSlope = 24f; EditorUtility.SetDirty(fireItem);

            var recipe = ASHBuildUtil.Asset<ASHRecipe>(RecipeDir + "/ASH_Recipe_Campfire.asset");
            recipe.recipeId = "campfire"; recipe.displayName = "Campfire"; recipe.description = "4 deadwood, 3 stones.";
            recipe.ingredients = new System.Collections.Generic.List<ASHRecipeIngredient>
            {
                new ASHRecipeIngredient { item = deadwood, amount = 4 },
                new ASHRecipeIngredient { item = stone, amount = 3 },
            };
            recipe.outputItem = fireItem; recipe.outputAmount = 1; recipe.craftSeconds = 1.5f;
            EditorUtility.SetDirty(recipe);

            // world prefabs
            BuildDeadwood(deadwood, wood, bark);
            BuildStonePile(stone, stoneMesh, stoneMat);
            BuildChokecherry(cherries, leaf, berry, bark);
            BuildStockTank(steel, water, rust);
            BuildCampsite(stoneMesh, stoneMat, charred, ashMat, canvas, wood, rust, smokeMat, emberMat);
            BuildDeadTree(bark);
            BuildSagebrush(sage, bark);
            BuildAshFall(flakeMat, emberMat);

            AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Content built (items, recipe, tuning, input actions, world prefabs, ash FX).");
        }

        // ------------------------------------------------------------------ data
        public static void BuildInputActions()
        {
            ASHBuildUtil.Folder(Path.GetDirectoryName(InputPath));
            var a = ASHInput.CreateDefaultActions();
            File.WriteAllText(InputPath, a.ToJson());
            Object.DestroyImmediate(a);
            AssetDatabase.ImportAsset(InputPath);
        }

        static ASHItemDefinition Item(string file, string id, string name, string desc, ASHItemCategory cat, int stack, Color tint)
        {
            var it = ASHBuildUtil.Asset<ASHItemDefinition>($"{ItemDir}/{file}.asset");
            it.itemId = id; it.displayName = name; it.description = desc; it.category = cat; it.maxStack = stack; it.uiTint = tint;
            EditorUtility.SetDirty(it);
            return it;
        }

        public static ASHItemDefinition LoadItem(string file) => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{ItemDir}/{file}.asset");
        public static ASHRecipe LoadRecipe() => AssetDatabase.LoadAssetAtPath<ASHRecipe>(RecipeDir + "/ASH_Recipe_Campfire.asset");
        public static GameObject LoadPrefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{WorldPrefabs}/{name}.prefab") ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{FxPrefabs}/{name}.prefab");

        static Mesh LoadStoneMesh()
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(PWStone)) if (o is Mesh m) return m;
            Debug.LogWarning("[ASH] PW_Stone_01 mesh not found; stones fall back to spheres.");
            return null;
        }

        static GameObject Stone(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 euler, float size)
        {
            if (mesh == null) return ASHBuildUtil.Prim(PrimitiveType.Sphere, name, parent, pos, euler, Vector3.one * size, mat);
            var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localEulerAngles = euler;
            float ext = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z);
            g.transform.localScale = Vector3.one * (size / Mathf.Max(0.001f, ext));
            g.GetComponent<MeshFilter>().sharedMesh = mesh; g.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }

        static GameObject NodeRoot(string name, out Transform visual)
        {
            var root = new GameObject(name);
            root.layer = LayerMask.NameToLayer("Resource") < 0 ? 0 : LayerMask.NameToLayer("Resource");
            visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false);
            return root;
        }

        static void Node(GameObject root, Transform visual, ASHItemDefinition item, string verb, int min, int max, int gathers, float hold)
        {
            var n = root.AddComponent<ASHResourceNode>();
            n.Configure(item, verb, min, max, gathers, hold);
            ASHBuildUtil.Set(n, "visualRoot", visual.gameObject);
        }

        // ------------------------------------------------------------------ prefabs
        static void BuildDeadwood(ASHItemDefinition item, Material wood, Material bark)
        {
            var root = NodeRoot("ASH_Deadwood", out var v);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Log", v, new Vector3(0, 0.12f, 0), new Vector3(0, 0, 90), new Vector3(0.2f, 0.85f, 0.2f), bark);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Branch1", v, new Vector3(0.1f, 0.2f, 0.15f), new Vector3(20, 35, 85), new Vector3(0.09f, 0.6f, 0.09f), wood);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Branch2", v, new Vector3(-0.25f, 0.08f, -0.2f), new Vector3(0, -40, 92), new Vector3(0.07f, 0.45f, 0.07f), wood);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Twig", v, new Vector3(0.45f, 0.25f, 0.05f), new Vector3(0, 10, 50), new Vector3(0.04f, 0.25f, 0.04f), wood);
            var c = root.AddComponent<BoxCollider>(); c.center = new Vector3(0, 0.18f, 0); c.size = new Vector3(1.8f, 0.4f, 0.8f);
            Node(root, v, item, "Gather", 1, 2, 3, 1.0f);
            ASHBuildUtil.SetLayerRecursive(root, root.layer);
            ASHBuildUtil.SavePrefab(root, WorldPrefabs + "/ASH_Deadwood.prefab");
        }

        static void BuildStonePile(ASHItemDefinition item, Mesh mesh, Material mat)
        {
            var root = NodeRoot("ASH_StonePile", out var v);
            Stone(v, "S1", mesh, mat, new Vector3(0, 0.08f, 0), new Vector3(10, 30, 0), 0.38f);
            Stone(v, "S2", mesh, mat, new Vector3(0.28f, 0.06f, 0.12f), new Vector3(0, 120, 15), 0.28f);
            Stone(v, "S3", mesh, mat, new Vector3(-0.2f, 0.05f, 0.22f), new Vector3(30, 200, 0), 0.24f);
            Stone(v, "S4", mesh, mat, new Vector3(0.05f, 0.05f, -0.28f), new Vector3(0, 290, 20), 0.22f);
            var c = root.AddComponent<BoxCollider>(); c.center = new Vector3(0.03f, 0.15f, 0); c.size = new Vector3(0.9f, 0.35f, 0.8f);
            Node(root, v, item, "Collect", 1, 2, 2, 1.2f);
            ASHBuildUtil.SetLayerRecursive(root, root.layer);
            ASHBuildUtil.SavePrefab(root, WorldPrefabs + "/ASH_StonePile.prefab");
        }

        static void BuildChokecherry(ASHItemDefinition item, Material leaf, Material berry, Material bark)
        {
            var root = NodeRoot("ASH_ChokecherryBush", out var v);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Stem", v, new Vector3(0, 0.4f, 0), Vector3.zero, new Vector3(0.06f, 0.4f, 0.06f), bark);
            var rnd = new System.Random(7);
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f; float r = 0.35f + (float)rnd.NextDouble() * 0.2f;
                ASHBuildUtil.Prim(PrimitiveType.Sphere, "Leaves" + i, v, new Vector3(Mathf.Cos(a) * r, 0.7f + (float)rnd.NextDouble() * 0.5f, Mathf.Sin(a) * r), Vector3.zero, new Vector3(0.75f, 0.6f, 0.75f) * (0.8f + (float)rnd.NextDouble() * 0.4f), leaf);
            }
            ASHBuildUtil.Prim(PrimitiveType.Sphere, "LeavesTop", v, new Vector3(0, 1.25f, 0), Vector3.zero, new Vector3(0.8f, 0.6f, 0.8f), leaf);
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f; float r = 0.55f + (float)rnd.NextDouble() * 0.2f;
                ASHBuildUtil.Prim(PrimitiveType.Sphere, "Cluster" + i, v, new Vector3(Mathf.Cos(a) * r, 0.65f + (float)rnd.NextDouble() * 0.6f, Mathf.Sin(a) * r), Vector3.zero, Vector3.one * 0.09f, berry);
            }
            var c = root.AddComponent<CapsuleCollider>(); c.center = new Vector3(0, 0.8f, 0); c.radius = 0.7f; c.height = 1.6f;
            Node(root, v, item, "Pick", 1, 2, 2, 0.8f);
            ASHBuildUtil.SetLayerRecursive(root, root.layer);
            ASHBuildUtil.SavePrefab(root, WorldPrefabs + "/ASH_ChokecherryBush.prefab");
        }

        static void BuildStockTank(Material steel, Material water, Material rust)
        {
            var root = new GameObject("ASH_StockTank");
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Tank", root.transform, new Vector3(0, 0.3f, 0), Vector3.zero, new Vector3(3f, 0.3f, 3f), steel);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Water", root.transform, new Vector3(0, 0.56f, 0), Vector3.zero, new Vector3(2.85f, 0.01f, 2.85f), water);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Rim", root.transform, new Vector3(0, 0.61f, 0), Vector3.zero, new Vector3(3.05f, 0.02f, 3.05f), rust);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "WindmillPipe", root.transform, new Vector3(1.4f, 1.0f, 0.3f), new Vector3(0, 0, 8), new Vector3(0.08f, 1f, 0.08f), rust);
            var c = root.AddComponent<CapsuleCollider>(); c.center = new Vector3(0, 0.3f, 0); c.radius = 1.5f; c.height = 0.6f; c.direction = 1;
            var b = root.AddComponent<BoxCollider>(); b.center = new Vector3(0, 0.3f, 0); b.size = new Vector3(2.2f, 0.62f, 2.2f);
            Object.DestroyImmediate(c);
            root.AddComponent<ASHWaterSource>();
            ASHBuildUtil.SavePrefab(root, WorldPrefabs + "/ASH_StockTank.prefab");
        }

        static GameObject BuildCampfire(Mesh stoneMesh, Material stoneMat, Material wood, Material charred, Material flameMat, Material smokeMat, Material emberMat)
        {
            var root = new GameObject("ASH_Campfire");
            var ring = new GameObject("StoneRing").transform; ring.SetParent(root.transform, false);
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f;
                Stone(ring, "Stone" + i, stoneMesh, stoneMat, new Vector3(Mathf.Cos(a) * 0.55f, 0.06f, Mathf.Sin(a) * 0.55f), new Vector3(0, i * 47f, 0), 0.28f);
            }
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Coals", root.transform, new Vector3(0, 0.02f, 0), Vector3.zero, new Vector3(0.8f, 0.02f, 0.8f), charred);
            for (int i = 0; i < 4; i++)
                ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Log" + i, root.transform, new Vector3(0, 0.18f, 0), new Vector3(62, i * 90f + 20f, 0), new Vector3(0.09f, 0.32f, 0.09f), wood);

            var light = new GameObject("FireLight", typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(root.transform, false); light.transform.localPosition = new Vector3(0, 0.6f, 0);
            light.type = LightType.Point; light.color = new Color(1f, 0.58f, 0.28f); light.range = 10f; light.intensity = 3.2f; light.shadows = LightShadows.Soft;

            var flames = Fx(root.transform, "Flames", flameMat, new Vector3(0, 0.12f, 0));
            ConfigureFlames(flames);
            var smoke = Fx(root.transform, "Smoke", smokeMat, new Vector3(0, 0.6f, 0));
            ConfigureSmoke(smoke, 0.4f, 2.4f, 5f, 9f, new Color(0.35f, 0.33f, 0.31f, 0.35f));
            var sparks = Fx(root.transform, "Sparks", emberMat, new Vector3(0, 0.3f, 0));
            ConfigureSparks(sparks);

            var col = root.AddComponent<BoxCollider>(); col.center = new Vector3(0, 0.2f, 0); col.size = new Vector3(1.3f, 0.4f, 1.3f);
            var cf = root.AddComponent<ASHCampfire>();
            ASHBuildUtil.Set(cf, "fireLight", light);
            ASHBuildUtil.SetList(cf, "effects", new Object[] { flames, smoke, sparks });
            return ASHBuildUtil.SavePrefab(root, WorldPrefabs + "/ASH_Campfire.prefab");
        }

        static void BuildCampsite(Mesh stoneMesh, Material stoneMat, Material charred, Material ashMat, Material canvas, Material wood, Material rust, Material smokeMat, Material emberMat)
        {
            var root = new GameObject("ASH_OldCampsite");
            var ring = new GameObject("ColdFireRing").transform; ring.SetParent(root.transform, false); ring.localPosition = new Vector3(-3f, 0, 2f);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                Stone(ring, "Stone" + i, stoneMesh, stoneMat, new Vector3(Mathf.Cos(a) * 0.7f, 0.05f, Mathf.Sin(a) * 0.7f), new Vector3(0, i * 33f, 0), 0.32f);
            }
            ASHBuildUtil.Prim(PrimitiveType.Sphere, "AshMound", ring, new Vector3(0, -0.02f, 0), Vector3.zero, new Vector3(1.1f, 0.12f, 1.1f), ashMat);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "CharredLog", ring, new Vector3(0.1f, 0.06f, 0), new Vector3(0, 30, 90), new Vector3(0.12f, 0.4f, 0.12f), charred);
            var smoulder = Fx(ring, "SmokeColumn", smokeMat, new Vector3(0, 0.4f, 0));
            ConfigureSmoke(smoulder, 1.2f, 2.2f, 14f, 22f, new Color(0.42f, 0.4f, 0.38f, 0.28f), 3f, 2.5f, 5f);
            var embers = Fx(ring, "Embers", emberMat, new Vector3(0, 0.1f, 0));
            ConfigureSparks(embers, 2f);

            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "SeatLog1", root.transform, new Vector3(-1.2f, 0.2f, 3.6f), new Vector3(0, 70, 90), new Vector3(0.38f, 0.9f, 0.38f), wood, true);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "SeatLog2", root.transform, new Vector3(-4.6f, 0.2f, 0.4f), new Vector3(0, -20, 90), new Vector3(0.34f, 0.8f, 0.34f), wood, true);
            // collapsed lean-to
            var lean = new GameObject("LeanTo").transform; lean.SetParent(root.transform, false); lean.localPosition = new Vector3(1.5f, 0, -0.5f); lean.localEulerAngles = new Vector3(0, 25, 0);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Ridge", lean, new Vector3(0, 1.2f, 0), new Vector3(0, 0, 90), new Vector3(0.08f, 1.3f, 0.08f), wood);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "PoleL", lean, new Vector3(-1.2f, 0.6f, 0), new Vector3(0, 0, 0), new Vector3(0.07f, 0.62f, 0.07f), wood);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "PoleR", lean, new Vector3(1.2f, 0.45f, 0.3f), new Vector3(35, 0, 0), new Vector3(0.07f, 0.6f, 0.07f), wood);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "Tarp", lean, new Vector3(0, 0.62f, -0.55f), new Vector3(-52, 0, 4), new Vector3(2.5f, 0.02f, 1.6f), canvas, true);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Can", root.transform, new Vector3(-2.3f, 0.06f, 1.1f), new Vector3(90, 40, 0), new Vector3(0.09f, 0.06f, 0.09f), rust);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "Crate", root.transform, new Vector3(2.8f, 0.22f, 2.2f), new Vector3(0, 18, 6), new Vector3(0.6f, 0.44f, 0.45f), wood, true);
            ASHBuildUtil.SavePrefab(root, WorldPrefabs + "/ASH_OldCampsite.prefab");
        }

        static void BuildDeadTree(Material bark)
        {
            var root = new GameObject("ASH_DeadTree");
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Trunk", root.transform, new Vector3(0, 2.6f, 0), new Vector3(3, 0, 2), new Vector3(0.32f, 2.6f, 0.32f), bark);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "BranchA", root.transform, new Vector3(0.6f, 3.6f, 0.1f), new Vector3(0, 0, -48), new Vector3(0.13f, 1.0f, 0.13f), bark);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "BranchB", root.transform, new Vector3(-0.5f, 4.2f, -0.2f), new Vector3(15, 0, 40), new Vector3(0.11f, 0.85f, 0.11f), bark);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "BranchC", root.transform, new Vector3(0.1f, 4.9f, 0.45f), new Vector3(38, 0, -10), new Vector3(0.08f, 0.6f, 0.08f), bark);
            ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Snag", root.transform, new Vector3(-0.35f, 2.2f, 0.2f), new Vector3(-20, 0, 60), new Vector3(0.09f, 0.45f, 0.09f), bark);
            var c = root.AddComponent<CapsuleCollider>(); c.center = new Vector3(0, 2.6f, 0); c.radius = 0.2f; c.height = 5.2f;
            ASHBuildUtil.SavePrefab(root, WorldPrefabs + "/ASH_DeadTree.prefab");
        }

        static void BuildSagebrush(Material sage, Material bark)
        {
            var root = new GameObject("ASH_Sagebrush");
            var rnd = new System.Random(3);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f;
                ASHBuildUtil.Prim(PrimitiveType.Sphere, "Puff" + i, root.transform, new Vector3(Mathf.Cos(a) * 0.25f, 0.3f + (float)rnd.NextDouble() * 0.15f, Mathf.Sin(a) * 0.25f), Vector3.zero, new Vector3(0.55f, 0.42f, 0.55f), sage);
            }
            ASHBuildUtil.Prim(PrimitiveType.Sphere, "Top", root.transform, new Vector3(0, 0.5f, 0), Vector3.zero, new Vector3(0.5f, 0.35f, 0.5f), sage);
            ASHBuildUtil.SavePrefab(root, WorldPrefabs + "/ASH_Sagebrush.prefab");
        }

        /// <summary>Applies the current ASHAshFall Default preset to the prefab asset and every instance in the open scene, then saves.</summary>
        [MenuItem("ASH/Build/Upgrade Ash Fall (prefab + open scene)")]
        public static void UpgradeAshFall()
        {
            string path = FxPrefabs + "/ASH_AshFall.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            root.GetComponent<ASH.World.ASHAshFall>().PresetDefault();
            PrefabUtility.SaveAsPrefabAsset(root, path); PrefabUtility.UnloadPrefabContents(root);
            int n = 0;
            foreach (var a in Object.FindObjectsByType<ASH.World.ASHAshFall>(FindObjectsInactive.Include))
            {
                Undo.RecordObject(a, "Upgrade ash fall"); a.PresetDefault(); EditorUtility.SetDirty(a);
                var ps = a.GetComponent<ParticleSystem>(); EditorUtility.SetDirty(ps); n++;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[ASH] Ash fall upgraded: prefab + {n} scene instance(s).");
        }

        static void BuildAshFall(Material flakeMat, Material emberMat)
        {
            var root = new GameObject("ASH_AshFall", typeof(ParticleSystem));
            root.GetComponent<ParticleSystemRenderer>().sharedMaterial = flakeMat;
            var embers = new GameObject("Embers", typeof(ParticleSystem));
            embers.transform.SetParent(root.transform, false);
            embers.GetComponent<ParticleSystemRenderer>().sharedMaterial = emberMat;
            root.AddComponent<ASHAshFall>().Apply();
            ASHBuildUtil.SavePrefab(root, FxPrefabs + "/ASH_AshFall.prefab");
        }

        // ------------------------------------------------------------------ particles
        static ParticleSystem Fx(Transform parent, string name, Material mat, Vector3 pos)
        {
            var g = new GameObject(name, typeof(ParticleSystem));
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localEulerAngles = new Vector3(-90, 0, 0);
            var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.GetComponent<ParticleSystem>();
        }

        static Gradient Grad((float t, Color c)[] colors, (float t, float a)[] alphas)
        {
            var g = new Gradient(); var ck = new GradientColorKey[colors.Length]; var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i].c, colors[i].t);
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            g.SetKeys(ck, ak); return g;
        }

        static void ConfigureFlames(ParticleSystem ps)
        {
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f); m.maxParticles = 250; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startColor = new Color(1f, 0.75f, 0.4f);
            var e = ps.emission; e.rateOverTime = 45f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 8f; s.radius = 0.22f;
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { (0f, new Color(1f, 0.9f, 0.6f)), (0.4f, new Color(1f, 0.5f, 0.15f)), (1f, new Color(0.6f, 0.1f, 0.05f)) }, new[] { (0f, 0f), (0.15f, 1f), (0.7f, 0.6f), (1f, 0f) });
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 0.2f));
            var n = ps.noise; n.enabled = true; n.strength = 0.25f; n.frequency = 1.5f;
        }

        static void ConfigureSmoke(ParticleSystem ps, float speedMin, float speedMax, float lifeMin, float lifeMax, Color c, float rate = 7f, float size0 = 0.5f, float sizeGrow = 5f)
        {
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax); m.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            m.startSize = new ParticleSystem.MinMaxCurve(size0, size0 * 1.6f); m.maxParticles = 400; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startColor = c; m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var e = ps.emission; e.rateOverTime = rate;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 6f; s.radius = 0.25f;
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { (0f, Color.white), (1f, new Color(0.8f, 0.8f, 0.8f)) }, new[] { (0f, 0f), (0.1f, 1f), (0.6f, 0.5f), (1f, 0f) });
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(sizeGrow, AnimationCurve.Linear(0, 0.15f, 1, 1f));
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(0.3f, 0.7f); v.y = new ParticleSystem.MinMaxCurve(0f, 0f); v.z = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            var n = ps.noise; n.enabled = true; n.strength = 0.35f; n.frequency = 0.25f;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        }

        static void ConfigureSparks(ParticleSystem ps, float rate = 6f)
        {
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f); m.maxParticles = 80; m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.startColor = new Color(1f, 0.6f, 0.2f); m.gravityModifier = -0.05f;
            var e = ps.emission; e.rateOverTime = rate;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 20f; s.radius = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = Grad(new[] { (0f, new Color(1f, 0.8f, 0.4f)), (1f, new Color(1f, 0.25f, 0.05f)) }, new[] { (0f, 1f), (0.7f, 0.8f), (1f, 0f) });
            var n = ps.noise; n.enabled = true; n.strength = 0.6f; n.frequency = 1f;
        }
    }
}
