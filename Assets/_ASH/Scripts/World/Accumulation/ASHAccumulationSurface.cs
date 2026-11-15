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
    /// The layer clears around every nearby collider's real footprint (collider fade mask, see ASH_ColliderFadeMask.shader):
    /// box / sphere / capsule / mesh shapes drawn top-down at texel accuracy, plus Collider Fade Padding and Softness.
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
        [Tooltip("Normal Smoothing: width of the per-pixel normal filter in terrain heightmap texels (1 = one texel; higher = softer light on the layer).")]
        [Range(0.5f, 4f)] public float normalSmoothing = 1.5f;
        [Tooltip("Deform Normal Strength: how strongly footprints / trails / drifts tilt the lighting normal (0 = flat lighting over them).")]
        [Range(0f, 3f)] public float deformNormalStrength = 1f;
        [Tooltip("Defaults to Camera.main.")] public Transform follow;
        [Header("Thin layer (no stair-steps where the layer is only a few cm)")]
        [Tooltip("Dissolve the real-depth surface where the layer is thin (world-space noise, patchy), letting the terrain's soft coverage show. Off = legacy behaviour.")] public bool thinFade = true;
        [Tooltip("Depth (m) below which the real-depth surface is fully dissolved.")] [Range(0f, 0.5f)] public float thinFadeStart = 0.01f;
        [Tooltip("Depth (m) above which the real-depth surface is fully solid.")] [Range(0.001f, 1f)] public float thinFadeEnd = 0.04f;
        [Tooltip("Minimum height (m) of the visible surface above the terrain, so it never intersects the terrain mesh.")] [Range(0f, 0.2f)] public float surfaceLift = 0.02f;

        [Header("Collider fade (ash clears around every collider's real footprint)")]
        public bool colliderFade = true;
        [Tooltip("Hidden/ASH/Collider Fade Mask (found automatically in the editor; assign for builds).")] public Shader colliderMaskShader;
        [Tooltip("Collider Fade Padding: extra clear distance outside each collider's footprint, metres.")] [Range(0f, 2f)] public float colliderFadePadding = 0.02f;
        [Tooltip("Collider Fade Softness: width of the soft edge after the padding, metres (0 = hard cut).")] [Range(0f, 2f)] public float colliderFadeSoftness = 0.03f;
        [Tooltip("Mask texel size, metres (smaller = tighter edge, more memory). The mask is texel-aligned to the world grid.")] [Range(0.005f, 0.25f)] public float colliderMaskTexel = 0.02f;
        [Tooltip("Collider geometry higher than this above the ground is ignored (so overhangs/roofs don't widen the footprint).")] [Range(0.05f, 10f)] public float colliderFootprintHeight = 1.5f;
        [Tooltip("Only colliders whose bottom is within this height of the terrain clear the surface.")] [Range(0.1f, 5f)] public float blockerGroundTolerance = 1f;
        [Tooltip("Seconds between mask rebuilds (also rebuilt when the area moves).")] [Range(0.1f, 5f)] public float blockerRefresh = 1f;
        [Tooltip("Include trigger colliders of pickups (other triggers, e.g. interaction zones, never clear the ash).")] public bool pickupTriggersClear = true;

        GameObject _go; Mesh _mesh; Material _mat; MeshRenderer _mr; float _builtSize, _builtStep; float _nextBlock;
        RenderTexture _maskRaw, _maskRow, _mask; Material _maskMat; CommandBuffer _cb; Vector2 _maskMin; float _maskSize; int _maskRes;
        Mesh _cube, _sphere, _capsule;
        static readonly int ID_HM = Shader.PropertyToID("_ASH_Heightmap"), ID_HP = Shader.PropertyToID("_ASH_HeightParams"), ID_SR = Shader.PropertyToID("_ASH_SurfaceRect"),
            ID_CM = Shader.PropertyToID("_ASH_ColliderMask"), ID_CMR = Shader.PropertyToID("_ASH_ColliderMaskRect"), ID_GS = Shader.PropertyToID("_ASH_GridStep"), ID_SQ = Shader.PropertyToID("_ASH_SurfQuality"), ID_SN = Shader.PropertyToID("_ASH_SurfNormal"), ID_Thin = Shader.PropertyToID("_ASH_Thin");

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

        void OnDisable()
        {
            if (_go) Destroy(_go); if (_mesh) Destroy(_mesh); if (_mat) Destroy(_mat); _go = null; _mesh = null; _mat = null;
            ReleaseMask(); if (_maskMat) Destroy(_maskMat); _maskMat = null; _cb?.Release(); _cb = null;
            Shader.SetGlobalVector(ID_CMR, Vector4.zero);
        }
        void ReleaseMask() { foreach (var r in new[] { _maskRaw, _maskRow, _mask }) if (r) { r.Release(); Destroy(r); } _maskRaw = _maskRow = _mask = null; _maskRes = 0; }

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
            Shader.SetGlobalVector(ID_Thin, new Vector4(thinFadeStart, Mathf.Max(thinFadeEnd, thinFadeStart + 0.001f), surfaceLift, thinFade ? 1f : 0f));
            var fld = c.Field; float texel = fld && fld.Active ? fld.sizeMetres / fld.resolution : 0.125f;
            Shader.SetGlobalVector(ID_SN, new Vector4(normalSmoothing, deformNormalStrength, texel, 0f));
            Shader.SetGlobalVector(ID_SQ, new Vector4(normalQuality == 0 ? 2f : 1f, normalQuality >= 2 ? 0.6f : 0f, texel, 0f));
            if (Time.unscaledTime >= _nextBlock || MaskNeedsMove(centre)) { _nextBlock = Time.unscaledTime + blockerRefresh; RefreshBlockers(centre, t); }
        }

        // ---- collider fade mask -------------------------------------------------------------------------------------------
        readonly List<Collider> _cols = new List<Collider>(); readonly Collider[] _hits = new Collider[2048];
        public int ColliderFadeCount { get; private set; }
        public RenderTexture ColliderMask => _mask;
        public Vector4 ColliderMaskRect => new Vector4(_maskMin.x, _maskMin.y, _maskSize, _maskRes);

        Mesh Prim(PrimitiveType t) { var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); return m; }

        bool Clears(Collider col, Transform player, Terrain t)
        {
            if (!col || !col.enabled || col is TerrainCollider || col is WheelCollider || col is CharacterController) return false;
            if (player && col.transform.IsChildOf(player)) return false;
            if (col.isTrigger && !(pickupTriggersClear && col.GetComponentInParent<ASHItemPickup>())) return false;
            var an = col.GetComponentInParent<Animator>(); if (an && an.isHuman) return false;            // characters / animals
            var ch = col.GetComponentInParent<ASHChoppable>(); if (ch && !ch.BlocksSurface) return false;  // standing trees stay as before
            var b = col.bounds; if (b.size.x > _maskSize || b.size.z > _maskSize) return false;           // world volumes
            float g = t.SampleHeight(b.center) + t.GetPosition().y;
            return b.min.y <= g + blockerGroundTolerance;
        }

        void RefreshBlockers(Vector3 centre, Terrain t)
        {
            if (!colliderFade) { Shader.SetGlobalVector(ID_CMR, Vector4.zero); ColliderFadeCount = 0; return; }
            var sh = colliderMaskShader ? colliderMaskShader : (colliderMaskShader = Shader.Find("Hidden/ASH/Collider Fade Mask")); if (!sh) return;
            if (!_maskMat) _maskMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            if (!_cube) { _cube = Prim(PrimitiveType.Cube); _sphere = Prim(PrimitiveType.Sphere); _capsule = Prim(PrimitiveType.Capsule); }

            // texel-aligned square covering the surface plus a margin (rebuilt before the area walks off it)
            float tx = colliderMaskTexel; int res = Mathf.Clamp(Mathf.CeilToInt((size + 8f) / tx), 64, 4096); tx = Mathf.Max(tx, (size + 8f) / res);
            if (_maskRes != res) { ReleaseMask(); _maskRes = res;
                _maskRaw = NewRT(res, RenderTextureFormat.R8, FilterMode.Point, "ASH_ColliderFootprint");
                _maskRow = NewRT(res, RenderTextureFormat.RHalf, FilterMode.Point, "ASH_ColliderFadeRows");
                _mask = NewRT(res, RenderTextureFormat.RHalf, FilterMode.Bilinear, "ASH_ColliderFade"); }
            _maskSize = res * tx;
            _maskMin = new Vector2(Mathf.Floor((centre.x - _maskSize * 0.5f) / tx) * tx, Mathf.Floor((centre.z - _maskSize * 0.5f) / tx) * tx);
            _maskCentre = centre;

            Transform player = follow ? follow.root : null;
            var pc = GameObject.FindWithTag("Player"); Transform playerTag = pc ? pc.transform.root : null;
            float gy = t.GetPosition().y + t.terrainData.size.y * 0.5f;
            int n = Physics.OverlapBoxNonAlloc(new Vector3(_maskMin.x + _maskSize * 0.5f, gy, _maskMin.y + _maskSize * 0.5f),
                new Vector3(_maskSize * 0.5f, t.terrainData.size.y * 0.5f + 50f, _maskSize * 0.5f), _hits, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
            _cols.Clear();
            for (int i = 0; i < n; i++) if (Clears(_hits[i], player, t) && (!playerTag || !_hits[i].transform.IsChildOf(playerTag))) _cols.Add(_hits[i]);
            ColliderFadeCount = _cols.Count;

            if (_cb == null) _cb = new CommandBuffer { name = "ASH Collider Fade Mask" };
            _cb.Clear();
            _cb.SetGlobalVector("_ASH_CFM_Rect", new Vector4(_maskMin.x, _maskMin.y, 1f / _maskSize, colliderFootprintHeight));
            float reach = colliderFadePadding + colliderFadeSoftness + tx; int st = Mathf.Max(1, Mathf.CeilToInt(reach / tx / 64f)); int taps = Mathf.Min(64, Mathf.CeilToInt(reach / tx / st));
            _cb.SetGlobalVector("_ASH_CFM_Params", new Vector4(tx, colliderFadePadding, colliderFadeSoftness, st));
            _cb.SetGlobalFloat("_ASH_CFM_Taps", taps); _cb.SetGlobalFloat("_ASH_CFM_Res", res);
            _cb.SetRenderTarget(_maskRaw); _cb.ClearRenderTarget(false, true, Color.clear);
            foreach (var col in _cols) DrawCollider(col);
            _cb.SetGlobalTexture("_ASH_CFM_Src", _maskRaw); _cb.SetRenderTarget(_maskRow); _cb.DrawProcedural(Matrix4x4.identity, _maskMat, 1, MeshTopology.Triangles, 3);
            _cb.SetGlobalTexture("_ASH_CFM_Src", _maskRow); _cb.SetRenderTarget(_mask); _cb.DrawProcedural(Matrix4x4.identity, _maskMat, 2, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_cb);
            Shader.SetGlobalTexture(ID_CM, _mask);
            Shader.SetGlobalVector(ID_CMR, new Vector4(_maskMin.x, _maskMin.y, 1f / _maskSize, 1f));
        }
        Vector3 _maskCentre;
        public bool MaskNeedsMove(Vector3 centre) => _maskRes > 0 && (Mathf.Abs(centre.x - _maskCentre.x) > 3f || Mathf.Abs(centre.z - _maskCentre.z) > 3f);

        static RenderTexture NewRT(int res, RenderTextureFormat f, FilterMode fm, string name)
        {
            var r = new RenderTexture(res, res, 0, f, RenderTextureReadWrite.Linear) { name = name, filterMode = fm, wrapMode = TextureWrapMode.Clamp, useMipMap = false, hideFlags = HideFlags.HideAndDontSave };
            r.Create(); return r;
        }

        void DrawCollider(Collider col)
        {
            var tr = col.transform; Matrix4x4 m;
            switch (col)
            {
                case BoxCollider b:
                    m = tr.localToWorldMatrix * Matrix4x4.TRS(b.center, Quaternion.identity, b.size); _cb.DrawMesh(_cube, m, _maskMat, 0, 0); return;
                case SphereCollider s:
                {
                    var ls = tr.lossyScale; float sc = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                    m = Matrix4x4.TRS(tr.TransformPoint(s.center), tr.rotation, Vector3.one * (2f * s.radius * sc)); _cb.DrawMesh(_sphere, m, _maskMat, 0, 0); return;
                }
                case CapsuleCollider c:
                {
                    var ls = tr.lossyScale; var a = new[] { Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z) };
                    int d = c.direction; float sa = a[d], sr = Mathf.Max(a[(d + 1) % 3], a[(d + 2) % 3]);
                    float r = c.radius * sr, h = Mathf.Max(c.height * sa, 2f * r);
                    var axisRot = d == 0 ? Quaternion.Euler(0, 0, 90) : d == 2 ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
                    m = Matrix4x4.TRS(tr.TransformPoint(c.center), tr.rotation * axisRot, new Vector3(2f * r, h * 0.5f, 2f * r)); _cb.DrawMesh(_capsule, m, _maskMat, 0, 0); return;
                }
                case MeshCollider mc when mc.sharedMesh:
                    for (int i = 0; i < mc.sharedMesh.subMeshCount; i++) _cb.DrawMesh(mc.sharedMesh, tr.localToWorldMatrix, _maskMat, i, 0);
                    return;
            }
            var bb = col.bounds; _cb.DrawMesh(_cube, Matrix4x4.TRS(bb.center, Quaternion.identity, bb.size), _maskMat, 0, 0); // other collider types: bounds
        }
    }
}
