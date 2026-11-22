using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// All survival balancing in one asset. Rates are per real minute / second at the default 48-minute day
    /// (1 game hour = 2 real minutes); ASHSurvivalStats applies them on the game clock, so they scale with day length. Wide ranges plus
    /// presets (Relaxed / Default / Extreme) for stress testing.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Survival/Survival Tuning", fileName = "ASH_SurvivalTuning")]
    public sealed class ASHSurvivalTuning : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHSurvivalTuning()
        {
            _bound = default;
        }

        [Header("Maximums")]
        [Range(10f, 1000f)] public float maxHealth = 100f;
        [Range(10f, 1000f)] public float maxStamina = 100f;
        [Range(10f, 1000f)] public float maxHunger = 100f;
        [Range(10f, 1000f)] public float maxThirst = 100f;

        [Header("Start values (fraction of max)")]
        [Range(0f, 1f)] public float startHunger = 0.65f;
        [Range(0f, 1f)] public float startThirst = 0.55f;

        [Header("Needs decay (points per minute at the default 48-min day = per half game hour; runs on the game clock)")]
        [Range(0f, 100f)] public float hungerPerMinute = 1.1f;
        [Range(0f, 100f)] public float thirstPerMinute = 1.6f;
        [Tooltip("Needs decay multiplier while sprinting.")]
        [Range(1f, 10f)] public float sprintNeedsMultiplier = 1.75f;
        [Tooltip("Global multiplier for stress tests (1 = normal).")]
        [Range(0.1f, 60f)] public float needsRateMultiplier = 1f;

        [Header("Global drain rate")]
        [Tooltip("Speeds hunger, thirst, sprint stamina drain, outdoor temperature loss, and wet rag / filter usage (1 = normal, 1.5 = 50% faster). Does not affect stamina or health regen, warming from fires or shelters, jump cost, or health drain. The worn rag/filter countdown shows effective game time (a 7 h rag lasts ~4.7 h at 1.5). Stacks with Needs Rate Multiplier for hunger and thirst.")]
        [Range(0f, 10f)] public float survivalRateMultiplier = 1.25f;

        static ASHSurvivalTuning _bound;
        /// <summary>Active drain-rate multiplier (1.25 if nothing is bound). See <see cref="survivalRateMultiplier"/>.</summary>
        public static float DrainRate => _bound ? Mathf.Max(0f, _bound.survivalRateMultiplier) : 1.25f;
        public static void Bind(ASHSurvivalTuning tuning) => _bound = tuning;

        [Header("Stamina (drives the Invector motor)")]
        [Range(0f, 100f)] public float sprintDrainPerSecond = 12f;
        [Range(0f, 100f)] public float staminaRecoveryPerSecond = 16f;
        [Range(0f, 100f)] public float jumpStaminaCost = 12f;
        [Header("Exertion (t412): stamina spent drains needs, thirst more than hunger; regen never counts")]
        [Tooltip("Hunger lost per stamina point spent. 0.008 = a full 100-point bar costs 0.8 hunger.")]
        [Range(0f, 0.5f)] public float staminaHungerPerPoint = 0.008f;
        [Tooltip("Thirst lost per stamina point spent. 0.02 = a full 100-point bar costs 2 thirst (non-stop sprinting adds ~8 thirst/min on top of normal decay).")]
        [Range(0f, 0.5f)] public float staminaThirstPerPoint = 0.02f;
        [Tooltip("Recovery multiplier when hunger or thirst is low.")]
        [Range(0f, 1f)] public float lowNeedsRecoveryMultiplier = 0.5f;

        [Header("Health")]
        [Tooltip("Fraction of max under which a need counts as 'low' (HUD warning, slower stamina).")]
        [Range(0.01f, 0.9f)] public float lowNeedThreshold = 0.2f;
        [Tooltip("Health lost per second while hunger or thirst is empty (each).")]
        [Range(0f, 20f)] public float emptyNeedHealthDrainPerSecond = 0.6f;
        [Tooltip("Health regen per second while both needs are above the regen threshold.")]
        [Range(0f, 20f)] public float healthRegenPerSecond = 0.35f;
        [Range(0f, 1f)] public float regenNeedThreshold = 0.5f;
        [Range(0f, 60f)] public float regenDelayAfterDamage = 8f;

        public void ApplyPreset(int preset)
        {
            switch (preset)
            {
                case 0: // Relaxed
                    hungerPerMinute = 0.6f; thirstPerMinute = 0.9f; needsRateMultiplier = 1f; sprintDrainPerSecond = 8f; staminaRecoveryPerSecond = 20f; break;
                case 2: // Extreme stress test: needs empty in ~1 minute
                    hungerPerMinute = 1.1f; thirstPerMinute = 1.6f; needsRateMultiplier = 50f; sprintDrainPerSecond = 40f; staminaRecoveryPerSecond = 6f; break;
                default:
                    hungerPerMinute = 1.1f; thirstPerMinute = 1.6f; needsRateMultiplier = 1f; sprintDrainPerSecond = 12f; staminaRecoveryPerSecond = 16f; break;
            }
        }

        [ContextMenu("Preset/Relaxed")] private void PresetRelaxed() => ApplyPreset(0);
        [ContextMenu("Preset/Default")] private void PresetDefault() => ApplyPreset(1);
        [ContextMenu("Preset/Extreme (stress test)")] private void PresetExtreme() => ApplyPreset(2);
    }
}
