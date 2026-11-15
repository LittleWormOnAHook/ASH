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
    /// RMB nock/draw, string toward the archer, arrow nocked forward, string pulled, RMB release never fires, LMB while aiming fires a gravity arrow from the bow rest (speed scales with draw), arrows stick + become pickups, rifle/pistol aim,
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

            // edit-mode placement: every held item is a pre-placed child, hidden until equipped, never moved at runtime
            var heldItems = Resources.FindObjectsOfTypeAll<ASHItemDefinition>().Where(i => i && i.IsEquippable && i.heldPrefab).ToList();
            var missing = heldItems.Where(i => !_eq.Placed(i)).Select(i => ASHEquipment.HeldName(i)).ToList();
            if (!_eq.Placed(ASHEquipment.HandArrowName)) missing.Add(ASHEquipment.HandArrowName);
            var placedAll = _eq.AllPlaced.Where(g => g).ToList();
            int visibleIdle = placedAll.Count(g => g.activeSelf && g != _eq.MainVisual && g != _eq.OffVisual && g != _eq.HipVisual && g != _wa.HandArrow);
            var authored = placedAll.ToDictionary(g => g, g => (g.transform.parent, g.transform.localPosition, g.transform.localRotation, g.transform.localScale));
            Check("held items pre-placed, hidden", missing.Count == 0 && visibleIdle == 0, $"placed {placedAll.Count} missing [{string.Join(",", missing)}] visible-unequipped {visibleIdle}");
            var lh = _eq.Animator.GetBoneTransform(HumanBodyBones.RightHand); // string/arrow hand
            var bowHand = _eq.Animator.GetBoneTransform(HumanBodyBones.LeftHand);

            // bow: left hand, arrow in the right; equipping it drops a hand light
            _inv.AddItem(torch, 1); _inv.AddItem(bow, 1); _inv.AddItem(arrow, 10);
            _eq.Equip(torch); _eq.Equip(bow); yield return null; yield return null;
            var ha = _wa.HandArrow;
            Check("bow left hand, arrow right hand", _eq.Main == bow && _eq.Off == null && _eq.MainVisual && _eq.MainVisual == _eq.Placed(bow) && _eq.MainVisual.transform.IsChildOf(bowHand) && ha && ha == _eq.Placed(ASHEquipment.HandArrowName) && ha.activeSelf && ha.transform.IsChildOf(lh),
                $"main={(_eq.Main ? _eq.Main.displayName : "-")} off={(_eq.Off ? _eq.Off.displayName : "-")} handArrow={(ha ? ha.transform.parent.name : "none")}");

            // RMB: nock + draw (strafe aim), hands together
            bool strafe0 = _cc.isStrafing;
            var chest = _eq.Animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? _eq.Animator.GetBoneTransform(HumanBodyBones.Chest);
            _in.SimSecondaryHeld = true; { float tw0 = 0f; while (_wa.Draw01 < 0.99f && tw0 < 3f) { tw0 += Time.deltaTime; yield return null; } } yield return new WaitForSeconds(0.5f); yield return new WaitForEndOfFrame();
            var bowPos = _eq.MainVisual.transform.position; float handsGap = Vector3.Distance(lh.position, bowPos);
            var aimFwd = Camera.main ? Camera.main.transform.forward : _cc.transform.forward; var aimFlat = Vector3.ProjectOnPlane(aimFwd, Vector3.up).normalized;
            var restW = _wa.ArrowRestWorld; var nockW = _wa.NockWorld;
            float drawBack = Vector3.Dot(restW - nockW, aimFlat); // string fingers sit behind the arrow rest along the aim
            Check("RMB nock + full draw (right hand draws)", _wa.Aiming && _cc.isStrafing && _wa.Draw01 >= 0.99f && _layer.State == "BowAim" && _layer.Weight > 0.5f && drawBack > 0.45f && handsGap < 1.1f,
                $"aim={_wa.Aiming} strafe={_cc.isStrafing} draw={_wa.Draw01:F2} layer={_layer.State} w={_layer.Weight:F2} hands gap {handsGap:F2} m draw-back {drawBack:F2} m");
            // bow orientation: limbs up, string side toward the archer (not the limbs' back)
            var sDir = _wa.BowStringDirWorld; var toArcher = Vector3.ProjectOnPlane((chest ? chest.position : _cc.transform.position + Vector3.up * 1.4f) - restW, Vector3.up).normalized;
            float stringDot = Vector3.Dot(sDir, toArcher), limbUp = Vector3.Dot(_eq.MainVisual.transform.forward, Vector3.up);
            Check("string faces the archer at full draw", _wa.BowGeometryOk && stringDot > 0.6f && limbUp > 0.85f,
                $"string->archer dot {stringDot:F2} limbs-up dot {limbUp:F2} string side sign {_wa.BowStringSign:+0;-0} geometry={_wa.BowGeometryOk}");
            // nocked arrow: nock on the string at the fingers, resting on the bow, tip forward along the aim
            ha = _wa.HandArrow;
            Vector3 aF = ha ? ha.transform.forward : Vector3.zero, aP = ha ? ha.transform.position : Vector3.zero;
            var aimLine = (_wa.AimPoint(out _) - restW).normalized;
            float fwdDot = Vector3.Dot(aF, aimLine), restOff = ha ? Vector3.Cross(aF, restW - aP).magnitude : 9f, nockOff = ha ? Vector3.Distance(aP, nockW) : 9f, fingerOff = Vector3.Distance(nockW, _wa.FingerWorld);
            Check("arrow nocked on the string, forward along the aim", ha && ha.name == "ASH_NockArrow" && ha.activeInHierarchy && fwdDot > 0.97f && restOff < 0.04f && nockOff < 0.03f && fingerOff < 0.08f && Vector3.Dot(restW - aP, aF) > 0.3f,
                $"arrow={(ha ? ha.name : "none")} fwd.aimLine {fwdDot:F3} rest off line {restOff * 100f:F1} cm nock off {nockOff * 100f:F1} cm nock-fingers {fingerOff * 100f:F1} cm");
            Check("string pulled to the drawing hand", _wa.BowStringVerts > 0 && _wa.StringPullMetres > 0.3f,
                $"string verts {_wa.BowStringVerts} pull {_wa.StringPullMetres:F2} m");
            // bow arm: left hand in toward the chest centre line, in front of the chest, elbow slightly bent
            {
                var an = _eq.Animator; var lua = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); var rua = an.GetBoneTransform(HumanBodyBones.RightUpperArm); var lla = an.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var midS = (lua.position + rua.position) * 0.5f; var rt = _cc.transform.right;
                float lat = Vector3.Dot(bowHand.position - midS, rt), fwd = Vector3.Dot(bowHand.position - midS, aimFlat);
                float bend = Vector3.Angle(lla.position - lua.position, bowHand.position - lla.position);
                Check("bow hand in front of the chest, elbow bent", lat > -0.2f && lat < 0.08f && fwd > 0.3f && bend > 4f && bend < 60f,
                    $"bow hand lateral {lat:F2} m (centre 0, - = left) forward {fwd:F2} m elbow bend {bend:F0} deg");
            }
            Check("aim camera over the right shoulder", _wa.CameraAiming && _wa.CameraTargetState == ASHWeaponAim.AimCameraState && _wa.CameraRightNow > _wa.aimCameraRight * 0.7f,
                $"aiming={_wa.CameraAiming} state={_wa.CameraTargetState} right now {_wa.CameraRightNow:F2} (target {_wa.aimCameraRight:F2})");

            // holds full draw while RMB stays down
            yield return new WaitForSeconds(2f);
            float nt = _layer.NormalizedTime;
            Check("bow holds full draw until RMB release", _wa.Aiming && _wa.Draw01 >= 0.99f && _layer.State == "BowAim" && _layer.Weight > 0.9f && nt >= _wa.bowAimHoldNormalized - 0.05f && nt <= _wa.bowAimHoldNormalized + 0.05f,
                $"aim={_wa.Aiming} draw={_wa.Draw01:F2} state={_layer.State} w={_layer.Weight:F2} clip t {nt:F2} (hold {_wa.bowAimHoldNormalized:F2})");
            // RMB release NEVER fires: after the long hold, after a short hold, and an LMB click right after letting go
            var lastBefore = ASHArrow.Last; int shots0 = _wa.Shots, ar0 = _inv.Count(arrow), live0 = ASHArrow.Live.Count;
            _in.SimSecondaryHeld = false; yield return new WaitForSeconds(0.6f);
            bool noFireLong = _wa.Shots == shots0 && _inv.Count(arrow) == ar0 && ASHArrow.Last == lastBefore && ASHArrow.Live.Count == live0;
            _in.SimSecondaryHeld = true; yield return new WaitForSeconds(0.35f); _in.SimSecondaryHeld = false; yield return new WaitForSeconds(0.6f);
            bool noFireShort = _wa.Shots == shots0 && _inv.Count(arrow) == ar0 && ASHArrow.Last == lastBefore;
            _in.SimSecondaryHeld = true; yield return new WaitForSeconds(0.6f); _in.SimSecondaryHeld = false; yield return null; _in.SimPrimary = true; yield return null; yield return null; yield return new WaitForSeconds(0.3f);
            bool noFireLmbAfter = _wa.Shots == shots0 && _inv.Count(arrow) == ar0 && ASHArrow.Last == lastBefore;
            Check("RMB release never fires", noFireLong && noFireShort && noFireLmbAfter && !_wa.Aiming,
                $"after long hold {noFireLong}, after short hold {noFireShort}, LMB just after letting go {noFireLmbAfter}; shots {shots0}->{_wa.Shots} arrows {ar0}->{_inv.Count(arrow)} layer={_layer.State}");

            // LMB while aiming: full-draw arrow, launched from the arrow rest on the bow toward the crosshair
            _in.SimSecondaryHeld = true; yield return new WaitForSeconds(1.2f); yield return new WaitForEndOfFrame();
            var restAtShot = _wa.ArrowRestWorld; var aimPt = _wa.AimPoint(out _);
            int a0 = _inv.Count(arrow); _in.SimPrimary = true; yield return null; yield return null;
            var full = ASHArrow.Last; float fullSpeed = full && full != lastBefore ? full.LaunchVelocity.magnitude : 0f;
            Check("LMB while aiming fires an arrow", full && full != lastBefore && _inv.Count(arrow) == a0 - 1 && fullSpeed > 40f && _layer.State.StartsWith("Bow"),
                $"arrows {a0}->{_inv.Count(arrow)} launch {fullSpeed:F1} m/s stuck={(full && full.Stuck)} in {(full && full.StuckIn ? full.StuckIn.name : "-")} layer={_layer.State}");
            float spawnOff = full ? Vector3.Distance(full.LaunchPosition, restAtShot) : 9f, spawnBody = full ? Vector3.Distance(full.LaunchPosition, chest ? chest.position : bowPos) : 0f;
            float aimDot = full ? Vector3.Dot(full.LaunchVelocity.normalized, (aimPt - full.LaunchPosition).normalized) : 0f;
            Check("arrow spawns at the bow, aimed at the crosshair", full && spawnOff < 0.08f && spawnBody > 0.35f && aimDot > 0.995f,
                $"spawn-rest {spawnOff * 100f:F1} cm spawn-chest {spawnBody:F2} m dir.toCrosshair {aimDot:F4}");
            // gravity arc + faces velocity: a level shot high above the player
            var gGo = Instantiate(_wa.arrowPrefab); gGo.transform.position = _cc.transform.position + Vector3.up * 40f;
            var g = gGo.AddComponent<ASHArrow>(); g.Launch(_cc.transform.forward * 30f, _cc.transform, 1f, 0.02f, 5f, arrow, false, 0f, 30);
            yield return new WaitForSeconds(0.4f);
            bool facing = g && Vector3.Dot(g.transform.forward, g.Velocity.normalized) > 0.99f;
            Check("gravity arc, faces velocity", g && g.Velocity.y < -2.5f && facing && g.transform.position.y < g.LaunchPosition.y - 0.3f, $"vy {(g ? g.Velocity.y : 0f):F2} drop {(g ? g.LaunchPosition.y - g.transform.position.y : 0f):F2} m faces={facing}");
            if (g) Destroy(g.gameObject);
            // short draw = slower
            _in.SimSecondaryHeld = false; yield return new WaitForSeconds(0.3f); // let go, then a fresh short draw
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
            yield return new WaitForEndOfFrame();
            var rIK = _eq.MainVisual ? _eq.MainVisual.transform.Find(ASHWeaponAim.LeftHandIKName) : null;
            Check("rifle support hand on LeftHandIK", rIK && _wa.GunLeftHandError >= 0f && _wa.GunLeftHandError < 0.03f && _wa.GunLeftHandWeight > 0.95f,
                $"target={(bool)rIK} left hand error {_wa.GunLeftHandError * 100f:F1} cm weight {_wa.GunLeftHandWeight:F2} reach {_wa.GunLeftReach:F2}/{_wa.GunLeftArmLength:F2} m pulled back {_wa.GunPull * 100f:F0} cm target(root) {(_eq.MainVisual && _eq.MainVisual.transform.Find(ASHWeaponAim.LeftHandIKName) ? _wa.transform.InverseTransformPoint(_eq.MainVisual.transform.Find(ASHWeaponAim.LeftHandIKName).position).ToString("F2") : "-")}");
            string rState = _layer.State; int r0 = _inv.Count(rounds); int s0 = _wa.Shots;
            _in.SimPrimary = true; yield return null; yield return null;
            var kick = Camera.main ? Camera.main.GetComponent<ASHCameraKick>() : null;
            Check("rifle aim + fire + recoil", rState == "RifleAim" && _wa.Aiming && _inv.Count(rounds) == r0 - 1 && _wa.Shots == s0 + 1 && kick && kick.Pitch > 0.5f && _wa.KickAmount > 0.3f,
                $"layer {rState} rounds {r0}->{_inv.Count(rounds)} camKick {(kick ? kick.Pitch : 0f):F2} weaponKick {_wa.KickAmount:F2} hit {(_wa.LastHit.HasValue ? _wa.LastHit.Value.collider.name : "none")}");
            _in.SimSecondaryHeld = false; yield return new WaitForSeconds(0.3f);

            // pistol: aim pose + one round
            _inv.AddItem(pistol, 1); _inv.AddItem(pr, 5); _eq.Equip(pistol); yield return null;
            _in.SimSecondaryHeld = true; yield return new WaitForSeconds(0.4f);
            yield return new WaitForEndOfFrame();
            var pIK = _eq.MainVisual ? _eq.MainVisual.transform.Find(ASHWeaponAim.LeftHandIKName) : null;
            Check("pistol support hand on LeftHandIK", pIK && _wa.GunLeftHandError >= 0f && _wa.GunLeftHandError < 0.03f && _wa.GunLeftHandWeight > 0.95f,
                $"target={(bool)pIK} left hand error {_wa.GunLeftHandError * 100f:F1} cm weight {_wa.GunLeftHandWeight:F2} reach {_wa.GunLeftReach:F2}/{_wa.GunLeftArmLength:F2} m pulled back {_wa.GunPull * 100f:F0} cm target(root) {(_eq.MainVisual && _eq.MainVisual.transform.Find(ASHWeaponAim.LeftHandIKName) ? _wa.transform.InverseTransformPoint(_eq.MainVisual.transform.Find(ASHWeaponAim.LeftHandIKName).position).ToString("F2") : "-")}");
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
            _eq.Unequip(ASHEquipSlot.MainHand); _eq.Unequip(ASHEquipSlot.OffHand); yield return null; yield return null;
            int moved = authored.Count(kv => kv.Key && (kv.Key.transform.parent != kv.Value.Item1 || (kv.Key.transform.localPosition - kv.Value.Item2).sqrMagnitude > 1e-10f
                || Quaternion.Angle(kv.Key.transform.localRotation, kv.Value.Item3) > 0.01f || (kv.Key.transform.localScale - kv.Value.Item4).sqrMagnitude > 1e-10f));
            int leftOn = placedAll.Count(g => g && g.activeSelf && g != _eq.HipVisual);
            Check("hand placements untouched after aim/fire/chop", moved == 0 && leftOn == 0, $"moved {moved}/{authored.Count} still visible {leftOn}");
            Finish();
        }

        void Finish() { if (_ix) _ix.SimRay = null; if (_in) { _in.SimSecondaryHeld = null; _in.SimInteractHeld = null; } Debug.Log($"[ASH] WEAPON SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}
