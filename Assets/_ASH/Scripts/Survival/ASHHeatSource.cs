using System.Collections.Generic;
using ASH.Gameplay;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// Warmth around an object (registry, no physics queries). Lanterns / torches warm you to ~52, fires (campfire, stove,
    /// fireplace) to ~75 (normal outdoor warmth is 30). Preset caps and rates live in ASH_TemperatureSettings. Active only while lit: a campfire on the same object must be lit, otherwise
    /// (requireLight) a Light in its children must be enabled. Campfires get one automatically.
    /// </summary>
    public sealed class ASHHeatSource : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHHeatSource()
        {
            _all.Clear();
        }

        public enum Preset { Custom, Lantern, Torch, Stove, Fireplace, Campfire }
        public Preset preset = Preset.Custom;
        [Range(0.5f, 30f)] public float radius = 4f;
        [Tooltip("Warming at full strength (warmth points per game hour). 0 = only slows the drop.")]
        [Range(0f, 100f)] public float warmthPerGameHour = 12f;
        [Tooltip("Custom preset only: this source can't warm you above this warmth (30 = normal).")]
        [Range(0f, 150f)] public float warmTarget = 30f;
        [SerializeField, HideInInspector] int _presetVersion; // < 2: presets re-apply (warmth caps 52 / 75) // false on components saved before the warmth scale: presets re-apply
        [Tooltip("Fraction of outdoor cooling removed while in range (lanterns/torches stay below 1, so they can't stop it).")]
        [Range(0f, 1f)] public float coolingReduction = 0.85f;
        [Tooltip("Full strength inside this fraction of the radius, fading to 0 at the edge.")]
        [Range(0f, 1f)] public float innerFraction = 0.5f;
        [Tooltip("Only warm while a Light in the children is on (lanterns, stoves with fire lights).")]
        public bool requireLight;

        static readonly List<ASHHeatSource> _all = new List<ASHHeatSource>();
        public static IReadOnlyList<ASHHeatSource> All => _all;
        ASHCampfire _fire; Light _light;

        void OnEnable() { if (_presetVersion < 2 && preset != Preset.Custom) ApplyPreset(preset); _all.Add(this); _fire = GetComponent<ASHCampfire>(); _light = GetComponentInChildren<Light>(true); }
        void OnDisable() => _all.Remove(this);

        /// <summary>Live values: a preset reads ASH_TemperatureSettings (so caps stay tunable in one place); Custom uses the fields.</summary>
        public float WarmTarget { get { var t = ASHTemperatureSettings.I; switch (preset) { case Preset.Lantern: return t.lanternWarmTarget; case Preset.Torch: return t.torchWarmTarget; case Preset.Stove: return t.stoveWarmTarget; case Preset.Fireplace: return t.fireplaceWarmTarget; case Preset.Campfire: return t.campfireWarmTarget; } return warmTarget; } }
        public float WarmPerGameHour { get { var t = ASHTemperatureSettings.I; switch (preset) { case Preset.Lantern: return t.lanternWarmPerGameHour; case Preset.Torch: return t.torchWarmPerGameHour; case Preset.Stove: return t.stoveWarmPerGameHour; case Preset.Fireplace: return t.fireplaceWarmPerGameHour; case Preset.Campfire: return t.campfireWarmPerGameHour; } return warmthPerGameHour; } }

        public bool Active => (!_fire || _fire.Lit) && (!requireLight || (_light && _light.enabled && _light.gameObject.activeInHierarchy));

        /// <summary>0..1 strength at a point (0 = out of range / unlit).</summary>
        public float StrengthAt(Vector3 p)
        {
            if (!Active) return 0f;
            float d = Vector3.Distance(p, transform.position);
            if (d >= radius) return 0f;
            float inner = radius * innerFraction;
            return d <= inner ? 1f : 1f - (d - inner) / Mathf.Max(0.01f, radius - inner);
        }

        public void ApplyPreset(Preset p)
        {
            preset = p; _presetVersion = 2;
            switch (p)
            {
                case Preset.Lantern: radius = 3f; warmthPerGameHour = 16f; warmTarget = 52f; coolingReduction = 1f; requireLight = true; break;
                case Preset.Torch: radius = 3f; warmthPerGameHour = 16f; warmTarget = 52f; coolingReduction = 1f; requireLight = true; break;
                case Preset.Stove: radius = 4f; warmthPerGameHour = 30f; warmTarget = 75f; coolingReduction = 1f; requireLight = false; break;
                case Preset.Fireplace: radius = 6f; warmthPerGameHour = 34f; warmTarget = 75f; coolingReduction = 1f; requireLight = false; break;
                case Preset.Campfire: radius = 5f; warmthPerGameHour = 38f; warmTarget = 75f; coolingReduction = 1f; requireLight = false; break;
            }
        }

        void Reset() { if (GetComponent<ASHCampfire>()) ApplyPreset(Preset.Campfire); }
        void OnDrawGizmosSelected() { Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.6f); Gizmos.DrawWireSphere(transform.position, radius); }
    }
}
