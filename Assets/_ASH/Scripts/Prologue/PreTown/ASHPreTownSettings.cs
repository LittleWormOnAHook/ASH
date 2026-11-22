using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>Pre-town loop (t617): truck part copies at nearby places, the Redstone lookout reveal, and the Red Rock road block.
    /// Resources/ASH_PreTownSettings; editable in Play, edits persist (asset). Created / wired by ASH/Systems/Prologue/Set Up Pre-Town (idempotent).</summary>
    [CreateAssetMenu(menuName = "ASH/Pre-Town Settings", fileName = "ASH_PreTownSettings")]
    public sealed class ASHPreTownSettings : ScriptableObject
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHPreTownSettings() { _i = null; }
        static ASHPreTownSettings _i;
        public static ASHPreTownSettings I { get { if (!_i) { _i = Resources.Load<ASHPreTownSettings>("ASH_PreTownSettings"); if (!_i) { _i = CreateInstance<ASHPreTownSettings>(); _i.FillDefaults(); } } return _i; } }

        [Serializable]
        public sealed class PartSpot
        {
            [Tooltip("Chain step this part belongs to (pt_tire1, pt_tire2, pt_battery, pt_belt, pt_fuel).")] public string stepId;
            [Tooltip("Item id (ASH_ItemRegistry).")] public string itemId;
            public string placeName;
            [Tooltip("World X/Z of the spot (terrain metres). The part is set down on clear, flat ground near it.")] public Vector2 worldXZ;
            [Tooltip("Optional scene object to use as the spot instead (its position wins when found).")] public string anchorObject;
            [Tooltip("Off = nothing is spawned (the part is already in the world, e.g. the shed spare); the objective still points here.")] public bool spawnCopy = true;
            [Range(0f, 40f)] public float searchRadius = 14f;
            [Tooltip("Reveal radius on the map when the lookout is logged (0 = none).")] [Range(0f, 600f)] public float revealRadius = 150f;
        }

        [Header("General")]
        [Tooltip("Master switch: part copies, lookout extras and the road block.")] public bool preTownEnabled = true;
        [Tooltip("Seconds after the scene loads before the part copies are placed (a save load restores them instead).")] [Range(0.2f, 10f)] public float spawnDelay = 1.5f;

        [Header("Truck part copies (the originals at the cabin stay where they are)")]
        public List<PartSpot> parts = new List<PartSpot>();
        [Tooltip("Compass markers on the part spots once the lookout is logged (until that part's objective is done).")] public bool markPartsOnCompass = true;
        public Color partMarkerColor = new Color(0.95f, 0.72f, 0.3f, 1f);

        [Header("Lookout (Get your bearings)")]
        public string lookoutSurveyId = "s1_redstone";
        [Tooltip("Map POIs marked when the lookout is logged (on top of the survey's own). Red Rock is never in here.")]
        public List<string> lookoutPois = new List<string> { "old_quarry", "cache_gully", "ranger_station", "the_overlook" };

        [Header("Red Rock")]
        [Tooltip("Scene object for Red Rock (centre of the block ring and the drive target).")] public string redRockObject = "REDROCK";
        public Vector2 redRockXZ = new Vector2(3108.4f, 3217f);
        [Tooltip("Drive-to-town objective completes inside this distance of Red Rock.")] [Range(20f, 800f)] public float arriveRadius = 250f;
        [Tooltip("Map reveal around Red Rock once the truck runs.")] [Range(0f, 800f)] public float revealOnTruckReady = 320f;

        [Header("Road block (until the truck is repaired)")]
        public bool roadBlock = true;
        [Tooltip("Invisible wall ring around Red Rock (blocks walking and driving in; removed when the truck is ready).")] [Range(100f, 1200f)] public float blockRadius = 380f;
        [Range(16, 256)] public int wallSegments = 96;
        [Range(5f, 200f)] public float wallHeight = 60f;
        [Tooltip("Barricade on the road into town. Empty = auto: the point of the ring facing Grandpa's cabin.")] public bool barricadeAuto = true;
        public Vector2 barricadeXZ;
        public Vector2 cabinXZ = new Vector2(522.1f, 1559.7f);
        [Tooltip("Optional barricade props (vendor prefabs; empty = simple log barricade).")] public List<GameObject> barricadeProps = new List<GameObject>();
        [Tooltip("Toast when the player walks up to the block.")] [TextArea(1, 3)] public string blockedMessage = "Downed trees and a washed-out cut block the way to Red Rock. You'll need Grandpa's truck running and a way around.";
        [Range(2f, 60f)] public float messageDistance = 22f;
        [Range(2f, 120f)] public float messageCooldown = 20f;
        [Tooltip("Don't build the ring while the player is already inside it (testing teleports).")] public bool skipIfPlayerInside = true;

        public PartSpot Part(string stepId) { foreach (var p in parts) if (p != null && p.stepId == stepId) return p; return null; }

        public void FillDefaults()
        {
            parts = new List<PartSpot>
            {
                new PartSpot { stepId = "pt_tire1", itemId = "tire", placeName = "the shed", worldXZ = new Vector2(550.9f, 1576.4f), anchorObject = "Tire Replacement (1)", spawnCopy = false, revealRadius = 0f },
                new PartSpot { stepId = "pt_tire2", itemId = "tire", placeName = "the old campsite", worldXZ = new Vector2(523.5f, 2276.4f), anchorObject = "ASH_Objective_OldCampsite", revealRadius = 160f },
                new PartSpot { stepId = "pt_battery", itemId = "carbattery", placeName = "the Old Quarry", worldXZ = new Vector2(287.4f, 983.5f), revealRadius = 180f },
                new PartSpot { stepId = "pt_belt", itemId = "fanbelt", placeName = "the Ranger Station", worldXZ = new Vector2(1269.5f, 1180.1f), revealRadius = 0f },
                new PartSpot { stepId = "pt_fuel", itemId = "fuel_can", placeName = "the washout cache", worldXZ = new Vector2(1158.1f, 1810.6f), revealRadius = 170f },
            };
        }
        void Reset() => FillDefaults();
        void OnEnable() { if (parts == null || parts.Count == 0) FillDefaults(); }
    }
}
