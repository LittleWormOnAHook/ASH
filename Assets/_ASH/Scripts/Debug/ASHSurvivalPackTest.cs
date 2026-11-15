using System.Collections;
using System.Linq;
using System.Text;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Survival pack smoke test (Play Mode, exec ASH.DebugTools.ASHSurvivalPackTest.Run): vendor pickup visuals in the
    /// scene + pick one up through the interactor, craft a torch and light it, lantern fuel/auto-refill/toggle, rifle hold + stub fire.
    /// Logs "[ASH] PACK ..." and "[ASH] PACK SUMMARY".</summary>
    public sealed class ASHSurvivalPackTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_PackTest").AddComponent<ASHSurvivalPackTest>(); }

        /// <summary>exec in Play (diagnostics): hold the rifle + lit lantern and log a camera pose facing the player for a bridge shot.</summary>
        public static void DiagEquip()
        {
            if (!Application.isPlaying) return;
            var eq = FindAnyObjectByType<ASHEquipment>(); var inv = FindAnyObjectByType<ASHInventory>(); var cc = FindAnyObjectByType<vThirdPersonController>();
            string main = DiagMain ?? "HuntingRifle", off = DiagOff ?? "Torch";
            var m = Item(main); var o = Item(off);
            if (m && inv.Count(m) == 0) inv.AddItem(m, 1); if (o && inv.Count(o) == 0) inv.AddItem(o, 1);
            if (m) eq.Equip(m); if (o) eq.Equip(o);
            var lan = Item("Lantern"); if (lan && inv.Count(lan) == 0) inv.AddItem(lan, 1); if (lan && eq.Hip != lan) eq.Equip(lan);
            var input = FindAnyObjectByType<ASHInput>(); if (input) input.SimSecondaryHeld = DiagAim ? true : (bool?)null;
            var t = cc.transform; var cam = t.position + t.forward * 1.9f + t.right * 0.4f + Vector3.up * 1.35f; var look = Quaternion.LookRotation((t.position + Vector3.up * 1.05f) - cam).eulerAngles;
            Debug.Log($"[ASH] DIAG pose {cam.x:F2},{cam.y:F2},{cam.z:F2}|{look.x:F1},{look.y:F1},40");
        }
        public static string DiagMain, DiagOff; public static bool DiagAim;
        public static void DiagTorchAim() { DiagMain = "Hatchet"; DiagOff = "Torch"; DiagAim = true; DiagEquip(); }
        public static void DiagFlashAim() { DiagMain = "Pistol"; DiagOff = "Flashlight"; DiagAim = true; DiagEquip(); }
        public static void DiagHatchetTorch() { DiagMain = "Hatchet"; DiagOff = "Torch"; DiagAim = false; DiagEquip(); }
        public static void DiagBowFlashlight() { DiagMain = "WoodenBow"; DiagOff = "Flashlight"; DiagAim = false; DiagEquip(); }
        public static void DiagPistol() { DiagMain = "Pistol"; DiagOff = "Lantern"; DiagEquip(); }

        ASHInput _in; vThirdPersonController _cc; ASHInteractor _ix; ASHInventory _inv; ASHCrafting _craft; ASHEquipment _eq;
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"PACK {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }

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
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var h, 8f, 1, QueryTriggerInteraction.Ignore)) p = h.point;
            var rb = _cc.GetComponent<Rigidbody>(); if (rb) { rb.linearVelocity = Vector3.zero; rb.position = p + Vector3.up * 0.05f; }
            _cc.transform.position = p + Vector3.up * 0.05f;
        }
        // Approach side whose eye ray reaches the pickup first (scene props like cabins may block one side).
        static Vector3 ClearSide(Transform pickup, Vector3 c)
        {
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, (Vector3.right + Vector3.forward).normalized, (Vector3.left + Vector3.back).normalized };
            foreach (var d in dirs)
            {
                var eye = pickup.position + d * 1.1f + Vector3.up * 1.6f;
                if (Physics.Raycast(eye, (c - eye).normalized, out var h, 5f, ~0, QueryTriggerInteraction.Collide) && h.collider.transform.IsChildOf(pickup)
                    && !Physics.CheckSphere(pickup.position + d * 1.1f + Vector3.up * 1.0f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) return d;
            }
            return Vector3.right;
        }
        void Aim(Vector3 target) { var eye = _cc.transform.position + Vector3.up * 1.6f; _ix.SimRay = new Ray(eye, (target - eye).normalized); }

        IEnumerator Start()
        {
            _in = FindAnyObjectByType<ASHInput>(); _cc = FindAnyObjectByType<vThirdPersonController>(); _ix = FindAnyObjectByType<ASHInteractor>();
            _inv = FindAnyObjectByType<ASHInventory>(); _craft = FindAnyObjectByType<ASHCrafting>(); _eq = FindAnyObjectByType<ASHEquipment>();
            var torch = Item("Torch"); var lantern = Item("Lantern"); var alcohol = Item("RubbingAlcohol"); var rifle = Item("HuntingRifle"); var rounds = Item("RifleRounds");
            Check("setup", _in && _cc && _ix && _inv && _craft && _eq && torch && lantern && alcohol && rifle && rounds, $"eq={(bool)_eq} craft={(bool)_craft} items={(bool)torch}/{(bool)lantern}/{(bool)rifle}");
            if (_fail > 0) { Finish(); yield break; }
            yield return new WaitForSeconds(1f);

            // 1) pickup visuals
            var pickups = FindObjectsByType<ASHItemPickup>(FindObjectsSortMode.None);
            int vendorVis = pickups.Count(p => { var v = p.transform.Find("Visual"); return v && v.childCount > 0 && v.GetComponentsInChildren<MeshFilter>().Any(m => m.sharedMesh && !m.sharedMesh.name.StartsWith("Cube") && !m.sharedMesh.name.StartsWith("Cylinder") && !m.sharedMesh.name.StartsWith("Sphere") && !m.sharedMesh.name.StartsWith("Capsule")); });
            var placed = GameObject.Find("ASH_SurvivalPackPickups");
            Check("pickup visuals swapped", vendorVis >= 30, $"{vendorVis}/{pickups.Length} pickups use vendor models; new item pickups placed {(placed ? placed.transform.childCount : 0)}");
            var lanternPickup = pickups.Where(p => p.name.Contains("Lantern")).OrderBy(p => (p.transform.position - _cc.transform.position).sqrMagnitude).FirstOrDefault();
            if (lanternPickup)
            {
                int l0 = _inv.Count(lantern);
                var c = lanternPickup.GetComponent<Collider>().bounds.center;
                Teleport(lanternPickup.transform.position + ClearSide(lanternPickup.transform, c) * 1.1f); yield return new WaitForSeconds(0.6f); Aim(c); yield return new WaitForSeconds(0.3f);
                string pr = _ix.Prompt;
                var hits = Physics.RaycastAll(_ix.SimRay.Value, 10f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance);
                string diag = $"target={(_ix.Target as Component)?.name} | player {_cc.transform.position} pickup {lanternPickup.transform.position} hits {string.Join(",", hits.Select(h => h.collider.name + ":" + h.distance.ToString("F1")))}";
                _in.SimInteractPressed = true; yield return new WaitForSeconds(0.4f);
                Check("lantern pickup through interactor", _inv.Count(lantern) > l0, $"prompt '{pr}', lantern {l0}->{_inv.Count(lantern)} {(_inv.Count(lantern) > l0 ? "" : diag)}");
                _ix.SimRay = null;
            }
            else Check("lantern pickup through interactor", false, "no lantern pickup in scene");
            if (_inv.Count(lantern) == 0) _inv.AddItem(lantern, 1);

            // 2) torch craft + light
            var recipe = _craft.Recipes.FirstOrDefault(r => r && r.recipeId == "torch");
            _inv.AddItem(Item("Cloth"), 1); _inv.AddItem(alcohol, 1);
#if UNITY_EDITOR
            _inv.AddItem(UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>("Assets/_ASH/Data/Items/ASH_Item_Deadwood.asset"), 1);
#endif
            int t0 = _inv.Count(torch);
            bool started = recipe && _craft.TryCraft(recipe);
            float w = 0f; while (_craft.IsCrafting && w < 6f) { w += Time.deltaTime; yield return null; }
            Check("torch crafted", _inv.Count(torch) > t0, $"recipe {(bool)recipe} started {started} torch {t0}->{_inv.Count(torch)} ({(recipe ? _craft.MissingReason(recipe) : "no recipe")})");
            if (_inv.Count(torch) > 0)
            {
                _eq.ToggleEquip(torch); yield return null;
                float f0 = _eq.Fuel(torch); yield return new WaitForSeconds(1f);
                var lightOn = _eq.OffVisual ? _eq.OffVisual.GetComponentInChildren<Light>() : null;
                Check("torch lit in hand", _eq.Off == torch && _eq.HandLightOn && lightOn && lightOn.enabled && _eq.Fuel(torch) < f0, $"off={(_eq.Off ? _eq.Off.displayName : "-")} on={_eq.HandLightOn} light={(lightOn ? lightOn.enabled.ToString() : "none")} burn {f0:F1}->{_eq.Fuel(torch):F1}s parent {(_eq.OffVisual ? _eq.OffVisual.transform.parent.name : "-")}");
            }

            // 2b) torch raised + RMB aim (body faces camera via strafe)
            var aim = FindAnyObjectByType<ASHLightAim>(); var cc = _cc;
            if (aim && _eq.Off == torch)
            {
                yield return new WaitForSeconds(0.5f);
                bool raised = aim.TorchRaised; int ik0 = aim.IKCalls; bool strafe0 = cc.isStrafing;
                _in.SimSecondaryHeld = true; yield return new WaitForSeconds(0.4f);
                bool aiming = aim.Aiming, strafing = cc.isStrafing; int ik1 = aim.IKCalls;
                _in.SimSecondaryHeld = false; yield return new WaitForSeconds(0.2f);
                Check("torch raised + RMB aim", raised && aiming && strafing && !aim.Aiming && cc.isStrafing == strafe0 && ik1 > ik0, $"raised={raised} aim={aiming} strafe {strafe0}->{strafing}->{cc.isStrafing} ikCalls +{ik1 - ik0} w={aim.Weight:F2}");
                _in.SimSecondaryHeld = null;
            }
            else Check("torch raised + RMB aim", false, $"aim={(bool)aim} off={(_eq.Off ? _eq.Off.displayName : "-")}");

            // 2c) flashlight in the left hand, RMB aims the beam
            var flashlight = Item("Flashlight");
            if (flashlight && aim)
            {
                _inv.AddItem(flashlight, 1); _eq.Equip(flashlight); yield return null;
                bool lit = _eq.HandLightOn;
                _in.SimSecondaryHeld = true; yield return new WaitForSeconds(0.6f);
                yield return new WaitForEndOfFrame(); // the aim turns the hand bone in LateUpdate (Invector animates in physics time)
                var beam = _eq.OffVisual ? _eq.OffVisual.GetComponentInChildren<Light>() : null;
                var cam = Camera.main; float dot = beam && cam ? Vector3.Dot(beam.transform.forward, cam.transform.forward) : -2f;
                bool aiming = aim.Aiming, strafing = cc.isStrafing;
                _in.SimSecondaryHeld = false; yield return new WaitForSeconds(0.2f); _in.SimSecondaryHeld = null;
                Check("flashlight left hand + RMB aim", _eq.Off == flashlight && lit && aiming && strafing && dot > 0.8f && !aim.Aiming, $"lit={lit} aim={aiming} strafe={strafing} beam.cam dot {dot:F2} parent {(_eq.OffVisual ? _eq.OffVisual.transform.parent.name : "-")}");
            }

            // 3) lantern on the left hip: equip (on), run dry -> pours alcohol from the pack, L toggles off/on
            _eq.Unequip(ASHEquipSlot.OffHand); yield return null;
            _inv.AddItem(alcohol, 2); int a0 = _inv.Count(alcohol);
            _eq.Equip(lantern); yield return null; yield return null;
            var hv = _eq.HipVisual;
            Check("lantern hangs on the hip, lit", _eq.Hip == lantern && _eq.HipLightOn && hv && !hv.transform.IsChildOf(_eq.Animator.GetBoneTransform(HumanBodyBones.LeftHand)) && hv.transform.lossyScale.x < 0.95f,
                $"hip={(_eq.Hip ? _eq.Hip.displayName : "-")} on={_eq.HipLightOn} scale {(hv ? hv.transform.lossyScale.x : 0f):F2} dist to hips {(hv ? Vector3.Distance(hv.transform.position, _eq.Animator.GetBoneTransform(HumanBodyBones.Hips).position) : -1f):F2}");
            _eq.SetFuel(lantern, 0.3f); yield return new WaitForSeconds(0.8f);
            Check("lantern refills from pack", _inv.Count(alcohol) == a0 - 1 && _eq.HipLightOn && _eq.Fuel(lantern) > 100f, $"alcohol {a0}->{_inv.Count(alcohol)} on={_eq.HipLightOn} fuel {_eq.Fuel(lantern):F0}s");
            _in.SimLight = true; yield return null; yield return null;
            float lf = _eq.Fuel(lantern); yield return new WaitForSeconds(0.5f);
            var ll = _eq.HipVisual ? _eq.HipVisual.GetComponentInChildren<Light>(true) : null;
            Check("lantern toggles off (L)", !_eq.HipLightOn && (!ll || !ll.enabled) && Mathf.Abs(_eq.Fuel(lantern) - lf) < 0.01f, $"on={_eq.HipLightOn} light={(ll ? ll.enabled.ToString() : "none")} fuel {lf:F1}->{_eq.Fuel(lantern):F1}");
            _in.SimLight = true; yield return null; yield return null;
            Check("lantern toggles on (L)", _eq.HipLightOn, $"on={_eq.HipLightOn}");

            // 4) rifle hold + stub fire
            _inv.AddItem(rifle, 1); _inv.AddItem(rounds, 3); int r0 = _inv.Count(rounds), s0 = _eq.ShotsFired;
            _eq.Equip(rifle); yield return null;
            _in.SimPrimary = true; yield return null; yield return null;
            Check("rifle held + fires a round", _eq.Main == rifle && _eq.MainVisual && _inv.Count(rounds) == r0 - 1 && _eq.ShotsFired == s0 + 1, $"main={(_eq.Main ? _eq.Main.displayName : "-")} visual={(bool)_eq.MainVisual} rounds {r0}->{_inv.Count(rounds)} parent {(_eq.MainVisual ? _eq.MainVisual.transform.parent.name : "-")}");
            var hatchet = Item("Hatchet");
            if (hatchet) { if (_inv.Count(hatchet) == 0) _inv.AddItem(hatchet, 1); _eq.Equip(hatchet); yield return null;
                float hs = _eq.MainVisual ? _eq.MainVisual.transform.lossyScale.x : 0f;
                Check("hatchet held at 0.8 scale", _eq.Main == hatchet && Mathf.Abs(hs - 0.8f) < 0.02f, $"main={(_eq.Main ? _eq.Main.displayName : "-")} scale {hs:F2} (item heldScale {hatchet.heldScale:F2})"); }
            _eq.Unequip(ASHEquipSlot.MainHand); _eq.Unequip(ASHEquipSlot.OffHand); _eq.Unequip(ASHEquipSlot.Hip);
            Finish();
        }

        void Finish() { if (_ix) _ix.SimRay = null; Debug.Log($"[ASH] PACK SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}
