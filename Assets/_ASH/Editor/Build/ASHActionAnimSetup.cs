using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>
    /// ASH/Build/Action Animations: (re)builds the "ASH_Action" upper-body override layer on ASH_BasicLocomotion with
    /// clips copied from Genesis into Assets/_ASH/Animation/Imported (Villager Axe-Chopping01; Invector Shooter archery,
    /// upper-body aim poses and shots). Bow states use the original (unmirrored) Invector clips: bow in the left hand, right hand draws. Safe to re-run.
    /// States: Empty, Chop (speed 0, driven by ASHChopAnimator), BowAim, BowShot, RifleAim, RifleShot, PistolAim, PistolShot.
    /// </summary>
    public static class ASHActionAnimSetup
    {
        public const string ControllerPath = "Assets/_ASH/Animation/ASH_BasicLocomotion.controller";
        public const string ClipDir = "Assets/_ASH/Animation/Imported";
        const string LayerName = "ASH_Action";

        [MenuItem("ASH/Build/Action Animations (chop, bow, guns)", priority = 40)]
        public static void Run() { Debug.Log("[ASH] ActionAnim: " + Build()); }

        public static string Build()
        {
            var sb = new StringBuilder();
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!c) return "controller missing";
            AnimationClip Clip(string file, string name)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{ClipDir}/{file}"))
                    if (o is AnimationClip a && !a.name.StartsWith("__preview__") && (name == null || a.name == name)) { if (!a.humanMotion) sb.Append($"{a.name} not humanoid; "); return a; }
                sb.Append($"missing {file}:{name}; "); return null;
            }
            var chop = Clip("Villager@Axe-Chopping01.fbx", null);
            var bowAim = Clip("Shooter_Archery.fbx", "Bow_Aiming"); var bowShot = Clip("Shooter_Archery.fbx", "Bow_Shot");
            var rAim = Clip("Shooter_UpperBodyPoses.fbx", "Aiming@Rifle"); var pAim = Clip("Shooter_UpperBodyPoses.fbx", "Aiming@Pistol");
            var rShot = Clip("Shooter_Shot&Reload.fbx", "Shot_Rifle"); var pShot = Clip("Shooter_Shot&Reload.fbx", "Shot_Pistol");

            int existing = System.Array.FindIndex(c.layers, l => l.name == LayerName);
            if (existing >= 0) c.RemoveLayer(existing);
            var upper = c.layers.FirstOrDefault(l => l.name == "UpperBody");
            c.AddLayer(LayerName);
            var layers = c.layers; var L = layers[layers.Length - 1];
            L.avatarMask = upper != null ? upper.avatarMask : null; L.defaultWeight = 0f; L.iKPass = false; L.blendingMode = AnimatorLayerBlendingMode.Override;
            c.layers = layers;
            var sm = c.layers[c.layers.Length - 1].stateMachine;

            AnimatorState S(string n, Motion m, bool mirror, float speed, Vector3 pos)
            {
                var s = sm.AddState(n, pos); s.motion = m; s.mirror = mirror; s.speed = speed; s.writeDefaultValues = true; return s;
            }
            var empty = S("Empty", null, false, 1f, new Vector3(250, 0)); sm.defaultState = empty;
            S("Chop", chop, false, 0f, new Vector3(250, 80));
            var ba = S("BowAim", bowAim, false, 1f, new Vector3(500, 0)); var bs = S("BowShot", bowShot, false, 1f, new Vector3(750, 0));
            var ra = S("RifleAim", rAim, false, 1f, new Vector3(500, 120)); var rs = S("RifleShot", rShot, false, 1f, new Vector3(750, 120));
            var pa = S("PistolAim", pAim, false, 1f, new Vector3(500, 240)); var ps = S("PistolShot", pShot, false, 1f, new Vector3(750, 240));
            void Back(AnimatorState from, AnimatorState to) { var t = from.AddTransition(to); t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.1f; }
            Back(bs, ba); Back(rs, ra); Back(ps, pa);
            EditorUtility.SetDirty(c); AssetDatabase.SaveAssets();
            sb.Append($"layer {LayerName} at index {c.layers.Length - 1} mask={(L.avatarMask ? L.avatarMask.name : "none")} states 8");
            return sb.ToString();
        }
    }
}
