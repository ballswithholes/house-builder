// Rules engine tests for class specials (one or more per class) and the special-handler hooks they rely on.
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
    public static class TestsRulesSpecials
    {
        static (Battle b, Unit hero, Unit foe) Setup(ClassId c, int level, string mob = "cr_bandit_cutthroat", int mobLevel = 0, float dist = 2f, int seed = 5, bool tough = true)
        {
            var b = NewBattle(seed, new Inventory());
            var hero = Hero(c, level).At(20f, 20f);
            hero.AutoPlay = false;
            var foe = Mob(mob, mobLevel > 0 ? mobLevel : Math.Max(1, level - 3)).At(20f + dist, 20f);
            if (tough) foe.Tough();
            hero.FaceTowards(foe.Position);
            foe.FaceTowards(hero.Position);
            b.AddUnit(hero);
            b.AddUnit(foe);
            b.Begin();
            SkipTo(b, hero);
            return (b, hero, foe);
        }

        static void Fresh(Unit u)
        {
            u.TimeLeft = 6f;
            u.TimeDebt = 0f;
            u.Cooldowns.Clear();
            u.Mana = u.MaxMana;
            u.Energy = u.MaxResource(ResourceType.Energy);
        }

        // ------------------------------------------------------------- warrior

        [Test]
        public static void WarriorStanceSwapKeepsTacticalMasteryRage()
        {
            var (b, w, foe) = Setup(ClassId.Warrior, 30);
            w.Talents["warrior_arms_tactical_mastery"] = 3;
            if (w.HasAura("warrior_defensive_stance_aura")) { Fresh(w); b.UseAbility(w, "warrior_battle_stance"); }
            Assert(!b.CanUse(w, "warrior_battle_stance").Ok || !w.HasAura("warrior_battle_stance_aura"), "already-in-stance check");
            Fresh(w);
            w.Rage = 60f;
            var r = b.UseAbility(w, "warrior_defensive_stance");
            Assert(r.Ok, "defensive stance: " + r.Reason);
            AssertNear(w.Rage, 15f, 0.01f, "rage cut to 5 × Tactical Mastery rank");
            Assert(w.HasAura("warrior_defensive_stance_aura") && !w.HasAura("warrior_battle_stance_aura"), "stance replaced");
        }

        [Test]
        public static void WarriorChargeOnlyBeforeEngaging()
        {
            var b = NewBattle(4, new Inventory());
            var w = Hero(ClassId.Warrior, 20).At(10f, 20f);
            var foe = Mob("cr_wolf", 18).At(18f, 20f).Tough();
            w.AutoPlay = false;
            b.AddUnit(w); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, w);
            if (!w.HasAura("warrior_battle_stance_aura")) { Fresh(w); b.UseAbility(w, "warrior_battle_stance"); }
            Fresh(w);
            var r = b.UseAbility(w, "warrior_charge", foe);
            Assert(r.Ok, "first charge: " + r.Reason);
            Assert(w.DistanceTo(foe) < 3f, "charged into melee");
            w.Position = new Vec2(10f, 20f);
            Fresh(w);
            var again = b.CanUse(w, "warrior_charge", foe);
            Assert(!again.Ok && again.Reason.Contains("combat"), "charge blocked once engaged: " + again.Reason);
        }

        [Test]
        public static void WarriorExecuteUsesExtraRage()
        {
            var (b, w, foe) = Setup(ClassId.Warrior, 40, mobLevel: 20);
            if (!w.HasAura("warrior_battle_stance_aura")) { Fresh(w); b.UseAbility(w, "warrior_battle_stance"); }
            foe.Health = foe.MaxHealth * 0.1f;
            var ex = Db.Ability("warrior_execute");
            var h = Specials.Get("WarriorExecute");
            var c = new AbilityCast { Battle = b, Caster = w, Ability = ex, Rank = 3, Target = foe, ExtraResource = 30f };
            float v = h.ModifyDamage(c, ex.effects[0], foe, 100f);
            AssertNear(v, 100f + 30f * 3f * 3f, 0.01f, "3 × rank damage per extra rage");
            Fresh(w);
            w.Rage = 80f;
            w.OpeningSwingUsed = true;   // measure the drain alone (the opening swing after a strike would add rage)
            var r = b.UseAbility(w, "warrior_execute", foe);
            Assert(r.Ok, "execute: " + r.Reason);
            Assert(w.Rage < 1f, $"all rage consumed (left {w.Rage})");
        }

        [Test]
        public static void OverpowerIsUnavoidable()
        {
            var b = NewBattle(1);
            var w = Hero(ClassId.Warrior, 30);
            var a = Db.Ability("warrior_overpower");
            var c = new AbilityCast { Battle = b, Caster = w, Ability = a };
            Assert(Specials.Get("WarriorOverpower").Unavoidable(c, a.effects[0]), "overpower cannot be dodged/parried/blocked");
        }

        [Test]
        public static void BerserkerRageAndImmunityTags()
        {
            var (b, w, foe) = Setup(ClassId.Warrior, 40);
            var fear = Db.Aura("warlock_fear");
            b.ApplyAura(foe, w, fear);
            var br = Db.Aura("warrior_berserker_rage");
            b.ApplyAura(w, w, br);
            Assert(!w.HasState(UnitState.Fear) && w.FindAura("warlock_fear") == null, "berserker rage breaks fear");
            Assert(b.ApplyAura(foe, w, fear) == null, "immune to new fear");
            b.RemoveAura(w.FindAura(br.id), AuraRemoveReason.Cancelled);
            var reck = Db.Aura("warrior_recklessness");
            b.ApplyAura(w, w, reck);
            Assert(b.ApplyAura(foe, w, fear) == null, "recklessness: immune to fear (ImmuneFear tag)");
        }

        [Test]
        public static void SweepingStrikesCopiesHits()
        {
            var (b, w, foe) = Setup(ClassId.Warrior, 40, mobLevel: 30);
            var other = Mob("cr_bandit_cutthroat", 30).At(22.5f, 21f).Tough();
            b.AddUnit(other);
            var ss = b.ApplyAura(w, w, Db.Aura("warrior_sweeping_strikes"));
            float before = other.Health;
            b.DealDamage(w, foe, 100f, School.Physical, new DamageInfo { Kind = AttackKind.Melee, AutoAttack = true, IgnoreArmor = true, Name = "test" });
            Assert(other.Health < before, "copied to the second enemy");
            Assert(ss.Charges == 4, "one charge used");
        }

        [Test]
        public static void RankDurationsByRank()
        {
            var (b, r, foe) = Setup(ClassId.Rogue, 60);
            var sap = Db.Aura("rogue_sap");
            var inst = b.ApplyAura(r, foe, sap, new AuraApplyInfo { Rank = 3, EffLevel = 48, LearnLevel = 10, Duration = sap.duration, Mods = AbilityModSet.Empty });
            Assert(inst != null, "sap applied");
            AssertNear(inst.Duration, 45f, 0.01f, "rank 3 sap lasts 45 s");
        }

        // ------------------------------------------------------------- rogue

        [Test]
        public static void RogueVanishNeedsAndConsumesPowder()
        {
            var (b, r, foe) = Setup(ClassId.Rogue, 30);
            Fresh(r);
            var chk = b.CanUse(r, "rogue_vanish", r);
            Assert(!chk.Ok && chk.Reason.Contains("Flash Powder"), "needs Flash Powder: " + chk.Reason);
            b.Inventory.Add(Db.Item("rogue_flash_powder"), 2);
            b.AddThreat(foe, r, 300f, true);
            var snare = Db.Aura("hunter_frost_trap");
            b.ApplyAura(foe, r, snare);
            var res = b.UseAbility(r, "rogue_vanish", r);
            Assert(res.Ok, "vanish: " + res.Reason);
            Assert(b.Inventory.Count("rogue_flash_powder") == 1, "powder consumed");
            Assert(r.IsStealthed && r.HasAura("rogue_vanish_aura"), "stealthed");
            Assert(b.ThreatOf(foe, r) <= 0.01f, "threat dropped");
            Assert(r.FindAura("hunter_frost_trap") == null, "snare removed");
            Assert(!b.CanSee(foe, r), "undetectable even in melee range");
        }

        [Test]
        public static void RoguePerComboByRankAddsDamage()
        {
            var b = NewBattle(1);
            var r = Hero(ClassId.Rogue, 60);
            var ev = Db.Ability("rogue_eviscerate");
            var h = Specials.Get("RoguePerComboByRank");
            var c = new AbilityCast { Battle = b, Caster = r, Ability = ev, Rank = 8, EffLevel = 57, LearnLevel = 1, ComboPoints = 5 };
            float v = h.ModifyDamage(c, ev.effects[0], null, 0f);
            AssertNear(v, ev.effects[0].amount * 56f * 5f, 0.5f, "amount × (effLevel − learnLevel) × combo points");
        }

        [Test]
        public static void ExposeArmorScalesAndReplacesSunder()
        {
            var (b, r, foe) = Setup(ClassId.Rogue, 60);
            var ea = b.ApplyAura(r, foe, Db.Aura("rogue_expose_armor"), new AuraApplyInfo { Rank = 5, EffLevel = 56, LearnLevel = 14, ComboPoints = 5, Duration = 30f });
            Assert(ea != null, "applied");
            AssertNear(ea.ModValues[0], -340f * 5f, 1f, "armor reduction × combo points");
            var sunder = b.ApplyAura(r, foe, Db.Aura("warrior_sunder_armor"), new AuraApplyInfo { Rank = 5, EffLevel = 58, LearnLevel = 10, Duration = 30f });
            Assert(foe.FindAura("warrior_sunder_armor") == null && foe.HasAura("rogue_expose_armor"), "weaker sunder removed");
        }

        [Test]
        public static void KidneyShotRankAndTalent()
        {
            var (b, r, foe) = Setup(ClassId.Rogue, 40);
            r.Talents["rogue_assassination_improved_kidney_shot"] = 3;
            var def = Db.Aura("rogue_kidney_shot");
            var ks = b.ApplyAura(r, foe, def, new AuraApplyInfo { Rank = 1, EffLevel = 30, LearnLevel = 30, ComboPoints = 3, Duration = 4f });
            Assert(ks != null, "applied");
            AssertNear(ks.Duration, 3f, 0.01f, "rank 1 lasts one second less");
            AssertNear(ks.ModValues[0], 9f, 0.01f, "+9% damage taken with 3/3 Improved Kidney Shot");
        }

        [Test]
        public static void RelentlessStrikesAndVigor()
        {
            var r = Hero(ClassId.Rogue, 40, talents: false);
            float e0 = r.MaxResource(ResourceType.Energy);
            r.Talents["rogue_assassination_vigor"] = 1;
            r.InvalidateStats();
            AssertNear(r.MaxResource(ResourceType.Energy), e0 + 10f, 0.01f, "Vigor +10 max energy");
            var b = NewBattle(2);
            b.AddUnit(r);
            r.Talents["rogue_assassination_relentless_strikes"] = 1;
            r.Energy = 0f;
            Specials.Get("RogueRelentlessStrikes").OnProc(b, ProcTrigger.OnFinisher, r, null, new ProcInfo { ComboPoints = 5 }, 1);
            AssertNear(r.Energy, 25f, 0.01f, "5 combo points: 100% for 25 energy");
        }

        // ------------------------------------------------------------- mage

        [Test]
        public static void PresenceOfMindMakesNextCastInstant()
        {
            var (b, m, foe) = Setup(ClassId.Mage, 40, dist: 7f);
            var fb = Db.Ability("mage_fireball");
            b.ApplyAura(m, m, Db.Aura("mage_presence_of_mind"));
            AssertNear(AbilityRules.CastTime(m, fb, AbilityMods.For(m, fb)), 0f, 0.001f, "instant while PoM is up");
            Fresh(m);
            Assert(b.UseAbility(m, "mage_fireball", foe).Ok, "fireball");
            Assert(m.Pending == null && !m.HasAura("mage_presence_of_mind"), "resolved instantly, PoM consumed");
            Assert(AbilityRules.CastTime(m, fb, AbilityMods.For(m, fb)) > 1f, "next one has a cast time again");
        }

        [Test]
        public static void PolymorphOneTargetAndBlinkFreedom()
        {
            var (b, m, foe) = Setup(ClassId.Mage, 40, mob: "cr_bandit_cutthroat", dist: 8f);
            var second = Mob("cr_bandit_cutthroat", 35).At(28f, 22f);
            b.AddUnit(second);
            var poly = Db.Aura("mage_polymorph");
            b.ApplyAura(m, foe, poly);
            b.ApplyAura(m, second, poly);
            Assert(foe.FindAura("mage_polymorph") == null && second.HasAura("mage_polymorph"), "only one polymorph per mage");
            b.ApplyAura(foe, m, Db.Aura("mage_frost_nova_root"));
            Assert(m.HasState(UnitState.Root), "rooted");
            Fresh(m);
            var r = b.UseAbility(m, "mage_blink", null, new Vec2(14f, 20f));
            Assert(r.Ok, "blink: " + r.Reason);
            Assert(!m.HasStateAura(UnitState.Root), "blink removes roots");
        }

        [Test]
        public static void IgniteBuildsAPool()
        {
            var (b, m, foe) = Setup(ClassId.Mage, 40, dist: 7f);
            m.Talents["mage_fire_ignite"] = 5;
            var fb = Db.Ability("mage_fireball");
            var c = new AbilityCast { Battle = b, Caster = m, Ability = fb, Target = foe, School = School.Fire, Mods = AbilityModSet.Empty };
            Specials.OnEffectCrit(c, fb.effects[0], foe, 500f, false);
            var ig = foe.FindAura("mage_ignite");
            Assert(ig != null, "ignite applied");
            AssertNear(ig.GetVar("pool"), 200f, 0.5f, "40% of the crit in the pool");
            float hp = foe.Health;
            b.TickAura(ig);
            Assert(foe.Health < hp - 50f, "tick deals pool / remaining ticks");
        }

        [Test]
        public static void ShatterAndWintersChillCrit()
        {
            var (b, m, foe) = Setup(ClassId.Mage, 40, dist: 7f);
            m.Talents["mage_frost_shatter"] = 5;
            var fb = Db.Ability("mage_frostbolt");
            var c = b.NewCast(m, fb, 1, foe, null, null);
            c.CurrentEffect = fb.effects.First(e => e.type == EffectType.Damage);
            float baseCh = b.CritChance(c, foe, false, School.Frost);
            b.ApplyAura(m, foe, Db.Aura("mage_frost_nova_root"));
            float frozen = b.CritChance(c, foe, false, School.Frost);
            AssertNear(frozen - baseCh, 50f, 0.01f, "Shatter +50% vs frozen");
            b.ApplyAura(m, foe, Db.Aura("mage_winters_chill"), new AuraApplyInfo { Stacks = 3, Duration = 15f });
            float wc = b.CritChance(c, foe, false, School.Frost);
            AssertNear(Math.Min(100f, wc) - Math.Min(100f, frozen), Math.Min(100f, frozen + 6f) - Math.Min(100f, frozen), 0.01f, "Winter's Chill +2% per stack");
        }

        [Test]
        public static void DampenMagicReducesSpellDamage()
        {
            var (b, m, foe) = Setup(ClassId.Mage, 60);
            b.ApplyAura(m, m, Db.Aura("mage_dampen_magic"), new AuraApplyInfo { Rank = 5, EffLevel = 60, LearnLevel = 12, Duration = 600f });
            float hp = m.Health;
            b.DealDamage(foe, m, 100f, School.Fire, new DamageInfo { Kind = AttackKind.Spell, Name = "t" });
            Assert(hp - m.Health <= 100f - 90f + 0.5f, $"90 less fire damage (took {hp - m.Health})");
        }

        // ------------------------------------------------------------- priest

        [Test]
        public static void ManaBurnBurnsAndDamages()
        {
            var (b, p, _) = Setup(ClassId.Priest, 40, dist: 8f);
            var foe = Hero(ClassId.Mage, 38, name: "Enemy Mage").At(26f, 20f);
            foe.Team = Team.Enemy;
            b.AddUnit(foe);
            Assert(foe.MaxMana > 0f, "enemy mage has mana");
            float mana = foe.Mana, hp = foe.Health;
            var h = Specials.Get("PriestManaBurn");
            var a = Db.Ability("priest_mana_burn");
            var c = new AbilityCast { Battle = b, Caster = p, Ability = a, Target = foe, Rank = 1, EffLevel = 24, LearnLevel = 24, Mods = AbilityModSet.Empty, SourceProc = new ProcDef() };
            h.Execute(c, a.effects[0], foe);
            Assert(foe.Mana < mana, "mana burned");
            Assert(foe.Health < hp, "shadow damage dealt");
        }

        [Test]
        public static void FadeRestoresThreatWhenItEnds()
        {
            var (b, p, foe) = Setup(ClassId.Priest, 40);
            b.AddThreat(foe, p, 1000f, true);
            float t0 = b.ThreatOf(foe, p);
            Fresh(p);
            Assert(b.UseAbility(p, "priest_fade", p).Ok, "fade");
            Assert(b.ThreatOf(foe, p) < t0, "threat reduced");
            b.RemoveAura(p.FindAura("priest_fade"), AuraRemoveReason.Expired);
            AssertNear(b.ThreatOf(foe, p), t0, 1f, "threat restored when Fade ends");
        }

        [Test]
        public static void SpiritOfRedemption()
        {
            var (b, p, foe) = Setup(ClassId.Priest, 40);
            p.Abilities["priest_spirit_of_redemption"] = 1;
            b.ApplyPassives(p);
            b.DealDamage(foe, p, p.Health + 100f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(p.IsAlive && p.HasAura("priest_spirit_of_redemption"), "spirit form instead of being downed");
            var chk = b.CanUse(p, "priest_mind_blast", foe);
            Assert(!chk.Ok, "only heals in spirit form");
            b.RemoveAura(p.FindAura("priest_spirit_of_redemption"), AuraRemoveReason.Expired);
            Assert(p.Downed, "downed when the form ends");
        }

        [Test]
        public static void FearWardAndMindControl()
        {
            var (b, p, foe) = Setup(ClassId.Priest, 40, mob: "cr_bandit_cutthroat", mobLevel: 30, dist: 6f);
            b.ApplyAura(p, p, Db.Aura("priest_fear_ward"));
            Assert(b.ApplyAura(foe, p, Db.Aura("warlock_fear")) == null && !p.HasAura("priest_fear_ward"), "fear ward eats one fear");
            // mind control
            var mc = Db.Ability("priest_mind_control");
            var cast = b.NewCast(p, mc, 1, foe, null, null);
            b.ApplyAura(p, foe, Db.Aura("priest_mind_control"), new AuraApplyInfo { Duration = 60f });
            Specials.Get("PriestMindControl").Execute(cast, mc.effects[1], foe);
            Assert(foe.Team == p.Team, "controlled creature joins the party");
            Assert(!b.IsOver, "battle not won by mind controlling the last enemy");
            Fresh(p);
            b.UseAbility(p, "priest_power_word_fortitude", p);
            Assert(foe.Team != p.Team && b.ThreatOf(foe, p) > 1000f, "acting ends the control; the creature is furious at the priest");
        }

        [Test]
        public static void LightwellRenewIsContextual()
        {
            var (b, p, foe) = Setup(ClassId.Priest, 50, dist: 10f);
            p.Abilities["priest_lightwell"] = 1;
            Fresh(p);
            var r = b.UseAbility(p, "priest_lightwell", p);
            Assert(r.Ok || p.Pending != null, "lightwell: " + r.Reason);
            if (p.Pending != null) { SkipTo(b, p); }
            var well = b.Units.FirstOrDefault(u => u.Creature != null && u.Creature.id == "priest_lightwell");
            Assert(well != null && well.GetVar("charges") == 5f, "lightwell with 5 charges");
            SkipTo(b, p);
            p.Position = well.Position + new Vec2(1f, 0f);
            Assert(Specials.ContextualAbilities(b, p).Contains("priest_lightwell_renew"), "renew offered next to the well");
            p.Health = p.MaxHealth * 0.5f;
            Fresh(p);
            Assert(b.UseAbility(p, "priest_lightwell_renew", p).Ok, "use renew");
            var hot = p.FindAura("priest_lightwell_renew");
            Assert(hot != null && hot.Caster == p && well.GetVar("charges") == 4f, "HoT from the lightwell's priest, one charge used");
        }

        // ------------------------------------------------------------- paladin

        [Test]
        public static void JudgementConsumesSealAndCastsItsJudgement()
        {
            var (b, pal, foe) = Setup(ClassId.Paladin, 30);
            Fresh(pal);
            Assert(b.UseAbility(pal, "paladin_seal_of_the_crusader", pal).Ok, "seal");
            var seal = pal.FindAura("paladin_seal_of_the_crusader");
            Assert(seal != null, "seal up");
            Fresh(pal);
            var r = b.UseAbility(pal, "paladin_judgement", foe);
            Assert(r.Ok, "judgement: " + r.Reason);
            Assert(!pal.HasAura("paladin_seal_of_the_crusader"), "seal consumed");
            var deb = foe.FindAura("paladin_judgement_of_the_crusader_debuff");
            bool resisted = b.Events.Any(e => e.Type == CombatEventType.Resist && e.Target == foe);
            Assert(deb != null || resisted, "judgement of the crusader applied");
            if (deb != null)
            {
                var c = b.NewCast(pal, Db.Ability("paladin_holy_light"), 1, foe, null, null);
                float bonus = Specials.Get("PaladinJudgementOfTheCrusader").IncomingFlatDamageBonus(deb, c, new EffectDef { type = EffectType.Damage, coef = 0.5f }, School.Holy, false);
                Assert(bonus > 0f, "holy damage bonus against the judged target");
            }
        }

        [Test]
        public static void HolyShockHealsFriendsAndHurtsFoes()
        {
            var (b, pal, foe) = Setup(ClassId.Paladin, 50, dist: 5f);
            pal.Abilities["paladin_holy_shock"] = 1;
            pal.Health = pal.MaxHealth * 0.5f;
            Fresh(pal);
            float hp = pal.Health;
            Assert(b.UseAbility(pal, "paladin_holy_shock", pal).Ok, "holy shock on self");
            Assert(pal.Health > hp, "healed");
            Fresh(pal);
            float fh = foe.Health;
            Assert(b.UseAbility(pal, "paladin_holy_shock", foe).Ok, "holy shock on enemy");
            Assert(foe.Health < fh || b.Events.Any(e => e.Type == CombatEventType.Resist && e.Target == foe), "damaged");
        }

        [Test]
        public static void LayOnHandsAndForbearance()
        {
            var (b, pal, foe) = Setup(ClassId.Paladin, 50);
            pal.Health = 10f;
            Fresh(pal);
            Assert(b.UseAbility(pal, "paladin_lay_on_hands", pal).Ok, "lay on hands");
            AssertNear(pal.Health, pal.MaxHealth, 0.5f, "full heal");
            Assert(pal.Mana >= 0f && pal.Mana <= 550f + 0.5f, "mana drained (then +550 for rank 3 on self)");
            Fresh(pal);
            Assert(b.UseAbility(pal, "paladin_divine_shield", pal).Ok, "divine shield");
            Fresh(pal);
            var chk = b.CanUse(pal, "paladin_divine_protection", pal);
            Assert(!chk.Ok && chk.Reason.Contains("Forbearance"), "forbearance blocks: " + chk.Reason);
        }

        [Test]
        public static void BlessingOfSacrificeAndSanctuary()
        {
            var b = NewBattle(3);
            var pal = Hero(ClassId.Paladin, 60, name: "Pal").At(10, 10);
            var war = Hero(ClassId.Warrior, 60, name: "War").At(12, 10);
            var foe = Mob("cr_bandit_chief", 58).At(14, 10);
            b.AddUnit(pal); b.AddUnit(war); b.AddUnit(foe);
            b.Begin();
            b.ApplyAura(pal, war, Db.Aura("paladin_blessing_of_sacrifice"), new AuraApplyInfo { Rank = 2, EffLevel = 54, LearnLevel = 46, Duration = 30f });
            float ph = pal.Health, wh = war.Health;
            b.DealDamage(foe, war, 200f, School.Fire, new DamageInfo { Kind = AttackKind.Spell, IgnoreModifiers = false, Name = "t" });
            Assert(pal.Health < ph, "paladin takes part of the damage");
            b.RemoveAura(war.FindAura("paladin_blessing_of_sacrifice"), AuraRemoveReason.Cancelled);
            b.ApplyAura(pal, war, Db.Aura("paladin_greater_blessing_of_sanctuary"), new AuraApplyInfo { Duration = 900f });
            wh = war.Health;
            b.DealDamage(foe, war, 20f, School.Fire, new DamageInfo { Kind = AttackKind.Spell, Name = "t", IgnoreArmor = true });
            Assert(war.Health >= wh - 0.5f, "sanctuary removes 24 from a 20-damage hit");
        }

        [Test]
        public static void DivineInterventionKillsThePaladin()
        {
            var b = NewBattle(3);
            var pal = Hero(ClassId.Paladin, 40, name: "Pal").At(10, 10);
            var war = Hero(ClassId.Warrior, 40, name: "War").At(12, 10);
            var mage = Hero(ClassId.Mage, 40, name: "Mage").At(8, 12);
            var foe = Mob("cr_bandit_chief", 40).At(14, 10).Tough();
            pal.AutoPlay = false;
            b.AddUnit(pal); b.AddUnit(war); b.AddUnit(mage); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, pal);
            Fresh(pal);
            var r = b.UseAbility(pal, "paladin_divine_intervention", war);
            Assert(r.Ok, "DI: " + r.Reason);
            Assert(pal.Dead && !pal.Downed, "paladin dies outright");
            Assert(war.HasAura("paladin_divine_intervention"), "target protected");
            Assert(!b.IsOver, "the fight goes on while another party member stands");
            b.KillUnit(mage);
            Assert(b.IsOver && b.Outcome == BattleOutcome.Defeat, "the protected unit alone cannot win: defeat");
            Assert(!war.HasAura("paladin_divine_intervention"), "the protection ends with the battle");
        }

        // ------------------------------------------------------------- hunter

        [Test]
        public static void HunterPetLifecycle()
        {
            var (b, h, foe) = Setup(ClassId.Hunter, 30, dist: 12f);
            if (h.Pet != null) b.DismissPet(h);
            Fresh(h);
            var r = b.UseAbility(h, "hunter_call_pet", h);
            Assert(r.Ok, "call pet: " + r.Reason);
            var pet = h.Pet;
            Assert(pet != null && pet.Kind == UnitKind.Pet && pet.Level == h.Level, "pet at the hunter's level");
            pet.Health = pet.MaxHealth * 0.4f;
            Fresh(h);
            var d = b.UseAbility(h, "hunter_dismiss_pet", h);
            if (h.Pending != null) { SkipTo(b, h); }
            Assert(h.Pet == null && Math.Abs(h.HunterPet.HealthFraction - 0.4f) < 0.02f, "dismissed, health remembered");
            SkipTo(b, h);
            Fresh(h);
            Assert(b.UseAbility(h, "hunter_call_pet", h).Ok, "call again");
            AssertNear(h.Pet.Health / h.Pet.MaxHealth, 0.4f, 0.02f, "returns at remembered health");
            b.KillUnit(h.Pet);
            Assert(h.HunterPet.Dead, "pet death remembered");
            Fresh(h);
            Assert(!b.CanUse(h, "hunter_call_pet", h).Ok, "cannot call a dead pet");
            Assert(b.CanUse(h, "hunter_revive_pet", h).Ok, "revive available");
        }

        [Test]
        public static void TameBeastTakesTheCreature()
        {
            var (b, h, foe) = Setup(ClassId.Hunter, 20, dist: 10f, tough: false);
            if (h.Pet != null) b.DismissPet(h);
            var cat = Db.Creatures.Values.FirstOrDefault(c => c.tameable && c.type == CreatureType.Beast && c.rank == CreatureRank.Normal);
            if (cat == null) return; // no tameable beast in content
            var beast = Mob(cat.id, 18).At(26f, 22f);
            b.AddUnit(beast);
            var a = Db.Ability("hunter_tame_beast");
            var c = b.NewCast(h, a, 1, beast, null, null);
            Specials.Get("HunterTameBeast").Execute(c, a.effects[0], beast);
            Assert(h.Pet != null && h.Pet.Name == beast.Name && !b.Units.Contains(beast), "tamed: becomes the hunter's pet");
            Assert(h.HunterPet.TemplateId.StartsWith("hunter_pet_"), "pet template " + h.HunterPet.TemplateId);
        }

        [Test]
        public static void TrapTriggersOnMovement()
        {
            var (b, h, foe) = Setup(ClassId.Hunter, 30, mob: "cr_bandit_cutthroat", dist: 8f);
            Fresh(h);
            var r = b.UseAbility(h, "hunter_freezing_trap", null, new Vec2(22f, 20f));
            Assert(r.Ok, "trap: " + r.Reason);
            var trap = h.Totems.TryGetValue("Trap", out var t) ? t : null;
            Assert(trap != null && trap.IsUntargetable, "armed trap is untargetable");
            SkipTo(b, foe);
            var mv = b.Move(foe, new Vec2(18f, 20f));
            Assert(!b.Units.Contains(trap) || !trap.IsAlive, "trap fired and destroyed itself");
            Assert(foe.HasAura("hunter_freezing_trap") || b.Events.Any(e => e.Type == CombatEventType.Resist && e.Target == foe), "freezing trap on the mover");
            Assert(foe.Position.x > 21f, "movement stopped at the trap");
        }

        [Test]
        public static void FeignDeathResistersKeepTarget()
        {
            var (b, h, foe) = Setup(ClassId.Hunter, 40, mobLevel: 43, dist: 10f);
            b.AddThreat(foe, h, 500f, true);
            h.SetVar("fd_resist:" + foe.Id, 500f);
            var fd = Db.Aura("hunter_feign_death");
            b.ApplyAura(h, h, fd);
            Assert(h.IsFeigningDeath && !h.IsFeigningDeathFor(foe), "resisting enemy sees through");
            Assert(b.ThreatOf(foe, h) >= 499f, "its threat is kept");
        }

        [Test]
        public static void HuntersMarkAndBestialWrath()
        {
            var (b, h, foe) = Setup(ClassId.Hunter, 60, dist: 14f);
            var mark = b.ApplyAura(h, foe, Db.Aura("hunter_hunters_mark"), new AuraApplyInfo { Rank = 4, Duration = 120f });
            Assert(Specials.Get("HunterMark").IncomingRangedApBonus(mark, h) >= 110f, "+110 RAP at rank 4");
            if (h.Pet == null) { Fresh(h); b.UseAbility(h, "hunter_call_pet", h); }
            var pet = h.Pet;
            b.ApplyAura(foe, pet, Db.Aura("mage_frost_nova_root"));
            b.ApplyAura(h, pet, Db.Aura("hunter_bestial_wrath"));
            Assert(!pet.HasStateAura(UnitState.Root), "bestial wrath frees the pet");
            Assert(b.ApplyAura(foe, pet, Db.Aura("warlock_fear")) == null, "and makes it immune to fear");
        }

        // ------------------------------------------------------------- shaman

        [Test]
        public static void WindfuryGivesExtraSwings()
        {
            var (b, s, foe) = Setup(ClassId.Shaman, 40);
            var wf = b.ApplyAura(s, s, Db.Aura("shaman_windfury_weapon"), new AuraApplyInfo { Rank = 2, EffLevel = 40, LearnLevel = 30, Duration = 300f });
            var proc = wf.Def.procs[0];
            var eff = proc.effects[0];
            int before = b.Events.Count(e => e.Source == s && e.AutoAttack);
            var c = new AbilityCast { Battle = b, Caster = s, Target = s, ProcOther = foe, SourceProc = proc, ProcAura = wf, EffLevel = 40, LearnLevel = 30, Mods = AbilityModSet.Empty, ProcInfo = new ProcInfo() };
            Specials.Get("ShamanWindfuryAttack").Execute(c, eff, s);
            int swings = b.Events.Count(e => e.Source == s && e.AutoAttack) - before;
            Assert(swings == Math.Max(1, eff.count), $"{eff.count} extra swings (got {swings})");
        }

        [Test]
        public static void StoneskinReducesMeleeOnly()
        {
            var (b, s, foe) = Setup(ClassId.Shaman, 40);
            var st = b.ApplyAura(s, s, Db.Aura("shaman_stoneskin"), new AuraApplyInfo { EffLevel = 40, LearnLevel = 4, Duration = 10f });
            float r = st.Def.absorb.amount + st.Def.absorb.perLevel * 36f;
            float h0 = s.Health;
            b.DealDamage(foe, s, 50f, School.Physical, new DamageInfo { Kind = AttackKind.Melee, IgnoreArmor = true, Name = "t" });
            float melee = h0 - s.Health;
            Assert(melee < 50f - r + 1.5f && melee > 0f, $"melee reduced by {r} (took {melee})");
            Assert(st.AbsorbLeft > 0f && s.HasAura("shaman_stoneskin"), "not a depleting shield");
        }

        [Test]
        public static void GroundingTotemRedirectsSpells()
        {
            var b = NewBattle(6);
            var sham = Hero(ClassId.Shaman, 40, name: "Sham").At(10, 10);
            var foe = Mob("cr_bandit_hexer", 38).At(20, 10);
            sham.AutoPlay = false;
            b.AddUnit(sham); b.AddUnit(foe);
            b.Begin();
            SkipTo(b, sham);
            Fresh(sham);
            Assert(b.UseAbility(sham, "shaman_grounding_totem", sham).Ok, "grounding totem");
            var totem = sham.Totems["Air"];
            var bolt = Db.Ability("warlock_shadow_bolt");
            var c = b.NewCast(foe, bolt, 1, sham, null, null);
            b.RedirectOrReflect(c);
            Assert(c.Target == totem, "first spell grounded");
            var c2 = b.NewCast(foe, bolt, 1, sham, null, null);
            b.RedirectOrReflect(c2);
            Assert(c2.Target == sham, "inactive right after");
        }

        [Test]
        public static void ReincarnationOffer()
        {
            var b = NewBattle(7, new Inventory());
            var sham = Hero(ClassId.Shaman, 40, name: "Sham").At(10, 10);
            var other = Hero(ClassId.Warrior, 40, name: "War").At(12, 10);
            var foe = Mob("cr_bandit_chief", 40).At(14, 10).Tough();
            b.AddUnit(sham); b.AddUnit(other); b.AddUnit(foe);
            b.Inventory.Add(Db.Item("shaman_ankh"), 1);
            b.Begin();
            b.DealDamage(foe, sham, sham.Health + 10f, School.Physical, new DamageInfo { IgnoreModifiers = true, IgnoreAbsorb = true });
            Assert(sham.Downed && sham.SelfRes != null && sham.SelfRes.Source == "shaman_reincarnation", "reincarnation offered");
            int guard = 0;
            while (b.ActiveUnit != sham && guard++ < 20 && !b.IsOver) b.EndTurn(b.ActiveUnit);
            b.AcceptSelfResurrection(sham);
            Assert(sham.IsAlive && b.Inventory.Count("shaman_ankh") == 0, "risen, ankh used");
            Assert(sham.CooldownLeft(Db.Ability("shaman_reincarnation")) > 0f, "cooldown started");
        }

        // ------------------------------------------------------------- warlock

        [Test]
        public static void WarlockShardsLifeTapAndAgony()
        {
            var (b, w, foe) = Setup(ClassId.Warlock, 40, dist: 10f);
            Fresh(w);
            var chk = b.CanUse(w, "warlock_shadowburn", foe);
            if (w.Knows("warlock_shadowburn")) Assert(!chk.Ok && chk.Reason.Contains("Soul Shard"), "shadowburn needs a shard: " + chk.Reason);
            // life tap
            w.Mana = 0f;
            float hp = w.Health;
            Assert(b.UseAbility(w, "warlock_life_tap", w).Ok, "life tap");
            Assert(w.Health < hp && w.Mana > 0f, "health into mana");
            // curse of agony ramp
            var coa = b.ApplyAura(w, foe, Db.Aura("warlock_curse_of_agony"), new AuraApplyInfo { Source = Db.Ability("warlock_curse_of_agony"), Rank = 1, EffLevel = 8, LearnLevel = 8, Duration = 24f });
            if (coa == null) return;
            var ticks = new List<float>();
            for (int i = 0; i < 12; i++)
            {
                float before = foe.Health;
                b.TickAura(coa);
                ticks.Add(before - foe.Health);
            }
            Assert(ticks[0] < ticks[11], $"agony ramps up ({ticks[0]} → {ticks[11]})");
        }

        [Test]
        public static void FelDominationHalvesSummonCost()
        {
            var (b, w, foe) = Setup(ClassId.Warlock, 40, dist: 10f);
            b.Inventory.Add(Db.Item("soul_shard"), 2);
            if (w.Pet != null) b.DismissPet(w);
            var vw = Db.Ability("warlock_summon_voidwalker");
            float full = AbilityRules.ResourceCost(w, vw, 1, AbilityMods.For(w, vw));
            b.ApplyAura(w, w, Db.Aura("warlock_fel_domination"));
            Fresh(w);
            float m0 = w.Mana;
            Assert(b.UseAbility(w, "warlock_summon_voidwalker", w).Ok, "summon");
            int guard = 0;
            while (w.Pending != null && guard++ < 5) { SkipTo(b, w); }
            Assert(w.Pet != null, "voidwalker out");
            Assert(!w.HasAura("warlock_fel_domination"), "fel domination consumed");
            float paid = m0 - w.Mana;
            Assert(paid < full * 0.75f, $"about half cost paid ({paid} of {full})");
        }

        [Test]
        public static void DemonicSacrificeAndMasterDemonologist()
        {
            var (b, w, foe) = Setup(ClassId.Warlock, 50, dist: 10f);
            b.Inventory.Add(Db.Item("soul_shard"), 2);
            w.Talents["warlock_demonology_master_demonologist"] = 5;
            if (w.Pet != null) b.DismissPet(w);
            var imp = b.SummonUnit(w, Db.Creature("warlock_demon_imp"), UnitKind.Pet, -1f, w.Position + new Vec2(1, 0));
            Assert(w.HasAura("warlock_master_demonologist_imp") && imp.HasAura("warlock_master_demonologist_imp"), "master demonologist imp aura on both");
            var md = w.FindAura("warlock_master_demonologist_imp");
            float scaled = md.ModValues[0];
            UnitFactory.RefreshModValues(md);
            AssertNear(md.ModValues[0], scaled, 0.001f, "save-restore recompute keeps the talent scaling");
            w.Abilities["warlock_demonic_sacrifice"] = 1;
            Fresh(w);
            Assert(b.UseAbility(w, "warlock_demonic_sacrifice", w).Ok, "sacrifice");
            Assert(w.HasAura("warlock_demonic_sacrifice_imp") && w.Pet == null, "imp sacrificed for its buff");
            Assert(!w.HasAura("warlock_master_demonologist_imp"), "master demonologist aura removed with the pet");
        }

        [Test]
        public static void EnslaveDemonChangesSides()
        {
            var b = NewBattle(8, new Inventory());
            var w = Hero(ClassId.Warlock, 40, name: "Lock").At(10, 10);
            var demon = UnitFactory.CreateCreature(Db, Db.Creature("warlock_demon_succubus"), 38, Team.Enemy).At(14, 10);
            var other = Mob("cr_bandit_cutthroat", 38).At(16, 12).Tough();
            b.AddUnit(w); b.AddUnit(demon); b.AddUnit(other);
            b.Inventory.Add(Db.Item("soul_shard"), 1);
            b.Begin();
            var aura = b.ApplyAura(w, demon, Db.Aura("warlock_enslave_demon"), new AuraApplyInfo { Duration = 300f });
            Assert(aura != null && demon.Team == w.Team && w.Pet == demon, "enslaved demon is the warlock's pet");
            Assert(b.Inventory.Count("soul_shard") == 0, "shard consumed");
            b.RemoveAura(aura, AuraRemoveReason.Broken);
            Assert(demon.Team != w.Team && b.ThreatOf(demon, w) > 1000f, "breaks free, angry at the warlock");
        }

        // ------------------------------------------------------------- content

        sealed class Ctx : IContentContext
        {
            public readonly Dictionary<string, bool> Flags = new Dictionary<string, bool>();
            public readonly List<string> Events = new List<string>();
            public bool GetFlag(string f) => Flags.TryGetValue(f, out var v) && v;
            public void SetFlag(string f, bool v) => Flags[f] = v;
            public void RaiseEvent(string n, string arg = "") => Events.Add(n);
        }

        [Test]
        public static void RekindleLanternsContentSpecial()
        {
            var ctx = new Ctx();
            Assert(Specials.RunContentSpecial("RekindleLanterns", ctx), "content special runs");
            Assert(ctx.GetFlag("lanterns_rekindled") && ctx.Events.Contains("RekindleLanterns"), "flag set and event raised");
        }
    }
}
