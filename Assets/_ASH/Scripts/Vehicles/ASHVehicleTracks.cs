using System.Collections.Generic;
using NWH.VehiclePhysics;
using NWH.WheelController3D;
using UnityEngine;
using UnityEngine.SceneManagement;
using ASH.World.Accumulation;

namespace ASH.Vehicles
{
    /// <summary>t399/t400: tyres press packed-snow ruts into the ash/snow layer through the same deformation field the player and dog
    /// use (ASHAccumulationController.DeformSegment). A rut flattens only part of the layer (packedDepth), so it reads as compressed
    /// snow, not bare ground, and recent ruts are re-pressed for persistSeconds so new fall doesn't fill them meanwhile.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHVehicleTracks : MonoBehaviour
    {
        [Header("Packed snow tracks (t400)")]
        public bool leaveTracks = true;
        [Tooltip("Rut (tyre) width, metres.")] [Range(0.05f, 0.8f)] public float rutWidth = 0.24f;
        [Tooltip("Share of the ash/snow layer pressed down in the rut (1 = cleared to the ground, 0.6 = packed snow with 40% left).")] [Range(0f, 1f)] public float packedDepth = 0.6f;
        [Tooltip("Seconds a rut is held pressed (new fall can't fill it meanwhile); after that the normal refill / fade takes over.")] [Range(0f, 1800f)] public float persistSeconds = 300f;
        [Tooltip("Max remembered rut segments per vehicle (older ones stop being held).")] [Range(50, 4000)] public int maxSegments = 1200;
        [Tooltip("New segment every this many metres of travel.")] [Range(0.1f, 3f)] public float segmentSpacing = 0.6f;
        [Tooltip("Re-press held ruts every this many seconds.")] [Range(0.1f, 5f)] public float holdInterval = 0.5f;
        [Tooltip("Only while moving faster than this, km/h (parked wheels don't keep stamping).")] [Range(0f, 10f)] public float minSpeedKph = 0.5f;

        struct Seg { public Vector3 a, b; public float t; }
        readonly List<Seg> _segs = new List<Seg>(); int _head; float _nextHold;
        WheelController[] _wheels; Vector3[] _last; bool[] _had; Rigidbody _rb;
        public int SegmentCount => _segs.Count; public int Stamped { get; private set; }

        void Start()
        {
            _wheels = GetComponentsInChildren<WheelController>(true); _last = new Vector3[_wheels.Length]; _had = new bool[_wheels.Length]; _rb = GetComponent<Rigidbody>();
        }

        void LateUpdate()
        {
            var acc = ASHAccumulationController.Instance; if (!leaveTracks || acc == null || _wheels == null) return;
            float r = rutWidth * 0.5f, s = Mathf.Clamp01(packedDepth);
            bool moving = !_rb || _rb.linearVelocity.magnitude * 3.6f > minSpeedKph;
            for (int i = 0; i < _wheels.Length; i++)
            {
                var w = _wheels[i]; if (!w) continue;
                bool g = w.isGrounded && w.wheelHit != null;
                if (!g) { _had[i] = false; continue; }
                var p = w.wheelHit.point;
                if (!_had[i]) { _last[i] = p; _had[i] = true; acc.DeformSegment(p, p, r, s); Stamped++; continue; }
                if (!moving) continue;
                if ((p - _last[i]).sqrMagnitude < segmentSpacing * segmentSpacing * 0.25f) { acc.DeformSegment(_last[i], p, r, s); continue; }
                if ((p - _last[i]).sqrMagnitude > 16f) { _last[i] = p; continue; } // teleport guard
                acc.DeformSegment(_last[i], p, r, s); Stamped++;
                Remember(_last[i], p);
                if ((p - _last[i]).sqrMagnitude >= segmentSpacing * segmentSpacing) _last[i] = p;
            }
            if (persistSeconds > 0f && Time.time >= _nextHold)
            {
                _nextHold = Time.time + holdInterval; float now = Time.time;
                foreach (var sg in _segs) if (now - sg.t < persistSeconds) acc.DeformSegment(sg.a, sg.b, r, s);
            }
        }

        void Remember(Vector3 a, Vector3 b)
        {
            var sg = new Seg { a = a, b = b, t = Time.time };
            if (_segs.Count < maxSegments) _segs.Add(sg); else { _segs[_head] = sg; _head = (_head + 1) % maxSegments; }
        }
    }

    /// <summary>t399/t403: NWH vehicles warn "No GroundDetection component found in the scene" unless one exists. One is made at Play
    /// start (runtime only, never saved): every terrain layer and plain ground maps to a surface that keeps each wheel's own friction
    /// preset (so handling doesn't change), or to frictionOverride if set. No NWH skidmarks: the ruts come from ASHVehicleTracks.</summary>
    public static class ASHGroundDetectionBootstrap
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHVehicleTracks()
        {
            Instance = default;
        }

        public static GroundDetection Instance { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { Make(); SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded; }
        static void OnLoaded(Scene s, LoadSceneMode m) => Make();
        public static void Make()
        {
            if (Instance || Object.FindAnyObjectByType<GroundDetection>()) { if (!Instance) Instance = Object.FindAnyObjectByType<GroundDetection>(); return; }
            var fx = Resources.Load<ASHVehicleSurfaceFx>(ASHVehicleSurfaceFx.ResPath);
            var go = new GameObject("ASH_GroundDetection (runtime)"); go.SetActive(false); var gd = go.AddComponent<GroundDetection>(); Instance = gd;
            if (fx) { gd.smokePrefab = fx.smokePrefab; gd.dustPrefab = fx.dustPrefab; }
            var wc = Object.FindAnyObjectByType<WheelController>();
            var fr = wc ? wc.activeFrictionPresetEnum : WheelController.FrictionPreset.FrictionPresetEnum.Generic;
            var terrainIdx = new List<int>(); int layers = 0;
            foreach (var t in Terrain.activeTerrains) if (t && t.terrainData) layers = Mathf.Max(layers, t.terrainData.alphamapLayers);
            for (int i = 0; i < Mathf.Max(1, layers); i++) terrainIdx.Add(i);
            gd.groundEntities.Add(new GroundDetection.GroundEntity { name = "Ash / snow (terrain)", terrainTextureIndices = terrainIdx, frictionPresetEnum = fr, surfaceSoundComponent = new GroundDetection.SoundComponent(), skidSoundComponent = new GroundDetection.SoundComponent(),
                dustColor = fx ? fx.dustColor : Color.white, dustIntensity = fx ? fx.terrainDustIntensity : 0f, smokeIntensity = fx ? fx.smokeIntensity : 30f });
            gd.groundEntities.Add(new GroundDetection.GroundEntity { name = "Ground (colliders)", tags = new List<string> { "Untagged", "Terrain", "Ground" }, frictionPresetEnum = fr, surfaceSoundComponent = new GroundDetection.SoundComponent(), skidSoundComponent = new GroundDetection.SoundComponent(),
                dustColor = fx ? fx.dustColor : Color.white, dustIntensity = fx ? fx.groundDustIntensity : 0f, smokeIntensity = fx ? fx.smokeIntensity : 30f });
            go.SetActive(true);
            Debug.Log($"[ASH] Vehicles: GroundDetection made at Play start ({terrainIdx.Count} terrain layers + ground, friction {fr}, smoke={(gd.smokePrefab ? gd.smokePrefab.name : "none")}, dust={(gd.dustPrefab ? gd.dustPrefab.name : "none")}).");
        }
    }
}
