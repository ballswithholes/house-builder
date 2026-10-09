// Engine/AI fixes found by the full-game playthrough (TestsSessionFullPlaythrough): telegraphed casts always go pending,
// companions stop them with stuns/interrupts, drink potions when about to fall, keep Lay on Hands for characters in real
// danger, and do not refresh a fresh Immolate when another nuke is ready.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsRulesPlaythroughFixes
    {
        [Test]
        public static void Telegraph_CastAlwaysPending_And_CompanionsStunIt()
        {
            var b = NewBattle(31, new Inventory());
            var pal = Hero(ClassId.Paladin, 12, talents: false).At(20f, 20f);
            var warden = Mob("cr_hollow_warden", 14).At(24f, 20f);
            pal.FaceTowards(warden.Position);
            b.AddUnit(pal); b.AddUnit(warden);
            b.Begin();
            SkipTo(b, warden);
            var req = Db.Ability("cr_lantern_requiem");
            Assert(Battle.IsTelegraphed(req), "Lantern Requiem is tagged Telegraph");
            warden.TimeLeft = 6f; warden.TimeDebt = 0f;
            Assert(AbilityRules.CastTime(warden, req, AbilityMods.For(warden, req)) <= warden.TimeLeft + 1e-3f, "the cast would fit in the turn");
            var r = b.UseAbility(warden, req.id);
            Assert(r.Ok, "requiem: " + r.Reason);
            Assert(warden.Pending != null && warden.Pending.Ability == req, "telegraphed: pending even though it fits");
            AssertNear(warden.Pending.RemainingTime, 0f, 1e-3f, "resolves at the start of the Warden's next turn");
            Assert(warden.Pending.TotalTime > 5.9f, "cast bar total");
            Assert(!b.Events.Any(e => e.Type == CombatEventType.Damage && e.AbilityId == req.id), "no damage yet");

            // the paladin's turn: the AI stops it with Hammer of Justice (bosses are immune to many controls, not stuns)
            SkipTo(b, pal);
            Assert(warden.Pending != null, "still casting on the paladin's turn");
            pal.Cooldowns.Clear(); pal.Mana = pal.MaxMana; pal.AutoPlay = true;
            Assert(AI.CancelsCast(b, Db.Ability("paladin_hammer_of_justice"), warden), "a stun cancels the cast");
            Assert(!AI.CancelsCast(b, Db.Ability("paladin_seal_of_righteousness"), warden), "a seal does not");
            var step = AI.NextStep(b, pal);
            Assert(step.Kind == AIStepKind.UseAbility && step.AbilityId == "paladin_hammer_of_justice" && step.Target == warden, "AI: stop the requiem: " + step);
            Assert(AI.Execute(b, step).Ok, "hammer of justice");
            Assert(warden.Pending == null && b.Events.Any(e => e.Type == CombatEventType.CastInterrupted && e.AbilityId == req.id), "the requiem is cancelled");
        }

        [Test]
        public static void Telegraph_OnlyTaggedCasts()
        {
            Assert(!Battle.IsTelegraphed(Db.Ability("cr_gore")), "instant abilities are not telegraphed");
            Assert(!Battle.IsTelegraphed(Db.Ability("mage_fireball")), "untagged casts are not");
            Assert(!Battle.IsTelegraphed(null), "null");
        }

        [Test]
        public static void Companion_DrinksAPotionWhenAboutToFall()
        {
            var inv = new Inventory();
            inv.Add(Db.Item("potion_minor_healing"), 2);
            inv.Add(Db.Item("potion_minor_mana"), 1);
            var b = NewBattle(32, inv);
            var w = Hero(ClassId.Warrior, 10, talents: false).At(20f, 20f);
            var foe = Mob("cr_bandit_cutthroat", 10).At(22f, 20f).Tough();
            b.AddUnit(w); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, w);
            w.Health = w.MaxHealth * 0.5f;
            Assert(AI.PotionStep(b, w) == null, "no potion at half health");
            w.Health = w.MaxHealth * 0.2f;
            var step = AI.NextStep(b, w);
            Assert(step.Kind == AIStepKind.UseItem && step.Item.Id == "potion_minor_healing" && step.Target == w, "drinks a healing potion: " + step);
            float hp = w.Health;
            Assert(AI.Execute(b, step).Ok, "potion used");
            Assert(w.Health > hp && inv.Count("potion_minor_healing") == 1, "healed, one potion used");
            Assert(AI.PotionStep(b, w) == null || AI.PotionStep(b, w).Item.Id != "potion_minor_healing", "potion cooldown respected");

            // mana: casters drink when dry (potions share a cooldown group, so a fresh caster)
            var mage = Hero(ClassId.Mage, 10, talents: false).At(19f, 21f);
            b.AddUnit(mage);
            SkipTo(b, mage);
            mage.Mana = mage.MaxMana * 0.1f;
            var ms = AI.PotionStep(b, mage);
            Assert(ms != null && ms.Item.Id == "potion_minor_mana", "mana potion when dry");
            Assert(AI.PotionStep(NewBattle(1, null), mage) == null, "no bags, no potions");
        }

        [Test]
        public static void Companion_LayOnHandsOnlyForACharacterInDanger()
        {
            var b = NewBattle(33, new Inventory());
            var pal = Hero(ClassId.Paladin, 12, talents: false).At(20f, 20f);
            var lock_ = Hero(ClassId.Warlock, 12, talents: false).At(21f, 22f);
            var foe = Mob("cr_bandit_cutthroat", 12).At(30f, 20f).Tough();
            b.AddUnit(pal); b.AddUnit(lock_); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, lock_);
            Assert(b.UseAbility(lock_, "warlock_summon_imp").Ok || lock_.Pending != null, "imp summoned");
            SkipTo(b, pal);
            if (lock_.Pending != null) { SkipTo(b, lock_); SkipTo(b, pal); }
            var imp = lock_.Pet;
            Assert(imp != null && imp.IsAlive, "the imp is out");
            foreach (var id in pal.Abilities.Keys.ToList()) if (id != "paladin_lay_on_hands") pal.Abilities.Remove(id);
            pal.AutoPlay = true; pal.RoleOverride = UnitRole.Healer;
            imp.Health = imp.MaxHealth * 0.1f;
            lock_.Health = lock_.MaxHealth * 0.4f;
            var step = AI.NextStep(b, pal);
            Assert(!(step.Kind == AIStepKind.UseAbility && step.AbilityId == "paladin_lay_on_hands"), "no Lay on Hands on a pet or a character at 40%: " + step);
            lock_.Health = lock_.MaxHealth * 0.15f;
            pal.AIMemory.Reset();
            step = AI.NextStep(b, pal);
            Assert(step.Kind == AIStepKind.UseAbility && step.AbilityId == "paladin_lay_on_hands" && step.Target == lock_, "Lay on Hands saves the warlock: " + step);
        }

        [Test]
        public static void Companion_DoesNotRefreshAFreshImmolate()
        {
            var b = NewBattle(34, new Inventory());
            var wl = Hero(ClassId.Warlock, 12, talents: false).At(20f, 20f);
            var foe = Mob("cr_bandit_cutthroat", 12).At(28f, 20f).Tough();
            wl.FaceTowards(foe.Position);
            b.AddUnit(wl); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, wl);
            foreach (var id in wl.Abilities.Keys.ToList()) if (id != "warlock_immolate" && id != "warlock_shadow_bolt") wl.Abilities.Remove(id);
            wl.AutoPlay = true;
            wl.AttackTarget = foe;
            b.ApplyAura(wl, foe, Db.Aura("warlock_immolate"), new AuraApplyInfo { Source = Db.Ability("warlock_immolate"), Duration = 15f });
            var step = AI.NextStep(b, wl);
            Assert(step.Kind == AIStepKind.UseAbility && step.AbilityId == "warlock_shadow_bolt", "Shadow Bolt while Immolate is fresh: " + step);
            foe.FindAura("warlock_immolate", wl).Remaining = 3f;
            wl.AIMemory.Reset();
            var again = AI.NextStep(b, wl);
            Assert(again.Kind == AIStepKind.UseAbility, "still casting: " + again);
        }
    }
}
