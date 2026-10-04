// Procedural sprites and helpers shared by the world, unit and FX presentation.
//
// Every texture here is generated once on first use and cached. Shapes are white/greyscale so they
// can be tinted per use (school colours, ring colours, time of day). Sprites are sized so their
// LARGEST side is 1 world unit unless stated otherwise; scale them with SetSize().
// Real art always wins: Fx(key) returns ArtLibrary's sprite when a PNG exists for an fx_* key.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class PresentationArt
    {
        delegate float AlphaFn(float u, float v);
        delegate Color ColorFn(float u, float v);

        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        static readonly Dictionary<int, Sprite> IntCache = new Dictionary<int, Sprite>(); // allocation-free keys for ring/cone variants
        static readonly Dictionary<string, bool> RealArt = new Dictionary<string, bool>();

        // ------------------------------------------------------------------ materials

        const string LitShaderName = "Universal Render Pipeline/2D/Sprite-Lit-Default";

        static Material unlit;
        static bool unlitMissing;
        static Material lit;
        static bool litMissing;

        // Neither probe caches a "not lit" answer: Lighting2D.Reset() (editor pipeline setup) can turn
        // URP-lit mode on later in the same domain.

        /// <summary>
        /// Unlit sprite material for glows, FX, sky and previews when URP 2D lights are active
        /// (so they glow instead of being darkened at night). Null when not needed / not found.
        /// </summary>
        public static Material Unlit
        {
            get
            {
                if (!Lighting2D.IsLit) return null;
                if (unlit != null) return unlit;
                if (unlitMissing) return null;
                var s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (s == null) s = Shader.Find("Sprites/Default");
                if (s == null) { unlitMissing = true; return null; }
                unlit = new Material(s) { name = "Lanternvale Sprite Unlit" };
                return unlit;
            }
        }

        /// <summary>
        /// Sprite-Lit material for every world sprite that should react to URP 2D lights. It has to be
        /// assigned explicitly: URP hands a new SpriteRenderer its Sprite-Lit-Default as the pipeline's
        /// default 2D material only in the Editor (UniversalRenderPipelineAsset.GetMaterial returns null in
        /// players), so in a build a renderer created at runtime keeps the built-in unlit Sprites-Default
        /// and ignores every Light2D. Null when not URP-lit, or when the shader is not in this build
        /// (URP 12–14 ship it only if something references it; URP 17 ships it with the 2D renderer).
        /// </summary>
        public static Material Lit
        {
            get
            {
                if (!Lighting2D.IsLit) return null;
                if (lit != null) return lit;
                if (litMissing) return null;
                var s = Shader.Find(LitShaderName);
                if (s == null || !s.isSupported)
                {
                    litMissing = true;
                    Debug.LogWarning($"[Lanternvale] The URP 2D Renderer is active, but the shader '{LitShaderName}' is " +
                                     (s == null ? "not included in this build" : "not supported on this device") +
                                     ", so sprites cannot be lit by 2D lights. Falling back to the night-overlay lighting. " +
                                     "Add it (and Sprite-Unlit-Default) to Project Settings > Graphics > Always Included Shaders.");
                    return null;
                }
                lit = new Material(s) { name = "Lanternvale Sprite Lit" };
                return lit;
            }
        }

        /// <summary>
        /// True when world sprites really are lit by URP 2D lights: the 2D Renderer is active AND the
        /// Sprite-Lit material is available. Presentation code branches on this (not Lighting2D.IsLit)
        /// between the Light2D path and the night-overlay path, so a build without the lit shader still
        /// gets darker nights instead of daylight-bright sprites under an unused global light.
        /// </summary>
        public static bool SpritesLit => Lit != null;

        /// <summary>Makes a renderer ignore 2D lights (no-op in unlit projects).</summary>
        public static void MakeUnlit(SpriteRenderer sr)
        {
            if (sr == null || !Lighting2D.IsLit) return;
            var m = Unlit;
            if (m != null) sr.sharedMaterial = m;
        }

        /// <summary>Makes a renderer react to 2D lights (no-op when sprites are not URP-lit).</summary>
        public static void MakeLit(SpriteRenderer sr)
        {
            if (sr == null) return;
            var m = Lit;
            if (m != null) sr.sharedMaterial = m;
        }

        /// <summary>
        /// Lighting2D.AddPointLight for world props, with the halo sprite unlit. When the 2D Renderer is
        /// active but sprites cannot be lit (see <see cref="SpritesLit"/>), the Light2D is switched off and
        /// the halo is laid out like Lighting2D's unlit mode (bigger, above the night overlay).
        /// </summary>
        public static LightHandle AddPointLight(GameObject go, Vector3 localOffset, Color color, float radius, float intensity)
        {
            var h = Lighting2D.AddPointLight(go, localOffset, color, radius, intensity);
            if (h == null) return null;
            if (Lighting2D.IsLit && !SpritesLit)
            {
                if (h.light is Behaviour b) b.enabled = false;
                h.light = null;
                if (h.glow != null)
                {
                    h.glow.sortingOrder = SortingOrders.NightOverlay + 100;
                    float d = radius * 1.6f;
                    h.glow.transform.localScale = new Vector3(d, d, 1f);
                }
            }
            if (h.glow != null) MakeUnlit(h.glow);
            return h;
        }

        // ------------------------------------------------------------------ renderer helpers

        /// <summary>
        /// New sprite renderer. Lit by URP 2D lights (Sprite-Lit material) unless <paramref name="unlitMat"/>,
        /// which keeps glows, FX and the sky bright at night.
        /// </summary>
        public static SpriteRenderer NewRenderer(string name, Transform parent, Sprite sprite, int order, bool unlitMat = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (unlitMat) MakeUnlit(sr);
            else MakeLit(sr);
            return sr;
        }

        /// <summary>Scales t so that sprite s is drawn w × h world units.</summary>
        public static void SetSize(Transform t, Sprite s, float w, float h)
        {
            if (s == null) { t.localScale = new Vector3(w, h, 1f); return; }
            var b = s.bounds.size;
            t.localScale = new Vector3(w / Mathf.Max(1e-4f, b.x), h / Mathf.Max(1e-4f, b.y), 1f);
        }

        /// <summary>Normalised pivot (0..1) of a sprite.</summary>
        public static Vector2 PivotNorm(Sprite s)
        {
            if (s == null) return new Vector2(0.5f, 0.5f);
            var r = s.rect;
            return new Vector2(s.pivot.x / Mathf.Max(1f, r.width), s.pivot.y / Mathf.Max(1f, r.height));
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        /// <summary>True if a real PNG exists for the art key (cached).</summary>
        public static bool HasReal(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (RealArt.TryGetValue(key, out var b)) return b;
            b = ArtLibrary.HasRealTexture(key);
            RealArt[key] = b;
            return b;
        }

        /// <summary>
        /// Sprite for an effect key: the real art if a PNG exists, otherwise a matching procedural
        /// shape (fx_glow, fx_spark, fx_ring, fx_bolt, fx_arrow, fx_slash, fx_heal, fx_smoke, fx_leaf,
        /// fx_firefly, fx_shadow, fx_target_ring, fx_rune_circle). Unknown keys → ArtLibrary sprite.
        /// </summary>
        public static Sprite Fx(string key)
        {
            if (HasReal(key)) return ArtLibrary.Sprite(key);
            switch (key)
            {
                case "fx_glow": return Glow;
                case "fx_spark": return Spark;
                case "fx_ring": return Ring(5, 128);
                case "fx_bolt": return Bolt;
                case "fx_arrow": return Arrow;
                case "fx_slash": return Slash;
                case "fx_heal": return Plus;
                case "fx_smoke": return Smoke;
                case "fx_leaf": return Leaf;
                case "fx_firefly": return SoftDot;
                case "fx_shadow": return SoftDisc;
                case "fx_target_ring": return UnitRing;
                case "fx_rune_circle": return RuneCircle;
                case "": case null: return Glow;
                default: return ArtLibrary.Sprite(key);
            }
        }

        // ------------------------------------------------------------------ generation core

        static Texture2D MakeTex(string name, int w, int h, AlphaFn fn, bool readable = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a = Mathf.Clamp01(fn((x + 0.5f) / w, (y + 0.5f) / h));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f + 0.5f));
                }
            t.SetPixels32(px);
            t.Apply(false, !readable);
            return t;
        }

        static Texture2D MakeTexColor(string name, int w, int h, ColorFn fn, bool readable = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = fn((x + 0.5f) / w, (y + 0.5f) / h);
            t.SetPixels32(px);
            t.Apply(false, !readable);
            return t;
        }

        static Sprite MakeSprite(Texture2D t, Vector2 pivot, float largestSide = 1f, Vector4 border = default)
        {
            float ppu = Mathf.Max(t.width, t.height) / Mathf.Max(1e-4f, largestSide);
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, ppu, 0, SpriteMeshType.FullRect, border);
            s.name = t.name;
            return s;
        }

        static Sprite Cached(string key, System.Func<Sprite> make)
        {
            if (Cache.TryGetValue(key, out var s) && s != null) return s;
            s = make();
            Cache[key] = s;
            return s;
        }

        static float Smooth(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // centred coordinates in [-1, 1]
        static float Cx(float u) => u * 2f - 1f;

        // ------------------------------------------------------------------ basic shapes

        /// <summary>1×1 m white square, centred pivot.</summary>
        public static Sprite White => Cached("white", () => MakeSprite(ProceduralArt.White, new Vector2(0.5f, 0.5f)));

        /// <summary>Soft radial glow, 1 m diameter (same texture as Lighting2D glows).</summary>
        public static Sprite Glow => Lighting2D.GlowSprite;

        /// <summary>Small bright dot with a soft halo (fireflies, pollen, trails), 1 m diameter.</summary>
        public static Sprite SoftDot => Cached("softdot", () => MakeSprite(MakeTex("lv_softdot", 64, 64, (u, v) =>
        {
            float d = Mathf.Sqrt(Cx(u) * Cx(u) + Cx(v) * Cx(v));
            float halo = Mathf.Clamp01(1f - d); halo *= halo;
            float core = 1f - Smooth(0.12f, 0.38f, d);
            return Mathf.Max(halo * 0.65f, core);
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Soft filled disc (ground shadows, mist puffs), 1 m diameter.</summary>
        public static Sprite SoftDisc => Cached("softdisc", () => MakeSprite(MakeTex("lv_softdisc", 64, 64, (u, v) =>
        {
            float d = Mathf.Sqrt(Cx(u) * Cx(u) + Cx(v) * Cx(v));
            return 1f - Smooth(0.25f, 1f, d);
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Anti-aliased hard disc, 1 m diameter.</summary>
        public static Sprite Disc => Cached("disc", () => MakeSprite(MakeTex("lv_disc", 128, 128, (u, v) =>
        {
            float d = Mathf.Sqrt(Cx(u) * Cx(u) + Cx(v) * Cx(v));
            return (1f - d) * 64f + 0.5f;
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Thin ring of constant pixel thickness, 1 m diameter (use a bigger size for big radii).</summary>
        public static Sprite Ring(int thicknessPx, int size)
        {
            thicknessPx = Mathf.Clamp(thicknessPx, 1, size / 4);
            int key = 1000000 + size * 1000 + thicknessPx;
            if (IntCache.TryGetValue(key, out var cached) && cached != null) return cached;
            return IntCache[key] = MakeRing(thicknessPx, size);
        }

        static Sprite MakeRing(int thicknessPx, int size)
        {
            float R = size * 0.5f;
            float rc = R - thicknessPx * 0.5f - 1.5f;
            return MakeSprite(MakeTex("lv_ring", size, size, (u, v) =>
            {
                float d = Mathf.Sqrt(Cx(u) * Cx(u) + Cx(v) * Cx(v)) * R;
                return thicknessPx * 0.5f + 0.5f - Mathf.Abs(d - rc);
            }), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Ring sprite whose line looks ~lineMetres thick at the given radius.</summary>
        public static Sprite RingFor(float radius, float lineMetres = 0.07f)
        {
            radius = Mathf.Max(0.05f, radius);
            int size = radius > 3f ? 512 : 256;
            int px = Mathf.RoundToInt(lineMetres / (radius * 2f) * size);
            return Ring(Mathf.Clamp(px, 2, size / 6), size);
        }

        /// <summary>Selection ellipse ring with a soft inner glow (scale non-uniformly for perspective).</summary>
        public static Sprite UnitRing => Cached("unitring", () => MakeSprite(MakeTex("lv_unitring", 128, 128, (u, v) =>
        {
            float d = Mathf.Sqrt(Cx(u) * Cx(u) + Cx(v) * Cx(v));
            float dp = d * 64f;
            float ring = 4.5f - Mathf.Abs(dp - 58f);
            float inner = d < 0.9f ? 0.28f * Smooth(0.45f, 0.9f, d) : 0f;
            return Mathf.Max(ring, inner);
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Vertical gradient: transparent at the bottom, opaque at the top (sky, fades). 1 m tall.</summary>
        public static Sprite GradientUp => Cached("gradup", () => MakeSprite(MakeTex("lv_gradup", 4, 256, (u, v) => Smooth(0f, 1f, v)), new Vector2(0.5f, 0.5f)));

        /// <summary>Horizontal band: opaque in the middle, transparent at top and bottom. 1 m tall.</summary>
        public static Sprite HazeBand => Cached("haze", () => MakeSprite(MakeTex("lv_haze", 4, 128, (u, v) => Mathf.Pow(Mathf.Sin(v * Mathf.PI), 1.6f)), new Vector2(0.5f, 0.5f)));

        /// <summary>Soft horizontal line (beams, path segments), pivot at the left middle, 1×1 m.</summary>
        public static Sprite BeamLine => Cached("beam", () => MakeSprite(MakeTex("lv_beam", 16, 64, (u, v) =>
        {
            float y = (v - 0.5f) / 0.2f;
            return Mathf.Exp(-y * y);
        }), new Vector2(0f, 0.5f), 1f));

        // ------------------------------------------------------------------ effect shapes

        /// <summary>Glowing teardrop projectile pointing +x, 1 m long.</summary>
        public static Sprite Bolt => Cached("bolt", () => MakeSprite(MakeTex("lv_bolt", 128, 64, (u, v) =>
        {
            float x = u * 128f, y = v * 64f - 32f;
            const float hx = 98f, hr = 15f;
            float dHead = Mathf.Sqrt((x - hx) * (x - hx) + y * y);
            float half = x < hx ? hr * Mathf.Pow(Mathf.Clamp01((x - 6f) / (hx - 6f)), 1.5f) : 0f;
            float tail = x < hx ? half + 0.5f - Mathf.Abs(y) : -1f;
            float shape = Mathf.Max(hr + 0.5f - dHead, tail);
            float glow = 0.4f * Mathf.Exp(-(dHead * dHead) / (2f * 16f * 16f));
            return Mathf.Max(Mathf.Clamp01(shape), glow);
        }), new Vector2(0.75f, 0.5f)));

        /// <summary>Arrow pointing +x (greyscale with a soft ink edge), 1 m long.</summary>
        public static Sprite Arrow => Cached("arrow", () => MakeSprite(MakeTexColor("lv_arrow", 128, 32, (u, v) =>
        {
            float x = u * 128f, y = v * 32f - 16f, ay = Mathf.Abs(y);
            float a = 0f, g = 0.85f;
            if (x > 10f && x < 106f && ay < 1.6f) { a = 1f; g = 0.82f; }
            if (x >= 100f && x < 126f && ay < 7.5f * (126f - x) / 26f) { a = 1f; g = 0.98f; }
            if (ay >= 1.5f && ay < 6.5f)
            {
                float s = (ay - 1.5f) * 2f;
                if (x > 6f + s && x < 26f + s) { a = 1f; g = 0.93f; }
            }
            return new Color(g, g, g, a);
        }), new Vector2(0.85f, 0.5f)));

        /// <summary>Four-point sparkle star, 1 m.</summary>
        public static Sprite Spark => Cached("spark", () => MakeSprite(MakeTex("lv_spark", 64, 64, (u, v) =>
        {
            float x = Cx(u), y = Cx(v);
            float d = Mathf.Sqrt(x * x + y * y);
            float core = Mathf.Exp(-d * d / 0.03f);
            float s1 = Mathf.Exp(-(y * y) / 0.004f) * Mathf.Clamp01(1f - Mathf.Abs(x));
            float s2 = Mathf.Exp(-(x * x) / 0.004f) * Mathf.Clamp01(1f - Mathf.Abs(y));
            return core + s1 + s2;
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Soft rounded plus (healing), 1 m.</summary>
        public static Sprite Plus => Cached("plus", () => MakeSprite(MakeTex("lv_plus", 64, 64, (u, v) =>
        {
            float x = Mathf.Abs(Cx(u)), y = Mathf.Abs(Cx(v));
            float b1 = SdBox(x, y, 0.62f, 0.17f), b2 = SdBox(x, y, 0.17f, 0.62f);
            float sd = Mathf.Min(b1, b2) - 0.06f;
            float shape = Mathf.Clamp01(0.5f - sd * 32f);
            float glow = 0.3f * Mathf.Exp(-Mathf.Max(0f, sd) * 9f);
            return Mathf.Max(shape, glow * (1f - Smooth(0.7f, 1f, Mathf.Max(x, y))));
        }), new Vector2(0.5f, 0.5f)));

        static float SdBox(float x, float y, float bx, float by)
        {
            float dx = x - bx, dy = y - by;
            float ox = Mathf.Max(dx, 0f), oy = Mathf.Max(dy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(dx, dy), 0f);
        }

        /// <summary>Crescent slash arc bulging towards +x, 1 m.</summary>
        public static Sprite Slash => Cached("slash", () => MakeSprite(MakeTex("lv_slash", 128, 128, (u, v) =>
        {
            float x = Cx(u), y = Cx(v);
            float d1 = Mathf.Sqrt(x * x + y * y);
            float d2 = Mathf.Sqrt((x + 0.32f) * (x + 0.32f) + y * y);
            float inside = Mathf.Clamp01((0.95f - d1) * 64f) * Mathf.Clamp01((d2 - 0.98f) * 64f);
            float taper = Mathf.Clamp01(1f - Mathf.Abs(y) / 0.95f);
            float bright = Smooth(0.6f, 0.95f, d1);
            return inside * Mathf.Pow(taper, 0.6f) * (0.45f + 0.55f * bright);
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Leaf with a vein (greyscale), 1 m.</summary>
        public static Sprite Leaf => Cached("leaf", () => MakeSprite(MakeTexColor("lv_leaf", 64, 64, (u, v) =>
        {
            float x0 = Cx(u), y0 = Cx(v);
            const float c = 0.70710678f;
            float x = (x0 + y0) * c, y = (y0 - x0) * c; // rotate 45°
            float r1 = Mathf.Sqrt(x * x + (y - 0.62f) * (y - 0.62f));
            float r2 = Mathf.Sqrt(x * x + (y + 0.62f) * (y + 0.62f));
            float sd = Mathf.Max(r1, r2) - 1.0f;
            float a = Mathf.Clamp01(0.5f - sd * 32f);
            if (x < -0.8f && Mathf.Abs(y) < 0.04f && x > -0.98f) a = 1f; // stem
            float g = Mathf.Abs(y) < 0.035f ? 0.68f : 0.92f - 0.1f * Mathf.Clamp01(-sd * 3f);
            return new Color(g, g, g, a);
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Soft noisy smoke puff, 1 m.</summary>
        public static Sprite Smoke => Cached("smoke", () => MakeSprite(MakeTex("lv_smoke", 64, 64, (u, v) =>
        {
            float d = Mathf.Sqrt(Cx(u) * Cx(u) + Cx(v) * Cx(v));
            float n = Mathf.PerlinNoise(u * 4.3f + 11.7f, v * 4.3f + 3.1f);
            return (1f - Smooth(0.35f, 1f, d + (n - 0.5f) * 0.35f)) * (0.65f + 0.35f * n);
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Large soft mist bank (2:1), 1 m wide.</summary>
        public static Sprite Mist => Cached("mist", () => MakeSprite(MakeTex("lv_mist", 256, 128, (u, v) =>
        {
            float x = Cx(u), y = Cx(v);
            float d = Mathf.Sqrt(x * x + y * y * 1.1f);
            float n = Mathf.PerlinNoise(u * 3.1f + 5.3f, v * 2.2f + 1.7f) * 0.65f + Mathf.PerlinNoise(u * 7.7f + 2.1f, v * 6.1f + 9.4f) * 0.35f;
            return (1f - Smooth(0.25f, 1f, d + (n - 0.5f) * 0.5f)) * (0.5f + 0.5f * n);
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Thin rain streak (bright at the bottom), 1 m tall.</summary>
        public static Sprite RainStreak => Cached("rain", () => MakeSprite(MakeTex("lv_rain", 8, 64, (u, v) =>
        {
            float x = (u - 0.5f) * 8f;
            return Mathf.Clamp01(1.3f - Mathf.Abs(x)) * Smooth(0f, 0.9f, 1f - v);
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Ice shard / diamond, 1 m tall.</summary>
        public static Sprite Shard => Cached("shard", () => MakeSprite(MakeTex("lv_shard", 32, 64, (u, v) =>
        {
            float x = Mathf.Abs(Cx(u)), y = Mathf.Abs(Cx(v));
            float sd = x / 0.8f + y - 1f;
            return Mathf.Clamp01(0.5f - sd * 24f) * (0.75f + 0.25f * (1f - x));
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Double chevron pointing +y (ground exit markers), 1 m.</summary>
        public static Sprite Chevron => Cached("chevron", () => MakeSprite(MakeTex("lv_chevron", 128, 128, (u, v) =>
        {
            float x = Cx(u), y = Cx(v);
            float a1 = Chev(x, y - 0.18f), a2 = Chev(x, y + 0.32f) * 0.6f;
            float glow = 0.22f * Mathf.Exp(-(x * x + y * y) / 0.35f);
            return Mathf.Max(Mathf.Max(a1, a2), glow);
        }), new Vector2(0.5f, 0.5f)));

        static float Chev(float x, float y)
        {
            // distance to the polyline (-0.55,-0.25) -> (0,0.25) -> (0.55,-0.25)
            float d = Mathf.Min(SegDist(x, y, -0.55f, -0.25f, 0f, 0.25f), SegDist(x, y, 0f, 0.25f, 0.55f, -0.25f));
            return Mathf.Clamp01((0.11f - d) * 40f);
        }

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay, wx = px - ax, wy = py - ay;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy));
            float dx = wx - vx * t, dy = wy - vy * t;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Magic circle with runes (cast circles, transition runes), 1 m.</summary>
        public static Sprite RuneCircle => Cached("rune", () => MakeSprite(MakeTex("lv_rune", 256, 256, (u, v) =>
        {
            float x = Cx(u), y = Cx(v);
            float d = Mathf.Sqrt(x * x + y * y);
            const float px = 1f / 128f;
            float a = Line(d - 0.95f, 1.6f * px) + Line(d - 0.80f, 1.1f * px) * 0.9f + Line(d - 0.44f, 1.0f * px) * 0.8f;
            float ang = Mathf.Atan2(y, x);
            // tick marks between the outer rings
            float seg = Mathf.PI * 2f / 16f;
            float k = Mathf.Repeat(ang + seg * 0.5f, seg) - seg * 0.5f;
            if (d > 0.835f && d < 0.915f) a += Mathf.Clamp01(1.4f - Mathf.Abs(k * d) / px);
            // hexagram inscribed in the middle ring
            float best = 10f;
            for (int i = 0; i < 6; i++)
            {
                float a0 = Mathf.PI / 2f + i * Mathf.PI * 2f / 6f;
                float a1 = a0 + Mathf.PI * 2f / 3f;
                best = Mathf.Min(best, SegDist(x, y, Mathf.Cos(a0) * 0.8f, Mathf.Sin(a0) * 0.8f, Mathf.Cos(a1) * 0.8f, Mathf.Sin(a1) * 0.8f));
            }
            a += Mathf.Clamp01(1.2f - best / px) * 0.7f;
            float glow = d < 1f ? 0.08f * (1f - d) : 0f;
            return Mathf.Clamp01(a) + glow;
        }), new Vector2(0.5f, 0.5f)));

        static float Line(float dist, float halfWidth) => Mathf.Clamp01(1f - (Mathf.Abs(dist) - halfWidth) * 128f);

        /// <summary>Pale moon disc with soft halo, 1 m.</summary>
        public static Sprite Moon => Cached("moon", () => MakeSprite(MakeTexColor("lv_moon", 128, 128, (u, v) =>
        {
            float x = Cx(u), y = Cx(v);
            float d = Mathf.Sqrt(x * x + y * y);
            float disc = Mathf.Clamp01((0.55f - d) * 64f);
            float halo = 0.35f * Mathf.Exp(-(d - 0.55f) * (d - 0.55f) / 0.04f) * (d > 0.55f ? 1f : 0f);
            float n = Mathf.PerlinNoise(u * 6f + 3f, v * 6f + 7f);
            float g = 0.92f + 0.08f * n - 0.12f * Smooth(0.6f, 0.75f, n);
            return new Color(g, g, g * 0.97f, Mathf.Max(disc, halo));
        }), new Vector2(0.5f, 0.5f)));

        /// <summary>Filled sector (cone preview) pointing +x with a bright outline. Bucketed by 5°.</summary>
        public static Sprite Cone(float angleDeg)
        {
            int bucket = Mathf.Clamp(Mathf.RoundToInt(angleDeg / 5f) * 5, 5, 360);
            if (IntCache.TryGetValue(bucket, out var cached) && cached != null) return cached;
            return IntCache[bucket] = MakeCone(bucket);
        }

        static Sprite MakeCone(int bucket)
        {
            float half = bucket * 0.5f * Mathf.Deg2Rad;
            const float R = 128f;
            return MakeSprite(MakeTex("lv_cone" + bucket, 256, 256, (u, v) =>
            {
                float x = Cx(u), y = Cx(v);
                float d = Mathf.Sqrt(x * x + y * y);
                float ang = Mathf.Abs(Mathf.Atan2(y, x));
                float angular = -10f;
                if (bucket < 360)
                {
                    float over = ang - half;
                    angular = over < Mathf.PI * 0.5f ? d * Mathf.Sin(over) : d;
                }
                float sd = Mathf.Max(d - 0.985f, angular) * R; // signed distance in pixels
                float fill = Mathf.Clamp01(0.5f - sd) * (0.16f + 0.16f * d);
                float edge = Mathf.Clamp01(1.6f - Mathf.Abs(sd + 1.6f));
                return Mathf.Max(fill, edge);
            }), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Rounded rectangle with a bright outline and faint fill for SpriteDrawMode.Sliced
        /// (line/rect previews). Border ≈ 0.09 m.
        /// </summary>
        public static Sprite SlicedRect => Cached("slicedrect", () =>
        {
            const int n = 64; const float r = 14f;
            var t = MakeTex("lv_slicedrect", n, n, (u, v) =>
            {
                float x = u * n, y = v * n;
                float cx = Mathf.Clamp(x, r, n - r), cy = Mathf.Clamp(y, r, n - r);
                float d = r - Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); // inside distance to edge
                float edge = Mathf.Clamp01(1.5f - Mathf.Abs(d - 2.5f) * 0.9f);
                float fill = Mathf.Clamp01(d) * 0.22f;
                return Mathf.Max(edge, fill);
            });
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 0.48f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
        });

        /// <summary>Fluffy sheep (polymorph), ~0.95 m tall, standing on its feet, facing +x. Readable texture.</summary>
        public static Sprite Sheep => Cached("sheep", () =>
        {
            const int n = 128;
            var mask = new byte[n * n]; // 0 empty, 1 wool, 2 dark (legs/head), 3 eye, 4 wool tuft
            for (int py = 0; py < n; py++)
                for (int px = 0; px < n; px++)
                {
                    float u = (px + 0.5f) / n, v = (py + 0.5f) / n;
                    byte m = 0;
                    // legs
                    if (v > 0.05f && v < 0.32f && (Mathf.Abs(u - 0.31f) < 0.026f || Mathf.Abs(u - 0.41f) < 0.026f || Mathf.Abs(u - 0.58f) < 0.026f || Mathf.Abs(u - 0.67f) < 0.026f)) m = 2;
                    // wool: ellipse plus puffs
                    float ex = (u - 0.48f) / 0.29f, ey = (v - 0.47f) / 0.18f;
                    bool wool = ex * ex + ey * ey < 1f;
                    for (int i = 0; i < 11 && !wool; i++)
                    {
                        float a = i / 11f * Mathf.PI * 2f;
                        float cx = 0.48f + Mathf.Cos(a) * 0.27f, cy = 0.47f + Mathf.Sin(a) * 0.17f;
                        float rr = 0.085f + 0.02f * ((i * 7) % 3) / 2f;
                        if ((u - cx) * (u - cx) + (v - cy) * (v - cy) < rr * rr) wool = true;
                    }
                    if (wool) m = 1;
                    // head
                    float hx = (u - 0.8f) / 0.095f, hy = (v - 0.53f) / 0.085f;
                    if (hx * hx + hy * hy < 1f) m = 2;
                    float ex2 = (u - 0.73f) / 0.06f, ey2 = (v - 0.6f) / 0.028f;
                    if (ex2 * ex2 + ey2 * ey2 < 1f) m = 2;
                    if ((u - 0.765f) * (u - 0.765f) + (v - 0.62f) * (v - 0.62f) < 0.05f * 0.05f) m = 4;
                    if ((u - 0.835f) * (u - 0.835f) + (v - 0.555f) * (v - 0.555f) < 0.016f * 0.016f) m = 3;
                    mask[py * n + px] = m;
                }
            var ink = new Color32(58, 47, 66, 255);
            var px32 = new Color32[n * n];
            for (int py = 0; py < n; py++)
                for (int px = 0; px < n; px++)
                {
                    byte m = mask[py * n + px];
                    Color32 c;
                    if (m == 1 || m == 4)
                    {
                        float shade = 0.84f + 0.16f * Mathf.Clamp01((py / (float)n - 0.3f) / 0.35f);
                        byte g = (byte)(255 * shade);
                        c = new Color32(g, g, (byte)(g * 0.97f), 255);
                    }
                    else if (m == 2) c = new Color32(84, 72, 88, 255);
                    else if (m == 3) c = new Color32(250, 246, 236, 255);
                    else
                    {
                        // outline: any filled pixel within 2 px
                        bool near = false;
                        for (int oy = -2; oy <= 2 && !near; oy++)
                            for (int ox = -2; ox <= 2 && !near; ox++)
                            {
                                int qx = px + ox, qy = py + oy;
                                if (qx < 0 || qy < 0 || qx >= n || qy >= n) continue;
                                if (mask[qy * n + qx] != 0 && ox * ox + oy * oy <= 5) near = true;
                            }
                        c = near ? ink : new Color32(250, 250, 250, 0);
                    }
                    px32[py * n + px] = c;
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "lv_sheep", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels32(px32);
            t.Apply(false, false);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.04f), n / 0.95f, 0, SpriteMeshType.FullRect);
        });
    }
}
