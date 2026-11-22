using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Map;
using ASH.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.Prologue
{
    /// <summary>Found pages of the story collections (Grandpa's Journal). Separate from the Survival Guide torn pages. Each found page
    /// writes a Notes entry. Saved ("journalPages"). One per scene, created automatically.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHGrandpaJournal : MonoBehaviour, IASHSaveable
    {
        static readonly HashSet<string> _found = new HashSet<string>();
        static readonly HashSet<string> _cleared = new HashSet<string>();
        public static event Action<string, string> PageFound; // collection, page id
        const float DefaultReach = 30f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _found.Clear(); _cleared.Clear(); PageFound = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }
        static void OnSceneLoaded(Scene s, LoadSceneMode m) => Ensure();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Ensure()
        {
            if (FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>() == null) return;
            if (FindAnyObjectByType<ASHGrandpaJournal>()) return;
            new GameObject("ASH_GrandpaJournal").AddComponent<ASHGrandpaJournal>();
        }
        static string Key(string c, string id) => (string.IsNullOrEmpty(c) ? "grandpa" : c) + "/" + id;
        static string MarkerId(ASHJournalCollectionPage p) => "gj_" + p.id;
        public static bool Has(string collection, string pageId) => !string.IsNullOrEmpty(pageId) && _found.Contains(Key(collection, pageId));
        public static int Count(string collection) { int n = 0; string p = (string.IsNullOrEmpty(collection) ? "grandpa" : collection) + "/"; foreach (var k in _found) if (k.StartsWith(p)) n++; return n; }

        /// <summary>Picks up a page: counts it and writes the Notes entry. False when it was already found.</summary>
        public static bool Find(string collection, string pageId)
        {
            if (string.IsNullOrEmpty(pageId) || !_found.Add(Key(collection, pageId))) return false;
            var col = ASHJournalCollection.Get(collection); var pg = col ? col.Page(pageId) : null;
            string title = pg != null ? $"{(col ? col.title : "Journal")} - p.{pg.number}: {pg.title}" : pageId;
            ASHJournalLog.Add(title, pg != null ? pg.text : "", Key(collection, pageId));
            var h = FindAnyObjectByType<ASHHud>(); if (h) h.Toast($"Found {(col ? col.title : "a journal")} page {(pg != null ? pg.number.ToString() : pageId)}{(pg != null ? " (" + pg.TypeLabel + ")" : "")}: {(pg != null ? pg.title : "")}  -  journal [J]");
            if (pg != null) Marker(pg);
            ASHLog.Info($"Journal page found: {Key(collection, pageId)}");
            var fm = ASH.Inventory.ASHFieldManual.Instance; if (fm) fm.Reevaluate(); // JournalPage unlock conditions
            PageFound?.Invoke(string.IsNullOrEmpty(collection) ? "grandpa" : collection, pageId);
            return true;
        }

        /// <summary>t443: clue pages point to the next area with a compass marker (when that area exists).</summary>
        static void Marker(ASHJournalCollectionPage p)
        {
            if (p == null || p.type != ASHJournalPageType.Clue || !p.compassMarker || p.markerPosition == Vector3.zero) return;
            string id = MarkerId(p);
            if (_cleared.Contains(id)) return;
            ASH.UI.ASHCompass.Unregister(id);
            ASH.UI.ASHCompass.Register(p.markerPosition, ASH.UI.ASHCompassMarkerKind.Poi, new Color(0.85f, 0.68f, 0.35f), id);
        }

        static void ClearMarker(ASHJournalCollectionPage p, string reason)
        {
            if (p == null) return;
            string id = MarkerId(p);
            if (!_cleared.Add(id)) return;
            ASH.UI.ASHCompass.Unregister(id);
            ASHLog.Info($"Journal clue marker cleared ({reason}): {id}");
        }

        static Vector3 PlayerPos()
        {
            if (ASHCompass.Instance != null)
            {
                var p = ASHCompass.Instance.DebugPlayerPos;
                if (p != Vector3.zero) return p;
            }
            var cam = Camera.main;
            return cam ? cam.transform.position : Vector3.zero;
        }

        void Update()
        {
            if (_found.Count == 0) return;
            var col = ASHJournalCollection.Grandpa; if (!col || col.pages == null) return;
            var pos = PlayerPos(); if (pos == Vector3.zero) return;
            foreach (var p in col.pages)
            {
                if (p == null || p.type != ASHJournalPageType.Clue || !p.compassMarker || p.markerPosition == Vector3.zero) continue;
                if (!_found.Contains(Key(col.collectionId, p.id))) continue;
                string id = MarkerId(p); if (_cleared.Contains(id)) continue;
                float r = p.markerReachRadius > 0f ? p.markerReachRadius : DefaultReach;
                var a = pos; var b = p.markerPosition; a.y = b.y = 0f;
                if ((a - b).sqrMagnitude <= r * r) ClearMarker(p, "arrived");
            }
        }

        void OnEnable() { _found.Clear(); _cleared.Clear(); ASHSaveSystem.Register(this); }
        void OnDisable() { ASHSaveSystem.Unregister(this); }
        [Serializable] sealed class State { public List<string> found = new List<string>(); public List<string> cleared = new List<string>(); }
        public string SaveKey => "journalPages";
        public int RestoreOrder => 22; // before the Field Manual re-evaluates? (it evaluates on demand) and before the prologue (30)
        public string CaptureJson() { var s = new State(); s.found.AddRange(_found); s.cleared.AddRange(_cleared); return JsonUtility.ToJson(s); }
        public void RestoreJson(string json)
        {
            _found.Clear(); _cleared.Clear(); var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json);
            if (s != null)
            {
                if (s.found != null) foreach (var k in s.found) _found.Add(k);
                if (s.cleared != null) foreach (var k in s.cleared) _cleared.Add(k);
            }
            var col = ASHJournalCollection.Grandpa; if (col) foreach (var p in col.pages) if (p != null && _found.Contains(Key(col.collectionId, p.id))) Marker(p);
        }
    }
}
