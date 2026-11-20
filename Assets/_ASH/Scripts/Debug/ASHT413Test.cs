using System.Collections;
using System.IO;
using ASH.Core;
using ASH.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ASH.DebugTools
{
    /// <summary>Play Mode, exec ASH.DebugTools.ASHT413Test.Run: autosave row on top of the save/load list, Save disabled on it, load from it.
    /// Backs up and restores the existing slot_auto.json. Logs "[ASH] T413 ...".</summary>
    public sealed class ASHT413Test : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) { var g = new GameObject("ASH_T413Test"); DontDestroyOnLoad(g); g.AddComponent<ASHT413Test>(); } }
        static void L(string m) => Debug.Log("[ASH] T413 " + m);
        bool _restored;
        IEnumerator Start()
        {
            yield return null;
            string p = ASHSaveSystem.SlotPath(ASHSaveSystem.AutoSlot), bak = p + ".t413bak";
            bool had = File.Exists(p); if (had) File.Copy(p, bak, true);
            bool saved = ASHSaveSystem.Instance && ASHSaveSystem.Instance.Save(ASHSaveSystem.AutoSlot);
            var d = ASHSaveSystem.Read(ASHSaveSystem.AutoSlot);
            L((saved && d != null ? "PASS" : "FAIL") + $" autosave written to its own file {Path.GetFileName(p)}");
            var panel = FindAnyObjectByType<ASHSaveSlotsPanel>(FindObjectsInactive.Include);
            if (!panel) { L("FAIL no save slots panel"); yield break; }
            foreach (var mode in new[] { ASHSaveSlotsPanel.Mode.Load, ASHSaveSlotsPanel.Mode.Save })
            {
                panel.Show(mode); yield return null;
                Transform row = null; foreach (var t in panel.GetComponentsInChildren<Transform>(true)) if (t.name == "Slot_auto") { row = t; break; }
                if (!row) { L($"FAIL {mode}: no Autosave row"); continue; }
                var first = row.parent.GetChild(0); var next = row.parent.childCount > 1 ? row.parent.GetChild(1).name : "-";
                string info = ""; foreach (var tx in row.GetComponentsInChildren<Text>(true)) if (tx.name == "Info") info = tx.text;
                bool inter = row.GetComponent<Button>().interactable;
                bool want = mode == ASHSaveSlotsPanel.Mode.Load ? inter : !inter;
                L((first == row && next == "Slot_1" && want && info.Contains("Played") ? "PASS" : "FAIL") + $" {mode}: Autosave row first (next {next}), clickable={inter}, info=\"{info.Replace("\n", " | ")}\"");
            }
            panel.gameObject.SetActive(false);
            ASHSaveSystem.Restored += OnRestored;
            float h0 = ASHSaveSystem.PlaySeconds;
            ASHSaveSystem.Load(d);
            float w = 0f; while (!_restored && w < 60f) { w += Time.unscaledDeltaTime; yield return null; }
            ASHSaveSystem.Restored -= OnRestored;
            L((_restored ? "PASS" : "FAIL") + $" load from Autosave restored ({w:0.0}s, play time {ASHSaveSystem.PlaySeconds:0}s, saved {d.playSeconds:0}s)");
            if (had) { File.Copy(bak, p, true); File.Delete(bak); } else if (File.Exists(p)) File.Delete(p);
            L($"DONE (original autosave {(had ? "restored" : "removed, none existed")})");
            Destroy(gameObject);
        }
        void OnRestored() => _restored = true;
    }
}