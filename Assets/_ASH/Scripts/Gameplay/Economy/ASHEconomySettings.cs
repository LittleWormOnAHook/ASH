using System;
using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Money and the placeholder vendor (Resources/ASH_EconomySettings). Money is Copper / Silver / Gold Bits, stored as total
    /// copper (int) everywhere (item sellValue, wallet, prices). Every value is read live, so Play-mode edits apply at once and are kept
    /// (it is an asset). Created / filled by ASH/Systems/Economy/Setup Economy And Vendor (idempotent).</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Economy Settings", fileName = "ASH_EconomySettings")]
    public sealed class ASHEconomySettings : ScriptableObject
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHEconomySettings() { _i = null; }

        static ASHEconomySettings _i;
        public static ASHEconomySettings I { get { if (!_i) { _i = Resources.Load<ASHEconomySettings>("ASH_EconomySettings"); if (!_i) _i = CreateInstance<ASHEconomySettings>(); } return _i; } }

        [Serializable]
        public sealed class StockEntry
        {
            public ASHItemDefinition item;
            [Tooltip("How many the vendor holds (-1 = unlimited). Refills every restockHours.")] [Range(-1, 999)] public int quantity = 3;
            [Tooltip("Skill needed to buy it (empty = none): crafting, hunting, cooking.")] public string skill = "";
            [Tooltip("Skill level needed (0 = no lock).")] [Range(0, 20)] public int minLevel;
            [Tooltip("Price override in copper (0 = item value x buy multiplier).")] [Min(0)] public int priceOverride;
        }

        [Header("Denominations (stored as total copper)")]
        [Range(2, 1000)] public int copperPerSilver = 100;
        [Range(2, 1000)] public int silverPerGold = 100;
        public string copperName = "Copper Bits", silverName = "Silver Bits", goldName = "Gold Bits";
        public string copperShort = "c", silverShort = "s", goldShort = "g";
        public Color copperColor = new Color32(0xB8, 0x73, 0x33, 0xFF);
        public Color silverColor = new Color32(0xC0, 0xC0, 0xC0, 0xFF);
        public Color goldColor = new Color32(0xD4, 0xAF, 0x37, 0xFF);
        [Tooltip("Show coin amounts in their coin colours (off = plain text).")] public bool colouredCoins = true;

        [Header("Purse")]
        [Tooltip("Copper in the purse at the start of a new game (default 1000 = 10 silver).")] [Range(0, 1000000)] public int startingPurse = 1000;

        [Header("Vendor prices")]
        [Tooltip("Buy price = item value x this.")] [Range(0.1f, 10f)] public float buyMultiplier = 1f;
        [Tooltip("Sell price = item value x this (rounded down, at least 1 copper for sellable items).")] [Range(0f, 5f)] public float sellMultiplier = 0.4f;
        [Tooltip("Sell price also scales with the piece's condition (0..1).")] public bool sellScalesWithCondition = true;
        [Tooltip("Lowest condition share paid when selling worn items.")] [Range(0f, 1f)] public float minConditionPay = 0.25f;

        [Header("Vendor")]
        public string vendorName = "Trader";
        [TextArea(1, 3)] public string vendorGreeting = "Got coin? I got goods. I'll take most things off your hands too.";
        [Tooltip("Game hours between stock refills (0 = never).")] [Range(0f, 720f)] public float restockHours = 24f;
        [Range(1f, 10f)] public float useRange = 3f;
        public List<StockEntry> stock = new List<StockEntry>();

        [Header("Setup (editor)")]
        [Tooltip("Item values were converted from the old AC numbers to copper (editor migration flag; do not untick).")] public bool valuesInCopper;

        public StockEntry Find(ASHItemDefinition it) { foreach (var s in stock) if (s != null && s.item == it) return s; return null; }
    }

    /// <summary>Money formatting and conversions. All amounts are total copper.</summary>
    public static class ASHEconomy
    {
        /// <summary>Old AC numbers (clothing C1/C2 value table, setup menus) x this = copper. 1 AC = 50 copper.</summary>
        public const int LegacyCopperPerAC = 50;
        public static int LegacyToCopper(int ac) => ac * LegacyCopperPerAC;

        public static int CopperPerGold { get { var S = ASHEconomySettings.I; return Mathf.Max(1, S.copperPerSilver) * Mathf.Max(1, S.silverPerGold); } }

        public static void Split(int copper, out int g, out int s, out int c)
        {
            var S = ASHEconomySettings.I; int cps = Mathf.Max(1, S.copperPerSilver), cpg = CopperPerGold;
            copper = Mathf.Max(0, copper);
            g = copper / cpg; copper -= g * cpg; s = copper / cps; c = copper - s * cps;
        }

        /// <summary>"1g 25s 40c" (zero parts left out, "0c" for nothing), in coin colours when colouredCoins is on.</summary>
        public static string Format(int copper, bool rich = true)
        {
            var S = ASHEconomySettings.I; Split(copper, out int g, out int s, out int c);
            rich &= S.colouredCoins;
            var sb = new System.Text.StringBuilder();
            void Part(int n, string sh, Color col)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (rich) sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(col)).Append('>');
                sb.Append(n).Append(sh);
                if (rich) sb.Append("</color>");
            }
            if (g > 0) Part(g, S.goldShort, S.goldColor);
            if (s > 0) Part(s, S.silverShort, S.silverColor);
            if (c > 0 || sb.Length == 0) Part(c, S.copperShort, S.copperColor);
            return sb.ToString();
        }

        public static int BuyPrice(ASHItemDefinition it)
        {
            if (!it) return 0; var S = ASHEconomySettings.I; var e = S.Find(it);
            if (e != null && e.priceOverride > 0) return e.priceOverride;
            return Mathf.Max(1, Mathf.RoundToInt(it.sellValue * S.buyMultiplier));
        }

        public static int SellPrice(ASHItemDefinition it, float condition = 1f)
        {
            if (!it || !it.CanSell || ASHInventory.IsKeyItem(it)) return 0; var S = ASHEconomySettings.I;
            float f = S.sellMultiplier;
            if (S.sellScalesWithCondition && it.maxStack <= 1) f *= Mathf.Lerp(S.minConditionPay, 1f, Mathf.Clamp01(condition));
            return Mathf.Max(1, Mathf.FloorToInt(it.sellValue * f));
        }
    }
}
