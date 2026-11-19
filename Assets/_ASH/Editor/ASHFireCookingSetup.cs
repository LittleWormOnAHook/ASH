using System.Collections.Generic;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: creates Resources/ASH_CookingSettings and assigns the cooking/boiling loops; gives Campfire recipes a
    /// cook time where still 0; sets food safety on raw food (only where still 0) and buffs dried chokecherries (only if still the
    /// old weaker values); creates the combined-food items and the starter combinations (Resources/CookCombos) when missing and
    /// adds them to the item registry. Never overwrites hand-tuned values. No scene changes.</summary>
    public static class ASHFireCookingSetup
    {
        const string AssetPath = "Assets/_ASH/Resources/ASH_CookingSettings.asset";
        const string AudioDir = "Assets/_ASH/Audio/Cooking";
        const string ItemDir = "Assets/_ASH/Data/Items/Cooked";
        const string ComboDir = "Assets/_ASH/Resources/CookCombos";
        const string RegistryPath = "Assets/_ASH/Resources/ASH_ItemRegistry.asset";
        static readonly Dictionary<string, float> Minutes = new Dictionary<string, float>
        { { "boilwater", 20f }, { "cookfish", 20f }, { "cookmeat", 30f }, { "drychokecherries", 90f } };
        const float OtherCampfireMinutes = 30f;

        // itemId, sickness chance, health, thirst, hunger loss
        static readonly (string id, float chance, float hp, float thirst, float hunger)[] Sickness =
        { ("rawmeat", 0.35f, 8f, 10f, 6f), ("rawfish", 0.3f, 6f, 8f, 5f), ("tinofwater", 0.25f, 5f, 12f, 0f) };

        sealed class NewItem { public string id, name, desc, baseId, verb; public float h, t, hp, st; public Color tint; }
        static readonly NewItem[] Items =
        {
            new NewItem { id = "meatstew", name = "Meat Stew", desc = "Meat simmered in boiled water. Warm, filling and safe.", baseId = "cookedmeat", verb = "Eat", h = 55, t = 20, hp = 10, st = 0, tint = new Color(0.62f, 0.42f, 0.3f) },
            new NewItem { id = "glazedmeat", name = "Berry-Glazed Meat", desc = "Meat cooked with a sticky chokecherry glaze.", baseId = "cookedmeat", verb = "Eat", h = 48, t = 4, hp = 8, st = 6, tint = new Color(0.6f, 0.22f, 0.3f) },
            new NewItem { id = "fishbroth", name = "Fish Broth", desc = "Trout boiled down into a clear, salty broth.", baseId = "cookedfish", verb = "Eat", h = 38, t = 25, hp = 8, st = 0, tint = new Color(0.75f, 0.7f, 0.5f) },
            new NewItem { id = "chokecherrytea", name = "Chokecherry Tea", desc = "Dried chokecherries steeped in boiled water. Tart and warming.", baseId = "boiledwater", verb = "Drink", h = 6, t = 38, hp = 6, st = 12, tint = new Color(0.65f, 0.25f, 0.35f) },
            new NewItem { id = "heartychili", name = "Hearty Chili", desc = "Beans and meat cooked down together. Keeps you going for hours.", baseId = "cannedbeans", verb = "Eat", h = 70, t = 0, hp = 12, st = 8, tint = new Color(0.7f, 0.3f, 0.18f) },
            new NewItem { id = "smokedfish", name = "Cherry-Smoked Fish", desc = "Fish smoked over the fire with crushed chokecherries.", baseId = "cookedfish", verb = "Eat", h = 42, t = 4, hp = 7, st = 4, tint = new Color(0.7f, 0.45f, 0.35f) },
        };
        // comboId, name, a, b, output, unlockLevel (0 = discover only), hint
        static readonly (string id, string name, string a, string b, string output, int level, string hint)[] Combos =
        {
            ("chokecherrytea", "Chokecherry Tea", "boiledwater", "driedchokecherries", "chokecherrytea", 2, "Something to steep."),
            ("glazedmeat", "Berry-Glazed Meat", "rawmeat", "chokecherries", "glazedmeat", 3, "Sweet and savoury."),
            ("fishbroth", "Fish Broth", "boiledwater", "rawfish", "fishbroth", 4, "Fish and water."),
            ("meatstew", "Meat Stew", "boiledwater", "rawmeat", "meatstew", 6, "Meat and water."),
            ("heartychili", "Hearty Chili", "cannedbeans", "rawmeat", "heartychili", 10, "From a tin, with meat."),
            ("smokedfish", "Cherry-Smoked Fish", "rawfish", "chokecherries", "smokedfish", 0, "Nobody wrote this one down."),
        };

        [MenuItem("ASH/Systems/Fire Cooking (create or update)")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Fire Cooking: stop Play mode first."); return; }
            var s = AssetDatabase.LoadAssetAtPath<ASHCookingSettings>(AssetPath);
            if (!s) { s = ScriptableObject.CreateInstance<ASHCookingSettings>(); AssetDatabase.CreateAsset(s, AssetPath); Debug.Log("[ASH] Created " + AssetPath); }
            if (!s.cookLoop) s.cookLoop = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/ASH_Cook_Simmer_Loop.ogg");
            if (!s.boilLoop) s.boilLoop = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/ASH_Boil_Loop.ogg");
            if (s.startClips == null || s.startClips.Length == 0)
            {
                var c = AssetDatabase.LoadAssetAtPath<ASHCampfireSettings>("Assets/_ASH/Resources/ASH_CampfireSettings.asset");
                if (c && c.addFuelClips != null) s.startClips = c.addFuelClips; // same wood/pot clunk as feeding the fire
            }
            EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s);

            int set = 0, kept = 0;
            foreach (var g in AssetDatabase.FindAssets("t:ASHRecipe", new[] { "Assets/_ASH" }))
            {
                var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(AssetDatabase.GUIDToAssetPath(g));
                if (!r || r.station != ASHCraftStation.Campfire) continue;
                if (r.cookGameMinutes > 0f) { kept++; continue; }
                r.cookGameMinutes = Minutes.TryGetValue(r.recipeId, out var m) ? m : OtherCampfireMinutes;
                EditorUtility.SetDirty(r); AssetDatabase.SaveAssetIfDirty(r); set++;
            }

            // ---- items by id
            var byId = new Dictionary<string, ASHItemDefinition>();
            foreach (var g in AssetDatabase.FindAssets("t:ASHItemDefinition", new[] { "Assets/_ASH" }))
            {
                var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g));
                if (it && !string.IsNullOrEmpty(it.itemId) && !byId.ContainsKey(it.itemId)) byId[it.itemId] = it;
            }

            int sick = 0;
            foreach (var x in Sickness)
            {
                if (!byId.TryGetValue(x.id, out var it) || it.sicknessChance > 0f) continue;
                it.sicknessChance = x.chance; it.sicknessHealthLoss = x.hp; it.sicknessThirstLoss = x.thirst; it.sicknessHungerLoss = x.hunger;
                EditorUtility.SetDirty(it); AssetDatabase.SaveAssetIfDirty(it); sick++;
            }
            bool dried = false;
            if (byId.TryGetValue("driedchokecherries", out var dc) && dc.restoreHunger < 18f)
            {
                dc.restoreHunger = 20f; dc.restoreThirst = 0f; dc.restoreHealth = 3f; dc.restoreStamina = 4f; // was weaker than fresh berries
                EditorUtility.SetDirty(dc); AssetDatabase.SaveAssetIfDirty(dc); dried = true;
            }

            // ---- combined foods
            Folder(ItemDir); Folder(ComboDir);
            int madeItems = 0;
            foreach (var n in Items)
            {
                if (byId.ContainsKey(n.id)) continue;
                var it = ScriptableObject.CreateInstance<ASHItemDefinition>();
                if (byId.TryGetValue(n.baseId, out var b)) EditorUtility.CopySerialized(b, it); // icon, world prefab, stack, etc.
                it.itemId = n.id; it.displayName = n.name; it.description = n.desc; it.uiTint = n.tint; it.useVerb = n.verb;
                it.category = ASHItemCategory.Consumable; it.keyItem = false; it.requiresOpener = false; it.leftoverItem = null;
                it.restoreHunger = n.h; it.restoreThirst = n.t; it.restoreHealth = n.hp; it.restoreStamina = n.st;
                it.sicknessChance = 0f; it.breakdownYield = new List<ASHRecipeIngredient>(); it.equipSlot = ASHEquipSlot.None;
                it.maxStack = Mathf.Max(5, it.maxStack);
                string path = $"{ItemDir}/ASH_Item_{Pascal(n.name)}.asset";
                it.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(it, path);
                byId[n.id] = it; madeItems++;
            }

            int madeCombos = 0, skipped = 0;
            var existing = new HashSet<string>();
            foreach (var g in AssetDatabase.FindAssets("t:ASHCookCombo", new[] { "Assets/_ASH" }))
            { var c = AssetDatabase.LoadAssetAtPath<ASHCookCombo>(AssetDatabase.GUIDToAssetPath(g)); if (c) existing.Add(c.comboId); }
            foreach (var x in Combos)
            {
                if (existing.Contains(x.id)) continue;
                if (!byId.TryGetValue(x.a, out var a) || !byId.TryGetValue(x.b, out var b) || !byId.TryGetValue(x.output, out var o)) { skipped++; Debug.LogWarning($"[ASH] Fire Cooking: combo {x.id} skipped (missing item)"); continue; }
                var c = ScriptableObject.CreateInstance<ASHCookCombo>();
                c.comboId = x.id; c.displayName = x.name; c.hint = x.hint; c.ingredientA = a; c.ingredientB = b; c.output = o; c.outputAmount = 1; c.unlockLevel = x.level;
                AssetDatabase.CreateAsset(c, $"{ComboDir}/ASH_Combo_{Pascal(x.name)}.asset"); madeCombos++;
            }

            // ---- registry
            int reg = 0;
            var registry = AssetDatabase.LoadAssetAtPath<ASHItemRegistry>(RegistryPath);
            if (registry)
            {
                foreach (var n in Items) if (byId.TryGetValue(n.id, out var it) && !registry.items.Contains(it)) { registry.items.Add(it); reg++; }
                if (reg > 0) { EditorUtility.SetDirty(registry); AssetDatabase.SaveAssetIfDirty(registry); }
            }
            else Debug.LogWarning("[ASH] Fire Cooking: no item registry at " + RegistryPath + " (run ASH/Systems/Install Data)");

            Debug.Log($"[ASH] Fire Cooking: settings ok (cook loop {(s.cookLoop ? "set" : "MISSING")}, boil loop {(s.boilLoop ? "set" : "MISSING")}); campfire recipes timed {set}, kept {kept}; " +
                      $"food safety set on {sick}; dried chokecherries {(dried ? "buffed" : "kept")}; combined foods created {madeItems}; combos created {madeCombos} (skipped {skipped}); registry +{reg}.");
        }

        static string Pascal(string n) { var sb = new System.Text.StringBuilder(); bool up = true; foreach (char ch in n) { if (char.IsLetterOrDigit(ch)) { sb.Append(up ? char.ToUpperInvariant(ch) : ch); up = false; } else up = true; } return sb.ToString(); }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
