using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Core
{
    /// <summary>
    /// Tracks which full-screen panels are open (inventory, crafting, pause, placement) so gameplay input,
    /// cursor lock and camera look can be gated from one place without hidden scene lookups.
    /// Modelled on the Genesis PlayerController "BlocksCombatInput"/cursor flags, reduced to a token set.
    /// </summary>
    public static class ASHUIState
    {
        private static readonly HashSet<string> OpenPanels = new HashSet<string>();
        public static event Action Changed;

        /// <summary>True when any cursor-requiring panel is open: movement/look/interaction should pause.</summary>
        public static bool BlocksGameplay => OpenPanels.Count > 0;
        public static bool IsOpen(string panel) => OpenPanels.Contains(panel);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { OpenPanels.Clear(); Changed = null; }

        public static void SetOpen(string panel, bool open)
        {
            bool changed = open ? OpenPanels.Add(panel) : OpenPanels.Remove(panel);
            if (!changed) return;
            ApplyCursor();
            Changed?.Invoke();
        }

        public static void ApplyCursor()
        {
            bool free = BlocksGameplay;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }

    public static class ASHPanels
    {
        public const string Inventory = "inventory";
        public const string Pause = "pause";
        public const string Death = "death";
        public const string Craft = "craft";
    }
}
