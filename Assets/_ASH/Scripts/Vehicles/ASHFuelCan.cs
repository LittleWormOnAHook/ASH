using ASH.Interaction;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>Fuel cans (t362): an item with containerLitres &gt; 0. The slot's condition is how full it is (0..1), so each can keeps its
    /// own level; empty cans stay in the pack. Shows the litres on the slot and in the tooltip.</summary>
    public static class ASHFuelCan
    {
        public static bool IsCan(ASHItemDefinition it) => it && it.containerLitres > 0f;
        public static float Litres(ASHInventory.Slot s) => s == null || s.IsEmpty || !IsCan(s.item) ? 0f : Mathf.Clamp01(s.condition) * s.item.containerLitres * s.amount;
        public static float TotalLitres(ASHInventory inv) { float l = 0f; if (inv) foreach (var s in inv.Slots) l += Litres(s); return l; }

        /// <summary>Pours up to 'want' litres out of the cans in the pack (fullest last, so part-empty cans drain first). Returns litres poured.</summary>
        public static float Pour(ASHInventory inv, float want)
        {
            if (!inv || want <= 0f) return 0f; float got = 0f;
            foreach (var s in inv.Slots)
            {
                if (want - got <= 1e-4f) break;
                if (s == null || s.IsEmpty || !IsCan(s.item) || s.condition <= 0f) continue;
                float cap = s.item.containerLitres, have = s.condition * cap, take = Mathf.Min(have, want - got);
                s.condition = Mathf.Clamp01((have - take) / cap); got += take;
            }
            if (got > 0f) inv.NotifyChanged();
            return got;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ASHHud.SlotBadge = s => s != null && !s.IsEmpty && IsCan(s.item) ? $"{Litres(s):0}L" : null;
            ASHHud.TipExtra = s => s != null && !s.IsEmpty && IsCan(s.item)
                ? $"<color=#e0b36a>Fuel  {Litres(s):0.0} / {s.item.containerLitres:0} L</color>  <color=#a59e93>· E on a vehicle → Fuel Up</color>" : null;
        }
    }

}
