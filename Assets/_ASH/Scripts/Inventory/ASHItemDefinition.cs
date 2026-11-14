using System.Collections.Generic;
using UnityEngine;

namespace ASH.Inventory
{
    public enum ASHItemCategory { Resource, Consumable, Placeable, Tool, Salvage, Medical, Key }

    /// <summary>Tool roles an item can fill (any matching item in the pack satisfies a requirement).</summary>
    public enum ASHToolType { None, Axe, Knife, CanOpener, GPS }

    /// <summary>
    /// Item definition. Slimmed from Genesis Project.Data.ItemData (identity, icon, world prefab, max stack,
    /// type, survival restores); combat/progression/XP fields removed. ASH adds tool roles, can-opening,
    /// leftovers (empty tins) and break-down (salvage) yields.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Inventory/Item Definition", fileName = "ASH_Item_New")]
    public sealed class ASHItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string itemId = "item";
        public string displayName = "New Item";
        [TextArea(2, 3)] public string description;
        public Sprite icon;
        public Color uiTint = new Color(0.85f, 0.8f, 0.7f);
        public ASHItemCategory category = ASHItemCategory.Resource;
        [Min(1)] public int maxStack = 20;
        [Tooltip("Verb shown when used, e.g. Eat, Drink, Apply.")]
        public string useVerb = "Use";

        [Header("Consumable restores (absolute points)")]
        public float restoreHunger;
        public float restoreThirst;
        public float restoreHealth;
        public float restoreStamina;
        [Tooltip("Canned goods: needs a can opener, knife or hatchet to open cleanly.")]
        public bool requiresOpener;
        [Range(0f, 1f)] [Tooltip("Share of restores kept when smashed open without a tool.")]
        public float smashedYield = 0.65f;
        [Tooltip("Given back after use (e.g. empty tin can). Not returned when a can is smashed open.")]
        public ASHItemDefinition leftoverItem;

        [Header("Tool")]
        public ASHToolType toolType = ASHToolType.None;

        [Header("Break down / harvest")]
        public List<ASHRecipeIngredient> breakdownYield = new List<ASHRecipeIngredient>();
        public ASHToolType breakdownTool = ASHToolType.None;
        [Tooltip("If true the tool is mandatory; otherwise it only speeds up and improves the yield.")]
        public bool breakdownToolRequired;
        [Range(0f, 15f)] public float breakdownSeconds = 2f;
        [Range(0f, 1f)] [Tooltip("Yield multiplier without the (optional) tool. Rounds down, minimum 1.")]
        public float breakdownNoToolYield = 0.5f;

        [Header("Placeable")]
        [Tooltip("Spawned in the world when this item is placed.")]
        public GameObject placeablePrefab;
        [Tooltip("Footprint used for placement validity checks (metres).")]
        public Vector3 placementFootprint = new Vector3(1.4f, 0.6f, 1.4f);
        [Range(0f, 45f)] public float maxPlacementSlope = 22f;

        public bool IsConsumable => category == ASHItemCategory.Consumable || category == ASHItemCategory.Medical;
        public bool IsPlaceable => category == ASHItemCategory.Placeable && placeablePrefab != null;
        public bool CanBreakDown => breakdownYield != null && breakdownYield.Count > 0;
    }
}
