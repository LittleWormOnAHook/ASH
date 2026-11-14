using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// All survival balancing in one asset. Rates are per real minute for readability. Wide ranges plus
    /// presets (Relaxed / Default / Extreme) for stress testing.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Survival/Survival Tuning", fileName = "ASH_SurvivalTuning")]
    public sealed class ASHSurvivalTuning : ScriptableObject
    {
        [Header("Maximums")]
        [Range(10f, 1000f)] public float maxHealth = 100f;
        [Range(10f, 1000f)] public float maxStamina = 100f;
        [Range(10f, 1000f)] public float maxHunger = 100f;
        [Range(10f, 1000f)] public float maxThirst = 100f;

        [Header("Start values (fraction of max)")]
        [Range(0f, 1f)] public float startHunger = 0.72f;
        [Range(0f, 1f)] public float startThirst = 0.64f;

        [Header("Needs decay (points per real minute)")]
        [Range(0f, 100f)] public float hungerPerMinute = 1.1f;
        [Range(0f, 100f)] public float thirstPerMinute = 1.6f;
        [Tooltip("Needs decay multiplier while sprinting.")]
        [Range(1f, 10f)] public float sprintNeedsMultiplier = 1.75f;
        [Tooltip("Global multiplier for stress tests (1 = normal).")]
        [Range(0.1f, 60f)] public float needsRateMultiplier = 1f;

        [Header("Stamina (drives the Invector motor)")]
        [Range(0f, 100f)] public float sprintDrainPerSecond = 12f;
        [Range(0f, 100f)] public float staminaRecoveryPerSecond = 16f;
        [Range(0f, 100f)] public float jumpStaminaCost = 12f;
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
