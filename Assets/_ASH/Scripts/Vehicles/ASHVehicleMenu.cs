using System.Text;
using ASH.Core;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;

namespace ASH.Vehicles
{
    /// <summary>Vehicle menu (t362): E at the driver door opens it (E again = Drive). Drive / Fuel Up / Repair, with the fuel,
    /// battery and belt status and the part each repair needs. Fuel Up pours from the fuel cans in the pack over time with a glug
    /// until the tank is full or the cans are empty. Code-built uGUI in the dog-menu style.</summary>
    public sealed class ASHVehicleMenu : MonoBehaviour
    {
        // Domain reload is off on Enter Play Mode: reset statics so nothing leaks between Play sessions.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHVehicleMenu()
        {
            _i = default;
        }

        public const string PanelId = "vehicle";
        static ASHVehicleMenu _i;
        public static bool IsOpen => _i && _i._root && _i._root.gameObject.activeSelf;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook() => ASHVehicle.OpenMenu = Open;

        public static bool Open(ASHVehicle v, ASHInventory inv, Transform player)
        {
            if (!v) return false;
            if (!inv) inv = FindAnyObjectByType<ASHInventory>(); if (!inv) return false;
            if (!_i) { var go = new GameObject("ASH_VehicleMenu"); _i = go.AddComponent<ASHVehicleMenu>(); _i.Build(); }
            var hud = FindAnyObjectByType<ASHHud>(); if (hud) hud.CloseAllPanels();
            _i._v = v; _i._inv = inv; _i._player = player; _i._openFrame = Time.frameCount; _i._pouring = false;
            _i._root.gameObject.SetActive(true); ASHUIState.SetOpen(PanelId, true); _i.Refresh();
            return true;
        }
        public static void CloseIfOpen() { if (IsOpen) _i.Close(); }

        ASHVehicle _v; ASHInventory _inv; Transform _player; int _openFrame; bool _pouring; float _poured;
        RectTransform _root, _fuelFill, _repairList; Text _title, _status, _fuelTxt, _fuelBtnTxt; Image _fuelFillImg; AudioSource _glug;

        static RectTransform TL(string n, Transform p, float x, float y, float w, float h) => K.At(n, p, new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));

        void Build()
        {
            var cv = gameObject.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 60;
            var sc = gameObject.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var ct = (RectTransform)transform;
            _root = K.At("VehicleMenu", ct, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(720, 470)); K.Image(_root, K.Panel, true);
            K.Image(K.At("Accent", _root, new Vector2(0.5f, 1), Vector2.zero, new Vector2(720, 3)), K.Accent);
            _title = K.Text(TL("Title", _root, 28, 14, 560, 40), "", 30, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            K.Button(K.At("Close", _root, new Vector2(1, 1), new Vector2(-20, -16), new Vector2(46, 38)), "X", 20, Close, new Color(1, 1, 1, 0.15f));

            K.Text(TL("FuelLbl", _root, 28, 66, 80, 24), "FUEL", 17, K.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            var bg = TL("FuelBar", _root, 100, 72, 330, 14); K.Image(bg, new Color(1, 1, 1, 0.1f));
            _fuelFill = K.Stretch("Fill", bg); _fuelFillImg = K.Image(_fuelFill, K.Accent); _fuelFill.anchorMax = new Vector2(0, 1);
            _fuelTxt = K.Text(TL("FuelTxt", _root, 444, 66, 250, 24), "", 16, K.Ink);
            _status = K.Text(TL("Status", _root, 28, 100, 330, 150), "", 16, K.Ink, TextAnchor.UpperLeft);

            float y = 262;
            K.Button(TL("Drive", _root, 28, y, 200, 48), "DRIVE  [E]", 19, Drive);
            var fb = K.Button(TL("Fuel", _root, 238, y, 200, 48), "FUEL UP", 19, ToggleFuel, new Color(1, 1, 1, 0.18f));
            _fuelBtnTxt = fb.GetComponentInChildren<Text>();
            K.Text(TL("Hint", _root, 28, y + 60, 330, 80), "<color=#a69e91>Fuel cans in your pack fill the tank. Repairs use the part shown, from your pack.</color>", 14, K.Dim, TextAnchor.UpperLeft);

            K.Text(TL("RepLbl", _root, 380, 100, 320, 24), "REPAIR", 18, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _repairList = K.ScrollList(TL("Repair", _root, 380, 128, 320, 320), 3f);
            _root.gameObject.SetActive(false);

            _glug = gameObject.AddComponent<AudioSource>(); _glug.loop = true; _glug.playOnAwake = false; _glug.spatialBlend = 0f; _glug.volume = 0.55f; _glug.clip = GlugClip();
        }

        void OnDestroy() { if (_i == this) { _i = null; ASHUIState.SetOpen(PanelId, false); } }

        void Close() { StopPour(); if (_root) _root.gameObject.SetActive(false); ASHUIState.SetOpen(PanelId, false); }

        void Drive() { var v = _v; Close(); if (v) v.Enter(); }

        void ToggleFuel()
        {
            if (_pouring) { StopPour(); return; }
            if (!_v || !_v.Fuel) return;
            if (_v.Fuel.litres >= _v.Fuel.capacityLitres - 0.05f) { Toast("The tank is full."); return; }
            if (ASHFuelCan.TotalLitres(_inv) <= 0.01f) { Toast("You have no fuel. Find a fuel can."); return; }
            _pouring = true; _poured = 0f; if (_glug.clip) _glug.Play();
        }
        void StopPour()
        {
            if (_pouring && _poured > 0.05f) Toast($"Poured {_poured:0.0} L into the {(_v ? _v.displayName.ToLowerInvariant() : "tank")}.");
            _pouring = false; if (_glug && _glug.isPlaying) _glug.Stop();
        }

        void Update()
        {
            if (!IsOpen) return;
            var stats = _player ? _player.GetComponentInParent<ASH.Survival.ASHSurvivalStats>() : null;
            bool far = !_v || ASHVehicle.Driving || (_player && _v.exitPoint && (_player.position - _v.exitPoint.position).sqrMagnitude > 25f);
            var kb = Keyboard.current;
            if (far || (stats && stats.IsDead) || ASHUIState.IsOpen(ASHPanels.Inventory) || ASHUIState.IsOpen(ASHPanels.Journal) || ASHUIState.IsOpen(ASHPanels.Map) || ASHUIState.IsOpen(ASHPanels.Death)) { Close(); return; }
            if (kb != null && kb.eKey.wasPressedThisFrame && Time.frameCount > _openFrame + 1) { Drive(); return; }
            if (_pouring && _v && _v.Fuel)
            {
                var f = _v.Fuel; float room = f.capacityLitres - f.litres;
                float got = ASHFuelCan.Pour(_inv, Mathf.Min(room, f.pourLitresPerSecond * Time.deltaTime));
                f.Refuel(got); _poured += got;
                if (got <= 1e-5f || f.litres >= f.capacityLitres - 0.01f) { if (ASHFuelCan.TotalLitres(_inv) <= 0.01f && f.litres < f.capacityLitres - 0.01f) Toast("The cans are empty."); StopPour(); }
            }
            Refresh();
        }

        string _sig;
        void Refresh()
        {
            if (!_v) return;
            var f = _v.Fuel; var c = _v.Condition;
            _title.text = _v.displayName.ToUpperInvariant();
            float fr = f ? f.Fraction : 0f;
            _fuelFill.anchorMax = new Vector2(fr, 1); _fuelFill.offsetMin = _fuelFill.offsetMax = Vector2.zero; _fuelFillImg.color = fr < 0.15f ? K.Bad : K.Accent;
            float cans = ASHFuelCan.TotalLitres(_inv);
            _fuelTxt.text = f ? $"{f.litres:0.0} / {f.capacityLitres:0} L   <color=#a69e91>cans {cans:0.0} L</color>" : "-";
            _fuelBtnTxt.text = _pouring ? "STOP" : "FUEL UP";
            var sb = new StringBuilder();
            if (c)
            {
                sb.AppendLine(c.batteryInstalled ? $"Battery  {(c.BatteryOk ? "<color=#9fd39b>" : "<color=#f2806b>")}{c.batteryCharge * 100f:0}%</color>" : "Battery  <color=#f2806b>missing</color>");
                if (c.HasTires) sb.AppendLine(c.MissingTireCount == 0 ? "Tires  <color=#9fd39b>good</color>" : $"Tires  <color=#f2806b>{c.MissingTireCount} missing</color>");
                sb.AppendLine(c.beltInstalled ? $"Fan belt  {(c.BeltOk ? "<color=#9fd39b>good</color>" : "<color=#f2806b>worn out</color>")}" : "Fan belt  <color=#f2806b>missing</color>");
            }
            if (f) sb.AppendLine($"Fuel  {(f.Empty ? "<color=#f2806b>empty</color>" : $"{fr * 100f:0}%")}");
            string why = c ? c.WhyNoStart(f) : null;
            sb.AppendLine(why == null ? "<color=#9fd39b>Ready to start.</color>" : $"<color=#f2806b>{why}</color>");
            _status.text = sb.ToString();

            string sig = (c ? $"{c.batteryInstalled}{c.BatteryOk}{c.beltInstalled}{c.BeltOk}" : "") + (f ? f.Empty.ToString() : "") + Count(c ? c.BatteryItem : null) + "/" + Count(c ? c.BeltItem : null) + "/" + Count(c ? c.TireItem : null) + "/" + (c ? c.MissingMask : 0) + "/" + (cans > 0.01f);
            if (sig == _sig) return; _sig = sig;
            K.Clear(_repairList);
            if (c)
            {
                RepairRow("Battery", c.BatteryItem, !c.BatteryOk, c.batteryInstalled ? "flat" : "missing", () => c.InstallBattery(1f));
                RepairRow("Fan belt", c.BeltItem, !c.BeltOk, c.beltInstalled ? "worn out" : "missing", () => c.InstallBelt(1f));
                if (c.HasTires) for (int i = 0; i < c.wheelSlots.Count; i++) { int w = i; if (c.TireMissing(w)) RepairRow(ASHVehicleCondition.WheelName(w) + " tire", c.TireItem, true, "missing", () => c.InstallTire(w)); }
            }
            if (f)
            {
                var row = K.Rect("Row", _repairList); K.Layout(row, 46); K.Image(row, K.Row, true);
                K.Text(K.Stretch("Name", row, 12, 0, 120, 0), $"Fuel  <size=13><color=#a69e91>needs a fuel can</color></size>", 16, K.Ink);
                K.Text(K.Stretch("Right", row, 0, 0, 10, 0), f.Empty ? "<color=#f2806b>empty</color>" : "<color=#9fd39b>ok</color>", 15, K.Ink, TextAnchor.MiddleRight);
            }
        }

        int Count(ASHItemDefinition it) => it && _inv ? _inv.Count(it) : 0;

        void RepairRow(string part, ASHItemDefinition need, bool broken, string state, System.Action fix)
        {
            var row = K.Rect("Row", _repairList); K.Layout(row, 46); var img = K.Image(row, K.Row, true);
            int have = Count(need);
            if (need) { var tn = need; ASHItemTooltip.Attach(row, () => { var i = ASHItemTooltip.Info.Item(tn, have); i.note = $"Fits the {part.ToLowerInvariant()}"; return i; }); }
            if (need) { var ic = K.Image(K.At("Icon", row, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(36, 36)), need.icon ? Color.white : need.uiTint); if (need.icon) { ic.sprite = need.icon; ic.preserveAspect = true; } }
            string needTxt = need ? $"needs {need.displayName}{(broken ? (have > 0 ? $" <color=#9fd39b>(have {have})</color>" : " <color=#f2806b>(none)</color>") : "")}" : "";
            K.Text(K.Stretch("Name", row, 50, 0, 100, 0), $"{part}  <size=13><color=#a69e91>{needTxt}</color></size>", 16, K.Ink);
            if (!broken) { K.Text(K.Stretch("Right", row, 0, 0, 10, 0), "<color=#9fd39b>ok</color>", 15, K.Ink, TextAnchor.MiddleRight); return; }
            if (have > 0)
            {
                var b = row.gameObject.AddComponent<Button>(); b.targetGraphic = img;
                b.onClick.AddListener(() => { if (_inv.Remove(need, 1)) { fix(); Toast($"{part} fitted."); _sig = null; } });
                K.Text(K.Stretch("Right", row, 0, 0, 10, 0), "<color=#e0b36a>REPAIR</color>", 15, K.Ink, TextAnchor.MiddleRight);
            }
            else K.Text(K.Stretch("Right", row, 0, 0, 10, 0), $"<color=#f2806b>{state}</color>", 15, K.Ink, TextAnchor.MiddleRight);
        }

        void Toast(string m) { var h = FindAnyObjectByType<ASHHud>(); if (h) h.Toast(m); }

        /// <summary>Synthesised liquid glug loop (bubble chirps over a soft pour hiss), no external asset needed.</summary>
        static AudioClip GlugClip()
        {
            const int rate = 22050; float len = 2f; int n = (int)(rate * len); var d = new float[n];
            var rnd = new System.Random(7); float lp = 0f;
            for (int i = 0; i < n; i++) { float w = (float)(rnd.NextDouble() * 2 - 1); lp += (w - lp) * 0.08f; d[i] = lp * 0.25f; }
            float t0 = 0f;
            while (t0 < len - 0.15f)
            {
                float f0 = 120f + (float)rnd.NextDouble() * 160f, dur = 0.07f + (float)rnd.NextDouble() * 0.07f, amp = 0.35f + (float)rnd.NextDouble() * 0.35f;
                int s0 = (int)(t0 * rate), sn = (int)(dur * rate); float ph = 0f;
                for (int k = 0; k < sn && s0 + k < n; k++)
                {
                    float t = k / (float)rate, fr = f0 * (1f + 2.2f * t / dur);
                    ph += 2f * Mathf.PI * fr / rate;
                    d[s0 + k] += Mathf.Sin(ph) * amp * Mathf.Exp(-t / (dur * 0.35f)) * Mathf.Clamp01(t * 400f);
                }
                t0 += 0.11f + (float)rnd.NextDouble() * 0.12f;
            }
            int fade = rate / 50; for (int i = 0; i < fade; i++) { float g = i / (float)fade; d[i] *= g; d[n - 1 - i] *= g; }
            var clip = AudioClip.Create("ASH_Glug", n, 1, rate, false); clip.SetData(d, 0); return clip;
        }
    }
}
