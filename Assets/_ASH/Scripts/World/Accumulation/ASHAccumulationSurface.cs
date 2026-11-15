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
        [Tooltip("Mask Max Resolution: caps the collider mask (the measured walking cost: at 4096 every rebuild was ~17M pixels x ~50 taps, twice). 2048 over the ~120 m mask = ~6 cm texels; banks look the same, the thin clear edge is slightly softer. 4096 = old look, ~8x the cost.")]
        [Range(512, 4096)] public int maskMaxResolution = 2048;
        [Tooltip("Mask Move Margin: metres of mask beyond the surface on each side. The mask is redrawn only after the player walks ~75% of this (was 3 m, i.e. a full redraw every 3 m of walking).")]
        [Range(4f, 40f)] public float maskMoveMargin = 12f;
        [Tooltip("Include trigger colliders of pickups (other triggers, e.g. interaction zones, never clear the ash).")] public bool pickupTriggersClear = true;

        [Header("Banking (ash/snow piles up softly against objects instead of clearing around them)")]
        [Tooltip("Raise the layer into a soft bank against every collider at least Bank Min Collider Size big (smaller ones keep the collider fade).")] public bool banking = true;
        [Tooltip("Bank height at the collider's footprint edge, metres (on top of the layer, at full layer depth).")] [Range(0f, 2f)] public float bankHeight = 0.12f;
        [Tooltip("How far out from the collider the bank reaches, metres.")] [Range(0.05f, 4f)] public float bankReach = 0.7f;
        [Tooltip("Bank curve: 1 = smooth S ramp, >1 = piled steeply against the object, <1 = broad gentle swell.")] [Range(0.2f, 6f)] public float bankCurve = 1.6f;
        [Tooltip("Noise breakup scale (1/m): higher = smaller lumps along the bank.")] [Range(0.05f, 10f)] public float bankNoiseScale = 1.3f;
        [Tooltip("Noise breakup strength: varies bank height and reach (0 = perfectly even).")] [Range(0f, 1f)] public float bankNoiseStrength = 0.45f;
        [Tooltip("Layer depth (m) at which banks reach full height; thinner layers get proportionally smaller banks.")] [Range(0.005f, 1f)] public float bankFullAtDepth = 0.06f;
        [Tooltip("Colliders smaller than this (largest bounds side, metres) don't bank: grass, small props, pickups keep the collider fade.")] [Range(0f, 10f)] public float bankMinColliderSize = 0.6f;
        [Tooltip("Light dusting on the bottom of objects standing in the layer (ASH Accumulation Lit materials).")] public bool baseDusting = true;
        [Tooltip("Dusting height above the bank / layer top, metres (varies with noise).")] [Range(0f, 0.5f)] public float dustHeight = 0.04f;
        [Tooltip("Dusting fade width, metres.")] [Range(0.005f, 0.5f)] public float dustFade = 0.04f;
        [Tooltip("Dusting strength.")] [Range(0f, 1f)] public float dustStrength = 0.75f;

        GameObject _go; Mesh _mesh; Material _mat; MeshRenderer _mr; float _builtSize, _builtStep; float _nextBlock;
        RenderTexture _maskRaw, _maskRow, _mask; Material _maskMat; CommandBuffer _cb; Vector2 _maskMin; float _maskSize; int _maskRes;
        Mesh _cube, _sphere, _capsule;
        static readonly int ID_HM = Shader.PropertyToID("_ASH_Heightmap"), ID_HP = Shader.PropertyToID("_ASH_HeightParams"), ID_SR = Shader.PropertyToID("_ASH_SurfaceRect"),
            ID_CM = Shader.PropertyToID("_ASH_ColliderMask"), ID_CMR = Shader.PropertyToID("_ASH_ColliderMaskRect"), ID_GS = Shader.PropertyToID("_ASH_GridStep"), ID_SQ = Shader.PropertyToID("_ASH_SurfQuality"), ID_SN = Shader.PropertyToID("_ASH_SurfNormal"), ID_Thin = Shader.PropertyToID("_ASH_Thin"),
            ID_Bank = Shader.PropertyToID("_ASH_Bank"), ID_Bank2 = Shader.PropertyToID("_ASH_Bank2"), ID_Dust = Shader.PropertyToID("_ASH_Dust"), ID_CFMBank = Shader.PropertyToID("_ASH_CFM_Bank");
        bool BankOn => banking && bankHeight > 0f && colliderFade;

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
            Shader.SetGlobalVector(ID_CMR, Vector4.zero); Shader.SetGlobalVector(ID_Bank, Vector4.zero); Shader.SetGlobalVector(ID_Dust, Vector4.zero);
        }
        void ReleaseMask() { foreach (var r in new[] { _maskRaw, _maskRow, _mask }) if (r) { r.Release(); Destroy(r); } _maskRaw = _maskRow = _mask = null; _maskRes = 0; _maskValid = false; _colsPending = false; }

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
            float mtx = _maskRes > 0 ? _maskSize / _maskRes : colliderMaskTexel;
            Shader.SetGlobalVector(ID_Bank, new Vector4(bankHeight, bankReach, bankCurve, BankOn ? 1f : 0f));
            Shader.SetGlobalVector(ID_Bank2, new Vector4(bankNoiseScale, bankNoiseStrength, bankFullAtDepth, mtx));
            Shader.SetGlobalVector(ID_Dust, new Vector4(dustHeight, dustFade, dustStrength, BankOn && baseDusting ? 1f : 0f));
            if (BankOn != _builtBank) _nextBlock = 0f; // rebuild the mask when banking is toggled
            if (_colsPending) { FinishMask(); return; } // second half of a rebuild (spread over two frames)
            bool move = MaskNeedsMove(centre);
            if (Time.unscaledTime >= _nextBlock || move) { _nextBlock = Time.unscaledTime + blockerRefresh; RefreshBlockers(move || _maskRes == 0 ? centre : _maskCentre, t); } // timed refreshes keep the window (no redraw just for walking)
        }

        // ---- collider fade mask -------------------------------------------------------------------------------------------
        readonly List<Collider> _cols = new List<Collider>(); readonly Collider[] _hits = new Collider[2048];
        public int ColliderFadeCount { get; private set; }
        public RenderTexture ColliderMask => _mask;
        public Vector4 ColliderMaskRect => new Vector4(_maskMin.x, _maskMin.y, _maskSize, _maskRes);

        Mesh Prim(PrimitiveType t) { var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); return m; }

        // static part of the Clears test, cached per collider (GetComponentInParent x3 per collider every rebuild was a CPU spike)
        struct ColInfo { public bool ok; public ASHChoppable ch; }
        readonly Dictionary<Collider, ColInfo> _colInfo = new Dictionary<Collider, ColInfo>();
        ColInfo Info(Collider col)
        {
            if (_colInfo.TryGetValue(col, out var i)) return i;
            i.ok = !(col is TerrainCollider || col is WheelCollider || col is CharacterController);
            if (i.ok && col.isTrigger && !(pickupTriggersClear && col.GetComponentInParent<ASHItemPickup>())) i.ok = false;
            if (i.ok) { var an = col.GetComponentInParent<Animator>(); if (an && an.isHuman) i.ok = false; }   // characters / animals
            if (i.ok) i.ch = col.GetComponentInParent<ASHChoppable>();
            if (_colInfo.Count > 8192) _colInfo.Clear();
            _colInfo[col] = i; return i;
        }

        bool Clears(Collider col, Transform player, Terrain t)
        {
            if (!col || !col.enabled) return false;
            var info = Info(col); if (!info.ok) return false;
            if (player && col.transform.IsChildOf(player)) return false;
            if (info.ch && !info.ch.BlocksSurface) return false;  // standing trees stay as before
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
            float span = size + 2f * Mathf.Max(4f, maskMoveMargin);
            float tx = colliderMaskTexel; int res = Mathf.Clamp(Mathf.CeilToInt(span / tx), 64, Mathf.Clamp(Mathf.ClosestPowerOfTwo(maskMaxResolution), 512, 4096)); tx = Mathf.Max(tx, span / res);
            if (_maskRes != res) { ReleaseMask(); _maskRes = res;
                _maskRaw = NewRT(res, RenderTextureFormat.RG16, FilterMode.Point, "ASH_ColliderFootprint");   // R clearing, G banking collider
                _maskRow = NewRT(res, RenderTextureFormat.RGHalf, FilterMode.Point, "ASH_ColliderFadeRows");
                _mask = NewRT(res, RenderTextureFormat.RGHalf, FilterMode.Bilinear, "ASH_ColliderFade"); }        // R fade, G bank distance m
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

            // only redraw when something changed (collider set, their poses, settings or the mask window)
            bool bank = BankOn;
            int sig = 17;
            unchecked
            {
                sig = sig * 31 + _maskMin.GetHashCode(); sig = sig * 31 + res; sig = sig * 31 + (bank ? 1 : 0);
                sig = sig * 31 + colliderFadePadding.GetHashCode(); sig = sig * 31 + colliderFadeSoftness.GetHashCode(); sig = sig * 31 + colliderFootprintHeight.GetHashCode();
                sig = sig * 31 + bankReach.GetHashCode(); sig = sig * 31 + bankNoiseStrength.GetHashCode(); sig = sig * 31 + bankMinColliderSize.GetHashCode();
                foreach (var col in _cols) { var b = col.bounds; sig = sig * 31 + col.GetInstanceID(); sig = sig * 31 + Vector3Int.RoundToInt(b.center * 50f).GetHashCode(); sig = sig * 31 + Vector3Int.RoundToInt(b.size * 50f).GetHashCode(); }
            }
            if (sig == _maskSig && _maskValid) return;
            _maskSig = sig; _maskValid = true;

            if (_cb == null) _cb = new CommandBuffer { name = "ASH Collider Fade Mask" };
            _cb.Clear();
            _cb.SetGlobalVector("_ASH_CFM_Rect", new Vector4(_maskMin.x, _maskMin.y, 1f / _maskSize, colliderFootprintHeight));
            _builtBank = bank;
            // two-tier distance search: exact 1-texel steps through padding + softness (the clearing edge), then coarser steps
            // out to the bank reach (the bank profile is smooth, so a 2-3 texel step there is invisible) - ~half the taps
            float fineReach = colliderFadePadding + colliderFadeSoftness + tx;
            int nf = Mathf.Clamp(Mathf.CeilToInt(fineReach / tx), 1, 48);
            float reach = Mathf.Max(fineReach, bank ? bankReach * (1f + 0.4f * bankNoiseStrength) + tx : 0f);
            int st = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0f, reach - nf * tx) / tx / 24f));
            int nc = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0f, reach - nf * tx) / tx / st), 0, 64 - nf);
            _cb.SetGlobalVector("_ASH_CFM_Params", new Vector4(tx, colliderFadePadding, colliderFadeSoftness, st));
            _cb.SetGlobalFloat("_ASH_CFM_Taps", nf + nc); _cb.SetGlobalFloat("_ASH_CFM_Fine", nf); _cb.SetGlobalFloat("_ASH_CFM_Res", res);
            _cb.SetRenderTarget(_maskRaw); _cb.ClearRenderTarget(false, true, Color.clear);
            foreach (var col in _cols)
            {
                var cb = col.bounds; bool banks = bank && Mathf.Max(cb.size.x, cb.size.y, cb.size.z) >= bankMinColliderSize && CanBank(col);
                _cb.SetGlobalFloat(ID_CFMBank, banks ? 1f : 0f);
                DrawCollider(col);
            }
            _cb.SetGlobalTexture("_ASH_CFM_Src", _maskRaw); _cb.SetRenderTarget(_maskRow); _cb.DrawProcedural(Matrix4x4.identity, _maskMat, 1, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_cb);
            _colsPending = true; // columns pass + publish next frame (halves the GPU spike; the old mask stays live until then)
        }

        void FinishMask()
        {
            _colsPending = false;
            if (!_mask || !_maskRow || !_maskMat) return;
            _cb.Clear();
            _cb.SetGlobalTexture("_ASH_CFM_Src", _maskRow); _cb.SetRenderTarget(_mask); _cb.DrawProcedural(Matrix4x4.identity, _maskMat, 2, MeshTopology.Triangles, 3);
            Graphics.ExecuteCommandBuffer(_cb);
            Shader.SetGlobalTexture(ID_CM, _mask);
            Shader.SetGlobalVector(ID_CMR, new Vector4(_maskMin.x, _maskMin.y, 1f / _maskSize, 1f));
        }
        bool _colsPending, _maskValid; int _maskSig;
        Vector3 _maskCentre; bool _builtBank;
        readonly Dictionary<Collider, bool> _bankable = new Dictionary<Collider, bool>();

        // Banks only form against visible, static-ish things: not invisible helpers (no renderer / Ignore Raycast layer, e.g.
        // the opening's exit blockers), not swinging doors, not pickups. Cached per collider.
        bool CanBank(Collider col)
        {
            if (_bankable.TryGetValue(col, out var ok)) return ok;
            ok = !col.isTrigger && col.gameObject.layer != 2 && !col.GetComponentInParent<ASHDoor>() && !col.GetComponentInParent<ASHItemPickup>()
                 && (col.GetComponent<Renderer>() || col.GetComponentInParent<Renderer>() || col.GetComponentInChildren<Renderer>());
            if (_bankable.Count > 4096) _bankable.Clear();
            _bankable[col] = ok; return ok;
        }
        public bool MaskNeedsMove(Vector3 centre) { float m = Mathf.Max(3f, Mathf.Max(4f, maskMoveMargin) * 0.75f); return _maskRes > 0 && (Mathf.Abs(centre.x - _maskCentre.x) > m || Mathf.Abs(centre.z - _maskCentre.z) > m); }

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
