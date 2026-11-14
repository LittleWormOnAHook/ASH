using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>Drinkable water (old stock tank). Hold to drink; restores thirst.</summary>
    public sealed class ASHWaterSource : MonoBehaviour, IASHUsable
    {
        [SerializeField] private string label = "stock tank";
        [Range(1f, 100f)] [SerializeField] private float thirstPerDrink = 25f;
        [Range(0.2f, 5f)] [SerializeField] private float holdSeconds = 1.4f;

        public Transform UseTransform => transform;
        public float GetUsePriority(in ASHUseContext c) => c.Stats == null ? -1f : 40f;
        public float GetHoldDuration(in ASHUseContext c) => holdSeconds;
        public string GetPrompt(in ASHUseContext c) =>
            c.Stats != null && c.Stats.Thirst >= c.Stats.MaxThirst - 0.5f ? $"Not thirsty ({label})" : $"Drink from {label}";

        public bool TryUse(in ASHUseContext c)
        {
            if (c.Stats == null || c.Stats.Thirst >= c.Stats.MaxThirst - 0.5f) return false;
            c.Stats.Drink(thirstPerDrink);
            return true;
        }
    }
}
