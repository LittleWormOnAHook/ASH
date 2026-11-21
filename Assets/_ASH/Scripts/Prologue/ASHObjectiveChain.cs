using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>M1 (t440): data-driven objective chain. Each step lists its prerequisites (all must be complete before it shows),
    /// an optional reveal flag (e.g. the quake warning), how it completes (a director flag and/or a crafted recipe), and the steps
    /// it leads to. Steps satisfied early (found / crafted before they show) complete the moment they would appear: no repeats,
    /// no softlocks when things are done out of order. The asset is read live, so edits in Edit and Play mode apply and are kept.
    /// t453: every step can list the items it needs; the HUD shows each as its own line with a live have/need count
    /// (crafting steps list their recipe's ingredients automatically). Foundation for M2-M4.</summary>
    [CreateAssetMenu(menuName = "ASH/Objective Chain", fileName = "ASH_PrologueChain")]
    public sealed class ASHObjectiveChain : ScriptableObject
    {
        public enum Kind { Main, Side, Craft }

        /// <summary>t453: one item an objective needs ("Kindling 2/3"). Counted live from the inventory.</summary>
        [Serializable]
        public sealed class ItemNeed
        {
            [Tooltip("Item id (ASH_ItemRegistry), e.g. tire, carbattery, fanbelt, fuel_can, kindling.")] public string itemId;
            [Min(1)] public int count = 1;
            [Tooltip("Optional HUD label (empty = the item's display name).")] public string label;
            [Tooltip("Also count parts already fitted to Grandpa's truck (tire, carbattery, fanbelt, fuel_can).")] public bool countInstalled;
            public static ItemNeed Of(string id, int n, string label = null, bool installed = false) => new ItemNeed { itemId = id, count = n, label = label, countInstalled = installed };
        }

        [Serializable]
        public sealed class Step
        {
            [Tooltip("Stable id (save data + tracker). The prologue director's ids: p00_home, p01_home_again, p02_shelter, p03_car, p03_wayout, p03_truck.")]
            public string id;
            public string title;
            public Kind kind = Kind.Main;
            [Tooltip("Final main quest of the chapter: listed after every other open objective, never auto-focused while others are open.")]
            public bool final;
            [Tooltip("All of these must be complete before this step shows.")]
            public List<string> prerequisites = new List<string>();
            [Tooltip("Steps this one leads to (documentation / validation; the prerequisites decide the order).")]
            public List<string> next = new List<string>();
            [Tooltip("Optional: also wait for this director flag before showing (e.g. 'warning'). Flags: arrived, homeAgain, journal, supplies, warning, sheltered, carInspected, truckFound, truckReady.")]
            public string revealFlag;
            [Tooltip("Director flag that completes the step (see revealFlag for the list). Empty = none.")]
            public string completeFlag;
            [Tooltip("Recipe asset name that completes the step when crafted (ever, also before the step shows). Any recipe with the same output item counts too (e.g. the campfire variants).")]
            public string recipeName;
            [Tooltip("Placing the recipe's output in the world also completes the step (e.g. a campfire placed from the pack).")]
            public bool completeOnPlace;
            [Tooltip("Crafting steps: list the recipe's ingredients as item lines with have/need counts.")]
            public bool listRecipeIngredients = true;
            [Tooltip("Items this step needs, one HUD line each with a live count (added after the recipe ingredients).")]
            public List<ItemNeed> items = new List<ItemNeed>();
            [Tooltip("Short hint toward where to go / what to do, shown when the step appears and as its line on the HUD.")]
            [TextArea(1, 3)] public string hint;
        }

        [Tooltip("Off = the prologue director registers its own objectives as before (no crafting steps).")]
        public bool chainEnabled = true;
        [Tooltip("Show a subtitle with the hint when a main step appears.")]
        public bool announceMain = true;
        [Tooltip("Show a toast when a side / crafting step appears.")]
        public bool announceSide = true;
        [Range(1f, 15f)] public float hintSeconds = 6f;
        [HideInInspector] public int contentVersion;
        public List<Step> steps = new List<Step>();

        public Step Get(string id) { foreach (var s in steps) if (s != null && s.id == id) return s; return null; }

        void OnEnable() { if (steps != null && steps.Count > 0 && contentVersion < 2) Upgrade2(); }
        /// <summary>t453/t454: item needs on the truck, campfire completes when placed (keeps every other edit).</summary>
        void Upgrade2()
        {
            foreach (var s in steps)
            {
                if (s == null) continue;
                if (s.items == null) s.items = new List<ItemNeed>();
                if (s.kind == Kind.Craft) s.listRecipeIngredients = true;
                if (s.id == "c02_campfire") s.completeOnPlace = true;
                if (s.id == "p03_truck" && s.items.Count == 0) s.items.AddRange(TruckParts());
            }
            contentVersion = 2;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        static List<ItemNeed> TruckParts() => new List<ItemNeed> { ItemNeed.Of("tire", 2, "Tire", true), ItemNeed.Of("carbattery", 1, "Car battery", true), ItemNeed.Of("fanbelt", 1, "Fan belt", true), ItemNeed.Of("fuel_can", 1, "Fuel can", true) };

        static ASHObjectiveChain _i;
        public static ASHObjectiveChain I
        {
            get
            {
                if (_i) return _i;
                _i = Resources.Load<ASHObjectiveChain>("ASH_PrologueChain");
#if UNITY_EDITOR
                if (!_i)
                {
                    _i = CreateInstance<ASHObjectiveChain>(); _i.FillM1();
                    System.IO.Directory.CreateDirectory("Assets/_ASH/Resources");
                    UnityEditor.AssetDatabase.CreateAsset(_i, "Assets/_ASH/Resources/ASH_PrologueChain.asset");
                    UnityEditor.AssetDatabase.SaveAssets();
                    Debug.Log("[ASH] Created Assets/_ASH/Resources/ASH_PrologueChain.asset (M1 objective chain, " + _i.steps.Count + " steps)");
                }
#endif
                if (!_i) { _i = CreateInstance<ASHObjectiveChain>(); _i.FillM1(); }
                return _i;
            }
        }

        static Step S(string id, string title, Kind k, string[] pre, string[] next, string complete, string recipe, string hint, string reveal = null, bool final = false)
            => new Step { id = id, title = title, kind = k, prerequisites = new List<string>(pre), next = new List<string>(next), completeFlag = complete, recipeName = recipe, hint = hint, revealFlag = reveal, final = final };

        /// <summary>The M1 chain (main story + the five starting recipes as crafting steps).</summary>
        public void FillM1()
        {
            steps = new List<Step>
            {
                S("p00_home", "Returning Home", Kind.Main, new string[0], new[] { "p01_home_again" }, "arrived", null, "Follow the road to Grandpa's cabin and park by it."),
                S("p01_home_again", "Home Again", Kind.Main, new[] { "p00_home" }, new[] { "p02_shelter", "c01_kindling" }, "homeAgain", null, "Grandpa's journal and his supplies are inside the cabin."),
                S("c01_kindling", "Split some kindling", Kind.Craft, new[] { "p01_home_again" }, new[] { "c02_campfire" }, null, "ASH_Recipe_SplitKindling", "Use Grandpa's hatchet. Journal crafting tab [Q]."),
                S("c02_campfire", "Build a campfire", Kind.Craft, new[] { "c01_kindling" }, new[] { "c05_boilwater" }, null, "ASH_Recipe_Campfire", "Kindling and a few stones. Journal crafting tab [Q], then place it."),
                S("p02_shelter", "SEEK SHELTER", Kind.Main, new[] { "p01_home_again" }, new[] { "p03_car", "c03_bandage", "c04_wetrag" }, "sheltered", null, "Get inside the cabin, now.", reveal: "warning"),
                S("c03_bandage", "Make a bandage", Kind.Craft, new[] { "p02_shelter" }, new string[0], null, "ASH_Recipe_Bandage", "The quake left you bruised. Cloth makes a bandage."),
                S("c04_wetrag", "Make a wet rag", Kind.Craft, new[] { "p02_shelter" }, new string[0], null, "ASH_Recipe_WetRag", "Ash in the air. A wet rag over your face helps."),
                S("p03_car", "No Way Back", Kind.Main, new[] { "p02_shelter" }, new[] { "p03_wayout" }, "carInspected", null, "Go and see what's left of the car."),
                S("c05_boilwater", "Boil some water", Kind.Craft, new[] { "c02_campfire" }, new string[0], null, "ASH_Recipe_BoilWater", "Boil water over your campfire to make it safe."),
                S("p03_wayout", "Find a way out of the wilderness", Kind.Main, new[] { "p03_car" }, new[] { "p03_truck" }, "truckFound", null, "Grandpa kept an old pickup near the cabin."),
                S("p03_truck", "Repair Grandpa's truck", Kind.Main, new[] { "p03_wayout" }, new string[0], "truckReady", null, "Tires, battery, fan belt and fuel. Parts found earlier count.", final: true),
            };
            Get("c02_campfire").completeOnPlace = true;
            Get("p03_truck").items = TruckParts();
            contentVersion = 2;
        }
    }
}