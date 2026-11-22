using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ASH.Companion;
using UnityEngine;

namespace ASH.Wildlife.Birds
{
    /// <summary>
    /// Sky tier, vendor roosts and swarms. The Bird Flocks vendor scripts (FlockController / FlockChild / LandingSpotController) are
    /// driven by reflection, so ASH still compiles on machines without the (untracked) vendor pack. Every vendor sky bird gets an
    /// ASHSkyBirdProxy hit sphere: a shot one is swapped for a falling ASH bird of the matching type, so it can be harvested.
    /// </summary>
    public sealed partial class ASHBirdManager
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHBirdManager_Sky()
        {
            _fRoamers.Clear();
            _fAmount.Clear();
            _fPosBuf.Clear();
            _fSpeed.Clear();
            _fLandFlock.Clear();
            _fLandOnStart.Clear();
            _fSpawner.Clear();
            _fLanding.Clear();
            _vendorWarned.Clear();
        }

        sealed class SkyFlock
        {
            public Component ctrl;
            public string typeId;
            public ASHBirdRoostSpot roost;
            public Component landing;
            public int known = -1;
            public float followAt;
        }

        readonly List<SkyFlock> _sky = new List<SkyFlock>();
        float _skySpawnAt, _swarmReadyAt, _swarmRollAt;
        Dictionary<Material, Material> _matSwap;
        int _matSwapStamp = -1;

        // ------------------------------------------------------------------ reflection on the vendor scripts (no compile-time dependency)
        static readonly Dictionary<Type, FieldInfo> _fRoamers = new Dictionary<Type, FieldInfo>(), _fAmount = new Dictionary<Type, FieldInfo>(),
            _fPosBuf = new Dictionary<Type, FieldInfo>(), _fSpeed = new Dictionary<Type, FieldInfo>(), _fLandFlock = new Dictionary<Type, FieldInfo>(),
            _fLandOnStart = new Dictionary<Type, FieldInfo>(), _fSpawner = new Dictionary<Type, FieldInfo>(), _fLanding = new Dictionary<Type, FieldInfo>();
        const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static FieldInfo F(Dictionary<Type, FieldInfo> cache, Type t, string name)
        {
            if (t == null) return null;
            if (!cache.TryGetValue(t, out var f)) cache[t] = f = t.GetField(name, BF);
            return f;
        }
        static object Get(Component c, Dictionary<Type, FieldInfo> cache, string name) { var f = c ? F(cache, c.GetType(), name) : null; return f != null ? f.GetValue(c) : null; }
        static void Set(Component c, Dictionary<Type, FieldInfo> cache, string name, object v)
        {
            var f = c ? F(cache, c.GetType(), name) : null;
            if (f == null) return;
            try { f.SetValue(c, Convert.ChangeType(v, f.FieldType)); } catch { try { f.SetValue(c, v); } catch { } }
        }
        static readonly HashSet<string> _vendorWarned = new HashSet<string>();
        static void Call(Component c, string method, params object[] args)
        {
            if (!c) return;
            foreach (var m in c.GetType().GetMethods(BF))
                if (m.Name == method && m.GetParameters().Length == args.Length) { try { m.Invoke(c, args); } catch (Exception e) { if (_vendorWarned.Add(method)) Debug.LogWarning("[ASH] Birds: vendor " + method + " failed (shown once): " + (e.InnerException ?? e).Message); } return; }
        }
        static IList Roamers(Component ctrl) => Get(ctrl, _fRoamers, "_roamers") as IList;

        // ------------------------------------------------------------------ sky tier
        void SkyTick(ASHBirdSettings s)
        {
            if (!_player) return;
            bool on = s.skyBirdsEnabled && s.skyFlockPrefabs != null && s.skyFlockPrefabs.Length > 0;
            if (on && Time.time >= _skySpawnAt)
            {
                _skySpawnAt = Time.time + s.skySpawnInterval;
                int free = 0; foreach (var k in _sky) if (!k.roost) free++;
                if (free < s.skyMaxFlocks) SpawnSkyFlock(s);
            }
            for (int i = _sky.Count - 1; i >= 0; i--)
            {
                var k = _sky[i];
                if (!k.ctrl) { if (k.landing) Destroy(k.landing.gameObject); _sky.RemoveAt(i); continue; }
                var ct = k.ctrl.transform;
                if (!k.roost)
                {
                    if (!on || Flat(ct.position, _player.position) > s.skyDespawnDistance) { DestroySky(k); _sky.RemoveAt(i); continue; }
                    if (Time.time >= k.followAt)
                    {
                        // drift the flock centre over the player's general area, keep the height band
                        k.followAt = Time.time + s.skyFollowSeconds;
                        var p = ct.position;
                        if (Flat(p, _player.position) > s.skySpawnRadiusMax * 0.8f) p = Vector3.Lerp(p, _player.position, 0.35f);
                        p += Random3(20f);
                        p.y = GroundY(p, s) + Rng(s.skyHeightMin, Mathf.Max(s.skyHeightMin, s.skyHeightMax));
                        ct.position = p;
                    }
                }
                var roam = Roamers(k.ctrl);
                if (roam != null && roam.Count != k.known)
                {
                    k.known = roam.Count;
                    foreach (var o in roam) if (o is Component c && c) EnsureProxy(c, k, s);
                }
            }
        }

        void SpawnSkyFlock(ASHBirdSettings s)
        {
            int n = s.skyFlockPrefabs.Length;
            int idx = _rng.Next(n);
            GameObject pf = null;
            for (int tries = 0; tries < n && !pf; tries++) { pf = s.skyFlockPrefabs[(idx + tries) % n]; if (pf) idx = (idx + tries) % n; }
            if (!pf) return;
            float ang = R01() * Mathf.PI * 2f;
            var p = _player.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Rng(s.skySpawnRadiusMin, Mathf.Max(s.skySpawnRadiusMin, s.skySpawnRadiusMax));
            p.y = GroundY(p, s) + Rng(s.skyHeightMin, Mathf.Max(s.skyHeightMin, s.skyHeightMax));
            var go = Instantiate(pf, p, Quaternion.identity);
            go.name = "ASH_SkyFlock_" + pf.name;
            var ctrl = go.GetComponent("FlockController");
            if (!ctrl) { Destroy(go); return; }
            // before the vendor Start(): bird count and a cheap update divisor
            int count = _rng.Next(Mathf.Max(1, s.skyBirdsMin), Mathf.Max(s.skyBirdsMin, s.skyBirdsMax) + 1);
            Set(ctrl, _fAmount, "_childAmount", count);
            string tid = s.skyFlockTypeIds != null && idx < s.skyFlockTypeIds.Length && !string.IsNullOrEmpty(s.skyFlockTypeIds[idx]) ? s.skyFlockTypeIds[idx] : "crow";
            _sky.Add(new SkyFlock { ctrl = ctrl, typeId = tid, followAt = Time.time + s.skyFollowSeconds });
        }

        void EnsureProxy(Component child, SkyFlock k, ASHBirdSettings s)
        {
            var go = child.gameObject;
            var px = go.GetComponent<ASHSkyBirdProxy>();
            if (px) return;
            px = go.AddComponent<ASHSkyBirdProxy>();
            px.mgr = this; px.typeId = k.typeId; px.ctrl = k.ctrl;
            int layer = LayerMask.NameToLayer("Enemy");
            if (layer >= 0) go.layer = layer;
            var col = go.GetComponent<SphereCollider>();
            if (!col) col = go.AddComponent<SphereCollider>();
            float sc = Mathf.Max(0.05f, go.transform.lossyScale.x);
            col.radius = s.skyBirdHitRadius / sc;
            col.enabled = s.skyKillable;
            var rb = go.GetComponent<Rigidbody>();
            if (!rb) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            SwapVendorMaterials(go, s);
            // vendor audio sources follow the game's SFX volume
            foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) { a.spatialBlend = 1f; a.maxDistance = Mathf.Max(a.maxDistance, s.audioMaxDistance); a.volume = Mathf.Clamp01(a.volume) * ASH.Core.ASHGameSettings.CachedSfx; }
        }

        void SwapVendorMaterials(GameObject go, ASHBirdSettings s)
        {
            if (s.vendorMaterialFrom == null || s.vendorMaterialTo == null) return;
            int stamp = s.vendorMaterialFrom.Length * 31 + s.vendorMaterialTo.Length;
            if (_matSwap == null || stamp != _matSwapStamp)
            {
                _matSwapStamp = stamp;
                _matSwap = new Dictionary<Material, Material>();
                for (int i = 0; i < Mathf.Min(s.vendorMaterialFrom.Length, s.vendorMaterialTo.Length); i++)
                    if (s.vendorMaterialFrom[i] && s.vendorMaterialTo[i]) _matSwap[s.vendorMaterialFrom[i]] = s.vendorMaterialTo[i];
            }
            if (_matSwap.Count == 0) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool dirty = false;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && _matSwap.TryGetValue(mats[i], out var to)) { mats[i] = to; dirty = true; }
                if (dirty) r.sharedMaterials = mats;
            }
        }

        /// <summary>A vendor sky bird was hit: remove it from its vendor flock and drop a real ASH bird of the matching type in its place.</summary>
        internal void KillSky(ASHSkyBirdProxy px, Vector3 push)
        {
            var b = ReplaceSkyBird(px);
            if (b) Kill(b, push);
        }

        ASHBird ReplaceSkyBird(ASHSkyBirdProxy px)
        {
            var s = S;
            if (!px || !s) return null;
            var t = s.Find(px.typeId) ?? s.Find("crow");
            var tr = px.transform;
            var pos = tr.position; var fwd = tr.forward;
            float spd = Get(px.GetComponent("FlockChild"), _fSpeed, "_speed") is float f ? f : (t != null ? t.flySpeed : 8f);
            RemoveVendorChild(px);
            if (t == null) return null;
            var b = Get(s, t.prefab);
            if (!b) return null;
            b.Apply(t, _rng, 0f);
            b.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd.sqrMagnitude > 0.01f ? fwd : Vector3.forward));
            b.vel = fwd * spd;
            b.state = ASHBirdState.Fly;
            b.stateAt = b.launchAt = Time.time;
            return b;
        }

        void RemoveVendorChild(ASHSkyBirdProxy px)
        {
            var ctrl = px.ctrl;
            var child = px.GetComponent("FlockChild");
            if (ctrl && child)
            {
                var roam = Roamers(ctrl);
                if (roam != null && roam.Contains(child)) roam.Remove(child);
                if (Get(ctrl, _fAmount, "_childAmount") is int n) Set(ctrl, _fAmount, "_childAmount", Mathf.Max(0, n - 1));
                foreach (var k in _sky) if (k.ctrl == ctrl) k.known = roam != null ? roam.Count : -1;
            }
            // free any vendor landing spot holding it
            if (px.ctrl) foreach (var k in _sky) if (k.ctrl == px.ctrl && k.landing) Call(k.landing, "ScareAll", 0f, 0.3f);
            Destroy(px.gameObject);
        }

        void SkyGunshot(ASHBirdSettings s, Vector3 at, float range)
        {
            foreach (var k in _sky)
            {
                if (!k.ctrl) continue;
                var ct = k.ctrl.transform;
                if (k.landing && k.roost && Flat(k.roost.transform.position, at) <= range) { Call(k.landing, "ScareAll", 0f, 0.6f); k.roost.scaredAt = Time.time; }
                if (k.roost || s.skyScatterMeters <= 0f || (ct.position - at).magnitude > Mathf.Max(range, s.skyHeightMax * 1.5f)) continue;
                var away = ct.position - at; away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Random3(1f).normalized;
                var p = ct.position + away * s.skyScatterMeters;
                p.y = GroundY(p, s) + s.skyHeightMax;
                ct.position = p;
                Call(k.ctrl, "SetFlockRandomPosition");
                var roam = Roamers(k.ctrl);
                if (roam != null) foreach (var o in roam) if (o is Component c && c) Call(c, "Wander", R01() * 0.4f);
            }
        }

        void DestroySky(SkyFlock k)
        {
            if (k.ctrl)
            {
                Call(k.ctrl, "destroyBirds");
                Destroy(k.ctrl.gameObject);
            }
            if (k.landing) Destroy(k.landing.gameObject);
            if (k.roost) { k.roost.flock = null; k.roost.landing = null; }
        }

        void DespawnSky()
        {
            foreach (var k in _sky) DestroySky(k);
            _sky.Clear();
        }

        // ------------------------------------------------------------------ vendor roosts: Landing Spot Controller on real perches
        void RoostTick(ASHBirdSettings s)
        {
            if (!_player) return;
            foreach (var r in ASHBirdRoostSpot.All)
            {
                if (!r || !r.isActiveAndEnabled) continue;
                float d = Flat(r.transform.position, _player.position);
                SkyFlock k = null;
                foreach (var x in _sky) if (x.roost == r) { k = x; break; }
                if (k == null)
                {
                    if (d <= r.activateDistance) SpawnRoost(s, r);
                    continue;
                }
                if (d > r.activateDistance * 1.3f) { DestroySky(k); _sky.Remove(k); return; }
                // player close: lift them off (vendor ScareAll); the vendor auto-catch lands them again later
                var br = ASH.Combat.ASHCombatBridge.Player;
                float sd = r.scareDistance * (br && br.Crouching ? 0.6f : (br && br.Sprinting ? 1.4f : 1f));
                if (d < sd && Time.time - r.scaredAt > 4f && k.landing) { r.scaredAt = Time.time; Call(k.landing, "ScareAll", 0f, 0.8f); }
            }
        }

        void SpawnRoost(ASHBirdSettings s, ASHBirdRoostSpot r)
        {
            bool crow = r.style == ASHBirdRoostSpot.Style.Crow;
            var fp = crow ? s.roostCrowFlock : s.roostGullFlock;
            var lp = crow ? s.roostCrowLanding : s.roostGullLanding;
            if (!fp || !lp) return;
            var pos = r.transform.position;
            var fgo = Instantiate(fp, pos + Vector3.up * r.flockHeight, Quaternion.identity);
            fgo.name = "ASH_RoostFlock_" + r.name;
            var ctrl = fgo.GetComponent("FlockController");
            if (!ctrl) { Destroy(fgo); return; }
            int count = _rng.Next(Mathf.Max(1, r.birdsMin), Mathf.Max(r.birdsMin, r.birdsMax) + 1);
            Set(ctrl, _fAmount, "_childAmount", count);
            var lgo = Instantiate(lp, pos, Quaternion.identity);
            lgo.name = "ASH_RoostSpots_" + r.name;
            var land = lgo.GetComponent("LandingSpotController");
            if (land)
            {
                // assign the flock before the vendor Start() so it never has to search the scene for one
                var lf = F(_fLandFlock, land.GetType(), "_flock");
                if (lf != null) lf.SetValue(land, ctrl);
                Set(land, _fLandOnStart, "_landOnStart", true);
                // move the vendor landing spots onto real perches around the roost (terrain, top edges of colliders)
                int placed = 0;
                var spots = new List<Transform>();
                foreach (Transform c in lgo.transform) if (c.GetComponent("LandingSpot")) spots.Add(c);
                foreach (var c in spots)
                {
                    if (placed >= count) { c.gameObject.SetActive(false); continue; }
                    if (FindPerch(pos, r.radius, s.Find(crow ? "crow" : "gull"), s, out var p, out var fwd, out _))
                    {
                        c.SetPositionAndRotation(p, Quaternion.LookRotation(fwd.sqrMagnitude > 0.01f ? fwd : Vector3.forward));
                        placed++;
                    }
                    else c.gameObject.SetActive(false);
                }
            }
            var k = new SkyFlock { ctrl = ctrl, typeId = crow ? "crow" : "gull", roost = r, landing = land, followAt = float.MaxValue };
            r.flock = ctrl; r.landing = land; r.flockGo = fgo; r.landingGo = lgo;
            _sky.Add(k);
        }

        // ------------------------------------------------------------------ swarms: rare whole-flock attacks
        void SwarmTick(ASHBirdSettings s)
        {
            if (!s.swarmEnabled || !_player || Time.time < _swarmRollAt) return;
            _swarmRollAt = Time.time + 1f;
            if (Time.time < _swarmReadyAt) return;
            if (R01() < s.swarmChancePerMinute / 60f) TryStartSwarm(s, _player.position);
        }

        bool TryStartSwarm(ASHBirdSettings s, Vector3 near)
        {
            if (!s.swarmEnabled || !_player || Time.time < _swarmReadyAt) return false;
            Flock best = null;
            if (R01() >= s.swarmFromSkyChance)
                best = PickSwarmFlock(s, near);
            if (best == null) best = SkyToSwarm(s, near);
            if (best == null) best = PickSwarmFlock(s, near);
            if (best == null) return false;
            _swarmReadyAt = Time.time + s.swarmCooldown;
            best.swarm = true; best.swarmKills = 0;
            int n = 0;
            foreach (var b in best.m.ToArray())
            {
                if (n >= s.swarmMaxBirds) break;
                if (!b || b.IsDead || b.state == ASHBirdState.Attack) continue;
                StartAttack(b, s, true);
                n++;
            }
            var t = best.type;
            if (t != null && t.calls != null && t.calls.Length > 0) PlayAt(t.alarmCalls != null && t.alarmCalls.Length > 0 ? t.alarmCalls : t.calls, Centroid(best), t.callVolume * 1.1f, t.callPitch);
            return n > 0;
        }

        Flock PickSwarmFlock(ASHBirdSettings s, Vector3 near)
        {
            Flock best = null; float bd = float.MaxValue;
            foreach (var f in _flocks)
            {
                if (f.placed || f.swarm || f.type == null || !f.type.canSwarm || f.m.Count < s.swarmMinBirds) continue;
                float d = Flat(Centroid(f), _player.position);
                if (d > s.swarmRange) continue;
                d += Flat(Centroid(f), near) * 0.5f;
                if (d < bd) { bd = d; best = f; }
            }
            return best;
        }

        /// <summary>Peels birds out of the nearest swarm-capable vendor sky flock into a new ASH flock that can attack.</summary>
        Flock SkyToSwarm(ASHBirdSettings s, Vector3 near)
        {
            SkyFlock pick = null; float bd = float.MaxValue;
            foreach (var k in _sky)
            {
                if (!k.ctrl || k.roost) continue;
                var t = s.Find(k.typeId);
                if (t == null || !t.canSwarm) continue;
                float d = Flat(k.ctrl.transform.position, _player.position);
                if (d > s.swarmRange * 1.5f) continue;
                var roam = Roamers(k.ctrl);
                if (roam == null || roam.Count < s.swarmMinBirds) continue;
                if (d < bd) { bd = d; pick = k; }
            }
            if (pick == null) return null;
            var type = s.Find(pick.typeId);
            var list = new List<ASHSkyBirdProxy>();
            foreach (var o in Roamers(pick.ctrl)) if (o is Component c && c) { var px = c.GetComponent<ASHSkyBirdProxy>(); if (px) list.Add(px); }
            int want = Mathf.Min(list.Count, _rng.Next(s.swarmMinBirds, Mathf.Max(s.swarmMinBirds, s.swarmMaxBirds) + 1));
            if (want < 2) return null;
            var f = new Flock { type = type, home = pick.ctrl.transform.position, callAt = Time.time + 3f };
            for (int i = 0; i < want; i++)
            {
                var b = ReplaceSkyBird(list[i]);
                if (!b) continue;
                b.flock = f; f.m.Add(b);
            }
            if (f.m.Count == 0) return null;
            f.target = f.home; f.flyUntil = Time.time + 30f;
            _flocks.Add(f);
            return f;
        }
    }

    /// <summary>Hit sphere on a vendor (Bird Flocks) sky bird: any hit kills it (swapped for a falling, harvestable ASH bird).</summary>
    [DisallowMultipleComponent]
    public sealed class ASHSkyBirdProxy : MonoBehaviour, Invector.vIDamageReceiver
    {
        internal ASHBirdManager mgr;
        internal Component ctrl;
        internal string typeId = "crow";
        readonly Invector.OnReceiveDamage _onStart = new Invector.OnReceiveDamage();
        readonly Invector.OnReceiveDamage _onRecv = new Invector.OnReceiveDamage();
        public Invector.OnReceiveDamage onStartReceiveDamage => _onStart;
        public Invector.OnReceiveDamage onReceiveDamage => _onRecv;
        bool _dead;

        public void TakeDamage(Invector.vDamage damage)
        {
            if (_dead || damage == null || !mgr) return;
            _dead = true;
            _onStart.Invoke(damage);
            Vector3 push = transform.position - damage.hitPosition;
            if (damage.hitPosition.sqrMagnitude < 0.0001f || push.sqrMagnitude < 0.0001f) push = damage.sender ? transform.position - damage.sender.position : -transform.forward;
            _onRecv.Invoke(damage);
            mgr.KillSky(this, push.normalized);
        }
    }
}
