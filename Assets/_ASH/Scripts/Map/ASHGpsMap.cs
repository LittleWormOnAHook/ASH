using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Map
{
    /// <summary>
    /// The damaged GPS: holds the region map, fog of war and logged survey points. Starts nearly blank (a small
    /// patch around the start), records a coarse track as the player walks, and opens up regions when survey
    /// points are logged. Position is only approximate: a drifting estimate inside an uncertainty circle.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ASHGpsMap : MonoBehaviour
    {
        public static ASHGpsMap Instance { get; private set; }

        [SerializeField] private ASHRegionMap region;
        [SerializeField] private Transform player;
        [Range(2f, 50f)] [SerializeField] private float walkStampSpacing = 12f;

        public ASHRegionMap Region => region;
        public Transform Player => player;
        public ASHMapFog Fog { get; private set; }
        public event Action<ASHSurveyPointDef> SurveyLogged;
        public event Action Revealed;
        readonly HashSet<string> _logged = new HashSet<string>();
        readonly HashSet<string> _knownPois = new HashSet<string>();
        Vector2 _lastStamp = new Vector2(float.MaxValue, float.MaxValue);

        public int LoggedCount => _logged.Count;
        public int SurveyCount => region ? region.surveys.Count : 0;
        public bool IsLogged(ASHSurveyPointDef d) => d != null && _logged.Contains(d.surveyId);
        public bool IsPoiKnown(string id) => _knownPois.Contains(id);

        private void Awake()
        {
            Instance = this;
            if (region == null) { enabled = false; return; }
            Fog = new ASHMapFog(region.fogResolution, region.worldMin, region.worldSize);
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            if (player == null) { var p = GameObject.Find("ASH_Player"); if (p) player = p.transform; }
            if (player) { Fog.RevealCircle(XZ(player.position), region.startRevealRadius); MarkKnownPois(); }
            Fog.Upload();
        }

        private void Update()
        {
            if (player && region.walkRevealRadius > 0f)
            {
                var p = XZ(player.position);
                if ((p - _lastStamp).sqrMagnitude >= walkStampSpacing * walkStampSpacing)
                {
                    Fog.RevealSegment(_lastStamp.x > 1e8f ? p : _lastStamp, p, region.walkRevealRadius);
                    _lastStamp = p;
                }
            }
            Fog.Upload();
        }

        public static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>Approximate GPS fix: true position plus slow drift, with the uncertainty radius in metres.</summary>
        public Vector2 ApproxPosition(out float uncertainty)
        {
            uncertainty = region ? region.gpsUncertainty : 50f;
            if (!player) return Vector2.zero;
            float t = Time.unscaledTime * 0.05f, drift = region ? region.gpsDrift : 0f;
            var off = new Vector2(Mathf.PerlinNoise(t, 3.1f) - 0.5f, Mathf.PerlinNoise(7.7f, t) - 0.5f) * 2f * drift;
            return XZ(player.position) + off;
        }

        public bool LogSurvey(ASHSurveyPointDef d)
        {
            if (d == null || region == null || _logged.Contains(d.surveyId)) return false;
            _logged.Add(d.surveyId);
            if (d.revealAll) Fog.RevealAll();
            foreach (var c in d.circles) Fog.RevealCircle(c.center, c.radius);
            foreach (var path in d.paths)
                for (int i = 0; i + 1 < path.points.Count; i++) Fog.RevealSegment(path.points[i], path.points[i + 1], path.width * 0.5f);
            Fog.RevealCircle(d.worldXZ, 160f);
            Fog.Upload();
            var newPois = new List<string>();
            foreach (var id in d.unlocksPois) { var poi = region.FindPoi(id); if (poi != null && _knownPois.Add(id)) newPois.Add(poi.displayName); }
            MarkKnownPois();
            string body = d.journalEntry;
            if (newPois.Count > 0) body += "\n<color=#c9a46a>Marked:</color> " + string.Join(", ", newPois);
            if (!string.IsNullOrEmpty(d.nextHint)) body += "\n<color=#c9a46a>Next:</color> " + d.nextHint;
            ASHJournalLog.Add($"Survey {d.order}: {d.displayName}", body);
            SurveyLogged?.Invoke(d);
            Revealed?.Invoke();
            return true;
        }

        void MarkKnownPois()
        {
            foreach (var p in region.pois) if (Fog.IsRevealed(p.worldXZ, 0.5f)) _knownPois.Add(p.id);
        }

        public ASHSurveyPointDef FindSurvey(string id) => region ? region.surveys.Find(s => s.surveyId == id) : null;
    }
}
