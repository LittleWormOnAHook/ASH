using System.Collections.Generic;
using ASH.Interaction;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>Grandpa's supplies in the cabin (P01): hold E to take the exact starting kit from ASH_PrologueSettings.supplies.
    /// What doesn't fit stays in the box. Saved by the prologue director (no duplicates on load or retry).</summary>
    public sealed class ASHGrandpaSupplies : MonoBehaviour, IASHUsable
    {
        [Range(0f, 3f)] public float holdSeconds = 0.6f;
        public string label = "Grandpa's supplies";
        /// <summary>Amounts left per settings entry (null = untouched, full kit).</summary>
        List<int> _left;
        public bool Taken { get { if (_left == null) return false; foreach (var n in _left) if (n > 0) return false; return true; } }
        public bool Touched => _left != null;
        public event System.Action Changed;
        public Transform UseTransform => transform;
        public float GetUsePriority(in ASHUseContext c) => Taken ? -1f : 55f;
        public float GetHoldDuration(in ASHUseContext c) => holdSeconds;
        public string GetPrompt(in ASHUseContext c) => Touched ? $"Take the rest of {label}" : $"Take {label}";

        void EnsureLeft()
        {
            var s = ASHPrologueSettings.I; if (_left != null || !s) return;
            _left = new List<int>(); foreach (var e in s.supplies) _left.Add(e != null && e.item ? e.amount : 0);
        }

        public bool TryUse(in ASHUseContext c) => Take(c.Inventory ? c.Inventory : FindAnyObjectByType<ASHInventory>());

        /// <summary>Moves what fits into the pack. True when anything was taken.</summary>
        public bool Take(ASHInventory inv)
        {
            var s = ASHPrologueSettings.I;
            if (!s || !inv || Taken) return false;
            EnsureLeft();
            var got = new List<string>(); bool full = false;
            for (int i = 0; i < s.supplies.Count && i < _left.Count; i++)
            {
                var e = s.supplies[i]; if (e == null || !e.item || _left[i] <= 0) continue;
                int added = inv.AddItem(e.item, _left[i]);
                if (added > 0) { got.Add(added > 1 ? $"{e.item.displayName} x{added}" : e.item.displayName); _left[i] -= added; }
                if (_left[i] > 0) full = true;
            }
            var h = FindAnyObjectByType<ASHHud>();
            if (h) h.Toast(got.Count > 0 ? "Took: " + string.Join(", ", got) + (full ? ". No room for the rest." : "") : "No room in your pack.");
            Changed?.Invoke();
            return got.Count > 0;
        }

        public List<int> Capture() => _left != null ? new List<int>(_left) : null;
        public void Restore(List<int> left, bool touched) { _left = touched && left != null ? new List<int>(left) : null; Changed?.Invoke(); }
    }
}
