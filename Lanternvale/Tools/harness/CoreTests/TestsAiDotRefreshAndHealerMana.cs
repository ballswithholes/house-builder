// Regression tests of two companion/enemy AI fixes: a DoT/HoT is not recast while it still has ticks to deliver at its
// bearer's next turn start (refreshing it a turn early threw those ticks away), and a healer keeps the same 70% mana
// floor for mana-cost debuffs (Shadow Word: Pain) as for nukes. Non-periodic debuffs keep their one-turn refresh window
// (TestsCoreReviewRound.StackedSunderIsRefreshedBeforeItFallsOff_AndSharedBetweenWarriors).
using System;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsAiDotRefreshAndHealerMana
    {
        static void OnlyKnows(Unit u, params string[] ids)
        {
            foreach (var id in u.Abilities.Keys.ToList())
                if (Array.IndexOf(ids, id) < 0) u.Abilities.Remove(id);
        }

        static bool Casts(AIStep step, string id, Unit target) =>
            step.Kind == AIStepKind.UseAbility && step.AbilityId == id && step.Target == target;

        [Test]
        public static void DotIsNotRecastWhileItStillHasTicksLeft()
        {
            var corruption = Db.Aura("warlock_corruption");
            Assert(corruption != null && corruption.tickInterval > 0f && corruption.states.Length == 0 && corruption.mods.Count == 0, "Corruption is a pure DoT");
            var b = NewBattle(73, new Inventory());
            var wl = Hero(ClassId.Warlock, 20, talents: false, name: "Lock").At(20f, 20f);
            var foe = Mob("cr_bandit_chief", 20).At(28f, 20f).Tough();
            wl.FaceTowards(foe.Position);
            b.AddUnit(wl); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, wl);
            Assert(b.ActiveUnit == wl, "the warlock's turn");
            OnlyKnows(wl, "warlock_corruption");
            wl.AutoPlay = true;
            wl.AttackTarget = foe;
            Assert(b.CanUse(wl, "warlock_corruption", foe).Ok, "Corruption usable: " + b.CanUse(wl, "warlock_corruption", foe).Reason);

            var step = AI.NextStep(b, wl);
            Assert(Casts(step, "warlock_corruption", foe), "Corruption goes up when missing: " + step);

            b.ApplyAura(wl, foe, corruption, new AuraApplyInfo { Source = Db.Ability("warlock_corruption"), Duration = 18f });
            var dot = foe.FindAura(corruption.id, wl);
            Assert(dot != null, "Corruption on the foe");
            foreach (var left in new[] { 12f, RulesConstants.TurnSeconds, 3f })
            {
                dot.Remaining = left;
                wl.AIMemory.Reset();
                step = AI.NextStep(b, wl);
                Assert(!Casts(step, "warlock_corruption", foe), $"Corruption with {left}s (ticks still to come) is not recast: " + step);
            }

            // the bearer's next turn start still delivers the last two ticks, then the DoT is gone and goes back up
            dot.Remaining = RulesConstants.TurnSeconds;
            dot.TickAccum = 0f;
            int before = b.Events.Count(e => e.Type == CombatEventType.Damage && e.Periodic && e.Target == foe && e.Source == wl);
            b.ElapseAuras(foe, RulesConstants.TurnSeconds);
            int ticks = b.Events.Count(e => e.Type == CombatEventType.Damage && e.Periodic && e.Target == foe && e.Source == wl) - before;
            Assert(ticks == 2, "the last 6s still tick twice: " + ticks);
            Assert(foe.FindAura(corruption.id, wl) == null, "then Corruption expires");
            wl.AIMemory.Reset();
            step = AI.NextStep(b, wl);
            Assert(Casts(step, "warlock_corruption", foe), "Corruption is recast once it ran out: " + step);
        }

        [Test]
        public static void HotIsNotRecastWhileItStillHasTicksLeft()
        {
            var renew = Db.Aura("priest_renew");
            Assert(renew != null && renew.tickInterval > 0f && renew.states.Length == 0 && renew.mods.Count == 0, "Renew is a pure HoT");
            var b = NewBattle(74, new Inventory());
            var pr = Hero(ClassId.Priest, 20, talents: false, name: "Healer").At(20f, 20f);
            var tank = Hero(ClassId.Warrior, 20, name: "Tank").At(22f, 20f);
            var foe = Mob("cr_bandit_chief", 20).At(30f, 20f).Tough();
            b.AddUnit(pr); b.AddUnit(tank); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, pr);
            Assert(b.ActiveUnit == pr, "the priest's turn");
            OnlyKnows(pr, "priest_renew");
            pr.AutoPlay = true; pr.RoleOverride = UnitRole.Healer;
            tank.Health = tank.MaxHealth * 0.5f;
            Assert(b.CanUse(pr, "priest_renew", tank).Ok, "Renew usable: " + b.CanUse(pr, "priest_renew", tank).Reason);

            var step = AI.NextStep(b, pr);
            Assert(Casts(step, "priest_renew", tank), "Renew on the hurt tank: " + step);

            b.ApplyAura(pr, tank, renew, new AuraApplyInfo { Source = Db.Ability("priest_renew"), Duration = 15f });
            var hot = tank.FindAura(renew.id);
            Assert(hot != null, "Renew on the tank");
            hot.Remaining = 3f;   // one tick left at the tank's next turn start
            hot.TickAccum = 0f;
            pr.AIMemory.Reset();
            step = AI.NextStep(b, pr);
            Assert(!Casts(step, "priest_renew", tank), "Renew with its last tick to come is not recast: " + step);

            b.RemoveAura(hot, AuraRemoveReason.Expired);
            pr.AIMemory.Reset();
            step = AI.NextStep(b, pr);
            Assert(Casts(step, "priest_renew", tank), "Renew goes back up once it ran out: " + step);
        }

        [Test]
        public static void HealerKeepsItsManaFloorForShadowWordPain()
        {
            var b = NewBattle(75, new Inventory());
            var pr = Hero(ClassId.Priest, 20, talents: false, name: "Healer").At(20f, 20f);
            var foe = Mob("cr_bandit_chief", 20).At(28f, 20f).Tough();
            pr.FaceTowards(foe.Position);
            b.AddUnit(pr); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, pr);
            Assert(b.ActiveUnit == pr, "the priest's turn");
            OnlyKnows(pr, "priest_shadow_word_pain");
            pr.AutoPlay = true; pr.RoleOverride = UnitRole.Healer;
            pr.AttackTarget = foe;
            Assert(AI.HintOf(Db.Ability("priest_shadow_word_pain")) == "Debuff", "SW:Pain is a debuff");

            pr.Mana = pr.MaxMana * 0.9f;
            Assert(b.CanUse(pr, "priest_shadow_word_pain", foe).Ok, "SW:Pain usable: " + b.CanUse(pr, "priest_shadow_word_pain", foe).Reason);
            var step = AI.NextStep(b, pr);
            Assert(Casts(step, "priest_shadow_word_pain", foe), "with mana to spare the healer puts SW:Pain up: " + step);

            pr.Mana = pr.MaxMana * 0.5f;   // above the 25% low-mana mark, under the healer's 70% floor for offence
            pr.AIMemory.Reset();
            step = AI.NextStep(b, pr);
            Assert(!Casts(step, "priest_shadow_word_pain", foe), "at half mana the healer saves it for heals: " + step);
        }
    }
}
