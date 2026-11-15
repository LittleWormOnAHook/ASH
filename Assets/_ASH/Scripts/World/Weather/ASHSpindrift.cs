using ASH.World.Accumulation;
using UnityEngine;

namespace ASH.World.Weather
{
    /// <summary>
    /// Blowing snow / ash near the ground ("spindrift"): thin streaks that race along the shared wind in gusts, only around
    /// the player, more when there is loose snow/ash on the ground and when the wind gusts. Driven by ASHWeatherDirector
    /// (wind dir/strength, Gust01, state spindrift amount) and ASHAccumulationController (ground snow/ash, wetness).
    /// One small particle system (default max 700 particles), runtime-only, suppressed indoors.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHSpindrift : MonoBehaviour
    {
        [Tooltip("Global multiplier on top of each weather state's Spindrift amount.")] [Range(0f, 5f)] public float amount = 1f;
        [Tooltip("Particles per second at amount 1, full wind, loose ground cover.")] [Range(0f, 2000f)] public float baseRate = 140f;
        [Range(50, 5000)] public int maxParticles = 700;
        [Tooltip("Area around the player (m).")] [Range(2f, 60f)] public float radius = 13f;
        [Tooltip("Height of the drift layer above the feet (m).")] [Range(0.05f, 5f)] public float layerHeight = 0.55f;
        [Tooltip("Wisp speed as a fraction of the wind speed.")] [Range(0f, 3f)] public float speedFactor = 1f;
        [Tooltip("Extra emission during gusts (x Gust01).")] [Range(0f, 8f)] public float gustBurst = 2.2f;
        [Tooltip("Wind speed (m/s) at which drifting is fully on.")] [Range(0.5f, 40f)] public float fullWindMS = 8f;
        public Vector2 lifetime = new Vector2(1.2f, 2.8f);
        public Vector2 size = new Vector2(0.04f, 0.14f);
        [Tooltip("Streak length (stretched billboards).")] [Range(0f, 0.5f)] public float streak = 0.07f;
        [Range(0f, 1f)] public float alpha = 0.35f;
        public Color snowColor = new Color(0.95f, 0.97f, 1f);
        public Color ashColor = new Color(0.55f, 0.52f, 0.5f);
        [Tooltip("Swirl (noise) strength.")] [Range(0f, 3f)] public float swirl = 0.5f;

        ASHWeatherDirector _d; ASHWeatherOutputs _o; ParticleSystem _ps; Transform _player; float _nextSync;
        public float Rate { get; private set; }

        void Start()
        {
            _d = GetComponent<ASHWeatherDirector>(); _o = GetComponent<ASHWeatherOutputs>();
            var cc = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>(); _player = cc ? cc.transform : null;
            Build();
        }

        void Build()
        {
            var go = new GameObject("ASH_Spindrift") { hideFlags = HideFlags.DontSave }; go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>(); _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _ps.main; m.loop = true; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startSpeed = 0f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y); m.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            m.maxParticles = maxParticles; m.gravityModifier = 0f; m.startColor = snowColor;
            var em = _ps.emission; em.rateOverTime = 0f;
            var sh = _ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(radius * 2f, layerHeight, radius * 2f); sh.position = new Vector3(0f, layerHeight * 0.5f, 0f);
            var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            var n = _ps.noise; n.enabled = swirl > 0f; n.strength = swirl; n.frequency = 0.6f; n.scrollSpeed = 0.4f; n.damping = true; n.quality = ParticleSystemNoiseQuality.Low;
            var col = _ps.colorOverLifetime; col.enabled = true; var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = streak; r.lengthScale = 1.2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            var src = _o && _o.ashFall ? _o.ashFall.GetComponent<ParticleSystemRenderer>() : null;
            r.sharedMaterial = src && src.sharedMaterial ? src.sharedMaterial : DefaultMaterial();
            if (_o && _o.interior) { var tr = _ps.trigger; tr.enabled = true; tr.SetCollider(0, _o.interior); tr.inside = ParticleSystemOverlapAction.Kill; tr.enter = ParticleSystemOverlapAction.Kill; tr.outside = ParticleSystemOverlapAction.Ignore; }
            _ps.Play();
        }

        static Material _def;
        static Material DefaultMaterial()
        {
            if (_def) return _def;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit"); if (!sh) return null;
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16, 16)) / 16f; tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d))); }
            tex.Apply();
            _def = new Material(sh) { hideFlags = HideFlags.DontSave, renderQueue = 3000 };
            _def.SetTexture("_BaseMap", tex); _def.SetFloat("_Surface", 1f); _def.SetFloat("_Blend", 0f);
            _def.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); _def.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); _def.SetFloat("_ZWrite", 0f);
            _def.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return _def;
        }

        void LateUpdate()
        {
            if (!_ps || !_d) return;
            if (!_player) { var cc = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>(); _player = cc ? cc.transform : null; if (!_player) return; }
            var w = _d.Current; var dir = new Vector3(_d.WindDir2.x, 0f, _d.WindDir2.y);
            // emitter sits slightly upwind so wisps stream across the player
            _ps.transform.SetPositionAndRotation(_player.position - dir * radius * 0.35f, Quaternion.identity);
            if (Time.time < _nextSync) return; _nextSync = Time.time + 0.2f;
            var acc = ASHAccumulationController.Instance;
            float snowG = acc ? acc.snowAmount : 0.3f, ashG = acc ? acc.ashAmount : 0f, wet = acc ? acc.wetness : 0f;
            float loose = Mathf.Clamp01((snowG + ashG) * (1f - wet) * (1f - w.rain) + w.snow * 0.5f + w.ashfall * 0.3f);
            float windK = Mathf.Clamp01(_d.WindMS / Mathf.Max(0.1f, fullWindMS));
            float indoor = _o ? _o.Indoor01 : 0f;
            Rate = baseRate * amount * w.spindrift * windK * (0.25f + gustBurst * _d.Gust01) * loose * (1f - indoor);
            var em = _ps.emission; em.rateOverTime = Rate;
            float spd = _d.WindMS * speedFactor;
            var vel = _ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(dir.x * spd * 0.7f, dir.x * spd * 1.3f);
            vel.z = new ParticleSystem.MinMaxCurve(dir.z * spd * 0.7f, dir.z * spd * 1.3f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.25f + 0.4f * _d.Gust01);
            float ashShare = (snowG + ashG) > 0.001f ? ashG / (snowG + ashG) : (w.ashfall > w.snow ? 1f : 0f);
            var c = Color.Lerp(snowColor, ashColor, ashShare); c.a = alpha;
            var m = _ps.main; m.startColor = c; m.maxParticles = maxParticles;
            var sh = _ps.shape; sh.scale = new Vector3(radius * 2f, layerHeight, radius * 2f); sh.position = new Vector3(0f, layerHeight * 0.5f, 0f);
            if (!_psr) _psr = _ps.GetComponent<ParticleSystemRenderer>(); if (_psr) _psr.velocityScale = streak;
        }

        ParticleSystemRenderer _psr;
        void OnDestroy() { if (_ps) Destroy(_ps.gameObject); }
    }
}
