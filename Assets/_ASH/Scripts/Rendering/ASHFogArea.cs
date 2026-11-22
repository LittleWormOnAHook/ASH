using System.Collections.Generic;
using UnityEngine;

namespace ASH.Rendering
{
    /// <summary>Local volumetric fog area (GameObject > ASH > Volumetric Fog Area). The shape is this object's Box or Sphere
    /// trigger collider. Local Density adds fog inside the shape (soft edge, own colour and noise); Blend Global smoothly
    /// blends the global "ASH Volumetric Fog" settings toward this area's settings while the camera (or player) is inside or
    /// within Blend Distance (fade in/out time, priority). Several areas can be active at once (up to 8 local areas render,
    /// nearest to the camera first).</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ASHFogArea : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHFogArea()
        {
            Active.Clear();
            _player = default;
            _playerLookup = default;
        }

        public enum AreaMode { LocalDensity, BlendGlobal }
        public enum Target { Camera, Player }
        public static readonly List<ASHFogArea> Active = new List<ASHFogArea>();

        public AreaMode mode = AreaMode.LocalDensity;
        [Tooltip("Higher priority blends last (wins) and renders first when more than 8 local areas are near.")] public int priority;

        [Header("Local density")]
        [Tooltip("Extinction per metre added inside the shape.")] [Range(0f, 2f)] public float localDensity = 0.03f;
        public Color color = new Color(0.75f, 0.72f, 0.68f);
        [Tooltip("Soft edge width inside the shape, metres.")] [Range(0.01f, 100f)] public float edgeFalloff = 2f;
        [Tooltip("How much the global haze noise breaks this area up (0 = smooth).")] [Range(0f, 1f)] public float noiseAmount = 0.6f;
        [Tooltip("Density falls off with height inside the area (1/m from the shape's bottom; 0 = uniform).")] [Range(0f, 2f)] public float heightFalloff = 0f;

        [Header("Blend global (camera/player inside)")]
        public Target blendTarget = Target.Camera;
        [Tooltip("Blend starts this far outside the shape, metres.")] [Range(0f, 200f)] public float blendDistance = 5f;
        [Range(0f, 30f)] public float fadeInTime = 1.5f;
        [Range(0f, 30f)] public float fadeOutTime = 2f;
        [Range(0f, 1f)] public float density = 0.02f;
        [Range(0f, 2f)] public float heightFalloffGlobal = 0.035f;
        public Color albedo = new Color(0.7f, 0.68f, 0.66f);
        [Range(-0.95f, 0.95f)] public float anisotropy = 0.35f;
        [Range(0f, 32f)] public float lightIntensity = 0.6f;
        [Range(0f, 16f)] public float ambientIntensity = 1f;
        [Range(0f, 1f)] public float noiseStrength = 0.5f;
        [Range(5f, 5000f)] public float maxDistance = 150f;

        [Header("Gizmo")]
        public Color gizmoColor = new Color(0.8f, 0.8f, 0.9f, 0.6f);

        /// <summary>Current blend weight (Blend Global), 0..1, smoothed with the fade times.</summary>
        public float Weight { get; private set; }
        Collider _col;

        public Collider Shape { get { if (!_col) _col = GetComponent<Collider>(); return _col; } }

        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); EnsureTrigger(); }
        void OnDisable() { Active.Remove(this); Weight = 0f; }
        void OnValidate() => EnsureTrigger();
        void EnsureTrigger() { var c = Shape; if (c && !c.isTrigger) c.isTrigger = true; }

        /// <summary>World -> local metres (unscaled), half extents (box) or radius (sphere, x). False if no Box/Sphere collider.</summary>
        public bool GetShape(out Matrix4x4 worldToLocal, out Vector3 half, out bool sphere)
        {
            worldToLocal = Matrix4x4.identity; half = Vector3.zero; sphere = false;
            var t = transform; var ls = t.lossyScale; ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            switch (Shape)
            {
                case BoxCollider b:
                    worldToLocal = Matrix4x4.TRS(t.TransformPoint(b.center), t.rotation, Vector3.one).inverse;
                    half = Vector3.Scale(b.size, ls) * 0.5f; return true;
                case SphereCollider s:
                    worldToLocal = Matrix4x4.TRS(t.TransformPoint(s.center), t.rotation, Vector3.one).inverse;
                    half = Vector3.one * s.radius * Mathf.Max(ls.x, ls.y, ls.z); sphere = true; return true;
            }
            return false;
        }

        /// <summary>Signed distance (m) from p to the shape surface (negative inside).</summary>
        public float Distance(Vector3 p)
        {
            if (!GetShape(out var m, out var h, out bool sph)) return float.MaxValue;
            var q = m.MultiplyPoint3x4(p);
            if (sph) return q.magnitude - h.x;
            var d = new Vector3(Mathf.Abs(q.x) - h.x, Mathf.Abs(q.y) - h.y, Mathf.Abs(q.z) - h.z);
            return Vector3.Max(d, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(d.x, Mathf.Max(d.y, d.z)), 0f);
        }

        static Transform _player; static float _playerLookup;
        void Update()
        {
            if (mode != AreaMode.BlendGlobal) { Weight = 0f; return; }
            Vector3 p;
            if (blendTarget == Target.Player)
            {
                if (!_player && Time.unscaledTime > _playerLookup) { _playerLookup = Time.unscaledTime + 1f; var g = GameObject.FindWithTag("Player"); _player = g ? g.transform : null; }
                if (!_player) { Weight = Mathf.MoveTowards(Weight, 0f, Time.deltaTime / Mathf.Max(fadeOutTime, 1e-3f)); return; }
                p = _player.position + Vector3.up;
            }
            else { var c = Camera.main; if (!c) return; p = c.transform.position; }
            float d = Distance(p);
            float target = d <= 0f ? 1f : blendDistance > 0f ? Mathf.Clamp01(1f - d / blendDistance) : 0f;
            target = target * target * (3f - 2f * target);
            if (!Application.isPlaying) { Weight = target; return; }
            float rate = 1f / Mathf.Max(target > Weight ? fadeInTime : fadeOutTime, 1e-3f);
            Weight = Mathf.MoveTowards(Weight, target, rate * Time.deltaTime);
        }

        void OnDrawGizmos() { DrawGizmo(false); }
        void OnDrawGizmosSelected() { DrawGizmo(true); }
        void DrawGizmo(bool selected)
        {
            if (!GetShape(out var m, out var h, out bool sph)) return;
            var old = Gizmos.matrix; Gizmos.matrix = m.inverse;
            var c = gizmoColor; Gizmos.color = new Color(c.r, c.g, c.b, selected ? 0.18f : 0.06f);
            if (sph) Gizmos.DrawSphere(Vector3.zero, h.x); else Gizmos.DrawCube(Vector3.zero, h * 2f);
            Gizmos.color = c;
            if (sph) Gizmos.DrawWireSphere(Vector3.zero, h.x); else Gizmos.DrawWireCube(Vector3.zero, h * 2f);
            Gizmos.color = new Color(c.r, c.g, c.b, c.a * 0.4f);
            if (mode == AreaMode.LocalDensity && edgeFalloff > 0f)
            {   // inner edge where the falloff reaches full density
                if (sph) Gizmos.DrawWireSphere(Vector3.zero, Mathf.Max(0f, h.x - edgeFalloff));
                else Gizmos.DrawWireCube(Vector3.zero, Vector3.Max(Vector3.zero, h * 2f - Vector3.one * 2f * edgeFalloff));
            }
            else if (mode == AreaMode.BlendGlobal && blendDistance > 0f)
            {   // outer blend distance
                if (sph) Gizmos.DrawWireSphere(Vector3.zero, h.x + blendDistance); else Gizmos.DrawWireCube(Vector3.zero, h * 2f + Vector3.one * 2f * blendDistance);
            }
            Gizmos.matrix = old;
        }
    }
}
