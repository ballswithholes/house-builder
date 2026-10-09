// Small procedural textures used by the 3D world (sky discs, stars, glows, particles, water, markers, the terrain's
// gravel and leaf-litter detail layers). Generated once on first use and kept for the session; owned by the world layer
// so it does not depend on other builders' art code.
using UnityEngine;

namespace Lanternvale.Game
{
    internal static class WorldTextures
    {
        static Texture2D glow, dot, star, moon, leaf, mist, streak, ring, chevron, water, gravel, litter;

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

        // ------------------------------------------------------------------ terrain detail layers (Lanternvale/Terrain _DetailTex)

        /// <summary>
        /// Tiling raked gravel (2.4 m per tile, opaque): small pale pebbles, warm and cool, in raked ripples running along
        /// X (10 per tile) — the shrine's courtyard gravel beside its paving.
        /// </summary>
        public static Texture2D Gravel
        {
            get
            {
                if (gravel != null) return gravel;
                const int n = 256;
                var rng = new System.Random(4711);
                float R() => (float)rng.NextDouble();
                var col = new Color[n * n];
                var lowN = TileNoise(n, 8, rng);
                // the bed between the pebbles, shaded by the rake's ripples (16 per tile, gently wavering)
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (float)x / n, v = (float)y / n;
                        float rip = Ripple(u, v);
                        float k = 0.9f + 0.1f * rip + 0.05f * (lowN[y * n + x] - 0.5f);
                        col[y * n + x] = new Color(0.78f * k, 0.74f * k, 0.66f * k, 1f);
                    }
                var tones = new[]
                {
                    new Color(0.88f, 0.85f, 0.78f), new Color(0.85f, 0.80f, 0.72f), new Color(0.82f, 0.81f, 0.78f),
                    new Color(0.90f, 0.87f, 0.80f), new Color(0.80f, 0.80f, 0.80f), new Color(0.84f, 0.78f, 0.70f),
                };
                for (int i = 0; i < 2400; i++)
                {
                    float cx = R() * n, cy = R() * n;
                    float rx = 1.3f + R() * 1.9f, ry = rx * (0.65f + 0.3f * R());
                    float ang = R() * Mathf.PI;
                    // the rake leaves the crests lit and the furrows in shade
                    float rip = Ripple(cx / n, cy / n);
                    var tone = tones[rng.Next(tones.Length)] * (0.95f + 0.06f * R() + 0.08f * rip);
                    float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    int x0 = Mathf.FloorToInt(cx - rx - 1), x1 = Mathf.CeilToInt(cx + rx + 1);
                    int y0 = Mathf.FloorToInt(cy - rx - 1), y1 = Mathf.CeilToInt(cy + rx + 1);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                            float lx = (dx * ca + dy * sa) / rx, ly = (-dx * sa + dy * ca) / ry;
                            float d = Mathf.Sqrt(lx * lx + ly * ly);
                            if (d >= 1.1f) continue;
                            float a = 1f - Smooth(0.75f, 1.1f, d);
                            // lit from above-left, a darker rim where it sinks into the bed
                            float lit = 1.02f + 0.06f * (-dx / rx * 0.5f + dy / rx * 0.7f) - 0.08f * Smooth(0.55f, 1f, d);
                            int k = Wrap(y, n) * n + Wrap(x, n);
                            col[k] = Color.Lerp(col[k], tone * lit, a);
                        }
                }
                var px = new Color32[n * n];
                for (int i = 0; i < px.Length; i++) { var c = col[i]; c.a = 1f; px[i] = c; }
                return gravel = Make("lv_world_gravel", n, n, px, TextureWrapMode.Repeat);
            }
        }

        /// <summary>
        /// Tiling leaf litter (3.2 m per tile; alpha = the leaves): ochre, amber, olive and faded rust leaves in loose
        /// drifts with a few twigs — the forest floor under its trees.
        /// </summary>
        public static Texture2D LeafLitter
        {
            get
            {
                if (litter != null) return litter;
                const int n = 512;
                var rng = new System.Random(1337);
                float R() => (float)rng.NextDouble();
                var col = new Color[n * n];
                var bg = new Color(0.62f, 0.52f, 0.30f, 0f);   // the leaves' average: no dark fringe when filtered
                for (int i = 0; i < col.Length; i++) col[i] = bg;
                var tones = new[]
                {
                    new Color(0.80f, 0.60f, 0.26f), new Color(0.86f, 0.68f, 0.32f), new Color(0.72f, 0.66f, 0.30f),
                    new Color(0.74f, 0.50f, 0.30f), new Color(0.60f, 0.44f, 0.26f), new Color(0.68f, 0.70f, 0.38f),
                    new Color(0.90f, 0.76f, 0.40f),
                };
                // twigs first (under the leaves)
                for (int i = 0; i < 14; i++)
                {
                    float cx = R() * n, cy = R() * n, ang = R() * Mathf.PI, len = 24f + R() * 40f;
                    var c = new Color(0.40f, 0.30f, 0.22f) * (0.9f + 0.2f * R());
                    StampStroke(col, n, cx, cy, ang, len, 1.3f, c);
                }
                // leaves in loose drifts
                var centres = new Vector2[26];
                for (int i = 0; i < centres.Length; i++) centres[i] = new Vector2(R() * n, R() * n);
                for (int i = 0; i < 230; i++)
                {
                    Vector2 p;
                    if (R() < 0.72f)
                    {
                        var cc = centres[rng.Next(centres.Length)];
                        float a = R() * Mathf.PI * 2f, r = Mathf.Sqrt(R()) * 46f;
                        p = cc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    }
                    else p = new Vector2(R() * n, R() * n);
                    float len = 16f + R() * 11f, wid = len * (0.36f + 0.14f * R());
                    var tone = tones[rng.Next(tones.Length)] * (0.92f + 0.14f * R());
                    StampLeaf(col, n, p.x, p.y, R() * Mathf.PI * 2f, len, wid, tone);
                }
                var px = new Color32[n * n];
                for (int i = 0; i < px.Length; i++) px[i] = col[i];
                return litter = Make("lv_world_litter", n, n, px, TextureWrapMode.Repeat);
            }
        }

        // ------------------------------------------------------------------ the expansion biomes' detail layers (alpha = cover)

        static Texture2D scree, puddles, snowDust, roots, ash;

        /// <summary>
        /// Tiling scree (3 m per tile; alpha = the stones): angular grey-brown stones and grit, lit from above-left, with
        /// dark gaps — rubble at the foot of cave and crypt walls.
        /// </summary>
        public static Texture2D Scree
        {
            get
            {
                if (scree != null) return scree;
                const int n = 256;
                var rng = new System.Random(2027);
                float R() => (float)rng.NextDouble();
                var col = new Color[n * n];
                var bg = new Color(0.46f, 0.43f, 0.4f, 0f);
                for (int i = 0; i < col.Length; i++) col[i] = bg;
                // grit first, then stones on top (bigger ones last)
                for (int i = 0; i < 900; i++)
                    StampStone(col, n, R() * n, R() * n, 1.2f + R() * 1.6f, R() * Mathf.PI, 5, Tone(rng, 0.42f, 0.4f, 0.38f, 0.14f), 0.8f);
                for (int i = 0; i < 170; i++)
                    StampStone(col, n, R() * n, R() * n, 3f + R() * 6f, R() * Mathf.PI, 4 + rng.Next(3), Tone(rng, 0.56f, 0.53f, 0.5f, 0.16f), 1f);
                return scree = Finish("lv_world_scree", n, col);
            }
        }

        /// <summary>
        /// Tiling puddles (5 m per tile; alpha = the water): still, sky-tinted pools with a darker muddy rim and a pale
        /// glint along their far edge — the fen's wet ground.
        /// </summary>
        public static Texture2D Puddles
        {
            get
            {
                if (puddles != null) return puddles;
                const int n = 256;
                var rng = new System.Random(3301);
                float R() => (float)rng.NextDouble();
                var field = new float[n * n];
                // a few soft blobs per tile, summed (wrapping), thresholded into puddle shapes
                for (int i = 0; i < 9; i++)
                {
                    float cx = R() * n, cy = R() * n, rx = 14f + R() * 26f, ry = rx * (0.45f + 0.35f * R()), ang = R() * Mathf.PI;
                    float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                    int ext = Mathf.CeilToInt(rx * 1.6f);
                    for (int y = -ext; y <= ext; y++)
                        for (int x = -ext; x <= ext; x++)
                        {
                            float lx = (x * ca + y * sa) / rx, ly = (-x * sa + y * ca) / ry;
                            float d = lx * lx + ly * ly;
                            if (d > 2.5f) continue;
                            int k = Wrap(Mathf.RoundToInt(cy) + y, n) * n + Wrap(Mathf.RoundToInt(cx) + x, n);
                            field[k] += Mathf.Exp(-d * 1.6f);
                        }
                }
                var lowN = TileNoise(n, 16, rng);
                var col = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int k = y * n + x;
                        float f = field[k] + (lowN[k] - 0.5f) * 0.35f;
                        float water = Smooth(0.42f, 0.5f, f);
                        float rim = Smooth(0.28f, 0.42f, f) * (1f - water);
                        // the far (+v) edge catches the sky
                        float glint = water * (1f - Smooth(0.5f, 0.62f, f)) * 0.6f;
                        var w = Color.Lerp(new Color(0.16f, 0.21f, 0.21f), new Color(0.26f, 0.32f, 0.31f), lowN[k]);
                        w = Color.Lerp(w, new Color(0.7f, 0.76f, 0.74f), glint);
                        var mud = new Color(0.27f, 0.25f, 0.18f);
                        var c = Color.Lerp(mud, w, water);
                        c.a = Mathf.Max(water, rim * 0.75f);
                        col[k] = c;
                    }
                return puddles = Finish("lv_world_puddles", n, col);
            }
        }

        /// <summary>Tiling snow dust (4 m per tile; alpha = the snow): soft drifts with a few sparkling grains.</summary>
        public static Texture2D SnowDust
        {
            get
            {
                if (snowDust != null) return snowDust;
                const int n = 256;
                var rng = new System.Random(4409);
                float R() => (float)rng.NextDouble();
                var a = TileNoise(n, 6, rng);
                var b = TileNoise(n, 20, rng);
                var col = new Color[n * n];
                for (int i = 0; i < col.Length; i++)
                {
                    float f = a[i] * 0.7f + b[i] * 0.3f;
                    float cover = Smooth(0.48f, 0.66f, f);
                    float shade = 0.9f + 0.1f * b[i];
                    col[i] = new Color(0.84f * shade, 0.91f * shade, 1f * shade, cover);
                }
                for (int i = 0; i < 260; i++)
                {
                    int k = rng.Next(n) * n + rng.Next(n);
                    col[k] = new Color(1f, 1f, 1f, Mathf.Max(col[k].a, 0.6f + 0.4f * R()));
                }
                return snowDust = Finish("lv_world_snowdust", n, col);
            }
        }

        /// <summary>Tiling roots (5 m per tile; alpha = the roots): dark, branching roots with a violet sheen.</summary>
        public static Texture2D Roots
        {
            get
            {
                if (roots != null) return roots;
                const int n = 512;
                var rng = new System.Random(5521);
                float R() => (float)rng.NextDouble();
                var col = new Color[n * n];
                var bg = new Color(0.26f, 0.2f, 0.24f, 0f);
                for (int i = 0; i < col.Length; i++) col[i] = bg;
                for (int r = 0; r < 9; r++)
                    Root(col, n, rng, R() * n, R() * n, R() * Mathf.PI * 2f, 7f + R() * 5f, 3);
                return roots = Finish("lv_world_roots", n, col);
            }
        }

        static void Root(Color[] col, int n, System.Random rng, float x, float y, float ang, float w, int depth)
        {
            float R() => (float)rng.NextDouble();
            int steps = 18 + rng.Next(18);
            var tone = new Color(0.3f, 0.22f, 0.26f) * (0.9f + 0.25f * R());
            for (int i = 0; i < steps && w > 0.8f; i++)
            {
                float len = 6f + R() * 6f;
                ang += (R() - 0.5f) * 0.7f;
                float nx = x + Mathf.Cos(ang) * len, ny = y + Mathf.Sin(ang) * len;
                // a pale top-light along the root
                StampStroke(col, n, (x + nx) * 0.5f, (y + ny) * 0.5f, ang, len + w * 0.5f, w, tone);
                StampStroke(col, n, (x + nx) * 0.5f - Mathf.Sin(ang) * w * 0.18f, (y + ny) * 0.5f + Mathf.Cos(ang) * w * 0.18f, ang, len, w * 0.3f, tone * 1.45f);
                x = nx; y = ny;
                w *= 0.95f;
                if (depth > 0 && R() < 0.14f) Root(col, n, rng, x, y, ang + (R() < 0.5f ? 0.9f : -0.9f), w * 0.65f, depth - 1);
            }
        }

        /// <summary>Tiling ash (4.5 m per tile; alpha = the ash): grey drifts, soot-black smudges and charred flecks.</summary>
        public static Texture2D Ash
        {
            get
            {
                if (ash != null) return ash;
                const int n = 256;
                var rng = new System.Random(6607);
                var a = TileNoise(n, 5, rng);
                var b = TileNoise(n, 18, rng);
                var c2 = TileNoise(n, 9, rng);
                var col = new Color[n * n];
                for (int i = 0; i < col.Length; i++)
                {
                    float f = a[i] * 0.65f + b[i] * 0.35f;
                    float cover = Smooth(0.45f, 0.65f, f) * 0.8f;
                    float soot = Smooth(0.68f, 0.8f, c2[i]);
                    var g = Color.Lerp(new Color(0.62f, 0.6f, 0.58f), new Color(0.2f, 0.18f, 0.18f), soot) * (0.92f + 0.12f * b[i]);
                    g.a = Mathf.Max(cover, soot * 0.35f);
                    col[i] = g;
                }
                for (int i = 0; i < 400; i++)
                {
                    int x = rng.Next(n), y = rng.Next(n);
                    StampStone(col, n, x, y, 0.8f + (float)rng.NextDouble() * 1.4f, (float)rng.NextDouble() * Mathf.PI, 4, new Color(0.14f, 0.12f, 0.12f), 0.9f);
                }
                return ash = Finish("lv_world_ash", n, col);
            }
        }

        static Color Tone(System.Random rng, float r, float g, float b, float spread)
        {
            float k = 1f + ((float)rng.NextDouble() - 0.5f) * 2f * spread;
            float warm = ((float)rng.NextDouble() - 0.5f) * 0.06f;
            return new Color(r * k + warm, g * k, b * k - warm);
        }

        /// <summary>An angular stone (a jittered polygon) lit from above-left, with a dark contact rim.</summary>
        static void StampStone(Color[] col, int n, float cx, float cy, float rad, float rot, int sides, Color tone, float alpha)
        {
            int ext = Mathf.CeilToInt(rad * 1.5f) + 1;
            for (int y = -ext; y <= ext; y++)
                for (int x = -ext; x <= ext; x++)
                {
                    float dx = x + 0.5f, dy = y + 0.5f;
                    float ang = Mathf.Atan2(dy, dx) - rot;
                    // distance to a regular polygon's edge (angular silhouette)
                    float sector = Mathf.PI * 2f / sides;
                    float local = Mathf.Repeat(ang, sector) - sector * 0.5f;
                    float edge = rad * Mathf.Cos(sector * 0.5f) / Mathf.Cos(local);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / edge;
                    if (d > 1.15f) continue;
                    float a = (1f - Smooth(0.85f, 1.1f, d)) * alpha;
                    float lit = 1.05f - 0.25f * (dx / rad * 0.5f - dy / rad * 0.7f) * 0.5f - 0.25f * Smooth(0.6f, 1f, d);
                    int k = Wrap(Mathf.RoundToInt(cy) + y, n) * n + Wrap(Mathf.RoundToInt(cx) + x, n);
                    var o = col[k];
                    var rgb = Color.Lerp(o.a > 0.01f ? o : tone, tone * lit, a);
                    col[k] = new Color(rgb.r, rgb.g, rgb.b, Mathf.Max(o.a, a));
                }
        }

        static Texture2D Finish(string name, int n, Color[] col)
        {
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = col[i];
            return Make(name, n, n, px, TextureWrapMode.Repeat);
        }

        /// <summary>The rake's ripples across a gravel tile (u, v in tile units): −1 furrow … 1 crest, gently wavering.</summary>
        static float Ripple(float u, float v) =>
            Mathf.Sin((v * 10f + 0.16f * Mathf.Sin(u * Mathf.PI * 4f) + 0.05f * Mathf.Sin(u * Mathf.PI * 10f)) * Mathf.PI * 2f);

        static void StampLeaf(Color[] col, int n, float cx, float cy, float ang, float len, float wid, Color tone)
        {
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            float ext = len * 0.6f + 2f;
            int x0 = Mathf.FloorToInt(cx - ext), x1 = Mathf.CeilToInt(cx + ext), y0 = Mathf.FloorToInt(cy - ext), y1 = Mathf.CeilToInt(cy + ext);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float along = dx * ca + dy * sa, across = -dx * sa + dy * ca;
                    float t = along / len + 0.5f;           // 0 stem … 1 tip
                    if (t < -0.08f || t > 1.02f) continue;
                    float half = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * wid * 0.5f * (1f - 0.3f * t);
                    float stem = t < 0.04f ? 1f - Smooth(0.5f, 1.1f, Mathf.Abs(across)) : 0f;
                    float a = Mathf.Max(1f - Smooth(half - 0.9f, half + 0.4f, Mathf.Abs(across)), stem);
                    if (a <= 0f) continue;
                    // a darker midrib, one half a little lighter (the light catches the curl)
                    float rib = 1f - 0.18f * (1f - Smooth(0.3f, 1.1f, Mathf.Abs(across)));
                    float side = across > 0f ? 1.06f : 0.95f;
                    var c = tone * (rib * side * (0.92f + 0.12f * t));
                    int k = Wrap(y, n) * n + Wrap(x, n);
                    var o = col[k];
                    float oa = o.a;
                    var rgb = Color.Lerp(oa > 0.01f ? o : c, c, a);
                    col[k] = new Color(rgb.r, rgb.g, rgb.b, Mathf.Max(oa, a));
                }
        }

        static void StampStroke(Color[] col, int n, float cx, float cy, float ang, float len, float w, Color tone)
        {
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            float ext = len * 0.5f + w + 2f;
            int x0 = Mathf.FloorToInt(cx - ext), x1 = Mathf.CeilToInt(cx + ext), y0 = Mathf.FloorToInt(cy - ext), y1 = Mathf.CeilToInt(cy + ext);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float along = dx * ca + dy * sa, across = -dx * sa + dy * ca;
                    if (Mathf.Abs(along) > len * 0.5f) continue;
                    float a = (1f - Smooth(w * 0.5f, w * 0.5f + 0.9f, Mathf.Abs(across))) * (1f - Smooth(len * 0.42f, len * 0.5f, Mathf.Abs(along)));
                    if (a <= 0f) continue;
                    int k = Wrap(y, n) * n + Wrap(x, n);
                    var o = col[k];
                    var rgb = Color.Lerp(o.a > 0.01f ? o : tone, tone, a);
                    col[k] = new Color(rgb.r, rgb.g, rgb.b, Mathf.Max(o.a, a * 0.9f));
                }
        }

        /// <summary>Tiling value noise in [0,1] (cells per side), bilinear between random lattice values.</summary>
        static float[] TileNoise(int n, int cells, System.Random rng)
        {
            var lat = new float[cells * cells];
            for (int i = 0; i < lat.Length; i++) lat[i] = (float)rng.NextDouble();
            var o = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (float)x / n * cells, fy = (float)y / n * cells;
                    int ix = (int)fx, iy = (int)fy;
                    float tx = fx - ix, ty = fy - iy;
                    tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
                    int x1 = (ix + 1) % cells, y1 = (iy + 1) % cells;
                    float a = Mathf.Lerp(lat[iy * cells + ix], lat[iy * cells + x1], tx);
                    float b = Mathf.Lerp(lat[y1 * cells + ix], lat[y1 * cells + x1], tx);
                    o[y * n + x] = Mathf.Lerp(a, b, ty);
                }
            return o;
        }

        static int Wrap(int i, int n) => ((i % n) + n) % n;

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
