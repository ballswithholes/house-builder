// Regression tests of a review round of core fixes: the AI gives its single-target party buffs (Fortitude, Arcane
// Intellect, Blessings) to the allies who can use them (in combat only while they cost at most a tenth of its mana: see
// AI.CombatBuffManaShare), slows that wear off within a turn still slow that turn, and companion approval only moves for
// companions who witness the scene.
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
    public static class TestsBuffsSlowsApproval
    {
        // ------------------------------------------------------------------------------------- AI party buffs

        static bool HasGroupFrom(Unit u, string group, Unit caster)
        {
            foreach (var a in u.Auras) if (a.Def.exclusiveGroup == group && a.Caster == caster) return true;
            return false;
        }

        /// <summary>Warrior tank, priest, mage and paladin played by the AI for six rounds against the training dummy (or a
        /// tough bandit chief that hits back).</summary>
        static (Unit war, Unit pri, Unit mag, Unit pal) BuffedParty(int level, int seed, string foeId = "cr_training_dummy")
        {
            var b = NewBattle(seed, new Inventory());
            var war = Hero(ClassId.Warrior, level, name: "Tank").At(20, 20);
            war.RoleOverride = UnitRole.Tank;
            var pri = Hero(ClassId.Priest, level, name: "Priest").At(14, 18);
            pri.RoleOverride = UnitRole.Healer;
            var mag = Hero(ClassId.Mage, level, name: "Mage").At(14, 22);
            var pal = Hero(ClassId.Paladin, level, name: "Paladin").At(17, 20);
            pal.RoleOverride = UnitRole.MeleeDps;
            var foe = Mob(foeId, level).At(27, 20).Tough();
            foreach (var u in new[] { war, pri, mag, pal, foe }) b.AddUnit(u);
            Run(b, 6);
            return (war, pri, mag, pal);
        }

        static bool OnOther(Unit caster, string auraId, params Unit[] others)
        {
            foreach (var o in others) if (o != caster && o.HasAura(auraId, caster)) return true;
            return false;
        }

        static bool AnyHas(string auraId, params Unit[] units)
        {
            foreach (var u in units) if (u.HasAura(auraId)) return true;
            return false;
        }

        [Test]
        public static void AiPartyBuffsReachTheAllies()
        {
            // level 10 on the dummy: rank-1 Fortitude (~6% of the priest's mana) still goes up in combat, on the tank too
            var (war0, pri0, mag0, pal0) = BuffedParty(10, 60);
            Assert(OnOther(pri0, "priest_power_word_fortitude", war0, mag0, pal0), "L10: the priest's Fortitude reaches another member");
            Assert(war0.HasAura("priest_power_word_fortitude", pri0), "L10: Fortitude on the tank");

            // level 12 on the dummy: Arcane Intellect and Blessing of Might are the cheap single-target grouped buffs;
            // Fortitude rank 2 (~29% of the priest's mana) waits for the end of the fight
            var (war, pri, mag, pal) = BuffedParty(12, 61);
            Assert(!AnyHas("priest_power_word_fortitude", war, pri, mag, pal), "L12: no Fortitude rank 2 cast in combat");
            Assert(OnOther(mag, "mage_arcane_intellect", pri, pal), "L12: the mage's Arcane Intellect reaches another mana user");
            Assert(!war.HasAura("mage_arcane_intellect"), "L12: no Arcane Intellect on the rage-using warrior");
            Assert(war.HasAura("paladin_blessing_of_might", pal) && pal.HasAura("paladin_blessing_of_might", pal),
                "L12: Blessing of Might on the tank and the paladin (attack power users)");
            Assert(!pri.HasAura("paladin_blessing_of_might") && !mag.HasAura("paladin_blessing_of_might"), "L12: no Blessing of Might on the casters");
            Assert(mag.HasAura("mage_dampen_magic", mag), "L12: the mage keeps Dampen Magic on itself");
            foreach (var u in new[] { war, pri, pal })
                Assert(!u.HasAura("mage_dampen_magic") && !u.HasAura("mage_amplify_magic"), $"L12: no Dampen/Amplify Magic on {u.Name}");

            // level 30: every member gets a blessing that suits it from the paladin
            var (war2, pri2, mag2, pal2) = BuffedParty(30, 62);
            foreach (var u in new[] { war2, pri2, mag2, pal2 })
                Assert(HasGroupFrom(u, "paladin_blessing", pal2), $"L30: {u.Name} has a blessing from the paladin");
            Assert(!war2.HasAura("paladin_blessing_of_salvation") && !war2.HasAura("paladin_greater_blessing_of_salvation"), "L30: no Salvation on the tank");
            Assert(!war2.HasAura("paladin_blessing_of_wisdom") && !war2.HasAura("paladin_greater_blessing_of_wisdom") && !war2.HasAura("mage_arcane_intellect"),
                "L30: no mana buffs on the warrior");
            foreach (var u in new[] { pri2, mag2 })
                Assert(!u.HasAura("paladin_blessing_of_might") && !u.HasAura("paladin_greater_blessing_of_might"), $"L30: no Might on {u.Name}");
            // Fortitude and Arcane Intellect cost ~23% of the pool at 30: not in combat
            Assert(!AnyHas("priest_power_word_fortitude", war2, pri2, mag2, pal2), "L30: no Fortitude cast in combat");
            Assert(!AnyHas("mage_arcane_intellect", war2, pri2, mag2, pal2), "L30: no Arcane Intellect cast in combat");

            // a real fight: the opening turns still put the cheap buffs on the party, not only on the casters
            var (war3, pri3, mag3, pal3) = BuffedParty(12, 63, "cr_bandit_chief");
            Assert(war3.HasAura("paladin_blessing_of_might", pal3), "L12 fight: the tank is blessed");
            Assert(OnOther(mag3, "mage_arcane_intellect", pri3, pal3), "L12 fight: Arcane Intellect reaches another mana user");
        }

        // ------------------------------------------------------------------------------- slows within a turn

        /// <summary>Applies the aura to a level-12 wolf on the hunter's turn, then returns the wolf's movement at its own turn.</summary>
        static (float left, float full) MoveAfter(string auraId, int seed)
        {
            var b = NewBattle(seed, new Inventory());
            var hunter = Hero(ClassId.Hunter, 12, name: "Hunter").At(10, 20);
            hunter.AutoPlay = false;
            var wolf = Mob("cr_wolf", 12).At(30, 20).Tough();
            b.AddUnit(hunter); b.AddUnit(wolf);
            b.Begin();
            SkipTo(b, hunter);
            Assert(b.ActiveUnit == hunter, "the hunter's turn");
            float full = wolf.Creature.moveSpeed;
            if (auraId != null)
            {
                var def = Db.Aura(auraId) ?? throw new Exception("no aura " + auraId);
                Assert(b.ApplyAura(hunter, wolf, def) != null && wolf.HasAura(auraId), auraId + " applied");
            }
            SkipTo(b, wolf);
            Assert(b.ActiveUnit == wolf, "the wolf's turn");
            Assert(Math.Abs(wolf.MoveLeft - wolf.MoveBudget) < 1e-3f, "nothing else limits the wolf's movement");
            return (wolf.MoveLeft, full);
        }

        [Test]
        public static void ExpiringSlowsStillSlowTheTurn()
        {
            var (free, full) = MoveAfter(null, 71);
            AssertNear(free, full, 1e-3f, "unslowed wolf");
            // Daze/snare −50% for the whole turn (6 s auras) or part of it (4 s Concussive Shot: two thirds)
            AssertNear(MoveAfter("warrior_piercing_howl", 72).left, full * 0.5f, 1e-2f, "Piercing Howl (6 s) halves the move");
            AssertNear(MoveAfter("cr_dazed", 73).left, full * 0.5f, 1e-2f, "Dazed (6 s) halves the move");
            AssertNear(MoveAfter("mage_blast_wave_daze", 74).left, full * 0.5f, 1e-2f, "Blast Wave daze (6 s) halves the move");
            AssertNear(MoveAfter("hunter_concussive_shot", 75).left, full * (0.5f * 4f / 6f + 2f / 6f), 1e-2f, "Concussive Shot (4 s) slows two thirds of the turn");
            AssertNear(MoveAfter("warlock_aftermath", 76).left, full * (0.5f * 5f / 6f + 1f / 6f), 1e-2f, "Aftermath (5 s)");
            AssertNear(MoveAfter("mage_frost_armor_chill", 77).left, full * (0.7f * 5f / 6f + 1f / 6f), 1e-2f, "Frost Armor chill (5 s, −30%)");
            // a slow outlasting the turn was already counted
            AssertNear(MoveAfter("warrior_hamstring", 78).left, full * 0.5f, 1e-2f, "Hamstring (15 s)");
        }

        // ---------------------------------------------------------------------------------------- approval

        [Test]
        public static void ApprovalOnlyForWitnesses()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 5, seed: 81);
            s.TakeEvents();
            IDialogueContext ctx = s;

            // not met yet: nothing changes, no toast (Puddlecap's intimidation outcome for Morwen)
            WorldRules.Execute(new OutcomeDef { type = OutcomeType.Approval, key = "morwen", amount = 3 }, ctx);
            Assert(s.GetApproval("morwen") == 0, "a companion not met yet is unaffected");
            Assert(SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.ApprovalChanged) == null, "no approval toast from a stranger");

            // in the party: reacts
            s.Recruit("seren");
            s.TakeEvents();
            WorldRules.Execute(new OutcomeDef { type = OutcomeType.Approval, key = "seren", amount = -3 }, ctx);
            Assert(s.GetApproval("seren") == -3, "an active companion reacts");
            var ev = SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.ApprovalChanged, "seren");
            Assert(ev != null && ev.Amount == -3 && ev.Text.Contains("disapproves"), "approval toast for a witness");

            // at camp: unaffected
            s.Recruit("kael"); s.Recruit("lys"); s.Recruit("pip");
            Assert(s.CompanionStatusOf("pip") == CompanionStatus.Camp, "Pip waits at camp (party full)");
            s.TakeEvents();
            s.ChangeApproval("pip", 5);
            Assert(s.GetApproval("pip") == 0 && SessionTest.FindEvent(s.TakeEvents(), SessionEventKind.ApprovalChanged) == null,
                "a companion at camp did not see it");

            // the companion the party is talking to (its recruitment talk) reacts even before joining
            Assert(s.StartDialogue("dlg_recruit_aldric", "aldric"), "talk to Aldric");
            s.ChangeApproval("aldric", 8);
            Assert(s.GetApproval("aldric") == 8, "the companion spoken to reacts");
            s.ChangeApproval("morwen", 2);
            Assert(s.GetApproval("morwen") == 0, "others not present still do not");
            s.Dialogue.End();
            s.ChangeApproval("aldric", 5);
            Assert(s.GetApproval("aldric") == 8, "after the talk, a companion not in the party is unaffected");

            // gated content cannot be unlocked by scenes the companion missed
            Assert(!WorldRules.Check(new ConditionDef { type = ConditionType.Companion, key = "pip", amount = 5 }, ctx), "Companion condition unaffected");
        }
    }
}
