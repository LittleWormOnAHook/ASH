using System;
using UnityEngine;
using ASH.World.Weather;

namespace ASH.Opening
{
    /// <summary>All timings and texts of the Phase 2A opening ("The Last Normal Morning"). One asset, wide ranges.</summary>
    [CreateAssetMenu(menuName = "ASH/Opening Profile", fileName = "ASH_OpeningProfile")]
    public sealed class ASHOpeningProfile : ScriptableObject
    {
        [Serializable] public sealed class Line { public string speaker = "Radio"; [TextArea(2, 4)] public string text; [Range(1f, 30f)] public float seconds = 6f; }

        [Header("Stage 1 - The last normal morning")]
        [Tooltip("Peaceful time before the broadcast (s).")] [Range(5f, 900f)] public float peacefulSeconds = 75f;
        [Tooltip("Time of day the opening starts at (hours). < 0 keeps the clock's value.")] [Range(-1f, 24f)] public float startTimeOfDay = 7.6f;
        [Tooltip("Ground ash/snow at the start of the opening (runtime only). < 0 keeps the scene values.")]
        [Range(-1f, 1f)] public float groundAshAtStart = 0f;
        [Range(-1f, 1f)] public float groundSnowAtStart = 0f;
        [Range(0f, 1f)] public float musicVolume = 0.28f;

        [Header("Stage 2 - Emergency broadcast")]
        [Tooltip("Music fade before the alert (s).")] [Range(0.1f, 10f)] public float musicFadeSeconds = 1.5f;
        [Range(1f, 30f)] public float alertSeconds = 7f;
        [Range(0f, 1f)] public float broadcastVolume = 0.75f;
        [Range(0f, 1f)] public float staticVolume = 0.12f;
        [Tooltip("Random signal dropouts per minute during the announcement.")] [Range(0f, 60f)] public float dropoutsPerMinute = 6f;
        public Line[] broadcast =
        {
            new Line { text = "This is an emergency broadcast for the southern Colorado region.", seconds = 5f },
            new Line { text = "The U.S. Geological Survey reports extraordinary seismic activity and a major volcanic eruption in the Yellowstone region.", seconds = 8f },
            new Line { text = "Volcanic ash may fall across the area over the coming hours. Ash can damage lungs, engines and roofs.", seconds = 7f },
            new Line { text = "Avoid unnecessary travel. Stay indoors and keep doors and windows closed.", seconds = 6f },
            new Line { text = "Exposed water may become contaminated. Store clean drinking water now and keep it covered.", seconds = 7f },
            new Line { text = "Stay tuned to this station for further updates.", seconds = 5f },
        };
        [Tooltip("Broken fragments played between bursts of static after the shaking.")]
        public Line[] afterQuake =
        {
            new Line { text = "...repeat... remain indoors...", seconds = 4f },
            new Line { text = "...ashfall expected... cover your mouth and nose...", seconds = 5f },
            new Line { text = "...further information... as it becomes...", seconds = 4f },
        };

        [Header("Stage 3 - Shaking")]
        [Tooltip("Shaking starts this long after the broadcast voice begins (s).")] [Range(0f, 120f)] public float quakeDelay = 14f;

        [Header("Stage 4 - First ash")]
        [Tooltip("Quiet after the shaking (s).")] [Range(0f, 300f)] public float quietSeconds = 20f;
        [Tooltip("Blend into hazy light / first dust during the quiet (s).")] [Range(1f, 600f)] public float hazeSeconds = 40f;
        [Tooltip("Ashfall onset: time after the quiet starts until ash begins to build (s).")] [Range(0f, 600f)] public float ashOnsetSeconds = 20f;
        [Tooltip("Ramp from first dust to Light Ash (s).")] [Range(5f, 1800f)] public float ashRampSeconds = 240f;

        [Header("Stage 5 - First heavy fallout wave")]
        [Range(5f, 900f)] public float heavyRampSeconds = 90f;
        [Range(5f, 3600f)] public float heavyWaveSeconds = 180f;
        [Tooltip("Easing back to Calm/Hazy (s).")] [Range(5f, 900f)] public float easeSeconds = 120f;
        public string objectiveTitle = "Secure the cabin";
        public string objectiveMessage = "Secure the cabin and prepare for worsening conditions.";
        [TextArea(2, 6)] public string journalBroadcastTitle = "Emergency broadcast";
        [TextArea(3, 8)] public string journalBroadcastBody = "Radio: extraordinary seismic activity and a major eruption in the Yellowstone region. Ash may fall over the coming hours. Avoid travel, stay indoors, and store and cover clean drinking water - exposed water may be contaminated.";
        [TextArea(2, 6)] public string journalWaveTitle = "First fallout";
        [TextArea(3, 8)] public string journalWaveBody = "The sky went dark by mid-morning. Ash is coming down like dirty snow and the wind is picking it up off the ground. Stay in, collect water and food, wait for it to ease.";

        [Header("After the opening")]
        [Tooltip("Hand weather over to the scheduler (waves) once the first wave has eased.")] public bool startSchedulerAfter = true;
        public ASHWeatherKind afterState = ASHWeatherKind.CalmHazy;
    }
}
