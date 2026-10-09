// Runtime placeholder textures used when an art key has no PNG. Also provides small utility
// textures (soft glow, white pixel, rounded panels) for rendering and UI.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class ProceduralArt
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D White
        {
            get
            {
                if (Cache.TryGetValue("__white", out var t) && t != null) return t;
                t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "white", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                t.SetPixels32(px);
                t.Apply(false, true);
                Cache["__white"] = t;
                return t;
            }
        }

        /// <summary>Soft radial white glow (alpha falloff), used for light fallbacks and effects.</summary>
        public static Texture2D Glow
        {
            get
            {
                if (Cache.TryGetValue("__glow", out var t) && t != null) return t;
                const int n = 128;
                t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "glow", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                    }
                t.SetPixels32(px);
                t.Apply(false, true);
                Cache["__glow"] = t;
                return t;
            }
        }

        /// <summary>Value-type cache key for RoundedRect (no string formatting / boxing per lookup).</summary>
        readonly struct RoundedKey : System.IEquatable<RoundedKey>
        {
            readonly int size, radius, borderWidth;
            readonly Color fill, border, shade;
            readonly bool hasShade;

            public RoundedKey(int size, int radius, Color fill, Color border, int borderWidth, Color? shade)
            {
                this.size = size;
                this.radius = radius;
                this.borderWidth = borderWidth;
                this.fill = fill;
                this.border = border;
                hasShade = shade.HasValue;
                this.shade = shade ?? default;
            }

            public bool Equals(RoundedKey o) =>
                size == o.size && radius == o.radius && borderWidth == o.borderWidth && hasShade == o.hasShade
                && fill.Equals(o.fill) && border.Equals(o.border) && shade.Equals(o.shade);

            public override bool Equals(object obj) => obj is RoundedKey o && Equals(o);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = size;
                    h = h * 31 + radius;
                    h = h * 31 + borderWidth;
                    h = h * 31 + (hasShade ? 1 : 0);
                    h = h * 31 + fill.GetHashCode();
                    h = h * 31 + border.GetHashCode();
                    h = h * 31 + shade.GetHashCode();
                    return h;
                }
            }
        }

        static readonly Dictionary<RoundedKey, Texture2D> RoundedCache = new Dictionary<RoundedKey, Texture2D>();

        /// <summary>Rounded rectangle for 9-sliced UI panels (use GUIStyle.border = radius+border).
        /// Cached by value; lookups do not allocate, but hot draw paths should still keep the result in a field.</summary>
        public static Texture2D RoundedRect(int size, int radius, Color fill, Color border, int borderWidth, Color? shade = null)
        {
            var key = new RoundedKey(size, radius, fill, border, borderWidth, shade);
            if (RoundedCache.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "rounded", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float outer = Mathf.Clamp01(radius - d + 0.5f);
                    float inner = Mathf.Clamp01(radius - borderWidth - d + 0.5f);
                    var f = fill;
                    if (shade.HasValue) f = Color.Lerp(shade.Value, fill, (float)y / size);
                    var c = Color.Lerp(border, f, inner);
                    c.a *= outer;
                    px[y * size + x] = c;
                }
            t.SetPixels(px);
            t.Apply(false, true);
            RoundedCache[key] = t;
            return t;
        }

        public static Texture2D Make(string key, string category)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            Texture2D t;
            var hue = (Mathf.Abs((key ?? "").GetHashCode()) % 1000) / 1000f;
            switch (category)
            {
                case "Background": t = Hills(key, hue); break;
                case "Ground": t = Ground(hue); break;
                case "Character":
                case "Creature": t = Figure(hue, category == "Creature"); break;
                case "Portrait": t = Disc(Color.HSVToRGB(hue, 0.35f, 0.85f), 128); break;
                case "Effect": t = Glow; break;
                case "Icon": t = Disc(Color.white, 128); break;
                case "UI": t = Flat(new Color(0.98f, 0.94f, 0.85f)); break;
                case "Foreground": t = Tuft(hue); break;
                default: t = Blob(hue); break;
            }
            t.name = "placeholder_" + key;
            Cache[key] = t;
            return t;
        }

        static Texture2D New(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };

        static Texture2D Flat(Color c)
        {
            var t = New(8, 8);
            var px = new Color[64];
            for (int i = 0; i < 64; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }

        static Texture2D Disc(Color c, int n)
        {
            var t = New(n, n);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * n * 0.5f);
                    px[y * n + x] = new Color(c.r, c.g, c.b, a);
                }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }

        static Texture2D Hills(string key, float hue)
        {
            const int w = 512, h = 256;
            var t = New(w, h);
            var px = new Color[w * h];
            bool far = key.Contains("mountain") || key.Contains("far") || key.Contains("cloud");
            var baseCol = far ? new Color(0.62f, 0.68f, 0.82f) : new Color(0.55f, 0.74f, 0.52f);
            if (key.Contains("forest")) baseCol = new Color(0.36f, 0.52f, 0.42f);
            if (key.Contains("cloud")) baseCol = new Color(1f, 1f, 1f, 0.85f);
            float k = Mathf.PI * 2f / w;
            for (int x = 0; x < w; x++)
            {
                // integer frequencies keep the loop seamless
                float top = h * (0.55f + 0.18f * Mathf.Sin(x * k * 2 + hue * 6f) + 0.08f * Mathf.Sin(x * k * 5 + 1.3f) + 0.03f * Mathf.Sin(x * k * 13));
                for (int y = 0; y < h; y++)
                {
                    float a = Mathf.Clamp01((top - y) * 0.5f);
                    float shade = 0.85f + 0.15f * (y / top);
                    px[y * w + x] = new Color(baseCol.r * shade, baseCol.g * shade, baseCol.b * shade, a * baseCol.a);
                }
            }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }

        static Texture2D Ground(float hue)
        {
            const int n = 128;
            var t = New(n, n);
            var px = new Color[n * n];
            var c0 = new Color(0.56f, 0.72f, 0.45f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.12f + Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.05f;
                    px[y * n + x] = new Color(c0.r + v - 0.08f, c0.g + v - 0.08f, c0.b + v * 0.5f - 0.05f, 1f);
                }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }

        static Texture2D Figure(float hue, bool creature)
        {
            const int w = 128, h = 256;
            var t = New(w, h);
            var px = new Color[w * h];
            var body = Color.HSVToRGB(hue, 0.45f, 0.8f);
            var skin = new Color(0.98f, 0.86f, 0.74f);
            var ink = new Color(0.2f, 0.16f, 0.22f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fx = (x + 0.5f) / w - 0.5f, fy = (y + 0.5f) / h;
                    Color c = Color.clear;
                    if (creature)
                    {
                        float d = Mathf.Sqrt(fx * fx * 4f + (fy - 0.35f) * (fy - 0.35f) * 6f);
                        if (d < 0.62f) c = d > 0.57f ? ink : body;
                    }
                    else
                    {
                        float bodyD = Mathf.Abs(fx) * 3.2f + Mathf.Max(0, fy - 0.62f) * 4f + Mathf.Max(0, 0.06f - fy) * 8f;
                        if (fy > 0.04f && fy < 0.64f && bodyD < 0.9f) c = bodyD > 0.82f ? ink : body;
                        float hd = Mathf.Sqrt(fx * fx * 9f + (fy - 0.78f) * (fy - 0.78f) * 9f * 4f);
                        if (hd < 0.62f) c = hd > 0.55f ? ink : skin;
                    }
                    px[y * w + x] = c;
                }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }

        static Texture2D Blob(float hue)
        {
            const int n = 128;
            var t = New(n, n);
            var px = new Color[n * n];
            var c = Color.HSVToRGB(hue, 0.3f, 0.75f);
            var ink = new Color(0.22f, 0.18f, 0.2f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n - 0.5f, fy = (y + 0.5f) / n;
                    float d = Mathf.Max(Mathf.Abs(fx) * 2.2f, Mathf.Abs(fy - 0.5f) * 2.2f);
                    px[y * n + x] = d < 0.98f ? (d > 0.92f ? ink : c * (0.85f + 0.15f * fy)) : Color.clear;
                }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }

        static Texture2D Tuft(float hue)
        {
            const int n = 128;
            var t = New(n, n);
            var px = new Color[n * n];
            var c = new Color(0.42f, 0.62f, 0.36f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n, fy = (y + 0.5f) / n;
                    float blade = Mathf.Abs(Mathf.Sin(fx * 31f + hue * 9f)) * (1f - Mathf.Abs(fx - 0.5f) * 2f);
                    px[y * n + x] = fy < blade * 0.9f ? c * (0.8f + 0.3f * fy) : Color.clear;
                }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }
    }
}
