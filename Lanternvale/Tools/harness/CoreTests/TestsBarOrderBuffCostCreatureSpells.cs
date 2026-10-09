// Regression tests of a review round of core fixes: a save keeps every unit's abilities in learn order (the default
// action bar order and hotkeys, the AI's tie-break order) instead of sorting them; the companion AI leaves buffs that
// outlast the fight and cost over a tenth of its mana (Fortitude, Arcane Intellect, their party-wide versions) for
// between fights; and unowned creatures' spells and heals above level 20 follow the creature melee curve.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsBarOrderBuffCostCreatureSpells
    {
        // ------------------------------------------------------------------------------ ability order after a load

        static string Keys(Unit u) => string.Join(",", u.Abilities.Keys);
        static string BarIds(GameSession s, Unit u) => string.Join(",", s.GetAbilityBar(u, false).Select(x => x.Ability.id));

        static GameSession Reload(string json, ulong seed)
        {
            var s = new GameSession(Db, seed);
            Assert(s.LoadGame(json, out var err), "load: " + err);
            return s;
        }

        [Test]
        public static void SaveKeepsTheAbilityOrder()
        {
            // a level-1 priest: Attack, Help Up, Shoot, Smite, Lesser Heal (hotkeys 1-5), which is not the ids' sorted order
            var s = SessionTest.NewGame(ClassId.Priest, 1, seed: 811);
            s.Recruit("kael");
            var pri = s.Main;
            var ids = pri.Abilities.Keys.ToList();
            var sorted = new List<string>(ids);
            sorted.Sort(StringComparer.Ordinal);
            Assert(!ids.SequenceEqual(sorted), "the learn order is not the sorted order (else this test proves nothing): " + Keys(pri));
            string bar = BarIds(s, pri);
            string json = s.SaveGame();

            var s2 = Reload(json, 812);
            Assert(BarIds(s2, s2.Main) == bar, $"same action bar after a load: {BarIds(s2, s2.Main)} (was {bar})");
            foreach (var u in s.Roster)
            {
                var v = s2.Roster.First(x => x.Name == u.Name);
                Assert(Keys(v) == Keys(u), $"{u.Name}: abilities in the same order after a load: {Keys(v)} (was {Keys(u)})");
            }
            Assert(s2.SaveGame() == json, "save -> load -> save identical");

            // learned after a load: appended, and still last after the next load; a removed ability's slot reused by a
            // new one (talent reset, then another talent) keeps its place too
            var p2 = s2.Main;
            p2.Abilities["priest_renew"] = 1;
            p2.Abilities.Remove("priest_smite");
            p2.Abilities["priest_power_word_shield"] = 1;
            string keys2 = Keys(p2), bar2 = BarIds(s2, p2);
            var s3 = Reload(s2.SaveGame(), 813);
            Assert(Keys(s3.Main) == keys2, $"order kept across a second load: {Keys(s3.Main)} (was {keys2})");
            Assert(BarIds(s3, s3.Main) == bar2, "same bar after the second load");

            // a level-20 hunter and her pet
            var h = SessionTest.NewGame(ClassId.Hunter, 20, seed: 814);
            var r = h.UseAbility(h.Main, "hunter_call_pet");
            Assert(r.Ok && h.Main.Pet != null, "call pet: " + r.Reason);
            string hBar = BarIds(h, h.Main), petKeys = Keys(h.Main.Pet);
            var h2 = Reload(h.SaveGame(), 815);
            Assert(BarIds(h2, h2.Main) == hBar, $"hunter bar after a load: {BarIds(h2, h2.Main)} (was {hBar})");
            Assert(h2.Main.Pet != null && Keys(h2.Main.Pet) == petKeys, $"pet abilities in the same order: {Keys(h2.Main.Pet)} (was {petKeys})");
        }

        /// <summary>The abilities used in an auto-played fight of the encounter (who used what, in order).</summary>
        static string AutoFight(GameSession s, string encounterId)
        {
            var b = s.StartEncounter(encounterId);
            Assert(b != null, "start " + encounterId + ": " + s.LastError);
            s.AutoResolve(40);
            var used = s.TakeBattleEvents().Where(e => e.Type == CombatEventType.AbilityUsed).Select(e => e.Source?.Name + ":" + e.AbilityId);
            return b.Outcome + " " + string.Join(",", used);
        }

        [Test]
        public static void ReloadedGamePlaysTheSameFight()
        {
            // the AI breaks score ties in ability order: a reloaded game must fight as the continued one would (with
            // sorted abilities, each of these chose differently after the load)
            foreach (var (cls, level, companions) in new[] { (ClassId.Warlock, 10, true), (ClassId.Hunter, 12, false), (ClassId.Mage, 20, true) })
            {
                var s = SessionTest.NewGame(cls, level, seed: 8008);
                if (companions) { s.Recruit("kael"); s.Recruit("seren"); }
                s.EnterMap("whisperwood", "from_village");
                string json = s.SaveGame();
                string continued = AutoFight(s, "enc_boars_edge");
                string reloaded = AutoFight(Reload(json, 8009), "enc_boars_edge");
                Assert(reloaded == continued, $"{cls} L{level}: the reloaded game plays the same fight\n  continued {continued}\n  reloaded  {reloaded}");
            }
        }

        // ---------------------------------------------------------------------- lasting buffs and the mana pool

        static bool HasGroup(Unit u, string group)
        {
            foreach (var a in u.Auras) if (a.Def.exclusiveGroup == group) return true;
            return false;
        }

        /// <summary>The AI plays a party for <paramref name="rounds"/> rounds against the passive training dummy. Every buff
        /// it casts that outlasts the fight (a minute or more) is listed with its share of the caster's mana pool.</summary>
        static Unit[] DummyFight(int level, int seed, int rounds, List<string> lastingBuffs, params (ClassId cls, UnitRole role)[] members)
        {
            var b = NewBattle(seed, new Inventory());
            var list = new List<Unit>();
            for (int i = 0; i < members.Length; i++)
            {
                var u = Hero(members[i].cls, level, name: members[i].cls + "_" + i).At(14 + 2 * i, 18 + (i % 2) * 4);
                u.RoleOverride = members[i].role;
                b.AddUnit(u);
                list.Add(u);
            }
            b.AddUnit(Mob("cr_training_dummy", level).At(27, 20).Tough());
            b.EventRaised += ev =>
            {
                if (ev.Type != CombatEventType.AbilityUsed || ev.Source == null || !list.Contains(ev.Source)) return;
                var a = Db.Ability(ev.AbilityId);
                if (a == null || a.aiHint != "Buff" || a.cost == null || a.cost.type != ResourceType.Mana) return;
                AuraDef def = null;
                foreach (var e in a.effects) if (e.type == EffectType.ApplyAura && (def = Db.Aura(e.aura)) != null) break;
                if (def == null || def.duration < 60f) return;
                var u = ev.Source;
                float cost = AbilityRules.ResourceCost(u, a, AbilityRules.UsedRank(u, a), AbilityMods.For(u, a));
                lastingBuffs.Add($"{u.Name}:{a.id}:{100f * cost / u.MaxMana:0.0}%:{def.charges}");
                Assert(cost <= AI.CombatBuffManaShare * u.MaxMana || def.charges > 0,
                    $"L{level}: {u.Name} cast {a.id} in combat for {cost:0} of {u.MaxMana:0} mana");
            };
            Run(b, rounds);
            return list.ToArray();
        }

        [Test]
        public static void CostlyLastingBuffsWaitForTheEndOfTheFight()
        {
            // level 30: Fortitude and Arcane Intellect cost ~23% of the pool; a healer casting them on three members at the
            // start of every fight was dry by round 3. Fear Ward, Shadow Protection (3-7%) and Blessings still go up.
            var used30 = new List<string>();
            var p30 = DummyFight(30, 821, 3, used30, (ClassId.Warrior, UnitRole.Tank), (ClassId.Priest, UnitRole.Healer), (ClassId.Mage, UnitRole.RangedDps),
                (ClassId.Paladin, UnitRole.MeleeDps));
            foreach (var u in p30)
            {
                Assert(!HasGroup(u, "priest_fortitude"), $"L30: no Fortitude on {u.Name} in combat");
                Assert(!HasGroup(u, "mage_intellect"), $"L30: no Arcane Intellect on {u.Name} in combat");
            }
            Assert(used30.Any(x => x.Contains("paladin_blessing")), "L30: cheap lasting buffs still go up in combat: " + string.Join(", ", used30));

            // level 60: nor their party-wide versions (Prayer of Fortitude ~32%, Arcane Brilliance ~25%)
            var used60 = new List<string>();
            var p60 = DummyFight(60, 822, 3, used60, (ClassId.Warrior, UnitRole.Tank), (ClassId.Priest, UnitRole.Healer), (ClassId.Mage, UnitRole.RangedDps),
                (ClassId.Shaman, UnitRole.MeleeDps));
            foreach (var u in p60)
            {
                Assert(!HasGroup(u, "priest_fortitude"), $"L60: no Fortitude/Prayer of Fortitude on {u.Name} in combat");
                Assert(!HasGroup(u, "mage_intellect"), $"L60: no Arcane Intellect/Brilliance on {u.Name} in combat");
            }
            // a shield spent by the fight (Lightning Shield, 3 charges, ~11% of the pool) still goes up in it
            Assert(p60[3].HasAura("shaman_lightning_shield"), "L60: the shaman raises Lightning Shield in combat: " + string.Join(", ", used60));

            // out of combat the same buff is a normal cast (the player's, between fights)
            var field = NewBattle(823, new Inventory(), inCombat: false);
            var fp = Hero(ClassId.Priest, 30, name: "FieldPriest").At(10, 10);
            var fw = Hero(ClassId.Warrior, 30, name: "FieldWarrior").At(11, 10);
            field.AddUnit(fp); field.AddUnit(fw);
            Assert(field.UseAbility(fp, "priest_power_word_fortitude", fw).Ok && fw.HasAura("priest_power_word_fortitude"), "Fortitude cast between fights");
        }

        // --------------------------------------------------------------------- creature spells above level 20

        static float GreyBolt(int level, out float lo, out float hi, int seed)
        {
            var b = NewBattle(seed, new Inventory());
            var wisp = Mob("cr_hollow_wisp", level).At(10, 10);
            var dummy = Mob("cr_training_dummy", level, Team.Player).At(14, 10).Tough();
            b.AddUnit(wisp); b.AddUnit(dummy);
            b.Begin();
            var a = Db.Ability("cr_grey_bolt");
            var e = a.effects[0];
            int lvl = CreatureScaling.SpellMagnitudeLevel(wisp, level, a.learnLevel, out float k);
            float dm = CreatureScaling.DamageMult(wisp.Creature);
            lo = (e.min + e.perLevel * Math.Max(0, lvl - a.learnLevel)) * k * dm;
            hi = (Math.Max(e.min, e.max) + e.perLevel * Math.Max(0, lvl - a.learnLevel)) * k * dm;
            int cursor = 0;
            b.TakeEvents(ref cursor);
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < 40; i++)
            {
                b.TriggerAbility(wisp, a, dummy, null);
                foreach (var ev in b.TakeEvents(ref cursor))
                {
                    if (ev.Type != CombatEventType.Damage || ev.Source != wisp || ev.AbilityId != a.id || ev.Crit) continue;
                    float v = ev.Amount + ev.Resisted + ev.Absorbed;
                    Assert(v >= lo - 0.5f && v <= hi + 0.5f, $"L{level} Grey Bolt hit {v:0.0} within {lo:0.0}-{hi:0.0}");
                    sum += v;
                    n++;
                }
            }
            Assert(n >= 10, $"L{level}: enough Grey Bolt hits ({n})");
            return sum / n;
        }

        static (float lo, float hi) ParseRange(string s)
        {
            var parts = s.Split(new[] { " to " }, StringSplitOptions.None);
            float lo = float.Parse(parts[0], CultureInfo.InvariantCulture);
            return (lo, parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : lo);
        }

        [Test]
        public static void CreatureSpellsFollowTheMeleeCurve()
        {
            var bolt = Db.Ability("cr_grey_bolt");
            var e = bolt.effects[0];

            // who follows the curve: unowned creatures and a creature's own summons; never characters, their pets or totems
            var wisp60 = Mob("cr_hollow_wisp", 60);
            Assert(CreatureScaling.SpellMagnitudeLevel(wisp60, 60, 1, out float k60) == CreatureScaling.SpellCurveLevel, "L60 wisp: read at level 20");
            AssertNear(k60, CreatureScaling.MeleeDps(60) / CreatureScaling.MeleeDps(20), 1e-4f, "L60 wisp: × the melee curve");
            Assert(CreatureScaling.SpellMagnitudeLevel(Mob("cr_hollow_wisp", 12), 12, 1, out float k12) == 12 && k12 == 1f, "L12 wisp: unchanged");
            Assert(CreatureScaling.SpellMagnitudeLevel(wisp60, 20, 1, out float k20) == 20 && k20 == 1f, "level 20 itself: unchanged");
            var warden = Mob("cr_hollow_warden", 60);
            var summoned = UnitFactory.CreateSummon(Db, Db.Creature("cr_hollow_wisp"), warden, UnitKind.Summon, 30f);
            Assert(CreatureScaling.UsesCreatureSpellCurve(summoned), "the Warden's summoned wisps are creatures too");
            var lock60 = Hero(ClassId.Warlock, 60, name: "Lock");
            var imp = UnitFactory.CreateSummon(Db, Db.Creature("warlock_demon_imp"), lock60, UnitKind.Pet, -1f);
            Assert(!CreatureScaling.UsesCreatureSpellCurve(imp) && CreatureScaling.SpellMagnitudeLevel(imp, 60, 1, out float kp) == 60 && kp == 1f,
                "a character's pet keeps the linear values");
            Assert(!CreatureScaling.UsesCreatureSpellCurve(lock60), "characters keep the linear values");

            // the battle: unchanged at 12, ~2× the old linear value at 60 (old 57-60 before damageMult)
            float dm = CreatureScaling.DamageMult(wisp60.Creature);
            float avg12 = GreyBolt(12, out float lo12, out float hi12, 831);
            AssertNear(lo12, (e.min + e.perLevel * 11) * dm, 1e-3f, "L12 range as the data says");
            float avg60 = GreyBolt(60, out float lo60, out float hi60, 832);
            float oldHi60 = (Math.Max(e.min, e.max) + e.perLevel * 59) * dm;
            Assert(lo60 > 1.8f * oldHi60, $"L60 Grey Bolt {lo60:0}-{hi60:0} well above the old linear {oldHi60:0}");
            float swing60 = CreatureScaling.SwingAverage(wisp60.Creature, 60) * dm;
            Assert(avg60 > 0.6f * swing60, $"L60 Grey Bolt ({avg60:0}) keeps pace with the wisp's own attack ({swing60:0})");

            // the tooltip shows what the battle deals
            var (tlo, thi) = ParseRange(Tooltip.Magnitude(wisp60, bolt, e, 60, AbilityMods.For(wisp60, bolt)));
            AssertNear(tlo, lo60, 1f, "tooltip low end at 60");
            AssertNear(thi, hi60, 1f, "tooltip high end at 60");

            // heals too: Dark Mend of a level-60 bandit hexer
            var b = NewBattle(833, new Inventory());
            var hexer = Mob("cr_bandit_hexer", 60).At(10, 10);
            var ally = Mob("cr_bandit_hexer", 60).At(12, 10).Tough();
            var foe = Mob("cr_training_dummy", 60, Team.Player).At(30, 10);
            b.AddUnit(hexer); b.AddUnit(ally); b.AddUnit(foe);
            b.Begin();
            var mend = Db.Ability("cr_dark_mend");
            var he = mend.effects[0];
            int hl = CreatureScaling.SpellMagnitudeLevel(hexer, 60, mend.learnLevel, out float hk);
            float hlo = (he.min + he.perLevel * Math.Max(0, hl - mend.learnLevel)) * hk, hhi = (Math.Max(he.min, he.max) + he.perLevel * Math.Max(0, hl - mend.learnLevel)) * hk;
            Assert(hlo > he.max + he.perLevel * 59, $"L60 Dark Mend {hlo:0}-{hhi:0} above the old linear {he.max + he.perLevel * 59:0}");
            int cursor = 0;
            b.TakeEvents(ref cursor);
            int heals = 0;
            for (int i = 0; i < 20; i++)
            {
                ally.Health = ally.MaxHealth * 0.2f;
                b.TriggerAbility(hexer, mend, ally, null);
                foreach (var ev in b.TakeEvents(ref cursor))
                {
                    if (ev.Type != CombatEventType.Heal || ev.Source != hexer || ev.Crit || ev.Periodic) continue;
                    float v = ev.Amount + ev.Overheal;
                    Assert(v >= hlo - 0.5f && v <= hhi + 0.5f, $"L60 Dark Mend heal {v:0.0} within {hlo:0.0}-{hhi:0.0}");
                    heals++;
                }
            }
            Assert(heals >= 10, "enough Dark Mend heals: " + heals);
        }
    }
}
