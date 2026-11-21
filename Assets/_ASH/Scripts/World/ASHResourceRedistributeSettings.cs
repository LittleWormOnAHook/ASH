using UnityEngine;

namespace ASH.World
{
    /// <summary>Play-editable C9 redistribution rules. Resources/ASH_ResourceRedistributeSettings.</summary>
    [CreateAssetMenu(menuName = "ASH/World/Resource Redistribute Settings", fileName = "ASH_ResourceRedistributeSettings")]
    public sealed class ASHResourceRedistributeSettings : ScriptableObject
    {
        [Tooltip("Seed 0 picks a new seed each redistribute.")]
        public int seed = 0;
        [Range(10f, 300f)] public float campExclusionMeters = 80f;
        [Range(1f, 40f)] public float minSpacing = 4f;
        [Range(0.5f, 20f)] public float roadEdgeClear = 2.5f;
        [Tooltip("Fraction of auto-scattered (not keep) nodes to re-place. 1 = all of them.")]
        [Range(0.1f, 1f)] public float relocateFraction = 1f;

        static ASHResourceRedistributeSettings _i;
        public static ASHResourceRedistributeSettings I =>
            _i ? _i : (_i = Resources.Load<ASHResourceRedistributeSettings>("ASH_ResourceRedistributeSettings"));
    }
}
