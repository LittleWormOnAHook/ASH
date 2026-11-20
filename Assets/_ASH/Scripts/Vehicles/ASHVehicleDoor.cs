using System.Collections;
using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>Hinged door on its model pivot (t357): eased swing open / closed about hingeAxisLocal, with sounds. The door's
    /// colliders are off while it is not closed so the swing never shoves the player.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHVehicleDoor : MonoBehaviour
    {
        [Tooltip("Hinge axis in this pivot's local space (vertical for car doors, set by Setup Vehicles).")]
        public Vector3 hingeAxisLocal = Vector3.up;
        [Tooltip("Open angle in degrees; the sign picks the outward direction (set by Setup Vehicles).")]
        [Range(-120f, 120f)] public float openAngle = 65f;
        [Tooltip("Seconds to swing fully open / closed.")] [Range(0.1f, 3f)] public float swingSeconds = 0.45f;
        [Range(0f, 1f)] public float volume = 0.8f;
        public AudioClip openClip, closeClip;
        public Collider[] doorColliders;
        Quaternion _closed; bool _init; float _t; Coroutine _run; AudioSource _src;
        public float Open01 => _t;
        public bool IsOpen => _t > 0.98f;
        public bool IsClosed => _t < 0.001f;

        void Init() { if (_init) return; _init = true; _closed = transform.localRotation; if (doorColliders == null || doorColliders.Length == 0) doorColliders = GetComponentsInChildren<Collider>(true); }
        void Awake() => Init();

        public IEnumerator OpenRoutine() { Init(); Play(openClip); yield return Swing(1f); }
        public IEnumerator CloseRoutine() { Init(); yield return Swing(0f); Play(closeClip); }
        public void Open() { Init(); if (_run != null) StopCoroutine(_run); _run = StartCoroutine(OpenRoutine()); }
        public void Close() { Init(); if (_run != null) StopCoroutine(_run); _run = StartCoroutine(CloseRoutine()); }

        IEnumerator Swing(float to)
        {
            SetColliders(false);
            while (!Mathf.Approximately(_t, to))
            {
                _t = Mathf.MoveTowards(_t, to, Time.deltaTime / Mathf.Max(0.05f, swingSeconds));
                Apply(); yield return null;
            }
            Apply();
            if (to <= 0f) SetColliders(true);
        }
        void Apply()
        {
            float e = _t * _t * (3f - 2f * _t);
            transform.localRotation = _closed * Quaternion.AngleAxis(openAngle * e, hingeAxisLocal.sqrMagnitude > 1e-6f ? hingeAxisLocal.normalized : Vector3.up);
        }
        void SetColliders(bool on) { if (doorColliders != null) foreach (var c in doorColliders) if (c) c.enabled = on; }
        void Play(AudioClip c)
        {
            if (!c) return;
            if (!_src) { _src = gameObject.AddComponent<AudioSource>(); _src.spatialBlend = 1f; _src.minDistance = 1.5f; _src.maxDistance = 25f; _src.playOnAwake = false; }
            _src.pitch = Random.Range(0.95f, 1.05f); _src.PlayOneShot(c, volume);
        }
    }
}
