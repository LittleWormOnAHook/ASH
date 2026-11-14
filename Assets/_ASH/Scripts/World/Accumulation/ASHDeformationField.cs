using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ASH.World.Accumulation
{
    /// <summary>
    /// Player-following deformation field (Play Mode): a ping-pong render texture (R = fraction of the ash/snow layer pushed
    /// aside) that scrolls with the follow target, refills from new fall (controller.DeformRefillPerSecond) and takes stamps
    /// from every ASHDeformer plus one-shot ASHAccumulationController.Deform calls. Shaders read it as _ASH_DeformTex.
    /// The ONLY deformation implementation: weather/vehicles/animals add ASHDeformer or call Deform, never a second field.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHDeformationField : MonoBehaviour
    {
        public Shader updateShader;
        [Tooltip("Texels per side (1024 over 128 m = 12.5 cm per texel).")] [Range(256, 4096)] public int resolution = 1024;
        [Tooltip("Window size in metres; trails outside the window are forgotten.")] [Range(16f, 512f)] public float sizeMetres = 128f;
        [Tooltip("Defaults to the player, then Camera.main.")] public Transform follow;
        [Range(0.02f, 1f)] public float readbackInterval = 0.15f;
        [Tooltip("Stamp/scroll updates per second (0 = every frame). Set by the controller's quality.")] [Range(0f, 120f)] public float stampRate = 0f;
        [Tooltip("Refill-only updates per second when nothing stamps or scrolls.")] [Range(0.5f, 60f)] public float refillRate = 4f;
        [Tooltip("CPU readback region radius around the follow target (gameplay depth queries), metres.")] [Range(4f, 64f)] public float readbackRadius = 16f;

        public bool Active { get; private set; }
        RenderTexture _a, _b; Material _mat; Vector2 _origin; bool _init; float _nextRead, _builtSize, _pendingDt, _lastStamp, _lastRefill;
        readonly Vector4[] _segA = new Vector4[32], _parA = new Vector4[32];
        ushort[] _cpu; Vector4 _cpuRect; bool _cpuValid; int _cpuW, _cpuH; // rect: x0, z0, texel, -
        readonly List<Vector4> _seg = new List<Vector4>(32), _par = new List<Vector4>(32), _oneShotSeg = new List<Vector4>(), _oneShotPar = new List<Vector4>();
        static readonly Vector4[] _zero = new Vector4[32];
        static readonly int ID_Rect = Shader.PropertyToID("_ASH_DF_Rect"), ID_Off = Shader.PropertyToID("_ASH_DF_Offset"), ID_Seg = Shader.PropertyToID("_ASH_DF_Seg"),
            ID_SegP = Shader.PropertyToID("_ASH_DF_SegP"), ID_Count = Shader.PropertyToID("_ASH_DF_Count"), ID_Tex = Shader.PropertyToID("_ASH_DeformTex"), ID_DRect = Shader.PropertyToID("_ASH_DeformRect");

        public void AddStamp(Vector3 a, Vector3 b, float radius, float strength) { _oneShotSeg.Add(new Vector4(a.x, a.z, b.x, b.z)); _oneShotPar.Add(new Vector4(radius, Mathf.Clamp01(strength), 0, 0)); }

        public string DebugState => $"follow {(follow ? follow.name : "-")} {(follow ? follow.position : Vector3.zero)}, cpu valid {_cpuValid} rect ({_cpuRect.x:F1},{_cpuRect.y:F1}) texel {_cpuRect.z:F3} w {_cpuW}, res {resolution}, size {sizeMetres}, pending one-shots {_oneShotSeg.Count}";
        /// <summary>Debug: world position and value of the strongest deformation in the CPU copy.</summary>
        public Vector4 DebugMax()
        {
            if (!_cpuValid) return Vector4.zero; float best = 0; int bi = 0;
            for (int i = 0; i < _cpu.Length; i++) { float v = Mathf.HalfToFloat(_cpu[i]); if (v > best) { best = v; bi = i; } }
            return new Vector4(_cpuRect.x + (bi % _cpuW + 0.5f) * _cpuRect.z, 0, _cpuRect.y + (bi / _cpuW + 0.5f) * _cpuRect.z, best);
        }
        public Vector3 WindowCentre => new Vector3(_origin.x + sizeMetres * 0.5f, 0f, _origin.y + sizeMetres * 0.5f);

        void Ensure()
        {
            resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 256, 4096);
            if (_a && _a.width == resolution && Mathf.Approximately(_builtSize, sizeMetres)) return;
            Release();
            _a = NewRT(); _b = NewRT();
            if (!updateShader) updateShader = Shader.Find("Hidden/ASH/DeformUpdate");
            _mat = updateShader ? new Material(updateShader) { hideFlags = HideFlags.HideAndDontSave } : null;
            if (_mat) { _mat.SetVectorArray(ID_Seg, _zero); _mat.SetVectorArray(ID_SegP, _zero); }
            _init = false; _cpuValid = false; _builtSize = sizeMetres; _pendingDt = 0f;
        }
        RenderTexture NewRT()
        {
            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
            { name = "ASH_DeformField", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, hideFlags = HideFlags.HideAndDontSave };
            rt.Create(); var prev = RenderTexture.active; RenderTexture.active = rt; GL.Clear(false, true, Color.clear); RenderTexture.active = prev; return rt;
        }
        void Release() { if (_a) { _a.Release(); Destroy(_a); } if (_b) { _b.Release(); Destroy(_b); } if (_mat) Destroy(_mat); _a = _b = null; _mat = null; }

        void OnDisable() { Active = false; Release(); Shader.SetGlobalVector(ID_DRect, Vector4.zero); Shader.SetGlobalTexture(ID_Tex, Texture2D.blackTexture); }

        void LateUpdate()
        {
            var c = ASHAccumulationController.Instance;
            if (!Application.isPlaying || !c || !c.deformation || !c.DeformationQualityOn) { if (Active) OnDisable(); return; }
            Ensure(); if (!_mat) return;
            Transform f = follow;
            if (!f) { var pl = GameObject.FindWithTag("Player"); f = pl ? pl.transform : (Camera.main ? Camera.main.transform : null); follow = f; }
            if (!f) return;
            float texel = sizeMetres / resolution; var p = f.position;
            var o = new Vector2(Mathf.Floor((p.x - sizeMetres * 0.5f) / texel) * texel, Mathf.Floor((p.z - sizeMetres * 0.5f) / texel) * texel);
            var off = _init ? (o - _origin) / sizeMetres : Vector2.zero;

            _pendingDt += Time.deltaTime;
            float now = Time.unscaledTime;
            bool stampDue = stampRate <= 0f || now - _lastStamp >= 1f / stampRate;
            bool refillDue = now - _lastRefill >= 1f / Mathf.Max(refillRate, 0.5f);
            bool moved = _init && (o - _origin).sqrMagnitude > 0f;
            if (_init && !stampDue && !refillDue) return;
            _seg.Clear(); _par.Clear();
            if (stampDue || !_init) { _lastStamp = now; foreach (var d in ASHDeformer.All) if (d && d.isActiveAndEnabled) d.Collect(_seg, _par, c); }
            if (_init && _seg.Count == 0 && _oneShotSeg.Count == 0 && !moved && !refillDue) return;
            _lastRefill = now;
            int take = Mathf.Clamp(32 - _seg.Count, 0, _oneShotSeg.Count); // leftovers stamp next frame
            for (int i = 0; i < take; i++) { _seg.Add(_oneShotSeg[i]); _par.Add(_oneShotPar[i]); }
            _oneShotSeg.RemoveRange(0, take); _oneShotPar.RemoveRange(0, take);
            int n = Mathf.Min(32, _seg.Count);
            System.Array.Clear(_segA, 0, 32); System.Array.Clear(_parA, 0, 32);
            for (int i = 0; i < n; i++) { _segA[i] = _seg[i]; _parA[i] = _par[i]; }
            _mat.SetVectorArray(ID_Seg, _segA); _mat.SetVectorArray(ID_SegP, _parA); _mat.SetFloat(ID_Count, n);
            _mat.SetVector(ID_Rect, new Vector4(o.x, o.y, sizeMetres, 0));
            _mat.SetVector(ID_Off, new Vector4(off.x, off.y, c.DeformRefillPerSecond() * _pendingDt, _init ? 0f : 1f)); _pendingDt = 0f;
            Graphics.Blit(_a, _b, _mat);
            var t = _a; _a = _b; _b = t; _origin = o; _init = true; Active = true;
            Shader.SetGlobalTexture(ID_Tex, _a);
            Shader.SetGlobalVector(ID_DRect, new Vector4(o.x, o.y, 1f / sizeMetres, 1f));

            if (Time.unscaledTime >= _nextRead && SystemInfo.supportsAsyncGPUReadback)
            {
                _nextRead = Time.unscaledTime + readbackInterval;
                // region readback around the follow target only (cheap at 4096)
                int r = Mathf.Clamp(Mathf.CeilToInt(readbackRadius / texel), 4, resolution / 2);
                int cx = Mathf.Clamp(Mathf.RoundToInt((p.x - o.x) / texel), r, resolution - r), cz = Mathf.Clamp(Mathf.RoundToInt((p.z - o.y) / texel), r, resolution - r);
                int x0 = cx - r, z0 = cz - r, w = 2 * r;
                var rect = new Vector4(o.x + x0 * texel, o.y + z0 * texel, texel, 0f); var src = _a;
                AsyncGPUReadback.Request(src, 0, x0, w, z0, w, 0, 1, req =>
                {
                    if (req.hasError || !this || src != _a && src != _b) return;
                    var data = req.GetData<ushort>();
                    if (_cpu == null || _cpu.Length != data.Length) _cpu = new ushort[data.Length];
                    data.CopyTo(_cpu); _cpuRect = rect; _cpuW = w; _cpuH = w; _cpuValid = true;
                });
            }
        }

        /// <summary>CPU sample (0..1) of the latest readback; 0 outside the window.</summary>
        public float Sample(Vector3 p)
        {
            if (!_cpuValid) return 0f;
            int x = Mathf.FloorToInt((p.x - _cpuRect.x) / _cpuRect.z), y = Mathf.FloorToInt((p.z - _cpuRect.y) / _cpuRect.z);
            if (x < 0 || y < 0 || x >= _cpuW || y >= _cpuH) return 0f;
            return Mathf.Clamp01(Mathf.HalfToFloat(_cpu[y * _cpuW + x]));
        }
    }
}
