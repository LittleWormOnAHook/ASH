using System.Collections.Generic;
using UnityEngine;

namespace ASH.Opening
{
    /// <summary>
    /// A loose cabin prop driven by ASHEarthquakeEffects (no Update of its own): Wobble (rattles in place), Swing (hanging
    /// light / picture on a nail: pendulum about its pivot), Fall (stays kinematic until the shaking passes Fall Threshold,
    /// then drops with a small push - only a few props should use this). ResetAll puts everything back (replay).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHQuakeProp : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHQuakeProp()
        {
            All.Clear();
        }

        public enum Kind { Wobble, Swing, Fall }
        public static readonly List<ASHQuakeProp> All = new List<ASHQuakeProp>();
        public Kind kind = Kind.Wobble;
        [Range(0f, 30f)] public float maxAngle = 4f;
        [Range(0f, 0.1f)] public float maxOffset = 0.01f;
        [Range(0.1f, 20f)] public float frequency = 6f;
        [Range(0f, 1f)] public float fallThreshold = 0.6f;
        [Range(0f, 5f)] public float fallPush = 0.6f;

        Vector3 _p0; Quaternion _r0; float _seed; Vector2 _swing, _swingV; bool _fallen; Rigidbody _rb;
        public bool Fallen => _fallen;

        void Awake() { _p0 = transform.localPosition; _r0 = transform.localRotation; _seed = Random.Range(0f, 100f); _rb = GetComponent<Rigidbody>(); }
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Drive(float k, float dt)
        {
            float t = Time.time * frequency + _seed;
            switch (kind)
            {
                case Kind.Wobble:
                    if (k <= 0.001f) { transform.localPosition = _p0; transform.localRotation = _r0; return; }
                    var e = new Vector3(Mathf.PerlinNoise(t, 1f) - 0.5f, Mathf.PerlinNoise(t, 2f) - 0.5f, Mathf.PerlinNoise(t, 3f) - 0.5f) * 2f * maxAngle * k;
                    var o = new Vector3(Mathf.PerlinNoise(t, 4f) - 0.5f, 0f, Mathf.PerlinNoise(t, 5f) - 0.5f) * 2f * maxOffset * k;
                    transform.localPosition = _p0 + o; transform.localRotation = _r0 * Quaternion.Euler(e);
                    break;
                case Kind.Swing:
                    // damped pendulum pushed by the ground motion
                    var push = new Vector2(Mathf.PerlinNoise(t * 0.5f, 6f) - 0.5f, Mathf.PerlinNoise(t * 0.5f, 7f) - 0.5f) * 2f * k * maxAngle * 30f;
                    _swingV += (push - _swing * 9f - _swingV * 1.2f) * dt; _swing += _swingV * dt;
                    _swing = Vector2.ClampMagnitude(_swing, maxAngle * 3f);
                    transform.localRotation = _r0 * Quaternion.Euler(_swing.x, 0f, _swing.y);
                    break;
                case Kind.Fall:
                    if (!_fallen && k >= fallThreshold && _rb)
                    {
                        _fallen = true; _rb.isKinematic = false; _rb.WakeUp();
                        _rb.AddForce((Random.insideUnitSphere + Vector3.up * 0.3f) * fallPush, ForceMode.VelocityChange);
                        _rb.AddTorque(Random.insideUnitSphere * fallPush * 2f, ForceMode.VelocityChange);
                        var bank = FindAnyObjectByType<ASHEarthquakeEffects>(); if (bank) bank.PlayFall(transform.position);
                    }
                    else if (!_fallen && k > 0.001f)
                    {
                        var w = new Vector3(Mathf.PerlinNoise(t, 1f) - 0.5f, 0f, Mathf.PerlinNoise(t, 3f) - 0.5f) * 2f * maxAngle * k;
                        transform.localRotation = _r0 * Quaternion.Euler(w);
                    }
                    break;
            }
        }

        public void ResetPose()
        {
            if (_rb) { if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; } _rb.isKinematic = kind == Kind.Fall || _rb.isKinematic; }
            transform.localPosition = _p0; transform.localRotation = _r0; _fallen = false; _swing = _swingV = Vector2.zero;
        }
        public static void ResetAll() { foreach (var p in All) if (p) p.ResetPose(); }
    }
}
