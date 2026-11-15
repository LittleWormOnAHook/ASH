using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.Rendering
{
    public enum ASHFogQuality { Low, Medium, High, Ultra, Custom }
    [Serializable] public sealed class ASHFogQualityParameter : VolumeParameter<ASHFogQuality> { public ASHFogQualityParameter(ASHFogQuality v, bool o = false) : base(v, o) { } }

    /// <summary>"ASH Volumetric Fog" volume override (Add Override > ASH > ASH Volumetric Fog on the Global Volume profile).
    /// Rendered by ASHVolumetricFogFeature on the URP renderer. Height fog: full density below Base Height, falling off
    /// exponentially above it; lit by the main light (Henyey-Greenstein) with optional shadows (light shafts) plus ambient.</summary>
    [Serializable, VolumeComponentMenu("ASH/ASH Volumetric Fog"), SupportedOnRenderPipeline(typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset))]
    public sealed class ASHVolumetricFog : VolumeComponent, IPostProcessComponent
    {
        [Header("General")]
        public BoolParameter enabled = new BoolParameter(false);
        [Tooltip("Low 16 steps half res, Medium 32 half, High 48 full, Ultra 96 full. Custom uses Steps / Half Resolution below.")]
        public ASHFogQualityParameter quality = new ASHFogQualityParameter(ASHFogQuality.Medium);
        public ClampedIntParameter steps = new ClampedIntParameter(32, 4, 256);
        public BoolParameter halfResolution = new BoolParameter(true);
        [Tooltip("Vary the ray start every frame (pairs well with TAA). Spatial noise jitter is always on.")]
        public BoolParameter temporalJitter = new BoolParameter(false);

        [Header("Density / height")]
        [Tooltip("Extinction per metre at and below Base Height.")] public ClampedFloatParameter density = new ClampedFloatParameter(0.004f, 0f, 1f);
        [Tooltip("World Y where the fog is densest; it thins out above.")] public FloatParameter baseHeight = new FloatParameter(0f);
        [Tooltip("How fast density falls off above Base Height (1/m).")] public ClampedFloatParameter heightFalloff = new ClampedFloatParameter(0.035f, 0f, 2f);
        [Tooltip("Ray length, metres.")] public ClampedFloatParameter maxDistance = new ClampedFloatParameter(300f, 5f, 5000f);

        [Header("Colour / lighting")]
        public ColorParameter albedo = new ColorParameter(new Color(0.72f, 0.68f, 0.64f), false, false, true);
        [Tooltip("Tint the albedo with the scene fog colour (driven by ASHDayNightClock) so it matches the distance fog.")]
        public BoolParameter useDayNightColor = new BoolParameter(true);
        public ColorParameter ambientTint = new ColorParameter(new Color(0.42f, 0.44f, 0.5f), true, false, true);
        [Tooltip("Multiplies Ambient Tint and the scene ambient colour.")] public ClampedFloatParameter ambientIntensity = new ClampedFloatParameter(1f, 0f, 16f);
        [Tooltip("Henyey-Greenstein g: >0 glows toward the sun, <0 back-scatters.")] public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.35f, -0.95f, 0.95f);
        public ClampedFloatParameter lightIntensity = new ClampedFloatParameter(0.6f, 0f, 32f);
        [Tooltip("Sample the main light shadow map along the ray (light shafts through trees).")] public BoolParameter shadows = new BoolParameter(true);

        [Header("Ash haze noise")]
        [Tooltip("Noise frequency (1/m). 0 strength disables the noise.")] public ClampedFloatParameter noiseScale = new ClampedFloatParameter(0.04f, 0.0005f, 2f);
        public ClampedFloatParameter noiseStrength = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("How fast the haze churns (vertical evolution).")] public ClampedFloatParameter noiseSpeed = new ClampedFloatParameter(0.15f, 0f, 10f);
        [Tooltip("Wind direction, degrees (0 = +Z / north, 90 = +X / east).")] public ClampedFloatParameter windDirection = new ClampedFloatParameter(60f, 0f, 360f);
        [Tooltip("Wind speed, m/s (haze drift).")] public ClampedFloatParameter windSpeed = new ClampedFloatParameter(2.5f, 0f, 100f);

        public bool IsActive() => enabled.value && density.value > 0f;
        public bool IsTileCompatible() => false;

        public void Resolve(out int stepCount, out bool half)
        {
            switch (quality.value)
            {
                case ASHFogQuality.Low: stepCount = 16; half = true; break;
                case ASHFogQuality.Medium: stepCount = 32; half = true; break;
                case ASHFogQuality.High: stepCount = 48; half = false; break;
                case ASHFogQuality.Ultra: stepCount = 96; half = false; break;
                default: stepCount = steps.value; half = halfResolution.value; break;
            }
        }
    }
}
