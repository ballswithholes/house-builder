// A live aura on a unit.
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    public sealed class AuraInstance
    {
        static long nextUid = 1;
        public readonly long Uid = nextUid++;

        public AuraDef Def;
        /// <summary>The unit that applied the aura (may be the bearer, or null for environment/passive auras).</summary>
        public Unit Caster;
        /// <summary>The unit the aura is on.</summary>
        public Unit Bearer;

        /// <summary>Total duration in seconds (≤ 0 = permanent).</summary>
        public float Duration;
        /// <summary>Remaining seconds (ignored when permanent).</summary>
        public float Remaining;
        public int Stacks = 1;
        /// <summary>Remaining charges (0 = unlimited).</summary>
        public int Charges;

        /// <summary>Ability that applied the aura (rank scaling, AbilityMods), or null.</summary>
        public AbilityDef SourceAbility;
        /// <summary>Rank of the source ability / talent (1-based) used to index per-rank values.</summary>
        public int Rank = 1;
        /// <summary>Effective level for perLevel scaling (rank level, or caster level for creatures).</summary>
        public int EffLevel = 1;
        /// <summary>Learn level of the source ability (perLevel base).</summary>
        public int LearnLevel = 1;
        /// <summary>Combo points spent when a finisher applied the aura.</summary>
        public int ComboPoints;
        /// <summary>Generic magnitude multiplier (AbilityMod Effect pct of the source ability).</summary>
        public float EffectMult = 1f;
        /// <summary>Damage/heal multiplier of the source ability's Damage/Healing AbilityMods (applied to ticks).</summary>
        public float DamageMult = 1f, HealingMult = 1f;

        /// <summary>Fractional tick accumulator (seconds since last tick).</summary>
        public float TickAccum;
        /// <summary>Damage taken since application (breakDamageThreshold).</summary>
        public float DamageTaken;
        /// <summary>Remaining absorb amount (when Def.absorb != null).</summary>
        public float AbsorbLeft;
        /// <summary>Resolved stat modifier values (index-aligned with Def.mods), already scaled by rank/effect mult (per stack).</summary>
        public float[] ModValues = new float[0];

        /// <summary>For radiusAura children: the area aura that grants this aura (removed when out of range).</summary>
        public AuraInstance AreaSource;
        /// <summary>Granted permanently by a passive ability, creature passive or talent (never expires, not dispellable).</summary>
        public bool IsPassive;
        /// <summary>Internal cooldown timers of this aura's procs (seconds remaining, index-aligned with Def.procs).</summary>
        public float[] ProcCooldowns = new float[0];
        /// <summary>Battle cast serial during which the aura was applied (procs of that same cast ignore it).</summary>
        public int CastSerial = -1;
        /// <summary>Free-form state for special handlers.</summary>
        public Dictionary<string, float> Vars;

        public string Id => Def.id;
        public bool IsPermanent => Duration <= 0f || IsPassive;
        public bool IsDebuff => Def.kind == AuraKind.Debuff;
        public bool IsAreaChild => AreaSource != null;

        public bool HasState(UnitState s)
        {
            foreach (var st in Def.states) if (st == s) return true;
            return false;
        }

        public bool HasTag(string tag)
        {
            foreach (var t in Def.tags) if (string.Equals(t, tag, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public float GetVar(string k, float def = 0f) => Vars != null && Vars.TryGetValue(k, out var v) ? v : def;
        public void SetVar(string k, float v) { (Vars ??= new Dictionary<string, float>())[k] = v; }

        public override string ToString() => $"{Def.name}{(Stacks > 1 ? " x" + Stacks : "")}{(IsPermanent ? "" : $" ({Remaining:0.#}s)")}";
    }
}
