// MapTerrain: MapDef.paths — painted trails in any direction. Each polyline is smoothed (Catmull-Rom through its
// points), sampled every DataPathStep metres along its length with a gentle wander and width variation, and rendered
// by the same ribbon as the chained path decals (MapTerrain.AddTrail: the art "straightened" along the trail). A trail
// that meets the map's edge runs on across the flat margin (further at an exit) and fades out there. On paved maps
// (the shrine) the paths are processional walkways instead (MapTerrain.AnalyseShrine).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    internal sealed partial class MapTerrain
    {
        const float DataPathStep = 0.25f;

        void AnalyseDataPaths()
        {
            if (def.paths == null) return;
            foreach (var pd in def.paths)
            {
                if (pd == null || pd.points == null || pd.points.Count < 2) continue;
                string art = string.IsNullOrEmpty(pd.art) ? "decal_path_dirt" : pd.art;
                var tex = ArtLibrary.Texture(art);
                float aspect = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 2f;
                var ch = new PathChain { art = art, tex = tex, profile = ProfileOf(tex), order = chains.Count, data = true };
                float meanHalf = 0f;
                for (int i = 0; i < PathProfile.Columns; i++) meanHalf += ch.profile.half[i];
                meanHalf /= PathProfile.Columns;
                float width = pd.width > 0.2f ? pd.width : 2.2f;
                // the art's painted trail is as wide as the path asks
                ch.texH = width * 0.5f / Mathf.Max(0.05f, meanHalf);
                ch.tile = ch.texH * aspect;

                var ctrl = new List<Vector2>(pd.points.Count + 2);
                foreach (var p in pd.points)
                {
                    var v = new Vector2(p.x, p.y);
                    if (ctrl.Count == 0 || (ctrl[ctrl.Count - 1] - v).sqrMagnitude > 0.0025f) ctrl.Add(v);
                }
                if (ctrl.Count < 2) continue;
                ExtendAtEdge(ctrl, true);
                ExtendAtEdge(ctrl, false);
                var dense = CatmullRom(ctrl, 0.2f);
                SampleDataPath(ch, dense, width);
                if (ch.pts.Count < 2) continue;
                BuildBlocks(ch);
                chains.Add(ch);
            }
        }

        /// <summary>A path ending within 3 m of the map's edge runs on across the margin (3.4 m at an exit, else 2 m).</summary>
        void ExtendAtEdge(List<Vector2> pts, bool start)
        {
            var e = start ? pts[0] : pts[pts.Count - 1];
            float dl = e.x, dr = W - e.x, df = e.y, db = D - e.y;
            float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(df, db));
            if (m > 3f) return;
            Vector2 outward = m == dl ? Vector2.left : m == dr ? Vector2.right : m == df ? Vector2.down : Vector2.up;
            bool exit = false;
            foreach (var t in def.transitions)
                if (t != null && !t.hidden && (new Vector2(t.pos.x, t.pos.y) - e).sqrMagnitude < 16f) exit = true;
            var end = e + outward * (m + (exit ? 3.4f : 2f));
            if (start) pts.Insert(0, end); else pts.Add(end);
        }

        /// <summary>A Catmull-Rom spline through the points (ends mirrored), sampled about every step metres.</summary>
        static List<Vector2> CatmullRom(List<Vector2> p, float step)
        {
            var o = new List<Vector2>();
            int n = p.Count;
            for (int i = 0; i < n - 1; i++)
            {
                var p1 = p[i];
                var p2 = p[i + 1];
                var p0 = i > 0 ? p[i - 1] : p1 * 2f - p2;
                var p3 = i + 2 < n ? p[i + 2] : p2 * 2f - p1;
                int k = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / step));
                for (int j = 0; j < k; j++)
                {
                    float t = (float)j / k, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(p[n - 1]);
            return o;
        }

        /// <summary>The trail's samples along a dense centreline: a little wander, width variation, ends known.</summary>
        void SampleDataPath(PathChain ch, List<Vector2> dense, float width)
        {
            int n = dense.Count;
            var along = new float[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + Vector2.Distance(dense[i - 1], dense[i]);
            float L = along[n - 1];
            if (L < 0.5f) return;
            float phase = s2 + ch.order * 17.3f;
            ch.x0 = ch.yMin = float.MaxValue;
            ch.x1 = ch.yMax = float.MinValue;
            int steps = Mathf.Max(1, Mathf.CeilToInt(L / DataPathStep));
            int seg = 0;
            for (int k = 0; k <= steps; k++)
            {
                float s = Mathf.Min(L, k * DataPathStep);
                while (seg < n - 2 && along[seg + 1] < s) seg++;
                float sl = Mathf.Max(1e-5f, along[seg + 1] - along[seg]);
                var p = Vector2.Lerp(dense[seg], dense[seg + 1], (s - along[seg]) / sl);
                var tan = dense[seg + 1] - dense[seg];
                tan = tan.sqrMagnitude > 1e-8f ? tan.normalized : Vector2.right;
                var nrm = new Vector2(-tan.y, tan.x);
                // let the trail wander a little (less near its ends, which the data placed)
                float endDist = Mathf.Min(s, L - s);
                p += nrm * ((Mathf.PerlinNoise(s * 0.11f + phase, 0.37f) - 0.5f) * 0.5f * Smooth(0f, 3f, endDist));
                ch.pts.Add(p);
                ch.half.Add(width * 0.5f * (0.9f + 0.22f * Mathf.PerlinNoise(s * 0.17f + phase, 7.7f)));
                ch.endD.Add(endDist);
                ch.x0 = Mathf.Min(ch.x0, p.x);
                ch.x1 = Mathf.Max(ch.x1, p.x);
                ch.yMin = Mathf.Min(ch.yMin, p.y);
                ch.yMax = Mathf.Max(ch.yMax, p.y);
            }
        }
    }
}
