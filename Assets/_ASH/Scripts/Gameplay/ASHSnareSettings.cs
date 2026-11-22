using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Play-editable snare and game-trap rules. Resources/ASH_SnareSettings.</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Snare Settings", fileName = "ASH_SnareSettings")]
    public sealed class ASHSnareSettings : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHSnareSettings()
        {
            _i = default;
        }

        [Header("Catch (game hours)")]
        [Tooltip("Game hours after placing or resetting before the first catch roll.")]
        [Range(0.1f, 48f)] public float firstCheckHours = 2f;
        [Tooltip("If the first roll misses, try again this often.")]
        [Range(0.1f, 24f)] public float retryHours = 1f;
        [Tooltip("Chance a waiting snare catches prey on each check (0-1). Overridden per trap size if species chances are set on wildlife settings.")]
        [Range(0f, 1f)] public float catchChance = 0.65f;
        [Tooltip("Snares inside a building or the cabin never catch.")]
        public bool requireOutdoors = true;

        [Header("Live walk-in")]
        [Tooltip("A live animal of the right size that wanders this close is caught, in addition to the timed roll.")]
        [Range(0.4f, 6f)] public float proximityRadius = 1.15f;

        [Header("Large trap / wolf")]
        [Tooltip("Trapped wolf may break free after this many real seconds.")]
        [Range(5f, 600f)] public float wolfBreakFreeSeconds = 60f;

        [Header("Use")]
        [Range(0.2f, 8f)] public float collectHoldSeconds = 1.2f;
        [Range(0.2f, 8f)] public float packHoldSeconds = 1.2f;
        public string setPrompt = "Snare is set";
        public string collectPrompt = "Collect catch";
        public string packPrompt = "Pack up trap";
        public string harvestPrompt = "Harvest with a knife";
        public string wolfTrappedPrompt = "The wolf is trapped — stay back";
        public string noSpaceMessage = "No room in the pack.";
        public string indoorMessage = "Too close to a building";
        public string caughtToast = "The trap caught something.";
        public string wolfCaughtToast = "A wolf is caught in the trap — it's still dangerous.";
        public string wolfBrokeFreeToast = "The wolf broke free of the trap.";

        [Header("Placement")]
        [Range(0.4f, 8f)] public float footprintX = 0.8f;
        [Range(0.2f, 4f)] public float footprintY = 0.5f;
        [Range(0.4f, 8f)] public float footprintZ = 0.8f;
        [Range(0f, 45f)] public float maxSlope = 22f;
        [Range(0.4f, 8f)] public float largeFootprintX = 1.4f;
        [Range(0.2f, 4f)] public float largeFootprintY = 0.7f;
        [Range(0.4f, 8f)] public float largeFootprintZ = 1.4f;

        static ASHSnareSettings _i;
        public static ASHSnareSettings I
        {
            get
            {
                if (!_i) { _i = Resources.Load<ASHSnareSettings>("ASH_SnareSettings"); if (!_i) _i = CreateInstance<ASHSnareSettings>(); }
                return _i;
            }
        }
    }
}
