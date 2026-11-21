using ASH.Combat;
using Invector.vShooter;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Runtime bow tuning (t335). ASH/Systems/Combat/Bow Tuning Window, usable while aiming in Play:
    /// - Aim pose: Invector's ASH_IK_Bow left-hand offset for StandingAiming / CrouchingAiming (the IK solve moves the whole bow
    ///   arm, so the bow, arrow, string hand and anchor stay attached). This is an asset, so edits made in Play are KEPT.
    ///   Same data as Invector/Shooter/Open IK Adjust Window (gizmo handles), here as numbers and nudge buttons.
    /// - Draw rig (ASHBowDrawRig on Held_WoodenBow): draw length, anchor, nock / rest offsets, bow scale. Scene values reset
    ///   after Play, so "Save Bow Tuning" (also the rig's component menu) writes them to ASH_Player.prefab (when Play ends).
    /// </summary>
    [InitializeOnLoad]
    public sealed class ASHBowTuning : EditorWindow
    {
        const string IKPath = "Assets/_ASH/Data/Combat/IK/ASH_IK_Bow.asset";
        const string PlayerPrefab = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        const string PendingKey = "ASH_BowTuningPending";

        static ASHBowTuning()
        {
            ASHBowDrawRig.SaveTuningHook = Save;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) ApplyPending(); };
            EditorApplication.update += PausedHelperPreview;
        }

        // ---- Right Hand Helper (t353) ----
        static ASHBowDrawRig LiveRig() => Object.FindAnyObjectByType<ASHBowDrawRig>(FindObjectsInactive.Exclude);
        [MenuItem("ASH/Systems/Combat/Select Right Hand Helper")]
        public static void SelectHelper()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[ASH] Right Hand Helper: enter Play and equip the bow first."); return; }
            var rig = LiveRig(); if (!rig) { Debug.LogWarning("[ASH] Right Hand Helper: no active ASHBowDrawRig (equip the bow)."); return; }
            var h = rig.EnsureHelper(); if (!h) { Debug.LogWarning("[ASH] Right Hand Helper: the rig has no hand IK target."); return; }
            rig.useRightHandHelper = true; Selection.activeTransform = h; EditorGUIUtility.PingObject(h);
            if (SceneView.lastActiveSceneView) { SceneView.lastActiveSceneView.Frame(new Bounds(h.position, Vector3.one * 0.6f), false); }
            Tools.current = Tool.Move;
            Debug.Log("[ASH] Right Hand Helper selected: move / rotate it in the Scene view (live while paused).");
        }
        public static void ResetHelper(ASHBowDrawRig rig) { if (rig && rig.EnsureHelper(true)) { rig.helperTransform.hasChanged = true; Debug.Log("[ASH] Right Hand Helper reset to the rig's current hand pose."); } }
        public static void SaveHelper(ASHBowDrawRig rig)
        {
            if (!rig || !rig.helperTransform) { Debug.LogWarning("[ASH] Right Hand Helper: nothing to save."); return; }
            rig.BakeHelper(); Save(rig);
            Debug.Log($"[ASH] Right Hand Helper baked: anchorOffsetLocal {rig.anchorOffsetLocal} handLineSide {rig.handLineSide:F3} handRot {rig.handRotLocal.eulerAngles}");
        }
        static void PausedHelperPreview()
        {
            if (!Application.isPlaying || !EditorApplication.isPaused) return;
            var rig = LiveRig(); if (!rig || !rig.useRightHandHelper || !rig.helperTransform || !rig.helperTransform.hasChanged) return;
            rig.helperTransform.hasChanged = false;
            rig.PreviewArm(rig.GetComponentInParent<Animator>());
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        [MenuItem("ASH/Systems/Combat/Bow Tuning Window")] static void Open() => GetWindow<ASHBowTuning>("Bow Tuning");

        // Nudge directions (camera view while aiming) in IK-offset axes: measured by ASHT336Test's bow probe.
        public static Vector3 LeftAxis = new Vector3(-1f, 0f, 0f), CloserAxis = new Vector3(0f, 0f, -1f), UpAxis = new Vector3(0f, 1f, 0f);
        public static Vector3 YawRightAxis = new Vector3(0f, 1f, 0f);

        Vector2 _scroll; float _step = 0.01f, _degStep = 1f;

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var ik = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjust>(IKPath);
            EditorGUILayout.HelpBox("Aim the bow in Play and tweak. Aim pose values are saved in the IK asset immediately (kept after Play). Draw rig values need Save Bow Tuning.", MessageType.Info);
            _step = EditorGUILayout.Slider("Nudge (m)", _step, 0.001f, 0.05f); _degStep = EditorGUILayout.Slider("Nudge (deg)", _degStep, 0.1f, 10f);
            if (ik) { Adjust(ik, "StandingAiming"); Adjust(ik, "CrouchingAiming"); }
            else EditorGUILayout.HelpBox(IKPath + " missing", MessageType.Warning);

            EditorGUILayout.Space(); EditorGUILayout.LabelField("Draw rig (Held_WoodenBow)", EditorStyles.boldLabel);
            var rig = Object.FindAnyObjectByType<ASHBowDrawRig>(FindObjectsInactive.Include);
            if (!rig) EditorGUILayout.HelpBox("No ASHBowDrawRig in the open scene (equip the bow).", MessageType.None);
            else
            {
                var so = new SerializedObject(rig); so.Update();
                foreach (var n in new[] { "bowScale", "drawLength", "anchorIn", "anchorOffsetLocal", "handOnArrowLine", "handLineSide", "nockOffsetLocal", "restOffsetLocal", "modelRestEuler" })
                { var p = so.FindProperty(n); if (p != null) EditorGUILayout.PropertyField(p, true); }
                so.ApplyModifiedProperties();
                if (GUILayout.Button(Application.isPlaying ? "Save Bow Tuning (written to ASH_Player.prefab when Play ends)" : "Save Bow Tuning to ASH_Player.prefab")) Save(rig);
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Right Hand Helper (draw hand, full draw)", EditorStyles.boldLabel);
            var lr = Application.isPlaying ? LiveRig() : null;
            if (!lr) EditorGUILayout.HelpBox("Play, equip the bow and aim/draw, then pause. The helper is a handle on the bow you move/rotate in the Scene view; the hand follows live while paused.", MessageType.None);
            else
            {
                bool on = EditorGUILayout.Toggle("Use helper (hand follows it)", lr.useRightHandHelper);
                if (on != lr.useRightHandHelper) { lr.useRightHandHelper = on; if (on) lr.EnsureHelper(); if (lr.helperTransform) lr.helperTransform.hasChanged = true; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select Right Hand Helper")) SelectHelper();
                    if (GUILayout.Button("Reset")) ResetHelper(lr);
                    if (GUILayout.Button("Save Right Hand Helper")) SaveHelper(lr);
                }
                if (lr.helperTransform) EditorGUILayout.LabelField($"Helper local pos {lr.helperTransform.localPosition.ToString("F3")} rot {lr.helperTransform.localEulerAngles.ToString("F1")}");
            }
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Bow aim camera and body (ASHWeaponAim)", EditorStyles.boldLabel);
            var wa = Object.FindAnyObjectByType<ASH.Gameplay.ASHWeaponAim>(FindObjectsInactive.Include);
            if (!wa) EditorGUILayout.HelpBox("No ASHWeaponAim in the open scene.", MessageType.None);
            else
            {
                var so = new SerializedObject(wa); so.Update();
                foreach (var n in AimFields) { var p = so.FindProperty(n); if (p != null) EditorGUILayout.PropertyField(p, true); }
                so.ApplyModifiedProperties();
                if (Application.isPlaying) EditorGUILayout.LabelField($"Arrow line vs crosshair: {wa.BowCrosshairErrorBefore:F1} -> {wa.BowCrosshairErrorAfter:F1} deg   lean: {wa.BowLeanBefore:F0} -> {wa.BowLeanAfter:F0} deg");
                if (GUILayout.Button(Application.isPlaying ? "Save Aim Camera/Body (written to ASH_Player.prefab when Play ends)" : "Save Aim Camera/Body to ASH_Player.prefab")) SaveAim(wa);
            }            EditorGUILayout.Space();
            if (GUILayout.Button("Invector IK Adjust Window (gizmo handles)")) EditorApplication.ExecuteMenuItem("Invector/Shooter/Open IK Adjust Window");
            EditorGUILayout.EndScrollView();
        }

        void Adjust(vWeaponIKAdjust ik, string state)
        {
            var a = ik.ikAdjustsLeft.Find(x => x.name == state); if (a == null) return;
            EditorGUILayout.Space(); EditorGUILayout.LabelField($"Aim pose: {state} (bow hand)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var pos = EditorGUILayout.Vector3Field("Position", a.weaponHandOffset.position);
            var eul = EditorGUILayout.Vector3Field("Rotation", a.weaponHandOffset.eulerAngles);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Left")) pos += LeftAxis * _step; if (GUILayout.Button("Right")) pos -= LeftAxis * _step;
                if (GUILayout.Button("Closer")) pos += CloserAxis * _step; if (GUILayout.Button("Farther")) pos -= CloserAxis * _step;
                if (GUILayout.Button("Up")) pos += UpAxis * _step; if (GUILayout.Button("Down")) pos -= UpAxis * _step;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Yaw left")) eul -= YawRightAxis * _degStep; if (GUILayout.Button("Yaw right")) eul += YawRightAxis * _degStep;
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(ik, "Bow aim tuning");
                a.weaponHandOffset.position = pos; a.weaponHandOffset.eulerAngles = eul;
                EditorUtility.SetDirty(ik); AssetDatabase.SaveAssetIfDirty(ik);
            }
        }

        /// <summary>Stores the rig's values; applies them to ASH_Player.prefab now (Edit Mode) or when Play ends.</summary>
        public static void Save(ASHBowDrawRig rig)
        {
            if (!rig) return;
            SessionState.SetString(PendingKey, EditorJsonUtility.ToJson(rig));
            if (Application.isPlaying) Debug.Log("[ASH] Bow tuning stored; it is written to ASH_Player.prefab when Play ends.");
            else ApplyPending();
        }

        static readonly string[] AimFields = { "bowCameraLeft", "bowCameraDown", "aimCameraRight", "aimCameraHeight", "bowCrosshairAlign", "bowCrosshairWeight", "bowCrosshairMaxDeg", "bowUprightTorso", "bowTorsoLean" };
        const string PendingAimKey = "ASH_BowAimTuningPending";
        static void SaveAim(ASH.Gameplay.ASHWeaponAim wa)
        {
            var so = new SerializedObject(wa); var parts = new System.Collections.Generic.List<string>();
            foreach (var n in AimFields) { var p = so.FindProperty(n); if (p == null) continue; parts.Add(n + "=" + (p.propertyType == SerializedPropertyType.Boolean ? (p.boolValue ? "1" : "0") : p.floatValue.ToString(System.Globalization.CultureInfo.InvariantCulture))); }
            SessionState.SetString(PendingAimKey, string.Join(";", parts));
            if (Application.isPlaying) Debug.Log("[ASH] Bow aim camera/body stored; written to ASH_Player.prefab when Play ends."); else ApplyPending();
        }
        static void WriteAim(Object target, string data)
        {
            var so = new SerializedObject(target);
            foreach (var kv in data.Split(';'))
            {
                var i = kv.IndexOf('='); if (i < 0) continue; var p = so.FindProperty(kv.Substring(0, i)); if (p == null) continue; var v = kv.Substring(i + 1);
                if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v == "1"; else p.floatValue = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            so.ApplyModifiedProperties();
        }
        /// <summary>t340: writes the draw-hand-on-arrow-line defaults to ASH_Player.prefab and the open scene's rigs (scene not saved).</summary>
        public static void ApplyHandLineDefaults()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try { foreach (var r in root.GetComponentsInChildren<ASHBowDrawRig>(true)) { r.handOnArrowLine = 1f; r.handLineSide = 0.07f; } PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            foreach (var r in Object.FindObjectsByType<ASHBowDrawRig>(FindObjectsInactive.Include))
            { if (EditorUtility.IsPersistent(r)) continue; Undo.RecordObject(r, "Bow hand line"); r.handOnArrowLine = 1f; r.handLineSide = 0.07f; EditorUtility.SetDirty(r); }
            Debug.Log("[ASH] BOWA hand line defaults written (prefab + open scene rigs)");
        }

        static void ApplyPending()
        {
            var aimData = SessionState.GetString(PendingAimKey, "");
            if (!string.IsNullOrEmpty(aimData))
            {
                SessionState.EraseString(PendingAimKey);
                var r0 = PrefabUtility.LoadPrefabContents(PlayerPrefab);
                try { var wa = r0 ? r0.GetComponentInChildren<ASH.Gameplay.ASHWeaponAim>(true) : null; if (wa) { WriteAim(wa, aimData); PrefabUtility.SaveAsPrefabAsset(r0, PlayerPrefab); Debug.Log("[ASH] Bow aim camera/body saved to ASH_Player.prefab."); } }
                finally { if (r0) PrefabUtility.UnloadPrefabContents(r0); }
                foreach (var w in Object.FindObjectsByType<ASH.Gameplay.ASHWeaponAim>(FindObjectsInactive.Include))
                { if (EditorUtility.IsPersistent(w)) continue; Undo.RecordObject(w, "Bow aim tuning"); WriteAim(w, aimData); EditorUtility.SetDirty(w); }
            }
            var json = SessionState.GetString(PendingKey, ""); if (string.IsNullOrEmpty(json)) return;
            SessionState.EraseString(PendingKey);
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var rig = root ? root.GetComponentInChildren<ASHBowDrawRig>(true) : null;
                if (!rig) { Debug.LogWarning("[ASH] Save Bow Tuning: no ASHBowDrawRig in " + PlayerPrefab); return; }
                Overwrite(rig, json);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            }
            finally { if (root) PrefabUtility.UnloadPrefabContents(root); }
            // the open scene's player may override these fields: give it the same values (scene left unsaved)
            foreach (var r in Object.FindObjectsByType<ASHBowDrawRig>(FindObjectsInactive.Include))
            { if (EditorUtility.IsPersistent(r)) continue; Undo.RecordObject(r, "Bow tuning"); Overwrite(r, json); EditorUtility.SetDirty(r); }
            Debug.Log("[ASH] Bow tuning saved to ASH_Player.prefab.");
        }

        static void Overwrite(ASHBowDrawRig rig, string json)
        {
            var keep = rig.handTarget; var en = rig.enabled;
            EditorJsonUtility.FromJsonOverwrite(json, rig);
            rig.handTarget = keep; rig.enabled = en;
        }
    }
}
