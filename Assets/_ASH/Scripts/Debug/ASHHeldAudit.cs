using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Play Mode held-item audit (exec ASH.DebugTools.ASHHeldAudit.Run): poses every held weapon/item through its states
    /// (bow rest/nocked/full draw/release, guns idle/aim, axe held/mid-chop, torch raised/aim, flashlight idle/aim, lantern on the hip)
    /// and records close frames from 4 cameras (front, outer side, top, body 3/4) into one contact sheet per item
    /// (rows = states) under AshBridge/audit/rN/, plus hand-space numbers in log.txt. Not in git.</summary>
    [DefaultExecutionOrder(32001)]
    public sealed class ASHHeldAudit : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_HeldAudit").AddComponent<ASHHeldAudit>(); }
        /// <summary>Each pre-placed held object alone in the sky with its local axes (X red, Y green, Z blue), seen from +X, +Y, +Z and 3/4.</summary>
        public static void RunAxes() { if (Application.isPlaying) { var g = new GameObject("ASH_HeldAudit"); g.AddComponent<ASHHeldAudit>()._axesOnly = true; } }
        /// <summary>Bow only, through rest / nocked / half / full draw / LMB loose / RMB let go (no shot), plus the game-view frame per state.</summary>
        public static void RunBow() { if (Application.isPlaying) { var g = new GameObject("ASH_HeldAudit"); g.AddComponent<ASHHeldAudit>()._bowOnly = true; } }
        bool _axesOnly, _bowOnly;
        const int Cell = 360;
        static string Base => Path.GetFullPath(Path.Combine(Application.dataPath, "../AshBridge/audit"));

        ASHEquipment _eq; ASHInventory _inv; ASHInput _in; ASHActionLayer _layer; Animator _anim; Transform _root;
        readonly Camera[] _cams = new Camera[4]; readonly RenderTexture[] _rts = new RenderTexture[4]; Light _l1, _l2;
        GameObject _focus; bool _left; string _sheetItem = "item"; string _dir; readonly StringBuilder _log = new StringBuilder();
        readonly List<Texture2D[]> _rows = new List<Texture2D[]>(); readonly List<string> _labels = new List<string>();

        static ASHItemDefinition Item(string f) => Resources.FindObjectsOfTypeAll<ASHItemDefinition>().FirstOrDefault(i => i && i.name == "ASH_Item_" + f);

        /// <summary>Audit pacing: states are held long enough (with their normal blends) that a screen recording of the run looks like real play.</summary>
        public static float Pace = 1.5f, MinStateSeconds = 0.8f;
        static WaitForSeconds Wait(float s) => new WaitForSeconds(s >= 0.3f ? Mathf.Max(s * Pace, MinStateSeconds) : s * Pace);

        IEnumerator Start()
        {
            _eq = FindAnyObjectByType<ASHEquipment>(); _inv = FindAnyObjectByType<ASHInventory>(); _in = FindAnyObjectByType<ASHInput>();
            _anim = _eq ? _eq.Animator : null; _layer = _anim ? _anim.GetComponent<ASHActionLayer>() : null; _root = _eq ? _eq.transform : null;
            if (!_eq || !_inv || !_in || !_anim) { Debug.Log("[ASH] AUDIT failed: no player"); Destroy(gameObject); yield break; }
            Directory.CreateDirectory(Base); int n = 1; while (Directory.Exists(Path.Combine(Base, "r" + n))) n++;
            _dir = Path.Combine(Base, "r" + n); Directory.CreateDirectory(_dir);
            for (int i = 0; i < 4; i++)
            {
                var g = new GameObject("AuditCam" + i); g.transform.SetParent(transform, false);
                var c = g.AddComponent<Camera>(); c.enabled = false; c.fieldOfView = i == 3 ? 38f : 34f; c.nearClipPlane = 0.02f; c.farClipPlane = 60f; c.depth = -50;
                _rts[i] = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32); c.targetTexture = _rts[i]; _cams[i] = c;
            }
            _l1 = new GameObject("AuditLight1").AddComponent<Light>(); _l1.type = LightType.Point; _l1.range = 5f; _l1.intensity = 2.5f; _l1.shadows = LightShadows.None;
            _l2 = new GameObject("AuditLight2").AddComponent<Light>(); _l2.type = LightType.Point; _l2.range = 5f; _l2.intensity = 1.5f; _l2.shadows = LightShadows.None;
            _l1.transform.SetParent(transform); _l2.transform.SetParent(transform);
            _eq.Unequip(ASHEquipSlot.MainHand); _eq.Unequip(ASHEquipSlot.OffHand); _eq.Unequip(ASHEquipSlot.Hip);
            yield return Wait(0.3f);
            if (_axesOnly) { yield return Axes(); File.WriteAllText(Path.Combine(_dir, "log.txt"), _log.ToString()); Debug.Log("[ASH] AUDIT wrote " + _dir); Destroy(gameObject); yield break; }

            // bow + arrow
            var bow = Item("WoodenBow"); var arrow = Item("WoodenArrow");
            if (_bowOnly && bow)
            {
                var wa = _anim.GetComponent<ASHWeaponAim>();
                Give(bow); if (arrow && _inv.Count(arrow) < 5) _inv.AddItem(arrow, 5); _eq.Equip(bow); _left = true; _focus = _eq.MainVisual;
                yield return Wait(0.6f); yield return Shot("rest");
                _in.SimSecondaryHeld = true; yield return Wait(0.15f); yield return Shot("nocked"); BowLog("nocked", wa);
                yield return Wait(0.3f); yield return Shot("halfdraw"); BowLog("halfdraw", wa);
                yield return Wait(1.2f); yield return Shot("fulldraw"); BowLog("fulldraw", wa);
                int a0 = _inv.Count(arrow); _in.SimSecondaryHeld = false; yield return Wait(0.08f); yield return Shot("rmb-up-no-shot");
                yield return Wait(0.5f); _log.Append($"rmb-up: arrows {a0}->{_inv.Count(arrow)} shots {wa.Shots}\n");
                _in.SimSecondaryHeld = true; yield return Wait(1.4f);
                _in.SimPrimary = true; yield return null; yield return null; yield return Shot("lmb-loose"); _log.Append($"loose: spawn {wa.LastArrowSpawn:F2} rest {wa.ArrowRestWorld:F2}\n");
                _in.SimSecondaryHeld = null; yield return Wait(0.4f); Sheet("WoodenBow"); _eq.Unequip(ASHEquipSlot.MainHand);
                File.WriteAllText(Path.Combine(_dir, "log.txt"), _log.ToString()); Debug.Log("[ASH] AUDIT wrote " + _dir);
                foreach (var r in _rts) if (r) r.Release(); Destroy(gameObject); yield break;
            }
            if (bow) { Give(bow); if (arrow && _inv.Count(arrow) < 5) _inv.AddItem(arrow, 5); _eq.Equip(bow); _left = true; _focus = _eq.MainVisual;
                yield return Wait(0.6f); yield return Shot("rest");
                _in.SimSecondaryHeld = true; yield return Wait(0.15f); yield return Shot("nocked");
                yield return Wait(1.2f); yield return Shot("fulldraw");
                _in.SimPrimary = true; yield return null; yield return null; yield return Shot("release");
                _in.SimSecondaryHeld = null; yield return Wait(0.4f); Sheet("WoodenBow"); _eq.Unequip(ASHEquipSlot.MainHand); }
            foreach (var gun in new[] { "HuntingRifle", "Pistol" })
            {
                var it = Item(gun); if (!it) continue;
                Give(it); if (it.ammoItem && _inv.Count(it.ammoItem) < 3) _inv.AddItem(it.ammoItem, 3);
                _eq.Equip(it); _left = false; _focus = _eq.MainVisual;
                yield return Wait(0.6f); yield return Shot("idle");
                _in.SimSecondaryHeld = true; yield return Wait(0.7f); yield return Shot("aim");
                _in.SimSecondaryHeld = null; yield return Wait(0.4f); Sheet(gun); _eq.Unequip(ASHEquipSlot.MainHand);
            }
            var axe = Item("Hatchet");
            if (axe) { Give(axe); _eq.Equip(axe); _left = false; _focus = _eq.MainVisual;
                yield return Wait(0.6f); yield return Shot("held");
                foreach (var (stn, t) in new[] { ("Chop", 0.3f), ("Chop", 0.44f), ("Chop", 0.51f), ("ChopHigh", 0.3f), ("ChopHigh", 0.45f) })
                {
                    if (!_layer.Has(stn)) continue;
                    float until = Time.time + 0.35f; while (Time.time < until) { _layer.PlayAt(stn, t); yield return null; }
                    _layer.PlayAt(stn, t);
                    var rh = _anim.GetBoneTransform(HumanBodyBones.RightHand); var hp = _anim.GetBoneTransform(HumanBodyBones.Hips);
                    Debug.Log($"[ASH] AUDIT chop {stn}@{t:F2} layer={_layer.State} w={_layer.Weight:F2} nt={_layer.NormalizedTime:F2} hand y vs hips {(rh.position.y - hp.position.y):F2}");
                    yield return Shot(stn.ToLowerInvariant() + t.ToString("F2"));
                }
                _layer.Release(); yield return Wait(0.4f); Sheet("Hatchet"); _eq.Unequip(ASHEquipSlot.MainHand); }
            foreach (var lt in new[] { "Torch", "Flashlight" })
            {
                var it = Item(lt); if (!it) continue;
                Give(it); _eq.Equip(it); _left = true; _focus = _eq.OffVisual;
                yield return Wait(0.9f); yield return Shot(lt == "Torch" ? "raised" : "idle");
                _in.SimSecondaryHeld = true; yield return Wait(0.8f); yield return Shot("aim");
                _in.SimSecondaryHeld = null; yield return Wait(0.4f); Sheet(lt); _eq.Unequip(ASHEquipSlot.OffHand);
            }
            var lan = Item("Lantern");
            if (lan) { Give(lan); _eq.Equip(lan); _left = true; _focus = _eq.HipVisual;
                yield return Wait(0.8f); yield return Shot("hip"); yield return Wait(0.5f); yield return Shot("hip2");
                Sheet("Lantern"); _eq.Unequip(ASHEquipSlot.Hip); }

            File.WriteAllText(Path.Combine(_dir, "log.txt"), _log.ToString());
            Debug.Log("[ASH] AUDIT wrote " + _dir);
            foreach (var r in _rts) if (r) r.Release();
            Destroy(gameObject);
        }

        IEnumerator Axes()
        {
            _focus = null;
            var sky = _root.position + Vector3.up * 60f;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            foreach (var src in _eq.AllPlaced.Where(g => g).OrderBy(g => g.name))
            {
                var g = Instantiate(src); g.SetActive(true); g.transform.SetPositionAndRotation(sky, Quaternion.identity); g.transform.localScale = src.transform.lossyScale;
                foreach (var b in g.GetComponentsInChildren<Behaviour>(true)) if (!(b is Light)) b.enabled = false;
                var bb = Bounds(g); float L = Mathf.Max(0.25f, bb.extents.magnitude * 0.9f);
                Line(g.transform, Vector3.right, L, Color.red, mat); Line(g.transform, Vector3.up, L, Color.green, mat); Line(g.transform, Vector3.forward, L, Color.blue, mat);
                var c = bb.center; float d = Mathf.Max(0.8f, bb.size.magnitude * 1.6f);
                Vector3[] dirs = { Vector3.right, Vector3.up, Vector3.forward, new Vector3(1f, 0.8f, 1f).normalized };
                for (int i = 0; i < 4; i++)
                {
                    var cam = _cams[i]; cam.orthographic = i < 3; cam.orthographicSize = d * 0.45f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.35f, 0.35f, 0.38f);
                    var up = i == 1 ? Vector3.forward : Vector3.up;
                    cam.transform.position = c + dirs[i] * d; cam.transform.rotation = Quaternion.LookRotation(-dirs[i], up);
                }
                _l1.transform.position = c + new Vector3(0.6f, 1f, 0.8f); _l2.transform.position = c + new Vector3(-0.8f, 0.3f, -0.6f);
                _l1.intensity = 6f; _l2.intensity = 4f;
                foreach (var cam in _cams) cam.enabled = true;
                yield return new WaitForEndOfFrame();
                var row = new Texture2D[4];
                for (int i = 0; i < 4; i++) { RenderTexture.active = _rts[i]; var tx = new Texture2D(Cell, Cell, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0); tx.Apply(); row[i] = tx; }
                RenderTexture.active = null; foreach (var cam in _cams) cam.enabled = false;
                _rows.Add(row); _labels.Add(src.name);
                _log.Append($"{src.name}: localBounds c{LocalBounds(g).center:F3} s{LocalBounds(g).size:F3} | {Extremes(g)}\n");
                Destroy(g);
                yield return null;
            }
            Sheet("Axes");
            _log.Append("Axes.png cols: from +X (right=-Z... looking down -X, up=Y), from +Y (looking down, up=Z), from +Z (looking down -Z, up=Y), 3/4 from (+X,+Y,+Z)\n");
        }

        static void Line(Transform parent, Vector3 dir, float len, Color col, Material mat)
        {
            var go = new GameObject("axis"); go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>(); lr.useWorldSpace = false; lr.positionCount = 2; lr.SetPosition(0, Vector3.zero); lr.SetPosition(1, dir * len / Mathf.Max(parent.lossyScale.x, 1e-3f));
            lr.widthMultiplier = 0.012f; var m = new Material(mat); m.SetColor("_BaseColor", col); m.color = col; lr.sharedMaterial = m;
        }

        /// <summary>Vertex extremes in object space (metres): per axis the min/max and, for the far 15% along Z at each end, the X/Y spread (finds heads, blades, strings).</summary>
        static string Extremes(GameObject g)
        {
            var pts = new List<Vector3>(); var t = g.transform;
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>()) { if (!mf.sharedMesh || !mf.sharedMesh.isReadable) continue; foreach (var v in mf.sharedMesh.vertices) pts.Add(Vector3.Scale(t.InverseTransformPoint(mf.transform.TransformPoint(v)), t.lossyScale)); }
            if (pts.Count == 0) return "mesh not readable";
            float zmin = pts.Min(p => p.z), zmax = pts.Max(p => p.z), span = zmax - zmin;
            string End(bool top)
            {
                var e = pts.Where(p => top ? p.z > zmax - span * 0.2f : p.z < zmin + span * 0.2f).ToList(); if (e.Count == 0) return "-";
                return $"x[{e.Min(p => p.x):F3},{e.Max(p => p.x):F3}] y[{e.Min(p => p.y):F3},{e.Max(p => p.y):F3}] n{e.Count}";
            }
            var mid = pts.Where(p => Mathf.Abs(p.z - (zmin + zmax) * 0.5f) < span * 0.06f).ToList();
            string Mid() => mid.Count == 0 ? "-" : $"x[{mid.Min(p => p.x):F3},{mid.Max(p => p.x):F3}] y[{mid.Min(p => p.y):F3},{mid.Max(p => p.y):F3}] meanX {mid.Average(p => p.x):F3} n{mid.Count}";
            return $"z[{zmin:F3},{zmax:F3}] topZ: {End(true)} | bottomZ: {End(false)} | midZ: {Mid()}";
        }

        void Give(ASHItemDefinition it) { if (_inv.Count(it) <= 0) _inv.AddItem(it, 1); }

        // cameras follow the focus after all animation / bone deltas (execution order 32001)
        void LateUpdate()
        {
            if (!_focus || !_root) return;
            var b = Bounds(_focus); var c = b.center; float d = Mathf.Clamp(b.size.magnitude * 1.35f, 0.45f, 1.7f);
            Vector3 fwd = Vector3.ProjectOnPlane(_root.forward, Vector3.up).normalized, up = Vector3.up, side = _left ? -_root.right : _root.right;
            Place(_cams[0], c + fwd * d + up * 0.08f * d, c);
            Place(_cams[1], c + side * d + up * 0.05f * d, c);
            Place(_cams[2], c + up * d + fwd * 0.3f * d, c);
            var chest = _anim.GetBoneTransform(HumanBodyBones.Chest); var cc = chest ? Vector3.Lerp(chest.position, c, 0.4f) : c;
            Place(_cams[3], cc + (fwd + side * 0.8f).normalized * 2.0f + up * 0.25f, cc);
            _l1.transform.position = c + (fwd + up) * 0.7f; _l2.transform.position = c + (side - fwd * 0.3f + up * 0.4f) * 0.8f;
        }
        static void Place(Camera cam, Vector3 p, Vector3 look) { cam.transform.position = p; cam.transform.rotation = Quaternion.LookRotation(look - p, Vector3.up); }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.one * 0.3f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        IEnumerator Shot(string label)
        {
            if (!_focus) _focus = _eq.MainVisual ? _eq.MainVisual : _eq.OffVisual ? _eq.OffVisual : _eq.HipVisual;
            foreach (var c in _cams) c.enabled = true;
            yield return new WaitForEndOfFrame();
            var row = new Texture2D[4];
            for (int i = 0; i < 4; i++)
            {
                RenderTexture.active = _rts[i]; var t = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0); t.Apply(); row[i] = t;
            }
            RenderTexture.active = null;
            foreach (var c in _cams) c.enabled = false;
            { var g = ScreenCapture.CaptureScreenshotAsTexture(); if (g) { File.WriteAllBytes(Path.Combine(_dir, $"game_{(_focus ? _focus.name : _sheetItem)}_{_rows.Count}_{label}.png"), g.EncodeToPNG()); Destroy(g); } }
            _rows.Add(row); _labels.Add(label);
            LogPose(label);
        }

        void Sheet(string item)
        {
            if (_rows.Count == 0) return;
            var s = new Texture2D(Cell * 4, Cell * _rows.Count, TextureFormat.RGB24, false);
            for (int r = 0; r < _rows.Count; r++) for (int i = 0; i < 4; i++) s.SetPixels(i * Cell, (_rows.Count - 1 - r) * Cell, Cell, Cell, _rows[r][i].GetPixels());
            s.Apply(); File.WriteAllBytes(Path.Combine(_dir, item + ".png"), s.EncodeToPNG());
            _log.Append($"== {item}.png rows: {string.Join(", ", _labels)}; cols: front, outer side, top, body\n");
            foreach (var row in _rows) foreach (var t in row) Destroy(t);
            _rows.Clear(); _labels.Clear();
        }

        void BowLog(string label, ASHWeaponAim wa)
        {
            if (!wa) return; var ha = wa.HandArrow; var cam = Camera.main;
            _log.Append($"{label}: draw {wa.Draw01:F2} geo {wa.BowGeometryOk} stringSign {wa.BowStringSign} stringVerts {wa.BowStringVerts} pull {wa.StringPullMetres:F2} m | arrow {(ha ? ha.name : "-")} fwd.camFwd {(ha && cam ? Vector3.Dot(ha.transform.forward, cam.transform.forward) : 0f):F2} | string.toCam {(cam ? Vector3.Dot(wa.BowStringDirWorld, (cam.transform.position - wa.ArrowRestWorld).normalized) : 0f):F2}\n");
        }

        void LogPose(string label)
        {
            var v = _focus; if (!v) { _log.Append($"{label}: no visual\n"); return; }
            var hand = _anim.GetBoneTransform(_left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var mid = _anim.GetBoneTransform(_left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            var idx = _anim.GetBoneTransform(_left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            var lit = _anim.GetBoneTransform(_left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            var t = v.transform; Vector3 fwd = _root.forward, up = Vector3.up, right = _root.right;
            string W(Vector3 a) => $"(f{Vector3.Dot(a, fwd):F2} u{Vector3.Dot(a, up):F2} r{Vector3.Dot(a, right):F2})";
            var sb = new StringBuilder($"{label} {v.name}: world X{W(t.right)} Y{W(t.up)} Z{W(t.forward)}");
            if (hand && mid && idx && lit)
            {
                Vector3 along = (mid.position - hand.position).normalized, thumb = (idx.position - lit.position).normalized;
                Vector3 palm = Vector3.Cross(along, thumb).normalized * (_left ? -1f : 1f);
                string H(Vector3 a) => $"(al{Vector3.Dot(a, along):F2} th{Vector3.Dot(a, thumb):F2} pa{Vector3.Dot(a, palm):F2})";
                var pc = Vector3.Lerp(hand.position, mid.position, 0.6f) + palm * 0.03f;
                sb.Append($" | hand X{H(t.right)} Y{H(t.up)} Z{H(t.forward)} origin-palm {H(t.position - pc)} {(t.position - pc).magnitude:F3}m");
                sb.Append($" | handframe world along{W(along)} thumb{W(thumb)} palm{W(palm)}");
            }
            var lb = LocalBounds(v); sb.Append($" | localBounds c{lb.center:F3} s{lb.size:F3}");
            _log.Append(sb).Append('\n');
        }

        static Bounds LocalBounds(GameObject g)
        {
            var t = g.transform; bool any = false; var b = new Bounds();
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = t.InverseTransformPoint(mf.transform.TransformPoint(p)); lp = Vector3.Scale(lp, t.lossyScale);
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; } else b.Encapsulate(lp);
                }
            }
            return b;
        }
    }
}
