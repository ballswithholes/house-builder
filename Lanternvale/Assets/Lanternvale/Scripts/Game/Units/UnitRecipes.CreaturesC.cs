// Expansion models, group C (Docs/Expansion.md §9 models-c): beasts, fliers, giants and dragons of the level 12-30
// zones, the hidden dungeons and the two raids. Registered through RegisterCreaturesC (UnitRecipes.TryBuildExpansion).
//
//   fliers   cr_hawk (biped flier, owl pattern), cr_harpy (biped floater with feathered wings)
//   beasts   cr_crocolisk, cr_wolf_frost, cr_blight_hound (quads), cr_spider_giant (spider), cr_yeti (heavy biped)
//   roots    cr_rootling (small biped), cr_rootwarden (heavy biped), cr_r1_thornmaw (boss quad)
//   dragons  cr_drake_whelp (hovering winged quad), cr_frost_drake, cr_r2_cinder_drake (+ _emberjaw / _ashtongue),
//            cr_r2_vyrmathra (the Ashwyrm) — winged quads on the QB wing bones (QuadKit.Wings, UnitAnimator wings)
//   bosses   cr_r1_hollow_heart (a rooted heart-lantern on the spider rig: eight root tendrils, a beating heart),
//            cr_r2_frostclaw (the yeti matriarch)
//
// Natural heights are what the data `size` scales; the recommended sizes per key are in Docs/ArtKeys.md.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class UnitRecipes
    {
        static partial void RegisterCreaturesC(Dictionary<string, Func<UnitModel>> d)
        {
            d["cr_drake_whelp"] = () => Drake("cr_drake_whelp", DrakeKind.Whelp);
            d["cr_frost_drake"] = () => Drake("cr_frost_drake", DrakeKind.Frost);
            d["cr_r2_cinder_drake"] = () => Drake("cr_r2_cinder_drake", DrakeKind.Cinder);
            d["cr_r2_cinder_drake_emberjaw"] = d["cr_r2_emberjaw"] = () => Drake("cr_r2_cinder_drake_emberjaw", DrakeKind.Emberjaw);
            d["cr_r2_cinder_drake_ashtongue"] = d["cr_r2_ashtongue"] = () => Drake("cr_r2_cinder_drake_ashtongue", DrakeKind.Ashtongue);
            d["cr_r2_vyrmathra"] = () => Vyrmathra("cr_r2_vyrmathra");
            d["cr_hawk"] = () => Hawk("cr_hawk");
            d["cr_harpy"] = () => Harpy("cr_harpy");
            d["cr_crocolisk"] = () => Crocolisk("cr_crocolisk");
            d["cr_wolf_frost"] = () => FrostWolf("cr_wolf_frost");
            d["cr_yeti"] = () => Yeti("cr_yeti", false);
            d["cr_rootling"] = () => Rootling("cr_rootling");
            d["cr_blight_hound"] = () => BlightHound("cr_blight_hound");
            d["cr_spider_giant"] = () => GiantSpider("cr_spider_giant");
            d["cr_rootwarden"] = d["cr_dg1_rootwarden"] = () => Rootwarden("cr_rootwarden");
            d["cr_r1_thornmaw"] = () => Thornmaw("cr_r1_thornmaw");
            d["cr_r1_hollow_heart"] = () => HollowHeart("cr_r1_hollow_heart");
            d["cr_r2_frostclaw"] = () => Yeti("cr_r2_frostclaw", true);
        }

        // ================================================================== dragons (winged quads)

        enum DrakeKind { Whelp, Frost, Cinder, Emberjaw, Ashtongue }

        struct DragonLook
        {
            public Color Back, Saddle, Belly, Leg, Claw, Horn, HornTip, Eye, Membrane, Under, WingArm, Spine, Glow, Mouth;
            public bool GlowEyes, Embers, Ice;
        }

        static DragonLook DrakeLook(DrakeKind kind)
        {
            var l = new DragonLook();
            switch (kind)
            {
                case DrakeKind.Whelp:
                    l.Back = C("#d9774a"); l.Saddle = C("#b85a36"); l.Belly = C("#f7dca2"); l.Leg = C("#c2643c"); l.Claw = C("#f4ead2");
                    l.Horn = C("#f4e6c4"); l.HornTip = C("#d8c094"); l.Eye = C("#f0a832"); l.Membrane = C("#e8935a"); l.Under = C("#fbd9a0");
                    l.WingArm = C("#b85a36"); l.Spine = C("#f4d08a"); l.Glow = C("#ffb040"); l.Mouth = C("#7a2a22");
                    break;
                case DrakeKind.Frost:
                    l.Back = C("#9cc6dc"); l.Saddle = C("#6b98bd"); l.Belly = C("#eef6f4"); l.Leg = C("#7aa6c6"); l.Claw = C("#f2fbff");
                    l.Horn = C("#e6f7ff"); l.HornTip = C("#9fe6ff"); l.Eye = C("#a6f4ff"); l.Membrane = C("#7aa8cf"); l.Under = C("#dceff6");
                    l.WingArm = C("#5d87ad"); l.Spine = C("#d4f2ff"); l.Glow = C("#9fe8ff"); l.Mouth = C("#2a4a6a");
                    l.GlowEyes = true; l.Ice = true;
                    break;
                case DrakeKind.Emberjaw:
                    l.Back = C("#b8462a"); l.Saddle = C("#7e2a1c"); l.Belly = C("#f2a453"); l.Leg = C("#8e3220"); l.Claw = C("#2e2220");
                    l.Horn = C("#3a2a26"); l.HornTip = C("#ffb050"); l.Eye = C("#ffe070"); l.Membrane = C("#8e2e1e"); l.Under = C("#f49a52");
                    l.WingArm = C("#5e2016"); l.Spine = C("#2e2220"); l.Glow = C("#ff8a2a"); l.Mouth = C("#ffb040");
                    l.GlowEyes = true; l.Embers = true;
                    break;
                case DrakeKind.Ashtongue:
                    l.Back = C("#7c7090"); l.Saddle = C("#554a66"); l.Belly = C("#c2b4cc"); l.Leg = C("#544a5e"); l.Claw = C("#e8e0ea");
                    l.Horn = C("#e2d8e6"); l.HornTip = C("#c8a8ff"); l.Eye = C("#e6d4ff"); l.Membrane = C("#4e4260"); l.Under = C("#a891c4");
                    l.WingArm = C("#3a3246"); l.Spine = C("#e2d8e6"); l.Glow = C("#c9a2ff"); l.Mouth = C("#d8b8ff");
                    l.GlowEyes = true; l.Embers = true;
                    break;
                default:   // Cinder: charcoal scales split by ember seams
                    l.Back = C("#4e4240"); l.Saddle = C("#352c2c"); l.Belly = C("#d9894a"); l.Leg = C("#3e3434"); l.Claw = C("#e8dcc8");
                    l.Horn = C("#e6dac2"); l.HornTip = C("#5a4a40"); l.Eye = C("#ffd060"); l.Membrane = C("#4a3634"); l.Under = C("#d0683a");
                    l.WingArm = C("#2e2626"); l.Spine = C("#e6dac2"); l.Glow = C("#ff8a3a"); l.Mouth = C("#ff9a40");
                    l.GlowEyes = true; l.Embers = true;
                    break;
            }
            return l;
        }

        /// <summary>
        /// A drake: a lizard body with a raised S neck, a long horned head, clawed legs, a long spined tail and folded
        /// wings. The whelp is a chubby big-headed baby that hovers on quick beats; the frost drake is icy blue with
        /// crystal horns and spines; the cinder drakes (raid, Ashwyrm's Roost) are charcoal with ember seams, the
        /// Emberjaw red-orange with a molten jaw, the Ashtongue ash-violet with pale violet fire.
        /// </summary>
        static UnitModel Drake(string key, DrakeKind kind)
        {
            var l = DrakeLook(kind);
            bool whelp = kind == DrakeKind.Whelp;
            bool raid = kind == DrakeKind.Cinder || kind == DrakeKind.Emberjaw || kind == DrakeKind.Ashtongue;
            QuadKit k = whelp
                ? new QuadKit(81, 0.42f, 0.46f, 0.46f, 0.21f, 0.13f, 0.05f, 0.4f, 0.42f, 0.2f, 0.26f, 0.17f, 0.03f)
                : new QuadKit(raid ? 83 : 82, 0.98f, 1.08f, 1.36f, 0.46f, 0.33f, 0.13f, 0.9f, 0.94f, 0.6f, 0.86f, 0.25f, 0.05f);
            k.M.Jitter = 0.06f;
            k.Body(l.Back, l.Belly, 1.0f, whelp ? 1.1f : 1.08f, whelp ? 0.1f : 0.35f, whelp ? 1.0f : 0.92f, l.Saddle);
            if (l.Embers) EmberSeams(k, l.Glow, whelp ? 3 : 5);
            DragonNeck(k, l, whelp ? 0.95f : 1f);
            var tip = DragonHead(k, l, whelp ? 0.75f : 1.25f, whelp, kind == DrakeKind.Frost ? 2 : (raid ? 1 : 0));
            DragonLegs(k, l, whelp);
            k.LongTail(l.Back, whelp ? 0.55f : 1.9f, k.BodyR * (whelp ? 0.45f : 0.5f), 1f, whelp ? 0.4f : 0.6f, l.Spine, k.BodyR * (whelp ? 0.25f : 0.4f), l.Belly);
            DragonSpines(k, l.Spine, whelp ? 5 : 9, whelp ? 0.4f : 0.65f, l.Ice);
            k.Wings(whelp ? 0.78f : 1.75f, l.WingArm, l.Membrane, l.Under, l.Claw, whelp ? 3 : 4, whelp ? 0.85f : 1f, raid ? 0.35f : 0f, whelp ? 0.05f : 0.035f, Paint.Shade(l.WingArm, 1.1f));
            float height = whelp ? 1.0f : 2.4f;
            k.Finish(key, height, raid ? UnitStrike.Swipe : UnitStrike.Bite, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            m.WingBeat = whelp ? 2.6f : 1.1f;
            m.WingFold = whelp ? 0.2f : 0.78f;
            if (whelp)
            {
                m.FloatHeight = 0.55f;
                m.Dust = false;
                m.Breath = 1.3f;
            }
            else
            {
                m.Heavy = raid ? 0.75f : 0.5f;
                m.MaxCadence = raid ? 1.6f : 2.0f;
                m.TurnRate = raid ? 200f : 300f;
                m.DustColor = l.Ice ? new Color(0.92f, 0.96f, 1f, 0.4f) : new Color(0.6f, 0.52f, 0.48f, 0.4f);
            }
            return Bake(k);
        }

        /// <summary>Glowing ember seams on the flanks (cinder drakes, the Ashwyrm): short crack strokes along the ribs.</summary>
        static void EmberSeams(QuadKit k, Color glow, int n)
        {
            var keepE = k.M.Emission;
            k.M.Emission = 1f; k.M.Color = glow;
            float z0 = k.Bind[QB.Hips].z - k.BodyR * 0.2f, z1 = k.Bind[QB.Chest].z + k.BodyR * 0.3f;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < n; i++)
                {
                    float z = Mathf.Lerp(z0, z1, (i + 0.5f) / n);
                    k.M.Bone = z > (k.Bind[QB.Hips].z + k.Bind[QB.Chest].z) * 0.5f ? QB.Chest : QB.Hips;
                    // a forked crack: a slanted main stroke and a short branch, angles and lengths varied per seam
                    float h = Mathf.Abs(Mathf.Sin(i * 12.9898f + s * 4.1f));
                    var a = k.TorsoPoint(z + k.BodyR * (0.08f - 0.16f * h), s * (62f + h * 22f), 0.004f);
                    var b = k.TorsoPoint(z - k.BodyR * (0.1f + 0.12f * h), s * (98f + (i % 3) * 9f), 0.004f);
                    var c = k.TorsoPoint(z + k.BodyR * (0.06f + 0.1f * h), s * (124f + h * 10f), 0.004f);
                    k.M.Segment(a, b, k.BodyR * 0.03f, k.BodyR * 0.04f, 4);
                    k.M.Segment(b, c, k.BodyR * 0.04f, k.BodyR * 0.015f, 4);
                }
            k.M.Emission = keepE;
        }

        /// <summary>The S neck from the shoulders to the head, a paler throat plate strip and the nape spines.</summary>
        static void DragonNeck(QuadKit k, DragonLook l, float thick)
        {
            k.M.Bone = QB.Neck;
            var a = k.NeckBase - new Vector3(0f, k.BodyR * 0.25f, k.BodyR * 0.3f);
            var b = k.HeadBase;
            var ctrl = Vector3.Lerp(a, b, 0.5f) + new Vector3(0f, (b - a).magnitude * 0.06f, -(b - a).magnitude * 0.14f);
            k.M.Color = l.Back;
            k.M.Curve(a, ctrl, b, k.BodyR * 0.62f * thick, k.HeadR * 0.72f * thick, 4, 8);
            k.M.Color = l.Belly;
            var fw = new Vector3(0f, -k.BodyR * 0.12f, k.BodyR * 0.2f);
            k.M.Curve(a + fw * 1.4f, ctrl + fw, b + fw * 0.5f + Vector3.down * k.HeadR * 0.15f, k.BodyR * 0.46f * thick, k.HeadR * 0.5f * thick, 4, 7);
        }

        /// <summary>
        /// A dragon head on QB.Head (snout along +Z) and the jaw on QB.Jaw: skull, a long faceted snout with nostrils and
        /// a row of teeth, brow ridges over (glowing) eyes, swept-back horns (0 plain pair, 1 a crown of horns, 2 ice
        /// crystal horns), cheek frills. `cute`: a big round head, short snout, big shiny eyes, nub horns. Returns the snout tip.
        /// </summary>
        static Vector3 DragonHead(QuadKit k, DragonLook l, float snoutK, bool cute, int horns)
        {
            var M = k.M;
            float r = k.HeadR;
            var hb = k.HeadBase;
            var hc = hb + new Vector3(0f, r * 0.15f, r * 0.2f);
            M.Bone = QB.Head; M.Color = l.Back;
            M.Sphere(hc, cute ? new Vector3(r * 1.12f, r, r * 1.05f) : new Vector3(r * 0.92f, r * 0.78f, r * 1.0f), 10, 7);
            // snout: a faceted wedge, flatter than tall
            float sl = r * snoutK * (cute ? 1.0f : 1.55f);
            var s0 = hc + new Vector3(0f, -r * 0.08f, r * 0.55f);
            var s1 = s0 + new Vector3(0f, -r * (cute ? 0.12f : 0.2f), sl);
            M.Push().Translate(s0).Scale(new Vector3(1f, 0.72f, 1f)).Translate(-s0);
            M.Segment(s0, s1, r * (cute ? 0.68f : 0.6f), r * (cute ? 0.5f : 0.36f), 7);
            M.Pop();
            M.Color = l.Saddle;
            M.Sphere(s1 + new Vector3(0f, r * 0.06f, -r * 0.05f), new Vector3(r * 0.38f, r * 0.2f, r * 0.26f) * (cute ? 1.2f : 1f), 7, 4);
            // nostrils (smoking embers on fire drakes)
            var keepE = M.Emission;
            M.Color = l.Embers ? l.Glow : Paint.Shade(l.Saddle, 0.5f);
            M.Emission = l.Embers ? 1f : 0f;
            for (int s = -1; s <= 1; s += 2)
                M.Sphere(s1 + new Vector3(s * r * 0.17f, r * 0.12f, r * 0.12f), r * 0.07f, 5, 3);
            M.Emission = keepE;
            // upper teeth
            if (!cute)
            {
                M.Color = C("#f6f0e0");
                for (int s = -1; s <= 1; s += 2)
                    for (int i = 0; i < 4; i++)
                    {
                        var p = Vector3.Lerp(s0, s1, 0.3f + i * 0.2f) + new Vector3(s * r * Mathf.Lerp(0.42f, 0.28f, i / 3f), -r * 0.3f, 0f);
                        M.Spike(p, Vector3.down + Vector3.forward * 0.2f, r * 0.05f, r * 0.16f, 4);
                    }
            }
            // eyes and brows
            M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = hc + new Vector3(s * r * (cute ? 0.58f : 0.55f), r * (cute ? 0.12f : 0.18f), r * (cute ? 0.68f : 0.62f));
                M.Push().Translate(e).Rotate(0f, s * 38f, 0f);
                if (cute)
                {
                    M.Color = C("#2a1c14"); M.Sphere(Vector3.zero, new Vector3(r * 0.26f, r * 0.3f, r * 0.14f), 8, 5);
                    M.Color = l.Eye; M.Sphere(new Vector3(0f, -r * 0.05f, r * 0.05f), new Vector3(r * 0.18f, r * 0.2f, r * 0.1f), 7, 4);
                    M.Emission = 0.7f; M.Color = Color.white;
                    M.Sphere(new Vector3(s * r * 0.06f, r * 0.12f, r * 0.12f), r * 0.07f, 5, 3);
                    M.Emission = keepE;
                }
                else
                {
                    M.Emission = l.GlowEyes ? 1f : 0.3f; M.Color = l.Eye;
                    M.Sphere(Vector3.zero, new Vector3(r * 0.24f, r * 0.12f, r * 0.1f), 7, 4);
                    M.Emission = keepE;
                    M.Color = C("#1a1210");
                    M.Box(new Vector3(0f, 0f, r * 0.07f), new Vector3(r * 0.04f, r * 0.17f, r * 0.03f));
                }
                M.Pop();
                // brow ridge (an angry wedge on adults, a soft bump on the whelp)
                M.Color = l.Saddle;
                M.Push().Translate(e + new Vector3(-s * r * 0.04f, r * (cute ? 0.26f : 0.16f), -r * 0.02f)).Rotate(cute ? 0f : 12f, s * 30f, s * (cute ? -8f : -18f));
                M.Box(Vector3.zero, new Vector3(r * (cute ? 0.3f : 0.42f), r * 0.1f, r * 0.24f));
                M.Pop();
            }
            M.Jitter = 0.06f;
            // horns
            var hornBase = hc + new Vector3(0f, r * 0.6f, -r * 0.35f);
            if (cute)
            {
                M.Color = l.Horn;
                for (int s = -1; s <= 1; s += 2)
                    M.Spike(hornBase + new Vector3(s * r * 0.42f, 0f, 0f), new Vector3(s * 0.25f, 1f, -0.7f), r * 0.16f, r * 0.5f, 5);
                M.Color = l.Spine;
                for (int i = 0; i < 3; i++)
                    M.Spike(hc + new Vector3(0f, r * (0.85f - i * 0.15f), -r * (0.1f + i * 0.35f)), new Vector3(0f, 1f, -0.8f), r * 0.09f, r * 0.24f, 4);
            }
            else
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    var h0 = hornBase + new Vector3(s * r * 0.45f, 0f, 0f);
                    if (horns == 2)
                    {
                        // ice crystal horns: faceted shards with a glow
                        M.Emission = 0.35f;
                        M.Color = l.Horn;
                        M.Spike(h0, new Vector3(s * 0.35f, 0.8f, -1f), r * 0.17f, r * 1.4f, 5);
                        M.Color = l.HornTip;
                        M.Spike(h0 + new Vector3(s * r * 0.15f, -r * 0.1f, r * 0.1f), new Vector3(s * 0.8f, 0.5f, -0.6f), r * 0.1f, r * 0.75f, 4);
                        M.Emission = keepE;
                    }
                    else
                    {
                        M.Color = l.Horn;
                        var h1 = h0 + new Vector3(s * r * 0.35f, r * 0.55f, -r * 0.9f);
                        var h2 = h1 + new Vector3(s * r * 0.1f, r * 0.15f, -r * 0.7f);
                        M.Curve(h0, h1, h2, r * 0.17f, r * 0.03f, 4, 6);
                        if (horns == 1)
                        {
                            M.Color = l.HornTip;
                            var c0 = h0 + new Vector3(s * r * 0.25f, -r * 0.25f, -r * 0.05f);
                            M.Curve(c0, c0 + new Vector3(s * r * 0.55f, r * 0.15f, -r * 0.35f), c0 + new Vector3(s * r * 0.75f, r * 0.45f, -r * 0.75f), r * 0.1f, r * 0.02f, 3, 5);
                        }
                    }
                }
                // cheek frills: short spikes swept back from the jaw hinge
                M.Color = l.Spine;
                for (int s = -1; s <= 1; s += 2)
                    for (int i = 0; i < 3; i++)
                        M.Spike(hc + new Vector3(s * r * 0.72f, -r * (0.1f + i * 0.2f), -r * 0.25f), new Vector3(s * 0.7f, 0.3f - i * 0.25f, -1f), r * 0.09f, r * (0.45f - i * 0.08f), 4);
            }
            // jaw with lower teeth and a tongue glow in the mouth (shown when it opens)
            M.Bone = QB.Jaw;
            var j = k.Bind[QB.Jaw];
            float jl = sl * 0.74f + r * 0.3f;
            M.Color = Paint.Mix(l.Belly, l.Saddle, 0.45f);
            M.Push().Translate(j).Scale(new Vector3(1f, 0.6f, 1f)).Translate(-j);
            M.Segment(j + new Vector3(0f, 0f, -r * 0.1f), j + new Vector3(0f, -r * 0.08f, jl), r * (cute ? 0.5f : 0.5f), r * (cute ? 0.38f : 0.34f), 6);
            M.Pop();
            M.Color = l.Mouth; M.Emission = l.Embers ? 0.9f : 0f;
            M.Sphere(j + new Vector3(0f, r * 0.08f, jl * 0.5f), new Vector3(r * 0.28f, r * 0.06f, jl * 0.38f), 6, 3);
            M.Emission = keepE;
            if (!cute)
            {
                M.Color = C("#f6f0e0");
                for (int s = -1; s <= 1; s += 2)
                    for (int i = 0; i < 3; i++)
                        M.Spike(j + new Vector3(s * r * 0.22f, r * 0.08f, jl * (0.45f + i * 0.2f)), Vector3.up, r * 0.045f, r * 0.13f, 4);
            }
            var m = k.Model;
            m.HeadBone = QB.Head;
            m.HeadTop = hc + new Vector3(0f, r * (cute ? 1.2f : 1.5f), 0f) - hb;
            m.CastBone = QB.Head;
            return s1;
        }

        /// <summary>Thick scaled legs with clawed paws (three claws forward, one back).</summary>
        static void DragonLegs(QuadKit k, DragonLook l, bool cute)
        {
            k.Legs(l.Back, l.Leg, l.Leg, cute ? 1.15f : 1.12f, false, 1.08f);
            k.M.Color = l.Claw;
            int[] feet = { QB.FLF, QB.FRF, QB.BLF, QB.BRF };
            float r = k.LegR * (cute ? 1.15f : 1.12f);
            foreach (int f in feet)
            {
                k.M.Bone = f;
                var a = k.Bind[f];
                for (int c = -1; c <= 1; c++)
                    k.M.Spike(new Vector3(a.x + c * r * 0.55f, r * 0.35f, a.z + r * 1.1f), new Vector3(c * 0.25f, -0.35f, 1f), r * 0.2f, r * (cute ? 0.45f : 0.75f), 4);
            }
        }

        /// <summary>A row of dorsal spines/plates from the nape to the rump, tallest over the shoulders (ice: glassy shards).</summary>
        static void DragonSpines(QuadKit k, Color c, int n, float size, bool ice)
        {
            var keepE = k.M.Emission;
            if (ice) k.M.Emission = 0.3f;
            k.M.Color = c;
            float zMid = (k.Bind[QB.Hips].z + k.Bind[QB.Chest].z) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? i / (n - 1f) : 0f;
                float z = Mathf.Lerp(k.Bind[QB.Chest].z + k.BodyR * 0.55f, k.Bind[QB.Hips].z - k.BodyR * 0.6f, t);
                float len = k.BodyR * size * Mathf.Lerp(1f, 0.55f, t) * (i % 2 == 0 ? 1f : 0.8f);
                k.M.Bone = z > zMid ? QB.Chest : QB.Hips;
                var at = new Vector3(0f, k.TopY(z) - k.BodyR * 0.05f, z);
                k.M.Aim(at, new Vector3(0f, 1f, -0.55f));
                k.M.Push().Scale(new Vector3(0.38f, 1f, 1.5f));
                k.M.Cone(Vector3.zero, k.BodyR * 0.2f, len, 4);
                k.M.Pop();
                k.M.Pop();
            }
            k.M.Emission = keepE;
        }

        // ================================================================== Vyrmathra, the Ashwyrm (raid finale)

        /// <summary>
        /// Vyrmathra: a great ash-black dragon (natural 4.1 m to the crown, ≈ 9 m long; data size ≈ 7.5) with molten
        /// seams along the flanks and throat, a furnace glowing between the belly plates, a crown of bone-gold horns,
        /// molten eyes and nostrils, a double row of dorsal plates with ember tips, a long tail ending in a spade blade,
        /// and vast tattered wings (5 fingers) whose undersides glow ember-orange. Rears and stomps with wings thrown open;
        /// its breath (cast) spreads the wings wide.
        /// </summary>
        static UnitModel Vyrmathra(string key)
        {
            var l = new DragonLook
            {
                Back = C("#4d4347"), Saddle = C("#332a2f"), Belly = C("#c4743f"), Leg = C("#352d32"), Claw = C("#ecdcb2"),
                Horn = C("#eedca8"), HornTip = C("#b89a62"), Eye = C("#ffd25a"), Membrane = C("#5a3c40"), Under = C("#c8592e"),
                WingArm = C("#2a2226"), Spine = C("#2b2428"), Glow = C("#ff8a32"), Mouth = C("#ffa040"),
                GlowEyes = true, Embers = true,
            };
            var k = new QuadKit(91, 1.55f, 1.75f, 2.3f, 0.8f, 0.56f, 0.22f, 1.45f, 1.5f, 1.25f, 1.75f, 0.5f, 0.08f);
            var M = k.M;
            M.Jitter = 0.06f;
            k.Body(l.Back, l.Belly, 1.0f, 1.14f, 0.55f, 0.93f, l.Saddle);
            EmberSeams(k, l.Glow, 7);
            // the furnace: belly plates with molten light between them
            float zf = k.Bind[QB.Chest].z + k.BodyR * 0.6f, zb = k.Bind[QB.Hips].z - k.BodyR * 0.1f;
            float zMid = (k.Bind[QB.Hips].z + k.Bind[QB.Chest].z) * 0.5f;
            for (int i = 0; i < 7; i++)
            {
                float z = Mathf.Lerp(zf, zb, i / 6f);
                M.Bone = z > zMid ? QB.Chest : QB.Hips;
                var side = k.TorsoPoint(z, 128f, 0.01f);
                float w = Mathf.Abs(side.x) * 1.9f;
                if (i < 6)
                {
                    // molten light between the belly plates (the plates are the Body's belly colour)
                    M.Emission = 1f; M.Color = l.Glow;
                    float z2 = Mathf.Lerp(zf, zb, (i + 0.5f) / 6f);
                    M.Box(k.TorsoPoint(z2, 180f, -0.01f), new Vector3(w * 0.7f, 0.05f, k.BodyR * 0.07f));
                    M.Emission = 0f;
                }
            }
            DragonNeck(k, l, 1.08f);
            // molten throat seams down the front of the neck
            M.Bone = QB.Neck; M.Emission = 1f; M.Color = l.Glow;
            for (int i = 0; i < 4; i++)
            {
                var p = Vector3.Lerp(k.NeckBase, k.HeadBase, 0.15f + i * 0.2f) + new Vector3(0f, -k.BodyR * 0.3f, k.BodyR * 0.42f);
                M.Box(p, new Vector3(k.BodyR * 0.5f, 0.035f, 0.05f));
            }
            M.Emission = 0f;
            var tip = DragonHead(k, l, 1.3f, false, 1);
            // the crown: a second and third pair of horns sweeping back from the skull, a nose horn, a jaw beard of spikes
            float r = k.HeadR;
            var hc = k.HeadBase + new Vector3(0f, r * 0.15f, r * 0.2f);
            M.Bone = QB.Head;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int i = 0; i < 2; i++)
                {
                    M.Color = i == 0 ? l.Horn : l.HornTip;
                    var h0 = hc + new Vector3(s * r * (0.2f + i * 0.5f), r * (0.75f - i * 0.25f), -r * (0.55f + i * 0.1f));
                    M.Curve(h0, h0 + new Vector3(s * r * (0.15f + i * 0.4f), r * (0.75f - i * 0.2f), -r * 0.6f),
                            h0 + new Vector3(s * r * (0.3f + i * 0.6f), r * (1.05f - i * 0.35f), -r * 1.35f), r * (0.13f - i * 0.02f), r * 0.02f, 4, 5);
                }
            }
            M.Color = l.Horn;
            M.Spike(tip + new Vector3(0f, r * 0.15f, -r * 0.45f), new Vector3(0f, 1f, 0.35f), r * 0.1f, r * 0.42f, 5);
            M.Bone = QB.Jaw; M.Color = l.Saddle;
            var jw = k.Bind[QB.Jaw];
            for (int i = 0; i < 4; i++)
                M.Spike(jw + new Vector3((i - 1.5f) * r * 0.18f, -r * 0.2f, r * (0.2f + Mathf.Abs(i - 1.5f) * 0.1f)), new Vector3((i - 1.5f) * 0.3f, -1f, -0.6f), r * 0.07f, r * 0.45f, 4);
            DragonLegs(k, l, false);
            // the tail: long, spined, ending in a bone spade with an ember edge
            var tt = k.LongTail(l.Back, 3.4f, k.BodyR * 0.52f, 1f, 0.55f, l.Spine, k.BodyR * 0.42f, l.Belly);
            M.Bone = QB.Tail3;
            var td = (tt - k.Bind[QB.Tail3]).normalized;
            M.Push().Translate(tt).Rotate(Quaternion.LookRotation(td, Vector3.up)).Scale(1.7f);
            M.Color = l.Claw;
            M.Flat(new[] { new Vector2(0f, -0.05f), new Vector2(0.32f, 0.22f), new Vector2(0f, 0.75f), new Vector2(-0.32f, 0.22f) }, 0.06f);
            M.Emission = 1f; M.Color = l.Glow;
            M.Flat(new[] { new Vector2(0f, 0.1f), new Vector2(0.16f, 0.26f), new Vector2(0f, 0.55f), new Vector2(-0.16f, 0.26f) }, 0.075f);
            M.Emission = 0f;
            M.Pop();
            // a double row of dorsal plates, ember-tipped
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f;
                float z = Mathf.Lerp(k.Bind[QB.Chest].z + k.BodyR * 0.6f, k.Bind[QB.Hips].z - k.BodyR * 0.75f, t);
                M.Bone = z > zMid ? QB.Chest : QB.Hips;
                float len = k.BodyR * Mathf.Lerp(0.85f, 0.45f, t) * (i % 2 == 0 ? 1f : 0.82f);
                for (int s = -1; s <= 1; s += 2)
                {
                    var at = new Vector3(s * k.BodyR * 0.14f, k.TopY(z) - k.BodyR * 0.06f, z);
                    var dir = new Vector3(s * 0.28f, 1f, -0.5f);
                    M.Color = l.Spine;
                    M.Aim(at, dir);
                    M.Push().Scale(new Vector3(0.35f, 1f, 1.5f));
                    M.Cone(Vector3.zero, k.BodyR * 0.2f, len, 4);
                    M.Pop();
                    M.Pop();
                    M.Emission = 1f; M.Color = l.Glow;
                    M.Spike(at + dir.normalized * len * 0.62f, dir, k.BodyR * 0.05f, len * 0.4f, 4);
                    M.Emission = 0f;
                }
            }
            k.Wings(3.6f, l.WingArm, l.Membrane, l.Under, l.Claw, 5, 1.05f, 0.6f, 0.03f, C("#8e3a22"));
            float height = k.HeadBase.y + r * 1.9f;
            k.Finish(key, height, UnitStrike.Stomp, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            m.HeadTop = hc + new Vector3(0f, r * 2.1f, -r * 0.4f) - k.HeadBase;
            m.Heavy = 1f; m.MaxCadence = 0.9f; m.TurnRate = 90f; m.StrideK = 1.05f;
            m.WingBeat = 0.7f; m.WingFold = 0.72f;
            m.DustColor = new Color(0.45f, 0.4f, 0.4f, 0.5f);
            return Bake(k);
        }

        // ================================================================== frost wolf, blight hound, crocolisk, giant spider

        /// <summary>
        /// Skyreach frost wolf: a wolf a size up from the grey, snow-white with a blue-grey saddle, a thick frosty ruff,
        /// tufted ears, pale glowing eyes and glassy ice crystals growing from the shoulders and along the spine.
        /// </summary>
        static UnitModel FrostWolf(string key)
        {
            var k = new QuadKit(84, 0.6f, 0.66f, 0.6f, 0.205f, 0.11f, 0.048f, 0.56f, 0.58f, 0.15f, 0.24f, 0.125f);
            Color back = C("#eef2f6"), belly = C("#ffffff"), saddle = C("#a9bcd2"), legs = C("#c6d3e2"), eyes = C("#8ff0ff"),
                  nose = C("#2a3446"), ice = C("#c4ecff"), iceD = C("#7cc4ea");
            k.Body(back, belly, 0.98f, 1.1f, 0.25f, 0.87f, saddle);
            k.Neck(back, 1.12f, 1.02f);
            // the frost ruff: white lumps round the neck, blue-tipped locks standing out of it
            k.M.Jitter = 0.08f;
            k.M.Bone = QB.Chest;
            var nb = k.NeckBase;
            for (int i = 0; i < 6; i++)
            {
                float a = (i / 5f - 0.5f) * 2.4f;
                k.M.Color = i % 2 == 0 ? belly : C("#dfe8f2");
                k.M.Blob(nb + new Vector3(Mathf.Sin(a) * 0.1f, 0.02f - Mathf.Abs(a) * 0.03f, -0.02f + Mathf.Cos(a) * 0.03f), new Vector3(0.12f, 0.11f, 0.11f), 1, 0.22f, 40 + i);
            }
            for (int i = 0; i < 8; i++)
            {
                float a = (i / 7f - 0.5f) * 2.6f;
                k.M.Color = (i & 1) == 0 ? C("#dfe8f2") : saddle;
                k.M.Spike(nb + new Vector3(Mathf.Sin(a) * 0.12f, 0.04f, -0.05f), new Vector3(Mathf.Sin(a), 0.6f, -0.7f), 0.035f, 0.14f, 4);
            }
            k.M.Jitter = 0.055f;
            var tip = k.Head(back, belly, nose, eyes, 0.17f, 0.5f, true, 1.25f);
            k.Jaw(C("#e4ecf4"), 0.2f, C("#f4f8ff"), 0.36f);
            k.Ears(back, saddle, 1.05f, 0.55f, 0.12f, false, true);
            k.Legs(back, legs, Paint.Shade(legs, 0.85f), 1.05f);
            k.Tail(back, 0.38f, 0.066f, ice, true, 0.5f);
            // ice crystals: a cluster on each shoulder and a short ridge along the spine
            k.M.Emission = 0.35f;
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = QB.Chest;
                var sh = k.TorsoPoint(k.Bind[QB.Chest].z - 0.02f, s * 40f, -0.01f);
                k.M.Color = ice; k.M.Spike(sh, new Vector3(s * 0.5f, 1f, -0.3f), 0.035f, 0.16f, 4);
                k.M.Color = iceD; k.M.Spike(sh + new Vector3(s * 0.03f, -0.02f, -0.05f), new Vector3(s * 0.9f, 0.7f, -0.4f), 0.025f, 0.1f, 4);
            }
            for (int i = 0; i < 4; i++)
            {
                float z = Mathf.Lerp(k.Bind[QB.Chest].z - 0.12f, k.Bind[QB.Hips].z, i / 3f);
                k.M.Bone = z > 0f ? QB.Chest : QB.Hips;
                k.M.Color = (i & 1) == 0 ? ice : iceD;
                k.M.Spike(new Vector3(0f, k.TopY(z) - 0.02f, z), new Vector3((i & 1) == 0 ? 0.2f : -0.2f, 1f, -0.4f), 0.03f, 0.11f - i * 0.015f, 4);
            }
            k.M.Emission = 0f;
            k.Finish(key, 1.05f, UnitStrike.Bite, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            m.Heavy = 0.15f;
            m.DustColor = new Color(0.94f, 0.97f, 1f, 0.42f);
            return Bake(k);
        }

        /// <summary>
        /// Blight hound (the Hollow's hunting beast): gaunt and tall at the shoulder, violet-charcoal coat, exposed bone
        /// ribs and a bone skull-mask over the muzzle with glowing eye holes, a ragged dark mane, big blight crystals
        /// breaking out of the shoulders and a crystal tail tip.
        /// </summary>
        static UnitModel BlightHound(string key)
        {
            var k = new QuadKit(85, 0.66f, 0.8f, 0.64f, 0.2f, 0.12f, 0.044f, 0.72f, 0.64f, 0.18f, 0.26f, 0.13f);
            Color coat = C("#4e4560"), belly = C("#6e6284"), legs = C("#2e2838"), bone = C("#e8dcc4"), boneD = C("#b8a88e"),
                  blight = C("#c48cff"), blightL = C("#e6cdff"), mane = C("#2a2234"), eyes = C("#f0dcff");
            k.Body(coat, belly, 0.9f, 1.12f, 0.7f, 0.78f, Paint.Shade(coat, 0.72f));
            k.Neck(coat, 1.0f, 0.95f);
            var tip = k.Head(coat, Paint.Shade(coat, 0.85f), C("#1e1a24"), eyes, 0.19f, 0.46f, true, 1.0f);
            k.Jaw(Paint.Shade(coat, 0.8f), 0.21f, bone, 0.34f);
            k.Ears(coat, Paint.Shade(coat, 0.6f), 1.15f, 0.5f, 0.25f, false, false, 0.6f);
            // the bone mask over brow and muzzle; the eyes glow through it
            float r = k.HeadR;
            var hc = k.HeadBase + new Vector3(0f, r * 0.2f, r * 0.35f);
            k.M.Bone = QB.Head; k.M.Color = bone;
            k.M.Push().Translate(hc + new Vector3(0f, r * 0.35f, r * 0.45f)).Rotate(18f, 0f, 0f);
            k.M.Sphere(Vector3.zero, new Vector3(r * 0.95f, r * 0.45f, r * 0.7f), 8, 4);
            k.M.Pop();
            k.M.Box(hc + new Vector3(0f, r * 0.1f, r * 1.15f), new Vector3(r * 0.7f, r * 0.22f, r * 0.9f));
            k.M.Color = boneD;
            for (int s = -1; s <= 1; s += 2)
                k.M.Spike(hc + new Vector3(s * r * 0.45f, r * 0.55f, r * 0.1f), new Vector3(s * 0.4f, 0.6f, -1f), r * 0.12f, r * 0.75f, 4);
            k.M.Emission = 1f; k.M.Color = blightL;
            for (int s = -1; s <= 1; s += 2)
                k.M.Sphere(hc + new Vector3(s * r * 0.42f, r * 0.42f, r * 0.82f), new Vector3(r * 0.16f, r * 0.1f, r * 0.08f), 5, 3);
            k.M.Emission = 0f;
            // ragged mane down the neck
            k.M.Bone = QB.Neck; k.M.Color = mane;
            for (int i = 0; i < 6; i++)
            {
                var p = Vector3.Lerp(k.NeckBase, k.HeadBase, i / 5f) + new Vector3(0f, k.BodyR * 0.35f, -0.03f);
                k.M.Spike(p, new Vector3((i & 1) == 0 ? 0.3f : -0.3f, 0.7f, -0.8f), 0.035f, 0.15f, 4);
            }
            k.Legs(coat, legs, legs, 0.95f);
            k.Tail(coat, 0.34f, 0.04f, null, false, 0.7f);
            // exposed ribs on the flanks and a bony spine ridge
            k.M.Color = bone;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 3; i++)
                {
                    float z = k.Bind[QB.Chest].z - 0.04f - i * 0.08f;
                    k.M.Bone = QB.Chest;
                    var a = k.TorsoPoint(z, s * 60f, 0.006f);
                    var c2 = k.TorsoPoint(z - 0.03f, s * 125f, 0.006f);
                    var mid = k.TorsoPoint(z - 0.01f, s * 92f, 0.03f);
                    k.M.Curve(a, mid, c2, 0.016f, 0.01f, 3, 4);
                }
            for (int i = 0; i < 6; i++)
            {
                float z = Mathf.Lerp(k.Bind[QB.Chest].z + 0.05f, k.Bind[QB.Hips].z - 0.1f, i / 5f);
                k.M.Bone = z > 0f ? QB.Chest : QB.Hips;
                k.M.Color = (i & 1) == 0 ? bone : boneD;
                k.M.Spike(new Vector3(0f, k.TopY(z) - 0.02f, z), new Vector3(0f, 1f, -0.5f), 0.025f, 0.07f, 4);
            }
            // blight crystals breaking out of the shoulders, and the tail tip
            k.M.Emission = 1f;
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = QB.Chest;
                var sh = k.TorsoPoint(k.Bind[QB.Chest].z + 0.02f, s * 48f, -0.02f);
                k.M.Color = blight; k.M.Spike(sh, new Vector3(s * 0.45f, 1f, -0.35f), 0.05f, 0.26f, 4);
                k.M.Color = blightL; k.M.Spike(sh + new Vector3(s * 0.02f, 0f, -0.07f), new Vector3(s * 0.9f, 0.8f, -0.2f), 0.035f, 0.15f, 4);
            }
            k.M.Bone = QB.Tail2; k.M.Color = blight;
            var t2 = k.Bind[QB.Tail2];
            var tend = t2 + (t2 - k.Bind[QB.Tail1]).normalized * 0.3f + Vector3.down * 0.18f;
            k.M.Spike(tend, new Vector3(0f, 0.6f, -1f), 0.035f, 0.13f, 4);
            k.M.Emission = 0f;
            k.Finish(key, 1.2f, UnitStrike.Bite, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = tip - k.HeadBase;
            m.Heavy = 0.2f;
            m.DustColor = new Color(0.62f, 0.56f, 0.7f, 0.32f);
            return Bake(k);
        }

        /// <summary>
        /// Mirefen crocolisk: low and long, splayed legs, a flattened mossy-green body with two rows of dark scutes and moss
        /// patches, a long flat toothy snout with yellow eyes on top of the head, a heavy flat tail — and a lily pad with a
        /// pink flower riding on its head.
        /// </summary>
        static UnitModel Crocolisk(string key)
        {
            var k = new QuadKit(86, 0.34f, 0.37f, 0.95f, 0.29f, 0.33f, 0.072f, 0.3f, 0.32f, 0.04f, 0.22f, 0.18f, 0.03f);
            Color back = C("#5f7c47"), saddle = C("#40593a"), belly = C("#dcd39c"), legs = C("#4f6a3c"), scute = C("#34472c"),
                  moss = C("#8eaa5c"), eye = C("#f2d040"), teeth = C("#f6f0dc"), lily = C("#5aa04a"), pink = C("#f4a0c0");
            // a flattened body: wider than tall
            float cy = (k.HipY + k.ChestY) * 0.5f;
            k.M.Push().Translate(0f, cy, 0f).Scale(new Vector3(1.18f, 0.74f, 1f)).Translate(0f, -cy, 0f);
            k.Body(back, belly, 1.0f, 1.04f, 0f, 1.0f, saddle);
            k.M.Pop();
            float zMid = (k.Bind[QB.Hips].z + k.Bind[QB.Chest].z) * 0.5f;
            // scutes in two rows and moss patches
            for (int i = 0; i < 7; i++)
            {
                float z = Mathf.Lerp(k.Bind[QB.Chest].z + 0.1f, k.Bind[QB.Hips].z - 0.18f, i / 6f);
                k.M.Bone = z > zMid ? QB.Chest : QB.Hips;
                float y = cy + (k.TopY(z) - cy) * 0.74f;
                k.M.Color = scute;
                for (int s = -1; s <= 1; s += 2)
                    k.M.Box(new Vector3(s * 0.07f, y + 0.01f, z), new Vector3(0.06f, 0.05f, 0.08f));
                if (i % 3 == 1)
                {
                    k.M.Color = moss;
                    k.M.Blob(new Vector3((i % 2 == 0 ? 1f : -1f) * 0.14f, y - 0.01f, z + 0.04f), new Vector3(0.09f, 0.035f, 0.08f), 0, 0.3f, 50 + i);
                }
            }
            k.Neck(back, 1.1f, 1.0f);
            // the head: a small skull, a long flat snout, eye bumps on top, teeth along the jaw line
            float r = k.HeadR;
            var hc = k.HeadBase + new Vector3(0f, r * 0.05f, r * 0.1f);
            k.M.Bone = QB.Head; k.M.Color = back;
            k.M.Sphere(hc, new Vector3(r * 0.95f, r * 0.62f, r * 0.95f), 9, 5);
            var s0 = hc + new Vector3(0f, -r * 0.1f, r * 0.5f);
            var s1 = s0 + new Vector3(0f, -r * 0.08f, r * 2.6f);
            k.M.Push().Translate(s0).Scale(new Vector3(1f, 0.5f, 1f)).Translate(-s0);
            k.M.Segment(s0, s1, r * 0.7f, r * 0.42f, 7);
            k.M.Pop();
            k.M.Color = saddle;
            for (int s = -1; s <= 1; s += 2) k.M.Sphere(s1 + new Vector3(s * r * 0.14f, r * 0.12f, -r * 0.05f), r * 0.1f, 5, 3);
            k.M.Color = teeth;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 6; i++)
                {
                    var p = Vector3.Lerp(s0, s1, 0.1f + i * 0.16f) + new Vector3(s * r * Mathf.Lerp(0.62f, 0.4f, i / 5f), -r * 0.16f, 0f);
                    k.M.Spike(p, new Vector3(s * 0.2f, -1f, 0f), r * 0.05f, r * 0.17f, 3);
                }
            for (int s = -1; s <= 1; s += 2)
            {
                var e = hc + new Vector3(s * r * 0.42f, r * 0.42f, r * 0.15f);
                k.M.Color = back; k.M.Sphere(e, new Vector3(r * 0.26f, r * 0.22f, r * 0.26f), 7, 4);
                k.M.Color = eye; k.M.Emission = 0.3f;
                k.M.Sphere(e + new Vector3(s * r * 0.08f, r * 0.06f, r * 0.12f), new Vector3(r * 0.15f, r * 0.13f, r * 0.12f), 6, 3);
                k.M.Emission = 0f; k.M.Color = C("#1a1a10");
                k.M.Box(e + new Vector3(s * r * 0.14f, r * 0.08f, r * 0.2f), new Vector3(r * 0.04f, r * 0.14f, r * 0.03f));
            }
            // the lily pad hat
            var lp = hc + new Vector3(r * 0.1f, r * 0.62f, -r * 0.2f);
            k.M.Color = lily;
            k.M.Push().Translate(lp).Rotate(-6f, 30f, 8f);
            k.M.Cylinder(Vector3.zero, r * 0.85f, r * 0.85f, r * 0.06f, 10);
            k.M.Color = pink; k.M.Emission = 0.15f;
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                k.M.Blade(new Vector3(r * 0.2f, r * 0.08f, 0f), new Vector3(r * 0.2f + Mathf.Sin(a) * r * 0.32f, r * 0.34f, Mathf.Cos(a) * r * 0.32f), r * 0.2f);
            }
            k.M.Emission = 0.3f; k.M.Color = C("#f8e070");
            k.M.Sphere(new Vector3(r * 0.2f, r * 0.14f, 0f), r * 0.1f, 5, 3);
            k.M.Emission = 0f;
            k.M.Pop();
            // the lower jaw: long, flat, toothed
            k.M.Bone = QB.Jaw; k.M.Color = Paint.Mix(belly, back, 0.55f);
            var j = k.Bind[QB.Jaw];
            k.M.Push().Translate(j).Scale(new Vector3(1f, 0.45f, 1f)).Translate(-j);
            k.M.Segment(j + new Vector3(0f, 0f, -r * 0.2f), j + new Vector3(0f, -r * 0.05f, r * 2.55f), r * 0.6f, r * 0.36f, 6);
            k.M.Pop();
            k.M.Color = teeth;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 4; i++)
                    k.M.Spike(j + new Vector3(s * r * 0.38f, r * 0.05f, r * (0.6f + i * 0.5f)), Vector3.up, r * 0.045f, r * 0.15f, 3);
            k.Model.HeadBone = QB.Head;
            k.Model.HeadTop = lp + new Vector3(0f, r * 0.6f, 0f) - k.HeadBase;
            k.Model.CastBone = QB.Head;
            k.Legs(back, legs, legs, 1.1f, false, 1.15f);
            k.M.Color = scute;
            int[] feet = { QB.FLF, QB.FRF, QB.BLF, QB.BRF };
            foreach (int f in feet)
            {
                k.M.Bone = f;
                var a = k.Bind[f];
                for (int c = -1; c <= 1; c++)
                    k.M.Spike(new Vector3(a.x + c * 0.035f, 0.025f, a.z + 0.07f), new Vector3(c * 0.4f, -0.2f, 1f), 0.014f, 0.06f, 3);
            }
            k.LongTail(back, 1.35f, 0.2f, 1.45f, 0.95f, scute, 0.06f, belly);
            k.Finish(key, 0.72f, UnitStrike.Bite, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = s1 - k.HeadBase;
            m.Heavy = 0.3f; m.MaxCadence = 2.4f; m.TurnRate = 300f;
            m.DustColor = new Color(0.55f, 0.6f, 0.45f, 0.3f);
            return Bake(k);
        }

        /// <summary>
        /// Mossdeep giant spider: twice the old spider, teal-black with moss on its back, glowing sea-green
        /// bioluminescent spots on the abdomen, bright banded legs with bristles at the knees and big pale fangs.
        /// </summary>
        static UnitModel GiantSpider(string key)
        {
            var k = new SpiderKit(87, 0.52f, 0.44f, 1.8f);
            Color body = C("#2c3a3c"), abdomen = C("#344a4a"), legs = C("#4c5e58"), band = C("#9ad4b4"), glow = C("#7affd0"), moss = C("#6f9248");
            k.Build(body, abdomen, C("#5aa890"), legs, band, glow, moss);
            var M = k.M;
            float R = k.BodyR;
            var ab = k.Bind[SB.Abdomen] + new Vector3(0f, R * 0.25f, -R * 0.75f);
            M.Bone = SB.Abdomen;
            M.Emission = 1f; M.Color = glow;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 3; i++)
                    M.Sphere(ab + new Vector3(s * R * (0.55f - i * 0.08f), R * (0.55f - i * 0.12f), R * (0.45f - i * 0.5f)), new Vector3(R * 0.14f, R * 0.08f, R * 0.14f), 5, 3);
            M.Box(ab + new Vector3(0f, R * 0.9f, -R * 0.75f), new Vector3(R * 0.12f, R * 0.06f, R * 0.4f));
            M.Emission = 0f;
            M.Color = moss;
            M.Blob(ab + new Vector3(-R * 0.3f, R * 0.85f, R * 0.3f), new Vector3(0.4f, 0.16f, 0.45f) * R, 1, 0.3f, 7);
            M.Color = C("#e8dcc0");
            M.Bone = SB.Fangs;
            for (int s = -1; s <= 1; s += 2)
                M.Spike(k.Bind[SB.Fangs] + new Vector3(s * R * 0.16f, -R * 0.1f, R * 0.12f), new Vector3(s * 0.15f, -1f, 0.35f), R * 0.06f, R * 0.4f, 4);
            // bristles at the knees
            M.Color = Paint.Shade(legs, 0.6f);
            for (int i = 0; i < 8; i++)
            {
                M.Bone = SB.Lower(i);
                var kn = k.Bind[SB.Lower(i)];
                M.Spike(kn, new Vector3(kn.x, 0.3f, kn.z).normalized + Vector3.up, R * 0.05f, R * 0.3f, 3);
            }
            k.Finish(key, 1.25f);
            k.Model.DustColor = new Color(0.5f, 0.58f, 0.5f, 0.25f);
            k.Model.MaxCadence = 3.4f;
            return UnitModels.Bake(k.Model, k.M);
        }

        // ================================================================== hawk, harpy (fliers on the biped rig)

        /// <summary>
        /// Amberfield hawk: a big red-tailed bird of prey soaring at head height, a chestnut back, a barred cream chest, a
        /// yellow cere over a dark hooked beak, a fierce dark brow stripe, long fingered wings and a rufous fan tail.
        /// </summary>
        static UnitModel Hawk(string key)
        {
            var k = new BipedKit(88, 0.85f, 0.13f, 0.2f, 1f, false, 0.9f, 0.6f);
            Color back = C("#8c5a36"), backD = C("#5a3822"), cream = C("#f4e6c8"), bar = C("#a8784e"), rufous = C("#cc6a36"),
                  beak = C("#3a3a42"), cere = C("#f2c440"), eye = C("#f6d040"), feet = C("#f2c440");
            var M = k.M;
            M.Jitter = 0.05f;
            var bodyC = new Vector3(0f, 0.36f, 0f);
            // a sleek body tipped forward
            M.Bone = BB.Hips; M.Color = back;
            M.Push().Translate(bodyC).Rotate(28f, 0f, 0f);
            M.Sphere(Vector3.zero, new Vector3(0.15f, 0.24f, 0.15f), 10, 7);
            M.Pop();
            M.Bone = BB.Chest; M.Color = cream;
            M.Push().Translate(bodyC + new Vector3(0f, 0.01f, 0.06f)).Rotate(28f, 0f, 0f);
            M.Sphere(Vector3.zero, new Vector3(0.12f, 0.19f, 0.11f), 9, 6);
            M.Color = bar;
            for (int i = 0; i < 3; i++)
                for (int s = -1; s <= 1; s += 2)
                    M.Box(new Vector3(s * 0.04f, 0.06f - i * 0.07f, 0.105f), new Vector3(0.05f, 0.012f, 0.012f));
            M.Pop();
            // head: smaller and flatter than the owl's, a hooked beak, a fierce brow
            M.Bone = BB.Head; M.Color = back;
            var hc = new Vector3(0f, k.HeadCY - 0.03f, 0.08f);
            M.Sphere(hc, new Vector3(k.R * 0.95f, k.R * 0.85f, k.R * 1.05f), 10, 7);
            M.Color = cream;
            M.Sphere(hc + new Vector3(0f, -k.R * 0.3f, k.R * 0.25f), new Vector3(k.R * 0.75f, k.R * 0.5f, k.R * 0.7f), 8, 5);
            M.Color = cere;
            M.Sphere(hc + new Vector3(0f, -k.R * 0.02f, k.R * 0.9f), new Vector3(k.R * 0.32f, k.R * 0.26f, k.R * 0.24f), 6, 4);
            M.Color = beak;
            M.Curve(hc + new Vector3(0f, -k.R * 0.02f, k.R * 1.0f), hc + new Vector3(0f, k.R * 0.05f, k.R * 1.45f), hc + new Vector3(0f, -k.R * 0.42f, k.R * 1.42f), k.R * 0.2f, k.R * 0.03f, 3, 5);
            M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = hc + new Vector3(s * k.R * 0.55f, k.R * 0.12f, k.R * 0.6f);
                M.Push().Translate(e).Rotate(0f, s * 40f, 0f);
                M.Color = eye; M.Emission = 0.3f;
                M.Sphere(Vector3.zero, new Vector3(0.2f, 0.2f, 0.1f) * k.R, 7, 4);
                M.Emission = 0f; M.Color = C("#141010");
                M.Sphere(new Vector3(0f, 0f, 0.06f * k.R), new Vector3(0.11f, 0.11f, 0.06f) * k.R, 6, 3);
                M.Emission = 0.6f; M.Color = Color.white;
                M.Sphere(new Vector3(s * 0.04f * k.R, 0.06f * k.R, 0.09f * k.R), 0.035f * k.R, 4, 2);
                M.Emission = 0f;
                M.Pop();
                // the brow stripe: a dark wedge over the eye towards the nape
                M.Color = backD;
                M.Push().Translate(e + new Vector3(-s * k.R * 0.05f, k.R * 0.2f, -k.R * 0.15f)).Rotate(-12f, s * 35f, s * -14f);
                M.Box(Vector3.zero, new Vector3(k.R * 0.22f, k.R * 0.1f, k.R * 0.6f));
                M.Pop();
            }
            M.Jitter = 0.05f;
            // wings: long and fingered
            for (int s = -1; s <= 1; s += 2)
            {
                int wb = s < 0 ? BB.WingL : BB.WingR;
                k.Bind[wb] = bodyC + new Vector3(s * 0.12f, 0.12f, 0.02f);
                var w0 = k.Bind[wb];
                M.Bone = wb; M.Color = back;
                var wrist = w0 + new Vector3(s * 0.4f, 0.06f, -0.03f);
                M.Segment(w0, wrist, 0.04f, 0.025f, 6);
                M.Sphere(wrist, 0.028f, 6, 4);
                M.Push().Translate(w0 + new Vector3(0f, 0f, -0.03f)).Rotate(0f, s * 8f, 0f);
                M.Flat(new[]
                {
                    new Vector2(0f, 0.02f), new Vector2(s * 0.22f, 0.07f), new Vector2(s * 0.42f, 0.05f), new Vector2(s * 0.46f, -0.05f),
                    new Vector2(s * 0.3f, -0.16f), new Vector2(s * 0.05f, -0.2f),
                }, 0.035f);
                M.Color = cream;
                M.Push().Translate(0f, 0f, -0.02f);
                M.Flat(new[] { new Vector2(s * 0.03f, -0.01f), new Vector2(s * 0.32f, 0.02f), new Vector2(s * 0.3f, -0.08f), new Vector2(s * 0.06f, -0.12f) }, 0.012f);
                M.Pop();
                M.Pop();
                M.Color = backD;
                for (int f = 0; f < 5; f++)
                {
                    var b = wrist + new Vector3(s * (0.02f - f * 0.035f), -0.01f - f * 0.03f, -0.02f);
                    M.Strip(b, b + new Vector3(s * (0.2f - f * 0.025f), -0.03f - f * 0.035f, -0.03f), 0.05f, 0.025f, Vector3.forward, 0.014f);
                }
            }
            // a rufous fan tail with a dark band, yellow feet with dark talons
            k.Bind[BB.Tail] = bodyC + new Vector3(0f, -0.14f, -0.16f);
            M.Bone = BB.Tail;
            for (int i = -2; i <= 2; i++)
            {
                var a = k.Bind[BB.Tail];
                var b = a + new Vector3(i * 0.045f, -0.1f, -0.2f);
                M.Color = rufous; M.Strip(a, b, 0.05f, 0.06f, Vector3.up, 0.014f);
                M.Color = backD; M.Strip(b, b + (b - a).normalized * 0.02f, 0.06f, 0.06f, Vector3.up, 0.016f);
            }
            M.Bone = BB.Hips;
            for (int s = -1; s <= 1; s += 2)
            {
                var f0 = bodyC + new Vector3(s * 0.06f, -0.2f, 0.02f);
                M.Color = feet;
                M.Segment(f0 + Vector3.up * 0.05f, f0, 0.02f, 0.016f, 5);
                M.Color = C("#2a2420");
                for (int c = -1; c <= 1; c++)
                    M.Spike(f0, new Vector3(c * 0.4f, -0.5f, 1f), 0.01f, 0.05f, 3);
            }
            var m = k.Model;
            m.FloatHeight = 1.05f;
            m.Wings = true; m.WingsFlap = true;
            m.Strike = UnitStrike.Claw; m.Ranged = UnitRanged.Point;
            m.Dust = false;
            k.Finish(key, UnitGait.Flier);
            m.Legs = new UnitLeg[0];
            m.Height = 0.85f;
            m.Radius = 0.32f;
            m.HipY = k.Bind[BB.Hips].y;
            m.CenterBone = BB.Hips; m.CenterOffset = bodyC - k.Bind[BB.Hips];
            m.HeadTop = hc + new Vector3(0f, k.R * 1.1f, 0f) - k.Bind[BB.Head];
            m.CastBone = BB.Head; m.CastOffset = hc + new Vector3(0f, 0f, k.R * 1.4f) - k.Bind[BB.Head];
            m.PickBones = new[] { BB.Hips, BB.Head, BB.WingL, BB.WingR };
            m.PickPad = 0.2f;
            return UnitModels.Bake(m, k.M);
        }

        /// <summary>
        /// Skyreach harpy: a hovering bird-woman — storm-blue plumage over a feathered vest and a short feather skirt, big
        /// feathered wings on her back, a wild coral feather crest, white war paint, taloned hands and dangling bird legs
        /// with dark talons, a fan of tail feathers.
        /// </summary>
        static UnitModel Harpy(string key)
        {
            var k = new BipedKit(89, 1.7f, 0.14f, 0.5f, 0.85f, true);
            Color skin = C("#ecc8a6"), plume = C("#5a6c9c"), plumeL = C("#a9b8dc"), plumeD = C("#3a466e"), crest = C("#f27a4a"),
                  crestL = C("#ffc070"), paint = C("#f8f4ec"), talon = C("#2a2430"), shin = C("#d8b860"), iris = C("#f0b030");
            k.Skin = skin;
            var M = k.M;
            k.Torso(plume, plumeD, skin, 0f);
            k.ChestPanel(plumeL, 0.16f, true, crest);
            k.Neck(skin);
            k.Head(skin, iris, crest, EyeStyle.Narrow, 14f, false);
            k.Cheeks(paint, 0.8f);
            k.HairCap(crest);
            k.Spikes(crest, 11, 0.85f, 0.8f, 0.9f, 7, 0.24f);
            M.Bone = BB.Head; M.Color = crestL;
            for (int i = -1; i <= 1; i++)
                M.Spike(new Vector3(i * 0.05f * k.U, k.HeadCY + 0.75f * k.R, -0.2f * k.R), new Vector3(i * 0.4f, 1f, -0.6f), 0.12f * k.R, 0.9f * k.R, 4);
            // war paint: a white stripe across the eyes
            M.Color = paint;
            M.Box(new Vector3(0f, k.HeadCY + 0.08f * k.R, 0.93f * k.R), new Vector3(0.85f * k.R, 0.08f * k.R, 0.05f * k.R));
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, plume, skin);
                k.Cuff(s, plumeL, 0.2f, 0.75f, 1.45f);
                M.Bone = BB.Hand(s); M.Color = talon;
                var h = k.Bind[BB.Hand(s)];
                for (int c = -1; c <= 1; c++)
                    M.Spike(h + new Vector3(s * 0.005f + c * 0.018f, -0.09f, 0.03f), new Vector3(c * 0.2f, -1f, 0.5f), 0.012f, 0.07f, 3);
                // feathered forearm fringe
                M.Bone = BB.ArmL(s); M.Color = plumeL;
                for (int f = 0; f < 3; f++)
                    M.Strip(new Vector3(s * (k.ShoulderX + 0.02f), k.ElbowY - f * 0.06f, -0.02f), new Vector3(s * (k.ShoulderX + 0.09f), k.ElbowY - f * 0.06f - 0.1f, -0.06f), 0.05f, 0.02f, Vector3.right * s, 0.012f);
            }
            k.Skirt(plume, plumeD, k.KneeY + 0.06f, 1.5f, 0f, plumeL);
            // dangling bird legs: scaly shins and talons (she hovers; no leg IK)
            for (int s = -1; s <= 1; s += 2)
            {
                M.Bone = BB.LegU(s); M.Color = plumeD;
                M.Segment(new Vector3(s * k.HipX, k.HipY, 0f), new Vector3(s * k.HipX, k.KneeY, 0.02f), k.ThighR, k.ThighR * 0.7f, 6);
                M.Bone = BB.LegL(s); M.Color = shin;
                M.Segment(new Vector3(s * k.HipX, k.KneeY, 0.02f), new Vector3(s * k.HipX, k.AnkleY + 0.02f, -0.02f), k.ShinR * 0.7f, k.ShinR * 0.55f, 5);
                M.Bone = BB.Foot(s); M.Color = talon;
                var a = new Vector3(s * k.HipX, k.AnkleY, -0.01f);
                for (int c = -1; c <= 1; c++)
                    M.Curve(a, a + new Vector3(c * 0.04f, -0.03f, 0.06f), a + new Vector3(c * 0.06f, -0.09f, 0.08f), 0.018f, 0.006f, 2, 4);
                M.Spike(a, new Vector3(0f, -0.6f, -1f), 0.014f, 0.07f, 3);
            }
            // feathered wings on the back, held tilted back so their broad faces show from above
            for (int s = -1; s <= 1; s += 2)
            {
                int wb = s < 0 ? BB.WingL : BB.WingR;
                M.Bone = wb;
                M.Push().Translate(k.Bind[wb] + new Vector3(0f, 0f, -0.03f)).Rotate(-50f, s * 12f, 0f);
                M.Color = plume;
                var wrist = new Vector3(s * 0.46f, 0.34f, 0f);
                M.Segment(Vector3.zero, wrist, 0.04f, 0.028f, 6);
                M.Flat(new[]
                {
                    new Vector2(0f, 0.05f), new Vector2(s * 0.28f, 0.3f), new Vector2(s * 0.5f, 0.42f), new Vector2(s * 0.66f, 0.28f),
                    new Vector2(s * 0.62f, -0.06f), new Vector2(s * 0.32f, -0.3f), new Vector2(s * 0.05f, -0.2f),
                }, 0.04f);
                M.Color = plumeL;
                M.Push().Translate(0f, 0f, 0.026f);
                M.Flat(new[] { new Vector2(s * 0.04f, 0.04f), new Vector2(s * 0.3f, 0.27f), new Vector2(s * 0.46f, 0.3f), new Vector2(s * 0.42f, 0.08f), new Vector2(s * 0.1f, -0.1f) }, 0.012f);
                M.Pop();
                // primaries and secondaries overlapping along the trailing edge, darker, the outer ones longest
                for (int f = 0; f < 6; f++)
                {
                    float t = f / 5f;
                    M.Color = f % 2 == 0 ? plumeD : plume;
                    var b0 = new Vector3(s * Mathf.Lerp(0.6f, 0.12f, t), Mathf.Lerp(0.3f, -0.22f, t), 0f);
                    var b1 = b0 + new Vector3(s * Mathf.Lerp(0.34f, 0.08f, t), Mathf.Lerp(0.02f, -0.3f, t) - 0.04f, 0f);
                    M.Strip(b0, b1, 0.12f, 0.07f, Vector3.forward, 0.018f);
                }
                M.Color = crest;
                M.Strip(new Vector3(s * 0.62f, 0.34f, 0.01f), new Vector3(s * 0.95f, 0.36f, 0.01f), 0.08f, 0.03f, Vector3.forward, 0.016f);
                M.Pop();
            }
            // tail feathers
            M.Bone = BB.Tail;
            var t0 = k.Bind[BB.Tail];
            for (int i = -2; i <= 2; i++)
            {
                M.Color = i == 0 ? crest : (Mathf.Abs(i) == 1 ? plume : plumeD);
                M.Strip(t0, t0 + new Vector3(i * 0.08f, -0.35f, -0.22f), 0.05f, 0.07f, Vector3.back, 0.014f);
            }
            var m = k.Model;
            m.HoldR = UnitHold.Claws; m.HoldL = UnitHold.Claws;
            m.Strike = UnitStrike.Claw; m.Ranged = UnitRanged.Point;
            m.Wings = true; m.WingsFlap = true;
            m.FloatHeight = 0.4f;
            m.Dust = false;
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.HeadTop = new Vector3(0f, k.H - k.HeadY + 0.12f * k.U, 0f);
            return UnitModels.Bake(m, k.M);
        }

        // ================================================================== yetis

        /// <summary>
        /// Yeti (Skyreach) and Frostclaw, the yeti matriarch (raid boss 1 of Ashwyrm's Roost): a huge shaggy white ape
        /// with a blue-skinned face, curled ram horns, small tusks, long arms with big clawed hands and shaggy fringes. The
        /// matriarch is broader, frost-blue at the tips, wears an ice-crystal crown, a hide cloak, bead braids and a
        /// necklace of ice charms, and has glowing ice eyes and icicle claws.
        /// </summary>
        static UnitModel Yeti(string key, bool matriarch)
        {
            float H = matriarch ? 2.8f : 2.4f;
            var k = new BipedKit(matriarch ? 97 : 96, H, matriarch ? 0.25f : 0.235f, 0.38f, matriarch ? 2.1f : 1.9f, false, 1.25f, 1.42f);
            float U = k.U;
            Color fur = C("#eef1f5"), furS = C("#cbd5e2"), tip = matriarch ? C("#9cc4e6") : C("#b9c8da"), skin = C("#7f96b8"),
                  skinD = C("#58698c"), horn = C("#e4d4b2"), claw = matriarch ? C("#c8f0ff") : C("#2e3240"), ice = C("#bfeeff");
            var M = k.M;
            M.Jitter = 0.07f;
            k.Torso(fur, furS, fur, 0.28f);
            // shaggy body: lumps over the chest and belly, a big ruff
            M.Bone = BB.Chest;
            for (int i = 0; i < 5; i++)
            {
                float a = (i - 2) * 0.55f;
                M.Color = i % 2 == 0 ? fur : furS;
                M.Blob(new Vector3(Mathf.Sin(a) * k.ChestR * 0.85f, k.ChestY - 0.05f * U - Mathf.Abs(i - 2) * 0.03f * U, Mathf.Cos(a) * k.ChestR * k.DepthK * 0.85f),
                       new Vector3(0.14f, 0.16f, 0.1f) * U, 0, 0.25f, 60 + i);
            }
            k.Mantle(matriarch ? tip : furS, matriarch ? 2.1f : 1.8f, 0.03f, 64);
            k.Neck(fur);
            // head: a fur dome with a blue face, a heavy brow, small eyes, flat nose, tusks, curled horns
            float r = k.R;
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = fur;
            M.Blob(hc + new Vector3(0f, 0.1f * r, -0.1f * r), new Vector3(1.15f, 1.1f, 1.1f) * r, 1, 0.12f, 66);
            M.Color = skin;
            M.Sphere(hc + new Vector3(0f, -0.15f * r, 0.62f * r), new Vector3(0.78f * r, 0.72f * r, 0.5f * r), 9, 6);
            M.Color = furS;
            M.Push().Translate(hc + new Vector3(0f, 0.28f * r, 0.85f * r)).Rotate(-10f, 0f, 0f);
            M.Box(Vector3.zero, new Vector3(1.2f * r, 0.22f * r, 0.32f * r));
            M.Pop();
            M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = hc + new Vector3(s * 0.3f * r, 0.08f * r, 1.04f * r);
                if (matriarch)
                {
                    M.Emission = 1f; M.Color = ice;
                    M.Sphere(e, new Vector3(0.15f, 0.08f, 0.06f) * r, 6, 3);
                    M.Emission = 0f;
                }
                else
                {
                    M.Color = C("#1a1c28"); M.Sphere(e, new Vector3(0.12f, 0.12f, 0.06f) * r, 6, 3);
                    M.Emission = 0.6f; M.Color = Color.white;
                    M.Sphere(e + new Vector3(s * 0.03f * r, 0.04f * r, 0.04f * r), 0.035f * r, 4, 2);
                    M.Emission = 0f;
                }
            }
            M.Color = skinD;
            M.Sphere(hc + new Vector3(0f, -0.1f * r, 1.08f * r), new Vector3(0.24f, 0.16f, 0.14f) * r, 6, 4);
            M.Box(hc + new Vector3(0f, -0.48f * r, 1.02f * r), new Vector3(0.5f * r, 0.08f * r, 0.08f * r));
            M.Color = C("#f6f2e6");
            for (int s = -1; s <= 1; s += 2)
                M.Spike(hc + new Vector3(s * 0.22f * r, -0.5f * r, 1.04f * r), new Vector3(s * 0.15f, 1f, 0.2f), 0.06f * r, 0.26f * r, 4);
            M.Jitter = 0.07f;
            k.Horns(horn, matriarch ? 0.95f : 0.8f, 0.5f, 0.4f, 0.17f, true);
            if (matriarch)
            {
                // the ice crown, bead braids, a necklace of ice charms
                M.Bone = BB.Head; M.Emission = 0.4f;
                for (int i = 0; i < 5; i++)
                {
                    float a = (i - 2) * 0.42f;
                    M.Color = i % 2 == 0 ? ice : C("#86c8f0");
                    M.Spike(hc + new Vector3(Mathf.Sin(a) * 0.62f * r, 0.78f * r, Mathf.Cos(a) * 0.35f * r - 0.1f * r), new Vector3(Mathf.Sin(a) * 0.4f, 1f, 0.1f), 0.12f * r, (i == 2 ? 1.0f : 0.65f) * r, 4);
                }
                M.Emission = 0f;
                for (int s = -1; s <= 1; s += 2)
                    k.Braid(tip, hc + new Vector3(s * 0.95f * r, 0.0f, 0.2f * r), hc + new Vector3(s * 1.05f * r, -1.2f * r, 0.35f * r), 4, 0.2f, C("#e2b45a"));
                M.Bone = BB.Chest;
                for (int i = 0; i < 7; i++)
                {
                    float a = (i - 3) * 0.32f;
                    var p = new Vector3(Mathf.Sin(a) * k.ChestR * 0.8f, k.ShoulderY - 0.12f * U - Mathf.Cos(a) * 0.05f * U, Mathf.Cos(a) * k.ChestR * k.DepthK * 1.15f);
                    M.Color = i % 2 == 0 ? C("#e2b45a") : ice;
                    M.Emission = i % 2 == 0 ? 0f : 0.5f;
                    if (i % 2 == 0) M.Sphere(p, 0.035f * U, 5, 3);
                    else M.Spike(p + Vector3.up * 0.02f * U, Vector3.down, 0.03f * U, 0.12f * U, 4);
                }
                M.Emission = 0f;
            }
            // long shaggy arms, big skin hands with claws
            for (int s = -1; s <= 1; s += 2)
            {
                M.Bone = BB.ArmU(s); M.Color = fur;
                M.Blob(k.Bind[BB.ArmU(s)] + new Vector3(s * 0.04f, 0.02f, 0f) * U, new Vector3(0.17f, 0.15f, 0.17f) * U, 0, 0.2f, 70 + s);
                M.Segment(k.Bind[BB.ArmU(s)], k.Bind[BB.ArmL(s)], 0.12f * U, 0.1f * U, 7);
                M.Bone = BB.ArmL(s); M.Color = furS;
                M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.11f * U, 0.12f * U, 7);
                M.Color = matriarch ? tip : fur;
                for (int f = 0; f < 4; f++)
                {
                    var p = Vector3.Lerp(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.25f + f * 0.2f);
                    M.Spike(p + new Vector3(s * 0.09f * U, 0f, 0f), new Vector3(s * 1f, -0.7f, (f % 2 == 0 ? -0.3f : 0.3f)), 0.04f * U, 0.12f * U, 4);
                }
                M.Bone = BB.Hand(s); M.Color = skin;
                var h = k.Bind[BB.Hand(s)];
                M.Blob(h + Vector3.down * 0.08f * U, new Vector3(0.11f, 0.12f, 0.12f) * U, 0, 0.15f, 74 + s);
                M.Color = claw; M.Emission = matriarch ? 0.35f : 0f;
                for (int c = -1; c <= 1; c++)
                    M.Spike(h + new Vector3(c * 0.05f * U, -0.17f * U, 0.05f * U), new Vector3(c * 0.2f, -1f, 0.6f), 0.022f * U, 0.1f * U, 4);
                M.Emission = 0f;
                k.Leg(s, fur, furS, skin, 0f);
                k.LegCuff(s, matriarch ? tip : furS, 0.1f, 1.55f, true);
                k.LegCuff(s, fur, 0.35f, 1.35f, false);
            }
            var m = k.Model;
            m.Strike = UnitStrike.Slam; m.Ranged = UnitRanged.Throw;
            m.Heavy = matriarch ? 1f : 0.8f; m.MaxCadence = matriarch ? 1.2f : 1.6f; m.TurnRate = matriarch ? 180f : 300f;
            m.DustColor = new Color(0.94f, 0.97f, 1f, 0.45f);
            m.CastBone = BB.HandR; m.CastOffset = new Vector3(0f, -0.15f * U, 0.05f * U);
            var res = Done(k, key, UnitGait.Heavy);
            res.HeadTop = new Vector3(0f, k.H - k.HeadY + (matriarch ? 0.6f * r : 0.2f * r), 0f);
            return res;
        }

        // ================================================================== root creatures: rootling, rootwarden, Thornmaw

        /// <summary>
        /// Rootling (the Root Hollows under Old Kusu): a little walking root tuber — a ginseng-tan bulb body with root
        /// lines, glowing amber eyes in a dark face patch, a sprout of three leaves with a softly glowing bud on top, thin
        /// forked root arms and root legs, wispy root hairs down its back.
        /// </summary>
        static UnitModel Rootling(string key)
        {
            const float H = 0.8f;
            var k = new BipedKit(92, H, 0.2f, 0.26f, 1.3f, false, 1.0f, 1.25f);
            Color root = C("#c89058"), rootD = C("#94643a"), rootL = C("#e2b884"), face = C("#4a3020"), eye = C("#ffc860"),
                  leaf = C("#78b456"), leafD = C("#4f8a3c"), bud = C("#fff0a0");
            float r = k.R;
            var M = k.M;
            M.Jitter = 0.07f;
            M.Bone = BB.Hips; M.Color = rootD;
            M.Sphere(new Vector3(0f, k.PelvisY, 0f), new Vector3(0.15f, 0.12f, 0.14f), 7, 5);
            // the bulb: a fat tuber narrowing to a point below, root lines
            M.Bone = BB.Head; M.Color = root;
            var hc = new Vector3(0f, k.HeadCY - 0.2f * r, 0f);
            M.Lathe(new[] { new Vector2(0.02f, hc.y - 1.35f * r), new Vector2(0.6f * r, hc.y - 1.0f * r), new Vector2(1.05f * r, hc.y - 0.3f * r),
                            new Vector2(1.0f * r, hc.y + 0.4f * r), new Vector2(0.6f * r, hc.y + 0.9f * r), new Vector2(0.12f * r, hc.y + 1.05f * r) }, 9, true);
            M.Color = rootD;
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 95f + 20f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a) * 1.0f * r, hc.y - 0.2f * r + (i % 2) * 0.3f * r, Mathf.Cos(a) * 1.0f * r);
                M.Push().Translate(p).Rotate(0f, a * Mathf.Rad2Deg, 75f + i * 10f);
                M.Box(Vector3.zero, new Vector3(0.05f * r, 0.5f * r, 0.04f * r));
                M.Pop();
            }
            // face: a dark hollow with glowing eyes and a small o mouth
            M.Color = face;
            M.Sphere(hc + new Vector3(0f, 0.05f * r, 0.82f * r), new Vector3(0.62f * r, 0.42f * r, 0.25f * r), 8, 4);
            M.Jitter = 0f;
            M.Emission = 1f; M.Color = eye;
            for (int s = -1; s <= 1; s += 2)
                M.Sphere(hc + new Vector3(s * 0.26f * r, 0.12f * r, 1.0f * r), new Vector3(0.14f * r, 0.17f * r, 0.08f * r), 6, 4);
            M.Emission = 0.4f;
            M.Sphere(hc + new Vector3(0f, -0.17f * r, 1.0f * r), new Vector3(0.07f * r, 0.06f * r, 0.05f * r), 5, 3);
            M.Emission = 0f;
            M.Jitter = 0.07f;
            // the sprout: three leaves and a glowing bud
            var top = hc + new Vector3(0f, 1.0f * r, 0f);
            M.Color = leafD;
            M.Segment(top, top + new Vector3(0f, 0.35f * r, 0.02f * r), 0.05f * r, 0.035f * r, 5);
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                M.Color = i == 1 ? leafD : leaf;
                var b = top + new Vector3(0f, 0.3f * r, 0f);
                M.Strip(b, b + new Vector3(Mathf.Sin(a) * 0.75f * r, 0.35f * r, Mathf.Cos(a) * 0.75f * r), 0.32f * r, 0.12f * r, Vector3.up, 0.02f);
            }
            M.Emission = 0.9f; M.Color = bud;
            M.Sphere(top + new Vector3(0f, 0.48f * r, 0f), new Vector3(0.16f, 0.2f, 0.16f) * r, 6, 4);
            M.Emission = 0f;
            // root hairs down the back
            M.Bone = BB.HairB; M.Color = rootD;
            for (int i = -2; i <= 2; i++)
            {
                var a = hc + new Vector3(i * 0.22f * r, 0.4f * r, -0.85f * r);
                M.Curve(a, a + new Vector3(i * 0.08f * r, -0.3f * r, -0.35f * r), a + new Vector3(i * 0.15f * r, -0.95f * r, -0.4f * r), 0.05f * r, 0.012f * r, 3, 4);
            }
            // arms and legs: thin roots with forked ends
            for (int s = -1; s <= 1; s += 2)
            {
                M.Bone = BB.ArmU(s); M.Color = rootD;
                M.Segment(k.Bind[BB.ArmU(s)], k.Bind[BB.ArmL(s)], 0.03f, 0.025f, 5);
                M.Bone = BB.ArmL(s);
                M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.025f, 0.022f, 5);
                M.Bone = BB.Hand(s); M.Color = root;
                var h = k.Bind[BB.Hand(s)];
                for (int c = -1; c <= 1; c++)
                    M.Curve(h, h + new Vector3(c * 0.02f, -0.03f, 0.02f), h + new Vector3(c * 0.04f + s * 0.01f, -0.08f, 0.03f), 0.016f, 0.005f, 2, 4);
                M.Bone = BB.LegU(s); M.Color = rootD;
                M.Segment(k.Bind[BB.LegU(s)] + Vector3.up * 0.02f, k.Bind[BB.LegL(s)], 0.04f, 0.032f, 5);
                M.Bone = BB.LegL(s);
                M.Segment(k.Bind[BB.LegL(s)], k.Bind[BB.Foot(s)], 0.032f, 0.028f, 5);
                M.Bone = BB.Foot(s); M.Color = rootL;
                var f = k.Bind[BB.Foot(s)];
                for (int c = -1; c <= 1; c++)
                    M.Curve(f, f + new Vector3(c * 0.03f, -0.03f, 0.03f), new Vector3(f.x + c * 0.05f, 0.01f, f.z + 0.07f), 0.022f, 0.008f, 2, 4);
            }
            var m = k.Model;
            m.Strike = UnitStrike.Headbutt; m.Ranged = UnitRanged.Throw;
            m.DustColor = new Color(0.7f, 0.6f, 0.45f, 0.3f);
            m.CastBone = BB.Head; m.CastOffset = top + new Vector3(0f, 0.48f * r, 0f) - k.Bind[BB.Head];
            var res = Done(k, key, UnitGait.Small);
            res.HeadTop = top + new Vector3(0f, 0.75f * r, 0f) - k.Bind[BB.Head];
            res.Radius = 0.26f;
            return res;
        }

        /// <summary>
        /// The Rootwarden (boss of the Root Hollows): a hunched guardian of gnarled root wood, 3 m, with a cage of root ribs
        /// round a honey-gold spirit lantern in its chest, a pale shrine mask with vermilion marks and glowing eye slits, a
        /// crown of root antlers hung with two paper lanterns, mossy burls with mushrooms on the shoulders, long root arms
        /// ending in clawed root fingers and stumpy legs splaying into roots.
        /// </summary>
        static UnitModel Rootwarden(string key)
        {
            var k = new BipedKit(93, 3.0f, 0.19f, 0.36f, 1.75f, false, 1.3f, 1.5f);
            float U = k.U, r = k.R;
            Color bark = C("#6e5038"), barkD = C("#4a3426"), barkL = C("#94704e"), moss = C("#6f9a4a"), mossL = C("#9ab868"),
                  mask = C("#ece4d2"), red = C("#d0402e"), glow = C("#ffc860"), paper = C("#f4e6c8"), cap = C("#e8b860");
            var M = k.M;
            M.Jitter = 0.08f;
            k.Torso(bark, barkD, barkD, 0.1f);
            // the lantern heart and the cage of root ribs
            M.Bone = BB.Chest;
            var core = new Vector3(0f, k.ChestY, k.ChestR * k.DepthK * 0.7f);
            M.Emission = 1f; M.Color = glow;
            M.Sphere(core, new Vector3(0.13f, 0.16f, 0.1f) * U, 8, 6);
            M.Emission = 0f;
            M.Color = barkL;
            for (int i = 0; i < 4; i++)
            {
                float x = (i - 1.5f) * 0.07f * U;
                var a = new Vector3(x * 1.6f, k.ShoulderY - 0.04f * U, k.ChestR * k.DepthK * 0.8f);
                var b = new Vector3(x * 1.2f, k.SpineY + 0.02f * U, k.WaistR * k.DepthK * 0.9f);
                M.Curve(a, new Vector3(x * 2.2f, k.ChestY, core.z + 0.16f * U), b, 0.035f * U, 0.03f * U, 3, 5);
            }
            // root antler crown, mask, moss
            var hc = new Vector3(0f, k.HeadCY, 0f);
            M.Bone = BB.Head; M.Color = bark;
            M.Blob(hc, new Vector3(1.05f, 1.0f, 1.0f) * r, 1, 0.15f, 80);
            // a flat shrine mask, tilted up to the camera: vermilion brow and cheek marks, glowing eye slits
            var mc = hc + new Vector3(0f, 0.05f * r, 0.92f * r);
            M.Push().Translate(mc).Rotate(-18f, 0f, 0f);
            M.Color = mask;
            M.Sphere(Vector3.zero, new Vector3(0.98f * r, 1.12f * r, 0.2f * r), 10, 6);
            M.Color = red;
            M.Box(new Vector3(0f, 0.62f * r, 0.17f * r), new Vector3(0.14f * r, 0.4f * r, 0.05f * r));
            for (int s = -1; s <= 1; s += 2)
            {
                M.Push().Translate(s * 0.48f * r, -0.42f * r, 0.15f * r).Rotate(0f, 0f, s * 28f);
                M.Box(Vector3.zero, new Vector3(0.42f * r, 0.1f * r, 0.05f * r));
                M.Pop();
                M.Emission = 1f; M.Color = glow;
                M.Push().Translate(s * 0.38f * r, 0.16f * r, 0.17f * r).Rotate(0f, 0f, s * -14f);
                M.Box(Vector3.zero, new Vector3(0.4f * r, 0.11f * r, 0.05f * r));
                M.Pop();
                M.Emission = 0f; M.Color = red;
            }
            M.Pop();
            M.Color = barkL;
            var hangs = new Vector3[2];
            for (int s = -1; s <= 1; s += 2)
            {
                var a0 = hc + new Vector3(s * 0.55f * r, 0.75f * r, -0.2f * r);
                var a1 = a0 + new Vector3(s * 0.75f * r, 1.3f * r, -0.3f * r);
                var a2 = a1 + new Vector3(s * 0.65f * r, 0.6f * r, 0.2f * r);
                M.Curve(a0, a0 + new Vector3(s * 0.1f * r, 0.8f * r, 0f), a1, 0.16f * r, 0.1f * r, 3, 5);
                M.Curve(a1, a1 + new Vector3(s * 0.35f * r, 0.05f * r, 0f), a2, 0.1f * r, 0.03f * r, 3, 5);
                M.Spike(a1, new Vector3(-s * 0.2f, 1f, 0.3f), 0.06f * r, 0.7f * r, 4);
                hangs[(s + 1) / 2] = a2;
            }
            foreach (var hp in hangs)
            {
                M.Color = barkD;
                M.Segment(hp, hp + Vector3.down * 0.35f * r, 0.02f * r, 0.02f * r, 3);
                M.Emission = 1f; M.Color = glow;
                M.Sphere(hp + Vector3.down * 0.65f * r, new Vector3(0.24f, 0.32f, 0.24f) * r, 7, 5);
                M.Emission = 0f; M.Color = paper;
                M.Cylinder(hp + Vector3.down * 0.4f * r, 0.12f * r, 0.12f * r, 0.08f * r, 6);
            }
            M.Color = moss;
            M.Blob(hc + new Vector3(0.1f * r, 0.8f * r, -0.3f * r), new Vector3(0.7f, 0.3f, 0.6f) * r, 0, 0.3f, 81);
            // shoulders: mossy burls with mushrooms, hanging moss
            for (int s = -1; s <= 1; s += 2)
            {
                M.Bone = BB.ArmU(s);
                var sp = k.Bind[BB.ArmU(s)] + new Vector3(s * 0.06f, 0.06f, 0f) * U;
                M.Color = barkL; M.Blob(sp, new Vector3(0.2f, 0.17f, 0.2f) * U, 0, 0.25f, 82 + s);
                M.Color = s < 0 ? moss : mossL; M.Blob(sp + new Vector3(0f, 0.1f, -0.02f) * U, new Vector3(0.17f, 0.08f, 0.16f) * U, 0, 0.3f, 84 + s);
                for (int i = 0; i < 2; i++)
                {
                    var mp = sp + new Vector3(s * (0.08f + i * 0.06f), 0.14f + i * 0.02f, 0.05f - i * 0.1f) * U;
                    M.Color = C("#f2e6cc"); M.Cylinder(mp, 0.02f * U, 0.018f * U, 0.07f * U, 5);
                    M.Color = cap; M.Emission = 0.25f;
                    M.Sphere(mp + Vector3.up * 0.075f * U, new Vector3(0.06f, 0.035f, 0.06f) * U, 6, 3);
                    M.Emission = 0f;
                }
                M.Color = barkD;
                M.Segment(k.Bind[BB.ArmU(s)], k.Bind[BB.ArmL(s)], 0.1f * U, 0.085f * U, 6);
                M.Color = mossL;
                M.Strip(k.Bind[BB.ArmU(s)] + new Vector3(s * 0.08f * U, -0.08f * U, -0.05f * U), k.Bind[BB.ArmU(s)] + new Vector3(s * 0.1f * U, -0.42f * U, -0.06f * U), 0.08f * U, 0.02f * U, Vector3.right * s, 0.012f);
                M.Bone = BB.ArmL(s); M.Color = bark;
                M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.085f * U, 0.1f * U, 6);
                M.Color = barkL;
                M.Curve(k.Bind[BB.ArmL(s)] + new Vector3(s * 0.07f, 0f, 0.03f) * U, Vector3.Lerp(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.5f) + new Vector3(s * 0.12f, 0f, -0.03f) * U,
                        k.Bind[BB.Hand(s)] + new Vector3(s * 0.06f, 0.02f, 0.02f) * U, 0.03f * U, 0.02f * U, 3, 4);
                M.Bone = BB.Hand(s); M.Color = barkD;
                var h = k.Bind[BB.Hand(s)];
                M.Blob(h + Vector3.down * 0.06f * U, new Vector3(0.1f, 0.09f, 0.1f) * U, 0, 0.2f, 86 + s);
                for (int c = 0; c < 4; c++)
                {
                    float x = (c - 1.5f) * 0.045f * U;
                    M.Curve(h + new Vector3(x, -0.1f * U, 0.03f * U), h + new Vector3(x * 1.3f, -0.25f * U, 0.1f * U), h + new Vector3(x * 1.5f, -0.38f * U, 0.02f * U), 0.03f * U, 0.006f * U, 3, 4);
                }
                // stumpy legs splaying into roots
                k.Leg(s, bark, barkD, barkD, 0f);
                M.Bone = BB.Foot(s); M.Color = barkL;
                var f = k.Bind[BB.Foot(s)];
                for (int c = -1; c <= 1; c++)
                    M.Curve(f + Vector3.up * 0.04f * U, f + new Vector3(c * 0.1f, 0.04f, 0.12f) * U, new Vector3(f.x + c * 0.18f * U, 0.02f, f.z + (0.24f - Mathf.Abs(c) * 0.06f) * U), 0.04f * U, 0.012f * U, 3, 4);
            }
            var m = k.Model;
            m.Strike = UnitStrike.Slam; m.Ranged = UnitRanged.Point;
            m.Heavy = 0.85f; m.MaxCadence = 1.3f; m.TurnRate = 220f;
            m.DustColor = new Color(0.6f, 0.52f, 0.4f, 0.4f);
            m.CastBone = BB.Chest; m.CastOffset = core - k.Bind[BB.Chest];
            var res = Done(k, key, UnitGait.Heavy);
            res.HeadTop = hc + new Vector3(0f, 2.5f * r, 0f) - k.Bind[BB.Head];
            return res;
        }

        /// <summary>
        /// Thornmaw (raid boss 1 of the Hollow Heart): a giant beast of tangled root wood, 3 m natural (data size ≈ 6), low
        /// and massive, wrapped in twisting root bands, a bramble of black thorns and blight crystals along the back, moss and
        /// pale corrupted mushrooms, a huge maw lined with thorn teeth glowing violet-red inside, four violet eyes, root
        /// horns, trunk-like legs splaying into roots and a stubby root tail.
        /// </summary>
        static UnitModel Thornmaw(string key)
        {
            var k = new QuadKit(94, 1.3f, 1.5f, 1.9f, 0.86f, 0.62f, 0.2f, 1.25f, 1.2f, 0.12f, 0.5f, 0.62f, 0.07f);
            Color bark = C("#5c4434"), barkD = C("#3a2a20"), barkL = C("#86644a"), thorn = C("#2a2028"), thornT = C("#d8c8b0"),
                  blight = C("#b47aff"), blightL = C("#e2c6ff"), moss = C("#5f8a44"), maw = C("#c84a7a"), shroom = C("#dcb0ec");
            var M = k.M;
            M.Jitter = 0.08f;
            k.Body(bark, barkD, 1.05f, 1.15f, 0.9f, 1.0f, barkD);
            float zMid = (k.Bind[QB.Hips].z + k.Bind[QB.Chest].z) * 0.5f;
            // twisting root bands round the torso
            M.Color = barkL;
            for (int b = 0; b < 4; b++)
            {
                float z0 = Mathf.Lerp(k.Bind[QB.Hips].z - 0.6f, k.Bind[QB.Chest].z + 0.4f, b / 3f);
                for (int s = -1; s <= 1; s += 2)
                {
                    M.Bone = z0 > zMid ? QB.Chest : QB.Hips;
                    var a = k.TorsoPoint(z0, s * 20f, 0.02f);
                    var c = k.TorsoPoint(z0 - 0.3f, s * 75f, 0.07f);
                    var e = k.TorsoPoint(z0 - 0.45f, s * 135f, 0.02f);
                    M.Curve(a, c, e, 0.09f, 0.05f, 4, 5);
                }
            }
            EmberSeams(k, blight, 4);
            // the bramble: black thorns curving back, blight crystals, moss and pale mushrooms
            for (int i = 0; i < 12; i++)
            {
                float t = i / 11f;
                float z = Mathf.Lerp(k.Bind[QB.Chest].z + 0.55f, k.Bind[QB.Hips].z - 0.7f, t);
                float side = (i % 3 - 1) * 0.28f;
                M.Bone = z > zMid ? QB.Chest : QB.Hips;
                var at = k.TorsoPoint(z, side * 120f, -0.04f);
                float len = Mathf.Lerp(0.95f, 0.5f, t) * (i % 2 == 0 ? 1f : 0.75f);
                var dir = new Vector3(side, 1f, -0.5f).normalized;
                M.Color = thorn;
                M.Curve(at, at + dir * len * 0.6f, at + dir * len + new Vector3(0f, -0.1f, -len * 0.45f), 0.09f, 0.012f, 3, 5);
                if (i % 4 == 1)
                {
                    M.Emission = 1f; M.Color = i % 8 == 1 ? blight : blightL;
                    M.Spike(k.TorsoPoint(z - 0.1f, -side * 100f, -0.05f), new Vector3(-side, 1f, -0.2f), 0.1f, 0.55f, 4);
                    M.Emission = 0f;
                }
                if (i % 4 == 3)
                {
                    M.Color = moss;
                    M.Blob(k.TorsoPoint(z, -side * 80f, 0.02f), new Vector3(0.3f, 0.12f, 0.28f), 0, 0.3f, 90 + i);
                    var mp = k.TorsoPoint(z + 0.12f, -side * 80f + 15f, 0.06f);
                    M.Color = C("#efe2d4"); M.Cylinder(mp, 0.035f, 0.03f, 0.14f, 5);
                    M.Color = shroom; M.Emission = 0.35f;
                    M.Sphere(mp + Vector3.up * 0.15f, new Vector3(0.12f, 0.06f, 0.12f), 6, 3);
                    M.Emission = 0f;
                }
            }
            k.Neck(bark, 1.15f, 1.1f);
            // the head: a broad wooden skull and upper jaw with thorn teeth, four violet eyes, root horns
            float r = k.HeadR;
            var hc = k.HeadBase + new Vector3(0f, r * 0.15f, r * 0.2f);
            M.Bone = QB.Head; M.Color = bark;
            M.Blob(hc, new Vector3(1.05f, 0.75f, 0.95f) * r, 1, 0.12f, 95);
            var s0 = hc + new Vector3(0f, -r * 0.1f, r * 0.5f);
            var s1 = s0 + new Vector3(0f, -r * 0.15f, r * 0.85f);
            M.Push().Translate(s0).Scale(new Vector3(1.15f, 0.6f, 1f)).Translate(-s0);
            M.Segment(s0, s1, r * 0.82f, r * 0.62f, 7);
            M.Pop();
            M.Emission = 0.9f; M.Color = maw;
            M.Sphere(Vector3.Lerp(s0, s1, 0.5f) + Vector3.down * r * 0.32f, new Vector3(r * 0.7f, r * 0.12f, r * 0.55f), 7, 3);
            M.Emission = 0f; M.Color = thornT;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 5; i++)
                {
                    var p = Vector3.Lerp(s0, s1, 0.05f + i * 0.22f) + new Vector3(s * r * Mathf.Lerp(0.85f, 0.55f, i / 4f), -r * 0.32f, 0f);
                    M.Spike(p, new Vector3(s * 0.15f, -1f, 0.15f), r * 0.07f, r * (0.28f + (i % 2) * 0.12f), 4);
                }
            M.Emission = 1f; M.Color = blightL;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 2; i++)
                    M.Sphere(hc + new Vector3(s * r * (0.55f + i * 0.2f), r * (0.32f - i * 0.18f), r * (0.62f - i * 0.15f)), new Vector3(r * 0.12f, r * 0.08f, r * 0.06f) * (i == 0 ? 1.2f : 0.85f), 5, 3);
            M.Emission = 0f; M.Color = barkL;
            for (int s = -1; s <= 1; s += 2)
            {
                var h0 = hc + new Vector3(s * r * 0.6f, r * 0.45f, -r * 0.3f);
                M.Curve(h0, h0 + new Vector3(s * r * 0.6f, r * 0.7f, -r * 0.3f), h0 + new Vector3(s * r * 0.5f, r * 1.0f, -r * 1.2f), r * 0.2f, r * 0.04f, 4, 5);
                M.Color = thorn;
                M.Spike(h0 + new Vector3(s * r * 0.4f, r * 0.55f, -r * 0.25f), new Vector3(s * 0.6f, 0.7f, 0.3f), r * 0.06f, r * 0.45f, 4);
                M.Color = barkL;
            }
            M.Color = moss;
            M.Blob(hc + new Vector3(-r * 0.2f, r * 0.62f, -r * 0.1f), new Vector3(0.55f, 0.2f, 0.5f) * r, 0, 0.3f, 96);
            // lower jaw
            M.Bone = QB.Jaw; M.Color = barkD;
            var j = k.Bind[QB.Jaw];
            M.Push().Translate(j).Scale(new Vector3(1.15f, 0.5f, 1f)).Translate(-j);
            M.Segment(j + new Vector3(0f, 0f, -r * 0.1f), j + new Vector3(0f, -r * 0.1f, r * 1.05f), r * 0.75f, r * 0.55f, 6);
            M.Pop();
            M.Emission = 0.9f; M.Color = maw;
            M.Sphere(j + new Vector3(0f, r * 0.12f, r * 0.5f), new Vector3(r * 0.55f, r * 0.08f, r * 0.45f), 6, 3);
            M.Emission = 0f; M.Color = thornT;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 4; i++)
                    M.Spike(j + new Vector3(s * r * Mathf.Lerp(0.62f, 0.4f, i / 3f), r * 0.12f, r * (0.15f + i * 0.27f)), new Vector3(s * 0.1f, 1f, 0.1f), r * 0.06f, r * 0.28f, 4);
            k.Model.HeadBone = QB.Head;
            k.Model.HeadTop = hc + new Vector3(0f, r * 1.35f, 0f) - k.HeadBase;
            k.Model.CastBone = QB.Head;
            // trunk legs with root toes
            k.Legs(bark, barkD, barkD, 1.2f, false, 1.1f);
            M.Color = barkL;
            int[] feet = { QB.FLF, QB.FRF, QB.BLF, QB.BRF };
            foreach (int f in feet)
            {
                M.Bone = f;
                var a = k.Bind[f];
                for (int c = -2; c <= 2; c++)
                {
                    float ang = c * 0.55f;
                    var e = new Vector3(a.x + Mathf.Sin(ang) * 0.45f, 0.03f, a.z + Mathf.Cos(ang) * 0.42f);
                    M.Curve(a + Vector3.up * 0.12f, Vector3.Lerp(a, e, 0.5f) + Vector3.up * 0.14f, e, 0.08f, 0.02f, 3, 4);
                }
            }
            k.Tail(bark, 0.8f, 0.22f, thorn, false, 0.8f);
            k.Finish(key, k.HeadBase.y + r * 1.25f, UnitStrike.Bite, UnitRanged.Howl);
            var m = k.Model;
            m.CastOffset = Vector3.Lerp(s0, s1, 0.8f) - k.HeadBase;
            m.Heavy = 1f; m.MaxCadence = 1.0f; m.TurnRate = 110f; m.StrideK = 1.05f;
            m.DustColor = new Color(0.55f, 0.48f, 0.4f, 0.45f);
            return Bake(k);
        }

        // ================================================================== the Hollow Heart (raid final boss)

        /// <summary>
        /// The Hollow Heart: the spirit grove's great heart-lantern, corrupted — a huge beating heart (rose-violet, glowing
        /// veins) hung in a lantern cage of root ribs under a dark shrine-lantern roof with paper charms, root bundles
        /// dangling beneath, and eight great grasping root tendrils arching out of it into the ground (the spider rig: the
        /// tendrils plant, rear and lash in attacks; the heart beats on SB.Abdomen). A rooted, camera-facing static boss.
        /// Natural 4.4 m to the roof finial; the tendrils arch to ≈ 5 m.
        /// </summary>
        static UnitModel HollowHeart(string key)
        {
            var k = new SpiderKit(95, 1.5f, 1.25f, 3.3f);
            Color bark = C("#5a4642"), barkL = C("#7e6458"), heart = C("#d24a84"), heartD = C("#8a2a5e"), vein = C("#ffc0e0"),
                  blight = C("#c08aff"), roof = C("#3a2c34"), roofL = C("#6a5260"), paper = C("#f6ecd8"), gold = C("#ffd27a");
            float R = k.BodyR;
            var body = k.Bind[SB.Body];
            k.Bind[SB.Abdomen] = body;
            float height = body.y + R * 2.15f;   // the finial
            k.Finish(key, height);
            var m = k.Model;
            var M = k.M;
            M.Jitter = 0.06f;
            // the heart (beats on the Abdomen bone): two big lobes and a point, glowing veins and a golden lantern core
            M.Bone = SB.Abdomen;
            M.Emission = 0.5f; M.Color = heart;
            for (int sd = -1; sd <= 1; sd += 2)
                M.Sphere(body + new Vector3(sd * R * 0.36f, R * 0.22f, 0f), new Vector3(R * 0.6f, R * 0.6f, R * 0.54f), 10, 7);
            M.Color = heartD;
            M.Push().Translate(body + new Vector3(0f, -R * 0.08f, 0f)).Rotate(180f, 0f, 0f);
            M.Cone(Vector3.zero, R * 0.78f, R * 0.78f, 10, true);
            M.Pop();
            // the stumps of great vessels on top, where the corruption creeps in
            M.Emission = 0.2f; M.Color = heartD;
            for (int sd = -1; sd <= 1; sd += 2)
                M.Segment(body + new Vector3(sd * R * 0.18f, R * 0.62f, -R * 0.1f), body + new Vector3(sd * R * 0.3f, R * 1.0f, -R * 0.2f), R * 0.16f, R * 0.12f, 7);
            M.Emission = 1f; M.Color = vein;
            for (int i = 0; i < 7; i++)
            {
                float a = (i * 51f + 15f) * Mathf.Deg2Rad;
                var p0 = body + new Vector3(Mathf.Sin(a) * R * 0.3f, R * 0.72f, Mathf.Cos(a) * R * 0.3f);
                var p1 = body + new Vector3(Mathf.Sin(a) * R * 0.78f, R * 0.12f, Mathf.Cos(a) * R * 0.6f);
                var p2 = body + new Vector3(Mathf.Sin(a) * R * 0.25f, -R * 0.8f, Mathf.Cos(a) * R * 0.22f);
                M.Curve(p0, p1 + (p1 - body) * 0.1f, p2, R * 0.045f, R * 0.02f, 4, 4);
            }
            M.Color = gold;
            M.Sphere(body + new Vector3(0f, R * 0.2f, R * 0.5f), new Vector3(R * 0.2f, R * 0.26f, R * 0.1f), 7, 4);
            M.Emission = 0f;
            // the lantern cage: six root ribs from the base knot, up round the heart, to a ring of the old lantern frame
            M.Bone = SB.Body;
            var baseK = body + Vector3.down * R * 1.0f;
            var ringY = body.y + R * 1.18f;
            M.Color = bark;
            M.Sphere(baseK, new Vector3(R * 0.45f, R * 0.25f, R * 0.45f), 8, 4);
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f + 30f) * Mathf.Deg2Rad;
                var o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var mid = body + o * R * 1.08f;
                M.Color = i % 2 == 0 ? bark : barkL;
                M.Curve(baseK + o * R * 0.3f, mid + Vector3.down * R * 0.6f, mid, R * 0.08f, R * 0.07f, 3, 5);
                M.Curve(mid, mid + Vector3.up * R * 0.6f, new Vector3(o.x * R * 0.72f, ringY, o.z * R * 0.72f), R * 0.07f, R * 0.055f, 3, 5);
                if (i % 2 == 0)
                {
                    M.Emission = 1f; M.Color = blight;
                    M.Spike(mid, o + Vector3.up * 0.3f, R * 0.07f, R * 0.32f, 4);
                    M.Emission = 0f;
                }
            }
            // the old lantern frame: a dark ring with a gold finial on four arched struts, paper charms hanging from it
            M.Color = roof;
            M.Push().Translate(new Vector3(0f, ringY, 0f));
            M.Torus(Vector3.zero, R * 0.72f, R * 0.07f, 12, 5);
            M.Pop();
            M.Color = roofL;
            var fin = new Vector3(0f, ringY + R * 0.75f, 0f);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                M.Curve(new Vector3(o.x * R * 0.72f, ringY, o.z * R * 0.72f), new Vector3(o.x * R * 0.55f, ringY + R * 0.7f, o.z * R * 0.55f), fin, R * 0.05f, R * 0.04f, 3, 4);
            }
            M.Color = gold; M.Emission = 0.7f;
            M.Sphere(fin + Vector3.up * R * 0.1f, new Vector3(R * 0.15f, R * 0.2f, R * 0.15f), 7, 4);
            M.Emission = 0f;
            M.Color = paper;
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f) * Mathf.Deg2Rad;
                var o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var p = new Vector3(o.x * R * 0.75f, ringY - R * 0.04f, o.z * R * 0.75f);
                M.Strip(p, p + Vector3.down * R * 0.25f + o * R * 0.05f, R * 0.11f, R * 0.11f, o, 0.01f);
                M.Strip(p + Vector3.down * R * 0.25f + o * R * 0.05f, p + Vector3.down * R * 0.48f, R * 0.11f, R * 0.09f, o, 0.01f);
            }
            // roots spreading from the base knot onto the ground
            M.Color = bark;
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f + 30f) * Mathf.Deg2Rad;
                var o = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var a0 = baseK + o * R * 0.2f;
                var e = new Vector3(o.x * R * (0.95f + (i % 2) * 0.25f), 0.04f, o.z * R * (0.95f + (i % 2) * 0.25f));
                M.Curve(a0, a0 + o * R * 0.45f + Vector3.down * R * 0.05f, e, R * 0.12f, R * 0.03f, 3, 5);
            }
            // the eight root tendrils: authored hanging from their joints (the IK arches them into the ground)
            for (int i = 0; i < m.Legs.Length; i++)
            {
                var leg = m.Legs[i];
                var hip = k.Bind[leg.Upper];
                var knee = k.Bind[leg.Lower];
                var foot = knee + Vector3.down * leg.B;
                // bowed like old roots: +Z here is the side the IK pole (out and up) turns to, so the upper part arches
                // further out and the lower part curls back in towards the ground (an S)
                // the front pair (Legs[0], Legs[1]: they rear and lash in attacks) are slender grasping arms, the six behind thick roots
                bool arm = i < 2;
                float th = arm ? 0.62f : (i % 3 == 0 ? 1.15f : (i % 3 == 1 ? 0.92f : 1.05f));
                M.Bone = leg.Upper; M.Color = i % 2 == 0 ? bark : barkL;
                var hip0 = Vector3.Lerp(body, hip, 0.6f);
                M.Curve(hip0, Vector3.Lerp(hip0, knee, 0.5f) + Vector3.forward * leg.A * 0.16f, knee, R * 0.26f * th, R * 0.18f * th, 3, 7);
                M.Sphere(knee, R * 0.2f * th, 7, 4);
                M.Bone = leg.Lower;
                var mid = Vector3.Lerp(knee, foot, 0.55f) + Vector3.back * leg.B * 0.1f;
                M.Curve(knee, Vector3.Lerp(knee, mid, 0.5f) + Vector3.back * leg.B * 0.04f, mid, R * 0.18f * th, R * 0.12f * th, 2, 6);
                M.Curve(mid, Vector3.Lerp(mid, foot, 0.5f) + Vector3.back * leg.B * 0.03f, foot, R * 0.12f * th, R * 0.04f, 2, 6);
                M.Color = C("#2a2028");
                M.Spike(Vector3.Lerp(knee, mid, 0.5f), new Vector3(leg.Side, 0f, 0.3f), R * 0.05f, R * 0.25f, 4);
                M.Spike(Vector3.Lerp(mid, foot, 0.4f), new Vector3(-leg.Side, 0.2f, -0.3f), R * 0.04f, R * 0.2f, 4);
                if (i % 2 == 1)
                {
                    M.Emission = 1f; M.Color = blight;
                    M.Segment(knee + new Vector3(0f, 0f, R * 0.17f), mid + new Vector3(0f, 0f, R * 0.11f), R * 0.03f, R * 0.02f, 4);
                    M.Emission = 0f;
                }
                M.Color = barkL;
                if (arm)
                {
                    // a grasping hand of hooked root fingers
                    M.Color = C("#2a2028");
                    for (int c = -1; c <= 1; c++)
                        M.Curve(foot + Vector3.up * R * 0.3f, foot + new Vector3(c * R * 0.22f, R * 0.1f, R * 0.25f), foot + new Vector3(c * R * 0.16f, -R * 0.02f, R * 0.32f), R * 0.06f, R * 0.012f, 3, 4);
                }
                else
                    for (int c = -1; c <= 1; c++)
                        M.Curve(foot + Vector3.up * R * 0.15f, foot + new Vector3(c * R * 0.12f, R * 0.02f, R * 0.08f), foot + new Vector3(c * R * 0.22f, -R * 0.05f, R * 0.1f), R * 0.05f, R * 0.01f, 2, 4);
            }
            m.Static = true;
            m.HeartBeat = 0.9f;
            m.Dust = false;
            m.TurnRate = 0f;
            m.Strike = UnitStrike.Fangs; m.Ranged = UnitRanged.Pulse;
            m.HeadBone = SB.Body;
            m.HeadTop = new Vector3(0f, height - body.y + 0.1f, 0f);
            m.CenterBone = SB.Body; m.CenterOffset = Vector3.zero;
            m.CastBone = SB.Abdomen; m.CastOffset = new Vector3(0f, 0f, R * 0.4f);
            m.PickBones = new[] { SB.Body, SB.Abdomen, SB.Lower(0), SB.Lower(3), SB.Lower(4), SB.Lower(7) };
            m.PickPad = R * 1.1f;

            return UnitModels.Bake(m, k.M);
        }
    }
}
