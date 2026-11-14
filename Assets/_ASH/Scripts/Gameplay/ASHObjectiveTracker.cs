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
        public event Action<string> Message;

        public Transform Overlook => overlook;
        public Transform Campsite => campsite;
        private bool _crafted, _reached;

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

        private void Awake() { if (firstSurvey != null && gps != null) Current = Stage.Survey; }

        private void OnSurvey(ASHSurveyPointDef d)
        {
            if (Current == Stage.Survey && d == firstSurvey)
            {
                Advance(Stage.Gather, "Viewpoint logged. Now find deadwood, stones and something to eat before dark.");
                Evaluate();
            }
        }

        private void Start()
        {
            Message?.Invoke(Current == Stage.Survey ? "The GPS is damaged. Climb the Redstone Camp lookout and log the viewpoint." : "Find deadwood, stones and something to eat before dark.");
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
            if (Current == Stage.Travel && player && overlook && Flat(player.position, overlook.position) <= overlookRadius)
            {
                _reached = true; Evaluate();
            }
        }

        private void Evaluate()
        {
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

        public string Title()
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

        public IEnumerable<(string text, bool done)> Lines()
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
