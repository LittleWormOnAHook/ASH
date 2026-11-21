using System.Linq;
using System.Text;
using ASH.Core;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Prologue;
using ASH.Vehicles;
using Invector.vCharacterController;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>M1 Play-mode test helpers (bridge exec / ASH/Dev/Prologue menu). Lightweight; nothing here runs in builds.</summary>
    public static class ASHPrologueDev
    {
        static ASHPrologueDirector D => ASHPrologueDirector.Instance;
        static vThirdPersonController CC => Object.FindAnyObjectByType<vThirdPersonController>();
        static ASHInventory Inv => Object.FindAnyObjectByType<ASHInventory>();
        static void Tp(Vector3 p)
        {
            var cc = CC; if (!cc) return; var rb = cc.GetComponent<Rigidbody>();
            if (rb) { rb.linearVelocity = Vector3.zero; rb.position = p + Vector3.up * 0.1f; }
            cc.transform.position = p + Vector3.up * 0.1f; Physics.SyncTransforms();
        }

        [MenuItem("ASH/Dev/Prologue/Status (log)")]
        public static void Status()
        {
            var sb = new StringBuilder("[ASH] M1 STATUS ");
            sb.Append(D ? D.Describe() : "no director").Append('\n');
            { var ex = ASH.Survival.ASHExposure.Instance; if (ex) sb.Append($" exposure: air={ex.AirStrength:F2} airBad={ex.AirBad} exposure={ex.Exposure:F2} warning={ex.Warning}\n"); }
            var inv = Inv;
            if (inv)
            {
                var counts = inv.Slots.Where(s => !s.IsEmpty).GroupBy(s => s.item.itemId).Select(g => $"{g.Key}x{g.Sum(s => s.amount)}");
                sb.Append(" inv: ").Append(string.Join(", ", counts)).Append('\n');
            }
            var tr = Object.FindAnyObjectByType<ASHObjectiveTracker>();
            if (tr) sb.Append(" objectives: ").Append(string.Join(" | ", tr.Objectives.Select(o => $"{o.Id}:{o.Title}{(o.Completed ? "(done)" : "")}"))).Append($" tracked={tr.TrackedId} sliceStarted={tr.SliceStarted}\n");
            var fm = ASHFieldManual.Instance;
            if (fm && fm.Data) sb.Append($" manual read={fm.IsRead} unlocked={fm.Data.entries.Count(e => e.recipe && fm.IsUnlocked(e.recipe))}: {string.Join(",", fm.Data.entries.Where(e => e.recipe && fm.IsUnlocked(e.recipe)).Select(e => e.recipe.name.Replace("ASH_Recipe_", "")))}\n");
            var ch = ASH.Prologue.ASHObjectiveChainRunner.Instance; if (ch) sb.Append(" chain: ").Append(ch.Describe()).Append('\n');
            sb.Append($" grandpa pages={ASHGrandpaJournal.Count("grandpa")} gj_01={ASHGrandpaJournal.Has("grandpa", "gj_01")} gj_02={ASHGrandpaJournal.Has("grandpa", "gj_02")}\n");
            var seq = ASH.Opening.ASHEruptionSequence.Instance; if (seq) sb.Append(" opening: ").Append(seq.Describe()).Append('\n');
            foreach (var v in ASHVehicle.All) if (v) sb.Append($" vehicle {v.vehicleId}: pos={v.transform.position:F1} destroyed={v.Destroyed} occupied={v.IsOccupied} kinematic={(v.GetComponent<Rigidbody>() ? v.GetComponent<Rigidbody>().isKinematic : false)}\n");
            var dog = ASH.Companion.ASHDogCompanion.Active; if (dog) sb.Append($" dog: riding={dog.Riding} passenger={dog.InPassengerSeat}\n");
            sb.Append(" saves: ").Append(string.Join(", ", ASHSaveSystem.AllSaves().Select(s => s.slot + "@" + s.savedAt)));
            var cc = CC; if (cc) sb.Append($"\n player={cc.transform.position:F1}");
            Debug.Log(sb.ToString());
        }

        [MenuItem("ASH/Dev/Prologue/Park Car In Zone And Exit")]
        public static void ParkAndExit()
        {
            var car = ASHVehicle.All.FirstOrDefault(v => v && v.vehicleId == "car"); if (!car || !D || !D.parkingZone) return;
            var rb = car.GetComponent<Rigidbody>(); var z = D.parkingZone;
            if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.position = z.position; rb.rotation = z.rotation; }
            car.transform.SetPositionAndRotation(z.position, z.rotation); Physics.SyncTransforms();
            car.Exit(); Debug.Log("[ASH] Dev: car parked in the zone (teleport, no driving), exiting");
        }
        [MenuItem("ASH/Dev/Prologue/Teleport Into Cabin")]
        public static void TpCabin() { if (D && D.cabinShelter) { var b = D.cabinShelter.volume ? D.cabinShelter.volume.bounds : new Bounds(D.cabinShelter.transform.position, Vector3.one); Tp(new Vector3(b.center.x, b.min.y + 0.2f, b.center.z)); Debug.Log("[ASH] Dev: player in the cabin"); } }
        [MenuItem("ASH/Dev/Prologue/Teleport Outside (by the car)")]
        public static void TpOutside() { if (D && D.parkingZone && D.cabinShelter) { var p = Vector3.Lerp(D.cabinShelter.transform.position, D.parkingZone.position, 0.45f); if (Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out var h, 100f, ~0, QueryTriggerInteraction.Ignore)) p = h.point; Tp(p); Debug.Log($"[ASH] Dev: player outside at {p:F1}"); } }
        [MenuItem("ASH/Dev/Prologue/Take Supplies")]
        public static void TakeSupplies() { if (D && D.supplies) Debug.Log("[ASH] Dev: take supplies -> " + D.supplies.Take(Inv)); }
        [MenuItem("ASH/Dev/Prologue/Pick Up Journal")]
        public static void PickJournal()
        {
            var fm = ASHFieldManual.Instance; var inv = Inv; if (!fm || !inv || !fm.Data || !fm.Data.journalItem) return;
            var g = GameObject.Find("ASH_Pickup_GrandpasJournal"); if (g) g.SetActive(false);
            inv.AddItem(fm.Data.journalItem, 1); fm.Read(); Debug.Log("[ASH] Dev: journal picked up and read");
        }
        [MenuItem("ASH/Dev/Prologue/Pick Up Grandpa Page 1")]
        public static void Page1() => ASHGrandpaJournal.Find("grandpa", "gj_01");
        /// <summary>t443 one-shot check: picks up the roadside page gj_03 through its real torn-page pickup, opens Grandpa's Journal in the reader.</summary>
        [MenuItem("ASH/Dev/Prologue/Pick Up Grandpa Page 3 (road) + Read")]
        public static void GpPage3()
        {
            var pk = Object.FindObjectsByType<ASH.Interaction.ASHManualPagePickup>(FindObjectsInactive.Exclude).FirstOrDefault(p => p.pageId == "gj_03");
            bool used = pk && pk.TryUse(default);
            var cm = ASH.UI.ASHCompass.Instance; var md = cm ? typeof(ASH.UI.ASHCompass).GetField("_markers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(cm) as System.Collections.IDictionary : null;
            var ui = Object.FindAnyObjectByType<ASH.UI.ASHJournalUI>(FindObjectsInactive.Include); string title = "-", num = "-";
            if (ui) { ui.Open(ASH.UI.ASHJournalUI.Tab.Notes); ui.OpenGrandpa(null); var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                title = (typeof(ASH.UI.ASHJournalUI).GetField("_bookTitle", bf)?.GetValue(ui) as UnityEngine.UI.Text)?.text; num = (typeof(ASH.UI.ASHJournalUI).GetField("_bookNum", bf)?.GetValue(ui) as UnityEngine.UI.Text)?.text; }
            Debug.Log($"[ASH] Dev GpPage3: pickup={(bool)pk} used={used} has={ASHGrandpaJournal.Has("grandpa", "gj_03")} pickupActive={(pk ? pk.gameObject.activeSelf : false)} marker gj_gj_03={(md != null && md.Contains("gj_gj_03"))} reader='{num}' / '{title}'");
        }
        /// <summary>t450: build list = ASH_MainMenu, ASH_Prologue (game), then the rest minus the old ASH_Prototype (file kept).</summary>
        [MenuItem("ASH/Dev/Prologue/Build List: MainMenu + ASH_Prologue")]
        public static void UsePrologueScene()
        {
            const string menu = "Assets/_ASH/Scenes/ASH_MainMenu.unity", game = "Assets/_ASH/Scenes/ASH_Prologue.unity", old = "Assets/_ASH/Scenes/ASH_Prototype.unity";
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(menu, true), new EditorBuildSettingsScene(game, true) };
            foreach (var s in EditorBuildSettings.scenes) if (s.path != menu && s.path != game && s.path != old) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray(); AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Dev: build list = " + string.Join(", ", list.Select(s => s.path + (s.enabled ? "" : " (off)"))));
        }
        [MenuItem("ASH/Dev/Prologue/Start Warning Now")]
        public static void Warn() { if (D) D.DevStartWarning(); }
        [MenuItem("ASH/Dev/Prologue/Inspect Car")]
        public static void InspectCar() { if (D) D.DevInspectCar(); }
        [MenuItem("ASH/Dev/Prologue/Teleport To Truck")]
        public static void TpTruck() { var t = ASHVehicle.All.FirstOrDefault(v => v && v.vehicleId == "pickup"); if (t) { var p = t.transform.position + t.transform.right * 3.5f; if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var h, 60f, ~0, QueryTriggerInteraction.Ignore)) p = h.point; Tp(p); } }
        [MenuItem("ASH/Dev/Prologue/Save Test Slot")]
        public static void SaveTest() { var s = ASHSaveSystem.Instance; Debug.Log("[ASH] Dev: save m1test -> " + (s && s.Save("m1test"))); }
        [MenuItem("ASH/Dev/Prologue/Load Test Slot")]
        public static void LoadTest() { var d = ASHSaveSystem.Read("m1test"); Debug.Log("[ASH] Dev: load m1test " + (d != null)); if (d != null) ASHSaveSystem.Load(d); }
        [MenuItem("ASH/Dev/Prologue/Retry (death screen button)")]
        public static void Retry() { Debug.Log($"[ASH] Dev: retry available={(ASH.UI.ASHDeathScreen.RetryAvailable != null && ASH.UI.ASHDeathScreen.RetryAvailable())}"); ASH.UI.ASHDeathScreen.Retry?.Invoke(); }
        public static void MainMenu() => ASHSceneFlow.MainMenu();
        public static void NewGame() => ASHSceneFlow.NewGame();
        public static void Shot() { var p = System.IO.Path.GetFullPath("AshBridge/shots/m1_" + System.DateTime.Now.ToString("HHmmss") + ".png"); ScreenCapture.CaptureScreenshot(p); Debug.Log("[ASH] Dev: screenshot " + p); }
        public static void Warning15() { var s = ASHPrologueSettings.I; if (s) { s.warningSeconds = 15f; Debug.Log("[ASH] Dev: warningSeconds = 15 (TEST ONLY, restore with Warning75)"); } }
        public static void Warning75() { var s = ASHPrologueSettings.I; if (s) { s.warningSeconds = 75f; EditorUtility.SetDirty(s); Debug.Log("[ASH] Dev: warningSeconds = 75"); } }
    }
}
