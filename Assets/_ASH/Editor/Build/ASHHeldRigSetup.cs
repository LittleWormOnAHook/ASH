using System.IO;
using System.Linq;
using System.Text;
using ASH.DebugTools;
using ASH.Gameplay;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>ASH/Build/Held Item Rig: adds the item-driven-hands hierarchy to the player prefab from AshBridge/heldrig_capture.json
    /// (made by ASHHeldRigCapture in Play Mode): ASH_AimPivot / ASH_BodyPivot under the player root with AimAnchor_X / UseAnchor_X,
    /// and Grip_R / Grip_L / String_R / Stock under each Held_X item, plus the ASHHeldRig component. Only MISSING transforms are
    /// added; existing ones are never moved. Logs every hierarchy path.</summary>
    public static class ASHHeldRigSetup
    {
        const string PlayerPrefab = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";

        [MenuItem("ASH/Build/Held Item Rig")]
        public static void Run()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../AshBridge/heldrig_capture.json"));
            if (!File.Exists(path)) { Debug.LogWarning("[ASH] HELDRIG no capture: run ASH.DebugTools.ASHHeldRigCapture.Run in Play Mode first"); return; }
            var cap = JsonUtility.FromJson<ASHHeldRigCapture.Capture>(File.ReadAllText(path));
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            var sb = new StringBuilder(); int added = 0, kept = 0;
            try
            {
                var anim = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(x => x.isHuman) ?? root.GetComponentInChildren<Animator>(true);
                if (anim && !anim.GetComponent<ASHHeldRig>()) { anim.gameObject.AddComponent<ASHHeldRig>(); sb.Append("added ASHHeldRig component\n"); }
                Transform Pivot(string n) { var t = root.transform.Find(n); if (!t) { t = new GameObject(n).transform; t.SetParent(root.transform, false); added++; } return t; }
                var aimP = Pivot(ASHHeldRig.AimPivotName); var bodyP = Pivot(ASHHeldRig.BodyPivotName);
                foreach (var e in cap.items)
                {
                    var item = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == e.visual);
                    foreach (var p in e.poses)
                    {
                        Transform parent = p.parent == ASHHeldRig.AimPivotName ? aimP : p.parent == ASHHeldRig.BodyPivotName ? bodyP : item;
                        if (!parent) { sb.Append($"  {e.item}: no {p.parent} in the prefab\n"); continue; }
                        var t = parent.Find(p.name);
                        if (t) kept++;
                        else { t = new GameObject(p.name).transform; t.SetParent(parent, false); t.localPosition = p.p; t.localRotation = p.r; added++; }
                        sb.Append($"  {e.item}: {AnimationUtility.CalculateTransformPath(t, root.transform)}\n");
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log($"[ASH] HELDRIG built: added {added} kept {kept}\n{sb}");
        }
    }
}
