// CombatController — beat presenters and the CombatEvent → visual mapping (UnitView animations, FxSystem,
// FloatingText, Sfx, camera), persistent state visuals (stealth, polymorph, control tints, pending-cast glows and
// channel beams) and the formatted combat log.
//
// Timing (seconds at speed 1; everything is scaled by Time.timeScale = PresentationSpeed):
//   melee   lunge → blow lands at UnitView.AttackHitTime (0.22) → hits/text → hold
//   ranged  shoot → release at ShootReleaseTime (0.2) → projectile flight → hits → hold 0.4
//   spells  cast pose → release at CastReleaseTime (0.45) → bolt flight / burst / sparkles → hits → hold 0.45
//   ticks   all periodic events of one turn start together, small text, 0.4
//   moves   UnitView.MoveAlong at 4.6 m/s, wait for arrival (camera follows)
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
        enum Delivery { None, Melee, Ranged, Wand, Bolt, OnTarget, Self, AreaCaster, AreaPoint, Cone, Line, Channel, Tick }

        const float MoveSpeed = 4.6f;
        const float FearMoveSpeed = 5.4f;
        const float ChargeSpeed = 15f;

        // presentation context of the running beat
        Unit ctxActor;
        Delivery ctxDelivery;
        Unit ctxImpactDrawnFor;
        bool ctxPendingCast;
        int curChannelBeam = -1;
        Unit curChannelFrom, curChannelTo;

        UnitView activeRingView;
        bool startedCombatMusic;
        bool selectFailed;
        float presClock;

        static readonly Color ControlColor = new Color(1f, 0.64f, 0.36f);
        static readonly Color DebuffTextColor = new Color(1f, 0.74f, 0.45f);
        static readonly Color BuffTextColor = new Color(0.66f, 0.95f, 0.72f);
        static readonly Color MutedText = new Color(0.82f, 0.78f, 0.9f);
        static readonly Color InterruptColor = new Color(1f, 0.86f, 0.38f);
        static readonly Color SelfCostColor = new Color(1f, 0.55f, 0.5f);
        static readonly Color GoldText = new Color(1f, 0.85f, 0.42f);
        static readonly Color ComboColor = new Color(1f, 0.92f, 0.4f);

        void ResetCtx(Beat b)
        {
            ctxActor = b != null ? b.Actor : null;
            ctxDelivery = Delivery.None;
            ctxImpactDrawnFor = null;
            ctxPendingCast = false;
        }

        // ================================================================ announce / show

        /// <summary>Marks an event as presented: GameFlow.CombatEventPresented + combat log (no visuals).</summary>
        void Announce(CombatEvent e)
        {
            if (e == null || !presented.Add(e)) return;
            if (flow != null)
            {
                try { flow.NotifyCombatEventPresented(e); }
                catch (Exception ex) { LogOnce("notify", "CombatEventPresented handler failed: " + ex); }
            }
            AddLog(e);
        }

        /// <summary>Presents an event now: announce + its visuals. Each event is shown at most once.</summary>
        void Show(CombatEvent e)
        {
            if (e == null || presented.Contains(e)) return;
            Announce(e);
            try { Visual(e); }
            catch (Exception ex) { LogOnce("visual:" + e.Type, "Visual for " + e.Type + " failed: " + ex.Message); }
        }

        void ShowAll(Beat b)
        {
            for (int i = 0; i < b.Events.Count; i++) Show(b.Events[i]);
        }

        /// <summary>Shows the beat's events in order up to and including e (keeps the presentation order = event order).</summary>
        void ShowUpTo(Beat b, CombatEvent e)
        {
            for (int i = 0; i < b.Events.Count; i++)
            {
                var x = b.Events[i];
                Show(x);
                if (x == e) return;
            }
        }

        // ================================================================ presenters

        IEnumerator<float> PresentMarker(Beat b)
        {
            var e = b.Events[0];
            Show(e);
            float wait = 0f;
            switch (e.Type)
            {
                case CombatEventType.TurnStart:
                    wait = e.Source != null && !Battle.IsAIControlled(e.Source) ? 0.2f : 0.25f;
                    break;
                case CombatEventType.TurnSkipped: wait = 0.6f; break;
                case CombatEventType.BattleStart: wait = 0.3f; break;
                case CombatEventType.CastStart: wait = 0.5f; break;   // continuing cast
            }
            if (wait > 0f) yield return wait;
        }

        IEnumerator<float> PresentMove(Beat b)
        {
            var e = b.Events[0];
            var u = e.Source ?? e.Target;
            var v = V(u);
            Show(e);
            if (v == null || e.Path == null || e.Path.Count < 2) { if (v != null) v.Teleport(ToV(e.To)); yield break; }
            bool panic = e.Reason == "Fear" || e.Reason == "Confuse";
            float speed = panic ? FearMoveSpeed : MoveSpeed;
            if (panic) FloatingText.Status(Head(u, v), e.Reason == "Fear" ? "Feared" : "Confused", ControlColor);
            v.MoveAlong(e.Path, speed);
            float len = e.Amount > 0.01f ? e.Amount : PathLength(e.Path);
            float timeout = len / speed + 1.2f;
            while (v != null && v.IsMoving && timeout > 0f)
            {
                timeout -= FrameDt;
                FocusOn(v.FeetPosition);
                yield return 0f;
            }
            var to = ToV(e.To);
            if (v != null && (v.FeetPosition - to).sqrMagnitude > 0.0025f) v.Teleport(to);
            yield return 0.06f;
        }

        IEnumerator<float> PresentTick(Beat b)
        {
            ctxDelivery = Delivery.Tick;
            bool visible = false;
            for (int i = 0; i < b.Events.Count; i++)
            {
                var e = b.Events[i];
                if ((e.Type == CombatEventType.Damage || e.Type == CombatEventType.Heal) && e.Reason != "regen" && e.Amount >= 0.5f) visible = true;
                else if (e.Type == CombatEventType.Death || e.Type == CombatEventType.Downed || e.Type == CombatEventType.Absorb) visible = true;
            }
            ShowAll(b);
            if (visible) yield return 0.4f;
        }

        Unit lastSwingActor, lastSwingTarget;
        float lastSwingClock = -10f;

        IEnumerator<float> PresentSwing(Beat b)
        {
            var head = b.Events[0];
            var actor = head.Source;
            var target = head.Target;
            // follow-up swings of one end-of-turn volley (fast weapons, off hand, extra attacks) play quicker
            bool followUp = actor == lastSwingActor && target == lastSwingTarget && presClock - lastSwingClock < 1.5f;
            lastSwingActor = actor;
            lastSwingTarget = target;
            var av = V(actor);
            var tv = V(target);
            ctxActor = actor;
            ctxDelivery = head.Ranged ? Delivery.Ranged : Delivery.Melee;
            var tp = Feet(target, tv);
            FocusIfNeeded(tp);
            if (head.Ranged)
            {
                if (av != null) av.PlayShoot(tp);
                Sfx.Play("bow", Feet(actor, av));
                yield return UnitView.ShootReleaseTime;
                var school = SwingSchool(b);
                float flight = target != null ? FxSystem.Projectile(Hand(actor, av, tp), Center(target, tv), school, "", 20f) : 0f;
                if (school != School.Physical) ctxImpactDrawnFor = target;
                if (flight > 0f) yield return flight;
                ShowAll(b);
                lastSwingClock = presClock;
                yield return followUp ? 0.12f : 0.28f;
            }
            else
            {
                float dur = av != null ? av.PlayAttack(tp) : 0.5f;
                Sfx.Play("swing", Feet(actor, av), 0.75f, 1f);
                yield return UnitView.AttackHitTime;
                ShowAll(b);
                lastSwingClock = presClock;
                float rest = Mathf.Max(0.2f, dur - UnitView.AttackHitTime);
                yield return head.OffHand || followUp ? Mathf.Min(rest, 0.16f) : rest;
            }
        }

        IEnumerator<float> PresentAction(Beat b)
        {
            var actor = b.Actor;
            var a = b.Ability;
            var av = V(actor);
            CombatEvent castStart = null, castComplete = null, castStop = null, charge = null;
            Unit firstTarget = null;
            bool anyFromActor = false;
            var school = a != null ? a.school : School.Physical;
            bool schoolKnown = a != null;
            for (int i = 0; i < b.Events.Count; i++)
            {
                var e = b.Events[i];
                if (e.Type == CombatEventType.CastInterrupted && e.Target == actor && actor != null) castStop = e;
                if (actor == null || e.Source != actor) continue;
                switch (e.Type)
                {
                    case CombatEventType.CastStart: if (castStart == null) castStart = e; break;
                    case CombatEventType.CastComplete: castComplete = e; break;
                    case CombatEventType.CastFailed: castStop = e; break;
                    case CombatEventType.Charge: if (charge == null) charge = e; break;
                }
                if (IsPrimary(e)) anyFromActor = true;
                if (firstTarget == null && e.Target != null && e.Target != actor &&
                    (IsOutcome(e.Type) || e.Type == CombatEventType.AuraApplied || e.Type == CombatEventType.Knockback ||
                     e.Type == CombatEventType.Dispel || e.Type == CombatEventType.Taunt || e.Type == CombatEventType.Revive))
                    firstTarget = e.Target;
                if (!schoolKnown && IsOutcome(e.Type) && !e.AutoAttack) { school = e.School; schoolKnown = true; }
            }
            var target = b.Target ?? firstTarget;
            var tv = V(target);
            var col = Ui.SchoolColor(school);
            var actorFeet = Feet(actor, av);
            ctxActor = actor;

            // consequences only (aura expiry, resource changes, logs...)
            if (actor == null || (!b.HasPrimary && !anyFromActor))
            {
                bool vis = HasVisibleText(b);
                ShowAll(b);
                if (vis) yield return 0.3f;
                yield break;
            }

            // auto attack toggles and queued "next swing" abilities: no wind-up, the swing comes at the end of the turn
            // (a queued Heroic Strike resolving inside the swing has outcome events and is presented as a strike)
            if (a != null && (a.autoAttack || a.nextSwing) && castStart == null && !HasOutcomeFrom(b, actor))
            {
                if (av != null && target != null && target != actor) av.FaceTowards(Feet(target, tv));
                ShowAll(b);
                yield return a.nextSwing ? 0.25f : 0.12f;
                yield break;
            }

            if (target != null && target != actor) FocusIfNeeded(Center(target, tv));
            if (a != null && ShouldAnnounce(actor, a))
                FloatingText.Spawn(Head(actor, av) + new Vector2(0f, 0.45f), a.name, Color.Lerp(col, Color.white, 0.55f), 0.85f);

            // a cast that does not fit this turn: telegraph it and keep the glow until it resolves or is interrupted
            if (castStart != null && castStart.Reason != "channel" && castComplete == null && castStop == null)
            {
                if (av != null) av.PlayCast(col);
                Sfx.Play("cast_start", actorFeet);
                ctxPendingCast = true;
                ShowAll(b);
                yield return 0.85f;
                yield break;
            }

            // leading motion (Charge, Intercept): run in first, then strike
            if (charge != null)
            {
                ShowUpTo(b, charge);
                float timeout = Vec2.Distance(charge.From, charge.To) / ChargeSpeed + 0.6f;
                while (av != null && av.IsMoving && timeout > 0f)
                {
                    timeout -= FrameDt;
                    FocusOn(av.FeetPosition);
                    yield return 0f;
                }
                actorFeet = Feet(actor, av);
            }

            var d = charge != null ? Delivery.Melee : Classify(a, b, actor, target, av, tv);
            ctxDelivery = d;

            // cast time resolved within this turn: a short wind-up with the casting glow
            if (castStart != null && d != Delivery.Channel)
            {
                ShowUpTo(b, castStart);
                Sfx.Play("cast_start", actorFeet, 0.8f, 1f);
                float t = 0f;
                const float wind = 0.32f;
                while (t < wind)
                {
                    t += FrameDt;
                    if (av != null) av.SetCasting(col, Mathf.Clamp01(t / wind));
                    yield return 0f;
                }
                if (av != null) av.StopCasting();
            }

            switch (d)
            {
                case Delivery.Melee:
                {
                    var tp = Feet(target, tv);
                    float dur = av != null ? av.PlayAttack(tp) : 0.5f;
                    Sfx.Play("swing", actorFeet);
                    yield return UnitView.AttackHitTime;
                    StopCastVis(actor);
                    ShowAll(b);
                    yield return Mathf.Max(0.3f, dur - UnitView.AttackHitTime) + 0.05f;
                    break;
                }
                case Delivery.Ranged:
                case Delivery.Wand:
                {
                    var tp = Feet(target, tv);
                    if (av != null) av.PlayShoot(tp);
                    if (d == Delivery.Wand) Sfx.Play("cast_start", actorFeet, 0.6f, 1.25f);
                    else Sfx.Play("bow", actorFeet);
                    yield return UnitView.ShootReleaseTime;
                    StopCastVis(actor);
                    float flight = 0f;
                    if (target != null && target != actor)
                    {
                        var ps = d == Delivery.Wand ? school : School.Physical;
                        flight = FxSystem.Projectile(Hand(actor, av, tp), Center(target, tv), ps, "", d == Delivery.Wand ? 15f : 20f);
                        if (ps != School.Physical) ctxImpactDrawnFor = target;
                    }
                    if (flight > 0f) yield return flight;
                    ShowAll(b);
                    yield return 0.32f;
                    break;
                }
                case Delivery.Bolt:
                {
                    if (av != null) av.PlayCast(col);
                    if (castStart == null) Sfx.Play("cast_start", actorFeet, 0.7f, 1.1f);
                    yield return UnitView.CastReleaseTime;
                    StopCastVis(actor);
                    float flight = 0f;
                    if (target != null)
                    {
                        var tp = Feet(target, tv);
                        flight = FxSystem.Projectile(Hand(actor, av, tp), Center(target, tv), school, "", 13f);
                        ctxImpactDrawnFor = target;
                    }
                    if (flight > 0f) yield return flight;
                    ShowAll(b);
                    yield return 0.38f;
                    break;
                }
                case Delivery.OnTarget:
                {
                    if (av != null)
                    {
                        if (target != null) av.FaceTowards(Feet(target, tv));
                        av.PlayCast(col);
                    }
                    if (castStart == null) Sfx.Play("cast_start", actorFeet, 0.6f, 1.15f);
                    yield return UnitView.CastReleaseTime;
                    StopCastVis(actor);
                    ShowAll(b);
                    PulseIfGroupBuff(b, actor, av, col);
                    yield return 0.38f;
                    break;
                }
                case Delivery.Self:
                {
                    if (av != null) av.PlayCast(col);
                    yield return UnitView.CastReleaseTime * 0.75f;
                    StopCastVis(actor);
                    ShowAll(b);
                    PulseIfGroupBuff(b, actor, av, col);
                    yield return 0.32f;
                    break;
                }
                case Delivery.AreaCaster:
                {
                    bool physical = school == School.Physical;
                    if (av != null)
                    {
                        if (physical) av.PlayAttack(actorFeet + new Vector2(av.Facing * 0.6f, 0f));
                        else av.PlayCast(col);
                    }
                    if (physical) Sfx.Play("swing", actorFeet);
                    yield return physical ? UnitView.AttackHitTime : UnitView.CastReleaseTime;
                    StopCastVis(actor);
                    FxSystem.Burst(Feet(actor, av), AreaRadius(actor, a), school);
                    Sfx.Impact(school, actorFeet, false);
                    ShowAll(b);
                    yield return 0.48f;
                    break;
                }
                case Delivery.AreaPoint:
                {
                    var c = AreaCenter(b, a, actor, target);
                    if (av != null) { av.FaceTowards(c); av.PlayCast(col); }
                    yield return UnitView.CastReleaseTime;
                    StopCastVis(actor);
                    FocusIfNeeded(c);
                    FxSystem.Burst(c, AreaRadius(actor, a), school);
                    Sfx.Impact(school, c, false);
                    ShowAll(b);
                    yield return 0.48f;
                    break;
                }
                case Delivery.Cone:
                case Delivery.Line:
                {
                    var dir = AimDirection(b, actor, av, target, tv);
                    bool physical = school == School.Physical;
                    if (av != null)
                    {
                        if (physical) av.PlayAttack(actorFeet + dir);
                        else { av.FaceTowards(actorFeet + dir); av.PlayCast(col); }
                    }
                    yield return physical ? UnitView.AttackHitTime : UnitView.CastReleaseTime;
                    StopCastVis(actor);
                    float r = Mathf.Max(1.5f, AreaRadius(actor, a));
                    var mid = actorFeet + dir * (r * 0.5f);
                    FxSystem.Burst(mid, r * (d == Delivery.Cone ? 0.45f : 0.3f), school);
                    if (physical) FxSystem.Slash(mid + new Vector2(0f, 0.6f), dir);
                    Sfx.Impact(school, mid, false);
                    ShowAll(b);
                    yield return 0.5f;
                    break;
                }
                case Delivery.Channel:
                {
                    if (av != null) av.PlayCast(col);
                    if (castStart != null) { ShowUpTo(b, castStart); Sfx.Play("cast_start", actorFeet, 0.7f, 0.9f); }
                    yield return UnitView.CastReleaseTime * 0.6f;
                    bool area = a != null && a.area.shape != AreaShape.None;
                    var areaC = area ? AreaCenter(b, a, actor, target) : Vector2.zero;
                    float rad = area ? AreaRadius(actor, a) : 0f;
                    // a channel continuing from last turn keeps its beam
                    int beam = -1;
                    if (casting.TryGetValue(actor, out var cvis) && cvis.Beam >= 0) { beam = cvis.Beam; cvis.Beam = -1; }
                    if (beam < 0 && !area && target != null && target != actor)
                        beam = FxSystem.Beam(Hand(actor, av, Feet(target, tv)), Center(target, tv), school, 60f);
                    curChannelBeam = beam;
                    curChannelFrom = actor;
                    curChannelTo = target;
                    if (av != null) av.SetCasting(col, 0.6f);
                    bool firstTick = true;
                    for (int i = 0; i < b.Events.Count; i++)
                    {
                        var e = b.Events[i];
                        if (e == castComplete || e == castStop) break;   // shown (in order) once the beam is down
                        if (e.Type == CombatEventType.ChannelTick && e.Source == actor)
                        {
                            if (!firstTick) yield return 0.32f;
                            firstTick = false;
                            if (area) { FxSystem.Burst(areaC, rad * 0.75f, school); Sfx.Impact(school, areaC, false); }
                        }
                        Show(e);
                    }
                    yield return 0.3f;
                    if (castComplete != null || castStop != null)
                    {
                        if (curChannelBeam >= 0) { FxSystem.StopBeam(curChannelBeam); curChannelBeam = -1; }
                        StopCastVis(actor);
                        if (av != null) av.StopCasting();
                        ShowAll(b);
                        yield return 0.2f;
                    }
                    else
                    {
                        // the channel continues next turn: glow and beam stay until it completes or is interrupted
                        float total = castStart != null ? castStart.Seconds : (a != null ? a.castTime : 0f);
                        BeginCastVis(actor, a, castStart != null ? castStart.Name : (a != null ? a.name : ""), total, true, curChannelBeam, target);
                        curChannelBeam = -1;
                        ShowAll(b);
                        yield return 0.3f;
                    }
                    break;
                }
                default:
                {
                    StopCastVis(actor);
                    bool vis = HasVisibleText(b);
                    ShowAll(b);
                    if (vis) yield return 0.3f;
                    break;
                }
            }
        }

        IEnumerator<float> PresentEnding()
        {
            HidePlayerTurnVisuals();
            if (activeRingView != null) { activeRingView.SetActiveTurn(false); activeRingView = null; }
            var pos = BannerPosition();
            // the "Victory!" / "Defeat" words are the HUD's banner (ToastsHud, on the BattleEnd event); the world gets
            // the sparkles and the sound only, so the outcome is not announced twice
            switch (Battle.Outcome)
            {
                case BattleOutcome.Victory:
                    FxSystem.Sparkles(pos - new Vector2(0f, 0.6f), GoldText, 18);
                    Sfx.Play("quest");
                    break;
                case BattleOutcome.Defeat:
                    Sfx.Play("death", null, 0.9f, 0.7f);
                    break;
                default:
                    FloatingText.Spawn(pos, "The fight is over", MutedText, 1.5f, false);
                    Sfx.Play("ui_close");
                    break;
            }
            yield return 1.8f;
            endingDone = true;
        }

        IEnumerator<float> PresentDisengage()
        {
            HidePlayerTurnVisuals();
            var u = Battle.ActiveUnit;
            if (u == null || u.Team != Battle.PlayerTeam)
                foreach (var m in Battle.Units) if (m.Team == Battle.PlayerTeam && m.IsCharacter && m.IsAlive) { u = m; break; }
            if (u != null) FloatingText.Spawn(Head(u, V(u)) + new Vector2(0f, 0.5f), "Disengaged", MutedText, 1.2f);
            Sfx.Play("ui_close");
            yield return 0.7f;
            disengageDone = true;
        }

        // ================================================================ event → visual

        void Visual(CombatEvent e)
        {
            switch (e.Type)
            {
                case CombatEventType.TurnStart: VisTurnStart(e); break;
                case CombatEventType.TurnSkipped:
                {
                    var u = e.Source ?? e.Target;
                    string word = e.Reason == "Surprised" ? "Surprised!" : StateWord(e.Reason) ?? "Turn lost";
                    FloatingText.Status(Head(u, V(u)), word, ControlColor);
                    break;
                }
                case CombatEventType.BattleStart:
                    if (Music.Mood != "combat") { Music.Play("combat", 1.5f); startedCombatMusic = true; }
                    break;
                case CombatEventType.Damage: VisDamage(e); break;
                case CombatEventType.Heal: VisHeal(e); break;
                case CombatEventType.Miss: VisAvoid(e, "Miss", true); break;
                case CombatEventType.Dodge: VisAvoid(e, "Dodge", true); break;
                case CombatEventType.Parry: VisAvoid(e, "Parry", true); break;
                case CombatEventType.Evade: VisAvoid(e, "Evade", true); break;
                case CombatEventType.Block:
                    VisAvoid(e, "Block", false);
                    Sfx.Play("hit_physical", Feet(e.Target, V(e.Target)), 0.6f, 1.35f);
                    break;
                case CombatEventType.Resist: VisAvoid(e, "Resist", false); break;
                case CombatEventType.Immune: VisAvoid(e, "Immune", false); break;
                case CombatEventType.Absorb:
                {
                    var tv = V(e.Target);
                    int n = Mathf.RoundToInt(e.Amount);
                    FloatingText.Miss(Head(e.Target, tv), n > 0 ? "Absorb " + n : "Absorb");
                    FxSystem.Sparkles(Center(e.Target, tv), new Color(0.75f, 0.9f, 1f), 6);
                    break;
                }
                case CombatEventType.AuraApplied: VisAuraApplied(e); break;
                case CombatEventType.AuraStack:
                {
                    if (e.Reason == "charges") break;
                    var def = Db?.Aura(e.AuraId);
                    if (def == null || def.hidden || Throttled(e.Target, e.AuraId, 1.5f)) break;
                    FloatingText.Spawn(Head(e.Target, V(e.Target)), def.name + " x" + e.Count,
                        def.kind == AuraKind.Debuff ? DebuffTextColor : BuffTextColor, 0.72f);
                    break;
                }
                case CombatEventType.AuraRemoved: VisAuraRemoved(e, false); break;
                case CombatEventType.AuraBroken: VisAuraRemoved(e, true); break;
                case CombatEventType.Dispel:
                {
                    var tv = V(e.Target);
                    FloatingText.Spawn(Head(e.Target, tv), (string.IsNullOrEmpty(e.Name) ? "Dispelled" : e.Name + " removed"), MutedText, 0.72f);
                    FxSystem.Sparkles(Center(e.Target, tv), Color.white, 8);
                    Sfx.Play("buff", Feet(e.Target, tv), 0.5f, 1.3f);
                    break;
                }
                case CombatEventType.CastStart:
                    if (ctxPendingCast && e.Source != null)
                        BeginCastVis(e.Source, Db?.Ability(e.AbilityId), e.Name, e.Seconds, e.Reason == "channel", -1, e.Target);
                    else if (e.Reason == "continuing") VisContinueCast(e);
                    break;
                case CombatEventType.CastComplete: StopCastVis(e.Source); break;
                case CombatEventType.CastInterrupted:
                {
                    var u = e.Target ?? e.Source;
                    StopCastVis(u);
                    var v = V(u);
                    FloatingText.Status(Head(u, v), "Interrupted", InterruptColor);
                    FxSystem.Sparkles(Center(u, v), Ui.SchoolColor(e.School), 8);
                    Sfx.Play("debuff", Feet(u, v), 0.8f, 1.2f);
                    break;
                }
                case CombatEventType.CastFailed:
                {
                    StopCastVis(e.Source);
                    FloatingText.Spawn(Head(e.Source, V(e.Source)), "Failed", MutedText, 0.75f);
                    break;
                }
                case CombatEventType.SwingQueued:
                    FloatingText.Spawn(Head(e.Source, V(e.Source)) + new Vector2(0f, 0.3f), e.Name, GoldText, 0.75f);
                    break;
                case CombatEventType.AutoAttackToggled:
                    if (e.Amount > 0f && e.Target != null)
                    {
                        var sv = V(e.Source);
                        if (sv != null) sv.FaceTowards(Feet(e.Target, V(e.Target)));
                    }
                    break;
                case CombatEventType.ResourceChange: VisResource(e); break;
                case CombatEventType.ComboPoints:
                    if (e.Count > 0 && e.Source != null)
                        FloatingText.Spawn(Head(e.Source, V(e.Source)), Mathf.RoundToInt(e.Amount) + " combo", ComboColor, 0.7f);
                    break;
                case CombatEventType.Teleport: VisTeleport(e); break;
                case CombatEventType.Charge:
                {
                    var v = V(e.Source);
                    var from = ToV(e.From);
                    var to = ToV(e.To);
                    FxSystem.Puff(from, new Color(0.85f, 0.78f, 0.65f, 0.8f), 0.8f);
                    if (v != null) v.MoveAlong(new List<Vector2>(2) { from, to }, ChargeSpeed);
                    break;
                }
                case CombatEventType.Knockback:
                {
                    var v = V(e.Target);
                    if (v != null) v.Knockback(ToV(e.To), 0.35f);
                    break;
                }
                case CombatEventType.Summon: VisSummon(e); break;
                case CombatEventType.Despawn: VisDespawn(e); break;
                case CombatEventType.Death: VisDeath(e); break;
                case CombatEventType.Downed:
                {
                    var u = e.Target;
                    var v = V(u);
                    StopCastVis(u);
                    if (v != null) v.PlayDowned();
                    FloatingText.Status(Head(u, v), "Downed!", Ui.Bad);
                    Sfx.Play("death", Feet(u, v), 0.6f, 1.2f);
                    break;
                }
                case CombatEventType.Revive: VisRevive(e); break;
                case CombatEventType.Taunt:
                {
                    var v = V(e.Target);
                    FloatingText.Status(Head(e.Target, v), "Taunted!", ControlColor);
                    Sfx.Play("debuff", Feet(e.Target, v), 0.7f, 0.9f);
                    break;
                }
                case CombatEventType.ItemCreated:
                    if (e.Source != null)
                        FloatingText.Spawn(Head(e.Source, V(e.Source)), "+" + e.Name + (e.Count > 1 ? " x" + e.Count : ""), BuffTextColor, 0.7f);
                    break;
                case CombatEventType.CooldownReset:
                    if (e.Source != null) FloatingText.Spawn(Head(e.Source, V(e.Source)), "Cooldowns reset", Ui.Time, 0.7f);
                    break;
                // presented through the log/HUD only
                case CombatEventType.BattleEnd:
                case CombatEventType.RoundStart:
                case CombatEventType.TurnEnd:
                case CombatEventType.AuraRefreshed:
                case CombatEventType.AbilityUsed:
                case CombatEventType.ChannelTick:
                case CombatEventType.Threat:
                case CombatEventType.TargetChanged:
                case CombatEventType.ItemConsumed:
                case CombatEventType.Initiative:
                case CombatEventType.Log:
                case CombatEventType.Move:
                    break;
                default:
                    LogOnce("unknown:" + e.Type, "No visual for combat event type " + e.Type + " (log only).");
                    break;
            }
        }

        void VisTurnStart(CombatEvent e)
        {
            var u = e.Source ?? e.Target;
            if (activeRingView != null) activeRingView.SetActiveTurn(false);
            activeRingView = null;
            var v = V(u);
            if (v != null)
            {
                v.SetActiveTurn(true);
                activeRingView = v;
                FocusOn(v.FeetPosition);
            }
            bool player = u != null && u.Team == Battle.PlayerTeam && !Battle.IsAIControlled(u);
            if (player)
            {
                if (flow != null && !selectFailed)
                {
                    try { flow.Select(u); }
                    catch (Exception ex) { selectFailed = true; LogOnce("select", "GameFlow.Select failed: " + ex.Message); }
                }
                Sfx.Play("ui_open", null, 0.45f, 1.3f);
            }
            if (u != null && e.Reason == "self-resurrection offer")
                FloatingText.Status(Head(u, v), (u.SelfRes != null ? u.SelfRes.Name : "Resurrection") + "?", GoldText);
        }

        void VisDamage(CombatEvent e)
        {
            var t = e.Target;
            if (t == null) return;
            var tv = V(t);
            var head = Head(t, tv);
            var center = Center(t, tv);
            int amt = Mathf.RoundToInt(e.Amount);
            if (e.Reason == "self")
            {
                if (amt > 0) FloatingText.Spawn(head, "-" + amt, SelfCostColor, 0.75f);
                return;
            }
            if (amt <= 0)
            {
                if (e.Absorbed > 0f && tv != null && !tv.IsDead) tv.PlayHit();
                return;
            }
            if (tv != null && !tv.IsDead) tv.PlayHit();
            if (e.Periodic || ctxDelivery == Delivery.Tick)
            {
                var pc = e.School == School.Physical ? new Color(1f, 0.88f, 0.84f) : Color.Lerp(Color.white, Ui.SchoolColor(e.School), 0.6f);
                FloatingText.Spawn(head, amt.ToString(), pc, e.Crit ? 0.95f : 0.78f, e.Crit);
                FxSystem.Puff(center, Ui.SchoolColor(e.School), 0.45f);
                Sfx.Play(Sfx.ImpactId(e.School), center, 0.4f, 1.1f);
                return;
            }
            FloatingText.Damage(head, amt, e.Crit, e.School);
            bool melee = (e.AutoAttack && !e.Ranged) || (ctxDelivery == Delivery.Melee && e.Source == ctxActor);
            if (melee && e.Source != null)
            {
                var dir = center - Center(e.Source, V(e.Source));
                FxSystem.Slash(center, dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector2.right);
            }
            else if (ctxImpactDrawnFor != t) FxSystem.Impact(center, e.School);
            Sfx.Impact(e.School, center, e.Crit);
            if (e.Crit)
            {
                var rig = CameraRig.Instance;
                if (rig != null) rig.Shake(0.08f, 0.2f);
            }
        }

        void VisHeal(CombatEvent e)
        {
            if (e.Reason == "regen" || e.Target == null) return;
            var t = e.Target;
            var tv = V(t);
            int amt = Mathf.RoundToInt(e.Amount);
            if (e.Periodic || ctxDelivery == Delivery.Tick)
            {
                if (amt > 0) FloatingText.Spawn(Head(t, tv), "+" + amt, Ui.Good, 0.78f);
                FxSystem.Sparkles(Center(t, tv), new Color(0.6f, 1f, 0.6f), 5);
                return;
            }
            if (tv != null) FxSystem.HealSparkles(tv.FeetPosition, tv.Height);
            else FxSystem.HealSparkles(Feet(t, null));
            if (amt > 0) FloatingText.Heal(Head(t, tv), amt, e.Crit);
            Sfx.Play("heal", Feet(t, tv));
        }

        void VisAvoid(CombatEvent e, string word, bool dodge)
        {
            var t = e.Target;
            if (t == null) return;
            var tv = V(t);
            FloatingText.Miss(Head(t, tv), word);
            if (dodge)
            {
                if (tv != null && !tv.IsDead && !tv.IsDowned) tv.PlayDodge();
            }
            else FxSystem.Sparkles(Center(t, tv), Color.Lerp(Ui.SchoolColor(e.School), Color.white, 0.4f), 6);
        }

        void VisResource(CombatEvent e)
        {
            if (e.Periodic || e.Amount <= 0f || e.Target == null) return;
            var t = e.Target;
            int amt = Mathf.RoundToInt(e.Amount);
            switch (e.Resource)
            {
                case ResourceType.Rage:
                case ResourceType.Energy:
                case ResourceType.Focus:
                    if (amt >= 15) FloatingText.Resource(Head(t, V(t)), amt, e.Resource);
                    break;
                case ResourceType.Mana:
                    if (amt >= Mathf.Max(50f, t.MaxMana * 0.1f)) FloatingText.Resource(Head(t, V(t)), amt, e.Resource);
                    break;
            }
        }

        void VisTeleport(CombatEvent e)
        {
            var u = e.Source ?? e.Target;
            var v = V(u);
            var from = ToV(e.From);
            var to = ToV(e.To);
            var col = Ui.SchoolColor(School.Arcane);
            var up = new Vector2(0f, 0.7f);
            FxSystem.Puff(from + up, col, 1f);
            FxSystem.Sparkles(from + up, col, 8);
            if (v != null) v.Teleport(to);
            FxSystem.Puff(to + up, col, 1f);
            Sfx.Play("cast_start", to, 0.5f, 1.45f);
        }

        void VisSummon(CombatEvent e)
        {
            var u = e.Target;
            if (u == null) return;
            removedViews.Remove(u);
            var v = Ensure(u);
            var def = Db?.Ability(e.AbilityId);
            var col = def != null ? Ui.SchoolColor(def.school) : Ui.SchoolColor(School.Shadow);
            var p = ToV(e.To);
            if (v != null)
            {
                v.Teleport(p);
                v.SetVisible(true);
                v.PlayRevive();
                if (Mathf.Abs(u.Facing.x) > 0.1f) v.SetFacing(u.Facing.x >= 0f ? 1 : -1);
            }
            FxSystem.Puff(p + new Vector2(0f, 0.5f), col, 1.2f);
            FxSystem.Sparkles(p + new Vector2(0f, 0.8f), col, 10);
            if (e.Reason == "totem") FxSystem.GroundRing(p, 1.1f, col, 0.8f);
            Sfx.Play("buff", p);
            RefreshStateVisuals(u);
        }

        void VisDespawn(CombatEvent e)
        {
            var u = e.Source ?? e.Target;
            if (u == null) return;
            StopCastVis(u);
            var v = V(u);
            if (v != null && !v.IsDead) FxSystem.Puff(Center(u, v), new Color(0.92f, 0.9f, 1f, 0.8f), 1f);
            if (activeRingView != null && activeRingView == v) { v.SetActiveTurn(false); activeRingView = null; }
            Defer(0.3f, DeferredKind.RemoveDespawned, u);
        }

        void VisDeath(CombatEvent e)
        {
            var u = e.Target;
            if (u == null) return;
            StopCastVis(u);
            ClearPresentedAuras(u);
            var v = V(u);
            if (v != null)
            {
                v.SetPolymorphed(false);
                v.SetTint(Color.white);
                v.SetStealthed(false);
                if (activeRingView == v) { v.SetActiveTurn(false); activeRingView = null; }
                float d = v.PlayDeath();
                if (!u.IsCharacter) Defer(d + 0.25f, DeferredKind.RemoveDead, u);
            }
            Sfx.Play("death", Feet(u, v));
        }

        void VisRevive(CombatEvent e)
        {
            var u = e.Target;
            if (u == null) return;
            var v = V(u);
            if (v == null || removedViews.Contains(u))
            {
                removedViews.Remove(u);
                v = Ensure(u);
                if (v != null) v.Teleport(ToV(u.Position));
            }
            if (v != null)
            {
                v.PlayRevive();
                FxSystem.HealSparkles(v.FeetPosition, v.Height);
            }
            FloatingText.Status(Head(u, v), "Revived", GoldText);
            Sfx.Play("heal", Feet(u, v));
            RefreshStateVisuals(u);
        }

        // ================================================================ auras & state visuals

        // aura ids per unit as presented so far (multiset: one entry per instance)
        readonly Dictionary<Unit, Dictionary<string, int>> presentedAuras = new Dictionary<Unit, Dictionary<string, int>>();
        readonly Dictionary<long, float> auraTextTimes = new Dictionary<long, float>();

        void SeedPresentedAuras()
        {
            foreach (var u in Battle.Units)
                foreach (var a in u.Auras)
                    if (a != null && a.Def != null) IncAura(u, a.Def.id, 1);
            // undo what the events already recorded did, so presenting them from the start is consistent
            var ev = Battle.Events;
            for (int i = ev.Count - 1; i >= 0; i--)
            {
                var e = ev[i];
                if (e == null || e.Target == null) continue;
                if (e.Type == CombatEventType.AuraApplied) IncAura(e.Target, e.AuraId, -1);
                else if (e.Type == CombatEventType.AuraRemoved || e.Type == CombatEventType.AuraBroken) IncAura(e.Target, e.AuraId, 1);
            }
        }

        void IncAura(Unit u, string id, int delta)
        {
            if (u == null || string.IsNullOrEmpty(id)) return;
            if (!presentedAuras.TryGetValue(u, out var d)) presentedAuras[u] = d = new Dictionary<string, int>();
            d.TryGetValue(id, out var n);
            n = Math.Max(0, n + delta);
            if (n == 0) d.Remove(id); else d[id] = n;
        }

        void ClearPresentedAuras(Unit u)
        {
            if (u != null && presentedAuras.TryGetValue(u, out var d)) d.Clear();
        }

        void VisAuraApplied(CombatEvent e)
        {
            var u = e.Target;
            if (u == null) return;
            IncAura(u, e.AuraId, 1);
            RefreshStateVisuals(u);
            var def = Db?.Aura(e.AuraId);
            if (def == null || def.hidden) return;
            var v = V(u);
            string word = ControlWord(def);
            if (word != null)
            {
                FloatingText.Status(Head(u, v), word, ControlColor);
                Sfx.Play("debuff", Feet(u, v), 0.75f, 1f);
                return;
            }
            if (HasState(def, UnitState.Stealth) || HasState(def, UnitState.Invisible))
            {
                FxSystem.Puff(Center(u, v), new Color(0.55f, 0.5f, 0.7f, 0.7f), 1f);
                return;
            }
            // a buff named like the ability that applied it (Battle Shout, Devotion Aura…) is already told by the
            // ability label / aura pulse — don't repeat it over every ally
            if (def.kind == AuraKind.Buff && curBeat != null && curBeat.Ability != null && curBeat.Ability.name == def.name) return;
            if (Throttled(u, e.AuraId, 2.5f)) return;
            if (def.kind == AuraKind.Debuff)
            {
                FloatingText.Spawn(Head(u, v), def.name, DebuffTextColor, 0.75f);
                Sfx.Play("debuff", Feet(u, v), 0.55f, 1f);
            }
            else if (e.Seconds > 0f || e.Source != u)
            {
                FloatingText.Spawn(Head(u, v), def.name, BuffTextColor, 0.72f);
                Sfx.Play("buff", Feet(u, v), 0.5f, 1f);
            }
        }

        void VisAuraRemoved(CombatEvent e, bool broken)
        {
            var u = e.Target;
            if (u == null) return;
            IncAura(u, e.AuraId, -1);
            RefreshStateVisuals(u);
            var def = Db?.Aura(e.AuraId);
            if (def == null || def.hidden) return;
            var v = V(u);
            if (HasState(def, UnitState.Polymorph)) FxSystem.Puff(Center(u, v), Color.white, 1f);
            if (broken && def.states.Length > 0) FloatingText.Spawn(Head(u, v), def.name + " broken", MutedText, 0.72f);
        }

        void RefreshStateVisuals(Unit u)
        {
            var v = V(u);
            if (v == null) return;
            int mask = 0;
            bool frost = false;
            if (presentedAuras.TryGetValue(u, out var d))
            {
                var db = Db;
                foreach (var kv in d)
                {
                    var def = db?.Aura(kv.Key);
                    if (def == null || def.states.Length == 0) continue;
                    for (int i = 0; i < def.states.Length; i++) mask |= 1 << (int)def.states[i];
                    if (def.school == School.Frost && (HasState(def, UnitState.Root) || HasState(def, UnitState.Stun))) frost = true;
                }
            }
            ApplyStateMask(v, mask, frost);
        }

        void ApplyRealStateVisuals(Unit u, UnitView v)
        {
            int mask = 0;
            bool frost = false;
            if (u.IsAlive)
                foreach (var a in u.Auras)
                {
                    if (a == null || a.Def == null || a.Def.states.Length == 0) continue;
                    for (int i = 0; i < a.Def.states.Length; i++) mask |= 1 << (int)a.Def.states[i];
                    if (a.Def.school == School.Frost && (HasState(a.Def, UnitState.Root) || HasState(a.Def, UnitState.Stun))) frost = true;
                }
            ApplyStateMask(v, mask, frost);
        }

        static bool Has(int mask, UnitState s) => (mask & (1 << (int)s)) != 0;

        static void ApplyStateMask(UnitView v, int mask, bool frost)
        {
            if (v.IsDead) return;
            v.SetStealthed(Has(mask, UnitState.Stealth) || Has(mask, UnitState.Invisible));
            v.SetPolymorphed(Has(mask, UnitState.Polymorph));
            Color tint = Color.white;
            if (Has(mask, UnitState.Banish)) tint = new Color(0.78f, 0.72f, 1f, 0.8f);
            else if (Has(mask, UnitState.Invulnerable)) tint = new Color(1f, 0.95f, 0.72f);
            else if (frost) tint = new Color(0.62f, 0.85f, 1f);
            else if (Has(mask, UnitState.Stun)) tint = new Color(0.96f, 0.9f, 0.62f);
            else if (Has(mask, UnitState.Fear)) tint = new Color(0.8f, 0.68f, 0.96f);
            else if (Has(mask, UnitState.Confuse)) tint = new Color(0.92f, 0.74f, 0.95f);
            else if (Has(mask, UnitState.Sleep) || Has(mask, UnitState.Incapacitate)) tint = new Color(0.78f, 0.8f, 0.95f);
            else if (Has(mask, UnitState.Root)) tint = new Color(0.78f, 0.93f, 0.66f);
            v.SetTint(tint);
        }

        static bool HasState(AuraDef d, UnitState s) => d != null && Array.IndexOf(d.states, s) >= 0;

        static string ControlWord(AuraDef d)
        {
            if (d == null || d.states.Length == 0) return null;
            if (HasState(d, UnitState.Polymorph)) return "Polymorphed";
            if (HasState(d, UnitState.Stun)) return d.school == School.Frost ? "Frozen" : "Stunned";
            if (HasState(d, UnitState.Sleep)) return "Asleep";
            if (HasState(d, UnitState.Incapacitate)) return "Incapacitated";
            if (HasState(d, UnitState.Fear)) return "Feared";
            if (HasState(d, UnitState.Confuse)) return "Confused";
            if (HasState(d, UnitState.Banish)) return "Banished";
            if (HasState(d, UnitState.Root)) return d.school == School.Frost ? "Frozen" : "Rooted";
            if (HasState(d, UnitState.Silence)) return "Silenced";
            if (HasState(d, UnitState.Disarm)) return "Disarmed";
            if (HasState(d, UnitState.Pacify)) return "Pacified";
            if (HasState(d, UnitState.Invulnerable)) return "Invulnerable";
            return null;
        }

        static string StateWord(string state)
        {
            switch (state)
            {
                case "Stun": return "Stunned";
                case "Polymorph": return "Polymorphed";
                case "Incapacitate": return "Incapacitated";
                case "Sleep": return "Asleep";
                case "Banish": return "Banished";
                case "Fear": return "Feared";
                case "Confuse": return "Confused";
                default: return string.IsNullOrEmpty(state) ? null : state;
            }
        }

        bool Throttled(Unit u, string auraId, float seconds)
        {
            if (u == null || auraId == null) return false;
            long key = u.Id * 1000003L + auraId.GetHashCode();
            if (auraTextTimes.TryGetValue(key, out var t) && presClock - t < seconds) return true;
            auraTextTimes[key] = presClock;
            return false;
        }

        // ================================================================ pending casts & channels

        sealed class CastVis
        {
            public Color Color;
            public string Name = "";
            public float Total;
            public float Progress;
            public int Beam = -1;
            public Unit Target;
            public bool Channel;
            public bool HasPoint;
            public Vector2 Point;
        }

        readonly Dictionary<Unit, CastVis> casting = new Dictionary<Unit, CastVis>();

        void BeginCastVis(Unit u, AbilityDef a, string name, float total, bool channel, int beam, Unit target)
        {
            if (u == null) return;
            if (casting.TryGetValue(u, out var old) && old.Beam >= 0 && old.Beam != beam) FxSystem.StopBeam(old.Beam);
            var c = new CastVis
            {
                Color = Ui.SchoolColor(a != null ? a.school : School.Arcane),
                Name = string.IsNullOrEmpty(name) ? (a != null ? a.name : "") : name,
                Total = Mathf.Max(0.01f, total), Beam = beam, Target = target, Channel = channel, Progress = 0.5f,
            };
            var p = u.Pending;
            if (curBeat != null && curBeat.Actor == u && curBeat.Point.HasValue) { c.HasPoint = true; c.Point = ToV(curBeat.Point.Value); }
            else if (p != null && p.HasPoint) { c.HasPoint = true; c.Point = ToV(p.Point); }
            if (p != null && p.Ability != null && (a == null || p.Ability.id == a.id))
            {
                float tot = channel && p.ChannelDuration > 0f ? p.ChannelDuration : c.Total;
                c.Progress = Mathf.Clamp01(1f - p.RemainingTime / Mathf.Max(0.01f, tot));
            }
            casting[u] = c;
            var v = V(u);
            if (v != null) v.SetCasting(c.Color, Mathf.Max(0.15f, c.Progress));
            FloatingText.Status(Head(u, v) + new Vector2(0f, 0.35f), (channel ? "Channeling " : "Casting ") + c.Name + "...",
                Color.Lerp(c.Color, Color.white, 0.45f));
        }

        void VisContinueCast(CombatEvent e)
        {
            var u = e.Source;
            if (u == null) return;
            if (!casting.TryGetValue(u, out var c))
            {
                BeginCastVis(u, Db?.Ability(e.AbilityId), e.Name, e.Seconds + RulesConstants.TurnSeconds, false, -1, e.Target);
                return;
            }
            c.Progress = Mathf.Clamp01(1f - e.Seconds / Mathf.Max(c.Total, e.Seconds + 0.01f));
            var v = V(u);
            if (v != null) v.SetCasting(c.Color, Mathf.Max(0.15f, c.Progress));
            FloatingText.Status(Head(u, v) + new Vector2(0f, 0.35f), "Casting " + c.Name + "... (" + e.Seconds.ToString("0.#") + " s)",
                Color.Lerp(c.Color, Color.white, 0.45f));
        }

        void StopCastVis(Unit u)
        {
            if (u == null) return;
            if (casting.TryGetValue(u, out var c))
            {
                if (c.Beam >= 0) FxSystem.StopBeam(c.Beam);
                casting.Remove(u);
            }
            var v = V(u);
            if (v != null) v.StopCasting();
        }

        /// <summary>Per frame: beams follow their units.</summary>
        void UpdatePersistentVisuals()
        {
            presClock += FrameDt;
            foreach (var kv in casting)
            {
                var c = kv.Value;
                if (c.Beam < 0 || c.Target == null) continue;
                var av = V(kv.Key);
                var tv = V(c.Target);
                if (av == null || tv == null) continue;
                FxSystem.MoveBeam(c.Beam, Hand(kv.Key, av, tv.FeetPosition), tv.CenterPosition);
            }
            if (curChannelBeam >= 0 && curChannelFrom != null && curChannelTo != null)
            {
                var av = V(curChannelFrom);
                var tv = V(curChannelTo);
                if (av != null && tv != null) FxSystem.MoveBeam(curChannelBeam, Hand(curChannelFrom, av, tv.FeetPosition), tv.CenterPosition);
            }
        }

        // ================================================================ classification helpers

        Delivery Classify(AbilityDef a, Beat b, Unit actor, Unit target, UnitView av, UnitView tv)
        {
            bool other = target != null && target != actor;
            bool hostile = other && target.IsHostileTo(actor);
            float dist = other ? Vector2.Distance(Feet(actor, av), Feet(target, tv)) : 0f;
            float reach = other ? Battle.MeleeReachOf(actor, target) + 1f : 0f;
            if (a == null)
            {
                if (!other) return Delivery.Self;
                if (!hostile) return Delivery.OnTarget;
                bool ranged = false;
                var s = School.Physical;
                for (int i = 0; i < b.Events.Count; i++)
                {
                    var e = b.Events[i];
                    if (e.Source != actor || !IsOutcome(e.Type)) continue;
                    if (e.Ranged) ranged = true;
                    s = e.School;
                    break;
                }
                if (s != School.Physical) return Delivery.Bolt;
                if (actor.Creature != null && actor.Creature.ranged && dist > reach) ranged = true;
                return ranged || dist > reach ? Delivery.Ranged : Delivery.Melee;
            }
            if (a.channeled && a.channelTicks > 0) return Delivery.Channel;
            switch (a.area.shape)
            {
                case AreaShape.Circle:
                    if (a.area.centeredOnCaster || a.target == TargetType.Self) return Delivery.AreaCaster;
                    if (AbilityRules.IsRangedWeaponAbility(a) && other) return Delivery.Ranged;
                    return Delivery.AreaPoint;
                case AreaShape.Cone: return Delivery.Cone;
                case AreaShape.Line: return Delivery.Line;
            }
            if (a.special == "Shoot") return other ? Delivery.Wand : Delivery.Self;
            if (!other || a.target == TargetType.Self) return Delivery.Self;
            if (!hostile) return Delivery.OnTarget;
            if (AbilityRules.IsRangedWeaponAbility(a)) return Delivery.Ranged;
            var kind = AbilityRules.KindOf(a);
            if (kind == AttackKind.Melee) return dist > reach + 1f ? (a.school == School.Physical ? Delivery.Ranged : Delivery.Bolt) : Delivery.Melee;
            if (kind == AttackKind.Ranged) return Delivery.Ranged;
            return Delivery.Bolt;
        }

        bool ShouldAnnounce(Unit actor, AbilityDef a)
        {
            if (a == null || a.autoAttack || a.nextSwing) return false;
            if (a.id == "attack" || a.id == "auto_shot" || a.id == "shoot" || a.special == "Shoot") return false;
            if (a.name == "Shoot" || a.name == "Throw" || a.name == "Attack") return false;
            return Battle.IsAIControlled(actor);
        }

        static School SwingSchool(Beat b)
        {
            for (int i = 0; i < b.Events.Count; i++)
                if (b.Events[i].Type == CombatEventType.Damage) return b.Events[i].School;
            return School.Physical;
        }

        float AreaRadius(Unit actor, AbilityDef a)
        {
            if (a == null) return 3f;
            float r;
            try { r = AbilityRules.RadiusMetres(a, AbilityMods.For(actor, a)); }
            catch (Exception) { r = MathUtil.Yd(a.area.radius); }
            return Mathf.Clamp(r, 0.8f, 20f);
        }

        Vector2 AreaCenter(Beat b, AbilityDef a, Unit actor, Unit target)
        {
            if (a != null && (a.area.centeredOnCaster || a.target == TargetType.Self)) return Feet(actor, V(actor));
            if (b.Point.HasValue) return ToV(b.Point.Value);
            // a pending area cast resolving at turn start: the point it was aimed at
            if (actor != null && casting.TryGetValue(actor, out var cv) && cv.HasPoint) return cv.Point;
            if (target != null && target != actor) return Feet(target, V(target));
            // centroid of everyone hit
            Vector2 sum = Vector2.zero;
            int n = 0;
            for (int i = 0; i < b.Events.Count; i++)
            {
                var e = b.Events[i];
                if (e.Source != actor || e.Target == null || e.Target == actor || !IsOutcome(e.Type)) continue;
                sum += Feet(e.Target, V(e.Target));
                n++;
            }
            return n > 0 ? sum / n : Feet(actor, V(actor));
        }

        Vector2 AimDirection(Beat b, Unit actor, UnitView av, Unit target, UnitView tv)
        {
            var from = Feet(actor, av);
            Vector2 aim;
            if (b.Point.HasValue) aim = ToV(b.Point.Value);
            else if (target != null && target != actor) aim = Feet(target, tv);
            else aim = from + new Vector2(actor.Facing.x, actor.Facing.y);
            var d = aim - from;
            if (d.sqrMagnitude < 1e-4f) d = new Vector2(av != null ? av.Facing : 1f, 0f);
            return d.normalized;
        }

        void PulseIfGroupBuff(Beat b, Unit actor, UnitView av, Color col)
        {
            int n = 0;
            float far = 0f;
            var c = Feet(actor, av);
            Unit last = null;
            for (int i = 0; i < b.Events.Count; i++)
            {
                var e = b.Events[i];
                if (e.Type != CombatEventType.AuraApplied && e.Type != CombatEventType.AuraRefreshed) continue;
                if (e.Source != actor || e.Target == null || e.Target.IsHostileTo(actor) || e.Target == last) continue;
                last = e.Target;
                n++;
                far = Mathf.Max(far, Vector2.Distance(c, Feet(e.Target, V(e.Target))));
            }
            if (n >= 2) FxSystem.AuraPulse(c, Mathf.Clamp(far + 0.8f, 2.5f, 14f), Color.Lerp(col, Color.white, 0.3f));
            else if (n == 1 && last == actor) FxSystem.AuraPulse(c, 1.3f, Color.Lerp(col, Color.white, 0.3f));
        }

        static bool HasOutcomeFrom(Beat b, Unit actor)
        {
            for (int i = 0; i < b.Events.Count; i++)
            {
                var e = b.Events[i];
                if (e.Source == actor && IsOutcome(e.Type) && !e.AutoAttack) return true;
            }
            return false;
        }

        bool HasVisibleText(Beat b)
        {
            for (int i = 0; i < b.Events.Count; i++)
            {
                switch (b.Events[i].Type)
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
                    case CombatEventType.Death:
                    case CombatEventType.Downed:
                    case CombatEventType.Revive:
                    case CombatEventType.CastInterrupted:
                    case CombatEventType.CastFailed:
                    case CombatEventType.Taunt:
                    case CombatEventType.Dispel:
                    case CombatEventType.AuraBroken:
                    case CombatEventType.Summon:
                    case CombatEventType.Teleport:
                    case CombatEventType.Knockback:
                        if (b.Events[i].Reason != "regen") return true;
                        break;
                    case CombatEventType.AuraApplied:
                    {
                        var def = Db?.Aura(b.Events[i].AuraId);
                        if (def != null && !def.hidden && def.states.Length > 0) return true;
                        break;
                    }
                }
            }
            return false;
        }

        // ================================================================ positions & camera

        static Vector2 Feet(Unit u, UnitView v) => v != null ? v.FeetPosition : (u != null ? ToV(u.Position) : Vector2.zero);
        static Vector2 Center(Unit u, UnitView v) => v != null ? v.CenterPosition : Feet(u, null) + new Vector2(0f, 0.9f);
        static Vector2 Head(Unit u, UnitView v) => v != null ? v.HeadPosition : Feet(u, null) + new Vector2(0f, 1.8f);

        static Vector2 Hand(Unit u, UnitView v, Vector2 towards)
        {
            var c = Center(u, v);
            float dir = towards.x >= c.x ? 1f : -1f;
            return c + new Vector2(dir * 0.3f, 0.12f);
        }

        static float PathLength(List<Vec2> path)
        {
            float len = 0f;
            for (int i = 1; i < path.Count; i++) len += Vec2.Distance(path[i - 1], path[i]);
            return len;
        }

        Vector2 BannerPosition()
        {
            Vector2 sum = Vector2.zero;
            int n = 0;
            foreach (var u in Battle.Units)
            {
                if (u.Team != Battle.PlayerTeam || !u.IsCharacter || u.Dead) continue;
                sum += Head(u, V(u));
                n++;
            }
            if (n > 0) return sum / n + new Vector2(0f, 0.9f);
            var rig = CameraRig.Instance;
            if (rig != null) return (Vector2)rig.transform.position;
            return Vector2.zero;
        }

        static void FocusOn(Vector2 p)
        {
            var rig = CameraRig.Instance;
            if (rig != null) rig.Focus(p);
        }

        /// <summary>Re-frames the camera only when the point is near the screen edge.</summary>
        static void FocusIfNeeded(Vector2 p)
        {
            var rig = CameraRig.Instance;
            if (rig == null || rig.Cam == null) return;
            var vp = rig.Cam.WorldToViewportPoint(new Vector3(p.x, p.y, 0f));
            if (vp.x > 0.16f && vp.x < 0.84f && vp.y > 0.14f && vp.y < 0.86f) return;
            rig.Focus(p);
        }

        // ================================================================ combat log

        readonly List<string> logLines = new List<string>(340);
        const int LogCap = 300;

        const string ColDefault = "#d9d2e6", ColDamageOut = "#f4ede2", ColDamageIn = "#ff9c8c", ColHeal = "#8fe08a",
            ColAvoid = "#b8acc9", ColBuff = "#a8e6c8", ColDebuff = "#f2b36b", ColTurn = "#e8c374", ColRound = "#b98a3e",
            ColDeath = "#ff6b5e", ColCast = "#c9a3f0", ColResource = "#9fc7f0", ColThreat = "#f0a060", ColMuted = "#9a90ab";

        void AddLog(CombatEvent e)
        {
            // resource costs and small gains (rage from every hit) would drown the log: HUD bars show them
            if (e.Type == CombatEventType.ResourceChange && (e.Amount < 0f || e.Amount < (e.Resource == ResourceType.Mana ? 50f : 10f))) return;
            string text = e.Text;
            if (string.IsNullOrEmpty(text))
            {
                try { text = CombatLog.Format(e); }
                catch (Exception) { text = ""; }
            }
            if (string.IsNullOrEmpty(text)) return;
            logLines.Add("<color=" + LogColor(e) + ">" + text + "</color>");
            if (logLines.Count > LogCap + 30) logLines.RemoveRange(0, logLines.Count - LogCap);
        }

        string LogColor(CombatEvent e)
        {
            switch (e.Type)
            {
                case CombatEventType.Damage:
                    return e.Target != null && e.Target.Team == Battle.PlayerTeam ? ColDamageIn : ColDamageOut;
                case CombatEventType.Heal:
                case CombatEventType.Revive:
                    return ColHeal;
                case CombatEventType.Miss:
                case CombatEventType.Dodge:
                case CombatEventType.Parry:
                case CombatEventType.Block:
                case CombatEventType.Resist:
                case CombatEventType.Absorb:
                case CombatEventType.Immune:
                case CombatEventType.Evade:
                    return ColAvoid;
                case CombatEventType.AuraApplied:
                case CombatEventType.AuraRefreshed:
                case CombatEventType.AuraStack:
                {
                    var def = Db?.Aura(e.AuraId);
                    return def != null && def.kind == AuraKind.Debuff ? ColDebuff : ColBuff;
                }
                case CombatEventType.AuraRemoved:
                case CombatEventType.AuraBroken:
                case CombatEventType.Dispel:
                    return ColMuted;
                case CombatEventType.TurnStart:
                    return ColTurn;
                case CombatEventType.RoundStart:
                case CombatEventType.BattleStart:
                case CombatEventType.BattleEnd:
                case CombatEventType.Initiative:
                    return ColRound;
                case CombatEventType.Death:
                case CombatEventType.Downed:
                case CombatEventType.TurnSkipped:
                    return ColDeath;
                case CombatEventType.CastStart:
                case CombatEventType.CastComplete:
                case CombatEventType.CastInterrupted:
                case CombatEventType.CastFailed:
                case CombatEventType.AbilityUsed:
                case CombatEventType.SwingQueued:
                case CombatEventType.Summon:
                case CombatEventType.Despawn:
                    return ColCast;
                case CombatEventType.ResourceChange:
                case CombatEventType.ComboPoints:
                case CombatEventType.ItemCreated:
                case CombatEventType.CooldownReset:
                    return ColResource;
                case CombatEventType.Taunt:
                case CombatEventType.TargetChanged:
                case CombatEventType.Threat:
                    return ColThreat;
                default:
                    return ColDefault;
            }
        }
    }
}
