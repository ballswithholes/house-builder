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
        int castFxFrame = -1;
        Unit castFxCaster;

        static readonly Color BuffColor = new Color(0.98f, 0.9f, 0.62f, 0.85f);
        static readonly Color DebuffColor = new Color(0.72f, 0.55f, 0.9f, 0.85f);
        static readonly Color ItemGold = new Color(1f, 0.88f, 0.55f);

        // ------------------------------------------------------------ commands

        string UseAbilityInField(Unit caster, string abilityId, Unit target, int rank)
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
            if (a.target == TargetType.Enemy && (target == null || !target.IsHostileTo(caster)))
            {
                // an attack out of combat opens a fight: arm it, the next click on an enemy engages with it
                if (!s.IsInParty(caster) || !caster.IsAlive) return "Only an active party member can start a fight.";
                if (!caster.Knows(a.id)) return $"{caster.Name} does not know {a.name}.";
                if (enemyEntries.Count == 0) return "There is no enemy to attack.";
                // refuse what cannot open the fight anyway (no stealth/stance, cooldown, cost…) instead of walking the
                // party into the pack only for the engagement to fail and start a plain fight
                var why = OpenerUnusableReason(caster, a);
                if (why != null) return why;
                ArmOpener(caster, a);
                return null;
            }
            if (target == null && (a.target == TargetType.Ally || a.target == TargetType.Self)) target = caster;
            if (target == null && a.target == TargetType.Pet) target = caster.Pet;
            try
            {
                SyncUnitPositionsFromViews();
                MarkFieldAction(caster);
                Lanternvale.Util.Vec2? point = null;
                if (a.target == TargetType.Point) point = target != null ? target.Position : caster.Position;
                // a pinned rank the caster no longer knows falls back to the highest known one
                if (rank > 0 && rank > AbilityRules.KnownRanks(caster, a)) rank = 0;
                var r = s.UseAbility(caster, abilityId, target, point, Math.Max(0, rank));
                return r.Ok ? null : (string.IsNullOrEmpty(r.Reason) ? "You can't do that now." : r.Reason);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return "That didn't work.";
            }
        }

        /// <summary>
        /// Why <paramref name="caster"/> cannot open a fight with <paramref name="a"/> right now, or null: the
        /// target-independent use check of the exploration context (the action bar's: stealth/stance requirements, cooldown,
        /// resource cost, reagents…) at the highest known rank, the rank the engagement uses the opener at. Range, facing and
        /// the target's own requirements depend on the enemy and stay with the engagement (BeginWithOpener, whose failure
        /// starts the fight normally). Null as well when there is no field context to ask (nothing is refused then).
        /// </summary>
        string OpenerUnusableReason(Unit caster, AbilityDef a)
        {
            var s = Session;
            if (s == null || caster == null || a == null || s.Battle != null) return null;
            try
            {
                if (s.Field == null) s.GetAbilityBar(caster, false);   // lets the session build its field context first
                var f = s.Field;
                if (f == null || !f.Units.Contains(caster)) return null;
                var chk = f.CanUseIgnoringTarget(caster, a, false, 0);
                if (chk.Ok) return null;
                return string.IsNullOrEmpty(chk.Reason) ? $"{a.name} cannot be used now." : chk.Reason;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
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
                    // instants raise AbilityUsed, cast-time spells CastStart (+ CastComplete): present each action once
                    MarkFieldAction(e.Source);
                    if (castFxFrame == Time.frameCount && castFxCaster == e.Source) break;
                    castFxFrame = Time.frameCount;
                    castFxCaster = e.Source;
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
                        CombatSfx.CastWindup(a, school, v.FeetPosition, 0.75f);
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
                    if (e.Periodic) CombatSfx.Tick(e, tv.FeetPosition); else CombatSfx.Hit(e, tv.FeetPosition);
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
                    if (tv != null) FloatingText.Miss(tv.HeadPosition, CombatLog.AvoidWord(e));
                    if (tv != null) CombatSfx.Avoid(e, tv.FeetPosition);
                    break;
                }
                case CombatEventType.AuraApplied:
                case CombatEventType.AuraStack:
                {
                    // an area aura's radius children follow the party around (CombatEvent.AreaAuraChild): never shown; other
                    // auras only when an action was just taken (field rebuilds re-apply auras silently)
                    if (e.AreaAuraChild || !InFieldAction) break;
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
                    CombatSfx.Death(u, v.FeetPosition, false);
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
                    FxSystem.Puff(World3D.At(ToUnity(e.From), v.Height * 0.5f), new Color(0.85f, 0.8f, 1f), 0.8f);
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
                if (u.IsAlive && (v.IsDowned || v.IsDead)) v.PlayRevive();
                FxSystem.HealSparkles(v.FeetPosition, v.Height);
            }
            Sfx.Play("buff", null, 0.9f, 0.9f);
        }
    }
}
