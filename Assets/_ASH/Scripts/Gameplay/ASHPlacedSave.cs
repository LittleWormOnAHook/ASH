using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>"placed" save section: player-placed objects (respawned from their item's placeablePrefab) plus the lit state
    /// and fuel of every campfire, placed or authored in the scene (by scene path id, like ASHWorldSave). Saves without the
    /// section keep the scene's defaults.</summary>
    public sealed class ASHPlacedSave : IASHSaveable
    {
        [Serializable] sealed class Fire { public string id; public bool lit; public float fuel; public string cook; }
        [Serializable] sealed class Placed { public string item; public Vector3 pos; public Quaternion rot; public bool hasFire; public bool lit; public float fuel; public string cook; public bool hasSnare; public bool snareCaught; public float snareHours; public string snareCaughtKind; }
        [Serializable] sealed class State { public List<Fire> fires = new List<Fire>(); public List<Placed> placed = new List<Placed>(); }

        readonly Dictionary<string, ASHCampfire> _sceneFires = new Dictionary<string, ASHCampfire>();

        public string SaveKey => "placed";
        public int RestoreOrder => 12;

        /// <summary>Records the scene's own campfires (called on scene load, before anything is placed).</summary>
        public void Snapshot()
        {
            _sceneFires.Clear();
            ASHCookStoves.EnsureAll(); // stoves / furnaces become cook stations first, so they are saved like campfires
            foreach (var f in UnityEngine.Object.FindObjectsByType<ASHCampfire>(FindObjectsInactive.Include))
            {
                if (f.GetComponent<ASHPlacedObject>() || f.name == "ASH_PlacementGhost") continue;
                var id = ASHWorldSave.IdOf(f.transform); if (!_sceneFires.ContainsKey(id)) _sceneFires[id] = f;
            }
        }

        public string CaptureJson()
        {
            var s = new State();
            foreach (var kv in _sceneFires) if (kv.Value) s.fires.Add(new Fire { id = kv.Key, lit = kv.Value.Lit, fuel = kv.Value.FuelHours, cook = ASHFireCooking.Capture(kv.Value) });
            foreach (var p in ASHPlacedObject.All)
            {
                if (!p || string.IsNullOrEmpty(p.itemId)) continue;
                var f = p.GetComponent<ASHCampfire>();
                var sn = p.GetComponent<ASHSnare>();
                s.placed.Add(new Placed { item = p.itemId, pos = p.transform.position, rot = p.transform.rotation, hasFire = f, lit = f && f.Lit, fuel = f ? f.FuelHours : 0f, cook = f ? ASHFireCooking.Capture(f) : null, hasSnare = sn, snareCaught = sn && sn.HasCatch, snareHours = sn ? sn.WaitedHours : 0f, snareCaughtKind = sn ? sn.CaughtKind : null });
            }
            return JsonUtility.ToJson(s);
        }

        public void RestoreJson(string json)
        {
            var s = JsonUtility.FromJson<State>(json); if (s == null) return;
            if (s.fires != null) foreach (var f in s.fires) if (_sceneFires.TryGetValue(f.id, out var c) && c) { c.RestoreState(f.lit, f.fuel); ASHFireCooking.Restore(c, f.cook); }
            if (s.placed == null) return;
            var reg = ASHItemRegistry.I;
            foreach (var p in s.placed)
            {
                var it = reg.Find(p.item); if (!it || !it.placeablePrefab) continue;
                var go = UnityEngine.Object.Instantiate(it.placeablePrefab, p.pos, p.rot);
                go.name = it.placeablePrefab.name;
                go.AddComponent<ASHPlacedObject>().itemId = p.item;
                if (p.hasFire) { var c = go.GetComponent<ASHCampfire>(); if (c) { c.RestoreState(p.lit, p.fuel); ASHFireCooking.Restore(c, p.cook); } }
                if (p.hasSnare) { var sn = go.GetComponent<ASHSnare>(); if (sn) sn.RestoreState(p.snareCaught, p.snareHours, p.snareCaughtKind); }
            }
        }
    }
}
