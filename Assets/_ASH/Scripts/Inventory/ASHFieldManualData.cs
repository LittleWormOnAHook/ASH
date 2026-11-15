using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Inventory
{
    public enum ASHUnlockKind
    {
        [Tooltip("First time the item enters the pack.")] FirstPickup,
        [Tooltip("Item crafted (total output, at least Quantity).")] Crafted,
        [Tooltip("Torn manual page number found.")] ManualPage
    }

    [Serializable]
    public sealed class ASHUnlockCondition
    {
        public ASHUnlockKind kind = ASHUnlockKind.FirstPickup;
        [Tooltip("FirstPickup / Crafted: the item.")] public ASHItemDefinition item;
        [Tooltip("Crafted: how many must have been crafted in total.")] [Min(1)] public int quantity = 1;
        [Tooltip("ManualPage: page number (1-based).")] [Min(1)] public int page = 1;
    }

    /// <summary>One recipe in Grandpa's Field Manual tree.</summary>
    [Serializable]
    public sealed class ASHFieldManualEntry
    {
        public ASHRecipe recipe;
        [Tooltip("Unlocked as soon as the manual is read (the basics).")] public bool basic;
        [Tooltip("Unlock conditions (empty + not basic = never unlocks).")] public List<ASHUnlockCondition> conditions = new List<ASHUnlockCondition>();
        [Tooltip("All conditions needed (otherwise any one).")] public bool requireAll = true;
        [Tooltip("Shown on the faded sketch while locked. Empty = generated from the conditions.")] [TextArea(1, 3)] public string hint;
        [Header("Tree layout")]
        [Tooltip("Column (0 = left).")] [Range(0, 12)] public int column;
        [Tooltip("Row (0 = top).")] [Range(0, 30)] public int row;
        [Tooltip("Branch lines come from these recipes.")] public List<ASHRecipe> parents = new List<ASHRecipe>();
    }

    [Serializable]
    public sealed class ASHManualPage
    {
        [Min(1)] public int number = 1;
        public string title = "Torn page";
        [TextArea(2, 6)] public string text;
    }

    /// <summary>
    /// Grandpa's Field Manual: the crafting tree (recipes, basics, unlock conditions, layout) and the torn pages. Lives at
    /// Resources/ASH_FieldManual (created/updated by ASH/Systems/Field Manual/Install Data). Edit freely: entries, conditions
    /// and layout are pure data.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Inventory/Field Manual", fileName = "ASH_FieldManual")]
    public sealed class ASHFieldManualData : ScriptableObject
    {
        [Header("Cover")]
        public string coverTitle = "SURVIVAL GUIDE";
        public string coverSubtitle = "Field notes - property of W. Black";
        [TextArea(2, 5)] public string coverNote = "If you're reading this, the world went sideways. Read it all. Keep it dry. - Grandpa";
        public string tabName = "GRANDPA'S FIELD MANUAL";
        [Tooltip("The journal item picked up in Grandpa's cabin (opens the cover view).")]
        public ASHItemDefinition journalItem;
        [Tooltip("Toast when Q is pressed before the manual was read.")]
        public string lockedMessage = "You have no guide to craft from yet. Grandpa kept a journal somewhere in the cabin.";

        [Header("Pages")]
        [Range(1, 60)] public int totalPages = 12;
        public List<ASHManualPage> pages = new List<ASHManualPage>();

        [Header("Recipes")]
        public List<ASHFieldManualEntry> entries = new List<ASHFieldManualEntry>();

        public ASHFieldManualEntry Find(ASHRecipe r) { foreach (var e in entries) if (e != null && e.recipe == r) return e; return null; }
        public ASHManualPage Page(int n) { foreach (var p in pages) if (p != null && p.number == n) return p; return null; }

        static ASHFieldManualData _i;
        public static ASHFieldManualData I { get { if (!_i) _i = Resources.Load<ASHFieldManualData>("ASH_FieldManual"); return _i; } }
    }
}
