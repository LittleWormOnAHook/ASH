using ASH.World;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>Game-clock time for survival timers (ASHDayNightClock.timeOfDay). Delta per frame in game hours, midnight wrap
    /// handled; a jump of more than maxJumpHours in one frame (loading a save, debug time set) counts as 0. A frozen clock
    /// (minutesPerDay 0) freezes the timers. Without a clock in the scene: the default rate (48 min day).</summary>
    public static class ASHGameTime
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHGameTime()
        {
            _delta = default;
        }

        const float MaxJumpHours = 0.5f, DefaultMinutesPerDay = 48f;
        static ASHDayNightClock _clock; static float _last = -1f; static int _frame = -1; static float _delta;

        public static ASHDayNightClock Clock { get { if (!_clock) _clock = Object.FindAnyObjectByType<ASHDayNightClock>(); return _clock; } }

        /// <summary>Game hours elapsed this frame (same value for every caller in a frame).</summary>
        public static float DeltaHours
        {
            get
            {
                if (_frame == Time.frameCount) return _delta;
                _frame = Time.frameCount; _delta = 0f;
                var c = Clock;
                if (!c)
                {
                    _last = -1f;
                    _delta = Time.deltaTime * 24f / (DefaultMinutesPerDay * 60f);
                    return _delta;
                }
                float t = c.timeOfDay;
                if (_last >= 0f)
                {
                    float d = t - _last;
                    if (d < -12f) d += 24f;          // midnight
                    if (d < 0f || d > MaxJumpHours) d = 0f;
                    _delta = d;
                }
                _last = t;
                return _delta;
            }
        }

        /// <summary>Game hours per real second at the current clock rate (0 when frozen).</summary>
        public static float HoursPerSecond { get { var c = Clock; float mpd = c ? c.minutesPerDay : DefaultMinutesPerDay; return mpd <= 0f ? 0f : 24f / (mpd * 60f); } }

        /// <summary>h:mm of game time.</summary>
        public static string Format(float hours)
        {
            int m = Mathf.Max(0, Mathf.CeilToInt(hours * 60f - 0.001f));
            return $"{m / 60}:{m % 60:00}";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { _clock = null; _last = -1f; _frame = -1; }
    }
}
