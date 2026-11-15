using ASH.Inventory;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>A torn page of Grandpa's Field Manual lying in the world (place with ASH/Systems/Field Manual/Place Torn Page).
    /// E picks it up: the page counts towards "Page N of 12" and can unlock recipes.</summary>
    public sealed class ASHManualPagePickup : MonoBehaviour, IASHUsable
    {
        [Min(1)] public int pageNumber = 1;
        public Transform UseTransform => transform;
        public float GetUsePriority(in ASHUseContext c) => ASHFieldManual.Instance ? 65f : -1f;
        public float GetHoldDuration(in ASHUseContext c) => 0f;
        public string GetPrompt(in ASHUseContext c) => "Pick up torn page";
        public bool TryUse(in ASHUseContext c)
        {
            var m = ASHFieldManual.Instance; if (!m) return false;
            m.FindPage(pageNumber);
            Destroy(gameObject);
            return true;
        }
    }
}
