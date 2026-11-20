using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>Driving cameras (t356): third-person orbit/follow and first-person in the cab at the driver's head. Tab toggles.
    /// Runs late so it wins over everything else that touches the main camera while driving.</summary>
    [DefaultExecutionOrder(5000)]
    public sealed class ASHVehicleCamera : MonoBehaviour
    {
        public enum View { ThirdPerson, FirstPerson }
        public View view = View.ThirdPerson;
        [Header("Third person")]
        [Tooltip("Legacy single distance (unused while zoomSteps has entries).")] [Range(2f, 20f)] public float distance = 6.5f;
        [Tooltip("t410: mouse-wheel zoom steps, metres (close, mid, far). Wheel up = closer.")] public float[] zoomSteps = { 5f, 8f, 12f };
        [Tooltip("Step used the first time you drive this session (0 = close).")] [Range(0, 2)] public int startStep = 1;
        [Tooltip("How fast the camera eases to a new zoom step.")] [Range(0.5f, 20f)] public float zoomEase = 5f;
        [Range(0f, 6f)] public float height = 1.6f;
        [Tooltip("Look-at point above the vehicle origin.")] [Range(0f, 4f)] public float lookHeight = 1.2f;
        [Range(-30f, 80f)] public float minPitch = -10f;
        [Range(-30f, 80f)] public float maxPitch = 60f;
        [Range(0.5f, 30f)] public float followSharpness = 8f;
        [Tooltip("Seconds without mouse input before the camera swings back behind the vehicle (moving only).")] [Range(0f, 10f)] public float recenterDelay = 1.5f;
        [Range(0f, 10f)] public float recenterSpeed = 2.5f;
        [Range(40f, 100f)] public float thirdFov = 60f;
        [Header("First person")]
        [Tooltip("t410: first-person cab view is off for now (Tab does nothing). Tick to bring it back.")] public bool enable1stPerson = false;
        [Range(40f, 100f)] public float firstFov = 65f;
        [Range(10f, 170f)] public float yawLimit = 120f;
        [Range(-89f, 0f)] public float firstMinPitch = -50f;
        [Range(0f, 89f)] public float firstMaxPitch = 45f;
        [Header("Mouse")]
        [Range(0.1f, 20f)] public float sensitivity = 3f;
        public Transform head;
        static int s_zoomStep = -1; float _dist;
        public int ZoomStep => s_zoomStep; public float CurrentDistance => _dist;
        float StepDist => zoomSteps != null && zoomSteps.Length > 0 ? zoomSteps[Mathf.Clamp(s_zoomStep, 0, zoomSteps.Length - 1)] : distance;
        Camera _cam; float _yaw, _pitch = 12f, _fyaw, _fpitch, _idle, _savedFov; bool _active; Vector3 _vel;
        ASH.Player.ASHInput _input; Rigidbody _rb;
        public bool Active => _active;

        public void Begin(Camera cam)
        {
            _cam = cam; _active = cam; if (!_active) return;
            _savedFov = cam.fieldOfView; _yaw = 0f; _pitch = 12f; _fyaw = 0f; _fpitch = 0f;
            _input = FindAnyObjectByType<ASH.Player.ASHInput>(); _rb = GetComponent<Rigidbody>();
            if (s_zoomStep < 0) s_zoomStep = startStep; // remembered for the session after that
            if (!enable1stPerson) view = View.ThirdPerson;
            _dist = StepDist;
            Snap();
        }
        public void End() { if (_cam) _cam.fieldOfView = _savedFov; _active = false; _cam = null; }
        public void ToggleView() { if (!enable1stPerson) { view = View.ThirdPerson; return; } view = view == View.ThirdPerson ? View.FirstPerson : View.ThirdPerson; Snap(); }

        void Snap() { if (!_active) return; Place(1f); }

        void LateUpdate() { if (_active && _cam) Place(Time.deltaTime); }

        void Place(float dt)
        {
            var look = _input ? _input.Look * sensitivity : Vector2.zero;
            var t = _cam.transform;
            if (!enable1stPerson) view = View.ThirdPerson;
            if (view == View.FirstPerson && head)
            {
                _fyaw = Mathf.Clamp(_fyaw + look.x, -yawLimit, yawLimit); _fpitch = Mathf.Clamp(_fpitch - look.y, firstMinPitch, firstMaxPitch);
                _cam.fieldOfView = firstFov;
                t.SetPositionAndRotation(head.position, transform.rotation * Quaternion.Euler(_fpitch, _fyaw, 0f));
                return;
            }
            _cam.fieldOfView = thirdFov;
            float z = _input ? _input.Zoom : 0f; int n = zoomSteps != null ? zoomSteps.Length : 0;
            if (n > 0 && Mathf.Abs(z) > 1e-5f) s_zoomStep = Mathf.Clamp(s_zoomStep + (z > 0f ? -1 : 1), 0, n - 1);
            _dist = dt >= 1f ? StepDist : Mathf.Lerp(_dist, StepDist, 1f - Mathf.Exp(-zoomEase * dt));
            if (look.sqrMagnitude > 1e-4f) _idle = 0f; else _idle += dt;
            _yaw += look.x; _pitch = Mathf.Clamp(_pitch - look.y, minPitch, maxPitch);
            float speed = _rb ? _rb.linearVelocity.magnitude : 0f;
            if (_idle > recenterDelay && speed > 2f) _yaw = Mathf.LerpAngle(_yaw, 0f, 1f - Mathf.Exp(-recenterSpeed * dt));
            var target = transform.position + Vector3.up * lookHeight;
            var rot = Quaternion.Euler(_pitch, transform.eulerAngles.y + _yaw, 0f);
            var want = target + rot * new Vector3(0f, 0f, -_dist) + Vector3.up * (height - lookHeight) * 0.5f;
            var dir = want - target; float len = dir.magnitude;
            if (len > 0.01f)
                foreach (var h in Physics.RaycastAll(target, dir / len, len, ~0, QueryTriggerInteraction.Ignore))
                    if (!h.collider.transform.IsChildOf(transform) && h.distance < len && !(h.collider.attachedRigidbody && h.collider.attachedRigidbody.CompareTag("Player")))
                    { len = Mathf.Max(0.5f, h.distance - 0.25f); want = target + dir.normalized * len; }
            t.position = dt >= 1f ? want : Vector3.Lerp(t.position, want, 1f - Mathf.Exp(-followSharpness * dt));
            t.rotation = Quaternion.LookRotation(target - t.position, Vector3.up);
        }
    }
}
