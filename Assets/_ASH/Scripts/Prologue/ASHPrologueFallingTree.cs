using System.Collections;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>The pre-placed tree that falls onto the parked car at the quake peak. This object is the pivot (at the trunk base);
    /// it tips over towards Fall Target. Chopping is off on it (story prop). The fallen pose is saved by the prologue director.</summary>
    public sealed class ASHPrologueFallingTree : MonoBehaviour
    {
        [Tooltip("Where the tree lands (the parking zone).")] public Transform fallTarget;
        [Tooltip("Tree height (m), measured by the setup menu from the renderers.")] [Range(2f, 60f)] public float height = 12f;
        [Tooltip("Extra tilt past the computed resting angle, degrees.")] [Range(-20f, 20f)] public float extraTilt = 0f;
        public AudioClip fallClip, impactClip;
        Quaternion _upright; bool _haveUpright;
        public bool Fallen { get; private set; }
        public bool Falling { get; private set; }

        void Awake() { _upright = transform.rotation; _haveUpright = true; foreach (var c in GetComponentsInChildren<ASH.Interaction.ASHChoppable>(true)) c.enabled = false; }

        /// <summary>Resting rotation that lays the trunk over a target of the given top height (metres above the base).</summary>
        public Quaternion FallenRotation(float targetTopHeight)
        {
            if (!_haveUpright) { _upright = transform.rotation; _haveUpright = true; }
            var to = fallTarget ? fallTarget.position - transform.position : transform.forward * 8f;
            var flat = new Vector3(to.x, 0f, to.z); float d = Mathf.Max(0.5f, flat.magnitude);
            float h = Mathf.Max(0.3f, targetTopHeight - (fallTarget ? 0f : 0f) - 0.0f);
            float ang = Mathf.Clamp(Mathf.Atan2(d, h) * Mathf.Rad2Deg + extraTilt, 30f, 92f);
            var axis = Vector3.Cross(Vector3.up, flat.normalized);
            return Quaternion.AngleAxis(ang, axis) * _upright;
        }

        public IEnumerator Fall(float targetTopHeight, float seconds, System.Action onImpact)
        {
            if (Fallen || Falling) yield break;
            Falling = true;
            var cols = GetComponentsInChildren<Collider>(true); foreach (var c in cols) c.enabled = false;
            var from = transform.rotation; var to = FallenRotation(targetTopHeight);
            if (fallClip) AudioSource.PlayClipAtPoint(fallClip, transform.position + Vector3.up * 3f, 1f);
            seconds = Mathf.Max(0.2f, seconds);
            for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
            {
                float k = t * t * t; // gravity-like: slow tip, fast end
                transform.rotation = Quaternion.SlerpUnclamped(from, to, k);
                yield return null;
            }
            transform.rotation = to;
            if (impactClip) AudioSource.PlayClipAtPoint(impactClip, fallTarget ? fallTarget.position : transform.position, 1f);
            Falling = false; Fallen = true;
            foreach (var c in cols) if (c) c.enabled = true;
            onImpact?.Invoke();
        }

        public void SetFallen(Quaternion rot) { Fallen = true; Falling = false; transform.rotation = rot; }
        public void ResetUpright() { if (_haveUpright) transform.rotation = _upright; Fallen = false; Falling = false; }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.4f, 0.2f, 0.8f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * height);
            if (fallTarget) Gizmos.DrawLine(transform.position, fallTarget.position);
        }
    }
}
