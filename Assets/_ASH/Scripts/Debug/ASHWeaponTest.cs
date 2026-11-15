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
    /// <summary>Pass C smoke test (Play Mode, exec ASH.DebugTools.ASHWeaponTest.Run): bow (left hand) + arrow (right hand, draws),
    /// RMB nock/draw, release fires a gravity arrow (speed scales with draw), arrows stick + become pickups, rifle/pistol aim,
    /// fire, recoil and ammo, axe chop animation synced to the harvest hit. Logs "[ASH] WEAPON ..." + SUMMARY.</summary>
    public sealed class ASHWeaponTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_WeaponTest").AddComponent<ASHWeaponTest>(); }
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        ASHInput _in; vThirdPersonController _cc; ASHInteractor _ix; ASHInventory _inv; ASHEquipment _eq; ASHWeaponAim _wa; ASHChopAnimator _chop; ASHActionLayer _layer;
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"WEAPON {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }
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
        static Vector3 Centre(Component c) { var col = c.GetComponentsInChildren<Collider>().FirstOrDefault(x => x.enabled); return col ? col.bounds.center : c.transform.position + Vector3.up; }

        IEnumerator Start()
        {
            yield return null;
            _in = FindAnyObjectByType<ASHInput>(); _cc = FindAnyObjectByType<vThirdPersonController>(); _ix = FindAnyObjectByType<ASHInteractor>(); _inv = FindAnyObjectByType<ASHInventory>(); _eq = FindAnyObjectByType<ASHEquipment>();
            _wa = FindAnyObjectByType<ASHWeaponAim>(); _chop = FindAnyObjectByType<ASHChopAnimator>(); _layer = FindAnyObjectByType<ASHActionLayer>();
            var bow = Item("WoodenBow"); var arrow = Item("WoodenArrow"); var rifle = Item("HuntingRifle"); var rounds = Item("RifleRounds"); var pistol = Item("Pistol"); var pr = Item("PistolRounds"); var torch = Item("Torch"); var hatchet = Item("Hatchet");
            bool ok = _in && _cc && _inv && _eq && _wa && _chop && _layer && bow && arrow && rifle && pistol;
            Check("setup", ok && _layer.Valid && _layer.Has("Chop") && _layer.Has("BowAim") && _layer.Has("RifleShot"), $"wa={(bool)_wa} chop={(bool)_chop} layer valid={(_layer && _layer.Valid)} states={(_layer && _layer.Has("Chop"))}/{(_layer && _layer.Has("BowAim"))} arrowPrefab={(_wa && _wa.arrowPrefab)}");
            if (!ok) { Finish(); yield break; }
            var lh = _eq.Animator.GetBoneTransform(HumanBodyBones.RightHand); // string/arrow hand
            var bowHand = _eq.Animator.GetBoneTransform(HumanBodyBones.LeftHand);

            // bow: left hand, arrow in the right; equipping it drops a hand light
            _inv.AddItem(torch, 1); _inv.AddItem(bow, 1); _inv.AddItem(arrow, 10);
            _eq.Equip(torch); _eq.Equip(bow); yield return null; yield return null;
            var ha = _wa.HandArrow;
            Check("bow left hand, arrow right hand", _eq.Main == bow && _eq.Off == null && _eq.MainVisual && _eq.MainVisual.transform.IsChildOf(bowHand) && ha && ha.activeSelf && ha.transform.IsChildOf(lh),
                $"main={(_eq.Main ? _eq.Main.displayName : "-")} off={(_eq.Off ? _eq.Off.displayName : "-")} handArrow={(ha ? ha.transform.parent.name : "none")}");

            // RMB: nock + draw (strafe aim), hands together
            bool strafe0 = _cc.isStrafing;
            _in.SimSecondaryHeld = true; yield return new WaitForSeconds(1.1f);
            var bowPos = _eq.MainVisual.transform.position; float handsGap = Vector3.Distance(lh.position, bowPos);
            Vector3 arrowDir = ha ? ha.transform.forward : Vector3.zero; float alignDot = Vector3.Dot(arrowDir, (bowPos - lh.position).normalized);
            var aimFwd = Camera.main ? Camera.main.transform.forward : _cc.transform.forward; aimFwd.y = 0f; aimFwd.Normalize();
            float drawBack = Vector3.Dot(bowPos - lh.position, aimFwd); // right hand sits behind the bow along the aim
            Check("RMB nock + full draw (right hand draws)", _wa.Aiming && _cc.isStrafing && _wa.Draw01 >= 0.99f && _layer.State == "BowAim" && _layer.Weight > 0.5f && alignDot > 0.9f && drawBack > 0.15f && handsGap < 1.0f,
                $"aim={_wa.Aiming} strafe={_cc.isStrafing} draw={_wa.Draw01:F2} layer={_layer.State} w={_layer.Weight:F2} hands gap {handsGap:F2} m draw-back {drawBack:F2} m arrow->bow dot {alignDot:F2}");

            // release: full-draw arrow
            int a0 = _inv.Count(arrow); _in.SimPrimary = true; yield return null; yield return null;
            var full = ASHArrow.Last; float fullSpeed = full ? full.LaunchVelocity.magnitude : 0f;
            Check("release fires an arrow", full && _inv.Count(arrow) == a0 - 1 && fullSpeed > 40f && _layer.State.StartsWith("Bow"),
                $"arrows {a0}->{_inv.Count(arrow)} launch {fullSpeed:F1} m/s stuck={(full && full.Stuck)} in {(full && full.StuckIn ? full.StuckIn.name : "-")} after {(full ? full.FlightTime : 0f):F2}s layer={_layer.State}");
            // gravity arc + faces velocity: a level shot high above the player
            var gGo = Instantiate(_wa.arrowPrefab); gGo.transform.position = _cc.transform.position + Vector3.up * 40f;
            var g = gGo.AddComponent<ASHArrow>(); g.Launch(_cc.transform.forward * 30f, _cc.transform, 1f, 0.02f, 5f, arrow, false, 0f, 30);
            yield return new WaitForSeconds(0.4f);
            bool facing = g && Vector3.Dot(g.transform.forward, g.Velocity.normalized) > 0.99f;
            Check("gravity arc, faces velocity", g && g.Velocity.y < -2.5f && facing && g.transform.position.y < g.LaunchPosition.y - 0.3f, $"vy {(g ? g.Velocity.y : 0f):F2} drop {(g ? g.LaunchPosition.y - g.transform.position.y : 0f):F2} m faces={facing}");
            if (g) Destroy(g.gameObject);
            // short draw = slower
            _in.SimSecondaryHeld = false; yield return null; yield return null; // let go, then a fresh short draw
            _in.SimSecondaryHeld = true; yield return new WaitForSeconds(0.35f);
            _in.SimPrimary = true; yield return null; yield return null;
            var weak = ASHArrow.Last; float weakSpeed = weak && weak != full ? weak.LaunchVelocity.magnitude : 0f;
            Check("launch speed scales with draw", weak && weak != full && weakSpeed > 0f && weakSpeed < fullSpeed - 5f, $"full {fullSpeed:F1} vs short draw {weakSpeed:F1} m/s");
            _in.SimSecondaryHeld = false; yield return new WaitForSeconds(0.4f);
            Check("RMB release restores", !_wa.Aiming && _cc.isStrafing == strafe0, $"aim={_wa.Aiming} strafe {_cc.isStrafing} (was {strafe0}) layer={_layer.State} w={_layer.Weight:F2}");

            // stick + recover: a test arrow shot at the ground in front of the player
            var go = Instantiate(_wa.arrowPrefab ? _wa.arrowPrefab : new GameObject("tmp"));
            var start = _cc.transform.position + Vector3.up * 1.5f + _cc.transform.forward * 0.6f; go.transform.position = start;
            var dir = (_cc.transform.forward * 0.8f + Vector3.down).normalized;
            var test = go.AddComponent<ASHArrow>(); test.Launch(dir * 25f, _cc.transform, 1f, 0.02f, 60f, arrow, true, 1f, 30);
            float tw = 0f; while (test && !test.Stuck && tw < 3f) { tw += Time.deltaTime; yield return null; }
            var pick = test ? test.GetComponent<ASHItemPickup>() : null;
            Check("arrow sticks + is recoverable", test && test.Stuck && pick && pick.Item == arrow, $"stuck={(test && test.Stuck)} in {(test && test.StuckIn ? test.StuckIn.name : "-")} after {tw:F2}s pickup={(bool)pick}");
            if (pick) { int c0 = _inv.Count(arrow); var ray = new Ray(_cc.transform.position + Vector3.up * 1.6f, (test.transform.position - (_cc.transform.position + Vector3.up * 1.6f)).normalized); _ix.SimRay = ray; yield return new WaitForSeconds(0.2f); _in.SimInteractPressed = true; yield return new WaitForSeconds(0.2f); _ix.SimRay = null;
                Check("recover arrow via interact", _inv.Count(arrow) == c0 + 1, $"arrows {c0}->{_inv.Count(arrow)} target was {(_ix.Target as Component)?.name}"); }

            // rifle: aim, fire, recoil, ammo
            _inv.AddItem(rifle, 1); _inv.AddItem(rounds, 5); _eq.Equip(rifle); yield return null;
            _in.SimSecondaryHeld = true; yield return new WaitForSeconds(0.5f);
            string rState = _layer.State; int r0 = _inv.Count(rounds); int s0 = _wa.Shots;
            _in.SimPrimary = true; yield return null; yield return null;
            var kick = Camera.main ? Camera.main.GetComponent<ASHCameraKick>() : null;
            Check("rifle aim + fire + recoil", rState == "RifleAim" && _wa.Aiming && _inv.Count(rounds) == r0 - 1 && _wa.Shots == s0 + 1 && kick && kick.Pitch > 0.5f && _wa.KickAmount > 0.3f,
                $"layer {rState} rounds {r0}->{_inv.Count(rounds)} camKick {(kick ? kick.Pitch : 0f):F2} weaponKick {_wa.KickAmount:F2} hit {(_wa.LastHit.HasValue ? _wa.LastHit.Value.collider.name : "none")}");
            _in.SimSecondaryHeld = false; yield return new WaitForSeconds(0.3f);

            // pistol: aim pose + one round
            _inv.AddItem(pistol, 1); _inv.AddItem(pr, 5); _eq.Equip(pistol); yield return null;
            _in.SimSecondaryHeld = true; yield return new WaitForSeconds(0.4f);
            string pState = _layer.State; int p0 = _inv.Count(pr);
            _in.SimPrimary = true; yield return null; yield return null;
            Check("pistol aim + fire", pState == "PistolAim" && _inv.Count(pr) == p0 - 1, $"layer {pState} rounds {p0}->{_inv.Count(pr)}");
            _in.SimSecondaryHeld = null; yield return new WaitForSeconds(0.3f);

            // chop animation on a log/stump/tree
            if (_inv.Count(hatchet) == 0) _inv.AddItem(hatchet, 1);
            for (int i = 0; i < _inv.Slots.Count; i++) if (_inv.Slots[i].item == hatchet) { if (!_inv.IsHotbar(i)) _inv.Move(i, 0); break; }
            var p = _cc.transform.position;
            var target = FindObjectsByType<ASHChoppable>(FindObjectsSortMode.None).Where(c => !c.Done && !c.Busy && c.HitsLeft > 1).OrderBy(c => (c.transform.position - p).sqrMagnitude).FirstOrDefault();
            if (target)
            {
                var side = Vector3.Cross(Vector3.up, target.transform.forward).normalized;
                Teleport(target.transform.position - side * 1.6f); yield return new WaitForSeconds(0.6f);
                var aimAt = target.Kind == ASHChopKind.StandingTree ? target.transform.position + Vector3.up * 1.1f : Centre(target);
                var eye = _cc.transform.position + Vector3.up * 1.6f; _ix.SimRay = new Ray(eye, (aimAt - eye).normalized); yield return new WaitForSeconds(0.25f);
                int h0 = target.HitsLeft; int imp0 = _chop.Impacts; var mainBefore = _eq.Main;
                _in.SimInteractHeld = true; float t = 0f; bool sawChop = false, locked = false, axeHeld = false; float minClip = 1f, maxClip = 0f;
                while (t < 6f && target.HitsLeft >= h0 - 1 && _chop.Impacts < imp0 + 2) { t += Time.deltaTime; if (_chop.Chopping) { sawChop |= _layer.State == "Chop" && _layer.Weight > 0.3f; locked |= _chop.MovementLocked; axeHeld |= _eq.Main && _eq.Main.toolType == ASHToolType.Axe; minClip = Mathf.Min(minClip, _chop.ClipTime); maxClip = Mathf.Max(maxClip, _chop.ClipTime); } yield return null; }
                _in.SimInteractHeld = null; _ix.SimRay = null;
                yield return new WaitForSeconds(0.6f);
                Check("chop swing synced to hit", sawChop && locked && axeHeld && target.HitsLeft < h0 && _chop.Impacts > imp0 && maxClip - minClip > 0.5f,
                    $"{target.name} ({target.Kind}) hits {h0}->{target.HitsLeft} impacts +{_chop.Impacts - imp0} anim={sawChop} locked={locked} axeInHand={axeHeld} clip {minClip:F2}..{maxClip:F2}");
                Check("chop releases", !_chop.Chopping && !_chop.MovementLocked && _eq.Main == mainBefore, $"chopping={_chop.Chopping} locked={_chop.MovementLocked} main={(_eq.Main ? _eq.Main.displayName : "-")} (before {(mainBefore ? mainBefore.displayName : "-")})");
            }
            else Check("chop target", false, "no choppable found");
            _eq.Unequip(ASHEquipSlot.MainHand);
            Finish();
        }

        void Finish() { if (_ix) _ix.SimRay = null; if (_in) { _in.SimSecondaryHeld = null; _in.SimInteractHeld = null; } Debug.Log($"[ASH] WEAPON SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}
