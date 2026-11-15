using ASH.Inventory;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>Drinkable water (old stock tank). Hold to drink; restores thirst.</summary>
    public sealed class ASHWaterSource : MonoBehaviour, IASHUsable
    {
        [SerializeField] private string label = "stock tank";
        [Range(1f, 100f)] [SerializeField] private float thirstPerDrink = 25f;
        [Range(0.2f, 5f)] [SerializeField] private float holdSeconds = 1.4f;

        [Header("Fill containers (when not thirsty)")]
        [SerializeField] private ASHItemDefinition emptyContainer;
        [SerializeField] private ASHItemDefinition filledContainer;

        public Transform UseTransform => transform;
        public void ConfigureFill(ASHItemDefinition empty, ASHItemDefinition filled) { emptyContainer = empty; filledContainer = filled; }
        public float GetUsePriority(in ASHUseContext c) => c.Stats == null ? -1f : 40f;
        public float GetHoldDuration(in ASHUseContext c) => holdSeconds;

        bool Thirsty(in ASHUseContext c) => c.Stats != null && c.Stats.Thirst < c.Stats.MaxThirst - 0.5f;
        bool CanFill(in ASHUseContext c) => emptyContainer && filledContainer && c.Inventory != null && c.Inventory.Count(emptyContainer) > 0;

        /// <summary>Grandpa's refillable bottle (ASH.Survival.ASHFaceProtection): refill when it isn't full.</summary>
        public static System.Func<bool> BottleNeedsRefill;
        public static System.Action<ASHWaterSource> RefillBottle;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetHooks() { BottleNeedsRefill = null; RefillBottle = null; }
        bool CanRefill => BottleNeedsRefill != null && RefillBottle != null && BottleNeedsRefill();

        public string GetPrompt(in ASHUseContext c)
        {
            if (Thirsty(c)) return $"Drink from {label}";
            if (CanRefill) return $"Refill water bottle ({label})";
            if (CanFill(c)) return $"Fill {emptyContainer.displayName} ({label})";
            return $"Not thirsty ({label})";
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (Thirsty(c)) { c.Stats.Drink(thirstPerDrink); return true; }
            if (CanRefill) { RefillBottle(this); return true; }
            if (CanFill(c) && c.Inventory.Remove(emptyContainer, 1)) { c.Inventory.AddItem(filledContainer, 1); return true; }
            return false;
        }
    }
}
