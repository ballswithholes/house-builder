// Ability smoke test (Tools/check.sh core --filter Smoke).
//
// Every ability in the database is used through the real rules engine in a fresh, controlled battle:
//   * class abilities (level 60, every rank at max, every talent at max rank, so every talent-granted ability is known)
//     and the shared basics the class knows (attack, auto_shot, shoot, help_up);
//   * pet / demon abilities (cast by the pet), totem pulses and trap effects (the hero places the totem/trap and ends
//     the turn, the stationary unit acts), triggered hidden abilities (judgements, procs, contextual Lightwell renew);
//   * every item `use` ability (UseItem) and every enemy creature ability (cast by the creature against a party).
// The scene: caster, a damaged living ally, a downed ally, three enemies (melee range 1.9 m, 3.85 m, 5.5 m; mana users
// for mana drains; creature type chosen from the ability's requirements: beast, undead, demon, elemental, humanoid).
// Prerequisites (stances, seals, stealth, combo points, reactive windows, target health, pets, dead pets, weapons /
// shields / ranged weapons from the database, casting targets for interrupts, dispellable and removable auras, cooldowns
// to reset, roots for Blink) are set up programmatically; resources are refilled and cooldowns cleared.
// Variants: out-of-combat abilities run in a Field context and must be refused in combat (deliberate requirement, no
// side effects); non-hostile abilities also run in the field; Any-target abilities on an enemy and an ally; seal-tag
// requirements with every seal; openers also through BeginWithOpener.
// Each use must not throw, CanUse and UseAbility must agree, and it must produce a meaningful CombatEvent — or a
// pending cast / queued swing / totem pulse that resolves when the turn ends or the caster's next turn starts — with
// evidence of EVERY deterministic effect (damage, heal, aura, removal, dispel, interrupt, taunt, threat, resource, movement,
// summon, revive, item, cooldown reset, special); it must pay its cost, start its cooldown, spend its Time, never land
// and be avoided by the same target at once, never break its own crowd control before the next turn, and its aftermath
// (ticks, expiry, end of battle, out-of-combat ticks) must run without exceptions or engine warnings.
// More checks: tooltips/action bars, AI playing every ability set, proc auras firing, hit procs not being avoided, range
// rules, item level/class restrictions, level-scaling floors, aura restore.
// Problems caused by DATA (not the engine) are listed in KnownDataIssues below (expected failures, reported).
// Debugging: SMOKE_DEBUG=<ability>[@variant] dumps a job's events, SMOKE_VERBOSE=1 lists every job,
// SMOKE_MAGNITUDES=1 compares dealt amounts with tooltips, SMOKE_LEVEL=<n> explores another character level.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsAbilitySmoke
    {
        // ==============================================================================================================
        // EXPECTED FAILURES — DATA ISSUES. These abilities fail the smoke test because of their JSON data, not the
        // rules engine. They are reported to the class/content owners; remove an entry once the data is fixed (the test
        // prints entries that pass again). Key: "<abilityId>" or "<abilityId>@<variant>".
        // ==============================================================================================================
        static readonly Dictionary<string, string> KnownDataIssues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // (empty: hunter_counterattack and shaman_tremor_totem_pulse were fixed in the class data)
        };

        const int Tries = 6;
        /// <summary>SMOKE_LEVEL=&lt;n&gt; runs the class smoke tests at another character level (exploration; default 60).</summary>
        static readonly int HeroLevel = int.TryParse(Environment.GetEnvironmentVariable("SMOKE_LEVEL"), out var lv) ? lv : 60;
        static int FoeLevel => Math.Max(1, HeroLevel - 4);   // Mind Control rank 3 affects up to level 57

        // ------------------------------------------------------------------------------------------------- tests

        [Test] public static void Smoke_Warrior() => RunClass(ClassId.Warrior);
        [Test] public static void Smoke_Hunter() => RunClass(ClassId.Hunter);
        [Test] public static void Smoke_Paladin() => RunClass(ClassId.Paladin);
        [Test] public static void Smoke_Mage() => RunClass(ClassId.Mage);
        [Test] public static void Smoke_Priest() => RunClass(ClassId.Priest);
        [Test] public static void Smoke_Rogue() => RunClass(ClassId.Rogue);
        [Test] public static void Smoke_Warlock() => RunClass(ClassId.Warlock);
        [Test] public static void Smoke_Shaman() => RunClass(ClassId.Shaman);

        [Test]
        public static void Smoke_ItemUses()
        {
            var jobs = new List<Job>();
            foreach (var it in Db.Items.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(it.use)) continue;
                var a = Db.Ability(it.use);
                Assert(a != null, $"item {it.id}: unknown use ability '{it.use}'");
                var cls = ItemUser(it, a);
                foreach (var v in Variants(a, false))
                    jobs.Add(new Job { Kind = Kind.Item, A = a, Item = it, Class = cls, Variant = v, Label = it.id });
            }
            RunJobs("items", jobs);
        }

        [Test]
        public static void Smoke_EnemyCreatureAbilities()
        {
            var jobs = new List<Job>();
            foreach (var cr in Db.Creatures.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                if (OwnerClassOf(cr) != ClassId.None) continue; // pets/demons/totems/traps: tested with their class
                foreach (var ca in cr.abilities)
                {
                    var a = Db.Ability(ca.ability);
                    Assert(a != null, $"creature {cr.id}: unknown ability '{ca.ability}'");
                    if (a.passive) continue;
                    jobs.Add(new Job { Kind = Kind.Enemy, A = a, Creature = cr, Variant = cr.id, Label = cr.id });
                }
            }
            RunJobs("creatures", jobs);
        }

        /// <summary>
        /// Action bar, status and tooltip of every ability a class hero (and its pets/totems) knows, every talent at every
        /// rank and every creature/item ability: no exception, no unresolved {token} and no "?" from a token that does not
        /// match an effect.
        /// </summary>
        [Test]
        public static void Smoke_TooltipsAndActionBars()
        {
            var problems = new List<string>();
            void CheckText(string what, string desc, string text)
            {
                if (text == null) { problems.Add(what + ": null text"); return; }
                int open = text.IndexOf('{');
                if (open >= 0 && text.IndexOf('}', open) > open) problems.Add($"{what}: unresolved token in \"{text}\"");
                if (text.Contains("?") && !(desc ?? "").Contains("?")) problems.Add($"{what}: token does not match an effect in \"{desc}\"");
            }
            foreach (ClassId cls in Enum.GetValues(typeof(ClassId)))
            {
                if (cls == ClassId.None || Db.Class(cls) == null) continue;
                var s = Build(new Job { Kind = Kind.Hero, A = Db.Ability("attack"), Class = cls }, 1);
                var b = s.B;
                b.Begin();
                SkipTo(b, s.Hero, 80);
                EnsurePet(s, s.Hero);
                try
                {
                    foreach (var st in b.GetAbilityBar(s.Hero, includeHidden: true))
                    {
                        CheckText($"{cls} bar {st.Ability.id}", st.Ability.description, st.Tooltip);
                        CheckText($"{cls} full {st.Ability.id}", st.Ability.description, Tooltip.AbilityFull(s.Hero, st.Ability));
                        if (st.MaxRank < 1 || st.Rank > st.MaxRank) problems.Add($"{cls} {st.Ability.id}: rank {st.Rank}/{st.MaxRank}");
                    }
                    if (s.Hero.Pet != null)
                        foreach (var st in b.GetAbilityBar(s.Hero.Pet, includeHidden: true))
                            CheckText($"{cls} pet {st.Ability.id}", st.Ability.description, st.Tooltip);
                }
                catch (Exception e) { problems.Add($"{cls}: {e.GetType().Name}: {e.Message} @ {FirstFrame(e)}"); }
                foreach (var tree in Db.TalentTrees.Values.Where(t => t.classId == cls))
                    foreach (var t in tree.talents)
                        for (int r = 0; r <= t.maxRank; r++)
                        {
                            try { CheckText($"talent {t.id} r{r}", t.description, Tooltip.Talent(t, r, false, false)); CheckText($"talent {t.id} r{r}", t.description, Tooltip.Talent(t, r, true, false)); }
                            catch (Exception e) { problems.Add($"talent {t.id}: {e.GetType().Name}: {e.Message}"); }
                        }
            }
            foreach (var cr in Db.Creatures.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                var u = UnitFactory.CreateCreature(Db, cr, Math.Max(1, cr.levelMax), Team.Enemy);
                foreach (var ca in cr.abilities)
                {
                    var a = Db.Ability(ca.ability);
                    if (a == null) continue;
                    try { CheckText($"creature {cr.id} {a.id}", a.description, Tooltip.AbilityFull(u, a)); }
                    catch (Exception e) { problems.Add($"creature {cr.id} {a.id}: {e.GetType().Name}: {e.Message} @ {FirstFrame(e)}"); }
                }
            }
            var user = MakeHero(ClassId.Paladin, HeroLevel, everything: false);
            foreach (var it in Db.Items.Values.Where(x => !string.IsNullOrEmpty(x.use)))
            {
                var a = Db.Ability(it.use);
                try { CheckText($"item {it.id}", a.description, Tooltip.AbilityFull(user, a)); }
                catch (Exception e) { problems.Add($"item {it.id}: {e.GetType().Name}: {e.Message} @ {FirstFrame(e)}"); }
            }
            var unexpected = problems.Where(p => !KnownDataIssues.Keys.Any(k => p.Contains(" " + k + ":") || p.Contains(" " + k + " "))).ToList();
            foreach (var p in problems) Console.WriteLine($"      {(unexpected.Contains(p) ? "[FAIL]" : "[data]")} {p}");
            Assert(unexpected.Count == 0, $"{unexpected.Count} tooltip/action bar problems: " + string.Join("; ", unexpected.Take(10)));
        }

        /// <summary>
        /// The companion AI plays a level-60 hero of every class that knows every ability and talent (plus an auto-played
        /// ally, a downed ally and three enemy AIs) for a few rounds: no exception, no engine warning.
        /// </summary>
        [Test]
        public static void Smoke_AIPlaysEveryAbilitySet()
        {
            var problems = new List<string>();
            foreach (ClassId cls in Enum.GetValues(typeof(ClassId)))
            {
                if (cls == ClassId.None || Db.Class(cls) == null) continue;
                var used = new HashSet<string>();
                for (int seed = 1; seed <= 3; seed++)
                {
                    var s = Build(new Job { Kind = Kind.Hero, A = Db.Ability("attack"), Class = cls }, seed);
                    s.Hero.AutoPlay = true;
                    s.Ally.AutoPlay = true;
                    var warnings = new List<string>();
                    try { Run(s.B, 6, warnings); }
                    catch (Exception e) { problems.Add($"{cls} seed {seed}: {e.GetType().Name}: {e.Message} @ {FirstFrame(e)}"); }
                    foreach (var w in warnings.Distinct()) problems.Add($"{cls} seed {seed}: warning {w}");
                    foreach (var e in s.B.Events)
                        if ((e.Type == CombatEventType.AbilityUsed || e.Type == CombatEventType.CastStart) && e.Source == s.Hero) used.Add(e.AbilityId);
                    if (DebugKey == "ai:" + cls)
                        foreach (var e in s.B.Events)
                            if (e.Source == s.Hero || e.Target == s.Hero) Console.WriteLine($"          r{e.Round} {e.Type,-16} {e.AbilityId} {e.AuraId} {e.Amount:0} {e.Reason} | {e.Text}");
                }
                Console.WriteLine($"    AI {cls}: used {used.Count} abilities: {string.Join(", ", used.OrderBy(x => x).Take(40))}");
                if (used.Count == 0) problems.Add($"{cls}: the AI used no ability in 3 battles");
            }
            foreach (var p in problems) Console.WriteLine("      [FAIL] " + p);
            Assert(problems.Count == 0, $"{problems.Count} AI problems: " + string.Join("; ", problems.Take(8)));
        }

        /// <summary>
        /// Range rules of every hero ability on an enemy (with its other prerequisites met): melee abilities refuse a target
        /// out of melee reach, ranged ones a target beyond their range, dead-zone ones (hunter shots) a target in melee range —
        /// each with the matching failure code and without side effects.
        /// </summary>
        [Test]
        public static void Smoke_RangeRules()
        {
            var problems = new List<string>();
            int checks = 0;
            foreach (ClassId cls in Enum.GetValues(typeof(ClassId)))
            {
                if (cls == ClassId.None || Db.Class(cls) == null) continue;
                foreach (var a in SortedAbilities())
                {
                    if (a.classId != cls || a.passive || a.hidden || a.target != TargetType.Enemy) continue;
                    var r = a.requires ?? new RequirementDef();
                    if (r.notInCombat || r.minComboPoints > 0 || (a.cost != null && a.cost.consumesComboPoints) || r.targetAuras.Length > 0 ||
                        r.targetAuraTags.Length > 0 || r.targetHealthBelowPct > 0 || r.behindTarget || r.outOfMeleeRange) continue;
                    var j = new Job { Kind = Kind.Hero, A = a, Class = cls };
                    var s = Build(j, 1);
                    var b = s.B;
                    b.Begin();
                    SkipTo(b, s.Hero, 80);
                    Prepare(s, s.Hero, a);
                    var hero = s.Hero;
                    var mods = AbilityMods.For(hero, a);
                    int cursor = b.Events.Count;
                    void Expect(Unit t, UseFailure code, string what)
                    {
                        checks++;
                        var chk = b.CanUse(hero, a, t);
                        if (chk.Code != code) problems.Add($"{a.id} {what}: expected {code}, got {chk}");
                        else
                        {
                            var res = b.UseAbility(hero, a.id, t);
                            if (res.Ok) problems.Add($"{a.id} {what}: UseAbility succeeded although CanUse said {chk}");
                        }
                    }
                    if (AbilityRules.MinRangeMetres(a) > 0f && hero.DistanceTo(s.Near) < AbilityRules.MinRangeMetres(a)) Expect(s.Near, UseFailure.TooClose, "in the dead zone");
                    float max = AbilityRules.RangeMetres(hero, a, s.Far, mods, Db.Config);
                    if (!float.IsInfinity(max))
                    {
                        var p = hero.Position + new Vec2(max + 2f, -6f);
                        b.Teleport(s.Far, p);
                        Expect(s.Far, UseFailure.Range, $"at {hero.DistanceTo(s.Far):0.#} m (max {max:0.#})");
                    }
                    if (b.Events.Skip(cursor).Any(e => e.Type != CombatEventType.Teleport && e.Type != CombatEventType.Log))
                        problems.Add($"{a.id}: refused uses had side effects: {string.Join(",", b.Events.Skip(cursor).Select(e => e.Type).Distinct())}");
                }
            }
            Console.WriteLine($"    range checks: {checks}");
            foreach (var p in problems) Console.WriteLine("      [FAIL] " + p);
            Assert(problems.Count == 0, $"{problems.Count} range rule problems: " + string.Join("; ", problems.Take(8)));
        }

        /// <summary>Bag items keep their level and class requirements when used (CanUseItem and UseItem agree).</summary>
        [Test]
        public static void Smoke_ItemRestrictions()
        {
            int checks = 0;
            foreach (var it in Db.Items.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(it.use) || Db.Ability(it.use) == null) continue;
                bool levelGate = it.requiredLevel > 1;
                bool classGate = it.classes != null && it.classes.Length > 0;
                if (!levelGate && !classGate) continue;
                var allowed = classGate ? it.classes[0] : ClassId.Paladin;
                var other = Enum.GetValues(typeof(ClassId)).Cast<ClassId>().First(x => x != ClassId.None && (!classGate || !it.classes.Contains(x)));
                foreach (var (cls, level, ok) in new[] { (allowed, Math.Max(it.requiredLevel, 1), true), (allowed, it.requiredLevel - 1, !levelGate), (other, 60, !classGate) })
                {
                    if (level < 1) continue;
                    var inv = NewInventory();
                    inv.Add(it, 1);
                    var u = UnitFactory.CreateCharacter(Db, cls, cls.ToString(), level, learnAll: true);
                    var b = Battle.CreateField(Db, new Rng(3), new[] { u }, new StraightLinePathfinder(80f, 60f), inv);
                    var inst = inv.Items.First(x => x.Def == it);
                    var chk = b.CanUseItem(u, inst, u);
                    checks++;
                    if (!ok)
                    {
                        Assert(!chk.Ok && chk.Code == UseFailure.Requirement, $"{it.id}: a level {level} {cls} may use it ({chk})");
                        var r = b.UseItem(u, inst, u);
                        Assert(!r.Ok && r.Reason == chk.Reason && inv.Count(it.id) == 1, $"{it.id}: level {level} {cls} UseItem {r}");
                    }
                    else Assert(chk.Code != UseFailure.Requirement || !chk.Reason.StartsWith("Requires level") && !chk.Reason.StartsWith("Requires class"),
                                $"{it.id}: a level {level} {cls} should meet its requirements ({chk})");
                }
            }
            Console.WriteLine($"    item restriction checks: {checks}");
            Assert(checks > 0, "no restricted item found");
        }

        /// <summary>
        /// Level-scaled item uses (conjured water/food scale with the user's level) never fall below their base value for a
        /// user under the ability's base level: a level 1 drinker gains mana, it does not lose it.
        /// </summary>
        [Test]
        public static void Smoke_LevelScalingFloorsAtBase()
        {
            int checks = 0;
            foreach (var it in Db.Items.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                var a = Db.Ability(it.use);
                if (a == null || !a.scaleWithLevel || Math.Max(1, it.requiredLevel) >= a.learnLevel) continue;
                var cls = ItemUser(it, a);
                var u = UnitFactory.CreateCharacter(Db, cls, cls.ToString(), Math.Max(1, it.requiredLevel), learnAll: true);
                u.Health = u.MaxHealth * 0.3f;
                if (u.MaxMana > 0) u.Mana = u.MaxMana * 0.3f;
                var inv = NewInventory();
                inv.Add(it, 1);
                var b = Battle.CreateField(Db, new Rng(5), new[] { u }, new StraightLinePathfinder(80f, 60f), inv);
                float hp = u.Health, mana = u.Mana;
                var r = b.UseItem(u, inv.Items.First(x => x.Def == it), u);
                Assert(r.Ok, $"{it.id}: {r.Reason}");
                for (int i = 0; i < 10; i++) b.TickUnitOutOfCombat(u, 3f);
                var bad = b.Events.FirstOrDefault(e => e.Target == u && ((e.Type == CombatEventType.ResourceChange && e.Amount < 0f) || e.Type == CombatEventType.Damage));
                Assert(bad == null, $"{it.id} at level {u.Level}: {bad?.Text} ({bad?.Type} {bad?.Amount})");
                checks++;
            }
            Assert(checks > 0, "no level-scaled item below its base level found");
        }

        /// <summary>
        /// Mixed AI battles: parties of four level-60 heroes (every class over the seeds, every ability and talent) against
        /// mixed enemy groups (beasts, humanoids, casters, undead, elementals, the Warden) — no exception, no engine warning,
        /// every battle either ends or keeps running sanely (no unit with NaN/negative health, no orphaned pets/totems).
        /// </summary>
        [Test]
        public static void Smoke_AIChaosBattles()
        {
            var classes = Enum.GetValues(typeof(ClassId)).Cast<ClassId>().Where(c => c != ClassId.None && Db.Class(c) != null).ToList();
            var foes = new[] { "cr_greymane", "cr_bandit_chief", "cr_bandit_hexer", "cr_hollow_keeper", "cr_mossling_chief", "cr_hollow_treant", "cr_spider", "cr_hollow_warden" };
            var problems = new List<string>();
            for (int seed = 1; seed <= 8; seed++)
            {
                var rng = new Rng((ulong)(seed * 101));
                var inv = NewInventory();
                var b = new Battle(Db, new Rng((ulong)seed), new StraightLinePathfinder(80f, 60f), inv);
                for (int i = 0; i < 4; i++)
                {
                    var cls = classes[(seed + i * 3) % classes.Count];
                    var h = MakeHero(cls, 60, everything: true, name: cls + "#" + i);
                    h.AutoPlay = true;
                    Put(h, 14f + (i % 2) * 2f, 16f + i * 2.5f);
                    b.AddUnit(h);
                }
                for (int i = 0; i < 4; i++)
                {
                    var f = UnitFactory.CreateCreature(Db, Db.Creature(foes[rng.Range(0, foes.Length - 1)]), 58, Team.Enemy).Tough(4f);
                    Put(f, 30f + (i % 2) * 3f, 15f + i * 3f);
                    b.AddUnit(f);
                }
                var warnings = new List<string>();
                try
                {
                    Run(b, 10, warnings);
                    foreach (var u in b.Units)
                    {
                        if (float.IsNaN(u.Health) || u.Health < 0f || float.IsNaN(u.Mana)) problems.Add($"seed {seed}: {u.Name} has health {u.Health} mana {u.Mana}");
                        if (u.Owner != null && u.IsAlive && !b.Units.Contains(u.Owner)) problems.Add($"seed {seed}: {u.Name} outlived its owner's presence");
                    }
                    if (!b.IsOver) b.Finish(BattleOutcome.Victory);
                }
                catch (Exception e) { problems.Add($"seed {seed}: {e.GetType().Name}: {e.Message} @ {FirstFrame(e)}"); }
                foreach (var w in warnings.Distinct()) problems.Add($"seed {seed}: warning {w}");
                Console.WriteLine($"    chaos seed {seed}: {b.Outcome} after {b.Round} rounds, {b.Events.Count} events");
            }
            foreach (var p in problems) Console.WriteLine("      [FAIL] " + p);
            Assert(problems.Count == 0, $"{problems.Count} chaos battle problems: " + string.Join("; ", problems.Take(8)));
        }

        /// <summary>
        /// A companion-AI rogue that gouged (or sapped) an enemy finishes its turn without breaking its own crowd control: it
        /// turns to another enemy instead of restarting its auto attack on the gouged one.
        /// </summary>
        [Test]
        public static void Smoke_AIKeepsOwnCrowdControl()
        {
            int kept = 0;
            foreach (var id in new[] { "rogue_gouge", "rogue_sap" })
                for (int seed = 1; seed <= 6; seed++)
                {
                    var j = new Job { Kind = Kind.Hero, A = Db.Ability(id), Class = ClassId.Rogue };
                    var s = Build(j, seed);
                    var b = s.B;
                    b.Begin();
                    SkipTo(b, s.Hero, 80);
                    Prepare(s, s.Hero, j.A);
                    if (id == "rogue_gouge") b.StartAutoAttack(s.Hero, s.Target, Db.Ability("attack"), false); // already fighting it
                    var r = b.UseAbility(s.Hero, id, s.Target);
                    Assert(r.Ok, $"{id}: {r.Reason}");
                    var cc = s.Target.Auras.FirstOrDefault(x => x.Caster == s.Hero && Battle.IsBreakableControl(x.Def));
                    Assert(cc == null || !s.Hero.AutoAttacking || s.Hero.AttackTarget != s.Target, $"{id}: still auto attacking the target it just controlled");
                    if (cc == null) continue; // dodged / parried
                    int mark = b.Events.Count;
                    s.Hero.AutoPlay = true;
                    AI.RunTurn(b, s.Hero);
                    var broken = b.Events.Skip(mark).FirstOrDefault(e => e.Type == CombatEventType.AuraBroken && e.AuraId == cc.Def.id && e.Target == s.Target);
                    if (DebugKey == "aicc") foreach (var e in b.Events.Skip(mark)) Console.WriteLine($"          {id} s{seed} {e.Type,-16} {e.Source?.Name} {e.AbilityId} {e.AuraId} | {e.Text}");
                    Assert(broken == null, $"{id} seed {seed}: the AI broke its own {cc.Def.name}: " +
                        string.Join(" | ", b.Events.Skip(mark).Where(e => e.Source == s.Hero).Select(e => e.Text).Where(t => t.Length > 0).Take(8)));
                    kept++;
                }
            Assert(kept > 0, "no gouge/sap landed");
        }

        /// <summary>
        /// Procs that ride on a landed hit (Winter's Chill, Impact, Frostbite from Frost/Fire spells; Deep Wounds from a melee
        /// crit) are not separate melee attacks: they never miss, get dodged, parried or blocked (spell procs may be resisted).
        /// </summary>
        [Test]
        public static void Smoke_HitProcsAreNotAvoided()
        {
            int procs = 0;
            var bad = new List<string>();
            foreach (var (cls, ability) in new[] { (ClassId.Mage, "mage_frostbolt"), (ClassId.Mage, "mage_fire_blast"), (ClassId.Warrior, "warrior_mortal_strike") })
            {
                for (int seed = 1; seed <= 25; seed++)
                {
                    var j = new Job { Kind = Kind.Hero, A = Db.Ability(ability), Class = cls };
                    var s = Build(j, seed);
                    var b = s.B;
                    b.Begin();
                    SkipTo(b, s.Hero, 80);
                    if (cls == ClassId.Warrior) EquipWeapon(s.Hero, d => d.weaponType == WeaponType.TwoHandAxe, EquipSlot.MainHand); // no sword/mace procs
                    Prepare(s, s.Hero, j.A);
                    int cursor = b.Events.Count;
                    var r = b.UseAbility(s.Hero, ability, s.Target, s.Point);
                    Assert(r.Ok, $"{ability}: {r.Reason}");
                    if (s.Hero.Pending != null || cls == ClassId.Warrior) { b.EndTurn(s.Hero); SkipTo(b, s.Hero, 80); }
                    foreach (var e in b.Events.Skip(cursor))
                    {
                        if (e.Source != s.Hero) continue;
                        if (e.Type == CombatEventType.AuraApplied && e.Target != s.Hero && (e.AuraId == "mage_winters_chill" || e.AuraId == "mage_impact_stun" ||
                            e.AuraId == "mage_frostbite" || e.AuraId == "mage_fire_vulnerability" || e.AuraId == "warrior_deep_wounds")) procs++;
                        bool avoid = e.Type == CombatEventType.Dodge || e.Type == CombatEventType.Parry || e.Type == CombatEventType.Block || e.Type == CombatEventType.Miss;
                        if (avoid && string.IsNullOrEmpty(e.AbilityId)) bad.Add($"{ability} seed {seed}: {e.Text}");
                    }
                }
            }
            Console.WriteLine($"    hit procs landed: {procs}");
            Assert(procs > 0, "no hit proc landed (Winter's Chill / Impact / Deep Wounds)");
            Assert(bad.Count == 0, $"{bad.Count} procs were avoided like melee attacks: " + string.Join("; ", bad.Take(5)));
        }

        /// <summary>
        /// Every class ability (or item use) that puts a proc aura on its user — weapon imbues, poisons, seals, armors,
        /// shields, Retaliation, Blessing of Sanctuary... — is applied, then its trigger is exercised (the hero swings, gets
        /// hit, blocks or casts for a few turns): the proc must fire at least once and leave a trace.
        /// </summary>
        [Test]
        public static void Smoke_ProcAurasFire()
        {
            var problems = new List<string>();
            int checkedCount = 0;
            var uses = new List<(AbilityDef a, ClassId cls, ItemDef item)>();
            foreach (var a in SortedAbilities())
                if (a.classId != ClassId.None && !a.hidden && !a.passive) uses.Add((a, a.classId, null));
            foreach (var it in Db.Items.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                var a = Db.Ability(it.use);
                if (a != null) uses.Add((a, ItemUser(it, a), it));
            }
            foreach (var (a, cls, item) in uses)
            {
                if (a.target != TargetType.Self && a.target != TargetType.Ally) continue;
                foreach (var e in a.effects)
                {
                    if (e.type != EffectType.ApplyAura || (e.target != EffectTarget.Self && e.target != EffectTarget.Target)) continue;
                    var aura = Db.Aura(e.aura);
                    if (aura == null) continue;
                    for (int pi = 0; pi < aura.procs.Count; pi++)
                    {
                        var proc = aura.procs[pi];
                        if (proc.effects.Count == 0 || !ProcDriveable(proc.trigger)) continue;
                        if (proc.effects.Any(pe => pe.type == EffectType.Special)) continue; // Windfury/Feedback: covered by TestsRulesSpecials
                        if (proc.abilities.Length > 0 || proc.tags.Length > 0) continue; // filtered to specific abilities
                        checkedCount++;
                        string key = $"{a.id}:{aura.id}#{pi}";
                        string why = null;
                        for (int seed = 1; seed <= 4; seed++)
                        {
                            try { why = DriveProc(a, cls, item, aura, proc, seed); }
                            catch (Exception ex) { why = $"{ex.GetType().Name}: {ex.Message} @ {FirstFrame(ex)}"; break; }
                            if (why == null) break;
                        }
                        if (why != null) problems.Add($"{key} ({proc.trigger}): {why}");
                    }
                }
            }
            Console.WriteLine($"    proc auras checked: {checkedCount}");
            var unexpected = problems.Where(p => !KnownDataIssues.Keys.Any(k => p.StartsWith(k, StringComparison.Ordinal))).ToList();
            foreach (var p in problems) Console.WriteLine($"      {(unexpected.Contains(p) ? "[FAIL]" : "[data]")} {p}");
            Assert(unexpected.Count == 0, $"{unexpected.Count} proc auras never fired: " + string.Join("; ", unexpected.Take(8)));
        }

        static bool ProcDriveable(ProcTrigger t) =>
            t == ProcTrigger.OnMeleeHit || t == ProcTrigger.OnAutoAttackHit || t == ProcTrigger.OnStruck || t == ProcTrigger.OnDamaged ||
            t == ProcTrigger.OnBlock || t == ProcTrigger.OnSpellHit || t == ProcTrigger.OnSpellCast;

        /// <summary>Applies the proc aura through its ability and drives the trigger for up to 8 turns. Null = the proc fired.</summary>
        static string DriveProc(AbilityDef a, ClassId cls, ItemDef item, AuraDef aura, ProcDef proc, int seed)
        {
            var j = new Job { Kind = item != null ? Kind.Item : Kind.Hero, A = a, Class = cls, Item = item };
            var s = Build(j, seed);
            var b = s.B;
            var hero = s.Hero;
            b.Begin();
            SkipTo(b, hero, 80);
            if (proc.trigger == ProcTrigger.OnBlock) EquipWeapon(hero, d => d.weaponType == WeaponType.Shield, EquipSlot.OffHand);
            if (proc.trigger == ProcTrigger.OnMeleeHit || proc.trigger == ProcTrigger.OnAutoAttackHit)
                if (!hero.Equipment.HasMeleeWeapon) EquipWeapon(hero, d => d.equip == EquipType.OneHand, EquipSlot.MainHand);
            Prepare(s, hero, a);
            var target = a.target == TargetType.Ally ? hero : s.Target; // the hero wears it
            ActionResult r;
            if (item != null)
            {
                s.Inv.Add(item, 1);
                r = b.UseItem(hero, s.Inv.Items.First(x => x.Def == item), target, s.Point);
            }
            else r = b.UseAbility(hero, a.id, target, s.Point);
            if (!r.Ok) return "could not apply: " + r.Reason;
            if (hero.Pending != null) { b.EndTurn(hero); SkipTo(b, hero, 80); }
            if (!hero.HasAura(aura.id)) return "aura not on the user after the use";
            int mark = b.Events.Count;
            var near = s.Near;
            var spell = SortedAbilities().FirstOrDefault(x => hero.Knows(x.id) && x.target == TargetType.Enemy && x.school != School.Physical &&
                                                             x.castTime <= 0f && x.effects.Any(ef => ef.type == EffectType.Damage) &&
                                                             (proc.schools.Length == 0 || proc.schools.Contains(x.school)));
            int turns = proc.ppm > 0 ? 20 : 8;
            for (int turn = 0; turn < turns && !b.IsOver; turn++)
            {
                if (b.ActiveUnit != hero) SkipTo(b, hero, 80);
                if (b.ActiveUnit != hero) break;
                hero.Health = hero.MaxHealth * 0.6f;   // room for heals
                if (hero.MaxMana > 0) hero.Mana = hero.MaxMana * 0.5f; // room for mana gains
                near.Health = near.MaxHealth;
                hero.Cooldowns.Clear();
                if (!hero.HasAura(aura.id)) // expired (30 s seals): put it back up
                {
                    if (item != null) { s.Inv.Add(item, 1); b.UseItem(hero, s.Inv.Items.First(x => x.Def == item), target, s.Point); }
                    else b.UseAbility(hero, a.id, target, s.Point);
                    if (b.ActiveUnit != hero) continue;
                }
                switch (proc.trigger)
                {
                    case ProcTrigger.OnMeleeHit:
                    case ProcTrigger.OnAutoAttackHit:
                        b.StartAutoAttack(hero, near, Db.Ability("attack"), false);
                        break;
                    case ProcTrigger.OnSpellHit:
                    case ProcTrigger.OnSpellCast:
                        if (spell == null) return "the hero knows no instant damage spell to trigger it";
                        hero.Cooldowns.Clear();
                        b.UseAbility(hero, spell.id, near);
                        break;
                    default: // struck, damaged, block: the enemy swings at the hero (in front of it)
                        near.FaceTowards(hero.Position);
                        hero.FaceTowards(near.Position);
                        b.StartAutoAttack(near, hero, Db.Ability("attack"), false);
                        break;
                }
                b.EndTurn(hero);
                if (ProcTraces(b.Events.Skip(mark), a, aura, proc, hero)) return null;
                SkipTo(b, hero, 80);
                if (ProcTraces(b.Events.Skip(mark), a, aura, proc, hero)) return null;
            }
            var seen = string.Join(",", b.Events.Skip(mark).Select(e => e.Type.ToString()).Distinct().Take(14));
            if (DebugKey == "proc:" + aura.id)
                foreach (var e in b.Events.Skip(mark)) if (e.Source == hero || e.Target == hero) Console.WriteLine($"          r{e.Round} {e.Type,-16} {e.AbilityId} {e.AuraId} {e.Amount:0} {e.Reason} | {e.Text}");
            return "never fired [" + seen + "]";
        }

        static bool ProcTraces(IEnumerable<CombatEvent> evs, AbilityDef a, AuraDef aura, ProcDef proc, Unit hero)
        {
            foreach (var e in evs)
            {
                foreach (var pe in proc.effects)
                {
                    switch (pe.type)
                    {
                        case EffectType.ApplyAura:
                            if ((e.Type == CombatEventType.AuraApplied || e.Type == CombatEventType.AuraRefreshed || e.Type == CombatEventType.AuraStack ||
                                 e.Type == CombatEventType.Resist || e.Type == CombatEventType.Immune) && e.AuraId == pe.aura) return true;
                            break;
                        case EffectType.Damage:
                        case EffectType.WeaponDamage:
                        case EffectType.Heal:
                            if ((e.Type == CombatEventType.Damage || e.Type == CombatEventType.Heal || e.Type == CombatEventType.Absorb) &&
                                (e.AbilityId == a.id || (e.Source == hero && e.Name == aura.name))) return true;
                            break;
                        case EffectType.GainResource:
                            if (e.Type == CombatEventType.ResourceChange && e.Amount > 0f && !(e.Periodic && e.Source == e.Target) && e.Target == hero) return true;
                            break;
                        default:
                            // specials (Windfury): an extra attack or anything else attributable to the aura
                            if (e.Source == hero && (e.Type == CombatEventType.Damage || e.Type == CombatEventType.Log) && !e.AutoAttack && e.AbilityId != "attack") return true;
                            break;
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------- jobs

        enum Kind { Hero, Pet, Totem, Triggered, Item, Enemy }

        sealed class Job
        {
            public Kind Kind;
            public AbilityDef A;
            public ClassId Class;
            public CreatureDef Creature;     // pet / totem / enemy caster
            public AbilityDef Placer;        // totems/traps: the class ability that summons the creature
            public ItemDef Item;
            public string PreUse = "";       // ability used first (contextual abilities: Lightwell → its renew)
            public string Variant = "";      // "", field, opener, enemy, ally, aura:<id>, <creature id>, natural
            public string Label = "";
            public string Key => Variant.Length == 0 ? A.id : A.id + "@" + Variant;
            public bool Field => Variant == "field" || Variant.EndsWith("+field", StringComparison.Ordinal);
            public bool Opener => Variant == "opener";
        }

        enum Status { Pass, Partial, Split, Avoided, Nothing, Unusable, Inconsistent, Unresolved, Threw }

        sealed class Result
        {
            public Status Status;
            public string Text = "";
            public override string ToString() => Status + (Text.Length > 0 ? ": " + Text : "");
        }

        static IEnumerable<AbilityDef> SortedAbilities() => Db.Abilities.Values.OrderBy(x => x.id, StringComparer.Ordinal);

        static void RunClass(ClassId cls)
        {
            var jobs = new List<Job>();
            var probe = MakeHero(cls, HeroLevel);
            var creatureRefs = new HashSet<string>();
            var itemRefs = new HashSet<string>();
            foreach (var cr in Db.Creatures.Values) foreach (var ca in cr.abilities) creatureRefs.Add(ca.ability);
            foreach (var it in Db.Items.Values) if (!string.IsNullOrEmpty(it.use)) itemRefs.Add(it.use);

            // 1. class abilities (incl. talent-granted) and the shared basics the class knows
            foreach (var a in SortedAbilities())
            {
                if (a.passive || a.hidden) continue;
                bool mine = a.classId == cls || (a.classId == ClassId.None && probe.Knows(a.id) && !creatureRefs.Contains(a.id));
                if (!mine) continue;
                if (HeroLevel < 60 && !probe.Knows(a.id)) continue; // not learnable yet at this level
                Assert(probe.Knows(a.id), $"{cls} hero does not know {a.id}");
                foreach (var v in Variants(a, true)) jobs.Add(new Job { Kind = Kind.Hero, A = a, Class = cls, Variant = v });
            }
            // 2. abilities of the class's pets, demons, totems and traps (cast by the creature)
            foreach (var cr in Db.Creatures.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                if (OwnerClassOf(cr) != cls) continue;
                bool stationary = cr.rank == CreatureRank.Totem || cr.ai == AIProfile.Totem;
                var placer = stationary ? PlacerOf(cls, cr.id) : null;
                foreach (var ca in cr.abilities)
                {
                    var a = Db.Ability(ca.ability);
                    Assert(a != null, $"creature {cr.id}: unknown ability '{ca.ability}'");
                    if (a.passive) continue;
                    if (stationary)
                    {
                        Assert(placer != null, $"{cr.id}: no {cls} ability summons it");
                        jobs.Add(new Job { Kind = Kind.Totem, A = a, Class = cls, Creature = cr, Placer = placer, Variant = cr.id });
                    }
                    else
                        foreach (var v in Variants(a, false))
                            jobs.Add(new Job { Kind = Kind.Pet, A = a, Class = cls, Creature = cr, Variant = v.Length > 0 ? cr.id + "+" + v : cr.id });
                }
            }
            // 3. hidden class abilities used by neither creatures nor items: triggered (judgements, procs, contextual)
            foreach (var a in SortedAbilities())
            {
                if (a.classId != cls || !a.hidden || a.passive || creatureRefs.Contains(a.id) || itemRefs.Contains(a.id)) continue;
                jobs.Add(new Job { Kind = Kind.Triggered, A = a, Class = cls, Variant = "triggered" });
                if (PreUses.TryGetValue(a.id, out var pre)) jobs.Add(new Job { Kind = Kind.Hero, A = a, Class = cls, PreUse = pre, Variant = "natural" });
            }
            RunJobs(cls.ToString(), jobs);
        }

        /// <summary>Contextual hidden abilities and the ability that makes them available.</summary>
        static readonly Dictionary<string, string> PreUses = new Dictionary<string, string>
        {
            { "priest_lightwell_renew", "priest_lightwell" },
        };

        static List<string> Variants(AbilityDef a, bool heroAbility)
        {
            var list = new List<string>();
            var r = a.requires ?? new RequirementDef();
            if (r.notInCombat) { list.Add("field"); list.Add("denied"); return list; }
            if (a.target == TargetType.Any) { list.Add("enemy"); list.Add("ally"); }
            else if (heroAbility && r.casterAuraTags.Length > 0)
            {
                foreach (var au in ClassAurasWithTag(a.classId, r.casterAuraTags)) list.Add("aura:" + au.id);
                if (list.Count == 0) list.Add("");
            }
            else list.Add("");
            if (heroAbility && (AbilityMods.HasTag(a, "Opener") || a.special == "WarriorCharge")) list.Add("opener");
            if (heroAbility && Specials.UsesSpecial(a, "HunterRevivePet")) list.Add("deadpet");
            bool friendly = a.target == TargetType.Self || a.target == TargetType.Ally || a.target == TargetType.AllyOther ||
                            a.target == TargetType.DeadAlly || a.target == TargetType.Pet;
            if (heroAbility && friendly && !r.inCombat && !a.autoAttack && !AbilityRules.IsHarmful(a)) list.Add("field");
            return list;
        }

        static void RunJobs(string label, List<Job> jobs)
        {
            var problems = new List<(Job job, Result res)>();
            var passedKnown = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int pass = 0;
            foreach (var j in jobs)
            {
                var res = RunJob(j);
                if (res.Status == Status.Pass)
                {
                    pass++;
                    if (KnownDataIssues.ContainsKey(j.Key) || KnownDataIssues.ContainsKey(j.A.id)) passedKnown.Add(j.Key);
                }
                else problems.Add((j, res));
            }
            var unexpected = problems.Where(p => !KnownDataIssues.ContainsKey(p.job.Key) && !KnownDataIssues.ContainsKey(p.job.A.id)).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"    smoke {label}: {jobs.Count} uses, {pass} ok, {problems.Count - unexpected.Count} known data issues, {unexpected.Count} unexpected ({sw.ElapsedMilliseconds} ms)");
            foreach (var p in problems)
            {
                bool known = !unexpected.Contains(p);
                sb.AppendLine($"      {(known ? "[data]" : "[FAIL]")} {p.job.Key,-58} {p.job.Kind,-9} {p.res}");
            }
            foreach (var k in passedKnown) sb.AppendLine($"      [now passing — remove from KnownDataIssues] {k}");
            Console.Write(sb.ToString());
            Assert(unexpected.Count == 0, $"{unexpected.Count} {label} abilities failed the smoke test: " +
                                         string.Join("; ", unexpected.Select(p => p.job.Key + " → " + p.res)));
        }

        /// <summary>SMOKE_DEBUG=&lt;job key&gt; dumps every event of that job's attempts; SMOKE_VERBOSE=1 lists every job.</summary>
        static readonly string DebugKey = Environment.GetEnvironmentVariable("SMOKE_DEBUG");
        static readonly bool Verbose = Environment.GetEnvironmentVariable("SMOKE_VERBOSE") == "1";

        static Result RunJob(Job j)
        {
            Result best = null;
            for (int seed = 1; seed <= Tries; seed++)
            {
                Result r;
                var warnings = new List<string>();
                var oldWarn = Log.WarnHandler;
                Log.WarnHandler = w => warnings.Add(w);
                try { r = Attempt(j, seed); }
                catch (Exception e) { r = new Result { Status = Status.Threw, Text = e.GetType().Name + ": " + e.Message + " @ " + FirstFrame(e) }; }
                finally { Log.WarnHandler = oldWarn; }
                if (r.Status == Status.Pass && warnings.Count > 0) r = new Result { Status = Status.Inconsistent, Text = "warning: " + warnings[0] };
                if (Verbose || (DebugKey != null && (DebugKey == j.Key || DebugKey == j.A.id)))
                {
                    Console.WriteLine($"      ~ {j.Key} seed {seed}: {r}");
                    if (DebugKey != null && LastBattle != null)
                        foreach (var e in LastBattle.Events) Console.WriteLine($"          {e.Type,-16} src={e.Source?.Name} tgt={e.Target?.Name} ab={e.AbilityId} aura={e.AuraId} amt={e.Amount:0.#} {e.Reason} | {e.Text}");
                }
                if (r.Status == Status.Pass || r.Status == Status.Threw || r.Status == Status.Split) return r;
                if (best == null || Rank(r.Status) > Rank(best.Status)) best = r;
            }
            return best;
        }

        static int Rank(Status s) => s == Status.Split ? 5 : s == Status.Partial ? 4 : s == Status.Avoided ? 3 : s == Status.Nothing ? 2 : s == Status.Unresolved ? 1 : 0;

        static string FirstFrame(Exception e)
        {
            var st = e.StackTrace ?? "";
            foreach (var line in st.Split('\n'))
                if (line.Contains("Lanternvale.Rules") || line.Contains("Lanternvale.Data")) return line.Trim();
            return st.Split('\n').FirstOrDefault()?.Trim() ?? "";
        }

        // ------------------------------------------------------------------------------------------------- scene

        sealed class Scene
        {
            public Job Job;
            public Battle B;
            public Inventory Inv;
            public Unit Hero;                 // the class character (owner of pets/totems); null for enemy-creature jobs
            public Unit Caster;               // the unit that uses the ability
            public Unit Ally, Downed;         // friendly to the caster (damaged / downed)
            public Unit Near, Far, Third;     // hostile to the caster: melee range (1.9 m), 5.5 m, 3.85 m
            public Unit Target;
            public Vec2? Point;
            public readonly List<string> FieldEvents = new List<string>();
        }

        static readonly Vec2 Origin = new Vec2(20f, 20f);

        static Inventory NewInventory()
        {
            var inv = new Inventory { Gold = 100000 };
            foreach (var id in new[] { "soul_shard", "rogue_flash_powder", "rogue_blinding_powder", "shaman_ankh" })
            {
                var def = Db.Item(id);
                if (def != null) inv.Add(def, 5);
            }
            return inv;
        }

        /// <summary>Level-60 character knowing every class ability at max rank and every talent at max rank.</summary>
        static Unit MakeHero(ClassId cls, int level, bool everything = true, string name = null)
        {
            var u = UnitFactory.CreateCharacter(Db, cls, name ?? cls.ToString(), level, learnAll: true);
            if (everything)
            {
                foreach (var tree in Db.TalentTrees.Values)
                {
                    if (tree.classId != cls) continue;
                    foreach (var t in tree.talents)
                    {
                        u.Talents[t.id] = t.maxRank;
                        foreach (var p in t.effects)
                            if (p.type == "GrantAbility" && Db.Ability(p.ability) != null)
                                u.Abilities[p.ability] = Math.Max(1, AbilityRules.MaxRankAtLevel(Db.Ability(p.ability), level));
                    }
                }
                foreach (var a in Db.Abilities.Values)
                {
                    if (a.classId != cls || a.hidden) continue;
                    int r = AbilityRules.MaxRankAtLevel(a, level);
                    if (r > 0) u.Abilities[a.id] = r;
                }
            }
            ItemGenerator.EquipVeteranGear(Db, u, new Rng(7));
            UnitFactory.AttachPassives(u);
            u.InvalidateStats();
            u.RestoreFull();
            u.AutoPlay = false;
            return u;
        }

        static Unit Foe(string creatureId, int level)
        {
            var def = Db.Creature(creatureId) ?? throw new Exception("no creature " + creatureId);
            var u = UnitFactory.CreateCreature(Db, def, level, Team.Enemy);
            u.Tough(50f);
            return u;
        }

        static void Put(Unit u, float x, float y) { u.Position = new Vec2(x, y); }

        /// <summary>Creature type the ability's enemy target must have, as a content creature id.</summary>
        static string FoeFor(AbilityDef a)
        {
            var types = a.requires != null ? a.requires.targetCreatureTypes : new CreatureType[0];
            foreach (var t in types)
            {
                switch (t)
                {
                    case CreatureType.Humanoid: return "cr_bandit_cutthroat";
                    case CreatureType.Beast: return "cr_wolf";
                    case CreatureType.Undead: return "cr_hollow_spirit";
                    case CreatureType.Demon: return "warlock_demon_voidwalker";
                    case CreatureType.Elemental: return "cr_mossling";
                    case CreatureType.Mechanical: return "cr_training_dummy";
                    case CreatureType.Spirit: return "cr_hollow_warden";
                }
            }
            return "cr_bandit_cutthroat";
        }

        static Scene Build(Job j, int seed)
        {
            var s = new Scene { Job = j, Inv = NewInventory() };
            var pf = new StraightLinePathfinder(80f, 60f);
            var rng = new Rng((ulong)(seed * 7919 + 17));
            if (j.Kind == Kind.Enemy) return BuildEnemyScene(s, pf, rng);

            var hero = MakeHero(j.Class, HeroLevel);
            Put(hero, 20f, 20f);
            hero.Facing = Vec2.Right;
            var ally = MakeHero(j.Class == ClassId.Paladin ? ClassId.Warrior : ClassId.Paladin, HeroLevel, everything: false, name: "Ally");
            Put(ally, 18.8f, 20.6f);
            ally.Health = ally.MaxHealth * 0.5f;
            if (ally.MaxMana > 0) ally.Mana = ally.MaxMana * 0.5f;
            var downed = UnitFactory.CreateCharacter(Db, ClassId.Priest, "Downed", HeroLevel, learnAll: false);
            Put(downed, 19.0f, 18.7f);
            downed.Downed = true;
            downed.Health = 0f;
            downed.Mana = 0f;
            s.Hero = hero; s.Ally = ally; s.Downed = downed;

            if (j.Field)
            {
                s.B = Battle.CreateField(Db, rng, new[] { hero, ally, downed }, pf, s.Inv);
            }
            else
            {
                s.B = new Battle(Db, rng, pf, s.Inv);
                // the enemy type follows the requirements of the tested ability (and of the totem pulse / placer)
                string foe = FoeFor(j.A);
                s.Near = Foe(foe, FoeLevel); Put(s.Near, 21.9f, 20f);
                s.Far = Foe(foe, FoeLevel); Put(s.Far, 25.5f, 20f);
                s.Third = Foe("cr_bandit_hexer", FoeLevel); Put(s.Third, 23.4f, 21.8f);
                s.B.AddUnits(new[] { hero, ally, downed, s.Near, s.Far, s.Third });
            }
            s.Caster = hero;
            if (j.Kind == Kind.Pet)
            {
                var pet = s.B.SummonUnit(hero, j.Creature, UnitKind.Pet, -1f, new Vec2(21.0f, 21.3f));
                pet.Position = new Vec2(21.0f, 21.3f);
                s.B.Teleport(pet, pet.Position);
                s.Caster = pet;
            }
            return s;
        }

        static Scene BuildEnemyScene(Scene s, IPathfinder pf, Rng rng)
        {
            var j = s.Job;
            int lvl = Math.Max(1, j.Creature.levelMax);
            s.B = new Battle(Db, rng, pf, s.Inv);
            var c = UnitFactory.CreateCreature(Db, j.Creature, lvl, Team.Enemy).Tough(50f);
            Put(c, 20f, 20f);
            c.Facing = Vec2.Right;
            var ally = UnitFactory.CreateCreature(Db, Db.Creature("cr_bandit_cutthroat"), lvl, Team.Enemy).Tough(50f);
            Put(ally, 18.8f, 20.6f);
            ally.Health = ally.MaxHealth * 0.5f;
            Unit PartyMember(ClassId cls, float x, float y)
            {
                var u = MakeHero(cls, lvl, everything: false);
                u.Tough(50f);
                Put(u, x, y);
                return u;
            }
            s.Near = PartyMember(ClassId.Warrior, 21.9f, 20f);
            s.Far = PartyMember(ClassId.Priest, 25.5f, 20f);
            s.Third = PartyMember(ClassId.Mage, 23.4f, 21.8f);
            s.Caster = c;
            s.Ally = ally;
            s.B.AddUnits(new[] { c, ally, s.Near, s.Far, s.Third });
            return s;
        }

        // ---------------------------------------------------------------------------------------------- attempt

        static Result Attempt(Job j, int seed)
        {
            var s = Build(j, seed);
            var b = s.B;
            LastBattle = b;
            bool combat = !j.Field;
            if (combat && !j.Opener)
            {
                b.Begin();
                SkipTo(b, s.Caster, 80);
                if (b.ActiveUnit != s.Caster) return Fail(Status.Unresolved, "could not reach the caster's turn");
            }
            Action<Battle, Unit, string, Unit> onField = (bb, u, name, t) => { if (bb == b) s.FieldEvents.Add(name); };
            Specials.FieldEvent += onField;
            try
            {
                Result r;
                switch (j.Kind)
                {
                    case Kind.Totem: r = AttemptTotem(s); break;
                    case Kind.Triggered: r = AttemptTriggered(s); break;
                    default: r = AttemptUse(s); break;
                }
                if (r.Status == Status.Pass) Aftermath(s);
                return r;
            }
            finally { Specials.FieldEvent -= onField; }
        }

        /// <summary>
        /// Lets everything the use started run its course (DoT/HoT ticks, expiry and onExpire effects, summon lifetimes, food
        /// and drink out of combat), then ends the battle (victory cleanup, OnBattleFinished hooks). Exceptions propagate.
        /// </summary>
        static void Aftermath(Scene s)
        {
            var b = s.B;
            if (!b.InCombat)
            {
                for (int i = 0; i < 12; i++) b.TickOutOfCombat(5f);
                return;
            }
            if (!b.Started || b.IsOver) return;
            int guard = 0;
            int startRound = b.Round;
            while (!b.IsOver && b.Round < startRound + 6 && guard++ < 400)
            {
                var u = b.ActiveUnit;
                if (u == null) break;
                if (b.PendingSelfResurrection(u) != null) { b.DeclineSelfResurrection(u); continue; }
                b.EndTurn(u);
            }
            if (!b.IsOver) b.Finish(BattleOutcome.Victory);
            foreach (var u in b.Units) { var _ = u.Stats; }
        }

        /// <summary>Every aura restored from a save (AttachAura + RefreshModValues) on a character and a creature: stats compute.</summary>
        [Test]
        public static void Smoke_AuraRestoreAndStats()
        {
            var problems = new List<string>();
            var hero = MakeHero(ClassId.Paladin, HeroLevel, everything: false);
            var mob = UnitFactory.CreateCreature(Db, Db.Creature("cr_bandit_cutthroat"), 20, Team.Enemy);
            foreach (var def in Db.Auras.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                foreach (var u in new[] { hero, mob })
                {
                    try
                    {
                        var src = ApplierOf(def, ClassId.None);
                        var inst = UnitFactory.AttachAura(u, def, hero, false, 3, 60, src != null ? src.learnLevel : 1, def.duration > 0 ? def.duration : -1f);
                        inst.ComboPoints = 3;
                        UnitFactory.RefreshModValues(inst);
                        u.InvalidateStats();
                        var st = u.Stats;
                        if (float.IsNaN(st.MaxHealth) || float.IsNaN(st.Armor) || float.IsNaN(st.AttackPower)) problems.Add($"{def.id}: NaN stats");
                        u.Auras.Remove(inst);
                        u.InvalidateStats();
                    }
                    catch (Exception e) { problems.Add($"{def.id} on {u.Name}: {e.GetType().Name}: {e.Message} @ {FirstFrame(e)}"); }
                }
            }
            foreach (var p in problems) Console.WriteLine("      [FAIL] " + p);
            Assert(problems.Count == 0, $"{problems.Count} aura restore problems: " + string.Join("; ", problems.Take(8)));
        }

        static Battle LastBattle;

        static Result Fail(Status st, string text) => new Result { Status = st, Text = text };

        /// <summary>The caster uses the ability (hero, pet, enemy creature, item, opener, field).</summary>
        static Result AttemptUse(Scene s)
        {
            var j = s.Job;
            var b = s.B;
            var a = j.A;
            if (!string.IsNullOrEmpty(j.PreUse))
            {
                var pre = Db.Ability(j.PreUse);
                var r0 = UsePrepared(s, s.Caster, pre, out _);
                if (!r0.Ok) return Fail(Status.Unusable, $"pre-use {pre.id}: {r0.Reason}");
            }
            ItemInstance inst = null;
            if (j.Kind == Kind.Item)
            {
                s.Inv.Add(j.Item, 1);
                inst = s.Inv.Items.FirstOrDefault(x => x.Def == j.Item);
            }
            Prepare(s, s.Caster, a);
            int cursor = b.Events.Count;
            var c = s.Caster;
            var book = Bookkeeping.Before(b, c, a, j);
            var chk = j.Kind == Kind.Item ? b.CanUseItem(c, inst, s.Target ?? c, s.Point) : b.CanUse(c, a, s.Target, s.Point);
            ActionResult res;
            if (j.Opener) res = b.BeginWithOpener(c, a.id, s.Target, s.Point);
            else if (j.Kind == Kind.Item) res = b.UseItem(c, inst, s.Target, s.Point);
            else res = b.UseAbility(c, a.id, s.Target, s.Point);
            if (j.Variant == "denied" || j.Variant.EndsWith("+denied", StringComparison.Ordinal))
            {
                // out-of-combat only: in combat it must be refused for that reason, without side effects
                bool clean = !b.Events.Skip(cursor).Any(e => e.Type != CombatEventType.Log);
                if (!chk.Ok && chk.Code == UseFailure.Requirement && !res.Ok && res.Reason == chk.Reason && clean) return new Result { Status = Status.Pass };
                return Fail(Status.Inconsistent, $"in combat: CanUse {chk}, UseAbility {res}, side effects: {!clean}");
            }
            if (!chk.Ok && !res.Ok) return Fail(Status.Unusable, chk.ToString());
            if (chk.Ok != res.Ok) return Fail(Status.Inconsistent, $"CanUse {chk} but UseAbility {res}");

            var immediate = b.Events.Skip(cursor).ToList();
            bool follow = InCombatNow(s) && (c.Pending != null || a.nextSwing || a.autoAttack);
            var timeProblem = book.CheckTime(b, c);
            if (!follow)
            {
                var v = Classify(s, a, immediate, null, c);
                if (v.Status == Status.Pass) v = book.CheckCostAndCooldown(b, c, immediate) ?? timeProblem ?? v;
                if (v.Status == Status.Pass && InCombatNow(s)) v = CheckControlSurvivesTurnEnd(s, a, c) ?? v;
                return v;
            }

            // pending cast / channel, queued swing or auto attack: end the turn and come back
            int mark = b.Events.Count;
            int guard = 0;
            do
            {
                if (b.IsOver || !c.IsAlive) break;
                if (b.ActiveUnit == c) b.EndTurn(c);
                if (b.IsOver) break;
                SkipTo(b, c, 80);
            } while (c.Pending != null && guard++ < 6);
            if (c.Pending != null) return Fail(Status.Unresolved, $"still casting after {guard} turns ({c.Pending})");
            var later = b.Events.Skip(mark).ToList();
            // the pending cast resolves at the caster's turn start; the queued swing announces itself (AbilityUsed)
            var inWindow = Window(later, e => e.Source == c && (e.Type == CombatEventType.TurnStart || e.AbilityId == a.id));
            Func<CombatEvent, bool> mine = a.autoAttack
                ? (Func<CombatEvent, bool>)(e => e.Source == c && e.AutoAttack)
                : e => !e.AutoAttack && (e.AbilityId == a.id || inWindow.Contains(e));
            var all = (a.nextSwing || a.autoAttack ? new List<CombatEvent>() : immediate).Concat(later.Where(mine)).ToList();
            var v2 = Classify(s, a, all, null, c);
            if (v2.Status == Status.Pass) v2 = book.CheckCostAndCooldown(b, c, immediate.Concat(later.Where(e => inWindow.Contains(e) || e.AbilityId == a.id)).ToList()) ?? timeProblem ?? v2;
            return v2;
        }

        /// <summary>Resource cost, cooldown and turn Time of a use, predicted before and checked after.</summary>
        sealed class Bookkeeping
        {
            public float Cost, Time, Cast, Cooldown, TimeLeft, Debt;
            public ResourceType CostType;
            public AbilityDef A;
            public bool Check;

            public static Bookkeeping Before(Battle b, Unit c, AbilityDef a, Job j)
            {
                var mods = AbilityMods.For(c, a);
                var k = new Bookkeeping
                {
                    A = a, CostType = a.cost != null ? a.cost.type : ResourceType.None,
                    Cost = AbilityRules.ResourceCost(c, a, AbilityRules.UsedRank(c, a), mods),
                    Time = AbilityRules.TimeCost(c, a, mods), Cast = AbilityRules.CastTime(c, a, mods),
                    Cooldown = AbilityRules.Cooldown(c, a, mods), TimeLeft = c.TimeLeft, Debt = c.TimeDebt,
                    Check = !j.Opener && !a.autoAttack && !a.nextSwing,
                };
                return k;
            }

            /// <summary>Turn Time spent equals the ability's Time cost (or the cast became pending / the turn ended).</summary>
            public Result CheckTime(Battle b, Unit c)
            {
                if (!Check || !b.InCombat || !b.Started || b.IsOver || b.ActiveUnit != c || c.Pending != null) return null;
                float spent = (TimeLeft - c.TimeLeft) + (c.TimeDebt - Debt);
                if (c.TimeLeft <= 1e-3f && spent < Time) return null; // the cost overflowed into time debt
                bool special = !string.IsNullOrEmpty(A.special) || A.effects.Any(e => !string.IsNullOrEmpty(e.special));
                if (c.TimeLeft <= 1e-3f && special) return null;      // a special ends the turn on purpose (Feign Death)
                if (Math.Abs(spent - Time) > 0.05f) return Fail(Status.Partial, $"spent {spent:0.##} s of the turn, its Time cost is {Time:0.##} s (cast {Cast:0.##})");
                return null;
            }

            /// <summary>The predicted resource cost was paid (once) and the cooldown started.</summary>
            public Result CheckCostAndCooldown(Battle b, Unit c, List<CombatEvent> evs)
            {
                if (!Check) return null;
                if (Cost > 0f && CostType != ResourceType.None && !(A.cost != null && A.cost.consumeAll))
                {
                    float paid = -evs.Where(e => e.Type == CombatEventType.ResourceChange && e.Target == c && e.Resource == CostType && e.Amount < 0f && !e.Periodic)
                                     .Sum(e => e.Amount);
                    bool drains = A.effects.Any(e => e.type == EffectType.DrainResource || e.type == EffectType.Special);
                    if (paid + 0.6f < Cost || (!drains && paid > Cost + 0.6f))
                        return Fail(Status.Partial, $"paid {paid:0.#} {CostType}, its cost is {Cost:0.#}");
                }
                if (Cooldown > 0f && c.CooldownLeft(A) <= 1e-3f && !A.effects.Any(e => e.type == EffectType.ResetCooldowns))
                    return Fail(Status.Partial, $"no cooldown after the use (cooldown {Cooldown:0.#} s)");
                return null;
            }
        }

        static bool InCombatNow(Scene s) => s.B.InCombat && s.B.Started;

        /// <summary>
        /// Sap, Gouge, Polymorph, Scatter Shot...: the breakable control the ability just applied must not be broken by its own
        /// caster before the caster's next turn (auto attack swings at the end of the turn, on-hit procs...).
        /// </summary>
        static Result CheckControlSurvivesTurnEnd(Scene s, AbilityDef a, Unit c)
        {
            var b = s.B;
            var t = s.Target;
            if (t == null || t == c || !t.IsHostileTo(c) || !b.AppliesBreakableControl(a)) return null;
            var cc = a.effects.Where(e => e.type == EffectType.ApplyAura && e.target == EffectTarget.Target && Battle.IsBreakableControl(Db.Aura(e.aura)))
                              .Select(e => e.aura).ToList();
            if (!t.Auras.Any(x => cc.Contains(x.Def.id) && x.Caster == c)) return null; // resisted/immune: nothing to keep
            int mark = b.Events.Count;
            if (b.ActiveUnit == c) b.EndTurn(c);
            if (!b.IsOver) SkipTo(b, c, 80);
            var broken = b.Events.Skip(mark).FirstOrDefault(e => e.Type == CombatEventType.AuraBroken && cc.Contains(e.AuraId) && e.Target == t);
            if (broken == null) return null;
            var cause = b.Events.Skip(mark).TakeWhile(e => e != broken).LastOrDefault(e => e.Type == CombatEventType.Damage && e.Target == t);
            return Fail(Status.Partial, $"{broken.AuraId} was broken before the caster's next turn by {cause?.Source?.Name ?? "?"}'s {(cause == null ? "?" : cause.AutoAttack ? "auto attack" : cause.AbilityId.Length > 0 ? cause.AbilityId : "proc")}");
        }

        /// <summary>The hero places the totem/trap; the creature acts when the hero's turn ends.</summary>
        static Result AttemptTotem(Scene s)
        {
            var j = s.Job;
            var b = s.B;
            var hero = s.Hero;
            // the pulse's needs (damaged allies, dispellable/removable auras, a caster to interrupt...)
            ChooseTarget(s, hero, j.Placer);
            var pulseTarget = j.A.target == TargetType.Enemy ? s.Near : j.A.target == TargetType.Self ? null : s.Ally;
            PrepareNeeds(s, hero, j.A, pulseTarget ?? s.Ally);
            var r = UsePrepared(s, hero, j.Placer, out var placed);
            if (!r.Ok) return Fail(Status.Unusable, $"placer {j.Placer.id}: {r.Reason}");
            var totem = b.Units.FirstOrDefault(u => u.Creature == j.Creature && u.Owner == hero);
            if (totem == null) return Fail(Status.Nothing, $"{j.Placer.id} did not summon {j.Creature.id}");
            int mark = b.Events.Count;
            Func<CombatEvent, bool> fromTotem = e => e.Source != null && e.Source.Creature == j.Creature && e.Source.Owner == hero;
            Func<CombatEvent, bool> opens = e => e.AbilityId == j.A.id && fromTotem(e);
            Result last = null;
            for (int turn = 0; turn < 2; turn++)
            {
                if (b.IsOver) break;
                if (b.ActiveUnit != hero) SkipTo(b, hero, 80);
                if (b.ActiveUnit != hero) break;
                if (turn > 0) PrepareNeeds(s, hero, j.A, pulseTarget ?? s.Ally);
                b.EndTurn(hero);
                var evs = b.Events.Skip(mark).ToList();
                var inWindow = Window(evs, opens);
                last = Classify(s, j.A, evs, e => e.AbilityId == j.A.id || fromTotem(e) || inWindow.Contains(e), totem);
                if (last.Status == Status.Pass) return last;
            }
            return last ?? Fail(Status.Unresolved, "the hero's turn did not come back");
        }

        /// <summary>Events caused by a use: from its AbilityUsed/CastStart until the next action, turn or round boundary.</summary>
        static HashSet<CombatEvent> Window(List<CombatEvent> evs, Func<CombatEvent, bool> opens)
        {
            var set = new HashSet<CombatEvent>();
            bool open = false;
            foreach (var e in evs)
            {
                bool boundary = e.Type == CombatEventType.AbilityUsed || e.Type == CombatEventType.CastStart || e.Type == CombatEventType.TurnStart ||
                                e.Type == CombatEventType.TurnEnd || e.Type == CombatEventType.RoundStart;
                if (boundary) open = opens(e);
                if (open) set.Add(e);
            }
            return set;
        }

        /// <summary>Hidden abilities that only run as part of another one (judgements, procs): TriggerAbility.</summary>
        static Result AttemptTriggered(Scene s)
        {
            var b = s.B;
            var a = s.Job.A;
            Prepare(s, s.Hero, a);
            int cursor = b.Events.Count;
            b.TriggerAbility(s.Hero, a, s.Target, s.Point);
            return Classify(s, a, b.Events.Skip(cursor).ToList(), null, s.Hero);
        }

        /// <summary>Prepares and uses a helper ability (placer / pre-use) with its own target.</summary>
        static ActionResult UsePrepared(Scene s, Unit c, AbilityDef a, out Unit target)
        {
            var keepT = s.Target; var keepP = s.Point;
            Prepare(s, c, a);
            target = s.Target;
            var r = s.B.UseAbility(c, a.id, s.Target, s.Point);
            s.Target = keepT; s.Point = keepP;
            return r;
        }

        // ------------------------------------------------------------------------------------------- classify

        static readonly HashSet<CombatEventType> PositiveTypes = new HashSet<CombatEventType>
        {
            CombatEventType.Damage, CombatEventType.Heal, CombatEventType.Absorb, CombatEventType.AuraApplied, CombatEventType.AuraRefreshed,
            CombatEventType.AuraStack, CombatEventType.Dispel, CombatEventType.ComboPoints, CombatEventType.Teleport, CombatEventType.Charge,
            CombatEventType.Knockback, CombatEventType.Summon, CombatEventType.Despawn, CombatEventType.Death, CombatEventType.Downed,
            CombatEventType.Revive, CombatEventType.Threat, CombatEventType.Taunt, CombatEventType.ItemCreated, CombatEventType.CooldownReset,
            CombatEventType.CastInterrupted,
        };

        static readonly HashSet<CombatEventType> AvoidTypes = new HashSet<CombatEventType>
        {
            CombatEventType.Miss, CombatEventType.Dodge, CombatEventType.Parry, CombatEventType.Block, CombatEventType.Resist,
            CombatEventType.Immune, CombatEventType.Evade,
        };

        static Result Classify(Scene s, AbilityDef a, List<CombatEvent> events, Func<CombatEvent, bool> filter, Unit actor)
        {
            var c = actor ?? s.Caster;
            bool positive = false, avoided = false;
            var seen = new List<string>();
            var mine = new List<CombatEvent>();
            foreach (var e in events)
            {
                if (filter != null && !filter(e)) continue;
                mine.Add(e);
                seen.Add(e.Type.ToString());
                if (PositiveTypes.Contains(e.Type)) positive = true;
                else if (AvoidTypes.Contains(e.Type)) avoided = true;
                else if (e.Type == CombatEventType.AuraRemoved || e.Type == CombatEventType.AuraBroken)
                {
                    if (e.Target != c) positive = true; // not just the caster's own stealth breaking
                }
                else if (e.Type == CombatEventType.ResourceChange)
                {
                    bool cost = e.Target == c && e.Amount < 0f && !e.Periodic;
                    bool regen = e.Periodic && e.Source == e.Target;
                    if (!cost && !regen) positive = true;
                }
            }
            if (s.FieldEvents.Count > 0) positive = true;
            string list = string.Join(",", seen.Distinct().Take(12));
            if (!positive)
            {
                if (avoided) return Fail(Status.Avoided, "only avoided [" + list + "]");
                return Fail(Status.Nothing, "no meaningful event [" + list + "]");
            }
            if (Magnitudes) ReportMagnitudes(s, a, mine, c);
            // one cast, one target, one hit roll: it cannot both land and be avoided (unless it ticks or channels)
            if (!a.channeled && !a.autoAttack)
                foreach (var hit in mine.Where(x => x.Type == CombatEventType.Damage && x.AbilityId == a.id && !x.Periodic && !x.AutoAttack && x.Target != c))
                {
                    var avoid = mine.FirstOrDefault(x => (x.Type == CombatEventType.Dodge || x.Type == CombatEventType.Parry || x.Type == CombatEventType.Miss ||
                                                          x.Type == CombatEventType.Resist) && x.AbilityId == a.id && x.Target == hit.Target);
                    if (avoid != null) return Fail(Status.Split, $"split outcome on {hit.Target.Name}: '{hit.Text}' and '{avoid.Text}' (an effect without cannotMiss rolled again)");
                }
            var missing = MissingEffects(a, mine, c, s.Target, s.FieldEvents, !s.Job.Field);
            if (missing.Count == 0) return new Result { Status = Status.Pass, Text = Verbose ? "[" + list + "]" : "" };
            if (avoided) return Fail(Status.Avoided, "partly avoided, no evidence of " + string.Join(", ", missing) + " [" + list + "]");
            return Fail(Status.Partial, "no evidence of " + string.Join(", ", missing) + " [" + list + "]");
        }

        /// <summary>
        /// Effects of the ability that left no trace in the events (an aura that was never applied, damage never dealt...).
        /// Random (chance &lt; 100), conditional (requireTargetAura) and special-handled effects are not checked.
        /// </summary>
        static List<string> MissingEffects(AbilityDef a, List<CombatEvent> evs, Unit c, Unit target, List<string> fieldEvents, bool combat)
        {
            // Holy Shock-like abilities: damage only lands on enemies and heals only on allies
            bool friendlyTarget = target != null && !target.IsHostileTo(c);
            bool hostileTarget = target != null && target.IsHostileTo(c);
            var miss = new List<string>();
            bool From(CombatEvent e) => e.Source == c || (e.Source != null && (e.Source.Owner == c || e.Source == c.Owner));
            bool Has(Func<CombatEvent, bool> p) => evs.Any(p);
            for (int i = 0; i < a.effects.Count; i++)
            {
                var e = a.effects[i];
                if (e.chance < 100f || !string.IsNullOrEmpty(e.requireTargetAura)) continue;
                bool onTarget = e.target == EffectTarget.Target && a.area.shape == AreaShape.None;
                if (onTarget && friendlyTarget && (e.type == EffectType.Damage || e.type == EffectType.WeaponDamage)) continue;
                if (onTarget && hostileTarget && e.type == EffectType.Heal) continue;
                bool ok;
                switch (e.type)
                {
                    case EffectType.Damage:
                    case EffectType.WeaponDamage:
                        ok = Has(x => (x.Type == CombatEventType.Damage || x.Type == CombatEventType.Absorb || x.Type == CombatEventType.Immune) && x.AbilityId == a.id);
                        break;
                    case EffectType.Heal:
                        ok = Has(x => x.Type == CombatEventType.Heal && x.AbilityId == a.id);
                        break;
                    case EffectType.ApplyAura:
                        ok = Has(x => (x.Type == CombatEventType.AuraApplied || x.Type == CombatEventType.AuraRefreshed || x.Type == CombatEventType.AuraStack ||
                                       x.Type == CombatEventType.Immune || x.Type == CombatEventType.Resist) && x.AuraId == e.aura);
                        break;
                    case EffectType.RemoveAura:
                        ok = Has(x => (x.Type == CombatEventType.AuraRemoved || x.Type == CombatEventType.AuraBroken) && AuraMatches(x.AuraId, e));
                        break;
                    case EffectType.Dispel: ok = Has(x => x.Type == CombatEventType.Dispel && From(x)); break;
                    case EffectType.Interrupt: ok = Has(x => x.Type == CombatEventType.CastInterrupted || (x.Type == CombatEventType.Resist && From(x))); break;
                    case EffectType.Taunt: ok = Has(x => x.Type == CombatEventType.Taunt && From(x)); break;
                    case EffectType.Threat: ok = Has(x => x.Type == CombatEventType.Threat && From(x)); break;
                    case EffectType.GainResource:
                        ok = Has(x => (x.Type == CombatEventType.ResourceChange && x.Amount > 0f && !(x.Periodic && x.Source == x.Target)) ||
                                      x.Type == CombatEventType.ComboPoints || (x.Type == CombatEventType.Heal && x.AbilityId == a.id) ||
                                      (x.Type == CombatEventType.Damage && x.AbilityId == a.id && x.Reason == "self"));
                        break;
                    case EffectType.DrainResource:
                        ok = Has(x => (x.Type == CombatEventType.ResourceChange && x.Amount < 0f && !(x.Target == c && x.Source == c && !x.Periodic && e.target != EffectTarget.Self)) ||
                                      (x.Type == CombatEventType.Damage && x.AbilityId == a.id));
                        break;
                    case EffectType.Teleport: ok = Has(x => x.Type == CombatEventType.Teleport && x.Source == c); break;
                    case EffectType.Charge: ok = Has(x => x.Type == CombatEventType.Charge && x.Source == c); break;
                    case EffectType.Knockback: ok = Has(x => x.Type == CombatEventType.Knockback && x.Source == c); break;
                    case EffectType.Summon:
                    case EffectType.SummonTotem: ok = Has(x => x.Type == CombatEventType.Summon && From(x)); break;
                    case EffectType.Resurrect: ok = Has(x => x.Type == CombatEventType.Revive); break;
                    case EffectType.CreateItem: ok = Has(x => x.Type == CombatEventType.ItemCreated); break;
                    case EffectType.ResetCooldowns: ok = Has(x => x.Type == CombatEventType.CooldownReset); break;
                    case EffectType.Kill: ok = Has(x => x.Type == CombatEventType.Death || x.Type == CombatEventType.Despawn || x.Type == CombatEventType.Downed); break;
                    case EffectType.TriggerAbility: ok = Has(x => x.AbilityId == e.ability) || Has(x => PositiveTypes.Contains(x.Type)); break;
                    case EffectType.Special: ok = SpecialEvidence(e.special, a, evs, c, fieldEvents, combat); break;
                    default: ok = true; break;
                }
                if (!ok) miss.Add($"#{i} {e.type}{(string.IsNullOrEmpty(e.aura) ? "" : ":" + e.aura)}{(string.IsNullOrEmpty(e.auraTag) ? "" : ":" + e.auraTag)}({e.target})");
            }
            return miss;
        }

        /// <summary>What each effect-level special handler visibly does (unknown handlers are not checked).</summary>
        static bool SpecialEvidence(string name, AbilityDef a, List<CombatEvent> evs, Unit c, List<string> fieldEvents, bool combat)
        {
            bool Has(Func<CombatEvent, bool> p) => evs.Any(p);
            switch (name)
            {
                case "HunterDismissPet": return Has(x => x.Type == CombatEventType.Despawn);
                case "HunterRevivePet":
                case "HunterTameBeast": return Has(x => x.Type == CombatEventType.Summon);
                case "MageEvocation":
                case "WarlockLifeTap": return Has(x => x.Type == CombatEventType.ResourceChange && x.Target == c && x.Resource == ResourceType.Mana && x.Amount > 0f);
                case "MageBlinkFreedom": return Has(x => x.Type == CombatEventType.AuraRemoved && x.Target == c);
                case "PaladinJudgement":
                    return Has(x => x.Type == CombatEventType.AuraRemoved && Db.Aura(x.AuraId) != null && Db.Aura(x.AuraId).tags.Contains("Seal")) &&
                           Has(x => x.Type == CombatEventType.Damage || x.Type == CombatEventType.AuraApplied || AvoidTypes.Contains(x.Type));
                case "PaladinLayOnHands": return Has(x => x.Type == CombatEventType.Heal && x.AbilityId == a.id);
                case "PaladinDivineIntervention": return !combat || Has(x => x.Type == CombatEventType.Death && x.Target == c); // the paladin only dies in combat
                case "PriestManaBurn":
                    return Has(x => (x.Type == CombatEventType.Damage || x.Type == CombatEventType.Absorb) && x.AbilityId == a.id) &&
                           Has(x => x.Type == CombatEventType.ResourceChange && x.Resource == ResourceType.Mana && x.Amount < 0f && x.Target != c);
                case "HelpUp": return Has(x => x.Type == CombatEventType.Revive);
                case "WarlockDemonicSacrifice": return Has(x => x.Type == CombatEventType.Despawn) && Has(x => x.Type == CombatEventType.AuraApplied && x.Target == c);
                case "RoguePickLock": return fieldEvents.Contains(name);
                default: return true;
            }
        }

        static readonly bool Magnitudes = Environment.GetEnvironmentVariable("SMOKE_MAGNITUDES") == "1";

        /// <summary>SMOKE_MAGNITUDES=1: dealt/healed amount of the first direct damage/heal vs the tooltip's predicted range.</summary>
        static void ReportMagnitudes(Scene s, AbilityDef a, List<CombatEvent> evs, Unit c)
        {
            var mods = AbilityMods.For(c, a);
            int eff = AbilityRules.EffLevel(c, a, AbilityRules.UsedRank(c, a));
            foreach (var e in a.effects)
            {
                if (e.type != EffectType.Damage && e.type != EffectType.Heal && e.type != EffectType.WeaponDamage) continue;
                var ev = evs.FirstOrDefault(x => (e.type == EffectType.Heal ? x.Type == CombatEventType.Heal : x.Type == CombatEventType.Damage) && x.AbilityId == a.id && !x.Periodic);
                if (ev == null) continue;
                var txt = Tooltip.Magnitude(c, a, e, eff, mods);
                var nums = System.Text.RegularExpressions.Regex.Matches(txt, @"[0-9]+(\.[0-9]+)?").Cast<System.Text.RegularExpressions.Match>().Select(m => float.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
                if (nums.Count == 0) continue;
                float lo = nums[0], hi = nums.Count > 1 ? nums[1] : nums[0];
                float got = ev.Amount + ev.Overheal + ev.Absorbed + ev.Resisted + ev.Blocked;
                float ratio = got / Math.Max(1f, (lo + hi) / 2f);
                Console.WriteLine($"      MAG {s.Job.Key,-50} {e.type,-12} got {got,7:0} tooltip {txt,-22} ratio {ratio:0.00}{(ev.Crit ? " crit" : "")}");
                break;
            }
        }

        static bool AuraMatches(string auraId, EffectDef e)
        {
            if (!string.IsNullOrEmpty(e.aura)) return auraId == e.aura;
            var d = Db.Aura(auraId);
            if (d == null) return false;
            return d.tags.Contains(e.auraTag) || (Enum.TryParse<UnitState>(e.auraTag, true, out var st) && d.states.Contains(st));
        }

        // ------------------------------------------------------------------------------------------- prepare

        /// <summary>Target/point, requirements, implicit needs, resources and cooldowns for <paramref name="c"/> using <paramref name="a"/>.</summary>
        static void Prepare(Scene s, Unit c, AbilityDef a)
        {
            var b = s.B;
            var r = a.requires ?? new RequirementDef();
            // equipment first (stats)
            if (c.Class != null)
            {
                if (r.mainHand.Length > 0 && Enum.TryParse<WeaponType>(r.mainHand[0], true, out var wt))
                {
                    var mh = c.Equipment.MainHand;
                    if (mh == null || !r.mainHand.Any(x => string.Equals(x, mh.Def.weaponType.ToString(), StringComparison.OrdinalIgnoreCase)))
                        EquipWeapon(c, d => d.weaponType == wt, EquipSlot.MainHand);
                }
                if (r.shield && !c.Equipment.HasShield) EquipWeapon(c, d => d.weaponType == WeaponType.Shield, EquipSlot.OffHand);
                if (r.dualWield && !c.Equipment.IsDualWielding) EquipWeapon(c, d => d.equip == EquipType.OneHand, EquipSlot.OffHand);
                if ((r.meleeWeapon || r.shield || r.mainHand.Length > 0) && !c.Equipment.HasMeleeWeapon)
                    EquipWeapon(c, d => d.equip == EquipType.OneHand || d.equip == EquipType.MainHand, EquipSlot.MainHand);
                if (r.rangedWeapon || a.autoAttack && AbilityRules.IsRangedWeaponAbility(a)) EquipRanged(c, a);
            }
            // pets
            if (r.hasPet || a.target == TargetType.Pet || a.effects.Any(e => e.target == EffectTarget.Pet)) EnsurePet(s, c);
            if (r.noPet && c.Pet != null) b.DismissPet(c);
            if (s.Job.Variant == "deadpet" && c == s.Hero) KillPet(s, c);
            // Blink frees from roots: root the mage first
            if (Specials.UsesSpecial(a, "MageBlinkFreedom") && !c.HasStateAura(UnitState.Root))
                GiveAura(s, HostileOf(s, c), c, Db.Auras.Values.OrderBy(x => x.id, StringComparer.Ordinal)
                    .FirstOrDefault(x => x.kind == AuraKind.Debuff && x.states.Length == 1 && x.states[0] == UnitState.Root && string.IsNullOrEmpty(x.special)));
            // caster auras / states
            if (r.casterAuras.Length > 0 && !r.casterAuras.Any(id => c.HasAura(id))) GiveAura(s, c, c, Db.Aura(r.casterAuras[0]));
            if (r.casterAuraTags.Length > 0 && !r.casterAuraTags.Any(t => c.HasAuraWithTag(t)))
            {
                AuraDef pick = null;
                if (s.Job.Variant.StartsWith("aura:", StringComparison.Ordinal)) pick = Db.Aura(s.Job.Variant.Substring(5));
                pick ??= ClassAurasWithTag(c.ClassId, r.casterAuraTags).FirstOrDefault();
                GiveAura(s, c, c, pick);
            }
            foreach (var st in r.casterStates)
            {
                if (!Enum.TryParse<UnitState>(st, true, out var state) || c.HasStateAura(state)) continue;
                GiveAura(s, c, c, AuraWithState(c.ClassId, state));
            }
            ChooseTarget(s, c, a);
            var t = s.Target;
            if (t != null && t != c)
            {
                if (r.targetAuras.Length > 0 && !r.targetAuras.Any(id => t.HasAura(id, r.targetAuraFromSelf ? c : null)))
                    GiveAura(s, c, t, Db.Aura(r.targetAuras[0]));
                if (r.targetAuraTags.Length > 0 && !r.targetAuraTags.Any(tag => t.HasAuraWithTag(tag, r.targetAuraFromSelf ? c : null)))
                    GiveAura(s, c, t, Db.Auras.Values.OrderBy(x => x.id, StringComparer.Ordinal).FirstOrDefault(x => r.targetAuraTags.Any(tag => x.tags.Contains(tag))));
                if (r.behindTarget) t.Facing = (t.Position - c.Position).Normalized;
                if (r.targetHealthBelowPct > 0) t.Health = t.MaxHealth * r.targetHealthBelowPct * 0.5f / 100f;
            }
            int needCp = Math.Max(r.minComboPoints, a.cost != null && a.cost.consumesComboPoints ? 1 : 0);
            if (needCp > 0) b.AddComboPoints(c, t != null && t.IsHostileTo(c) ? t : s.Near, 5);
            if (!string.IsNullOrEmpty(r.reactive))
            {
                c.Reactive[r.reactive] = c.TurnsTaken + 1;
                if (r.reactive == "SelfDodgedParriedBlocked") c.Reactive["SelfDodged"] = c.TurnsTaken + 1;
            }
            PrepareNeeds(s, c, a, t);
            Refill(c, a);
            if (r.minHealthPct > 0 || (a.cost != null && a.cost.health > 0)) c.Health = c.MaxHealth;
            // cooldown resets need something on cooldown
            foreach (var e in a.effects)
            {
                if (e.type != EffectType.ResetCooldowns) continue;
                foreach (var kv in c.Abilities)
                {
                    var o = Db.Ability(kv.Key);
                    if (o == null || o == a || o.cooldown <= 0f) continue;
                    bool all = e.abilities.Length == 0 && e.tags.Length == 0 && e.schools.Length == 0;
                    if (all || e.abilities.Contains(o.id) || e.tags.Any(tg => AbilityMods.HasTag(o, tg)) || e.schools.Contains(o.school))
                        c.Cooldowns[o.id] = o.cooldown;
                }
            }
            // an aura the ability puts on its caster that is already there (stances, forms): switch away first
            foreach (var e in a.effects)
            {
                if (e.type != EffectType.ApplyAura || (e.target != EffectTarget.Self && !(e.target == EffectTarget.Target && a.target == TargetType.Self))) continue;
                var have = c.FindAura(e.aura);
                if (have == null || have.IsPassive) continue;
                var other = string.IsNullOrEmpty(have.Def.exclusiveGroup) ? null
                    : ClassAurasInGroup(c.ClassId, have.Def.exclusiveGroup).FirstOrDefault(x => x.id != have.Def.id);
                if (other != null) GiveAura(s, c, c, other);
                else s.B.RemoveAura(have, AuraRemoveReason.Cancelled);
            }
        }

        static List<AuraDef> ClassAurasInGroup(ClassId cls, string group)
        {
            var list = new List<AuraDef>();
            foreach (var ab in SortedAbilities())
            {
                if (ab.classId != cls) continue;
                foreach (var e in ab.effects)
                {
                    var au = e.type == EffectType.ApplyAura ? Db.Aura(e.aura) : null;
                    if (au != null && au.exclusiveGroup == group && !list.Contains(au)) list.Add(au);
                }
            }
            return list;
        }

        static void ChooseTarget(Scene s, Unit c, AbilityDef a)
        {
            s.Point = null;
            switch (a.target)
            {
                case TargetType.Self: s.Target = c; break;
                case TargetType.Pet: EnsurePet(s, c); s.Target = c.Pet; break;
                case TargetType.Ally: s.Target = s.Ally ?? c; break;
                case TargetType.AllyOther:
                    s.Target = a.effects.Any(e => e.special == "HelpUp") ? s.Downed : s.Ally;
                    if (s.Target == s.Downed && s.Downed != null) PlaceNextTo(s, s.Downed, c);
                    break;
                case TargetType.DeadAlly: s.Target = s.Downed; break;
                case TargetType.Any: s.Target = s.Job.Variant == "ally" ? s.Ally : Hostile(s, a, c); break;
                case TargetType.Enemy: s.Target = Hostile(s, a, c); break;
                case TargetType.Point:
                    s.Target = null;
                    s.Point = (s.Near ?? s.Ally ?? c).Position;
                    break;
            }
        }

        static void PlaceNextTo(Scene s, Unit who, Unit c)
        {
            var p = c.Position + new Vec2(-1.2f, -0.6f);
            if (s.B.Started) s.B.Teleport(who, p); else who.Position = p;
        }

        /// <summary>The first hostile (melee, 3rd, far; mana users first for mana drains) inside the ability's range band.</summary>
        static Unit Hostile(Scene s, AbilityDef a, Unit c)
        {
            if (s.Near == null) return null;
            var r = a.requires ?? new RequirementDef();
            var order = NeedsManaTarget(a) ? new[] { s.Third, s.Near, s.Far } : new[] { s.Near, s.Third, s.Far };
            var mods = AbilityMods.For(c, a);
            foreach (var h in order)
            {
                float d = c.DistanceTo(h);
                if (d > AbilityRules.RangeMetres(c, a, h, mods, Db.Config) + 1e-3f) continue;
                if (d < AbilityRules.MinRangeMetres(a)) continue;
                if (r.outOfMeleeRange && d < MathUtil.Yd(8f) + h.Radius) continue;
                return h;
            }
            return order[0];
        }

        static bool NeedsManaTarget(AbilityDef a)
        {
            foreach (var e in a.effects)
            {
                if (e.type == EffectType.DrainResource && string.Equals(e.resource, "Mana", StringComparison.OrdinalIgnoreCase) && e.target == EffectTarget.Target) return true;
                if (e.type == EffectType.Special && e.special.IndexOf("ManaBurn", StringComparison.Ordinal) >= 0) return true;
                if (e.type == EffectType.ApplyAura)
                {
                    var au = Db.Aura(e.aura);
                    if (au != null && au.tickEffects.Any(x => x.type == EffectType.DrainResource && string.Equals(x.resource, "Mana", StringComparison.OrdinalIgnoreCase))) return true;
                }
            }
            return false;
        }

        /// <summary>Interrupt targets cast, dispels/removals find something, heals/resource gains find a deficit.</summary>
        static void PrepareNeeds(Scene s, Unit c, AbilityDef a, Unit t)
        {
            foreach (var e in a.effects)
            {
                foreach (var rcp in Recipients(s, a, e, c, t))
                {
                    if (rcp == null || !rcp.IsAlive) continue;
                    switch (e.type)
                    {
                        case EffectType.Interrupt:
                            if (rcp.IsHostileTo(c) && rcp.Pending == null) MakeCasting(s, rcp, c);
                            break;
                        case EffectType.Dispel:
                        {
                            bool hostile = rcp.IsHostileTo(c);
                            var dt = e.dispelType == DispelType.None ? DispelType.Magic : e.dispelType;
                            var def = PickAura(hostile ? AuraKind.Buff : AuraKind.Debuff, dt, null);
                            if (def != null && !rcp.Auras.Any(x => x.Def.dispel == dt && x.Def.kind == def.kind))
                                GiveAura(s, hostile ? rcp : HostileOf(s, rcp), rcp, def);
                            break;
                        }
                        case EffectType.RemoveAura:
                        {
                            AuraDef def = !string.IsNullOrEmpty(e.aura) ? Db.Aura(e.aura) : PickAura(null, null, e.auraTag, rcp.IsHostileTo(c) ? AuraKind.Buff : AuraKind.Debuff);
                            if (def == null || rcp.HasAura(def.id)) break;
                            if (rcp == c && def.states.Length > 0) break; // never control the caster itself
                            GiveAura(s, def.kind == AuraKind.Buff ? rcp : HostileOf(s, rcp), rcp, def);
                            break;
                        }
                        case EffectType.Heal:
                            if (rcp.Health >= rcp.MaxHealth * 0.8f) rcp.Health = rcp.MaxHealth * 0.6f;
                            break;
                        case EffectType.GainResource:
                            if (rcp != c && rcp.MaxMana > 0 && string.Equals(e.resource, "Mana", StringComparison.OrdinalIgnoreCase)) rcp.Mana = rcp.MaxMana * 0.4f;
                            break;
                    }
                }
            }
        }

        static IEnumerable<Unit> Recipients(Scene s, AbilityDef a, EffectDef e, Unit c, Unit t)
        {
            switch (e.target)
            {
                case EffectTarget.Self: yield return c; break;
                case EffectTarget.Pet: yield return c.Pet; break;
                case EffectTarget.Owner: yield return c.Owner; break;
                case EffectTarget.AlliesInRadius:
                case EffectTarget.Party: yield return s.Ally; break;
                case EffectTarget.EnemiesInRadius: yield return s.Near; break;
                default:
                    if (a.area.shape != AreaShape.None)
                    {
                        if (a.area.affects == AreaAffects.Allies) yield return s.Ally;
                        else yield return t != null && t.IsHostileTo(c) ? t : s.Near;
                    }
                    else yield return t;
                    break;
            }
        }

        static Unit HostileOf(Scene s, Unit u)
        {
            foreach (var o in new[] { s.Near, s.Far, s.Third, s.Hero, s.Caster, s.Ally })
                if (o != null && o.IsAlive && o.IsHostileTo(u)) return o;
            return u;
        }

        static void MakeCasting(Scene s, Unit who, Unit victim)
        {
            var spell = SortedAbilities().First(x => x.castTime >= 2f && !x.channeled && x.school != School.Physical && x.target == TargetType.Enemy && !x.hidden);
            who.Pending = new PendingCast { Ability = spell, Rank = 1, Target = victim, RemainingTime = 2f, StartRound = s.B.Round };
        }

        /// <summary>A plain aura (no control state, no special) of the kind and dispel type, or matching a tag/state name.</summary>
        static AuraDef PickAura(AuraKind? kind, DispelType? dispel, string tag, AuraKind? preferKind = null)
        {
            IEnumerable<AuraDef> all = Db.Auras.Values.OrderBy(x => x.id, StringComparer.Ordinal);
            if (tag != null)
            {
                var matches = all.Where(x => x.tags.Contains(tag) || (Enum.TryParse<UnitState>(tag, true, out var st) && x.states.Contains(st))).ToList();
                return matches.FirstOrDefault(x => preferKind == null || x.kind == preferKind.Value) ?? matches.FirstOrDefault();
            }
            return all.FirstOrDefault(x => (kind == null || x.kind == kind.Value) && (dispel == null || x.dispel == dispel.Value) &&
                                           x.states.Length == 0 && string.IsNullOrEmpty(x.special) && !x.hidden && x.radius <= 0f &&
                                           x.duration > 0f && x.procs.Count == 0 && x.absorb == null);
        }

        /// <summary>Auras with one of the tags that the class's own abilities apply (seals for Judgement...).</summary>
        static List<AuraDef> ClassAurasWithTag(ClassId cls, string[] tags)
        {
            var list = new List<AuraDef>();
            foreach (var ab in SortedAbilities())
            {
                if (ab.classId != cls) continue;
                foreach (var e in ab.effects)
                {
                    if (e.type != EffectType.ApplyAura) continue;
                    var au = Db.Aura(e.aura);
                    if (au != null && tags.Any(t => au.tags.Contains(t)) && !list.Contains(au)) list.Add(au);
                }
            }
            return list;
        }

        static AuraDef AuraWithState(ClassId cls, UnitState st)
        {
            foreach (var ab in SortedAbilities())
            {
                if (ab.classId != cls) continue;
                foreach (var e in ab.effects)
                {
                    var au = e.type == EffectType.ApplyAura ? Db.Aura(e.aura) : null;
                    if (au != null && au.states.Contains(st)) return au;
                }
            }
            return Db.Auras.Values.OrderBy(x => x.id, StringComparer.Ordinal).FirstOrDefault(x => x.states.Contains(st));
        }

        /// <summary>The ability (of the caster's class when possible) that applies the aura, for scaling.</summary>
        static AbilityDef ApplierOf(AuraDef def, ClassId cls)
        {
            AbilityDef any = null;
            foreach (var ab in SortedAbilities())
                foreach (var e in ab.effects)
                    if (e.type == EffectType.ApplyAura && e.aura == def.id)
                    {
                        if (ab.classId == cls) return ab;
                        any ??= ab;
                    }
            return any;
        }

        static void GiveAura(Scene s, Unit caster, Unit target, AuraDef def)
        {
            if (def == null || target == null) return;
            var src = ApplierOf(def, caster.ClassId);
            int rank = src != null ? Math.Max(1, caster.RankOf(src.id)) : 1;
            var info = new AuraApplyInfo
            {
                Source = src, Rank = rank, EffLevel = src != null ? AbilityRules.EffLevel(caster, src, rank) : caster.Level,
                LearnLevel = src != null ? src.learnLevel : 1, Duration = def.duration > 0 ? Math.Max(def.duration, 12f) : 0f,
            };
            if (s.B.ApplyAura(caster, target, def, info) == null && !target.HasAura(def.id))
                UnitFactory.AttachAura(target, def, caster, false, rank, info.EffLevel, info.LearnLevel, def.duration > 0 ? 30f : -1f);
        }

        static void EnsurePet(Scene s, Unit owner)
        {
            if (owner.Pet != null && owner.Pet.IsAlive && s.B.Units.Contains(owner.Pet)) return;
            string id = owner.ClassId == ClassId.Hunter ? (owner.HunterPet?.TemplateId ?? "hunter_pet_wolf")
                      : owner.ClassId == ClassId.Warlock ? "warlock_demon_voidwalker" : null;
            if (id == null || Db.Creature(id) == null) return;
            var at = owner.Position + new Vec2(-1.0f, -1.3f);
            if (owner.ClassId == ClassId.Hunter) HunterPets.State(owner, id);
            var pet = s.B.SummonUnit(owner, Db.Creature(id), UnitKind.Pet, -1f, at);
            pet.Health = pet.MaxHealth * 0.6f;
        }

        static void KillPet(Scene s, Unit owner)
        {
            EnsurePet(s, owner);
            var pet = owner.Pet;
            if (pet == null || !pet.IsAlive) return;
            s.B.DealDamage(HostileOf(s, pet), pet, pet.Health + 10f, School.Physical,
                           new DamageInfo { Name = "smoke", IgnoreArmor = true, IgnoreAbsorb = true, IgnoreModifiers = true });
        }

        static void Refill(Unit c, AbilityDef a)
        {
            c.Cooldowns.Clear();
            c.Lockouts.Clear();
            var mods = AbilityMods.For(c, a);
            float cost = AbilityRules.ResourceCost(c, a, AbilityRules.UsedRank(c, a), mods);
            var ct = a.cost != null ? a.cost.type : ResourceType.None;
            if (c.MaxMana > 0) c.Mana = Math.Min(c.MaxMana, Math.Max(ct == ResourceType.Mana ? cost + 1f : 0f, c.MaxMana * 0.6f));
            c.Rage = Math.Min(100f, Math.Max(ct == ResourceType.Rage ? cost + 30f : 0f, 40f));
            float maxE = c.MaxResource(ResourceType.Energy);
            c.Energy = Math.Min(maxE, Math.Max(ct == ResourceType.Energy ? cost : 0f, 60f));
            c.Focus = c.MaxResource(ResourceType.Focus);
            c.Health = c.MaxHealth * 0.7f;
        }

        // ------------------------------------------------------------------------------------------- equipment

        static bool EquipWeapon(Unit u, Func<ItemDef, bool> pred, EquipSlot slot)
        {
            foreach (var d in Db.Items.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                if (d.weaponType == WeaponType.None || !pred(d)) continue;
                // plain weapons only: a proc, epic or legendary weapon (expansion content) would add hits these tests do not expect
                if (d.equipEffects.Count > 0 || d.quality >= Quality.Epic) continue;
                if (EquipmentRules.CannotEquipReason(u, d, slot) != null) continue;
                EquipmentRules.Equip(u, new ItemInstance(d), slot);
                if (slot == EquipSlot.OffHand && !u.Equipment.HasMeleeWeapon)
                    EquipWeapon(u, x => x.equip == EquipType.OneHand || x.equip == EquipType.MainHand, EquipSlot.MainHand);
                u.InvalidateStats();
                return true;
            }
            return false;
        }

        static void EquipRanged(Unit u, AbilityDef a)
        {
            WeaponType[] want;
            if (a.special == "Shoot") want = new[] { WeaponType.Wand };
            else if (AbilityMods.HasTag(a, "Thrown")) want = new[] { WeaponType.Thrown };
            else if (AbilityMods.HasTag(a, "Shoot") || u.ClassId == ClassId.Hunter) want = new[] { WeaponType.Bow, WeaponType.Gun, WeaponType.Crossbow };
            else want = new[] { WeaponType.Bow, WeaponType.Gun, WeaponType.Crossbow, WeaponType.Thrown, WeaponType.Wand };
            var cur = u.Equipment.Ranged;
            if (cur != null && want.Contains(cur.Def.weaponType)) return;
            foreach (var w in want)
                if (EquipWeapon(u, d => d.weaponType == w, EquipSlot.Ranged)) return;
        }

        // ------------------------------------------------------------------------------------------- data helpers

        /// <summary>Class owning a creature (pets/demons/totems/traps/Lightwell): any of its abilities or a summon by a class ability.</summary>
        static ClassId OwnerClassOf(CreatureDef cr)
        {
            foreach (var ca in cr.abilities)
            {
                var a = Db.Ability(ca.ability);
                if (a != null && a.classId != ClassId.None) return a.classId;
            }
            foreach (var a in Db.Abilities.Values)
                foreach (var e in a.effects)
                    if ((e.type == EffectType.Summon || e.type == EffectType.SummonTotem) && e.summon == cr.id && a.classId != ClassId.None) return a.classId;
            return ClassId.None;
        }

        static AbilityDef PlacerOf(ClassId cls, string creatureId)
        {
            foreach (var a in SortedAbilities())
            {
                if (a.classId != cls || a.hidden) continue;
                foreach (var e in a.effects)
                    if ((e.type == EffectType.SummonTotem || e.type == EffectType.Summon) && e.summon == creatureId) return a;
            }
            return null;
        }

        /// <summary>Who uses an item in the test: its class restriction, else a class that benefits from it.</summary>
        static ClassId ItemUser(ItemDef it, AbilityDef use)
        {
            if (it.classes != null && it.classes.Length > 0) return it.classes[0];
            if (use.classId != ClassId.None) return use.classId;
            foreach (var e in use.effects)
            {
                if (e.type == EffectType.GainResource && string.Equals(e.resource, "Rage", StringComparison.OrdinalIgnoreCase)) return ClassId.Warrior;
                if (e.type == EffectType.GainResource && string.Equals(e.resource, "Energy", StringComparison.OrdinalIgnoreCase)) return ClassId.Rogue;
            }
            return ClassId.Paladin; // mana, health and a melee weapon
        }
    }
}
