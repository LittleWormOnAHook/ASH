using System.Collections;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Camera creep regression (Play Mode, exec ASH.DebugTools.ASHCameraSwitchTest.Run): switches held items ~10 times
    /// (rifle, pistol, bow, torch, flashlight, hatchet), aiming on some and switching WHILE RMB is held on others, then checks the
    /// camera returns to its baseline state/height/offset and strafe is restored. Logs "[ASH] CAMSWITCH ..." + SUMMARY.</summary>
    public sealed class ASHCameraSwitchTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_CamSwitchTest").AddComponent<ASHCameraSwitchTest>(); }
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"CAMSWITCH {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }
        static ASHItemDefinition Item(string file)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"Assets/_ASH/Data/Items/ASH_Item_{file}.asset");
#else
            return null;
#endif
        }

        IEnumerator Start()
        {
            yield return null;
            var inv = FindAnyObjectByType<ASHInventory>(); var eq = FindAnyObjectByType<ASHEquipment>(); var input = FindAnyObjectByType<ASHInput>();
            var wa = FindAnyObjectByType<ASHWeaponAim>(); var cc = FindAnyObjectByType<vThirdPersonController>(); var tp = cc ? cc.GetComponent<vThirdPersonInput>() : null;
            var cam = tp ? tp.tpCamera : null;
            var items = new[] { Item("HuntingRifle"), Item("Pistol"), Item("WoodenBow"), Item("Torch"), Item("Flashlight"), Item("Hatchet") };
            var ammo = new[] { Item("RifleRounds"), Item("PistolRounds"), Item("WoodenArrow") };
            bool ok = inv && eq && input && wa && cc && cam && System.Array.TrueForAll(items, i => i);
            Check("setup", ok, $"inv={(bool)inv} eq={(bool)eq} wa={(bool)wa} cc={(bool)cc} cam={(bool)cam}");
            if (!ok) { Finish(); yield break; }
            foreach (var i in items) if (inv.Count(i) == 0) inv.AddItem(i, 1);
            foreach (var a in ammo) if (a && inv.Count(a) < 5) inv.AddItem(a, 5);
            input.SimSecondaryHeld = false;
            bool lock0 = tp.lockCameraInput; tp.lockCameraInput = true; // real mouse movement on the PC must not move the pitch during the test
            eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand); eq.Unequip(ASHEquipSlot.Hip);
            yield return new WaitForSeconds(2.5f);
            // let the free-look pitch settle (Invector eases it back behind the player after a delay) before taking the baseline
            for (float w = 0f; w < 8f; w += 0.5f) { float a = MouseY(cam); yield return new WaitForSeconds(0.5f); if (Mathf.Abs(Mathf.DeltaAngle(a, MouseY(cam))) < 0.3f) break; }
            string st0 = wa.CameraStateNow; float h0 = wa.CameraHeightNow, r0 = cam.currentState.right, d0 = cam.currentState.defaultDistance; bool s0 = cc.isStrafing;
            var cp0 = cc.transform.InverseTransformPoint(cam.transform.position); float my0 = MouseY(cam);
            Diag("baseline", cc, cam, wa);

            float maxH = h0;
            for (int n = 0; n < 12; n++)
            {
                var it = items[n % items.Length];
                eq.Equip(it); yield return null;
                if (n % 3 == 0) { input.SimSecondaryHeld = true; yield return new WaitForSeconds(0.5f); input.SimSecondaryHeld = false; }      // aim, then let go
                else if (n % 3 == 1) { input.SimSecondaryHeld = true; yield return new WaitForSeconds(0.3f); }                                  // keep RMB held into the next switch
                else { yield return new WaitForSeconds(0.3f); input.SimSecondaryHeld = false; }
                yield return new WaitForSeconds(0.2f);
                maxH = Mathf.Max(maxH, wa.CameraHeightNow);
                Diag($"step{n} {it.name}", cc, cam, wa);
            }
            input.SimSecondaryHeld = false; yield return null;
            eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand); eq.Unequip(ASHEquipSlot.Hip);
            yield return new WaitForSeconds(3f);
            string st1 = wa.CameraStateNow; float h1 = wa.CameraHeightNow, r1 = cam.currentState.right, d1 = cam.currentState.defaultDistance;
            var cp1 = cc.transform.InverseTransformPoint(cam.transform.position); float my1 = MouseY(cam);
            Diag("end", cc, cam, wa); tp.lockCameraInput = lock0;
            Check("strafe restored after overlapping aims", cc.isStrafing == s0 && !wa.Aiming && ASHStrafeLock.Owners(cc) == 0, $"strafing {s0}->{cc.isStrafing} aim {wa.Aiming} owners {ASHStrafeLock.Owners(cc)}");
            Check("camera back on its baseline state", st1 == st0 && st1 != ASHWeaponAim.AimCameraState, $"state {st0} -> {st1}");
            Check("camera height and offset back to baseline (no creep)", Mathf.Abs(h1 - h0) < 0.02f && Mathf.Abs(r1 - r0) < 0.02f && Mathf.Abs(d1 - d0) < 0.05f && Mathf.DeltaAngle(my0, my1) < 1f, // no upward creep (Invector eases free-look pitch back down on its own)
                $"height {h0:F3}->{h1:F3} (peak while aiming {maxH:F2}) right {r0:F2}->{r1:F2} dist {d0:F2}->{d1:F2} pitch {my0:F1}->{my1:F1} (cam local y {cp0.y:F2}->{cp1.y:F2}, varies with collision pull-in)");
            Finish();
        }

        static float MouseY(Component cam)
        {
            var f = cam.GetType().GetField("_mouseY", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return f != null ? (float)f.GetValue(cam) : float.NaN;
        }
        static void Diag(string tag, vThirdPersonController cc, Invector.vCamera.vThirdPersonCamera cam, ASHWeaponAim wa)
        {
            var cs = cam.currentState; var cp = cc.transform.InverseTransformPoint(cam.transform.position);
            Debug.Log($"[ASH] CAMSWITCH DIAG {tag}: state={wa.CameraStateNow} mouseY={MouseY(cam):F1} camEulerX={cam.transform.eulerAngles.x:F1} ccEuler=({cc.transform.eulerAngles.x:F1},{cc.transform.eulerAngles.z:F1}) h={cs.height:F2} yLim={cs.yMinLimit:F0}..{cs.yMaxLimit:F0} camLocalY={cp.y:F2} strafe={cc.isStrafing}");
        }

        void Finish() { Debug.Log($"[ASH] CAMSWITCH SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}
