using System;
using ASH.Rendering;
using ASH.World;
using ASH.World.Accumulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.Core
{
    /// <summary>
    /// Player settings (graphics, audio, controls, difficulty) stored in PlayerPrefs and shared by the main menu and the
    /// pause menu. Values that would override the scene's own tuning (fog quality, snow quality, banks) default to
    /// "Scene default" (-1) so nothing changes until the player picks something.
    /// </summary>
    public static class ASHGameSettings
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHGameSettings()
        {
            Changed = null;
            CachedSensitivity = 1f;
            CachedSfx = 1f;
            CachedMusic = 1f;
            CachedAmbience = 1f;
            CachedExposure = 1f;
            CachedParticles = 1f;
            CachedInvertY = default;
            _baseAshRate = -1f;
            _ashRef = default;
            _fogVol = default;
            _fogProf = default;
        }

        const string P = "ASH.Settings.";
        public static event Action Changed;

        // ---- graphics
        public static int Quality { get => I("Quality", QualitySettings.GetQualityLevel()); set => SetI("Quality", value); }
        public static int ResolutionIndex { get => I("Resolution", -1); set => SetI("Resolution", value); }
        public static bool Fullscreen { get => I("Fullscreen", Screen.fullScreen ? 1 : 0) == 1; set => SetI("Fullscreen", value ? 1 : 0); }
        public static bool VSync { get => I("VSync", QualitySettings.vSyncCount > 0 ? 1 : 0) == 1; set => SetI("VSync", value ? 1 : 0); }
        /// <summary>0 = unlimited.</summary>
        public static int FpsCap { get => I("FpsCap", 0); set => SetI("FpsCap", value); }
        /// <summary>-1 scene default, else ASHFogQuality (Low..Ultra).</summary>
        public static int FogQuality { get => I("FogQuality", -1); set => SetI("FogQuality", value); }
        /// <summary>-1 scene default, else ASHAccumulationController.DeformQuality (Off..Extreme).</summary>
        public static int SnowQuality { get => I("SnowQuality", -1); set => SetI("SnowQuality", value); }
        /// <summary>-1 scene default, 0 off, 1 on.</summary>
        public static int SnowBanks { get => I("SnowBanks", -1); set => SetI("SnowBanks", value); }
        /// <summary>Weather particle amount multiplier (0.1 - 1.5).</summary>
        public static float ParticleAmount { get => F("Particles", 1f); set => SetF("Particles", value); }

        // ---- audio
        public static float MasterVolume { get => F("Master", 1f); set => SetF("Master", value); }
        public static float MusicVolume { get => F("Music", 1f); set => SetF("Music", value); }
        public static float SfxVolume { get => F("Sfx", 1f); set => SetF("Sfx", value); }
        public static float AmbienceVolume { get => F("Ambience", 1f); set => SetF("Ambience", value); }

        // ---- controls
        public static float MouseSensitivity { get => F("Sensitivity", 1f); set => SetF("Sensitivity", value); }
        public static bool InvertY { get => I("InvertY", 0) == 1; set => SetI("InvertY", value ? 1 : 0); }

        // ---- difficulty
        /// <summary>Multiplies ash exposure drain (0.25 - 3).</summary>
        public static float ExposureRate { get => F("ExposureRate", 1f); set => SetF("ExposureRate", value); }

        // cached hot values (read every frame by input/audio)
        public static float CachedSensitivity = 1f, CachedSfx = 1f, CachedMusic = 1f, CachedAmbience = 1f, CachedExposure = 1f, CachedParticles = 1f;
        public static bool CachedInvertY;

        static int I(string k, int d) => PlayerPrefs.GetInt(P + k, d);
        static float F(string k, float d) => PlayerPrefs.GetFloat(P + k, d);
        static void SetI(string k, int v) { PlayerPrefs.SetInt(P + k, v); }
        static void SetF(string k, float v) { PlayerPrefs.SetFloat(P + k, v); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { Apply(false); UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded; }
        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => ApplyScene();

        public static Resolution[] Resolutions
        {
            get
            {
                var all = Screen.resolutions; var list = new System.Collections.Generic.List<Resolution>();
                foreach (var r in all) if (!list.Exists(x => x.width == r.width && x.height == r.height)) list.Add(r);
                return list.ToArray();
            }
        }

        /// <summary>Applies everything (display, audio, scene overrides). display=false skips resolution/fullscreen (startup keeps the player's window).</summary>
        public static void Apply(bool display = true)
        {
            CachedSensitivity = Mathf.Clamp(MouseSensitivity, 0.05f, 5f); CachedInvertY = InvertY;
            CachedSfx = Mathf.Clamp01(SfxVolume); CachedMusic = Mathf.Clamp01(MusicVolume); CachedAmbience = Mathf.Clamp01(AmbienceVolume);
            CachedExposure = Mathf.Clamp(ExposureRate, 0f, 5f); CachedParticles = Mathf.Clamp(ParticleAmount, 0.05f, 2f);
            int q = Mathf.Clamp(Quality, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            if (PlayerPrefs.HasKey(P + "Quality") && q != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(q, true);
            if (PlayerPrefs.HasKey(P + "VSync")) QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FpsCap > 0 ? FpsCap : -1;
            AudioListener.volume = Mathf.Clamp01(MasterVolume);
            if (display)
            {
                var res = Resolutions; int ri = ResolutionIndex;
                if (ri >= 0 && ri < res.Length) Screen.SetResolution(res[ri].width, res[ri].height, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
                else Screen.fullScreen = Fullscreen;
            }
            ApplyScene();
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static float _baseAshRate = -1f; static ASHAshFall _ashRef;
        static Volume _fogVol; static VolumeProfile _fogProf;

        /// <summary>Scene-side overrides: snow quality/banks, weather particle budget, fog quality volume.</summary>
        public static void ApplyScene()
        {
            var acc = ASHAccumulationController.Instance ? ASHAccumulationController.Instance : UnityEngine.Object.FindAnyObjectByType<ASHAccumulationController>();
            if (acc && SnowQuality >= 0) acc.SetDeformationQuality((ASHAccumulationController.DeformQuality)Mathf.Clamp(SnowQuality, 0, (int)ASHAccumulationController.DeformQuality.Extreme));
            if (SnowBanks >= 0) foreach (var s in UnityEngine.Object.FindObjectsByType<ASHAccumulationSurface>()) s.banking = SnowBanks == 1;
            var ash = UnityEngine.Object.FindAnyObjectByType<ASHAshFall>();
            if (ash)
            {
                if (_ashRef != ash) { _ashRef = ash; _baseAshRate = ash.weatherParticleBudget; }
                ash.weatherParticleBudget = Mathf.Clamp(Mathf.RoundToInt(_baseAshRate * CachedParticles), 1000, 200000);
            }
            if (FogQuality >= 0)
            {
                if (!_fogVol)
                {
                    var go = new GameObject("ASH_SettingsFogQuality"); UnityEngine.Object.DontDestroyOnLoad(go);
                    _fogVol = go.AddComponent<Volume>(); _fogVol.isGlobal = true; _fogVol.priority = 500f;
                    _fogProf = ScriptableObject.CreateInstance<VolumeProfile>(); _fogVol.sharedProfile = _fogProf;
                    _fogProf.Add<ASHVolumetricFog>(false);
                }
                if (_fogProf.TryGet<ASHVolumetricFog>(out var f)) f.quality.Override((ASHFogQuality)Mathf.Clamp(FogQuality, 0, (int)ASHFogQuality.Ultra));
            }
            else if (_fogVol) { UnityEngine.Object.Destroy(_fogVol.gameObject); _fogVol = null; }
        }
    }
}
