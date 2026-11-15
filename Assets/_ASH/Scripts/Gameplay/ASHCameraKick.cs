using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Cosmetic camera offsets applied after the Invector camera and removed again next frame so they never
    /// accumulate into the camera's own transform: recoil kick (decaying pitch/yaw) and an externally driven shake
    /// (rotation + position, e.g. ASHEarthquakeEffects).</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(32000)]
    public sealed class ASHCameraKick : MonoBehaviour
    {
        [Range(1f, 40f)] public float recoverSpeed = 10f;
        Vector2 _kick; bool _applied;
        // offsets are applied and removed in LOCAL space: the camera is a child of the Invector camera rig, so its world
        // pose changes whenever the rig moves even though nobody rewrote it - comparing world values let offsets bake in.
        Quaternion _baseRot, _appliedRot; Vector3 _basePos, _appliedPos;
        Vector3 _shakeRot, _shakePos;
        public float Pitch => _kick.x;
        public static Vector3 ShakeRotation { get; private set; }

        static ASHCameraKick Get()
        {
            var cam = Camera.main; if (!cam) return null;
            var k = cam.GetComponent<ASHCameraKick>(); if (!k) k = cam.gameObject.AddComponent<ASHCameraKick>();
            return k;
        }

        public static void Kick(float pitchDeg, float yawDeg)
        {
            var k = Get(); if (k) k._kick += new Vector2(pitchDeg, yawDeg);
        }

        /// <summary>Set the shake offset (degrees pitch/yaw/roll, metres in camera space), recomputed by the caller every
        /// frame from noise x intensity. Zero = none: the camera's own pose is restored immediately (no residue).</summary>
        public static void SetShake(Vector3 rotationDeg, Vector3 offsetMetres)
        {
            ShakeRotation = rotationDeg;
            var cam = Camera.main; var k = cam ? cam.GetComponent<ASHCameraKick>() : null;
            if (rotationDeg == Vector3.zero && offsetMetres == Vector3.zero)
            {
                if (k) { k._shakeRot = Vector3.zero; k._shakePos = Vector3.zero; if (k._kick == Vector2.zero) k.Undo(); }
                return;
            }
            if (!k) k = Get();
            if (k) { k._shakeRot = rotationDeg; k._shakePos = offsetMetres; }
        }

        /// <summary>Drop every offset (kick + shake) and put the camera back on its own pose.</summary>
        public static void ClearAll()
        {
            ShakeRotation = Vector3.zero;
            var cam = Camera.main; var k = cam ? cam.GetComponent<ASHCameraKick>() : null;
            if (k) { k._kick = Vector2.zero; k._shakeRot = Vector3.zero; k._shakePos = Vector3.zero; k.Undo(); }
        }

        void OnDisable() { Undo(); }

        void Undo()
        {
            if (!_applied) return;
            var t = transform;
            // restore only what nobody else rewrote since we applied it (Invector may own the camera pose directly)
            if (Quaternion.Angle(t.localRotation, _appliedRot) < 1e-3f) t.localRotation = _baseRot;
            if ((t.localPosition - _appliedPos).sqrMagnitude < 1e-10f) t.localPosition = _basePos;
            _applied = false;
        }

        void LateUpdate()
        {
            Undo();
            if (_kick.sqrMagnitude < 1e-6f) _kick = Vector2.zero;
            bool kick = _kick != Vector2.zero;
            bool shake = _shakeRot != Vector3.zero || _shakePos != Vector3.zero;
            if (!kick && !shake) return;
            var t = transform;
            _baseRot = t.localRotation; _basePos = t.localPosition;
            // pure additive offset from the base, recomputed this frame (never accumulated)
            t.localRotation = _baseRot * Quaternion.Euler(-_kick.x + _shakeRot.x, _kick.y + _shakeRot.y, _shakeRot.z);
            if (_shakePos != Vector3.zero) t.localPosition = _basePos + _baseRot * _shakePos;
            _appliedRot = t.localRotation; _appliedPos = t.localPosition; _applied = true;
            _kick = Vector2.Lerp(_kick, Vector2.zero, 1f - Mathf.Exp(-recoverSpeed * Time.deltaTime));
        }
    }
}
