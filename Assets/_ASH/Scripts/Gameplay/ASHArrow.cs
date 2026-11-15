using System.Collections.Generic;
using ASH.Interaction;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Physical arrow: ballistic flight (gravity x gravityScale, linear drag), faces along its velocity, swept raycast each
    /// frame, sticks into what it hits (parents to moving/rigidbody targets, nudges rigidbodies) and can be picked back up.
    /// No damage or combat.
    /// </summary>
    public sealed class ASHArrow : MonoBehaviour
    {
        public Vector3 Velocity;
        public float gravityScale = 1f, drag = 0.02f, stuckLifeSeconds = 120f, maxFlightSeconds = 12f, embedDepth = 0.12f, tipLength = 0.35f;
        public bool Stuck { get; private set; }
        public Collider StuckIn { get; private set; }
        public float FlightTime { get; private set; }
        public float MaxHeight { get; private set; }
        public Vector3 LaunchPosition { get; private set; }
        public Vector3 LaunchVelocity { get; private set; }
        public bool Recoverable { get; private set; }
        public static readonly List<ASHArrow> Live = new List<ASHArrow>();
        public static ASHArrow Last;

        Transform _ignore; ASHItemDefinition _item; bool _recover; float _recoverChance; float _stuckAt; int _maxStuck = 30;
        static readonly RaycastHit[] _hits = new RaycastHit[16];

        public void Launch(Vector3 velocity, Transform ignoreRoot, float gScale, float linearDrag, float stuckLife, ASHItemDefinition item, bool recover, float recoverChance, int maxStuck)
        {
            Velocity = velocity; _ignore = ignoreRoot; gravityScale = gScale; drag = linearDrag; stuckLifeSeconds = stuckLife;
            _item = item; _recover = recover; _recoverChance = recoverChance; _maxStuck = Mathf.Max(1, maxStuck);
            LaunchPosition = transform.position; LaunchVelocity = velocity; MaxHeight = transform.position.y;
            if (velocity.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(velocity);
            foreach (var c in GetComponentsInChildren<Collider>(true)) Destroy(c);
            Live.Add(this); Last = this;
        }

        void OnDestroy() { Live.Remove(this); }

        void Update()
        {
            float dt = Time.deltaTime; if (dt <= 0f) return;
            if (Stuck) { if (stuckLifeSeconds > 0f && Time.time - _stuckAt > stuckLifeSeconds) Destroy(gameObject); return; }
            FlightTime += dt; if (FlightTime > maxFlightSeconds) { Destroy(gameObject); return; }
            Velocity += Physics.gravity * (gravityScale * dt);
            Velocity *= Mathf.Clamp01(1f - drag * dt);
            Vector3 p = transform.position, step = Velocity * dt; float len = step.magnitude;
            if (len > 1e-5f)
            {
                var dir = step / len;
                int n = Physics.RaycastNonAlloc(p, dir, _hits, len + tipLength, ~0, QueryTriggerInteraction.Ignore);
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var h = _hits[i]; if (!h.collider || (_ignore && h.collider.transform.IsChildOf(_ignore))) continue;
                    if (h.distance < bd) { bd = h.distance; best = i; }
                }
                if (best >= 0) { Stick(_hits[best], dir); return; }
                transform.position = p + step;
                transform.rotation = Quaternion.LookRotation(dir);
                MaxHeight = Mathf.Max(MaxHeight, transform.position.y);
            }
        }

        void Stick(RaycastHit h, Vector3 dir)
        {
            Stuck = true; StuckIn = h.collider; _stuckAt = Time.time;
            transform.rotation = Quaternion.LookRotation(dir);
            transform.position = h.point - dir * (tipLength - embedDepth);
            var rb = h.collider.attachedRigidbody;
            if (rb && !rb.isKinematic) rb.AddForceAtPosition(Velocity * 0.02f, h.point, ForceMode.Impulse);
            if (rb || !h.collider.gameObject.isStatic) transform.SetParent(h.collider.transform, true);
            Velocity = Vector3.zero;
            if (_recover && _item && Random.value <= _recoverChance)
            {
                int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) gameObject.layer = layer;
                var box = gameObject.AddComponent<BoxCollider>(); box.isTrigger = true; box.center = new Vector3(0, 0, 0.1f); box.size = new Vector3(0.12f, 0.12f, 0.8f);
                gameObject.AddComponent<ASHItemPickup>().Configure(_item, 1);
                Recoverable = true;
            }
            // cap stuck arrows (oldest first)
            int stuck = 0; for (int i = Live.Count - 1; i >= 0; i--) if (Live[i] && Live[i].Stuck && ++stuck > _maxStuck) Destroy(Live[i].gameObject);
        }
    }
}
