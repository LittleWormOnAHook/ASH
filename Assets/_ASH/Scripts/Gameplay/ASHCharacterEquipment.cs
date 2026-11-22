using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Survival;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Worn clothing / gear for the Character journal tab. Face is a view of ASHFaceProtection (rag / mask);
    /// other slots hold the item (removed from the pack while worn) and restore through the "equipment" save section.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class ASHCharacterEquipment : MonoBehaviour, IASHSaveable
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHCharacterEquipment()
        {
            Changed = null;
            Instance = default;
        }

        public const int AccessoryCount = 2;

        public static event Action Changed;
        public static ASHCharacterEquipment Instance { get; private set; }

        public enum Slot { Head, Face, Outerwear, Torso, Hands, Legs, Feet, Backpack, Accessory0, Accessory1 }

        [Serializable] public sealed class Worn
        {
            public ASHItemDefinition item;
            public float condition = 1f;
            public bool Empty => !item;
            public void Clear() { item = null; condition = 1f; }
        }

        readonly Worn[] _slots = new Worn[10];
        /// <summary>Normal face clothing (face wrap, goggles). The rag / gas mask are worn through ASHFaceProtection on top of it.</summary>
        readonly Worn _faceGear = new Worn();
        public Worn FaceGear => _faceGear;
        ASHInventory _inv;
        public string Message { get; private set; }

        public Worn this[Slot s] => _slots[(int)s] ?? (_slots[(int)s] = new Worn());
        public IReadOnlyList<Worn> All => _slots;

        void Awake()
        {
            Instance = this;
            for (int i = 0; i < _slots.Length; i++) if (_slots[i] == null) _slots[i] = new Worn();
            _inv = GetComponent<ASHInventory>();
            if (!_inv) _inv = GetComponentInParent<ASHInventory>();
            if (!_inv) _inv = FindAnyObjectByType<ASHInventory>();
        }

        void OnEnable()
        {
            Instance = this;
            ASHSaveSystem.Register(this);
        }

        void OnDisable()
        {
            ASHSaveSystem.Unregister(this);
            if (Instance == this) Instance = null;
        }

        public static ASHCharacterEquipment Ensure()
        {
            if (Instance) return Instance;
            var inv = FindAnyObjectByType<ASHInventory>();
            if (!inv) return null;
            var e = inv.GetComponent<ASHCharacterEquipment>();
            if (!e) e = inv.gameObject.AddComponent<ASHCharacterEquipment>();
            return e;
        }

        public static string Label(Slot s)
        {
            switch (s)
            {
                case Slot.Head: return "Head";
                case Slot.Face: return "Face";
                case Slot.Outerwear: return "Outerwear";
                case Slot.Torso: return "Torso";
                case Slot.Hands: return "Hands";
                case Slot.Legs: return "Legs";
                case Slot.Feet: return "Feet";
                case Slot.Backpack: return "Backpack";
                case Slot.Accessory0: return "Accessory 1";
                case Slot.Accessory1: return "Accessory 2";
                default: return s.ToString();
            }
        }

        public static bool IsAccessory(Slot s) => s == Slot.Accessory0 || s == Slot.Accessory1;

        public static bool Compatible(ASHItemDefinition item, Slot slot)
        {
            if (!item || item.wearSlot == ASHWearSlot.None) return false;
            if (slot == Slot.Face) return item.wearSlot == ASHWearSlot.Face || item.itemId == "wet_rag" || item.itemId == "gas_mask";
            if (IsAccessory(slot)) return item.wearSlot == ASHWearSlot.Accessory;
            return item.wearSlot == ToWear(slot);
        }

        public static ASHWearSlot ToWear(Slot s)
        {
            switch (s)
            {
                case Slot.Head: return ASHWearSlot.Head;
                case Slot.Face: return ASHWearSlot.Face;
                case Slot.Outerwear: return ASHWearSlot.Outerwear;
                case Slot.Torso: return ASHWearSlot.Torso;
                case Slot.Hands: return ASHWearSlot.Hands;
                case Slot.Legs: return ASHWearSlot.Legs;
                case Slot.Feet: return ASHWearSlot.Feet;
                case Slot.Backpack: return ASHWearSlot.Backpack;
                default: return ASHWearSlot.Accessory;
            }
        }

        public Worn View(Slot s)
        {
            if (s == Slot.Face) { var f = FaceView(); return f.Empty ? _faceGear : f; }
            return this[s];
        }

        /// <summary>Everything worn that can carry clothing stats (face gear included, the rag / mask view excluded).</summary>
        public IEnumerable<Worn> WornPieces()
        {
            for (int i = 0; i < _slots.Length; i++) { if ((Slot)i == Slot.Face) continue; var w = _slots[i]; if (w != null && w.item) yield return w; }
            if (_faceGear.item) yield return _faceGear;
        }

        static int Carry(ASHItemDefinition it) => it && it.clothing != null ? Mathf.Clamp(it.clothing.carrySlots, 0, ASHInventory.MaxBonusPackSlots) : 0;
        public int CarrySlots => Carry(this[Slot.Backpack].item);

        Worn FaceView()
        {
            var w = this[Slot.Face];
            w.Clear();
            var face = ASHFaceProtection.Instance;
            if (face == null) return w;
            if (face.Worn == ASHFaceProtection.Kind.Rag)
            {
                w.item = FindItem("wet_rag");
                float hours = ASHExposureSettings.I ? ASHExposureSettings.I.ragHours : 5f;
                w.condition = hours > 0.01f ? Mathf.Clamp01(face.RagHours / hours) : 1f;
            }
            else if (face.Worn == ASHFaceProtection.Kind.Mask)
            {
                w.item = FindItem("gas_mask");
                w.condition = 1f;
            }
            return w;
        }

        public bool TryEquip(ASHItemDefinition item, int packSlot, Slot slot)
        {
            Message = null;
            if (!item) { Message = "Nothing to wear."; return false; }
            if (!Compatible(item, slot)) { Message = $"{item.displayName} can't go in the {Label(slot)} slot."; return false; }

            if (slot == Slot.Face)
            {
                if (item.itemId != "wet_rag" && item.itemId != "gas_mask") return EquipInto(_faceGear, item, packSlot);
                var face = ASHFaceProtection.Instance;
                if (!face) { Message = "Can't wear that right now."; return false; }
                if (item.itemId == "wet_rag") { if (!face.WearRag(packSlot)) { Message = "You need a wet rag."; return false; } Notify(); return true; }
                if (item.itemId == "gas_mask")
                {
                    if (face.Worn == ASHFaceProtection.Kind.Mask) { Notify(); return true; }
                    if (!face.WearMask()) { Message = "You need a gas mask."; return false; }
                    Notify(); return true;
                }
                Message = "Only a wet rag or gas mask goes on the face.";
                return false;
            }

            if (slot == Slot.Backpack) return EquipBackpack(item, packSlot);
            return EquipInto(this[slot], item, packSlot);
        }

        int FindInPack(ASHItemDefinition item, int packSlot)
        {
            int i = packSlot;
            if (i < 0 || i >= _inv.Slots.Count || _inv.Slots[i].item != item)
            {
                i = -1;
                for (int s = 0; s < _inv.Slots.Count; s++) if (!_inv.Slots[s].IsEmpty && _inv.Slots[s].item == item) { i = s; break; }
            }
            return i;
        }

        bool EquipInto(Worn target, ASHItemDefinition item, int packSlot)
        {
            if (!_inv) { Message = "No pack."; return false; }
            int i = FindInPack(item, packSlot);
            if (i < 0) { Message = $"You're not carrying {item.displayName}."; return false; }
            float cond = _inv.Slots[i].condition;
            if (!_inv.RemoveFromSlot(i, 1)) { Message = "Couldn't take it from the pack."; return false; }
            var oldItem = target.item; float oldCond = target.condition;
            target.item = item; target.condition = cond;
            if (oldItem) ReturnOrDrop(oldItem, oldCond); // the slot freed above makes room for the swapped piece
            Notify();
            return true;
        }

        /// <summary>Backpack swap / equip: growing the pack always works; a smaller (or no) backpack first packs items out of the
        /// slots that go away and is refused when they don't fit (nothing is dropped or lost).</summary>
        bool EquipBackpack(ASHItemDefinition item, int packSlot)
        {
            if (!_inv) { Message = "No pack."; return false; }
            int i = FindInPack(item, packSlot);
            if (i < 0) { Message = $"You're not carrying {item.displayName}."; return false; }
            var w = this[Slot.Backpack];
            var oldItem = w.item; float oldCond = w.condition;
            int newCarry = Carry(item);
            float cond = _inv.Slots[i].condition;
            if (!_inv.RemoveFromSlot(i, 1)) { Message = "Couldn't take it from the pack."; return false; }
            if (newCarry >= _inv.BonusPackSlots) _inv.ForceBonusPackSlots(newCarry);
            else if (!_inv.TrySetBonusPackSlots(newCarry, oldItem ? 1 : 0, out int need))
            {
                _inv.AddItemWithCondition(item, 1, cond);
                Message = $"Your pack is too full to swap: empty {need} slot(s) first.";
                return false;
            }
            w.item = item; w.condition = cond;
            if (oldItem) ReturnOrDrop(oldItem, oldCond);
            Notify();
            return true;
        }

        void ReturnOrDrop(ASHItemDefinition item, float cond)
        {
            if (_inv && _inv.AddItemWithCondition(item, 1, cond) > 0) return;
            var pk = ASHItemDrop.Spawn(item, 1, transform);
            if (pk) pk.SetCondition(cond);
            Message = $"Pack full: {item.displayName} dropped at your feet.";
        }

        public bool Unequip(Slot slot, bool notify = true)
        {
            Message = null;
            if (slot == Slot.Face)
            {
                var face = ASHFaceProtection.Instance;
                if (face != null && face.Worn != ASHFaceProtection.Kind.None) { face.TakeOffFace(); if (notify) Notify(); return true; }
                return UnequipWorn(_faceGear, notify);
            }
            if (slot == Slot.Backpack && !this[slot].Empty && _inv && _inv.BonusPackSlots > 0)
            {
                if (!_inv.TrySetBonusPackSlots(0, 1, out int need))
                {
                    Message = $"The {this[slot].item.displayName}'s extra slots hold items: empty {need} pack slot(s) first.";
                    return false;
                }
            }
            return UnequipWorn(this[slot], notify);
        }

        bool UnequipWorn(Worn w, bool notify)
        {
            if (w.Empty) return true;
            if (!_inv) { Message = "No pack."; return false; }
            if (_inv.AddItemWithCondition(w.item, 1, w.condition) <= 0)
            {
                Message = "Pack is full.";
                return false;
            }
            w.Clear();
            if (notify) Notify();
            return true;
        }

        void Notify() { ASHClothing.MarkDirty(); Changed?.Invoke(); }

        void Update()
        {
            if (!_inv || Time.timeScale <= 0f) return;
            // keep the pack's bonus slots in step with the worn backpack (save restore, live edits of carrySlots)
            if (!ASHSaveSystem.IsRestoring)
            {
                int want = CarrySlots;
                if (_inv.BonusPackSlots < want) _inv.ForceBonusPackSlots(want);
                else if (_inv.BonusPackSlots > want) _inv.TrySetBonusPackSlots(want, 0, out _);
            }
            // outdoor wear (condition)
            var S = ASHClothingSettings.I; float dh = ASHGameTime.DeltaHours;
            if (dh <= 0f || S.wearHoursOutdoors <= 0f) return;
            var ex = ASHExposure.Instance;
            if (ex && ex.Sealing >= S.dryWhenSealing) return;
            float rate = dh / S.wearHoursOutdoors * ((ex && ex.Exposure > 0.01f) || ASHClothing.IsWet() ? S.stormWearMultiplier : 1f);
            foreach (var w in WornPieces()) w.condition = Mathf.Max(0f, w.condition - rate);
        }

        static ASHItemDefinition FindItem(string id) => ASHItemRegistry.I ? ASHItemRegistry.I.Find(id) : null;

        [Serializable] sealed class State
        {
            public List<string> ids = new List<string>();
            public List<float> cond = new List<float>();
            public string faceGear = "";
            public float faceGearCond = 1f;
        }

        public string SaveKey => "equipment";
        public int RestoreOrder => 25;
        public string CaptureJson()
        {
            var s = new State();
            for (int i = 0; i < _slots.Length; i++)
            {
                if ((Slot)i == Slot.Face) { s.ids.Add(""); s.cond.Add(0f); continue; } // face is saved by ASHFaceProtection
                var w = _slots[i] ?? new Worn();
                s.ids.Add(w.item ? w.item.itemId : "");
                s.cond.Add(w.condition);
            }
            s.faceGear = _faceGear.item ? _faceGear.item.itemId : ""; s.faceGearCond = _faceGear.condition;
            return JsonUtility.ToJson(s);
        }

        public void RestoreJson(string json)
        {
            for (int i = 0; i < _slots.Length; i++) { if (_slots[i] == null) _slots[i] = new Worn(); else _slots[i].Clear(); }
            _faceGear.Clear();
            if (string.IsNullOrEmpty(json)) { Notify(); return; }
            var s = JsonUtility.FromJson<State>(json);
            if (s == null || s.ids == null) { Notify(); return; }
            for (int i = 0; i < _slots.Length && i < s.ids.Count; i++)
            {
                if ((Slot)i == Slot.Face) continue;
                if (string.IsNullOrEmpty(s.ids[i])) continue;
                var it = FindItem(s.ids[i]);
                if (!it) continue;
                _slots[i].item = it;
                _slots[i].condition = i < s.cond.Count ? s.cond[i] : 1f;
            }
            if (!string.IsNullOrEmpty(s.faceGear)) { var fg = FindItem(s.faceGear); if (fg) { _faceGear.item = fg; _faceGear.condition = s.faceGearCond; } }
            if (_inv) { int want = CarrySlots; if (_inv.BonusPackSlots < want) _inv.ForceBonusPackSlots(want); else if (_inv.BonusPackSlots > want) _inv.TrySetBonusPackSlots(want, 0, out _); }
            Notify();
        }
    }
}
