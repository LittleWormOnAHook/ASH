using System;
using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Wildlife.Birds
{
    /// <summary>
    /// Every bird tunable (Resources/ASH_BirdSettings). Play-editable: the manager reads it live, so edits made in Play stay on the asset.
    /// Created / filled by ASH/Systems/Wildlife/Setup Birds (idempotent).
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Wildlife/Bird Settings", fileName = "ASH_BirdSettings")]
    public sealed class ASHBirdSettings : ScriptableObject
    {
        static ASHBirdSettings _i;
        public static ASHBirdSettings I => _i ? _i : (_i = Resources.Load<ASHBirdSettings>("ASH_BirdSettings"));

        [Serializable]
        public sealed class BirdType
        {
            public string id = "crow";
            public string displayName = "Crow";
            [Tooltip("Relative spawn weight (0 = never spawns).")]
            [Range(0f, 10f)] public float weight = 1f;
            [Tooltip("Multiplies the model's base colour (all types share the one animated crow rig).")]
            public Color tint = Color.white;
            [Range(0.1f, 3f)] public float scale = 1f;
            [Tooltip("Model prefab for this type (built by Setup Birds from the vendor packs). Empty = the shared birdPrefab.")]
            public GameObject prefab;
            [Tooltip("Set by Setup Birds once it has assigned the vendor model / audio; untick to let it re-apply.")]
            public bool vendorApplied;
            [Tooltip("Share of this type's flocks that only circle high in the sky and never land.")]
            [Range(0f, 1f)] public float skyOnlyChance = 0f;

            [Header("Group")]
            [Range(0f, 1f)] public float soloChance = 0.3f;
            [Range(1, 30)] public int flockMin = 2;
            [Range(1, 30)] public int flockMax = 5;

            [Header("Flight")]
            [Range(1f, 30f)] public float flySpeed = 8f;
            [Range(0f, 0.6f)] public float speedJitter = 0.15f;
            [Range(0.5f, 30f)] public float maxAccel = 9f;
            [Tooltip("Fraction of airborne time spent gliding (wings still) instead of flapping.")]
            [Range(0f, 0.9f)] public float glideShare = 0.35f;
            [Range(2f, 60f)] public float altitudeMin = 7f;
            [Range(4f, 120f)] public float altitudeMax = 22f;

            [Header("Ground")]
            [Tooltip("Takes off when the player gets this close (crouch / sprint multipliers below).")]
            [Range(1f, 60f)] public float takeOffDistance = 13f;
            [Range(0f, 1f)] public float peckChance = 0.5f;
            [Tooltip("0 = only terrain / ground, 1 = prefers the top edges of rocks, fences, roofs, logs.")]
            [Range(0f, 1f)] public float perchOnObjectsChance = 0.55f;
            [Range(2f, 300f)] public float landedMinSeconds = 10f;
            [Range(2f, 600f)] public float landedMaxSeconds = 45f;

            [Header("Audio")]
            public AudioClip[] calls;
            [Tooltip("Alarm / scared calls on take-off (empty = the normal calls).")]
            public AudioClip[] alarmCalls;
            [Range(0f, 1.5f)] public float callVolume = 0.8f;
            [Range(0.3f, 2.5f)] public float callPitch = 1f;
            [Range(0.5f, 120f)] public float callIntervalMin = 4f;
            [Range(0.5f, 240f)] public float callIntervalMax = 14f;

            [Header("Attack (bigger birds only)")]
            [Tooltip("This type can be an attacker (crows / ravens). Small songbirds should stay off.")]
            public bool canAttack;
            [Tooltip("Share of this type's ambient birds that are attackers (the rest just flee).")]
            [Range(0f, 1f)] public float attackerChance = 0.25f;
            [Tooltip("Set by Setup Birds once; untick to re-apply attack defaults for this type.")]
            public bool attackDefaultsApplied;
            [Tooltip("Whole flocks of this type can swarm the player (rare, see Swarms).")]
            public bool canSwarm;

            [Header("Harvest")]
            [Range(0, 20)] public int feathersMin = 2;
            [Range(0, 20)] public int feathersMax = 4;
            [Range(0, 10)] public int meatMin = 1;
            [Range(0, 10)] public int meatMax = 1;
        }

        [Header("Master")]
        public bool birdsEnabled = true;
        [Tooltip("Spawn birds automatically around the player in any scene with a Terrain and a Player.")]
        public bool autoSpawn = true;
        [Tooltip("Optional scene name filter (empty = every scene with a Terrain and Player).")]
        public List<string> sceneNames = new List<string>();
        public GameObject birdPrefab;
        [Tooltip("Spawn seed (0 = random each session).")]
        public int seed;

        [Header("Types (crow is the source model; others are tinted / scaled variants of the same rig)")]
        public List<BirdType> types = new List<BirdType>();

        [Header("Population / pooling")]
        [Range(0, 200)] public int maxBirds = 36;
        [Range(0, 40)] public int maxGroups = 7;
        [Range(0.2f, 60f)] public float spawnInterval = 3f;
        [Range(5f, 300f)] public float spawnRadiusMin = 45f;
        [Range(10f, 400f)] public float spawnRadiusMax = 120f;
        [Tooltip("Groups this far from the player go back to the pool.")]
        [Range(30f, 800f)] public float despawnDistance = 190f;
        [Range(0f, 1f)] public float startLandedChance = 0.55f;

        [Header("Flocking (boids)")]
        [Range(0f, 10f)] public float separationWeight = 2.2f;
        [Range(0.2f, 10f)] public float separationRadius = 1.6f;
        [Range(0f, 10f)] public float alignmentWeight = 1.0f;
        [Range(0f, 10f)] public float cohesionWeight = 0.8f;
        [Range(0f, 10f)] public float seekWeight = 1.3f;
        [Range(0f, 20f)] public float altitudeWeight = 2.5f;
        [Range(0f, 20f)] public float avoidWeight = 6f;
        [Range(1f, 40f)] public float avoidLookAhead = 8f;
        [Range(0f, 90f)] public float maxBankDegrees = 35f;
        [Range(2f, 120f)] public float flyMinSeconds = 10f;
        [Range(4f, 240f)] public float flyMaxSeconds = 30f;
        [Range(10f, 300f)] public float wanderRadius = 70f;

        [Header("Landing")]
        [Tooltip("Layers birds may land on (Player, Enemy, Water, Ignore Raycast and UI are always removed).")]
        public LayerMask perchLayers = ~0;
        [Range(3f, 80f)] public float landingSearchRadius = 18f;
        [Range(1, 40)] public int landingTries = 10;
        [Range(0f, 60f)] public float maxPerchSlope = 32f;
        [Range(0f, 40f)] public float maxPerchHeight = 12f;
        [Range(1f, 40f)] public float approachDistance = 9f;
        [Range(0.3f, 6f)] public float touchdownSpeed = 1.6f;
        [Range(3f, 40f)] public float approachTimeout = 14f;
        [Tooltip("How far in from a top edge the bird stands (metres).")]
        [Range(0f, 0.5f)] public float edgeInset = 0.07f;

        [Header("Scare / take-off")]
        [Range(0.1f, 1.5f)] public float crouchTakeOffMul = 0.6f;
        [Range(1f, 3f)] public float sprintTakeOffMul = 1.4f;
        [Tooltip("Gunshots within this range flush every landed group (bow shots use the bow hearing range).")]
        [Range(0f, 400f)] public float gunshotScareMeters = 140f;
        [Range(0.5f, 15f)] public float takeOffUpSpeed = 4f;
        [Range(10f, 400f)] public float fleeDistance = 80f;

        [Header("Noise / startle (footsteps, dog, gunshots; Play-editable)")]
        [Tooltip("Player standing still: flush distance multiplier (they mostly ignore a motionless person).")]
        [Range(0f, 1.5f)] public float stillTakeOffMul = 0.4f;
        [Tooltip("Player speed (m/s) below which they count as standing still.")]
        [Range(0f, 2f)] public float stillSpeed = 0.35f;
        [Tooltip("Player speed (m/s) above which footsteps count as loud even without the sprint flag (e.g. running downhill).")]
        [Range(2f, 15f)] public float loudSpeed = 5.5f;
        [Tooltip("Crouched footsteps count as quiet: they still alert first, but only flush much closer.")]
        public bool crouchAlertsFirst = true;
        [Tooltip("Dog walking: flush distance in metres (the dog barks/runs = loud).")]
        [Range(0f, 40f)] public float dogNoiseMeters = 8f;
        [Range(1f, 4f)] public float dogRunMul = 1.6f;
        [Tooltip("Dog speed (m/s) above which it counts as running (loud).")]
        [Range(1f, 12f)] public float dogRunSpeed = 3.5f;
        [Range(0f, 1.5f)] public float dogStillMul = 0.35f;
        [Tooltip("Alert band: a noise source within flushDistance x this makes the group freeze and look up, without flushing.")]
        [Range(1f, 4f)] public float alertDistanceMul = 1.7f;
        [Tooltip("Moderate noise inside flush distance: they freeze head-up this long, then flush.")]
        [Range(0f, 5f)] public float alertFreezeMin = 0.45f;
        [Range(0f, 8f)] public float alertFreezeMax = 1.3f;
        [Tooltip("Seconds without noise before alerted birds relax and go back to pecking.")]
        [Range(0.5f, 20f)] public float alertCalmSeconds = 3f;
        [Tooltip("Head lift (degrees) on the head/neck bones while alert.")]
        [Range(0f, 60f)] public float alertHeadPitch = 24f;
        [Tooltip("Whole-body tilt back (degrees) while alert (upright stance).")]
        [Range(0f, 30f)] public float alertBodyPitch = 9f;
        [Tooltip("Animator speed while frozen alert (0 = full freeze).")]
        [Range(0f, 1f)] public float alertAnimSpeed = 0.05f;
        [Tooltip("Startle spreads through the group: extra launch delay per metre from the first bird that reacted.")]
        [Range(0f, 1f)] public float startleSpreadPerMeter = 0.12f;
        [Range(0f, 1f)] public float startleJitter = 0.18f;
        [Range(0f, 3f)] public float startleMaxDelay = 0.9f;
        [Tooltip("Gunshots spread the startle faster (multiplies the spread delay).")]
        [Range(0f, 2f)] public float gunshotCascadeMul = 0.45f;
        [Tooltip("Chance each startled bird gives an alarm call as it lifts off (the first one always calls).")]
        [Range(0f, 1f)] public float alarmCallChance = 0.35f;
        [Range(0.5f, 2f)] public float alarmPitchMul = 1.1f;
        [Range(0.05f, 1f)] public float noiseCheckInterval = 0.15f;

        [Header("Ground behaviour (walk / peck; Play-editable)")]
        public bool groundWalkEnabled = true;
        [Tooltip("Animator state for walking. The crow rig has none (idle / fly / peck only), so empty = procedural hop-walk over the idle pose.")]
        public string walkState = "";
        [Tooltip("Chance a landed bird starts a short walk at each behaviour change (else idle / peck).")]
        [Range(0f, 1f)] public float walkChance = 0.35f;
        [Tooltip("Chance to peck right after a walk (found something).")]
        [Range(0f, 1f)] public float peckAfterWalkChance = 0.6f;
        [Tooltip("Walk bout length in metres at scale 1.")]
        [Range(0.05f, 5f)] public float walkBoutMin = 0.3f;
        [Range(0.1f, 8f)] public float walkBoutMax = 1.3f;
        [Tooltip("Distance per hop/step at scale 1.")]
        [Range(0.02f, 0.6f)] public float hopDistance = 0.13f;
        [Tooltip("Hops per second (small birds hop quicker; scaled by 1/sqrt(scale)).")]
        [Range(0.5f, 12f)] public float hopRate = 4.5f;
        [Tooltip("Hop height at scale 1 (body bob).")]
        [Range(0f, 0.3f)] public float hopHeight = 0.035f;
        [Tooltip("Forward nod per hop (degrees).")]
        [Range(0f, 30f)] public float hopNodDegrees = 6f;
        [Tooltip("How far a bird may wander from where it landed (metres, scaled).")]
        [Range(0.2f, 15f)] public float maxWanderFromLanding = 2.5f;
        [Tooltip("Loose group: a bird farther than this from the landed group's centre walks back toward it.")]
        [Range(0.3f, 20f)] public float groupCohesionMeters = 2.5f;
        [Tooltip("Personal space between walking mates (scaled).")]
        [Range(0.05f, 2f)] public float groundSpacing = 0.35f;
        [Range(0.02f, 1f)] public float maxStepUp = 0.18f;
        [Tooltip("Birds perched on objects (fences, rocks, roofs) also walk along the top, never past the edge.")]
        public bool walkOnObjects = true;
        [Range(0.5f, 20f)] public float idleSecondsMin = 2f;
        [Range(0.5f, 30f)] public float idleSecondsMax = 6f;
        [Range(0.5f, 20f)] public float peckSecondsMin = 2.2f;
        [Range(0.5f, 30f)] public float peckSecondsMax = 5f;

        [Header("Animation")]
        public string idleState = "idle";
        public string flyState = "fly";
        public string peckState = "peck";
        [Range(0.2f, 3f)] public float flapAnimSpeed = 1f;
        [Range(0f, 1f)] public float glideAnimSpeed = 0.08f;
        [Range(0.2f, 3f)] public float groundAnimSpeed = 1f;

        [Header("Ground variety (vendor rigs: idle_01/02, idle_eat, walk, take_off, land, death...)")]
        [Tooltip("Chance a landed bird stretches / flaps its wings in place at a behaviour change.")]
        [Range(0f, 1f)] public float groundFlapChance = 0.08f;
        [Range(0.2f, 5f)] public float groundFlapSeconds = 0.8f;
        [Tooltip("Seconds the take-off clip plays after leaving the ground (rigs that have one).")]
        [Range(0f, 3f)] public float takeOffClipSeconds = 0.55f;
        [Tooltip("Distance from the perch where the landing clip starts (rigs that have one).")]
        [Range(0f, 10f)] public float landClipDistance = 2.2f;
        [Tooltip("Play the rig's death clip while falling (rigs that have one); otherwise the body goes limp.")]
        public bool deathClipEnabled = true;

        [Header("Sky birds (Bird Flocks vendor flocks: high, circling, never land; still killable)")]
        public bool skyBirdsEnabled = true;
        [Tooltip("Vendor flock prefabs (Bird Flocks: Avoidance, Free Roam, Ultra Synced, Wild, Wild Few, Wild Synced; crow + seagull). One is picked at random per sky flock.")]
        public GameObject[] skyFlockPrefabs = new GameObject[0];
        [Tooltip("Bird type id for each sky flock prefab above (what a shot one turns into: crow / gull ...).")]
        public string[] skyFlockTypeIds = new string[0];
        [Range(0, 12)] public int skyMaxFlocks = 3;
        [Range(1, 60)] public int skyBirdsMin = 5;
        [Range(1, 120)] public int skyBirdsMax = 14;
        [Tooltip("Height above the ground for sky flocks (vendor flocks and sky-only ASH flocks).")]
        [Range(10f, 300f)] public float skyHeightMin = 40f;
        [Range(10f, 400f)] public float skyHeightMax = 85f;
        [Range(20f, 600f)] public float skySpawnRadiusMin = 60f;
        [Range(30f, 800f)] public float skySpawnRadiusMax = 170f;
        [Range(50f, 1200f)] public float skyDespawnDistance = 340f;
        [Range(1f, 120f)] public float skySpawnInterval = 12f;
        [Tooltip("Every this many seconds a sky flock drifts its centre back toward the player's area.")]
        [Range(2f, 300f)] public float skyFollowSeconds = 25f;
        public bool skyKillable = true;
        [Tooltip("Hit sphere radius on each vendor sky bird (metres).")]
        [Range(0.1f, 2f)] public float skyBirdHitRadius = 0.45f;
        [Tooltip("A gunshot within this distance scatters a sky flock this far away.")]
        [Range(0f, 400f)] public float skyScatterMeters = 60f;
        [Tooltip("Sky-only ASH flocks (eagles, storks, swallows...): circle radius and speed.")]
        [Range(5f, 200f)] public float skyOrbitRadiusMin = 18f;
        [Range(5f, 300f)] public float skyOrbitRadiusMax = 50f;
        [Range(1f, 90f)] public float skyOrbitDegreesPerSecond = 9f;
        [Tooltip("Vendor materials use Built-in shaders (pink in URP). Setup Birds fills URP replacements here; swapped on each vendor bird at spawn.")]
        public Material[] vendorMaterialFrom = new Material[0];
        public Material[] vendorMaterialTo = new Material[0];

        [Header("Vendor roosts (ASH_Bird_CrowRoostSpot / GullRoostSpot: vendor Landing Spot Controller)")]
        public GameObject roostCrowFlock;
        public GameObject roostCrowLanding;
        public GameObject roostGullFlock;
        public GameObject roostGullLanding;

        [Header("Lake ducks (float on named lakes, circle them, keep the shore free for fishing)")]
        public bool lakeDucksEnabled = true;
        [Tooltip("Scene objects whose name contains one of these are lakes (the largest flat renderer is the water surface).")]
        public string[] lakeObjectNames = { "Lake Tinkiller" };
        public string lakeBirdTypeId = "duck";
        [Range(0, 10)] public int lakeFlocksPerLake = 2;
        [Range(1, 20)] public int lakeGroupMin = 2;
        [Range(1, 30)] public int lakeGroupMax = 5;
        [Range(20f, 1000f)] public float lakeActivateDistance = 220f;
        [Tooltip("Water surface height correction (m) if the lake mesh top isn't the visible water line.")]
        [Range(-3f, 3f)] public float lakeSurfaceOffset = 0f;
        [Range(0.05f, 5f)] public float lakeMinDepth = 0.4f;
        [Tooltip("Ducks stay this far from the shore (room for fishing).")]
        [Range(0f, 30f)] public float lakeShoreClear = 4f;
        [Range(0.05f, 3f)] public float lakeSwimSpeed = 0.35f;
        [Range(0.5f, 30f)] public float lakeSwimRoam = 5f;
        [Range(0f, 0.3f)] public float lakeBobHeight = 0.025f;
        [Range(0.1f, 6f)] public float lakeBobRate = 1.6f;
        [Tooltip("How deep the body sits in the water (x type scale).")]
        [Range(0f, 0.3f)] public float lakeFloatDepth = 0.05f;
        [Range(2f, 60f)] public float lakeFlyHeightMin = 6f;
        [Range(4f, 120f)] public float lakeFlyHeightMax = 16f;
        [Tooltip("Fraction of the lake radius the flock circles at.")]
        [Range(0.2f, 2.5f)] public float lakeCircleRadiusMul = 1.1f;

        [Header("Swarms (rare whole-flock attacks; Play-editable)")]
        public bool swarmEnabled = true;
        [Tooltip("Chance per minute that a nearby swarm-capable flock attacks unprovoked.")]
        [Range(0f, 1f)] public float swarmChancePerMinute = 0.04f;
        [Tooltip("Chance a gunshot near a swarm-capable flock sets off a swarm.")]
        [Range(0f, 1f)] public float swarmOnShotChance = 0.15f;
        [Range(0f, 3600f)] public float swarmCooldown = 480f;
        [Range(2, 40)] public int swarmMinBirds = 4;
        [Range(2, 60)] public int swarmMaxBirds = 10;
        [Tooltip("Only flocks within this distance of the player can swarm.")]
        [Range(10f, 400f)] public float swarmRange = 90f;
        [Range(0f, 30f)] public float swarmPlayerDamage = 1.5f;
        [Range(0f, 30f)] public float swarmDogDamage = 1f;
        [Range(1, 10)] public int swarmPassesMin = 2;
        [Range(1, 10)] public int swarmPassesMax = 3;
        [Tooltip("The swarm breaks off after this many of them are killed.")]
        [Range(1, 20)] public int swarmDisperseKills = 2;
        [Tooltip("Chance a swarm comes from a sky flock (crows / gulls drop out of the sky) instead of a ground flock.")]
        [Range(0f, 1f)] public float swarmFromSkyChance = 0.5f;

        [Header("LOD / culling")]
        [Tooltip("Beyond this camera distance, landed birds freeze their Animator (flying birds keep it).")]
        [Range(10f, 300f)] public float animCullDistance = 60f;
        [Tooltip("Beyond this camera distance, flocking updates every Nth frame.")]
        [Range(10f, 300f)] public float tickLodDistance = 45f;
        [Range(1, 8)] public int farTickEvery = 3;
        [Tooltip("LODGroup screen size where a bird stops rendering.")]
        [Range(0f, 0.1f)] public float cullScreenSize = 0.004f;

        [Header("Audio")]
        public AudioClip[] takeOffClips;
        [Range(0f, 1.5f)] public float takeOffVolume = 0.7f;
        public AudioClip[] deathClips;
        [Range(5f, 300f)] public float audioMaxDistance = 70f;
        [Range(1, 16)] public int audioVoices = 6;

        [Header("Attack birds (crows / ravens; Play-editable)")]
        public bool attackBirdsEnabled = true;
        [Tooltip("Chance an attacker actually attacks when triggered (shot at / near miss / flock-mate shot).")]
        [Range(0f, 1f)] public float attackOnShotChance = 0.85f;
        [Tooltip("A shot fired within this distance of an attacker counts as being shot at.")]
        [Range(5f, 200f)] public float attackTriggerMeters = 45f;
        [Tooltip("Chance the attacker goes for the dog instead of the player (when the dog is around and not downed).")]
        [Range(0f, 1f)] public float dogTargetChance = 0.35f;
        [Range(0f, 50f)] public float playerDamage = 4f;
        [Range(0f, 50f)] public float dogDamage = 2f;
        [Range(1, 12)] public int passesMin = 2;
        [Range(1, 12)] public int passesMax = 4;
        [Tooltip("Flight speed multiplier while attacking.")]
        [Range(0.5f, 4f)] public float attackSpeedMul = 1.6f;
        [Tooltip("Height above the target to climb to before each dive.")]
        [Range(2f, 30f)] public float climbHeight = 7f;
        [Tooltip("How far out to the side each climb point is.")]
        [Range(2f, 30f)] public float climbRadius = 9f;
        [Range(0.3f, 4f)] public float hitRadius = 1.1f;
        [Range(1f, 15f)] public float diveTimeout = 5f;
        [Tooltip("Seconds before the same bird can attack again.")]
        [Range(0f, 900f)] public float attackCooldown = 60f;
        [Tooltip("Played at the start of each dive (empty = take-off flaps). The bird also caws.")]
        public AudioClip[] swoopClips;
        [Range(0f, 1.5f)] public float swoopVolume = 0.9f;

        [Header("Bird spots (ASH_Bird_AggressiveCrowSpot prefab)")]
        [Tooltip("Spots spawn their group when the player is within the spot's activate distance.")]
        [Range(0.2f, 10f)] public float spotCheckInterval = 1f;

        [Header("Death / harvest")]
        [Range(1f, 40f)] public float gravity = 14f;
        [Range(10f, 3600f)] public float deadLifetime = 600f;
        [Range(0.2f, 10f)] public float harvestHold = 1.4f;
        public ASHItemDefinition featherItem;
        public ASHItemDefinition meatItem;

        public BirdType Find(string id)
        {
            foreach (var t in types) if (t != null && t.id == id) return t;
            return null;
        }

        public BirdType Pick(System.Random rng)
        {
            float total = 0f;
            foreach (var t in types) if (t != null) total += Mathf.Max(0f, t.weight);
            if (total <= 0f) return null;
            float r = (float)rng.NextDouble() * total;
            foreach (var t in types)
            {
                if (t == null) continue;
                r -= Mathf.Max(0f, t.weight);
                if (r <= 0f) return t;
            }
            return types.Count > 0 ? types[types.Count - 1] : null;
        }

        public int PerchMask()
        {
            int m = perchLayers;
            foreach (var n in new[] { "Player", "Enemy", "Water", "Ignore Raycast", "UI" })
            {
                int l = LayerMask.NameToLayer(n);
                if (l >= 0) m &= ~(1 << l);
            }
            return m;
        }
    }
}
