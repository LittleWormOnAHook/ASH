using ASH.Inventory;
using ASH.Prologue;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>A torn page lying in the world (place with ASH/Systems/Field Manual/Place Torn Page). E picks it up.
    /// Survival Guide page (collection empty): counts towards "Page N of 12" and can unlock recipes.
    /// t443: Grandpa's Journal page (collectionId + pageId set): same pickup, goes to that collection (tip / clue / rare recipe).</summary>
    public sealed class ASHManualPagePickup : MonoBehaviour, IASHUsable
    {
        [Min(1)] public int pageNumber = 1;
        [Tooltip("Empty = Survival Guide torn page (pageNumber). 'grandpa' = Grandpa's Journal page (pageId).")] public string collectionId;
        [Tooltip("Grandpa's Journal page id (e.g. gj_03).")] public string pageId;
        bool IsStory => !string.IsNullOrEmpty(pageId);
        public Transform UseTransform => transform;
        public float GetUsePriority(in ASHUseContext c) => IsStory ? (ASHGrandpaJournal.Has(collectionId, pageId) ? -1f : 65f) : ASHFieldManual.Instance ? 65f : -1f;
        public float GetHoldDuration(in ASHUseContext c) => 0f;
        public string GetPrompt(in ASHUseContext c) => IsStory ? "Pick up Grandpa's page" : "Pick up torn page";
        public bool TryUse(in ASHUseContext c)
        {
            if (IsStory) { ASHGrandpaJournal.Find(collectionId, pageId); gameObject.SetActive(false); return true; }
            var m = ASHFieldManual.Instance; if (!m) return false;
            m.FindPage(pageNumber);
            Destroy(gameObject);
            return true;
        }
        void Update() { if (IsStory && ASHGrandpaJournal.Has(collectionId, pageId)) gameObject.SetActive(false); } // gone after a load where it was found
    }
}
