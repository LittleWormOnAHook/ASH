using System.Collections.Generic;
using ASH.Gameplay;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// Warmth around an object (registry, no physics queries). Fires (campfire, stove, fireplace) warm the player back to normal warmth (30);
    /// lanterns and torches only slow the drop. Active only while lit: a campfire on the same object must be lit, otherwise
    /// (requireLight) a Light in its children must be enabled. Campfires get one automatically.
    /// </summary>
    public sealed class ASHHeatSource : MonoBehaviour
    {
        public enum Preset { Custom, Lantern, Torch, Stove, Fireplace, Campfire }
        public Preset preset = Preset.Custom;
        [Range(0.5f, 30f)] public float radius = 4f;
        [Tooltip("Warming at full strength (warmth points per game hour). 0 = only slows the drop.")]
        [Range(0f, 100f)] public float warmthPerGameHour = 12f;
        [Tooltip("This source can't warm you above this warmth (30 = normal).")]
        [Range(0f, 100f)] public float warmTarget = 30f;
        [SerializeField, HideInInspector] bool _warmthScale; // false on components saved before the warmth scale: presets re-apply
        [Tooltip("Fraction of outdoor cooling removed while in range (lanterns/torches stay below 1, so they can't stop it).")]
        [Range(0f, 1f)] public float coolingReduction = 0.85f;
        [Tooltip("Full strength inside this fraction of the radius, fading to 0 at the edge.")]
        [Range(0f, 1f)] public float innerFraction = 0.5f;
        [Tooltip("Only warm while a Light in the children is on (lanterns, stoves with fire lights).")]
        public bool requireLight;

        static readonly List<ASHHeatSource> _all = new List<ASHHeatSource>();
        public static IReadOnlyList<ASHHeatSource> All => _all;
        ASHCampfire _fire; Light _light;

        void OnEnable() { if (!_warmthScale && preset != Preset.Custom) ApplyPreset(preset); _all.Add(this); _fire = GetComponent<ASHCampfire>(); _light = GetComponentInChildren<Light>(true); }
        void OnDisable() => _all.Remove(this);

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
            preset = p; _warmthScale = true;
            switch (p)
            {
                case Preset.Lantern: radius = 2.5f; warmthPerGameHour = 0f; warmTarget = 30f; coolingReduction = 0.3f; requireLight = true; break;
                case Preset.Torch: radius = 2.5f; warmthPerGameHour = 0f; warmTarget = 30f; coolingReduction = 0.4f; requireLight = true; break;
                case Preset.Stove: radius = 4f; warmthPerGameHour = 10f; warmTarget = 30f; coolingReduction = 1f; requireLight = false; break;
                case Preset.Fireplace: radius = 6f; warmthPerGameHour = 12f; warmTarget = 30f; coolingReduction = 1f; requireLight = false; break;
                case Preset.Campfire: radius = 5f; warmthPerGameHour = 14f; warmTarget = 30f; coolingReduction = 1f; requireLight = false; break;
            }
        }

        void Reset() { if (GetComponent<ASHCampfire>()) ApplyPreset(Preset.Campfire); }
        void OnDrawGizmosSelected() { Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.6f); Gizmos.DrawWireSphere(transform.position, radius); }
    }
}
