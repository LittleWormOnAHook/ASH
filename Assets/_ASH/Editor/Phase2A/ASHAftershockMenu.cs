using ASH.Opening;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    static class ASHAftershockMenu
    {
        [MenuItem("ASH/Phase 2A/Debug/Force Aftershock", false, 30)]
        static void Force()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] Force Aftershock: enter Play Mode first"); return; }
            var s = ASHEruptionSequence.Instance ? ASHEruptionSequence.Instance : Object.FindAnyObjectByType<ASHEruptionSequence>();
            if (!s) { Debug.LogWarning("[ASH] Force Aftershock: no ASH_OpeningSequence in the scene"); return; }
            if (!s.ForceAftershock()) Debug.LogWarning("[ASH] Force Aftershock: a quake is already shaking (or no quake/profile)");
        }
    }
}
