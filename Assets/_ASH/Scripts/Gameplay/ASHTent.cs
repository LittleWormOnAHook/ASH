using System.Collections.Generic;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Survival;
using ASH.UI;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Pitched tent: leaky shelter (not quake-safe), pack-up with hold-E, one-per-type and campfire-distance rules for the placer.
    /// A lit campfire within the settings radius grants the fire recovery bonus while you stand inside.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHTent : MonoBehaviour, IASHUsable
    {
        public ASHTentSettings.Kind kind = ASHTentSettings.Kind.Packed;
        [Tooltip("Item returned when packed up (empty = packedtent / builttent from the registry).")]
        public ASHItemDefinition packItem;

        ASHShelterZone _zone;
        Collider _volume;

        static readonly List<ASHTent> _all = new List<ASHTent>();
        public static IReadOnlyList<ASHTent> All => _all;

        ASHTentSettings S => ASHTentSettings.I;

        public Transform UseTransform => transform;

        void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
            ApplyZone();
        }

        void OnDisable() => _all.Remove(this);

        void LateUpdate() => ApplyZone();

        void ApplyZone()
        {
            if (!_zone) _zone = GetComponent<ASHShelterZone>() ?? GetComponentInChildren<ASHShelterZone>();
            if (!_zone)
            {
                _zone = gameObject.AddComponent<ASHShelterZone>();
                _zone.volume = Volume();
            }
            _zone.structural = false;
            _zone.sealing = S.Sealing(kind);
            if (string.IsNullOrEmpty(_zone.label)) _zone.label = kind == ASHTentSettings.Kind.Built ? S.builtLabel : S.packedLabel;
        }

        Collider Volume()
        {
            if (_volume) return _volume;
            if (_zone && _zone.volume) return _volume = _zone.volume;
            _volume = GetComponent<Collider>();
            if (!_volume) _volume = GetComponentInChildren<Collider>();
            return _volume;
        }

        public ASHItemDefinition PackItem()
        {
            if (packItem) return packItem;
            var id = kind == ASHTentSettings.Kind.Built ? "builttent" : "packedtent";
            return ASHItemRegistry.I ? ASHItemRegistry.I.Find(id) : null;
        }

        public static int CountOf(ASHTentSettings.Kind kind)
        {
            int n = 0;
            for (int i = 0; i < _all.Count; i++)
            {
                var t = _all[i];
                if (t && t.isActiveAndEnabled && t.kind == kind && t.gameObject.name != "ASH_PlacementGhost") n++;
            }
            return n;
        }

        public static bool Inside(Vector3 feet, out ASHTent tent)
        {
            tent = null;
            ASHShelterZone.SealingAt(feet, out var zone);
            if (!zone) return false;
            tent = zone.GetComponent<ASHTent>() ?? zone.GetComponentInParent<ASHTent>();
            return tent;
        }

        /// <summary>1 = no bonus, otherwise the fire recovery multiplier while inside a tent with a lit campfire in range.</summary>
        public static float RecoveryBonusAt(Vector3 feet)
        {
            if (!Inside(feet, out _)) return 1f;
            var s = ASHTentSettings.I;
            return ASHCampfire.NearestLit(feet, s.fireBonusRadius) ? s.fireRecoveryMultiplier : 1f;
        }

        /// <summary>True when this placement should be rejected (sets <paramref name="reason"/>).</summary>
        public static bool TryRejectPlacement(ASHItemDefinition item, Vector3 point, out string reason)
        {
            reason = null;
            if (!item) return false;
            var s = ASHTentSettings.I;
            bool placingTent = IsTentItem(item, out var kind);
            bool placingFire = item.itemId == "campfire";
            if (!placingTent && !placingFire) return false;

            if (placingTent && s.onePerType && CountOf(kind) > 0)
            {
                reason = s.alreadyPitchedMessage;
                return true;
            }

            if (placingTent)
            {
                var fire = ClosestCampfire(point, out float fireDist);
                if (fire && fireDist < s.minCampfireDistance)
                {
                    reason = s.tooCloseFireMessage;
                    return true;
                }
            }

            if (placingFire)
            {
                var tent = ClosestTent(point, out float tentDist);
                if (tent && tentDist < s.minCampfireDistance)
                {
                    reason = s.tooCloseTentMessage;
                    return true;
                }
            }
            return false;
        }

        public static bool IsTentItem(ASHItemDefinition item, out ASHTentSettings.Kind kind)
        {
            kind = ASHTentSettings.Kind.Packed;
            if (!item) return false;
            if (item.itemId == "builttent") { kind = ASHTentSettings.Kind.Built; return true; }
            return item.itemId == "packedtent";
        }

        static ASHCampfire ClosestCampfire(Vector3 p, out float dist)
        {
            ASHCampfire best = null; dist = float.PositiveInfinity;
            var list = ASHCampfire.All;
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                if (!f || !f.isActiveAndEnabled || f.gameObject.name == "ASH_PlacementGhost") continue;
                float d = Vector3.Distance(p, f.transform.position);
                if (d < dist) { dist = d; best = f; }
            }
            return best;
        }

        static ASHTent ClosestTent(Vector3 p, out float dist)
        {
            ASHTent best = null; dist = float.PositiveInfinity;
            for (int i = 0; i < _all.Count; i++)
            {
                var t = _all[i];
                if (!t || !t.isActiveAndEnabled || t.gameObject.name == "ASH_PlacementGhost") continue;
                float d = DistanceToVolume(t, p);
                if (d < dist) { dist = d; best = t; }
            }
            return best;
        }

        static float DistanceToVolume(ASHTent t, Vector3 p)
        {
            var c = t.Volume();
            if (!c) return Vector3.Distance(p, t.transform.position);
            var closest = c.ClosestPoint(p);
            return Vector3.Distance(p, closest);
        }

        public float GetUsePriority(in ASHUseContext c) => 25f;

        public float GetHoldDuration(in ASHUseContext c) => S.packHoldSeconds;

        public string GetPrompt(in ASHUseContext c)
        {
            var it = PackItem();
            if (c.Inventory && it && !c.Inventory.HasSpaceFor(it, 1)) return S.noSpaceMessage;
            return "[Hold E] " + S.PackPrompt(kind);
        }

        public bool TryUse(in ASHUseContext c)
        {
            var it = PackItem();
            if (!it || c.Inventory == null) return false;
            if (!c.Inventory.HasSpaceFor(it, 1))
            {
                var hud = Object.FindAnyObjectByType<ASHHud>();
                if (hud && !string.IsNullOrEmpty(S.noSpaceMessage)) hud.Toast(S.noSpaceMessage);
                return false;
            }
            if (c.Inventory.AddItem(it, 1) <= 0) return false;
            Destroy(gameObject);
            return true;
        }
    }
}
