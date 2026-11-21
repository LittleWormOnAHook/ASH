#if UNITY_EDITOR
using System.Collections.Generic;
using ASH.EditorTools.Systems;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent M2 cooking / crafting gates. Leaves five Survival Guide basics
    /// (plus the firewood campfire variant). Does not save the open scene.</summary>
    public static class ASHCraftingGatesSetup
    {
        const string ManualPath = "Assets/_ASH/Resources/ASH_FieldManual.asset";

        [MenuItem("ASH/Systems/Field Manual/Apply M2 Crafting Gates (idempotent)", false, 214)]
        public static void Apply()
        {
            var d = AssetDatabase.LoadAssetAtPath<ASHFieldManualData>(ManualPath);
            if (!d) { Debug.LogError("[ASH] Field Manual asset missing."); return; }

            // Survive (journal basics)
            KeepBasic(d, "wet_rag");
            KeepBasic(d, "campfire");
            KeepBasic(d, "campfirefirewood");
            KeepBasic(d, "splitkindling");
            KeepBasic(d, "bandage");
            KeepBasic(d, "boilwater");

            // Process: first pickup
            SetFirstPickup(d, "clothfromshirt", "oldshirt", "Find an old shirt, then you can cut it into cloth.");
            SetFirstPickup(d, "rope", "plantfiber", "Gather plant fiber, then you can twist it into rope.");
            SetFirstPickup(d, "flattentins", "emptytin", "Keep an empty tin can — flatten it into a blade blank.");

            // Craft equipment: torch from Survival Guide page 3 (Fire in the wet)
            SetManualPage(d, "torch", 3, "A note on making fire (Survival Guide page 3) shows how to bind a torch.");

            // Hunt / trap + shelter: Grandpa's Journal pages 4 and 5
            SetJournalPage(d, "rabbitsnare", "gj_04", "Grandpa's note at the hunting shack (journal page 4) shows how to set a snare.");
            SetJournalPage(d, "builttent", "gj_05", "Grandpa's note at the abandoned camp (journal page 5) shows how to build a tent.");
            SetJournalPage(d, "builttent_firewood", "gj_05", "Grandpa's note at the abandoned camp (journal page 5) shows how to build a tent.");

            // Bow branch also comes from the hunting shack page (keep the existing craft-2-ropes gate)
            AlignBow(d);

            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssets();
            Selection.activeObject = d;
            if (ASHFieldManual.Instance) ASHFieldManual.Instance.Reevaluate();
            Debug.Log("[ASH] M2 crafting gates applied. Basics: Wet Rag, Campfire (+ firewood variant), Split Kindling, Bandage, Boil Water. Torch = manual page 3. Cloth / Rope / Flatten Tins = first pickup. Snare + bow = Grandpa page 4. Built Tent = Grandpa page 5.");
        }

        static ASHFieldManualEntry Entry(ASHFieldManualData d, string recipeId)
        {
            var r = FindRecipe(recipeId);
            if (!r) { Debug.LogWarning("[ASH] Recipe missing: " + recipeId); return null; }
            var e = d.Find(r);
            if (e == null)
            {
                e = new ASHFieldManualEntry { recipe = r, basic = false, requireAll = true };
                d.entries.Add(e);
            }
            return e;
        }

        static void KeepBasic(ASHFieldManualData d, string recipeId)
        {
            var e = Entry(d, recipeId);
            if (e == null) return;
            e.basic = true;
            if (e.conditions == null) e.conditions = new List<ASHUnlockCondition>();
            else e.conditions.Clear();
        }

        static void SetFirstPickup(ASHFieldManualData d, string recipeId, string itemId, string hint)
        {
            var e = Entry(d, recipeId);
            if (e == null) return;
            var item = FindItem(itemId);
            e.basic = false;
            e.requireAll = true;
            e.hint = hint;
            e.conditions = new List<ASHUnlockCondition>
            {
                new ASHUnlockCondition { kind = ASHUnlockKind.FirstPickup, item = item, quantity = 1 }
            };
        }

        static void SetManualPage(ASHFieldManualData d, string recipeId, int page, string hint)
        {
            var e = Entry(d, recipeId);
            if (e == null) return;
            e.basic = false;
            e.requireAll = true;
            e.hint = hint;
            e.conditions = new List<ASHUnlockCondition>
            {
                new ASHUnlockCondition { kind = ASHUnlockKind.ManualPage, page = page }
            };
        }

        static void SetJournalPage(ASHFieldManualData d, string recipeId, string pageId, string hint)
        {
            var e = Entry(d, recipeId);
            if (e == null) return;
            e.basic = false;
            e.requireAll = true;
            e.hint = hint;
            e.conditions = new List<ASHUnlockCondition>
            {
                new ASHUnlockCondition { kind = ASHUnlockKind.JournalPage, collection = "grandpa", pageId = pageId }
            };
        }

        static void AlignBow(ASHFieldManualData d)
        {
            var e = Entry(d, "woodenbow");
            if (e == null) return;
            e.basic = false;
            e.requireAll = true;
            if (string.IsNullOrEmpty(e.hint)) e.hint = "Grandpa's note at the hunting shack (journal page 4) shows how to make a bow.";
            var rope = FindItem("rope");
            var conditions = new List<ASHUnlockCondition>();
            // keep the existing "craft 2 rope" discovery gate if rope exists
            if (rope) conditions.Add(new ASHUnlockCondition { kind = ASHUnlockKind.Crafted, item = rope, quantity = 2 });
            conditions.Add(new ASHUnlockCondition { kind = ASHUnlockKind.JournalPage, collection = "grandpa", pageId = "gj_04" });
            e.conditions = conditions;

            var arrows = Entry(d, "woodenarrows");
            if (arrows == null) return;
            arrows.basic = false;
            arrows.requireAll = true;
            if (string.IsNullOrEmpty(arrows.hint)) arrows.hint = "Grandpa's note at the hunting shack (journal page 4) shows how to fletch arrows.";
            // keep "craft the bow" if that condition was already pointing at the bow
            var bowItem = FindItem("woodenbow") ?? FindItem("bow");
            var ac = new List<ASHUnlockCondition>();
            if (bowItem) ac.Add(new ASHUnlockCondition { kind = ASHUnlockKind.Crafted, item = bowItem, quantity = 1 });
            ac.Add(new ASHUnlockCondition { kind = ASHUnlockKind.JournalPage, collection = "grandpa", pageId = "gj_04" });
            arrows.conditions = ac;
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
