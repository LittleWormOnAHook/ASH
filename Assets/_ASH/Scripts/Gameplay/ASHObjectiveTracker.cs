using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Inventory;
using ASH.Map;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// The slice's short objective chain: log the Redstone Camp lookout (tutorial survey) -> gather -> craft campfire
    /// -> reach The Overlook -> make camp at the old campsite. Counts are "ever collected" so eating never regresses.
    /// </summary>
    public sealed class ASHObjectiveTracker : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHObjectiveTracker()
        {
            HoldIntroMessage = default;
            HoldSlice = default;
        }

        [Serializable] public sealed class GatherGoal { public ASHItemDefinition item; [Min(1)] public int required = 3; [NonSerialized] public int collected; }

        [SerializeField] private ASHInventory inventory;
        [SerializeField] private ASHCrafting crafting;
        [SerializeField] private ASHPlacer placer;
        [SerializeField] private Transform player;
        [SerializeField] private List<GatherGoal> gatherGoals = new List<GatherGoal>();
        [SerializeField] private ASHRecipe campfireRecipe;
        [SerializeField] private Transform overlook;
        [Range(5f, 100f)] [SerializeField] private float overlookRadius = 25f;
        [SerializeField] private Transform campsite;
        [Range(5f, 100f)] [SerializeField] private float campsiteRadius = 30f;
        [Header("Tutorial survey (optional; skipped when unassigned)")]
        [SerializeField] private ASHGpsMap gps;
        [SerializeField] private ASHSurveyPointDef firstSurvey;
        [SerializeField] private Transform firstSurveyPoint;

        public enum Stage { Survey, Gather, Craft, Travel, MakeCamp, Complete }
        public Stage Current { get; private set; } = Stage.Gather;
        public ASHSurveyPointDef FirstSurvey => firstSurvey;
        public Transform FirstSurveyPoint => firstSurveyPoint;
        public IReadOnlyList<GatherGoal> GatherGoals => gatherGoals;
        public event Action Changed;
        /// <summary>t475: objective lines read live counts; this redraws them (inventory change, reveal, restore).</summary>
        public void NotifyLinesChanged() => Changed?.Invoke();
        public event Action<string> Message;

        public Transform Overlook => overlook;
        public Transform Campsite => campsite;
        private bool _crafted, _reached;

        // ---- save / load (ASHCoreSave)
        [Serializable] sealed class SavedLine { public string text; public bool done; }
        [Serializable] sealed class SavedObjective { public string id, title; public bool priority; public List<SavedLine> lines = new List<SavedLine>(); }
        [Serializable] sealed class SavedState
        {
            public int stage; public List<int> gathered = new List<int>(); public bool crafted, reached, sliceHeld;
            public string trackedId; public bool playerPicked;
            public List<SavedObjective> completed = new List<SavedObjective>();
        }

        public string CaptureJson()
        {
            var s = new SavedState { stage = (int)Current, crafted = _crafted, reached = _reached, sliceHeld = HoldSlice, trackedId = TrackedId, playerPicked = PlayerPicked };
            foreach (var g in gatherGoals) s.gathered.Add(g.collected);
            foreach (var o in _objectives)
            {
                if (o == _slice || !o.Completed) continue;
                var so = new SavedObjective { id = o.Id, title = o.Title, priority = o.Priority };
                foreach (var (text, done) in o.GetLines()) so.lines.Add(new SavedLine { text = text, done = done });
                s.completed.Add(so);
            }
            return JsonUtility.ToJson(s);
        }

        /// <summary>Restores the slice chain, completed objectives (greyed in the journal) and the tracked pick. An objective that is
        /// active right now (e.g. "Secure the cabin" restarted by the opening restore) is kept.</summary>
        public void RestoreJson(string json)
        {
            var s = JsonUtility.FromJson<SavedState>(json); if (s == null) return;
            EnsureSlice();
            Current = (Stage)Mathf.Clamp(s.stage, 0, (int)Stage.Complete);
            for (int i = 0; i < gatherGoals.Count && i < s.gathered.Count; i++) gatherGoals[i].collected = s.gathered[i];
            _crafted = s.crafted; _reached = s.reached;
            HoldSlice = s.sliceHeld; if (!s.sliceHeld) HoldIntroMessage = false;
            foreach (var so in s.completed)
            {
                if (Find(so.id) != null || _objectives.Exists(x => x.Completed && x.Priority && so.priority && x.Title == so.title)) continue;
                var o = new Objective { Id = so.id, Title = so.title, Priority = so.priority, Completed = true, Order = _order++, Frozen = new List<(string, bool)>() };
                foreach (var l in so.lines) o.Frozen.Add((l.text, l.done));
                _objectives.Add(o);
            }
            _slice.Order = _order++;
            if (!string.IsNullOrEmpty(s.trackedId) && Find(s.trackedId) != null && !Find(s.trackedId).Completed) { TrackedId = s.trackedId; PlayerPicked = s.playerPicked; }
            TrackedChanged?.Invoke(TrackedId);
            Changed?.Invoke();
        }

        // ---- objective list (journal Objectives tab + HUD focus). The slice chain is one entry ("slice"); story beats such as
        // the Phase 2A opening come in through SetPriority (they take the HUD focus when they start, like before) and stay
        // in the list greyed out once cleared. Other systems can add their own with Register/Complete.
        public sealed class Objective
        {
            public string Id, Title; public bool Completed, Priority; public int Order;
            /// <summary>Unscaled time Complete() ran (the HUD lingers on a just-finished objective briefly, then clears).</summary>
            public float CompletedAt;
            /// <summary>t411: main (story) objective - the tracked one shows as a gold compass dot. Side objectives: false.</summary>
            public bool Main;
            /// <summary>t438: final main quest of a chapter (e.g. "Repair Grandpa's truck"): listed after every other active objective and
            /// only auto-focused when nothing else is open (the player can still pick it in the journal).</summary>
            public bool Final;
            /// <summary>t411: optional world target for the compass (null / returns null = no dot).</summary>
            public Func<Vector3?> Target;
            public Func<IEnumerable<(string text, bool done)>> Lines;
            internal List<(string text, bool done)> Frozen;
            public IEnumerable<(string text, bool done)> GetLines() { if (Frozen != null) return Frozen; return Lines != null ? Lines() : Array.Empty<(string, bool)>(); }
        }
        readonly List<Objective> _objectives = new List<Objective>(); int _order; Objective _slice;
        /// <summary>All objectives: active (newest first), then completed (newest first).</summary>
        public IEnumerable<Objective> Objectives { get { foreach (var o in Sorted()) yield return o; } }
        List<Objective> Sorted() { EnsureSlice(); var l = new List<Objective>(_objectives); if (SliceHeld) l.Remove(_slice); l.Sort((x, y) => x.Completed != y.Completed ? (x.Completed ? 1 : -1) : !x.Completed && x.Final != y.Final ? (x.Final ? 1 : -1) : y.Order.CompareTo(x.Order)); return l; }
        /// <summary>Id of the objective the player picked in the journal (null = automatic: newest active).</summary>
        public string TrackedId { get; private set; }
        public bool PlayerPicked { get; private set; }
        public event Action<string> TrackedChanged;

        void EnsureSlice()
        {
            if (_slice != null) return;
            _slice = new Objective { Id = "slice", Order = _order++, Lines = SliceLines, Main = true, Target = SliceTarget };
            _objectives.Add(_slice);
        }

        /// <summary>Seconds the HUD keeps a just-completed objective when nothing else is active.</summary>
        public static float CompletedLinger = 4f;
        public Objective Find(string id) { EnsureSlice(); return _objectives.Find(o => o.Id == id); }

        /// <summary>The objective the HUD focuses on: the picked one while active, else the newest active one.</summary>
        public Objective Tracked
        {
            get
            {
                EnsureSlice(); _slice.Title = SliceTitle(); _slice.Completed = Current == Stage.Complete;
                var t = TrackedId != null ? Find(TrackedId) : null;
                if (t != null && !t.Completed) return t;
                Objective best = null; foreach (var o in _objectives) if (!o.Completed && !(SliceHeld && o == _slice) && (best == null || (best.Final && !o.Final) || (best.Final == o.Final && o.Order > best.Order))) best = o;
                if (best == null) best = t ?? (SliceHeld ? _none : _slice);
                if (t != null && t.Completed && best != t && best != _none) { TrackedId = best.Id; TrackedChanged?.Invoke(TrackedId); } // tracked one finished: follow the next active one
                // nothing left open: show the finished one for a moment, then clear the HUD (was stuck on its frozen all-ticked lines forever)
                if (best != _none && best.Completed && (best != t || Time.unscaledTime - best.CompletedAt > CompletedLinger)) best = _none;
                return best;
            }
        }

        /// <summary>Journal click: follow this objective. Picking the tracked one again returns to automatic.</summary>
        public void Track(string id)
        {
            if (id != null && PlayerPicked && TrackedId == id) { id = null; }
            TrackedId = id; PlayerPicked = id != null;
            TrackedChanged?.Invoke(TrackedId); Changed?.Invoke();
        }

        /// <summary>Add or update an objective (other systems / future quests). New ones become the newest.</summary>
        public Objective Register(string id, string title, Func<IEnumerable<(string text, bool done)>> lines, bool takeFocus = false, bool main = false, Func<Vector3?> target = null)
        {
            var o = Find(id);
            if (o == null) { o = new Objective { Id = id, Order = _order++ }; _objectives.Add(o); if (takeFocus) { TrackedId = id; PlayerPicked = false; } }
            o.Title = title; o.Lines = lines; o.Completed = false; o.Frozen = null; o.Main = main; o.Target = target;
            Changed?.Invoke(); return o;
        }
        public void Complete(string id, string message = null)
        {
            var o = Find(id); if (o == null || o.Completed) return;
            o.Frozen = new List<(string, bool)>(o.GetLines()); o.Completed = true; o.CompletedAt = Time.unscaledTime;
            if (!string.IsNullOrEmpty(message)) Message?.Invoke(message);
            Changed?.Invoke();
        }

        // ---- priority objective (story beats such as the Phase 2A opening take the HUD focus while active)
        /// <summary>Set before Start (e.g. by the opening sequence) to hold back the intro toast; ReplayIntro() shows it later.</summary>
        public static bool HoldIntroMessage;
        /// <summary>The slice chain ("Get your bearings" ...) has not started yet: set by the Phase 2A opening (ASHEruptionSequence)
        /// until it reaches Done, which is also when "Secure the cabin" ends. While held the slice is hidden from the HUD and the
        /// journal and does not progress. StartSlice() releases it (and the intro toast) and gives it the HUD focus.</summary>
        public static bool HoldSlice;
        static bool SliceHeld => HoldSlice;
        readonly Objective _none = new Objective { Id = "none", Title = "" };
        public bool SliceStarted => !HoldSlice;
        public void StartSlice()
        {
            bool was = HoldSlice; HoldSlice = false;
            EnsureSlice();
            if (was) { _slice.Order = _order++; TrackedId = null; PlayerPicked = false; TrackedChanged?.Invoke(null); ASHLog.Info("Objective -> slice started"); }
            if (HoldIntroMessage || was) { HoldIntroMessage = false; Message?.Invoke(IntroText()); }
            Evaluate();
        }
        Objective ActivePriority() { foreach (var o in _objectives) if (o.Priority && !o.Completed) return o; return null; }
        public bool HasPriority => ActivePriority() != null;
        public string PriorityTitle => ActivePriority()?.Title;
        public void SetPriority(string title, Func<IEnumerable<(string text, bool done)>> lines, string message = null, Func<Vector3?> target = null)
        {
            EnsureSlice();
            var o = ActivePriority();
            if (o == null)
            {
                o = new Objective { Id = "story-" + _order, Order = _order++, Priority = true }; _objectives.Add(o);
                TrackedId = o.Id; PlayerPicked = false; TrackedChanged?.Invoke(TrackedId); // story beats take the focus when they start
            }
            o.Title = title; o.Lines = lines; o.Main = true; if (target != null) o.Target = target;
            ASHLog.Info("Objective (priority) -> " + title);
            if (!string.IsNullOrEmpty(message)) Message?.Invoke(message);
            Changed?.Invoke();
        }
        public void ClearPriority(string message = null)
        {
            var o = ActivePriority(); if (o == null) return;
            Complete(o.Id, message);
        }
        public void ReplayIntro() { if (HoldSlice) { StartSlice(); return; } HoldIntroMessage = false; Message?.Invoke(IntroText()); Changed?.Invoke(); }
        string IntroText() => Current == Stage.Survey ? "The GPS is damaged. Climb the Redstone Camp lookout and log the viewpoint." : "Find deadwood, stones and something to eat before dark.";

        private void OnEnable()
        {
            if (inventory) inventory.ItemAdded += OnItemAdded;
            if (crafting) crafting.Crafted += OnCrafted;
            if (placer) placer.PlacedObject += OnPlaced;
            if (gps) gps.SurveyLogged += OnSurvey;
        }
        private void OnDisable()
        {
            if (inventory) inventory.ItemAdded -= OnItemAdded;
            if (crafting) crafting.Crafted -= OnCrafted;
            if (placer) placer.PlacedObject -= OnPlaced;
            if (gps) gps.SurveyLogged -= OnSurvey;
        }

        private void Awake() { EnsureSlice(); if (firstSurvey != null && gps != null) Current = Stage.Survey; }

        private void OnSurvey(ASHSurveyPointDef d)
        {
            if (SliceHeld) return;
            if (Current == Stage.Survey && d == firstSurvey)
            {
                Advance(Stage.Gather, "Viewpoint logged. Now find deadwood, stones and something to eat before dark.");
                Evaluate();
            }
        }

        private void Start()
        {
            if (!HoldIntroMessage && !HoldSlice) Message?.Invoke(IntroText());
            Changed?.Invoke();
        }

        private void OnItemAdded(ASHItemDefinition item, int amount)
        {
            foreach (var g in gatherGoals) if (g.item == item) g.collected += amount;
            Evaluate();
        }

        private void OnCrafted(ASHRecipe r) { if (campfireRecipe == null || r == campfireRecipe) _crafted = true; Evaluate(); }

        private void OnPlaced(ASHItemDefinition item, GameObject go)
        {
            if (Current != Stage.MakeCamp && Current != Stage.Travel) return;
            if (campsite != null && Flat(go.transform.position, campsite.position) <= campsiteRadius)
            {
                _reached = true; Advance(Stage.Complete, "Camp made before dark. (Slice complete)");
            }
            else Message?.Invoke("Not here. The old campsite is up on the ridge.");
        }

        private void Update()
        {
            if (SliceHeld) return;
            if (Current == Stage.Travel && player && overlook && Flat(player.position, overlook.position) <= overlookRadius)
            {
                _reached = true; Evaluate();
            }
        }

        private void Evaluate()
        {
            if (SliceHeld) { Changed?.Invoke(); return; }
            var before = Current;
            if (Current == Stage.Survey) { if (before == Current) Changed?.Invoke(); return; }
            bool gathered = true; foreach (var g in gatherGoals) if (g.collected < g.required) gathered = false;
            if (Current == Stage.Gather && gathered) Advance(Stage.Craft, "Gathered enough. Craft a campfire.");
            if (Current == Stage.Craft && (_crafted || (inventory && campfireRecipe && inventory.Count(campfireRecipe.outputItem) > 0))) Advance(Stage.Travel, $"Campfire ready. Head for The Overlook to the {Compass(overlook)}.");
            if (Current == Stage.Travel && _reached) Advance(Stage.MakeCamp, "The old campsite. Place your campfire here.");
            if (before == Current) Changed?.Invoke();
        }

        private void Advance(Stage s, string msg)
        {
            Current = s; ASHLog.Info("Objective -> " + s); Message?.Invoke(msg); Changed?.Invoke();
        }

        /// <summary>Title of the tracked objective (HUD).</summary>
        public string Title() => Tracked.Title;
        /// <summary>Lines of the tracked objective (HUD).</summary>
        public IEnumerable<(string text, bool done)> Lines() => Tracked.GetLines();
        /// <summary>Number of active objectives other than the tracked one.</summary>
        /// <summary>Active objectives other than the tracked one, in display order (HUD shows a few under the tracked one).</summary>
        public List<Objective> OtherActive(int max)
        {
            var t = Tracked; var list = new List<Objective>();
            foreach (var o in Sorted()) { if (list.Count >= max) break; if (!o.Completed && o != t && !(SliceHeld && o == _slice)) list.Add(o); }
            return list;
        }
        public int OtherActiveCount { get { var t = Tracked; int n = 0; foreach (var o in _objectives) if (!o.Completed && o != t && !(SliceHeld && o == _slice)) n++; return n; } }

        string SliceTitle()
        {
            switch (Current)
            {
                case Stage.Survey: return "Get your bearings";
                case Stage.Gather: return "Prepare for nightfall";
                case Stage.Craft: return "Craft a campfire";
                case Stage.Travel: return "Reach The Overlook";
                case Stage.MakeCamp: return "Make camp";
                default: return "Camp made";
            }
        }

        IEnumerable<(string text, bool done)> SliceLines()
        {
            if (Current == Stage.Survey)
            {
                float d = player && firstSurveyPoint ? Flat(player.position, firstSurveyPoint.position) : 0f;
                yield return ($"Climb the Redstone Camp lookout, {Compass(firstSurveyPoint)}  ({d:0} m)", false);
                yield return ("Log the viewpoint with the GPS (hold E at the topo panel)", false);
            }
            else if (Current == Stage.Gather)
                foreach (var g in gatherGoals) yield return ($"{(g.item ? g.item.displayName : "?")}  {Mathf.Min(g.collected, g.required)}/{g.required}", g.collected >= g.required);
            else if (Current == Stage.Craft) yield return ("Open the journal crafting tab [Q] and build a campfire", false);
            else if (Current == Stage.Travel)
            {
                float d = player && overlook ? Flat(player.position, overlook.position) : 0f;
                yield return ($"Climb to The Overlook, {Compass(overlook)}  ({d:0} m)", false);
                yield return ("Look for the smoke rising from the old campsite", false);
            }
            else if (Current == Stage.MakeCamp) yield return ("Place the campfire at the old campsite", false);
            else yield return ("Night falls on the prairie. Thanks for playing the slice.", true);
        }

        /// <summary>t411: world target of the slice step for the compass (survey point / overlook / campsite; none while gathering or crafting).</summary>
        Vector3? SliceTarget()
        {
            var t = Current == Stage.Survey ? firstSurveyPoint : Current == Stage.Travel ? overlook : Current == Stage.MakeCamp ? campsite : null;
            return t ? t.position : (Vector3?)null;
        }

        public float DistanceToGoal()
        {
            var t = Current == Stage.Survey ? firstSurveyPoint : Current == Stage.MakeCamp ? campsite : overlook;
            return player && t ? Flat(player.position, t.position) : -1f;
        }

        /// <summary>Cardinal direction from the player to a target (terrain north = +Z).</summary>
        public string Compass(Transform target)
        {
            if (!player || !target) return "";
            Vector3 d = target.position - player.position;
            float ang = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
            string[] names = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
            return names[Mathf.RoundToInt(ang / 45f) % 8];
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

        public void DevComplete(Stage s) { Current = s; Changed?.Invoke(); }
    }
}
