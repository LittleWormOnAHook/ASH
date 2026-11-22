using System.Collections.Generic;
using UnityEngine;

namespace ASH.Wildlife.Birds
{
    /// <summary>
    /// Drop-in roost driven by the Bird Flocks vendor scripts: when the player comes within range the bird manager spawns a vendor
    /// flock plus its Landing Spot Controller here, moves the landing spots onto real perches (terrain, rock / fence / roof top edges)
    /// around the spot, and scares them off on gunshots or when the player gets close. Shot birds fall and can be harvested.
    /// The vendor prefabs are set in ASH_BirdSettings (Vendor roosts), so this prefab has no vendor references itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHBirdRoostSpot : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHBirdRoostSpot()
        {
            All.Clear();
        }

        public static readonly List<ASHBirdRoostSpot> All = new List<ASHBirdRoostSpot>();

        public enum Style { Crow, Seagull }
        public Style style = Style.Crow;
        [Range(1, 60)] public int birdsMin = 6;
        [Range(1, 80)] public int birdsMax = 12;
        [Tooltip("Perch search radius for the landing spots.")]
        [Range(2f, 60f)] public float radius = 10f;
        [Tooltip("Flock circles this high above the spot while not perched.")]
        [Range(3f, 80f)] public float flockHeight = 14f;
        [Range(20f, 500f)] public float activateDistance = 160f;
        [Tooltip("Birds lift off when the player is this close (x1.4 sprinting, x0.6 crouched).")]
        [Range(1f, 60f)] public float scareDistance = 14f;

        internal Component flock, landing;
        internal GameObject flockGo, landingGo;
        internal float scaredAt = -999f;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void OnDrawGizmos()
        {
            Gizmos.color = style == Style.Crow ? new Color(0.1f, 0.1f, 0.1f, 0.9f) : new Color(0.85f, 0.85f, 0.9f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawWireCube(transform.position + Vector3.up * flockHeight, Vector3.one * 1.5f);
        }
    }
}
