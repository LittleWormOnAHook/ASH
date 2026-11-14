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
            public bool IsEmpty => item == null || amount <= 0;
            public void Clear() { item = null; amount = 0; }
        }

        [Range(4, 40)] [SerializeField] private int slotCount = 12;
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
            if (slots.Count == slotCount) return;
            ResizeSlots();
        }

        private void ResizeSlots()
        {
            while (slots.Count < slotCount) slots.Add(new Slot());
            if (slots.Count > slotCount) slots.RemoveRange(slotCount, slots.Count - slotCount);
        }

        public int AddItem(ASHItemDefinition item, int amount = 1)
        {
            if (item == null || amount <= 0) return 0;
            EnsureSlots();
            int remaining = amount;
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                if (slots[i].item != item || slots[i].amount >= item.maxStack) continue;
                int add = Mathf.Min(remaining, item.maxStack - slots[i].amount);
                slots[i].amount += add; remaining -= add;
            }
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty) continue;
                int add = Mathf.Min(remaining, item.maxStack);
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
            foreach (var s in slots)
            {
                if (s.IsEmpty) free += item.maxStack;
                else if (s.item == item) free += item.maxStack - s.amount;
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

        public bool Remove(ASHItemDefinition item, int amount = 1)
        {
            if (item == null || amount <= 0 || Count(item) < amount) return false;
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

        /// <summary>Consumes one unit of a consumable through the supplied consumer (survival stats).</summary>
        public bool TryConsume(ASHItemDefinition item, IASHConsumer consumer)
        {
            if (item == null || !item.IsConsumable || consumer == null || Count(item) <= 0) return false;
            consumer.Consume(item);
            Remove(item, 1);
            ItemConsumed?.Invoke(item);
            return true;
        }

        public int SlotCount => slotCount;
        /// <summary>Slots [0, HotbarSize) double as the always-visible hotbar (keys 1..N).</summary>
        [Range(0, 10)] public int hotbarSize = 6;

        /// <summary>Moves/merges slot a onto slot b (stacks merge when the item matches, otherwise they swap).</summary>
        public bool Move(int a, int b)
        {
            EnsureSlots();
            if (a == b || a < 0 || b < 0 || a >= slots.Count || b >= slots.Count || slots[a].IsEmpty) return false;
            var sa = slots[a]; var sb = slots[b];
            if (!sb.IsEmpty && sb.item == sa.item && sb.amount < sb.item.maxStack)
            {
                int add = Mathf.Min(sa.amount, sb.item.maxStack - sb.amount);
                sb.amount += add; sa.amount -= add; if (sa.amount <= 0) sa.Clear();
            }
            else { slots[a] = sb; slots[b] = sa; }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Splits half of a stack into the first empty slot.</summary>
        public bool Split(int i)
        {
            EnsureSlots();
            if (i < 0 || i >= slots.Count || slots[i].IsEmpty || slots[i].amount < 2) return false;
            int empty = slots.FindIndex(s => s.IsEmpty); if (empty < 0) return false;
            int half = slots[i].amount / 2;
            slots[empty].item = slots[i].item; slots[empty].amount = half; slots[i].amount -= half;
            Changed?.Invoke();
            return true;
        }

        public void ClearAll() { foreach (var s in slots) s.Clear(); Changed?.Invoke(); }
    }

    /// <summary>Anything that can apply a consumable's restores (implemented by survival stats).</summary>
    public interface IASHConsumer { void Consume(ASHItemDefinition item); }
}
