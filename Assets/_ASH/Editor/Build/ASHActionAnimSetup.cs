using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>
    /// ASH/Build/Action Animations: (re)builds the "ASH_Action" upper-body override layer on ASH_BasicLocomotion with
    /// clips copied from Genesis into Assets/_ASH/Animation/Imported (Villager Axe-Chopping02 + Malbers Axe_2Hand Swing_Right; Invector Shooter archery,
    /// upper-body aim poses and shots). Bow: Mixamo longbow draw / aim-idle hold / recoil from Genesis (bow in the left hand, right hand draws to the cheek). Safe to re-run.
    /// Full-body layer ASH_ActionFull (no mask): Empty, Chop, ChopHigh (speed 0, driven by ASHChopAnimator).
    /// Upper-body ASH_Action states: Empty, BowDraw (speed 0, driven by the draw), BowHold (loop), BowShot, RifleAim, RifleShot, PistolAim, PistolShot.
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
            var chop = Clip("Chop/Villager@Axe-Chopping02.fbx", null); var chopHigh = Clip("Chop/Malbers_Axe2H_SwingRight.fbx", null);
            // bow: Mixamo longbow set copied from Genesis (Animations/Mixamo Animations/Bow and Arrow) into Imported/Bow with new GUIDs
            var bowDraw = Clip("Bow/Mixamo_BowDraw.fbx", null); var bowHold = Clip("Bow/Mixamo_BowAimIdle.fbx", "mixamo.com"); var bowShot = Clip("Bow/Mixamo_BowRecoil.fbx", null);
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
            S("BowDraw", bowDraw, false, 0f, new Vector3(500, -60)); /* speed 0: scrubbed by the draw (RMB down) */
            S("BowHold", bowHold, false, 1f, new Vector3(500, 0)); /* looping hold at full draw while RMB stays down */
            S("BowShot", bowShot, false, 1f, new Vector3(750, 0)); /* release on LMB */
            var ra = S("RifleAim", rAim, false, 1f, new Vector3(500, 120)); var rs = S("RifleShot", rShot, false, 1f, new Vector3(750, 120));
            var pa = S("PistolAim", pAim, false, 1f, new Vector3(500, 240)); var ps = S("PistolShot", pShot, false, 1f, new Vector3(750, 240));
            void Back(AnimatorState from, AnimatorState to) { var t = from.AddTransition(to); t.hasExitTime = true; t.exitTime = 0.95f; t.duration = 0.1f; }
            Back(rs, ra); Back(ps, pa);
            // full-body chop layer (no mask): the axe clips bend knees and hips into the swing
            int fe = System.Array.FindIndex(c.layers, l => l.name == ASH.Gameplay.ASHActionLayer.FullLayerName);
            if (fe >= 0) c.RemoveLayer(fe);
            c.AddLayer(ASH.Gameplay.ASHActionLayer.FullLayerName);
            var fl = c.layers; var F = fl[fl.Length - 1]; F.avatarMask = null; F.defaultWeight = 0f; F.iKPass = false; F.blendingMode = AnimatorLayerBlendingMode.Override; c.layers = fl;
            var fsm = c.layers[c.layers.Length - 1].stateMachine;
            AnimatorState FS(string n, Motion m, Vector3 pos) { var s = fsm.AddState(n, pos); s.motion = m; s.speed = m ? 0f : 1f; s.writeDefaultValues = true; return s; }
            fsm.defaultState = FS("Empty", null, new Vector3(250, 0));
            FS("Chop", chop, new Vector3(250, 80));          /* overhead chop down to a log / stump / downed tree (speed 0, driven by ASHChopAnimator) */
            FS("ChopHigh", chopHigh, new Vector3(250, 160)); /* level swing into a standing trunk */
            sb.Append($"full layer {F.name} at index {c.layers.Length - 1} states {fsm.states.Length}; ");
            EditorUtility.SetDirty(c); AssetDatabase.SaveAssets();
            sb.Append($"layer {LayerName} at index {System.Array.FindIndex(c.layers, l => l.name == LayerName)} mask={(L.avatarMask ? L.avatarMask.name : "none")} states {sm.states.Length}");
            return sb.ToString();
        }
    }
}
