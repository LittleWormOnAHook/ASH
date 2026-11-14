using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.World
{
    /// <summary>Settings for ASH/World/Resource Scatter (branch piles, stumps, downed trees, plant-fiber plants). Additive runs; Clear removes a group.</summary>
    [CreateAssetMenu(menuName = "ASH/World/Resource Scatter Settings", fileName = "ASH_ResourceScatterSettings")]
    public sealed class ASHResourceScatterSettings : ScriptableObject
    {
        public enum Preset { Low, Default, Extreme }
        public enum Group { Wood, Plants }

        [Serializable]
        public sealed class Entry
        {
            public GameObject prefab;
            public Group group = Group.Wood;
            [Tooltip("Placed per run (runs are additive).")] [Range(0, 5000)] public int count = 100;
            [Tooltip("Biome weights.")] [Range(0f, 1f)] public float ridge = 0.6f, gully = 1f, prairie = 0.2f, creek = 0.6f;
            [Range(0.2f, 3f)] public float minScale = 0.85f, maxScale = 1.2f;
            [Range(0f, 60f)] public float maxSlope = 30f;
            [Tooltip("Tilt to the ground normal (branches, logs, plants) instead of standing upright (stumps).")]
            public bool alignToGround = true;
            [Tooltip("Base count at the Default preset.")] public int defaultCount = 100;
        }

        public List<Entry> entries = new List<Entry>();
        [Tooltip("0 = new seed each run (additive runs spread out).")] public int seed = 0;
        [Header("Near trees")]
        [Tooltip("Never closer than this to a terrain tree (keeps clear of Anthony's and ASH trees).")]
        [Range(0.5f, 5f)] public float minTreeDistance = 1.4f;
        [Tooltip("Within this distance of a tree the weight is full; beyond it, 'openGroundWeight'.")]
        [Range(2f, 40f)] public float nearTreeRadius = 9f;
        [Range(0f, 1f)] public float openGroundWeight = 0.25f;
        [Header("Clearances (m)")]
        [Range(0f, 30f)] public float roadEdgeClear = 2.5f;
        [Range(0f, 30f)] public float trailClear = 2f;
        [Range(0.5f, 20f)] public float minSpacing = 2.5f;
        [Range(1, 200)] public int attemptsPerItem = 40;

        public void ApplyPreset(Preset p)
        {
            float m = p == Preset.Low ? 0.4f : p == Preset.Extreme ? 3f : 1f;
            foreach (var e in entries) e.count = Mathf.RoundToInt(e.defaultCount * m);
            minSpacing = p == Preset.Extreme ? 1.5f : 2.5f;
        }
    }
}
