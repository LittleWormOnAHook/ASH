using ASH.World.Weather;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>Warmth tuning (Resources/ASH_TemperatureSettings). Warmth is an abstract scale shown as degrees: 30 = normal /
    /// comfortable outside; it drops the longer you're outdoors and at the cold threshold (6) the cold effects start.
    /// Rates are warmth points per GAME hour (ASHDayNightClock; 1 game hour = 2 real minutes with the default 48 min day).</summary>
    [CreateAssetMenu(menuName = "ASH/Survival/Temperature Settings", fileName = "ASH_TemperatureSettings")]
    public sealed class ASHTemperatureSettings : ScriptableObject
    {
        [Header("Warmth scale (shown as degrees)")]
        [Tooltip("Normal / comfortable warmth: a new game starts here, shelters and fires bring you back to it.")]
        [Range(5f, 100f)] public float normalWarmth = 30f;
        [Range(-30f, 10f)] public float minWarmth = 0f;
        [Tooltip("At or below this: the meter pulses, frost vignette, health drain, cold breath.")]
        [Range(-10f, 60f)] public float coldWarmth = 6f;

        [Header("Outdoor drop (warmth per game hour)")]
        [Tooltip("Base drop outdoors in calm clear daytime. 30 -> 6 takes (30-6)/rate game hours (default 2 = 12 game h = 24 real min).")]
        [Range(0f, 60f)] public float outdoorDropPerGameHour = 2.5f;
        [Tooltip("Drop multiplier at the coldest hour of the night (blends from 1 at the warmest hour).")]
        [Range(0.2f, 6f)] public float nightMultiplier = 1.6f;
        [Range(0f, 23f)] public float warmestHour = 15f;
        [Header("Weather multipliers (outdoor drop)")]
        [Range(0f, 6f)] public float clearMul = 1f;
        [Range(0f, 6f)] public float calmHazyMul = 1.1f;
        [Range(0f, 6f)] public float lightAshMul = 1.2f;
        [Range(0f, 6f)] public float heavyAshMul = 1.5f;
        [Range(0f, 6f)] public float rainAshMul = 1.6f;
        [Range(0f, 6f)] public float snowstormMul = 2.2f;
        [Range(0f, 6f)] public float thunderstormMul = 1.5f;
        [Range(0f, 6f)] public float lightSnowMorningMul = 1.3f;
        [Tooltip("Wind: extra drop multiplier per m/s (outdoors), capped.")]
        [Range(0f, 0.5f)] public float windPerMS = 0.05f;
        [Range(0f, 5f)] public float maxWindBonus = 1f;
        [Tooltip("How fast the weather/night multiplier follows changes (per game hour).")]
        [Range(0.1f, 50f)] public float multiplierBlendPerGameHour = 4f;

        [Header("Shelter (ASHShelterZone sealing)")]
        [Tooltip("Inside, the drop is multiplied by (1 - sealing). At or above this sealing warmth also recovers.")]
        [Range(0f, 1f)] public float shelterWarmMinSealing = 0.5f;
        [Tooltip("Shelter recovery (warmth per game hour, x sealing).")]
        [Range(0f, 60f)] public float shelterRecoverPerGameHour = 4f;
        [Tooltip("Shelter alone recovers you up to this (fires go to normal).")]
        [Range(0f, 100f)] public float shelterRecoverTarget = 30f;

        [Header("Heat source presets (ASHHeatSource with a preset reads these live)")]
        [Tooltip("Lantern / torch placed in the world: warms you up to this cap while you stand near it.")]
        [Range(0f, 150f)] public float lanternWarmTarget = 52f;
        [Range(0f, 200f)] public float lanternWarmPerGameHour = 16f;
        [Range(0f, 150f)] public float torchWarmTarget = 52f;
        [Range(0f, 200f)] public float torchWarmPerGameHour = 16f;
        [Tooltip("Stove / fireplace / campfire: warms you up to this cap.")]
        [Range(0f, 150f)] public float stoveWarmTarget = 75f;
        [Range(0f, 200f)] public float stoveWarmPerGameHour = 30f;
        [Range(0f, 150f)] public float fireplaceWarmTarget = 75f;
        [Range(0f, 200f)] public float fireplaceWarmPerGameHour = 34f;
        [Range(0f, 150f)] public float campfireWarmTarget = 75f;
        [Range(0f, 200f)] public float campfireWarmPerGameHour = 38f;
        [Tooltip("Above normal warmth with no heat source holding you there, warmth sheds back toward normal this fast (per game hour, on top of the outdoor drop).")]
        [Range(0f, 200f)] public float excessWarmthLossPerGameHour = 8f;
        [Tooltip("Highest warmth a save can restore / the scale can reach.")]
        [Range(30f, 200f)] public float maxWarmth = 100f;

        [Header("Held lights (torch / lantern in hand or on the hip, lit)")]
        [Range(0f, 0.95f)] public float heldTorchCoolingReduction = 0.4f;
        [Range(0f, 0.95f)] public float heldLanternCoolingReduction = 0.3f;
        [Tooltip("Held lights can also warm you (per game hour) up to heldLightWarmTarget. 0 = held lights only slow the drop (so carrying a torch doesn't remove the cold).")]
        [Range(0f, 200f)] public float heldLightWarmPerGameHour = 0f;
        [Range(0f, 150f)] public float heldLightWarmTarget = 52f;

        [Header("Cold effects (at or below the cold threshold)")]
        [Tooltip("Health per game hour at the threshold...")]
        [Range(0f, 200f)] public float healthDrainPerGameHour = 8f;
        [Tooltip("...plus this much per warmth point below it.")]
        [Range(0f, 50f)] public float healthDrainPerPointBelow = 1.5f;
        [Tooltip("Stamina recovery x (1 - this) at the minimum warmth (scaled from the threshold).")]
        [Range(0f, 1f)] public float staminaRecoveryPenalty = 0.3f;
        public string freezingToast = "You're freezing — find warmth";
        [Header("Frost vignette")]
        [Tooltip("Frost starts at this warmth (default = cold threshold)...")]
        [Range(-10f, 60f)] public float frostStartWarmth = 6f;
        [Tooltip("...and is full at this warmth.")]
        [Range(-30f, 50f)] public float frostFullWarmth = 0f;
        [Range(0f, 1f)] public float frostMaxAlpha = 0.8f;
        [Range(40f, 600f)] public float frostEdgeWidth = 260f;

        [Header("Breath fog (visible cold breath, tied to warmth)")]
        [Header("Breathing pace (cold breath + the breathing under the rag / mask)")]
        [Tooltip("Breath intervals x this while standing still (2 = half as many breaths). Sprinting, running at or above Run Speed, and the rag / filter warning window use the normal pace (x1). Low-stamina heavy breaths and the strained warning breaths are unaffected.")]
        [Range(1f, 5f)] public float idleBreathIntervalMultiplier = 2f;
        [Tooltip("Breath intervals x this while walking (between idle and running).")]
        [Range(1f, 5f)] public float walkBreathIntervalMultiplier = 1.4f;
        [Tooltip("Below this ground speed (m/s) the player counts as idle.")]
        [Range(0f, 2f)] public float idleSpeed = 0.3f;
        [Tooltip("At or above this ground speed (m/s) breathing runs at the normal pace.")]
        [Range(0.5f, 10f)] public float runSpeed = 3.5f;
        public bool breathFog = true;
        [Tooltip("Cold breath shows at or below this warmth (default = cold threshold).")]
        [Range(-10f, 60f)] public float breathFogWarmth = 6f;
        [Tooltip("Densest breath at or below this warmth.")]
        [Range(-30f, 50f)] public float breathFogFullWarmth = 0f;
        [Tooltip("Occasional cold breath: seconds between visible breaths (+ exhale) at rest / exhausted or sprinting (real seconds).")]
        [Range(0.5f, 40f)] public float coldBreathEveryRest = 8f;
        [Range(0.3f, 30f)] public float coldBreathEveryExerted = 4.5f;
        [Range(0f, 0.6f)] public float coldBreathJitter = 0.3f;
        [Tooltip("Particles per breath at the threshold / at the densest.")]
        [Range(1, 80)] public int breathPuffCountMin = 6;
        [Range(1, 160)] public int breathPuffCountMax = 10;
        [Range(0.2f, 8f)] public float breathPuffLifetime = 2f;
        public Vector2 breathPuffSize = new Vector2(0.16f, 0.42f);
        [Tooltip("Initial forward speed (m/s). The puff then slows and floats up.")]
        [Range(0f, 3f)] public float breathPuffSpeed = 0.38f;
        [Tooltip("Upward drift (m/s) as the puff slows and dissipates.")]
        [Range(0f, 2f)] public float breathPuffRise = 0.2f;
        [Tooltip("How quickly the forward speed is damped (0-1).")]
        [Range(0f, 1f)] public float breathPuffDampen = 0.55f;
        [Tooltip("Size multiplier at the end of the puff's life (grows as it dissipates).")]
        [Range(0.5f, 6f)] public float breathPuffGrow = 2.2f;
        [Tooltip("Puff opacity (0-1). Keep this low so the breath does not glow at night.")]
        [Range(0f, 1f)] public float breathPuffOpacity = 0.3f;
        [Tooltip("Lowest density right at the threshold (0-1).")]
        [Range(0f, 1f)] public float breathMinDensity = 0.7f;
        public Color breathPuffColor = new Color(0.78f, 0.81f, 0.86f, 1f);
        [Tooltip("Colour multiplier. Keep at or below 1 so the puff is not an unlit glow in the dark.")]
        [Range(0.1f, 3f)] public float breathPuffBrightness = 0.7f;
        [Tooltip("Soft cloud / smoke texture (vendor texture by reference is fine). Empty = a procedural wispy puff.")]
        public Texture2D breathPuffTexture;
        [Tooltip("Mouth position from the Head bone: x right, y up, z forward (player facing).")]
        public Vector3 mouthOffset = new Vector3(0f, -0.02f, 0.13f);
        [Tooltip("Breath fog multiplier while the wet rag is worn (0 = hidden).")]
        [Range(0f, 1f)] public float ragFogMultiplier = 0.35f;
        [Tooltip("Breath fog multiplier while the gas mask is worn (0 = hidden).")]
        [Range(0f, 1f)] public float maskFogMultiplier = 0f;

        [Header("Exhale sound (one per puff)")]
        public AudioClip[] exhaleClips;
        [Range(0f, 1f)] public float exhaleVolume = 0.16f;
        [Tooltip("Volume when exhausted / sprinting.")]
        [Range(0f, 1f)] public float exhaleExhaustedVolume = 0.32f;
        [Range(0f, 0.5f)] public float exhaleVolumeJitter = 0.25f;
        [Range(0f, 0.4f)] public float exhalePitchJitter = 0.08f;
        [Tooltip("Pitch when exhausted (heavier, lower).")]
        [Range(0.5f, 1.5f)] public float exhaleExhaustedPitch = 0.92f;
        [Range(200f, 22000f)] public float exhaleMaskMuffleHz = 900f;
        [Range(200f, 22000f)] public float exhaleRagMuffleHz = 2600f;

        [Header("Heavy breathing (any temperature)")]
        [Tooltip("Heavy breaths play while stamina is at or below this (stamina points).")]
        [Range(0f, 100f)] public float heavyBreathStamina = 10f;
        [Tooltip("Real seconds between heavy breaths while stamina is low.")]
        public Vector2 heavyBreathEverySeconds = new Vector2(6f, 12f);
        [Range(0f, 1f)] public float heavyBreathVolume = 0.34f;
        [Tooltip("One recovery breath when you stop sprinting.")]
        public bool breathOnSprintStop = true;
        [Tooltip("Only after sprinting at least this long (real seconds).")]
        [Range(0f, 10f)] public float sprintStopMinSeconds = 1.5f;
        [Range(0f, 1f)] public float sprintStopVolume = 0.3f;
        [Tooltip("Clips for heavy / recovery breaths (empty = the exhale clips).")]
        public AudioClip[] heavyBreathClips;

        [Header("HUD")]
        [Tooltip("Trend arrow shows when warmth changes faster than this (points per game hour).")]
        [Range(0f, 5f)] public float warmthTrendDeadzone = 0.2f;
        [Tooltip("Meter pulse speed at/below the cold threshold.")]
        [Range(0.5f, 12f)] public float pulseSpeed = 6f;

        public float WeatherMultiplier(ASHWeatherKind k)
        {
            switch (k)
            {
                case ASHWeatherKind.Clear: return clearMul;
                case ASHWeatherKind.CalmHazy: return calmHazyMul;
                case ASHWeatherKind.LightAsh: return lightAshMul;
                case ASHWeatherKind.HeavyAsh: return heavyAshMul;
                case ASHWeatherKind.RainAsh: return rainAshMul;
                case ASHWeatherKind.Snowstorm: return snowstormMul;
                case ASHWeatherKind.Thunderstorm: return thunderstormMul;
                case ASHWeatherKind.LightSnowMorning: return lightSnowMorningMul;
            }
            return 1f;
        }

        public string Format(float w) => $"{w:0}°";
        static ASHTemperatureSettings _i;
        public static ASHTemperatureSettings I { get { if (!_i) { _i = Resources.Load<ASHTemperatureSettings>("ASH_TemperatureSettings"); if (!_i) _i = CreateInstance<ASHTemperatureSettings>(); } return _i; } }
    }
}
