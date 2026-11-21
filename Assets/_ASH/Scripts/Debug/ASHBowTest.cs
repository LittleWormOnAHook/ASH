using System.Collections;
using System.Globalization;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Bow aim / draw / nocked-arrow check (Play Mode, exec ASH.DebugTools.ASHBowTest.Run). Equips the bow, aims (no draw),
    /// full draw, releases. Each phase logs "[ASH] BOWTEST ..." (draw hand to nock, arrow vs aim angle, bow facing, hips height,
    /// knee bend, nock arrow shown) and "BOWTEST POSE name|x,y,z|pitch,yaw" camera poses (behind / right side), then holds 5 s.</summary>
    public sealed class ASHBowTest : MonoBehaviour
    {
        public static void RunSide() { if (Application.isPlaying) new GameObject("ASH_BowTest").AddComponent<ASHBowTest>()._side = true; }
        bool _side;
        IEnumerator Side(ASHWeaponAim aim, ASHEquipment eq, Camera cam)
        {
            var rig = eq.MainVisual ? eq.MainVisual.GetComponent<ASH.Combat.ASHBowDrawRig>() : null; if (!rig) { Debug.Log("[ASH] BOWA side: no rig"); yield break; }
            float w0 = rig.handOnArrowLine, s0 = rig.handLineSide; var rh = eq.Animator.GetBoneTransform(HumanBodyBones.RightHand);
            foreach (var cfg in new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, -0.06f), new Vector2(1f, 0.06f) })
            {
                rig.handOnArrowLine = cfg.x; rig.handLineSide = cfg.y; yield return new WaitForSeconds(0.5f); yield return new WaitForEndOfFrame();
                var l = (aim.ArrowRestWorld - aim.NockWorld).normalized; var rel = rh.position - aim.NockWorld; var off = rel - Vector3.Dot(rel, l) * l;
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[ASH] BOWA side w={0:F0} side={1:F2}: handOff camRight={2:F3} camUp={3:F3} hand->nock={4:F3} target->hand={5:F3}", cfg.x, cfg.y, Vector3.Dot(off, cam.transform.right), Vector3.Dot(off, cam.transform.up), rel.magnitude, rig.handTarget ? Vector3.Distance(rig.handTarget.position, rh.position) : -1f));
            }
            rig.handOnArrowLine = w0; rig.handLineSide = s0;
        }
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_BowTest").AddComponent<ASHBowTest>(); }
        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3}", v.x, v.y, v.z);

        IEnumerator Start()
        {
            yield return null;
            var inv = FindAnyObjectByType<ASHInventory>(); var eq = FindAnyObjectByType<ASHEquipment>(); var input = FindAnyObjectByType<ASHInput>();
            var cc = FindAnyObjectByType<vThirdPersonController>(); var aim = FindAnyObjectByType<ASHWeaponAim>(); var cam = Camera.main;
            if (!(inv && eq && input && cc && aim && cam)) { Debug.Log("[ASH] BOWTEST setup failed"); Destroy(gameObject); yield break; }
#if UNITY_EDITOR
            var bow = UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_WoodenBow.asset");
            var arrows = UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_WoodenArrow.asset");
#else
            ASHItemDefinition bow = null, arrows = null;
#endif
            var tp = cc.GetComponent<vThirdPersonInput>(); if (tp) tp.lockCameraInput = true;
            if (arrows && inv.Count(arrows) < 5) inv.AddItem(arrows, 5);
            if (bow && inv.Count(bow) == 0) inv.AddItem(bow, 1);
            eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand); yield return null;
            eq.Equip(bow); yield return new WaitForSeconds(0.6f);
            yield return EndLog("idle", eq, aim, cc, cam);
            input.SimSecondaryHeld = true; yield return new WaitForSeconds(1.8f);
            yield return EndLog("aim", eq, aim, cc, cam); yield return new WaitForSeconds(5f);
            input.SimDraw = 1f; yield return new WaitForSeconds(1.5f);
            yield return EndLog("draw", eq, aim, cc, cam); if (_side) yield return Side(aim, eq, cam); yield return new WaitForSeconds(_side ? 0.2f : 5f);
            input.SimDraw = -1f; yield return new WaitForSeconds(0.15f);
            yield return EndLog("shot", eq, aim, cc, cam);
            yield return new WaitForSeconds(0.8f);
            yield return EndLog("renock", eq, aim, cc, cam);
            input.SimSecondaryHeld = false; yield return new WaitForSeconds(0.6f);
            yield return EndLog("lowered", eq, aim, cc, cam);
            if (tp) tp.lockCameraInput = false;
            Debug.Log("[ASH] BOWTEST done"); Destroy(gameObject);
        }

        static IEnumerator EndLog(string phase, ASHEquipment eq, ASHWeaponAim aim, vThirdPersonController cc, Camera cam) { yield return new WaitForEndOfFrame(); Log(phase, eq, aim, cc, cam); }
        static void Log(string phase, ASHEquipment eq, ASHWeaponAim aim, vThirdPersonController cc, Camera cam)
        {
            var a = eq.Animator; var root = cc.transform;
            var hips = a.GetBoneTransform(HumanBodyBones.Hips); var rh = a.GetBoneTransform(HumanBodyBones.RightHand);
            var ri = a.GetBoneTransform(HumanBodyBones.RightIndexProximal) ?? rh; var chest = a.GetBoneTransform(HumanBodyBones.UpperChest) ?? a.GetBoneTransform(HumanBodyBones.Chest);
            float Knee(HumanBodyBones u, HumanBodyBones l, HumanBodyBones f)
            { var U = a.GetBoneTransform(u); var L = a.GetBoneTransform(l); var F = a.GetBoneTransform(f); return U && L && F ? Vector3.Angle(L.position - U.position, F.position - L.position) : -1f; }
            var nockGo = GameObject.Find("ASH_NockArrow"); bool shown = nockGo && nockGo.activeInHierarchy;
            var shaft = aim.ArrowRestWorld - aim.NockWorld; var camF = cam.transform.forward;
            float arrowVsAim = shaft.sqrMagnitude > 1e-4f ? Vector3.Angle(shaft, camF) : -1f;
            float yawErr = shaft.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(Vector3.ProjectOnPlane(camF, Vector3.up), Vector3.ProjectOnPlane(shaft, Vector3.up), Vector3.up) : 0f;
            float arrowLen = -1f; if (shown) { var rs = nockGo.GetComponentsInChildren<Renderer>(); if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); arrowLen = b.size.magnitude; } }
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[ASH] BOWTEST {0}: aiming={1} draw={2:F2} nockArrow={3} arrowBoundsDiag={4:F2}m fingers->nock={5:F3}m hand->nock={6:F3}m arrow-vs-aim={7:F1}deg yawErr={8:F1}deg nock-from-chest(right/up/fwd)={9} hipsH={10:F3} kneeL={11:F0} kneeR={12:F0} moveSet={13}",
                phase, aim.Aiming, aim.Draw01, shown, arrowLen, Vector3.Distance(ri.position, aim.NockWorld), Vector3.Distance(rh.position, aim.NockWorld), arrowVsAim, yawErr,
                chest ? V(cam.transform.InverseTransformDirection(aim.NockWorld - chest.position)) : "-", hips.position.y - root.position.y,
                Knee(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot), Knee(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
                a.GetFloat("MoveSet_ID")));
            var lg = a.GetComponent<ASHBowUprightLegs>(); var inp = cc.GetComponent<vThirdPersonController>();
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[ASH] BOWTEST legs {0}: comp={1} target={2:F2} still={3} w={4:F2} hasStand={5} standHipsH={6:F3} grounded={7} crouch={8} input={9}",
                phase, (bool)lg, lg ? lg.targetWeight : -1f, lg && lg.standingStill, lg ? lg.Weight : -1f, lg && lg.HasStand, lg ? lg.StandHipsHeight : -1f, cc.isGrounded, cc.isCrouching, cc.input));
            {
                var ap = aim.AimPoint(out _); var camR = cam.transform.right;
                var l = (aim.ArrowRestWorld - aim.NockWorld).normalized; var rel = rh.position - aim.NockWorld; var off = rel - Vector3.Dot(rel, l) * l;
                var sp = cam.WorldToViewportPoint(aim.ArrowRestWorld + l * 5f);
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "[ASH] BOWA {0}: xhairErr before={1:F1} after={2:F1} line-vs-aimpt={3:F1}deg handOffLine(camRight/camUp)={4:F3},{5:F3} arrowTip5mViewport={6:F3},{7:F3} aimDist={8:F1} lean={9:F1}->{10:F1} camRight={11:F2} camH={12:F2}",
                    phase, aim.BowCrosshairErrorBefore, aim.BowCrosshairErrorAfter, Vector3.Angle(aim.ArrowRestWorld - aim.NockWorld, ap - aim.NockWorld), Vector3.Dot(off, camR), Vector3.Dot(off, cam.transform.up), sp.x, sp.y, Vector3.Distance(cam.transform.position, ap), aim.BowLeanBefore, aim.BowLeanAfter, aim.CameraRightNow, aim.CameraHeightNow));
                if (phase == "aim" || phase == "draw") ScreenCapture.CaptureScreenshot(@"C:\Users\Teabagger\ashx\bow_" + phase + ".png");
                if (phase == "shot")
                    foreach (var rb in Object.FindObjectsByType<Rigidbody>())
                        if (rb.linearVelocity.sqrMagnitude > 25f && !rb.transform.IsChildOf(root))
                            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[ASH] BOWA projectile {0}: vel-vs-aimpt={1:F1}deg", rb.name, Vector3.Angle(rb.linearVelocity, ap - rb.position)));
            }
            if (phase != "aim" && phase != "draw") return;
            var target = chest ? chest.position : root.position + Vector3.up * 1.4f;
            var fwd = Vector3.ProjectOnPlane(camF, Vector3.up).normalized; var right = Vector3.Cross(Vector3.up, fwd);
            Pose("behind", target - fwd * 2.0f + right * 0.25f + Vector3.up * 0.25f, target);
            Pose("side", target + right * 2.2f + fwd * 0.4f + Vector3.up * 0.1f, target);
        }

        static void Pose(string n, Vector3 pos, Vector3 look)
        {
            var d = (look - pos).normalized; float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pitch = -Mathf.Asin(d.y) * Mathf.Rad2Deg;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[ASH] BOWTEST POSE {0}|{1}|{2:F1},{3:F1},50", n, V(pos), pitch, yaw));
        }
    }
}