// Small procedural textures used by the panels (generated once, cached): the d20 for skill checks, arrow heads for
// talent prerequisites, discs, rings, diamonds, soft gradients and a parchment-tinted map backdrop.
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public static class PanelArt
    {
        static Texture2D d20, arrowHead, disc, ringTex, diamond, gradientUp, gradientDown, gradientRight, sparkle, lockTex, check;

        static Texture2D New(int w, int h, string name)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }

        static bool InsidePoly(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>A d20 seen face-on: ivory-gold hexagon with the facet lines of an icosahedron, ink outline.</summary>
        public static Texture2D D20
        {
            get
            {
                if (d20 != null) return d20;
                const int n = 160;
                var t = New(n, n, "lv_d20");
                var px = new Color[n * n];
                var c = new Vector2(n * 0.5f, n * 0.5f);
                float R = n * 0.46f;
                var hex = new Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float a = Mathf.Deg2Rad * (90f + 60f * i);
                    hex[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;
                }
                // central triangle (pointing up on screen: texture y is up) and its nine facet edges
                float r2 = R * 0.55f;
                var tri = new Vector2[3];
                for (int i = 0; i < 3; i++)
                {
                    float a = Mathf.Deg2Rad * (90f + 120f * i);
                    tri[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r2;
                }
                var ink = new Color(0.17f, 0.13f, 0.22f, 1f);
                var light = new Color(1f, 0.97f, 0.88f, 1f);
                var deep = new Color(0.93f, 0.76f, 0.45f, 1f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        if (!InsidePoly(p, hex))
                        {
                            // anti-aliased rim outside
                            float de = float.MaxValue;
                            for (int i = 0; i < 6; i++) de = Mathf.Min(de, SegDist(p, hex[i], hex[(i + 1) % 6]));
                            float a = Mathf.Clamp01(3.2f - de);
                            px[y * n + x] = new Color(ink.r, ink.g, ink.b, a);
                            continue;
                        }
                        // facet shading: the centre face is brightest, faces lit from the upper left
                        bool inTri = InsidePoly(p, tri);
                        float ang = Mathf.Atan2(p.y - c.y, p.x - c.x) * Mathf.Rad2Deg;
                        float shade = inTri ? 0.08f : 0.35f + 0.25f * (0.5f - 0.5f * Mathf.Cos((ang - 135f) * Mathf.Deg2Rad));
                        var col = Color.Lerp(light, deep, shade);
                        float d = float.MaxValue;
                        for (int i = 0; i < 6; i++) d = Mathf.Min(d, SegDist(p, hex[i], hex[(i + 1) % 6]) - 1.6f);
                        for (int i = 0; i < 3; i++)
                        {
                            d = Mathf.Min(d, SegDist(p, tri[i], tri[(i + 1) % 3]));
                            d = Mathf.Min(d, SegDist(p, tri[i], hex[(2 * i) % 6]));
                            d = Mathf.Min(d, SegDist(p, tri[i], hex[(2 * i + 1) % 6]));
                            d = Mathf.Min(d, SegDist(p, tri[i], hex[(2 * i + 5) % 6]));
                        }
                        float line = Mathf.Clamp01(2.2f - d);
                        px[y * n + x] = Color.Lerp(col, ink, line * 0.85f);
                    }
                t.SetPixels(px);
                t.Apply(false, true);
                return d20 = t;
            }
        }

        /// <summary>White check mark (✓) for checkboxes and completed objectives.</summary>
        public static Texture2D Check
        {
            get
            {
                if (check != null) return check;
                const int n = 48;
                var t = New(n, n, "lv_check");
                var px = new Color[n * n];
                // texture y is up: the short stroke goes down-right, the long one up-right
                var a = new Vector2(9f, 25f);
                var b = new Vector2(20f, 13f);
                var c = new Vector2(40f, 37f);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float d = Mathf.Min(SegDist(p, a, b), SegDist(p, b, c));
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(4.2f - d));
                    }
                t.SetPixels(px);
                t.Apply(false, true);
                return check = t;
            }
        }

        /// <summary>White triangle pointing right (+x).</summary>
        public static Texture2D ArrowHead
        {
            get
            {
                if (arrowHead != null) return arrowHead;
                const int n = 32;
                var t = New(n, n, "lv_arrowhead");
                var px = new Color[n * n];
                var a = new Vector2(3f, 3f);
                var b = new Vector2(29f, 16f);
                var c = new Vector2(3f, 29f);
                var poly = new[] { a, b, c };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float d = Mathf.Min(SegDist(p, a, b), Mathf.Min(SegDist(p, b, c), SegDist(p, c, a)));
                        float alpha = InsidePoly(p, poly) ? 1f : Mathf.Clamp01(1f - d);
                        px[y * n + x] = new Color(1f, 1f, 1f, alpha);
                    }
                t.SetPixels(px);
                t.Apply(false, true);
                return arrowHead = t;
            }
        }

        public static Texture2D Disc
        {
            get
            {
                if (disc != null) return disc;
                const int n = 64;
                var t = New(n, n, "lv_disc");
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(n * 0.5f - 1f - d));
                    }
                t.SetPixels(px);
                t.Apply(false, true);
                return disc = t;
            }
        }

        public static Texture2D RingCircle
        {
            get
            {
                if (ringTex != null) return ringTex;
                const int n = 64;
                var t = New(n, n, "lv_ring");
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                        float d = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - (n * 0.5f - 4f));
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(2.5f - d));
                    }
                t.SetPixels(px);
                t.Apply(false, true);
                return ringTex = t;
            }
        }

        public static Texture2D Diamond
        {
            get
            {
                if (diamond != null) return diamond;
                const int n = 32;
                var t = New(n, n, "lv_diamond");
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Mathf.Abs(x + 0.5f - n * 0.5f) + Mathf.Abs(y + 0.5f - n * 0.5f);
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(n * 0.5f - 1f - d));
                    }
                t.SetPixels(px);
                t.Apply(false, true);
                return diamond = t;
            }
        }

        /// <summary>Four-pointed twinkle (flourishes, lantern motes).</summary>
        public static Texture2D Sparkle
        {
            get
            {
                if (sparkle != null) return sparkle;
                const int n = 64;
                var t = New(n, n, "lv_sparkle");
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = Mathf.Abs(x + 0.5f - n * 0.5f) / (n * 0.5f), dy = Mathf.Abs(y + 0.5f - n * 0.5f) / (n * 0.5f);
                        float star = Mathf.Clamp01(1f - (Mathf.Sqrt(dx) + Mathf.Sqrt(dy)) * 0.95f);
                        float glow = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)) * 0.35f;
                        px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(star * 1.6f + glow * glow));
                    }
                t.SetPixels(px);
                t.Apply(false, true);
                return sparkle = t;
            }
        }

        /// <summary>Vertical alpha gradient: opaque at the bottom, transparent at the top.</summary>
        public static Texture2D GradientUp => gradientUp != null ? gradientUp : (gradientUp = Gradient(true, false));
        /// <summary>Opaque at the top, transparent at the bottom.</summary>
        public static Texture2D GradientDown => gradientDown != null ? gradientDown : (gradientDown = Gradient(false, false));
        /// <summary>Opaque on the left, transparent on the right.</summary>
        public static Texture2D GradientRight => gradientRight != null ? gradientRight : (gradientRight = Gradient(false, true));

        static Texture2D Gradient(bool up, bool horizontal)
        {
            const int n = 64;
            var t = horizontal ? New(n, 2, "lv_grad_h") : New(2, n, "lv_grad_v");
            var px = new Color[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) / n;
                a = a * a * (3f - 2f * a);
                if (horizontal)
                {
                    float v = 1f - a;
                    px[i] = new Color(1f, 1f, 1f, v);
                    px[n + i] = new Color(1f, 1f, 1f, v);
                }
                else
                {
                    // texture rows go bottom → top; GUI draws row 0 at the bottom of the rect
                    float v = up ? 1f - a : a;
                    px[i * 2] = new Color(1f, 1f, 1f, v);
                    px[i * 2 + 1] = new Color(1f, 1f, 1f, v);
                }
            }
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>Padlock (art glyph when available, else a procedural one).</summary>
        public static Texture2D Lock
        {
            get
            {
                if (lockTex != null) return lockTex;
                var g = Ui.GlyphTexture("lock");
                if (g != null) return lockTex = g;
                const int n = 48;
                var t = New(n, n, "lv_lock");
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float fx = x + 0.5f, fy = y + 0.5f;
                        bool body = fx > 10 && fx < 38 && fy > 6 && fy < 26;
                        float dx = fx - 24f, dy = fy - 26f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        bool shackle = fy >= 26 && r > 7f && r < 11.5f;
                        px[y * n + x] = new Color(1f, 1f, 1f, body || shackle ? 1f : 0f);
                    }
                t.SetPixels(px);
                t.Apply(false, true);
                return lockTex = t;
            }
        }
    }
}
