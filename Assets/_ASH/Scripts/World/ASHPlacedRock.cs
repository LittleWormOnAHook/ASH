using UnityEngine;

namespace ASH.World
{
    /// <summary>
    /// Marker on every rock, outcrop or cliff placed by the Rocks and Cliffs tool.
    /// Stores the seed and style so a copy (Ctrl+D, paste, or Shift-drag in the tool) can rebuild a new variation.
    /// Existing hand-placed scene rocks without this component are never touched.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, SelectionBase]
    public sealed class ASHPlacedRock : MonoBehaviour
    {
        public const string RootName = "ASH_PlacedRocks";

        public ASHRockKitSettings.Style style = ASHRockKitSettings.Style.Boulder;
        public int seed;
        [Tooltip("Keep the same shape when this object is duplicated.")]
        public bool lockSeed;
        [Tooltip("How far this rock is sunk into the ground (metres). Kept when you move it.")]
        public float sinkMetres;
        public float yaw;
        [Tooltip("Extra uniform scale on top of the piece's own random scale.")]
        public float scaleMul = 1f;
        [Tooltip("World-space end of a cliff line (style Cliff only).")]
        public Vector3 cliffEnd;
        public bool hasCliffEnd;
        [Tooltip("True when the placer could not clear an overlap.")]
        public bool overlapWarning;

        /// <summary>Find or create the scene root that owns tool-placed rocks. Does not save the scene.</summary>
        public static Transform FindRoot()
        {
            var go = GameObject.Find(RootName);
            return go ? go.transform : null;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!overlapWarning) return;
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.35f);
            var b = RenderBounds();
            Gizmos.DrawWireCube(b.center, b.size);
        }

        public Bounds RenderBounds()
        {
            var rs = GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(transform.position, Vector3.one);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }
#endif
    }
}
