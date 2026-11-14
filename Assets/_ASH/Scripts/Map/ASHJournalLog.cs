using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.Map
{
    /// <summary>Field-journal notes (survey logs, discoveries, objective notes). Session-only for the slice.</summary>
    public sealed class ASHJournalLog : MonoBehaviour
    {
        [Serializable] public sealed class Entry { public string title; [TextArea] public string body; public string stamp; }

        public static ASHJournalLog Instance { get; private set; }
        [SerializeField] private List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;
        public event Action<Entry> Added;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public static void Add(string title, string body)
        {
            var log = Instance != null ? Instance : FindAnyObjectByType<ASHJournalLog>();
            if (log == null) return;
            var clock = FindAnyObjectByType<ASH.World.ASHDayNightClock>();
            string stamp = clock ? $"{(int)clock.timeOfDay:00}:{(int)((clock.timeOfDay % 1f) * 60f):00}" : "";
            var e = new Entry { title = title, body = body, stamp = stamp };
            log.entries.Add(e);
            log.Added?.Invoke(e);
        }
    }
}
