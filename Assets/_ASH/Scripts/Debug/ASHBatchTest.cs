using System.Collections;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>One combined Play-mode pass (exec ASH.DebugTools.ASHBatchTest.Run): jitter, camera stop, then the Keep-Play-Changes
    /// probe, one after another in a single Play session. Logs "[ASH] BATCH SUMMARY" when all three finished.</summary>
    public sealed class ASHBatchTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_BatchTest").AddComponent<ASHBatchTest>(); }
        string _seen = "";
        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }
        void OnLog(string m, string st, LogType t) { if (m.Contains(" SUMMARY")) _seen += m.Split('\n')[0] + " | "; }
        IEnumerator WaitFor(string tag, float max) { float t = 0f; while (!_seen.Contains(tag) && t < max) { t += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator Start()
        {
            yield return null;
            ASHJitterTest.Run(); yield return WaitFor("JITTER SUMMARY", 120f); yield return new WaitForSeconds(1f);
            ASHCameraStopTest.Run(); yield return WaitFor("CAMSTOP SUMMARY", 120f); yield return new WaitForSeconds(1f);
            ASHPersistProbe.Run(); yield return new WaitForSeconds(1f);
            ASHColliderFadeProbe.Run(); yield return WaitFor("CFADE SUMMARY", 60f);
            Debug.Log("[ASH] BATCH SUMMARY done: " + _seen);
            Destroy(gameObject);
        }
    }
}
