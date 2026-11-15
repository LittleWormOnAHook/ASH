using System;
using System.Collections.Generic;
using ASH.World.Accumulation;
using UnityEngine;

namespace ASH.World.Weather
{
    /// <summary>
    /// Central weather controller. Holds the current blended ASHWeatherParams (smooth transitions between ASHWeatherProfile
    /// assets) and schedules waves (weighted major events with calm periods between). It owns no rendering of its own:
    /// ASHWeatherOutputs pushes the values into the existing systems (ASHAshFall, ASHDayNightClock, scene fog and sky,
    /// a runtime weather Volume over the scene post stack, ASH Volumetric Fog, ambience). Feeds ASHAccumulationController
    /// as its weather source, so ground ash/snow persist independently of the current weather.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class ASHWeatherDirector : MonoBehaviour, IASHWeatherInputs
    {
        public static ASHWeatherDirector Instance { get; private set; }

        [Header("Profiles (one per state)")]
        public ASHWeatherProfile[] profiles = new ASHWeatherProfile[0];
        [Tooltip("State the scene starts in when no opening sequence takes over.")]
        public ASHWeatherKind initialState = ASHWeatherKind.Clear;

        [Header("Transitions")]
        [Tooltip("Default transition time between states (s).")]
        [Range(1f, 900f)] public float transitionSeconds = 60f;
        public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Scheduler (waves)")]
        [Tooltip("Auto weather. The opening sequence switches this on when it hands over.")]
        public bool schedulerEnabled;
        [Tooltip("Minutes of relative calm (Calm/Hazy or Light Ash) between major events.")]
        public Vector2 calmBetweenWavesMinutes = new Vector2(4f, 9f);
        [Tooltip("0 = random every session; otherwise a deterministic sequence.")]
        public int seed;

        [Header("Global limits and scales")]
        [Tooltip("Visibility never goes below / above these (m), whatever the profile says.")]
        public Vector2 visibilityLimits = new Vector2(25f, 8000f);
        [Range(0f, 3f)] public float windIntensity = 1f;
        [Tooltip("World direction the wind blows towards (degrees, 0 = +Z north, 90 = +X east).")]
        [Range(0f, 360f)] public float windDirection = 80f;
        [Tooltip("Slow wander of the wind direction (degrees).")] [Range(0f, 90f)] public float windWander = 20f;
        [Tooltip("Ground accumulation rate (fraction of a full layer per minute at full fall). < 0 keeps the accumulation controller's own value.")]
        [Range(-1f, 2f)] public float accumulationRate = 0.08f;
        [Tooltip("Wind lifting settled ash back into the air (extra airborne ash at full wind on a full layer; damped by wetness).")]
        [Range(0f, 1f)] public float windLift = 0.25f;
        [Tooltip("Shapes the gust noise (x = raw 0-1 noise, y = gust 0-1). Default: mostly calm with occasional swells. Shared by wind audio, falling snow/ash drift, spindrift and fog wind.")]
        public AnimationCurve gustCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.55f, 0.15f), new Keyframe(0.8f, 0.55f), new Keyframe(1f, 1f));
        [Tooltip("How fast gusts come and go (higher = more frequent).")] [Range(0.01f, 5f)] public float gustFrequency = 0.35f;
        [Tooltip("Gust strength on top of the steady wind (x gustiness of the state).")] [Range(0f, 4f)] public float gustStrength = 1f;

        [Header("Debug")]
        [Tooltip("Force a state (testing). Overrides the scheduler and the opening sequence.")]
        public bool debugOverride;
        public ASHWeatherKind debugState = ASHWeatherKind.HeavyAsh;
        [Range(0.5f, 120f)] public float debugTransitionSeconds = 6f;

        // ---- state
        ASHWeatherParams _from, _to, _cur; float _t = 1f, _dur = 1f; ASHWeatherKind _fromKind, _toKind;
        bool _debugWas; ASHWeatherKind _debugKindWas; float _phaseUntil; bool _inMajor;
        System.Random _rng; float _gustPhase;

        public ASHWeatherParams Current => _cur;
        public ASHWeatherKind CurrentKind => _t >= 1f ? _toKind : (_t < 0.5f ? _fromKind : _toKind);
        public ASHWeatherKind TargetKind => _toKind;
        public float Blend01 => _t;
        public bool Transitioning => _t < 1f;
        /// <summary>Airborne ash including wind-lifted ground ash (0-1+).</summary>
        public float AirborneAsh { get; private set; }
        public float Gust { get; private set; }
        /// <summary>Shaped gust 0-1 (0 = lull, 1 = peak), for audio swells and spindrift bursts.</summary>
        public float Gust01 { get; private set; }
        public Vector2 WindDir2 { get; private set; } = new Vector2(1f, 0f);
        public float WindMS => _cur.wind * _cur.windSpeed * windIntensity * (1f + Gust);
        public event Action<ASHWeatherKind> StateChanged;

        public ASHWeatherProfile Profile(ASHWeatherKind k) { foreach (var p in profiles) if (p && p.kind == k) return p; return null; }

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Start()
        {
            _rng = seed != 0 ? new System.Random(seed) : new System.Random();
            Snap(initialState);
            var acc = ASHAccumulationController.Instance; if (acc) { acc.weatherSource = this; if (accumulationRate >= 0f) acc.accumulationRate = accumulationRate; }
        }

        /// <summary>Jump to a state immediately (no blend).</summary>
        public void Snap(ASHWeatherKind k)
        {
            var p = Profile(k); var v = p ? p.ToParams() : _cur;
            _from = _to = _cur = v; _fromKind = _toKind = k; _t = 1f; StateChanged?.Invoke(k);
        }

        /// <summary>Blend from the current (possibly mid-blend) values to state k over seconds (&lt;= 0: default).</summary>
        public void TransitionTo(ASHWeatherKind k, float seconds = -1f)
        {
            var p = Profile(k); if (!p) { Debug.LogWarning($"[ASH] Weather: no profile for {k}"); return; }
            _from = _cur; _fromKind = CurrentKind; _to = p.ToParams(); _toKind = k;
            _dur = Mathf.Max(0.05f, seconds > 0f ? seconds : transitionSeconds); _t = 0f;
            StateChanged?.Invoke(k);
        }

        public void SetScheduler(bool on) { schedulerEnabled = on; _phaseUntil = 0f; }

        void Update()
        {
            float dt = Time.deltaTime;
            // debug override
            if (debugOverride && (!_debugWas || _debugKindWas != debugState)) TransitionTo(debugState, debugTransitionSeconds);
            if (!debugOverride && _debugWas && schedulerEnabled) _phaseUntil = 0f;
            _debugWas = debugOverride; _debugKindWas = debugState;
            if (!debugOverride && schedulerEnabled) Schedule();

            if (_t < 1f) { _t = Mathf.Min(1f, _t + dt / _dur); }
            float k = transitionCurve != null && transitionCurve.length > 1 ? Mathf.Clamp01(transitionCurve.Evaluate(_t)) : _t;
            _cur = ASHWeatherParams.Lerp(_from, _to, k);
            _cur.visibility = Mathf.Clamp(_cur.visibility, visibilityLimits.x, visibilityLimits.y);

            // wind: slow direction wander + gusts
            _gustPhase += dt * gustFrequency * (0.4f + _cur.gustiness);
            float raw = Mathf.Clamp01(Mathf.PerlinNoise(_gustPhase, 0.37f) * 0.75f + Mathf.PerlinNoise(_gustPhase * 2.7f, 5.1f) * 0.25f);
            Gust01 = gustCurve != null && gustCurve.length > 1 ? Mathf.Clamp01(gustCurve.Evaluate(raw)) : raw;
            Gust = Gust01 * _cur.gustiness * gustStrength;
            float dir = windDirection + (Mathf.PerlinNoise(Time.time * 0.01f, 7.1f) - 0.5f) * 2f * windWander;
            WindDir2 = new Vector2(Mathf.Sin(dir * Mathf.Deg2Rad), Mathf.Cos(dir * Mathf.Deg2Rad));

            // wind lifting settled ash (damped by wetness / rain)
            var acc = ASHAccumulationController.Instance;
            float ground = acc ? acc.ashAmount : 0f, wet = acc ? acc.wetness : 0f;
            float lift = windLift * Mathf.Clamp01(_cur.wind * windIntensity) * ground * (1f - wet) * (1f - _cur.rain);
            AirborneAsh = _cur.ashfall + lift;
        }

        void Schedule()
        {
            if (_t < 1f || Time.time < _phaseUntil) return;
            ASHWeatherProfile next;
            if (_inMajor) next = Pick(false); else next = Pick(true);
            if (!next) return;
            _inMajor = next.majorEvent;
            float dur = next.majorEvent ? R(next.durationMinutes.x, next.durationMinutes.y) : R(calmBetweenWavesMinutes.x, calmBetweenWavesMinutes.y);
            TransitionTo(next.kind, transitionSeconds);
            _phaseUntil = Time.time + transitionSeconds + dur * 60f;
            Debug.Log($"[ASH] Weather scheduler -> {next.kind} for {dur:F1} min (+{transitionSeconds:F0} s blend)");
        }

        ASHWeatherProfile Pick(bool major)
        {
            float sum = 0f; var list = new List<ASHWeatherProfile>();
            foreach (var p in profiles) if (p && p.majorEvent == major && p.probability > 0f && p.kind != ASHWeatherKind.Clear) { list.Add(p); sum += p.probability; }
            if (list.Count == 0) return null;
            float r = (float)_rng.NextDouble() * sum;
            foreach (var p in list) { r -= p.probability; if (r <= 0f) return p; }
            return list[list.Count - 1];
        }
        float R(float a, float b) => a + (float)_rng.NextDouble() * Mathf.Max(0f, b - a);

        // ---- accumulation feed (pull)
        public ASHWeatherInputs GetWeatherInputs() => new ASHWeatherInputs
        {
            ashfallIntensity = Mathf.Clamp01(_cur.ashfall), snowfallIntensity = Mathf.Clamp01(_cur.snow),
            windDirection = WindDir2, windStrength = Mathf.Clamp01(_cur.wind * windIntensity), temperatureC = _cur.temperatureC,
            rainIntensity = Mathf.Clamp01(_cur.rain)
        };

        public string Describe() => $"{_fromKind}->{_toKind} t={_t:F2} ash={_cur.ashfall:F2} air={AirborneAsh:F2} rain={_cur.rain:F2} snow={_cur.snow:F2} wind={_cur.wind:F2} vis={_cur.visibility:F0}m atm={_cur.atmosphereWeight:F2}";
    }
}
