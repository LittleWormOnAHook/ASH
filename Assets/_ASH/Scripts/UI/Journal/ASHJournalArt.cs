using System;
using UnityEngine;

namespace ASH.UI
{
    /// <summary>Journal chrome art (Resources/ASH_JournalArt): Rook's 17 PNGs plus the sheet-2 cut-outs (ASH_Atlas_*, ASH_UI_Sheet2_*), in
    /// artFolder (default Assets/_ASH/Art/ASH_ICON_FINAL/Journal). Entries are object references (GUIDs), so moving the folder doesn't break
    /// them; the Editor fills empty entries by file name (artFolder first, then anywhere) via ASH/Systems/Journal and an import hook.
    /// Import settings are never changed: each entry keeps the Texture2D and, if it was imported as a sprite, the Sprite too; anything
    /// missing falls back to a generated parchment stand-in in Grandpa's page colours. Art quirks are handled here (sub-rects, 9-slice).</summary>
    [CreateAssetMenu(menuName = "ASH/UI/Journal Art", fileName = "ASH_JournalArt")]
    public sealed class ASHJournalArt : ScriptableObject
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHJournalArt() { _i = null; _standPaper = null; _standPanel = null; _cache = null; }

        static ASHJournalArt _i;
        public static ASHJournalArt I { get { if (!_i) { _i = Resources.Load<ASHJournalArt>("ASH_JournalArt"); if (!_i) _i = CreateInstance<ASHJournalArt>(); } return _i; } }

        public enum TabStyle { Sheet2PaperTabs, RibbonAndMotif }
        public enum CornerStyle { Sheet2Brass, Sheet2Silver, PageCorner, None }

        [Tooltip("Where the journal PNGs live (used only to find art by file name for empty entries; links themselves follow the asset GUID).")]
        public string artFolder = "Assets/_ASH/Art/ASH_ICON_FINAL/Journal";
        [Tooltip("Side tabs: the sheet-2 paper tabs with coloured corners (default), or Rook's tinted ribbon + ink stamp.")]
        public TabStyle tabStyle = TabStyle.Sheet2PaperTabs;
        [Tooltip("Page / panel corners: sheet-2 metal caps (brass or silver) or Rook's burnt page corner.")]
        public CornerStyle cornerStyle = CornerStyle.Sheet2Brass;

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("File name in Assets/_ASH/Art/UI/Journal (without .png).")] public string file;
            public Texture2D texture;
            [Tooltip("Set when the PNG was imported as Sprite (2D and UI).")] public Sprite sprite;
            public bool Present => texture || sprite;
            public Texture2D Tex => sprite ? sprite.texture : texture;
        }

        [Header("Paper and panels")]
        public Entry journalPaper = new Entry { file = "ASH_UI_JournalPaper" };
        public Entry tooltipPanel = new Entry { file = "ASH_UI_TooltipPanel" };
        public Entry slotFrame = new Entry { file = "ASH_UI_SlotFrame" };
        [Header("Page turn arrows")]
        public Entry pageArrowLeft = new Entry { file = "ASH_UI_PageArrow_Left" };
        public Entry pageArrowRight = new Entry { file = "ASH_UI_PageArrow_Right" };
        [Header("Ribbons")]
        public Entry ribbonTab = new Entry { file = "ASH_UI_RibbonTab" };
        public Entry ribbonTabEmpty = new Entry { file = "ASH_UI_RibbonTab_Empty" };
        [Header("Page details")]
        public Entry pageCorner = new Entry { file = "ASH_UI_PageCorner" };
        public Entry pageRule = new Entry { file = "ASH_UI_PageRule" };
        public Entry journalCover = new Entry { file = "ASH_UI_JournalCover" };
        [Header("Tab stamps")]
        public Entry motifItems = new Entry { file = "ASH_UI_Motif_Items" };
        public Entry motifNotes = new Entry { file = "ASH_UI_Motif_Notes" };
        public Entry motifObjectives = new Entry { file = "ASH_UI_Motif_Objectives" };
        public Entry motifSkills = new Entry { file = "ASH_UI_Motif_Skills" };
        public Entry motifPets = new Entry { file = "ASH_UI_Motif_Pets" };
        public Entry motifRook = new Entry { file = "ASH_UI_Motif_Rook" };
        public Entry motifGuide = new Entry { file = "ASH_UI_Motif_Guide" };

        [Header("Sheet 2: section tabs (ASH_Atlas_Tab_*)")]
        public Entry tabItems = new Entry { file = "ASH_Atlas_Tab_Items" };
        public Entry tabRook = new Entry { file = "ASH_Atlas_Tab_Rook" };
        public Entry tabNotes = new Entry { file = "ASH_Atlas_Tab_Notes" };
        public Entry tabObjectives = new Entry { file = "ASH_Atlas_Tab_Objectives" };
        public Entry tabSkills = new Entry { file = "ASH_Atlas_Tab_Skills" };
        public Entry tabPets = new Entry { file = "ASH_Atlas_Tab_Pets" };
        public Entry tabGuide = new Entry { file = "ASH_Atlas_Tab_Guide" };
        [Header("Sheet 2: ribbons, corners, strips")]
        public Entry ribbonRed = new Entry { file = "ASH_Atlas_Ribbon_Red" };
        public Entry ribbonBlue = new Entry { file = "ASH_Atlas_Ribbon_Blue" };
        public Entry ribbonGreen = new Entry { file = "ASH_Atlas_Ribbon_Green" };
        public Entry cornerBrassLeft = new Entry { file = "ASH_Atlas_Corner_Brass_Left" };
        public Entry cornerBrassRight = new Entry { file = "ASH_Atlas_Corner_Brass_Right" };
        public Entry cornerSilverLeft = new Entry { file = "ASH_Atlas_Corner_Silver_Left" };
        public Entry cornerSilverRight = new Entry { file = "ASH_Atlas_Corner_Silver_Right" };
        public Entry stripNotebook = new Entry { file = "ASH_Atlas_Paper_Torn_Notebook" };
        public Entry stripLong = new Entry { file = "ASH_Atlas_Paper_Torn_Long" };
        public Entry stripFolded = new Entry { file = "ASH_Atlas_Paper_Torn_Folded" };
        public Entry stripStains = new Entry { file = "ASH_Atlas_Paper_Stains" };
        [Header("Sheet 2: flattened page backgrounds (ASH_UI_Sheet2_Page_*)")]
        public Entry pageBlankLeft = new Entry { file = "ASH_UI_Sheet2_Page_Blank_Left" };
        public Entry pageBlankRight = new Entry { file = "ASH_UI_Sheet2_Page_Blank_Right" };
        public Entry pageGridLeft = new Entry { file = "ASH_UI_Sheet2_Page_Grid_Left" };
        public Entry pageGridRight = new Entry { file = "ASH_UI_Sheet2_Page_Grid_Right" };
        public Entry pageMapLeft = new Entry { file = "ASH_UI_Sheet2_Page_Map_Left" };
        public Entry pageMapRight = new Entry { file = "ASH_UI_Sheet2_Page_Map_Right" };
        public Entry pageBotanicalLeft = new Entry { file = "ASH_UI_Sheet2_Page_Botanical_Left" };
        public Entry pageBotanicalRight = new Entry { file = "ASH_UI_Sheet2_Page_Botanical_Right" };

        [Header("Art quirks (pixels, bottom-left origin, in the PNG)")]
        [Tooltip("On: the setup menu (and any re-import of the art) measures ASH_UI_TooltipPanel's painted area and sets the rect and borders below, so a re-saved PNG still slices right.")]
        public bool autoFitTooltipPanel = true;
        [Tooltip("Auto-fit border as a fraction of the painted panel's short side.")] [Range(0.05f, 0.45f)] public float autoFitBorderFraction = 0.19f;
        [Tooltip("ASH_UI_TooltipPanel sub-rect to 9-slice, in imported-texture pixels (width 0 = the whole texture).")]
        public RectInt tooltipPanelRect = new RectInt(0, 0, 0, 0);
        [Tooltip("9-slice borders (left, bottom, right, top) in PNG pixels: 6 px pad + the ~50 px burnt edge and corner flaps.")]
        public Vector4 tooltipPanelBorder = new Vector4(58, 58, 58, 58);
        [Tooltip("ASH_UI_PageRule is now a full-width ink rule (1024x64) with the mountain flourish centred. Off = a plain drawn ink line.")]
        public bool usePageRuleArt = true;

        [Header("Stand-in parchment (used until the PNGs are in)")]
        public Color paperColor = new Color32(0xE7, 0xB4, 0x7B, 0xFF);
        public Color paperEdgeColor = new Color32(0xDA, 0x95, 0x4E, 0xFF);
        public Color burnColor = new Color32(0x5A, 0x34, 0x18, 0xFF);

        public Entry[] All => new[] { journalPaper, tooltipPanel, slotFrame, pageArrowLeft, pageArrowRight, ribbonTab, ribbonTabEmpty, pageCorner, pageRule, journalCover,
            motifItems, motifNotes, motifObjectives, motifSkills, motifPets, motifRook, motifGuide,
            tabItems, tabRook, tabNotes, tabObjectives, tabSkills, tabPets, tabGuide, ribbonRed, ribbonBlue, ribbonGreen,
            cornerBrassLeft, cornerBrassRight, cornerSilverLeft, cornerSilverRight, stripNotebook, stripLong, stripFolded, stripStains,
            pageBlankLeft, pageBlankRight, pageGridLeft, pageGridRight, pageMapLeft, pageMapRight, pageBotanicalLeft, pageBotanicalRight };

        // ------------------------------------------------------------------ runtime sprites
        static System.Collections.Generic.Dictionary<string, Sprite> _cache;
        static Texture2D _standPaper; static Sprite _standPanel;

        /// <summary>Full sprite for an entry (the imported Sprite, else one made from the texture), or null when the PNG isn't in.</summary>
        public static Sprite SpriteOf(Entry e)
        {
            if (e == null || !e.Present) return null;
            if (e.sprite) return e.sprite;
            return Cached("full:" + e.file + e.texture.GetHashCode(), () => Sprite.Create(e.texture, new Rect(0, 0, e.texture.width, e.texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect));
        }

        static Sprite Cached(string key, Func<Sprite> make)
        {
            _cache ??= new System.Collections.Generic.Dictionary<string, Sprite>();
            if (_cache.TryGetValue(key, out var s) && s) return s;
            s = make(); if (s) s.name = key; _cache[key] = s; return s;
        }

        /// <summary>Paper texture for book pages and backgrounds: ASH_UI_JournalPaper, else the generated tile.</summary>
        public static Texture2D Paper { get { var t = I.journalPaper.Tex; return t ? t : StandInPaper(); } }
        public static bool HasPaperArt => I.journalPaper.Present;

        /// <summary>Sliced popup panel: ASH_UI_TooltipPanel cut to its opaque sub-rect, else the stand-in parchment card.</summary>
        public static Sprite TooltipPanel
        {
            get
            {
                var a = I; var t = a.tooltipPanel.Tex;
                if (!t) return StandInPanel();
                var r = a.tooltipPanelRect; if (r.width <= 0 || r.height <= 0) r = new RectInt(0, 0, t.width, t.height);
                r.x = Mathf.Clamp(r.x, 0, t.width - 2); r.y = Mathf.Clamp(r.y, 0, t.height - 2);
                r.width = Mathf.Clamp(r.width, 2, t.width - r.x); r.height = Mathf.Clamp(r.height, 2, t.height - r.y);
                var b = a.tooltipPanelBorder; b.x = Mathf.Clamp(b.x, 0, r.width * 0.49f); b.z = Mathf.Clamp(b.z, 0, r.width * 0.49f); b.y = Mathf.Clamp(b.y, 0, r.height * 0.49f); b.w = Mathf.Clamp(b.w, 0, r.height * 0.49f);
                string key = $"panel:{t.GetHashCode()}:{r}:{b}";
                return Cached(key, () => Sprite.Create(t, new Rect(r.x, r.y, r.width, r.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, b));
            }
        }
        public static Vector4 TooltipBorder => I.tooltipPanel.Present ? I.tooltipPanelBorder : new Vector4(22, 22, 22, 22);

        /// <summary>Full-width page rule (ASH_UI_PageRule) when usePageRuleArt is on and the PNG is in, else null = draw an ink line.</summary>
        public static Sprite PageRule => I.usePageRuleArt ? SpriteOf(I.pageRule) : null;

        public static Sprite SlotFrame => SpriteOf(I.slotFrame);
        public static Sprite ArrowLeft => SpriteOf(I.pageArrowLeft);
        public static Sprite ArrowRight => SpriteOf(I.pageArrowRight);
        public static Sprite Ribbon => SpriteOf(I.ribbonTab);
        public static Sprite RibbonEmpty => SpriteOf(I.ribbonTabEmpty);
        public static Sprite Corner => SpriteOf(I.pageCorner);
        public static Texture2D Cover => I.journalCover.Tex;

        /// <summary>Sheet-2 paper section tab by journal tab name (null when not in).</summary>
        public static Sprite PaperTab(string tab)
        {
            var a = I;
            switch (tab)
            {
                case "Items": return SpriteOf(a.tabItems);
                case "Notes": return SpriteOf(a.tabNotes);
                case "Objectives": return SpriteOf(a.tabObjectives);
                case "Skills": return SpriteOf(a.tabSkills);
                case "Pets": return SpriteOf(a.tabPets);
                case "Character": case "Rook": return SpriteOf(a.tabRook);
                case "Crafting": case "Guide": return SpriteOf(a.tabGuide);
            }
            return null;
        }

        /// <summary>Corner cap for the chosen style (left = top-left art, right = top-right art). Null for None / missing.</summary>
        public static Sprite CornerCap(bool right)
        {
            var a = I;
            switch (a.cornerStyle)
            {
                case CornerStyle.Sheet2Brass: return SpriteOf(right ? a.cornerBrassRight : a.cornerBrassLeft) ?? Corner;
                case CornerStyle.Sheet2Silver: return SpriteOf(right ? a.cornerSilverRight : a.cornerSilverLeft) ?? Corner;
                case CornerStyle.PageCorner: return Corner;
            }
            return null;
        }

        /// <summary>Flattened sheet-2 page background: kind 0 blank, 1 grid, 2 map, 3 botanical. Null when not in.</summary>
        public static Texture2D PageBackground(int kind, bool right)
        {
            var a = I;
            switch (kind)
            {
                case 1: return (right ? a.pageGridRight : a.pageGridLeft).Tex;
                case 2: return (right ? a.pageMapRight : a.pageMapLeft).Tex;
                case 3: return (right ? a.pageBotanicalRight : a.pageBotanicalLeft).Tex;
            }
            return (right ? a.pageBlankRight : a.pageBlankLeft).Tex;
        }

        /// <summary>Tab stamp by journal tab name: Items, Notes, Objectives, Skills, Pets, Character/Rook, Crafting/Guide.</summary>
        public static Sprite Motif(string tab)
        {
            var a = I;
            switch (tab)
            {
                case "Items": return SpriteOf(a.motifItems);
                case "Notes": return SpriteOf(a.motifNotes);
                case "Objectives": return SpriteOf(a.motifObjectives);
                case "Skills": return SpriteOf(a.motifSkills);
                case "Pets": return SpriteOf(a.motifPets);
                case "Character": case "Rook": return SpriteOf(a.motifRook);
                case "Crafting": case "Guide": return SpriteOf(a.motifGuide);
            }
            return null;
        }

        // ------------------------------------------------------------------ stand-ins (generated, nothing written to disk)
        /// <summary>Seamless 256 parchment tile in Grandpa's page colours (wrapped value noise, ash flecks).</summary>
        public static Texture2D StandInPaper()
        {
            if (_standPaper) return _standPaper;
            const int N = 256; var a = I;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "ASH_PaperStandIn" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                float n = Noise(u, v, 8) * 0.55f + Noise(u, v, 24) * 0.3f + Noise(u, v, 64) * 0.15f;
                float fleck = Noise(u, v, 96); fleck = fleck > 0.86f ? (fleck - 0.86f) * 4f : 0f;
                var c = Color.Lerp(a.paperColor, a.paperEdgeColor, Mathf.Clamp01((n - 0.35f) * 1.4f));
                c = Color.Lerp(c, a.burnColor, fleck * 0.5f);
                px[y * N + x] = c;
            }
            tex.SetPixels32(px); tex.Apply(true, false);
            return _standPaper = tex;
        }

        /// <summary>9-sliced stand-in card: the paper tile with a darkened, burnt rim.</summary>
        public static Sprite StandInPanel()
        {
            if (_standPanel) return _standPanel;
            const int N = 128, B = 22; var a = I; var paper = StandInPaper().GetPixels32();
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ASH_PanelStandIn" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            {
                float d = Mathf.Min(Mathf.Min(x, N - 1 - x), Mathf.Min(y, N - 1 - y)); // px from the edge
                float rough = Noise(x / (float)N, y / (float)N, 16) * 5f;
                float edge = Mathf.Clamp01(1f - (d - rough) / 12f);
                Color c = paper[(y * 2 % 256) * 256 + (x * 2 % 256)];
                c = Color.Lerp(c, a.paperEdgeColor, Mathf.Clamp01(edge * 1.6f));
                c = Color.Lerp(c, a.burnColor, Mathf.Clamp01((edge - 0.55f) * 2.2f));
                c.a = Mathf.Clamp01((d - rough * 0.4f) / 2f);
                px[y * N + x] = c;
            }
            tex.SetPixels32(px); tex.Apply(false, false);
            return _standPanel = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(B, B, B, B));
        }

        // value noise on a wrapped lattice (period = cells) so the tile is seamless
        static float Noise(float u, float v, int cells)
        {
            float x = u * cells, y = v * cells; int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float H(int i, int j) { i = ((i % cells) + cells) % cells; j = ((j % cells) + cells) % cells; uint h = (uint)(i * 374761393 + j * 668265263 + cells * 2246822519u); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; }
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), fx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), fx), fy);
        }
    }
}
