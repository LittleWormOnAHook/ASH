using System.Linq;
using ASH.Player;
using ASH.World.Accumulation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ASH.EditorTools.PlayerTools
{
    /// <summary>ASH/Player menus: foot IK + deformer on the ASH_Player prefab, using an ASH copy of the Invector animator (vendor asset untouched).</summary>
    public static class ASHPlayerTools
    {
        public const string PrefabPath = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        public const string ControllerCopy = "Assets/_ASH/Animation/ASH_BasicLocomotion.controller";

        /// <summary>exec ASH.EditorTools.PlayerTools.ASHPlayerTools.SetupFootIK</summary>
        [MenuItem("ASH/Player/Setup Foot IK + Ash Deformer", priority = 20)]
        public static void SetupFootIK()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var anim = root.GetComponent<Animator>(); if (!anim) { Debug.LogError("[ASH] ASH_Player has no Animator on the root."); return; }
                var src = anim.runtimeAnimatorController as AnimatorController;
                var srcPath = src ? AssetDatabase.GetAssetPath(src) : null;
                AnimatorController ac;
                if (srcPath == ControllerCopy) ac = src;
                else
                {
                    if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerCopy))
                    {
                        if (!AssetDatabase.IsValidFolder("Assets/_ASH/Animation")) AssetDatabase.CreateFolder("Assets/_ASH", "Animation");
                        if (!AssetDatabase.CopyAsset(srcPath, ControllerCopy)) { Debug.LogError($"[ASH] Could not copy {srcPath}"); return; }
                    }
                    ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerCopy);
                }
                var layers = ac.layers; layers[0].iKPass = true; ac.layers = layers; EditorUtility.SetDirty(ac); AssetDatabase.SaveAssetIfDirty(ac);
                anim.runtimeAnimatorController = ac;
                var ik = root.GetComponent<ASHFootIK>(); bool addedIk = !ik; if (!ik) ik = root.AddComponent<ASHFootIK>();
                var def = root.GetComponent<ASHDeformer>(); bool addedDef = !def; if (!def) def = root.AddComponent<ASHDeformer>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"[ASH] Foot IK setup: animator {ControllerCopy} (copied from {srcPath ?? "-"}, base layer IK Pass on), ASHFootIK {(addedIk ? "added" : "kept")}, ASHDeformer {(addedDef ? "added" : "kept")} on {PrefabPath}.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            foreach (var c in Object.FindObjectsByType<Invector.vCharacterController.vThirdPersonController>(FindObjectsInactive.Include))
            {
                var a = c.GetComponent<Animator>();
                var src = PrefabUtility.GetCorrespondingObjectFromSource(c.gameObject);
                Debug.Log($"[ASH] Scene player '{c.name}': prefab {(src ? AssetDatabase.GetAssetPath(src) : "none")}, controller {(a && a.runtimeAnimatorController ? AssetDatabase.GetAssetPath(a.runtimeAnimatorController) : "-")}, footIK {(bool)c.GetComponent<ASHFootIK>()}, deformer {(bool)c.GetComponent<ASHDeformer>()}");
            }
        }
    }
}
