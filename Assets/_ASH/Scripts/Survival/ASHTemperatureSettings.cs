using ASH.World.Weather;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>Body temperature tuning (Resources/ASH_TemperatureSettings). Temperatures in °C, rates per GAME hour
    /// (ASHDayNightClock; 1 game hour = 2 real minutes with the default 48 min day).</summary>
    [CreateAssetMenu(menuName = "ASH/Survival/Temperature Settings", fileName = "ASH_TemperatureSettings")]
    public sealed class ASHTemperatureSettings : ScriptableObject
    {
        [Header("Ambient (°C)")]
        [Tooltip("Warmest air of the day (around 15:00), clear weather, no wind.")]
        [Range(-40f, 40f)] public float dayTemperature = 4f;
        [Tooltip("Coldest air of the night (around 03:00).")]
        [Range(-50f, 30f)] public float nightTemperature = -8f;
        [Range(0f, 23f)] public float warmestHour = 15f;
        [Header("Weather offsets (°C added to ambient)")]
        [Range(-30f, 10f)] public float clear = 0f;
        [Range(-30f, 10f)] public float calmHazy = -1f;
        [Range(-30f, 10f)] public float lightAsh = -2f;
        [Range(-30f, 10f)] public float heavyAsh = -4f;
        [Range(-30f, 10f)] public float rainAsh = -5f;
        [Range(-30f, 10f)] public float snowstorm = -10f;
        [Range(-30f, 10f)] public float thunderstorm = -4f;
        [Range(-30f, 10f)] public float lightSnowMorning = -3f;
        [Tooltip("Wind chill: °C colder per m/s of wind (outdoors only).")]
        [Range(0f, 3f)] public float windChillPerMS = 0.4f;
        [Range(0f, 40f)] public float maxWindChill = 10f;
        [Tooltip("How fast the air changes when weather/time change (°C per game hour).")]
        [Range(0.5f, 100f)] public float ambientBlendPerGameHour = 8f;

        [Header("Body (°C)")]
        [Range(30f, 40f)] public float normalBody = 37f;
        [Range(15f, 35f)] public float minBody = 25f;
        [Tooltip("No cooling at or above this ambient temperature.")]
        [Range(-10f, 35f)] public float comfortAmbient = 15f;
        [Tooltip("Body °C lost per game hour, per °C of ambient below comfort (outdoors, no heat).")]
        [Range(0f, 1f)] public float coolingPerDegree = 0.035f;
        [Tooltip("Above comfort the body recovers towards normal on its own (°C per game hour).")]
        [Range(0f, 10f)] public float passiveWarmPerGameHour = 0.5f;

        [Header("Shelter (ASHShelterZone sealing)")]
        [Tooltip("Inside, cooling is multiplied by (1 - sealing). At or above this sealing the body also warms slowly.")]
        [Range(0f, 1f)] public float shelterWarmMinSealing = 0.5f;
        [Range(0f, 10f)] public float shelterWarmPerGameHour = 1f;
        [Tooltip("Shelter alone warms you up to this (fires go to normal).")]
        [Range(30f, 40f)] public float shelterWarmTarget = 36.5f;

        [Header("Held lights (torch / lantern in hand or on the hip, lit)")]
        [Range(0f, 0.95f)] public float heldTorchCoolingReduction = 0.4f;
        [Range(0f, 0.95f)] public float heldLanternCoolingReduction = 0.3f;

        [Header("Cold effects")]
        [Tooltip("Below this body temperature health drains.")]
        [Range(30f, 37f)] public float coldThreshold = 35f;
        [Tooltip("Health per game hour, per °C below the cold threshold.")]
        [Range(0f, 100f)] public float healthDrainPerDegree = 6f;
        [Tooltip("Stamina recovery x (1 - this) at 3 °C below the threshold (scaled).")]
        [Range(0f, 1f)] public float staminaRecoveryPenalty = 0.3f;
        public string freezingToast = "You're freezing — find warmth";
        [Header("Frost vignette")]
        [Range(28f, 37f)] public float frostStart = 34.5f;
        [Range(20f, 36f)] public float frostFull = 31.5f;
        [Range(0f, 1f)] public float frostMaxAlpha = 0.8f;
        [Range(40f, 600f)] public float frostEdgeWidth = 260f;

        [Header("Breath fog (visible breath when the air is cold)")]
        public bool breathFog = true;
        [Tooltip("Breath shows below this air temperature (°C).")]
        [Range(-30f, 20f)] public float breathFogAmbient = 5f;
        [Tooltip("Densest breath at or below this air temperature.")]
        [Range(-50f, 10f)] public float breathFogFullAmbient = -15f;
        [Tooltip("Air temperature used inside a fully sealed shelter (blended by sealing).")]
        [Range(-10f, 30f)] public float shelterAmbient = 10f;
        [Tooltip("Seconds between breaths at rest / exhausted or sprinting (real seconds).")]
        [Range(0.5f, 10f)] public float breathIntervalRest = 3.6f;
        [Range(0.3f, 5f)] public float breathIntervalExerted = 1.3f;
        [Range(0f, 0.3f)] public float breathIntervalJitter = 0.12f;
        [Range(1, 30)] public int puffParticlesMin = 2;
        [Range(1, 60)] public int puffParticlesMax = 7;
        [Range(0.2f, 5f)] public float puffLifetime = 1.6f;
        public Vector2 puffSize = new Vector2(0.08f, 0.32f);
        [Range(0f, 3f)] public float puffSpeed = 0.35f;
        [Range(0f, 1f)] public float puffAlpha = 0.32f;
        public Color puffColor = new Color(0.92f, 0.94f, 0.97f, 1f);
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

        [Header("HUD")]
        public bool displayFahrenheit = true;
        [Tooltip("Trend arrow shows when body temperature changes faster than this (°C per game hour).")]
        [Range(0f, 2f)] public float trendDeadzone = 0.05f;

        public float WeatherOffset(ASHWeatherKind k)
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

        public string Format(float c) => displayFahrenheit ? $"{c * 9f / 5f + 32f:0.0}°F" : $"{c:0.0}°C";

        static ASHTemperatureSettings _i;
        public static ASHTemperatureSettings I { get { if (!_i) { _i = Resources.Load<ASHTemperatureSettings>("ASH_TemperatureSettings"); if (!_i) _i = CreateInstance<ASHTemperatureSettings>(); } return _i; } }
    }
}
