using System;
using System.Collections.Generic;
using ASH.Core;
using UnityEngine;

namespace ASH.Inventory
{
    /// <summary>
    /// Player upgrades that change item rules at runtime (not the item assets). The first is the Ammo Pouch:
    /// each crafted tier adds 25% to ammo max stacks, up to 4. Saved as the "upgrades" section; old saves start at tier 0.
    /// Reusable: any upgrade id can store a tier, and recipes set upgradeId + upgradeTier.
    /// </summary>
    public static class ASHUpgrades
    {
        public const string AmmoPouch = "ammo_pouch";
        public const int AmmoPouchMaxTier = 4;
        public const float AmmoPouchBonusPerTier = 0.25f;

        static readonly Dictionary<string, int> _tiers = new Dictionary<string, int>();
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _tiers.Clear(); Changed = null; }

        public static void ResetAll() { _tiers.Clear(); Changed?.Invoke(); }

        public static int Tier(string id) => !string.IsNullOrEmpty(id) && _tiers.TryGetValue(id, out var t) ? t : 0;

        /// <summary>Sets a tier if it is exactly the next one (1..max). Returns false if skipped or already there.</summary>
        public static bool Apply(string id, int tier, int maxTier = 8)
        {
            if (string.IsNullOrEmpty(id) || tier < 1) return false;
            int have = Tier(id);
            if (tier <= have) return false;
            if (tier > have + 1) return false;
            if (tier > maxTier) return false;
            _tiers[id] = tier;
            Changed?.Invoke();
            return true;
        }

        public static int AmmoPouchTier => Mathf.Clamp(Tier(AmmoPouch), 0, AmmoPouchMaxTier);

        public static bool IsAmmo(ASHItemDefinition item)
        {
            if (!item) return false;
            if (item.category == ASHItemCategory.Ammo) return true;
            switch (item.itemId)
            {
                case "pistolrounds":
                case "riflerounds":
                case "shotgunshells":
                case "woodenarrow":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Runtime stack cap: the item's authored maxStack, plus ammo-pouch bonuses for ammo.</summary>
        public static int StackLimit(ASHItemDefinition item)
        {
            if (!item) return 1;
            int b = Mathf.Max(1, item.maxStack);
            if (!IsAmmo(item)) return b;
            int t = AmmoPouchTier;
            if (t <= 0) return b;
            return Mathf.Max(1, Mathf.RoundToInt(b * (1f + AmmoPouchBonusPerTier * t)));
        }

        [Serializable] internal sealed class Entry { public string id; public int tier; }
        [Serializable] internal sealed class State { public List<Entry> tiers = new List<Entry>(); }

        internal static string Capture()
        {
            var s = new State();
            foreach (var kv in _tiers) if (kv.Value > 0) s.tiers.Add(new Entry { id = kv.Key, tier = kv.Value });
            return JsonUtility.ToJson(s);
        }

        internal static void Restore(string json)
        {
            _tiers.Clear();
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json);
            if (s != null && s.tiers != null)
                foreach (var e in s.tiers)
                    if (e != null && !string.IsNullOrEmpty(e.id) && e.tier > 0) _tiers[e.id] = e.tier;
            Changed?.Invoke();
        }
    }

    /// <summary>"upgrades" save section (registered by ASHSaveSystem).</summary>
    public sealed class ASHUpgradeSave : IASHSaveable
    {
        public string SaveKey => "upgrades";
        public int RestoreOrder => 6;
        public string CaptureJson() => ASHUpgrades.Capture();
        public void RestoreJson(string json) => ASHUpgrades.Restore(json);
    }
}
