using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Map
{
    [Serializable] public struct ASHRevealCircle { public Vector2 center; [Min(1f)] public float radius; }

    [Serializable]
    public sealed class ASHRevealPath
    {
        public string name;
        public List<Vector2> points = new List<Vector2>();
        [Min(5f)] public float width = 120f;
    }

    public enum ASHSurveyKind { TopoPanel, SurveyMarker, TrailRegister, RadioRelay }

    /// <summary>
    /// One survey point (high POI). Logging it with the damaged GPS reveals the listed map regions and points the
    /// player at the next destination. World coordinates are terrain X/Z in metres (north = +Z).
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Map/Survey Point", fileName = "ASH_Survey_New")]
    public sealed class ASHSurveyPointDef : ScriptableObject
    {
        public string surveyId = "survey";
        public string displayName = "Survey point";
        [Tooltip("Structure the player climbs (lookout tower, ranger-station roof...).")]
        public string structureName = "Lookout";
        public ASHSurveyKind kind = ASHSurveyKind.TopoPanel;
        [Min(1)] public int order = 1;
        public Vector2 worldXZ;
        [TextArea(3, 6)] public string journalEntry;
        [Tooltip("Shown as the next destination after logging.")]
        public string nextHint;
        public List<string> unlocksPois = new List<string>();
        public List<ASHRevealCircle> circles = new List<ASHRevealCircle>();
        public List<ASHRevealPath> paths = new List<ASHRevealPath>();
        [Tooltip("Late-game reward: reveals the whole region map.")]
        public bool revealAll;
    }
}
