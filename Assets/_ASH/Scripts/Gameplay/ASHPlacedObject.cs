using System.Collections.Generic;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Marks an object the player placed with ASHPlacer (added at runtime, never in scenes or prefabs), so
    /// ASHPlacedSave can respawn it from its item's placeablePrefab on load.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHPlacedObject : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHPlacedObject()
        {
            _all.Clear();
        }

        public string itemId;
        static readonly List<ASHPlacedObject> _all = new List<ASHPlacedObject>();
        public static IReadOnlyList<ASHPlacedObject> All => _all;
        void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        void OnDisable() => _all.Remove(this);
    }
}
