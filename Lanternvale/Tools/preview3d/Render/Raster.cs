// Software rasterizer for the preview: perspective-correct triangles with near-plane clipping and Unity's culling
// rules, a deferred opaque pass (G-buffer → LV_Shade per pixel), an ink outline post-pass approximating the
// inverted-hull Outline shader, sorted transparent passes (LitTransparent, Shadow, Additive) and a supersampled
// resolve to sRGB.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Lanternvale.Preview
{
    public enum DrawKind { Sky, Opaque, Terrain, LitTransparent, Shadow, Additive }

    /// <summary>One mesh draw with its material state resolved (colours already in the working space).</summary>
    public sealed class DrawCall
    {
        public string Name = "";
        public Mesh Mesh;
        public Matrix4x4 M = Matrix4x4.identity;
        public DrawKind Kind;
        public bool CullBack = true;
        public bool Outlined;
        public int ObjId = -1;
        public V3 Tint = new V3(1, 1, 1);
        public V3 ColorRgb = new V3(1, 1, 1);
        public float ColorA = 1f;
        public float Emission, FogScale = 1f, Rim, Softness = 0.65f, Fade = 1f;
        public Sampler Tex, Tex2;
        public float Planar, Planar2, TexStrength = 1f;
        public V3 Avg1, Avg2;
        public Vector2 TexOffset;
        public int Queue = 2000, Order;
        public float SortDist;
    }

    public sealed class Frame
    {
        public readonly int W, H;
        public readonly ViewCam Cam;
        public readonly Lighting L;
        public readonly float[] Depth;      // view depth of the front opaque surface (+inf = sky)
        public readonly float[] C;          // colour, rgb interleaved (working space)
        readonly float[] G;                 // G-buffer: n(3) albedo(3) emission fogScale rim world(3)
        const int GS = 12;
        public readonly int[] Obj;          // renderer id of the front opaque surface (−1 = none / unlit)
        readonly List<bool> outlined = new List<bool>();
        readonly List<float> fades = new List<float>();
        public int Triangles, Fragments;

        public Frame(ViewCam cam, Lighting l)
        {
            Cam = cam; L = l; W = cam.W; H = cam.H;
            int n = W * H;
            Depth = new float[n];
            C = new float[n * 3];
            G = new float[n * GS];
            Obj = new int[n];
            Array.Fill(Depth, float.PositiveInfinity);
            Array.Fill(Obj, -1);
        }

        public int NewObject(bool isOutlined, float fade = 1f) { outlined.Add(isOutlined); fades.Add(fade); return outlined.Count - 1; }

        /// <summary>LV_Dither: interleaved gradient noise of a pixel (the dither dissolve of _Fade).</summary>
        public static float Dither(float px, float py)
        {
            float d = px * 0.06711056f + py * 0.00583715f;
            d -= (float)Math.Floor(d);
            float r = 52.9829189f * d;
            return r - (float)Math.Floor(r);
        }

        // ================================================================== geometry

        struct CV
        {
            public float wx, wy, wz, nx, ny, nz, r, g, b, a, u, v, vx, vy, vz;

            public static CV Lerp(in CV p, in CV q, float t)
            {
                CV o;
                o.wx = p.wx + (q.wx - p.wx) * t; o.wy = p.wy + (q.wy - p.wy) * t; o.wz = p.wz + (q.wz - p.wz) * t;
                o.nx = p.nx + (q.nx - p.nx) * t; o.ny = p.ny + (q.ny - p.ny) * t; o.nz = p.nz + (q.nz - p.nz) * t;
                o.r = p.r + (q.r - p.r) * t; o.g = p.g + (q.g - p.g) * t; o.b = p.b + (q.b - p.b) * t; o.a = p.a + (q.a - p.a) * t;
                o.u = p.u + (q.u - p.u) * t; o.v = p.v + (q.v - p.v) * t;
                o.vx = p.vx + (q.vx - p.vx) * t; o.vy = p.vy + (q.vy - p.vy) * t; o.vz = p.vz + (q.vz - p.vz) * t;
                return o;
            }
        }

        public void Draw(DrawCall d)
        {
            var mesh = d.Mesh;
            if (mesh == null || mesh.T.Count < 3) return;
            var V = mesh.V;
            int nv = V.Count;
            var M = d.M;
            var Nm = M.inverse.transpose;
            bool mirrored = M.determinant < 0f;
            var cv = new CV[nv];
            var cam = Cam;
            bool hasN = mesh.N.Count == nv, hasC = mesh.C.Count == nv, hasUV = mesh.UV0.Count == nv;
            for (int i = 0; i < nv; i++)
            {
                var w = M.MultiplyPoint3x4(V[i]);
                var n = hasN ? Nm.MultiplyVector(mesh.N[i]).normalized : Vector3.zero;
                var c = hasC ? mesh.C[i] : Color.white;
                var uv = hasUV ? mesh.UV0[i] : Vector2.zero;
                var rel = w - cam.Pos;
                cv[i] = new CV
                {
                    wx = w.x, wy = w.y, wz = w.z, nx = n.x, ny = n.y, nz = n.z, r = c.r, g = c.g, b = c.b, a = c.a, u = uv.x + d.TexOffset.x, v = uv.y + d.TexOffset.y,
                    vx = Vector3.Dot(rel, cam.R), vy = Vector3.Dot(rel, cam.U), vz = Vector3.Dot(rel, cam.F),
                };
            }
            var T = mesh.T;
            float near = cam.Near, sx = cam.TanHalf * cam.Aspect, sy = cam.TanHalf;
            var poly = new CV[4];
            var clipped = new CV[5];
            for (int t = 0; t + 2 < T.Count; t += 3)
            {
                ref var a = ref cv[T[t]];
                ref var b = ref cv[T[t + 1]];
                ref var c = ref cv[T[t + 2]];
                if (a.vz < near && b.vz < near && c.vz < near) continue;
                if (a.vz > cam.Far && b.vz > cam.Far && c.vz > cam.Far) continue;
                // frustum side planes (x/z beyond ±tan)
                if (a.vx > a.vz * sx && b.vx > b.vz * sx && c.vx > c.vz * sx) continue;
                if (a.vx < -a.vz * sx && b.vx < -b.vz * sx && c.vx < -c.vz * sx) continue;
                if (a.vy > a.vz * sy && b.vy > b.vz * sy && c.vy > c.vz * sy) continue;
                if (a.vy < -a.vz * sy && b.vy < -b.vz * sy && c.vy < -c.vz * sy) continue;
                // facing (Unity: Cross(b−a, c−a) points at the viewer for a front face; mirrored transforms flip it)
                float e1x = b.wx - a.wx, e1y = b.wy - a.wy, e1z = b.wz - a.wz, e2x = c.wx - a.wx, e2y = c.wy - a.wy, e2z = c.wz - a.wz;
                float fx = e1y * e2z - e1z * e2y, fy = e1z * e2x - e1x * e2z, fz = e1x * e2y - e1y * e2x;
                float facing = fx * (cam.Pos.x - a.wx) + fy * (cam.Pos.y - a.wy) + fz * (cam.Pos.z - a.wz);
                bool front = (facing > 0f) != mirrored;
                if (d.Kind != DrawKind.Sky && d.CullBack && !front) continue;
                if (!hasN)
                {
                    float fl = (float)Math.Sqrt(fx * fx + fy * fy + fz * fz);
                    if (fl < 1e-12f) continue;
                    float s = (mirrored ? -1f : 1f) / fl;
                    a.nx = b.nx = c.nx = fx * s; a.ny = b.ny = c.ny = fy * s; a.nz = b.nz = c.nz = fz * s;
                }
                // texel density of uv-mapped textures (texels per metre) for the mip choice
                float tpm = 0f;
                if (d.Tex != null && (d.Kind == DrawKind.LitTransparent || d.Kind == DrawKind.Additive || (d.Kind == DrawKind.Opaque && d.Planar <= 0f)))
                {
                    float wa = (float)Math.Sqrt(fx * fx + fy * fy + fz * fz);
                    float ua = Math.Abs((b.u - a.u) * (c.v - a.v) - (c.u - a.u) * (b.v - a.v)) * d.Tex.Width * d.Tex.Height;
                    tpm = wa > 1e-9f ? (float)Math.Sqrt(ua / wa) : 0f;
                }
                Triangles++;
                if (a.vz >= near && b.vz >= near && c.vz >= near) { Raster(in a, in b, in c, d, front, tpm); continue; }
                // clip against the near plane
                poly[0] = a; poly[1] = b; poly[2] = c;
                int n = 0;
                for (int i = 0; i < 3; i++)
                {
                    ref var p = ref poly[i];
                    ref var q = ref poly[(i + 1) % 3];
                    bool pin = p.vz >= near, qin = q.vz >= near;
                    if (pin) clipped[n++] = p;
                    if (pin != qin) clipped[n++] = CV.Lerp(in p, in q, (near - p.vz) / (q.vz - p.vz));
                }
                for (int i = 1; i + 1 < n; i++) Raster(in clipped[0], in clipped[i], in clipped[i + 1], d, front, tpm);
            }
        }

        void Raster(in CV a, in CV b, in CV c, DrawCall d, bool front, float tpm)
        {
            var cam = Cam;
            float hw = W * 0.5f, hh = H * 0.5f;
            float kx = hw / (cam.TanHalf * cam.Aspect), ky = hh / cam.TanHalf;
            float ax = hw + a.vx / a.vz * kx, ay = hh - a.vy / a.vz * ky;
            float bx = hw + b.vx / b.vz * kx, by = hh - b.vy / b.vz * ky;
            float cx = hw + c.vx / c.vz * kx, cy = hh - c.vy / c.vz * ky;
            float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Math.Abs(area) < 1e-9f) return;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), x1 = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), y1 = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            if (x0 > x1 || y0 > y1) return;
            float ia = 1f / a.vz, ib = 1f / b.vz, ic = 1f / c.vz;
            float inv = 1f / area;
            float pixelK = 2f * cam.TanHalf / H;   // world size of a pixel per metre of depth
            float sgn = front ? 1f : -1f;
            var L = this.L;
            for (int y = y0; y <= y1; y++)
            {
                float py = y + 0.5f;
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f;
                    float w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) * inv;
                    float w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) * inv;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -1e-5f || w1 < -1e-5f || w2 < -1e-5f) continue;
                    float iz = w0 * ia + w1 * ib + w2 * ic;
                    float z = 1f / iz;
                    int idx = y * W + x;
                    if (d.Kind != DrawKind.Sky && z >= Depth[idx]) continue;
                    if (d.Fade < 0.999f && d.Fade - Dither(px, H - py) - 0.001f < 0f) continue;   // clip(_Fade − dither)
                    float p0 = w0 * ia * z, p1 = w1 * ib * z, p2 = w2 * ic * z;
                    float wx = p0 * a.wx + p1 * b.wx + p2 * c.wx, wy = p0 * a.wy + p1 * b.wy + p2 * c.wy, wz = p0 * a.wz + p1 * b.wz + p2 * c.wz;
                    float nx = p0 * a.nx + p1 * b.nx + p2 * c.nx, ny = p0 * a.ny + p1 * b.ny + p2 * c.ny, nz = p0 * a.nz + p1 * b.nz + p2 * c.nz;
                    float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (nl > 1e-6f) { nx *= sgn / nl; ny *= sgn / nl; nz *= sgn / nl; }
                    float cr = p0 * a.r + p1 * b.r + p2 * c.r, cg = p0 * a.g + p1 * b.g + p2 * c.g, cb = p0 * a.b + p1 * b.b + p2 * c.b, ca = p0 * a.a + p1 * b.a + p2 * c.a;
                    float u = p0 * a.u + p1 * b.u + p2 * c.u, v = p0 * a.v + p1 * b.v + p2 * c.v;
                    Fragments++;
                    switch (d.Kind)
                    {
                        case DrawKind.Sky:
                        {
                            if (!float.IsPositiveInfinity(Depth[idx])) break;
                            C[idx * 3] = Lighting.Lin(cr) * d.Tint.x; C[idx * 3 + 1] = Lighting.Lin(cg) * d.Tint.y; C[idx * 3 + 2] = Lighting.Lin(cb) * d.Tint.z;
                            break;
                        }
                        case DrawKind.Opaque:
                        case DrawKind.Terrain:
                        {
                            float ar = Lighting.Lin(cr), ag = Lighting.Lin(cg), ab = Lighting.Lin(cb), em;
                            if (d.Kind == DrawKind.Terrain)
                            {
                                float cosv = CosView(wx, wy, wz, nx, ny, nz);
                                float pix = z * pixelK / Math.Max(cosv, 0.3f);
                                d.Tex.Sample(wx * d.Planar, wy * d.Planar, pix * d.Tex.Width * d.Planar, out var gr, out var gg, out var gb, out _);
                                d.Tex2.Sample(wx * d.Planar2, wy * d.Planar2, pix * d.Tex2.Width * d.Planar2, out var sr, out var sg, out var sb, out _);
                                float bl = Mathf.Clamp01(u), st = Mathf.Clamp01(v);
                                float tr = gr + (sr - gr) * bl, tg = gg + (sg - gg) * bl, tb = gb + (sb - gb) * bl;
                                float vr = d.Avg1.x + (d.Avg2.x - d.Avg1.x) * bl, vg = d.Avg1.y + (d.Avg2.y - d.Avg1.y) * bl, vb = d.Avg1.z + (d.Avg2.z - d.Avg1.z) * bl;
                                ar *= vr + (tr - vr) * st; ag *= vg + (tg - vg) * st; ab *= vb + (tb - vb) * st;
                                em = ca;
                            }
                            else
                            {
                                ar *= d.ColorRgb.x * d.Tint.x; ag *= d.ColorRgb.y * d.Tint.y; ab *= d.ColorRgb.z * d.Tint.z;
                                if (d.Tex != null)
                                {
                                    float cosv = CosView(wx, wy, wz, nx, ny, nz);
                                    float tu = d.Planar > 0f ? wx * d.Planar : u, tv = d.Planar > 0f ? wy * d.Planar : v;
                                    float tpp = z * pixelK / Math.Max(cosv, 0.3f) * (d.Planar > 0f ? d.Tex.Width * d.Planar : tpm);
                                    d.Tex.Sample(tu, tv, tpp, out var tr, out var tg, out var tb, out _);
                                    float s = d.TexStrength;
                                    ar *= 1f + (tr - 1f) * s; ag *= 1f + (tg - 1f) * s; ab *= 1f + (tb - 1f) * s;
                                }
                                em = ca;
                            }
                            Depth[idx] = z;
                            Obj[idx] = d.ObjId;
                            int g = idx * GS;
                            G[g] = nx; G[g + 1] = ny; G[g + 2] = nz; G[g + 3] = ar; G[g + 4] = ag; G[g + 5] = ab;
                            G[g + 6] = em; G[g + 7] = d.FogScale; G[g + 8] = d.Rim; G[g + 9] = wx; G[g + 10] = wy; G[g + 11] = wz;
                            break;
                        }
                        case DrawKind.LitTransparent:
                        {
                            float tr = 1f, tg = 1f, tb = 1f, ta = 1f;
                            if (d.Tex != null)
                            {
                                float cosv = CosView(wx, wy, wz, nx, ny, nz);
                                d.Tex.Sample(u, v, z * pixelK / Math.Max(cosv, 0.3f) * tpm, out tr, out tg, out tb, out ta);
                            }
                            float alpha = ta * ca * d.ColorA;
                            if (alpha <= 0.002f) break;
                            var albedo = new V3(tr * Lighting.Lin(cr) * d.ColorRgb.x, tg * Lighting.Lin(cg) * d.ColorRgb.y, tb * Lighting.Lin(cb) * d.ColorRgb.z);
                            var col = L.Shade(new V3(wx, wy, wz), new V3(nx, ny, nz), albedo, d.Emission, 0f, d.FogScale);
                            Blend(idx, col, Math.Min(1f, alpha));
                            break;
                        }
                        case DrawKind.Shadow:
                        {
                            float su = u * 2f - 1f, sv = v * 2f - 1f;
                            float dd = (float)Math.Sqrt(su * su + sv * sv);
                            float t = Mathf.Clamp01((dd - (1f - d.Softness)) / Math.Max(d.Softness, 1e-4f));
                            float alpha = d.ColorA * (1f - t * t * (3f - 2f * t)) * ca;
                            if (alpha <= 0.002f) break;
                            Blend(idx, d.ColorRgb, alpha);
                            break;
                        }
                        case DrawKind.Additive:
                        {
                            float tr = 1f, tg = 1f, tb = 1f, ta = 1f;
                            if (d.Tex != null) d.Tex.Sample(u, v, z * pixelK * tpm, out tr, out tg, out tb, out ta);
                            float alpha = ta * ca * d.ColorA;
                            float dist = (float)Math.Sqrt((wx - L.CamPos.x) * (wx - L.CamPos.x) + (wy - L.CamPos.y) * (wy - L.CamPos.y) + (wz - L.CamPos.z) * (wz - L.CamPos.z));
                            alpha *= 1f - L.FogAmount(dist, d.FogScale);
                            if (alpha <= 0.001f) break;
                            C[idx * 3] += tr * cr * d.ColorRgb.x * alpha;
                            C[idx * 3 + 1] += tg * cg * d.ColorRgb.y * alpha;
                            C[idx * 3 + 2] += tb * cb * d.ColorRgb.z * alpha;
                            break;
                        }
                    }
                }
            }
        }

        float CosView(float wx, float wy, float wz, float nx, float ny, float nz)
        {
            float vx = Cam.Pos.x - wx, vy = Cam.Pos.y - wy, vz = Cam.Pos.z - wz;
            float l = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
            return l > 1e-6f ? Math.Abs(vx * nx + vy * ny + vz * nz) / l : 1f;
        }

        void Blend(int idx, V3 col, float a)
        {
            int k = idx * 3;
            C[k] += (col.x - C[k]) * a; C[k + 1] += (col.y - C[k + 1]) * a; C[k + 2] += (col.z - C[k + 2]) * a;
        }

        // ================================================================== passes

        /// <summary>Lights every opaque pixel of the G-buffer (LV_Shade).</summary>
        public void Resolve()
        {
            Parallel.For(0, H, y =>
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    if (float.IsPositiveInfinity(Depth[idx])) continue;
                    int g = idx * GS;
                    var col = L.Shade(new V3(G[g + 9], G[g + 10], G[g + 11]), new V3(G[g], G[g + 1], G[g + 2]), new V3(G[g + 3], G[g + 4], G[g + 5]), G[g + 6], G[g + 8], G[g + 7]);
                    C[idx * 3] = col.x; C[idx * 3 + 1] = col.y; C[idx * 3 + 2] = col.z;
                }
            });
        }

        /// <summary>
        /// Ink outline (Lanternvale/Outline: an inverted hull pushed out by a constant pixel width, depth-tested): pixels
        /// within `radius` of a nearer outlined surface, or just behind a depth crease of one, take the ink colour at
        /// that surface's depth (and its depth, so later transparents stay behind it).
        /// </summary>
        public void Ink(int radius, Color ink)
        {
            if (radius <= 0) return;
            var offs = new List<(int dx, int dy)>();
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                    if ((dx != 0 || dy != 0) && dx * dx + dy * dy <= radius * radius + radius) offs.Add((dx, dy));
            var inkLin = Lighting.Lin(ink);
            var newDepth = (float[])Depth.Clone();
            var newC = (float[])C.Clone();
            var ol = outlined.ToArray();
            var fd = fades.ToArray();
            Parallel.For(0, H, y =>
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = y * W + x;
                    float dp = Depth[idx];
                    int op = Obj[idx];
                    float best = float.PositiveInfinity;
                    int bestObj = -1;
                    foreach (var (dx, dy) in offs)
                    {
                        int qx = x + dx, qy = y + dy;
                        if (qx < 0 || qy < 0 || qx >= W || qy >= H) continue;
                        int q = qy * W + qx;
                        int oq = Obj[q];
                        if (oq < 0 || !ol[oq]) continue;
                        float dq = Depth[q];
                        if (dq >= best) continue;
                        if (oq != op) { if (dq < dp - (0.02f * dq + 0.04f)) { best = dq; bestObj = oq; } }
                        else if (dp - dq > 0.12f * dq + 0.25f) { best = dq; bestObj = oq; }   // crease inside one model
                    }
                    if (float.IsPositiveInfinity(best)) continue;
                    if (fd[bestObj] < 0.999f && fd[bestObj] - Dither(x + 0.5f, H - y - 0.5f) - 0.001f < 0f) continue;   // the hull dithers too
                    // the hull's fog: at the outlined surface's distance (view depth ≈ distance near the centre)
                    float fog = L.FogAmount(best, 1f);
                    var c = V3.Lerp(inkLin, L.FogCol, fog);
                    newC[idx * 3] = c.x; newC[idx * 3 + 1] = c.y; newC[idx * 3 + 2] = c.z;
                    newDepth[idx] = best;
                }
            });
            Array.Copy(newDepth, Depth, Depth.Length);
            Array.Copy(newC, C, C.Length);
        }

        /// <summary>Box-filters ss×ss blocks and encodes sRGB (or clamps in gamma mode): top-down RGB bytes.</summary>
        public byte[] Output(int ss, out int ow, out int oh)
        {
            ow = W / ss; oh = H / ss;
            int w = ow, h = oh;
            var px = new byte[w * h * 3];
            float k = 1f / (ss * ss);
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                    for (int ch = 0; ch < 3; ch++)
                    {
                        float s = 0f;
                        for (int sy = 0; sy < ss; sy++)
                            for (int sx = 0; sx < ss; sx++)
                                s += Math.Max(0f, C[((y * ss + sy) * W + x * ss + sx) * 3 + ch]);
                        s *= k;
                        float o = Lighting.Linear ? Mathf.LinearToGammaSpace(s) : s;
                        px[(y * w + x) * 3 + ch] = (byte)Math.Clamp((int)(o * 255f + 0.5f), 0, 255);
                    }
            });
            return px;
        }
    }
}
