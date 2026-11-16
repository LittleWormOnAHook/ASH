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
    /// Warmth (abstract scale shown as degrees on the HUD): starts at 30 (normal outside) and drops the longer you're outdoors,
    /// faster at night, in bad weather and wind (game-clock time). At or below 6 (cold threshold): the meter pulses, frost
    /// vignette, health drain + toast, slower stamina recovery and cold breath (ASHBreathFog). Shelters (exposure sealing) cut the
    /// drop and recover you toward 30; heat sources (ASHHeatSource: fires warm you back to 30, lanterns/torches only slow the drop;
    /// a lit held torch/lantern counts). Saved (old body-°C saves are converted). Added to the player automatically.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHBodyTemperature : MonoBehaviour, IASHSaveable
    {
        public static ASHBodyTemperature Instance { get; private set; }
        /// <summary>Current warmth (30 = normal by default).</summary>
        public float Warmth { get; private set; } = 30f;
        /// <summary>Kept for older callers: same as Warmth.</summary>
        public float Body => Warmth;
        /// <summary>Current outdoor drop multiplier (night x weather x wind).</summary>
        public float DropMultiplier { get; private set; } = 1f;
        /// <summary>Warmth points per game hour (smoothed; + warming, - cooling).</summary>
        public float Trend { get; private set; }
        public bool Cold => Warmth <= S.coldWarmth;
        /// <summary>0..1 frost vignette strength.</summary>
        public float Frost => Warmth > S.frostStartWarmth ? 0f : Mathf.Clamp01(Mathf.InverseLerp(S.frostStartWarmth + 0.001f, S.frostFullWarmth, Warmth));
        public bool NearHeat { get; private set; }
        bool _heldLit;

        ASHTemperatureSettings S => ASHTemperatureSettings.I;
        ASHSurvivalStats _stats; ASHEquipment _eq; float _eqLook; bool _init, _toasted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
            ASHCampfire.Placed -= AddToFire; ASHCampfire.Placed += AddToFire;
        }
        /// <summary>Also attach after the first scene loads: with Enter Play Mode Options (no scene reload) sceneLoaded never fires on Play.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachAfterLoad() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        static void AddToFire(ASHCampfire f) { if (f && !f.GetComponent<ASHHeatSource>()) f.gameObject.AddComponent<ASHHeatSource>().ApplyPreset(ASHHeatSource.Preset.Campfire); }
        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            foreach (var f in FindObjectsByType<ASHCampfire>(FindObjectsInactive.Include)) AddToFire(f);
            var st = FindAnyObjectByType<ASHSurvivalStats>(); if (st && !st.GetComponent<ASHBodyTemperature>()) st.gameObject.AddComponent<ASHBodyTemperature>();
            if (st && !st.GetComponent<ASHBreathFog>()) st.gameObject.AddComponent<ASHBreathFog>();
        }

        void Awake() { Instance = this; _stats = GetComponent<ASHSurvivalStats>(); Warmth = S.normalWarmth; }
        void OnEnable() => ASHSaveSystem.Register(this);
        void OnDisable() { ASHSaveSystem.Unregister(this); if (_stats) _stats.ColdRecoveryMultiplier = 1f; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        float TargetMultiplier()
        {
            var clock = ASHGameTime.Clock;
            float hour = clock ? clock.timeOfDay : 12f;
            float day = 0.5f + 0.5f * Mathf.Cos((hour - S.warmestHour) / 24f * Mathf.PI * 2f); // 1 at the warmest hour, 0 twelve hours later
            float m = Mathf.Lerp(S.nightMultiplier, 1f, day);
            var dir = ASHWeatherDirector.Instance;
            if (dir) { m *= S.WeatherMultiplier(dir.CurrentKind); m *= 1f + Mathf.Min(S.maxWindBonus, S.windPerMS * Mathf.Max(0f, dir.WindMS)); }
            return m;
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
            float target = TargetMultiplier();
            if (!_init) { _init = true; DropMultiplier = target; }
            else DropMultiplier = Mathf.MoveTowards(DropMultiplier, target, S.multiplierBlendPerGameHour * Mathf.Max(dh, Time.deltaTime / 120f));
            if (dh <= 0f) { ApplyEffects(0f); return; }

            float sealing = Sealing();
            // heat sources: strongest drop reduction, summed warming (capped by each source's target)
            float reduction = 0f, warm = 0f, warmTarget = 0f, hold = 0f; var p = transform.position + Vector3.up;
            foreach (var h in ASHHeatSource.All)
            {
                if (!h) continue; float k = h.StrengthAt(p); if (k <= 0f) continue;
                reduction = Mathf.Max(reduction, h.coolingReduction * k);
                float wr = h.WarmPerGameHour, wt = h.WarmTarget;
                if (wr > 0f) { hold = Mathf.Max(hold, wt); if (Warmth < wt) { warm += wr * k; warmTarget = Mathf.Max(warmTarget, wt); } }
            }
            reduction = Mathf.Max(reduction, HeldLightReduction());
            if (_heldLit && S.heldLightWarmPerGameHour > 0f)
            {
                hold = Mathf.Max(hold, S.heldLightWarmTarget);
                if (Warmth < S.heldLightWarmTarget) { warm += S.heldLightWarmPerGameHour; warmTarget = Mathf.Max(warmTarget, S.heldLightWarmTarget); }
            }
            NearHeat = reduction > 0.01f || warm > 0f;

            float rate = -S.outdoorDropPerGameHour * DropMultiplier * (1f - sealing) * (1f - Mathf.Clamp01(reduction));
            bool shelterWarms = sealing >= S.shelterWarmMinSealing && Warmth < S.shelterRecoverTarget;
            if (shelterWarms) rate += S.shelterRecoverPerGameHour * sealing;
            rate += warm;
            // above normal with nothing holding you there: shed the extra warmth back toward normal (or the strongest source in range)
            float floorW = Mathf.Max(S.normalWarmth, hold);
            if (Warmth > floorW && S.excessWarmthLossPerGameHour > 0f) rate -= S.excessWarmthLossPerGameHour;

            float before = Warmth;
            float ceiling = Mathf.Max(warm > 0f ? warmTarget : 0f, sealing >= S.shelterWarmMinSealing ? S.shelterRecoverTarget : 0f);
            Warmth = rate > 0f ? Mathf.Min(before + rate * dh, Mathf.Max(before, ceiling)) : before + rate * dh;
            if (before > floorW && Warmth < floorW) Warmth = floorW; // excess loss stops at the floor (normal cooling continues next frame)
            Warmth = Mathf.Min(Warmth, S.maxWarmth);
            Warmth = Mathf.Max(Warmth, S.minWarmth);
            Trend = Mathf.Lerp(Trend, (Warmth - before) / dh, 1f - Mathf.Exp(-Time.deltaTime * 2f));
            ApplyEffects(dh);
        }

        float HeldLightReduction()
        {
            if (!_eq && Time.unscaledTime >= _eqLook) { _eqLook = Time.unscaledTime + 3f; _eq = GetComponent<ASHEquipment>(); if (!_eq) _eq = FindAnyObjectByType<ASHEquipment>(); }
            _heldLit = false;
            if (!_eq) return 0f;
            float r = 0f;
            foreach (var it in new[] { _eq.Off, _eq.Main, _eq.Hip })
            {
                if (!it || !it.IsLightSource || !_eq.IsLit(it)) continue;
                _heldLit = true;
                string id = (it.itemId + " " + it.displayName).ToLowerInvariant();
                if (id.Contains("torch")) r = Mathf.Max(r, S.heldTorchCoolingReduction);
                else if (id.Contains("lantern")) r = Mathf.Max(r, S.heldLanternCoolingReduction);
            }
            return r;
        }

        void ApplyEffects(float dh)
        {
            if (Warmth <= S.coldWarmth)
            {
                float under = S.coldWarmth - Warmth;
                float drain = S.healthDrainPerGameHour + S.healthDrainPerPointBelow * under;
                if (dh > 0f && drain > 0f) _stats.Damage(drain * dh, false);
                float span = Mathf.Max(0.01f, S.coldWarmth - S.minWarmth);
                _stats.ColdRecoveryMultiplier = 1f - S.staminaRecoveryPenalty * Mathf.Clamp01(0.25f + 0.75f * under / span);
                if (!_toasted) { _toasted = true; var hud = FindAnyObjectByType<ASHHud>(); if (hud && !string.IsNullOrEmpty(S.freezingToast)) hud.Toast(S.freezingToast); }
            }
            else
            {
                _stats.ColdRecoveryMultiplier = 1f;
                if (Warmth > S.coldWarmth + 1f) _toasted = false;
            }
        }

        // ------------------------------------------------------------------ save
        /// <summary>v2 = warmth scale. Older saves stored body °C (37 normal, 35 cold): mapped 37 -> 30, 35 -> 6 (linear).</summary>
        [Serializable] sealed class State { public float body; public float warmth; public int v; }
        public string SaveKey => "temperature";
        public int RestoreOrder => 35;
        public string CaptureJson() => JsonUtility.ToJson(new State { warmth = Warmth, v = 2 });
        public void RestoreJson(string json)
        {
            var s = JsonUtility.FromJson<State>(json); if (s == null) return;
            float w = s.v >= 2 ? s.warmth : S.coldWarmth + (s.body - 35f) * (S.normalWarmth - S.coldWarmth) / 2f;
            Warmth = Mathf.Clamp(w, S.minWarmth, Mathf.Max(S.maxWarmth, S.normalWarmth)); Trend = 0f; _toasted = Warmth <= S.coldWarmth;
        }
    }
}
