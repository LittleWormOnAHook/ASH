using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASH.World.Accumulation
{
    /// <summary>
    /// Stamps footprints / trails / tracks into the deformation field. Drop on the player (feet auto-found on a Humanoid),
    /// animals, NPCs or vehicles (add wheel transforms to Points). A point stamps while it is inside the ash/snow layer,
    /// drawing a segment from its last stamped position so fast movers leave continuous trails.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHDeformer : MonoBehaviour
    {
        [Serializable] public sealed class Point
        {
            public Transform transform;
            [Range(0.02f, 2f)] public float radius = 0.13f;
            [Tooltip("Multiplier on the controller's Footprint Depth.")] [Range(0f, 1f)] public float strength = 1f;
            [Tooltip("Height of the contact (sole/tyre bottom) below the transform, metres.")] public float bottomOffset = 0.08f;
            [NonSerialized] public Vector3 last; [NonSerialized] public bool had;
        }

        public List<Point> points = new List<Point>();
        [Tooltip("If Points is empty and this has a Humanoid Animator, use the feet.")] public bool autoHumanoidFeet = true;
        [Range(0.02f, 1f)] public float footRadius = 0.13f;
        [Tooltip("Contact counts while the point is below the undisturbed surface + this tolerance.")] [Range(0f, 0.5f)] public float surfaceTolerance = 0.05f;
        [Header("Wading trench (legs ploughing deep material)")]
        public bool wadingTrench = true;
        [Range(0.05f, 1f)] public float trenchRadius = 0.3f;
        [Range(0f, 1f)] public float trenchStrength = 0.6f;
        [Tooltip("Depth (m) where the trench starts; full at +0.3 m.")] [Range(0f, 1f)] public float trenchStartDepth = 0.18f;

        public static readonly List<ASHDeformer> All = new List<ASHDeformer>();
        Vector3 _lastBody; bool _hadBody; Terrain _t;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Start()
        {
            _t = Terrain.activeTerrain;
            if (autoHumanoidFeet && points.Count == 0)
            {
                var a = GetComponentInChildren<Animator>();
                if (a && a.isHuman)
                {
                    var l = a.GetBoneTransform(HumanBodyBones.LeftFoot); var r = a.GetBoneTransform(HumanBodyBones.RightFoot);
                    float bl = Mathf.Max(0.02f, a.leftFeetBottomHeight), br = Mathf.Max(0.02f, a.rightFeetBottomHeight);
                    if (l) points.Add(new Point { transform = l, radius = footRadius, bottomOffset = bl });
                    if (r) points.Add(new Point { transform = r, radius = footRadius, bottomOffset = br });
                }
            }
        }

        float Ground(Vector3 p) => _t ? _t.SampleHeight(p) + _t.GetPosition().y : p.y;

        public void Collect(List<Vector4> seg, List<Vector4> par, ASHAccumulationController c)
        {
            float fd = c.footprintDepth;
            foreach (var pt in points)
            {
                if (pt == null || !pt.transform) continue;
                var pos = pt.transform.position; float sole = pos.y - pt.bottomOffset;
                float surf = Ground(pos) + c.GetBaseDepthMetres(pos);
                bool touch = sole <= surf + surfaceTolerance;
                if (touch)
                {
                    var a = pt.had ? pt.last : pos;
                    if ((a - pos).sqrMagnitude > 4f) a = pos; // teleport guard
                    seg.Add(new Vector4(a.x, a.z, pos.x, pos.z)); par.Add(new Vector4(pt.radius, fd * pt.strength, 0, 0));
                }
                pt.last = pos; pt.had = touch;
            }
            if (wadingTrench)
            {
                var p = transform.position; float d = c.GetBaseDepthMetres(p);
                float k = Mathf.Clamp01((d - trenchStartDepth) / 0.3f);
                if (k > 0f)
                {
                    var a = _hadBody && (p - _lastBody).sqrMagnitude < 4f ? _lastBody : p;
                    seg.Add(new Vector4(a.x, a.z, p.x, p.z)); par.Add(new Vector4(trenchRadius, fd * trenchStrength * k, 0, 0));
                }
                _lastBody = p; _hadBody = k > 0f;
            }
        }
    }
}
