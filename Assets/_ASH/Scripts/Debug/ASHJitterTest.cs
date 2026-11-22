using System.Collections;
using System.Collections.Generic;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using Invector.vCamera;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Held-item jitter at normal speed (Play Mode, exec ASH.DebugTools.ASHJitterTest.Run): holds each weapon/item in its
    /// idle/aim pose with the player standing still and measures the frame-to-frame jerk (second difference) of the held visual in
    /// camera space over ~2 s of rendered frames. Logs "[ASH] JITTER ..." + SUMMARY, plus timeScale / animator update mode /
    /// body interpolation / camera late follow.</summary>
    public sealed class ASHJitterTest : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHJitterTest()
        {
            RangedOnly = default;
            PerFrame = -1;
            SpinInput = default;
            Frames = 150;
            MaxJerkMm = 6f;
            MaxRotJerkDeg = 1.0f;
        }

        public static void Run() { if (Application.isPlaying) new GameObject("ASH_JitterTest").AddComponent<ASHJitterTest>(); }
        /// <summary>Ranged-only comparison: per-frame aim IK forced on / off (restored after), camera still or turning at SpinInput per frame.</summary>
        public static bool RangedOnly; public static int PerFrame = -1; public static float SpinInput;
        public static void RunRangedFixed() { RangedOnly = true; PerFrame = 0; SpinInput = 0f; Run(); }
        public static void RunRangedPerFrame() { RangedOnly = true; PerFrame = 1; SpinInput = 0f; Run(); }
        public static void RunRangedFixedSpin() { RangedOnly = true; PerFrame = 0; SpinInput = 0.6f; Run(); }
        public static void RunRangedPerFrameSpin() { RangedOnly = true; PerFrame = 1; SpinInput = 0.6f; Run(); }
        /// <summary>t336: aimed spin with ASHAimCameraSync forced off / on (prev = 1 uses the previous-frame camera for the solve, 0 this frame's).</summary>
        public static void RunSpinSyncOff() { ASH.Combat.ASHAimCameraSync.ForceMode = 0; RunRangedFixedSpin(); }
        public static void RunSpinSyncOn() { ASH.Combat.ASHAimCameraSync.ForceMode = 1; RunRangedFixedSpin(); }
        /// <summary>Longer sample (frames per weapon).</summary>
        public static int Frames = 150;
        public static void RunSpinSyncOffLong() { Frames = 600; RunSpinSyncOff(); }
        public static void RunSpinSyncOnLong() { Frames = 600; RunSpinSyncOn(); }
        bool _pfWas; bool _pfSet;
        public static float MaxJerkMm = 6f, MaxRotJerkDeg = 1.0f;
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"JITTER {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }
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
            var cc = FindAnyObjectByType<vThirdPersonController>(); var cam = Camera.main; var rb = cc ? cc.GetComponent<Rigidbody>() : null;
            var guard = eq && eq.Animator ? eq.Animator.GetComponent("ASHPoseGuard") : null;
            Check("setup", inv && eq && input && cc && cam, $"timeScale {Time.timeScale:F2} animator {(eq && eq.Animator ? eq.Animator.updateMode.ToString() : "-")} body interpolation {(rb ? rb.interpolation.ToString() : "-")} camera late follow {vThirdPersonCamera.DMFollowInLateUpdate} pose guard {(bool)guard}");
            if (!(inv && eq && input && cc && cam)) { Finish(); yield break; }
            Check("normal speed", Mathf.Approximately(Time.timeScale, 1f) && Time.captureDeltaTime == 0f, $"timeScale {Time.timeScale} captureDeltaTime {Time.captureDeltaTime}");
            var tp = cc.GetComponent<vThirdPersonInput>(); bool lock0 = tp && tp.lockCameraInput; if (tp) tp.lockCameraInput = true;
            input.SimSecondaryHeld = false; eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand);
            var cases = new (string item, bool aim, bool off)[] { ("HuntingRifle", true, false), ("Pistol", true, false), ("WoodenBow", true, false), ("Hatchet", false, false), ("Torch", false, true), ("Flashlight", true, true) };
            if (RangedOnly) cases = new (string item, bool aim, bool off)[] { ("HuntingRifle", true, false), ("Pistol", true, false), ("WoodenBow", true, false) };
            var tun = ASH.Combat.ASHCombatTuning.Live;
            if (PerFrame >= 0) { _pfWas = tun.perFrameAimIK; _pfSet = true; tun.perFrameAimIK = PerFrame == 1; }
            Check("mode", true, $"perFrameAimIK={tun.perFrameAimIK} spin={SpinInput} fixedDt={Time.fixedDeltaTime:F4} vSync={QualitySettings.vSyncCount} targetFps={Application.targetFrameRate}");
            var ammo = new[] { Item("RifleRounds"), Item("PistolRounds"), Item("WoodenArrow") };
            foreach (var a in ammo) if (a && inv.Count(a) < 5) inv.AddItem(a, 5);
            foreach (var c in cases)
            {
                var it = Item(c.item); if (!it) { Check(c.item, false, "item missing"); continue; }
                if (inv.Count(it) == 0) inv.AddItem(it, 1);
                eq.Equip(it); yield return new WaitForSeconds(0.4f);
                if (c.aim) input.SimSecondaryHeld = true;
                yield return new WaitForSeconds(1.6f);
                var v = c.off ? eq.OffVisual : eq.MainVisual;
                var ps = new List<Vector3>(); var rs = new List<Quaternion>(); int frames = 0, restored0 = Restored(guard); float t0 = Time.unscaledTime;
                while (frames < Frames && v)
                {
                    if (SpinInput != 0f && tp && tp.tpCamera) tp.tpCamera.RotateCamera(SpinInput, 0f);
                    yield return new WaitForEndOfFrame();
                    ps.Add(cam.transform.InverseTransformPoint(v.transform.position)); rs.Add(Quaternion.Inverse(cam.transform.rotation) * v.transform.rotation); frames++;
                }
                float fps = frames / Mathf.Max(0.001f, Time.unscaledTime - t0);
                float maxJ = 0f, sum = 0f, maxR = 0f;
                for (int i = 1; i < ps.Count - 1; i++)
                {
                    float j = (ps[i + 1] - 2f * ps[i] + ps[i - 1]).magnitude * 1000f; maxJ = Mathf.Max(maxJ, j); sum += j * j;
                    float r1 = Quaternion.Angle(rs[i - 1], rs[i]), r2 = Quaternion.Angle(rs[i], rs[i + 1]); maxR = Mathf.Max(maxR, Mathf.Abs(r2 - r1));
                }
                float rms = ps.Count > 2 ? Mathf.Sqrt(sum / (ps.Count - 2)) : 0f;
                Check($"{c.item} {(c.aim ? "aim" : "held")} steady", v && maxJ < MaxJerkMm && maxR < MaxRotJerkDeg,
                    $"{frames} frames at {fps:F0} fps: jerk max {maxJ:F2} mm rms {rms:F2} mm, rot jerk max {maxR:F2} deg; pose guard restored {(guard ? Restored(guard) - restored0 : -1)} bones");
                input.SimSecondaryHeld = false; yield return new WaitForSeconds(0.3f);
                eq.Unequip(c.off ? ASHEquipSlot.OffHand : ASHEquipSlot.MainHand); yield return null;
            }
            if (tp) tp.lockCameraInput = lock0;
            Finish();
        }

        static int Restored(Component g) { var p = g ? g.GetType().GetProperty("RestoredTotal") : null; return p != null ? (int)p.GetValue(g) : 0; }

        void Finish() { ASH.Combat.ASHAimCameraSync.ForceMode = -1; Frames = 150; if (_pfSet) ASH.Combat.ASHCombatTuning.Live.perFrameAimIK = _pfWas; RangedOnly = false; PerFrame = -1; SpinInput = 0f; Debug.Log($"[ASH] JITTER SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}
