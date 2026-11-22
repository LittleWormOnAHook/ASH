using System;
using ASH.Core;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>The player's money (AC). Starts at ASH_ClothingSettings.startingCurrency on a new game and is saved ("wallet").
    /// Lives next to the inventory (added at runtime when missing). Selling / buying needs a vendor (not built yet): use Earn / TrySpend.</summary>
    public sealed class ASHWallet : MonoBehaviour, IASHSaveable
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHWallet() { Instance = null; Changed = null; }

        public static ASHWallet Instance { get; private set; }
        public static event Action Changed;

        [SerializeField] int balance = -1;
        public int Balance { get { if (balance < 0) balance = ASHClothingSettings.I.startingCurrency; return balance; } }
        public static string Currency => ASHClothingSettings.I.currencyName;

        void Awake() { Instance = this; if (balance < 0) balance = ASHClothingSettings.I.startingCurrency; }
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

        /// <summary>Sells amount of item from the inventory for its sellValue each (vendor hook). Returns AC earned.</summary>
        public int Sell(ASHInventory inv, ASHItemDefinition item, int amount = 1)
        {
            if (!inv || !item || item.keyItem || item.sellValue <= 0 || amount <= 0) return 0;
            int n = Mathf.Min(amount, inv.Count(item)); if (n <= 0 || !inv.Remove(item, n)) return 0;
            Earn(n * item.sellValue); return n * item.sellValue;
        }

        [Serializable] sealed class State { public int balance; }
        public string SaveKey => "wallet";
        public int RestoreOrder => 5;
        public string CaptureJson() => JsonUtility.ToJson(new State { balance = Balance });
        public void RestoreJson(string json)
        {
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json);
            balance = s != null ? Mathf.Max(0, s.balance) : ASHClothingSettings.I.startingCurrency;
            Changed?.Invoke();
        }
    }
}
