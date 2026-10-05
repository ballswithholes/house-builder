// Recipes: village NPCs, bandits, hollow spirits, mosslings, the treant and humanoid demons.
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class UnitRecipes
    {
        // ================================================================== village NPCs

        static UnitModel Elder(string key)
        {
            var k = new BipedKit(31, 1.62f, 0.14f, 0.47f, 0.95f, false, 0.95f);
            Color robe = C("#8a6a4a"), robeD = C("#6a4e36"), honey = C("#e8b04f"), green = C("#5f8a5a"), greenD = C("#3f6146"),
                  gold = C("#d9b25a"), skin = C("#e9c4a0"), white = C("#f4f2ee"), wood = C("#6b4a35");
            k.Torso(robe, robeD);
            k.Sash(honey, k.SpineY - 0.03f, 0.08f);
            k.Neck(skin);
            k.Head(skin, C("#4a3a30"), white, EyeStyle.Closed, -8f);
            // bald with white hair at the sides and back
            k.M.Bone = BB.Head; k.M.Color = white;
            k.M.Shell(new Vector3(0f, k.HeadCY + 0.02f * k.R, -0.03f * k.R), new Vector3(1.07f, 1.04f, 1.06f) * k.R, 70f, 290f, 10, 72f, 118f, 2);
            k.Beard(white, 1.4f, 0.85f);
            k.Moustache(white);
            k.Cape(green, greenD, k.KneeY - 0.02f, 1.05f, gold);
            k.Collar(green, greenD, 0.05f, 30f, 1.15f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, robe, robe, skin);
                k.WideSleeve(s, robe, robeD, 0.28f, 2.1f, honey);
                k.Leg(s, robeD, robeD, C("#4a3a2a"), 0.3f);
            }
            k.Skirt(robe, robeD, k.AnkleY + 0.05f, 1.45f, 0f, robeD);
            k.Staff(1, 1.45f, wood, BipedKit.StaffTop.Lantern, wood, C("#ffd890"));
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            m.StrideK = 0.8f; m.Breath = 1.3f;
            return Done(k, key);
        }

        static UnitModel Innkeeper(string key)
        {
            var k = new BipedKit(32, 1.62f, 0.14f, 0.48f, 1.25f, true, 1.02f);
            Color rose = C("#d0707a"), roseD = C("#a8505e"), cream = C("#f2e8d0"), green = C("#5f9a6a"), skin = C("#f2cfae"),
                  hair = C("#8a4a2a"), wood = C("#8a5a3a"), froth = C("#fffaf0");
            k.Torso(rose, rose, rose, 0.28f);
            k.ChestPanel(cream, 0.17f, false, null, k.ShoulderY - 0.08f);
            k.Neck(skin);
            k.Head(skin, C("#6a4a30"), hair, EyeStyle.Closed, -6f);
            k.Cheeks(C("#f0a0a0"));
            k.HairCap(hair);
            k.Headscarf(green, green);
            // towel over the right shoulder
            k.M.Bone = BB.Chest; k.M.Color = C("#e8e0d0");
            k.M.Box(new Vector3(k.ShoulderX * 0.7f, k.ShoulderY + 0.01f, 0f), new Vector3(0.1f, 0.03f, 0.26f));
            k.M.Box(new Vector3(k.ShoulderX * 0.75f, k.ShoulderY - 0.1f, -k.ChestR * k.DepthK - 0.01f), new Vector3(0.1f, 0.2f, 0.02f));
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, rose, skin, skin);
                k.PuffSleeve(s, rose, 1.5f);
                k.Leg(s, roseD, skin, C("#6a4a30"), 0.35f);
            }
            k.Skirt(rose, roseD, k.AnkleY + 0.06f, 1.45f, 0f, roseD);
            k.FrontFlap(cream, k.KneeY - 0.12f, 0.3f, null, -1f, 0.03f);
            k.Belt(cream, cream, k.HipY + 0.12f, 1.12f, 0.035f);
            // frothy mug in the left hand
            k.BeginHand(-1, 0.0f);
            k.M.Color = wood;
            k.M.Cylinder(new Vector3(0f, -0.02f, 0f), 0.045f, 0.045f, 0.12f, 8);
            k.M.Color = froth;
            k.M.Sphere(new Vector3(0f, 0.105f, 0f), new Vector3(0.05f, 0.025f, 0.05f), 7, 4);
            k.M.Color = Paint.Shade(wood, 0.7f);
            k.M.Torus(new Vector3(0f, 0.04f, -0.06f), 0.025f, 0.008f, 6, 3);
            k.End();
            var m = k.Model;
            m.HoldL = UnitHold.Carry; m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel Merchant(string key)
        {
            var k = new BipedKit(33, 1.74f, 0.14f, 0.49f, 1.02f, false);
            Color vest = C("#5f8a4a"), shirt = C("#efe3c4"), pants = C("#7a5a3a"), hat = C("#6a4a32"), feather = C("#f08ab0"),
                  skin = C("#e9c4a0"), beard = C("#6a4a2a"), pack = C("#9a7a4a"), leather = C("#5e4030");
            k.Torso(vest, pants, shirt);
            k.Neck(skin);
            k.Head(skin, C("#5a7a4a"), beard, EyeStyle.Round, -3f);
            k.HairCap(beard, 60f, 100f, 118f);
            k.Beard(beard, 0.3f, 0.75f);
            k.BrimHat(hat, hat, 2.5f, 0.85f, 0f, vest, -4f);
            k.M.Bone = BB.Head; k.M.Color = feather;
            k.M.Blade(new Vector3(0.95f * k.R, k.HeadCY + 0.75f * k.R, -0.2f * k.R), new Vector3(1.3f * k.R, k.HeadCY + 1.9f * k.R, -0.9f * k.R), 0.06f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, shirt, shirt, skin);
                k.Cuff(s, shirt, 0.6f, 0.9f, 1.3f);
                k.Leg(s, pants, pants, leather, 0.55f);
            }
            k.Belt(leather, C("#d9b25a"));
            k.Pouch(1, C("#c9a24f"), 0.7f, 1.1f);
            k.Backpack(pack, leather, C("#c96a4a"), C("#5a5a62"));
            var m = k.Model;
            m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel Smith(string key)
        {
            var k = new BipedKit(34, 1.8f, 0.14f, 0.48f, 1.3f, false, 1.15f);
            Color skin = C("#d9a07a"), apron = C("#6a4a32"), shirt = C("#efe3c4"), pants = C("#4a4038"), beard = C("#2a2420"),
                  gloves = C("#4a3a2a"), steel = C("#9aa2aa"), wood = C("#7a5534");
            k.Torso(shirt, pants);
            k.ChestPanel(apron, 0.26f, false, null, k.ShoulderY - 0.1f);
            k.Neck(skin);
            k.Head(skin, C("#5a4030"), beard, EyeStyle.Narrow, 6f);
            k.Beard(beard, 0.5f, 0.9f);
            k.Moustache(beard);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, shirt, skin, gloves);
                k.ArmBand(s, shirt, 0.88f, 1.35f, 0.06f);
                k.Cuff(s, gloves, 0.7f, 0.95f, 1.3f);
                k.Leg(s, pants, pants, C("#3a2e26"), 0.6f);
            }
            k.FrontFlap(apron, k.KneeY - 0.08f, 0.34f, null, -1f, 0.02f);
            k.Belt(apron, steel);
            k.Hammer(1, steel, wood, 0.55f, 0.8f);
            // tongs in the left hand
            k.BeginHand(-1, -0.05f);
            k.M.Color = C("#3a3a40");
            k.M.Segment(new Vector3(-0.012f, 0f, 0f), new Vector3(-0.01f, 0.42f, 0.02f), 0.01f, 0.008f, 4);
            k.M.Segment(new Vector3(0.012f, 0f, 0f), new Vector3(0.02f, 0.42f, 0.0f), 0.01f, 0.008f, 4);
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.HoldL = UnitHold.OneHand; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel VillagerA(string key)
        {
            var k = new BipedKit(35, 1.64f, 0.138f, 0.5f, 0.95f, true);
            Color dress = C("#5a7fc0"), bodice = C("#3f5f9a"), cream = C("#f2e8d0"), yellow = C("#f0c84a"), skin = C("#f2cfae"),
                  hair = C("#7a4a2a"), basket = C("#b08a50");
            k.Torso(bodice, dress);
            k.M.Bone = BB.Chest; k.M.Color = cream;
            for (int i = 0; i < 3; i++)
                k.M.Box(new Vector3(0f, k.ChestY - 0.04f + i * 0.06f, k.ChestR * k.DepthK + 0.008f), new Vector3(0.06f, 0.01f, 0.01f));
            k.Neck(skin);
            k.Head(skin, C("#5a8ac0"), hair, EyeStyle.Round, -4f);
            k.Cheeks(C("#f0a8a8"), 0.8f);
            k.HairCap(hair);
            k.Bangs(hair, 4, 0.35f, 60f);
            k.Headscarf(yellow, yellow);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, cream, skin, skin);
                k.PuffSleeve(s, cream, 1.35f);
                k.Leg(s, dress, skin, C("#6a4a30"), 0.35f);
            }
            k.Skirt(dress, bodice, k.AnkleY + 0.08f, 1.45f, 0f, bodice);
            k.FrontFlap(cream, k.KneeY - 0.1f, 0.26f, null, -1f, 0.03f);
            // flower basket on the left arm
            k.BeginHand(-1, 0f);
            k.M.Color = basket;
            k.M.Lathe(new[] { new Vector2(0.07f, -0.02f), new Vector2(0.11f, 0.08f), new Vector2(0.115f, 0.1f) }, 8, false, true, false);
            k.M.Push().Translate(0f, 0.1f, 0f).Rotate(0f, 0f, 90f);
            k.M.Torus(Vector3.zero, 0.1f, 0.008f, 8, 3);
            k.M.Pop();
            k.M.Color = C("#f2a0b8"); k.M.Sphere(new Vector3(0.03f, 0.11f, 0.02f), 0.035f, 5, 3);
            k.M.Color = C("#fff6e0"); k.M.Sphere(new Vector3(-0.04f, 0.11f, -0.01f), 0.03f, 5, 3);
            k.M.Color = C("#f0d050"); k.M.Sphere(new Vector3(0.0f, 0.12f, -0.05f), 0.03f, 5, 3);
            k.M.Color = C("#7aa05a"); k.M.Blade(new Vector3(0.02f, 0.1f, 0.05f), new Vector3(0.05f, 0.17f, 0.08f), 0.03f);
            k.End();
            var m = k.Model;
            m.HoldL = UnitHold.Carry; m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel VillagerB(string key)
        {
            var k = new BipedKit(36, 1.76f, 0.14f, 0.49f, 1.02f, false);
            Color straw = C("#e8c870"), shirt = C("#efe3c4"), overalls = C("#4a6fa8"), skin = C("#d9a882"), hair = C("#6a4a2a"),
                  wood = C("#8a6a48"), steel = C("#8a929a");
            k.Torso(shirt, overalls);
            k.ChestPanel(overalls, 0.2f, false, null, k.ShoulderY - 0.1f);
            k.Strap(overalls, -1, 0.035f);
            k.Strap(overalls, 1, 0.035f);
            k.Neck(skin);
            k.Head(skin, C("#6a5030"), hair, EyeStyle.Round, 0f);
            k.HairCap(hair);
            k.BrimHat(straw, straw, 2.5f, 0.8f, 0f, C("#c44a3a"), -3f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, shirt, skin, skin);
                k.ArmBand(s, shirt, 0.9f, 1.3f, 0.05f);
                k.Leg(s, overalls, overalls, C("#5a4030"), 0.5f);
            }
            k.Staff(1, 1.6f, wood, BipedKit.StaffTop.Plain, wood, wood);
            k.BeginHand(1, 0f);
            k.M.Color = steel;
            k.M.Box(new Vector3(0f, 0.58f * 1.6f * k.U - 0.02f, -0.07f), new Vector3(0.13f, 0.03f, 0.16f));
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel Child(string key)
        {
            var k = new BipedKit(37, 1.2f, 0.13f, 0.43f, 0.95f, false, 0.95f, 0.95f);
            Color tunic = C("#6fa35a"), tunicD = C("#4f7f42"), cream = C("#f2e8d0"), shorts = C("#7a5a3a"), skin = C("#f2cfae"),
                  hair = C("#7a4a2a"), paper = C("#ffe0a0"), stick = C("#8a6a48");
            k.Torso(tunic, tunicD);
            k.M.Bone = BB.Chest; k.M.Color = cream;
            k.M.Band(Vector3.zero, 0.08f, 0.1f, k.ShoulderY - 0.02f, k.ShoulderY + 0.015f, 10, 0.9f);
            k.Neck(skin);
            k.Head(skin, C("#6a4a30"), hair, EyeStyle.Round, -6f, true, 0.95f);
            k.Cheeks(C("#f0a0a0"));
            k.HairCap(hair);
            k.Spikes(hair, 10, 0.4f, 0.3f, 0.3f, 37, 0.24f);
            k.Bangs(hair, 5, 0.45f, 80f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, tunic, skin, skin);
                k.Leg(s, shorts, skin, C("#6a4a30"), 0.4f);
            }
            k.Skirt(tunic, tunicD, k.HipY - 0.07f, 1.25f, 0f, tunicD);
            k.Belt(C("#6a4a30"), cream, k.HipY + 0.06f, 1.08f, 0.03f);
            // little paper lantern on a stick
            k.BeginHand(1, 0f);
            k.M.Color = stick;
            k.M.Segment(new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.36f, 0.02f), 0.008f, 0.006f, 4);
            k.M.Segment(new Vector3(0f, 0.36f, 0.02f), new Vector3(0f, 0.36f, 0.12f), 0.005f, 0.005f, 3);
            k.M.Emission = 1f; k.M.Color = paper;
            k.M.Sphere(new Vector3(0f, 0.36f, 0.16f), new Vector3(0.045f, 0.045f, 0.055f), 7, 5);
            k.M.Emission = 0f; k.M.Color = C("#c44a3a");
            k.M.Box(new Vector3(0f, 0.36f, 0.215f), new Vector3(0.03f, 0.03f, 0.01f));
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.Carry; m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Throw;
            m.CastBone = BB.HandR;
            m.CastOffset = k.Grip(1) - k.Bind[BB.HandR] + new Vector3(0f, -0.16f, 0.36f);
            m.Breath = 1.3f;
            return Done(k, key);
        }

        static UnitModel Guard(string key)
        {
            var k = new BipedKit(38, 1.8f, 0.14f, 0.49f, 1.1f, false, 1.05f);
            Color steel = C("#aab4bf"), steelD = C("#78838e"), mail = C("#8e98a0"), teal = C("#3f8f8a"), cream = C("#efe3c4"),
                  skin = C("#e9c4a0"), brown = C("#6a4a2a"), wood = C("#7a5534"), pants = C("#4a4a52");
            k.Torso(mail, mail);
            k.ChestPanel(teal, 0.22f, true, cream);
            k.Neck(skin);
            k.Head(skin, C("#5a6a7a"), brown, EyeStyle.Narrow, 8f);
            k.Helmet(steel, 1.75f, steelD);
            k.Moustache(brown);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, mail, mail, C("#5e4030"));
                k.Pauldron(s, steel, 1.15f, null, 2);
                k.Leg(s, pants, steelD, C("#4a3a2a"), 0.62f, steel);
            }
            k.FrontFlap(teal, k.KneeY - 0.05f, 0.22f, cream);
            k.BackFlap(teal, k.KneeY - 0.06f, 0.22f, cream);
            k.Belt(C("#5e4030"), steel);
            k.Spear(1, 2.0f, wood, C("#c8ced4"), C("#c44a3a"));
            k.Shield(teal, cream, cream, true, 1f);
            var m = k.Model;
            m.HoldR = UnitHold.Spear; m.Strike = UnitStrike.Spear; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel Trainer(string key)
        {
            var k = new BipedKit(39, 1.76f, 0.14f, 0.49f, 1.02f, false);
            Color robe = C("#6f8fb0"), robeD = C("#4f6a88"), gold = C("#e2b45a"), plum = C("#7a3f6e"), skin = C("#e9c4a0"),
                  grey = C("#b8b0a8"), book = C("#5a3a2a");
            k.Torso(robe, robeD);
            k.Sash(gold, k.SpineY - 0.02f, 0.07f);
            k.Strap(plum, -1, 0.07f);
            k.Strap(plum, 1, 0.07f);
            k.Neck(skin);
            k.Head(skin, C("#5a6a8a"), grey, EyeStyle.Narrow, 2f);
            k.HairCap(grey, 62f, 98f, 116f);
            k.Beard(grey, 0.3f, 0.8f);
            // hood down: a fold around the shoulders
            k.M.Bone = BB.Chest;
            k.M.Panel(Vector3.zero, 0f, 360f, 10, new[] { new Vector2(0.11f, k.NeckY + 0.02f), new Vector2(k.ShoulderR * 1.1f, k.ShoulderY - 0.04f), new Vector2(k.ShoulderR * 1.2f, k.ShoulderY - 0.12f) }, k.DepthK * 1.12f, robeD, new Color(0, 0, 0, 0));
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, robe, robe, skin);
                k.WideSleeve(s, robe, robeD, 0.28f, 2.1f, gold);
                k.Leg(s, robeD, robeD, C("#3a3040"), 0.35f);
            }
            k.Skirt(robe, robeD, k.AnkleY + 0.05f, 1.45f, 0f, gold);
            k.FrontFlap(plum, k.KneeY - 0.1f, 0.09f, gold, -1f, 0.02f);
            k.Book(-1, book, C("#f2ecd8"));
            var m = k.Model;
            m.HoldL = UnitHold.Book; m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Point;
            return Done(k, key);
        }

        /// <summary>Kodama-like forest spirit: small, pale, glowing, floating, three hollow dots for a face.</summary>
        static UnitModel ForestSpirit(string key)
        {
            var k = new BipedKit(40, 0.9f, 0.2f, 0.22f, 1.3f, false, 0.7f, 0.7f);
            Color body = C("#eef5e6"), green = C("#cfe8c0"), dark = C("#2a3a2a"), leaf = C("#7fc060");
            k.M.Emission = 0.35f;
            k.M.Jitter = 0.03f;
            // round body (no legs)
            k.M.Bone = BB.Hips; k.M.Color = green;
            k.M.Sphere(new Vector3(0f, k.PelvisY - 0.02f, 0f), new Vector3(0.13f, 0.14f, 0.12f), 9, 6);
            k.M.Bone = BB.Chest; k.M.Color = body;
            k.M.Sphere(new Vector3(0f, k.ChestY, 0f), new Vector3(0.12f, 0.15f, 0.11f), 9, 6);
            k.M.Bone = BB.Head; k.M.Color = body;
            k.M.Sphere(new Vector3(0f, k.HeadCY, 0f), new Vector3(k.R * 1.05f, k.R * 0.95f, k.R), 12, 8);
            // three hollow dots
            k.M.Emission = 0f; k.M.Color = dark;
            k.M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
                k.M.Sphere(new Vector3(s * 0.32f * k.R, k.HeadCY + 0.05f * k.R, 0.92f * k.R), new Vector3(0.12f, 0.14f, 0.06f) * k.R, 6, 4);
            k.M.Sphere(new Vector3(0f, k.HeadCY - 0.32f * k.R, 0.9f * k.R), new Vector3(0.1f, 0.12f, 0.06f) * k.R, 6, 4);
            // sprout
            k.M.Emission = 0.3f; k.M.Color = leaf;
            var top = new Vector3(0f, k.HeadCY + 0.92f * k.R, 0f);
            k.M.Segment(top, top + new Vector3(0f, 0.07f, 0f), 0.008f, 0.006f, 4);
            k.M.Blade(top + new Vector3(0f, 0.07f, 0f), top + new Vector3(-0.08f, 0.12f, 0.01f), 0.05f);
            k.M.Blade(top + new Vector3(0f, 0.07f, 0f), top + new Vector3(0.07f, 0.13f, -0.01f), 0.045f);
            // little stub arms
            k.M.Emission = 0.35f;
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = BB.ArmU(s); k.M.Color = body;
                k.M.Segment(k.Bind[BB.ArmU(s)], k.Bind[BB.ArmL(s)], 0.03f, 0.026f, 5);
                k.M.Bone = BB.ArmL(s);
                k.M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.026f, 0.022f, 5);
                k.M.Sphere(k.Bind[BB.Hand(s)] + Vector3.down * 0.01f, 0.03f, 5, 3);
            }
            k.M.Emission = 0f;
            var m = k.Model;
            m.FloatHeight = 0.22f;
            m.BaseFade = 0.8f;
            m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Point;
            m.Dust = false;
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.Radius = 0.24f;
            m.CastBone = BB.Head;
            m.CastOffset = new Vector3(0f, k.HeadCY - k.HeadY + k.R, 0f);
            return UnitModels.Bake(m, k.M);
        }

        // ================================================================== bandits

        static UnitModel Bandit(string key)
        {
            var k = new BipedKit(41, 1.78f, 0.138f, 0.49f, 1f, false);
            Color hood = C("#7a5a3a"), hoodD = C("#4e3824"), red = C("#b8423a"), leather = C("#6a4a32"), leatherD = C("#4a3424"),
                  pants = C("#4a4038"), steel = C("#c0c6cc"), skin = C("#d9a882");
            k.Torso(leather, pants);
            k.Strap(leatherD, -1, 0.05f);
            k.Strap(leatherD, 1, 0.04f);
            k.Neck(skin);
            k.Head(skin, C("#3a3028"), C("#2a2420"), EyeStyle.Narrow, 16f);
            k.HairCap(C("#3a2a22"));
            k.Hood(hood, hoodD, 55f, 0.4f);
            k.Scarf(red, 0.35f, true);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, leather, leatherD, leatherD);
                k.Leg(s, pants, pants, leatherD, 0.7f);
            }
            k.Skirt(leather, leatherD, k.KneeY + 0.02f, 1.2f, 35f);
            k.Belt(leatherD, steel);
            k.Pouch(1, leatherD);
            k.Dagger(1, steel, leatherD, 0.32f);
            k.Dagger(-1, steel, leatherD, 0.3f);
            var m = k.Model;
            m.HoldR = UnitHold.Daggers; m.HoldL = UnitHold.Daggers; m.Strike = UnitStrike.Daggers; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel BanditArcher(string key)
        {
            var k = new BipedKit(42, 1.78f, 0.138f, 0.49f, 0.98f, false);
            Color olive = C("#6f7a3e"), oliveD = C("#4a5228"), scarf = C("#5a4a3a"), leather = C("#6a4a32"), leatherD = C("#4a3424"),
                  pants = C("#4a4038"), skin = C("#d9a882"), wood = C("#7a5534");
            k.Torso(leather, pants);
            k.Neck(skin);
            k.Head(skin, C("#3a3028"), C("#2a2420"), EyeStyle.Narrow, 12f);
            k.HairCap(C("#3a2a22"));
            k.Hood(olive, oliveD, 55f, 0.5f);
            k.Scarf(scarf, 0.3f, true);
            k.Cape(olive, oliveD, k.KneeY - 0.02f, 1f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, olive, leatherD, leatherD);
                k.Leg(s, pants, pants, leatherD, 0.7f);
            }
            k.Belt(leatherD, C("#a8a090"));
            k.Bow(-1, 1.05f, wood, leatherD, C("#efe3c4"));
            k.Quiver(leather, C("#e8e0d0"));
            k.Dagger(1, C("#c0c6cc"), leatherD, 0.24f);
            var m = k.Model;
            m.HoldL = UnitHold.Bow; m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Sword; m.Ranged = UnitRanged.Bow;
            return Done(k, key);
        }

        static UnitModel BanditHexer(string key)
        {
            var k = new BipedKit(43, 1.78f, 0.138f, 0.49f, 0.98f, false);
            Color plum = C("#6a3a5e"), plumD = C("#44243c"), bone = C("#e9e0c8"), wood = C("#5e4030"), hex = C("#b06aff"),
                  skin = C("#d9a882"), leather = C("#4a3424");
            k.Torso(plum, plumD);
            k.Strap(leather, -1, 0.04f);
            k.Neck(skin);
            k.Head(skin, hex, C("#2a2420"), EyeStyle.Glow, 14f);
            k.HairCap(C("#2a2226"));
            k.Hood(plum, plumD, 52f, 0.6f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, plum, plumD, skin);
                k.WideSleeve(s, plum, plumD, 0.24f, 1.9f, bone);
                k.Leg(s, plumD, plumD, leather, 0.5f);
            }
            k.Skirt(plum, plumD, k.AnkleY + 0.1f, 1.4f, 10f, bone);
            k.Belt(leather, bone);
            k.Pouch(1, bone, 0.6f, 0.8f);
            k.Pouch(-1, wood, 0.6f, 0.8f);
            k.M.Bone = BB.Chest; k.M.Color = bone;
            k.M.Beads(new Vector3(-0.08f, k.ShoulderY - 0.04f, k.ChestR * k.DepthK + 0.01f), new Vector3(0.08f, k.ShoulderY - 0.04f, k.ChestR * k.DepthK + 0.01f), 5, 0.018f, 0.018f, bone, wood);
            k.Staff(1, 1.7f, wood, BipedKit.StaffTop.Skull, bone, hex);
            k.HandFlame(-1, C("#f0d8ff"), hex, 0.9f);
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.HoldL = UnitHold.Carry; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            // the hex glows in the free hand
            m.CastBone = BB.HandL;
            m.CastOffset = k.Grip(-1) - k.Bind[BB.HandL] + new Vector3(0f, -0.07f, 0.01f);
            return Done(k, key);
        }

        static UnitModel BanditChief(string key)
        {
            var k = new BipedKit(44, 2.0f, 0.145f, 0.48f, 1.35f, false, 1.18f);
            Color skin = C("#d9a07a"), red = C("#c4442e"), fur = C("#8a6a48"), furL = C("#a8865e"), leather = C("#5e4030"),
                  steel = C("#a8b0b8"), pants = C("#4a4038"), dark = C("#2a2226");
            k.Torso(leather, pants, skin, 0.12f);
            k.Mantle(fur, 1.35f, 0.04f, 13);
            k.Pauldron(1, steel, 1.55f, C("#6a7078"), 3);
            k.Strap(dark, -1, 0.06f);
            k.Neck(skin);
            k.Head(skin, C("#5a4030"), red, EyeStyle.Scar, 18f);
            // eyepatch over the left eye
            k.M.Bone = BB.Head; k.M.Color = dark;
            k.M.Push().Translate(-0.36f * k.R, k.HeadCY - 0.1f * k.R, 0.86f * k.R).Rotate(0f, -20f, 0f);
            k.M.Sphere(Vector3.zero, new Vector3(0.2f, 0.18f, 0.07f) * k.R, 6, 3);
            k.M.Pop();
            k.M.Band(new Vector3(0f, 0f, -0.04f * k.R), 1.04f * k.R, 1.03f * k.R, k.HeadCY + 0.0f, k.HeadCY + 0.05f * k.R, 12);
            // red mohawk + beard
            k.M.Color = red;
            for (int i = 0; i < 7; i++)
            {
                float ph = -50f + i * 22f;
                var at = k.HeadPoint(ph < 0f ? 0f : 180f, Mathf.Abs(ph), 0.96f);
                var dir = (at - new Vector3(0f, k.HeadCY, 0f)).normalized + Vector3.up * 0.4f;
                k.M.Push().Translate(at);
                k.M.Rotate(Quaternion.FromToRotation(Vector3.up, dir.normalized));
                k.M.Push().Scale(new Vector3(0.45f, 1f, 1.4f));
                k.M.Cone(Vector3.zero, 0.24f * k.R, 0.6f * k.R, 5);
                k.M.Pop();
                k.M.Pop();
            }
            k.Beard(red, 0.6f, 1.0f);
            k.Moustache(red);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, skin, leather);
                k.Cuff(s, leather, 0.3f, 0.95f, 1.22f);
                k.Leg(s, pants, pants, leather, 0.7f, furL);
            }
            k.ArmBand(-1, dark, 0.3f, 1.25f, 0.05f);
            k.Skirt(leather, C("#3a2a20"), k.KneeY + 0.05f, 1.25f, 30f, fur);
            k.Belt(dark, steel, -1f, 1.1f, 0.07f);
            k.Axe(1, steel, C("#6a4a32"), 1.05f, 1.45f);
            var m = k.Model;
            m.HoldR = UnitHold.Shoulder; m.Strike = UnitStrike.Mace; m.TwoHanded = true; m.Ranged = UnitRanged.Throw;
            m.TurnRate = 540f;
            return Done(k, key);
        }

        // ================================================================== hollow & forest foes

        static UnitModel HollowSpirit(string key)
        {
            var k = new BipedKit(45, 2.0f, 0.15f, 0.36f, 0.95f, false, 0.95f, 1.35f);
            Color robe = C("#8e8899"), robeD = C("#5e5870"), robeT = C("#c9c2d6"), mask = C("#f2ede4"), glow = C("#c9a8ff"), hand = C("#d8d2e2");
            k.Torso(robe, robeD);
            k.Neck(robeD);
            // pale mask face with hollow glowing eyes, inside a hood
            k.M.Bone = BB.Head; k.M.Color = C("#2a2632");
            k.M.Sphere(new Vector3(0f, k.HeadCY, -0.02f * k.R), new Vector3(k.R, k.R, k.R * 0.95f), 10, 7);
            k.M.Color = mask;
            k.M.Shell(new Vector3(0f, k.HeadCY - 0.05f * k.R, 0.06f * k.R), new Vector3(0.88f, 1.02f, 0.98f) * k.R, -62f, 62f, 8, 25f, 150f, 4);
            k.Eyes(glow, new Color(0, 0, 0, 0), EyeStyle.Glow, 0f, -0.1f, 0.34f, 1.1f);
            k.M.Emission = 0.6f; k.M.Color = glow;
            k.M.Box(new Vector3(0f, k.HeadCY + 0.42f * k.R, 0.95f * k.R), new Vector3(0.06f, 0.18f, 0.03f) * k.R);
            k.M.Emission = 0f;
            k.Hood(robe, robeD, 60f, 0.7f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, robe, hand, hand);
                k.WideSleeve(s, robe, robeD, 0.34f, 2.2f, robeT);
            }
            // long robe fading into tatters near the ground (robe hangs from the hips; the spirit floats)
            k.Skirt(robe, robeD, 0.25f, 1.6f, 0f, robeT);
            k.M.Bone = BB.SkirtB;
            for (int i = 0; i < 10; i++)
            {
                float th = i * 36f * Mathf.Deg2Rad;
                float r = k.HipR * 1.55f;
                var a = new Vector3(Mathf.Sin(th) * r, 0.3f, Mathf.Cos(th) * r * k.DepthK * 1.08f);
                k.M.Color = i % 2 == 0 ? robeT : robe;
                k.M.Blade(a, a + new Vector3(Mathf.Sin(th) * 0.05f, -0.32f - (i % 3) * 0.05f, Mathf.Cos(th) * 0.05f), 0.09f);
            }
            // faint runes on the chest
            k.M.Bone = BB.Chest; k.M.Emission = 0.8f; k.M.Color = glow;
            for (int i = 0; i < 3; i++)
                k.M.Box(new Vector3((i - 1) * 0.06f, k.ChestY + 0.02f * i, k.ChestR * k.DepthK + 0.006f), new Vector3(0.025f, 0.05f, 0.008f));
            k.M.Emission = 0f;
            var m = k.Model;
            m.FloatHeight = 0.3f;
            m.BaseFade = 0.88f;
            m.Strike = UnitStrike.Claw; m.Ranged = UnitRanged.Point;
            m.HoldR = UnitHold.Claws; m.HoldL = UnitHold.Claws;
            m.Dust = false;
            m.CastBone = BB.HandR; m.CastOffset = new Vector3(0f, -0.07f, 0.02f);
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            return UnitModels.Bake(m, k.M);
        }

        /// <summary>Floating grey-violet will-o'-wisp: glowing core, two dark eyes, a flame-like tail.</summary>
        static UnitModel HollowWisp(string key)
        {
            var k = new BipedKit(46, 1.0f, 0.2f, 0.3f, 1f, false);
            Color core = C("#e8dcff"), outer = C("#a99cc8"), violet = C("#c9a8ff"), dark = C("#2a2236");
            // all bones collapse onto the core so the body moves as one; the tail trails
            var center = new Vector3(0f, 0.5f, 0f);
            for (int i = 0; i < BB.Count; i++) k.Bind[i] = center;
            k.Bind[BB.Tail] = center + new Vector3(0f, 0.05f, -0.12f);
            k.Bind[BB.HairB] = center + new Vector3(0f, 0.12f, -0.08f);
            k.M.Jitter = 0.03f;
            k.M.Bone = BB.Hips; k.M.Emission = 1f; k.M.Color = core;
            k.M.Sphere(center, 0.16f, 10, 7);
            k.M.Bone = BB.Chest; k.M.Emission = 0.65f; k.M.Color = outer;
            k.M.Blob(center, new Vector3(0.24f, 0.25f, 0.24f), 1, 0.12f, 3);
            // eyes
            k.M.Bone = BB.Head; k.M.Emission = 0f; k.M.Color = dark;
            k.M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
                k.M.Sphere(center + new Vector3(s * 0.075f, 0.04f, 0.225f), new Vector3(0.035f, 0.05f, 0.02f), 6, 4);
            // flame tail (trails behind and up)
            k.M.Bone = BB.Tail; k.M.Emission = 0.7f;
            for (int i = 0; i < 5; i++)
            {
                k.M.Color = i % 2 == 0 ? violet : outer;
                float a = (i - 2) * 0.35f;
                var b = center + new Vector3(Mathf.Sin(a) * 0.08f, 0.05f, -0.15f);
                k.M.Blade(b, b + new Vector3(Mathf.Sin(a) * 0.18f, 0.22f + (i % 2) * 0.1f, -0.4f - (i % 3) * 0.08f), 0.12f);
            }
            k.M.Bone = BB.HairB; k.M.Color = violet;
            k.M.Blade(center + new Vector3(0f, 0.15f, -0.05f), center + new Vector3(0f, 0.42f, -0.2f), 0.1f);
            k.M.Emission = 0f;
            var m = k.Model;
            m.FloatHeight = 0.55f;
            m.BaseFade = 0.92f;
            m.Strike = UnitStrike.Lunge; m.Ranged = UnitRanged.Pulse;
            m.Dust = false;
            m.Breath = 2f;
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.Radius = 0.26f;
            m.HeadTop = new Vector3(0f, 0.26f, 0f) + (center - k.Bind[BB.Head]);
            m.CenterBone = BB.Hips; m.CenterOffset = Vector3.zero;
            m.CastBone = BB.Hips; m.CastOffset = new Vector3(0f, 0f, 0.15f);
            m.PickBones = new[] { BB.Hips, BB.Tail };
            m.PickPad = 0.25f;
            m.HipY = center.y;
            m.Height = 1.0f;
            return UnitModels.Bake(m, k.M);
        }

        static UnitModel Treant(string key)
        {
            var k = new BipedKit(47, 2.6f, 0.26f, 0.36f, 1.7f, false, 1.15f, 1.25f);
            Color bark = C("#6e6878"), barkD = C("#4a4554"), barkL = C("#8e8899"), glow = C("#c9a8ff"), leaf = C("#9a7a5a"),
                  crystal = C("#b98aff"), moss = C("#7a8a5a");
            float U = k.U;
            k.M.Jitter = 0.08f;
            // trunk body: faceted
            k.M.Bone = BB.Hips; k.M.Color = barkD;
            k.M.Body(Vector3.zero, new[] { new Vector2(k.HipR * 1.1f, k.HipY - 0.1f * U), new Vector2(k.HipR * 1.15f, k.PelvisY + 0.05f * U) }, 7, 0.9f);
            k.M.Bone = BB.Spine; k.M.Color = bark;
            k.M.Body(Vector3.zero, new[] { new Vector2(k.HipR * 1.1f, k.PelvisY - 0.02f * U), new Vector2(k.WaistR * 1.2f, k.SpineY + 0.08f * U) }, 7, 0.9f, false, false);
            k.M.Bone = BB.Chest;
            k.M.Body(Vector3.zero, new[] { new Vector2(k.WaistR * 1.2f, k.SpineY), new Vector2(k.ChestR * 1.15f, k.ChestY), new Vector2(k.ShoulderR * 1.1f, k.ShoulderY), new Vector2(k.ShoulderR * 0.6f, k.NeckY + 0.05f) }, 7, 0.85f, false, true);
            k.M.Color = moss;
            k.M.Blob(new Vector3(-k.ShoulderX * 0.5f, k.ShoulderY + 0.02f, 0f), new Vector3(0.18f, 0.08f, 0.16f) * U, 1, 0.3f, 2);
            k.M.Color = crystal; k.M.Emission = 0.85f;
            k.M.Spike(new Vector3(k.ShoulderX * 0.4f, k.ShoulderY, -0.05f), new Vector3(0.3f, 1f, -0.3f), 0.05f * U, 0.32f * U, 5);
            k.M.Spike(new Vector3(k.ShoulderX * 0.6f, k.ShoulderY - 0.05f, 0.05f), new Vector3(0.7f, 1f, 0.2f), 0.04f * U, 0.22f * U, 5);
            k.M.Emission = 0f;
            // head: top of the trunk with a carved face
            k.M.Bone = BB.Head; k.M.Color = bark;
            k.M.Body(Vector3.zero, new[] { new Vector2(k.R * 0.85f, k.HeadY - 0.02f), new Vector2(k.R * 1.0f, k.HeadCY), new Vector2(k.R * 0.75f, k.H - 0.02f) }, 7, 0.95f, true, true);
            k.M.Emission = 1f; k.M.Color = glow;
            for (int s = -1; s <= 1; s += 2)
                k.M.Sphere(new Vector3(s * 0.38f * k.R, k.HeadCY + 0.08f * k.R, 0.92f * k.R), new Vector3(0.16f, 0.12f, 0.05f) * k.R, 6, 3);
            k.M.Sphere(new Vector3(0f, k.HeadCY - 0.4f * k.R, 0.9f * k.R), new Vector3(0.3f, 0.1f, 0.05f) * k.R, 6, 3);
            k.M.Emission = 0f;
            // branches on the head
            k.M.Color = barkL;
            k.M.Curve(new Vector3(-0.2f * k.R, k.H - 0.05f, 0f), new Vector3(-0.6f * k.R, k.H + 0.25f, 0.05f), new Vector3(-1.2f * k.R, k.H + 0.35f, -0.1f), 0.06f * U, 0.015f, 3, 5);
            k.M.Curve(new Vector3(0.25f * k.R, k.H - 0.05f, -0.05f), new Vector3(0.5f * k.R, k.H + 0.35f, -0.1f), new Vector3(0.7f * k.R, k.H + 0.5f, 0.1f), 0.05f * U, 0.012f, 3, 5);
            k.M.Color = leaf;
            k.M.Blade(new Vector3(-1.1f * k.R, k.H + 0.33f, -0.1f), new Vector3(-1.25f * k.R, k.H + 0.2f, 0.05f), 0.08f);
            k.M.Blade(new Vector3(0.68f * k.R, k.H + 0.48f, 0.1f), new Vector3(0.85f * k.R, k.H + 0.4f, 0.2f), 0.08f);
            // branch arms with twig fingers
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = BB.ArmU(s); k.M.Color = bark;
                k.M.Segment(k.Bind[BB.ArmU(s)] + Vector3.up * 0.05f, k.Bind[BB.ArmL(s)], 0.12f * U, 0.09f * U, 6);
                k.M.Bone = BB.ArmL(s);
                k.M.Sphere(k.Bind[BB.ArmL(s)], 0.09f * U, 6, 4);
                k.M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.09f * U, 0.07f * U, 6);
                k.M.Bone = BB.Hand(s); k.M.Color = barkL;
                var h = k.Bind[BB.Hand(s)];
                for (int f = 0; f < 3; f++)
                    k.M.Curve(h, h + new Vector3(s * (f - 1) * 0.05f, -0.12f, 0.06f), h + new Vector3(s * (f - 1) * 0.1f, -0.28f, 0.12f), 0.035f * U, 0.008f, 2, 4);
                // root legs
                k.M.Bone = BB.LegU(s); k.M.Color = barkD;
                k.M.Segment(k.Bind[BB.LegU(s)] + Vector3.up * 0.05f, k.Bind[BB.LegL(s)], 0.13f * U, 0.11f * U, 6);
                k.M.Bone = BB.LegL(s);
                k.M.Sphere(k.Bind[BB.LegL(s)], 0.11f * U, 6, 4);
                k.M.Segment(k.Bind[BB.LegL(s)], k.Bind[BB.Foot(s)], 0.11f * U, 0.12f * U, 6);
                k.M.Bone = BB.Foot(s);
                var ft = k.Bind[BB.Foot(s)];
                for (int r = 0; r < 3; r++)
                {
                    float a = (r - 1) * 0.6f;
                    k.M.Curve(ft, ft + new Vector3(Mathf.Sin(a) * 0.12f, -0.02f, Mathf.Cos(a) * 0.12f + 0.02f), new Vector3(ft.x + Mathf.Sin(a) * 0.22f, 0.02f, Mathf.Cos(a) * 0.24f), 0.05f * U, 0.02f, 2, 4);
                }
            }
            var m = k.Model;
            m.Heavy = 1f; m.StrideK = 1.0f; m.MaxCadence = 1.5f; m.TurnRate = 220f;
            m.Strike = UnitStrike.Slam; m.Ranged = UnitRanged.Point;
            m.DustColor = new Color(0.8f, 0.76f, 0.7f, 0.4f);
            m.CastBone = BB.Head; m.CastOffset = new Vector3(0f, k.HeadCY - k.HeadY, 0.9f * k.R);
            return Done(k, key, UnitGait.Heavy);
        }

        static UnitModel Mossling(string key, bool shaman)
        {
            float H = shaman ? 1.0f : 0.9f;
            var k = new BipedKit(shaman ? 48 : 49, H, 0.22f, 0.24f, 1.4f, false, 1.1f, 0.85f);
            Color moss = C("#7d9a55"), mossD = C("#5a7a3e"), mossL = C("#a8bc8a"), leaf = C("#5f9a4a"), leafD = C("#3f6a32"),
                  eye = C("#2a2a20"), teeth = C("#f4f0e0"), feet = C("#6a5a3a");
            float r = k.R;
            k.M.Jitter = 0.08f;
            // round mossy body = head + belly in one ball
            k.M.Bone = BB.Hips; k.M.Color = mossD;
            k.M.Sphere(new Vector3(0f, k.PelvisY + 0.01f, 0f), new Vector3(0.2f, 0.17f, 0.18f) * (H / 0.9f), 8, 5);
            k.M.Bone = BB.Head; k.M.Color = moss;
            var hc = new Vector3(0f, k.HeadCY - 0.15f * r, 0f);
            k.M.Blob(hc, new Vector3(r * 1.15f, r * 1.05f, r * 1.05f), 1, 0.1f, shaman ? 5 : 6);
            k.M.Color = mossL;
            k.M.Blob(hc + new Vector3(0.3f * r, 0.5f * r, -0.4f * r), new Vector3(0.4f, 0.3f, 0.4f) * r, 0, 0.3f, 7);
            k.M.Blob(hc + new Vector3(-0.5f * r, 0.2f * r, -0.5f * r), new Vector3(0.35f, 0.3f, 0.35f) * r, 0, 0.3f, 8);
            // big shiny eyes
            k.M.Jitter = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                var e = hc + new Vector3(s * 0.38f * r, 0.12f * r, 0.88f * r);
                k.M.Color = C("#f4f0e0");
                k.M.Sphere(e, new Vector3(0.24f, 0.28f, 0.14f) * r, 7, 5);
                k.M.Color = eye;
                k.M.Sphere(e + new Vector3(0f, -0.02f * r, 0.08f * r), new Vector3(0.17f, 0.2f, 0.1f) * r, 6, 4);
                k.M.Emission = 0.6f; k.M.Color = Color.white;
                k.M.Sphere(e + new Vector3(s * 0.05f * r, 0.08f * r, 0.17f * r), 0.05f * r, 4, 2);
                k.M.Emission = 0f;
            }
            // toothy grin
            k.M.Color = C("#3a2a20");
            k.M.Sphere(hc + new Vector3(0f, -0.38f * r, 0.9f * r), new Vector3(0.42f, 0.12f, 0.08f) * r, 7, 3);
            k.M.Color = teeth;
            for (int i = -2; i <= 2; i++)
                k.M.Box(hc + new Vector3(i * 0.12f * r, -0.33f * r, 0.95f * r), new Vector3(0.07f, 0.08f, 0.04f) * r);
            k.M.Jitter = 0.08f;
            // big leaf hat
            k.M.Color = leaf;
            var hatP = hc + new Vector3(0f, 1.0f * r, 0f);
            k.M.Push().Translate(hatP).Rotate(-8f, 25f, 6f);
            k.M.Push().Scale(new Vector3(1f, 0.25f, 1.35f));
            k.M.Sphere(Vector3.zero, r * 0.95f, 9, 4, false);
            k.M.Pop();
            k.M.Color = leafD;
            k.M.Box(new Vector3(0f, 0.06f * r, 0f), new Vector3(0.05f * r, 0.03f * r, 2.4f * r));
            k.M.Segment(new Vector3(0f, 0.05f * r, -1.25f * r), new Vector3(0f, 0.25f * r, -1.5f * r), 0.03f * r, 0.02f * r, 4);
            if (shaman)
            {
                k.M.Color = C("#f4f0e6"); k.M.Blade(new Vector3(0.3f * r, 0.1f * r, -0.4f * r), new Vector3(0.6f * r, 0.9f * r, -0.8f * r), 0.12f * r);
                k.M.Color = C("#c84a3a"); k.M.Blade(new Vector3(0.1f * r, 0.1f * r, -0.5f * r), new Vector3(0.25f * r, 1.0f * r, -0.95f * r), 0.12f * r);
            }
            k.M.Pop();
            if (shaman)
            {
                k.M.Bone = BB.Head;
                k.M.Beads(hc + new Vector3(-0.6f * r, -0.75f * r, 0.5f * r), hc + new Vector3(0.6f * r, -0.75f * r, 0.5f * r), 6, 0.08f * r, 0.08f * r, C("#e8dcc0"), C("#5fd0c8"));
            }
            // stubby arms and feet
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = BB.ArmU(s); k.M.Color = moss;
                k.M.Segment(k.Bind[BB.ArmU(s)], k.Bind[BB.ArmL(s)], 0.045f, 0.04f, 5);
                k.M.Bone = BB.ArmL(s);
                k.M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.04f, 0.035f, 5);
                k.M.Bone = BB.Hand(s); k.M.Color = mossD;
                k.M.Sphere(k.Bind[BB.Hand(s)] + Vector3.down * 0.025f, 0.045f, 6, 4);
                k.M.Bone = BB.LegU(s); k.M.Color = mossD;
                k.M.Segment(k.Bind[BB.LegU(s)] + Vector3.up * 0.02f, k.Bind[BB.LegL(s)], 0.055f, 0.045f, 5);
                k.M.Bone = BB.LegL(s);
                k.M.Segment(k.Bind[BB.LegL(s)], k.Bind[BB.Foot(s)], 0.045f, 0.04f, 5);
                k.M.Bone = BB.Foot(s); k.M.Color = feet;
                k.M.Sphere(new Vector3(k.Bind[BB.Foot(s)].x, 0.035f, 0.03f), new Vector3(0.055f, 0.04f, 0.075f), 6, 4);
            }
            if (shaman) k.Staff(1, 0.95f, C("#7a5a3a"), BipedKit.StaffTop.Bead, C("#7aa05a"), C("#5fe8d8"));
            var m = k.Model;
            m.HoldR = shaman ? UnitHold.Staff : UnitHold.Relaxed;
            m.Strike = shaman ? UnitStrike.Staff : UnitStrike.Fist;
            m.Ranged = shaman ? UnitRanged.Point : UnitRanged.Throw;
            m.DustColor = new Color(0.72f, 0.8f, 0.55f, 0.3f);
            var res = Done(k, key, UnitGait.Small);
            res.HeadTop = hatP + new Vector3(0f, 0.15f * r, 0f) - k.Bind[BB.Head];
            res.Radius = 0.26f * H / 0.9f;
            return res;
        }

        // ================================================================== humanoid demons

        static UnitModel Imp(string key)
        {
            var k = new BipedKit(51, 0.9f, 0.16f, 0.36f, 0.9f, false, 0.9f, 1.0f);
            Color red = C("#d65a3a"), redD = C("#a03a28"), horn = C("#3a2a2a"), belly = C("#f08a5a"), fire = C("#ffb040"), core = C("#fff0b0");
            k.Torso(red, redD, belly, 0.25f);
            k.Neck(red);
            k.Head(red, C("#ffe080"), horn, EyeStyle.Glow, 18f, false);
            k.M.Bone = BB.Head; k.M.Color = red;
            for (int s = -1; s <= 1; s += 2)
                k.M.Spike(new Vector3(s * 0.85f * k.R, k.HeadCY + 0.05f * k.R, -0.05f * k.R), new Vector3(s * 1f, 0.25f, -0.2f), 0.18f * k.R, 0.8f * k.R, 4);
            k.Horns(horn, 0.7f, 0.2f, 1f, 0.16f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, red, red, redD);
                k.Leg(s, red, red, redD, 0f);
            }
            // bat wings + devil tail
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = s < 0 ? BB.WingL : BB.WingR; k.M.Color = redD;
                var w0 = k.Bind[s < 0 ? BB.WingL : BB.WingR];
                k.M.Segment(w0, w0 + new Vector3(s * 0.22f, 0.16f, -0.05f), 0.012f, 0.008f, 4);
                k.M.QuadTwoSided(w0, w0 + new Vector3(s * 0.22f, 0.16f, -0.05f), w0 + new Vector3(s * 0.26f, -0.05f, -0.06f), w0 + new Vector3(s * 0.08f, -0.1f, -0.03f));
            }
            k.M.Bone = BB.Tail; k.M.Color = red;
            var t0 = k.Bind[BB.Tail];
            k.M.Curve(t0, t0 + new Vector3(0f, -0.12f, -0.15f), t0 + new Vector3(0.05f, 0.05f, -0.3f), 0.018f, 0.01f, 4, 5);
            k.M.Color = horn;
            k.M.Spike(t0 + new Vector3(0.05f, 0.05f, -0.3f), new Vector3(0.2f, 0.6f, -0.5f), 0.03f, 0.06f, 3);
            k.HandFlame(1, core, fire, 1.0f);
            var m = k.Model;
            m.HoldR = UnitHold.Carry; m.Strike = UnitStrike.Claw; m.Ranged = UnitRanged.Throw;
            m.Wings = true;
            m.DustColor = new Color(0.86f, 0.78f, 0.64f, 0.25f);
            return Done(k, key, UnitGait.Small);
        }

        static UnitModel Voidwalker(string key)
        {
            var k = new BipedKit(52, 2.2f, 0.17f, 0.34f, 1.7f, false, 1.3f, 1.15f);
            Color voidC = C("#5a5ad0"), voidD = C("#34306e"), smoke = C("#7a6ae0"), gold = C("#e2b45a"), eyes = C("#e8f0ff");
            k.M.Emission = 0.25f;
            k.Torso(voidC, voidD);
            k.Neck(voidD);
            k.M.Emission = 0f;
            k.M.Bone = BB.Head; k.M.Color = C("#1e1c3a");
            k.M.Sphere(new Vector3(0f, k.HeadCY, 0f), new Vector3(k.R, k.R, k.R * 0.95f), 10, 7);
            k.Eyes(eyes, new Color(0, 0, 0, 0), EyeStyle.Glow, 0f, -0.05f, 0.33f, 1.1f);
            k.Hood(voidD, C("#1e1c3a"), 58f, 0.35f);
            k.M.Emission = 0.25f;
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, voidC, voidC, voidD);
                k.M.Emission = 0f;
                k.Cuff(s, gold, 0.4f, 0.95f, 1.3f);
                k.M.Emission = 0.25f;
            }
            // no legs: a smoky tail
            k.M.Emission = 0.45f;
            k.M.Bone = BB.Hips; k.M.Color = voidD;
            k.M.Lathe(new[] { new Vector2(k.HipR * 1.05f, k.PelvisY + 0.05f), new Vector2(k.HipR * 0.8f, k.HipY - 0.25f), new Vector2(0.12f, 0.35f) }, 8, false, false, false);
            k.M.Bone = BB.SkirtB; k.M.Color = smoke;
            k.M.Lathe(new[] { new Vector2(0.14f, 0.4f), new Vector2(0.09f, 0.18f), new Vector2(0f, 0.02f) }, 7, false, true, false);
            k.M.Blade(new Vector3(0f, 0.35f, -0.1f), new Vector3(0.05f, 0.05f, -0.45f), 0.14f);
            k.M.Emission = 0f;
            var m = k.Model;
            m.FloatHeight = 0.25f;
            m.Strike = UnitStrike.Slam; m.Ranged = UnitRanged.Point;
            m.Dust = false; m.TurnRate = 420f;
            k.Finish(key, UnitGait.Floater);
            m.Legs = new UnitLeg[0];
            m.Heavy = 0.5f;
            return UnitModels.Bake(m, k.M);
        }

        static UnitModel Succubus(string key)
        {
            var k = new BipedKit(53, 1.85f, 0.135f, 0.51f, 0.9f, true);
            Color skin = C("#c9a8d8"), horn = C("#1e1a22"), hair = C("#24202e"), gown = C("#6a3a9a"), gownD = C("#42245e"),
                  armor = C("#2e2838"), gold = C("#e2b45a"), whip = C("#3a2a30");
            k.Skin = skin;
            k.Torso(armor, gownD);
            k.ChestPanel(gown, 0.18f, true, gold);
            k.Neck(skin);
            k.Head(skin, C("#ffd060"), hair, EyeStyle.Narrow, 10f);
            k.HairCap(hair);
            k.Bangs(hair, 5, 0.45f);
            k.LongBack(hair, k.SpineY, 1.05f, 1.2f);
            k.Horns(horn, 1.0f, 0.5f, 0.7f, 0.15f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, armor, skin);
                k.Cuff(s, gold, 0.85f, 0.95f, 1.3f);
                k.Pauldron(s, armor, 1.0f, gold, 1);
                k.Leg(s, gownD, skin, armor, 0.85f);
            }
            k.Skirt(gown, gownD, k.AnkleY + 0.03f, 1.55f, 18f, gold);
            k.Belt(armor, gold);
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = s < 0 ? BB.WingL : BB.WingR; k.M.Color = C("#3a2a4a");
                var w0 = k.Bind[s < 0 ? BB.WingL : BB.WingR];
                var tip = w0 + new Vector3(s * 0.45f, 0.42f, -0.12f);
                k.M.Segment(w0, tip, 0.02f, 0.01f, 4);
                k.M.QuadTwoSided(w0, tip, w0 + new Vector3(s * 0.55f, -0.05f, -0.15f), w0 + new Vector3(s * 0.18f, -0.3f, -0.08f));
            }
            k.M.Bone = BB.Tail; k.M.Color = skin;
            var t0 = k.Bind[BB.Tail];
            k.M.Curve(t0, t0 + new Vector3(0f, -0.3f, -0.2f), t0 + new Vector3(0.1f, -0.15f, -0.45f), 0.025f, 0.012f, 4, 5);
            k.M.Color = horn;
            k.M.Spike(t0 + new Vector3(0.1f, -0.15f, -0.45f), new Vector3(0.3f, 0.3f, -1f), 0.04f, 0.08f, 3);
            // coiled whip in the right hand
            k.BeginHand(1, 0f);
            k.M.Color = whip;
            k.M.Cylinder(new Vector3(0f, -0.08f, 0f), 0.016f, 0.016f, 0.18f, 6);
            k.M.Push().Translate(0f, -0.15f, 0.06f);
            k.M.Torus(Vector3.zero, 0.07f, 0.01f, 10, 3);
            k.M.Torus(new Vector3(0f, -0.02f, 0f), 0.06f, 0.01f, 10, 3);
            k.M.Pop();
            k.End();
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Whip; m.Ranged = UnitRanged.Point;
            m.Wings = true;
            return Done(k, key);
        }

        static UnitModel Infernal(string key)
        {
            var k = new BipedKit(54, 2.6f, 0.15f, 0.38f, 1.9f, false, 1.35f, 1.2f);
            Color rock = C("#3a3436"), rockL = C("#57504e"), lava = C("#ff7a2a"), core = C("#ffd060"), flame = C("#ff9a3a");
            float U = k.U;
            k.M.Jitter = 0.1f;
            k.Torso(rock, rock);
            // molten core + lava cracks
            k.M.Bone = BB.Chest; k.M.Emission = 1f; k.M.Color = core;
            k.M.Sphere(new Vector3(0f, k.ChestY, k.ChestR * k.DepthK * 0.75f), new Vector3(0.12f, 0.12f, 0.1f) * U, 7, 5);
            k.M.Color = lava;
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.26f;
                k.M.Push().Translate(Mathf.Sin(a) * k.ChestR * 0.7f, k.ChestY + Mathf.Cos(a) * 0.12f * U, k.ChestR * k.DepthK * 0.98f).Rotate(0f, 0f, a * 50f);
                k.M.Box(Vector3.zero, new Vector3(0.025f, 0.16f, 0.02f) * U);
                k.M.Pop();
            }
            k.M.Emission = 0f;
            // rock plates on the shoulders and back
            k.M.Color = rockL;
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = BB.ArmU(s);
                k.M.Blob(k.Bind[BB.ArmU(s)] + new Vector3(s * 0.05f, 0.05f, 0f) * U, new Vector3(0.2f, 0.16f, 0.2f) * U, 0, 0.25f, 11 + s);
            }
            k.M.Bone = BB.Chest;
            k.M.Blob(new Vector3(0f, k.ChestY + 0.1f * U, -k.ChestR * k.DepthK), new Vector3(0.28f, 0.22f, 0.12f) * U, 0, 0.25f, 4);
            // small head with a flame crown and glowing eyes
            k.M.Bone = BB.Head; k.M.Color = rock;
            k.M.Blob(new Vector3(0f, k.HeadCY, 0f), new Vector3(k.R * 1.1f, k.R, k.R * 1.05f), 0, 0.15f, 6);
            k.M.Emission = 1f; k.M.Color = core;
            for (int s = -1; s <= 1; s += 2) k.M.Sphere(new Vector3(s * 0.35f * k.R, k.HeadCY, 0.95f * k.R), new Vector3(0.18f, 0.1f, 0.06f) * k.R, 5, 3);
            k.M.Color = flame;
            for (int i = 0; i < 5; i++)
            {
                float a = (i - 2) * 0.45f;
                var at = new Vector3(Mathf.Sin(a) * 0.6f * k.R, k.HeadCY + 0.7f * k.R, Mathf.Cos(a) * 0.3f * k.R - 0.2f * k.R);
                k.M.Blade(at, at + new Vector3(Mathf.Sin(a) * 0.08f, 0.3f + (i % 2) * 0.12f, 0f), 0.1f);
            }
            k.M.Emission = 0f;
            // massive arms and fists, stumpy legs
            for (int s = -1; s <= 1; s += 2)
            {
                k.M.Bone = BB.ArmU(s); k.M.Color = rock;
                k.M.Segment(k.Bind[BB.ArmU(s)], k.Bind[BB.ArmL(s)], 0.13f * U, 0.11f * U, 6);
                k.M.Bone = BB.ArmL(s); k.M.Color = rockL;
                k.M.Segment(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.12f * U, 0.13f * U, 6);
                k.M.Emission = 0.9f; k.M.Color = lava;
                k.M.Box(Vector3.Lerp(k.Bind[BB.ArmL(s)], k.Bind[BB.Hand(s)], 0.5f) + new Vector3(s * 0.11f * U, 0f, 0f), new Vector3(0.02f, 0.12f, 0.03f) * U);
                k.M.Emission = 0f;
                k.M.Bone = BB.Hand(s); k.M.Color = rock;
                k.M.Blob(k.Bind[BB.Hand(s)] + Vector3.down * 0.1f * U, new Vector3(0.17f, 0.17f, 0.17f) * U, 0, 0.2f, 21 + s);
                k.Leg(s, rock, rockL, rock, 0f);
            }
            var m = k.Model;
            m.Heavy = 0.9f; m.MaxCadence = 1.5f; m.TurnRate = 260f;
            m.Strike = UnitStrike.Slam; m.Ranged = UnitRanged.Throw;
            m.DustColor = new Color(0.55f, 0.45f, 0.4f, 0.4f);
            m.CastBone = BB.Chest; m.CastOffset = new Vector3(0f, 0f, k.ChestR * k.DepthK);
            return Done(k, key, UnitGait.Heavy);
        }
    }
}
