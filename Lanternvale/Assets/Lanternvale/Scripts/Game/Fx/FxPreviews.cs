// Persistent targeting previews lying on the ground plane (Lanternvale/GroundOverlay: unlit, drawn over the terrain and
// the painted decals, hidden behind units and props standing on them): circle, cone, line, path (solid or marching
// dots) and the movement range (a small mask texture baked at nav-cell resolution, upsampled with smooth contours).
// Every shape is a soft fill plus a crisp bright outline edged with a thin dark fringe, so it reads on bright grass as
// well as on dark dirt, by day and by night. Layering (render queue): move range < fills < outlines/dots < end marker.
//
// Call Show* every frame or only when something changes — re-showing an id reuses its sprites.
using System;
using System.Collections.Generic;
using Lanternvale.Util;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class FxSystem
    {
        /// <summary>Height of previews above the ground (the shader's polygon offset does the rest).</summary>
        const float PreviewLift = 0.01f;

        // ground overlay layers (see FxSystem.GroundMat)
        const int LayerRange = 0, LayerFill = 1, LayerLine = 2, LayerMarker = 3;

        enum PreviewKind { Circle, Cone, Line, Path, MoveRange }

        sealed class Preview
        {
            public string id;
            public PreviewKind kind;
            public readonly List<Fx> parts = new List<Fx>();
            public Color color;
            public float age;
            // path
            public readonly List<Vector2> pts = new List<Vector2>();
            public readonly List<float> cum = new List<float>();
            public float total;
            public bool dashed;
            // move range
            public Texture2D tex;
            public Sprite sprite;
            public Color32[] buf;
            public float[] mask, mask2;
        }

        readonly Dictionary<string, Preview> previews = new Dictionary<string, Preview>(StringComparer.Ordinal);
        readonly List<Preview> previewList = new List<Preview>();

        Preview GetPreview(string id, PreviewKind kind)
        {
            id = id ?? "";
            if (previews.TryGetValue(id, out var p))
            {
                if (p.kind != kind) { SetPartCount(p, 0); p.kind = kind; }
                return p;
            }
            p = new Preview { id = id, kind = kind };
            previews[id] = p;
            previewList.Add(p);
            return p;
        }

        void SetPartCount(Preview p, int n)
        {
            while (p.parts.Count < n) p.parts.Add(Acquire(PresentationArt.White, Mat.GroundAlpha, LayerLine));
            while (p.parts.Count > n)
            {
                Release(p.parts[p.parts.Count - 1]);
                p.parts.RemoveAt(p.parts.Count - 1);
            }
        }

        /// <summary>Lays a pooled sprite flat on the ground: centre pos, w × h metres, turned angle° (sprite +x → that direction).</summary>
        void PlaceFlat(Fx f, Sprite s, int layer, Vector2 pos, float w, float h, float angle, Color c)
        {
            if (f.mat != Mat.GroundAlpha || f.layer != layer)
            {
                f.sr.sharedMaterial = GroundMat(false, layer);
                f.mat = Mat.GroundAlpha;
                f.layer = layer;
            }
            if (f.sr.sprite != s) f.sr.sprite = s;
            f.sr.drawMode = SpriteDrawMode.Simple;
            f.t.position = new Vector3(pos.x, pos.y, -PreviewLift);
            f.t.rotation = Quaternion.Euler(0f, 0f, angle);
            Size(f, s.bounds.size, w, h);
            f.sr.color = c;
        }

        /// <summary>PlaceFlat for sprites authored in nominal units (1 unit = the shape's diameter): uniform scale.</summary>
        void PlaceFlatScaled(Fx f, Sprite s, int layer, Vector2 pos, float scale, float angle, Color c)
        {
            PlaceFlat(f, s, layer, pos, 1f, 1f, angle, c);
            f.t.localScale = new Vector3(scale, scale, 1f);
        }

        // ------------------------------------------------------------------ preview art (white line, dark fringe)

        static readonly Dictionary<int, Sprite> previewArt = new Dictionary<int, Sprite>();
        static readonly Color32 Clear32 = new Color32(255, 255, 255, 0);

        delegate Color32 PixelFn(float x, float y);

        static Texture2D Bake(string name, int w, int h, PixelFn fn)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = fn(x + 0.5f, y + 0.5f);
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>
        /// One texel of a preview shape from its signed distance to the outline (pixels, negative inside): a bright line
        /// just inside the edge, a dark fringe just outside, an optional faint fill.
        /// </summary>
        static Color32 Shade(float sd, float lineHalf, float fill)
        {
            float line = Mathf.Clamp01(lineHalf + 0.5f - Mathf.Abs(sd + lineHalf + 0.5f));
            float fringe = Mathf.Clamp01(1f - Mathf.Abs(sd - 1.3f) / 1.6f) * 0.42f;
            float inside = Mathf.Clamp01(0.5f - sd) * fill;
            float a = Mathf.Max(line, Mathf.Max(fringe, inside));
            if (a <= 0.002f) return Clear32;
            // white where the line or the fill dominates, near-black in the fringe (tinted by the sprite colour)
            float lum = Mathf.Max(line, inside) >= fringe ? 1f : Mathf.Lerp(0.08f, 1f, Mathf.Max(line, inside) / Mathf.Max(1e-4f, fringe));
            byte g = (byte)(lum * 255f);
            return new Color32(g, g, g, (byte)(a * 255f + 0.5f));
        }

        /// <summary>
        /// Outlined ring for a radius (metres), the line ≈ lineMetres wide at that radius, fringed on both sides.
        /// 1 sprite unit = the ring's diameter (draw it with PlaceFlatScaled, scale = 2 × radius).
        /// </summary>
        static Sprite PreviewRing(float radius, float lineMetres)
        {
            radius = Mathf.Max(0.05f, radius);
            int size = radius > 10f ? 1024 : radius > 3f ? 512 : 256;
            float px = Mathf.Clamp(lineMetres * (size * 0.5f - 6f) / radius, 2f, size / 8f);
            int q = Mathf.RoundToInt(px * 2f);
            int key = 10000000 + size * 1000 + q;   // q < 1000; cones 3e6, rect 4e6, dot 5e6
            if (previewArt.TryGetValue(key, out var s) && s != null) return s;
            float half = q * 0.25f;
            float rc = size * 0.5f - (half + 4.5f);   // centre line; room for the band and the outer fringe
            var tex = Bake("lv_pv_ring", size, size, (x, y) =>
            {
                float dx = x - size * 0.5f, dy = y - size * 0.5f;
                float sd = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - rc) - (half + 0.5f);
                return Shade(sd, half, 0f);
            });
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), rc * 2f, 0, SpriteMeshType.FullRect);
            s.name = "lv_pv_ring";
            previewArt[key] = s;
            return s;
        }

        /// <summary>Filled sector pointing +x with a bright, fringed outline; 1 m = the diameter. Bucketed by 5°.</summary>
        static Sprite PreviewCone(float angleDeg)
        {
            int bucket = Mathf.Clamp(Mathf.RoundToInt(angleDeg / 5f) * 5, 5, 360);
            int key = 3000000 + bucket;
            if (previewArt.TryGetValue(key, out var s) && s != null) return s;
            const int size = 256;
            const float R = size * 0.5f - 4f;
            float half = bucket * 0.5f * Mathf.Deg2Rad;
            var tex = Bake("lv_pv_cone" + bucket, size, size, (x, y) =>
            {
                float dx = x - size * 0.5f, dy = y - size * 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float sd = d - R;
                if (bucket < 360)
                {
                    float over = Mathf.Abs(Mathf.Atan2(dy, dx)) - half;
                    float angular = over < Mathf.PI * 0.5f ? d * Mathf.Sin(over) : d;
                    sd = Mathf.Max(sd, angular);
                }
                return Shade(sd, 1.6f, 0.16f + 0.16f * Mathf.Clamp01(d / R));
            });
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), R * 2f, 0, SpriteMeshType.FullRect);
            s.name = "lv_pv_cone";
            previewArt[key] = s;
            return s;
        }

        /// <summary>Rounded rectangle (bright fringed outline, faint fill) for SpriteDrawMode.Sliced. Border ≈ 0.11 m.</summary>
        static Sprite PreviewRect
        {
            get
            {
                const int key = 4000000;
                if (previewArt.TryGetValue(key, out var s) && s != null) return s;
                const int n = 64;
                const float r = 13f, inset = 4f;
                var tex = Bake("lv_pv_rect", n, n, (x, y) =>
                {
                    // signed distance to a rounded rect inset from the texture edge
                    float hx = n * 0.5f - inset, hy = n * 0.5f - inset;
                    float qx = Mathf.Abs(x - n * 0.5f) - (hx - r), qy = Mathf.Abs(y - n * 0.5f) - (hy - r);
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    float sd = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    return Shade(sd, 1.4f, 0.22f);
                });
                s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n / 0.5f, 0, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18));
                s.name = "lv_pv_rect";
                previewArt[key] = s;
                return s;
            }
        }

        /// <summary>Path dot: bright core, soft halo, dark rim. 1 m diameter.</summary>
        static Sprite PreviewDot
        {
            get
            {
                const int key = 5000000;
                if (previewArt.TryGetValue(key, out var s) && s != null) return s;
                const int n = 64;
                var tex = Bake("lv_pv_dot", n, n, (x, y) =>
                {
                    float dx = x - n * 0.5f, dy = y - n * 0.5f;
                    float sd = Mathf.Sqrt(dx * dx + dy * dy) - n * 0.34f;
                    return Shade(sd, 6f, 1f);
                });
                s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n, 0, SpriteMeshType.FullRect);
                s.name = "lv_pv_dot";
                previewArt[key] = s;
                return s;
            }
        }

        // ------------------------------------------------------------------ shapes

        /// <summary>Filled circle with a crisp outline (AoE radius, metres).</summary>
        public static void ShowCircle(string id, Vector2 center, float radius, Color color)
        {
            var s = Get();
            var p = s.GetPreview(id, PreviewKind.Circle);
            p.color = color;
            s.SetPartCount(p, 2);
            radius = Mathf.Max(0.05f, radius);
            s.PlaceFlat(p.parts[0], PresentationArt.Disc, LayerFill, center, radius * 2f, radius * 2f, 0f, A(color, 0.16f * color.a));
            s.PlaceFlatScaled(p.parts[1], PreviewRing(radius, 0.08f), LayerLine, center, radius * 2f, 0f, A(color, 0.95f * color.a));
        }

        /// <summary>Cone from origin towards dir with the given radius and full angle (degrees).</summary>
        public static void ShowCone(string id, Vector2 origin, Vector2 dir, float radius, float angleDeg, Color color)
        {
            var s = Get();
            var p = s.GetPreview(id, PreviewKind.Cone);
            p.color = color;
            s.SetPartCount(p, 1);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            radius = Mathf.Max(0.05f, radius);
            s.PlaceFlatScaled(p.parts[0], PreviewCone(angleDeg), LayerLine, origin, radius * 2f, ang, A(color, color.a));
        }

        /// <summary>Rectangle from → to with the given width (line AoEs, charge lanes).</summary>
        public static void ShowLine(string id, Vector2 from, Vector2 to, float width, Color color)
        {
            var s = Get();
            var p = s.GetPreview(id, PreviewKind.Line);
            p.color = color;
            s.SetPartCount(p, 1);
            var d = to - from;
            float len = Mathf.Max(0.05f, d.magnitude);
            var f = p.parts[0];
            var mid = (from + to) * 0.5f;
            s.PlaceFlat(f, PreviewRect, LayerLine, mid, 1f, 1f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, A(color, color.a));
            // the outline sits RectInset inside the sprite rect: grow it so the line is on the true edge
            const float minSide = 0.3f, RectInset = 4f / 128f;
            f.sr.drawMode = SpriteDrawMode.Sliced;
            f.sr.size = new Vector2(Mathf.Max(minSide, len) + RectInset * 2f, Mathf.Max(minSide, width) + RectInset * 2f);
            f.t.localScale = Vector3.one;
        }

        /// <summary>Movement path: marching dots (dashed) or a soft solid line, with an end marker.</summary>
        public static void ShowPath(string id, IList<Vector2> points, Color color, bool dashed = true)
        {
            var s = Get();
            var p = s.GetPreview(id, PreviewKind.Path);
            p.pts.Clear();
            if (points != null) for (int i = 0; i < points.Count; i++) p.pts.Add(points[i]);
            s.BuildPath(p, color, dashed);
        }

        /// <summary>ShowPath for rules-engine paths (NavPath.Points).</summary>
        public static void ShowPath(string id, IReadOnlyList<Vec2> points, Color color, bool dashed = true)
        {
            var s = Get();
            var p = s.GetPreview(id, PreviewKind.Path);
            p.pts.Clear();
            if (points != null) for (int i = 0; i < points.Count; i++) p.pts.Add(new Vector2(points[i].x, points[i].y));
            s.BuildPath(p, color, dashed);
        }

        const float DotSpacing = 0.34f;
        const float DotSize = 0.24f;
        const int MaxPathParts = 220;

        void BuildPath(Preview p, Color color, bool dashed)
        {
            p.color = color;
            p.dashed = dashed;
            p.cum.Clear();
            p.total = 0f;
            for (int i = 0; i < p.pts.Count; i++)
            {
                if (i > 0) p.total += Vector2.Distance(p.pts[i - 1], p.pts[i]);
                p.cum.Add(p.total);
            }
            if (p.pts.Count < 2) { SetPartCount(p, 0); return; }
            int body = dashed ? Mathf.Min(MaxPathParts, Mathf.FloorToInt(p.total / DotSpacing) + 1) : Mathf.Min(MaxPathParts, p.pts.Count - 1);
            SetPartCount(p, 2 + body);
            var end = p.pts[p.pts.Count - 1];
            PlaceFlatScaled(p.parts[0], PreviewRing(0.31f, 0.06f), LayerMarker, end, 0.62f, 0f, A(color, 0.95f * color.a));
            PlaceFlat(p.parts[1], PreviewDot, LayerMarker, end, 0.34f, 0.34f, 0f, A(color, 0.8f * color.a));
            if (!dashed)
            {
                var bl = PresentationArt.BeamLine;
                for (int i = 0; i < body; i++)
                {
                    var a = p.pts[i]; var b = p.pts[i + 1];
                    var d = b - a;
                    PlaceFlat(p.parts[2 + i], bl, LayerLine, a, Mathf.Max(0.01f, d.magnitude), 0.18f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, A(color, 0.9f * color.a));
                }
            }
            else LayoutDots(p, 0f);
        }

        void LayoutDots(Preview p, float offset)
        {
            int body = p.parts.Count - 2;
            var dot = PreviewDot;
            int seg = 0;
            for (int i = 0; i < body; i++)
            {
                var f = p.parts[2 + i];
                float d = i * DotSpacing + offset;
                if (d > p.total - 0.25f) { f.sr.enabled = false; continue; }
                f.sr.enabled = true;
                while (seg < p.cum.Count - 2 && p.cum[seg + 1] < d) seg++;
                float segLen = p.cum[seg + 1] - p.cum[seg];
                float t = segLen > 1e-5f ? (d - p.cum[seg]) / segLen : 0f;
                var pos = Vector2.Lerp(p.pts[seg], p.pts[seg + 1], t);
                float fadeIn = Mathf.Clamp01(d / 0.5f);
                PlaceFlat(f, dot, LayerLine, pos, DotSize, DotSize, 0f, A(p.color, 0.95f * p.color.a * (0.4f + 0.6f * fadeIn)));
            }
        }

        // ------------------------------------------------------------------ movement range

        /// <summary>
        /// Soft-edged movement area: canReach is sampled at every cell centre of area (cellSize metres), baked into a
        /// small mask texture (smooth bilinear contour, bright outline with a dark fringe) and laid on the ground as one
        /// sprite. Re-bake only when the reachable set changes.
        /// </summary>
        public static void ShowMoveRange(string id, Func<Vector2, bool> canReach, Rect area, float cellSize, Color color)
        {
            if (canReach == null) return;
            var s = Get();
            var p = s.GetPreview(id, PreviewKind.MoveRange);
            s.BakeMoveRange(p, canReach, area, cellSize, color);
        }

        /// <summary>ShowMoveRange straight from a NavGrid.ReachableWithin result.</summary>
        public static void ShowMoveRange(string id, Lanternvale.World.ReachMap reach, Color color)
        {
            if (reach == null) return;
            float cs = Mathf.Max(0.1f, reach.CellSize); // the cell size BakeMoveRange samples with
            float m = reach.MaxMetres + cs * 2f;
            // Snap to the NavGrid lattice (cells span [k·cs, (k+1)·cs) from the origin): every sample is then a
            // cell centre and the baked texels line up with the cells, so the contour sits on the cell edges
            // instead of drifting by up to half a cell with the unit's exact position.
            float x0 = Mathf.Floor((reach.Start.x - m) / cs) * cs, y0 = Mathf.Floor((reach.Start.y - m) / cs) * cs;
            float x1 = Mathf.Ceil((reach.Start.x + m) / cs) * cs, y1 = Mathf.Ceil((reach.Start.y + m) / cs) * cs;
            var area = Rect.MinMaxRect(x0, y0, x1, y1);
            ShowMoveRange(id, v => reach.CanReach(new Vec2(v.x, v.y)), area, cs, color);
        }

        void BakeMoveRange(Preview p, Func<Vector2, bool> canReach, Rect area, float cellSize, Color color)
        {
            p.color = color;
            float cell = Mathf.Max(0.1f, cellSize);
            int nx = Mathf.Clamp(Mathf.CeilToInt(area.width / cell), 1, 600);
            int ny = Mathf.Clamp(Mathf.CeilToInt(area.height / cell), 1, 600);
            int NX = nx + 2, NY = ny + 2; // one empty cell of padding so the outline closes at the area edge
            int n = NX * NY;
            if (p.mask == null || p.mask.Length < n) { p.mask = new float[n]; p.mask2 = new float[n]; }
            Array.Clear(p.mask, 0, n);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    var c = new Vector2(area.xMin + (i + 0.5f) * cell, area.yMin + (j + 0.5f) * cell);
                    bool ok;
                    try { ok = canReach(c); } catch (Exception) { ok = false; }
                    p.mask[(j + 1) * NX + i + 1] = ok ? 1f : 0f;
                }
            // [1 2 1] blur (rounds the staircase into smooth contours)
            var m = p.mask; var m2 = p.mask2;
            for (int j = 0; j < NY; j++)
                for (int i = 0; i < NX; i++)
                {
                    float l = i > 0 ? m[j * NX + i - 1] : 0f, r = i < NX - 1 ? m[j * NX + i + 1] : 0f;
                    m2[j * NX + i] = (l + 2f * m[j * NX + i] + r) * 0.25f;
                }
            for (int j = 0; j < NY; j++)
                for (int i = 0; i < NX; i++)
                {
                    float d = j > 0 ? m2[(j - 1) * NX + i] : 0f, u = j < NY - 1 ? m2[(j + 1) * NX + i] : 0f;
                    m[j * NX + i] = (d + 2f * m2[j * NX + i] + u) * 0.25f;
                }

            int U = Mathf.Clamp(1024 / Mathf.Max(NX, NY), 1, 4);
            int W = NX * U, H = NY * U;
            if (p.tex == null || p.tex.width != W || p.tex.height != H)
            {
                if (p.tex != null) Destroy(p.tex);
                p.tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "lv_moverange", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                p.buf = new Color32[W * H];
            }
            var buf = p.buf;
            float inv = 1f / U;
            for (int ty = 0; ty < H; ty++)
            {
                float gy = (ty + 0.5f) * inv - 0.5f;
                int y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, NY - 1), y1 = Mathf.Min(NY - 1, y0 + 1);
                float fy = Mathf.Clamp01(gy - y0);
                for (int tx = 0; tx < W; tx++)
                {
                    float gx = (tx + 0.5f) * inv - 0.5f;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, NX - 1), x1 = Mathf.Min(NX - 1, x0 + 1);
                    float fx = Mathf.Clamp01(gx - x0);
                    float a0 = m[y0 * NX + x0] + (m[y0 * NX + x1] - m[y0 * NX + x0]) * fx;
                    float a1 = m[y1 * NX + x0] + (m[y1 * NX + x1] - m[y1 * NX + x0]) * fx;
                    float v = a0 + (a1 - a0) * fy;
                    // soft fill inside, a bright line on the contour (v = 0.5), a thin dark fringe just outside it
                    float fill = v <= 0.45f ? 0f : v >= 0.6f ? 1f : (v - 0.45f) / 0.15f;
                    fill = fill * fill * (3f - 2f * fill) * 0.2f;
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) / 0.09f) * 0.95f;
                    float fringe = Mathf.Clamp01(1f - Mathf.Abs(v - 0.36f) / 0.07f) * 0.4f;
                    float a = Mathf.Max(fill, Mathf.Max(edge, fringe));
                    byte g = (byte)(edge >= fringe ? (edge > fill ? 255 : 226) : (fill > fringe ? 226 : 24));
                    buf[ty * W + tx] = new Color32(g, g, g, (byte)(a * 255f));
                }
            }
            p.tex.SetPixels32(buf);
            p.tex.Apply(false, false);
            if (p.sprite != null) Destroy(p.sprite);
            // pivot at the bottom-left of the padded area: one cell below/left of area.min
            p.sprite = Sprite.Create(p.tex, new Rect(0, 0, W, H), Vector2.zero, U / cell, 0, SpriteMeshType.FullRect);
            p.sprite.name = "lv_moverange";

            SetPartCount(p, 1);
            var f = p.parts[0];
            PlaceFlat(f, p.sprite, LayerRange, new Vector2(area.xMin - cell, area.yMin - cell), 1f, 1f, 0f, A(color, color.a));
            f.t.localScale = Vector3.one;   // the sprite is already in metres (U texels per cell)
        }

        // ------------------------------------------------------------------ hide / update

        /// <summary>Removes one preview.</summary>
        public static void Hide(string id)
        {
            var s = Instance;
            if (s == null || id == null || !s.previews.TryGetValue(id, out var p)) return;
            s.DestroyPreview(p);
            s.previews.Remove(id);
            s.previewList.Remove(p);
        }

        /// <summary>Removes every preview.</summary>
        public static void HideAll()
        {
            var s = Instance;
            if (s == null) return;
            for (int i = 0; i < s.previewList.Count; i++) s.DestroyPreview(s.previewList[i]);
            s.previews.Clear();
            s.previewList.Clear();
        }

        /// <summary>True if a preview with this id is showing.</summary>
        public static bool IsShowing(string id) => Instance != null && id != null && Instance.previews.ContainsKey(id);

        void DestroyPreview(Preview p)
        {
            SetPartCount(p, 0);
            if (p.sprite != null) { Destroy(p.sprite); p.sprite = null; }
            if (p.tex != null) { Destroy(p.tex); p.tex = null; }
        }

        void UpdatePreviews(float dt)
        {
            for (int i = 0; i < previewList.Count; i++)
            {
                var p = previewList[i];
                p.age += dt;
                switch (p.kind)
                {
                    case PreviewKind.Path:
                        if (p.dashed && p.parts.Count > 2) LayoutDots(p, Mathf.Repeat(p.age * 0.9f, DotSpacing));
                        break;
                    case PreviewKind.Circle:
                        if (p.parts.Count > 1) p.parts[1].sr.color = A(p.color, (0.8f + 0.2f * Mathf.Sin(p.age * 4f)) * p.color.a);
                        break;
                    case PreviewKind.Cone:
                    case PreviewKind.Line:
                        if (p.parts.Count > 0) p.parts[0].sr.color = A(p.color, (0.86f + 0.14f * Mathf.Sin(p.age * 4f)) * p.color.a);
                        break;
                }
            }
        }
    }
}
