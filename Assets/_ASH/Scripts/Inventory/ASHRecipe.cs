using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Inventory
{
    [Serializable]
    public sealed class ASHRecipeIngredient
    {
        public ASHItemDefinition item;
        [Min(1)] public int amount = 1;
    }

    public enum ASHRecipeCategory { Fire, Medical, Food, Tools, Materials }
    public enum ASHCraftStation { Anywhere, Campfire }

    /// <summary>
    /// Crafting recipe. Same shape as Genesis Project.Crafting.RecipeDefinition (id, name, description,
    /// ingredients, output, amount) and its CraftingStationType idea, reduced to Anywhere / lit Campfire,
    /// plus an optional required tool. Level gates and blueprint discovery removed.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Inventory/Recipe", fileName = "ASH_Recipe_New")]
    public sealed class ASHRecipe : ScriptableObject
    {
        public string recipeId = "recipe";
        public string displayName = "New Recipe";
        [TextArea(2, 3)] public string description;
        public ASHRecipeCategory category = ASHRecipeCategory.Materials;
        public List<ASHRecipeIngredient> ingredients = new List<ASHRecipeIngredient>();
        public ASHItemDefinition outputItem;
        [Min(1)] public int outputAmount = 1;
        [Tooltip("Optional icon when there is no output item (upgrade recipes).")]
        public Sprite icon;
        [Tooltip("Minimum Crafting skill level. 1 = no extra gate. Upgrade tiers should climb.")]
        [Range(1, 20)] public int requiredCraftingLevel = 1;
        [Tooltip("If set, crafting this applies a saved upgrade (e.g. ammo_pouch) instead of adding an item.")]
        public string upgradeId;
        [Tooltip("1-based tier applied when crafted. Must be exactly one above the current tier.")]
        [Range(0, 8)] public int upgradeTier = 0;
        public bool IsUpgrade => !string.IsNullOrEmpty(upgradeId) && upgradeTier > 0;
        [Tooltip("Seconds the craft takes (0 = instant).")]
        [Range(0f, 20f)] public float craftSeconds = 1.2f;
        public ASHToolType requiredTool = ASHToolType.None;
        public ASHCraftStation station = ASHCraftStation.Anywhere;
        [Tooltip("Water drawn from the refillable bottle, in % of a full bottle (e.g. 10 for a wet rag). 0 = none.")]
        [Range(0f, 100f)] public float waterPercent = 0f;
        [Tooltip("Campfire recipes: GAME minutes the food / water sits on the fire (1 game hour = 2 real minutes at the 48-minute day). " +
                 "Needs a lit fire with fuel the whole time. 0 = old timed craft in the pack.")]
        [Range(0f, 720f)] public float cookGameMinutes = 0f;
        [Tooltip("Campfire / stove cook menu: REAL seconds per item before the Cooking skill. 0 = ASH_CookingSettings default by type (food 5, meat 10, water 15).")]
        [Range(0f, 600f)] public float cookSeconds = 0f;
        public bool CooksOnFire => station == ASHCraftStation.Campfire && cookGameMinutes > 0f;
    }
}
