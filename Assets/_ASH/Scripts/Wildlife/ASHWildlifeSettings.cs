using System;
using System.Collections.Generic;
using ASH.Inventory;
using ASH.Movement;
using UnityEngine;

namespace ASH.Wildlife
{
    public enum ASHWildlifeKind { Wolf, Bear, Fox, Rabbit, Raccoon, Deer, Fawn, Boar, Cougar }
    public enum ASHWildlifeSize { Young, Adult, Alpha }
    public enum ASHWildlifeTemperament { Passive, Hostile, Stalk }
    public enum ASHTrapSize { None, Snare, Large }
    public enum ASHDogWildlifePolicy { Engage, WarnDistractRetreat, Ignore }
    /// <summary>Per-species surprise move (seeded chance + cooldown per placed animal).</summary>
    public enum ASHSurpriseKind { None, FreezeThenBolt, CircleBehind, FalseCharge, StandAndHuff, Pounce, PlayDead, StalkFromCover }

    [CreateAssetMenu(menuName = "ASH/Wildlife/Wildlife Settings", fileName = "ASH_WildlifeSettings")]
    public sealed class ASHWildlifeSettings : ScriptableObject
    {
        static ASHWildlifeSettings _i;
        public static ASHWildlifeSettings I => _i ? _i : (_i = Resources.Load<ASHWildlifeSettings>("ASH_WildlifeSettings"));

        [Serializable]
        public sealed class Drop
        {
            public ASHItemDefinition item;
            [Range(0, 20)] public int min = 1;
            [Range(0, 20)] public int max = 1;
            [Range(0f, 1f)] public float chance = 1f;
        }

        /// <summary>Per-species ranges a placed animal's seeded personality is rolled from (stable per instance).</summary>
        [Serializable]
        public sealed class PersonalityRange
        {
            [Tooltip("0 = skittish (flees/engages late), 1 = bold.")]
            [Range(0f, 1f)] public float boldnessMin = 0.2f;
            [Range(0f, 1f)] public float boldnessMax = 0.8f;
            [Tooltip("0 = slow, hesitant attacks; 1 = short holds, fast follow-ups.")]
            [Range(0f, 1f)] public float aggressionMin = 0.2f;
            [Range(0f, 1f)] public float aggressionMax = 0.8f;
            [Tooltip("Multiplies walk/trot/run (animation playback follows).")]
            [Range(0.5f, 1.5f)] public float speedMin = 0.92f;
            [Range(0.5f, 1.5f)] public float speedMax = 1.08f;
            [Tooltip("Multiplies flee distances.")]
            [Range(0.2f, 3f)] public float fleeMin = 0.75f;
            [Range(0.2f, 3f)] public float fleeMax = 1.3f;
            [Tooltip("Multiplies wander radius.")]
            [Range(0.2f, 3f)] public float wanderMin = 0.7f;
            [Range(0.2f, 3f)] public float wanderMax = 1.4f;
            [Tooltip("Multiplies graze chance.")]
            [Range(0f, 3f)] public float grazeMin = 0.6f;
            [Range(0f, 3f)] public float grazeMax = 1.5f;
            [Tooltip("Multiplies herd / pack radius (low = tight group).")]
            [Range(0.2f, 3f)] public float cohesionMin = 0.7f;
            [Range(0.2f, 3f)] public float cohesionMax = 1.3f;
            [Tooltip("Multiplies the stand-off buffer.")]
            [Range(0.3f, 2f)] public float standOffMin = 0.85f;
            [Range(0.3f, 2f)] public float standOffMax = 1.2f;
        }

        [Serializable]
        public sealed class Species
        {
            public ASHWildlifeKind kind;
            public string displayName = "Wolf";
            public ASHWildlifeTemperament temperament = ASHWildlifeTemperament.Hostile;
            public ASHTrapSize trapSize = ASHTrapSize.None;
            public ASHDogWildlifePolicy dogPolicy = ASHDogWildlifePolicy.Engage;
            [Range(1f, 500f)] public float maxHealth = 40f;
            [Range(0f, 80f)] public float damage = 8f;
            [Range(1f, 80f)] public float detectRange = 22f;
            [Range(4f, 80f)] public float stalkRange = 32f;
            [Range(0.5f, 8f)] public float attackRange = 2.2f;
            [Range(0.2f, 8f)] public float walkSpeed = 2.2f;
            [Range(0.2f, 16f)] public float runSpeed = 6.5f;
            [Range(0.2f, 12f)] public float attackCooldown = 1.6f;
            [Range(0.4f, 20f)] public float harvestHold = 3.5f;
            [Range(0.5f, 40f)] public float wanderRadius = 18f;
            [Tooltip("Short flee distance when they only dash and look back.")]
            [Range(2f, 40f)] public float shortFleeMeters = 8f;
            [Range(4f, 80f)] public float longFleeMeters = 22f;
            [Range(0f, 1f)] public float shortFleeChance = 0.4f;
            [Range(0.5f, 2f)] public float baseScale = 1f;
            public bool scaredByTorch = true;
            [Range(2f, 40f)] public float torchScareRadius = 8f;
            [Range(1f, 30f)] public float torchScareSeconds = 6f;
            [Range(0, 12)] public int placeCount = 2;
            [Range(1, 6)] public int packSize = 1;
            [Tooltip("Trot is mid-gait. 0 = halfway between walk and run.")]
            [Range(0f, 14f)] public float trotSpeed = 0f;
            [Tooltip("Ground speed the walk/trot/run clips cover at playback 1. Used so feet stay planted.")]
            [Range(0.05f, 8f)] public float walkClipSpeed = 1f;
            [Range(0.05f, 12f)] public float trotClipSpeed = 2f;
            [Range(0.05f, 20f)] public float runClipSpeed = 4f;
            [Range(0f, 1.2f)] public float bodyRadius = 0f;
            [Range(0f, 2.5f)] public float bodyLength = 0f;
            [Range(0f, 1.8f)] public float bodyHeight = 0f;

            [Header("Senses (0 range = use detect / stalk)")]
            [Range(0f, 80f)] public float sightRange = 0f;
            [Range(20f, 180f)] public float sightAngle = 80f;
            [Range(1f, 40f)] public float hearWalk = 8f;
            [Range(0.5f, 20f)] public float hearCrouch = 3f;
            [Range(4f, 80f)] public float hearSprint = 22f;
            [Range(10f, 200f)] public float hearGunshot = 70f;
            [Range(2f, 80f)] public float scentRange = 18f;

            [Header("Alert / graze / retreat")]
            [Range(0.2f, 8f)] public float curiousSeconds = 1.6f;
            [Range(0f, 1f)] public float grazeChance = 0.35f;
            [Range(1f, 20f)] public float grazeMin = 4f;
            [Range(2f, 30f)] public float grazeMax = 10f;
            [Range(0f, 1f)] public float lookBackChance = 0.65f;
            [Range(0.4f, 8f)] public float lookBackSeconds = 2.4f;
            [Range(0.05f, 0.9f)] public float retreatHealth = 0.3f;
            [Range(0f, 1f)] public float retreatChance = 0.45f;
            [Range(1f, 20f)] public float retreatSeconds = 6f;
            [Range(4f, 30f)] public float fatigueRunSeconds = 12f;

            [Header("Damage taken (1 = as rolled; lower = tougher)")]
            [Range(0.2f, 2f)] public float incomingMul = 1f;
            [Range(0.2f, 2f)] public float arrowMul = 1f;
            [Range(0.2f, 2f)] public float bulletMul = 1f;
            [Range(0.2f, 2f)] public float meleeMul = 1f;
            [Range(1f, 3f)] public float vitalMul = 1.6f;
            [Range(0.15f, 0.9f)] public float attackHitNormalized = 0.42f;
            [Tooltip("Set by setup once; do not tick unless you want defaults re-applied.")]
            public bool combatProfileApplied;

            [Header("Daily activity (1 = normal; 0 = mostly bedded)")]
            [Range(0f, 2f)] public float dayActivity = 1f;
            [Range(0f, 2f)] public float nightActivity = 1f;

            [Header("Species tactics")]
            public bool herdFlee;
            public bool packFlank;
            public bool bluffCharge;
            public bool ambush;
            public bool chargeAndTurn;

            [Header("Lethality (ranged; predictable shot counts)")]
            [Tooltip("Body hits from a bow or rifle needed to kill (deer 2, wolf/cougar 3, bear 4). Head/heart = 1 with bow or rifle.")]
            [Range(1, 12)] public int bodyShotsToKill = 2;

            [Header("Stand-off (aggressive species)")]
            [Tooltip("Buffer the animal holds from its target before and after each attack. 0 = old straight-in behaviour.")]
            [Range(0f, 15f)] public float standOffMeters = 4.5f;
            [Range(0f, 10f)] public float standOffHoldMin = 0.8f;
            [Range(0f, 15f)] public float standOffHoldMax = 2.2f;

            [Header("Seeded personality ranges")]
            public PersonalityRange personality = new PersonalityRange();
            [Tooltip("Set by setup once; untick to re-apply this pass's per-species defaults (shots, stand-off, personality).")]
            public bool m2TuneApplied;

            [Header("Surprise behaviour (seeded; Play-editable)")]
            [Tooltip("Deer freeze then bolt, wolf circles behind, boar false-charges, bear stands and huffs, fox pounces, raccoon plays dead, cougar stalks from cover.")]
            public ASHSurpriseKind surprise = ASHSurpriseKind.None;
            [Tooltip("Chance the surprise fires when its trigger comes up (scaled by the animal's seeded tendency and the global multiplier).")]
            [Range(0f, 1f)] public float surpriseChance = 0.35f;
            [Tooltip("Seconds before the same animal can surprise again (random between min and max).")]
            [Range(0f, 900f)] public float surpriseCooldownMin = 45f;
            [Range(0f, 900f)] public float surpriseCooldownMax = 120f;
            [Tooltip("Main hold time: freeze, huff, stand, play-dead, wait in cover, etc.")]
            [Range(0.2f, 20f)] public float surpriseSeconds = 2.5f;
            [Tooltip("Distance used by the move: false-charge stop, pounce length, circle-behind ring, cover search radius, play-dead 'too close'.")]
            [Range(0.5f, 40f)] public float surpriseMeters = 6f;
            [Tooltip("Speed multiplier for the burst after the surprise (bolt, pounce, rush).")]
            [Range(0.5f, 2.5f)] public float surpriseBurstMul = 1.25f;
            [Tooltip("Optional Animator state for the move (e.g. a bear 'Stand' state). Empty = call / idle / death fallback.")]
            public string surpriseState = "";
            [Tooltip("Set by defaults once; untick to re-apply this species' surprise defaults.")]
            public bool m3SurpriseApplied;

            [Header("Temperament (aggressive species): 0 = Aggressive, 0.5 = Passive-aggressive, 1 = Sneaky")]
            [Tooltip("Each placed animal rolls its temperament in this range from its seed (unless the instance overrides it).")]
            [Range(0f, 1f)] public float temperamentMin = 0f;
            [Range(0f, 1f)] public float temperamentMax = 1f;
            [Tooltip("Set by defaults once; untick to re-apply this species' temperament range.")]
            public bool m4TemperamentApplied;

            public List<Drop> drops = new List<Drop>();
        }

        [Header("Size variants (Young / Adult / Alpha). Play-editable.")]
        [Range(0.3f, 1f)] public float youngScale = 0.7f;
        [Range(1f, 1.6f)] public float alphaScale = 1.18f;
        [Range(0.2f, 1f)] public float youngHealthMul = 0.6f;
        [Range(1f, 2.5f)] public float alphaHealthMul = 1.45f;
        [Range(0.2f, 1f)] public float youngDamageMul = 0.55f;
        [Range(1f, 2.5f)] public float alphaDamageMul = 1.35f;
        [Range(0.5f, 1.5f)] public float youngSpeedMul = 1.08f;
        [Range(0.5f, 1.5f)] public float alphaSpeedMul = 0.96f;

        public Species wolf, bear, fox, rabbit, raccoon, deer, fawn, boar, cougar;

        [Header("Wrapper prefabs (filled by Setup Wildlife; not committed)")]
        public GameObject wolfPrefab, bearPrefab, foxPrefab, rabbitPrefab, raccoonPrefab, deerPrefab, fawnPrefab, boarPrefab, cougarPrefab;

        public GameObject Prefab(ASHWildlifeKind kind)
        {
            switch (kind)
            {
                case ASHWildlifeKind.Bear: return bearPrefab;
                case ASHWildlifeKind.Fox: return foxPrefab;
                case ASHWildlifeKind.Rabbit: return rabbitPrefab;
                case ASHWildlifeKind.Raccoon: return raccoonPrefab;
                case ASHWildlifeKind.Deer: return deerPrefab;
                case ASHWildlifeKind.Fawn: return fawnPrefab;
                case ASHWildlifeKind.Boar: return boarPrefab;
                case ASHWildlifeKind.Cougar: return cougarPrefab;
                default: return wolfPrefab;
            }
        }

        public void SetPrefab(ASHWildlifeKind kind, GameObject go)
        {
            switch (kind)
            {
                case ASHWildlifeKind.Bear: bearPrefab = go; break;
                case ASHWildlifeKind.Fox: foxPrefab = go; break;
                case ASHWildlifeKind.Rabbit: rabbitPrefab = go; break;
                case ASHWildlifeKind.Raccoon: raccoonPrefab = go; break;
                case ASHWildlifeKind.Deer: deerPrefab = go; break;
                case ASHWildlifeKind.Fawn: fawnPrefab = go; break;
                case ASHWildlifeKind.Boar: boarPrefab = go; break;
                case ASHWildlifeKind.Cougar: cougarPrefab = go; break;
                default: wolfPrefab = go; break;
            }
        }

        [Header("Locomotion (same walk/turn/run setup as the dog; Play-editable)")]
        [Range(0.5f, 30f)] public float acceleration = 6f;
        [Range(30f, 1080f)] public float turnSpeed = 300f;
        [Range(0f, 1f)] public float turnSmoothTime = 0.18f;
        [Range(30f, 180f)] public float turnInPlaceAngle = 100f;
        [Range(5f, 90f)] public float turnInPlaceDone = 30f;
        [Range(0.1f, 1f)] public float arcTurnSlow = 0.55f;
        [Range(30f, 720f)] public float turnBlendFullRate = 160f;
        [Range(0.2f, 3f)] public float animSpeedScale = 1f;
        [Range(0.1f, 1f)] public float animRateMin = 0.6f;
        [Range(1f, 5f)] public float animRateMax = 2.6f;
        [Range(10f, 70f)] public float maxSlope = 45f;
        [Range(0.1f, 1.5f)] public float maxStepUp = 0.45f;
        [Range(0.3f, 4f)] public float whiskerLength = 1.3f;
        [Range(10f, 80f)] public float whiskerAngle = 35f;
        public bool depenetrate = true;
        public LayerMask environmentLayers = ~((1 << 2) | (1 << 5));

        [Header("Speed / gait sync (Play-editable)")]
        [Tooltip("Global walk/trot/run multiplier for every species (0.85 = 15% slower). Animation playback follows so feet stay planted.")]
        [Range(0.3f, 1.5f)] public float speedMul = 0.85f;
        [Tooltip("Ignore measured clip speeds that are far from the species' gait speed (bad root-motion data made clips play up to 2.6x = bouncing).")]
        public bool clipSpeedSanity = true;
        [Range(0.2f, 1f)] public float clipTrustMin = 0.6f;
        [Range(1f, 5f)] public float clipTrustMax = 1.67f;
        [Tooltip("Vertical ground-follow smoothing in seconds (0 = snap each frame). Removes stair-step bobbing on uneven terrain.")]
        [Range(0f, 0.4f)] public float groundSmoothTime = 0.07f;
        [Tooltip("Ground probe ignores other animals / the player so they never stand on each other's colliders.")]
        public bool groundIgnoreCreatures = true;
        [Tooltip("Boar-style charge speed vs run.")]
        [Range(0.6f, 1.6f)] public float chargeSpeedMul = 1.0f;

        [Header("Turn-taking + stand-off (Play-editable)")]
        public bool standOffEnabled = true;
        [Tooltip("How many animals may attack the same target at once; the rest hold at their buffer.")]
        [Range(1, 6)] public int maxAttackersPerTarget = 1;
        [Tooltip("After an animal's turn it waits this long before it may take another, so others get a go.")]
        [Range(0f, 8f)] public float attackTurnGap = 1.4f;
        [Tooltip("An attacker gives up its turn if it hasn't landed an attack in this long.")]
        [Range(1f, 12f)] public float attackTurnTimeout = 4f;
        [Tooltip("Circling speed around the target while holding (degrees / second).")]
        [Range(0f, 90f)] public float circleDegPerSecond = 22f;
        [Range(0.5f, 1.6f)] public float lungeSpeedMul = 1.0f;
        [Range(0.5f, 6f)] public float backOffMaxSeconds = 2.4f;

        [Header("Lethality (Play-editable)")]
        [Tooltip("Bow/rifle/shotgun damage uses each species' bodyShotsToKill instead of raw weapon damage.")]
        public bool shotCountLethality = true;
        public bool bowVitalOneShot = true;
        public bool rifleVitalOneShot = true;
        [Tooltip("Shotgun one-shots any animal within this distance.")]
        [Range(0f, 40f)] public float shotgunOneShotMeters = 8f;
        [Tooltip("Pellets from one shotgun blast inside this window count as one shot.")]
        [Range(0.02f, 0.5f)] public float shotgunPelletWindow = 0.15f;
        [Tooltip("Pistol needs this many times the body shots (a head/heart pistol hit counts double).")]
        [Range(1f, 4f)] public float pistolShotsMul = 1.5f;
        [Range(0, 4)] public int alphaExtraShots = 0;
        [Tooltip("Head / heart zone radius as a share of body radius (zone check around head and chest bones, or body-shape estimate).")]
        [Range(0.3f, 3f)] public float headZoneScale = 1.1f;
        [Range(0.3f, 3f)] public float heartZoneScale = 0.9f;

        [Header("Seeded personality")]
        public bool personalityEnabled = true;

        [Header("Temperament blend (Play-editable). Aggressive -> Passive-aggressive -> Sneaky")]
        public bool temperamentEnabled = true;
        [Tooltip("Aggressive: stand-off buffer multiplier (smaller = presses closer).")]
        [Range(0.3f, 1.5f)] public float aggressiveBufferMul = 0.7f;
        [Tooltip("Aggressive: hold/circle time multiplier between attacks (smaller = attacks more often).")]
        [Range(0.1f, 1.5f)] public float aggressiveHoldMul = 0.45f;
        [Tooltip("Aggressive: attack cooldown multiplier.")]
        [Range(0.3f, 1.5f)] public float aggressiveCooldownMul = 0.75f;
        [Tooltip("Aggressive: lowers the alert needed to engage (0 = no change, 1 = engages on first notice).")]
        [Range(0f, 1f)] public float aggressiveEagerness = 0.5f;
        [Tooltip("Passive-aggressive: only engages if provoked (hit) or the target comes this close; otherwise warns / bluffs.")]
        [Range(1f, 30f)] public float passiveProvokeMeters = 7f;
        [Tooltip("Passive-aggressive: seconds between warnings / bluffs.")]
        [Range(1f, 120f)] public float passiveWarnCooldown = 12f;
        [Tooltip("Passive-aggressive: retreat-chance multiplier when hurt.")]
        [Range(1f, 4f)] public float passiveRetreatMul = 1.8f;
        [Tooltip("Passive-aggressive: hold time multiplier between attacks.")]
        [Range(0.5f, 3f)] public float passiveHoldMul = 1.4f;
        [Tooltip("Sneaky: back-off distance multiplier after each strike.")]
        [Range(1f, 4f)] public float sneakyWithdrawMul = 1.7f;
        [Tooltip("Sneaky: chance after a strike to melt away and stalk again instead of circling.")]
        [Range(0f, 1f)] public float sneakyRestalkChance = 0.6f;
        [Tooltip("How strongly temperament reshapes surprise chances (sneaky -> cover / circle-behind, passive-aggressive -> false charge / stand and huff).")]
        [Range(0f, 2f)] public float temperamentSurpriseBias = 1f;

        [Header("Surprise behaviours (Play-editable)")]
        public bool surprisesEnabled = true;
        [Tooltip("Multiplies every species' surprise chance (0 = off, 2 = twice as often).")]
        [Range(0f, 4f)] public float surpriseChanceMul = 1f;
        [Tooltip("Each placed animal rolls a seeded tendency in this range (multiplies its surprise chance).")]
        [Range(0f, 3f)] public float surpriseTendencyMin = 0.4f;
        [Range(0f, 3f)] public float surpriseTendencyMax = 1.6f;

        [Header("Traps")]
        [Range(0.4f, 6f)] public float trapProximityRadius = 1.15f;
        [Tooltip("Trapped wolf may break free after this many real seconds.")]
        [Range(5f, 600f)] public float wolfBreakFreeSeconds = 60f;
        [Range(0f, 1f)] public float snareRabbitChance = 0.7f;
        [Range(0f, 1f)] public float snareRaccoonChance = 0.3f;
        [Range(0f, 1f)] public float largeTrapFawnChance = 0.6f;
        [Range(0f, 1f)] public float largeTrapWolfChance = 0.4f;

        [Header("Spawning (Place Far From Camp)")]
        [Tooltip("Nothing is placed this close to the cabin, truck, player, POIs, lights, Gaia Water, porch or tire objects.")]
        [Range(10f, 300f)] public float campExclusionMeters = 80f;
        [Range(4f, 80f)] public float packSpacing = 14f;
        [Header("World senses / AI LOD (Play-editable)")]
        public bool windScent = true;
        [Range(0f, 360f)] public float windDegrees = 40f;
        [Range(0f, 180f)] public float windSpinPerMinute = 8f;
        [Range(20f, 200f)] public float aiLodMeters = 75f;
        [Range(20f, 250f)] public float gunshotHearMeters = 80f;
        [Range(4f, 40f)] public float bowHearMeters = 16f;
        [Range(4f, 40f)] public float herdRadius = 16f;
        [Range(2f, 20f)] public float packFlankRadius = 7f;
        public int spawnSeed = 0;
        [Range(0f, 1f)] public float youngChance = 0.25f;
        [Range(0f, 1f)] public float alphaChance = 0.12f;

        [Header("Dog assist")]
        [Range(8f, 80f)] public float dogAssistRange = 28f;
        [Range(1f, 20f)] public float dogDistractDistance = 5f;
        [Range(1f, 20f)] public float dogDistractRetreatSeconds = 3.5f;

        void OnEnable() => EnsureDefaults();

        public void EnsureDefaults()
        {
            if (wolf == null || wolf.kind != ASHWildlifeKind.Wolf || string.IsNullOrEmpty(wolf.displayName))
                wolf = Sp(ASHWildlifeKind.Wolf, "Wolf", ASHWildlifeTemperament.Stalk, ASHTrapSize.Large, ASHDogWildlifePolicy.Engage, 40, 10, 24, 36, 2.1f, 2.4f, 7.4f, 1.5f, 3.2f, 20, true, 3, 2);
            if (bear == null || bear.kind != ASHWildlifeKind.Bear || string.IsNullOrEmpty(bear.displayName))
                bear = Sp(ASHWildlifeKind.Bear, "Bear", ASHWildlifeTemperament.Hostile, ASHTrapSize.None, ASHDogWildlifePolicy.WarnDistractRetreat, 160, 22, 18, 28, 2.6f, 2.0f, 5.5f, 2.2f, 5.5f, 16, false, 1, 1);
            if (fox == null || fox.kind != ASHWildlifeKind.Fox || fox.displayName == "Wolf")
                fox = Sp(ASHWildlifeKind.Fox, "Fox", ASHWildlifeTemperament.Passive, ASHTrapSize.None, ASHDogWildlifePolicy.Ignore, 18, 0, 16, 16, 1.6f, 2.0f, 8.0f, 2f, 2.8f, 22, true, 2, 1);
            if (rabbit == null || rabbit.kind != ASHWildlifeKind.Rabbit || rabbit.displayName == "Wolf")
                rabbit = Sp(ASHWildlifeKind.Rabbit, "Rabbit", ASHWildlifeTemperament.Passive, ASHTrapSize.Snare, ASHDogWildlifePolicy.Ignore, 8, 0, 12, 12, 1.2f, 1.8f, 9.0f, 2f, 2.0f, 14, true, 6, 1);
            if (raccoon == null || raccoon.kind != ASHWildlifeKind.Raccoon || raccoon.displayName == "Wolf")
                raccoon = Sp(ASHWildlifeKind.Raccoon, "Raccoon", ASHWildlifeTemperament.Passive, ASHTrapSize.Snare, ASHDogWildlifePolicy.Ignore, 14, 0, 11, 11, 1.4f, 1.6f, 6.5f, 2f, 2.6f, 16, true, 3, 1);
            if (deer == null || deer.kind != ASHWildlifeKind.Deer || deer.displayName == "Wolf")
                deer = Sp(ASHWildlifeKind.Deer, "Deer", ASHWildlifeTemperament.Passive, ASHTrapSize.None, ASHDogWildlifePolicy.Ignore, 45, 0, 20, 20, 2.0f, 2.4f, 11f, 2f, 4.0f, 24, true, 3, 1);
            if (fawn == null || fawn.kind != ASHWildlifeKind.Fawn || fawn.displayName == "Wolf")
                fawn = Sp(ASHWildlifeKind.Fawn, "Fawn", ASHWildlifeTemperament.Passive, ASHTrapSize.Large, ASHDogWildlifePolicy.Ignore, 22, 0, 14, 14, 1.6f, 2.0f, 8.5f, 2f, 3.2f, 16, true, 2, 1);
            if (boar == null || boar.kind != ASHWildlifeKind.Boar || boar.displayName == "Wolf")
                boar = Sp(ASHWildlifeKind.Boar, "Boar", ASHWildlifeTemperament.Hostile, ASHTrapSize.None, ASHDogWildlifePolicy.Engage, 80, 14, 16, 22, 2.2f, 2.1f, 8.0f, 1.8f, 4.2f, 18, true, 2, 1);
            if (cougar == null || cougar.kind != ASHWildlifeKind.Cougar || cougar.displayName == "Wolf")
                cougar = Sp(ASHWildlifeKind.Cougar, "Cougar", ASHWildlifeTemperament.Stalk, ASHTrapSize.None, ASHDogWildlifePolicy.Engage, 70, 18, 26, 42, 2.2f, 2.6f, 9.2f, 1.4f, 4.0f, 22, true, 1, 1);
            if (fawn.baseScale < 0.2f) fawn.baseScale = 0.72f;
            if (rabbit.baseScale < 0.2f) rabbit.baseScale = 0.85f;
            if (wolf.placeCount <= 0) wolf.placeCount = 3;
            if (wolf.packSize <= 0) wolf.packSize = 2;
            if (bear.placeCount <= 0) bear.placeCount = 1;
            if (fox.placeCount <= 0) fox.placeCount = 2;
            if (rabbit.placeCount <= 0) rabbit.placeCount = 6;
            if (raccoon.placeCount <= 0) raccoon.placeCount = 3;
            if (deer.placeCount <= 0) deer.placeCount = 3;
            if (fawn.placeCount <= 0) fawn.placeCount = 2;
            if (boar.placeCount <= 0) boar.placeCount = 2;
            if (cougar.placeCount <= 0) cougar.placeCount = 1;
            if (wolf.trapSize == ASHTrapSize.None) wolf.trapSize = ASHTrapSize.Large;
            if (wolf.temperament == ASHWildlifeTemperament.Hostile) wolf.temperament = ASHWildlifeTemperament.Stalk;
            if (fawn.trapSize == ASHTrapSize.None) fawn.trapSize = ASHTrapSize.Large;
            if (rabbit.trapSize == ASHTrapSize.None) rabbit.trapSize = ASHTrapSize.Snare;
            if (raccoon.trapSize == ASHTrapSize.None) raccoon.trapSize = ASHTrapSize.Snare;
            ApplyCombatProfile(wolf, 1f, 1f, 1f, 1f, 1.7f, 0.7f, 1.25f, false, true, false, false, false);
            ApplyCombatProfile(bear, 0.7f, 0.45f, 0.65f, 0.8f, 1.35f, 1.1f, 0.85f, false, false, true, false, false);
            ApplyCombatProfile(fox, 1.15f, 1.2f, 1.15f, 1.1f, 1.8f, 1f, 1.1f, false, false, false, false, false);
            ApplyCombatProfile(rabbit, 1.25f, 1.3f, 1.25f, 1.2f, 2f, 1f, 1.15f, false, false, false, false, false);
            ApplyCombatProfile(raccoon, 1.1f, 1.15f, 1.1f, 1.05f, 1.7f, 0.9f, 1.2f, false, false, false, false, false);
            ApplyCombatProfile(deer, 1f, 1f, 1f, 1f, 1.7f, 1.15f, 0.9f, true, false, false, false, false);
            ApplyCombatProfile(fawn, 1.2f, 1.2f, 1.15f, 1.15f, 1.9f, 1.1f, 1f, true, false, false, false, false);
            ApplyCombatProfile(boar, 0.85f, 0.7f, 0.85f, 0.9f, 1.45f, 1.1f, 0.8f, false, false, false, false, true);
            ApplyCombatProfile(cougar, 1f, 1.05f, 1f, 1f, 1.8f, 0.55f, 1.35f, false, false, false, true, false);
            //            shots standOff holdMin holdMax  bold(min,max)  aggr(min,max)
            ApplyTune(wolf,    3, 4.5f, 0.8f, 2.4f, 0.35f, 0.85f, 0.3f, 0.85f);
            ApplyTune(bear,    4, 4.0f, 1.0f, 2.6f, 0.4f, 0.95f, 0.3f, 0.9f);
            ApplyTune(fox,     1, 0f, 0f, 0f, 0.15f, 0.6f, 0f, 0.3f);
            ApplyTune(rabbit,  1, 0f, 0f, 0f, 0f, 0.35f, 0f, 0.1f);
            ApplyTune(raccoon, 1, 0f, 0f, 0f, 0.2f, 0.7f, 0f, 0.4f);
            ApplyTune(deer,    2, 0f, 0f, 0f, 0.05f, 0.55f, 0f, 0.3f);
            ApplyTune(fawn,    1, 0f, 0f, 0f, 0f, 0.4f, 0f, 0.1f);
            ApplyTune(boar,    3, 6.0f, 0.9f, 2.2f, 0.4f, 0.95f, 0.5f, 1f);
            ApplyTune(cougar,  3, 5.0f, 0.6f, 1.8f, 0.5f, 1f, 0.4f, 0.95f);
            //                                                    chance cdMin cdMax  secs  meters burst
            ApplySurprise(deer,    ASHSurpriseKind.FreezeThenBolt, 0.45f, 40f, 110f, 2.2f, 0f,   1.25f);
            ApplySurprise(fawn,    ASHSurpriseKind.FreezeThenBolt, 0.55f, 30f, 90f,  1.6f, 0f,   1.2f);
            ApplySurprise(rabbit,  ASHSurpriseKind.FreezeThenBolt, 0.5f,  20f, 60f,  2.6f, 0f,   1.3f);
            ApplySurprise(wolf,    ASHSurpriseKind.CircleBehind,   0.4f,  45f, 120f, 1.2f, 7f,   1.15f);
            ApplySurprise(boar,    ASHSurpriseKind.FalseCharge,    0.5f,  40f, 100f, 1.6f, 4.5f, 1.2f);
            ApplySurprise(bear,    ASHSurpriseKind.StandAndHuff,   0.55f, 60f, 160f, 2.8f, 0f,   1.1f);
            ApplySurprise(fox,     ASHSurpriseKind.Pounce,         0.3f,  25f, 70f,  0.9f, 2.4f, 1.6f);
            ApplySurprise(raccoon, ASHSurpriseKind.PlayDead,       0.45f, 60f, 150f, 6f,   4f,   1.3f);
            ApplySurprise(cougar,  ASHSurpriseKind.StalkFromCover, 0.5f,  60f, 160f, 3.5f, 18f,  1.3f);
            //                         min   max   (0 aggressive .. 0.5 passive-aggressive .. 1 sneaky)
            ApplyTemperament(wolf,    0.15f, 0.9f);
            ApplyTemperament(bear,    0.2f,  0.65f);
            ApplyTemperament(boar,    0f,    0.55f);
            ApplyTemperament(cougar,  0.55f, 1f);
        }

        static void ApplyTemperament(Species sp, float min, float max)
        {
            if (sp == null || sp.m4TemperamentApplied) return;
            sp.temperamentMin = min; sp.temperamentMax = max;
            sp.m4TemperamentApplied = true;
        }

        static void ApplySurprise(Species sp, ASHSurpriseKind k, float chance, float cdMin, float cdMax, float secs, float meters, float burst)
        {
            if (sp == null || sp.m3SurpriseApplied) return;
            sp.surprise = k; sp.surpriseChance = chance; sp.surpriseCooldownMin = cdMin; sp.surpriseCooldownMax = cdMax;
            sp.surpriseSeconds = secs; if (meters > 0f) sp.surpriseMeters = meters; sp.surpriseBurstMul = burst;
            sp.m3SurpriseApplied = true;
        }

        static void ApplyTune(Species sp, int shots, float standOff, float holdMin, float holdMax, float boldMin, float boldMax, float aggrMin, float aggrMax)
        {
            if (sp == null || sp.m2TuneApplied) return;
            sp.bodyShotsToKill = shots;
            sp.standOffMeters = standOff; sp.standOffHoldMin = holdMin; sp.standOffHoldMax = holdMax;
            if (sp.personality == null) sp.personality = new PersonalityRange();
            var p = sp.personality;
            p.boldnessMin = boldMin; p.boldnessMax = boldMax; p.aggressionMin = aggrMin; p.aggressionMax = aggrMax;
            if (sp.kind == ASHWildlifeKind.Deer || sp.kind == ASHWildlifeKind.Fawn) { p.cohesionMin = 0.6f; p.cohesionMax = 1.1f; p.grazeMin = 0.8f; p.grazeMax = 1.6f; }
            if (sp.kind == ASHWildlifeKind.Wolf) { p.cohesionMin = 0.75f; p.cohesionMax = 1.25f; }
            if (sp.kind == ASHWildlifeKind.Cougar) { p.wanderMin = 1f; p.wanderMax = 1.8f; }
            if (sp.kind == ASHWildlifeKind.Bear) { p.speedMin = 0.95f; p.speedMax = 1.05f; }
            sp.m2TuneApplied = true;
        }

        public PersonalityRange Ranges(Species sp) => sp != null && sp.personality != null ? sp.personality : new PersonalityRange();

        public int ShotsToKill(Species sp, ASHWildlifeSize size)
        {
            int n = sp != null ? Mathf.Max(1, sp.bodyShotsToKill) : 2;
            if (size == ASHWildlifeSize.Alpha) n += alphaExtraShots;
            return Mathf.Max(1, n);
        }

        static void ApplyCombatProfile(Species sp, float incoming, float arrow, float bullet, float melee, float vital, float day, float night, bool herd, bool flank, bool bluff, bool ambush, bool charge)
        {
            if (sp == null || sp.combatProfileApplied) return;
            sp.incomingMul = incoming; sp.arrowMul = arrow; sp.bulletMul = bullet; sp.meleeMul = melee; sp.vitalMul = vital;
            sp.dayActivity = day; sp.nightActivity = night;
            sp.herdFlee = herd; sp.packFlank = flank; sp.bluffCharge = bluff; sp.ambush = ambush; sp.chargeAndTurn = charge;
            if (sp.kind == ASHWildlifeKind.Deer || sp.kind == ASHWildlifeKind.Fawn || sp.kind == ASHWildlifeKind.Rabbit) sp.grazeChance = 0.55f;
            if (sp.kind == ASHWildlifeKind.Bear) { sp.scaredByTorch = false; sp.retreatChance = 0.25f; sp.retreatHealth = 0.22f; }
            if (sp.kind == ASHWildlifeKind.Wolf) { sp.retreatChance = 0.5f; sp.retreatHealth = 0.35f; }
            if (sp.kind == ASHWildlifeKind.Cougar) { sp.sightAngle = 110f; sp.scentRange = 22f; }
            if (sp.kind == ASHWildlifeKind.Boar) { sp.retreatChance = 0.3f; sp.attackRange = Mathf.Max(sp.attackRange, 2.4f); }
            sp.combatProfileApplied = true;
        }

        static Species Sp(ASHWildlifeKind k, string name, ASHWildlifeTemperament t, ASHTrapSize trap, ASHDogWildlifePolicy dog,
            float hp, float dmg, float detect, float stalk, float atkR, float walk, float run, float cd, float hold, float wander, bool torch, int count, int pack)
        {
            return new Species
            {
                kind = k, displayName = name, temperament = t, trapSize = trap, dogPolicy = dog,
                maxHealth = hp, damage = dmg, detectRange = detect, stalkRange = stalk, attackRange = atkR,
                walkSpeed = walk, runSpeed = run, attackCooldown = cd, harvestHold = hold, wanderRadius = wander,
                scaredByTorch = torch, placeCount = count, packSize = pack, drops = new List<Drop>()
            };
        }

        public Species Of(ASHWildlifeKind kind)
        {
            EnsureDefaults();
            switch (kind)
            {
                case ASHWildlifeKind.Bear: return bear;
                case ASHWildlifeKind.Fox: return fox;
                case ASHWildlifeKind.Rabbit: return rabbit;
                case ASHWildlifeKind.Raccoon: return raccoon;
                case ASHWildlifeKind.Deer: return deer;
                case ASHWildlifeKind.Fawn: return fawn;
                case ASHWildlifeKind.Boar: return boar;
                case ASHWildlifeKind.Cougar: return cougar;
                default: return wolf;
            }
        }

        public IEnumerable<Species> All()
        {
            EnsureDefaults();
            yield return wolf; yield return bear; yield return fox; yield return rabbit;
            yield return raccoon; yield return deer; yield return fawn; yield return boar; yield return cougar;
        }

        public float Scale(Species sp, ASHWildlifeSize size)
        {
            if (sp == null) return 1f;
            float s = sp.baseScale > 0.1f ? sp.baseScale : 1f;
            if (size == ASHWildlifeSize.Young) s *= youngScale;
            else if (size == ASHWildlifeSize.Alpha) s *= alphaScale;
            return s;
        }

        public float Health(Species sp, ASHWildlifeSize size)
        {
            if (sp == null) return 1f;
            float h = sp.maxHealth;
            if (size == ASHWildlifeSize.Young) h *= youngHealthMul;
            else if (size == ASHWildlifeSize.Alpha) h *= alphaHealthMul;
            return Mathf.Max(1f, h);
        }

        public float Damage(Species sp, ASHWildlifeSize size)
        {
            if (sp == null) return 0f;
            float d = sp.damage;
            if (size == ASHWildlifeSize.Young) d *= youngDamageMul;
            else if (size == ASHWildlifeSize.Alpha) d *= alphaDamageMul;
            return Mathf.Max(0f, d);
        }

        public float Incoming(Species sp, ASHWildlifeSize size, float raw, string weaponKind, bool vital)
        {
            if (sp == null) return Mathf.Max(0f, raw);
            float m = sp.incomingMul;
            if (weaponKind == "arrow") m *= sp.arrowMul;
            else if (weaponKind == "bullet") m *= sp.bulletMul;
            else m *= sp.meleeMul;
            if (vital) m *= sp.vitalMul;
            if (size == ASHWildlifeSize.Young) m *= 1.2f;
            else if (size == ASHWildlifeSize.Alpha) m *= 0.9f;
            return Mathf.Max(0f, raw * m);
        }

        public float SightRange(Species sp)
        {
            if (sp == null) return 22f;
            return sp.sightRange > 0.5f ? sp.sightRange : Mathf.Max(sp.detectRange, sp.stalkRange);
        }

        public float Walk(Species sp, ASHWildlifeSize size) => Speed(sp != null ? sp.walkSpeed : 2f, size);
        public float Trot(Species sp, ASHWildlifeSize size)
        {
            if (sp == null) return Speed(3f, size);
            float t = sp.trotSpeed > 0.2f ? sp.trotSpeed : (sp.walkSpeed + sp.runSpeed) * 0.5f;
            return Speed(t, size);
        }
        public float Run(Species sp, ASHWildlifeSize size) => Speed(sp != null ? sp.runSpeed : 6f, size);

        public ASHGroundLocomotion.Tune Loco(Species sp, ASHWildlifeSize size)
        {
            var t = new ASHGroundLocomotion.Tune();
            t.walkSpeed = Walk(sp, size);
            t.trotSpeed = Trot(sp, size);
            t.runSpeed = Run(sp, size);
            if (sp != null)
            {
                // Reference speeds = ground speed at which each clip plays at 1x. Raw (pre speedMul) species gaits are what the
                // clips were tuned against, so slowing the animal slows playback in step (no foot sliding).
                float rw = Mathf.Max(0.2f, sp.walkSpeed), rr = Mathf.Max(rw + 0.1f, sp.runSpeed);
                float rt = sp.trotSpeed > 0.2f ? sp.trotSpeed : (rw + rr) * 0.5f;
                t.walkClipSpeed = ClipRef(sp.walkClipSpeed, rw);
                t.trotClipSpeed = ClipRef(sp.trotClipSpeed, rt);
                t.runClipSpeed = ClipRef(sp.runClipSpeed, rr);
                // controller blend thresholds were built from the raw gaits: feed Speed in those units
                t.speedParamScale = rr / Mathf.Max(0.2f, t.runSpeed);
            }
            t.groundSmoothTime = groundSmoothTime;
            t.groundIgnoreCreatures = groundIgnoreCreatures;
            t.turnSpeed = turnSpeed;
            t.turnSmoothTime = turnSmoothTime;
            t.acceleration = acceleration;
            t.turnInPlaceAngle = turnInPlaceAngle;
            t.turnInPlaceDone = turnInPlaceDone;
            t.arcTurnSlow = arcTurnSlow;
            t.turnBlendFullRate = turnBlendFullRate;
            t.animSpeedScale = animSpeedScale;
            t.animRateMin = animRateMin;
            t.animRateMax = animRateMax;
            t.maxSlope = maxSlope;
            t.maxStepUp = maxStepUp;
            t.whiskerLength = whiskerLength;
            t.whiskerAngle = whiskerAngle;
            t.depenetrate = depenetrate;
            t.environmentLayers = environmentLayers;
            float sc = Scale(sp, size);
            var body = Body(sp);
            t.bodyRadius = body.x * sc;
            t.bodyLength = body.y * sc;
            t.bodyHeight = body.z * sc;
            return t;
        }

        float ClipRef(float measured, float gait)
        {
            if (measured <= 0.05f) return gait;
            if (!clipSpeedSanity) return measured;
            float r = measured / Mathf.Max(0.05f, gait);
            return r < clipTrustMin || r > clipTrustMax ? gait : measured;
        }

        public Vector3 Body(Species sp)
        {
            if (sp != null && sp.bodyRadius > 0.05f)
                return new Vector3(sp.bodyRadius, sp.bodyLength, sp.bodyHeight);
            switch (sp != null ? sp.kind : ASHWildlifeKind.Wolf)
            {
                case ASHWildlifeKind.Bear: return new Vector3(0.45f, 1.1f, 0.7f);
                case ASHWildlifeKind.Fox: return new Vector3(0.18f, 0.45f, 0.3f);
                case ASHWildlifeKind.Rabbit: return new Vector3(0.12f, 0.25f, 0.16f);
                case ASHWildlifeKind.Raccoon: return new Vector3(0.16f, 0.35f, 0.25f);
                case ASHWildlifeKind.Deer: return new Vector3(0.28f, 0.9f, 0.7f);
                case ASHWildlifeKind.Fawn: return new Vector3(0.2f, 0.55f, 0.45f);
                case ASHWildlifeKind.Boar: return new Vector3(0.32f, 0.7f, 0.45f);
                case ASHWildlifeKind.Cougar: return new Vector3(0.28f, 0.85f, 0.45f);
                default: return new Vector3(0.28f, 0.7f, 0.5f);
            }
        }

        float Speed(float v, ASHWildlifeSize size)
        {
            v *= speedMul;
            if (size == ASHWildlifeSize.Young) v *= youngSpeedMul;
            else if (size == ASHWildlifeSize.Alpha) v *= alphaSpeedMul;
            return Mathf.Max(0.2f, v);
        }
    }
}
