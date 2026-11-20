using System.Collections.Generic;
using System;
using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>Repair hooks (t356, stub for the next phase): battery and fan/alternator belt. The engine needs a charged battery and a
    /// working belt to start; headlights drain the battery, a running engine with a belt charges it. startBroken (pickup later)
    /// removes both parts on a fresh game (not when restoring a save).</summary>
    [DisallowMultipleComponent]
    public sealed class ASHVehicleCondition : MonoBehaviour
    {
        public enum Part { Battery, Belt, Fuel, Tire }
        [Header("Parts")]
        public bool batteryInstalled = true;
        [Range(0f, 1f)] public float batteryCharge = 1f;
        public bool beltInstalled = true;
        [Range(0f, 1f)] public float beltCondition = 1f;
        [Tooltip("Fresh game: start with no battery, no belt, an empty tank and the startMissingTires off (the repair quest).")]
        public bool startBroken = false;
        [Header("Tires (pickup only)")]
        [Tooltip("Wheels that can lose a tire, in order FL, FR, BL, BR (empty = no tire repair, e.g. the car).")]
        public List<NWH.WheelController3D.WheelController> wheelSlots = new List<NWH.WheelController3D.WheelController>();
        [Tooltip("Indices into wheelSlots missing a tire on a fresh broken start (0 FL, 1 FR, 2 BL, 3 BR).")]
        public List<int> startMissingTires = new List<int> { 0, 3 };
        public ASH.Inventory.ASHItemDefinition tireItem;
        int _missingMask;
        [Header("Battery")]
        [Tooltip("Charge needed to crank the engine.")] [Range(0f, 1f)] public float minChargeToStart = 0.15f;
        [Tooltip("Charge used per engine start.")] [Range(0f, 0.3f)] public float chargePerStart = 0.02f;
        [Tooltip("Charge per minute lost with the headlights on (engine off).")] [Range(0f, 0.2f)] public float headlightDrainPerMinute = 0.01f;
        [Tooltip("Charge per minute gained while the engine runs with a working belt.")] [Range(0f, 0.5f)] public float chargePerMinute = 0.05f;
        public static event Action<ASHVehicleCondition, Part> PartChanged;
        [Header("Repair items")]
        public ASH.Inventory.ASHItemDefinition batteryItem, beltItem;
        public ASH.Inventory.ASHItemDefinition BatteryItem => batteryItem ? batteryItem : batteryItem = ASH.Inventory.ASHItemRegistry.I.Find("carbattery");
        public ASH.Inventory.ASHItemDefinition BeltItem => beltItem ? beltItem : beltItem = ASH.Inventory.ASHItemRegistry.I.Find("fanbelt");

        public bool BatteryOk => batteryInstalled && batteryCharge >= minChargeToStart;
        public bool BeltOk => beltInstalled && beltCondition > 0.05f;
        public bool HasTires => wheelSlots != null && wheelSlots.Count > 0;
        public int MissingMask => _missingMask;
        public int MissingTireCount { get { int n = 0; for (int i = 0; i < 8; i++) if ((_missingMask & (1 << i)) != 0) n++; return n; } }
        public bool TireMissing(int i) => (_missingMask & (1 << i)) != 0;
        public ASH.Inventory.ASHItemDefinition TireItem => tireItem ? tireItem : tireItem = ASH.Inventory.ASHItemRegistry.I.Find("tire");
        public static string WheelName(int i) => i == 0 ? "Front left" : i == 1 ? "Front right" : i == 2 ? "Rear left" : "Rear right";
        public bool HasLightPower => batteryInstalled && batteryCharge > 0.01f;

        /// <summary>Null when the engine can start, else the reason (shown as a toast).</summary>
        public string WhyNoStart(ASHVehicleFuel fuel)
        {
            if (!batteryInstalled) return "It needs a battery.";
            if (batteryCharge < minChargeToStart) return "The battery is flat.";
            if (!BeltOk) return "The engine belt is missing.";
            int t = MissingTireCount; if (t > 0) return t == 1 ? "A tire is missing." : $"{t} tires are missing.";
            if (fuel && fuel.Empty) return "Out of gas.";
            return null;
        }

        public void OnStarted() { batteryCharge = Mathf.Max(0f, batteryCharge - chargePerStart); }

        public void Tick(float dt, bool running, bool lightsOn)
        {
            if (!batteryInstalled) return;
            if (running && BeltOk) batteryCharge += chargePerMinute / 60f * dt;
            else if (lightsOn) batteryCharge -= headlightDrainPerMinute / 60f * dt;
            batteryCharge = Mathf.Clamp01(batteryCharge);
        }

        // ---- repair-system hooks (next phase) ----
        public void InstallBattery(float charge = 1f) { batteryInstalled = true; batteryCharge = Mathf.Clamp01(charge); PartChanged?.Invoke(this, Part.Battery); }
        public void RemoveBattery() { batteryInstalled = false; PartChanged?.Invoke(this, Part.Battery); }
        public void InstallBelt(float condition = 1f) { beltInstalled = true; beltCondition = Mathf.Clamp01(condition); PartChanged?.Invoke(this, Part.Belt); }
        public void RemoveBelt() { beltInstalled = false; PartChanged?.Invoke(this, Part.Belt); }
        public void ApplyStartBroken()
        {
            if (!startBroken) return; batteryInstalled = false; beltInstalled = false;
            int m = 0; if (HasTires) foreach (var i in startMissingTires) if (i >= 0 && i < wheelSlots.Count) m |= 1 << i;
            SetMissingMask(m);
            var f = GetComponent<ASHVehicleFuel>(); if (f) f.litres = 0f;
        }

        /// <summary>Missing tires: the wheel mesh is hidden and its wheel controller is off, so that corner sits down on the hub.</summary>
        public void SetMissingMask(int mask)
        {
            _missingMask = HasTires ? mask & ((1 << wheelSlots.Count) - 1) : 0;
            if (!HasTires) return;
            for (int i = 0; i < wheelSlots.Count; i++)
            {
                var wc = wheelSlots[i]; if (!wc) continue; bool miss = TireMissing(i);
                wc.enabled = !miss;
                if (wc.wheel != null && wc.wheel.visual) foreach (var r in wc.wheel.visual.GetComponentsInChildren<Renderer>(true)) r.enabled = !miss;
            }
        }
        public void InstallTire(int i) { SetMissingMask(_missingMask & ~(1 << i)); PartChanged?.Invoke(this, Part.Tire); }
        public void RemoveTire(int i) { SetMissingMask(_missingMask | (1 << i)); PartChanged?.Invoke(this, Part.Tire); }
    }
}
