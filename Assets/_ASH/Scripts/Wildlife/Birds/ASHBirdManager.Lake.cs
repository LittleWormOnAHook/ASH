using System.Collections.Generic;
using UnityEngine;

namespace ASH.Wildlife.Birds
{
    /// <summary>Lake ducks: small duck groups that float on named lakes (Lake Tinkiller), paddle and dabble, take off when you get close
    /// or shoot, circle the lake and settle back on open water. They keep off the shoreline (left free for fishing).</summary>
    public sealed partial class ASHBirdManager
    {
        readonly List<Flock> _lakeFlocks = new List<Flock>();
        float _lakeAt;

        void LakeTick(ASHBirdSettings s)
        {
            _lakeFlocks.RemoveAll(f => f == null || !_flocks.Contains(f));
            if (!s.lakeDucksEnabled || !_player || Time.time < _lakeAt) return;
            _lakeAt = Time.time + 2f;
            var t = s.Find(s.lakeBirdTypeId);
            if (t == null) return;
            foreach (var lake in ASHWaterBody.Find(s.lakeObjectNames, s.lakeSurfaceOffset))
            {
                if (lake == null || !lake.root) continue;
                float d = Flat(lake.Center, _player.position) - lake.Radius;
                if (d > s.lakeActivateDistance) continue;
                int have = 0;
                foreach (var f in _lakeFlocks) if (f.lake == lake) have++;
                if (have >= s.lakeFlocksPerLake) continue;
                int free = s.maxBirds - ActiveBirds;
                int n = Mathf.Min(free, _rng.Next(Mathf.Max(1, s.lakeGroupMin), Mathf.Max(s.lakeGroupMin, s.lakeGroupMax) + 1));
                if (n <= 0) return;
                if (!lake.RandomWaterPoint(out var c, s.lakeMinDepth, s.lakeShoreClear, 30, null, 0f, _mask)) continue;
                if (_player && Flat(c, _player.position) < t.takeOffDistance * 1.5f) continue;
                var f2 = SpawnFlock(s, t, c, n, false, null, 0f, 4f);
                if (f2 == null) continue;
                f2.lake = lake;
                f2.home = c;
                foreach (var b in f2.m)
                {
                    lake.RandomWaterPoint(out var wp, s.lakeMinDepth, s.lakeShoreClear, 12, c, 4f, _mask);
                    Place(b, wp, Random3(1f), true);
                    b.surface = null;
                }
                f2.landed = true; f2.landing = false;
                f2.landedUntil = Time.time + Rng(t.landedMinSeconds, Mathf.Max(t.landedMinSeconds, t.landedMaxSeconds)) * 2f;
                _lakeFlocks.Add(f2);
                return;
            }
        }

        /// <summary>Lake flocks wander over the lake (circle it) instead of the open country.</summary>
        Vector3 LakeWander(Flock f, ASHBirdSettings s)
        {
            var lake = f.lake;
            float a = Rng(0f, Mathf.PI * 2f);
            var p = lake.Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * lake.Radius * Rng(0.3f, s.lakeCircleRadiusMul);
            p.y = Mathf.Max(lake.surfaceY, GroundY(p, s)) + Rng(s.lakeFlyHeightMin, Mathf.Max(s.lakeFlyHeightMin, s.lakeFlyHeightMax));
            return p;
        }

        bool LakePerch(ASHBird b, ASHBirdSettings s)
        {
            var lake = b.flock.lake;
            Vector3 near = b.flock.m.Count > 0 ? LandedMate(b.flock) : lake.Center;
            bool mateDown = false;
            foreach (var m in b.flock.m) if (m.state == ASHBirdState.Landed) { mateDown = true; break; }
            bool ok = mateDown ? lake.RandomWaterPoint(out var p, s.lakeMinDepth, s.lakeShoreClear, 16, near, 5f, _mask)
                               : lake.RandomWaterPoint(out p, s.lakeMinDepth, s.lakeShoreClear, 24, null, 0f, _mask);
            if (!ok) return false;
            if (_player && Flat(p, _player.position) < b.type.takeOffDistance * 1.2f) return false;
            b.perch = p; b.perchFwd = Random3(1f); b.perchGround = true;
            b.state = ASHBirdState.Approach;
            b.stateAt = Time.time;
            return true;
        }

        /// <summary>Floating: slow paddle toward a nearby water point, gentle bob, dabble (eat clip) or idle.</summary>
        void SwimTick(ASHBird b, Flock f, ASHBirdSettings s, ASHBirdSettings.BirdType t, float dt)
        {
            var lake = f.lake;
            var tr = b.transform;
            if (b.walkPhase <= 0f) b.walkPhase = Rng(0.05f, 1f); // per-bird bob phase (swimming doesn't hop)
            if (Time.time >= b.nextAnimSwap)
            {
                b.nextAnimSwap = Time.time + Rng(2.5f, 7f);
                lake.RandomWaterPoint(out var wp, s.lakeMinDepth, s.lakeShoreClear, 8, b.perch, s.lakeSwimRoam, _mask);
                b.walkTo = wp;
                bool dabble = R01() < t.peckChance * 0.6f;
                b.Play(dabble ? PeckSt(b, s) : IdleSt(b, s), s.groundAnimSpeed);
            }
            var to = b.walkTo - b.perch; to.y = 0f;
            float d = to.magnitude;
            if (d > 0.05f)
            {
                var next = b.perch + to / d * Mathf.Min(d, s.lakeSwimSpeed * Mathf.Max(0.4f, t.scale) * dt);
                // keep spacing with mates
                bool blocked = false;
                foreach (var m in f.m) if (m != b && m.state == ASHBirdState.Landed && Flat(m.perch, next) < 0.5f * t.scale && Flat(m.perch, next) < Flat(m.perch, b.perch)) { blocked = true; break; }
                if (!blocked && lake.Depth(next, _mask) >= s.lakeMinDepth * 0.5f) { b.perch = next; b.targetYaw = Quaternion.LookRotation(to / d).eulerAngles.y; }
                else b.walkTo = b.perch;
            }
            float bob = Mathf.Sin((Time.time + b.walkPhase * 7.3f) * s.lakeBobRate) * s.lakeBobHeight;
            var p = b.perch; p.y = lake.surfaceY + bob - s.lakeFloatDepth * Mathf.Max(0.3f, t.scale);
            tr.position = p;
            var yaw = Mathf.MoveTowardsAngle(tr.eulerAngles.y, b.targetYaw, 70f * dt);
            tr.rotation = Quaternion.Euler(0f, yaw, Mathf.Sin(Time.time * s.lakeBobRate * 0.7f + b.walkPhase * 5.1f) * 2f);
        }
    }
}
