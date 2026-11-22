using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.World.Roads
{
    /// <summary>t619: pre-town road network (EasyRoads3D) - waypoints, surfaces, carve, clearing, dressing, bridges, town signs and the
    /// truck breakdown. Resources/ASH_RoadSettings. Editable in Play (kept on exit). Geometry changes need
    /// ASH/Systems/Roads/Build Pre-Town Roads (edit mode); accumulation coverage / patchiness and the breakdown are live.</summary>
    [CreateAssetMenu(menuName = "ASH/World/Road Settings")]
    public sealed class ASHRoadSettings : ScriptableObject
    {
        public const string ResPath = "ASH_RoadSettings";
        static ASHRoadSettings _i;
        public static ASHRoadSettings I { get { if (!_i) { _i = Resources.Load<ASHRoadSettings>(ResPath); if (!_i) { _i = CreateInstance<ASHRoadSettings>(); _i.FillDefaults(); } } return _i; } }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics_ASHRoadSettings() { _i = null; }

        public enum Kind { Dirt, Gravel, Blacktop }

        [Serializable] public class Surface
        {
            public Kind kind;
            [Range(2f, 14f)] public float width = 5.5f;
            [Tooltip("ASH copy of the EasyRoads material on ASH/Accumulation Lit (made by the build menu).")] public Material material;
            [Tooltip("Ash/snow build-up on this road surface (1 = same as other props, 0 = none).")] [Range(0f, 1f)] public float accumulationCoverage = 0.6f;
            [Tooltip("Share of the road left bare in patches (0 = even cover, 1 = bare). Keeps the road readable.")] [Range(0f, 1f)] public float patchiness = 0.45f;
            [Tooltip("Half span of an automatic bridge where this road kind crosses a creek, metres.")] [Range(3f, 40f)] public float bridgeHalfSpan = 10f;
        }

        [Serializable] public class Segment
        {
            public string id = "road";
            public string displayName = "Road";
            public Kind kind = Kind.Gravel;
            public bool enabled = true;
            [Tooltip("0 = the surface width.")] [Range(0f, 14f)] public float widthOverride;
            [Tooltip("Id of the road this one branches off (T junction): the start is trimmed back to that road's edge.")] public string startOn = "";
            [Tooltip("Wooden sign at the start of this road (empty = none).")] public string signText = "";
            [Tooltip("World XZ waypoints (the build smooths them into a spline, heights come from the terrain).")] public List<Vector2> waypoints = new List<Vector2>();
        }

        [Serializable] public class Zone { public string name; public Vector2 xz; [Range(1f, 400f)] public float radius = 20f; }
        [Serializable] public class Bridge { public string segment; public Vector2 xz; [Range(0f, 40f)] public float halfSpan; }

        [Header("Surfaces")]
        public List<Surface> surfaces = new List<Surface>();
        [Header("Segments (cabin first)")]
        public List<Segment> segments = new List<Segment>();

        [Header("Carve (EasyRoads terrain deformation)")]
        [Tooltip("On = EasyRoads cuts / fills a roadbed into the terrain (TerrainData is backed up first). Off = roads snap to the surface.")] public bool carve = true;
        [Tooltip("Marker spacing along a road, metres.")] [Range(5f, 80f)] public float markerSpacing = 20f;
        [Tooltip("Marker spacing inside no-carve zones (road follows the ground closely there).")] [Range(2f, 20f)] public float protectedMarkerSpacing = 5f;
        [Tooltip("Road heights are the terrain averaged over this distance (bigger = gentler grades, deeper cuts).")] [Range(0f, 300f)] public float heightSmoothing = 70f;
        [Tooltip("Road surface above the averaged ground, metres.")] [Range(-1f, 2f)] public float roadRaise = 0.06f;
        [Tooltip("No terrain deformation here (cabin, his placements, Red Rock, spawn points). The road lies on the ground instead.")]
        public List<Zone> noCarveZones = new List<Zone>();

        [Header("Bridges")]
        [Tooltip("Put a bridge wherever a road crosses a creek of ASH_RegionLayout.json.")] public bool autoBridges = true;
        [Tooltip("Extra bridges (segment id + XZ).")] public List<Bridge> extraBridges = new List<Bridge>();
        [Tooltip("Deck height above the higher bank, metres.")] [Range(0f, 4f)] public float deckRaise = 0.35f;
        [Tooltip("Timber span placeholder (posts, beams, rails) under the road deck when the EasyRoads demo bridge meshes are not in the project.")] public bool timberPlaceholder = true;

        [Header("Clearing (trees, grass / plants, scattered vegetation)")]
        public bool clearTrees = true;
        [Tooltip("Terrain trees are removed within road half width + this, metres.")] [Range(0f, 20f)] public float treeShoulder = 3f;
        public bool clearDetails = true;
        [Tooltip("Terrain grass / detail is removed within road half width + this, metres.")] [Range(0f, 10f)] public float detailShoulder = 1.5f;
        public bool clearScatteredPrefabs = true;
        [Tooltip("Scattered prefabs are removed within road half width + this, metres.")] [Range(0f, 10f)] public float prefabShoulder = 1.5f;
        [Tooltip("Only children (any depth) of these generated scatter roots are candidates. Never his hand placements.")] public List<string> scatterRoots = new List<string>();
        [Tooltip("...and only if the object name starts with one of these.")] public List<string> scatterNamePrefixes = new List<string>();

        [Header("Dressing (HPA / vendor props)")]
        public bool dressing = true;
        [Tooltip("Fence runs along gravel and blacktop roads every N metres (0 = none).")] [Range(0f, 600f)] public float fenceEvery = 160f;
        [Range(1, 8)] public int fenceRunLength = 3;
        [Tooltip("Fence distance from the road edge, metres.")] [Range(0.5f, 10f)] public float fenceOffset = 2.5f;
        [Range(0f, 1f)] public float brokenFenceChance = 0.35f;
        [Range(0, 6)] public int abandonedCars = 2;
        [Range(0.2f, 3f)] public float signTextSize = 1f;
        [Tooltip("Sign text height on the board (0..1 of the board height).")] [Range(0f, 1f)] public float signTextHeight = 0.72f;
        public int randomSeed = 619;
        public List<GameObject> fencePrefabs = new List<GameObject>();
        public List<GameObject> brokenFencePrefabs = new List<GameObject>();
        public GameObject signPrefab, mailboxPrefab, blockerPrefab, conePrefab;
        public List<GameObject> carPrefabs = new List<GameObject>();
        public GameObject railPrefab;

        [Header("Town signs (visual cue for the breakdown)")]
        public string welcomeText = "WELCOME TO\nRED ROCK";
        [Tooltip("Distance from Red Rock (along the blacktop) of the Welcome sign, metres.")] [Range(50f, 800f)] public float welcomeDistance = 270f;
        public string arrowText = "RED ROCK";
        [Tooltip("Distance from Red Rock of the arrow sign (just past the breakdown point), metres.")] [Range(50f, 1200f)] public float arrowDistance = 385f;
        [Tooltip("Move the pre-town barricade onto the blacktop (ASH_PreTownSettings barricadeXZ).")] public bool snapBarricadeToRoad = true;

        [Header("Truck breakdown (Drive to Red Rock)")]
        public bool breakdown = true;
        [Tooltip("The repaired truck smokes and stalls this close to Red Rock, metres.")] [Range(50f, 1500f)] public float stallDistance = 400f;
        [Tooltip("Seconds the engine refuses to restart.")] [Range(0f, 60f)] public float stallSeconds = 7f;
        [Tooltip("Engine power while limping (x the normal power).")] [Range(0.05f, 1f)] public float limpPower = 0.3f;
        [Tooltip("Seconds between sputters while limping (random in range).")] public Vector2 sputterEvery = new Vector2(2.5f, 6f);
        [Tooltip("Seconds of near-zero power per sputter.")] [Range(0.05f, 2f)] public float sputterLength = 0.45f;
        [Tooltip("The engine dies for good this close to Red Rock (edge of town), metres.")] [Range(0f, 800f)] public float townEdgeDistance = 230f;
        [Tooltip("Hood smoke position on the truck (local). Zero = auto (front top of the body).")] public Vector3 smokeOffset = Vector3.zero;
        [Range(0f, 200f)] public float smokeRateStalled = 40f;
        [Range(0f, 200f)] public float smokeRateLimping = 14f;
        public Color smokeColor = new Color(0.32f, 0.31f, 0.3f, 0.65f);
        [TextArea] public string stallMessage = "Steam and black smoke pour from under the hood. The engine coughs and dies.";
        [TextArea] public string limpMessage = "It catches again - barely. Nurse it toward town.";
        [TextArea] public string deadMessage = "The old motor finally gives out. Red Rock is just ahead - walk the rest.";
        [TextArea] public string journalEntry = "Grandpa's truck made it most of the way. Ash in the intake, the belt squealing, smoke from under the hood - it died at the edge of Red Rock. I walked in from there.";

        public Surface SurfaceFor(Kind k) { foreach (var s in surfaces) if (s != null && s.kind == k) return s; return null; }
        public Segment Find(string id) { foreach (var s in segments) if (s != null && s.id == id) return s; return null; }
        public float WidthOf(Segment s) { if (s == null) return 0f; if (s.widthOverride > 0.1f) return s.widthOverride; var f = SurfaceFor(s.kind); return f != null ? f.width : 5f; }

        // per-material accumulation: the ASH/Accumulation Lit road copies read _ClearCoatMask as (1 - coverage) and _ClearCoatSmoothness
        // as patchiness (both unused by Lit, so the SRP batcher layout is URP's own). Live in Play.
        public void ApplyMaterials()
        {
            foreach (var s in surfaces)
            {
                if (s == null || !s.material) continue;
                s.material.SetFloat("_ClearCoatMask", 1f - Mathf.Clamp01(s.accumulationCoverage));
                s.material.SetFloat("_ClearCoatSmoothness", Mathf.Clamp01(s.patchiness));
            }
        }
        void OnValidate() { ApplyMaterials(); }
        void OnEnable() { ApplyMaterials(); }

        public void FillDefaults()
        {
            surfaces = new List<Surface>
            {
                new Surface { kind = Kind.Dirt, width = 3.6f, accumulationCoverage = 0.7f, patchiness = 0.35f, bridgeHalfSpan = 8f },
                new Surface { kind = Kind.Gravel, width = 5.5f, accumulationCoverage = 0.6f, patchiness = 0.45f, bridgeHalfSpan = 12f },
                new Surface { kind = Kind.Blacktop, width = 6.5f, accumulationCoverage = 0.5f, patchiness = 0.55f, bridgeHalfSpan = 14f },
            };
            V2 v(float x, float z) => new V2(x, z);
            segments = new List<Segment>
            {
                Seg("incoming", "County Road", Kind.Gravel, "", "", v(606, 1830), v(600, 1800), v(590, 1760), v(580, 1735), v(575, 1705), v(566, 1688), v(558.7f, 1670.3f), v(548, 1640), v(540, 1618), v(536, 1606)),
                Seg("campsite", "Campsite Track", Kind.Dirt, "", "OLD CAMPSITE", v(606, 1830), v(614, 1870), v(632, 1925), v(642, 1995), v(625, 2070), v(610, 2150), v(575, 2225), v(548, 2255)),
                Seg("lookout", "Lookout Track", Kind.Dirt, "incoming", "LOOKOUT", v(544, 1630), v(510, 1628), v(475, 1628), v(446, 1631)),
                Seg("blacktop", "Red Rock Road", Kind.Blacktop, "incoming", "RED ROCK", v(600, 1800), v(680, 1830), v(780, 1848), v(900, 1855), v(990, 1880), v(1010, 1960), v(1150, 2060), v(1300, 2200), v(1450, 2330), v(1600, 2460), v(1800, 2560), v(2100, 2720), v(2400, 2860), v(2620, 2950), v(2800, 3020), v(2985, 3125)),
                Seg("washout", "Washout Road", Kind.Gravel, "blacktop", "WASHOUT", v(900, 1855), v(980, 1848), v(1060, 1830), v(1110, 1820), v(1148, 1814)),
                Seg("ranger", "Ranger Road", Kind.Gravel, "", "RANGER STATION", v(560, 1540), v(620, 1510), v(700, 1480), v(820, 1405), v(950, 1330), v(1100, 1255), v(1255, 1190)),
                Seg("quarry", "Quarry Track", Kind.Dirt, "", "OLD QUARRY", v(500, 1525), v(460, 1450), v(430, 1380), v(385, 1280), v(340, 1180), v(310, 1080), v(290, 1000)),
            };
            noCarveZones = new List<Zone>
            {
                new Zone { name = "Grandpa's cabin + yard (porch, vendor, truck, tire, falling tree)", xz = new Vector2(530f, 1572f), radius = 52f },
                new Zone { name = "Arrival spawn (car)", xz = new Vector2(558.7f, 1670.3f), radius = 10f },
                new Zone { name = "Redstone lookout", xz = new Vector2(432.7f, 1632.1f), radius = 14f },
                new Zone { name = "Old campsite / overlook", xz = new Vector2(530f, 2270f), radius = 18f },
                new Zone { name = "Old quarry", xz = new Vector2(287.4f, 983.5f), radius = 16f },
                new Zone { name = "Ranger station", xz = new Vector2(1269.5f, 1180.1f), radius = 16f },
                new Zone { name = "Washout cache", xz = new Vector2(1158.1f, 1810.6f), radius = 10f },
                new Zone { name = "Red Rock", xz = new Vector2(3108.4f, 3217f), radius = 140f },
            };
            scatterRoots = new List<string> { "ASH_ResourceScatter", "ASH_PathMarkers" };
            scatterNamePrefixes = new List<string> { "ASH_Fiber_", "ASH_Deadwood_", "ASH_Harvest_Stump", "ASH_Harvest_DeadTreeDowned", "ASH_PathMarker_Post" };
        }
        struct V2 { public float x, z; public V2(float x, float z) { this.x = x; this.z = z; } }
        static Segment Seg(string id, string name, Kind k, string startOn, string sign, params V2[] p)
        {
            var s = new Segment { id = id, displayName = name, kind = k, startOn = startOn, signText = sign };
            foreach (var q in p) s.waypoints.Add(new Vector2(q.x, q.z));
            return s;
        }
    }
}
