// Persistent targeting previews drawn on the ground (under units, above shadows/decals):
// circle, cone, line, path (solid or marching dashes) and the movement range (a small mask
// texture baked at nav-cell resolution, upsampled with smooth bilinear contours and a bright edge).
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
        /// <summary>Sorting order of ground previews (above decals/shadows, below y-sorted units).</summary>
        public const int PreviewOrder = SortingOrders.Shadow + 50;

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
            while (p.parts.Count < n) p.parts.Add(Acquire(PresentationArt.White, PreviewOrder));
            while (p.parts.Count > n)
            {
                Release(p.parts[p.parts.Count - 1]);
                p.parts.RemoveAt(p.parts.Count - 1);
            }
        }

        static void Place(Fx f, Sprite s, int order, Vector2 pos, float w, float h, float angle, Color c)
        {
            if (f.sr.sprite != s) f.sr.sprite = s;
            f.sr.sortingOrder = order;
            f.sr.drawMode = SpriteDrawMode.Simple;
            f.t.position = new Vector3(pos.x, pos.y, 0f);
            f.t.localRotation = Quaternion.Euler(0f, 0f, angle);
            Size(f, s.bounds.size, w, h);
            f.sr.color = c;
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
            Place(p.parts[0], PresentationArt.Disc, PreviewOrder, center, radius * 2f, radius * 2f, 0f, PresentationArt.WithAlpha(color, 0.16f * color.a));
            Place(p.parts[1], PresentationArt.RingFor(radius, 0.06f), PreviewOrder + 1, center, radius * 2f, radius * 2f, 0f, PresentationArt.WithAlpha(color, 0.9f * color.a));
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
            Place(p.parts[0], PresentationArt.Cone(angleDeg), PreviewOrder, origin, radius * 2f, radius * 2f, ang, PresentationArt.WithAlpha(color, color.a));
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
            var sprite = PresentationArt.SlicedRect;
            if (f.sr.sprite != sprite) f.sr.sprite = sprite;
            f.sr.sortingOrder = PreviewOrder;
            f.sr.drawMode = SpriteDrawMode.Sliced;
            float minSide = 0.26f;
            f.sr.size = new Vector2(Mathf.Max(minSide, len), Mathf.Max(minSide, width));
            var mid = (from + to) * 0.5f;
            f.t.position = new Vector3(mid.x, mid.y, 0f);
            f.t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            f.t.localScale = Vector3.one;
            f.sr.color = PresentationArt.WithAlpha(color, color.a);
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
            Place(p.parts[0], PresentationArt.Ring(5, 128), PreviewOrder + 3, end, 0.62f, 0.62f, 0f, PresentationArt.WithAlpha(color, 0.95f * color.a));
            Place(p.parts[1], PresentationArt.Disc, PreviewOrder + 2, end, 0.3f, 0.3f, 0f, PresentationArt.WithAlpha(color, 0.55f * color.a));
            if (!dashed)
            {
                var bl = PresentationArt.BeamLine;
                for (int i = 0; i < body; i++)
                {
                    var a = p.pts[i]; var b = p.pts[i + 1];
                    var d = b - a;
                    Place(p.parts[2 + i], bl, PreviewOrder + 1, a, Mathf.Max(0.01f, d.magnitude), 0.16f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, PresentationArt.WithAlpha(color, 0.9f * color.a));
                }
            }
            else LayoutDots(p, 0f);
        }

        void LayoutDots(Preview p, float offset)
        {
            int body = p.parts.Count - 2;
            var dot = PresentationArt.SoftDot;
            int seg = 0;
            for (int i = 0; i < body; i++)
            {
                float d = i * DotSpacing + offset;
                if (d > p.total - 0.2f) { p.parts[2 + i].sr.enabled = false; continue; }
                p.parts[2 + i].sr.enabled = true;
                while (seg < p.cum.Count - 2 && p.cum[seg + 1] < d) seg++;
                float segLen = p.cum[seg + 1] - p.cum[seg];
                float t = segLen > 1e-5f ? (d - p.cum[seg]) / segLen : 0f;
                var pos = Vector2.Lerp(p.pts[seg], p.pts[seg + 1], t);
                float fadeIn = Mathf.Clamp01(d / 0.5f);
                Place(p.parts[2 + i], dot, PreviewOrder + 1, pos, 0.2f, 0.2f, 0f, PresentationArt.WithAlpha(p.color, 0.95f * p.color.a * (0.4f + 0.6f * fadeIn)));
            }
        }

        // ------------------------------------------------------------------ movement range

        /// <summary>
        /// Soft-edged movement area: canReach is sampled at every cell centre of area (cellSize
        /// metres), baked into a small mask texture (smooth bilinear contour, brighter outline)
        /// and drawn as one sprite. Re-bake only when the reachable set changes.
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
                    float fill = v <= 0.42f ? 0f : v >= 0.58f ? 1f : (v - 0.42f) / 0.16f;
                    fill = fill * fill * (3f - 2f * fill) * 0.2f;
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) / 0.1f) * 0.95f;
                    byte g = (byte)(edge > fill ? 255 : 226);
                    buf[ty * W + tx] = new Color32(g, g, g, (byte)(Mathf.Max(fill, edge) * 255f));
                }
            }
            p.tex.SetPixels32(buf);
            p.tex.Apply(false, false);
            if (p.sprite != null) Destroy(p.sprite);
            p.sprite = Sprite.Create(p.tex, new Rect(0, 0, W, H), Vector2.zero, U / cell, 0, SpriteMeshType.FullRect);
            p.sprite.name = "lv_moverange";

            SetPartCount(p, 1);
            var f = p.parts[0];
            f.sr.sprite = p.sprite;
            f.sr.sortingOrder = PreviewOrder - 2;
            f.sr.drawMode = SpriteDrawMode.Simple;
            f.t.position = new Vector3(area.xMin - cell, area.yMin - cell, 0f);
            f.t.localRotation = Quaternion.identity;
            f.t.localScale = Vector3.one;
            f.sr.color = PresentationArt.WithAlpha(color, color.a);
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
                        if (p.parts.Count > 1) p.parts[1].sr.color = PresentationArt.WithAlpha(p.color, (0.78f + 0.2f * Mathf.Sin(p.age * 4f)) * p.color.a);
                        break;
                    case PreviewKind.Cone:
                    case PreviewKind.Line:
                        if (p.parts.Count > 0) p.parts[0].sr.color = PresentationArt.WithAlpha(p.color, (0.85f + 0.15f * Mathf.Sin(p.age * 4f)) * p.color.a);
                        break;
                }
            }
        }
    }
}
