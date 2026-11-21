using ASH.Interaction;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>LEGACY (t443): kept only so a scene saved before the switch still loads. Grandpa's pages now use the standard
    /// torn-page pickup (ASHManualPagePickup with collectionId/pageId). This converts itself on load; no logic of its own.</summary>
    [AddComponentMenu("")]
    public sealed class ASHJournalPagePickup : MonoBehaviour
    {
        public string collectionId = "grandpa";
        public string pageId = "gj_01";
        void Awake()
        {
            var p = GetComponent<ASHManualPagePickup>(); if (!p) p = gameObject.AddComponent<ASHManualPagePickup>();
            p.collectionId = collectionId; p.pageId = pageId; Destroy(this);
        }
    }
}