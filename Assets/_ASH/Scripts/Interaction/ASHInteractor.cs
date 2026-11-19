using System.Collections.Generic;
using ASH.Core;
using ASH.Inventory;
using ASH.Player;
using ASH.Survival;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>
    /// Center-screen interaction. Lite port of Genesis WorldUseController: camera-centre ray with a sphere
    /// aim-assist fallback, player-distance gate (Genesis MaxPickupDistance = 3 m), use debounce, throttled
    /// prompt refresh and tap/hold use. Combat, vehicles, vendors, pets, etc. removed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHInteractor : MonoBehaviour
    {
        [SerializeField] private ASHInput input;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private ASHInventory inventory;
        [SerializeField] private ASHSurvivalStats stats;
        [Tooltip("Max distance from the player's body to the target (metres).")]
        [Range(1f, 8f)] [SerializeField] private float useRange = 3f;
        [Tooltip("Aim assist sphere radius when the precise centre ray misses.")]
        [Range(0f, 1.5f)] [SerializeField] private float aimAssistRadius = 0.45f;
        [SerializeField] private LayerMask interactMask = ~0;
        [Range(0f, 0.5f)] [SerializeField] private float useDebounce = 0.15f;
        [Range(0.02f, 0.3f)] [SerializeField] private float refreshInterval = 0.05f;

        public IASHUsable Target { get; private set; }
        public string Prompt { get; private set; }
        public float HoldProgress01 { get; private set; }
        public bool Suppressed { get; set; }
        public event System.Action<IASHUsable> Used;
        /// <summary>Automated-test override for the camera centre ray.</summary>
        public Ray? SimRay;

        private float _nextRefresh, _nextUse, _holdTime;
        private IASHUsable _holdTarget;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private Transform _playerRoot;

        private void Awake()
        {
            _playerRoot = transform;
            if (viewCamera == null) viewCamera = Camera.main;
        }

        private void Update()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null || input == null) return;

            if (Suppressed || ASHUIState.BlocksGameplay)
            {
                ClearTarget();
                return;
            }

            var ctx = BuildContext();
            if (Time.unscaledTime >= _nextRefresh || Target == null)
            {
                _nextRefresh = Time.unscaledTime + refreshInterval;
                Target = FindTarget(ctx);
                Prompt = Target != null ? Target.GetPrompt(ctx) : null;
            }

            if (Target == null) { ResetHold(); return; }

            float hold = Target.GetHoldDuration(ctx);
            if (hold <= 0f)
            {
                ResetHold();
                if (input.InteractPressed && Time.unscaledTime >= _nextUse) Use(ctx);
                return;
            }

            if (input.InteractHeld)
            {
                if (_holdTarget != Target) { _holdTarget = Target; _holdTime = 0f; }
                _holdTime += Time.deltaTime;
                HoldProgress01 = Mathf.Clamp01(_holdTime / hold);
                if (_holdTime >= hold)
                {
                    Use(ctx);
                    _holdTime = 0f; HoldProgress01 = 0f; // keep holding to gather again
                }
            }
            else ResetHold();
        }

        private void Use(in ASHUseContext ctx)
        {
            _nextUse = Time.unscaledTime + useDebounce;
            var used = Target;
            if (used.TryUse(ctx)) Used?.Invoke(used);
            _nextRefresh = 0f;
        }

        private void ResetHold() { _holdTarget = null; _holdTime = 0f; HoldProgress01 = 0f; }
        private void ClearTarget() { Target = null; Prompt = null; ResetHold(); }

        private ASHUseContext BuildContext()
        {
            Ray ray = SimRay ?? viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return new ASHUseContext(_playerRoot, viewCamera, inventory, stats, useRange, ray, null);
        }

        private IASHUsable FindTarget(in ASHUseContext ctx)
        {
            // Ray must reach past the camera boom: camera-to-player distance + use range.
            float camToPlayer = Vector3.Distance(ctx.ViewRay.origin, _playerRoot.position + Vector3.up * 1.4f);
            float maxDist = camToPlayer + useRange + 1f;

            IASHUsable best = Probe(ctx, Physics.RaycastNonAlloc(ctx.ViewRay, _hits, maxDist, interactMask, QueryTriggerInteraction.Collide));
            if (best == null && aimAssistRadius > 0f)
                best = Probe(ctx, Physics.SphereCastNonAlloc(ctx.ViewRay, aimAssistRadius, _hits, maxDist, interactMask, QueryTriggerInteraction.Collide));
            return best;
        }

        private IASHUsable Probe(in ASHUseContext ctx, int count)
        {
            System.Array.Sort(_hits, 0, count, HitDistanceComparer.Instance); // NonAlloc results are unordered
            IASHUsable best = null;
            float bestScore = float.NegativeInfinity;
            float nearestBlocker = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(_playerRoot)) continue;
                var usable = h.collider.GetComponentInParent<IASHUsable>();
                if (usable == null)
                {
                    if (!h.collider.isTrigger && h.distance > 0f) nearestBlocker = Mathf.Min(nearestBlocker, h.distance);
                    continue;
                }
                if (h.distance > nearestBlocker + 0.05f) continue;
                Vector3 closest = SafeClosest(h.collider, _playerRoot.position + Vector3.up);
                if (Vector3.Distance(closest, _playerRoot.position + Vector3.up) > useRange) continue;
                float priority = usable.GetUsePriority(ctx);
                if (priority < 0f) continue;
                float score = priority - h.distance * 0.1f;
                if (score > bestScore) { bestScore = score; best = usable; }
            }
            // Re-check blockers that were sorted after the usable (NonAlloc results are unordered).
            if (best != null && nearestBlocker < float.PositiveInfinity)
            {
                for (int i = 0; i < count; i++)
                    if (_hits[i].collider != null && _hits[i].collider.GetComponentInParent<IASHUsable>() == best && _hits[i].distance > nearestBlocker + 0.05f)
                        return null;
            }
            return best;
        }
    
        /// <summary>Collider.ClosestPoint only supports primitives and convex meshes; non-convex MeshColliders (rocks, cliffs) and terrain use bounds.</summary>
        static Vector3 SafeClosest(Collider c, Vector3 p)
        {
            if (c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex)) return c.ClosestPoint(p);
            return c.bounds.ClosestPoint(p);
        }

        private sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
