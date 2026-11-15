using ASH.Gameplay;
using ASH.World.Weather;
using UnityEngine;

namespace ASH.Opening
{
    /// <summary>
    /// Dramatized local shaking for gameplay (not a claim about real eruption effects at this distance). Intensity follows a
    /// configurable curve over Duration: additive camera shake through ASHCameraKick (applied after the Invector camera and
    /// undone every frame, so camera states / the reset fix are untouched), low rumble, creaks and rattles, loose props
    /// (ASHQuakeProp) and radio static. Never touches player input. Cheap: a handful of props, pooled one-shots.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHEarthquakeEffects : MonoBehaviour
    {
        [Tooltip("Normalized time (0-1 of Duration) -> intensity (0-1). Builds, peaks briefly, then fades.")]
        public AnimationCurve intensityCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.12f, 0.25f), new Keyframe(0.3f, 0.55f), new Keyframe(0.42f, 1f), new Keyframe(0.55f, 0.7f), new Keyframe(0.75f, 0.3f), new Keyframe(1f, 0f));
        [Range(2f, 180f)] public float duration = 24f;
        [Range(0f, 3f)] public float maxIntensity = 1f;
        [Header("Camera (additive)")]
        [Range(0f, 10f)] public float maxRotationDeg = 1.1f;
        [Range(0f, 0.5f)] public float maxOffsetMetres = 0.035f;
        [Range(0.5f, 30f)] public float shakeFrequency = 9f;
        [Header("Sound")]
        public ASHPhase2AAudio audioBank;
        [Range(0f, 1f)] public float rumbleVolume = 0.85f;
        [Tooltip("Creak/rattle one-shots per second at full intensity.")] [Range(0f, 20f)] public float clattersPerSecond = 3f;
        [Tooltip("Where creaks and rattles come from (the cabin interior); defaults to this transform.")]
        public BoxCollider soundArea;
        public ASHRadioController radio;

        AudioSource _rumble; float _t = -1f, _seed, _tail;
        public float Intensity { get; private set; }
        public float Peak { get; private set; }
        public bool Running => _t >= 0f && _t < duration;
        public bool Done { get; private set; }
        public int Clatters { get; private set; }

        void Awake()
        {
            var go = new GameObject("Rumble"); go.transform.SetParent(transform, false);
            _rumble = go.AddComponent<AudioSource>(); _rumble.loop = true; _rumble.spatialBlend = 0.25f; _rumble.volume = 0f; _rumble.playOnAwake = false; _rumble.dopplerLevel = 0f;
            _seed = Random.Range(0f, 100f);
        }

        public void Begin()
        {
            _t = 0f; Done = false; Peak = 0f;
            if (audioBank && audioBank.rumbleLoop) { _rumble.clip = audioBank.rumbleLoop; _rumble.Play(); }
            if (audioBank) ASHSfx.Play("quakeSwell", audioBank.rumbleSwell, Area(), rumbleVolume * Sfx(), 0.9f, 0.05f, 0.3f, 60f);
        }

        public void Stop() { _t = -1f; Intensity = 0f; ASHCameraKick.SetShake(Vector3.zero, Vector3.zero); _rumble.Stop(); foreach (var p in ASHQuakeProp.All) if (p) p.Drive(0f, 0f); }

        float Sfx() => audioBank ? audioBank.sfxMaster : 1f;

        void Update()
        {
            if (_t < 0f) { if (_tail > 0f) { _tail -= Time.deltaTime; foreach (var p in ASHQuakeProp.All) if (p && p.kind == ASHQuakeProp.Kind.Swing) p.Drive(0f, Time.deltaTime); } return; }
            float dt = Time.deltaTime; _t += dt;
            float n = Mathf.Clamp01(_t / duration);
            Intensity = Mathf.Max(0f, intensityCurve.Evaluate(n)) * maxIntensity; Peak = Mathf.Max(Peak, Intensity);
            // camera: smooth noise, additive (ASHCameraKick applies and removes it every frame)
            float f = Time.time * shakeFrequency + _seed;
            var rot = new Vector3(Mathf.PerlinNoise(f, 0.1f) - 0.5f, Mathf.PerlinNoise(f, 0.7f) - 0.5f, (Mathf.PerlinNoise(f, 1.3f) - 0.5f) * 0.6f) * 2f * maxRotationDeg * Intensity;
            var pos = new Vector3(Mathf.PerlinNoise(f * 1.3f, 2.1f) - 0.5f, Mathf.PerlinNoise(f * 1.3f, 2.9f) - 0.5f, Mathf.PerlinNoise(f * 1.3f, 3.7f) - 0.5f) * 2f * maxOffsetMetres * Intensity;
            ASHCameraKick.SetShake(rot, pos);
            foreach (var p in ASHQuakeProp.All) if (p) p.Drive(Mathf.Clamp01(Intensity), dt);
            _rumble.volume = rumbleVolume * Sfx() * Mathf.Clamp01(Intensity * 1.2f); _rumble.pitch = 0.85f + 0.2f * Intensity;
            if (audioBank && Random.value < clattersPerSecond * Intensity * dt)
            {
                Clatters++;
                bool creak = Random.value < 0.5f;
                ASHSfx.Play(creak ? "quakeCreak" : "quakeRattle", creak ? audioBank.creaks : audioBank.rattles, Area(), (creak ? 0.6f : 0.45f) * Sfx() * Mathf.Clamp01(0.4f + Intensity), creak ? 0.8f : 1f, 0.12f, 1f, 25f);
            }
            if (radio) radio.Interrupt(Intensity);
            if (_t >= duration) { Done = true; _t = -1f; Intensity = 0f; ASHCameraKick.SetShake(Vector3.zero, Vector3.zero); _rumble.Stop(); foreach (var p in ASHQuakeProp.All) if (p && p.kind != ASHQuakeProp.Kind.Swing) p.Drive(0f, dt); _tail = 12f; }
        }

        Vector3 Area()
        {
            if (!soundArea) return transform.position;
            var b = soundArea.bounds; return new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y + 0.5f, b.max.y), Random.Range(b.min.z, b.max.z));
        }

        public void PlayFall(Vector3 at) { if (audioBank) ASHSfx.Play("quakeFall", audioBank.falls, at, 0.7f * Sfx(), 1f, 0.1f, 1f, 25f); }
    }
}
