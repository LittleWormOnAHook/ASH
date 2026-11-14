using System.Collections;
using System.Linq;
using System.Text;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Harvest loop smoke test (Play Mode, exec ASH.DebugTools.ASHHarvestTest.Run): no-axe prompt, equip hatchet,
    /// fell a dead fir through the real interactor, logs spawn, chop a log, firewood/kindling in the pack, gather a branch pile.
    /// Logs "[ASH] HARVEST ..." and "[ASH] HARVEST SUMMARY".</summary>
    public sealed class ASHHarvestTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_HarvestTest").AddComponent<ASHHarvestTest>(); }

        /// <summary>exec in Play: average FPS over 8 s after a 4 s warm-up at the current view, plus resource object counts.</summary>
        public static void Fps() { if (Application.isPlaying) new GameObject("ASH_FpsProbe").AddComponent<FpsProbe>(); }
        sealed class FpsProbe : MonoBehaviour
        {
            IEnumerator Start()
            {
                yield return new WaitForSeconds(4f);
                int frames = 0; float t = 0f, worst = 0f;
                while (t < 8f) { t += Time.unscaledDeltaTime; worst = Mathf.Max(worst, Time.unscaledDeltaTime); frames++; yield return null; }
                int nodes = FindObjectsByType<ASHResourceNode>(FindObjectsSortMode.None).Length, chops = FindObjectsByType<ASHChoppable>(FindObjectsSortMode.None).Length;
                int rends = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
                Debug.Log($"[ASH] FPS avg {frames / t:F1} worst frame {worst * 1000f:F1} ms | resource nodes {nodes}, choppables {chops}, renderers {rends}");
                Destroy(gameObject);
            }
        }

        ASHInput _in; vThirdPersonController _cc; ASHInteractor _ix; ASHInventory _inv; int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"HARVEST {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }

        static ASHItemDefinition Item(string file)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"Assets/_ASH/Data/Items/ASH_Item_{file}.asset");
#else
            return null;
#endif
        }

        void Teleport(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var h, 100f, 1, QueryTriggerInteraction.Ignore)) p = h.point;
            var rb = _cc.GetComponent<Rigidbody>(); if (rb) { rb.linearVelocity = Vector3.zero; rb.position = p + Vector3.up * 0.05f; }
            _cc.transform.position = p + Vector3.up * 0.05f;
        }

        string Diag(Component tree)
        {
            var eye = _cc.transform.position + Vector3.up * 1.6f; var hits = Physics.RaycastAll(_ix.SimRay ?? new Ray(eye, Vector3.forward), 12f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance);
            return $"| player {_cc.transform.position} dTree {Vector3.Distance(_cc.transform.position, tree.transform.position):F2} cols {string.Join(",", tree.GetComponentsInChildren<Collider>().Select(c => c.GetType().Name + (c.enabled ? "" : "(off)") + "@" + LayerMask.LayerToName(c.gameObject.layer)))} hits {string.Join(",", hits.Select(h => h.collider.name + ":" + h.distance.ToString("F1")))}";
        }

        void Aim(Vector3 target) { var eye = _cc.transform.position + Vector3.up * 1.6f; _ix.SimRay = new Ray(eye, (target - eye).normalized); }

        static Vector3 Centre(Component c) { var col = c.GetComponentsInChildren<Collider>().FirstOrDefault(x => x.enabled); return col ? col.bounds.center : c.transform.position + Vector3.up; }

        IEnumerator Hold(System.Func<bool> until, float timeout)
        {
            _in.SimInteractHeld = true; float t = 0f;
            while (!until() && t < timeout) { t += Time.deltaTime; yield return null; }
            _in.SimInteractHeld = false;
        }

        IEnumerator Start()
        {
            _in = FindAnyObjectByType<ASHInput>(); _cc = FindAnyObjectByType<vThirdPersonController>(); _ix = FindAnyObjectByType<ASHInteractor>(); _inv = FindAnyObjectByType<ASHInventory>();
            var hatchet = Item("Hatchet"); var firewood = Item("Firewood"); var kindling = Item("Kindling"); var deadwood = Item("Deadwood");
            Check("setup", _in && _cc && _ix && _inv && hatchet && firewood && kindling, $"input={(bool)_in} cc={(bool)_cc} ix={(bool)_ix} inv={(bool)_inv} items={(bool)hatchet}/{(bool)firewood}/{(bool)kindling}");
            if (_fail > 0) { Finish(); yield break; }
            yield return new WaitForSeconds(1f);
            while (_inv.Count(hatchet) > 0) _inv.Remove(hatchet, 1);

            var p0 = _cc.transform.position;
            var tree = FindObjectsByType<ASHChoppable>(FindObjectsSortMode.None).Where(c => c.Kind == ASHChopKind.StandingTree && !c.Done).OrderBy(c => (c.transform.position - p0).sqrMagnitude).FirstOrDefault();
            Check("standing dead tree in scene", tree, tree ? $"{tree.name} at {tree.transform.position} ({FindObjectsByType<ASHChoppable>(FindObjectsSortMode.None).Length} choppables)" : "none");
            if (!tree) { Finish(); yield break; }
            var side = Vector3.Cross(Vector3.up, tree.transform.forward).normalized;
            Teleport(tree.transform.position - side * 1.6f); yield return new WaitForSeconds(0.6f);
            var trunk = tree.transform.position + Vector3.up * 1.1f; Aim(trunk); yield return new WaitForSeconds(0.25f);
            Check("no-axe prompt", _ix.Target as Object == tree && _ix.Prompt != null && _ix.Prompt.StartsWith("Needs"), $"target={(_ix.Target as Component)?.name} prompt='{_ix.Prompt}' {Diag(tree)}");
            yield return Hold(() => false, 1.0f);
            Check("no chop without axe", tree.HitsLeft > 1 && !tree.Done, $"hits left {tree.HitsLeft}");

            _inv.AddItem(hatchet, 1); yield return new WaitForSeconds(0.2f);
            Aim(trunk); yield return new WaitForSeconds(0.2f);
            Check("axe prompt", _ix.Prompt != null && _ix.Prompt.StartsWith("Fell"), $"prompt='{_ix.Prompt}' {Diag(tree)}");
            float t0 = Time.time;
            yield return Hold(() => tree.Done, 20f);
            Check("tree felled", tree.Done, $"in {Time.time - t0:F1}s, hits left {tree.HitsLeft}");
            float w = 0f; while (tree.Busy && w < 8f) { w += Time.deltaTime; yield return null; }
            var logs = tree.Spawned.Where(g => g && g.GetComponent<ASHChoppable>() && g.GetComponent<ASHChoppable>().Kind == ASHChopKind.Log).Select(g => g.GetComponent<ASHChoppable>()).ToList();
            var stump = tree.Spawned.Select(g => g ? g.GetComponent<ASHChoppable>() : null).FirstOrDefault(c => c && c.Kind == ASHChopKind.Stump);
            Check("logs spawned", logs.Count > 0, $"{logs.Count} logs, stump {(bool)stump}, tree visual hidden {!tree.GetComponentsInChildren<Renderer>().Any()}");
            if (logs.Count > 0)
            {
                var log = logs[0]; int fw0 = _inv.Count(firewood), kd0 = _inv.Count(kindling);
                var lc = Centre(log); var off = Vector3.Cross(Vector3.up, log.transform.right).normalized * 1.4f;
                Teleport(lc + off); yield return new WaitForSeconds(0.6f); Aim(Centre(log)); yield return new WaitForSeconds(0.25f);
                string pr = _ix.Prompt; bool tgt = _ix.Target as Object == log;
                yield return Hold(() => log.Done, 10f);
                Check("log chopped", log.Done && _inv.Count(firewood) > fw0, $"target ok {tgt}, prompt '{pr}', firewood {fw0}->{_inv.Count(firewood)}, kindling {kd0}->{_inv.Count(kindling)}");
            }
            if (stump)
            {
                int fw0 = _inv.Count(firewood); var sc = Centre(stump);
                Teleport(sc + Vector3.right * 1.3f); yield return new WaitForSeconds(0.6f); Aim(Centre(stump)); yield return new WaitForSeconds(0.25f);
                yield return Hold(() => stump.Done, 10f);
                Check("stump chopped", stump.Done && _inv.Count(firewood) > fw0, $"firewood {fw0}->{_inv.Count(firewood)}");
            }
            // branch pile pickup (deadwood)
            var branch = FindObjectsByType<ASHResourceNode>(FindObjectsSortMode.None).Where(n => n.name.StartsWith("ASH_Deadwood_Branches") && !n.Depleted).OrderBy(n => (n.transform.position - _cc.transform.position).sqrMagnitude).FirstOrDefault();
            if (branch)
            {
                int d0 = _inv.Count(deadwood); var bc = Centre(branch);
                Teleport(bc + Vector3.forward * 1.3f); yield return new WaitForSeconds(0.6f); Aim(Centre(branch)); yield return new WaitForSeconds(0.25f);
                string pr = _ix.Prompt;
                yield return Hold(() => _inv.Count(deadwood) > d0, 6f);
                Check("branch pile gathers deadwood", _inv.Count(deadwood) > d0, $"prompt '{pr}', deadwood {d0}->{_inv.Count(deadwood)}");
            }
            else Check("branch pile gathers deadwood", false, "no branch pile found");
            var plant = FindObjectsByType<ASHResourceNode>(FindObjectsSortMode.None).Where(n => n.name.StartsWith("ASH_Fiber_") && !n.Depleted).OrderBy(n => (n.transform.position - _cc.transform.position).sqrMagnitude).FirstOrDefault();
            if (plant)
            {
                var fiber = plant.Item; int f0 = _inv.Count(fiber); var pc = Centre(plant);
                Teleport(pc + Vector3.forward * 1.3f); yield return new WaitForSeconds(0.6f); Aim(Centre(plant)); yield return new WaitForSeconds(0.25f);
                string pr = _ix.Prompt;
                yield return Hold(() => _inv.Count(fiber) > f0, 6f);
                Check("plant gathers fiber", _inv.Count(fiber) > f0, $"prompt '{pr}', {(fiber ? fiber.displayName : "?")} {f0}->{_inv.Count(fiber)}");
            }
            else Check("plant gathers fiber", false, "no plant pickup found");
            _ix.SimRay = null;
            Finish();
        }

        void Finish() { if (_ix) _ix.SimRay = null; if (_in) _in.SimInteractHeld = null; Debug.Log($"[ASH] HARVEST SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}
