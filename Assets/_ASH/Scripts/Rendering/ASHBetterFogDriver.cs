using ASH.World;
using ASH.World.Weather;
using INab.BetterFog.URP;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.Rendering
{
    /// <summary>
    /// Drives the INab Better Fog override on ASH_Fog's global Volume from ASHBetterFogSettings, the ASH weather and time of day.
    /// Runs in Edit mode too ([ExecuteAlways]): Edit mode shows the settings' preview weather (Clear / Ash Storm Max / Snowstorm / Custom) so edits to
    /// ASH_BetterFogSettings show at once in the Scene/Game view. Both modes write to a temporary copy of the profile (never saved,
    /// the asset is never dirtied); non-driven fields are re-read from ASH_BetterFogProfile every frame in Edit mode.
    /// While enabled (Edit and Play) Unity scene fog and the old ASH Volumetric Fog pass are switched off so nothing stacks; both
    /// are restored on disable. Created by ASH/Systems/Weather/Ensure INab Fog.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ASHBetterFogDriver : MonoBehaviour
    {
        public Volume volume;
        [Tooltip("Empty = Resources/ASH_BetterFogSettings.")] public ASHBetterFogSettings settings;
        [Tooltip("Optional; found automatically.")] public ASHDayNightClock clock;

        // scene fog state to restore (serialized so it survives domain reloads / editor restarts while ASH_Fog is active)
        [SerializeField, HideInInspector] bool _sceneFogCaptured, _sceneFogWasOn;

        VolumeProfile _runtime; BetterFogVolumeComponent _f, _src;
        bool _init, _volOff0, _volCaptured, _logged;
        float _dens, _hDens, _height, _ground, _sky, _noise, _sun, _intens; Color _col;
        Vector3 _noiseOffset;
        /// <summary>Current weather intensity 0 (Clear look) .. 1 (Ash Storm look).</summary>
        public float LastIntensity { get; private set; }

        ASHBetterFogSettings S => settings ? settings : ASHBetterFogSettings.I;

        void OnEnable()
        {
            if (!volume) volume = GetComponent<Volume>();
            if (!volume || !volume.sharedProfile) { Debug.LogWarning("[ASH] FOG ASH_Fog has no Volume or profile - run ASH/Systems/Weather/Ensure INab Fog"); return; }
            _src = null; volume.sharedProfile.TryGet(out _src);
            // deep copy: Instantiate(VolumeProfile) shares the component objects with the asset, so clone each component
            _runtime = ScriptableObject.CreateInstance<VolumeProfile>(); _runtime.name = volume.sharedProfile.name + " (temp)"; _runtime.hideFlags = HideFlags.DontSave;
            foreach (var comp in volume.sharedProfile.components)
            {
                if (!comp) continue;
                var cl = Instantiate(comp); cl.name = comp.name; cl.hideFlags = HideFlags.DontSave; _runtime.components.Add(cl);
            }
            volume.profile = _runtime;
            if (!_runtime.TryGet(out _f)) _f = _runtime.Add<BetterFogVolumeComponent>(true);
            ApplyExclusive();
            ASHBetterFogSettings.Changed += OnSettingsChanged;
            _init = false;
            if (!_logged) { _logged = true; LogStatus(); }
            Tick();
        }

        void OnDisable()
        {
            ASHBetterFogSettings.Changed -= OnSettingsChanged;
            RestoreExclusive();
#if UNITY_EDITOR
            if (Application.isPlaying && _f && S.saveProfileOnStop) SaveProfileToAsset();
#endif
            if (volume) volume.profile = null; // back to the shared profile asset
            if (_runtime)
            {
                foreach (var comp in _runtime.components) if (comp) { if (Application.isPlaying) Destroy(comp); else DestroyImmediate(comp); }
                if (Application.isPlaying) Destroy(_runtime); else DestroyImmediate(_runtime); _runtime = null;
            }
            _f = null; _src = null;
        }

        void OnSettingsChanged()
        {
            _init = false; // snap to the new values instead of blending
            if (!isActiveAndEnabled || !_f) return;
            ApplyExclusive(); Tick();
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEditor.SceneView.RepaintAll(); UnityEditor.EditorApplication.QueuePlayerLoopUpdate(); }
#endif
        }

        /// <summary>Scene fog and old volumetric fog off while ASH_Fog runs (Edit and Play), per the settings toggles.</summary>
        void ApplyExclusive()
        {
            var s = S;
            if (s.replaceSceneFog)
            {
                if (!_sceneFogCaptured) { _sceneFogWasOn = RenderSettings.fog; _sceneFogCaptured = true; }
                if (RenderSettings.fog) RenderSettings.fog = false;
            }
            else if (_sceneFogCaptured) { RenderSettings.fog = _sceneFogWasOn; _sceneFogCaptured = false; }

            if (s.disableOldVolumetricFog)
            {
                if (!_volCaptured) { _volOff0 = ASHVolumetricFogFeature.DebugDisable; _volCaptured = true; }
                ASHVolumetricFogFeature.DebugDisable = true;
            }
            else if (_volCaptured) { ASHVolumetricFogFeature.DebugDisable = _volOff0; _volCaptured = false; }
        }

        void RestoreExclusive()
        {
            if (_sceneFogCaptured) { RenderSettings.fog = _sceneFogWasOn; _sceneFogCaptured = false; }
            if (_volCaptured) { ASHVolumetricFogFeature.DebugDisable = _volOff0; _volCaptured = false; }
        }

        void Update() { Tick(); }

        void Tick()
        {
            if (!_f) return;
            var s = S;
            bool play = Application.isPlaying;
            if (!clock) clock = FindAnyObjectByType<ASHDayNightClock>();
            if (s.replaceSceneFog && RenderSettings.fog) RenderSettings.fog = false; // something else turned it back on

            // Edit mode: refresh non-driven fields from the profile asset so its Inspector edits show live too
            if (!play && _src) CopyParams(_src, _f);

            var d = play ? ASHWeatherDirector.Instance : null; // Edit mode = preview weather from the settings
            Camera cam = Camera.main;
#if UNITY_EDITOR
            if (!cam && !play && UnityEditor.SceneView.lastActiveSceneView) cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            float vis = s.clearVisibility, ash = 0f, snow = 0f, rain = 0f, haze = 0f, dark = 0f, wind = 0f, storm = 0f;
            if (d)
            {
                var w = d.Current;
                vis = Mathf.Max(1f, w.visibility); ash = Mathf.Max(w.ashfall, d.AirborneAsh); snow = w.snow; rain = w.rain;
                haze = w.haze; dark = w.cloudDarkness; wind = d.WindMS; storm = Mathf.Clamp01(w.thunderPerMinute / 4f);
            }
            else if (!play)
            {
                switch (s.preview)
                {
                    case ASHBetterFogSettings.Preview.AshStormMax: ash = 1f; wind = s.stormWind; vis = s.stormVisibility; dark = 0.78f; haze = 0.9f; break;
                    case ASHBetterFogSettings.Preview.Snowstorm: ash = 0.1f; snow = 0.85f; wind = s.stormWind; vis = 120f; dark = 0.55f; haze = 0.5f; break;
                    case ASHBetterFogSettings.Preview.Custom: ash = s.previewAsh; snow = s.previewSnow; wind = s.previewWind; vis = s.previewVisibility; dark = s.previewCloudDarkness; break;
                }
            }
            vis = Mathf.Max(1f, vis);
            float indoor = 0f, flash = 0f;
            var outs = d ? d.GetComponent<ASHWeatherOutputs>() : null;
            if (outs) { indoor = outs.Indoor01; flash = outs.Flash; }
            float hour = play ? (clock ? clock.timeOfDay : 12f) : s.previewHour;
            bool night = play ? (clock && clock.IsNight) : (hour < 5.5f || hour > 19.8f);

            // weather intensity: 0 = Clear (min) look, 1 = Ash Storm (max) look
            float windF = Mathf.Lerp(s.windMinFactor, 1f, Mathf.Clamp01(Mathf.InverseLerp(s.calmWind, Mathf.Max(s.calmWind + 0.01f, s.stormWind), wind)));
            float snowW = Mathf.Clamp01(snow * windF * s.snowStormStrength);
            float visW = Mathf.Clamp01(Mathf.InverseLerp(Mathf.Log(Mathf.Max(1f, s.clearVisibility)), Mathf.Log(Mathf.Max(1f, s.stormVisibility)), Mathf.Log(vis))) * s.visibilityWeight;
            float wi = Mathf.Clamp01(Mathf.Max(ash * windF, Mathf.Max(snowW, visW)));
            wi = Mathf.Pow(wi, s.intensityCurve);
            LastIntensity = wi;

            float dens = Mathf.Lerp(s.clearDensity, s.stormDensity, wi);
            float thick = wi;

            // height fog
            float hd = s.clearHeightDensity;
            hd = Mathf.Lerp(hd, s.heightDensityRain, rain); hd = Mathf.Lerp(hd, s.heightDensitySnow, snow); hd = Mathf.Lerp(hd, s.stormHeightDensity, wi * (1f - snow * 0.5f));
            if (s.morningMistHours.y > s.morningMistHours.x && hour > s.morningMistHours.x && hour < s.morningMistHours.y)
                hd += s.morningMist * Mathf.Sin(Mathf.InverseLerp(s.morningMistHours.x, s.morningMistHours.y, hour) * Mathf.PI);
            hd = Mathf.Clamp(hd * s.heightDensityMultiplier, 0f, 0.5f);
            if (cam)
            {
                var t = Terrain.activeTerrain; var p = cam.transform.position;
                float g = t ? t.SampleHeight(p) + t.GetPosition().y : p.y - 1.7f;
                _ground = (_init && play) ? Mathf.Lerp(_ground, g, 1f - Mathf.Exp(-s.groundFollow * Time.deltaTime)) : g;
            }
            float height = _ground + s.heightAboveGround + Mathf.Min(ash, wi) * s.stormExtraHeight + snow * s.snowExtraHeight;

            // colour: day / night palette, warmed by the clock's time-of-day hue, tinted by weather; brightness floor; snow -> lighter tint
            float day01 = Mathf.Clamp01(Mathf.InverseLerp(s.dawnHours.x, s.dawnHours.y, hour)) * Mathf.Clamp01(Mathf.InverseLerp(s.duskHours.y, s.duskHours.x, hour));
            Color c = Color.Lerp(s.nightColor, s.dayColor, day01);
            if (play)
            {
                Color tod = RenderSettings.fogColor; float todMax = Mathf.Max(0.001f, tod.maxColorComponent);
                c = Color.Lerp(c, tod / todMax * c.maxColorComponent, s.timeOfDayHue);
            }
            float tw = ash + snow + rain;
            if (tw > 0.001f)
            {
                Color tint = (s.ashTint * ash + s.snowTint * snow + s.rainTint * rain) / tw;
                tint /= Mathf.Max(0.001f, tint.maxColorComponent);
                c = Color.Lerp(c, tint * c.maxColorComponent, Mathf.Clamp01(tw) * s.tintStrength);
            }
            float bright = c.maxColorComponent * s.colorMultiplier * (1f - s.cloudDarken * dark);
            bright = Mathf.Max(bright, Mathf.Lerp(s.minBrightnessNight, s.minBrightnessDay, day01));
            bright = Mathf.Min(1f, bright);
            c = c / Mathf.Max(0.001f, c.maxColorComponent) * bright;
            float snowK = Mathf.Clamp01(snow) * s.snowTintBlend;
            if (snowK > 0f)
            {
                Color st = s.snowStormTint * Mathf.Lerp(0.25f, 1f, day01); // dimmer at night, full by day
                c = Color.Lerp(c, st, snowK);
            }
            c *= 1f + flash * s.flashBoost; c.a = 1f;

            dens *= Mathf.Lerp(s.nightDensityMultiplier, 1f, day01);
            dens = Mathf.Min(dens, 0.1f);
            float intens = Mathf.Lerp(s.clearIntensity, s.stormIntensity, wi) * Mathf.Lerp(1f, s.indoorIntensity, indoor);
            float sky = Mathf.Lerp(s.clearSkyFill, s.stormSkyFill, Mathf.Max(thick, haze * 0.5f * s.visibilityWeight));
            float noise = Mathf.Lerp(s.clearNoise, s.stormNoise, Mathf.Max(wi, storm));
            float sun = night ? 0f : Mathf.Lerp(s.clearSunIntensity, s.stormSunIntensity, wi);

            float k = (_init && play) ? 1f - Mathf.Exp(-s.blendPerSecond * Time.deltaTime) : 1f;
            _dens = Mathf.Lerp(_dens, dens, k); _hDens = Mathf.Lerp(_hDens, hd, k); _height = Mathf.Lerp(_height, height, k);
            _col = Color.Lerp(_col, c, k); _sky = Mathf.Lerp(_sky, sky, k); _noise = Mathf.Lerp(_noise, noise, k);
            _sun = Mathf.Lerp(_sun, sun, k); _intens = Mathf.Lerp(_intens, intens, k);
            _init = true;

            if (d) { var w2 = d.WindDir2; _noiseOffset = new Vector3(w2.x, 0f, w2.y) * (wind * s.noiseWindSpeed); } else _noiseOffset = new Vector3(0.7f, 0f, 0.7f) * (wind * s.noiseWindSpeed);

            if (play && !s.driveFromWeather) return; // manual Inspector values on the runtime profile win (Play)

            _f._FogIntensity.Override(Mathf.Clamp01(_intens));
            _f._UseDistanceFog.Override(true);
            _f._FogType.Override(FogMode.ExponentialSquared);
            _f._FogDensity.Override(Mathf.Clamp(_dens, 0f, 0.1f));
            _f._DistanceFogOffset.Override(s.startOffset);
            _f._FogColor.Override(_col);
            _f._UseGradient.Override(false);
            _f._UseHeightFog.Override(s.heightFog);
            _f._Height.Override(_height);
            _f._HeightDensity.Override(_hDens);
            _f._UseSkyboxHeightFog.Override(s.skyboxFog);
            _f._SkyboxFill.Override(_sky);
            _f._SkyboxFogHardness.Override(s.skyHardness);
            _f._UseSunLight.Override(s.sunScatter);
            _f._SunIntensity.Override(_sun);
            _f._SunPower.Override(s.sunPower);
            if (RenderSettings.sun) _f._SunColor.Override(RenderSettings.sun.color);
            _f._UseNoise.Override(s.noise);
            _f._NoiseIntensity.Override(_noise);
            _f._Scale1.Override(s.noiseScale);
            _f._NoiseDistanceEnd.Override(s.noiseDistanceEnd);
            _f._NoiseSpeed1.Override(_noiseOffset);
        }

        static void CopyParams(BetterFogVolumeComponent from, BetterFogVolumeComponent to)
        {
            foreach (var fi in typeof(BetterFogVolumeComponent).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!typeof(VolumeParameter).IsAssignableFrom(fi.FieldType) || Driven.Contains(fi.Name)) continue;
                var a = fi.GetValue(from) as VolumeParameter; var b = fi.GetValue(to) as VolumeParameter;
                if (a == null || b == null) continue;
                b.SetValue(a); b.overrideState = a.overrideState;
            }
            to.active = from.active;
        }

        /// <summary>One line: active pipeline/renderer, Better Fog feature found/active, profile override found, other fog states.</summary>
        public void LogStatus()
        {
            string rp = "none", rend = "?", feat = "NOT FOUND";
            var pipe = GraphicsSettings.currentRenderPipeline;
            if (pipe)
            {
                rp = pipe.name;
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                var list = pipe.GetType().GetField("m_RendererDataList", F)?.GetValue(pipe) as System.Array;
                int idx = (int)(pipe.GetType().GetField("m_DefaultRendererIndex", F)?.GetValue(pipe) ?? 0);
                var cam = Camera.main;
                if (cam)
                {
                    var acd = cam.GetComponent("UniversalAdditionalCameraData");
                    if (acd) { var ri = acd.GetType().GetField("m_RendererIndex", F)?.GetValue(acd); if (ri is int i && i >= 0) idx = i; }
                }
                var data = list != null && idx >= 0 && idx < list.Length ? list.GetValue(idx) as UnityEngine.Rendering.Universal.ScriptableRendererData : null;
                if (data)
                {
                    rend = data.name;
                    foreach (var rf in data.rendererFeatures) if (rf is BetterFog) { feat = rf.isActive ? "found+active" : "found but INACTIVE"; break; }
                }
            }
            bool ovr = _f != null; bool srcOvr = volume && volume.sharedProfile && volume.sharedProfile.Has<BetterFogVolumeComponent>();
            Debug.Log($"[ASH] FOG start ({(Application.isPlaying ? "Play" : "Edit preview")}): quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} pipeline={rp} renderer={rend} BetterFogFeature={feat} profileOverride={(srcOvr ? "found" : "MISSING")}{(ovr ? "" : " (no temp copy)")} volumePriority={(volume ? volume.priority : 0f)} sceneFog=off(was {(_sceneFogWasOn ? "on" : "off")}) oldVolFog={(ASHVolumetricFogFeature.DebugDisable ? "off" : "ON")} settings={(settings ? settings.name : "Resources/ASH_BetterFogSettings")}");
        }

        /// <summary>Fields this driver writes every frame while driveFromWeather is on (never saved back then).</summary>
        static readonly System.Collections.Generic.HashSet<string> Driven = new System.Collections.Generic.HashSet<string>
        {
            "_FogIntensity", "_UseDistanceFog", "_FogType", "_FogDensity", "_DistanceFogOffset", "_FogColor", "_UseGradient",
            "_UseHeightFog", "_Height", "_HeightDensity", "_UseSkyboxHeightFog", "_SkyboxFill", "_SkyboxFogHardness",
            "_UseSunLight", "_SunIntensity", "_SunPower", "_SunColor", "_UseNoise", "_NoiseIntensity", "_Scale1", "_NoiseDistanceEnd", "_NoiseSpeed1",
        };

        /// <summary>Play mode: copies the runtime profile's Better Fog values (your Inspector edits) back into the profile asset.
        /// Driven fields are skipped while driveFromWeather is on. Returns how many fields were written.</summary>
        public int SaveProfileToAsset()
        {
#if UNITY_EDITOR
            if (!_f || !volume || !volume.sharedProfile || !volume.sharedProfile.TryGet(out BetterFogVolumeComponent dst)) return 0;
            bool skipDriven = S.driveFromWeather; int n = 0;
            foreach (var fi in typeof(BetterFogVolumeComponent).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!typeof(VolumeParameter).IsAssignableFrom(fi.FieldType)) continue;
                if (skipDriven && Driven.Contains(fi.Name)) continue;
                var src = fi.GetValue(_f) as VolumeParameter; var d = fi.GetValue(dst) as VolumeParameter;
                if (src == null || d == null) continue;
                d.SetValue(src); d.overrideState = src.overrideState; n++;
            }
            UnityEditor.EditorUtility.SetDirty(dst); UnityEditor.EditorUtility.SetDirty(volume.sharedProfile);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(volume.sharedProfile);
            Debug.Log($"[ASH] FOG profile saved from Play: {n} fields -> {UnityEditor.AssetDatabase.GetAssetPath(volume.sharedProfile)}" + (skipDriven ? " (driven fields skipped)" : ""));
            return n;
#else
            return 0;
#endif
        }
    }
}

