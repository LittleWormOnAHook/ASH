using System.Text;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>M1 dev probe: logs where the opening's scene pieces are (vehicles, shelters, journal, supplies, spawn points).</summary>
    public static class ASHPrologueProbe
    {
        static string P(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
        static string V(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";
        [MenuItem("ASH/Dev/Prologue Probe (log)")]
        public static void Probe()
        {
            var sb = new StringBuilder("[ASH] PROBE\n");
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (!mb) continue; var n = mb.GetType().Name;
                if (n == "ASHVehicle" || n == "ASHShelterZone" || n == "ASHSupplyCrate" || n == "ASHDogCompanion" || n == "ASHEruptionSequence" || n == "ASHOpeningObjective"
                    || n == "ASHObjectiveTracker" || n == "ASHPlayerDeath" || n == "ASHSaveSystem" || n == "ASHCabinDoor" || n.Contains("Cabin") || n == "ASHVehicleCondition" || n == "ASHPickup" && mb.name.Contains("Journal"))
                {
                    var t = mb.transform; string extra = "";
                    var c = mb.GetComponent<Collider>(); if (c) extra += " bounds=" + V(c.bounds.size);
                    if (n == "ASHVehicle") { var so = new SerializedObject(mb); extra += " id=" + so.FindProperty("vehicleId").stringValue + " name=" + so.FindProperty("displayName").stringValue; }
                    if (n == "ASHShelterZone") { var so = new SerializedObject(mb); extra += " sealing=" + so.FindProperty("sealing").floatValue + " label=" + so.FindProperty("label").stringValue; }
                    sb.AppendLine($"{n} | {P(t)} | {V(t.position)} yaw={t.eulerAngles.y:F0} active={mb.gameObject.activeInHierarchy}{extra}");
                }
            }
            foreach (var nm in new[] { "ASH_OpeningSpawn", "ASH_PlayerSpawn", "ASH_Pickup_GrandpasJournal", "POI_redstone_camp", "shack_spur", "Tire Replacement (1)", "ASH_Objective_OldCampsite", "ASH_CabinInterior", "ASH_VehicleItems_Cabin", "ASH_CabinDoor" })
            { var g = GameObject.Find(nm); sb.AppendLine(nm + " : " + (g ? P(g.transform) + " " + V(g.transform.position) : "not found/inactive")); }
            Debug.Log(sb.ToString());
        }
    }
}
