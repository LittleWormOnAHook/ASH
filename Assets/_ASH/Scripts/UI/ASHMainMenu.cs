using ASH.Audio;
using ASH.Core;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;
using W = ASH.UI.ASHMenuWidgets;

namespace ASH.UI
{
    /// <summary>
    /// Main menu (ASH_MainMenu scene, made by ASH/Systems/Create Main Menu Scene): Continue (shown only when a
    /// save exists; loads the latest autosave) sits above New Game. Settings, Load Game, Exit follow.
    /// Code-built uGUI in the HUD style. No scene save needed for button changes.
    /// </summary>
    public sealed class ASHMainMenu : MonoBehaviour
    {
        [Tooltip("Optional background image (e.g. a key art sprite). Empty = dark ash gradient.")]
        public Sprite background;
        public Color backgroundTint = new Color(0.08f, 0.075f, 0.07f, 1f);

        RectTransform _root, _menu, _continueRt, _newGameRt, _infoRt, _loadRt, _settingsRt, _exitRt;
        Button _continue, _newGame; Text _continueInfo;
        ASHSettingsPanel _settings; ASHSaveSlotsPanel _slots;
        readonly Vector2 _btnSize = new Vector2(380, 62);
        const float BtnX = 140f, BtnStep = 74f, BtnTop = 40f;

        void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            W.EnsureEventSystem(transform);
            _root = W.Canvas(transform, "ASH_MainMenuCanvas", 100);
            var bg = K.Image(K.Stretch("Background", _root), background ? Color.white : backgroundTint, true);
            if (background) { bg.sprite = background; bg.preserveAspect = false; }
            K.Image(K.Stretch("Haze", _root), new Color(0.55f, 0.5f, 0.46f, 0.08f));
            _menu = K.Stretch("Menu", _root);
            W.Title(_menu, "ASH", new Vector2(140, -170), 120, new Vector2(0, 1));
            var sub = K.Text(K.At("Subtitle", _menu, new Vector2(0, 1), new Vector2(146, -270), new Vector2(900, 40)), ASHMenuConfig.I.subtitle, 24, K.Dim, TextAnchor.MiddleLeft);
            K.Shadow(sub);
            var a = new Vector2(0, 0.5f);
            _continue = W.MenuButton(_menu, "Continue", new Vector2(BtnX, BtnTop), _btnSize, Continue, a);
            _continueRt = _continue.GetComponent<RectTransform>();
            _continueInfo = K.Text(K.At("ContinueInfo", _menu, a, new Vector2(540, BtnTop), new Vector2(700, 60)), "", 18, K.Dim, TextAnchor.MiddleLeft);
            _infoRt = _continueInfo.rectTransform;
            _newGame = W.MenuButton(_menu, "New Game", new Vector2(BtnX, BtnTop - BtnStep), _btnSize, StartNewGame, a);
            _newGameRt = _newGame.GetComponent<RectTransform>();
            var load = W.MenuButton(_menu, "Load Game", new Vector2(BtnX, BtnTop - BtnStep * 2), _btnSize, () => { _menu.gameObject.SetActive(false); _slots.Show(ASHSaveSlotsPanel.Mode.Load); }, a);
            _loadRt = load.GetComponent<RectTransform>();
            var settingsBtn = W.MenuButton(_menu, "Settings", new Vector2(BtnX, BtnTop - BtnStep * 3), _btnSize, () => { _menu.gameObject.SetActive(false); _settings.Show(); }, a);
            _settingsRt = settingsBtn.GetComponent<RectTransform>();
            var exit = W.MenuButton(_menu, "Exit", new Vector2(BtnX, BtnTop - BtnStep * 4), _btnSize, ASHSceneFlow.Quit, a);
            _exitRt = exit.GetComponent<RectTransform>();
            K.Text(K.At("Version", _root, new Vector2(1, 0), new Vector2(-24, 16), new Vector2(600, 30)), "Prototype build", 16, K.Dim, TextAnchor.LowerRight);
            _settings = ASHSettingsPanel.Create(_root, ShowMenu);
            _slots = ASHSaveSlotsPanel.Create(_root, ShowMenu);
            ShowMenu();
        }

        void ShowMenu()
        {
            _menu.gameObject.SetActive(true);
            var newest = ASHSaveSystem.Newest();
            bool has = newest != null;
            if (_continueRt) _continueRt.gameObject.SetActive(has);
            if (_infoRt) _infoRt.gameObject.SetActive(has);
            float y = BtnTop;
            if (has)
            {
                SetY(_continueRt, y);
                SetY(_infoRt, y);
                y -= BtnStep;
                _continueInfo.text = $"{newest.location}  ·  {newest.weather}\n<color=#857d72>{(newest.slot == ASHSaveSystem.AutoSlot ? "Autosave" : "Slot " + newest.slot)}  ·  {newest.savedAt}  ·  played {W.PlayTime(newest.playSeconds)}</color>";
            }
            SetY(_newGameRt, y); y -= BtnStep;
            SetY(_loadRt, y); y -= BtnStep;
            SetY(_settingsRt, y); y -= BtnStep;
            SetY(_exitRt, y);
        }

        static void SetY(RectTransform rt, float y)
        {
            if (!rt) return;
            var p = rt.anchoredPosition;
            p.y = y;
            rt.anchoredPosition = p;
        }

        void Continue()
        {
            var auto = ASHSaveSystem.Read(ASHSaveSystem.AutoSlot);
            if (auto != null) { ASHSaveSystem.Load(auto); return; }
            var d = ASHSaveSystem.Newest();
            if (d != null) ASHSaveSystem.Load(d);
        }

        void StartNewGame()
        {
            var saves = ASHSaveSystem.AllSaves();
            bool wipe = ShouldWipeSaves();
            if (saves.Count == 0)
            {
                ASHSceneFlow.NewGame();
                return;
            }
            if (!wipe)
            {
                Debug.Log("[ASH] New Game (Editor / development): keeps " + saves.Count + " save(s). Would have deleted: " + ASHSaveSystem.ListSaveNames() + ". Enable 'Wipe Saves On New Game In Editor' on ASH_AudioSettings to test the shipped path.");
                ASHSceneFlow.NewGame();
                return;
            }
            W.Confirm(_root, "Starting a new game deletes your saves", "START", "CANCEL",
                () => { ASHSaveSystem.DeleteAll(); ASHSceneFlow.NewGame(); });
        }

        static bool ShouldWipeSaves()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var s = ASHAudioSettings.I;
            return s && s.wipeSavesOnNewGameInEditor;
#else
            return true;
#endif
        }
    }
}
