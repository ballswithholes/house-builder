// MapTerrain: the biomes' ground cover and hill trees, and the interior fill pass (MapDef.fill).
//
// Around the map (outside the walkable rect) each biome lays its own cover: golden grass, wheat tufts and poppies on the
// highlands; reeds, cattails, moss hummocks and bog cotton in the fen (none in its pools); dry tufts, snow-capped
// stones and drifts on the peaks; dead grass, soot-dark rocks, ash heaps and charred sticks on the dragon's summit;
// rubble, stalagmites, ice, bones or roots and glowing fungus along an indoor map's walls. Hill trees follow the biome
// (golden round trees, willows and dead snags, snowy pines). With MapDef.fill > 0 the same cover is scattered inside
// the map, on open ground only: clear of trails, water, props, exits, spawns, NPCs and chests.
using UnityEngine;

namespace Lanternvale.Game
{
    internal sealed partial class MapTerrain
    {
        /// <summary>The cover outside the map for the new biomes and indoor maps (meadow-like biomes: BuildMeadowCover).</summary>
        void BuildBiomeCover()
        {
            var rng = new System.Random(StableHash(def.id) * 7 + 13);
            float R() => (float)rng.NextDouble();
            float x0 = -46f, x1 = W + 46f;
            const float chunkW = 34f;
            for (float cx = x0; cx < x1; cx += chunkW)
            {
                var soft = new MeshBuilder(StableHash(def.id) + (int)cx + 3) { Jitter = 0.06f };
                var hard = new MeshBuilder(StableHash(def.id) + (int)cx + 9) { Jitter = 0.06f };
                float cxe = Mathf.Min(x1, cx + chunkW);
                for (float gx = cx; gx < cxe; gx += 1.4f)
                    for (float gy = -24f; gy < D + 26f; gy += 1.4f)
                    {
                        float x = gx + R() * 1.4f, y = gy + R() * 1.4f;
                        float dOut = DistanceOutside(x, y);
                        if (dOut < 0.9f || Reserved(x, y, 0.2f)) continue;
                        float h = Height(x, y);
                        if (walled)
                        {
                            // indoors only the wall's foot and the lip in front carry anything
                            if (h > 0.7f || h < -0.25f || dOut > 3.5f) continue;
                        }
                        else if (style.WaterTable && h < FenWaterLevel + 0.02f) continue;   // a pool
                        float density = y < 0f ? Mathf.Lerp(0.55f, 0.18f, Smooth(2f, 22f, dOut)) : Mathf.Lerp(0.32f, 0.08f, Smooth(2f, 20f, dOut));
                        if (walled) density = 0.35f;
                        float clump = Mathf.PerlinNoise(x * 0.21f + s2, y * 0.21f + s5);
                        density *= 0.4f + 1.2f * clump;
                        if (R() > density) continue;
                        PlaceCover(soft, hard, rng, x, y, h, dOut, false);
                    }
                Emit(soft, "Ground Cover " + cx);
                Emit(hard, "Ground Rocks " + cx, true);
            }
        }

        /// <summary>
        /// The interior fill (MapDef.fill 0..1): the biome's ground cover scattered over the map's open ground, in loose
        /// clumps, never on trails, water, props, exits, spawns, NPCs or chests.
        /// </summary>
        void BuildFill()
        {
            float fill = Mathf.Clamp01(def.fill);
            if (fill <= 0f) return;
            var rng = new System.Random(StableHash(def.id) * 11 + 29);
            float R() => (float)rng.NextDouble();
            const float chunkW = 30f;
            for (float cx = 0f; cx < W; cx += chunkW)
            {
                var soft = new MeshBuilder(StableHash(def.id) + (int)cx + 41) { Jitter = 0.06f };
                var hard = new MeshBuilder(StableHash(def.id) + (int)cx + 43) { Jitter = 0.06f };
                float cxe = Mathf.Min(W, cx + chunkW);
                for (float gx = cx; gx < cxe; gx += 1.25f)
                    for (float gy = 0f; gy < D; gy += 1.25f)
                    {
                        float x = gx + R() * 1.25f, y = gy + R() * 1.25f;
                        float clump = Mathf.PerlinNoise(x * 0.17f + s4, y * 0.17f + s6);
                        float density = fill * 0.9f * Smooth(0.25f, 0.7f, clump);
                        // the downs' grass grows in thicker sweeps than other ground cover
                        if (style.Cover == BiomeCover.Golden) density = Mathf.Min(0.95f, density * 1.5f);
                        if (R() > density) continue;
                        if (!OpenGround(x, y, 0f)) continue;
                        if (waters.Count > 0 && Shore(soft, rng, x, y)) continue;
                        PlaceCover(soft, hard, rng, x, y, 0f, 0f, true);
                    }
                Emit(soft, "Ground Fill " + cx);
                Emit(hard, "Ground Fill Rocks " + cx, true);
            }
        }

        /// <summary>
        /// Along a river's or pond's bank (outdoors): reeds and rushes, cattails in the fen, crowding the water's edge.
        /// True when it placed something (the fill's piece for this spot).
        /// </summary>
        bool Shore(MeshBuilder soft, System.Random rng, float x, float y)
        {
            if (walled || style.Cover == BiomeCover.Snow || style.Cover == BiomeCover.Ash) return false;
            float e = WaterEdge(x, y);
            if (e > 1.9f) return false;
            float R() => (float)rng.NextDouble();
            if (R() > 0.75f) return false;
            float h = Height(x, y);
            if (style.Cover == BiomeCover.Golden || style.Cover == BiomeCover.Meadow)
                Tuft(soft, rng, new Vector3(x, h, y), Mathf.Lerp(0.45f, 0.8f, R()), Ui.Hex("#7f9a4e"), Ui.Hex("#5e7a40"), null);
            else Reeds(soft, rng, new Vector3(x, h, y), Mathf.Lerp(0.8f, 1.35f, R()), style.Cover == BiomeCover.Fen && R() < 0.4f);
            return true;
        }

        /// <summary>Open ground on the map: clear of trails, water, props, exits, spawns, NPCs and chests (by clear m more).</summary>
        bool OpenGround(float x, float y, float clear)
        {
            if (x < 0.5f || x > W - 0.5f || y < 0.4f || y > D - 0.5f) return false;
            if (chains.Count > 0 && PathDistance(x, y, out float half) < half + 1.1f + clear) return false;
            if (waters.Count > 0 && WaterEdge(x, y) < 0.7f + clear) return false;
            if (HasStream && Mathf.Abs(x - StreamX(y)) < StreamHalfWidth(y) + 1f + clear) return false;
            if (pavedInside)
            {
                ShrineGround(x, y, 0f, out float paved, out float gravel);
                if (paved > 0.05f || gravel > 0.5f) return false;
            }
            for (int i = 0; i < wear.Count; i++)
            {
                var wr = wear[i];
                float r = Mathf.Max(wr.rx, wr.gather > 0f ? 2.6f : 0f) + 0.6f + clear;
                if ((new Vector2(x, y) - wr.p).sqrMagnitude < r * r) return false;
            }
            for (int i = 0; i < lanes.Count; i++)
                if (SegmentDistance(new Vector2(x, y), lanes[i].a, lanes[i].b) < lanes[i].half + 0.8f + clear) return false;
            foreach (var p in def.props)
            {
                if (p == null || string.IsNullOrEmpty(p.art)) continue;
                float sc = p.scale > 0f ? p.scale : 1f;
                float rx, ry;
                if (p.art.StartsWith("decal_")) { rx = ry = 1.4f * sc; }
                else if (p.collider != null && p.collider.w > 0f) { rx = p.collider.w * 0.5f * sc + 0.7f; ry = p.collider.h * 0.5f * sc + 0.7f; }
                else { rx = ry = 1.2f * sc; }
                rx += clear; ry += clear;
                float dx = (x - p.pos.x) / rx, dy = (y - p.pos.y) / ry;
                if (dx * dx + dy * dy < 1f) return false;
            }
            foreach (var t in def.transitions)
                if (t != null && Mathf.Abs(x - t.pos.x) < t.size.x * 0.5f + 1.2f + clear && Mathf.Abs(y - t.pos.y) < t.size.y * 0.5f + 1.2f + clear) return false;
            foreach (var sp in def.spawns)
                if (sp != null && (new Vector2(x - sp.pos.x, y - sp.pos.y)).sqrMagnitude < 2.6f + clear) return false;
            foreach (var n in def.npcs)
                if (n != null && (new Vector2(x - n.pos.x, y - n.pos.y)).sqrMagnitude < 1.6f + clear) return false;
            foreach (var c in def.chests)
                if (c != null && (new Vector2(x - c.pos.x, y - c.pos.y)).sqrMagnitude < 1.6f + clear) return false;
            return true;
        }

        /// <summary>One piece of the biome's cover at a ground point (inside: the fill pass, smaller and never bushy).</summary>
        void PlaceCover(MeshBuilder soft, MeshBuilder hard, System.Random rng, float x, float y, float h, float dOut, bool inside)
        {
            float R() => (float)rng.NextDouble();
            var at = new Vector3(x, h, y);
            float pick = R();
            var grass = style.Grass;
            var dark = style.GrassDark;
            switch (style.Cover)
            {
                case BiomeCover.Meadow:
                    if (pick < 0.62f) Tuft(soft, rng, at, Mathf.Lerp(0.3f, 0.6f, R()), grass, dark, null);
                    else if (pick < 0.86f) Tuft(soft, rng, at, Mathf.Lerp(0.28f, 0.5f, R()), grass, dark, style.Flowers);
                    else if (pick < 0.95f) Stone(hard, rng, at + new Vector3(0f, -0.04f, 0f), Mathf.Lerp(0.12f, 0.32f, R()));
                    else if (!inside || OpenGround(x, y, 1.2f)) Bush(hard, rng, at, Mathf.Lerp(0.45f, 0.75f, R()), style.Bush);
                    break;
                case BiomeCover.Golden:
                    // tall, sun-ripened grass in sweeping clumps, ripe wheat among it
                    if (pick < 0.45f) Tuft(soft, rng, at, Mathf.Lerp(inside ? 0.5f : 0.35f, inside ? 0.95f : 0.75f, R()), grass, dark, null);
                    else if (pick < 0.7f) Wheat(soft, rng, at, Mathf.Lerp(0.6f, inside ? 0.95f : 1.05f, R()));
                    else if (pick < 0.85f) Tuft(soft, rng, at, Mathf.Lerp(0.3f, 0.55f, R()), grass, dark, style.Flowers);
                    else if (pick < 0.94f) Stone(hard, rng, at + new Vector3(0f, -0.04f, 0f), Mathf.Lerp(0.14f, 0.4f, R()));
                    else if (!inside ? dOut > 3f : OpenGround(x, y, 1.2f)) Bush(hard, rng, at, Mathf.Lerp(0.5f, 0.85f, R()), style.Bush);
                    break;
                case BiomeCover.Fen:
                {
                    // reeds crowd the water's edge
                    bool shore = !inside && h < FenWaterLevel + 0.25f;
                    if (shore && pick < 0.65f) Reeds(soft, rng, at, Mathf.Lerp(0.9f, 1.6f, R()), R() < 0.4f);
                    else if (pick < 0.36f) Tuft(soft, rng, at, Mathf.Lerp(0.3f, 0.6f, R()), grass, dark, null);
                    else if (pick < 0.56f) Reeds(soft, rng, at, Mathf.Lerp(inside ? 0.6f : 0.8f, inside ? 1.0f : 1.4f, R()), R() < 0.3f);
                    else if (pick < 0.74f) MossMound(soft, rng, at, Mathf.Lerp(0.3f, 0.65f, R()));
                    else if (pick < 0.86f) Tuft(soft, rng, at, Mathf.Lerp(0.3f, 0.5f, R()), grass, dark, style.Flowers);
                    else if (pick < 0.94f) Stone(hard, rng, at + new Vector3(0f, -0.06f, 0f), Mathf.Lerp(0.12f, 0.3f, R()));
                    else if (!inside ? dOut > 3f : OpenGround(x, y, 1.2f)) Bush(hard, rng, at, Mathf.Lerp(0.5f, 0.8f, R()), style.Bush);
                    break;
                }
                case BiomeCover.Snow:
                    if (pick < 0.45f) Tuft(soft, rng, at, Mathf.Lerp(0.22f, 0.45f, R()), grass, dark, null);
                    else if (pick < 0.66f) SnowStone(hard, rng, at, Mathf.Lerp(0.18f, inside ? 0.34f : 0.5f, R()));
                    else if (pick < 0.76f && !inside) SnowMound(soft, rng, at, Mathf.Lerp(0.6f, 1.4f, R()));
                    else if (pick < 0.76f) SnowStone(hard, rng, at, Mathf.Lerp(0.14f, 0.26f, R()));
                    else if (pick < 0.9f) Tuft(soft, rng, at, Mathf.Lerp(0.2f, 0.35f, R()), grass, dark, style.Flowers);
                    else if (!inside ? dOut > 3f : OpenGround(x, y, 1.2f)) SnowyBush(hard, rng, at, Mathf.Lerp(0.45f, 0.75f, R()), style.Bush);
                    break;
                case BiomeCover.Ash:
                    if (pick < 0.32f) Tuft(soft, rng, at, Mathf.Lerp(0.2f, 0.42f, R()), grass, dark, null);
                    else if (pick < 0.62f) Rubble(hard, rng, at, Mathf.Lerp(0.15f, inside ? 0.35f : 0.6f, R()), Ui.Hex("#55504c"));
                    else if (pick < 0.8f) AshMound(soft, rng, at, Mathf.Lerp(0.35f, 0.8f, R()));
                    else if (pick < 0.92f) CharredSticks(hard, rng, at, Mathf.Lerp(0.5f, 0.9f, R()));
                    else Bones(hard, rng, at, Mathf.Lerp(0.6f, 1f, R()));
                    break;
                case BiomeCover.Rubble:
                    if (pick < 0.55f) Rubble(hard, rng, at, Mathf.Lerp(0.1f, 0.3f, R()), Color.Lerp(style.WallLow, style.WallHigh, 0.2f));
                    else if (pick < 0.78f) Pebbles(hard, rng, at, Color.Lerp(style.WallLow, style.WallHigh, 0.1f));
                    else if (pick < 0.9f) Stalagmite(hard, rng, at, Mathf.Lerp(0.35f, inside ? 0.8f : 1.2f, R()), Color.Lerp(style.WallLow, style.WallHigh, 0.2f));
                    else Tuft(soft, rng, at, Mathf.Lerp(0.15f, 0.3f, R()), grass, dark, null);
                    break;
                case BiomeCover.Ice:
                    if (pick < 0.4f) Rubble(hard, rng, at, Mathf.Lerp(0.12f, 0.32f, R()), new Color(0.66f, 0.78f, 0.86f));
                    else if (pick < 0.55f && !inside) SnowMound(soft, rng, at, Mathf.Lerp(0.4f, 0.8f, R()));
                    else if (pick < 0.55f) Pebbles(hard, rng, at, new Color(0.7f, 0.8f, 0.88f));
                    else if (pick < 0.85f) Crystals(hard, hard, rng, at, Mathf.Lerp(0.3f, inside ? 0.6f : 0.9f, R()));
                    else Pebbles(hard, rng, at, new Color(0.7f, 0.8f, 0.88f));
                    break;
                case BiomeCover.Crypt:
                    if (pick < 0.5f) Rubble(hard, rng, at, Mathf.Lerp(0.1f, 0.28f, R()), style.WallLow);
                    else if (pick < 0.72f) Bones(hard, rng, at, Mathf.Lerp(0.5f, 0.85f, R()));
                    else if (pick < 0.9f) FlagShard(hard, rng, at, style.WallLow);
                    else Pebbles(hard, rng, at, style.WallLow);
                    break;
                case BiomeCover.Hollow:
                    if (pick < 0.32f) RootArc(hard, rng, at, Mathf.Lerp(0.5f, 1.1f, R()));
                    else if (pick < 0.55f) Fungus(soft, rng, at, Mathf.Lerp(0.3f, 0.6f, R()));
                    else if (pick < 0.8f) Tuft(soft, rng, at, Mathf.Lerp(0.2f, 0.42f, R()), grass, dark, R() < 0.3f ? style.Flowers : null);
                    else Rubble(hard, rng, at, Mathf.Lerp(0.12f, 0.3f, R()), style.WallLow);
                    break;
            }
        }

        /// <summary>Hill trees of the new outdoor biomes (the original maps: BuildMeadowCover).</summary>
        void BuildBiomeHillTrees()
        {
            var rng = new System.Random(StableHash(def.id) * 17 + 5);
            float R() => (float)rng.NextDouble();
            var trees = new MeshBuilder(StableHash(def.id) + 199) { Jitter = 0.07f };
            var leaf = style.HillLeaf;
            int count = 0;
            float cell = style.SnowyPines ? 5.5f : 7f;
            for (float gx = -120f; gx < W + 120f; gx += cell)
                for (float gy = -40f; gy < D + 150f; gy += cell)
                {
                    float x = gx + R() * cell, y = gy + R() * cell;
                    float dOut = DistanceOutside(x, y);
                    if (dOut < (y > D ? 6.5f : 11f)) continue;
                    if (y < -8f && dOut < 26f) continue;
                    if (Reserved(x, y, 3f)) continue;
                    float hh = Height(x, y);
                    if (style.WaterTable && hh < FenWaterLevel + 0.1f) continue;
                    if (style.SnowyPines && hh > 26f) continue;   // the treeline
                    float clump = Mathf.PerlinNoise(x * 0.035f + s6, y * 0.035f + s3);
                    float thresh = style.SnowyPines ? 0.42f : 0.52f;
                    if (clump < thresh || R() > (clump - thresh + 0.07f) * 1.6f) continue;
                    float size = Mathf.Lerp(0.7f, 1.25f, R()) * Mathf.Lerp(1f, 1.5f, Smooth(30f, 120f, dOut));
                    var b = Local(x, y, -0.2f);
                    var lf = leaf[rng.Next(leaf.Length)];
                    if (style.SnowyPines) SnowyPine(trees, rng, b, size * 7.5f, Paint.Shade(lf, 0.95f));
                    else if (style.Id == Biomes.Fen)
                    {
                        if (R() < 0.3f) DeadSnag(trees, rng, b, size * 4.5f);
                        else Willow(trees, rng, b, size * 5f, lf);
                    }
                    else if (R() < style.PineShare) Pine(trees, rng, b, size * 7f, Paint.Shade(leaf[3], 0.9f));
                    else RoundTree(trees, rng, b, size * 5.5f, lf);
                    if (++count > 320) break;
                }
            Emit(trees, "Hill Trees");
        }

        // ================================================================== shapes (local Y-up, like Tuft / Stone / Bush)

        /// <summary>A tuft of ripe wheat: straw stalks nodding under golden ears.</summary>
        internal static void Wheat(MeshBuilder mb, System.Random rng, Vector3 b, float height)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 1.1f; mb.WindGradient = true; mb.WindY0 = b.y; mb.WindY1 = b.y + height;
            int n = 7 + rng.Next(5);
            var straw = new Color(0.86f, 0.74f, 0.42f);
            var ear = new Color(0.94f, 0.78f, 0.38f);
            for (int i = 0; i < n; i++)
            {
                float a = R() * Mathf.PI * 2f, lean = 0.06f + R() * 0.16f;
                float hh = height * (0.75f + 0.25f * R());
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var basePt = b + dir * (0.03f + 0.07f * R());
                var tip = basePt + dir * (lean * hh) + Vector3.up * hh;
                mb.Color = Paint.Shade(straw, 0.85f + 0.2f * R());
                mb.Blade(basePt, tip, 0.035f, new Vector3(-dir.z, 0f, dir.x));
                // the ear, nodding over
                var earTip = tip + dir * 0.07f + Vector3.up * 0.1f;
                mb.Color = Paint.Shade(ear, 0.9f + 0.18f * R());
                mb.Segment(tip - Vector3.up * 0.02f, earTip, 0.028f, 0.012f, 4, false, true);
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        /// <summary>Reeds: tall dark blades fading to ochre tips, now and then cattails' brown heads.</summary>
        internal static void Reeds(MeshBuilder mb, System.Random rng, Vector3 b, float height, bool cattails)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0.9f; mb.WindGradient = true; mb.WindY0 = b.y; mb.WindY1 = b.y + height;
            int n = 10 + rng.Next(6);
            var green = new Color(0.42f, 0.52f, 0.3f);
            var tipC = new Color(0.72f, 0.64f, 0.38f);
            for (int i = 0; i < n; i++)
            {
                float a = R() * Mathf.PI * 2f, lean = 0.05f + R() * 0.2f;
                float hh = height * (0.6f + 0.4f * R());
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var basePt = b + dir * (0.03f + 0.09f * R());
                var mid = basePt + dir * (lean * hh * 0.4f) + Vector3.up * (hh * 0.55f);
                var tip = basePt + dir * (lean * hh) + Vector3.up * hh;
                var side = new Vector3(-dir.z, 0f, dir.x);
                mb.Color = Paint.Shade(green, 0.85f + 0.25f * R());
                mb.Blade(basePt, mid, 0.1f, side);
                mb.Color = Color.Lerp(green, tipC, 0.5f + 0.4f * R());
                mb.Blade(mid, tip, 0.075f, side);
            }
            if (cattails)
            {
                int k = 2 + rng.Next(3);
                for (int i = 0; i < k; i++)
                {
                    float a = R() * Mathf.PI * 2f;
                    var basePt = b + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.06f;
                    float hh = height * (0.85f + 0.3f * R());
                    var top = basePt + new Vector3(Mathf.Cos(a) * 0.08f, hh, Mathf.Sin(a) * 0.08f);
                    mb.Color = new Color(0.5f, 0.56f, 0.34f);
                    mb.Segment(basePt, top, 0.012f, 0.01f, 4, false, false);
                    mb.Color = Paint.Shade(new Color(0.46f, 0.3f, 0.2f), 0.9f + 0.15f * R());
                    mb.Capsule(top - new Vector3(0f, 0.3f, 0f), 0.06f, 0.26f, 6, true);
                }
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        internal static void MossMound(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f;
            var moss = new Color(0.36f, 0.46f, 0.26f);
            mb.Color = Paint.Shade(moss, 0.88f + 0.18f * R());
            mb.Blob(b + new Vector3(0f, size * 0.12f, 0f), new Vector3(size, size * 0.38f, size * (0.75f + 0.3f * R())), 1, 0.15f, rng.Next(1000), 0.6f);
            mb.Color = Paint.Shade(new Color(0.46f, 0.56f, 0.3f), 0.95f + 0.1f * R());
            mb.Blob(b + new Vector3((R() - 0.5f) * size * 0.3f, size * 0.32f, (R() - 0.5f) * size * 0.3f), new Vector3(size * 0.55f, size * 0.24f, size * 0.5f), 0, 0.15f, rng.Next(1000), 0.5f);
        }

        internal static void SnowStone(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f;
            var grey = Color.Lerp(new Color(0.5f, 0.53f, 0.58f), new Color(0.58f, 0.58f, 0.6f), R());
            mb.Color = Paint.Shade(grey, 0.9f + 0.2f * R());
            var c = b + new Vector3(0f, size * 0.3f, 0f);
            var rad = new Vector3(size, size * 0.65f, size * (0.7f + 0.3f * R()));
            mb.Blob(c, rad, 0, 0.22f, rng.Next(1000), 0.55f);
            // a cap of snow on top
            mb.Color = Paint.Shade(new Color(0.94f, 0.96f, 1f), 0.96f + 0.06f * R());
            mb.Blob(c + new Vector3(0f, rad.y * 0.55f, 0f), new Vector3(rad.x * 0.85f, rad.y * 0.4f, rad.z * 0.85f), 0, 0.12f, rng.Next(1000), 0.7f);
        }

        /// <summary>A low, wind-combed drift: long across the wind, barely rising, the snow's own colour.</summary>
        internal static void SnowMound(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f;
            mb.Color = Paint.Shade(new Color(0.78f, 0.81f, 0.87f), 0.97f + 0.04f * R());
            mb.Push().Translate(b + new Vector3(0f, -0.03f, 0f)).Rotate(0f, (R() - 0.5f) * 40f, 0f);
            mb.Blob(Vector3.zero, new Vector3(size * 1.3f, size * 0.11f, size * (0.4f + 0.2f * R())), 2, 0.04f, rng.Next(1000), 0.8f);
            mb.Pop();
        }

        internal static void SnowyBush(MeshBuilder mb, System.Random rng, Vector3 b, float size, Color col)
        {
            Bush(mb, rng, b, size, col);
            float R() => (float)rng.NextDouble();
            mb.Color = Paint.Shade(new Color(0.94f, 0.96f, 1f), 0.97f + 0.05f * R());
            mb.Blob(b + new Vector3(0f, size * 1.05f, 0f), new Vector3(size * 0.62f, size * 0.22f, size * 0.55f), 0, 0.12f, rng.Next(1000), 0.6f);
        }

        internal static void AshMound(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f;
            mb.Color = Paint.Shade(new Color(0.55f, 0.53f, 0.52f), 0.88f + 0.16f * R());
            mb.Blob(b + new Vector3(0f, 0.02f, 0f), new Vector3(size, size * 0.22f, size * (0.6f + 0.3f * R())), 1, 0.12f, rng.Next(1000), 0.75f);
        }

        internal static void CharredSticks(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f;
            int n = 2 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float a = R() * Mathf.PI;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * size * (0.5f + 0.5f * R());
                var c = b + new Vector3((R() - 0.5f) * size * 0.5f, 0.04f + 0.03f * i, (R() - 0.5f) * size * 0.4f);
                mb.Color = Paint.Shade(new Color(0.18f, 0.15f, 0.14f), 0.9f + 0.3f * R());
                mb.Segment(c - d * 0.5f, c + d * 0.5f, 0.045f, 0.03f, 5, false, true);
            }
        }

        internal static void Bones(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f;
            var bone = new Color(0.88f, 0.84f, 0.74f);
            int n = 1 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float a = R() * Mathf.PI;
                float len = size * (0.35f + 0.25f * R());
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * len;
                var c = b + new Vector3((R() - 0.5f) * size * 0.4f, 0.04f, (R() - 0.5f) * size * 0.3f);
                mb.Color = Paint.Shade(bone, 0.88f + 0.14f * R());
                mb.Segment(c - d * 0.5f, c + d * 0.5f, 0.03f, 0.03f, 5, false, false);
                mb.Blob(c - d * 0.5f, new Vector3(0.05f, 0.04f, 0.05f), 0, 0.1f, rng.Next(1000));
                mb.Blob(c + d * 0.5f, new Vector3(0.05f, 0.04f, 0.05f), 0, 0.1f, rng.Next(1000));
            }
            if (R() < 0.25f)
            {
                // a little skull
                mb.Color = Paint.Shade(bone, 0.95f);
                mb.Blob(b + new Vector3(0f, 0.09f, 0f), new Vector3(0.1f, 0.09f, 0.12f), 1, 0.06f, rng.Next(1000), 0.3f);
            }
        }

        internal static void Pebbles(MeshBuilder mb, System.Random rng, Vector3 b, Color col)
        {
            float R() => (float)rng.NextDouble();
            int n = 3 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                var p = b + new Vector3((R() - 0.5f) * 0.7f, 0f, (R() - 0.5f) * 0.5f);
                Rubble(mb, rng, p, Mathf.Lerp(0.05f, 0.11f, R()), col);
            }
        }

        /// <summary>A broken flagstone, tipped up a little.</summary>
        internal static void FlagShard(MeshBuilder mb, System.Random rng, Vector3 b, Color col)
        {
            float R() => (float)rng.NextDouble();
            mb.Color = Paint.Shade(col, 0.9f + 0.15f * R());
            mb.Push().Translate(b + new Vector3(0f, 0.04f, 0f)).Rotate((R() - 0.5f) * 16f, R() * 180f, (R() - 0.5f) * 16f);
            mb.Box(Vector3.zero, new Vector3(Mathf.Lerp(0.35f, 0.6f, R()), 0.07f, Mathf.Lerp(0.25f, 0.45f, R())));
            mb.Pop();
        }

        /// <summary>A root arching out of the ground and back into it.</summary>
        internal static void RootArc(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            float a = R() * Mathf.PI;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var bark = new Color(0.32f, 0.25f, 0.28f);
            mb.Color = Paint.Shade(bark, 0.9f + 0.2f * R());
            float r = 0.06f + 0.05f * R();
            var p0 = b - d * size * 0.5f + Vector3.down * 0.05f;
            var p1 = b - d * size * 0.2f + Vector3.up * size * 0.22f;
            var p2 = b + d * size * 0.2f + Vector3.up * size * 0.2f;
            var p3 = b + d * size * 0.5f + Vector3.down * 0.05f;
            mb.Segment(p0, p1, r * 1.2f, r, 5, false, false);
            mb.Segment(p1, p2, r, r * 0.9f, 5, false, false);
            mb.Segment(p2, p3, r * 0.9f, r * 0.7f, 5, false, false);
        }

        /// <summary>A pine with snow lying on each tier.</summary>
        internal static void SnowyPine(MeshBuilder mb, System.Random rng, Vector3 b, float height, Color leaf)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f; mb.WindGradient = false;
            mb.Color = Ui.Hex("#5e4636");
            mb.Cylinder(b, height * 0.04f, height * 0.025f, height * 0.3f, 6);
            mb.Wind = 0.15f; mb.WindGradient = true; mb.WindY0 = b.y + height * 0.2f; mb.WindY1 = b.y + height;
            float r = height * 0.24f;
            var snow = new Color(0.93f, 0.96f, 1f);
            for (int i = 0; i < 3; i++)
            {
                float y0 = height * (0.2f + i * 0.22f);
                float rr = r * (1f - i * 0.24f);
                float th = height * (0.42f - i * 0.04f);
                mb.Color = Paint.Shade(leaf, 0.9f + 0.08f * i + 0.08f * R());
                mb.Cylinder(b + new Vector3(0f, y0, 0f), rr, 0f, th, 7, false, true, false);
                // the snow on this tier: a lighter, smaller cone over its upper part
                mb.Color = Paint.Shade(snow, 0.95f + 0.05f * R());
                mb.Cylinder(b + new Vector3(0f, y0 + th * 0.38f, 0f), rr * 0.66f, 0f, th * 0.64f, 7, false, true, false);
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        /// <summary>A weeping willow: a soft crown with long strands hanging down around it.</summary>
        internal static void Willow(MeshBuilder mb, System.Random rng, Vector3 b, float height, Color leaf)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f; mb.WindGradient = false;
            mb.Color = Ui.Hex("#5a4a3c");
            float trunkH = height * 0.48f;
            mb.Cylinder(b, height * 0.06f, height * 0.035f, trunkH + 0.3f, 6);
            float cr = height * 0.34f;
            var top = b + new Vector3(0f, trunkH + cr * 0.55f, 0f);
            mb.Wind = 0.3f; mb.WindGradient = true; mb.WindY0 = b.y + trunkH * 0.4f; mb.WindY1 = b.y + height;
            mb.Color = Paint.Shade(leaf, 0.95f + 0.1f * R());
            mb.Blob(top, new Vector3(cr, cr * 0.7f, cr), 1, 0.14f, rng.Next(1000), 0.3f);
            int n = 14 + rng.Next(6);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + (R() - 0.5f) * 0.3f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var s0 = top + dir * (cr * 0.85f) + Vector3.up * (cr * 0.05f);
                var s1 = s0 + dir * 0.15f + Vector3.down * (height * Mathf.Lerp(0.38f, 0.55f, R()));
                mb.Color = Paint.Shade(leaf, 0.82f + 0.16f * R());
                mb.Blade(s0, s1, 0.3f, new Vector3(-dir.z, 0f, dir.x));
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        /// <summary>A dead snag: a grey, broken trunk with a couple of bare limbs.</summary>
        internal static void DeadSnag(MeshBuilder mb, System.Random rng, Vector3 b, float height)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f; mb.WindGradient = false;
            var wood = new Color(0.46f, 0.42f, 0.38f);
            mb.Color = Paint.Shade(wood, 0.9f + 0.15f * R());
            var top = b + new Vector3((R() - 0.5f) * 0.4f, height, (R() - 0.5f) * 0.4f);
            mb.Segment(b, top, height * 0.06f, height * 0.025f, 6, false, true);
            int n = 2 + rng.Next(2);
            for (int i = 0; i < n; i++)
            {
                float t = Mathf.Lerp(0.45f, 0.8f, R());
                var p = Vector3.Lerp(b, top, t);
                float a = R() * Mathf.PI * 2f;
                var tip = p + new Vector3(Mathf.Cos(a) * height * 0.3f, height * Mathf.Lerp(0.12f, 0.3f, R()), Mathf.Sin(a) * height * 0.3f);
                mb.Color = Paint.Shade(wood, 0.85f + 0.15f * R());
                mb.Segment(p, tip, height * 0.025f, height * 0.008f, 5, false, false);
            }
        }
    }
}
