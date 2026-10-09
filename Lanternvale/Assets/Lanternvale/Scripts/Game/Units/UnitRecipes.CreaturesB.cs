// Expansion models, group B (Docs/Expansion.md §9 models-b): undead, elementals and humanoid bosses of the level 12-30
// zones, the hidden dungeons and the Hollow Heart / Ashwyrm's Roost raids. Registered through RegisterCreaturesB
// (UnitRecipes.TryBuildExpansion). All on the biped rig.
//
//   undead      cr_skeleton (sword & round shield), cr_skeleton_archer, cr_barrow_wight (+ cr_frost_wight), cr_bog_ghoul
//               (hunched), cr_hollow_knight (grey hollow plate, dark lantern)
//   bosses      cr_lantern_lich (= cr_dg3_lantern_lich), cr_dg4_king_aldwin, cr_tidewitch (= cr_dg5_tidewitch),
//               cr_rimeheart (= cr_dg6_rimeheart), cr_r1_twin (= cr_r1_twin_sorrow) and cr_r1_twin_solace,
//               cr_r1_mother_mire, cr_r2_varkas
//   drowned     cr_drowned_sentinel (barnacles, seaweed, dripping)
//   elementals  cr_ice_elemental, cr_ash_elemental, cr_fen_wisp
//
// Hunched models (bog ghoul, Mother Mire) are authored upright and tipped forward about the hip line (BeginHipTilt /
// EndHipTilt): the upper body's geometry and its bone pivots turn together, so the rig animates the hunched pose.
// Natural heights are what the data `size` scales; the recommended sizes per key are in Docs/ArtKeys.md.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class UnitRecipes
    {
        static partial void RegisterCreaturesB(Dictionary<string, Func<UnitModel>> d)
        {
            d["cr_skeleton"] = () => Skeleton("cr_skeleton", false);
            d["cr_skeleton_archer"] = () => Skeleton("cr_skeleton_archer", true);
            d["cr_barrow_wight"] = () => BarrowWight("cr_barrow_wight", false);
            d["cr_frost_wight"] = d["cr_barrow_wight_frost"] = () => BarrowWight("cr_frost_wight", true);
            d["cr_bog_ghoul"] = () => BogGhoul("cr_bog_ghoul");
            d["cr_hollow_knight"] = () => HollowKnight("cr_hollow_knight");
            d["cr_lantern_lich"] = d["cr_dg3_lantern_lich"] = () => LanternLich("cr_lantern_lich");
            d["cr_drowned_sentinel"] = () => DrownedSentinel("cr_drowned_sentinel");
            d["cr_tidewitch"] = d["cr_dg5_tidewitch"] = () => Tidewitch("cr_tidewitch");
            d["cr_dg4_king_aldwin"] = d["cr_king_aldwin"] = () => KingAldwin("cr_dg4_king_aldwin");
            d["cr_ice_elemental"] = () => IceElemental("cr_ice_elemental", false);
            d["cr_rimeheart"] = d["cr_dg6_rimeheart"] = () => IceElemental("cr_rimeheart", true);
            d["cr_ash_elemental"] = () => AshElemental("cr_ash_elemental");
            d["cr_fen_wisp"] = () => FenWisp("cr_fen_wisp");
            d["cr_r1_twin"] = d["cr_r1_twin_sorrow"] = () => WeepingTwin("cr_r1_twin_sorrow", false);
            d["cr_r1_twin_solace"] = () => WeepingTwin("cr_r1_twin_solace", true);
            d["cr_r1_mother_mire"] = () => MotherMire("cr_r1_mother_mire");
            d["cr_r2_varkas"] = () => Varkas("cr_r2_varkas");
        }

        // ================================================================== shared pieces

        /// <summary>Upper-body hunch: geometry drawn between BeginHipTilt and EndHipTilt, and the upper bones, turn forward.</summary>
        struct HipTilt
        {
            public Vector3 Pivot;
            public Quaternion Q;
            public Vector3 Apply(Vector3 p) => Pivot + Q * (p - Pivot);
        }

        static HipTilt BeginHipTilt(BipedKit k, float deg, float pivotY, float pivotZ = 0f)
        {
            var h = new HipTilt { Pivot = new Vector3(0f, pivotY, pivotZ), Q = Quaternion.Euler(deg, 0f, 0f) };
            k.M.Push().Translate(h.Pivot).Rotate(h.Q).Translate(-h.Pivot);
            return h;
        }

        static readonly int[] HipTiltBones =
        {
            BB.Spine, BB.Chest, BB.Neck, BB.Head, BB.ArmUL, BB.ArmLL, BB.HandL, BB.ArmUR, BB.ArmLR, BB.HandR,
            BB.Cape, BB.HairB, BB.WingL, BB.WingR, BB.DrawnR, BB.StringA, BB.StringB, BB.ArrowR,
        };

        static void EndHipTilt(BipedKit k, HipTilt h)
        {
            k.M.Pop();
            foreach (int b in HipTiltBones) k.Bind[b] = h.Apply(k.Bind[b]);
        }

        /// <summary>After Finish: the true height and the nameplate anchor of a hunched model.</summary>
        static void FinishHipTilt(BipedKit k, HipTilt h, float top)
        {
            var m = k.Model;
            m.HeadTop = h.Q * new Vector3(0f, k.H - k.HeadY, 0f);
            m.Height = top;
        }

        /// <summary>
        /// A cartoon skull on BB.Head: a domed cranium, cheek plate and jaw with a row of teeth, two big dark sockets with
        /// glowing pupils (turned out a little, so they read from the side) and a nose notch.
        /// </summary>
        static void SkullFace(BipedKit k, Color bone, Color boneD, Color socket, Color glow, float glowK = 1f)
        {
            var M = k.M;
            float r = k.R;
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = bone;
            M.Sphere(hc + new Vector3(0f, 0.14f * r, -0.06f * r), new Vector3(0.95f, 0.92f, 1.0f) * r, 10, 7);
            M.Sphere(hc + new Vector3(0f, -0.2f * r, 0.32f * r), new Vector3(0.8f, 0.62f, 0.62f) * r, 9, 6);
            M.Color = Paint.Mix(bone, boneD, 0.35f);
            M.Push().Translate(hc + new Vector3(0f, -0.66f * r, 0.36f * r)).Rotate(-10f, 0f, 0f);
            M.Sphere(Vector3.zero, new Vector3(0.6f * r, 0.26f * r, 0.56f * r), 8, 5);
            M.Pop();
            // teeth
            M.Color = Paint.Shade(bone, 1.05f);
            for (int i = -3; i <= 3; i++)
            {
                float x = i * 0.12f * r;
                float z = 0.36f * r + 0.56f * r * Mathf.Sqrt(Mathf.Max(0.1f, 1f - (x / (0.62f * r)) * (x / (0.62f * r)))) + 0.01f * r;
                M.Box(new Vector3(x, hc.y - 0.52f * r, z), new Vector3(0.09f * r, 0.14f * r, 0.06f * r));
            }
            M.Color = socket;
            M.Box(new Vector3(0f, hc.y - 0.52f * r, 0.88f * r), new Vector3(0.7f * r, 0.035f * r, 0.03f * r));
            // sockets, pupils, nose
            for (int s = -1; s <= 1; s += 2)
            {
                M.Color = socket;
                M.Push().Translate(hc + new Vector3(s * 0.36f * r, -0.02f * r, 0.8f * r)).Rotate(0f, s * 24f, 0f);
                M.Sphere(Vector3.zero, new Vector3(0.27f, 0.29f, 0.16f) * r, 7, 5);
                M.Emission = 1f; M.Color = glow;
                M.Sphere(new Vector3(0f, -0.02f * r, 0.1f * r), new Vector3(0.12f, 0.13f, 0.07f) * r * glowK, 6, 4);
                M.Emission = 0f;
                M.Pop();
            }
            M.Color = socket;
            M.Push().Translate(hc + new Vector3(0f, -0.32f * r, 0.9f * r)).Rotate(0f, 0f, 45f);
            M.Box(Vector3.zero, new Vector3(0.13f, 0.13f, 0.06f) * r);
            M.Pop();
        }

        /// <summary>Ribcage, spine and collarbones (BB.Chest / Spine) and a small pelvis (BB.Hips), round a dark core.</summary>
        static void RibCage(BipedKit k, Color bone, Color boneD, Color dark, int ribs = 4)
        {
            var M = k.M;
            float U = k.U;
            float rr = 0.02f * U;
            // dark core so the cage reads as a body, not as loose sticks
            M.Bone = BB.Chest; M.Color = dark;
            M.Body(Vector3.zero, new[]
            {
                new Vector2(k.WaistR * 0.45f, k.SpineY), new Vector2(k.ChestR * 0.66f, k.ChestY), new Vector2(k.ChestR * 0.55f, k.ShoulderY - 0.05f * U),
            }, 6, k.DepthK, true, true);
            // spine (vertebrae down the back)
            M.Color = boneD;
            for (int i = 0; i < 7; i++)
            {
                float y = Mathf.Lerp(k.NeckY, k.PelvisY + 0.02f * U, i / 6f);
                M.Bone = y > k.SpineY + 0.04f * U ? BB.Chest : BB.Spine;
                M.Sphere(new Vector3(0f, y, -k.ChestR * k.DepthK * 0.45f), new Vector3(0.034f, 0.026f, 0.03f) * U, 5, 3, false);
            }
            M.Bone = BB.Spine;
            M.Segment(new Vector3(0f, k.PelvisY, -0.03f * U), new Vector3(0f, k.SpineY + 0.06f * U, -0.04f * U), 0.022f * U, 0.022f * U, 5);
            // ribs: arcs from the spine round to the sternum, dipping at the front
            M.Bone = BB.Chest; M.Color = bone;
            for (int i = 0; i < ribs; i++)
            {
                float t = i / (float)(ribs - 1);
                float y = Mathf.Lerp(k.ShoulderY - 0.08f * U, k.SpineY + 0.06f * U, t);
                float rx = k.ChestR * Mathf.Lerp(0.98f, 0.88f, t * t);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 P(float thDeg)
                    {
                        float th = thDeg * Mathf.Deg2Rad;
                        float dip = (1f - (thDeg - 15f) / 155f) * 0.05f * U;
                        return new Vector3(s * Mathf.Sin(th) * rx, y - dip, Mathf.Cos(th) * rx * k.DepthK * 1.05f);
                    }
                    var prev = P(166f);
                    foreach (float th in new[] { 125f, 85f, 48f, 16f })
                    {
                        var p = P(th);
                        M.Segment(prev, p, rr, rr, 4);
                        prev = p;
                    }
                }
            }
            // sternum and collarbones
            M.Box(new Vector3(0f, k.ChestY - 0.01f * U, k.ChestR * k.DepthK * 1.0f), new Vector3(0.04f, 0.2f, 0.03f) * U);
            for (int s = -1; s <= 1; s += 2)
                M.Segment(new Vector3(s * 0.03f * U, k.ShoulderY - 0.02f * U, k.ChestR * k.DepthK * 0.8f), new Vector3(s * k.ShoulderX, k.ShoulderY + 0.005f * U, 0f), rr * 1.1f, rr, 4);
            // pelvis
            M.Bone = BB.Hips; M.Color = bone;
            M.Body(Vector3.zero, new[]
            {
                new Vector2(k.HipR * 0.45f, k.HipY - 0.06f * U), new Vector2(k.HipR * 0.92f, k.HipY + 0.0f * U), new Vector2(k.HipR * 0.78f, k.PelvisY + 0.03f * U),
            }, 7, k.DepthK * 0.85f, true, true);
            M.Color = dark;
            M.Sphere(new Vector3(0f, k.HipY - 0.01f * U, k.HipR * k.DepthK * 0.62f), new Vector3(0.05f, 0.04f, 0.02f) * U, 6, 4);
        }

        /// <summary>A bony arm: humerus, a two-bone forearm, knobbly joints and a bony hand round the grip.</summary>
        static void BoneArm(BipedKit k, int s, Color bone, Color boneD, float thick = 1f, bool handOnly = false)
        {
            var M = k.M;
            float U = k.U, x = s * k.ShoulderX;
            float r = 0.022f * U * thick;
            if (!handOnly)
            {
                M.Bone = BB.ArmU(s); M.Color = bone;
                M.Sphere(new Vector3(x, k.ShoulderY - 0.01f * U, 0f), r * 1.9f, 6, 4);
                M.Segment(new Vector3(x, k.ShoulderY, 0f), new Vector3(x, k.ElbowY, 0f), r * 1.15f, r, 5);
                M.Bone = BB.ArmL(s);
                M.Sphere(new Vector3(x, k.ElbowY, 0f), r * 1.55f, 6, 4);
                M.Color = boneD;
                for (int j = -1; j <= 1; j += 2)
                    M.Segment(new Vector3(x + j * r * 0.55f, k.ElbowY, 0f), new Vector3(x + j * r * 0.6f, k.WristY + 0.01f * U, 0f), r * 0.75f, r * 0.7f, 4);
            }
            M.Bone = BB.Hand(s); M.Color = bone;
            var g = k.Grip(s);
            M.Sphere(new Vector3(x, k.WristY - 0.01f * U, 0f), new Vector3(0.034f, 0.03f, 0.03f) * U * thick, 6, 4);
            for (int f = 0; f < 4; f++)
            {
                float fx = x + (f - 1.5f) * 0.016f * U * s;
                var a = new Vector3(fx, k.WristY - 0.03f * U, 0.012f * U);
                M.Curve(a, a + new Vector3(0f, -0.04f * U, 0.035f * U), new Vector3(fx, g.y - 0.035f * U, g.z + 0.02f * U), r * 0.55f, r * 0.4f, 2, 4);
            }
        }

        /// <summary>A bony leg: femur, knee cap, shin bone pair and a skeletal foot.</summary>
        static void BoneLeg(BipedKit k, int s, Color bone, Color boneD, float thick = 1f)
        {
            var M = k.M;
            float U = k.U, x = s * k.HipX;
            float r = 0.026f * U * thick;
            M.Bone = BB.LegU(s); M.Color = bone;
            M.Sphere(new Vector3(x, k.HipY + 0.01f * U, 0f), r * 1.6f, 6, 4);
            M.Segment(new Vector3(x, k.HipY + 0.02f * U, 0f), new Vector3(x, k.KneeY, 0f), r * 1.15f, r, 5);
            M.Bone = BB.LegL(s);
            M.Sphere(new Vector3(x, k.KneeY, 0.008f * U), r * 1.6f, 6, 4);
            M.Color = boneD;
            M.Segment(new Vector3(x - s * r * 0.4f, k.KneeY, 0f), new Vector3(x - s * r * 0.4f, k.AnkleY + 0.02f * U, 0f), r * 0.95f, r * 0.8f, 5);
            M.Segment(new Vector3(x + s * r * 0.7f, k.KneeY, -0.004f * U), new Vector3(x + s * r * 0.6f, k.AnkleY + 0.02f * U, -0.004f * U), r * 0.6f, r * 0.55f, 4);
            M.Bone = BB.Foot(s); M.Color = bone;
            M.Sphere(new Vector3(x, k.AnkleY, -0.01f * U), r * 1.4f, 6, 4);
            M.Box(new Vector3(x, k.AnkleY * 0.45f, 0.04f * U), new Vector3(0.07f, 0.05f, 0.1f) * U * thick);
            for (int t = 0; t < 3; t++)
            {
                float tx = x + (t - 1) * 0.022f * U;
                M.Segment(new Vector3(tx, 0.025f * U, 0.08f * U), new Vector3(tx, 0.012f * U, 0.14f * U), r * 0.55f, r * 0.45f, 4);
            }
        }

        /// <summary>Ragged strips along the hem of a cape (BB.Cape).</summary>
        static void CapeTatters(BipedKit k, float hemY, float width, int n, Color a, Color b, float len = 0.1f, float glow = 0f)
        {
            var M = k.M;
            M.Bone = BB.Cape;
            for (int i = 0; i < n; i++)
            {
                float th = Mathf.Lerp(124f, 236f, i / (float)(n - 1)) * Mathf.Deg2Rad;
                float r = k.ShoulderR * 1.45f * width;
                var p = new Vector3(Mathf.Sin(th) * r, hemY + 0.035f * k.U, Mathf.Cos(th) * r * k.DepthK * 1.25f + 0.02f * k.U);
                M.Color = i % 2 == 0 ? a : b;
                M.Emission = i % 2 == 0 ? glow : 0f;
                M.Blade(p, p + new Vector3(Mathf.Sin(th) * 0.02f, -(len + (i % 3) * len * 0.45f) * k.U, Mathf.Cos(th) * 0.03f), 0.08f * k.U * width);
            }
            M.Emission = 0f;
        }

        /// <summary>A hexagonal crystal / ice shard from `at` along `dir`: a short taper, a prism and a point.</summary>
        static void Crystal(MeshBuilder M, Vector3 at, Vector3 dir, float r, float len, int sides = 5)
        {
            M.Aim(at, dir);
            M.Lathe(new[] { new Vector2(r * 0.55f, 0f), new Vector2(r, len * 0.22f), new Vector2(r * 0.92f, len * 0.68f), new Vector2(0f, len) }, sides, false, true, false);
            M.Pop();
        }

        /// <summary>A double-pointed floating shard centred on `at`.</summary>
        static void Shard(MeshBuilder M, Vector3 at, Vector3 dir, float r, float len, int sides = 4)
        {
            M.Aim(at - dir.normalized * len * 0.35f, dir);
            M.Lathe(new[] { new Vector2(0f, 0f), new Vector2(r, len * 0.35f), new Vector2(r * 0.8f, len * 0.6f), new Vector2(0f, len) }, sides, false, false, false);
            M.Pop();
        }

        // ================================================================== skeletons

        /// <summary>
        /// Barrow / catacomb skeleton: a cartoon skull with big sockets and pale-cyan pupils, a ribcage round a dark core,
        /// knobbly bones, a dented rust helm and a faded rag loincloth. The warrior carries a notched sword and a round
        /// shield of faded teal paint with a rust boss; the archer wears a ragged hood and draws a weathered bow.
        /// </summary>
        static UnitModel Skeleton(string key, bool archer)
        {
            var k = new BipedKit(archer ? 202 : 201, 1.8f, 0.15f, 0.49f, 0.85f, false, 0.95f);
            float U = k.U;
            var M = k.M;
            Color bone = C("#ece2c8"), boneD = C("#c0ae8a"), socket = C("#1c1a26"), glow = C("#8ff4ff"), dark = C("#2c2834"),
                  rust = C("#a4603c"), rustD = C("#6e3c28"), iron = C("#80848c"), leather = C("#5a3e2c"),
                  rag = archer ? C("#5e6e58") : C("#6a5070"), ragD = archer ? C("#3e4a3a") : C("#463450");
            M.Jitter = 0.03f;
            RibCage(k, bone, boneD, dark);
            SkullFace(k, bone, boneD, socket, glow);
            for (int s = -1; s <= 1; s += 2)
            {
                BoneArm(k, s, bone, boneD);
                BoneLeg(k, s, bone, boneD);
            }
            // rag loincloth on a rotten belt (front and back flaps, ragged)
            k.Belt(leather, rust, k.HipY + 0.05f * U, 0.95f, 0.045f);
            k.FrontFlap(rag, k.KneeY + 0.08f * U, 0.13f, null, k.HipY + 0.05f * U);
            k.BackFlap(ragD, k.KneeY + 0.04f * U, 0.17f);
            if (archer)
            {
                k.Hood(rag, ragD, 62f, 0.25f, false);
                // a ragged half-cape on the shoulders
                k.Cape(rag, ragD, k.SpineY, 0.85f);
                CapeTatters(k, k.SpineY, 0.85f, 6, rag, ragD, 0.07f);
                k.Cuff(-1, leather, 0.3f, 0.95f, 1.6f);
                k.Bow(-1, 1.0f, C("#6a5038"), rustD, C("#d8d0c0"), C("#8a7a60"), C("#6a5070"));
                k.Quiver(leather, C("#d8d0c0"));
            }
            else
            {
                k.Helmet(iron, 0f, rust, true);
                // rust streaks and a broken horn stub on the helm
                M.Bone = BB.Head; M.Color = rust;
                M.Box(new Vector3(-0.45f * k.R, k.HeadCY + 0.75f * k.R, 0.55f * k.R), new Vector3(0.3f, 0.12f, 0.3f) * k.R);
                M.Color = boneD;
                M.Spike(new Vector3(0.9f * k.R, k.HeadCY + 0.5f * k.R, -0.05f * k.R), new Vector3(1f, 0.9f, 0f), 0.13f * k.R, 0.38f * k.R, 5);
                k.Pauldron(1, iron, 1.25f, rust, 2);
                M.Jitter = 0f;
                k.Sword(1, 0.72f, 0.075f, C("#b8b4a8"), leather, rust);
                k.Shield(C("#4e7480"), rust, C("#d8c890"), true, 0.95f, C("#6a5038"));
                M.Jitter = 0.03f;
            }
            var m = k.Model;
            if (archer)
            {
                m.HoldL = UnitHold.Bow; m.HoldR = UnitHold.Relaxed; m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Bow;
            }
            else
            {
                m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Sword; m.Ranged = UnitRanged.Throw;
            }
            m.DustColor = new Color(0.7f, 0.66f, 0.58f, 0.3f);
            m.Breath = 0.4f;
            return Done(k, key);
        }

        // ================================================================== barrow wight

        /// <summary>
        /// Barrow wight: a gaunt dead warrior-lord in rusted plate over mail, a tarnished crown-helm, a pale grey mask of a
        /// face (HollowFace) with ghost-blue eyes and a brow mark, long white hair, a grave-shroud cloak with faintly
        /// glowing torn edges, ghost light leaking from the cracks of the breastplate and a rusted longsword. The frost
        /// wight (Frozen Sanctum) is the same warrior rimed in ice: frosted blue steel, icicles, a white shroud.
        /// </summary>
        static UnitModel BarrowWight(string key, bool frost)
        {
            var k = new BipedKit(frost ? 204 : 203, 2.05f, 0.14f, 0.48f, 0.95f, false, 1.06f, 1.12f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color plate = frost ? C("#9ab4c8") : C("#5e6672"), plateD = frost ? C("#5a7690") : C("#3c424c"), mail = frost ? C("#6c7c8c") : C("#4e5258"),
                  mailD = frost ? C("#465564") : C("#33363c"), trim = frost ? C("#e6f6ff") : C("#a8884e"), rust = frost ? C("#dff4ff") : C("#a65a34"),
                  shroud = frost ? C("#d4e2ec") : C("#3c4a5c"), shroudD = frost ? C("#8aa2b6") : C("#232c38"), mask = frost ? C("#dfeaf2") : C("#a9b4b8"),
                  glow = frost ? C("#c6f6ff") : C("#8fe6ff"), hair = C("#e8eef2"), leather = C("#3a302e"), hand = frost ? C("#a8bccc") : C("#7e8a90");
            M.Jitter = 0.04f;
            k.Torso(plate, mailD);
            // breastplate ridge, rust/frost patches and ghost light leaking from cracks
            M.Bone = BB.Chest; M.Color = plateD;
            M.Box(new Vector3(0f, k.ChestY, k.ChestR * k.DepthK * 0.98f), new Vector3(0.03f, 0.22f, 0.03f) * U);
            M.Emission = 0.9f; M.Color = glow;
            for (int i = 0; i < 3; i++)
            {
                M.Push().Translate((i - 1) * 0.07f * U, k.ChestY - 0.03f * U + (i % 2) * 0.05f * U, k.ChestR * k.DepthK * 0.99f).Rotate(0f, 0f, 30f - i * 25f);
                M.Box(Vector3.zero, new Vector3(0.016f, 0.09f, 0.02f) * U);
                M.Pop();
            }
            M.Emission = 0f;
            M.Color = rust;
            M.Push().Translate(-0.09f * U, k.ChestY + 0.07f * U, k.ChestR * k.DepthK * 0.97f).Rotate(0f, -20f, 15f);
            M.Box(Vector3.zero, new Vector3(0.08f, 0.05f, 0.02f) * U);
            M.Pop();
            M.Push().Translate(0.1f * U, k.ChestY - 0.08f * U, k.ChestR * k.DepthK * 0.95f).Rotate(0f, 22f, -10f);
            M.Box(Vector3.zero, new Vector3(0.06f, 0.07f, 0.02f) * U);
            M.Pop();
            k.Neck(C("#2a2e36"));
            HollowFace(k, mask, glow, 0.98f);
            k.Eyes(glow, new Color(0, 0, 0, 0), EyeStyle.Glow, 0f, -0.08f, 0.36f, 1.55f);
            // long thin white hair falling from under the helm
            M.Bone = BB.HairB; M.Color = hair;
            for (int i = 0; i < 7; i++)
            {
                float th = Mathf.Lerp(100f, 260f, i / 6f) * Mathf.Deg2Rad;
                var a = new Vector3(Mathf.Sin(th) * 0.92f * r, k.HeadCY - 0.1f * r, Mathf.Cos(th) * 0.9f * r - 0.05f * r);
                M.Blade(a, a + new Vector3(Mathf.Sin(th) * 0.18f * r, -1.9f * r - (i % 2) * 0.4f * r, Mathf.Cos(th) * 0.35f * r), 0.3f * r);
            }
            // crown-helm: a skull cap with a tarnished band of spikes and cheek guards
            k.Helmet(plate, 0f, trim);
            M.Bone = BB.Head; M.Color = trim;
            for (int i = 0; i < 7; i++)
            {
                float th = Mathf.Lerp(-80f, 80f, i / 6f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(th) * 1.1f * r, k.HeadCY + 0.48f * r, Mathf.Cos(th) * 1.1f * r - 0.03f * r);
                M.Spike(p, new Vector3(Mathf.Sin(th) * 0.25f, 1f, Mathf.Cos(th) * 0.25f), 0.1f * r, (i == 3 ? 0.62f : 0.4f) * r, 4);
            }
            // a ghost-blue wisp streaming back from each eye
            M.Bone = BB.HairB; M.Emission = 0.8f; M.Color = glow;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = new Vector3(s * 0.42f * r, k.HeadCY - 0.08f * r, 0.75f * r);
                M.Blade(e, e + new Vector3(s * 0.35f * r, 0.12f * r, -1.3f * r), 0.16f * r);
            }
            M.Emission = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, mail, mailD, hand);
                k.Cuff(s, plate, 0.35f, 0.95f, 1.35f);
                k.Pauldron(s, plate, 1.35f, rust, 2);
                k.Leg(s, mail, plate, plateD, 0.62f, plateD);
            }
            k.Skirt(mail, mailD, k.KneeY - 0.02f * U, 1.28f, 18f, plateD);
            k.Belt(leather, trim);
            // grave-shroud cloak, torn, the torn ends faintly glowing
            k.Cape(shroud, shroudD, k.AnkleY + 0.2f * U, 1.05f);
            CapeTatters(k, k.AnkleY + 0.2f * U, 1.05f, 8, shroud, shroudD, 0.12f, 0.35f);
            if (frost)
            {
                // icicles under the pauldrons, the belt and the helm
                M.Emission = 0.25f; M.Color = C("#dff6ff");
                for (int s = -1; s <= 1; s += 2)
                {
                    M.Bone = BB.ArmU(s);
                    for (int i = 0; i < 3; i++)
                        M.Spike(new Vector3(s * (k.ShoulderX + 0.05f * U), k.ShoulderY - 0.07f * U, (i - 1) * 0.04f * U), Vector3.down, 0.014f * U, (0.08f + i % 2 * 0.05f) * U, 4);
                }
                M.Bone = BB.Head;
                for (int i = 0; i < 5; i++)
                {
                    float th = Mathf.Lerp(-60f, 60f, i / 4f) * Mathf.Deg2Rad;
                    M.Spike(new Vector3(Mathf.Sin(th) * 1.12f * r, k.HeadCY + 0.36f * r, Mathf.Cos(th) * 1.1f * r), Vector3.down, 0.05f * r, 0.32f * r, 4);
                }
                M.Emission = 0f;
            }
            M.Jitter = 0f;
            k.Sword(1, 0.86f, 0.072f, frost ? C("#cfe6f2") : C("#a89a8a"), leather, trim, true);
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Sword; m.Ranged = UnitRanged.Point;
            m.DustColor = new Color(0.62f, 0.66f, 0.7f, 0.3f);
            m.Breath = 0.5f;
            m.TurnRate = 520f;
            var mm = Done(k, key);
            mm.HeadTop = new Vector3(0f, k.H - k.HeadY + 0.1f * U, 0f);
            return mm;
        }

        // ================================================================== bog ghoul

        /// <summary>
        /// Mirefen bog ghoul: a hunched grey-green ghoul, long arms dangling forward to clawed hands, a hump crusted with
        /// moss and a tuft of reeds, lank black hair, a gaping toothy mouth, sickly yellow-green glowing eyes, a rotten
        /// sackcloth loincloth and legs caked in mud to the knee, with big splayed clawed feet.
        /// </summary>
        static UnitModel BogGhoul(string key)
        {
            var k = new BipedKit(205, 1.85f, 0.16f, 0.45f, 1.0f, false, 1.02f, 1.42f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color skin = C("#8a9a78"), skinD = C("#5e6e52"), skinL = C("#a6b48e"), mud = C("#5a4430"), mudD = C("#3e2e22"),
                  moss = C("#5f8a3e"), mossL = C("#86ac52"), reed = C("#a89a58"), hair = C("#1e2420"), sack = C("#7a6a4c"),
                  sackD = C("#544834"), claw = C("#e8dcc0"), eye = C("#e6f56a"), mouth = C("#2a1a1e"), teeth = C("#e8e0c4");
            M.Jitter = 0.07f;
            // legs (upright): skinny, mud to the knee, big clawed feet
            for (int s = -1; s <= 1; s += 2)
            {
                k.Leg(s, skin, mud, mudD, 0f);
                k.LegCuff(s, mud, 0.1f, 1.25f, true);
                M.Bone = BB.Foot(s); M.Color = mudD;
                M.Blob(new Vector3(s * k.HipX, k.AnkleY * 0.6f, 0.04f * U), new Vector3(0.06f, 0.045f, 0.1f) * U, 0, 0.2f, 3 + s);
                M.Color = claw;
                for (int t = -1; t <= 1; t++)
                    M.Spike(new Vector3(s * k.HipX + t * 0.03f * U, 0.02f * U, 0.13f * U), new Vector3(t * 0.2f, -0.3f, 1f), 0.012f * U, 0.05f * U, 4);
            }
            var h = BeginHipTilt(k, 32f, k.HipY);
            k.Torso(skin, sackD, skinL, 0.12f);
            // the hump: moss and a tuft of reeds
            M.Bone = BB.Chest; M.Color = skinD;
            M.Blob(new Vector3(0f, k.ShoulderY - 0.06f * U, -k.ChestR * k.DepthK * 0.7f), new Vector3(0.17f, 0.15f, 0.14f) * U, 1, 0.12f, 7);
            M.Color = moss;
            M.Blob(new Vector3(0.02f * U, k.ShoulderY + 0.02f * U, -k.ChestR * k.DepthK * 0.85f), new Vector3(0.15f, 0.09f, 0.12f) * U, 0, 0.3f, 8);
            M.Color = mossL;
            M.Blob(new Vector3(-0.08f * U, k.ShoulderY - 0.02f * U, -k.ChestR * k.DepthK * 0.95f), new Vector3(0.08f, 0.06f, 0.07f) * U, 0, 0.3f, 9);
            M.Color = reed;
            for (int i = 0; i < 5; i++)
            {
                var a = new Vector3((i - 2) * 0.025f * U, k.ShoulderY + 0.04f * U, -k.ChestR * k.DepthK * 0.95f);
                M.Blade(a, a + new Vector3((i - 2) * 0.04f * U, 0.22f * U + (i % 2) * 0.06f * U, -0.08f * U), 0.025f * U);
            }
            // ribs showing through the belly skin
            M.Bone = BB.Spine; M.Color = skinD;
            for (int i = 0; i < 3; i++)
                M.Box(new Vector3(0f, k.SpineY + 0.02f * U - i * 0.035f * U, k.WaistR * k.DepthK * 1.08f), new Vector3(0.14f - i * 0.02f, 0.012f, 0.015f) * U);
            k.Neck(skinD);
            // head thrust forward: heavy brow, glowing eyes, a gaping mouth full of teeth, lank hair
            k.Head(skin, eye, skinD, EyeStyle.Glow, 24f, true, 1.15f);
            M.Bone = BB.Head;
            var mc = k.FacePoint(0f, -0.52f, 0.02f);
            M.Color = mouth;
            M.Sphere(mc, new Vector3(0.36f, 0.22f, 0.14f) * r, 8, 5);
            M.Color = teeth;
            for (int i = -2; i <= 2; i++)
            {
                M.Spike(mc + new Vector3(i * 0.12f * r, 0.17f * r, 0.06f * r), Vector3.down, 0.04f * r, 0.15f * r, 3);
                if (i != 0) M.Spike(mc + new Vector3(i * 0.13f * r, -0.17f * r, 0.06f * r), Vector3.up, 0.035f * r, 0.12f * r, 3);
            }
            M.Color = skinD;
            M.Box(new Vector3(0f, k.HeadCY + 0.18f * r, 0.86f * r), new Vector3(1.2f * r, 0.16f * r, 0.22f * r));
            // a bald mottled crown, lank strands from the sides and back
            M.Color = skinD;
            M.Sphere(new Vector3(-0.3f * r, k.HeadCY + 0.82f * r, 0.2f * r), new Vector3(0.22f, 0.1f, 0.2f) * r, 5, 3);
            M.Sphere(new Vector3(0.25f * r, k.HeadCY + 0.6f * r, -0.55f * r), new Vector3(0.2f, 0.12f, 0.18f) * r, 5, 3);
            M.Bone = BB.HairB; M.Color = hair;
            for (int i = 0; i < 9; i++)
            {
                float th = Mathf.Lerp(80f, 280f, i / 8f) * Mathf.Deg2Rad;
                var a = new Vector3(Mathf.Sin(th) * 0.9f * r, k.HeadCY + 0.45f * r, Mathf.Cos(th) * 0.85f * r - 0.05f * r);
                M.Blade(a, a + new Vector3(Mathf.Sin(th) * 0.25f * r, -1.5f * r - (i % 3) * 0.35f * r, Mathf.Cos(th) * 0.2f * r), 0.28f * r);
            }
            M.Color = moss;
            M.Blob(new Vector3(0.35f * r, k.HeadCY + 0.75f * r, -0.2f * r), new Vector3(0.35f, 0.18f, 0.3f) * r, 0, 0.3f, 12);
            // long arms, knobbly elbows, long claws
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, skinL, skinD);
                M.Bone = BB.ArmL(s); M.Color = mud;
                M.Segment(new Vector3(s * k.ShoulderX, k.WristY + 0.1f * U, 0f), new Vector3(s * k.ShoulderX, k.WristY + 0.02f * U, 0f), k.ForeR * 1.12f, k.ForeR * 1.0f, 6);
                M.Bone = BB.Hand(s); M.Color = claw;
                var g = k.Grip(s);
                for (int f = -1; f <= 1; f++)
                    M.Curve(g + new Vector3(f * 0.022f * U, 0.02f * U, 0.02f * U), g + new Vector3(f * 0.03f * U, -0.06f * U, 0.05f * U),
                            g + new Vector3(f * 0.035f * U, -0.12f * U, 0.0f), 0.012f * U, 0.003f * U, 3, 4);
                M.Bone = BB.ArmU(s); M.Color = moss;
                M.Blob(new Vector3(s * (k.ShoulderX + 0.01f * U), k.ShoulderY + 0.02f * U, -0.01f * U), new Vector3(0.08f, 0.05f, 0.08f) * U, 0, 0.3f, 20 + s);
            }
            EndHipTilt(k, h);
            // rotten sackcloth loincloth (hips, not hunched)
            k.Belt(sackD, mudD, k.HipY + 0.04f * U, 1.0f, 0.05f);
            k.FrontFlap(sack, k.KneeY + 0.06f * U, 0.15f, null, k.HipY + 0.04f * U);
            k.BackFlap(sackD, k.KneeY + 0.02f * U, 0.2f);
            var m = k.Model;
            m.HoldR = UnitHold.Claws; m.HoldL = UnitHold.Claws; m.Strike = UnitStrike.Claw; m.Ranged = UnitRanged.Throw;
            m.DustColor = new Color(0.4f, 0.34f, 0.24f, 0.35f);
            m.StrideK = 0.9f;
            var mm = Done(k, key);
            FinishHipTilt(k, h, h.Apply(new Vector3(0f, k.H, 0f)).y);
            return mm;
        }

        // ================================================================== hollow knight

        /// <summary>
        /// Hollow knight (Lantern Catacombs): a knight emptied by the hollowing — ashen grey plate, a closed great helm with
        /// a cross slit glowing violet from the empty inside, a tattered violet-grey tabard and cloak, a plain longsword
        /// and, hanging from the left fist on a short chain, a dark iron lantern burning a low violet flame.
        /// </summary>
        static UnitModel HollowKnight(string key)
        {
            var k = new BipedKit(206, 2.0f, 0.14f, 0.49f, 1.15f, false, 1.1f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color plate = C("#8e9098"), plateD = C("#5c5e68"), plateL = C("#b2b4bc"), cloth = C("#4e4666"), clothD = C("#2e2a40"),
                  glow = C("#c9a8ff"), leather = C("#3e3438"), iron = C("#2e2a30"), dark = C("#14121a");
            M.Jitter = 0.03f;
            k.Torso(plate, plateD);
            M.Bone = BB.Chest; M.Color = plateL;
            M.Box(new Vector3(0f, k.ChestY + 0.02f * U, k.ChestR * k.DepthK * 0.98f), new Vector3(0.025f, 0.2f, 0.03f) * U);
            k.Neck(plateD);
            // closed great helm: a flat-topped barrel with a cross-shaped glowing slit and a tattered crest
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = plate;
            M.Push().Translate(hc + new Vector3(0f, 0f, -0.02f * r));
            M.Body(Vector3.zero, new[]
            {
                new Vector2(0.92f * r, -1.08f * r), new Vector2(1.1f * r, -0.4f * r), new Vector2(1.12f * r, 0.45f * r),
                new Vector2(0.95f * r, 0.95f * r), new Vector2(0.001f, 1.06f * r),
            }, 10, 1f, true, true);
            M.Pop();
            M.Color = plateL;
            M.Box(new Vector3(0f, hc.y + 0.1f * r, 1.08f * r), new Vector3(0.14f * r, 1.7f * r, 0.08f * r));
            M.Color = dark;
            M.Box(new Vector3(0f, hc.y + 0.02f * r, 1.1f * r), new Vector3(1.5f * r, 0.17f * r, 0.08f * r));
            M.Box(new Vector3(0f, hc.y - 0.3f * r, 1.11f * r), new Vector3(0.12f * r, 0.6f * r, 0.08f * r));
            M.Emission = 1f; M.Color = glow;
            for (int s = -1; s <= 1; s += 2)
                M.Box(new Vector3(s * 0.38f * r, hc.y + 0.02f * r, 1.14f * r), new Vector3(0.3f * r, 0.09f * r, 0.05f * r));
            M.Emission = 0f;
            M.Color = plateD;
            for (int i = 0; i < 6; i++)
                M.Sphere(new Vector3((i % 3 - 1) * 0.3f * r, hc.y - 0.6f * r - (i / 3) * 0.18f * r, 1.04f * r), 0.05f * r, 4, 3);
            M.Bone = BB.HairB; M.Color = cloth;
            for (int i = 0; i < 5; i++)
            {
                var a = new Vector3(0f, hc.y + 1.0f * r, (0.5f - i * 0.28f) * r);
                M.Blade(a, a + new Vector3((i % 2 == 0 ? 0.08f : -0.08f) * r, 0.25f * r - i * 0.15f * r, -0.9f * r - i * 0.1f * r), 0.42f * r);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, plate, plateD, plateD);
                k.Cuff(s, plate, 0.45f, 0.98f, 1.4f);
                k.Leg(s, plateD, plate, plateD, 0.7f, plateL);
            }
            k.BigPauldron(1, plate, plateL, clothD, 1.8f);
            k.Pauldron(-1, plate, 1.3f, plateL, 3);
            k.Skirt(cloth, clothD, k.KneeY - 0.04f * U, 1.25f, 34f);
            k.FrontFlap(cloth, k.KneeY - 0.1f * U, 0.13f, glow);
            HollowTatters(k, 8, k.KneeY - 0.02f * U, k.HipR * 1.32f, cloth, clothD, glow);
            k.Belt(leather, plateL);
            k.Cape(cloth, clothD, k.AnkleY + 0.25f * U, 1.0f);
            CapeTatters(k, k.AnkleY + 0.25f * U, 1.0f, 7, cloth, clothD, 0.1f, 0.3f);
            M.Jitter = 0f;
            k.Sword(1, 0.86f, 0.075f, C("#c4c8d0"), leather, plateD);
            // the dark lantern on a chain from the left fist
            M.Bone = BB.HandL;
            var g = k.Grip(-1);
            M.Color = iron;
            M.Segment(g + new Vector3(0f, 0.01f * U, 0f), g + new Vector3(0f, -0.1f * U, 0.01f * U), 0.006f * U, 0.006f * U, 4);
            var lamp = g + new Vector3(0f, -0.19f * U, 0.01f * U);
            M.Cylinder(lamp + new Vector3(0f, 0.07f * U, 0f), 0.05f * U, 0.025f * U, 0.035f * U, 6);
            M.Cylinder(lamp + new Vector3(0f, -0.085f * U, 0f), 0.055f * U, 0.055f * U, 0.02f * U, 6);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(a) * 0.05f * U, 0f, Mathf.Sin(a) * 0.05f * U);
                M.Segment(lamp + c + new Vector3(0f, -0.07f * U, 0f), lamp + c + new Vector3(0f, 0.07f * U, 0f), 0.007f * U, 0.007f * U, 3);
            }
            M.Emission = 0.85f; M.Color = C("#8f6ad8");
            M.Sphere(lamp, new Vector3(0.042f, 0.06f, 0.042f) * U, 6, 4);
            M.Emission = 1f; M.Color = C("#e4d4ff");
            M.Sphere(lamp + new Vector3(0f, -0.01f * U, 0f), 0.02f * U, 5, 3);
            M.Emission = 0f;
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Sword; m.Ranged = UnitRanged.Point;
            m.CastBone = BB.HandL; m.CastOffset = lamp - k.Bind[BB.HandL];
            m.DustColor = new Color(0.6f, 0.6f, 0.64f, 0.3f);
            m.Heavy = 0.25f;
            m.Breath = 0.5f;
            return Done(k, key);
        }

        // ================================================================== the Lantern Lich

        /// <summary>
        /// The Lantern Lich (boss of the Lantern Catacombs): a tall floating lantern-keeper long dead — a skull under a
        /// peaked indigo hood and a bone-and-gold mitre crown, a high gold-lined collar, layered robes fading into glowing
        /// tatters, skeletal hands, a soul lantern hung over the breastbone, a gold halo ring of candle flames standing
        /// behind the shoulders, and a tall crook staff hung with a great caged lantern burning lavender soulfire.
        /// </summary>
        static UnitModel LanternLich(string key)
        {
            var k = new BipedKit(207, 2.5f, 0.16f, 0.4f, 1.2f, false, 1.12f, 1.25f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color robe = C("#4a3c6c"), robeD = C("#2a2240"), robeL = C("#6a5a94"), gold = C("#e2b65c"), goldD = C("#a07a38"),
                  bone = C("#ece2c8"), boneD = C("#c0ae8a"), socket = C("#1a1622"), glow = C("#d9c2ff"), fire = C("#b48cff"),
                  candle = C("#f4ead2"), flame = C("#ffd27a"), iron = C("#2c2632");
            k.HemGlow = 0.6f;
            k.Torso(robeL, robeD);
            // a gold-edged stole down the front
            M.Bone = BB.Chest; M.Color = gold;
            for (int s = -1; s <= 1; s += 2)
                M.Box(new Vector3(s * 0.055f * U, k.ChestY - 0.02f * U, k.ChestR * k.DepthK * 0.99f), new Vector3(0.035f, 0.3f, 0.015f) * U);
            k.Neck(C("#2a2234"));
            SkullFace(k, bone, boneD, socket, glow, 1.2f);
            k.Hood(robe, robeD, 64f, 0.2f, true);
            // the mitre crown: a gold band, tall bone tines and a glowing gem
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = gold;
            M.Band(new Vector3(0f, 0f, -0.06f * r), 1.2f * r, 1.16f * r, hc.y + 0.62f * r, hc.y + 0.82f * r, 10, 1f);
            M.Color = bone;
            for (int i = 0; i < 5; i++)
            {
                float th = Mathf.Lerp(-70f, 70f, i / 4f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(th) * 1.16f * r, hc.y + 0.78f * r, Mathf.Cos(th) * 1.16f * r - 0.06f * r);
                float len = (i == 2 ? 1.25f : (i % 2 == 1 ? 0.85f : 0.6f)) * r;
                M.Spike(p, new Vector3(Mathf.Sin(th) * 0.35f, 1f, Mathf.Cos(th) * 0.1f), 0.12f * r, len, 4);
            }
            M.Emission = 1f; M.Color = glow;
            M.Sphere(new Vector3(0f, hc.y + 0.72f * r, 1.2f * r), new Vector3(0.14f, 0.16f, 0.08f) * r, 6, 4);
            M.Emission = 0f;
            k.Collar(robeD, gold, 0.2f, 34f, 1.5f);
            // the halo of candles standing behind the shoulders (Cape bone, sways a little)
            M.Bone = BB.Cape;
            var ring = new Vector3(0f, k.HeadCY + 0.1f * r, -k.ChestR * k.DepthK - 0.12f * U);
            M.Color = gold;
            M.Push().Translate(ring).Rotate(90f, 0f, 0f);
            M.Torus(Vector3.zero, 0.34f * U, 0.016f * U, 16, 4);
            M.Pop();
            M.Segment(ring + new Vector3(0f, -0.34f * U, 0f), new Vector3(0f, k.ShoulderY - 0.02f * U, ring.z + 0.02f * U), 0.018f * U, 0.02f * U, 4);
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.Lerp(-120f, 120f, i / 6f) * Mathf.Deg2Rad;
                var p = ring + new Vector3(Mathf.Sin(a) * 0.34f * U, Mathf.Cos(a) * 0.34f * U, 0f);
                float ch = (0.07f + (i % 2) * 0.03f) * U;
                M.Color = candle;
                M.Cylinder(p, 0.022f * U, 0.02f * U, ch, 5);
                M.Emission = 1f; M.Color = i % 2 == 0 ? glow : flame;
                M.Blade(p + new Vector3(0f, ch, 0f), p + new Vector3(0f, ch + 0.08f * U, 0f), 0.04f * U, Vector3.right);
                M.Emission = 0f;
            }
            // the soul lantern over the breastbone
            M.Bone = BB.Chest;
            var sl = new Vector3(0f, k.ChestY - 0.04f * U, k.ChestR * k.DepthK + 0.05f * U);
            M.Color = gold;
            M.Segment(new Vector3(-0.08f * U, k.ShoulderY - 0.02f * U, k.ChestR * k.DepthK * 0.8f), sl + new Vector3(0f, 0.06f * U, 0f), 0.006f * U, 0.006f * U, 3);
            M.Segment(new Vector3(0.08f * U, k.ShoulderY - 0.02f * U, k.ChestR * k.DepthK * 0.8f), sl + new Vector3(0f, 0.06f * U, 0f), 0.006f * U, 0.006f * U, 3);
            M.Cylinder(sl + new Vector3(0f, 0.04f * U, 0f), 0.04f * U, 0.02f * U, 0.03f * U, 6);
            M.Cylinder(sl + new Vector3(0f, -0.06f * U, 0f), 0.04f * U, 0.04f * U, 0.015f * U, 6);
            M.Emission = 1f; M.Color = glow;
            M.Sphere(sl, new Vector3(0.036f, 0.05f, 0.036f) * U, 6, 4);
            M.Emission = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, robe, robeD, boneD);
                BoneArm(k, s, bone, boneD, 1.3f, true);
                k.WideSleeve(s, robe, robeD, 0.4f, 2.5f, gold);
            }
            k.Skirt(robe, robeD, 0.24f * U, 1.7f, 0f, gold);
            HollowTatters(k, 12, 0.27f * U, k.HipR * 1.75f, robe, robeL, fire);
            k.Belt(goldD, gold, -1f, 1.12f, 0.06f);
            // the crook staff and its great caged lantern
            k.Staff(1, 2.2f, iron, BipedKit.StaffTop.Crook, gold, glow);
            float above = 2.2f * 0.58f * U;
            var hook = new Vector3(0f, above + 0.13f * U, 0.13f * U);
            var lamp = hook + new Vector3(0f, -0.26f * U, 0f);
            k.BeginHand(1);
            M.Color = gold;
            M.Segment(hook, hook + new Vector3(0f, -0.11f * U, 0f), 0.007f * U, 0.007f * U, 4);
            M.Cylinder(lamp + new Vector3(0f, 0.1f * U, 0f), 0.08f * U, 0.035f * U, 0.05f * U, 6);
            M.Cylinder(lamp + new Vector3(0f, -0.13f * U, 0f), 0.075f * U, 0.07f * U, 0.03f * U, 6);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                var c = new Vector3(Mathf.Cos(a) * 0.07f * U, 0f, Mathf.Sin(a) * 0.07f * U);
                M.Segment(lamp + c + new Vector3(0f, -0.1f * U, 0f), lamp + c + new Vector3(0f, 0.1f * U, 0f), 0.008f * U, 0.008f * U, 3);
            }
            M.Emission = 1f; M.Color = fire;
            M.Sphere(lamp, new Vector3(0.06f, 0.09f, 0.06f) * U, 7, 5);
            M.Color = C("#f4ecff");
            M.Blade(lamp + new Vector3(0f, -0.04f * U, 0f), lamp + new Vector3(0f, 0.07f * U, 0f), 0.05f * U, Vector3.right);
            M.Emission = 0f;
            k.End();
            var m = k.Model;
            m.CastBone = BB.HandR;
            m.CastOffset = k.Grip(1) - k.Bind[BB.HandR] + new Vector3(lamp.x, -lamp.z, lamp.y);
            m.FloatHeight = 0.35f;
            m.BaseFade = 1f;
            m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            m.HoldR = UnitHold.Staff;
            m.Dust = false;
            m.TurnRate = 420f;
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.HeadTop = new Vector3(0f, k.H - k.HeadY + 0.75f * r, 0f);
            return UnitModels.Bake(m, k.M);
        }

        // ================================================================== drowned sentinel

        /// <summary>Barnacles: a cluster of little white cones with dark mouths around `at` (the current bone).</summary>
        static void Barnacles(MeshBuilder M, Vector3 at, float size, int n, Color shell, Color mouth, int seed)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (i * 137.5f + seed * 41f) * Mathf.Deg2Rad;
                float d = size * (0.2f + 0.8f * ((i * 7 + seed + 50) % 5) / 4f);
                var p = at + new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a * 1.3f) * d * 0.6f, Mathf.Sin(a) * d * 0.5f);
                float br = size * (0.32f + 0.12f * (i % 3));
                M.Color = shell;
                M.Cylinder(p, br, br * 0.55f, br * 0.9f, 5, false, false, true);
                M.Color = mouth;
                M.Disc(p + new Vector3(0f, br * 0.91f, 0f), br * 0.36f, 5);
            }
        }

        /// <summary>A hanging seaweed strand: a wavy ribbon from `at` downwards.</summary>
        static void Seaweed(MeshBuilder M, Vector3 at, float len, float w, Color c, float sway)
        {
            M.Color = c;
            var mid = at + new Vector3(sway * len * 0.35f, -len * 0.5f, len * 0.08f);
            M.CurvedStrip(at, mid, at + new Vector3(-sway * len * 0.1f, -len, 0f), w, w * 0.45f, Vector3.forward, w * 0.15f, 3);
        }

        /// <summary>
        /// Drowned sentinel (the Drowned Vault): a vault guardian that has stood under the water for centuries — verdigris
        /// bronze plate crusted with barnacles, a round helm with a glowing sea-green porthole visor and a fin crest,
        /// seaweed hanging from the shoulders, belt and arms, water dripping from every edge, and a barnacled trident.
        /// </summary>
        static UnitModel DrownedSentinel(string key)
        {
            var k = new BipedKit(208, 2.2f, 0.15f, 0.48f, 1.3f, false, 1.15f, 1.1f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color verd = C("#5f8f7e"), verdD = C("#3c5e56"), verdL = C("#8cbca6"), bronze = C("#a0804e"), barn = C("#e6e2d4"),
                  barnD = C("#3a3a3a"), weed = C("#2e5e3a"), weedL = C("#4f8a46"), glow = C("#7ff8e4"), dark = C("#0e1a1c"),
                  drip = C("#9ff4ff"), leather = C("#3a3428");
            M.Jitter = 0.05f;
            k.Torso(verd, verdD);
            M.Bone = BB.Chest; M.Color = bronze;
            M.Band(Vector3.zero, k.ChestR * 1.04f, k.ChestR * 1.02f, k.ChestY + 0.04f * U, k.ChestY + 0.07f * U, 9, k.DepthK);
            Barnacles(M, new Vector3(0.08f * U, k.ChestY - 0.06f * U, k.ChestR * k.DepthK * 0.95f), 0.05f * U, 5, barn, barnD, 1);
            k.Neck(verdD);
            // round helm with a porthole visor and a fin crest
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = verd;
            M.Sphere(hc + new Vector3(0f, 0.05f * r, 0f), new Vector3(1.22f, 1.22f, 1.2f) * r, 10, 7);
            M.Color = bronze;
            M.Push().Translate(hc + new Vector3(0f, -0.02f * r, 1.08f * r)).Rotate(90f, 0f, 0f);
            M.Torus(Vector3.zero, 0.52f * r, 0.1f * r, 10, 4);
            M.Pop();
            M.Color = dark;
            M.Sphere(hc + new Vector3(0f, -0.02f * r, 1.04f * r), new Vector3(0.5f, 0.5f, 0.16f) * r, 8, 4);
            M.Emission = 1f; M.Color = glow;
            for (int s = -1; s <= 1; s += 2)
                M.Sphere(hc + new Vector3(s * 0.2f * r, 0.02f * r, 1.16f * r), new Vector3(0.12f, 0.08f, 0.05f) * r, 6, 3);
            M.Emission = 0f;
            M.Color = bronze;
            M.Band(Vector3.zero, 1.24f * r, 1.24f * r, hc.y - 0.75f * r, hc.y - 0.6f * r, 10, 1f);
            M.Color = verdL;
            M.Push().Translate(hc + new Vector3(0f, 1.1f * r, -0.1f * r)).Rotate(0f, 90f, 0f);
            M.Flat(new[] { new Vector2(-1.0f * r, -0.1f * r), new Vector2(0.9f * r, -0.1f * r), new Vector2(0.4f * r, 0.45f * r), new Vector2(-0.3f * r, 0.75f * r), new Vector2(-1.2f * r, 0.35f * r) }, 0.08f * r);
            M.Pop();
            Barnacles(M, hc + new Vector3(-0.7f * r, 0.6f * r, 0.3f * r), 0.28f * r, 4, barn, barnD, 2);
            M.Bone = BB.HairB;
            Seaweed(M, hc + new Vector3(0.7f * r, 0.7f * r, -0.5f * r), 1.8f * r, 0.3f * r, weed, 0.3f);
            Seaweed(M, hc + new Vector3(-0.3f * r, 0.9f * r, -0.7f * r), 1.4f * r, 0.26f * r, weedL, -0.3f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, verd, verdD, verdD);
                k.Cuff(s, verd, 0.4f, 0.98f, 1.38f);
                k.Pauldron(s, verd, 1.5f, bronze, 2);
                k.Leg(s, verdD, verd, verdD, 0.68f, bronze);
                M.Bone = BB.ArmU(s);
                Barnacles(M, new Vector3(s * (k.ShoulderX + 0.04f * U), k.ShoulderY + 0.05f * U, 0f), 0.05f * U, 4, barn, barnD, 3 + s);
                Seaweed(M, new Vector3(s * (k.ShoulderX + 0.09f * U), k.ShoulderY - 0.02f * U, -0.03f * U), 0.36f * U, 0.06f * U, s > 0 ? weed : weedL, s * 0.4f);
                M.Bone = BB.ArmL(s);
                Seaweed(M, new Vector3(s * (k.ShoulderX + k.ForeR * 1.3f), k.ElbowY - 0.05f * U, 0f), 0.22f * U, 0.045f * U, weedL, s * 0.3f);
                M.Bone = BB.LegL(s);
                Barnacles(M, new Vector3(s * (k.HipX + k.ShinR), k.KneeY - 0.12f * U, 0.03f * U), 0.04f * U, 3, barn, barnD, 6 + s);
                // drips from the gauntlet and the pauldron rim
                M.Bone = BB.Hand(s); M.Emission = 0.6f; M.Color = drip;
                M.Sphere(new Vector3(s * k.ShoulderX, k.WristY - 0.16f * U, 0.02f * U), new Vector3(0.012f, 0.022f, 0.012f) * U, 4, 3);
                M.Bone = BB.ArmU(s);
                M.Sphere(new Vector3(s * (k.ShoulderX + 0.1f * U), k.ShoulderY - 0.13f * U, 0.02f * U), new Vector3(0.012f, 0.024f, 0.012f) * U, 4, 3);
                M.Emission = 0f;
            }
            k.Skirt(verdD, dark, k.KneeY + 0.02f * U, 1.32f, 26f, bronze);
            k.Belt(leather, bronze, -1f, 1.12f, 0.07f);
            for (int i = 0; i < 5; i++)
            {
                float th = Mathf.Lerp(-70f, 70f, i / 4f) * Mathf.Deg2Rad;
                var a = new Vector3(Mathf.Sin(th) * k.HipR * 1.16f, k.HipY + 0.05f * U, Mathf.Cos(th) * k.HipR * k.DepthK * 1.2f);
                M.Bone = th < -0.3f ? BB.SkirtL : (th > 0.3f ? BB.SkirtR : BB.SkirtF);
                Seaweed(M, a, (0.2f + (i % 2) * 0.12f) * U, 0.05f * U, i % 2 == 0 ? weed : weedL, (i - 2) * 0.2f);
            }
            // water streaks running down the breastplate
            M.Bone = BB.Chest; M.Emission = 0.45f; M.Color = drip;
            for (int i = 0; i < 3; i++)
                M.Box(new Vector3((i - 1) * 0.09f * U, k.ChestY - 0.06f * U - (i % 2) * 0.03f * U, k.ChestR * k.DepthK * 1.0f), new Vector3(0.01f, 0.12f, 0.01f) * U);
            M.Emission = 0f;
            M.Jitter = 0f;
            // the trident: a long shaft, a barnacled collar and three barbed prongs
            k.Staff(1, 2.3f, C("#4a3e30"), BipedKit.StaffTop.Plain, bronze, bronze);
            float above = 2.3f * 0.58f * U;
            k.BeginHand(1);
            var tip = new Vector3(0f, above, 0f);
            M.Color = bronze;
            M.Box(tip, new Vector3(0.24f, 0.04f, 0.04f) * U);
            for (int i = -1; i <= 1; i++)
            {
                var b = tip + new Vector3(i * 0.1f * U, 0f, 0f);
                var e = b + new Vector3(i * 0.01f * U, (i == 0 ? 0.3f : 0.22f) * U, 0f);
                M.Segment(b, e, 0.014f * U, 0.012f * U, 4);
                M.Spike(e, Vector3.up, 0.024f * U, 0.08f * U, 4);
            }
            Barnacles(M, tip + new Vector3(0f, -0.06f * U, 0f), 0.035f * U, 3, barn, barnD, 9);
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Spear; m.Strike = UnitStrike.Spear; m.Ranged = UnitRanged.Throw;
            m.Heavy = 0.35f; m.MaxCadence = 2.4f; m.TurnRate = 420f;
            m.DustColor = new Color(0.5f, 0.6f, 0.58f, 0.3f);
            var mm = Done(k, key);
            mm.HeadTop = new Vector3(0f, k.H - k.HeadY + 0.35f * r, 0f);
            return mm;
        }

        // ================================================================== the Tidewitch

        /// <summary>
        /// The Tidewitch (boss of the Drowned Vault): a sea-witch who floats on a turning swirl of water — sea-green skin,
        /// finned ears, a mane of dark kelp hair strung with shells and pearls under a branching coral crown, a scallop
        /// bodice and a deep-sea gown with a foam hem that unravels into a glowing water spiral, four plum tentacles curling
        /// out from under the hem, gold bangles, and a driftwood-and-coral staff holding a glowing pearl.
        /// </summary>
        static UnitModel Tidewitch(string key)
        {
            var k = new BipedKit(209, 2.6f, 0.15f, 0.48f, 0.95f, true, 1.02f, 1.1f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color skin = C("#8cc8bc"), skinD = C("#5f9e94"), hair = C("#1e4a52"), hairL = C("#2f7068"), coral = C("#f2806a"), coralL = C("#ffb09a"),
                  pearl = C("#f4f0ff"), shell = C("#f6d8c4"), gown = C("#2a5c7a"), gownD = C("#163650"), foam = C("#e6fbff"),
                  water = C("#7fe6f0"), waterD = C("#3cb8d0"), gold = C("#e8c060"), tent = C("#7a4a8a"), tentL = C("#c89ad8"), wood = C("#8a7458");
            k.Skin = skin;
            k.Torso(gown, gownD);
            // scallop-shell bodice
            M.Bone = BB.Chest;
            for (int s = -1; s <= 1; s += 2)
            {
                M.Color = shell;
                M.Push().Translate(s * 0.065f * U, k.ChestY + 0.02f * U, k.ChestR * k.DepthK * 0.92f).Rotate(-8f, s * 22f, 0f);
                M.Sphere(Vector3.zero, new Vector3(0.07f, 0.065f, 0.03f) * U, 7, 4);
                M.Color = coral;
                for (int i = -2; i <= 2; i++)
                    M.Box(new Vector3(i * 0.024f * U, 0.005f * U, 0.026f * U), new Vector3(0.006f, 0.1f, 0.008f) * U);
                M.Pop();
            }
            k.Neck(skin);
            k.Head(skin, C("#ffd27a"), hair, EyeStyle.Narrow, 14f, false);
            k.Cheeks(C("#6fb0a6"), 0.8f);
            // finned ears
            M.Bone = BB.Head; M.Color = skinD;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = new Vector3(s * 0.92f * r, k.HeadCY - 0.05f * r, -0.05f * r);
                M.Blade(e, e + new Vector3(s * 0.55f * r, 0.35f * r, -0.35f * r), 0.45f * r);
                M.Blade(e + new Vector3(0f, -0.15f * r, 0f), e + new Vector3(s * 0.45f * r, -0.05f * r, -0.45f * r), 0.3f * r);
            }
            k.HairCap(hair, 52f, 98f, 122f, 1.1f);
            k.Bangs(hair, 5, 0.5f, 72f);
            k.LongBack(hair, k.SpineY - 0.05f * U, 1.35f, 1.7f);
            // kelp locks with shells and pearls
            M.Bone = BB.HairB;
            for (int i = 0; i < 6; i++)
            {
                float th = Mathf.Lerp(110f, 250f, i / 5f) * Mathf.Deg2Rad;
                var a = new Vector3(Mathf.Sin(th) * 1.0f * r, k.HeadCY - 0.2f * r, Mathf.Cos(th) * r - 0.1f * r);
                var e = a + new Vector3(Mathf.Sin(th) * 0.4f * r, -3.2f * r - (i % 2) * 0.6f * r, Mathf.Cos(th) * 0.6f * r);
                M.Color = i % 2 == 0 ? hairL : hair;
                M.CurvedStrip(a, Vector3.Lerp(a, e, 0.5f) + new Vector3((i % 2 == 0 ? 0.3f : -0.3f) * r, 0f, 0f), e, 0.36f * r, 0.16f * r, Vector3.back, 0.05f * r, 3);
                M.Color = i % 3 == 0 ? pearl : shell;
                M.Sphere(Vector3.Lerp(a, e, 0.65f) + new Vector3(0f, 0f, -0.05f * r), 0.12f * r, 5, 3);
            }
            // branching coral crown
            M.Bone = BB.Head;
            for (int i = 0; i < 5; i++)
            {
                float th = Mathf.Lerp(-64f, 64f, i / 4f) * Mathf.Deg2Rad;
                var a = new Vector3(Mathf.Sin(th) * 0.95f * r, k.HeadCY + 0.62f * r, Mathf.Cos(th) * 0.92f * r - 0.12f * r);
                float len = (i == 2 ? 1.1f : 0.75f) * r;
                var t = a + new Vector3(Mathf.Sin(th) * 0.4f * r, len, -0.1f * r);
                var mid = Vector3.Lerp(a, t, 0.5f);
                M.Color = i % 2 == 0 ? coral : coralL;
                M.Curve(a, a + new Vector3(0f, len * 0.6f, 0.1f * r), t, 0.11f * r, 0.05f * r, 3, 5);
                M.Curve(mid, mid + new Vector3(Mathf.Sin(th) * 0.3f * r + 0.15f * r, 0.15f * r, 0f), mid + new Vector3(Mathf.Sin(th) * 0.35f * r + 0.25f * r, 0.45f * r, 0f), 0.07f * r, 0.035f * r, 2, 4);
            }
            M.Emission = 0.9f; M.Color = pearl;
            M.Sphere(new Vector3(0f, k.HeadCY + 0.62f * r, 0.95f * r), 0.14f * r, 6, 4);
            M.Emission = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, skin, skin);
                k.Cuff(s, gold, 0.82f, 0.9f, 1.25f);
                k.ArmBand(s, gold, 0.3f, 1.25f, 0.035f);
                M.Bone = BB.ArmU(s); M.Color = shell;
                M.Sphere(new Vector3(s * (k.ShoulderX + 0.02f * U), k.ShoulderY + 0.01f * U, 0f), new Vector3(0.07f, 0.04f, 0.07f) * U, 7, 4);
            }
            // the gown, foam hem
            float hemY = k.HipY - 0.42f * U;
            k.Skirt(gown, gownD, hemY, 1.65f, 0f, foam);
            k.Belt(gold, pearl, -1f, 1.08f, 0.05f);
            // the water swirl: a glowing funnel, two foam rings and a spiral of water blades down to a point near the ground
            M.Bone = BB.Hips;
            M.Emission = 0.55f; M.Color = waterD;
            M.Lathe(new[] { new Vector2(k.HipR * 1.2f, hemY + 0.06f * U), new Vector2(k.HipR * 0.8f, hemY - 0.3f * U), new Vector2(0.05f * U, 0.12f * U) }, 9, true, false, false);
            M.Bone = BB.SkirtB;
            for (int i = 0; i < 14; i++)
            {
                float t = i / 13f;
                float a = (i * 52f) * Mathf.Deg2Rad;
                float y = Mathf.Lerp(hemY - 0.02f * U, 0.14f * U, t);
                float rad = Mathf.Lerp(k.HipR * 1.9f, 0.12f * U, t * 0.85f);
                var p = new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad);
                var tan = new Vector3(Mathf.Cos(a), -0.25f, -Mathf.Sin(a));
                M.Emission = 0.7f; M.Color = i % 3 == 0 ? foam : water;
                M.Blade(p, p + tan * (0.3f - t * 0.12f) * U, (0.09f - t * 0.04f) * U, Vector3.up);
            }
            M.Emission = 0.6f; M.Color = foam;
            M.Torus(new Vector3(0f, hemY - 0.1f * U, 0f), k.HipR * 1.75f, 0.018f * U, 14, 3);
            M.Torus(new Vector3(0f, hemY - 0.32f * U, 0f), k.HipR * 1.2f, 0.016f * U, 12, 3);
            M.Emission = 0f;
            // tentacles curling out from under the hem
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                M.Bone = i == 0 ? BB.SkirtR : (i == 3 ? BB.SkirtL : BB.SkirtB);
                var a0 = new Vector3(Mathf.Sin(a) * k.HipR * 1.1f, hemY + 0.04f * U, Mathf.Cos(a) * k.HipR * 1.1f);
                var a2 = new Vector3(Mathf.Sin(a) * k.HipR * 2.6f, hemY - 0.28f * U, Mathf.Cos(a) * k.HipR * 2.6f);
                var c = new Vector3(Mathf.Sin(a) * k.HipR * 2.4f, hemY + 0.02f * U, Mathf.Cos(a) * k.HipR * 2.4f);
                M.Color = tent;
                M.Curve(a0, c, a2, 0.055f * U, 0.03f * U, 4, 6);
                var a3 = a2 + new Vector3(Mathf.Sin(a + 1.2f) * 0.1f * U, 0.1f * U, Mathf.Cos(a + 1.2f) * 0.1f * U);
                M.Curve(a2, a2 + new Vector3(Mathf.Sin(a) * 0.08f * U, -0.04f * U, Mathf.Cos(a) * 0.08f * U), a3, 0.03f * U, 0.008f * U, 3, 5);
                M.Color = tentL;
                M.Sphere(Vector3.Lerp(a0, a2, 0.55f) + new Vector3(0f, -0.04f * U, 0f), 0.022f * U, 4, 3);
            }
            // the staff: driftwood with a coral claw holding a glowing pearl
            k.Staff(1, 2.0f, wood, BipedKit.StaffTop.Orb, coral, pearl, 0.1f);
            var m = k.Model;
            m.FloatHeight = 0.45f;
            m.BaseFade = 1f;
            m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            m.HoldR = UnitHold.Staff;
            m.Dust = false;
            m.TurnRate = 420f;
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.HeadTop = new Vector3(0f, k.H - k.HeadY + 0.9f * r, 0f);
            return UnitModels.Bake(m, k.M);
        }

        // ================================================================== King Aldwin

        /// <summary>
        /// King Aldwin (boss of the Barrow): the barrow king risen — a skull with ghost-blue eyes and a long white beard
        /// under a tall tarnished crown of spikes and blue gems, tarnished plate with gold trim over a royal-blue tabard,
        /// an ermine mantle, a vast torn royal cloak, ghost light leaking from his breastplate, and a greatsword with a
        /// glowing rune line, shouldered in both hands.
        /// </summary>
        static UnitModel KingAldwin(string key)
        {
            var k = new BipedKit(210, 2.5f, 0.155f, 0.48f, 1.35f, false, 1.15f, 1.1f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color plate = C("#7a7c84"), plateD = C("#4a4c56"), gold = C("#c8a050"), goldD = C("#8a6a34"),
                  royal = C("#36407c"), royalD = C("#1e2450"), ermine = C("#eee8dc"), spot = C("#1e1c22"), bone = C("#e4dac2"),
                  boneD = C("#b4a486"), socket = C("#141822"), glow = C("#8fe6ff"), beard = C("#e6ecf2"), leather = C("#3a2e28");
            M.Jitter = 0.03f;
            k.Torso(plate, plateD);
            k.ChestPanel(royal, 0.24f, true, gold);
            // ghost light in the breastplate seams
            M.Bone = BB.Chest; M.Emission = 0.9f; M.Color = glow;
            for (int s = -1; s <= 1; s += 2)
            {
                M.Push().Translate(s * 0.13f * U, k.ChestY + 0.04f * U, k.ChestR * k.DepthK * 0.9f).Rotate(0f, s * 30f, s * 18f);
                M.Box(Vector3.zero, new Vector3(0.014f, 0.12f, 0.02f) * U);
                M.Pop();
            }
            M.Emission = 0f;
            k.Neck(C("#24262e"));
            SkullFace(k, bone, boneD, socket, glow, 1.25f);
            k.Beard(beard, 0.75f, 0.8f);
            // the tall crown
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = gold;
            M.Band(new Vector3(0f, 0f, -0.05f * r), 1.04f * r, 1.1f * r, hc.y + 0.45f * r, hc.y + 0.82f * r, 10, 1f);
            M.Color = goldD;
            M.Band(new Vector3(0f, 0f, -0.05f * r), 1.06f * r, 1.06f * r, hc.y + 0.45f * r, hc.y + 0.52f * r, 10, 1f);
            for (int i = 0; i < 8; i++)
            {
                float th = (i * 45f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(th) * 1.08f * r, hc.y + 0.78f * r, Mathf.Cos(th) * 1.08f * r - 0.05f * r);
                M.Color = gold;
                M.Spike(p, new Vector3(Mathf.Sin(th) * 0.15f, 1f, Mathf.Cos(th) * 0.15f), 0.17f * r, (i % 2 == 0 ? 1.05f : 0.6f) * r, 4);
                if (i % 2 == 0)
                {
                    M.Emission = 1f; M.Color = glow;
                    M.Sphere(p + new Vector3(Mathf.Sin(th) * 0.04f * r, -0.15f * r, Mathf.Cos(th) * 0.04f * r), 0.1f * r, 5, 3);
                    M.Emission = 0f;
                }
            }
            k.Mantle(ermine, 1.35f, 0.04f, 31);
            M.Bone = BB.Chest; M.Color = spot;
            for (int i = 0; i < 7; i++)
            {
                float th = (i / 7f) * Mathf.PI * 2f + 0.3f;
                M.Box(new Vector3(Mathf.Sin(th) * k.ShoulderR * 1.15f, k.ShoulderY - 0.02f * U, Mathf.Cos(th) * k.ShoulderR * k.DepthK * 1.3f), new Vector3(0.015f, 0.04f, 0.015f) * U);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, plateD, plate, plateD);
                k.Cuff(s, plate, 0.4f, 0.98f, 1.42f);
                k.Cuff(s, gold, 0.92f, 0.98f, 1.48f);
                if (s > 0) k.BigPauldron(s, plate, gold, royalD, 1.6f);
                else k.Pauldron(s, plate, 1.5f, gold, 3);
                k.Leg(s, plateD, plate, plateD, 0.72f, gold);
            }
            k.Skirt(plateD, royalD, k.KneeY - 0.04f * U, 1.36f, 22f, gold);
            k.Belt(leather, gold, -1f, 1.1f, 0.07f);
            k.Cape(royal, royalD, k.AnkleY + 0.04f * U, 1.25f, gold);
            CapeTatters(k, k.AnkleY + 0.04f * U, 1.25f, 9, royal, royalD, 0.09f);
            M.Jitter = 0f;
            k.Greatsword(1, 1.5f, C("#9ca8b4"), leather, gold);
            k.BeginHand(1);
            M.Emission = 1f; M.Color = glow;
            M.Box(new Vector3(0f, 0.56f * U, 0.027f * U), new Vector3(0.022f, 0.9f, 0.006f) * U);
            M.Box(new Vector3(0f, 0.56f * U, -0.027f * U), new Vector3(0.022f, 0.9f, 0.006f) * U);
            M.Emission = 0f;
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Shoulder; m.Strike = UnitStrike.Greatsword; m.Ranged = UnitRanged.Point; m.TwoHanded = true;
            m.Heavy = 0.55f; m.MaxCadence = 2.0f; m.TurnRate = 360f;
            m.DustColor = new Color(0.6f, 0.62f, 0.66f, 0.35f);
            m.Breath = 0.5f;
            var mm = Done(k, key);
            mm.HeadTop = new Vector3(0f, k.H - k.HeadY + 0.9f * r, 0f);
            return mm;
        }

        // ================================================================== ice elemental and the Rimeheart

        /// <summary>
        /// Ice elemental (Skyreach, the Frozen Sanctum): a floating body of ice shards held round a glowing cold core — a
        /// crown of crystals round the core for a chest, a faceted head with glowing eye slits and a shard crest, arms of
        /// separate floating shards ending in crystal fists, a tapering cluster of down-pointing shards instead of legs and
        /// a few loose shards drifting round the shoulders. The Rimeheart (boss of the Frozen Sanctum) is its lord: twice
        /// the mass, a great frozen heart blazing in a cage of crystal, a crown of tall ice spires, towering shoulder
        /// spires, blade-like shard fists and a mantle of frost.
        /// </summary>
        static UnitModel IceElemental(string key, bool lord)
        {
            float H = lord ? 3.2f : 2.1f;
            var k = new BipedKit(lord ? 212 : 211, H, lord ? 0.2f : 0.16f, 0.42f, lord ? 1.9f : 1.5f, false, lord ? 1.4f : 1.25f, 1.3f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color ice = C("#d8f2ff"), iceM = C("#a2d4f0"), iceD = C("#6aa4d0"), deep = C("#40709f"), core = C("#f2feff"), glow = C("#8fe8ff"),
                  frost = C("#eef8ff"), heart = C("#bff6ff");
            if (lord)
            {
                // the lord is old glacier ice: deeper blues under a white crown
                ice = C("#c4e8fc"); iceM = C("#7cb4e2"); iceD = C("#4a7fba"); deep = C("#26487a");
            }
            Color[] shades = { ice, iceM, iceD, iceM };
            M.Jitter = 0f;
            // the core
            var cc = new Vector3(0f, k.ChestY, 0.02f * U);
            M.Bone = BB.Chest;
            M.Emission = 1f; M.Color = lord ? heart : core;
            if (lord)
            {
                // a frozen heart: two lobes and a point, blazing
                for (int s = -1; s <= 1; s += 2)
                    M.Sphere(cc + new Vector3(s * 0.06f * U, 0.05f * U, 0.06f * U), 0.1f * U, 8, 6);
                M.Aim(cc + new Vector3(0f, 0.03f * U, 0.06f * U), Vector3.down);
                M.Cone(Vector3.zero, 0.135f * U, 0.2f * U, 7);
                M.Pop();
            }
            else M.Sphere(cc, 0.095f * U, 8, 6);
            M.Emission = 0.55f; M.Color = glow;
            M.Blob(cc, new Vector3(0.15f, 0.16f, 0.13f) * U, 0, 0.12f, 3);
            M.Emission = 0f;
            // crown of crystals round the core: the chest
            int nc = lord ? 11 : 9;
            for (int i = 0; i < nc; i++)
            {
                float th = (i * 360f / nc + 15f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(th), 0.55f + (i % 3) * 0.25f, Mathf.Cos(th) * 0.75f);
                var b = cc + new Vector3(Mathf.Sin(th) * k.ChestR * 0.35f, -0.1f * U, Mathf.Cos(th) * k.ChestR * 0.3f);
                M.Color = shades[i % 4];
                Crystal(M, b, dir, (0.05f + (i % 2) * 0.02f) * U, (0.28f + (i % 3) * 0.06f) * U);
            }
            // a lower ring pointing down (the waist) on the spine
            M.Bone = BB.Spine;
            for (int i = 0; i < 6; i++)
            {
                float th = (i * 60f) * Mathf.Deg2Rad;
                var b = new Vector3(Mathf.Sin(th) * k.WaistR * 0.5f, k.SpineY + 0.05f * U, Mathf.Cos(th) * k.WaistR * 0.4f);
                M.Color = shades[(i + 1) % 4];
                Crystal(M, b, new Vector3(Mathf.Sin(th) * 0.5f, -1f, Mathf.Cos(th) * 0.4f), 0.05f * U, 0.2f * U);
            }
            // instead of legs: a tapering cluster of shards pointing at the ground, and loose shards round it
            M.Bone = BB.Hips;
            M.Color = iceD;
            Crystal(M, new Vector3(0f, k.PelvisY + 0.02f * U, 0f), Vector3.down, 0.11f * U, k.PelvisY - 0.3f * U, 6);
            for (int i = 0; i < 4; i++)
            {
                float th = (i * 90f + 45f) * Mathf.Deg2Rad;
                M.Color = shades[i];
                Crystal(M, new Vector3(Mathf.Sin(th) * 0.08f * U, k.PelvisY, Mathf.Cos(th) * 0.07f * U),
                        new Vector3(Mathf.Sin(th) * 0.3f, -1f, Mathf.Cos(th) * 0.25f), 0.06f * U, (k.PelvisY - 0.4f * U) * 0.7f);
            }
            int[] skirt = { BB.SkirtL, BB.SkirtR, BB.SkirtB, BB.SkirtF };
            for (int i = 0; i < 4; i++)
            {
                M.Bone = skirt[i];
                float th = (i == 0 ? -100f : i == 1 ? 100f : i == 2 ? 180f : 0f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(th) * k.HipR * 1.5f, k.HipY - 0.25f * U, Mathf.Cos(th) * k.HipR * 1.3f);
                M.Color = shades[(i + 2) % 4];
                Shard(M, p, new Vector3(Mathf.Sin(th) * 0.2f, -1f, Mathf.Cos(th) * 0.2f), 0.04f * U, 0.2f * U);
            }
            // head: faceted ice with glowing eye slits and a crest of shards
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = iceM;
            M.Blob(hc, new Vector3(1.0f, 1.05f, 0.95f) * r, 0, 0.12f, 5);
            M.Color = deep;
            M.Push().Translate(hc + new Vector3(0f, 0.05f * r, 0.78f * r)).Rotate(-10f, 0f, 0f);
            M.Box(Vector3.zero, new Vector3(1.2f * r, 0.32f * r, 0.3f * r));
            M.Pop();
            M.Emission = 1f; M.Color = core;
            for (int s = -1; s <= 1; s += 2)
            {
                M.Push().Translate(hc + new Vector3(s * 0.3f * r, 0.03f * r, 0.92f * r)).Rotate(0f, s * 20f, s * -12f);
                M.Box(Vector3.zero, new Vector3(0.36f * r, 0.12f * r, 0.08f * r));
                M.Pop();
            }
            M.Emission = 0f;
            int nh = lord ? 7 : 5;
            for (int i = 0; i < nh; i++)
            {
                float t = i / (float)(nh - 1) - 0.5f;
                float len = (lord ? 2.4f : 1.4f) * r * (1f - Mathf.Abs(t) * 0.9f);
                M.Color = lord ? (i % 2 == 0 ? frost : C("#d6f0ff")) : (i % 2 == 0 ? ice : iceM);
                if (lord) M.Emission = 0.2f;
                Crystal(M, hc + new Vector3(t * 1.4f * r, 0.6f * r, -0.15f * r), new Vector3(t * 1.2f, 1f, -0.25f), 0.17f * r, len);
            }
            M.Emission = 0f;
            // shoulders: spires; arms: floating shards; fists: crystal clusters
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * k.ShoulderX;
                M.Bone = BB.ArmU(s);
                M.Color = ice;
                Crystal(M, new Vector3(x, k.ShoulderY - 0.02f * U, 0f), new Vector3(s * 0.45f, 1f, -0.15f), (lord ? 0.09f : 0.07f) * U, (lord ? 0.62f : 0.36f) * U);
                M.Color = iceD;
                Crystal(M, new Vector3(x, k.ShoulderY - 0.04f * U, 0.03f * U), new Vector3(s * 1f, 0.35f, 0.3f), 0.05f * U, (lord ? 0.34f : 0.22f) * U);
                M.Color = iceM;
                Crystal(M, new Vector3(x, k.ShoulderY - 0.03f * U, -0.04f * U), new Vector3(s * 0.6f, 0.6f, -0.8f), 0.05f * U, (lord ? 0.4f : 0.24f) * U);
                M.Color = iceM;
                Shard(M, new Vector3(x, (k.ShoulderY + k.ElbowY) * 0.5f - 0.02f * U, 0f), Vector3.down, (lord ? 0.085f : 0.07f) * U, (k.ShoulderY - k.ElbowY) * 0.95f, 5);
                M.Bone = BB.ArmL(s); M.Color = iceD;
                Shard(M, new Vector3(x, (k.ElbowY + k.WristY) * 0.5f, 0f), Vector3.down, (lord ? 0.08f : 0.065f) * U, (k.ElbowY - k.WristY) * 0.95f, 5);
                M.Bone = BB.Hand(s);
                var g = k.Grip(s);
                M.Color = ice;
                M.Blob(g + new Vector3(0f, 0.01f * U, 0f), new Vector3(0.09f, 0.08f, 0.09f) * U * (lord ? 1.3f : 1f), 0, 0.1f, 8 + s);
                for (int f = -1; f <= 1; f++)
                {
                    M.Color = shades[f + 1];
                    Crystal(M, g + new Vector3(f * 0.04f * U, -0.03f * U, 0.02f * U), new Vector3(f * 0.3f + s * 0.2f, -1f, 0.4f), 0.035f * U, (lord ? 0.26f : 0.14f) * U);
                }
            }
            // loose shards drifting round the shoulders (spring bones: they bob and trail)
            int[] loose = { BB.Cape, BB.HairB, BB.WingL, BB.WingR, BB.Tail };
            for (int i = 0; i < loose.Length; i++)
            {
                M.Bone = loose[i];
                var p = k.Bind[loose[i]] + new Vector3((i % 2 == 0 ? 1f : -1f) * (0.15f + i * 0.04f) * U, 0.05f * U * i, -0.12f * U);
                M.Emission = 0.25f; M.Color = shades[i % 4];
                Shard(M, p, new Vector3(0.3f * (i - 2), 1f, 0.2f), 0.035f * U, (0.14f + (i % 2) * 0.06f) * U);
            }
            M.Emission = 0f;
            if (lord)
            {
                // a mantle of frost: pale ragged blades hanging from the shoulders behind
                M.Bone = BB.Cape;
                for (int i = 0; i < 9; i++)
                {
                    float th = Mathf.Lerp(120f, 240f, i / 8f) * Mathf.Deg2Rad;
                    var a = new Vector3(Mathf.Sin(th) * k.ShoulderR * 1.2f, k.ShoulderY, Mathf.Cos(th) * k.ShoulderR * 0.9f);
                    M.Emission = 0.2f; M.Color = i % 2 == 0 ? frost : iceM;
                    M.Blade(a, a + new Vector3(Mathf.Sin(th) * 0.12f * U, -(0.7f + (i % 3) * 0.15f) * U, Mathf.Cos(th) * 0.18f * U), 0.2f * U);
                }
                M.Emission = 0f;
            }
            var m = k.Model;
            m.FloatHeight = lord ? 0.3f : 0.35f;
            m.Strike = UnitStrike.Slam; m.Ranged = UnitRanged.Point;
            m.HoldR = UnitHold.Relaxed;
            m.Dust = false;
            m.Heavy = lord ? 0.8f : 0.45f;
            m.MaxCadence = lord ? 1.6f : 2.4f;
            m.TurnRate = lord ? 260f : 420f;
            m.CastBone = BB.Chest; m.CastOffset = cc + new Vector3(0f, 0f, 0.12f * U) - k.Bind[BB.Chest];
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.HeadTop = new Vector3(0f, k.H - k.HeadY + (lord ? 1.6f : 1.0f) * r, 0f);
            return UnitModels.Bake(m, k.M);
        }

        // ================================================================== ash elemental

        /// <summary>
        /// Ash elemental (Ashwyrm's Roost, Skyreach): a hulking body of packed ash and cinders split by glowing ember seams,
        /// a furnace core showing through a crack in the chest, a craggy head with ember eyes under a mane of rising smoke
        /// plumes, smoking shoulders, heavy cinder fists, and below the hips a swirling vortex of ash and sparks.
        /// </summary>
        static UnitModel AshElemental(string key)
        {
            var k = new BipedKit(213, 2.3f, 0.15f, 0.42f, 1.7f, false, 1.3f, 1.3f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color ash = C("#5e5654"), ashD = C("#3c3536"), ashL = C("#8c827e"), smoke = C("#a69c98"), smokeD = C("#6e6664"),
                  ember = C("#ff8a3a"), core = C("#ffd27a"), dark = C("#1e1a1c");
            M.Jitter = 0.12f;
            k.Torso(ash, ashD);
            // the furnace crack and the ember seams
            M.Bone = BB.Chest;
            var cc = new Vector3(0.02f * U, k.ChestY, k.ChestR * k.DepthK * 0.78f);
            M.Color = dark;
            M.Sphere(cc, new Vector3(0.1f, 0.13f, 0.06f) * U, 7, 5);
            M.Emission = 1f; M.Color = core;
            M.Sphere(cc + new Vector3(0f, 0f, 0.02f * U), new Vector3(0.07f, 0.1f, 0.05f) * U, 7, 5);
            M.Color = ember;
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f + 0.4f;
                M.Push().Translate(Mathf.Sin(a) * k.ChestR * 0.75f, k.ChestY + Mathf.Cos(a) * 0.13f * U, k.ChestR * k.DepthK * 0.96f).Rotate(0f, Mathf.Sin(a) * 30f, a * 47f);
                M.Box(Vector3.zero, new Vector3(0.02f, 0.14f, 0.02f) * U);
                M.Pop();
            }
            M.Bone = BB.Spine;
            for (int i = 0; i < 3; i++)
            {
                M.Push().Translate((i - 1) * 0.08f * U, k.SpineY + 0.02f * U, k.WaistR * k.DepthK * 0.95f).Rotate(0f, 0f, 20f - i * 20f);
                M.Box(Vector3.zero, new Vector3(0.018f, 0.1f, 0.02f) * U);
                M.Pop();
            }
            M.Emission = 0f;
            // crusted shoulder masses with smoke rising from them
            for (int s = -1; s <= 1; s += 2)
            {
                M.Bone = BB.ArmU(s); M.Color = ashL;
                M.Blob(k.Bind[BB.ArmU(s)] + new Vector3(s * 0.04f, 0.06f, 0f) * U, new Vector3(0.18f, 0.15f, 0.17f) * U, 0, 0.25f, 30 + s);
                M.Color = smoke;
                for (int i = 0; i < 3; i++)
                {
                    var a = k.Bind[BB.ArmU(s)] + new Vector3(s * (0.02f + i * 0.04f), 0.16f, (i - 1) * 0.06f) * U;
                    M.Color = i % 2 == 0 ? smoke : smokeD;
                    M.Blade(a, a + new Vector3(s * 0.05f, 0.24f + i * 0.05f, -0.12f) * U, 0.1f * U);
                }
            }
            // head: craggy, ember eyes, a mane of smoke plumes with ember tips
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = ashD;
            M.Blob(hc, new Vector3(1.1f, 1.0f, 1.05f) * r, 0, 0.18f, 41);
            M.Color = ash;
            M.Box(new Vector3(0f, hc.y + 0.25f * r, 0.85f * r), new Vector3(1.3f * r, 0.25f * r, 0.3f * r));
            M.Emission = 1f; M.Color = core;
            for (int s = -1; s <= 1; s += 2)
                M.Sphere(hc + new Vector3(s * 0.36f * r, 0.0f, 0.95f * r), new Vector3(0.2f, 0.1f, 0.06f) * r, 5, 3);
            M.Emission = 0.9f; M.Color = ember;
            M.Box(new Vector3(0f, hc.y - 0.5f * r, 0.95f * r), new Vector3(0.6f * r, 0.08f * r, 0.06f * r));
            M.Emission = 0f;
            M.Bone = BB.HairB;
            M.Jitter = 0.05f;
            for (int i = 0; i < 7; i++)
            {
                float t = i / 6f - 0.5f;
                var a = hc + new Vector3(t * 1.4f * r, 0.6f * r, -0.2f * r);
                var tip = a + new Vector3(t * 0.8f * r, (2.0f - Mathf.Abs(t) * 1.6f) * r, -1.4f * r);
                M.Color = i % 2 == 0 ? smoke : smokeD;
                M.CurvedStrip(a, Vector3.Lerp(a, tip, 0.5f) + new Vector3(0f, 0.3f * r, 0.3f * r), tip, 0.5f * r, 0.12f * r, Vector3.back, 0.06f * r, 3);
                M.Emission = 1f; M.Color = ember;
                M.Sphere(tip, 0.1f * r, 4, 3);
                M.Emission = 0f;
            }
            M.Jitter = 0.12f;
            // massive cinder arms and fists
            for (int s = -1; s <= 1; s += 2)
            {
                M.Bone = BB.ArmU(s); M.Color = ash;
                M.Segment(k.Bind[BB.ArmU(s)], k.Bind[BB.ArmL(s)], 0.12f * U, 0.1f * U, 6);
                M.Bone = BB.ArmL(s); M.Color = ashD;
                M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.11f * U, 0.12f * U, 6);
                M.Emission = 0.9f; M.Color = ember;
                M.Box(Vector3.Lerp(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.45f) + new Vector3(s * 0.1f * U, 0f, 0.02f * U), new Vector3(0.02f, 0.13f, 0.03f) * U);
                M.Emission = 0f;
                M.Bone = BB.Hand(s); M.Color = ashD;
                var fist = k.Bind[BB.Hand(s)] + Vector3.down * 0.1f * U;
                M.Blob(fist, new Vector3(0.16f, 0.16f, 0.16f) * U, 0, 0.2f, 51 + s);
                M.Emission = 1f; M.Color = ember;
                for (int f = -1; f <= 1; f++)
                    M.Sphere(fist + new Vector3(f * 0.06f * U, -0.06f * U, 0.12f * U), 0.025f * U, 4, 3);
                M.Emission = 0f;
            }
            // the vortex: a tapering funnel of ash, glowing rings and swirling ribbons of ash and sparks
            M.Jitter = 0.05f;
            M.Bone = BB.Hips; M.Color = ashD;
            M.Lathe(new[] { new Vector2(k.HipR * 1.05f, k.PelvisY), new Vector2(k.HipR * 0.9f, k.HipY - 0.2f * U), new Vector2(0.14f * U, 0.4f * U), new Vector2(0.02f * U, 0.12f * U) }, 8, false, false, false);
            M.Emission = 0.85f; M.Color = ember;
            M.Torus(new Vector3(0f, k.HipY - 0.28f * U, 0f), k.HipR * 0.78f, 0.012f * U, 10, 3);
            M.Bone = BB.SkirtB;
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                float a = (i * 58f) * Mathf.Deg2Rad;
                float y = Mathf.Lerp(k.HipY - 0.05f * U, 0.18f * U, t);
                float rad = Mathf.Lerp(k.HipR * 1.6f, 0.15f * U, t);
                var p = new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad);
                var tan = new Vector3(Mathf.Cos(a), -0.2f, -Mathf.Sin(a));
                bool spark = i % 3 == 1;
                M.Emission = spark ? 1f : 0f; M.Color = spark ? ember : (i % 2 == 0 ? smoke : smokeD);
                M.Blade(p, p + tan * (0.32f - t * 0.14f) * U, (spark ? 0.04f : 0.1f) * U, Vector3.up);
            }
            M.Emission = 0f;
            // drifting embers
            int[] loose = { BB.Cape, BB.WingL, BB.WingR, BB.Tail };
            M.Emission = 1f;
            for (int i = 0; i < loose.Length; i++)
            {
                M.Bone = loose[i];
                M.Color = i % 2 == 0 ? ember : core;
                M.Sphere(k.Bind[loose[i]] + new Vector3((i % 2 == 0 ? 1f : -1f) * 0.22f * U, 0.12f * U * i, -0.08f * U), 0.03f * U, 4, 3);
            }
            M.Emission = 0f;
            var m = k.Model;
            m.FloatHeight = 0.3f;
            m.Strike = UnitStrike.Slam; m.Ranged = UnitRanged.Throw;
            m.Dust = false;
            m.Heavy = 0.6f; m.MaxCadence = 2.0f; m.TurnRate = 320f;
            m.CastBone = BB.Chest; m.CastOffset = cc + new Vector3(0f, 0f, 0.1f * U) - k.Bind[BB.Chest];
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.HeadTop = new Vector3(0f, k.H - k.HeadY + 0.9f * r, 0f);
            return UnitModels.Bake(m, k.M);
        }

        // ================================================================== fen wisp

        /// <summary>
        /// Fen wisp (Mirefen): a little teal bog-light, kin to the hollow wisp but of the marsh — a glowing sea-green orb with
        /// a bright core, big round dark eyes with a sparkle, a lily-pad cap with a pink bud, two drooping reed-leaf ears,
        /// a tail of three wisps trailing down and back, and two fireflies circling it.
        /// </summary>
        static UnitModel FenWisp(string key)
        {
            var k = new BipedKit(214, 1.0f, 0.2f, 0.3f, 1f, false);
            Color core = C("#e8fff6"), outer = C("#5fe0c4"), outerD = C("#2fa890"), pad = C("#4e8a46"), padL = C("#6eaa58"), bud = C("#ff9ec4"),
                  leaf = C("#3e9a7a"), dark = C("#14262a"), fly = C("#f4ff9a");
            var center = new Vector3(0f, 0.5f, 0f);
            for (int i = 0; i < BB.Count; i++) k.Bind[i] = center;
            k.Bind[BB.Tail] = center + new Vector3(0f, -0.08f, -0.12f);
            k.Bind[BB.HairB] = center + new Vector3(0f, 0.18f, -0.04f);
            k.Bind[BB.WingL] = center + new Vector3(-0.18f, 0.1f, 0f);
            k.Bind[BB.WingR] = center + new Vector3(0.18f, 0.1f, 0f);
            k.Bind[BB.Cape] = center + new Vector3(0f, 0.05f, -0.2f);
            var M = k.M;
            M.Jitter = 0f;
            M.Bone = BB.Hips; M.Emission = 1f; M.Color = core;
            M.Sphere(center, 0.13f, 10, 7);
            M.Bone = BB.Chest; M.Emission = 0.6f; M.Color = outer;
            M.Sphere(center, new Vector3(0.23f, 0.22f, 0.22f), 12, 8);
            // the wisp tail: three curling flames trailing down and back
            M.Bone = BB.Tail;
            for (int i = 0; i < 3; i++)
            {
                float a = (i - 1) * 0.6f;
                var b = center + new Vector3(Mathf.Sin(a) * 0.1f, -0.1f, -0.12f);
                var e = b + new Vector3(Mathf.Sin(a) * 0.16f, -0.18f - (i % 2) * 0.06f, -0.3f);
                M.Emission = 0.7f; M.Color = i == 1 ? outer : outerD;
                M.CurvedStrip(b, Vector3.Lerp(b, e, 0.5f) + new Vector3(0f, -0.08f, 0.05f), e, 0.12f, 0.02f, Vector3.up, 0.015f, 3);
            }
            M.Emission = 0f;
            // eyes: big, round, dark, with a sparkle
            M.Bone = BB.Head;
            for (int s = -1; s <= 1; s += 2)
            {
                M.Color = dark;
                M.Push().Translate(center + new Vector3(s * 0.085f, 0.02f, 0.2f)).Rotate(0f, s * 22f, 0f);
                M.Sphere(Vector3.zero, new Vector3(0.05f, 0.065f, 0.025f), 8, 5);
                M.Emission = 1f; M.Color = Color.white;
                M.Sphere(new Vector3(-0.015f, 0.022f, 0.02f), 0.014f, 5, 3);
                M.Emission = 0f;
                M.Pop();
            }
            // lily-pad cap with a pink bud
            M.Color = pad;
            M.Push().Translate(center + new Vector3(0f, 0.2f, -0.02f)).Rotate(-10f, 0f, 8f);
            M.Cylinder(Vector3.zero, 0.2f, 0.19f, 0.025f, 12);
            M.Color = padL;
            M.Cylinder(new Vector3(0f, 0.024f, 0f), 0.15f, 0.14f, 0.006f, 12);
            M.Color = bud;
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                M.Blade(new Vector3(0.04f, 0.03f, 0.02f), new Vector3(0.04f + Mathf.Sin(a) * 0.05f, 0.11f, 0.02f + Mathf.Cos(a) * 0.05f), 0.05f);
            }
            M.Emission = 0.8f; M.Color = C("#fff0a0");
            M.Sphere(new Vector3(0.04f, 0.06f, 0.02f), 0.018f, 5, 3);
            M.Emission = 0f;
            M.Pop();
            // drooping reed-leaf ears
            for (int s = -1; s <= 1; s += 2)
            {
                M.Bone = s < 0 ? BB.WingL : BB.WingR;
                M.Color = leaf;
                var a = center + new Vector3(s * 0.17f, 0.1f, 0f);
                M.CurvedStrip(a, a + new Vector3(s * 0.12f, 0.04f, 0f), a + new Vector3(s * 0.2f, -0.14f, -0.04f), 0.07f, 0.01f, Vector3.up, 0.01f, 3);
            }
            // fireflies
            M.Bone = BB.HairB; M.Emission = 1f; M.Color = fly;
            M.Sphere(center + new Vector3(0.28f, 0.22f, 0.05f), 0.02f, 5, 3);
            M.Bone = BB.Cape;
            M.Sphere(center + new Vector3(-0.3f, 0.06f, -0.12f), 0.018f, 5, 3);
            M.Emission = 0f;
            var m = k.Model;
            m.FloatHeight = 0.55f;
            m.BaseFade = 0.92f;
            m.Strike = UnitStrike.Lunge; m.Ranged = UnitRanged.Pulse;
            m.Dust = false;
            m.Breath = 2f;
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.Radius = 0.26f;
            m.HeadTop = new Vector3(0f, 0.3f, 0f) + (center - k.Bind[BB.Head]);
            m.CenterBone = BB.Hips; m.CenterOffset = Vector3.zero;
            m.CastBone = BB.Hips; m.CastOffset = new Vector3(0f, 0f, 0.15f);
            m.PickBones = new[] { BB.Hips, BB.Tail };
            m.PickPad = 0.25f;
            m.HipY = center.y;
            m.Height = 1.0f;
            return UnitModels.Bake(m, k.M);
        }

        // ================================================================== the Weeping Twins

        /// <summary>
        /// The Weeping Twins (the Hollow Heart, boss 2): two spectral maidens, mirror images. Sorrow is moon-blue: closed
        /// grieving eyes with glowing tears, silver circlet, a long veil and hair to the hips. Solace is dawn-rose: serene
        /// closed eyes, a wreath of pale flowers, and a small lantern held in her left hand. Both float in long gowns with
        /// wide sleeves that fray into glowing tatters, and long spectral ribbons stream from their shoulders.
        /// </summary>
        static UnitModel WeepingTwin(string key, bool solace)
        {
            var k = new BipedKit(solace ? 216 : 215, 2.2f, 0.135f, 0.5f, 0.9f, true, 0.98f, 1.05f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color skin = solace ? C("#fbefe6") : C("#d6e2f4"), gown = solace ? C("#eea8ae") : C("#8aa4dc"), gownD = solace ? C("#a86478") : C("#4c5c9a"),
                  gownL = solace ? C("#ffe2d6") : C("#b6caf0"), hair = solace ? C("#fff0c8") : C("#eef4ff"), hairD = solace ? C("#e2c088") : C("#aabce4"),
                  glow = solace ? C("#ffd890") : C("#a8dcff"), metal = solace ? C("#e8c060") : C("#e2eaf6"), petal = C("#fff4f4"), petalP = C("#ffc4d4");
            k.Skin = skin;
            k.HemGlow = 0.55f;
            M.Emission = 0.12f;
            k.Torso(gown, gownD);
            k.ChestPanel(gownL, 0.14f, false);
            k.Neck(skin);
            M.Emission = 0.08f;
            k.Head(skin, glow, hairD, EyeStyle.Closed, solace ? 6f : -14f);
            M.Emission = 0.12f;
            k.HairCap(hair, 50f, 96f, 124f, 1.08f);
            k.Bangs(hair, 6, 0.46f, 70f);
            k.LongBack(hair, k.HipY - 0.12f * U, 1.25f, 1.5f);
            k.SideLocks(hair, k.ChestY - 0.04f * U, 0.22f);
            M.Emission = 0f;
            var hc = new Vector3(0f, k.HeadCY, 0f);
            if (solace)
            {
                // a wreath of pale flowers
                M.Bone = BB.Head;
                for (int i = 0; i < 9; i++)
                {
                    float th = (i * 40f) * Mathf.Deg2Rad;
                    var p = new Vector3(Mathf.Sin(th) * 1.08f * r, hc.y + 0.5f * r, Mathf.Cos(th) * 1.05f * r - 0.04f * r);
                    M.Color = C("#8cb070");
                    M.Sphere(p + new Vector3(0f, -0.05f * r, 0f), 0.12f * r, 4, 3);
                    M.Emission = 0.3f; M.Color = i % 3 == 0 ? petalP : petal;
                    M.Sphere(p + new Vector3(0f, 0.04f * r, 0f), new Vector3(0.17f, 0.1f, 0.17f) * r, 6, 3);
                    M.Emission = 0f;
                }
            }
            else
            {
                // a silver circlet, glowing tears and a long veil from the back of the head
                M.Bone = BB.Head; M.Color = metal;
                M.Band(new Vector3(0f, 0f, -0.04f * r), 1.06f * r, 1.06f * r, hc.y + 0.3f * r, hc.y + 0.38f * r, 12, 1f);
                M.Emission = 1f; M.Color = glow;
                M.Sphere(new Vector3(0f, hc.y + 0.36f * r, 1.03f * r), 0.09f * r, 5, 3);
                for (int s = -1; s <= 1; s += 2)
                {
                    var t0 = k.FacePoint(s * 0.36f, -0.2f, 0.04f);
                    var t1 = k.FacePoint(s * 0.4f, -0.62f, 0.04f);
                    M.Segment(t0, t1, 0.035f * r, 0.05f * r, 4);
                    M.Sphere(t1 + new Vector3(0f, -0.04f * r, 0f), 0.06f * r, 4, 3);
                }
                M.Emission = 0.2f;
                M.Bone = BB.HairB;
                for (int i = 0; i < 3; i++)
                {
                    float x = (i - 1) * 0.55f * r;
                    var a = new Vector3(x, hc.y + 0.4f * r, -0.85f * r);
                    var e = new Vector3(x * 1.6f, k.SpineY - 0.05f * U, -0.32f * U);
                    M.Color = i == 1 ? gownL : gown;
                    M.CurvedStrip(a, Vector3.Lerp(a, e, 0.5f) + new Vector3(0f, 0f, -0.08f * U), e, 0.7f * r, 0.9f * r, Vector3.back, 0.03f * r, 4);
                }
                M.Emission = 0f;
            }
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, gownL, skin, skin);
                M.Emission = 0.12f;
                k.WideSleeve(s, gown, gownD, 0.46f, 2.7f, glow);
                M.Emission = 0f;
            }
            M.Emission = 0.12f;
            k.Skirt(gown, gownD, 0.22f * U, 1.75f, 0f, glow);
            M.Emission = 0f;
            HollowTatters(k, 12, 0.25f * U, k.HipR * 1.8f, gown, gownL, glow);
            k.Sash(metal, k.HipY + 0.1f * U, 0.05f, true, glow);
            // spectral ribbons streaming back from the shoulders
            M.Bone = BB.Cape;
            for (int i = 0; i < 4; i++)
            {
                int s = i < 2 ? -1 : 1;
                var a = new Vector3(s * (0.06f + (i % 2) * 0.06f) * U, k.ShoulderY - 0.02f * U, -k.ChestR * k.DepthK);
                var e = a + new Vector3(s * (0.1f + (i % 2) * 0.08f) * U, -(0.5f + (i % 2) * 0.22f) * U, -(0.2f + (i % 2) * 0.08f) * U);
                M.Emission = 0.55f; M.Color = i % 2 == 0 ? glow : gownL;
                M.CurvedStrip(a, Vector3.Lerp(a, e, 0.5f) + new Vector3(s * 0.06f * U, 0.04f * U, -0.08f * U), e, 0.13f * U, 0.05f * U, Vector3.back, 0.01f * U, 4);
            }
            M.Emission = 0f;
            var m = k.Model;
            if (solace)
            {
                // the little lantern of solace
                M.Bone = BB.HandL;
                var g = k.Grip(-1);
                M.Color = metal;
                M.Segment(g, g + new Vector3(0f, -0.07f * U, 0f), 0.005f * U, 0.005f * U, 3);
                var lamp = g + new Vector3(0f, -0.14f * U, 0f);
                M.Cylinder(lamp + new Vector3(0f, 0.05f * U, 0f), 0.04f * U, 0.015f * U, 0.03f * U, 6);
                M.Cylinder(lamp + new Vector3(0f, -0.065f * U, 0f), 0.04f * U, 0.04f * U, 0.015f * U, 6);
                M.Emission = 1f; M.Color = glow;
                M.Sphere(lamp, new Vector3(0.036f, 0.05f, 0.036f) * U, 6, 4);
                M.Emission = 0f;
                m.CastBone = BB.HandL; m.CastOffset = lamp - k.Bind[BB.HandL];
            }
            m.FloatHeight = 0.4f;
            m.BaseFade = 1f;
            m.Strike = UnitStrike.Claw; m.Ranged = UnitRanged.Point;
            m.Dust = false;
            m.TurnRate = 480f;
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.HeadTop = new Vector3(0f, k.H - k.HeadY + 0.15f * r, 0f);
            return UnitModels.Bake(m, k.M);
        }

        // ================================================================== Mother Mire

        /// <summary>
        /// Mother Mire (the Hollow Heart, boss 3): a huge hunched bog hag — mossy olive skin, a great hooked nose with warts,
        /// a wide snaggle-toothed grin and small glowing eyes, hair of tangled roots hung with charms, bottles and little
        /// mushrooms, a moss shawl over a plum cloak, layered rag skirts, long knobbly arms with dark nails, and her
        /// cauldron-lantern: an iron pot of glowing green brew with a lantern cage for a lid, swinging from a crooked staff.
        /// </summary>
        static UnitModel MotherMire(string key)
        {
            var k = new BipedKit(217, 3.2f, 0.27f, 0.36f, 2.0f, true, 1.15f, 1.5f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color skin = C("#a4b27a"), skinD = C("#76844e"), skinL = C("#bcc690"), wart = C("#7e8a52"), root = C("#5e4434"), rootL = C("#7e5e44"),
                  moss = C("#6e9a44"), mossL = C("#96bc5a"), rag = C("#5e5844"), ragD = C("#3a372c"), shawl = C("#86607c"), shawlD = C("#4e3448"),
                  iron = C("#34302e"), ironL = C("#5a524c"), brew = C("#b8ff6a"), flame = C("#ffd27a"), nail = C("#2a2220"), teeth = C("#e8dcb8"),
                  eye = C("#e8ff7a"), mouth = C("#2a1418"), wood = C("#4e3a2a"), cap = C("#d8a44a");
            M.Jitter = 0.06f;
            // bare feet under the rags
            for (int s = -1; s <= 1; s += 2)
            {
                k.Leg(s, skin, skin, skin, 0f);
                M.Bone = BB.Foot(s); M.Color = skinD;
                M.Blob(new Vector3(s * k.HipX, k.AnkleY * 0.6f, 0.05f * U), new Vector3(0.07f, 0.05f, 0.11f) * U, 0, 0.2f, 60 + s);
                for (int t = -1; t <= 1; t++)
                    M.Sphere(new Vector3(s * k.HipX + t * 0.035f * U, 0.02f * U, 0.15f * U), 0.02f * U, 4, 3);
            }
            // layered rag skirts with a mossy, torn hem
            k.Skirt(rag, ragD, k.AnkleY + 0.08f * U, 1.55f, 0f, moss);
            M.Bone = BB.SkirtB;
            for (int i = 0; i < 12; i++)
            {
                float th = i * 30f * Mathf.Deg2Rad;
                float rr = k.HipR * 1.56f;
                var a = new Vector3(Mathf.Sin(th) * rr, k.AnkleY + 0.12f * U, Mathf.Cos(th) * rr * k.DepthK * 1.08f);
                M.Bone = th < 1.2f || th > 5.1f ? BB.SkirtF : (th < Mathf.PI ? BB.SkirtR : BB.SkirtL);
                if (th > 2.0f && th < 4.3f) M.Bone = BB.SkirtB;
                M.Color = i % 3 == 0 ? mossL : (i % 2 == 0 ? rag : ragD);
                M.Blade(a, a + new Vector3(Mathf.Sin(th) * 0.03f, -0.1f - (i % 3) * 0.03f, Mathf.Cos(th) * 0.03f) * U, 0.1f * U);
            }
            k.Skirt(shawlD, ragD, k.KneeY - 0.04f * U, 1.45f, 40f, null, -1f, 1f);
            var h = BeginHipTilt(k, 26f, k.HipY);
            k.Torso(shawl, rag, skinL, 0.35f);
            // moss shawl and a plum cloak with tatters
            k.Mantle(moss, 1.2f, 0.05f, 70);
            M.Bone = BB.Chest; M.Color = mossL;
            for (int i = 0; i < 4; i++)
            {
                float th = (i * 90f + 20f) * Mathf.Deg2Rad;
                M.Blob(new Vector3(Mathf.Sin(th) * k.ShoulderR * 0.9f, k.ShoulderY + 0.02f * U, Mathf.Cos(th) * k.ShoulderR * k.DepthK), new Vector3(0.08f, 0.05f, 0.08f) * U, 0, 0.3f, 75 + i);
            }
            k.Cape(shawl, shawlD, k.KneeY + 0.05f * U, 1.2f);
            CapeTatters(k, k.KneeY + 0.05f * U, 1.2f, 8, shawl, shawlD, 0.1f);
            k.Neck(skin);
            // the face: hooked nose, warts, a snaggle-toothed grin, small glowing eyes
            k.Head(skin, eye, skinD, EyeStyle.Glow, 26f, false, 1.2f);
            k.Eyes(eye, new Color(0, 0, 0, 0), EyeStyle.Glow, 0f, -0.08f, 0.38f, 1.35f);
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = skinD;
            var n0 = k.FacePoint(0f, -0.1f, 0f);
            M.Curve(n0, n0 + new Vector3(0f, 0.05f * r, 0.6f * r), n0 + new Vector3(0f, -0.45f * r, 0.62f * r), 0.24f * r, 0.08f * r, 4, 6);
            M.Color = wart;
            M.Sphere(n0 + new Vector3(0.1f * r, 0.0f, 0.42f * r), 0.07f * r, 4, 3);
            M.Sphere(k.FacePoint(-0.55f, -0.35f, 0.02f), 0.08f * r, 4, 3);
            M.Sphere(k.FacePoint(0.5f, 0.3f, 0.02f), 0.06f * r, 4, 3);
            var mc = k.FacePoint(0f, -0.6f, 0.02f);
            M.Color = mouth;
            M.Push().Translate(mc).Rotate(0f, 0f, 0f);
            M.Sphere(Vector3.zero, new Vector3(0.55f, 0.13f, 0.12f) * r, 8, 4);
            M.Pop();
            M.Color = teeth;
            for (int i = -2; i <= 2; i += 2)
                M.Box(mc + new Vector3(i * 0.17f * r, (i == 0 ? -0.06f : 0.06f) * r, 0.06f * r), new Vector3(0.09f, 0.16f, 0.06f) * r);
            M.Color = skinD;
            M.Box(new Vector3(0f, hc.y + 0.2f * r, 0.84f * r), new Vector3(1.1f * r, 0.14f * r, 0.2f * r));
            // root hair, hung with charms, little bottles and mushrooms
            M.Color = root;
            M.Shell(new Vector3(0f, hc.y + 0.05f * r, -0.04f * r), new Vector3(1.06f, 1.06f, 1.04f) * r, -180f, 180f, 12, 0f, th =>
            {
                float a = Mathf.Abs(th);
                return a < 70f ? 52f : Mathf.Lerp(52f, 115f, (a - 70f) / 110f);
            }, 3);
            M.Bone = BB.HairB;
            for (int i = 0; i < 11; i++)
            {
                float th = Mathf.Lerp(60f, 300f, i / 10f) * Mathf.Deg2Rad;
                var a = hc + new Vector3(Mathf.Sin(th) * 0.95f * r, 0.35f * r, Mathf.Cos(th) * 0.9f * r - 0.05f * r);
                var e = a + new Vector3(Mathf.Sin(th) * 0.6f * r, -(2.6f + (i % 3) * 0.7f) * r, Mathf.Cos(th) * 0.5f * r - 0.4f * r);
                M.Color = i % 2 == 0 ? root : rootL;
                M.Curve(a, Vector3.Lerp(a, e, 0.45f) + new Vector3(Mathf.Sin(th) * 0.35f * r, 0.2f * r, 0f), e, 0.12f * r, 0.035f * r, 4, 5);
                if (i % 3 == 1)
                {
                    M.Color = i % 2 == 0 ? teeth : C("#5a7a8a");
                    M.Segment(e, e + new Vector3(0f, -0.12f * r, 0f), 0.01f * r, 0.01f * r, 3);
                    M.Emission = i == 4 ? 0.9f : 0f;
                    M.Color = i == 4 ? brew : C("#6a8a9a");
                    M.Sphere(e + new Vector3(0f, -0.2f * r, 0f), new Vector3(0.1f, 0.14f, 0.1f) * r, 5, 3);
                    M.Emission = 0f;
                }
            }
            M.Bone = BB.Head;
            for (int i = 0; i < 3; i++)
            {
                var p = hc + new Vector3((i - 1) * 0.45f * r, 0.95f * r - Mathf.Abs(i - 1) * 0.18f * r, -0.25f * r);
                M.Color = C("#e8dcc0");
                M.Cylinder(p, 0.05f * r, 0.05f * r, 0.18f * r, 5);
                M.Color = cap;
                M.Sphere(p + new Vector3(0f, 0.2f * r, 0f), new Vector3(0.2f, 0.1f, 0.2f) * r, 6, 3);
            }
            M.Color = moss;
            M.Blob(hc + new Vector3(-0.4f * r, 0.85f * r, 0.15f * r), new Vector3(0.3f, 0.14f, 0.26f) * r, 0, 0.3f, 80);
            // long knobbly arms, dark nails
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, skinL, skinD);
                k.Cuff(s, shawlD, 0.05f, 0.5f, 1.35f);
                M.Bone = BB.Hand(s); M.Color = nail;
                var g = k.Grip(s);
                for (int f = -1; f <= 1; f++)
                    M.Spike(g + new Vector3(f * 0.025f * U, -0.04f * U, 0.03f * U), new Vector3(f * 0.2f, -1f, 0.5f), 0.01f * U, 0.06f * U, 4);
            }
            // the cauldron-lantern on a crooked staff
            M.Jitter = 0f;
            k.Staff(1, 1.9f, wood, BipedKit.StaffTop.Crook, ironL, brew);
            float above = 1.9f * 0.58f * U;
            var hook = new Vector3(0f, above + 0.12f * U, 0.12f * U);
            var pot = hook + new Vector3(0f, -0.36f * U, 0f);
            k.BeginHand(1);
            M.Color = iron;
            M.Segment(hook, pot + new Vector3(-0.1f * U, 0.12f * U, 0f), 0.008f * U, 0.008f * U, 3);
            M.Segment(hook, pot + new Vector3(0.1f * U, 0.12f * U, 0f), 0.008f * U, 0.008f * U, 3);
            M.Push().Translate(pot);
            M.Lathe(new[] { new Vector2(0.04f * U, -0.12f * U), new Vector2(0.12f * U, -0.08f * U), new Vector2(0.13f * U, 0.02f * U), new Vector2(0.1f * U, 0.08f * U) }, 9, true, true, false);
            M.Pop();
            M.Color = ironL;
            M.Torus(pot + new Vector3(0f, 0.08f * U, 0f), 0.105f * U, 0.016f * U, 10, 4);
            M.Emission = 1f; M.Color = brew;
            M.Cylinder(pot + new Vector3(0f, 0.06f * U, 0f), 0.095f * U, 0.095f * U, 0.012f * U, 9);
            M.Emission = 0.6f;
            M.Sphere(pot + new Vector3(0f, 0.1f * U, 0f), new Vector3(0.08f, 0.05f, 0.08f) * U, 7, 4);
            M.Emission = 1f;
            M.Sphere(pot + new Vector3(0.04f * U, 0.09f * U, 0.02f * U), 0.025f * U, 4, 3);
            M.Sphere(pot + new Vector3(-0.03f * U, 0.1f * U, -0.03f * U), 0.018f * U, 4, 3);
            M.Blade(pot + new Vector3(0.11f * U, 0.0f, 0f), pot + new Vector3(0.12f * U, -0.14f * U, 0f), 0.025f * U, Vector3.forward);
            M.Emission = 0f;
            // the lantern cage over the brew
            M.Color = iron;
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var b = pot + new Vector3(Mathf.Cos(a) * 0.08f * U, 0.08f * U, Mathf.Sin(a) * 0.08f * U);
                M.Curve(b, b + new Vector3(0f, 0.1f * U, 0f), pot + new Vector3(0f, 0.22f * U, 0f), 0.008f * U, 0.008f * U, 2, 3);
            }
            M.Emission = 1f; M.Color = flame;
            M.Blade(pot + new Vector3(0f, 0.1f * U, 0f), pot + new Vector3(0f, 0.2f * U, 0f), 0.05f * U, Vector3.right);
            M.Emission = 0f;
            k.End();
            var castLocal = k.Grip(1) - k.Bind[BB.HandR] + new Vector3(pot.x, -pot.z, pot.y + 0.08f * U);
            EndHipTilt(k, h);
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            m.CastBone = BB.HandR; m.CastOffset = h.Q * castLocal;
            m.Heavy = 0.6f; m.MaxCadence = 1.6f; m.TurnRate = 300f;
            m.DustColor = new Color(0.4f, 0.36f, 0.26f, 0.35f);
            m.SkirtClosed = 1f;
            var mm = Done(k, key);
            FinishHipTilt(k, h, h.Apply(new Vector3(0f, k.H + 0.2f * r, 0f)).y);
            return mm;
        }

        // ================================================================== Varkas

        /// <summary>
        /// Varkas (Ashwyrm's Roost, boss 3): the ogre lord who serves the Ashwyrm — a giant ruddy ogre with an underbite and
        /// tusks, a black topknot bound in gold and red war paint, armoured in dragon scale (overlapping red-black scales
        /// with ember edges), a whole drake skull worn on his left shoulder, a great scaled pauldron on the right, a dark
        /// fur mantle, a gold dragon buckle hung with fangs, and a two-handed maul of dragon bone.
        /// </summary>
        static UnitModel Varkas(string key)
        {
            var k = new BipedKit(218, 3.4f, 0.29f, 0.43f, 1.9f, false, 1.4f, 1.3f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color skin = C("#c88a6a"), skinD = C("#985e46"), scale = C("#5e2a26"), scaleL = C("#8a3a2e"), edge = C("#ff9a4a"),
                  bone = C("#ecdfc4"), boneD = C("#bfae8e"), iron = C("#3e383a"), gold = C("#dcaa4c"), hair = C("#221a1a"),
                  leather = C("#4e3828"), fur = C("#6a4e3a"), paint = C("#c8302a"), dark = C("#1a1414");
            M.Jitter = 0.04f;
            k.Torso(scale, leather, skinD, 0.32f);
            // dragon-scale armour: rows of overlapping scales with glowing edges over the chest and shoulders
            M.Bone = BB.Chest;
            for (int row = 0; row < 4; row++)
            {
                float y = Mathf.Lerp(k.ShoulderY - 0.06f * U, k.ChestY - 0.08f * U, row / 3f);
                int n = 7 - (row % 2);
                for (int i = 0; i < n; i++)
                {
                    float th = Mathf.Lerp(-80f, 80f, (i + (row % 2) * 0.5f) / 6f) * Mathf.Deg2Rad;
                    float rr = k.ChestR * 1.03f;
                    var p = new Vector3(Mathf.Sin(th) * rr, y, Mathf.Cos(th) * rr * k.DepthK + 0.01f * U);
                    M.Push().Translate(p).Rotate(-18f, th * Mathf.Rad2Deg, 0f);
                    int tone = (i * 5 + row * 3) % 4;
                    M.Color = tone == 0 ? scaleL : (tone == 3 ? Paint.Shade(scale, 0.85f) : scale);
                    M.Box(Vector3.zero, new Vector3(0.09f, 0.08f, 0.02f) * U);
                    M.Emission = 0.6f; M.Color = edge;
                    M.Box(new Vector3(0f, -0.042f * U, 0.004f * U), new Vector3(0.08f, 0.008f, 0.018f) * U);
                    M.Emission = 0f;
                    M.Pop();
                }
            }
            k.Mantle(fur, 1.15f, 0.02f, 90);
            k.Neck(skin);
            // head: heavy jaw with tusks, brow, war paint, topknot
            k.Head(skin, C("#ffb040"), hair, EyeStyle.Narrow, 24f, true, 1.45f);
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = skinD;
            M.Box(new Vector3(0f, hc.y + 0.24f * r, 0.84f * r), new Vector3(1.25f * r, 0.2f * r, 0.26f * r));
            M.Sphere(k.FacePoint(0f, -0.25f, 0.05f), new Vector3(0.22f, 0.2f, 0.2f) * r, 6, 4);
            M.Color = skin;
            M.Sphere(new Vector3(0f, hc.y - 0.72f * r, 0.42f * r), new Vector3(0.78f, 0.36f, 0.58f) * r, 8, 5);
            M.Color = bone;
            for (int s = -1; s <= 1; s += 2)
                M.Spike(new Vector3(s * 0.38f * r, hc.y - 0.62f * r, 0.88f * r), new Vector3(s * 0.25f, 1f, 0.2f), 0.09f * r, 0.42f * r, 5);
            M.Color = paint;
            for (int s = -1; s <= 1; s += 2)
            {
                M.Push().Translate(k.FacePoint(s * 0.45f, -0.05f, 0.02f)).Rotate(0f, s * 30f, 0f);
                M.Box(Vector3.zero, new Vector3(0.1f, 0.5f, 0.04f) * r);
                M.Pop();
            }
            k.HairCap(hair, 30f, 70f, 92f, 1.02f);
            k.Bun(hair, new Vector3(0f, hc.y + 1.0f * r, -0.35f * r), 0.45f);
            M.Bone = BB.Head; M.Color = gold;
            M.Band(new Vector3(0f, 0f, -0.35f * r), 0.24f * r, 0.24f * r, hc.y + 0.82f * r, hc.y + 0.95f * r, 8, 1f);
            M.Bone = BB.HairB; M.Color = hair;
            var pt = new Vector3(0f, hc.y + 1.05f * r, -0.55f * r);
            M.Curve(pt, pt + new Vector3(0f, 0.2f * r, -0.5f * r), pt + new Vector3(0.1f * r, -0.9f * r, -0.8f * r), 0.16f * r, 0.05f * r, 4, 5);
            // arms: bare, bracers of scale and gold bands
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, skin, skinD);
                k.Cuff(s, scale, 0.35f, 0.98f, 1.35f);
                k.ArmBand(s, gold, 0.35f, 1.25f, 0.04f);
                k.Leg(s, leather, scale, iron, 0.62f, scaleL);
            }
            // right shoulder: three overlapping lames of scale with glowing edges
            k.Pauldron(1, scale, 0.95f, gold, 3);
            M.Bone = BB.ArmUR;
            for (int i = 0; i < 4; i++)
            {
                float a = (i - 1.5f) * 0.45f;
                var p = new Vector3(k.ShoulderX + 0.07f * U, k.ShoulderY + 0.06f * U, Mathf.Sin(a) * 0.07f * U);
                M.Color = i % 2 == 0 ? scaleL : scale;
                M.Spike(p, new Vector3(0.5f, 1f, Mathf.Sin(a) * 0.6f), 0.03f * U, 0.12f * U, 4);
            }
            // the drake skull on the left shoulder: snout forward-out, horns sweeping back
            M.Bone = BB.ArmUL;
            var sk = new Vector3(-k.ShoulderX - 0.05f * U, k.ShoulderY + 0.06f * U, 0f);
            M.Color = bone;
            M.Sphere(sk, new Vector3(0.13f, 0.1f, 0.14f) * U, 8, 5);
            M.Push().Translate(sk + new Vector3(-0.02f * U, -0.02f * U, 0.1f * U)).Rotate(-10f, -15f, 0f);
            M.Sphere(Vector3.zero, new Vector3(0.07f, 0.06f, 0.15f) * U, 7, 4);
            M.Color = dark;
            M.Box(new Vector3(0f, 0.02f * U, 0.13f * U), new Vector3(0.06f, 0.015f, 0.03f) * U);
            M.Color = bone;
            for (int t = -2; t <= 2; t++)
                M.Spike(new Vector3(t * 0.022f * U, -0.04f * U, (0.06f - Mathf.Abs(t) * 0.01f) * U), Vector3.down, 0.008f * U, 0.035f * U, 3);
            M.Pop();
            M.Color = dark;
            for (int s = -1; s <= 1; s += 2)
                M.Sphere(sk + new Vector3(s * 0.08f * U, 0.02f * U, 0.07f * U), new Vector3(0.03f, 0.035f, 0.025f) * U, 5, 3);
            M.Color = boneD;
            for (int s = -1; s <= 1; s += 2)
            {
                var a = sk + new Vector3(s * 0.07f * U, 0.07f * U, -0.04f * U);
                M.Curve(a, a + new Vector3(s * 0.05f * U, 0.12f * U, -0.06f * U), a + new Vector3(s * 0.08f * U, 0.1f * U, -0.24f * U), 0.03f * U, 0.006f * U, 3, 5);
            }
            k.Skirt(scale, leather, k.KneeY + 0.04f * U, 1.42f, 28f, gold);
            k.Belt(leather, gold, -1f, 1.12f, 0.09f);
            M.Bone = BB.Spine;
            var buckle = new Vector3(0f, k.HipY + 0.09f * U, k.WaistR * k.DepthK * (1.32f + 0.3f) + 0.02f * U);
            M.Color = gold;
            M.Sphere(buckle, new Vector3(0.09f, 0.08f, 0.03f) * U, 7, 4);
            M.Color = dark;
            for (int s = -1; s <= 1; s += 2)
                M.Box(buckle + new Vector3(s * 0.03f * U, 0.01f * U, 0.025f * U), new Vector3(0.016f, 0.01f, 0.01f) * U);
            M.Bone = BB.SkirtF; M.Color = bone;
            for (int i = -2; i <= 2; i++)
            {
                var a = buckle + new Vector3(i * 0.05f * U, -0.06f * U, 0f);
                M.Segment(a, a + new Vector3(0f, -0.03f * U, 0f), 0.004f * U, 0.004f * U, 3);
                M.Spike(a + new Vector3(0f, -0.03f * U, 0f), Vector3.down, 0.012f * U, 0.06f * U, 4);
            }
            // the dragon-bone maul
            M.Jitter = 0f;
            // the dragon-bone maul: a great knuckle of bone on a long haft, banded in gold and set with fangs
            k.BeginHand(1, -0.18f * U);
            float len = 1.25f * U;
            M.Color = C("#3a2a22");
            M.Cylinder(Vector3.zero, 0.024f * U, 0.026f * U, len, 6);
            M.Color = leather;
            M.Cylinder(new Vector3(0f, 0.05f * U, 0f), 0.03f * U, 0.03f * U, 0.28f * U, 6);
            var head = new Vector3(0f, len + 0.02f * U, 0f);
            M.Jitter = 0.08f; M.Color = bone;
            M.Blob(head, new Vector3(0.16f, 0.14f, 0.24f) * U, 1, 0.12f, 95);
            M.Color = boneD;
            for (int s = -1; s <= 1; s += 2)
                M.Blob(head + new Vector3(0f, 0.02f * U, s * 0.2f * U), new Vector3(0.13f, 0.12f, 0.09f) * U, 0, 0.15f, 96 + s);
            M.Jitter = 0f; M.Color = gold;
            M.Band(head, 0.15f * U, 0.15f * U, -0.08f * U, -0.02f * U, 8, 1f);
            M.Color = C("#f4ecd8");
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                M.Spike(head + new Vector3(Mathf.Cos(a) * 0.13f * U, 0.02f * U, Mathf.Sin(a) * 0.2f * U), new Vector3(Mathf.Cos(a), 0.35f, Mathf.Sin(a)), 0.025f * U, 0.11f * U, 4);
            }
            k.End();
            k.Model.CastBone = BB.HandR;
            k.Model.CastOffset = new Vector3(0f, -0.05f * U * k.HandK, (len - 0.18f * U)) + new Vector3(0f, 0f, 0.012f * U);
            var m = k.Model;
            m.HoldR = UnitHold.Shoulder; m.Strike = UnitStrike.Mace; m.TwoHanded = true; m.Ranged = UnitRanged.Throw;
            m.Heavy = 0.9f; m.MaxCadence = 1.4f; m.TurnRate = 240f;
            m.DustColor = new Color(0.5f, 0.44f, 0.4f, 0.4f);
            var mm = Done(k, key, UnitGait.Heavy);
            mm.HeadTop = new Vector3(0f, k.H - k.HeadY + 0.45f * r, 0f);
            return mm;
        }
    }
}
