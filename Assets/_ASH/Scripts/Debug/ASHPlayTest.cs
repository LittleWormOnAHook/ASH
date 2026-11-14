using System.Collections;
using System.Linq;
using System.Text;
using ASH.Core;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using ASH.Survival;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>
    /// Automated slice smoke test driven through ASHInput simulation overrides (works with the editor unfocused).
    /// Start in Play Mode via the editor bridge: exec ASH.DebugTools.ASHPlayTest.Run. Results are logged as
    /// "[ASH] TEST ..." lines and a final "[ASH] TEST SUMMARY".
    /// </summary>
    public sealed class ASHPlayTest : MonoBehaviour
    {
        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] TEST requires Play Mode"); return; }
            var go = new GameObject("ASH_PlayTest"); go.AddComponent<ASHPlayTest>();
        }

        ASHInput _in; vThirdPersonController _cc; ASHInteractor _ix; ASHInventory _inv; ASHSurvivalStats _st; ASHCrafting _cr; ASHPlacer _pl; ASHObjectiveTracker _obj;
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();

        void Check(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            string line = $"TEST {(ok ? "PASS" : "FAIL")} {name}: {detail}";
            _sum.AppendLine(line); Debug.Log("[ASH] " + line);
        }

        IEnumerator Start()
        {
            _in = FindAnyObjectByType<ASHInput>(); _cc = FindAnyObjectByType<vThirdPersonController>(); _ix = FindAnyObjectByType<ASHInteractor>();
            _inv = FindAnyObjectByType<ASHInventory>(); _st = FindAnyObjectByType<ASHSurvivalStats>(); _cr = FindAnyObjectByType<ASHCrafting>();
            _pl = FindAnyObjectByType<ASHPlacer>(); _obj = FindAnyObjectByType<ASHObjectiveTracker>();
            Check("components", _in && _cc && _ix && _inv && _st && _cr && _pl, $"input={(bool)_in} cc={(bool)_cc} ix={(bool)_ix} inv={(bool)_inv} stats={(bool)_st} craft={(bool)_cr} placer={(bool)_pl} obj={(bool)_obj}");
            if (_fail > 0) { Finish(); yield break; }
            var tr = _cc.transform;
            yield return new WaitForSeconds(1.5f);
            Check("spawn grounded", _cc.isGrounded, $"pos={tr.position} grounded={_cc.isGrounded}");

            // walk
            Vector3 p0 = tr.position; _in.SimMove = new Vector2(0, 1); yield return new WaitForSeconds(2f);
            float walk = Flat(tr.position - p0) / 2f; Check("walk", walk > 1.5f, $"avg speed {walk:F2} m/s");
            // sprint
            float s0 = _cc.currentStamina; p0 = tr.position; _in.SimSprint = true; yield return new WaitForSeconds(2f);
            float sprint = Flat(tr.position - p0) / 2f; Check("sprint", sprint > walk * 1.2f && _cc.currentStamina < s0, $"avg speed {sprint:F2} m/s, stamina {s0:F0}->{_cc.currentStamina:F0}, HUD stamina {_st.Stamina:F0}");
            _in.SimSprint = false; _in.SimMove = Vector2.zero; yield return new WaitForSeconds(0.8f);
            // jump
            float y0 = tr.position.y, maxY = y0; _in.SimJump = true;
            for (float t = 0; t < 1.2f; t += Time.deltaTime) { maxY = Mathf.Max(maxY, tr.position.y); yield return null; }
            Check("jump", maxY - y0 > 0.3f, $"apex +{maxY - y0:F2} m");
            yield return new WaitForSeconds(0.6f);
            // crouch
            _in.SimCrouch = true; yield return new WaitForSeconds(0.5f); bool crouched = _cc.isCrouching;
            _in.SimCrouch = true; yield return new WaitForSeconds(0.5f);
            Check("crouch", crouched && !_cc.isCrouching, $"crouched={crouched} after toggle={_cc.isCrouching}");

            // gather each resource type through the interactor
            var nodes = FindObjectsByType<ASHResourceNode>();
            foreach (var group in nodes.Where(n => n.Item != null).GroupBy(n => n.Item))
            {
                var item = group.Key;
                var node = group.OrderBy(n => (n.transform.position - tr.position).sqrMagnitude).First();
                int before = _inv.Count(item);
                Teleport(node.transform.position + Away(node.transform.position) * 1.6f);
                yield return new WaitForSeconds(0.5f);
                Vector3 eye = tr.position + Vector3.up * 1.6f;
                var col = node.GetComponentInChildren<Collider>();
                Vector3 target = col ? col.bounds.center : node.transform.position + Vector3.up * 0.3f;
                _ix.SimRay = new Ray(eye, (target - eye).normalized);
                yield return new WaitForSeconds(0.2f);
                string prompt = _ix.Prompt;
                _in.SimInteractHeld = true;
                float waited = 0f;
                while (_inv.Count(item) <= before && waited < 4f) { waited += Time.deltaTime; yield return null; }
                _in.SimInteractHeld = false; _ix.SimRay = null;
                Check("gather " + item.displayName, _inv.Count(item) > before, $"prompt='{prompt}' {before}->{_inv.Count(item)} in {waited:F1}s");
                // top up to goal for the objective chain
                if (_obj) foreach (var g in _obj.GatherGoals) if (g.item == item) while (g.collected < g.required) _inv.AddItem(item, 1);
                if (!_obj) while (_inv.Count(item) < 4) _inv.AddItem(item, 1);
            }

            // eat
            var food = _inv.Slots.Where(s => !s.IsEmpty && s.item.IsConsumable).Select(s => s.item).FirstOrDefault();
            if (food) { float h0 = _st.Hunger; bool ate = _inv.TryConsume(food, _st); Check("eat " + food.displayName, ate && _st.Hunger > h0, $"hunger {h0:F1}->{_st.Hunger:F1}"); }
            else Check("eat", false, "no consumable in inventory");
            // inventory UI: hotbar + pack slots mirror the inventory, move/split, hotbar key use
            var hud = FindAnyObjectByType<ASH.UI.ASHHud>();
            if (hud)
            {
                string slot0 = hud.SlotText(0);
                Check("hotbar slots", hud.HotbarSlotCount >= 6 && hud.PackSlotCount == _inv.SlotCount && !string.IsNullOrEmpty(slot0) && !slot0.StartsWith("|"), $"hotbar={hud.HotbarSlotCount} pack={hud.PackSlotCount} slot0='{slot0}' slot1='{hud.SlotText(1)}' slot2='{hud.SlotText(2)}'");
                var a0 = _inv.Slots[0].item; int n0 = _inv.Slots[0].amount;
                bool moved = _inv.Move(0, 7); yield return null;
                bool movedOk = moved && _inv.Slots[7].item == a0 && (hud.SlotText(7, false) ?? "").StartsWith(a0 ? a0.displayName : "?");
                _inv.Move(7, 0); yield return null;
                bool split = n0 >= 2 && _inv.Split(0); int after = _inv.Slots[0].amount; int total = _inv.Count(a0);
                Check("slot move/split", movedOk && (n0 < 2 || (split && after == n0 - n0 / 2 && total == n0)), $"moved={movedOk} split={split} {n0}->{after} total={total}");
                System.IO.Directory.CreateDirectory("AshBridge/shots/v3");
                ScreenCapture.CaptureScreenshot("AshBridge/shots/v3/06_hud_hotbar.png"); yield return null; yield return null;
                _in.SimInventory = true; yield return null; yield return null; yield return null;
                ScreenCapture.CaptureScreenshot("AshBridge/shots/v3/07_pack_open.png"); yield return null; yield return null;
                _in.SimInventory = true; yield return null; yield return null;
                int ci = -1; for (int i = 0; i < 6 && i < _inv.SlotCount; i++) if (!_inv.Slots[i].IsEmpty && _inv.Slots[i].item.IsConsumable) { ci = i; break; }
                if (ci >= 0) { var it = _inv.Slots[ci].item; int c0 = _inv.Count(it); _in.SimHotbar = ci; yield return null; yield return null; Check("hotbar key eat", _inv.Count(it) == c0 - 1, $"slot {ci + 1}: {c0}->{_inv.Count(it)}"); }
                else Check("hotbar key eat", false, "no consumable in hotbar slots 1-6");
            }
            else Check("hotbar slots", false, "no HUD in scene");
            if (_obj) Check("objective after gather", _obj.Current == ASHObjectiveTracker.Stage.Craft, "stage=" + _obj.Current);

            // craft
            var recipe = _cr.Recipes.FirstOrDefault();
            bool started = recipe && _cr.TryCraft(recipe);
            float cw = 0; while (_cr.IsCrafting && cw < 10f) { cw += Time.deltaTime; yield return null; }
            var fire = recipe ? recipe.outputItem : null;
            Check("craft campfire", started && fire && _inv.Count(fire) > 0, $"started={started} have={(fire ? _inv.Count(fire) : 0)} stage={(_obj ? _obj.Current.ToString() : "-")}");

            // travel to overlook
            if (_obj && _obj.Overlook) { Teleport(_obj.Overlook.position + Vector3.right * 3f); yield return new WaitForSeconds(0.8f); }
            if (_obj) Check("reach overlook", _obj.Current == ASHObjectiveTracker.Stage.MakeCamp, "stage=" + _obj.Current);

            // place at campsite
            if (fire)
            {
                Vector3 site = _obj && _obj.Campsite ? _obj.Campsite.position : tr.position + tr.forward * 3f;
                if (_obj && _obj.Campsite) { Teleport(site + new Vector3(3.5f, 0, 0)); yield return new WaitForSeconds(0.6f); }
                bool began = _pl.Begin(fire);
                // aim at the first clear spot beside the old campsite (its cold ring occupies the centre)
                Vector3 aimAt = site; string reason = "";
                for (int k = 0; k < 24; k++)
                {
                    float ang = k * 45f * Mathf.Deg2Rad; float rad = 3.5f + (k / 8) * 2f;
                    _pl.SimAimPoint = site + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * rad;
                    yield return null; yield return null;
                    reason = _pl.InvalidReason;
                    if (_pl.IsValid) { aimAt = _pl.SimAimPoint.Value; break; }
                }
                yield return new WaitForSeconds(0.3f);
                _in.SimPrimary = true; yield return new WaitForSeconds(0.4f);
                _pl.SimAimPoint = null;
                var placed = FindObjectsByType<ASHCampfire>().Where(c => c.name != "ASH_PlacementGhost").ToArray();
                Check("place campfire", began && placed.Length > 0, $"began={began} reason='{reason}' aim={aimAt} placed={placed.Length} remaining={_inv.Count(fire)}");
            }
            if (_obj) Check("objective complete", _obj.Current == ASHObjectiveTracker.Stage.Complete, "stage=" + _obj.Current);

            // needs decay sanity
            float hu = _st.Hunger, th = _st.Thirst; yield return new WaitForSeconds(2f);
            Check("needs decay", _st.Hunger < hu && _st.Thirst < th, $"hunger {hu:F3}->{_st.Hunger:F3} thirst {th:F3}->{_st.Thirst:F3}");
            Finish();
        }

        Vector3 Away(Vector3 p) { var d = _cc.transform.position - p; d.y = 0; return d.sqrMagnitude < 0.01f ? Vector3.back : d.normalized; }

        void Teleport(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var h, 100f, 1, QueryTriggerInteraction.Ignore)) p = h.point;
            var rb = _cc.GetComponent<Rigidbody>(); if (rb) { rb.linearVelocity = Vector3.zero; rb.position = p + Vector3.up * 0.05f; }
            _cc.transform.position = p + Vector3.up * 0.05f;
        }

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        void Finish()
        {
            Debug.Log($"[ASH] TEST SUMMARY pass={_pass} fail={_fail}\n{_sum}");
            Destroy(gameObject);
        }
    }
}
