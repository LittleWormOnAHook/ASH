using UnityEngine;
using UnityEngine.Serialization;

namespace ASH.Rendering
{
    /// <summary>
    /// Tuning for the INab Better Fog layer (ASH_Fog). Lives in Resources/ASH_BetterFogSettings.asset so edits save in Edit mode.
    /// Two looks: "Clear (min)" = light clear-day haze, "Ash Storm (max)" = full ashfall storm with strong wind.
    /// ASHBetterFogDriver blends between them by weather intensity = airborne ash x wind factor (also snow x wind, and low
    /// visibility). Snow pulls the fog colour toward a lighter snow tint. Night and indoor handling sit on top.
    /// All ranges are wide on purpose.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Rendering/Better Fog Settings", fileName = "ASH_BetterFogSettings")]
    public sealed class ASHBetterFogSettings : ScriptableObject
    {
        public enum Preview { Clear, AshStormMax, Snowstorm, Custom }

        static ASHBetterFogSettings _i;
        /// <summary>Raised when the asset is edited in the Inspector (Edit or Play); ASHBetterFogDriver snaps to the new values.</summary>
        public static event System.Action Changed;
        public static ASHBetterFogSettings I { get { if (!_i) { _i = Resources.Load<ASHBetterFogSettings>("ASH_BetterFogSettings"); if (!_i) _i = CreateInstance<ASHBetterFogSettings>(); } return _i; } }

        [Header("Master")]
        [Tooltip("Fog strength while indoors (cabin / shelters), as a fraction of the outdoor value.")] [Range(0f, 1f)] public float indoorIntensity = 0.05f;
        [Tooltip("How fast the fog follows its target (per second). Weather is already blended; this smooths the rest.")] [Range(0.01f, 20f)] public float blendPerSecond = 0.6f;
        [Tooltip("Turn Unity scene fog (RenderSettings.fog) off while ASH_Fog runs so the two don't stack. Restored when ASH_Fog is disabled.")]
        public bool replaceSceneFog = true;
        [Tooltip("Switch the old ASH Volumetric Fog pass off while ASH_Fog runs so the two don't stack. Restored when ASH_Fog is disabled.")]
        public bool disableOldVolumetricFog = true;
        [Tooltip("On: weather/time of day drive density, height, colour, sun, sky fill and noise. Off: your Inspector values on ASH_Fog's profile win (edit them in Play, they are saved back on Stop).")]
        public bool driveFromWeather = true;
        [Tooltip("On Stop (and via ASH/Systems/Weather/Save Fog Profile from Play), write Play-mode edits of ASH_Fog's profile back to ASH_BetterFogProfile. Driven fields are skipped while driveFromWeather is on.")]
        public bool saveProfileOnStop = true;

        [Header("Edit-mode preview (written to a temporary profile, nothing is saved)")]
        [Tooltip("Which weather the Scene/Game view shows in Edit mode. Custom = the sliders below.")] public Preview preview = Preview.Clear;
        [Tooltip("Game hour shown in Edit mode.")] [Range(0f, 24f)] public float previewHour = 12f;
        [Tooltip("Custom preview: airborne ash 0-1.")] [Range(0f, 1f)] public float previewAsh = 0.5f;
        [Tooltip("Custom preview: snow 0-1.")] [Range(0f, 1f)] public float previewSnow = 0f;
        [Tooltip("Custom preview: wind m/s.")] [Range(0f, 40f)] public float previewWind = 6f;
        [Tooltip("Custom preview: weather visibility (m).")] [Range(10f, 10000f)] public float previewVisibility = 8000f;
        [Tooltip("Custom preview: cloud darkness 0-1.")] [Range(0f, 1f)] public float previewCloudDarkness = 0.3f;

        [Header("Weather intensity (0 = Clear look, 1 = Ash Storm look)")]
        [Tooltip("Wind (m/s) at or below which ash/snow only count at 'Wind Min Factor'.")] [Range(0f, 40f)] public float calmWind = 1f;
        [Tooltip("Wind (m/s) at which ash/snow count fully (Heavy Ash ~8-14 m/s with gusts).")] [Range(0.1f, 60f)] public float stormWind = 12f;
        [Tooltip("How much ash/snow counts with no wind (0 = wind required, 1 = wind ignored).")] [Range(0f, 1f)] public float windMinFactor = 0.6f;
        [Tooltip("How much a snowstorm counts toward the storm look (snow x wind x this).")] [Range(0f, 2f)] public float snowStormStrength = 0.9f;
        [Tooltip("Low visibility alone (rain, haze, thunder) also thickens fog: Clear visibility -> 0, Storm visibility -> this weight.")] [Range(0f, 1f)] public float visibilityWeight = 0.7f;
        [Range(10f, 20000f)] public float clearVisibility = 8000f;
        [Range(1f, 5000f)] public float stormVisibility = 90f;
        [Tooltip("Shapes the blend: 1 = linear, >1 = stays light longer then ramps, <1 = thickens early.")] [Range(0.2f, 5f)] public float intensityCurve = 1f;

        [Header("Clear (min) - light clear-day look")]
        [FormerlySerializedAs("intensity")] [Tooltip("Better Fog 'Fog Intensity' on a clear day.")] [Range(0f, 1f)] public float clearIntensity = 0.439f;
        [FormerlySerializedAs("minDensity")] [Tooltip("0.0015: under 1% fog at 50 m, ~50% at 450 m. Better Fog density, 0-0.1.")] [Range(0f, 0.1f)] public float clearDensity = 0.0015f;
        [FormerlySerializedAs("heightDensityClear")] [Range(0f, 0.5f)] public float clearHeightDensity = 0.12f;
        [FormerlySerializedAs("skyFillClear")] [Range(0f, 1f)] public float clearSkyFill = 0f;
        [FormerlySerializedAs("noiseClear")] [Range(0f, 1f)] public float clearNoise = 0.15f;
        [FormerlySerializedAs("sunIntensity")] [Range(0f, 1f)] public float clearSunIntensity = 0.35f;

        [Header("Ash Storm (max) - full ashfall + strong wind (seeded from your 20 Nov look)")]
        [Range(0f, 1f)] public float stormIntensity = 0.439f;
        [Range(0f, 0.1f)] public float stormDensity = 0.0244f;
        [FormerlySerializedAs("heightDensityAsh")] [Range(0f, 0.5f)] public float stormHeightDensity = 0.3f;
        [FormerlySerializedAs("ashExtraHeight")] [Tooltip("Extra fog height at full storm (the ash layer rises), m.")] [Range(0f, 300f)] public float stormExtraHeight = 30f;
        [Range(0f, 1f)] public float stormSkyFill = 0.666f;
        [Range(0f, 1f)] public float stormNoise = 0.377f;
        [Range(0f, 1f)] public float stormSunIntensity = 0.035f;

        [Header("Snow / snowstorm")]
        [Tooltip("Lighter fog colour the ash tint moves toward in snow / snowstorms.")] public Color snowStormTint = new Color(0.86f, 0.88f, 0.92f);
        [Tooltip("How far full snow pulls the colour to Snow Storm Tint (scaled by snow amount).")] [Range(0f, 1f)] public float snowTintBlend = 0.8f;
        [Tooltip("Extra height at full snow.")] [Range(0f, 300f)] public float snowExtraHeight = 12f;
        [Range(0f, 0.5f)] public float heightDensitySnow = 0.25f;

        [Header("Other weather / night")]
        [Range(0f, 0.5f)] public float heightDensityRain = 0.22f;
        [Tooltip("Distance density at night (blended over dawn/dusk). Lower = see further at night.")] [Range(0f, 3f)] public float nightDensityMultiplier = 1.25f;
        [Tooltip("Distance before fog starts (m, Better Fog 'Distance Fog Offset').")] [Range(-200f, 200f)] public float startOffset = 0f;

        [Header("Height fog (valley mist / ash layer)")]
        public bool heightFog = true;
        [Tooltip("Fog top above the ground under the camera (m).")] [Range(-50f, 300f)] public float heightAboveGround = 6f;
        [Tooltip("Morning valley mist added to height density around dawn (game hours below).")] [Range(0f, 0.5f)] public float morningMist = 0.08f;
        public Vector2 morningMistHours = new Vector2(4.5f, 9.5f);
        [Range(0f, 5f)] public float heightDensityMultiplier = 1f;
        [Tooltip("How fast the fog height follows the ground under the camera (per second).")] [Range(0.01f, 10f)] public float groundFollow = 0.5f;

        [Header("Colour (day / night palette, warmed by the clock's time-of-day hue, tinted by weather)")]
        public Color dayColor = new Color(0.82f, 0.83f, 0.86f);
        public Color nightColor = new Color(0.13f, 0.15f, 0.20f);
        [Tooltip("Game hours: fog goes from night to day colour over dawn, and back over dusk.")] public Vector2 dawnHours = new Vector2(5f, 8f);
        public Vector2 duskHours = new Vector2(17.5f, 20.5f);
        [Tooltip("How much of the clock's time-of-day fog hue (warm dawn/dusk) is mixed in (hue only, not brightness).")] [Range(0f, 1f)] public float timeOfDayHue = 0.35f;
        [Tooltip("Ash storm tint (applied by ash amount x Tint Strength).")] public Color ashTint = new Color(0.72f, 0.68f, 0.63f);
        [Tooltip("Hue tint for light snow (the lighter Snow Storm Tint is applied on top).")] public Color snowTint = new Color(0.86f, 0.89f, 0.95f);
        public Color rainTint = new Color(0.62f, 0.65f, 0.70f);
        [Tooltip("How far weather tints pull the hue (brightness stays with the palette).")] [Range(0f, 1f)] public float tintStrength = 0.6f;
        [Tooltip("Overall fog brightness. Raise to lighten, lower to darken.")] [Range(0f, 3f)] public float colorMultiplier = 1f;
        [Tooltip("Darkening per unit of cloud darkness (heavy ash ~0.8).")] [Range(0f, 1f)] public float cloudDarken = 0.15f;
        [Tooltip("Fog never gets darker than this in full day (0-1 brightness).")] [Range(0f, 1f)] public float minBrightnessDay = 0.35f;
        [Tooltip("Fog never gets darker than this at night.")] [Range(0f, 1f)] public float minBrightnessNight = 0.12f;
        [Tooltip("Lightning flash brightens the fog.")] [Range(0f, 5f)] public float flashBoost = 1.5f;

        [Header("Sun scattering")]
        public bool sunScatter = true;
        [Range(0.1f, 12f)] public float sunPower = 4f;

        [Header("Sky")]
        public bool skyboxFog = true;
        [Range(0f, 0.999f)] public float skyHardness = 0.75f;

        [Header("Noise (drifting fog)")]
        public bool noise = true;
        [Tooltip("Noise drift per m/s of wind.")] [Range(0f, 2f)] public float noiseWindSpeed = 0.15f;
        [Range(5f, 140f)] public float noiseScale = 45f;
        [Range(1f, 2000f)] public float noiseDistanceEnd = 120f;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this); // Play-mode edits save too
            UnityEditor.EditorApplication.delayCall += () => Changed?.Invoke();
        }
#endif
    }
}
