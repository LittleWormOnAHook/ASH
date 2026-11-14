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

    /// <summary>
    /// Crafting recipe. Same shape as Genesis Project.Crafting.RecipeDefinition (id, name, description,
    /// ingredients, output, amount); station type, level gates and blueprint discovery removed.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Inventory/Recipe", fileName = "ASH_Recipe_New")]
    public sealed class ASHRecipe : ScriptableObject
    {
        public string recipeId = "recipe";
        public string displayName = "New Recipe";
        [TextArea(2, 3)] public string description;
        public List<ASHRecipeIngredient> ingredients = new List<ASHRecipeIngredient>();
        public ASHItemDefinition outputItem;
        [Min(1)] public int outputAmount = 1;
        [Tooltip("Seconds the craft takes (0 = instant).")]
        [Range(0f, 10f)] public float craftSeconds = 1.2f;
    }
}
