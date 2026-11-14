using System.Collections.Generic;
using ASH.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.World.Accumulation
{
    /// <summary>
    /// Visible ash/snow DEPTH near the camera (Play Mode): a dense grid that follows the follow target and is displaced in the
    /// vertex shader to terrain height + layer depth (ASH_DepthMetres: drift mask, paths, wind, deformation). Beyond its edge
    /// the terrain shader's coverage takes over. Visual only: no collider, characters stay on the terrain (feet sink in).
    /// Structures with ASHAccumulation and pickups/interactables are kept clear (oriented box blockers).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHAccumulationSurface : MonoBehaviour
    {
        public enum Preset { Low, Default, High, Extreme }
        public Shader shader;
        [Tooltip("Side length of the displaced area, metres.")] [Range(8f, 160f)] public float size = 48f;
        [Tooltip("Grid spacing, metres (smaller = sharper footprints, more vertices).")] [Range(0.08f, 1f)] public float gridStep = 0.2f;
        [Tooltip("Depth fades to zero over this many metres at the edge.")] [Range(0.5f, 40f)] public float edgeFade = 8f;
        [Tooltip("0 coarse, 1 vertex, 2 vertex + per-pixel footprint normals.")] [Range(0, 2)] public int normalQuality = 1;
        [Tooltip("Defaults to Camera.main.")] public Transform follow;
        [Header("Blockers (keep structures / pickups clear)")]
        [Range(0, 16)] public int maxBlockers = 16;
        [Range(5f, 150f)] public float blockerRadius = 40f;
        [Range(0.1f, 5f)] public float blockerRefresh = 1f;
        [Tooltip("Only renderers whose bottom is within this height of the terrain block the surface.")] [Range(0.1f, 5f)] public float blockerGroundTolerance = 1f;

        GameObject _go; Mesh _mesh; Material _mat; MeshRenderer _mr; float _builtSize, _builtStep; float _nextBlock;
        readonly Vector4[] _blk = new Vector4[16], _rot = new Vector4[16];
        static readonly int ID_HM = Shader.PropertyToID("_ASH_Heightmap"), ID_HP = Shader.PropertyToID("_ASH_HeightParams"), ID_SR = Shader.PropertyToID("_ASH_SurfaceRect"),
            ID_B = Shader.PropertyToID("_ASH_Blockers"), ID_BR = Shader.PropertyToID("_ASH_BlockerRot"), ID_BC = Shader.PropertyToID("_ASH_BlockerCount"), ID_GS = Shader.PropertyToID("_ASH_GridStep"), ID_SQ = Shader.PropertyToID("_ASH_SurfQuality");

        public void ApplyPreset(Preset p)
        {
            switch (p)
            {
                case Preset.Low: size = 32f; gridStep = 0.33f; edgeFade = 6f; break;
                case Preset.Default: size = 48f; gridStep = 0.2f; edgeFade = 8f; break;
                case Preset.High: size = 72f; gridStep = 0.15f; edgeFade = 12f; break;
                case Preset.Extreme: size = 120f; gridStep = 0.12f; edgeFade = 20f; break;
            }
        }
        [ContextMenu("Preset/Low")] void PL() => ApplyPreset(Preset.Low);
        [ContextMenu("Preset/Default")] void PD() => ApplyPreset(Preset.Default);
        [ContextMenu("Preset/High")] void PH() => ApplyPreset(Preset.High);
        [ContextMenu("Preset/Extreme")] void PE() => ApplyPreset(Preset.Extreme);

        public int VertexCount => _mesh ? _mesh.vertexCount : 0;

        void Build()
        {
            if (_mesh) Destroy(_mesh);
            int n = Mathf.Clamp(Mathf.RoundToInt(size / gridStep), 8, 2047) + 1; float half = (n - 1) * gridStep * 0.5f;
            var v = new Vector3[n * n]; var idx = new int[(n - 1) * (n - 1) * 6];
            for (int z = 0, k = 0; z < n; z++) for (int x = 0; x < n; x++) v[k++] = new Vector3(x * gridStep - half, 0f, z * gridStep - half);
            for (int z = 0, k = 0; z < n - 1; z++) for (int x = 0; x < n - 1; x++)
            {
                int i = z * n + x; idx[k++] = i; idx[k++] = i + n; idx[k++] = i + 1; idx[k++] = i + 1; idx[k++] = i + n; idx[k++] = i + n + 1;
            }
            _mesh = new Mesh { name = "ASH_AccumSurface", indexFormat = v.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16, hideFlags = HideFlags.HideAndDontSave };
            _mesh.vertices = v; _mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            _mesh.bounds = new Bounds(new Vector3(0f, 2000f, 0f), new Vector3(size + 2f, 8000f, size + 2f));
            if (!_go)
            {
                _go = new GameObject("ASH_AccumSurface") { hideFlags = HideFlags.DontSave };
                _go.transform.SetParent(transform, false);
                _go.AddComponent<MeshFilter>(); _mr = _go.AddComponent<MeshRenderer>();
                _mr.shadowCastingMode = ShadowCastingMode.Off; _mr.receiveShadows = true; _mr.lightProbeUsage = LightProbeUsage.Off; _mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _mr.allowOcclusionWhenDynamic = false;
            }
            _go.GetComponent<MeshFilter>().sharedMesh = _mesh;
            if (!shader) shader = Shader.Find("Hidden/ASH/Accumulation Surface");
            if (!_mat && shader) _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mr.sharedMaterial = _mat;
            _builtSize = size; _builtStep = gridStep;
        }

        void OnDisable() { if (_go) Destroy(_go); if (_mesh) Destroy(_mesh); if (_mat) Destroy(_mat); _go = null; _mesh = null; _mat = null; }

        void LateUpdate()
        {
            var c = ASHAccumulationController.Instance;
            bool on = Application.isPlaying && c && c.visibleDepth && c.DeformationQualityOn && c.terrain;
            if (!on) { if (_go || _mesh) OnDisable(); return; } // free mesh/material while off (quality Off, depth off)
            if (!_mesh || _builtSize != size || _builtStep != gridStep) Build();
            if (!_mat) return;
            _go.SetActive(true);
            var f = follow ? follow : (Camera.main ? Camera.main.transform : null); if (!f) return;
            var p = f.position; float snap = gridStep * 2f;
            var centre = new Vector3(Mathf.Round(p.x / snap) * snap, 0f, Mathf.Round(p.z / snap) * snap);
            _go.transform.SetPositionAndRotation(centre, Quaternion.identity); _go.transform.localScale = Vector3.one;

            var t = c.terrain; var td = t.terrainData;
            Shader.SetGlobalTexture(ID_HM, td.heightmapTexture);
            Shader.SetGlobalVector(ID_HP, new Vector4(td.heightmapResolution, td.size.y / (32766f / 65535f), t.GetPosition().y, 0f));
            Shader.SetGlobalVector(ID_SR, new Vector4(centre.x, centre.z, size * 0.5f, edgeFade));
            Shader.SetGlobalFloat(ID_GS, gridStep);
            var fld = c.Field; float texel = fld && fld.Active ? fld.sizeMetres / fld.resolution : 0.125f;
            Shader.SetGlobalVector(ID_SQ, new Vector4(normalQuality == 0 ? 2f : 1f, normalQuality >= 2 ? 0.6f : 0f, texel, 0f));
            if (Time.unscaledTime >= _nextBlock) { _nextBlock = Time.unscaledTime + blockerRefresh; RefreshBlockers(centre, t); }
        }

        struct Cand { public float d; public Vector4 b, r; }
        readonly List<Cand> _cands = new List<Cand>();

        void RefreshBlockers(Vector3 centre, Terrain t)
        {
            _cands.Clear(); float r2 = blockerRadius * blockerRadius; var seen = new HashSet<Renderer>();
            void Add(Renderer r, float inflate)
            {
                if (!r || !r.enabled || !seen.Add(r) || r is SkinnedMeshRenderer) return;
                var wb = r.bounds; var flat = new Vector3(wb.center.x - centre.x, 0f, wb.center.z - centre.z);
                if (flat.sqrMagnitude > r2 + wb.extents.sqrMagnitude) return;
                float g = t.SampleHeight(wb.center) + t.GetPosition().y;
                if (wb.min.y > g + blockerGroundTolerance) return;
                var mf = r.GetComponent<MeshFilter>(); var tr = r.transform;
                Vector2 c2, h2; float yaw;
                if (mf && mf.sharedMesh)
                {
                    var lb = mf.sharedMesh.bounds; var wc = tr.TransformPoint(lb.center);
                    var ax = tr.TransformVector(new Vector3(lb.extents.x, 0, 0)); var az = tr.TransformVector(new Vector3(0, 0, lb.extents.z));
                    var axf = new Vector2(ax.x, ax.z); var azf = new Vector2(az.x, az.z);
                    c2 = new Vector2(wc.x, wc.z); h2 = new Vector2(axf.magnitude, azf.magnitude);
                    yaw = Mathf.Atan2(axf.y, axf.x);
                }
                else { c2 = new Vector2(wb.center.x, wb.center.z); h2 = new Vector2(wb.extents.x, wb.extents.z); yaw = 0f; }
                if (h2.x * h2.y < 0.0025f) return;
                h2 += Vector2.one * inflate;
                _cands.Add(new Cand { d = (c2 - new Vector2(centre.x, centre.z)).sqrMagnitude, b = new Vector4(c2.x, c2.y, h2.x, h2.y), r = new Vector4(Mathf.Cos(yaw), Mathf.Sin(yaw), 0, 0) });
            }
            foreach (var a in ASHAccumulation.All) if (a) foreach (var r in a.GetComponentsInChildren<Renderer>()) Add(r, 0.05f);
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                if (mb is IASHUsable || mb is ASHItemPickup) foreach (var r in mb.GetComponentsInChildren<Renderer>()) Add(r, 0.12f);
            _cands.Sort((x, y) => x.d.CompareTo(y.d));
            int n = Mathf.Min(maxBlockers, _cands.Count);
            for (int i = 0; i < 16; i++) { _blk[i] = i < n ? _cands[i].b : Vector4.zero; _rot[i] = i < n ? _cands[i].r : new Vector4(1, 0, 0, 0); }
            Shader.SetGlobalVectorArray(ID_B, _blk); Shader.SetGlobalVectorArray(ID_BR, _rot); Shader.SetGlobalFloat(ID_BC, n);
        }
    }
}
