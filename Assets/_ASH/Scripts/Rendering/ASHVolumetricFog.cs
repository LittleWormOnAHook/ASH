using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.Rendering
{
    public enum ASHFogQuality { Low, Medium, High, Ultra, Custom }
    public enum ASHFogHeightMode { World, RelativeToCamera }
    public enum ASHFogDebug { Off, FogOnly, Transmittance, FogColor }
    [Serializable] public sealed class ASHFogQualityParameter : VolumeParameter<ASHFogQuality> { public ASHFogQualityParameter(ASHFogQuality v, bool o = false) : base(v, o) { } }
    [Serializable] public sealed class ASHFogHeightModeParameter : VolumeParameter<ASHFogHeightMode> { public ASHFogHeightModeParameter(ASHFogHeightMode v, bool o = false) : base(v, o) { } }
    [Serializable] public sealed class ASHFogDebugParameter : VolumeParameter<ASHFogDebug> { public ASHFogDebugParameter(ASHFogDebug v, bool o = false) : base(v, o) { } }

    /// <summary>"ASH Volumetric Fog" volume override (Global Volume > Add Override > ASH > ASH Volumetric Fog). Rendered by
    /// ASHVolumetricFogFeature on the URP renderer. Local fog: ASHFogArea components (GameObject > ASH > Volumetric Fog Area).</summary>
    [Serializable, VolumeComponentMenu("ASH/ASH Volumetric Fog"), SupportedOnRenderPipeline(typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset))]
    public sealed class ASHVolumetricFog : VolumeComponent, IPostProcessComponent
    {
        [Header("General")]
        public BoolParameter enabled = new BoolParameter(false);
        [Tooltip("Multiplies all fog density (global and fog areas).")] public ClampedFloatParameter densityMultiplier = new ClampedFloatParameter(1f, 0f, 10f);
        [Tooltip("Multiplies the in-scattered light (brightness of the fog).")] public ClampedFloatParameter brightness = new ClampedFloatParameter(1f, 0f, 10f);

        [Header("Height")]
        [Tooltip("Extinction per metre at and below Base Height.")] public ClampedFloatParameter density = new ClampedFloatParameter(0.004f, 0f, 1f);
        public ASHFogHeightModeParameter heightMode = new ASHFogHeightModeParameter(ASHFogHeightMode.World);
        [Tooltip("World Y (or offset from the camera in Relative mode) where the fog is densest; it thins out above.")] public FloatParameter baseHeight = new FloatParameter(0f);
        [Tooltip("How fast density falls off above Base Height (1/m). 0 = uniform.")] public ClampedFloatParameter heightFalloff = new ClampedFloatParameter(0.035f, 0f, 2f);
        [Tooltip("Density below Base Height relative to the base (1 = flat, <1 thinner in hollows).")] public ClampedFloatParameter belowBaseDensity = new ClampedFloatParameter(1f, 0f, 4f);
        [Tooltip("No fog above this world Y (soft over Ceiling Fade).")] public FloatParameter ceilingHeight = new FloatParameter(100000f);
        public ClampedFloatParameter ceilingFade = new ClampedFloatParameter(50f, 0.1f, 2000f);

        [Header("Lighting")]
        public ColorParameter albedo = new ColorParameter(new Color(0.72f, 0.68f, 0.64f), false, false, true);
        [Tooltip("Tint the albedo with the scene fog colour (driven by ASHDayNightClock) so it matches the distance fog.")]
        public BoolParameter useDayNightColor = new BoolParameter(true);
        [Tooltip("How much of the day/night fog colour tint is used.")] public ClampedFloatParameter dayNightColorBlend = new ClampedFloatParameter(1f, 0f, 1f);
        public ColorParameter ambientTint = new ColorParameter(new Color(0.42f, 0.44f, 0.5f), true, false, true);
        [Tooltip("Multiplies Ambient Tint and the scene ambient colour.")] public ClampedFloatParameter ambientIntensity = new ClampedFloatParameter(1f, 0f, 16f);
        [Tooltip("Henyey-Greenstein g for the sun: >0 glows toward the sun, <0 back-scatters.")] public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.35f, -0.95f, 0.95f);
        [Tooltip("Main light (sun/moon) in-scattering strength.")] public ClampedFloatParameter lightIntensity = new ClampedFloatParameter(0.6f, 0f, 32f);
        public ColorParameter mainLightTint = new ColorParameter(Color.white, true, false, true);
        [Tooltip("Point/spot lights (torch, flashlight, lanterns) light the fog.")] public BoolParameter additionalLights = new BoolParameter(true);
        public ClampedFloatParameter additionalLightIntensity = new ClampedFloatParameter(1f, 0f, 32f);
        public ClampedIntParameter maxAdditionalLights = new ClampedIntParameter(4, 0, 8);
        public ClampedFloatParameter additionalAnisotropy = new ClampedFloatParameter(0.2f, -0.95f, 0.95f);

        [Header("Shadows")]
        [Tooltip("Sample the main light shadow map along the ray (light shafts through trees).")] public BoolParameter shadows = new BoolParameter(true);
        [Tooltip("0 = shadows ignored, 1 = full shafts.")] public ClampedFloatParameter shadowStrength = new ClampedFloatParameter(1f, 0f, 1f);

        [Header("Noise / Wind")]
        [Tooltip("0 disables the noise.")] public ClampedFloatParameter noiseStrength = new ClampedFloatParameter(0.5f, 0f, 1f);
        [Tooltip("Noise frequency (1/m).")] public ClampedFloatParameter noiseScale = new ClampedFloatParameter(0.04f, 0.0005f, 2f);
        public ClampedIntParameter noiseOctaves = new ClampedIntParameter(2, 1, 3);
        [Tooltip("How fast the haze churns (vertical evolution).")] public ClampedFloatParameter noiseSpeed = new ClampedFloatParameter(0.15f, 0f, 10f);
        [Tooltip("Wind direction, degrees (0 = +Z / north, 90 = +X / east).")] public ClampedFloatParameter windDirection = new ClampedFloatParameter(60f, 0f, 360f);
        [Tooltip("Wind speed, m/s (haze drift).")] public ClampedFloatParameter windSpeed = new ClampedFloatParameter(2.5f, 0f, 100f);

        [Header("Distance")]
        [Tooltip("Ray length, metres.")] public ClampedFloatParameter maxDistance = new ClampedFloatParameter(300f, 5f, 5000f);
        [Tooltip("No fog closer than this (m).")] public ClampedFloatParameter startDistance = new ClampedFloatParameter(0f, 0f, 1000f);
        [Tooltip("Fog fades in over this many metres after Start Distance.")] public ClampedFloatParameter startFade = new ClampedFloatParameter(2f, 0f, 500f);

        [Header("Sky blending")]
        [Tooltip("Fog over the sky (pixels with no geometry).")] public BoolParameter fogSky = new BoolParameter(true);
        [Tooltip("Density multiplier along sky rays.")] public ClampedFloatParameter skyDensity = new ClampedFloatParameter(1f, 0f, 4f);
        [Tooltip("Sky rays march this fraction of Max Distance.")] public ClampedFloatParameter skyDistance = new ClampedFloatParameter(1f, 0.05f, 4f);

        [Header("Quality / Performance")]
        [Tooltip("Low 16 steps half res, Medium 32 half, High 48 full, Ultra 96 full. Custom uses Steps / Half Resolution.")]
        public ASHFogQualityParameter quality = new ASHFogQualityParameter(ASHFogQuality.Medium);
        public ClampedIntParameter steps = new ClampedIntParameter(32, 4, 256);
        public BoolParameter halfResolution = new BoolParameter(true);
        [Tooltip("Half-res upsample: how strongly depth edges are preserved.")] public ClampedFloatParameter upsampleDepthSensitivity = new ClampedFloatParameter(1f, 0.01f, 20f);
        [Tooltip("Stop marching once the ray is this opaque (higher = cheaper).")] public ClampedFloatParameter earlyOutOpacity = new ClampedFloatParameter(0.997f, 0.9f, 1f);

        [Header("Temporal")]
        [Tooltip("Vary the ray start every frame (pairs well with TAA). Spatial noise jitter is always on.")]
        public BoolParameter temporalJitter = new BoolParameter(false);
        [Tooltip("Ray start jitter amount (hides banding; 0 = none).")] public ClampedFloatParameter jitterStrength = new ClampedFloatParameter(1f, 0f, 1f);

        [Header("Debug")]
        public ASHFogDebugParameter debugView = new ASHFogDebugParameter(ASHFogDebug.Off);

        public bool IsActive() => enabled.value && densityMultiplier.value > 0f && (density.value > 0f || ASHFogArea.Active.Count > 0);
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
