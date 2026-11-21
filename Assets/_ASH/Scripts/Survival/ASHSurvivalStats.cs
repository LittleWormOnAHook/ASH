using System;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// Health, stamina, hunger and thirst. Structure ported from Genesis Project.Survival.SurvivalStats:
    /// authored maxima, low-stat health drain, delayed health regen, throttled change notification,
    /// Consume(item). Energy/oxygen/exposure/progression replaced by hunger and thirst.
    /// Stamina itself is simulated by the Invector motor and mirrored in via <see cref="SetStaminaFromMotor"/>.
    /// Hunger, thirst, empty-need health drain, regen and the regen delay run on GAME time (<see cref="ASHGameTime"/>):
    /// tuning values stay "per real minute / second at the default 48-minute day" (1 game hour = 120 of those seconds),
    /// so the balance is unchanged at 48 min/day and scales with the clock if the day length changes (frozen clock = frozen needs).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHSurvivalStats : MonoBehaviour, IASHConsumer
    {
        [SerializeField] private ASHSurvivalTuning tuning;

        public ASHSurvivalTuning Tuning => tuning;
        public float Health { get; private set; }
        public float Stamina { get; private set; }
        public float Hunger { get; private set; }
        /// <summary>Test/debug hook: sets hunger directly (clamped).</summary>
        public void SetHunger(float v) => Hunger = Mathf.Clamp(v, 0f, MaxHunger);
        public float Thirst { get; private set; }
        public float MaxHealth => tuning.maxHealth;
        public float MaxStamina => tuning.maxStamina;
        public float MaxHunger => tuning.maxHunger;
        public float MaxThirst => tuning.maxThirst;
        public bool IsDead { get; private set; }
        public bool IsSprinting { get; set; }
        public bool Paused { get; set; }

        public bool HungerLow => Hunger <= MaxHunger * tuning.lowNeedThreshold;
        public bool ThirstLow => Thirst <= MaxThirst * tuning.lowNeedThreshold;
        public float StaminaRecoveryMultiplier => (HungerLow || ThirstLow ? tuning.lowNeedsRecoveryMultiplier : 1f) * ExposureRecoveryMultiplier * ColdRecoveryMultiplier;
        /// <summary>Set by ash exposure (1 = no effect): slows stamina recovery outside in ash.</summary>
        public float ExposureRecoveryMultiplier { get; set; } = 1f;
        /// <summary>Set by ASHBodyTemperature (cold slows stamina recovery).</summary>
        public float ColdRecoveryMultiplier { get; set; } = 1f;

        /// <summary>Save restore: absolute values (clamped).</summary>
        public void RestoreValues(float health, float hunger, float thirst)
        {
            IsDead = false;
            Health = Mathf.Clamp(health, 1f, MaxHealth);
            if (hunger >= 0f) Hunger = Mathf.Clamp(hunger, 0f, MaxHunger);
            if (thirst >= 0f) Thirst = Mathf.Clamp(thirst, 0f, MaxThirst);
            _sinceDamage = 0f;
            Changed?.Invoke();
        }

        public event Action Changed;
        public event Action Died;
        public event Action<ASHItemDefinition> Consumed;

        private const float NotifyInterval = 0.1f;
        private float _nextNotify;
        /// <summary>Real seconds per game hour at the default 48-minute day (the unit the tuning rates are authored in).</summary>
        public const float SecondsPerGameHourAtDefault = 48f * 60f / 24f;
        private float _sinceDamage = float.PositiveInfinity; // game-time seconds (at the default day) since the last damage

        private void Awake()
        {
            if (tuning == null) tuning = ScriptableObject.CreateInstance<ASHSurvivalTuning>();
            ASHSurvivalTuning.Bind(tuning);
            ResetStats();
        }

        public void ResetStats()
        {
            IsDead = false;
            Health = tuning.maxHealth;
            Stamina = tuning.maxStamina;
            Hunger = tuning.maxHunger * tuning.startHunger;
            Thirst = tuning.maxThirst * tuning.startThirst;
            Changed?.Invoke();
        }

        private void Update()
        {
            if (IsDead || Paused || Time.timeScale <= 0f) return;
            // Game-clock delta expressed in "seconds at the default 48-min day" so authored rates keep their balance.
            float dt = ASHGameTime.DeltaHours * SecondsPerGameHourAtDefault;
            ASHSurvivalTuning.Bind(tuning);
            if (_sinceDamage < float.PositiveInfinity) _sinceDamage += dt;
            float mul = tuning.needsRateMultiplier * ASHSurvivalTuning.DrainRate * (IsSprinting ? tuning.sprintNeedsMultiplier : 1f) / 60f;
            Hunger = Mathf.Max(0f, Hunger - tuning.hungerPerMinute * mul * dt);
            Thirst = Mathf.Max(0f, Thirst - tuning.thirstPerMinute * mul * dt);

            float drain = 0f;
            if (Hunger <= 0f) drain += tuning.emptyNeedHealthDrainPerSecond;
            if (Thirst <= 0f) drain += tuning.emptyNeedHealthDrainPerSecond;
            if (drain > 0f) Damage(drain * dt, false);
            else if (Hunger >= MaxHunger * tuning.regenNeedThreshold && Thirst >= MaxThirst * tuning.regenNeedThreshold
                     && _sinceDamage > tuning.regenDelayAfterDamage && Health < MaxHealth)
                Health = Mathf.Min(MaxHealth, Health + tuning.healthRegenPerSecond * dt);

            if (Time.unscaledTime >= _nextNotify) { _nextNotify = Time.unscaledTime + NotifyInterval; Changed?.Invoke(); }
        }

        public void SetStaminaFromMotor(float current, float max)
        {
            float next = max > 0f ? Mathf.Clamp(current / max * MaxStamina, 0f, MaxStamina) : 0f;
            // t412: stamina SPENT (drops only; regen never counts) drains hunger a little and thirst more
            float spent = Mathf.Min(Stamina - next, MaxStamina);
            if (spent > 0f && !IsDead && !Paused && Time.timeScale > 0f) ApplyExertion(spent);
            Stamina = next;
        }

        /// <summary>Total hunger / thirst taken by stamina use this session (debug).</summary>
        public float ExertionHunger { get; private set; }
        public float ExertionThirst { get; private set; }
        /// <summary>t412: drain needs for stamina points spent (sprint, attacks, jumps, rolls...).</summary>
        public void ApplyExertion(float staminaSpent)
        {
            if (staminaSpent <= 0f) return;
            float h = staminaSpent * tuning.staminaHungerPerPoint, t = staminaSpent * tuning.staminaThirstPerPoint;
            Hunger = Mathf.Max(0f, Hunger - h); Thirst = Mathf.Max(0f, Thirst - t);
            ExertionHunger += h; ExertionThirst += t;
        }

        public void Damage(float amount, bool resetsRegen = true)
        {
            if (IsDead || amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            _sinceDamage = 0f; // every damage source pauses regen (resetsRegen kept for API compatibility)
            if (Health <= 0f) { IsDead = true; Changed?.Invoke(); Died?.Invoke(); }
        }

        public void Consume(ASHItemDefinition item, float factor = 1f)
        {
            if (item == null || IsDead) return;
            Hunger = Mathf.Min(MaxHunger, Hunger + item.restoreHunger * factor);
            Thirst = Mathf.Min(MaxThirst, Thirst + item.restoreThirst * factor);
            Health = Mathf.Min(MaxHealth, Health + item.restoreHealth * factor);
            TrySickness(item, factor);
            Changed?.Invoke();
            Consumed?.Invoke(item);
        }

        /// <summary>(item, health lost) when raw / dirty food made the player sick.</summary>
        public static event Action<ASHItemDefinition, float> Sickened;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetSickness() => Sickened = null;

        void TrySickness(ASHItemDefinition item, float factor)
        {
            if (item.sicknessChance <= 0f || factor <= 0f) return;
            float chance = Mathf.Clamp01(item.sicknessChance * ASH.Gameplay.ASHCookingSettings.I.sicknessChanceMultiplier);
            if (UnityEngine.Random.value >= chance) return;
            Hunger = Mathf.Max(0f, Hunger - item.sicknessHungerLoss);
            Thirst = Mathf.Max(0f, Thirst - item.sicknessThirstLoss);
            Damage(item.sicknessHealthLoss);
            Sickened?.Invoke(item, item.sicknessHealthLoss);
            var hud = FindAnyObjectByType<ASH.UI.ASHHud>();
            if (hud) hud.Toast($"The {item.displayName.ToLowerInvariant()} made you sick (-{item.sicknessHealthLoss:0} health). Cook it first.");
        }

        public void Drink(float amount) { if (IsDead) return; Thirst = Mathf.Min(MaxThirst, Thirst + amount); Changed?.Invoke(); }

        /// <summary>Removes thirst points (ash exposure and other drains). Clamped at 0.</summary>
        public void DrainThirst(float amount)
        {
            if (IsDead || amount <= 0f) return;
            Thirst = Mathf.Max(0f, Thirst - amount);
            Changed?.Invoke();
        }

        /// <summary>Debug / stress helpers.</summary>
        public void DevSetNeeds(float hunger01, float thirst01) { Hunger = MaxHunger * hunger01; Thirst = MaxThirst * thirst01; Changed?.Invoke(); }
    }
}
