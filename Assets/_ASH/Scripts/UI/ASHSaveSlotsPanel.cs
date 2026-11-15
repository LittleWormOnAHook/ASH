using System;
using ASH.Core;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;
using W = ASH.UI.ASHMenuWidgets;

namespace ASH.UI
{
    /// <summary>Save / load slot list: autosave (load only) + manual slots, each with play time, date, location, weather and
    /// time of day. Save asks before overwriting; load reloads the game scene with that save.</summary>
    public sealed class ASHSaveSlotsPanel : MonoBehaviour
    {
        public enum Mode { Save, Load }
        Mode _mode; Action _onBack; RectTransform _list, _root; Text _title;

        public static ASHSaveSlotsPanel Create(RectTransform parent, Action onBack)
        {
            var rt = K.At("Slots", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 760));
            K.Image(rt, K.Panel, true);
            var p = rt.gameObject.AddComponent<ASHSaveSlotsPanel>(); p._onBack = onBack; p._root = parent; p.Build(rt);
            rt.gameObject.SetActive(false);
            return p;
        }

        void Build(RectTransform rt)
        {
            K.Image(K.At("Accent", rt, new Vector2(0.5f, 1), Vector2.zero, new Vector2(1000, 3)), K.Accent);
            _title = K.Text(K.At("Title", rt, new Vector2(0, 1), new Vector2(30, -18), new Vector2(600, 44)), "", 32, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _list = K.ScrollList(K.Stretch("List", rt, 30, 90, 30, 80), 8);
            K.Button(K.At("Back", rt, new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(260, 52)), "BACK", 22, () => { gameObject.SetActive(false); _onBack?.Invoke(); });
        }

        public void Show(Mode m) { _mode = m; gameObject.SetActive(true); transform.SetAsLastSibling(); Rebuild(); }

        void Rebuild()
        {
            _title.text = _mode == Mode.Save ? "SAVE GAME" : "LOAD GAME";
            K.Clear(_list);
            if (_mode == Mode.Load) Slot(ASHSaveSystem.AutoSlot, "Autosave");
            int n = ASHSaveSettings.I.manualSlots;
            for (int i = 1; i <= n; i++) Slot(i.ToString(), $"Slot {i}");
        }

        void Slot(string slot, string label)
        {
            var d = ASHSaveSystem.Read(slot);
            var row = K.Rect("Slot_" + slot, _list); K.Layout(row, 96);
            var img = K.Image(row, K.Row, true);
            var b = row.gameObject.AddComponent<Button>(); b.targetGraphic = img; var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            var cb = b.colors; cb.highlightedColor = new Color(1.8f, 1.4f, 1f); cb.colorMultiplier = 2f; b.colors = cb;
            K.Text(K.At("Name", row, new Vector2(0, 1), new Vector2(20, -10), new Vector2(300, 34)), label, 24, slot == ASHSaveSystem.AutoSlot ? K.Accent : K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            string info = d == null ? "<color=#857d72>Empty</color>"
                : $"{d.location}   ·   {d.weather}{(string.IsNullOrEmpty(d.timeOfDay) ? "" : "   ·   " + d.timeOfDay)}\n<color=#a59e93>Played {W.PlayTime(d.playSeconds)}   ·   saved {d.savedAt}</color>";
            K.Text(K.At("Info", row, new Vector2(0, 1), new Vector2(20, -44), new Vector2(900, 50)), info, 18, K.Ink, TextAnchor.UpperLeft);
            bool ok = _mode == Mode.Save ? ASHSaveSystem.Instance != null : d != null;
            b.interactable = ok;
            b.onClick.AddListener(() => Pick(slot, d));
        }

        void Pick(string slot, ASHSaveData d)
        {
            var host = (RectTransform)transform.parent;
            if (_mode == Mode.Save)
            {
                void DoSave() { if (ASHSaveSystem.Instance && ASHSaveSystem.Instance.Save(slot)) Rebuild(); }
                if (d != null) W.Confirm(host, $"Overwrite {(slot == ASHSaveSystem.AutoSlot ? "the autosave" : "slot " + slot)}?\n<size=18><color=#a59e93>{d.location} · {d.savedAt}</color></size>", "OVERWRITE", "CANCEL", DoSave);
                else DoSave();
            }
            else if (d != null)
            {
                bool inGame = ASHSaveSystem.Instance != null;
                if (inGame) W.Confirm(host, "Load this save? Progress since your last save will be lost.", "LOAD", "CANCEL", () => ASHSaveSystem.Load(d));
                else ASHSaveSystem.Load(d);
            }
        }
    }
}
