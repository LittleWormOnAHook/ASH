using System.Collections;
using ASH.Survival;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>t346 (exec ASH.DebugTools.ASHT346Test.Run): wears a wet rag for a moment, screenshots the HUD to
    /// C:\Users\Teabagger\ashx\hud_rag.png, then restores the previous face state. Logs "[ASH] T346 ...".</summary>
    public sealed class ASHT346Test : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_T346Test").AddComponent<ASHT346Test>(); }
        IEnumerator Start()
        {
            var f = ASHFaceProtection.Instance;
            if (!f) { Debug.Log("[ASH] T346 no face protection"); Debug.Log("[ASH] T346 SUMMARY"); Destroy(gameObject); yield break; }
            string was = f.CaptureJson();
            f.RestoreJson("{\"worn\":1,\"ragHours\":4.15,\"filterHours\":0,\"dirty\":false}");
            yield return new WaitForSeconds(1f);
            ScreenCapture.CaptureScreenshot(@"C:\Users\Teabagger\ashx\hud_rag.png");
            yield return new WaitForSeconds(1f);
            f.RestoreJson(was);
            Debug.Log($"[ASH] T346 shot taken, worn {f.Worn} restored");
            Debug.Log("[ASH] T346 SUMMARY"); Destroy(gameObject);
        }
    }
}
