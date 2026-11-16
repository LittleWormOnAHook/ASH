using System.Collections.Generic;
using System.Linq;
using ASH.EditorTools.Systems;
using ASH.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Style = ASH.World.ASHRockKitSettings.Style;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// Edit-mode Rocks and Cliffs placer. Click in the Scene view to place, click-drag to draw a cliff,
    /// hold to brush rubble, Shift-drag to copy a reseeded variation. Ctrl+D / paste also reseeds
    /// (unless Lock Seed is on). Pieces come from ANGRY MESH and Quantum Theory prefabs (referenced, not vendored).
    /// Everything lives under ASH_PlacedRocks, is undoable, and the open scene is never saved.
    /// Newly placed pieces (and the wrapper/root) are marked fully static.
    /// </summary>
    public sealed class ASHRockCliffTool : EditorWindow
    {
        public const string SettingsPath = ASHMenuInstaller.ResDir + "/ASH_RockKitSettings.asset";
        public const string RootName = ASHPlacedRock.RootName;

        static bool _active = true;
        static bool _placeGuard;
        static bool _altSnapOff;
        Style _style = Style.Boulder;
        bool _lockSeed;
        float _scaleMul = 1f;
        bool _brush;
        Vector3 _cliffStart;
        bool _cliffDrag;
        ASHPlacedRock _dragCopy;
        Vector3 _lastBrush;
        bool _brushing;
        Vector2 _scroll;
        static bool _hooked;

        [MenuItem("ASH/World/Rocks and Cliffs/Open Tool", false, 50)]
        public static void Open() { _active = true; GetWindow<ASHRockCliffTool>("Rocks & Cliffs"); }

        [MenuItem("ASH/World/Rocks and Cliffs/Install Settings (scan packs)", false, 51)]
        public static void Install()
        {
            var s = EnsureSettings();
            int n = Rescan(s);
            EditorUtility.SetDirty(s); AssetDatabase.SaveAssets(); Selection.activeObject = s;
            Debug.Log($"[ASH] Rock kit settings: {SettingsPath} (boulders {Count(s.boulders)}, rubble {Count(s.rubble)}, cliffs {Count(s.cliffs)}, sharp {Count(s.sharp)}, scanned {n}). Open ASH/World/Rocks and Cliffs/Open Tool to place. Scene is not saved.");
        }

        [MenuItem("ASH/World/Rocks and Cliffs/Select Settings", false, 52)]
        public static void SelectSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHRockKitSettings>(SettingsPath);
            if (!s) { Install(); return; }
            Selection.activeObject = s; EditorGUIUtility.PingObject(s);
        }

        [MenuItem("ASH/World/Rocks and Cliffs/Preset/Relaxed", false, 60)] public static void P0() => SetPreset(ASHRockKitSettings.Preset.Relaxed);
        [MenuItem("ASH/World/Rocks and Cliffs/Preset/Default", false, 61)] public static void P1() => SetPreset(ASHRockKitSettings.Preset.Default);
        [MenuItem("ASH/World/Rocks and Cliffs/Preset/Extreme (stress test)", false, 62)] public static void P2() => SetPreset(ASHRockKitSettings.Preset.Extreme);

        [MenuItem("ASH/World/Rocks and Cliffs/Rebuild Selected", false, 70)]
        public static void RebuildSelected()
        {
            int n = 0;
            foreach (var r in Selection.GetFiltered<ASHPlacedRock>(SelectionMode.Editable)) { Rebuild(r); n++; }
            Debug.Log(n == 0 ? "[ASH] Select one or more tool-placed rocks first." : $"[ASH] Rebuilt {n} rock(s). Scene not saved.");
        }

        [MenuItem("ASH/World/Rocks and Cliffs/Clear Tool-Placed Rocks", false, 71)]
        public static void ClearPlaced()
        {
            var root = GameObject.Find(RootName);
            if (!root) { Debug.Log("[ASH] No ASH_PlacedRocks root in the scene."); return; }
            int n = root.transform.childCount;
            if (n == 0) { Debug.Log("[ASH] ASH_PlacedRocks is already empty."); return; }
            if (!EditorUtility.DisplayDialog("Clear tool-placed rocks",
                $"Delete {n} rock(s) under {RootName}? Hand-placed rocks outside this root are left alone. The scene will not be saved.",
                "Delete", "Cancel")) return;
            Undo.IncrementCurrentGroup();
            int g = Undo.GetCurrentGroup();
            for (int i = n - 1; i >= 0; i--) Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Undo.CollapseUndoOperations(g);
            Debug.Log($"[ASH] Cleared {n} tool-placed rock(s) under {RootName}. Scene not saved.");
        }

        static void SetPreset(ASHRockKitSettings.Preset p)
        {
            var s = EnsureSettings(); s.ApplyPreset(p); EditorUtility.SetDirty(s); AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] Rock kit preset {p}.");
        }

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            ObjectChangeEvents.changesPublished -= OnChanges;
            ObjectChangeEvents.changesPublished += OnChanges;
        }

        static void OnChanges(ref ObjectChangeEventStream stream)
        {
            if (_placeGuard || EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.CreateGameObjectHierarchy) continue;
                stream.GetCreateGameObjectHierarchyEvent(i, out var ev);
                var go = EditorUtility.EntityIdToObject(ev.entityId) as GameObject;
                if (!go) continue;
                foreach (var r in go.GetComponentsInChildren<ASHPlacedRock>())
                    HandleDuplicated(r);
            }
        }

        static void HandleDuplicated(ASHPlacedRock r)
        {
            if (!r || EditorUtility.IsPersistent(r)) return;
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(r.gameObject);
            if (stage != null) return;
            if (!r.lockSeed) r.seed = NewSeed();
            Rebuild(r);
            Snap(r, keepSink: true, force: true);
            EditorUtility.SetDirty(r);
        }

        public static ASHRockKitSettings EnsureSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHRockKitSettings>(SettingsPath);
            if (s) return s;
            s = ASHMenuInstaller.Ensure<ASHRockKitSettings>(SettingsPath);
            s.ApplyPreset(ASHRockKitSettings.Preset.Default);
            Rescan(s);
            EditorUtility.SetDirty(s); AssetDatabase.SaveAssets();
            return s;
        }

        public static int Rescan(ASHRockKitSettings s)
        {
            int n = 0;
            s.boulders = LoadPrefabs(s.boulderFolder, out int a); n += a;
            s.rubble = LoadPrefabs(s.rubbleFolder, out int b); n += b;
            if (s.rubble == null || s.rubble.Length == 0) s.rubble = s.boulders;
            s.cliffs = LoadPrefabs(s.cliffFolder, out int c); n += c;
            s.sharp = (s.cliffs ?? System.Array.Empty<GameObject>()).Where(p => p && p.name.ToLowerInvariant().Contains("cubic")).ToArray();
            if (s.sharp.Length == 0) s.sharp = s.cliffs;
            return n;
        }

        static GameObject[] LoadPrefabs(string folder, out int found)
        {
            found = 0;
            string path = "Assets/" + (folder ?? "").Trim().TrimStart('/');
            if (!AssetDatabase.IsValidFolder(path)) { Debug.LogWarning($"[ASH] Rock kit folder missing: {path} (vendor pack not imported?)"); return System.Array.Empty<GameObject>(); }
            var list = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { path }))
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (!p) continue;
                list.Add(p); found++;
            }
            return list.OrderBy(p => p.name).ToArray();
        }

        static int Count(GameObject[] a) => a == null ? 0 : a.Count(x => x);

        void OnEnable() { SceneView.duringSceneGui -= OnScene; SceneView.duringSceneGui += OnScene; }
        void OnDisable() { SceneView.duringSceneGui -= OnScene; }

        void OnGUI()
        {
            var s = EnsureSettings();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Rocks and Cliffs", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Toggle Placement on, then click in the Scene view.\n" +
                "• Click: place one (or a cluster, depending on style)\n" +
                "• Click-drag: draw a cliff wall (Cliff style, or hold Ctrl)\n" +
                "• Hold LMB with Brush: scatter along the stroke\n" +
                "• Shift-drag a placed rock: copy a new variation, snapped to the ground\n" +
                "• Alt while moving: don't re-snap\n" +
                "• Ctrl+D / paste: new seed (unless Lock Seed)\n" +
                "All new rocks go under ASH_PlacedRocks and are undoable. The scene is not saved. Hand-placed rocks outside that root are left alone.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            _active = EditorGUILayout.ToggleLeft("Placement tool", _active);
            _style = (Style)EditorGUILayout.EnumPopup("Style", _style);
            _lockSeed = EditorGUILayout.Toggle("Lock seed on next place / copies", _lockSeed);
            _scaleMul = EditorGUILayout.Slider("Scale multiplier", _scaleMul, 0.1f, 8f);
            _brush = EditorGUILayout.Toggle(new GUIContent("Brush (hold LMB)", "Paint clusters along a stroke. Best with Rubble or Boulder."), _brush);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Kit: {Count(s.boulders)} boulders, {Count(s.rubble)} rubble, {Count(s.cliffs)} cliffs, {Count(s.sharp)} sharp");
            if (Count(s.boulders) + Count(s.cliffs) == 0)
                EditorGUILayout.HelpBox("No vendor prefabs scanned. Import ANGRY MESH and/or Quantum Theory, then click Rescan Packs.", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan Packs")) { int n = Rescan(s); EditorUtility.SetDirty(s); AssetDatabase.SaveAssets(); Debug.Log($"[ASH] Rescanned rock packs: {n} prefabs."); }
            if (GUILayout.Button("Settings")) SelectSettings();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild Selected")) RebuildSelected();
            if (GUILayout.Button("Clear Tool-Placed")) ClearPlaced();
            EditorGUILayout.EndHorizontal();

            var sel = Selection.GetFiltered<ASHPlacedRock>(SelectionMode.Editable);
            if (sel.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Selected: {sel.Length}  ({sel[0].style} seed {sel[0].seed}" + (sel[0].lockSeed ? ", locked" : "") + (sel[0].overlapWarning ? ", overlap!" : "") + ")");
                if (GUILayout.Button("Toggle Lock Seed on Selected"))
                {
                    foreach (var r in sel) { Undo.RecordObject(r, "Lock seed"); r.lockSeed = !r.lockSeed; EditorUtility.SetDirty(r); }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void OnScene(SceneView sv)
        {
            _altSnapOff = Event.current.alt;
            if (!_active || EditorApplication.isPlaying) return;
            var e = Event.current;
            if (e == null) return;
            if (e.alt) return; // leave navigation / no-snap moves alone

            int id = GUIUtility.GetControlID(FocusType.Passive);
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            bool hit = GroundRay(ray, null, out var gh);

            if (hit && (e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.Repaint))
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                Handles.color = new Color(0.85f, 0.75f, 0.45f, 0.9f);
                Handles.DrawSolidDisc(gh.point, gh.normal, 0.35f);
                Handles.ArrowHandleCap(0, gh.point, Quaternion.LookRotation(gh.normal), 1.2f, EventType.Repaint);
                if (_cliffDrag) Handles.DrawLine(_cliffStart, gh.point, 3f);
                sv.Repaint();
            }

            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (!hit) return;
                GUIUtility.hotControl = id;
                if (e.shift)
                {
                    var pick = HandleUtility.PickGameObject(e.mousePosition, false);
                    var src = pick ? pick.GetComponentInParent<ASHPlacedRock>() : null;
                    if (src)
                    {
                        _dragCopy = DuplicateAt(src, gh.point, gh.normal);
                        e.Use(); return;
                    }
                }
                bool cliff = _style == Style.Cliff || e.control;
                if (cliff && !_brush)
                {
                    _cliffDrag = true; _cliffStart = gh.point;
                }
                else if (_brush)
                {
                    _brushing = true; _lastBrush = gh.point;
                    Place(_style, gh.point, gh.normal, Vector3.zero, false);
                }
                else Place(_style, gh.point, gh.normal, Vector3.zero, false);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && GUIUtility.hotControl == id)
            {
                if (!hit) return;
                if (_dragCopy)
                {
                    Undo.RecordObject(_dragCopy.transform, "Move rock copy");
                    _dragCopy.transform.position = gh.point;
                    Snap(_dragCopy, keepSink: true, force: true);
                }
                else if (_cliffDrag)
                {
                    // preview only (line already drawn)
                }
                else if (_brushing)
                {
                    var s = EnsureSettings();
                    if ((gh.point - _lastBrush).magnitude >= Mathf.Max(0.25f, s.brushSpacing))
                    {
                        Place(_style, gh.point, gh.normal, Vector3.zero, false);
                        _lastBrush = gh.point;
                    }
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == id)
            {
                if (_cliffDrag && hit)
                {
                    float len = Vector3.Distance(_cliffStart, gh.point);
                    if (len > 0.75f) Place(Style.Cliff, _cliffStart, gh.normal, gh.point, true);
                    else Place(_style == Style.Cliff ? Style.Cliff : _style, gh.point, gh.normal, Vector3.zero, false);
                }
                _cliffDrag = false; _brushing = false; _dragCopy = null;
                if (GUIUtility.hotControl == id) GUIUtility.hotControl = 0;
                e.Use();
            }
        }

        ASHPlacedRock DuplicateAt(ASHPlacedRock src, Vector3 pos, Vector3 normal)
        {
            _placeGuard = true;
            try
            {
                var clone = Instantiate(src.gameObject, pos, src.transform.rotation, EnsureRoot());
                Undo.RegisterCreatedObjectUndo(clone, "Copy rock");
                clone.name = src.gameObject.name;
                var r = clone.GetComponent<ASHPlacedRock>();
                if (!r.lockSeed && !_lockSeed) r.seed = NewSeed();
                r.lockSeed = _lockSeed || r.lockSeed;
                r.hasCliffEnd = false;
                Rebuild(r);
                Snap(r, keepSink: true, force: true);
                ASHMeshColliderTool.MarkStaticRecursive(clone);
                Selection.activeGameObject = clone;
                return r;
            }
            finally { _placeGuard = false; }
        }

        void Place(Style style, Vector3 pos, Vector3 normal, Vector3 cliffEnd, bool hasEnd)
        {
            var s = EnsureSettings();
            if (Count(s.boulders) + Count(s.cliffs) + Count(s.rubble) == 0)
            {
                Debug.LogWarning("[ASH] No rock prefabs in the kit. Run ASH/World/Rocks and Cliffs/Install Settings after importing the vendor packs.");
                return;
            }
            float slope = Vector3.Angle(normal, Vector3.up);
            if ((style == Style.Boulder || style == Style.BoulderPile || style == Style.Rubble)
                && s.skipBoulderAboveDegrees > 0.1f && slope > s.skipBoulderAboveDegrees)
            {
                Debug.Log($"[ASH] Skipped {style} on {slope:F0}° slope (limit {s.skipBoulderAboveDegrees}°). Use Cliff or Spire, or raise Skip Boulder Above.");
                return;
            }
            _placeGuard = true;
            try
            {
                Undo.IncrementCurrentGroup();
                int g = Undo.GetCurrentGroup();
                var go = new GameObject($"ASH_{style}_{NewSeed() % 100000:D5}");
                Undo.RegisterCreatedObjectUndo(go, "Place rock");
                go.transform.SetParent(EnsureRoot(), true);
                go.transform.position = pos;
                var r = Undo.AddComponent<ASHPlacedRock>(go);
                r.style = style;
                r.seed = NewSeed();
                r.lockSeed = _lockSeed;
                r.scaleMul = _scaleMul;
                r.yaw = 0f;
                r.hasCliffEnd = hasEnd;
                r.cliffEnd = cliffEnd;
                Rebuild(r);
                Snap(r, keepSink: false, force: true);
                ASHMeshColliderTool.MarkStaticRecursive(go);
                Selection.activeGameObject = go;
                EditorSceneManager.MarkSceneDirty(go.scene);
                Undo.CollapseUndoOperations(g);
            }
            finally { _placeGuard = false; }
        }

        public static Transform EnsureRoot()
        {
            var existing = GameObject.Find(RootName);
            if (existing)
            {
                ASHMeshColliderTool.MarkStaticRecursive(existing);
                return existing.transform;
            }
            var go = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(go, "ASH placed rocks root");
            ASHMeshColliderTool.MarkStaticRecursive(go);
            EditorSceneManager.MarkSceneDirty(go.scene);
            return go.transform;
        }

        static int NewSeed() => System.Guid.NewGuid().GetHashCode() & 0x7fffffff;

        public static void Rebuild(ASHPlacedRock r)
        {
            if (!r) return;
            var s = EnsureSettings();
            var rng = new System.Random(r.seed == 0 ? 1 : r.seed);
            _placeGuard = true;
            try
            {
                for (int i = r.transform.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(r.transform.GetChild(i).gameObject);

                var kit = s.KitFor(r.style);
                if (kit == null || kit.All(p => !p))
                {
                    Debug.LogWarning($"[ASH] No prefabs for style {r.style}.");
                    return;
                }

                float yaw = r.yaw;
                if (Mathf.Approximately(yaw, 0f)) yaw = (float)(rng.NextDouble() * 2.0 * s.yawJitter - s.yawJitter);
                r.yaw = yaw;
                var yawRot = Quaternion.Euler(0f, yaw, 0f);
                r.transform.rotation = yawRot;

                switch (r.style)
                {
                    case Style.Boulder:
                    case Style.Sharp:
                        Spawn(r, Pick(kit, rng), Vector3.zero, Quaternion.Euler(0f, Jitter(rng, 20f), 0f), Rand(rng, ScaleRange(s, r.style)) * r.scaleMul);
                        break;
                    case Style.BoulderPile:
                        int pn = rng.Next(s.pileCountMin, s.pileCountMax + 1);
                        for (int i = 0; i < pn; i++)
                        {
                            var o = i == 0 ? Vector3.zero : yawRot * new Vector3(Jitter(rng, s.pileRadius), 0f, Jitter(rng, s.pileRadius));
                            Spawn(r, Pick(First(s.boulders, kit), rng), o, Quaternion.Euler(0f, Jitter(rng, 180f), 0f), Rand(rng, s.pileScaleMin, s.pileScaleMax) * r.scaleMul);
                        }
                        break;
                    case Style.Outcrop:
                        int on = rng.Next(s.outcropCountMin, s.outcropCountMax + 1);
                        var axis = yawRot * Vector3.right;
                        for (int i = 0; i < on; i++)
                        {
                            float t = on == 1 ? 0f : (i / (float)(on - 1) - 0.5f);
                            var o = axis * t * s.outcropLength + Vector3.forward * Jitter(rng, 0.6f) + Vector3.up * (i == 0 ? 0f : 0.05f);
                            Spawn(r, Pick(First(s.cliffs, kit), rng), o, Quaternion.Euler(0f, Jitter(rng, 18f), 0f), Rand(rng, s.outcropScaleMin, s.outcropScaleMax) * r.scaleMul);
                        }
                        break;
                    case Style.Rubble:
                        int rn = rng.Next(s.rubbleCountMin, s.rubbleCountMax + 1);
                        for (int i = 0; i < rn; i++)
                        {
                            var o = yawRot * new Vector3(Jitter(rng, s.rubbleRadius), 0f, Jitter(rng, s.rubbleRadius));
                            Spawn(r, Pick(First(s.rubble, kit), rng), o, Quaternion.Euler(0f, Jitter(rng, 180f), 0f), Rand(rng, s.rubbleScaleMin, s.rubbleScaleMax) * r.scaleMul);
                        }
                        break;
                    case Style.Spire:
                        int sn = rng.Next(s.spireCountMin, s.spireCountMax + 1);
                        float y = 0f;
                        for (int i = 0; i < sn; i++)
                        {
                            float sc = Rand(rng, s.spireScaleMin, s.spireScaleMax) * r.scaleMul * Mathf.Lerp(1f, 0.45f, i / (float)Mathf.Max(1, sn - 1));
                            var o = new Vector3(Jitter(rng, 0.25f), y, Jitter(rng, 0.25f));
                            var piece = Spawn(r, Pick(First(s.cliffs, kit), rng), o, Quaternion.Euler(0f, Jitter(rng, 25f), 0f), sc);
                            y += ApproximateHeight(piece) * 0.55f;
                        }
                        break;
                    case Style.Cliff:
                        BuildCliff(r, s, kit, rng);
                        break;
                }

                if (r.style == Style.BoulderPile || r.style == Style.Rubble || r.style == Style.Outcrop || r.style == Style.Cliff)
                    SeatChildren(r, s);
                EditorUtility.SetDirty(r);
            }
            finally { _placeGuard = false; }
        }

        static void BuildCliff(ASHPlacedRock r, ASHRockKitSettings s, GameObject[] kit, System.Random rng)
        {
            Vector3 start = r.transform.position;
            Vector3 end = r.hasCliffEnd ? r.cliffEnd : start + r.transform.right * Mathf.Max(4f, s.outcropLength);
            Vector3 delta = end - start; delta.y = 0f;
            float len = Mathf.Max(0.5f, delta.magnitude);
            Vector3 dir = delta.normalized;
            Vector3 face = Vector3.Cross(Vector3.up, dir).normalized;
            if (face.sqrMagnitude < 0.01f) face = r.transform.forward;
            int n = Mathf.Clamp(Mathf.RoundToInt(len * s.cliffPiecesPerMetre), s.cliffCountMin, s.cliffCountMax);
            n = Mathf.Max(1, n);
            var use = First(s.cliffs, kit);
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : i / (float)(n - 1);
                var along = dir * (t * len) + face * Jitter(rng, 0.35f);
                // local space of the wrapper, which sits at start
                var local = r.transform.InverseTransformVector(along);
                var rot = Quaternion.LookRotation(face, Vector3.up) * Quaternion.Euler(0f, Jitter(rng, 12f), 0f);
                var localRot = Quaternion.Inverse(r.transform.rotation) * rot;
                Spawn(r, Pick(use, rng), local, localRot, Rand(rng, s.cliffScaleMin, s.cliffScaleMax) * r.scaleMul);
            }
            r.hasCliffEnd = true;
            r.cliffEnd = start + dir * len;
        }

        static GameObject Spawn(ASHPlacedRock r, GameObject prefab, Vector3 localPos, Quaternion localRot, float scale)
        {
            if (!prefab) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, r.transform);
            if (!go) go = (GameObject)Object.Instantiate(prefab, r.transform);
            Undo.RegisterCreatedObjectUndo(go, "Place rock piece");
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            go.name = prefab.name;
            ASHMeshColliderTool.MarkStaticRecursive(go);
            return go;
        }

        static void SeatChildren(ASHPlacedRock r, ASHRockKitSettings s)
        {
            // Drop each piece onto the ground under it so piles and rubble follow bumps, then keep their relative layout.
            for (int i = 0; i < r.transform.childCount; i++)
            {
                var c = r.transform.GetChild(i);
                var world = c.position + Vector3.up * 6f;
                if (!GroundRay(new Ray(world, Vector3.down), r.transform, out var hit)) continue;
                c.position = hit.point; // temporary; Snap() will sink the wrapper as a whole
            }
        }

        public static void Snap(ASHPlacedRock r, bool keepSink, bool force, bool recordUndo = true)
        {
            if (!r) return;
            if (!force && Event.current != null && Event.current.alt) return;
            var s = EnsureSettings();
            var origin = r.transform.position + Vector3.up * 8f;
            if (!GroundRay(new Ray(origin, Vector3.down), r.transform, out var hit))
            {
                var ter = Terrain.activeTerrain;
                if (!ter) return;
                var p = r.transform.position;
                p.y = ter.SampleHeight(p) + ter.transform.position.y;
                hit = new RaycastHit { point = p, normal = ter.terrainData ? ter.terrainData.GetInterpolatedNormal(
                    (p.x - ter.transform.position.x) / ter.terrainData.size.x,
                    (p.z - ter.transform.position.z) / ter.terrainData.size.z) : Vector3.up };
            }

            float slope = Vector3.Angle(hit.normal, Vector3.up);
            float sink = keepSink && r.sinkMetres > 0f ? r.sinkMetres : Mathf.Clamp(s.baseSinkMetres + s.sinkPerDegree * slope, 0f, s.maxSinkMetres);
            r.sinkMetres = sink;

            var tiltN = Vector3.Slerp(Vector3.up, hit.normal, Mathf.Clamp01(s.tiltStrength));
            float tilt = Vector3.Angle(Vector3.up, tiltN);
            if (tilt > s.maxTiltDegrees) tiltN = Vector3.Slerp(Vector3.up, hit.normal, s.maxTiltDegrees / Mathf.Max(0.01f, Vector3.Angle(Vector3.up, hit.normal)));
            var yaw = Quaternion.Euler(0f, r.yaw, 0f);
            var rot = Quaternion.FromToRotation(Vector3.up, tiltN) * yaw;

            if (recordUndo) { Undo.RecordObject(r.transform, "Snap rock"); Undo.RecordObject(r, "Snap rock"); }
            r.transform.SetPositionAndRotation(hit.point - tiltN.normalized * sink, rot);
            if (s.avoidOverlap) ResolveOverlap(r, s);
            EditorUtility.SetDirty(r);
            EditorUtility.SetDirty(r.transform);
            EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
        }

        static void ResolveOverlap(ASHPlacedRock r, ASHRockKitSettings s)
        {
            r.overlapWarning = false;
            var bounds = r.RenderBounds();
            for (int t = 0; t < Mathf.Max(1, s.overlapTries); t++)
            {
                var hits = Physics.OverlapBox(bounds.center, bounds.extents * 0.92f, r.transform.rotation, s.snapMask, QueryTriggerInteraction.Ignore);
                Vector3 push = Vector3.zero; int n = 0;
                foreach (var h in hits)
                {
                    if (!h || h.transform == r.transform || h.transform.IsChildOf(r.transform)) continue;
                    if (h.GetComponentInParent<Terrain>()) continue;
                    var other = h.GetComponentInParent<ASHPlacedRock>();
                    Vector3 away = r.transform.position - (other ? other.transform.position : h.bounds.center);
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.0001f) away = new Vector3(0.4f, 0f, 0.3f);
                    push += away.normalized;
                    n++;
                }
                if (n == 0) { r.overlapWarning = false; return; }
                r.transform.position += push.normalized * s.overlapPushMetres;
                if (GroundRay(new Ray(r.transform.position + Vector3.up * 8f, Vector3.down), r.transform, out var hit))
                    r.transform.position = hit.point - r.transform.up * r.sinkMetres;
                bounds = r.RenderBounds();
            }
            r.overlapWarning = true;
            Debug.LogWarning($"[ASH] {r.name} still overlaps something. It is marked (yellow wire) so you can move it. Scene not saved.");
        }

        static bool GroundRay(Ray ray, Transform ignore, out RaycastHit hit)
        {
            hit = default;
            var s = AssetDatabase.LoadAssetAtPath<ASHRockKitSettings>(SettingsPath);
            int mask = s ? s.snapMask.value : ~0;
            bool props = !s || s.snapToProps;
            var hits = Physics.RaycastAll(ray, 400f, mask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (!h.collider) continue;
                if (ignore && (h.transform == ignore || h.transform.IsChildOf(ignore) || ignore.IsChildOf(h.transform))) continue;
                if (!props && !h.collider.GetComponentInParent<Terrain>()) continue;
                hit = h; return true;
            }
            var ter = Terrain.activeTerrain;
            if (!ter || !ter.terrainData) return false;
            var plane = new Plane(Vector3.up, new Vector3(0f, ter.transform.position.y, 0f));
            // Walk the ray against the height field in a few steps, then SampleHeight.
            if (!plane.Raycast(ray, out float enter) && ray.origin.y < ter.transform.position.y + ter.terrainData.size.y)
                return false;
            var p = ray.GetPoint(Mathf.Max(0f, enter));
            p.y = ter.SampleHeight(p) + ter.transform.position.y;
            var size = ter.terrainData.size;
            var n = ter.terrainData.GetInterpolatedNormal(
                Mathf.InverseLerp(ter.transform.position.x, ter.transform.position.x + size.x, p.x),
                Mathf.InverseLerp(ter.transform.position.z, ter.transform.position.z + size.z, p.z));
            hit = new RaycastHit { point = p, normal = n, distance = Vector3.Distance(ray.origin, p) };
            return true;
        }

        static GameObject Pick(GameObject[] kit, System.Random rng)
        {
            if (kit == null || kit.Length == 0) return null;
            for (int tries = 0; tries < 8; tries++)
            {
                var p = kit[rng.Next(kit.Length)];
                if (p) return p;
            }
            return kit.FirstOrDefault(p => p);
        }

        static GameObject[] First(GameObject[] preferred, GameObject[] fallback)
        {
            if (preferred != null) { for (int i = 0; i < preferred.Length; i++) if (preferred[i]) return preferred; }
            return fallback;
        }

        static (float min, float max) ScaleRange(ASHRockKitSettings s, Style style)
        {
            switch (style)
            {
                case Style.Sharp: return (s.boulderScaleMin, s.boulderScaleMax);
                default: return (s.boulderScaleMin, s.boulderScaleMax);
            }
        }

        static float Rand(System.Random rng, (float min, float max) r) => Rand(rng, r.min, r.max);
        static float Rand(System.Random rng, float min, float max)
        {
            if (max < min) { var t = min; min = max; max = t; }
            return min + (float)rng.NextDouble() * (max - min);
        }
        static float Jitter(System.Random rng, float range) => (float)(rng.NextDouble() * 2.0 - 1.0) * range;

        static float ApproximateHeight(GameObject go)
        {
            if (!go) return 1f;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return go.transform.localScale.y;
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return Mathf.Max(0.2f, b.size.y);
        }
    }

    [CustomEditor(typeof(ASHPlacedRock))]
    public sealed class ASHPlacedRockEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var r = (ASHPlacedRock)target;
            EditorGUILayout.Space();
            if (GUILayout.Button("Rebuild")) { ASHRockCliffTool.Rebuild(r); ASHRockCliffTool.Snap(r, keepSink: true, force: true); }
            if (GUILayout.Button("Snap to Ground")) ASHRockCliffTool.Snap(r, keepSink: true, force: true);
            if (GUILayout.Button(r.lockSeed ? "Unlock Seed" : "Lock Seed")) { Undo.RecordObject(r, "Lock seed"); r.lockSeed = !r.lockSeed; EditorUtility.SetDirty(r); }
        }

        void OnSceneGUI()
        {
            var r = (ASHPlacedRock)target;
            if (!r || Event.current == null || Event.current.alt) return;
            if (Tools.current != Tool.Move && Tools.current != Tool.Transform) return;
            if (!r.transform.hasChanged) return;
            ASHRockCliffTool.Snap(r, keepSink: true, force: false, recordUndo: Event.current.type == EventType.MouseUp);
            r.transform.hasChanged = false;
        }
    }
}
