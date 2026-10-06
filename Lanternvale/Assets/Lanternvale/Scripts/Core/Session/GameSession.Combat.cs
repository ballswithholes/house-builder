// Encounters -> Battle (party, pets, scaled enemies, NavGrid pathfinder), AI stepping, leave combat, FinishBattle.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Session
{
    public sealed partial class GameSession
    {
        /// <summary>The current battle (null when exploring). Stays set after it ends until FinishBattle.</summary>
        public Battle Battle { get; private set; }
        public EncounterDef BattleEncounter { get; private set; }
        int battleCursor;
        bool battleLeft;

        /// <summary>The active unit waits for player input.</summary>
        public bool IsPlayerTurn => Battle != null && Battle.NeedsPlayerInput;
        public Unit ActiveUnit => Battle?.ActiveUnit;

        // ================================================================= start

        /// <summary>
        /// Starts combat with an encounter of the current map: enemies from the EncounterDef (scaled to the party),
        /// the active party and its pets, NavGrid pathfinding. Null (reason in LastError) when unknown, already done,
        /// unavailable or not possible now.
        /// </summary>
        public Battle StartEncounter(string encounterId, SurpriseMode surprise = SurpriseMode.None)
        {
            var b = PrepareEncounter(encounterId, out var enemies);
            if (b == null) return null;
            Team? surprised = surprise == SurpriseMode.EnemiesSurprised ? Team.Enemy : surprise == SurpriseMode.PartySurprised ? Team.Player : (Team?)null;
            b.Begin(surprised);
            return b;
        }

        /// <summary>
        /// The player attacks an encounter first (its dialogue is skipped). With an opener ability (Charge, Ambush,
        /// Cheap Shot, Pyroblast…) the attacker uses it on enemy <paramref name="targetIndex"/> (EncounterDef.enemies
        /// order) before the first round (Battle.BeginWithOpener: an Opener from stealth surprises the enemies).
        /// Without one, the enemies are surprised when the attacker (default: the leader) is stealthed.
        /// An encounter field ability as the opener (IsEncounterFieldAbility: Mind Soothe) starts no fight: it is cast through
        /// <see cref="UseAbilityOnEncounter"/> and null is returned (LastError = its failure reason, "" on success).
        /// </summary>
        public Battle EngageEncounter(string encounterId, Unit attacker = null, string openerAbilityId = null, int targetIndex = 0)
        {
            attacker ??= Leader;
            if (!string.IsNullOrEmpty(openerAbilityId) && Battle == null && IsEncounterFieldAbility(openerAbilityId))
            {
                var fr = UseAbilityOnEncounter(attacker, openerAbilityId, encounterId, targetIndex);
                LastError = fr.Ok ? "" : (fr.Reason ?? "");
                return null;
            }
            bool stealth = attacker != null && attacker.IsStealthed;
            var b = PrepareEncounter(encounterId, out var enemies, stealth, stealth || !string.IsNullOrEmpty(openerAbilityId));
            if (b == null) return null;
            if (!string.IsNullOrEmpty(openerAbilityId) && attacker != null && b.Units.Contains(attacker))
            {
                var target = enemies.Count > 0 ? enemies[MathUtil.Clamp(targetIndex, 0, enemies.Count - 1)] : null;
                var r = b.BeginWithOpener(attacker, openerAbilityId, target);
                if (r.Ok) return b;
                LastError = r.Reason;
                Toast(r.Reason);
            }
            b.Begin(stealth ? Team.Enemy : (Team?)null);
            return b;
        }

        /// <summary>Builds (but does not begin) the battle for an encounter; null + LastError when not possible.
        /// <paramref name="unaware"/>: a stealthed attacker opens the fight, the enemies keep their idle facing.
        /// <paramref name="keepPositions"/>: no battle formation (stealth or an armed opener: positions matter).</summary>
        Battle PrepareEncounter(string encounterId, out List<Unit> enemies, bool unaware = false, bool keepPositions = false)
        {
            enemies = new List<Unit>();
            LastError = "";
            if (!hasGame || gameOver) { LastError = "No game."; return null; }
            if (Battle != null) { LastError = "Already in combat."; return null; }
            if (Dialogue.IsActive) { LastError = "Finish the conversation first."; return null; }
            var enc = Map?.FindEncounter(encounterId);
            if (enc == null) { LastError = $"No encounter '{encounterId}' here."; return null; }
            if (Map.IsEncounterDone(enc)) { LastError = "That fight is already over."; return null; }
            if (!Map.IsEncounterAvailable(enc)) { LastError = "There is nobody to fight."; return null; }

            var units = new List<Unit>();
            foreach (var m in party)
            {
                if (m.Dead) continue;
                units.Add(m);
            }
            foreach (var m in party)
                if (m.Pet != null && m.Pet.IsAlive && units.Contains(m) && !units.Contains(m.Pet)) units.Add(m.Pet);
            foreach (var x in OwnedSummons()) if (!units.Contains(x)) units.Add(x);   // totems placed before the pull

            int lvl = PartyLevel;
            int characters = 0;
            foreach (var u in units) if (u.IsCharacter) characters++;
            float healthScale = EncounterHealthScale(characters, MapDef != null && MapDef.raidSize > 0);   // GameSession.Raid.cs
            foreach (var ed in enc.enemies)
            {
                var def = Db.Creature(ed.creature);
                if (def == null) { Log.Warn($"GameSession: encounter {enc.id}: unknown creature '{ed.creature}'"); continue; }
                int level = UnitFactory.CreatureLevel(def, ed.level, lvl, Rng);
                var e = UnitFactory.CreateCreature(Db, def, level, Team.Enemy);
                if (healthScale != 1f)
                {
                    e.MaxHealthMult *= healthScale;
                    e.InvalidateStats();
                    e.RestoreFull();
                }
                e.Position = ed.pos;
                e.Facing = EncounterIdleFacing(enc, ed);
                enemies.Add(e);
            }
            if (enemies.Count == 0) { LastError = "There is nobody to fight."; return null; }

            if (PendingLoot != null) CloseLoot(true);
            CloseVendor();
            CloseTrainer();
            CloseRespec();

            // the party takes its battle formation by role (GameSession.Raid.cs), then everyone stands on free walkable spots
            if (!keepPositions && !IsPracticeEncounter(enc)) ArrangeBattleFormation(units, enemies);
            Nav.ClearUnits();
            var all = new List<Unit>(units);
            all.AddRange(enemies);
            var spots = new List<Vec2>();
            foreach (var u in all)
            {
                var agent = NavAgent.ForUnit(u.Radius, u.Id);
                if (!Nav.IsWalkable(u.Position, agent))
                {
                    spots.Clear();
                    Nav.FindStandingSpots(u.Position, 1, u.Radius * 2f, agent, spots);
                    if (spots.Count > 0) u.Position = spots[0];
                }
                Nav.SetUnit(u.Id, u.Position, u.Radius);
            }
            // the enemies turn towards the party, unless a stealthed attacker opens on them: they have noticed nobody yet
            // and keep their idle facing, so a rogue standing behind one can open with Garrote or Ambush (Battle.Begin
            // turns everyone towards the closest hostile after the opener)
            if (!unaware)
            {
                var partyCenter = Centroid(units);
                foreach (var e in enemies) e.FaceTowards(partyCenter);
            }

            var b = new Battle(Db, Rng, pathfinder, Inventory, true);
            b.AddUnits(all);
            b.EventRaised += ForwardCombatEvent;
            DetachField();
            Battle = b;
            BattleEncounter = enc;
            battleCursor = 0;
            battleLeft = false;
            Map.MarkEncounterTriggered(enc.id);
            suppressedEncounters.Remove(enc.id);
            soothedEncounters.Remove(enc.id);

            string bark = "";
            foreach (var e in enemies) if (!string.IsNullOrEmpty(e.Creature.bark)) { bark = e.Creature.bark; break; }
            Raise(new SessionEvent { Kind = SessionEventKind.CombatStarted, Battle = b, Id = enc.id, Text = bark });
            return b;
        }

        /// <summary>
        /// Which way an encounter enemy faces while idle (<see cref="EncounterEnemyPreview.Facing"/>): towards the
        /// encounter's centre, so a group faces inwards; +x for an enemy standing on the centre.
        /// </summary>
        static Vec2 EncounterIdleFacing(EncounterDef enc, EncounterEnemyDef ed)
        {
            if (enc != null && ed != null)
            {
                var d = enc.pos - ed.pos;
                if (d.SqrLength > 0.0625f) return d.Normalized;
            }
            return Vec2.Right;
        }

        static Vec2 Centroid(List<Unit> units)
        {
            if (units.Count == 0) return Vec2.Zero;
            var s = Vec2.Zero;
            foreach (var u in units) s += u.Position;
            return s / units.Count;
        }

        // ================================================================= stepping

        /// <summary>One step for the active AI-controlled unit (enemy, pet, auto-played companion). Null when it is the player's turn.</summary>
        public AIStep RunAIStep()
        {
            var b = Battle;
            if (b == null || b.IsOver) return null;
            var u = b.ActiveUnit;
            if (u == null || !b.IsAIControlled(u)) return null;
            var step = AI.NextStep(b, u);
            var r = AI.Execute(b, step);
            if (!r.Ok && step.Kind == AIStepKind.EndTurn && b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
            return step;
        }

        /// <summary>Runs the rest of the active AI-controlled unit's turn. Returns steps taken.</summary>
        public int RunAITurn()
        {
            var b = Battle;
            if (b == null || b.IsOver || b.ActiveUnit == null || !b.IsAIControlled(b.ActiveUnit)) return 0;
            return AI.RunTurn(b, b.ActiveUnit);
        }

        /// <summary>AI plays every unit (including player-controlled ones) until the battle ends or the round cap.</summary>
        public BattleOutcome AutoResolve(int maxRounds = 60)
        {
            var b = Battle;
            if (b == null) return BattleOutcome.None;
            int guard = 0;
            while (!b.IsOver && b.Round <= maxRounds && guard++ < 100000)
            {
                var u = b.ActiveUnit;
                if (u == null) break;
                AI.RunTurn(b, u);
                if (b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
            }
            return b.Outcome;
        }

        /// <summary>Battle events not yet taken by this cursor-based helper (alternative to Battle.TakeEvents).</summary>
        public List<CombatEvent> TakeBattleEvents() => Battle != null ? Battle.TakeEvents(ref battleCursor) : new List<CombatEvent>();

        // ================================================================= leave / finish

        /// <summary>Null when the party may walk away: every remaining hostile is Passive (training dummy).</summary>
        public string CannotLeaveCombatReason()
        {
            var b = Battle;
            if (b == null) return "Not in combat.";
            if (b.IsOver) return "The battle is over.";
            return b.CanDisengage ? null : "You cannot leave while enemies are fighting.";
        }

        /// <summary>Ends a fight against passive targets (Battle.Disengage) without marking the encounter done; applies it
        /// like FinishBattle. Returns the summary (Outcome Left), or null (LastError) when not allowed.</summary>
        public BattleSummary LeaveCombat()
        {
            var why = CannotLeaveCombatReason();
            if (why != null) { LastError = why; return null; }
            var r = Battle.Disengage();
            if (!r.Ok) { LastError = r.Reason; return null; }
            battleLeft = true;
            return FinishBattle();
        }

        /// <summary>
        /// Applies the result of a finished battle: XP (level-ups), quest kills, the encounter's done flag, loot window,
        /// downed allies back at 1 HP; on defeat, game over. Null (LastError) when there is no finished battle.
        /// </summary>
        public BattleSummary FinishBattle()
        {
            var b = Battle;
            if (b == null) { LastError = "Not in combat."; return null; }
            if (!b.IsOver) { LastError = "The battle is not over."; return null; }
            var enc = BattleEncounter;
            var s = new BattleSummary { EncounterId = enc != null ? enc.id : "", Rounds = b.Round };
            b.EventRaised -= ForwardCombatEvent;
            Battle = null;
            BattleEncounter = null;
            bool left = battleLeft || b.Outcome == BattleOutcome.Fled;
            battleLeft = false;
            Nav?.ClearUnits();

            if (b.Outcome == BattleOutcome.Defeat)
            {
                s.Outcome = CombatEndKind.Defeat;
                if (TryRaidWipe(b, enc, s)) return s;   // a raid wipe: home, healed, no game over (GameSession.Raid.cs)
                gameOver = true;
                Raise(new SessionEvent { Kind = SessionEventKind.CombatEnded, Outcome = CombatEndKind.Defeat, Id = s.EncounterId, Battle = b, Text = "Defeat..." });
                Raise(new SessionEvent { Kind = SessionEventKind.GameOver, Text = "The party has fallen." });
                return s;
            }
            s.Outcome = left ? CombatEndKind.Left : CombatEndKind.Victory;

            // allies get up, pets that died are gone
            foreach (var m in party)
            {
                if (m.Dead || m.Downed || m.Health <= 0f)
                {
                    m.Dead = false;
                    m.Downed = false;
                    m.Health = Math.Max(1f, m.Health);
                }
                m.SecondsSinceCombat = 0f;
                if (m.Pet != null && (m.Pet.Dead || !m.Pet.IsAlive || !m.Pet.IsPersistentPet))
                {
                    m.Pet = null;
                    Raise(new SessionEvent { Kind = SessionEventKind.PetChanged, Unit = m });
                }
            }

            // quest kills
            foreach (var id in b.KilledCreatures)
            {
                s.Killed.Add(id);
                World.Quests.OnKill(id);
            }

            // xp (rate already applied by the rules engine)
            var main = Main;
            int xp = 0;
            if (b.Result != null && main != null)
            {
                if (!b.Result.Xp.TryGetValue(main, out xp))
                {
                    xp = 0;
                    foreach (var d in b.Result.Defeated) xp += Progression.KillXp(Db, main.Level, d);
                }
            }
            s.Xp = xp;
            if (xp > 0) s.LevelUps.AddRange(GivePartyXp(xp));

            // encounter state
            if (enc != null && Map != null)
            {
                if (left || IsPracticeEncounter(enc))
                {
                    Map.ResetEncounter(enc.id);
                    suppressedEncounters.Add(enc.id);
                }
                else Map.MarkEncounterDone(enc.id);
            }

            RebuildField();
            // the party stands where it fought: a leader who ended the fight inside a transition must step out of it before
            // it can travel (as on map entry) — the next step after a battle never changes the map by surprise
            if (Map != null && Leader != null) transitionArmed = Map.TransitionAt(Leader.Position) == null;
            Raise(new SessionEvent
            {
                Kind = SessionEventKind.CombatEnded, Outcome = s.Outcome, Id = s.EncounterId, Battle = b,
                Text = left ? "You step away from the fight." : "Victory!",
            });
            FlushFlagsChanged();

            // loot
            if (b.Result != null && (b.Result.Loot.Items.Count > 0 || b.Result.Loot.Gold > 0))
            {
                var w = new LootWindow { Source = "battle", Title = "Spoils" };
                w.Items.AddRange(b.Result.Loot.Items);
                w.Gold = b.Result.Loot.Gold;
                s.Loot = w;
                OpenLoot(w);
            }
            return s;
        }

        /// <summary>Every enemy of the encounter has Passive AI (training dummies): it resets instead of being marked done.</summary>
        public bool IsPracticeEncounter(EncounterDef enc)
        {
            if (enc == null || enc.enemies.Count == 0) return false;
            foreach (var ed in enc.enemies)
            {
                var c = Db.Creature(ed.creature);
                if (c == null || c.ai != AIProfile.Passive) return false;
            }
            return true;
        }

        /// <summary>Clears an encounter's done flag (respawn / practice targets) so it can be fought again.</summary>
        public void ResetEncounter(string encounterId)
        {
            if (Map == null || string.IsNullOrEmpty(encounterId)) return;
            if (Battle != null && BattleEncounter != null && BattleEncounter.id == encounterId) return;
            Map.ResetEncounter(encounterId);
            suppressedEncounters.Add(encounterId);
        }
    }
}
