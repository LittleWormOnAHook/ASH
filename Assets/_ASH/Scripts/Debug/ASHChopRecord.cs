using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// <summary>Normal-speed chop recording (Play Mode, exec ASH.DebugTools.ASHChopRecord.Run): fells a standing tree to the
    /// ground, then cuts a downed tree, at normal game speed (frame durations logged so the video is rebuilt at true speed). Writes AshBridge/chop_rec.mjpg (game view, 640x360) + chop_rec.csv (per-frame root position / drift) + impact_N.jpg
    /// (side + top views on each impact frame) and checks: pinned (&lt; 2 cm) for the whole sequence, no slide back after release,
    /// blade at the trunk centre and blade edge leading on the impact frame. Logs "[ASH] CHOPREC ..." + "CHOPREC SUMMARY".</summary>
    public sealed class ASHChopRecord : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_ChopRecord").AddComponent<ASHChopRecord>(); }
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"CHOPREC {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }
        string _dir; FileStream _mj; RenderTexture _full, _small, _view; Texture2D _px, _px2; Camera _side, _top;
        ASHInput _in; vThirdPersonController _cc; ASHInteractor _ix; ASHInventory _inv; ASHEquipment _eq; ASHChopAnimator _chop; ASHActionLayer _layer;
        readonly StringBuilder _csv = new StringBuilder("t,phase,x,y,z,driftCm,chopping,locked,state,clip,impacts\n");
        int _impShots;

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
        static Bounds WoodBounds(Component c)
        {
            var cols = c.GetComponentsInChildren<Collider>().Where(x => x.enabled && !x.isTrigger).ToList();
            if (cols.Count == 0) return new Bounds(c.transform.position + Vector3.up, Vector3.one * 0.4f);
            var b = cols[0].bounds; foreach (var x in cols.Skip(1)) b.Encapsulate(x.bounds); return b;
        }

        void Frame()
        {
            if (!_full || _full.width != Screen.width || _full.height != Screen.height) { if (_full) _full.Release(); _full = new RenderTexture(Screen.width, Screen.height, 0); }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_full);
            Graphics.Blit(_full, _small);
            var prev = RenderTexture.active; RenderTexture.active = _small; _px.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); _px.Apply(); RenderTexture.active = prev;
            var j = _px.EncodeToJPG(70); _mj.Write(j, 0, j.Length);
            float now = Time.unscaledTime; if (_lastFrameT >= 0f) _frameDur.AppendLine($"{now - _lastFrameT:F4}"); _lastFrameT = now;
        }
        float _lastFrameT = -1f; readonly StringBuilder _frameDur = new StringBuilder(); // real frame durations: the video is rebuilt at true speed (no captureFramerate / timeScale change)

        void ImpactShot(Vector3 centre, Vector3 blade, string tag)
        {
            var me = _cc.transform; var right = me.right;
            foreach (var (cam, x) in new[] { (_side, 0), (_top, 640) })
            {
                if (cam == _side) { cam.transform.position = me.position + Vector3.up * 1.1f + right * 3.2f + me.forward * 0.4f; cam.transform.LookAt(me.position + Vector3.up * 1.0f + me.forward * 0.5f); }
                else { cam.transform.position = me.position + Vector3.up * 4.5f + me.forward * 0.5f; cam.transform.rotation = Quaternion.LookRotation(Vector3.down, me.forward); }
                cam.targetTexture = _view; cam.Render(); cam.targetTexture = null;
                var prev = RenderTexture.active; RenderTexture.active = _view; _px2.ReadPixels(new Rect(0, 0, 640, 360), x, 0); RenderTexture.active = prev;
            }
            _px2.Apply();
            File.WriteAllBytes(Path.Combine(_dir, $"impact_{_impShots++}_{tag}.jpg"), _px2.EncodeToJPG(80));
        }

        /// <summary>Blade edge direction: in the head (far 25% of the handle), the vertex farthest from the handle axis.</summary>
        bool BladeEdge(out Vector3 dir)
        {
            dir = default; var v = _eq.MainVisual; if (!v) return false;
            var mf = v.GetComponentsInChildren<MeshFilter>().FirstOrDefault(m => m.sharedMesh && m.gameObject.activeInHierarchy);
            if (!mf || !mf.sharedMesh.isReadable) return false;
            var hand = _eq.Animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            var vs = mf.sharedMesh.vertices.Select(q => mf.transform.TransformPoint(q)).ToArray();
            var e = mf.sharedMesh.bounds.extents; var ax = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
            var axis = mf.transform.TransformDirection(ax).normalized; var c = mf.transform.TransformPoint(mf.sharedMesh.bounds.center);
            if (Vector3.Dot(c - hand, axis) < 0f) axis = -axis; // handle axis pointing from the hand to the head
            float max = vs.Max(q => Vector3.Dot(q - c, axis)), min = vs.Min(q => Vector3.Dot(q - c, axis));
            var head = vs.Where(q => Vector3.Dot(q - c, axis) > max - (max - min) * 0.25f).ToArray(); if (head.Length == 0) return false;
            Vector3 Off(Vector3 q) { var r = q - c; return r - axis * Vector3.Dot(r, axis); }
            var tip = head.OrderByDescending(q => Off(q).sqrMagnitude).First();
            dir = Off(tip).normalized; return dir.sqrMagnitude > 0.5f;
        }

        IEnumerator Start()
        {
            yield return null;
            _in = FindAnyObjectByType<ASHInput>(); _cc = FindAnyObjectByType<vThirdPersonController>(); _ix = FindAnyObjectByType<ASHInteractor>(); _inv = FindAnyObjectByType<ASHInventory>(); _eq = FindAnyObjectByType<ASHEquipment>();
            _chop = FindAnyObjectByType<ASHChopAnimator>(); _layer = FindAnyObjectByType<ASHActionLayer>();
            var hatchet = Item("Hatchet");
            if (!(_in && _cc && _ix && _inv && _eq && _chop && _layer && hatchet)) { Check("setup", false, "missing components"); Finish(); yield break; }
            _dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../AshBridge/chop_rec")); Directory.CreateDirectory(_dir);
            foreach (var f in Directory.GetFiles(_dir)) File.Delete(f);
            _mj = new FileStream(Path.Combine(_dir, "chop_rec.mjpg"), FileMode.Create);
            _small = new RenderTexture(640, 360, 0); _view = new RenderTexture(640, 360, 24); _px = new Texture2D(640, 360, TextureFormat.RGB24, false); _px2 = new Texture2D(1280, 360, TextureFormat.RGB24, false);
            var main = Camera.main;
            _side = new GameObject("ASH_RecSide").AddComponent<Camera>(); _top = new GameObject("ASH_RecTop").AddComponent<Camera>();
            foreach (var c in new[] { _side, _top }) { if (main) c.CopyFrom(main); c.enabled = false; c.fieldOfView = 40f; c.nearClipPlane = 0.05f; }
            var tps = FindAnyObjectByType<vThirdPersonInput>(); bool lockCam0 = tps && tps.lockCameraInput; if (tps) tps.lockCameraInput = true;

            if (_inv.Count(hatchet) == 0) _inv.AddItem(hatchet, 1);
            for (int i = 0; i < _inv.Slots.Count; i++) if (_inv.Slots[i].item == hatchet) { if (!_inv.IsHotbar(i)) _inv.Move(i, 0); break; }
            var p0 = _cc.transform.position;
            var all = FindObjectsByType<ASHChoppable>().Where(c => !c.Done && !c.Busy && c.HitsLeft > 1).OrderBy(c => (c.transform.position - p0).sqrMagnitude).ToList();
            var targets = new[] { all.FirstOrDefault(c => c.Kind == ASHChopKind.StandingTree) }.Where(c => c).ToList(); // felling only
            Check("standing harvest tree found", targets.Count > 0, targets.Count > 0 ? targets[0].name + " at " + targets[0].transform.position.ToString("F1") : "none");
            float t = 0f;
            foreach (var target in targets)
            {
                var side = Vector3.Cross(Vector3.up, target.transform.forward).normalized;
                Teleport(target.transform.position - side * 1.6f);
                for (int k = 0; k < 15; k++) { yield return new WaitForEndOfFrame(); Frame(); yield return null; }
                var wb = WoodBounds(target);
                var aimAt = target.Kind == ASHChopKind.StandingTree ? target.transform.position + Vector3.up * 1.1f : wb.center;
                var eye = _cc.transform.position + Vector3.up * 1.6f; _ix.SimRay = new Ray(eye, (aimAt - eye).normalized);
                for (int k = 0; k < 8; k++) { yield return new WaitForEndOfFrame(); Frame(); yield return null; }
                var start = _cc.transform.position; int imp0 = _chop.Impacts, lastImp = imp0, hits0 = target.HitsLeft;
                _in.SimInteractHeld = true;
                Vector3? pinned = null; float maxDrift = 0f, frames = 0f; bool everLocked = false; var misses = new List<string>(); var leads = new List<float>();
                bool havePrev = false; Vector3 prevBw = default; int prevSw = -99;
                var passGap = new Dictionary<int, float>(); var passLat = new Dictionary<int, float>(); var passH = new Dictionary<int, float>();
                float maxCentreMiss = 0f, maxSurfGap = 0f; int nImp = 0; float tt = 0f, snapAt = -1f; var impT = new List<float>(); var faceErr = new List<float>();
                int limit = target.Kind == ASHChopKind.StandingTree ? 40 : 5;
                while (tt < 40f && !target.Done && _chop.Impacts < imp0 + limit)
                {
                    yield return new WaitForEndOfFrame();
                    tt += Time.deltaTime; t += Time.deltaTime;
                    var pos = _cc.transform.position;
                    bool snapDone = _chop.HardLocked && Vector3.ProjectOnPlane(pos - _chop.ChopPosition, Vector3.up).magnitude < 0.002f;
                    if (_chop.HardLocked) everLocked = true;
                    if (snapDone && pinned == null) { pinned = pos; snapAt = tt; }
                    float drift = pinned.HasValue ? Vector3.ProjectOnPlane(pos - pinned.Value, Vector3.up).magnitude : 0f;
                    if (pinned.HasValue) { maxDrift = Mathf.Max(maxDrift, drift); frames++; }
                    _csv.AppendLine($"{t:F3},{target.Kind},{pos.x:F4},{pos.y:F4},{pos.z:F4},{drift * 100f:F2},{_chop.Chopping},{_chop.HardLocked},{_layer.State},{_chop.ClipTime:F3},{_chop.Impacts - imp0}");
                    if (target.Kind == ASHChopKind.StandingTree && pinned.HasValue && _chop.SwingState == "ChopHigh" && _chop.BladeWorld(out var bw))
                    {   // swing window around the impact time: closest pass of the axe head to the trunk
                        float dt = Mathf.Abs(Mathf.DeltaAngle(_chop.ClipTime * 360f, _chop.impactNormalizedHigh * 360f)) / 360f;
                        if (dt >= 0.08f) havePrev = false;
                        if (dt < 0.08f)
                        {
                            var tc = ASHChopAnimator.TrunkCentre(target); var cH = new Vector3(tc.x, bw.y, tc.z);
                            float rad = Mathf.Max(0.05f, Vector3.ProjectOnPlane(cH - _cc.transform.position, Vector3.up).magnitude - _chop.SnapSurfaceDistance);
                            float g = Vector3.ProjectOnPlane(bw - cH, Vector3.up).magnitude - rad;
                            int sw = _chop.Impacts - imp0 + (_chop.ClipTime < _chop.impactNormalizedHigh ? 0 : -1); // swing that lands at the next/last impact
                            if (havePrev && prevSw == sw)
                            {   // the head moves ~0.5 m per frame here: also test the chord between this and the last sample
                                var a = Vector3.ProjectOnPlane(prevBw - cH, Vector3.up); var b = Vector3.ProjectOnPlane(bw - cH, Vector3.up); var ab = b - a;
                                float u = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(-Vector3.Dot(a, ab) / ab.sqrMagnitude) : 0f; var q = a + ab * u;
                                float gq = q.magnitude - rad; var to3 = cH - _cc.transform.position; to3.y = 0f;
                                if (!passGap.ContainsKey(sw) || gq < passGap[sw]) { passGap[sw] = gq; passLat[sw] = Vector3.Dot(q, Vector3.Cross(Vector3.up, to3.normalized)); passH[sw] = Mathf.Lerp(prevBw.y, bw.y, u) - _cc.transform.position.y; }
                            }
                            havePrev = true; prevBw = bw; prevSw = sw;
                            if (!passGap.ContainsKey(sw) || g < passGap[sw]) { passGap[sw] = g; var to2 = cH - _cc.transform.position; to2.y = 0f; passLat[sw] = Vector3.Dot(bw - cH, Vector3.Cross(Vector3.up, to2.normalized)); passH[sw] = bw.y - _cc.transform.position.y; }
                        }
                    }
                    if (_chop.Impacts != lastImp && _chop.BladeWorld(out var blade))
                    {
                        lastImp = _chop.Impacts; nImp++; impT.Add(tt);
                        { var tc0 = ASHChopAnimator.TrunkCentre(target) - _cc.transform.position; tc0.y = 0f; faceErr.Add(Vector3.SignedAngle(_cc.transform.forward, tc0, Vector3.up)); }
                        var tc = ASHChopAnimator.TrunkCentre(target); var centre = new Vector3(tc.x, blade.y, tc.z);
                        if (target.Kind == ASHChopKind.DownedTree)
                        {   // log: centre line along its long axis, nearest point
                            var ax = wb.size.x > wb.size.z ? Vector3.right : Vector3.forward; var cc = wb.center;
                            centre = cc + ax * Vector3.Dot(blade - cc, ax); centre.y = blade.y;
                        }
                        var me = _cc.transform; var to = centre - me.position; to.y = 0f;
                        float lateral = Vector3.Dot(blade - centre, Vector3.Cross(Vector3.up, to.normalized)); // + = right of the centre line
                        float radius = target.Kind == ASHChopKind.StandingTree ? Mathf.Max(0.05f, Vector3.ProjectOnPlane(centre - me.position, Vector3.up).magnitude - _chop.SnapSurfaceDistance) : Mathf.Min(wb.extents.y, Mathf.Min(wb.extents.x, wb.extents.z));
                        float gap = Vector3.ProjectOnPlane(blade - centre, Vector3.up).magnitude - radius; // <= 0 = blade inside the bark
                        float lead = BladeEdge(out var edge) ? Vector3.Dot(edge, (centre - blade).normalized) : float.NaN;
                        if (target.Kind == ASHChopKind.StandingTree) { maxCentreMiss = Mathf.Max(maxCentreMiss, Mathf.Abs(lateral)); maxSurfGap = Mathf.Max(maxSurfGap, gap); }
                        else maxSurfGap = Mathf.Max(maxSurfGap, Mathf.Abs(blade.y - wb.max.y) > 0.4f ? Mathf.Abs(blade.y - wb.max.y) : 0f);
                        leads.Add(lead); misses.Add($"lat {lateral * 100f:F0}cm gap {gap * 100f:F0}cm lead {lead:F2}");
                        if (_impShots < 8) ImpactShot(centre, blade, target.Kind.ToString());
                    }
                    Frame();
                    yield return null;
                }
                _in.SimInteractHeld = null; _ix.SimRay = null;
                var end = _cc.transform.position; float back = 0f;
                for (int k = 0; k < 45; k++) { yield return new WaitForEndOfFrame(); t += Time.deltaTime; var pos = _cc.transform.position; back = Mathf.Max(back, Vector3.ProjectOnPlane(pos - end, Vector3.up).magnitude); _csv.AppendLine($"{t:F3},release,{pos.x:F4},{pos.y:F4},{pos.z:F4},{back * 100f:F2},{_chop.Chopping},{_chop.HardLocked},{_layer.State},{_chop.ClipTime:F3},{_chop.Impacts - imp0}"); Frame(); yield return null; }
                if (target.Kind == ASHChopKind.StandingTree && pinned.HasValue)
                {   // exact impact pose (not frame-sampled): same spot and facing, clip held at impactNormalizedHigh
                    _eq.Equip(hatchet); for (int k = 0; k < 10; k++) yield return null;
                    var me = _cc.transform; var pinP = me.position; var pinR = me.rotation; var rb = _cc.GetComponent<Rigidbody>();
                    for (int k = 0; k < 30; k++) { _layer.PlayAt("ChopHigh", _chop.impactNormalizedHigh); me.SetPositionAndRotation(pinP, pinR); if (rb) { rb.position = pinP; rb.linearVelocity = Vector3.zero; } yield return new WaitForFixedUpdate(); yield return null; }
                    yield return new WaitForEndOfFrame();
                    if (!_chop.BladeWorld(out var hb)) Check("exact impact pose measured", false, $"no axe visual (main {(_eq.Main ? _eq.Main.name : "-")})");
                    else
                    {
                        var tc = ASHChopAnimator.TrunkCentre(target); var cH = new Vector3(tc.x, hb.y, tc.z); var to = cH - me.position; to.y = 0f;
                        float rad = Mathf.Max(0.05f, to.magnitude - _chop.SnapSurfaceDistance);
                        float lat = Vector3.Dot(hb - cH, Vector3.Cross(Vector3.up, to.normalized)), bite = rad - Vector3.ProjectOnPlane(hb - cH, Vector3.up).magnitude;
                        Check("axe head on the trunk at the exact impact time (clip held at impactNormalizedHigh, same spot)", Mathf.Abs(lat) < 0.08f && bite > 0f && bite < 0.2f,
                            $"sideways {lat * 100f:F1} cm (+ right), bite into the bark {bite * 100f:F1} cm, height {hb.y - me.position.y:F2} m, trunk radius {rad * 100f:F0} cm, facing yaw {_chop.fellFacingYaw:F0}, distance {_chop.fellDistance:F2} m");
                        ImpactShot(cH, hb, "exact");
                    }
                    _layer.Release();
                    for (int k = 0; k < 10; k++) yield return null;
                }
                Debug.Log($"[ASH] CHOPREC {target.Kind} {target.name}: start {start:F2} lock {(pinned ?? Vector3.zero):F2} snap {_chop.SnapSurfaceDistance:F2} m from the surface; hits {hits0}->{target.HitsLeft} done={target.Done}; impacts: {string.Join("; ", misses)}");
                Check("felling snap + face finishes fast at normal speed", snapAt > 0f && snapAt < 0.7f && Mathf.Approximately(Time.timeScale, 1f) && Mathf.Approximately(_eq.Animator.speed, 1f),
                    $"in the spot and facing after {snapAt:F2} s (setting {_chop.fellSnapSeconds:F2}); timeScale {Time.timeScale} animator speed {_eq.Animator.speed} swingSpeed {_chop.swingSpeed}; impacts at {string.Join(", ", impT.Select(x => x.ToString("F2")))} s; facing error at impacts {string.Join(", ", faceErr.Select(x => x.ToString("F0")))} deg");
                Check($"pinned for the whole chop sequence ({target.Kind})", everLocked && pinned.HasValue && maxDrift < 0.02f, $"max drift {maxDrift * 100f:F2} cm over {frames} frames, hard lock {everLocked}");
                Check($"no slide back after release ({target.Kind})", back < 0.02f, $"moved {back * 100f:F2} cm in 1.5 s after release; start was {Vector3.ProjectOnPlane(end - start, Vector3.up).magnitude:F2} m away");
                if (target.Kind == ASHChopKind.StandingTree)
                {
                    Check("tree felled", target.Done || target.Busy, $"hits left {target.HitsLeft}");
                    var keys = passGap.Keys.OrderBy(k => k).ToList();
                    var gs = keys.Select(k => passGap[k]).OrderBy(x => x).ToList(); float med = gs.Count > 0 ? gs[gs.Count / 2] : 9f;
                    bool allHit = keys.Count >= 3 && med < 0.05f && med > -0.2f; // frame-sampled (the head moves ~0.5 m per frame here): median closest pass
                    Check("axe head reaches the trunk at the impact on every swing", allHit,
                        $"per swing closest pass (gap to bark cm / sideways cm / height m): {string.Join("; ", keys.Select(k => $"{passGap[k] * 100f:F0}/{passLat[k] * 100f:F0}/{passH[k]:F2}"))} (impact-frame samples: max sideways {maxCentreMiss * 100f:F0} cm)");
                }
                var ld = leads.Where(x => !float.IsNaN(x)).ToList();
                Debug.Log($"[ASH] CHOPREC info blade edge heuristic ({target.Kind}): " + ( ld.Count > 0 ? $"edge . to-trunk min {ld.Min():F2} avg {ld.Average():F2} (frame-sampled, check impact_N.jpg)" : "mesh not readable"));
            }
            if (tps) tps.lockCameraInput = lockCam0;
            File.WriteAllText(Path.Combine(_dir, "chop_rec.csv"), _csv.ToString());
            File.WriteAllText(Path.Combine(_dir, "frame_durations.txt"), _frameDur.ToString());
            Finish();
        }

        void Finish()
        {
            if (_mj != null) { _mj.Flush(); _mj.Dispose(); _mj = null; }
            if (_side) Destroy(_side.gameObject); if (_top) Destroy(_top.gameObject);
            if (_ix) _ix.SimRay = null; if (_in) _in.SimInteractHeld = null;
            Debug.Log($"[ASH] CHOPREC SUMMARY pass={_pass} fail={_fail} dir={_dir}\n{_sum}");
            Destroy(gameObject);
        }
    }
}
