using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>t380: vehicle drive tests (Play only). Terrain is the default; the 1 km flat plane is opt-in.</summary>
    public static class ASHVehicleTestMenu
    {
        [MenuItem("ASH/Systems/Vehicles/Test Drive (Terrain)")]
        static void Terrain() { if (!Application.isPlaying) { Debug.LogWarning("[ASH] Enter Play first"); return; } ASH.DebugTools.ASHVehicleBounceTest.RunTerrainBoth(); }
        [MenuItem("ASH/Systems/Vehicles/Full Test Pass (Terrain)")]
        static void Full() { if (!Application.isPlaying) { Debug.LogWarning("[ASH] Enter Play first"); return; } ASH.DebugTools.ASHVehicleFullTest.Run(); }
        [MenuItem("ASH/Systems/Vehicles/Test Driver + Dog Bed + Brakes (t389)")]
        static void T389() { if (!Application.isPlaying) { Debug.LogWarning("[ASH] Enter Play first"); return; } ASH.DebugTools.ASHVehicleT389Test.Run(); }
        [MenuItem("ASH/Systems/Vehicles/Test Body Roll (1 km Flat Plane)")]
        static void Roll() { if (!Application.isPlaying) { Debug.LogWarning("[ASH] Enter Play first"); return; } ASH.DebugTools.ASHVehicleBounceTest.RunRoll(); }
        [MenuItem("ASH/Systems/Vehicles/Test Drive (1 km Flat Plane)")]
        static void Plane() { if (!Application.isPlaying) { Debug.LogWarning("[ASH] Enter Play first"); return; } ASH.DebugTools.ASHVehicleBounceTest.RunPlaneBoth(); }
    }
}