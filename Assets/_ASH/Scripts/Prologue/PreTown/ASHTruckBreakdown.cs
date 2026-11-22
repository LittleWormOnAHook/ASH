using System;
using ASH.Core;
using ASH.Map;
using ASH.UI;
using ASH.Vehicles;
using ASH.World.Roads;
using UnityEngine;

namespace ASH.Prologue
{
    /// <summary>t619: the repaired truck smokes and stalls ~400 m before Red Rock (ASH_RoadSettings), then limps on with reduced
    /// power, sputters and hood smoke until the edge of town, where it dies for good and stays in the road. Drive to Red Rock still
    /// completes at the pre-town arrive radius (on foot). Runtime only, saved under "truckBreakdown".</summary>
    public sealed class ASHTruckBreakdown : MonoBehaviour, IASHSaveable
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHTruckBreakdown() { Instance = null; }
        public static ASHTruckBreakdown Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook() { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded; Ensure(); }
        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => Ensure();
        static void Ensure()
        {
            if (Instance || !ASHPrologueDirector.Instance && !FindAnyObjectByType<ASHPrologueDirector>()) return;
            new GameObject("ASH_TruckBreakdown").AddComponent<ASHTruckBreakdown>();
        }

        public enum Phase { None, Stalled, Limping, Dead }
        public Phase State { get; private set; }

        static ASHRoadSettings S => ASHRoadSettings.I;
        ASHVehicle _truck; float _phaseT, _nextSputter, _sputterEnd, _basePM = -1f, _nextNag, _slowT;
        ParticleSystem _smoke;

        void Awake() { Instance = this; }
        void OnEnable() { Instance = this; ASHSaveSystem.Register(this); }
        void OnDisable() { ASHSaveSystem.Unregister(this); if (Instance == this) Instance = null; }
        void OnDestroy() { RestorePower(); if (_smoke) Destroy(_smoke.gameObject); }

        ASHVehicle Truck()
        {
            if (_truck) return _truck;
            var id = ASHPrologueSettings.I ? ASHPrologueSettings.I.truckVehicleId : "pickup";
            foreach (var v in ASHVehicle.All) if (v && v.vehicleId == id) return _truck = v;
            return null;
        }

        static Vector3 RedRock()
        {
            var p = ASHPreTownSettings.I; var go = string.IsNullOrEmpty(p.redRockObject) ? null : GameObject.Find(p.redRockObject);
            return go ? go.transform.position : new Vector3(p.redRockXZ.x, 0f, p.redRockXZ.y);
        }
        static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        void Update()
        {
            if (!S.breakdown || ASHSaveSystem.IsRestoring) return;
            var t = Truck(); if (!t) return;
            var vc = t.Controller;
            switch (State)
            {
                case Phase.None:
                    _slowT -= Time.deltaTime; if (_slowT > 0f) return; _slowT = 0.25f;
                    if (ASHVehicle.Driving != t || !ASHPreTown.TruckReady || !t.EngineRunning) return;
                    if (FlatDist(t.transform.position, RedRock()) > S.stallDistance) return;
                    Enter(Phase.Stalled, true);
                    break;
                case Phase.Stalled:
                    KeepOff(vc, false);
                    if (Time.time - _phaseT >= S.stallSeconds) Enter(Phase.Limping, true);
                    break;
                case Phase.Limping:
                    if (FlatDist(t.transform.position, RedRock()) <= S.townEdgeDistance) { Enter(Phase.Dead, true); break; }
                    Sputter(t);
                    break;
                case Phase.Dead:
                    KeepOff(vc, true);
                    break;
            }
        }

        void Enter(Phase p, bool say)
        {
            State = p; _phaseT = Time.time;
            var t = Truck(); var vc = t ? t.Controller : null;
            EnsureSmoke(t);
            switch (p)
            {
                case Phase.Stalled:
                    if (vc) { try { if (vc.engine.IsRunning) vc.engine.Stop(); } catch { } }
                    SetSmoke(S.smokeRateStalled);
                    if (say) Say(S.stallMessage);
                    Debug.Log("[ASH] Truck breakdown: stalled " + (t ? FlatDist(t.transform.position, RedRock()).ToString("F0") : "?") + " m from Red Rock.");
                    break;
                case Phase.Limping:
                    ApplyLimp(t, S.limpPower);
                    if (vc && ASHVehicle.Driving == t) { try { if (!vc.engine.IsRunning) vc.engine.Start(); } catch { } }
                    SetSmoke(S.smokeRateLimping);
                    _nextSputter = Time.time + UnityEngine.Random.Range(S.sputterEvery.x, Mathf.Max(S.sputterEvery.x, S.sputterEvery.y));
                    if (say) Say(S.limpMessage);
                    break;
                case Phase.Dead:
                    if (vc) { try { if (vc.engine.IsRunning) vc.engine.Stop(); } catch { } }
                    RestorePower();
                    SetSmoke(S.smokeRateLimping * 0.5f);
                    if (say) { Say(S.deadMessage); if (!string.IsNullOrEmpty(S.journalEntry)) ASHJournalLog.Add("The truck gives out", S.journalEntry, "truck_breakdown"); }
                    Debug.Log("[ASH] Truck breakdown: died at the edge of Red Rock.");
                    break;
            }
        }

        void KeepOff(NWH.VehiclePhysics.VehicleController vc, bool dead)
        {
            if (!vc) return;
            bool running; try { running = vc.engine.IsRunning; } catch { return; }
            if (!running) return;
            try { vc.engine.Stop(); } catch { }
            if (Time.time >= _nextNag && ASHVehicle.Driving == _truck) { _nextNag = Time.time + 6f; Say(dead ? "It won't turn over. The motor's done." : "The starter grinds. Give it a moment."); }
        }

        void ApplyLimp(ASHVehicle t, float mul)
        {
            if (!t) return;
            if (_basePM < 0f) _basePM = t.powerMultiplier;
            t.powerMultiplier = Mathf.Max(0.02f, _basePM * mul); t.ApplyPower();
        }
        void RestorePower() { if (_truck && _basePM >= 0f) { _truck.powerMultiplier = _basePM; _truck.ApplyPower(); } _basePM = -1f; }

        void Sputter(ASHVehicle t)
        {
            if (!t) return;
            if (_sputterEnd > 0f)
            {
                if (Time.time >= _sputterEnd) { _sputterEnd = 0f; ApplyLimp(t, S.limpPower); SetSmoke(S.smokeRateLimping); }
                return;
            }
            if (Time.time < _nextSputter) return;
            _sputterEnd = Time.time + S.sputterLength;
            _nextSputter = _sputterEnd + UnityEngine.Random.Range(S.sputterEvery.x, Mathf.Max(S.sputterEvery.x, S.sputterEvery.y));
            ApplyLimp(t, 0.02f);
            if (_smoke) _smoke.Emit(12);
        }

        void Say(string msg) { if (string.IsNullOrEmpty(msg)) return; var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.Toast(msg); }

        // ------------------------------------------------------------------ hood smoke
        void EnsureSmoke(ASHVehicle t)
        {
            if (_smoke || !t) return;
            var go = new GameObject("ASH_HoodSmoke"); go.transform.SetParent(t.transform, false);
            go.transform.localPosition = S.smokeOffset != Vector3.zero ? S.smokeOffset : HoodPoint(t);
            _smoke = go.AddComponent<ParticleSystem>(); _smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _smoke.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f); main.startColor = S.smokeColor; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 600;
            main.gravityModifier = -0.05f;
            var sh = _smoke.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = 0.35f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var sol = _smoke.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.6f));
            var col = _smoke.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = _smoke.noise; noise.enabled = true; noise.strength = 0.4f; noise.frequency = 0.4f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var shd = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shd)
            {
                var m = new Material(shd) { name = "ASH_HoodSmoke (runtime)" };
                var tex = Puff(); m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
                if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
                if (m.HasProperty("_SrcBlend")) { m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 3000; r.sharedMaterial = m;
            }
            r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var em = _smoke.emission; em.rateOverTime = 0f;
            _smoke.Play();
        }
        void SetSmoke(float rate) { if (!_smoke) return; var em = _smoke.emission; em.rateOverTime = rate; }

        static Vector3 HoodPoint(ASHVehicle t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(); bool any = false; Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
            foreach (var r in rs)
            {
                if (!r || r is ParticleSystemRenderer || !r.enabled) continue;
                var b = r.bounds; any = true;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var l = t.transform.InverseTransformPoint(c); mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
                }
            }
            if (!any) return new Vector3(0f, 1.3f, 1.8f);
            return new Vector3((mn.x + mx.x) * 0.5f, Mathf.Lerp(mn.y, mx.y, 0.72f), Mathf.Lerp(mn.z, mx.z, 0.86f));
        }

        static Texture2D _puff;
        static Texture2D Puff()
        {
            if (_puff) return _puff;
            const int N = 64; _puff = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "ASH_SmokePuff", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f; float d = Mathf.Sqrt(dx * dx + dy * dy);
                float n = Mathf.PerlinNoise(x * 0.13f, y * 0.13f) * 0.35f + 0.65f;
                byte a = (byte)(Mathf.Clamp01((1f - d) * 1.3f) * n * 255f);
                px[y * N + x] = new Color32(255, 255, 255, a);
            }
            _puff.SetPixels32(px); _puff.Apply(); return _puff;
        }

        // ------------------------------------------------------------------ save
        [Serializable] sealed class Save { public int phase; }
        public string SaveKey => "truckBreakdown";
        public int RestoreOrder => 45; // after vehicles (40)
        public string CaptureJson() => JsonUtility.ToJson(new Save { phase = (int)State });
        public void RestoreJson(string json)
        {
            var s = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Save>(json);
            var p = s == null ? Phase.None : (Phase)s.phase;
            RestorePower(); State = Phase.None;
            if (p == Phase.Stalled) p = Phase.Limping; // a reload skips the stall wait
            if (p != Phase.None) Enter(p, false);
        }
    }
}
