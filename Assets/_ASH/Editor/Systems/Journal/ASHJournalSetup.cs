#if UNITY_EDITOR
using System.IO;
using System.Linq;
using ASH.EditorTools.Systems;
using ASH.UI;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Journal book + item tooltip setup (idempotent, saves only its own assets, never the scene or import settings):
    /// creates Resources/ASH_ItemTooltipSettings and Resources/ASH_JournalArt, and wires every art entry by file name
    /// (artFolder first, then anywhere under Assets/_ASH). References are GUID-based asset refs, so moving the folder keeps them.
    /// The postprocessor re-wires automatically when PNGs in the art folder are imported.
    /// </summary>
    public static class ASHJournalSetup
    {
        public const string TooltipPath = "Assets/_ASH/Resources/ASH_ItemTooltipSettings.asset";
        public const string ArtPath = "Assets/_ASH/Resources/ASH_JournalArt.asset";

        [MenuItem("ASH/Systems/Journal/Setup Item Tooltip And Journal Art (idempotent)", false, 250)]
        public static void Setup()
        {
            ASHMenuInstaller.Folder("Assets/_ASH/Resources");
            var tip = ASHMenuInstaller.Ensure<ASHItemTooltipSettings>(TooltipPath);
            var art = ASHMenuInstaller.Ensure<ASHJournalArt>(ArtPath);
            int wired = Wire(art, out int missing);
            AssetDatabase.SaveAssetIfDirty(tip); AssetDatabase.SaveAssetIfDirty(art);
            Debug.Log($"[ASH Journal] Tooltip settings + journal art ready. Art wired {wired}, missing {missing} (stand-ins used for missing).");
        }

        /// <summary>Fill each entry's texture / sprite from its file name. Returns how many are present.</summary>
        public static int Wire(ASHJournalArt art, out int missing)
        {
            int ok = 0; missing = 0; bool changed = false;
            string folder = string.IsNullOrEmpty(art.artFolder) ? "Assets/_ASH" : art.artFolder.TrimEnd('/');
            foreach (var e in art.All)
            {
                if (e == null || string.IsNullOrEmpty(e.file)) continue;
                string path = Find(folder, e.file);
                Texture2D tex = null; Sprite sp = null;
                if (path != null)
                {
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    sp = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
                }
                if (path == null && e.Present) { ok++; continue; } // keep a manual assignment
                if (e.texture != tex || e.sprite != sp) { e.texture = tex; e.sprite = sp; changed = true; }
                if (e.Present) ok++; else missing++;
            }
            if (FitTooltipPanel(art)) changed = true;
            if (changed) EditorUtility.SetDirty(art);
            return ok;
        }

        /// <summary>Reads the tooltip panel PNG (read only, never written) and sets the 9-slice rect + borders around its painted area.</summary>
        static bool FitTooltipPanel(ASHJournalArt art)
        {
            if (!art.autoFitTooltipPanel) return false;
            var t = art.tooltipPanel.Tex; if (!t) return false;
            string path = AssetDatabase.GetAssetPath(t); if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tmp.LoadImage(File.ReadAllBytes(path), false)) return false;
                var px = tmp.GetPixels32(); int w = tmp.width, h = tmp.height;
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (px[y * w + x].a > 25) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                if (x1 < x0 || y1 < y0) return false;
                x0 = Mathf.Max(0, x0 - 2); y0 = Mathf.Max(0, y0 - 2); x1 = Mathf.Min(w - 1, x1 + 2); y1 = Mathf.Min(h - 1, y1 + 2);
                float sx = t.width / (float)w, sy = t.height / (float)h; // imported size may be capped
                var r = new RectInt(Mathf.RoundToInt(x0 * sx), Mathf.RoundToInt(y0 * sy), Mathf.RoundToInt((x1 - x0 + 1) * sx), Mathf.RoundToInt((y1 - y0 + 1) * sy));
                float bd = Mathf.Round(Mathf.Min(r.width, r.height) * art.autoFitBorderFraction);
                var b = new Vector4(bd, bd, bd, bd);
                if (r.Equals(art.tooltipPanelRect) && b == art.tooltipPanelBorder) return false;
                art.tooltipPanelRect = r; art.tooltipPanelBorder = b; return true;
            }
            finally { Object.DestroyImmediate(tmp); }
        }

        static string Find(string folder, string file)
        {
            string direct = folder + "/" + file + ".png";
            if (File.Exists(direct)) return direct;
            foreach (var root in new[] { folder, "Assets/_ASH" })
            {
                if (!AssetDatabase.IsValidFolder(root)) continue;
                foreach (var g in AssetDatabase.FindAssets(file + " t:Texture2D", new[] { root }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (Path.GetFileNameWithoutExtension(p) == file) return p;
                }
            }
            return null;
        }
    }

    /// <summary>Re-wires ASH_JournalArt when art in its folder is (re)imported, e.g. after switching a PNG to Sprite.</summary>
    sealed class ASHJournalArtPostprocessor : AssetPostprocessor
    {
        static bool _queued;
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (_queued) return;
            bool hit = imported.Concat(moved).Concat(deleted).Any(p => p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase) && p.Contains("/Journal/"));
            if (!hit) return;
            _queued = true;
            EditorApplication.delayCall += () =>
            {
                _queued = false;
                var art = AssetDatabase.LoadAssetAtPath<ASHJournalArt>(ASHJournalSetup.ArtPath);
                if (!art) return; // only after the setup menu has created it
                ASHJournalSetup.Wire(art, out _);
                AssetDatabase.SaveAssetIfDirty(art);
            };
        }
    }
}
#endif
