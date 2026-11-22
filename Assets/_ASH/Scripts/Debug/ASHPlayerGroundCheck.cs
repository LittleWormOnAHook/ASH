using System.Collections;
using System.IO;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>
    /// Player grounding check in Play Mode: root vs terrain vs capsule vs baked-mesh sole height, plus a low side-view shot.
    /// exec ASH.DebugTools.ASHPlayerGroundCheck.Run. Shots: AshBridge/shots/phase2/.
    /// </summary>
    public sealed class ASHPlayerGroundCheck : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHPlayerGroundCheck()
        {
            _prevBg = default;
            ShotName = "10_feet_side.png";
        }

        public const string Dir = "AshBridge/shots/phase2/";
        static bool _prevBg;
        public static string ShotName = "10_feet_side.png";

        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] ground check requires Play Mode"); return; }
            _prevBg = Application.runInBackground; Application.runInBackground = true;
            new GameObject("ASH_GroundCheck").AddComponent<ASHPlayerGroundCheck>();
        }

        public static float SoleOffset(GameObject player, out string detail)
        {
            detail = ""; float minY = float.MaxValue;
            foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var m = new Mesh(); smr.BakeMesh(m, true); var v = m.vertices; var tr = smr.transform;
                foreach (var p in v) { float y = tr.TransformPoint(p).y; if (y < minY) minY = y; }
                Destroy(m);
            }
            var anim = player.GetComponent<Animator>();
            if (anim && anim.isHuman)
            {
                var lf = anim.GetBoneTransform(HumanBodyBones.LeftFoot); var lt = anim.GetBoneTransform(HumanBodyBones.LeftToes);
                detail = $"leftFoot {lf.position.y - player.transform.position.y:F3}, leftToes {(lt ? lt.position.y - player.transform.position.y : 0f):F3}, humanScale {anim.humanScale:F3}, leftFeetBottomHeight {anim.leftFeetBottomHeight:F3}";
            }
            return minY - player.transform.position.y;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(Dir);
            var player = GameObject.FindWithTag("Player"); var t = Terrain.activeTerrain;
            yield return new WaitForSecondsRealtime(1.5f);
            if (!player || !t) { Debug.LogWarning("[ASH] ground check: no player/terrain"); Finish(); yield break; }
            var pos = player.transform.position; float th = t.SampleHeight(pos) + t.GetPosition().y;
            var cap = player.GetComponent<CapsuleCollider>();
            float capBottom = cap ? cap.bounds.min.y : pos.y;
            Physics.Raycast(pos + Vector3.up * 1f, Vector3.down, out var hit, 5f, ~(1 << 13), QueryTriggerInteraction.Ignore);
            float sole = SoleOffset(player, out var det);
            Debug.Log($"[ASH] GROUND root y {pos.y:F3}, terrain {th:F3} (root-terrain {pos.y - th:+0.000;-0.000}), ray hit {(hit.collider ? hit.collider.name : "none")} {hit.point.y:F3}, capsule bottom-root {capBottom - pos.y:+0.000;-0.000}, mesh sole-root {sole:+0.000;-0.000} => sole above terrain {pos.y + sole - th:+0.000;-0.000} m; {det}");

            var cam = new GameObject("ASH_GroundCam").AddComponent<Camera>(); cam.depth = 100; cam.fieldOfView = 35f; cam.nearClipPlane = 0.05f;
            var side = player.transform.right; var fp = pos + side * 2.6f; fp.y = th + 0.35f;
            cam.transform.position = fp; cam.transform.LookAt(new Vector3(pos.x, th + 0.25f, pos.z));
            yield return new WaitForSecondsRealtime(1f);
            ScreenCapture.CaptureScreenshot(Dir + ShotName); yield return null; yield return null; yield return null;
            foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Debug.Log($"[ASH] GROUND smr {smr.name} enabled {smr.enabled} active {smr.gameObject.activeInHierarchy} layer {smr.gameObject.layer} forceOff {smr.forceRenderingOff} bounds {smr.bounds.center - pos} ext {smr.bounds.extents} rootBone {(smr.rootBone ? smr.rootBone.name : "-")} shadowsOnly {smr.shadowCastingMode} visible {smr.isVisible}");
            foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Dir + "10b_feet_side_offscreen.png"); yield return null; yield return null; yield return null;
            Destroy(cam.gameObject);
            Finish();
        }

        void Finish() { Debug.Log("[ASH] GROUND check done"); Application.runInBackground = _prevBg; Destroy(gameObject); }
    }
}
