using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ASH.Vehicles;

namespace ASH.EditorTools
{
    /// <summary>t394/t395: tuning lives on the vehicle PREFABS (ASHVehicle, ASHVehicleDriver, ASHVehicleLights, ASHVehicleCamera,
    /// ASHVehicleStability) and the dog's ASH_CompanionSettings asset, so it is editable and saved in Edit mode like any prefab / asset.
    /// In Play, "Save Seat/Driver Tuning" (menu, or the button on those components) writes the live values back to the same prefabs /
    /// asset. Only plain values are copied (object references, fuel / damage state are left alone). The scene is never saved.</summary>
    public static class ASHVehicleTuningSave
    {
        static readonly System.Type[] Types = { typeof(ASHVehicle), typeof(ASHVehicleDriver), typeof(ASHVehicleLights), typeof(ASHVehicleCamera), typeof(ASHVehicleStability), typeof(ASHVehicleTracks), typeof(ASHVehicleSettle) };
        static string PrefabFor(ASHVehicle v)
        {
            string p = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(v.gameObject);
            if (!string.IsNullOrEmpty(p) && AssetDatabase.LoadAssetAtPath<GameObject>(p) && AssetDatabase.LoadAssetAtPath<GameObject>(p).GetComponent<ASHVehicle>()) return p;
            return v.vehicleId == "pickup" ? "Assets/_ASH/Prefabs/Vehicles/ASH_Vehicle_Pickup.prefab" : v.vehicleId == "car" ? "Assets/_ASH/Prefabs/Vehicles/ASH_Vehicle_Car.prefab" : null;
        }

        [MenuItem("ASH/Systems/Vehicles/Save Seat-Driver Tuning (Play)")]
        public static void SaveAll()
        {
            if (!Application.isPlaying) { Debug.Log("[ASH] TUNING: in Edit mode just edit the vehicle prefabs / ASH_CompanionSettings; they save normally. This button is for Play."); return; }
            foreach (var v in ASHVehicle.All.ToArray()) Save(v);
            var dog = ASH.Companion.ASHCompanionSettings.I;
            if (dog && AssetDatabase.Contains(dog)) { EditorUtility.SetDirty(dog); Debug.Log("[ASH] TUNING: saved dog bed / passenger settings -> " + AssetDatabase.GetAssetPath(dog)); }
            AssetDatabase.SaveAssets();
        }

        public static void Save(ASHVehicle v)
        {
            var path = PrefabFor(v); var root = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            if (!root) { Debug.LogWarning($"[ASH] TUNING: no prefab for {v.name}"); return; }
            int n = 0; var names = new List<string>();
            foreach (var t in Types)
            {
                var src = v.GetComponent(t); if (!src) continue;
                var dst = root.GetComponent(t); if (!dst) dst = root.AddComponent(t);
                n += CopyValues(src, dst); names.Add(t.Name);
                EditorUtility.SetDirty(dst);
            }
            PrefabUtility.SavePrefabAsset(root);
            Debug.Log($"[ASH] TUNING: saved {n} values from {v.name} -> {path} ({string.Join(", ", names)}). Scene not saved.");
        }

        static readonly HashSet<string> Skip = new HashSet<string> { "m_Script", "m_Enabled", "m_ObjectHideFlags", "tuningVersion" };
        static int CopyValues(Object src, Object dst)
        {
            var so = new SerializedObject(src); var dso = new SerializedObject(dst); int n = 0;
            var it = so.GetIterator(); bool enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (Skip.Contains(it.name) || HasRef(it)) continue;
                dso.CopyFromSerializedProperty(it); n++;
            }
            dso.ApplyModifiedPropertiesWithoutUndo();
            return n;
        }
        static bool HasRef(SerializedProperty p)
        {
            if (p.propertyType == SerializedPropertyType.ObjectReference || p.propertyType == SerializedPropertyType.ExposedReference || p.propertyType == SerializedPropertyType.ManagedReference) return true;
            if (!p.hasVisibleChildren && !p.isArray) return false;
            var c = p.Copy(); var end = p.GetEndProperty();
            while (c.NextVisible(true) && !SerializedProperty.EqualContents(c, end))
                if (c.propertyType == SerializedPropertyType.ObjectReference || c.propertyType == SerializedPropertyType.ManagedReference) return true;
            return false;
        }
    }

    /// <summary>The Play-mode Save button on the tuning components.</summary>
    [CustomEditor(typeof(ASHVehicleDriver))] public sealed class ASHVehicleDriverEditor : ASHTuningEditor { }
    [CustomEditor(typeof(ASHVehicleLights))] public sealed class ASHVehicleLightsEditor : ASHTuningEditor { }
    [CustomEditor(typeof(ASHVehicleCamera))] public sealed class ASHVehicleCameraEditor : ASHTuningEditor { }
    public abstract class ASHTuningEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (Application.isPlaying)
            {
                if (GUILayout.Button("Save Seat/Driver Tuning (to prefab)", GUILayout.Height(26)))
                    foreach (var t in targets) { var v = ((Component)t).GetComponent<ASHVehicle>(); if (v) ASHVehicleTuningSave.Save(v); }
                EditorGUILayout.HelpBox("Writes the live values of this vehicle's driver, lights, camera, stability and vehicle settings to its prefab. Scene is not saved.", MessageType.None);
            }
            else EditorGUILayout.HelpBox("Edit mode: edit these on the vehicle prefab (or here and Apply to prefab); they save normally.", MessageType.None);
        }
    }
}
