using System;
using System.Collections.Generic;
using ASH.Core;
using UnityEngine;
using UnityEngine.UI;
using K = ASH.UI.ASHUIKit;
using W = ASH.UI.ASHMenuWidgets;

namespace ASH.UI
{
    /// <summary>Settings screen (graphics, audio, controls, difficulty) shared by the main and pause menus. Values apply live
    /// and are stored in PlayerPrefs (ASHGameSettings); Back closes it.</summary>
    public sealed class ASHSettingsPanel : MonoBehaviour
    {
        Action _onBack; RectTransform _list; string _tab = "Graphics";
        readonly Dictionary<string, Image> _tabs = new Dictionary<string, Image>();

        public static ASHSettingsPanel Create(RectTransform parent, Action onBack)
        {
            var rt = K.At("Settings", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 780));
            K.Image(rt, K.Panel, true);
            var p = rt.gameObject.AddComponent<ASHSettingsPanel>(); p._onBack = onBack; p.Build(rt);
            rt.gameObject.SetActive(false);
            return p;
        }

        void Build(RectTransform rt)
        {
            K.Image(K.At("Accent", rt, new Vector2(0.5f, 1), Vector2.zero, new Vector2(1100, 3)), K.Accent);
            K.Text(K.At("Title", rt, new Vector2(0, 1), new Vector2(30, -18), new Vector2(400, 44)), "SETTINGS", 32, K.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            float x = 300;
            foreach (var t in new[] { "Graphics", "Audio", "Controls", "Difficulty" })
            {
                var tab = t; var b = K.At("Tab" + t, rt, new Vector2(0, 1), new Vector2(x, -22), new Vector2(180, 40)); x += 190;
                K.Button(b, t.ToUpperInvariant(), 18, () => { _tab = tab; Rebuild(); }, new Color(1, 1, 1, 0.12f));
                _tabs[t] = b.GetComponent<Image>();
            }
            _list = K.ScrollList(K.Stretch("List", rt, 30, 90, 30, 84), 6);
            K.Button(K.At("Back", rt, new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(260, 52)), "BACK", 22, () => { ASHGameSettings.Apply(false); gameObject.SetActive(false); _onBack?.Invoke(); });
        }

        public void Show() { gameObject.SetActive(true); transform.SetAsLastSibling(); Rebuild(); }

        void Rebuild()
        {
            foreach (var kv in _tabs) kv.Value.color = kv.Key == _tab ? W.ButtonHi : new Color(1, 1, 1, 0.12f);
            K.Clear(_list);
            switch (_tab)
            {
                case "Graphics": Graphics(); break;
                case "Audio": Audio(); break;
                case "Controls": Controls(); break;
                default: Difficulty(); break;
            }
        }

        static string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";

        void Graphics()
        {
            var q = QualitySettings.names;
            W.StepperRow(_list, "Quality preset", q, ASHGameSettings.Quality, i => { ASHGameSettings.Quality = i; ASHGameSettings.Apply(false); });
            var res = ASHGameSettings.Resolutions; var names = new string[res.Length + 1]; names[0] = "Current";
            for (int i = 0; i < res.Length; i++) names[i + 1] = $"{res[i].width} x {res[i].height}";
            W.StepperRow(_list, "Resolution", names, ASHGameSettings.ResolutionIndex + 1, i => { ASHGameSettings.ResolutionIndex = i - 1; ASHGameSettings.Apply(true); });
            W.ToggleRow(_list, "Fullscreen", ASHGameSettings.Fullscreen, v => { ASHGameSettings.Fullscreen = v; ASHGameSettings.Apply(true); });
            W.ToggleRow(_list, "V-Sync", ASHGameSettings.VSync, v => { ASHGameSettings.VSync = v; ASHGameSettings.Apply(false); });
            int[] caps = { 0, 30, 45, 60, 75, 90, 120, 144, 165, 240 };
            var capNames = Array.ConvertAll(caps, c => c == 0 ? "Unlimited" : c + " fps");
            W.StepperRow(_list, "FPS cap", capNames, Mathf.Max(0, Array.IndexOf(caps, ASHGameSettings.FpsCap)), i => { ASHGameSettings.FpsCap = caps[i]; ASHGameSettings.Apply(false); });
            W.Header(_list, "World");
            W.StepperRow(_list, "Fog quality", new[] { "Scene default", "Low", "Medium", "High", "Ultra" }, ASHGameSettings.FogQuality + 1, i => { ASHGameSettings.FogQuality = i - 1; ASHGameSettings.Apply(false); });
            W.StepperRow(_list, "Snow / ash quality", new[] { "Scene default", "Off", "Low", "Medium", "High", "Ultra", "Extreme" }, ASHGameSettings.SnowQuality + 1, i => { ASHGameSettings.SnowQuality = i - 1; ASHGameSettings.Apply(false); });
            W.StepperRow(_list, "Snow banks", new[] { "Scene default", "Off", "On" }, ASHGameSettings.SnowBanks + 1, i => { ASHGameSettings.SnowBanks = i - 1; ASHGameSettings.Apply(false); });
            W.SliderRow(_list, "Weather particles", 0.1f, 1.5f, ASHGameSettings.ParticleAmount, Pct, v => { ASHGameSettings.ParticleAmount = v; ASHGameSettings.Apply(false); });
        }

        void Audio()
        {
            W.SliderRow(_list, "Master", 0f, 1f, ASHGameSettings.MasterVolume, Pct, v => { ASHGameSettings.MasterVolume = v; ASHGameSettings.Apply(false); });
            W.SliderRow(_list, "Music", 0f, 1f, ASHGameSettings.MusicVolume, Pct, v => { ASHGameSettings.MusicVolume = v; ASHGameSettings.Apply(false); });
            W.SliderRow(_list, "Sound effects", 0f, 1f, ASHGameSettings.SfxVolume, Pct, v => { ASHGameSettings.SfxVolume = v; ASHGameSettings.Apply(false); });
            W.SliderRow(_list, "Ambience (wind, rain)", 0f, 1f, ASHGameSettings.AmbienceVolume, Pct, v => { ASHGameSettings.AmbienceVolume = v; ASHGameSettings.Apply(false); });
        }

        void Controls()
        {
            W.SliderRow(_list, "Mouse sensitivity", 0.1f, 3f, ASHGameSettings.MouseSensitivity, v => $"{v:0.00}x", v => { ASHGameSettings.MouseSensitivity = v; ASHGameSettings.Apply(false); });
            W.ToggleRow(_list, "Invert Y", ASHGameSettings.InvertY, v => { ASHGameSettings.InvertY = v; ASHGameSettings.Apply(false); });
        }

        void Difficulty()
        {
            W.SliderRow(_list, "Ash exposure rate", 0.25f, 3f, ASHGameSettings.ExposureRate, v => $"{v:0.00}x", v => { ASHGameSettings.ExposureRate = v; ASHGameSettings.Apply(false); });
            var row = K.Rect("Note", _list); K.Layout(row, 70);
            K.Text(K.Stretch("T", row, 6, 0, 6, 0), "How fast ash and smoke hurt you outside without shelter. 1x = as designed; lower is easier.", 17, K.Dim, TextAnchor.UpperLeft);
        }
    }
}
