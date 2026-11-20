using UnityEditor;
using UnityEngine;
using ASH.Vehicles;

namespace ASH.EditorTools.Vehicles
{
    /// <summary>Creates Assets/_ASH/Resources/ASH_VehicleSurfaceFx.asset once (NWH smoke + dust prefabs), never overwrites edits. No scene changes.</summary>
    [InitializeOnLoad]
    static class ASHVehicleSurfaceFxAsset
    {
        const string AssetPath = "Assets/_ASH/Resources/ASH_VehicleSurfaceFx.asset";
        static ASHVehicleSurfaceFxAsset() { EditorApplication.delayCall += Ensure; }
        [MenuItem("ASH/Systems/Vehicles/Ensure Surface FX (dust + smoke)")]
        public static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var fx = AssetDatabase.LoadAssetAtPath<ASHVehicleSurfaceFx>(AssetPath); bool made = false;
            if (!fx) { fx = ScriptableObject.CreateInstance<ASHVehicleSurfaceFx>(); AssetDatabase.CreateAsset(fx, AssetPath); made = true; }
            bool ch = made;
            if (!fx.smokePrefab) { fx.smokePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VehiclePhysics/Effects/Particles/Prefabs/SkidSmoke.prefab"); ch |= fx.smokePrefab; }
            if (!fx.dustPrefab) { fx.dustPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VehiclePhysics/Effects/Particles/Prefabs/SurfaceDust.prefab"); ch |= fx.dustPrefab; }
            if (ch) { EditorUtility.SetDirty(fx); AssetDatabase.SaveAssetIfDirty(fx); Debug.Log($"[ASH] Vehicles: surface FX asset {(made ? "created" : "updated")} smoke={(fx.smokePrefab ? fx.smokePrefab.name : "none")} dust={(fx.dustPrefab ? fx.dustPrefab.name : "none")}"); }
        }
    }
}
