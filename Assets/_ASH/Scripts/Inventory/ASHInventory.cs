using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Inventory
{
    /// <summary>
    /// Small fixed-slot stack inventory. Add/stack/remove/count logic ported from Genesis
    /// Project.Inventory.InventorySystem (AddItem two-pass stack-then-empty, RemoveItem across stacks);
    /// equipment, ammo, hotbar, storage-row unlocks and level gates removed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHInventory : MonoBehaviour
    {
        [Serializable]
        public sealed class Slot
        {
            public ASHItemDefinition item;
            public int amount;
            [Tooltip("Condition 0-1 for single (maxStack 1) items such as filter cartridges. 1 = new.")]
            public float condition = 1f;
            public bool IsEmpty => item == null || amount <= 0;
            public void Clear() { item = null; amount = 0; condition = 1f; }
        }

        [Tooltip("Pack slots. Never fewer than MinPackSlots at runtime (older scenes/prefabs saved 12 or 22).")]
        [Range(4, 60)] [SerializeField] private int slotCount = MinPackSlots;
        /// <summary>Pack size floor: the original 12, plus 10, plus 10 more. Older saved layouts are migrated up to this.</summary>
        public const int MinPackSlots = 32;
        /// <summary>Hotbar slots [0, Hot): their own slots, not part of the pack grid. An item on the hotbar is not in the pack.</summary>
        int Hot => Mathf.Clamp(hotbarSize, 0, 10);
        /// <summary>End of the hotbar + pack range (the cross bar follows): hotbar, then the pack grid (serialized slotCount, raised to MinPackSlots).
        /// Older layouts had the hotbar as the pack's top row, so their slots [0, hotbar) simply stay on the hotbar.</summary>
        int Pack => Hot + Mathf.Max(slotCount, MinPackSlots);
        /// <summary>First pack-grid slot (after the hotbar).</summary>
        public int PackStart => Hot;
        /// <summary>Pack-grid slots (hotbar and cross bar excluded).</summary>
        public int PackSlotCount => Pack - Hot;
        public int HotbarCount => Hot;
        public bool IsPack(int i) => i >= Hot && i < Pack;
        [SerializeField] private List<Slot> slots = new List<Slot>();

        /// <summary>Slots are created on first access so UI built before this Awake still sees them.</summary>
        public IReadOnlyList<Slot> Slots { get { EnsureSlots(); return slots; } }
        public event Action Changed;
        /// <summary>Raised for every successful add (item, amount actually added).</summary>
        public event Action<ASHItemDefinition, int> ItemAdded;
        public event Action<ASHItemDefinition> ItemConsumed;

        private void Awake() => EnsureSlots();

        private void EnsureSlots()
        {
            if (slots.Count == TotalSlots) return;
            ResizeSlots();
        }

        /// <summary>Resizes the pack while keeping the cross bar (the last CrossCount slots of the old layout) on the cross bar.
        /// Pack items beyond a smaller pack move into free pack slots; anything that still doesn't fit is reported.</summary>
        private void ResizeSlots()
        {
            var old = new List<Slot>(slots);
            int oldPack = old.Count >= CrossCount ? old.Count - CrossCount : old.Count;
            var pack = new List<Slot>(); var cross = new List<Slot>(); var spill = new List<Slot>();
            for (int i = 0; i < oldPack; i++) (i < Pack ? pack : spill).Add(old[i] ?? new Slot());
            for (int i = oldPack; i < old.Count; i++) cross.Add(old[i] ?? new Slot());
            while (pack.Count < Pack) pack.Add(new Slot());
            while (cross.Count < CrossCount) cross.Add(new Slot());
            foreach (var s in spill)
            {
                if (s.IsEmpty) continue;
                int free = pack.FindIndex(p => p.IsEmpty);
                if (free >= 0) pack[free] = s; else Debug.LogWarning($"[ASH] Inventory resize: no room for {s.item.displayName} x{s.amount}");
            }
            slots.Clear(); slots.AddRange(pack); slots.AddRange(cross);
        }

        /// <summary>Key items stay in the pack (only Move works on them): the keyItem flag, or the Survival Guide's journal item.</summary>
        public static bool IsKeyItem(ASHItemDefinition it) => it && (it.keyItem || (ASHFieldManualData.I && ASHFieldManualData.I.journalItem == it));

        // ---------------------------------------------------------------- drop
        /// <summary>Takes up to amount from slot i out of the inventory (for dropping). Returns the item and how many were taken.</summary>
        public int TakeFromSlot(int i, int amount, out ASHItemDefinition item)
        {
            EnsureSlots(); item = null;
            if (i < 0 || i >= slots.Count || slots[i].IsEmpty || amount <= 0) return 0;
            if (IsKeyItem(slots[i].item)) return 0;
            item = slots[i].item; LastTakenCondition = slots[i].condition; int take = Mathf.Min(amount, slots[i].amount);
            slots[i].amount -= take; if (slots[i].amount <= 0) slots[i].Clear();
            Changed?.Invoke();
            return take;
        }

        /// <summary>Condition of the slot last emptied by TakeFromSlot (drops keep a filter's condition).</summary>
        public float LastTakenCondition { get; private set; } = 1f;

        /// <summary>Adds single (maxStack 1) items that carry a condition (0-1) into empty pack slots. Returns how many were added.</summary>
        public int AddItemWithCondition(ASHItemDefinition item, int amount, float condition)
        {
            if (item == null || amount <= 0) return 0;
            if (StackLimit(item) > 1) return AddItem(item, amount);
            EnsureSlots(); int added = 0;
            if (crossFirst)
                for (int q = 0; q < CrossCount && added < amount; q++)
                {
                    int ci = CrossIndex((CrossQuad)q);
                    if (slots[ci].IsEmpty && CrossAccepts((CrossQuad)q, item)) { slots[ci].item = item; slots[ci].amount = 1; slots[ci].condition = Mathf.Clamp01(condition); added++; }
                }
            for (int i = Hot; i < Pack && added < amount; i++)
                if (slots[i].IsEmpty) { slots[i].item = item; slots[i].amount = 1; slots[i].condition = Mathf.Clamp01(condition); added++; }
            if (added > 0) { Changed?.Invoke(); ItemAdded?.Invoke(item, added); }
            return added;
        }

        /// <summary>Save restore: writes a slot directly (no events; call NotifyChanged after).</summary>
        public void RestoreSlot(int i, ASHItemDefinition item, int amount, float condition)
        {
            EnsureSlots(); if (i < 0 || i >= slots.Count) return;
            if (item == null || amount <= 0) { slots[i].Clear(); return; }
            slots[i].item = item; slots[i].amount = amount; slots[i].condition = Mathf.Clamp01(condition);
        }
        public void NotifyChanged() => Changed?.Invoke();
        /// <summary>Removes one item from a specific slot (keeps the other stacks). Raises Changed.</summary>
        public bool RemoveFromSlot(int i, int amount = 1)
        {
            EnsureSlots(); if (i < 0 || i >= slots.Count || slots[i].IsEmpty || slots[i].amount < amount || IsKeyItem(slots[i].item)) return false;
            slots[i].amount -= amount; if (slots[i].amount <= 0) slots[i].Clear(); Changed?.Invoke(); return true;
        }

        // ---------------------------------------------------------------- cross bar (Genesis Hot Cross, adapted)
        /// <summary>Cross bar quadrants, stored as 4 extra slots after the pack: [CrossStart + quadrant].</summary>
        public enum CrossQuad { Weapon = 0, Ammo = 1, Light = 2, Food = 3 }
        public const int CrossCount = 4;
        public int CrossStart => Pack;
        public int TotalSlots => Pack + CrossCount;
        public int CrossIndex(CrossQuad q) => Pack + (int)q;
        public bool IsCross(int i) => i >= Pack && i < TotalSlots;
        public bool IsHotbar(int i) => i >= 0 && i < Hot;
        /// <summary>Hotbar or cross bar slot (quick slots hold items outside the pack).</summary>
        public bool IsQuick(int i) => IsHotbar(i) || IsCross(i);

        /// <summary>Moves a hotbar / cross bar stack back into the pack (merging into matching stacks first, then empty pack slots).
        /// Returns how many did NOT fit (still in the quick slot; the caller drops them). -1 when the slot isn't a quick slot or is empty.</summary>
        public int ReturnToPack(int i)
        {
            EnsureSlots();
            if (!IsQuick(i) || i >= slots.Count || slots[i].IsEmpty) return -1;
            var s = slots[i]; var item = s.item;
            for (int k = Hot; k < Pack && s.amount > 0; k++)
            {
                var d = slots[k]; if (d.item != item || d.amount >= StackLimit(item) || StackLimit(item) <= 1) continue;
                int add = Mathf.Min(s.amount, StackLimit(item) - d.amount); d.amount += add; s.amount -= add;
            }
            if (s.amount > 0)
                for (int k = Hot; k < Pack; k++)
                    if (slots[k].IsEmpty) { slots[k] = s; slots[i] = new Slot(); break; }
            int left = slots[i].IsEmpty ? 0 : slots[i].amount;
            if (left == 0) slots[i].Clear();
            Changed?.Invoke();
            return left;
        }
        public ASHInventory.Slot CrossSlot(CrossQuad q) { EnsureSlots(); return slots[CrossIndex(q)]; }

        /// <summary>Slot category rules: pack and hotbar take anything; each cross quadrant takes one kind.</summary>
        public bool Accepts(int i, ASHItemDefinition item)
        {
            if (item == null) return true;
            if (!IsCross(i)) return true;
            return CrossAccepts((CrossQuad)(i - Pack), item);
        }

        public static bool CrossAccepts(CrossQuad q, ASHItemDefinition item)
        {
            if (item == null) return true;
            switch (q)
            {
                case CrossQuad.Weapon: return item.IsWeapon || item.toolType == ASHToolType.Axe;
                case CrossQuad.Ammo: return item.category == ASHItemCategory.Ammo;
                case CrossQuad.Light: return item.IsLightSource;
                case CrossQuad.Food: return item.IsConsumable;
            }
            return false;
        }

        public static string CrossLabel(CrossQuad q) => q == CrossQuad.Weapon ? "Weapon" : q == CrossQuad.Ammo ? "Ammo" : q == CrossQuad.Light ? "Light" : "Food";

        /// <summary>The axe only counts as a usable tool from the hotbar, the cross bar, or while held.</summary>
        public bool axeNeedsQuickSlot = true;
        /// <summary>Set by ASHEquipment: true while an item is held.</summary>
        public Func<ASHItemDefinition, bool> IsHeld;
        bool QuickOrHeld(int i) => IsHotbar(i) || IsCross(i) || (IsHeld != null && IsHeld(slots[i].item));

        [Tooltip("New items fill a matching empty cross bar slot before the pack.")]
        public bool crossFirst = true;

        public int AddItem(ASHItemDefinition item, int amount = 1)
        {
            if (item == null || amount <= 0) return 0;
            EnsureSlots();
            int remaining = amount;
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                if (slots[i].item != item || slots[i].amount >= StackLimit(item)) continue;
                int add = Mathf.Min(remaining, StackLimit(item) - slots[i].amount);
                slots[i].amount += add; remaining -= add;
            }
            // pickups go to a matching empty cross bar slot first (weapon / ammo / light / food), then the pack
            if (crossFirst && remaining > 0)
                for (int q = 0; q < CrossCount && remaining > 0; q++)
                {
                    int ci = CrossIndex((CrossQuad)q);
                    if (!slots[ci].IsEmpty || !CrossAccepts((CrossQuad)q, item)) continue;
                    int add = Mathf.Min(remaining, StackLimit(item));
                    slots[ci].item = item; slots[ci].amount = add; slots[ci].condition = 1f; remaining -= add;
                }
            for (int i = Hot; i < Pack && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty) continue;
                int add = Mathf.Min(remaining, StackLimit(item));
                slots[i].item = item; slots[i].amount = add; remaining -= add;
            }
            int added = amount - remaining;
            if (added > 0) { Changed?.Invoke(); ItemAdded?.Invoke(item, added); }
            return added;
        }

        public bool HasSpaceFor(ASHItemDefinition item, int amount)
        {
            if (item == null || amount <= 0) return false;
            int free = 0;
            EnsureSlots();
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s.IsEmpty) { if (IsPack(i) || (crossFirst && IsCross(i) && Accepts(i, item))) free += StackLimit(item); }
                else if (s.item == item) free += StackLimit(item) - s.amount;
                if (free >= amount) return true;
            }
            return false;
        }

        public int Count(ASHItemDefinition item)
        {
            if (item == null) return 0;
            int n = 0;
            foreach (var s in slots) if (s.item == item) n += s.amount;
            return n;
        }

        /// <summary>Runtime stack cap (StackLimit(item) plus ammo-pouch bonuses). Use this instead of the asset field.</summary>
        public static int StackLimit(ASHItemDefinition item) => ASHUpgrades.StackLimit(item);

        public bool Remove(ASHItemDefinition item, int amount = 1)
        {
            if (item == null || amount <= 0 || Count(item) < amount || IsKeyItem(item)) return false;
            int remaining = amount;
            for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
            {
                if (slots[i].item != item) continue;
                int take = Mathf.Min(remaining, slots[i].amount);
                slots[i].amount -= take; remaining -= take;
                if (slots[i].amount <= 0) slots[i].Clear();
            }
            Changed?.Invoke();
            return true;
        }

        public int SlotCount => Pack;
        /// <summary>Slots [0, HotbarSize) are the always-visible hotbar (keys 1..N), separate from the pack grid.</summary>
        [Range(0, 10)] public int hotbarSize = 6;

        /// <summary>Moves/merges slot a onto slot b (stacks merge when the item matches, otherwise they swap).</summary>
        public bool Move(int a, int b)
        {
            EnsureSlots();
            if (a == b || a < 0 || b < 0 || a >= slots.Count || b >= slots.Count || slots[a].IsEmpty) return false;
            var sa = slots[a]; var sb = slots[b];
            if (!Accepts(b, sa.item)) { LastMoveRefused = b; return false; }
            bool merge = !sb.IsEmpty && sb.item == sa.item && sb.amount < StackLimit(sb.item);
            if (!merge && !sb.IsEmpty && !Accepts(a, sb.item)) { LastMoveRefused = a; return false; }
            LastMoveRefused = -1;
            if (!sb.IsEmpty && sb.item == sa.item && sb.amount < StackLimit(sb.item))
            {
                int add = Mathf.Min(sa.amount, StackLimit(sb.item) - sb.amount);
                sb.amount += add; sa.amount -= add; if (sa.amount <= 0) sa.Clear();
            }
            else { slots[a] = sb; slots[b] = sa; }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Slot whose category rule refused the last Move, or -1.</summary>
        public int LastMoveRefused { get; private set; } = -1;

        /// <summary>Splits half of a stack into the first empty pack slot.</summary>
        public bool Split(int i)
        {
            EnsureSlots();
            if (i < 0 || i >= slots.Count || slots[i].IsEmpty || slots[i].amount < 2 || IsKeyItem(slots[i].item)) return false;
            int empty = -1; for (int k = Hot; k < Pack; k++) if (slots[k].IsEmpty) { empty = k; break; } if (empty < 0) return false;
            int half = slots[i].amount / 2;
            slots[empty].item = slots[i].item; slots[empty].amount = half; slots[i].amount -= half;
            Changed?.Invoke();
            return true;
        }

        /// <summary>True when any carried item fills this tool role.</summary>
        public bool HasTool(ASHToolType tool)
        {
            if (tool == ASHToolType.None) return true;
            var all = Slots;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s.IsEmpty || s.item.toolType != tool) continue;
                if (tool == ASHToolType.Axe && axeNeedsQuickSlot && !QuickOrHeld(i)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Tools that open cans cleanly (can opener, knife, hatchet), as in survival-game convention.</summary>
        public bool HasOpener => HasTool(ASHToolType.CanOpener) || HasTool(ASHToolType.Knife) || HasTool(ASHToolType.Axe);

        /// <summary>Starting kit, added once on Start (e.g. the damaged GPS).</summary>
        [SerializeField] private List<ASHRecipeIngredient> startingItems = new List<ASHRecipeIngredient>();
        [Tooltip("Off: the GPS and Grandpa's journal are never starting items (both are found as pickups), even if listed above.")]
        public bool startWithGpsAndJournal = false;
        private bool _startingGiven;
        private void Start()
        {
            if (_startingGiven) return; _startingGiven = true;
            foreach (var s in startingItems)
            {
                if (s.item == null || Count(s.item) > 0) continue;
                if (!startWithGpsAndJournal && (s.item.toolType == ASHToolType.GPS || (ASHFieldManualData.I && ASHFieldManualData.I.journalItem == s.item))) continue;
                AddItemSilently(s.item, s.amount);
            }
        }

        /// <summary>Removes every stack of an item, key items included (the read journal becomes a permanent tool). Returns how many.</summary>
        public int RemoveAll(ASHItemDefinition item)
        {
            if (item == null) return 0; EnsureSlots(); int n = 0;
            foreach (var s in slots) if (!s.IsEmpty && s.item == item) { n += s.amount; s.Clear(); }
            if (n > 0) Changed?.Invoke();
            return n;
        }

        private void AddItemSilently(ASHItemDefinition item, int amount)
        {
            EnsureSlots();
            for (int i = Hot; i < Pack && amount > 0; i++)
                if (slots[i].IsEmpty) { int add = Mathf.Min(amount, StackLimit(item)); slots[i].item = item; slots[i].amount = add; amount -= add; }
            Changed?.Invoke();
        }

        /// <summary>Last consume result for UI messages: true when a can was smashed open (spilled).</summary>
        public bool LastConsumeSmashed { get; private set; }

        /// <summary>Consumes one unit through the consumer (survival stats). Cans without an opener are smashed open:
        /// reduced restores and no empty tin returned.</summary>
        public bool TryConsume(ASHItemDefinition item, IASHConsumer consumer) => TryConsume(item, consumer, -1);

        /// <summary>As TryConsume, taking the unit from slot fromSlot when it holds the item (hotbar / cross bar use eats from that stack).</summary>
        public bool TryConsume(ASHItemDefinition item, IASHConsumer consumer, int fromSlot)
        {
            if (item == null || !item.IsConsumable || consumer == null || Count(item) <= 0) return false;
            bool smashed = item.requiresOpener && !HasOpener;
            LastConsumeSmashed = smashed;
            consumer.Consume(item, smashed ? item.smashedYield : 1f);
            if (!(fromSlot >= 0 && fromSlot < slots.Count && slots[fromSlot].item == item && RemoveFromSlot(fromSlot, 1))) Remove(item, 1);
            if (item.leftoverItem != null && !smashed) AddItem(item.leftoverItem, 1);
            ItemConsumed?.Invoke(item);
            return true;
        }

        public void ClearAll() { foreach (var s in slots) s.Clear(); Changed?.Invoke(); }
    }

    /// <summary>Anything that can apply a consumable's restores (implemented by survival stats).</summary>
    public interface IASHConsumer { void Consume(ASHItemDefinition item, float factor = 1f); }
}
