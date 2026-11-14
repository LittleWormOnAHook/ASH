using System.Collections;
using System.IO;
using ASH.Player;
using ASH.World.Accumulation;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>
    /// Phase 2 check (Play Mode): visible ash depth, footprints, knee drift, foot IK on a slope, FPS at spawn and ridge timber.
    /// exec ASH.DebugTools.ASHPhase2Check.Run. Logs "[ASH] P2 DONE". Shots: AshBridge/shots/phase2/.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class ASHPhase2Check : MonoBehaviour
    {
        void FixedUpdate() { if (_mot) _mot.input = Vector3.zero; }
        const string Dir = ASHPlayerGroundCheck.Dir;
        static bool _prevBg; static bool _quick;
        public static void RunQuick() { _quick = true; Run(); }
        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] P2 requires Play Mode"); return; }
            _prevBg = Application.runInBackground; Application.runInBackground = true;
            new GameObject("ASH_P2Check").AddComponent<ASHPhase2Check>();
        }

        Invector.vCharacterController.vThirdPersonMotor _mot; Invector.vCharacterController.vThirdPersonInput _inp;
        Camera _cam; GameObject _player; Terrain _t; ASHAccumulationController _c;
        float H(Vector3 p) => _t.SampleHeight(p) + _t.GetPosition().y;

        IEnumerator Fps(string label, float secs)
        {
            yield return new WaitForSecondsRealtime(1f);
            int f = 0; float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < secs) { f++; yield return null; }
            Debug.Log($"[ASH] P2 FPS {label}: {f / (Time.realtimeSinceStartup - t0):F1}");
        }
        IEnumerator Shot(string name) { yield return new WaitForSecondsRealtime(0.6f); ScreenCapture.CaptureScreenshot(Dir + name); yield return null; yield return null; yield return null; }
        void Look(Vector3 from, Vector3 at) { _cam.transform.position = from; _cam.transform.LookAt(at); }
        void Teleport(Vector3 p, Vector3 fwd)
        {
            var rb = _player.GetComponent<Rigidbody>();
            if (rb) { rb.linearVelocity = Vector3.zero; rb.position = p; }
            _player.transform.SetPositionAndRotation(p, Quaternion.LookRotation(fwd, Vector3.up));
        }
        float Slope(Vector3 p) { var n = _t.terrainData.GetInterpolatedNormal((p.x - _t.GetPosition().x) / _t.terrainData.size.x, (p.z - _t.GetPosition().z) / _t.terrainData.size.z); return Vector3.Angle(Vector3.up, n); }
        Vector3 Normal(Vector3 p) => _t.terrainData.GetInterpolatedNormal((p.x - _t.GetPosition().x) / _t.terrainData.size.x, (p.z - _t.GetPosition().z) / _t.terrainData.size.z);
        bool Clear(Vector3 p) => !Physics.CheckSphere(p + Vector3.up * 1.2f, 0.9f, ~((1 << 13) | (1 << 2)), QueryTriggerInteraction.Ignore);

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            yield return new WaitForSecondsRealtime(2f);
            _player = GameObject.FindWithTag("Player");
            if (_player) { var ctl = _player.GetComponentInParent<Invector.vCharacterController.vThirdPersonController>(); if (!ctl) ctl = FindAnyObjectByType<Invector.vCharacterController.vThirdPersonController>(); if (ctl) _player = ctl.gameObject; } _t = Terrain.activeTerrain; _c = ASHAccumulationController.Instance;
            if (!_player || !_t || !_c) { Debug.LogWarning($"[ASH] P2 missing player {(bool)_player} terrain {(bool)_t} controller {(bool)_c}"); Finish(); yield break; }
            var anim = _player.GetComponent<Animator>(); var ik = _player.GetComponent<ASHFootIK>();
            Debug.Log($"[ASH] P2 player '{_player.name}' animator {(anim && anim.runtimeAnimatorController ? anim.runtimeAnimatorController.name : "-")}, footIK {(bool)ik}, deformer {(bool)_player.GetComponent<ASHDeformer>()}, surface {(_c.Surface ? _c.Surface.VertexCount : 0)} verts, field {(_c.Field && _c.Field.Active)}");

            // freeze player input during the check (a stuck forward input was seen in unfocused Play)
            _mot = _player.GetComponent<Invector.vCharacterController.vThirdPersonMotor>(); _inp = _player.GetComponent<Invector.vCharacterController.vThirdPersonInput>();
            Debug.Log($"[ASH] P2 input at start {(_mot ? _mot.input : Vector3.zero)}");
            if (_mot) _mot.input = Vector3.zero; // zeroed every FixedUpdate before Invector reads it
            { var rbz = _player.GetComponent<Rigidbody>(); if (rbz) rbz.linearVelocity = Vector3.zero; }
            yield return new WaitForSecondsRealtime(1f);
            foreach (var smr in _player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true; // test cams only see him
            var spawn = _player.transform.position; var fwd = _player.transform.forward;
            yield return Fps("spawn (player cam)", 5f);

            // deformation quality sweep at spawn (player cam only; lightweight frame timing)
            var startQ = _c.deformationQuality;
            var qs = _quick ? new int[0] : new[] { 0, 1, 2, 3, 4, 5, 5, 4, 3, 2, 1, 0 };
            foreach (var qi in qs)
            {
                var q = (ASHAccumulationController.DeformQuality)qi;
                _c.SetDeformationQuality(q);
                yield return new WaitForSecondsRealtime(1.5f);
                int f = 0; float t0 = Time.realtimeSinceStartup, worst = 0f, last = t0;
                while (Time.realtimeSinceStartup - t0 < 4f) { yield return null; f++; float now = Time.realtimeSinceStartup; worst = Mathf.Max(worst, now - last); last = now; }
                int rts = 0, meshes = 0;
                foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>()) if (rt.name == "ASH_DeformField") rts++;
                foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) if (m.name == "ASH_AccumSurface") meshes++;
                Debug.Log($"[ASH] P2 QUALITY {q}: {f / (Time.realtimeSinceStartup - t0):F1} fps (worst frame {worst * 1000f:F0} ms), RT {_c.deformResolution}, verts {(_c.Surface ? _c.Surface.VertexCount : 0)}, live deform RTs {rts}, surface meshes {meshes}");
            }
            _c.SetDeformationQuality(startQ);
            yield return new WaitForSecondsRealtime(1.5f);

            _cam = new GameObject("ASH_P2Cam").AddComponent<Camera>(); _cam.depth = 100; _cam.fieldOfView = 40f; _cam.nearClipPlane = 0.05f;
            if (_c.Surface) _c.Surface.follow = _cam.transform;

            // 1) feet on flat ground after IK
            float sole = ASHPlayerGroundCheck.SoleOffset(_player, out _);
            float th = H(spawn);
            Debug.Log($"[ASH] P2 GROUND root-terrain {spawn.y - th:+0.000;-0.000}, sole above terrain {spawn.y + sole - th:+0.000;-0.000}, pelvis IK {(ik ? ik.PelvisOffset : 0f):+0.000;-0.000}, ash base depth {_c.GetBaseDepthMetres(spawn):F3} m, depth incl. deformation {_c.GetDepthMetres(spawn):F3} m");
            var side = _player.transform.right;
            Look(spawn + side * 2.6f + Vector3.up * 0.65f, spawn + Vector3.up * 0.3f); yield return Shot("10_feet_side.png");
            Look(spawn + side * 2.2f + fwd * 1.2f + Vector3.up * 0.9f, spawn + Vector3.up * 0.2f); yield return Shot("11_ash_depth_feet.png");

            // 2) footprints + drag trail beside the player
            var o = spawn + side * 1.5f; float fp = _c.footprintDepth;
            for (int i = 0; i < 12; i++) { var p = o + fwd * (i * 0.45f) + side * ((i & 1) == 0 ? -0.12f : 0.12f); _c.Deform(p, 0.13f, 1f); }
            _c.DeformSegment(o + side * 1.2f, o + side * 1.2f + fwd * 5f, 0.3f, 0.7f);
            yield return new WaitForSecondsRealtime(0.8f);
            var probe = o + fwd * 0.9f + side * -0.12f;
            Debug.Log($"[ASH] P2 FIELD {(_c.Field ? _c.Field.DebugState : "-")}");
            var mx = _c.Field ? _c.Field.DebugMax() : Vector4.zero; Debug.Log($"[ASH] P2 FIELD max {mx.w:F2} at ({mx.x:F2},{mx.z:F2}); probe ({probe.x:F2},{probe.z:F2}); first stamp ({o.x:F2},{o.z:F2}) fwd {fwd} side {side}");
            Debug.Log($"[ASH] P2 FOOTPRINT deformation {_c.GetDeformation(probe):F2} (footprintDepth {fp:F2}), depth there {_c.GetDepthMetres(probe):F3} vs base {_c.GetBaseDepthMetres(probe):F3}; refill/s {_c.DeformRefillPerSecond():F4}");
            Look(o - fwd * 2.2f + side * 0.6f + Vector3.up * 1.8f, o + fwd * 2.2f + side * 0.5f); yield return Shot("12_footprints.png");

            // depth distribution (400 m square around spawn, flat-ish ground)
            int[] hist = new int[6]; int tot = 0;
            for (float x = -200; x <= 200; x += 5) for (float z = -200; z <= 200; z += 5)
            { var p = spawn + new Vector3(x, 0, z); p.y = H(p); float d = _c.GetBaseDepthMetres(p); hist[Mathf.Min(5, (int)(d / 0.1f))]++; tot++; }
            Debug.Log($"[ASH] P2 DEPTH HIST (0-.1/.1-.2/.2-.3/.3-.4/.4-.5/.5+): {string.Join(" / ", System.Array.ConvertAll(hist, h => (100f * h / tot).ToString("F0") + "%"))}");
            // 3) knee drift: deepest base depth near spawn
            Vector3 best = spawn; float bd = -1f;
            for (float x = -120; x <= 120; x += 4) for (float z = -120; z <= 120; z += 4)
            { var p = spawn + new Vector3(x, 0, z); p.y = H(p); float d = _c.GetBaseDepthMetres(p); if (d > bd && Slope(p) < 25f) { bd = d; best = p; } }
            Debug.Log($"[ASH] P2 DRIFT deepest {bd:F3} m at {best}");
            var away = (best - spawn); away.y = 0; away = away.sqrMagnitude > 1 ? away.normalized : Vector3.forward;
            Teleport(best + Vector3.up * 0.05f, Vector3.Cross(Vector3.up, away)); yield return new WaitForSecondsRealtime(1.5f);
            var pp = _player.transform.position;
            Debug.Log($"[ASH] P2 DRIFT player depth {_c.GetDepthMetres(pp):F3} (base {_c.GetBaseDepthMetres(pp):F3})");
            Look(pp + _player.transform.right * 2.8f + Vector3.up * 0.8f, pp + Vector3.up * 0.4f); yield return Shot("14_knee_drift.png");

            // 4) slope IK: 15-28 deg spot near spawn, standing across the slope
            Vector3 sp = Vector3.zero; bool found = false;
            for (float r = 4; r < 120 && !found; r += 3) for (int a = 0; a < 24 && !found; a++)
            { var p = spawn + Quaternion.Euler(0, a * 15f, 0) * Vector3.forward * r; p.y = H(p); float s = Slope(p); if (s > 15f && s < 28f && Clear(p)) { sp = p; found = true; } }
            if (found)
            {
                var n = Normal(sp); var down = new Vector3(n.x, 0, n.z).normalized; var along = Vector3.Cross(Vector3.up, down);
                Teleport(sp + Vector3.up * 0.05f, along); yield return new WaitForSecondsRealtime(2.5f);
                var q = _player.transform.position; var l = anim.GetBoneTransform(HumanBodyBones.LeftFoot); var rt = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                Debug.Log($"[ASH] P2 SLOPE {Slope(sp):F1} deg at {sp}: root-terrain {q.y - H(q):+0.000;-0.000}, pelvis {(ik ? ik.PelvisOffset : 0f):+0.000;-0.000}, L foot-terrain {l.position.y - H(l.position):+0.000;-0.000}, R foot-terrain {rt.position.y - H(rt.position):+0.000;-0.000}");
                Look(q + along * 2.6f + Vector3.up * 0.7f, q + Vector3.up * 0.5f); yield return Shot("13_slope_ik.png");
                Look(q - down * 2.4f + along * 0.8f + Vector3.up * 0.6f, q + Vector3.up * 0.5f); yield return Shot("13b_slope_ik_front.png");
            }
            else Debug.LogWarning("[ASH] P2 no clear 15-28 deg slope near spawn");
            Teleport(spawn, fwd);

            // 5) ridge timber FPS
            var ridge = new Vector3(400, 0, 1400); ridge.y = H(ridge) + 2f;
            Look(ridge, ridge + new Vector3(1, -0.1f, 1));
            yield return Fps("ridge timber (test cam + player cam)", 5f);
            yield return Shot("15_ridge.png");
            Finish();
        }

        void Finish()
        {
            if (_c && _c.Surface) _c.Surface.follow = null;
            Application.runInBackground = _prevBg;
            if (_cam) Destroy(_cam.gameObject);
            Debug.Log("[ASH] P2 DONE");
            Destroy(gameObject);
        }
    }
}
