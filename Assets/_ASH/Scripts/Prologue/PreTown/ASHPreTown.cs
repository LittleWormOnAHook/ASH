using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Map;
using ASH.UI;
using ASH.Vehicles;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>
    /// t617 pre-town loop, all at runtime so nothing in the scene has to be saved:
    /// - copies of the truck parts are set down once per new game at nearby places (the world save keeps them as drops; the parts at
    ///   the cabin are untouched),
    /// - logging the Redstone lookout reveals the part sites on the map and marks them on the compass (Red Rock stays fogged),
    /// - an invisible ring (plus a barricade on the road side) keeps the player out of Red Rock until Grandpa's truck is ready,
    ///   then it is removed and Red Rock is revealed.
    /// Chain flags: lookoutLogged, reachedRedRock. Saves "preTown" (parts placed).
    /// </summary>
    public sealed class ASHPreTown : MonoBehaviour, IASHSaveable
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHPreTown() { Instance = null; }
        public static ASHPreTown Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            Ensure();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => Ensure();

        /// <summary>Added next to the prologue director (only scenes running the prologue get the loop).</summary>
        public static void Ensure()
        {
            if (Instance) return;
            var d = FindAnyObjectByType<ASHPrologueDirector>(); if (!d) return;
            if (!d.GetComponent<ASHPreTown>()) d.gameObject.AddComponent<ASHPreTown>();
        }

        bool _placed; float _spawnAt; float _slowT;
        Transform _wallRoot; bool _revealedTown; float _nextMsg;
        readonly HashSet<string> _markers = new HashSet<string>();
        static ASHPreTownSettings S => ASHPreTownSettings.I;

        void Awake() { Instance = this; _spawnAt = Time.unscaledTime + S.spawnDelay; }
        void OnEnable() { Instance = this; ASHSaveSystem.Register(this); }
        void OnDisable() { ASHSaveSystem.Unregister(this); if (Instance == this) Instance = null; var g = ASHGpsMap.Instance; if (g) g.SurveyLogged -= OnSurvey; _gpsHooked = false; }
        void OnDestroy() { if (_wallRoot) Destroy(_wallRoot.gameObject); foreach (var id in _markers) ASHCompass.Unregister(id); }

        bool _gpsHooked;
        void Update()
        {
            if (!S.preTownEnabled) { if (_wallRoot) { Destroy(_wallRoot.gameObject); _wallRoot = null; } return; }
            if (!_gpsHooked) { var g = ASHGpsMap.Instance; if (g) { g.SurveyLogged += OnSurvey; _gpsHooked = true; } }
            if (ASHSaveSystem.IsRestoring) return;
            if (!_placed && Time.unscaledTime >= _spawnAt && ASHSaveSystem.PendingLoad == null) { _placed = true; PlaceParts(); }
            _slowT -= Time.unscaledDeltaTime; if (_slowT > 0f) return; _slowT = 0.5f;
            bool ready = TruckReady;
            SyncWall(ready);
            SyncMarkers();
            if (ready && !_revealedTown) RevealTown();
            if (!ready && _wallRoot) BlockMessage();
        }

        // ------------------------------------------------------------------ flags / targets (chain)
        public static bool TruckReady { get { var d = ASHPrologueDirector.Instance; return d && d.ChainFlag("truckReady"); } }
        public static bool LookoutLogged { get { var g = ASHGpsMap.Instance; var def = g ? g.FindSurvey(S.lookoutSurveyId) : null; return def && g.IsLogged(def); } }

        /// <summary>Extra chain flags (the director forwards unknown ones here).</summary>
        public static bool ChainFlag(string f)
        {
            switch (f)
            {
                case "lookoutLogged": return LookoutLogged;
                case "reachedRedRock": { var p = PlayerPos(); return p.HasValue && Flat(p.Value - RedRockPos()).magnitude <= S.arriveRadius; }
            }
            return false;
        }

        /// <summary>Compass target of the pre-town steps (null = none).</summary>
        public static Vector3? TargetFor(string stepId)
        {
            if (stepId == "p07_lookout") { var sp = FindSurveyPoint(); if (sp) return sp.position; var g = ASHGpsMap.Instance; var def = g ? g.FindSurvey(S.lookoutSurveyId) : null; return def ? Ground(def.worldXZ) : (Vector3?)null; }
            if (stepId == "p08_drive") return RedRockPos();
            var part = S.Part(stepId); if (part == null) return null;
            return SpotPos(part);
        }

        static Transform FindSurveyPoint()
        {
            foreach (var s in FindObjectsByType<ASHSurveyPoint>(FindObjectsInactive.Exclude)) if (s.Definition && s.Definition.surveyId == S.lookoutSurveyId) return s.transform;
            return null;
        }

        static Vector3 RedRockPos()
        {
            var go = string.IsNullOrEmpty(S.redRockObject) ? null : GameObject.Find(S.redRockObject);
            return go ? go.transform.position : Ground(S.redRockXZ);
        }

        static Vector3 SpotPos(ASHPreTownSettings.PartSpot p)
        {
            var go = string.IsNullOrEmpty(p.anchorObject) ? null : GameObject.Find(p.anchorObject);
            return go ? go.transform.position : Ground(p.worldXZ);
        }

        static Vector3? PlayerPos()
        {
            var v = ASHVehicle.Driving; if (v) return v.transform.position;
            var inv = FindAnyObjectByType<ASHInventory>(); return inv ? inv.transform.position : (Vector3?)null;
        }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        static Vector3 Ground(Vector2 xz)
        {
            var top = new Vector3(xz.x, 2000f, xz.y);
            if (Physics.Raycast(top, Vector3.down, out var h, 4000f, ~0, QueryTriggerInteraction.Ignore)) return h.point;
            var t = Terrain.activeTerrain; return new Vector3(xz.x, t ? t.SampleHeight(new Vector3(xz.x, 0, xz.y)) + t.transform.position.y : 0f, xz.y);
        }

        // ------------------------------------------------------------------ part copies
        void PlaceParts()
        {
            var reg = ASHItemRegistry.I; if (!reg) return;
            var log = new System.Text.StringBuilder();
            foreach (var p in S.parts)
            {
                if (p == null || !p.spawnCopy || string.IsNullOrEmpty(p.itemId)) continue;
                var def = reg.Find(p.itemId); if (!def) { log.Append($" {p.itemId}: no item;"); continue; }
                var c = SpotPos(p);
                if (!FindSpot(c, p.searchRadius, out var at)) at = Ground(new Vector2(c.x, c.z)) + Vector3.up * 0.05f;
                var pk = ASHItemDrop.SpawnAt(def, 1, at + Vector3.up * 0.02f, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                if (pk) { pk.gameObject.name = "ASH_PreTown_" + p.stepId; log.Append($" {def.displayName} at {p.placeName} ({at.x:F0}, {at.y:F0}, {at.z:F0});"); }
            }
            Debug.Log("[ASH] Pre-town: truck part copies placed:" + log);
        }

        /// <summary>Clear, flat ground near c: rings outward from 3 m, first spot that is level enough and not inside a prop.</summary>
        static bool FindSpot(Vector3 c, float radius, out Vector3 at)
        {
            at = c;
            for (float r = 3f; r <= Mathf.Max(3f, radius); r += 2.5f)
                for (int k = 0; k < 12; k++)
                {
                    float a = (k / 12f + r * 0.13f) * Mathf.PI * 2f;
                    var top = new Vector3(c.x + Mathf.Cos(a) * r, c.y + 40f, c.z + Mathf.Sin(a) * r);
                    if (!Physics.Raycast(top, Vector3.down, out var h, 120f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Vector3.Angle(h.normal, Vector3.up) > 28f) continue;
                    if (Mathf.Abs(h.point.y - c.y) > 6f) continue; // roof tops / ledges
                    if (Physics.CheckSphere(h.point + Vector3.up * 0.45f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    at = h.point; return true;
                }
            return false;
        }

        // ------------------------------------------------------------------ lookout
        void OnSurvey(ASHSurveyPointDef d)
        {
            if (!d || d.surveyId != S.lookoutSurveyId || !S.preTownEnabled) return;
            var g = ASHGpsMap.Instance; if (!g) return;
            var names = new List<string>();
            foreach (var p in S.parts)
            {
                if (p == null) continue;
                var pos = SpotPos(p);
                if (p.revealRadius > 0f) g.RevealExtra(new Vector2(pos.x, pos.z), p.revealRadius);
                names.Add($"{Label(p.itemId)}: {p.placeName}");
            }
            g.MarkPois(S.lookoutPois);
            ASHJournalLog.Add("Truck parts", "From the tower I can make out where Grandpa left things.\n" + string.Join("\n", names), "pretown:parts");
            var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.Toast("Part sites marked on the map.");
            _slowT = 0f;
        }

        static string Label(string id) { var reg = ASHItemRegistry.I; var def = reg ? reg.Find(id) : null; return def ? def.displayName : id; }

        void SyncMarkers()
        {
            bool on = S.markPartsOnCompass && LookoutLogged;
            var r = ASHObjectiveChainRunner.Instance;
            foreach (var p in S.parts)
            {
                if (p == null) continue;
                string id = "pretown_" + p.stepId;
                bool want = on && r && !r.IsDone(p.stepId);
                if (want && !_markers.Contains(id)) { ASHCompass.Register(SpotPos(p), ASHCompassMarkerKind.Custom, S.partMarkerColor, id); _markers.Add(id); }
                else if (!want && _markers.Contains(id)) { ASHCompass.Unregister(id); _markers.Remove(id); }
            }
        }

        // ------------------------------------------------------------------ Red Rock block + reveal
        void SyncWall(bool ready)
        {
            bool want = S.roadBlock && !ready;
            if (!want) { if (_wallRoot) { Destroy(_wallRoot.gameObject); _wallRoot = null; Debug.Log("[ASH] Pre-town: Red Rock road block removed (truck ready)."); } return; }
            if (_wallRoot) return;
            var c = RedRockPos(); var pp = PlayerPos();
            if (S.skipIfPlayerInside && pp.HasValue && Flat(pp.Value - c).magnitude < S.blockRadius) { if (!_insideLogged) { _insideLogged = true; Debug.Log("[ASH] Pre-town: player is inside the Red Rock ring, road block not built."); } return; }
            BuildWall(c);
        }
        bool _insideLogged;

        void BuildWall(Vector3 c)
        {
            _wallRoot = new GameObject("ASH_PreTown_RedRockBlock").transform;
            int n = Mathf.Max(16, S.wallSegments); float R = S.blockRadius;
            float seg = 2f * Mathf.PI * R / n * 1.08f;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var g = Ground(new Vector2(c.x + Mathf.Cos(a) * R, c.z + Mathf.Sin(a) * R));
                var w = new GameObject("Wall" + i); w.transform.SetParent(_wallRoot, false);
                w.transform.position = g + Vector3.up * (S.wallHeight * 0.5f - 15f);
                w.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                var b = w.AddComponent<BoxCollider>(); b.size = new Vector3(seg, S.wallHeight + 30f, 1.5f);
            }
            // barricade on the road side
            Vector3 bp;
            if (S.barricadeAuto) { var dir = Flat(new Vector3(S.cabinXZ.x, 0, S.cabinXZ.y) - c).normalized; if (dir.sqrMagnitude < 0.5f) dir = Vector3.back; bp = Ground(new Vector2(c.x + dir.x * (R + 3f), c.z + dir.z * (R + 3f))); }
            else bp = Ground(S.barricadeXZ);
            BuildBarricade(bp, Quaternion.LookRotation(Flat(c - bp).normalized));
            Debug.Log($"[ASH] Pre-town: Red Rock road block up (ring {R:F0} m around {c.x:F0}, {c.z:F0}; barricade at {bp.x:F0}, {bp.y:F0}, {bp.z:F0}).");
        }

        void BuildBarricade(Vector3 at, Quaternion facing)
        {
            var root = new GameObject("Barricade").transform; root.SetParent(_wallRoot, false); root.SetPositionAndRotation(at, facing);
            int i = 0;
            foreach (var pf in S.barricadeProps) // optional props beside the logs (sign, fence)
            {
                if (!pf) continue;
                var x = Instantiate(pf, root); x.transform.localPosition = new Vector3((i % 2 == 0 ? 1f : -1f) * (5.5f + (i / 2) * 3f), 0f, -0.6f); x.transform.localRotation = Quaternion.identity; i++;
            }
            // simple log barricade: two posts, three stacked logs across the road, a plank sign
            var wood = new Color(0.33f, 0.22f, 0.13f);
            for (int s = -1; s <= 1; s += 2) Prim(PrimitiveType.Cylinder, root, new Vector3(s * 3.2f, 0.7f, 0f), Quaternion.identity, new Vector3(0.28f, 0.7f, 0.28f), wood);
            for (int k = 0; k < 3; k++) Prim(PrimitiveType.Cylinder, root, new Vector3(0f, 0.3f + k * 0.42f, -0.15f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.4f, 3.6f, 0.4f), wood * (0.9f + k * 0.07f));
            Prim(PrimitiveType.Cube, root, new Vector3(0f, 1.75f, -0.3f), Quaternion.identity, new Vector3(1.6f, 0.45f, 0.06f), new Color(0.55f, 0.42f, 0.28f));
        }

        static void Prim(PrimitiveType t, Transform parent, Vector3 lp, Quaternion lr, Vector3 ls, Color c)
        {
            var g = GameObject.CreatePrimitive(t); g.transform.SetParent(parent, false); g.transform.localPosition = lp; g.transform.localRotation = lr; g.transform.localScale = ls;
            var r = g.GetComponent<Renderer>(); if (r) { var sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh) { var m = new Material(sh); m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); r.sharedMaterial = m; } }
        }

        void BlockMessage()
        {
            if (Time.unscaledTime < _nextMsg) return;
            var p = PlayerPos(); if (!p.HasValue) return;
            float d = Flat(p.Value - RedRockPos()).magnitude - S.blockRadius;
            if (d < 0f || d > S.messageDistance) return;
            _nextMsg = Time.unscaledTime + S.messageCooldown;
            var hud = FindAnyObjectByType<ASHHud>(); if (hud && !string.IsNullOrEmpty(S.blockedMessage)) hud.Toast(S.blockedMessage);
        }

        void RevealTown()
        {
            _revealedTown = true;
            var g = ASHGpsMap.Instance; var c = RedRockPos();
            if (g && S.revealOnTruckReady > 0f) g.RevealExtra(new Vector2(c.x, c.z), S.revealOnTruckReady);
            ASHCompass.Register(c, ASHCompassMarkerKind.Poi, default, "pretown_redrock"); _markers.Add("pretown_redrock");
        }

        // ------------------------------------------------------------------ save
        [Serializable] sealed class State { public bool placed; }
        public string SaveKey => "preTown";
        public int RestoreOrder => 31;
        public string CaptureJson() => JsonUtility.ToJson(new State { placed = _placed });
        public void RestoreJson(string json) { var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json); _placed = s == null || s.placed; }
    }
}
