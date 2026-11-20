using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>t338 idle arm jitter (exec ASH.DebugTools.ASHArmJitterTest.Run): empty hands, standing still, camera still.
    /// Measures the frame-to-frame jerk (2nd difference, rms / max mm) of both hands in chest space (arm-only motion),
    /// the chest in body space, and the hands in camera space, then repeats with each LateUpdate script on the
    /// character disabled in turn and with pose interpolation forced on. Logs "[ASH] ARMJ ...".</summary>
    public sealed class ASHArmJitterTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_ArmJitterTest").AddComponent<ASHArmJitterTest>(); }
        public static bool Quick; public static void RunQuick() { Quick = true; Run(); }
        readonly StringBuilder _sum = new StringBuilder();
        void Log(string l) { _sum.AppendLine(l); Debug.Log("[ASH] ARMJ " + l); }

        Animator _a; Camera _cam; Transform _lh, _rh, _chest, _root;

        IEnumerator Start()
        {
            yield return null;
            var eq = FindAnyObjectByType<ASHEquipment>(); var cc = FindAnyObjectByType<vThirdPersonController>(); _cam = Camera.main;
            _a = eq ? eq.Animator : null;
            if (!_a || !cc || !_cam) { Log("setup missing"); Finish(); yield break; }
            var tp = cc.GetComponent<vThirdPersonInput>(); bool lock0 = tp && tp.lockCameraInput; if (tp) tp.lockCameraInput = true;
            eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand);
            _lh = _a.GetBoneTransform(HumanBodyBones.LeftHand); _rh = _a.GetBoneTransform(HumanBodyBones.RightHand);
            _chest = _a.GetBoneTransform(HumanBodyBones.UpperChest); if (!_chest) _chest = _a.GetBoneTransform(HumanBodyBones.Chest); _root = _a.transform;
            yield return new WaitForSeconds(1.5f);
            Log($"animator {_a.updateMode}, fixedDt {Time.fixedDeltaTime:F4}");
            ASH.Combat.ASHAimCameraSync.ForceMode = 0; yield return new WaitForSeconds(0.5f); yield return Measure("interpolation OFF (old)");
            ASH.Combat.ASHAimCameraSync.ForceMode = 2; yield return new WaitForSeconds(0.5f); yield return Measure("interpolation ON (all states)");
            ASH.Combat.ASHAimCameraSync.ForceMode = 0;
            if (!Quick)
            {
                // every behaviour with a LateUpdate on the animator object and the player root, disabled one at a time
                var list = new List<Behaviour>();
                foreach (var go in new[] { _a.gameObject, cc.gameObject })
                    foreach (var b in go.GetComponents<MonoBehaviour>())
                    {
                        if (!b || !b.enabled || b == this) continue;
                        var t = b.GetType(); bool lu = false;
                        for (var tt = t; tt != null && tt != typeof(MonoBehaviour); tt = tt.BaseType)
                            if (tt.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null) { lu = true; break; }
                        if (lu && !list.Contains(b)) list.Add(b);
                    }
                var names = new StringBuilder(); foreach (var b in list) names.Append(b.GetType().Name).Append(", ");
                Log("LateUpdate scripts: " + names);
                foreach (var b in list)
                {
                    if (b is vThirdPersonInput) continue; // camera / input owner
                    var n = b.GetType().Name;
                    if (!(n.Contains("Aim") || n.Contains("Rig") || n.Contains("Pose") || n.Contains("Bow") || n.Contains("Chop"))) continue; // pose writers only
                    b.enabled = false; yield return new WaitForSeconds(0.4f);
                    yield return Measure("without " + b.GetType().Name);
                    b.enabled = true; yield return new WaitForSeconds(0.3f);
                }
            }
            if (tp) tp.lockCameraInput = lock0;
            Finish();
        }

        IEnumerator Measure(string label)
        {
            const int N = 300;
            var hl = new List<Vector3>(N); var hc = new List<Vector3>(N); var cb = new List<Vector3>(N); var hv = new List<Vector3>(N);
            float rt = Time.realtimeSinceStartup + 25f;
            for (int i = 0; i < N; i++)
            {
                yield return new WaitForEndOfFrame();
                if (Time.realtimeSinceStartup > rt) { Log(label + ": timed out (game view not rendering?)"); yield break; }
                hl.Add(_chest.InverseTransformPoint(_lh.position)); hc.Add(_chest.InverseTransformPoint(_rh.position));
                cb.Add(_root.InverseTransformPoint(_chest.position));
                hv.Add(_cam.transform.InverseTransformPoint(_rh.position));
            }
            Log($"{label}: L hand in chest {J(hl, 1f)}, R hand in chest {J(hc, 1f)}, chest-in-body {J(cb, 1f)}, right hand in view {J(hv, 1f)}");
        }

        static string J(List<Vector3> p, float k)
        {
            float mx = 0f, sum = 0f; int n = 0;
            for (int i = 1; i < p.Count - 1; i++)
            {
                var d = p[i + 1] - 2f * p[i] + p[i - 1];
                float j = d.magnitude * 1000f * k; mx = Mathf.Max(mx, j); sum += j * j; n++;
            }
            return $"rms {Mathf.Sqrt(sum / Mathf.Max(1, n)):F2} mm max {mx:F2}";
        }

        void Finish() { ASH.Combat.ASHAimCameraSync.ForceMode = -1; Quick = false; Debug.Log("[ASH] ARMJ SUMMARY\n" + _sum); Destroy(gameObject); }
    }
}
