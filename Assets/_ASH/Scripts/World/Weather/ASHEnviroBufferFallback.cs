using System;
using UnityEngine;

namespace ASH.World.Weather
{
    /// <summary>
    /// Enviro 3 particle/fog shaders (Enviro3/Particles/WeatherParticles, fog includes) declare
    /// StructuredBuffer _EnviroRemovalZones + float _EnviroRemovalZonesCount. Only EnviroManager binds them, so with no active
    /// EnviroManager (ASH drives the Enviro particle materials itself) D3D12 logs "requires a buffer (SRV) _EnviroRemovalZones
    /// ... Skipping draw calls". This binds a zeroed 1-element dummy (count 0) globally whenever no EnviroManager is active,
    /// re-checked every second, and releases it on quit / domain reload. No vendor files are changed.
    /// </summary>
    public sealed class ASHEnviroBufferFallback : MonoBehaviour
    {
        /// <summary>sizeof(EnviroRemovalZones) in ParticlesInclude.cginc: 32 floats (type, pos3, radius, size3, axis3, stretch,
        /// density, feather, float4x4, pad0, pad1) = 128 bytes.</summary>
        public const int Stride = 128;
        static readonly int BufId = Shader.PropertyToID("_EnviroRemovalZones"), CountId = Shader.PropertyToID("_EnviroRemovalZonesCount");
        static ComputeBuffer _dummy; static Type _mgrType; static bool _typeLooked;
        float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<ASHEnviroBufferFallback>()) return;
            var go = new GameObject("ASH_EnviroBufferFallback") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            DontDestroyOnLoad(go); go.AddComponent<ASHEnviroBufferFallback>();
            Application.quitting -= Release; Application.quitting += Release;
        }

        static bool EnviroManagerActive()
        {
            if (!_typeLooked) { _typeLooked = true; _mgrType = Type.GetType("Enviro.EnviroManager, Enviro3.Runtime"); }
            if (_mgrType == null) return false;
            var m = FindAnyObjectByType(_mgrType) as Behaviour;
            return m && m.isActiveAndEnabled;
        }

        /// <summary>Binds the dummy buffer (count 0) unless an active EnviroManager provides the real one.</summary>
        public static void BindIfNeeded()
        {
            if (!SystemInfo.supportsComputeShaders || EnviroManagerActive()) return;
            if (_dummy == null || !_dummy.IsValid())
            {
                _dummy = new ComputeBuffer(1, Stride, ComputeBufferType.Structured);
                _dummy.SetData(new float[Stride / 4]);
            }
            Shader.SetGlobalBuffer(BufId, _dummy);
            Shader.SetGlobalFloat(CountId, 0f);
        }

        public static void Release() { if (_dummy != null) { _dummy.Release(); _dummy = null; } }

        void OnEnable() => BindIfNeeded();
        void Update() { if (Time.unscaledTime >= _next) { _next = Time.unscaledTime + 1f; BindIfNeeded(); } }
        void OnDestroy() => Release();
    }
}
