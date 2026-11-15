using System.Linq;
using System.Text;
using ASH.Gameplay;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>One-off grip fixes from the recorded held-item audit (AshBridge/audit), approved by Anthony. Seats items in the palm
    /// using the hand frame from the prefab bones (along = wrist->middle knuckle, thumb = little->index knuckle, palm normal), which is
    /// fixed relative to the hand bone, so the result holds in every pose. Not a menu item and not part of any build: run on request only
    /// (exec ASH.EditorTools.Build.ASHHeldPlacementFix.Run). Logs before/after local poses.</summary>
    public static class ASHHeldPlacementFix
    {
        public static void Run() => Debug.Log("[ASH] HeldFix: " + Apply());

        /// <summary>Second audit pass (r3): the hanging lantern still met the relaxed left hand, so the hook goes a further 7 cm back
        /// and 2 cm out. Touches Holder_Hip only.</summary>
        public static void RunLantern()
        {
            var root = PrefabUtility.LoadPrefabContents(ASHPlayerBuilder.PrefabPath);
            try
            {
                var hook = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == ASHEquipment.HipHolderName);
                if (!hook) { Debug.Log("[ASH] HeldFix2: Holder_Hip missing"); return; }
                var b = $"pos{hook.localPosition:F3}";
                hook.position += root.transform.TransformDirection(new Vector3(-0.02f, 0f, -0.07f));
                PrefabUtility.SaveAsPrefabAsset(root, ASHPlayerBuilder.PrefabPath);
                Debug.Log($"[ASH] HeldFix2: Holder_Hip {b} -> pos{hook.localPosition:F3}; saved");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static string Apply()
        {
            var sb = new StringBuilder();
            var root = PrefabUtility.LoadPrefabContents(ASHPlayerBuilder.PrefabPath);
            try
            {
                var anim = root.GetComponentInChildren<Animator>();
                Transform Find(string n) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                Transform B(HumanBodyBones b) { var t = anim.GetBoneTransform(b); if (t) return t; var h = anim.avatar.humanDescription.human.FirstOrDefault(x => x.humanName == HumanTrait.BoneName[(int)b]); return Find(h.boneName); }
                void Frame(bool left, out Vector3 A, out Vector3 T, out Vector3 P, out Vector3 pc)
                {
                    var hand = B(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand); var mid = B(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                    var idx = B(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal); var lit = B(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                    A = (mid.position - hand.position).normalized; T = Vector3.ProjectOnPlane(idx.position - lit.position, A).normalized;
                    P = Vector3.Cross(A, T).normalized * (left ? -1f : 1f);
                    pc = Vector3.Lerp(hand.position, mid.position, 0.6f) + P * 0.03f;
                }
                string Pose(Transform t) => $"pos{t.localPosition:F3} rot{t.localEulerAngles:F0}";
                // seat: item axis Z -> zDir, item X -> xDir (orthogonalised); item point 'grip' (metres, item axes) at the palm centre
                void Seat(string name, bool left, System.Func<Vector3, Vector3, Vector3, Vector3> zDir, System.Func<Vector3, Vector3, Vector3, Vector3> xDir, Vector3 grip)
                {
                    var t = Find(name); if (!t) { sb.Append($"{name} missing; "); return; }
                    Frame(left, out var A, out var T, out var P, out var pc);
                    var z = zDir(A, T, P).normalized; var x = Vector3.ProjectOnPlane(xDir(A, T, P), z).normalized; var y = Vector3.Cross(z, x);
                    var before = Pose(t);
                    var rot = Quaternion.LookRotation(z, y);
                    t.rotation = rot; t.position = pc - rot * grip;
                    sb.Append($"{name}: {before} -> {Pose(t)}; ");
                }
                void Regrip(string name, bool left, Vector3 grip)
                {
                    var t = Find(name); if (!t) { sb.Append($"{name} missing; "); return; }
                    Frame(left, out _, out _, out _, out var pc); var before = Pose(t);
                    t.position = pc - t.rotation * grip;
                    sb.Append($"{name}: {before} -> {Pose(t)}; ");
                }

                // hatchet: handle across the fist (head on the thumb side), blade leading along the knuckles, held near the handle end
                Seat("Held_Hatchet", false, (A, T, P) => T, (A, T, P) => A, new Vector3(-0.055f, 0f, -0.45f));
                // rifle: pistol-grip carry like the pistol, muzzle forward and a little down, top toward the wrist
                Seat("Held_HuntingRifle", false, (A, T, P) => T + 0.35f * A, (A, T, P) => Vector3.Cross(-A, T + 0.35f * A), new Vector3(0f, -0.03f, 0f));
                // bow: same orientation, but the RISER (x -0.16) in the palm instead of the empty middle between riser and string
                Regrip("Held_WoodenBow", true, new Vector3(-0.16f, 0f, 0f));
                // rest arrow: held 12 cm from the nock, tip down and forward along the leg
                Seat("Held_HandArrow", false, (A, T, P) => A + 0.5f * T, (A, T, P) => P, new Vector3(0f, 0f, 0.12f));
                // lantern hook: back and out so the hanging lantern clears the left hand
                var hook = Find(ASHEquipment.HipHolderName);
                if (hook) { var b = Pose(hook); hook.position += root.transform.TransformDirection(new Vector3(-0.035f, 0f, -0.09f)); sb.Append($"Holder_Hip: {b} -> {Pose(hook)}; "); }
                // (the bow aim pose is now derived from the bow mesh at runtime: ASHWeaponAim.bowAimTrimEuler is only a trim)
                PrefabUtility.SaveAsPrefabAsset(root, ASHPlayerBuilder.PrefabPath);
                sb.Append("saved");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return sb.ToString();
        }
    }
}
