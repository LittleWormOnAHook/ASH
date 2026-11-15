using System.Collections;
using System.IO;
using ASH.Rendering;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace ASH.DebugTools
{
    /// <summary>Play-mode check for ASH Volumetric Fog: GPU time of the march/composite passes (Recorder, lightweight), one frame
    /// with fog and one without (AshBridge/volfog_on.png / volfog_off.png) and their mean difference. Logs "VOLFOG SUMMARY".</summary>
    public sealed class ASHVolFogTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_VolFogTest").AddComponent<ASHVolFogTest>(); }

        IEnumerator Start()
        {
            var v = VolumeManager.instance.stack.GetComponent<ASHVolumetricFog>();
            bool active = v != null && v.IsActive();
            var rm = Recorder.Get("ASH VolFog March"); var rc = Recorder.Get("ASH VolFog Composite"); rm.enabled = true; rc.enabled = true;
            // one temporary local fog area 8 m ahead of the camera (removed at the end)
            var cam = Camera.main; var areaGo = new GameObject("ASH_TestFogArea");
            if (cam) areaGo.transform.position = cam.transform.position + cam.transform.forward * 8f;
            var bc = areaGo.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(6f, 4f, 6f);
            var area = areaGo.AddComponent<ASHFogArea>(); area.localDensity = 0.15f; area.edgeFalloff = 1f;
            yield return new WaitForSeconds(2f);
            float gm = 0, gc = 0, ft = 0; int n = 0;
            for (int i = 0; i < 90; i++) { yield return null; if (i < 10) continue; gm += rm.gpuElapsedNanoseconds / 1e6f; gc += rc.gpuElapsedNanoseconds / 1e6f; ft += Time.unscaledDeltaTime * 1000f; n++; }
            gm /= n; gc /= n; ft /= n;
            yield return new WaitForEndOfFrame();
            var on = ScreenCapture.CaptureScreenshotAsTexture();
            ASHVolumetricFogFeature.DebugDisable = true;
            float ft2 = 0; for (int i = 0; i < 60; i++) { yield return null; if (i >= 10) ft2 += Time.unscaledDeltaTime * 1000f; } ft2 /= 50f;
            yield return new WaitForEndOfFrame();
            var off = ScreenCapture.CaptureScreenshotAsTexture();
            ASHVolumetricFogFeature.DebugDisable = false;
            var a = on.GetPixels(); var b = off.GetPixels(); double diff = 0; int nan = 0;
            for (int i = 0; i < a.Length; i += 7) { var d = a[i] - b[i]; diff += (Mathf.Abs(d.r) + Mathf.Abs(d.g) + Mathf.Abs(d.b)) / 3f; if (float.IsNaN(a[i].r)) nan++; }
            diff /= a.Length / 7;
            string dir = Path.Combine(Application.dataPath, "../AshBridge");
            File.WriteAllBytes(Path.Combine(dir, "volfog_on.png"), on.EncodeToPNG()); File.WriteAllBytes(Path.Combine(dir, "volfog_off.png"), off.EncodeToPNG());
            v.Resolve(out int steps, out bool half);
            bool renders = active && diff > 0.004; bool cheap = gm + gc < 3.0f || (gm + gc == 0 && ft - ft2 < 3f);
            Debug.Log($"[ASH] VOLFOG SUMMARY {(renders && nan == 0 ? "PASS" : "FAIL")} active={active} meanDiff={diff:F4} nan={nan} gpu march={gm:F2}ms composite={gc:F2}ms frame on={ft:F1}ms off={ft2:F1}ms steps={steps} half={half} areas={ASHFogArea.Active.Count} cost={(cheap ? "ok" : "HIGH")} res={on.width}x{on.height}");
            Destroy(areaGo); Destroy(on); Destroy(off); rm.enabled = false; rc.enabled = false; Destroy(gameObject);
        }
    }
}
