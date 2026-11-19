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
        [Tooltip("Seconds the craft takes (0 = instant).")]
        [Range(0f, 20f)] public float craftSeconds = 1.2f;
        public ASHToolType requiredTool = ASHToolType.None;
        public ASHCraftStation station = ASHCraftStation.Anywhere;
        [Tooltip("Water drawn from the refillable bottle, in % of a full bottle (e.g. 10 for a wet rag). 0 = none.")]
        [Range(0f, 100f)] public float waterPercent = 0f;
        [Tooltip("Campfire recipes: GAME minutes the food / water sits on the fire (1 game hour = 2 real minutes at the 48-minute day). " +
                 "Needs a lit fire with fuel the whole time. 0 = old timed craft in the pack.")]
        [Range(0f, 720f)] public float cookGameMinutes = 0f;
        public bool CooksOnFire => station == ASHCraftStation.Campfire && cookGameMinutes > 0f;
    }
}
