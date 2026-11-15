using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Chop clip sweep (Play Mode, exec ASH.DebugTools.ASHChopSweep.Run): holds the player still with the hatchet and
    /// steps the "Chop" and "ChopHigh" swings through normalized time, logging the axe head point in the player's space
    /// (x right, y up, z forward) and the blade edge direction, plus side/top views. Finds the impact time (head furthest
    /// forward at trunk height / lowest in front). Logs "[ASH] CHOPSWEEP ..." + "CHOPSWEEP done".</summary>
    public sealed class ASHChopSweep : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_ChopSweep").AddComponent<ASHChopSweep>(); }
        IEnumerator Start()
        {
            yield return null;
            var cc = FindAnyObjectByType<vThirdPersonController>(); var eq = FindAnyObjectByType<ASHEquipment>(); var inv = FindAnyObjectByType<ASHInventory>();
            var chop = FindAnyObjectByType<ASHChopAnimator>(); var layer = FindAnyObjectByType<ASHActionLayer>();
#if UNITY_EDITOR
            var hatchet = UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_Hatchet.asset");
#else
            ASHItemDefinition hatchet = null;
#endif
            if (inv.Count(hatchet) == 0) inv.AddItem(hatchet, 1); eq.Equip(hatchet); yield return new WaitForSeconds(0.5f);
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../AshBridge/chop_rec")); Directory.CreateDirectory(dir);
            var cam = new GameObject("ASH_SweepCam").AddComponent<Camera>(); if (Camera.main) cam.CopyFrom(Camera.main); cam.enabled = false; cam.fieldOfView = 40f; cam.nearClipPlane = 0.05f;
            var rt = new RenderTexture(320, 360, 24); var sheet = new Texture2D(320 * 6, 360 * 2, TextureFormat.RGB24, false);
            var tps = cc.GetComponent<vThirdPersonInput>(); if (tps) tps.lockCameraInput = true;
            cc.lockMovement = true; cc.lockRotation = true;
            var rb = cc.GetComponent<Rigidbody>(); var pin = cc.transform.position; var rot = cc.transform.rotation;
            foreach (var st in new[] { "Chop", "ChopHigh" })
            {
                var sb = new StringBuilder(); float bestScore = -99f, bestT = -1f; int shot = 0;
                for (int k = 0; k < 40; k++) { layer.PlayAt(st, 0f); yield return null; }
                for (int i = 0; i <= 50; i++)
                {
                    float t = i / 50f;
                    for (int k = 0; k < 3; k++) { layer.PlayAt(st, t); cc.transform.SetPositionAndRotation(pin, rot); if (rb) { rb.position = pin; rb.linearVelocity = Vector3.zero; } yield return new WaitForFixedUpdate(); yield return null; }
                    yield return new WaitForEndOfFrame();
                    if (!chop.BladeWorld(out var b)) continue;
                    var me = cc.transform; var l = me.InverseTransformPoint(b);
                    var hand = eq.Animator.GetBoneTransform(HumanBodyBones.RightHand).position; var hl = me.InverseTransformPoint(hand);
                    // impact score: standing tree (ChopHigh) = furthest forward at 0.6-1.4 m; low chop = furthest forward with the head low (<0.9 m)
                    float score = st == "ChopHigh" ? (l.y > 0.5f && l.y < 1.5f ? l.z : -9f) : (l.y < 0.9f ? l.z - Mathf.Max(0f, l.y - 0.3f) : -9f);
                    if (score > bestScore) { bestScore = score; bestT = t; }
                    sb.Append($"\n  t {t:F2}: head ({l.x:F2},{l.y:F2},{l.z:F2}) hand ({hl.x:F2},{hl.y:F2},{hl.z:F2})");
                    if (i % 4 == 0 && shot < 12)
                    {
                        cam.transform.position = me.position + Vector3.up * 1.0f + me.right * 3.0f + me.forward * 0.4f; cam.transform.LookAt(me.position + Vector3.up * 0.9f + me.forward * 0.4f);
                        cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                        var prev = RenderTexture.active; RenderTexture.active = rt; sheet.ReadPixels(new Rect(0, 0, 320, 360), (shot % 6) * 320, (1 - shot / 6) * 360); RenderTexture.active = prev; shot++;
                    }
                }
                sheet.Apply(); File.WriteAllBytes(Path.Combine(dir, $"sweep_{st}.jpg"), sheet.EncodeToJPG(80));
                Debug.Log($"[ASH] CHOPSWEEP {st}: best impact t {bestT:F2} (score {bestScore:F2}); sheet = t 0,0.08,..0.88 left-to-right top row first (side view, forward = right){sb}");
            }
            layer.Release(); cc.lockMovement = false; cc.lockRotation = false; if (tps) tps.lockCameraInput = false; Destroy(cam.gameObject);
            Debug.Log("[ASH] CHOPSWEEP done");
            Destroy(gameObject);
        }
    }
}
