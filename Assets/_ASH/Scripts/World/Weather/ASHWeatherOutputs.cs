using System.Collections.Generic;
using ASH.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ASH.World.Weather
{
    /// <summary>
    /// Pushes ASHWeatherDirector's blended values into the systems that already exist - nothing here renders on its own:
    /// ASHAshFall (falling ash rate / wind / flake size), rain and snow particle rigs (Enviro 3 prefabs or ASHAshFall clones),
    /// ASHDayNightClock (sun / ambient scale, haze floor, lightning flash), RenderSettings fog density (never thinner than tuned),
    /// a runtime copy of the skybox material, a runtime weather Volume (priority above the scene volumes, weight = Atmosphere
    /// Weight: Color Adjustments, Vignette and the ASH Volumetric Fog override - the profile assets are never edited), the
    /// ambience loops and thunder/lightning. Indoors (inside Interior) falling particles are killed and outdoor audio is muffled.
    /// Everything it changes is restored when it is disabled.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ASHWeatherDirector))]
    [DefaultExecutionOrder(-40)]
    public sealed class ASHWeatherOutputs : MonoBehaviour
    {
        [Header("Existing systems")]
        public ASHDayNightClock clock;
        public ASHAshFall ashFall;
        [Tooltip("Root of the rain particle rig (all child particle systems are scaled by rain intensity).")]
        public GameObject rainRig;
        public GameObject snowRig;
        [Tooltip("Trigger volume of the cabin interior: particles are killed inside, outdoor audio muffled.")]
        public BoxCollider interior;
        public ASHPhase2AAudio audioBank;
        [Tooltip("Optional lightning bolt prefab (Enviro 3 LightningStrike); flash-only when empty.")]
        public GameObject lightningPrefab;

        [Header("Mapping")]
        [Tooltip("Ash particles per second at Ashfall 1.")]
        [Range(0f, 50000f)] public float ashParticlesAtFull = 4000f;
        [Tooltip("Rig emission multiplier at Rain/Snow 1 (1 = the rig's own authored rate).")]
        [Range(0f, 10f)] public float rainRateScale = 1.5f, snowRateScale = 1.5f;
        [Tooltip("Particle drift per m/s of wind.")] [Range(0f, 2f)] public float particleWindFactor = 0.35f;
        [Range(0f, 1f)] public float indoorParticleScale = 0f;
        [Range(100f, 22000f)] public float indoorLowPassHz = 1100f;
        [Range(0f, 1f)] public float indoorVolume = 0.5f;
        [Tooltip("Weather Volume priority (scene volumes are 0, ASH post is 10).")]
        [Range(0f, 1000f)] public float volumePriority = 50f;
        [Header("Wind (shared with the director's gusts)")]
        [Tooltip("How much gusts swell the wind loops (0 = steady).")] [Range(0f, 2f)] public float gustSwell = 0.8f;
        [Tooltip("Sideways push on falling snow per m/s of wind.")] [Range(0f, 10f)] public float snowWindFactor = 2f;
        [Tooltip("Sideways push on rain per m/s of wind.")] [Range(0f, 10f)] public float rainWindFactor = 0.6f;
        [Header("Clouds / blowing snow (added at runtime when missing)")]
        public Shader cloudShader;
        public bool cloudLayer = true;
        public bool blowingSnow = true;

        [Header("Lightning")]
        public Vector2 strikeDistance = new Vector2(250f, 1200f);
        [Range(0f, 10f)] public float flashStrength = 3f;
        [Range(0f, 1f)] public float thunderVolume = 0.9f;

        ASHWeatherDirector _d; Volume _vol; VolumeProfile _prof; ColorAdjustments _ca; Vignette _vig; ASHVolumetricFog _fog;
        float _fogDensity0, _fogEnd0; FogMode _fogMode0; bool _fogCaptured;
        Material _sky0, _sky; float _skyExp0 = 1f, _skyAtm0 = 1f; Color _skyTint0 = Color.gray; bool _skyHasExp, _skyHasTint, _skyHasAtm;
        readonly List<(ParticleSystem ps, float rate)> _rain = new List<(ParticleSystem, float)>(), _snow = new List<(ParticleSystem, float)>();
        AudioSource _windS, _windM, _windH, _rainS, _rainH, _amb; readonly List<AudioLowPassFilter> _lp = new List<AudioLowPassFilter>();
        readonly List<(Light l, float i)> _otherSuns = new List<(Light, float)>();
        ASHCloudLayer _clouds;
        float _indoor, _nextParticleSync, _lastAshRate = -1f, _flash; Transform _player, _cam;
        public float Indoor01 => _indoor;
        public int Strikes { get; private set; }
        public string LastThunder { get; private set; } = "";
        public float Flash => _flash;

        void Awake() { _d = GetComponent<ASHWeatherDirector>(); }

        void Start()
        {
            if (!clock) clock = FindAnyObjectByType<ASHDayNightClock>();
            if (!ashFall) ashFall = FindAnyObjectByType<ASHAshFall>();
            var cc = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>(); _player = cc ? cc.transform : null;
            CollectRig(rainRig, _rain); CollectRig(snowRig, _snow);
            if (interior) foreach (var ps in AllFalling()) { var tr = ps.trigger; tr.enabled = true; tr.SetCollider(0, interior); tr.inside = ParticleSystemOverlapAction.Kill; tr.outside = ParticleSystemOverlapAction.Ignore; tr.enter = ParticleSystemOverlapAction.Kill; tr.radiusScale = 1f; }
            // directional lights the clock does not drive (e.g. a second scene sun) get the same weather scale
            var clockSun = RenderSettings.sun;
            foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.type == LightType.Directional && l != clockSun && l.isActiveAndEnabled) _otherSuns.Add((l, l.intensity));
            BuildVolume(); CaptureFog(); CaptureSky(); BuildAudio();
            if (cloudLayer) { _clouds = GetComponent<ASHCloudLayer>(); if (!_clouds) _clouds = gameObject.AddComponent<ASHCloudLayer>(); if (!_clouds.shader) _clouds.shader = cloudShader; }
            if (blowingSnow && !GetComponent<ASHSpindrift>()) gameObject.AddComponent<ASHSpindrift>();
            Push(true);
        }

        IEnumerable<ParticleSystem> AllFalling()
        {
            if (ashFall) foreach (var p in ashFall.GetComponentsInChildren<ParticleSystem>(true)) yield return p;
            foreach (var r in _rain) yield return r.ps; foreach (var s in _snow) yield return s.ps;
        }

        static void CollectRig(GameObject root, List<(ParticleSystem, float)> list)
        {
            list.Clear(); if (!root) return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var em = ps.emission; float r = em.rateOverTime.constantMax; if (r <= 0f) r = em.rateOverTime.constant;
                list.Add((ps, r)); var m = ps.main; m.playOnAwake = true; em.rateOverTime = 0f; m.simulationSpace = ParticleSystemSimulationSpace.World;
            }
        }

        void BuildVolume()
        {
            var go = new GameObject("ASH_WeatherVolume") { hideFlags = HideFlags.DontSave }; go.transform.SetParent(transform, false);
            _vol = go.AddComponent<Volume>(); _vol.isGlobal = true; _vol.priority = volumePriority; _vol.weight = 0f;
            _prof = ScriptableObject.CreateInstance<VolumeProfile>(); _prof.name = "ASH_Weather (runtime)"; _prof.hideFlags = HideFlags.DontSave;
            _ca = _prof.Add<ColorAdjustments>(false); _vig = _prof.Add<Vignette>(false); _fog = _prof.Add<ASHVolumetricFog>(false);
            _vol.sharedProfile = _prof;
        }

        void CaptureFog() { _fogDensity0 = RenderSettings.fogDensity; _fogEnd0 = RenderSettings.fogEndDistance; _fogMode0 = RenderSettings.fogMode; _fogCaptured = true; }

        void CaptureSky()
        {
            _sky0 = RenderSettings.skybox; if (!_sky0) return;
            _sky = new Material(_sky0) { name = _sky0.name + " (weather)", hideFlags = HideFlags.DontSave };
            _skyHasExp = _sky.HasProperty("_Exposure"); _skyHasTint = _sky.HasProperty("_SkyTint"); _skyHasAtm = _sky.HasProperty("_AtmosphereThickness");
            if (_skyHasExp) _skyExp0 = _sky.GetFloat("_Exposure"); if (_skyHasTint) _skyTint0 = _sky.GetColor("_SkyTint"); if (_skyHasAtm) _skyAtm0 = _sky.GetFloat("_AtmosphereThickness");
            RenderSettings.skybox = _sky;
        }

        AudioSource Loop(string n, AudioClip c)
        {
            if (!c) return null;
            var go = new GameObject(n); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.spatialBlend = 0f; s.volume = 0f; s.playOnAwake = false; s.dopplerLevel = 0f;
            _lp.Add(go.AddComponent<AudioLowPassFilter>());
            s.time = Random.Range(0f, Mathf.Max(0f, c.length - 1f)); s.Play(); return s;
        }

        void BuildAudio()
        {
            var a = audioBank; if (!a) return;
            _windS = Loop("Wind_Soft", a.windSoft); _windM = Loop("Wind_Strong", a.windStrong); _windH = Loop("Wind_Howl", a.windHowl);
            _rainS = Loop("Rain_Soft", a.rainSoft); _rainH = Loop("Rain_Strong", a.rainStrong); _amb = Loop("Morning_Ambience", a.morningAmbience);
        }

        void OnDisable()
        {
            if (_fogCaptured) { RenderSettings.fogDensity = _fogDensity0; RenderSettings.fogEndDistance = _fogEnd0; RenderSettings.fogMode = _fogMode0; }
            if (_sky0) RenderSettings.skybox = _sky0;
            foreach (var (l, i) in _otherSuns) if (l) l.intensity = i;
            if (clock) { clock.weatherSunScale = 1f; clock.weatherAmbientScale = 1f; clock.weatherHazeMin = 0f; }
            if (_vol) Destroy(_vol.gameObject); if (_prof) Destroy(_prof); if (_sky) Destroy(_sky);
        }

        void Update()
        {
            if (!_cam || !_cam.gameObject.activeInHierarchy) { var c = Camera.main; _cam = c ? c.transform : null; }
            bool inside = interior && _player && interior.bounds.Contains(_player.position + Vector3.up * 0.5f) && (interior.ClosestPoint(_player.position + Vector3.up * 0.5f) - (_player.position + Vector3.up * 0.5f)).sqrMagnitude < 1e-4f;
            _indoor = Mathf.MoveTowards(_indoor, inside ? 1f : 0f, Time.deltaTime * 1.5f);
            Thunder();
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 6f);
            Push(false);
        }

        void Push(bool force)
        {
            var w = _d.Current; float atm = w.atmosphereWeight;
            // post + volumetric fog (runtime volume, blended by weight over the scene volumes)
            if (_vol)
            {
                _vol.weight = atm;
                _ca.postExposure.Override(w.postExposure + _flash * 0.6f); _ca.saturation.Override(w.saturation); _ca.colorFilter.Override(w.colorFilter);
                _vig.intensity.Override(w.vignette);
                _fog.enabled.Override(true); _fog.densityMultiplier.Override(w.volFogDensity); _fog.albedo.Override(w.volFogAlbedo);
                _fog.noiseStrength.Override(w.volFogNoise); _fog.lightIntensity.Override(w.volFogLight);
                _fog.windSpeed.Override(Mathf.Min(100f, _d.WindMS)); _fog.windDirection.Override(Mathf.Repeat(Mathf.Atan2(_d.WindDir2.x, _d.WindDir2.y) * Mathf.Rad2Deg, 360f));
            }
            // lighting (the clock stays the owner; we only scale)
            if (clock)
            {
                clock.weatherSunScale = (1f - 0.9f * w.cloudDarkness) * (1f + _flash * flashStrength);
                clock.weatherAmbientScale = w.ambientScale * (1f + _flash * flashStrength * 0.5f);
                clock.weatherHazeMin = w.haze;
            }
            float sunK = (1f - 0.9f * w.cloudDarkness) * (1f + _flash * flashStrength);
            foreach (var (l, i) in _otherSuns) if (l) l.intensity = i * sunK;
            // scene fog: visibility (never thinner than tuned)
            if (_fogCaptured)
            {
                float v = Mathf.Max(1f, w.visibility);
                switch (_fogMode0)
                {
                    case FogMode.ExponentialSquared: RenderSettings.fogDensity = Mathf.Max(_fogDensity0, 1.978f / v); break;
                    case FogMode.Exponential: RenderSettings.fogDensity = Mathf.Max(_fogDensity0, 3.912f / v); break;
                    default: RenderSettings.fogEndDistance = Mathf.Min(_fogEnd0, v); break;
                }
            }
            // sky (runtime material copy)
            if (_sky)
            {
                float dark = w.cloudDarkness, tint = w.skyAshTint;
                if (_skyHasExp) _sky.SetFloat("_Exposure", _skyExp0 * w.skyExposureScale * (1f + _flash * 2f));
                if (_skyHasTint) _sky.SetColor("_SkyTint", Color.Lerp(_skyTint0, new Color(0.42f, 0.4f, 0.38f), tint));
                if (_skyHasAtm) _sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(_skyAtm0, Mathf.Max(_skyAtm0, 2.2f), dark * tint));
            }
            // particles (throttled: particle module writes are not free)
            float inScale = Mathf.Lerp(1f, indoorParticleScale, _indoor);
            if (force || Time.time >= _nextParticleSync)
            {
                _nextParticleSync = Time.time + 0.2f;
                var wind = new Vector3(_d.WindDir2.x, 0f, _d.WindDir2.y) * _d.WindMS * particleWindFactor;
                float rate = ashParticlesAtFull * _d.AirborneAsh * inScale;
                if (ashFall && (force || Mathf.Abs(rate - _lastAshRate) > 0.03f * Mathf.Max(50f, _lastAshRate) || _d.Transitioning)) { ashFall.SetWeather(rate, wind, w.ashFlakeSize, force && rate < 1f); _lastAshRate = rate; }
                var windRaw = new Vector3(_d.WindDir2.x, 0f, _d.WindDir2.y) * _d.WindMS;
                Rig(_rain, rainRig, w.rain * rainRateScale * inScale, windRaw * rainWindFactor);
                Rig(_snow, snowRig, w.snow * snowRateScale * inScale, windRaw * snowWindFactor);
            }
            if (_cam) { if (rainRig) rainRig.transform.position = _cam.position + Vector3.up * 12f; if (snowRig) snowRig.transform.position = _cam.position + Vector3.up * 12f; }
            // audio
            if (_clouds) _clouds.Drive(w.cloudCoverage, w.cloudDarkness, _d.WindDir2, _d.WindMS, Time.deltaTime);
            float wv = Mathf.Clamp01(w.wind * _d.windIntensity * (1f + 0.3f * _d.Gust01)), g = Mathf.Lerp(1f, 0.55f + 1.1f * _d.Gust01, gustSwell), amb = audioBank ? audioBank.ambienceMaster : 1f;
            float ind = Mathf.Lerp(1f, indoorVolume, _indoor);
            Vol(_windS, w.windVolume * (1f - Smooth(0.35f, 0.7f, wv)) * g * amb * ind);
            Vol(_windM, w.windVolume * Smooth(0.25f, 0.6f, wv) * (1f - Smooth(0.75f, 1f, wv)) * g * amb * ind);
            Vol(_windH, w.windVolume * Smooth(0.7f, 1f, wv) * g * amb * ind);
            Vol(_rainS, w.rainVolume * (1f - Smooth(0.4f, 0.8f, w.rain)) * Mathf.Clamp01(w.rain * 4f) * amb * ind);
            Vol(_rainH, w.rainVolume * Smooth(0.4f, 0.8f, w.rain) * amb * ind);
            Vol(_amb, w.ambienceVolume * Mathf.Clamp01(1f - _d.AirborneAsh * 1.5f) * amb * Mathf.Lerp(1f, 0.6f, _indoor));
            float wp = 0.94f + 0.14f * _d.Gust01; if (_windS) _windS.pitch = wp; if (_windM) _windM.pitch = wp; if (_windH) _windH.pitch = wp;
            float cut = Mathf.Lerp(22000f, indoorLowPassHz, _indoor); foreach (var lp in _lp) if (lp) lp.cutoffFrequency = cut;
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));
        static void Vol(AudioSource s, float v) { if (!s) return; s.volume = Mathf.MoveTowards(s.volume, Mathf.Clamp01(v * ASH.Core.ASHGameSettings.CachedAmbience), Time.deltaTime * 0.5f); }

        static void Rig(List<(ParticleSystem ps, float rate)> list, GameObject root, float k, Vector3 wind)
        {
            if (!root) return;
            foreach (var (ps, rate) in list)
            {
                var em = ps.emission; em.rateOverTime = rate * k;
                if (k > 0.001f && !ps.isPlaying) ps.Play(true);
                var f = ps.forceOverLifetime; f.enabled = true; f.space = ParticleSystemSimulationSpace.World; f.x = wind.x; f.z = wind.z; f.y = 0f;
            }
        }

        // ---- thunder and lightning
        void Thunder()
        {
            float perMin = _d.Current.thunderPerMinute; if (perMin <= 0.01f) return;
            if (Random.value < perMin / 60f * Time.deltaTime) Strike();
        }

        /// <summary>Lightning flash (+ bolt when a prefab is set) and distance-delayed thunder.</summary>
        public void Strike()
        {
            var c = _player ? _player.position : (_cam ? _cam.position : transform.position);
            float dist = Random.Range(strikeDistance.x, strikeDistance.y); var dir = Random.insideUnitCircle.normalized;
            var ground = c + new Vector3(dir.x, 0f, dir.y) * dist; var t = Terrain.activeTerrain; if (t) ground.y = t.SampleHeight(ground) + t.transform.position.y;
            _flash = Mathf.Clamp01(1.2f - dist / (strikeDistance.y * 1.5f)); Strikes++;
            if (lightningPrefab) SpawnBolt(ground + Vector3.up * 900f, ground);
            var clip = audioBank ? ASH.Gameplay.ASHSfx.Pick(audioBank.thunder) : null;
            if (clip) StartCoroutine(PlayThunder(clip, Mathf.Min(5f, dist / 343f), ground, dist));
        }

        System.Collections.IEnumerator PlayThunder(AudioClip clip, float delay, Vector3 at, float dist)
        {
            yield return new WaitForSeconds(delay);
            var go = new GameObject("ASH_Thunder"); go.transform.position = at; var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.spatialBlend = 0.35f; s.minDistance = 400f; s.maxDistance = 5000f; s.dopplerLevel = 0f; s.pitch = Random.Range(0.85f, 1.05f);
            s.volume = thunderVolume * Mathf.Lerp(1f, 0.45f, Mathf.InverseLerp(strikeDistance.x, strikeDistance.y, dist)) * Mathf.Lerp(1f, indoorVolume + 0.2f, _indoor) * (audioBank ? audioBank.sfxMaster : 1f);
            s.Play(); LastThunder = clip.name; Destroy(go, clip.length / s.pitch + 0.2f);
        }

        void SpawnBolt(Vector3 from, Vector3 to)
        {
            // instantiate inactive, give the bolt runtime material copies (the vendor script writes material values), then fire
            var holder = new GameObject("ASH_LightningHolder"); holder.SetActive(false);
            var bolt = Instantiate(lightningPrefab, from, Quaternion.identity, holder.transform);
            Component comp = null; foreach (var mb in bolt.GetComponentsInChildren<MonoBehaviour>(true)) if (mb && mb.GetType().Name == "Lightning") { comp = mb; break; }
            if (comp != null)
            {
                var ty = comp.GetType();
                var tf = ty.GetField("target"); if (tf != null && tf.FieldType == typeof(Vector3)) tf.SetValue(comp, to);
                var pm = ty.GetField("planeMat"); var orig = pm?.GetValue(comp) as Material;
                if (orig) { var copy = new Material(orig); pm.SetValue(comp, copy); foreach (var r in bolt.GetComponentsInChildren<Renderer>(true)) if (r.sharedMaterial == orig) r.sharedMaterial = copy; }
            }
            holder.SetActive(true); bolt.transform.SetParent(null, true); Destroy(holder); Destroy(bolt, 3f);
        }
    }
}
