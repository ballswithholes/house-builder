// White silhouettes of sprites, generated once per sprite: a "fill" (exact shape, used for hit
// flashes and hover brightening) and an "outline" (dilated shape drawn behind the sprite in a
// colour, used for hover/target outlines). No custom shaders: the alpha is read back once (CPU
// copy for readable textures, GPU blit + ReadPixels otherwise), downscaled, dilated and cached.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class Silhouette
    {
        public Sprite Fill;     // white, same footprint/pivot as the source
        public Sprite Outline;  // white, dilated by OutlinePx texels
    }

    public static class Silhouettes
    {
        const int MaxDim = 320;
        static readonly Dictionary<int, Silhouette> Cache = new Dictionary<int, Silhouette>();
        static bool failed; // readback unavailable (e.g. -nographics): stop trying

        /// <summary>
        /// Silhouette sprites for s, or null when they can't be produced (callers fall back to colour tints).
        /// Outline thickness is about outlineMetres in world units.
        /// </summary>
        public static Silhouette Get(Sprite s, float outlineMetres = 0.05f)
        {
            if (s == null || failed) return null;
            int key = s.GetInstanceID();
            if (Cache.TryGetValue(key, out var sil)) return sil;
            sil = null;
            try { sil = Build(s, outlineMetres); }
            catch (Exception e)
            {
                Debug.LogWarning("[Lanternvale] Silhouette generation unavailable: " + e.Message);
                failed = true;
            }
            Cache[key] = sil;
            return sil;
        }

        static Silhouette Build(Sprite s, float outlineMetres)
        {
            var tex = s.texture;
            if (tex == null) return null;
            var rect = s.textureRect;
            int srcW = Mathf.Max(1, Mathf.RoundToInt(rect.width)), srcH = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            float f = Mathf.Min(1f, MaxDim / (float)Mathf.Max(srcW, srcH));
            int w = Mathf.Max(4, Mathf.RoundToInt(srcW * f)), h = Mathf.Max(4, Mathf.RoundToInt(srcH * f));
            var alpha = ReadAlpha(tex, rect, w, h);
            if (alpha == null) return null;

            float ppu = s.pixelsPerUnit * (w / (float)srcW);
            int radius = Mathf.Clamp(Mathf.RoundToInt(outlineMetres * ppu), 1, 8);
            int pad = radius + 2;
            int W = w + pad * 2, H = h + pad * 2;

            var fill = new byte[W * H];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(alpha, y * w, fill, (y + pad) * W + pad, w);
            var dil = Dilate(fill, W, H, radius);

            var pivotPx = new Vector2(s.pivot.x * (w / (float)srcW) + pad, s.pivot.y * (h / (float)srcH) + pad);
            var pivot = new Vector2(pivotPx.x / W, pivotPx.y / H);
            return new Silhouette
            {
                Fill = MakeSprite("sil_fill_" + s.name, fill, W, H, pivot, ppu),
                Outline = MakeSprite("sil_outline_" + s.name, dil, W, H, pivot, ppu),
            };
        }

        static Sprite MakeSprite(string name, byte[] a, int w, int h, Vector2 pivot, float ppu)
        {
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, a[i]);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels32(px);
            t.Apply(false, true);
            var sp = Sprite.Create(t, new Rect(0, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
            sp.name = name;
            return sp;
        }

        static byte[] ReadAlpha(Texture2D tex, Rect rect, int w, int h)
        {
            var px = TextureReadback.Read(tex, rect, w, h);
            if (px == null) return null;
            var a = new byte[w * h];
            for (int i = 0; i < a.Length && i < px.Length; i++) a[i] = px[i].a;
            return a;
        }

        /// <summary>Separable max-filter dilation followed by a light 3×3 blur (rounded corners).</summary>
        static byte[] Dilate(byte[] src, int w, int h, int r)
        {
            var tmp = new byte[w * h];
            var dst = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    int m = 0, x0 = Mathf.Max(0, x - r), x1 = Mathf.Min(w - 1, x + r);
                    for (int k = x0; k <= x1; k++) { int v = src[row + k]; if (v > m) m = v; }
                    tmp[row + x] = (byte)m;
                }
            }
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    int m = 0, y0 = Mathf.Max(0, y - r), y1 = Mathf.Min(h - 1, y + r);
                    for (int k = y0; k <= y1; k++) { int v = tmp[k * w + x]; if (v > m) m = v; }
                    dst[y * w + x] = (byte)m;
                }
            }
            // 3x3 box blur softens the square corners of the max filter
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int sum = 0;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        int row = (y + oy) * w + x;
                        sum += dst[row - 1] + dst[row] + dst[row + 1];
                    }
                    tmp[y * w + x] = (byte)(sum / 9);
                }
            return tmp;
        }
    }

    /// <summary>One-off CPU copies of (possibly non-readable) textures, downscaled.</summary>
    public static class TextureReadback
    {
        /// <summary>
        /// Pixels of a texture region resampled to w×h (rows bottom-up). Uses a CPU copy for readable
        /// textures, otherwise a GPU blit + ReadPixels. Throws when no GPU readback is possible.
        /// </summary>
        public static Color32[] Read(Texture2D tex, Rect rect, int w, int h)
        {
            if (tex == null) return null;
            if (tex.isReadable)
            {
                var res = new Color32[w * h];
                for (int y = 0; y < h; y++)
                {
                    float v = (rect.y + (y + 0.5f) / h * rect.height) / tex.height;
                    for (int x = 0; x < w; x++)
                    {
                        float u = (rect.x + (x + 0.5f) / w * rect.width) / tex.width;
                        res[y * w + x] = tex.GetPixelBilinear(u, v);
                    }
                }
                return res;
            }
            if (!SystemInfo.supportsRenderTextures || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("no GPU readback");
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Texture2D tmp = null;
            try
            {
                var scale = new Vector2(rect.width / tex.width, rect.height / tex.height);
                var offset = new Vector2(rect.x / tex.width, rect.y / tex.height);
                Graphics.Blit(tex, rt, scale, offset);
                RenderTexture.active = rt;
                tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                return tmp.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (tmp != null) UnityEngine.Object.Destroy(tmp);
            }
        }
    }
}
