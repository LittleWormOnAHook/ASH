using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.Gameplay;
using Invector.vCharacterController;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Keep Play-mode tweaks of the player (opt-in, this editor session): with "ASH/Keep Player Play Changes" ticked, leaving Play
    /// writes back to ASH_Player.prefab (never the scene) the local poses of the held-item rig transforms (AimAnchor_/UseAnchor_,
    /// Grip_R/Grip_L/String_R/Stock) - never the Held_ items' own placements - plus the value settings (numbers, toggles, vectors, enums - no object
    /// references) of ASHWeaponAim, ASHLightAim, ASHChopAnimator, ASHHeldRig, ASHActionLayer and ASHEquipment. A component's
    /// context menu "Save Runtime Pose to Prefab" does the same once for that Play session. Logs everything it saved.
    /// Separate opt-in "ASH/Keep Held Item Placements From Play" (default off; context menu "Save Held Item Placements to Prefab"
    /// for one session): also writes the local position/rotation/scale of Held_ item objects - only the ones you moved yourself
    /// in Play: every Held_ item and everything under it (grips, String_R, Stock, child meshes) plus the AimAnchor_/UseAnchor_
    /// transforms are snapshotted at Play start and again once the rig has settled; at exit each one's authored local pose
    /// (ASHHeldRig's forced aim pose is undone) is compared with the snapshot and whatever differs beyond a small epsilon is
    /// saved. Transforms that moved by themselves between the two snapshots (runtime-driven) are left out.
    /// The prefab is backed up to Library/ASH_PrefabBackups before every write.
    /// </summary>
    [InitializeOnLoad]
    public static class ASHPlayModePersist
    {
        const string Menu = "ASH/Keep Player Play Changes";
        const string KeyOn = "ASH_KeepPlayChanges", KeyOnce = "ASH_KeepPlayChangesOnce", KeyData = "ASH_KeepPlayChangesData";
        const string MenuHeld = "ASH/Keep Held Item Placements From Play";
        const string KeyHeld = "ASH_KeepHeldPlacements", KeyHeldOnce = "ASH_KeepHeldPlacementsOnce", KeyHeldData = "ASH_KeepHeldPlacementsData";
        const string HeldPrefix = "Held_";
        const string PlayerPrefab = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        static readonly System.Type[] Types = { typeof(ASHWeaponAim), typeof(ASHLightAim), typeof(ASHChopAnimator), typeof(ASHHeldRig), typeof(ASHActionLayer), typeof(ASHEquipment) };
        static readonly string[] RigPrefixes = { ASHHeldRig.AimAnchorPrefix, ASHHeldRig.UseAnchorPrefix, ASHHeldRig.GripR, ASHHeldRig.GripL, ASHHeldRig.StringR, ASHHeldRig.Stock }; // Held_ item rest placements are hand-set in edit mode and never written back

        [System.Serializable] class TPose { public string path; public Vector3 p; public Quaternion r; public Vector3 s; }
        [System.Serializable] class Prop { public string comp, path, prop, type, value; }
        [System.Serializable] class Data { public List<TPose> poses = new List<TPose>(); public List<Prop> props = new List<Prop>(); }

        static ASHPlayModePersist() { EditorApplication.playModeStateChanged += OnPlayMode; }

        [MenuItem(Menu, priority = 210)] static void Toggle() => SessionState.SetBool(KeyOn, !SessionState.GetBool(KeyOn, false));
        [MenuItem(Menu, true)] static bool ToggleV() { UnityEditor.Menu.SetChecked(Menu, SessionState.GetBool(KeyOn, false)); return true; }
        public static bool KeepOn { get => SessionState.GetBool(KeyOn, false); set => SessionState.SetBool(KeyOn, value); }

        static void SaveOnce(MenuCommand c) { SessionState.SetBool(KeyOnce, true); Debug.Log("[ASH] KEEP: this Play session's player tweaks will be saved to the prefab when you leave Play"); }
        [MenuItem("CONTEXT/ASHHeldRig/Save Runtime Pose to Prefab")] static void S1(MenuCommand c) => SaveOnce(c);
        [MenuItem("CONTEXT/ASHWeaponAim/Save Runtime Pose to Prefab")] static void S2(MenuCommand c) => SaveOnce(c);
        [MenuItem("CONTEXT/ASHLightAim/Save Runtime Pose to Prefab")] static void S3(MenuCommand c) => SaveOnce(c);
        [MenuItem("CONTEXT/ASHChopAnimator/Save Runtime Pose to Prefab")] static void S4(MenuCommand c) => SaveOnce(c);

        // ---- Held_ item placements (separate opt-in) ----
        [MenuItem(MenuHeld, priority = 211)] static void ToggleHeld() => SessionState.SetBool(KeyHeld, !SessionState.GetBool(KeyHeld, false));
        [MenuItem(MenuHeld, true)] static bool ToggleHeldV() { UnityEditor.Menu.SetChecked(MenuHeld, SessionState.GetBool(KeyHeld, false)); return true; }
        public static bool KeepHeldOn { get => SessionState.GetBool(KeyHeld, false); set => SessionState.SetBool(KeyHeld, value); }
        static void SaveHeldOnce(MenuCommand c) { SessionState.SetBool(KeyHeldOnce, true); Debug.Log("[ASH] KEEP HELD: Held_ items you moved this Play session will be saved to the prefab when you leave Play"); }
        [MenuItem("CONTEXT/ASHHeldRig/Save Held Item Placements to Prefab")] static void H1(MenuCommand c) => SaveHeldOnce(c);
        [MenuItem("CONTEXT/ASHEquipment/Save Held Item Placements to Prefab")] static void H2(MenuCommand c) => SaveHeldOnce(c);
        [MenuItem("CONTEXT/Transform/Save Held Item Placements to Prefab")] static void H3(MenuCommand c) => SaveHeldOnce(c);
        [MenuItem("CONTEXT/Transform/Save Held Item Placements to Prefab", true)] static bool H3V(MenuCommand c) => EditorApplication.isPlaying && c.context is Transform t && t.name.StartsWith(HeldPrefix);

        static readonly Dictionary<string, TPose> _heldStart = new Dictionary<string, TPose>();
        static readonly HashSet<string> _heldRuntime = new HashSet<string>();
        static int _settleFrames = -1;
        const int SettleFrames = 90;

        static bool IsRigName(string n) => n.StartsWith(ASHHeldRig.AimAnchorPrefix) || n.StartsWith(ASHHeldRig.UseAnchorPrefix);
        /// <summary>Held_ items, everything under them, and the aim/use anchors; never runtime clones or runtime-made objects.</summary>
        static bool Tracked(Transform t, Transform root)
        {
            if (!t || !root || t == root || !t.IsChildOf(root)) return false;
            // the bow rig drives its Model (idle hang) and the draw-hand IK target every frame: never a hand edit, never saved
            // (saving the hung Model rotation made it the rest pose, which turned the aimed bow sideways)
            var bowRig = t.parent ? t.parent.GetComponent<ASH.Combat.ASHBowDrawRig>() : null;
            if (bowRig && (t.name == "Model" || t == bowRig.handTarget)) return false;
            for (var x = t; x && x != root; x = x.parent)
            {
                if (x.name.EndsWith("(Clone)") || x.name.StartsWith("ASH_NockArrow") || x.name.StartsWith("ASH_HandArrow")) return false;
                if (x.name.StartsWith(HeldPrefix)) return true;
            }
            return IsRigName(t.name);
        }
        static TPose Pose(Transform t, Transform root)
        {
            Vector3 p = t.localPosition; Quaternion r = t.localRotation;
            var rig = root.GetComponentInChildren<ASHHeldRig>(true); if (rig) rig.AuthoredLocal(t, ref p, ref r); // undo the rig's aim-time forcing
            return new TPose { path = AnimationUtility.CalculateTransformPath(t, root), p = p, r = r, s = t.localScale };
        }
        static bool Same(TPose a, TPose b) => (a.p - b.p).sqrMagnitude < 1e-8f && Quaternion.Angle(a.r, b.r) < 0.01f && (a.s - b.s).sqrMagnitude < 1e-8f;
        static Dictionary<string, TPose> SnapshotHeld()
        {
            var d = new Dictionary<string, TPose>(); var root = PlayerRoot(); if (!root) return d;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (Tracked(t, root)) { var p = Pose(t, root); d[p.path] = p; }
            return d;
        }
        static void SettleTick()
        {   // second snapshot after the rig has run: anything that moved on its own by now is runtime-driven, not a hand edit
            if (!EditorApplication.isPlaying) { EditorApplication.update -= SettleTick; _settleFrames = -1; return; }
            if (--_settleFrames > 0) return;
            EditorApplication.update -= SettleTick; _settleFrames = -1;
            foreach (var kv in SnapshotHeld())
                if (_heldStart.TryGetValue(kv.Key, out var st) && !Same(st, kv.Value)) { _heldRuntime.Add(kv.Key); }
            if (_heldRuntime.Count > 0) Debug.Log("[ASH] KEEP HELD: runtime-driven (never saved): " + string.Join(", ", _heldRuntime));
        }

        [System.Serializable] class HeldData { public List<TPose> poses = new List<TPose>(); }

        static void OnPlayMode(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode)
            {
                _heldStart.Clear(); _heldRuntime.Clear();
                foreach (var kv in SnapshotHeld()) _heldStart[kv.Key] = kv.Value;
                EditorApplication.update -= SettleTick; _settleFrames = SettleFrames; EditorApplication.update += SettleTick;
            }
            if (s == PlayModeStateChange.ExitingPlayMode && (KeepHeldOn || SessionState.GetBool(KeyHeldOnce, false)))
            {
                SessionState.SetBool(KeyHeldOnce, false);
                var hd = new HeldData(); var now = SnapshotHeld();
                foreach (var kv in now)
                    if (_heldStart.TryGetValue(kv.Key, out var st) && !_heldRuntime.Contains(kv.Key) && !Same(st, kv.Value)) hd.poses.Add(kv.Value);
                if (hd.poses.Count > 0) SessionState.SetString(KeyHeldData, JsonUtility.ToJson(hd));
                else Debug.Log($"[ASH] KEEP HELD: no Held_ item, grip or anchor differs from Play start ({now.Count} compared, {_heldStart.Count} at start)");
            }
            if (s == PlayModeStateChange.EnteredEditMode)
            {
                var hj = SessionState.GetString(KeyHeldData, ""); SessionState.EraseString(KeyHeldData);
                if (!string.IsNullOrEmpty(hj)) ApplyHeld(JsonUtility.FromJson<HeldData>(hj));
            }
            if (s == PlayModeStateChange.ExitingPlayMode && (KeepOn || SessionState.GetBool(KeyOnce, false)))
            {
                SessionState.SetBool(KeyOnce, false);
                var d = CaptureNow(); if (d != null) SessionState.SetString(KeyData, JsonUtility.ToJson(d));
            }
            else if (s == PlayModeStateChange.EnteredEditMode)
            {
                var json = SessionState.GetString(KeyData, ""); SessionState.EraseString(KeyData);
                if (!string.IsNullOrEmpty(json)) Apply(JsonUtility.FromJson<Data>(json));
            }
        }

        static Transform PlayerRoot() { var cc = Object.FindAnyObjectByType<vThirdPersonController>(); return cc ? cc.transform : null; }

        static Data CaptureNow()
        {
            var root = PlayerRoot(); if (!root) return null;
            var d = new Data();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && RigPrefixes.Any(px => t.name.StartsWith(px)) && !t.name.EndsWith("(Clone)") && t.name != "Held_HandArrow")
                    d.poses.Add(new TPose { path = AnimationUtility.CalculateTransformPath(t, root), p = t.localPosition, r = t.localRotation, s = t.localScale });
            foreach (var ty in Types)
                foreach (var c in root.GetComponentsInChildren(ty, true))
                {
                    string cp = AnimationUtility.CalculateTransformPath(c.transform, root);
                    var so = new SerializedObject(c); var it = so.GetIterator();
                    while (it.Next(true))
                    {
                        if (it.name == "m_Script" || it.propertyPath.StartsWith("m_")) continue;
                        string v = Get(it); if (v != null) d.props.Add(new Prop { comp = ty.Name, path = cp, prop = it.propertyPath, type = it.propertyType.ToString(), value = v });
                    }
                }
            return d;
        }

        static string Get(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: return p.floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.Integer: return p.intValue.ToString();
                case SerializedPropertyType.Boolean: return p.boolValue ? "1" : "0";
                case SerializedPropertyType.Enum: return p.enumValueIndex.ToString();
                case SerializedPropertyType.String: return p.stringValue;
                case SerializedPropertyType.Vector2: return JsonUtility.ToJson(new V4 { v = p.vector2Value });
                case SerializedPropertyType.Vector3: return JsonUtility.ToJson(new V4 { v = p.vector3Value });
                case SerializedPropertyType.Vector4: return JsonUtility.ToJson(new V4 { v = p.vector4Value });
                case SerializedPropertyType.Quaternion: var q = p.quaternionValue; return JsonUtility.ToJson(new V4 { v = new Vector4(q.x, q.y, q.z, q.w) });
                case SerializedPropertyType.Color: return JsonUtility.ToJson(new V4 { v = (Vector4)p.colorValue });
                default: return null;
            }
        }
        [System.Serializable] struct V4 { public Vector4 v; }

        static bool Set(SerializedProperty p, string type, string v)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture; V4 x;
            switch (type)
            {
                case "Float": var f = float.Parse(v, inv); if (Mathf.Approximately(p.floatValue, f)) return false; p.floatValue = f; return true;
                case "Integer": var i = int.Parse(v); if (p.intValue == i) return false; p.intValue = i; return true;
                case "Boolean": var b = v == "1"; if (p.boolValue == b) return false; p.boolValue = b; return true;
                case "Enum": var e = int.Parse(v); if (p.enumValueIndex == e) return false; p.enumValueIndex = e; return true;
                case "String": if (p.stringValue == v) return false; p.stringValue = v; return true;
                case "Vector2": x = JsonUtility.FromJson<V4>(v); if (p.vector2Value == (Vector2)x.v) return false; p.vector2Value = x.v; return true;
                case "Vector3": x = JsonUtility.FromJson<V4>(v); if (p.vector3Value == (Vector3)x.v) return false; p.vector3Value = x.v; return true;
                case "Vector4": x = JsonUtility.FromJson<V4>(v); if (p.vector4Value == x.v) return false; p.vector4Value = x.v; return true;
                case "Quaternion": x = JsonUtility.FromJson<V4>(v); var q = new Quaternion(x.v.x, x.v.y, x.v.z, x.v.w); if (p.quaternionValue == q) return false; p.quaternionValue = q; return true;
                case "Color": x = JsonUtility.FromJson<V4>(v); if (p.colorValue == (Color)x.v) return false; p.colorValue = x.v; return true;
            }
            return false;
        }

        static void Backup()
        {
            var dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Library/ASH_PrefabBackups"); System.IO.Directory.CreateDirectory(dir);
            var dst = System.IO.Path.Combine(dir, "ASH_Player_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".prefab");
            System.IO.File.Copy(PlayerPrefab, dst, true); Debug.Log($"[ASH] KEEP backup {dst}");
        }

        static void ApplyHeld(HeldData d)
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab); var sb = new StringBuilder(); int n = 0;
            try
            {
                foreach (var tp in d.poses)
                {
                    var t = root.transform.Find(tp.path); if (!t) { sb.Append($"  (missing in prefab) {tp.path}\n"); continue; }
                    if (t.localPosition == tp.p && t.localRotation == tp.r && t.localScale == tp.s) continue;
                    t.localPosition = tp.p; t.localRotation = tp.r; t.localScale = tp.s; n++; sb.Append($"  {tp.path} pos {tp.p:F4} rot {tp.r.eulerAngles:F2} scale {tp.s:F4}\n");
                }
                if (n > 0) { Backup(); PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log(n > 0 ? $"[ASH] KEEP HELD saved {n} Held_ placement(s) to {PlayerPrefab} (scene untouched):\n{sb}" : "[ASH] KEEP HELD: nothing to save\n" + sb);
        }

        static void Apply(Data d)
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab); var sb = new StringBuilder(); int n = 0;
            try
            {
                foreach (var tp in d.poses)
                {
                    var t = root.transform.Find(tp.path); if (!t) continue;
                    if (t.localPosition == tp.p && t.localRotation == tp.r && t.localScale == tp.s) continue;
                    t.localPosition = tp.p; t.localRotation = tp.r; t.localScale = tp.s; n++; sb.Append($"  transform {tp.path}\n");
                }
                foreach (var g in d.props.GroupBy(x => x.comp + "|" + x.path))
                {
                    var f = g.First(); var host = string.IsNullOrEmpty(f.path) ? root.transform : root.transform.Find(f.path); if (!host) continue;
                    var ty = Types.FirstOrDefault(x => x.Name == f.comp); var c = ty != null ? host.GetComponent(ty) : null; if (!c) continue;
                    var so = new SerializedObject(c); bool any = false;
                    foreach (var pr in g) { var sp = so.FindProperty(pr.prop); if (sp != null && Set(sp, pr.type, pr.value)) { any = true; n++; sb.Append($"  {f.comp}.{pr.prop} = {pr.value}\n"); } }
                    if (any) so.ApplyModifiedPropertiesWithoutUndo();
                }
                if (n > 0) { Backup(); PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log(n > 0 ? $"[ASH] KEEP saved {n} Play-mode change(s) to {PlayerPrefab} (scene untouched):\n{sb}" : "[ASH] KEEP: no player changes to save");
        }

        /// <summary>Harness: switch the session toggle from the bridge.</summary>
        public static void EnableKeep() { KeepOn = true; Debug.Log("[ASH] KEEP toggle=True"); }
        public static void DisableKeep() { KeepOn = false; Debug.Log("[ASH] KEEP toggle=False"); }
        public static void EnableKeepHeld() { KeepHeldOn = true; Debug.Log("[ASH] KEEP HELD toggle=True"); }
        public static void DisableKeepHeld() { KeepHeldOn = false; Debug.Log("[ASH] KEEP HELD toggle=False"); }
    }
}
