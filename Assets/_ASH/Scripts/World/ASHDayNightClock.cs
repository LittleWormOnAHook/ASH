using UnityEngine;

namespace ASH.World
{
    /// <summary>
    /// Lite day/night clock (role of Genesis DMIoClock, reduced to one sun + ambient + fog). Drives the sun's
    /// rotation, colour and intensity, ambient light and fog colour from a 24 h time of day. Wide ranges plus an
    /// Extreme preset (very fast cycle) for stress tests.
    /// </summary>
    [ExecuteAlways]
    public sealed class ASHDayNightClock : MonoBehaviour
    {
        [SerializeField] private Light sun;
        [Tooltip("Hours, 0-24. The slice starts in late afternoon.")]
        [Range(0f, 24f)] public float timeOfDay = 17.2f;
        [Tooltip("Real minutes per in-game 24 h. 0 = frozen.")]
        [Range(0f, 1440f)] public float minutesPerDay = 48f;
        [Tooltip("Sun azimuth (degrees, world Y) at noon.")]
        [Range(0f, 360f)] public float sunAzimuth = 170f;
        [Range(0f, 90f)] public float maxSunElevation = 62f;
        [Range(0f, 8f)] public float maxSunIntensity = 2.1f;
        [Range(0f, 2f)] public float moonIntensity = 0.12f;
        public Gradient sunColor = DefaultSunGradient();
        public Gradient ambientColor = DefaultAmbientGradient();
        public Gradient fogColor = DefaultFogGradient();
        [Tooltip("Smoke-haze tint blended into fog/ambient (ash in the air).")]
        [Range(0f, 1f)] public float ashHaze = 0.35f;
        public Color ashHazeColor = new Color(0.55f, 0.50f, 0.46f);
        public bool driveFog = true;
        public bool previewInEditMode;

        public float Hour01 => timeOfDay / 24f;
        public bool IsNight => timeOfDay < 5.5f || timeOfDay > 19.8f;

        private void Update()
        {
            if (Application.isPlaying || previewInEditMode)
            {
                if (Application.isPlaying && minutesPerDay > 0f)
                    timeOfDay = Mathf.Repeat(timeOfDay + Time.deltaTime * 24f / (minutesPerDay * 60f), 24f);
            }
            Apply();
        }

        public void Apply()
        {
            if (sun == null) sun = RenderSettings.sun;
            float t = Hour01;
            // Sun path: rises 6:00 east, sets 20:00 west (summer-ish, high plains).
            float dayT = Mathf.InverseLerp(6f, 20f, timeOfDay);
            float elev = Mathf.Sin(dayT * Mathf.PI) * maxSunElevation;
            bool day = timeOfDay > 6f && timeOfDay < 20f;
            if (!day) elev = 10f + 25f * Mathf.Sin(Mathf.InverseLerp(20f, 30f, timeOfDay < 6f ? timeOfDay + 24f : timeOfDay) * Mathf.PI); // moon
            float az = sunAzimuth + Mathf.Lerp(-100f, 100f, day ? dayT : 0.5f);
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(Mathf.Max(elev, 2f), az, 0f);
                float sunUp = Mathf.Clamp01(Mathf.InverseLerp(-2f, 12f, day ? elev : -10f));
                sun.intensity = day ? maxSunIntensity * sunUp : moonIntensity;
                sun.color = day ? sunColor.Evaluate(t) : new Color(0.55f, 0.62f, 0.85f);
                sun.shadowStrength = day ? Mathf.Lerp(0.5f, 0.95f, sunUp) : 0.4f;
            }
            var amb = Color.Lerp(ambientColor.Evaluate(t), ashHazeColor * ambientColor.Evaluate(t).maxColorComponent, ashHaze * 0.5f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = amb;
            if (driveFog) RenderSettings.fogColor = Color.Lerp(fogColor.Evaluate(t), ashHazeColor * fogColor.Evaluate(t).maxColorComponent, ashHaze);
        }

        [ContextMenu("Preset/Default (48 min day)")] public void PresetDefault() { minutesPerDay = 48f; }
        [ContextMenu("Preset/Frozen golden hour")] public void PresetFrozen() { minutesPerDay = 0f; timeOfDay = 18.4f; }
        [ContextMenu("Preset/Extreme (1 min day)")] public void PresetExtreme() { minutesPerDay = 1f; }

        static Gradient G(params (float t, Color c)[] keys)
        {
            var g = new Gradient(); var ck = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++) ck[i] = new GradientColorKey(keys[i].c, keys[i].t);
            g.SetKeys(ck, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }); return g;
        }
        static Gradient DefaultSunGradient() => G((0.25f, new Color(1f, 0.55f, 0.3f)), (0.35f, new Color(1f, 0.86f, 0.7f)), (0.5f, new Color(1f, 0.96f, 0.9f)), (0.72f, new Color(1f, 0.8f, 0.58f)), (0.8f, new Color(1f, 0.5f, 0.25f)), (0.85f, new Color(0.9f, 0.35f, 0.2f)));
        static Gradient DefaultAmbientGradient() => G((0f, new Color(0.05f, 0.06f, 0.1f)), (0.25f, new Color(0.3f, 0.26f, 0.25f)), (0.5f, new Color(0.52f, 0.52f, 0.5f)), (0.75f, new Color(0.45f, 0.36f, 0.3f)), (0.85f, new Color(0.16f, 0.13f, 0.15f)), (1f, new Color(0.05f, 0.06f, 0.1f)));
        static Gradient DefaultFogGradient() => G((0f, new Color(0.04f, 0.05f, 0.08f)), (0.25f, new Color(0.55f, 0.45f, 0.4f)), (0.5f, new Color(0.68f, 0.66f, 0.62f)), (0.75f, new Color(0.7f, 0.52f, 0.38f)), (0.85f, new Color(0.25f, 0.18f, 0.17f)), (1f, new Color(0.04f, 0.05f, 0.08f)));
    }
}
