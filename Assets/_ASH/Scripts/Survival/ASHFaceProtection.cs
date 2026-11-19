using System;
using ASH.Core;
using ASH.Interaction;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// What the player wears over mouth and nose (wet rag / gas mask + filter cartridge) and Grandpa's refillable water bottle.
    /// UI only (no screen overlay). Durations run on the game clock (ASHGameTime). Single items keep their state in the pack
    /// slot's condition: a Wet Rag's condition = moisture left, a Filter Cartridge's = filter life left, the Water Bottle's = fill.
    /// Use handlers: Wet Rag (wear / swap), Dry Rag (re-wet with bottle water), Gas Mask (wear / take off), Filter Cartridge
    /// (fit to the mask: the old one goes back to the pack with its condition), Water Bottle (drink 10%). Saved ("face").
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHFaceProtection : MonoBehaviour, IASHSaveable
    {
        public enum Kind { None, Rag, Mask }
        public const string WetRagId = "wet_rag", DryRagId = "dry_rag", MaskId = "gas_mask", FilterId = "filter_cartridge", BottleId = "water_bottle";

        public static ASHFaceProtection Instance { get; private set; }
        public Kind Worn { get; private set; }
        public float RagHours { get; private set; }
        /// <summary>Game hours left on the fitted filter (0 = none / clogged).</summary>
        public float FilterHours { get; private set; }
        /// <summary>Bottle holds untreated water (hook for later: dirty water effects).</summary>
        public bool BottleDirty { get; set; }
        public static event Action<float> DrankFromBottle;
        /// <summary>Hook: return true when the water source gives dirty water (refill marks the bottle dirty).</summary>
        public static Func<ASHWaterSource, bool> SourceIsDirty;

        ASHExposureSettings S => ASHExposureSettings.I;
        ASHInventory _inv; ASHSurvivalStats _stats;
        bool _ragWarned, _filterWarned;

        static ASHItemDefinition Def(string id) => ASHItemRegistry.I.Find(id);

        // ------------------------------------------------------------------ bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ASHHud.UseHandlers.Add(new WearRagHandler()); ASHHud.UseHandlers.Add(new WetDryRagHandler()); ASHHud.UseHandlers.Add(new WearMaskHandler());
            ASHHud.UseHandlers.Add(new FitFilterHandler()); ASHHud.UseHandlers.Add(new DrinkBottleHandler());
            ASHItemPickup.ConditionRoller = it => it && it.itemId == FilterId ? ASHExposureSettings.I.RollFilterCondition() : -1f;
        }

        void Awake()
        {
            Instance = this;
            _stats = GetComponent<ASHSurvivalStats>(); _inv = GetComponent<ASHInventory>(); if (!_inv) _inv = FindAnyObjectByType<ASHInventory>();
        }
        void OnEnable()
        {
            ASHSaveSystem.Register(this);
            ASHCrafting.WaterPercentAvailable = () => BottleFill * 100f;
            ASHCrafting.ConsumeWater = p => ConsumeWater(p);
            ASHWaterSource.BottleNeedsRefill = () => HasBottle && !BottleFull;
            ASHWaterSource.RefillBottle = RefillBottles;
        }
        void OnDisable() { ASHSaveSystem.Unregister(this); ASHCrafting.WaterPercentAvailable = null; ASHCrafting.ConsumeWater = null; ASHWaterSource.BottleNeedsRefill = null; ASHWaterSource.RefillBottle = null; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        static void Toast(string m) { var h = FindAnyObjectByType<ASHHud>(); if (h) h.Toast(m); }
        static float Rate => Mathf.Max(0.0001f, ASHSurvivalTuning.DrainRate);
        static float Effective(float hours) => hours / Rate;

        // ------------------------------------------------------------------ protection
        public float Protection
        {
            get
            {
                if (Worn == Kind.Rag) return S.ragProtection * (S.ragDegradeHours > 0f ? Mathf.Clamp01(Effective(RagHours) / S.ragDegradeHours) : (RagHours > 0f ? 1f : 0f));
                if (Worn == Kind.Mask)
                {
                    if (FilterHours <= 0f) return S.maskNoFilterProtection;
                    float f = S.filterDegradeHours > 0f ? Mathf.Clamp01(Effective(FilterHours) / S.filterDegradeHours) : 1f;
                    return Mathf.Lerp(S.maskNoFilterProtection, S.maskProtection, f);
                }
                return 0f;
            }
        }

        /// <summary>Remaining effective game hours of what is worn (raw life / drain rate; -1 = nothing to count down).</summary>
        public float RemainingHours => Worn == Kind.Rag ? Effective(RagHours) : Worn == Kind.Mask ? Effective(FilterHours) : -1f;
        public bool Low => Worn == Kind.Rag ? Effective(RagHours) <= S.ragDegradeHours : Worn == Kind.Mask && Effective(FilterHours) <= S.filterLowHours;
        public float Durability01 => Worn == Kind.Rag ? Mathf.Clamp01(RagHours / Mathf.Max(0.01f, S.ragHours)) : Worn == Kind.Mask ? Mathf.Clamp01(FilterHours / Mathf.Max(0.01f, S.filterHours)) : 0f;

        /// <summary>Called by ASHExposure every frame: dh = game hours, exposed = the air is bad where you stand.</summary>
        public void Tick(float dh, bool exposed)
        {
            if (Worn == Kind.Mask && _inv && Def(MaskId) && _inv.Count(Def(MaskId)) <= 0) { Worn = Kind.None; Toast("Gas mask off."); }
            if (dh <= 0f) return;
            if (Worn == Kind.Rag)
            {
                RagHours -= dh * Rate;
                if (!_ragWarned && Effective(RagHours) <= S.ragDegradeHours && RagHours > 0f) { _ragWarned = true; Toast("Your rag is drying out"); }
                if (RagHours <= 0f)
                {
                    RagHours = 0f; Worn = Kind.None; _ragWarned = false;
                    Give(Def(DryRagId), 1f);
                    Toast("Your rag dried out. Wet it again (10% water).");
                }
            }
            else if (Worn == Kind.Mask && FilterHours > 0f && (exposed || !S.filterWearsOnlyWhenExposed))
            {
                FilterHours -= dh * Rate;
                if (!_filterWarned && Effective(FilterHours) <= S.filterLowHours && FilterHours > 0f) { _filterWarned = true; Toast("Gas mask filter is running low"); }
                if (FilterHours <= 0f) { FilterHours = 0f; _filterWarned = false; Toast("Filter clogged. Fit a new cartridge."); }
            }
        }

        void Give(ASHItemDefinition item, float condition)
        {
            if (!item || !_inv) return;
            int added = item.maxStack == 1 ? _inv.AddItemWithCondition(item, 1, condition) : _inv.AddItem(item, 1);
            if (added <= 0) { var pk = ASHItemDrop.Spawn(item, 1, transform); if (pk && item.maxStack == 1) pk.SetCondition(condition); }
        }

        /// <summary>Puts water back into the bottle (a cancelled cook batch). Fills the fullest bottle that isn't full.</summary>
        public void AddWater(float percent)
        {
            var b = Def(BottleId); if (!b || !_inv || percent <= 0f) return;
            int best = -1; float bc = -1f; var sl = _inv.Slots;
            for (int i = 0; i < sl.Count; i++) if (!sl[i].IsEmpty && sl[i].item == b && sl[i].condition < 0.999f && sl[i].condition > bc) { bc = sl[i].condition; best = i; }
            if (best < 0) return;
            sl[best].condition = Mathf.Clamp01(sl[best].condition + percent / 100f); _inv.NotifyChanged();
        }

        int SlotOf(ASHItemDefinition item, int slot, bool best)
        {
            if (!_inv || !item) return -1;
            var sl = _inv.Slots;
            if (slot >= 0 && slot < sl.Count && sl[slot].item == item && !sl[slot].IsEmpty) return slot;
            int found = -1; float bc = -1f;
            for (int i = 0; i < sl.Count; i++) if (!sl[i].IsEmpty && sl[i].item == item) { if (!best) return i; if (sl[i].condition > bc) { bc = sl[i].condition; found = i; } }
            return found;
        }

        void ReturnRag()
        {
            if (Worn != Kind.Rag) return;
            float c = RagHours / Mathf.Max(0.01f, S.ragHours);
            if (c > 0.02f) Give(Def(WetRagId), c); else Give(Def(DryRagId), 1f);
            Worn = Kind.None; RagHours = 0f;
        }

        public bool WearRag(int slot)
        {
            var rag = Def(WetRagId); int i = SlotOf(rag, slot, true); if (i < 0) return false;
            float c = Mathf.Clamp01(_inv.Slots[i].condition);
            _inv.RemoveFromSlot(i, 1);
            ReturnRag();
            Worn = Kind.Rag; RagHours = c * S.ragHours; _ragWarned = Effective(RagHours) <= S.ragDegradeHours;
            Toast($"Wet rag tied over your face  ({ASHGameTime.Format(Effective(RagHours))} left)");
            return true;
        }

        public bool WearMask()
        {
            if (Worn == Kind.Mask) { Worn = Kind.None; Toast("Gas mask off."); return true; }
            var m = Def(MaskId); if (!m || !_inv || _inv.Count(m) <= 0) return false;
            ReturnRag();
            Worn = Kind.Mask;
            if (FilterHours <= 0f) FitFilter(-1, true);
            Toast(FilterHours > 0f ? $"Gas mask on  (filter {ASHGameTime.Format(Effective(FilterHours))} left)" : "Gas mask on - no filter fitted. It barely helps.");
            return true;
        }

        public bool FitFilter(int slot, bool quiet = false)
        {
            var f = Def(FilterId); var m = Def(MaskId);
            if (!m || _inv.Count(m) <= 0) { if (!quiet) Toast("You need a gas mask to fit this."); return !quiet; }
            int i = SlotOf(f, slot, true); if (i < 0) return false;
            float c = Mathf.Clamp01(_inv.Slots[i].condition);
            if (c < S.filterUnusableBelow) { if (!quiet) Toast("This filter is clogged. Useless."); return !quiet; }
            _inv.RemoveFromSlot(i, 1);
            float old = FilterHours / Mathf.Max(0.01f, S.filterHours);
            if (old >= S.filterUnusableBelow) Give(f, old); else if (FilterHours > 0f || !quiet) Toast("Spent filter thrown away.");
            FilterHours = c * S.filterHours; _filterWarned = Effective(FilterHours) <= S.filterLowHours;
            if (!quiet) Toast($"Filter fitted  ({ASHGameTime.Format(Effective(FilterHours))} left)" + (Worn != Kind.Mask ? " - wear the mask to use it" : ""));
            return true;
        }

        public bool RewetDryRag(int slot)
        {
            var dry = Def(DryRagId); var wet = Def(WetRagId); int i = SlotOf(dry, slot, false); if (i < 0 || !wet) return false;
            if (BottleFill * 100f + 0.01f < S.rewetWaterPercent) { Toast(HasBottle ? $"Not enough water in your bottle ({S.rewetWaterPercent:0}% needed)." : "You need water (a water bottle) to wet it."); return true; }
            ConsumeWater(S.rewetWaterPercent);
            _inv.RemoveFromSlot(i, 1);
            Give(wet, 1f);
            Toast("Rag soaked again.");
            return true;
        }

        // ------------------------------------------------------------------ bottle (fill = slot condition)
        public bool HasBottle => _inv && Def(BottleId) && _inv.Count(Def(BottleId)) > 0;
        /// <summary>Fill 0-1 of the fullest bottle (0 when none).</summary>
        public float BottleFill { get { var b = Def(BottleId); int i = SlotOf(b, -1, true); return i < 0 ? 0f : Mathf.Clamp01(_inv.Slots[i].condition); } }
        public bool BottleFull { get { var b = Def(BottleId); if (!b || !_inv) return true; foreach (var s in _inv.Slots) if (!s.IsEmpty && s.item == b && s.condition < 0.995f) return false; return true; } }

        public bool ConsumeWater(float percent)
        {
            var b = Def(BottleId); int i = SlotOf(b, -1, true); if (i < 0) return false;
            var s = _inv.Slots[i]; float need = percent / 100f;
            if (s.condition + 0.0001f < need) return false;
            s.condition = Mathf.Clamp01(s.condition - need); _inv.NotifyChanged();
            return true;
        }

        public bool Drink(int slot)
        {
            var b = Def(BottleId); int i = SlotOf(b, slot, true); if (i < 0) return false;
            var s = _inv.Slots[i];
            if (s.condition * 100f + 0.01f < S.sipPercent) { Toast("Your water bottle is empty. Refill it at a water source."); return true; }
            s.condition = Mathf.Clamp01(s.condition - S.sipPercent / 100f); _inv.NotifyChanged();
            if (_stats) _stats.Drink(S.thirstPerSip);
            DrankFromBottle?.Invoke(S.sipPercent);
            Toast($"Water bottle: {Mathf.RoundToInt(s.condition * 100f)}%");
            return true;
        }

        public void RefillBottles(ASHWaterSource src)
        {
            var b = Def(BottleId); if (!b || !_inv) return;
            foreach (var s in _inv.Slots) if (!s.IsEmpty && s.item == b) s.condition = 1f;
            BottleDirty = SourceIsDirty != null && SourceIsDirty(src);
            _inv.NotifyChanged();
            Toast("Water bottle refilled.");
        }

        // ------------------------------------------------------------------ use handlers
        sealed class WearRagHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) => it && it.itemId == WetRagId;
            public string UseLabel(ASHItemDefinition it) { var p = Instance; return p && p.Worn == Kind.Rag ? "Swap rag" : p && p.Worn == Kind.Mask ? "Wear (mask off)" : "Wear"; }
            public bool Use(ASHItemDefinition it, int slot) => Instance && Instance.WearRag(slot);
        }
        sealed class WetDryRagHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) => it && it.itemId == DryRagId;
            public string UseLabel(ASHItemDefinition it) => $"Wet ({ASHExposureSettings.I.rewetWaterPercent:0}% water)";
            public bool Use(ASHItemDefinition it, int slot) => Instance && Instance.RewetDryRag(slot);
        }
        sealed class WearMaskHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) => it && it.itemId == MaskId;
            public string UseLabel(ASHItemDefinition it) => Instance && Instance.Worn == Kind.Mask ? "Take off" : "Wear";
            public bool Use(ASHItemDefinition it, int slot) => Instance && Instance.WearMask();
        }
        sealed class FitFilterHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) => it && it.itemId == FilterId;
            public string UseLabel(ASHItemDefinition it) => "Fit to mask";
            public bool Use(ASHItemDefinition it, int slot) => Instance && Instance.FitFilter(slot);
        }
        sealed class DrinkBottleHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) => it && it.itemId == BottleId;
            public string UseLabel(ASHItemDefinition it) => Instance ? $"Drink ({Mathf.RoundToInt(Instance.BottleFill * 100f)}%)" : "Drink";
            public bool Use(ASHItemDefinition it, int slot) => Instance && Instance.Drink(slot);
        }

        // ------------------------------------------------------------------ save
        [Serializable] sealed class State { public int worn; public float ragHours, filterHours; public bool dirty; }
        public string SaveKey => "face";
        public int RestoreOrder => 30;
        public string CaptureJson() => JsonUtility.ToJson(new State { worn = (int)Worn, ragHours = RagHours, filterHours = FilterHours, dirty = BottleDirty });
        public void RestoreJson(string json)
        {
            var s = JsonUtility.FromJson<State>(json); if (s == null) return;
            Worn = (Kind)s.worn; RagHours = s.ragHours; FilterHours = s.filterHours; BottleDirty = s.dirty;
            _ragWarned = Worn == Kind.Rag && Effective(RagHours) <= S.ragDegradeHours; _filterWarned = Effective(FilterHours) <= S.filterLowHours;
        }
    }
}
