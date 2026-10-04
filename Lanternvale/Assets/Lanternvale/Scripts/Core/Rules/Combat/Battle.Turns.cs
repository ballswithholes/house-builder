// Rounds, initiative, start/end-of-turn processing, regeneration, victory/defeat (Design.md §2, §4).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public sealed partial class Battle
    {
        static readonly UnitState[] ControlStates =
        {
            UnitState.Stun, UnitState.Incapacitate, UnitState.Sleep, UnitState.Polymorph, UnitState.Banish, UnitState.Fear, UnitState.Confuse,
        };

        /// <summary>
        /// Starts combat: rolls initiative, builds the turn order (pets after owners), fills threat tables,
        /// marks surprised units (they lose their first turn), fires OnBattleStart procs and starts the first turn.
        /// </summary>
        /// <param name="surprisedTeam">Team that is surprised (e.g. Team.Enemy when the party opens from stealth), or null.</param>
        public void Begin(Team? surprisedTeam = null)
        {
            if (Started) return;
            Started = true;
            InCombat = true;
            Round = 1;
            foreach (var u in Units)
            {
                ResetCombatState(u);
                if (!Meters.ContainsKey(u)) Meters[u] = new UnitMeters();
                u.Surprised = surprisedTeam.HasValue && u.Team == surprisedTeam.Value;
                if (u.IsAlive) Pathfinder?.SetUnit(u.Id, u.Position, u.Radius);
            }
            // initial facing: towards the closest hostile
            foreach (var u in Units)
            {
                Unit best = null; float bd = float.MaxValue;
                foreach (var o in Units) if (o.IsAlive && u.IsHostileTo(o)) { float d = u.DistanceTo(o); if (d < bd) { bd = d; best = o; } }
                if (best != null) u.FaceTowards(best.Position);
            }
            foreach (var u in Units) if (u.Team != PlayerTeam && u.IsAlive) EnsureThreatEntries(u);

            // initiative
            var roll = new List<Unit>();
            foreach (var u in Units)
            {
                if (!u.IsAlive || u.Kind == UnitKind.Totem) continue;
                if (u.Owner != null && Units.Contains(u.Owner)) continue; // pets follow their owner
                int d20 = Rng.D20();
                u.Initiative = Formulas.Initiative(d20, u.Stats.Agility);
                roll.Add(u);
                Emit(new CombatEvent { Type = CombatEventType.Initiative, Source = u, Amount = u.Initiative, Count = d20 });
            }
            roll.Sort((a, b) =>
            {
                int c = b.Initiative.CompareTo(a.Initiative);
                if (c != 0) return c;
                c = b.Stats.Agility.CompareTo(a.Stats.Agility);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            TurnOrder.Clear();
            foreach (var u in roll)
            {
                TurnOrder.Add(u);
                foreach (var p in Units)
                    if (p.Owner == u && p.IsAlive && p.Kind != UnitKind.Totem && !TurnOrder.Contains(p)) TurnOrder.Add(p);
            }
            Emit(new CombatEvent { Type = CombatEventType.BattleStart, Count = TurnOrder.Count });
            foreach (var u in new List<Unit>(Units)) if (u.IsAlive) FireProcs(ProcTrigger.OnBattleStart, u, null, new ProcInfo());
            RefreshAreaAuras();
            Emit(new CombatEvent { Type = CombatEventType.RoundStart });
            TurnIndex = -1;
            CheckBattleEnd();
            if (!IsOver) AdvanceTurn();
        }

        void ResetCombatState(Unit u)
        {
            u.Threat.Clear();
            u.AggroTarget = null;
            u.TauntedBy = null;
            u.TauntUntilTurn = -1;
            u.SelfRes = null;
            u.SwingMain = u.SwingOff = u.SwingRanged = 0f;
            u.QueuedSwing = "";
            u.QueuedSwingRank = 0;
            u.Pending = null;
            u.TimeDebt = 0f;
            u.TimeLeft = RulesConstants.TurnSeconds;
            u.Reactive.Clear();
            u.ExtraAttacks = 0;
            u.InOwnTurn = false;
            u.TurnsTaken = 0;
            u.ManaSpentTurn = -100;
            Array.Clear(u.StateWindow, 0, u.StateWindow.Length);
            u.LockoutWindow.Clear();
            if (u.Class != null && u.PowerType == ResourceType.Energy && u.Energy <= 0) u.Energy = u.MaxResource(ResourceType.Energy);
        }

        // ================================================================== turns

        /// <summary>Moves to the next unit able to act, processing skipped turns. Stops when a unit can act or the battle ends.</summary>
        bool advancing;

        void AdvanceTurn()
        {
            if (advancing) return; // the running loop continues by itself
            advancing = true;
            try { AdvanceTurnLoop(); }
            finally { advancing = false; }
        }

        void AdvanceTurnLoop()
        {
            int guard = 0;
            while (!IsOver && guard++ < 10000)
            {
                TurnIndex++;
                if (TurnIndex >= TurnOrder.Count)
                {
                    TurnIndex = 0;
                    Round++;
                    Emit(new CombatEvent { Type = CombatEventType.RoundStart });
                    if (TurnOrder.Count == 0) { CheckBattleEnd(); return; }
                }
                var u = TurnOrder[TurnIndex];
                if (!Units.Contains(u)) continue;
                if (!u.IsAlive)
                {
                    if (u.SelfRes != null && u.IsDeadOrDowned && StartSelfResTurn(u)) return;
                    continue;
                }
                if (StartTurn(u)) return; // waiting for actions
                if (ActiveUnit != null) return; // something else took the turn (should not happen)
            }
        }

        /// <summary>Start-of-turn processing. Returns true when the unit can now act (false when the turn was skipped/ended).</summary>
        bool StartTurn(Unit u)
        {
            ActiveUnit = u;
            u.InOwnTurn = true;
            u.TurnsTaken++;
            u.AIMemory.Reset();
            Emit(new CombatEvent { Type = CombatEventType.TurnStart, Source = u, Target = u });

            // 1. control windows from the state at turn start (before 6 s elapse)
            ComputeWindows(u);
            float lost = 0f;
            UnitState lostBy = UnitState.Stun;
            foreach (var s in ControlStates)
                if (u.StateWindow[(int)s] > lost) { lost = u.StateWindow[(int)s]; lostBy = s; }
            if (u.Surprised && u.TurnsTaken == 1) { lost = RulesConstants.TurnSeconds; lostBy = UnitState.Stun; }
            float rootTime = u.StateWindow[(int)UnitState.Root];

            // 2. elapse 6 s: auras (ticks), cooldowns, lockouts, proc ICDs, lifetime
            ElapseAuras(u, RulesConstants.TurnSeconds);
            if (!u.IsAlive || IsOver) { EndTurnInternal(u, false); return false; }
            ElapseTimers(u, RulesConstants.TurnSeconds);
            if (u.Lifetime > 0)
            {
                u.Lifetime -= RulesConstants.TurnSeconds;
                if (u.Lifetime <= 1e-3f) { Despawn(u, "expired"); EndTurnInternal(u, false); return false; }
            }
            // still controlled after elapsing (control lasts beyond this turn) => whole turn lost
            foreach (var s in ControlStates)
                if (u.HasStateAura(s)) { lost = RulesConstants.TurnSeconds; lostBy = s; break; }
            if (u.HasStateAura(UnitState.Root)) rootTime = RulesConstants.TurnSeconds;
            if (Specials.IgnoresState(u, UnitState.Root)) rootTime = 0f;
            Specials.OnBearerTurnStart(this, u);
            Specials.OnAnyTurnStart(this, u);
            if (!u.IsAlive || IsOver || ActiveUnit != u) { if (ActiveUnit == u) EndTurnInternal(u, false); return false; }

            // 3. regeneration
            RegenTurn(u);

            // 4. time budget
            float startClock = Math.Max(u.TimeDebt, lost);
            u.TimeDebt = 0f;
            u.TimeLeft = RulesConstants.TurnSeconds - startClock;
            u.SwingTimeThisTurn = RulesConstants.TurnSeconds - Math.Min(RulesConstants.TurnSeconds, lost);

            // 5. movement budget
            u.MoveBudget = BaseMove(u);
            float movingFraction = 1f - Math.Min(RulesConstants.TurnSeconds, Math.Max(lost, rootTime)) / RulesConstants.TurnSeconds;
            u.MoveLeft = u.MoveBudget * movingFraction;

            // 6. pending cast / channel
            if (u.Pending != null)
            {
                if (lost >= RulesConstants.TurnSeconds - 1e-3f)
                {
                    CancelPending(u, "controlled", null, 0f);
                }
                else if (u.Pending.RemainingTime > RulesConstants.TurnSeconds + 1e-3f && !u.Pending.Channel)
                {
                    u.Pending.RemainingTime -= RulesConstants.TurnSeconds;
                    Emit(new CombatEvent { Type = CombatEventType.CastStart, Source = u, Target = u.Pending.Target, AbilityId = u.Pending.Ability.id, Name = u.Pending.Ability.name, Seconds = u.Pending.RemainingTime, Reason = "continuing" });
                    EndTurnInternal(u, false);
                    return false;
                }
                else
                {
                    float used = Math.Min(RulesConstants.TurnSeconds, u.Pending.RemainingTime);
                    u.TimeLeft = Math.Min(u.TimeLeft, RulesConstants.TurnSeconds - used);
                    ResolvePending(u);
                    if (!u.IsAlive || IsOver) { EndTurnInternal(u, false); return false; }
                }
            }

            FireProcs(ProcTrigger.OnTurnStart, u, null, new ProcInfo());
            if (!u.IsAlive || IsOver) { EndTurnInternal(u, false); return false; }

            // 7. control: fear/confuse move the unit; a fully lost turn is skipped
            if (lost > 0f)
            {
                if (lostBy == UnitState.Fear || lostBy == UnitState.Confuse) ControlledMove(u, lostBy, lost);
                if (lost >= RulesConstants.TurnSeconds - 1e-3f)
                {
                    Emit(new CombatEvent { Type = CombatEventType.TurnSkipped, Source = u, Target = u, Reason = u.Surprised && u.TurnsTaken == 1 ? "Surprised" : lostBy.ToString() });
                    EndTurnInternal(u, false);
                    return false;
                }
            }
            if (u.Pending != null) { EndTurnInternal(u, false); return false; }
            RefreshAreaAuras();
            return true;
        }

        void ComputeWindows(Unit u)
        {
            Array.Clear(u.StateWindow, 0, u.StateWindow.Length);
            foreach (var a in u.Auras)
            {
                if (a.Def.states.Length == 0) continue;
                float w = a.IsPermanent ? RulesConstants.TurnSeconds : Math.Min(RulesConstants.TurnSeconds, a.Remaining);
                foreach (var s in a.Def.states)
                    if (w > u.StateWindow[(int)s]) u.StateWindow[(int)s] = w;
            }
            u.LockoutWindow.Clear();
            foreach (var kv in u.Lockouts)
                if (kv.Value > 0) u.LockoutWindow[kv.Key] = Math.Min(RulesConstants.TurnSeconds, kv.Value);
        }

        float BaseMove(Unit u)
        {
            float b = u.Creature != null && u.Class == null ? Math.Max(0f, u.Creature.moveSpeed) : (Config.baseMoveMetres > 0 ? Config.baseMoveMetres : RulesConstants.DefaultMoveMetres);
            if (u.IsTotem) return 0f;
            return Math.Max(0f, b * (1f + u.Stats.MoveSpeedPct / 100f));
        }

        // key snapshots of ElapseTimers (it calls nothing back, so one scratch list each is enough)
        readonly List<string> timerKeys = new List<string>();
        readonly List<School> lockoutKeys = new List<School>();

        void ElapseTimers(Unit u, float dt)
        {
            if (u.Cooldowns.Count > 0) ElapseTimerDictionary(u.Cooldowns, timerKeys, dt);
            if (u.Lockouts.Count > 0) ElapseTimerDictionary(u.Lockouts, lockoutKeys, dt);
            if (u.ProcCooldowns.Count > 0) ElapseTimerDictionary(u.ProcCooldowns, timerKeys, dt);
        }

        static void ElapseTimerDictionary<TKey>(Dictionary<TKey, float> timers, List<TKey> keys, float dt)
        {
            keys.Clear();
            foreach (var kv in timers) keys.Add(kv.Key);
            foreach (var k in keys)
            {
                float v = timers[k] - dt;
                if (v <= 1e-3f) timers.Remove(k); else timers[k] = v;
            }
            keys.Clear();
        }

        /// <summary>Fear/confuse: the unit moves on its own for the controlled part of the turn.</summary>
        void ControlledMove(Unit u, UnitState state, float seconds)
        {
            if (u.IsTotem || u.HasStateAura(UnitState.Root)) return;
            float dist = u.MoveBudget * Math.Min(1f, seconds / RulesConstants.TurnSeconds);
            if (dist <= 0.1f) return;
            Vec2 dir;
            if (state == UnitState.Fear)
            {
                Unit from = null;
                foreach (var a in u.Auras) if (a.HasState(UnitState.Fear) && a.Caster != null) { from = a.Caster; break; }
                if (from == null)
                {
                    float bd = float.MaxValue;
                    foreach (var o in Units) if (o.IsAlive && o.IsHostileTo(u)) { float d = u.DistanceTo(o); if (d < bd) { bd = d; from = o; } }
                }
                dir = from != null ? (u.Position - from.Position).Normalized : u.Facing * -1f;
                if (dir.SqrLength < 1e-6f) dir = Vec2.Right;
                // a little randomness
                double ang = (Rng.Value - 0.5) * 0.8;
                dir = new Vec2((float)(dir.x * Math.Cos(ang) - dir.y * Math.Sin(ang)), (float)(dir.x * Math.Sin(ang) + dir.y * Math.Cos(ang)));
            }
            else
            {
                double ang = Rng.Value * Math.PI * 2;
                dir = new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang));
            }
            var goal = Pathfinder.ClampToWalkable(u.Position + dir * dist, u.Radius, u.Id);
            var path = Pathfinder.FindPath(u.Position, goal, u.Radius, u.Id, -1, dist);
            if (path.Found && path.Length > 0.05f) DoMove(u, path.Points, path.Length, CombatEventType.Move, state.ToString());
        }

        // =============================================================== regen

        void RegenTurn(Unit u)
        {
            var st = u.Stats;
            const float T = RulesConstants.TurnSeconds;
            switch (u.PowerType)
            {
                case ResourceType.Energy:
                    ChangeResource(u, ResourceType.Energy, RulesConstants.EnergyPerTurn * st.EnergyRegen, null, true);
                    break;
                case ResourceType.Focus:
                    // no separate Focus-regeneration stat: EnergyRegen also scales Focus (Bestial Discipline)
                    ChangeResource(u, ResourceType.Focus, RulesConstants.FocusPerTurn * st.EnergyRegen, null, true);
                    break;
            }
            if (u.MaxMana > 0)
            {
                float regen;
                bool fsr = u.ManaSpentTurn >= u.TurnsTaken - 1;
                if (u.Class != null)
                {
                    float spirit = st.SpiritRegenPerTick * (T / 2f);
                    if (fsr) spirit *= st.SpiritRegenWhileCasting / 100f;
                    regen = spirit + st.ManaRegen * T / 5f;
                }
                else
                {
                    regen = u.MaxMana * (fsr ? 0.01f : 0.05f) + st.ManaRegen * T / 5f;
                }
                if (regen > 0) ChangeResource(u, ResourceType.Mana, regen, null, true);
            }
            if (st.HealthRegen > 0 && u.Health < u.MaxHealth)
                HealUnit(null, u, st.HealthRegen * T / 5f, new HealInfo { Periodic = true, Silent = true, NoThreat = true });
        }

        /// <summary>Changes a resource and emits a ResourceChange event. Returns the actual change.</summary>
        public float ChangeResource(Unit u, ResourceType r, float delta, Unit source = null, bool regen = false)
        {
            if (r == ResourceType.None || delta == 0f) return 0f;
            float actual = u.SetResource(r, u.GetResource(r) + delta);
            if (Math.Abs(actual) > 1e-3f)
                Emit(new CombatEvent { Type = CombatEventType.ResourceChange, Source = source ?? u, Target = u, Resource = r, Amount = actual, Periodic = regen });
            return actual;
        }

        // ============================================================ end turn

        /// <summary>Ends the active unit's turn: auto attacks, totem actions, OnTurnEnd procs; then starts the next turn.</summary>
        public ActionResult EndTurn(Unit u)
        {
            if (!InCombat || IsOver) return ActionResult.Fail("Not in combat.");
            if (u != ActiveUnit) return ActionResult.Fail("It is not this unit's turn.");
            EndTurnInternal(u, true);
            return ActionResult.Success;
        }

        void EndTurnInternal(Unit u, bool canSwing)
        {
            if (u.InOwnTurn)
            {
                if (u.IsAlive && !IsOver)
                {
                    bool swing = canSwing && u.Pending == null && !u.IsControlled;
                    AutoAttacks(u, swing);
                    if (!IsOver) TotemActions(u);
                    if (u.IsAlive && !IsOver) FireProcs(ProcTrigger.OnTurnEnd, u, null, new ProcInfo());
                }
                // reactive windows & taunt expiry
                if (u.Reactive.Count > 0)
                {
                    var rm = new List<string>();
                    foreach (var kv in u.Reactive) if (kv.Value <= u.TurnsTaken) rm.Add(kv.Key);
                    foreach (var k in rm) u.Reactive.Remove(k);
                }
                if (u.TauntedBy != null && u.TauntUntilTurn <= u.TurnsTaken) { u.TauntedBy = null; }
                if (u.AttackTarget != null && u.IsAlive && u.Team != PlayerTeam) u.FaceTowards(u.AttackTarget.Position);
                u.InOwnTurn = false;
                Array.Clear(u.StateWindow, 0, u.StateWindow.Length);
                u.LockoutWindow.Clear();
                Emit(new CombatEvent { Type = CombatEventType.TurnEnd, Source = u, Target = u });
            }
            if (ActiveUnit == u) ActiveUnit = null;
            RefreshAreaAuras();
            CheckBattleEnd();
            if (!IsOver && Started && InCombat && ActiveUnit == null) AdvanceTurn();
        }

        // ======================================================= victory / defeat

        /// <summary>Checks victory (no hostile non-totem unit alive) and defeat (whole party downed).</summary>
        public void CheckBattleEnd()
        {
            if (!Started || IsOver || !InCombat) return;
            bool hostileAlive = false, partyUp = false, anyParty = false;
            foreach (var u in Units)
            {
                if (!u.IsAlive) { if (u.Team == PlayerTeam && u.IsCharacter) anyParty = true; continue; }
                // mind-controlled enemies still count as enemies (enslaved demons are real pets until they break free)
                bool hostileSide = u.Team != PlayerTeam && u.Team != Team.Neutral;
                if (u.Team == PlayerTeam && u.OriginalTeam.HasValue && u.OriginalTeam.Value != PlayerTeam && u.Kind != UnitKind.Pet) hostileSide = true;
                if (hostileSide && !u.IsTotem) hostileAlive = true;
                if (u.Team == PlayerTeam && u.IsCharacter)
                {
                    anyParty = true;
                    // a party member sealed away (Divine Intervention: banished + invulnerable) cannot win the fight alone
                    if (!(u.HasStateAura(UnitState.Banish) && u.IsInvulnerable)) partyUp = true;
                }
            }
            if (anyParty && !partyUp) Finish(BattleOutcome.Defeat);
            else if (!hostileAlive) Finish(BattleOutcome.Victory);
        }

        /// <summary>Ends the battle immediately (e.g. scripted flee). Normally called automatically.</summary>
        public void Finish(BattleOutcome outcome)
        {
            if (IsOver) return;
            Outcome = outcome;
            if (ActiveUnit != null) { ActiveUnit.InOwnTurn = false; ActiveUnit = null; }
            Result = BattleResult.Compute(this);
            // cleanup
            foreach (var u in new List<Unit>(Units))
            {
                if (!Units.Contains(u)) continue;
                u.SelfRes = null;   // self-resurrection offers (Soulstone, Reincarnation) only last for this battle
                if ((u.Kind == UnitKind.Totem || u.Kind == UnitKind.Summon || (u.Kind == UnitKind.Pet && u.Lifetime > 0)) && u.Team == PlayerTeam && !u.Dead)
                {
                    Despawn(u, "combat ended");
                    continue;
                }
                u.Pending = null;
                u.AutoAttacking = false;
                u.AttackTarget = null;
                u.QueuedSwing = "";
                u.QueuedSwingRank = 0;
                u.Threat.Clear();
                u.ComboPoints = 0; u.ComboTarget = null;
                u.TimeDebt = 0f;
                u.InOwnTurn = false;
                u.Reactive.Clear();
                u.ExtraAttacks = 0;
                if (outcome == BattleOutcome.Victory && u.Team == PlayerTeam && u.Downed)
                {
                    u.Downed = false;
                    u.Health = 1f;
                    Emit(new CombatEvent { Type = CombatEventType.Revive, Source = u, Target = u, Amount = 1f, Reason = "combat ended" });
                }
                // auras applied by enemies end with combat (debuffs from the dead linger in WoW, but keep it tidy)
                if (outcome == BattleOutcome.Victory && u.Team == PlayerTeam)
                {
                    foreach (var a in new List<AuraInstance>(u.Auras))
                        if (a.Caster != null && a.Caster.Team != PlayerTeam && !a.IsPassive) RemoveAura(a, AuraRemoveReason.Cancelled);
                }
            }
            Emit(new CombatEvent { Type = CombatEventType.BattleEnd, Reason = outcome.ToString(), Count = Round });
            InCombat = false;
            Specials.OnBattleFinished(this);
        }

        internal void Despawn(Unit u, string reason)
        {
            if (u == null) return;
            // owned totems and summons go with their owner
            foreach (var t in new List<Unit>(u.Totems.Values)) if (t != u) Despawn(t, "owner gone");
            foreach (var s in new List<Unit>(u.Summons)) if (s != u && s.Kind != UnitKind.Pet) Despawn(s, "owner gone");
            u.Dead = true;
            RemoveUnit(u, reason);
        }
    }
}
