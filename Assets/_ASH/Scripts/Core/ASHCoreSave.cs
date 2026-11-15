using System;
using System.Collections.Generic;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Map;
using ASH.Opening;
using ASH.Survival;
using ASH.World;
using ASH.World.Accumulation;
using ASH.World.Weather;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Core
{
    /// <summary>Core save section: player pose, health/needs, pack + hotbar + cross bar (with per-slot condition), held items,
    /// objectives (+ tracked), journal notes, opening stage, weather state, time of day and ground accumulation.</summary>
    public sealed class ASHCoreSave : IASHSaveable
    {
        [Serializable] public sealed class SlotState { public string id; public int amount; public float condition = 1f; }
        [Serializable] public sealed class NoteState { public string title, body, stamp; }
        [Serializable]
        sealed class State
        {
            public Vector3 pos; public Quaternion rot = Quaternion.identity; public bool hasPose;
            public float health = -1f, hunger = -1f, thirst = -1f;
            public List<SlotState> slots = new List<SlotState>();
            public string main, off, hip;
            public string objectives;
            public List<NoteState> notes = new List<NoteState>();
            public int openingStage = -1; public bool aftershocksOver, openingSuppressed, openingObjectiveDone;
            public int weatherKind = -1; public bool scheduler;
            public float timeOfDay = -1f;
            public float ash = -1f, snow = -1f, wet = -1f;
        }

        /// <summary>Optional location label provider (shelter zones name the place the player is in).</summary>
        public static Func<string> LocationOverride;

        public string SaveKey => "core";
        public int RestoreOrder => 0;

        static vThirdPersonController Player() => UnityEngine.Object.FindAnyObjectByType<vThirdPersonController>();

        public string CaptureJson()
        {
            var s = new State();
            var cc = Player();
            if (cc) { s.pos = cc.transform.position; s.rot = cc.transform.rotation; s.hasPose = true; }
            var stats = UnityEngine.Object.FindAnyObjectByType<ASHSurvivalStats>();
            if (stats) { s.health = stats.Health; s.hunger = stats.Hunger; s.thirst = stats.Thirst; }
            var inv = UnityEngine.Object.FindAnyObjectByType<ASHInventory>();
            if (inv) foreach (var sl in inv.Slots) s.slots.Add(sl.IsEmpty ? new SlotState() : new SlotState { id = sl.item.itemId, amount = sl.amount, condition = sl.condition });
            var eq = UnityEngine.Object.FindAnyObjectByType<ASHEquipment>();
            if (eq) { s.main = eq.Main ? eq.Main.itemId : null; s.off = eq.Off ? eq.Off.itemId : null; s.hip = eq.Hip ? eq.Hip.itemId : null; }
            var tr = UnityEngine.Object.FindAnyObjectByType<ASHObjectiveTracker>();
            if (tr) s.objectives = tr.CaptureJson();
            var log = ASHJournalLog.Instance ? ASHJournalLog.Instance : UnityEngine.Object.FindAnyObjectByType<ASHJournalLog>();
            if (log) foreach (var e in log.Entries) s.notes.Add(new NoteState { title = e.title, body = e.body, stamp = e.stamp });
            var seq = ASHEruptionSequence.Instance ? ASHEruptionSequence.Instance : UnityEngine.Object.FindAnyObjectByType<ASHEruptionSequence>();
            if (seq) { s.openingStage = (int)seq.Current; s.aftershocksOver = seq.AftershocksOver; s.openingSuppressed = seq.Suppressed; s.openingObjectiveDone = seq.ObjectiveFinishedEarly; }
            var dir = ASHWeatherDirector.Instance;
            if (dir) { s.weatherKind = (int)dir.TargetKind; s.scheduler = dir.schedulerEnabled; }
            var clock = UnityEngine.Object.FindAnyObjectByType<ASHDayNightClock>();
            if (clock) s.timeOfDay = clock.timeOfDay;
            var acc = ASHAccumulationController.Instance;
            if (acc) { s.ash = acc.ashAmount; s.snow = acc.snowAmount; s.wet = acc.wetness; }
            return JsonUtility.ToJson(s);
        }

        public void RestoreJson(string json)
        {
            var s = JsonUtility.FromJson<State>(json); if (s == null) return;
            // opening first (restarting a stage resets doors/weather/time/ground), then the saved time, ground and weather on top
            var seq = ASHEruptionSequence.Instance ? ASHEruptionSequence.Instance : UnityEngine.Object.FindAnyObjectByType<ASHEruptionSequence>();
            if (seq && s.openingStage >= 0) seq.RestoreFromSave((ASHEruptionSequence.Stage)s.openingStage, s.aftershocksOver, s.openingSuppressed, s.openingObjectiveDone);
            var clock = UnityEngine.Object.FindAnyObjectByType<ASHDayNightClock>();
            if (clock && s.timeOfDay >= 0f) clock.timeOfDay = s.timeOfDay;
            var acc = ASHAccumulationController.Instance;
            if (acc) { if (s.ash >= 0f) acc.ashAmount = s.ash; if (s.snow >= 0f) acc.snowAmount = s.snow; if (s.wet >= 0f) acc.wetness = s.wet; }
            var dir = ASHWeatherDirector.Instance;
            if (dir && s.weatherKind >= 0) { dir.debugOverride = false; dir.Snap((ASHWeatherKind)s.weatherKind); dir.SetScheduler(s.scheduler); }

            // pack
            var inv = UnityEngine.Object.FindAnyObjectByType<ASHInventory>();
            if (inv && s.slots.Count > 0)
            {
                var reg = ASHItemRegistry.I;
                int n = Mathf.Min(inv.Slots.Count, s.slots.Count);
                for (int i = 0; i < inv.Slots.Count; i++)
                {
                    var st = i < n ? s.slots[i] : null;
                    var item = st != null && !string.IsNullOrEmpty(st.id) ? reg.Find(st.id) : null;
                    if (st != null && !string.IsNullOrEmpty(st.id) && item == null) Debug.LogWarning($"[ASH] Save: unknown item '{st.id}' (run ASH/Systems/Install Data to refresh the item registry)");
                    inv.RestoreSlot(i, item, item ? st.amount : 0, st != null ? st.condition : 1f);
                }
                inv.NotifyChanged();
            }
            var eq = UnityEngine.Object.FindAnyObjectByType<ASHEquipment>();
            if (eq)
            {
                var reg = ASHItemRegistry.I;
                foreach (var id in new[] { s.main, s.off, s.hip }) { var it = string.IsNullOrEmpty(id) ? null : reg.Find(id); if (it && !eq.IsEquipped(it) && inv && inv.Count(it) > 0) eq.Equip(it); }
            }
            var stats = UnityEngine.Object.FindAnyObjectByType<ASHSurvivalStats>();
            if (stats && s.health >= 0f) stats.RestoreValues(s.health, s.hunger, s.thirst);
            var tr = UnityEngine.Object.FindAnyObjectByType<ASHObjectiveTracker>();
            if (tr && !string.IsNullOrEmpty(s.objectives)) tr.RestoreJson(s.objectives);
            var log = ASHJournalLog.Instance ? ASHJournalLog.Instance : UnityEngine.Object.FindAnyObjectByType<ASHJournalLog>();
            if (log) { var list = new List<ASHJournalLog.Entry>(); foreach (var e in s.notes) list.Add(new ASHJournalLog.Entry { title = e.title, body = e.body, stamp = e.stamp }); log.RestoreEntries(list); }

            var cc = Player();
            if (cc && s.hasPose)
            {
                var rb = cc.GetComponent<Rigidbody>(); if (rb) { rb.linearVelocity = Vector3.zero; rb.position = s.pos + Vector3.up * 0.05f; rb.rotation = s.rot; }
                cc.transform.SetPositionAndRotation(s.pos + Vector3.up * 0.05f, s.rot);
            }
        }

        public string LocationName()
        {
            var o = LocationOverride?.Invoke(); if (!string.IsNullOrEmpty(o)) return o;
            var cc = Player(); if (!cc) return "Unknown";
            ASHSurveyPoint best = null; float bd = float.MaxValue;
            foreach (var p in UnityEngine.Object.FindObjectsByType<ASHSurveyPoint>(FindObjectsSortMode.None))
            {
                if (!p.Definition) continue; float d = (p.transform.position - cc.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (best != null && bd < 600f * 600f) return (bd < 60f * 60f ? "" : "Near ") + best.Definition.displayName;
            return "The wilds";
        }

        public string WeatherName()
        {
            var d = ASHWeatherDirector.Instance; if (!d) return "";
            return WeatherLabel(d.TargetKind);
        }

        public static string WeatherLabel(ASHWeatherKind k)
        {
            switch (k)
            {
                case ASHWeatherKind.Clear: return "Clear";
                case ASHWeatherKind.CalmHazy: return "Hazy";
                case ASHWeatherKind.LightAsh: return "Light ash";
                case ASHWeatherKind.HeavyAsh: return "Heavy ash";
                case ASHWeatherKind.RainAsh: return "Ash rain";
                case ASHWeatherKind.Snowstorm: return "Snowstorm";
                case ASHWeatherKind.Thunderstorm: return "Thunderstorm";
                case ASHWeatherKind.LightSnowMorning: return "Light snow";
            }
            return k.ToString();
        }

        public string TimeText()
        {
            var c = UnityEngine.Object.FindAnyObjectByType<ASHDayNightClock>(); if (!c) return "";
            int h = (int)c.timeOfDay; int m = (int)((c.timeOfDay - h) * 60f); return $"{h:00}:{m:00}";
        }
    }

    /// <summary>World save section: felled trees / chopped logs, depleted resource nodes, taken or partly taken scene pickups and
    /// items the player dropped. Scene objects are identified by a hierarchy path recorded when the scene loads (before anything
    /// changes), so the ids match between the saving run and the restoring run.</summary>
    public sealed class ASHWorldSave : IASHSaveable
    {
        [Serializable] sealed class Node { public string id; public int remaining; }
        [Serializable] sealed class Pick { public string id; public int amount; }
        [Serializable] sealed class Drop { public string item; public int amount; public float condition = 1f; public Vector3 pos; public Quaternion rot; }
        [Serializable]
        sealed class State
        {
            public List<string> felled = new List<string>();
            public List<Node> nodes = new List<Node>();
            public List<Pick> pickups = new List<Pick>();   // amount 0 = taken
            public List<Drop> drops = new List<Drop>();
        }

        readonly Dictionary<string, ASHChoppable> _chop = new Dictionary<string, ASHChoppable>();
        readonly Dictionary<string, ASHResourceNode> _nodes = new Dictionary<string, ASHResourceNode>();
        readonly Dictionary<string, (ASHItemPickup pk, int amount)> _picks = new Dictionary<string, (ASHItemPickup, int)>();
        readonly HashSet<ASHItemPickup> _scenePicks = new HashSet<ASHItemPickup>();

        public string SaveKey => "world";
        public int RestoreOrder => 10;

        public static string IdOf(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            for (var x = t; x != null; x = x.parent) sb.Insert(0, "/" + x.name + "#" + x.GetSiblingIndex());
            return t.gameObject.scene.name + sb;
        }

        /// <summary>Records every scene choppable, resource node and pickup with its id (called on scene load).</summary>
        public void Snapshot()
        {
            _chop.Clear(); _nodes.Clear(); _picks.Clear(); _scenePicks.Clear();
            foreach (var c in UnityEngine.Object.FindObjectsByType<ASHChoppable>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { var id = IdOf(c.transform); if (!_chop.ContainsKey(id)) _chop[id] = c; }
            foreach (var n in UnityEngine.Object.FindObjectsByType<ASHResourceNode>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { var id = IdOf(n.transform); if (!_nodes.ContainsKey(id)) _nodes[id] = n; }
            foreach (var p in UnityEngine.Object.FindObjectsByType<ASHItemPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { var id = IdOf(p.transform); if (!_picks.ContainsKey(id)) { _picks[id] = (p, p.Amount); _scenePicks.Add(p); } }
        }

        public string CaptureJson()
        {
            var s = new State();
            foreach (var kv in _chop) if (kv.Value && kv.Value.Done) s.felled.Add(kv.Key);
            foreach (var kv in _nodes) if (kv.Value && kv.Value.Remaining < kv.Value.MaxGathers) s.nodes.Add(new Node { id = kv.Key, remaining = kv.Value.Remaining });
            foreach (var kv in _picks)
            {
                var pk = kv.Value.pk;
                if (!pk) s.pickups.Add(new Pick { id = kv.Key, amount = 0 });
                else if (pk.Amount != kv.Value.amount) s.pickups.Add(new Pick { id = kv.Key, amount = pk.Amount });
            }
            foreach (var pk in UnityEngine.Object.FindObjectsByType<ASHItemPickup>(FindObjectsSortMode.None))
            {
                if (_scenePicks.Contains(pk) || !pk.Item || pk.Amount <= 0) continue;
                s.drops.Add(new Drop { item = pk.Item.itemId, amount = pk.Amount, condition = pk.Condition, pos = pk.transform.position, rot = pk.transform.rotation });
            }
            return JsonUtility.ToJson(s);
        }

        public void RestoreJson(string json)
        {
            var s = JsonUtility.FromJson<State>(json); if (s == null) return;
            foreach (var id in s.felled) if (_chop.TryGetValue(id, out var c) && c) c.RestoreFelled();
            foreach (var n in s.nodes) if (_nodes.TryGetValue(n.id, out var r) && r) r.RestoreRemaining(n.remaining);
            foreach (var p in s.pickups)
            {
                if (!_picks.TryGetValue(p.id, out var e) || !e.pk) continue;
                if (p.amount <= 0) UnityEngine.Object.Destroy(e.pk.gameObject); else e.pk.Configure(e.pk.Item, p.amount);
            }
            var reg = ASHItemRegistry.I;
            foreach (var d in s.drops)
            {
                var it = reg.Find(d.item); if (!it) continue;
                var pk = ASHItemDrop.SpawnAt(it, d.amount, d.pos, d.rot);
                if (pk) pk.SetCondition(d.condition);
            }
        }
    }
}
