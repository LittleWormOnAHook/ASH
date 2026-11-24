using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.World.Roads;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ASH.EditorTools.Roads.ASHRoadBuilder;

namespace ASH.EditorTools.Roads
{
    /// <summary>pt630u: EasyRoads keeps a road level from edge to edge, so on side slopes one edge floats and the other sinks.
    /// Snap Roads To Terrain Angles tilts each marker of the ASH roads (or the selected EasyRoads roads) to the ground's cross-slope
    /// (sign auto-detected per road), optionally turns on EasyRoads snap-to-terrain, and records what it changed so Revert can undo it.
    /// Idempotent; never moves markers, never touches props or terrain (unless recarveAfterTilt), never saves the scene.</summary>
    public static class ASHRoadTerrainAngles
    {
        const string BackupPath = "Assets/_ASH/Backups/Roads/ASH_RoadTilts.json";
        static ASHRoadSettings S => ASHRoadSettings.I;

        [Serializable] class RoadRec { public string road; public float[] original; public float[] applied; public bool snapWas; public float snapOffsetWas; public bool snapSet; }
        [Serializable] class Store { public List<RoadRec> roads = new List<RoadRec>(); }

        [MenuItem("ASH/Systems/Roads/Snap Roads To Terrain Angles (all ASH roads or selected)", priority = 20)]
        public static void SnapMenu() => Run(false);
        [MenuItem("ASH/Systems/Roads/Revert Road Terrain Angles (all ASH roads or selected)", priority = 21)]
        public static void RevertMenu() => Run(true);
        [MenuItem("ASH/Systems/Roads/Report Road Terrain Angles (log only)", priority = 22)]
        public static void ReportMenu() { var r = Report(); Debug.Log(r); Write(r); }

        static void Write(string r) { try { Directory.CreateDirectory("AshBridge"); File.WriteAllText("AshBridge/roads_angles.txt", r); } catch { } }

        static Terrain _t;
        static float H(Vector3 p) => _t ? _t.SampleHeight(p) + _t.transform.position.y : p.y;

        static (object net, List<object> roads, string err) Gather()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return (null, null, "Stop Play first.");
            if (ErT("ERRoadNetwork") == null) return (null, null, "EasyRoads3D not found.");
            _t = FindTerrain(); if (!_t) return (null, null, "No terrain.");
            var net = NewEr(ErT("ERRoadNetwork"));
            var sel = new List<object>();
            foreach (var go in Selection.gameObjects)
                for (var tr = go.transform; tr; tr = tr.parent)
                {
                    object r = null; try { r = Call(net, "GetRoadByGameObject", tr.gameObject); } catch { }
                    if (r != null) { if (!sel.Any(x => Name(x) == Name(r))) sel.Add(r); break; }
                }
            if (sel.Count > 0) return (net, sel, null);
            var all = ((Array)Call(net, "GetRoads")).Cast<object>().Where(r => (Name(r) ?? "").StartsWith(Prefix)).ToList();
            return (net, all, all.Count == 0 ? "No ASH_Road_* roads in the open scene." : null);
        }
        static string Name(object r) => Call(r, "GetName") as string;
        static bool BuildMode(object net) => InBuildMode(net);
        static Vector3[] Pts(object r, string m) { try { return (Vector3[])Call(r, m); } catch { return Array.Empty<Vector3>(); } }

        /// <summary>Mean cross-slope mismatch (m): road edge height difference vs the ground's, over spline points near the ground.</summary>
        static float Mismatch(object r, out int n)
        {
            var c = Pts(r, "GetSplinePointsCenter"); var L = Pts(r, "GetSplinePointsLeftSide"); var R = Pts(r, "GetSplinePointsRightSide");
            int cnt = Mathf.Min(c.Length, Mathf.Min(L.Length, R.Length)); double sum = 0; n = 0;
            for (int i = 0; i < cnt; i++)
            {
                if (Mathf.Abs(c[i].y - H(c[i])) > S.tiltMaxGroundGap) continue;
                sum += Mathf.Abs((R[i].y - L[i].y) - (H(R[i]) - H(L[i]))); n++;
            }
            return n > 0 ? (float)(sum / n) : 0f;
        }

        /// <summary>Ground side angle (degrees, right edge up = positive) at each marker, smoothed, clamped, dead-banded.</summary>
        static float[] Targets(object r, int markers)
        {
            var m = (Vector3[])Call(r, "GetMarkerPositions");
            var c = Pts(r, "GetSplinePointsCenter"); var L = Pts(r, "GetSplinePointsLeftSide"); var R = Pts(r, "GetSplinePointsRightSide");
            int cnt = Mathf.Min(c.Length, Mathf.Min(L.Length, R.Length));
            var raw = new float[markers]; var ok = new bool[markers];
            for (int i = 0; i < markers && cnt > 0; i++)
            {
                int j = 0; float bd = float.MaxValue;
                for (int k = 0; k < cnt; k++) { float d = (c[k] - m[i]).sqrMagnitude; if (d < bd) { bd = d; j = k; } }
                if (Mathf.Abs(c[j].y - H(c[j])) > S.tiltMaxGroundGap) continue;
                float w = Vector2.Distance(new Vector2(L[j].x, L[j].z), new Vector2(R[j].x, R[j].z)); if (w < 0.5f) continue;
                raw[i] = Mathf.Atan2(H(R[j]) - H(L[j]), w) * Mathf.Rad2Deg; ok[i] = true;
            }
            var res = new float[markers];
            for (int i = 0; i < markers; i++)
            {
                if (!ok[i]) { res[i] = 0f; continue; }
                float s = 0; int n = 0;
                for (int k = Mathf.Max(0, i - S.tiltSmoothingMarkers); k <= Mathf.Min(markers - 1, i + S.tiltSmoothingMarkers); k++) if (ok[k]) { s += raw[k]; n++; }
                float a = (n > 0 ? s / n : raw[i]) * S.crossSlopeStrength;
                if (Mathf.Abs(a) < S.tiltDeadband) a = 0f;
                res[i] = Mathf.Clamp(a, -S.maxTiltDegrees, S.maxTiltDegrees);
            }
            return res;
        }

        static float GetTilt(object r, int i) { try { return (float)Call(r, "GetMarkerTilting", i); } catch { return 0f; } }
        static void SetTilts(object r, float[] v, bool[] mask) { for (int i = 0; i < v.Length; i++) if (mask[i]) Call(r, "SetMarkerTilting", v[i], i, false); Call(r, "Refresh"); }

        static Store Load() { try { return File.Exists(BackupPath) ? JsonUtility.FromJson<Store>(File.ReadAllText(BackupPath)) ?? new Store() : new Store(); } catch { return new Store(); } }
        static void Save(Store s) { Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)); File.WriteAllText(BackupPath, JsonUtility.ToJson(s, true)); }

        static void Run(bool revert)
        {
            var sb = new StringBuilder($"[ASH Roads] {(revert ? "Revert" : "Snap")} Road Terrain Angles\n");
            var (net, roads, err) = Gather();
            if (err != null) { sb.AppendLine("  " + err); Done(sb); return; }
            bool bm = BuildMode(net);
            sb.AppendLine($"  Network build mode: {bm}. Roads: {string.Join(", ", roads.Select(Name))}");
            var store = Load(); int touched = 0, markersSet = 0, handKept = 0;
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            foreach (var r in roads)
            {
                string nm = Name(r);
                var go = Fld(r, "gameObject") as GameObject; if (go) Undo.RegisterFullObjectHierarchyUndo(go, "ASH Road terrain angles");
                int mc = ((Vector3[])Call(r, "GetMarkerPositions")).Length;
                var cur = Enumerable.Range(0, mc).Select(i => GetTilt(r, i)).ToArray();
                var rec = store.roads.FirstOrDefault(x => x.road == nm);
                float before = Mismatch(r, out _);
                if (revert)
                {
                    if (rec == null) { sb.AppendLine($"  {nm}: nothing recorded, left as is."); continue; }
                    var mask = new bool[mc]; var vals = new float[mc]; int n = 0;
                    for (int i = 0; i < mc; i++)
                    {
                        bool ours = rec.applied != null && i < rec.applied.Length && Mathf.Abs(cur[i] - rec.applied[i]) < 0.01f;
                        if (ours) { mask[i] = true; vals[i] = rec.original != null && i < rec.original.Length ? rec.original[i] : 0f; n++; } else if (Mathf.Abs(cur[i] - (rec.applied != null && i < rec.applied.Length ? rec.applied[i] : 0f)) >= 0.01f) handKept++;
                    }
                    SetTilts(r, vals, mask); markersSet += n;
                    if (rec.snapSet) { try { Call(r, "SnapToTerrain", rec.snapWas, rec.snapOffsetWas, true); } catch { } }
                    store.roads.Remove(rec); touched++;
                    sb.AppendLine($"  {nm}: {n} marker tilts restored; cross-slope mismatch {before:F2} -> {Mismatch(r, out _):F2} m");
                    continue;
                }
                if (rec == null || rec.original == null || rec.original.Length != mc)
                {
                    rec ??= new RoadRec { road = nm }; if (!store.roads.Contains(rec)) store.roads.Add(rec);
                    rec.original = cur.ToArray(); rec.applied = cur.ToArray();
                }
                // markers we may change: ours (still at the value we set) or untouched (still original); anything else is a hand edit
                var can = new bool[mc];
                for (int i = 0; i < mc; i++)
                {
                    bool atApplied = Mathf.Abs(cur[i] - rec.applied[i]) < 0.01f, atOrig = Mathf.Abs(cur[i] - rec.original[i]) < 0.01f;
                    bool handTiltedBefore = Mathf.Abs(rec.original[i]) > 0.01f && atOrig && Mathf.Abs(rec.applied[i] - rec.original[i]) < 0.01f;
                    can[i] = (atApplied || atOrig) && !(S.keepHandTilts && handTiltedBefore);
                    if (!can[i]) handKept++;
                }
                if (S.snapToTerrain)
                {
                    if (!rec.snapSet) { rec.snapWas = Fld(Fld(r, "road") ?? r, "snapToTerrain") is bool sw && sw; rec.snapOffsetWas = Fld(Fld(r, "road") ?? r, "snapOffset") is float so ? so : 0f; rec.snapSet = true; }
                    try { Call(r, "SnapToTerrain", true, S.snapOffset, false); } catch (Exception e) { sb.AppendLine($"  {nm}: SnapToTerrain failed ({(e.InnerException ?? e).Message})"); }
                }
                if (S.followCrossSlope)
                {
                    var tgt = Targets(r, mc);
                    // EasyRoads' tilt sign isn't documented: try both, keep whichever fits the ground better
                    var plus = new float[mc]; var minus = new float[mc];
                    for (int i = 0; i < mc; i++) { plus[i] = can[i] ? tgt[i] : cur[i]; minus[i] = can[i] ? -tgt[i] : cur[i]; }
                    SetTilts(r, plus, can); float ep = Mismatch(r, out _);
                    SetTilts(r, minus, can); float em = Mismatch(r, out _);
                    float[] best = em <= ep ? minus : plus; float eb = Mathf.Min(ep, em);
                    if (eb > before - 0.005f) { best = cur; eb = before; }
                    SetTilts(r, best, can);
                    int n = 0; for (int i = 0; i < mc; i++) if (can[i]) { if (Mathf.Abs(best[i] - cur[i]) > 0.01f) n++; rec.applied[i] = best[i]; }
                    markersSet += n;
                    float maxT = best.Select(Mathf.Abs).DefaultIfEmpty(0f).Max();
                    sb.AppendLine($"  {nm}: {mc} markers, {n} tilts changed (max {maxT:F1} deg, sign {(ReferenceEquals(best, minus) ? "-" : ReferenceEquals(best, plus) ? "+" : "kept")}); cross-slope mismatch {before:F2} -> {eb:F2} m");
                }
                else Call(r, "Refresh");
                touched++;
            }
            Save(store);
            if (!revert && S.recarveAfterTilt && bm)
            {
                try { Call(net, "BuildRoadNetwork", false, false, false, true, _t); sb.AppendLine("  Terrain re-carved to the tilted roads."); }
                catch (Exception e) { sb.AppendLine("  Re-carve failed: " + (e.InnerException ?? e).Message); }
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(_t.gameObject.scene);
            sb.AppendLine($"  Roads {touched}, marker tilts {(revert ? "restored" : "changed")} {markersSet}, hand-edited markers left alone {handKept}. Record: {BackupPath}. Scene NOT saved.");
            Done(sb);
        }

        /// <summary>Hand tilts on the existing ASH roads (anything this tool did not set), keyed by road name, with marker positions.</summary>
        internal static Dictionary<string, List<(Vector3 pos, float tilt)>> CaptureHandTilts(object net)
        {
            var res = new Dictionary<string, List<(Vector3, float)>>(); var store = Load();
            foreach (var r in (Array)Call(net, "GetRoads"))
            {
                string nm = Name(r); if (nm == null || !nm.StartsWith(Prefix)) continue;
                var m = (Vector3[])Call(r, "GetMarkerPositions"); var rec = store.roads.FirstOrDefault(x => x.road == nm); var list = new List<(Vector3, float)>();
                for (int i = 0; i < m.Length; i++)
                {
                    float cur = GetTilt(r, i);
                    bool recOk = rec != null && rec.applied != null && rec.original != null && i < rec.applied.Length && i < rec.original.Length;
                    bool hand = !recOk ? Mathf.Abs(cur) > 0.01f
                        : Mathf.Abs(cur - rec.applied[i]) >= 0.01f || (Mathf.Abs(rec.original[i]) > 0.01f && Mathf.Abs(rec.applied[i] - rec.original[i]) < 0.01f);
                    if (hand && Mathf.Abs(cur) > 0.01f) list.Add((m[i], cur)); // a tilt reset to 0 (undo / EasyRoads edit mode) is not a hand tilt
                }
                if (list.Count > 0) res[nm] = list;
            }
            return res;
        }

        /// <summary>Build step: tilt freshly created roads to the (uncarved) ground's cross-slope before EasyRoads carves, re-applying hand tilts.</summary>
        internal static string TiltForBuild(List<object> roads, Dictionary<string, List<(Vector3 pos, float tilt)>> hand)
        {
            _t = FindTerrain(); if (!_t || !S.followCrossSlope) return "Cross-slope follow off.";
            var store = Load(); int changed = 0, kept = 0; var per = new List<string>();
            foreach (var r in roads)
            {
                string nm = Name(r); Call(r, "Refresh");
                var m = (Vector3[])Call(r, "GetMarkerPositions"); int mc = m.Length;
                var orig = new float[mc]; var can = Enumerable.Repeat(true, mc).ToArray();
                if (hand.TryGetValue(nm, out var hl))
                    foreach (var (pos, tilt) in hl)
                    {
                        int bi = -1; float bd = 4f;
                        for (int i = 0; i < mc; i++) { float d = Vector2.Distance(new Vector2(m[i].x, m[i].z), new Vector2(pos.x, pos.z)); if (d < bd) { bd = d; bi = i; } }
                        if (bi >= 0) { orig[bi] = tilt; can[bi] = false; kept++; }
                    }
                SetTilts(r, orig, Enumerable.Repeat(true, mc).ToArray());
                if (S.snapToTerrain) { try { Call(r, "SnapToTerrain", true, S.snapOffset, false); } catch { } }
                float before = Mismatch(r, out _);
                var tgt = Targets(r, mc); var plus = new float[mc]; var minus = new float[mc];
                for (int i = 0; i < mc; i++) { plus[i] = can[i] ? tgt[i] : orig[i]; minus[i] = can[i] ? -tgt[i] : orig[i]; }
                SetTilts(r, plus, can); float ep = Mismatch(r, out _);
                SetTilts(r, minus, can); float em = Mismatch(r, out _);
                var best = em <= ep ? minus : plus; float eb = Mathf.Min(ep, em);
                if (eb > before - 0.005f) { best = orig; eb = before; }
                SetTilts(r, best, can);
                int n = 0; for (int i = 0; i < mc; i++) if (can[i] && Mathf.Abs(best[i]) > 0.01f) n++;
                changed += n;
                store.roads.RemoveAll(x => x.road == nm);
                store.roads.Add(new RoadRec { road = nm, original = orig, applied = best.ToArray() });
                per.Add($"{nm.Substring(Prefix.Length)} {before:F2}->{eb:F2} m");
            }
            Save(store);
            return $"Cross-slope tilt: {changed} markers tilted, {kept} hand tilts re-applied; side mismatch {string.Join(", ", per)}.";
        }

        static void Done(StringBuilder sb) { var r = sb.ToString(); Debug.Log(r); Write(r); }

        public static string Report()
        {
            var sb = new StringBuilder("[ASH Roads] Road terrain angles (nothing changed)\n");
            var (net, roads, err) = Gather();
            if (err != null) return sb.AppendLine("  " + err).ToString();
            sb.AppendLine($"  Network build mode: {BuildMode(net)}");
            foreach (var r in roads)
            {
                int mc = ((Vector3[])Call(r, "GetMarkerPositions")).Length;
                var t = Targets(r, mc); float mm = Mismatch(r, out int n);
                float curMax = Enumerable.Range(0, mc).Select(i => Mathf.Abs(GetTilt(r, i))).DefaultIfEmpty(0f).Max();
                sb.AppendLine($"  {Name(r)}: {mc} markers, cross-slope mismatch {mm:F2} m over {n} pts, ground side angle max {t.Select(Mathf.Abs).DefaultIfEmpty(0f).Max():F1} deg, current tilt max {curMax:F1} deg");
            }
            return sb.ToString();
        }
    }
}
