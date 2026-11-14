using System.Collections.Generic;
using UnityEngine;

namespace ASH.World
{
    /// <summary>Which terrain tree/detail prototypes ASH/World/Scatter Vegetation added, so "Clear ASH-scattered"
    /// removes only those (Anthony's prototypes and instances are never touched).</summary>
    public sealed class ASHVegetationRecord : ScriptableObject
    {
        public string terrainDataGuid;
        public List<int> treeIndices = new List<int>();
        public List<GameObject> treePrefabs = new List<GameObject>();
        public List<int> detailIndices = new List<int>();
        public List<Object> detailSources = new List<Object>();
        [HideInInspector] public List<Vector2> placed = new List<Vector2>(); // normalized x,z of every ASH-placed tree
        [HideInInspector] public List<Vector2> removedSpots = new List<Vector2>(); // Anthony deleted an ASH tree here: never replant (2.5 m)
        public int treesPlaced;
        public long grassPlaced;
        public string lastRun;
    }
}
