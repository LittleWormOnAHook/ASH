using System.Collections;
using ASH.Companion;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Dog companion smoke test (Play Mode, exec ASH.DebugTools.ASHDogTest.Run). Logs "[ASH] DOGTEST ..." lines;
    /// "DOGTEST SHOT name" lines are when a screenshot is worth taking (holds 3 s).</summary>
    public sealed class ASHDogTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_DogTest").AddComponent<ASHDogTest>(); }
        static void L(string m) => Debug.Log("[ASH] DOGTEST " + m);
        static string St(ASHDogCompanion d, Transform p) => $"mode={d.Mode} act='{d.Activity}' dist={Vector3.Distance(d.transform.position, p.position):F1} lv={d.Level} xp={d.Xp:F1}/{d.XpNeed:F0} hp={d.Health:F0}/{d.MaxHealth:F0} hunger={d.Hunger:F0} bond={d.Bond:F0} pack={d.PackSlots} pos={d.transform.position}";

        IEnumerator Start()
        {
            yield return new WaitForSeconds(0.5f);
            var d = ASHDogCompanion.Active; var input = FindAnyObjectByType<ASHInput>(); var inv = FindAnyObjectByType<ASHInventory>();
            var pl = FindAnyObjectByType<ASH.Survival.ASHSurvivalStats>();
            if (!d || !input || !pl) { L($"setup failed dog={(bool)d} input={(bool)input} player={(bool)pl}"); Destroy(gameObject); yield break; }
            var P = pl.transform; var anim = d.GetComponentInChildren<Animator>();
            L("start " + St(d, P) + $" controller={(anim && anim.runtimeAnimatorController ? anim.runtimeAnimatorController.name : "-")}");
            d.SetMode(ASHDogMode.Follow);
            // pathing: put the dog ~12 m away behind something solid (no straight line to you), then let it come back
            var S = ASHCompanionSettings.I; Vector3 start = Vector3.zero; bool got = false;
            for (int a = 0; a < 360 && !got; a += 20)
                foreach (float r in new[] { 12f, 9f, 15f })
                {
                    var p = P.position + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                    if (!Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out var g, 10f, S.environmentLayers, QueryTriggerInteraction.Ignore) || g.normal.y < 0.7f) continue;
                    if (Physics.CheckCapsule(g.point + Vector3.up * 0.3f, g.point + Vector3.up * 0.7f, 0.25f, S.environmentLayers, QueryTriggerInteraction.Ignore)) continue;
                    if (!Physics.Linecast(P.position + Vector3.up, g.point + Vector3.up * 0.5f, S.environmentLayers, QueryTriggerInteraction.Ignore)) continue;
                    start = g.point; got = true; break;
                }
            d.SetMode(ASHDogMode.Follow);
            if (got) d.transform.position = start;
            var body = d.transform.Find("ASH_DogBody") ? d.transform.Find("ASH_DogBody").GetComponent<CapsuleCollider>() : null;
            float t0 = Time.time, maxPen = 0f; string penOn = "-"; float minDist = 99f;
            while (Time.time - t0 < 10f)
            {
                yield return null;
                minDist = Mathf.Min(minDist, Vector3.Distance(d.transform.position, P.position));
                if (!body) continue;
                var cols = Physics.OverlapCapsule(body.transform.TransformPoint(body.center + Vector3.forward * (body.height * 0.5f - body.radius)), body.transform.TransformPoint(body.center - Vector3.forward * (body.height * 0.5f - body.radius)), body.radius * 0.8f, S.environmentLayers, QueryTriggerInteraction.Ignore);
                foreach (var c in cols)
                {
                    if (c.transform.IsChildOf(d.transform) || c.transform.IsChildOf(P)) continue;
                    if (Physics.ComputePenetration(body, body.transform.position, body.transform.rotation, c, c.transform.position, c.transform.rotation, out var dir, out var dist) && dir.y < 0.7f && dist > maxPen) { maxPen = dist; penOn = c.name; }
                }
            }
            L($"path: start={(got ? start.ToString() : "none")} blockedLine={got} minDist={minDist:F1} final " + St(d, P) + $" maxWallPenetration={maxPen * 100f:F0}cm on '{penOn}' animSpeed={(anim ? anim.GetFloat("Speed") : -1):F2}");            L("SHOT follow"); yield return new WaitForSeconds(3f);
            // find: drop a pickup 7 m to the side
            var it = inv ? FirstItem(inv) : null;
            if (!it) foreach (var r in ASHItemRegistry.I.items) if (r && !ASHInventory.IsKeyItem(r) && r.category == ASHItemCategory.Resource) { it = r; break; }
            if (it && inv && inv.Count(it) == 0) inv.AddItem(it, 2);
            ASHItemPickup pk = null;
            if (it) pk = ASHItemDrop.SpawnAt(it, 1, P.position + P.right * 7f + Vector3.up * 0.5f, Quaternion.identity);
            L($"find: dropped {(it ? it.displayName : "nothing")} pickup={(bool)pk}");
            t0 = Time.time; bool pointed = false;
            while (Time.time - t0 < 14f) { if (d.Activity == "Pointing") { pointed = true; break; } yield return null; }
            L($"find: pointed={pointed} after {Time.time - t0:F1}s " + St(d, P)); if (pointed) { L("SHOT find"); yield return new WaitForSeconds(3f); }
            // danger: a tagged dummy enemy 12 m ahead
            var en = GameObject.CreatePrimitive(PrimitiveType.Capsule); en.name = "ASH_TestEnemy"; en.tag = "Enemy"; en.transform.position = P.position + P.forward * 12f + Vector3.up;
            t0 = Time.time; bool warned = false;
            while (Time.time - t0 < 3f) { if (d.Activity.StartsWith("Growl")) { warned = true; break; } yield return null; }
            L($"danger: warned={warned} " + St(d, P));
            d.SetMode(ASHDogMode.Defend); t0 = Time.time; float minE = 99f;
            while (Time.time - t0 < 5f) { minE = Mathf.Min(minE, Vector3.Distance(d.transform.position, en.transform.position)); yield return null; }
            L($"defend: closest to enemy {minE:F1} m act='{d.Activity}'");
            Destroy(en); d.SetMode(ASHDogMode.Stay); yield return new WaitForSeconds(3f);
            L("stay " + St(d, P) + $" pose={(anim ? anim.GetInteger("Pose") : -1)}"); { var c = d.transform.position + d.transform.forward * 2.2f + d.transform.right * 0.8f + Vector3.up * 0.9f; var dir = (d.transform.position + Vector3.up * 0.4f - c).normalized; L(string.Format(System.Globalization.CultureInfo.InvariantCulture, "SHOTPOSE stay|{0:F2},{1:F2},{2:F2}|{3:F1},{4:F1},50", c.x, c.y, c.z, -Mathf.Asin(dir.y) * Mathf.Rad2Deg, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg)); } yield return new WaitForSeconds(3f);
            // call from far: move the dog 40 m away, then call
            d.transform.position = P.position + P.forward * 40f; yield return null;
            bool called = d.Call(); yield return new WaitForSeconds(0.5f);
            L($"call: ok={called} " + St(d, P));
            // give / take + feed + pet
            int give = -1; if (inv) for (int i = 0; i < inv.Slots.Count; i++) if (!inv.Slots[i].IsEmpty && !ASHInventory.IsKeyItem(inv.Slots[i].item)) { give = i; break; }
            int moved = give >= 0 ? d.GiveFromSlot(inv, give) : 0; int back = moved > 0 ? d.TakeSlot(inv, 0) : 0;
            d.Pet(); L($"pack: gave {moved} took {back}; pet bond={d.Bond:F0}");
            var save = d.CaptureJson(); L("save " + save.Substring(0, Mathf.Min(220, save.Length)));
            ASHDogMenu.Open(d, inv, P); yield return new WaitForSeconds(0.6f); ScreenCapture.CaptureScreenshot(@"C:\Users\Teabagger\dog_ui_menu.png"); L("UI menu"); yield return new WaitForSeconds(3f); ASHDogMenu.CloseIfOpen();
            var j = FindAnyObjectByType<ASH.UI.ASHJournalUI>(FindObjectsInactive.Include);
            if (j) { j.Open(ASH.UI.ASHJournalUI.Tab.Pets); yield return new WaitForSeconds(0.8f); ScreenCapture.CaptureScreenshot(@"C:\Users\Teabagger\dog_ui_journal.png"); L("UI journal"); yield return new WaitForSeconds(3f); j.Close(); }
            L("done"); Destroy(gameObject);
        }

        static ASHItemDefinition FirstItem(ASHInventory inv) { foreach (var s in inv.Slots) if (!s.IsEmpty && !ASHInventory.IsKeyItem(s.item)) return s.item; return null; }
    }
}