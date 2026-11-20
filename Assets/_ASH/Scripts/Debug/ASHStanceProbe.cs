using System.Collections;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using Invector.vCharacterController;
using ASH.Core;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Stance probe (Play Mode, exec ASH.DebugTools.ASHStanceProbe.Run): hips height and knee bend while idle and
    /// while aiming each ranged weapon, plus crouch/strafe flags and the clip playing on every Animator layer. Logs "[ASH] STANCE ...".</summary>
    public sealed class ASHStanceProbe : MonoBehaviour
    {
        /// <summary>Log only (no equip/aim), every 0.5 s for 22 s; run alongside ASHJitterTest.RunRangedFixed.</summary>
        public static void RunPassive() { if (Application.isPlaying) new GameObject("ASH_StanceProbe").AddComponent<ASHStanceProbe>()._passive = true; }
        bool _passive;
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_StanceProbe").AddComponent<ASHStanceProbe>(); }
        static ASHItemDefinition Item(string f)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"Assets/_ASH/Data/Items/ASH_Item_{f}.asset");
#else
            return null;
#endif
        }
        IEnumerator Start()
        {
            yield return null;
            var inv = FindAnyObjectByType<ASHInventory>(); var eq = FindAnyObjectByType<ASHEquipment>(); var input = FindAnyObjectByType<ASHInput>();
            var cc = FindAnyObjectByType<vThirdPersonController>();
            if (!(inv && eq && input && cc && eq.Animator)) { Debug.Log("[ASH] STANCE setup failed"); Destroy(gameObject); yield break; }
            if (_passive)
            {
                var pa = cc.GetComponent<Animator>(); var wa = FindAnyObjectByType<ASHWeaponAim>();
                for (int k = 0; k < 44; k++) { yield return new WaitForSeconds(0.5f); yield return new WaitForEndOfFrame(); Log($"p{k} {(wa ? wa.Current.ToString() : "-")}", pa, cc); }
                Destroy(gameObject); yield break;
            }
            var a = cc.GetComponent<Animator>() ? cc.GetComponent<Animator>() : eq.Animator; Debug.Log($"[ASH] STANCE animator {a.name} same={a == eq.Animator} mode={a.updateMode} t={Time.time:F2}");
            var tp = cc.GetComponent<vThirdPersonInput>(); bool lock0 = tp && tp.lockCameraInput; if (tp) tp.lockCameraInput = true;
            input.SimSecondaryHeld = false; eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand);
            yield return new WaitForSeconds(0.8f); yield return new WaitForEndOfFrame(); Log("none idle", a, cc);
            foreach (var am in new[] { "RifleRounds", "PistolRounds", "WoodenArrow" }) { var ai = Item(am); if (ai && inv.Count(ai) < 5) inv.AddItem(ai, 5); }
            foreach (var n in new[] { "HuntingRifle", "Pistol", "WoodenBow" })
            {
                var it = Item(n); if (!it) continue; if (inv.Count(it) == 0) inv.AddItem(it, 1);
                eq.Equip(it); yield return new WaitForSeconds(0.8f); yield return new WaitForEndOfFrame(); Log(n + " idle", a, cc);
                input.SimSecondaryHeld = true; yield return new WaitForSeconds(1.8f); yield return new WaitForEndOfFrame(); Log(n + " aim", a, cc); Debug.Log($"[ASH] STANCE ui blocks={ASHUIState.BlocksGameplay} invAiming={(cc.GetComponent<vShooterMeleeInput>() ? cc.GetComponent<vShooterMeleeInput>().IsAiming : false)}");
                input.SimSecondaryHeld = false; yield return new WaitForSeconds(0.3f); eq.Unequip(ASHEquipSlot.MainHand); yield return new WaitForSeconds(0.3f);
            }
            if (tp) tp.lockCameraInput = lock0; Destroy(gameObject);
        }
        static float Knee(Animator a, HumanBodyBones u, HumanBodyBones l, HumanBodyBones f)
        {
            var U = a.GetBoneTransform(u); var L = a.GetBoneTransform(l); var F = a.GetBoneTransform(f);
            return U && L && F ? 180f - Vector3.Angle(U.position - L.position, F.position - L.position) : -1f;
        }
        static void Log(string tag, Animator a, vThirdPersonController cc)
        {
            var hips = a.GetBoneTransform(HumanBodyBones.Hips); var head = a.GetBoneTransform(HumanBodyBones.Head); var ch = a.GetBoneTransform(HumanBodyBones.Chest);
            var sb = new StringBuilder();
            sb.Append($"STANCE {tag}: hipsH {(hips.position.y - cc.transform.position.y):F3} headH {(head.position.y - cc.transform.position.y):F3} kneeL {Knee(a, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot):F0} kneeR {Knee(a, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot):F0} chestPitch {(ch ? Vector3.Angle(ch.up, Vector3.up) : -1):F0}");
            sb.Append($" t {Time.time:F2} aimFlag {(FindAnyObjectByType<ASHWeaponAim>() ? FindAnyObjectByType<ASHWeaponAim>().Aiming : false)} crouch {cc.isCrouching} strafe {cc.isStrafing} |");
            for (int i = 0; i < a.layerCount; i++)
            {
                float w = i == 0 ? 1f : a.GetLayerWeight(i); if (w < 0.01f) continue;
                var ci = a.GetCurrentAnimatorClipInfo(i); string clip = ci.Length > 0 && ci[0].clip ? ci[0].clip.name : "-";
                sb.Append($" [{i}:{a.GetLayerName(i)} w{w:F2} {clip}]");
            }
            Debug.Log("[ASH] " + sb);
        }
    }
}
