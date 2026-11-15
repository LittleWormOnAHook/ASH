using ASH.Core;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;
using W = ASH.UI.ASHMenuWidgets;

namespace ASH.UI
{
    /// <summary>
    /// Main menu (ASH_MainMenu scene, made by ASH/Systems/Create Main Menu Scene): Continue (newest save; disabled when there
    /// is none), Start Game (new game with the opening; asks before replacing the autosave), Load Game (slots), Settings, Exit.
    /// Code-built uGUI in the HUD style.
    /// </summary>
    public sealed class ASHMainMenu : MonoBehaviour
    {
        [Tooltip("Optional background image (e.g. a key art sprite). Empty = dark ash gradient.")]
        public Sprite background;
        public Color backgroundTint = new Color(0.08f, 0.075f, 0.07f, 1f);

        RectTransform _root, _menu; Button _continue; Text _continueInfo;
        ASHSettingsPanel _settings; ASHSaveSlotsPanel _slots;

        void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            W.EnsureEventSystem(transform);
            _root = W.Canvas(transform, "ASH_MainMenuCanvas", 100);
            var bg = K.Image(K.Stretch("Background", _root), background ? Color.white : backgroundTint, true);
            if (background) { bg.sprite = background; bg.preserveAspect = false; }
            var vign = K.Image(K.Stretch("Haze", _root), new Color(0.55f, 0.5f, 0.46f, 0.08f));
            _menu = K.Stretch("Menu", _root);
            W.Title(_menu, "ASH", new Vector2(140, -170), 120, new Vector2(0, 1));
            var sub = K.Text(K.At("Subtitle", _menu, new Vector2(0, 1), new Vector2(146, -270), new Vector2(900, 40)), ASHMenuConfig.I.subtitle, 24, K.Dim, TextAnchor.MiddleLeft);
            K.Shadow(sub);
            float y = 40; var a = new Vector2(0, 0.5f); var size = new Vector2(380, 62);
            _continue = W.MenuButton(_menu, "Continue", new Vector2(140, y), size, Continue, a); y -= 74;
            _continueInfo = K.Text(K.At("ContinueInfo", _menu, a, new Vector2(540, 40), new Vector2(700, 60)), "", 18, K.Dim, TextAnchor.MiddleLeft);
            W.MenuButton(_menu, "Start Game", new Vector2(140, y), size, StartGame, a); y -= 74;
            W.MenuButton(_menu, "Load Game", new Vector2(140, y), size, () => { _menu.gameObject.SetActive(false); _slots.Show(ASHSaveSlotsPanel.Mode.Load); }, a); y -= 74;
            W.MenuButton(_menu, "Settings", new Vector2(140, y), size, () => { _menu.gameObject.SetActive(false); _settings.Show(); }, a); y -= 74;
            W.MenuButton(_menu, "Exit", new Vector2(140, y), size, ASHSceneFlow.Quit, a);
            K.Text(K.At("Version", _root, new Vector2(1, 0), new Vector2(-24, 16), new Vector2(600, 30)), "Prototype build", 16, K.Dim, TextAnchor.LowerRight);
            _settings = ASHSettingsPanel.Create(_root, ShowMenu);
            _slots = ASHSaveSlotsPanel.Create(_root, ShowMenu);
            ShowMenu();
        }

        void ShowMenu()
        {
            _menu.gameObject.SetActive(true);
            var newest = ASHSaveSystem.Newest();
            W.SetInteractable(_continue, newest != null);
            _continueInfo.text = newest == null ? "" : $"{newest.location}  ·  {newest.weather}\n<color=#857d72>{(newest.slot == ASHSaveSystem.AutoSlot ? "Autosave" : "Slot " + newest.slot)}  ·  {newest.savedAt}  ·  played {W.PlayTime(newest.playSeconds)}</color>";
        }

        void Continue() { var d = ASHSaveSystem.Newest(); if (d != null) ASHSaveSystem.Load(d); }

        void StartGame()
        {
            if (ASHSaveSystem.Exists(ASHSaveSystem.AutoSlot))
                W.Confirm(_root, "Start a new game?\n<size=19><color=#a59e93>The autosave will be replaced by the new game. Manual slots are kept.</color></size>", "START", "CANCEL",
                    () => { ASHSaveSystem.Delete(ASHSaveSystem.AutoSlot); ASHSceneFlow.NewGame(); });
            else ASHSceneFlow.NewGame();
        }
    }
}
