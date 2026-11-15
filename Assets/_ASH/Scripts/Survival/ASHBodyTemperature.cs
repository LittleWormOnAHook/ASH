using System;
using ASH.Core;
using ASH.Gameplay;
using ASH.UI;
using ASH.World.Weather;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.Survival
{
    /// <summary>
    /// Body temperature: ambient air from the clock (colder at night), the weather kind and wind chill; outdoors the body cools by
    /// how far ambient is below comfort. Shelters (same sealing as exposure) cut cooling and warm you slowly; heat sources
    /// (ASHHeatSource registry: fires warm you to normal, lanterns/torches only slow cooling; a lit held torch/lantern counts).
    /// Cold: health drain + toast, slower stamina recovery, frost vignette (survival HUD). Rates per game hour. Saved.
    /// Added to the player automatically on scene load.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHBodyTemperature : MonoBehaviour, IASHSaveable
    {
        public static ASHBodyTemperature Instance { get; private set; }
        public float Body { get; private set; } = 37f;
        public float Ambient { get; private set; }
        /// <summary>°C per game hour (smoothed; + warming, - cooling).</summary>
        public float Trend { get; private set; }
        public bool Cold => Body < S.coldThreshold;
        /// <summary>0..1 frost vignette strength.</summary>
        public float Frost => Mathf.Clamp01(Mathf.InverseLerp(S.frostStart, S.frostFull, Body));
        public bool NearHeat { get; private set; }

        ASHTemperatureSettings S => ASHTemperatureSettings.I;
        ASHSurvivalStats _stats; ASHEquipment _eq; float _eqLook; bool _init, _toasted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
            ASHCampfire.Placed -= AddToFire; ASHCampfire.Placed += AddToFire;
        }
        static void AddToFire(ASHCampfire f) { if (f && !f.GetComponent<ASHHeatSource>()) f.gameObject.AddComponent<ASHHeatSource>().ApplyPreset(ASHHeatSource.Preset.Campfire); }
        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            foreach (var f in FindObjectsByType<ASHCampfire>(FindObjectsInactive.Include)) AddToFire(f);
            var st = FindAnyObjectByType<ASHSurvivalStats>(); if (st && !st.GetComponent<ASHBodyTemperature>()) st.gameObject.AddComponent<ASHBodyTemperature>();
            if (st && !st.GetComponent<ASHBreathFog>()) st.gameObject.AddComponent<ASHBreathFog>();
        }

        void Awake() { Instance = this; _stats = GetComponent<ASHSurvivalStats>(); Body = S.normalBody; }
        void OnEnable() => ASHSaveSystem.Register(this);
        void OnDisable() { ASHSaveSystem.Unregister(this); if (_stats) _stats.ColdRecoveryMultiplier = 1f; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        float TargetAmbient(out bool outdoorsWind)
        {
            outdoorsWind = true;
            var clock = ASHGameTime.Clock;
            float hour = clock ? clock.timeOfDay : 12f;
            float f = 0.5f + 0.5f * Mathf.Cos((hour - S.warmestHour) / 24f * Mathf.PI * 2f);
            float a = Mathf.Lerp(S.nightTemperature, S.dayTemperature, f);
            var dir = ASHWeatherDirector.Instance;
            if (dir) { a += S.WeatherOffset(dir.CurrentKind); a -= Mathf.Min(S.maxWindChill, S.windChillPerMS * Mathf.Max(0f, dir.WindMS)); }
            return a;
        }

        float Sealing()
        {
            var ex = ASHExposure.Instance;
            if (ex) return ex.Sealing;
            return ASHShelterZone.SealingAt(transform.position, out _);
        }

        void Update()
        {
            if (!_stats || _stats.IsDead || ASHSaveSystem.IsRestoring) return;
            float dh = ASHGameTime.DeltaHours;
            float target = TargetAmbient(out _);
            if (!_init) { _init = true; Ambient = target; }
            else Ambient = Mathf.MoveTowards(Ambient, target, S.ambientBlendPerGameHour * Mathf.Max(dh, Time.deltaTime / 120f));
            if (dh <= 0f) { ApplyEffects(0f); return; }

            float sealing = Sealing();
            // heat sources: strongest cooling reduction, summed warming (capped by each source's target)
            float reduction = 0f, warm = 0f, warmTarget = 0f; var p = transform.position + Vector3.up;
            foreach (var h in ASHHeatSource.All)
            {
                if (!h) continue; float k = h.StrengthAt(p); if (k <= 0f) continue;
                reduction = Mathf.Max(reduction, h.coolingReduction * k);
                if (h.warmth > 0f && Body < h.maxWarmTarget) { warm += h.warmth * k; warmTarget = Mathf.Max(warmTarget, h.maxWarmTarget); }
            }
            reduction = Mathf.Max(reduction, HeldLightReduction());
            NearHeat = reduction > 0.01f || warm > 0f;

            float below = Mathf.Max(0f, S.comfortAmbient - Ambient);
            float rate = -below * S.coolingPerDegree * (1f - sealing) * (1f - Mathf.Clamp01(reduction));
            if (below <= 0f && Body < S.normalBody) rate += S.passiveWarmPerGameHour;
            if (sealing >= S.shelterWarmMinSealing && Body < S.shelterWarmTarget) rate += S.shelterWarmPerGameHour * sealing;
            rate += warm;

            float before = Body;
            float ceiling = warm > 0f ? Mathf.Max(warmTarget, S.shelterWarmTarget) : sealing >= S.shelterWarmMinSealing ? S.shelterWarmTarget : S.normalBody;
            if (warm <= 0f && sealing >= S.shelterWarmMinSealing && below <= 0f) ceiling = S.normalBody;
            Body = rate > 0f ? Mathf.Min(before + rate * dh, Mathf.Max(before, ceiling)) : before + rate * dh;
            Body = Mathf.Max(Body, S.minBody);
            Trend = Mathf.Lerp(Trend, (Body - before) / dh, 1f - Mathf.Exp(-Time.deltaTime * 2f));
            ApplyEffects(dh);
        }

        float HeldLightReduction()
        {
            if (!_eq && Time.unscaledTime >= _eqLook) { _eqLook = Time.unscaledTime + 3f; _eq = GetComponent<ASHEquipment>(); if (!_eq) _eq = FindAnyObjectByType<ASHEquipment>(); }
            if (!_eq) return 0f;
            float r = 0f;
            foreach (var it in new[] { _eq.Off, _eq.Main, _eq.Hip })
            {
                if (!it || !it.IsLightSource || !_eq.IsLit(it)) continue;
                string id = (it.itemId + " " + it.displayName).ToLowerInvariant();
                if (id.Contains("torch")) r = Mathf.Max(r, S.heldTorchCoolingReduction);
                else if (id.Contains("lantern")) r = Mathf.Max(r, S.heldLanternCoolingReduction);
            }
            return r;
        }

        void ApplyEffects(float dh)
        {
            float under = S.coldThreshold - Body;
            if (under > 0f)
            {
                if (dh > 0f && S.healthDrainPerDegree > 0f) _stats.Damage(S.healthDrainPerDegree * under * dh, false);
                _stats.ColdRecoveryMultiplier = 1f - S.staminaRecoveryPenalty * Mathf.Clamp01(under / 3f);
                if (!_toasted) { _toasted = true; var hud = FindAnyObjectByType<ASHHud>(); if (hud && !string.IsNullOrEmpty(S.freezingToast)) hud.Toast(S.freezingToast); }
            }
            else
            {
                _stats.ColdRecoveryMultiplier = 1f;
                if (Body > S.coldThreshold + 0.5f) _toasted = false;
            }
        }

        // ------------------------------------------------------------------ save
        [Serializable] sealed class State { public float body; }
        public string SaveKey => "temperature";
        public int RestoreOrder => 35;
        public string CaptureJson() => JsonUtility.ToJson(new State { body = Body });
        public void RestoreJson(string json) { var s = JsonUtility.FromJson<State>(json); if (s != null) { Body = Mathf.Clamp(s.body, S.minBody, 42f); Trend = 0f; _toasted = Body < S.coldThreshold; } }
    }
}
