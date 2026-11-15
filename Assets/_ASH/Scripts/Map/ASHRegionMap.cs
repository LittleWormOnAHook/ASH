using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Map
{
    public enum ASHPoiKind { Camp, Survey, Building, Crossing, Ruin, Landmark, Supply }

    [Serializable]
    public sealed class ASHPoi
    {
        public string id;
        public string displayName;
        public ASHPoiKind kind;
        public Vector2 worldXZ;
    }

    /// <summary>
    /// Region map data: the hand-drawn GPS map art, the world rectangle it covers, POIs and survey points.
    /// Built from Assets/_ASH/Data/Map/ASH_RegionLayout.json by the editor region builder.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Map/Region Map", fileName = "ASH_RegionMap")]
    public sealed class ASHRegionMap : ScriptableObject
    {
        public string regionName = "Ash Region";
        public Texture2D mapTexture;
        [Tooltip("World X/Z of the map's bottom-left (south-west) corner.")]
        public Vector2 worldMin = Vector2.zero;
        public Vector2 worldSize = new Vector2(4000f, 4000f);
        [Header("GPS (damaged)")]
        [Range(20f, 1000f)] public float startRevealRadius = 140f;
        [Range(0f, 200f)] public float walkRevealRadius = 30f;
        [Range(5f, 300f)] public float gpsUncertainty = 70f;
        [Range(0f, 100f)] public float gpsDrift = 25f;
        [Range(64, 2048)] public int fogResolution = 512;
        [Header("Fog of war look")]
        [Tooltip("Opacity of the fog over unexplored ground. 1 = fully solid (nothing of the map shows through).")]
        [Range(0f, 1f)] public float fogOpacity = 1f;
        [Tooltip("Width of the soft edge between explored and unexplored, as a fraction of each reveal radius (0 = hard edge, 1 = fades over the whole radius).")]
        [Range(0f, 1f)] public float fogEdgeSoftness = 0.35f;
        [Tooltip("Minimum soft-edge width in metres (keeps small walk reveals from getting a hard rim).")]
        [Range(0f, 200f)] public float fogEdgeMinMetres = 8f;
        [Tooltip("Brightness flicker of the fog pattern (damaged GPS feel). Only affects brightness, never opacity.")]
        [Range(0f, 0.5f)] public float fogFlicker = 0.1f;
        public List<ASHPoi> pois = new List<ASHPoi>();
        public List<ASHSurveyPointDef> surveys = new List<ASHSurveyPointDef>();

        public Vector2 WorldToUV(Vector2 xz) => new Vector2((xz.x - worldMin.x) / worldSize.x, (xz.y - worldMin.y) / worldSize.y);
        public Vector2 UVToWorld(Vector2 uv) => new Vector2(worldMin.x + uv.x * worldSize.x, worldMin.y + uv.y * worldSize.y);
        public ASHPoi FindPoi(string id) => pois.Find(p => p.id == id);
    }
}
