using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.World
{
    /// <summary>
    /// Path marker placement (ASH/World/Place Path Markers). Swap the prefab list for flags, stakes, fence posts or cairns
    /// and re-run; markers sit at the path edge along ASH_RegionLayout roads/trails, never in the path or on trees.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/World/Path Marker Settings", fileName = "ASH_PathMarkerSettings")]
    public sealed class ASHPathMarkerSettings : ScriptableObject
    {
        public enum PickMode { Random, Sequential, PerRoute }
        public enum SideMode { Alternate, Left, Right, Both }
        public enum Preset { Sparse, Default, Dense, Extreme }

        [Header("Prefabs (swappable placeholders)")]
        [Tooltip("Marker prefabs. Random pick, in order, or one per route (route index mod count).")]
        public List<GameObject> prefabs = new List<GameObject>();
        public PickMode pickMode = PickMode.Random;

        [Header("Spacing and placement")]
        [Range(5f, 500f)] public float spacing = 50f;
        [Tooltip("Random +/- jitter of the spacing, metres.")] [Range(0f, 50f)] public float spacingJitter = 6f;
        [Tooltip("Distance beyond the painted ROAD edge, metres.")] [Range(0f, 20f)] public float roadEdgeOffset = 1.5f;
        [Tooltip("Distance beyond the painted TRAIL edge, metres.")] [Range(0f, 20f)] public float trailEdgeOffset = 1.0f;
        public SideMode side = SideMode.Alternate;
        public bool includeRoads = true, includeTrails = true;

        [Header("Variation")]
        [Range(0f, 180f)] public float randomYaw = 180f;
        [Range(0f, 30f)] public float randomTilt = 4f;
        [Range(0.1f, 5f)] public float scaleMin = 0.9f, scaleMax = 1.1f;
        [Tooltip("Sink into the ground, metres (hides the base on slopes).")] [Range(0f, 1f)] public float sink = 0.05f;
        public int seed = 1234;

        [Header("Avoid")]
        [Tooltip("Minimum distance to any terrain tree (yours or scattered), metres.")] [Range(0f, 10f)] public float treeClearance = 1.8f;
        [Range(5f, 90f)] public float maxSlope = 40f;
        [Tooltip("Skip spots overlapping existing colliders (structures, props).")] public bool avoidColliders = true;

        [Header("Accumulation")]
        [Tooltip("Add ASHAccumulation to markers (URP/Lit parts get ash/snow; bright Simple Lit parts stay visible).")]
        public bool accumulate = true;

        [Serializable] public sealed class RouteOverride
        {
            [Tooltip("Route id or name from ASH_RegionLayout.json.")] public string route;
            public bool skip;
            [Tooltip("0 = use default spacing.")] [Range(0f, 500f)] public float spacing;
            [Tooltip("Negative = use default edge offset.")] public float edgeOffset = -1f;
            [Tooltip("Empty = use the default prefab list.")] public List<GameObject> prefabs = new List<GameObject>();
        }
        [Header("Per-route overrides")] public List<RouteOverride> overrides = new List<RouteOverride>();

        public void ApplyPreset(Preset p)
        {
            switch (p)
            {
                case Preset.Sparse: spacing = 90f; spacingJitter = 10f; break;
                case Preset.Default: spacing = 50f; spacingJitter = 6f; break;
                case Preset.Dense: spacing = 25f; spacingJitter = 3f; break;
                case Preset.Extreme: spacing = 8f; spacingJitter = 1f; side = SideMode.Both; break;
            }
        }
        [ContextMenu("Preset/Sparse")] void PS() => ApplyPreset(Preset.Sparse);
        [ContextMenu("Preset/Default")] void PD() => ApplyPreset(Preset.Default);
        [ContextMenu("Preset/Dense")] void PN() => ApplyPreset(Preset.Dense);
        [ContextMenu("Preset/Extreme")] void PE() => ApplyPreset(Preset.Extreme);
    }
}
