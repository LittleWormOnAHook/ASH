using ASH.Inventory;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>Tap-to-pick-up world item. Lite port of Genesis ItemPickup (pickup + destroy, no loot rolls/XP).</summary>
    public sealed class ASHItemPickup : MonoBehaviour, IASHUsable
    {
        [SerializeField] private ASHItemDefinition item;
        [Range(1, 50)] [SerializeField] private int amount = 1;

        public Transform UseTransform => transform;
        public void Configure(ASHItemDefinition def, int count) { item = def; amount = count; }

        public float GetUsePriority(in ASHUseContext c) => item == null ? -1f : 60f;
        public float GetHoldDuration(in ASHUseContext c) => 0f;
        public string GetPrompt(in ASHUseContext c) => item == null ? null : amount > 1 ? $"Pick up {item.displayName} x{amount}" : $"Pick up {item.displayName}";

        public bool TryUse(in ASHUseContext c)
        {
            if (item == null || c.Inventory == null) return false;
            int added = c.Inventory.AddItem(item, amount);
            amount -= added;
            if (amount <= 0) Destroy(gameObject);
            return added > 0;
        }
    }
}
