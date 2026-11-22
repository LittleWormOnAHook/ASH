using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Survival;
using ASH.UI;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>M1 (t440): runs <see cref="ASHObjectiveChain"/> on the objective tracker. Added at Play by the prologue director.
    /// Saves which steps are done / shown and every recipe crafted (so a craft made early still counts later).</summary>
    public sealed class ASHObjectiveChainRunner : MonoBehaviour, IASHSaveable
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHObjectiveChainRunner()
        {
            Instance = default;
        }

        public static ASHObjectiveChainRunner Instance { get; private set; }
        public static bool Drives { get { var r = Instance; var c = ASHObjectiveChain.I; return r && r.isActiveAndEnabled && c && c.chainEnabled && ASHPrologueDirector.TakesOverOpening; } }
        static ASHObjectiveChain C => ASHObjectiveChain.I;

        readonly HashSet<string> _done = new HashSet<string>(), _shown = new HashSet<string>(), _crafted = new HashSet<string>();
        ASHObjectiveTracker _tracker; ASHCrafting _craft; ASHInventory _inv; ASH.Gameplay.ASHPlacer _placer; ASHSurvivalStats _stats; float _t; bool _restored;

        void Awake() { Instance = this; }
        void OnEnable() { ASHSaveSystem.Register(this); }
        void OnDisable() { ASHSaveSystem.Unregister(this); if (_craft) _craft.Crafted -= OnCrafted; _craft = null; if (_placer) _placer.PlacedObject -= OnPlaced; _placer = null; if (_inv) _inv.Changed -= OnInventoryChanged; _inv = null; if (_stats) _stats.Consumed -= OnConsumed; _stats = null; }
        // t475: anything entering / leaving the pack re-checks every objective at once (counts + auto-complete)
        void OnInventoryChanged() { _t = 0f; _countsDirty = true; }
        bool _countsDirty;

        /// <summary>t475: THE refresh for every objective type (main, side, craft): re-reads inventory + fitted/placed state, shows
        /// newly available steps, completes anything already satisfied and redraws the HUD counts. Used on reveal, inventory
        /// change, load and checkpoint restore.</summary>
        public void Refresh(bool announce)
        {
            Cache(); if (!_tracker || !C) return;
            Tick(announce);
            _countsDirty = false; _tracker.NotifyLinesChanged();
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Cache()
        {
            if (!_tracker) _tracker = FindAnyObjectByType<ASHObjectiveTracker>();
            if (!_craft) { _craft = FindAnyObjectByType<ASHCrafting>(); if (_craft) _craft.Crafted += OnCrafted; }
            if (!_inv) { _inv = FindAnyObjectByType<ASHInventory>(); if (_inv) _inv.Changed += OnInventoryChanged; }
            if (!_placer) { _placer = FindAnyObjectByType<ASH.Gameplay.ASHPlacer>(); if (_placer) _placer.PlacedObject += OnPlaced; }
            if (!_stats) { _stats = FindAnyObjectByType<ASHSurvivalStats>(); if (_stats) _stats.Consumed += OnConsumed; }
        }
        // t454: remember the recipe AND its output item, so any recipe making the same thing counts (campfire variants)
        void OnCrafted(ASHRecipe r) { if (!r) return; _crafted.Add(r.name); if (r.outputItem) _crafted.Add("out:" + r.outputItem.itemId); _t = 0f; }
        // t454: placing an item (e.g. a campfire from the pack) counts for steps with completeOnPlace
        void OnPlaced(ASHItemDefinition item, GameObject go) { if (item) { _crafted.Add("placed:" + item.itemId); _t = 0f; } }
        void OnConsumed(ASHItemDefinition item) { if (item && !string.IsNullOrEmpty(item.itemId)) { _crafted.Add("ate:" + item.itemId); _t = 0f; } }

        void Update()
        {
            if (!Drives) return;
            var d = ASHPrologueDirector.Instance; if (!d || d.Current == ASHPrologueDirector.Phase.NotStarted) return;
            if (ASHSaveSystem.IsRestoring) return;
            _t -= Time.deltaTime; if (_t > 0f) return; _t = 0.25f;
            if (_countsDirty || _restored) { _restored = false; Refresh(true); } else Tick(true);
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
                _countsDirty = true; // something was shown or finished: redraw the counts
            }
            if (_countsDirty && _tracker) { _countsDirty = false; _tracker.NotifyLinesChanged(); }
        }

        bool PrereqsDone(ASHObjectiveChain.Step s) { foreach (var p in s.prerequisites) if (!string.IsNullOrEmpty(p) && !_done.Contains(p) && !TrackerDone(p)) return false; return true; }
        bool TrackerDone(string id) { var o = _tracker ? _tracker.Find(id) : null; return o != null && o.Completed; }
        static bool Flag(string f, bool ifEmpty) { if (string.IsNullOrEmpty(f)) return ifEmpty; if (f.StartsWith("page:")) return ASHGrandpaJournal.Has("grandpa", f.Substring(5)); var d = ASHPrologueDirector.Instance; return d && d.ChainFlag(f); }

        bool Satisfied(ASHObjectiveChain.Step s)
        {
            bool any = false, ok = false;
            if (!string.IsNullOrEmpty(s.completeFlag)) { any = true; ok |= Flag(s.completeFlag, false); }
            if (!string.IsNullOrEmpty(s.recipeName)) { any = true; ok |= Crafted(s); }
            if (s.completeOnPlace && !string.IsNullOrEmpty(PlaceId(s))) { any = true; ok |= _crafted.Contains("placed:" + PlaceId(s)); }
            if (s.completeOnConsume) { any = true; ok |= AteNeeded(s) || ItemsMet(s); }
            if (!any && s.items != null && s.items.Count > 0) return ItemsMet(s); // collect-only: every item is in the pack (or fitted)
            return any && ok;
        }
        bool ItemsMet(ASHObjectiveChain.Step s)
        {
            if (s.items == null || s.items.Count == 0) return false;
            foreach (var n in s.items)
            {
                if (n == null || string.IsNullOrEmpty(n.itemId)) continue;
                if (Have(n, out int need) < need) return false;
            }
            return true;
        }
        bool AteNeeded(ASHObjectiveChain.Step s)
        {
            if (s.items == null) return false;
            foreach (var n in s.items)
            {
                if (n == null) continue;
                if (Ate(n.itemId)) return true;
                if (n.anyOf != null) foreach (var id in n.anyOf) if (Ate(id)) return true;
            }
            return false;
        }
        bool Ate(string id) => !string.IsNullOrEmpty(id) && _crafted.Contains("ate:" + id);
        string PlaceId(ASHObjectiveChain.Step s)
        {
            if (!string.IsNullOrEmpty(s.placeItemId)) return s.placeItemId;
            var r = !string.IsNullOrEmpty(s.recipeName) ? Recipe(s.recipeName) : null;
            return r && r.outputItem ? r.outputItem.itemId : null;
        }
        bool Crafted(ASHObjectiveChain.Step s)
        {
            if (_crafted.Contains(s.recipeName)) return true;
            var r = Recipe(s.recipeName); var outId = r && r.outputItem ? r.outputItem.itemId : null;
            if (outId != null && _crafted.Contains("out:" + outId)) return true; // any recipe with the same output
            if (s.completeOnPlace && outId != null && _crafted.Contains("placed:" + outId)) return true; // placed in the world
            return false; // owning the output (e.g. supply bandages) is not crafting it
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
            var step = s; // lines read the asset live (edits in Play show at once)
            if (lines == null) lines = () => StepLines(step);
            else { var main0 = lines; lines = () => Concat(main0(), ItemLines(step)); }
            bool main = s.kind == ASHObjectiveChain.Kind.Main;
            var o = _tracker.Register(s.id, s.title, lines, main && !s.final, main, target);
            o.Final = s.final;
        }
        IEnumerable<(string, bool)> StepLines(ASHObjectiveChain.Step s)
        {
            bool sat = Satisfied(s);
            if (!string.IsNullOrEmpty(s.recipeName)) { var r = Recipe(s.recipeName); yield return ($"{(s.completeOnPlace ? "Craft and place" : "Craft")}: {(r && r.outputItem ? r.outputItem.displayName : s.title)}", sat); }
            foreach (var l in ItemLines(s)) yield return l;
            if (!string.IsNullOrEmpty(s.hint)) yield return (s.hint, sat);
        }
        /// <summary>t475: items held now (pack) plus parts already fitted to the truck; need = how many the step wants.</summary>
        int Have(ASHObjectiveChain.ItemNeed n, out int need)
        {
            Cache(); var d = ASHPrologueDirector.Instance;
            need = n.countInstalled && d ? d.PartNeeded(n.itemId, n.count) : n.count;
            int have = CountId(n.itemId, n.countInstalled);
            if (n.anyOf != null)
                foreach (var id in n.anyOf)
                    if (!string.IsNullOrEmpty(id) && id != n.itemId) have += CountId(id, false);
            return have;
        }
        int CountId(string id, bool installed)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            var d = ASHPrologueDirector.Instance; var reg = ASHItemRegistry.I; var def = reg ? reg.Find(id) : null;
            return (installed && d ? d.PartInstalled(id) : 0) + (def && _inv ? _inv.Count(def) : 0);
        }
        static IEnumerable<(string, bool)> Concat(IEnumerable<(string, bool)> a, IEnumerable<(string, bool)> b) { foreach (var x in a) yield return x; foreach (var x in b) yield return x; }

        /// <summary>t453: one line per needed item with a live have/need count (prefixed for the HUD item style). Recipe ingredients
        /// first (crafting steps), then the step's own items. Done once the step is complete or enough is in the pack (or fitted).</summary>
        IEnumerable<(string, bool)> ItemLines(ASHObjectiveChain.Step s)
        {
            Cache(); bool sat = Satisfied(s) || _done.Contains(s.id);
            if (s.listRecipeIngredients && !string.IsNullOrEmpty(s.recipeName))
            {
                var r = Recipe(s.recipeName);
                if (r) foreach (var ing in r.ingredients) if (ing != null && ing.item)
                {
                    int have = _inv ? _inv.Count(ing.item) : 0;
                    yield return (ASHObjectiveHudSettings.ItemPrefix + $"{ing.item.displayName}  {Mathf.Min(have, ing.amount)}/{ing.amount}", sat || have >= ing.amount);
                }
            }
            if (s.items == null) yield break;
            var d = ASHPrologueDirector.Instance; var reg = ASHItemRegistry.I;
            foreach (var n in s.items)
            {
                if (n == null || string.IsNullOrEmpty(n.itemId)) continue;
                var def = reg ? reg.Find(n.itemId) : null;
                int fitted = n.countInstalled && d ? d.PartInstalled(n.itemId) : 0;
                int have = Have(n, out int need);
                string name = !string.IsNullOrEmpty(n.label) ? n.label : def ? def.displayName : n.itemId;
                yield return (ASHObjectiveHudSettings.ItemPrefix + $"{name}  {Mathf.Min(have, need)}/{need}{(fitted > 0 ? $"  ({fitted} fitted)" : "")}", sat || have >= need);
            }
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
            _t = 0f; _restored = true; // t475: first frame after the restore runs the full Refresh (inventory is back by then)
        }
    }
}
