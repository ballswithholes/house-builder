// Model recipes: sprite key → procedurally modelled, rigged unit (see BipedKit / CreatureKit).
//
// Player classes follow the class identities (Warrior plate + sword & shield in crimson/steel, Hunter hooded ranger
// with bow & quiver, Paladin white-gold plate with hammer & shield, Mage robe + pointed hat + orb staff, Priest white
// & gold robes with a ringed staff, Rogue dark leathers + mask + twin daggers, Warlock dark robe + horned cowl + skull
// staff with fel-green glow, Shaman mail & fur with feathers and a totem staff). Companions/NPCs/creatures follow
// Docs/ArtPrompts.md. Colours are sRGB hex (the shaders convert).
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class UnitRecipes
    {
        static Color C(string h) => Paint.Hex(h);

        /// <summary>Number of look variations of a key (UnitModels.Get(key, variant), UnitView.SetVariant); 1 = none.</summary>
        public static int VariantCount(string key)
        {
            switch (key)
            {
                case "npc_villager_a":
                case "npc_villager_b":
                case "npc_child":
                    return 16;
            }
            return 1;
        }

        public static UnitModel Build(string key)
        {
            // "key#n": look variation n of a generic villager (see VariantCount)
            int hash = key.IndexOf('#');
            if (hash > 0)
            {
                string baseKey = key.Substring(0, hash);
                if (!int.TryParse(key.Substring(hash + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v)) v = 0;
                switch (baseKey)
                {
                    case "npc_villager_a": return VillagerA(key, v);
                    case "npc_villager_b": return VillagerB(key, v);
                    case "npc_child": return Child(key, v);
                }
                return Build(baseKey);
            }
            switch (key)
            {
                // ---- player classes
                case "char_warrior": return Warrior(key);
                case "char_hunter": return Hunter(key);
                case "char_paladin": return Paladin(key);
                case "char_mage": return Mage(key);
                case "char_priest": return Priest(key);
                case "char_rogue": return Rogue(key);
                case "char_warlock": return Warlock(key);
                case "char_shaman": return Shaman(key);
                // ---- companions
                case "comp_kael": return Kael(key);
                case "comp_lys": return Lys(key);
                case "comp_seren": return Seren(key);
                case "comp_rook": return Rook(key);
                case "comp_pip": return Pip(key);
                case "comp_torvan": return Torvan(key);
                case "comp_aldric": return Aldric(key);
                case "comp_morwen": return Morwen(key);
                // ---- NPCs
                case "npc_elder": return Elder(key);
                case "npc_innkeeper": return Innkeeper(key);
                case "npc_merchant": return Merchant(key);
                case "npc_smith": return Smith(key);
                case "npc_villager_a": return VillagerA(key);
                case "npc_villager_b": return VillagerB(key);
                case "npc_child": return Child(key);
                case "npc_guard": return Guard(key);
                case "npc_trainer": return Trainer(key);
                case "npc_spirit": return ForestSpirit(key);
                // ---- humanoid foes & demons
                case "cr_bandit": return Bandit(key);
                case "cr_bandit_archer": return BanditArcher(key);
                case "cr_bandit_hexer": return BanditHexer(key);
                case "cr_bandit_chief": return BanditChief(key);
                case "cr_hollow_spirit": return HollowSpirit(key);
                case "cr_hollow_keeper": return HollowKeeper(key);
                case "cr_hollow_wisp": return HollowWisp(key);
                case "cr_hollow_treant": return Treant(key);
                case "cr_mossling": return Mossling(key, false);
                case "cr_mossling_shaman": return Mossling(key, true);
                case "demon_imp": return Imp(key);
                case "demon_voidwalker": return Voidwalker(key);
                case "demon_succubus": return Succubus(key);
                case "demon_infernal": return Infernal(key);
                // ---- beasts
                case "cr_wolf": return Wolf(key, WolfKind.Grey);
                case "cr_wolf_blighted": return Wolf(key, WolfKind.Blighted);
                case "pet_wolf": return Wolf(key, WolfKind.Pet);
                case "demon_felhunter": return Wolf(key, WolfKind.Felhunter);
                case "cr_boar": return Boar(key, false);
                case "pet_boar": return Boar(key, true);
                case "pet_cat": return Cat(key);
                case "pet_bear": return Bear(key);
                case "pet_owl": return Owl(key);
                case "cr_spider": return Spider(key);
                case "cr_hollow_warden": return Warden(key);
                case "sheep": return Sheep(key);
                // ---- static
                case "cr_training_dummy": return Dummy(key);
                case "totem_earth": return Totem(key, 0);
                case "totem_fire": return Totem(key, 1);
                case "totem_water": return Totem(key, 2);
                case "totem_air": return Totem(key, 3);
                case "fx_rune_circle": return Trap(key);
                case "prop_spirit_lantern": return Lightwell(key);
            }
            // unknown keys: a sensible generic model of the right family
            if (key.StartsWith("cr_owl")) return Owl(key);
            if (key.StartsWith("cr_wolf") || key.StartsWith("pet_") || key.StartsWith("cr_beast")) return Wolf(key, WolfKind.Grey);
            if (key.StartsWith("cr_spider")) return Spider(key);
            if (key.StartsWith("cr_bandit")) return Bandit(key);
            if (key.StartsWith("cr_hollow")) return HollowSpirit(key);
            if (key.StartsWith("demon_")) return Imp(key);
            if (key.StartsWith("totem_")) return Totem(key, 0);
            if (key.StartsWith("fx_")) return Trap(key);
            if (key.StartsWith("prop_")) return Lightwell(key);
            if (key.StartsWith("cr_")) return Wolf(key, WolfKind.Grey);
            return VillagerB(key);
        }

        static UnitModel Done(BipedKit k, string key, UnitGait gait = UnitGait.Humanoid)
        {
            k.Finish(key, gait);
            return UnitModels.Bake(k.Model, k.M);
        }

        static void Glint(BipedKit k, bool on) { k.M.Emission = on ? 0.9f : 0f; }

        // ================================================================== player classes

        static UnitModel Warrior(string key)
        {
            var k = new BipedKit(11, 1.8f, 0.14f, 0.49f, 1.1f, false, 1.06f);
            Color steel = C("#aab4bf"), steelD = C("#6d7885"), crimson = C("#b8323c"), crimsonD = C("#7a1e29"), gold = C("#e3b65c"),
                  leather = C("#6b4630"), skin = C("#eec39e"), hair = C("#9a5a34"), hairL = C("#b06c3e"), sash = C("#e0a848");
            k.Torso(steel, crimsonD);
            k.ChestPanel(crimson, 0.22f, true, gold);
            k.Neck(skin);
            k.Head(skin, C("#c98b2e"), C("#6e3e24"), EyeStyle.Narrow, 12f);
            k.HairCap(hair, 55f, 96f, 118f);
            k.Spikes(hair, 6, 0.5f, 0.45f, 0.9f, 7);
            k.Bangs(hair, 4, 0.38f, 60f);
            // FFX hero hair: three bold locks swept up and back from the brow, one flicking out at the nape
            float r = k.R;
            k.M.Bone = BB.Head;
            k.M.Color = hairL;
            var l0 = k.HeadPoint(4f, 30f, 0.92f);
            k.M.Curve(l0, l0 + new Vector3(0.02f * r, 0.72f * r, 0.12f * r), l0 + new Vector3(0.08f * r, 0.88f * r, -1.25f * r), 0.36f * r, 0.03f * r, 3, 5);
            k.M.Color = hair;
            var l1 = k.HeadPoint(58f, 48f, 0.92f);
            k.M.Curve(l1, l1 + new Vector3(0.4f * r, 0.5f * r, -0.1f * r), l1 + new Vector3(0.68f * r, 0.5f * r, -1.3f * r), 0.3f * r, 0.03f * r, 3, 5);
            var l2 = k.HeadPoint(-62f, 52f, 0.92f);
            k.M.Curve(l2, l2 + new Vector3(-0.38f * r, 0.45f * r, -0.1f * r), l2 + new Vector3(-0.64f * r, 0.42f * r, -1.25f * r), 0.29f * r, 0.03f * r, 3, 5);
            var l3 = k.HeadPoint(170f, 72f, 0.9f);
            k.M.Curve(l3, l3 + new Vector3(0.05f * r, 0.1f * r, -0.5f * r), l3 + new Vector3(0.12f * r, 0.36f * r, -0.85f * r), 0.26f * r, 0.03f * r, 3, 5);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, steel, steelD, leather);
                k.Cuff(s, gold, 0.8f, 0.95f, 1.25f);
                k.Leg(s, crimsonD, steelD, leather, 0.62f, steel);
            }
            // asymmetric armour: one oversized layered pauldron on the shield shoulder, a small one on the sword arm
            k.BigPauldron(1, steel, gold, crimsonD, 1.75f);
            k.Pauldron(-1, steel, 1.0f, gold, 2);
            k.Skirt(crimson, crimsonD, k.KneeY + 0.06f, 1.3f, 0f, gold);
            k.Belt(leather, gold);
            k.SashTails(1, sash, crimsonD, 0.4f);
            k.Cape(crimson, crimsonD, k.KneeY - 0.05f, 0.95f, gold);
            k.Sword(1, 0.78f, 0.075f, C("#dfe4ea"), leather, gold);
            k.Shield(crimson, steel, gold, false, 1f, C("#8a5e3c"));
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Sword; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel Hunter(string key)
        {
            var k = new BipedKit(12, 1.7f, 0.136f, 0.5f, 0.95f, true);
            Color forest = C("#3f6146"), forestL = C("#5f8a5a"), forestD = C("#2c4433"), leather = C("#8a5a3a"), leatherD = C("#5e3c27"),
                  skin = C("#eec39e"), hair = C("#8b4a2b"), cream = C("#efe3c4"), boot = C("#4a3222");
            k.Torso(leather, leatherD);
            k.Strap(leatherD, -1, 0.04f);
            k.Neck(skin);
            k.Head(skin, C("#4f8f5a"), hair, EyeStyle.Round, 4f);
            k.HairCap(hair);
            k.SideLocks(hair, k.ShoulderY - 0.02f, 0.18f);
            k.Hood(forest, forestD, 60f, 0.55f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, forestL, leather, skin);
                k.Cuff(s, leatherD, 0.35f, 0.95f, 1.22f);
                k.Leg(s, leatherD, leatherD, boot, 0.72f, leather);
            }
            k.Skirt(forest, forestD, k.KneeY + 0.06f, 1.25f, 32f, forestL);
            k.Belt(leatherD, C("#d9b25a"));
            k.Pouch(1, leather);
            k.Pouch(-1, leather, 0.2f, 0.8f);
            k.Bow(-1, 1.25f, C("#9a6b3d"), leatherD, cream);
            k.Quiver(leather, cream);
            // the hunting knife lives on the right hip and is drawn for melee only (the right hand draws the bowstring)
            k.SheathedDagger(C("#d0d6dc"), leatherD, leather, 0.26f, C("#d9b25a"));
            var m = k.Model;
            m.HoldL = UnitHold.Bow; m.HoldR = UnitHold.Relaxed; m.Strike = UnitStrike.Sword; m.Ranged = UnitRanged.Bow;
            return Done(k, key);
        }

        static UnitModel Paladin(string key)
        {
            var k = new BipedKit(13, 1.82f, 0.14f, 0.49f, 1.12f, false, 1.1f);
            Color white = C("#eef0f3"), whiteD = C("#c3c9d4"), gold = C("#e8b04f"), blue = C("#4a6fb3"), blueD = C("#2f4a80"),
                  tabard = C("#f7f1e1"), skin = C("#f2cfae"), hair = C("#e8c46a"), brown = C("#7a5534");
            k.Torso(white, whiteD);
            k.ChestPanel(tabard, 0.22f, true, gold);
            k.Neck(skin);
            k.Head(skin, C("#4a7ad0"), Paint.Shade(hair, 0.8f), EyeStyle.Round, 6f);
            k.HairCap(hair, 60f, 98f, 116f);
            k.Bangs(hair, 5, 0.34f, 75f, 0.5f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, white, whiteD, C("#d9b26a"));
                k.Cuff(s, gold, 0.75f, 0.95f, 1.28f);
                k.Pauldron(s, white, 1.5f, gold, 3);
                k.Leg(s, whiteD, white, whiteD, 0.66f, gold);
            }
            k.FrontFlap(tabard, k.KneeY - 0.06f, 0.24f, blue);
            k.BackFlap(tabard, k.KneeY - 0.06f, 0.24f, blue);
            k.Belt(gold, white);
            k.Cape(blue, blueD, k.KneeY - 0.12f, 1.05f, gold);
            k.Hammer(1, C("#d4d8de"), brown, 0.78f, 1f, gold);
            k.Shield(blue, gold, gold);
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel Mage(string key)
        {
            var k = new BipedKit(14, 1.7f, 0.136f, 0.5f, 0.92f, true);
            Color violet = C("#6a4fa3"), violetD = C("#46336f"), blueL = C("#8fa7e8"), gold = C("#e2b45a"), skin = C("#f6d7bd"),
                  hair = C("#2d2a3e"), boot = C("#3d2b4f"), wood = C("#6b4a35");
            k.Torso(violet, violetD);
            k.Collar(violetD, blueL, 0.12f, 35f, 1.3f);
            k.Sash(gold, k.SpineY - 0.02f, 0.06f);
            k.Neck(skin);
            k.Head(skin, C("#9b6ee0"), hair, EyeStyle.Round, 2f);
            k.HairCap(hair);
            k.Bangs(hair, 5, 0.4f);
            k.LongBack(hair, k.ShoulderY - 0.2f, 1f, 1.1f);
            // brim kept narrow and tipped back so the face shows under it from the 44° game camera
            k.BrimHat(violetD, violet, 1.95f, 2.6f, 0.65f, gold, -12f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, violet, violet, skin);
                k.WideSleeve(s, violet, blueL, 0.3f, 2.3f, gold);
                k.Leg(s, violetD, violetD, boot, 0.4f);
            }
            k.Skirt(violet, blueL, k.AnkleY + 0.08f, 1.55f, 0f, gold);
            k.Staff(1, 1.75f, wood, BipedKit.StaffTop.Orb, gold, C("#8fd0ff"));
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            return Done(k, key);
        }

        static UnitModel Priest(string key)
        {
            var k = new BipedKit(15, 1.68f, 0.136f, 0.5f, 0.9f, true);
            Color white = C("#f6f1e6"), cream = C("#e6dcc4"), gold = C("#e8c060"), skin = C("#f6d7bd"), hair = C("#c99a5b"),
                  boot = C("#d8ccb4"), wood = C("#efe6d2");
            k.Torso(white, cream);
            k.Sash(gold, k.SpineY - 0.03f, 0.08f);
            k.Neck(skin);
            k.Head(skin, C("#4fa3a0"), Paint.Shade(hair, 0.8f), EyeStyle.Round, -4f);
            k.HairCap(hair);
            k.Bangs(hair, 5, 0.4f);
            k.LongBack(hair, k.ChestY + 0.02f, 1f, 1.1f);
            k.SideLocks(hair, k.ShoulderY + 0.02f, 0.16f);
            // golden circlet
            k.M.Bone = BB.Head; k.M.Color = gold;
            k.M.Push().Translate(0f, k.HeadCY + 0.42f * k.R, -0.02f * k.R).Rotate(-8f, 0f, 0f);
            k.M.Torus(Vector3.zero, 1.08f * k.R, 0.04f * k.R, 14, 4);
            k.M.Pop();
            Glint(k, true); k.M.Color = C("#9fe0ff");
            k.M.Sphere(new Vector3(0f, k.HeadCY + 0.45f * k.R, 1.05f * k.R), 0.07f * k.R, 5, 3);
            Glint(k, false);
            k.Cape(cream, gold, k.ChestY - 0.06f, 1.08f, gold);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, white, white, skin);
                k.WideSleeve(s, white, cream, 0.3f, 2.2f, gold);
                k.Leg(s, cream, cream, boot, 0.35f);
            }
            k.Skirt(white, cream, k.AnkleY + 0.06f, 1.5f, 0f, gold);
            k.FrontFlap(gold, k.KneeY - 0.14f, 0.11f, white, -1f, 0.01f);
            k.Staff(1, 1.72f, wood, BipedKit.StaffTop.Ring, gold, C("#fff0b0"));
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            return Done(k, key);
        }

        static UnitModel Rogue(string key)
        {
            var k = new BipedKit(16, 1.74f, 0.138f, 0.5f, 0.95f, false, 0.96f);
            Color charcoal = C("#3a3540"), charcoalL = C("#57505e"), plum = C("#7a3f6e"), plumD = C("#52284a"), leather = C("#4a3a34"),
                  steel = C("#c8ced4"), skin = C("#e9bf9b"), hair = C("#2a2a30");
            k.Torso(charcoal, charcoalL);
            k.Strap(plum, 1, 0.05f);
            k.Strap(leather, -1, 0.04f);
            k.Neck(skin);
            k.Head(skin, C("#e0a640"), hair, EyeStyle.Narrow, 10f);
            k.HairCap(hair);
            k.Bangs(hair, 4, 0.45f, 60f);
            k.Hood(charcoal, plumD, 54f, 0.45f);
            k.Scarf(plum, 0.5f, true);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, charcoalL, leather, charcoal);
                k.Cuff(s, plumD, 0.5f, 0.95f, 1.22f);
                k.Leg(s, charcoal, charcoal, leather, 0.78f);
                k.Pouch(s, leather, 0.35f, 0.9f);
            }
            k.Skirt(charcoalL, plumD, k.KneeY - 0.02f, 1.2f, 40f);
            k.Belt(leather, steel);
            k.Dagger(1, steel, leather, 0.32f);
            k.Dagger(-1, steel, leather, 0.3f);
            var m = k.Model;
            m.HoldR = UnitHold.Daggers; m.HoldL = UnitHold.Daggers; m.Strike = UnitStrike.Daggers; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel Warlock(string key)
        {
            var k = new BipedKit(17, 1.78f, 0.138f, 0.49f, 1f, false);
            Color robe = C("#2f2638"), robeL = C("#4a3a5a"), purple = C("#6b3f8f"), fel = C("#7cf06a"), bone = C("#e9e0c8"),
                  skin = C("#e8d5c8"), wood = C("#3a2c26");
            k.Torso(robeL, robe);
            k.ChestPanel(purple, 0.16f, true, fel);
            k.Collar(robe, purple, 0.16f, 40f, 1.45f);
            k.Neck(skin);
            k.Head(skin, fel, C("#2a2030"), EyeStyle.Glow, 14f);
            k.HairCap(C("#3a3046"));
            k.Hood(robe, purple, 56f, 0.25f);
            k.Horns(bone, 1.0f, 0.4f, 0.85f, 0.17f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, robeL, robe, skin);
                k.WideSleeve(s, robe, purple, 0.3f, 2.1f, purple);
                Glint(k, true);
                k.Cuff(s, fel, 0.88f, 0.96f, 1.32f);
                Glint(k, false);
                k.Leg(s, robe, robe, C("#22202a"), 0.5f);
            }
            k.Skirt(robe, purple, k.AnkleY + 0.05f, 1.5f, 16f, purple);
            k.Belt(C("#2a1f2a"), bone);
            k.Pouch(-1, bone, 0.4f, 0.8f);
            k.Staff(1, 1.75f, wood, BipedKit.StaffTop.Skull, bone, fel);
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            return Done(k, key);
        }

        static UnitModel Shaman(string key)
        {
            var k = new BipedKit(18, 1.82f, 0.14f, 0.49f, 1.12f, false, 1.05f);
            Color teal = C("#3f8f8a"), tealD = C("#2a5f5c"), mail = C("#8e98a0"), fur = C("#d5c3a0"), furD = C("#8a6a48"),
                  brown = C("#7a5232"), leather = C("#5e4030"), skin = C("#c98e62"), hair = C("#3a2a22"), wood = C("#7a5232");
            k.Torso(mail, brown);
            k.Mantle(fur, 1.1f);
            k.Strap(leather, -1, 0.05f);
            k.Neck(skin);
            k.Head(skin, C("#6aa0b8"), hair, EyeStyle.Round, 8f);
            k.HairCap(hair, 58f, 100f, 125f);
            for (int s = -1; s <= 1; s += 2)
                k.Braid(hair, k.HeadPoint(s * 80f, 95f, 1.02f), k.HeadPoint(s * 75f, 95f, 1.0f) + new Vector3(s * 0.02f, -0.3f, 0.05f), 5, 0.17f, teal);
            // feather headband
            k.M.Bone = BB.Head; k.M.Color = teal;
            k.M.Band(new Vector3(0f, 0f, -0.02f * k.R), 1.1f * k.R, 1.09f * k.R, k.HeadCY + 0.3f * k.R, k.HeadCY + 0.42f * k.R, 12);
            var fb = new Vector3(0f, k.HeadCY + 0.4f * k.R, -1.0f * k.R);
            k.M.Color = C("#f4f0e6"); k.M.Blade(fb, fb + new Vector3(-0.08f, 0.22f, -0.1f), 0.06f);
            k.M.Color = C("#c84a3a"); k.M.Blade(fb, fb + new Vector3(0.02f, 0.26f, -0.12f), 0.06f);
            k.M.Color = teal; k.M.Blade(fb, fb + new Vector3(0.1f, 0.2f, -0.08f), 0.055f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, leather, skin);
                k.ArmBand(s, mail, 0.15f, 1.35f, 0.12f);
                k.Cuff(s, leather, 0.3f, 0.95f, 1.25f);
                k.Leg(s, brown, furD, leather, 0.7f);
                k.LegCuff(s, fur, 0.3f, 1.3f, true);
            }
            k.FrontFlap(teal, k.KneeY - 0.05f, 0.22f, C("#d9b25a"));
            k.BackFlap(teal, k.KneeY - 0.08f, 0.24f, C("#d9b25a"));
            k.Belt(leather, teal);
            k.Pouch(1, furD);
            k.Staff(1, 1.7f, wood, BipedKit.StaffTop.Totem, teal, C("#7ff0e0"));
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            return Done(k, key);
        }

        // ================================================================== companions

        static UnitModel Kael(string key)
        {
            var k = new BipedKit(21, 1.86f, 0.14f, 0.49f, 1.12f, false, 1.08f);
            Color coat = C("#8e2a2e"), coatD = C("#5c1a20"), lining = C("#3a2a2a"), plate = C("#4f4a52"), mail = C("#8a8f95"),
                  gold = C("#c9a24f"), skin = C("#d9a882"), hair = C("#4a4542"), grey = C("#a8a39c"), leather = C("#4a3a30");
            k.Torso(plate, mail);
            k.Strap(coat, -1, 0.1f);
            k.Strap(coat, 1, 0.1f);
            k.Collar(coat, lining, 0.2f, 28f, 1.2f);
            k.Neck(skin);
            k.Head(skin, C("#c9902e"), hair, EyeStyle.Scar, 14f);
            k.HairCap(hair, 50f, 94f, 125f);
            k.Spikes(hair, 8, 0.48f, 0.05f, 1.3f, 21, 0.24f);
            k.Spikes(grey, 3, 0.44f, 0.05f, 1.3f, 5, 0.2f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, coat, coat, leather);
                k.Cuff(s, gold, 0.72f, 0.82f, 1.35f);
                k.Cuff(s, coatD, 0.82f, 0.98f, 1.42f);
                k.Leg(s, mail, plate, C("#3a302a"), 0.6f, plate);
            }
            k.Skirt(coat, lining, k.KneeY - 0.22f, 1.42f, 26f, gold);
            k.Belt(leather, gold);
            k.Belt(leather, gold, k.HipY + 0.17f, 1.12f, 0.035f);
            k.HipItem(-1, C("#b0a080"), 0.12f, 0.04f, C("#6a5a4a"));
            k.Greatsword(1, 1.3f, C("#b8bec4"), C("#3a2a22"), C("#6a5a4a"));
            var m = k.Model;
            m.HoldR = UnitHold.Shoulder; m.Strike = UnitStrike.Greatsword; m.Ranged = UnitRanged.Throw; m.TwoHanded = true;
            return Done(k, key);
        }

        static UnitModel Lys(string key)
        {
            var k = new BipedKit(22, 1.72f, 0.136f, 0.5f, 0.9f, true);
            Color ink = C("#2b2a36"), slate = C("#4a4d5e"), belt = C("#3a3446"), silver = C("#c8ccd6"), fur = C("#e8e2d6"),
                  skin = C("#f3e3d6"), hair = C("#1f1d26");
            k.Torso(slate, ink);
            k.Mantle(fur, 0.9f, 0.05f);
            k.Strap(belt, -1, 0.05f);
            k.Strap(belt, 1, 0.05f);
            k.Neck(skin);
            k.Head(skin, C("#8a5ab0"), hair, EyeStyle.Narrow, 4f);
            k.HairCap(hair);
            k.Bangs(hair, 6, 0.42f, 80f);
            for (int s = -1; s <= 1; s += 2)
                k.Braid(hair, k.HeadPoint(s * 62f, 80f, 1.02f), new Vector3(s * 0.11f, k.ChestY - 0.02f, 0.13f), 7, 0.2f, silver);
            var bun = new Vector3(0f, k.HeadCY + 0.1f * k.R, -1.0f * k.R);
            k.Bun(hair, bun, 0.42f);
            k.M.Color = silver;
            k.M.Segment(bun + new Vector3(-0.09f, 0.06f, 0f), bun + new Vector3(0.12f, -0.04f, -0.02f), 0.006f, 0.004f, 4);
            k.M.Segment(bun + new Vector3(-0.1f, -0.03f, 0f), bun + new Vector3(0.11f, 0.07f, -0.02f), 0.006f, 0.004f, 4);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, ink, ink, skin);
                k.Cuff(s, fur, 0.78f, 0.98f, 1.35f);
                k.Leg(s, ink, ink, C("#22202a"), 0.4f);
            }
            k.Skirt(slate, ink, 0.03f, 1.8f, 0f, belt);
            k.Belt(belt, silver, k.HipY + 0.08f, 1.1f, 0.04f);
            k.Belt(belt, silver, k.HipY + 0.15f, 1.06f, 0.035f);
            k.Staff(1, 1.75f, C("#3a3040"), BipedKit.StaffTop.Orb, silver, C("#7fb2ff"), 0.08f);
            k.Book(-1, C("#6a8a5a"), C("#f2ecd8"));
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.HoldL = UnitHold.Book; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            return Done(k, key);
        }

        static UnitModel Seren(string key)
        {
            var k = new BipedKit(23, 1.66f, 0.136f, 0.5f, 0.9f, true);
            Color pale = C("#cfe2f3"), blueT = C("#6f9fd6"), violet = C("#8a6fc0"), violetD = C("#6a50a0"), lav = C("#c4b0e8"),
                  gold = C("#e2b45a"), pink = C("#f0a0b8"), skin = C("#f6d7bd"), hair = C("#2a2420"), white = C("#f4f2ee");
            k.Torso(pale, violet);
            k.Sash(lav, k.SpineY - 0.03f, 0.12f, true, lav);
            k.Neck(skin);
            k.Head(skin, C("#4fa3a0"), hair, EyeStyle.Round, -3f);
            k.Cheeks(C("#f2b0b0"), 0.8f);
            k.HairCap(hair);
            k.Bangs(hair, 5, 0.4f);
            k.Ponytail(hair, k.ChestY - 0.06f, 0.32f, 0.7f, gold);
            // Yuna's wrapped lock: a long lock beside the right ear, bound in white cloth, falling to the shoulder
            // (it used to cross the face like a bandage)
            k.M.Bone = BB.Head;
            var wt = k.HeadPoint(90f, 74f, 1.07f);
            var wb = new Vector3(wt.x - 0.012f, k.ShoulderY - 0.07f, wt.z + 0.06f);
            k.M.Color = hair;
            k.M.Segment(wt + new Vector3(0f, 0.01f, -0.01f), wb + Vector3.down * 0.07f, 0.026f, 0.008f, 5);
            k.M.Color = white;
            k.M.Segment(wt + Vector3.down * 0.03f, wb, 0.032f, 0.029f, 6);
            k.M.Color = C("#5fc0c0");
            k.M.Segment(wb + Vector3.up * 0.012f, wb + Vector3.down * 0.006f, 0.034f, 0.031f, 6);
            k.M.Segment(wt + Vector3.down * 0.022f, wt + Vector3.down * 0.04f, 0.035f, 0.034f, 6);
            k.M.Sphere(k.HeadPoint(-55f, 50f, 1.1f), 0.022f, 5, 3);
            k.M.Sphere(k.HeadPoint(-62f, 58f, 1.1f), 0.018f, 5, 3);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, pale, pale, skin);
                k.WideSleeve(s, pale, white, 0.34f, 2.5f, pink);
                k.Leg(s, violetD, skin, C("#3a3040"), 0.7f);
            }
            k.Skirt(violet, lav, k.AnkleY + 0.04f, 1.5f, 0f, violetD);
            k.Staff(1, 1.75f, gold, BipedKit.StaffTop.Ring, gold, C("#fff0c0"));
            var m = k.Model;
            m.HoldR = UnitHold.Staff; m.Strike = UnitStrike.Staff; m.Ranged = UnitRanged.Point;
            return Done(k, key);
        }

        static UnitModel Rook(string key)
        {
            var k = new BipedKit(24, 1.9f, 0.142f, 0.49f, 1.22f, false, 1.15f);
            Color skin = C("#b8784e"), hair = C("#d0742e"), bandana = C("#3f6fb8"), vest = C("#4a8fd0"), seven = C("#f2e8c8"),
                  fur = C("#cdb894"), shorts = C("#e88a3a"), sash = C("#c43a2e"), wood = C("#d8c8a8"), leather = C("#6a4a32");
            k.Torso(vest, shorts, skin, 0.05f);
            k.M.Bone = BB.Chest; k.M.Color = seven;
            float bz = -k.ChestR * k.DepthK - 0.012f;
            k.M.Box(new Vector3(0f, k.ChestY + 0.1f, bz), new Vector3(0.16f, 0.035f, 0.012f));
            k.M.Push().Translate(0.02f, k.ChestY - 0.01f, bz).Rotate(0f, 0f, -22f);
            k.M.Box(Vector3.zero, new Vector3(0.035f, 0.2f, 0.012f));
            k.M.Pop();
            k.Neck(skin);
            k.Head(skin, C("#5fa05a"), hair, EyeStyle.Round, -2f);
            k.HairCap(hair);
            k.Headscarf(bandana, bandana);
            for (int i = 0; i < 3; i++)
            {
                float th = 150f + i * 30f;
                k.Braid(hair, k.HeadPoint(th, 100f, 1.05f), k.HeadPoint(th, 100f, 1.05f) + new Vector3((i - 1) * 0.04f, -0.32f, -0.06f), 6, 0.16f, C("#f0d070"), BB.HairB);
            }
            k.Pauldron(-1, fur, 1.3f, null, 1);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, skin, skin, skin);
                k.Cuff(s, leather, 0.55f, 0.95f, 1.2f);
                k.Leg(s, shorts, skin, leather, 0.28f);
                k.LegCuff(s, shorts, 0.72f, 1.45f);
            }
            k.Sash(sash, k.HipY + 0.12f, 0.08f);
            k.FrontFlap(sash, k.KneeY + 0.1f, 0.07f, null, -1f, 0.02f);
            // the ringball on the hip
            k.M.Bone = BB.SkirtR; k.M.Color = C("#e8d090");
            k.M.Sphere(new Vector3(k.HipR * 1.3f + 0.06f, k.HipY - 0.04f, -0.02f), 0.1f, 8, 6);
            k.M.Color = C("#3f6fb8");
            k.M.Push().Translate(k.HipR * 1.3f + 0.06f, k.HipY - 0.04f, -0.02f).Rotate(0f, 0f, 90f);
            k.M.Torus(Vector3.zero, 0.1f, 0.012f, 10, 3);
            k.M.Pop();
            k.Bow(-1, 1.3f, wood, leather, C("#efe3c4"));
            k.Quiver(leather, C("#f2e8c8"));
            var m = k.Model;
            m.HoldL = UnitHold.Bow; m.Strike = UnitStrike.Fist; m.Ranged = UnitRanged.Bow;
            return Done(k, key);
        }

        static UnitModel Pip(string key)
        {
            var k = new BipedKit(25, 1.5f, 0.138f, 0.47f, 0.9f, true);
            Color skin = C("#d9a070"), hair = C("#f0cf5a"), brass = C("#c9973e"), lens = C("#5fd0c8"), pink = C("#f06a9a"),
                  top = C("#3fb8b0"), shorts = C("#7a4fa8"), yellow = C("#f0c040"), belt = C("#6a4a30"), boots = C("#5a3a28");
            k.Torso(top, shorts, skin);
            k.Neck(skin);
            k.Head(skin, C("#4fc070"), Paint.Shade(hair, 0.75f), EyeStyle.Round, -6f);
            k.Cheeks(C("#f0a0a0"), 0.8f);
            k.HairCap(hair);
            k.Bangs(hair, 6, 0.42f, 85f);
            for (int s = -1; s <= 1; s += 2) k.Bun(hair, k.HeadPoint(s * 55f, 32f, 1.1f), 0.38f);
            k.Braid(hair, k.HeadPoint(180f, 80f, 1.05f), k.HeadPoint(180f, 80f, 1.05f) + new Vector3(0f, -0.25f, -0.06f), 5, 0.15f, pink, BB.HairB);
            // three-lens goggles on the forehead
            k.M.Bone = BB.Head; k.M.Color = belt;
            k.M.Band(new Vector3(0f, 0f, -0.02f * k.R), 1.12f * k.R, 1.11f * k.R, k.HeadCY + 0.35f * k.R, k.HeadCY + 0.47f * k.R, 12);
            for (int i = -1; i <= 1; i++)
            {
                var at = k.HeadPoint(i * 30f, 62f, 1.06f);
                var nrm = (at - new Vector3(0f, k.HeadCY, 0f)).normalized;
                k.M.Color = brass;
                k.M.Aim(at, nrm);
                k.M.Cylinder(Vector3.zero, 0.035f, 0.033f, 0.035f, 8);
                k.M.Emission = 0.5f; k.M.Color = lens;
                k.M.Cylinder(new Vector3(0f, 0.035f, 0f), 0.026f, 0.026f, 0.004f, 8);
                k.M.Emission = 0f;
                k.M.Pop();
            }
            k.Scarf(pink, 0.6f);
            k.Arm(-1, yellow, yellow, C("#4a3a30"));
            k.Arm(1, skin, skin, C("#4a3a30"));
            k.ArmBand(1, brass, 0.55f, 1.25f, 0.04f);
            k.Cuff(1, belt, 0.75f, 0.92f, 1.2f);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Leg(s, shorts, skin, boots, 0.85f);
                k.LegCuff(s, shorts, 0.35f, 1.25f);
                k.Pouch(s, C("#8a6a40"), 0.5f, 0.9f);
                k.Pouch(s, C("#8a6a40"), 0.15f, 0.8f);
            }
            k.Belt(belt, brass);
            k.Dagger(1, C("#d0d6dc"), belt, 0.26f, true, brass);
            k.Dagger(-1, C("#d0d6dc"), belt, 0.24f, true, brass);
            var m = k.Model;
            m.HoldR = UnitHold.Daggers; m.HoldL = UnitHold.Daggers; m.Strike = UnitStrike.Daggers; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel Torvan(string key)
        {
            var k = new BipedKit(26, 2.1f, 0.15f, 0.48f, 1.25f, false, 1.12f, 1.05f);
            // slate-blue Ronso fur with a cream mane (grey fur read as unpainted plaster next to stone)
            Color fur = C("#6f84b0"), furL = C("#b8c6e0"), mane = C("#f2e6c8"), horn = C("#e8dcc0"), mark = C("#d8573a"),
                  moss = C("#6f8f4a"), gold = C("#d9b25a"), leather = C("#6a4a32"), wood = C("#8a6a48");
            k.Skin = fur;
            k.Torso(fur, moss);
            k.Mantle(mane, 1.25f, 0.04f, 9);
            k.Strap(moss, -1, 0.07f);
            k.Neck(fur);
            k.Head(fur, C("#e8b84a"), mane, EyeStyle.Narrow, 10f, false, 1.1f);
            // feline muzzle + nose
            k.M.Bone = BB.Head; k.M.Color = furL;
            k.M.Sphere(new Vector3(0f, k.HeadCY - 0.38f * k.R, 0.72f * k.R), new Vector3(0.42f * k.R, 0.32f * k.R, 0.38f * k.R), 8, 5);
            k.M.Color = C("#3a3038");
            k.M.Sphere(new Vector3(0f, k.HeadCY - 0.22f * k.R, 1.06f * k.R), new Vector3(0.12f, 0.07f, 0.06f) * k.R * 1.6f, 5, 3);
            // tribal markings
            k.M.Color = mark;
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 2; i++)
                {
                    k.M.Push().Translate(s * 0.62f * k.R, k.HeadCY - (0.05f + i * 0.14f) * k.R, 0.72f * k.R).Rotate(0f, s * 38f, s * 12f);
                    k.M.Box(Vector3.zero, new Vector3(0.28f * k.R, 0.05f * k.R, 0.05f * k.R));
                    k.M.Pop();
                }
            k.HairCap(mane, 40f, 92f, 140f, 1.12f);
            k.LongBack(mane, k.ShoulderY - 0.05f, 1.1f, 1.2f);
            k.Horns(horn, 1.0f, 0.3f, 0.5f, 0.2f, true);
            for (int s = -1; s <= 1; s += 2)
            {
                var hp = k.HeadPoint(s * 95f, 75f, 1.5f);
                k.M.Bone = BB.Head;
                k.M.Beads(hp, hp + new Vector3(0f, -0.22f, 0.02f), 4, 0.025f, 0.02f, C("#c84a3a"), C("#f0d070"));
                k.M.Color = C("#f4f0e6");
                k.M.Blade(hp + new Vector3(0f, -0.22f, 0f), hp + new Vector3(s * 0.03f, -0.36f, -0.03f), 0.04f);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, fur, fur, furL);
                k.Cuff(s, gold, 0.85f, 0.95f, 1.25f);
                k.Leg(s, fur, fur, Paint.Shade(fur, 0.85f), 0.5f);
            }
            k.FrontFlap(moss, k.KneeY - 0.1f, 0.24f, gold);
            k.BackFlap(moss, k.KneeY - 0.12f, 0.26f, gold);
            k.Belt(leather, gold);
            k.Pouch(1, wood, 0.6f, 1f);
            k.Pouch(-1, C("#c84a3a"), 0.3f, 0.8f);
            // tail with a mane tuft
            k.M.Bone = BB.Tail; k.M.Color = fur;
            var t0 = k.Bind[BB.Tail];
            k.M.Curve(t0, t0 + new Vector3(0f, -0.2f, -0.25f), t0 + new Vector3(0f, -0.55f, -0.3f), 0.05f, 0.035f, 4, 6);
            k.M.Color = mane;
            k.M.Blob(t0 + new Vector3(0f, -0.6f, -0.3f), new Vector3(0.07f, 0.11f, 0.07f), 0, 0.2f, 3);
            k.Spear(1, 2.1f, wood, C("#b9c4cc"), C("#c84a3a"));
            var m = k.Model;
            m.HoldR = UnitHold.Spear; m.Strike = UnitStrike.Spear; m.Ranged = UnitRanged.Point;
            return Done(k, key);
        }

        static UnitModel Aldric(string key)
        {
            var k = new BipedKit(27, 1.78f, 0.14f, 0.5f, 1f, false, 1.02f);
            Color gold = C("#f0c040"), sky = C("#6fb0e8"), skyD = C("#4a80c0"), cream = C("#f2e8d0"), steel = C("#c9ced6"),
                  leather = C("#8a5a3a"), skin = C("#f2cfae"), hair = C("#f0c860"), pants = C("#4a5a7a");
            k.Torso(C("#d8dde3"), pants);
            k.ChestPanel(gold, 0.2f, true, sky);
            k.Strap(gold, -1, 0.09f);
            k.Neck(skin);
            k.Head(skin, C("#4a8ae0"), Paint.Shade(hair, 0.75f), EyeStyle.Round, -6f);
            k.HairCap(hair, 60f, 98f, 118f);
            k.Spikes(hair, 14, 0.5f, 0.6f, 0.45f, 27);
            k.Bangs(hair, 5, 0.42f, 80f, 0.6f);
            k.Pauldron(-1, steel, 1.25f, gold, 2);
            k.Arm(-1, steel, steel, leather);
            k.Arm(1, skin, skin, skin);
            k.Cuff(1, leather, 0.35f, 0.92f, 1.25f);
            for (int s = -1; s <= 1; s += 2) k.Leg(s, pants, steel, leather, 0.7f, steel);
            k.FrontFlap(gold, k.KneeY - 0.02f, 0.22f, sky);
            k.BackFlap(gold, k.KneeY - 0.04f, 0.22f, sky);
            k.Belt(leather, gold);
            k.Cape(sky, skyD, k.KneeY + 0.04f, 0.95f, gold);
            k.Hammer(1, steel, leather, 0.72f, 0.95f, gold);
            k.Shield(cream, gold, gold);
            var m = k.Model;
            m.HoldR = UnitHold.OneHand; m.Strike = UnitStrike.Mace; m.Ranged = UnitRanged.Throw;
            return Done(k, key);
        }

        static UnitModel Morwen(string key)
        {
            var k = new BipedKit(28, 1.74f, 0.136f, 0.5f, 0.9f, true);
            // told apart from Lys (black hair, ink robes) at a glance: silver-white hair, a wine-red gold-trimmed coat
            // and a gold crescent diadem
            Color plum = C("#7a2a40"), plumD = C("#4a1a2a"), violet = C("#a04a6a"), silver = C("#d9b25a"), hair = C("#ece6f2"),
                  skin = C("#f0e2da"), rune = C("#c9a8ff"), book = C("#4a3a6a"), gold = C("#e2b45a");
            k.Torso(plum, plumD);
            k.Collar(plum, gold, 0.18f, 30f, 1.4f);
            k.Neck(skin);
            k.Head(skin, C("#a070e0"), C("#b8aec8"), EyeStyle.Narrow, 6f);
            k.HairCap(hair);
            k.Bangs(hair, 5, 0.45f, 70f);
            k.LongBack(hair, k.ChestY - 0.12f, 1.05f, 1.15f);
            k.SideLocks(hair, k.ShoulderY - 0.01f, 0.15f);
            // crescent diadem: a gold band over the brow, a crescent rising at the front with a violet gem
            k.M.Bone = BB.Head; k.M.Color = gold;
            k.M.Push().Translate(0f, k.HeadCY + 0.4f * k.R, -0.02f * k.R).Rotate(-10f, 0f, 0f);
            k.M.Torus(Vector3.zero, 1.1f * k.R, 0.045f * k.R, 14, 4);
            k.M.Pop();
            var dc = k.HeadPoint(0f, 34f, 1.14f);
            for (int s = -1; s <= 1; s += 2)
                k.M.Curve(dc + new Vector3(0f, -0.01f, 0f), dc + new Vector3(s * 0.05f, 0.015f, 0.01f), dc + new Vector3(s * 0.065f, 0.075f, -0.005f), 0.014f, 0.004f, 3, 5);
            Glint(k, true); k.M.Color = rune;
            k.M.Sphere(dc + new Vector3(0f, 0.004f, 0.008f), 0.016f, 6, 4);
            Glint(k, false);
            for (int s = -1; s <= 1; s += 2)
            {
                k.Arm(s, plum, plum, skin);
                k.Cuff(s, silver, 0.86f, 0.96f, 1.28f);
                k.Leg(s, plumD, plumD, C("#2a2028"), 0.7f);
            }
            k.Skirt(plum, violet, k.AnkleY + 0.1f, 1.45f, 24f, silver);
            // glowing runes along the coat hem
            Glint(k, true); k.M.Color = rune;
            for (int i = 0; i < 9; i++)
            {
                float th = -150f + i * 37.5f;
                if (Mathf.Abs(th) < 30f) continue;
                int bone = th < -88f || th > 88f ? BB.SkirtB : (th < 0f ? BB.SkirtL : BB.SkirtR);
                k.M.Bone = bone;
                float r = k.HipR * 1.45f + 0.01f;
                float a = th * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a) * r, k.AnkleY + 0.2f, Mathf.Cos(a) * r * k.DepthK * 1.08f);
                k.M.Push().Translate(p).Rotate(0f, th, 45f);
                k.M.Box(Vector3.zero, new Vector3(0.035f, 0.035f, 0.01f));
                k.M.Pop();
            }
            Glint(k, false);
            k.Belt(C("#2a1f2a"), silver, k.HipY + 0.08f, 1.1f, 0.04f);
            k.Belt(C("#2a1f2a"), silver, k.HipY + 0.16f, 1.06f, 0.035f);
            k.Book(-1, book, C("#f2ecd8"), true, rune);
            k.HandFlame(1, C("#f4dcff"), violet, 1f);
            // the imp familiar on her left shoulder, holding an inkpot
            k.M.Bone = BB.Chest;
            var ip = new Vector3(-k.ShoulderX * 0.85f, k.ShoulderY + 0.07f, -0.02f);
            var red = C("#d65a3a");
            k.M.Color = red;
            k.M.Sphere(ip, new Vector3(0.045f, 0.055f, 0.045f), 6, 4);
            k.M.Sphere(ip + new Vector3(0f, 0.08f, 0.01f), 0.045f, 7, 5);
            k.M.Color = C("#3a2a2a");
            k.M.Spike(ip + new Vector3(-0.025f, 0.115f, 0f), new Vector3(-0.3f, 1f, -0.2f), 0.012f, 0.05f, 4);
            k.M.Spike(ip + new Vector3(0.025f, 0.115f, 0f), new Vector3(0.3f, 1f, -0.2f), 0.012f, 0.05f, 4);
            k.M.Color = Paint.Shade(red, 0.8f);
            k.M.Blade(ip + new Vector3(-0.02f, 0.02f, -0.03f), ip + new Vector3(-0.11f, 0.08f, -0.06f), 0.06f);
            k.M.Blade(ip + new Vector3(0.02f, 0.02f, -0.03f), ip + new Vector3(0.1f, 0.09f, -0.05f), 0.06f);
            Glint(k, true); k.M.Color = C("#ffe080");
            k.M.Sphere(ip + new Vector3(-0.016f, 0.09f, 0.045f), 0.009f, 4, 2);
            k.M.Sphere(ip + new Vector3(0.016f, 0.09f, 0.045f), 0.009f, 4, 2);
            Glint(k, false);
            k.M.Color = C("#2a2a3a");
            k.M.Box(ip + new Vector3(0.0f, -0.02f, 0.05f), new Vector3(0.03f, 0.035f, 0.03f));
            var m = k.Model;
            m.HoldL = UnitHold.Book; m.HoldR = UnitHold.Carry; m.Strike = UnitStrike.Claw; m.Ranged = UnitRanged.Point;
            return Done(k, key);
        }
    }
}
