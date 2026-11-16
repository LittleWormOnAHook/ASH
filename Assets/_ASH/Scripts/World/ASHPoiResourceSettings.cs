using System;
using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.World
{
    /// <summary>
    /// Settings for ASH/Systems/Resources/Scatter Around POIs: counts, radius, ground rules and the supply-crate loot table.
    /// Wide ranges. A 7-entry loot table is filled by the installer if left empty.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/World/POI Resource Settings", fileName = "ASH_PoiResourceSettings")]
    public sealed class ASHPoiResourceSettings : ScriptableObject
    {
        [Serializable]
        public sealed class LootEntry
        {
            public ASHItemDefinition item;
            [Range(0f, 100f)] public float weight = 1f;
            [Range(1, 20)] public int amountMin = 1;
            [Range(1, 20)] public int amountMax = 2;
        }

        [Header("Placement (metres)")]
        [Tooltip("Items are placed in a ring around each POI_ object, outside the keep-out and inside this radius.")]
        [Range(5f, 200f)] public float radius = 40f;
        [Tooltip("Nothing is placed this close to the POI origin (keeps items out of cabins / markers).")]
        [Range(0f, 40f)] public float poiKeepout = 8f;
        [Range(5f, 70f)] public float maxSlope = 28f;
        [Tooltip("Never closer than this to a terrain tree (Anthony's and ASH trees).")]
        [Range(0.5f, 10f)] public float minTreeDistance = 1.6f;
        [Tooltip("Keep off existing interactables, buildings and other placed items.")]
        [Range(0.5f, 20f)] public float minObjectDistance = 2.2f;
        [Range(0.5f, 20f)] public float minSpacing = 2.2f;
        [Range(1, 200)] public int attemptsPerItem = 40;
        [Tooltip("0 = a new seed each run.")]
        public int seed = 17;

        [Header("Counts per POI (then scaled by the POI's type)")]
        [Range(0, 80)] public int stonePerPoi = 4;
        [Range(0, 80)] public int berriesPerPoi = 3;
        [Range(0, 80)] public int woodPerPoi = 4;
        [Range(0, 80)] public int kindlingPerPoi = 5;
        [Range(0, 80)] public int foodPerPoi = 3;
        [Range(0, 20)] public int cratesPerPoi = 1;

        [Header("Prefabs (filled by the scatter menu if empty)")]
        public GameObject stonePrefab;
        public GameObject berryPrefab;
        public GameObject kindlingPrefab;
        public GameObject cratePrefab;
        public GameObject[] woodPrefabs;
        public GameObject[] foodPrefabs;

        [Header("Supply crate loot (wide, adjustable)")]
        [Range(1, 12)] public int crateLootMin = 2;
        [Range(1, 12)] public int crateLootMax = 5;
        public List<LootEntry> crateLoot = new List<LootEntry>();
    }
}
