using ASH.Core;
using UnityEngine;

namespace ASH.Wildlife.Birds
{
    /// <summary>Adds every bird type prefab (vendor rigs + the ASH crow) to the loading screen's off-screen pre-render.</summary>
    static class ASHBirdLoadingHook
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            ASHLoadingScreen.CollectPrewarm -= Collect;
            ASHLoadingScreen.CollectPrewarm += Collect;
        }

        static void Collect(System.Collections.Generic.List<GameObject> list)
        {
            var s = ASHBirdSettings.I;
            if (!s) return;
            if (s.birdPrefab) list.Add(s.birdPrefab);
            if (s.types != null) foreach (var t in s.types) if (t != null && t.prefab && !list.Contains(t.prefab)) list.Add(t.prefab);
            if (s.vendorMaterialTo != null && s.skyFlockPrefabs != null)
                foreach (var f in s.skyFlockPrefabs) if (f && !list.Contains(f)) list.Add(f);
        }
    }
}
