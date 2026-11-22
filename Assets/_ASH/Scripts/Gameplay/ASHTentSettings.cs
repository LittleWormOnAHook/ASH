using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Play-editable tent rules (pitch / pack, sealing, campfire distance and the fire recovery bonus). Resources/ASH_TentSettings.</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Tent Settings", fileName = "ASH_TentSettings")]
    public sealed class ASHTentSettings : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHTentSettings()
        {
            _i = default;
        }

        public enum Kind { Packed, Built }

        [Header("Sealing (tents are not structural: the quake still hits you)")]
        [Tooltip("Ash sealing of a pitched packed tent, with no fire.")]
        [Range(0f, 1f)] public float packedSealing = 0.6f;
        [Tooltip("Ash sealing of a built tent shelter, with no fire.")]
        [Range(0f, 1f)] public float builtSealing = 0.5f;

        [Header("Campfire")]
        [Tooltip("A lit campfire this close (metres) adds its heat (already) and the recovery bonus below.")]
        [Range(0.5f, 30f)] public float fireBonusRadius = 6f;
        [Tooltip("Stamina (and health) recovery multiplier while inside a tent with a lit campfire in range.")]
        [Range(1f, 5f)] public float fireRecoveryMultiplier = 1.5f;
        [Tooltip("Campfires cannot be placed closer than this to a tent (metres, from the tent volume).")]
        [Range(0f, 10f)] public float minCampfireDistance = 1.5f;
        [Tooltip("Only one pitched tent of each type at a time.")]
        public bool onePerType = true;

        [Header("Pack up")]
        [Range(0.2f, 8f)] public float packHoldSeconds = 1.6f;
        [Range(1f, 8f)] public float packUseRange = 3f;
        public string packPackedPrompt = "Pack up tent";
        public string packBuiltPrompt = "Pack up built tent";
        public string noSpaceMessage = "No room in the pack to take the tent down.";
        public string tooCloseTentMessage = "Too close to a tent";
        public string tooCloseFireMessage = "Too close to a campfire";
        public string alreadyPitchedMessage = "A tent of this type is already pitched";

        [Header("Ground snap (t565: pitched tents sit on the ground, pegs and base touching)")]
        [Tooltip("Player-placed tents settle onto the ground under their footprint (terrain or rock), tilted to the slope. Hand-placed scene tents are never moved.")]
        public bool snapToGround = true;
        [Tooltip("Max tilt to follow the slope (degrees). 0 = always upright.")]
        [Range(0f, 45f)] public float maxTilt = 20f;
        [Tooltip("Sink the base this far into the ground (metres) so the edges and pegs never show a gap.")]
        [Range(-0.5f, 1f)] public float groundSink = 0.05f;
        [Tooltip("On bumpy ground the tent drops until no footprint corner floats, but never more than this extra (metres).")]
        [Range(0f, 3f)] public float maxExtraSink = 1f;
        [Tooltip("Footprint corners sampled this fraction in from the mesh edge (0 = exact edge).")]
        [Range(0f, 0.45f)] public float footprintInset = 0.1f;
        [Tooltip("t568: base = bottom of the tent's BoxCollider (the authored footprint) instead of the mesh bounds (ropes / pegs can reach lower).")]
        public bool baseFromCollider = true;
        [Tooltip("Ground samples per side of the footprint (grid N x N + centre).")]
        [Range(2, 8)] public int footprintSamples = 4;
        [Tooltip("A sample may float this much (metres) before the tent drops further.")]
        [Range(0f, 0.5f)] public float floatTolerance = 0.02f;
        [Tooltip("Sit on top of the visible ash / snow layer (visual only, characters sink into it) instead of the terrain under it.")]
        public bool sitOnAshLayer = true;
        [Tooltip("Share of the layer depth to raise the base by (1 = on top of the layer, 0.5 = half sunk in).")]
        [Range(0f, 2f)] public float ashDepthFactor = 0.6f;
        [Tooltip("How far below the tent's current height the ground is searched (metres).")]
        [Range(1f, 100f)] public float groundSearchDepth = 20f;
        [Tooltip("Colliders smaller than this (largest horizontal side, metres) are not ground: grass, pickups, debris.")]
        [Range(0f, 5f)] public float minGroundColliderSize = 0.75f;
        [Tooltip("Re-settle at these times (seconds after the tent appears), once the layer and streamed colliders are ready.")]
        public float[] resettleDelays = { 0.25f, 1.5f };
        [Tooltip("Also settle hand-placed scene tents on Play (off = they stay exactly where they were placed).")]
        public bool settleSceneTents = false;
        [Tooltip("Log one line per settle to the console (hits, tilt, ash, drop) for diagnosing.")]
        public bool logSettle = true;

        [Header("Tent menu (E on the tent)")]
        public string menuPromptPacked = "Tent";
        public string menuPromptBuilt = "Built tent";
        public string packButton = "PACK UP TENT";
        public string sleepButton = "REST / SLEEP";

        [Header("Rest / sleep")]
        [Range(1, 24)] public int maxSleepHours = 12;
        [Range(1, 24)] public int defaultSleepHours = 8;
        [Tooltip("Game hours that pass per real second while the screen is black.")]
        [Range(0.5f, 48f)] public float sleepHoursPerSecond = 4f;
        [Tooltip("Hunger and thirst drain while asleep, as a multiple of the normal awake rate for the same game time.")]
        [Range(0f, 3f)] public float sleepNeedsMultiplier = 0.6f;
        [Tooltip("Health restored per game hour of sleep (only while hunger and thirst are above the wake thresholds).")]
        [Range(0f, 50f)] public float restHealthPerHour = 3f;
        [Tooltip("Wake early when hunger falls to this fraction of max (critical).")]
        [Range(0f, 0.5f)] public float wakeHungerFraction = 0.1f;
        [Tooltip("Wake early when thirst falls to this fraction of max (critical).")]
        [Range(0f, 0.5f)] public float wakeThirstFraction = 0.1f;
        [Tooltip("Wake early if you take damage while asleep.")]
        public bool wakeOnDamage = true;
        [Range(0.05f, 5f)] public float fadeToBlackSeconds = 0.8f;
        [Range(0f, 5f)] public float blackHoldSeconds = 0.4f;
        [Range(0.05f, 5f)] public float fadeBackSeconds = 1.2f;
        public string sleptMessage = "You slept {0}.";
        public string wokeHungryMessage = "You woke early, starving.";
        public string wokeThirstyMessage = "You woke early, parched.";
        public string wokeHurtMessage = "Something woke you!";
        public string tooHungryMessage = "You're too hungry to sleep. Eat first.";
        public string tooThirstyMessage = "You're too thirsty to sleep. Drink first.";

        [Header("Labels")]
        public string packedLabel = "Packed Tent";
        public string builtLabel = "Built Tent";

        public float Sealing(Kind kind) => kind == Kind.Built ? builtSealing : packedSealing;
        public string PackPrompt(Kind kind) => kind == Kind.Built ? packBuiltPrompt : packPackedPrompt;
        public string MenuPrompt(Kind kind) => kind == Kind.Built ? menuPromptBuilt : menuPromptPacked;

        static ASHTentSettings _i;
        public static ASHTentSettings I
        {
            get
            {
                if (!_i) { _i = Resources.Load<ASHTentSettings>("ASH_TentSettings"); if (!_i) _i = CreateInstance<ASHTentSettings>(); }
                return _i;
            }
        }
    }
}
