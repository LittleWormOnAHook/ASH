using ASH.Gameplay;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>Tap-to-pick-up world item. Lite port of Genesis ItemPickup (pickup + destroy, no loot rolls/XP).</summary>
    public sealed class ASHItemPickup : MonoBehaviour, IASHUsable
    {
        [SerializeField] private ASHItemDefinition item;
        [Range(1, 50)] [SerializeField] private int amount = 1;
        [Tooltip("If max is at least min and greater than 0, the amount is rolled in this inclusive range when the pickup wakes (world deer-skin drops, later hunting).")]
        [SerializeField] private int amountMin, amountMax;
        [Tooltip("0-1 skill factor that shifts the roll toward the max (0 = a plain min-max roll). Wildlife kills should pass hunting or weapon skill.")]
        [Range(0f, 1f)] [SerializeField] private float harvestSkill01;
        bool _yieldRolled;
        [Tooltip("Condition 0-1 of single items (filter cartridges): 1 = new.")]
        [Range(0f, 1f)] [SerializeField] private float condition = 1f;
        [Tooltip("Roll a random condition when the scene starts (weighted distribution from the item's condition settings, e.g. gas mask filters found in the world).")]
        [SerializeField] private bool rollCondition;

        /// <summary>Set by systems that own a condition distribution (gas mask filters). Returns -1 when the item has none.</summary>
        public static System.Func<ASHItemDefinition, float> ConditionRoller;
        /// <summary>Raised after a successful pickup (item, amount taken).</summary>
        public static event System.Action<ASHItemPickup, ASHItemDefinition, int> PickedUp;

        public Transform UseTransform => transform;
        public void Configure(ASHItemDefinition def, int count) { item = def; amount = count; amountMin = amountMax = 0; _yieldRolled = true; }
        public void ConfigureRange(ASHItemDefinition def, int min, int max, float skill01 = 0f)
        {
            item = def; amountMin = min; amountMax = max; harvestSkill01 = skill01; _yieldRolled = false;
            if (max >= min && max > 0) amount = Mathf.Max(min, 1);
        }
        public void SetCondition(float c) { condition = Mathf.Clamp01(c); rollCondition = false; }
        public void SetRollCondition(bool on) { rollCondition = on; }
        public ASHItemDefinition Item => item;
        public int Amount => amount;
        public float Condition => condition;

        void Awake() { RollYield(); }

        void RollYield()
        {
            if (_yieldRolled) return;
            if (amountMax < 1 || amountMax < amountMin) return;
            amount = ASHHarvest.Yield(amountMin, amountMax, harvestSkill01);
            _yieldRolled = true;
        }

        void Start()
        {
            RollYield();
            if (!rollCondition || ConditionRoller == null || item == null) return;
            float c = ConditionRoller(item); if (c >= 0f) condition = Mathf.Clamp01(c);
            rollCondition = false;
        }

        public float GetUsePriority(in ASHUseContext c) => item == null ? -1f : 60f;
        public float GetHoldDuration(in ASHUseContext c) => 0f;
        public string GetPrompt(in ASHUseContext c) => item == null ? null : amount > 1 ? $"Pick up {item.displayName} x{amount}" : $"Pick up {item.displayName}";

        public bool TryUse(in ASHUseContext c)
        {
            if (item == null || c.Inventory == null) return false;
            int added = item.maxStack == 1 && condition < 0.999f ? c.Inventory.AddItemWithCondition(item, amount, condition) : c.Inventory.AddItem(item, amount);
            amount -= added;
            if (added > 0) PickedUp?.Invoke(this, item, added);
            if (amount <= 0) Destroy(gameObject);
            return added > 0;
        }
    }
}
