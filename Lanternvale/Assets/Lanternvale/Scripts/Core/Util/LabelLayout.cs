// Greedy label placement (Map panel names, Docs/UI_Panels.md "Map"): labels are placed one by one in priority order
// at the first candidate position that stays inside the bounds and overlaps neither an obstacle (dots, glyphs) nor
// a label placed before. Candidates go round the anchor (below, above, right, left, then the diagonals) on growing
// rings; a label placed beyond the first ring gets a leader line back to its anchor. A label that finds no place is
// left out (the caller shows it on hover). Pure C# (screen units, y down), no Unity types.
using System;
using System.Collections.Generic;

namespace Lanternvale.Util
{
    /// <summary>Axis-aligned rect in screen units (y down).</summary>
    public struct LabelRect
    {
        public float X, Y, W, H;

        public LabelRect(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }

        public float XMax => X + W;
        public float YMax => Y + H;
        public float CenterX => X + W * 0.5f;
        public float CenterY => Y + H * 0.5f;

        public static LabelRect Around(float cx, float cy, float w, float h) => new LabelRect(cx - w * 0.5f, cy - h * 0.5f, w, h);

        /// <summary>The rects share interior area once this one is grown by pad on every side.</summary>
        public bool Overlaps(LabelRect o, float pad = 0f) =>
            X - pad < o.XMax && o.X < XMax + pad && Y - pad < o.YMax && o.Y < YMax + pad;

        public bool Contains(LabelRect inner) => inner.X >= X - 1e-3f && inner.Y >= Y - 1e-3f && inner.XMax <= XMax + 1e-3f && inner.YMax <= YMax + 1e-3f;

        public bool Contains(float x, float y) => x >= X && x <= XMax && y >= Y && y <= YMax;

        /// <summary>The point of the rect closest to (x, y) (leader line end).</summary>
        public void Closest(float x, float y, out float cx, out float cy)
        {
            cx = Math.Max(X, Math.Min(XMax, x));
            cy = Math.Max(Y, Math.Min(YMax, y));
        }

        public override string ToString() => $"({X:0.#},{Y:0.#} {W:0.#}x{H:0.#})";
    }

    public sealed class LabelLayout
    {
        public sealed class Item
        {
            public string Id = "";
            public float AnchorX, AnchorY;
            public float Width, Height;
            /// <summary>Lower places first; ties keep insertion order.</summary>
            public int Priority;
            /// <summary>Distance from the anchor to the near edge of the label on the first ring.</summary>
            public float Gap = 8f;
            /// <summary>Search further rings (a label that must be shown, e.g. a quest NPC).</summary>
            public bool Important;
            /// <summary>Obstacle index this label may cover (its own dot), or -1.</summary>
            public int OwnObstacle = -1;

            // ---- results
            public bool Placed;
            public LabelRect Rect;
            /// <summary>Placed away from its anchor: draw a thin line from the anchor to the label.</summary>
            public bool Leader;
            /// <summary>Ring (0 = touching the anchor) and direction (0 below, 1 above, 2 right, 3 left, 4–7 diagonals) used.</summary>
            public int Ring, Direction;
        }

        /// <summary>Labels must stay inside these bounds.</summary>
        public LabelRect Bounds;
        /// <summary>Areas no label may cover (dots, glyphs, exit rects).</summary>
        public readonly List<LabelRect> Obstacles = new List<LabelRect>();
        /// <summary>Clearance kept between a label and anything else.</summary>
        public float Padding = 2f;
        /// <summary>Extra distance of each ring beyond the first (screen units).</summary>
        public float[] Rings = { 0f, 14f, 30f, 50f };
        /// <summary>More rings for Important labels.</summary>
        public float[] FarRings = { 76f, 110f, 150f, 200f, 260f };

        readonly List<Item> placed = new List<Item>();

        public IReadOnlyList<Item> Placed => placed;

        public LabelLayout(LabelRect bounds) { Bounds = bounds; }

        /// <summary>Places every item (Priority order, stable). Returns how many were placed.</summary>
        public int PlaceAll(IList<Item> items)
        {
            var order = new List<int>(items.Count);
            for (int i = 0; i < items.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = items[a].Priority.CompareTo(items[b].Priority);
                return c != 0 ? c : a.CompareTo(b);
            });
            int n = 0;
            foreach (int i in order) if (TryPlace(items[i])) n++;
            return n;
        }

        /// <summary>Places one item at its first free candidate. False (Placed = false) when none fits.</summary>
        public bool TryPlace(Item it)
        {
            it.Placed = false;
            it.Leader = false;
            if (it.Width <= 0f || it.Height <= 0f) return false;
            int rings = Rings.Length + (it.Important ? FarRings.Length : 0);
            for (int ring = 0; ring < rings; ring++)
            {
                float extra = ring < Rings.Length ? Rings[ring] : FarRings[ring - Rings.Length];
                for (int dir = 0; dir < 8; dir++)
                {
                    // far rings also try the four directions slid half a label sideways
                    int slides = ring >= 2 ? 3 : 1;
                    for (int sl = 0; sl < slides; sl++)
                    {
                        var r = Candidate(it, dir, it.Gap + extra, sl == 0 ? 0f : sl == 1 ? -1f : 1f);
                        if (!Free(r, it)) continue;
                        it.Placed = true;
                        it.Rect = r;
                        it.Ring = ring;
                        it.Direction = dir;
                        it.Leader = ring > 0;
                        placed.Add(it);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Takes a placed item out again (its space becomes free).</summary>
        public void Remove(Item it)
        {
            if (it == null) return;
            placed.Remove(it);
            it.Placed = false;
        }

        /// <summary>
        /// Candidate rect for a direction at a distance from the anchor: 0 below, 1 above, 2 right, 3 left, 4 below-right,
        /// 5 below-left, 6 above-right, 7 above-left. slide (−1, 0, 1) shifts it half a label along the side.
        /// </summary>
        public static LabelRect Candidate(Item it, int dir, float d, float slide = 0f)
        {
            float ax = it.AnchorX, ay = it.AnchorY, w = it.Width, h = it.Height;
            float dd = d * 0.7f;
            switch (dir)
            {
                case 0: return new LabelRect(ax - w * 0.5f + slide * w * 0.5f, ay + d, w, h);
                case 1: return new LabelRect(ax - w * 0.5f + slide * w * 0.5f, ay - d - h, w, h);
                case 2: return new LabelRect(ax + d, ay - h * 0.5f + slide * h, w, h);
                case 3: return new LabelRect(ax - d - w, ay - h * 0.5f + slide * h, w, h);
                case 4: return new LabelRect(ax + dd, ay + dd + slide * h * 0.5f, w, h);
                case 5: return new LabelRect(ax - dd - w, ay + dd + slide * h * 0.5f, w, h);
                case 6: return new LabelRect(ax + dd, ay - dd - h + slide * h * 0.5f, w, h);
                default: return new LabelRect(ax - dd - w, ay - dd - h + slide * h * 0.5f, w, h);
            }
        }

        bool Free(LabelRect r, Item self)
        {
            if (!Bounds.Contains(r)) return false;
            for (int i = 0; i < Obstacles.Count; i++)
                if (i != self.OwnObstacle && r.Overlaps(Obstacles[i], Padding)) return false;
            for (int i = 0; i < placed.Count; i++)
                if (r.Overlaps(placed[i].Rect, Padding)) return false;
            return true;
        }

        /// <summary>True when no two placed labels overlap (test helper).</summary>
        public static bool NoOverlaps(IList<Item> items, out string conflict)
        {
            conflict = "";
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].Placed) continue;
                for (int j = i + 1; j < items.Count; j++)
                {
                    if (!items[j].Placed || !items[i].Rect.Overlaps(items[j].Rect)) continue;
                    conflict = $"{items[i].Id} {items[i].Rect} overlaps {items[j].Id} {items[j].Rect}";
                    return false;
                }
            }
            return true;
        }
    }
}
