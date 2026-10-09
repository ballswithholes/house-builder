// Attack tables: the single source of truth for the avoidance/crit chances that RollOutcome (abilities) and WhiteSwing
// (auto attacks) roll against, and the HitChance/SwingHitChance previews the UI shows ("Hit 92% · Crit 18%").
using System;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    /// <summary>
    /// Attack-table preview of an attacker using an ability (or a white swing) on a target. Every value is a percentage
    /// (0..100) of attempts, computed by the same code the engine rolls with (Battle.HitTable.cs):
    /// <list type="bullet">
    /// <item><c>Miss</c>, <c>Dodge</c>, <c>Parry</c>, <c>Block</c>: outcomes of the avoidance roll (melee/ranged tables;
    /// wands miss). Blocked attacks still land, with block value subtracted.</item>
    /// <item><c>Resist</c>: a spell's full resist (the spell table's miss).</item>
    /// <item><c>Hit</c>: the attack lands = 100 − Miss − Dodge − Parry − Resist (includes blocked and critical hits).</item>
    /// <item><c>Crit</c>: critical hits. White swings roll one table (crit is what is left after the avoidance results);
    /// abilities roll crit per effect on a landed hit, so Crit = Hit × CritOnHit / 100.</item>
    /// <item><c>CritOnHit</c>: crit chance of an attack that lands.</item>
    /// </list>
    /// Helpful abilities on allies never miss (Hit 100; Crit = the heal's crit chance). Partial resists (average mitigation)
    /// are damage reduction, not outcomes, and are not listed.
    /// </summary>
    public struct HitChanceInfo
    {
        /// <summary>Hit table used: Melee, Ranged, Spell or Wand.</summary>
        public AttackKind Kind;
        /// <summary>White swing (one roll for miss/dodge/parry/block/crit/hit).</summary>
        public bool SingleRoll;
        /// <summary>The ability rolls on the hit table at all (false: it cannot miss — helpful, cannotMiss, no hostile effect).</summary>
        public bool Rolls;
        /// <summary>The target is invulnerable (Divine Shield, Ice Block, banished): nothing lands.</summary>
        public bool Immune;
        /// <summary>The ability has an effect that can critically hit.</summary>
        public bool CanCrit;
        public float Hit, Miss, Dodge, Parry, Block, Resist, Crit;
        public float CritOnHit;
        public override string ToString() =>
            Immune ? "Immune" : $"Hit {Hit:0.#}% (miss {Miss:0.#}, dodge {Dodge:0.#}, parry {Parry:0.#}, block {Block:0.#}, resist {Resist:0.#}) crit {Crit:0.#}%";
    }

    public sealed partial class Battle
    {
        // ============================================================= shared tables (rolled by the engine)

        /// <summary>
        /// Avoidance table of an ability attack (RollOutcome rolls one d100 against it): spells and wands only miss
        /// (spell miss by level difference − spell hit, capped at 99% hit); melee/ranged abilities miss, then dodge,
        /// parry (melee, frontal) and block (frontal) unless the target is controlled or the effect is unavoidable.
        /// </summary>
        internal void AbilityAvoidanceTable(Unit c, Unit t, AttackKind kind, School school, float hitBonus, bool unavoidable,
            out float miss, out float dodge, out float parry, out float block)
        {
            dodge = parry = block = 0f;
            if (kind == AttackKind.Spell || kind == AttackKind.Wand)
            {
                miss = Formulas.SpellMissChance(c.Level, t.Level) - c.Stats.SpellHit(school) - hitBonus - t.Stats.ChanceToBeHit
                       + Specials.IncomingMissChance(t, kind);
                miss = MathUtil.Clamp(miss, 100f - RulesConstants.MaxHitChance, 100f);
                return;
            }
            bool ranged = kind == AttackKind.Ranged;
            float hit = ranged ? c.Stats.RangedHit : c.Stats.MeleeHit;
            miss = Formulas.MeleeMissChance(c.Level, t.Level, false) - hit - hitBonus - t.Stats.ChanceToBeHit + t.Stats.Defense * 0.04f
                   + Specials.IncomingMissChance(t, kind);
            miss = MathUtil.Clamp(miss, 100f - RulesConstants.MaxHitChance, 100f);
            bool canAvoid = !t.IsControlled && !unavoidable;
            bool frontal = !c.IsBehind(t);
            dodge = canAvoid ? Math.Max(0f, t.Stats.Dodge - c.Stats.DodgeChanceAgainstMe) : 0f;
            parry = canAvoid && !ranged && frontal && t.Stats.CanParry ? t.Stats.Parry : 0f;
            block = canAvoid && frontal && t.Stats.CanBlock ? t.Stats.BlockChance : 0f;
        }

        /// <summary>
        /// Single-roll table of a white swing (WhiteSwing rolls one d100 against it in this order): miss (+19% dual wield
        /// for melee), dodge, parry (melee, frontal), block (frontal), crit (−0.04% per defense point, weapon talents,
        /// crit specials), hit.
        /// </summary>
        internal void SwingTable(Unit u, Unit target, WeaponSlot slot, WeaponInfo w,
            out float miss, out float dodge, out float parry, out float block, out float crit)
        {
            bool ranged = slot == WeaponSlot.Ranged;
            var st = u.Stats;
            var tst = target.Stats;
            // the dual-wield penalty applies whenever the off hand swings too (its weapon next to a main-hand weapon or the
            // fists: an Off Hand weapon with an empty main hand still swings both hands)
            bool dw = !ranged && StatCalculator.GetWeapon(u, WeaponSlot.OffHand).Valid;
            miss = Formulas.MeleeMissChance(u.Level, target.Level, dw) - (ranged ? st.RangedHit : st.MeleeHit) - tst.ChanceToBeHit + tst.Defense * 0.04f
                   + Specials.IncomingMissChance(target, ranged ? AttackKind.Ranged : AttackKind.Melee);
            miss = MathUtil.Clamp(miss, 100f - RulesConstants.MaxHitChance, 100f);
            bool canAvoid = !target.IsControlled;
            bool frontal = !u.IsBehind(target);
            dodge = canAvoid ? Math.Max(0f, tst.Dodge - st.DodgeChanceAgainstMe) : 0f;
            parry = canAvoid && !ranged && frontal && tst.CanParry ? tst.Parry : 0f;
            block = canAvoid && frontal && tst.CanBlock ? tst.BlockChance : 0f;
            crit = (ranged ? st.RangedCrit : st.MeleeCrit) - tst.Defense * 0.04f;
            crit += WeaponTalents.StatBonus(u, w.Type, ranged ? StatId.RangedCrit : StatId.MeleeCrit);
            crit = Math.Max(0f, Specials.CritChanceBonus(new AbilityCast { Battle = this, Caster = u, Ability = Db.Ability(ranged ? "auto_shot" : "attack"), Target = target, School = w.School }, target, w.School, crit));
        }

        // ============================================================= previews

        /// <summary>
        /// Attack-table preview (percentages, see <see cref="HitChanceInfo"/>) of <paramref name="caster"/> using
        /// <paramref name="ability"/> on <paramref name="target"/>, from the code that rolls it. A null ability or a basic
        /// attack (Attack, Auto Shot, Shoot) previews the white swing of the weapon it uses (<see cref="SwingHitChance"/>).
        /// The hit roll is the one of the ability's first hostile effect that needs it (as the engine rolls once per cast
        /// and target); the crit chance is the one of its first damage (or, on allies, heal) effect that can crit.
        /// </summary>
        public HitChanceInfo HitChance(Unit caster, AbilityDef ability, Unit target)
        {
            if (caster == null || target == null) return default;
            if (ability == null || ability.autoAttack)
            {
                WeaponSlot slot;
                if (ability == null) slot = IsRangedAutoMode(caster, target) ? WeaponSlot.Ranged : WeaponSlot.MainHand;
                else if (caster.Class == null) slot = caster.Creature != null && caster.Creature.ranged && !InMeleeReach(caster, target) ? WeaponSlot.Ranged : WeaponSlot.MainHand;
                else slot = AbilityRules.IsRangedWeaponAbility(ability) ? WeaponSlot.Ranged : WeaponSlot.MainHand;
                return SwingHitChance(caster, target, slot);
            }

            var cast = NewCast(caster, ability, AbilityRules.UsedRank(caster, ability), target, null, null);
            var info = new HitChanceInfo { Kind = cast.Kind };
            bool hostile = target.IsHostileTo(caster);

            // crit: the first effect that can crit (damage on enemies, heals on allies)
            EffectDef critEffect = null;
            foreach (var e in ability.effects)
            {
                if (e.cannotCrit || !LandsOnTarget(e)) continue;
                bool dmg = e.type == EffectType.Damage || e.type == EffectType.WeaponDamage;
                bool heal = e.type == EffectType.Heal && e.pctOfMax <= 0;
                if (hostile ? dmg : heal) { critEffect = e; break; }
            }
            float critOnHit = 0f;
            if (critEffect != null)
            {
                info.CanCrit = true;
                bool heal = critEffect.type == EffectType.Heal;
                School school = critEffect.school ?? cast.School;
                if (critEffect.type == EffectType.WeaponDamage && critEffect.school == null && cast.School == School.Physical)
                {
                    var w = StatCalculator.GetWeapon(caster, critEffect.ranged ? WeaponSlot.Ranged : (critEffect.offHand ? WeaponSlot.OffHand : WeaponSlot.MainHand));
                    if (w.Valid) school = w.School;
                }
                cast.CurrentEffect = critEffect;
                critOnHit = CritChance(cast, target, heal, school);
            }
            info.CritOnHit = critOnHit;

            if (!hostile)
            {
                info.Hit = 100f;
                info.Crit = critOnHit;
                return info;
            }

            // the hit roll: the first hostile effect that needs one (rolled once per cast and target)
            EffectDef rollEffect = null;
            foreach (var e in ability.effects)
                if (!e.cannotMiss && NeedsHitRoll(e.type) && LandsOnTarget(e)) { rollEffect = e; break; }
            if (target.IsInvulnerable && (rollEffect != null || critEffect != null))
            {
                info.Immune = true;
                info.Rolls = rollEffect != null;
                info.CritOnHit = 0f;
                return info;
            }
            if (rollEffect == null)
            {
                info.Hit = 100f;
                info.Crit = critOnHit;
                return info;
            }
            info.Rolls = true;
            cast.CurrentEffect = rollEffect;
            bool unavoidable = Specials.Unavoidable(cast, rollEffect);
            AbilityAvoidanceTable(caster, target, cast.Kind, cast.School, cast.Mods.HitChance, unavoidable, out float miss, out float dodge, out float parry, out float block);
            float rem = 100f;
            float Take(float v) { v = MathUtil.Clamp(v, 0f, rem); rem -= v; return v; }
            if (cast.Kind == AttackKind.Spell) info.Resist = Take(miss);
            else info.Miss = Take(miss);
            info.Dodge = Take(dodge);
            info.Parry = Take(parry);
            info.Hit = rem;                    // blocked hits land
            info.Block = Take(block);
            info.Crit = info.Hit * critOnHit / 100f;
            return info;
        }

        /// <summary>
        /// White-swing preview of the weapon in <paramref name="slot"/> against <paramref name="target"/> (the single-roll
        /// table WhiteSwing rolls: miss, dodge, parry, block, crit, hit; percentages of swings). No weapon: all zero.
        /// </summary>
        public HitChanceInfo SwingHitChance(Unit attacker, Unit target, WeaponSlot slot = WeaponSlot.MainHand)
        {
            var info = new HitChanceInfo { Kind = slot == WeaponSlot.Ranged ? AttackKind.Ranged : AttackKind.Melee, SingleRoll = true, Rolls = true, CanCrit = true };
            if (attacker == null || target == null) return default;
            var w = StatCalculator.GetWeapon(attacker, slot);
            if (!w.Valid) return default;
            if (target.IsInvulnerable) { info.Immune = true; return info; }
            SwingTable(attacker, target, slot, w, out float miss, out float dodge, out float parry, out float block, out float crit);
            float rem = 100f;
            float Take(float v) { v = MathUtil.Clamp(v, 0f, rem); rem -= v; return v; }
            info.Miss = Take(miss);
            info.Dodge = Take(dodge);
            info.Parry = Take(parry);
            info.Hit = rem;
            info.Block = Take(block);
            info.Crit = Take(crit);
            info.CritOnHit = info.Hit > 1e-4f ? info.Crit / info.Hit * 100f : 0f;
            return info;
        }

        /// <summary>The effect is applied to the cast's target (not to the caster, its pet/owner or its allies).</summary>
        static bool LandsOnTarget(EffectDef e)
        {
            switch (e.target)
            {
                case EffectTarget.Target: case EffectTarget.Area: case EffectTarget.EnemiesInRadius: case EffectTarget.Attacker:
                    return true;
                default: return false;
            }
        }
    }
}
