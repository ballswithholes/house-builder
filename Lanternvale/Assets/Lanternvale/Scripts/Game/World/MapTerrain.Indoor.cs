// MapTerrain: indoor maps (caves, the ice cave, crypts, the Hollow Heart) — a cutaway diorama. The floor (the walkable
// rect) is flat; behind it a steep wall climbs 9-13 m within a few metres and keeps rising into the dark; at the sides
// the rock climbs more gently (about 50°, so the camera at yaw ±45° looks over it onto the floor) and steepens higher
// up; in front, past a low rubble lip, the ground falls away into a dark pit, so nothing ever stands between the camera
// and the floor. Exits cut passages through the walls that slope down into darkness. Crypts stand dressed masonry in
// front of the rock (a tall back wall with pilasters, alcoves and a cornice, low walls at the sides); caves get boulders,
// ledges and stalagmites, the ice cave crystals and snow, the Hollow Heart great roots and glowing fungus.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    internal sealed partial class MapTerrain
    {
        bool Masonry => style.Walls == BiomeWalls.Masonry;
        /// <summary>Where the rock starts behind / beside the floor (m outside the rect; masonry stands in front of it).</summary>
        float BackStart => Masonry ? 1.7f : 0.5f;
        float SideStart => Masonry ? 1.5f : 0.6f;
        const float MasonryBackH = 7.6f, MasonrySideH = 2.4f;

        /// <summary>Indoor land height (m): 0 on the floor, the walls around, the pit in front.</summary>
        float IndoorHeight(float x, float y)
        {
            float ox = Mathf.Max(-x, x - W), ob = y - D, of = -y;
            if (ox <= 0f && ob <= 0f && of <= 0f) return 0f;
            float nA = Mathf.PerlinNoise(x * 0.09f + s1, y * 0.09f + s2);
            float nB = Mathf.PerlinNoise(x * 0.23f + s3, y * 0.23f + s4);
            float nC = Mathf.PerlinNoise(x * 0.035f + s5, y * 0.035f + s6);
            float rough = (nA - 0.5f) * 1.8f + (nB - 0.5f) * 0.8f;

            float back = 0f;
            // the wall's foot wanders in and out (bays and buttresses), never onto the floor
            float bay = Masonry ? 0f : (Mathf.PerlinNoise(x * 0.13f + s4, 3.7f) * 0.7f + Mathf.PerlinNoise(x * 0.37f + s2, 8.1f) * 0.3f) * 1.6f;
            float tb = ob - BackStart - bay;
            if (tb > 0f)
            {
                if (Masonry) back = 9.5f * Smooth(0f, 1.2f, tb) + 0.5f * Mathf.Max(0f, tb - 3.4f);
                else
                {
                    // two tiers: a cliff to a broad ledge (rubble and stalagmites lie up there, lit from above), a second
                    // cliff above it, then the rock keeps climbing into the dark
                    float ledge = 4.6f + (nC - 0.5f) * 2.2f, top = 10.5f + (nC - 0.5f) * 5f;
                    float l0 = 1.4f + nB * 0.6f, l1 = l0 + 1.1f + nA * 0.9f;
                    back = ledge * Smooth(0f, l0, tb) + (top - ledge) * Smooth(l1, l1 + 1.7f, tb) + 0.5f * Mathf.Max(0f, tb - l1 - 1.7f);
                }
                back += rough * Smooth(0.2f, 2f, tb) * (Masonry ? 0.5f : 1.3f);
                for (int i = 0; i < backExits.Count; i++) back = Passage(back, Mathf.Abs(x - backExits[i]), tb);
            }
            float side = 0f;
            float sbay = Masonry ? 0f : (Mathf.PerlinNoise(y * 0.13f + s5, x < 0f ? 1.3f : 6.6f) * 0.7f + Mathf.PerlinNoise(y * 0.37f + s1, x < 0f ? 4.4f : 9.9f) * 0.3f) * 1.4f;
            float ts = ox - SideStart - sbay;
            if (ts > 0f)
            {
                // about 50° low down (the camera at yaw ±45° sees over it), steeper higher up
                side = 1.25f * ts + 0.6f * Mathf.Pow(Mathf.Max(0f, ts - 4.5f), 1.3f);
                side += rough * Smooth(0.2f, 2f, ts);
                for (int i = 0; i < corridors.Count; i++)
                {
                    var c = corridors[i];
                    if ((c.x < 0f && x < 0f) || (c.x > 0f && x > W)) side = Passage(side, Mathf.Abs(y - c.y), ts);
                }
                // the sides end at the front cut
                side *= Smooth(-6.5f, -0.6f, y);
            }
            float wall = Mathf.Max(back, side);
            // rock (not masonry) breaks into ledges: strata stepping up the face
            if (!Masonry && wall > 0.6f)
            {
                const float course = 1.7f;
                float hs = wall / course + (nB - 0.5f) * 0.5f;
                float f = hs - Mathf.Floor(hs);
                float stepped = (Mathf.Floor(hs) + Smooth(0.3f, 0.75f, f)) * course - (nB - 0.5f) * 0.5f * course;
                wall = Mathf.Lerp(wall, stepped, 0.75f * Smooth(0.6f, 2f, wall));
            }
            float h = wall;
            if (of > 0f)
            {
                // a low rubble lip, then the drop into darkness
                float lip = 0.28f * Smooth(0.1f, 0.7f, of) * (1f - Smooth(0.9f, 1.6f, of)) * (0.6f + 0.8f * nB);
                float pit = -9f * Smooth(1.1f, 5.5f, of) * (0.8f + 0.4f * nA);
                for (int i = 0; i < frontExits.Count; i++)
                {
                    // a ramp across the pit at a front exit
                    float pf = 1f - Smooth(1.8f, 4.2f, Mathf.Abs(x - frontExits[i]));
                    pit = Mathf.Lerp(pit, -0.35f * of, pf);
                }
                h += (lip + pit) * (1f - Smooth(0.5f, 4f, wall));
            }
            return h;
        }

        float wallNorm = -1f;

        /// <summary>The factor that fits the wall palette's brightest tone (a lit band on a pale ledge, snow) under a tint of 1.</summary>
        float WallNorm(Color avg)
        {
            if (wallNorm > 0f) return wallNorm;
            float peak = 0f;
            var lo = Paint.Shade(style.WallLow, 1.22f * 1.1f);
            peak = Mathf.Max(peak, Mathf.Max(lo.r / Mathf.Max(0.05f, avg.r), Mathf.Max(lo.g / Mathf.Max(0.05f, avg.g), lo.b / Mathf.Max(0.05f, avg.b))));
            if (style.Walls == BiomeWalls.Ice) peak = Mathf.Max(peak, 0.98f / Mathf.Max(0.05f, Mathf.Min(avg.r, Mathf.Min(avg.g, avg.b))));
            return wallNorm = 1f / Mathf.Max(1f, peak);
        }

        /// <summary>A wall cut by an exit's passage (dist: from the passage's centre line; t: metres into the wall).</summary>
        static float Passage(float wall, float dist, float t)
        {
            float pf = 1f - Smooth(1.8f, 4.4f, dist);
            if (pf <= 0f) return wall;
            // the passage's floor slopes down into the dark beyond the wall's foot
            float floor = -0.22f * Mathf.Max(0f, t - 1.5f);
            return Mathf.Lerp(wall, floor, pf);
        }

        /// <summary>Indoor colours: the walls' rock (strata, streaks, darker high up), the pit's darkness.</summary>
        Color IndoorTint(float x, float y, float h, float steep, Color c, ref float strength, ref float detail)
        {
            if (h > 0.05f)
            {
                float k = Smooth(0.05f, 1.1f, h);
                // bedding planes: crisp pale and dark bands across the face (they read even in shade), gently warped
                float bandH = h * 1.35f + Mathf.PerlinNoise(x * 0.06f + s2, y * 0.06f + s5) * 3f;
                float band = Smooth(0.25f, 0.45f, Mathf.Repeat(bandH, 1f)) * (1f - Smooth(0.8f, 0.95f, Mathf.Repeat(bandH, 1f)));
                float strata = 0.84f + 0.26f * band;
                float streak = 0.92f + 0.12f * Mathf.PerlinNoise(x * 0.45f + s6, h * 0.12f + s1);
                var wc = Color.Lerp(style.WallLow, style.WallHigh, Smooth(0.5f, 13f, h));
                wc = new Color(wc.r * strata * streak, wc.g * strata * streak, wc.b * strata * streak);
                // ledges and shelves (locally flat): dusty and pale, so the tiers read from the camera
                float lx = IndoorHeight(x + 0.35f, y) - IndoorHeight(x - 0.35f, y), ly = IndoorHeight(x, y + 0.35f) - IndoorHeight(x, y - 0.35f);
                float flat = 1f - Smooth(0.35f, 1.1f, Mathf.Sqrt(lx * lx + ly * ly) / 0.7f);
                wc = Color.Lerp(wc, Paint.Shade(style.WallLow, 1.22f), flat * 0.55f * Smooth(0.8f, 2f, h));
                if (style.Walls == BiomeWalls.Ice)
                {
                    // snow settles on the gentler ledges
                    float snow = (1f - Smooth(0.6f, 1.4f, steep)) * Smooth(0.5f, 2f, h);
                    wc = Color.Lerp(wc, new Color(0.9f, 0.94f, 0.98f), snow * 0.8f);
                }
                // the wall colours are albedo: the vertex tint multiplies the surroundings texture's average colour, and a
                // tint can't brighten it — so the whole wall palette is scaled down together until its brightest band fits
                var avg = Biomes.AverageFallback(style.Side);
                float norm = WallNorm(avg);
                wc = new Color(wc.r / Mathf.Max(0.05f, avg.r) * norm, wc.g / Mathf.Max(0.05f, avg.g) * norm, wc.b / Mathf.Max(0.05f, avg.b) * norm);
                c = Color.Lerp(c, wc, k);
                strength *= 1f - 0.97f * k;
                detail *= 1f - k;
            }
            else if (h < -0.05f)
            {
                float k = Smooth(0.05f, 4f, -h);
                c = new Color(c.r * Mathf.Lerp(1f, 0.1f, k), c.g * Mathf.Lerp(1f, 0.1f, k), c.b * Mathf.Lerp(1f, 0.12f, k));
                strength *= 1f - 0.8f * k;
                detail *= 1f - k;
            }
            return c;
        }

        // ================================================================== wall dressing

        /// <summary>Boulders, ledges, stalagmites / crystals / roots and fungus, or masonry, along the walls.</summary>
        void BuildWalls()
        {
            if (Masonry) { BuildMasonry(); return; }
            var rng = new System.Random(StableHash(def.id) * 3 + 77);
            float R() => (float)rng.NextDouble();
            var rocks = new MeshBuilder(StableHash(def.id) + 501) { Jitter = 0.09f };
            var spikes = new MeshBuilder(StableHash(def.id) + 502) { Jitter = 0.03f };
            var glows = new MeshBuilder(StableHash(def.id) + 503) { Jitter = 0.02f };
            var rockLow = style.WallLow;
            var rockHigh = style.WallHigh;

            // along the back wall (x) and both side walls (y): foot boulders, ledges on the face, spikes at the foot
            foreach (var spot in WallSpots(rng, 2.4f))
            {
                var p = spot.foot;
                if (Reserved(p.x, p.y, 0.5f)) continue;
                float hf = Height(p.x, p.y);
                // a boulder at the foot, half sunk into the floor
                float bs = Mathf.Lerp(0.45f, 1.25f, R()) * (R() < 0.15f ? 1.5f : 1f);
                Boulder(rocks, rng, new Vector3(p.x, hf, p.y), bs, Paint.Shade(Color.Lerp(rockLow, rockHigh, 0.1f), 0.95f + 0.2f * R()));
                // spikes: stalagmites, ice crystals, or roots and fungus
                if (R() < (style.Walls == BiomeWalls.Rock ? 0.3f : 0.5f))
                {
                    var q = spot.foot - spot.into * Mathf.Lerp(0.2f, 1.1f, R()) + spot.along * (R() - 0.5f) * 1.6f;
                    if (q.x > 0.3f && q.x < W - 0.3f && q.y > 0.3f && q.y < D - 0.3f && !OpenGround(q.x, q.y, 0.4f)) continue;
                    var at = new Vector3(q.x, Height(q.x, q.y), q.y);
                    switch (style.Walls)
                    {
                        case BiomeWalls.Ice: Crystals(spikes, glows, rng, at, Mathf.Lerp(0.7f, 1.9f, R())); break;
                        case BiomeWalls.Roots: Fungus(glows, rng, at, Mathf.Lerp(0.6f, 1.2f, R())); break;
                        default: Stalagmite(spikes, rng, at, Mathf.Lerp(0.5f, 1.7f, R()), Color.Lerp(rockLow, rockHigh, 0.25f)); break;
                    }
                }
            }
            FrontLip(rocks, rng, Color.Lerp(rockLow, rockHigh, 0.2f));
            BuildCliffColumns(rocks, rng);
            if (style.Walls == BiomeWalls.Roots) BuildWallRoots(rng);
            if (style.Walls == BiomeWalls.Ice) BuildIcicleLedges(rng, spikes, glows);
            Emit(rocks, "Wall Rocks", true);
            Emit(spikes, "Wall Spikes", true);
            Emit(glows, "Wall Glows");
        }

        /// <summary>A faceted boulder half sunk into the floor, its top paler (dust, lit from above).</summary>
        internal static void Boulder(MeshBuilder mb, System.Random rng, Vector3 b, float size, Color col)
        {
            float R() => (float)rng.NextDouble();
            var rad = new Vector3(size, size * (0.6f + 0.15f * R()), size * (0.75f + 0.3f * R()));
            var c = b + new Vector3(0f, rad.y * 0.35f, 0f);
            mb.Color = col;
            mb.Blob(c, rad, 1, 0.28f, rng.Next(1000), 0.45f);
            mb.Color = Paint.Shade(col, 1.22f);
            mb.Blob(c + new Vector3((R() - 0.5f) * size * 0.2f, rad.y * 0.5f, (R() - 0.5f) * size * 0.15f), new Vector3(rad.x * 0.72f, rad.y * 0.42f, rad.z * 0.7f), 0, 0.2f, rng.Next(1000), 0.6f);
        }

        /// <summary>
        /// The walls' faces: rows of faceted, slab-like rock columns (the shrine cliffs' language) standing on the
        /// terrain's tiers — a low row at the foot reaching the ledge, a taller one behind it climbing into the dark; at
        /// the sides lower rows, so the camera at yaw ±45° still looks over them. Ink-outlined, lit tops, dark feet.
        /// </summary>
        void BuildCliffColumns(MeshBuilder mb, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            Color foot, mid, top, crown;
            switch (style.Walls)
            {
                case BiomeWalls.Ice:
                    foot = new Color(0.5f, 0.64f, 0.78f); mid = new Color(0.66f, 0.82f, 0.93f); top = new Color(0.8f, 0.9f, 0.98f); crown = new Color(0.94f, 0.97f, 1f);
                    break;
                case BiomeWalls.Roots:
                    foot = Paint.Shade(style.WallHigh, 0.9f); mid = style.WallLow; top = Paint.Shade(style.WallLow, 1.15f); crown = Ui.Hex("#6f7a5a");
                    break;
                default:
                    foot = Paint.Shade(style.WallLow, 0.72f); mid = Paint.Shade(style.WallLow, 0.92f); top = Paint.Shade(style.WallLow, 1.1f); crown = Paint.Shade(style.WallLow, 1.28f);
                    break;
            }
            float emit = style.Walls == BiomeWalls.Ice ? 0.06f : 0f;

            void Column(float x, float z, float r, float h, float squash, float sink)
            {
                float gy = Height(x, z) - sink;
                float t = Mathf.Lerp(0.82f, 0.94f, R());
                var prof = new[] { new Vector2(r * 1.06f, 0f), new Vector2(r, sink + 0.4f), new Vector2(r * Mathf.Lerp(1f, t, 0.5f), (h + sink) * 0.55f), new Vector2(r * t, h + sink - 0.12f), new Vector2(r * t * 0.82f, h + sink) };
                float k = 0.94f + 0.12f * R();
                var cols = new[] { Paint.Shade(foot, k * 0.9f), Paint.Shade(foot, k), Paint.Shade(mid, k), Paint.Shade(top, k), Paint.Shade(crown, k) };
                mb.Emission = emit;
                // leaning a little this way or that, never quite in step with its neighbours
                mb.Push().Translate(x, gy, z).Rotate((R() - 0.5f) * 7f, R() * 60f, (R() - 0.5f) * 9f).Scale(new Vector3(1f, 1f, squash));
                mb.Lathe(prof, 6, false, false, true, cols, 0f);
                mb.Pop();
                mb.Emission = 0f;
            }

            // the back wall: a row at the foot up to the ledge, a taller row on the ledge behind it
            for (float x = -3f; x < W + 3f; x += Mathf.Lerp(2.1f, 3.2f, R()))
            {
                if (PassageAt(true, x, 0f)) continue;
                float r = Mathf.Lerp(1.05f, 1.7f, R());
                float z = D + BackStart + r * 0.62f + 0.25f;
                float hh = Mathf.Lerp(2.8f, 5.6f, R()) * (R() < 0.15f ? 0.6f : 1f);
                Column(x, z, r, hh, Mathf.Lerp(0.55f, 0.8f, R()), 0.4f);
            }
            for (float x = -4f; x < W + 4f; x += Mathf.Lerp(2.6f, 3.8f, R()))
            {
                if (PassageAt(true, x, 0f)) continue;
                float r = Mathf.Lerp(1.4f, 2.2f, R());
                float z = D + BackStart + Mathf.Lerp(3.6f, 4.6f, R()) + r * 0.5f;
                Column(x, z, r, Mathf.Lerp(5f, 8.5f, R()), Mathf.Lerp(0.6f, 0.85f, R()), 1f);
            }
            // the sides: lower rows, sinking towards the front cut
            for (int sd = 0; sd < 2; sd++)
            {
                float sign = sd == 0 ? -1f : 1f;
                float edge = sd == 0 ? 0f : W;
                for (float y = -0.5f; y < D + 1.5f; y += Mathf.Lerp(2.2f, 3.2f, R()))
                {
                    if (PassageAt(false, y, 0f, sign)) continue;
                    float r = Mathf.Lerp(0.9f, 1.4f, R());
                    float front = Smooth(-1f, 7f, y);
                    float x = edge + sign * (SideStart + r * 0.7f + 0.2f);
                    Column(x, y, r, Mathf.Lerp(1.6f, 2.8f, R()) * Mathf.Lerp(0.55f, 1f, front), 1f, 0.4f);
                    if (R() < 0.85f)
                    {
                        float r2 = Mathf.Lerp(1.3f, 2f, R());
                        float x2 = edge + sign * (SideStart + Mathf.Lerp(3.8f, 4.8f, R()) + r2 * 0.6f);
                        Column(x2, y + (R() - 0.5f) * 1.2f, r2, Mathf.Lerp(3.5f, 6f, R()) * Mathf.Lerp(0.5f, 1f, front), 1f, 1f);
                    }
                }
            }
        }

        /// <summary>Low rocks along the front lip: the floor's edge reads as a ledge over the dark, not a fade.</summary>
        void FrontLip(MeshBuilder mb, System.Random rng, Color col)
        {
            float R() => (float)rng.NextDouble();
            for (float x = -1f; x < W + 1f; x += Mathf.Lerp(1.4f, 3.2f, R()))
            {
                bool exit = false;
                for (int i = 0; i < frontExits.Count; i++) if (Mathf.Abs(x - frontExits[i]) < 3f) exit = true;
                if (exit) continue;
                float y = -0.75f - R() * 0.5f;
                float s = Mathf.Lerp(0.3f, 0.75f, R());
                mb.Color = Paint.Shade(col, 0.85f + 0.2f * R());
                mb.Blob(new Vector3(x, Height(x, y) + s * 0.15f, y), new Vector3(s * 1.2f, s * 0.55f, s), 1, 0.25f, rng.Next(1000), 0.4f);
            }
        }

        struct WallSpot { public Vector2 foot, into, along; }

        /// <summary>Points along the wall's foot (back and sides), every ~step metres, with the direction into the wall.</summary>
        IEnumerable<WallSpot> WallSpots(System.Random rng, float step)
        {
            float R() => (float)rng.NextDouble();
            for (float x = -2f; x < W + 2f; x += step * Mathf.Lerp(0.7f, 1.3f, R()))
                yield return new WallSpot { foot = new Vector2(x, D + BackStart + 0.2f), into = Vector2.up, along = Vector2.right };
            for (int s = 0; s < 2; s++)
                for (float y = -1f; y < D; y += step * Mathf.Lerp(0.7f, 1.3f, R()))
                {
                    float x = s == 0 ? -SideStart - 0.3f : W + SideStart + 0.3f;
                    yield return new WallSpot { foot = new Vector2(x, y), into = s == 0 ? Vector2.left : Vector2.right, along = Vector2.up };
                }
        }

        /// <summary>A small cluster of stalagmites: lumpy, leaning a little, of different heights.</summary>
        internal static void Stalagmite(MeshBuilder mb, System.Random rng, Vector3 b, float height, Color col)
        {
            float R() => (float)rng.NextDouble();
            int n = 1 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float h = height * (i == 0 ? 1f : Mathf.Lerp(0.35f, 0.7f, R()));
                float r = h * Mathf.Lerp(0.2f, 0.27f, R());
                float a = R() * Mathf.PI * 2f;
                var p = b + (i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (height * 0.28f));
                var lean = new Vector3((R() - 0.5f) * 0.25f, 1f, (R() - 0.5f) * 0.25f).normalized;
                mb.Color = Paint.Shade(col, 0.9f + 0.18f * R());
                // a lumpy foot, then the tapering column
                mb.Blob(p + new Vector3(0f, r * 0.35f, 0f), new Vector3(r * 1.25f, r * 0.6f, r * 1.15f), 0, 0.2f, rng.Next(1000), 0.5f);
                var mid = p + lean * (h * 0.45f);
                mb.Segment(p, mid, r, r * 0.62f, 6, false, false);
                mb.Segment(mid, p + lean * h, r * 0.62f, r * 0.08f, 6, false, false);
            }
        }

        internal static void Crystals(MeshBuilder mb, MeshBuilder glow, System.Random rng, Vector3 b, float height)
        {
            float R() => (float)rng.NextDouble();
            int n = 3 + rng.Next(3);
            var ice = new Color(0.72f, 0.88f, 0.96f);
            for (int i = 0; i < n; i++)
            {
                float a = R() * Mathf.PI * 2f, lean = Mathf.Lerp(0.05f, 0.45f, R());
                float h = height * (i == 0 ? 1f : Mathf.Lerp(0.35f, 0.75f, R()));
                float r = h * Mathf.Lerp(0.11f, 0.16f, R());
                var dir = new Vector3(Mathf.Cos(a) * lean, 1f, Mathf.Sin(a) * lean).normalized;
                var basePt = b + new Vector3(Mathf.Cos(a) * r * (i == 0 ? 0f : 1.2f), -0.05f, Mathf.Sin(a) * r * (i == 0 ? 0f : 1.2f));
                var tip = basePt + dir * h;
                var target = i == 0 ? glow : mb;
                target.Color = Paint.Shade(ice, 0.92f + 0.14f * R());
                target.Emission = i == 0 ? 0.35f : 0.12f;
                target.Segment(basePt, basePt + dir * (h * 0.78f), r, r * 0.92f, 6, false, false);
                target.Segment(basePt + dir * (h * 0.78f), tip, r * 0.92f, 0.01f, 6, false, false);
                target.Emission = 0f;
            }
        }

        internal static void Fungus(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            var cols = new[] { new Color(0.78f, 0.6f, 0.96f), new Color(0.56f, 0.9f, 0.84f) };
            var cap = cols[rng.Next(cols.Length)];
            int n = 3 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                float a = R() * Mathf.PI * 2f, d = R() * size * 0.5f;
                var p = b + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                float h = size * Mathf.Lerp(0.18f, 0.5f, R()), cr = h * Mathf.Lerp(0.45f, 0.7f, R());
                mb.Emission = 0.15f;
                mb.Color = new Color(0.86f, 0.82f, 0.88f);
                mb.Cylinder(p, cr * 0.22f, cr * 0.16f, h, 5);
                mb.Emission = 0.75f;
                mb.Color = Paint.Shade(cap, 0.9f + 0.15f * R());
                mb.Blob(p + new Vector3(0f, h, 0f), new Vector3(cr, cr * 0.45f, cr), 0, 0.1f, rng.Next(1000), 0.6f);
            }
            mb.Emission = 0f;
        }

        /// <summary>The Hollow Heart: great roots draping down the back and side walls onto the floor.</summary>
        void BuildWallRoots(System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            var mb = new MeshBuilder(StableHash(def.id) + 507) { Jitter = 0.03f };
            var bark = Ui.Hex("#4a3a40");
            foreach (var spot in WallSpots(rng, 5.5f))
            {
                if (R() < 0.35f || Reserved(spot.foot.x, spot.foot.y, 1f)) continue;
                // from high on the face down over the foot and a little way onto the floor
                var top = spot.foot + spot.into * Mathf.Lerp(4f, 7f, R());
                var pts = new List<Vector3>();
                int n = 7;
                float wob = R() * 10f;
                for (int i = 0; i <= n; i++)
                {
                    float t = (float)i / n;
                    var g = Vector2.Lerp(top, spot.foot - spot.into * Mathf.Lerp(0.6f, 1.6f, R() * 0.3f + 0.7f), t);
                    g += spot.along * Mathf.Sin(t * 3f + wob) * 0.8f;
                    pts.Add(new Vector3(g.x, Height(g.x, g.y) + 0.12f, g.y));
                }
                float r0 = Mathf.Lerp(0.22f, 0.42f, R());
                mb.Color = Paint.Shade(bark, 0.9f + 0.2f * R());
                for (int i = 0; i < n; i++)
                {
                    float t0 = (float)i / n, t1 = (float)(i + 1) / n;
                    mb.Segment(pts[i], pts[i + 1], Mathf.Lerp(r0, r0 * 0.35f, t0), Mathf.Lerp(r0, r0 * 0.35f, t1), 6, false, i == 0);
                }
            }
            Emit(mb, "Wall Roots", true);
        }

        /// <summary>The ice cave: icicle fringes under the ledges of the back wall.</summary>
        void BuildIcicleLedges(System.Random rng, MeshBuilder mb, MeshBuilder glow)
        {
            float R() => (float)rng.NextDouble();
            for (float x = 0f; x < W; x += Mathf.Lerp(2.5f, 5f, R()))
            {
                float y = D + BackStart + Mathf.Lerp(1.8f, 2.8f, R());
                float h = Height(x, y);
                if (h < 3f) continue;
                int n = 3 + rng.Next(4);
                for (int i = 0; i < n; i++)
                {
                    float xx = x + (R() - 0.5f) * 1.4f;
                    float hh = Height(xx, y) - 0.1f;
                    float len = Mathf.Lerp(0.4f, 1.3f, R());
                    mb.Color = Paint.Shade(new Color(0.78f, 0.9f, 0.98f), 0.95f + 0.1f * R());
                    mb.Emission = 0.15f;
                    mb.Segment(new Vector3(xx, hh, y - 0.35f), new Vector3(xx, hh - len, y - 0.45f), 0.09f, 0.01f, 5, false, false);
                    mb.Emission = 0f;
                }
            }
        }

        // ------------------------------------------------------------------ masonry (crypts)

        void BuildMasonry()
        {
            var rng = new System.Random(StableHash(def.id) * 5 + 3);
            float R() => (float)rng.NextDouble();
            var blocks = new MeshBuilder(StableHash(def.id) + 601) { Jitter = 0.01f };
            var trim = new MeshBuilder(StableHash(def.id) + 602) { Jitter = 0.01f };
            var stone = style.WallLow;
            var dark = style.WallHigh;
            const float course = 0.62f, blockW = 1.15f;

            // the back wall: courses of dressed blocks, pilasters every ~4.6 m with alcoves between, a cornice
            float yFace = D + 0.55f, thick = 0.9f;
            float x0 = -SideStart - 0.4f, x1 = W + SideStart + 0.4f;
            int rows = Mathf.CeilToInt(MasonryBackH / course);
            for (int r = 0; r < rows; r++)
            {
                float y0 = r * course;
                float off = (r % 2) * blockW * 0.5f;
                for (float x = x0 - off; x < x1; x += blockW)
                {
                    float a = Mathf.Max(x0, x), b = Mathf.Min(x1, x + blockW);
                    if (b - a < 0.1f) continue;
                    if (PassageAt(true, (a + b) * 0.5f, y0)) continue;
                    float k = 0.88f + 0.16f * R();
                    // higher courses darker (soot, and the light falls off)
                    blocks.Color = Paint.Shade(Color.Lerp(stone, dark, Smooth(1f, MasonryBackH + 2f, y0)), k);
                    float inset = 0.02f + 0.04f * R();
                    blocks.Box(new Vector3((a + b) * 0.5f, y0 + course * 0.5f, yFace + thick * 0.5f + inset), new Vector3(b - a - 0.05f, course - 0.05f, thick));
                }
            }
            // mortar: a dark backing just behind the blocks' joints
            blocks.Color = Paint.Shade(dark, 0.55f);
            blocks.Box(new Vector3((x0 + x1) * 0.5f, MasonryBackH * 0.5f, yFace + thick * 0.5f + 0.08f), new Vector3(x1 - x0, MasonryBackH, thick));
            float pitch = 4.6f;
            for (float px = 0.5f; px < W; px += pitch)
            {
                if (PassageAt(true, px, 0f)) continue;
                // pilaster with plinth and capital
                trim.Color = Paint.Shade(stone, 1.02f);
                trim.Box(new Vector3(px, 0.3f, yFace - 0.12f), new Vector3(1.05f, 0.6f, 0.5f));
                trim.Color = Paint.Shade(stone, 0.96f + 0.06f * R());
                trim.Box(new Vector3(px, MasonryBackH * 0.5f, yFace - 0.05f), new Vector3(0.78f, MasonryBackH, 0.36f));
                trim.Color = Paint.Shade(stone, 1.05f);
                trim.Box(new Vector3(px, MasonryBackH - 0.5f, yFace - 0.12f), new Vector3(1.05f, 0.34f, 0.5f));
                // an alcove between this pilaster and the next: a dark arched recess (bones, urns and candles are props)
                float ax = px + pitch * 0.5f;
                if (ax < W - 0.8f && !PassageAt(true, ax, 0f) && R() < 0.75f)
                {
                    trim.Color = Paint.Shade(dark, 0.42f);
                    trim.Box(new Vector3(ax, 1.9f, yFace + 0.01f), new Vector3(1.3f, 1.9f, 0.04f));
                    // the arch: a disc stood up to face the camera (−Z)
                    trim.Push().Translate(new Vector3(ax, 2.85f, yFace + 0.005f)).Rotate(-90f, 0f, 0f);
                    trim.Disc(Vector3.zero, 0.65f, 12, false);
                    trim.Pop();
                    trim.Color = Paint.Shade(stone, 1.02f);
                    trim.Box(new Vector3(ax, 0.98f, yFace - 0.08f), new Vector3(1.55f, 0.12f, 0.24f));
                }
            }
            // cornice
            trim.Color = Paint.Shade(stone, 0.92f);
            trim.Box(new Vector3((x0 + x1) * 0.5f, MasonryBackH + 0.15f, yFace + 0.1f), new Vector3(x1 - x0, 0.3f, 1.2f));
            Emit(blocks, "Masonry Blocks");

            // the low side walls (the camera looks over them at yaw ±45°): coping on top, buttresses
            var side = new MeshBuilder(StableHash(def.id) + 603) { Jitter = 0.01f };
            for (int s = 0; s < 2; s++)
            {
                float xf = s == 0 ? -0.55f : W + 0.55f;
                float xc = xf + (s == 0 ? -0.45f : 0.45f);
                int rowsS = Mathf.CeilToInt(MasonrySideH / course);
                for (int r = 0; r < rowsS; r++)
                {
                    float y0 = r * course;
                    float off = (r % 2) * blockW * 0.5f;
                    for (float y = -1.2f - off; y < D + 0.6f; y += blockW)
                    {
                        float a = Mathf.Max(-1.2f, y), b = Mathf.Min(D + 0.6f, y + blockW);
                        if (b - a < 0.1f || PassageAt(false, (a + b) * 0.5f, y0, s == 0 ? -1f : 1f)) continue;
                        side.Color = Paint.Shade(Color.Lerp(stone, dark, 0.15f), 0.88f + 0.16f * R());
                        side.Box(new Vector3(xc, y0 + course * 0.5f, (a + b) * 0.5f), new Vector3(0.9f, course - 0.05f, b - a - 0.05f));
                    }
                }
                for (float y = -1.2f; y < D + 0.6f; y += 1.0f)
                {
                    if (PassageAt(false, y + 0.5f, 0f, s == 0 ? -1f : 1f)) continue;
                    trim.Color = Paint.Shade(stone, 1.0f + 0.05f * R());
                    trim.Box(new Vector3(xc, MasonrySideH + 0.1f, y + 0.5f), new Vector3(1.1f, 0.2f, 1.02f));
                }
            }
            Emit(side, "Masonry Side Walls");
            Emit(trim, "Masonry Trim", true);

            // rubble along the walls' foot
            var rubble = new MeshBuilder(StableHash(def.id) + 604) { Jitter = 0.06f };
            foreach (var spot in WallSpots(rng, 1.6f))
            {
                if (R() < 0.45f) continue;
                var p = spot.foot - spot.into * (Masonry ? (spot.into.y > 0f ? 1.25f : 1.05f) : 0.3f) + spot.along * (R() - 0.5f);
                if (Reserved(p.x, p.y, 0.3f) || !OpenGround(p.x, p.y, 0.2f)) continue;
                for (int i = 0; i < 2 + rng.Next(3); i++)
                {
                    var q = p + new Vector2((R() - 0.5f) * 0.7f, (R() - 0.5f) * 0.5f);
                    Rubble(rubble, rng, new Vector3(q.x, 0f, q.y), Mathf.Lerp(0.12f, 0.3f, R()), stone);
                }
            }
            FrontLip(rubble, rng, Color.Lerp(stone, dark, 0.2f));
            Emit(rubble, "Wall Rubble", true);
        }

        /// <summary>True on an exit's passage through the back (x) or a side (y) wall.</summary>
        bool PassageAt(bool back, float along, float height, float sideSign = 0f)
        {
            if (back)
            {
                for (int i = 0; i < backExits.Count; i++) if (Mathf.Abs(along - backExits[i]) < 2.6f) return true;
                return false;
            }
            for (int i = 0; i < corridors.Count; i++)
                if (corridors[i].x == sideSign && Mathf.Abs(along - corridors[i].y) < 2.6f) return true;
            return false;
        }

        internal static void Rubble(MeshBuilder mb, System.Random rng, Vector3 b, float size, Color col)
        {
            float R() => (float)rng.NextDouble();
            mb.Color = Paint.Shade(col, 0.85f + 0.2f * R());
            mb.Blob(b + new Vector3(0f, size * 0.3f, 0f), new Vector3(size, size * 0.6f, size * (0.7f + 0.3f * R())), 0, 0.3f, rng.Next(1000), 0.5f);
        }
    }
}
