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
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHSurvivalStats : MonoBehaviour, IASHConsumer
    {
        [SerializeField] private ASHSurvivalTuning tuning;

        public ASHSurvivalTuning Tuning => tuning;
        public float Health { get; private set; }
        public float Stamina { get; private set; }
        public float Hunger { get; private set; }
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
        public float StaminaRecoveryMultiplier => HungerLow || ThirstLow ? tuning.lowNeedsRecoveryMultiplier : 1f;

        public event Action Changed;
        public event Action Died;
        public event Action<ASHItemDefinition> Consumed;

        private const float NotifyInterval = 0.1f;
        private float _nextNotify;
        private float _lastDamageTime = float.NegativeInfinity;

        private void Awake()
        {
            if (tuning == null) tuning = ScriptableObject.CreateInstance<ASHSurvivalTuning>();
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
            float dt = Time.deltaTime;
            float mul = tuning.needsRateMultiplier * (IsSprinting ? tuning.sprintNeedsMultiplier : 1f) / 60f;
            Hunger = Mathf.Max(0f, Hunger - tuning.hungerPerMinute * mul * dt);
            Thirst = Mathf.Max(0f, Thirst - tuning.thirstPerMinute * mul * dt);

            float drain = 0f;
            if (Hunger <= 0f) drain += tuning.emptyNeedHealthDrainPerSecond;
            if (Thirst <= 0f) drain += tuning.emptyNeedHealthDrainPerSecond;
            if (drain > 0f) Damage(drain * dt, false);
            else if (Hunger >= MaxHunger * tuning.regenNeedThreshold && Thirst >= MaxThirst * tuning.regenNeedThreshold
                     && Time.time - _lastDamageTime > tuning.regenDelayAfterDamage && Health < MaxHealth)
                Health = Mathf.Min(MaxHealth, Health + tuning.healthRegenPerSecond * dt);

            if (Time.unscaledTime >= _nextNotify) { _nextNotify = Time.unscaledTime + NotifyInterval; Changed?.Invoke(); }
        }

        public void SetStaminaFromMotor(float current, float max)
        {
            Stamina = max > 0f ? Mathf.Clamp(current / max * MaxStamina, 0f, MaxStamina) : 0f;
        }

        public void Damage(float amount, bool resetsRegen = true)
        {
            if (IsDead || amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            if (resetsRegen) _lastDamageTime = Time.time;
            else _lastDamageTime = Time.time;
            if (Health <= 0f) { IsDead = true; Changed?.Invoke(); Died?.Invoke(); }
        }

        public void Consume(ASHItemDefinition item)
        {
            if (item == null || IsDead) return;
            Hunger = Mathf.Min(MaxHunger, Hunger + item.restoreHunger);
            Thirst = Mathf.Min(MaxThirst, Thirst + item.restoreThirst);
            Health = Mathf.Min(MaxHealth, Health + item.restoreHealth);
            Changed?.Invoke();
            Consumed?.Invoke(item);
        }

        public void Drink(float amount) { if (IsDead) return; Thirst = Mathf.Min(MaxThirst, Thirst + amount); Changed?.Invoke(); }

        /// <summary>Debug / stress helpers.</summary>
        public void DevSetNeeds(float hunger01, float thirst01) { Hunger = MaxHunger * hunger01; Thirst = MaxThirst * thirst01; Changed?.Invoke(); }
    }
}
