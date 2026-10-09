// Village props: market stall, well, fence, lamp post, barrel, crate, hay bale, signpost, notice board, bench, banner,
// cart, the old bridge (with its brook) and the treasure chest (lid hinge for MapView.SetChestOpen).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterVillage(Dictionary<string, Recipe> r)
        {
            r["prop_shop_stall"] = ShopStall;
            r["prop_well"] = Well;
            r["prop_fence"] = (art, seed) => Simple(art, seed, BuildFence, 1.5f);
            r["prop_lamp_post"] = LampPost;
            r["prop_barrel"] = (art, seed) => Simple(art, seed, BuildBarrel, 0.35f);
            r["prop_crate"] = (art, seed) => Simple(art, seed, BuildCrate, 0.4f);
            r["prop_hay"] = (art, seed) => Simple(art, seed, BuildHay, 0.55f);
            r["prop_signpost"] = (art, seed) => Simple(art, seed, BuildSignpost, 0.3f);
            r["prop_noticeboard"] = (art, seed) => Simple(art, seed, BuildNoticeboard, 0.8f);
            r["prop_bench"] = (art, seed) => Simple(art, seed, BuildBench, 0.7f);
            r["prop_banner"] = Banner;
            r["prop_cart"] = (art, seed) => Simple(art, seed, BuildCart, 1.0f);
            r["prop_bridge"] = Bridge;
            r["prop_chest"] = Chest;
        }

        /// <summary>A static single-mesh prop (one variant per seed bucket).</summary>
        static PropModel Simple(string art, int seed, System.Func<int, MeshBuilder> build, float radius, Skin skin = Skin.Outlined)
        {
            int b = Bucket(seed);
            var rig = new Rig(art);
            rig.Body(Cached(art + "#" + b, () => build(b)), skin);
            return rig.Done(radius);
        }

        // ------------------------------------------------------------------ market stall (collider 3.0 × 1.1)

        static PropModel ShopStall(string art, int seed) =>
            LitBuilding(art, seed, BuildStall, 1.5f, new Vector3(1.05f, 2.02f, -0.62f));

        static MeshBuilder BuildStall(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_shop_stall", b), 0.06f, 0.3f, 0.6f);
            var stripe = new[] { Pal.Terracotta, Pal.Teal, Paint.Hex("#C9A04A"), Pal.DustyBlue }[b];
            // counter (plank front) with a top board
            PlankWall(mb, new Vector3(0f, 0f, 0.08f), 2.36f, 0.9f, 0.76f, Pal.WoodLight, 0.24f);
            mb.Color = Pal.Wood;
            mb.BoxOn(new Vector3(0f, 0.9f, 0.08f), new Vector3(2.42f, 0.08f, 0.82f));
            // posts (front lower, back higher) and back shelves
            mb.Color = Pal.Timber;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.BoxOn(new Vector3(s * 1.24f, 0f, -0.22f), new Vector3(0.1f, 2.3f, 0.1f));
                mb.BoxOn(new Vector3(s * 1.24f, 0f, 0.9f), new Vector3(0.1f, 2.78f, 0.1f));
            }
            mb.Color = Pal.Wood;
            foreach (float y in new[] { 1.3f, 1.8f })
                mb.Box(new Vector3(0f, y, 0.86f), new Vector3(2.4f, 0.05f, 0.3f));
            mb.Box(new Vector3(0f, 1.6f, 0.99f), new Vector3(2.4f, 1.2f, 0.04f));
            Color[] jarCols = { Pal.Honey, Pal.Sage, Paint.Hex("#C05A6A"), Pal.DustyBlue, Pal.Pumpkin };
            for (int shelf = 0; shelf < 2; shelf++)
                for (int i = 0; i < 6; i++)
                {
                    float x = -1.0f + i * 0.4f + (mb.Random01() - 0.5f) * 0.08f, y = shelf == 0 ? 1.325f : 1.825f;
                    float h = 0.16f + mb.Random01() * 0.1f;
                    mb.Color = jarCols[(i + shelf * 2 + b) % jarCols.Length];
                    mb.Cylinder(new Vector3(x, y, 0.85f), 0.07f, 0.065f, h, 6);
                    mb.Color = Pal.WoodLight;
                    mb.Cylinder(new Vector3(x, y + h, 0.85f), 0.045f, 0.045f, 0.04f, 5);
                }
            // goods: baskets of apples, pears and squash, a few loose apples
            Basket(mb, new Vector3(-0.75f, 0.98f, 0.0f), 0.24f, Pal.Apple, b * 3 + 1);
            Basket(mb, new Vector3(0.05f, 0.98f, 0.05f), 0.22f, Pal.Pear, b * 3 + 2);
            for (int i = 0; i < 3; i++)
            {
                mb.Color = i == 1 ? Paint.Hex("#E9A84A") : Pal.Pumpkin;
                Pumpkin(mb, new Vector3(0.7f + i * 0.2f - (i == 2 ? 0.28f : 0f), 0.98f + (i == 2 ? 0.14f : 0f), -0.05f + (i == 1 ? 0.12f : 0f)), 0.12f);
            }
            mb.Color = Pal.Apple;
            Gem(mb, new Vector3(-0.3f, 1.03f, -0.18f), 0.05f);
            Gem(mb, new Vector3(-0.38f, 1.03f, -0.1f), 0.05f);
            // striped awning: high at the back, overhanging the front, with a scalloped valance
            var back = new Vector3(0f, 2.86f, 1.0f);
            var front = new Vector3(0f, 2.28f, -0.78f);
            var dir = (front - back).normalized;
            float len = (front - back).magnitude;
            var n = Vector3.Cross(Vector3.right, dir).normalized;
            if (n.y < 0f) n = -n;
            const int strips = 10;
            const float aw = 3.1f;
            for (int i = 0; i < strips; i++)
            {
                float x = -aw * 0.5f + aw * (i + 0.5f) / strips;
                mb.Color = i % 2 == 0 ? Pal.Cream : stripe;
                OBox(mb, (back + front) * 0.5f + new Vector3(x, 0f, 0f), new Vector3(aw / strips + 0.002f, 0.04f, len), Quaternion.LookRotation(dir, n));
                var top = front + new Vector3(x, 0f, 0f);
                var pts = new List<Vector3>
                {
                    top + new Vector3(-aw / strips * 0.5f, 0f, 0f), top + new Vector3(aw / strips * 0.5f, 0f, 0f),
                };
                pts.Add(top + new Vector3(aw / strips * 0.5f, -0.16f, 0f));
                var arc = NgonPoints(top + new Vector3(0f, -0.16f, 0f), aw / strips * 0.5f, 0.1f, 4, Vector3.right, Vector3.up, 0f, 0f, -180f);
                for (int k = 1; k < arc.Length - 1; k++) pts.Add(arc[k]);
                pts.Add(top + new Vector3(-aw / strips * 0.5f, -0.16f, 0f));
                Slab(mb, pts, Vector3.back, 0.025f);
            }
            // paper lantern hanging at the front corner
            mb.Color = Pal.Ink;
            mb.Segment(new Vector3(1.05f, 2.31f, -0.62f), new Vector3(1.05f, 2.2f, -0.62f), 0.012f, 0.012f, 3);
            PaperLantern(mb, new Vector3(1.05f, 2.02f, -0.62f), 0.14f, 0.3f, Paint.Hex("#F2D49A"), lit, Pal.TimberDark);
            return mb;
        }

        /// <summary>Woven basket heaped with fruit (gems) on a surface at `at`.</summary>
        static void Basket(MeshBuilder mb, Vector3 at, float r, Color fruit, int seed)
        {
            var keepC = mb.Color;
            mb.Color = Pal.WoodLight;
            mb.Push().Translate(at);
            mb.Lathe(new[] { new Vector2(r * 0.7f, 0f), new Vector2(r, r * 0.6f), new Vector2(r * 1.05f, r * 0.68f), new Vector2(r * 0.9f, r * 0.68f) }, 8, false, true, false,
                     new[] { Paint.Shade(Pal.WoodLight, 0.85f), Pal.WoodLight, Pal.Wood, Pal.Wood });
            mb.Pop();
            mb.Color = Paint.Shade(Pal.Wood, 0.8f);
            mb.Disc(at + new Vector3(0f, r * 0.5f, 0f), r * 0.92f, 8);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 2.4f + seed, rr = i == 0 ? 0f : r * 0.55f;
                mb.Color = Paint.Shade(fruit, 0.9f + 0.2f * mb.Random01());
                Gem(mb, at + new Vector3(Mathf.Cos(a) * rr, r * 0.68f + (i == 0 ? 0.08f : 0.02f), Mathf.Sin(a) * rr), r * 0.36f);
            }
            mb.Color = keepC;
        }

        /// <summary>Ribbed pumpkin / squash (colour = mb.Color) resting on `at`.</summary>
        static void Pumpkin(MeshBuilder mb, Vector3 at, float r)
        {
            var keepC = mb.Color;
            var c = mb.Color;
            mb.Push().Translate(at);
            mb.Lathe(new[] { new Vector2(r * 0.3f, 0f), new Vector2(r * 0.9f, r * 0.2f), new Vector2(r, r * 0.65f), new Vector2(r * 0.8f, r * 1.1f), new Vector2(r * 0.25f, r * 1.25f) }, 8, false, true, true,
                     new[] { Paint.Shade(c, 0.8f), c, Paint.Shade(c, 1.08f), c, Paint.Shade(c, 0.9f) });
            mb.Color = Pal.LeafDark;
            mb.Cylinder(new Vector3(0f, r * 1.2f, 0f), r * 0.12f, r * 0.08f, r * 0.35f, 4);
            mb.Pop();
            mb.Color = keepC;
        }

        // ------------------------------------------------------------------ well (collider 1.8 × 0.9)

        static PropModel Well(string art, int seed) => Simple(art, seed, BuildWell, 0.9f);

        static MeshBuilder BuildWell(int b)
        {
            var mb = Builder(VariantSeed("prop_well", b), 0.08f, 0.3f, 0.5f);
            const float ZC = 0.37f;
            mb.Push().Translate(0f, 0f, ZC);
            // stone ring with a rim, mossy here and there
            mb.Color = Pal.Stone;
            mb.Lathe(new[]
            {
                new Vector2(0.8f, 0f), new Vector2(0.79f, 0.25f), new Vector2(0.79f, 0.25f), new Vector2(0.78f, 0.5f), new Vector2(0.78f, 0.5f),
                new Vector2(0.77f, 0.7f), new Vector2(0.82f, 0.72f), new Vector2(0.82f, 0.84f), new Vector2(0.6f, 0.84f), new Vector2(0.6f, 0.3f),
            }, 10, false, false, false, new[]
            {
                Pal.StoneDark, Pal.Stone, Pal.StoneLight, Pal.StoneLight, Pal.Stone, Pal.Stone, Pal.StoneLight, Pal.StoneLight, Pal.StoneDark, Pal.StoneDark,
            });
            mb.Color = Pal.Water;
            mb.Emission = 0.2f;
            mb.Disc(new Vector3(0f, 0.32f, 0f), 0.6f, 10);
            mb.Emission = 0f;
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 95f + b * 40f + 20f) * Mathf.Deg2Rad;
                FacetBlob(mb, new Vector3(Mathf.Cos(a) * 0.71f, 0.85f, Mathf.Sin(a) * 0.71f), new Vector3(0.18f, 0.05f, 0.12f), 0, 0.25f, b * 7 + i, 0f,
                          ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            }
            Ivy(mb, new Vector3(-0.42f, 0.16f, -0.62f), new Vector3(-0.3f, 0.75f, -0.68f), 7, 0.2f, new Vector3(-0.4f, 0f, -1f).normalized);
            // posts, crank axle with rope, bucket
            mb.Color = Pal.Timber;
            for (int s = -1; s <= 1; s += 2) mb.BoxOn(new Vector3(s * 0.71f, 0.8f, 0f), new Vector3(0.12f, 1.45f, 0.12f));
            mb.Color = Pal.Wood;
            mb.Segment(new Vector3(-0.78f, 1.78f, 0f), new Vector3(0.84f, 1.78f, 0f), 0.06f, 0.06f, 6);
            mb.Color = Pal.RopeStraw;
            mb.Segment(new Vector3(-0.28f, 1.78f, 0f), new Vector3(0.28f, 1.78f, 0f), 0.1f, 0.1f, 7);
            mb.Segment(new Vector3(0.12f, 1.7f, -0.06f), new Vector3(0.12f, 1.3f, -0.06f), 0.014f, 0.014f, 3);
            mb.Color = Pal.Iron;
            Beam(mb, new Vector3(0.84f, 1.78f, 0f), new Vector3(0.84f, 1.5f, -0.12f), 0.04f, 0.04f, Vector3.right);
            mb.Color = Pal.TimberDark;
            mb.Segment(new Vector3(0.84f, 1.5f, -0.12f), new Vector3(0.98f, 1.5f, -0.12f), 0.03f, 0.03f, 5);
            mb.Color = Pal.Wood;
            mb.Push().Translate(0.12f, 1.02f, -0.06f);
            mb.Lathe(new[] { new Vector2(0.1f, 0f), new Vector2(0.13f, 0.22f), new Vector2(0.11f, 0.22f), new Vector2(0.1f, 0.05f) }, 7, false, true, false);
            mb.Color = Pal.Iron;
            mb.Lathe(new[] { new Vector2(0.117f, 0.12f), new Vector2(0.125f, 0.17f) }, 7, false, false, false);
            mb.Torus(new Vector3(0f, 0.22f, 0f), 0.02f, 0.008f, 4, 3);
            mb.Pop();
            mb.Push().Translate(0.12f, 1.24f, -0.06f).Rotate(90f, 0f, 0f);
            mb.Color = Pal.Iron;
            mb.Torus(Vector3.zero, 0.12f, 0.012f, 8, 3);
            mb.Pop();
            // little tiled roof
            var tile = b % 2 == 0 ? Pal.Terracotta : Paint.Hsv(Pal.TileBlue, b * 10f);
            CourseRoof(mb, new Vector3(0f, 2.22f, 0f), 1.55f, 1.1f, 0.55f, 0.2f, 3, tile, Paint.Shade(tile, 0.9f), Paint.Shade(tile, 0.75f));
            mb.Color = Pal.Timber;
            mb.Box(new Vector3(0f, 2.2f, 0f), new Vector3(1.6f, 0.1f, 0.12f));
            GableWall(mb, -0.72f, 2.22f, -0.5f, 0.5f, 0.45f, -1f);
            GableWall(mb, 0.72f, 2.22f, -0.5f, 0.5f, 0.45f, 1f);
            mb.Pop();
            return mb;
        }

        // ------------------------------------------------------------------ fence segment (3 m, collider 3.0 × 0.35)

        static MeshBuilder BuildFence(int b)
        {
            var mb = Builder(VariantSeed("prop_fence", b), 0.08f, 0.3f, 0.4f);
            var wood = Color.Lerp(Pal.WoodGrey, Pal.Wood, 0.3f + b * 0.15f);
            float[] xs = { -1.42f, 0f, 1.42f };
            foreach (float x in xs)
            {
                float h = 1.0f + mb.Random01() * 0.12f;
                mb.Color = wood;
                mb.Push().Translate(x, 0f, 0f).Rotate((mb.Random01() - 0.5f) * 5f, mb.Random01() * 20f, (mb.Random01() - 0.5f) * 6f);
                mb.BoxOn(Vector3.zero, new Vector3(0.11f, h, 0.11f));
                mb.Color = Paint.Shade(wood, 1.1f);
                mb.Cone(new Vector3(0f, h, 0f), 0.078f, 0.1f, 4);
                mb.Pop();
                Tuft(mb, new Vector3(x + 0.06f, 0f, -0.04f), 0.32f, 5, Pal.Leaf);
            }
            mb.Color = Paint.Shade(wood, 0.95f);
            for (int r = 0; r < 2; r++)
            {
                float y = r == 0 ? 0.42f : 0.82f;
                Beam(mb, new Vector3(-1.5f, y + (mb.Random01() - 0.5f) * 0.06f, -0.08f), new Vector3(1.5f, y + (mb.Random01() - 0.5f) * 0.06f, -0.08f), 0.06f, 0.1f, Vector3.up);
            }
            Tuft(mb, new Vector3(-0.7f, 0f, 0.02f), 0.26f, 4, Pal.Sage);
            return mb;
        }

        // ------------------------------------------------------------------ lamp post (collider 0.4 × 0.3)

        static PropModel LampPost(string art, int seed) =>
            LitBuilding(art, seed, BuildLampPost, 0.25f, new Vector3(0.5f, 2.27f, 0f));

        static MeshBuilder BuildLampPost(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_lamp_post", b), 0.06f, 0.3f, 0.4f);
            mb.Color = Pal.Stone;
            mb.Cylinder(Vector3.zero, 0.145f, 0.12f, 0.24f, 8);
            mb.Color = Pal.Timber;
            mb.BoxOn(new Vector3(0f, 0.22f, 0f), new Vector3(0.12f, 2.56f, 0.12f));
            mb.Color = Pal.TimberDark;
            mb.Cone(new Vector3(0f, 2.78f, 0f), 0.12f, 0.14f, 4);
            mb.Color = Pal.Timber;
            Beam(mb, new Vector3(-0.06f, 2.62f, 0f), new Vector3(0.6f, 2.62f, 0f), 0.08f, 0.08f, Vector3.forward);
            Beam(mb, new Vector3(0f, 2.28f, 0f), new Vector3(0.34f, 2.6f, 0f), 0.06f, 0.06f, Vector3.forward);
            mb.Color = Pal.Ink;
            mb.Segment(new Vector3(0.5f, 2.58f, 0f), new Vector3(0.5f, 2.46f, 0f), 0.012f, 0.012f, 3);
            PaperLantern(mb, new Vector3(0.5f, 2.27f, 0f), 0.16f, 0.34f, Paint.Hex("#F3DAA6"), lit, Pal.TimberDark);
            return mb;
        }

        // ------------------------------------------------------------------ barrel, crate, hay

        static MeshBuilder BuildBarrel(int b)
        {
            var mb = Builder(VariantSeed("prop_barrel", b), 0.06f, 0.3f, 0.4f);
            BarrelGeom(mb, new Vector3(0f, 0f, 0.05f), 0.31f, 0.95f, b % 2 == 0 ? Pal.Wood : Pal.WoodLight);
            if (b % 2 == 1)
                for (int i = 0; i < 4; i++)
                {
                    mb.Color = i == 3 ? Pal.Pear : Pal.Apple;
                    Gem(mb, new Vector3(-0.08f + (i % 2) * 0.15f, 0.97f, -0.03f + (i / 2) * 0.14f), 0.065f);
                }
            return mb;
        }

        static MeshBuilder BuildCrate(int b)
        {
            var mb = Builder(VariantSeed("prop_crate", b), 0.06f, 0.3f, 0.4f);
            CrateGeom(mb, new Vector3(0f, 0f, 0.16f), 0.64f, b % 2 == 0 ? Pal.WoodLight : Pal.Wood, (b - 1.5f) * 4f);
            if (b == 1 || b == 3)
            {
                mb.Color = Pal.Burlap;
                Sack(mb, new Vector3(0.05f, 0.64f, 0.18f), 0.2f, b);
            }
            return mb;
        }

        static MeshBuilder BuildHay(int b)
        {
            var mb = Builder(VariantSeed("prop_hay", b), 0.05f, 0.3f, 0.4f);
            const float r = 0.5f, len = 0.88f;
            mb.Push().Translate(0f, r, -0.19f).Rotate(90f, 0f, 0f);
            var light = Pal.ThatchLight;
            var mid = Pal.Thatch;
            var dark = Pal.ThatchDark;
            mb.Color = mid;
            mb.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.14f, 0f), new Vector2(0.14f, 0f), new Vector2(0.27f, 0f), new Vector2(0.27f, 0f), new Vector2(0.4f, 0.01f),
                new Vector2(0.4f, 0.01f), new Vector2(r, 0.07f), new Vector2(r, len - 0.07f), new Vector2(0.4f, len - 0.01f), new Vector2(0f, len),
            }, 12, false, false, false, new[] { light, light, mid, mid, light, light, dark, mid, mid, dark, mid }, 7f);
            mb.Color = Pal.Vermilion;
            mb.Torus(new Vector3(0f, len * 0.3f, 0f), r + 0.008f, 0.022f, 12, 3);
            mb.Torus(new Vector3(0f, len * 0.7f, 0f), r + 0.008f, 0.022f, 12, 3);
            mb.Pop();
            mb.Color = Pal.ThatchLight;
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 70f + b * 25f) * Mathf.Deg2Rad;
                var basePt = new Vector3(Mathf.Cos(a) * r * 0.95f, r + Mathf.Sin(a) * r * 0.95f, -0.19f + 0.15f + i * 0.13f);
                mb.Segment(basePt, basePt + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.13f + Vector3.up * 0.03f, 0.025f, 0.004f, 3, false, false);
            }
            return mb;
        }

        // ------------------------------------------------------------------ signpost, notice board, bench

        static MeshBuilder BuildSignpost(int b)
        {
            var mb = Builder(VariantSeed("prop_signpost", b), 0.07f, 0.3f, 0.4f);
            mb.Color = Pal.Timber;
            mb.BoxOn(Vector3.zero, new Vector3(0.12f, 2.05f, 0.12f));
            mb.Color = Pal.TimberDark;
            mb.Cone(new Vector3(0f, 2.05f, 0f), 0.11f, 0.16f, 4);
            float[] heights = { 1.8f, 1.55f, 1.3f };
            float[] yaws = { 12f + b * 5f, 168f - b * 6f, -28f + b * 4f };
            Color[] boards = { Pal.WoodLight, Paint.Shade(Pal.WoodLight, 0.92f), Pal.Wood };
            for (int i = 0; i < 3; i++)
            {
                mb.Push().Translate(0f, heights[i], 0f).Rotate(0f, yaws[i], 0f);
                mb.Color = boards[i];
                var arrow = new[] { new Vector3(0.05f, -0.09f, -0.07f), new Vector3(0.05f, 0.09f, -0.07f), new Vector3(0.58f, 0.09f, -0.07f), new Vector3(0.7f, 0f, -0.07f), new Vector3(0.58f, -0.09f, -0.07f) };
                Slab(mb, arrow, Vector3.back, 0.045f);
                mb.Color = Pal.Ink;
                for (int k = 0; k < 3; k++)
                    mb.Box(new Vector3(0.16f + k * 0.13f + (k == 2 ? 0.02f : 0f), (k % 2 == 0 ? 0.015f : -0.015f), -0.077f), new Vector3(0.08f - k * 0.015f, 0.025f, 0.01f));
                mb.Pop();
            }
            // a few stones at the foot
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + b * 30f) * Mathf.Deg2Rad;
                FacetBlob(mb, new Vector3(Mathf.Cos(a) * 0.14f, 0.04f, Mathf.Sin(a) * 0.1f), new Vector3(0.08f, 0.06f, 0.06f), 0, 0.2f, i + b * 3, 0.3f, Mossy(Pal.Stone, Pal.Moss));
            }
            return mb;
        }

        static MeshBuilder BuildNoticeboard(int b)
        {
            var mb = Builder(VariantSeed("prop_noticeboard", b), 0.06f, 0.3f, 0.4f);
            mb.Color = Pal.Timber;
            for (int s = -1; s <= 1; s += 2) mb.BoxOn(new Vector3(s * 0.7f, 0f, 0f), new Vector3(0.12f, 2.32f, 0.12f));
            mb.Color = Pal.WoodLight;
            mb.Box(new Vector3(0f, 1.48f, 0f), new Vector3(1.3f, 1.0f, 0.06f));
            mb.Color = Pal.TimberDark;
            mb.Box(new Vector3(0f, 2.0f, -0.01f), new Vector3(1.32f, 0.07f, 0.08f));
            mb.Box(new Vector3(0f, 0.96f, -0.01f), new Vector3(1.32f, 0.07f, 0.08f));
            var tile = Paint.Hsv(Pal.Terracotta, b * 12f - 10f);
            CourseRoof(mb, new Vector3(0f, 2.3f, 0f), 1.5f, 0.42f, 0.3f, 0.14f, 2, tile, Paint.Shade(tile, 0.9f), Paint.Shade(tile, 0.75f), false, false);
            Color[] papers = { Pal.Paper, Paint.Hex("#F6E7B0"), Paint.Hex("#DDE8F2"), Paint.Hex("#F2D6D2"), Pal.Cream };
            for (int i = 0; i < 6; i++)
            {
                float x = -0.45f + (i % 3) * 0.45f + (mb.Random01() - 0.5f) * 0.12f;
                float y = (i < 3 ? 1.72f : 1.24f) + (mb.Random01() - 0.5f) * 0.1f;
                float w = 0.24f + mb.Random01() * 0.1f, h = 0.28f + mb.Random01() * 0.1f;
                mb.Color = papers[(i + b) % papers.Length];
                OBox(mb, new Vector3(x, y, -0.036f - i * 0.002f), new Vector3(w, h, 0.008f), Quaternion.Euler(0f, 0f, (mb.Random01() - 0.5f) * 14f));
                mb.Color = Paint.Shade(Pal.Ink, 1.6f);
                for (int k = 0; k < 3; k++)
                    mb.Box(new Vector3(x - w * 0.15f + (k == 1 ? 0.03f : 0f), y + h * 0.2f - k * h * 0.18f, -0.045f - i * 0.002f), new Vector3(w * (k == 0 ? 0.6f : 0.45f), 0.018f, 0.004f));
                mb.Color = i % 2 == 0 ? Pal.Vermilion : Pal.Brass;
                Gem(mb, new Vector3(x, y + h * 0.42f, -0.05f - i * 0.002f), 0.022f);
            }
            return mb;
        }

        static MeshBuilder BuildBench(int b)
        {
            var mb = Builder(VariantSeed("prop_bench", b), 0.06f, 0.3f, 0.4f);
            var wood = b % 2 == 0 ? Pal.Wood : Pal.WoodLight;
            mb.Color = Pal.Timber;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.BoxOn(new Vector3(s * 0.55f, 0f, -0.08f), new Vector3(0.07f, 0.42f, 0.07f));
                mb.Push().Translate(s * 0.55f, 0f, 0.17f).Rotate(-8f, 0f, 0f);
                mb.BoxOn(Vector3.zero, new Vector3(0.07f, 0.88f, 0.07f));
                mb.Pop();
                mb.Box(new Vector3(s * 0.55f, 0.2f, 0.05f), new Vector3(0.05f, 0.05f, 0.3f));
                mb.Box(new Vector3(s * 0.6f, 0.64f, 0.03f), new Vector3(0.07f, 0.05f, 0.36f));
                mb.BoxOn(new Vector3(s * 0.6f, 0.44f, -0.1f), new Vector3(0.05f, 0.2f, 0.05f));
            }
            mb.Color = wood;
            for (int i = 0; i < 3; i++) mb.Box(new Vector3(0f, 0.44f, -0.1f + i * 0.12f), new Vector3(1.3f, 0.045f, 0.1f));
            for (int i = 0; i < 2; i++)
            {
                mb.Push().Translate(0f, 0.63f + i * 0.17f, 0.21f + i * 0.025f).Rotate(-8f, 0f, 0f);
                mb.Box(Vector3.zero, new Vector3(1.24f, 0.1f, 0.035f));
                mb.Pop();
            }
            return mb;
        }

        // ------------------------------------------------------------------ banner (cloth sways; collider 0.4 × 0.3)

        static PropModel Banner(string art, int seed)
        {
            var m = Simple(art, seed, BuildBanner, 0.3f);
            m.Sways = true;
            return m;
        }

        static MeshBuilder BuildBanner(int b)
        {
            var mb = Builder(VariantSeed("prop_banner", b), 0.05f, 0.3f, 0.4f);
            mb.Color = Pal.Stone;
            mb.Cylinder(Vector3.zero, 0.14f, 0.12f, 0.16f, 7);
            mb.Color = Pal.TimberDark;
            mb.Cylinder(new Vector3(0f, 0.12f, 0f), 0.055f, 0.045f, 2.95f, 6);
            mb.Color = Pal.Gold;
            Gem(mb, new Vector3(0f, 3.12f, 0f), new Vector3(0.07f, 0.1f, 0.07f));
            mb.Color = Pal.TimberDark;
            Beam(mb, new Vector3(-0.04f, 2.86f, 0f), new Vector3(0.82f, 2.86f, 0f), 0.05f, 0.05f, Vector3.forward);
            mb.Color = Pal.Gold;
            Gem(mb, new Vector3(0.84f, 2.86f, 0f), 0.04f);
            // the cloth is authored hanging "upwards" (y' = distance below the bar) so the wind gradient grows towards
            // the free bottom edge, then appended turned 180° about Z (x' = −x)
            var cloth = new MeshBuilder(VariantSeed("prop_banner_cloth", b)) { Jitter = 0.03f, Wind = 2.6f, WindGradient = true, WindY0 = 0f, WindY1 = 1.6f };
            var deep = new[] { Paint.Hex("#2F4E8E"), Paint.Hex("#2E6E6A"), Paint.Hex("#7A2E44"), Paint.Hex("#3E5A2E") }[b];
            float[] xs = { 0.1f, 0.15f, 0.42f, 0.69f, 0.74f };
            float[] ys = { 0f, 0.07f, 0.45f, 0.95f, 1.3f };
            const float t = 0.018f;
            for (int r = 0; r < ys.Length - 1; r++)
                for (int c = 0; c < xs.Length - 1; c++)
                {
                    bool trim = r == 0 || c == 0 || c == xs.Length - 2;
                    cloth.Color = trim ? Pal.Gold : deep;
                    Slab(cloth, new[]
                    {
                        new Vector3(-xs[c], ys[r], 0f), new Vector3(-xs[c + 1], ys[r], 0f), new Vector3(-xs[c + 1], ys[r + 1], 0f), new Vector3(-xs[c], ys[r + 1], 0f),
                    }, Vector3.back, t);
                }
            // swallowtail
            cloth.Color = Pal.Gold;
            Slab(cloth, new[] { new Vector3(-0.1f, 1.3f, 0f), new Vector3(-0.15f, 1.3f, 0f), new Vector3(-0.15f, 1.56f, 0f), new Vector3(-0.1f, 1.6f, 0f) }, Vector3.back, t);
            Slab(cloth, new[] { new Vector3(-0.69f, 1.3f, 0f), new Vector3(-0.74f, 1.3f, 0f), new Vector3(-0.74f, 1.6f, 0f), new Vector3(-0.69f, 1.56f, 0f) }, Vector3.back, t);
            cloth.Color = deep;
            Slab(cloth, new[] { new Vector3(-0.15f, 1.3f, 0f), new Vector3(-0.42f, 1.3f, 0f), new Vector3(-0.15f, 1.56f, 0f) }, Vector3.back, t);
            Slab(cloth, new[] { new Vector3(-0.42f, 1.3f, 0f), new Vector3(-0.69f, 1.3f, 0f), new Vector3(-0.69f, 1.56f, 0f) }, Vector3.back, t);
            // glowing lantern emblem
            cloth.Emission = 0.65f;
            QuadC(cloth, new Vector3(-0.35f, 0.55f, -0.006f), new Vector3(-0.49f, 0.55f, -0.006f), new Vector3(-0.49f, 0.78f, -0.006f), new Vector3(-0.35f, 0.78f, -0.006f), Vector3.back, Pal.Glow);
            Tri(cloth, new Vector3(-0.33f, 0.55f, -0.007f), new Vector3(-0.51f, 0.55f, -0.007f), new Vector3(-0.42f, 0.45f, -0.007f), Vector3.back, Pal.Honey);
            Tri(cloth, new Vector3(-0.33f, 0.78f, -0.007f), new Vector3(-0.51f, 0.78f, -0.007f), new Vector3(-0.42f, 0.86f, -0.007f), Vector3.back, Pal.Honey);
            cloth.Emission = 0f;
            mb.Push().Translate(0f, 2.84f, -0.01f).Rotate(0f, 0f, 180f);
            mb.Append(cloth);
            mb.Pop();
            return mb;
        }

        // ------------------------------------------------------------------ hand cart (collider 2.0 × 1.0)

        static MeshBuilder BuildCart(int b)
        {
            var mb = Builder(VariantSeed("prop_cart", b), 0.06f, 0.3f, 0.5f);
            var wood = b % 2 == 0 ? Pal.WoodLight : Pal.Wood;
            // bed with side boards
            mb.Color = Paint.Shade(wood, 0.9f);
            mb.Box(new Vector3(0f, 0.57f, 0f), new Vector3(1.44f, 0.06f, 0.74f));
            mb.Color = wood;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(0f, 0.74f, s * 0.36f), new Vector3(1.44f, 0.28f, 0.04f));
                mb.Box(new Vector3(s * 0.7f, 0.74f, 0f), new Vector3(0.04f, 0.28f, 0.7f));
            }
            mb.Color = Pal.Timber;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    mb.Box(new Vector3(sx * 0.7f, 0.76f, sz * 0.37f), new Vector3(0.06f, 0.4f, 0.06f));
            // handles, stand leg, axle
            for (int s = -1; s <= 1; s += 2)
                Beam(mb, new Vector3(0.6f, 0.58f, s * 0.26f), new Vector3(0.98f, 0.62f, s * 0.2f), 0.05f, 0.05f);
            mb.Segment(new Vector3(0.98f, 0.62f, -0.2f), new Vector3(0.98f, 0.62f, 0.2f), 0.03f, 0.03f, 5);
            Beam(mb, new Vector3(-0.6f, 0.55f, 0f), new Vector3(-0.64f, 0f, 0f), 0.07f, 0.07f, Vector3.forward);
            mb.Color = Pal.Iron;
            mb.Segment(new Vector3(-0.05f, 0.45f, -0.46f), new Vector3(-0.05f, 0.45f, 0.46f), 0.035f, 0.035f, 5);
            // spoked wheels
            for (int s = -1; s <= 1; s += 2)
            {
                var c = new Vector3(-0.05f, 0.45f, s * 0.45f);
                mb.Push().Translate(c).Rotate(90f, 0f, 0f);
                mb.Color = Pal.TimberDark;
                mb.Torus(Vector3.zero, 0.41f, 0.045f, 12, 4);
                mb.Color = Pal.Timber;
                mb.Cylinder(new Vector3(0f, -0.06f, 0f), 0.08f, 0.08f, 0.12f, 6);
                mb.Pop();
                mb.Color = Pal.Timber;
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f + 0.2f;
                    Beam(mb, c + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.07f, c + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.38f, 0.04f, 0.03f, Vector3.forward);
                }
            }
            // cargo
            Sack(mb, new Vector3(-0.38f, 0.6f, -0.08f), 0.25f, b + 2);
            Sack(mb, new Vector3(-0.1f, 0.6f, 0.12f), 0.22f, b + 5);
            CrateGeom(mb, new Vector3(0.35f, 0.6f, 0.02f), 0.42f, Pal.Wood, 10f, true);
            mb.Color = Pal.Pumpkin;
            Pumpkin(mb, new Vector3(0.33f, 1.02f, 0.0f), 0.14f);
            mb.Color = Paint.Hex("#E9A84A");
            Pumpkin(mb, new Vector3(0.0f, 0.6f, -0.22f), 0.13f);
            return mb;
        }

        // ------------------------------------------------------------------ the old bridge: low arched footbridge over a brook (no collider)

        /// <summary>
        /// The bridge model includes the brook it crosses (a 12 m water strip along local Z at y = 0.02 with pebbly banks).
        /// Turn this off if the world draws its own stream.
        /// </summary>
        // the World's terrain already carves and draws Whisperwood's brook under the bridge
        static readonly bool BridgeWithBrook = false;

        static PropModel Bridge(string art, int seed)
        {
            var m = Simple(art, seed, BuildBridge, 2.6f);
            // picking / labels / fades: the bridge itself, not the brook
            m.LocalBounds = new Bounds(new Vector3(0f, 0.68f, 0f), new Vector3(5.3f, 1.42f, 2.0f));
            m.Height = 1.37f;
            return m;
        }

        static MeshBuilder BuildBridge(int b)
        {
            var mb = Builder(VariantSeed("prop_bridge", b), 0.07f, 0.25f, 0.35f);
            const float L = 2.5f, Wd = 0.82f, rise = 0.13f;
            float DeckY(float x) => 0.06f + rise * (1f - (x / L) * (x / L));
            if (BridgeWithBrook) BuildBrook(mb, b, Wd);
            mb.AOStrength = 0.25f;
            // stone abutments
            mb.Color = Pal.Stone;
            for (int s = -1; s <= 1; s += 2) mb.BoxOn(new Vector3(s * (L - 0.15f), 0f, 0f), new Vector3(0.5f, 0.1f, Wd * 2f + 0.3f), Pal.StoneLight);
            // stringers along both edges, following the arch
            mb.Color = Pal.TimberDark;
            const int ss = 6;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < ss; i++)
                {
                    float xa = -L + 2f * L * i / ss, xb = -L + 2f * L * (i + 1) / ss;
                    Beam(mb, new Vector3(xa, DeckY(xa) - 0.1f, s * Wd), new Vector3(xb, DeckY(xb) - 0.1f, s * Wd), 0.1f, 0.16f);
                }
            // deck planks
            const int planks = 20;
            for (int i = 0; i < planks; i++)
            {
                float x = -L + 2f * L * (i + 0.5f) / planks;
                float slope = Mathf.Atan(-2f * rise * x / (L * L)) * Mathf.Rad2Deg;
                mb.Color = i % 3 == 0 ? Pal.WoodGrey : (i % 3 == 1 ? Pal.Wood : Paint.Shade(Pal.Wood, 0.9f));
                OBox(mb, new Vector3(x, DeckY(x) - 0.025f, 0f), new Vector3(2f * L / planks - 0.02f, 0.05f, Wd * 2f + 0.12f + (i % 2) * 0.06f), Quaternion.Euler(0f, 0f, slope));
            }
            // vermilion railings with bronze finials
            float[] posts = { -2.4f, -1.2f, 0f, 1.2f, 2.4f };
            const float rail = 0.95f, zr = Wd + 0.08f;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int i = 0; i < posts.Length; i++)
                {
                    float x = posts[i];
                    mb.Color = Pal.Vermilion;
                    mb.BoxOn(new Vector3(x, DeckY(x) - 0.12f, s * zr), new Vector3(0.11f, rail + 0.12f + (i == 0 || i == posts.Length - 1 ? 0.12f : 0f), 0.11f));
                    if (i == 0 || i == posts.Length - 1)
                    {
                        mb.Color = Pal.Brass;
                        float top = DeckY(x) + rail + 0.12f;
                        mb.Sphere(new Vector3(x, top + 0.07f, s * zr), new Vector3(0.08f, 0.08f, 0.08f), 6, 4, false);
                        mb.Cone(new Vector3(x, top + 0.13f, s * zr), 0.04f, 0.1f, 6);
                    }
                    if (i < posts.Length - 1)
                    {
                        float xn = posts[i + 1];
                        mb.Color = Pal.Vermilion;
                        Beam(mb, new Vector3(x, DeckY(x) + rail - 0.05f, s * zr), new Vector3(xn, DeckY(xn) + rail - 0.05f, s * zr), 0.08f, 0.08f);
                        mb.Color = Paint.Shade(Pal.Vermilion, 0.85f);
                        Beam(mb, new Vector3(x, DeckY(x) + rail * 0.5f, s * zr), new Vector3(xn, DeckY(xn) + rail * 0.5f, s * zr), 0.05f, 0.05f);
                    }
                }
            }
            return mb;
        }

        /// <summary>The brook under the bridge: a meandering water strip along Z with pebbles and reeds on its banks.</summary>
        static void BuildBrook(MeshBuilder mb, int b, float Wd)
        {
            const int seg = 12;
            const float z0 = -5.2f, z1 = 6.8f;
            float Mx(float z) => Mathf.Sin(z * 0.55f + b) * 0.25f;
            float Hw(float z) => 0.62f + Mathf.Sin(z * 0.9f + 1.3f + b) * 0.1f - Mathf.Clamp01((Mathf.Abs(z - 0.8f) - 4.8f) / 1.2f) * 0.4f;
            mb.AOStrength = 0f;
            mb.Emission = 0.12f;
            for (int i = 0; i < seg; i++)
            {
                float za = Mathf.Lerp(z0, z1, (float)i / seg), zb = Mathf.Lerp(z0, z1, (float)(i + 1) / seg);
                var col = Color.Lerp(Pal.Water, Paint.Hex("#A9D3DE"), 0.3f * Mathf.Sin(i * 1.7f) + 0.3f);
                QuadC(mb, new Vector3(Mx(za) - Hw(za), 0.02f, za), new Vector3(Mx(zb) - Hw(zb), 0.02f, zb), new Vector3(Mx(zb) + Hw(zb), 0.02f, zb), new Vector3(Mx(za) + Hw(za), 0.02f, za), Vector3.up, col);
            }
            mb.Emission = 0f;
            for (int i = 0; i < 26; i++)
            {
                float z = Mathf.Lerp(z0 + 0.3f, z1 - 0.3f, mb.Random01());
                if (Mathf.Abs(z) < Wd + 0.15f) continue;
                float side = mb.Random01() < 0.5f ? -1f : 1f;
                mb.Color = mb.Random01() < 0.6f ? Pal.StoneLight : Pal.Stone;
                Gem(mb, new Vector3(Mx(z) + side * (Hw(z) + 0.02f), 0.03f, z), new Vector3(0.09f + mb.Random01() * 0.06f, 0.05f, 0.08f));
            }
            for (int i = 0; i < 4; i++)
            {
                float z = (i < 2 ? -1f : 1f) * (1.6f + i % 2 * 1.4f);
                Tuft(mb, new Vector3(Mx(z) + (i % 2 == 0 ? -1f : 1f) * (Hw(z) + 0.12f), 0f, z), 0.45f, 5, Pal.Sage);
            }
        }

        // ------------------------------------------------------------------ treasure chest: Lid hinge at the back top edge
        // Rest pose = closed (identity) for prop_chest and prop_chest_open alike: MapView opens it by rotating the Lid +105°
        // about its local X (positive X lifts the front edge up and back), revealing the glinting coins inside.

        const float ChestW = 0.92f, ChestD = 0.58f, ChestH = 0.46f;

        static PropModel Chest(string art, int seed)
        {
            int b = Bucket(seed);
            var rig = new Rig(art);
            rig.Body(Cached("prop_chest#body" + b, () => BuildChestBody(b)));
            var lid = rig.Part("Lid", Cached("prop_chest#lid" + b, () => BuildChestLid(b)), new Vector3(0f, ChestH, ChestD * 0.5f), Quaternion.identity);
            rig.Model.Lid = lid.transform;
            var open = new Bounds(new Vector3(0f, ChestH + 0.3f, ChestD * 0.5f + 0.15f), new Vector3(ChestW + 0.06f, 0.65f, 0.4f));
            return rig.Done(0.5f, ChestH + ChestD * 0.5f + 0.02f, open);
        }

        static MeshBuilder BuildChestBody(int b)
        {
            var mb = Builder(VariantSeed("prop_chest", b), 0.06f, 0.3f, 0.3f);
            var wood = b % 2 == 0 ? Paint.Hex("#9A6542") : Paint.Hex("#8A5A3E");
            const float W = ChestW, D = ChestD, H = ChestH, t = 0.05f;
            // floor and four walls (open top: the treasure shows when the lid opens)
            mb.Color = wood;
            mb.BoxOn(new Vector3(0f, 0f, 0f), new Vector3(W, 0.06f, D));
            mb.BoxOn(new Vector3(0f, 0f, -D * 0.5f + t * 0.5f), new Vector3(W, H, t));
            mb.BoxOn(new Vector3(0f, 0f, D * 0.5f - t * 0.5f), new Vector3(W, H, t));
            mb.BoxOn(new Vector3(-W * 0.5f + t * 0.5f, 0f, 0f), new Vector3(t, H, D - 2f * t));
            mb.BoxOn(new Vector3(W * 0.5f - t * 0.5f, 0f, 0f), new Vector3(t, H, D - 2f * t));
            mb.Color = Paint.Shade(wood, 0.72f);
            for (int i = 1; i < 3; i++) mb.Box(new Vector3(0f, H * i / 3f, -D * 0.5f - 0.004f), new Vector3(W * 0.98f, 0.015f, 0.01f));
            // iron bands, corner brackets, gold lock plate
            mb.Color = Pal.Iron;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * W * 0.3f;
                mb.Box(new Vector3(x, H * 0.5f, -D * 0.5f - 0.012f), new Vector3(0.07f, H, 0.025f));
                mb.Box(new Vector3(x, H * 0.5f, D * 0.5f + 0.012f), new Vector3(0.07f, H, 0.025f));
                mb.Box(new Vector3(x, -0.005f, 0f), new Vector3(0.07f, 0.02f, D + 0.05f));
                mb.Box(new Vector3(s * (W * 0.5f + 0.008f), H * 0.5f, -D * 0.5f + 0.04f), new Vector3(0.025f, H + 0.01f, 0.09f));
                mb.Box(new Vector3(s * (W * 0.5f - 0.035f), H * 0.5f, -D * 0.5f - 0.008f), new Vector3(0.09f, H + 0.01f, 0.025f));
            }
            mb.Box(new Vector3(0f, 0.05f, -D * 0.5f - 0.01f), new Vector3(W + 0.02f, 0.05f, 0.025f));
            mb.Color = Pal.Gold;
            mb.Box(new Vector3(0f, H - 0.11f, -D * 0.5f - 0.02f), new Vector3(0.16f, 0.17f, 0.03f));
            mb.Color = Pal.Ink;
            mb.Box(new Vector3(0f, H - 0.13f, -D * 0.5f - 0.037f), new Vector3(0.03f, 0.06f, 0.01f));
            // treasure inside: a heap of glinting coins and a gem
            mb.Emission = 0.35f;
            FacetBlob(mb, new Vector3(0f, 0.12f, 0f), new Vector3(W * 0.42f, 0.24f, D * 0.36f), 0, 0.12f, b + 3, 0.6f, ByNormal(Paint.Hex("#FFE08A"), Pal.Gold, Pal.Brass));
            mb.Emission = 0.6f;
            for (int i = 0; i < 6; i++)
            {
                mb.Color = Paint.Hex("#FFE08A");
                Gem(mb, new Vector3((mb.Random01() - 0.5f) * W * 0.6f, 0.28f + mb.Random01() * 0.06f, (mb.Random01() - 0.5f) * D * 0.4f), new Vector3(0.05f, 0.015f, 0.05f));
            }
            mb.Color = b % 2 == 0 ? Paint.Hex("#E8486A") : Paint.Hex("#58B6E8");
            Gem(mb, new Vector3(0.12f, 0.36f, -0.02f), 0.06f);
            mb.Emission = 0f;
            return mb;
        }

        static MeshBuilder BuildChestLid(int b)
        {
            // authored relative to the hinge (back top edge of the body): the lid reaches forward to z = −D
            var mb = Builder(VariantSeed("prop_chest_lid", b), 0.06f, 0f, 1f);
            var wood = b % 2 == 0 ? Paint.Hex("#A56E48") : Paint.Hex("#94613F");
            const float W = ChestW + 0.03f, D = ChestD + 0.03f, r = D * 0.5f;
            Vector3[] Ring(float x, float rr, float zc)
            {
                var ring = new Vector3[8];
                for (int i = 0; i <= 6; i++)
                {
                    float a = Mathf.PI * i / 6f;
                    ring[i] = new Vector3(x, Mathf.Sin(a) * rr, zc + Mathf.Cos(a) * rr);
                }
                ring[7] = new Vector3(x, 0f, zc);
                return ring;
            }
            mb.Color = wood;
            Loft(mb, new List<Vector3[]> { Ring(-W * 0.5f, r, -r + 0.015f), Ring(W * 0.5f, r, -r + 0.015f) });
            mb.Color = Pal.Iron;
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * ChestW * 0.3f;
                Loft(mb, new List<Vector3[]> { Ring(x - 0.036f, r + 0.014f, -r + 0.015f), Ring(x + 0.036f, r + 0.014f, -r + 0.015f) });
                Loft(mb, new List<Vector3[]> { Ring(s * (W * 0.5f - 0.03f), r + 0.01f, -r + 0.015f), Ring(s * (W * 0.5f + 0.006f), r + 0.01f, -r + 0.015f) });
            }
            mb.Color = Pal.Gold;
            mb.Box(new Vector3(0f, -0.03f, -D + 0.005f), new Vector3(0.09f, 0.14f, 0.03f));
            mb.Color = Pal.Gold;
            Gem(mb, new Vector3(0f, r + 0.02f, -r + 0.015f), new Vector3(0.05f, 0.03f, 0.05f));
            return mb;
        }
    }
}
