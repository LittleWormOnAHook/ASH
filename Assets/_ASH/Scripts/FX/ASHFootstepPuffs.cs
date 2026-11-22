using ASH.World.Accumulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.FX
{
    /// <summary>
    /// Pooled ash/snow footstep puffs: ONE particle system, Emit() per step (no per-step allocation). Soft, fluffy, light
    /// gray-white mix (ash vs snow from the controller's layers), hangs a little, then settles and sinks into the surface while
    /// fading. Size/count scale with local depth (GetDepthMetres) and speed; nothing on bare ground or roads (below Min Depth).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHFootstepPuffs : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHFootstepPuffs()
        {
            _rtMat = default;
        }

        public enum Quality { Off, Low, Medium, High, Extreme }
        public Quality quality = Quality.Medium;
        public Material material;
        [Header("Look")]
        public Color snowColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        [Tooltip("How far the ash tint is lifted towards white (light gray-white mix).")] [Range(0f, 1f)] public float ashLighten = 0.4f;
        [Range(0f, 1f)] public float opacity = 0.55f;
        [Header("Amount")]
        [Tooltip("No puffs below this local ash/snow depth (m): bare ground, roads.")] [Range(0f, 0.2f)] public float minDepth = 0.015f;
        public Vector2Int countRange = new Vector2Int(3, 12);
        public Vector2 sizeRange = new Vector2(0.18f, 0.55f);
        [Tooltip("Seconds.")] public Vector2 lifetime = new Vector2(2.2f, 3.4f);
        [Tooltip("Outward/up kick (m/s), scaled by speed.")] public Vector2 kick = new Vector2(0.25f, 1.1f);
        [Tooltip("Downward pull: settle and sink into the surface.")] [Range(0f, 0.3f)] public float settle = 0.05f;
        [Tooltip("Air drag (fluffy deceleration).")] [Range(0f, 1f)] public float drag = 0.35f;
        public int maxParticles = 600;

        float _countMul = 1f, _sizeMul = 1f, _lifeMul = 1f;
        ParticleSystem _ps;
        public int Emitted { get; private set; }
        public ParticleSystem System => _ps;

        public void ApplyQuality(Quality q)
        {
            quality = q;
            switch (q)
            {
                case Quality.Off: _countMul = 0f; break;
                case Quality.Low: _countMul = 0.4f; _sizeMul = 0.9f; _lifeMul = 0.75f; maxParticles = 200; break;
                case Quality.Medium: _countMul = 1f; _sizeMul = 1f; _lifeMul = 1f; maxParticles = 600; break;
                case Quality.High: _countMul = 1.6f; _sizeMul = 1.05f; _lifeMul = 1.15f; maxParticles = 1200; break;
                case Quality.Extreme: _countMul = 3f; _sizeMul = 1.15f; _lifeMul = 1.35f; maxParticles = 3000; break;
            }
            if (_ps) { var m = _ps.main; m.maxParticles = maxParticles; }
        }

        void Awake() { ApplyQuality(quality); Build(); }
        void OnValidate() { if (Application.isPlaying) ApplyQuality(quality); }

        void Build()
        {
            var go = new GameObject("ASH_FootstepPuffs"); go.transform.SetParent(null, false); // world space, not parented (player root motion)
            _ps = go.AddComponent<ParticleSystem>(); _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _ps.main; m.loop = false; m.playOnAwake = false; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = maxParticles;
            m.startSpeed = 0f; m.gravityModifier = settle; m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = _ps.emission; em.enabled = false;
            var sh = _ps.shape; sh.enabled = false;
            var lim = _ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0f; lim.dampen = drag; lim.drag = drag; lim.multiplyDragByParticleSize = true;
            var sol = _ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.35f, 1.15f), new Keyframe(1f, 1.35f)));
            var col = _ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.92f, 0.92f, 0.93f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rot = _ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var noise = _ps.noise; noise.enabled = true; noise.strength = 0.12f; noise.frequency = 0.6f; noise.scrollSpeed = 0.2f; noise.quality = ParticleSystemNoiseQuality.Low;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.sharedMaterial = material ? material : RuntimeMaterial(); r.sortingFudge = 10f; r.minParticleSize = 0f; r.maxParticleSize = 2f;
            _ps.Play();
        }

        void OnDestroy() { if (_ps) Destroy(_ps.gameObject); }

        /// <summary>Emit one footstep puff. depth = local ash/snow depth (m), speed01 = 0 walk-still .. 1 run.</summary>
        public void Puff(Vector3 pos, float depth, float speed01)
        {
            if (!_ps || _countMul <= 0f || depth < minDepth) return;
            var c = ASHAccumulationController.Instance;
            float d01 = c && c.maxDepthMetres > 0.01f ? Mathf.Clamp01(depth / Mathf.Max(0.05f, c.ankleDepthMetres * 1.5f)) : 0.5f;
            float a = 0.5f, ashA = 0.7f, snowA = 0.6f; Color ash = new Color(0.44f, 0.44f, 0.46f);
            if (c) { c.GetLayers(pos, out ashA, out snowA); ash = c.ashColor; } // local layers (ash on top of snow)
            a = ashA / Mathf.Max(0.001f, ashA + snowA);
            var ashLight = Color.Lerp(ash, Color.white, ashLighten);
            var c0 = Color.Lerp(snowColor, ashLight, a * 0.5f); var c1 = Color.Lerp(snowColor, ashLight, Mathf.Clamp01(a * 1.1f));
            c0.a = c1.a = opacity;
            float amt = Mathf.Clamp01(0.35f + 0.65f * d01) * Mathf.Lerp(0.55f, 1f, speed01);
            int n = Mathf.RoundToInt(Mathf.Lerp(countRange.x, countRange.y, amt) * _countMul);
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                var dir = Random.insideUnitCircle;
                float k = Mathf.Lerp(kick.x, kick.y, speed01) * Random.Range(0.4f, 1f);
                ep.position = pos + new Vector3(dir.x, 0f, dir.y) * 0.12f + Vector3.up * Random.Range(0.02f, 0.08f);
                ep.velocity = new Vector3(dir.x * k, Random.Range(0.15f, 0.55f) * k + 0.08f, dir.y * k);
                ep.startSize = Random.Range(sizeRange.x, sizeRange.y) * Mathf.Lerp(0.6f, 1.2f, amt) * _sizeMul;
                ep.startLifetime = Random.Range(lifetime.x, lifetime.y) * _lifeMul;
                ep.startColor = Color.Lerp(c0, c1, Random.value);
                ep.rotation = Random.Range(0f, 360f);
                _ps.Emit(ep, 1);
            }
            Emitted += n;
        }

        static Material _rtMat;
        static Material RuntimeMaterial()
        {
            if (_rtMat) return _rtMat;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Simple Lit"); if (!sh) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            _rtMat = new Material(sh) { name = "ASH_FootstepPuff (runtime)" };
            SetupMaterial(_rtMat, SoftTexture(64));
            return _rtMat;
        }

        /// <summary>Transparent alpha-blended URP particle material (also used by the editor tool).</summary>
        public static void SetupMaterial(Material m, Texture2D tex)
        {
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ReceiveShadows", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            m.renderQueue = (int)RenderQueue.Transparent; m.SetOverrideTag("RenderType", "Transparent");
        }

        /// <summary>Soft fluffy puff (radial falloff with value noise), white RGB, alpha = density.</summary>
        public static Texture2D SoftTexture(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "ASH_SoftPuff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size]; float inv = 1f / (size - 1);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = x * inv * 2f - 1f, v = y * inv * 2f - 1f; float r = Mathf.Sqrt(u * u + v * v);
                float n = Mathf.PerlinNoise(x * 0.11f + 3.1f, y * 0.11f + 7.7f) * 0.6f + Mathf.PerlinNoise(x * 0.27f + 11f, y * 0.27f + 2f) * 0.4f;
                float fall = Mathf.Clamp01(1f - r); fall = fall * fall * (3f - 2f * fall);
                float al = Mathf.Clamp01(fall * (0.55f + 0.7f * n) - 0.05f);
                byte b = (byte)Mathf.RoundToInt(Mathf.Lerp(225f, 255f, n));
                px[y * size + x] = new Color32(b, b, b, (byte)Mathf.RoundToInt(al * 255f));
            }
            t.SetPixels32(px); t.Apply(true); return t;
        }
    }
}
