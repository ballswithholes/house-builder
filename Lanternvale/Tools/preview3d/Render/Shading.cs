// The Lanternvale lighting model (Resources/Shaders/LanternvaleCommon.cginc) on the CPU, plus the view camera and
// texture sampling. Colours are in the working space: linear (like a Linear-colour-space Unity project; --gamma
// switches to the gamma pipeline), vertex colours and textures are sRGB and converted on read.
using System;
using System.Collections.Generic;
using Lanternvale.Game;
using UnityEngine;

namespace Lanternvale.Preview
{
    public struct V3
    {
        public float x, y, z;
        public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static V3 From(Vector3 v) => new V3(v.x, v.y, v.z);
        public static V3 From(Color c) => new V3(c.r, c.g, c.b);
        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 operator *(V3 a, float k) => new V3(a.x * k, a.y * k, a.z * k);
        public static V3 operator *(V3 a, V3 b) => new V3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static float Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static V3 Lerp(V3 a, V3 b, float t) => new V3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
    }

    /// <summary>A perspective camera (Unity conventions: looks along +Z of its rotation, vertical FOV).</summary>
    public sealed class ViewCam
    {
        public Vector3 Pos, F, R, U;
        public float TanHalf, Aspect, Near = 0.3f, Far = 600f, FovDeg;
        public int W, H;

        public static ViewCam Create(Vector3 pos, Quaternion rot, float fovDeg, int w, int h)
        {
            return new ViewCam
            {
                Pos = pos, F = (rot * Vector3.forward).normalized, R = (rot * Vector3.right).normalized, U = (rot * Vector3.up).normalized,
                FovDeg = fovDeg, TanHalf = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad), Aspect = (float)w / h, W = w, H = h,
            };
        }
    }

    /// <summary>SceneLighting's globals in the working colour space, and LV_Shade.</summary>
    public sealed class Lighting
    {
        public const int MaxLights = 16;
        public static bool Linear = true;
        public V3 SunDir, SunCol, SkyAmb, GroundAmb, FogCol, RimCol, CamPos;
        public float FogStart, FogEnd, FogMax, RimPower, NightGlow;
        public int LightCount;
        public readonly V3[] LPos = new V3[MaxLights];
        public readonly V3[] LCol = new V3[MaxLights];
        public readonly float[] LRange = new float[MaxLights];

        public static float Lin(float c) => Linear ? Mathf.GammaToLinearSpace(c) : c;
        public static V3 Lin(Color c) => new V3(Lin(c.r), Lin(c.g), Lin(c.b));
        static V3 Working(Color c, float k) => Lin(c) * k;

        static readonly float[] LinLut = BuildLut();
        static float[] BuildLut() { var t = new float[256]; for (int i = 0; i < 256; i++) t[i] = Mathf.GammaToLinearSpace(i / 255f); return t; }
        public static float LinByte(byte b) => Linear ? LinLut[b] : b / 255f;

        /// <summary>Reads SceneLighting (after DayNight.ApplyTo and the map's fog), like SceneLighting.Push.</summary>
        public static Lighting FromScene(Vector3 camPos, IList<(Vector3 pos, Color color, float intensity, float range)> lights, Vector3 focus)
        {
            var l = new Lighting();
            var sd = SceneLighting.SunDirection.sqrMagnitude > 1e-6f ? SceneLighting.SunDirection.normalized : new Vector3(0f, 0f, -1f);
            l.SunDir = V3.From(sd);
            l.SunCol = Working(SceneLighting.SunColor, SceneLighting.SunIntensity);
            l.SkyAmb = Working(SceneLighting.SkyAmbient, SceneLighting.AmbientIntensity);
            l.GroundAmb = Working(SceneLighting.GroundAmbient, SceneLighting.AmbientIntensity);
            l.FogCol = Working(SceneLighting.FogColor, 1f);
            l.FogMax = Mathf.Clamp01(SceneLighting.FogMax);
            l.FogStart = SceneLighting.FogStart;
            l.FogEnd = Mathf.Max(SceneLighting.FogStart + 0.01f, SceneLighting.FogEnd);
            l.RimCol = Working(SceneLighting.RimColor, 1f);
            l.RimPower = SceneLighting.RimPower;
            l.NightGlow = SceneLighting.NightGlow;
            l.CamPos = V3.From(camPos);
            // the 16 strongest near the focus, scored like SceneLighting.Push
            var chosen = new List<(float score, int i)>();
            for (int i = 0; i < lights.Count; i++)
            {
                var p = lights[i];
                if (p.intensity <= 0.001f || p.range <= 0.01f) continue;
                float d = Vector3.Distance(p.pos, focus);
                chosen.Add((p.intensity * p.range / (1f + d * d * 0.01f), i));
            }
            chosen.Sort((a, b) => b.score.CompareTo(a.score));
            for (int k = 0; k < chosen.Count && k < MaxLights; k++)
            {
                var p = lights[chosen[k].i];
                l.LPos[k] = V3.From(p.pos);
                l.LRange[k] = p.range;
                l.LCol[k] = Working(p.color, p.intensity);
                l.LightCount = k + 1;
            }
            return l;
        }

        public float FogAmount(float dist, float fogScale) =>
            Mathf.Clamp01((dist - FogStart) / Math.Max(FogEnd - FogStart, 0.01f)) * FogMax * fogScale;

        /// <summary>LV_Light: wrapped sun, hemisphere ambient (up = −Z), point lights.</summary>
        public V3 Light(V3 wp, V3 n)
        {
            float ndl = V3.Dot(n, SunDir);
            float wrap = Mathf.Clamp01((ndl + 0.45f) / 1.45f);
            float ramp = wrap * wrap * (3f - 2f * wrap);
            var light = SunCol * ramp;
            float hemi = -n.z * 0.5f + 0.5f;
            light = light + V3.Lerp(GroundAmb, SkyAmb, hemi);
            for (int i = 0; i < LightCount; i++)
            {
                var d = LPos[i] - wp;
                float dist2 = Math.Max(V3.Dot(d, d), 0.0001f);
                float r = Math.Max(LRange[i], 0.01f);
                float att = Mathf.Clamp01(1f - dist2 / (r * r));
                if (att <= 0f) continue;
                att *= att;
                float pl = Mathf.Clamp01((V3.Dot(n, d) / (float)Math.Sqrt(dist2) + 0.6f) / 1.6f);
                light = light + LCol[i] * (att * pl);
            }
            return light;
        }

        /// <summary>LV_Shade: lit albedo + rim + emission, then fog.</summary>
        public V3 Shade(V3 wp, V3 n, V3 albedo, float emission, float rimBoost, float fogScale)
        {
            var v = CamPos - wp;
            float dist = (float)Math.Sqrt(V3.Dot(v, v));
            var view = dist > 1e-5f ? v * (1f / dist) : new V3(0, 0, -1);
            var light = Light(wp, n);
            var col = albedo * light;
            float rim = Mathf.Pow(1f - Mathf.Clamp01(V3.Dot(n, view)), Math.Max(RimPower, 0.5f));
            float lum = light.x * 0.3f + light.y * 0.59f + light.z * 0.11f;
            col = col + RimCol * (rim * (0.22f * Mathf.Clamp01(lum) + rimBoost));
            float e = Mathf.Clamp01(emission);
            if (e > 0f) col = V3.Lerp(col, albedo * (1f + NightGlow), e);
            float fog = FogAmount(dist, fogScale);
            return V3.Lerp(col, FogCol, fog);
        }
    }

    /// <summary>Mip-mapped bilinear sampler over a Texture2D (colour converted to the working space, alpha linear).</summary>
    public sealed class Sampler
    {
        readonly List<float[]> levels = new List<float[]>();   // rgba interleaved
        readonly List<int> ws = new List<int>(), hs = new List<int>();
        public readonly bool Repeat;
        public int Width => ws[0];
        public int Height => hs[0];

        static readonly Dictionary<Texture, Sampler> cache = new Dictionary<Texture, Sampler>();

        public static Sampler For(Texture t)
        {
            if (!(t is Texture2D t2) || t2.Pixels == null || t2.Pixels.Length == 0) return null;
            lock (cache)
            {
                if (cache.TryGetValue(t, out var s)) return s;
                s = new Sampler(t2);
                cache[t] = s;
                return s;
            }
        }

        Sampler(Texture2D t)
        {
            Repeat = t.wrapMode == TextureWrapMode.Repeat;
            int w = t.width, h = t.height;
            var l0 = new float[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                var c = t.Pixels[i];
                l0[i * 4] = Lighting.LinByte(c.r); l0[i * 4 + 1] = Lighting.LinByte(c.g); l0[i * 4 + 2] = Lighting.LinByte(c.b); l0[i * 4 + 3] = c.a / 255f;
            }
            levels.Add(l0); ws.Add(w); hs.Add(h);
            while (w > 1 || h > 1)
            {
                int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
                var prev = levels[levels.Count - 1];
                var next = new float[nw * nh * 4];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                        for (int k = 0; k < 4; k++)
                        {
                            int x0 = Math.Min(w - 1, x * 2), x1 = Math.Min(w - 1, x * 2 + 1), y0 = Math.Min(h - 1, y * 2), y1 = Math.Min(h - 1, y * 2 + 1);
                            next[(y * nw + x) * 4 + k] = 0.25f * (prev[(y0 * w + x0) * 4 + k] + prev[(y0 * w + x1) * 4 + k] + prev[(y1 * w + x0) * 4 + k] + prev[(y1 * w + x1) * 4 + k]);
                        }
                levels.Add(next); ws.Add(nw); hs.Add(nh);
                w = nw; h = nh;
            }
        }

        /// <summary>Average colour (the smallest mip).</summary>
        public void Average(out float r, out float g, out float b, out float a)
        {
            var l = levels[levels.Count - 1];
            r = l[0]; g = l[1]; b = l[2]; a = l[3];
        }

        /// <summary>Bilinear sample at uv (0..1, v up) with a mip level from texels-per-pixel.</summary>
        public void Sample(float u, float v, float texelsPerPixel, out float r, out float g, out float b, out float a)
        {
            int lod = texelsPerPixel <= 1f ? 0 : Math.Min(levels.Count - 1, (int)Math.Round(Math.Log(texelsPerPixel, 2)));
            var px = levels[lod];
            int w = ws[lod], h = hs[lod];
            float fx = u * w - 0.5f, fy = v * h - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            int x1 = x0 + 1, y1 = y0 + 1;
            if (Repeat) { x0 = Mod(x0, w); x1 = Mod(x1, w); y0 = Mod(y0, h); y1 = Mod(y1, h); }
            else { x0 = Clamp(x0, w); x1 = Clamp(x1, w); y0 = Clamp(y0, h); y1 = Clamp(y1, h); }
            int i00 = (y0 * w + x0) * 4, i10 = (y0 * w + x1) * 4, i01 = (y1 * w + x0) * 4, i11 = (y1 * w + x1) * 4;
            float w00 = (1 - tx) * (1 - ty), w10 = tx * (1 - ty), w01 = (1 - tx) * ty, w11 = tx * ty;
            r = px[i00] * w00 + px[i10] * w10 + px[i01] * w01 + px[i11] * w11;
            g = px[i00 + 1] * w00 + px[i10 + 1] * w10 + px[i01 + 1] * w01 + px[i11 + 1] * w11;
            b = px[i00 + 2] * w00 + px[i10 + 2] * w10 + px[i01 + 2] * w01 + px[i11 + 2] * w11;
            a = px[i00 + 3] * w00 + px[i10 + 3] * w10 + px[i01 + 3] * w01 + px[i11 + 3] * w11;
        }

        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }
        static int Clamp(int a, int m) => a < 0 ? 0 : a >= m ? m - 1 : a;
    }
}
