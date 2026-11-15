using UnityEngine;

namespace ASH.World.Weather
{
    /// <summary>
    /// Cheap procedural cloud deck (shader ASH/CloudLayer) on a camera-following dome: one transparent draw, no textures,
    /// lit by the main light (our sun / day-night clock). Coverage and darkness come from the weather director through
    /// ASHWeatherOutputs; clouds drift with the shared wind. Everything it creates is runtime-only (DontSave).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHCloudLayer : MonoBehaviour
    {
        public Shader shader;
        public bool showClouds = true;
        [Tooltip("Multiplies the weather's cloud coverage.")] [Range(0f, 2f)] public float coverageScale = 1f;
        [Range(0f, 1f)] public float opacity = 0.95f;
        [Tooltip("Cloud size: higher = smaller clouds.")] [Range(0.05f, 10f)] public float scale = 1.2f;
        [Range(0.5f, 12f)] public float edgeSharpness = 3f;
        [Range(1, 5)] public int detailOctaves = 4;
        [Tooltip("Cloud drift speed per m/s of ground wind (clouds move faster aloft).")] [Range(0f, 20f)] public float windDrift = 3f;
        [Range(0f, 4f)] public float sunScatter = 0.8f;
        public Color tint = Color.white;
        public Color shadowColor = new Color(0.42f, 0.45f, 0.52f);
        [Tooltip("Dome radius as a fraction of the camera far plane.")] [Range(0.2f, 0.98f)] public float domeFraction = 0.9f;

        Material _mat; Transform _dome; Vector2 _offset; Camera _cam;
        public float Coverage { get; private set; }
        static readonly int ID_Cov = Shader.PropertyToID("_Coverage"), ID_Dark = Shader.PropertyToID("_Darkness"), ID_Wind = Shader.PropertyToID("_WindOffset"),
            ID_Op = Shader.PropertyToID("_Opacity"), ID_Scale = Shader.PropertyToID("_Scale"), ID_Sharp = Shader.PropertyToID("_Sharpness"), ID_Det = Shader.PropertyToID("_Detail"),
            ID_Scat = Shader.PropertyToID("_SunScatter"), ID_Tint = Shader.PropertyToID("_Tint"), ID_Shadow = Shader.PropertyToID("_ShadowColor");

        void Build()
        {
            if (_dome) return;
            if (!shader) shader = Shader.Find("ASH/CloudLayer");
            if (!shader) { Debug.LogWarning("[ASH] Cloud layer: shader ASH/CloudLayer not found"); enabled = false; return; }
            _mat = new Material(shader) { name = "ASH_CloudLayer (runtime)", hideFlags = HideFlags.DontSave };
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = "ASH_CloudDome"; go.hideFlags = HideFlags.DontSave;
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = _mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _dome = go.transform; _dome.SetParent(transform, false);
        }

        public void Drive(float coverage, float darkness, Vector2 windDir, float windMS, float dt)
        {
            Build(); if (!_mat) return;
            Coverage = Mathf.Clamp01(coverage * coverageScale);
            _offset += windDir * (windMS * windDrift * dt / 1000f);
            if (_offset.sqrMagnitude > 1e6f) _offset = Vector2.zero;
            _mat.SetFloat(ID_Cov, Coverage); _mat.SetFloat(ID_Dark, Mathf.Clamp01(darkness)); _mat.SetVector(ID_Wind, new Vector4(_offset.x, _offset.y, 0, 0));
            _mat.SetFloat(ID_Op, opacity); _mat.SetFloat(ID_Scale, scale); _mat.SetFloat(ID_Sharp, edgeSharpness); _mat.SetFloat(ID_Det, detailOctaves);
            _mat.SetFloat(ID_Scat, sunScatter); _mat.SetColor(ID_Tint, tint); _mat.SetColor(ID_Shadow, shadowColor);
            _dome.gameObject.SetActive(showClouds && Coverage > 0.001f);
        }

        void LateUpdate()
        {
            if (!_dome) return;
            if (!_cam || !_cam.isActiveAndEnabled) _cam = Camera.main;
            if (!_cam) return;
            _dome.position = _cam.transform.position; _dome.rotation = Quaternion.identity;
            _dome.localScale = Vector3.one * (_cam.farClipPlane * domeFraction * 2f);
        }

        void OnDestroy() { if (_dome) Destroy(_dome.gameObject); if (_mat) Destroy(_mat); }
    }
}
