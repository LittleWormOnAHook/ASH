using UnityEngine;

namespace ASH.World
{
    /// <summary>Stable id for a gatherable resource so redistribution can disable and restore it without deleting it.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHResourceId : MonoBehaviour
    {
        public string id;
        [Tooltip("True for hand-placed resources. Redistribute leaves these enabled and does not clone them.")]
        public bool keep;
        [Tooltip("Set when this object was disabled by a redistribute pass.")]
        public bool disabledByRedistribute;
    }
}
