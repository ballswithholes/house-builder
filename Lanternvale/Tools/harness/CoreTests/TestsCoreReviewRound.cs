// Regression tests of a review round of core fixes: a pending Soulstone/Reincarnation offer keeps a wiped party in the
// fight until its slot, cast and channel time holds the swing timers, wandless AI casters keep casting at low mana,
// stacked/expiring debuffs are refreshed (one shared Sunder Armor stack), and the totems, traps and temporary guardians
// placed out of combat survive a save/load. (Normal creature health: TestsRulesCore.CreatureCurves.)
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsCoreReviewRound
    {
        static void KillOutright(Battle b, Unit src, Unit u) =>
            b.DealDamage(src, u, u.Health + 50f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });

        /// <summary>A lone level-40 hero against a tough bandit chief, at the bandit's turn.</summary>
        static (Battle b, Unit hero, Unit foe) Solo(ClassId c, int seed, Inventory inv = null)
        {
            var b = NewBattle(seed, inv ?? new Inventory());
            var hero = Hero(c, 40, name: "Solo").At(20, 20);
            var foe = Mob("cr_bandit_chief", 40).At(27, 20).Tough();
            hero.AutoPlay = false;
            b.AddUnit(hero); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, foe);
            Assert(b.ActiveUnit == foe, "the bandit's turn");
            return (b, hero, foe);
        }

        static void EndTurnsUntil(Battle b, Unit u)
        {
            int guard = 0;
            while (!b.IsOver && b.ActiveUnit != null && b.ActiveUnit != u && guard++ < 20) b.EndTurn(b.ActiveUnit);
        }

        // ------------------------------------------------------------------------------- self-resurrection on a wipe

        [Test]
        public static void SoulstoneSavesAPartyWipe()
        {
            var ss = Db.Aura("warlock_soulstone_minor");

            // accepted: the last member standing falls, rises at its own slot and the fight goes on
            var (b, wl, foe) = Solo(ClassId.Warlock, 21);
            b.ApplyAura(wl, wl, ss);
            KillOutright(b, foe, wl);
            Assert(wl.Downed && wl.SelfRes != null && !wl.HasAura(ss.id), "soulstone consumed into an offer");
            Assert(!b.IsOver, "a pending Soulstone is no defeat (outcome " + b.Outcome + ")");
            EndTurnsUntil(b, wl);
            Assert(!b.IsOver && b.PendingSelfResurrection(wl) != null, "the offer comes at the warlock's slot");
            Assert(b.AcceptSelfResurrection(wl).Ok, "accept");
            Assert(wl.IsAlive && b.ActiveUnit == wl && !b.IsOver, "risen, taking the turn, the battle goes on");

            // declined: nobody is left standing, so the battle is lost there and then
            var (b2, wl2, foe2) = Solo(ClassId.Warlock, 22);
            b2.ApplyAura(wl2, wl2, ss);
            KillOutright(b2, foe2, wl2);
            Assert(!b2.IsOver, "offer pending");
            EndTurnsUntil(b2, wl2);
            Assert(b2.PendingSelfResurrection(wl2) != null, "offer at the slot");
            Assert(b2.DeclineSelfResurrection(wl2).Ok, "decline");
            Assert(b2.IsOver && b2.Outcome == BattleOutcome.Defeat && wl2.SelfRes == null, "declining the last offer loses the battle (" + b2.Outcome + ")");

            // no offer: a wipe stays an immediate defeat
            var (b3, wl3, foe3) = Solo(ClassId.Warlock, 23);
            KillOutright(b3, foe3, wl3);
            Assert(b3.IsOver && b3.Outcome == BattleOutcome.Defeat, "a wipe without a self-resurrection is a defeat");
        }

        [Test]
        public static void ReincarnationSavesAWipe_AutoPlayedShamanRises()
        {
            var inv = new Inventory();
            inv.Add(Db.Item("shaman_ankh"), 1);
            var (b, sham, foe) = Solo(ClassId.Shaman, 24, inv);
            sham.AutoPlay = true;
            KillOutright(b, foe, sham);
            Assert(sham.Downed && sham.SelfRes != null && !b.IsOver, "reincarnation offered, the battle goes on");
            b.EndTurn(foe);
            Assert(sham.IsAlive && b.ActiveUnit == sham && !b.IsOver, "the auto-played shaman rose at its slot and acts");
            Assert(inv.Count("shaman_ankh") == 0, "the Ankh was used");
        }

        // ------------------------------------------------------------------------------------------- swing timers

        static int WhiteSwings(Battle b, Unit u, int from)
        {
            int n = 0;
            for (int i = from; i < b.Events.Count; i++)
                if (b.Events[i].AutoAttack && b.Events[i].Source == u && b.Events[i].Type != CombatEventType.AutoAttackToggled) n++;
            return n;
        }

        [Test]
        public static void CastingHoldsTheSwingTimer()
        {
            var b = NewBattle(31, new Inventory());
            var sham = Hero(ClassId.Shaman, 40, talents: false, name: "Sham").At(20, 20);
            var foe = Mob("cr_bandit_chief", 40).At(21.2f, 20).Tough();
            sham.AutoPlay = false;
            b.AddUnit(sham); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, sham);
            Assert(b.ActiveUnit == sham && b.InMeleeReach(sham, foe), "the shaman's turn, in melee reach");
            AssertNear(sham.SwingTimeThisTurn, 6f, 1e-3f, "a free turn has 6 s of swing time");
            Assert(b.UseAbility(sham, "attack", foe).Ok && sham.AutoAttacking, "auto attack on");

            // two 3 s Lightning Bolts fill the turn: no white swings on top of them
            var bolt = Db.Ability("shaman_lightning_bolt");
            float cast = AbilityRules.CastTime(sham, bolt, AbilityMods.For(sham, bolt));
            AssertNear(cast, 3f, 1e-3f, "untalented top-rank bolt");
            sham.Mana = sham.MaxMana;
            var r = b.UseAbility(sham, bolt.id, foe);
            Assert(r.Ok && sham.Pending == null, "first bolt: " + r.Reason);
            AssertNear(sham.SwingTimeThisTurn, 3f, 1e-3f, "the cast held the swing timer");
            sham.Mana = sham.MaxMana;
            r = b.UseAbility(sham, bolt.id, foe);
            Assert(r.Ok && sham.Pending == null, "second bolt: " + r.Reason);
            AssertNear(sham.SwingTimeThisTurn, 0f, 1e-3f, "a turn of casting leaves no swing time");
            int mark = b.Events.Count;
            b.EndTurn(sham);
            Assert(WhiteSwings(b, sham, mark) == 0, "no white swings after a full turn of casting, got " + WhiteSwings(b, sham, mark));

            // a turn without casting still swings
            SkipTo(b, sham);
            AssertNear(sham.SwingTimeThisTurn, 6f, 1e-3f, "next turn: 6 s of swing time again");
            mark = b.Events.Count;
            b.EndTurn(sham);
            Assert(WhiteSwings(b, sham, mark) >= 1, "a turn of only auto attacks swings");

            // a cast that spills over: the rest of it at the next turn start holds the timer too
            SkipTo(b, sham);
            Assert(b.Wait(sham, 4.5f).Ok, "wait");
            sham.Mana = sham.MaxMana;
            r = b.UseAbility(sham, bolt.id, foe);
            Assert(r.Ok && sham.Pending != null && b.ActiveUnit != sham, "the bolt did not fit and ends the turn: " + r.Reason);
            SkipTo(b, sham);
            Assert(sham.Pending == null, "the pending bolt resolved at turn start");
            AssertNear(sham.SwingTimeThisTurn, 4.5f, 1e-3f, "1.5 s of the turn went to finishing the bolt");

            // a channel that fits in the turn holds the timer for its duration
            var b2 = NewBattle(32, new Inventory());
            var mage = Hero(ClassId.Mage, 40, talents: false, name: "Mage").At(20, 20);
            var foe2 = Mob("cr_bandit_chief", 40).At(26, 20).Tough();
            mage.AutoPlay = false;
            b2.AddUnit(mage); b2.AddUnit(foe2);
            b2.Begin();
            SkipTo(b2, mage);
            var am = Db.Ability("mage_arcane_missiles");
            float channel = AbilityRules.CastTime(mage, am, AbilityMods.For(mage, am));
            Assert(channel > 0f && channel < 6f, "arcane missiles channel " + channel);
            r = b2.UseAbility(mage, am.id, foe2);
            Assert(r.Ok && mage.Pending == null, "channel: " + r.Reason);
            AssertNear(mage.SwingTimeThisTurn, 6f - channel, 1e-3f, "the channel held the swing timer");
        }

        // ------------------------------------------------------------------------------------- companion AI choices

        static void OnlyKnows(Unit u, params string[] ids)
        {
            foreach (var id in u.Abilities.Keys.ToList())
                if (Array.IndexOf(ids, id) < 0) u.Abilities.Remove(id);
        }

        static ItemDef AnyWand(int maxLevel) =>
            Db.Items.Values.Where(d => d.weaponType == WeaponType.Wand && d.requiredLevel <= maxLevel).OrderBy(d => d.id, StringComparer.Ordinal).FirstOrDefault();

        [Test]
        public static void WandlessCasterKeepsCastingAtLowMana()
        {
            foreach (bool wand in new[] { false, true })
            {
                var b = NewBattle(41, new Inventory());
                var mage = Hero(ClassId.Mage, 20, name: "Mage").At(10, 20);
                var foe = Mob("cr_bandit_chief", 20).At(20, 20).Tough();
                OnlyKnows(mage, "mage_fireball", "shoot", "attack");
                if (mage.Equipment.Ranged != null) EquipmentRules.Unequip(mage, EquipSlot.Ranged);
                if (wand)
                {
                    var def = AnyWand(20);
                    Assert(def != null, "a wand in the database");
                    EquipmentRules.Equip(mage, new ItemInstance(def, 1), EquipSlot.Ranged);
                }
                Assert(mage.Equipment.HasWand == wand, "wand equipped: " + wand);
                b.AddUnit(mage); b.AddUnit(foe);
                b.Begin();
                SkipTo(b, mage);
                Assert(b.ActiveUnit == mage && mage.Role == UnitRole.RangedDps, "the mage's turn (role " + mage.Role + ")");
                mage.Mana = mage.MaxMana * 0.2f;   // between the 15% mana-potion step and the 25% saving threshold
                Assert(b.CanUse(mage, "mage_fireball", foe).Ok, "fireball is affordable");
                var step = AI.NextStep(b, mage);
                if (wand) Assert(step.Kind == AIStepKind.UseAbility && step.AbilityId == "shoot", "with a wand it saves mana and shoots: " + step);
                else Assert(step.Kind == AIStepKind.UseAbility && step.AbilityId == "mage_fireball", "without a wand it keeps casting instead of idling: " + step);
            }
        }

        [Test]
        public static void StackedSunderIsRefreshedBeforeItFallsOff_AndSharedBetweenWarriors()
        {
            var sunder = Db.Aura("warrior_sunder_armor");
            Assert(sunder != null && sunder.maxStacks == 5, "Sunder Armor stacks to 5");
            var b = NewBattle(51, new Inventory());
            var w1 = Hero(ClassId.Warrior, 30, name: "W1").At(20, 20);
            var w2 = Hero(ClassId.Warrior, 30, name: "W2").At(20, 21.6f);
            var foe = Mob("cr_bandit_chief", 30).At(21.3f, 20.8f).Tough();
            b.AddUnit(w1); b.AddUnit(w2); b.AddUnit(foe);
            b.Begin();
            foreach (var w in new[] { w1, w2 }) OnlyKnows(w, "warrior_sunder_armor", "attack");
            for (int i = 0; i < 5; i++) b.ApplyAura(w1, foe, sunder);
            var stack = foe.FindAura(sunder.id);
            Assert(stack != null && stack.Stacks == 5 && stack.Caster == w1, "five stacks from W1");

            SkipTo(b, w1);
            Assert(b.ActiveUnit == w1, "W1's turn");
            w1.Rage = 100f;
            Assert(b.CanUse(w1, "warrior_sunder_armor", foe).Ok, "sunder usable: " + b.CanUse(w1, "warrior_sunder_armor", foe).Reason);
            stack.Remaining = 25f;
            var step = AI.NextStep(b, w1);
            Assert(step.AbilityId != "warrior_sunder_armor", "a full, fresh stack is left alone: " + step);
            stack.Remaining = RulesConstants.TurnSeconds;   // would tick off at the bearer's next turn start
            step = AI.NextStep(b, w1);
            Assert(step.AbilityId == "warrior_sunder_armor" && step.Target == foe, "a full stack about to fall off is refreshed: " + step);

            // the stack is one shared instance: the other warrior does not re-apply W1's fresh 5/5 stack as "not mine"
            stack.Remaining = 25f;
            SkipTo(b, w2);
            Assert(b.ActiveUnit == w2, "W2's turn");
            w2.Rage = 100f;
            Assert(b.CanUse(w2, "warrior_sunder_armor", foe).Ok, "sunder usable by W2");
            stack = foe.FindAura(sunder.id);
            Assert(stack != null && stack.Stacks == 5 && stack.Remaining > RulesConstants.TurnSeconds, "still a fresh full stack: " + stack);
            step = AI.NextStep(b, w2);
            Assert(step.AbilityId != "warrior_sunder_armor", "W2 leaves W1's full stack alone: " + step);
        }

        // ------------------------------------------------------------------------------------- field summons in saves

        [Test]
        public static void FieldTotemsTrapsAndGuardians_SurviveSaveLoad()
        {
            var s = SessionTest.NewGame(ClassId.Shaman, 40, seed: 61, name: "Yuna");
            s.Recruit("rook");
            s.Recruit("seren");
            var sham = s.Main;
            var rook = s.FindMember("rook");
            var seren = s.FindMember("seren");
            Assert(rook != null && rook.ClassId == ClassId.Hunter && seren != null && seren.ClassId == ClassId.Priest, "a hunter and a priest in the party");
            foreach (var id in new[] { "shaman_strength_of_earth_totem", "shaman_mana_spring_totem", "shaman_flametongue_totem", "shaman_windwall_totem" })
            {
                sham.Mana = sham.MaxMana;
                var r = s.UseAbility(sham, id);
                Assert(r.Ok, id + ": " + r.Reason);
            }
            rook.Mana = rook.MaxMana;
            var trap = s.UseAbility(rook, "hunter_freezing_trap", null, rook.Position + rook.Facing * 2f);   // laid before a pull
            Assert(trap.Ok, "freezing trap: " + trap.Reason);
            seren.Abilities["priest_lightwell"] = 1;   // Holy talent
            seren.Mana = seren.MaxMana;
            var well = s.UseAbility(seren, "priest_lightwell");
            Assert(well.Ok, "lightwell: " + well.Reason);
            s.Tick(3f);

            var before = s.OwnedSummons();
            Assert(before.Count == 6, "4 totems, a trap and a Lightwell placed: " + string.Join(", ", before.Select(x => x.Name + "/" + x.TotemElement + "/" + x.Owner?.Name)));
            Assert(rook.Totems.ContainsKey("Trap") && seren.Summons.Count == 1, "trap in the hunter's trap slot, the Lightwell a summon");
            var wellUnit = seren.Summons[0];
            Assert(wellUnit.GetVar("charges") == 5f, "Lightwell charges");
            float sofLife = sham.Totems["Earth"].Lifetime;
            Assert(sofLife > 0f && sofLife < 120f, "the earth totem's lifetime runs in the field: " + sofLife);
            float trapCd = rook.CooldownLeft(Db.Ability("hunter_freezing_trap"));
            Assert(trapCd > 0f, "the trap is on cooldown");
            string json1 = s.SaveGame();

            var s2 = new GameSession(Db, 999);
            Assert(s2.LoadGame(json1, out var err), "load: " + err);
            var after = s2.OwnedSummons();
            Assert(after.Count == before.Count, "every summon restored: " + after.Count + " of " + before.Count);
            var sham2 = s2.Main;
            var rook2 = s2.FindMember("rook");
            var seren2 = s2.FindMember("seren");
            foreach (var el in sham.Totems.Keys)
            {
                Assert(sham2.Totems.TryGetValue(el, out var t2) && t2 != null, "totem slot " + el);
                var t1 = sham.Totems[el];
                Assert(t2.Creature.id == t1.Creature.id && t2.Kind == UnitKind.Totem && t2.Owner == sham2 && t2.Team == Team.Player, el + " totem restored");
                AssertNear(t2.Lifetime, t1.Lifetime, 1e-3f, el + " lifetime left");
                AssertNear((t2.Position - t1.Position).Length, 0f, 1e-3f, el + " position");
                AssertNear(t2.Health, t1.Health, 1e-3f, el + " health");
            }
            Assert(rook2.Totems.TryGetValue("Trap", out var trap2) && trap2.Creature.id == "hunter_trap_freezing" && trap2.TotemElement == "Trap", "the freezing trap is back in its slot");
            Assert(seren2.Summons.Count == 1 && seren2.Summons[0].Creature.id == "priest_lightwell" && seren2.Summons[0].Kind == UnitKind.Summon, "the Lightwell is back");
            Assert(seren2.Summons[0].GetVar("charges") == 5f && seren2.Summons[0].GetVar("rank") == wellUnit.GetVar("rank"), "Lightwell charges and rank");
            AssertNear(rook2.CooldownLeft(Db.Ability("hunter_freezing_trap")), trapCd, 1e-3f, "cooldowns as before");
            foreach (var x in after) Assert(s2.Field != null && s2.Field.Units.Contains(x), x.Name + " is in the exploration context");

            string json2 = s2.SaveGame();
            Assert(json1 == json2, "save -> load -> save identical with summons");

            // they keep ticking after the load and expire on time
            s2.Tick(sofLife + 1f);
            Assert(!sham2.Totems.ContainsKey("Earth") || !sham2.Totems["Earth"].IsAlive, "the earth totem expired after its lifetime");

            // older saves without summons load without them
            var noSummons = new GameSession(Db, 998);
            var legacy = json1.Replace("\"summons\"", "\"summonsOld\"");
            Assert(noSummons.LoadGame(legacy, out err), "legacy load: " + err);
            Assert(noSummons.OwnedSummons().Count == 0, "no summons from a save without them");
        }
    }
}
