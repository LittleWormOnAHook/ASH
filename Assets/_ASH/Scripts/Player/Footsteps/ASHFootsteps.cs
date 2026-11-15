using System.Collections.Generic;
using ASH.FX;
using ASH.World.Accumulation;
using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// ASH footsteps: detects foot plants from the Humanoid feet (works with root motion, foot IK and no animation events),
    /// resolves the surface through ASHFootstepSurfaceMap, plays pooled 3D audio (louder/crunchier with ash/snow depth) and asks
    /// ASHFootstepPuffs for an ash/snow puff. Replaces the unconfigured Invector vFootStep on ASH_Player (it had no foot triggers).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHFootsteps : MonoBehaviour
    {
        public ASHFootstepSurfaceMap map;
        public ASHFootstepPuffs puffs;
        [Header("Detection")]
        [Tooltip("Sole height above ground (m) that counts as planted.")] [Range(0.01f, 0.2f)] public float plantHeight = 0.07f;
        [Tooltip("Sole must rise above this (m) before the next plant on that foot.")] [Range(0.02f, 0.4f)] public float liftHeight = 0.11f;
        [Tooltip("Minimum horizontal speed (m/s) for steps.")] [Range(0f, 2f)] public float minSpeed = 0.35f;
        [Range(0.1f, 1f)] public float minInterval = 0.24f;
        [Tooltip("Speed treated as a full run (m/s) for volume / puff size.")] [Range(1f, 10f)] public float runSpeed = 5.5f;
        public LayerMask groundLayers = ~(1 << 13 | 1 << 2);
        [Header("Audio")]
        [Range(0f, 1f)] public float masterVolume = 0.9f;
        [Range(1, 8)] public int voices = 4;
        [Range(1f, 60f)] public float maxDistance = 25f;
        [Tooltip("Walk volume factor (run = 1).")] [Range(0f, 1f)] public float walkVolume = 0.6f;
        public bool debugLog;

        public string LastSurface { get; private set; } = "";
        public float LastDepth { get; private set; }
        public int StepCount { get; private set; }

        Animator _anim; Transform _lf, _rf; float _lb, _rb; bool _lUp = true, _rUp = true; float _lt, _rt;
        Vector3 _lastPos; float _speed; readonly List<AudioSource> _src = new List<AudioSource>(); int _next;
        readonly Dictionary<string, int> _lastClip = new Dictionary<string, int>();

        void Start()
        {
            _anim = GetComponentInChildren<Animator>();
            if (_anim && _anim.isHuman)
            {
                _lf = _anim.GetBoneTransform(HumanBodyBones.LeftFoot); _rf = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
                _lb = Mathf.Max(0.02f, _anim.leftFeetBottomHeight); _rb = Mathf.Max(0.02f, _anim.rightFeetBottomHeight);
            }
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("ASH_FootstepVoice" + i); go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1.5f; s.maxDistance = maxDistance; s.dopplerLevel = 0f;
                _src.Add(s);
            }
            if (!puffs) puffs = GetComponentInChildren<ASHFootstepPuffs>();
            _lastPos = transform.position;
        }

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var p = transform.position; var d = p - _lastPos; d.y = 0f; _lastPos = p;
            _speed = Mathf.Lerp(_speed, d.magnitude / dt, 1f - Mathf.Exp(-dt * 10f));
            if (!_lf || !_rf) return;
            Foot(_lf, _lb, ref _lUp, ref _lt);
            Foot(_rf, _rb, ref _rUp, ref _rt);
        }

        void Foot(Transform f, float bottom, ref bool up, ref float lastT)
        {
            var sole = f.position - Vector3.up * bottom;
            if (!Ground(sole, out var gy, out _)) return;
            float h = sole.y - gy;
            if (h > liftHeight) { up = true; return; }
            if (up && h < plantHeight && _speed >= minSpeed && Time.time - lastT >= minInterval) { up = false; lastT = Time.time; Step(sole); }
        }

        bool Ground(Vector3 sole, out float y, out RaycastHit hit)
        {
            if (Physics.Raycast(sole + Vector3.up * 0.6f, Vector3.down, out hit, 1.6f, groundLayers, QueryTriggerInteraction.Ignore)) { y = hit.point.y; return true; }
            var t = Terrain.activeTerrain; y = t ? t.SampleHeight(sole) + t.GetPosition().y : sole.y; return t;
        }

        /// <summary>Force a step at a world position (tests, animation events). speed01 &lt; 0 = use current speed.</summary>
        public void Step(Vector3 pos, float speed01 = -1f)
        {
            float s01 = speed01 >= 0f ? speed01 : Mathf.Clamp01(_speed / runSpeed);
            var surf = Resolve(pos, out float depth, out bool terrain, out bool road);
            LastSurface = surf != null ? surf.id : "none"; LastDepth = depth; StepCount++;
            var c = ASHAccumulationController.Instance;
            float depth01 = c && c.maxDepthMetres > 0.01f ? Mathf.Clamp01(depth / c.maxDepthMetres) : 0f;
            if (surf != null && surf.clips != null && surf.clips.Length > 0)
            {
                float vol = masterVolume * surf.volume * Mathf.Lerp(walkVolume, 1f, s01) * (1f + surf.deepVolumeBoost * depth01);
                float pitch = Random.Range(surf.pitch.x, surf.pitch.y) - surf.deepPitchDrop * depth01;
                Play(Pick(surf.id, surf.clips), pos, vol, pitch);
                if (depth01 > 0.25f && surf.deepLayerClips != null && surf.deepLayerClips.Length > 0)
                    Play(surf.deepLayerClips[Random.Range(0, surf.deepLayerClips.Length)], pos, vol * 0.5f * depth01, pitch * 0.9f);
            }
            if (puffs && surf != null && surf.puffs && !road) puffs.Puff(pos, depth, s01);
            if (debugLog) Debug.Log($"[ASH] FOOTSTEP {LastSurface} depth {depth:F3} m speed01 {s01:F2} terrain {terrain}");
        }

        AudioClip Pick(string id, AudioClip[] clips)
        {
            if (clips.Length == 1) return clips[0];
            _lastClip.TryGetValue(id, out int last); int i = Random.Range(0, clips.Length - 1); if (i >= last) i++; _lastClip[id] = i; return clips[i];
        }

        void Play(AudioClip clip, Vector3 pos, float vol, float pitch)
        {
            if (!clip || _src.Count == 0) return;
            var s = _src[_next]; _next = (_next + 1) % _src.Count;
            s.transform.position = pos; s.pitch = pitch; s.maxDistance = maxDistance; s.PlayOneShot(clip, Mathf.Clamp01(vol * ASH.Core.ASHGameSettings.CachedSfx));
        }

        /// <summary>Surface at a world position: terrain (depth -> snow, else dominant layer rule) or mesh rules.</summary>
        public ASHFootstepSurfaceMap.Surface Resolve(Vector3 pos, out float depth, out bool onTerrain) => Resolve(pos, out depth, out onTerrain, out _);
        public ASHFootstepSurfaceMap.Surface Resolve(Vector3 pos, out float depth, out bool onTerrain, out bool onRoad)
        {
            depth = 0f; onTerrain = false; onRoad = false; if (!map) return null;
            var c = ASHAccumulationController.Instance;
            bool hitSomething = Physics.Raycast(pos + Vector3.up * 0.6f, Vector3.down, out var hit, 1.6f, groundLayers, QueryTriggerInteraction.Ignore);
            var tc = hitSomething ? hit.collider as TerrainCollider : null;
            if (tc || !hitSomething)
            {
                onTerrain = true; var t = tc ? tc.GetComponent<Terrain>() : Terrain.activeTerrain;
                depth = c ? c.GetDepthMetres(pos) : 0f;
                if (c && c.GetPathFactor(pos) >= map.roadPathMin && depth < map.roadSnowDepth) { onRoad = true; return map.Get(map.roadSurface); }
                if (depth >= map.snowDepthMin) return map.Get(map.terrainDefault);
                var id = t ? ASHFootstepSurfaceMap.Match(map.terrainLayerRules, DominantLayerName(t, pos)) : null;
                return map.Get(id ?? map.terrainDefault);
            }
            var col = hit.collider; string sid = null;
            if (col.sharedMaterial) sid = ASHFootstepSurfaceMap.Match(map.physicMaterialRules, col.sharedMaterial.name);
            if (sid == null) sid = ASHFootstepSurfaceMap.Match(map.tagRules, col.tag, true);
            if (sid == null) foreach (var lr in map.layerRules) if ((lr.layers.value & (1 << col.gameObject.layer)) != 0) { sid = lr.surface; break; }
            if (sid == null)
            {
                for (var tr = col.transform; tr && sid == null; tr = tr.parent) sid = ASHFootstepSurfaceMap.Match(map.objectNameRules, tr.name);
                if (sid == null) { var r = col.GetComponentInParent<Renderer>(); if (r && r.sharedMaterial) sid = ASHFootstepSurfaceMap.Match(map.objectNameRules, r.sharedMaterial.name); }
            }
            return map.Get(sid ?? map.meshDefault);
        }

        float[,,] _splat = new float[1, 1, 1];
        string DominantLayerName(Terrain t, Vector3 pos)
        {
            var td = t.terrainData; var lp = pos - t.GetPosition();
            int x = Mathf.Clamp((int)(lp.x / td.size.x * td.alphamapWidth), 0, td.alphamapWidth - 1), z = Mathf.Clamp((int)(lp.z / td.size.z * td.alphamapHeight), 0, td.alphamapHeight - 1);
            _splat = td.GetAlphamaps(x, z, 1, 1); int best = 0; float bv = -1f;
            for (int i = 0; i < _splat.GetLength(2); i++) if (_splat[0, 0, i] > bv) { bv = _splat[0, 0, i]; best = i; }
            var layers = td.terrainLayers; if (layers == null || best >= layers.Length || !layers[best]) return "";
            return layers[best].name + " " + (layers[best].diffuseTexture ? layers[best].diffuseTexture.name : "");
        }
    }
}
