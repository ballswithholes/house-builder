// Expansion dungeon props, crypts (props-dungeon): bones, rubble, the brazier (lit, embers by its key), the torch sconce
// on a short post (walls are terrain), coffin, sarcophagus with an effigy, crypt pillar, the free-standing crypt doorway
// (also the "door" transition marker) and the stairs down (also the "stairs" marker).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterDungeonCrypt(Dictionary<string, Recipe> r)
        {
            r["prop_bones"] = (art, seed) => Simple(art, seed, BuildBones, 0.45f);
            r["prop_rubble"] = (art, seed) => Simple(art, seed, BuildRubble, 0.8f);
            r["prop_brazier"] = (art, seed) => DgLit(art, seed, BuildBrazier, 0.4f, new Vector3(0f, 0.97f, 0f), 0.95f, new Vector3(0f, 1.35f, 0f));
            r["prop_torch_sconce"] = (art, seed) => DgLit(art, seed, BuildTorchSconce, 0.25f, new Vector3(0f, SconceTorchY + 0.1f, SconceTorchZ), 0.42f, new Vector3(0f, SconceTorchY + 0.3f, SconceTorchZ));
            r["prop_coffin"] = (art, seed) => Simple(art, seed, BuildCoffin, 0.6f);
            r["prop_sarcophagus"] = (art, seed) => Simple(art, seed, BuildSarcophagus, 1.0f);
            r["prop_crypt_pillar"] = (art, seed) => Simple(art, seed, BuildCryptPillar, 0.5f);
            r["prop_crypt_door"] = CryptDoor;
            r["prop_crypt_door_golden"] = CryptDoor;   // Amberfield: the barrow grassed in golden downland turf
            r["prop_stairs_down"] = StairsDown;
        }

        // ------------------------------------------------------------------ bones (no collider: walkable clutter)

        static void DgRibcage(MeshBuilder mb, Vector3 at, float yaw, float s, Color bone)
        {
            mb.Push().Translate(at).Rotate(0f, yaw, 0f).Scale(s);
            mb.Color = Paint.Shade(bone, 0.95f);
            mb.Segment(new Vector3(-0.28f, 0.05f, 0f), new Vector3(0.28f, 0.07f, 0f), 0.03f, 0.025f, 5);
            mb.Color = bone;
            for (int i = 0; i < 5; i++)
            {
                float x = -0.2f + i * 0.1f;
                float k = 1f - Mathf.Abs(i - 1.5f) * 0.12f;
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 prev = new Vector3(x, 0.06f, 0f);
                    for (int j = 1; j <= 4; j++)
                    {
                        float a = j / 4f * Mathf.PI * 0.95f;
                        var p = new Vector3(x + 0.03f * j / 4f, 0.06f + Mathf.Sin(a) * 0.17f * k, sd * (1f - Mathf.Cos(a)) * 0.11f * k);
                        mb.Segment(prev, p, 0.015f, 0.013f, 4, false, true);
                        prev = p;
                    }
                }
            }
            mb.Pop();
        }

        static MeshBuilder BuildBones(int b)
        {
            var mb = Builder(VariantSeed("prop_bones", b), 0.07f, 0.25f, 0.2f);
            var bone = Paint.Hsv(DgPal.Bone, (b - 1.5f) * 3f);
            var dust = Color.Lerp(DgPal.Ash, DgPal.CryptWarm, 0.5f);
            FacetBlob(mb, new Vector3(0f, 0.0f, 0.05f), new Vector3(0.55f, 0.05f, 0.3f), 0, 0.2f, b + 1, 0.6f, ByNormal(Paint.Shade(dust, 1.05f), dust, dust));
            DgSkull(mb, new Vector3(-0.22f + b * 0.04f, 0f, -0.08f), 1.15f, 160f + b * 30f, bone, b % 2 == 0 ? 0f : -12f);
            if (b != 2) DgRibcage(mb, new Vector3(0.16f, 0f, 0.1f), 20f + b * 25f, 1f, bone);
            DgBone(mb, new Vector3(-0.5f, 0.03f, 0.12f), new Vector3(-0.1f, 0.03f, 0.3f), 0.028f, bone);
            DgBone(mb, new Vector3(0.12f, 0.03f, -0.22f), new Vector3(0.5f, 0.04f, -0.06f), 0.026f, Paint.Shade(bone, 0.95f));
            DgBone(mb, new Vector3(-0.05f, 0.06f, -0.12f), new Vector3(0.26f, 0.02f, -0.28f), 0.022f, bone);
            if (b % 2 == 1) DgBone(mb, new Vector3(-0.42f, 0.02f, -0.18f), new Vector3(-0.3f, 0.02f, 0.05f), 0.02f, bone);
            mb.Color = Paint.Shade(bone, 0.9f);
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 70f + b * 20f) * Mathf.Deg2Rad;
                Gem(mb, new Vector3(Mathf.Cos(a) * 0.45f, 0.02f, Mathf.Sin(a) * 0.24f + 0.05f), new Vector3(0.04f, 0.02f, 0.025f));
            }
            // a dropped, rusted sword or a cracked shield in alternate buckets
            if (b % 2 == 0)
            {
                mb.Color = DgPal.Rust;
                OBox(mb, new Vector3(0.3f, 0.02f, 0.28f), new Vector3(0.62f, 0.02f, 0.06f), Quaternion.Euler(0f, -25f, 0f));
                mb.Color = DgPal.IronOld;
                OBox(mb, new Vector3(0.0f, 0.03f, 0.41f), new Vector3(0.04f, 0.04f, 0.2f), Quaternion.Euler(0f, -25f, 0f));
            }
            else
            {
                mb.Color = DgPal.WoodDark;
                mb.Push().Translate(0.42f, 0.02f, 0.25f).Rotate(-80f, 30f, 0f);
                mb.Cylinder(Vector3.zero, 0.2f, 0.2f, 0.03f, 8);
                mb.Color = DgPal.IronOld;
                mb.Cylinder(new Vector3(0f, 0.03f, 0f), 0.05f, 0.03f, 0.03f, 6);
                mb.Pop();
            }
            return mb;
        }

        // ------------------------------------------------------------------ rubble (collider 1.8 × 1.0)

        static MeshBuilder BuildRubble(int b)
        {
            var mb = Builder(VariantSeed("prop_rubble", b), 0.07f, 0.35f, 0.4f);
            var stone = Paint.Hsv(DgPal.Crypt, (b - 1.5f) * 5f);
            var dust = Color.Lerp(DgPal.Ash, DgPal.CryptWarm, 0.5f);
            FacetBlob(mb, new Vector3(0f, 0.04f, 0.1f), new Vector3(0.78f, 0.18f, 0.38f), 1, 0.2f, b + 1, 0.5f, ByNormal(Paint.Shade(dust, 1.08f), dust, dust));
            (Vector3 c, Vector3 s, float tilt, float yaw)[] blocks =
            {
                (new Vector3(-0.32f, 0.26f, 0.15f), new Vector3(0.62f, 0.34f, 0.38f), 14f, 18f), (new Vector3(0.3f, 0.2f, 0.08f), new Vector3(0.5f, 0.3f, 0.34f), -10f, -25f),
                (new Vector3(0.0f, 0.55f, 0.22f), new Vector3(0.48f, 0.28f, 0.32f), 24f, 40f), (new Vector3(0.58f, 0.12f, -0.12f), new Vector3(0.3f, 0.2f, 0.24f), 30f, 10f),
                (new Vector3(-0.62f, 0.12f, -0.08f), new Vector3(0.28f, 0.18f, 0.26f), -20f, 60f),
            };
            for (int i = 0; i < blocks.Length; i++)
            {
                if (b == 3 && i == 2) continue;
                mb.Push().Rotate(0f, blocks[i].yaw + b * 9f, 0f);
                CutStone(mb, blocks[i].c, blocks[i].s, 0.05f, i % 2 == 0 ? stone : Paint.Shade(stone, 0.9f), blocks[i].tilt);
                mb.Pop();
            }
            // a broken column drum and a scatter of chips
            mb.Color = DgPal.CryptLight;
            mb.Push().Translate(-0.05f, 0.2f, -0.24f).Rotate(0f, 30f + b * 20f, 90f);
            mb.Cylinder(new Vector3(0f, -0.25f, 0f), 0.2f, 0.2f, 0.5f, 8);
            mb.Pop();
            for (int i = 0; i < 7; i++)
            {
                float a = (i * 51f + b * 33f) * Mathf.Deg2Rad;
                DgRock(mb, new Vector3(Mathf.Cos(a) * 0.72f, 0.03f, Mathf.Sin(a) * 0.32f + 0.05f), new Vector3(0.08f, 0.05f, 0.07f) * (1f + (i % 3) * 0.4f), b * 9 + i, stone, DgPal.CryptLight, 0.4f, 0);
            }
            return mb;
        }

        // ------------------------------------------------------------------ brazier (collider 0.9 × 0.7; lit, embers from its key)

        static MeshBuilder BuildBrazier(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_brazier", b), 0.06f, 0.3f, 0.3f);
            var iron = DgPal.IronOld;
            var brass = Pal.Brass;
            // three curved legs with clawed feet, a cross-ring, the wide bowl with a brass rim
            for (int i = 0; i < 3; i++)
            {
                float a = (90f + i * 120f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var foot = dir * 0.36f;
                var knee = dir * 0.3f + Vector3.up * 0.42f;
                var top = dir * 0.2f + Vector3.up * 0.78f;
                mb.Color = iron;
                mb.Segment(foot + Vector3.up * 0.06f, knee, 0.035f, 0.03f, 5);
                mb.Segment(knee, top, 0.03f, 0.035f, 5);
                mb.Color = brass;
                Gem(mb, foot + Vector3.up * 0.05f, new Vector3(0.07f, 0.06f, 0.07f));
                mb.Color = iron;
                mb.Segment(knee + dir * 0.02f, knee + dir * 0.12f + Vector3.up * 0.08f, 0.02f, 0.01f, 4);
            }
            mb.Color = iron;
            mb.Push().Translate(0f, 0.4f, 0f);
            mb.Torus(Vector3.zero, 0.3f, 0.018f, 12, 4);
            mb.Pop();
            mb.Color = iron;
            mb.Lathe(new[] { new Vector2(0.06f, 0.7f), new Vector2(0.22f, 0.74f), new Vector2(0.36f, 0.86f), new Vector2(0.42f, 0.98f) }, 10, false, true, false,
                     new[] { Paint.Shade(iron, 0.85f), iron, iron, Paint.Shade(iron, 1.1f) });
            mb.Color = brass;
            mb.Push().Translate(0f, 0.98f, 0f);
            mb.Torus(Vector3.zero, 0.42f, 0.03f, 14, 4);
            mb.Pop();
            // small brass lantern-flame emblems on the bowl
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var n = new Vector3(Mathf.Cos(a), -0.35f, Mathf.Sin(a)).normalized;
                Gem(mb, new Vector3(Mathf.Cos(a) * 0.38f, 0.88f, Mathf.Sin(a) * 0.38f) + n * 0.01f, new Vector3(0.05f, 0.06f, 0.05f));
            }
            // coals: glowing when lit, grey ash when out
            mb.Emission = lit ? 1f : 0f;
            for (int i = 0; i < 11; i++)
            {
                float a = (i * 137f + b * 21f) * Mathf.Deg2Rad;
                float rr = 0.08f + (i % 4) * 0.08f;
                mb.Color = lit ? Color.Lerp(Pal.Ember, Pal.FireCore, (i % 3) * 0.4f) : Paint.Shade(Pal.Charcoal, 1.4f + (i % 2) * 0.3f);
                Gem(mb, new Vector3(Mathf.Cos(a) * rr, 0.93f + (i % 2) * 0.03f, Mathf.Sin(a) * rr), new Vector3(0.08f, 0.05f, 0.08f));
            }
            mb.Emission = 0f;
            return mb;
        }

        // ------------------------------------------------------------------ torch sconce on a short post (collider 0.6 × 0.5; lit)

        const float SconceTorchY = 1.98f, SconceTorchZ = -0.36f;

        static MeshBuilder BuildTorchSconce(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_torch_sconce", b), 0.06f, 0.3f, 0.4f);
            var stone = Paint.Hsv(DgPal.Crypt, (b - 1.5f) * 4f);
            var stoneD = Paint.Shade(stone, 0.85f);
            // post: plinth, a slightly tapered square shaft of stacked blocks, a capstone
            mb.Color = stoneD;
            mb.BoxOn(new Vector3(0f, 0f, 0.05f), new Vector3(0.5f, 0.2f, 0.42f), stone);
            for (int k = 0; k < 4; k++)
            {
                mb.Color = k % 2 == 0 ? stone : Paint.Shade(stone, 0.94f);
                float w = 0.36f - k * 0.015f;
                OBox(mb, new Vector3((mb.Random01() - 0.5f) * 0.02f, 0.2f + 0.24f + k * 0.48f, 0.05f), new Vector3(w, 0.46f, w * 0.88f), Quaternion.Euler(0f, (mb.Random01() - 0.5f) * 4f, 0f));
            }
            mb.Color = DgPal.CryptLight;
            mb.BoxOn(new Vector3(0f, 2.12f, 0.05f), new Vector3(0.44f, 0.1f, 0.4f));
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.14f, 2.22f), new Vector2(0.1f, 2.3f), new Vector2(0f, 2.36f) }, 4, false, false, false, null, 45f);
            // carved lantern emblem on the front
            mb.Color = stoneD;
            Slab(mb, new[] { new Vector3(-0.07f, 1.2f, -0.115f), new Vector3(-0.07f, 1.38f, -0.115f), new Vector3(0f, 1.46f, -0.115f), new Vector3(0.07f, 1.38f, -0.115f), new Vector3(0.07f, 1.2f, -0.115f) }, Vector3.back, 0.015f);
            // iron bracket and the torch, leaning forward
            mb.Color = DgPal.IronOld;
            mb.Box(new Vector3(0f, 1.72f, -0.13f), new Vector3(0.16f, 0.22f, 0.04f));
            mb.Segment(new Vector3(0f, 1.7f, -0.14f), new Vector3(0f, SconceTorchY - 0.32f, SconceTorchZ + 0.06f), 0.025f, 0.02f, 4);
            mb.Push().Translate(0f, SconceTorchY - 0.28f, SconceTorchZ + 0.04f);
            mb.Torus(Vector3.zero, 0.05f, 0.015f, 8, 3);
            mb.Pop();
            mb.Color = DgPal.Wood;
            mb.Segment(new Vector3(0f, SconceTorchY - 0.55f, SconceTorchZ + 0.13f), new Vector3(0f, SconceTorchY, SconceTorchZ), 0.03f, 0.04f, 5);
            // the pitch-soaked wrap: glowing when lit
            mb.Emission = lit ? 0.8f : 0f;
            mb.Color = lit ? Pal.Ember : DgPal.Soot;
            mb.Cylinder(new Vector3(0f, SconceTorchY - 0.06f, SconceTorchZ), 0.06f, 0.07f, 0.14f, 6);
            mb.Emission = 0f;
            // moss at the foot
            FacetBlob(mb, new Vector3(0.12f, 0.2f, 0.18f), new Vector3(0.16f, 0.04f, 0.14f), 0, 0.25f, b + 2, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            return mb;
        }

        // ------------------------------------------------------------------ coffin (collider 2.3 × 0.9; lies along X)

        static MeshBuilder BuildCoffin(int b)
        {
            var mb = Builder(VariantSeed("prop_coffin", b), 0.07f, 0.3f, 0.3f);
            var wood = Paint.Hsv(DgPal.Wood, (b - 1.5f) * 5f);
            var woodD = Paint.Shade(wood, 0.82f);
            // the classic six-sided outline, widest at the shoulders
            Vector2[] Outline(float k) => new[]
            {
                new Vector2(-0.98f * k, 0f), new Vector2(-0.58f * k, -0.3f * k), new Vector2(0.9f * k, -0.2f * k),
                new Vector2(0.98f * k, 0f), new Vector2(0.9f * k, 0.2f * k), new Vector2(-0.58f * k, 0.3f * k),
            };
            mb.Push().Translate(0f, 0f, 0.02f);
            mb.Color = woodD;
            mb.Extrude(Outline(0.96f), 0f, 0.08f);
            mb.Color = wood;
            mb.Extrude(Outline(0.94f), 0.08f, 0.4f);
            // the lid: shut, or pushed askew in alternate buckets (a dark gap shows)
            bool ajar = b % 2 == 1;
            if (ajar)
            {
                mb.Color = DgPal.Void;
                mb.Extrude(Outline(0.86f), 0.4f, 0.405f, false, true);
            }
            mb.Push();
            if (ajar) mb.Translate(0.16f, 0.02f, -0.12f).Rotate(0f, -9f, -2f);
            mb.Color = Paint.Shade(wood, 1.08f);
            mb.Extrude(Outline(1f), 0.4f, 0.48f);
            mb.Color = wood;
            mb.Extrude(Outline(0.8f), 0.48f, 0.53f);
            // iron bands and a brass lantern plaque
            mb.Color = DgPal.IronOld;
            mb.Box(new Vector3(-0.58f, 0.5f, 0f), new Vector3(0.06f, 0.07f, 0.62f));
            mb.Box(new Vector3(0.55f, 0.5f, 0f), new Vector3(0.06f, 0.07f, 0.46f));
            mb.Color = Pal.Brass;
            mb.Box(new Vector3(-0.15f, 0.535f, 0f), new Vector3(0.22f, 0.012f, 0.14f));
            mb.Pop();
            mb.Pop();
            // a dried posy and a melted candle stub
            mb.Color = Paint.Hex("#B08A6A");
            mb.Segment(new Vector3(-0.4f, 0.54f, -0.05f), new Vector3(-0.15f, 0.55f, 0.08f), 0.012f, 0.01f, 3);
            mb.Color = Paint.Hex("#C98AA0");
            Gem(mb, new Vector3(-0.42f, 0.56f, -0.06f), 0.04f);
            Gem(mb, new Vector3(-0.38f, 0.56f, -0.1f), 0.035f);
            mb.Color = Pal.Cream;
            mb.Cylinder(new Vector3(0.62f, 0.0f, -0.3f), 0.04f, 0.035f, 0.12f, 6);
            FacetBlob(mb, new Vector3(0.62f, 0.0f, -0.3f), new Vector3(0.08f, 0.02f, 0.07f), 0, 0.2f, b, 0f, ByNormal(Pal.Cream, Pal.Cream, Pal.Cream));
            return mb;
        }

        // ------------------------------------------------------------------ sarcophagus with an effigy (collider 2.8 × 1.4; lies along X)

        static MeshBuilder BuildSarcophagus(int b)
        {
            var mb = Builder(VariantSeed("prop_sarcophagus", b), 0.06f, 0.3f, 0.4f);
            var stone = Paint.Hsv(DgPal.Crypt, (b - 1.5f) * 4f);
            var light = DgPal.CryptLight;
            var dark = Paint.Shade(stone, 0.84f);
            mb.Push().Translate(0f, 0f, 0.12f);
            mb.Color = dark;
            mb.BoxOn(Vector3.zero, new Vector3(2.3f, 0.14f, 1.0f), stone);
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, 0.14f, 0f), new Vector3(2.12f, 0.56f, 0.86f));
            // carved panels: three recessed lantern arches on the front
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 0.66f;
                mb.Color = dark;
                Slab(mb, NgonPoints(new Vector3(x, 0.44f, -0.44f), 0.17f, 0.17f, 6, Vector3.right, Vector3.up, 0f, 0f, 180f), Vector3.back, 0.012f);
                mb.Box(new Vector3(x, 0.33f, -0.437f), new Vector3(0.34f, 0.22f, 0.012f));
                mb.Color = light;
                mb.Box(new Vector3(x, 0.36f, -0.445f), new Vector3(0.06f, 0.12f, 0.01f));
            }
            mb.Color = light;
            mb.Box(new Vector3(0f, 0.7f, 0f), new Vector3(2.24f, 0.08f, 0.96f));
            mb.Color = stone;
            mb.Box(new Vector3(0f, 0.8f, 0f), new Vector3(2.12f, 0.12f, 0.86f));
            // the effigy: a knight at rest, head on a tasselled cushion, hands folded on a sword, feet on a little hound
            var eff = Color.Lerp(DgPal.CryptWarm, DgPal.Bone, 0.45f);
            var effD = Paint.Shade(eff, 0.86f);
            mb.Color = Paint.Hex("#8C5A6A");
            mb.Box(new Vector3(-0.86f, 0.92f, 0f), new Vector3(0.28f, 0.1f, 0.46f));
            mb.Color = Pal.Gold;
            for (int s = -1; s <= 1; s += 2) Gem(mb, new Vector3(-0.86f + s * 0.14f, 0.92f, -0.23f), 0.03f);
            mb.Color = eff;
            mb.Sphere(new Vector3(-0.74f, 1.06f, 0f), new Vector3(0.13f, 0.12f, 0.13f), 8, 5, false);
            mb.Color = effD;
            mb.Box(new Vector3(-0.76f, 1.14f, 0f), new Vector3(0.18f, 0.06f, 0.24f));
            mb.Color = eff;
            mb.Box(new Vector3(-0.6f, 0.97f, 0f), new Vector3(0.08f, 0.1f, 0.12f));
            mb.Box(new Vector3(-0.32f, 1.0f, 0f), new Vector3(0.5f, 0.2f, 0.46f));          // breastplate
            mb.Color = effD;
            mb.Box(new Vector3(-0.32f, 1.11f, 0f), new Vector3(0.42f, 0.03f, 0.3f));
            mb.Color = eff;
            mb.Box(new Vector3(0.0f, 0.97f, 0f), new Vector3(0.18f, 0.14f, 0.38f));          // tasset
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Box(new Vector3(0.36f, 0.95f, s * 0.1f), new Vector3(0.58f, 0.12f, 0.15f));  // legs
                mb.Box(new Vector3(0.68f, 1.01f, s * 0.1f), new Vector3(0.08f, 0.2f, 0.14f));   // feet up
            }
            mb.Color = effD;
            mb.Box(new Vector3(0.8f, 0.96f, 0f), new Vector3(0.14f, 0.14f, 0.36f));          // the hound at his feet
            mb.Sphere(new Vector3(0.82f, 1.06f, -0.17f), new Vector3(0.07f, 0.07f, 0.07f), 6, 4, false);
            // the sword laid along his body, hilt under the folded hands
            mb.Color = DgPal.CryptLight;
            mb.Box(new Vector3(0.05f, 1.13f, 0f), new Vector3(0.95f, 0.025f, 0.07f));
            mb.Color = eff;
            mb.Box(new Vector3(-0.42f, 1.14f, 0f), new Vector3(0.05f, 0.04f, 0.28f));
            mb.Box(new Vector3(-0.52f, 1.14f, 0f), new Vector3(0.14f, 0.035f, 0.04f));
            mb.Sphere(new Vector3(-0.45f, 1.17f, 0f), new Vector3(0.07f, 0.05f, 0.11f), 6, 4, false);
            for (int s = -1; s <= 1; s += 2) mb.Segment(new Vector3(-0.5f, 1.09f, s * 0.24f), new Vector3(-0.44f, 1.15f, s * 0.05f), 0.045f, 0.04f, 5);
            mb.Pop();
            // moss, a cobweb corner, and offerings (bucket varies)
            FacetBlob(mb, new Vector3(1.0f, 0.84f, 0.42f), new Vector3(0.2f, 0.04f, 0.16f), 0, 0.25f, b + 1, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            FacetBlob(mb, new Vector3(-1.05f, 0.14f, -0.32f), new Vector3(0.2f, 0.05f, 0.14f), 0, 0.25f, b + 2, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            mb.Color = Pal.Cream;
            for (int i = 0; i < 2 + b % 2; i++)
                mb.Cylinder(new Vector3(0.95f - i * 0.12f, 0.86f, -0.24f + (i % 2) * 0.05f), 0.035f, 0.03f, 0.1f + i * 0.05f, 6);
            if (b % 2 == 0)
            {
                mb.Color = Pal.Flowers[b % Pal.Flowers.Length];
                Gem(mb, new Vector3(-0.3f, 1.08f, -0.08f), 0.045f);
                Gem(mb, new Vector3(-0.25f, 1.08f, -0.02f), 0.04f);
            }
            return mb;
        }

        // ------------------------------------------------------------------ crypt pillar (collider 1.1 × 0.9)

        static MeshBuilder BuildCryptPillar(int b)
        {
            var mb = Builder(VariantSeed("prop_crypt_pillar", b), 0.06f, 0.35f, 0.6f);
            var stone = Paint.Hsv(DgPal.Crypt, (b - 1.5f) * 4f);
            var light = DgPal.CryptLight;
            var dark = Paint.Shade(stone, 0.84f);
            const float H = 4.4f;
            mb.Push().Translate(0f, 0f, 0.1f);
            // square plinth and torus base, an octagonal shaft of drums, a cushion capital and an abacus slab
            mb.Color = dark;
            mb.BoxOn(Vector3.zero, new Vector3(0.9f, 0.32f, 0.78f), stone);
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.38f, 0.32f), new Vector2(0.4f, 0.4f), new Vector2(0.34f, 0.48f), new Vector2(0.3f, 0.52f) }, 8, false, false, false, null, 22.5f);
            int drums = 5;
            for (int k = 0; k < drums; k++)
            {
                float y0 = 0.52f + k * (H - 1.1f) / drums, y1 = y0 + (H - 1.1f) / drums - 0.02f;
                mb.Color = k % 2 == 0 ? stone : Paint.Shade(stone, 0.95f);
                mb.Lathe(new[] { new Vector2(0.29f - k * 0.008f, y0), new Vector2(0.285f - k * 0.008f, y1) }, 8, false, false, false, null, 22.5f + (mb.Random01() - 0.5f) * 4f);
            }
            // the capital: a necking ring, a flared echinus carved with lantern leaves, and a modest abacus with a cap course
            mb.Color = light;
            mb.Lathe(new[] { new Vector2(0.3f, H - 0.62f), new Vector2(0.31f, H - 0.56f) }, 8, false, false, true, null, 22.5f);
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.27f, H - 0.56f), new Vector2(0.3f, H - 0.48f), new Vector2(0.4f, H - 0.36f), new Vector2(0.44f, H - 0.3f) }, 8, false, false, true,
                     new[] { dark, stone, light, light }, 22.5f);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                mb.Color = light;
                Gem(mb, d * 0.36f + Vector3.up * (H - 0.4f), new Vector3(0.09f, 0.11f, 0.09f));
            }
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, H - 0.3f, 0f), new Vector3(0.84f, 0.18f, 0.76f), light);
            mb.Color = light;
            mb.BoxOn(new Vector3(0f, H - 0.12f, 0f), new Vector3(0.74f, 0.12f, 0.66f));
            // a carved lantern niche in the shaft with a candle stub
            mb.Color = DgPal.VoidEdge;
            Slab(mb, NgonPoints(new Vector3(0f, 1.62f, -0.275f), 0.12f, 0.12f, 6, Vector3.right, Vector3.up, 0f, 0f, 180f), Vector3.back, 0.01f);
            mb.Box(new Vector3(0f, 1.5f, -0.272f), new Vector3(0.24f, 0.24f, 0.01f));
            mb.Color = Pal.Cream;
            mb.Box(new Vector3(0f, 1.42f, -0.29f), new Vector3(0.05f, 0.08f, 0.04f));
            mb.Pop();
            // cracks, moss at the foot and a fallen chip
            mb.Color = dark;
            Beam(mb, new Vector3(0.12f, 2.4f, -0.17f), new Vector3(0.04f, 2.75f, -0.17f), 0.025f, 0.01f, Vector3.back);
            Beam(mb, new Vector3(0.04f, 2.75f, -0.17f), new Vector3(0.1f, 3.0f, -0.17f), 0.02f, 0.01f, Vector3.back);
            FacetBlob(mb, new Vector3(-0.3f, 0.3f, -0.22f), new Vector3(0.22f, 0.05f, 0.14f), 0, 0.25f, b + 1, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            CutStone(mb, new Vector3(0.5f, 0.1f, 0.42f), new Vector3(0.24f, 0.18f, 0.2f), 0.04f, stone, 18f);
            return mb;
        }

        // ------------------------------------------------------------------ crypt doorway on outdoor maps (prop collider 4.4 × 2.4; the mound reaches 5 m back; as a marker none)

        const float CryptDoorHalfW = 0.78f, CryptDoorSpring = 1.6f;

        static PropModel CryptDoor(string art, int seed)
        {
            int b = Bucket(seed);
            string key = art + "#" + b;
            var rig = new Rig(art);
            bool golden = art.EndsWith("_golden");
            var mf = rig.Body(LitMesh(key, true, lit => BuildCryptDoor(b, lit, golden)));
            rig.Model.SetLit = MeshSwitch(mf, key, lit => BuildCryptDoor(b, lit, golden));
            rig.Part("Opening", Cached("prop_crypt_door#in", () =>
            {
                var mb = Builder(VariantSeed("prop_crypt_door_in", 0), 0.03f, 0f, 0.1f);
                DgOpening(mb, CryptDoorHalfW, CryptDoorSpring, -0.02f, 0.42f, Paint.Shade(DgPal.CryptDark, 0.5f), DgPal.Void, 7, 4);
                return mb;
            }), Vector3.zero, Quaternion.identity, Skin.Plain);
            rig.Light(new Vector3(-1.25f, 1.05f, -0.75f));
            rig.Light(new Vector3(1.25f, 1.05f, -0.75f));
            return rig.Done(1.8f);
        }

        static MeshBuilder BuildCryptDoor(int b, bool lit, bool golden)
        {
            var mb = Builder(VariantSeed(golden ? "prop_crypt_door_golden" : "prop_crypt_door", b), 0.06f, 0.35f, 0.6f);
            var stone = Paint.Hsv(DgPal.Crypt, (b - 1.5f) * 4f);
            var light = DgPal.CryptLight;
            var dark = Paint.Shade(stone, 0.84f);
            var turf = Paint.Hsv(golden ? Color.Lerp(Pal.Moss, Pal.Thatch, 0.62f) : Pal.Moss, (b - 1.5f) * 4f);
            var grass = golden ? Pal.ThatchLight : Pal.Sage;
            var grassL = golden ? Pal.Thatch : Pal.MossLight;
            // the barrow mound behind, grassed over
            // (its face stays behind the short dark passage behind the doors)
            var turfCol = ByNormal(Paint.Shade(turf, 1.1f), turf, Paint.Shade(turf, 0.75f), 0.1f);
            FacetBlob(mb, new Vector3(0f, 0.4f, 2.85f), new Vector3(3.0f, 3.0f, 2.2f), 1, 0.08f, b + 1, 0.55f, turfCol);
            FacetBlob(mb, new Vector3(0.7f, 2.95f, 2.4f), new Vector3(0.9f, 0.32f, 0.8f), 0, 0.15f, b + 2, 0.4f, Mossy(DgPal.Crypt, turf, 0.6f));
            for (int i = 0; i < 6; i++)
                Tuft(mb, new Vector3(-2.0f + i * 0.8f, 2.7f + Mathf.Sin(i * 1.3f) * 0.3f, 1.35f + (i % 2) * 0.3f), 0.35f, 5, i % 2 == 0 ? grass : grassL);
            // facade: two pilasters, the arch of voussoirs, a lintel and a pediment with a lantern emblem
            const float pw = 0.42f, px = CryptDoorHalfW + pw * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = dark;
                mb.BoxOn(new Vector3(s * px, 0f, 0.1f), new Vector3(pw + 0.12f, 0.24f, 0.62f), stone);
                for (int k = 0; k < 4; k++)
                {
                    mb.Color = k % 2 == 0 ? stone : Paint.Shade(stone, 0.94f);
                    mb.BoxOn(new Vector3(s * px, 0.24f + k * 0.42f, 0.12f), new Vector3(pw, 0.4f, 0.54f));
                }
                // side wing walls stepping down into the mound
                mb.Color = Paint.Shade(stone, 0.92f);
                mb.BoxOn(new Vector3(s * (px + 0.62f), 0f, 0.3f), new Vector3(0.82f, 1.25f, 0.5f), light);
                mb.Color = stone;
                mb.BoxOn(new Vector3(s * (px + 1.2f), 0f, 0.45f), new Vector3(0.5f, 0.75f, 0.45f), light);
            }
            const int vs = 7;
            for (int i = 0; i < vs; i++)
            {
                float am = (180f - (i + 0.5f) * 180f / vs) * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(am) * (CryptDoorHalfW + 0.17f), CryptDoorSpring + Mathf.Sin(am) * (CryptDoorHalfW + 0.17f), 0.06f);
                mb.Color = i == vs / 2 ? light : (i % 2 == 0 ? stone : Paint.Shade(stone, 0.93f));
                OBox(mb, c, new Vector3(0.3f, i == vs / 2 ? 0.42f : 0.34f, 0.5f), Quaternion.Euler(0f, 0f, am * Mathf.Rad2Deg - 90f));
            }
            // spandrel fill between the arch and the lintel (behind the voussoirs)
            mb.Color = Paint.Shade(stone, 0.96f);
            mb.Box(new Vector3(0f, CryptDoorSpring + 0.6f, 0.22f), new Vector3(2.4f, 1.2f, 0.3f));
            mb.Color = light;
            mb.Box(new Vector3(0f, 2.82f, 0.1f), new Vector3(2.5f, 0.2f, 0.66f));
            mb.Color = stone;
            Slab(mb, new[] { new Vector3(-1.2f, 2.92f, -0.15f), new Vector3(0f, 3.55f, -0.15f), new Vector3(1.2f, 2.92f, -0.15f) }, Vector3.back, 0.5f);
            mb.Color = light;
            Beam(mb, new Vector3(-1.32f, 2.88f, -0.2f), new Vector3(0.02f, 3.62f, -0.2f), 0.14f, 0.6f, Vector3.back);
            Beam(mb, new Vector3(1.32f, 2.88f, -0.2f), new Vector3(-0.02f, 3.62f, -0.2f), 0.14f, 0.6f, Vector3.back);
            mb.Color = dark;
            Slab(mb, new[] { new Vector3(-0.1f, 3.0f, -0.16f), new Vector3(-0.1f, 3.22f, -0.16f), new Vector3(0f, 3.32f, -0.16f), new Vector3(0.1f, 3.22f, -0.16f), new Vector3(0.1f, 3.0f, -0.16f) }, Vector3.back, 0.015f);
            // heavy iron-bound doors standing ajar inwards (the dark passage shows between them)
            for (int s = -1; s <= 1; s += 2)
            {
                float open = s < 0 ? 62f : 38f + b * 6f;
                mb.Push().Translate(s * CryptDoorHalfW, 0f, 0.12f).Rotate(0f, s * -open, 0f);
                mb.Color = DgPal.WoodDark;
                var leaf = new List<Vector3> { new Vector3(0f, 0.02f, 0f), new Vector3(0f, CryptDoorSpring, 0f) };
                for (int i = 0; i <= 4; i++)
                {
                    float a = Mathf.PI * 0.5f * i / 4f;
                    leaf.Add(new Vector3(-s * (CryptDoorHalfW - Mathf.Cos(a) * CryptDoorHalfW), CryptDoorSpring + Mathf.Sin(a) * CryptDoorHalfW - 0.02f, 0f));
                }
                leaf.Add(new Vector3(-s * CryptDoorHalfW, 0.02f, 0f));
                mb.Push().Scale(new Vector3(0.98f, 1f, 1f));
                Slab(mb, leaf, Vector3.back, 0.08f);
                mb.Pop();
                mb.Color = DgPal.IronOld;
                for (int k = 0; k < 3; k++) mb.Box(new Vector3(-s * CryptDoorHalfW * 0.5f, 0.35f + k * 0.6f, -0.01f), new Vector3(CryptDoorHalfW * 0.9f, 0.07f, 0.03f));
                mb.Torus(new Vector3(-s * CryptDoorHalfW * 0.82f, 1.0f, -0.03f), 0.07f, 0.015f, 8, 3);
                mb.Pop();
            }
            // two low steps, a stone lantern post either side (lit at night), ivy and grass
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, 0f, -0.42f), new Vector3(2.0f, 0.12f, 0.5f), light);
            mb.Color = Paint.Shade(stone, 0.95f);
            mb.BoxOn(new Vector3(0f, 0f, -0.82f), new Vector3(1.7f, 0.06f, 0.36f), light);
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = stone;
                mb.BoxOn(new Vector3(s * 1.25f, 0f, -0.75f), new Vector3(0.24f, 0.86f, 0.24f), light);
                mb.Color = dark;
                mb.BoxOn(new Vector3(s * 1.25f, 0.86f, -0.75f), new Vector3(0.3f, 0.04f, 0.3f));
                IronLantern(mb, new Vector3(s * 1.25f, 1.02f, -0.75f), 1.1f, lit);
            }
            mb.WindGradient = true; mb.WindY0 = 3.0f; mb.WindY1 = 1.6f; mb.Wind = 0.4f;
            for (int i = 0; i < 6; i++)
            {
                mb.Color = i % 2 == 0 ? Pal.Leaf : Pal.LeafDark;
                Gem(mb, new Vector3(-1.05f + (i % 2) * 0.05f, 2.72f - i * 0.16f, -0.25f), new Vector3(0.09f, 0.08f, 0.06f));
                if (i < 4) Gem(mb, new Vector3(0.95f + (i % 2) * 0.05f, 2.72f - i * 0.16f, -0.25f), new Vector3(0.08f, 0.07f, 0.06f));
            }
            mb.Wind = 0f; mb.WindGradient = false;
            foreach (var p in new[] { new Vector3(-1.75f, 0f, -0.2f), new Vector3(1.8f, 0f, -0.15f), new Vector3(-0.95f, 0f, -0.95f), new Vector3(2.4f, 0f, 0.4f) })
                Tuft(mb, p, 0.34f + mb.Random01() * 0.12f, 6, grass);
            return mb;
        }

        // ------------------------------------------------------------------ stairs down (prop collider 4.0 × 2.6; as a marker none)

        const float StairsTop = 0.5f;

        static PropModel StairsDown(string art, int seed)
        {
            int b = Bucket(seed);
            string key = art + "#" + b;
            var rig = new Rig(art);
            var mf = rig.Body(LitMesh(key, true, lit => BuildStairsDown(b, lit)));
            rig.Model.SetLit = MeshSwitch(mf, key, lit => BuildStairsDown(b, lit));
            rig.Part("Opening", Cached("prop_stairs_down#in", () =>
            {
                var mb = Builder(VariantSeed("prop_stairs_down_in", 0), 0.03f, 0f, 0.1f);
                mb.Push().Translate(0f, 0.02f, 0f);
                DgOpening(mb, 0.62f, 0.75f, 0.95f, 1.85f, Paint.Shade(DgPal.CryptDark, 0.45f), DgPal.Void, 6, 3);
                mb.Pop();
                return mb;
            }), Vector3.zero, Quaternion.identity, Skin.Plain);
            rig.Light(new Vector3(-1.04f, 1.16f, -0.82f));
            rig.Light(new Vector3(1.04f, 1.16f, -0.82f));
            return rig.Done(1.6f);
        }

        static MeshBuilder BuildStairsDown(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_stairs_down", b), 0.06f, 0.3f, 0.3f);
            var stone = Paint.Hsv(DgPal.Crypt, (b - 1.5f) * 4f);
            var light = DgPal.CryptLight;
            var dark = Paint.Shade(stone, 0.8f);
            // a raised stone platform with a stairwell cut into it: front kerb, side walls, back block under the hood
            const float hw = 0.62f, zf = -0.62f, zb = 0.95f;
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, 0f, -0.82f), new Vector3(2.4f, StairsTop, 0.4f), light);              // front kerb (corners stepped back)
            for (int s = -1; s <= 1; s += 2)
                mb.BoxOn(new Vector3(s * (hw + 0.44f), 0f, 0.6f), new Vector3(0.88f, StairsTop, 2.6f), light);  // sides
            mb.BoxOn(new Vector3(0f, 0f, 2.15f), new Vector3(3.0f, StairsTop, 0.5f), light);                // back
            // trim along the well's lip
            mb.Color = light;
            mb.Box(new Vector3(-hw - 0.05f, StairsTop + 0.03f, 0.15f), new Vector3(0.1f, 0.06f, 1.6f));
            mb.Box(new Vector3(hw + 0.05f, StairsTop + 0.03f, 0.15f), new Vector3(0.1f, 0.06f, 1.6f));
            // the steps descending away from the camera, darker as they go down
            const int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps;
                float z0 = Mathf.Lerp(zf, zb, (float)i / steps), z1 = Mathf.Lerp(zf, zb, (float)(i + 1) / steps);
                float y = StairsTop - 0.07f - i * (StairsTop - 0.1f) / steps;
                mb.Color = Color.Lerp(stone, Paint.Shade(DgPal.CryptDark, 0.45f), t * 0.9f);
                mb.Box(new Vector3(0f, y * 0.5f, (z0 + z1) * 0.5f), new Vector3(hw * 2f, y, z1 - z0));
            }
            // inner well walls (the side blocks' inner faces, darkened)
            for (int s = -1; s <= 1; s += 2)
                QuadC(mb, new Vector3(s * hw - s * 0.005f, 0f, zf), new Vector3(s * hw - s * 0.005f, StairsTop, zf), new Vector3(s * hw - s * 0.005f, StairsTop, zb + 0.9f), new Vector3(s * hw - s * 0.005f, 0f, zb + 0.9f),
                      new Vector3(-s, 0f, 0f), Paint.Shade(DgPal.CryptDark, 0.7f));
            // the hood: an arch over the bottom of the stair with a small gabled roof
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = stone;
                mb.BoxOn(new Vector3(s * (hw + 0.16f), StairsTop, 1.25f), new Vector3(0.32f, 0.85f, 0.6f), light);
            }
            const int vs = 5;
            for (int i = 0; i < vs; i++)
            {
                float am = (180f - (i + 0.5f) * 180f / vs) * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(am) * (hw + 0.15f), StairsTop + 0.85f + Mathf.Sin(am) * (hw + 0.15f) * 0.8f, 1.12f);
                mb.Color = i == vs / 2 ? light : stone;
                OBox(mb, c, new Vector3(0.28f, 0.3f, 0.38f), Quaternion.Euler(0f, 0f, am * Mathf.Rad2Deg - 90f));
            }
            mb.Color = Paint.Shade(stone, 0.95f);
            mb.Box(new Vector3(0f, StairsTop + 1.35f, 1.4f), new Vector3(1.7f, 0.5f, 0.9f));
            mb.Color = dark;
            mb.Roof(new Vector3(0f, StairsTop + 1.6f, 1.38f), 1.9f, 1.0f, 0.45f, 0.08f, 0.1f);
            FacetBlob(mb, new Vector3(0.3f, StairsTop + 1.85f, 1.3f), new Vector3(0.32f, 0.08f, 0.26f), 0, 0.25f, b + 3, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            // two lantern posts at the front corners (lit at night), moss and a cracked slab
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = stone;
                mb.BoxOn(new Vector3(s * 1.04f, StairsTop, -0.82f), new Vector3(0.2f, 0.5f, 0.2f), light);
                IronLantern(mb, new Vector3(s * 1.04f, StairsTop + 0.66f, -0.82f), 1.05f, lit);
            }
            FacetBlob(mb, new Vector3(-1.2f, StairsTop, 0.6f), new Vector3(0.3f, 0.05f, 0.4f), 0, 0.25f, b + 4, 0f, ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss));
            mb.Color = dark;
            Beam(mb, new Vector3(1.0f, StairsTop + 0.005f, -0.3f), new Vector3(1.25f, StairsTop + 0.005f, 0.5f), 0.025f, 0.01f);
            foreach (var p in new[] { new Vector3(-1.62f, 0f, 0.1f), new Vector3(1.62f, 0f, 0.6f), new Vector3(-1.6f, 0f, 1.2f) })
                Tuft(mb, p, 0.32f, 5, Pal.Sage);
            return mb;
        }
    }
}
