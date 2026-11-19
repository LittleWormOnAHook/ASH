using System;
using System.Collections.Generic;
using ASH.Core;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Lightweight learn-by-doing skills (Cooking first; reusable for any later skill). XP per action -> levels on a curve
    /// (XP for level L -> L+1 = baseXp * L^exponent, up to maxLevel), plus permanent "learned" keys (cooking combos etc).
    /// Systems register a curve with <see cref="Define"/> (live tuning via a Func), add XP with <see cref="AddXp"/>.
    /// Saved as the "skills" section (<see cref="ASHSkillSave"/>); reset on a new game. Old saves without it start at level 1.
    /// </summary>
    public static class ASHSkills
    {
        public const string Cooking = "cooking";

        public struct Curve { public string name; public float baseXp; public float exponent; public int maxLevel; }

        static readonly Dictionary<string, Func<Curve>> _defs = new Dictionary<string, Func<Curve>>();
        static readonly Dictionary<string, float> _xp = new Dictionary<string, float>();
        static readonly HashSet<string> _learned = new HashSet<string>();

        /// <summary>(skill id, new level)</summary>
        public static event Action<string, int> LevelUp;
        /// <summary>A key was learned for the first time (e.g. "combo:meatstew").</summary>
        public static event Action<string> Learned;
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _xp.Clear(); _learned.Clear(); LevelUp = null; Learned = null; Changed = null; }

        public static void Define(string id, Func<Curve> curve) { if (!string.IsNullOrEmpty(id) && curve != null) _defs[id] = curve; }
        public static IEnumerable<string> Defined => _defs.Keys;

        public static Curve CurveOf(string id)
        {
            Curve c = _defs.TryGetValue(id, out var f) ? f() : new Curve { name = id, baseXp = 40f, exponent = 1.5f, maxLevel = 20 };
            if (string.IsNullOrEmpty(c.name)) c.name = id;
            c.baseXp = Mathf.Max(1f, c.baseXp); c.exponent = Mathf.Max(0f, c.exponent); c.maxLevel = Mathf.Max(1, c.maxLevel);
            return c;
        }

        public static string NameOf(string id) => CurveOf(id).name;
        public static float Xp(string id) => _xp.TryGetValue(id, out var v) ? v : 0f;

        /// <summary>XP needed to go from level L to L+1.</summary>
        public static float XpForNext(string id, int level) { var c = CurveOf(id); return c.baseXp * Mathf.Pow(Mathf.Max(1, level), c.exponent); }

        public static int Level(string id)
        {
            var c = CurveOf(id); float xp = Xp(id); int l = 1;
            while (l < c.maxLevel) { float need = c.baseXp * Mathf.Pow(l, c.exponent); if (xp < need) break; xp -= need; l++; }
            return l;
        }

        /// <summary>XP into the current level and XP that level needs (need = 0 at max level).</summary>
        public static void Progress(string id, out int level, out float into, out float need)
        {
            var c = CurveOf(id); float xp = Xp(id); level = 1;
            while (level < c.maxLevel) { float n = c.baseXp * Mathf.Pow(level, c.exponent); if (xp < n) break; xp -= n; level++; }
            into = xp; need = level >= c.maxLevel ? 0f : c.baseXp * Mathf.Pow(level, c.exponent);
        }

        public static void AddXp(string id, float amount)
        {
            if (string.IsNullOrEmpty(id) || amount <= 0f) return;
            int before = Level(id);
            _xp[id] = Xp(id) + amount;
            int after = Level(id);
            Changed?.Invoke();
            for (int l = before + 1; l <= after; l++) LevelUp?.Invoke(id, l);
        }

        public static bool Knows(string key) => !string.IsNullOrEmpty(key) && _learned.Contains(key);
        public static IEnumerable<string> LearnedKeys => _learned;
        /// <summary>Learns a key permanently. True when it was new.</summary>
        public static bool Learn(string key)
        {
            if (string.IsNullOrEmpty(key) || !_learned.Add(key)) return false;
            Learned?.Invoke(key); Changed?.Invoke(); return true;
        }

        public static void ResetAll() { _xp.Clear(); _learned.Clear(); Changed?.Invoke(); }

        // ---- save ----
        [Serializable] internal sealed class Entry { public string id; public float xp; }
        [Serializable] internal sealed class State { public List<Entry> xp = new List<Entry>(); public List<string> learned = new List<string>(); }

        internal static string Capture()
        {
            var s = new State();
            foreach (var kv in _xp) s.xp.Add(new Entry { id = kv.Key, xp = kv.Value });
            s.learned.AddRange(_learned);
            return JsonUtility.ToJson(s);
        }

        internal static void Restore(string json)
        {
            _xp.Clear(); _learned.Clear();
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json);
            if (s != null)
            {
                if (s.xp != null) foreach (var e in s.xp) if (e != null && !string.IsNullOrEmpty(e.id)) _xp[e.id] = Mathf.Max(0f, e.xp);
                if (s.learned != null) foreach (var k in s.learned) if (!string.IsNullOrEmpty(k)) _learned.Add(k);
            }
            Changed?.Invoke();
        }
    }

    /// <summary>"skills" save section (registered by ASHSaveSystem).</summary>
    public sealed class ASHSkillSave : IASHSaveable
    {
        public string SaveKey => "skills";
        public int RestoreOrder => 5;
        public string CaptureJson() => ASHSkills.Capture();
        public void RestoreJson(string json) => ASHSkills.Restore(json);
    }
}
