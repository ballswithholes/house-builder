// Out-of-combat ("field") actions: abilities and items used while exploring (buffs, heals, Conjure, Call
// Pet, demons, totems, food) go through the session's field context; this partial presents the resulting
// CombatEvents (floating numbers, sparkles, aura pulses, summon/despawn views, sounds). Battle events are
// presented by the CombatController and ignored here. Also: long rest.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class GameFlow
    {
        /// <summary>True from CombatStarted until CombatEnded: battle units/events belong to the combat presenter.</summary>
        bool battlePresenting;
        int fieldActionFrame = -1;
        Unit fieldActionCaster;

        static readonly Color BuffColor = new Color(0.98f, 0.9f, 0.62f, 0.85f);
        static readonly Color DebuffColor = new Color(0.72f, 0.55f, 0.9f, 0.85f);
        static readonly Color ItemGold = new Color(1f, 0.88f, 0.55f);

        // ------------------------------------------------------------ commands

        string UseAbilityInField(Unit caster, string abilityId, Unit target)
        {
            var s = Session;
            if (s == null || !HasGame) return "There is no game running.";
            if (caster == null) return "Nobody to use it.";
            switch (s.Mode)
            {
                case SessionMode.Combat: return "You are in combat.";
                case SessionMode.Dialogue: return "Not during a conversation.";
                case SessionMode.GameOver: return "The party has fallen.";
            }
            var a = Db != null ? Db.Ability(abilityId) : null;
            if (a == null) return "Unknown ability.";
            if (target == null && (a.target == TargetType.Ally || a.target == TargetType.Self)) target = caster;
            if (target == null && a.target == TargetType.Pet) target = caster.Pet;
            try
            {
                SyncUnitPositionsFromViews();
                MarkFieldAction(caster);
                Lanternvale.Util.Vec2? point = null;
                if (a.target == TargetType.Point) point = target != null ? target.Position : caster.Position;
                var r = s.UseAbility(caster, abilityId, target, point);
                return r.Ok ? null : (string.IsNullOrEmpty(r.Reason) ? "You can't do that now." : r.Reason);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return "That didn't work.";
            }
        }

        string UseItemInField(Unit user, ItemInstance item, Unit target)
        {
            var s = Session;
            if (s == null || !HasGame) return "There is no game running.";
            if (item == null) return "No item.";
            if (user == null) user = Selected ?? s.Leader;
            switch (s.Mode)
            {
                case SessionMode.Combat: return "You are in combat.";
                case SessionMode.Dialogue: return "Not during a conversation.";
                case SessionMode.GameOver: return "The party has fallen.";
            }
            var a = Db != null && item.Def != null ? Db.Ability(item.Def.use) : null;
            if (target == null && a != null && (a.target == TargetType.Ally || a.target == TargetType.Self)) target = user;
            try
            {
                var why = s.CannotUseItemReason(user, item, target);
                if (why != null) return why;
                MarkFieldAction(user);
                var r = s.UseItem(user, item, target, null);
                return r.Ok ? null : (string.IsNullOrEmpty(r.Reason) ? "You can't use that now." : r.Reason);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return "That didn't work.";
            }
        }

        string TryRestInternal()
        {
            var s = Session;
            if (s == null || !HasGame) return "There is no game running.";
            var why = s.CannotRestReason();
            if (why != null) return why;
            StopPartyMove(true);
            if (!s.LongRest()) return string.IsNullOrEmpty(s.LastError) ? "You cannot rest now." : s.LastError;
            return null;   // the Rested event plays the fade
        }

        void MarkFieldAction(Unit caster)
        {
            fieldActionFrame = Time.frameCount;
            fieldActionCaster = caster;
        }

        bool InFieldAction => fieldActionFrame == Time.frameCount;

        // ------------------------------------------------------------ presentation

        void OnSessionCombatEvent(CombatEvent e)
        {
            var s = Session;
            if (e == null || s == null || battlePresenting || s.Battle != null) return;
            try { PresentFieldEvent(e); }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        void PresentFieldEvent(CombatEvent e)
        {
            switch (e.Type)
            {
                case CombatEventType.AbilityUsed:
                case CombatEventType.CastStart:
                {
                    MarkFieldAction(e.Source);
                    if (e.Type == CombatEventType.CastStart) break;
                    var v = ViewOf(e.Source);
                    if (v == null) break;
                    var a = Db != null ? Db.Ability(e.AbilityId) : null;
                    if (a != null && (a.passive || a.hidden) && string.IsNullOrEmpty(e.Name)) break;
                    var tv = e.Target != null && e.Target != e.Source ? ViewOf(e.Target) : null;
                    if (tv != null) v.FaceTowards(tv.FeetPosition);
                    var school = a != null ? a.school : e.School;
                    bool spell = a != null && (a.school != School.Physical || a.castTime > 0f);
                    if (spell)
                    {
                        v.PlayCast(Ui.SchoolColor(school));
                        Sfx.Play("cast_start", v.FeetPosition, 0.75f, 1f);
                    }
                    break;
                }
                case CombatEventType.Heal:
                {
                    if (e.Periodic) break;   // food/drink and HoT ticks stay quiet outside combat
                    var tv = ViewOf(e.Target);
                    if (tv == null) break;
                    int amt = Mathf.RoundToInt(e.Amount);
                    if (amt > 0) FloatingText.Heal(tv.HeadPosition, amt, e.Crit);
                    FxSystem.HealSparkles(tv.FeetPosition, tv.Height);
                    Sfx.Play("heal", tv.FeetPosition);
                    break;
                }
                case CombatEventType.Damage:
                {
                    var tv = ViewOf(e.Target);
                    if (tv == null) break;
                    int amt = Mathf.RoundToInt(e.Amount);
                    if (amt <= 0 && e.Periodic) break;
                    if (amt > 0) FloatingText.Damage(tv.HeadPosition, amt, e.Crit, e.School);
                    tv.PlayHit();
                    Sfx.Impact(e.School, tv.FeetPosition, e.Crit);
                    break;
                }
                case CombatEventType.Miss:
                case CombatEventType.Dodge:
                case CombatEventType.Parry:
                case CombatEventType.Block:
                case CombatEventType.Resist:
                case CombatEventType.Immune:
                case CombatEventType.Evade:
                case CombatEventType.Absorb:
                {
                    var tv = ViewOf(e.Target);
                    if (tv != null) FloatingText.Miss(tv.HeadPosition, e.Type.ToString());
                    break;
                }
                case CombatEventType.AuraApplied:
                case CombatEventType.AuraStack:
                {
                    // area auras are re-applied as the party walks around: only present auras from an action just taken
                    if (!InFieldAction) break;
                    var tv = ViewOf(e.Target);
                    if (tv == null) break;
                    var def = Db != null ? Db.Aura(e.AuraId) : null;
                    if (def != null && def.hidden) break;
                    bool debuff = def != null && def.kind == AuraKind.Debuff;
                    var col = debuff ? DebuffColor : Color.Lerp(BuffColor, Ui.SchoolColor(e.School), 0.35f);
                    FxSystem.AuraPulse(tv.FeetPosition, 1.0f, col);
                    if (e.Type == CombatEventType.AuraApplied && !string.IsNullOrEmpty(e.Name))
                        FloatingText.Status(tv.HeadPosition, e.Name, debuff ? Ui.Bad : new Color(1f, 0.93f, 0.7f));
                    Sfx.Play(debuff ? "debuff" : "buff", tv.FeetPosition, 0.8f, 1f);
                    break;
                }
                case CombatEventType.Summon:
                {
                    var u = e.Target;
                    if (u == null) break;
                    var v = EnsureView(u);
                    if (v == null) break;
                    v.Teleport(ToUnity(u.Position));
                    var col = u.Creature != null ? Ui.SchoolColor(u.Creature.meleeSchool == School.Physical ? School.Arcane : u.Creature.meleeSchool) : Ui.Gold;
                    FxSystem.Puff(v.CenterPosition, new Color(0.9f, 0.88f, 0.95f), 0.9f);
                    FxSystem.Sparkles(v.CenterPosition, col, 12);
                    FxSystem.AuraPulse(v.FeetPosition, 1.1f, new Color(col.r, col.g, col.b, 0.8f));
                    Sfx.Play("buff", v.FeetPosition, 0.8f, 0.9f);
                    break;
                }
                case CombatEventType.Despawn:
                {
                    var v = ViewOf(e.Target);
                    if (v == null) break;
                    FxSystem.Puff(v.CenterPosition, new Color(0.88f, 0.86f, 0.94f), 0.8f);
                    RemoveView(e.Target);
                    break;
                }
                case CombatEventType.Death:
                {
                    var u = e.Target;
                    var v = ViewOf(u);
                    if (v == null || v.IsDead) break;
                    if (u.IsCharacter) { v.PlayDowned(); break; }
                    v.PlayDeath();
                    Sfx.Play("death", v.FeetPosition, 0.7f, 1f);
                    DetachUnitView(u);
                    ScheduleRemoval(v, 1.4f);
                    break;
                }
                case CombatEventType.Downed:
                {
                    var v = ViewOf(e.Target);
                    if (v != null && !v.IsDowned) v.PlayDowned();
                    break;
                }
                case CombatEventType.Revive:
                {
                    var v = ViewOf(e.Target);
                    if (v == null) break;
                    v.PlayRevive();
                    FxSystem.HealSparkles(v.FeetPosition, v.Height);
                    Sfx.Play("heal", v.FeetPosition);
                    break;
                }
                case CombatEventType.Teleport:
                {
                    var u = e.Target ?? e.Source;
                    var v = ViewOf(u);
                    if (v == null) break;
                    if (Session != null && u == Session.Leader) StopPartyMove(false);
                    FxSystem.Puff(ToUnity(e.From) + new Vector2(0f, v.Height * 0.5f), new Color(0.85f, 0.8f, 1f), 0.8f);
                    v.Teleport(ToUnity(e.To));
                    FxSystem.Sparkles(v.CenterPosition, Ui.SchoolColor(School.Arcane), 10);
                    break;
                }
                case CombatEventType.ItemCreated:
                {
                    var v = ViewOf(e.Source);
                    if (v == null || string.IsNullOrEmpty(e.Name)) break;
                    FloatingText.Status(v.HeadPosition, "+" + (e.Count > 1 ? e.Count + " " : "") + e.Name, ItemGold);
                    Sfx.Play("ui_open", v.FeetPosition, 0.7f, 1.1f);
                    break;
                }
                case CombatEventType.ResourceChange:
                {
                    if (!InFieldAction) break;
                    int amt = Mathf.RoundToInt(e.Amount);
                    if (amt < 1) break;   // costs are not interesting, gains are (Life Tap, Evocation, Innervate)
                    var v = ViewOf(e.Target ?? e.Source);
                    if (v != null) FloatingText.Resource(v.HeadPosition, amt, e.Resource);
                    break;
                }
            }
        }

        // ------------------------------------------------------------ rest

        void OnRested()
        {
            StopPartyMove(false);
            BeginRestFade();
        }

        /// <summary>Called by the overlay when the rest fade is fully dark: the party wakes refreshed.</summary>
        void OnRestWake()
        {
            var s = Session;
            if (s == null) return;
            foreach (var u in s.PartyUnits())
            {
                var v = ViewOf(u);
                if (v == null) continue;
                if (v.IsDowned && u.IsAlive) v.PlayRevive();
                FxSystem.HealSparkles(v.FeetPosition, v.Height);
            }
            Sfx.Play("buff", null, 0.9f, 0.9f);
        }
    }
}
