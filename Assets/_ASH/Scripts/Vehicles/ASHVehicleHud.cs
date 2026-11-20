using ASH.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ASH.Vehicles
{
    /// <summary>Driving HUD (t356): gas gauge, speed, battery / engine status and the key hints. Shown only while driving.</summary>
    public sealed class ASHVehicleHud : MonoBehaviour
    {
        static ASHVehicleHud _i;
        RectTransform _root, _fill; Image _fillImg; Text _fuelTxt, _speedTxt, _status, _hint, _name;
        public static void Show(ASHVehicle v) { if (!_i) _i = new GameObject("ASH_VehicleHud").AddComponent<ASHVehicleHud>(); _i._v = v; _i._root.gameObject.SetActive(v); }
        ASHVehicle _v;
        public static bool Visible => _i && _i._root && _i._root.gameObject.activeSelf;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            var c = gameObject.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 120;
            var sc = gameObject.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            _root = ASHUIKit.At("Vehicle", transform, new Vector2(1f, 0f), new Vector2(-36f, 250f), new Vector2(300f, 132f));
            _name = ASHUIKit.Text(ASHUIKit.At("Name", _root, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(300f, 24f)), "", 18, ASHUIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold); ASHUIKit.Shadow(_name);
            var lbl = ASHUIKit.Text(ASHUIKit.At("FuelLbl", _root, new Vector2(0f, 1f), new Vector2(0f, -26f), new Vector2(60f, 22f)), "FUEL", 15, ASHUIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); ASHUIKit.Shadow(lbl);
            var bg = ASHUIKit.At("FuelBar", _root, new Vector2(0f, 1f), new Vector2(60f, -30f), new Vector2(150f, 14f)); ASHUIKit.Image(bg, new Color(0f, 0f, 0f, 0.55f));
            _fill = ASHUIKit.Stretch("Fill", bg, 2f, 2f, 2f, 2f); _fill.anchorMax = new Vector2(1f, 1f); _fillImg = ASHUIKit.Image(_fill, ASHUIKit.Accent);
            _fuelTxt = ASHUIKit.Text(ASHUIKit.At("FuelTxt", _root, new Vector2(0f, 1f), new Vector2(216f, -26f), new Vector2(90f, 22f)), "", 15, ASHUIKit.Ink); ASHUIKit.Shadow(_fuelTxt);
            _speedTxt = ASHUIKit.Text(ASHUIKit.At("Speed", _root, new Vector2(0f, 1f), new Vector2(0f, -52f), new Vector2(300f, 30f)), "", 24, ASHUIKit.Ink, TextAnchor.MiddleLeft, FontStyle.Bold); ASHUIKit.Shadow(_speedTxt);
            _status = ASHUIKit.Text(ASHUIKit.At("Status", _root, new Vector2(0f, 1f), new Vector2(0f, -84f), new Vector2(300f, 20f)), "", 14, ASHUIKit.Dim); ASHUIKit.Shadow(_status);
            _hint = ASHUIKit.Text(ASHUIKit.At("Hint", _root, new Vector2(0f, 1f), new Vector2(0f, -106f), new Vector2(330f, 20f)), "[E] Exit  [Tab] Camera  [L] Lights  [Space] Handbrake", 13, ASHUIKit.Dim); ASHUIKit.Shadow(_hint);
            _root.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!_v) { if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false); return; }
            var f = _v.Fuel; float fr = f ? f.Fraction : 0f;
            _fill.anchorMax = new Vector2(Mathf.Max(0.001f, fr), 1f);
            _fillImg.color = fr < 0.15f ? ASHUIKit.Bad : ASHUIKit.Accent;
            _fuelTxt.text = f ? $"{f.litres:0}/{f.capacityLitres:0} L" : "-";
            _name.text = _v.displayName.ToUpperInvariant();
            _speedTxt.text = $"{_v.SpeedKph:0} km/h   {_v.GearText}";
            var c = _v.Condition;
            string bat = c ? (c.batteryInstalled ? $"Battery {c.batteryCharge * 100f:0}%" : "No battery") : "";
            _status.text = (_v.EngineRunning ? "Engine running" : "Engine off") + (bat.Length > 0 ? "  ·  " + bat : "") + (_v.LightsOn ? "  ·  Lights on" : "");
        }
    }
}
