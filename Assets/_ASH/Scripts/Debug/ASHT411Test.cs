using System.Collections;
using ASH.Gameplay;
using ASH.Survival;
using ASH.UI;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Play Mode, exec ASH.DebugTools.ASHT411Test.Run: t411 gold compass dot for the tracked main objective (bearing check at two
    /// headings) + t412 stamina exertion drain. Logs "[ASH] T411 ...". Restores what it touches; does not save.</summary>
    public sealed class ASHT411Test : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_T411Test").AddComponent<ASHT411Test>(); }
        static void L(string m) => Debug.Log("[ASH] T411 " + m);
        IEnumerator Start()
        {
            yield return null;
            var tr = FindAnyObjectByType<ASHObjectiveTracker>(); var c = ASHCompass.Instance; var S = ASHCompassSettings.I;
            if (!tr || !c) { L($"FAIL setup tracker={(bool)tr} compass={(bool)c}"); Destroy(gameObject); yield break; }
            bool alwaysWas = S.compassAlwaysOn; bool visWas = S.compassAlwaysVisible;
            if (!c.IsVisible) { S.compassAlwaysOn = true; S.compassAlwaysVisible = true; }
            var stageWas = tr.Current; var trackedWas = tr.TrackedId;
            tr.DevComplete(ObjectiveStageTravel()); if (tr.TrackedId != "slice") tr.Track("slice");
            float w = 0f; while (w < 0.6f) { w += Time.unscaledDeltaTime; yield return null; }
            var cam = Camera.main; var tpc = Invector.vCamera.vThirdPersonCamera.instance; bool tpcWas = tpc && tpc.enabled; if (tpc) tpc.enabled = false;
            string id = ASHCompass.AutoObjective + "slice"; float maxErr = 0f; string got = "";
            foreach (float yaw in new[] { 0f, 60f, -120f })
            {
                if (cam) cam.transform.rotation = Quaternion.Euler(0f, yaw + (tr.Overlook ? Bearing(c.DebugPlayerPos, tr.Overlook.position) : 0f), 0f);
                yield return null; yield return new WaitForEndOfFrame();
                var x = c.MarkerX(id);
                float half = Mathf.Max(1f, S.fieldOfViewDegrees * 0.5f);
                float delta = tr.Overlook ? Mathf.DeltaAngle(cam.transform.eulerAngles.y, Bearing(c.DebugPlayerPos, tr.Overlook.position)) : 0f;
                bool off = Mathf.Abs(delta) > half; if (off) delta = Mathf.Sign(delta) * half;
                float want = delta / half * c.HalfStripWidth;
                got += $" yaw{yaw:+0;-0}:{(x.HasValue ? x.Value.ToString("0") : "none")}/want{want:0}{(off ? "(edge)" : "")}";
                maxErr = Mathf.Max(maxErr, x.HasValue ? Mathf.Abs(x.Value - want) : 9999f);
            }
            var col = c.MarkerColor(id);
            L((maxErr < 6f && col.HasValue && col.Value.r > 0.9f && col.Value.g > 0.7f && col.Value.b < 0.35f ? "PASS" : "FAIL") + $" gold dot at the tracked main objective bearing (Overlook){got} maxErr={maxErr:0.0}px color={col}");
            tr.Track(null); yield return null; tr.DevComplete(ObjectiveStageGather());
            w = 0f; while (w < 0.5f) { w += Time.unscaledDeltaTime; yield return null; }
            L((c.MarkerX(id) == null ? "PASS" : "FAIL") + " no dot while the tracked step has no world target (Gather)");
            if (tpc) tpc.enabled = tpcWas;
            tr.DevComplete(stageWas); if (tr.TrackedId != trackedWas) tr.Track(trackedWas);
            S.compassAlwaysOn = alwaysWas; S.compassAlwaysVisible = visWas;

            // t412 exertion
            var st = FindAnyObjectByType<ASHSurvivalStats>();
            if (st)
            {
                float h0 = st.ExertionHunger, t0 = st.ExertionThirst;
                st.ApplyExertion(100f); // one full stamina bar
                float dh = st.ExertionHunger - h0, dt = st.ExertionThirst - t0;
                L((dt > dh && dh > 0f ? "PASS" : "FAIL") + $" full stamina bar spent -> hunger -{dh:0.00} thirst -{dt:0.00} (per point {st.Tuning.staminaHungerPerPoint:0.000}/{st.Tuning.staminaThirstPerPoint:0.000})");
                float r0 = st.ExertionThirst; st.SetStaminaFromMotor(st.MaxStamina, st.MaxStamina); st.SetStaminaFromMotor(st.MaxStamina, st.MaxStamina);
                L((Mathf.Approximately(r0, st.ExertionThirst) ? "PASS" : "FAIL") + " regen / refill does not drain");
                float s0 = st.ExertionThirst; float sw = 0f; while (sw < 6f) { sw += Time.deltaTime; yield return null; }
                L($"INFO live exertion over 6 s idle: thirst -{st.ExertionThirst - s0:0.000}");
            }
            L("DONE");
            Destroy(gameObject);
        }
        static ASHObjectiveTracker.Stage ObjectiveStageTravel() => ASHObjectiveTracker.Stage.Travel;
        static ASHObjectiveTracker.Stage ObjectiveStageGather() => ASHObjectiveTracker.Stage.Gather;
        static float Bearing(Vector3 a, Vector3 b) { var d = b - a; d.y = 0f; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
    }
}
