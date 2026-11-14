using UnityEngine;

namespace ASH.Map
{
    /// <summary>
    /// Fog of war mask + overlay texture. Lite port of Genesis Project.Map.MapFogOfWar: soft-edged circle reveals
    /// into a byte mask, dirty-rect texture uploads, save/load bytes. Scanner skills, land masks and profile
    /// calibration removed; adds capsule (path) reveals and a "damaged GPS" static pattern under the fog.
    /// </summary>
    public sealed class ASHMapFog
    {
        public readonly int Resolution;
        public Texture2D Texture { get; }
        readonly byte[] _mask;
        readonly Color32[] _pattern, _pixels;
        int _dx0, _dy0, _dx1, _dy1; bool _dirty;
        readonly Vector2 _worldMin, _worldSize;

        public ASHMapFog(int resolution, Vector2 worldMin, Vector2 worldSize)
        {
            Resolution = Mathf.Clamp(resolution, 64, 2048);
            _worldMin = worldMin; _worldSize = worldSize;
            _mask = new byte[Resolution * Resolution];
            _pattern = BuildPattern(Resolution);
            _pixels = (Color32[])_pattern.Clone();
            Texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false) { name = "ASH_MapFog", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Texture.SetPixels32(_pixels); Texture.Apply(false);
        }

        static Color32[] BuildPattern(int n)
        {
            var px = new Color32[n * n];
            var rnd = new System.Random(1337);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float cloud = Mathf.PerlinNoise(u * 6f, v * 6f) * 0.6f + Mathf.PerlinNoise(u * 23f + 5f, v * 23f) * 0.4f;
                    int blockX = x / 4, blockY = y / 4;
                    float stat = (float)((blockX * 73856093 ^ blockY * 19349663) & 1023) / 1023f;
                    float grain = (float)rnd.NextDouble();
                    bool grid = (x % 32 == 0) || (y % 32 == 0);
                    bool scan = (y % 3) == 0;
                    float l = 0.11f + cloud * 0.07f + stat * 0.045f + grain * 0.03f + (grid ? 0.05f : 0f) - (scan ? 0.02f : 0f);
                    px[y * n + x] = new Color32((byte)(l * 0.85f * 255), (byte)(l * 1.02f * 255), (byte)(l * 0.92f * 255), 255);
                }
            return px;
        }

        Vector2 ToTex(Vector2 world) => new Vector2((world.x - _worldMin.x) / _worldSize.x * Resolution, (world.y - _worldMin.y) / _worldSize.y * Resolution);
        float MetresToTex(float m) => m / _worldSize.x * Resolution;

        public void RevealCircle(Vector2 worldXZ, float radius, float soft = -1f) => RevealSegment(worldXZ, worldXZ, radius, soft);

        /// <summary>Reveals a capsule between two world points (radius = half width).</summary>
        public void RevealSegment(Vector2 aWorld, Vector2 bWorld, float radius, float soft = -1f)
        {
            if (soft < 0f) soft = Mathf.Max(8f, radius * 0.35f);
            Vector2 a = ToTex(aWorld), b = ToTex(bWorld);
            float r = MetresToTex(radius), s = Mathf.Max(0.5f, MetresToTex(soft));
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - r - 1)), x1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + r + 1));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - r - 1)), y1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + r + 1));
            if (x0 > x1 || y0 > y1) return;
            Vector2 ab = b - a; float len2 = Mathf.Max(1e-5f, ab.sqrMagnitude);
            bool any = false;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float d = Vector2.Distance(p, a + ab * t);
                    if (d > r) continue;
                    float v = Mathf.Clamp01((r - d) / s);
                    v = v * v * (3f - 2f * v);
                    int i = y * Resolution + x; byte nv = (byte)(v * 255f);
                    if (nv <= _mask[i]) continue;
                    _mask[i] = nv; WritePixel(i); any = true;
                }
            if (any) Expand(x0, y0, x1, y1);
        }

        public void RevealAll()
        {
            for (int i = 0; i < _mask.Length; i++) { _mask[i] = 255; WritePixel(i); }
            Expand(0, 0, Resolution - 1, Resolution - 1);
        }

        void WritePixel(int i) { var c = _pattern[i]; c.a = (byte)(255 - _mask[i]); _pixels[i] = c; }
        void Expand(int x0, int y0, int x1, int y1)
        {
            if (!_dirty) { _dx0 = x0; _dy0 = y0; _dx1 = x1; _dy1 = y1; _dirty = true; return; }
            _dx0 = Mathf.Min(_dx0, x0); _dy0 = Mathf.Min(_dy0, y0); _dx1 = Mathf.Max(_dx1, x1); _dy1 = Mathf.Max(_dy1, y1);
        }

        /// <summary>Uploads changed pixels (call once per frame at most).</summary>
        public void Upload()
        {
            if (!_dirty) return;
            int w = _dx1 - _dx0 + 1, h = _dy1 - _dy0 + 1;
            var block = new Color32[w * h];
            for (int y = 0; y < h; y++) System.Array.Copy(_pixels, (_dy0 + y) * Resolution + _dx0, block, y * w, w);
            Texture.SetPixels32(_dx0, _dy0, w, h, block); Texture.Apply(false);
            _dirty = false;
        }

        public float Revealed01(Vector2 worldXZ)
        {
            var t = ToTex(worldXZ); int x = Mathf.Clamp((int)t.x, 0, Resolution - 1), y = Mathf.Clamp((int)t.y, 0, Resolution - 1);
            return _mask[y * Resolution + x] / 255f;
        }
        public bool IsRevealed(Vector2 worldXZ, float threshold = 0.35f) => Revealed01(worldXZ) >= threshold;

        public float Coverage01()
        {
            long sum = 0; foreach (var b in _mask) sum += b; return sum / (255f * _mask.Length);
        }

        public byte[] BuildSave() => (byte[])_mask.Clone();
        public void ApplySave(byte[] data)
        {
            if (data == null || data.Length != _mask.Length) return;
            System.Array.Copy(data, _mask, data.Length);
            for (int i = 0; i < _mask.Length; i++) WritePixel(i);
            Expand(0, 0, Resolution - 1, Resolution - 1);
        }
    }
}
