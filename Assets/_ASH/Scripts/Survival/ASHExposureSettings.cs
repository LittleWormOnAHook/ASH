using System;
using System.Collections.Generic;
using ASH.World.Weather;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// Ash exposure, wet rag, gas mask, filter cartridges and the refillable bottle. Every duration is in GAME hours
    /// (ASHDayNightClock: with the default 48 min day, 1 game hour = 2 real minutes). Resources/ASH_ExposureSettings.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Survival/Exposure Settings", fileName = "ASH_ExposureSettings")]
    public sealed class ASHExposureSettings : ScriptableObject
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHExposureSettings()
        {
            _i = default;
        }

        [Serializable]
        public sealed class ConditionBand
        {
            public string label = "used";
            [Range(0f, 1f)] public float min = 0.3f;
            [Range(0f, 1f)] public float max = 0.8f;
            [Range(0f, 100f)] public float weight = 1f;
        }

        [Header("Air quality per weather (0 = clean air, 1 = full strength)")]
        [Range(0f, 2f)] public float clear = 0f;
        [Range(0f, 2f)] public float calmHazy = 0.1f;
        [Range(0f, 2f)] public float lightAsh = 0.4f;
        [Range(0f, 2f)] public float heavyAsh = 1f;
        [Range(0f, 2f)] public float rainAsh = 0.6f;
        [Range(0f, 2f)] public float snowstorm = 0.15f;
        [Range(0f, 2f)] public float thunderstorm = 0.5f;
        [Range(0f, 2f)] public float lightSnowMorning = 0f;
        [Tooltip("How fast the air changes when the weather turns (strength units per game hour).")]
        [Range(0.1f, 50f)] public float weatherBlendPerGameHour = 3f;

        [Header("Drain at strength 1, unprotected, outside (per GAME hour)")]
        [Range(0f, 200f)] public float healthDrainPerGameHour = 8f;
        [Tooltip("Extra stamina drain (stamina points per game hour at exposure 1).")]
        [Range(0f, 500f)] public float staminaDrainPerGameHour = 2f;
        [Tooltip("Extra thirst drain (thirst points, 0–100 bar) per game hour at exposure 1. Planned start is 0.5; raise it in Play if it feels invisible next to the 1.6/min baseline.")]
        [Range(0f, 100f)] public float thirstPerGameHour = 0.5f;
        [Tooltip("Stamina recovery is multiplied by (1 - this x exposure).")]
        [Range(0f, 1f)] public float staminaRecoveryPenalty = 0.45f;
        [Tooltip("Exposure below this is ignored (no drain, no icon).")]
        [Range(0f, 0.5f)] public float exposureDeadzone = 0.02f;

        [Header("Wet rag (UI only, no screen overlay)")]
        [Range(0f, 1f)] public float ragProtection = 0.5f;
        [Range(0.1f, 72f)] public float ragHours = 5f;
        [Tooltip("Drying-out window: full protection until this many game hours remain, then it fades to zero (\"Your rag is drying out\" warning, amber timer).")]
        [Range(0f, 24f)] public float ragDegradeHours = 1.75f;
        [Tooltip("Wet rag + 10% bottle water.")]
        [Range(0f, 100f)] public float rewetWaterPercent = 10f;

        [Header("Gas mask + filter cartridges")]
        [Range(0f, 1f)] public float maskProtection = 0.85f;
        [Tooltip("Mask worn with no usable filter.")]
        [Range(0f, 1f)] public float maskNoFilterProtection = 0.15f;
        [Tooltip("Life of a new filter (game hours). Found filters roll a condition = fraction of this.")]
        [Range(0.1f, 144f)] public float filterHours = 10f;
        [Tooltip("Protection fades over the last game hours of a filter.")]
        [Range(0f, 24f)] public float filterDegradeHours = 0.875f;
        [Tooltip("Filters wear only outside / in leaky shelters (sealed shelters don't use them).")]
        public bool filterWearsOnlyWhenExposed = true;
        [Tooltip("Low-filter warning + amber timer (game hours left).")]
        [Range(0f, 48f)] public float filterLowHours = 3.5f;
        [Tooltip("Filters below this condition are clogged: can't be fitted.")]
        [Range(0f, 0.5f)] public float filterUnusableBelow = 0.03f;
        [Tooltip("Found filter cartridges roll a condition from these bands (weighted).")]
        public List<ConditionBand> foundFilterCondition = new List<ConditionBand>
        {
            new ConditionBand { label = "new", min = 0.95f, max = 1f, weight = 1.5f },
            new ConditionBand { label = "used", min = 0.45f, max = 0.9f, weight = 4f },
            new ConditionBand { label = "nearly spent", min = 0.08f, max = 0.4f, weight = 3f },
            new ConditionBand { label = "clogged", min = 0f, max = 0.02f, weight = 1.5f },
        };

        [Header("Low health warning")]
        [Range(0f, 1f)] public float warnHealthFraction = 0.25f;
        [Range(0.1f, 6f)] public float edgePulseSpeed = 1.6f;
        [Range(0f, 1f)] public float edgeMaxAlpha = 0.55f;
        [Range(20f, 400f)] public float edgeWidth = 140f;
        public string warnToast = "Find shelter quickly";

        [Header("Sounds")]
        public AudioClip[] coughClips;
        public AudioClip breathingLoop;
        [Range(0f, 1f)] public float coughVolume = 0.7f;
        [Tooltip("Occasional coughing outdoors in ash with no rag/mask, above this exposure.")]
        [Range(0f, 2f)] public float coughExposure = 0.25f;
        [Tooltip("Real seconds between occasional coughs (random in range).")]
        public Vector2 coughIntervalSeconds = new Vector2(18f, 45f);
        [Tooltip("The wet rag's final stretch (game hours left) where it lets ash through: coughing even while worn.")]
        [Range(0f, 12f)] public float ragCoughHours = 0.5f;
        public Vector2 ragEndingCoughIntervalSeconds = new Vector2(8f, 18f);
        public Vector2 lowHealthCoughIntervalSeconds = new Vector2(4f, 9f);
        [Range(0f, 1f)] public float breathingVolume = 0.35f;
        [Range(200f, 8000f)] public float breathingMuffleHz = 1100f;

        [Header("Strained breathing (rag drying out / filter low)")]
        [Tooltip("Clip for the strained breathing cue (empty = the breathing loop clip).")]
        public AudioClip laboredClip;
        [Range(0f, 1f)] public float laboredVolume = 0.35f;
        [Tooltip("Seconds between strained breaths: x = when the warning starts, y = near empty (real seconds).")]
        public Vector2 laboredIntervalSeconds = new Vector2(14f, 4f);
        [Range(0f, 0.4f)] public float laboredPitchJitter = 0.06f;

        [Header("Water bottle")]
        [Range(1f, 100f)] public float sipPercent = 10f;
        [Range(0f, 100f)] public float thirstPerSip = 12f;
        [Range(0.2f, 6f)] public float refillHoldSeconds = 1.6f;

        [Header("HUD")]
        [Tooltip("Countdown (worn rag / filter) position: offset from bottom-centre, in 1920x1080 reference pixels. The hotbar's top is ~124.")]
        public Vector2 timerOffset = new Vector2(0f, 140f);
        [Tooltip("Timer turns red below this many game minutes.")]
        [Range(0f, 120f)] public float timerRedMinutes = 10f;
        [Tooltip("Status icons (exposure, protection, bottle): offset from bottom-left (the health/needs bars end at ~185).")]
        public Vector2 statusOffset = new Vector2(40f, 200f);

        public float Strength(ASHWeatherKind k)
        {
            switch (k)
            {
                case ASHWeatherKind.Clear: return clear;
                case ASHWeatherKind.CalmHazy: return calmHazy;
                case ASHWeatherKind.LightAsh: return lightAsh;
                case ASHWeatherKind.HeavyAsh: return heavyAsh;
                case ASHWeatherKind.RainAsh: return rainAsh;
                case ASHWeatherKind.Snowstorm: return snowstorm;
                case ASHWeatherKind.Thunderstorm: return thunderstorm;
                case ASHWeatherKind.LightSnowMorning: return lightSnowMorning;
            }
            return 0f;
        }

        public float RollFilterCondition()
        {
            float total = 0f; foreach (var b in foundFilterCondition) if (b != null) total += Mathf.Max(0f, b.weight);
            if (total <= 0f) return 1f;
            float r = UnityEngine.Random.value * total;
            foreach (var b in foundFilterCondition)
            {
                if (b == null) continue; r -= Mathf.Max(0f, b.weight);
                if (r <= 0f) return Mathf.Clamp01(UnityEngine.Random.Range(Mathf.Min(b.min, b.max), Mathf.Max(b.min, b.max)));
            }
            return 1f;
        }

        static ASHExposureSettings _i;
        public static ASHExposureSettings I { get { if (!_i) { _i = Resources.Load<ASHExposureSettings>("ASH_ExposureSettings"); if (!_i) _i = CreateInstance<ASHExposureSettings>(); } return _i; } }
    }
}
