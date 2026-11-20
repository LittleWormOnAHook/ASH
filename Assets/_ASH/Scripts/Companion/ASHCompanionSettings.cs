using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Companion
{
    /// <summary>
    /// Every dog companion tunable (Resources/ASH_CompanionSettings). Wide ranges; Relaxed / Default / Extreme presets from
    /// the component menu or ASH/Systems/Companion/Preset ... Levels: XP to the next level = xpBase * level ^ xpExponent.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Companion Settings", fileName = "ASH_CompanionSettings")]
    public sealed class ASHCompanionSettings : ScriptableObject
    {
        static ASHCompanionSettings _i;
        public static ASHCompanionSettings I { get { if (!_i) { _i = Resources.Load<ASHCompanionSettings>("ASH_CompanionSettings"); if (!_i) _i = CreateInstance<ASHCompanionSettings>(); } return _i; } }

        [Header("Dog")]
        public string dogName = "Scout";
        [Tooltip("Show a marker for the dog on the compass bar.")] public bool dogCompassMarker = true;
        public Color dogMarkerColor = new Color(0.55f, 0.85f, 1f);

        [Header("Call (C, the dog menu, or the journal Pets tab)")]
        [Tooltip("Seconds between two calls.")] [Range(0f, 120f)] public float callCooldown = 8f;
        [Tooltip("Called from farther than this (or stuck / out of sight), the dog is brought next to you; closer, it runs to you.")] [Range(2f, 200f)] public float callTeleportBeyond = 14f;
        [Tooltip("How far from you a called dog appears.")] [Range(1f, 15f)] public float callArriveDistance = 3f;
        [Tooltip("Prefer an arrival spot the camera can't see (behind you / behind cover).")] public bool callOutOfView = true;
        [Tooltip("Also bring the dog over automatically when it ends up very far away or stuck (Follow / Defend).")] public bool autoTeleportWhenFar = true;

        [Header("Movement (m, m/s)")]
        [Range(0.3f, 4f)] public float walkSpeed = 1.0f;
        [Range(0.5f, 8f)] public float trotSpeed = 2.0f;
        [Range(1f, 16f)] public float runSpeed = 5.0f;
        [Range(30f, 1080f)] public float turnSpeed = 300f;
        [Range(0.5f, 30f)] public float acceleration = 6f;
        [Tooltip("Extra multiplier on the clip playback rate after speed sync (1 = feet match the ground).")] [Range(0.2f, 3f)] public float animSpeedScale = 1f;
        [Tooltip("Seconds to smooth the turn rate (higher = lazier, wider arcs).")] [Range(0f, 1f)] public float turnSmoothTime = 0.18f;
        [Tooltip("Above this angle to the target the dog stops and turns in place (turn clips) before moving off.")] [Range(30f, 180f)] public float turnInPlaceAngle = 100f;
        [Tooltip("Turn-in-place ends once facing within this angle.")] [Range(5f, 90f)] public float turnInPlaceDone = 30f;
        [Tooltip("Speed multiplier while arcing through a turn of 90 degrees (wider arcs when lower).")] [Range(0.1f, 1f)] public float arcTurnSlow = 0.55f;
        [Tooltip("Yaw rate (deg/s) that plays the full left/right turn and strafe-turn clips.")] [Range(30f, 720f)] public float turnBlendFullRate = 160f;

        [Header("Animation sync (filled from the Shepherd_RM root-motion clips by Rebuild Dog Animator)")]
        [Tooltip("Ground speed (m/s) the Walk_F clip covers at playback 1 on this dog.")] [Range(0.1f, 6f)] public float walkClipSpeed = 0.47f;
        [Range(0.2f, 10f)] public float trotClipSpeed = 0.87f;
        [Range(0.5f, 20f)] public float runClipSpeed = 1.57f;
        [Tooltip("Playback rate range used to match the clips to the actual ground speed.")] [Range(0.1f, 1f)] public float animRateMin = 0.6f;
        [Tooltip("The pack clips have short strides (walk 0.47, trot 0.87, run 1.57 m/s at 1x), so playback goes up to this to keep the paws planted.")] [Range(1f, 5f)] public float animRateMax = 2.6f;

        [Header("Paw prints and steps")]
        [Tooltip("Paws push into accumulated snow/ash (ASHDeformer on the four feet).")] public bool pawPrints = true;
        [Tooltip("Print radius, metres.")] [Range(0.02f, 0.4f)] public float pawPrintRadius = 0.07f;
        [Tooltip("Print depth (x the accumulation controller's Footprint Depth). Fade = the controller's Deformation Refill / settle.")] [Range(0f, 1f)] public float pawPrintDepth = 0.75f;
        [Tooltip("Paw sole below the foot bone, metres.")] [Range(0f, 0.2f)] public float pawBottomOffset = 0.03f;
        public bool pawStepSounds = true;
        [Tooltip("Paw step loudness (t338: cut to a quarter of the old 0.3).")] [Range(0f, 1f)] public float pawStepVolume = 0.075f;
        [Range(0.5f, 2.5f)] public float pawStepPitch = 1.35f;
        [Tooltip("Foot height above its lowest point (m) that counts as planted / lifted.")] [Range(0.005f, 0.1f)] public float pawPlantHeight = 0.015f;
        [Range(0.01f, 0.2f)] public float pawLiftHeight = 0.03f;
        public AudioClip[] pawStepClips;
        [Range(10f, 70f)] public float maxSlope = 45f;
        [Tooltip("Highest step the dog walks up (it won't climb onto tables or crates above this).")] [Range(0.1f, 1.5f)] public float maxStepUp = 0.45f;

        [Header("Collision / avoidance")]
        [Tooltip("Layers the dog collides with and walks on (walls, floors, terrain, props).")] public LayerMask environmentLayers = ~((1 << 2) | (1 << 5));
        [Tooltip("Body capsule radius (lies along the dog's length).")] [Range(0.1f, 0.6f)] public float bodyRadius = 0.24f;
        [Tooltip("Body length between the capsule end centres.")] [Range(0f, 1.6f)] public float bodyLength = 0.6f;
        [Range(0.2f, 1.2f)] public float bodyHeight = 0.45f;
        [Tooltip("Feelers that steer around obstacles before it bumps them.")] [Range(0.3f, 4f)] public float whiskerLength = 1.3f;
        [Range(10f, 80f)] public float whiskerAngle = 35f;
        [Tooltip("Push the dog out of anything it ends up inside (walls, rotated into a post).")] public bool depenetrate = true;

        [Header("Follow")]
        [Range(0.8f, 10f)] public float followDistance = 2.6f;
        [Tooltip("Extra distance before the dog moves again (no jitter when you shuffle).")] [Range(0f, 6f)] public float followSlack = 1.2f;
        [Range(3f, 40f)] public float trotDistance = 7f;
        [Range(5f, 80f)] public float runDistance = 13f;
        [Range(15f, 300f)] public float teleportDistance = 45f;
        [Tooltip("Seconds without progress while far behind before it is put next to you.")] [Range(1f, 30f)] public float stuckTeleportSeconds = 4f;
        [Tooltip("Seconds you stand still before the dog starts wandering and sniffing around you.")] [Range(0.5f, 60f)] public float wanderAfterIdle = 4f;
        [Range(1f, 30f)] public float wanderRadius = 6f;
        [Range(1f, 30f)] public float wanderPause = 5f;
        [Tooltip("Seconds you stand still before it sits, then lies down (0 = never).")] [Range(0f, 300f)] public float sitAfterIdle = 25f;
        [Range(0f, 600f)] public float lieAfterIdle = 60f;

        [Header("Idle brain (you stand still: weighted activities, Stay uses the in-place ones)")]
        [Tooltip("Activities stay inside this radius around you.")] [Range(2f, 30f)] public float idleRadius = 7f;
        [Range(0.5f, 10f)] public float idleMinRing = 2f;
        [Tooltip("Patrol points walked back and forth (ping-pong).")] [Range(2, 6)] public int patrolPoints = 3;
        public Vector2 patrolPause = new Vector2(1f, 3f);
        [Tooltip("Shortest time any activity lasts (no rapid switching).")] [Range(0.5f, 20f)] public float minActivitySeconds = 3f;
        public Vector2 settleSeconds = new Vector2(1f, 2.5f);
        [Range(0f, 10f)] public float wPatrol = 3f; public Vector2 patrolSeconds = new Vector2(10f, 25f);
        [Range(0f, 10f)] public float wSniff = 2f; public Vector2 sniffSeconds = new Vector2(3f, 6f);
        [Range(0f, 10f)] public float wLook = 2f; public Vector2 lookSeconds = new Vector2(3f, 6f);
        [Range(0f, 10f)] public float wSit = 2f; public Vector2 sitSeconds = new Vector2(8f, 20f);
        [Tooltip("Lie weight; x3 once you've been idle past Lie After Idle.")] [Range(0f, 10f)] public float wLie = 0.6f; public Vector2 lieSeconds = new Vector2(15f, 40f);
        [Tooltip("Sleep only after a long idle (x Lie After Idle 2).")] [Range(0f, 10f)] public float wSleep = 0.8f; public Vector2 sleepSeconds = new Vector2(20f, 60f);
        [Range(0f, 10f)] public float wDig = 1f; public Vector2 digSeconds = new Vector2(4f, 8f);
        [Tooltip("Idle_2/3/4 one-shots (stretch / scratch / shake style clips in the pack).")] [Range(0f, 10f)] public float wFidget = 1.5f;
        [Header("Leg-lift (pee) on trees, bushes, campfires (t343)")]
        public bool peeEnabled = true;
        [Tooltip("Weight in the idle activity pick (rare).")] [Range(0f, 10f)] public float wPee = 0.15f;
        [Tooltip("Seconds between two pees.")] [Range(0f, 3600f)] public float peeCooldown = 420f;
        public bool peeOnTrees = true, peeOnCampfires = true;
        [Tooltip("Gap between the target's trunk and the dog's side.")] [Range(0.1f, 1.5f)] public float peeStandOff = 0.35f;
        [Range(1f, 15f)] public float peeSeconds = 4.5f;
        [Tooltip("Which hind leg the Pissing clip lifts (auto-corrected after the first pee).")] public bool peeLegRight = true;
        public bool peeStream = true; [Range(0.1f, 1f)] public float peeStreamHeight = 0.42f; public Color peeStreamColor = new Color(1f, 0.86f, 0.25f, 0.85f);
        [Tooltip("Yellow melt spot on the snow (0 = none).")] [Range(0f, 900f)] public float peeDecalSeconds = 120f;
        [Range(0.1f, 2f)] public float peeDecalSize = 0.45f; public Color peeDecalColor = new Color(0.95f, 0.78f, 0.15f, 0.65f);
        [Tooltip("Campfire hiss (CC0, Assign Growl Clips fills it).")] public AudioClip peeHissClip; [Range(0f, 2f)] public float peeHissVolume = 0.8f;
        public bool peeSteam = true;
        [Tooltip("Chance a lit campfire goes out. Default off.")] [Range(0f, 1f)] public float peeExtinguishChance = 0f;
        [Tooltip("Game hours of fuel a lit campfire loses (dims it). 0 = none.")] [Range(0f, 12f)] public float peeFuelHoursLost = 0f;
        [Tooltip("Turn toward you only beyond this angle (deadzone, no spinning).")] [Range(5f, 90f)] public float faceDeadzone = 35f;

        [Header("Dig finds (rare sellable junk)")]
        [Tooltip("Chance per finished dig.")] [Range(0f, 1f)] public float digFindChance = 0.04f;
        [Range(0f, 0.05f)] public float digFindPerLevel = 0.002f;
        [Tooltip("Keen Nose (level) multiplies the chance.")] [Range(1f, 3f)] public float digFindKeenMul = 1.5f;
        [Tooltip("Seconds after a find before the next can happen.")] [Range(0f, 3600f)] public float digFindCooldown = 240f;
        [Range(0f, 500f)] public float xpDigFind = 8f;
        [Tooltip("Junk the dog can dig up (ASH/Systems/Items/Dog Dig Junk Items fills this).")] public List<ASH.Inventory.ASHItemDefinition> digJunk = new List<ASH.Inventory.ASHItemDefinition>();
        [Range(0f, 600f)] public float stayLieAfter = 20f;

        [Header("Growl (German Shepherd, CC0 freesound 246227)")]
        [Tooltip("Low warning growls (danger, Defend, before a bite, hurt). ASH/Systems/Companion/Assign Growl Clips fills this.")] public AudioClip[] growlClips = new AudioClip[0];
        [Tooltip("Soft playful growls (digging).")] public AudioClip[] playGrowlClips = new AudioClip[0];
        [Range(0f, 2f)] public float growlVolume = 0.9f;
        [Tooltip("Random pitch +/- this.")] [Range(0f, 0.5f)] public float growlPitchVariance = 0.08f;
        [Tooltip("Min seconds between any two growls.")] [Range(0f, 60f)] public float growlCooldown = 4f;
        public bool growlOnWarn = true;
        [Tooltip("Growl once when an enemy comes inside the defend radius (Defend mode).")] public bool growlOnDefendApproach = true;
        [Tooltip("Chance to growl (instead of a bark) right before a bite.")] [Range(0f, 1f)] public float growlBeforeAttackChance = 0.6f;
        [Tooltip("Chance to growl when hurt (a big hit still whines).")] [Range(0f, 1f)] public float growlOnHurtChance = 0.4f;
        [Header("Playful dig growl")]
        [Tooltip("Chance per second while digging.")] [Range(0f, 1f)] public float digGrowlChance = 0.12f;
        [Range(0f, 2f)] public float digGrowlVolume = 0.35f;
        [Range(0f, 60f)] public float digGrowlCooldown = 5f;

        [Header("Find loot / supply crates")]
        [Tooltip("Also point out loose items lying in the world (off: only loot boxes / supply crates).")] public bool findLoot = false;
        public bool findCrates = true;
        [Range(3f, 80f)] public float detectRadius = 14f;
        [Tooltip("How far from you the dog will run off to show you something.")] [Range(5f, 100f)] public float leashRadius = 20f;
        [Range(0f, 10f)] public float ignoreCloserThan = 2.5f;
        [Range(0.25f, 10f)] public float scanInterval = 1.5f;
        [Tooltip("Seconds between two finds.")] [Range(0f, 120f)] public float findCooldown = 10f;
        [Tooltip("Seconds before the same thing is shown again.")] [Range(10f, 3600f)] public float refindCooldown = 240f;
        [Range(1f, 30f)] public float pointSeconds = 4f;
        [Range(2f, 120f)] public float markerSeconds = 20f;
        public Color lootMarkerColor = new Color(1f, 0.82f, 0.35f);
        public Color crateMarkerColor = new Color(0.75f, 0.95f, 0.55f);

        [Header("Danger")]
        public bool warnDanger = true;
        [Tooltip("Only enemies this close are warned about.")] [Range(5f, 120f)] public float dangerRadius = 16f;
        [Range(1f, 120f)] public float warnCooldown = 12f;
        [Range(0.5f, 10f)] public float warnSeconds = 2f;
        [Tooltip("Mark threats the dog senses on the compass (red enemy dots).")] public bool dangerCompassMarkers = true;
        [Tooltip("Count the training dummy as an enemy (for testing Defend).")] public bool dummyIsThreat = false;
        [Tooltip("Tags / layer names counted as enemies (plus anything with IASHDogThreat).")] public List<string> enemyTags = new List<string> { "Enemy" };

        [Header("Defend / combat")]
        [Range(10f, 1000f)] public float maxHealth = 100f;
        [Range(0f, 20f)] public float healthRegen = 1f;
        [Range(2f, 60f)] public float defendRadius = 14f;
        [Range(0.8f, 8f)] public float defendFollowDistance = 1.8f;
        [Range(0.5f, 4f)] public float attackRange = 1.5f;
        [Range(1f, 200f)] public float biteDamage = 18f;
        [Range(0.2f, 6f)] public float attackCooldown = 1.4f;
        [Range(0f, 1f)] public float attackHitDelay = 0.35f;
        [Tooltip("Health fraction where the dog breaks off and comes back to you.")] [Range(0f, 0.9f)] public float retreatAt = 0.25f;
        [Tooltip("Goes down at 0 health instead of dying; hold E on it to revive.")] public bool downedNotDead = true;
        [Range(0.5f, 10f)] public float reviveHoldSeconds = 2.5f;
        [Range(0.05f, 1f)] public float reviveHealth = 0.35f;
        [Tooltip("Gets up by itself after this many seconds (0 = only when you revive it).")] [Range(0f, 900f)] public float selfReviveSeconds = 0f;

        [Header("Needs and bond")]
        public bool hunger = true;
        [Tooltip("Hunger lost per game hour (100 = full).")] [Range(0f, 50f)] public float hungerPerGameHour = 3f;
        [Range(10f, 600f)] public float realSecondsPerGameHour = 120f;
        [Tooltip("Hunger restored per point of a food's restoreHunger.")] [Range(0.1f, 5f)] public float feedFactor = 1.5f;
        [Range(0f, 50f)] public float starvingBelow = 15f;
        public bool bond = true;
        [Range(0f, 30f)] public float bondPerPet = 3f;
        [Range(0f, 30f)] public float bondPerFeed = 5f;
        [Range(1f, 600f)] public float petCooldown = 45f;
        [Tooltip("Bond lost per game hour while starving.")] [Range(0f, 20f)] public float bondLossStarving = 2f;
        [Tooltip("At full bond the dog runs this much faster to catch up and gains this much more XP.")] [Range(0f, 1f)] public float bondBonus = 0.2f;

        [Header("Levels")]
        [Range(1, 100)] public int maxLevel = 20;
        [Range(5f, 5000f)] public float xpBase = 60f;
        [Range(1f, 3f)] public float xpExponent = 1.45f;
        [Tooltip("XP per minute while following you.")] [Range(0f, 50f)] public float xpPerFollowMinute = 2f;
        [Range(0f, 500f)] public float xpFindLoot = 6f;
        [Range(0f, 500f)] public float xpFindCrate = 12f;
        [Range(0f, 500f)] public float xpWarn = 8f;
        [Range(0f, 500f)] public float xpBite = 4f;
        [Range(0f, 1000f)] public float xpKill = 30f;
        [Range(0f, 200f)] public float xpPet = 2f;
        [Range(0f, 200f)] public float xpFeed = 4f;
        [Tooltip("Find radius +% per level.")] [Range(0f, 0.3f)] public float detectPerLevel = 0.04f;
        [Range(0f, 100f)] public float healthPerLevel = 6f;
        [Tooltip("Bite damage +% per level.")] [Range(0f, 0.5f)] public float damagePerLevel = 0.05f;
        [Range(1, 24)] public int basePackSlots = 6;
        [Tooltip("Each level listed adds one pack slot.")] public List<int> extraSlotLevels = new List<int> { 5, 10, 15 };
        [Tooltip("Ability unlock levels.")] [Range(1, 100)] public int crateFindLevel = 1;
        [Range(1, 100)] public int keenNoseLevel = 7;
        [Tooltip("Keen Nose: find radius x this.")] [Range(1f, 3f)] public float keenNoseMul = 1.35f;
        [Range(1, 100)] public int sentinelLevel = 4;
        [Tooltip("Sentinel: danger radius x this.")] [Range(1f, 3f)] public float sentinelMul = 1.4f;
        [Range(1, 100)] public int toughLevel = 12;
        [Tooltip("Tough: damage taken x this.")] [Range(0.1f, 1f)] public float toughMul = 0.7f;

        public float XpToNext(int level) => level >= maxLevel ? 0f : Mathf.Max(1f, xpBase * Mathf.Pow(Mathf.Max(1, level), xpExponent));
        public int PackSlots(int level) { int n = basePackSlots; if (extraSlotLevels != null) foreach (var l in extraSlotLevels) if (level >= l) n++; return Mathf.Clamp(n, 1, 24); }
        public float DetectRadius(int level) => detectRadius * (1f + detectPerLevel * (level - 1)) * (level >= keenNoseLevel ? keenNoseMul : 1f);
        public float DangerRadius(int level) => dangerRadius * (level >= sentinelLevel ? sentinelMul : 1f);
        public float MaxHealth(int level) => maxHealth + healthPerLevel * (level - 1);
        public float Damage(int level) => biteDamage * (1f + damagePerLevel * (level - 1));

        public struct Ability { public string name, desc; public int level; }
        /// <summary>The ability ladder shown in the journal (Pets tab).</summary>
        public List<Ability> Abilities()
        {
            var l = new List<Ability>
            {
                new Ability { level = 1, name = "Companion", desc = "Follow, Stay and Defend. Carries a small pack." },
                new Ability { level = 1, name = "Sniff out loot", desc = "Points and barks at nearby pickups." },
                new Ability { level = 1, name = "Watchdog", desc = "Growls when enemies come near." },
                new Ability { level = crateFindLevel, name = "Find supplies", desc = "Shows you unopened supply crates." },
                new Ability { level = sentinelLevel, name = "Sentinel", desc = $"Senses danger x{sentinelMul:0.##} further away." },
                new Ability { level = keenNoseLevel, name = "Keen nose", desc = $"Finds things x{keenNoseMul:0.##} further away." },
                new Ability { level = toughLevel, name = "Tough", desc = $"Takes {Mathf.RoundToInt((1f - toughMul) * 100f)}% less damage." },
            };
            if (extraSlotLevels != null) foreach (var s in extraSlotLevels) l.Add(new Ability { level = s, name = "Bigger pack", desc = "+1 pack slot." });
            l.Sort((a, b) => a.level.CompareTo(b.level));
            return l;
        }

        [ContextMenu("Preset: Relaxed")] public void PresetRelaxed() { Defaults(); detectRadius = 22f; leashRadius = 28f; findCooldown = 5f; dangerRadius = 30f; maxHealth = 160f; healthRegen = 3f; hungerPerGameHour = 1.5f; xpBase = 35f; xpExponent = 1.3f; retreatAt = 0.35f; selfReviveSeconds = 60f; }
        [ContextMenu("Preset: Default")] public void PresetDefault() { Defaults(); }
        [ContextMenu("Preset: Extreme")] public void PresetExtreme() { Defaults(); detectRadius = 8f; leashRadius = 12f; findCooldown = 30f; dangerRadius = 14f; maxHealth = 60f; healthRegen = 0.2f; hungerPerGameHour = 8f; bondLossStarving = 6f; xpBase = 150f; xpExponent = 1.8f; retreatAt = 0.1f; selfReviveSeconds = 0f; biteDamage = 12f; }

        void Defaults()
        {
            var d = CreateInstance<ASHCompanionSettings>();
            string keepName = dogName; float w = walkClipSpeed, t = trotClipSpeed, r = runClipSpeed; var pc = pawStepClips; var dj = digJunk; var gc = growlClips; var gp = playGrowlClips; var hc = peeHissClip;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(d), this); DestroyImmediate(d);
            dogName = keepName; walkClipSpeed = w; trotClipSpeed = t; runClipSpeed = r; pawStepClips = pc; digJunk = dj; growlClips = gc; playGrowlClips = gp; peeHissClip = hc;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }

    /// <summary>Anything the dog should treat as an enemy (future ASH enemies / wildlife implement this).</summary>
    public interface IASHDogThreat { bool IsThreat { get; } }

    /// <summary>A loot box / container the dog points out (supply crates are found directly).</summary>
    public interface IASHDogFindable { bool DogCanFind { get; } string DogFindLabel { get; } }
}