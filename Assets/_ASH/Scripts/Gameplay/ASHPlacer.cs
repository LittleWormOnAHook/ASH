using System;
using ASH.Core;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Ghost placement for placeable items (campfire). Lite version of Genesis' build placement: camera-centre
    /// ground ray, slope + overlap validation, green/red ghost, scroll to rotate, Primary to place, Secondary to
    /// cancel. No snapping, sockets or structural rules (out of scope).
    /// </summary>
    public sealed class ASHPlacer : MonoBehaviour
    {
        [SerializeField] private ASHInput input;
        [SerializeField] private ASHInventory inventory;
        [SerializeField] private ASHInteractor interactor;
        [SerializeField] private Camera viewCamera;
        [Range(1f, 15f)] [SerializeField] private float maxDistance = 6f;
        [Range(0.5f, 5f)] [SerializeField] private float minDistance = 1.2f;
        [SerializeField] private LayerMask groundMask = 1; // Default (terrain)
        [SerializeField] private LayerMask blockMask = ~0;
        [Range(1f, 90f)] [SerializeField] private float rotateStepDegrees = 15f;
        public Color validColor = new Color(0.35f, 1f, 0.45f, 0.35f);
        public Color invalidColor = new Color(1f, 0.3f, 0.25f, 0.35f);

        public bool IsPlacing => _item != null;
        public bool IsValid { get; private set; }
        /// <summary>Frame on which placement was cancelled (lets the HUD ignore the same Esc press).</summary>
        public int LastCancelFrame { get; private set; } = -1;
        public string InvalidReason { get; private set; }
        public ASHItemDefinition PlacingItem => _item;
        public event Action<ASHItemDefinition, GameObject> PlacedObject;

        private ASHItemDefinition _item;
        private GameObject _ghost;
        private ASHTent _ghostTent;
        private Material _ghostMat;
        private float _yaw;
        private Vector3 _pos;
        private float _armTime;

        // Test hook: force the aim point (world position) instead of the camera ray.
        public Vector3? SimAimPoint;

        public bool Begin(ASHItemDefinition item)
        {
            if (item == null || !item.IsPlaceable || inventory == null || inventory.Count(item) <= 0) return false;
            Cancel();
            _item = item;
            _ghost = Instantiate(item.placeablePrefab);
            _ghost.name = "ASH_PlacementGhost";
            _ghostTent = _ghost.GetComponent<ASHTent>();
            foreach (var c in _ghost.GetComponentsInChildren<Collider>()) c.enabled = false;
            foreach (var b in _ghost.GetComponentsInChildren<MonoBehaviour>()) b.enabled = false;
            foreach (var p in _ghost.GetComponentsInChildren<ParticleSystem>()) p.gameObject.SetActive(false);
            foreach (var l in _ghost.GetComponentsInChildren<Light>()) l.enabled = false;
            _ghostMat = CreateGhostMaterial();
            foreach (var r in _ghost.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMat;
                r.sharedMaterials = mats; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _yaw = viewCamera ? viewCamera.transform.eulerAngles.y : 0f;
            _armTime = Time.unscaledTime + 0.25f; // ignore the click that started placement
            if (interactor) interactor.Suppressed = true;
            if (input) input.SuppressZoom = true;
            ASHLog.Info("Placement started: " + item.displayName);
            return true;
        }

        public void Cancel()
        {
            if (_item != null) LastCancelFrame = Time.frameCount;
            if (_ghost) Destroy(_ghost);
            _ghost = null; _item = null;
            if (interactor) interactor.Suppressed = false;
            if (input) input.SuppressZoom = false;
        }

        private void Update()
        {
            if (!IsPlacing) return;
            if (viewCamera == null) viewCamera = Camera.main;
            if (ASHUIState.BlocksGameplay) { _ghost.SetActive(false); return; }
            _ghost.SetActive(true);

            if (input.SecondaryPressed || input.PausePressed) { Cancel(); return; }
            float scroll = input.RawZoom;
            if (Mathf.Abs(scroll) > 0.01f) _yaw += Mathf.Sign(scroll) * rotateStepDegrees;

            Evaluate();
            _ghost.transform.SetPositionAndRotation(_pos, Quaternion.Euler(0f, _yaw, 0f));
            if (_ghostTent) ASHTent.SettleOnGround(_ghost.transform); // t565: preview the settled tent pose (the placed tent settles the same way)
            _ghostMat.color = IsValid ? validColor : invalidColor;
            if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", IsValid ? validColor : invalidColor);

            bool confirm = input.PrimaryPressed | input.InteractPressed;
            if (confirm && Time.unscaledTime >= _armTime) Confirm();
        }

        private void Evaluate()
        {
            IsValid = false; InvalidReason = "Aim at the ground";
            Vector3 player = transform.position;
            Vector3 aim;
            Vector3 normal = Vector3.up;
            if (SimAimPoint.HasValue)
            {
                aim = SimAimPoint.Value;
                if (Physics.Raycast(aim + Vector3.up * 50f, Vector3.down, out var hh, 200f, groundMask, QueryTriggerInteraction.Ignore)) { aim = hh.point; normal = hh.normal; }
            }
            else
            {
                Ray ray = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                float camToPlayer = Vector3.Distance(ray.origin, player);
                if (!Physics.Raycast(ray, out var hit, camToPlayer + maxDistance + 2f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    // fallback: project in front of the player
                    Vector3 f = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
                    Vector3 probe = player + f * (maxDistance * 0.6f) + Vector3.up * 10f;
                    if (!Physics.Raycast(probe, Vector3.down, out hit, 40f, groundMask, QueryTriggerInteraction.Ignore)) return;
                }
                aim = hit.point; normal = hit.normal;
            }
            Vector3 flat = aim - player; flat.y = 0f;
            float d = flat.magnitude;
            if (d > maxDistance) { aim = player + flat / d * maxDistance; d = maxDistance; Reground(ref aim, ref normal); }
            if (d < minDistance) { Vector3 dir = d > 0.01f ? flat / d : transform.forward; aim = player + dir * minDistance; Reground(ref aim, ref normal); }
            _pos = aim;

            float slope = Vector3.Angle(normal, Vector3.up);
            if (slope > _item.maxPlacementSlope) { InvalidReason = "Too steep"; return; }
            Vector3 half = _item.placementFootprint * 0.5f;
            var hits = Physics.OverlapBox(aim + Vector3.up * (half.y + 0.15f), half, Quaternion.Euler(0f, _yaw, 0f), blockMask, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h is TerrainCollider) continue;
                if (h.transform.IsChildOf(transform)) { InvalidReason = "Too close to you"; return; }
                InvalidReason = "Blocked"; return;
            }
            if (ASHTent.TryRejectPlacement(_item, aim, out var why)) { InvalidReason = why; return; }
            if (ASHSnare.TryRejectPlacement(_item, aim, out var whySnare)) { InvalidReason = whySnare; return; }
            IsValid = true; InvalidReason = null;
        }

        private void Reground(ref Vector3 p, ref Vector3 n)
        {
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var h, 80f, groundMask, QueryTriggerInteraction.Ignore)) { p = h.point; n = h.normal; }
        }

        public bool Confirm()
        {
            if (!IsPlacing) return false;
            Evaluate();
            if (!IsValid) { ASHLog.Info("Placement invalid: " + InvalidReason); return false; }
            var item = _item;
            if (!inventory.Remove(item, 1)) { Cancel(); return false; }
            var go = Instantiate(item.placeablePrefab, _pos, Quaternion.Euler(0f, _yaw, 0f));
            go.name = item.placeablePrefab.name;
            go.AddComponent<ASHPlacedObject>().itemId = item.itemId; // saved by ASHPlacedSave
            Cancel();
            ASHLog.Info($"Placed {item.displayName} at {go.transform.position}");
            PlacedObject?.Invoke(item, go);
            return true;
        }

        private static Material CreateGhostMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = "ASH_GhostMat" };
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); }
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        private void OnDisable() => Cancel();
    }
}
