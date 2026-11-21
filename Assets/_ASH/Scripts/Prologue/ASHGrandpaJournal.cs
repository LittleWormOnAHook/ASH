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
        public static event Action<string, string> PageFound; // collection, page id
        static bool _hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _found.Clear(); PageFound = null; _hooked = false; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { if (_hooked) return; _hooked = true; SceneManager.sceneLoaded += (s, m) => Ensure(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Ensure()
        {
            if (FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>() == null) return;
            if (FindAnyObjectByType<ASHGrandpaJournal>()) return;
            new GameObject("ASH_GrandpaJournal").AddComponent<ASHGrandpaJournal>();
        }
        static string Key(string c, string id) => (string.IsNullOrEmpty(c) ? "grandpa" : c) + "/" + id;
        public static bool Has(string collection, string pageId) => !string.IsNullOrEmpty(pageId) && _found.Contains(Key(collection, pageId));
        public static int Count(string collection) { int n = 0; string p = (string.IsNullOrEmpty(collection) ? "grandpa" : collection) + "/"; foreach (var k in _found) if (k.StartsWith(p)) n++; return n; }

        /// <summary>Picks up a page: counts it and writes the Notes entry. False when it was already found.</summary>
        public static bool Find(string collection, string pageId)
        {
            if (string.IsNullOrEmpty(pageId) || !_found.Add(Key(collection, pageId))) return false;
            var col = ASHJournalCollection.Get(collection); var pg = col ? col.Page(pageId) : null;
            string title = pg != null ? $"{(col ? col.title : "Journal")} - p.{pg.number}: {pg.title}" : pageId;
            ASHJournalLog.Add(title, pg != null ? pg.text : "");
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
            ASH.UI.ASHCompass.Unregister("gj_" + p.id);
            ASH.UI.ASHCompass.Register(p.markerPosition, ASH.UI.ASHCompassMarkerKind.Poi, new Color(0.85f, 0.68f, 0.35f), "gj_" + p.id);
        }

        void OnEnable() { _found.Clear(); ASHSaveSystem.Register(this); }
        void OnDisable() { ASHSaveSystem.Unregister(this); }
        [Serializable] sealed class State { public List<string> found = new List<string>(); }
        public string SaveKey => "journalPages";
        public int RestoreOrder => 22; // before the Field Manual re-evaluates? (it evaluates on demand) and before the prologue (30)
        public string CaptureJson() { var s = new State(); s.found.AddRange(_found); return JsonUtility.ToJson(s); }
        public void RestoreJson(string json)
        {
            _found.Clear(); var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json);
            if (s != null) foreach (var k in s.found) _found.Add(k);
            var col = ASHJournalCollection.Grandpa; if (col) foreach (var p in col.pages) if (p != null && _found.Contains(Key(col.collectionId, p.id))) Marker(p);
        }
    }
}
