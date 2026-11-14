using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// Swappable footstep surface mapping (audio + puff flag per surface, and the rules that pick a surface).
    /// Resolution order (ASHFootsteps): terrain -> ash/snow deeper than Snow Depth Min = terrain default (Snow), else dominant
    /// terrain layer rule, else terrain default; meshes -> physic material, tag, layer, object/material name, else mesh default.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Footstep Surface Map", fileName = "ASH_FootstepSurfaceMap")]
    public sealed class ASHFootstepSurfaceMap : ScriptableObject
    {
        [Serializable] public sealed class Surface
        {
            public string id = "Snow";
            public AudioClip[] clips = new AudioClip[0];
            [Range(0f, 1f)] public float volume = 0.6f;
            public Vector2 pitch = new Vector2(0.92f, 1.08f);
            [Tooltip("Ash/snow puffs can spawn on this surface (still needs local depth).")] public bool puffs;
            [Tooltip("Extra volume at knee-deep ash/snow (louder, crunchier).")] [Range(0f, 2f)] public float deepVolumeBoost = 0.6f;
            [Tooltip("Pitch drop at knee-deep (crunchier, heavier).")] [Range(0f, 0.5f)] public float deepPitchDrop = 0.12f;
            [Tooltip("Optional second layer played under deep steps (scaled by depth).")] public AudioClip[] deepLayerClips = new AudioClip[0];
        }
        [Serializable] public sealed class Rule { public string surface = "Dirt"; [Tooltip("Case-insensitive 'name contains' keywords (tags: exact).")] public string[] keywords = new string[0]; }
        [Serializable] public sealed class LayerRule { public string surface = "Stone"; public LayerMask layers; }

        public List<Surface> surfaces = new List<Surface>();
        [Header("Terrain")]
        public string terrainDefault = "Snow";
        [Tooltip("On terrain, ash/snow at least this deep (m) always uses the terrain default (snow steps).")] [Range(0f, 0.3f)] public float snowDepthMin = 0.03f;
        [Tooltip("Main roads/trails (drift mask path factor >= Road Path Min) with less than Road Snow Depth use this surface (packed trail), and get no puffs.")]
        public string roadSurface = "Gravel";
        [Range(0f, 1f)] public float roadPathMin = 0.5f;
        [Range(0f, 0.5f)] public float roadSnowDepth = 0.08f;
        [Tooltip("Keyword on the dominant TerrainLayer name or its diffuse texture name.")] public List<Rule> terrainLayerRules = new List<Rule>();
        [Header("Meshes")]
        public List<Rule> physicMaterialRules = new List<Rule>();
        public List<Rule> tagRules = new List<Rule>();
        public List<LayerRule> layerRules = new List<LayerRule>();
        [Tooltip("Keyword on the GameObject, parent or renderer material name.")] public List<Rule> objectNameRules = new List<Rule>();
        public string meshDefault = "Stone";

        public Surface Get(string id)
        {
            for (int i = 0; i < surfaces.Count; i++) if (string.Equals(surfaces[i].id, id, StringComparison.OrdinalIgnoreCase)) return surfaces[i];
            return surfaces.Count > 0 ? surfaces[0] : null;
        }

        public static string Match(List<Rule> rules, string name, bool exact = false)
        {
            if (string.IsNullOrEmpty(name) || rules == null) return null;
            for (int i = 0; i < rules.Count; i++)
                foreach (var k in rules[i].keywords)
                    if (!string.IsNullOrEmpty(k) && (exact ? string.Equals(name, k, StringComparison.Ordinal) : name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) return rules[i].surface;
            return null;
        }
    }
}
