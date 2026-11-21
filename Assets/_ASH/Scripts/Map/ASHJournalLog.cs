using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Map
{
    /// <summary>Field-journal notes (survey logs, discoveries, objective notes). Session-only for the slice.</summary>
    public sealed class ASHJournalLog : MonoBehaviour
    {
        [Serializable] public sealed class Entry { public string id; public string title; [TextArea] public string body; public string stamp; }

        public static ASHJournalLog Instance { get; private set; }
        [SerializeField] private List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;
        public event Action<Entry> Added;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        static string IdOf(Entry e) => e == null ? "" : (!string.IsNullOrEmpty(e.id) ? e.id : (e.title ?? ""));

        /// <summary>Save restore: replaces all entries and drops duplicates by id (empty id falls back to title).</summary>
        public void RestoreEntries(List<Entry> list)
        {
            entries.Clear();
            if (list == null) return;
            var seen = new HashSet<string>();
            foreach (var e in list)
            {
                if (e == null) continue;
                if (string.IsNullOrEmpty(e.id)) e.id = e.title;
                string id = IdOf(e);
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                entries.Add(e);
            }
        }

        public static void Add(string title, string body, string id = null)
        {
            var log = Instance != null ? Instance : FindAnyObjectByType<ASHJournalLog>();
            if (log == null) return;
            if (string.IsNullOrEmpty(id)) id = title;
            foreach (var existing in log.entries)
            {
                if (existing == null) continue;
                if (IdOf(existing) == id) return;
                if (!string.IsNullOrEmpty(title) && existing.title == title) return;
            }
            var clock = FindAnyObjectByType<ASH.World.ASHDayNightClock>();
            string stamp = clock ? $"{(int)clock.timeOfDay:00}:{(int)((clock.timeOfDay % 1f) * 60f):00}" : "";
            var e = new Entry { id = id, title = title, body = body, stamp = stamp };
            log.entries.Add(e);
            log.Added?.Invoke(e);
        }
    }
}
