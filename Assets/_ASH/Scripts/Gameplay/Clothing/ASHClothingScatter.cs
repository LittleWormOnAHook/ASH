using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Interaction;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Clothing C4 world scatter: on a new game, drops ASH_ClothingSettings.scatterCount clothing pieces (rarity weights, rolled
    /// condition) near existing loot spots: supply crates and scene pickups (test items and the vendor excluded). They are runtime drops,
    /// so the world save keeps them; this component saves only that the scatter happened ("clothingScatter").</summary>
    public sealed class ASHClothingScatter : MonoBehaviour, IASHSaveable
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHClothingScatter() { Instance = null; }
        public static ASHClothingScatter Instance { get; private set; }

        bool _done; float _at;

        public static void Ensure()
        {
            if (Instance) return;
            var inv = FindAnyObjectByType<ASHInventory>(); if (!inv) return;
            var c = inv.GetComponent<ASHClothingScatter>(); if (!c) inv.gameObject.AddComponent<ASHClothingScatter>();
        }

        void Awake() { Instance = this; _at = Time.unscaledTime + ASHClothingSettings.I.scatterDelay; }
        void OnEnable() { Instance = this; ASHSaveSystem.Register(this); }
        void OnDisable() { ASHSaveSystem.Unregister(this); if (Instance == this) Instance = null; }

        void Update()
        {
            if (_done || ASHSaveSystem.IsRestoring || Time.unscaledTime < _at) return;
            _done = true;
            var S = ASHClothingSettings.I; if (!S.worldScatter || S.scatterCount <= 0) return;
            int n = Scatter(S);
            Debug.Log($"[ASH] Clothing scatter: {n} pieces placed near loot spots.");
        }

        static bool Excluded(Transform t)
        {
            for (var x = t; x; x = x.parent) if (x.name == "ASH_TestItems" || x.name == "ASH_Vendor" || x.name == "ASH_LegendaryFinds") return true;
            return false;
        }

        static ASHItemDefinition Pick(ASHClothingSettings S, System.Random rng)
        {
            var pool = S.crateClothing; if (pool == null || pool.Count == 0) return null;
            float total = 0f;
            foreach (var it in pool) if (it && (S.legendaryInScatter || it.rarity != ASHItemRarity.Legendary)) total += S.RarityWeight(it.rarity);
            if (total <= 0f) return null;
            float r = (float)rng.NextDouble() * total, acc = 0f;
            foreach (var it in pool)
            {
                if (!it || (!S.legendaryInScatter && it.rarity == ASHItemRarity.Legendary)) continue;
                acc += S.RarityWeight(it.rarity); if (r <= acc) return it;
            }
            return null;
        }

        static int Scatter(ASHClothingSettings S)
        {
            var spots = new List<Vector3>();
            foreach (var c in FindObjectsByType<ASHSupplyCrate>(FindObjectsInactive.Exclude)) if (!Excluded(c.transform)) spots.Add(c.transform.position);
            foreach (var p in FindObjectsByType<ASHItemPickup>(FindObjectsInactive.Exclude)) if (!Excluded(p.transform)) spots.Add(p.transform.position);
            if (spots.Count == 0) return 0;
            var rng = S.scatterSeed != 0 ? new System.Random(S.scatterSeed) : new System.Random(Environment.TickCount);
            int placed = 0;
            for (int k = 0; k < S.scatterCount * 4 && placed < S.scatterCount; k++)
            {
                var it = Pick(S, rng); if (!it) break;
                var c = spots[rng.Next(spots.Count)];
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = Mathf.Lerp(S.scatterMinDistance, Mathf.Max(S.scatterMinDistance, S.scatterMaxDistance), (float)rng.NextDouble());
                var top = c + new Vector3(Mathf.Cos(a) * d, 6f, Mathf.Sin(a) * d);
                if (!Physics.Raycast(top, Vector3.down, out var hit, 14f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Vector3.Angle(hit.normal, Vector3.up) > 35f) continue;
                if (Physics.CheckSphere(hit.point + Vector3.up * 0.3f, 0.2f, ~0, QueryTriggerInteraction.Ignore)) continue; // inside a prop / wall
                var pk = ASHItemDrop.SpawnAt(it, 1, hit.point + Vector3.up * 0.02f, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                if (!pk) continue;
                pk.SetCondition(Mathf.Lerp(S.foundConditionMin, S.foundConditionMax, (float)rng.NextDouble()));
                placed++;
            }
            return placed;
        }

        [Serializable] sealed class State { public bool done; }
        public string SaveKey => "clothingScatter";
        public int RestoreOrder => 30;
        public string CaptureJson() => JsonUtility.ToJson(new State { done = _done });
        public void RestoreJson(string json) { var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json); _done = s == null || s.done; }
    }
}
