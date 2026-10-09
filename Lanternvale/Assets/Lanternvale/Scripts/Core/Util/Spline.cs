// Curves shared by the world view and the rules: a pond's shore is drawn (MapTerrain.Water) and blocked (NavGrid) along the
// same closed Catmull-Rom loop through the data polygon's corners, so nobody stands inside the visible water rim.
// Pure C#, no Unity types.
using System;
using System.Collections.Generic;

namespace Lanternvale.Util
{
    public static class Spline
    {
        /// <summary>A closed uniform Catmull-Rom loop through a polygon's corners, a point about every <paramref name="step"/>
        /// metres (a pond's soft shore). It bulges a little outside the polygon on convex corners.</summary>
        public static List<Vec2> ClosedCatmullRom(IList<Vec2> p, float step)
        {
            var o = new List<Vec2>();
            int n = p?.Count ?? 0;
            if (n < 3) { if (p != null) o.AddRange(p); return o; }
            step = Math.Max(0.01f, step);
            for (int i = 0; i < n; i++)
            {
                Vec2 p0 = p[(i + n - 1) % n], p1 = p[i], p2 = p[(i + 1) % n], p3 = p[(i + 2) % n];
                float dx = p2.x - p1.x, dy = p2.y - p1.y;
                int k = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / step));
                for (int j = 0; j < k; j++)
                {
                    float t = (float)j / k, t2 = t * t, t3 = t2 * t;
                    o.Add(new Vec2(
                        0.5f * (2f * p1.x + (p2.x - p0.x) * t + (2f * p0.x - 5f * p1.x + 4f * p2.x - p3.x) * t2 + (3f * p1.x - p0.x - 3f * p2.x + p3.x) * t3),
                        0.5f * (2f * p1.y + (p2.y - p0.y) * t + (2f * p0.y - 5f * p1.y + 4f * p2.y - p3.y) * t2 + (3f * p1.y - p0.y - 3f * p2.y + p3.y) * t3)));
                }
            }
            return o;
        }

        /// <summary>The shore of a closed water polygon as drawn: <see cref="ClosedCatmullRom"/> every 0.5 m.</summary>
        public static List<Vec2> PondShore(IList<Vec2> corners) => ClosedCatmullRom(corners, PondShoreStep);

        /// <summary>Sampling step (m) of <see cref="PondShore"/>.</summary>
        public const float PondShoreStep = 0.5f;
    }
}
