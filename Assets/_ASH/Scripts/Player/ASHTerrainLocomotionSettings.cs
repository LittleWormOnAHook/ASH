using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// Terrain slope + ash/snow depth slowdown (Resources/ASH_TerrainLocomotionSettings).
    /// The player is slower uphill (curve vs degrees) and in deeper ash/snow (ASHAccumulationController.GetDepthMetres,
    /// already thinner on packed main paths). Very steep slopes use Invector's slope limit and slide.
    /// Climbing costs extra stamina, multiplied by ASHSurvivalTuning.DrainRate.
    /// Wide ranges plus Relaxed / Default / Extreme presets.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Player/Terrain Locomotion Settings", fileName = "ASH_TerrainLocomotionSettings")]
    public sealed class ASHTerrainLocomotionSettings : ScriptableObject
    {
        public enum Preset { Relaxed, Default, Extreme }

        [Header("Preset")]
        public Preset preset = Preset.Default;

        [Header("Slope (degrees; 0 = flat, positive = uphill in the move direction)")]
        [Tooltip("Speed multiplier vs uphill angle in degrees (1 = no change).")]
        public AnimationCurve uphillSpeed = DefaultUphill();
        [Tooltip("Speed multiplier vs downhill angle in degrees (use unsigned degrees; 1 = no change).")]
        public AnimationCurve downhillSpeed = DefaultDownhill();
        [Tooltip("Degrees of uphill before the uphill curve is applied (flat and gentle rises stay full speed).")]
        [Range(0f, 30f)] public float uphillStartDegrees = 4f;
        [Tooltip("Invector slope limit applied at runtime (degrees). Above this the motor blocks the climb and can slide. Restored when the component is disabled.")]
        [Range(15f, 89f)] public float slopeLimitDegrees = 55f;
        [Tooltip("Let Invector slide the player when the ground angle is steeper than the slope limit.")]
        public bool allowSlide = true;
        [Tooltip("On slopes this steep or more, do not apply a downhill speed bonus (keep the downhill curve's low end).")]
        [Range(20f, 89f)] public float steepDownhillDegrees = 40f;

        [Header("Ash / snow depth (metres, from ASHAccumulationController)")]
        [Tooltip("Speed multiplier vs combined ash+snow depth in metres (1 = no change). Ankle ~0.12 m, knee ~0.5 m.")]
        public AnimationCurve depthSpeed = DefaultDepth();
        [Tooltip("If the accumulation system is missing, optionally fall back to a fixed depth (0 = no depth penalty).")]
        [Range(0f, 2f)] public float fallbackDepthMetres = 0f;
        [Tooltip("Extra speed multiplier on packed main paths (GetPathFactor). 1 = paths are only faster because they hold less snow.")]
        [Range(0.5f, 2f)] public float pathSpeedBonus = 1.05f;

        [Header("Stamina")]
        [Tooltip("Extra stamina drained per real second when climbing at 45° (scales with uphill angle / 45, and with move input). Multiplied by ASHSurvivalTuning.DrainRate.")]
        [Range(0f, 80f)] public float climbStaminaPerSecond = 8f;
        [Tooltip("Uphill degrees that count as a climb for stamina (below this, no extra drain).")]
        [Range(0f, 45f)] public float climbStaminaStartDegrees = 8f;
        [Tooltip("Optional extra stamina drain per real second per metre of ash/snow depth while moving (0 = off). Also multiplied by DrainRate.")]
        [Range(0f, 40f)] public float depthStaminaPerMetre = 2f;

        [Header("Blend")]
        [Tooltip("How quickly the speed multiplier eases toward the target (per second).")]
        [Range(0.5f, 40f)] public float blendPerSecond = 8f;
        [Tooltip("Hard floor on the combined speed multiplier.")]
        [Range(0.01f, 1f)] public float minSpeedMultiplier = 0.12f;
        [Tooltip("Hard ceiling on the combined speed multiplier (downhill / path bonuses).")]
        [Range(0.5f, 3f)] public float maxSpeedMultiplier = 1.25f;
        [Tooltip("Apply slowdown only while grounded.")]
        public bool groundedOnly = true;

        static ASHTerrainLocomotionSettings _i;
        public static ASHTerrainLocomotionSettings I
        {
            get
            {
                if (!_i)
                {
                    _i = Resources.Load<ASHTerrainLocomotionSettings>("ASH_TerrainLocomotionSettings");
                    if (!_i) _i = CreateInstance<ASHTerrainLocomotionSettings>();
                }
                return _i;
            }
        }

        public void ApplyPreset(Preset p)
        {
            preset = p;
            switch (p)
            {
                case Preset.Relaxed:
                    uphillSpeed = Curve((0f, 1f), (20f, 0.95f), (40f, 0.75f), (60f, 0.45f), (90f, 0.2f));
                    downhillSpeed = Curve((0f, 1f), (20f, 1.05f), (40f, 1f), (70f, 0.8f), (90f, 0.5f));
                    uphillStartDegrees = 8f; slopeLimitDegrees = 65f; allowSlide = true; steepDownhillDegrees = 50f;
                    depthSpeed = Curve((0f, 1f), (0.12f, 0.95f), (0.5f, 0.7f), (1f, 0.45f), (2f, 0.25f));
                    fallbackDepthMetres = 0f; pathSpeedBonus = 1.02f;
                    climbStaminaPerSecond = 3f; climbStaminaStartDegrees = 12f; depthStaminaPerMetre = 0.5f;
                    blendPerSecond = 8f; minSpeedMultiplier = 0.2f; maxSpeedMultiplier = 1.2f;
                    break;
                case Preset.Extreme:
                    uphillSpeed = Curve((0f, 1f), (8f, 0.8f), (20f, 0.45f), (35f, 0.18f), (50f, 0.05f), (90f, 0.02f));
                    downhillSpeed = Curve((0f, 1f), (15f, 1.15f), (30f, 0.85f), (45f, 0.4f), (90f, 0.1f));
                    uphillStartDegrees = 2f; slopeLimitDegrees = 38f; allowSlide = true; steepDownhillDegrees = 25f;
                    depthSpeed = Curve((0f, 1f), (0.08f, 0.7f), (0.2f, 0.4f), (0.5f, 0.15f), (1f, 0.05f), (2f, 0.02f));
                    fallbackDepthMetres = 0f; pathSpeedBonus = 1.15f;
                    climbStaminaPerSecond = 28f; climbStaminaStartDegrees = 4f; depthStaminaPerMetre = 12f;
                    blendPerSecond = 14f; minSpeedMultiplier = 0.02f; maxSpeedMultiplier = 1.4f;
                    break;
                default:
                    uphillSpeed = DefaultUphill(); downhillSpeed = DefaultDownhill(); depthSpeed = DefaultDepth();
                    uphillStartDegrees = 4f; slopeLimitDegrees = 55f; allowSlide = true; steepDownhillDegrees = 40f;
                    fallbackDepthMetres = 0f; pathSpeedBonus = 1.05f;
                    climbStaminaPerSecond = 8f; climbStaminaStartDegrees = 8f; depthStaminaPerMetre = 2f;
                    blendPerSecond = 8f; minSpeedMultiplier = 0.12f; maxSpeedMultiplier = 1.25f;
                    break;
            }
        }

        public static AnimationCurve DefaultUphill() => Curve((0f, 1f), (15f, 0.88f), (30f, 0.62f), (45f, 0.35f), (60f, 0.15f), (90f, 0.05f));
        public static AnimationCurve DefaultDownhill() => Curve((0f, 1f), (15f, 1.06f), (30f, 1.02f), (45f, 0.8f), (70f, 0.5f), (90f, 0.25f));
        public static AnimationCurve DefaultDepth() => Curve((0f, 1f), (0.12f, 0.85f), (0.5f, 0.45f), (1f, 0.22f), (2f, 0.1f));

        static AnimationCurve Curve(params (float t, float v)[] keys)
        {
            var c = new AnimationCurve();
            foreach (var k in keys) c.AddKey(new Keyframe(k.t, k.v));
            for (int i = 0; i < c.keys.Length; i++) c.SmoothTangents(i, 0.4f);
            return c;
        }

        [ContextMenu("Preset/Relaxed")] void P0() => ApplyPreset(Preset.Relaxed);
        [ContextMenu("Preset/Default")] void P1() => ApplyPreset(Preset.Default);
        [ContextMenu("Preset/Extreme (stress test)")] void P2() => ApplyPreset(Preset.Extreme);
    }
}
