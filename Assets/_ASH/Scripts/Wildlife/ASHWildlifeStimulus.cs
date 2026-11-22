using UnityEngine;

namespace ASH.Wildlife
{
    /// <summary>Cheap shared world events for wildlife senses: gunshots and a drifting wind direction for scent.</summary>
    public static class ASHWildlifeStimulus
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHWildlifeStimulus()
        {
            LastGunshotPos = default;
            LastGunshotTime = -999f;
            LastGunshotRange = 80f;
            LastGunshotLoud = default;
        }

        public static Vector3 LastGunshotPos;
        public static float LastGunshotTime = -999f;
        public static float LastGunshotRange = 80f;
        public static bool LastGunshotLoud;

        public static void Gunshot(Vector3 pos, float range, bool loud)
        {
            LastGunshotPos = pos;
            LastGunshotTime = Time.time;
            LastGunshotRange = range;
            LastGunshotLoud = loud;
        }

        public static Vector3 WindDir
        {
            get
            {
                var w = ASHWildlifeSettings.I;
                float deg = w != null ? w.windDegrees : 40f;
                float spin = w != null ? w.windSpinPerMinute : 8f;
                deg += Time.time * (spin / 60f);
                float r = deg * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
            }
        }
    }
}
