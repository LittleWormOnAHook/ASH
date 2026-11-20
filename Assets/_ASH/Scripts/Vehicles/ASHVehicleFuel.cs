using System;
using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>Fuel tank (t356). Burns fuel while the engine runs (idle + throttle/RPM), the engine stalls / will not start at empty.
    /// Refuel() is the hook for jerry cans and pumps later.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHVehicleFuel : MonoBehaviour
    {
        [Range(5f, 300f)] public float capacityLitres = 70f;
        [Range(0f, 300f)] public float litres = 40f;
        [Tooltip("Litres per hour at idle.")] [Range(0f, 20f)] public float idleLitresPerHour = 1.5f;
        [Tooltip("Litres per hour at full throttle / high RPM.")] [Range(0f, 120f)] public float fullThrottleLitresPerHour = 22f;
        [Tooltip("Scales all consumption (debug / difficulty; 0 = no fuel use).")] [Range(0f, 20f)] public float consumptionMultiplier = 1f;
        [Tooltip("Fuel Up speed from cans, litres per second.")] [Range(0.2f, 20f)] public float pourLitresPerSecond = 2f;
        public float Fraction => capacityLitres > 0f ? Mathf.Clamp01(litres / capacityLitres) : 0f;
        public bool Empty => litres <= 0.0001f;
        public float LitresPerHourNow { get; private set; }
        public static event Action<ASHVehicleFuel> RanDry;

        /// <summary>Adds fuel, returns the litres actually added.</summary>
        public float Refuel(float add) { float before = litres; litres = Mathf.Clamp(litres + Mathf.Max(0f, add), 0f, capacityLitres); return litres - before; }

        public void Tick(float dt, bool running, float throttle01, float rpm01)
        {
            if (!running) { LitresPerHourNow = 0f; return; }
            float k = Mathf.Clamp01(Mathf.Clamp01(throttle01) * 0.7f + Mathf.Clamp01(rpm01) * 0.3f);
            LitresPerHourNow = Mathf.Lerp(idleLitresPerHour, fullThrottleLitresPerHour, k) * consumptionMultiplier;
            bool had = !Empty;
            litres = Mathf.Max(0f, litres - LitresPerHourNow / 3600f * dt);
            if (had && Empty) RanDry?.Invoke(this);
        }
        void OnValidate() { litres = Mathf.Clamp(litres, 0f, capacityLitres); }
    }
}
