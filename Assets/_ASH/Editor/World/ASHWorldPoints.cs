using UnityEngine;

namespace ASH.EditorTools.World
{
    /// <summary>Slice anchor points on the 4 km master terrain (world XZ metres, terrain origin at 0,0).</summary>
    public static class ASHWorldPoints
    {
        public static readonly Vector2 Spawn = new Vector2(1905f, 1655f);
        public static readonly Vector2 Overlook = new Vector2(1312f, 1434f);
        public static readonly Vector2 Campsite = new Vector2(1316f, 1450f);
        /// <summary>Campsite chosen by the scene setup (relative to the cabin start); painter uses it for the ash scar.</summary>
        public static Vector2? CampsiteOverride;
        public static Vector2 CurrentCampsite()
        {
            if (CampsiteOverride.HasValue) return CampsiteOverride.Value;
            var a = GameObject.Find("ASH_Objective_OldCampsite");
            return a ? new Vector2(a.transform.position.x, a.transform.position.z) : Campsite;
        }
        public static readonly Vector2 StockTank = new Vector2(1870f, 1612f);

        public static Vector3 Ground(Terrain t, Vector2 xz)
        {
            var p = new Vector3(xz.x, 0f, xz.y);
            p.y = t.SampleHeight(p) + t.transform.position.y;
            return p;
        }
    }
}
