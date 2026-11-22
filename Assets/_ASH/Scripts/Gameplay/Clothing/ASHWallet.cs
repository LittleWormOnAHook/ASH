using System;
using ASH.Core;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>The player's purse: total copper (Copper / Silver / Gold Bits, ASH_EconomySettings). Starts at startingPurse on a new
    /// game and is saved ("wallet"; older saves in AC are converted). Lives next to the inventory (added at runtime when missing).
    /// The vendor uses Earn / TrySpend / Sell.</summary>
    public sealed class ASHWallet : MonoBehaviour, IASHSaveable
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHWallet() { Instance = null; Changed = null; }

        public static ASHWallet Instance { get; private set; }
        public static event Action Changed;

        [SerializeField] int balance = -1;
        public int Balance { get { if (balance < 0) balance = ASHEconomySettings.I.startingPurse; return balance; } }
        /// <summary>Balance as "1g 25s 40c".</summary>
        public string Formatted => ASHEconomy.Format(Balance);

        void Awake() { Instance = this; if (balance < 0) balance = ASHEconomySettings.I.startingPurse; }
        void OnEnable() { Instance = this; ASHSaveSystem.Register(this); }
        void OnDisable() { ASHSaveSystem.Unregister(this); if (Instance == this) Instance = null; }

        public static ASHWallet Ensure()
        {
            if (Instance) return Instance;
            var inv = FindAnyObjectByType<ASHInventory>(); if (!inv) return null;
            var w = inv.GetComponent<ASHWallet>(); if (!w) w = inv.gameObject.AddComponent<ASHWallet>();
            return w;
        }

        public void Earn(int amount) { if (amount <= 0) return; balance = Balance + amount; Changed?.Invoke(); }
        public bool TrySpend(int amount) { if (amount < 0 || Balance < amount) return false; balance = Balance - amount; Changed?.Invoke(); return true; }

        /// <summary>Sells amount of item from the inventory at the vendor sell price each. Returns copper earned.</summary>
        public int Sell(ASHInventory inv, ASHItemDefinition item, int amount = 1)
        {
            if (!inv || !item || !item.CanSell || ASHInventory.IsKeyItem(item) || amount <= 0) return 0;
            int n = Mathf.Min(amount, inv.Count(item)); if (n <= 0 || !inv.Remove(item, n)) return 0;
            int pay = n * ASHEconomy.SellPrice(item); Earn(pay); return pay;
        }

        [Serializable] sealed class State { public int balance; public int copper = 1; }
        public string SaveKey => "wallet";
        public int RestoreOrder => 5;
        public string CaptureJson() => JsonUtility.ToJson(new State { balance = Balance, copper = 1 });
        public void RestoreJson(string json)
        {
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json);
            if (s == null) balance = ASHEconomySettings.I.startingPurse;
            else if (!json.Contains("\"copper\"")) balance = Mathf.Max(0, s.balance) * ASHEconomy.LegacyCopperPerAC; // pre-coin save (AC)
            else balance = Mathf.Max(0, s.balance);
            Changed?.Invoke();
        }
    }
}
