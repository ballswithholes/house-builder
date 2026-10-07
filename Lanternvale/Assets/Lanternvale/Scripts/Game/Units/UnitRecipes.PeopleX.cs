// Expansion recipes (Docs/Expansion.md §9, builder models-people): the four new companions (Bruna, Ysolde, Liora,
// Nanami, after their "inspiration" notes in content/companions.json) and the new NPCs of Brightwater, Mirefen,
// Skyreach and the raid doors. All are bipeds built with BipedKit like the existing comp_* / npc_* recipes, and keep
// the face standards of UnitRecipes.People.cs: open eyes and brows clear of the hair, low open collars, narrow brims
// tipped back so the face shows from the 44° game camera.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class UnitRecipes
    {
        static partial void RegisterPeopleX(Dictionary<string, System.Func<UnitModel>> d)
        {
            d["comp_bruna"] = () => PxBruna("comp_bruna");
            d["comp_ysolde"] = () => PxYsolde("comp_ysolde");
            d["comp_liora"] = () => PxLiora("comp_liora");
            d["comp_nanami"] = () => PxNanami("comp_nanami");
            d["npc_archivist"] = () => PxArchivist("npc_archivist");
            d["npc_ferryman"] = () => PxFerryman("npc_ferryman");
            d["npc_fen_villager"] = () => PxFenVillager("npc_fen_villager");
            d["npc_mountain_guide"] = () => PxMountainGuide("npc_mountain_guide");
            d["npc_quartermaster"] = () => PxQuartermaster("npc_quartermaster");
            d["npc_town_guard"] = () => PxTownGuard("npc_town_guard");
            d["npc_dockhand"] = () => PxDockhand("npc_dockhand");
        }

        // ================================================================== shared bits

        /// <summary>A point on the front of the chest (x across, y height), lifted `lift` metres off the cloth.</summary>
        static Vector3 PxChestFront(BipedKit k, float x, float y, float lift = 0.006f)
        {
            float r = Mathf.Lerp(k.WaistR, k.ChestR, Mathf.Clamp01((y - k.SpineY) / Mathf.Max(0.01f, k.ChestY - k.SpineY)));
            float zx = Mathf.Sqrt(Mathf.Max(0f, 1f - (x * x) / (r * r)));
            return new Vector3(x, y, r * k.DepthK * zx + lift);
        }

        /// <summary>A little flower of `n` petals facing `normal` (water lily, sunflower, barley-tied posy).</summary>
        static void PxFlower(MeshBuilder m, Vector3 at, Vector3 normal, float r, int n, Color petal, Color heart, float cup = 0.25f)
        {
            normal = normal.normalized;
            var side = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var up = Vector3.Cross(side, normal).normalized;
            m.Color = petal;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var dir = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                m.Blade(at + dir * r * 0.15f, at + dir * r + normal * r * cup, r * 0.55f, Vector3.Cross(dir, normal));
            }
            m.Color = heart;
            m.Sphere(at + normal * r * 0.1f, r * 0.32f, 5, 3);
        }

        // ================================================================== companions

        /// <summary>
        /// Bruna Haybright, the Gate of Amberfield (Warrior): a broad, freckled gentle-giant farm girl — straw-gold hair in
        /// two thick braids with a barley sprig behind the ear, a harvest-orange smock over mail with the sleeves rolled,
        /// a huge round shield cut from a barn door with a painted sunflower, and a billhook-axe.
        /// </summary>
        static UnitModel PxBruna(string key)
        {
            var k = new BipedKit(81, 1.96f, 0.146f, 0.48f, 1.3f, true, 1.16f, 1.02f);
            Color smock = C("#e2782e"), smockD = C("#a8521e"), mail = C("#9aa3ad"), mailD = C("#6f7884"), cream = C("#f4e6c4"),
                  skin = C("#f0c49a"), hair = C("#ecc25a"), freckle = C("#c8784e"), leather = C("#7a5232"), leatherD = C("#55381f"),
                  pants = C("#6a5a44"), boot = C("#5a3a26"), brass = C("#d8a848"), green = C("#5f9a48"), steel = C("#c8ced4"),
                  wood = C("#8a6a48");
            k.Torso(smock, smockD);
            // the mail shows at the neck under the smock's square neckline
            k.M.Bone = BB.Chest; k.M.Color = mail;
            k.M.Band(Vector3.zero, 0.082f * k.U, 0.095f * k.U, k.ShoulderY - 0.03f * k.U, k.NeckY + 0.01f * k.U, 10, 0.95f);
            k.ChestPanel(cream, 0.13f, false, null, k.ShoulderY - 0.07f * k.U);
            k.Strap(leatherD, 1, 0.05f);
            k.Neck(skin);
            k.Head(skin, C("#5a8a4a"), Paint.Shade(hair, 0.72f), EyeStyle.Round, -6f, true, 1.05f);
            k.Cheeks(C("#f0988a"), 1f, -0.3f);
            // freckles across the nose and cheeks
            k.M.Bone = BB.Head; k.M.Color = freckle;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 3; i++)
                    k.M.Sphere(k.FacePoint(s * (0.2f + i * 0.11f), -0.2f - (i % 2) * 0.08f, 0.02f), 0.024f * k.R, 3, 2);
            k.Nose(Paint.Shade(skin, 0.93f), 1.05f, -0.28f);
            k.Smile(Paint.Shade(skin, 0.55f), -0.56f, 0.32f);
            k.HairCap(hair, 58f, 100f, 120f);
            k.Bangs(hair, 5, 0.34f, 74f, 0.5f);
            // two thick braids from behind the ears falling over the front of the shoulders, tied in green ribbon
            for (int s = -1; s <= 1; s += 2)
                k.Braid(hair, k.HeadPoint(s * 108f, 86f, 1.02f), new Vector3(s * 0.15f * k.U, k.ChestY - 0.02f * k.U, 0.1f * k.U), 5, 0.32f, green);
            // a sprig of barley tucked behind the right ear
            k.M.Bone = BB.Head;
            var ear = k.HeadPoint(96f, 70f, 1.06f);
            k.M.Color = C("#d8b048");
            k.M.Segment(ear + new Vector3(0f, -0.04f, 0.02f) * k.U, ear + new Vector3(0.02f, 0.12f, -0.05f) * k.U, 0.006f * k.U, 0.004f * k.U, 4);
            k.M.Color = C("#f2d06a");
            for (int i = 0; i < 4; i++)
            {
                var p = ear + new Vector3(0.012f, 0.06f + i * 0.02f, -0.025f - i * 0.009f) * k.U;
                k.M.Blade(p, p + new Vector3((i % 2 == 0 ? 1 : -1) * 0.025f, 0.03f, -0.01f) * k.U, 0.018f * k.U);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, smock, skin, skin);
                // sleeves rolled up over the elbow: a fat cream roll at the end of the smock sleeve
                k.ArmBand(s, cream, 0.86f, 1.4f, 0.07f);
                k.Leg(s, pants, pants, boot, 0.62f);
                k.LegCuff(s, smockD, 0.05f, 1.2f, true);
            }
            k.Cuff(-1, leather, 0.35f, 0.95f, 1.25f);
            k.Cuff(1, leatherD, 0.6f, 0.95f, 1.2f);
            // a quilted leather pad on the axe shoulder
            k.Pauldron(1, leather, 1.35f, brass, 2);
            // mail skirt under the smock's skirt
            k.Skirt(mail, mailD, k.KneeY + 0.02f * k.U, 1.3f, 0f, mailD);
            k.Skirt(smock, smockD, k.KneeY + 0.12f * k.U, 1.38f, 22f, cream);
            k.Belt(leather, brass, k.HipY + 0.1f * k.U, 1.12f, 0.06f);
            PxBillhook(k, 1, wood, steel, leatherD);
            PxBarnDoorShield(k, 1.42f);
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Throw;
            m.Breath = 1.15f;
            return Done(k, key);
        }

        /// <summary>A farm billhook-axe: ash haft, a broad blade with a forward hook at the top (hand frame, edge +Z).</summary>
        static void PxBillhook(BipedKit k, int side, Color haft, Color blade, Color grip)
        {
            float u = k.U, len = 0.7f;
            k.BeginHand(side, -0.16f * u);
            k.M.Color = haft;
            k.M.Cylinder(Vector3.zero, 0.022f * u, 0.02f * u, len * u, 6);
            k.M.Color = grip;
            k.M.Cylinder(new Vector3(0f, 0.08f * u, 0f), 0.026f * u, 0.026f * u, 0.14f * u, 6);
            k.M.Color = Paint.Shade(blade, 0.75f);
            k.M.Cylinder(new Vector3(0f, len * u - 0.2f * u, 0f), 0.028f * u, 0.028f * u, 0.05f * u, 6);
            k.M.Color = blade;
            k.M.Push().Translate(0f, len * u - 0.16f * u, 0f).Rotate(0f, -90f, 0f);
            k.M.Flat(new[]
            {
                new Vector2(0.0f, 0.0f), new Vector2(0.1f * u, -0.02f * u), new Vector2(0.17f * u, 0.06f * u), new Vector2(0.19f * u, 0.17f * u),
                new Vector2(0.15f * u, 0.27f * u), new Vector2(0.07f * u, 0.3f * u), new Vector2(0.1f * u, 0.22f * u), new Vector2(0.08f * u, 0.15f * u),
                new Vector2(0.0f, 0.2f * u),
            }, 0.026f * u);
            k.M.Pop();
            k.End();
        }

        /// <summary>
        /// Bruna's shield: a big round board cut from the old barn door (red planks, a dark iron rim and two cross
        /// battens) with a painted sunflower on the face, carried on the left forearm like BipedKit.Shield.
        /// </summary>
        static void PxBarnDoorShield(BipedKit k, float size)
        {
            float u = k.U, s = size * u;
            Color barn = C("#b84a3a"), barnD = C("#8a3428"), iron = C("#4a4448"), back = C("#9a7048"), strap = C("#5a3a26"),
                  petal = C("#f6c832"), petalD = C("#e09a22"), seeds = C("#6a4020"), leaf = C("#5f9a48");
            k.M.Bone = BB.ArmLL;
            float x = -k.ShoulderX - k.ForeR - 0.04f * u;
            float y = Mathf.Lerp(k.ElbowY, k.WristY, 0.45f);
            k.M.Push().Translate(x, y, 0.02f * u).Rotate(0f, 90f, 0f);
            float R0 = 0.27f * s;
            // board (planks: alternate shades in vertical strips, clipped round)
            k.M.Push().Rotate(90f, 0f, 0f);
            k.M.Color = back;
            k.M.Cylinder(new Vector3(0f, -0.012f * s, 0f), R0 * 0.97f, R0 * 0.97f, 0.036f * s, 14);
            k.M.Pop();
            for (int i = -2; i <= 2; i++)
            {
                float px = i * R0 * 0.38f, half = R0 * 0.19f;
                float hy = Mathf.Sqrt(Mathf.Max(0f, R0 * R0 - px * px)) * 0.97f;
                k.M.Color = (i & 1) == 0 ? barn : barnD;
                k.M.Box(new Vector3(px, 0f, -0.02f * s), new Vector3(half * 2f - 0.004f * s, hy * 2f, 0.012f * s));
            }
            k.M.Color = iron;
            k.M.Push().Rotate(90f, 0f, 0f);
            k.M.Torus(new Vector3(0f, 0.004f * s, 0f), R0, 0.02f * s, 14, 3);
            k.M.Pop();
            // the sunflower painted on the face (outside = −Z)
            float z = -0.03f * s;
            for (int ring = 0; ring < 2; ring++)
            {
                int n = ring == 0 ? 12 : 10;
                float rr = ring == 0 ? 0.17f * s : 0.12f * s;
                k.M.Color = ring == 0 ? petal : petalD;
                for (int i = 0; i < n; i++)
                {
                    float a = (i + ring * 0.5f) * Mathf.PI * 2f / n;
                    var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    k.M.Blade(new Vector3(0f, 0.01f * s, z - ring * 0.002f * s) + dir * 0.05f * s,
                              new Vector3(0f, 0.01f * s, z - ring * 0.002f * s) + dir * rr, 0.055f * s, Vector3.Cross(dir, Vector3.forward));
                }
            }
            k.M.Color = seeds;
            k.M.Push().Translate(0f, 0.01f * s, z - 0.006f * s).Rotate(90f, 0f, 0f);
            k.M.Cylinder(Vector3.zero, 0.065f * s, 0.06f * s, 0.012f * s, 10);
            k.M.Pop();
            k.M.Color = leaf;
            k.M.Blade(new Vector3(0f, -0.08f * s, z), new Vector3(0f, -0.24f * s, z), 0.025f * s, Vector3.right);
            k.M.Blade(new Vector3(0f, -0.17f * s, z - 0.002f * s), new Vector3(0.09f * s, -0.12f * s, z - 0.002f * s), 0.05f * s, Vector3.up);
            // arm straps on the back
            for (int i = -1; i <= 1; i += 2)
            {
                k.M.Color = strap;
                k.M.Box(new Vector3(0f, i * 0.085f * s, 0.012f * s), new Vector3(0.36f * s, 0.04f * s, 0.012f * s));
                k.M.Color = iron;
                k.M.Box(new Vector3(0f, i * 0.16f * s, 0.008f * s), new Vector3(0.44f * s, 0.03f * s, 0.01f * s));
            }
            k.M.Pop();
            k.Model.Shield = true;
        }

        /// <summary>
        /// Ysolde of the Heronguard (Paladin): tall and straight-backed, short silver-blonde hair, a long river-blue
        /// surcoat over polished mail with a white heron on the breast, the faded order sash, a tall kite shield with a
        /// heron feather tied to its rim and a plain arming sword.
        /// </summary>
        static UnitModel PxYsolde(string key)
        {
            var k = new BipedKit(82, 1.84f, 0.136f, 0.5f, 1.0f, true, 1.06f);
            Color blue = C("#3f78b4"), blueD = C("#28507e"), mail = C("#a9b2bc"), steel = C("#d0d6de"), steelD = C("#8a949f"),
                  white = C("#f6f4ee"), gold = C("#d8b464"), sash = C("#c48a84"), skin = C("#f2d2b8"), hair = C("#ece0c0"),
                  leather = C("#5e4030"), beak = C("#e8a43a");
            k.Torso(mail, mail);
            // surcoat body: a tabard over the mail (front and back), white heron on the breast
            k.ChestPanel(blue, 0.27f, true, null, k.ShoulderY - 0.02f * k.U);
            PxHeron(k, white, beak, PxChestFront(k, 0.02f * k.U, k.ChestY + 0.01f * k.U, 0.012f * k.U), 1f);
            // the faded order sash: shoulder to hip with a small heron badge
            k.Strap(sash, 1, 0.06f);
            k.M.Bone = BB.Chest;
            k.M.Color = white;
            var badge = PxChestFront(k, -k.ShoulderX * 0.42f, k.ChestY + 0.1f * k.U, 0.016f * k.U);
            k.M.Aim(badge, Vector3.forward);
            k.M.Cylinder(Vector3.zero, 0.03f * k.U, 0.03f * k.U, 0.008f * k.U, 8);
            k.M.Pop();
            k.Collar(steelD, blueD, 0.06f, 50f, 1.3f);
            k.Neck(skin);
            k.Head(skin, C("#5a7aa0"), Paint.Shade(hair, 0.62f), EyeStyle.Narrow, 4f, true, 0.96f);
            k.Nose(Paint.Shade(skin, 0.93f), 0.9f, -0.28f);
            // short silver-blonde crop: a neat cap, a side-swept fringe and short locks over the ears
            k.HairCap(hair, 56f, 104f, 116f, 1.1f);
            k.Bangs(hair, 4, 0.36f, 80f, 0.6f);
            k.SideLocks(hair, k.HeadCY - 0.55f * k.R, 0.2f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, mail, mail, leather);
                k.Cuff(s, steel, 0.55f, 0.95f, 1.28f);
                k.Leg(s, blueD, steelD, leather, 0.7f, steel);
            }
            k.Pauldron(1, steel, 1.2f, gold, 2);
            k.BigPauldron(-1, steel, gold, blueD, 1.45f);
            // the surcoat skirt, split front for the saddle, to mid-shin
            k.Skirt(blue, blueD, k.AnkleY + 0.16f * k.U, 1.38f, 26f, white);
            k.Belt(leather, gold, k.HipY + 0.1f * k.U, 1.12f, 0.05f);
            k.SashTails(1, sash, white, 0.36f, k.HipY + 0.1f * k.U);
            k.Sword(1, 0.82f, 0.07f, C("#e2e6ea"), leather, gold);
            PxHeronShield(k, blue, steel, white, beak, 1.22f);
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Sword; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        /// <summary>A white heron (the Heronguard's badge) laid flat on a surface at `at`, facing +Z, `size` ≈ 1 → 0.2 m tall.</summary>
        static void PxHeron(BipedKit k, Color white, Color beak, Vector3 at, float size, int bone = BB.Chest)
        {
            float u = k.U * size;
            k.M.Bone = bone;
            k.M.Push().Translate(at);
            k.M.Color = white;
            // body: a tilted oval; neck: an S curve; head; legs; beak
            k.M.Push().Translate(0.012f * u, -0.02f * u, 0f).Rotate(0f, 0f, -25f);
            k.M.Sphere(Vector3.zero, new Vector3(0.05f * u, 0.028f * u, 0.008f * u), 8, 3);
            k.M.Pop();
            k.M.Curve(new Vector3(-0.022f * u, -0.004f * u, 0.002f * u), new Vector3(-0.05f * u, 0.04f * u, 0.002f * u),
                      new Vector3(-0.022f * u, 0.07f * u, 0.002f * u), 0.009f * u, 0.007f * u, 3, 4);
            k.M.Sphere(new Vector3(-0.024f * u, 0.078f * u, 0.002f * u), new Vector3(0.014f, 0.012f, 0.007f) * u, 6, 3);
            k.M.Color = beak;
            k.M.Blade(new Vector3(-0.034f * u, 0.078f * u, 0.004f * u), new Vector3(-0.07f * u, 0.07f * u, 0.004f * u), 0.008f * u, Vector3.forward);
            k.M.Color = white;
            k.M.Blade(new Vector3(0.05f * u, -0.035f * u, 0.002f * u), new Vector3(0.07f * u, -0.05f * u, 0.002f * u), 0.018f * u, Vector3.forward);
            k.M.Color = Paint.Shade(beak, 0.8f);
            k.M.Segment(new Vector3(0.012f * u, -0.04f * u, 0.002f * u), new Vector3(0.01f * u, -0.085f * u, 0.002f * u), 0.003f * u, 0.003f * u, 3);
            k.M.Pop();
        }

        /// <summary>Ysolde's tall kite shield on the left forearm: river blue, steel rim, a white heron, a heron feather tied to the rim.</summary>
        static void PxHeronShield(BipedKit k, Color face, Color rim, Color white, Color beak, float size)
        {
            // BipedKit.Shield draws the board; its emblem is painted in the face colour so the heron replaces it
            k.Shield(face, rim, face, false, size, C("#8a6a4a"));
            float u = k.U, s = size * u;
            k.M.Bone = BB.ArmLL;
            float x = -k.ShoulderX - k.ForeR - 0.035f * u;
            float y = Mathf.Lerp(k.ElbowY, k.WristY, 0.45f);
            // heron on the outside: the shield frame looks out along its −Z, so the heron is drawn in that frame turned
            // round (facing −Z), standing proud of the painted face
            k.M.Push().Translate(x, y, 0.02f * u).Rotate(0f, -90f, 0f).Translate(0f, 0.02f * s, 0.034f * s);
            PxHeronLocal(k, white, beak, 1.25f * size);
            k.M.Pop();
            // a long grey heron feather tied at the top corner of the rim with a bit of red thread
            k.M.Push().Translate(x, y, 0.02f * u).Rotate(0f, 90f, 0f);
            var tie = new Vector3(0.235f * s, 0.24f * s, -0.01f * s);
            k.M.Color = C("#c8483a");
            k.M.Sphere(tie, 0.016f * s, 5, 3);
            k.M.Color = C("#d8dce4");
            k.M.Blade(tie, tie + new Vector3(0.08f * s, -0.24f * s, -0.03f * s), 0.05f * s, Vector3.forward);
            k.M.Color = C("#6a7280");
            k.M.Blade(tie + new Vector3(0.058f, -0.17f, -0.022f) * s, tie + new Vector3(0.08f * s, -0.24f * s, -0.031f * s), 0.03f * s, Vector3.forward);
            k.M.Pop();
        }

        /// <summary>The heron in the current frame (facing +Z, on the XY plane), without changing the bone.</summary>
        static void PxHeronLocal(BipedKit k, Color white, Color beak, float size)
        {
            float u = k.U * size;
            k.M.Color = white;
            k.M.Push().Translate(0.012f * u, -0.02f * u, 0f).Rotate(0f, 0f, -25f);
            k.M.Sphere(Vector3.zero, new Vector3(0.05f * u, 0.028f * u, 0.008f * u), 8, 3);
            k.M.Pop();
            k.M.Curve(new Vector3(-0.022f * u, -0.004f * u, 0.002f * u), new Vector3(-0.05f * u, 0.04f * u, 0.002f * u),
                      new Vector3(-0.022f * u, 0.07f * u, 0.002f * u), 0.009f * u, 0.007f * u, 3, 4);
            k.M.Sphere(new Vector3(-0.024f * u, 0.078f * u, 0.002f * u), new Vector3(0.014f, 0.012f, 0.007f) * u, 6, 3);
            k.M.Blade(new Vector3(0.05f * u, -0.035f * u, 0.002f * u), new Vector3(0.07f * u, -0.05f * u, 0.002f * u), 0.018f * u, Vector3.forward);
            k.M.Color = beak;
            k.M.Blade(new Vector3(-0.034f * u, 0.078f * u, 0.004f * u), new Vector3(-0.07f * u, 0.07f * u, 0.004f * u), 0.008f * u, Vector3.forward);
            k.M.Segment(new Vector3(0.012f * u, -0.04f * u, 0.002f * u), new Vector3(0.01f * u, -0.085f * u, 0.002f * u), 0.003f * u, 0.003f * u, 3);
        }

        /// <summary>
        /// Liora, the River Lantern (Priest): a serene chapel keeper in layered white and plum robes with long sleeves,
        /// silver-grey hair in a loose low braid over the shoulder, a gentle half-smile, and a small brass
        /// lantern-censer swinging on a chain from her hand.
        /// </summary>
        static UnitModel PxLiora(string key)
        {
            var k = new BipedKit(83, 1.67f, 0.138f, 0.5f, 0.95f, true);
            Color white = C("#f6f2ea"), cream = C("#e6dccb"), plum = C("#7a4670"), plumD = C("#55304f"), brass = C("#d6a24a"),
                  brassD = C("#9a6e2c"), glow = C("#ffd890"), skin = C("#f0d0b8"), hair = C("#cfcad2"), boot = C("#6a4a5a"),
                  steel = C("#b8bec6");
            k.Torso(white, plum);
            // plum stole down the front over the white robe
            k.ChestPanel(plum, 0.09f, false, null, k.ShoulderY - 0.01f * k.U);
            // a short plum shoulder mantle trimmed in brass, open at the front (the second layer of her robes)
            k.M.Bone = BB.Chest;
            k.M.Panel(Vector3.zero, 30f, 330f, 10, new[]
            {
                new Vector2(0.09f * k.U, k.NeckY + 0.01f * k.U), new Vector2(k.ShoulderR * 1.12f, k.ShoulderY - 0.03f * k.U),
                new Vector2(k.ShoulderR * 1.26f, k.ShoulderY - 0.17f * k.U),
            }, k.DepthK * 1.14f, plum, plumD);
            k.M.Color = brass;
            k.M.Band(Vector3.zero, k.ShoulderR * 1.27f, k.ShoulderR * 1.29f, k.ShoulderY - 0.18f * k.U, k.ShoulderY - 0.16f * k.U, 12, k.DepthK * 1.14f);
            k.Neck(skin);
            k.Head(skin, C("#8a6a48"), Paint.Shade(hair, 0.7f), EyeStyle.Round, -7f);
            k.Cheeks(C("#eea8a0"), 0.8f, -0.32f);
            k.Nose(Paint.Shade(skin, 0.94f), 0.9f, -0.28f);
            k.Smile(Paint.Shade(skin, 0.58f), -0.57f, 0.24f);
            k.HairCap(hair, 60f, 100f, 120f);
            k.Bangs(hair, 4, 0.36f, 90f, 0.3f);
            // the loose low braid over the right shoulder, tied with a plum ribbon
            k.Braid(hair, k.HeadPoint(150f, 100f, 1.0f), new Vector3(0.12f * k.U, k.ChestY - 0.06f * k.U, 0.1f * k.U), 8, 0.24f, plum);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, white, white, skin);
                k.WideSleeve(s, white, plum, 0.33f, 2.05f, plum);
                k.Leg(s, plumD, plumD, boot, 0.4f);
            }
            // inner plum robe to the ankle, outer white robe open at the front
            k.Skirt(plum, plumD, k.AnkleY + 0.03f * k.U, 1.45f, 0f, brass);
            k.Skirt(white, cream, k.AnkleY + 0.14f * k.U, 1.58f, 34f, plum);
            k.FrontFlap(plum, k.KneeY - 0.1f * k.U, 0.09f, brass, -1f, 0.02f * k.U);
            k.Sash(cream, k.SpineY - 0.02f * k.U, 0.07f);
            // the riverstep mace hanging at the left hip
            k.HipItem(-1, C("#6a4a3a"), 0.3f, 0.022f, steel);
            k.M.Bone = BB.SkirtL; k.M.Color = steel;
            k.M.Sphere(new Vector3(-(k.HipR * 1.1f) - 0.03f * k.U, k.HipY + 0.06f * k.U - 0.3f * k.U, -0.08f * k.U), 0.04f * k.U, 6, 4);
            // the lantern-censer: a ring in the fist, a chain, the little brass lantern (hand frame: +Y up, +Z forward)
            k.BeginHand(1, 0f);
            k.M.Color = brassD;
            k.M.Torus(new Vector3(0f, 0.01f, 0.03f), 0.03f, 0.007f, 8, 3);
            var lp = new Vector3(0f, -0.25f, 0.07f);
            k.M.Segment(new Vector3(0f, 0.0f, 0.035f), lp + new Vector3(0f, 0.11f, 0f), 0.006f, 0.006f, 3);
            k.M.Color = brass;
            k.M.Cone(lp + new Vector3(0f, 0.065f, 0f), 0.07f, 0.075f, 8);
            k.M.Sphere(lp + new Vector3(0f, 0.145f, 0f), 0.014f, 4, 2);
            k.M.Cylinder(lp + new Vector3(0f, -0.08f, 0f), 0.05f, 0.064f, 0.025f, 8);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                k.M.Box(lp + new Vector3(Mathf.Cos(a) * 0.054f, -0.008f, Mathf.Sin(a) * 0.054f), new Vector3(0.014f, 0.13f, 0.014f));
            }
            k.M.Emission = 1f; k.M.Color = glow;
            k.M.Sphere(lp + new Vector3(0f, -0.008f, 0f), new Vector3(0.05f, 0.064f, 0.05f), 7, 5);
            k.M.Emission = 0f;
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Carry; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Point;
            m.CastBone = BB.HandR;
            // hand frame → hand bone space: (x, y, z) → (x, −z, y)
            m.CastOffset = k.Grip(1) - k.Bind[BB.HandR] + new Vector3(lp.x, -lp.z, lp.y);
            m.StrideK = 0.88f;
            return Done(k, key);
        }

        /// <summary>
        /// Nanami, Voice of the Fen (Shaman): a barefoot tide-singer — sea-green wrap skirt, a short shell-bead top under
        /// a loose fen-grey shawl, dark hair in a high knot pinned with water-lilies, blue tide tattoos curling up her
        /// arms, and a driftwood staff hung with a conch and little bells.
        /// </summary>
        static UnitModel PxNanami(string key)
        {
            var k = new BipedKit(84, 1.58f, 0.142f, 0.5f, 0.88f, true, 0.96f);
            Color sea = C("#3aa58a"), seaD = C("#22735f"), seaL = C("#8ad8c0"), top = C("#f2e4cc"), coral = C("#f0806e"),
                  shawl = C("#a9b8b4"), shawlD = C("#7a8a88"), skin = C("#d9a27a"), hair = C("#2a2430"), tattoo = C("#3a76c8"),
                  lily = C("#fbeef4"), lilyP = C("#f2a6c4"), yellow = C("#f2d04a"), drift = C("#b9a88e"), conch = C("#f6dcc4"),
                  bell = C("#d8b04a");
            k.Torso(top, sea, skin);
            // shell beads along the lower hem of the top
            k.M.Bone = BB.Chest;
            for (int i = 0; i < 7; i++)
            {
                float th = (-72f + i * 24f) * Mathf.Deg2Rad;
                float r = k.ChestR * 0.97f;
                var p = new Vector3(Mathf.Sin(th) * r, k.SpineY + 0.035f * k.U, Mathf.Cos(th) * r * k.DepthK + 0.006f);
                k.M.Color = i % 2 == 0 ? coral : C("#fff6ea");
                k.M.Sphere(p, 0.018f * k.U, 4, 2);
            }
            Shawl(k, shawl);
            k.Neck(skin);
            k.Head(skin, C("#3a8a8a"), hair, EyeStyle.Round, -6f, true, 0.95f);
            k.Cheeks(C("#f09a8a"), 0.9f, -0.32f);
            k.Smile(Paint.Shade(skin, 0.55f), -0.56f, 0.26f);
            k.HairCap(hair);
            k.Bangs(hair, 5, 0.4f, 84f, 0.5f);
            k.SideLocks(hair, k.HeadCY - 0.7f * k.R, 0.17f);
            // the high knot, pinned with two water-lilies and a hairpin
            var knot = k.HeadPoint(180f, 28f, 1.05f);
            k.Bun(hair, knot, 0.5f);
            k.M.Bone = BB.Head;
            PxFlower(k.M, knot + new Vector3(0.05f, 0.03f, 0.02f) * k.U, new Vector3(0.7f, 0.5f, 0.4f), 0.05f * k.U, 7, lily, yellow, 0.4f);
            PxFlower(k.M, knot + new Vector3(-0.05f, 0.0f, 0.03f) * k.U, new Vector3(-0.7f, 0.3f, 0.5f), 0.04f * k.U, 6, lilyP, yellow, 0.4f);
            k.M.Color = seaD;
            k.M.Segment(knot + new Vector3(-0.08f, 0.04f, -0.01f) * k.U, knot + new Vector3(0.09f, -0.02f, -0.03f) * k.U, 0.005f * k.U, 0.004f * k.U, 4);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, skin, skin);
                // tide tattoos: thin blue bands curling up the forearm and a wave mark on the upper arm
                k.Cuff(s, tattoo, 0.25f, 0.31f, 1.04f);
                k.Cuff(s, tattoo, 0.58f, 0.63f, 1.04f);
                k.ArmBand(s, tattoo, 0.68f, 1.04f, 0.02f);
                k.Cuff(s, coral, 0.9f, 0.96f, 1.12f);
                // bare feet with a shell anklet
                k.Leg(s, skin, skin, skin, 0.08f, null, Paint.Shade(skin, 0.85f));
                k.LegCuff(s, coral, 0.9f, 1.15f, true);
            }
            k.Skirt(sea, seaD, k.AnkleY + 0.14f * k.U, 1.48f, 18f, seaL);
            k.SashTails(-1, seaL, coral, 0.4f, k.HipY + 0.12f * k.U);
            k.Belt(seaD, coral, k.HipY + 0.12f * k.U, 1.1f, 0.035f);
            PxTideStaff(k, drift, conch, bell, C("#7fe8d8"));
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            m.Breath = 1.2f;
            return Done(k, key);
        }

        /// <summary>Nanami's driftwood staff: a crooked pale branch forking at the top, a conch hung in the fork, and bells.</summary>
        static void PxTideStaff(BipedKit k, Color drift, Color conch, Color bell, Color glow)
        {
            float u = k.U, len = 1.62f;
            k.Staff(1, len, drift, BipedKit.StaffTop.Plain, drift, drift);
            float above = len * 0.58f * u;
            k.BeginHand(1);
            var tip = new Vector3(0f, above, 0f);
            k.M.Color = drift;
            // knots and the fork
            k.M.Sphere(tip * 0.55f, 0.028f * u, 5, 3);
            k.M.Curve(tip, tip + new Vector3(0.04f, 0.1f, 0f) * u, tip + new Vector3(0.1f, 0.16f, 0.02f) * u, 0.02f * u, 0.01f * u, 3, 5);
            k.M.Curve(tip, tip + new Vector3(-0.03f, 0.1f, 0f) * u, tip + new Vector3(-0.06f, 0.2f, -0.01f) * u, 0.02f * u, 0.009f * u, 3, 5);
            // the conch hanging in the fork on a cord
            var c0 = tip + new Vector3(0.02f, 0.02f, 0.0f) * u;
            k.M.Color = C("#6a5a4a");
            k.M.Segment(tip + new Vector3(0.06f, 0.12f, 0.01f) * u, c0 + new Vector3(0f, 0.04f, 0f) * u, 0.004f * u, 0.004f * u, 3);
            k.M.Color = conch;
            k.M.Push().Translate(c0).Rotate(0f, 0f, 70f);
            k.M.Lathe(new[] { new Vector2(0.0f, -0.06f * u), new Vector2(0.035f * u, -0.03f * u), new Vector2(0.04f * u, 0.0f), new Vector2(0.028f * u, 0.04f * u), new Vector2(0f, 0.08f * u) }, 7, false, false, false);
            k.M.Color = C("#f2a8a0");
            k.M.Sphere(new Vector3(0.018f * u, 0.0f, 0.02f * u), new Vector3(0.012f, 0.03f, 0.012f) * u, 5, 3);
            k.M.Pop();
            // three little bells on cords from the lower fork
            for (int i = 0; i < 3; i++)
            {
                var b0 = tip + new Vector3(-0.015f - i * 0.016f, 0.06f + i * 0.04f, 0f) * u;
                var b1 = b0 + new Vector3(-0.02f, -0.07f, (i - 1) * 0.02f) * u;
                k.M.Color = C("#6a5a4a");
                k.M.Segment(b0, b1, 0.003f * u, 0.003f * u, 3);
                k.M.Color = bell;
                k.M.Cone(b1 + new Vector3(0f, -0.025f, 0f) * u, 0.02f * u, 0.03f * u, 6);
            }
            // a ribbon of sea glass tied under the fork (the cast glows here)
            k.M.Emission = 0.8f; k.M.Color = glow;
            k.M.Sphere(tip + new Vector3(0f, -0.03f, 0.0f) * u, 0.022f * u, 6, 4);
            k.M.Emission = 0f;
            k.End();
            var anchor = tip + new Vector3(0f, 0.08f, 0f) * u;
            k.Model.CastBone = BB.HandR;
            k.Model.CastOffset = k.Grip(1) - k.Bind[BB.HandR] + new Vector3(anchor.x, -anchor.z, anchor.y);
        }

        // ================================================================== NPCs

        /// <summary>
        /// Archivist Penhallow (Brightwater): an old scholar in a faded indigo coat over a mustard waistcoat, round
        /// spectacles, wispy white hair, a quill behind the ear, ink-stained cuffs and fingers, a satchel stuffed with
        /// scrolls and a ledger in hand.
        /// </summary>
        static UnitModel PxArchivist(string key)
        {
            var k = new BipedKit(85, 1.68f, 0.144f, 0.47f, 0.92f, false, 0.92f);
            Color coat = C("#4c5a80"), coatD = C("#323c5a"), vest = C("#c99a46"), shirt = C("#f0e6d0"), skin = C("#ecc8a8"),
                  white = C("#f2f0ea"), ink = C("#1e2a52"), leather = C("#7a5232"), leatherD = C("#55381f"), paper = C("#f2e6c4"),
                  gold = C("#d9b25a"), pants = C("#4a4038");
            k.Torso(shirt, pants);
            Vest(k, vest);
            k.Neck(skin);
            k.Head(skin, C("#5a6a8a"), new Color(0f, 0f, 0f, 0f), EyeStyle.Round);
            k.BushyBrows(white, 0.24f, 0.37f, 12f, 1.0f);
            k.Nose(Paint.Shade(skin, 0.92f), 1.35f, -0.26f);
            k.Cheeks(C("#ee9a8a"), 0.7f, -0.34f);
            // wispy white hair: bald on top, tufts over the ears sticking out, a little crest
            k.M.Bone = BB.Head; k.M.Color = white;
            k.M.Shell(new Vector3(0f, k.HeadCY + 0.02f * k.R, -0.03f * k.R), new Vector3(1.08f, 1.04f, 1.07f) * k.R, 72f, 288f, 10, 70f, 116f, 2);
            // fluffy white puffs over the ears (behind the temples, clear of the face) and a wisp on the crown
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 2; i++)
                    k.M.Sphere(k.HeadPoint(s * (112f + i * 26f), 74f + i * 8f, 1.06f), new Vector3(0.3f, 0.26f, 0.3f) * k.R, 6, 4);
            k.M.Curve(k.HeadPoint(0f, 10f, 0.98f), k.HeadPoint(0f, 0f, 1.25f), k.HeadPoint(-30f, 14f, 1.3f), 0.06f * k.R, 0.02f * k.R, 3, 4);
            k.Spectacles(gold);
            // a quill tucked behind the right ear, its feather standing up and back
            var qe = k.HeadPoint(100f, 62f, 1.1f);
            k.M.Color = C("#5f9a8a");
            k.M.Blade(qe + new Vector3(0f, -0.02f, 0.03f) * k.U, qe + new Vector3(0.015f, 0.07f, -0.1f) * k.U, 0.026f * k.U);
            k.M.Color = ink;
            k.M.Segment(qe + new Vector3(0f, -0.02f, 0.03f) * k.U, qe + new Vector3(0f, -0.04f, 0.06f) * k.U, 0.004f * k.U, 0.002f * k.U, 3);
            // the long coat (open, tails to the shin) with wide turned-back cuffs
            k.M.Bone = BB.Chest;
            k.M.Panel(Vector3.zero, 30f, 330f, 10, new[]
            {
                new Vector2(0.09f * k.U, k.NeckY + 0.01f * k.U), new Vector2(k.ShoulderR * 1.08f, k.ShoulderY - 0.02f * k.U),
                new Vector2(k.ChestR * 1.1f, k.ChestY), new Vector2(k.WaistR * 1.16f, k.SpineY - 0.04f * k.U),
            }, k.DepthK * 1.12f, coat, coatD);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, coat, coat, skin);
                k.Cuff(s, coatD, 0.72f, 0.94f, 1.38f);
                k.Leg(s, pants, pants, C("#3a2a22"), 0.4f);
            }
            k.Skirt(coat, coatD, k.KneeY - 0.14f * k.U, 1.4f, 36f, coatD);
            // ink: stained fingers and blotches on the cuffs
            k.M.Color = ink;
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = BB.Hand(s);
                k.M.Sphere(new Vector3(s * k.ShoulderX - s * 0.02f * k.U, k.WristY - 0.08f * k.U, 0.03f * k.U), 0.02f * k.U, 5, 3);
                k.M.Bone = BB.ArmL(s);
                k.M.Sphere(new Vector3(s * (k.ShoulderX + k.ForeR * 1.2f), Mathf.Lerp(k.ElbowY, k.WristY, 0.82f), 0.02f * k.U), 0.022f * k.U, 5, 3);
                k.M.Sphere(new Vector3(s * k.ShoulderX, Mathf.Lerp(k.ElbowY, k.WristY, 0.78f), k.ForeR * 1.25f), 0.018f * k.U, 5, 3);
            }
            k.Belt(leatherD, gold, k.HipY + 0.1f * k.U, 1.1f, 0.035f);
            PxScrollSatchel(k, leather, leatherD, paper, C("#c8483a"));
            k.Book(-1, C("#6a3a2e"), paper);
            var m = k.Model;
            m.HoldL = UnitHold.Book; m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Throw;
            m.StrideK = 0.82f; m.Breath = 1.25f;
            return Done(k, key);
        }

        /// <summary>A satchel on the right hip (strap from the left shoulder) stuffed with rolled scrolls standing out of it.</summary>
        static void PxScrollSatchel(BipedKit k, Color bag, Color strap, Color paper, Color seal)
        {
            k.Strap(strap, -1, 0.045f);
            k.M.Bone = BB.Hips; k.M.Color = bag;
            float th = 66f * Mathf.Deg2Rad, rr = k.HipR * 1.25f;
            var p = new Vector3(Mathf.Sin(th) * rr, k.HipY + 0.02f * k.U, Mathf.Cos(th) * rr * k.DepthK * 1.1f);
            k.M.Push().Translate(p).Rotate(0f, 66f, 0f);
            k.M.Box(Vector3.zero, new Vector3(0.22f, 0.18f, 0.09f) * k.U);
            k.M.Color = Paint.Shade(bag, 0.8f);
            k.M.Box(new Vector3(0f, 0.06f * k.U, 0.047f * k.U), new Vector3(0.23f, 0.08f, 0.012f) * k.U);
            // scrolls poking out of the top
            for (int i = 0; i < 4; i++)
            {
                float x = (-0.07f + i * 0.045f) * k.U;
                var b = new Vector3(x, 0.04f * k.U, (i % 2 == 0 ? -0.015f : 0.012f) * k.U);
                var t = b + new Vector3((i - 1.5f) * 0.02f, 0.13f + (i % 2) * 0.04f, 0f) * k.U;
                k.M.Color = paper;
                k.M.Segment(b, t, 0.022f * k.U, 0.022f * k.U, 6);
                k.M.Color = i == 1 ? seal : Paint.Shade(paper, 0.85f);
                k.M.Segment(Vector3.Lerp(b, t, 0.7f), Vector3.Lerp(b, t, 0.78f), 0.024f * k.U, 0.024f * k.U, 6);
            }
            k.M.Pop();
        }

        /// <summary>
        /// The Brightwater ferryman: a weathered old river hand in a green oilskin cape and floppy hat, grey beard and a
        /// clay pipe, rolled trousers, a rope coil at the belt and a long punt pole with a little lantern on a hook.
        /// </summary>
        static UnitModel PxFerryman(string key)
        {
            var k = new BipedKit(86, 1.76f, 0.142f, 0.48f, 1.05f, false, 1.0f);
            Color oil = C("#4f7462"), oilD = C("#34503f"), shirt = C("#d8cdb0"), pants = C("#5a6a88"), skin = C("#d8a07a"),
                  beard = C("#b8b2aa"), hat = C("#7a5a3a"), rope = C("#d8c79a"), wood = C("#8a6a48"), pipe = C("#c8a888"),
                  red = C("#c44a3a"), glow = C("#ffd27a"), iron = C("#3a3638");
            k.Torso(shirt, pants);
            k.Neck(skin);
            k.Head(skin, C("#5a6a6a"), Paint.Shade(beard, 0.8f), EyeStyle.Round, 6f);
            k.HairCap(beard, 62f, 100f, 118f);
            k.Beard(beard, 0.45f, 0.85f);
            k.Moustache(beard);
            k.Nose(Paint.Shade(skin, 0.92f), 1.2f, -0.27f);
            // clay pipe in the corner of the mouth
            k.M.Bone = BB.Head; k.M.Color = pipe;
            var pm = k.FacePoint(0.32f, -0.52f, 0.05f);
            k.M.Segment(pm, pm + new Vector3(0.05f, -0.03f, 0.08f) * k.U, 0.007f * k.U, 0.006f * k.U, 4);
            k.M.Cylinder(pm + new Vector3(0.05f, -0.035f, 0.08f) * k.U, 0.018f * k.U, 0.022f * k.U, 0.045f * k.U, 6);
            // a floppy felt hat, brim turned down at the back
            k.BrimHat(hat, hat, 1.9f, 0.85f, 0.2f, red, -14f);
            // the oilskin rain cape: a short shoulder cape over a knee-length coat
            k.M.Bone = BB.Chest;
            k.M.Panel(Vector3.zero, 0f, 360f, 12, new[]
            {
                new Vector2(0.1f * k.U, k.NeckY + 0.015f * k.U), new Vector2(k.ShoulderR * 1.12f, k.ShoulderY - 0.03f * k.U),
                new Vector2(k.ShoulderR * 1.32f, k.ShoulderY - 0.2f * k.U),
            }, k.DepthK * 1.14f, oil, oilD);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, oil, shirt, skin);
                k.Cuff(s, shirt, 0.0f, 0.3f, 1.25f);
                k.Leg(s, pants, skin, C("#5a4030"), 0.18f);
                k.LegCuff(s, pants, 0.2f, 1.35f, true);
            }
            k.Skirt(oil, oilD, k.KneeY + 0.02f * k.U, 1.35f, 30f, oilD);
            k.Belt(C("#4a3426"), C("#a8a8a0"), k.HipY + 0.1f * k.U, 1.1f, 0.045f);
            // a coil of rope hanging at the left hip
            k.M.Bone = BB.SkirtL; k.M.Color = rope;
            var rc = new Vector3(-k.HipR * 1.3f, k.HipY - 0.05f * k.U, 0.0f);
            for (int i = 0; i < 3; i++)
            {
                k.M.Push().Translate(rc + new Vector3(-0.004f * i, -0.01f * i, 0f) * k.U).Rotate(0f, 0f, 90f + i * 6f);
                k.M.Torus(Vector3.zero, 0.075f * k.U, 0.012f * k.U, 10, 4);
                k.M.Pop();
            }
            // the punt pole: long and plain, an iron shoe at the foot, a hook near the top with a small lantern
            float len = 2.5f, above = len * 0.58f * k.U, below = len * 0.42f * k.U;
            k.Staff(1, len, wood, BipedKit.StaffTop.Plain, wood, wood);
            k.BeginHand(1);
            k.M.Color = iron;
            k.M.Cylinder(new Vector3(0f, -below - 0.02f * k.U, 0f), 0.026f * k.U, 0.024f * k.U, 0.08f * k.U, 6);
            var hook = new Vector3(0f, above - 0.18f * k.U, 0f);
            k.M.Curve(hook, hook + new Vector3(0.0f, 0.04f, 0.12f) * k.U, hook + new Vector3(0f, -0.02f, 0.16f) * k.U, 0.009f * k.U, 0.007f * k.U, 3, 4);
            var lan = hook + new Vector3(0f, -0.1f, 0.16f) * k.U;
            k.M.Segment(hook + new Vector3(0f, -0.02f, 0.16f) * k.U, lan + new Vector3(0f, 0.06f, 0f) * k.U, 0.004f * k.U, 0.004f * k.U, 3);
            k.M.Box(lan + new Vector3(0f, 0.05f, 0f) * k.U, new Vector3(0.08f, 0.02f, 0.08f) * k.U);
            k.M.Box(lan + new Vector3(0f, -0.05f, 0f) * k.U, new Vector3(0.085f, 0.02f, 0.085f) * k.U);
            k.M.Emission = 1f; k.M.Color = glow;
            k.M.Box(lan, new Vector3(0.06f, 0.08f, 0.06f) * k.U);
            k.M.Emission = 0f;
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Spear; m.Strike = UnitStrike.Spear; m.Ranged = UnitRanged.Throw;
            m.StrideK = 0.9f;
            return Done(k, key);
        }

        /// <summary>
        /// Mirefen stilt-folk: long-legged and lanky, a wide conical reed hat, a straw rain cape of layered reeds,
        /// bound shins for wading, bare feet and a three-pronged fishing spear.
        /// </summary>
        static UnitModel PxFenVillager(string key)
        {
            var k = new BipedKit(87, 1.82f, 0.132f, 0.54f, 0.84f, false, 0.92f);
            Color reed = C("#cdb26e"), reedD = C("#a08850"), tunic = C("#6f8a5a"), tunicD = C("#4f6a42"), wrap = C("#d8d0b8"),
                  pants = C("#4f5f7f"), skin = C("#c89070"), hair = C("#3a2e28"), wood = C("#8a7050"), iron = C("#8a9098"),
                  band = C("#5a8aa0");
            k.Torso(tunic, pants);
            k.Neck(skin);
            k.Head(skin, C("#6a7a4a"), hair, EyeStyle.Round, 2f, true, 0.95f);
            k.Nose(Paint.Shade(skin, 0.92f), 1.0f, -0.28f);
            k.HairCap(hair);
            k.Bangs(hair, 4, 0.3f, 70f);
            // the conical reed hat: a wide shallow cone with woven rings and a chin band, tipped back to show the face
            k.M.Bone = BB.Head;
            k.M.Push().Translate(0f, k.HeadCY + 0.42f * k.R, -0.04f * k.R).Rotate(-14f, 0f, 0f);
            k.M.Color = reed;
            k.M.Lathe(new[] { new Vector2(2.35f * k.R, -0.06f * k.R), new Vector2(2.3f * k.R, 0f), new Vector2(1.1f * k.R, 0.5f * k.R), new Vector2(0.12f * k.R, 0.95f * k.R), new Vector2(0f, 1.0f * k.R) }, 12, false, true, false);
            k.M.Color = reedD;
            k.M.Band(Vector3.zero, 1.72f * k.R, 1.12f * k.R, 0.22f * k.R, 0.48f * k.R, 12);
            k.M.Band(Vector3.zero, 0.62f * k.R, 0.32f * k.R, 0.69f * k.R, 0.82f * k.R, 10);
            k.M.Color = band;
            k.M.Sphere(new Vector3(0f, 0.97f * k.R, 0f), 0.12f * k.R, 5, 3);
            k.M.Pop();
            // the reed rain cape: two layers of straw fringe over the shoulders and back
            k.M.Bone = BB.Chest;
            k.M.Panel(Vector3.zero, 0f, 360f, 12, new[]
            {
                new Vector2(0.095f * k.U, k.NeckY + 0.01f * k.U), new Vector2(k.ShoulderR * 1.14f, k.ShoulderY - 0.03f * k.U),
                new Vector2(k.ShoulderR * 1.34f, k.ShoulderY - 0.17f * k.U),
            }, k.DepthK * 1.15f, reed, reedD);
            k.M.Panel(Vector3.zero, 120f, 240f, 8, new[]
            {
                new Vector2(k.ShoulderR * 1.2f, k.ShoulderY - 0.12f * k.U), new Vector2(k.ChestR * 1.32f, k.ChestY - 0.06f * k.U),
                new Vector2(k.WaistR * 1.5f, k.SpineY - 0.08f * k.U),
            }, k.DepthK * 1.25f, reedD, Paint.Shade(reedD, 0.8f));
            // straw ends: short blades along the shoulder cape's hem
            k.M.Color = reed;
            for (int i = 0; i < 14; i++)
            {
                float th = i * 360f / 14f;
                if (Mathf.Abs(Mathf.DeltaAngle(th, 0f)) < 30f) continue;
                float a = th * Mathf.Deg2Rad, r = k.ShoulderR * 1.32f;
                var p = new Vector3(Mathf.Sin(a) * r, k.ShoulderY - 0.16f * k.U, Mathf.Cos(a) * r * k.DepthK * 1.15f);
                k.M.Blade(p, p + new Vector3(Mathf.Sin(a) * 0.02f, -0.07f, Mathf.Cos(a) * 0.02f) * k.U, 0.035f * k.U);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, tunic, skin, skin);
                k.ArmBand(s, tunicD, 0.92f, 1.25f, 0.04f);
                k.Leg(s, pants, wrap, skin, 0.04f, null, Paint.Shade(skin, 0.85f));
                k.LegCuff(s, pants, 0.0f, 1.2f, true);
                // wading wraps bound criss-cross up the shin
                k.LegCuff(s, Paint.Shade(wrap, 0.85f), 0.35f, 1.12f, true);
                k.LegCuff(s, Paint.Shade(wrap, 0.85f), 0.7f, 1.1f, true);
            }
            k.Skirt(tunic, tunicD, k.HipY - 0.12f * k.U, 1.28f, 40f, tunicD);
            k.Sash(band, k.HipY + 0.12f * k.U, 0.06f);
            k.Pouch(-1, C("#8a7050"), 0.4f, 1.1f);
            // fishing spear: a long pole with three barbed prongs
            float len = 2.1f, above = len * 0.58f * k.U;
            k.Staff(1, len, wood, BipedKit.StaffTop.Plain, wood, wood);
            k.BeginHand(1);
            k.M.Color = iron;
            var t0 = new Vector3(0f, above, 0f);
            k.M.Box(t0 + new Vector3(0f, 0.01f, 0f) * k.U, new Vector3(0.14f, 0.02f, 0.03f) * k.U);
            for (int i = -1; i <= 1; i++)
            {
                var b = t0 + new Vector3(i * 0.06f, 0.01f, 0f) * k.U;
                k.M.Segment(b, b + new Vector3(0f, 0.17f - Mathf.Abs(i) * 0.03f, 0f) * k.U, 0.009f * k.U, 0.004f * k.U, 4);
            }
            k.M.Color = band;
            k.M.Segment(t0 + new Vector3(0f, -0.06f, 0f) * k.U, t0 + new Vector3(0f, -0.01f, 0f) * k.U, 0.026f * k.U, 0.026f * k.U, 6);
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Spear; m.Strike = UnitStrike.Spear; m.Ranged = UnitRanged.Throw;
            m.StrideK = 1.08f;
            return Done(k, key);
        }

        /// <summary>
        /// The Skyreach mountain guide: bundled in furs — a fur-trimmed hat with ear flaps, a heavy fur collar over a
        /// quilted leather coat, a red scarf, a frosted beard, a coil of rope across the chest and a tall iron-shod staff
        /// with an ice axe at the belt.
        /// </summary>
        static UnitModel PxMountainGuide(string key)
        {
            var k = new BipedKit(88, 1.82f, 0.142f, 0.48f, 1.2f, false, 1.08f);
            Color coat = C("#8a5a3a"), coatD = C("#5f3c26"), fur = C("#ece2cc"), furD = C("#b0906a"), scarf = C("#c8483a"),
                  scarfD = C("#8e2e26"), skin = C("#d49a72"), beard = C("#8a6a4a"), frost = C("#eef4f8"), rope = C("#dccb9c"),
                  wood = C("#7a5a3a"), iron = C("#5a5a62"), pants = C("#5a5048"), boot = C("#4a3424"), blue = C("#4f7aa8");
            k.Torso(coat, pants);
            k.Mantle(fur, 1.15f, 0.04f, 13);
            k.Neck(skin);
            k.Head(skin, C("#5a7aa0"), Paint.Shade(beard, 0.85f), EyeStyle.Narrow, 4f);
            k.Cheeks(C("#e8806e"), 1f, -0.3f);
            k.Nose(Paint.Shade(skin, 0.9f), 1.15f, -0.27f);
            k.Beard(beard, 0.55f, 0.95f);
            k.Moustache(beard);
            // frost in the beard
            k.M.Bone = BB.Head; k.M.Color = frost;
            for (int i = 0; i < 5; i++)
                k.M.Sphere(k.FacePoint((i - 2) * 0.17f, -0.75f - (i % 2) * 0.12f, 0.12f), 0.05f * k.R, 4, 2);
            // fur hat: a leather cap with a thick fur brim and ear flaps
            k.M.Bone = BB.Head;
            k.M.Color = coatD;
            k.M.Shell(new Vector3(0f, k.HeadCY + 0.08f * k.R, -0.03f * k.R), new Vector3(1.14f, 1.12f, 1.12f) * k.R, -180f, 180f, 12, 0f, th =>
            {
                float a = Mathf.Abs(th);
                return a < 70f ? 58f : Mathf.Lerp(58f, 92f, (a - 70f) / 110f);
            }, 3);
            k.M.Color = fur;
            k.M.Push().Translate(0f, k.HeadCY + 0.5f * k.R, -0.03f * k.R).Rotate(-10f, 0f, 0f);
            k.M.Torus(Vector3.zero, 1.08f * k.R, 0.2f * k.R, 12, 5);
            k.M.Pop();
            for (int s = -1; s <= 1; s += 2)
            {
                var ef = k.HeadPoint(s * 92f, 95f, 1.12f);
                k.M.Color = furD;
                k.M.Sphere(ef, new Vector3(0.18f, 0.36f, 0.3f) * k.R, 6, 4);
            }
            k.M.Color = blue;
            k.M.Sphere(new Vector3(0f, k.HeadCY + 1.1f * k.R, -0.08f * k.R), 0.18f * k.R, 6, 4);
            k.Scarf(scarf, 0.5f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, coat, coat, furD);
                k.Cuff(s, fur, 0.72f, 0.95f, 1.45f);
                k.Leg(s, pants, pants, boot, 0.72f);
                k.LegCuff(s, fur, 0.12f, 1.4f, true);
            }
            // quilted coat to the knee, open a little at the front, fur hem
            k.Skirt(coat, coatD, k.KneeY - 0.02f * k.U, 1.38f, 20f, fur);
            k.Belt(coatD, iron, k.HipY + 0.1f * k.U, 1.12f, 0.05f);
            // the rope coil slung across the chest from the left shoulder
            k.M.Bone = BB.Chest; k.M.Color = rope;
            for (int i = 0; i < 2; i++)
            {
                k.M.Push().Translate(0.01f * k.U, k.ChestY + 0.01f * k.U, -0.01f * k.U).Rotate(0f, 0f, 38f + i * 4f).Rotate(90f, 0f, 0f);
                k.M.Torus(Vector3.zero, k.ChestR * 1.3f + i * 0.012f * k.U, 0.017f * k.U, 14, 4);
                k.M.Pop();
            }
            // ice axe at the right hip
            k.M.Bone = BB.SkirtR; k.M.Color = wood;
            var ax = new Vector3(k.HipR * 1.2f, k.HipY + 0.08f * k.U, -0.03f * k.U);
            k.M.Segment(ax, ax + new Vector3(0.02f, -0.36f, -0.06f) * k.U, 0.016f * k.U, 0.014f * k.U, 5);
            k.M.Color = iron;
            k.M.Box(ax + new Vector3(0f, 0.02f, 0f) * k.U, new Vector3(0.03f, 0.03f, 0.2f) * k.U);
            k.Pouch(-1, furD, 0.5f, 1.1f);
            // tall alpenstock: iron shod, a leather grip and a little ribbon
            k.Staff(1, 1.9f, wood, BipedKit.StaffTop.Plain, wood, wood);
            k.BeginHand(1);
            float above = 1.9f * 0.58f * k.U, below = 1.9f * 0.42f * k.U;
            k.M.Color = iron;
            k.M.Cylinder(new Vector3(0f, -below - 0.1f * k.U, 0f), 0.004f * k.U, 0.024f * k.U, 0.08f * k.U, 5);
            k.M.Cylinder(new Vector3(0f, -below - 0.02f * k.U, 0f), 0.025f * k.U, 0.025f * k.U, 0.05f * k.U, 6);
            k.M.Cylinder(new Vector3(0f, above - 0.03f * k.U, 0f), 0.026f * k.U, 0.02f * k.U, 0.05f * k.U, 6);
            k.M.Color = scarf;
            k.M.Blade(new Vector3(0f, above - 0.08f * k.U, 0f), new Vector3(0.04f, above / k.U - 0.26f, -0.05f) * k.U, 0.04f * k.U);
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Throw;
            m.StrideK = 0.92f;
            return Done(k, key);
        }

        /// <summary>
        /// The raid quartermaster: a stout, brisk supplier in a leather jerkin and work apron full of pockets, a bandolier
        /// of coloured potion vials, a bandana over curly red hair, a pencil behind the ear, a heavy pack with a bedroll
        /// and a pot, and a ledger in hand.
        /// </summary>
        static UnitModel PxQuartermaster(string key)
        {
            var k = new BipedKit(89, 1.68f, 0.146f, 0.46f, 1.34f, true, 1.1f);
            Color jerkin = C("#7a5636"), jerkinD = C("#55381f"), shirt = C("#ece0c4"), apron = C("#5f6a4a"), pants = C("#4a4f60"),
                  skin = C("#ecc0a0"), hair = C("#c0582e"), bandana = C("#3f7a9a"), leather = C("#6a4a30"), brass = C("#d8a848"),
                  paper = C("#f2e6c4"), boot = C("#4a3424");
            k.Torso(jerkin, pants, jerkin, 0.12f);
            k.Neck(skin);
            k.Head(skin, C("#4f8a5a"), Paint.Shade(hair, 0.75f), EyeStyle.Round, 8f);
            k.Cheeks(C("#ec9a8a"), 0.9f, -0.32f);
            k.Nose(Paint.Shade(skin, 0.93f), 1.05f, -0.28f);
            k.Smile(Paint.Shade(skin, 0.55f), -0.57f, 0.22f);
            k.HairCap(hair);
            // curls escaping under the bandana at the temples and the nape
            k.M.Bone = BB.Head; k.M.Color = BipedKit.HairTone(hair);
            for (int i = 0; i < 7; i++)
            {
                float th = -110f + i * 37f;
                if (Mathf.Abs(th) < 40f) continue;
                k.M.Sphere(k.HeadPoint(th, 92f + (i % 2) * 8f, 1.04f), 0.18f * k.R, 4, 3);
            }
            k.Headscarf(bandana, Paint.Shade(bandana, 0.85f));
            // a pencil behind the left ear
            k.M.Bone = BB.Head; k.M.Color = C("#e8b830");
            var pe = k.HeadPoint(-96f, 66f, 1.1f);
            k.M.Segment(pe + new Vector3(0f, -0.02f, 0.06f) * k.U, pe + new Vector3(0f, 0.03f, -0.08f) * k.U, 0.008f * k.U, 0.008f * k.U, 5);
            // the bandolier of potion vials across the chest
            k.Strap(leather, 1, 0.06f);
            Color[] vials = { C("#e8483a"), C("#3a8ae0"), C("#e8b830"), C("#5ab84a"), C("#e8483a") };
            k.M.Bone = BB.Chest;
            for (int i = 0; i < 5; i++)
            {
                float t = 0.18f + i * 0.15f;
                var a = new Vector3(Mathf.Lerp(k.ShoulderX * 0.62f, -k.WaistR * 0.8f, t), Mathf.Lerp(k.ShoulderY - 0.02f * k.U, k.SpineY - 0.02f * k.U, t), 0f);
                var p = PxChestFront(k, a.x, a.y, 0.026f * k.U);
                k.M.Emission = 0.35f; k.M.Color = vials[i];
                k.M.Cylinder(p + new Vector3(0f, -0.035f * k.U, 0f), 0.016f * k.U, 0.016f * k.U, 0.05f * k.U, 6);
                k.M.Emission = 0f; k.M.Color = C("#8a6a48");
                k.M.Cylinder(p + new Vector3(0f, 0.015f * k.U, 0f), 0.009f * k.U, 0.009f * k.U, 0.018f * k.U, 5);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, shirt, shirt, skin);
                k.ArmBand(s, jerkinD, 0.12f, 1.25f, 0.06f);
                k.Cuff(s, leather, 0.55f, 0.95f, 1.25f);
                k.Leg(s, pants, pants, boot, 0.62f);
            }
            k.Skirt(jerkin, jerkinD, k.HipY - 0.1f * k.U, 1.2f, 30f, jerkinD);
            k.Apron(apron, k.KneeY - 0.06f * k.U, 0.32f, null, leather);
            // apron pockets
            k.M.Bone = BB.SkirtF; k.M.Color = Paint.Shade(apron, 0.8f);
            for (int s = -1; s <= 1; s += 2)
                k.M.Box(new Vector3(s * 0.08f * k.U, k.HipY - 0.12f * k.U, k.HipR * k.DepthK * 1.35f + 0.012f * k.U), new Vector3(0.1f, 0.09f, 0.012f) * k.U);
            k.Belt(leather, brass, k.HipY + 0.1f * k.U, 1.1f, 0.05f);
            k.Backpack(C("#8a6a45"), leather, C("#b8452f"));
            k.Book(-1, C("#5a7a4a"), paper);
            var m = k.Model;
            m.HoldL = UnitHold.Book; m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Throw;
            m.Breath = 1.2f;
            return Done(k, key);
        }

        /// <summary>
        /// A Brightwater town guard in the town's livery: river blue and white, a gold wave on the tabard, a brimmed
        /// steel morion with a blue plume, and a halberd.
        /// </summary>
        static UnitModel PxTownGuard(string key)
        {
            var k = new BipedKit(90, 1.82f, 0.14f, 0.49f, 1.1f, false, 1.06f);
            Color steel = C("#b8c0ca"), steelD = C("#7a858f"), mail = C("#959fa8"), blue = C("#3f78b4"), blueD = C("#28507e"),
                  white = C("#f4f2ea"), gold = C("#e2b45a"), skin = C("#e6b690"), hair = C("#5a3a26"), leather = C("#5e4030"),
                  wood = C("#7a5534"), pants = C("#3c4a68");
            k.Torso(mail, mail);
            k.ChestPanel(white, 0.26f, true, null);
            // livery: blue side panels and a gold wave across the white
            k.M.Bone = BB.Chest; k.M.Color = blue;
            for (int s = -1; s <= 1; s += 2)
            {
                var p = PxChestFront(k, s * 0.07f * k.U, (k.ShoulderY + k.SpineY) * 0.5f - 0.03f * k.U, 0.016f * k.U);
                k.M.Box(p, new Vector3(0.06f * k.U, (k.ShoulderY - k.SpineY) * 0.9f, 0.008f * k.U));
            }
            k.M.Color = gold;
            var wv = PxChestFront(k, 0f, k.ChestY - 0.01f * k.U, 0.024f * k.U);
            for (int i = 0; i < 3; i++)
            {
                float x0 = (-0.075f + i * 0.05f) * k.U;
                k.M.Curve(wv + new Vector3(x0, 0f, 0f), wv + new Vector3(x0 + 0.02f * k.U, 0.03f * k.U, 0f), wv + new Vector3(x0 + 0.05f * k.U, 0f, 0f), 0.009f * k.U, 0.009f * k.U, 3, 4);
            }
            k.Neck(skin);
            k.Head(skin, C("#5a6a7a"), hair, EyeStyle.Narrow, 6f);
            k.Nose(Paint.Shade(skin, 0.93f), 1.0f, -0.28f);
            k.HairCap(hair, 62f, 100f, 118f);
            // the morion: a steel dome with a comb on top, a brim tipped up at front and back, and a blue plume
            k.Helmet(steel, 0f, null);
            k.M.Bone = BB.Head; k.M.Color = steelD;
            k.M.Push().Translate(0f, k.HeadCY + 0.42f * k.R, -0.03f * k.R).Rotate(-10f, 0f, 0f);
            k.M.Cylinder(Vector3.zero, 1.55f * k.R, 1.42f * k.R, 0.06f * k.R, 12);
            k.M.Pop();
            k.M.Color = steel;
            k.M.Push().Translate(0f, k.HeadCY + 1.02f * k.R, -0.05f * k.R).Rotate(0f, 90f, 0f);
            k.M.Flat(new[] { new Vector2(-0.8f * k.R, 0f), new Vector2(-0.4f * k.R, 0.32f * k.R), new Vector2(0.4f * k.R, 0.32f * k.R), new Vector2(0.8f * k.R, 0f) }, 0.08f * k.R);
            k.M.Pop();
            k.M.Color = blue;
            var pl = k.HeadPoint(180f, 30f, 1.15f);
            k.M.Blade(pl, pl + new Vector3(0.02f, 0.22f, -0.2f) * k.U, 0.09f * k.U);
            k.M.Color = white;
            k.M.Blade(pl + new Vector3(0f, 0.01f, 0.01f), pl + new Vector3(0.0f, 0.18f, -0.22f) * k.U, 0.05f * k.U);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, blue, mail, leather);
                k.Pauldron(s, steel, 1.15f, gold, 2);
                k.Cuff(s, steelD, 0.5f, 0.95f, 1.25f);
                k.Leg(s, pants, steelD, leather, 0.62f, steel);
            }
            k.FrontFlap(white, k.KneeY - 0.04f * k.U, 0.24f, blue);
            k.BackFlap(white, k.KneeY - 0.05f * k.U, 0.24f, blue);
            k.Belt(leather, gold);
            k.HipItem(-1, C("#3a3040"), 0.4f, 0.025f, gold);
            // halberd: a spear with an axe blade and a back spike
            float len = 2.15f, above = len * 0.58f * k.U;
            k.Spear(1, len, wood, C("#d0d6dc"), blue);
            k.BeginHand(1);
            k.M.Color = C("#c8ced4");
            k.M.Push().Translate(0f, above - 0.04f * k.U, 0f).Rotate(0f, -90f, 0f);
            k.M.Flat(new[] { new Vector2(0.02f * k.U, 0.12f * k.U), new Vector2(0.2f * k.U, 0.18f * k.U), new Vector2(0.25f * k.U, -0.02f * k.U), new Vector2(0.17f * k.U, -0.12f * k.U), new Vector2(0.02f * k.U, -0.05f * k.U) }, 0.024f * k.U);
            k.M.Flat(new[] { new Vector2(-0.02f * k.U, 0.03f * k.U), new Vector2(-0.12f * k.U, -0.01f * k.U), new Vector2(-0.02f * k.U, -0.03f * k.U) }, 0.02f * k.U);
            k.M.Pop();
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Spear; m.Strike = UnitStrike.Spear; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        /// <summary>
        /// A Brightwater dockhand: a broad, cheerful stevedore in a striped shirt with the sleeves rolled, suspenders,
        /// a red knit cap, an anchor tattoo, a sack slung over the left shoulder and a cargo hook in hand.
        /// </summary>
        static UnitModel PxDockhand(string key)
        {
            var k = new BipedKit(91, 1.76f, 0.142f, 0.48f, 1.3f, true, 1.12f);
            Color shirt = C("#f2ede0"), stripe = C("#3a5a8a"), pants = C("#6a5440"), skin = C("#c88a62"), hair = C("#3a2a22"),
                  cap = C("#c8483a"), capD = C("#94302a"), sack = C("#c8b088"), sackD = C("#9a8460"), leather = C("#4a3426"),
                  iron = C("#5a5a62"), tattoo = C("#2f4f7f"), boot = C("#3a2c22");
            k.Torso(shirt, pants);
            // horizontal stripes round the shirt
            k.M.Bone = BB.Chest; k.M.Color = stripe;
            for (int i = 0; i < 3; i++)
            {
                float y = Mathf.Lerp(k.SpineY + 0.02f * k.U, k.ShoulderY - 0.07f * k.U, i / 2.5f);
                float r = Mathf.Lerp(k.WaistR, k.ChestR, Mathf.Clamp01((y - k.SpineY) / (k.ChestY - k.SpineY))) * 1.02f;
                k.M.Band(Vector3.zero, r, r, y - 0.018f * k.U, y + 0.018f * k.U, 10, k.DepthK * 1.02f);
            }
            k.M.Bone = BB.Spine;
            k.M.Band(Vector3.zero, k.WaistR * 1.06f, k.WaistR * 1.06f, k.SpineY - 0.07f * k.U, k.SpineY - 0.035f * k.U, 10, k.DepthK * 1.02f);
            // suspenders
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = BB.Chest; k.M.Color = leather;
                var a = PxChestFront(k, s * 0.075f * k.U, k.ShoulderY - 0.03f * k.U, 0.014f * k.U);
                var b = PxChestFront(k, s * 0.065f * k.U, k.SpineY - 0.05f * k.U, 0.014f * k.U);
                k.M.Segment(a, b, 0.016f * k.U, 0.016f * k.U, 4);
            }
            k.Neck(skin);
            k.Head(skin, C("#6a5030"), hair, EyeStyle.Round, -2f);
            k.Cheeks(C("#e88a7a"), 1f, -0.3f);
            k.Nose(Paint.Shade(skin, 0.92f), 1.05f, -0.28f);
            k.Smile(Paint.Shade(skin, 0.5f), -0.56f, 0.3f);
            k.HairCap(hair);
            k.Bangs(hair, 4, 0.3f, 70f);
            k.Ponytail(hair, k.ShoulderY - 0.02f * k.U, 0.3f, 0.3f, cap);
            // the knit cap: a snug dome sitting back on the head with a rolled brim and a pompom
            k.M.Bone = BB.Head; k.M.Color = cap;
            k.M.Shell(new Vector3(0f, k.HeadCY + 0.12f * k.R, -0.06f * k.R), new Vector3(1.12f, 1.1f, 1.12f) * k.R, -180f, 180f, 12, 0f, th =>
            {
                float a = Mathf.Abs(th);
                return a < 80f ? 50f : Mathf.Lerp(50f, 78f, (a - 80f) / 100f);
            }, 3);
            k.M.Color = capD;
            k.M.Push().Translate(0f, k.HeadCY + 0.62f * k.R, -0.1f * k.R).Rotate(-24f, 0f, 0f);
            k.M.Torus(Vector3.zero, 0.92f * k.R, 0.13f * k.R, 12, 4);
            k.M.Pop();
            k.M.Color = shirt;
            k.M.Sphere(new Vector3(0f, k.HeadCY + 1.12f * k.R, -0.28f * k.R), 0.2f * k.R, 6, 4);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, shirt, skin, skin);
                k.ArmBand(s, shirt, 0.9f, 1.32f, 0.06f);
                k.Cuff(s, leather, 0.82f, 0.96f, 1.2f);
                k.Leg(s, pants, pants, boot, 0.55f);
                k.LegCuff(s, Paint.Shade(pants, 0.85f), 0.62f, 1.25f, true);
            }
            // the anchor tattoo on the right forearm
            k.M.Bone = BB.ArmL(1); k.M.Color = tattoo;
            var ta = new Vector3(k.ShoulderX + k.ForeR * 0.6f, Mathf.Lerp(k.ElbowY, k.WristY, 0.38f), k.ForeR * 0.82f);
            k.M.Box(ta, new Vector3(0.01f, 0.05f, 0.01f) * k.U);
            k.M.Box(ta + new Vector3(0f, 0.018f, 0f) * k.U, new Vector3(0.03f, 0.008f, 0.01f) * k.U);
            k.M.Curve(ta + new Vector3(-0.018f, -0.012f, 0f) * k.U, ta + new Vector3(0f, -0.04f, 0.004f) * k.U, ta + new Vector3(0.018f, -0.012f, 0f) * k.U, 0.005f * k.U, 0.005f * k.U, 3, 3);
            k.Belt(leather, iron, k.HipY + 0.08f * k.U, 1.1f, 0.05f);
            // the sack slung over the left shoulder
            k.M.Bone = BB.Chest; k.M.Color = sack;
            var sp = new Vector3(-k.ShoulderX * 0.78f, k.ShoulderY + 0.05f * k.U, -0.08f * k.U);
            k.M.Push().Translate(sp).Rotate(20f, 0f, 70f);
            k.M.Blob(Vector3.zero, new Vector3(0.14f, 0.24f, 0.13f) * k.U, 1, 0.12f, 91, 0.1f);
            k.M.Color = sackD;
            k.M.Cylinder(new Vector3(0f, 0.22f * k.U, 0f), 0.04f * k.U, 0.03f * k.U, 0.06f * k.U, 6);
            k.M.Pop();
            // cargo hook in the right fist
            k.BeginHand(1, 0f);
            k.M.Color = C("#8a6a48");
            k.M.Cylinder(new Vector3(-0.06f * k.U, 0f, 0f), 0.022f * k.U, 0.022f * k.U, 0.12f * k.U, 6);
            k.M.Color = iron;
            k.M.Curve(new Vector3(0f, 0.0f, 0f), new Vector3(0f, 0.16f, 0.0f) * k.U, new Vector3(0f, 0.16f, 0.1f) * k.U, 0.012f * k.U, 0.006f * k.U, 4, 4);
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }
    }
}
