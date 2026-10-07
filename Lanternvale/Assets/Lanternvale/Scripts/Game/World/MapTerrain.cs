// The land of a map (World / MapView): one heightfield, flat at z = 0 over the walkable rect plus a margin, rolling into
// hills behind (y > depth), gentle banks and meadows in front (y < 0) and continuing far to the left and right so a
// yawed camera never sees the world's edge. Rendered as a few chunked, faceted (low-poly) meshes with the map's ground
// texture blending softly into the surrounding meadow (Lanternvale/Terrain), vertex-colour variation (meadow swathes,
// worn paths, shade under trees and buildings, sunny crests, field patches on far hills), a detail layer painted over
// the ground by a per-vertex weight (raked gravel around the shrine's paving, leaf litter under the forest's trees),
// ground cover (grass tufts, flowers, stones, outlined bushes, sparse trees) outside the walkable area, and, when the
// map has a bridge, a brook under it (or, with MapDef.water, its rivers, ponds, fords and bridges: MapTerrain.Water.cs).
//
// The look comes from the map's biome (Biomes.cs): ground and surroundings textures and tints, relief (hills, rolling
// downs, a flat fen with pools, mountains, summit crags, or the walls of an indoor map), detail layer, ground cover and
// hill trees. MapDef.paths are painted trails in any direction (MapTerrain.Paths.cs); MapDef.fill dresses the inside
// of the map with the biome's ground cover (MapTerrain.Cover.cs); indoor maps (caves, crypts, the Hollow Heart) rise
// into rock or masonry walls at the back and sides and fall into darkness at the front (MapTerrain.Indoor.cs).
//
// Paved maps (the shrine): the flagstones are only a processional walkway along the trail and forecourts around the
// gate, lanterns and statues; pale raked gravel borders them and moss / short grass covers the rest.
//
// Heights are metres above the ground plane (world z = −height). Inside the walkable rect the height is exactly 0.
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    internal sealed partial class MapTerrain
    {
        // flat margins around the walkable rect (props and building backs sit there)
        public const float FlatSide = 4f, FlatBack = 4.5f, FlatFront = 3f;

        readonly MapDef def;
        readonly float W, D;
        readonly float s1, s2, s3, s4, s5, s6;
        readonly List<Mesh> owned;
        readonly Transform root;

        /// <summary>A soft shade on the ground; dapple: broken by sun flecks (a great canopy far overhead).</summary>
        struct Blot { public Vector2 p; public float r; public Color tint; public float k; public bool dapple; }
        readonly List<Blot> blots = new List<Blot>();
        readonly List<Vector2> corridors = new List<Vector2>();   // x: −1 left / +1 right, y: centre
        // exits on the back (y ≈ D) and front (y ≈ 0) edges: the x centres of their roads through the hills
        readonly List<float> backExits = new List<float>(), frontExits = new List<float>();

        // worn ground (village): doorsteps and the plaza's furniture (ellipses), little lanes from doors to the paths
        struct Wear { public Vector2 p; public float rx, ry, gather; }   // rx = 0: no spot of its own
        readonly List<Wear> wear = new List<Wear>();
        struct Lane { public Vector2 a, b; public float half; }
        readonly List<Lane> lanes = new List<Lane>();

        // style
        BiomeStyle style;
        /// <summary>Indoors: walls instead of hills (MapTerrain.Indoor.cs).</summary>
        bool walled;
        Color innerTint = Color.white, sideTint = Color.white, meadowTint = Color.white, pathTint = new Color(1.08f, 0.97f, 0.86f);
        float blendStart = 1.5f, blendEnd = 7f;
        bool fields, forestBehind, cliffsBehind;
        /// <summary>The map's ground is the dirt-and-grass village texture: inside the map it becomes meadow, worn to dirt
        /// only along the paths, at doorsteps and around the plaza's furniture.</summary>
        bool meadowInside;
        /// <summary>The map's ground is paving (the shrine's flagstones): inside the map it is laid only on a
        /// processional walkway and forecourts, bordered by raked gravel (the detail layer), moss elsewhere.</summary>
        bool pavedInside;
        bool forest;
        Color mossTint = Color.white, gravelTint = Color.white;
        // shrine: the walkway's centreline, forecourts around the gate / lanterns / statues, lanes joining them to it
        readonly List<List<Vector2>> walks = new List<List<Vector2>>();
        const float WalkHalf = 2.4f;
        struct Pad { public Vector2 p; public float rx, ry; }
        readonly List<Pad> pads = new List<Pad>();
        readonly List<Lane> padLanes = new List<Lane>();
        // forest: leaf litter gathers under the trees (detail layer)
        struct Litter { public Vector2 p; public float r, k; }
        readonly List<Litter> litter = new List<Litter>();

        // brook (maps with a bridge)
        public bool HasStream { get; private set; }
        float streamX, streamY;
        Material waterMat;
        float waterScroll;

        public Material Material { get; private set; }
        public Color GroundAverage { get; private set; }

        /// <summary>
        /// The terrain's detail layer for a map (Lanternvale/Terrain _DetailTex, world-planar at planarScale), painted
        /// over the ground by MapTerrain's per-vertex weight: raked gravel around the shrine's paving, leaf litter under
        /// the forest's trees; null when the map has none.
        /// </summary>
        public static Texture2D DetailTexture(MapDef def, out float planarScale)
        {
            var st = StyleOf(def);
            planarScale = 0.25f;
            Texture2D tex;
            switch (st.Detail)
            {
                case BiomeDetail.Gravel: tex = WorldTextures.Gravel; break;
                case BiomeDetail.LeafLitter: tex = WorldTextures.LeafLitter; break;
                case BiomeDetail.Scree: tex = WorldTextures.Scree; break;
                case BiomeDetail.Puddles: tex = WorldTextures.Puddles; break;
                case BiomeDetail.SnowDust: tex = WorldTextures.SnowDust; break;
                case BiomeDetail.Roots: tex = WorldTextures.Roots; break;
                case BiomeDetail.Ash: tex = WorldTextures.Ash; break;
                default: return null;
            }
            planarScale = 1f / st.DetailTile;
            return tex;
        }

        /// <summary>The map's biome style; an indoor environment on an outdoor biome gets the cave (or crypt) style.</summary>
        internal static BiomeStyle StyleOf(MapDef def)
        {
            var st = Biomes.For(def);
            if (!st.Indoor && Biomes.IsIndoor(def)) st = Biomes.ById(def.environment == "crypt" ? Biomes.Crypt : Biomes.Cave);
            return st;
        }

        public MapTerrain(MapDef def, Transform parent, List<Mesh> owned)
        {
            this.def = def;
            this.owned = owned;
            W = Mathf.Max(1f, def.width);
            D = Mathf.Max(1f, def.depth);
            var rng = new System.Random(StableHash(def.id) ^ 0x5bd1e995);
            s1 = (float)rng.NextDouble() * 200f; s2 = (float)rng.NextDouble() * 200f;
            s3 = (float)rng.NextDouble() * 200f; s4 = (float)rng.NextDouble() * 200f;
            s5 = (float)rng.NextDouble() * 200f; s6 = (float)rng.NextDouble() * 200f;
            root = new GameObject("Terrain").transform;
            root.SetParent(parent, false);
            Analyse();
        }

        public static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                if (s != null) foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ================================================================== map analysis

        void Analyse()
        {
            style = StyleOf(def);
            walled = style.Indoor;
            string tintHex = Biomes.GroundTint(def);
            var gt = string.IsNullOrEmpty(tintHex) ? Color.white : Ui.Hex(tintHex);
            // the lights are bright on up-facing ground: a little under white keeps the painted ground as painted
            innerTint = gt * style.Inner;
            // forest: a sun-dappled wood, the painted moss a little brighter, the surroundings a fresh, lighter green;
            // shrine: paving with moss and short grass (the meadow texture, deepened) and pale raked gravel (detail)
            forest = style.Forest;
            pavedInside = style.Paved;
            mossTint = style.MossTint;
            gravelTint = style.GravelTint;
            sideTint = style.SideTint;
            blendStart = style.BlendStart; blendEnd = style.BlendEnd;
            // village: a meadow, a touch under the surroundings' exposure (lit from straight above), worn where feet go
            meadowInside = style.MeadowInside;
            meadowTint = style.MeadowTint;
            fields = style.Fields;
            foreach (var l in def.layers)
            {
                if (l == null || string.IsNullOrEmpty(l.art)) continue;
                if (l.art.Contains("hills") || l.art.Contains("village")) fields = true;
                if (l.art.Contains("forest_near")) forestBehind = true;
                if (l.art.Contains("cliff")) cliffsBehind = true;
            }

            foreach (var p in def.props)
            {
                if (p == null || string.IsNullOrEmpty(p.art)) continue;
                var pos = new Vector2(p.pos.x, p.pos.y);
                float sc = p.scale > 0f ? p.scale : 1f;
                string a = p.art;
                if (a.StartsWith("decal_")) continue;
                // the great tree's crown is far above the camera's view: its dappled shade on the plaza tells of it
                if (a.Contains("tree_great")) blots.Add(new Blot { p = pos + new Vector2(0f, 1f), r = 11f * sc, tint = new Color(0.62f, 0.72f, 0.80f), k = 0.85f, dapple = true });
                else if (a.Contains("tree_dead")) blots.Add(new Blot { p = pos, r = 2f * sc, tint = new Color(0.8f, 0.8f, 0.84f), k = 0.5f });
                // under a forest's canopies the shade stays soft (the floor is already a deep green)
                else if (a.Contains("tree")) blots.Add(new Blot { p = pos + new Vector2(0f, 0.3f), r = 3.6f * sc, tint = new Color(0.68f, 0.76f, 0.78f), k = forest ? 0.45f : 0.7f });
                else if (a.Contains("bush")) blots.Add(new Blot { p = pos, r = 1.7f * sc, tint = new Color(0.78f, 0.84f, 0.84f), k = 0.55f });
                else if (p.collider != null && p.collider.w >= 2.5f)   // buildings, tents, carts: a little contact darkening
                    blots.Add(new Blot { p = pos + new Vector2(0f, p.collider.h * 0.3f), r = p.collider.w * 0.62f * sc, tint = new Color(0.82f, 0.8f, 0.84f), k = 0.5f });
                if (forest && a.Contains("tree"))
                    litter.Add(new Litter { p = pos + new Vector2(0f, 0.3f), r = (a.Contains("tree_dead") ? 2.2f : 3.4f) * sc, k = a.Contains("tree_dead") ? 0.55f : 1f });
                else if (forest && (a.Contains("stump") || a.Contains("log") || a.Contains("rock_large")))
                    litter.Add(new Litter { p = pos, r = 1.6f * sc, k = 0.6f });
                if (a.Contains("bridge") && !HasStream && def.water.Count == 0)
                {
                    HasStream = true;
                    streamX = pos.x;
                    streamY = pos.y;
                }
            }
            foreach (var t in def.transitions)
            {
                // a hidden entrance gets no road (it would give the secret away)
                if (t == null || t.hidden) continue;
                if (t.pos.x <= 3.5f) corridors.Add(new Vector2(-1f, t.pos.y));
                else if (t.pos.x >= W - 3.5f) corridors.Add(new Vector2(1f, t.pos.y));
                else if (t.pos.y >= D - 3.5f) backExits.Add(t.pos.x);
                else if (t.pos.y <= 3.5f) frontExits.Add(t.pos.x);
            }
            AnalysePaths();
            AnalyseDataPaths();
            AnalyseWater();
            if (meadowInside) AnalyseWear();
            if (pavedInside) AnalyseShrine();
        }

        /// <summary>
        /// The shrine's paving: a processional walkway about 5 m wide along the painted trail (through its decals'
        /// centres, without the painted wave) and forecourts around the gate, the lanterns, statues and the ruined arch,
        /// each joined to the walkway by a short paved lane.
        /// </summary>
        void AnalyseShrine()
        {
            PathChain main = null;
            foreach (var ch in chains) if (!ch.data && (main == null || ch.x1 - ch.x0 > main.x1 - main.x0)) main = ch;
            var walk = new List<Vector2>();
            if (main != null)
            {
                var d = main.decals;
                walk.Add(new Vector2(main.x0, d[0].pos.y));
                for (int i = 0; i < d.Count; i++) walk.Add(new Vector2(d[i].pos.x, d[i].pos.y));
                walk.Add(new Vector2(main.x1, d[d.Count - 1].pos.y));
                walks.Add(walk);
            }
            // MapDef.paths are walkways too (a terraced climb in any direction), along their smoothed centrelines
            foreach (var ch in chains)
            {
                if (!ch.data) continue;
                var w = new List<Vector2>();
                for (int i = 0; i < ch.pts.Count; i += 4) w.Add(ch.pts[i]);
                w.Add(ch.pts[ch.pts.Count - 1]);
                walks.Add(w);
            }
            if (walks.Count == 0)
            {
                walk.Add(new Vector2(-3f, D * 0.45f));
                walk.Add(new Vector2(W + 3f, D * 0.45f));
                walks.Add(walk);
            }
            foreach (var p in def.props)
            {
                if (p == null || string.IsNullOrEmpty(p.art) || p.art.StartsWith("decal_")) continue;
                string a = p.art;
                var pos = new Vector2(p.pos.x, p.pos.y);
                float sc = p.scale > 0f ? p.scale : 1f;
                float wd = WalkDistance(pos.x, pos.y, out var onWalk);
                if (a.Contains("shrine_gate"))
                {
                    // the gate's forecourt reaches from the walkway up to the gate
                    var mid = (pos + onWalk) * 0.5f;
                    pads.Add(new Pad { p = mid + new Vector2(0f, 0.3f), rx = 3.1f * sc, ry = Mathf.Abs(pos.y - onWalk.y) * 0.5f + 1.4f });
                    continue;
                }
                float r = a.Contains("spirit_lantern") ? 1.3f : a.Contains("spirit_statue") ? 1.05f : a.Contains("ruin_arch") ? 2.1f : 0f;
                if (r <= 0f) continue;
                pads.Add(new Pad { p = pos + new Vector2(0f, -0.15f), rx = r * sc, ry = r * sc * 0.85f });
                if (wd - WalkHalf < 7f && wd > WalkHalf) padLanes.Add(new Lane { a = pos, b = onWalk, half = Mathf.Min(1.05f, 0.75f * sc) });
            }
        }

        /// <summary>Distance (m) from a ground point to the shrine walkway's centreline, and the nearest point on it.</summary>
        float WalkDistance(float x, float y, out Vector2 nearest)
        {
            float best = float.MaxValue;
            nearest = new Vector2(x, y);
            var q = new Vector2(x, y);
            for (int k = 0; k < walks.Count; k++)
            {
                var walk = walks[k];
                for (int i = 0; i + 1 < walk.Count; i++)
                {
                    var a = walk[i];
                    var ab = walk[i + 1] - a;
                    float l2 = ab.sqrMagnitude;
                    float t = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / l2) : 0f;
                    var c = a + ab * t;
                    float d = Vector2.Distance(q, c);
                    if (d < best) { best = d; nearest = c; }
                }
            }
            return best;
        }

        /// <summary>
        /// The shrine's ground at a point: paved (0..1: walkway, forecourts, lanes) and gravel (0..1: the raked border
        /// beside the paving; moss beyond it and under the trees).
        /// </summary>
        void ShrineGround(float x, float y, float ragged, out float paved, out float gravel)
        {
            // metres outside the nearest paved shape (negative inside it)
            float dist = WalkDistance(x, y, out _) - WalkHalf;
            for (int i = 0; i < pads.Count; i++)
            {
                var pd = pads[i];
                float ex = (x - pd.p.x) / pd.rx, ey = (y - pd.p.y) / pd.ry;
                float e = Mathf.Sqrt(ex * ex + ey * ey);
                dist = Mathf.Min(dist, (e - 1f) * Mathf.Min(pd.rx, pd.ry));
            }
            for (int i = 0; i < padLanes.Count; i++)
            {
                var ln = padLanes[i];
                dist = Mathf.Min(dist, SegmentDistance(new Vector2(x, y), ln.a, ln.b) - ln.half);
            }
            float jag = Mathf.PerlinNoise(x * 0.9f + s1, y * 0.9f + s5) - 0.5f;
            paved = 1f - Smooth(-0.22f, 0.18f, dist + ragged * 0.3f + jag * 0.18f);
            float wide = Mathf.PerlinNoise(x * 0.19f + s6, y * 0.19f + s2) - 0.5f;
            gravel = 1f - Smooth(1.3f, 2.9f, dist + wide * 1.8f + ragged * 0.5f);
            // moss creeps over the gravel under the trees and around the old stones
            for (int i = 0; i < blots.Count; i++)
            {
                var b = blots[i];
                float dx = x - b.p.x, dy = (y - b.p.y) * 1.25f;
                float d2 = (dx * dx + dy * dy) / (b.r * b.r * 0.8f);
                if (d2 < 1f) gravel *= Mathf.Lerp(1f, 0.25f, (1f - d2) * (1f - d2));
            }
        }

        /// <summary>
        /// Doorsteps of the buildings and a lane from each door to the nearest path; worn ground around the village's
        /// furniture, merging into a trodden plaza where it gathers (benches, lanterns, the well, the great tree).
        /// </summary>
        void AnalyseWear()
        {
            foreach (var p in def.props)
            {
                if (p == null || string.IsNullOrEmpty(p.art) || p.art.StartsWith("decal_")) continue;
                string a = p.art;
                var pos = new Vector2(p.pos.x, p.pos.y);
                float sc = p.scale > 0f ? p.scale : 1f;
                float cw = p.collider != null ? p.collider.w : 0f, chh = p.collider != null ? p.collider.h : 0f;
                bool building = a.Contains("cottage") || a.Contains("inn") || a.Contains("smithy") || a.Contains("windmill") || a.Contains("tent");
                if (building)
                {
                    // the front face sits on the collider's front edge (Docs/ThreeD.md §7); the door is in front of it
                    var door = new Vector2(pos.x, pos.y - Mathf.Max(0.6f, chh * 0.5f * sc) - 0.55f);
                    wear.Add(new Wear { p = door, rx = Mathf.Clamp(cw * sc * 0.36f, 1.2f, 3f), ry = 1.25f });
                    if (NearestPath(door.x, door.y, out var onPath, out float half) < 9f)
                    {
                        // a footpath from the doorstep to the trail's edge (the ragged noise keeps it from looking ruled)
                        var dir = onPath - door;
                        float len = dir.magnitude;
                        if (len > half + 0.5f) lanes.Add(new Lane { a = door, b = door + dir * ((len - half * 0.6f) / len), half = 0.62f });
                    }
                    continue;
                }
                if (a.Contains("tree_great"))
                {
                    // villagers gather under the great tree: no spot of its own, but it pulls the plaza together
                    wear.Add(new Wear { p = pos + new Vector2(0f, -2.8f * sc), gather = 1.5f });
                    continue;
                }
                float r = a.Contains("well") ? 2.3f : a.Contains("shop_stall") ? 2.2f : a.Contains("cart") ? 1.9f
                        : a.Contains("noticeboard") ? 1.7f : a.Contains("bench") ? 1.35f : a.Contains("campfire") ? 1.9f
                        : a.Contains("spirit_lantern") ? 1.15f : a.Contains("barrel") || a.Contains("crate") ? 1.0f
                        : a.Contains("lamp_post") || a.Contains("signpost") || a.Contains("banner") ? 0.75f : 0f;
                if (r <= 0f) continue;
                float gather = a.Contains("well") || a.Contains("shop_stall") || a.Contains("campfire") ? 1.2f
                             : a.Contains("bench") || a.Contains("noticeboard") || a.Contains("spirit_lantern") ? 1f
                             : a.Contains("cart") ? 0.8f : a.Contains("barrel") || a.Contains("crate") ? 0.55f : 0.4f;
                wear.Add(new Wear { p = pos + new Vector2(0f, -0.25f), rx = r * sc, ry = r * sc * 0.82f, gather = gather });
            }
        }

        // ================================================================== heights

        /// <summary>Centre of the brook at depth y.</summary>
        public float StreamX(float y)
        {
            float t = y - streamY;
            return streamX + 1.4f * Mathf.Sin(t * 0.2f) + 3.2f * Mathf.Sin(t * 0.043f);
        }

        /// <summary>Half width of the brook at depth y.</summary>
        public float StreamHalfWidth(float y)
        {
            float o = Mathf.Max(0f, Mathf.Max(-y, y - D));
            return 1.25f + 0.7f * Smooth(0f, 12f, o) - 0.5f * Smooth(40f, 110f, y - D);
        }

        /// <summary>0 inside the walkable depth range … 1 a few metres outside it (where the brook bed may be carved).</summary>
        float StreamDepthFactor(float y) => Smooth(0f, 2.5f, Mathf.Max(0f, Mathf.Max(-y, y - D)));

        /// <summary>Height (m) of the land at a ground point (0 everywhere inside the walkable rect).</summary>
        public float Height(float x, float y)
        {
            float h = BaseHeight(x, y);
            if (waters.Count > 0) h -= WaterCarve(x, y);
            if (HasStream)
            {
                float f = StreamDepthFactor(y);
                if (f > 0f)
                {
                    float u = Mathf.Abs(x - StreamX(y)) / StreamHalfWidth(y);
                    if (u < 1.6f) h -= 0.6f * f * (1f - Smooth(0.55f, 1.5f, u));
                }
            }
            return h;
        }

        /// <summary>Height without the brook's bed.</summary>
        public float BaseHeight(float x, float y)
        {
            if (walled) return IndoorHeight(x, y);
            float ox = Mathf.Max(0f, Mathf.Max(-FlatSide - x, x - (W + FlatSide)));
            float ob = Mathf.Max(0f, y - (D + FlatBack));
            float of = Mathf.Max(0f, -FlatFront - y);
            if (ox <= 0f && ob <= 0f && of <= 0f) return 0f;

            float n1 = Mathf.PerlinNoise(x * 0.045f + s1, y * 0.045f + s2);
            float n2 = Mathf.PerlinNoise(x * 0.012f + s3, y * 0.012f + s4);
            float n3 = Mathf.PerlinNoise(x * 0.11f + s5, y * 0.11f + s6);
            float valley = 0f;
            if (HasStream)
            {
                float hw = StreamHalfWidth(y);
                valley = 1f - Smooth(hw + 1f, hw + 8f + ob * 0.25f, Mathf.Abs(x - StreamX(y)));
            }
            if (waters.Count > 0) valley = Mathf.Max(valley, WaterValley(x, y, Mathf.Max(ox, Mathf.Max(ob, of))));
            float hills = style.HillScale;

            float h = 0f;
            if (ob > 0f)
            {
                float e = Smooth(0f, 12f, ob);
                float a = 15f * (1f - Mathf.Exp(-ob / 26f)) + 26f * Smooth(45f, 150f, ob) * (0.4f + n2);
                if (cliffsBehind) a *= 0.7f;
                a *= hills;
                float back = e * a * (0.5f + 0.95f * n1) * (1f - 0.8f * valley);
                // roads through the hills at the back edge's exits
                for (int i = 0; i < backExits.Count; i++)
                    back *= 1f - 0.85f * (1f - Smooth(2.5f, 8f, Mathf.Abs(x - backExits[i]))) * (1f - Smooth(70f, 120f, ob));
                h += back;
            }
            if (ox > 0f)
            {
                float e = Smooth(0f, 16f, ox);
                float a = 1.0f + 7f * Smooth(12f, 70f, ox) + 14f * Smooth(60f, 140f, ox) * (0.4f + n2);
                a *= hills;
                float side = e * a * (0.45f + 1.0f * n1);
                for (int i = 0; i < corridors.Count; i++)
                {
                    var c = corridors[i];
                    if ((c.x < 0f && x < 0f) || (c.x > 0f && x > W))
                        side *= 1f - 0.85f * (1f - Smooth(2.5f, 8f, Mathf.Abs(y - c.y))) * (1f - Smooth(70f, 120f, ox));
                }
                h += side;
            }
            if (of > 0f)
            {
                float bank = -0.55f * Smooth(0f, 7f, of);
                float rolls = (n1 - 0.5f) * 1.4f * Smooth(5f, 16f, of);
                float rise = 5f * Smooth(16f, 48f, of) * (0.5f + n2);
                if (style.Relief == BiomeRelief.Crags)
                {
                    // a summit: the land falls away in front, over a rocky lip
                    rise = -14f * Smooth(8f, 40f, of) * (0.6f + 0.6f * n2);
                    bank = -1.2f * Smooth(1f, 8f, of);
                }
                float front = (bank + rolls + rise * hills) * (1f - 0.6f * valley);
                for (int i = 0; i < frontExits.Count; i++)
                    front *= 1f - 0.85f * (1f - Smooth(2.5f, 8f, Mathf.Abs(x - frontExits[i])));
                h += front;
            }
            float o = Mathf.Max(ox, Mathf.Max(ob, of));
            h += (n3 - 0.5f) * 0.6f * Smooth(0f, 8f, o);
            if (style.Relief != BiomeRelief.Hills) h += ReliefDetail(x, y, ox, ob, of, o, valley);
            return h;
        }

        /// <summary>
        /// The biome's own touch on the land around the map: rolling downs smoothed out, the fen's hummocks and pools
        /// (around a water table just under the ground), the peaks' sharp ridges, the summit's broken crags.
        /// </summary>
        float ReliefDetail(float x, float y, float ox, float ob, float of, float o, float valley)
        {
            switch (style.Relief)
            {
                case BiomeRelief.Flat:
                {
                    // hummocks and hollows: where the land dips under the water table (FenWaterLevel) a pool shows
                    float p = Mathf.PerlinNoise(x * 0.085f + s4, y * 0.085f + s2) * 0.7f + Mathf.PerlinNoise(x * 0.23f + s6, y * 0.23f + s1) * 0.3f;
                    return (p - 0.5f) * 1.1f * Smooth(1.2f, 7f, o) * (1f - 0.7f * valley);
                }
                case BiomeRelief.Rolling:
                {
                    // long soft swells
                    float sw = Mathf.PerlinNoise(x * 0.02f + s2, y * 0.02f + s5);
                    return (sw - 0.4f) * 5f * Smooth(6f, 40f, o) * (1f - 0.8f * valley);
                }
                case BiomeRelief.Mountains:
                {
                    // ridges climbing to snowy peaks behind and at the sides
                    float r1 = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x * 0.026f + s3, y * 0.026f + s1) - 1f);
                    float r2 = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x * 0.071f + s5, y * 0.071f + s4) - 1f);
                    float ridge = r1 * r1 * 0.75f + r2 * r2 * 0.25f;
                    float far = Mathf.Max(ob, ox * 0.8f);
                    return ridge * (6f * Smooth(4f, 30f, far) + 30f * Smooth(30f, 140f, far)) * (1f - 0.85f * valley);
                }
                case BiomeRelief.Crags:
                {
                    // broken rock: sharp creases and knuckles
                    float r1 = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x * 0.06f + s2, y * 0.06f + s6) - 1f);
                    float r2 = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(x * 0.17f + s4, y * 0.17f + s3) - 1f);
                    float crag = r1 * r1 * r1 * 0.7f + r2 * r2 * 0.3f;
                    float far = Mathf.Max(ob, ox);
                    return crag * (4f * Smooth(1f, 14f, far) + 16f * Smooth(14f, 90f, far)) * (1f - 0.8f * valley);
                }
            }
            return 0f;
        }

        /// <summary>Distance (m) of a ground point outside the walkable rect (0 inside).</summary>
        public float DistanceOutside(float x, float y)
        {
            float dx = Mathf.Max(0f, Mathf.Max(-x, x - W));
            float dy = Mathf.Max(0f, Mathf.Max(-y, y - D));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>True when nothing (ground cover, backdrop trees) should stand here: brook, transition roads, trails.</summary>
        public bool Reserved(float x, float y, float margin)
        {
            if (HasStream && Mathf.Abs(x - StreamX(y)) < StreamHalfWidth(y) + 0.6f + margin) return true;
            if (chains.Count > 0 && PathDistance(x, y, out float half) < half + 0.35f + margin) return true;
            for (int i = 0; i < corridors.Count; i++)
            {
                var c = corridors[i];
                bool side = c.x < 0f ? x < 1f : x > W - 1f;
                if (side && Mathf.Abs(y - c.y) < 3.2f + margin) return true;
            }
            for (int i = 0; i < backExits.Count; i++)
                if (y > D - 1f && Mathf.Abs(x - backExits[i]) < 3.2f + margin) return true;
            for (int i = 0; i < frontExits.Count; i++)
                if (y < 1f && Mathf.Abs(x - frontExits[i]) < 3.2f + margin) return true;
            if (waters.Count > 0 && WaterEdge(x, y) < 0.6f + margin) return true;
            return false;
        }

        // ================================================================== colours

        void Sample(float x, float y, float h, out Color tint, out Vector2 blendDetail, out float detail)
        {
            detail = 0f;
            float dOut = DistanceOutside(x, y);
            float edge = Mathf.PerlinNoise(x * 0.15f + s5, y * 0.15f + s6);
            float blend = Smooth(blendStart, blendEnd, dOut + (edge - 0.5f) * 3.2f);
            float blendOut = blend;

            // map ground: soft swathes, warm/cool variation
            float v = Mathf.PerlinNoise(x * 0.06f + s1, y * 0.06f + s2);
            float w = Mathf.PerlinNoise(x * 0.028f + s3, y * 0.028f + s4);
            var inner = innerTint * (0.9f + 0.17f * v);
            inner = new Color(inner.r * Mathf.Lerp(0.97f, 1.04f, w), inner.g * Mathf.Lerp(1.02f, 1.0f, w), inner.b * Mathf.Lerp(0.96f, 0.92f, w));

            // surroundings: meadow swathes, sunny crests, cooler hollows, field patches far behind
            float v2 = Mathf.PerlinNoise(x * 0.05f + s4, y * 0.05f + s1);
            var side = sideTint * (0.86f + 0.24f * v2);
            float crest = Smooth(1.5f, 16f, h);
            side = new Color(side.r * (1f + 0.1f * crest), side.g * (1f + 0.07f * crest), side.b * (1f - 0.04f * crest));
            float ob = y - D;
            if (fields && ob > 22f)
            {
                float fx = Mathf.Floor((x + s2) / 23f), fy = Mathf.Floor((y + s3) / 17f);
                float pick = Mathf.PerlinNoise(fx * 0.73f + 0.31f, fy * 0.91f + 0.17f);
                Color patch = pick < 0.38f ? new Color(1.12f, 1.05f, 0.78f) : pick < 0.55f ? new Color(0.9f, 1.02f, 0.86f)
                            : pick < 0.68f ? new Color(1.06f, 0.98f, 0.86f) : Color.white;
                float k = Smooth(22f, 45f, ob) * 0.85f;
                side = new Color(side.r * Mathf.Lerp(1f, patch.r, k), side.g * Mathf.Lerp(1f, patch.g, k), side.b * Mathf.Lerp(1f, patch.b, k));
            }
            if (forestBehind && ob > 4f)
            {
                float k = Smooth(4f, 12f, ob);
                side = new Color(side.r * (1f - 0.28f * k), side.g * (1f - 0.18f * k), side.b * (1f - 0.22f * k));
            }

            // worn ground: along the painted paths (every map: a warm halo), and in the village the dirt itself
            float pathD = PathDistance(x, y, out float pathHalf);
            float ragged = Mathf.PerlinNoise(x * 0.43f + s3, y * 0.43f + s4) - 0.5f;
            float halo = 1f - Smooth(pathHalf * 0.75f, pathHalf + 1.5f, pathD + ragged * 0.9f);
            // roads continuing out of the map at its transitions (the painted trail runs onto them)
            float road = 0f;
            for (int i = 0; i < corridors.Count; i++)
            {
                var cr = corridors[i];
                float ox = cr.x < 0f ? -x : x - W;
                if (ox < 1f) continue;
                float wob = Mathf.Sin(ox * 0.09f + s1) * 1.2f;
                float band = 1f - Smooth(0.8f, 2.0f, Mathf.Abs(y - cr.y - wob) + ragged * 0.6f);
                road = Mathf.Max(road, band * Smooth(1f, 4f, ox) * (1f - Smooth(40f, 80f, ox)) * 0.7f);
            }
            for (int i = 0; i < backExits.Count + frontExits.Count; i++)
            {
                bool back = i < backExits.Count;
                float cx = back ? backExits[i] : frontExits[i - backExits.Count];
                float oy = back ? y - D : -y;
                if (oy < 1f) continue;
                float wob = Mathf.Sin(oy * 0.09f + s1) * 1.2f;
                float band = 1f - Smooth(0.8f, 2.0f, Mathf.Abs(x - cx - wob) + ragged * 0.6f);
                road = Mathf.Max(road, band * Smooth(1f, 4f, oy) * (1f - Smooth(40f, 80f, oy)) * 0.7f);
            }
            if (meadowInside)
            {
                // a lush meadow with the dirt worn in only where feet go
                float worn = 1f - Smooth(pathHalf * 0.65f, pathHalf + 1.25f, pathD + ragged * 1.1f);
                worn = Mathf.Max(worn, road);
                float gathering = 0f;
                for (int i = 0; i < wear.Count; i++)
                {
                    var wr = wear[i];
                    float gx = x - wr.p.x, gy = (y - wr.p.y) * 1.15f, g2 = gx * gx + gy * gy;
                    if (wr.gather > 0f && g2 < 64f) gathering += wr.gather * Mathf.Exp(-g2 * (1f / (2f * 2.3f * 2.3f)));
                    if (wr.rx <= 0f) continue;
                    float ex = gx / wr.rx, ey = (y - wr.p.y) / wr.ry;
                    float e = Mathf.Sqrt(ex * ex + ey * ey);
                    if (e < 1.6f) worn = Mathf.Max(worn, 1f - Smooth(0.5f, 1.15f, e + ragged * 0.5f));
                }
                // where furniture gathers (a plaza) the ground between it is trodden too
                worn = Mathf.Max(worn, 0.92f * Smooth(1.15f, 2.1f, gathering + ragged * 0.7f));
                for (int i = 0; i < lanes.Count; i++)
                {
                    var ln = lanes[i];
                    float d = SegmentDistance(new Vector2(x, y), ln.a, ln.b);
                    if (d < ln.half + 1.2f) worn = Mathf.Max(worn, 0.85f * (1f - Smooth(ln.half * 0.4f, ln.half + 0.55f, d + ragged * 0.45f)));
                }
                // a few bare patches in the grass, never big
                float bare = Mathf.PerlinNoise(x * 0.16f + s6, y * 0.16f + s1);
                worn = Mathf.Max(worn, Smooth(0.7f, 0.8f, bare) * 0.55f);
                float meadow = 1f - worn;
                var lush = meadowTint * (0.88f + 0.2f * v);
                lush = new Color(lush.r * Mathf.Lerp(0.98f, 1.04f, w), lush.g, lush.b * Mathf.Lerp(1.02f, 0.94f, w));
                inner = Color.Lerp(inner, lush, meadow);
                blend = Mathf.Max(blend, meadow);
            }
            if (pavedInside)
            {
                // flagstones only on the walkway and forecourts; raked gravel beside them, moss and short grass beyond
                ShrineGround(x, y, ragged, out float paved, out float gravel);
                float mv = Mathf.PerlinNoise(x * 0.23f + s2, y * 0.23f + s6);
                var moss = mossTint * (0.86f + 0.2f * v) * Mathf.Lerp(0.93f, 1.07f, mv);
                var grav = gravelTint * (0.95f + 0.08f * v);
                var floor = Color.Lerp(moss, grav, gravel);
                // a little moss in the paving's joints where it meets the grass
                var stone = Color.Lerp(inner, new Color(inner.r * 0.9f, inner.g * 0.98f, inner.b * 0.86f), (1f - paved) * 0.5f);
                inner = Color.Lerp(stone, floor, 1f - paved);
                float open = 1f - paved;
                detail = gravel * open * (1f - blendOut);
                blend = Mathf.Max(blendOut, open);
            }
            if (forest)
            {
                // leaf litter (detail layer) drifts under the trees and along the trail's edges, sparse elsewhere
                float lit = 0f;
                for (int i = 0; i < litter.Count; i++)
                {
                    var l = litter[i];
                    float dx = x - l.p.x, dy = (y - l.p.y) * 1.2f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / l.r;
                    if (d < 1.3f) lit = Mathf.Max(lit, (1f - Smooth(0.3f, 1.05f, d + ragged * 0.35f)) * l.k);
                }
                if (pathD < pathHalf + 3f)
                    lit = Mathf.Max(lit, 0.55f * (1f - Smooth(0.2f, 1.3f, Mathf.Abs(pathD - pathHalf * 1.08f) + ragged * 0.7f)));
                float drift = Mathf.PerlinNoise(x * 0.17f + s4, y * 0.17f + s2);
                lit = Mathf.Max(lit, Smooth(0.66f, 0.84f, drift) * 0.28f);
                detail = lit * (1f - Smooth(3f, 12f, dOut));
            }
            else if (!pavedInside && style.Detail != BiomeDetail.None) detail = BiomeDetailWeight(x, y, h, dOut, ragged);
            var c = Color.Lerp(inner, side, pavedInside ? blendOut : blend);
            // warm, slightly lighter trodden ground beside the paths (not on paving: the walkway is the path there)
            if (halo > 0f && dOut < 6f && !pavedInside)
            {
                float f = halo * 0.35f;
                c = new Color(c.r * Mathf.Lerp(1f, pathTint.r, f), c.g * Mathf.Lerp(1f, pathTint.g, f), c.b * Mathf.Lerp(1f, pathTint.b, f));
            }

            // shade under trees, contact darkening around buildings
            for (int i = 0; i < blots.Count; i++)
            {
                var b = blots[i];
                float dx = x - b.p.x, dy = (y - b.p.y) * 1.25f;
                float d2 = (dx * dx + dy * dy) / (b.r * b.r);
                if (d2 >= 1f) continue;
                float f = (1f - d2) * (1f - d2) * b.k;
                if (b.dapple)
                {
                    // a flat-topped shade (the crown's footprint) broken by round sun flecks
                    f = Smooth(0f, 0.55f, 1f - d2) * b.k;
                    float fleck = Mathf.PerlinNoise(x * 0.62f + s2, y * 0.7f + s5) * 0.7f + Mathf.PerlinNoise(x * 1.5f + s6, y * 1.6f + s3) * 0.3f;
                    f *= Mathf.Lerp(1.1f, 0.15f, Smooth(0.5f, 0.62f, fleck));
                }
                c = new Color(c.r * Mathf.Lerp(1f, b.tint.r, f), c.g * Mathf.Lerp(1f, b.tint.g, f), c.b * Mathf.Lerp(1f, b.tint.b, f));
            }
            // the roads out of the map: warmer, worn ground (near the village the dirt texture shows it, tint the meadow)
            if (road > 0f)
            {
                float f = road * (meadowInside ? blendOut : 1f);
                c = new Color(c.r * Mathf.Lerp(1f, RoadTint.r, f), c.g * Mathf.Lerp(1f, RoadTint.g, f), c.b * Mathf.Lerp(1f, RoadTint.b, f));
            }
            // damp banks along the brook
            if (HasStream)
            {
                float u = Mathf.Abs(x - StreamX(y)) - StreamHalfWidth(y);
                if (u < 2.2f)
                {
                    float f = (1f - Smooth(-0.5f, 2.2f, u)) * 0.6f;
                    c = new Color(c.r * Mathf.Lerp(1f, 0.78f, f), c.g * Mathf.Lerp(1f, 0.88f, f), c.b * Mathf.Lerp(1f, 0.86f, f));
                }
            }

            // detail fades with distance (and on steep slopes) into the textures' average colour
            float strength = 1f - 0.55f * Smooth(30f, 110f, dOut);
            float hx = BaseHeight(x + 1f, y) - BaseHeight(x - 1f, y), hy = BaseHeight(x, y + 1f) - BaseHeight(x, y - 1f);
            float steep = Mathf.Sqrt(hx * hx + hy * hy) * 0.5f;
            strength *= Mathf.Lerp(1f, 0.5f, Smooth(0.35f, 1.1f, steep));
            float emission = 0f;
            bool original = style.Id == Biomes.Meadow || style.Id == Biomes.Village || style.Id == Biomes.Forest || style.Id == Biomes.Shrine;
            if (!original)
            {
                // broad, soft light-and-shade swathes over the new biomes' ground: the painted tile never reads as a grid
                float macro = Mathf.PerlinNoise(x * 0.045f + s3, y * 0.045f + s6) * 0.65f + Mathf.PerlinNoise(x * 0.12f + s1, y * 0.12f + s4) * 0.35f;
                float mk = Mathf.Lerp(0.84f, 1.1f, macro);
                c = new Color(c.r * mk * style.Tint.r, c.g * mk * style.Tint.g, c.b * mk * style.Tint.b);
            }
            if (waters.Count > 0) c = WaterBankTint(x, y, c, ref strength);
            if (walled) c = IndoorTint(x, y, h, steep, c, ref strength, ref detail);
            else if (!original) c = OutdoorBiomeTint(x, y, h, dOut, steep, c, ref strength, ref emission);
            tint = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), emission);
            blendDetail = new Vector2(blend, strength);
        }

        /// <summary>
        /// Weight of the biome's detail layer: the fen's puddles, rubble at the foot of cave and crypt walls and in
        /// loose patches, snow dust in the ice cave, roots in the Hollow Heart, ash and soot on the dragon's summit.
        /// </summary>
        float BiomeDetailWeight(float x, float y, float h, float dOut, float ragged)
        {
            float patch = Mathf.PerlinNoise(x * 0.13f + s3, y * 0.13f + s5);
            float fine = Mathf.PerlinNoise(x * 0.37f + s6, y * 0.37f + s2);
            float inside = DistanceInside(x, y);
            float k;
            switch (style.Detail)
            {
                case BiomeDetail.Puddles:
                    // puddles gather in the low, wet patches; fewer on the map (more with a denser fill)
                    k = Smooth(0.56f, 0.7f, patch * 0.8f + fine * 0.2f) * (dOut > 0f ? 1f : Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(def.fill)));
                    return k * (1f - Smooth(6f, 20f, dOut));
                case BiomeDetail.Scree:
                case BiomeDetail.SnowDust:
                    // fallen from the walls: heaped along their foot, a few loose patches further in
                    k = 1f - Smooth(0.5f, 3.2f, inside + ragged * 1.2f);
                    k = Mathf.Max(k, Smooth(0.64f, 0.78f, patch) * 0.8f);
                    if (dOut > 0f) k = Mathf.Max(k, 1f - Smooth(1.5f, 4f, dOut));
                    return k;
                case BiomeDetail.Roots:
                    k = Smooth(0.5f, 0.66f, patch * 0.7f + fine * 0.3f);
                    k = Mathf.Max(k, 1f - Smooth(0.5f, 3.5f, inside + ragged));
                    return k * (dOut > 0f ? 1f - Smooth(2f, 5f, dOut) : 1f);
                case BiomeDetail.Ash:
                    k = Smooth(0.48f, 0.66f, patch * 0.75f + fine * 0.25f) * 0.85f;
                    return k * (1f - Smooth(10f, 40f, dOut));
            }
            return 0f;
        }

        /// <summary>Distance (m) of a ground point inside the walkable rect from its nearest edge (0 outside).</summary>
        float DistanceInside(float x, float y) => Mathf.Max(0f, Mathf.Min(Mathf.Min(x, W - x), Mathf.Min(y, D - y)));

        /// <summary>
        /// The new outdoor biomes' colours on the land: bare rock on steep slopes (peaks, the summit), the fen's darker,
        /// wet hollows by the water table, scorched ground on the dragon's summit (embers glowing at their hearts).
        /// </summary>
        Color OutdoorBiomeTint(float x, float y, float h, float dOut, float steep, Color c, ref float strength, ref float emission)
        {
            if (style.Rock.a > 0f)
            {
                float rk = Smooth(0.45f, 1.05f, steep + (Mathf.PerlinNoise(x * 0.2f + s1, y * 0.2f + s4) - 0.5f) * 0.4f) * style.Rock.a;
                if (rk > 0f)
                {
                    float band = 0.9f + 0.12f * Mathf.Sin(h * 2.3f + Mathf.PerlinNoise(x * 0.1f + s2, y * 0.1f + s6) * 4f);
                    var rock = new Color(style.Rock.r * band, style.Rock.g * band, style.Rock.b * band);
                    c = Color.Lerp(c, rock, rk);
                    strength *= 1f - 0.6f * rk;
                }
            }
            if (style.WaterTable && dOut > 0.5f)
            {
                // wet mud down by the pools, greener on the hummocks
                float wet = 1f - Smooth(FenWaterLevel - 0.05f, FenWaterLevel + 0.35f, h);
                c = new Color(c.r * Mathf.Lerp(1f, 0.72f, wet), c.g * Mathf.Lerp(1f, 0.76f, wet), c.b * Mathf.Lerp(1f, 0.7f, wet));
            }
            if (style.Id == Biomes.Roost)
            {
                // scorched patches: soot-black rings, still warm at the heart
                // smaller, crisper scorches with ragged rims (a dragon's breath, not a cloud's shadow)
                float sc = Mathf.PerlinNoise(x * 0.11f + s5, y * 0.11f + s1);
                float burn = Smooth(0.68f, 0.72f, sc + (Mathf.PerlinNoise(x * 0.6f + s2, y * 0.6f + s3) - 0.5f) * 0.1f) * 0.85f;
                if (burn > 0f)
                {
                    c = new Color(c.r * Mathf.Lerp(1f, 0.3f, burn), c.g * Mathf.Lerp(1f, 0.27f, burn), c.b * Mathf.Lerp(1f, 0.26f, burn));
                    float heart = Smooth(0.79f, 0.84f, sc);
                    if (heart > 0f)
                    {
                        c = Color.Lerp(c, new Color(0.62f, 0.26f, 0.12f), heart * 0.6f);
                        emission = heart * 0.35f;
                    }
                }
            }
            return c;
        }

        // ================================================================== painted paths (decal_path_*)
        //
        // The path decals are 2.5D paintings of a winding trail (one full wave per tile) that end abruptly at their left
        // and right edges. Consecutive decals of a row are chained into one trail: a smooth centreline that keeps each
        // decal's wave and position (its offsets blend smoothly from one decal to the next), wanders a little and varies
        // in width, rendered as a single ribbon. The ribbon samples the art "straightened": across the ribbon it maps the
        // band of the texture around the painted trail (measured per column), along it the texture runs on continuously
        // (mirrored every tile, so there is no seam). The sides are feathered and the ends fade out raggedly and narrow.

        static readonly Color RoadTint = new Color(1.1f, 0.95f, 0.8f);

        /// <summary>Where a path texture's painted trail runs: centre and half-width (texture v) per column, periodic in u.</summary>
        internal sealed class PathProfile
        {
            public const int Columns = 64;
            public readonly float[] centre = new float[Columns], half = new float[Columns];

            public float Centre(float u) => At(centre, u);
            public float Half(float u) => At(half, u);

            static float At(float[] a, float u)
            {
                float f = Mathf.Repeat(u, 1f) * Columns - 0.5f;
                int i0 = Mathf.FloorToInt(f);
                float t = f - i0;
                int a0 = ((i0 % Columns) + Columns) % Columns, a1 = (a0 + 1) % Columns;
                return a[a0] + (a[a1] - a[a0]) * t;
            }
        }

        /// <summary>Reads a texture's pixels (rows bottom-up), width × height; null when it can't.</summary>
        internal delegate Color32[] PixelReader(Texture2D tex, out int width, out int height);
        /// <summary>
        /// How MapTerrain reads painted textures (the path profiles). Game textures are not CPU-readable, so MapView installs
        /// a GPU read-back; by default the texture's own pixels are used (readable textures, the preview tool).
        /// </summary>
        internal static PixelReader ReadPixels;
        static readonly Dictionary<Texture2D, PathProfile> Profiles = new Dictionary<Texture2D, PathProfile>();

        internal static PathProfile ProfileOf(Texture2D tex)
        {
            if (tex != null && Profiles.TryGetValue(tex, out var known)) return known;
            var pr = new PathProfile();
            bool ok = false;
            if (tex != null)
            {
                Color32[] px = null;
                int w = 0, h = 0;
                try
                {
                    if (ReadPixels != null) px = ReadPixels(tex, out w, out h);
                    else { px = tex.GetPixels32(); w = tex.width; h = tex.height; }
                }
                catch (System.Exception) { px = null; }
                if (px != null && w > 8 && h > 8 && px.Length >= w * h) ok = MeasureProfile(px, w, h, pr);
            }
            if (!ok)
            {
                // the shape of the painted path art (one full wave across the tile), for when it can't be measured
                for (int i = 0; i < PathProfile.Columns; i++)
                {
                    float u = (i + 0.5f) / PathProfile.Columns;
                    pr.centre[i] = 0.512f - 0.157f * Mathf.Sin(u * Mathf.PI * 2f);
                    pr.half[i] = 0.235f;
                }
            }
            if (tex != null) Profiles[tex] = pr;
            return pr;
        }

        static bool MeasureProfile(Color32[] px, int w, int h, PathProfile pr)
        {
            int n = PathProfile.Columns, found = 0;
            var c = new float[n];
            var hw = new float[n];
            var has = new bool[n];
            for (int i = 0; i < n; i++)
            {
                // the outermost columns of the art are ragged: stay two pixels in
                int x = Mathf.Clamp(Mathf.RoundToInt((i + 0.5f) / n * w), 2, w - 3);
                int lo = -1, hi = -1;
                for (int y = 0; y < h; y++)
                {
                    int k = y * w + x;
                    if (px[k - 1].a + px[k].a + px[k + 1].a < 3 * 128) continue;   // a 3-pixel vote skips stray grass tips
                    if (lo < 0) lo = y;
                    hi = y;
                }
                if (lo < 0 || hi - lo < h / 20) continue;
                c[i] = (lo + hi + 1) * 0.5f / h;
                hw[i] = (hi + 1 - lo) * 0.5f / h;
                has[i] = true;
                found++;
            }
            if (found < n * 3 / 4) return false;
            for (int i = 0; i < n; i++)
            {
                if (has[i]) continue;
                for (int j = 1; j < n; j++)
                {
                    int k = has[(i + j) % n] ? (i + j) % n : has[(i - j + n) % n] ? (i - j + n) % n : -1;
                    if (k < 0) continue;
                    c[i] = c[k]; hw[i] = hw[k];
                    break;
                }
            }
            // smooth around the (periodic) strip: pebbles and grass tufts poke out of the trail's edge
            for (int i = 0; i < n; i++)
            {
                float sc = 0f, sh = 0f;
                for (int k = -3; k <= 3; k++) { sc += c[(i + k + n) % n]; sh += hw[(i + k + n) % n]; }
                pr.centre[i] = sc / 7f;
                pr.half[i] = Mathf.Clamp(sh / 7f, 0.08f, 0.46f);
            }
            return true;
        }

        /// <summary>
        /// A trail: chained path decals (its centreline sampled every PathStep metres along x), or a MapDef.paths
        /// polyline (data: smoothed, sampled every DataPathStep metres along its length, in any direction).
        /// </summary>
        sealed class PathChain
        {
            public string art;
            public Texture2D tex;
            public PathProfile profile;
            public int order;
            public bool data;
            public float tile, texH;                   // art tile length along the trail, art height across it (m)
            public float x0, x1, yMin, yMax;
            public readonly List<PropDef> decals = new List<PropDef>();
            public readonly List<Vector2> pts = new List<Vector2>();
            public readonly List<float> half = new List<float>();   // half-width (m) of the trail itself
            public readonly List<float> endD = new List<float>();   // distance (m) to the trail's nearer end
            public readonly List<Rect> blocks = new List<Rect>();   // bounds of every BlockSize samples (nearest search)
        }

        const int BlockSize = 24;

        /// <summary>The bounds of each run of BlockSize samples (NearestPath skips runs out of reach).</summary>
        static void BuildBlocks(PathChain ch)
        {
            ch.blocks.Clear();
            for (int b = 0; b < ch.pts.Count; b += BlockSize)
            {
                int e = Mathf.Min(ch.pts.Count, b + BlockSize + 1);
                float xa = float.MaxValue, xb = float.MinValue, ya = float.MaxValue, yb = float.MinValue;
                for (int i = b; i < e; i++)
                {
                    var p = ch.pts[i];
                    if (p.x < xa) xa = p.x;
                    if (p.x > xb) xb = p.x;
                    if (p.y < ya) ya = p.y;
                    if (p.y > yb) yb = p.y;
                }
                ch.blocks.Add(Rect.MinMaxRect(xa, ya, xb, yb));
            }
        }

        const float PathStep = 0.1f;
        readonly List<PathChain> chains = new List<PathChain>();

        static float Sc(PropDef p) => p.scale > 0f ? p.scale : 1f;

        void AnalysePaths()
        {
            var byArt = new Dictionary<string, List<PropDef>>(System.StringComparer.Ordinal);
            var arts = new List<string>();
            foreach (var p in def.props)
            {
                if (p == null || string.IsNullOrEmpty(p.art) || !p.art.StartsWith("decal_path", System.StringComparison.Ordinal)) continue;
                if (!byArt.TryGetValue(p.art, out var l)) { byArt[p.art] = l = new List<PropDef>(); arts.Add(p.art); }
                l.Add(p);
            }
            foreach (var art in arts)
            {
                var list = byArt[art];
                list.Sort((a, b) => a.pos.x != b.pos.x ? a.pos.x.CompareTo(b.pos.x) : a.pos.y.CompareTo(b.pos.y));
                var tex = ArtLibrary.Texture(art);
                float aspect = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 2f;
                float baseH = ArtLibrary.Height(art, 4f);
                var used = new bool[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    if (used[i]) continue;
                    var ch = new PathChain { art = art, tex = tex, profile = ProfileOf(tex), order = chains.Count };
                    used[i] = true;
                    ch.decals.Add(list[i]);
                    // follow the row: the next decal whose left end meets this one's right end
                    var cur = list[i];
                    while (true)
                    {
                        int best = -1;
                        float bestScore = float.MaxValue;
                        for (int j = 0; j < list.Count; j++)
                        {
                            if (used[j]) continue;
                            var nx = list[j];
                            float dx = nx.pos.x - cur.pos.x, need = baseH * aspect * (Sc(cur) + Sc(nx)) * 0.5f;
                            float dy = Mathf.Abs(nx.pos.y - cur.pos.y);
                            if (dx < need * 0.55f || dx > need * 1.25f || dy > baseH * Sc(cur) * 0.6f) continue;
                            float score = Mathf.Abs(dx - need) + dy;
                            if (score < bestScore) { bestScore = score; best = j; }
                        }
                        if (best < 0) break;
                        used[best] = true;
                        ch.decals.Add(list[best]);
                        cur = list[best];
                    }
                    BuildCentreline(ch, baseH, aspect);
                    chains.Add(ch);
                }
            }
        }

        void BuildCentreline(PathChain ch, float baseH, float aspect)
        {
            var d = ch.decals;
            int n = d.Count;
            ch.texH = baseH * Sc(d[0]);
            ch.tile = ch.texH * aspect;
            float start = d[0].pos.x - baseH * Sc(d[0]) * aspect * 0.5f;
            float end = d[n - 1].pos.x + baseH * Sc(d[n - 1]) * aspect * 0.5f;
            // a trail meeting a side edge runs on across the flat margin (further at an exit), fading out there
            if (start <= 3f) start = Mathf.Min(start, ExitNear(-1f, d[0].pos.y) ? -3.4f : -2f);
            if (end >= W - 3f) end = Mathf.Max(end, W + (ExitNear(1f, d[n - 1].pos.y) ? 3.4f : 2f));
            ch.x0 = start;
            ch.x1 = end;
            ch.yMin = float.MaxValue;
            ch.yMax = float.MinValue;
            float phase = s2 + ch.order * 17.3f;
            float meanHalf = 0f;
            for (int i = 0; i < PathProfile.Columns; i++) meanHalf += ch.profile.half[i];
            meanHalf /= PathProfile.Columns;
            int steps = Mathf.Max(1, Mathf.CeilToInt((end - start) / PathStep));
            for (int s = 0; s <= steps; s++)
            {
                float x = Mathf.Min(end, start + s * PathStep);
                // the decals' own centres, blended smoothly between neighbours
                int k = 0;
                while (k < n - 1 && d[k + 1].pos.x <= x) k++;
                float y, shape;
                if (x <= d[0].pos.x) { y = d[0].pos.y; shape = Wave(ch, d[0], baseH, aspect, x); }
                else if (k >= n - 1) { y = d[n - 1].pos.y; shape = Wave(ch, d[n - 1], baseH, aspect, x); }
                else
                {
                    float t = Smooth(d[k].pos.x, d[k + 1].pos.x, x);
                    y = Mathf.Lerp(d[k].pos.y, d[k + 1].pos.y, t);
                    shape = Mathf.Lerp(Wave(ch, d[k], baseH, aspect, x), Wave(ch, d[k + 1], baseH, aspect, x), t);
                }
                // keep the painted wave, a little irregular, and let the trail wander
                float amp = 0.82f + 0.3f * Mathf.PerlinNoise(x * 0.06f + phase, 3.1f);
                y += shape * amp + (Mathf.PerlinNoise(x * 0.11f + phase, 0.37f) - 0.5f) * 0.45f;
                ch.pts.Add(new Vector2(x, y));
                ch.half.Add(meanHalf * ch.texH * (0.9f + 0.22f * Mathf.PerlinNoise(x * 0.17f + phase, 7.7f)));
                ch.endD.Add(Mathf.Min(x - ch.x0, ch.x1 - x));
                ch.yMin = Mathf.Min(ch.yMin, y);
                ch.yMax = Mathf.Max(ch.yMax, y);
            }
            BuildBlocks(ch);
        }

        /// <summary>The decal's painted trail offset (m, along y) from its centre at x — periodic beyond its own tile.</summary>
        static float Wave(PathChain ch, PropDef p, float baseH, float aspect, float x)
        {
            float h = baseH * Sc(p), w = h * aspect;
            float u = (x - (p.pos.x - w * 0.5f)) / w;
            if (p.flip) u = 1f - u;
            return (ch.profile.Centre(u) - 0.5f) * h;
        }

        bool ExitNear(float side, float y)
        {
            for (int i = 0; i < corridors.Count; i++)
                if (corridors[i].x == side && Mathf.Abs(corridors[i].y - y) < 4f) return true;
            return false;
        }

        /// <summary>Distance (m) from a ground point to the nearest painted trail's centreline, and that trail's half-width.</summary>
        float PathDistance(float x, float y, out float half) => NearestPath(x, y, out _, out half);

        float NearestPath(float x, float y, out Vector2 nearest, out float half)
        {
            float best = float.MaxValue;
            nearest = new Vector2(x, y);
            half = 1f;
            // every sample within reach is visited (the nearest one wins); callers only care about trails within reach
            const float reach = 9f;
            const float reach2 = reach * reach;
            for (int c = 0; c < chains.Count; c++)
            {
                var ch = chains[c];
                if (x < ch.x0 - reach || x > ch.x1 + reach || y < ch.yMin - reach || y > ch.yMax + reach) continue;
                int n = ch.pts.Count;
                for (int b = 0; b < ch.blocks.Count; b++)
                {
                    var r = ch.blocks[b];
                    if (x < r.xMin - reach || x > r.xMax + reach || y < r.yMin - reach || y > r.yMax + reach) continue;
                    int i1 = Mathf.Min(n, (b + 1) * BlockSize);
                    for (int i = b * BlockSize; i < i1; i++)
                    {
                        var p = ch.pts[i];
                        float dx = p.x - x, dy = p.y - y, d2 = dx * dx + dy * dy;
                        if (d2 >= best || d2 > reach2) continue;
                        best = d2;
                        nearest = p;
                        // the trail narrows to nothing at its faded ends
                        half = ch.half[i] * Mathf.Lerp(0.3f, 1f, Smooth(0f, 2.2f, ch.endD[i]));
                    }
                }
            }
            return best < float.MaxValue ? Mathf.Sqrt(best) : float.MaxValue;
        }

        static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float l2 = ab.sqrMagnitude;
            float t = l2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------ decals

        const int DecalSortingBase = -8;

        static int DecalOrder(string art) => art.Contains("blight") ? 3 : art.Contains("flower") ? 2 : art.Contains("stone") ? 1 : 0;

        /// <summary>
        /// The painted ground decals, just above z = 0: the trails as soft ribbons (one mesh per path art), the others
        /// (flower beds, blight) as flat quads (one mesh per art). Drawn under ground previews / rings (order ≥ 0).
        /// </summary>
        public void BuildDecals(Transform parent)
        {
            var chainsByArt = new Dictionary<string, List<PathChain>>(System.StringComparer.Ordinal);
            var arts = new List<string>();
            foreach (var ch in chains)
            {
                if (!chainsByArt.TryGetValue(ch.art, out var l)) { chainsByArt[ch.art] = l = new List<PathChain>(); arts.Add(ch.art); }
                l.Add(ch);
            }
            var quads = new Dictionary<string, List<PropDef>>(System.StringComparer.Ordinal);
            foreach (var p in def.props)
            {
                if (p == null || string.IsNullOrEmpty(p.art) || !p.art.StartsWith("decal_", System.StringComparison.Ordinal)) continue;
                if (p.art.StartsWith("decal_path", System.StringComparison.Ordinal)) continue;
                if (!quads.TryGetValue(p.art, out var l)) { quads[p.art] = l = new List<PropDef>(); arts.Add(p.art); }
                l.Add(p);
            }
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color32>();
            var tris = new List<int>();
            foreach (var art in arts)
            {
                verts.Clear(); norms.Clear(); uvs.Clear(); cols.Clear(); tris.Clear();
                var tex = ArtLibrary.Texture(art);
                int ord = DecalOrder(art);
                if (chainsByArt.TryGetValue(art, out var trail))
                {
                    // on paved ground the walkway is the path: a painted trail on top would read as a stain
                    if (pavedInside) continue;
                    for (int i = 0; i < trail.Count; i++) AddTrail(trail[i], ord, verts, norms, uvs, cols, tris);
                }
                else AddPatches(quads[art], tex, ord, verts, norms, uvs, cols, tris);
                if (verts.Count == 0) continue;
                var m = new Mesh { name = "lv_decals_" + art };
                if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(verts);
                m.SetNormals(norms);
                m.SetUVs(0, uvs);
                m.SetColors(cols);
                m.SetTriangles(tris, 0, true);
                owned.Add(m);
                var go = new GameObject("Decals " + art);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                var mat = Materials3D.LitTransparent(tex);
                // the blight's veins glow (its art is only veins on a soft violet halo over a faint dark heart)
                if (art.Contains("blight")) mat.SetFloat(Materials3D.EmissionId, 0.45f);
                r.sharedMaterial = mat;
                r.sortingOrder = DecalSortingBase + ord;
                Quiet(r);
            }
        }

        static Color32 DecalColor(string tintHex, float alpha)
        {
            var tint = string.IsNullOrEmpty(tintHex) ? Color.white : Ui.Hex(tintHex);
            // painted a little under white to stay "as painted" under the bright up-facing light
            return new Color(tint.r * 0.86f, tint.g * 0.86f, tint.b * 0.86f, Mathf.Clamp01(tint.a * alpha));
        }

        // across the ribbon: offsets (× half-width) and opacity — feathered sides
        static readonly float[] RowT = { -1f, -0.8f, -0.58f, 0f, 0.58f, 0.8f, 1f };
        static readonly float[] RowA = { 0f, 0.55f, 1f, 1f, 1f, 0.55f, 0f };

        void AddTrail(PathChain ch, int ord, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Color32> cols, List<int> tris)
        {
            int np = ch.pts.Count;
            if (np < 2) return;
            // arc length along the sampled centreline
            var along = new float[np];
            for (int i = 1; i < np; i++) along[i] = along[i - 1] + Vector2.Distance(ch.pts[i - 1], ch.pts[i]);
            float L = along[np - 1];
            if (L < 0.5f) return;
            // stations: an integer number per tile so every mirror turn of the texture falls on one
            float tile = Mathf.Max(1f, ch.tile);
            int perTile = Mathf.Max(8, Mathf.CeilToInt(tile / 0.3f));
            float ds = tile / perTile;
            int stations = Mathf.CeilToInt(L / ds) + 1;
            float z = -(0.006f + ord * 0.002f + (ch.order % 8) * 0.0003f);
            float fadeLen = Mathf.Clamp(tile * 0.14f, 0.6f, 1.6f);
            float phase = s5 + ch.order * 11.1f;
            var tint = ch.decals.Count > 0 ? ch.decals[0].tint : "";
            int rows = RowT.Length;
            int b0 = verts.Count;
            int seg = 0;
            for (int k = 0; k < stations; k++)
            {
                float s = Mathf.Min(L, k * ds);
                while (seg < np - 2 && along[seg + 1] < s) seg++;
                float segLen = Mathf.Max(1e-5f, along[seg + 1] - along[seg]);
                var p = Vector2.Lerp(ch.pts[seg], ch.pts[seg + 1], (s - along[seg]) / segLen);
                var tan = ch.pts[Mathf.Min(np - 1, seg + 2)] - ch.pts[Mathf.Max(0, seg - 1)];
                tan = tan.sqrMagnitude > 1e-8f ? tan.normalized : Vector2.right;
                var nrm = new Vector2(-tan.y, tan.x);
                // texture column: runs on along the trail, mirrored every tile (no seam)
                float uT = Mathf.PingPong(s / tile, 1f);
                float u = 0.012f + 0.976f * uT;
                float cv = ch.profile.Centre(u), hv = ch.profile.Half(u) + 0.07f;
                float endDist = Mathf.Min(s, L - s);
                float taper = Mathf.Lerp(0.55f, 1f, Smooth(0f, 2.4f, endDist));
                float widthNoise = 0.88f + 0.24f * Mathf.PerlinNoise(s * 0.16f + phase, 5.3f);
                float halfW = hv * ch.texH * widthNoise * taper;
                for (int r = 0; r < rows; r++)
                {
                    // a ragged end: each row fades out a little earlier or later
                    float jitter = Mathf.PerlinNoise(r * 1.7f + phase, (s < L * 0.5f ? 0.5f : 9.5f)) * 0.7f;
                    float fade = Smooth(0f, fadeLen, endDist - jitter);
                    var q = p + nrm * (RowT[r] * halfW);
                    float lift = 0f;
                    if (ch.data)
                    {
                        // a data trail follows the land (an indoor passage, a ford's banks) and gives way to water: it fades
                        // into a ford, so the shallows and stepping stones show (a bridge carries the way over a river)
                        lift = Mathf.Max(0f, Height(q.x, q.y));
                        if (waters.Count > 0) fade *= Smooth(-0.7f, 0.05f, WaterEdge(q.x, q.y));
                    }
                    verts.Add(new Vector3(q.x, q.y, z - lift));
                    norms.Add(World3D.Up);
                    uvs.Add(new Vector2(u, cv + RowT[r] * hv));
                    cols.Add(DecalColor(tint, RowA[r] * fade));
                }
                if (k == 0) continue;
                int a0 = b0 + (k - 1) * rows, a1 = b0 + k * rows;
                for (int r = 0; r < rows - 1; r++)
                {
                    // previous station (x−) to this one (x+), row r (y−) to r + 1 (y+): clockwise seen from above
                    int A = a0 + r, B = a0 + r + 1, C = a1 + r + 1, Dd = a1 + r;
                    tris.Add(A); tris.Add(B); tris.Add(C);
                    tris.Add(A); tris.Add(C); tris.Add(Dd);
                }
            }
        }

        // rings of a painted patch: radius in texture units from the centre, opacity — per art, so each painting's own
        // soft edge is what fades out (the blight is a glow around its veins, the flower bed a loose drift of blooms)
        static readonly float[] PatchR = { 0.3f, 0.42f, 0.53f };
        static readonly float[] PatchA = { 1f, 0.62f, 0f };
        static readonly float[] BlightR = { 0.1f, 0.2f, 0.3f, 0.4f, 0.47f };
        static readonly float[] BlightA = { 1f, 1f, 0.85f, 0.4f, 0f };
        static readonly float[] FlowerR = { 0.2f, 0.3f, 0.38f, 0.46f };
        static readonly float[] FlowerA = { 1f, 0.9f, 0.5f, 0f };
        const int PatchSides = 18;

        static void PatchRings(string art, out float[] radii, out float[] alphas)
        {
            if (art.Contains("blight")) { radii = BlightR; alphas = BlightA; }
            else if (art.Contains("flower")) { radii = FlowerR; alphas = FlowerA; }
            else { radii = PatchR; alphas = PatchA; }
        }

        /// <summary>Round painted patches (flower beds, blight): a disc whose rim fades out, so the art's own hard,
        /// darker edge melts into the ground instead of reading as a stain.</summary>
        static void AddPatches(List<PropDef> list, Texture2D tex, int ord, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<Color32> cols, List<int> tris)
        {
            if (list == null || list.Count == 0) return;
            string art = list[0].art;
            float aspect = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 1f;
            float baseH = ArtLibrary.Height(art, 4f);
            PatchRings(art, out var ringR, out var ringA);
            int rings = ringR.Length;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                float h = baseH * Sc(p), w = h * aspect;
                float z = -(0.006f + ord * 0.002f + (i % 8) * 0.0003f);
                var c = new Vector2(p.pos.x, p.pos.y);
                int centre = verts.Count;
                verts.Add(new Vector3(c.x, c.y, z));
                uvs.Add(new Vector2(0.5f, 0.5f));
                cols.Add(DecalColor(p.tint, 1f));
                norms.Add(World3D.Up);
                float phase = (p.pos.x * 0.37f + p.pos.y * 0.73f) * 3.1f;
                for (int k = 0; k < PatchSides; k++)
                {
                    float ang = k * Mathf.PI * 2f / PatchSides;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    // an irregular rim
                    float wob = 1f + 0.08f * Mathf.Sin(ang * 3f + phase) + 0.05f * Mathf.Sin(ang * 5f + phase * 1.7f);
                    for (int r = 0; r < rings; r++)
                    {
                        float rr = ringR[r] * (r == 0 ? 1f : wob);
                        verts.Add(new Vector3(c.x + dir.x * rr * w, c.y + dir.y * rr * h, z));
                        float u = 0.5f + dir.x * rr;
                        uvs.Add(new Vector2(p.flip ? 1f - u : u, 0.5f + dir.y * rr));
                        cols.Add(DecalColor(p.tint, ringA[r]));
                        norms.Add(World3D.Up);
                    }
                }
                for (int k = 0; k < PatchSides; k++)
                {
                    int a = centre + 1 + k * rings, b = centre + 1 + ((k + 1) % PatchSides) * rings;
                    // counter-clockwise angles: (centre, next, this) is clockwise seen from above
                    tris.Add(centre); tris.Add(b); tris.Add(a);
                    for (int r = 0; r < rings - 1; r++)
                    {
                        tris.Add(a + r); tris.Add(b + r); tris.Add(b + r + 1);
                        tris.Add(a + r); tris.Add(b + r + 1); tris.Add(a + r + 1);
                    }
                }
            }
        }

        // ================================================================== mesh

        static List<float> Lines(float innerMin, float innerMax, float outerMin, float outerMax, float growth, float maxStep, float innerStep = 1f)
        {
            var mid = new List<float>();
            int n = Mathf.Max(1, Mathf.CeilToInt((innerMax - innerMin) / innerStep));
            float st = (innerMax - innerMin) / n;
            for (int i = 0; i <= n; i++) mid.Add(innerMin + i * st);
            var low = new List<float>();
            float s = 1.2f, v = innerMin;
            while (v > outerMin) { s = Mathf.Min(maxStep, s * growth); v = Mathf.Max(outerMin, v - s); low.Add(v); }
            low.Reverse();
            low.AddRange(mid);
            s = 1.2f; v = innerMax;
            while (v < outerMax) { s = Mathf.Min(maxStep, s * growth); v = Mathf.Min(outerMax, v + s); low.Add(v); }
            return low;
        }

        public void Build(Material material, float groundTile)
        {
            Material = material;
            // the village's dirt is worn into the meadow by vertex blend: a finer grid on the flat ground keeps it soft
            bool original = style.Id == Biomes.Meadow || style.Id == Biomes.Village || style.Id == Biomes.Forest || style.Id == Biomes.Shrine;
            float step = meadowInside || pavedInside || !original || waters.Count > 0 ? 0.5f : 1f;
            var xs = Lines(-8f, W + 8f, -170f, W + 170f, 1.12f, 10f, step);
            var ys = Lines(-5f, D + 6f, -52f, D + 215f, 1.12f, 10f, step);
            int nx = xs.Count, ny = ys.Count;
            var pos = new Vector3[nx * ny];
            var col = new Color32[nx * ny];
            var bd = new Vector2[nx * ny];
            var dw = new Vector2[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = xs[i], y = ys[j];
                    float h = Height(x, y);
                    int k = j * nx + i;
                    pos[k] = new Vector3(x, y, -h);
                    Sample(x, y, h, out var c, out var b, out float d);
                    col[k] = c;
                    bd[k] = b;
                    dw[k] = new Vector2(d, 0f);
                }

            int chunkCols = Mathf.Max(8, Mathf.CeilToInt((nx - 1) / 6f));
            var verts = new List<Vector3>(chunkCols * ny * 6);
            var norms = new List<Vector3>(chunkCols * ny * 6);
            var cols = new List<Color32>(chunkCols * ny * 6);
            var uvs = new List<Vector2>(chunkCols * ny * 6);
            var uv1s = new List<Vector2>(chunkCols * ny * 6);
            var tris = new List<int>(chunkCols * ny * 6);
            for (int c0 = 0; c0 < nx - 1; c0 += chunkCols)
            {
                int c1 = Mathf.Min(nx - 1, c0 + chunkCols);
                verts.Clear(); norms.Clear(); cols.Clear(); uvs.Clear(); uv1s.Clear(); tris.Clear();
                for (int j = 0; j < ny - 1; j++)
                    for (int i = c0; i < c1; i++)
                    {
                        int k00 = j * nx + i, k10 = k00 + 1, k01 = k00 + nx, k11 = k01 + 1;
                        // alternate the diagonal: a faceted, hand-cut look on the hills
                        if (((i + j) & 1) == 0)
                        {
                            Tri(pos, col, bd, dw, k00, k01, k11, verts, norms, cols, uvs, uv1s, tris);
                            Tri(pos, col, bd, dw, k00, k11, k10, verts, norms, cols, uvs, uv1s, tris);
                        }
                        else
                        {
                            Tri(pos, col, bd, dw, k00, k01, k10, verts, norms, cols, uvs, uv1s, tris);
                            Tri(pos, col, bd, dw, k01, k11, k10, verts, norms, cols, uvs, uv1s, tris);
                        }
                    }
                var m = new Mesh { name = "lv_terrain_" + c0 };
                if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(verts);
                m.SetNormals(norms);
                m.SetColors(cols);
                m.SetUVs(0, uvs);
                m.SetUVs(1, uv1s);
                m.SetTriangles(tris, 0, true);
                m.UploadMeshData(true);
                owned.Add(m);
                var go = new GameObject("Terrain Chunk " + c0);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                Quiet(r);
            }
        }

        static void Tri(Vector3[] pos, Color32[] col, Vector2[] bd, Vector2[] dw, int a, int b, int c,
                        List<Vector3> verts, List<Vector3> norms, List<Color32> cols, List<Vector2> uvs, List<Vector2> uv1s, List<int> tris)
        {
            var pa = pos[a]; var pb = pos[b]; var pc = pos[c];
            // vertices in grid order (x right, y into the scene) wind clockwise seen from above: the normal points up (−Z)
            var n = Vector3.Cross(pb - pa, pc - pa);
            n = n.sqrMagnitude > 1e-12f ? n.normalized : World3D.Up;
            if (n.z > 0f) n = -n;
            int i0 = verts.Count;
            verts.Add(pa); verts.Add(pb); verts.Add(pc);
            norms.Add(n); norms.Add(n); norms.Add(n);
            cols.Add(col[a]); cols.Add(col[b]); cols.Add(col[c]);
            uvs.Add(bd[a]); uvs.Add(bd[b]); uvs.Add(bd[c]);
            uv1s.Add(dw[a]); uv1s.Add(dw[b]); uv1s.Add(dw[c]);
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
        }

        static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ================================================================== ground cover

        /// <summary>Local Y-up point (x, height, depth) of a ground point: geometry built for a World3D.Upright root.</summary>
        Vector3 Local(float x, float y, float lift = 0f) => new Vector3(x, Height(x, y) + lift, y);

        /// <summary>
        /// Grass, flowers, stones and (outlined, like the prop bushes) bushes outside the walkable area, a sprinkle of
        /// flower tufts in the village meadow away from its paths, sparse trees on the hills.
        /// </summary>
        public void BuildGroundCover(bool hillTrees)
        {
            if (walled)
            {
                // indoors: rubble, spikes and the walls' dressing, no grass or trees
                BuildBiomeCover();
                BuildFill();
                BuildWalls();
                return;
            }
            if (style.Cover == BiomeCover.Meadow) BuildMeadowCover(hillTrees && style.HillTrees);
            else
            {
                BuildBiomeCover();
                if (hillTrees && style.HillTrees) BuildBiomeHillTrees();
            }
            BuildFill();
        }

        /// <summary>The original maps' ground cover (meadow, village, forest, shrine palettes) and hill trees.</summary>
        void BuildMeadowCover(bool hillTrees)
        {
            var rng = new System.Random(StableHash(def.id) * 7 + 11);
            float R() => (float)rng.NextDouble();
            float x0 = -46f, x1 = W + 46f;
            const float chunkW = 34f;
            var grass = style.Grass;
            var grassDark = style.GrassDark;
            var bushLeaf = style.Bush;
            var flowers = style.Flowers;

            for (float cx = x0; cx < x1; cx += chunkW)
            {
                var mb = new MeshBuilder(StableHash(def.id) + (int)cx) { Jitter = 0.06f };
                var bushes = new MeshBuilder(StableHash(def.id) + (int)cx + 7) { Jitter = 0.06f };
                float cxe = Mathf.Min(x1, cx + chunkW);
                // density: lush along the front bank (closest to the camera), lighter elsewhere
                for (float gx = cx; gx < cxe; gx += 1.4f)
                    for (float gy = -24f; gy < D + 26f; gy += 1.4f)
                    {
                        float x = gx + R() * 1.4f, y = gy + R() * 1.4f;
                        float dOut = DistanceOutside(x, y);
                        if (dOut < 0.9f)
                        {
                            // inside the village: now and then a flower tuft in the meadow, clear of paths and props
                            if (!meadowInside || R() > 0.075f || !OpenMeadow(x, y)) continue;
                            float clumpIn = Mathf.PerlinNoise(x * 0.3f + s3, y * 0.3f + s1);
                            if (clumpIn < 0.42f) continue;
                            Tuft(mb, rng, Local(x, y), Mathf.Lerp(0.26f, 0.42f, R()), grass, grassDark, R() < 0.75f ? flowers : null);
                            continue;
                        }
                        if (Reserved(x, y, 0.2f)) continue;
                        float density = y < 0f ? Mathf.Lerp(0.55f, 0.18f, Smooth(2f, 22f, dOut)) : Mathf.Lerp(0.32f, 0.08f, Smooth(2f, 20f, dOut));
                        float clump = Mathf.PerlinNoise(x * 0.21f + s2, y * 0.21f + s5);
                        density *= 0.4f + 1.2f * clump;
                        float roll = R();
                        if (roll > density) continue;
                        float pick = R();
                        if (pick < 0.68f) Tuft(mb, rng, Local(x, y), Mathf.Lerp(0.32f, 0.72f, R()), grass, grassDark, null);
                        else if (pick < 0.86f) Tuft(mb, rng, Local(x, y), Mathf.Lerp(0.3f, 0.55f, R()), grass, grassDark, flowers);
                        else if (pick < 0.95f) Stone(mb, rng, Local(x, y, -0.04f), Mathf.Lerp(0.14f, 0.42f, R()));
                        else if (dOut > 3f) Bush(bushes, rng, Local(x, y), Mathf.Lerp(0.5f, 0.9f, R()), bushLeaf);
                    }
                Emit(mb, "Ground Cover " + cx);
                Emit(bushes, "Ground Bushes " + cx, true);
            }

            if (!hillTrees) return;
            // sparse clumps of trees on the hills (behind and to the sides), never on the walkable strip's doorstep
            var trees = new MeshBuilder(StableHash(def.id) + 99) { Jitter = 0.07f };
            var leaf = style.HillLeaf;
            int count = 0;
            for (float gx = -120f; gx < W + 120f; gx += 7f)
                for (float gy = -40f; gy < D + 150f; gy += 7f)
                {
                    float x = gx + R() * 7f, y = gy + R() * 7f;
                    float dOut = DistanceOutside(x, y);
                    if (dOut < (y > D ? 6.5f : 11f)) continue;   // a few close behind the strip, the sides stay open
                    if (y < -8f && dOut < 26f) continue;         // keep the camera's side open
                    if (Reserved(x, y, 3f)) continue;
                    float clump = Mathf.PerlinNoise(x * 0.035f + s6, y * 0.035f + s3);
                    if (clump < 0.52f || R() > (clump - 0.45f) * 1.6f) continue;
                    float size = Mathf.Lerp(0.7f, 1.25f, R()) * Mathf.Lerp(1f, 1.5f, Smooth(30f, 120f, dOut));
                    if (R() < style.PineShare) Pine(trees, rng, Local(x, y, -0.2f), size * 7f, Paint.Shade(leaf[3], 0.9f));
                    else RoundTree(trees, rng, Local(x, y, -0.2f), size * 5.5f, leaf[rng.Next(leaf.Length)]);
                    if (++count > 260) break;
                }
            Emit(trees, "Hill Trees");
        }

        void Emit(MeshBuilder mb, string name, bool outlined = false)
        {
            if (mb.IsEmpty) return;
            var m = mb.ToMesh("lv_" + name.Replace(' ', '_').ToLowerInvariant());
            owned.Add(m);
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localRotation = World3D.Upright;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            if (outlined) r.sharedMaterials = Materials3D.WithOutline();
            else r.sharedMaterial = Materials3D.LowPoly;
            Quiet(r);
        }

        /// <summary>Open village meadow: clear of the trails and lanes, worn spots, props, transitions and spawns.</summary>
        bool OpenMeadow(float x, float y)
        {
            if (x < 0.6f || x > W - 0.6f || y < 0.4f || y > D - 0.6f) return false;
            if (PathDistance(x, y, out float half) < half + 1.5f) return false;
            for (int i = 0; i < wear.Count; i++)
            {
                var wr = wear[i];
                float r = Mathf.Max(wr.rx, wr.gather > 0f ? 2.6f : 0f) + 0.8f;
                if ((new Vector2(x, y) - wr.p).sqrMagnitude < r * r) return false;
            }
            for (int i = 0; i < lanes.Count; i++)
                if (SegmentDistance(new Vector2(x, y), lanes[i].a, lanes[i].b) < lanes[i].half + 0.9f) return false;
            foreach (var p in def.props)
            {
                if (p == null || string.IsNullOrEmpty(p.art)) continue;
                float sc = p.scale > 0f ? p.scale : 1f;
                float r = p.art.StartsWith("decal_") ? 1.6f * sc : p.collider != null ? Mathf.Max(p.collider.w, p.collider.h) * 0.6f * sc + 0.9f : 1.3f * sc;
                float dx = x - p.pos.x, dy = y - p.pos.y;
                if (dx * dx + dy * dy < r * r) return false;
            }
            foreach (var t in def.transitions)
                if (t != null && Mathf.Abs(x - t.pos.x) < t.size.x * 0.5f + 1f && Mathf.Abs(y - t.pos.y) < t.size.y * 0.5f + 1f) return false;
            foreach (var sp in def.spawns)
                if (sp != null && (new Vector2(x - sp.pos.x, y - sp.pos.y)).sqrMagnitude < 2.2f) return false;
            return true;
        }

        internal static void Tuft(MeshBuilder mb, System.Random rng, Vector3 b, float height, Color col, Color dark, Color[] flowers)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0.9f; mb.WindGradient = true; mb.WindY0 = b.y; mb.WindY1 = b.y + height;
            int blades = 5 + rng.Next(4);
            for (int i = 0; i < blades; i++)
            {
                float a = R() * Mathf.PI * 2f;
                float lean = 0.15f + R() * 0.3f;
                float hh = height * (0.65f + 0.35f * R());
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var basePt = b + dir * (0.04f + 0.06f * R());
                var tip = basePt + dir * (lean * hh) + Vector3.up * hh;
                mb.Color = Color.Lerp(dark, col, R());
                mb.Blade(basePt, tip, 0.07f + 0.05f * R(), new Vector3(-dir.z, 0f, dir.x));
            }
            if (flowers != null)
            {
                int heads = 2 + rng.Next(3);
                var fc = flowers[rng.Next(flowers.Length)];
                for (int i = 0; i < heads; i++)
                {
                    float a = R() * Mathf.PI * 2f;
                    var p = b + new Vector3(Mathf.Cos(a) * 0.12f, height * (0.7f + 0.3f * R()), Mathf.Sin(a) * 0.12f);
                    mb.Color = Paint.Shade(fc, 0.92f + 0.12f * R());
                    mb.Blob(p, new Vector3(0.055f, 0.04f, 0.055f), 0, 0.1f, rng.Next(1000));
                }
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        internal static void Stone(MeshBuilder mb, System.Random rng, Vector3 b, float size)
        {
            float R() => (float)rng.NextDouble();
            var grey = Color.Lerp(new Color(0.6f, 0.59f, 0.57f), new Color(0.55f, 0.58f, 0.5f), R());
            mb.Color = Paint.Shade(grey, 0.9f + 0.2f * R());
            mb.Wind = 0f;
            mb.Blob(b + new Vector3(0f, size * 0.32f, 0f), new Vector3(size, size * 0.62f, size * (0.7f + 0.3f * R())), 0, 0.22f, rng.Next(1000), 0.55f);
        }

        /// <summary>
        /// A ground-cover bush in the prop bushes' look (drawn outlined): a scalloped mound of small leafy lumps around a
        /// lighter crown, now and then dotted with blossoms or berries.
        /// </summary>
        internal static void Bush(MeshBuilder mb, System.Random rng, Vector3 b, float size, Color col)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0.18f; mb.WindGradient = true; mb.WindY0 = b.y; mb.WindY1 = b.y + size * 1.4f;
            var light = Color.Lerp(col, new Color(0.78f, 0.86f, 0.46f), 0.35f);
            int n = 4 + rng.Next(3);
            float turn = R() * Mathf.PI * 2f;
            var lumps = new Vector4[n + 1];
            for (int i = 0; i < n; i++)
            {
                float a = turn + i * Mathf.PI * 2f / n + (R() - 0.5f) * 0.5f;
                float d = size * (0.42f + 0.18f * R());
                float r = size * (0.36f + 0.14f * R());
                var c = b + new Vector3(Mathf.Cos(a) * d, r * 0.82f, Mathf.Sin(a) * d * 0.8f);
                lumps[i] = new Vector4(c.x, c.y, c.z, r);
                mb.Color = Paint.Shade(col, 0.9f + 0.16f * R());
                mb.Blob(c, new Vector3(r, r * 0.88f, r), 1, 0.12f, rng.Next(1000), 0.35f);
            }
            // the crown: lighter, catching the light
            float cr = size * (0.5f + 0.1f * R());
            var top = b + new Vector3((R() - 0.5f) * size * 0.2f, size * 0.62f + cr * 0.45f, (R() - 0.5f) * size * 0.15f);
            lumps[n] = new Vector4(top.x, top.y, top.z, cr);
            mb.Color = Paint.Shade(light, 0.98f + 0.08f * R());
            mb.Blob(top, new Vector3(cr, cr * 0.85f, cr), 1, 0.12f, rng.Next(1000), 0.2f);
            if (R() < 0.4f)
            {
                var dots = new[] { new Color(0.96f, 0.66f, 0.76f), new Color(1f, 0.96f, 0.88f), new Color(0.86f, 0.3f, 0.32f), new Color(0.72f, 0.62f, 0.94f) };
                var dc = dots[rng.Next(dots.Length)];
                int k = 5 + rng.Next(4);
                for (int i = 0; i < k; i++)
                {
                    var l = lumps[rng.Next(lumps.Length)];
                    float a = R() * Mathf.PI * 2f, e = Mathf.Lerp(0.2f, 1.2f, R());
                    var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e));
                    if (dir.z > 0.3f) dir.z = -dir.z;   // on the side facing the camera (−Z in model space)
                    mb.Color = Paint.Shade(dc, 0.95f + 0.1f * R());
                    mb.Blob(new Vector3(l.x, l.y, l.z) + dir * (l.w * 0.98f), new Vector3(0.055f, 0.05f, 0.055f), 0, 0.1f, rng.Next(1000));
                }
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        internal static void RoundTree(MeshBuilder mb, System.Random rng, Vector3 b, float height, Color leaf)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f; mb.WindGradient = false;
            mb.Color = Ui.Hex("#6b5040");
            float trunkH = height * 0.42f;
            mb.Cylinder(b, height * 0.045f, height * 0.03f, trunkH + 0.4f, 6);
            mb.Wind = 0.25f; mb.WindGradient = true; mb.WindY0 = b.y + trunkH; mb.WindY1 = b.y + height;
            float cr = height * 0.36f;
            mb.Color = Paint.Shade(leaf, 0.92f + 0.14f * R());
            mb.Blob(b + new Vector3(0f, trunkH + cr * 0.75f, 0f), new Vector3(cr, cr * 0.9f, cr), 1, 0.14f, rng.Next(1000), 0.25f);
            if (R() < 0.6f)
            {
                mb.Color = Paint.Shade(leaf, 1.05f + 0.1f * R());
                float s = cr * (0.55f + 0.2f * R());
                mb.Blob(b + new Vector3((R() - 0.5f) * cr, trunkH + cr * 1.35f, (R() - 0.5f) * cr * 0.6f), new Vector3(s, s * 0.85f, s), 0, 0.14f, rng.Next(1000));
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        internal static void Pine(MeshBuilder mb, System.Random rng, Vector3 b, float height, Color leaf)
        {
            float R() => (float)rng.NextDouble();
            mb.Wind = 0f; mb.WindGradient = false;
            mb.Color = Ui.Hex("#5e4636");
            mb.Cylinder(b, height * 0.04f, height * 0.025f, height * 0.3f, 6);
            mb.Wind = 0.2f; mb.WindGradient = true; mb.WindY0 = b.y + height * 0.2f; mb.WindY1 = b.y + height;
            float r = height * 0.24f;
            for (int i = 0; i < 3; i++)
            {
                float y0 = height * (0.2f + i * 0.22f);
                float rr = r * (1f - i * 0.24f);
                mb.Color = Paint.Shade(leaf, 0.9f + 0.08f * i + 0.08f * R());
                mb.Cylinder(b + new Vector3(0f, y0, 0f), rr, 0f, height * (0.42f - i * 0.04f), 7, false, true, false);
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        // ================================================================== brook

        /// <summary>The brook (maps with a bridge): a translucent, gently flowing ribbon lying in its carved bed.</summary>
        public void BuildStream(Color horizon)
        {
            if (!HasStream)
            {
                if (waters.Count > 0 || style.WaterTable) BuildWaters(horizon);
                return;
            }
            var verts = new List<Vector3>();
            var cols = new List<Color32>();
            var uvs = new List<Vector2>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            float[] us = { -1f, -0.55f, 0f, 0.55f, 1f };
            float[] alphas = { 0f, 0.62f, 0.7f, 0.62f, 0f };
            var deep = Color.Lerp(new Color(0.34f, 0.55f, 0.6f), horizon, 0.18f);
            var shallow = Color.Lerp(new Color(0.55f, 0.72f, 0.7f), horizon, 0.18f);
            float y = -52f, along = 0f;
            int rows = 0;
            while (y <= D + 120f)
            {
                float sx = StreamX(y), hw = StreamHalfWidth(y);
                float f = StreamDepthFactor(y);
                float hc = BaseHeight(sx, y);
                float wh = hc - 0.12f * f + 0.012f * (1f - f);
                float fade = (1f - Smooth(D + 50f, D + 115f, y)) * Smooth(-52f, -40f, y);
                for (int k = 0; k < us.Length; k++)
                {
                    verts.Add(new Vector3(sx + us[k] * hw, y, -wh));
                    norms.Add(World3D.Up);
                    var c = Color.Lerp(shallow, deep, 1f - Mathf.Abs(us[k]));
                    c.a = alphas[k] * fade;
                    cols.Add(c);
                    uvs.Add(new Vector2(us[k] * hw / 3f + 0.5f, along / 3f));
                }
                rows++;
                float step = y > -6f && y < D + 6f ? 0.8f : 2f;
                float nextY = y + step;
                along += Vector2.Distance(new Vector2(sx, y), new Vector2(StreamX(nextY), nextY));
                y = nextY;
            }
            int per = us.Length;
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < per - 1; k++)
                {
                    int a = r * per + k, b = a + 1, c = a + per, d = c + 1;
                    // x right, y into the scene: (a, c, d) and (a, d, b) wind clockwise seen from above
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(b);
                }
            var m = new Mesh { name = "lv_brook" };
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetColors(cols);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0, true);
            owned.Add(m);
            var go = new GameObject("Brook");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var rr = go.AddComponent<MeshRenderer>();
            waterMat = Materials3D.LitTransparent(WorldTextures.Water);
            waterMat.SetFloat(Materials3D.EmissionId, 0.12f);
            rr.sharedMaterial = waterMat;
            rr.sortingOrder = -12;   // under the painted decals (MapView: −8 … −5)
            Quiet(rr);
        }

        /// <summary>Flows the brook's ripples (one material property per frame).</summary>
        public void Update(float dt)
        {
            if (riverMat != null)
            {
                riverScroll = Mathf.Repeat(riverScroll - dt * 0.12f, 1f);
                riverMat.mainTextureOffset = new Vector2(Mathf.Sin(Time.time * 0.3f) * 0.02f, riverScroll);
            }
            // still water: only a slow sway of its ripples
            if (stillMat != null) stillMat.mainTextureOffset = new Vector2(Mathf.Sin(Time.time * 0.11f) * 0.05f, Mathf.Cos(Time.time * 0.09f) * 0.05f);
            if (waterMat == null) return;
            waterScroll = Mathf.Repeat(waterScroll - dt * 0.12f, 1f);
            waterMat.mainTextureOffset = new Vector2(Mathf.Sin(Time.time * 0.3f) * 0.02f, waterScroll);
        }
    }
}
