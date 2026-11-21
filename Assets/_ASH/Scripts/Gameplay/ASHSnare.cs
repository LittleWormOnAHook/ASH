using ASH.Interaction;
using ASH.Inventory;
using ASH.Survival;
using ASH.UI;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Placed rabbit snare: waits in game hours, then can catch a Dead Rabbit (no live animal required).
    /// Pack up with hold-E when empty; collect with hold-E when it has a catch. State is saved by ASHPlacedSave.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHSnare : MonoBehaviour, IASHUsable
    {
        public bool HasCatch { get; private set; }
        public float WaitedHours { get; private set; }

        Transform _catchVis;
        ASHItemDefinition _item, _rabbit;
        float _nextCheck;

        ASHSnareSettings S => ASHSnareSettings.I;
        public Transform UseTransform => transform;

        void Awake()
        {
            var vis = transform.Find("CatchVis");
            if (vis) _catchVis = vis;
            else
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "CatchVis";
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.18f, 0f);
                go.transform.localScale = Vector3.one * 0.22f;
                var col = go.GetComponent<Collider>(); if (col) Destroy(col);
                var r = go.GetComponent<Renderer>();
                if (r) { r.sharedMaterial = new Material(r.sharedMaterial) { color = new Color(0.55f, 0.4f, 0.28f) }; }
                _catchVis = go.transform;
            }
        }

        void OnEnable()
        {
            if (_nextCheck <= 0f && !HasCatch) _nextCheck = S.firstCheckHours;
            ApplyVis();
        }

        void Update()
        {
            if (HasCatch) return;
            if (S.requireOutdoors && IsIndoors(transform.position)) return;
            WaitedHours += ASHGameTime.DeltaHours;
            if (WaitedHours < _nextCheck) return;
            if (Random.value <= S.catchChance) SetCatch(true);
            else _nextCheck = WaitedHours + Mathf.Max(0.1f, S.retryHours);
        }

        public void RestoreState(bool caught, float waited)
        {
            HasCatch = caught;
            WaitedHours = Mathf.Max(0f, waited);
            if (HasCatch) _nextCheck = float.PositiveInfinity;
            else _nextCheck = WaitedHours < S.firstCheckHours ? S.firstCheckHours : WaitedHours + Mathf.Max(0.1f, S.retryHours);
            ApplyVis();
        }

        void SetCatch(bool on)
        {
            HasCatch = on;
            if (on)
            {
                _nextCheck = float.PositiveInfinity;
                var hud = Object.FindAnyObjectByType<ASHHud>();
                if (hud && !string.IsNullOrEmpty(S.caughtToast)) hud.Toast(S.caughtToast);
            }
            else
            {
                WaitedHours = 0f;
                _nextCheck = S.firstCheckHours;
            }
            ApplyVis();
        }

        void ApplyVis() { if (_catchVis) _catchVis.gameObject.SetActive(HasCatch); }

        static bool IsIndoors(Vector3 p)
        {
            if (ASHShelterZone.StructuralAt(p)) return true;
            var ex = ASHExposure.Instance;
            if (ex && ex.Sealing >= 0.99f) return true;
            return false;
        }

        /// <summary>Used by the placer: snares cannot be set inside a building or the cabin.</summary>
        public static bool TryRejectPlacement(ASHItemDefinition item, Vector3 point, out string reason)
        {
            reason = null;
            if (!item || item.itemId != "rabbitsnare") return false;
            if (ASHSnareSettings.I.requireOutdoors && IsIndoors(point))
            {
                reason = ASHSnareSettings.I.indoorMessage;
                return true;
            }
            return false;
        }

        ASHItemDefinition Item() => _item ? _item : (_item = ASHItemRegistry.I ? ASHItemRegistry.I.Find("rabbitsnare") : null);
        ASHItemDefinition Rabbit() => _rabbit ? _rabbit : (_rabbit = ASHItemRegistry.I ? ASHItemRegistry.I.Find("deadrabbit") : null);

        public float GetUsePriority(in ASHUseContext c) => 24f;
        public float GetHoldDuration(in ASHUseContext c) => HasCatch ? S.collectHoldSeconds : S.packHoldSeconds;

        public string GetPrompt(in ASHUseContext c)
        {
            var want = HasCatch ? Rabbit() : Item();
            if (c.Inventory && want && !c.Inventory.HasSpaceFor(want, 1)) return S.noSpaceMessage;
            if (HasCatch) return "[Hold E] " + S.collectPrompt;
            return "[Hold E] " + S.packPrompt;
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (c.Inventory == null) return false;
            if (HasCatch)
            {
                var r = Rabbit();
                if (!r) return false;
                if (!c.Inventory.HasSpaceFor(r, 1)) { Toast(S.noSpaceMessage); return false; }
                if (c.Inventory.AddItem(r, 1) <= 0) return false;
                SetCatch(false);
                return true;
            }
            var it = Item();
            if (!it) return false;
            if (!c.Inventory.HasSpaceFor(it, 1)) { Toast(S.noSpaceMessage); return false; }
            if (c.Inventory.AddItem(it, 1) <= 0) return false;
            Destroy(gameObject);
            return true;
        }

        static void Toast(string m)
        {
            if (string.IsNullOrEmpty(m)) return;
            var hud = Object.FindAnyObjectByType<ASHHud>();
            if (hud) hud.Toast(m);
        }
    }
}
