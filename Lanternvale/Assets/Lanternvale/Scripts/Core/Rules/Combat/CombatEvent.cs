// Events emitted by the rules engine for the presentation layer and the combat log.
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public enum CombatEventType
    {
        BattleStart, BattleEnd, RoundStart, TurnStart, TurnEnd, TurnSkipped,
        // attack outcomes
        Damage, Heal, Miss, Dodge, Parry, Block, Resist, Absorb, Immune, Evade,
        // auras
        AuraApplied, AuraRefreshed, AuraRemoved, AuraStack, AuraBroken, Dispel,
        // casts
        AbilityUsed, CastStart, CastComplete, CastInterrupted, CastFailed, ChannelTick, SwingQueued, AutoAttackToggled,
        // resources
        ResourceChange, ComboPoints,
        // movement
        Move, Teleport, Charge, Knockback,
        // units
        Summon, Despawn, Death, Downed, Revive,
        // threat
        Threat, Taunt, TargetChanged,
        // misc
        ItemCreated, ItemConsumed, CooldownReset, Initiative, Log,
    }

    public sealed class CombatEvent
    {
        public CombatEventType Type;
        public int Round;
        public Unit Source;
        public Unit Target;
        public string AbilityId = "";
        public string AuraId = "";
        public string Name = "";             // display name of the ability/aura/item
        public School School;
        /// <summary>Damage/heal dealt (after mitigation and absorbs), resource delta, threat, stacks, etc.</summary>
        public float Amount;
        public float Overkill, Overheal, Absorbed, Resisted, Blocked;
        public bool Crit;
        public bool Periodic;
        public bool OffHand;
        public bool Ranged;
        public bool AutoAttack;
        /// <summary>
        /// AuraApplied/AuraRemoved of an area-aura child: the radiusAura a bearer's area aura (paladin auras, totems, Trueshot,
        /// Moonkin-style auras…) puts on units entering its radius and takes off units leaving it (or when the source goes).
        /// Presenters may skip these (they fire on every step in and out of range); the combat log omits nothing.
        /// </summary>
        public bool AreaAuraChild;
        public ResourceType Resource;
        public Vec2 From, To;
        public List<Vec2> Path;
        /// <summary>Seconds (cast time, remaining time, lockout, durations).</summary>
        public float Seconds;
        public int Count;
        public string Text = "";             // pre-formatted combat log line (see CombatLog)
        public string Reason = "";           // CastFailed / Immune / AuraRemoved reason ("expired", "dispelled", "broken", "cancelled")

        public override string ToString() => string.IsNullOrEmpty(Text) ? CombatLog.Format(this) : Text;
    }
}
