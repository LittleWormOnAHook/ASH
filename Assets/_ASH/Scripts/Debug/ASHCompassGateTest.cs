using System.Collections;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Play Mode, exec ASH.DebugTools.ASHCompassGateTest.Run: compass hidden before the Compass item is used, shown after,
    /// item consumed. Logs "[ASH] COMPASSGATE ...". Does not save.</summary>
    public sealed class ASHCompassGateTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_CompassGateTest").AddComponent<ASHCompassGateTest>(); }
        IEnumerator Start()
        {
            yield return null;
            var hud = FindAnyObjectByType<ASHHud>(); var inv = FindAnyObjectByType<ASHInventory>(); var c = ASHCompass.Instance;
#if UNITY_EDITOR
            var it = UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_Compass.asset");
#else
            ASHItemDefinition it = null;
#endif
            float A() { var g = c ? c.GetComponentInChildren<CanvasGroup>(true) : null; return g ? g.alpha : -1f; }
            Debug.Log($"[ASH] COMPASSGATE before: unlocked {ASHCompassUnlock.Unlocked} alwaysOn {ASHCompassSettings.I.compassAlwaysOn} alpha {A():F2} hud {(bool)hud} item {(bool)it}");
            if (!hud || !inv || !it) { Destroy(gameObject); yield break; }
            inv.AddItem(it, 1); yield return null;
            bool used = hud.UseItem(it); yield return null; yield return null;
            Debug.Log($"[ASH] COMPASSGATE after use: used {used} unlocked {ASHCompassUnlock.Unlocked} alpha {A():F2} count {inv.Count(it)} save {FindAnyObjectByType<ASHCompassUnlock>()?.CaptureJson()}");
            Destroy(gameObject);
        }
    }
}