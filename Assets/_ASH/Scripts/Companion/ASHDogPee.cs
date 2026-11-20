using System.Collections.Generic;
using ASH.Gameplay;
using UnityEngine;

namespace ASH.Companion
{
    /// <summary>t343: the dog's occasional leg-lift on trees / bushes / campfires. Target search plus the effects:
    /// a thin yellow particle stream, a fading yellow melt spot on the snow, and hiss + steam on a campfire
    /// (optional chance to burn fuel or put it out). Driven by ASHDogCompanion (Act.Pee, clip "Arm_Dog|Pissing").</summary>
    public sealed class ASHDogPee : MonoBehaviour
    {
        public struct Target { public Vector3 pos; public ASHCampfire fire; public string what; public float radius; }

        static readonly string[] TreeWords = { "tree", "pine", "fir", "spruce", "birch", "oak", "bush", "shrub", "stump", "trunk", "log", "conifer" };
        static ASHCompanionSettings S => ASHCompanionSettings.I;
        ParticleSystem _stream, _steam; static Material _mat; static Texture2D _spot;
        Transform _hipL, _hipR, _footL, _footR;

        void Awake()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "foot_b.L") _footL = t; else if (t.name == "foot_b.R") _footR = t;
                else if (t.name == "thigh.L" || t.name == "thigh_b.L") _hipL = t; else if (t.name == "thigh.R" || t.name == "thigh_b.R") _hipR = t;
            }
        }

        /// <summary>Picks a tree, bush or campfire within reach of the player (random among the closest few). False when none.</summary>
        public static bool FindTarget(Vector3 dogPos, Vector3 player, float reach, out Target best)
        {
            best = default; var list = new List<Target>();
            if (S.peeOnCampfires)
                foreach (var f in ASHCampfire.All) if (f && Flat(f.transform.position, player) < reach) list.Add(new Target { pos = f.transform.position, fire = f, what = "campfire", radius = 0.6f });
            if (S.peeOnTrees)
            {
                foreach (var c in Physics.OverlapSphere(player, reach, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!c || c is TerrainCollider || c.attachedRigidbody) continue;
                    string n = c.name.ToLowerInvariant(), r = c.transform.root.name.ToLowerInvariant();
                    bool tree = c.CompareTag("Untagged") ? false : c.tag.ToLowerInvariant().Contains("tree");
                    foreach (var w in TreeWords) if (n.Contains(w) || r.Contains(w)) { tree = true; break; }
                    if (!tree) continue;
                    var b = c.bounds; if (b.size.y < 0.4f || b.extents.x > 3f) continue;
                    var p = b.center; p.y = b.min.y;
                    list.Add(new Target { pos = p, what = n.Contains("bush") || n.Contains("shrub") ? "bush" : "tree", radius = Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.z), 0.1f, 1.2f) });
                }
                var ter = Terrain.activeTerrain;
                if (ter && ter.terrainData)
                {
                    var td = ter.terrainData; var protos = td.treePrototypes; var tp = ter.transform.position; var sz = td.size;
                    foreach (var ti in td.treeInstances)
                    {
                        var p = tp + Vector3.Scale(ti.position, sz);
                        if (Flat(p, player) > reach) continue;
                        string pn = ti.prototypeIndex < protos.Length && protos[ti.prototypeIndex].prefab ? protos[ti.prototypeIndex].prefab.name.ToLowerInvariant() : "tree";
                        list.Add(new Target { pos = p, what = pn.Contains("bush") || pn.Contains("shrub") ? "bush" : "tree", radius = 0.3f * Mathf.Max(0.5f, ti.widthScale) });
                    }
                }
            }
            if (list.Count == 0) return false;
            list.Sort((a, b) => Flat(a.pos, dogPos).CompareTo(Flat(b.pos, dogPos)));
            best = list[Random.Range(0, Mathf.Min(3, list.Count))];
            return true;
        }

        /// <summary>The spot to stand on: beside the target, a body-width off its trunk, on the dog's side of it.</summary>
        public static Vector3 StandSpot(Target t, Vector3 dogPos)
        {
            var away = dogPos - t.pos; away.y = 0f; if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            return t.pos + away.normalized * (t.radius + S.peeStandOff);
        }

        /// <summary>Which hind leg is up right now (by foot height); 0 = unknown.</summary>
        public int LiftedSide()
        {
            if (!_footL || !_footR) return 0;
            float d = _footL.position.y - _footR.position.y;
            return Mathf.Abs(d) < 0.06f ? 0 : d > 0f ? -1 : 1;
        }

        public void StartStream(Vector3 target, bool rightLeg)
        {
            if (!S.peeStream) return;
            if (!_stream) _stream = MakeStream();
            var hip = rightLeg ? (_hipR ? _hipR : _footR) : (_hipL ? _hipL : _footL);
            var root = transform;
            var origin = root.position + Vector3.up * S.peeStreamHeight - root.forward * 0.18f + (rightLeg ? root.right : -root.right) * 0.06f;
            if (hip) origin = Vector3.Lerp(origin, new Vector3(origin.x, Mathf.Max(origin.y, hip.position.y - 0.05f), origin.z), 0.5f);
            _stream.transform.position = origin;
            var to = target + Vector3.up * 0.25f - origin; _stream.transform.rotation = Quaternion.LookRotation(to.normalized + Vector3.up * 0.25f);
            var main = _stream.main; main.startSpeed = Mathf.Clamp(to.magnitude * 3f, 1.2f, 3.2f);
            _stream.Play();
        }

        public void StopStream() { if (_stream) _stream.Stop(true, ParticleSystemStopBehavior.StopEmitting); }

        /// <summary>The fading yellow spot (snow) or hiss + steam (campfire).</summary>
        public void Finish(Target t, AudioSource src)
        {
            StopStream();
            if (t.fire)
            {
                var fp = t.fire.transform.position;
                if (S.peeHissClip) AudioSource.PlayClipAtPoint(S.peeHissClip, fp + Vector3.up * 0.3f, S.peeHissVolume);
                if (S.peeSteam) { if (!_steam) _steam = MakeSteam(); _steam.transform.position = fp + Vector3.up * 0.25f; _steam.Play(); }
                if (t.fire.Lit)
                {
                    if (Random.value < S.peeExtinguishChance) t.fire.PutOut();
                    else if (S.peeFuelHoursLost > 0f) t.fire.ConsumeFuel(S.peeFuelHoursLost);
                }
                return;
            }
            if (S.peeDecalSeconds <= 0f) return;
            var side = transform.position - t.pos; side.y = 0f; side = side.sqrMagnitude > 0.001f ? side.normalized : Vector3.forward;
            var p = t.pos + side * (t.radius + 0.12f);
            if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad); go.name = "ASH_DogPeeSpot";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = hit.point + hit.normal * 0.012f; go.transform.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.value * 360f);
            go.transform.localScale = Vector3.one * S.peeDecalSize * Random.Range(0.8f, 1.2f);
            var mr = go.GetComponent<MeshRenderer>(); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.material = new Material(Mat()) { mainTexture = Spot() };
            go.AddComponent<Fade>().Init(mr.material, S.peeDecalColor, S.peeDecalSeconds);
        }

        sealed class Fade : MonoBehaviour
        {
            Material _m; Color _c; float _t, _life;
            public void Init(Material m, Color c, float life) { _m = m; _c = c; _life = life; _m.color = new Color(c.r, c.g, c.b, 0f); }
            void Update()
            {
                _t += Time.deltaTime; float a = _t < 1.5f ? _t / 1.5f : 1f - Mathf.Clamp01((_t - _life * 0.5f) / (_life * 0.5f));
                _m.color = new Color(_c.r, _c.g, _c.b, _c.a * a);
                if (_t >= _life) { Destroy(_m); Destroy(gameObject); }
            }
        }

        static Material Mat() { if (!_mat) _mat = new Material(Shader.Find("Sprites/Default")); return _mat; }
        static Texture2D Spot()
        {
            if (_spot) return _spot;
            const int N = 64; _spot = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (x - N / 2f) / (N / 2f), dy = (y - N / 2f) / (N / 2f), r = Mathf.Sqrt(dx * dx + dy * dy * 1.4f);
                float n = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.35f;
                _spot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - Mathf.SmoothStep(0.35f, 1f, r + n))));
            }
            _spot.Apply(); return _spot;
        }

        ParticleSystem MakeStream()
        {
            var go = new GameObject("PeeStream"); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.loop = true; m.startLifetime = 0.5f; m.startSpeed = 2f; m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
            m.startColor = S.peeStreamColor; m.gravityModifier = 1f; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 300; m.playOnAwake = false;
            var e = ps.emission; e.rateOverTime = 90f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 3f; sh.radius = 0.004f;
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.bounce = 0.05f; col.lifetimeLoss = 0.6f; col.quality = ParticleSystemCollisionQuality.Low;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.material = Mat(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1.5f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        ParticleSystem MakeSteam()
        {
            var go = new GameObject("PeeSteam");
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.loop = false; m.duration = 2.5f; m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f); m.startColor = new Color(0.9f, 0.9f, 0.9f, 0.35f); m.gravityModifier = -0.05f; m.simulationSpace = ParticleSystemSimulationSpace.World; m.playOnAwake = false;
            var e = ps.emission; e.rateOverTime = 18f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.35f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2f));
            var ca = ps.colorOverLifetime; ca.enabled = true; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) }); ca.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.material = Mat(); r.material.mainTexture = Spot(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        void OnDestroy() { if (_steam) Destroy(_steam.gameObject); }
        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
