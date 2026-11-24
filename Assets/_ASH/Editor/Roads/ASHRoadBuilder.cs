using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ASH.Prologue;
using ASH.World.Roads;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASH.EditorTools.Roads
{
    /// <summary>t619: pre-town roads with EasyRoads3D (git-ignored vendor, so the API is called by reflection and this compiles
    /// without it). ASH/Systems/Roads/Build Pre-Town Roads: idempotent - restores the EasyRoads network, deletes only the ASH_Road_*
    /// roads and the ASH_RoadDressing root, rebuilds them from ASH_RoadSettings, carves, clears trees / grass / scattered vegetation
    /// along the roads, then dresses them (HPA props, town signs, timber bridges). The TerrainData is backed up once before the first
    /// carve. Never saves the scene.</summary>
    public static class ASHRoadBuilder
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_RoadSettings.asset";
        const string MatDir = "Assets/_ASH/Materials/Roads";
        public const string BackupDir = "Assets/_ASH/Backups/Terrain";
        const string ERRoads = "Assets/EasyRoads3D/Resources/Materials/roads/";
        const string LayoutPath = "Assets/_ASH/Data/Map/ASH_RegionLayout.json";
        const string DressRootName = "ASH_RoadDressing";
        internal const string Prefix = "ASH_Road_";
        static ASHRoadSettings S => ASHRoadSettings.I;

        // ------------------------------------------------------------------ menus
        [MenuItem("ASH/Systems/Roads/Build Pre-Town Roads (idempotent)", priority = 10)]
        public static void BuildMenu() { var r = Build(); Debug.Log(r); WriteReport(r); }
        [MenuItem("ASH/Systems/Roads/Preview Road Profiles (log only)", priority = 11)]
        public static void PreviewMenu() { var r = Preview(); Debug.Log(r); WriteReport(r); }
        [MenuItem("ASH/Systems/Roads/Backup Terrain Data", priority = 12)]
        public static void BackupMenu() { var t = FindTerrain(); Debug.Log(t ? "[ASH Roads] Backup: " + Backup(t.terrainData, true) : "[ASH Roads] No terrain."); }
        [MenuItem("ASH/Systems/Roads/Restore Terrain From Pre-Roads Backup", priority = 16)]
        public static void RestoreBackupMenu() { var r = RestoreBackup(); Debug.Log(r); WriteReport(r); }
        [MenuItem("ASH/Systems/Roads/Remove Pre-Town Roads (restore heights)", priority = 13)]
        public static void RemoveMenu() { var r = Remove(); Debug.Log(r); WriteReport(r); }
        [MenuItem("ASH/Systems/Roads/Update Road Materials", priority = 14)]
        public static void MaterialsMenu() { EnsureSettings(); Debug.Log("[ASH Roads] Materials: " + EnsureMaterials()); }
        [MenuItem("ASH/Systems/Roads/Select Road Settings", priority = 15)]
        public static void SelectMenu() { Selection.activeObject = EnsureSettings(); EditorGUIUtility.PingObject(Selection.activeObject); }

        static void WriteReport(string r) { try { Directory.CreateDirectory("AshBridge"); File.WriteAllText("AshBridge/roads_report.txt", r); } catch { } }

        // ------------------------------------------------------------------ settings / prefabs
        public static ASHRoadSettings EnsureSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHRoadSettings>(SettingsPath);
            if (!s) { s = ScriptableObject.CreateInstance<ASHRoadSettings>(); s.FillDefaults(); AssetDatabase.CreateAsset(s, SettingsPath); }
            bool dirty = false;
            GameObject P(string p) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HPA/Prefabs/" + p + ".prefab");
            void L(List<GameObject> list, params string[] paths) { if (list.Count(x => x) > 0) return; list.Clear(); foreach (var p in paths) { var g = P(p); if (g) { list.Add(g); dirty = true; } } }
            L(s.fencePrefabs, "Fence/Farm_Fence/LOD_SNAP_Farm_Fence_x4_01", "Fence/Farm_Fence/LOD_SNAP_Farm_Fence_x4_02", "Fence/Farm_Fence/LOD_SNAP_Farm_Fence_x4_03");
            L(s.brokenFencePrefabs, "Fence/Fence_03/LOD_Fence_03_Break", "Fence/Fence_03/LOD_Fence_03_Break_02", "Fence/Fence_03/LOD_Fence_03_Break_Fall", "Fence/Farm_Fence/LOD_SNAP_Farm_Fence_01C");
            L(s.carPrefabs, "Vehicles/LOD_Car_01", "Vehicles/LOD_Car_01C", "Vehicles/LOD_Car_01F");
            if (!s.signPrefab) { s.signPrefab = P("Bastion/Signboard_Wood/LOD_Signboard_Wood_01"); dirty |= s.signPrefab != null; }
            if (!s.mailboxPrefab) { s.mailboxPrefab = P("Mail_Box/LOD_Mailbox_01"); dirty |= s.mailboxPrefab != null; }
            if (!s.blockerPrefab) { s.blockerPrefab = P("Street/Road_Sign/LOD_Road_Blocker_01"); dirty |= s.blockerPrefab != null; }
            if (!s.conePrefab) { s.conePrefab = P("Street/Road_Sign/LOD_Traffic_Cone_01"); dirty |= s.conePrefab != null; }
            if (dirty) { EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s); }
            return s;
        }

        // ------------------------------------------------------------------ EasyRoads reflection
        static Assembly ErAsm => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "EasyRoads3Dv3");
        internal static Type ErT(string n) => ErAsm?.GetType("EasyRoads3Dv3." + n);
        internal static object Call(object o, string name, params object[] a)
        {
            var t = o as Type ?? o.GetType(); var inst = o is Type ? null : o;
            foreach (var mi in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (mi.Name != name) continue; var ps = mi.GetParameters(); if (ps.Length != a.Length) continue;
                bool ok = true;
                for (int i = 0; i < a.Length && ok; i++) ok = a[i] == null ? !ps[i].ParameterType.IsValueType : ps[i].ParameterType.IsInstanceOfType(a[i]);
                if (ok) return mi.Invoke(inst, a);
            }
            throw new MissingMethodException(t.Name + "." + name + "/" + a.Length);
        }
        internal static object Fld(object o, string f) => o.GetType().GetField(f)?.GetValue(o);
        internal static void SetFld(object o, string f, object v) { var fi = o.GetType().GetField(f); if (fi != null) fi.SetValue(o, v); }

        // ------------------------------------------------------------------ terrain
        internal static Terrain FindTerrain()
        {
            var t = Terrain.activeTerrain; if (t) return t;
            var go = GameObject.Find("Terrain"); return go ? go.GetComponent<Terrain>() : Object.FindAnyObjectByType<Terrain>();
        }
        static Terrain _t;
        static float H(Vector2 xz) => _t ? _t.SampleHeight(new Vector3(xz.x, 0f, xz.y)) + _t.transform.position.y : 0f;

        /// <summary>One-time copy of the TerrainData asset (heights, trees, grass/details, splats) before the first carve / clear.</summary>
        public static string Backup(TerrainData td, bool force)
        {
            var src = AssetDatabase.GetAssetPath(td); if (string.IsNullOrEmpty(src)) return null;
            ASH.EditorTools.Build.ASHBuildUtil.Folder(BackupDir);
            var dst = $"{BackupDir}/{Path.GetFileNameWithoutExtension(src)}_preRoads.asset";
            if (!force && AssetDatabase.LoadAssetAtPath<TerrainData>(dst)) return dst;
            if (force && AssetDatabase.LoadAssetAtPath<TerrainData>(dst)) dst = $"{BackupDir}/{Path.GetFileNameWithoutExtension(src)}_{DateTime.Now:yyyyMMdd_HHmmss}.asset";
            if (EditorUtility.IsDirty(td))
            {
                // unsaved in-memory terrain edits: snapshot the live data instead of the older file on disk
                var copy = Object.Instantiate(td); AssetDatabase.CreateAsset(copy, dst);
            }
            else if (!AssetDatabase.CopyAsset(src, dst)) return null;
            AssetDatabase.SaveAssets();
            return dst;
        }

        // ------------------------------------------------------------------ materials
        static string EnsureMaterials()
        {
            ASH.EditorTools.Build.ASHBuildUtil.Folder(MatDir);
            var sh = Shader.Find("ASH/Accumulation Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            var sb = new StringBuilder();
            foreach (var surf in S.surfaces)
            {
                if (surf == null) continue;
                string srcName = surf.kind == ASHRoadSettings.Kind.Dirt ? "Dirt Grass" : surf.kind == ASHRoadSettings.Kind.Gravel ? "Dirt Gravel" : "Old Asphalt Decal Blend";
                var path = $"{MatDir}/ASH_Road_{surf.kind}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                var src = AssetDatabase.LoadAssetAtPath<Material>(ERRoads + srcName + ".mat");
                if (!m)
                {
                    m = new Material(sh) { name = "ASH_Road_" + surf.kind };
                    string got = "none";
                    if (src)
                    {
                        foreach (var p in new[] { "_MainTex", "_Albedo", "_Road", "_Maintex", "_BaseMap", "_Splat0", "_Detail1" })
                            if (src.HasProperty(p) && src.GetTexture(p)) { m.SetTexture("_BaseMap", src.GetTexture(p)); m.SetTextureScale("_BaseMap", src.GetTextureScale(p)); m.SetTextureOffset("_BaseMap", src.GetTextureOffset(p)); got = p; break; }
                        foreach (var p in new[] { "_BumpMap", "_NormalRoad", "_MainTexNormal", "_Normal0" })
                            if (src.HasProperty(p) && src.GetTexture(p)) { m.SetTexture("_BumpMap", src.GetTexture(p)); m.SetTextureScale("_BumpMap", src.GetTextureScale(p)); m.EnableKeyword("_NORMALMAP"); got += "+" + p; break; }
                    }
                    var tint = surf.kind == ASHRoadSettings.Kind.Blacktop ? new Color(0.62f, 0.62f, 0.62f) : surf.kind == ASHRoadSettings.Kind.Gravel ? new Color(0.78f, 0.74f, 0.68f) : new Color(0.62f, 0.52f, 0.4f);
                    m.SetColor("_BaseColor", m.GetTexture("_BaseMap") ? Color.white : tint);
                    m.SetFloat("_Smoothness", surf.kind == ASHRoadSettings.Kind.Blacktop ? 0.22f : 0.1f); m.SetFloat("_Metallic", 0f);
                    AssetDatabase.CreateAsset(m, path);
                    sb.Append($"{surf.kind}: new from '{srcName}' ({(src ? got : "source missing")}); ");
                }
                else
                {
                    if (m.shader != sh && sh) m.shader = sh;
                    sb.Append($"{surf.kind}: kept {path}; ");
                }
                surf.material = m;
            }
            S.ApplyMaterials();
            EditorUtility.SetDirty(S); AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // ------------------------------------------------------------------ plan
        sealed class Plan
        {
            public ASHRoadSettings.Segment seg; public float width;
            public List<Vector3> markers = new List<Vector3>(); public List<bool> carve = new List<bool>();
            public List<Vector2> dense = new List<Vector2>();
            public List<(int a, int b, Vector3 pa, Vector3 pb)> bridges = new List<(int, int, Vector3, Vector3)>();
            public float maxCut, maxFill, length; public Vector2 cutAt, fillAt; public List<Vector3> spline;
        }

        [Serializable] class XZ { public float x, z; }
        [Serializable] class CreekJ { public float acc; public XZ[] pts; }
        [Serializable] class LayoutJ { public CreekJ[] creeks; }
        static List<List<Vector2>> Creeks()
        {
            var res = new List<List<Vector2>>();
            try { var l = JsonUtility.FromJson<LayoutJ>(File.ReadAllText(LayoutPath)); foreach (var c in l.creeks ?? new CreekJ[0]) res.Add(c.pts.Select(p => new Vector2(p.x, p.z)).ToList()); } catch (Exception e) { Debug.LogWarning("[ASH Roads] creeks: " + e.Message); }
            return res;
        }

        static ASHRoadSettings.Zone ZoneAt(Vector2 p, float pad = 0f)
        {
            foreach (var z in S.noCarveZones) if (z != null && Vector2.Distance(p, z.xz) < z.radius + pad) return z;
            return null;
        }
        static float ZoneEdgeDist(Vector2 p)
        {
            float d = float.MaxValue; foreach (var z in S.noCarveZones) if (z != null) d = Mathf.Min(d, Vector2.Distance(p, z.xz) - z.radius);
            return d;
        }

        static List<Vector2> CatmullDense(List<Vector2> w, float step)
        {
            var o = new List<Vector2>();
            if (w.Count < 2) return o;
            for (int i = 0; i < w.Count - 1; i++)
            {
                Vector2 p0 = w[Mathf.Max(0, i - 1)], p1 = w[i], p2 = w[i + 1], p3 = w[Mathf.Min(w.Count - 1, i + 2)];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / step));
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(w[w.Count - 1]);
            return o;
        }

        static bool SegInter(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 hit)
        {
            hit = default; float den = (a.x - b.x) * (c.y - d.y) - (a.y - b.y) * (c.x - d.x); if (Mathf.Abs(den) < 1e-6f) return false;
            float t = ((a.x - c.x) * (c.y - d.y) - (a.y - c.y) * (c.x - d.x)) / den, u = -((a.x - b.x) * (a.y - c.y) - (a.y - b.y) * (a.x - c.x)) / den;
            if (t < 0f || t > 1f || u < 0f || u > 1f) return false; hit = a + t * (b - a); return true;
        }

        static Plan MakePlan(ASHRoadSettings.Segment seg, Dictionary<string, Plan> done, List<List<Vector2>> creeks)
        {
            var p = new Plan { seg = seg, width = S.WidthOf(seg) };
            var w = seg.waypoints.ToList(); if (w.Count < 2) return null;
            if (!string.IsNullOrEmpty(seg.startOn) && done.TryGetValue(seg.startOn, out var parent))
            {
                var dir = (w[1] - w[0]).normalized; w[0] += dir * (parent.width * 0.5f);
            }
            var dense = CatmullDense(w, 1f); p.dense = dense;
            var acc = new float[dense.Count]; for (int i = 1; i < dense.Count; i++) acc[i] = acc[i - 1] + Vector2.Distance(dense[i - 1], dense[i]);
            p.length = acc[acc.Length - 1];
            var raw = dense.Select(H).ToArray();

            // bridges: creek crossings + extras -> arc-length ranges
            var surf = S.SurfaceFor(seg.kind); float hs = surf != null ? surf.bridgeHalfSpan : 10f;
            var ranges = new List<(float a, float b)>();
            void AddRange(float s, float h) { float a = Mathf.Max(0.5f, s - h), b = Mathf.Min(p.length - 0.5f, s + h); if (b - a > 2f) ranges.Add((a, b)); }
            if (S.autoBridges)
                for (int i = 0; i < dense.Count - 1; i++)
                    foreach (var c in creeks) for (int j = 0; j < c.Count - 1; j++)
                            if (SegInter(dense[i], dense[i + 1], c[j], c[j + 1], out var hit)) AddRange(acc[i] + Vector2.Distance(dense[i], hit), hs);
            foreach (var eb in S.extraBridges)
                if (eb != null && eb.segment == seg.id) { int bi = 0; float bd = float.MaxValue; for (int i = 0; i < dense.Count; i++) { float d = Vector2.Distance(dense[i], eb.xz); if (d < bd) { bd = d; bi = i; } } AddRange(acc[bi], eb.halfSpan > 0.1f ? eb.halfSpan : hs); }
            ranges = ranges.OrderBy(r => r.a).ToList();
            for (int i = ranges.Count - 1; i > 0; i--) if (ranges[i].a <= ranges[i - 1].b + 4f) { ranges[i - 1] = (ranges[i - 1].a, Mathf.Max(ranges[i].b, ranges[i - 1].b)); ranges.RemoveAt(i); }

            int Idx(float s) { int i = Array.BinarySearch(acc, s); if (i < 0) i = ~i; return Mathf.Clamp(i, 0, acc.Length - 1); }
            float Smooth(int i)
            {
                float win = S.heightSmoothing * 0.5f; if (win < 0.5f) return raw[i];
                double sum = 0; int n = 0; int a = Idx(acc[i] - win), b = Idx(acc[i] + win);
                for (int k = a; k <= b; k++) { sum += raw[k]; n++; }
                return n > 0 ? (float)(sum / n) : raw[i];
            }
            float RoadY(int i)
            {
                var xz = dense[i];
                float edge = ZoneEdgeDist(xz);
                float sm = Smooth(i);
                if (edge < 0f || !S.carve) return raw[i] + S.roadRaise + 0.08f;
                float k = Mathf.Clamp01(edge / 15f);
                if (S.endTaper > 0.5f) k = Mathf.Min(k, Mathf.SmoothStep(0f, 1f, Mathf.Min(acc[i], p.length - acc[i]) / S.endTaper));
                float y = Mathf.Lerp(raw[i] + S.roadRaise + 0.08f, sm + S.roadRaise, k);
                return Mathf.Clamp(y, raw[i] - S.maxCutDepth, raw[i] + S.maxFillHeight);
            }

            // markers (bridge ends are locked so spacing never drops them)
            var idx = new List<int> { 0 }; var locked = new HashSet<int> { 0 };
            float last = 0f; int ri = 0;
            for (int i = 1; i < dense.Count - 1; i++)
            {
                while (ri < ranges.Count && acc[i] > ranges[ri].b) ri++;
                if (ri < ranges.Count && acc[i] >= ranges[ri].a)
                {
                    int ia = Mathf.Max(1, Idx(ranges[ri].a)), ib = Mathf.Min(dense.Count - 2, Idx(ranges[ri].b));
                    int lastI = idx[idx.Count - 1];
                    if (idx.Count > 1 && !locked.Contains(lastI) && acc[ia] - acc[lastI] < 3f) idx.RemoveAt(idx.Count - 1);
                    if (ib > ia) { idx.Add(ia); idx.Add(ib); locked.Add(ia); locked.Add(ib); last = acc[ib]; i = ib; }
                    ri++; continue;
                }
                float sp = ZoneAt(dense[i], 10f) != null ? S.protectedMarkerSpacing : S.markerSpacing;
                if (acc[i] - last >= sp) { idx.Add(i); last = acc[i]; }
            }
            { int lastI = idx[idx.Count - 1]; if (idx.Count > 1 && !locked.Contains(lastI) && acc[acc.Length - 1] - acc[lastI] < 3f) idx.RemoveAt(idx.Count - 1); }
            idx.Add(dense.Count - 1);
            idx = idx.Distinct().OrderBy(x => x).ToList();

            var bridgeStarts = new HashSet<int>();
            foreach (var r in ranges)
            {
                int ia = Mathf.Max(1, Idx(r.a)), ib = Mathf.Min(dense.Count - 2, Idx(r.b)); int ma = idx.IndexOf(ia), mb = idx.IndexOf(ib);
                if (ma < 0 || mb < 0) continue;
                float deck = Mathf.Max(raw[ia], raw[ib]) + S.deckRaise;
                bridgeStarts.Add(ma);
                p.bridges.Add((ma, mb, new Vector3(dense[ia].x, deck, dense[ia].y), new Vector3(dense[ib].x, deck, dense[ib].y)));
            }
            for (int m = 0; m < idx.Count; m++)
            {
                int i = idx[m]; float y = RoadY(i);
                foreach (var b in p.bridges) if (m == b.a || m == b.b) y = b.pa.y;
                p.markers.Add(new Vector3(dense[i].x, y, dense[i].y));
                bool carve = S.carve && ZoneAt(dense[i]) == null && !bridgeStarts.Contains(m);
                p.carve.Add(carve);
                if (carve) { float d = y - raw[i]; if (d > p.maxFill) { p.maxFill = d; p.fillAt = dense[i]; } if (-d > p.maxCut) { p.maxCut = -d; p.cutAt = dense[i]; } }
            }
            return p;
        }

        // EasyRoads types may only expose ctors with (optional) parameters; pick the simplest one we can satisfy.
        internal static object NewEr(Type t)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var ctors = t.GetConstructors(F).OrderBy(c => c.GetParameters().Length).ToArray();
            foreach (var c in ctors)
            {
                var ps = c.GetParameters(); var args = new object[ps.Length]; bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    var pt = ps[i].ParameterType;
                    if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                    else if (pt == typeof(string)) args[i] = "ASH Road Network";
                    else if (pt == typeof(bool)) args[i] = false;
                    else if (pt.IsValueType) args[i] = Activator.CreateInstance(pt);
                    else if (!pt.IsAbstract && pt.GetConstructor(Type.EmptyTypes) == null) { ok = false; break; }
                    else args[i] = null;
                }
                if (!ok) continue;
                try { return c.Invoke(args); } catch (Exception e) { Debug.LogWarning($"[ASH Roads] {t.Name}({string.Join(", ", ps.Select(x => x.ParameterType.Name))}) failed: {e.InnerException?.Message ?? e.Message}"); }
            }
            throw new MissingMethodException($"[ASH Roads] no usable ctor for {t.FullName}: " + string.Join(" | ", ctors.Select(c => "(" + string.Join(", ", c.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name)) + ")")));
        }

        static List<Plan> MakePlans(StringBuilder sb)
        {
            var creeks = Creeks(); var done = new Dictionary<string, Plan>(); var list = new List<Plan>();
            foreach (var seg in S.segments)
            {
                if (seg == null || !seg.enabled) continue;
                var p = MakePlan(seg, done, creeks); if (p == null) continue;
                done[seg.id] = p; list.Add(p);
                sb.AppendLine($"  {seg.id} ({seg.kind}, {p.width:F1} m): {p.length:F0} m, {p.markers.Count} markers, bridges {p.bridges.Count}, max cut {p.maxCut:F1} m at ({p.cutAt.x:F0}, {p.cutAt.y:F0}) / fill {p.maxFill:F1} m at ({p.fillAt.x:F0}, {p.fillAt.y:F0}), no-carve markers {p.carve.Count(c => !c)}");
            }
            return list;
        }

        public static string Preview()
        {
            EnsureSettings(); _t = FindTerrain(); if (!_t) return "[ASH Roads] No terrain.";
            var sb = new StringBuilder("[ASH Roads] Preview (nothing changed)\n"); MakePlans(sb); return sb.ToString();
        }

        // ------------------------------------------------------------------ build
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[ASH Roads] Stop Play first (EasyRoads builds in edit mode only).";
            if (ErAsm == null) return "[ASH Roads] EasyRoads3D (EasyRoads3Dv3.dll) not found - nothing built.";
            EnsureSettings(); _t = FindTerrain(); if (!_t) return "[ASH Roads] No terrain.";
            var sb = new StringBuilder("[ASH Roads] Build Pre-Town Roads\n");
            var td = _t.terrainData;
            var backup = Backup(td, false);
            if (backup == null) return "[ASH Roads] TerrainData backup failed - nothing changed.";
            sb.AppendLine("  TerrainData backup: " + backup);
            sb.AppendLine("  Materials: " + EnsureMaterials());

            // network: restore heights, remove only our roads
            var existing = Object.FindObjectsByType(ErT("ERModularBase"));
            if (existing.Length > 1) sb.AppendLine($"  NOTE: {existing.Length} EasyRoads networks in the scene; using the first.");
            var net = NewEr(ErT("ERRoadNetwork"));
            if (Fld(net, "isInBuildMode") is bool bm && bm) { Call(net, "RestoreRoadNetwork"); sb.AppendLine("  Restored the EasyRoads network (heights back to pre-build)."); }
            var handTilts = ASHRoadTerrainAngles.CaptureHandTilts(net);
            var oldMarkers = new Dictionary<string, Vector3[]>();
            foreach (var r in (Array)Call(net, "GetRoads")) { var n = Call(r, "GetName") as string; if (n != null && n.StartsWith(Prefix)) try { oldMarkers[n] = (Vector3[])Call(r, "GetMarkerPositions"); } catch { } }
            int removed = 0;
            foreach (var r in (Array)Call(net, "GetRoads")) { var n = Call(r, "GetName") as string; if (n != null && n.StartsWith(Prefix)) { Call(r, "Destroy"); removed++; } }
            int foreign = ((Array)Call(net, "GetRoads")).Length;
            if (removed > 0) sb.AppendLine($"  Removed {removed} previous ASH roads.");
            if (foreign > 0) sb.AppendLine($"  Left {foreign} other EasyRoads roads untouched (they are rebuilt with the network).");
            var oldDress = GameObject.Find(DressRootName); if (oldDress) Object.DestroyImmediate(oldDress);

            // roads
            var plans = MakePlans(sb);
            // hand-moved markers: keep his positions for that road (carve per protected zone, bridges only if the marker count still matches)
            foreach (var p in plans)
            {
                if (!S.keepHandMovedMarkers || !oldMarkers.TryGetValue(Prefix + p.seg.id, out var om) || om.Length < 2) continue;
                bool moved = om.Length != p.markers.Count || om.Where((v, i) => Vector2.Distance(new Vector2(v.x, v.z), new Vector2(p.markers[i].x, p.markers[i].z)) > 0.5f).Any();
                if (!moved) continue;
                bool same = om.Length == p.markers.Count;
                p.markers = om.ToList(); p.carve = om.Select(v => S.carve && ZoneAt(new Vector2(v.x, v.z)) == null).ToList();
                if (!same) p.bridges.Clear();
                sb.AppendLine($"  {p.seg.id}: markers were moved by hand - kept your {om.Length} marker positions{(same ? "" : " (marker count changed, auto bridges skipped)")}.");
            }
            var roads = new Dictionary<Plan, object>();
            foreach (var p in plans)
            {
                var surf = S.SurfaceFor(p.seg.kind);
                var rt = NewEr(ErT("ERRoadType"));
                SetFld(rt, "roadTypeName", "ASH " + p.seg.kind); SetFld(rt, "roadWidth", p.width); SetFld(rt, "roadMaterial", surf?.material);
                SetFld(rt, "hasMeshCollider", true); SetFld(rt, "terrainDeformation", S.carve); SetFld(rt, "layer", _t.gameObject.layer); SetFld(rt, "tag", "Untagged");
                var road = Call(net, "CreateRoad", Prefix + p.seg.id, rt, p.markers.ToArray());
                roads[p] = road;
                try { Call(road, "SetTerrainDeformation", S.carve, false); } catch { }
                if (S.carve) for (int i = 0; i < p.carve.Count; i++) if (!p.carve[i]) { try { Call(road, "SetTerrainDeformation", i, false); } catch { } }
                if (!S.carve) { try { Call(road, "SnapToTerrain", true, S.roadRaise, false); } catch { } }
                try { if (surf?.material) Call(road, "SetMaterial", surf.material); } catch { }
            }
            if (S.followCrossSlope) { try { sb.AppendLine("  " + ASHRoadTerrainAngles.TiltForBuild(roads.Values.ToList(), handTilts)); } catch (Exception e) { sb.AppendLine("  Cross-slope tilt failed: " + (e.InnerException ?? e).Message); } }
            try { Call(net, "BuildRoadNetwork", false, false, false, S.carve, _t); }
            catch (Exception e) { sb.AppendLine("  Build(…, terrain) failed (" + (e.InnerException ?? e).Message + "), using BuildRoadNetwork()."); Call(net, "BuildRoadNetwork"); }
            foreach (var kv in roads)
            {
                try { kv.Key.spline = ((Vector3[])Call(kv.Value, "GetSplinePointsCenter")).ToList(); } catch { }
                if (kv.Key.spline == null || kv.Key.spline.Count < 2) kv.Key.spline = kv.Key.markers;
                var go = Fld(kv.Value, "gameObject") as GameObject;
                if (go) foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) if (kv.Key.seg != null && S.SurfaceFor(kv.Key.seg.kind)?.material) mr.sharedMaterial = S.SurfaceFor(kv.Key.seg.kind).material;
            }
            sb.AppendLine($"  EasyRoads build done ({(S.carve ? "carved roadbed" : "snapped to terrain")}).");

            // clearing
            sb.AppendLine("  " + Clear(plans, td));

            // dressing
            if (S.dressing) sb.AppendLine("  " + Dress(plans));
            if (S.snapBarricadeToRoad) sb.AppendLine("  " + SnapBarricade(plans));

            EditorSceneManager.MarkSceneDirty(_t.gameObject.scene);
            sb.AppendLine("  Scene NOT saved (roads, dressing and the cleared scatter only persist when you save the scene; the carve and tree/grass clearing live in the TerrainData).");
            return sb.ToString();
        }

        /// <summary>Copies heights, splat maps, trees and grass/details from the pre-roads TerrainData backup back into the live terrain.
        /// Restores the EasyRoads network first if it is in build mode. Does not touch roads or scene objects.</summary>
        public static string RestoreBackup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[ASH Roads] Stop Play first.";
            var t = FindTerrain(); if (!t) return "[ASH Roads] No terrain.";
            var td = t.terrainData; string path = BackupDir + "/" + td.name + "_preRoads.asset";
            var bk = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (!bk) { var any = AssetDatabase.FindAssets("t:TerrainData", new[] { BackupDir }); if (any.Length > 0) { path = AssetDatabase.GUIDToAssetPath(any[0]); bk = AssetDatabase.LoadAssetAtPath<TerrainData>(path); } }
            if (!bk) return "[ASH Roads] No pre-roads backup in " + BackupDir + " - nothing changed.";
            if (bk.heightmapResolution != td.heightmapResolution) return $"[ASH Roads] Backup heightmap {bk.heightmapResolution} != live {td.heightmapResolution} - nothing changed.";
            var sb = new StringBuilder("[ASH Roads] Restore terrain from " + path + "\n");
            if (ErAsm != null) { var net = NewEr(ErT("ERRoadNetwork")); if (Fld(net, "isInBuildMode") is bool bm && bm) { Call(net, "RestoreRoadNetwork"); sb.AppendLine("  EasyRoads network was in build mode - restored it first."); } }
            int r = td.heightmapResolution;
            td.SetHeights(0, 0, bk.GetHeights(0, 0, r, r)); sb.AppendLine($"  Heights {r}x{r} restored.");
            if (bk.alphamapLayers == td.alphamapLayers && bk.alphamapResolution == td.alphamapResolution) { td.SetAlphamaps(0, 0, bk.GetAlphamaps(0, 0, bk.alphamapWidth, bk.alphamapHeight)); sb.AppendLine($"  Splat maps restored ({bk.alphamapLayers} layers)."); }
            else sb.AppendLine("  Splat maps skipped (layer count / resolution differs).");
            if (bk.treePrototypes.Length == td.treePrototypes.Length) { td.SetTreeInstances(bk.treeInstances, false); sb.AppendLine($"  Trees restored: {bk.treeInstanceCount} (was {td.treeInstanceCount})."); }
            else sb.AppendLine("  Trees skipped (prototype count differs).");
            if (bk.detailPrototypes.Length == td.detailPrototypes.Length && bk.detailResolution == td.detailResolution)
            { for (int i = 0; i < bk.detailPrototypes.Length; i++) td.SetDetailLayer(0, 0, i, bk.GetDetailLayer(0, 0, bk.detailWidth, bk.detailHeight, i)); sb.AppendLine($"  Grass/detail layers restored: {bk.detailPrototypes.Length}."); }
            else sb.AppendLine("  Grass/detail skipped (prototype count / resolution differs).");
            t.Flush(); EditorUtility.SetDirty(td); EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            sb.AppendLine("  Scene/project NOT saved (File > Save Project writes the TerrainData).");
            return sb.ToString();
        }

        public static string Remove()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[ASH Roads] Stop Play first.";
            if (ErAsm == null) return "[ASH Roads] EasyRoads3D not found.";
            var net = NewEr(ErT("ERRoadNetwork"));
            if (Fld(net, "isInBuildMode") is bool bm && bm) Call(net, "RestoreRoadNetwork");
            int n = 0; foreach (var r in (Array)Call(net, "GetRoads")) { var nm = Call(r, "GetName") as string; if (nm != null && nm.StartsWith(Prefix)) { Call(r, "Destroy"); n++; } }
            var d = GameObject.Find(DressRootName); if (d) Object.DestroyImmediate(d);
            if (((Array)Call(net, "GetRoads")).Length > 0) Call(net, "BuildRoadNetwork");
            var t = FindTerrain(); if (t) EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            return $"[ASH Roads] Removed {n} ASH roads + dressing; terrain heights restored by EasyRoads. Cleared trees/grass are not restored (use the backup in {BackupDir}). Scene not saved.";
        }

        // ------------------------------------------------------------------ clearing
        struct Seg2 { public Vector2 a, b; public float r; }
        static float DistSeg(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float L = ab.sqrMagnitude; float t = L > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / L) : 0f; return Vector2.Distance(p, a + t * ab); }
        static List<Seg2> Segs(List<Plan> plans, Func<Plan, float> radius)
        {
            var l = new List<Seg2>();
            foreach (var p in plans) { float r = radius(p); var s = p.spline; for (int i = 0; i < s.Count - 1; i++) l.Add(new Seg2 { a = new Vector2(s[i].x, s[i].z), b = new Vector2(s[i + 1].x, s[i + 1].z), r = r }); }
            return l;
        }
        sealed class Grid
        {
            const float C = 32f; readonly Dictionary<long, List<int>> _g = new Dictionary<long, List<int>>(); readonly List<Seg2> _s;
            static long K(int x, int z) => ((long)x << 32) ^ (uint)z;
            public Grid(List<Seg2> s)
            {
                _s = s;
                for (int i = 0; i < s.Count; i++)
                {
                    var mn = Vector2.Min(s[i].a, s[i].b) - Vector2.one * s[i].r; var mx = Vector2.Max(s[i].a, s[i].b) + Vector2.one * s[i].r;
                    for (int x = Mathf.FloorToInt(mn.x / C); x <= Mathf.FloorToInt(mx.x / C); x++) for (int z = Mathf.FloorToInt(mn.y / C); z <= Mathf.FloorToInt(mx.y / C); z++)
                        { if (!_g.TryGetValue(K(x, z), out var l)) _g[K(x, z)] = l = new List<int>(); l.Add(i); }
                }
            }
            public bool Hit(Vector2 p)
            {
                if (!_g.TryGetValue(K(Mathf.FloorToInt(p.x / C), Mathf.FloorToInt(p.y / C)), out var l)) return false;
                foreach (var i in l) if (DistSeg(p, _s[i].a, _s[i].b) < _s[i].r) return true;
                return false;
            }
        }

        static string Clear(List<Plan> plans, TerrainData td)
        {
            var sb = new StringBuilder("Clearing: ");
            Vector3 tp = _t.transform.position, size = td.size;
            if (S.clearTrees)
            {
                var g = new Grid(Segs(plans, p => p.width * 0.5f + S.treeShoulder));
                var all = td.treeInstances; var keep = new List<TreeInstance>(all.Length);
                foreach (var ti in all) { var w = new Vector2(tp.x + ti.position.x * size.x, tp.z + ti.position.z * size.z); if (!g.Hit(w)) keep.Add(ti); }
                int n = all.Length - keep.Count;
                if (n > 0) td.SetTreeInstances(keep.ToArray(), true);
                sb.Append($"{n} terrain trees removed (of {all.Length}); ");
            }
            if (S.clearDetails && td.detailPrototypes.Length > 0)
            {
                int res = td.detailResolution; float cx = size.x / res, cz = size.z / res;
                long cleared = 0, inst = 0;
                foreach (var p in plans)
                {
                    float r = p.width * 0.5f + S.detailShoulder;
                    var pts = p.spline.Select(v => new Vector2(v.x, v.z)).ToList();
                    var mn = pts.Aggregate(Vector2.Min) - Vector2.one * r; var mx = pts.Aggregate(Vector2.Max) + Vector2.one * r;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt((mn.x - tp.x) / cx), 0, res - 1), z0 = Mathf.Clamp(Mathf.FloorToInt((mn.y - tp.z) / cz), 0, res - 1);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt((mx.x - tp.x) / cx), 0, res - 1), z1 = Mathf.Clamp(Mathf.CeilToInt((mx.y - tp.z) / cz), 0, res - 1);
                    int w = x1 - x0 + 1, h = z1 - z0 + 1; if (w <= 0 || h <= 0) continue;
                    var mask = new bool[h, w];
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        var a = pts[i]; var b = pts[i + 1]; var smn = Vector2.Min(a, b) - Vector2.one * r; var smx = Vector2.Max(a, b) + Vector2.one * r;
                        int sx0 = Mathf.Max(x0, Mathf.FloorToInt((smn.x - tp.x) / cx)), sx1 = Mathf.Min(x1, Mathf.CeilToInt((smx.x - tp.x) / cx));
                        int sz0 = Mathf.Max(z0, Mathf.FloorToInt((smn.y - tp.z) / cz)), sz1 = Mathf.Min(z1, Mathf.CeilToInt((smx.y - tp.z) / cz));
                        for (int z = sz0; z <= sz1; z++) for (int x = sx0; x <= sx1; x++)
                            if (!mask[z - z0, x - x0] && DistSeg(new Vector2(tp.x + (x + 0.5f) * cx, tp.z + (z + 0.5f) * cz), a, b) < r) mask[z - z0, x - x0] = true;
                    }
                    for (int layer = 0; layer < td.detailPrototypes.Length; layer++)
                    {
                        var d = td.GetDetailLayer(x0, z0, w, h, layer); bool ch = false;
                        for (int z = 0; z < h; z++) for (int x = 0; x < w; x++) if (mask[z, x] && d[z, x] != 0) { inst += d[z, x]; d[z, x] = 0; cleared++; ch = true; }
                        if (ch) td.SetDetailLayer(x0, z0, layer, d);
                    }
                }
                sb.Append($"grass/detail: {cleared} cells cleared ({inst} density units) over {td.detailPrototypes.Length} layers; ");
            }
            if (S.clearScatteredPrefabs)
            {
                var g = new Grid(Segs(plans, p => p.width * 0.5f + S.prefabShoulder));
                int n = 0; var names = new Dictionary<string, int>();
                var scene = _t.gameObject.scene;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (!tr || !S.scatterRoots.Contains(tr.name)) continue;
                        foreach (var c in tr.GetComponentsInChildren<Transform>(true).ToArray())
                        {
                            if (!c || c == tr) continue;
                            var nm = c.name; if (!S.scatterNamePrefixes.Any(px => !string.IsNullOrEmpty(px) && nm.StartsWith(px))) continue;
                            if (!g.Hit(new Vector2(c.position.x, c.position.z))) continue;
                            if (PrefabUtility.IsPartOfPrefabInstance(c.gameObject) && PrefabUtility.GetOutermostPrefabInstanceRoot(c.gameObject) != c.gameObject) continue;
                            var key = S.scatterNamePrefixes.First(px => nm.StartsWith(px)); names[key] = names.TryGetValue(key, out var k) ? k + 1 : 1;
                            Undo.DestroyObjectImmediate(c.gameObject); n++;
                        }
                    }
                sb.Append($"{n} scattered prefabs removed ({string.Join(", ", names.Select(kv => kv.Key + " " + kv.Value))}).");
            }
            EditorUtility.SetDirty(td);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ dressing
        static System.Random _rng;
        static Transform _dress;
        static Material _wood;

        static (Vector3 pos, Vector3 dir) At(Plan p, float s)
        {
            var sp = p.spline; float acc = 0f;
            for (int i = 0; i < sp.Count - 1; i++)
            {
                float L = Vector3.Distance(sp[i], sp[i + 1]);
                if (acc + L >= s || i == sp.Count - 2) { float t = L > 1e-4f ? Mathf.Clamp01((s - acc) / L) : 0f; var d = sp[i + 1] - sp[i]; d.y = 0f; return (Vector3.Lerp(sp[i], sp[i + 1], t), d.normalized); }
                acc += L;
            }
            return (sp[sp.Count - 1], Vector3.forward);
        }
        static float Len(Plan p) { float a = 0f; for (int i = 0; i < p.spline.Count - 1; i++) a += Vector3.Distance(p.spline[i], p.spline[i + 1]); return a; }
        static Vector3 Ground(Vector3 p) { p.y = H(new Vector2(p.x, p.z)); return p; }

        static GameObject Place(GameObject prefab, Vector3 pos, Quaternion rot, string name)
        {
            if (!prefab) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _dress);
            go.transform.SetPositionAndRotation(pos, rot); if (!string.IsNullOrEmpty(name)) go.name = name;
            return go;
        }
        static Bounds LocalBounds(GameObject prefab)
        {
            var b = new Bounds(); bool any = false;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue; var m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++) { var c = m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z)); if (!any) { b = new Bounds(c, Vector3.zero); any = true; } else b.Encapsulate(c); }
            }
            return b;
        }

        static string Dress(List<Plan> plans)
        {
            _rng = new System.Random(S.randomSeed); var used = new Dictionary<string, int>();
            void U(GameObject pf, int n = 1) { if (!pf || n <= 0) return; used[pf.name] = used.TryGetValue(pf.name, out var k) ? k + n : n; }
            _dress = new GameObject(DressRootName).transform;
            Undo.RegisterCreatedObjectUndo(_dress.gameObject, "ASH Roads");
            _wood = null;
            if (S.signPrefab) { var r = S.signPrefab.GetComponentInChildren<Renderer>(); if (r) _wood = r.sharedMaterial; }
            if (!_wood) { _wood = new Material(Shader.Find("Universal Render Pipeline/Lit")); _wood.color = new Color(0.36f, 0.26f, 0.17f); }

            // junction / destination signs at the start of each road
            int signs = 0;
            foreach (var p in plans)
            {
                if (string.IsNullOrEmpty(p.seg.signText)) continue;
                var (pos, dir) = At(p, Mathf.Min(6f, Len(p) * 0.2f));
                var right = Vector3.Cross(Vector3.up, dir);
                var at = Ground(pos + right * (p.width * 0.5f + 1.6f));
                if (Sign(at, -dir, p.seg.signText, 1f, null)) { signs++; U(S.signPrefab); }
            }

            // fences along gravel and blacktop
            int fences = 0;
            if (S.fenceEvery > 1f && S.fencePrefabs.Count(x => x) > 0)
                foreach (var p in plans)
                {
                    if (p.seg.kind == ASHRoadSettings.Kind.Dirt) continue;
                    float L = Len(p); int side = 1;
                    for (float s = 70f; s < L - 40f; s += S.fenceEvery)
                    {
                        side = -side; float t = s;
                        for (int k = 0; k < S.fenceRunLength; k++)
                        {
                            bool broken = _rng.NextDouble() < S.brokenFencePrefabs.Count(x => x) * S.brokenFenceChance / Mathf.Max(1, S.brokenFencePrefabs.Count(x => x));
                            var list = (broken ? S.brokenFencePrefabs : S.fencePrefabs).Where(x => x).ToList(); if (list.Count == 0) list = S.fencePrefabs.Where(x => x).ToList();
                            var pf = list[_rng.Next(list.Count)]; var lb = LocalBounds(pf);
                            bool alongX = lb.size.x >= lb.size.z; float len = Mathf.Max(1f, alongX ? lb.size.x : lb.size.z);
                            var (pos, dir) = At(p, t + len * 0.5f); var right = Vector3.Cross(Vector3.up, dir);
                            var c = pos + right * side * (p.width * 0.5f + S.fenceOffset);
                            if (ZoneAt(new Vector2(c.x, c.z), 6f) != null || OnAnyRoad(plans, c, p, 1.5f)) { t += len; continue; }
                            var rot = Quaternion.LookRotation(dir) * (alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity) * Quaternion.Euler(0f, (float)(_rng.NextDouble() * 4 - 2), 0f);
                            var lc = rot * lb.center; lc.y = 0f;
                            if (Place(pf, Ground(c) - lc - Vector3.up * 0.05f, rot, null)) { fences++; U(pf); }
                            t += len;
                        }
                    }
                }

            // mailbox on the incoming road, abandoned cars on the blacktop, blocker + cones near the old barricade line
            var inc = plans.FirstOrDefault(x => x.seg.id == "incoming");
            if (inc != null && S.mailboxPrefab) { var (pos, dir) = At(inc, Len(inc) * 0.35f); var right = Vector3.Cross(Vector3.up, dir); if (Place(S.mailboxPrefab, Ground(pos + right * (inc.width * 0.5f + 1.2f)), Quaternion.LookRotation(-right), "Mailbox (Grandpa)")) U(S.mailboxPrefab); }
            var bt = plans.FirstOrDefault(x => x.seg.kind == ASHRoadSettings.Kind.Blacktop);
            int cars = 0;
            if (bt != null)
            {
                var carList = S.carPrefabs.Where(x => x).ToList();
                for (int i = 0; i < S.abandonedCars && carList.Count > 0; i++)
                {
                    float f = (i + 1f) / (S.abandonedCars + 1f) * 0.8f + 0.05f; var (pos, dir) = At(bt, Len(bt) * f); var right = Vector3.Cross(Vector3.up, dir);
                    float side = i % 2 == 0 ? 1f : -1f; var c = Ground(pos + right * side * (bt.width * 0.5f + 2.6f));
                    if (ZoneAt(new Vector2(c.x, c.z), 10f) != null) continue;
                    var pf = carList[i % carList.Count];
                    if (Place(pf, c, Quaternion.LookRotation(dir * (i % 3 == 1 ? -1f : 1f)) * Quaternion.Euler(0f, (float)(_rng.NextDouble() * 30 - 15), (float)(_rng.NextDouble() * 6 - 3)), "Abandoned car")) { cars++; U(pf); }
                }
                // town signs
                var rr = RedRock();
                var w = PointAtDistance(bt, rr, S.welcomeDistance);
                if (w.HasValue) { var (pos, dir) = w.Value; var right = Vector3.Cross(Vector3.up, dir); if (Sign(Ground(pos + right * (bt.width * 0.5f + 2.2f)), -dir, S.welcomeText, 1.6f, "LEAVING\nRED ROCK")) { signs++; U(S.signPrefab); } }
                var a = PointAtDistance(bt, rr, S.arrowDistance);
                if (a.HasValue) { var (pos, dir) = a.Value; var right = Vector3.Cross(Vector3.up, dir); ArrowSign(Ground(pos + right * (bt.width * 0.5f + 1.8f)), dir, S.arrowText); signs++; }
                var bpos = PointAtDistance(bt, rr, ASHPreTownSettings.I.blockRadius + 12f);
                if (bpos.HasValue)
                {
                    var (pos, dir) = bpos.Value; var right = Vector3.Cross(Vector3.up, dir);
                    if (Place(S.blockerPrefab, Ground(pos - right * (bt.width * 0.5f + 1.4f)), Quaternion.LookRotation(right) * Quaternion.Euler(0f, 12f, 0f), "Old road blocker")) U(S.blockerPrefab);
                    for (int k = 0; k < 3; k++) if (Place(S.conePrefab, Ground(pos - right * (bt.width * 0.5f + 0.6f) + dir * (k * 2.2f - 2f)), Quaternion.Euler(0f, k * 47f, k == 2 ? 88f : 0f), "Cone")) U(S.conePrefab);
                }
            }

            // bridges (timber placeholder; the EasyRoads demo bridge meshes are not in this project)
            int spans = 0;
            if (S.timberPlaceholder) foreach (var p in plans) foreach (var b in p.bridges) { TimberSpan(b.pa, b.pb, p.width, p.seg.id); spans++; }

            return $"Dressing: {signs} signs, {fences} fence pieces, {cars} abandoned cars, {spans} timber bridge spans; props: {string.Join(", ", used.Select(kv => kv.Key + " x" + kv.Value))}.";
        }

        static bool OnAnyRoad(List<Plan> plans, Vector3 c, Plan self, float pad)
        {
            var q = new Vector2(c.x, c.z);
            foreach (var p in plans)
            {
                if (p == self) continue; var s = p.spline;
                for (int i = 0; i < s.Count - 1; i++) if (DistSeg(q, new Vector2(s[i].x, s[i].z), new Vector2(s[i + 1].x, s[i + 1].z)) < p.width * 0.5f + pad) return true;
            }
            return false;
        }

        static Vector3 RedRock() { var p = ASHPreTownSettings.I; var go = string.IsNullOrEmpty(p.redRockObject) ? null : GameObject.Find(p.redRockObject); return go ? go.transform.position : new Vector3(p.redRockXZ.x, 0f, p.redRockXZ.y); }
        /// <summary>First point along the road (from its start) within d of target (flat).</summary>
        static (Vector3, Vector3)? PointAtDistance(Plan p, Vector3 target, float d)
        {
            float L = Len(p); var tg = new Vector2(target.x, target.z);
            for (float s = 0f; s <= L; s += 1f) { var (pos, dir) = At(p, s); if (Vector2.Distance(new Vector2(pos.x, pos.z), tg) <= d) return (pos, dir); }
            return null;
        }

        static GameObject Sign(Vector3 at, Vector3 facing, string text, float scale, string backText)
        {
            if (!S.signPrefab) return null;
            var lb = LocalBounds(S.signPrefab);
            bool thinZ = lb.size.z <= lb.size.x; // board normal along local z (else x)
            var rot = Quaternion.LookRotation(facing) * (thinZ ? Quaternion.identity : Quaternion.Euler(0f, -90f, 0f));
            var go = Place(S.signPrefab, at, rot, "Sign - " + text.Replace("\n", " "));
            if (!go) return null;
            go.transform.localScale = Vector3.one * scale;
            float boardW = thinZ ? lb.size.x : lb.size.z, thick = thinZ ? lb.size.z : lb.size.x;
            var center = new Vector3(lb.center.x, Mathf.Lerp(lb.min.y, lb.max.y, S.signTextHeight), lb.center.z);
            Vector3 n = thinZ ? Vector3.forward : Vector3.right;
            AddText(go.transform, center - n * (thick * 0.5f + 0.012f), Quaternion.LookRotation(n), text, boardW * 0.8f, lb.size.y * 0.32f);
            AddText(go.transform, center + n * (thick * 0.5f + 0.012f), Quaternion.LookRotation(-n), string.IsNullOrEmpty(backText) ? text : backText, boardW * 0.8f, lb.size.y * 0.32f);
            return go;
        }

        static void AddText(Transform parent, Vector3 localPos, Quaternion localRot, string text, float w, float h)
        {
            var go = new GameObject("Text"); go.transform.SetParent(parent, false); go.transform.localPosition = localPos; go.transform.localRotation = localRot;
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.text = text; tmp.alignment = TMPro.TextAlignmentOptions.Center; tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.05f; tmp.fontSizeMax = 12f * S.signTextSize;
            tmp.color = new Color(0.9f, 0.87f, 0.78f); tmp.fontStyle = TMPro.FontStyles.Bold; tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(Mathf.Max(0.2f, w), Mathf.Max(0.1f, h));
        }

        static void ArrowSign(Vector3 at, Vector3 roadDir, string text)
        {
            var root = new GameObject("Sign - Arrow to " + text).transform; root.SetParent(_dress, false);
            // plank parallel to the road, turned 35 degrees toward oncoming traffic; the pointed end points toward town
            root.SetPositionAndRotation(at, Quaternion.LookRotation(roadDir) * Quaternion.Euler(0f, -35f, 0f));
            Cube(root, new Vector3(0f, 1.1f, 0f), Quaternion.identity, new Vector3(0.14f, 2.2f, 0.14f), "Post");
            Cube(root, new Vector3(0f, 1.95f, 0.55f), Quaternion.identity, new Vector3(0.06f, 0.42f, 1.7f), "Plank");
            Cube(root, new Vector3(0f, 1.95f, 1.4f), Quaternion.Euler(45f, 0f, 0f), new Vector3(0.055f, 0.3f, 0.3f), "Tip");
            AddText(root, new Vector3(0.04f, 1.95f, 0.5f), Quaternion.LookRotation(Vector3.left), text + "  >", 1.5f, 0.32f);
            AddText(root, new Vector3(-0.04f, 1.95f, 0.5f), Quaternion.LookRotation(Vector3.right), "<  " + text, 1.5f, 0.32f);
        }

        static GameObject Cube(Transform parent, Vector3 lp, Quaternion lr, Vector3 ls, string name, bool collider = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = name; g.transform.SetParent(parent, false);
            g.transform.localPosition = lp; g.transform.localRotation = lr; g.transform.localScale = ls;
            g.GetComponent<Renderer>().sharedMaterial = _wood; if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        static void TimberSpan(Vector3 a, Vector3 b, float width, string id)
        {
            var dir = b - a; dir.y = 0f; float L = dir.magnitude; if (L < 1f) return; dir /= L;
            var root = new GameObject("Bridge (timber placeholder) - " + id).transform; root.SetParent(_dress, false);
            root.SetPositionAndRotation(new Vector3((a.x + b.x) * 0.5f, a.y, (a.z + b.z) * 0.5f), Quaternion.LookRotation(dir));
            float hw = width * 0.5f;
            // stringers under the deck edges, cross beams, posts down to the creek bed, rails on both sides
            for (int s = -1; s <= 1; s += 2) Cube(root, new Vector3(s * (hw - 0.3f), -0.35f, 0f), Quaternion.identity, new Vector3(0.35f, 0.45f, L + 1f), "Stringer", false);
            for (float z = -L * 0.5f; z <= L * 0.5f + 0.01f; z += 2f) Cube(root, new Vector3(0f, -0.15f, z), Quaternion.identity, new Vector3(width + 0.6f, 0.18f, 0.28f), "Beam", false);
            int bents = Mathf.Max(1, Mathf.RoundToInt(L / 6f));
            for (int i = 1; i < bents; i++)
            {
                float z = -L * 0.5f + L * i / bents;
                for (int s = -1; s <= 1; s += 2)
                {
                    var wp = root.TransformPoint(new Vector3(s * (hw - 0.3f), 0f, z)); float gy = H(new Vector2(wp.x, wp.z)); float h = Mathf.Max(0.5f, a.y - 0.55f - gy + 0.6f);
                    Cube(root, new Vector3(s * (hw - 0.3f), -0.55f - h * 0.5f, z), Quaternion.identity, new Vector3(0.3f, h, 0.3f), "Pile");
                }
            }
            for (int s = -1; s <= 1; s += 2)
            {
                for (float z = -L * 0.5f; z <= L * 0.5f + 0.01f; z += 2.5f) Cube(root, new Vector3(s * (hw + 0.15f), 0.5f, z), Quaternion.identity, new Vector3(0.16f, 1.1f, 0.16f), "RailPost");
                Cube(root, new Vector3(s * (hw + 0.15f), 1.0f, 0f), Quaternion.identity, new Vector3(0.12f, 0.16f, L + 0.3f), "Rail");
                Cube(root, new Vector3(s * (hw + 0.15f), 0.55f, 0f), Quaternion.identity, new Vector3(0.1f, 0.12f, L + 0.3f), "MidRail", false);
            }
        }

        static string SnapBarricade(List<Plan> plans)
        {
            var bt = plans.FirstOrDefault(x => x.seg.kind == ASHRoadSettings.Kind.Blacktop); if (bt == null) return "Barricade: no blacktop.";
            var pts = ASHPreTownSettings.I; var r = PointAtDistance(bt, RedRock(), pts.blockRadius + 3f);
            if (!r.HasValue) return "Barricade: road never reaches the ring.";
            var (pos, _) = r.Value;
            Undo.RecordObject(pts, "ASH Roads barricade");
            pts.barricadeAuto = false; pts.barricadeXZ = new Vector2(pos.x, pos.z); EditorUtility.SetDirty(pts); AssetDatabase.SaveAssetIfDirty(pts);
            return $"Barricade moved onto the blacktop at {pos.x:F0}, {pos.z:F0} (ASH_PreTownSettings barricadeAuto off).";
        }
    }
}
