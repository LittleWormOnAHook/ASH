using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>M1 (t440): runs <see cref="ASHObjectiveChain"/> on the objective tracker. Added at Play by the prologue director.
    /// Saves which steps are done / shown and every recipe crafted (so a craft made early still counts later).</summary>
    public sealed class ASHObjectiveChainRunner : MonoBehaviour, IASHSaveable
    {
        public static ASHObjectiveChainRunner Instance { get; private set; }
        public static bool Drives { get { var r = Instance; var c = ASHObjectiveChain.I; return r && r.isActiveAndEnabled && c && c.chainEnabled && ASHPrologueDirector.TakesOverOpening; } }
        static ASHObjectiveChain C => ASHObjectiveChain.I;

        readonly HashSet<string> _done = new HashSet<string>(), _shown = new HashSet<string>(), _crafted = new HashSet<string>();
        ASHObjectiveTracker _tracker; ASHCrafting _craft; ASHInventory _inv; float _t;

        void Awake() { Instance = this; }
        void OnEnable() { ASHSaveSystem.Register(this); }
        void OnDisable() { ASHSaveSystem.Unregister(this); if (_craft) _craft.Crafted -= OnCrafted; _craft = null; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Cache()
        {
            if (!_tracker) _tracker = FindAnyObjectByType<ASHObjectiveTracker>();
            if (!_craft) { _craft = FindAnyObjectByType<ASHCrafting>(); if (_craft) _craft.Crafted += OnCrafted; }
            if (!_inv) _inv = FindAnyObjectByType<ASHInventory>();
        }
        void OnCrafted(ASHRecipe r) { if (r && _crafted.Add(r.name)) _t = 0f; }

        void Update()
        {
            if (!Drives) return;
            var d = ASHPrologueDirector.Instance; if (!d || d.Current == ASHPrologueDirector.Phase.NotStarted) return;
            if (ASHSaveSystem.IsRestoring) return;
            _t -= Time.deltaTime; if (_t > 0f) return; _t = 0.25f;
            Tick(true);
        }

        /// <summary>Shows every step whose prerequisites are done and completes every satisfied one (repeats until stable).</summary>
        public void Tick(bool announce)
        {
            Cache(); if (!_tracker || !C) return;
            for (int pass = 0; pass < 24; pass++)
            {
                bool changed = false;
                foreach (var s in C.steps)
                {
                    if (s == null || string.IsNullOrEmpty(s.id) || _done.Contains(s.id)) continue;
                    bool sat = Satisfied(s) || TrackerDone(s.id);
                    if (!_shown.Contains(s.id))
                    {
                        if (!PrereqsDone(s) || !Flag(s.revealFlag, true)) continue;
                        Show(s, announce && !sat); changed = true;
                    }
                    if (sat) { Finish(s); changed = true; }
                }
                if (!changed) break;
            }
        }

        bool PrereqsDone(ASHObjectiveChain.Step s) { foreach (var p in s.prerequisites) if (!string.IsNullOrEmpty(p) && !_done.Contains(p) && !TrackerDone(p)) return false; return true; }
        bool TrackerDone(string id) { var o = _tracker ? _tracker.Find(id) : null; return o != null && o.Completed; }
        static bool Flag(string f, bool ifEmpty) { if (string.IsNullOrEmpty(f)) return ifEmpty; if (f.StartsWith("page:")) return ASHGrandpaJournal.Has("grandpa", f.Substring(5)); var d = ASHPrologueDirector.Instance; return d && d.ChainFlag(f); }

        bool Satisfied(ASHObjectiveChain.Step s)
        {
            bool any = false, ok = false;
            if (!string.IsNullOrEmpty(s.completeFlag)) { any = true; ok |= Flag(s.completeFlag, false); }
            if (!string.IsNullOrEmpty(s.recipeName)) { any = true; ok |= Crafted(s.recipeName); }
            return any && ok;
        }
        bool Crafted(string recipeName)
        {
            if (_crafted.Contains(recipeName)) return true;
            return false; // crafted only: owning the output (e.g. supply bandages) is not crafting it
        }
        static ASHRecipe Recipe(string name)
        {
            var fm = ASHFieldManualData.I; if (!fm) return null;
            foreach (var e in fm.entries) if (e != null && e.recipe && e.recipe.name == name) return e.recipe;
            return null;
        }

        void Show(ASHObjectiveChain.Step s, bool announce)
        {
            _shown.Add(s.id);
            Register(s);
            if (!announce) return;
            var hud = FindAnyObjectByType<ASHHud>(); if (!hud) return;
            if (s.kind == ASHObjectiveChain.Kind.Main) { if (C.announceMain && !string.IsNullOrEmpty(s.hint)) hud.ShowSubtitle(s.hint, C.hintSeconds); }
            else if (C.announceSide) hud.Toast((s.kind == ASHObjectiveChain.Kind.Craft ? "New crafting task: " : "New task: ") + s.title);
            Debug.Log($"[ASH] Chain: show {s.id} '{s.title}'");
        }

        void Register(ASHObjectiveChain.Step s)
        {
            var d = ASHPrologueDirector.Instance;
            Func<IEnumerable<(string, bool)>> lines = null; Func<Vector3?> target = null;
            if (s.kind == ASHObjectiveChain.Kind.Main && d) d.ChainContent(s.id, out lines, out target);
            if (lines == null)
            {
                var step = s; // lines read the asset live (edits in Play show at once)
                lines = () => StepLines(step);
            }
            bool main = s.kind == ASHObjectiveChain.Kind.Main;
            var o = _tracker.Register(s.id, s.title, lines, main && !s.final, main, target);
            o.Final = s.final;
        }
        IEnumerable<(string, bool)> StepLines(ASHObjectiveChain.Step s)
        {
            bool sat = Satisfied(s);
            if (!string.IsNullOrEmpty(s.recipeName)) { var r = Recipe(s.recipeName); yield return ($"Craft: {(r && r.outputItem ? r.outputItem.displayName : s.title)}", sat); }
            if (!string.IsNullOrEmpty(s.hint)) yield return (s.hint, sat);
        }

        void Finish(ASHObjectiveChain.Step s)
        {
            _done.Add(s.id);
            if (_tracker && !TrackerDone(s.id)) _tracker.Complete(s.id, s.kind == ASHObjectiveChain.Kind.Main ? null : $"Done: {s.title}");
            Debug.Log($"[ASH] Chain: done {s.id}");
        }

        /// <summary>Debug: one line per step (shown / done / satisfied).</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            if (C) foreach (var s in C.steps) if (s != null) sb.Append($"{s.id}:{(_done.Contains(s.id) ? "done" : _shown.Contains(s.id) ? "shown" : "hidden")}{(Satisfied(s) ? "*" : "")} ");
            sb.Append(" crafted=").Append(string.Join(",", _crafted));
            return sb.ToString();
        }

        // ---- save
        [Serializable] sealed class State { public List<string> done = new List<string>(), shown = new List<string>(), crafted = new List<string>(); }
        public string SaveKey => "objectiveChain";
        public int RestoreOrder => 46; // after the prologue director (45): its flags are back
        public string CaptureJson() => JsonUtility.ToJson(new State { done = new List<string>(_done), shown = new List<string>(_shown), crafted = new List<string>(_crafted) });
        public void RestoreJson(string json)
        {
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json); if (s == null) return;
            _done.Clear(); _shown.Clear(); _crafted.Clear();
            foreach (var x in s.done) _done.Add(x); foreach (var x in s.crafted) _crafted.Add(x);
            Cache(); if (!_tracker || !C) return;
            foreach (var id in s.shown) { _shown.Add(id); if (_done.Contains(id) || TrackerDone(id)) continue; var st = C.Get(id); if (st != null) Register(st); }
            _t = 0f;
        }
    }
}
