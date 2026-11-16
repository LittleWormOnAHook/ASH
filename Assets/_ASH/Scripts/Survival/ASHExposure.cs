using ASH.Core;
using ASH.UI;
using ASH.World.Weather;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.Survival
{
    /// <summary>
    /// Ash exposure: outside in bad air (strength per weather kind, ASHExposureSettings) health drains a little and stamina
    /// recovers slower; the wet rag slows it, the gas mask more; shelters (ASHShelterZone, and the cabin interior) cut it by
    /// their sealing (ash-tight = stops). Rates per GAME hour x the difficulty exposure multiplier (Settings). Near 25% health
    /// while exposed: pulsing red screen edge, "Find shelter quickly", coughing. Muffled breathing while a rag/mask is worn.
    /// Added automatically (with ASHFaceProtection and the survival HUD) to the player (ASHSurvivalStats) on scene load.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHExposure : MonoBehaviour
    {
        public static ASHExposure Instance { get; private set; }
        /// <summary>Weather air strength where you are before shelter/protection (smoothed).</summary>
        public float AirStrength { get; private set; }
        /// <summary>Shelter sealing at the player (0 = outside, 1 = ash-tight).</summary>
        public float Sealing { get; private set; }
        public ASHShelterZone Zone { get; private set; }
        /// <summary>Air after shelter (before rag/mask).</summary>
        public float Air => AirStrength * (1f - Sealing);
        public bool AirBad => Air > S.exposureDeadzone;
        /// <summary>Effective exposure after protection and difficulty (drives the drain).</summary>
        public float Exposure { get; private set; }
        public bool Draining => Exposure > S.exposureDeadzone;
        /// <summary>Low health while exposed: red edge pulses.</summary>
        public bool Warning { get; private set; }

        ASHExposureSettings S => ASHExposureSettings.I;
        ASHSurvivalStats _stats; ASHFaceProtection _face;
        bool _init, _warnToasted; float _nextCough; float _prevSealing; ASHShelterZone _prevZone;
        AudioSource _breath; AudioLowPassFilter _lp; ASHWeatherOutputs _wo; float _woLook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        /// <summary>Also attach after the first scene loads: with Enter Play Mode Options (no scene reload) sceneLoaded never fires on Play.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachAfterLoad() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            var st = FindAnyObjectByType<ASHSurvivalStats>(); if (!st) return;
            if (!st.GetComponent<ASHFaceProtection>()) st.gameObject.AddComponent<ASHFaceProtection>();
            if (!st.GetComponent<ASHExposure>()) st.gameObject.AddComponent<ASHExposure>();
        }

        void Awake() { Instance = this; _stats = GetComponent<ASHSurvivalStats>(); _face = GetComponent<ASHFaceProtection>(); }
        void Start() { ASHSurvivalHud.Ensure(this); }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Player.ASHInvectorStaminaLink.ExtraDrainPerSecond = 0f;
            if (ASHCoreSave.LocationOverride == (System.Func<string>)ZoneLabel) ASHCoreSave.LocationOverride = null;
        }

        string ZoneLabel() => Zone && Sealing > 0f ? Zone.label : null;

        float SealingNow(Vector3 feet, out ASHShelterZone zone)
        {
            float s = ASHShelterZone.SealingAt(feet, out zone);
            if (s < 1f)
            {
                // the cabin interior counts as ash-tight even without a zone on it (same indoor check as the weather)
                if (!_wo && Time.unscaledTime >= _woLook) { _woLook = Time.unscaledTime + 3f; _wo = FindAnyObjectByType<ASHWeatherOutputs>(); }
                var box = _wo ? _wo.interior : null;
                if (box)
                {
                    var p = feet + Vector3.up * 0.5f;
                    if (box.bounds.Contains(p) && (box.ClosestPoint(p) - p).sqrMagnitude < 1e-4f && (!zone || zone.volume != box)) { zone = box.GetComponent<ASHShelterZone>(); s = zone ? Mathf.Max(s, zone.sealing) : 1f; }
                }
            }
            return s;
        }

        void Update()
        {
            if (!_stats) return;
            if (_stats.IsDead || ASHSaveSystem.IsRestoring)
            {
                _stats.ExposureRecoveryMultiplier = 1f; Player.ASHInvectorStaminaLink.ExtraDrainPerSecond = 0f; Warning = false; Exposure = 0f;
                UpdateBreathing(); return;
            }
            float dh = ASHGameTime.DeltaHours;
            var dir = ASHWeatherDirector.Instance;
            float target = dir ? S.Strength(dir.CurrentKind) : 0f;
            if (!_init) { _init = true; AirStrength = target; }
            else AirStrength = Mathf.MoveTowards(AirStrength, target, S.weatherBlendPerGameHour * Mathf.Max(dh, Time.deltaTime / 120f));

            Sealing = SealingNow(transform.position, out var zone); Zone = zone;
            if (Sealing > 0f && (_prevSealing <= 0f || zone != _prevZone))
            {
                if (ASHSaveSystem.Instance) ASHSaveSystem.Instance.OnShelterEntered(Sealing);
                ASHCoreSave.LocationOverride = ZoneLabel;
            }
            _prevSealing = Sealing; _prevZone = zone;

            bool airBad = AirBad;
            if (_face) _face.Tick(dh, airBad);
            float prot = _face ? _face.Protection : 0f;
            Exposure = airBad ? Air * (1f - prot) * ASHGameSettings.CachedExposure : 0f;

            if (Draining && dh > 0f && S.healthDrainPerGameHour > 0f) _stats.Damage(S.healthDrainPerGameHour * Exposure * dh, false);
            _stats.ExposureRecoveryMultiplier = Draining ? 1f - S.staminaRecoveryPenalty * Mathf.Clamp01(Exposure) : 1f;
            Player.ASHInvectorStaminaLink.ExtraDrainPerSecond = Draining && Time.deltaTime > 0f ? S.staminaDrainPerGameHour * Exposure * dh / Time.deltaTime : 0f;

            bool low = _stats.Health <= _stats.MaxHealth * S.warnHealthFraction;
            Warning = low && Draining;
            if (Warning && !_warnToasted) { _warnToasted = true; var hud = FindAnyObjectByType<ASHHud>(); if (hud && !string.IsNullOrEmpty(S.warnToast)) hud.Toast(S.warnToast); }
            if (!low || Sealing >= 0.99f) _warnToasted = false;

            // coughing: occasional when outdoors in ash without protection, more often while the wet rag is in its final stretch
            // or at low health
            bool unprotected = airBad && Sealing < 0.5f && (!_face || _face.Worn == ASHFaceProtection.Kind.None) && Exposure >= S.coughExposure;
            bool ragEnding = airBad && _face && _face.Worn == ASHFaceProtection.Kind.Rag && _face.RemainingHours <= S.ragCoughHours;
            bool coughs = unprotected || ragEnding || Warning;
            if (!coughs) _nextCough = Mathf.Max(_nextCough, Time.time + 2f);
            else if (Time.time >= _nextCough)
            {
                var iv = Warning ? S.lowHealthCoughIntervalSeconds : ragEnding ? S.ragEndingCoughIntervalSeconds : S.coughIntervalSeconds;
                _nextCough = Time.time + Random.Range(Mathf.Min(iv.x, iv.y), Mathf.Max(iv.x, iv.y));
                if (S.coughClips != null && S.coughClips.Length > 0)
                    Gameplay.ASHSfx.Play("exposure.cough", S.coughClips, transform.position + Vector3.up * 1.6f, S.coughVolume, 1f, 0.08f, 0f);
            }
            UpdateBreathing();
            UpdateLabored();
        }

        AudioSource _labored; AudioLowPassFilter _laboredLp; float _nextLabored;
        /// <summary>Strained breathing while the rag dries out / the filter runs low, heavier near empty.</summary>
        void UpdateLabored()
        {
            if (!_face || _face.Worn == ASHFaceProtection.Kind.None || !_face.Low || _stats.IsDead || Time.timeScale <= 0f) { _nextLabored = Mathf.Max(_nextLabored, Time.time + 2f); return; }
            var clip = S.laboredClip ? S.laboredClip : S.breathingLoop; if (!clip) return;
            bool mask = _face.Worn == ASHFaceProtection.Kind.Mask;
            float window = mask ? S.filterLowHours : S.ragDegradeHours;
            float k = window > 0f ? Mathf.Clamp01(1f - _face.RemainingHours / window) : 1f;
            if (mask && _face.FilterHours <= 0f) k = 1f;
            if (Time.time < _nextLabored) return;
            var iv = S.laboredIntervalSeconds;
            _nextLabored = Time.time + Mathf.Lerp(iv.x, iv.y, k) * Random.Range(0.85f, 1.15f);
            if (!_labored)
            {
                var go = new GameObject("ASH_LaboredBreath"); go.transform.SetParent(transform, false); go.transform.localPosition = Vector3.up * 1.6f;
                _labored = go.AddComponent<AudioSource>(); _labored.playOnAwake = false; _labored.spatialBlend = 0f;
                _laboredLp = go.AddComponent<AudioLowPassFilter>();
            }
            _laboredLp.cutoffFrequency = mask ? S.breathingMuffleHz : S.breathingMuffleHz * 1.8f;
            _labored.pitch = Mathf.Lerp(1f, 0.93f, k) * (1f + Random.Range(-S.laboredPitchJitter, S.laboredPitchJitter));
            _labored.PlayOneShot(clip, Mathf.Clamp01(S.laboredVolume * Mathf.Lerp(0.5f, 1f, k) * ASHGameSettings.CachedSfx));
        }

        float _breathPassStart = -999f;
        void UpdateBreathing()
        {
            bool on = _face && _face.Worn != ASHFaceProtection.Kind.None && S.breathingLoop && !_stats.IsDead && Time.timeScale > 0f;
            if (!on) { if (_breath && _breath.isPlaying) _breath.Stop(); return; }
            if (!_breath)
            {
                var go = new GameObject("ASH_Breathing"); go.transform.SetParent(transform, false); go.transform.localPosition = Vector3.up * 1.6f;
                _breath = go.AddComponent<AudioSource>(); _breath.loop = true; _breath.playOnAwake = false; _breath.spatialBlend = 0f;
                _lp = go.AddComponent<AudioLowPassFilter>();
            }
            if (_breath.clip != S.breathingLoop) { _breath.Stop(); _breath.clip = S.breathingLoop; }
            _lp.cutoffFrequency = _face.Worn == ASHFaceProtection.Kind.Mask ? S.breathingMuffleHz : S.breathingMuffleHz * 1.8f;
            _breath.volume = S.breathingVolume * ASHGameSettings.CachedSfx * (_face.Worn == ASHFaceProtection.Kind.Mask ? 1f : 0.7f);
            // pace: each pass of the breathing clip is followed by a gap so the cycle lasts clip x pace multiplier
            // (x1 = back-to-back as before: running / sprinting / rag-filter warning; ~x2 standing still). No pitch change.
            _breath.loop = false;
            if (!_breath.isPlaying && _breath.clip)
            {
                float pace = Mathf.Max(1f, ASHBreathFog.PaceMultiplier(gameObject)); // re-read every frame: speeding up cuts the gap short
                float len = _breath.clip.length / Mathf.Max(0.1f, Mathf.Abs(_breath.pitch));
                if (Time.time >= _breathPassStart + len * pace) { _breath.Play(); _breathPassStart = Time.time; }
            }
        }
    }
}
