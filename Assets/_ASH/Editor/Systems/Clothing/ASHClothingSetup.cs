#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using ASH.EditorTools.Systems;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// ASH/Systems/Clothing/Setup Clothing (idempotent). Clothing plan C1+C2:
    /// - Resources/ASH_ClothingSettings (sets, caps, rarity colours, crate clothing, recipes, 200 AC start);
    /// - stats / rarity / set on the 11 existing wearables + Old Pocket Watch (only while their stats are still empty);
    ///   Old Flannel Shirt becomes salvage (not wearable);
    /// - 25 new clothing items (created once; never rewritten), icons from ASH_ICON_FINAL/Clothing (rarity letter placeholder until a sprite exists);
    /// - clothing recipes + Survival Guide entries (columns 9-12), added to the player at runtime (no prefab / scene edits);
    /// - a value (AC) on every item still at 0 and a breakdown on every likely item that has none (existing materials only).
    /// Saves only the assets it touched (SaveAssetIfDirty), never the scene. Re-running changes nothing that is already set.
    /// "Re-apply Clothing Stats" force-writes the plan's stats / values onto all clothing pieces.
    /// </summary>
    public static class ASHClothingSetup
    {
        const string ItemDir = "Assets/_ASH/Data/Items";
        const string RecipeDir = "Assets/_ASH/Data/Recipes";
        const string SettingsPath = "Assets/_ASH/Resources/ASH_ClothingSettings.asset";
        const string ManualPath = "Assets/_ASH/Resources/ASH_FieldManual.asset";
        const string RegistryPath = "Assets/_ASH/Resources/ASH_ItemRegistry.asset";
        const string PlaceholderDir = "Assets/_ASH/Art/Icons Placeholder/Clothing";
        /// <summary>Icon folders searched for ASH_Icon_&lt;Name&gt;.png (first match wins).</summary>
        internal static readonly string[] IconDirs = { "Assets/_ASH/Art/ASH_ICON_FINAL/Clothing", "Assets/_ASH/Art/ASH_ICON_FINAL/Inventory", "Assets/_ASH/Art/ASH_ICON_FINAL/NewIcons" };
        /// <summary>Items this tool never touches (pre-existing local edits).</summary>
        static readonly HashSet<string> Hands = new HashSet<string> { "builttent", "packedtent", "grandpas_journal", "grandpas_key" };

        const ASHItemRarity C = ASHItemRarity.Common, U = ASHItemRarity.Uncommon, R = ASHItemRarity.Rare, L = ASHItemRarity.Legendary;

        internal sealed class Piece
        {
            public string id, icon, name, letters, desc, set = ""; public ASHWearSlot slot; public ASHItemRarity rarity; public int value;
            public float cold, ash, hp, st, hpDrain = 1f, stDrain = 1f, hunger = 1f, thirst = 1f; public int carry; public bool waterproof, isNew, crate;
        }

        static Piece P(string id, string icon, string name, string letters, ASHWearSlot slot, ASHItemRarity r, int value, float cold, float ash, string desc, bool isNew = true, bool crate = false)
            => new Piece { id = id, icon = icon, name = name, letters = letters, slot = slot, rarity = r, value = value, cold = cold, ash = ash, desc = desc, isNew = isNew, crate = crate };

        /// <summary>The 37 pieces of the clothing plan (rag + mask keep their ASHFaceProtection path; they only get rarity / value here).</summary>
        internal static List<Piece> Pieces()
        {
            var H = ASHWearSlot.Head; var F = ASHWearSlot.Face; var T = ASHWearSlot.Torso; var O = ASHWearSlot.Outerwear; var Hn = ASHWearSlot.Hands;
            var Lg = ASHWearSlot.Legs; var Ft = ASHWearSlot.Feet; var B = ASHWearSlot.Backpack; var A = ASHWearSlot.Accessory;
            var l = new List<Piece>
            {
                // head
                P("watchcap", "WatchCap", "Watch Cap", "WC", H, C, 10, 8, 0, "A knit wool cap. Keeps the ears warm.", true, true),
                P("furhat", null, "Fur Hat", null, H, U, 18, 16, 0, null, false),
                P("ashhood", "AshHood", "Ash Hood", "AH", H, R, 35, 10, 16, "A cloth and hide hood that covers the head and neck against falling ash.", true, true),
                P("bearskinhood", "BearskinHood", "Bearskin Hood", "BH", H, L, 90, 26, 8, "A heavy hood of bear hide. Warm, tough and hard to come by.", true, false),
                // face
                P("wet_rag", null, "Wet Rag", null, F, C, 1, 0, 0, null, false),
                P("clothfacewrap", "ClothFaceWrap", "Cloth Face Wrap", "FW", F, C, 4, 4, 12, "A dry cloth wrap over nose and mouth. Weaker than a wet rag, but it never dries out.", true, false),
                P("dustgoggles", "DustGoggles", "Dust Goggles", "DG", F, U, 16, 0, 8, "Old shop goggles. Keeps ash out of the eyes.", true, true),
                P("gas_mask", null, "Gas Mask", null, F, R, 40, 0, 0, null, false),
                // torso
                P("woolundershirt", "WoolUndershirt", "Wool Undershirt", "WU", T, U, 18, 12, 0, "A thick wool base layer.", true, true),
                P("hidevest", "HideVest", "Hide Vest", "HV", T, U, 22, 12, 4, "A stitched hide vest.", true, false),
                P("quiltedliner", "QuiltedLiner", "Quilted Liner", "LN", T, R, 40, 18, 4, "An insulated quilted shirt. Rare and very warm.", true, true),
                // outerwear
                P("canvasworkcoat", "CanvasWorkCoat", "Canvas Work Coat", "CW", O, C, 14, 10, 6, "A heavy canvas chore coat.", true, true),
                P("furcoat", null, "Fur Coat", null, O, U, 30, 22, 8, null, false),
                P("waxedcloak", "WaxedCloak", "Waxed Cloak", "CL", O, U, 26, 8, 14, "Cloth rubbed with tallow wax. Sheds ash-rain.", true, false),
                P("wolfskinparka", "WolfskinParka", "Wolfskin Parka", "WP", O, R, 55, 30, 10, "A parka of wolf pelts.", true, false),
                P("expeditionparka", "ExpeditionParka", "Expedition Parka", "EP", O, L, 120, 36, 18, "A sealed expedition parka. The best coat in the valley.", true, true),
                // hands
                P("workgloves", "WorkGloves", "Work Gloves", "WG", Hn, C, 8, 6, 0, "Worn leather work gloves.", true, true),
                P("furgloves", null, "Fur Gloves", null, Hn, U, 16, 14, 0, null, false),
                P("hidegauntlets", "HideGauntlets", "Hide Gauntlets", "HG", Hn, U, 18, 12, 2, "Long hide gloves laced at the wrist.", true, false),
                P("rangergloves", "RangerGloves", "Ranger Gloves", "RG", Hn, R, 32, 16, 4, "Insulated ranger-issue gloves.", true, true),
                // legs
                P("workpants", "WorkPants", "Worn Work Pants", "WT", Lg, C, 8, 6, 0, "Patched canvas work pants.", true, true),
                P("furpants", null, "Fur Pants", null, Lg, U, 22, 16, 2, null, false),
                P("hidetrousers", "HideTrousers", "Hide Trousers", "HT", Lg, U, 22, 14, 4, "Stitched hide trousers.", true, false),
                P("wooltrousers", "WoolTrousers", "Wool Trousers", "WL", Lg, R, 34, 20, 2, "Thick wool trousers.", true, true),
                // feet
                P("workboots", "WorkBoots", "Worn Work Boots", "WB", Ft, C, 12, 8, 0, "Scuffed leather work boots.", true, true),
                P("hidewraps", "HideWraps", "Hide Wraps", "HW", Ft, U, 14, 10, 0, "Hide strips wrapped over cloth.", true, false),
                P("hideboots", "HideBoots", "Hide Boots", "HO", Ft, U, 22, 14, 2, "Stitched hide boots.", true, false),
                P("winterboots", "WinterBoots", "Winter Boots", "WN", Ft, R, 40, 20, 2, "Insulated winter boots.", true, true),
                // backpacks (4 / 8 / 12 extra slots)
                P("canvasrucksack", "CanvasRucksack", "Canvas Rucksack", "CR", B, C, 15, 0, 0, "A small canvas rucksack. +4 pack slots.", true, true),
                P("hidebackpack", null, "Hide Backpack", null, B, U, 30, 0, 0, null, false),
                P("scoutpack", "ScoutPack", "Scout Pack", "SP", B, R, 50, 0, 0, "A framed scout pack. +12 pack slots, a little more tiring to carry.", true, true),
                // accessories
                P("woolscarf", "WoolScarf", "Wool Scarf", "WS", A, C, 8, 8, 4, "A long wool scarf.", true, true),
                P("wolfcharm", null, "Wolf-Tooth Charm", null, A, U, 20, 0, 0, null, false),
                P("tusktrophy", null, "Tusk Trophy", null, A, U, 22, 0, 0, null, false),
                P("clawcharm", null, "Claw Charm", null, A, R, 30, 0, 0, null, false),
                P("pocketwatch", null, "Old Pocket Watch", null, A, R, 45, 0, 0, null, false),
            };
            void M(string id, System.Action<Piece> f) { foreach (var p in l) if (p.id == id) f(p); }
            M("ashhood", p => { p.stDrain = 0.97f; p.set = "Ash walker"; });
            M("bearskinhood", p => { p.hp = 8; p.hpDrain = 0.95f; });
            M("clothfacewrap", p => p.set = "Ash walker");
            M("dustgoggles", p => { p.stDrain = 0.98f; p.set = "Ash walker"; });
            M("woolundershirt", p => { p.hunger = 0.97f; p.hp = 5; });
            M("hidevest", p => { p.st = 5; p.set = "Hide"; });
            M("quiltedliner", p => { p.hp = 10; p.hpDrain = 0.96f; });
            M("furcoat", p => { p.hp = 5; p.set = "Fur"; });
            M("furhat", p => p.set = "Fur"); M("furgloves", p => p.set = "Fur"); M("furpants", p => p.set = "Fur");
            M("waxedcloak", p => { p.thirst = 0.97f; p.waterproof = true; p.set = "Ash walker"; });
            M("wolfskinparka", p => { p.stDrain = 0.94f; p.st = 8; });
            M("expeditionparka", p => { p.hp = 15; p.hpDrain = 0.90f; p.st = 10; p.waterproof = true; });
            M("hidegauntlets", p => { p.stDrain = 0.97f; p.set = "Hide"; });
            M("rangergloves", p => { p.stDrain = 0.95f; p.st = 5; });
            M("hidetrousers", p => { p.st = 5; p.set = "Hide"; });
            M("wooltrousers", p => { p.hunger = 0.97f; p.hp = 5; });
            M("hidewraps", p => { p.stDrain = 0.98f; p.set = "Hide"; });
            M("hideboots", p => p.set = "Hide");
            M("winterboots", p => { p.stDrain = 0.95f; p.st = 5; p.waterproof = true; });
            M("canvasrucksack", p => p.carry = 4);
            M("hidebackpack", p => { p.carry = 8; p.set = "Hide"; });
            M("scoutpack", p => { p.carry = 12; p.stDrain = 1.04f; });
            M("wolfcharm", p => { p.st = 5; p.stDrain = 0.96f; });
            M("tusktrophy", p => p.hp = 8);
            M("clawcharm", p => { p.hpDrain = 0.95f; p.hunger = 0.97f; });
            M("pocketwatch", p => { p.stDrain = 0.97f; p.thirst = 0.98f; });
            return l;
        }

        [MenuItem("ASH/Systems/Clothing/Setup Clothing (idempotent)", false, 270)]
        public static void Setup() => Run(false);

        [MenuItem("ASH/Systems/Clothing/Re-apply Clothing Stats (overwrite plan values)", false, 271)]
        public static void Reapply() => Run(true);

        [MenuItem("ASH/Systems/Clothing/Wire Clothing Icons", false, 272)]
        public static void WireIconsMenu()
        {
            var touched = new HashSet<Object>(); int n = WireIcons(touched); Save(touched);
            Debug.Log($"[ASH] Clothing icons: {n} assigned.");
        }

        static void Run(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Stop Play mode first."); return; }
            var touched = new HashSet<Object>();
            var S = EnsureSettings(touched);
            var pieces = Pieces();
            int created = 0, stats = 0;
            var crate = new List<ASHItemDefinition>();
            foreach (var p in pieces)
            {
                var it = I(p.id);
                if (!it && p.isNew) { it = Create(p); created++; }
                if (!it) { Debug.LogWarning($"[ASH] Clothing: item '{p.id}' not found (skipped)."); continue; }
                if (force || it.clothing == null || (it.clothing.IsEmpty && it.rarity == ASHItemRarity.Common && string.IsNullOrEmpty(it.clothingSet)) || IsFresh(it))
                {
                    if (Apply(it, p)) stats++;
                    touched.Add(it);
                }
                if (p.crate) crate.Add(it);
            }
            // Old Flannel Shirt: salvage only (t573u)
            var shirt = I("oldshirt");
            if (shirt && shirt.wearSlot != ASHWearSlot.None) { shirt.wearSlot = ASHWearSlot.None; EditorUtility.SetDirty(shirt); touched.Add(shirt); }

            if (S.crateClothing == null || S.crateClothing.Count == 0) { S.crateClothing = crate; EditorUtility.SetDirty(S); }

            int icons = WireIcons(touched);
            var recipes = Recipes(touched);
            if (force || S.recipes == null || S.recipes.Count == 0 || S.recipes.Exists(r => !r)) { S.recipes = recipes; EditorUtility.SetDirty(S); }
            else foreach (var r in recipes) if (r && !S.recipes.Contains(r)) { S.recipes.Add(r); EditorUtility.SetDirty(S); }
            WireManual(recipes, touched);
            int values = ValuesAndBreakdowns(touched, force);

            ASHMenuInstaller.RefreshRegistry();
            var reg = AssetDatabase.LoadAssetAtPath<Object>(RegistryPath); if (reg) touched.Add(reg);
            touched.Add(S);
            Save(touched);
            Debug.Log($"[ASH] Clothing setup: {created} new items, {stats} pieces with stats, {icons} icons wired, {recipes.Count} recipes, {values} value/breakdown fills. Saved {touched.Count} own assets (scene untouched).");
        }

        /// <summary>A piece created by this tool that still has no stats.</summary>
        static bool IsFresh(ASHItemDefinition it) => it.clothing != null && it.clothing.IsEmpty && it.rarity == ASHItemRarity.Common && it.IsWearable && it.clothing.carrySlots == 0 && string.IsNullOrEmpty(it.clothingSet);

        static bool Apply(ASHItemDefinition it, Piece p)
        {
            if (it.clothing == null) it.clothing = new ASHClothingMods();
            var m = it.clothing;
            m.coldResist = p.cold; m.ashResist = p.ash; m.bonusHealth = p.hp; m.bonusStamina = p.st;
            m.healthDrainMul = p.hpDrain; m.staminaDrainMul = p.stDrain; m.hungerMul = p.hunger; m.thirstMul = p.thirst;
            m.carrySlots = p.carry; m.waterproof = p.waterproof;
            it.rarity = p.rarity; it.clothingSet = p.set ?? ""; it.sellValue = p.value;
            if (it.wearSlot == ASHWearSlot.None) it.wearSlot = p.slot;
            if (it.wearSlot != ASHWearSlot.Face || (p.id != "wet_rag" && p.id != "gas_mask")) it.maxStack = 1;
            EditorUtility.SetDirty(it);
            return true;
        }

        static ASHItemDefinition Create(Piece p)
        {
            ASHMenuInstaller.Folder(ItemDir);
            string path = ItemDir + "/ASH_Item_" + p.icon + ".asset";
            var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(path);
            if (it) return it;
            it = ScriptableObject.CreateInstance<ASHItemDefinition>();
            it.itemId = p.id; it.displayName = p.name; it.description = p.desc;
            it.category = ASHItemCategory.Tool; it.maxStack = 1; it.useVerb = "Wear"; it.wearSlot = p.slot;
            it.uiTint = new Color(0.8f, 0.74f, 0.62f);
            AssetDatabase.CreateAsset(it, path);
            Apply(it, p);
            return it;
        }

        // ------------------------------------------------------------------ icons
        /// <summary>Puts ASH_Icon_&lt;Name&gt; sprites on the new clothing items (replacing placeholders or empty icons only);
        /// items without a sprite yet get a rarity-coloured letter placeholder. Import settings are never changed.</summary>
        internal static int WireIcons(HashSet<Object> touched)
        {
            int n = 0; var S = ASHClothingSettings.I;
            foreach (var p in Pieces())
            {
                if (string.IsNullOrEmpty(p.icon)) continue;
                var it = I(p.id); if (!it) continue;
                var art = FindIcon(p.icon);
                bool placeholder = !it.icon || AssetDatabase.GetAssetPath(it.icon).StartsWith(PlaceholderDir) || AssetDatabase.GetAssetPath(it.icon).Contains("/ASH_Letter_");
                if (art) { if (it.icon != art && placeholder) { it.icon = art; EditorUtility.SetDirty(it); touched.Add(it); n++; } continue; }
                if (!it.icon)
                {
                    var ph = EnsurePlaceholder(p, S.RarityColor(p.rarity));
                    if (ph) { it.icon = ph; EditorUtility.SetDirty(it); touched.Add(it); }
                }
            }
            return n;
        }

        internal static Sprite FindIcon(string name)
        {
            foreach (var d in IconDirs)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(d + "/ASH_Icon_" + name + ".png");
                if (s) return s;
            }
            return null;
        }

        static Sprite EnsurePlaceholder(Piece p, Color rarity)
        {
            ASHMenuInstaller.Folder(PlaceholderDir);
            string path = PlaceholderDir + "/ASH_Ph_" + p.icon + ".png";
            if (!File.Exists(path))
            {
                const int S = 256; var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                var px = new Color32[S * S]; Color32 bg = rarity, edge = Color.Lerp(rarity, Color.black, 0.45f);
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    bool inside = x >= 24 && x < S - 24 && y >= 24 && y < S - 24;
                    bool border = inside && (x < 34 || x >= S - 34 || y < 34 || y >= S - 34);
                    px[y * S + x] = !inside ? new Color32(0, 0, 0, 0) : border ? edge : bg;
                }
                tex.SetPixels32(px);
                ASHWildlifeSetup.DrawLetters(tex, p.letters, new Color(0.95f, 0.9f, 0.8f, 1f));
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                if (imp) { imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.SaveAndReimport(); }
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ------------------------------------------------------------------ recipes + Survival Guide
        sealed class Meta { public ASHUnlockCondition[] unlock; public int col, row, level; public string hint; public ASHRecipe[] parents; }
        static readonly Dictionary<ASHRecipe, Meta> Pending = new Dictionary<ASHRecipe, Meta>();

        static ASHUnlockCondition[] First(params ASHItemDefinition[] items)
        {
            var l = new List<ASHUnlockCondition>(); foreach (var it in items) if (it) l.Add(new ASHUnlockCondition { kind = ASHUnlockKind.FirstPickup, item = it }); return l.ToArray();
        }
        static ASHUnlockCondition[] Crafted(params ASHItemDefinition[] items)
        {
            var l = new List<ASHUnlockCondition>(); foreach (var it in items) if (it) l.Add(new ASHUnlockCondition { kind = ASHUnlockKind.Crafted, item = it, quantity = 1 }); return l.ToArray();
        }
        static ASHUnlockCondition[] Join(params ASHUnlockCondition[][] a) { var l = new List<ASHUnlockCondition>(); foreach (var x in a) if (x != null) l.AddRange(x); return l.ToArray(); }
        static ASHRecipeIngredient Ing(ASHItemDefinition it, int n) => it ? new ASHRecipeIngredient { item = it, amount = Mathf.Max(1, n) } : null;
        static ASHRecipe R0(string id) => AssetDatabase.LoadAssetAtPath<ASHRecipe>(RecipeDir + "/ASH_Recipe_" + id + ".asset") ?? FindRecipe(id);
        static ASHRecipe FindRecipe(string recipeId)
        {
            foreach (var g in AssetDatabase.FindAssets("t:ASHRecipe")) { var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(AssetDatabase.GUIDToAssetPath(g)); if (r && r.recipeId == recipeId) return r; }
            return null;
        }

        static ASHRecipe Rec(string id, string asset, string name, string desc, ASHItemDefinition output, float secs, int level, ASHToolType tool, ASHUnlockCondition[] unlock, int col, int row, string hint, ASHRecipe[] parents, params ASHRecipeIngredient[] ings)
        {
            if (!output) return null;
            var valid = new List<ASHRecipeIngredient>(); foreach (var x in ings) if (x != null && x.item) valid.Add(x);
            if (valid.Count == 0) return null;
            ASHMenuInstaller.Folder(RecipeDir);
            string path = RecipeDir + "/ASH_Recipe_" + asset + ".asset";
            var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(path);
            bool isNew = !r;
            if (isNew) { r = ScriptableObject.CreateInstance<ASHRecipe>(); AssetDatabase.CreateAsset(r, path); }
            if (isNew)
            {
                r.recipeId = id; r.displayName = name; r.description = desc; r.category = ASHRecipeCategory.Clothing;
                r.outputItem = output; r.outputAmount = 1; r.craftSeconds = secs; r.requiredCraftingLevel = Mathf.Max(1, level);
                r.requiredTool = tool; r.station = ASHCraftStation.Anywhere; r.ingredients = valid;
            }
            if (!r.icon && output.icon) r.icon = output.icon;
            else if (r.icon && output.icon && r.icon != output.icon && AssetDatabase.GetAssetPath(r.icon).StartsWith(PlaceholderDir)) r.icon = output.icon;
            EditorUtility.SetDirty(r);
            Pending[r] = new Meta { unlock = unlock, col = col, row = row, level = level, hint = hint, parents = parents };
            return r;
        }

        static List<ASHRecipe> Recipes(HashSet<Object> touched)
        {
            var cloth = I("cloth"); var rags = I("rags"); var strips = I("hidestrips"); var thread = I("sinewthread"); var needle = I("boneneedle");
            var deer = I("deer_skin"); var wolf = I("wolfpelt"); var boar = I("boarhide"); var bear = I("bearhide"); var proof = I("waterproofing"); var rope = I("rope");
            var rStrips = FindRecipe("hidestrips"); var rNeedle = FindRecipe("boneneedle"); var rProof = FindRecipe("waterproofing"); var rThread = FindRecipe("sinewthread");
            var l = new List<ASHRecipe>();
            void Add(ASHRecipe r) { if (r) { l.Add(r); touched.Add(r); } }
            // column 9: Sew
            Add(Rec("clothfacewrap", "ClothFaceWrap", "Cloth Face Wrap", "Fold a dry cloth over nose and mouth.", I("clothfacewrap"), 1.5f, 1, ASHToolType.None, First(cloth, rags), 9, 0, "Find some cloth or rags.", null, Ing(cloth ?? rags, 1)));
            Add(Rec("woolscarf", "WoolScarf", "Wool Scarf", "Stitch cloth into a long scarf.", I("woolscarf"), 2.5f, 1, ASHToolType.None, First(cloth), 9, 1, "Find some cloth.", rThread ? new[] { rThread } : null, Ing(cloth, 2), Ing(thread, 1)));
            Add(Rec("ashhood", "AshHood", "Ash Hood", "A cloth hood lined with hide strips against the ash.", I("ashhood"), 4f, 2, ASHToolType.None, Join(Crafted(strips), First(I("ashhood"))), 9, 2, "Cut hide strips (or find an Ash Hood).", rStrips ? new[] { rStrips } : null, Ing(cloth, 2), Ing(rags, 1), Ing(strips, 2), Ing(thread, 1)));
            // column 10: Clothes (hide)
            var hideParents = new List<ASHRecipe>(); if (rStrips) hideParents.Add(rStrips); if (rNeedle) hideParents.Add(rNeedle);
            var hp = hideParents.ToArray();
            Add(Rec("hidevest", "HideVest", "Hide Vest", "A stitched hide vest.", I("hidevest"), 5f, 2, ASHToolType.None, Crafted(strips, needle), 10, 0, "Cut hide strips and carve a bone needle.", hp, Ing(deer ?? wolf, 1), Ing(strips, 2), Ing(thread, 1), Ing(needle, 1)));
            Add(Rec("hidetrousers", "HideTrousers", "Hide Trousers", "Stitched hide trousers.", I("hidetrousers"), 5f, 2, ASHToolType.None, Crafted(strips, needle), 10, 1, "Cut hide strips and carve a bone needle.", hp, Ing(boar ?? deer, 1), Ing(strips, 2), Ing(thread, 1), Ing(needle, 1)));
            Add(Rec("hidewraps", "HideWraps", "Hide Wraps", "Wrap hide strips over cloth for the feet.", I("hidewraps"), 3f, 2, ASHToolType.None, Crafted(strips), 10, 2, "Cut hide strips.", rStrips ? new[] { rStrips } : null, Ing(strips, 2), Ing(cloth, 1), Ing(thread, 1)));
            Add(Rec("hideboots", "HideBoots", "Hide Boots", "Stitched hide boots.", I("hideboots"), 5f, 2, ASHToolType.None, Crafted(strips, needle), 10, 3, "Cut hide strips and carve a bone needle.", hp, Ing(deer ?? boar ?? wolf, 1), Ing(strips, 2), Ing(thread, 1), Ing(needle, 1)));
            Add(Rec("hidegauntlets", "HideGauntlets", "Hide Gauntlets", "Long hide gloves laced at the wrist.", I("hidegauntlets"), 4f, 2, ASHToolType.None, Crafted(strips, needle), 10, 4, "Cut hide strips and carve a bone needle.", hp, Ing(deer ?? wolf, 1), Ing(strips, 2), Ing(thread, 1), Ing(needle, 1)));
            // column 11: Weather
            Add(Rec("waxedcloak", "WaxedCloak", "Waxed Cloak", "Cloth rubbed with tallow wax. Sheds ash-rain.", I("waxedcloak"), 5f, 2, ASHToolType.None, Crafted(proof), 11, 0, "Make waterproofing.", rProof ? new[] { rProof } : null, Ing(cloth, 3), Ing(proof, 1), Ing(rope, 1)));
            // column 12: Hunt gear
            Add(Rec("wolfskinparka", "WolfskinParka", "Wolfskin Parka", "A parka of wolf pelts.", I("wolfskinparka"), 7f, 3, ASHToolType.None, First(wolf), 12, 0, "Take a wolf pelt. (Crafting level 3)", hp, Ing(wolf, 2), Ing(strips, 3), Ing(thread, 2), Ing(needle, 1)));
            Add(Rec("bearskinhood", "BearskinHood", "Bearskin Hood", "A heavy hood of bear hide.", I("bearskinhood"), 7f, 4, ASHToolType.None, Join(First(bear), new[] { new ASHUnlockCondition { kind = ASHUnlockKind.ManualPage, page = 4 } }), 12, 1, "Take a bear hide, or find the hunting notes. (Crafting level 4)", hp, Ing(bear, 1), Ing(strips, 2), Ing(thread, 1), Ing(needle, 1)));
            return l;
        }

        static void WireManual(List<ASHRecipe> recipes, HashSet<Object> touched)
        {
            var d = AssetDatabase.LoadAssetAtPath<ASHFieldManualData>(ManualPath);
            if (!d) { Debug.LogWarning("[ASH] Field Manual asset missing; clothing recipes were created but not wired."); Pending.Clear(); return; }
            bool changed = false;
            foreach (var r in recipes)
            {
                if (!r || d.Find(r) != null || !Pending.TryGetValue(r, out var m)) continue;
                var e = new ASHFieldManualEntry { recipe = r, basic = false, column = m.col, row = m.row, hint = m.hint, requireAll = false };
                if (m.unlock != null) foreach (var c in m.unlock) if (c != null) e.conditions.Add(c);
                // hide pieces need both strips AND a needle crafted
                e.requireAll = m.unlock != null && m.unlock.Length > 1 && m.unlock[0].kind == ASHUnlockKind.Crafted && m.unlock[m.unlock.Length - 1].kind == ASHUnlockKind.Crafted;
                if (m.parents != null) foreach (var p in m.parents) if (p) e.parents.Add(p);
                d.entries.Add(e); changed = true;
            }
            Pending.Clear();
            if (changed) { EditorUtility.SetDirty(d); touched.Add(d); }
        }

        // ------------------------------------------------------------------ values (AC) + breakdowns for every item
        static readonly Dictionary<string, int> Values = new Dictionary<string, int>
        {
            // resources
            {"battery",5},{"cloth",2},{"deadwood",1},{"deer_skin",6},{"dry_rag",1},{"emptytin",1},{"firewood",1},{"kindling",1},{"plantfiber",1},
            {"reclaimedwood",1},{"rope",4},{"rubbingalcohol",4},{"scrapmetal",2},{"stone",1},{"rags",1},{"oldshirt",3},
            // food / drink
            {"boiledwater",2},{"bottledwater",3},{"cannedbeans",6},{"cannedpeaches",6},{"chokecherries",1},{"chokecherrytea",2},{"cookedfish",4},{"cookedmeat",4},
            {"driedchokecherries",2},{"energybar",5},{"fishbroth",4},{"glazedmeat",6},{"heartychili",7},{"meatstew",6},{"rawfish",2},{"rawmeat",2},{"rawsmallmeat",1},
            {"smokedfish",6},{"sodacan",2},{"tinofwater",2},{"whiskey",10},{"deadrabbit",3},{"wholetrout",4},
            // medical
            {"antisepticbandage",8},{"bandage",4},{"fiberbandage",3},{"firstaidkit",25},{"painkillers",8},
            // tools / lights / placeables
            {"canopener",5},{"compass",15},{"damagedgps",10},{"filter_cartridge",12},{"hatchet",18},{"huntingknife",15},{"improvisedknife",4},{"water_bottle",5},
            {"campfire",1},{"flashlight",12},{"lantern",15},{"torch",2},
            // weapons / ammo
            {"huntingrifle",60},{"pistol",45},{"shotgun",55},{"woodenbow",15},{"pistolrounds",1},{"riflerounds",2},{"shotgunshells",2},{"woodenarrow",1},
            // vehicle parts
            {"gascan",8},{"radiatorhose",6},{"sparetire",12},{"sparkplugs",4},
        };

        sealed class BD { public (string id, int n)[] y; public ASHToolType tool; public float secs; }
        static BD Soft(params (string, int)[] y) => new BD { y = y, tool = ASHToolType.Knife, secs = 2.5f };
        static BD Hard(params (string, int)[] y) => new BD { y = y, tool = ASHToolType.None, secs = 3f };

        static readonly Dictionary<string, BD> Breakdowns = new Dictionary<string, BD>
        {
            // clothing
            {"watchcap",Soft(("rags",2))},{"ashhood",Soft(("cloth",1),("hidestrips",1))},{"bearskinhood",Soft(("hidestrips",3),("sinewthread",1))},
            {"clothfacewrap",Soft(("rags",1))},{"dustgoggles",Hard(("scrapmetal",1))},{"woolundershirt",Soft(("cloth",2))},{"hidevest",Soft(("hidestrips",2))},
            {"quiltedliner",Soft(("cloth",3))},{"canvasworkcoat",Soft(("cloth",3))},{"waxedcloak",Soft(("cloth",2))},{"wolfskinparka",Soft(("hidestrips",3),("sinewthread",1))},
            {"expeditionparka",Soft(("cloth",4),("hidestrips",2))},{"workgloves",Soft(("rags",2))},{"hidegauntlets",Soft(("hidestrips",2))},{"rangergloves",Soft(("cloth",1),("hidestrips",1))},
            {"workpants",Soft(("cloth",2))},{"hidetrousers",Soft(("hidestrips",2))},{"wooltrousers",Soft(("cloth",3))},{"workboots",Soft(("hidestrips",2),("scrapmetal",1))},
            {"hidewraps",Soft(("hidestrips",1))},{"hideboots",Soft(("hidestrips",2))},{"winterboots",Soft(("hidestrips",2),("cloth",1))},
            {"canvasrucksack",Soft(("cloth",3),("rope",1))},{"scoutpack",Soft(("cloth",3),("rope",1),("scrapmetal",1))},{"woolscarf",Soft(("cloth",1))},
            {"furhat",Soft(("hidestrips",1),("rabbitfur",1))},{"furcoat",Soft(("hidestrips",3))},{"furgloves",Soft(("hidestrips",1))},{"furpants",Soft(("hidestrips",2))},
            {"hidebackpack",Soft(("hidestrips",3),("rope",1))},{"wolfcharm",Soft(("wolftooth",1))},{"tusktrophy",Soft(("boartusk",1))},{"clawcharm",Soft(("predatorclaw",1))},
            {"gas_mask",Hard(("scrapmetal",1),("cloth",1))},{"pocketwatch",Hard(("scrapmetal",1))},
            // tools / lights
            {"hatchet",Hard(("scrapmetal",2),("deadwood",1))},{"huntingknife",Hard(("scrapmetal",2))},{"improvisedknife",Hard(("scrapmetal",1))},{"canopener",Hard(("scrapmetal",1))},
            {"compass",Hard(("scrapmetal",1))},{"damagedgps",Hard(("scrapmetal",1),("battery",1))},{"flashlight",Hard(("scrapmetal",1))},{"lantern",Hard(("scrapmetal",2))},
            {"torch",Hard(("deadwood",1))},{"boneknife",Hard(("animalbone",1))},{"antlerknife",Hard(("antler",1))},{"filter_cartridge",Soft(("cloth",1))},
            {"fuel_can",Hard(("scrapmetal",2))},{"gascan",Hard(("scrapmetal",2))},
            // weapons
            {"huntingrifle",Hard(("scrapmetal",4),("reclaimedwood",1))},{"shotgun",Hard(("scrapmetal",4),("reclaimedwood",1))},{"pistol",Hard(("scrapmetal",3))},{"woodenbow",Hard(("deadwood",1),("bowstring",1))},
            // vehicle / trinkets
            {"carbattery",Hard(("scrapmetal",3))},{"tire",Hard(("scrapmetal",1))},{"sparetire",Hard(("scrapmetal",1))},{"sparkplugs",Hard(("scrapmetal",1))},
            {"antiquebutton",Hard(("scrapmetal",1))},{"cointin",Hard(("emptytin",1))},{"silverring",Hard(("scrapmetal",1))},
            // medical / camp
            {"firstaidkit",Soft(("bandage",2),("cloth",1))},{"bandage",Soft(("cloth",1))},{"antisepticbandage",Soft(("cloth",1))},{"fiberbandage",Soft(("plantfiber",1))},
            {"bedroll",Soft(("cloth",2),("hidestrips",1))},{"largetrap",Hard(("scrapmetal",3))},{"rabbitsnare",Hard(("rope",1))},{"tallowcandle",Hard(("tallow",1))},
            {"rope",Soft(("plantfiber",2))},{"waterskin",Soft(("hidestrips",1))},{"emptywaterskin",Soft(("hidestrips",1))},
        };

        static int ValuesAndBreakdowns(HashSet<Object> touched, bool force)
        {
            int n = 0;
            foreach (var g in AssetDatabase.FindAssets("t:ASHItemDefinition", new[] { "Assets/_ASH" }))
            {
                var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g));
                if (!it || string.IsNullOrEmpty(it.itemId) || Hands.Contains(it.itemId) || it.keyItem) continue;
                bool dirty = false;
                if (it.sellValue <= 0 && Values.TryGetValue(it.itemId, out int v)) { it.sellValue = v; dirty = true; }
                if ((it.breakdownYield == null || it.breakdownYield.Count == 0 || force) && Breakdowns.TryGetValue(it.itemId, out var bd))
                {
                    var list = new List<ASHRecipeIngredient>();
                    foreach (var (id, c) in bd.y) { var m = I(id); if (m && m != it) list.Add(new ASHRecipeIngredient { item = m, amount = c }); }
                    if (list.Count > 0 && (force || it.breakdownYield == null || it.breakdownYield.Count == 0))
                    {
                        it.breakdownYield = list; it.breakdownTool = bd.tool; it.breakdownToolRequired = false; it.breakdownSeconds = bd.secs; it.breakdownNoToolYield = 0.5f; dirty = true;
                    }
                }
                if (dirty) { EditorUtility.SetDirty(it); touched.Add(it); n++; }
            }
            return n;
        }

        // ------------------------------------------------------------------ settings
        static ASHClothingSettings EnsureSettings(HashSet<Object> touched)
        {
            var S = AssetDatabase.LoadAssetAtPath<ASHClothingSettings>(SettingsPath);
            if (!S) { ASHMenuInstaller.Folder("Assets/_ASH/Resources"); S = ScriptableObject.CreateInstance<ASHClothingSettings>(); AssetDatabase.CreateAsset(S, SettingsPath); }
            if (S.sets == null || S.sets.Count == 0)
            {
                S.sets = new List<ASHClothingSettings.SetBonus>
                {
                    new ASHClothingSettings.SetBonus { setName = "Fur", piecesRequired = 3, coldResist = 8, description = "Fur Hat, Fur Coat, Fur Gloves, Fur Pants: +8% cold resist." },
                    new ASHClothingSettings.SetBonus { setName = "Hide", piecesRequired = 3, staminaDrainMul = 0.95f, description = "Hide Vest / Trousers / Boots / Wraps / Gauntlets / Backpack: -5% stamina drain (backpack slots stay 4 / 8 / 12)." },
                    new ASHClothingSettings.SetBonus { setName = "Ash walker", piecesRequired = 3, ashResist = 8, description = "Ash Hood, Waxed Cloak, Face Wrap or Dust Goggles: +8% ash resist." },
                };
                EditorUtility.SetDirty(S);
            }
            touched.Add(S);
            return S;
        }

        static void Save(HashSet<Object> touched) { foreach (var o in touched) if (o) AssetDatabase.SaveAssetIfDirty(o); }

        internal static ASHItemDefinition I(string id)
        {
            foreach (var g in AssetDatabase.FindAssets("t:ASHItemDefinition"))
            {
                var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g));
                if (it && it.itemId == id) return it;
            }
            return null;
        }
    }

    /// <summary>When an ASH_Icon_&lt;Name&gt;.png in an icon folder is (re)imported as a Sprite - e.g. after Anthony converts it - the matching
    /// clothing item gets it if its icon is empty or a placeholder. Saves only those items.</summary>
    sealed class ASHClothingIconPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool hit = false;
            foreach (var p in imported)
            {
                var f = p.Replace('\\', '/');
                if (!Path.GetFileName(f).StartsWith("ASH_Icon_") || !f.EndsWith(".png")) continue;
                foreach (var d in ASHClothingSetup.IconDirs) if (f.StartsWith(d + "/")) hit = true;
            }
            if (!hit) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var touched = new HashSet<Object>();
                int n = ASHClothingSetup.WireIcons(touched);
                foreach (var o in touched) if (o) AssetDatabase.SaveAssetIfDirty(o);
                if (n > 0) Debug.Log($"[ASH] Clothing icons auto-wired: {n}");
            };
        }
    }
}
#endif
