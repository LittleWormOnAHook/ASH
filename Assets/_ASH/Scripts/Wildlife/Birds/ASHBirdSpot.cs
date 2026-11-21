using System.Collections.Generic;
using UnityEngine;

namespace ASH.Wildlife.Birds
{
    /// <summary>
    /// Drop-in spot for a resident bird group (e.g. ASH_Bird_AggressiveCrowSpot). The bird manager spawns the group here when the
    /// player comes within range, keeps it roosting around the spot, and respawns it a while after it is wiped out.
    /// aggressiveFraction 1 = every bird is an attacker (only types with canAttack, i.e. crows / ravens, ever attack).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHBirdSpot : MonoBehaviour
    {
        public static readonly List<ASHBirdSpot> All = new List<ASHBirdSpot>();

        [Tooltip("Bird type id from ASH_BirdSettings > types (crow, raven, ...).")]
        public string typeId = "crow";
        [Range(1, 12)] public int countMin = 2;
        [Range(1, 12)] public int countMax = 4;
        [Tooltip("Share of the group that attacks when shot at (only crows / ravens can).")]
        [Range(0f, 1f)] public float aggressiveFraction = 1f;
        [Tooltip("Perch search radius around the spot.")]
        [Range(1f, 40f)] public float radius = 8f;
        [Tooltip("The group appears when the player is within this distance.")]
        [Range(20f, 400f)] public float activateDistance = 150f;
        [Tooltip("Seconds before a wiped-out group comes back.")]
        [Range(0f, 3600f)] public float respawnSeconds = 300f;

        internal ASHBirdManager.Flock flock;
        internal float respawnAt;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.color = aggressiveFraction > 0f ? new Color(0.9f, 0.2f, 0.15f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
            Gizmos.DrawSphere(transform.position + Vector3.up * 0.5f, 0.35f);
        }
    }
}
