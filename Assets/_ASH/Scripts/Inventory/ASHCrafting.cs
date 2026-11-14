using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Inventory
{
    /// <summary>A lit heat source that enables Campfire-station recipes nearby (implemented by ASHCampfire).</summary>
    public interface IASHCraftStation
    {
        ASHCraftStation StationType { get; }
        bool StationActive { get; }
        Vector3 StationPosition { get; }
    }

    /// <summary>
    /// Hand crafting and break-down. CanCraft/HasIngredients/TryCraft flow ported from Genesis CraftingManager
    /// (ingredients check, space check, consume, add output, OnCrafted) and its station requirement, reduced to
    /// "near a lit campfire"; tool requirements and timed salvage (break down) added for ASH.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHCrafting : MonoBehaviour
    {
        public static readonly List<IASHCraftStation> Stations = new List<IASHCraftStation>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => Stations.Clear();

        [SerializeField] private ASHInventory inventory;
        [SerializeField] private List<ASHRecipe> recipes = new List<ASHRecipe>();
        [Range(1f, 10f)] [SerializeField] private float stationRange = 4f;

        public IReadOnlyList<ASHRecipe> Recipes => recipes;
        public ASHInventory Inventory => inventory;
        public bool IsCrafting { get; private set; }
        public float CraftProgress01 { get; private set; }
        public ASHRecipe CurrentRecipe { get; private set; }
        public ASHItemDefinition CurrentBreakdown { get; private set; }
        public event Action<ASHRecipe> Crafted;
        public event Action<ASHRecipe, string> CraftFailed;
        /// <summary>(item broken down, results text)</summary>
        public event Action<ASHItemDefinition, string> BrokeDown;
        public event Action<ASHItemDefinition, string> BreakdownFailed;

        public bool HasIngredients(ASHRecipe r)
        {
            if (r == null || inventory == null) return false;
            foreach (var ing in r.ingredients)
                if (ing.item == null || inventory.Count(ing.item) < ing.amount) return false;
            return true;
        }

        public bool NearStation(ASHCraftStation type)
        {
            if (type == ASHCraftStation.Anywhere) return true;
            foreach (var s in Stations)
                if (s != null && s.StationType == type && s.StationActive && (s.StationPosition - transform.position).sqrMagnitude <= stationRange * stationRange) return true;
            return false;
        }

        /// <summary>Why a recipe can't be crafted right now (null = craftable).</summary>
        public string MissingReason(ASHRecipe r)
        {
            if (r == null || r.outputItem == null) return "Invalid recipe";
            if (IsCrafting) return "Busy";
            if (!HasIngredients(r)) return "Missing materials";
            if (r.requiredTool != ASHToolType.None && !inventory.HasTool(r.requiredTool)) return "Needs " + ToolName(r.requiredTool);
            if (!NearStation(r.station)) return "Needs a lit campfire nearby";
            if (!inventory.HasSpaceFor(r.outputItem, r.outputAmount)) return "No space in pack";
            return null;
        }

        public bool CanCraft(ASHRecipe r) => MissingReason(r) == null;

        public bool TryCraft(ASHRecipe r)
        {
            string why = MissingReason(r);
            if (why != null) { CraftFailed?.Invoke(r, why); return false; }
            StartCoroutine(CraftRoutine(r));
            return true;
        }

        private IEnumerator CraftRoutine(ASHRecipe r)
        {
            IsCrafting = true; CurrentRecipe = r; CraftProgress01 = 0f;
            yield return Progress(r.craftSeconds);
            if (HasIngredients(r))
            {
                foreach (var ing in r.ingredients) inventory.Remove(ing.item, ing.amount);
                inventory.AddItem(r.outputItem, r.outputAmount);
                Crafted?.Invoke(r);
            }
            else CraftFailed?.Invoke(r, "Missing materials");
            IsCrafting = false; CurrentRecipe = null; CraftProgress01 = 0f;
        }

        private IEnumerator Progress(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; CraftProgress01 = seconds <= 0f ? 1f : Mathf.Clamp01(t / seconds); yield return null; }
            CraftProgress01 = 1f;
        }

        // ------------------------------------------------------------------ break down
        public string BreakdownBlockReason(ASHItemDefinition item)
        {
            if (item == null || !item.CanBreakDown) return "Can't be broken down";
            if (IsCrafting) return "Busy";
            if (inventory.Count(item) <= 0) return "Not carried";
            if (item.breakdownToolRequired && !inventory.HasTool(item.breakdownTool)) return "Needs " + ToolName(item.breakdownTool);
            return null;
        }

        /// <summary>Yield multiplier for breaking an item down with the current tools.</summary>
        public float BreakdownYieldFactor(ASHItemDefinition item) =>
            item.breakdownTool == ASHToolType.None || inventory.HasTool(item.breakdownTool) ? 1f : item.breakdownNoToolYield;

        public float BreakdownSeconds(ASHItemDefinition item) =>
            item.breakdownSeconds * (item.breakdownTool == ASHToolType.None || inventory.HasTool(item.breakdownTool) ? 1f : 1.75f);

        public bool TryBreakDown(ASHItemDefinition item)
        {
            string why = BreakdownBlockReason(item);
            if (why != null) { BreakdownFailed?.Invoke(item, why); return false; }
            StartCoroutine(BreakdownRoutine(item));
            return true;
        }

        private IEnumerator BreakdownRoutine(ASHItemDefinition item)
        {
            IsCrafting = true; CurrentBreakdown = item; CraftProgress01 = 0f;
            float factor = BreakdownYieldFactor(item);
            yield return Progress(BreakdownSeconds(item));
            if (inventory.Remove(item, 1))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var y in item.breakdownYield)
                {
                    if (y.item == null) continue;
                    int n = Mathf.Max(1, Mathf.FloorToInt(y.amount * factor));
                    int added = inventory.AddItem(y.item, n);
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"{added} {y.item.displayName}");
                }
                BrokeDown?.Invoke(item, sb.ToString());
            }
            IsCrafting = false; CurrentBreakdown = null; CraftProgress01 = 0f;
        }

        public static string ToolName(ASHToolType t)
        {
            switch (t)
            {
                case ASHToolType.Axe: return "a hatchet";
                case ASHToolType.Knife: return "a knife";
                case ASHToolType.CanOpener: return "a can opener";
                case ASHToolType.GPS: return "the GPS";
                default: return "a tool";
            }
        }
    }
}
