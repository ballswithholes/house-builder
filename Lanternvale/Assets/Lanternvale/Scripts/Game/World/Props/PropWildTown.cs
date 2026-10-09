// Brightwater (river town): timber jetties and a moored rowing boat, tall pastel river houses, the town hall with its
// clock tower, the heron fountain, the stone bridge and the market awnings. Pastel plaster, slate-blue and terracotta
// roofs, honey-gold lanterns; the town's emblem is a heron under a lantern. Every building is finished on all four
// sides and centred on its pivot. Colliders, light offsets and the bridge/water rule: Docs/ArtKeys.md (props-wild block).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterWildTown(Dictionary<string, Recipe> r)
        {
            r["prop_dock"] = (art, seed) => Dock(art, seed, false);
            r["prop_dock_x"] = (art, seed) => Dock(art, seed, true);
            r["prop_boat"] = Boat;
            r["prop_river_house"] = RiverHouse;
            r["prop_town_hall"] = TownHall;
            r["prop_fountain"] = Fountain;
            r["prop_bridge_stone"] = (art, seed) => StoneBridge(art, seed, false);
            r["prop_bridge_stone_y"] = (art, seed) => StoneBridge(art, seed, true);
            r["prop_market_awning"] = MarketAwning;
        }

        static readonly Color[] TwPlaster = { Paint.Hex("#EBC0AE"), Paint.Hex("#F2DCA6"), Paint.Hex("#C5DDBE"), Paint.Hex("#BFD4EA") };
        static readonly Color TwSlate = Paint.Hex("#5F6F8C"), TwSlateAlt = Paint.Hex("#6C7D9A"), TwSlateRidge = Paint.Hex("#46526A");
        static readonly Color TwStone = Paint.Hex("#C9BBA4"), TwStoneLight = Paint.Hex("#DCCFB8"), TwStoneDark = Paint.Hex("#A89A86");
        static readonly Color TwHeronBlue = Paint.Hex("#3E6FA8"), TwWater = Paint.Hex("#86C4D8"), TwBronze = Paint.Hex("#6FA597");
        static readonly Color TwWood = Paint.Hex("#8C6A4E"), TwWoodWet = Paint.Hex("#5E4B3E");

        // ------------------------------------------------------------------ shared building parts

        /// <summary>Half-timbering on a front wall (outer face z = wallZ, facing −Z): posts every bay, sill and head rails, end braces.</summary>
        static void TwFrame(MeshBuilder mb, float x0, float x1, float y0, float y1, float wallZ, Color timber, int bays, bool braces = true)
        {
            var keep = mb.Color;
            mb.Color = timber;
            float z = wallZ - 0.025f;
            for (int i = 0; i <= bays; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)bays);
                mb.Box(new Vector3(x, (y0 + y1) * 0.5f, z), new Vector3(0.12f, y1 - y0, 0.05f));
            }
            mb.Box(new Vector3((x0 + x1) * 0.5f, y0 + 0.07f, z), new Vector3(x1 - x0 + 0.12f, 0.14f, 0.06f));
            mb.Box(new Vector3((x0 + x1) * 0.5f, y1 - 0.06f, z), new Vector3(x1 - x0 + 0.12f, 0.12f, 0.06f));
            mb.Box(new Vector3((x0 + x1) * 0.5f, y0 + (y1 - y0) * 0.3f, z), new Vector3(x1 - x0, 0.08f, 0.045f));
            if (braces && bays >= 2)
            {
                float bw = (x1 - x0) / bays;
                Beam(mb, new Vector3(x0 + 0.06f, y0 + 0.12f, z), new Vector3(x0 + bw - 0.06f, y1 - 0.1f, z), 0.1f, 0.045f, Vector3.back);
                Beam(mb, new Vector3(x1 - 0.06f, y0 + 0.12f, z), new Vector3(x1 - bw + 0.06f, y1 - 0.1f, z), 0.1f, 0.045f, Vector3.back);
            }
            mb.Color = keep;
        }

        /// <summary>A heron emblem disc on a wall (facing −Z): blue roundel, white heron, gold lantern.</summary>
        static void TwEmblem(MeshBuilder mb, Vector3 c, float r)
        {
            var keep = mb.Color;
            Ngon(mb, c, r * 1.12f, 12, Vector3.back, Vector3.up, 0f, Pal.Gold);
            Ngon(mb, c + Vector3.back * 0.01f, r, 12, Vector3.back, Vector3.up, 0f, TwHeronBlue);
            var z = c.z - 0.02f;
            mb.Color = Pal.Paper;
            Slab(mb, new[] { new Vector3(c.x - r * 0.15f, c.y + r * 0.55f, z), new Vector3(c.x + r * 0.05f, c.y + r * 0.1f, z), new Vector3(c.x - r * 0.05f, c.y - r * 0.55f, z), new Vector3(c.x - r * 0.3f, c.y - r * 0.1f, z) }, Vector3.back, 0.01f);
            Slab(mb, new[] { new Vector3(c.x - r * 0.15f, c.y + r * 0.55f, z), new Vector3(c.x + r * 0.35f, c.y + r * 0.42f, z), new Vector3(c.x + r * 0.32f, c.y + r * 0.36f, z) }, Vector3.back, 0.01f);
            mb.Color = Pal.Gold;
            mb.Box(new Vector3(c.x + r * 0.35f, c.y - r * 0.2f, z), new Vector3(r * 0.22f, r * 0.3f, 0.02f));
            mb.Color = keep;
        }

        /// <summary>A pot of flowers hanging from a wall bracket (wall face z = wallZ, facing −Z).</summary>
        static void TwHangingBasket(MeshBuilder mb, float x, float y, float wallZ, int flowerSeed)
        {
            var keep = mb.Color;
            mb.Color = Pal.Iron;
            Beam(mb, new Vector3(x, y + 0.3f, wallZ), new Vector3(x, y + 0.3f, wallZ - 0.4f), 0.03f, 0.03f, Vector3.right);
            mb.Segment(new Vector3(x, y + 0.3f, wallZ - 0.38f), new Vector3(x, y + 0.12f, wallZ - 0.38f), 0.008f, 0.008f, 3);
            mb.Color = Pal.Wood;
            mb.Push().Translate(x, y, wallZ - 0.38f);
            mb.Lathe(new[] { new Vector2(0.06f, -0.04f), new Vector2(0.16f, 0.06f), new Vector2(0.17f, 0.1f) }, 7, false, true, false);
            mb.Pop();
            FacetBlob(mb, new Vector3(x, y + 0.12f, wallZ - 0.38f), new Vector3(0.2f, 0.12f, 0.2f), 0, 0.3f, flowerSeed, 0.3f, ByNormal(Pal.Leaf, Pal.LeafDark, Pal.Forest));
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 72f + flowerSeed * 29f) * Mathf.Deg2Rad;
                Bloom(mb, new Vector3(x + Mathf.Cos(a) * 0.15f, y + 0.14f + (i % 2) * 0.06f, wallZ - 0.38f + Mathf.Sin(a) * 0.15f), 0.06f,
                      new Vector3(Mathf.Cos(a), 0.8f, Mathf.Sin(a)), Pal.Flowers[(flowerSeed + i) % Pal.Flowers.Length], Pal.Honey, i * 40f);
                mb.Color = Pal.Leaf;
                mb.Segment(new Vector3(x + Mathf.Cos(a) * 0.17f, y + 0.06f, wallZ - 0.38f + Mathf.Sin(a) * 0.17f),
                           new Vector3(x + Mathf.Cos(a) * 0.22f, y - 0.22f - 0.08f * (i % 2), wallZ - 0.38f + Mathf.Sin(a) * 0.22f), 0.012f, 0.006f, 3, false, false);
            }
            mb.Color = keep;
        }

        // ------------------------------------------------------------------ dock (deck 2.2 × 5.2 m along local Z; walkable, no collider)

        static PropModel Dock(string art, int seed, bool alongX)
        {
            int b = Bucket(seed);
            string key = art + "#" + b;
            var rig = new Rig(art);
            var mf = rig.Body(LitMesh(key, true, lit => BuildDock(b, lit, alongX)));
            rig.Model.SetLit = MeshSwitch(mf, key, lit => BuildDock(b, lit, alongX));
            var lamp = new Vector3(-0.95f, 2.0f, 2.25f);
            rig.Light(alongX ? new Vector3(lamp.z, lamp.y, -lamp.x) : lamp);
            return rig.Done(2.0f);
        }

        static MeshBuilder BuildDock(int b, bool lit, bool alongX)
        {
            var mb = Builder(VariantSeed("prop_dock", b), 0.07f, 0.25f, 0.6f);
            int sd = VariantSeed("dock", b);
            if (alongX) mb.Push().Rotate(0f, 90f, 0f);
            const float HW = 1.1f, HL = 2.6f, top = 0.18f;
            // piles (down into the river bed), stringers, cross planks
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(-HL + 0.2f, HL - 0.2f, i / 3f);
                for (int s = -1; s <= 1; s += 2)
                {
                    float h = (i == 0 || i == 3) ? 0.6f + 0.08f * ((i + s + b) % 2) : 0.0f;
                    mb.Color = TwWoodWet;
                    mb.Segment(new Vector3(s * (HW + 0.02f), -1.2f, z), new Vector3(s * (HW + 0.02f), top + h, z), 0.1f, 0.095f, 7);
                    if (h > 0f)
                    {
                        mb.Color = Paint.Shade(TwWood, 0.9f);
                        mb.Cylinder(new Vector3(s * (HW + 0.02f), top + h, z), 0.1f, 0.08f, 0.05f, 7);
                    }
                    mb.Color = Paint.Hex("#7E9A5A");
                    mb.Torus(new Vector3(s * (HW + 0.02f), -0.08f, z), 0.1f, 0.03f, 7, 3);
                }
            }
            mb.Color = TwWoodWet;
            for (int s = -1; s <= 1; s += 2) mb.Box(new Vector3(s * (HW - 0.12f), top - 0.14f, 0f), new Vector3(0.14f, 0.14f, HL * 2f));
            int planks = 20;
            for (int i = 0; i < planks; i++)
            {
                float z = -HL + 2f * HL * (i + 0.5f) / planks;
                mb.Color = (i * 7 + b) % 5 == 0 ? Pal.WoodGrey : (i % 2 == 0 ? TwWood : Color.Lerp(TwWood, Pal.WoodLight, 0.35f));
                float twist = WildHash(i, 1, sd) * 1.6f;
                OBox(mb, new Vector3(WildHash(i, 2, sd) * 0.03f, top - 0.03f, z), new Vector3(HW * 2f + 0.1f + WildHash(i, 3, sd) * 0.08f, 0.06f, 2f * HL / planks - 0.03f), Quaternion.Euler(0f, twist, 0f));
            }
            // mooring rope wound about the far bollards, a coil on the deck
            mb.Color = Pal.RopeStraw;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Torus(new Vector3(s * (HW + 0.02f), top + 0.42f, HL - 0.2f), 0.11f, 0.025f, 8, 3);
                mb.Torus(new Vector3(s * (HW + 0.02f), top + 0.34f, HL - 0.2f), 0.11f, 0.025f, 8, 3);
            }
            Rope(mb, new Vector3(HW + 0.1f, top + 0.38f, HL - 0.2f), new Vector3(HW + 0.6f, -0.08f, HL + 0.4f), 0.1f, 0.022f, 5, 3);
            for (int k = 0; k < 3; k++) mb.Torus(new Vector3(0.45f, top + 0.03f + k * 0.04f, 1.2f), 0.2f - k * 0.03f, 0.03f, 10, 3);
            // ladder down into the water at the far end
            mb.Color = TwWood;
            for (int s = -1; s <= 1; s += 2) mb.Segment(new Vector3(-0.2f + s * 0.22f, -0.9f, HL + 0.05f), new Vector3(-0.2f + s * 0.22f, top + 0.45f, HL + 0.05f), 0.035f, 0.035f, 5);
            for (int k = 0; k < 4; k++) mb.Box(new Vector3(-0.2f, -0.6f + k * 0.28f, HL + 0.05f), new Vector3(0.44f, 0.04f, 0.05f));
            // lamp post at the far corner with a hanging lantern
            mb.Color = Pal.TimberDark;
            mb.Segment(new Vector3(-0.95f, top, 2.25f), new Vector3(-0.95f, 2.45f, 2.25f), 0.06f, 0.05f, 6);
            Beam(mb, new Vector3(-0.95f, 2.4f, 2.25f), new Vector3(-0.55f, 2.4f, 2.25f), 0.05f, 0.05f);
            mb.Color = Pal.Iron;
            mb.Segment(new Vector3(-0.6f, 2.38f, 2.25f), new Vector3(-0.6f, 2.25f, 2.25f), 0.008f, 0.008f, 3);
            IronLantern(mb, new Vector3(-0.6f, 2.05f, 2.25f), 1.1f, lit);
            // cargo at the landward end: crates, a barrel, a fish basket and a folded net
            CrateGeom(mb, new Vector3(0.62f, top, -1.95f), 0.55f, Pal.WoodLight, 8f, true);
            CrateGeom(mb, new Vector3(0.65f, top + 0.55f, -1.95f), 0.42f, Pal.Wood, -12f, true);
            BarrelGeom(mb, new Vector3(-0.6f, top, -2.1f), 0.28f, 0.7f, Pal.Wood, true);
            mb.Color = Pal.Burlap;
            mb.Push().Translate(-0.55f, top, -1.35f);
            mb.Lathe(new[] { new Vector2(0.16f, 0f), new Vector2(0.22f, 0.22f), new Vector2(0.21f, 0.26f) }, 8, false, true, false);
            mb.Pop();
            mb.Color = Paint.Hex("#B9C3C8");
            for (int i = 0; i < 3; i++)
                mb.Sphere(new Vector3(-0.6f + i * 0.06f, top + 0.27f, -1.38f + (i % 2) * 0.05f), new Vector3(0.05f, 0.03f, 0.13f), 6, 3, false);
            mb.Color = Paint.Hex("#7C8A6A");
            FacetBlob(mb, new Vector3(0.3f, top + 0.06f, 0.2f), new Vector3(0.45f, 0.08f, 0.32f), 0, 0.3f, sd, 0.4f, ByNormal(Paint.Hex("#8E9B78"), Paint.Hex("#6F7C5C"), Paint.Hex("#5A654A")));
            mb.Color = Pal.Vermilion;
            Gem(mb, new Vector3(0.55f, top + 0.13f, 0.1f), 0.05f);
            Gem(mb, new Vector3(0.12f, top + 0.12f, 0.35f), 0.045f);
            if (alongX) mb.Pop();
            return mb;
        }

        // ------------------------------------------------------------------ rowing boat (3.4 m along X; collider 3.4 × 1.3 when beached)

        static PropModel Boat(string art, int seed) =>
            LitBuilding(art, seed, BuildBoat, 1.7f, new Vector3(-1.62f, 1.25f, 0f));

        static MeshBuilder BuildBoat(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_boat", b), 0.05f, 0.3f, 0.4f);
            var paint = new[] { Paint.Hex("#4F8FA8"), Paint.Hex("#C9654E"), Paint.Hex("#5E8F62"), Paint.Hex("#E1B550") }[b];
            var stripe = b == 3 ? Paint.Hex("#3E6FA8") : Pal.Cream;
            var tar = Paint.Hex("#4A3A36");
            var inner = Pal.WoodLight;
            const float L = 1.7f;
            const int NX = 11;
            // cross-sections along X: half beam and sheer (gunwale height) shrink and rise towards the pointed ends
            Vector2[] Prof(float x)
            {
                float t = x / L;
                float w = 0.62f * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t * t * t)) + 0.03f;
                float s = 0.36f + 0.2f * t * t + (t > 0f ? 0.06f * t * t : 0f);
                float k = -0.28f + 0.12f * t * t;
                float f = Mathf.Max(k + 0.1f, 0.06f);
                return new[]
                {
                    new Vector2(-w, s), new Vector2(-w * 0.96f, s - 0.2f), new Vector2(-w * 0.62f, k + 0.1f), new Vector2(0f, k),
                    new Vector2(w * 0.62f, k + 0.1f), new Vector2(w * 0.96f, s - 0.2f), new Vector2(w, s),
                    new Vector2(w - 0.05f, s), new Vector2(w * 0.7f, f), new Vector2(-w * 0.7f, f), new Vector2(-w + 0.05f, s),
                };
            }
            Color EdgeCol(int j)
            {
                if (j == 0 || j == 5) return stripe;
                if (j == 1 || j == 4) return paint;
                if (j == 2 || j == 3) return tar;
                if (j == 6 || j == 10) return Pal.TimberDark;
                return j == 8 ? Paint.Shade(inner, 0.85f) : inner;
            }
            var rings = new List<Vector2[]>();
            var xs = new List<float>();
            for (int i = 0; i <= NX; i++) { float x = Mathf.Lerp(-L, L, i / (float)NX); xs.Add(x); rings.Add(Prof(x)); }
            for (int i = 0; i < NX; i++)
            {
                var A = rings[i]; var B = rings[i + 1];
                float xa = xs[i], xb = xs[i + 1];
                for (int j = 0; j < 11; j++)
                {
                    int jn = (j + 1) % 11;
                    var pa = new Vector3(xa, A[j].y, A[j].x); var pb = new Vector3(xa, A[jn].y, A[jn].x);
                    var qa = new Vector3(xb, B[j].y, B[j].x); var qb = new Vector3(xb, B[jn].y, B[jn].x);
                    var mid2 = (A[j] + A[jn]) * 0.5f;
                    Vector3 outward;
                    if (j < 6) outward = new Vector3(0f, mid2.y + 0.05f, mid2.x);                 // hull outside: away from the keel line
                    else if (j == 6 || j == 10) outward = Vector3.up;                               // gunwale rim
                    else outward = new Vector3(0f, 0.6f, 0f) - new Vector3(0f, mid2.y, mid2.x);     // the inside: towards the boat's middle
                    var c = EdgeCol(j);
                    float k = 1f + (mb.Random01() * 2f - 1f) * mb.Jitter;
                    QuadC(mb, pa, pb, qb, qa, outward, new Color(c.r * k, c.g * k, c.b * k, 1f));
                }
            }
            // stem and stern posts; a little carved heron head on the bow
            mb.Color = Pal.TimberDark;
            var bow = new Vector3(L + 0.02f, Prof(L).Length > 0 ? 0.62f : 0.6f, 0f);
            mb.Segment(new Vector3(L - 0.05f, -0.12f, 0f), bow + Vector3.up * 0.12f, 0.05f, 0.045f, 5);
            mb.Segment(new Vector3(-L + 0.05f, -0.12f, 0f), new Vector3(-L - 0.02f, 0.62f, 0f), 0.05f, 0.045f, 5);
            mb.Color = Pal.Paper;
            mb.Sphere(bow + new Vector3(0.02f, 0.2f, 0f), new Vector3(0.07f, 0.07f, 0.06f), 6, 4, false);
            mb.Color = Pal.Gold;
            mb.Segment(bow + new Vector3(0.06f, 0.21f, 0f), bow + new Vector3(0.24f, 0.18f, 0f), 0.025f, 0.004f, 4);
            mb.Color = Pal.Ink;
            Gem(mb, bow + new Vector3(0.05f, 0.23f, -0.055f), 0.012f);
            Gem(mb, bow + new Vector3(0.05f, 0.23f, 0.055f), 0.012f);
            // thwarts, oars, a coil of rope, a creel
            mb.Color = Pal.Wood;
            mb.Box(new Vector3(-0.55f, 0.28f, 0f), new Vector3(0.24f, 0.05f, 1.15f));
            mb.Box(new Vector3(0.6f, 0.3f, 0f), new Vector3(0.24f, 0.05f, 1.1f));
            mb.Box(new Vector3(-1.2f, 0.32f, 0f), new Vector3(0.36f, 0.05f, 0.7f));
            mb.Color = Pal.WoodLight;
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(-1.0f, 0.36f, s * 0.22f);
                var c = new Vector3(1.25f, 0.4f, s * 0.48f);
                mb.Segment(a, c, 0.03f, 0.03f, 5);
                OBox(mb, c + new Vector3(0.22f, 0.01f, s * 0.04f), new Vector3(0.5f, 0.02f, 0.16f), Quaternion.Euler(0f, s * -8f, 0f));
            }
            mb.Color = Pal.RopeStraw;
            for (int k = 0; k < 3; k++) mb.Torus(new Vector3(1.1f, 0.12f + k * 0.035f, 0f), 0.16f - k * 0.025f, 0.025f, 9, 3);
            mb.Color = Pal.Burlap;
            mb.Push().Translate(0.05f, 0.08f, 0.15f);
            mb.Lathe(new[] { new Vector2(0.13f, 0f), new Vector2(0.18f, 0.2f), new Vector2(0.16f, 0.25f) }, 7, false, true, false);
            mb.Pop();
            // stern pole with a paper lantern
            mb.Color = Pal.TimberDark;
            mb.Segment(new Vector3(-1.45f, 0.3f, 0f), new Vector3(-1.62f, 1.55f, 0f), 0.03f, 0.025f, 5);
            Beam(mb, new Vector3(-1.62f, 1.52f, 0f), new Vector3(-1.62f, 1.52f, 0f) + new Vector3(-0.02f, 0f, 0f), 0.03f, 0.03f);
            PaperLantern(mb, new Vector3(-1.62f, 1.25f, 0f), 0.13f, 0.26f, Pal.Paper, lit, Pal.TimberDark);
            // painted name board on both bows: a tiny white band with dark marks
            mb.Color = Pal.Ink;
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < 3; k++)
                {
                    var p = new Vector3(1.0f + k * 0.12f, 0.43f, s * 0.6f);
                    Ngon(mb, p, 0.03f, 4, new Vector3(0f, 0f, s), Vector3.up, 45f, Pal.Ink);
                }
            return mb;
        }

        // ------------------------------------------------------------------ river house (≈ 4.2 × 4.1 m, 8.4 m; collider 5.2 × 5.0, centred)

        const float RhX = 2.0f, RhZ = 1.8f, RhG = 2.5f, RhU = 4.7f, RhJet = 0.22f;

        static PropModel RiverHouse(string art, int seed) =>
            LitBuilding(art, seed, BuildRiverHouse, 2.4f,
                        new Vector3(0.9f, 1.25f, -RhZ - 0.1f), new Vector3(-0.2f, 2.15f, -RhZ - 0.45f), new Vector3(-0.95f, 3.6f, -RhZ - RhJet - 0.1f),
                        new Vector3(0.95f, 3.6f, -RhZ - RhJet - 0.1f), new Vector3(0f, 5.4f, -1.0f), new Vector3(-0.9f, 1.3f, RhZ + 0.1f));

        static MeshBuilder BuildRiverHouse(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_river_house", b), 0.05f, 0.3f, 1.0f);
            var plaster = TwPlaster[b];
            var timber = Pal.TimberDark;
            var shutter = new[] { Pal.Teal, Paint.Hex("#6F8FC4"), Paint.Hex("#C46B5B"), Paint.Hex("#7E9B5A") }[b];
            bool slate = b % 2 == 0;
            Color tile = slate ? TwSlate : Pal.Terracotta, tileAlt = slate ? TwSlateAlt : Paint.Shade(Pal.Terracotta, 1.08f), ridge = slate ? TwSlateRidge : Pal.TerracottaDark;
            // ground storey: dressed stone with a plinth
            mb.Color = TwStoneDark;
            mb.BoxOn(new Vector3(0f, 0f, 0f), new Vector3(RhX * 2f + 0.12f, 0.3f, RhZ * 2f + 0.12f));
            mb.Color = TwStone;
            mb.BoxOn(new Vector3(0f, 0.3f, 0f), new Vector3(RhX * 2f, RhG - 0.3f, RhZ * 2f));
            // quoins on all four corners
            for (int k = 0; k < 4; k++)
            {
                float y = 0.55f + k * 0.5f;
                bool wide = k % 2 == 0;
                mb.Color = TwStoneLight;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        mb.Box(new Vector3(sx * (RhX - (wide ? 0.2f : 0.13f)), y, sz * (RhZ + 0.015f)), new Vector3(wide ? 0.42f : 0.28f, 0.42f, 0.04f));
                        mb.Box(new Vector3(sx * (RhX + 0.015f), y, sz * (RhZ - (wide ? 0.13f : 0.2f))), new Vector3(0.04f, 0.42f, wide ? 0.28f : 0.42f));
                    }
            }
            // jettied upper storey (overhangs front and back), plaster and timber framing on every side
            float uz = RhZ + RhJet;
            mb.Color = timber;
            mb.Box(new Vector3(0f, RhG + 0.06f, 0f), new Vector3(RhX * 2f + 0.1f, 0.12f, uz * 2f + 0.06f));
            for (int i = 0; i < 6; i++)
                for (int s = -1; s <= 1; s += 2)
                    mb.Box(new Vector3(-RhX + 0.25f + i * (RhX * 2f - 0.5f) / 5f, RhG - 0.04f, s * (RhZ + RhJet * 0.5f)), new Vector3(0.1f, 0.1f, RhJet + 0.1f));
            mb.Color = plaster;
            mb.BoxOn(new Vector3(0f, RhG + 0.12f, 0f), new Vector3(RhX * 2f, RhU - RhG - 0.12f, uz * 2f));
            TwFrame(mb, -RhX + 0.06f, RhX - 0.06f, RhG + 0.12f, RhU, -uz, timber, 4);
            mb.Push().Rotate(0f, 180f, 0f);
            TwFrame(mb, -RhX + 0.06f, RhX - 0.06f, RhG + 0.12f, RhU, -uz, timber, 4);
            mb.Pop();
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Push().Rotate(0f, s * 90f, 0f);
                TwFrame(mb, -uz + 0.06f, uz - 0.06f, RhG + 0.12f, RhU, -RhX, timber, 3);
                mb.Pop();
            }
            // front: an arched door with a lantern, the shop window with a striped awning, a hanging fish sign
            float fz = -RhZ;
            Door(mb, -0.85f, 0.3f, 0.95f, 2.0f, fz, Paint.Shade(shutter, 0.8f), TwStoneLight, true, Pal.Brass);
            mb.Color = TwStoneLight;
            mb.BoxOn(new Vector3(-0.85f, 0f, fz - 0.25f), new Vector3(1.25f, 0.3f, 0.5f));
            mb.BoxOn(new Vector3(-0.85f, 0f, fz - 0.6f), new Vector3(1.05f, 0.15f, 0.3f));
            Window(mb, 0.9f, 1.25f, 1.15f, 0.9f, fz, lit, timber, null, true, true);
            var awn = new[] { Pal.Terracotta, Pal.Teal, Paint.Hex("#C9A04A"), TwHeronBlue }[b];
            for (int i = 0; i < 6; i++)
            {
                float x0 = 0.2f + i * 0.24f, x1 = x0 + 0.24f;
                mb.Color = i % 2 == 0 ? awn : Pal.Cream;
                Slab(mb, new[] { new Vector3(x0, 2.05f, fz - 0.02f), new Vector3(x1, 2.05f, fz - 0.02f), new Vector3(x1, 1.82f, fz - 0.55f), new Vector3(x0, 1.82f, fz - 0.55f) },
                     new Vector3(0f, 1f, -0.5f), 0.02f);
                Slab(mb, new[] { new Vector3(x0, 1.82f, fz - 0.55f), new Vector3(x1, 1.82f, fz - 0.55f), new Vector3((x0 + x1) * 0.5f, 1.68f, fz - 0.57f) }, Vector3.back, 0.015f);
            }
            mb.Color = Pal.Iron;
            Beam(mb, new Vector3(-0.2f, 2.42f, fz), new Vector3(-0.2f, 2.42f, fz - 0.4f), 0.03f, 0.03f, Vector3.right);
            IronLantern(mb, new Vector3(-0.2f, 2.15f, fz - 0.42f), 1f, lit);
            // the fish sign on a bracket at the left corner
            mb.Color = Pal.Iron;
            Beam(mb, new Vector3(-RhX, 2.2f, fz + 0.3f), new Vector3(-RhX - 0.7f, 2.2f, fz + 0.3f), 0.035f, 0.035f);
            mb.Color = TwWood;
            mb.Box(new Vector3(-RhX - 0.45f, 1.92f, fz + 0.3f), new Vector3(0.55f, 0.36f, 0.04f));
            mb.Color = Paint.Hex("#B9C9D2");
            mb.Sphere(new Vector3(-RhX - 0.47f, 1.92f, fz + 0.27f), new Vector3(0.18f, 0.07f, 0.02f), 7, 3, false);
            Tri(mb, new Vector3(-RhX - 0.3f, 1.92f, fz + 0.265f), new Vector3(-RhX - 0.22f, 1.99f, fz + 0.265f), new Vector3(-RhX - 0.22f, 1.85f, fz + 0.265f), Vector3.back, Paint.Hex("#B9C9D2"));
            // upper front: two shuttered windows, the left with a little balcony of flowers
            Window(mb, -0.95f, 3.6f, 0.7f, 0.95f, -uz - 0.03f, lit, timber, shutter);
            Window(mb, 0.95f, 3.6f, 0.7f, 0.95f, -uz - 0.03f, lit, timber, shutter);
            mb.Color = timber;
            mb.Box(new Vector3(-0.95f, 2.98f, -uz - 0.28f), new Vector3(1.2f, 0.08f, 0.5f));
            for (int i = 0; i < 7; i++) mb.Box(new Vector3(-1.5f + i * 0.183f, 3.2f, -uz - 0.5f), new Vector3(0.04f, 0.4f, 0.04f));
            mb.Box(new Vector3(-0.95f, 3.42f, -uz - 0.5f), new Vector3(1.2f, 0.05f, 0.07f));
            FlowerBox(mb, -0.95f, 3.1f, 1.05f, -uz - 0.46f, Pal.Wood, b * 5 + 2);
            FlowerBox(mb, 0.95f, 3.0f, 0.8f, -uz - 0.03f, Pal.Wood, b * 5 + 3);
            // sides: one window up, one down each; ivy on the left stone wall; a rain barrel on the right
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Push().Rotate(0f, s * 90f, 0f);   // s = 1: local −Z faces −X
                Window(mb, s * 0.3f, 3.6f, 0.6f, 0.85f, -RhX - 0.03f, lit, timber, shutter);
                Window(mb, -s * 0.6f, 1.3f, 0.55f, 0.7f, -RhX, lit, timber, null);
                mb.Pop();
            }
            Ivy(mb, new Vector3(-RhX - 0.02f, 0.3f, 1.1f), new Vector3(-RhX - 0.02f, 2.3f, 0.6f), 14, 0.35f, Vector3.left);
            BarrelGeom(mb, new Vector3(RhX + 0.38f, 0f, 0.95f), 0.3f, 0.75f, Pal.Wood, true);
            mb.Color = Pal.Iron;
            mb.Segment(new Vector3(RhX + 0.06f, 4.6f, 0.95f), new Vector3(RhX + 0.06f, 0.8f, 0.95f), 0.035f, 0.035f, 5);
            mb.Segment(new Vector3(RhX + 0.06f, 0.82f, 0.95f), new Vector3(RhX + 0.3f, 0.78f, 0.95f), 0.035f, 0.035f, 5);
            // back: a door down to the water with steps and a mooring ring, two windows up with a hanging basket
            mb.Push().Rotate(0f, 180f, 0f);   // local −Z faces +Z, local x = −world x
            Door(mb, -0.6f, 0.3f, 0.85f, 1.9f, -RhZ, TwWood, TwStoneLight, true);
            Window(mb, 0.9f, 1.3f, 0.65f, 0.75f, -RhZ, lit, timber, shutter);
            Window(mb, -0.95f, 3.6f, 0.7f, 0.95f, -uz - 0.03f, lit, timber, shutter);
            Window(mb, 0.95f, 3.6f, 0.7f, 0.95f, -uz - 0.03f, lit, timber, shutter);
            TwHangingBasket(mb, 0f, 3.55f, -uz - 0.03f, b * 3 + 1);
            mb.Pop();
            mb.Color = TwStoneLight;
            mb.BoxOn(new Vector3(0.6f, 0f, RhZ + 0.25f), new Vector3(1.15f, 0.3f, 0.5f));
            mb.BoxOn(new Vector3(0.6f, 0f, RhZ + 0.6f), new Vector3(1.0f, 0.15f, 0.3f));
            mb.Color = Pal.Iron;
            mb.Push().Translate(-0.4f, 0.55f, RhZ + 0.04f).Rotate(90f, 0f, 0f);
            mb.Torus(Vector3.zero, 0.09f, 0.018f, 8, 3);
            mb.Pop();
            // steep roof with front and back dormers, gable walls with round windows, a chimney
            const float rh = 2.6f, over = 0.32f;
            float rz = uz;
            CourseRoof(mb, new Vector3(0f, RhU, 0f), RhX * 2f + 0.1f, rz * 2f, rh, over, 6, tile, tileAlt, ridge, !slate);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = plaster;
                GableWall(mb, s * (RhX + 0.0f), RhU, -rz, rz, rh - 0.08f, s);
                mb.Push().Rotate(0f, s * 90f, 0f);
                RoundWindow(mb, 0f, RhU + 0.85f, 0.26f, -RhX - 0.01f, lit, timber);
                mb.Pop();
            }
            float SlopeZ(float y) => -rz * (1f - (y - RhU) / rh);
            Dormer(mb, 0f, 1.0f, RhU + 0.25f, 0.85f, SlopeZ, plaster, tile, timber, lit, false);
            mb.Push().Rotate(0f, 180f, 0f);
            Dormer(mb, 0.5f, 0.9f, RhU + 0.25f, 0.8f, SlopeZ, plaster, tile, timber, lit, true);
            mb.Pop();
            Chimney(mb, new Vector3(1.25f, RhU + 1.2f, 0.55f), 0.55f, 0.5f, 2.0f, TwStoneLight, 2f);
            return mb;
        }

        // ------------------------------------------------------------------ town hall (≈ 8.8 × 4.8 m, 14 m tower; collider 10.8 × 6.8, centred)

        const float ThX = 4.4f, ThZ = 2.4f, ThG = 3.0f, ThU = 5.6f, ThArc = 0.8f;

        static PropModel TownHall(string art, int seed) =>
            LitBuilding(art, seed, BuildTownHall, 4.6f,
                        new Vector3(-0.975f, 2.2f, -ThZ + ThArc * 0.5f), new Vector3(0.975f, 2.2f, -ThZ + ThArc * 0.5f),
                        new Vector3(0f, 4.2f, -ThZ - 0.6f), new Vector3(-3.3f, 4.3f, -ThZ - 0.1f), new Vector3(3.3f, 4.3f, -ThZ - 0.1f),
                        new Vector3(0f, 9.1f, -0.6f - 1.0f), new Vector3(0f, 4.3f, ThZ + 0.1f));

        /// <summary>Fills the stone between a semicircular arch (centre cx, springing y0, radius r) and its square head, front and back, with the soffit.</summary>
        static void TwArchFill(MeshBuilder mb, float cx, float y0, float r, float zFront, float zBack, Color stone, Color soffit)
        {
            const int n = 8;
            float top = y0 + r;
            for (int i = 0; i < n; i++)
            {
                float a0 = Mathf.PI * (1f - i / (float)n), a1 = Mathf.PI * (1f - (i + 1) / (float)n);
                var p0 = new Vector2(cx + Mathf.Cos(a0) * r, y0 + Mathf.Sin(a0) * r);
                var p1 = new Vector2(cx + Mathf.Cos(a1) * r, y0 + Mathf.Sin(a1) * r);
                QuadC(mb, new Vector3(p0.x, p0.y, zFront), new Vector3(p1.x, p1.y, zFront), new Vector3(p1.x, top, zFront), new Vector3(p0.x, top, zFront), Vector3.back, stone);
                QuadC(mb, new Vector3(p0.x, p0.y, zBack), new Vector3(p1.x, p1.y, zBack), new Vector3(p1.x, top, zBack), new Vector3(p0.x, top, zBack), Vector3.forward, stone);
                var inward = new Vector3(cx, y0, 0f) - new Vector3((p0.x + p1.x) * 0.5f, (p0.y + p1.y) * 0.5f, 0f);
                QuadC(mb, new Vector3(p0.x, p0.y, zFront), new Vector3(p1.x, p1.y, zFront), new Vector3(p1.x, p1.y, zBack), new Vector3(p0.x, p0.y, zBack), inward, soffit);
            }
        }

        /// <summary>A round clock face on a wall (facing −Z at z), cream dial, gold ring and hands.</summary>
        static void TwClock(MeshBuilder mb, Vector3 c, float r, int b)
        {
            Ngon(mb, c, r + 0.08f, 16, Vector3.back, Vector3.up, 0f, Pal.Gold);
            Ngon(mb, c + Vector3.back * 0.01f, r, 16, Vector3.back, Vector3.up, 0f, Pal.Paper);
            var z = c.z - 0.025f;
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                Ngon(mb, new Vector3(c.x + Mathf.Cos(a) * r * 0.8f, c.y + Mathf.Sin(a) * r * 0.8f, z), 0.03f, 4, Vector3.back, Vector3.up, 45f, Pal.Ink);
            }
            mb.Color = Pal.Ink;
            float ha = (60f + b * 35f) * Mathf.Deg2Rad, ma = (100f - b * 70f) * Mathf.Deg2Rad;
            Beam(mb, new Vector3(c.x, c.y, z), new Vector3(c.x + Mathf.Cos(ha) * r * 0.5f, c.y + Mathf.Sin(ha) * r * 0.5f, z), 0.06f, 0.02f, Vector3.back);
            Beam(mb, new Vector3(c.x, c.y, z - 0.01f), new Vector3(c.x + Mathf.Cos(ma) * r * 0.75f, c.y + Mathf.Sin(ma) * r * 0.75f, z - 0.01f), 0.04f, 0.02f, Vector3.back);
            mb.Color = Pal.Gold;
            Gem(mb, new Vector3(c.x, c.y, z - 0.02f), 0.05f);
        }

        static MeshBuilder BuildTownHall(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_town_hall", b), 0.05f, 0.3f, 1.4f);
            var plaster = Color.Lerp(Pal.Cream, TwPlaster[b], 0.35f);
            var timber = Pal.TimberDark;
            var trim = TwHeronBlue;
            // plinth and the ground storey: a three-bay arcade (loggia) across the front
            mb.Color = TwStoneDark;
            mb.BoxOn(Vector3.zero, new Vector3(ThX * 2f + 0.16f, 0.35f, ThZ * 2f + 0.16f), TwStoneLight);
            mb.Color = TwStone;
            float inner = -ThZ + ThArc;
            mb.BoxOn(new Vector3(0f, 0.35f, ThArc * 0.5f), new Vector3(ThX * 2f, ThG - 0.35f, ThZ * 2f - ThArc));
            // the front piers and arches
            const float aSpring = 2.0f;
            // outer bays: solid walls with a window; middle bays: open arches
            for (int i = 0; i < 2; i++)
            {
                float x0 = i == 0 ? -ThX : 1.95f, x1 = i == 0 ? -1.95f : ThX;
                mb.Color = TwStone;
                mb.BoxOn(new Vector3((x0 + x1) * 0.5f, 0.35f, -ThZ + ThArc * 0.5f), new Vector3(x1 - x0, ThG - 0.35f, ThArc));
                Window(mb, (x0 + x1) * 0.5f, 1.6f, 0.8f, 1.1f, -ThZ, lit, timber, trim);
            }
            for (int i = 0; i < 3; i++)
            {
                float px = -1.95f + i * 1.95f;
                // piers between the arches
                if (i == 1)
                {
                    mb.Color = TwStone;
                    mb.BoxOn(new Vector3(0f, 0.35f, -ThZ + ThArc * 0.5f), new Vector3(0.5f, ThG - 0.35f, ThArc));
                }
                mb.Color = TwStoneLight;
                mb.BoxOn(new Vector3(px, 0.35f, -ThZ - 0.05f), new Vector3(0.62f, 0.25f, 0.2f));
            }
            for (int i = 0; i < 2; i++)
            {
                float cx = i == 0 ? -0.975f : 0.975f;
                float half = 0.725f;
                TwArchFill(mb, cx, aSpring, half, -ThZ, inner, TwStone, Paint.Shade(TwStone, 0.8f));
                mb.Color = TwStone;
                mb.Box(new Vector3(cx, (aSpring + half + ThG) * 0.5f, -ThZ + ThArc * 0.5f), new Vector3(half * 2f + 0.02f, ThG - aSpring - half, ThArc));
                // voussoirs around the arch, alternating light and dark
                for (int k = 0; k < 7; k++)
                {
                    float a0 = Mathf.PI * (1f - k / 7f), a1 = Mathf.PI * (1f - (k + 1) / 7f);
                    var p = new Vector3(cx + Mathf.Cos(a0) * (half + 0.08f), aSpring + Mathf.Sin(a0) * (half + 0.08f), -ThZ - 0.03f);
                    var q = new Vector3(cx + Mathf.Cos(a1) * (half + 0.08f), aSpring + Mathf.Sin(a1) * (half + 0.08f), -ThZ - 0.03f);
                    mb.Color = k % 2 == 0 ? TwStoneLight : Paint.Shade(TwStone, 0.92f);
                    Beam(mb, p, q, 0.2f, 0.07f, Vector3.back);
                }
                // a lantern hanging in each arch
                mb.Color = Pal.Iron;
                mb.Segment(new Vector3(cx, aSpring + half - 0.02f, -ThZ + ThArc * 0.5f), new Vector3(cx, aSpring + 0.35f, -ThZ + ThArc * 0.5f), 0.01f, 0.01f, 3);
                IronLantern(mb, new Vector3(cx, aSpring + 0.2f, -ThZ + ThArc * 0.5f), 1.2f, lit);
            }
            // the loggia's back wall: the great door and two windows, a vaulted ceiling shade
            Door(mb, 0f, 0.35f, 1.2f, 2.3f, inner, TwHeronBlue, TwStoneLight, true, Pal.Gold);
            Window(mb, -1.4f, 1.6f, 0.5f, 0.85f, inner, lit, timber, null, false, true);
            Window(mb, 1.4f, 1.6f, 0.5f, 0.85f, inner, lit, timber, null, false, true);
            mb.Color = Paint.Shade(TwStone, 0.75f);
            mb.Box(new Vector3(0f, ThG - 0.05f, -ThZ + ThArc * 0.5f), new Vector3(3.9f, 0.1f, ThArc));
            // broad front steps
            mb.Color = TwStoneLight;
            mb.BoxOn(new Vector3(0f, 0f, -ThZ - 0.3f), new Vector3(4.0f, 0.24f, 0.6f));
            mb.BoxOn(new Vector3(0f, 0f, -ThZ - 0.62f), new Vector3(3.6f, 0.12f, 0.35f));
            // string course, then the upper storey: plaster with timber framing, tall windows, a balcony over the arcade
            mb.Color = TwStoneLight;
            mb.Box(new Vector3(0f, ThG + 0.05f, 0f), new Vector3(ThX * 2f + 0.16f, 0.14f, ThZ * 2f + 0.16f));
            mb.Color = plaster;
            mb.BoxOn(new Vector3(0f, ThG + 0.12f, 0f), new Vector3(ThX * 2f, ThU - ThG - 0.12f, ThZ * 2f));
            TwFrame(mb, -ThX + 0.06f, ThX - 0.06f, ThG + 0.12f, ThU, -ThZ, timber, 8);
            mb.Push().Rotate(0f, 180f, 0f);
            TwFrame(mb, -ThX + 0.06f, ThX - 0.06f, ThG + 0.12f, ThU, -ThZ, timber, 8);
            mb.Pop();
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Push().Rotate(0f, s * 90f, 0f);
                TwFrame(mb, -ThZ + 0.06f, ThZ - 0.06f, ThG + 0.12f, ThU, -ThX, timber, 4);
                mb.Pop();
            }
            float[] wx = { -3.3f, -1.65f, 1.65f, 3.3f };
            foreach (var x in wx) Window(mb, x, 4.3f, 0.7f, 1.2f, -ThZ - 0.03f, lit, timber, trim);
            // balcony with a balustrade, the balcony door and the town's emblem over it
            Door(mb, 0f, ThG + 0.15f, 0.9f, 1.9f, -ThZ - 0.03f, TwHeronBlue, timber, true, Pal.Gold);
            mb.Color = TwStoneLight;
            mb.Box(new Vector3(0f, ThG + 0.1f, -ThZ - 0.45f), new Vector3(2.4f, 0.16f, 0.9f));
            for (int i = 0; i < 9; i++)
            {
                float x = -1.1f + i * 0.275f;
                mb.Color = TwStoneLight;
                mb.Push().Translate(x, ThG + 0.18f, -ThZ - 0.82f);
                mb.Lathe(new[] { new Vector2(0.06f, 0f), new Vector2(0.09f, 0.18f), new Vector2(0.05f, 0.38f), new Vector2(0.07f, 0.5f) }, 6, false, false, false);
                mb.Pop();
            }
            for (int s = -1; s <= 1; s += 2)
                mb.Box(new Vector3(s * 1.15f, ThG + 0.45f, -ThZ - 0.48f), new Vector3(0.1f, 0.55f, 0.8f));
            mb.Box(new Vector3(0f, ThG + 0.72f, -ThZ - 0.82f), new Vector3(2.4f, 0.08f, 0.16f));
            mb.Color = TwStoneDark;
            for (int s = -1; s <= 1; s += 2)
                Beam(mb, new Vector3(s * 0.9f, ThG - 0.45f, -ThZ), new Vector3(s * 0.9f, ThG + 0.02f, -ThZ - 0.8f), 0.14f, 0.14f, Vector3.right);
            TwEmblem(mb, new Vector3(0f, ThU - 0.35f, -ThZ - 0.05f), 0.28f);
            // two long banners hanging beside the balcony
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 0.82f;
                mb.Color = Pal.Gold;
                mb.Segment(new Vector3(x - 0.3f, ThU - 0.25f, -ThZ - 0.08f), new Vector3(x + 0.3f, ThU - 0.25f, -ThZ - 0.08f), 0.025f, 0.025f, 5);
                mb.Wind = 0.6f;
                mb.Color = TwHeronBlue;
                Slab(mb, new[] { new Vector3(x - 0.24f, ThU - 0.28f, -ThZ - 0.08f), new Vector3(x + 0.24f, ThU - 0.28f, -ThZ - 0.08f), new Vector3(x + 0.24f, ThG + 0.95f, -ThZ - 0.1f), new Vector3(x, ThG + 0.72f, -ThZ - 0.1f), new Vector3(x - 0.24f, ThG + 0.95f, -ThZ - 0.1f) }, Vector3.back, 0.015f);
                mb.Color = Pal.Gold;
                mb.Box(new Vector3(x, ThU - 0.85f, -ThZ - 0.12f), new Vector3(0.16f, 0.22f, 0.01f));
                mb.Wind = 0f;
            }
            // sides and back: windows on both storeys, a back door; chimneys at the ends
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Push().Rotate(0f, s * 90f, 0f);
                Window(mb, -0.9f, 4.3f, 0.65f, 1.1f, -ThX - 0.03f, lit, timber, trim);
                Window(mb, 0.9f, 4.3f, 0.65f, 1.1f, -ThX - 0.03f, lit, timber, trim);
                Window(mb, 0f, 1.6f, 0.7f, 1.0f, -ThX, lit, timber, null);
                mb.Pop();
            }
            mb.Push().Rotate(0f, 180f, 0f);
            foreach (var x in new[] { -3.3f, -1.65f, 0f, 1.65f, 3.3f }) Window(mb, x, 4.3f, 0.65f, 1.1f, -ThZ - 0.03f, lit, timber, trim);
            foreach (var x in new[] { -2.8f, 2.8f }) Window(mb, x, 1.6f, 0.75f, 1.0f, -ThZ, lit, timber, null);
            Door(mb, 0f, 0.35f, 1.0f, 2.1f, -ThZ, TwWood, TwStoneLight, true);
            mb.Pop();
            mb.Color = TwStoneLight;
            mb.BoxOn(new Vector3(0f, 0f, ThZ + 0.25f), new Vector3(1.6f, 0.3f, 0.5f));
            // roof
            const float rh = 2.4f, over = 0.4f;
            CourseRoof(mb, new Vector3(0f, ThU, 0f), ThX * 2f + 0.1f, ThZ * 2f, rh, over, 7, TwSlate, TwSlateAlt, TwSlateRidge);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = plaster;
                GableWall(mb, s * ThX, ThU, -ThZ, ThZ, rh - 0.08f, s);
                mb.Push().Rotate(0f, s * 90f, 0f);
                RoundWindow(mb, 0f, ThU + 0.8f, 0.3f, -ThX - 0.01f, lit, timber);
                mb.Pop();
                Chimney(mb, new Vector3(s * (ThX - 0.7f), ThU + 0.9f, 0.9f), 0.6f, 0.55f, 2.2f, TwStoneLight, s * 2f);
            }
            float SlopeZ(float y) => -ThZ * (1f - (y - ThU) / rh);
            Dormer(mb, -2.6f, 0.9f, ThU + 0.2f, 0.8f, SlopeZ, plaster, TwSlate, timber, lit, true);
            Dormer(mb, 2.6f, 0.9f, ThU + 0.2f, 0.8f, SlopeZ, plaster, TwSlate, timber, lit, true);
            mb.Push().Rotate(0f, 180f, 0f);
            Dormer(mb, -1.6f, 0.9f, ThU + 0.2f, 0.8f, SlopeZ, plaster, TwSlate, timber, lit, false);
            Dormer(mb, 1.6f, 0.9f, ThU + 0.2f, 0.8f, SlopeZ, plaster, TwSlate, timber, lit, false);
            mb.Pop();
            // the clock tower rising from the roof's front slope: clock faces on all four sides, an open belfry, a spire
            const float tz = -0.7f, tw = 1.9f, t0 = 5.2f, t1 = 10.0f, t2 = 11.6f;
            mb.Color = TwStone;
            mb.BoxOn(new Vector3(0f, t0, tz), new Vector3(tw, t1 - t0, tw));
            mb.Color = TwStoneLight;
            mb.Box(new Vector3(0f, t1 - 1.9f, tz), new Vector3(tw + 0.12f, 0.12f, tw + 0.12f));
            mb.Box(new Vector3(0f, t1, tz), new Vector3(tw + 0.2f, 0.16f, tw + 0.2f));
            for (int q = 0; q < 4; q++)
            {
                mb.Push().Translate(0f, 0f, tz).Rotate(0f, q * 90f, 0f);
                TwClock(mb, new Vector3(0f, 9.0f, -tw * 0.5f - 0.02f), 0.55f, b);
                if (q != 0) Window(mb, 0f, 7.3f, 0.35f, 0.6f, -tw * 0.5f, lit, timber, null, false, false);
                mb.Pop();
            }
            // belfry: corner posts, round arches, the bell
            mb.Color = TwStoneLight;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    mb.BoxOn(new Vector3(sx * (tw * 0.5f - 0.12f), t1 + 0.08f, tz + sz * (tw * 0.5f - 0.12f)), new Vector3(0.3f, t2 - t1 - 0.08f, 0.3f));
            for (int q = 0; q < 4; q++)
            {
                mb.Push().Translate(0f, 0f, tz).Rotate(0f, q * 90f, 0f);
                TwArchFill(mb, 0f, t2 - 0.7f, 0.67f, -tw * 0.5f, -tw * 0.5f + 0.25f, TwStoneLight, Paint.Shade(TwStoneLight, 0.8f));
                mb.Pop();
            }
            mb.Color = Pal.Brass;
            mb.Push().Translate(0f, t1 + 0.35f, tz);
            mb.Lathe(new[] { new Vector2(0.42f, 0f), new Vector2(0.38f, 0.1f), new Vector2(0.27f, 0.45f), new Vector2(0.24f, 0.75f), new Vector2(0.1f, 0.85f), new Vector2(0f, 0.86f) }, 10, true, true, false);
            mb.Pop();
            mb.Color = timber;
            mb.Box(new Vector3(0f, t1 + 1.25f, tz), new Vector3(tw - 0.3f, 0.12f, 0.14f));
            // spire: a four-sided slate pyramid with flared eaves, a gold ball and the heron weather vane
            mb.Color = TwSlateRidge;
            mb.Box(new Vector3(0f, t2 + 0.06f, tz), new Vector3(tw + 0.4f, 0.12f, tw + 0.4f));
            mb.Color = TwSlate;
            mb.Push().Translate(0f, t2 + 0.1f, tz).Rotate(0f, 45f, 0f);
            mb.Lathe(new[] { new Vector2((tw + 0.4f) * 0.707f, 0f), new Vector2((tw + 0.1f) * 0.6f, 0.45f), new Vector2(0f, 2.6f) }, 4, false, true, false,
                     new[] { TwSlateAlt, TwSlate, Paint.Shade(TwSlate, 1.1f) }, 0f);
            mb.Pop();
            mb.Color = Pal.Gold;
            mb.Sphere(new Vector3(0f, t2 + 2.75f, tz), new Vector3(0.13f, 0.13f, 0.13f), 7, 5, false);
            mb.Segment(new Vector3(0f, t2 + 2.7f, tz), new Vector3(0f, t2 + 3.3f, tz), 0.02f, 0.02f, 4);
            Slab(mb, new[] { new Vector3(-0.1f, t2 + 3.2f, tz), new Vector3(0.35f, t2 + 3.15f, tz), new Vector3(0.05f, t2 + 3.05f, tz) }, Vector3.back, 0.02f);
            Slab(mb, new[] { new Vector3(-0.1f, t2 + 3.2f, tz), new Vector3(-0.05f, t2 + 3.5f, tz), new Vector3(-0.3f, t2 + 3.25f, tz) }, Vector3.back, 0.02f);
            // pennants from the belfry corners
            mb.Wind = 1.2f;
            for (int s = -1; s <= 1; s += 2)
            {
                var p = new Vector3(s * (tw * 0.5f + 0.2f), t2 + 0.1f, tz - tw * 0.5f - 0.2f);
                mb.Color = Pal.TimberDark;
                mb.Segment(p, p + Vector3.up * 0.9f, 0.025f, 0.02f, 4);
                mb.Color = s < 0 ? TwHeronBlue : Pal.Gold;
                Slab(mb, new[] { p + Vector3.up * 0.88f, p + Vector3.up * 0.58f, p + new Vector3(s * 0.55f, 0.7f, -0.05f) }, Vector3.back, 0.01f);
            }
            mb.Wind = 0f;
            // flower tubs beside the steps
            Pot(mb, new Vector3(-2.3f, 0.35f, -ThZ - 0.35f), 0.26f, 0.42f, b * 2 + 1);
            Pot(mb, new Vector3(2.3f, 0.35f, -ThZ - 0.35f), 0.26f, 0.42f, b * 2 + 4);
            return mb;
        }

        // ------------------------------------------------------------------ heron fountain (Ø 2.9 m, 2.7 m; collider 3.0 × 3.0)

        static PropModel Fountain(string art, int seed)
        {
            var m = Simple(art, seed, BuildFountain, 1.5f);
            m.LightAnchors.Add(new Vector3(0f, 1.3f, -0.7f));
            return m;
        }

        static MeshBuilder BuildFountain(int b)
        {
            var mb = Builder(VariantSeed("prop_fountain", b), 0.05f, 0.3f, 0.5f);
            const int N = 12;
            const float RO = 1.45f, RI = 1.2f, H = 0.55f, WL = 0.42f;
            var stone = TwStone;
            var water = TwWater;
            // basin: a low wall with a rounded coping, steps of paving around it
            // a ring of paving round the basin
            for (int i = 0; i < N * 2; i++)
            {
                float a0 = (i * 360f / (N * 2)) * Mathf.Deg2Rad, a1 = ((i + 1) * 360f / (N * 2)) * Mathf.Deg2Rad;
                var col = i % 3 == 0 ? TwStoneLight : (i % 3 == 1 ? Paint.Shade(TwStoneLight, 0.94f) : Color.Lerp(TwStoneLight, TwStone, 0.5f));
                var o0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var o1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                QuadC(mb, o0 * (RO - 0.02f) + Vector3.up * 0.05f, o1 * (RO - 0.02f) + Vector3.up * 0.05f, o1 * (RO + 0.42f) + Vector3.up * 0.03f, o0 * (RO + 0.42f) + Vector3.up * 0.03f, Vector3.up, col);
                QuadC(mb, o0 * (RO + 0.42f) + Vector3.up * 0.03f, o1 * (RO + 0.42f) + Vector3.up * 0.03f, o1 * (RO + 0.44f) - Vector3.up * 0.03f, o0 * (RO + 0.44f) - Vector3.up * 0.03f, o0 + o1, Paint.Shade(TwStoneDark, 0.95f));
            }
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(RO, 0f), new Vector2(RO, H - 0.08f), new Vector2(RO + 0.08f, H - 0.06f), new Vector2(RO + 0.08f, H), new Vector2(RI - 0.05f, H), new Vector2(RI - 0.05f, H - 0.06f), new Vector2(RI, H - 0.08f), new Vector2(RI, WL - 0.1f) },
                     N, false, false, false, new[] { TwStoneDark, stone, TwStoneLight, TwStoneLight, TwStoneLight, stone, stone, Paint.Shade(stone, 0.7f) }, 15f);
            // carved panels on the basin wall
            for (int i = 0; i < N; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / N + 15f * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var c = n * (RO * Mathf.Cos(Mathf.PI / N) + 0.012f) + Vector3.up * (H * 0.45f);
                var t = new Vector3(-n.z, 0f, n.x);
                QuadC(mb, c - t * 0.24f - Vector3.up * 0.13f, c + t * 0.24f - Vector3.up * 0.13f, c + t * 0.24f + Vector3.up * 0.13f, c - t * 0.24f + Vector3.up * 0.13f, n, Paint.Shade(stone, 0.9f));
            }
            // water with ripples and lily pads (faintly luminous so it reads in shade)
            mb.Emission = 0.12f;
            mb.Color = water;
            mb.Disc(new Vector3(0f, WL, 0f), RI + 0.01f, N * 2);
            mb.Emission = 0.3f;
            mb.Color = Color.Lerp(water, Color.white, 0.5f);
            mb.Torus(new Vector3(0f, WL + 0.005f, 0f), 0.55f, 0.015f, 16, 3);
            mb.Torus(new Vector3(0f, WL + 0.005f, 0f), 0.85f, 0.012f, 18, 3);
            mb.Emission = 0f;
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + b * 40f + 30f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a) * 0.95f, WL + 0.012f, Mathf.Sin(a) * 0.95f);
                mb.Color = i % 2 == 0 ? Paint.Hex("#6E9A4E") : Paint.Hex("#86AE5A");
                var pts = NgonPoints(p, 0.16f, 0.16f, 8, Vector3.right, Vector3.forward, i * 40f, 30f, 360f);
                for (int k = 0; k < pts.Length - 1; k++) Tri(mb, p, pts[k], pts[k + 1], Vector3.up, mb.Color);
            }
            Bloom(mb, new Vector3(Mathf.Cos((b * 40f + 30f) * Mathf.Deg2Rad) * 0.95f, WL + 0.06f, Mathf.Sin((b * 40f + 30f) * Mathf.Deg2Rad) * 0.95f), 0.07f, Vector3.up, Paint.Hex("#F6B3C8"), Pal.Honey);
            // the pedestal, the lower bowl, the upper bowl
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.32f, WL - 0.05f), new Vector2(0.26f, WL + 0.15f), new Vector2(0.18f, 0.8f), new Vector2(0.2f, 1.0f), new Vector2(0.15f, 1.05f) }, 10, false, false, false,
                     new[] { TwStoneDark, stone, stone, TwStoneLight, stone }, 0f);
            mb.Lathe(new[] { new Vector2(0.12f, 1.0f), new Vector2(0.55f, 1.12f), new Vector2(0.72f, 1.28f), new Vector2(0.72f, 1.34f), new Vector2(0.62f, 1.34f), new Vector2(0.6f, 1.3f) }, 12, false, false, false,
                     new[] { Paint.Shade(stone, 0.8f), stone, stone, TwStoneLight, TwStoneLight, stone }, 0f);
            mb.Emission = 0.15f;
            mb.Color = water;
            mb.Disc(new Vector3(0f, 1.3f, 0f), 0.61f, 12);
            mb.Emission = 0f;
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.12f, 1.3f), new Vector2(0.1f, 1.7f), new Vector2(0.14f, 1.75f), new Vector2(0.3f, 1.84f), new Vector2(0.36f, 1.94f), new Vector2(0.3f, 1.94f) }, 10, false, false, false,
                     new[] { stone, stone, TwStoneLight, stone, TwStoneLight, TwStoneLight }, 0f);
            mb.Emission = 0.15f;
            mb.Color = water;
            mb.Disc(new Vector3(0f, 1.92f, 0f), 0.3f, 10);
            // the bronze heron, wings half raised, spouting a thin arc of water
            var bronze = Paint.Hsv(TwBronze, (b - 1.5f) * 4f);
            mb.Emission = 0f;
            mb.Color = bronze;
            var hb = new Vector3(0f, 2.18f, 0.02f);
            mb.Segment(new Vector3(0.02f, 1.93f, 0f), hb + new Vector3(0.01f, -0.1f, 0f), 0.025f, 0.02f, 4);
            mb.Segment(new Vector3(-0.06f, 1.93f, 0f), new Vector3(-0.1f, 2.0f, -0.03f), 0.02f, 0.02f, 4);
            mb.Sphere(hb, new Vector3(0.11f, 0.14f, 0.2f), 8, 5, false);
            var neck0 = hb + new Vector3(0f, 0.1f, -0.12f);
            var neck1 = neck0 + new Vector3(0f, 0.2f, -0.02f);
            var head = neck1 + new Vector3(0f, 0.08f, -0.04f);
            mb.Segment(neck0, neck1, 0.045f, 0.035f, 5);
            mb.Segment(neck1, head, 0.035f, 0.04f, 5);
            mb.Sphere(head, new Vector3(0.05f, 0.05f, 0.07f), 6, 4, false);
            mb.Color = Pal.Gold;
            mb.Segment(head + new Vector3(0f, 0.01f, -0.05f), head + new Vector3(0f, 0.08f, -0.22f), 0.018f, 0.003f, 4);
            mb.Color = bronze;
            for (int s = -1; s <= 1; s += 2)
                Slab(mb, new[] { hb + new Vector3(s * 0.08f, 0.06f, -0.08f), hb + new Vector3(s * 0.38f, 0.32f, 0.02f), hb + new Vector3(s * 0.32f, 0.12f, 0.2f), hb + new Vector3(s * 0.08f, 0f, 0.14f) },
                     new Vector3(s, 0.6f, 0f), 0.025f);
            mb.Segment(hb + new Vector3(0f, -0.02f, 0.15f), hb + new Vector3(0f, -0.08f, 0.34f), 0.05f, 0.01f, 4);
            // water: the heron's spout arcing down into the upper bowl, curtains from the bowls' lips
            mb.Emission = 0.35f;
            mb.Color = Color.Lerp(water, Color.white, 0.45f);
            var sp = head + new Vector3(0f, 0.1f, -0.24f);
            Vector3 prev = sp;
            for (int k = 1; k <= 5; k++)
            {
                float t = k / 5f;
                var p = sp + new Vector3(0f, 0.18f * t - 0.62f * t * t, -0.22f * t);
                mb.Segment(prev, p, 0.02f, 0.018f, 4, false, k == 5);
                prev = p;
            }
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / 8f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p0 = d * 0.74f + Vector3.up * 1.33f;
                var p1 = d * 0.9f + Vector3.up * 1.22f;
                var p2 = d * 1.0f + Vector3.up * 0.85f;
                var p3 = d * 1.04f + Vector3.up * (WL + 0.01f);
                mb.Segment(p0, p1, 0.03f, 0.028f, 4, false, false);
                mb.Segment(p1, p2, 0.028f, 0.026f, 4, false, false);
                mb.Segment(p2, p3, 0.026f, 0.04f, 4, false, true);
                {
                    var q0 = d * 0.36f + Vector3.up * 1.93f;
                    var q1 = d * 0.42f + Vector3.up * 1.32f;
                    mb.Segment(q0, q1, 0.025f, 0.025f, 4, false, true);
                }
            }
            mb.Emission = 0f;
            // a few coins on the coping, moss tufts in the joints
            mb.Color = Pal.Gold;
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 33f + b * 50f + 200f) * Mathf.Deg2Rad;
                mb.Cylinder(new Vector3(Mathf.Cos(a) * (RI + 0.12f), H, Mathf.Sin(a) * (RI + 0.12f)), 0.035f, 0.035f, 0.01f, 6);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f + b * 20f) * Mathf.Deg2Rad;
                FacetBlob(mb, new Vector3(Mathf.Cos(a) * (RO + 0.02f), 0.06f, Mathf.Sin(a) * (RO + 0.02f)), new Vector3(0.14f, 0.06f, 0.1f), 0, 0.25f, i + b * 7, 0.2f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            }
            return mb;
        }

        // ------------------------------------------------------------------ stone bridge (deck 7.6 m along local X; walkable, no collider)

        static PropModel StoneBridge(string art, int seed, bool alongY)
        {
            int b = Bucket(seed);
            string key = art + "#" + b;
            var rig = new Rig(art);
            var mf = rig.Body(LitMesh(key, true, lit => BuildStoneBridge(b, lit, alongY)));
            rig.Model.SetLit = MeshSwitch(mf, key, lit => BuildStoneBridge(b, lit, alongY));
            foreach (var p in BridgeLamps)
                rig.Light(alongY ? new Vector3(p.z, p.y, -p.x) : p);
            return rig.Done(3.8f);
        }

        static readonly Vector3[] BridgeLamps =
        {
            new Vector3(-3.95f, 1.75f, -1.75f), new Vector3(3.95f, 1.75f, -1.75f), new Vector3(-3.95f, 1.75f, 1.75f), new Vector3(3.95f, 1.75f, 1.75f),
        };

        static MeshBuilder BuildStoneBridge(int b, bool lit, bool alongY)
        {
            var mb = Builder(VariantSeed("prop_bridge_stone", b), 0.06f, 0.3f, 0.5f);
            int sd = VariantSeed("bridgestone", b);
            if (alongY) mb.Push().Rotate(0f, 90f, 0f);
            const float L = 3.8f, WD = 1.1f, PT = 0.32f, PH = 0.72f;
            var stone = Paint.Hsv(TwStone, (b - 1.5f) * 3f);
            var light = Paint.Hsv(TwStoneLight, (b - 1.5f) * 3f);
            var dark = Paint.Hsv(TwStoneDark, (b - 1.5f) * 3f);
            float Deck(float x) => 0.07f + 0.12f * (1f - (x / L) * (x / L));
            // parapet line: straight over the river, flaring out to the wing walls at both ends
            float Zp(float x) { float t = Mathf.InverseLerp(L - 1.3f, L, Mathf.Abs(x)); return WD + PT * 0.5f + t * t * 0.45f; }
            const int S = 16;
            // the deck: paving with joints
            for (int i = 0; i < S; i++)
            {
                float x0 = -L + 2f * L * i / S, x1 = -L + 2f * L * (i + 1) / S;
                float z0 = Zp(x0) - PT * 0.5f, z1 = Zp(x1) - PT * 0.5f;
                var col = i % 2 == 0 ? light : Color.Lerp(light, stone, 0.4f);
                QuadC(mb, new Vector3(x0, Deck(x0), -z0), new Vector3(x1, Deck(x1), -z1), new Vector3(x1, Deck(x1), z1), new Vector3(x0, Deck(x0), z0), Vector3.up, col);
                QuadC(mb, new Vector3(x0, Deck(x0), -z0), new Vector3(x1, Deck(x1), -z1), new Vector3(x1, Deck(x1) - 0.25f, -z1), new Vector3(x0, Deck(x0) - 0.25f, -z0), Vector3.back, dark);
                QuadC(mb, new Vector3(x0, Deck(x0), z0), new Vector3(x1, Deck(x1), z1), new Vector3(x1, Deck(x1) - 0.25f, z1), new Vector3(x0, Deck(x0) - 0.25f, z0), Vector3.forward, dark);
                mb.Color = Paint.Shade(stone, 0.85f);
                if (i > 0) OBox(mb, new Vector3(x0, Deck(x0) + 0.003f, 0f), new Vector3(0.03f, 0.01f, z0 * 2f - 0.1f), Quaternion.Euler(0f, 0f, Mathf.Atan(-0.24f * x0 / (L * L)) * Mathf.Rad2Deg));
            }
            // parapets: two courses and a coping, following the deck and the flare
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < S; i++)
                {
                    float x0 = -L + 2f * L * i / S, x1 = -L + 2f * L * (i + 1) / S;
                    var a = new Vector3(x0, Deck(x0), s * Zp(x0));
                    var c = new Vector3(x1, Deck(x1), s * Zp(x1));
                    var dir = c - a;
                    var mid = (a + c) * 0.5f;
                    var rot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up) * -s, Vector3.up);
                    mb.Push().Translate(mid).Rotate(rot);
                    mb.Color = (i + (s > 0 ? 1 : 0)) % 2 == 0 ? stone : Paint.Shade(stone, 0.94f);
                    mb.Box(new Vector3(0f, PH * 0.27f - 0.05f, 0f), new Vector3(dir.magnitude + 0.02f, PH * 0.54f + 0.1f, PT));
                    mb.Color = (i + (s > 0 ? 0 : 1)) % 2 == 0 ? Paint.Shade(stone, 1.04f) : stone;
                    mb.Box(new Vector3(0f, PH * 0.72f, 0f), new Vector3(dir.magnitude + 0.02f, PH * 0.36f, PT - 0.02f));
                    mb.Color = light;
                    mb.Box(new Vector3(0f, PH + 0.05f, 0f), new Vector3(dir.magnitude + 0.04f, 0.1f, PT + 0.1f), Color.Lerp(light, Pal.MossLight, (i * 7 + sd) % 5 == 0 ? 0.6f : 0f));
                    mb.Pop();
                }
            // the faces below the deck: masonry down into the water, cutwater piers, the town emblem on the keystone
            for (int s = -1; s <= 1; s += 2)
            {
                float zf = s * (Zp(0f) + PT * 0.5f + 0.01f);
                for (int i = 0; i < S; i++)
                {
                    float x0 = -L + 2f * L * i / S, x1 = -L + 2f * L * (i + 1) / S;
                    if (Mathf.Abs(x0) > L - 1.3f || Mathf.Abs(x1) > L - 1.3f) continue;
                    QuadC(mb, new Vector3(x0, Deck(x0) - 0.05f, zf), new Vector3(x1, Deck(x1) - 0.05f, zf), new Vector3(x1, -1.0f, zf), new Vector3(x0, -1.0f, zf), new Vector3(0f, 0f, s), i % 2 == 0 ? stone : Paint.Shade(stone, 0.95f));
                }
                mb.Color = light;
                mb.Push().Translate(0f, 0f, zf).Rotate(0f, s > 0 ? 180f : 0f, 0f);
                mb.Box(new Vector3(0f, Deck(0f) - 0.12f, -0.04f), new Vector3((L - 1.3f) * 2f, 0.12f, 0.08f));
                mb.Pop();
                // cutwaters
                foreach (float px in new[] { -1.25f, 1.25f })
                {
                    var tip = new Vector3(px, 0f, zf + s * 0.65f);
                    var l = new Vector3(px - 0.42f, 0f, zf);
                    var r = new Vector3(px + 0.42f, 0f, zf);
                    var col = stone;
                    float yb = -1.0f, yt = Deck(px) - 0.2f;
                    QuadC(mb, l + Vector3.up * yb, tip + Vector3.up * yb, tip + Vector3.up * yt, l + Vector3.up * yt, new Vector3(-0.8f, 0f, s * 0.6f), col);
                    QuadC(mb, tip + Vector3.up * yb, r + Vector3.up * yb, r + Vector3.up * yt, tip + Vector3.up * yt, new Vector3(0.8f, 0f, s * 0.6f), Paint.Shade(col, 0.92f));
                    // pyramid cap
                    var apex = new Vector3(px, yt + 0.32f, zf + s * 0.05f);
                    Tri(mb, l + Vector3.up * yt, tip + Vector3.up * yt, apex, new Vector3(-0.6f, 0.6f, s * 0.6f), light);
                    Tri(mb, tip + Vector3.up * yt, r + Vector3.up * yt, apex, new Vector3(0.6f, 0.6f, s * 0.6f), Paint.Shade(light, 0.94f));
                    // a moss line at the waterline
                    mb.Color = Pal.Moss;
                    mb.Segment(l + new Vector3(0f, -0.08f, s * 0.02f), tip + new Vector3(0f, -0.08f, s * 0.02f), 0.035f, 0.035f, 3);
                    mb.Segment(tip + new Vector3(0f, -0.08f, s * 0.02f), r + new Vector3(0f, -0.08f, s * 0.02f), 0.035f, 0.035f, 3);
                }
                mb.Push().Translate(0f, 0f, zf).Rotate(0f, s > 0 ? 180f : 0f, 0f);
                TwEmblem(mb, new Vector3(0f, Deck(0f) + 0.32f, -PT * 0.0f - 0.02f), 0.2f);
                mb.Pop();
                // ivy trailing over the parapet towards the water
                Ivy(mb, new Vector3(-2.0f + s * 0.4f, Deck(-2.0f) + PH, s * (Zp(-2f) + PT * 0.5f + 0.02f)), new Vector3(-1.8f + s * 0.4f, -0.05f, s * (Zp(-2f) + PT * 0.5f + 0.02f)), 9, 0.25f, new Vector3(0f, 0f, s));
            }
            // wing-wall ends: square newel pillars with lantern posts
            foreach (var lp in BridgeLamps)
            {
                float x = lp.x, z = lp.z;
                mb.Color = stone;
                mb.BoxOn(new Vector3(x, -0.05f, z), new Vector3(0.52f, 1.0f, 0.52f));
                mb.Color = light;
                mb.Box(new Vector3(x, 1.0f, z), new Vector3(0.62f, 0.1f, 0.62f));
                mb.Color = Pal.Iron;
                mb.Segment(new Vector3(x, 1.05f, z), new Vector3(x, 1.5f, z), 0.04f, 0.035f, 6);
                IronLantern(mb, new Vector3(x, 1.65f, z), 1.25f, lit);
            }
            if (alongY) mb.Pop();
            return mb;
        }

        // ------------------------------------------------------------------ market awning (≈ 3.0 × 2.0 m, 2.7 m; collider 3.4 × 1.8, centred)

        static PropModel MarketAwning(string art, int seed) =>
            LitBuilding(art, seed, BuildMarketAwning, 1.7f, new Vector3(-1.45f, 2.05f, -0.75f), new Vector3(1.45f, 2.05f, -0.75f));

        static MeshBuilder BuildMarketAwning(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_market_awning", b), 0.06f, 0.3f, 0.6f);
            int sd = VariantSeed("awning", b);
            var c1 = new[] { Paint.Hex("#C9654E"), TwHeronBlue, Paint.Hex("#5E8F62"), Paint.Hex("#C9A04A") }[b];
            var c2 = Pal.Cream;
            const float HX = 1.5f, ZF = -0.75f, ZB = 0.85f, YF = 2.3f, YB = 2.65f;
            // posts
            mb.Color = Pal.Timber;
            foreach (var (x, z, y) in new[] { (-HX, ZF, YF), (HX, ZF, YF), (-HX, ZB, YB), (HX, ZB, YB) })
            {
                mb.Segment(new Vector3(x, 0f, z), new Vector3(x, y + 0.05f, z), 0.05f, 0.045f, 6);
                mb.Color = Pal.Brass;
                mb.Sphere(new Vector3(x, y + 0.1f, z), new Vector3(0.06f, 0.06f, 0.06f), 6, 4, false);
                mb.Color = Pal.Timber;
            }
            // the striped canopy: strips running front to back, sagging a little between the poles
            const int strips = 10;
            mb.Wind = 0.5f; mb.WindGradient = false;
            for (int i = 0; i < strips; i++)
            {
                float x0 = -HX - 0.1f + (2f * HX + 0.2f) * i / strips, x1 = -HX - 0.1f + (2f * HX + 0.2f) * (i + 1) / strips;
                float sag0 = 0.08f * Mathf.Sin(Mathf.PI * (i / (float)strips)), sag1 = 0.08f * Mathf.Sin(Mathf.PI * ((i + 1) / (float)strips));
                mb.Color = i % 2 == 0 ? c1 : c2;
                var f0 = new Vector3(x0, YF + 0.06f - sag0, ZF - 0.15f); var f1 = new Vector3(x1, YF + 0.06f - sag1, ZF - 0.15f);
                var m0 = new Vector3(x0, (YF + YB) * 0.5f + 0.02f - sag0 * 1.6f, (ZF + ZB) * 0.5f); var m1 = new Vector3(x1, (YF + YB) * 0.5f + 0.02f - sag1 * 1.6f, (ZF + ZB) * 0.5f);
                var b0 = new Vector3(x0, YB + 0.08f - sag0 * 0.5f, ZB + 0.12f); var b1 = new Vector3(x1, YB + 0.08f - sag1 * 0.5f, ZB + 0.12f);
                Slab(mb, new[] { f0, f1, m1, m0 }, new Vector3(0f, 1f, -0.3f), 0.02f);
                Slab(mb, new[] { m0, m1, b1, b0 }, new Vector3(0f, 1f, -0.3f), 0.02f);
                // scalloped valance at the front
                var v0 = f0; var v1 = f1;
                Slab(mb, new[] { v0, v1, v1 + Vector3.down * 0.1f, (v0 + v1) * 0.5f + Vector3.down * 0.2f, v0 + Vector3.down * 0.1f }, Vector3.back, 0.015f);
            }
            mb.Wind = 0f;
            // trestle counter with a cloth
            mb.Color = Pal.WoodLight;
            mb.Box(new Vector3(0f, 0.86f, -0.35f), new Vector3(2.6f, 0.06f, 0.85f));
            mb.Color = Pal.Timber;
            for (int s = -1; s <= 1; s += 2)
            {
                Beam(mb, new Vector3(s * 1.1f, 0f, -0.7f), new Vector3(s * 1.1f, 0.83f, -0.05f), 0.06f, 0.06f, Vector3.right);
                Beam(mb, new Vector3(s * 1.1f, 0f, 0f), new Vector3(s * 1.1f, 0.83f, -0.65f), 0.06f, 0.06f, Vector3.right);
            }
            mb.Color = c1;
            Slab(mb, new[] { new Vector3(-1.32f, 0.9f, -0.79f), new Vector3(1.32f, 0.9f, -0.79f), new Vector3(1.32f, 0.55f, -0.8f), new Vector3(-1.32f, 0.55f, -0.8f) }, Vector3.back, 0.015f);
            mb.Color = c2;
            for (int i = 0; i < 7; i++) mb.Box(new Vector3(-1.2f + i * 0.4f, 0.62f, -0.815f), new Vector3(0.18f, 0.12f, 0.01f));
            // goods: a slanted tray of river fish on leaves, crayfish in a basket, greens, apples, honey jars, a scale
            mb.Color = Pal.Wood;
            OBox(mb, new Vector3(-0.7f, 0.98f, -0.4f), new Vector3(1.0f, 0.06f, 0.6f), Quaternion.Euler(-14f, 0f, 0f));
            mb.Color = Pal.Leaf;
            OBox(mb, new Vector3(-0.7f, 1.02f, -0.41f), new Vector3(0.94f, 0.02f, 0.54f), Quaternion.Euler(-14f, 0f, 0f));
            for (int i = 0; i < 5; i++)
            {
                float x = -1.08f + i * 0.19f;
                var c = new Vector3(x, 1.06f + 0.0f, -0.41f);
                mb.Color = i % 2 == 0 ? Paint.Hex("#B9C9D2") : Paint.Hex("#C9B8A0");
                mb.Push().Translate(c).Rotate(-14f, 0f, 0f);
                mb.Sphere(Vector3.zero, new Vector3(0.06f, 0.035f, 0.21f), 7, 3, false);
                Tri(mb, new Vector3(0f, 0f, 0.2f), new Vector3(-0.06f, 0.01f, 0.3f), new Vector3(0.06f, 0.01f, 0.3f), Vector3.up, mb.Color);
                mb.Color = Pal.Ink;
                Gem(mb, new Vector3(0.035f, 0.02f, -0.15f), 0.012f);
                mb.Pop();
            }
            Basket(mb, new Vector3(0.25f, 0.89f, -0.45f), 0.2f, Pal.Apple, sd);
            Basket(mb, new Vector3(0.75f, 0.89f, -0.25f), 0.2f, Pal.Pear, sd + 1);
            mb.Color = Paint.Hex("#C94E3A");
            for (int i = 0; i < 4; i++) Gem(mb, new Vector3(0.18f + i * 0.06f, 1.08f, -0.47f + (i % 2) * 0.05f), new Vector3(0.05f, 0.025f, 0.035f));
            for (int i = 0; i < 3; i++)
            {
                mb.Color = Pal.Honey;
                mb.Emission = 0.15f;
                mb.Cylinder(new Vector3(1.05f + i * 0.13f - 0.13f, 0.89f, -0.6f), 0.05f, 0.05f, 0.13f, 6);
                mb.Emission = 0f;
                mb.Color = Pal.Paper;
                mb.Cylinder(new Vector3(1.05f + i * 0.13f - 0.13f, 1.02f, -0.6f), 0.055f, 0.055f, 0.02f, 6);
            }
            mb.Color = Pal.Brass;
            mb.Cylinder(new Vector3(-0.05f, 0.89f, -0.15f), 0.04f, 0.03f, 0.3f, 5);
            Beam(mb, new Vector3(-0.25f, 1.2f, -0.15f), new Vector3(0.15f, 1.2f, -0.15f), 0.025f, 0.025f);
            mb.Disc(new Vector3(-0.25f, 1.1f, -0.15f), 0.08f, 8);
            mb.Disc(new Vector3(0.15f, 1.1f, -0.15f), 0.08f, 8);
            // strings of onions and garlic hanging from the front rail, lanterns at the corners
            mb.Color = Pal.Timber;
            mb.Segment(new Vector3(-HX, YF - 0.05f, ZF), new Vector3(HX, YF - 0.05f, ZF), 0.03f, 0.03f, 5);
            for (int k = 0; k < 3; k++)
            {
                float x = -0.6f + k * 0.6f;
                mb.Color = Pal.RopeStraw;
                mb.Segment(new Vector3(x, YF - 0.05f, ZF), new Vector3(x, YF - 0.6f, ZF), 0.012f, 0.012f, 3);
                for (int j = 0; j < 5; j++)
                {
                    mb.Color = k == 1 ? Pal.Paper : Paint.Hex("#C98A4A");
                    Gem(mb, new Vector3(x + ((j % 2) - 0.5f) * 0.07f, YF - 0.18f - j * 0.09f, ZF - 0.02f), new Vector3(0.05f, 0.055f, 0.05f));
                }
            }
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = Pal.Iron;
                mb.Segment(new Vector3(s * (HX - 0.05f), YF - 0.05f, ZF), new Vector3(s * (HX - 0.05f), YF - 0.12f, ZF), 0.008f, 0.008f, 3);
                PaperLantern(mb, new Vector3(s * (HX - 0.05f), YF - 0.25f, ZF), 0.1f, 0.2f, Pal.Paper, lit, Pal.TimberDark);
            }
            // behind: crates, a barrel and a stool, a chalk sign at the front
            CrateGeom(mb, new Vector3(-0.9f, 0f, 0.45f), 0.55f, Pal.Wood, 6f, true);
            CrateGeom(mb, new Vector3(-0.85f, 0.55f, 0.45f), 0.45f, Pal.WoodLight, -10f, true);
            BarrelGeom(mb, new Vector3(0.95f, 0f, 0.45f), 0.28f, 0.72f, Pal.Wood, true);
            Sack(mb, new Vector3(0.2f, 0f, 0.5f), 0.26f, sd);
            mb.Color = Pal.Timber;
            mb.Cylinder(new Vector3(-0.1f, 0f, 0.05f), 0.16f, 0.16f, 0.5f, 6);
            mb.Color = Paint.Hex("#3E4A44");
            OBox(mb, new Vector3(1.3f, 0.42f, -1.05f), new Vector3(0.5f, 0.66f, 0.04f), Quaternion.Euler(-12f, -15f, 0f));
            mb.Color = Pal.Paper;
            for (int i = 0; i < 3; i++)
                OBox(mb, new Vector3(1.3f - 0.02f * i, 0.58f - i * 0.13f, -1.09f + i * 0.03f), new Vector3(0.3f - i * 0.05f, 0.03f, 0.01f), Quaternion.Euler(-12f, -15f, 0f));
            mb.Color = Pal.Timber;
            OBox(mb, new Vector3(1.3f, 0.38f, -0.95f), new Vector3(0.04f, 0.76f, 0.04f), Quaternion.Euler(20f, -15f, 0f));
            return mb;
        }
    }
}
