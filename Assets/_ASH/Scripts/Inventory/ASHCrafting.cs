using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Inventory
{
    /// <summary>
    /// Hand crafting. CanCraft/HasIngredients/TryCraft flow ported from Genesis CraftingManager
    /// (ingredients check, space check, consume, add output, OnCrafted); stations, discovery,
    /// progression and energy costs removed. Not a singleton: referenced explicitly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHCrafting : MonoBehaviour
    {
        [SerializeField] private ASHInventory inventory;
        [SerializeField] private List<ASHRecipe> recipes = new List<ASHRecipe>();

        public IReadOnlyList<ASHRecipe> Recipes => recipes;
        public bool IsCrafting { get; private set; }
        public float CraftProgress01 { get; private set; }
        public ASHRecipe CurrentRecipe { get; private set; }
        public event Action<ASHRecipe> Crafted;
        public event Action<ASHRecipe, string> CraftFailed;

        public bool HasIngredients(ASHRecipe r)
        {
            if (r == null || inventory == null) return false;
            foreach (var ing in r.ingredients)
                if (ing.item == null || inventory.Count(ing.item) < ing.amount) return false;
            return true;
        }

        public bool CanCraft(ASHRecipe r) =>
            !IsCrafting && r != null && r.outputItem != null && HasIngredients(r) && inventory.HasSpaceFor(r.outputItem, r.outputAmount);

        public bool TryCraft(ASHRecipe r)
        {
            if (!CanCraft(r))
            {
                CraftFailed?.Invoke(r, HasIngredients(r) ? "No space in pack" : "Missing materials");
                return false;
            }
            StartCoroutine(CraftRoutine(r));
            return true;
        }

        private IEnumerator CraftRoutine(ASHRecipe r)
        {
            IsCrafting = true; CurrentRecipe = r; CraftProgress01 = 0f;
            float t = 0f;
            while (t < r.craftSeconds)
            {
                t += Time.deltaTime;
                CraftProgress01 = r.craftSeconds <= 0f ? 1f : Mathf.Clamp01(t / r.craftSeconds);
                yield return null;
            }
            if (HasIngredients(r))
            {
                foreach (var ing in r.ingredients) inventory.Remove(ing.item, ing.amount);
                inventory.AddItem(r.outputItem, r.outputAmount);
                Crafted?.Invoke(r);
            }
            else CraftFailed?.Invoke(r, "Missing materials");
            IsCrafting = false; CurrentRecipe = null; CraftProgress01 = 0f;
        }
    }
}
