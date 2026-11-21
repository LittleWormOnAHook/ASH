using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>Data-only placeholder for where a truck part goes once its area exists (M3: camp tire, shack fan belt). Spawns nothing in M1.</summary>
    public sealed class ASHPrologueItemSlot : MonoBehaviour
    {
        public string itemId = "tire";
        public string area = "Abandoned camp";
        [Tooltip("Off in M1: the area is not built yet, so nothing spawns here.")] public bool active = false;
        [TextArea(1, 3)] public string note;
        void OnDrawGizmos() { Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f); Gizmos.DrawWireSphere(transform.position, 0.6f); }
    }
}
