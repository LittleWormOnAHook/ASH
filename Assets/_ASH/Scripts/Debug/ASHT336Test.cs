using System.Collections;
using System.Text;
using ASH.Companion;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>t335/t336/t337 Play-mode checks (exec ASH.DebugTools.ASHT336Test.Run): bow aim IK probe (how the ASH_IK_Bow
    /// StandingAiming left-hand offset axes move the bow in camera space; values restored), zoom restore after aiming,
    /// dog call-then-move (walk/run clips play), turn blend, paw steps. Logs "[ASH] T336 ..." and "T336 SUMMARY".</summary>
    public sealed class ASHT336Test : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHT336Test()
        {
            SkipBow = default;
            SkipZoom = default;
            SkipDog = default;
        }

        public static void Run() { if (Application.isPlaying) new GameObject("ASH_T336Test").AddComponent<ASHT336Test>(); }
        public static bool SkipBow, SkipZoom, SkipDog;
        public static void RunDogOnly() { SkipBow = true; SkipZoom = true; Run(); }
        public static void RunBowOnly() { SkipDog = true; SkipZoom = true; Run(); }
        readonly StringBuilder _sum = new StringBuilder(); int _pass, _fail;
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"T336 {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }
        void Info(string n, string d) { var l = $"T336 INFO {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }

        static ASHItemDefinition Item(string file)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"Assets/_ASH/Data/Items/ASH_Item_{file}.asset");
#else
            return null;
#endif
        }

        ASHInventory _inv; ASHEquipment _eq; ASHInput _input; vThirdPersonController _cc; Camera _cam; Animator _anim;

        IEnumerator Start()
        {
            yield return null;
            _inv = FindAnyObjectByType<ASHInventory>(); _eq = FindAnyObjectByType<ASHEquipment>(); _input = FindAnyObjectByType<ASHInput>();
            _cc = FindAnyObjectByType<vThirdPersonController>(); _cam = Camera.main; _anim = _eq ? _eq.Animator : null;
            if (!(_inv && _eq && _input && _cc && _cam)) { Check("setup", false, "missing player parts"); Finish(); yield break; }
            if (!SkipBow) yield return Bow();
            if (!SkipZoom) yield return Zoom();
            if (!SkipDog) yield return Dog();
            Finish();
        }

        // ------------------------------------------------------------------ bow probe
        Vector3 _lh, _rh; float _yaw;
        IEnumerator Measure()
        {
            Vector3 l = Vector3.zero, r = Vector3.zero; int n = 0;
            var lt = _anim.GetBoneTransform(HumanBodyBones.LeftHand); var rt = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            for (int i = 0; i < 30; i++) { yield return new WaitForEndOfFrame(); l += _cam.transform.InverseTransformPoint(lt.position); r += _cam.transform.InverseTransformPoint(rt.position); n++; }
            _lh = l / n; _rh = r / n; var d = _lh - _rh; _yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        IEnumerator Bow()
        {
#if UNITY_EDITOR
            var ik = UnityEditor.AssetDatabase.LoadAssetAtPath<Invector.vShooter.vWeaponIKAdjust>("Assets/_ASH/Data/Combat/IK/ASH_IK_Bow.asset");
            var bow = Item("WoodenBow"); var arrows = Item("WoodenArrow");
            if (!ik || !bow || !_anim) { Check("bow probe", false, "IK asset / bow / animator missing"); yield break; }
            var adj = ik.ikAdjustsLeft.Find(a => a.name == "StandingAiming");
            if (adj == null) { Check("bow probe", false, "no StandingAiming left adjust"); yield break; }
            if (arrows && _inv.Count(arrows) < 5) _inv.AddItem(arrows, 5);
            if (_inv.Count(bow) == 0) _inv.AddItem(bow, 1);
            _eq.Equip(bow); yield return new WaitForSeconds(0.5f);
            _input.SimSecondaryHeld = true; yield return new WaitForSeconds(2.5f);
            var p0 = adj.weaponHandOffset.position; var e0 = adj.weaponHandOffset.eulerAngles;
            yield return Measure(); var bl = _lh; var br = _rh; float by = _yaw;
            Info("bow base", $"offset pos {p0} euler {e0}; cam-space left hand {bl.ToString("F3")} right hand {br.ToString("F3")} arrow yaw {by:F1}");
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            string[] an = { "x", "y", "z" };
            for (int i = 0; i < 3; i++)
            {
                adj.weaponHandOffset.position = p0 + axes[i] * 0.05f; yield return new WaitForSeconds(0.8f); yield return Measure();
                Info($"bow pos {an[i]}+0.05", $"left hand d {(_lh - bl).ToString("F3")} right hand d {(_rh - br).ToString("F3")} yaw d {Mathf.DeltaAngle(by, _yaw):F1}");
                adj.weaponHandOffset.position = p0;
            }
            for (int i = 0; i < 3; i++)
            {
                adj.weaponHandOffset.eulerAngles = e0 + axes[i] * 7f; yield return new WaitForSeconds(0.8f); yield return Measure();
                Info($"bow euler {an[i]}+7", $"left hand d {(_lh - bl).ToString("F3")} right hand d {(_rh - br).ToString("F3")} yaw d {Mathf.DeltaAngle(by, _yaw):F1}");
                adj.weaponHandOffset.eulerAngles = e0;
            }
            adj.weaponHandOffset.position = p0; adj.weaponHandOffset.eulerAngles = e0;
            Check("bow probe", true, "offsets restored");
            _input.SimSecondaryHeld = false; yield return new WaitForSeconds(0.4f); _eq.Unequip(ASHEquipSlot.MainHand); yield return new WaitForSeconds(0.3f);
#else
            yield break;
#endif
        }

        // ------------------------------------------------------------------ zoom restore
        IEnumerator Zoom()
        {
            var ii = _cc.GetComponent<ASHInvectorInput>(); var cam = ii ? ii.tpCamera : null;
            var pistol = Item("Pistol");
            if (!ii || cam == null || !pistol) { Check("zoom restore", false, "input / camera / pistol missing"); yield break; }
            var tp = ii; bool lockWas = tp.lockCameraInput; tp.lockCameraInput = false;
            for (int i = 0; i < 4; i++) { _input.SimZoomRaw = -120f; yield return new WaitForSeconds(0.15f); } // 4 wheel clicks out
            yield return new WaitForSeconds(1.2f);
            float before = cam.CurrentZoom, tBefore = ii.ZoomTarget;
            if (_inv.Count(pistol) == 0) _inv.AddItem(pistol, 1);
            _eq.Equip(pistol); yield return new WaitForSeconds(0.5f);
            _input.SimSecondaryHeld = true; yield return new WaitForSeconds(1.5f);
            float during = cam.CurrentZoom;
            _input.SimSecondaryHeld = false; yield return new WaitForSeconds(2.5f);
            float after = cam.CurrentZoom;
            Check("zoom restore", before > 3.8f && Mathf.Abs(after - before) < 0.25f, $"free {before:F2} (target {tBefore:F2}) -> aiming {during:F2} -> after {after:F2} (target {ii.ZoomTarget:F2})");
            _eq.Unequip(ASHEquipSlot.MainHand); tp.lockCameraInput = lockWas; yield return new WaitForSeconds(0.3f);
        }

        // ------------------------------------------------------------------ dog
        IEnumerator Dog()
        {
            var dog = ASHDogCompanion.Active; if (!dog) dog = FindAnyObjectByType<ASHDogCompanion>();
            if (!dog || !dog.Anim) { Check("dog", false, "no dog / animator"); yield break; }
            var a = dog.Anim; var S = ASHCompanionSettings.I;
            Info("dog sync", $"clip speeds walk {S.walkClipSpeed:F2} trot {S.trotClipSpeed:F2} run {S.runClipSpeed:F2}; paws {(dog.GetComponent<ASH.World.Accumulation.ASHDeformer>() ? dog.GetComponent<ASH.World.Accumulation.ASHDeformer>().points.Count : 0)}");
            dog.SetMode(ASHDogMode.Follow);
            var P = _cc.transform.position;
            // far away + out of sight, then Call (teleports)
            dog.transform.position = P + _cc.transform.forward * -40f + Vector3.up * 0.5f; yield return new WaitForSeconds(0.5f);
            bool called = dog.Call(); yield return new WaitForSeconds(0.6f);
            Check("dog call teleport", called && Vector3.Distance(dog.transform.position, P) < 8f, $"dist after call {Vector3.Distance(dog.transform.position, P):F1} m");
            // move the player away: the dog must follow with locomotion clips (walk/trot/run), not idle / Idle_2
            var rb = _cc.GetComponent<Rigidbody>();
            int steps0 = dog.PawSteps; float maxSpd = 0f, maxTurn = 0f; int locoFrames = 0, movingFrames = 0, happyMoving = 0; bool turnInPlace = false;
            for (int leg = 0; leg < 2; leg++)
            {
                var np = _cc.transform.position + Quaternion.Euler(0f, leg == 0 ? 0f : 150f, 0f) * _cc.transform.forward * 12f;
                if (Physics.Raycast(np + Vector3.up * 20f, Vector3.down, out var h, 60f, ~0, QueryTriggerInteraction.Ignore)) np = h.point + Vector3.up * 0.05f;
                if (rb) { rb.position = np; rb.linearVelocity = Vector3.zero; } _cc.transform.position = np;
                float t0 = Time.time;
                while (Time.time - t0 < 4f)
                {
                    yield return null;
                    var st = a.GetCurrentAnimatorStateInfo(0);
                    bool moving = dog.ActualSpeed > 0.6f;
                    if (moving) { movingFrames++; if (st.IsName("Locomotion")) locoFrames++; if (st.IsName("Happy")) happyMoving++; }
                    maxSpd = Mathf.Max(maxSpd, dog.ActualSpeed); maxTurn = Mathf.Max(maxTurn, Mathf.Abs(a.GetFloat("Turn"))); turnInPlace |= dog.TurningInPlace;
                }
            }
            Check("dog locomotion after call", movingFrames > 20 && locoFrames >= movingFrames * 0.9f && happyMoving == 0,
                $"moving frames {movingFrames}, in Locomotion {locoFrames}, in Happy while moving {happyMoving}, max speed {maxSpd:F2} m/s, animator speed {a.speed:F2}");
            Check("dog turn blend", maxTurn > 0.2f, $"max |Turn| {maxTurn:F2}, turned in place {turnInPlace}");
            Check("dog paw steps", dog.PawSteps > steps0, $"{dog.PawSteps - steps0} footfalls");
            var field = FindAnyObjectByType<ASH.World.Accumulation.ASHDeformationField>();
            if (field) Info("deformation", $"active {field.Active}; strongest {field.DebugMax()} ; dog at {dog.transform.position}");
            if (rb) { rb.position = P; } _cc.transform.position = P; yield return new WaitForSeconds(0.5f);
        }

        void Finish() { SkipBow = SkipZoom = SkipDog = false; Debug.Log($"[ASH] T336 SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}
