using System.Collections.Generic;
using ASH.Interaction;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Read-only fire state other systems can query (cooking, boiling water, warmth, AI). Implemented by ASHCampfire;
    /// stoves / fireplaces can implement it later. Find one near a point with <see cref="ASHCampfire.NearestLit"/>.
    /// </summary>
    public interface IASHFireState
    {
        bool Lit { get; }
        /// <summary>Game hours of burn time left.</summary>
        float FuelHours { get; }
        float MaxFuelHours { get; }
        /// <summary>0..1 fuel fill.</summary>
        float Fuel01 { get; }
        /// <summary>0..1 heat output (0 when out; dims while low on fuel). Cooking / boiling can scale their speed by this.</summary>
        float Heat01 { get; }
        Vector3 FirePosition { get; }
    }

    /// <summary>Placed campfire: flickering light, fire/smoke particles, fuel that burns down on the game clock.
    /// Wood or kindling from the pack adds fuel (tap Interact); a lit fire with no fuel to add is put out by holding Interact.
    /// When the fuel runs out the fire goes out, so its warmth (ASHHeatSource) and the Campfire crafting station stop too.
    /// Tuning: Resources/ASH_CampfireSettings (ASH/Systems/Campfire Fuel Settings).</summary>
    public sealed class ASHCampfire : MonoBehaviour, IASHUsable, IASHCraftStation, IASHFireState
    {
        [SerializeField] private Light fireLight;
        [SerializeField] private ParticleSystem[] effects;
        [SerializeField] private AudioSource crackle;
        [Range(0f, 20f)] public float baseIntensity = 3.2f;
        [Range(0f, 1f)] public float flicker = 0.35f;
        [Range(0.1f, 20f)] public float flickerSpeed = 7f;
        [SerializeField] private bool lit = true;
        [Tooltip("Starting fuel in game hours. -1 = settings default (placed fires vs scene fires).")]
        [SerializeField] private float fuelHours = -1f;

        public bool Lit => lit;
        public float FuelHours => Mathf.Max(0f, fuelHours);
        public float MaxFuelHours => ASHCampfireSettings.I.maxFuelHours;
        public float Fuel01 => MaxFuelHours > 0f ? Mathf.Clamp01(FuelHours / MaxFuelHours) : 0f;
        public float Heat01 => lit ? LowFuelScale : 0f;
        public Vector3 FirePosition => transform.position;
        public Transform UseTransform => transform;
        public static event System.Action<ASHCampfire> Placed;
        /// <summary>Lit state changed (true = lit). Fires on burn-out too.</summary>
        public event System.Action<ASHCampfire, bool> LitChanged;
        /// <summary>Fuel was added (item, game hours added).</summary>
        public event System.Action<ASHCampfire, ASHItemDefinition, float> FuelAdded;

        static readonly List<ASHCampfire> _all = new List<ASHCampfire>();
        public static IReadOnlyList<ASHCampfire> All => _all;

        /// <summary>Nearest lit fire within radius (null if none). Entry point for cooking / boiling water.</summary>
        public static ASHCampfire NearestLit(Vector3 p, float radius)
        {
            ASHCampfire best = null; float bd = radius * radius;
            foreach (var f in _all) { if (!f || !f.lit) continue; float d = (f.transform.position - p).sqrMagnitude; if (d <= bd) { bd = d; best = f; } }
            return best;
        }

        private float _seed;
        private bool _started;

        public ASHCraftStation StationType => ASHCraftStation.Campfire;
        public bool StationActive => lit && isActiveAndEnabled;
        public Vector3 StationPosition => transform.position;
        private void OnEnable() { if (!ASHCrafting.Stations.Contains(this)) ASHCrafting.Stations.Add(this); if (!_all.Contains(this)) _all.Add(this); }
        private void OnDisable() { ASHCrafting.Stations.Remove(this); _all.Remove(this); }

        private void Start()
        {
            _seed = Random.value * 100f;
            if (fuelHours < 0f) { var s = ASHCampfireSettings.I; fuelHours = GetComponent<ASHPlacedObject>() ? s.placedStartFuelHours : s.sceneStartFuelHours; }
            _started = true;
            SetLitSilent(lit && fuelHours > 0f);
            Placed?.Invoke(this);
        }

        float LowFuelScale
        {
            get { var s = ASHCampfireSettings.I; return s.lowFuelHours > 0f && fuelHours < s.lowFuelHours ? Mathf.Lerp(s.lowFuelLightScale, 1f, Mathf.Clamp01(fuelHours / s.lowFuelHours)) : 1f; }
        }

        private void Update()
        {
            if (!lit) return;
            var s = ASHCampfireSettings.I;
            float burn = ASHGameTimeBridge.DeltaHours * s.burnRateMultiplier;
            if (burn > 0f)
            {
                fuelHours = Mathf.Max(0f, fuelHours - burn);
                if (fuelHours <= 0f) { BurnOut(); return; }
            }
            if (fireLight == null) return;
            float n = Mathf.PerlinNoise(_seed, Time.time * flickerSpeed);
            fireLight.intensity = baseIntensity * LowFuelScale * (1f - flicker * 0.5f + flicker * n);
        }

        void BurnOut()
        {
            SetLit(false);
            ASHSfx.Play("campfire_out", ASHSoundBank.Instance ? ASHSoundBank.Instance.torchOff : null, transform.position, ASHCampfireSettings.I.sfxVolume);
            ASH.Core.ASHLog.Info("Campfire burned out");
        }

        /// <summary>Lights or puts out the fire. Lighting needs fuel.</summary>
        public void SetLit(bool on)
        {
            bool was = lit;
            SetLitSilent(on && (!_started || fuelHours > 0f));
            if (was != lit) LitChanged?.Invoke(this, lit);
        }

        void SetLitSilent(bool on)
        {
            lit = on;
            if (fireLight) fireLight.enabled = on;
            if (effects != null) foreach (var e in effects) if (e) { if (on) e.Play(); else e.Stop(); }
            if (crackle) { if (on) crackle.Play(); else crackle.Stop(); }
        }

        /// <summary>Save restore: absolute state (works before or after Start).</summary>
        public void RestoreState(bool isLit, float fuel)
        {
            fuelHours = Mathf.Max(0f, fuel);
            lit = isLit && fuelHours > 0f;
            if (_started) SetLitSilent(lit);
        }

        /// <summary>Fuel item from the pack this fire would take next (null = none or no room).</summary>
        public ASHItemDefinition FindFuelIn(ASHInventory inv, out float hours)
        {
            hours = 0f;
            if (inv == null) return null;
            var s = ASHCampfireSettings.I;
            if (fuelHours >= s.maxFuelHours - 0.01f) return null;
            var reg = ASHItemRegistry.I;
            foreach (var f in s.fuels)
            {
                if (f == null || string.IsNullOrEmpty(f.itemId) || f.hours <= 0f) continue;
                var it = reg ? reg.Find(f.itemId) : null;
                if (it && inv.Count(it) > 0) { hours = f.hours; return it; }
            }
            return null;
        }

        /// <summary>Takes one fuel item from the pack and adds its burn time (clamped to max). Does not light the fire.</summary>
        public bool TryAddFuel(ASHInventory inv)
        {
            var it = FindFuelIn(inv, out float h);
            if (!it || !inv.Remove(it, 1)) return false;
            fuelHours = Mathf.Min(ASHCampfireSettings.I.maxFuelHours, FuelHours + h);
            var s = ASHCampfireSettings.I;
            ASHSfx.Play("campfire_fuel", s.addFuelClips, transform.position, s.sfxVolume);
            FuelAdded?.Invoke(this, it, h);
            return true;
        }

        public float GetUsePriority(in ASHUseContext c) => 30f;
        public float GetHoldDuration(in ASHUseContext c) => lit && FindFuelIn(c.Inventory, out _) == null ? ASHCampfireSettings.I.putOutHoldSeconds : 0f;

        public string GetPrompt(in ASHUseContext c)
        {
            string fuel = $"fuel {ASHGameTimeBridge.Format(FuelHours)} / {ASHGameTimeBridge.Format(MaxFuelHours)}";
            var it = FindFuelIn(c.Inventory, out _);
            if (lit) return it ? $"Add {it.displayName} ({fuel})" : $"Put out fire ({fuel})";
            if (FuelHours > 0f) return $"Light fire ({fuel})";
            return it ? $"Add {it.displayName} (fire is out)" : "Fire is out: needs wood or kindling";
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (lit) { if (TryAddFuel(c.Inventory)) return true; SetLit(false); return true; }
            if (FuelHours > 0f)
            {
                SetLit(true);
                if (lit) ASHSfx.Play("campfire_light", ASHSoundBank.Instance ? ASHSoundBank.Instance.torchOn : null, transform.position, ASHCampfireSettings.I.sfxVolume);
                return lit;
            }
            return TryAddFuel(c.Inventory);
        }
    }

    /// <summary>Game-clock access for Gameplay without a hard Survival dependency in every call site.</summary>
    static class ASHGameTimeBridge
    {
        public static float DeltaHours => ASH.Survival.ASHGameTime.DeltaHours;
        public static string Format(float h) => ASH.Survival.ASHGameTime.Format(h);
    }
}
