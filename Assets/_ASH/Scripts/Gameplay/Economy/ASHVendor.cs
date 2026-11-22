using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Survival;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Placeholder vendor (E to trade). Stock, prices and names live in Resources/ASH_EconomySettings (editable in Play, kept).
    /// Remaining stock per item is saved ("vendor:" + vendorId) and refills every restockHours of game time. Placed by
    /// ASH/Systems/Economy/Place Vendor Near Grandpas Cabin.</summary>
    public sealed class ASHVendor : MonoBehaviour, IASHUsable, IASHSaveable
    {
        [Tooltip("Save id (one per vendor in the scene).")] public string vendorId = "grandpa_trader";

        readonly Dictionary<string, int> _left = new Dictionary<string, int>();
        float _hoursSinceRestock;

        ASHEconomySettings S => ASHEconomySettings.I;
        public Transform UseTransform => transform;

        void OnEnable() => ASHSaveSystem.Register(this);
        void OnDisable() => ASHSaveSystem.Unregister(this);

        void Update()
        {
            if (S.restockHours <= 0f) return;
            _hoursSinceRestock += ASHGameTime.DeltaHours;
            if (_hoursSinceRestock >= S.restockHours) { _hoursSinceRestock = 0f; _left.Clear(); }
        }

        public int Left(ASHEconomySettings.StockEntry e)
        {
            if (e == null || !e.item) return 0;
            if (e.quantity < 0) return int.MaxValue;
            return _left.TryGetValue(e.item.itemId, out var n) ? n : e.quantity;
        }

        /// <summary>Why this entry can't be bought by level, or null.</summary>
        public static string LockReason(ASHEconomySettings.StockEntry e)
        {
            if (e == null || e.minLevel <= 0 || string.IsNullOrEmpty(e.skill)) return null;
            int lv = ASHSkills.Level(e.skill);
            return lv >= e.minLevel ? null : $"Needs {ASHSkills.NameOf(e.skill)} lv {e.minLevel}";
        }

        public bool Buy(ASHEconomySettings.StockEntry e, ASHInventory inv, out string msg)
        {
            msg = null; var w = ASHWallet.Ensure();
            if (e == null || !e.item || !inv || !w) { msg = "Can't trade right now."; return false; }
            var lockR = LockReason(e); if (lockR != null) { msg = lockR + "."; return false; }
            if (Left(e) <= 0) { msg = "Sold out. Come back later."; return false; }
            int price = ASHEconomy.BuyPrice(e.item);
            if (w.Balance < price) { msg = $"Not enough coin ({ASHEconomy.Format(price)})."; return false; }
            int added = e.item.maxStack <= 1 ? inv.AddItemWithCondition(e.item, 1, 1f) : inv.AddItem(e.item, 1);
            if (added <= 0) { msg = "Your pack is full."; return false; }
            w.TrySpend(price);
            if (e.quantity >= 0) _left[e.item.itemId] = Left(e) - 1;
            msg = $"Bought {e.item.displayName} for {ASHEconomy.Format(price)}.";
            return true;
        }

        public bool SellSlot(ASHInventory inv, int slot, out string msg)
        {
            msg = null; var w = ASHWallet.Ensure();
            if (!inv || !w || slot < 0 || slot >= inv.Slots.Count) return false;
            var sl = inv.Slots[slot]; if (sl.IsEmpty) return false;
            var it = sl.item;
            if (ASHInventory.IsKeyItem(it) || !it.CanSell) { msg = $"The {S.vendorName.ToLowerInvariant()} won't buy that."; return false; }
            int pay = ASHEconomy.SellPrice(it, sl.condition);
            if (!inv.RemoveFromSlot(slot, 1)) return false;
            w.Earn(pay);
            msg = $"Sold {it.displayName} for {ASHEconomy.Format(pay)}.";
            return true;
        }

        // ------------------------------------------------------------------ IASHUsable
        public float GetUsePriority(in ASHUseContext c) => 30f;
        public string GetPrompt(in ASHUseContext c) => "Trade with " + S.vendorName;
        public float GetHoldDuration(in ASHUseContext c) => 0f;
        public bool TryUse(in ASHUseContext c) => ASHVendorMenu.Open(this, c.Inventory, c.PlayerTransform);

        // ------------------------------------------------------------------ save
        [Serializable] sealed class State { public List<string> ids = new List<string>(); public List<int> left = new List<int>(); public float hours; }
        public string SaveKey => "vendor:" + vendorId;
        public int RestoreOrder => 40;
        public string CaptureJson()
        {
            var s = new State { hours = _hoursSinceRestock };
            foreach (var kv in _left) { s.ids.Add(kv.Key); s.left.Add(kv.Value); }
            return JsonUtility.ToJson(s);
        }
        public void RestoreJson(string json)
        {
            _left.Clear(); _hoursSinceRestock = 0f;
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json); if (s == null) return;
            _hoursSinceRestock = s.hours;
            for (int i = 0; i < s.ids.Count && i < s.left.Count; i++) _left[s.ids[i]] = s.left[i];
        }
    }
}
