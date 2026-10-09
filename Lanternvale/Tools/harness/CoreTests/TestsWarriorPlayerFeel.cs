// The player's report "for warrior, when I'm playing I can't use any of the abilities like Heroic Strike or Rend to attack
// the target". Every warrior attack costs rage, fights start at 0 rage, and rage from hitting came only with the white
// swings at the END of the turn — so turn 1 could never use a rage ability, and a player who pressed Rend saw "Not enough
// rage" and nothing else. The fixes these tests pin down:
//   * the opening swing (Battle.OpeningSwing): the first melee swing of a battle lands as the unit engages, taken from
//     the turn's swing time (same number of swings), so its rage arrives in time for a strike in the same turn;
//   * a queued Heroic Strike / Cleave holds its rage (CheckUse) and, when its swing still cannot pay, fails visibly;
//   * the refusal says what to do ("Attack an enemy to build rage.") and the controller attacks the enemy first instead
//     of dead-ending (CombatController.Confirm's rage fallback, emulated below);
//   * Charge steps back out of its 8 yd minimum range from the battle formation (CombatController.TryStepBack).
// The fights are driven through the same Battle calls the Unity CombatController makes (its Begin, PlanUse, the approach
// walk, Confirm and the smart click on an enemy are mirrored by the Player class below).
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;
using static Lanternvale.Tests.RulesTestUtil;

namespace Lanternvale.Tests
{
    public static class TestsWarriorPlayerFeel
    {
        // ================================================================ rules

        static (Battle b, Unit w, Unit foe) Duel(int level, int seed = 5, float dist = 2f)
        {
            var b = NewBattle(seed, new Inventory());
            var w = Hero(ClassId.Warrior, level, talents: false).At(20f, 20f);
            w.AutoPlay = false;
            var foe = Mob("cr_bandit_cutthroat", Math.Max(1, level - 1)).At(20f + dist, 20f).Tough();
            w.FaceTowards(foe.Position);
            foe.FaceTowards(w.Position);
            b.AddUnit(w);
            b.AddUnit(foe);
            b.Begin();
            SkipTo(b, w);
            return (b, w, foe);
        }

        static int MainHandSwings(Battle b, Unit u, int from) =>
            b.Events.Skip(from).Count(e => e.Source == u && e.AutoAttack && !e.OffHand && !e.Ranged &&
                                           (e.Type == CombatEventType.Damage || e.Type == CombatEventType.Miss || e.Type == CombatEventType.Dodge ||
                                            e.Type == CombatEventType.Parry))
            + b.Events.Skip(from).Count(e => e.Source == u && e.Type == CombatEventType.AbilityUsed && e.Reason == "next swing");

        /// <summary>Attack lands its first swing at once (rage now, in the turn the warrior engages); the turn's total number of
        /// swings is the same as before — the opening swing is taken from the end-of-turn ones.</summary>
        [Test]
        public static void OpeningSwing_LandsWhenEngaging_SameSwingsPerTurn()
        {
            int rageNow = 0;
            for (int seed = 1; seed <= 6; seed++)
            {
                var (b, w, foe) = Duel(10, seed);
                Assert(w.Rage < 0.01f && b.HasOpeningSwing(w), "fights start at 0 rage, the opening swing to come");
                int c0 = b.Events.Count;
                Assert(b.UseAbility(w, "attack", foe).Ok, "attack");
                Assert(MainHandSwings(b, w, c0) == 1, "the first swing lands at once: " + MainHandSwings(b, w, c0));
                Assert(!b.HasOpeningSwing(w) && w.OpeningSwingUsed, "used once per battle");
                if (w.Rage > 0.5f) rageNow++;
                var speed = StatCalculator.GetWeapon(w, WeaponSlot.MainHand).Speed;
                b.EndTurn(w);
                int expected = (int)Math.Floor(RulesConstants.TurnSeconds * w.Stats.MeleeHaste / speed + 1e-4f);
                Assert(MainHandSwings(b, w, c0) == expected, $"turn 1: {MainHandSwings(b, w, c0)} main-hand swings, as many as a turn always had ({expected}, speed {speed:0.0})");
                // pressing Attack again later in the fight does not swing again
                SkipTo(b, w);
                int c1 = b.Events.Count;
                b.UseAbility(w, "attack", foe);   // toggles off
                b.UseAbility(w, "attack", foe);   // and on
                Assert(MainHandSwings(b, w, c1) == 0, "no second opening swing");
            }
            Assert(rageNow >= 4, $"the opening swing's rage arrives in the same turn ({rageNow}/6 hit)");
        }

        /// <summary>A melee strike (Rend) starts the auto attack; the opening swing follows it, after the strike has resolved.
        /// Enemies get the same rule (symmetric): their first swing lands when they engage.</summary>
        [Test]
        public static void OpeningSwing_FollowsAStrike_AndWorksForEnemies()
        {
            var (b, w, foe) = Duel(10);
            w.Rage = 20f;
            int c0 = b.Events.Count;
            Assert(b.UseAbility(w, "warrior_rend", foe).Ok, "rend");
            var evs = b.Events.Skip(c0).ToList();
            int rend = evs.FindIndex(e => e.Type == CombatEventType.AuraApplied && e.AuraId == "warrior_rend" && e.Target == foe);
            int swing = evs.FindIndex(e => e.Source == w && e.AutoAttack);
            Assert(rend >= 0 && swing > rend, $"Rend first ({rend}), then the opening swing ({swing})");

            var b2 = NewBattle(3, new Inventory());
            var hero = Hero(ClassId.Mage, 10).At(20f, 20f).Tough();
            var wolf = Mob("cr_wolf", 10).At(22f, 20f);
            b2.AddUnit(hero); b2.AddUnit(wolf);
            b2.Begin();
            SkipTo(b2, wolf);
            int c2 = b2.Events.Count;
            Assert(b2.UseAbility(wolf, "attack", hero).Ok, "wolf attacks");
            Assert(MainHandSwings(b2, wolf, c2) == 1, "the wolf's first bite lands as it engages");
        }

        /// <summary>A queued Heroic Strike holds its rage: other abilities may only spend the rest, the refusal says so, and
        /// the strike fires at the swing.</summary>
        [Test]
        public static void HeroicStrike_HoldsItsRage_UntilItsSwing()
        {
            var (b, w, foe) = Duel(10);
            w.OpeningSwingUsed = true;   // already swung this battle: the strike waits for the end-of-turn swing
            w.Rage = 24f;
            Assert(b.UseAbility(w, "attack", foe).Ok, "attacking");
            float hs = AbilityRules.ResourceCost(w, Db.Ability("warrior_heroic_strike"), AbilityRules.UsedRank(w, Db.Ability("warrior_heroic_strike")), AbilityMods.For(w, Db.Ability("warrior_heroic_strike")));
            Assert(b.UseAbility(w, "warrior_heroic_strike", foe).Ok, "queued");
            var chk = b.CanUse(w, "warrior_rend", foe);
            Assert(!chk.Ok && chk.Code == UseFailure.Resource && chk.Reason.Contains($"{hs:0} is held for Heroic Strike") && chk.Reason.Contains("press it again"),
                "Rend (10) does not fit beside the held rage: " + chk);
            var bar = b.GetStatus(w, Db.Ability("warrior_rend"), false, 0, false);
            Assert(!bar.Usable && bar.Code == UseFailure.Resource, "the bar shows it (blue no-rage tint and the reason)");
            Assert(b.CanUse(w, "warrior_cleave", foe).Code != UseFailure.Resource || w.Rage < 20f, "another next-swing ability replaces the queue, so it is not blocked by it");
            b.CancelQueuedSwing(w);
            Assert(b.CanUse(w, "warrior_rend", foe).Ok, "un-queued: the rage is free again");
            Assert(b.UseAbility(w, "warrior_heroic_strike", foe).Ok, "queued again");
            int c0 = b.Events.Count;
            b.EndTurn(w);
            Assert(b.Events.Skip(c0).Any(e => e.Type == CombatEventType.AbilityUsed && e.AbilityId == "warrior_heroic_strike" && e.Reason == "next swing"), "it fires at the swing");
        }

        /// <summary>When the swing still cannot pay the queued strike (a stance swap emptied the rage), it fails visibly (CastFailed
        /// with the reason, a log line) and a white swing lands instead — it no longer vanishes.</summary>
        [Test]
        public static void HeroicStrike_ThatCannotBePaid_FailsVisibly()
        {
            var (b, w, foe) = Duel(10);
            w.OpeningSwingUsed = true;
            w.Rage = 20f;
            Assert(b.UseAbility(w, "attack", foe).Ok && b.UseAbility(w, "warrior_heroic_strike", foe).Ok, "queued");
            w.Rage = 0f;   // what a stance swap without Tactical Mastery does
            int c0 = b.Events.Count;
            b.EndTurn(w);
            var evs = b.Events.Skip(c0).ToList();
            var fail = evs.FirstOrDefault(e => e.Type == CombatEventType.CastFailed && e.Source == w && e.AbilityId == "warrior_heroic_strike");
            Assert(fail != null && fail.Reason == "Not enough rage.", "CastFailed with the reason");
            Assert(CombatLog.Format(fail).Contains("Heroic Strike fails: Not enough rage"), "a log line: " + CombatLog.Format(fail));
            Assert(evs.Any(e => e.Source == w && e.AutoAttack && !e.OffHand), "a white swing instead");
            Assert(w.QueuedSwing == "", "the queue is cleared");
        }

        /// <summary>Every refusal for rage says what to do about it.</summary>
        [Test]
        public static void NotEnoughRage_SaysToAttack()
        {
            var (b, w, foe) = Duel(10);
            var chk = b.CanUseIgnoringTarget(w, Db.Ability("warrior_rend"));
            Assert(!chk.Ok && chk.Reason == "Not enough rage (10). Attack an enemy to build rage.", "not attacking: " + chk.Reason);
            var st = b.GetStatus(w, Db.Ability("warrior_heroic_strike"), false, 0, false);
            Assert(!st.Usable && st.Reason.Contains("Attack an enemy to build rage"), "the bar tooltip: " + st.Reason);
            w.OpeningSwingUsed = true;
            Assert(b.UseAbility(w, "attack", foe).Ok, "attacking");
            w.Rage = 0f;
            chk = b.CanUseIgnoringTarget(w, Db.Ability("warrior_rend"));
            Assert(chk.Reason.Contains("Your swings build more rage at the end of the turn"), "already swinging: " + chk.Reason);
            // other resources keep their plain sentence
            var (b2, m, _) = (NewBattle(2), Hero(ClassId.Mage, 10), (Unit)null);
            m.Mana = 0f;
            var c2 = b2.CanUseIgnoringTarget(m, Db.Ability("mage_fireball"));
            Assert(c2.Reason.StartsWith("Not enough mana") && !c2.Reason.Contains("Attack"), "mana: " + c2.Reason);
        }

        // ================================================================ the player's path

        /// <summary>
        /// The Unity CombatController's calls for one player unit (CombatController.Input.cs): Begin (the bar or hotkey),
        /// PlanUse with the approach walk (FindPathToRange) and the step back out of a minimum range, Confirm with the rage
        /// fallback, and the smart click on an enemy (PlanSmartAttack: melee Attack, walking into reach first).
        /// </summary>
        sealed class Player
        {
            public readonly Battle B;
            public readonly Unit U;
            UseFailure lastCode;
            List<Vec2> approach;

            public Player(Battle b, Unit u) { B = b; U = u; }

            NavGrid Grid => (B.Pathfinder as NavGridPathfinder)?.Grid;

            static bool RageStrike(AbilityDef a) => a.target == TargetType.Enemy && a.cost != null && a.cost.type == ResourceType.Rage;

            bool AttackingBuildsRageNow()
            {
                if (U.PowerType != ResourceType.Rage) return false;
                var t = U.AttackTarget;
                bool swinging = U.AutoAttacking && t != null && t.IsAlive && t.IsHostileTo(U) && B.Units.Contains(t) && B.InMeleeRange(U, t);
                return !swinging || B.HasOpeningSwing(U);
            }

            /// <summary>CombatController.PlanUse (+ TryApproach, TryStepBack). null = usable (approach set when it walks first).</summary>
            string PlanUse(AbilityDef a, Unit target, bool stepBack)
            {
                approach = null;
                var chk = B.CanUse(U, a, target);
                lastCode = chk.Code;
                if (chk.Ok) return null;
                if (chk.Code == UseFailure.Range || (chk.Code == UseFailure.LineOfSight && !AbilityRules.UsesMeleeReach(a)))
                    return TryApproach(a, target);
                if (stepBack && chk.Code == UseFailure.TooClose && target != null) return TryStepBack(a, target);
                return chk.Reason;
            }

            string TryApproach(AbilityDef a, Unit target)
            {
                var grid = Grid;
                if (grid == null) return "no grid";
                var cannot = B.CannotMoveReason(U);
                if (cannot != null) return "Out of range (" + cannot + ")";
                float range = AbilityRules.RangeMetres(U, a, target, AbilityMods.For(U, a), B.Config);
                var agent = NavAgent.ForUnit(U.Radius, U.Id, target.Id);
                var p = grid.FindPathToRange(U.Position, target.Position, Math.Max(0.1f, range - 0.15f), agent, Math.Max(0f, U.MoveLeft), !AbilityRules.UsesMeleeReach(a), new NavPath());
                if (p.Status == PathStatus.NoPath) return "No path to the target.";
                if (p.Truncated || p.Status != PathStatus.Complete) return $"Out of range (needs {Math.Max(0.1f, p.FullLength - U.MoveLeft):0.0} m more movement).";
                if (p.IsEmpty) return "Cannot move there.";
                approach = new List<Vec2>(p.Points);
                return null;
            }

            static readonly float[] Extra = { 0.3f, 0.8f, 1.5f };
            static readonly float[] Angles = { 0f, 25f, -25f, 50f, -50f, 80f, -80f, 115f, -115f };

            string TryStepBack(AbilityDef a, Unit target)
            {
                var grid = Grid;
                if (grid == null || B.CannotMoveReason(U) != null) return "Target is too close.";
                float min = AbilityRules.MinRangeMetres(a);
                var away = (U.Position - target.Position).Normalized;
                var agent = NavAgent.ForUnit(U.Radius, U.Id);
                List<Vec2> best = null;
                float bestLen = float.PositiveInfinity;
                foreach (var extra in Extra)
                {
                    foreach (var deg in Angles)
                    {
                        double r = deg * Math.PI / 180.0;
                        var dir = new Vec2((float)(away.x * Math.Cos(r) - away.y * Math.Sin(r)), (float)(away.x * Math.Sin(r) + away.y * Math.Cos(r)));
                        var goal = target.Position + dir * (min + extra);
                        if (!grid.IsWalkable(goal, agent)) continue;
                        var p = grid.FindPath(U.Position, goal, agent, U.MoveLeft, new NavPath());
                        if (!p.ReachedGoal || p.IsEmpty || p.Length >= bestLen || p.Length > U.MoveLeft + 1e-3f) continue;
                        var old = U.Position;
                        U.Position = goal;
                        bool ok = B.CanUse(U, a, target).Ok;
                        U.Position = old;
                        if (!ok) continue;
                        best = new List<Vec2>(p.Points);
                        bestLen = p.Length;
                    }
                    if (best != null) break;
                }
                if (best == null) return "Target is too close.";
                approach = best;
                return null;
            }

            /// <summary>CombatController.ExecutePlan + Execute.</summary>
            string Execute(AbilityDef a, Unit target)
            {
                if (approach != null)
                {
                    var m = B.MoveAlong(U, approach);
                    if (!m.Ok) return m.Reason;
                    var chk = B.CanUse(U, a, target);
                    if (!chk.Ok) return chk.Reason;
                }
                if (a.autoAttack && U.AutoAttacking && U.AttackTarget == target && U.AutoAttackAbility == a.id) return null;
                var r = B.UseAbility(U, a.id, target);
                return r.Ok ? null : r.Reason;
            }

            /// <summary>Left click on an enemy outside targeting (PlanSmartAttack, melee): walk into reach, Attack.</summary>
            public string ClickEnemy(Unit target)
            {
                var attack = Db.Ability("attack");
                var why = PlanUse(attack, target, false);
                return why ?? Execute(attack, target);
            }

            /// <summary>CombatController.Confirm: the targeted ability on a unit, with the rage fallback.</summary>
            string Confirm(AbilityDef a, Unit target)
            {
                var why = PlanUse(a, target, true);
                bool fallback = why != null && lastCode == UseFailure.Resource && RageStrike(a) && target.IsAlive && target.IsHostileTo(U) && AttackingBuildsRageNow();
                if (!fallback) return why ?? Execute(a, target);
                Fallbacks++;
                var atk = ClickEnemy(target);
                if (atk != null) return a.name + ": " + why + " " + atk;
                if (B.IsOver || B.ActiveUnit != U || !target.IsAlive) return "(fight over)";
                var again = PlanUse(a, target, true);
                return again ?? Execute(a, target);
            }

            public int Fallbacks;

            /// <summary>CombatController.Begin (bar click / hotkey), then a click on <paramref name="clickOn"/> when it targets.</summary>
            public string Press(AbilityDef a, Unit clickOn)
            {
                if (a.nextSwing && U.QueuedSwing == a.id) { B.CancelQueuedSwing(U); return "un-queued"; }
                var chk = B.CanUseIgnoringTarget(U, a);
                if (!chk.Ok)
                {
                    if (chk.Code == UseFailure.Resource && RageStrike(a) && AttackingBuildsRageNow()) return Confirm(a, clickOn);
                    return chk.Reason;
                }
                if (a.target == TargetType.Self) { var r = B.UseAbility(U, a.id, U); return r.Ok ? null : r.Reason; }
                if (a.target == TargetType.Point && a.area.centeredOnCaster) { var r = B.UseAbility(U, a.id, null, U.Position); return r.Ok ? null : r.Reason; }
                var cur = U.AttackTarget;
                bool curOk = cur != null && cur.IsAlive && cur.IsHostileTo(U) && B.Units.Contains(cur);
                if (a.nextSwing && curOk && B.CanUse(U, a, cur).Ok) { var r = B.UseAbility(U, a.id, cur); return r.Ok ? null : r.Reason; }
                return Confirm(a, clickOn);
            }
        }

        // ================================================================ fights

        enum Style { ClickEnemyFirst, BarOnly }

        sealed class FightStats
        {
            public int FirstRageTurn = -1, FirstReachTurn = -1, HsQueued, HsFired, HsFailed, RendLanded, HsLanded, Fallbacks;
            public BattleOutcome Outcome;
        }

        static readonly string[] Strikes = { "warrior_bloodrage", "warrior_rend", "warrior_heroic_strike", "warrior_hamstring", "warrior_sunder_armor" };

        static GameSession Party(int level, ulong seed)
        {
            var s = SessionTest.NewGame(ClassId.Warrior, level, seed);
            if (level >= 2) { s.Recruit("kael"); s.Recruit("aldric"); }
            if (level >= 8) { s.Recruit("seren"); s.Recruit("rook"); }
            foreach (var m in s.Party) if (m != s.Main) s.SetAutoPlay(m, true);
            s.TakeEvents();
            return s;
        }

        /// <summary>Walks the party into the encounter (the battle formation forms as in play) and returns the battle.</summary>
        static Battle Engage(GameSession s, string enc)
        {
            var ed = s.Map.FindEncounter(enc);
            Assert(ed != null, "encounter " + enc);
            if (!string.IsNullOrEmpty(ed.requireFlag))
                foreach (var part in ed.requireFlag.Split('&'))
                {
                    var t = part.Trim();
                    if (t.Length > 0 && !t.StartsWith("!") && !t.Contains("=") && !t.Contains("<") && !t.Contains(">")) s.Flags.Set(t);
                }
            for (int leg = 0; leg < 10 && s.Battle == null; leg++)
            {
                if (s.Mode == SessionMode.Dialogue)
                {
                    SessionTest.SkipText(s);
                    var v = s.Dialogue.Current;
                    int i = SessionTest.ChoiceIndex(v, "Attack");
                    if (i < 0) i = SessionTest.ChoiceIndex(v, "Stand aside");
                    if (i >= 0) s.ChooseDialogue(i); else SessionTest.Finish(s);
                    continue;
                }
                var dir = s.Leader.Position - ed.pos;
                if (dir.SqrLength < 1e-4f) dir = new Vec2(-1, 0);
                var edge = s.Nav.ClampToWalkable(ed.pos + dir.Normalized * Math.Max(0.5f, ed.radius - 0.3f), NavAgent.Default.IgnoringAllUnits());
                s.UpdatePartyPositions(edge);
                if (s.Battle == null && s.Mode == SessionMode.Exploration) s.MoveLeader(ed.pos);
            }
            var b = s.Battle ?? s.StartEncounter(enc);
            Assert(b != null && s.BattleEncounter?.id == enc, enc + ": " + s.LastError);
            return b;
        }

        static Unit Nearest(Battle b, Unit u)
        {
            Unit best = null; float bd = float.MaxValue;
            foreach (var o in b.Units)
                if (o.IsAlive && o.IsHostileTo(u) && !o.IsTotem && !o.IsUntargetable) { float d = u.DistanceTo(o); if (d < bd) { bd = d; best = o; } }
            return best;
        }

        static FightStats Fight(GameSession s, string enc, Style style)
        {
            var st = new FightStats();
            var b = Engage(s, enc);
            var me = s.Main;
            Assert(me.Rage < 0.01f, "fights start at 0 rage");
            var p = new Player(b, me);
            var strikes = Strikes.Select(id => Db.Ability(id)).Where(a => a != null && me.Knows(a.id)).ToList();
            int guard = 0;
            while (!b.IsOver && guard++ < 4000 && b.Round <= 40)
            {
                var u = b.ActiveUnit;
                if (u == null) break;
                if (u != me)
                {
                    AI.RunTurn(b, u);
                    if (b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
                    continue;
                }
                var tgt = me.AttackTarget != null && me.AttackTarget.IsAlive ? me.AttackTarget : Nearest(b, me);
                if (tgt == null) { b.EndTurn(me); continue; }
                if (style == Style.ClickEnemyFirst)
                {
                    var click = p.ClickEnemy(tgt);
                    if (Debug) Console.WriteLine($"      T{me.TurnsTaken} click {tgt.Name}: {click ?? "ok"}");
                }
                foreach (var a in strikes)
                {
                    if (b.IsOver || b.ActiveUnit != me) break;
                    if (a.id == "warrior_bloodrage" && me.Rage > 30f) continue;
                    if (a.nextSwing && me.QueuedSwing == a.id) continue;   // pressing it again would un-queue it
                    if (a.id == "warrior_rend" && tgt.HasAura("warrior_rend", me)) continue;
                    if (!tgt.IsAlive) tgt = Nearest(b, me);
                    if (tgt == null) break;
                    var pressed = p.Press(a, tgt);
                    if (Debug) Console.WriteLine($"      T{me.TurnsTaken} {a.name}: {pressed ?? "used"} (rage {me.Rage:0}, {me.DistanceTo(tgt):0.0} m away, {me.MoveLeft:0.0} m move left)");
                    if (pressed != null) continue;
                    if (a.id == "warrior_heroic_strike") st.HsQueued++;
                    if (st.FirstRageTurn < 0 && a.cost != null && a.cost.type == ResourceType.Rage) st.FirstRageTurn = me.TurnsTaken;
                }
                // the first turn the warrior stands in melee reach of an enemy (some fights start out of reach: a walk first)
                if (st.FirstReachTurn < 0 && b.Units.Any(o => o.IsAlive && o.IsHostileTo(me) && !o.IsTotem && b.InMeleeRange(me, o)))
                    st.FirstReachTurn = me.TurnsTaken;
                if (!b.IsOver && b.ActiveUnit == me) b.EndTurn(me);
            }
            foreach (var e in b.Events)
            {
                if (e.Source != me) continue;
                if (e.Type == CombatEventType.AbilityUsed && e.AbilityId == "warrior_heroic_strike" && e.Reason == "next swing") st.HsFired++;
                if (e.Type == CombatEventType.CastFailed && e.AbilityId == "warrior_heroic_strike" && !string.IsNullOrEmpty(e.Reason)) st.HsFailed++;
                if (e.Type == CombatEventType.AuraApplied && e.AuraId == "warrior_rend") st.RendLanded++;
                if (e.Type == CombatEventType.Damage && e.AbilityId == "warrior_heroic_strike") st.HsLanded++;
            }
            st.Outcome = b.Outcome;
            st.Fallbacks = p.Fallbacks;
            if (b.IsOver) { s.FinishBattle(); if (s.PendingLoot != null) s.TakeAllLoot(); }
            else s.LeaveCombat();
            return st;
        }

        static readonly (int lvl, string map, string enc)[] Fights =
        {
            (1, null, "enc_pasture_wolves"), (4, "whisperwood", "enc_boars_edge"), (8, "whisperwood", "enc_forest_wolves"),
            (12, "shrine", "enc_hollow_pilgrims"), (20, "mirefen", "enc_mf_barrow_ghouls"),
        };

        static int Seeds => int.TryParse(Environment.GetEnvironmentVariable("LV_FEEL_SEEDS"), out var n) ? n : 8;
        static bool Debug => Environment.GetEnvironmentVariable("LV_FEEL_DEBUG") == "1";

        /// <summary>
        /// A new-game warrior (levels 1, 4, 8, 12, 20, companions as the game gives them) walks into a normal fight and the
        /// player either clicks the enemy and then presses the strikes, or only presses the strikes on the bar. Either way
        /// a rage ability goes off on the turn the warrior reaches an enemy (turn 1 from the formation) or the next, Rend
        /// and Heroic Strike land on the target, and every queued Heroic Strike fires or fails visibly.
        /// </summary>
        [Test]
        public static void NewGameWarrior_UsesRageAbilities_OnTurnOneOrTwo()
        {
            var report = new List<string>();
            var bad = new List<string>();
            foreach (var style in new[] { Style.ClickEnemyFirst, Style.BarOnly })
                foreach (var f in Fights)
                {
                    int early = 0, turn1 = 0, reach1 = 0, rend = 0, hsLanded = 0, fallbacks = 0, wins = 0;
                    for (int i = 0; i < Seeds; i++)
                    {
                        var s = Party(f.lvl, (ulong)(300 + i));
                        if (f.map != null) s.EnterMap(f.map);
                        var st = Fight(s, f.enc, style);
                        // on the turn the warrior reaches an enemy, or the next one (turn 1 or 2 when he starts in reach)
                        if (st.FirstRageTurn >= 1 && st.FirstRageTurn <= Math.Max(1, st.FirstReachTurn) + 1) early++;
                        if (st.FirstRageTurn == 1) turn1++;
                        if (st.FirstReachTurn == 1) reach1++;
                        rend += st.RendLanded;
                        hsLanded += st.HsLanded;
                        fallbacks += st.Fallbacks;
                        if (st.Outcome == BattleOutcome.Victory) wins++;
                        if (st.HsQueued != st.HsFired + st.HsFailed)
                            bad.Add($"L{f.lvl} {style} seed {i}: Heroic Strike queued {st.HsQueued}, fired {st.HsFired}, failed visibly {st.HsFailed}");
                    }
                    report.Add($"L{f.lvl} {f.enc} {style}: rage ability by the turn after reaching an enemy in {early}/{Seeds} (on turn 1: {turn1}; in reach on turn 1: {reach1}), Rend landed {rend}, HS hits {hsLanded}, rage fallbacks {fallbacks}, wins {wins}");
                    if (early < Seeds - 1) bad.Add($"L{f.lvl} {style}: a rage ability by the turn after reaching an enemy in only {early}/{Seeds} fights");
                    if (turn1 < reach1 - 2) bad.Add($"L{f.lvl} {style}: in reach on turn 1 in {reach1} fights, but a rage ability on turn 1 in only {turn1}");
                    if (f.lvl >= 4 && rend == 0) bad.Add($"L{f.lvl} {style}: Rend never landed");
                    if (hsLanded == 0) bad.Add($"L{f.lvl} {style}: Heroic Strike never hit");
                    if (style == Style.BarOnly && fallbacks == 0) bad.Add($"L{f.lvl}: the bar-only player never needed the attack-first fallback?");
                }
            foreach (var r in report) Console.WriteLine("    " + r);
            Assert(bad.Count == 0, string.Join("\n    ", bad));
        }

        /// <summary>
        /// Charge from the battle formation: the warrior starts next to the enemies (inside Charge's 8 yd minimum), so the
        /// click steps back out of it and charges — rage and the stun on turn 1, when the warrior acts before being struck.
        /// Once he has fought, the refusal says Charge only opens a fight.
        /// </summary>
        [Test]
        public static void Charge_FromTheFormation_StepsBackAndCharges()
        {
            int tried = 0, charged = 0;
            var why = new List<string>();
            for (int i = 0; i < 8; i++)
            {
                var s = Party(8, (ulong)(500 + i));
                s.EnterMap("whisperwood");
                var b = Engage(s, "enc_forest_wolves");
                var me = s.Main;
                int guard = 0;
                while (!b.IsOver && b.ActiveUnit != me && guard++ < 50)
                {
                    var u = b.ActiveUnit;
                    AI.RunTurn(b, u);
                    if (b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
                }
                if (b.IsOver || b.ActiveUnit != me || me.TurnsTaken != 1) { s.LeaveCombat(); continue; }
                var charge = Db.Ability("warrior_charge");
                var target = Nearest(b, me);
                if (me.Engaged)
                {
                    var c = b.CanUseIgnoringTarget(me, charge);
                    Assert(!c.Ok && c.Reason.Contains("Charge only opens a fight"), "struck first: " + c.Reason);
                    s.LeaveCombat();
                    continue;
                }
                tried++;
                Assert(me.DistanceTo(target) < AbilityRules.MinRangeMetres(charge), $"the formation puts the warrior inside Charge's minimum range ({me.DistanceTo(target):0.0} m)");
                Assert(b.CanUse(me, charge, target).Code == UseFailure.TooClose, "too close where he stands");
                var p = new Player(b, me);
                int c0 = b.Events.Count;
                var r = p.Press(charge, target);
                if (r == null && b.Events.Skip(c0).Any(e => e.Type == CombatEventType.Charge && e.Source == me) && me.Rage >= 9f) charged++;
                else why.Add(r ?? $"no charge (rage {me.Rage:0})");
                s.LeaveCombat();
            }
            Console.WriteLine($"    Charge from the formation: {charged}/{tried} on turn 1 (warrior acting before being struck)" + (why.Count > 0 ? ": " + string.Join("; ", why) : ""));
            Assert(tried >= 1, "the warrior acts before being struck in some fights");
            Assert(charged == tried, "every time he steps back and charges");
        }

        /// <summary>Not a cause, kept true: the warrior is in Battle Stance after a new game, a load and a level-up.</summary>
        [Test]
        public static void BattleStance_AfterNewGame_Load_AndLevelUp()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 1, 5, veteranGear: false);
            Assert(s.Main.HasAura("warrior_battle_stance_aura"), "new game");
            var s2 = new GameSession(Db, 9);
            Assert(s2.LoadGame(s.SaveGame(), out var err), err);
            Assert(s2.Main.HasAura("warrior_battle_stance_aura"), "after load");
            s.GivePartyXp(5000);
            Assert(s.Main.Level > 1 && s.Main.HasAura("warrior_battle_stance_aura"), "after a level-up");
        }
    }
}
