// Regression tests of the final core fixes: stealth openers that need the rogue behind the target (Garrote, Ambush)
// work from exploration (the enemies keep their idle facing), pets learn the abilities their level unlocks (also after
// a save/load), and unused self-resurrection offers (Soulstone, Reincarnation) lapse instead of carrying over.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsFinalCoreFixes
    {
        // ------------------------------------------------------------------------------------------ stealth openers

        static GameSession StealthedRogueAtBoars(out EncounterDef enc, out EncounterEnemyPreview boar)
        {
            var s = SessionTest.NewGame(ClassId.Rogue, 30, seed: 121);
            s.EnterMap("whisperwood", "from_village");
            enc = s.Map.FindEncounter("enc_boars_edge");
            var prev = s.PreviewEncounter("enc_boars_edge");
            Assert(enc != null && prev.Count == enc.enemies.Count && prev.Count > 0, "boar encounter and its preview");
            boar = prev[0];
            AssertNear(boar.Facing.Length, 1f, 0.01f, "the preview gives the idle facing as a unit vector");
            // the main hand must be a dagger for Ambush
            var mh = s.Main.Equipment.MainHand;
            if (mh == null || mh.Def.weaponType != WeaponType.Dagger)
            {
                ItemDef dagger = null;
                foreach (var d in Db.Items.Values)
                    if (d.kind == ItemKind.Weapon && d.weaponType == WeaponType.Dagger && d.requiredLevel <= 30 &&
                        d.equipEffects.Count == 0 && d.quality < Quality.Epic &&   // a plain dagger (no expansion procs)
                        (d.equip == EquipType.OneHand || d.equip == EquipType.MainHand) &&
                        (dagger == null || string.CompareOrdinal(d.id, dagger.id) < 0)) dagger = d;
                Assert(dagger != null, "some dagger exists");
                var inst = new ItemInstance(dagger);
                s.Inventory.Add(inst);
                Assert(s.Equip(s.Main, inst, EquipSlot.MainHand) == null, "dagger equipped");
            }
            Assert(s.UseAbility(s.Main, "rogue_stealth").Ok && s.Main.IsStealthed, "stealth");
            return s;
        }

        [Test]
        public static void StealthOpenersNeedingBehind_LandFromBehindTheIdleFacing()
        {
            foreach (var opener in new[] { "rogue_garrote", "rogue_ambush" })
            {
                var s = StealthedRogueAtBoars(out var enc, out var boar);
                Assert(s.Main.Knows(opener), "the level-30 rogue knows " + opener);
                s.Main.Position = boar.Position - boar.Facing * 1.4f;   // the UI walks the rogue up behind the boar
                var b = s.EngageEncounter(enc.id, s.Main, opener, 0);
                Assert(b != null && b.Started, $"{opener}: battle begun ({s.LastError})");
                Assert(s.LastError == "", $"{opener} accepted from behind: '{s.LastError}'");
                Assert(b.Events.Exists(e => e.Type == CombatEventType.AbilityUsed && e.AbilityId == opener && e.Source == s.Main), opener + " used as the opener");
                int surprised = 0;
                foreach (var u in b.Units) if (u.Team == Team.Enemy && u.Surprised) surprised++;
                Assert(surprised == enc.enemies.Count, $"{opener}: enemies surprised ({surprised}/{enc.enemies.Count})");
            }

            // beside the boar is not behind it: the requirement still applies, and the fight starts without the opener
            var s2 = StealthedRogueAtBoars(out var enc2, out var boar2);
            var side = new Vec2(-boar2.Facing.y, boar2.Facing.x);
            s2.Main.Position = boar2.Position + side * 1.4f;
            var b2 = s2.EngageEncounter(enc2.id, s2.Main, "rogue_garrote", 0);
            Assert(b2 != null && b2.Started, "battle begun anyway");
            Assert(s2.LastError == "You must be behind your target.", "Garrote from the side is refused: " + s2.LastError);
            Assert(!b2.Events.Exists(e => e.Type == CombatEventType.AbilityUsed && e.AbilityId == "rogue_garrote"), "no Garrote");
        }

        // ------------------------------------------------------------------------------------------ pet abilities

        [Test]
        public static void PetLearnsLevelAbilities_OnLevelUp_AndAfterLoad()
        {
            const string howl = "hunter_pet_furious_howl";
            var s = SessionTest.NewGame(ClassId.Hunter, 9, seed: 131);
            var r = s.UseAbility(s.Main, "hunter_call_pet");
            Assert(r.Ok && s.Main.Pet != null, "call pet: " + r.Reason);
            var wolf = s.Main.Pet;
            Assert(Db.Ability(howl).learnLevel == 10, "Furious Howl is learned at 10");
            Assert(wolf.Level == 9 && !wolf.Knows(howl), "a level-9 wolf has no Furious Howl yet");

            s.GivePartyXp(Progression.XpToNextLevel(Db, s.Main.Level) - s.Main.Xp);
            Assert(s.Main.Level == 10 && wolf.Level == 10, "hunter and wolf reached level 10");
            Assert(wolf.Knows(howl), "the wolf learned Furious Howl with its level");
            Assert(wolf.Knows("hunter_pet_bite") && wolf.Knows("hunter_pet_growl"), "kept its other abilities");

            string json = s.SaveGame();
            var s2 = new GameSession(Db, 132);
            Assert(s2.LoadGame(json, out var err), "load: " + err);
            Assert(s2.Main.Pet != null && s2.Main.Pet.Knows(howl), "Furious Howl survives save/load");
            Assert(s2.SaveGame() == json, "save -> load -> save identical");

            // a save written while pets did not learn on level-up: the loaded wolf gets what its level allows
            wolf.Abilities.Remove(howl);
            var s3 = new GameSession(Db, 133);
            Assert(s3.LoadGame(s.SaveGame(), out err), "legacy load: " + err);
            Assert(s3.Main.Pet != null && s3.Main.Pet.Knows(howl), "loading merges the level-allowed abilities");
        }

        // ------------------------------------------------------------------------------------------ self-resurrection

        [Test]
        public static void SoulstoneOffer_LapsesWhenHelpedUp_OrAfterVictory()
        {
            var ss = Db.Aura("warlock_soulstone_minor");

            // helped up before its slot: the unused offer lapses
            var b = NewBattle(12, new Inventory());
            var pal = Hero(ClassId.Paladin, 40, name: "Pal").At(20, 20);
            var war = Hero(ClassId.Warrior, 40, name: "War").At(18.9f, 20);
            var foe = Mob("cr_bandit_chief", 40).At(27, 20).Tough();
            pal.AutoPlay = false;
            b.AddUnit(pal); b.AddUnit(war); b.AddUnit(foe);
            SkipTo(b, war);
            Assert(b.ActiveUnit == war, "the warrior's turn");
            b.ApplyAura(war, pal, ss);
            b.DealDamage(foe, pal, pal.Health + 50f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(pal.Downed && pal.SelfRes != null && !pal.HasAura(ss.id), "soulstone consumed into an offer");
            var help = b.UseAbility(war, "help_up", pal);
            Assert(help.Ok && pal.IsAlive, "helped up: " + help.Reason);
            Assert(pal.SelfRes == null, "helped up: the unused Soulstone offer lapses");

            // downed with an offer when the fight is won: standing up at the end drops it
            b.ApplyAura(war, pal, ss);
            b.DealDamage(foe, pal, pal.Health + 50f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(pal.Downed && pal.SelfRes != null, "second offer");
            b.Finish(BattleOutcome.Victory);
            Assert(pal.IsAlive && pal.SelfRes == null, "victory stands the paladin up and drops the offer");

            // the next fight: downed with no soulstone anywhere -> no offer
            var b2 = NewBattle(13, new Inventory());
            var foe2 = Mob("cr_bandit_chief", 40).At(27, 20).Tough();
            b2.AddUnit(pal); b2.AddUnit(war); b2.AddUnit(foe2);
            b2.Begin();
            b2.DealDamage(foe2, pal, pal.Health + 50f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(pal.Downed && pal.SelfRes == null, "no Soulstone offer without a soulstone");
            int guard = 0;
            while (!b2.IsOver && b2.ActiveUnit != null && guard++ < 12)
            {
                Assert(b2.PendingSelfResurrection(pal) == null, "never offered at the paladin's slot");
                b2.EndTurn(b2.ActiveUnit);
            }
            Assert(pal.Downed, "still downed");
        }

        [Test]
        public static void ReincarnationOffer_LapsesWhenHelpedUp_AndNeedsAnAnkhNextFight()
        {
            var inv = new Inventory();
            inv.Add(Db.Item("shaman_ankh"), 1);
            var b = NewBattle(7, inv);
            var sham = Hero(ClassId.Shaman, 40, name: "Sham").At(10, 10);
            var war = Hero(ClassId.Warrior, 40, name: "War").At(11.1f, 10);
            var foe = Mob("cr_bandit_chief", 40).At(17, 10).Tough();
            sham.AutoPlay = false;
            b.AddUnit(sham); b.AddUnit(war); b.AddUnit(foe);
            SkipTo(b, war);
            Assert(b.ActiveUnit == war, "the warrior's turn");
            b.DealDamage(foe, sham, sham.Health + 10f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(sham.Downed && sham.SelfRes != null && sham.SelfRes.Source == "shaman_reincarnation", "reincarnation offered");
            var help = b.UseAbility(war, "help_up", sham);
            Assert(help.Ok && sham.IsAlive && sham.SelfRes == null, "helped up: the offer lapses (" + help.Reason + ")");
            b.Finish(BattleOutcome.Victory);
            Assert(inv.Count("shaman_ankh") == 1, "the Ankh was not used");
            inv.Remove("shaman_ankh", 1);

            var b2 = NewBattle(8, inv);
            var foe2 = Mob("cr_bandit_chief", 40).At(17, 10).Tough();
            b2.AddUnit(sham); b2.AddUnit(war); b2.AddUnit(foe2);
            b2.Begin();
            b2.DealDamage(foe2, sham, sham.Health + 10f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(sham.Downed && sham.SelfRes == null, "no Ankh: no Reincarnation offer");
            int guard = 0;
            while (!b2.IsOver && b2.ActiveUnit != null && guard++ < 12)
            {
                Assert(b2.PendingSelfResurrection(sham) == null, "never offered at the shaman's slot");
                b2.EndTurn(b2.ActiveUnit);
            }
            Assert(sham.Downed && inv.Count("shaman_ankh") == 0, "still downed, no Ankh conjured");
        }
    }
}
