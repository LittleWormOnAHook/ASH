using System.Collections;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>t354 check (Play): shows the saving icon and writes a Game view screenshot to C:\Users\Teabagger\ashx\save_icon.png.</summary>
    public sealed class ASHT354Test : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_T354Test").AddComponent<ASHT354Test>(); }
        IEnumerator Start()
        {
            ASH.Core.ASHSaveSystem.PreviewSavingIcon(4f);
            yield return new WaitForSecondsRealtime(0.6f);
            ScreenCapture.CaptureScreenshot(@"C:\Users\Teabagger\ashx\save_icon.png");
            yield return new WaitForSecondsRealtime(0.5f);
            var go = GameObject.Find("Saving"); var rt = go ? go.GetComponent<RectTransform>() : null;
            Debug.Log(rt ? $"[ASH] T354 icon active={go.activeInHierarchy} anchor={rt.anchorMin} pos={rt.anchoredPosition} size={rt.sizeDelta} screen={RectTransformUtility.WorldToScreenPoint(null, rt.position)} of {Screen.width}x{Screen.height}" : "[ASH] T354 icon not found");
            Destroy(gameObject);
        }
    }
}
