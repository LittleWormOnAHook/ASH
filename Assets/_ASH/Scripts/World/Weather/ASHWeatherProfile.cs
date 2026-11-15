using UnityEngine;

namespace ASH.World.Weather
{
    public enum ASHWeatherKind { Clear, CalmHazy, LightAsh, HeavyAsh, RainAsh, Snowstorm, Thunderstorm }

    /// <summary>
    /// One weather state as a set of TARGET values for systems that already exist: ASHAshFall (ash/rain/snow particle
    /// volumes), ASHDayNightClock (sun, ambient, haze), the scene fog and sky, the post stack (a runtime weather Volume
    /// blended by weight over the scene volumes - the profile assets are never edited), the ASH Volumetric Fog override,
    /// ASHAccumulationController (ground layers) and the ambience loops. ASHWeatherDirector blends between profiles.
    /// "Clear" with Atmosphere Weight 0 means: the scene exactly as tuned by hand.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Weather Profile", fileName = "ASH_Weather_")]
    public sealed class ASHWeatherProfile : ScriptableObject
    {
        public ASHWeatherKind kind = ASHWeatherKind.Clear;
        public string displayName = "Clear";

        [Header("Precipitation (0-1)")]
        [Range(0f, 1f)] public float ashfall;
        [Range(0f, 1f)] public float rain;
        [Range(0f, 1f)] public float snow;
        [Tooltip("Flake size multiplier for falling ash (fine dust at the start, coarse flakes in heavy fallout).")]
        [Range(0.2f, 4f)] public float ashFlakeSize = 1f;

        [Header("Wind")]
        [Range(0f, 1f)] public float wind = 0.2f;
        [Tooltip("Wind speed at Wind = 1 (m/s): drives particle drift, haze drift and accumulation redistribution.")]
        [Range(0f, 40f)] public float windSpeed = 8f;
        [Tooltip("Gust variation 0-1.")] [Range(0f, 1f)] public float gustiness = 0.3f;

        [Header("Sky and light")]
        [Tooltip("0 = scene sun as tuned, 1 = sun almost gone.")] [Range(0f, 1f)] public float cloudDarkness;
        [Range(0f, 2f)] public float ambientScale = 1f;
        [Range(0f, 2f)] public float skyExposureScale = 1f;
        [Tooltip("Sky tint toward grey/brown ash (0 = sky material as tuned).")] [Range(0f, 1f)] public float skyAshTint;
        [Tooltip("Minimum ash haze fed to ASHDayNightClock (fog/ambient tint). The clock's own value wins when higher.")]
        [Range(0f, 1f)] public float haze;

        [Header("Visibility")]
        [Tooltip("Distance (m) at which the scene fog is near-opaque. Clamped by the director's visibility limits; the scene fog never gets thinner than tuned.")]
        [Range(10f, 10000f)] public float visibilityMetres = 6000f;
        [Tooltip("ASH Volumetric Fog density multiplier in this weather (1 = as tuned).")]
        [Range(0f, 20f)] public float volFogDensity = 1f;
        public Color volFogAlbedo = new Color(0.72f, 0.68f, 0.64f);
        [Range(0f, 1f)] public float volFogNoise = 0.5f;
        [Range(0f, 32f)] public float volFogLight = 0.6f;

        [Header("Post (runtime weather Volume, blended over the scene volumes)")]
        [Tooltip("Weight of the weather volume: 0 = post exactly as tuned in the scene profiles.")]
        [Range(0f, 1f)] public float atmosphereWeight;
        [Range(-100f, 100f)] public float saturation;
        [Range(-5f, 5f)] public float postExposure;
        public Color colorFilter = Color.white;
        [Range(0f, 1f)] public float vignette = 0.2f;

        [Header("Audio (0-1)")]
        [Range(0f, 1f)] public float windVolume = 0.15f;
        [Range(0f, 1f)] public float rainVolume;
        [Tooltip("Birdsong / calm ambience.")] [Range(0f, 1f)] public float ambienceVolume = 0.5f;
        [Tooltip("Average thunder strikes per minute (lightning flash + delayed thunder). 0 = none.")]
        [Range(0f, 30f)] public float thunderPerMinute;

        [Header("Ground (accumulation inputs)")]
        [Range(-40f, 40f)] public float temperatureC = 6f;

        [Header("Scheduling")]
        [Tooltip("Relative chance of being picked by the scheduler (0 = never auto-picked).")]
        [Range(0f, 10f)] public float probability = 1f;
        [Tooltip("Major events are the waves; the scheduler puts a calm period between them.")]
        public bool majorEvent;
        [Tooltip("Minutes this state lasts once reached (random in range).")]
        public Vector2 durationMinutes = new Vector2(3f, 6f);

        public ASHWeatherParams ToParams() => ASHWeatherParams.From(this);
    }

    /// <summary>Blendable snapshot of a weather profile.</summary>
    [System.Serializable]
    public struct ASHWeatherParams
    {
        public float ashfall, rain, snow, ashFlakeSize, wind, windSpeed, gustiness, cloudDarkness, ambientScale, skyExposureScale, skyAshTint, haze;
        public float visibility, volFogDensity, volFogNoise, volFogLight, atmosphereWeight, saturation, postExposure, vignette;
        public float windVolume, rainVolume, ambienceVolume, thunderPerMinute, temperatureC;
        public Color volFogAlbedo, colorFilter;

        public static ASHWeatherParams From(ASHWeatherProfile p) => new ASHWeatherParams
        {
            ashfall = p.ashfall, rain = p.rain, snow = p.snow, ashFlakeSize = p.ashFlakeSize, wind = p.wind, windSpeed = p.windSpeed, gustiness = p.gustiness,
            cloudDarkness = p.cloudDarkness, ambientScale = p.ambientScale, skyExposureScale = p.skyExposureScale, skyAshTint = p.skyAshTint, haze = p.haze,
            visibility = p.visibilityMetres, volFogDensity = p.volFogDensity, volFogNoise = p.volFogNoise, volFogLight = p.volFogLight,
            atmosphereWeight = p.atmosphereWeight, saturation = p.saturation, postExposure = p.postExposure, vignette = p.vignette,
            windVolume = p.windVolume, rainVolume = p.rainVolume, ambienceVolume = p.ambienceVolume, thunderPerMinute = p.thunderPerMinute,
            temperatureC = p.temperatureC, volFogAlbedo = p.volFogAlbedo, colorFilter = p.colorFilter
        };

        public static ASHWeatherParams Lerp(in ASHWeatherParams a, in ASHWeatherParams b, float t)
        {
            float L(float x, float y) => x + (y - x) * t;
            return new ASHWeatherParams
            {
                ashfall = L(a.ashfall, b.ashfall), rain = L(a.rain, b.rain), snow = L(a.snow, b.snow), ashFlakeSize = L(a.ashFlakeSize, b.ashFlakeSize),
                wind = L(a.wind, b.wind), windSpeed = L(a.windSpeed, b.windSpeed), gustiness = L(a.gustiness, b.gustiness),
                cloudDarkness = L(a.cloudDarkness, b.cloudDarkness), ambientScale = L(a.ambientScale, b.ambientScale), skyExposureScale = L(a.skyExposureScale, b.skyExposureScale),
                skyAshTint = L(a.skyAshTint, b.skyAshTint), haze = L(a.haze, b.haze),
                // visibility blends in log space so a clear-to-fog change reads evenly
                visibility = Mathf.Exp(L(Mathf.Log(Mathf.Max(1f, a.visibility)), Mathf.Log(Mathf.Max(1f, b.visibility)))),
                volFogDensity = L(a.volFogDensity, b.volFogDensity), volFogNoise = L(a.volFogNoise, b.volFogNoise), volFogLight = L(a.volFogLight, b.volFogLight),
                atmosphereWeight = L(a.atmosphereWeight, b.atmosphereWeight), saturation = L(a.saturation, b.saturation), postExposure = L(a.postExposure, b.postExposure),
                vignette = L(a.vignette, b.vignette), windVolume = L(a.windVolume, b.windVolume), rainVolume = L(a.rainVolume, b.rainVolume),
                ambienceVolume = L(a.ambienceVolume, b.ambienceVolume), thunderPerMinute = L(a.thunderPerMinute, b.thunderPerMinute), temperatureC = L(a.temperatureC, b.temperatureC),
                volFogAlbedo = Color.Lerp(a.volFogAlbedo, b.volFogAlbedo, t), colorFilter = Color.Lerp(a.colorFilter, b.colorFilter, t)
            };
        }
    }
}
