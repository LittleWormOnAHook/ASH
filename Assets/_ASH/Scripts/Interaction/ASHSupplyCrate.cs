using System.Collections.Generic;
using ASH.Inventory;
using ASH.UI;
using ASH.World;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>
    /// World crate the player can open (E) for a small random assortment from a loot table. Once opened it stays
    /// empty (saved with the world section). Placed around POIs by ASH/Systems/Resources/Scatter Around POIs.
    /// </summary>
    public sealed class ASHSupplyCrate : MonoBehaviour, IASHUsable
    {
        [SerializeField] List<ASHPoiResourceSettings.LootEntry> loot = new List<ASHPoiResourceSettings.LootEntry>();
        [SerializeField] [Range(1, 12)] int lootMin = 2;
        [SerializeField] [Range(1, 12)] int lootMax = 5;
        [SerializeField] [Range(0f, 4f)] float holdSeconds = 0.35f;
        [SerializeField] Transform lid;

        public bool Opened { get; private set; }
        public Transform UseTransform => transform;

        public void Configure(ASHPoiResourceSettings s)
        {
            if (!s) return;
            lootMin = s.crateLootMin; lootMax = s.crateLootMax;
            loot = new List<ASHPoiResourceSettings.LootEntry>();
            if (s.crateLoot != null) foreach (var e in s.crateLoot) if (e != null && e.item) loot.Add(new ASHPoiResourceSettings.LootEntry { item = e.item, weight = e.weight, amountMin = e.amountMin, amountMax = e.amountMax });
        }

        public void RestoreOpened() { Opened = true; ApplyOpened(); }

        public float GetUsePriority(in ASHUseContext c) => Opened ? -1f : 50f;
        public float GetHoldDuration(in ASHUseContext c) => holdSeconds;
        public string GetPrompt(in ASHUseContext c) => Opened ? "Empty supply crate" : "Open supply crate";

        public bool TryUse(in ASHUseContext c)
        {
            if (Opened || c.Inventory == null) return false;
            Opened = true;
            var drops = Roll();
            int given = 0, spilled = 0;
            foreach (var (item, amount) in drops)
            {
                int added = c.Inventory.AddItem(item, amount);
                given += added;
                int left = amount - added;
                if (left > 0)
                {
                    var pk = ASHItemDrop.SpawnAt(item, left, transform.position + transform.forward * 0.4f + Vector3.up * 0.2f, Quaternion.identity);
                    if (pk) spilled += left;
                }
            }
            ApplyOpened();
            var hud = Object.FindAnyObjectByType<ASHHud>();
            if (hud)
            {
                if (drops.Count == 0) hud.Toast("The crate is empty.");
                else if (spilled > 0) hud.Toast(given > 0 ? "Crate opened — some items fell out (pack full)." : "Crate opened — pack full, items dropped.");
                else hud.Toast("Supply crate opened.");
            }
            return true;
        }

        List<(ASHItemDefinition item, int amount)> Roll()
        {
            var result = new List<(ASHItemDefinition, int)>();
            float total = 0f; if (loot != null) foreach (var e in loot) if (e != null && e.item && e.weight > 0f) total += e.weight;
            if (total <= 0f) return result;
            int n = Random.Range(Mathf.Min(lootMin, lootMax), Mathf.Max(lootMin, lootMax) + 1);
            for (int i = 0; i < n; i++)
            {
                float r = Random.value * total, acc = 0f;
                ASHPoiResourceSettings.LootEntry pick = null;
                foreach (var e in loot)
                {
                    if (e == null || !e.item || e.weight <= 0f) continue;
                    acc += e.weight; if (r <= acc) { pick = e; break; }
                }
                if (pick == null) continue;
                int lo = Mathf.Max(1, Mathf.Min(pick.amountMin, pick.amountMax));
                int hi = Mathf.Max(1, Mathf.Max(pick.amountMin, pick.amountMax));
                result.Add((pick.item, Random.Range(lo, hi + 1)));
            }
            return result;
        }

        void ApplyOpened()
        {
            if (lid) lid.localRotation = Quaternion.Euler(-70f, 0f, 0f);
        }
    }
}
