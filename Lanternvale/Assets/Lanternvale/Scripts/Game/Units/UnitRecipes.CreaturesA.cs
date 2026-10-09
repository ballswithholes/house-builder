// Expansion models, group A (Docs/Expansion.md §9 models-a): the humanoid bipeds of the level 12-30 zones, the hidden
// dungeons and the raids. Registered through RegisterCreaturesA (UnitRecipes.TryBuildExpansion).
//
//   gnolls     cr_gnoll (cleaver brute), cr_gnoll_archer, cr_gnoll_mystic (bone fetishes, skull staff),
//              cr_gnoll_chief (elite: bone crown, war banner, great cleaver) — hunched hyena-folk, spotted sand fur
//   tunnelers  cr_tunneler (mole-folk miner: candle helmet, pick), cr_tunneler_geomancer (spectacles, crystal staff)
//   mirelings  cr_mireling (round frog-folk: wide mouth, fin crest), cr_mireling_hunter (spear), cr_mireling_oracle
//              (shell staff)
//   others     cr_mire_hag (crooked bog witch: reed cloak, lantern), cr_ogre (club), cr_ogre_mage (robes, glowing
//              staff), cr_dragonsworn (scale-mail cultist, horned helm), cr_mossling_king (giant mossling, mushroom crown)
//
// Every model is a BipedKit biped. Natural heights are what the data `size` scales; the recommended sizes per key are
// in Docs/ArtKeys.md ("3D-only model keys", the models-a block).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class UnitRecipes
    {
        static partial void RegisterCreaturesA(Dictionary<string, Func<UnitModel>> d)
        {
            d["cr_gnoll"] = () => Gnoll("cr_gnoll", GnollKind.Brute);
            d["cr_gnoll_archer"] = () => Gnoll("cr_gnoll_archer", GnollKind.Archer);
            d["cr_gnoll_mystic"] = () => Gnoll("cr_gnoll_mystic", GnollKind.Mystic);
            d["cr_gnoll_chief"] = () => Gnoll("cr_gnoll_chief", GnollKind.Chief);
            d["cr_tunneler"] = () => Tunneler("cr_tunneler", false);
            d["cr_tunneler_geomancer"] = () => Tunneler("cr_tunneler_geomancer", true);
            d["cr_mireling"] = () => Mireling("cr_mireling", MirelingKind.Warrior);
            d["cr_mireling_hunter"] = () => Mireling("cr_mireling_hunter", MirelingKind.Hunter);
            d["cr_mireling_oracle"] = () => Mireling("cr_mireling_oracle", MirelingKind.Oracle);
            d["cr_mire_hag"] = () => MireHag("cr_mire_hag");
            d["cr_ogre"] = () => Ogre("cr_ogre", false);
            d["cr_ogre_mage"] = () => Ogre("cr_ogre_mage", true);
            d["cr_dragonsworn"] = () => Dragonsworn("cr_dragonsworn");
            d["cr_mossling_king"] = () => MosslingKing("cr_mossling_king");
        }

        // ================================================================== shared helpers

        /// <summary>A beast eye: a dark socket, a bright iris with a pupil and a white glint, turned out to its side.</summary>
        static void BeastEye(MeshBuilder M, Vector3 at, int side, float size, Color iris, Color socket, float glow = 0.25f)
        {
            float keepJ = M.Jitter;
            M.Jitter = 0f;
            M.Push().Translate(at).Rotate(0f, side * 34f, 0f);
            M.Color = socket;
            M.Sphere(Vector3.zero, new Vector3(1.25f, 1.05f, 0.55f) * size, 7, 4);
            M.Emission = glow; M.Color = iris;
            M.Sphere(new Vector3(0f, 0f, 0.28f * size), new Vector3(0.95f, 0.85f, 0.45f) * size, 7, 4);
            M.Emission = 0f; M.Color = C("#1c1416");
            M.Sphere(new Vector3(0f, -0.05f * size, 0.5f * size), new Vector3(0.45f, 0.55f, 0.3f) * size, 6, 3);
            M.Emission = 0.7f; M.Color = Color.white;
            M.Sphere(new Vector3(side * 0.25f * size, 0.3f * size, 0.62f * size), 0.2f * size, 4, 2);
            M.Emission = 0f;
            M.Pop();
            M.Jitter = keepJ;
        }

        /// <summary>Flattened spots laid on an ellipse band around a bone (fur spots, scales, patches).</summary>
        static void Spots(MeshBuilder M, int bone, Color c, Vector3 centre, float rx, float rz, float y0, float y1, int count, float size,
                          float th0 = -110f, float th1 = 110f)
        {
            M.Bone = bone; M.Color = c;
            for (int i = 0; i < count; i++)
            {
                float th = Mathf.Lerp(th0, th1, (i + 0.5f) / count + M.Range(-0.4f, 0.4f) / count) * Mathf.Deg2Rad;
                float y = Mathf.Lerp(y0, y1, M.Random01());
                var n = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                var p = centre + new Vector3(n.x * rx, y, n.z * rz);
                float s = size * M.Range(0.75f, 1.25f);
                M.Push().Translate(p).Rotate(Quaternion.LookRotation(n, Vector3.up));
                M.Sphere(Vector3.zero, new Vector3(s, s * 0.8f, s * 0.3f), 5, 2);
                M.Pop();
            }
        }

        /// <summary>
        /// A hunched posture for a BipedKit build: the torso, neck and head lean forward by `deg` about the hip joints and
        /// the arms move with the shoulders (they still hang straight down, as the animator expects of a bind pose).
        /// Draw the upper body inside BeginLean … M.Pop() and the arms (and anything held) inside BeginArms … M.Pop();
        /// legs, belt and skirts stay as they are. Call EndHunch once everything is built to move the bones.
        /// </summary>
        struct Hunch { public Matrix4x4 Body; public Vector3 Arm, HeadOff; }

        static Hunch MakeHunch(BipedKit k, float deg, Vector3 headOff)
        {
            var piv = new Vector3(0f, k.HipY, 0f);
            var m = Matrix4x4.Translate(piv) * Matrix4x4.Rotate(Quaternion.Euler(deg, 0f, 0f)) * Matrix4x4.Translate(-piv);
            var sh = new Vector3(0f, k.ShoulderY, 0f);
            return new Hunch { Body = m, Arm = m.MultiplyPoint3x4(sh) - sh, HeadOff = headOff };
        }

        static void BeginLean(BipedKit k, Hunch h) { k.M.Push(); k.M.Matrix = k.M.Matrix * h.Body; }
        static void BeginArms(BipedKit k, Hunch h) { k.M.Push().Translate(h.Arm); }

        /// <summary>A point given in the un-leaned build space (head-space points include HeadOff) → bind space.</summary>
        static Vector3 Leaned(Hunch h, Vector3 p) => h.Body.MultiplyPoint3x4(p);

        static void EndHunch(BipedKit k, Hunch h)
        {
            var b = k.Bind;
            b[BB.Head] = h.Body.MultiplyPoint3x4(b[BB.Head] + h.HeadOff);
            b[BB.Neck] = h.Body.MultiplyPoint3x4(b[BB.Neck] + h.HeadOff * 0.4f);
            foreach (int i in new[] { BB.Spine, BB.Chest, BB.Cape, BB.HairB, BB.WingL, BB.WingR })
                b[i] = h.Body.MultiplyPoint3x4(b[i]);
            foreach (int i in new[] { BB.ArmUL, BB.ArmLL, BB.HandL, BB.ArmUR, BB.ArmLR, BB.HandR, BB.DrawnR, BB.StringA, BB.StringB, BB.ArrowR })
                b[i] += h.Arm;
        }

        // ================================================================== gnolls

        enum GnollKind { Brute, Archer, Mystic, Chief }

        /// <summary>
        /// Gnolls (Amberfield warcamp): hunched hyena-folk leaning forward from the hips. Sand-gold fur with chocolate
        /// spots, a long dark muzzle with a grin of teeth, big round dark-rimmed ears, a bristly dark mane with rust tips
        /// running from the crown down the hump of the back, a bushy tail, crude leathers and brick-red rags. The brute
        /// swings a notched cleaver, the archer a shortbow (green rags, feathers; claws in melee), the mystic a
        /// horned-skull staff hung with bone fetishes (teal glow, purple rags, face paint), and the chief is bigger, with
        /// a bone crown, spiked pauldrons, a war banner on his back and a great cleaver.
        /// </summary>
        static UnitModel Gnoll(string key, GnollKind kind)
        {
            bool chief = kind == GnollKind.Chief, mystic = kind == GnollKind.Mystic, archer = kind == GnollKind.Archer;
            float H = chief ? 2.35f : mystic ? 1.9f : 1.95f;
            var k = new BipedKit(chief ? 101 : 100 + (int)kind * 3, H, chief ? 0.16f : 0.155f, 0.45f,
                                 chief ? 1.42f : mystic ? 1.02f : 1.26f, false, chief ? 1.18f : mystic ? 1.04f : 1.14f, 1.16f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color fur = C("#a08068"), furL = C("#ead8b8"), spot = C("#4a3226"), muzzle = C("#3e2e2a"), nose = C("#231a1a"),
                  mane = C("#5b3a2b"), maneTip = C("#b4552d"), rag = archer ? C("#6f8a3c") : mystic ? C("#7a4a7a") : C("#b9472f"),
                  ragD = Paint.Shade(rag, 0.72f), leather = C("#6e4a32"), leatherD = C("#4a3226"), bone = C("#efe4c8"),
                  boneD = C("#cbbd98"), steel = C("#a9b0b4"), steelD = C("#6f777c"), wood = C("#7a5636"), teeth = C("#f6f0dc"),
                  glow = C("#63e6c0"), wrap = C("#d8c49a");
            if (chief) { mane = C("#3a2220"); maneTip = C("#d0582a"); fur = C("#8e6a52"); }

            var h = MakeHunch(k, chief ? 11f : 14f, new Vector3(0f, -0.03f * U, 0.07f * U));
            var hc = new Vector3(0f, k.HeadCY, 0f) + h.HeadOff;   // head centre in the un-leaned build space

            // ================= upper body (leaned)
            BeginLean(k, h);
            M.Jitter = 0.06f;
            k.Torso(fur, leather, Paint.Mix(fur, furL, 0.55f), chief ? 0.18f : 0.08f);
            M.Bone = BB.Chest; M.Color = fur;
            M.Blob(new Vector3(0f, k.ShoulderY - 0.06f * U, -k.ChestR * k.DepthK * 0.5f), new Vector3(k.ChestR * 0.95f, 0.13f * U, 0.12f * U), 0, 0.12f, 7);
            Spots(M, BB.Chest, spot, Vector3.zero, k.ChestR * 1.0f, k.ChestR * k.DepthK * 1.0f, k.SpineY + 0.04f * U, k.ShoulderY - 0.08f * U, 8, 0.034f * U, 40f, 320f);
            Spots(M, BB.Spine, spot, Vector3.zero, k.WaistR * 1.05f, k.WaistR * k.DepthK * 1.05f, k.PelvisY + 0.02f * U, k.SpineY + 0.02f * U, 4, 0.03f * U, 60f, 300f);
            // neck reaching forward to the head
            M.Bone = BB.Neck; M.Color = fur;
            M.Segment(new Vector3(0f, k.ShoulderY - 0.03f * U, -0.01f * U), hc + new Vector3(0f, -0.55f * r, -0.25f * r), 0.075f * U * k.Bulk, 0.062f * U, 7);
            M.Color = furL;
            M.Segment(new Vector3(0f, k.ShoulderY - 0.06f * U, 0.035f * U), hc + new Vector3(0f, -0.75f * r, 0.15f * r), 0.05f * U, 0.045f * U, 6);

            // ---- the hyena head
            M.Bone = BB.Head; M.Color = fur;
            M.Sphere(hc, new Vector3(0.98f * r, 0.9f * r, 1.0f * r), 9, 6);
            M.Color = Paint.Shade(fur, 0.88f);
            M.Sphere(hc + new Vector3(0f, 0.3f * r, 0.5f * r), new Vector3(0.8f * r, 0.3f * r, 0.42f * r), 7, 3);
            M.Color = furL;
            for (int s = -1; s <= 1; s += 2)
                M.Sphere(hc + new Vector3(s * 0.55f * r, -0.35f * r, 0.42f * r), new Vector3(0.38f, 0.32f, 0.35f) * r, 6, 3);
            // long dark muzzle, black nose, an open toothy jaw
            M.Color = muzzle;
            M.Segment(hc + new Vector3(0f, -0.1f * r, 0.5f * r), hc + new Vector3(0f, -0.2f * r, 1.68f * r), 0.52f * r, 0.34f * r, 8, true);
            M.Color = nose;
            M.Sphere(hc + new Vector3(0f, -0.02f * r, 1.92f * r), new Vector3(0.25f, 0.18f, 0.18f) * r, 6, 3);
            M.Color = C("#7a2e2a");
            M.Sphere(hc + new Vector3(0f, -0.56f * r, 1.15f * r), new Vector3(0.3f, 0.12f, 0.48f) * r, 6, 3);
            M.Color = Paint.Shade(muzzle, 1.25f);
            M.Segment(hc + new Vector3(0f, -0.58f * r, 0.45f * r), hc + new Vector3(0f, -0.74f * r, 1.42f * r), 0.3f * r, 0.2f * r, 7, true);
            M.Color = teeth;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 3; i++)
                {
                    float z = 0.95f + i * 0.28f;
                    M.Spike(hc + new Vector3(s * (0.3f - i * 0.04f) * r, -0.42f * r - i * 0.02f * r, z * r), new Vector3(0f, -1f, 0.15f), 0.06f * r, (i == 0 ? 0.28f : 0.17f) * r, 3);
                    if (i < 2) M.Spike(hc + new Vector3(s * (0.24f - i * 0.03f) * r, -0.64f * r - i * 0.03f * r, (z - 0.05f) * r), new Vector3(0f, 1f, 0.1f), 0.05f * r, 0.15f * r, 3);
                }
            // amber eyes on the sides of the brow
            for (int s = -1; s <= 1; s += 2)
                BeastEye(M, hc + new Vector3(s * 0.46f * r, 0.1f * r, 0.8f * r), s, 0.18f * r, chief ? C("#ffcf4a") : C("#f2b23a"), C("#3a2622"), chief ? 0.5f : 0.25f);
            M.Bone = BB.Head;
            // big round ears, dark-rimmed, standing up
            for (int s = -1; s <= 1; s += 2)
            {
                M.Push().Translate(hc + new Vector3(s * 0.58f * r, 0.88f * r, -0.15f * r)).Rotate(-18f, s * 22f, -s * 16f);
                M.Color = mane;
                M.Sphere(Vector3.zero, new Vector3(0.46f, 0.56f, 0.12f) * r, 8, 3);
                M.Color = C("#d8a07c");
                M.Sphere(new Vector3(0f, -0.06f * r, 0.07f * r), new Vector3(0.32f, 0.4f, 0.07f) * r, 7, 2);
                M.Pop();
            }
            // the bristly mane: crown → nape (Head) → the hump of the back (Chest)
            for (int i = 0; i < 11; i++)
            {
                float t = i / 10f;
                bool onHead = i < 4;
                M.Bone = onHead ? BB.Head : BB.Chest;
                Vector3 p, dir;
                if (onHead)
                {
                    float ph = Mathf.Lerp(0f, 115f, i / 3f) * Mathf.Deg2Rad;
                    p = hc + new Vector3(0f, Mathf.Cos(ph) * 0.9f * r, -Mathf.Sin(ph) * 0.9f * r + 0.12f * r);
                    dir = new Vector3(0f, 1f, -0.8f);
                }
                else
                {
                    float q = (i - 4) / 6f;
                    p = new Vector3(0f, Mathf.Lerp(k.ShoulderY + 0.06f * U, k.ChestY - 0.06f * U, q), -k.ChestR * k.DepthK * Mathf.Lerp(0.55f, 1.05f, q) - 0.05f * U * (1f - q));
                    dir = new Vector3(0f, 0.8f - q * 0.5f, -1f);
                }
                float len = (chief ? 0.21f : 0.16f) * U * (1f - Mathf.Abs(t - 0.4f) * 0.8f);
                var dn = dir.normalized;
                M.Color = mane;
                M.Spike(p, dn, 0.04f * U, len, 4);
                for (int j = -1; j <= 1; j += 2)
                    M.Spike(p + new Vector3(j * 0.03f * U, 0f, 0f), (dir + new Vector3(j * 0.45f, 0f, 0f)).normalized, 0.032f * U, len * 0.7f, 3);
                M.Color = maneTip;
                M.Spike(p + dn * len * 0.55f, dn, 0.022f * U, len * 0.6f, 3);
            }

            // ---- chest-mounted gear by role
            M.Jitter = 0.04f;
            switch (kind)
            {
                case GnollKind.Brute:
                    k.Strap(leather, -1, 0.06f);
                    break;
                case GnollKind.Archer:
                    k.Strap(leatherD, 1, 0.05f);
                    // a green rag bandana with tails, feathers tucked behind an ear
                    M.Bone = BB.Head; M.Color = rag;
                    M.Push().Translate(hc + new Vector3(0f, 0.4f * r, 0.05f * r)).Rotate(-14f, 0f, 0f);
                    M.Torus(Vector3.zero, 0.88f * r, 0.13f * r, 10, 4);
                    M.Pop();
                    M.Color = ragD;
                    M.Blade(hc + new Vector3(0.1f * r, 0.3f * r, -0.85f * r), hc + new Vector3(0.25f * r, -0.4f * r, -1.45f * r), 0.18f * r);
                    M.Blade(hc + new Vector3(-0.1f * r, 0.3f * r, -0.85f * r), hc + new Vector3(-0.15f * r, -0.55f * r, -1.3f * r), 0.16f * r);
                    M.Color = C("#e8e0cc");
                    M.Blade(hc + new Vector3(0.75f * r, 0.5f * r, -0.4f * r), hc + new Vector3(1.1f * r, 1.35f * r, -0.9f * r), 0.15f * r);
                    M.Color = C("#c8462e");
                    M.Blade(hc + new Vector3(0.7f * r, 0.45f * r, -0.5f * r), hc + new Vector3(1.3f * r, 1.05f * r, -1.05f * r), 0.13f * r);
                    k.Quiver(leatherD, C("#e8e0cc"));
                    break;
                case GnollKind.Mystic:
                {
                    // white face paint, feathers, a necklace of fangs and beads
                    M.Bone = BB.Head; M.Color = C("#f4efe2");
                    for (int s = -1; s <= 1; s += 2)
                    {
                        M.Push().Translate(hc + new Vector3(s * 0.34f * r, 0.0f, 1.12f * r)).Rotate(10f, s * 24f, s * 30f);
                        M.Box(Vector3.zero, new Vector3(0.08f, 0.34f, 0.05f) * r);
                        M.Pop();
                    }
                    M.Box(hc + new Vector3(0f, 0.18f * r, 1.3f * r), new Vector3(0.08f, 0.06f, 0.45f) * r);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = (i - 1.5f) * 0.38f;
                        M.Color = i % 2 == 0 ? C("#2a5a6a") : C("#f0e8d4");
                        var fp = hc + new Vector3(Mathf.Sin(a) * 0.5f * r, 0.6f * r, -0.7f * r);
                        M.Blade(fp, fp + new Vector3(Mathf.Sin(a) * 0.55f * r, 1.1f * r, -0.6f * r), 0.17f * r);
                    }
                    M.Bone = BB.Chest;
                    for (int i = 0; i < 9; i++)
                    {
                        float a = (i - 4) * 0.27f;
                        var p = new Vector3(Mathf.Sin(a) * k.ChestR * 0.85f, k.ShoulderY - 0.05f * U - Mathf.Cos(a) * 0.07f * U, Mathf.Cos(a) * k.ChestR * k.DepthK * 1.12f + 0.01f * U);
                        M.Color = i % 2 == 0 ? bone : C("#c8462e");
                        if (i % 2 == 0) M.Spike(p + Vector3.up * 0.015f * U, Vector3.down, 0.015f * U, 0.08f * U, 3);
                        else M.Sphere(p, 0.02f * U, 5, 3);
                    }
                    break;
                }
                case GnollKind.Chief:
                {
                    k.Mantle(mane, 1.4f, 0.05f, 21);
                    k.Strap(leatherD, -1, 0.075f);
                    // the bone crown: a band of bone with tall fangs and a red stone
                    M.Bone = BB.Head;
                    M.Push().Translate(hc + new Vector3(0f, 0.55f * r, -0.02f * r)).Rotate(-10f, 0f, 0f);
                    M.Color = boneD;
                    M.Torus(Vector3.zero, 0.8f * r, 0.11f * r, 10, 4);
                    M.Color = bone;
                    for (int i = 0; i < 7; i++)
                    {
                        float a = (i - 3) * 0.42f;
                        float len = (i == 3 ? 1.0f : i % 2 == 0 ? 0.75f : 0.5f) * r;
                        var bp = new Vector3(Mathf.Sin(a) * 0.8f * r, 0f, Mathf.Cos(a) * 0.8f * r);
                        M.Spike(bp, new Vector3(Mathf.Sin(a) * 0.3f, 1f, Mathf.Cos(a) * 0.25f), 0.11f * r, len, 4);
                    }
                    M.Emission = 0.5f; M.Color = C("#e8402a");
                    M.Sphere(new Vector3(0f, 0.05f * r, 0.9f * r), new Vector3(0.15f, 0.17f, 0.08f) * r, 6, 3);
                    M.Emission = 0f;
                    M.Pop();
                    // war banner: a pole up the back, a crossbar, a brick-red hide with a white jaw, a ragged hem
                    M.Bone = BB.Chest;
                    var pb = new Vector3(-0.1f * U, k.SpineY, -k.ChestR * k.DepthK - 0.05f * U);
                    var ptop = new Vector3(-0.14f * U, H + 0.42f * U, -k.ChestR * k.DepthK - 0.1f * U);
                    M.Color = wood;
                    M.Segment(pb, ptop, 0.022f * U, 0.02f * U, 5);
                    var bar = ptop + new Vector3(0f, -0.08f * U, 0f);
                    M.Segment(bar + new Vector3(-0.2f * U, 0f, 0f), bar + new Vector3(0.2f * U, 0f, 0f), 0.016f * U, 0.016f * U, 5);
                    M.Color = bone;
                    M.Spike(ptop, Vector3.up, 0.03f * U, 0.12f * U, 4);
                    for (int s = -1; s <= 1; s += 2) M.Sphere(bar + new Vector3(s * 0.21f * U, 0f, 0f), 0.025f * U, 5, 3);
                    float bw = 0.36f * U, bh = 0.5f * U, bz = bar.z - 0.015f * U;
                    M.Color = rag;
                    M.Box(new Vector3(bar.x, bar.y - bh * 0.5f, bz), new Vector3(bw, bh, 0.012f * U));
                    for (int i = 0; i < 4; i++)
                    {
                        float x = bar.x - bw * 0.5f + bw * (i + 0.5f) / 4f;
                        M.Blade(new Vector3(x, bar.y - bh + 0.01f * U, bz), new Vector3(x, bar.y - bh - (0.08f + (i % 2) * 0.05f) * U, bz), bw / 4f * 0.95f, Vector3.right);
                    }
                    // the emblem on both faces: a white hyena jaw with fangs
                    M.Color = C("#f4efe2");
                    for (int f = -1; f <= 1; f += 2)
                    {
                        var ec = new Vector3(bar.x, bar.y - bh * 0.45f, bz + f * 0.012f * U);
                        M.Box(ec + new Vector3(0f, 0.06f * U, 0f), new Vector3(0.22f * U, 0.035f * U, 0.01f * U));
                        M.Box(ec + new Vector3(0f, -0.06f * U, 0f), new Vector3(0.22f * U, 0.035f * U, 0.01f * U));
                        for (int i = -2; i <= 2; i++)
                        {
                            M.Spike(ec + new Vector3(i * 0.045f * U, 0.045f * U, 0f), Vector3.down, 0.015f * U, 0.05f * U, 3);
                            M.Spike(ec + new Vector3(i * 0.045f * U + 0.02f * U, -0.045f * U, 0f), Vector3.up, 0.015f * U, 0.05f * U, 3);
                        }
                    }
                    break;
                }
            }
            M.Pop();

            // ================= arms and held things (moved with the shoulders)
            BeginArms(k, h);
            M.Jitter = 0.06f;
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, fur, fur, muzzle);
                Spots(M, BB.ArmU(s), spot, new Vector3(s * k.ShoulderX, 0f, 0f), k.ArmR * 1.0f, k.ArmR, k.ElbowY + 0.03f * U, k.ShoulderY - 0.06f * U, 2, 0.026f * U, s * 30f, s * 200f);
                k.Cuff(s, archer && s < 0 ? leatherD : wrap, 0.35f, 0.92f, 1.2f);
                M.Bone = BB.Hand(s); M.Color = bone;
                for (int c = -1; c <= 1; c++)
                    M.Spike(k.Bind[BB.Hand(s)] + new Vector3(s * 0.006f * U + c * 0.016f * U, -0.085f * U, 0.035f * U), new Vector3(c * 0.15f, -0.6f, 1f), 0.009f * U, 0.04f * U, 3);
            }
            M.Jitter = 0.04f;
            switch (kind)
            {
                case GnollKind.Brute:
                {
                    k.Pauldron(-1, leather, 1.25f, boneD, 2);
                    M.Bone = BB.ArmU(-1); M.Color = bone;
                    M.Spike(k.Bind[BB.ArmU(-1)] + new Vector3(-0.09f * U, 0.07f * U, 0f), new Vector3(-0.6f, 1f, -0.2f), 0.026f * U, 0.14f * U, 4);
                    // a crude notched cleaver (edge forward, +Z of the weapon frame), a rag tied to the grip
                    k.BeginHand(1, -0.1f * U);
                    M.Color = leatherD;
                    M.Cylinder(Vector3.zero, 0.024f * U, 0.022f * U, 0.2f * U, 6);
                    M.Color = steelD;
                    M.Box(new Vector3(0f, 0.37f * U, 0.02f * U), new Vector3(0.026f * U, 0.36f * U, 0.18f * U));
                    M.Color = steel;
                    M.Box(new Vector3(0f, 0.38f * U, 0.11f * U), new Vector3(0.018f * U, 0.34f * U, 0.035f * U));
                    M.Color = C("#3a2a26");
                    M.Box(new Vector3(0f, 0.45f * U, -0.02f * U), new Vector3(0.032f * U, 0.035f * U, 0.035f * U));
                    M.Box(new Vector3(0f, 0.3f * U, 0.13f * U), new Vector3(0.032f * U, 0.03f * U, 0.03f * U));
                    M.Color = rag;
                    M.Blade(new Vector3(0f, 0.18f * U, -0.02f * U), new Vector3(0f, 0.06f * U, -0.13f * U), 0.05f * U);
                    k.End();
                    var m = k.Model;
                    m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Throw;
                    m.CastBone = BB.HandR; m.CastOffset = new Vector3(0f, -0.06f * U, 0.03f * U);
                    break;
                }
                case GnollKind.Archer:
                {
                    k.Pauldron(-1, leatherD, 1.0f, null, 1);
                    k.Cuff(-1, leather, 0.2f, 0.95f, 1.32f);
                    k.Bow(-1, 1.1f, C("#6a4a2c"), rag, C("#e8e0d0"), C("#b89a6a"), C("#d8d0b8"));
                    var m = k.Model;
                    m.HoldL = UnitHold.Bow; m.HoldR = UnitHold.Claws; m.Strike = UnitStrike.Claw; m.Ranged = UnitRanged.Bow;
                    break;
                }
                case GnollKind.Mystic:
                {
                    k.Staff(1, 1.75f, C("#6a4a30"), BipedKit.StaffTop.Skull, bone, glow);
                    // fetishes hanging under the skull: bones, beads and a feather
                    float above = 1.75f * 0.58f * k.U;
                    k.BeginHand(1);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var hp = new Vector3(s * 0.05f * U, above - 0.02f * U, 0.02f * U);
                        M.Beads(hp, hp + new Vector3(s * 0.02f, -0.16f, 0f) * U, 4, 0.014f * U, 0.012f * U, C("#c8462e"), bone);
                        M.Color = s < 0 ? C("#2a5a6a") : bone;
                        M.Blade(hp + new Vector3(s * 0.02f, -0.17f, 0f) * U, hp + new Vector3(s * 0.04f, -0.31f, 0.01f) * U, 0.045f * U);
                    }
                    k.End();
                    var castOff = k.Model.CastOffset;
                    k.HandFlame(-1, C("#d8fff0"), glow, 0.9f);
                    var m = k.Model;
                    m.CastBone = BB.HandR; m.CastOffset = castOff;
                    m.HoldR = UnitHold.Staff; m.HoldL = UnitHold.Relaxed; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
                    break;
                }
                case GnollKind.Chief:
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        k.Pauldron(s, leatherD, 1.45f, boneD, 2);
                        M.Bone = BB.ArmU(s); M.Color = bone;
                        for (int i = 0; i < 2; i++)
                            M.Spike(k.Bind[BB.ArmU(s)] + new Vector3(s * 0.08f * U, 0.07f * U, (i - 0.5f) * 0.07f * U), new Vector3(s * 0.7f, 1f, (i - 0.5f) * 0.6f), 0.03f * U, 0.18f * U, 4);
                    }
                    // the great cleaver, two-handed, carried on the shoulder
                    k.BeginHand(1, -0.25f * U);
                    M.Color = leatherD;
                    M.Cylinder(Vector3.zero, 0.026f * U, 0.024f * U, 0.85f * U, 6);
                    M.Color = steelD;
                    M.Box(new Vector3(0f, 0.72f * U, 0.07f * U), new Vector3(0.03f * U, 0.42f * U, 0.26f * U));
                    M.Color = steel;
                    M.Box(new Vector3(0f, 0.72f * U, 0.2f * U), new Vector3(0.022f * U, 0.4f * U, 0.05f * U));
                    M.Color = bone;
                    for (int i = 0; i < 3; i++) M.Spike(new Vector3(0f, (0.58f + i * 0.13f) * U, -0.06f * U), new Vector3(0f, 0.3f, -1f), 0.018f * U, 0.08f * U, 3);
                    M.Color = C("#3a2a26");
                    M.Box(new Vector3(0f, 0.8f * U, 0.22f * U), new Vector3(0.032f * U, 0.04f * U, 0.035f * U));
                    k.End();
                    var m = k.Model;
                    m.HoldR = UnitHold.Shoulder; m.Strike = UnitStrike.Mace; m.TwoHanded = true; m.Ranged = UnitRanged.Throw;
                    m.CastBone = BB.HandR; m.CastOffset = new Vector3(0f, -0.06f * U, 0.03f * U);
                    m.Heavy = 0.35f; m.TurnRate = 420f;
                    break;
                }
            }
            M.Pop();

            // ================= legs, tail, belt and rags (not leaned)
            M.Jitter = 0.06f;
            for (int s = -1; s <= 1; s += 2)
            {
                k.Leg(s, fur, fur, muzzle, 0.18f);
                Spots(M, BB.LegU(s), spot, new Vector3(s * k.HipX, 0f, 0f), k.ThighR * 1.0f, k.ThighR, k.KneeY + 0.05f * U, k.HipY - 0.03f * U, 2, 0.028f * U, -60f, 60f);
                k.LegCuff(s, wrap, 0.35f, 1.28f, true);
                k.LegCuff(s, wrap, 0.62f, 1.26f, true);
            }
            M.Bone = BB.Tail; M.Color = fur;
            var t0 = k.Bind[BB.Tail];
            M.Curve(t0, t0 + new Vector3(0f, -0.08f, -0.1f) * U, t0 + new Vector3(0f, -0.2f, -0.14f) * U, 0.045f * U, 0.06f * U, 3, 6);
            M.Color = mane;
            M.Blob(t0 + new Vector3(0f, -0.27f, -0.15f) * U, new Vector3(0.065f, 0.1f, 0.065f) * U, 0, 0.2f, 9);
            M.Jitter = 0.04f;
            k.Belt(leatherD, bone, -1f, 1.1f, 0.055f);
            k.FrontFlap(rag, k.KneeY + 0.02f * U, chief ? 0.26f : 0.2f, ragD);
            k.BackFlap(ragD, k.KneeY + 0.04f * U, 0.22f, rag);
            if (kind == GnollKind.Brute) k.Pouch(1, leather, 0.45f, 0.9f);
            if (mystic)
            {
                k.Skirt(C("#9a7a52"), C("#6a5038"), k.KneeY - 0.02f * U, 1.3f, 26f, rag);
                M.Bone = BB.Hips;
                for (int i = 0; i < 4; i++)
                {
                    float th = (i < 2 ? -1f : 1f) * (35f + (i % 2) * 30f) * Mathf.Deg2Rad;
                    var p = new Vector3(Mathf.Sin(th) * k.HipR * 1.15f, k.HipY + 0.02f * U, Mathf.Cos(th) * k.HipR * k.DepthK * 1.15f);
                    M.Color = leatherD;
                    M.Segment(p, p + new Vector3(0f, -0.07f * U, 0f), 0.004f * U, 0.004f * U, 3);
                    M.Color = bone;
                    M.Segment(p + new Vector3(-0.025f, -0.08f, 0f) * U, p + new Vector3(0.025f, -0.08f, 0f) * U, 0.01f * U, 0.01f * U, 4);
                    M.Sphere(p + new Vector3(-0.03f, -0.08f, 0f) * U, 0.014f * U, 4, 2);
                    M.Sphere(p + new Vector3(0.03f, -0.08f, 0f) * U, 0.014f * U, 4, 2);
                }
            }
            if (chief)
            {
                M.Bone = BB.Hips;
                var skullP = new Vector3(0f, k.HipY + 0.02f * U, k.HipR * k.DepthK * 1.18f);
                M.Color = bone;
                M.Sphere(skullP, new Vector3(0.065f, 0.06f, 0.05f) * U, 7, 4);
                M.Color = C("#3a2a26");
                for (int s = -1; s <= 1; s += 2) M.Sphere(skullP + new Vector3(s * 0.024f, 0.005f, 0.042f) * U, 0.015f * U, 4, 2);
            }

            var model = k.Model;
            model.DustColor = new Color(0.86f, 0.74f, 0.52f, 0.34f);
            var headTop = Leaned(h, hc + new Vector3(0f, (chief ? 1.6f : 1.25f) * r, 0f));
            EndHunch(k, h);
            var res = Done(k, key);
            res.HeadTop = headTop - k.Bind[BB.Head];
            return res;
        }
        // ================================================================== tunnelers (mole-folk)

        /// <summary>
        /// Mudpaw tunnelers (Amberfield quarry): stout mole-folk. Velvet cocoa fur, a pink star nose on a long snout,
        /// tiny bead eyes, big pink digging paws with cream claws, patched denim-blue overalls, little boots. The miner
        /// wears a dented brass helmet with a lit candle and swings a pick; the geomancer wears a knitted cap and round
        /// spectacles, a moss shawl and a long robe, and carries a root staff crowned with glowing amber crystals, with
        /// pebbles floating over his other paw.
        /// </summary>
        static UnitModel Tunneler(string key, bool geomancer)
        {
            float H = geomancer ? 1.35f : 1.3f;
            var k = new BipedKit(geomancer ? 111 : 110, H, 0.205f, 0.3f, 1.6f, false, 1.05f, 1.08f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color fur = C("#7a5f52"), furD = C("#5e4840"), furL = C("#9c7f6e"), pink = C("#f2a0a8"), pinkD = C("#d47c88"),
                  claw = C("#f4ead2"), denim = C("#5f7894"), denimL = C("#7f98b2"), patch = C("#c8a46a"), leather = C("#6a4a32"),
                  brass = C("#d0a650"), brassD = C("#9a7638"), wax = C("#f6ecd0"), flame = C("#ffc85a"), flameCore = C("#fff6c8"),
                  robe = C("#6f8a4a"), robeD = C("#4d6436"), knit = C("#c8573e"), crystal = C("#ffb84a"), wood = C("#7a5838");
            M.Jitter = 0.05f;
            if (geomancer)
            {
                k.Torso(fur, robe, robe, 0.3f);
                k.Mantle(C("#8fae5a"), 1.25f, 0.04f, 31);
            }
            else
            {
                k.Torso(fur, denim, denim, 0.3f);
                k.ChestPanel(denim, 0.26f, true);
                M.Bone = BB.Chest; M.Color = denimL;
                for (int s = -1; s <= 1; s += 2)
                {
                    M.Segment(new Vector3(s * 0.09f * U, k.SpineY + 0.06f * U, k.ChestR * k.DepthK + 0.016f * U),
                              new Vector3(s * 0.11f * U, k.ShoulderY + 0.005f * U, 0.02f * U), 0.016f * U, 0.016f * U, 4);
                    M.Segment(new Vector3(s * 0.11f * U, k.ShoulderY + 0.005f * U, 0.02f * U),
                              new Vector3(s * 0.09f * U, k.SpineY + 0.04f * U, -k.ChestR * k.DepthK - 0.016f * U), 0.016f * U, 0.016f * U, 4);
                    M.Color = brass;
                    M.Sphere(new Vector3(s * 0.09f * U, k.SpineY + 0.06f * U, k.ChestR * k.DepthK + 0.026f * U), 0.016f * U, 5, 3);
                    M.Color = denimL;
                }
                M.Color = patch;
                M.Push().Translate(0.05f * U, k.ChestY - 0.05f * U, k.ChestR * k.DepthK + 0.02f * U).Rotate(0f, 8f, 12f);
                M.Box(Vector3.zero, new Vector3(0.07f, 0.06f, 0.01f) * U);
                M.Pop();
            }
            // ---- the mole head: a velvet dome, a long snout with a pink star nose, bead eyes, whiskers
            var hc = new Vector3(0f, k.HeadCY - 0.08f * r, 0.04f * r);
            M.Bone = BB.Head; M.Color = fur;
            M.Sphere(hc, new Vector3(1.08f, 1.0f, 1.1f) * r, 9, 6);
            M.Color = furL;
            M.Sphere(hc + new Vector3(0f, -0.4f * r, 0.5f * r), new Vector3(0.75f, 0.5f, 0.6f) * r, 7, 4);
            M.Color = C("#c49a8a");
            M.Segment(hc + new Vector3(0f, -0.15f * r, 0.75f * r), hc + new Vector3(0f, -0.32f * r, 1.55f * r), 0.42f * r, 0.2f * r, 8, true);
            var nose = hc + new Vector3(0f, -0.34f * r, 1.72f * r);
            M.Color = pink;
            M.Sphere(nose, new Vector3(0.24f, 0.22f, 0.16f) * r, 7, 4);
            M.Color = pinkD;
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0.35f).normalized;
                M.Spike(nose + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.14f * r, d, 0.065f * r, 0.2f * r, 3);
            }
            M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = hc + new Vector3(s * 0.42f * r, 0.12f * r, 0.88f * r);
                M.Color = C("#1c1416");
                M.Sphere(e, new Vector3(0.11f, 0.1f, 0.07f) * r, 5, 3);
                M.Emission = 0.7f; M.Color = Color.white;
                M.Sphere(e + new Vector3(s * 0.03f * r, 0.04f * r, 0.05f * r), 0.03f * r, 4, 2);
                M.Emission = 0f;
                M.Color = C("#f4eee2");
                for (int w = 0; w < 3; w++)
                {
                    var wb = hc + new Vector3(s * 0.28f * r, (-0.26f - w * 0.08f) * r, 1.3f * r);
                    M.Blade(wb, wb + new Vector3(s * 0.55f * r, (0.1f - w * 0.12f) * r, -0.1f * r), 0.035f * r);
                }
            }
            M.Jitter = 0.05f;
            var candleTop = Vector3.zero;
            if (geomancer)
            {
                // knitted cap with a turned-up band and a pompom, round spectacles
                M.Bone = BB.Head;
                M.Color = knit;
                M.Shell(hc + new Vector3(0f, 0.1f * r, -0.05f * r), new Vector3(1.14f, 1.08f, 1.14f) * r, -180f, 180f, 12, 0f, 62f, 3);
                M.Color = Paint.Shade(knit, 0.82f);
                M.Band(hc + new Vector3(0f, 0f, -0.05f * r), 1.16f * r, 1.12f * r, 0.5f * r, 0.68f * r, 12);
                M.Color = C("#f2e6c8");
                M.Sphere(hc + new Vector3(0f, 1.22f * r, -0.1f * r), 0.24f * r, 7, 5);
                M.Jitter = 0f;
                for (int s = -1; s <= 1; s += 2)
                {
                    var e = hc + new Vector3(s * 0.4f * r, 0.13f * r, 0.95f * r);
                    M.Push().Translate(e).Rotate(0f, s * 24f, 0f).Rotate(90f, 0f, 0f);
                    M.Color = brassD;
                    M.Torus(Vector3.zero, 0.2f * r, 0.035f * r, 10, 4);
                    M.Color = C("#cfe8f0");
                    M.Cylinder(Vector3.zero, 0.19f * r, 0.19f * r, 0.01f * r, 10);
                    M.Pop();
                    M.Color = brassD;
                    M.Segment(e + new Vector3(s * 0.18f * r, 0f, -0.06f * r), hc + new Vector3(s * 0.98f * r, 0.1f * r, 0.1f * r), 0.025f * r, 0.025f * r, 4);
                }
                M.Segment(hc + new Vector3(-0.22f * r, 0.15f * r, 1.02f * r), hc + new Vector3(0.22f * r, 0.15f * r, 1.02f * r), 0.03f * r, 0.03f * r, 4);
                M.Jitter = 0.05f;
            }
            else
            {
                // dented brass miner's helmet with a candle in a holder
                k.Helmet(brass, 1.2f, brassD);
                M.Bone = BB.Head;
                var cup = new Vector3(0f, k.HeadCY + 0.88f * r, 0.42f * r);
                M.Color = brassD;
                M.Cylinder(cup, 0.2f * r, 0.24f * r, 0.12f * r, 8);
                M.Color = wax;
                M.Cylinder(cup + new Vector3(0f, 0.1f * r, 0f), 0.13f * r, 0.13f * r, 0.5f * r, 7);
                M.Segment(cup + new Vector3(0.12f * r, 0.5f * r, 0.03f * r), cup + new Vector3(0.14f * r, 0.3f * r, 0.03f * r), 0.04f * r, 0.03f * r, 4);
                M.Color = C("#3a2a26");
                M.Segment(cup + new Vector3(0f, 0.58f * r, 0f), cup + new Vector3(0f, 0.68f * r, 0f), 0.015f * r, 0.015f * r, 3);
                M.Emission = 1f; M.Color = flame;
                M.Spike(cup + new Vector3(0f, 0.64f * r, 0f), Vector3.up, 0.11f * r, 0.42f * r, 5);
                M.Color = flameCore;
                M.Sphere(cup + new Vector3(0f, 0.72f * r, 0f), new Vector3(0.08f, 0.12f, 0.08f) * r, 5, 3);
                M.Emission = 0f;
                candleTop = cup + new Vector3(0f, 1.1f * r, 0f);
            }
            // ---- arms with big pink digging paws and claws
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, fur, fur, pink);
                if (geomancer) k.WideSleeve(s, robe, robeD, 0.22f, 1.9f, Paint.Shade(robe, 1.2f));
                else k.Cuff(s, denimL, 0.0f, 0.12f, 1.25f);
                M.Bone = BB.Hand(s);
                var hp = k.Bind[BB.Hand(s)] + new Vector3(s * 0.01f * U, -0.06f * U, 0.02f * U);
                M.Color = pink;
                M.Push().Translate(hp).Rotate(0f, 0f, s * 10f);
                M.Sphere(Vector3.zero, new Vector3(0.05f, 0.07f, 0.075f) * U, 7, 4);
                M.Pop();
                M.Color = claw;
                for (int c = -2; c <= 2; c++)
                    M.Spike(hp + new Vector3(s * 0.012f * U, -0.05f * U, c * 0.022f * U), new Vector3(s * 0.15f, -1f, c * 0.15f + 0.25f), 0.012f * U, 0.05f * U, 3);
                k.Leg(s, geomancer ? robeD : denim, geomancer ? robeD : denim, leather, 0.55f);
                if (!geomancer) k.LegCuff(s, denimL, 0.45f, 1.32f, true);
            }
            k.Belt(leather, brass, -1f, 1.1f, 0.06f);
            k.Pouch(1, leather, 0.5f, 1.1f);
            k.Pouch(-1, C("#8a6a48"), 0.35f, 0.9f);
            // a short stubby tail
            M.Bone = BB.Tail; M.Color = pinkD;
            M.Segment(k.Bind[BB.Tail], k.Bind[BB.Tail] + new Vector3(0f, -0.06f, -0.08f) * U, 0.02f * U, 0.01f * U, 4);
            var m = k.Model;
            if (geomancer)
            {
                k.Skirt(robe, robeD, k.AnkleY + 0.05f * U, 1.35f, 0f, C("#c8a04a"));
                m.SkirtClosed = 0.8f;
                // root staff crowned with amber crystals
                k.Staff(1, 1.6f, wood, BipedKit.StaffTop.Plain, wood, crystal);
                float above = 1.6f * 0.58f * U;
                k.BeginHand(1);
                M.Color = wood;
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f;
                    M.Curve(new Vector3(0f, above - 0.06f * U, 0f), new Vector3(Mathf.Cos(a) * 0.06f, above / U + 0.02f, Mathf.Sin(a) * 0.06f) * U,
                            new Vector3(Mathf.Cos(a) * 0.04f * U, above + 0.08f * U, Mathf.Sin(a) * 0.04f * U), 0.014f * U, 0.008f * U, 3, 4);
                }
                M.Emission = 0.9f; M.Color = crystal;
                var ctop = new Vector3(0f, above + 0.05f * U, 0f);
                M.Spike(ctop, Vector3.up, 0.065f * U, 0.32f * U, 5);
                M.Color = C("#ffd890");
                M.Spike(ctop + new Vector3(0.04f, 0f, 0.015f) * U, new Vector3(0.5f, 1f, 0.2f), 0.045f * U, 0.21f * U, 4);
                M.Spike(ctop + new Vector3(-0.04f, 0f, -0.015f) * U, new Vector3(-0.45f, 1f, -0.25f), 0.045f * U, 0.19f * U, 4);
                M.Color = C("#ff9a3a");
                M.Spike(ctop + new Vector3(0f, 0f, 0.04f) * U, new Vector3(0.1f, 1f, 0.6f), 0.04f * U, 0.16f * U, 4);
                M.Emission = 0f;
                k.End();
                // pebbles floating above the left paw around an amber spark
                var castOff = new Vector3(0f, -0.03f * U, above + 0.2f * U);
                M.Bone = BB.HandL;
                var lp = k.Grip(-1) + new Vector3(0f, -0.1f * U, 0.02f * U);
                M.Color = C("#8a7a6a");
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f + 0.4f;
                    M.Blob(lp + new Vector3(Mathf.Cos(a) * 0.07f, -0.02f + i * 0.03f, Mathf.Sin(a) * 0.07f) * U, new Vector3(0.028f, 0.024f, 0.028f) * U, 0, 0.25f, 40 + i);
                }
                M.Emission = 1f; M.Color = crystal;
                M.Sphere(lp, 0.03f * U, 5, 3);
                M.Emission = 0f;
                m.CastBone = BB.HandR; m.CastOffset = k.Grip(1) - k.Bind[BB.HandR] + new Vector3(0f, -castOff.y, castOff.z);
                m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            }
            else
            {
                k.FrontFlap(denim, k.HipY - 0.06f * U, 0.22f, null);
                // the pick: an ash shaft and a curved iron head (points forward and back)
                k.BeginHand(1, -0.12f * U);
                M.Color = wood;
                M.Cylinder(Vector3.zero, 0.02f * U, 0.02f * U, 0.72f * U, 6);
                M.Color = C("#8a9096");
                var ph = new Vector3(0f, 0.68f * U, 0f);
                M.Box(ph, new Vector3(0.05f, 0.06f, 0.07f) * U);
                M.Curve(ph, ph + new Vector3(0f, 0.04f, 0.14f) * U, ph + new Vector3(0f, -0.08f, 0.27f) * U, 0.03f * U, 0.006f * U, 4, 5);
                M.Curve(ph, ph + new Vector3(0f, 0.04f, -0.12f) * U, ph + new Vector3(0f, -0.06f, -0.22f) * U, 0.03f * U, 0.01f * U, 4, 5);
                k.End();
                m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Throw;
                m.CastBone = BB.HandR; m.CastOffset = new Vector3(0f, -0.06f * U, 0.03f * U);
            }
            m.DustColor = new Color(0.62f, 0.52f, 0.42f, 0.4f);
            m.StrideK = 0.9f;
            var res = Done(k, key);
            if (geomancer) res.HeadTop = hc + new Vector3(0f, 1.45f * r, 0f) - k.Bind[BB.Head];
            else res.HeadTop = candleTop - k.Bind[BB.Head];
            res.Radius = Mathf.Max(res.Radius, 0.34f);
            return res;
        }

        // ================================================================== mirelings (frog-folk)

        enum MirelingKind { Warrior, Hunter, Oracle }

        /// <summary>
        /// Mirelings (Mirefen): round little frog-folk, head and body one ball. Pond-teal skin with darker spots, a cream
        /// belly, a wide grinning mouth, big golden eyes bulging on top, a coral fin crest from the brow down the back
        /// and cheek fins, webbed hands and big webbed feet; they hop. The warrior has a barnacled driftwood club and a
        /// shell pauldron, the hunter a bone-tipped spear, a reed headband and a creel, and the oracle (violet fins) a
        /// staff topped with a conch holding a glowing pearl, a kelp shawl and a pearl necklace.
        /// </summary>
        static UnitModel Mireling(string key, MirelingKind kind)
        {
            bool oracle = kind == MirelingKind.Oracle, hunter = kind == MirelingKind.Hunter;
            float H = oracle ? 1.2f : hunter ? 1.15f : 1.1f;
            var k = new BipedKit(120 + (int)kind, H, 0.27f * H / 1.1f, 0.26f, 1.45f, false, 1.1f, 1.3f);
            float U = k.U, r = k.R, S = H / 1.1f;
            var M = k.M;
            Color skin = C("#5faa8c"), back = C("#3f8070"), spot = C("#2f6458"), belly = C("#efe3a8"), mouth = C("#2a3a32"),
                  fin = oracle ? C("#8a7ae0") : C("#f08a5a"), finTip = oracle ? C("#c8bcff") : C("#ffc27a"), iris = C("#f4c43a"),
                  kelp = C("#6a7a3a"), kelpD = C("#4a5a2a"), shell = C("#f2dcc8"), shellD = C("#d8a890"), wood = C("#8a6a48"),
                  bone = C("#efe4c8"), pearl = C("#c8f6ff");
            M.Jitter = 0.04f;
            var hc = new Vector3(0f, k.HeadCY - 0.25f * r, 0f);
            // hips and the one round head-body
            M.Bone = BB.Hips; M.Color = back;
            M.Sphere(new Vector3(0f, k.PelvisY + 0.02f * S, -0.02f * S), new Vector3(0.22f, 0.17f, 0.2f) * S, 8, 5);
            M.Color = belly;
            M.Sphere(new Vector3(0f, k.PelvisY + 0.0f, 0.06f * S), new Vector3(0.17f, 0.13f, 0.14f) * S, 7, 4);
            M.Bone = BB.Head; M.Color = skin;
            M.Sphere(hc, new Vector3(1.22f, 1.0f, 1.08f) * r, 11, 7);
            M.Color = back;
            M.Shell(hc + new Vector3(0f, 0.02f * r, -0.04f * r), new Vector3(1.23f, 1.01f, 1.09f) * r, 110f, 250f, 8, 0f, 95f, 4);
            M.Color = belly;
            M.Sphere(hc + new Vector3(0f, -0.42f * r, 0.42f * r), new Vector3(0.95f, 0.62f, 0.7f) * r, 9, 5);
            Spots(M, BB.Head, spot, hc, 1.2f * r, 1.06f * r, 0.15f * r, 0.6f * r, 6, 0.12f * r, 110f, 250f);
            Spots(M, BB.Head, spot, hc, 1.18f * r, 1.04f * r, -0.1f * r, 0.3f * r, 4, 0.1f * r, 70f, 105f);
            Spots(M, BB.Head, spot, hc, 1.18f * r, 1.04f * r, -0.1f * r, 0.3f * r, 4, 0.1f * r, -105f, -70f);
            // the wide mouth: a dark grin from cheek to cheek with a hint of tongue
            M.Jitter = 0f;
            M.Bone = BB.Head; M.Color = mouth;
            M.Curve(hc + new Vector3(-0.92f * r, -0.02f * r, 0.62f * r), hc + new Vector3(0f, -0.42f * r, 1.24f * r), hc + new Vector3(0.92f * r, -0.02f * r, 0.62f * r), 0.06f * r, 0.06f * r, 6, 4);
            M.Color = C("#e8787a");
            M.Sphere(hc + new Vector3(0.15f * r, -0.22f * r, 1.02f * r), new Vector3(0.18f, 0.06f, 0.08f) * r, 5, 3);
            // big golden eyes bulging on top, horizontal pupils
            for (int s = -1; s <= 1; s += 2)
            {
                var e = hc + new Vector3(s * 0.5f * r, 0.78f * r, 0.42f * r);
                M.Color = skin;
                M.Sphere(e, new Vector3(0.36f, 0.34f, 0.34f) * r, 8, 5);
                M.Push().Translate(e + new Vector3(s * 0.04f * r, 0.04f * r, 0.12f * r)).Rotate(-10f, s * 26f, 0f);
                M.Color = iris; M.Emission = 0.15f;
                M.Sphere(Vector3.zero, new Vector3(0.29f, 0.28f, 0.27f) * r, 8, 5);
                M.Emission = 0f; M.Color = C("#1c1a18");
                M.Box(new Vector3(0f, 0f, 0.26f * r), new Vector3(0.3f, 0.09f, 0.05f) * r);
                M.Emission = 0.7f; M.Color = Color.white;
                M.Sphere(new Vector3(s * 0.08f * r, 0.12f * r, 0.24f * r), 0.06f * r, 4, 2);
                M.Emission = 0f;
                M.Pop();
                M.Color = back;
                M.Shell(e + new Vector3(0f, 0.02f * r, -0.02f * r), new Vector3(0.38f, 0.36f, 0.36f) * r, -180f, 180f, 8, 0f, 40f, 2);
            }
            // fin crest from between the eyes over the head and down the back, cheek fins
            M.Jitter = 0.03f;
            for (int i = 0; i < 7; i++)
            {
                float ph = Mathf.Lerp(28f, 150f, i / 6f) * Mathf.Deg2Rad;
                var n = new Vector3(0f, Mathf.Cos(ph), -Mathf.Sin(ph) * 0.9f + 0.25f).normalized;
                var p = hc + new Vector3(0f, Mathf.Cos(ph) * 0.96f * r, Mathf.Sin(-ph) * 1.0f * r + 0.35f * r * Mathf.Cos(ph));
                float len = (0.6f + 0.35f * Mathf.Sin(i / 6f * Mathf.PI)) * r;
                M.Color = fin;
                M.Blade(p - n * 0.05f * r, p + n * len + new Vector3(0f, 0f, -0.2f * r), 0.4f * r, Vector3.forward);
                M.Color = finTip;
                M.Spike(p, (n + new Vector3(0f, 0f, -0.3f)).normalized, 0.04f * r, len * 1.15f, 3);
            }
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 3; i++)
                {
                    var p = hc + new Vector3(s * 1.12f * r, (0.1f - i * 0.18f) * r, -0.1f * r);
                    M.Color = i == 1 ? finTip : fin;
                    M.Blade(p, p + new Vector3(s * 0.42f * r, (0.2f - i * 0.18f) * r, -0.35f * r), 0.2f * r);
                }
            // webbed arms and feet
            M.Jitter = 0.04f;
            for (int s = -1; s <= 1; s += 2)
            {
                M.Bone = BB.ArmU(s); M.Color = skin;
                M.Segment(k.Bind[BB.ArmU(s)], k.Bind[BB.ArmL(s)], 0.05f * S, 0.042f * S, 6);
                M.Bone = BB.ArmL(s);
                M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.042f * S, 0.036f * S, 6);
                M.Bone = BB.Hand(s);
                var hp = k.Bind[BB.Hand(s)] + new Vector3(0f, -0.035f * S, 0.01f * S);
                M.Sphere(hp, new Vector3(0.045f, 0.05f, 0.05f) * S, 6, 4);
                M.Color = belly;
                for (int f = -1; f <= 1; f++)
                    M.Sphere(hp + new Vector3(s * 0.01f * S, -0.05f * S, f * 0.03f * S), 0.017f * S, 4, 2);
                M.Bone = BB.LegU(s); M.Color = back;
                M.Segment(k.Bind[BB.LegU(s)] + Vector3.up * 0.02f * S, k.Bind[BB.LegL(s)], 0.07f * S, 0.055f * S, 6);
                M.Bone = BB.LegL(s); M.Color = skin;
                M.Segment(k.Bind[BB.LegL(s)], k.Bind[BB.Foot(s)], 0.05f * S, 0.04f * S, 6);
                M.Bone = BB.Foot(s);
                var fp = new Vector3(k.Bind[BB.Foot(s)].x + s * 0.01f * S, 0.025f * S, 0.05f * S);
                M.Sphere(fp, new Vector3(0.075f, 0.03f, 0.1f) * S, 7, 3);
                M.Color = belly;
                for (int t = -1; t <= 1; t++)
                    M.Sphere(fp + new Vector3(t * 0.045f * S, 0.0f, 0.09f * S - Mathf.Abs(t) * 0.02f * S), 0.022f * S, 4, 2);
            }
            // kelp loincloth on the hips
            M.Bone = BB.Hips;
            for (int i = 0; i < 7; i++)
            {
                float th = Mathf.Lerp(-80f, 80f, i / 6f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(th) * 0.2f * S, k.PelvisY - 0.02f * S, Mathf.Cos(th) * 0.17f * S);
                M.Color = i % 2 == 0 ? kelp : kelpD;
                M.Blade(p, p + new Vector3(Mathf.Sin(th) * 0.04f, -0.13f - (i % 3) * 0.03f, Mathf.Cos(th) * 0.04f) * S, 0.055f * S);
            }
            var m = k.Model;
            switch (kind)
            {
                case MirelingKind.Warrior:
                {
                    // a scallop-shell pauldron and a barnacled driftwood club
                    M.Bone = BB.ArmU(-1);
                    var sp = k.Bind[BB.ArmU(-1)] + new Vector3(-0.03f, 0.03f, 0f) * S;
                    M.Push().Translate(sp).Rotate(0f, 0f, 30f);
                    M.Color = shell;
                    M.Shell(Vector3.zero, new Vector3(0.09f, 0.06f, 0.09f) * S, -180f, 180f, 9, 0f, 80f, 2);
                    M.Color = shellD;
                    for (int i = -2; i <= 2; i++)
                        M.Segment(Vector3.zero + new Vector3(0f, 0.062f * S, 0f), new Vector3(Mathf.Sin(i * 0.5f) * 0.09f, 0.0f, Mathf.Cos(i * 0.5f) * 0.09f) * S, 0.007f * S, 0.007f * S, 3);
                    M.Pop();
                    k.BeginHand(1, -0.06f * U);
                    M.Color = wood;
                    M.Cylinder(Vector3.zero, 0.026f * U, 0.075f * U, 0.68f * U, 7);
                    M.Sphere(new Vector3(0f, 0.68f * U, 0f), new Vector3(0.075f, 0.05f, 0.075f) * U, 7, 3);
                    M.Color = C("#e8e2d2");
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 1.7f, y = 0.42f + i * 0.05f;
                        float rr = Mathf.Lerp(0.026f, 0.075f, y / 0.68f);
                        M.Sphere(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr) * U, 0.024f * U, 5, 3);
                    }
                    M.Color = kelp;
                    M.Blade(new Vector3(0f, 0.55f, 0.06f) * U, new Vector3(0.03f, 0.32f, 0.12f) * U, 0.045f * U);
                    k.End();
                    m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Throw;
                    break;
                }
                case MirelingKind.Hunter:
                {
                    // reed headband with a feather, a woven creel on the back, a bone-tipped spear
                    M.Bone = BB.Head; M.Color = C("#c8b07a");
                    M.Push().Translate(hc + new Vector3(0f, 0.3f * r, 0f)).Rotate(-8f, 0f, 0f);
                    M.Torus(Vector3.zero, 1.1f * r, 0.06f * r, 14, 3);
                    M.Pop();
                    M.Color = C("#f2ead6");
                    M.Blade(hc + new Vector3(0.95f * r, 0.4f * r, -0.5f * r), hc + new Vector3(1.3f * r, 1.1f * r, -1.0f * r), 0.16f * r);
                    M.Bone = BB.Head;
                    var cb = hc + new Vector3(0f, -0.35f * r, -1.12f * r);
                    M.Color = C("#b8945a");
                    M.Cylinder(cb + new Vector3(0f, -0.2f * r, 0f), 0.38f * r, 0.45f * r, 0.62f * r, 8);
                    M.Color = C("#8a6a3a");
                    M.Band(cb, 0.46f * r, 0.46f * r, 0.0f, 0.06f * r, 8);
                    M.Band(cb, 0.42f * r, 0.42f * r, -0.18f * r, -0.12f * r, 8);
                    M.Color = C("#8fb6c8");
                    M.Blade(cb + new Vector3(0.1f * r, 0.4f * r, 0f), cb + new Vector3(0.2f * r, 0.75f * r, 0.05f * r), 0.18f * r);
                    k.Staff(1, 1.35f, wood, BipedKit.StaffTop.Plain, bone, bone);
                    float above = 1.35f * 0.58f * U;
                    k.BeginHand(1);
                    M.Color = bone;
                    M.Push().Translate(0f, above, 0f).Scale(new Vector3(0.04f * U, 1f, 0.016f * U));
                    M.Lathe(new[] { new Vector2(0.5f, 0f), new Vector2(1f, 0.06f * U), new Vector2(0f, 0.22f * U) }, 4, false, true, false);
                    M.Pop();
                    M.Color = C("#c8b07a");
                    M.Cylinder(new Vector3(0f, above - 0.04f * U, 0f), 0.026f * U, 0.026f * U, 0.05f * U, 6);
                    M.Color = fin;
                    M.Blade(new Vector3(0f, above - 0.02f * U, 0f), new Vector3(0.03f, above / U - 0.18f, -0.04f) * U, 0.05f * U);
                    k.End();
                    m.HoldR = UnitHold.Spear; m.Strike = UnitStrike.Spear; m.Ranged = UnitRanged.Throw;
                    break;
                }
                case MirelingKind.Oracle:
                {
                    // kelp shawl, pearl necklace, a staff topped by a conch cradling a glowing pearl
                    M.Bone = BB.Head;
                    for (int i = 0; i < 9; i++)
                    {
                        float th = Mathf.Lerp(-150f, 150f, i / 8f) * Mathf.Deg2Rad;
                        var p = hc + new Vector3(Mathf.Sin(th) * 1.18f * r, -0.15f * r, Mathf.Cos(th) * 1.04f * r);
                        M.Color = i % 2 == 0 ? kelp : kelpD;
                        M.Blade(p, p + new Vector3(Mathf.Sin(th) * 0.2f * r, -0.75f * r, Mathf.Cos(th) * 0.2f * r), 0.3f * r);
                    }
                    M.Beads(hc + new Vector3(-0.85f * r, -0.5f * r, 0.75f * r), hc + new Vector3(0.85f * r, -0.5f * r, 0.75f * r), 7, 0.08f * r, 0.08f * r, C("#f4f0f8"), C("#d8c8f0"));
                    k.Staff(1, 1.4f, C("#7a6a5a"), BipedKit.StaffTop.Plain, shell, pearl);
                    float above = 1.4f * 0.58f * U;
                    k.BeginHand(1);
                    var top = new Vector3(0f, above, 0f);
                    const float conchK = 1.7f;
                    M.Push().Translate(top).Scale(conchK).Translate(-top);
                    M.Color = shell;
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 1.1f, t = i / 5f;
                        var p = top + new Vector3(Mathf.Cos(a) * 0.05f * (1f - t), 0.03f + t * 0.16f, Mathf.Sin(a) * 0.05f * (1f - t) - 0.03f) * U;
                        M.Color = i % 2 == 0 ? shell : shellD;
                        M.Sphere(p, (0.055f - t * 0.04f) * U, 6, 4);
                    }
                    M.Color = shellD;
                    M.Spike(top + new Vector3(0f, 0.2f, -0.03f) * U, Vector3.up, 0.012f * U, 0.06f * U, 4);
                    M.Emission = 1f; M.Color = pearl;
                    var pp = top + new Vector3(0f, 0.08f, 0.06f) * U;
                    M.Sphere(pp, 0.04f * U, 7, 5);
                    M.Emission = 0f;
                    M.Pop();
                    k.End();
                    pp = top + (pp - top) * conchK;
                    m.CastBone = BB.HandR;
                    m.CastOffset = k.Grip(1) - k.Bind[BB.HandR] + new Vector3(pp.x, -pp.z, pp.y);
                    m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
                    break;
                }
            }
            m.DustColor = new Color(0.55f, 0.62f, 0.5f, 0.3f);
            var res = Done(k, key, UnitGait.Small);
            res.HeadTop = hc + new Vector3(0f, 1.45f * r, 0f) - k.Bind[BB.Head];
            res.Radius = 0.34f * S;
            return res;
        }

        // ================================================================== the mire hag

        /// <summary>
        /// Mire hag (Mother Mire's coven, Mirefen): a crooked bog witch bent far forward. Grey-green skin, a long hooked
        /// nose with a wart, a pointed chin, stringy moss-green hair, a droopy wide-brimmed hat with a little frog on the
        /// brim, a cloak of dry reeds over a ragged plum dress, a gnarled crook in one hand and a lantern of soft
        /// marsh-light in the other (the cast anchor).
        /// </summary>
        static UnitModel MireHag(string key)
        {
            const float H = 1.85f;
            var k = new BipedKit(130, H, 0.145f, 0.46f, 0.92f, true, 0.95f, 1.18f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color skin = C("#9aae86"), skinD = C("#7a9068"), hair = C("#6f8062"), moss = C("#7d9a4a"), dress = C("#5a3e5a"),
                  dressD = C("#3e2a40"), reed = C("#c2a862"), reedD = C("#8f8a4a"), reedL = C("#dcc884"), hat = C("#3e4a3a"),
                  hatBand = C("#a8522e"), wood = C("#5e4632"), iron = C("#3a3436"), glow = C("#e6f080"), frog = C("#6fb04a");
            var h = MakeHunch(k, 21f, new Vector3(0f, -0.02f * U, 0.05f * U));
            var hc = new Vector3(0f, k.HeadCY, 0f) + h.HeadOff;

            BeginLean(k, h);
            M.Jitter = 0.05f;
            k.Torso(dress, dressD);
            k.Neck(skin);
            // a shawl of reeds around the shoulders
            M.Bone = BB.Chest;
            for (int i = 0; i < 14; i++)
            {
                float th = i / 14f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Sin(th) * k.ShoulderR * 1.0f, k.ShoulderY + 0.02f * U, Mathf.Cos(th) * k.ShoulderR * k.DepthK * 1.1f);
                M.Color = i % 3 == 0 ? reedL : i % 3 == 1 ? reed : reedD;
                M.Blade(p, p + new Vector3(Mathf.Sin(th) * 0.09f, -0.2f - (i % 2) * 0.05f, Mathf.Cos(th) * 0.08f) * U, 0.06f * U);
            }
            // the head (moved forward on the bent neck)
            M.Push().Translate(h.HeadOff);
            k.Head(skin, C("#e8b030"), hair, EyeStyle.Narrow, 16f, true, 0.85f);
            M.Bone = BB.Head;
            // long hooked nose with a wart, a pointed chin
            var n0 = k.FacePoint(0f, -0.12f, -0.05f);
            M.Color = skinD;
            M.Curve(n0, n0 + new Vector3(0f, 0.05f * r, 0.55f * r), n0 + new Vector3(0f, -0.4f * r, 0.75f * r), 0.16f * r, 0.05f * r, 5, 6);
            M.Color = C("#8a7a5a");
            M.Sphere(n0 + new Vector3(0.08f * r, -0.02f * r, 0.42f * r), 0.06f * r, 4, 3);
            M.Color = skin;
            M.Spike(k.FacePoint(0f, -0.72f, -0.15f), new Vector3(0f, -0.6f, 1f), 0.2f * r, 0.3f * r, 5);
            M.Color = C("#5a3a3a");
            M.Box(k.FacePoint(0f, -0.52f, 0.02f), new Vector3(0.36f, 0.04f, 0.04f) * r);
            k.Cheeks(C("#b8a07a"), 0.8f);
            // stringy hair with moss, under a droopy hat with a little frog
            k.HairCap(hair, 60f, 100f, 125f);
            k.LongBack(hair, k.ShoulderY - 0.12f * U, 1.1f, 1.3f);
            k.SideLocks(hair, k.ShoulderY - 0.02f * U, 0.22f);
            M.Bone = BB.Head; M.Color = moss;
            M.Blob(new Vector3(0.55f * r, k.HeadCY + 0.1f * r, -0.6f * r), new Vector3(0.22f, 0.18f, 0.2f) * r, 0, 0.3f, 11);
            M.Blob(new Vector3(-0.7f * r, k.HeadCY - 0.5f * r, -0.4f * r), new Vector3(0.18f, 0.2f, 0.18f) * r, 0, 0.3f, 12);
            k.BrimHat(hat, hat, 2.5f, 2.7f, 1.0f, hatBand, -12f);
            M.Bone = BB.Head;
            var fpos = new Vector3(-1.6f * r, k.HeadCY + 0.6f * r, 0.9f * r);
            M.Color = frog;
            M.Sphere(fpos, new Vector3(0.28f, 0.2f, 0.26f) * r, 7, 4);
            for (int s = -1; s <= 1; s += 2)
            {
                M.Sphere(fpos + new Vector3(s * 0.12f * r, 0.16f * r, 0.08f * r), 0.09f * r, 5, 3);
                M.Color = C("#1c1a18");
                M.Sphere(fpos + new Vector3(s * 0.13f * r, 0.18f * r, 0.15f * r), 0.045f * r, 4, 2);
                M.Color = frog;
            }
            M.Pop();
            M.Pop();

            // reed cloak hanging from the shoulders down the back (Cape bone: it sways)
            M.Push(); M.Matrix = M.Matrix * h.Body;
            k.Cape(reedD, C("#5a5230"), k.KneeY - 0.02f * U, 1.25f, reed);
            M.Bone = BB.Cape;
            for (int row = 0; row < 2; row++)
            {
                int n = 9;
                for (int i = 0; i < n; i++)
                {
                    float th = Mathf.Lerp(80f, 280f, i / (float)(n - 1)) * Mathf.Deg2Rad;
                    float y0 = k.ShoulderY - row * 0.24f * U, rr = k.ShoulderR * (1.2f + row * 0.18f);
                    var p = new Vector3(Mathf.Sin(th) * rr, y0, Mathf.Cos(th) * rr * k.DepthK * 1.2f);
                    float len = (0.32f + row * 0.12f + ((i * 7 + row) % 3) * 0.05f) * U;
                    M.Color = (i + row) % 3 == 0 ? reedL : (i + row) % 3 == 1 ? reed : reedD;
                    M.Blade(p, p + new Vector3(Mathf.Sin(th) * 0.1f * U, -len, Mathf.Cos(th) * 0.1f * U), 0.1f * U);
                }
            }
            M.Pop();

            // arms: ragged sleeves, bony green hands; the crook and the lantern
            BeginArms(k, h);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, dress, skin, skin);
                k.WideSleeve(s, dressD, dress, 0.26f, 2.0f, reedD);
            }
            k.Staff(1, 1.7f, wood, BipedKit.StaffTop.Crook, wood, glow);
            float above = 1.7f * 0.58f * U;
            k.BeginHand(1);
            M.Color = reedL;
            M.Beads(new Vector3(0f, above + 0.1f * U, 0.1f * U), new Vector3(0.02f, above / U - 0.05f, 0.12f) * U, 3, 0.016f * U, 0.012f * U, reedL, C("#d8e0f0"));
            M.Color = C("#efe4c8");
            M.Blade(new Vector3(0.02f, above / U - 0.05f, 0.12f) * U, new Vector3(0.03f, above / U - 0.14f, 0.13f) * U, 0.035f * U);
            k.End();
            // the marsh lantern, hanging from its bail in the left hand (hand frame: +Y up, +Z forward)
            k.BeginHand(-1, 0f);
            M.Color = iron;
            M.Torus(new Vector3(0f, 0f, 0.03f * U), 0.03f * U, 0.006f * U, 8, 3);
            var lp = new Vector3(0f, -0.11f * U, 0.05f * U);
            M.Cylinder(lp + new Vector3(0f, 0.055f * U, 0f), 0.05f * U, 0.02f * U, 0.04f * U, 6);
            M.Box(lp + new Vector3(0f, -0.065f * U, 0f), new Vector3(0.09f, 0.018f, 0.09f) * U);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                M.Box(lp + new Vector3(Mathf.Cos(a) * 0.038f, 0f, Mathf.Sin(a) * 0.038f) * U, new Vector3(0.01f, 0.11f, 0.01f) * U);
            }
            M.Emission = 1f; M.Color = glow;
            M.Box(lp, new Vector3(0.064f, 0.095f, 0.064f) * U);
            M.Emission = 0f;
            k.End();
            M.Pop();

            // legs under a ragged dress
            for (int s = -1; s <= 1; s += 2) k.Leg(s, dressD, skin, C("#4a3a2a"), 0.4f);
            k.Skirt(dress, dressD, k.AnkleY + 0.08f * U, 1.45f, 0f, dressD);
            M.Bone = BB.SkirtB;
            for (int i = 0; i < 6; i++)
            {
                float th = Mathf.Lerp(110f, 250f, i / 5f) * Mathf.Deg2Rad;
                float rr = k.HipR * 1.4f;
                var p = new Vector3(Mathf.Sin(th) * rr, k.AnkleY + 0.1f * U, Mathf.Cos(th) * rr * k.DepthK * 1.2f);
                M.Color = i % 2 == 0 ? dress : dressD;
                M.Blade(p, p + new Vector3(0f, -0.08f - (i % 3) * 0.03f, 0f) * U, 0.06f * U);
            }
            k.Belt(C("#4a3a2a"), C("#c8b07a"), -1f, 1.1f, 0.04f);
            M.Bone = BB.Hips;
            M.Color = C("#8a7a4a");
            M.Sphere(new Vector3(k.HipR * 0.9f, k.HipY - 0.02f * U, 0.05f * U), new Vector3(0.05f, 0.07f, 0.04f) * U, 6, 4);
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.HoldL = UnitHold.Carry; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            m.SkirtClosed = 0.7f;
            m.CastBone = BB.HandL;
            m.CastOffset = k.Grip(-1) - k.Bind[BB.HandL] + new Vector3(lp.x, -lp.z, lp.y);
            m.StrideK = 0.8f; m.MaxCadence = 2.4f;
            m.DustColor = new Color(0.5f, 0.55f, 0.42f, 0.3f);
            var headTop = Leaned(h, new Vector3(0f, k.HeadCY + 2.4f * r, 0f) + h.HeadOff);
            EndHunch(k, h);
            var res = Done(k, key);
            res.HeadTop = headTop - k.Bind[BB.Head];
            return res;
        }

        // ================================================================== ogres

        /// <summary>
        /// Skyreach ogres: 3 m tall, huge round belly and shoulders, a small head with a heavy brow, a big nose, an
        /// underbite with two tusks and a black topknot; lilac-blue skin reads against snow and grass alike. The ogre
        /// wears a brown bear pelt and a hide loincloth and swings a studded club; the ogre mage wears plum robes with
        /// gold trim, a cream turban with a ruby and a plume, gold earrings, and carries a staff with a big glowing orb.
        /// </summary>
        static UnitModel Ogre(string key, bool mage)
        {
            const float H = 3.0f;
            var k = new BipedKit(mage ? 141 : 140, H, 0.27f, 0.4f, 1.85f, false, 1.3f, 1.22f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color skin = C("#8f94c0"), skinL = C("#b4b6d8"), skinD = C("#6c709c"), hair = C("#2a2632"), tusk = C("#f4ecd4"),
                  pelt = C("#7a5a40"), peltL = C("#9a7a58"), hide = C("#a88a64"), hideD = C("#7a6248"), rope = C("#c8b07a"),
                  wood = C("#7a5636"), iron = C("#5a5e66"), robe = C("#6a3a6e"), robeD = C("#4a2850"), gold = C("#e0b44e"),
                  turban = C("#efe2c2"), orb = C("#8ad8ff");
            M.Jitter = 0.05f;
            var bellyC = Paint.Mix(skin, skinL, 0.4f);
            k.Torso(mage ? robe : skin, mage ? robeD : hideD, mage ? robe : bellyC, 0.42f);
            if (!mage)
            {
                // big round belly with a navel
                M.Bone = BB.Spine; M.Color = bellyC;
                M.Sphere(new Vector3(0f, k.SpineY - 0.02f * U, 0.05f * U), new Vector3(k.WaistR * 1.42f, 0.17f * U, k.WaistR * 1.22f), 10, 6);
                M.Color = skinD;
                M.Sphere(new Vector3(0f, k.SpineY - 0.02f * U, k.WaistR * 1.3f + 0.04f * U), new Vector3(0.016f, 0.02f, 0.01f) * U, 4, 2);
            }
            k.Neck(skin);
            // head: heavy brow, small eyes, big nose, underbite and tusks, topknot
            k.Head(skin, C("#f0c040"), hair, EyeStyle.Narrow, 22f, true, 1.25f);
            k.BushyBrows(hair, 0.16f, 0.36f, 0.25f, 1.2f);
            k.Nose(skinD, 2.1f, -0.28f);
            M.Bone = BB.Head; M.Color = skinD;
            M.Sphere(k.FacePoint(0f, -0.66f, -0.25f), new Vector3(0.62f, 0.3f, 0.4f) * r, 8, 4);
            M.Color = tusk;
            for (int s = -1; s <= 1; s += 2)
                M.Spike(k.FacePoint(s * 0.34f, -0.62f, -0.05f), new Vector3(s * 0.15f, 1f, 0.15f), 0.08f * r, 0.42f * r, 4);
            M.Color = C("#3a2a2a");
            M.Box(k.FacePoint(0f, -0.52f, 0.0f), new Vector3(0.4f, 0.045f, 0.04f) * r);
            if (mage)
            {
                // turban wound in rolls, a ruby, a plume; gold earrings
                M.Bone = BB.Head;
                for (int i = 0; i < 3; i++)
                {
                    M.Color = i == 1 ? Paint.Shade(turban, 0.9f) : turban;
                    M.Push().Translate(0f, k.HeadCY + (0.42f + i * 0.26f) * r, -0.04f * r).Rotate(-8f + i * 6f, 0f, (i - 1) * 6f);
                    M.Torus(Vector3.zero, (1.0f - i * 0.2f) * r, (0.22f - i * 0.03f) * r, 12, 5);
                    M.Pop();
                }
                M.Sphere(new Vector3(0f, k.HeadCY + 0.95f * r, -0.04f * r), new Vector3(0.55f, 0.35f, 0.55f) * r, 7, 4);
                M.Emission = 0.6f; M.Color = C("#e83a4a");
                M.Sphere(new Vector3(0f, k.HeadCY + 0.62f * r, 1.12f * r), new Vector3(0.16f, 0.2f, 0.1f) * r, 6, 4);
                M.Emission = 0f; M.Color = gold;
                M.Torus(new Vector3(0f, k.HeadCY + 0.62f * r, 1.08f * r), 0.2f * r, 0.04f * r, 8, 3);
                M.Color = C("#5ab0d8");
                M.Blade(new Vector3(0f, k.HeadCY + 0.8f * r, 1.0f * r), new Vector3(0.1f * r, k.HeadCY + 1.75f * r, 0.7f * r), 0.24f * r);
                M.Color = gold;
                for (int s = -1; s <= 1; s += 2)
                {
                    M.Push().Translate(s * 0.98f * r, k.HeadCY - 0.4f * r, -0.04f * r).Rotate(0f, 0f, 90f);
                    M.Torus(Vector3.zero, 0.16f * r, 0.035f * r, 8, 3);
                    M.Pop();
                }
                k.Collar(robeD, gold, 0.1f, 50f, 1.5f);
                k.Sash(gold, k.PelvisY + 0.02f * U, 0.07f, true, gold);
            }
            else
            {
                // black topknot with a rope tie and sideburns
                M.Bone = BB.Head; M.Color = hair;
                M.Sphere(new Vector3(0f, k.HeadCY + 0.82f * r, -0.25f * r), new Vector3(0.36f, 0.4f, 0.36f) * r, 7, 4);
                M.Curve(new Vector3(0f, k.HeadCY + 1.05f * r, -0.3f * r), new Vector3(0f, k.HeadCY + 1.35f * r, -0.5f * r), new Vector3(0f, k.HeadCY + 1.0f * r, -0.95f * r), 0.18f * r, 0.08f * r, 4, 5);
                M.Color = rope;
                M.Band(new Vector3(0f, 0f, -0.27f * r), 0.3f * r, 0.3f * r, k.HeadCY + 1.0f * r, k.HeadCY + 1.1f * r, 7);
                // bear pelt over the shoulders: a fur ring and the head of the pelt on the left shoulder
                k.Mantle(pelt, 2.1f, 0.03f, 41);
                M.Bone = BB.Chest; M.Color = peltL;
                var ph = new Vector3(-k.ShoulderX * 0.9f, k.ShoulderY + 0.06f * U, 0.03f * U);
                M.Sphere(ph, new Vector3(0.11f, 0.08f, 0.12f) * U, 7, 4);
                M.Color = pelt;
                M.Sphere(ph + new Vector3(0f, -0.01f, 0.1f) * U, new Vector3(0.05f, 0.04f, 0.06f) * U, 6, 3);
                for (int s = -1; s <= 1; s += 2) M.Sphere(ph + new Vector3(s * 0.07f, 0.06f, -0.02f) * U, 0.03f * U, 5, 3);
                M.Color = C("#2a2222");
                M.Sphere(ph + new Vector3(0f, 0.0f, 0.16f) * U, 0.017f * U, 4, 2);
                k.Strap(pelt, -1, 0.11f);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, mage ? robe : skin, skin, skin);
                if (mage) k.WideSleeve(s, robe, gold, 0.3f, 2.1f, gold);
                else
                {
                    k.Cuff(s, rope, 0.55f, 0.95f, 1.18f);
                    k.ArmBand(s, C("#a8aeb8"), 0.25f, 1.15f, 0.04f);
                }
                k.Leg(s, mage ? robeD : hideD, skin, skinD, 0.15f);
                k.LegCuff(s, mage ? robeD : peltL, 0.2f, 1.4f, true);
            }
            k.Belt(mage ? gold : rope, mage ? C("#e83a4a") : C("#a8aeb8"), -1f, 1.06f, 0.06f);
            var m = k.Model;
            if (mage)
            {
                k.Skirt(robe, robeD, k.AnkleY + 0.06f * U, 1.4f, 0f, gold);
                k.FrontFlap(robeD, k.AnkleY + 0.1f * U, 0.17f, gold, -1f, 0.06f * U);
                k.ChestPanel(robeD, 0.2f, false, gold);
                m.SkirtClosed = 0.8f;
                k.Staff(1, 1.55f, C("#4a3a5a"), BipedKit.StaffTop.Orb, gold, orb, 0.1f);
                k.HandFlame(-1, C("#e8f8ff"), orb, 1.1f);
                m.CastBone = BB.HandR;
                float above = 1.55f * 0.58f * U;
                m.CastOffset = k.Grip(1) - k.Bind[BB.HandR] + new Vector3(0f, 0f, above + 0.08f * U);
                m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
                m.Heavy = 0.6f;
            }
            else
            {
                k.FrontFlap(hide, k.KneeY + 0.03f * U, 0.26f, hideD);
                k.BackFlap(hideD, k.KneeY + 0.02f * U, 0.3f, hide);
                // a big studded club
                k.BeginHand(1, -0.14f * U);
                M.Color = wood;
                M.Cylinder(Vector3.zero, 0.03f * U, 0.085f * U, 0.72f * U, 7);
                M.Sphere(new Vector3(0f, 0.72f * U, 0f), new Vector3(0.085f, 0.06f, 0.085f) * U, 7, 3);
                M.Color = iron;
                M.Band(Vector3.zero, 0.075f * U, 0.08f * U, 0.5f * U, 0.56f * U, 7);
                for (int i = 0; i < 7; i++)
                {
                    float a = i * 2.4f, y = 0.38f + (i % 3) * 0.1f;
                    float rr = Mathf.Lerp(0.03f, 0.085f, y / 0.72f);
                    M.Spike(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr) * U, new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)), 0.016f * U, 0.05f * U, 4);
                }
                M.Color = rope;
                M.Band(Vector3.zero, 0.034f * U, 0.034f * U, 0.0f, 0.1f * U, 6);
                k.End();
                m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Throw;
                m.CastBone = BB.HandR; m.CastOffset = new Vector3(0f, -0.06f * U, 0.03f * U);
                m.Heavy = 0.8f;
            }
            m.MaxCadence = 1.6f; m.TurnRate = 300f;
            m.DustColor = new Color(0.8f, 0.76f, 0.7f, 0.42f);
            var res = Done(k, key, UnitGait.Heavy);
            if (mage) res.HeadTop = new Vector3(0f, k.HeadCY + 1.5f * r, 0f) - k.Bind[BB.Head];
            return res;
        }

        // ================================================================== the Dragonsworn

        /// <summary>
        /// Dragonsworn (the Ashwyrm's cultists, Skyreach and the roost): a cultist in crimson-and-bronze scale mail, a
        /// horned bronze helm with a dark visor and glowing ember eye slits, a dragon-scale pauldron, an ash-black cape
        /// with an ember hem, a black tabard with a burning eye, and a greatsword whose fuller glows like a coal.
        /// </summary>
        static UnitModel Dragonsworn(string key)
        {
            const float H = 1.95f;
            var k = new BipedKit(150, H, 0.14f, 0.49f, 1.18f, false, 1.12f);
            float U = k.U, r = k.R;
            var M = k.M;
            Color scale = C("#9a3430"), scaleD = C("#6a2226"), bronze = C("#c08a44"), bronzeD = C("#7a5636"), black = C("#2e2a30"),
                  ash = C("#3e383c"), ember = C("#ff8a3a"), emberD = C("#c2482a"), horn = C("#2a2224"), hornTip = C("#e8d8b8"),
                  steel = C("#8e939c"), skin = C("#d9a882");
            M.Jitter = 0.04f;
            k.Torso(scaleD, black);
            // overlapping scales in rows over chest and belly
            M.Jitter = 0f;
            for (int row = 0; row < 6; row++)
            {
                bool chest = row < 4;
                int bone = chest ? BB.Chest : BB.Spine;
                float y = chest ? Mathf.Lerp(k.ShoulderY - 0.06f * U, k.SpineY + 0.04f * U, row / 3f) : Mathf.Lerp(k.SpineY - 0.02f * U, k.PelvisY + 0.03f * U, row - 4);
                float rx = chest ? Mathf.Lerp(k.ChestR, k.WaistR * 1.05f, row / 3f) * 1.04f : k.WaistR * 1.06f;
                int n = 9;
                M.Bone = bone;
                for (int i = 0; i < n; i++)
                {
                    float th = (Mathf.Lerp(-150f, 150f, i / (float)(n - 1)) + (row % 2) * 13f) * Mathf.Deg2Rad;
                    var nrm = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
                    var p = new Vector3(nrm.x * rx, y, nrm.z * rx * k.DepthK);
                    M.Color = (i + row) % 4 == 0 ? bronze : scale;
                    M.Push().Translate(p).Rotate(Quaternion.LookRotation(nrm, Vector3.up)).Rotate(14f, 0f, 0f);
                    M.Sphere(Vector3.zero, new Vector3(0.034f, 0.04f, 0.012f) * U, 5, 2);
                    M.Pop();
                }
            }
            M.Jitter = 0.04f;
            k.Neck(black);
            k.Head(skin, C("#3a2a20"), C("#3a2a20"), EyeStyle.Narrow, 18f);
            // horned bronze helm with a dark visor and ember eye slits, a crest of scales
            k.Helmet(bronzeD, 0f, bronze, true);
            M.Bone = BB.Head; M.Color = black;
            M.Sphere(new Vector3(0f, k.HeadCY - 0.25f * r, 0.42f * r), new Vector3(0.92f, 0.8f, 0.68f) * r, 9, 5);
            M.Emission = 1f; M.Color = ember;
            for (int s = -1; s <= 1; s += 2)
            {
                M.Push().Translate(s * 0.36f * r, k.HeadCY - 0.12f * r, 1.06f * r).Rotate(0f, s * 26f, s * -12f);
                M.Box(Vector3.zero, new Vector3(0.34f, 0.08f, 0.06f) * r);
                M.Pop();
            }
            M.Emission = 0f;
            k.Horns(horn, 1.6f, 0.3f, 0.45f, 0.24f, false);
            M.Bone = BB.Head; M.Color = hornTip;
            for (int s = -1; s <= 1; s += 2)
            {
                var a = new Vector3(s * 0.55f * r, k.HeadCY + 0.62f * r, -0.1f * r);
                var tip = a + new Vector3(s * (0.55f + 0.3f * 0.3f) * r, 0.45f * 1.2f * r, -0.35f * r) * 1.6f;
                M.Sphere(tip, 0.06f * r, 4, 2);
            }
            M.Color = scale;
            for (int i = 0; i < 5; i++)
            {
                float ph = Mathf.Lerp(-10f, 110f, i / 4f) * Mathf.Deg2Rad;
                var p = new Vector3(0f, k.HeadCY + 0.08f * r + Mathf.Cos(ph) * 1.1f * r, Mathf.Sin(-ph) * 1.1f * r + 0.05f * r);
                M.Blade(p, p + new Vector3(0f, 0.3f * r * Mathf.Cos(ph * 0.5f), -0.3f * r), 0.24f * r, Vector3.forward);
            }
            // cape, pauldrons, tabard with the burning eye
            k.Cape(ash, emberD, k.AnkleY + 0.2f * U, 1.05f, emberD);
            M.Bone = BB.Cape;
            for (int i = 0; i < 6; i++)
            {
                float th = Mathf.Lerp(124f, 236f, i / 5f) * Mathf.Deg2Rad;
                float rr = k.ShoulderR * 1.45f * 1.05f;
                var a = new Vector3(Mathf.Sin(th) * rr, k.AnkleY + 0.23f * U, Mathf.Cos(th) * rr * k.DepthK * 1.25f + 0.02f * U);
                M.Color = i % 2 == 0 ? ash : emberD;
                M.Blade(a, a + new Vector3(Mathf.Sin(th) * 0.02f, -0.06f - (i % 3) * 0.04f, Mathf.Cos(th) * 0.02f) * U, 0.07f * U);
            }
            k.BigPauldron(-1, scale, bronze, black, 1.9f);
            k.Pauldron(1, bronzeD, 1.15f, bronze, 2);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, black, black, bronzeD);
                k.Cuff(s, bronze, 0.4f, 0.98f, 1.24f);
                k.Leg(s, scaleD, black, bronzeD, 0.62f, bronze);
                k.LegCuff(s, scale, 0.25f, 1.22f, false);
            }
            k.Belt(bronzeD, bronze, -1f, 1.1f, 0.06f);
            k.FrontFlap(black, k.KneeY - 0.06f * U, 0.2f, emberD);
            k.BackFlap(black, k.KneeY - 0.02f * U, 0.22f, emberD);
            M.Bone = BB.SkirtF;
            var ep = new Vector3(0f, k.HipY - 0.12f * U, k.HipR * k.DepthK + 0.045f * U);
            M.Color = emberD;
            M.Push().Translate(ep);
            M.Sphere(Vector3.zero, new Vector3(0.06f, 0.035f, 0.01f) * U, 8, 2);
            M.Emission = 0.9f; M.Color = ember;
            M.Sphere(new Vector3(0f, 0f, 0.006f * U), new Vector3(0.018f, 0.03f, 0.008f) * U, 6, 2);
            M.Emission = 0f;
            M.Pop();
            // greatsword with a glowing fuller
            k.Greatsword(1, 1.0f, steel, black, bronze);
            k.BeginHand(1);
            M.Emission = 1f; M.Color = ember;
            M.Box(new Vector3(0f, 0.05f * U + 0.43f * U, 0f), new Vector3(0.03f * U, 0.72f * U, 0.034f * U));
            M.Emission = 0f;
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Shoulder; m.Strike = UnitStrike.Greatsword; m.TwoHanded = true; m.Ranged = UnitRanged.Point;
            m.CastBone = BB.HandL; m.CastOffset = new Vector3(0f, -0.06f * U, 0.03f * U);
            m.DustColor = new Color(0.62f, 0.56f, 0.52f, 0.36f);
            var res = Done(k, key);
            res.HeadTop = new Vector3(0f, k.HeadCY + 1.25f * r, 0f) - k.Bind[BB.Head];
            return res;
        }

        // ================================================================== the Mossling King

        /// <summary>
        /// The Mossling King (Mossdeep's boss): a giant mossling, 2.4 m of round mossy body with big shiny eyes and a
        /// toothy grin, a long beard of hanging grey moss, a crown of red-capped spotted mushrooms around one big golden
        /// cap, a royal cape of autumn leaves, and a twig sceptre with a glowing seed bud.
        /// </summary>
        static UnitModel MosslingKing(string key)
        {
            const float H = 2.4f;
            const float S = H / 0.9f;
            var k = new BipedKit(160, H, 0.22f * S, 0.24f, 1.4f, false, 1.1f, 1.3f);
            float r = k.R, U = k.U;
            var M = k.M;
            Color moss = C("#7d9a55"), mossD = C("#5a7a3e"), mossL = C("#a8bc8a"), beard = C("#a4b08a"), beardD = C("#86946e"),
                  eye = C("#2a2a20"), teeth = C("#f4f0e0"), feet = C("#6a5a3a"), cap = C("#d8503a"), capSpot = C("#fbefd8"),
                  stalk = C("#efe2c4"), gold = C("#f2b84a"), bud = C("#ffe08a"), wood = C("#7a5a3a");
            M.Jitter = 0.08f;
            M.Bone = BB.Hips; M.Color = mossD;
            M.Sphere(new Vector3(0f, k.PelvisY + 0.01f * S, 0f), new Vector3(0.21f, 0.17f, 0.19f) * S, 9, 5);
            M.Bone = BB.Head; M.Color = moss;
            var hc = new Vector3(0f, k.HeadCY - 0.15f * r, 0f);
            M.Blob(hc, new Vector3(r * 1.18f, r * 1.05f, r * 1.08f), 1, 0.1f, 61);
            M.Color = mossL;
            M.Blob(hc + new Vector3(0.35f * r, 0.55f * r, -0.4f * r), new Vector3(0.4f, 0.3f, 0.4f) * r, 0, 0.3f, 62);
            M.Blob(hc + new Vector3(-0.55f * r, 0.25f * r, -0.5f * r), new Vector3(0.35f, 0.3f, 0.35f) * r, 0, 0.3f, 63);
            M.Blob(hc + new Vector3(0f, -0.55f * r, 0.62f * r), new Vector3(0.62f, 0.4f, 0.4f) * r, 0, 0.2f, 64);
            // tiny flowers in the moss
            M.Jitter = 0f;
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f + 0.3f;
                var p = hc + new Vector3(Mathf.Sin(a) * 1.08f * r, (0.1f + (i % 3) * 0.2f) * r, Mathf.Cos(a) * 1.0f * r);
                M.Color = i % 2 == 0 ? C("#f4e8f0") : C("#f8d860");
                M.Sphere(p, 0.07f * r, 4, 2);
            }
            // big shiny eyes, a toothy grin
            for (int s = -1; s <= 1; s += 2)
            {
                var e = hc + new Vector3(s * 0.38f * r, 0.15f * r, 0.9f * r);
                M.Color = C("#f4f0e0");
                M.Sphere(e, new Vector3(0.24f, 0.27f, 0.14f) * r, 8, 5);
                M.Color = eye;
                M.Sphere(e + new Vector3(0f, -0.02f * r, 0.08f * r), new Vector3(0.17f, 0.2f, 0.1f) * r, 7, 4);
                M.Emission = 0.6f; M.Color = Color.white;
                M.Sphere(e + new Vector3(s * 0.05f * r, 0.08f * r, 0.17f * r), 0.05f * r, 4, 2);
                M.Emission = 0f;
                M.Color = mossD;
                M.Push().Translate(e + new Vector3(0f, 0.3f * r, 0.05f * r)).Rotate(0f, 0f, -s * 14f);
                M.Box(Vector3.zero, new Vector3(0.34f, 0.08f, 0.1f) * r);
                M.Pop();
            }
            M.Color = C("#3a2a20");
            M.Sphere(hc + new Vector3(0f, -0.3f * r, 0.95f * r), new Vector3(0.45f, 0.13f, 0.08f) * r, 7, 3);
            M.Color = teeth;
            for (int i = -2; i <= 2; i++)
                M.Box(hc + new Vector3(i * 0.13f * r, -0.25f * r, 1.0f * r), new Vector3(0.08f, 0.09f, 0.04f) * r);
            // a long beard of hanging moss
            M.Jitter = 0.06f;
            for (int i = 0; i < 9; i++)
            {
                float a = (i - 4) * 0.2f;
                var p = hc + new Vector3(Mathf.Sin(a) * 0.75f * r, -0.5f * r, Mathf.Cos(a) * 0.75f * r + 0.1f * r);
                M.Color = i % 2 == 0 ? beard : beardD;
                float len = (0.75f - Mathf.Abs(i - 4) * 0.08f) * r;
                M.Blade(p, p + new Vector3(Mathf.Sin(a) * 0.1f * r, -len, 0.12f * r), 0.24f * r);
            }
            // the mushroom crown
            M.Jitter = 0.02f;
            var crown = hc + new Vector3(0f, 0.86f * r, -0.05f * r);
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f + 0.2f;
                var b = crown + new Vector3(Mathf.Sin(a) * 0.55f * r, -0.08f * r, Mathf.Cos(a) * 0.5f * r);
                float sz = (i % 2 == 0 ? 0.24f : 0.19f) * r;
                var top = b + new Vector3(Mathf.Sin(a) * 0.12f * r, sz * 1.7f, Mathf.Cos(a) * 0.1f * r);
                M.Color = stalk;
                M.Segment(b, top, sz * 0.35f, sz * 0.3f, 5);
                M.Color = cap;
                M.Push().Translate(top).Rotate(Mathf.Cos(a) * 15f, 0f, -Mathf.Sin(a) * 15f);
                M.Shell(Vector3.zero, new Vector3(sz * 1.1f, sz * 0.75f, sz * 1.1f), -180f, 180f, 8, 0f, 90f, 3);
                M.Color = capSpot;
                for (int j = 0; j < 3; j++)
                {
                    float b2 = j * 2.1f + a;
                    M.Sphere(new Vector3(Mathf.Sin(b2) * sz * 0.55f, sz * 0.58f, Mathf.Cos(b2) * sz * 0.55f), sz * 0.16f, 4, 2);
                }
                M.Pop();
            }
            M.Color = stalk;
            M.Segment(crown, crown + new Vector3(0f, 0.55f * r, 0f), 0.14f * r, 0.12f * r, 6);
            // the king's own cap glows softly (it lights his way in Mossdeep's dark)
            M.Emission = 0.35f; M.Color = gold;
            M.Shell(crown + new Vector3(0f, 0.55f * r, 0f), new Vector3(0.45f, 0.36f, 0.45f) * r, -180f, 180f, 10, 0f, 90f, 4);
            M.Emission = 0.5f; M.Color = capSpot;
            for (int j = 0; j < 5; j++)
            {
                float b2 = j * 1.26f;
                M.Sphere(crown + new Vector3(Mathf.Sin(b2) * 0.25f * r, 0.82f * r, Mathf.Cos(b2) * 0.25f * r), 0.06f * r, 4, 2);
            }
            M.Emission = 0f;
            // a royal cape of autumn leaves on the back (Cape bone)
            M.Bone = BB.Cape;
            var cb = k.Bind[BB.Cape] + new Vector3(0f, 0f, -0.25f * r);
            Color[] leaves = { C("#c8572e"), C("#e0a03a"), C("#8a3a2a"), C("#5f9a4a"), C("#d8782e") };
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < 7; i++)
                {
                    float x = (i - 3) * 0.24f * r * (1f + row * 0.15f);
                    var p = cb + new Vector3(x, -row * 0.32f * r, -row * 0.12f * r - Mathf.Abs(i - 3) * 0.03f * r);
                    M.Color = leaves[(i * 3 + row) % leaves.Length];
                    M.Blade(p, p + new Vector3(x * 0.25f, -0.6f * r, -0.18f * r), 0.3f * r, Vector3.right);
                }
            // stubby arms and big feet
            M.Jitter = 0.08f;
            for (int s = -1; s <= 1; s += 2)
            {
                M.Bone = BB.ArmU(s); M.Color = moss;
                M.Segment(k.Bind[BB.ArmU(s)], k.Bind[BB.ArmL(s)], 0.05f * S, 0.045f * S, 6);
                M.Bone = BB.ArmL(s);
                M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.045f * S, 0.04f * S, 6);
                M.Bone = BB.Hand(s); M.Color = mossD;
                M.Sphere(k.Bind[BB.Hand(s)] + Vector3.down * 0.025f * S, 0.05f * S, 6, 4);
                M.Bone = BB.LegU(s); M.Color = mossD;
                M.Segment(k.Bind[BB.LegU(s)] + Vector3.up * 0.02f * S, k.Bind[BB.LegL(s)], 0.06f * S, 0.05f * S, 6);
                M.Bone = BB.LegL(s);
                M.Segment(k.Bind[BB.LegL(s)], k.Bind[BB.Foot(s)], 0.05f * S, 0.045f * S, 6);
                M.Bone = BB.Foot(s); M.Color = feet;
                M.Sphere(new Vector3(k.Bind[BB.Foot(s)].x, 0.035f * S, 0.03f * S), new Vector3(0.06f, 0.04f, 0.08f) * S, 7, 4);
            }
            // the twig sceptre with a glowing seed bud between two leaves
            k.Staff(1, 1.05f, wood, BipedKit.StaffTop.Plain, wood, bud);
            float above = 1.05f * 0.58f * U;
            k.BeginHand(1);
            var tip = new Vector3(0f, above, 0f);
            M.Color = C("#6fb04a");
            M.Blade(tip, tip + new Vector3(0.12f, 0.1f, 0f) * U, 0.07f * U);
            M.Blade(tip, tip + new Vector3(-0.11f, 0.12f, 0.02f) * U, 0.07f * U);
            M.Emission = 1f; M.Color = bud;
            var bp = tip + new Vector3(0f, 0.07f * U, 0f);
            M.Sphere(bp, new Vector3(0.045f, 0.06f, 0.045f) * U, 7, 5);
            M.Emission = 0f;
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            m.CastBone = BB.HandR; m.CastOffset = k.Grip(1) - k.Bind[BB.HandR] + new Vector3(bp.x, -bp.z, bp.y);
            m.Heavy = 0.7f; m.MaxCadence = 1.8f; m.TurnRate = 300f;
            m.DustColor = new Color(0.72f, 0.8f, 0.55f, 0.36f);
            var res = Done(k, key, UnitGait.Heavy);
            res.HeadTop = crown + new Vector3(0f, 1.0f * r, 0f) - k.Bind[BB.Head];
            res.Radius = 0.3f * S;
            return res;
        }
    }
}
