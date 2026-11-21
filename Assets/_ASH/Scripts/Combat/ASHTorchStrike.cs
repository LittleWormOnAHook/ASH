using System.Collections.Generic;
using ASH.Gameplay;
using Invector;
using Invector.vMelee;
using UnityEngine;

namespace ASH.Combat
{
    /// <summary>Runtime hook on a held torch: keeps a vMeleeWeapon + hit box (without moving the held pose),
    /// pulses lit-torch presence for wolves, and applies a short burn on a lit strike.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHTorchStrike : MonoBehaviour
    {
        ASHLightSource _light;
        vMeleeWeapon _weapon;
        bool _lit;
        float _nextPulse;

        public vMeleeWeapon Weapon => _weapon;

        public static ASHTorchStrike Ensure(GameObject go)
        {
            if (!go) return null;
            var s = go.GetComponent<ASHTorchStrike>();
            if (!s) s = go.AddComponent<ASHTorchStrike>();
            s.EnsureMelee();
            s.Cache();
            return s;
        }

        void Awake() { Cache(); EnsureMelee(); }
        void OnEnable() { Cache(); SyncLit(true); }
        void OnDisable() { ASHTorchPresence.SetLit(transform, false); _lit = false; }

        void Cache()
        {
            if (!_light) _light = GetComponentInChildren<ASHLightSource>(true);
            if (!_weapon) _weapon = GetComponent<vMeleeWeapon>();
        }

        void Update()
        {
            SyncLit(false);
            if (!_lit) return;
            var s = ASHTorchSettings.Live;
            float interval = s ? s.presenceInterval : 0.5f;
            if (Time.time < _nextPulse) return;
            _nextPulse = Time.time + Mathf.Max(0.1f, interval);
            if (s) ASHTorchPresence.Scare(transform.position, s.scareRadius, s.scareSeconds);
        }

        void SyncLit(bool force)
        {
            if (!_light) _light = GetComponentInChildren<ASHLightSource>(true);
            bool lit = _light && _light.On;
            if (!force && lit == _lit) return;
            _lit = lit;
            ASHTorchPresence.SetLit(transform, _lit);
            if (_lit) _nextPulse = 0f;
        }

        public static void NotifyHit(vHitInfo hi)
        {
            if (hi == null || !hi.attackObject) return;
            var s = hi.attackObject.GetComponent<ASHTorchStrike>();
            if (!s) s = hi.attackObject.GetComponentInParent<ASHTorchStrike>();
            if (s) s.OnHit(hi);
        }

        void OnHit(vHitInfo hi)
        {
            Cache();
            SyncLit(false);
            if (!_lit) return;
            var s = ASHTorchSettings.Live;
            if (!s) return;
            var col = hi.targetCollider;
            if (col) ASHBurning.Apply(col, transform, s.burnSeconds, s.burnDps, s.burnTick);
            ASHTorchPresence.Scare(hi.hitPoint != Vector3.zero ? hi.hitPoint : transform.position, s.scareRadius, s.hitScareSeconds);
        }

        void EnsureMelee()
        {
            _weapon = GetComponent<vMeleeWeapon>();
            if (!_weapon) _weapon = gameObject.AddComponent<vMeleeWeapon>();
            _weapon.meleeType = vMeleeType.OnlyAttack;
            _weapon.useStrongAttack = false;
            var data = ASHWeaponData.For(FindTorchItem());
            var set = ASHTorchSettings.Live;
            if (data)
            {
                _weapon.distanceToAttack = data.reach;
                _weapon.staminaCost = data.staminaCost;
                _weapon.staminaRecoveryDelay = data.staminaRecoveryDelay;
                _weapon.attackID = data.attackID;
                _weapon.movesetID = data.movesetID;
            }
            else if (set)
            {
                _weapon.distanceToAttack = set.fallbackReach;
                _weapon.staminaCost = set.fallbackStamina;
            }
            if (_weapon.damage == null) _weapon.damage = new vDamage(data ? Mathf.RoundToInt(data.damage) : (set ? Mathf.RoundToInt(set.fallbackDamage) : 6));

            var hb = transform.Find("ASH_HitBox");
            if (!hb)
            {
                var go = new GameObject("ASH_HitBox");
                hb = go.transform;
                hb.SetParent(transform, false);
                go.layer = gameObject.layer;
                LocalBounds(transform, out var b);
                int ax = Longest(b.size);
                var handL = transform.InverseTransformPoint(transform.parent ? transform.parent.position : transform.position);
                Vector3 lo = b.center, hi = b.center;
                lo[ax] = b.min[ax]; hi[ax] = b.max[ax];
                bool hiIsHead = (hi - handL).sqrMagnitude >= (lo - handL).sqrMagnitude;
                var grip = hiIsHead ? lo : hi; var head = hiIsHead ? hi : lo;
                hb.localPosition = Vector3.Lerp(grip, head, data ? data.hitBoxAlong : 0.9f);
                var size = data ? data.hitBoxSize : new Vector3(0.12f, 0.12f, 0.18f);
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                var ls = transform.lossyScale;
                box.size = new Vector3(
                    size.x / Mathf.Max(0.01f, Mathf.Abs(ls.x)),
                    size.y / Mathf.Max(0.01f, Mathf.Abs(ls.y)),
                    size.z / Mathf.Max(0.01f, Mathf.Abs(ls.z)));
            }
            var hit = hb.GetComponent<vHitBox>();
            if (!hit) hit = hb.gameObject.AddComponent<vHitBox>();
            hit.attackObject = _weapon;
            hit.damagePercentage = 100;
            hit.triggerType = vHitBoxType.Damage | vHitBoxType.Recoil;
            var trig = hb.GetComponent<Collider>();
            if (trig) { trig.isTrigger = true; trig.enabled = false; }
            _weapon.hitBoxes = new List<vHitBox> { hit };
        }

        static ASH.Inventory.ASHItemDefinition FindTorchItem()
        {
            var eq = FindAnyObjectByType<ASHEquipment>();
            if (eq && eq.Off && eq.Off.itemId == "torch") return eq.Off;
            return ASH.Inventory.ASHItemRegistry.I ? ASH.Inventory.ASHItemRegistry.I.Find("torch") : null;
        }

        static void LocalBounds(Transform root, out Bounds b)
        {
            var rs = root.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            b = new Bounds(Vector3.zero, Vector3.one * 0.2f);
            foreach (var r in rs)
            {
                if (!r || r is ParticleSystemRenderer) continue;
                var c = root.InverseTransformPoint(r.bounds.center);
                var e = r.bounds.extents;
                // approximate local size from world extents
                var size = root.InverseTransformVector(new Vector3(e.x * 2f, e.y * 2f, e.z * 2f));
                size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                var nb = new Bounds(c, size);
                if (!any) { b = nb; any = true; }
                else b.Encapsulate(nb);
            }
        }

        static int Longest(Vector3 s)
        {
            if (s.y >= s.x && s.y >= s.z) return 1;
            if (s.z >= s.x && s.z >= s.y) return 2;
            return 0;
        }
    }
}
