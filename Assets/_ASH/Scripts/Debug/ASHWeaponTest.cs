using System.Collections;
using System.Collections.Generic;
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
        static bool WeaponAimEnds(GameObject g, out Vector3 tip, out Vector3 tail) => ASHWeaponAim.ArrowEndsWorld(g, out tip, out tail);
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
            ASHInput.IgnoreDevices = true; // real mouse clicks must not fire during the test
            _in = FindAnyObjectByType<ASHInput>(); _cc = FindAnyObjectByType<vThirdPersonController>(); _ix = FindAnyObjectByType<ASHInteractor>(); _inv = FindAnyObjectByType<ASHInventory>(); _eq = FindAnyObjectByType<ASHEquipment>();
            _wa = FindAnyObjectByType<ASHWeaponAim>(); _chop = FindAnyObjectByType<ASHChopAnimator>(); _layer = FindAnyObjectByType<ASHActionLayer>();
            var bow = Item("WoodenBow"); var arrow = Item("WoodenArrow"); var rifle = Item("HuntingRifle"); var rounds = Item("RifleRounds"); var pistol = Item("Pistol"); var pr = Item("PistolRounds"); var torch = Item("Torch"); var hatchet = Item("Hatchet");
            bool ok = _in && _cc && _inv && _eq && _wa && _chop && _layer && bow && arrow && rifle && pistol;
            Check("setup", ok && _layer.Valid && _layer.Has("Chop") && _layer.Has("ChopHigh") && _layer.Has("BowDraw") && _layer.Has("BowHold") && _layer.Has("BowShot") && _layer.Has("RifleShot"), $"wa={(bool)_wa} chop={(bool)_chop} layer valid={(_layer && _layer.Valid)} states={(_layer && _layer.Has("Chop"))}/{(_layer && _layer.Has("BowDraw"))}/{(_layer && _layer.Has("BowHold"))} arrowPrefab={(_wa && _wa.arrowPrefab)}");
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
            Check("bow left hand, right-hand arrow placed but hidden at rest (drawn from the quiver)", _eq.Main == bow && _eq.Off == null && _eq.MainVisual && _eq.MainVisual == _eq.Placed(bow) && _eq.MainVisual.transform.IsChildOf(bowHand) && ha && ha == _eq.Placed(ASHEquipment.HandArrowName) && !ha.activeSelf && ha.transform.IsChildOf(lh),
                $"main={(_eq.Main ? _eq.Main.displayName : "-")} off={(_eq.Off ? _eq.Off.displayName : "-")} handArrow={(ha ? ha.transform.parent.name : "none")}");

            // RMB: nock + draw (strafe aim), hands together
            bool strafe0 = _cc.isStrafing;
            var chest = _eq.Animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? _eq.Animator.GetBoneTransform(HumanBodyBones.Chest);
            _in.SimSecondaryHeld = true; { float tw0 = 0f; while (_wa.Draw01 < 0.99f && tw0 < 3f) { tw0 += Time.deltaTime; yield return null; } } yield return new WaitForSeconds(0.5f); yield return new WaitForEndOfFrame();
            var bowPos = _eq.MainVisual.transform.position; float handsGap = Vector3.Distance(lh.position, bowPos);
            var aimFwd = Camera.main ? Camera.main.transform.forward : _cc.transform.forward; var aimFlat = Vector3.ProjectOnPlane(aimFwd, Vector3.up).normalized;
            var restW = _wa.ArrowRestWorld; var nockW = _wa.NockWorld;
            float drawBack = Vector3.Dot(restW - nockW, aimFlat); // string fingers sit behind the arrow rest along the aim
            Check("RMB draw animation reaches the looping full-draw hold", _wa.Aiming && _cc.isStrafing && _wa.Draw01 >= 0.99f && _layer.State == "BowHold" && _layer.Weight > 0.5f && drawBack > 0.3f && handsGap < 1.1f,
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
            {
                bool okEnds = WeaponAimEnds(ha, out var nTip, out var nTail);
                float nDot = okEnds ? Vector3.Dot((nTip - nTail).normalized, aimLine) : -9f;
                Check("nocked arrow tip-forward (pointed end toward the target)", okEnds && nDot > 0.9f, $"tip-tail . aim {nDot:F3} tip-nock-fingers {(okEnds ? Vector3.Distance(nTail, _wa.FingerWorld) * 100f : -1f):F1} cm");
            }
            Check("arrow nocked at the drawing fingers, resting on the bow, pointing forward", ha && ha.name == "ASH_NockArrow" && ha.activeInHierarchy && fwdDot > 0.85f && restOff < 0.04f && nockOff < 0.03f && fingerOff < 0.03f && Vector3.Dot(restW - aP, aF) > 0.3f,
                $"arrow={(ha ? ha.name : "none")} fwd.aimLine {fwdDot:F3} rest off line {restOff * 100f:F1} cm nock off {nockOff * 100f:F1} cm nock-fingers {fingerOff * 100f:F1} cm");
            Check("string pulled to the drawing hand", _wa.BowStringVerts > 0 && _wa.StringPullMetres > 0.2f,
                $"string verts {_wa.BowStringVerts} pull {_wa.StringPullMetres:F2} m");
            // archery pose comes from the animation (no arm IK): bow arm out toward the target at shoulder height, drawing hand at the face
            {
                var an = _eq.Animator; var lua = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); var rua = an.GetBoneTransform(HumanBodyBones.RightUpperArm); var lla = an.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                var head = an.GetBoneTransform(HumanBodyBones.Head);
                var midS = (lua.position + rua.position) * 0.5f;
                float fwd = Vector3.Dot(bowHand.position - midS, aimFlat), up = bowHand.position.y - lua.position.y;
                float bend = Vector3.Angle(lla.position - lua.position, bowHand.position - lla.position);
                float face = head ? Vector3.Distance(lh.position, head.position) : 9f;
                float behind = Vector3.Dot(lh.position - midS, aimFlat);
                Check("archery pose from the animation: bow arm out, drawing hand at the face", fwd > 0.35f && Mathf.Abs(up) < 0.25f && bend < 40f && face < 0.3f && behind > -0.25f && _wa.BowArrowLineError >= 0f && _wa.BowArrowLineError < 10f,
                    $"body yaw {_wa.BowBodyYaw:F0} deg arrow line vs aim {_wa.BowArrowLineError:F1} deg; bow hand forward {fwd:F2} m height vs shoulder {up:F2} m elbow bend {bend:F0} deg; draw hand to head {face:F2} m, along aim from shoulders {behind:F2} m");
            }
            Check("head turns toward the crosshair while aiming the bow (partial, spread over spine/chest/neck/head)", _wa.HeadLookBlend > _wa.headLookWeight * 0.9f && _wa.HeadLookError >= 0f && _wa.HeadLookError < _wa.HeadLookRaw - 0.5f * _wa.HeadLookTurn && _wa.HeadLookTurn <= _wa.headLookClamp + 0.5f,
                $"look blend {_wa.HeadLookBlend:F2} face vs target {_wa.HeadLookRaw:F1} -> {_wa.HeadLookError:F1} deg, turn {_wa.HeadLookTurn:F1} (clamp {_wa.headLookClamp:F0}, yaw offset {_wa.headLookYawOffset:F0}, face from {_wa.FaceSource})");
            Check("aim camera over the right shoulder", _wa.CameraAiming && _wa.CameraTargetState == ASHWeaponAim.AimCameraState && _wa.CameraRightNow > _wa.aimCameraRight * 0.7f,
                $"aiming={_wa.CameraAiming} state={_wa.CameraTargetState} right now {_wa.CameraRightNow:F2} (target {_wa.aimCameraRight:F2})");

            // holds full draw while RMB stays down
            yield return new WaitForSeconds(2f);
            float nt = _layer.NormalizedTime;
            Check("bow holds full draw (looping hold) until RMB release", _wa.Aiming && _wa.Draw01 >= 0.99f && _layer.State == "BowHold" && _layer.IsIn("BowHold") && _layer.Weight > 0.9f,
                $"aim={_wa.Aiming} draw={_wa.Draw01:F2} state={_layer.State} w={_layer.Weight:F2} clip t {nt:F2}");
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
            {
                Vector3 fTip = default, fTail = default; bool okEnds = full && WeaponAimEnds(full.gameObject, out fTip, out fTail);
                float fDot = okEnds ? Vector3.Dot((fTip - fTail).normalized, full.LaunchVelocity.normalized) : -9f;
                float tw2 = 0f; while (full && !full.Stuck && tw2 < 3f) { tw2 += Time.deltaTime; yield return null; }
                bool tipIn = false; string stickInfo = "not stuck";
                Vector3 sTip = default, sTail = default;
                if (full && full.Stuck && WeaponAimEnds(full.gameObject, out sTip, out sTail))
                {
                    var d = sTip - sTail; tipIn = Physics.Raycast(sTail, d.normalized, out var sh, d.magnitude + 0.02f, ~0, QueryTriggerInteraction.Ignore);
                    stickInfo = $"stuck in {(full.StuckIn ? full.StuckIn.name : "-")} tip end {(tipIn ? "in the surface" : "OUT")} tail {(Physics.CheckSphere(sTail, 0.01f, ~0, QueryTriggerInteraction.Ignore) ? "buried" : "out")}";
                }
                Check("fired arrow flies and sticks tip-first", okEnds && fDot > 0.95f && full.Stuck && tipIn, $"tip-tail . velocity {fDot:F3}; {stickInfo}");
            }
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
            Check("head turns toward the crosshair while aiming the rifle", _wa.HeadLookError >= 0f && (_wa.HeadLookRaw < 3f || _wa.HeadLookError < _wa.HeadLookRaw) && _wa.HeadLookTurn <= _wa.headLookClamp + 0.5f, $"face vs target {_wa.HeadLookRaw:F1} -> {_wa.HeadLookError:F1} deg, turn {_wa.HeadLookTurn:F1}, face from {_wa.FaceSource}");
            Check("rifle butt seated in the shoulder, support hand on LeftHandIK", rIK && _wa.GunLeftHandError >= 0f && _wa.GunLeftHandError < 0.03f && _wa.GunLeftHandWeight > 0.95f && _wa.RifleButtToPocket >= 0f && _wa.RifleButtToPocket < 0.07f,
                $"butt to shoulder pocket {_wa.RifleButtToPocket * 100f:F1} cm grip slid back {_wa.GunGripSlide * 100f:F0} cm target={(bool)rIK} left hand error {_wa.GunLeftHandError * 100f:F1} cm weight {_wa.GunLeftHandWeight:F2} reach {_wa.GunLeftReach:F2}/{_wa.GunLeftArmLength:F2} m pulled back {_wa.GunPull * 100f:F0} cm target(root) {(_eq.MainVisual && _eq.MainVisual.transform.Find(ASHWeaponAim.LeftHandIKName) ? _wa.transform.InverseTransformPoint(_eq.MainVisual.transform.Find(ASHWeaponAim.LeftHandIKName).position).ToString("F2") : "-")}");
            string rState = _layer.State; int r0 = _inv.Count(rounds); int s0 = _wa.Shots;
            _in.SimPrimary = true; yield return null; yield return null;
            var kick = Camera.main ? Camera.main.GetComponent<ASHCameraKick>() : null;
            Check("rifle aim + fire + recoil", rState == "RifleAim" && _wa.Aiming && _inv.Count(rounds) == r0 - 1 && _wa.Shots == s0 + 1 && kick && kick.Pitch > 0.5f && _wa.KickAmount > 0.2f,
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

            // lights: flashlight aim keeps the wrist in line with the forearm (beam unchanged), torch raised lower
            {
                var la = FindAnyObjectByType<ASHLightAim>(); var fl = Item("Flashlight");
                if (la && fl && torch)
                {
                    _eq.Unequip(ASHEquipSlot.MainHand);
                    if (_inv.Count(fl) == 0) _inv.AddItem(fl, 1);
                    _eq.Equip(fl); yield return new WaitForSeconds(0.3f);
                    _in.SimSecondaryHeld = true; yield return new WaitForSeconds(1.2f);
                    float bend = la.WristBendDeg, beam = la.BeamErrorDeg;
                    _in.SimSecondaryHeld = null; yield return new WaitForSeconds(0.5f);
                    Check("flashlight aim: wrist straight, beam on the aim line", la.Aiming == false && bend >= 0f && bend < 25f && beam >= 0f && beam < 4f, $"wrist bend {bend:F0} deg (straighten {la.flashWristStraighten:F2}, angle {la.flashWristAngle:F0}) beam vs aim {beam:F1} deg");
                    _eq.Unequip(ASHEquipSlot.OffHand);
                    if (_inv.Count(torch) == 0) _inv.AddItem(torch, 1);
                    _eq.Equip(torch); yield return new WaitForSeconds(1.2f);
                    var an = _eq.Animator; var lhb = an.GetBoneTransform(HumanBodyBones.LeftHand); var lsh = an.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    float rise = lhb.position.y - lsh.position.y, drop = la.torchHeightDrop;
                    la.torchHeightDrop = 0f; yield return new WaitForSeconds(0.6f);
                    float rise0 = lhb.position.y - lsh.position.y; la.torchHeightDrop = drop; yield return new WaitForSeconds(0.4f);
                    Check("torch raised, lowered straight down by torchHeightDrop", la.TorchRaised && drop > 0.1f && Mathf.Abs((rise0 - rise) - drop) < 0.07f, $"hand above shoulder {rise0:F2} m without drop -> {rise:F2} m with drop {drop:F2} (lowered {rise0 - rise:F2} m)");
                    _eq.Unequip(ASHEquipSlot.OffHand); yield return new WaitForSeconds(0.3f);
                }
                else Check("lights", false, $"lightAim={(bool)la} flashlight={(bool)fl} torch={(bool)torch}");
            }

            // chop animation on a log/stump/tree
            if (_inv.Count(hatchet) == 0) _inv.AddItem(hatchet, 1);
            for (int i = 0; i < _inv.Slots.Count; i++) if (_inv.Slots[i].item == hatchet) { if (!_inv.IsHotbar(i)) _inv.Move(i, 0); break; }
            var p = _cc.transform.position;
            var all = FindObjectsByType<ASHChoppable>().Where(c => !c.Done && !c.Busy && c.HitsLeft > 1).OrderBy(c => c.HitsLeft >= 5 ? 0 : 1).ThenBy(c => (c.transform.position - p).sqrMagnitude).ToList();
            var targets = new[] { all.FirstOrDefault(c => c.Kind != ASHChopKind.StandingTree), all.FirstOrDefault(c => c.Kind == ASHChopKind.StandingTree) }.Where(c => c).ToList();
            foreach (var target in targets)
            {
                var side = Vector3.Cross(Vector3.up, target.transform.forward).normalized;
                Teleport(target.transform.position - side * 1.6f); yield return new WaitForSeconds(0.6f);
                var aimAt = target.Kind == ASHChopKind.StandingTree ? target.transform.position + Vector3.up * 1.1f : Centre(target);
                var eye = _cc.transform.position + Vector3.up * 1.6f; _ix.SimRay = new Ray(eye, (aimAt - eye).normalized); yield return new WaitForSeconds(0.25f);
                int h0 = target.HitsLeft; int imp0 = _chop.Impacts; var mainBefore = _eq.Main; var pos0 = _cc.transform.position;
                _in.SimInteractHeld = true; float t = 0f; bool sawChop = false, locked = false, axeHeld = false, fullBody = false; float minClip = 1f, maxClip = 0f;
                var feetL = new List<Vector3>(); var feetR = new List<Vector3>(); var misses = new List<float>(); int maxHeld = 0; string heldNames = "";
                var swingPos = new List<Vector3>(); int lastImp = _chop.Impacts; Vector3 snapPos = Vector3.zero; bool snapped = false; float surf = -1f;
                while (t < 14f && !target.Done && _chop.Impacts < imp0 + 5)
                {
                    t += Time.deltaTime;
                    if (_chop.Chopping) { sawChop |= _layer.State == _chop.SwingState && _layer.State.StartsWith("Chop") && _layer.Weight > 0.3f; fullBody |= _layer.OnFullBody; locked |= _chop.MovementLocked; axeHeld |= _eq.Main && _eq.Main.toolType == ASHToolType.Axe; minClip = Mathf.Min(minClip, _chop.ClipTime); maxClip = Mathf.Max(maxClip, _chop.ClipTime); }
                    if (_chop.Snapped && !snapped) { snapped = true; snapPos = _chop.ChopPosition; surf = _chop.SnapSurfaceDistance; }
                    if (_chop.Impacts != lastImp)
                    {
                        lastImp = _chop.Impacts; swingPos.Add(_cc.transform.position);
                        var an = _eq.Animator; if (an && an.isHuman) { feetL.Add(an.GetBoneTransform(HumanBodyBones.LeftFoot).position); feetR.Add(an.GetBoneTransform(HumanBodyBones.RightFoot).position); }
                        misses.Add(_chop.BladeMissDeg);
                    }
                    if (_chop.Chopping && _eq.Animator)
                    {
                        var hs = _eq.Animator.GetComponentsInChildren<Transform>(false).Where(t => t.name.StartsWith("Held_") && t.GetComponentsInChildren<Renderer>(false).Any(r => r.enabled)).Select(t => t.name).ToList();
                        if (_eq.MainVisual && !_eq.MainVisual.name.StartsWith("Held_") && _eq.MainVisual.activeInHierarchy) hs.Add(_eq.MainVisual.name);
                        if (_eq.OffVisual && _eq.OffVisual.activeInHierarchy && !hs.Contains(_eq.OffVisual.name)) hs.Add(_eq.OffVisual.name);
                        if (hs.Count > maxHeld) { maxHeld = hs.Count; heldNames = string.Join(",", hs); }
                    }
                    yield return null;
                }
                float swingDrift = 0f; for (int i = 1; i < swingPos.Count; i++) swingDrift = Mathf.Max(swingDrift, Vector3.ProjectOnPlane(swingPos[i] - swingPos[0], Vector3.up).magnitude);
                Debug.Log($"[ASH] WEAPON chop positions ({target.Kind}): start {pos0:F2} snap {snapPos:F2} surface {surf:F2} m; swings: {string.Join(" ", swingPos.Select(q => q.ToString("F3")))}");
                Check($"chop snaps once and holds still ({target.Kind})", snapped && swingPos.Count >= 2 && swingDrift < 0.02f && Mathf.Abs(surf - _chop.chopDistance) < 0.15f && Vector3.ProjectOnPlane(swingPos[0] - snapPos, Vector3.up).magnitude < 0.02f,
                    $"snapped={snapped} moved {Vector3.ProjectOnPlane(snapPos - pos0, Vector3.up).magnitude:F2} m to {surf:F2} m from the surface (target {_chop.chopDistance:F2}); {swingPos.Count} swings, max drift {swingDrift * 100f:F1} cm");
                float footDrift = 0f; for (int i = 1; i < feetL.Count; i++) footDrift = Mathf.Max(footDrift, Vector3.ProjectOnPlane(feetL[i] - feetL[0], Vector3.up).magnitude, Vector3.ProjectOnPlane(feetR[i] - feetR[0], Vector3.up).magnitude);
                if (target.Kind == ASHChopKind.StandingTree) Debug.Log($"[ASH] WEAPON info felling foot travel {footDrift * 100f:F1} cm (base pin checked by ASHChopRecord)"); else
                Check($"chop feet planted across swings ({target.Kind})", feetL.Count >= 2 && footDrift < 0.04f, $"{feetL.Count} impacts, max foot drift {footDrift * 100f:F1} cm");
                float lastMiss = misses.Count > 0 ? misses[misses.Count - 1] : float.NaN;
                Debug.Log($"[ASH] WEAPON info chop blade estimate ({target.Kind}): " + ( $"blade miss per impact (deg, + right): {string.Join(" ", misses.Select(m => m.ToString("F1")))}; last {_chop.BladeMiss * 100f:F0} cm; facing yaw {_chop.chopFacingYaw:F0} + learned {_chop.LearnedYaw:F1}"));
                Check($"only the axe is in hand while chopping ({target.Kind})", maxHeld <= 1, $"max held visuals {maxHeld} {heldNames}");
                var endPos = _cc.transform.position;
                _in.SimInteractHeld = null; _ix.SimRay = null;
                float te = 0f, tEnd = -1f, tOut = -1f, wAtEnd = 0f;
                while (te < 1.2f) { te += Time.deltaTime; if (tEnd < 0f && !_chop.Chopping) { tEnd = te; wAtEnd = _layer.Weight; } if (tEnd >= 0f && tOut < 0f && _layer.Weight < 0.05f) tOut = te; yield return null; }
                float exitT = tOut >= 0f && tEnd >= 0f ? tOut - tEnd : -1f;
                Check($"chop eases out to idle ({target.Kind})", wAtEnd > 0.5f && exitT >= 0.15f && exitT <= 0.6f, $"layer weight {wAtEnd:F2} at release, blended out in {exitT:F2} s (setting {_chop.chopExitSeconds:F2} s)");
                float back = Vector3.ProjectOnPlane(_cc.transform.position - endPos, Vector3.up).magnitude;
                Check($"chop swing synced to hit ({target.Kind})", sawChop && fullBody && back < 0.05f && locked && axeHeld && target.HitsLeft < h0 && _chop.Impacts > imp0 && maxClip - minClip > 0.5f,
                    $"{target.name} ({target.Kind}) hits {h0}->{target.HitsLeft} impacts +{_chop.Impacts - imp0} anim={sawChop} ({_chop.SwingState}, full body {fullBody}) no slide back after {back * 100f:F1} cm locked={locked} axeInHand={axeHeld} clip {minClip:F2}..{maxClip:F2}");
                Check($"chop releases ({target.Kind})", !_chop.Chopping && !_chop.MovementLocked && _eq.Main == mainBefore, $"chopping={_chop.Chopping} locked={_chop.MovementLocked} main={(_eq.Main ? _eq.Main.displayName : "-")} (before {(mainBefore ? mainBefore.displayName : "-")})");
            }
            if (targets.Count == 0) Check("chop target", false, "no choppable found");
            _eq.Unequip(ASHEquipSlot.MainHand); _eq.Unequip(ASHEquipSlot.OffHand); yield return null; yield return null;
            int moved = authored.Count(kv => kv.Key && (kv.Key.transform.parent != kv.Value.Item1 || (kv.Key.transform.localPosition - kv.Value.Item2).sqrMagnitude > 1e-10f
                || Quaternion.Angle(kv.Key.transform.localRotation, kv.Value.Item3) > 0.01f || (kv.Key.transform.localScale - kv.Value.Item4).sqrMagnitude > 1e-10f));
            int leftOn = placedAll.Count(g => g && g.activeSelf && g != _eq.HipVisual);
            Check("hand placements untouched after aim/fire/chop", moved == 0 && leftOn == 0, $"moved {moved}/{authored.Count} still visible {leftOn}");
            Finish();
        }

        void Finish() { ASHInput.IgnoreDevices = false; if (_ix) _ix.SimRay = null; if (_in) { _in.SimSecondaryHeld = null; _in.SimInteractHeld = null; } Debug.Log($"[ASH] WEAPON SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}
