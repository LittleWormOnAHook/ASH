using ASH.Interaction;
using ASH.Inventory;
using ASH.Survival;
using ASH.UI;
using ASH.Wildlife;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Placed snare or game trap: timed catch roll plus live animals walking in.
    /// Prey becomes a carcass for knife harvest. A trapped wolf stays alive and hostile and may break free.
    /// Pack up with hold-E when empty. State is saved by ASHPlacedSave.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHSnare : MonoBehaviour, IASHUsable
    {
        [Tooltip("Snare catches rabbits and raccoons. Large catches fawns and wolves.")]
        public ASHTrapSize trapSize = ASHTrapSize.Snare;
        public string itemId = "rabbitsnare";

        public bool HasCatch { get; private set; }
        public float WaitedHours { get; private set; }
        public string CaughtKind { get; private set; }
        public ASHAnimal LiveCatch { get; private set; }

        Transform _catchVis;
        ASHItemDefinition _item;
        ASHAnimal _carcass;
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
            if (LiveCatch)
            {
                if (LiveCatch.Harvested) { LiveCatch = null; _carcass = null; SetCatch(false); return; }
                if (LiveCatch.IsDead) { _carcass = LiveCatch; ApplyVis(); return; }
                if (!LiveCatch.IsTrapped)
                {
                    Toast(S.wolfBrokeFreeToast);
                    LiveCatch = null;
                    SetCatch(false);
                }
                return;
            }
            if (_carcass)
            {
                if (_carcass.Harvested) { _carcass = null; SetCatch(false); }
                return;
            }
            if (HasCatch) return;
            TryProximityCatch();
            if (HasCatch) return;
            if (S.requireOutdoors && IsIndoors(transform.position)) return;
            WaitedHours += ASHGameTime.DeltaHours;
            if (WaitedHours < _nextCheck) return;
            if (Random.value <= Mathf.Clamp01(S.catchChance)) SimulateCatch();
            else _nextCheck = WaitedHours + Mathf.Max(0.1f, S.retryHours);
        }

        void TryProximityCatch()
        {
            float r = Mathf.Max(0.4f, S.proximityRadius);
            var hits = Physics.OverlapSphere(transform.position, r, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                var a = hits[i] ? hits[i].GetComponentInParent<ASHAnimal>() : null;
                if (a && a.CanEnterTrap(trapSize)) { CatchLive(a); return; }
            }
        }

        void CatchLive(ASHAnimal a)
        {
            if (!a) return;
            a.EnterTrap(gameObject, trapSize);
            LiveCatch = a;
            CaughtKind = a.kind.ToString();
            HasCatch = true;
            _nextCheck = float.PositiveInfinity;
            if (a.kind == ASHWildlifeKind.Wolf && !a.IsDead) Toast(S.wolfCaughtToast);
            else Toast(S.caughtToast);
            if (a.IsDead) _carcass = a;
            ApplyVis();
        }

        void SimulateCatch()
        {
            var kind = RollKind();
            if (kind == ASHWildlifeKind.Wolf)
            {
                var a = SpawnAnimal(kind, false);
                if (a) { CatchLive(a); return; }
            }
            var body = SpawnAnimal(kind, true);
            if (body)
            {
                _carcass = body;
                CaughtKind = kind.ToString();
                SetCatch(true);
                return;
            }
            CaughtKind = kind.ToString();
            SetCatch(true);
        }

        ASHWildlifeKind RollKind()
        {
            var w = ASHWildlifeSettings.I;
            if (trapSize == ASHTrapSize.Large)
            {
                float f = w ? w.largeTrapFawnChance : 0.6f;
                float wolf = w ? w.largeTrapWolfChance : 0.4f;
                float t = f + wolf;
                if (t <= 0.01f) return ASHWildlifeKind.Fawn;
                return Random.value * t <= f ? ASHWildlifeKind.Fawn : ASHWildlifeKind.Wolf;
            }
            float r = w ? w.snareRabbitChance : 0.7f;
            float c = w ? w.snareRaccoonChance : 0.3f;
            float s = r + c;
            if (s <= 0.01f) return ASHWildlifeKind.Rabbit;
            return Random.value * s <= r ? ASHWildlifeKind.Rabbit : ASHWildlifeKind.Raccoon;
        }

        ASHAnimal SpawnAnimal(ASHWildlifeKind kind, bool dead)
        {
            var prefab = ASHWildlifeSettings.I ? ASHWildlifeSettings.I.Prefab(kind) : null;
            if (!prefab) return null;
            var go = Instantiate(prefab, transform.position + Vector3.up * 0.05f + transform.forward * 0.3f, transform.rotation);
            go.name = prefab.name;
            var a = go.GetComponent<ASHAnimal>() ?? go.AddComponent<ASHAnimal>();
            a.kind = kind;
            a.size = kind == ASHWildlifeKind.Fawn ? ASHWildlifeSize.Young : ASHWildlifeSize.Adult;
            if (dead) a.Die();
            return a;
        }

        public void RestoreState(bool caught, float waited, string kind = null)
        {
            HasCatch = caught;
            WaitedHours = Mathf.Max(0f, waited);
            CaughtKind = kind;
            if (HasCatch)
            {
                _nextCheck = float.PositiveInfinity;
                if (!string.IsNullOrEmpty(kind) && System.Enum.TryParse(kind, out ASHWildlifeKind k))
                {
                    if (k == ASHWildlifeKind.Wolf)
                    {
                        var a = SpawnAnimal(k, false);
                        if (a) CatchLive(a);
                    }
                    else _carcass = SpawnAnimal(k, true);
                }
            }
            else _nextCheck = WaitedHours < S.firstCheckHours ? S.firstCheckHours : WaitedHours + Mathf.Max(0.1f, S.retryHours);
            ApplyVis();
        }

        void SetCatch(bool on)
        {
            HasCatch = on;
            if (on)
            {
                _nextCheck = float.PositiveInfinity;
                if (LiveCatch == null) Toast(S.caughtToast);
            }
            else
            {
                WaitedHours = 0f;
                _nextCheck = S.firstCheckHours;
                CaughtKind = null;
                _carcass = null;
                LiveCatch = null;
            }
            ApplyVis();
        }

        void ApplyVis() { if (_catchVis) _catchVis.gameObject.SetActive(HasCatch && !_carcass && !LiveCatch); }

        static bool IsIndoors(Vector3 p)
        {
            if (ASHShelterZone.StructuralAt(p)) return true;
            var ex = ASHExposure.Instance;
            if (ex && ex.Sealing >= 0.99f) return true;
            return false;
        }

        public static bool TryRejectPlacement(ASHItemDefinition item, Vector3 point, out string reason)
        {
            reason = null;
            if (!item || (item.itemId != "rabbitsnare" && item.itemId != "largetrap")) return false;
            if (ASHSnareSettings.I.requireOutdoors && IsIndoors(point))
            {
                reason = ASHSnareSettings.I.indoorMessage;
                return true;
            }
            return false;
        }

        ASHItemDefinition Item() => _item ? _item : (_item = ASHItemRegistry.I ? ASHItemRegistry.I.Find(string.IsNullOrEmpty(itemId) ? "rabbitsnare" : itemId) : null);
        ASHItemDefinition Rabbit() => ASHItemRegistry.I ? ASHItemRegistry.I.Find("deadrabbit") : null;

        public float GetUsePriority(in ASHUseContext c) => 24f;

        public float GetHoldDuration(in ASHUseContext c)
        {
            if (LiveCatch && !LiveCatch.IsDead) return 0f;
            if (_carcass && !_carcass.Harvested) return 0f;
            if (HasCatch) return S.collectHoldSeconds;
            return S.packHoldSeconds;
        }

        public string GetPrompt(in ASHUseContext c)
        {
            if (LiveCatch && !LiveCatch.IsDead) return S.wolfTrappedPrompt;
            if (_carcass && !_carcass.Harvested) return S.harvestPrompt;
            var want = HasCatch ? Rabbit() : Item();
            if (c.Inventory && want && !c.Inventory.HasSpaceFor(want, 1)) return S.noSpaceMessage;
            if (HasCatch) return "[Hold E] " + S.collectPrompt;
            return "[Hold E] " + S.packPrompt;
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (c.Inventory == null) return false;
            if (LiveCatch && !LiveCatch.IsDead) return false;
            if (_carcass && !_carcass.Harvested) return false;
            if (HasCatch)
            {
                var r = Rabbit();
                if (!r) { SetCatch(false); return true; }
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
