// CombatController — event intake and the sequential presentation queue.
//
// The rules engine resolves every action synchronously and appends CombatEvents to Battle.Events. This partial
// pulls them incrementally (Battle.TakeEvents(ref cursor)), groups them into "beats" — one action with all its
// consequences (cast → hits/heals/auras/deaths, AoE hits together), one auto-attack swing, one batch of periodic
// ticks, one move, one turn marker — and plays the beats one after another. Each beat is an iterator that yields
// seconds to wait (0 = next frame) on the presentation clock (unscaled time × speed).
//
// Commands issued by this controller (player actions, AI steps) pass an Intent so the beat knows the ability,
// target and point even when the engine emits no header event (instant abilities emit no AbilityUsed).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class CombatController
    {
        // ================================================================ data

        enum BeatKind { Marker, Action, Swing, Tick, Move, Ending, Disengage }

        sealed class Beat
        {
            public BeatKind Kind;
            public Unit Actor, Target;
            public AbilityDef Ability;
            public ItemInstance Item;
            public Vec2? Point;
            public bool FromIntent;
            public readonly List<CombatEvent> Events = new List<CombatEvent>(8);
            public readonly List<Unit> Participants = new List<Unit>(4);
            public bool HasCastStart, HasPrimary;

            public void AddParticipant(Unit u)
            {
                if (u != null && !Participants.Contains(u)) Participants.Add(u);
            }
        }

        enum IntentKind { Ability, Move, Other }

        sealed class Intent
        {
            public IntentKind Kind;
            public Unit Actor, Target;
            public AbilityDef Ability;
            public ItemInstance Item;
            public Vec2? Point;
        }

        int cursor;
        readonly Queue<Beat> queue = new Queue<Beat>();
        Beat curBeat;
        IEnumerator<float> curRoutine;
        float waitLeft;
        readonly HashSet<CombatEvent> presented = new HashSet<CombatEvent>();

        bool QueueIdle => curRoutine == null && queue.Count == 0;

        // ================================================================ intake

        /// <summary>Takes every new battle event and turns it into beats. intent = the command that produced them (or null).</summary>
        void PullEvents(Intent intent)
        {
            if (Battle == null || disposed) return;
            var events = Battle.Events;
            if (cursor > events.Count) cursor = events.Count;   // event list was trimmed: never re-present
            if (events.Count <= cursor) return;
            List<CombatEvent> batch;
            try { batch = Battle.TakeEvents(ref cursor); }
            catch (Exception e) { LogOnce("take", "TakeEvents failed: " + e.Message); cursor = events.Count; return; }
            try { Segment(batch, intent); }
            catch (Exception e)
            {
                LogOnce("segment", "Grouping combat events failed (presenting them one by one): " + e);
                foreach (var ev in batch) if (ev != null) Enqueue(Single(BeatKind.Marker, ev));
            }
        }

        void Enqueue(Beat b)
        {
            if (b != null) queue.Enqueue(b);
        }

        static Beat Single(BeatKind kind, CombatEvent e)
        {
            var b = new Beat { Kind = kind, Actor = e.Source ?? e.Target, Target = e.Target };
            b.Events.Add(e);
            return b;
        }

        void Segment(List<CombatEvent> list, Intent intent)
        {
            Beat cur = null;
            if (intent != null && intent.Kind == IntentKind.Ability && intent.Actor != null)
            {
                cur = new Beat
                {
                    Kind = BeatKind.Action, Actor = intent.Actor, Target = intent.Target, Ability = intent.Ability,
                    Item = intent.Item, Point = intent.Point, FromIntent = true, HasPrimary = true,
                };
                cur.AddParticipant(intent.Actor);
                cur.AddParticipant(intent.Target);
            }
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null) continue;
                if (IsSeparator(e))
                {
                    Close(ref cur);
                    Enqueue(Single(e.Type == CombatEventType.Move ? BeatKind.Move : BeatKind.Marker, e));
                    continue;
                }
                if (cur != null && Joins(cur, e)) { Add(cur, e); continue; }
                Close(ref cur);
                cur = Open(list, i);
                Add(cur, e);
            }
            Close(ref cur);
        }

        void Close(ref Beat b)
        {
            if (b != null && b.Events.Count > 0) Enqueue(b);
            b = null;
        }

        static void Add(Beat b, CombatEvent e)
        {
            b.Events.Add(e);
            if (e.Type == CombatEventType.CastStart && e.Source == b.Actor) b.HasCastStart = true;
            if (e.Source == b.Actor && IsPrimary(e)) b.HasPrimary = true;
            if (e.Source == null || b.Participants.Contains(e.Source)) b.AddParticipant(e.Target);
            if (b.Ability == null && b.Kind == BeatKind.Action && e.Source == b.Actor && IsAbilityCarrier(e))
                b.Ability = b.Actor != null ? b.Actor.Db?.Ability(e.AbilityId) : null;
        }

        /// <summary>Events that always stand alone (turn structure, movement, continuing casts).</summary>
        static bool IsSeparator(CombatEvent e)
        {
            switch (e.Type)
            {
                case CombatEventType.TurnStart:
                case CombatEventType.TurnEnd:
                case CombatEventType.RoundStart:
                case CombatEventType.TurnSkipped:
                case CombatEventType.BattleStart:
                case CombatEventType.BattleEnd:
                case CombatEventType.Initiative:
                case CombatEventType.Move:
                    return true;
                case CombatEventType.CastStart:
                    return e.Reason == "continuing";
                default:
                    return false;
            }
        }

        static bool IsOutcome(CombatEventType t)
        {
            switch (t)
            {
                case CombatEventType.Damage:
                case CombatEventType.Heal:
                case CombatEventType.Miss:
                case CombatEventType.Dodge:
                case CombatEventType.Parry:
                case CombatEventType.Block:
                case CombatEventType.Resist:
                case CombatEventType.Absorb:
                case CombatEventType.Immune:
                case CombatEventType.Evade:
                    return true;
                default:
                    return false;
            }
        }

        static bool IsPeriodicLike(CombatEvent e) =>
            (IsOutcome(e.Type) && e.Periodic) || (e.Type == CombatEventType.Heal && e.Reason == "regen") ||
            (e.Type == CombatEventType.ResourceChange && e.Periodic);

        /// <summary>Events by which the actor visibly does something (as opposed to consequences such as resource or threat changes).</summary>
        static bool IsPrimary(CombatEvent e)
        {
            switch (e.Type)
            {
                case CombatEventType.Damage:
                case CombatEventType.Miss:
                case CombatEventType.Dodge:
                case CombatEventType.Parry:
                case CombatEventType.Block:
                case CombatEventType.Resist:
                case CombatEventType.Immune:
                case CombatEventType.Evade:
                    return !e.Periodic;
                case CombatEventType.Heal:
                    return !e.Periodic && e.Reason != "regen";
                case CombatEventType.AuraApplied:
                case CombatEventType.Summon:
                case CombatEventType.Teleport:
                case CombatEventType.Charge:
                case CombatEventType.Knockback:
                case CombatEventType.Dispel:
                case CombatEventType.AbilityUsed:
                case CombatEventType.CastStart:
                case CombatEventType.CastComplete:
                case CombatEventType.ChannelTick:
                case CombatEventType.Taunt:
                case CombatEventType.Revive:
                case CombatEventType.SwingQueued:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Events whose AbilityId names the ability being used (not items, auras or procs of other units).</summary>
        static bool IsAbilityCarrier(CombatEvent e)
        {
            if (string.IsNullOrEmpty(e.AbilityId) || e.AutoAttack || e.Periodic) return false;
            switch (e.Type)
            {
                case CombatEventType.ItemCreated:
                case CombatEventType.ItemConsumed:
                case CombatEventType.Threat:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>Does event e belong to the open beat?</summary>
        static bool Joins(Beat b, CombatEvent e)
        {
            var t = e.Type;
            switch (b.Kind)
            {
                case BeatKind.Action:
                    if (b.FromIntent)
                    {
                        // everything the command caused belongs to it (procs, reactions, deaths) until a separator;
                        // only a second, different cast start (e.g. a proc'd cast) opens a new beat
                        if (t == CombatEventType.CastStart && e.Source == b.Actor && b.HasCastStart) return false;
                        return true;
                    }
                    if (IsOutcome(t) && e.AutoAttack) return false;
                    if (IsOutcome(t) && e.Periodic && e.Source != b.Actor) return false;
                    if (t == CombatEventType.CastStart)
                        return e.Source == b.Actor && !b.HasCastStart && !b.HasPrimary &&
                               (b.Ability == null || b.Ability.id == e.AbilityId);
                    if (t == CombatEventType.CastComplete || t == CombatEventType.ChannelTick || t == CombatEventType.AbilityUsed ||
                        t == CombatEventType.SwingQueued)
                        return e.Source == b.Actor;
                    if (e.Source == null || b.Participants.Contains(e.Source)) return true;
                    return !IsPrimary(e) && e.Target != null && b.Participants.Contains(e.Target);

                case BeatKind.Swing:
                    if (IsOutcome(t) && (e.AutoAttack || e.Periodic)) return false;
                    if (t == CombatEventType.CastStart || t == CombatEventType.CastComplete || t == CombatEventType.ChannelTick ||
                        t == CombatEventType.AbilityUsed || t == CombatEventType.SwingQueued)
                        return false;
                    if (e.Source == null || b.Participants.Contains(e.Source)) return true;
                    return !IsPrimary(e) && e.Target != null && b.Participants.Contains(e.Target);

                case BeatKind.Tick:
                    if (IsPeriodicLike(e)) return true;
                    if (IsOutcome(t)) return false;   // a new action's hit (or an auto attack)
                    switch (t)
                    {
                        case CombatEventType.CastStart:
                        case CombatEventType.CastComplete:
                        case CombatEventType.ChannelTick:
                        case CombatEventType.AbilityUsed:
                        case CombatEventType.SwingQueued:
                        case CombatEventType.AutoAttackToggled:
                        case CombatEventType.Summon:
                        case CombatEventType.Teleport:
                        case CombatEventType.Charge:
                        case CombatEventType.Knockback:
                        case CombatEventType.Dispel:
                        case CombatEventType.Taunt:
                        case CombatEventType.CastFailed:
                            return false;
                        case CombatEventType.AuraApplied:
                            // a DoT tick's proc may apply an aura; a pending cast resolving applies one as its effect
                            return e.Source == null || e.Source == e.Target;
                        default:
                            return true;   // aura expiry, deaths, regen, absorbs, logs
                    }
                default:
                    return false;
            }
        }

        /// <summary>Opens the beat for list[i] (no beat was open or it did not fit).</summary>
        Beat Open(List<CombatEvent> list, int i)
        {
            var e = list[i];
            if (IsOutcome(e.Type) && e.AutoAttack && e.Source != null)
            {
                var s = new Beat { Kind = BeatKind.Swing, Actor = e.Source, Target = e.Target };
                s.AddParticipant(e.Source);
                s.AddParticipant(e.Target);
                return s;
            }
            if (IsPeriodicLike(e)) return new Beat { Kind = BeatKind.Tick, Actor = e.Source ?? e.Target };

            var b = new Beat { Kind = BeatKind.Action, Actor = e.Source ?? e.Target };
            b.Target = e.Target != b.Actor ? e.Target : null;
            b.AddParticipant(b.Actor);
            b.AddParticipant(e.Target);
            b.Ability = LookAheadAbility(list, i, b.Actor);
            return b;
        }

        /// <summary>The ability an actor's group of events is about (e.g. a pending cast resolving at turn start).</summary>
        AbilityDef LookAheadAbility(List<CombatEvent> list, int i, Unit actor)
        {
            if (actor == null) return null;
            var db = Db;
            for (int j = i; j < list.Count; j++)
            {
                var e = list[j];
                if (e == null) continue;
                if (IsSeparator(e)) break;
                if (j > i && IsOutcome(e.Type) && (e.AutoAttack || (!e.Periodic && e.Source != null && e.Source != actor))) break;
                if (e.Source == actor && IsAbilityCarrier(e)) return db?.Ability(e.AbilityId);
            }
            return null;
        }

        // ================================================================ runner

        /// <summary>Plays queued beats on the presentation clock. A beat that throws is logged once and skipped.</summary>
        void RunQueue(float dt)
        {
            float avail = dt;
            int guard = 0;
            while (guard++ < 400 && !disposed)
            {
                if (curRoutine == null)
                {
                    if (queue.Count == 0) return;
                    StartBeat(queue.Dequeue());
                    waitLeft = 0f;
                    if (curRoutine == null) continue;
                }
                if (waitLeft > 0f)
                {
                    if (avail >= waitLeft) { avail -= waitLeft; waitLeft = 0f; }
                    else { waitLeft -= avail; return; }
                }
                bool more;
                try { more = curRoutine.MoveNext(); }
                catch (Exception e)
                {
                    LogOnce("beat:" + (curBeat != null ? curBeat.Kind.ToString() : "?") + ":" + e.GetType().Name,
                        "Presenting combat events failed (skipped): " + e);
                    more = false;
                }
                if (disposed) return;
                if (!more) { EndBeat(); continue; }
                float v = curRoutine.Current;
                if (v > 0f) waitLeft = v;
                else return;   // 0 = continue next frame
            }
        }

        void StartBeat(Beat b)
        {
            curBeat = b;
            presented.Clear();
            ResetCtx(b);
            IEnumerator<float> r = null;
            try
            {
                switch (b.Kind)
                {
                    case BeatKind.Marker: r = PresentMarker(b); break;
                    case BeatKind.Action: r = PresentAction(b); break;
                    case BeatKind.Swing: r = PresentSwing(b); break;
                    case BeatKind.Tick: r = PresentTick(b); break;
                    case BeatKind.Move: r = PresentMove(b); break;
                    case BeatKind.Ending: r = PresentEnding(); break;
                    case BeatKind.Disengage: r = PresentDisengage(); break;
                }
            }
            catch (Exception e) { LogOnce("startbeat", "Starting a combat beat failed: " + e); }
            curRoutine = r;
            if (r == null) EndBeat();
        }

        void EndBeat()
        {
            var b = curBeat;
            curBeat = null;
            curRoutine = null;
            waitLeft = 0f;
            if (b == null) return;
            // every event is announced exactly once, even when its presenter skipped or failed
            for (int i = 0; i < b.Events.Count; i++) Announce(b.Events[i]);
            if (curChannelBeam >= 0 && b.Kind == BeatKind.Action) { FxSystem.StopBeam(curChannelBeam); curChannelBeam = -1; }
        }

        // ================================================================ deferred (non-blocking) work

        enum DeferredKind { RemoveView }

        struct Deferred
        {
            public float Time;
            public DeferredKind Kind;
            public Unit Unit;
        }

        readonly List<Deferred> deferred = new List<Deferred>();
        readonly HashSet<Unit> removedViews = new HashSet<Unit>();

        void Defer(float seconds, DeferredKind kind, Unit u)
        {
            deferred.Add(new Deferred { Time = seconds, Kind = kind, Unit = u });
        }

        void RunDeferred(float dt)
        {
            for (int i = deferred.Count - 1; i >= 0; i--)
            {
                var d = deferred[i];
                d.Time -= dt;
                if (d.Time > 0f) { deferred[i] = d; continue; }
                deferred.RemoveAt(i);
                try
                {
                    if (d.Kind == DeferredKind.RemoveView && d.Unit != null && !d.Unit.IsAlive) RemoveViewNow(d.Unit);
                }
                catch (Exception e) { LogOnce("deferred", "Deferred combat work failed: " + e.Message); }
            }
        }
    }
}
