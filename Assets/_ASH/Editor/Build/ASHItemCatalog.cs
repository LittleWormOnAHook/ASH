using System.Collections.Generic;
using System.IO;
using ASH.Interaction;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>
    /// Table-driven item + recipe catalog for the slice (placeholders; see Docs/ItemsAndCrafting.md). Creates or
    /// updates ScriptableObjects in place (GUIDs kept), primitive pickup prefabs, salvage world nodes (crate, chair,
    /// fiber grass), tool hooks on existing nodes, the stock-tank fill, the player's recipe list and starting GPS.
    /// Optional truck parts live only in Data/Items/Optional_TruckParts and are referenced by nothing else.
    /// </summary>
    public static class ASHItemCatalog
    {
        public const string ItemDir = ASHContentBuilder.ItemDir;
        public const string TruckDir = ItemDir + "/Optional_TruckParts";
        public const string RecipeDir = ASHContentBuilder.RecipeDir;
        public const string PickupDir = ASHContentBuilder.Root + "/Prefabs/Pickups";
        const string Mat = ASHContentBuilder.MatDir + "/Pickups";

        enum Shape { Box, Cyl, Ball, Capsule, Flat }
        sealed class Def
        {
            public string file, id, name, desc, verb = "Use"; public ASHItemCategory cat; public int stack = 10; public Color tint;
            public Shape shape = Shape.Box; public Vector3 size = new Vector3(0.3f, 0.2f, 0.3f); public bool pickup = true; public string dir = ItemDir;
            public System.Action<ASHItemDefinition> extra;
        }

        static Def D(string file, string name, ASHItemCategory cat, string desc, Color tint, Shape shape, Vector3 size, int stack = 10, string verb = "Use", System.Action<ASHItemDefinition> extra = null, bool pickup = true, string dir = ItemDir)
            => new Def { file = file, id = file.ToLowerInvariant(), name = name, cat = cat, desc = desc, tint = tint, shape = shape, size = size, stack = stack, verb = verb, extra = extra, pickup = pickup, dir = dir };

        static ASHItemDefinition I(string file) => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{ItemDir}/ASH_Item_{file}.asset");
        static ASHRecipeIngredient G(string file, int n) => new ASHRecipeIngredient { item = I(file) ?? ASHContentBuilder.LoadItem(file), amount = n };

        [MenuItem("ASH/Build/6. Item Catalog (items, recipes, pickups, salvage)")]
        public static void Build()
        {
            var C = new Color(0.62f, 0.56f, 0.46f);
            var defs = new List<Def>
            {
                // resources
                D("Cloth", "Cloth", ASHItemCategory.Resource, "Clean strips of fabric. Bandages, repairs, tinder.", new Color(0.75f, 0.68f, 0.55f), Shape.Box, new Vector3(0.3f, 0.06f, 0.24f)),
                D("PlantFiber", "Plant Fiber", ASHItemCategory.Resource, "Dry grass and inner bark, twisted into strands.", new Color(0.66f, 0.62f, 0.38f), Shape.Box, new Vector3(0.35f, 0.08f, 0.15f), 20),
                D("ScrapMetal", "Scrap Metal", ASHItemCategory.Resource, "Flattened tin and bent wire.", new Color(0.55f, 0.56f, 0.58f), Shape.Box, new Vector3(0.25f, 0.04f, 0.2f), 20),
                D("EmptyTin", "Empty Tin Can", ASHItemCategory.Resource, "Holds water from a tank or creek. Can be flattened into scrap.", new Color(0.7f, 0.7f, 0.72f), Shape.Cyl, new Vector3(0.09f, 0.06f, 0.09f), 10, "Use",
                    it => Breakdown(it, ASHToolType.None, false, 1.5f, 1f, ("ScrapMetal", 1))),
                D("ReclaimedWood", "Reclaimed Wood", ASHItemCategory.Resource, "Planks from crates and furniture. Burns longer than deadwood.", new Color(0.55f, 0.42f, 0.3f), Shape.Box, new Vector3(0.5f, 0.05f, 0.12f), 20),
                // wood from axe harvesting (dead trees, logs, stumps): see WoodDefs()
                // food & water
                D("RawMeat", "Raw Meat", ASHItemCategory.Consumable, "Edible raw in a pinch, but it will cost you. Cook it at a fire.", new Color(0.7f, 0.25f, 0.25f), Shape.Box, new Vector3(0.2f, 0.07f, 0.14f), 5, "Eat", it => Restore(it, 8, 0, -4)),
                D("CookedMeat", "Cooked Meat", ASHItemCategory.Consumable, "Charred outside, hot inside.", new Color(0.48f, 0.28f, 0.16f), Shape.Box, new Vector3(0.2f, 0.07f, 0.14f), 5, "Eat", it => Restore(it, 35, 0, 4)),
                D("RawFish", "Raw Fish", ASHItemCategory.Consumable, "A gutted creek trout. Cook it first.", new Color(0.62f, 0.6f, 0.5f), Shape.Capsule, new Vector3(0.07f, 0.16f, 0.07f), 5, "Eat", it => Restore(it, 6, 1, -3)),
                D("CookedFish", "Cooked Fish", ASHItemCategory.Consumable, "Flaky and smoky.", new Color(0.7f, 0.55f, 0.35f), Shape.Capsule, new Vector3(0.07f, 0.16f, 0.07f), 5, "Eat", it => Restore(it, 28, 2, 3)),
                D("DriedChokecherries", "Dried Chokecherries", ASHItemCategory.Consumable, "Fire-dried berries. Light and they keep.", new Color(0.35f, 0.08f, 0.12f), Shape.Ball, new Vector3(0.14f, 0.1f, 0.14f), 20, "Eat", it => Restore(it, 14, 0, 1)),
                D("CannedBeans", "Canned Beans", ASHItemCategory.Consumable, "Dented, label gone. Open it with a can opener or a blade, or smash it and lose some.", new Color(0.62f, 0.5f, 0.32f), Shape.Cyl, new Vector3(0.09f, 0.06f, 0.09f), 5, "Eat", it => { Restore(it, 30, 2, 0); Can(it); }),
                D("CannedPeaches", "Canned Peaches", ASHItemCategory.Consumable, "Sweet syrup and soft fruit. Needs opening.", new Color(0.86f, 0.6f, 0.3f), Shape.Cyl, new Vector3(0.09f, 0.06f, 0.09f), 5, "Eat", it => { Restore(it, 18, 12, 0); Can(it); }),
                D("TinOfWater", "Tin of Creek Water", ASHItemCategory.Consumable, "Silty and unsafe. Boil it at a fire.", new Color(0.45f, 0.52f, 0.5f), Shape.Cyl, new Vector3(0.09f, 0.06f, 0.09f), 5, "Drink", it => { Restore(it, 0, 15, -2); it.leftoverItem = I("EmptyTin"); }, pickup: false),
                D("BoiledWater", "Tin of Boiled Water", ASHItemCategory.Consumable, "Safe to drink.", new Color(0.6f, 0.7f, 0.75f), Shape.Cyl, new Vector3(0.09f, 0.06f, 0.09f), 5, "Drink", it => { Restore(it, 0, 28, 0); it.leftoverItem = I("EmptyTin"); }, pickup: false),
                // medical
                D("Bandage", "Bandage", ASHItemCategory.Medical, "Clean cloth bandage. Stops bleeding, restores some health.", new Color(0.9f, 0.88f, 0.82f), Shape.Cyl, new Vector3(0.08f, 0.04f, 0.08f), 10, "Apply", it => Restore(it, 0, 0, 15)),
                D("FiberBandage", "Fiber Bandage", ASHItemCategory.Medical, "Coarse plant-fiber wrap. Better than nothing.", new Color(0.7f, 0.66f, 0.45f), Shape.Cyl, new Vector3(0.08f, 0.04f, 0.08f), 10, "Apply", it => Restore(it, 0, 0, 8)),
                D("AntisepticBandage", "Antiseptic Bandage", ASHItemCategory.Medical, "Bandage soaked in alcohol. Restores more health.", new Color(0.85f, 0.9f, 0.85f), Shape.Cyl, new Vector3(0.08f, 0.04f, 0.08f), 10, "Apply", it => Restore(it, 0, 0, 30)),
                D("RubbingAlcohol", "Rubbing Alcohol", ASHItemCategory.Resource, "Antiseptic and an accelerant. Don't drink it.", new Color(0.55f, 0.7f, 0.85f), Shape.Cyl, new Vector3(0.07f, 0.1f, 0.07f), 5),
                D("Whiskey", "Whiskey", ASHItemCategory.Consumable, "Half a bottle. Antiseptic in a pinch, or a moment of warmth.", new Color(0.65f, 0.4f, 0.15f), Shape.Cyl, new Vector3(0.08f, 0.13f, 0.08f), 3, "Drink", it => { Restore(it, 0, 3, 0); it.restoreStamina = 15; }),
                D("Painkillers", "Painkillers", ASHItemCategory.Medical, "A few pills rattling in a bottle.", new Color(0.9f, 0.85f, 0.7f), Shape.Cyl, new Vector3(0.05f, 0.05f, 0.05f), 10, "Take", it => Restore(it, 0, 0, 10)),
                // tools (no combat use)
                D("Hatchet", "Hatchet", ASHItemCategory.Tool, HatchetDesc, new Color(0.55f, 0.5f, 0.45f), Shape.Box, new Vector3(0.08f, 0.04f, 0.38f), 1, "Use", it => { it.toolType = ASHToolType.Axe; it.equipSlot = ASHEquipSlot.MainHand; }),
                D("HuntingKnife", "Hunting Knife", ASHItemCategory.Tool, "Cuts cloth into strips, harvests meat, opens cans.", new Color(0.6f, 0.6f, 0.62f), Shape.Box, new Vector3(0.04f, 0.03f, 0.3f), 1, "Use", it => it.toolType = ASHToolType.Knife),
                D("CanOpener", "Can Opener", ASHItemCategory.Tool, "Opens cans cleanly and keeps the tin.", new Color(0.65f, 0.65f, 0.6f), Shape.Box, new Vector3(0.05f, 0.02f, 0.14f), 1, "Use", it => it.toolType = ASHToolType.CanOpener),
                D("ImprovisedKnife", "Improvised Knife", ASHItemCategory.Tool, "Scrap blade wrapped in cloth. Works as a knife.", new Color(0.5f, 0.5f, 0.48f), Shape.Box, new Vector3(0.04f, 0.03f, 0.24f), 1, "Use", it => it.toolType = ASHToolType.Knife, pickup: false),
                D("DamagedGPS", "Damaged GPS", ASHItemCategory.Tool, "Trailmate 40. Cracked screen, corrupt map data, a rough fix at best. Log viewpoints from high ground to rebuild the map. [M]", new Color(0.4f, 0.55f, 0.45f), Shape.Box, new Vector3(0.07f, 0.03f, 0.13f), 1, "Open map", it => it.toolType = ASHToolType.GPS, pickup: false),
                // salvage
                D("OldShirt", "Old Flannel Shirt", ASHItemCategory.Salvage, "Too torn to wear. Cut it into cloth (a knife gets more).", new Color(0.55f, 0.25f, 0.2f), Shape.Box, new Vector3(0.35f, 0.05f, 0.3f), 5, "Use",
                    it => Breakdown(it, ASHToolType.Knife, false, 3f, 0.5f, ("Cloth", 2))),
                D("Rags", "Rags", ASHItemCategory.Salvage, "Greasy rags. Rinse and tear for a strip of cloth.", new Color(0.45f, 0.42f, 0.35f), Shape.Box, new Vector3(0.22f, 0.05f, 0.2f), 10, "Use",
                    it => Breakdown(it, ASHToolType.None, false, 1.5f, 1f, ("Cloth", 1))),
                D("DeadRabbit", "Dead Rabbit", ASHItemCategory.Salvage, "Fresh. Needs a knife to harvest.", new Color(0.5f, 0.42f, 0.35f), Shape.Capsule, new Vector3(0.12f, 0.18f, 0.12f), 2, "Use",
                    it => Breakdown(it, ASHToolType.Knife, true, 5f, 0.5f, ("RawMeat", 2))),
                D("WholeTrout", "Whole Trout", ASHItemCategory.Salvage, "Stranded in a drying pool. Gut it (a knife wastes less).", new Color(0.55f, 0.55f, 0.45f), Shape.Capsule, new Vector3(0.07f, 0.18f, 0.07f), 3, "Use",
                    it => Breakdown(it, ASHToolType.Knife, false, 3f, 0.5f, ("RawFish", 1))),
                // optional, isolated: truck parts (vehicle undecided; nothing references these)
                D("GasCan", "Gas Can (optional)", ASHItemCategory.Key, "Half full. Optional truck content: undecided.", new Color(0.75f, 0.2f, 0.15f), Shape.Box, new Vector3(0.3f, 0.35f, 0.18f), 1, pickup: false, dir: TruckDir),
                D("SparkPlugs", "Spark Plugs (optional)", ASHItemCategory.Key, "Optional truck content: undecided.", C, Shape.Box, new Vector3(0.1f, 0.05f, 0.1f), 4, pickup: false, dir: TruckDir),
                D("CarBattery", "Car Battery (optional)", ASHItemCategory.Key, "Optional truck content: undecided.", new Color(0.2f, 0.2f, 0.22f), Shape.Box, new Vector3(0.3f, 0.2f, 0.18f), 1, pickup: false, dir: TruckDir),
                D("RadiatorHose", "Radiator Hose (optional)", ASHItemCategory.Key, "Optional truck content: undecided.", new Color(0.15f, 0.15f, 0.15f), Shape.Cyl, new Vector3(0.05f, 0.25f, 0.05f), 2, pickup: false, dir: TruckDir),
                D("SpareTire", "Spare Tire (optional)", ASHItemCategory.Key, "Optional truck content: undecided.", new Color(0.12f, 0.12f, 0.12f), Shape.Cyl, new Vector3(0.6f, 0.1f, 0.6f), 1, pickup: false, dir: TruckDir),
                D("FanBelt", "Fan Belt (optional)", ASHItemCategory.Key, "Optional truck content: undecided.", new Color(0.18f, 0.16f, 0.14f), Shape.Cyl, new Vector3(0.2f, 0.01f, 0.2f), 2, pickup: false, dir: TruckDir),
            };
            defs.AddRange(WoodDefs());
            defs.AddRange(PackDefs());

            // pass 1: create assets so cross references resolve; pass 2: configure
            foreach (var d in defs) ASHBuildUtil.Asset<ASHItemDefinition>($"{d.dir}/ASH_Item_{d.file}.asset");
            ASHBuildUtil.SaveOwn();
            foreach (var d in defs) ConfigureDef(d);
            var cherries = ASHContentBuilder.LoadItem("ASH_Item_Chokecherries"); if (cherries) { cherries.useVerb = "Eat"; EditorUtility.SetDirty(cherries); }

            var recipes = BuildRecipes();
            BuildSalvageNodes();
            HookExistingPrefabs();
            WirePlayer(recipes);
            ASHBuildUtil.SaveOwn();
            ASH.EditorTools.World.ASHSurvivalPackTools.ApplyAfterCatalog();
            Debug.Log($"[ASH] Item catalog: {defs.Count} items, {recipes.Count} recipes, pickups in {PickupDir}");
        }

        static void ConfigureDef(Def d)
        {
            var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{d.dir}/ASH_Item_{d.file}.asset");
            it.itemId = d.id; it.displayName = d.name; it.description = d.desc; it.category = d.cat; it.maxStack = d.stack; it.uiTint = d.tint; it.useVerb = d.verb;
            it.toolType = ASHToolType.None; it.requiresOpener = false; it.leftoverItem = null; it.breakdownYield = new List<ASHRecipeIngredient>();
            d.extra?.Invoke(it);
            EditorUtility.SetDirty(it);
            PickupPrefab(d, it); // every item gets a prefab (icon source); 'pickup' only marks world placement
        }

        // ------------------------------------------------------------------ wood (axe harvesting)
        static List<Def> WoodDefs() => new List<Def>
        {
            D("Firewood", "Firewood", ASHItemCategory.Resource, "Split dead timber from felled trees, logs and stumps. Burns long and hot.", new Color(0.5f, 0.38f, 0.26f), Shape.Box, new Vector3(0.42f, 0.11f, 0.13f), 20, pickup: false),
            D("Kindling", "Kindling", ASHItemCategory.Resource, "Dry splinters and shavings. Gets a fire going.", new Color(0.72f, 0.6f, 0.42f), Shape.Box, new Vector3(0.28f, 0.04f, 0.1f), 30, pickup: false),
        };

        static List<ASHRecipe> WoodRecipes()
        {
            var fireItem = ASHContentBuilder.LoadItem("ASH_Item_Campfire");
            return new List<ASHRecipe>
            {
                R("CampfireFirewood", "Campfire (firewood)", ASHRecipeCategory.Fire, "Split firewood and kindling: the best fire you can build.", fireItem, 1, 2f, ASHToolType.None, ASHCraftStation.Anywhere, G("Firewood", 2), G("Kindling", 2), G("ASH_Item_Stone", 3)),
                R("SplitKindling", "Split Kindling", ASHRecipeCategory.Materials, "Split a piece of firewood into kindling with the hatchet.", I("Kindling"), 3, 2f, ASHToolType.Axe, ASHCraftStation.Anywhere, G("Firewood", 1)),
            };
        }

        /// <summary>Creates/updates only the wood items + recipes and appends the recipes to the player (prefab + scene) without
        /// touching other items or recipes. Build() covers them too.</summary>
        public static List<ASHRecipe> BuildWoodContent()
        {
            var defs = WoodDefs();
            foreach (var d in defs) ASHBuildUtil.Asset<ASHItemDefinition>($"{d.dir}/ASH_Item_{d.file}.asset");
            ASHBuildUtil.SaveOwn();
            foreach (var d in defs) ConfigureDef(d);
            var hatchet = I("Hatchet"); if (hatchet) { hatchet.description = HatchetDesc; EditorUtility.SetDirty(hatchet); }
            var recipes = WoodRecipes();
            AppendRecipesToPlayer(recipes);
            return recipes;
        }
/// <summary>Appends recipes to the player's crafting list (prefab + scene player) without touching the others.</summary>
        public static void AppendRecipesToPlayer(List<ASHRecipe> recipes)
        {
            void Append(GameObject go)
            {
                var craft = go ? go.GetComponent<ASHCrafting>() : null; if (!craft) return;
                var so = new SerializedObject(craft); var p = so.FindProperty("recipes");
                foreach (var r in recipes)
                {
                    bool has = false; for (int i = 0; i < p.arraySize; i++) if (p.GetArrayElementAtIndex(i).objectReferenceValue == r) has = true;
                    if (!has) { p.arraySize++; p.GetArrayElementAtIndex(p.arraySize - 1).objectReferenceValue = r; }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditPrefab(ASHPlayerBuilder.PrefabPath, Append);
            var pl = GameObject.Find("ASH_Player"); if (pl) { Append(pl); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(pl.scene); }
            ASHBuildUtil.SaveOwn();
        }

        // ------------------------------------------------------------------ HQ Survival Pack items (weapons, lights, supplies)
        static List<Def> PackDefs()
        {
            var gun = new Color(0.3f, 0.28f, 0.26f); var brass = new Color(0.75f, 0.6f, 0.3f);
            return new List<Def>
            {
                D("HuntingRifle", "Hunting Rifle", ASHItemCategory.Weapon, "Bolt-action .30-06 with a worn scope. Loud, heavy, and rounds are scarce. [hold: hotbar or cross bar, LMB fires]", gun, Shape.Box, new Vector3(0.08f, 0.08f, 1.1f), 1, "Hold"),
                D("Pistol", "Pistol", ASHItemCategory.Weapon, "A scuffed 9mm semi-auto. [hold: hotbar or cross bar, LMB fires]", gun, Shape.Box, new Vector3(0.04f, 0.13f, 0.2f), 1, "Hold"),
                D("WoodenBow", "Wooden Bow", ASHItemCategory.Weapon, "Self bow of seasoned ash wood. Quiet; arrows can be made. [hold: hotbar or cross bar, LMB shoots]", new Color(0.5f, 0.36f, 0.22f), Shape.Box, new Vector3(0.05f, 1.2f, 0.12f), 1, "Hold"),
                D("RifleRounds", ".30-06 Rounds", ASHItemCategory.Ammo, "Hunting rifle cartridges.", brass, Shape.Box, new Vector3(0.1f, 0.05f, 0.07f), 20),
                D("PistolRounds", "9mm Rounds", ASHItemCategory.Ammo, "Pistol cartridges.", brass, Shape.Box, new Vector3(0.08f, 0.04f, 0.06f), 30),
                D("WoodenArrow", "Wooden Arrow", ASHItemCategory.Ammo, "Straight shaft, scrap-metal head, fiber-bound.", new Color(0.6f, 0.48f, 0.3f), Shape.Box, new Vector3(0.02f, 0.02f, 0.7f), 20),
                D("Lantern", "Oil Lantern", ASHItemCategory.Light, "Hangs from your left hip. Burns rubbing alcohol. Toggle with [L]; refill by using rubbing alcohol (it tops itself up from the pack when it runs dry).", new Color(0.4f, 0.35f, 0.25f), Shape.Cyl, new Vector3(0.14f, 0.15f, 0.14f), 1, "Hold"),
                D("Torch", "Torch", ASHItemCategory.Light, "Cloth soaked in alcohol, wrapped on a stick. Held high in the left hand; hold [RMB] to raise it toward where you look. Burns about five minutes. Toggle with [L].", new Color(0.45f, 0.32f, 0.2f), Shape.Box, new Vector3(0.05f, 0.05f, 0.6f), 3, "Hold"),
                D("Flashlight", "Flashlight", ASHItemCategory.Light, "Left hand; hold [RMB] to aim the beam where you look. Runs on batteries. Toggle with [L].", new Color(0.2f, 0.2f, 0.22f), Shape.Cyl, new Vector3(0.05f, 0.12f, 0.05f), 1, "Hold"),
                D("Battery", "Battery", ASHItemCategory.Resource, "A D-cell with some charge left. Powers the flashlight.", new Color(0.3f, 0.3f, 0.25f), Shape.Cyl, new Vector3(0.04f, 0.04f, 0.04f), 10),
                D("Rope", "Rope", ASHItemCategory.Resource, "Twisted plant fiber cordage.", new Color(0.62f, 0.55f, 0.38f), Shape.Cyl, new Vector3(0.18f, 0.04f, 0.18f), 10),
                D("FirstAidKit", "First Aid Kit", ASHItemCategory.Medical, "Gauze, tape and antiseptic wipes. Restores a lot of health.", new Color(0.75f, 0.2f, 0.18f), Shape.Box, new Vector3(0.25f, 0.1f, 0.18f), 3, "Apply", it => Restore(it, 0, 0, 45)),
                D("EnergyBar", "Energy Bar", ASHItemCategory.Consumable, "Dense, sweet, a little stale.", new Color(0.7f, 0.55f, 0.3f), Shape.Box, new Vector3(0.12f, 0.02f, 0.04f), 10, "Eat", it => { Restore(it, 22, 0, 0); it.restoreStamina = 10; }),
                D("SodaCan", "Can of Soda", ASHItemCategory.Consumable, "Warm and flat. Sugar and water. Keep the can.", new Color(0.7f, 0.2f, 0.2f), Shape.Cyl, new Vector3(0.07f, 0.06f, 0.07f), 6, "Drink", it => { Restore(it, 6, 14, 0); it.leftoverItem = I("EmptyTin"); }),
                D("BottledWater", "Bottled Water", ASHItemCategory.Consumable, "Sealed. Safe to drink.", new Color(0.55f, 0.7f, 0.8f), Shape.Cyl, new Vector3(0.07f, 0.12f, 0.07f), 5, "Drink", it => Restore(it, 0, 30, 0)),
            };
        }

        static List<ASHRecipe> PackRecipes()
        {
            var deadwood = ASHContentBuilder.LoadItem("ASH_Item_Deadwood");
            return new List<ASHRecipe>
            {
                R("Torch", "Torch", ASHRecipeCategory.Tools, "Wrap alcohol-soaked cloth around a branch. Burns about five minutes.", I("Torch"), 1, 2f, ASHToolType.None, ASHCraftStation.Anywhere, new ASHRecipeIngredient { item = deadwood, amount = 1 }, G("Cloth", 1), G("RubbingAlcohol", 1)),
                R("TorchKindling", "Torch (kindling)", ASHRecipeCategory.Tools, "Bind kindling into a handle instead of a branch.", I("Torch"), 1, 2.5f, ASHToolType.None, ASHCraftStation.Anywhere, G("Kindling", 2), G("Cloth", 1), G("RubbingAlcohol", 1)),
                R("Rope", "Rope", ASHRecipeCategory.Materials, "Twist plant fiber into cordage.", I("Rope"), 1, 4f, ASHToolType.None, ASHCraftStation.Anywhere, G("PlantFiber", 4)),
                R("WoodenArrows", "Wooden Arrows", ASHRecipeCategory.Tools, "Whittle shafts from kindling, scrap heads, fiber bindings.", I("WoodenArrow"), 3, 6f, ASHToolType.Knife, ASHCraftStation.Anywhere, G("Kindling", 2), G("ScrapMetal", 1), G("PlantFiber", 1)),
                R("WoodenBow", "Wooden Bow", ASHRecipeCategory.Tools, "Carve a bow stave from split firewood and string it with rope.", I("WoodenBow"), 1, 10f, ASHToolType.Knife, ASHCraftStation.Anywhere, G("Firewood", 2), G("Rope", 1)),
            };
        }

        /// <summary>Creates/updates only the HQ Survival Pack items + recipes and appends the recipes to the player.</summary>
        public static List<ASHRecipe> BuildPackContent()
        {
            var defs = PackDefs();
            foreach (var d in defs) ASHBuildUtil.Asset<ASHItemDefinition>($"{d.dir}/ASH_Item_{d.file}.asset");
            ASHBuildUtil.SaveOwn();
            foreach (var d in defs) ConfigureDef(d);
            var recipes = PackRecipes();
            AppendRecipesToPlayer(recipes);
            return recipes;
        }
        public static readonly string[] PackItemFiles = { "HuntingRifle", "Pistol", "WoodenBow", "RifleRounds", "PistolRounds", "WoodenArrow", "Lantern", "Torch", "Flashlight", "Battery", "Rope", "FirstAidKit", "EnergyBar", "SodaCan", "BottledWater" };

        const string HatchetDesc = "Fells dead trees and chops logs and stumps into firewood and kindling. Splits deadwood faster; breaks down crates and furniture. Opens cans.";

        static void Restore(ASHItemDefinition it, float hunger, float thirst, float health) { it.restoreHunger = hunger; it.restoreThirst = thirst; it.restoreHealth = health; it.restoreStamina = 0; }
        static void Can(ASHItemDefinition it) { it.requiresOpener = true; it.smashedYield = 0.65f; it.leftoverItem = I("EmptyTin"); }
        static void Breakdown(ASHItemDefinition it, ASHToolType tool, bool required, float secs, float noToolYield, params (string file, int n)[] yield)
        {
            it.breakdownTool = tool; it.breakdownToolRequired = required; it.breakdownSeconds = secs; it.breakdownNoToolYield = noToolYield;
            it.breakdownYield = new List<ASHRecipeIngredient>(); foreach (var y in yield) it.breakdownYield.Add(G(y.file, y.n));
        }

        // ------------------------------------------------------------------ recipes
        static List<ASHRecipe> BuildRecipes()
        {
            var list = new List<ASHRecipe>();
            var campfire = ASHContentBuilder.LoadRecipe();
            if (campfire) { campfire.category = ASHRecipeCategory.Fire; campfire.description = "A ring of stones and deadwood. Place it to make camp, cook and dry food."; EditorUtility.SetDirty(campfire); list.Add(campfire); }
            var fireItem = ASHContentBuilder.LoadItem("ASH_Item_Campfire");
            list.Add(R("CampfirePlanks", "Campfire (planks)", ASHRecipeCategory.Fire, "Reclaimed planks burn longer than deadwood.", fireItem, 1, 2f, ASHToolType.None, ASHCraftStation.Anywhere, G("ReclaimedWood", 3), G("ASH_Item_Stone", 3)));
            list.Add(R("CampfireAlcohol", "Campfire (alcohol start)", ASHRecipeCategory.Fire, "A splash of alcohol gets damp wood going: less deadwood needed.", fireItem, 1, 1.5f, ASHToolType.None, ASHCraftStation.Anywhere, G("ASH_Item_Deadwood", 2), G("ASH_Item_Stone", 3), G("RubbingAlcohol", 1)));
            list.Add(R("Bandage", "Bandage", ASHRecipeCategory.Medical, "Tear clean cloth into a bandage.", I("Bandage"), 1, 2f, ASHToolType.None, ASHCraftStation.Anywhere, G("Cloth", 1)));
            list.Add(R("FiberBandage", "Fiber Bandage", ASHRecipeCategory.Medical, "Twist plant fiber into a rough wrap.", I("FiberBandage"), 1, 3f, ASHToolType.None, ASHCraftStation.Anywhere, G("PlantFiber", 3)));
            list.Add(R("AntisepticBandage", "Antiseptic Bandage", ASHRecipeCategory.Medical, "Soak a bandage in rubbing alcohol.", I("AntisepticBandage"), 1, 2f, ASHToolType.None, ASHCraftStation.Anywhere, G("Bandage", 1), G("RubbingAlcohol", 1)));
            list.Add(R("AntisepticBandageWhiskey", "Antiseptic Bandage (whiskey)", ASHRecipeCategory.Medical, "Whiskey works too. A waste of whiskey.", I("AntisepticBandage"), 1, 2f, ASHToolType.None, ASHCraftStation.Anywhere, G("Bandage", 1), G("Whiskey", 1)));
            list.Add(R("CookMeat", "Cook Meat", ASHRecipeCategory.Food, "Cook raw meat over a lit campfire.", I("CookedMeat"), 1, 6f, ASHToolType.None, ASHCraftStation.Campfire, G("RawMeat", 1)));
            list.Add(R("CookFish", "Cook Fish", ASHRecipeCategory.Food, "Cook a gutted fish over a lit campfire.", I("CookedFish"), 1, 5f, ASHToolType.None, ASHCraftStation.Campfire, G("RawFish", 1)));
            list.Add(R("DryChokecherries", "Dry Chokecherries", ASHRecipeCategory.Food, "Dry berries by the fire so they keep.", I("DriedChokecherries"), 2, 5f, ASHToolType.None, ASHCraftStation.Campfire, G("ASH_Item_Chokecherries", 3)));
            list.Add(R("BoilWater", "Boil Water", ASHRecipeCategory.Food, "Boil a tin of creek water at the fire.", I("BoiledWater"), 1, 4f, ASHToolType.None, ASHCraftStation.Campfire, G("TinOfWater", 1)));
            list.Add(R("ImprovisedKnife", "Improvised Knife", ASHRecipeCategory.Tools, "Grind scrap to an edge against a stone and wrap the grip.", I("ImprovisedKnife"), 1, 6f, ASHToolType.None, ASHCraftStation.Anywhere, G("ScrapMetal", 2), G("Cloth", 1), G("ASH_Item_Stone", 1)));
            list.Add(R("ClothFromShirt", "Cut Cloth Strips", ASHRecipeCategory.Materials, "Cut an old shirt into clean strips.", I("Cloth"), 3, 3f, ASHToolType.Knife, ASHCraftStation.Anywhere, G("OldShirt", 1)));
            list.Add(R("FlattenTins", "Flatten Tins", ASHRecipeCategory.Materials, "Hammer empty tins flat for scrap.", I("ScrapMetal"), 1, 2f, ASHToolType.None, ASHCraftStation.Anywhere, G("EmptyTin", 2)));
            list.AddRange(WoodRecipes());
            list.AddRange(PackRecipes());
            return list;
        }

        static ASHRecipe R(string file, string name, ASHRecipeCategory cat, string desc, ASHItemDefinition output, int amount, float secs, ASHToolType tool, ASHCraftStation station, params ASHRecipeIngredient[] ing)
        {
            var r = ASHBuildUtil.Asset<ASHRecipe>($"{RecipeDir}/ASH_Recipe_{file}.asset");
            r.recipeId = file.ToLowerInvariant(); r.displayName = name; r.category = cat; r.description = desc; r.outputItem = output; r.outputAmount = amount;
            r.craftSeconds = secs; r.requiredTool = tool; r.station = station; r.ingredients = new List<ASHRecipeIngredient>(ing);
            foreach (var i in ing) if (i.item == null) Debug.LogWarning($"[ASH] Recipe {file}: missing ingredient item");
            EditorUtility.SetDirty(r); return r;
        }

        // ------------------------------------------------------------------ prefabs
        static void PickupPrefab(Def d, ASHItemDefinition it)
        {
            var root = new GameObject("ASH_Pickup_" + d.file);
            var mat = ASHBuildUtil.Lit($"{Mat}/ASH_M_Pickup_{d.file}.mat", d.tint, 0.25f);
            var type = d.shape == Shape.Cyl ? PrimitiveType.Cylinder : d.shape == Shape.Ball ? PrimitiveType.Sphere : d.shape == Shape.Capsule ? PrimitiveType.Capsule : PrimitiveType.Cube;
            var s = d.size;
            Vector3 euler = d.shape == Shape.Capsule ? new Vector3(0, 0, 90) : Vector3.zero;
            float half = d.shape == Shape.Cyl ? s.y : d.shape == Shape.Capsule ? s.x * 0.5f : s.y * 0.5f;
            ASHBuildUtil.Prim(type, "Visual", root.transform, new Vector3(0, half, 0), euler, s, mat);
            if (d.file == "Hatchet") ASHBuildUtil.Prim(PrimitiveType.Cube, "Head", root.transform, new Vector3(0, 0.04f, 0.17f), Vector3.zero, new Vector3(0.16f, 0.05f, 0.07f), ASHBuildUtil.Lit($"{Mat}/ASH_M_Pickup_Steel.mat", new Color(0.4f, 0.41f, 0.42f), 0.5f, null, 0.7f));
            var col = root.AddComponent<BoxCollider>();
            float m = 0.12f; col.size = new Vector3(Mathf.Max(s.x, 0.25f) + m, Mathf.Max(s.y * 2f, 0.25f), Mathf.Max(s.z, 0.25f) + m); col.center = new Vector3(0, col.size.y * 0.5f, 0);
            col.isTrigger = true;
            root.AddComponent<ASHItemPickup>().Configure(it, 1);
            int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) ASHBuildUtil.SetLayerRecursive(root, layer);
            ASHBuildUtil.SavePrefab(root, $"{PickupDir}/ASH_Pickup_{d.file}.prefab");
        }

        public static GameObject LoadPickup(string file) => AssetDatabase.LoadAssetAtPath<GameObject>($"{PickupDir}/ASH_Pickup_{file}.prefab");

        static void BuildSalvageNodes()
        {
            var wood = ASHBuildUtil.Lit(ASHContentBuilder.MatDir + "/ASH_M_CrateWood.mat", new Color(0.5f, 0.4f, 0.28f), 0.15f);
            var dark = ASHBuildUtil.Lit(ASHContentBuilder.MatDir + "/ASH_M_ChairWood.mat", new Color(0.32f, 0.22f, 0.15f), 0.2f);
            var grass = ASHBuildUtil.Lit(ASHContentBuilder.MatDir + "/ASH_M_FiberGrass.mat", new Color(0.62f, 0.58f, 0.34f), 0.1f);

            var crate = new GameObject("ASH_SalvageCrate"); var v = new GameObject("Visual").transform; v.SetParent(crate.transform, false);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "Box", v, new Vector3(0, 0.3f, 0), new Vector3(0, 12, 0), new Vector3(0.9f, 0.6f, 0.6f), wood);
            for (int i = 0; i < 3; i++) ASHBuildUtil.Prim(PrimitiveType.Cube, "Slat" + i, v, new Vector3(0, 0.12f + i * 0.2f, 0.31f), new Vector3(0, 12, 0), new Vector3(0.92f, 0.05f, 0.02f), dark);
            var c1 = crate.AddComponent<BoxCollider>(); c1.center = new Vector3(0, 0.3f, 0); c1.size = new Vector3(1f, 0.6f, 0.75f);
            Node(crate, v, I("ReclaimedWood"), "Break down", 2, 3, 1, 2.5f, ASHToolType.Axe, ASHToolType.None, I("ScrapMetal"), 0.5f);
            ASHBuildUtil.SavePrefab(crate, ASHContentBuilder.WorldPrefabs + "/ASH_SalvageCrate.prefab");

            var chair = new GameObject("ASH_BrokenChair"); v = new GameObject("Visual").transform; v.SetParent(chair.transform, false);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "Seat", v, new Vector3(0, 0.25f, 0), new Vector3(8, 25, 70), new Vector3(0.45f, 0.05f, 0.45f), dark);
            ASHBuildUtil.Prim(PrimitiveType.Cube, "Back", v, new Vector3(-0.15f, 0.12f, 0.1f), new Vector3(0, 25, 5), new Vector3(0.45f, 0.05f, 0.5f), dark);
            for (int i = 0; i < 3; i++) ASHBuildUtil.Prim(PrimitiveType.Cylinder, "Leg" + i, v, new Vector3(0.25f - i * 0.2f, 0.05f, -0.25f + i * 0.18f), new Vector3(90, i * 50, 0), new Vector3(0.04f, 0.22f, 0.04f), dark);
            var c2 = chair.AddComponent<BoxCollider>(); c2.center = new Vector3(0, 0.25f, 0); c2.size = new Vector3(0.9f, 0.5f, 0.9f);
            Node(chair, v, I("ReclaimedWood"), "Break down", 1, 2, 1, 2f, ASHToolType.Axe, ASHToolType.None, null, 0f);
            ASHBuildUtil.SavePrefab(chair, ASHContentBuilder.WorldPrefabs + "/ASH_BrokenChair.prefab");

            var fg = new GameObject("ASH_FiberGrass"); v = new GameObject("Visual").transform; v.SetParent(fg.transform, false);
            var rnd = new System.Random(3);
            for (int i = 0; i < 9; i++)
            {
                float a = (float)rnd.NextDouble() * 6.28f, r = (float)rnd.NextDouble() * 0.3f;
                ASHBuildUtil.Prim(PrimitiveType.Cube, "Blade" + i, v, new Vector3(Mathf.Cos(a) * r, 0.3f, Mathf.Sin(a) * r), new Vector3((float)rnd.NextDouble() * 30 - 15, (float)rnd.NextDouble() * 180, (float)rnd.NextDouble() * 30 - 15), new Vector3(0.03f, 0.6f + (float)rnd.NextDouble() * 0.3f, 0.08f), grass);
            }
            var c3 = fg.AddComponent<CapsuleCollider>(); c3.center = new Vector3(0, 0.4f, 0); c3.radius = 0.4f; c3.height = 0.8f;
            Node(fg, v, I("PlantFiber"), "Pull fiber", 1, 2, 2, 1.2f, ASHToolType.None, ASHToolType.Knife, null, 0f);
            ASHBuildUtil.SavePrefab(fg, ASHContentBuilder.WorldPrefabs + "/ASH_FiberGrass.prefab");
        }

        static void Node(GameObject root, Transform visual, ASHItemDefinition item, string verb, int min, int max, int gathers, float hold, ASHToolType req, ASHToolType bonus, ASHItemDefinition extra, float chance)
        {
            int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) ASHBuildUtil.SetLayerRecursive(root, layer);
            var n = root.AddComponent<ASHResourceNode>();
            n.Configure(item, verb, min, max, gathers, hold);
            n.ConfigureTools(req, bonus, 2f, 0.5f, extra, chance);
            ASHBuildUtil.Set(n, "visualRoot", visual.gameObject);
        }

        static void HookExistingPrefabs()
        {
            EditPrefab(ASHContentBuilder.WorldPrefabs + "/ASH_Deadwood.prefab", go =>
            { var n = go.GetComponent<ASHResourceNode>(); if (n) { n.ConfigureTools(ASHToolType.None, ASHToolType.Axe, 2f, 0.5f, null, 0f); EditorUtility.SetDirty(n); } });
            EditPrefab(ASHContentBuilder.WorldPrefabs + "/ASH_StockTank.prefab", go =>
            { var w = go.GetComponentInChildren<ASHWaterSource>(); if (w) { w.ConfigureFill(I("EmptyTin"), I("TinOfWater")); EditorUtility.SetDirty(w); } });
            // scene instances with overrides
            foreach (var w in Object.FindObjectsByType<ASHWaterSource>(FindObjectsInactive.Include)) { w.ConfigureFill(I("EmptyTin"), I("TinOfWater")); EditorUtility.SetDirty(w); }
        }

        static void EditPrefab(string path, System.Action<GameObject> edit)
        {
            if (!File.Exists(path)) { Debug.LogWarning("[ASH] missing prefab " + path); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); } finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void WirePlayer(List<ASHRecipe> recipes)
        {
            var arr = recipes.ToArray();
            EditPrefab(ASHPlayerBuilder.PrefabPath, go => Wire(go, arr));
            var p = GameObject.Find("ASH_Player"); if (p) { Wire(p, arr); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(p.scene); }
        }

        static void Wire(GameObject go, ASHRecipe[] recipes)
        {
            var craft = go.GetComponent<ASHCrafting>(); if (craft) ASHBuildUtil.SetList(craft, "recipes", recipes);
            var inv = go.GetComponent<ASHInventory>();
            if (inv)
            {
                var so = new SerializedObject(inv); var p = so.FindProperty("startingItems");
                p.arraySize = 1; var e = p.GetArrayElementAtIndex(0);
                e.FindPropertyRelative("item").objectReferenceValue = I("DamagedGPS"); e.FindPropertyRelative("amount").intValue = 1;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
