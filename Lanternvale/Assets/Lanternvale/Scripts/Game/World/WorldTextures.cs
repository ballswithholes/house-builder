// Small procedural textures used by the 3D world (sky discs, stars, glows, particles, water, markers). Generated once
// on first use and kept for the session; owned by the world layer so it does not depend on other builders' art code.
using UnityEngine;

namespace Lanternvale.Game
{
    internal static class WorldTextures
    {
        static Texture2D glow, dot, star, moon, leaf, mist, streak, ring, chevron, water;

        /// <summary>Soft radial glow: white, alpha falls off to the rim (halos, fireflies, embers, sun).</summary>
        public static Texture2D Glow => glow != null ? glow : (glow = Radial("lv_world_glow", 64, d =>
        {
            float a = Mathf.Clamp01(1f - d);
            return a * a * (0.35f + 0.65f * a);
        }));

        /// <summary>Small soft-edged disc (pollen, sparks).</summary>
        public static Texture2D Dot => dot != null ? dot : (dot = Radial("lv_world_dot", 32, d => 1f - Smooth(0.45f, 1f, d)));

        /// <summary>Star: bright core with a faint four-point flare.</summary>
        public static Texture2D Star
        {
            get
            {
                if (star != null) return star;
                const int n = 32;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(u * u + v * v);
                        float core = Mathf.Clamp01(1f - d * 2.6f);
                        core = core * core;
                        float flare = Mathf.Clamp01(1f - Mathf.Abs(u) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(v)) +
                                      Mathf.Clamp01(1f - Mathf.Abs(v) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(u));
                        float a = Mathf.Clamp01(core + flare * 0.35f) * Mathf.Clamp01(1f - d);
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                return star = Make("lv_world_star", n, n, px, TextureWrapMode.Clamp);
            }
        }

        /// <summary>Moon disc with soft maria shading (alpha = disc).</summary>
        public static Texture2D Moon
        {
            get
            {
                if (moon != null) return moon;
                const int n = 128;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(u * u + v * v);
                        float disc = 1f - Smooth(0.86f, 0.94f, d);
                        float halo = Mathf.Clamp01(1f - d) * 0.25f;
                        float maria = Mathf.PerlinNoise(u * 2.6f + 3.1f, v * 2.6f + 7.7f);
                        float shade = 0.82f + 0.18f * Smooth(0.35f, 0.7f, maria);
                        byte c = (byte)(255f * shade);
                        px[y * n + x] = new Color32(c, c, (byte)(c * 0.97f), (byte)(Mathf.Clamp01(disc + halo) * 255f));
                    }
                return moon = Make("lv_world_moon", n, n, px, TextureWrapMode.Clamp);
            }
        }

        /// <summary>Leaf silhouette (white; tinted by vertex colour), pointing up (+V).</summary>
        public static Texture2D Leaf
        {
            get
            {
                if (leaf != null) return leaf;
                const int n = 64;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n; // v 0 = stem … 1 = tip
                        float half = Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI) * 0.62f * (1f - 0.25f * v);
                        float inside = 1f - Smooth(half - 0.08f, half + 0.02f, Mathf.Abs(u));
                        float stem = v < 0.16f ? (1f - Smooth(0.03f, 0.07f, Mathf.Abs(u))) : 0f;
                        float a = Mathf.Max(inside, stem);
                        float rib = 1f - 0.22f * (1f - Smooth(0.0f, 0.05f, Mathf.Abs(u)));
                        byte c = (byte)(255f * rib * (0.88f + 0.12f * v));
                        px[y * n + x] = new Color32(c, c, c, (byte)(Mathf.Clamp01(a) * 255f));
                    }
                return leaf = Make("lv_world_leaf", n, n, px, TextureWrapMode.Clamp);
            }
        }

        /// <summary>Soft cloud-like puff with a broken edge (mist banks).</summary>
        public static Texture2D Mist
        {
            get
            {
                if (mist != null) return mist;
                const int w = 128, h = 64;
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                        float d = Mathf.Sqrt(u * u + v * v * 1.1f);
                        float n1 = Mathf.PerlinNoise(u * 3.1f + 11.3f, v * 3.1f + 4.2f);
                        float a = (1f - Smooth(0.25f, 1f, d + (n1 - 0.5f) * 0.45f)) * (0.75f + 0.25f * n1);
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                    }
                return mist = Make("lv_world_mist", w, h, px, TextureWrapMode.Clamp);
            }
        }

        /// <summary>Thin vertical streak (rain), bright in the middle, fading at both ends.</summary>
        public static Texture2D Streak
        {
            get
            {
                if (streak != null) return streak;
                const int w = 8, h = 64;
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                        float a = (1f - Mathf.Abs(u)) * Mathf.Sin(v * Mathf.PI) * (0.4f + 0.6f * v);
                        px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                    }
                return streak = Make("lv_world_streak", w, h, px, TextureWrapMode.Clamp);
            }
        }

        /// <summary>Thin soft ring (rain splashes).</summary>
        public static Texture2D Ring => ring != null ? ring : (ring = Radial("lv_world_ring", 64, d =>
        {
            float t = 1f - Mathf.Abs(d - 0.78f) / 0.16f;
            return Mathf.Clamp01(t) * Mathf.Clamp01(t);
        }));

        /// <summary>Soft chevron pointing up (+V): transition markers on the ground.</summary>
        public static Texture2D Chevron
        {
            get
            {
                if (chevron != null) return chevron;
                const int n = 64;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                        // distance to the two strokes of a "^" whose apex is at (0, 0.55)
                        float s = 0.55f - Mathf.Abs(u) * 0.95f;
                        float dist = Mathf.Abs(v - s) * 0.72f;
                        float inside = Mathf.Abs(u) < 0.82f ? 1f : 0f;
                        float a = (1f - Smooth(0.08f, 0.2f, dist)) * inside * (1f - Smooth(0.7f, 0.86f, Mathf.Abs(u)));
                        px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                    }
                return chevron = Make("lv_world_chevron", n, n, px, TextureWrapMode.Clamp);
            }
        }

        /// <summary>Tiling water surface: soft light ripples (rgb), alpha 1 (vertex colour alpha shapes the stream).</summary>
        public static Texture2D Water
        {
            get
            {
                if (water != null) return water;
                const int n = 128;
                var px = new Color32[n * n];
                const float tau = Mathf.PI * 2f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (float)x / n, v = (float)y / n;
                        // tileable sums of sines (integer frequencies) warped a little: caustic-like streaks
                        float w1 = Mathf.Sin(tau * (u * 2f + v * 1f) + 1.7f * Mathf.Sin(tau * v * 3f));
                        float w2 = Mathf.Sin(tau * (u * -1f + v * 3f) + 1.3f * Mathf.Sin(tau * u * 2f + 0.5f));
                        float w3 = Mathf.Sin(tau * (u * 4f + v * 5f) + 0.9f);
                        float k = (w1 + w2) * 0.25f + w3 * 0.1f + 0.5f;
                        float hi = Smooth(0.68f, 0.92f, k);
                        float c = 0.78f + 0.12f * k + 0.25f * hi;
                        byte b = (byte)(Mathf.Clamp01(c) * 255f);
                        px[y * n + x] = new Color32(b, b, b, 255);
                    }
                return water = Make("lv_world_water", n, n, px, TextureWrapMode.Repeat);
            }
        }

        // ------------------------------------------------------------------ helpers

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static Texture2D Radial(string name, int n, System.Func<float, float> alpha)
        {
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(d)) * 255f));
                }
            return Make(name, n, n, px, TextureWrapMode.Clamp);
        }

        static Texture2D Make(string name, int w, int h, Color32[] px, TextureWrapMode wrap)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2,
                hideFlags = HideFlags.DontSave,
            };
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }
    }
}
