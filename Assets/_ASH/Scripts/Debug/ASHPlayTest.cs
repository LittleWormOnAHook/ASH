using System.Collections;
using System.Linq;
using System.Text;
using ASH.Core;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Map;
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
            ASH.Opening.ASHEruptionSequence.SuppressIfPresent(); // Phase 2A opening: back to the scene spawn, effects off
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

            // ---- survey: map obscured -> log the Redstone lookout -> map revealed
            var hud0 = FindAnyObjectByType<ASH.UI.ASHHud>(); var gps = ASHGpsMap.Instance;
            System.IO.Directory.CreateDirectory("AshBridge/shots/v3");
            if (gps && hud0 && _obj && _obj.FirstSurveyPoint)
            {
                Check("objective starts at survey", _obj.Current == ASHObjectiveTracker.Stage.Survey, "stage=" + _obj.Current);
                float cov0 = gps.Fog.Coverage01();
                hud0.OpenMap(true); yield return new WaitForSeconds(0.4f);
                Check("map opens (GPS)", hud0.MapUI && hud0.MapUI.IsOpen, $"open={(hud0.MapUI && hud0.MapUI.IsOpen)} coverage={cov0 * 100f:F2}%");
                ScreenCapture.CaptureScreenshot("AshBridge/shots/v3/09_map_obscured.png"); yield return null; yield return null;
                hud0.OpenMap(false); yield return null;
                var sp = _obj.FirstSurveyPoint.GetComponent<ASHSurveyPoint>();
                var spPos = _obj.FirstSurveyPoint.position;
                Teleport(spPos - _obj.FirstSurveyPoint.forward * 1.3f, false); yield return new WaitForSeconds(0.6f);
                Vector3 eye = tr.position + Vector3.up * 1.6f; var spc = sp.GetComponent<Collider>();
                _ix.SimRay = new Ray(eye, ((spc ? spc.bounds.center : spPos + Vector3.up) - eye).normalized);
                yield return new WaitForSeconds(0.2f); string sprompt = _ix.Prompt;
                _in.SimInteractHeld = true; float sw = 0f;
                while (!gps.IsLogged(sp.Definition) && sw < 6f) { sw += Time.deltaTime; yield return null; }
                _in.SimInteractHeld = false; _ix.SimRay = null;
                float cov1 = gps.Fog.Coverage01();
                Check("survey logged (interactor)", gps.IsLogged(sp.Definition), $"prompt='{sprompt}' in {sw:F1}s, onTower y={tr.position.y - spPos.y:F1}");
                Check("survey reveals map", cov1 > cov0 + 0.01f && gps.IsPoiKnown("ranger_station") && gps.IsPoiKnown("the_overlook"), $"coverage {cov0 * 100f:F1}%->{cov1 * 100f:F1}%, ranger={gps.IsPoiKnown("ranger_station")} overlook={gps.IsPoiKnown("the_overlook")}");
                Check("objective after survey", _obj.Current == ASHObjectiveTracker.Stage.Gather, "stage=" + _obj.Current);
                var log = ASHJournalLog.Instance; Check("journal note added", log && log.Entries.Count > 0, $"entries={(log ? log.Entries.Count : 0)}");
                hud0.OpenMap(true); yield return new WaitForSeconds(0.7f);
                ScreenCapture.CaptureScreenshot("AshBridge/shots/v3/10_map_after_survey.png"); yield return null; yield return null;
                hud0.OpenMap(false); yield return null;
            }
            else Check("survey setup", false, $"gps={(bool)gps} hud={(bool)hud0} obj={(bool)_obj} firstSurvey={(_obj && _obj.FirstSurveyPoint)}");

            // gather each resource type through the interactor (tool-gated salvage is tested separately)
            var nodes = FindObjectsByType<ASHResourceNode>().Where(n => n.RequiredTool == ASHToolType.None && !n.Depleted).ToArray();
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

            // ---- cans: smashed without an opener vs opened with a knife
            var beans = Def("cannedbeans"); var tin = Def("emptytin"); var knife = Def("huntingknife"); var shirt = Def("oldshirt"); var cloth = Def("cloth");
            if (beans && tin && knife)
            {
                foreach (var s in _inv.Slots.ToArray()) if (!s.IsEmpty && (s.item.toolType == ASHToolType.Knife || s.item.toolType == ASHToolType.Axe || s.item.toolType == ASHToolType.CanOpener)) _inv.Remove(s.item, s.amount);
                _inv.AddItem(beans, 2); _st.SetHunger(10f); int t0 = _inv.Count(tin); float h0 = _st.Hunger;
                bool ok1 = _inv.TryConsume(beans, _st); bool smashed = _inv.LastConsumeSmashed; float gain1 = _st.Hunger - h0; int t1 = _inv.Count(tin);
                _inv.AddItem(knife, 1); _st.SetHunger(10f);
                bool ok2 = _inv.TryConsume(beans, _st); bool smashed2 = _inv.LastConsumeSmashed; float gain2 = _st.Hunger - 10f; int t2 = _inv.Count(tin);
                Check("can smashed vs opened", ok1 && smashed && t1 == t0 && ok2 && !smashed2 && t2 == t1 + 1 && gain2 > gain1, $"smash: +{gain1:F1} hunger tins {t0}->{t1}; knife: +{gain2:F1} tins {t1}->{t2}");
            }
            else Check("can smashed vs opened", false, $"items beans={(bool)beans} tin={(bool)tin} knife={(bool)knife}");
            // ---- break down a shirt with the knife -> cloth, then craft a bandage
            if (shirt && cloth)
            {
                int c0 = _inv.Count(cloth); _inv.AddItem(shirt, 1);
                bool bd = _cr.TryBreakDown(shirt); float bw = 0; while (_inv.Count(shirt) > 0 && bw < 8f) { bw += Time.deltaTime; yield return null; }
                yield return null;
                Check("break down shirt (knife)", bd && _inv.Count(cloth) >= c0 + 2, $"started={bd} cloth {c0}->{_inv.Count(cloth)} in {bw:F1}s");
                var bandageR = _cr.Recipes.FirstOrDefault(r => r && r.recipeId == "bandage");
                if (bandageR)
                {
                    var hudJ = FindAnyObjectByType<ASH.UI.ASHHud>();
                    if (hudJ) { hudJ.OpenJournal(ASH.UI.ASHJournalUI.Tab.Crafting); hudJ.Journal.SelectRecipe(bandageR); yield return new WaitForSeconds(0.4f);
                        ScreenCapture.CaptureScreenshot("AshBridge/shots/v3/08_journal_crafting.png"); yield return null; yield return null; }
                    int b0 = _inv.Count(bandageR.outputItem); bool st = _cr.TryCraft(bandageR); float w2 = 0; while (_cr.IsCrafting && w2 < 8f) { w2 += Time.deltaTime; yield return null; }
                    Check("craft bandage", st && _inv.Count(bandageR.outputItem) == b0 + 1, $"started={st} bandages {b0}->{_inv.Count(bandageR.outputItem)}");
                    if (hudJ) hudJ.CloseAllPanels();
                }
                else Check("craft bandage", false, "no bandage recipe on player");
            }
            else Check("break down shirt (knife)", false, "items missing");

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
            // ---- cook raw meat at the placed, lit campfire
            var cookR = _cr.Recipes.FirstOrDefault(r => r && r.recipeId == "cookmeat");
            var fireNow = FindObjectsByType<ASHCampfire>().FirstOrDefault(c => c.name != "ASH_PlacementGhost");
            if (cookR && fireNow)
            {
                fireNow.SetLit(true); Teleport(fireNow.transform.position + Vector3.right * 1.6f); yield return new WaitForSeconds(0.4f);
                _inv.AddItem(cookR.ingredients[0].item, 1); int k0 = _inv.Count(cookR.outputItem);
                string why = _cr.MissingReason(cookR); bool st = _cr.TryCraft(cookR); float w3 = 0; while (_cr.IsCrafting && w3 < 10f) { w3 += Time.deltaTime; yield return null; }
                Check("cook meat at campfire", st && _inv.Count(cookR.outputItem) == k0 + 1, $"started={st} reason='{why}' cooked {k0}->{_inv.Count(cookR.outputItem)}");
            }
            else Check("cook meat at campfire", false, $"recipe={(bool)cookR} fire={(bool)fireNow}");
            // ---- hatchet salvage: crate needs the axe
            var crate = FindObjectsByType<ASHResourceNode>().Where(n => n.RequiredTool == ASHToolType.Axe && !n.Depleted && n.transform.root.name != "ASH_Phase2A").OrderBy(n => (n.transform.position - tr.position).sqrMagnitude).FirstOrDefault();
            var hatchet = Def("hatchet");
            if (crate && hatchet)
            {
                _inv.AddItem(hatchet, 1); var it = crate.Item; int w0 = _inv.Count(it);
                Teleport(crate.transform.position + Away(crate.transform.position) * 1.6f); yield return new WaitForSeconds(0.5f);
                Vector3 eye = tr.position + Vector3.up * 1.6f; var col = crate.GetComponentInChildren<Collider>();
                _ix.SimRay = new Ray(eye, ((col ? col.bounds.center : crate.transform.position) - eye).normalized); yield return new WaitForSeconds(0.2f);
                string pr = _ix.Prompt; _in.SimInteractHeld = true; float w4 = 0;
                while (_inv.Count(it) <= w0 && w4 < 6f) { w4 += Time.deltaTime; yield return null; }
                _in.SimInteractHeld = false; _ix.SimRay = null;
                Check("hatchet salvage", _inv.Count(it) > w0, $"prompt='{pr}' {it.displayName} {w0}->{_inv.Count(it)}");
            }
            else Check("hatchet salvage", false, $"crate={(bool)crate} hatchet={(bool)hatchet}");

            if (_obj) Check("objective complete", _obj.Current == ASHObjectiveTracker.Stage.Complete, "stage=" + _obj.Current);

            // needs decay sanity
            float hu = _st.Hunger, th = _st.Thirst; yield return new WaitForSeconds(2f);
            Check("needs decay", _st.Hunger < hu && _st.Thirst < th, $"hunger {hu:F3}->{_st.Hunger:F3} thirst {th:F3}->{_st.Thirst:F3}");
            Finish();
        }

        Vector3 Away(Vector3 p) { var d = _cc.transform.position - p; d.y = 0; return d.sqrMagnitude < 0.01f ? Vector3.back : d.normalized; }

        static ASHItemDefinition Def(string id) => Resources.FindObjectsOfTypeAll<ASHItemDefinition>().FirstOrDefault(d => d.itemId == id);

        void Teleport(Vector3 p, bool snap = true)
        {
            if (snap && Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var h, 100f, 1, QueryTriggerInteraction.Ignore)) p = h.point;
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
