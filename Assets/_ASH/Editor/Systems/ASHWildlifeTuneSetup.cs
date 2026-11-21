using ASH.Wildlife;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: writes this pass's per-species defaults (shot counts, stand-off, personality ranges) into ASH_WildlifeSettings.</summary>
    public static class ASHWildlifeTuneSetup
    {
        [MenuItem("ASH/Systems/Wildlife/Apply Wildlife Tuning (idempotent)", false, 260)]
        public static string Apply()
        {
            var s = Resources.Load<ASHWildlifeSettings>("ASH_WildlifeSettings");
            if (!s) { Debug.LogWarning("[ASH] ASH_WildlifeSettings missing; run Setup Wildlife first."); return "missing"; }
            s.EnsureDefaults();
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssetIfDirty(s);
            string msg = $"[ASH] Wildlife tuning: speedMul={s.speedMul} shots deer={s.deer.bodyShotsToKill} wolf={s.wolf.bodyShotsToKill} cougar={s.cougar.bodyShotsToKill} bear={s.bear.bodyShotsToKill} boar={s.boar.bodyShotsToKill}";
            Debug.Log(msg);
            return msg;
        }
    }
}
