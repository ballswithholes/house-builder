// Proc triggers from auras, talents (Proc passives, incl. owner pet talents) and item equip effects.
using System;
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    /// <summary>Context of a proc trigger.</summary>
    public sealed class ProcInfo
    {
        public AbilityDef Ability;
        public School School;
        public bool Crit, Periodic, AutoAttack, OffHand;
        public float Damage;
        public float WeaponSpeed = 2f;
        public int ComboPoints;
        public int Depth;
        public bool Ranged;
        /// <summary>The proc comes from an extra attack granted by another proc (cannot re-trigger that proc).</summary>
        public object FromProc;
    }

    public sealed partial class Battle
    {
        int procDepth;
        readonly HashSet<object> runningProcs = new HashSet<object>();

        /// <summary>Fires every proc of <paramref name="owner"/> listening to <paramref name="trigger"/>. <paramref name="other"/> is the other unit of the event.</summary>
        public void FireProcs(ProcTrigger trigger, Unit owner, Unit other, ProcInfo info)
        {
            if (owner == null || procDepth >= RulesConstants.MaxProcDepth) return;
            if (!owner.IsAlive && trigger != ProcTrigger.OnKill) return;
            procDepth++;
            try
            {
                // auras
                if (owner.Auras.Count > 0)
                {
                    foreach (var a in new List<AuraInstance>(owner.Auras))
                    {
                        if (!owner.Auras.Contains(a)) continue;
                        if (a.CastSerial == castSerial && castSerial != 0) continue; // applied by the event's own cast/swing
                        var procs = a.Def.procs;
                        for (int i = 0; i < procs.Count; i++)
                        {
                            var p = procs[i];
                            if (p.trigger != trigger || !Matches(p, info) || runningProcs.Contains(p)) continue;
                            if (i < a.ProcCooldowns.Length && a.ProcCooldowns[i] > 0) continue;
                            float chance = p.chance + (p.ppm > 0 ? 0f : Specials.ProcChanceBonus(owner, a, p));
                            if (!Rng.Chance(ProcChance(p, chance, info))) continue;
                            if (p.internalCooldown > 0 && i < a.ProcCooldowns.Length) a.ProcCooldowns[i] = p.internalCooldown;
                            RunProc(owner, other, p, info, a);
                            if (p.consumeCharge && owner.Auras.Contains(a)) ConsumeCharge(a);
                            if (!owner.Auras.Contains(a)) break;
                        }
                    }
                }
                // talents (own; owner's "Pet" procs for pets)
                FireTalentProcs(trigger, owner, owner, other, info, false);
                if (owner.Owner != null && owner.Kind != UnitKind.Totem) FireTalentProcs(trigger, owner.Owner, owner, other, info, true);
                // items
                foreach (var kv in owner.Equipment.Equipped)
                {
                    var effs = kv.Value.Def.equipEffects;
                    for (int i = 0; i < effs.Count; i++)
                    {
                        var pd = effs[i];
                        if (pd.type != "Proc" || pd.proc == null || pd.proc.trigger != trigger || !Matches(pd.proc, info) || runningProcs.Contains(pd.proc)) continue;
                        // weapon "chance on hit" procs only from that weapon's swings
                        if ((kv.Key == EquipSlot.MainHand && info.OffHand) || (kv.Key == EquipSlot.OffHand && !info.OffHand && (trigger == ProcTrigger.OnMeleeHit || trigger == ProcTrigger.OnAutoAttackHit))) continue;
                        string key = "i:" + kv.Value.Def.id + ":" + i + ":" + kv.Key;
                        if (owner.ProcCooldowns.TryGetValue(key, out var cd) && cd > 0) continue;
                        float baseChance = pd.values.Length > 0 ? pd.values[0] : pd.proc.chance;
                        var speedInfo = info;
                        if (pd.proc.ppm > 0 && (kv.Key == EquipSlot.MainHand || kv.Key == EquipSlot.OffHand))
                        {
                            var w = StatCalculator.GetWeapon(owner, kv.Key == EquipSlot.OffHand ? WeaponSlot.OffHand : WeaponSlot.MainHand);
                            speedInfo = new ProcInfo { WeaponSpeed = w.Valid ? w.Speed : info.WeaponSpeed };
                        }
                        if (!Rng.Chance(ProcChance(pd.proc, baseChance, speedInfo))) continue;
                        if (pd.proc.internalCooldown > 0) owner.ProcCooldowns[key] = pd.proc.internalCooldown;
                        RunProc(owner, other, pd.proc, info, null, 1, 1);
                    }
                }
                Specials.OnProcTrigger(this, trigger, owner, other, info);
            }
            finally { procDepth--; }
        }

        void FireTalentProcs(ProcTrigger trigger, Unit talentOwner, Unit owner, Unit other, ProcInfo info, bool petOnly)
        {
            if (talentOwner.Talents.Count == 0) return;
            foreach (var kv in talentOwner.Talents)
            {
                if (kv.Value <= 0) continue;
                var t = Db.Talent(kv.Key);
                if (t == null) continue;
                for (int i = 0; i < t.effects.Count; i++)
                {
                    var pd = t.effects[i];
                    if (pd.proc == null || pd.proc.trigger != trigger || runningProcs.Contains(pd.proc)) continue;
                    if (pd.type != "Proc" && !(pd.type == "Special" && pd.special == WeaponTalents.SpecialName)) continue;
                    if (pd.type == "Special" && !WeaponTalents.AttackMatches(owner, pd, info)) continue;
                    bool pet = string.Equals(pd.target, "Pet", StringComparison.OrdinalIgnoreCase);
                    if (pet != petOnly) continue;
                    if (!Matches(pd.proc, info)) continue;
                    string key = "t:" + t.id + ":" + i;
                    if (owner.ProcCooldowns.TryGetValue(key, out var cd) && cd > 0) continue;
                    float chance;
                    if (pd.values.Length > 0) chance = pd.values[Math.Min(kv.Value, pd.values.Length) - 1];
                    else if (pd.value > 0) chance = pd.value * kv.Value;
                    else chance = pd.proc.chance;
                    if (!Rng.Chance(ProcChance(pd.proc, chance, info))) continue;
                    if (pd.proc.internalCooldown > 0) owner.ProcCooldowns[key] = pd.proc.internalCooldown;
                    RunProc(owner, other, pd.proc, info, null, kv.Value, owner.Level);
                }
            }
        }

        static float ProcChance(ProcDef p, float chance, ProcInfo info)
        {
            if (p.ppm > 0) return p.ppm * Math.Max(0.5f, info.WeaponSpeed) / 60f * 100f;
            return chance;
        }

        static bool Matches(ProcDef p, ProcInfo info)
        {
            bool filtered = false, ok = false;
            if (p.abilities.Length > 0)
            {
                filtered = true;
                if (info.Ability != null) foreach (var id in p.abilities) if (id == info.Ability.id) { ok = true; break; }
            }
            if (!ok && p.tags.Length > 0)
            {
                filtered = true;
                if (info.Ability != null) foreach (var t in p.tags) if (AbilityMods.HasTag(info.Ability, t)) { ok = true; break; }
            }
            if (filtered && !ok) return false;
            if (p.schools.Length > 0 && Array.IndexOf(p.schools, info.School) < 0) return false;
            return true;
        }

        /// <summary>
        /// Runs proc effects. Aura procs scale with the aura's rank/level (source ability mods and combo points);
        /// talent procs use Rank = talent rank, EffLevel = unit level, LearnLevel = 1; item procs do not scale.
        /// </summary>
        void RunProc(Unit owner, Unit other, ProcDef p, ProcInfo info, AuraInstance aura, int rank = 1, int effLevel = 1)
        {
            if (p.effects.Count == 0) return;
            var src = aura?.SourceAbility;
            var cast = new AbilityCast
            {
                Battle = this, Caster = owner, Ability = src, Target = other ?? owner, Point = (other ?? owner).Position,
                SourceProc = p, ProcInfo = info, ProcOther = other, Free = true, Depth = info.Depth + 1, Rank = aura != null ? aura.Rank : rank,
                EffLevel = aura != null ? aura.EffLevel : effLevel, LearnLevel = aura != null ? aura.LearnLevel : 1,
                // talent/item procs without an explicit school take the school of the triggering ability (Winter's Chill,
                // Impact, Shadow Weaving... are Frost/Fire/Shadow spell effects, not melee attacks that can be parried)
                School = aura != null ? aura.Def.school : (p.effects[0].school ?? (info != null && info.Ability != null ? info.School : School.Physical)),
                Mods = src != null ? AbilityMods.For(aura.Caster ?? owner, src) : AbilityModSet.Empty,
                ComboPoints = aura != null && aura.ComboPoints > 0 ? aura.ComboPoints : info.ComboPoints,
                ProcAura = aura,
            };
            runningProcs.Add(p);
            try { ExecuteEffects(cast, p.effects); }
            finally { runningProcs.Remove(p); }
        }
    }
}
