using ASH.Core;
using ASH.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.UI
{
    /// <summary>Cover view of Grandpa's journal ("Survival Guide"): shown when the journal is picked up or used from the pack before
    /// it was read. [E] / Read opens it: the Field Manual tab appears in the journal. Also the use handler for the journal item.</summary>
    public sealed class ASHFieldManualCover : MonoBehaviour
    {
        const string PanelId = "manualCover";
        static ASHFieldManualCover _open;
        ASHFieldManual _manual; float _openedAt;

        sealed class UseHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) { var d = ASHFieldManualData.I; return d && it && it == d.journalItem; }
            public string UseLabel(ASHItemDefinition it) { var m = ASHFieldManual.Instance; return m && m.IsRead ? "Open manual" : "Read cover"; }
            public bool Use(ASHItemDefinition it, int slot)
            {
                var m = ASHFieldManual.Instance; if (!m) return false;
                if (!m.IsRead) { Open(m); return true; }
                var hud = FindAnyObjectByType<ASHHud>(); if (hud) { hud.CloseAllPanels(); hud.OpenJournal(ASHJournalUI.Tab.Crafting); }
                return true;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() { ASHFieldManual.ShowCover = Open; ASHHud.UseHandlers.Add(new UseHandler()); }

        public static void Open(ASHFieldManual m)
        {
            if (_open || !m) return;
            var hud = FindAnyObjectByType<ASHHud>(); if (!hud || !hud.Root) return;
            hud.CloseAllPanels();
            var d = m.Data;
            var rt = K.Stretch("ManualCover", hud.Root); K.Image(rt, new Color(0, 0, 0, 0.72f), true);
            var c = rt.gameObject.AddComponent<ASHFieldManualCover>(); c._manual = m; c._openedAt = Time.unscaledTime; _open = c;
            var book = K.At("Book", rt, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(620, 820));
            K.Image(book, new Color(0.30f, 0.20f, 0.13f, 1f));
            K.Image(K.Stretch("Spine", book, 0, 0, 580, 0), new Color(0.2f, 0.13f, 0.08f, 1f));
            K.Image(K.Stretch("Inset", book, 70, 60, 36, 60), new Color(0.36f, 0.25f, 0.16f, 1f));
            var lbl = K.At("Label", book, new Vector2(0.5f, 1f), new Vector2(18, -120), new Vector2(440, 200)); K.Image(lbl, K.Paper);
            K.Text(K.Stretch("Title", lbl, 16, 70, 16, 18), d ? d.coverTitle : "SURVIVAL GUIDE", 44, K.PaperInk, TextAnchor.MiddleCenter, FontStyle.Bold);
            K.Text(K.Stretch("Sub", lbl, 16, 14, 16, 130), d ? d.coverSubtitle : "", 18, K.PaperInk, TextAnchor.MiddleCenter, FontStyle.Italic);
            var note = K.Text(K.At("Note", book, new Vector2(0.5f, 0.5f), new Vector2(18, -80), new Vector2(460, 200)), d ? d.coverNote : "", 22, new Color(0.9f, 0.84f, 0.72f), TextAnchor.MiddleCenter, FontStyle.Italic);
            K.Shadow(note);
            K.Button(K.At("Read", rt, new Vector2(0.5f, 0f), new Vector2(-140, 60), new Vector2(250, 56)), "READ  [E]", 22, () => c.Read());
            K.Button(K.At("Close", rt, new Vector2(0.5f, 0f), new Vector2(140, 60), new Vector2(250, 56)), "PUT AWAY  [Esc]", 20, () => c.Close(), new Color(1, 1, 1, 0.16f)).GetComponentInChildren<Text>().color = K.Ink;
            ASHUIState.SetOpen(PanelId, true);
        }

        void Update()
        {
            var kb = Keyboard.current; if (kb == null || Time.unscaledTime - _openedAt < 0.25f) return;
            if (kb.eKey.wasPressedThisFrame) Read();
            else if (kb.escapeKey.wasPressedThisFrame) { ASHHud.EscConsumedFrame = Time.frameCount; Close(); }
        }

        void Read()
        {
            var m = _manual; Close();
            if (!m) return;
            m.Read();
            var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.OpenJournal(ASHJournalUI.Tab.Crafting);
        }

        void Close() { if (this && gameObject) Destroy(gameObject); }
        void OnDestroy() { if (_open == this) _open = null; ASHUIState.SetOpen(PanelId, false); }
    }
}
