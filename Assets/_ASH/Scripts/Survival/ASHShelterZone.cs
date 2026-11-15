using System.Collections.Generic;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// Shelter volume (building, cabin, tent, lean-to): put it on an object with a convex collider (box works best) covering the
    /// inside. Same inside test as the cabin indoor check (bounds + ClosestPoint). sealing 1 = ash-tight (exposure drain stops),
    /// 0.5 = leaky lean-to (drain halved). Entering one can autosave (Save Settings: shelter autosave).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHShelterZone : MonoBehaviour
    {
        [Tooltip("1 = ash-tight (drain stops), 0.5 = leaky lean-to, 0 = no protection.")]
        [Range(0f, 1f)] public float sealing = 1f;
        [Tooltip("Location name used for saves while inside (empty = nearest survey point).")]
        public string label = "Shelter";
        [Tooltip("Collider used for the inside test (empty = the first collider on this object).")]
        public Collider volume;

        static readonly List<ASHShelterZone> _all = new List<ASHShelterZone>();
        public static IReadOnlyList<ASHShelterZone> All => _all;

        void Reset() { volume = GetComponent<Collider>(); }
        void OnEnable() { if (!volume) volume = GetComponent<Collider>(); _all.Add(this); }
        void OnDisable() => _all.Remove(this);

        public bool Contains(Vector3 feet)
        {
            if (!volume || !volume.enabled) return false;
            var p = feet + Vector3.up * 0.5f;
            if (!volume.bounds.Contains(p)) return false;
            if (volume is MeshCollider mc && !mc.convex) return true; // non-convex: bounds only
            return (volume.ClosestPoint(p) - p).sqrMagnitude < 1e-4f;
        }

        /// <summary>Best sealing of any zone containing the point (0 = outside), and that zone.</summary>
        public static float SealingAt(Vector3 feet, out ASHShelterZone zone)
        {
            zone = null; float best = 0f;
            foreach (var z in _all) if (z && z.Contains(feet) && z.sealing >= best) { best = z.sealing; zone = z; }
            return best;
        }

        void OnDrawGizmosSelected()
        {
            var c = volume ? volume : GetComponent<Collider>(); if (!c) return;
            Gizmos.color = new Color(0.3f, 0.9f, 0.5f, 0.25f); Gizmos.DrawCube(c.bounds.center, c.bounds.size);
        }
    }
}
