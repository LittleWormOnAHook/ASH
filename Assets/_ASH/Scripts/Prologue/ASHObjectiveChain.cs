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
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHObjectiveChain()
        {
            _i = default;
        }

        public enum Kind { Main, Side, Craft }

        /// <summary>t453: one item an objective needs ("Kindling 2/3"). Counted live from the inventory.</summary>
        [Serializable]
        public sealed class ItemNeed
        {
            [Tooltip("Item id (ASH_ItemRegistry), e.g. tire, carbattery, fanbelt, fuel_can, kindling.")] public string itemId;
            [Min(1)] public int count = 1;
            [Tooltip("Optional HUD label (empty = the item's display name).")] public string label;
            [Tooltip("Also count parts already fitted to Grandpa's truck (tire, carbattery, fanbelt, fuel_can).")] public bool countInstalled;
            [Tooltip("Other item ids that also count toward this need (OR). Used for '2 boiled or bottled waters'.")]
            public List<string> anyOf = new List<string>();
            public static ItemNeed Of(string id, int n, string label = null, bool installed = false) => new ItemNeed { itemId = id, count = n, label = label, countInstalled = installed };
            public static ItemNeed Any(string label, int n, params string[] ids)
            {
                var x = new ItemNeed { count = Mathf.Max(1, n), label = label, anyOf = new List<string>() };
                if (ids != null)
                    for (int i = 0; i < ids.Length; i++)
                    {
                        if (string.IsNullOrEmpty(ids[i])) continue;
                        if (string.IsNullOrEmpty(x.itemId)) x.itemId = ids[i];
                        else if (ids[i] != x.itemId) x.anyOf.Add(ids[i]);
                    }
                return x;
            }
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
            [Tooltip("Item id that completes the step when placed (used with completeOnPlace when there is no recipe).")]
            public string placeItemId;
            [Tooltip("Eating a needed item also completes the step (keeps credit if you eat the last one).")]
            public bool completeOnConsume;
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

        void OnEnable() { if (steps != null && steps.Count > 0 && contentVersion < 2) Upgrade2(); if (steps != null && steps.Count > 0 && contentVersion < 3) Upgrade3(); if (steps != null && steps.Count > 0 && contentVersion < 4) Upgrade4(); if (steps != null && steps.Count > 0 && contentVersion < 5) Upgrade5(); }
        /// <summary>t462: the locked cabin door adds "Find Grandpa's key" (after Home Again in the list).</summary>
        void Upgrade3()
        {
            if (Get("p01_key") == null) { int i = steps.FindIndex(s => s != null && s.id == "p01_home_again"); steps.Insert(i < 0 ? steps.Count : i + 1, KeyStep()); }
            contentVersion = 3;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        /// <summary>M2-C2: P04 Water First, P05 Fire and Shelter, P06 Finding Food (parallel to the truck, after SEEK SHELTER).</summary>
        void Upgrade4()
        {
            var p04 = WaterStep(); var p05 = TentStep(); var p06 = FoodStep();
            if (Get("p04_water") == null) steps.Add(p04);
            if (Get("p05_tent") == null) steps.Add(p05);
            if (Get("p06_food") == null) steps.Add(p06);
            var p02 = Get("p02_shelter");
            if (p02 != null && p02.next != null && !p02.next.Contains("p04_water")) p02.next.Add("p04_water");
            contentVersion = 4;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        /// <summary>t617 pre-town: wet rag, boil, water, tent and food all lead to the lookout ("Get your bearings"); then the car, the
        /// truck, one objective per part (any order), the repair, and the drive to Red Rock. Keeps every other edit.</summary>
        void Upgrade5()
        {
            ApplyPreTown();
            contentVersion = 5;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        public const string IdLookout = "p07_lookout", IdDrive = "p08_drive";
        public static readonly string[] PartSteps = { "pt_tire1", "pt_tire2", "pt_battery", "pt_belt", "pt_fuel" };
        /// <summary>Idempotent: adds / rewires the pre-town steps.</summary>
        public void ApplyPreTown()
        {
            void Next(string id, string n) { var x = Get(id); if (x != null && x.next != null && !x.next.Contains(n)) x.next.Add(n); }
            int At(string id) { int i = steps.FindIndex(x => x != null && x.id == id); return i < 0 ? steps.Count : i; }
            // the lookout, after everything at camp
            if (Get(IdLookout) == null)
                steps.Insert(At("p03_car"), S(IdLookout, "Get your bearings", Kind.Main, new[] { "p06_food", "c04_wetrag", "c05_boilwater", "p04_water", "p05_tent" }, new[] { "p03_car" }, "lookoutLogged", null,
                    "Climb Redstone Camp Lookout, just north-west of the cabin, and log the topo panel with the GPS."));
            foreach (var id in new[] { "p06_food", "c04_wetrag", "c05_boilwater" }) Next(id, IdLookout);
            var car = Get("p03_car"); if (car != null) car.prerequisites = new List<string> { IdLookout };
            var p02 = Get("p02_shelter"); if (p02 != null) p02.next.Remove("p03_car");
            // one objective per part
            Part("pt_tire1", "Grab the spare tire", "tire", 1, "Tire", "Grandpa's spare leans against the shed by the cabin.");
            Part("pt_tire2", "Tire at the old camp", "tire", 2, "Tires", "The other tire is under the tarp at the old campsite, up the mule trail to the north.");
            Part("pt_battery", "Find a battery", "carbattery", 1, "Car battery", "Grandpa ran the quarry pump off a truck battery. The Old Quarry is south-west of the cabin.");
            Part("pt_belt", "Find a fan belt", "fanbelt", 1, "Fan belt", "The fan belt is at the Ranger Station, east down the county road.");
            Part("pt_fuel", "Find fuel", "fuel_can", 1, "Fuel can", "A fuel can is stashed at the washout cache, east of the mule trail.");
            var way = Get("p03_wayout"); if (way != null) { way.next.Remove("p03_truck"); foreach (var id in PartSteps) Next("p03_wayout", id); }
            var truck = Get("p03_truck");
            if (truck != null)
            {
                var pre = new List<string> { "p03_wayout" }; pre.AddRange(PartSteps); truck.prerequisites = pre;
                truck.final = false; Next("p03_truck", IdDrive);
                truck.hint = "Fit both tires, the battery and the fan belt from the truck's repair menu, and pour in at least 10 L.";
            }
            if (Get(IdDrive) == null)
                steps.Insert(At("p03_truck") + 1, S(IdDrive, "Drive to Red Rock", Kind.Main, new[] { "p03_truck" }, new string[0], "reachedRedRock", null,
                    "Grandpa's truck runs. Take the road out to Red Rock, about 3 km north-east.", final: true));
        }
        void Part(string id, string title, string item, int count, string label, string hint)
        {
            if (Get(id) != null) return;
            int i = steps.FindIndex(x => x != null && x.id == "p03_truck"); if (i < 0) i = steps.Count;
            var s = S(id, title, Kind.Main, new[] { "p03_wayout" }, new[] { "p03_truck" }, null, null, hint);
            s.items = new List<ItemNeed> { ItemNeed.Of(item, count, label, true) };
            steps.Insert(i, s);
        }
        static Step WaterStep()
        {
            var s = S("p04_water", "Water First", Kind.Main, new[] { "p02_shelter" }, new[] { "p05_tent" }, null, null, "Boil creek water or find bottled water. Hold two safe drinks.");
            s.items = new List<ItemNeed> { ItemNeed.Any("Safe water", 2, "boiledwater", "bottledwater") };
            return s;
        }
        static Step TentStep()
        {
            var s = S("p05_tent", "Fire and Shelter", Kind.Main, new[] { "p04_water" }, new[] { "p06_food" }, null, null, "Pitch Grandpa's packed tent and keep a campfire close for warmth.");
            s.completeOnPlace = true; s.placeItemId = "packedtent";
            return s;
        }
        static Step FoodStep()
        {
            var s = S("p06_food", "Finding Food", Kind.Main, new[] { "p05_tent" }, new string[0], null, null, "Hunt, fish or cook a meal. Cabin cans don't count — this is food you find or make.");
            s.completeOnConsume = true;
            s.items = new List<ItemNeed> { ItemNeed.Any("Food", 1, "rawmeat", "cookedmeat", "rawfish", "cookedfish", "deadrabbit", "wholetrout", "glazedmeat", "smokedfish", "meatstew", "fishbroth", "heartychili", "chokecherries", "driedchokecherries") };
            return s;
        }
        static Step KeyStep()
        {
            var k = S("p01_key", "Find Grandpa's key", Kind.Side, new string[0], new[] { "p01_home_again" }, "doorUnlocked", null, "The cabin door is locked. Grandpa always kept a spare key close by.", reveal: "doorTried");
            k.items = new List<ItemNeed> { ItemNeed.Of("grandpas_key", 1, "Grandpa's Key") }; return k;
        }
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
                S("p02_shelter", "SEEK SHELTER", Kind.Main, new[] { "p01_home_again" }, new[] { "p03_car", "c03_bandage", "c04_wetrag", "p04_water" }, "sheltered", null, "Get inside the cabin, now.", reveal: "warning"),
                S("c03_bandage", "Make a bandage", Kind.Craft, new[] { "p02_shelter" }, new string[0], null, "ASH_Recipe_Bandage", "The quake left you bruised. Cloth makes a bandage."),
                S("c04_wetrag", "Make a wet rag", Kind.Craft, new[] { "p02_shelter" }, new string[0], null, "ASH_Recipe_WetRag", "Ash in the air. A wet rag over your face helps."),
                S("p03_car", "No Way Back", Kind.Main, new[] { "p02_shelter" }, new[] { "p03_wayout" }, "carInspected", null, "Go and see what's left of the car."),
                S("c05_boilwater", "Boil some water", Kind.Craft, new[] { "c02_campfire" }, new string[0], null, "ASH_Recipe_BoilWater", "Boil water over your campfire to make it safe."),
                S("p03_wayout", "Find a way out of the wilderness", Kind.Main, new[] { "p03_car" }, new[] { "p03_truck" }, "truckFound", null, "Grandpa kept an old pickup near the cabin."),
                S("p03_truck", "Repair Grandpa's truck", Kind.Main, new[] { "p03_wayout" }, new string[0], "truckReady", null, "Tires, battery, fan belt and fuel. Parts found earlier count.", final: true),
                WaterStep(),
                TentStep(),
                FoodStep(),
            };
            Get("c02_campfire").completeOnPlace = true;
            Get("p03_truck").items = TruckParts();
            steps.Insert(2, KeyStep());
            ApplyPreTown();
            contentVersion = 5;
        }
    }
}