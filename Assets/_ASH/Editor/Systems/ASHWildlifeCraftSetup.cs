#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using ASH.EditorTools.Build;
using ASH.EditorTools.Systems;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Idempotent animal-drop recipes for the journal. Reuses existing items (rawmeat, cookedmeat, hides, bones, fat, deer_skin).
    /// Never changes the five Survival Guide starter recipes (Wet Rag, Campfire, Split Kindling, Bandage, Boil Water).
    /// Wearables use Character tab slots. Unlocks are first pickup, page 4, or a parent craft.
    /// </summary>
    public static class ASHWildlifeCraftSetup
    {
        const string ItemDir = "Assets/_ASH/Data/Items";
        const string RecipeDir = "Assets/_ASH/Data/Recipes";
        const string LetterDir = "Assets/_ASH/Art/Icons Placeholder";
        const string ManualPath = "Assets/_ASH/Resources/ASH_FieldManual.asset";
        static readonly HashSet<string> StarterIds = new HashSet<string> { "wet_rag", "campfire", "campfirefirewood", "splitkindling", "bandage", "boilwater" };

        [MenuItem("ASH/Systems/Wildlife/Setup Animal Crafting (idempotent)", false, 264)]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }

            var bone = I("animalbone");
            var fat = I("animalfat");
            var sinew = I("sinew") ?? NewItem("sinew", "Sinew", "Tough connective tissue. Thread, bowstrings, bindings.", "SW", ASHItemCategory.Resource, 20, 2);
            var antler = I("antler") ?? NewItem("antler", "Antler", "A harvested deer antler. Handles and knives.", "AN", ASHItemCategory.Resource, 10, 6);
            var pelt = I("wolfpelt");
            var hide = I("bearhide");
            var deer = I("deer_skin");
            var fox = I("foxpelt");
            var meat = I("rawmeat");
            var cooked = I("cookedmeat");
            var cloth = I("cloth");
            var rope = I("rope");
            var plant = I("plantfiber");
            var wood = I("deadwood");
            var arrows = I("woodenarrow");
            var bowl = I("emptytin");
            var cherries = I("chokecherries");
            var water = I("boiledwater") ?? I("bottledwater");
            var tooth = I("wolftooth");
            var tusk = I("boartusk");
            var claw = I("predatorclaw");

            var strips = NewItem("hidestrips", "Hide Strips", "Cut leather strips. Sewing, bindings, straps.", "HS", ASHItemCategory.Resource, 20, 3);
            var tallow = NewItem("tallow", "Tallow", "Rendered fat. Candles, waterproofing, cooking fat.", "TA", ASHItemCategory.Resource, 20, 3);
            var candle = NewItem("tallowcandle", "Tallow Candle", "A slow, smoky candle of rendered fat and a fiber wick.", "TC", ASHItemCategory.Light, 10, 4);
            candle.fuelSecondsPerUnit = 360f; candle.fuelCapacityUnits = 1; candle.useVerb = "Light"; Dirty(candle);
            var proof = NewItem("waterproofing", "Waterproofing", "A tin of tallow rub. Seals boots, cloaks and tents.", "WW", ASHItemCategory.Resource, 10, 4);
            var needle = NewItem("boneneedle", "Bone Needle", "A needle carved from bone. Needed to sew hides.", "BN", ASHItemCategory.Tool, 10, 3);
            var boneknife = NewItem("boneknife", "Bone Knife", "A sharp splinter of bone bound to a stick. Harvests and cuts.", "BK", ASHItemCategory.Tool, 1, 6);
            boneknife.toolType = ASHToolType.Knife; boneknife.equipSlot = ASHEquipSlot.MainHand; boneknife.useVerb = "Equip"; Dirty(boneknife);
            var heads = NewItem("bonearrowhead", "Bone Arrowheads", "Carved points for wooden shafts.", "BA", ASHItemCategory.Resource, 20, 2);
            var hooks = NewItem("fishhook", "Bone Fishhooks", "Small barbed hooks carved from bone.", "FH", ASHItemCategory.Resource, 20, 3);
            var antlerknife = NewItem("antlerknife", "Antler Knife", "A knife with a deer-antler handle and a bone blade.", "AK", ASHItemCategory.Tool, 1, 10);
            antlerknife.toolType = ASHToolType.Knife; antlerknife.equipSlot = ASHEquipSlot.MainHand; antlerknife.useVerb = "Equip"; Dirty(antlerknife);
            var thread = NewItem("sinewthread", "Sinew Thread", "Twisted sinew for sewing.", "ST", ASHItemCategory.Resource, 20, 2);
            var bowstring = NewItem("bowstring", "Bowstring", "A stout sinew string for a bow.", "BS", ASHItemCategory.Resource, 10, 4);
            var jerky = NewItem("jerky", "Jerky", "Dried strips of meat. Keeps for a long time and does not spoil.", "JK", ASHItemCategory.Consumable, 20, 5);
            jerky.restoreHunger = 18f; jerky.sicknessChance = 0f; jerky.useVerb = "Eat"; Dirty(jerky);
            var smoked = NewItem("smokedmeat", "Smoked Meat", "Meat slow-smoked over a fire. Safer and richer than a quick roast.", "SM", ASHItemCategory.Consumable, 20, 6);
            smoked.restoreHunger = 28f; smoked.sicknessChance = 0f; smoked.useVerb = "Eat"; Dirty(smoked);
            var stew = NewItem("hunterstew", "Hunter's Stew", "Meat and berries simmered in a tin. Fills hunger and thirst.", "HJ", ASHItemCategory.Consumable, 10, 8);
            stew.restoreHunger = 32f; stew.restoreThirst = 12f; stew.sicknessChance = 0f; stew.useVerb = "Eat"; stew.leftoverItem = bowl; Dirty(stew);
            var hat = Wear("furhat", "Fur Hat", "A warm cap lined with rabbit or fox fur.", "FT", ASHWearSlot.Head, 5);
            var gloves = Wear("furgloves", "Fur Gloves", "Hide mittens lined with fur.", "FG", ASHWearSlot.Hands, 5);
            var coat = Wear("furcoat", "Fur Coat", "A heavy hide coat. Outerwear against the ash and cold.", "FC", ASHWearSlot.Outerwear, 14);
            var pants = Wear("furpants", "Fur Pants", "Hide trousers lined with fur.", "FU", ASHWearSlot.Legs, 8);
            var bedroll = NewItem("bedroll", "Bedroll", "A hide and cloth roll. Pitch it for a rough night's sleep.", "BR", ASHItemCategory.Placeable, 1, 10);
            bedroll.useVerb = "Place"; Dirty(bedroll);
            var skin = NewItem("waterskin", "Waterskin", "A sewn hide flask. Fill it with boiled or bottled water.", "WS", ASHItemCategory.Consumable, 5, 6);
            skin.restoreThirst = 40f; skin.useVerb = "Drink"; Dirty(skin);
            var emptySkin = NewItem("emptywaterskin", "Empty Waterskin", "A sewn hide flask, waiting to be filled.", "EW", ASHItemCategory.Resource, 5, 4);
            skin.leftoverItem = emptySkin; Dirty(skin);
            var pack = Wear("hidebackpack", "Hide Backpack", "A stitched hide pack. Worn on the Character tab.", "HB", ASHWearSlot.Backpack, 12);
            var charm = Wear("wolfcharm", "Wolf-Tooth Charm", "A canine on a sinew cord. A trophy that sells well.", "WC", ASHWearSlot.Accessory, 12);
            charm.category = ASHItemCategory.Salvage; Dirty(charm);
            var tusktr = Wear("tusktrophy", "Tusk Trophy", "A boar tusk on a cord. Sells well in town.", "TT", ASHWearSlot.Accessory, 14);
            tusktr.category = ASHItemCategory.Salvage; Dirty(tusktr);
            var clawtr = Wear("clawcharm", "Claw Charm", "A hooked predator claw, tied off as a keepsake.", "CC", ASHWearSlot.Accessory, 16);
            clawtr.category = ASHItemCategory.Salvage; Dirty(clawtr);

            var recipes = new List<ASHRecipe>();
            // materials
            recipes.Add(Rec("hidestrips", "Hide Strips", "Cut a hide into sewing strips.", ASHRecipeCategory.Materials, strips, 3, 1.5f, ASHCraftStation.Anywhere, 0, First(pelt, hide, deer, fox), 5, 0, Ing(deer ?? pelt ?? hide, 1), ToolKnife()));
            recipes.Add(Rec("tallow", "Render Tallow", "Melt animal fat down over a fire.", ASHRecipeCategory.Materials, tallow, 1, 2f, ASHCraftStation.Campfire, 20, First(fat), 5, 2, Ing(fat, 1)));
            recipes.Add(Rec("sinewthread", "Sinew Thread", "Twist sinew into sewing thread.", ASHRecipeCategory.Materials, thread, 2, 1.2f, ASHCraftStation.Anywhere, 0, First(sinew), 6, 1, Ing(sinew, 1)));
            recipes.Add(Rec("bowstring", "Bowstring", "A stout sinew string for a bow.", ASHRecipeCategory.Tools, bowstring, 1, 2f, ASHCraftStation.Anywhere, 0, First(sinew), 6, 3, Ing(sinew, 2)));
            recipes.Add(Rec("boneneedle", "Bone Needle", "Carve a needle from a splinter of bone.", ASHRecipeCategory.Tools, needle, 1, 1.5f, ASHCraftStation.Anywhere, 0, First(bone), 6, 4, Ing(bone, 1)));
            recipes.Add(Rec("bonearrowhead", "Bone Arrowheads", "Chip points from bone.", ASHRecipeCategory.Tools, heads, 4, 2f, ASHCraftStation.Anywhere, 0, First(bone), 6, 5, Ing(bone, 1)));
            recipes.Add(Rec("fishhook", "Bone Fishhooks", "Carve small barbed hooks.", ASHRecipeCategory.Tools, hooks, 3, 2f, ASHCraftStation.Anywhere, 0, First(bone), 6, 6, Ing(bone, 1)));
            recipes.Add(Rec("boneknife", "Bone Knife", "Lash a bone blade to a deadwood handle.", ASHRecipeCategory.Tools, boneknife, 1, 3f, ASHCraftStation.Anywhere, 0, First(bone), 6, 7, Ing(bone, 2), Ing(wood, 1), Ing(sinew, 1)));
            recipes.Add(Rec("antlerknife", "Antler Knife", "Haft a bone blade on an antler handle.", ASHRecipeCategory.Tools, antlerknife, 1, 3.5f, ASHCraftStation.Anywhere, 0, First(antler), 6, 8, Ing(antler, 1), Ing(bone, 1), Ing(sinew, 1)));
            recipes.Add(Rec("bonearrows", "Bone-Tipped Arrows", "Fit bone points to wooden shafts.", ASHRecipeCategory.Tools, arrows, 5, 2.5f, ASHCraftStation.Anywhere, 0, First(heads), 7, 5, Ing(heads, 1), Ing(wood, 2), Ing(sinew, 1)));
            // clothing
            recipes.Add(Rec("furhat", "Fur Hat", "Sew a lined cap from hide strips and fur.", ASHRecipeCategory.Materials, hat, 1, 4f, ASHCraftStation.Anywhere, 0, Crafted(strips), 5, 3, Ing(strips, 2), Ing(fox ?? I("rabbitfur") ?? pelt, 1), Ing(thread, 1)));
            recipes.Add(Rec("furgloves", "Fur Gloves", "Hide mittens lined with fur.", ASHRecipeCategory.Materials, gloves, 1, 4f, ASHCraftStation.Anywhere, 0, Crafted(strips), 5, 4, Ing(strips, 2), Ing(I("rabbitfur") ?? fox ?? pelt, 1), Ing(thread, 1)));
            recipes.Add(Rec("furcoat", "Fur Coat", "A heavy coat of hide and fur.", ASHRecipeCategory.Materials, coat, 1, 6f, ASHCraftStation.Anywhere, 0, Page4(), 5, 5, Ing(hide ?? pelt ?? deer, 2), Ing(strips, 3), Ing(thread, 2), Ing(needle, 1)));
            recipes.Add(Rec("furpants", "Fur Pants", "Hide trousers lined with fur.", ASHRecipeCategory.Materials, pants, 1, 5f, ASHCraftStation.Anywhere, 0, Crafted(strips), 5, 6, Ing(deer ?? pelt ?? hide, 1), Ing(strips, 2), Ing(thread, 1)));
            recipes.Add(Rec("bedroll", "Bedroll", "A hide and cloth roll for a night on the ground.", ASHRecipeCategory.Materials, bedroll, 1, 5f, ASHCraftStation.Anywhere, 0, Page4(), 5, 7, Ing(hide ?? deer ?? pelt, 1), Ing(cloth ?? plant, 2), Ing(strips, 2)));
            recipes.Add(Rec("waterskin", "Waterskin", "Sew a hide flask and bind the neck with sinew.", ASHRecipeCategory.Materials, emptySkin, 1, 3f, ASHCraftStation.Anywhere, 0, First(deer, pelt, hide), 5, 8, Ing(deer ?? pelt ?? hide, 1), Ing(sinew, 1)));
            recipes.Add(Rec("fillwaterskin", "Fill Waterskin", "Pour boiled or bottled water into the skin.", ASHRecipeCategory.Food, skin, 1, 0.8f, ASHCraftStation.Anywhere, 0, First(emptySkin), 5, 9, Ing(emptySkin, 1), Ing(water, 1)));
            recipes.Add(Rec("hidebackpack", "Hide Backpack", "A stitched hide pack, worn on the Character tab.", ASHRecipeCategory.Materials, pack, 1, 6f, ASHCraftStation.Anywhere, 0, Crafted(strips), 5, 10, Ing(hide ?? deer ?? pelt, 2), Ing(strips, 3), Ing(rope ?? sinew, 1), Ing(needle, 1)));
            // fat / light
            recipes.Add(Rec("tallowcandle", "Tallow Candle", "A fiber wick in a cup of tallow.", ASHRecipeCategory.Fire, candle, 1, 2f, ASHCraftStation.Campfire, 10, Crafted(tallow), 7, 1, Ing(tallow, 1), Ing(plant ?? cloth, 1)));
            recipes.Add(Rec("waterproofing", "Waterproofing", "Rub tallow into a tin paste for boots and cloaks.", ASHRecipeCategory.Materials, proof, 1, 1.5f, ASHCraftStation.Anywhere, 0, Crafted(tallow), 7, 2, Ing(tallow, 2), Ing(bowl, 1)));
            // food — leave cookmeat alone; these sit beside it
            recipes.Add(Rec("jerky", "Jerky", "Dry thin strips of meat over a low fire.", ASHRecipeCategory.Food, jerky, 2, 0f, ASHCraftStation.Campfire, 40, First(meat), 4, 8, Ing(meat, 1)));
            recipes.Add(Rec("smokedmeat", "Smoked Meat", "Hang meat in the smoke of a fire.", ASHRecipeCategory.Food, smoked, 1, 0f, ASHCraftStation.Campfire, 50, First(meat), 4, 9, Ing(meat, 1), Ing(wood ?? I("firewood"), 1)));
            recipes.Add(Rec("hunterstew", "Hunter's Stew", "Simmer meat and berries in a tin.", ASHRecipeCategory.Food, stew, 1, 0f, ASHCraftStation.Campfire, 30, First(cooked, meat), 4, 10, Ing(cooked ?? meat, 1), Ing(cherries, 1), Ing(bowl, 1)));
            // trophies
            recipes.Add(Rec("wolfcharm", "Wolf-Tooth Charm", "Tie a wolf tooth on a sinew cord.", ASHRecipeCategory.Materials, charm, 1, 1.5f, ASHCraftStation.Anywhere, 0, First(tooth), 7, 7, Ing(tooth, 1), Ing(sinew, 1)));
            recipes.Add(Rec("tusktrophy", "Tusk Trophy", "Lash a boar tusk to a cord.", ASHRecipeCategory.Materials, tusktr, 1, 1.5f, ASHCraftStation.Anywhere, 0, First(tusk), 7, 8, Ing(tusk, 1), Ing(sinew, 1)));
            recipes.Add(Rec("clawcharm", "Claw Charm", "Tie off a predator claw as a keepsake.", ASHRecipeCategory.Materials, clawtr, 1, 1.5f, ASHCraftStation.Anywhere, 0, First(claw), 7, 9, Ing(claw, 1), Ing(sinew, 1)));

            var cleaned = new List<ASHRecipe>();
            foreach (var r in recipes) if (r) cleaned.Add(r);
            WireManual(cleaned);
            ASHItemCatalog.AppendRecipesToPlayer(cleaned);
            ASHMenuInstaller.RefreshRegistry();
            AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Animal crafting: " + cleaned.Count + " recipes wired into the journal. Starter recipes left unchanged.");
        }

        static ASHItemDefinition I(string id)
        {
            foreach (var g in AssetDatabase.FindAssets("t:ASHItemDefinition"))
            {
                var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g));
                if (it && it.itemId == id) return it;
            }
            return null;
        }

        static ASHItemDefinition NewItem(string id, string name, string desc, string letters, ASHItemCategory cat, int stack, int sell)
        {
            var existing = I(id);
            var icon = ASHWildlifeSetup.EnsureLetter(LetterDir + "/ASH_Letter_" + letters + ".png", letters);
            if (existing)
            {
                if (icon && !existing.icon) { existing.icon = icon; EditorUtility.SetDirty(existing); }
                return existing;
            }
            ASHMenuInstaller.Folder(ItemDir);
            var it = ScriptableObject.CreateInstance<ASHItemDefinition>();
            it.itemId = id; it.displayName = name; it.description = desc; it.icon = icon;
            it.category = cat; it.maxStack = stack; it.sellValue = sell; it.useVerb = "Use";
            AssetDatabase.CreateAsset(it, ItemDir + "/ASH_Item_" + ToAsset(id) + ".asset");
            return it;
        }

        static ASHItemDefinition Wear(string id, string name, string desc, string letters, ASHWearSlot slot, int sell)
        {
            var it = NewItem(id, name, desc, letters, ASHItemCategory.Tool, 1, sell);
            it.wearSlot = slot;
            it.useVerb = "Wear";
            EditorUtility.SetDirty(it);
            return it;
        }

        static void Dirty(ASHItemDefinition it) { if (it) EditorUtility.SetDirty(it); }

        static ASHRecipeIngredient Ing(ASHItemDefinition item, int n)
        {
            return new ASHRecipeIngredient { item = item, amount = Mathf.Max(1, n) };
        }

        static ASHUnlockCondition[] First(params ASHItemDefinition[] items)
        {
            var list = new List<ASHUnlockCondition>();
            foreach (var it in items) if (it) list.Add(new ASHUnlockCondition { kind = ASHUnlockKind.FirstPickup, item = it });
            return list.ToArray();
        }

        static ASHUnlockCondition[] Crafted(ASHItemDefinition item)
        {
            if (!item) return new ASHUnlockCondition[0];
            return new[] { new ASHUnlockCondition { kind = ASHUnlockKind.Crafted, item = item, quantity = 1 } };
        }

        static ASHUnlockCondition[] Page4()
        {
            return new[] { new ASHUnlockCondition { kind = ASHUnlockKind.ManualPage, page = 4 } };
        }

        static ASHRecipeIngredient ToolKnife() => null; // knife is a requiredTool on the recipe, not an ingredient

        static ASHRecipe Rec(string id, string name, string desc, ASHRecipeCategory cat, ASHItemDefinition output, int amount, float seconds, ASHCraftStation station, float cookMin, ASHUnlockCondition[] unlock, int col, int row, params ASHRecipeIngredient[] ings)
        {
            if (!output) return null;
            var valid = new List<ASHRecipeIngredient>();
            foreach (var x in ings) if (x != null && x.item) valid.Add(x);
            if (valid.Count == 0) return null;
            ASHMenuInstaller.Folder(RecipeDir);
            string path = RecipeDir + "/ASH_Recipe_" + ToAsset(id) + ".asset";
            var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(path);
            if (!r)
            {
                r = ScriptableObject.CreateInstance<ASHRecipe>();
                AssetDatabase.CreateAsset(r, path);
            }
            if (StarterIds.Contains(r.recipeId)) return r; // never rewrite a starter
            r.recipeId = id;
            r.displayName = name;
            r.description = desc;
            r.category = cat;
            r.outputItem = output;
            r.outputAmount = Mathf.Max(1, amount);
            if (!r.icon && output && output.icon) r.icon = output.icon;
            r.craftSeconds = seconds;
            r.requiredCraftingLevel = 1;
            r.station = station;
            r.cookGameMinutes = cookMin;
            r.cookSeconds = cookMin > 0f ? 8f : 0f;
            r.ingredients = valid;
            if (id == "hidestrips") r.requiredTool = ASHToolType.Knife;
            EditorUtility.SetDirty(r);
            // stash unlock + layout in a sidecar via a static map consumed by WireManual
            Pending[r] = new Meta { unlock = unlock ?? new ASHUnlockCondition[0], col = col, row = row, all = unlock != null && unlock.Length == 1 };
            return r;
        }

        sealed class Meta { public ASHUnlockCondition[] unlock; public int col, row; public bool all; }
        static readonly Dictionary<ASHRecipe, Meta> Pending = new Dictionary<ASHRecipe, Meta>();

        static void WireManual(List<ASHRecipe> recipes)
        {
            var d = AssetDatabase.LoadAssetAtPath<ASHFieldManualData>(ManualPath);
            if (!d) { Debug.LogWarning("[ASH] Field Manual asset missing; recipes were created but not wired."); return; }
            foreach (var r in recipes)
            {
                if (!r || StarterIds.Contains(r.recipeId)) continue;
                var e = d.Find(r);
                Pending.TryGetValue(r, out var m);
                if (e == null)
                {
                    e = new ASHFieldManualEntry
                    {
                        recipe = r,
                        basic = false,
                        requireAll = m == null || m.all,
                        column = m != null ? m.col : 8,
                        row = m != null ? m.row : 0,
                        hint = "Gather the right animal material, or find the hunting notes (page 4)."
                    };
                    d.entries.Add(e);
                }
                e.basic = false;
                if (m != null && m.unlock != null && m.unlock.Length > 0)
                {
                    if (e.conditions == null) e.conditions = new List<ASHUnlockCondition>();
                    if (e.conditions.Count == 0)
                    {
                        foreach (var c in m.unlock) if (c != null) e.conditions.Add(c);
                        e.requireAll = m.unlock.Length <= 1;
                    }
                }
            }
            Pending.Clear();
            EditorUtility.SetDirty(d);
        }

        static string ToAsset(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Item";
            var parts = id.Replace('-', '_').Split('_');
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts) if (p.Length > 0) sb.Append(char.ToUpperInvariant(p[0])).Append(p.Length > 1 ? p.Substring(1) : "");
            return sb.ToString();
        }
    }
}
#endif
