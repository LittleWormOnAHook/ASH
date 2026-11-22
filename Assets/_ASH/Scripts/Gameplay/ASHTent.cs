using System.Collections.Generic;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Survival;
using ASH.UI;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>
    /// Pitched tent: leaky shelter (not quake-safe), one-per-type and campfire-distance rules for the placer.
    /// A lit campfire within the settings radius grants the fire recovery bonus while you stand inside.
    /// t565: E opens the tent menu (Pack up / Rest-Sleep, ASHTentMenu); player-placed tents settle onto the ground under their footprint.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHTent : MonoBehaviour, IASHUsable
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHTent()
        {
            _all.Clear();
        }

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

        void Start()
        {
            // only tents the player placed (ASHPlacer / save restore add ASHPlacedObject); hand-placed scene tents are left exactly where they are
            // (unless Settle Scene Tents is on). t568: settle now and again after short delays (the ash layer depth / streamed colliders
            // may not be ready on the first frame).
            if (gameObject.name == "ASH_PlacementGhost") return;
            if (GetComponent<ASHPlacedObject>() || S.settleSceneTents) StartCoroutine(SettleRoutine());
        }

        System.Collections.IEnumerator SettleRoutine()
        {
            SettleOnGround(transform, S.logSettle);
            float t0 = Time.time;
            var delays = S.resettleDelays;
            if (delays == null) yield break;
            for (int i = 0; i < delays.Length; i++)
            {
                while (Time.time - t0 < delays[i]) yield return null;
                SettleOnGround(transform, S.logSettle && i == delays.Length - 1);
            }
        }

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

        public float GetHoldDuration(in ASHUseContext c) => 0f; // t565: a tap opens the tent menu (the HUD adds the [E])

        public string GetPrompt(in ASHUseContext c) => S.MenuPrompt(kind);

        public bool TryUse(in ASHUseContext c) => ASHTentMenu.Open(this, c.Inventory, c.Stats, c.PlayerTransform);

        /// <summary>Takes the tent down into the pack. False (with a toast) when there is no room.</summary>
        public bool PackUp(ASHInventory inv)
        {
            var it = PackItem();
            if (!it || inv == null) return false;
            if (!inv.HasSpaceFor(it, 1))
            {
                var hud = Object.FindAnyObjectByType<ASHHud>();
                if (hud && !string.IsNullOrEmpty(S.noSpaceMessage)) hud.Toast(S.noSpaceMessage);
                return false;
            }
            if (inv.AddItem(it, 1) <= 0) return false;
            Destroy(gameObject);
            return true;
        }

        // ------------------------------------------------------------------ t565/t568 ground settle
        static readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>Settles a tent (or its placement ghost) onto the ground under its footprint. t568: the base is the bottom of the
        /// tent's BoxCollider (the authored footprint, ropes / stray mesh bits ignored), the ground is sampled on a grid (terrain or static
        /// colliders) and raised by the visible ash / snow layer depth, a plane is fitted (tilt capped at maxTilt) and the tent drops until no
        /// sample floats more than floatTolerance (bounded by maxExtraSink). Keeps x/z and heading. False (untouched) when no ground is found.</summary>
        public static bool SettleOnGround(Transform root) => SettleOnGround(root, false);

        public static bool SettleOnGround(Transform root, bool log)
        {
            var s = ASHTentSettings.I;
            if (!root || !s.snapToGround) return false;
            Vector3 fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            float yaw = fwd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(fwd).eulerAngles.y : root.eulerAngles.y;
            var upright = Quaternion.Euler(0f, yaw, 0f);
            var startPos = root.position;
            root.rotation = upright;
            if (!FootprintBounds(root, out var lb, out bool fromCollider)) { if (log) Debug.Log("[ASH] Tent settle: no collider / mesh bounds on " + root.name); return false; }
            if (!Application.isPlaying) Physics.SyncTransforms(); // edit-mode menu: colliders moved by Undo / tools

            int g = Mathf.Clamp(s.footprintSamples, 2, 8);
            float ix = lb.size.x * s.footprintInset, iz = lb.size.z * s.footprintInset;
            float x0 = lb.min.x + ix, x1 = lb.max.x - ix, z0 = lb.min.z + iz, z1 = lb.max.z - iz, y = lb.min.y;
            int total = g * g + 1;
            if (_local == null || _local.Length < total) { _local = new Vector3[total]; _hit = new Vector3[total]; _ok = new bool[total]; }
            int k = 0;
            for (int a = 0; a < g; a++)
                for (int b = 0; b < g; b++)
                    _local[k++] = new Vector3(Mathf.Lerp(x0, x1, a / (float)(g - 1)), y, Mathf.Lerp(z0, z1, b / (float)(g - 1)));
            _local[k++] = new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f);

            var acc = Application.isPlaying && s.sitOnAshLayer ? ASH.World.Accumulation.ASHAccumulationController.Instance : null;
            if (acc && !acc.isActiveAndEnabled) acc = null;
            Vector3 sc = root.lossyScale;
            float up = lb.size.y * Mathf.Max(0.01f, sc.y) + 2f;
            int n = 0; float maxAsh = 0f;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < total; i++)
            {
                _ok[i] = false;
                var w = root.position + upright * Vector3.Scale(_local[i], sc);
                if (!GroundBelow(root, new Vector3(w.x, Mathf.Max(w.y, startPos.y) + up, w.z), up + s.groundSearchDepth, out var p)) continue;
                if (acc)
                {
                    float d = acc.GetDepthMetres(p) * s.ashDepthFactor;
                    if (d > 0f) { p.y += d; maxAsh = Mathf.Max(maxAsh, d); }
                }
                _hit[i] = p; _ok[i] = true; n++; sum += p;
            }
            if (n < Mathf.Max(3, total / 3)) { if (log) Debug.Log("[ASH] Tent settle: ground not found under " + root.name + " (" + n + "/" + total + " samples)"); return false; }
            Vector3 avg = sum / n;

            // least squares plane y = A x + B z + C through the hits (relative to avg)
            double sxx = 0, sxz = 0, szz = 0, sxy = 0, szy = 0;
            for (int i = 0; i < total; i++)
            {
                if (!_ok[i]) continue;
                double dx = _hit[i].x - avg.x, dz = _hit[i].z - avg.z, dy = _hit[i].y - avg.y;
                sxx += dx * dx; sxz += dx * dz; szz += dz * dz; sxy += dx * dy; szy += dz * dy;
            }
            double det = sxx * szz - sxz * sxz;
            Vector3 normal = Vector3.up;
            if (System.Math.Abs(det) > 1e-6)
            {
                float A = (float)((sxy * szz - szy * sxz) / det), B = (float)((szy * sxx - sxy * sxz) / det);
                normal = new Vector3(-A, 1f, -B).normalized;
            }
            float ang = Vector3.Angle(Vector3.up, normal);
            if (ang > s.maxTilt) normal = ang > 0.01f ? Vector3.Slerp(Vector3.up, normal, s.maxTilt / ang).normalized : Vector3.up;
            var rot = Quaternion.FromToRotation(Vector3.up, normal) * upright;

            // footprint centre of the base onto the fitted plane through the average ground point
            Vector3 baseCentre = Vector3.Scale(_local[total - 1], sc);
            Vector3 pos = avg - rot * baseCentre - normal * s.groundSink;
            // no floating sample: drop until every sampled base point is within floatTolerance of the ground (bounded)
            float gap = 0f;
            for (int i = 0; i < total; i++)
            {
                if (!_ok[i]) continue;
                var bp = pos + rot * Vector3.Scale(_local[i], sc);
                gap = Mathf.Max(gap, bp.y - _hit[i].y - s.floatTolerance);
            }
            float drop = gap > 0f ? Mathf.Min(gap, s.maxExtraSink) : 0f;
            pos.y -= drop;
            root.SetPositionAndRotation(pos, rot);
            if (log)
                Debug.Log("[ASH] Tent settle: " + root.name + " base from " + (fromCollider ? "collider" : "mesh") + ", " + n + "/" + total + " ground hits, tilt "
                          + Vector3.Angle(Vector3.up, normal).ToString("0.0") + "deg (slope " + ang.ToString("0.0") + "), ash +" + maxAsh.ToString("0.00") + "m, extra drop "
                          + drop.ToString("0.00") + "m" + (gap > s.maxExtraSink ? " (capped, " + (gap - s.maxExtraSink).ToString("0.00") + "m still floating)" : "")
                          + ", moved " + (pos - startPos).ToString("F2"));
            return true;
        }

        static Vector3[] _local, _hit; static bool[] _ok;

        /// <summary>The footprint in root-local space: the root BoxCollider (the authored tent box, base at its bottom) when there is one,
        /// else the bounds of every active mesh (pose independent).</summary>
        static bool FootprintBounds(Transform root, out Bounds b, out bool fromCollider)
        {
            fromCollider = false;
            var box = root.GetComponent<BoxCollider>();
            if (box && ASHTentSettings.I.baseFromCollider)
            {
                b = new Bounds(box.center, box.size);
                fromCollider = true;
                return b.size.x > 0.05f && b.size.z > 0.05f;
            }
            return LocalBounds(root, out b);
        }

        /// <summary>Bounds of every active mesh under root, in root-local space (pose independent).</summary>
        static bool LocalBounds(Transform root, out Bounds b)
        {
            b = default; bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                Mesh m = null;
                if (r is MeshRenderer) { var mf = r.GetComponent<MeshFilter>(); m = mf ? mf.sharedMesh : null; }
                else if (r is SkinnedMeshRenderer smr) m = smr.sharedMesh;
                if (!m) continue;
                var mb = m.bounds; var t = r.transform;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(t.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any;
        }

        /// <summary>Nearest static ground (terrain, or a solid collider without a rigidbody) below <paramref name="from"/>, ignoring the tent
        /// itself, triggers, other tents / placed objects, characters, animals and small props (grass, pickups, debris).</summary>
        static bool GroundBelow(Transform self, Vector3 from, float dist, out Vector3 point)
        {
            point = default;
            int c = Physics.RaycastNonAlloc(from, Vector3.down, _hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            float minSize = ASHTentSettings.I.minGroundColliderSize;
            for (int i = 0; i < c; i++)
            {
                var h = _hits[i]; var col = h.collider;
                if (!col || col.transform.IsChildOf(self)) continue;
                if (!(col is TerrainCollider))
                {
                    if (col.attachedRigidbody || col is CharacterController || col.GetComponentInParent<ASHTent>() || col.GetComponentInParent<ASHPlacedObject>()) continue;
                    var bs = col.bounds.size;
                    if (Mathf.Max(bs.x, bs.z) < minSize) continue;
                }
                if (h.distance < best) { best = h.distance; point = h.point; }
            }
            return best < float.PositiveInfinity;
        }
    }
}
