using UnityEngine;

namespace ASH.World
{
    /// <summary>
    /// Falling ash: a snow-style particle volume re-tuned for ash (small flakes, grey/charcoal, slow drift and
    /// flutter). Follows the active camera so the volume is always around the player. All settings have wide
    /// ranges; presets Light / Default / Heavy / Extreme (stress test). No Gaia/vendor snow particle system was
    /// present (Gaia snow is shader-only), so this configures a URP particle system in code.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    [ExecuteAlways]
    public sealed class ASHAshFall : MonoBehaviour
    {
        [Header("Volume")]
        [Range(5f, 150f)] public float radius = 22f;
        [Tooltip("Top of the emission volume above the camera (m).")]
        [Range(2f, 60f)] public float heightAboveCamera = 10f;
        [Tooltip("Bottom of the emission volume below the camera (m). The volume must span eye level or flakes die before reaching view.")]
        [Range(0f, 30f)] public float heightBelowCamera = 6f;
        [Range(0f, 50000f)] public float particlesPerSecond = 1350f;
        [Range(100, 200000)] public int maxParticles = 30000;
        [Range(1f, 60f)] public float lifetime = 18f;
        [Tooltip("Minimum on-screen size as a fraction of screen height, so distant flakes stay visible (0.0008 ~ 0.9 px at 1080p).")]
        [Range(0f, 0.01f)] public float minScreenSize = 0.0008f;
        [HideInInspector] public int configVersion = 0;
        [Header("Flakes")]
        public Vector2 sizeRange = new Vector2(0.016f, 0.055f);
        public Color colorA = new Color(0.32f, 0.31f, 0.30f, 0.85f);
        public Color colorB = new Color(0.62f, 0.60f, 0.57f, 0.75f);
        [Tooltip("Small share of glowing embers among the flakes (0 = none).")]
        [Range(0f, 0.2f)] public float emberShare = 0.006f;
        public Color emberColor = new Color(1f, 0.45f, 0.15f, 1f);
        [Header("Motion")]
        [Range(0f, 5f)] public float fallSpeed = 0.7f;
        public Vector3 wind = new Vector3(0.6f, 0f, 0.25f);
        [Range(0f, 5f)] public float flutter = 0.7f;
        [Range(0f, 2f)] public float flutterFrequency = 0.35f;
        [Range(0f, 360f)] public float spinDegreesPerSec = 90f;
        public bool followCamera = true;

        private ParticleSystem _ps;
        private Transform _follow;

        private void OnEnable() { if (configVersion < 2) Base(); Apply(); }
        private void OnValidate() { if (isActiveAndEnabled) Apply(); }

        public void Apply()
        {
            _ps = GetComponent<ParticleSystem>();
            var main = _ps.main;
            main.loop = true; main.prewarm = true; main.playOnAwake = true;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.scalingMode = ParticleSystemScalingMode.Shape;

            var em = _ps.emission; em.enabled = true; em.rateOverTime = particlesPerSecond;
            var sh = _ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box;
            float h = heightAboveCamera + heightBelowCamera;
            sh.scale = new Vector3(radius * 2f, Mathf.Max(1f, h), radius * 2f); sh.position = new Vector3(0f, -h * 0.5f, 0f); sh.rotation = Vector3.zero;

            var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(wind.x * 0.7f, wind.x * 1.3f);
            vel.y = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.3f, -fallSpeed * 0.7f);
            vel.z = new ParticleSystem.MinMaxCurve(wind.z * 0.7f, wind.z * 1.3f);

            var noise = _ps.noise; noise.enabled = flutter > 0f; noise.strength = flutter; noise.frequency = flutterFrequency;
            noise.scrollSpeed = 0.2f; noise.damping = true; noise.quality = ParticleSystemNoiseQuality.Medium;

            var rot = _ps.rotationOverLifetime; rot.enabled = spinDegreesPerSec > 0f;
            rot.z = new ParticleSystem.MinMaxCurve(-spinDegreesPerSec * Mathf.Deg2Rad, spinDegreesPerSec * Mathf.Deg2Rad);

            var col = _ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var sub = _ps.customData; sub.enabled = false;
            var r = GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.minParticleSize = minScreenSize; r.maxParticleSize = 0.05f; r.sortingFudge = 0f;
            r.alignment = ParticleSystemRenderSpace.View;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;

            ConfigureEmbers();
        }

        private void ConfigureEmbers()
        {
            var t = transform.Find("Embers");
            if (emberShare <= 0f) { if (t) t.gameObject.SetActive(false); return; }
            if (t == null) return; // editor builder creates the child
            t.gameObject.SetActive(true);
            var ps = t.GetComponent<ParticleSystem>(); if (!ps) return;
            var m = ps.main; m.loop = true; m.prewarm = true; m.startLifetime = lifetime * 0.6f; m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x * 0.6f, sizeRange.y * 0.8f);
            m.startColor = emberColor; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = Mathf.Max(10, (int)(maxParticles * emberShare * 2f));
            var e = ps.emission; e.rateOverTime = particlesPerSecond * emberShare;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Box; float hh = heightAboveCamera + heightBelowCamera; s.scale = new Vector3(radius * 2f, Mathf.Max(1f, hh), radius * 2f); s.position = new Vector3(0f, -hh * 0.5f, 0f);
            var er = t.GetComponent<ParticleSystemRenderer>(); if (er) er.minParticleSize = minScreenSize;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(wind.x * 0.7f, wind.x * 1.3f); v.y = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.1f, -fallSpeed * 0.5f); v.z = new ParticleSystem.MinMaxCurve(wind.z * 0.7f, wind.z * 1.3f);
            var n = ps.noise; n.enabled = true; n.strength = flutter * 1.3f; n.frequency = flutterFrequency;
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.4f, 0.4f, 0.4f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        }

        private void LateUpdate()
        {
            if (!followCamera) return;
            // re-acquire if the cached camera was disabled/destroyed (e.g. scene camera vs Invector player camera)
            if (_follow == null || !_follow.gameObject.activeInHierarchy) { _follow = null; var cam = Camera.main; if (cam) _follow = cam.transform; }
            if (_follow == null) return;
            var p = _follow.position; transform.position = new Vector3(p.x, p.y + heightAboveCamera, p.z);
            transform.rotation = Quaternion.identity;
        }

        [ContextMenu("Preset/Light")] public void PresetLight() { Base(); particlesPerSecond = 500f; maxParticles = 15000; flutter = 0.5f; Apply(); }
        [ContextMenu("Preset/Default")] public void PresetDefault() { Base(); Apply(); }
        [ContextMenu("Preset/Heavy")] public void PresetHeavy() { Base(); particlesPerSecond = 4500f; maxParticles = 90000; flutter = 1.1f; sizeRange = new Vector2(0.035f, 0.1f); Apply(); }
        [ContextMenu("Preset/Extreme (stress)")] public void PresetExtreme() { Base(); particlesPerSecond = 20000f; maxParticles = 200000; radius = 60f; heightAboveCamera = 25f; heightBelowCamera = 10f; flutter = 1.6f; sizeRange = new Vector2(0.04f, 0.12f); Apply(); }

        void Base()
        {
            radius = 22f; heightAboveCamera = 10f; heightBelowCamera = 6f; particlesPerSecond = 1350f; maxParticles = 30000; lifetime = 18f;
            minScreenSize = 0.0008f; sizeRange = new Vector2(0.016f, 0.055f); colorA = new Color(0.32f, 0.31f, 0.30f, 0.85f); colorB = new Color(0.62f, 0.60f, 0.57f, 0.75f);
            emberShare = 0.006f; fallSpeed = 0.7f; wind = new Vector3(0.6f, 0f, 0.25f); flutter = 0.7f; flutterFrequency = 0.35f; spinDegreesPerSec = 90f; followCamera = true; configVersion = 2;
        }

        /// <summary>Play-mode diagnostic: logs each ash volume's state relative to the live camera.</summary>
        public static void LogState()
        {
            var cam = Camera.main;
            var all = FindObjectsByType<ASHAshFall>(FindObjectsInactive.Include);
            Debug.Log($"[ASH] AshFall diag: instances={all.Length} Camera.main={(cam ? cam.name + " @" + cam.transform.position + " mask=" + cam.cullingMask : "NONE")}");
            foreach (var a in all)
            {
                var ps = a.GetComponent<ParticleSystem>(); var r = a.GetComponent<ParticleSystemRenderer>();
                var buf = new ParticleSystem.Particle[Mathf.Min(ps.particleCount, 20000)]; int n = ps.GetParticles(buf);
                float minY = float.MaxValue, maxY = float.MinValue; int near = 0; float camY = cam ? cam.transform.position.y : 0f;
                for (int i = 0; i < n; i++) { float dy = buf[i].position.y - camY; minY = Mathf.Min(minY, dy); maxY = Mathf.Max(maxY, dy); if (cam && (buf[i].position - cam.transform.position).sqrMagnitude < 15f * 15f) near++; }
                Debug.Log($"[ASH] AshFall '{a.name}' active={a.isActiveAndEnabled} playing={ps.isPlaying} alive={ps.particleCount} near15m={near} dyRange=({(n > 0 ? minY : 0):F1},{(n > 0 ? maxY : 0):F1}) pos={a.transform.position} layer={a.gameObject.layer} inMask={(cam ? (cam.cullingMask & (1 << a.gameObject.layer)) != 0 : false)} shader={(r.sharedMaterial ? r.sharedMaterial.shader.name : "NULL")} rate={a.particlesPerSecond} size={a.sizeRange} v={a.configVersion}");
            }
        }
    }
}
