using System;
using ASH.Core;
using ASH.Inventory;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.UI
{
    /// <summary>
    /// The top-of-screen compass (ASHCompass) stays hidden until the player uses the Compass item (ASH_Item_Compass, from the
    /// pack or the hotbar). Using it unlocks the compass for good (saved with the game) and removes the item.
    /// ASH_CompassSettings.compassAlwaysOn bypasses the gate for testing. One instance per scene, created automatically.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHCompassUnlock : MonoBehaviour, IASHSaveable
    {
        public const string ItemId = "compass";
        /// <summary>True once the Compass item has been used in this run (restored from the save).</summary>
        public static bool Unlocked { get; private set; }
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Unlocked = false; Changed = null; _hooked = false; }
        static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ASHHud.UseHandlers.Add(new UseCompassHandler());
            if (_hooked) return; _hooked = true;
            SceneManager.sceneLoaded += (s, m) => Ensure();
        }

        /// <summary>Runs after the scene's Awake/OnEnable and before Start, so the save system sees it before it restores.</summary>
        static void Ensure()
        {
            if (FindAnyObjectByType<ASHCompassUnlock>()) return;
            new GameObject("ASH_CompassUnlock").AddComponent<ASHCompassUnlock>();
        }

        void OnEnable() { Unlocked = false; ASHSaveSystem.Register(this); }
        void OnDisable() { ASHSaveSystem.Unregister(this); }

        public static void Unlock()
        {
            if (Unlocked) return;
            Unlocked = true; Changed?.Invoke();
        }

        sealed class UseCompassHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) => it && it.itemId == ItemId;
            public string UseLabel(ASHItemDefinition it) => "Use (adds the compass)";
            public bool Use(ASHItemDefinition it, int slot)
            {
                var inv = FindAnyObjectByType<ASHInventory>(); if (!inv) return false;
                int i = slot;
                if (i < 0 || i >= inv.Slots.Count || inv.Slots[i].IsEmpty || inv.Slots[i].item != it)
                { i = -1; for (int k = 0; k < inv.Slots.Count; k++) if (!inv.Slots[k].IsEmpty && inv.Slots[k].item == it) { i = k; break; } }
                if (i < 0) return false;
                inv.RemoveFromSlot(i, 1);
                Unlock();
                var h = FindAnyObjectByType<ASHHud>(); if (h) h.Toast("Compass added: heading shows at the top of the screen.");
                return true;
            }
        }

        // ------------------------------------------------------------------ save
        [Serializable] sealed class State { public bool unlocked; }
        public string SaveKey => "compassUnlock";
        public int RestoreOrder => 25;
        public string CaptureJson() => JsonUtility.ToJson(new State { unlocked = Unlocked });
        public void RestoreJson(string json)
        {
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json);
            bool was = Unlocked; Unlocked = s != null && s.unlocked;
            if (was != Unlocked) Changed?.Invoke();
        }
    }
}