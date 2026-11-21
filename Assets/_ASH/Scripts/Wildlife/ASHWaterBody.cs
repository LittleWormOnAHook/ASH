using System.Collections.Generic;
using UnityEngine;

namespace ASH.Wildlife
{
    /// <summary>
    /// Runtime view of a lake / pond found by name in the scene (no scene edits): its water surface height and bounds, and
    /// "is this point open water, how deep" queries. Used by the lake ducks now and meant for fishing later.
    /// The surface is the top of the largest flat renderer under the named object; a point counts as water where the terrain
    /// is below that surface.
    /// </summary>
    public sealed class ASHWaterBody
    {
        public string name;
        public Transform root;
        public Bounds bounds;
        public float surfaceY;

        static readonly List<ASHWaterBody> _cache = new List<ASHWaterBody>();
        static float _cacheAt = -999f;
        static string _cacheKey;

        /// <summary>Water bodies whose GameObject name contains any of the given names (cached; rescanned every 30 s).</summary>
        public static List<ASHWaterBody> Find(string[] names, float surfaceOffset = 0f)
        {
            string key = names == null ? "" : string.Join("|", names) + surfaceOffset;
            if (Time.unscaledTime - _cacheAt < 30f && key == _cacheKey)
            {
                _cache.RemoveAll(w => w == null || !w.root);
                return _cache;
            }
            _cacheAt = Time.unscaledTime; _cacheKey = key;
            _cache.Clear();
            if (names == null || names.Length == 0) return _cache;
            foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            {
                if (!tr) continue;
                bool hit = false;
                foreach (var n in names) if (!string.IsNullOrEmpty(n) && tr.name.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                if (!hit) continue;
                if (tr.parent && _cache.Exists(w => tr.IsChildOf(w.root))) continue;
                var w = FromObject(tr, surfaceOffset);
                if (w != null) _cache.Add(w);
            }
            return _cache;
        }

        public static ASHWaterBody FromObject(Transform tr, float surfaceOffset = 0f)
        {
            Renderer best = null; float bestScore = 0f;
            foreach (var r in tr.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                var b = r.bounds;
                float area = b.size.x * b.size.z;
                float flat = area / Mathf.Max(0.5f, b.size.y * b.size.y);
                float score = area * Mathf.Clamp01(flat / 50f);
                if (score > bestScore) { bestScore = score; best = r; }
            }
            if (!best) return null;
            var bb = best.bounds;
            return new ASHWaterBody { name = tr.name, root = tr, bounds = bb, surfaceY = bb.max.y + surfaceOffset };
        }

        /// <summary>Water depth at p (surface minus terrain / collider below); 0 or less = dry land.</summary>
        public float Depth(Vector3 p, int mask = ~0)
        {
            if (p.x < bounds.min.x || p.x > bounds.max.x || p.z < bounds.min.z || p.z > bounds.max.z) return 0f;
            float floor = float.NegativeInfinity;
            var t = Terrain.activeTerrain;
            if (t) floor = t.SampleHeight(p) + t.GetPosition().y;
            if (Physics.Raycast(new Vector3(p.x, surfaceY + 0.5f, p.z), Vector3.down, out var h, 200f, mask, QueryTriggerInteraction.Ignore))
                floor = Mathf.Max(floor, h.point.y);
            if (float.IsNegativeInfinity(floor)) return 1f;
            return surfaceY - floor;
        }

        /// <summary>Open water at p: at least minDepth deep here and `shoreClear` metres around (keeps the shore free).</summary>
        public bool IsOpenWater(Vector3 p, float minDepth, float shoreClear, int mask = ~0)
        {
            if (Depth(p, mask) < minDepth) return false;
            if (shoreClear <= 0f) return true;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                if (Depth(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * shoreClear, mask) < minDepth * 0.5f) return false;
            }
            return true;
        }

        /// <summary>Random open-water point on the surface (near `near` when given radius > 0).</summary>
        public bool RandomWaterPoint(out Vector3 p, float minDepth, float shoreClear, int tries = 24, Vector3? near = null, float radius = 0f, int mask = ~0)
        {
            for (int i = 0; i < tries; i++)
            {
                Vector3 c;
                if (near.HasValue && radius > 0f) { var o = Random.insideUnitCircle * radius; c = near.Value + new Vector3(o.x, 0f, o.y); }
                else c = new Vector3(Random.Range(bounds.min.x, bounds.max.x), 0f, Random.Range(bounds.min.z, bounds.max.z));
                c.y = surfaceY;
                if (IsOpenWater(c, minDepth, shoreClear, mask)) { p = c; return true; }
            }
            p = bounds.center; p.y = surfaceY;
            return false;
        }

        public Vector3 Center => new Vector3(bounds.center.x, surfaceY, bounds.center.z);
        public float Radius => Mathf.Max(bounds.extents.x, bounds.extents.z);
    }
}
