// Final stats of a unit (Design.md §3) computed from class/creature base values, items, auras, talents.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.Rules
{
    public enum WeaponSlot { MainHand, OffHand, Ranged }

    /// <summary>A weapon used for an attack: a real item or a virtual creature/unarmed weapon.</summary>
    public struct WeaponInfo
    {
        public bool Valid;
        public float Min, Max, Speed;
        public School School;
        public WeaponType Type;
        public bool Ranged;
        public bool Unarmed;
        public ItemInstance Item;
        public float Average => (Min + Max) * 0.5f;
        public override string ToString() => Valid ? $"{Min:0}-{Max:0} @{Speed:0.0}s {Type}" : "none";
    }

    public sealed class UnitStats
    {
        public readonly StatBlock Block = new StatBlock();
        public float Strength, Agility, Stamina, Intellect, Spirit;
        public float BaseStrength, BaseAgility, BaseStamina, BaseIntellect, BaseSpirit;
        public float MaxHealth, MaxMana, BaseMana, BaseHealth;
        public float Armor, AttackPower, RangedAttackPower, HealingPower;
        public float MeleeCrit, RangedCrit, Dodge, Parry, BlockChance, BlockValue, Defense;
        public float MeleeHit, RangedHit;
        public float MoveSpeedPct;
        public float MeleeHaste = 1f, RangedHaste = 1f, CastSpeed = 1f;
        public float ManaRegen, HealthRegen, SpiritRegenWhileCasting;
        /// <summary>Mana per 2 s tick from Spirit (outside the five-second rule).</summary>
        public float SpiritRegenPerTick;
        public float HealingDone = 1f, HealingTaken = 1f, EnergyRegen = 1f, RageGenerated = 1f;
        public float ArmorPenetration, DodgeChanceAgainstMe, ChanceToBeHit, StealthDetection;
        public float ExtraMaxEnergy;
        /// <summary>Spell damage (all schools) added by special passives (Spiritual Guidance).</summary>
        public float ExtraSpellDamage;
        public bool CanParry, CanBlock;
        ClassDef cls;
        int level;
        bool creature;

        internal void Init(ClassDef c, int lvl, bool isCreature) { cls = c; level = lvl; creature = isCreature; }

        public float SpellDamage(School s) => Block.Base(StatId.SpellDamage, 0f, s) + ExtraSpellDamage;
        public float SpellHit(School s) => Block.Sum(StatId.SpellHit, s);
        public float SpellPenetration(School s) => Block.Sum(StatId.SpellPenetration, s);
        public float Resistance(School s) => s == School.Physical ? 0f : Math.Max(0f, Block.Base(StatId.Resistance, 0f, s));
        public float DamageDone(School s) => Block.Multiplier(StatId.DamageDone, s);
        public float DamageTaken(School s) => Block.Multiplier(StatId.DamageTaken, s);
        public float Threat(School s) => Block.Multiplier(StatId.ThreatGenerated, s);
        public float CritDamageBonus(School s) => Block.Sum(StatId.CritDamage, s);
        public float ManaCostMult(School s) => Block.Multiplier(StatId.ManaCost, s);

        public float SpellCrit(School s)
        {
            float b = creature || cls == null ? 5f : Formulas.SpellCritFromIntellect(cls, Intellect, level);
            return b + Block.Sum(StatId.SpellCrit, s);
        }
    }

    public static class StatCalculator
    {
        /// <summary>Value of a passive/stat entry at a rank: values[rank-1] if given, else value × rank.</summary>
        public static float RankValue(float value, float[] values, int rank)
        {
            if (rank <= 0) return 0f;
            if (values != null && values.Length > 0) return values[Math.Min(rank, values.Length) - 1];
            return value * rank;
        }

        public static UnitStats Compute(Unit u)
        {
            var s = new UnitStats();
            var b = s.Block;
            var c = u.Class;
            var cr = u.Creature;
            bool isCreature = c == null;
            s.Init(c, u.Level, isCreature);

            // ---- base primary stats
            if (c != null)
            {
                s.BaseStrength = Formulas.LevelLerp(c.baseStatsLevel1.strength, c.baseStatsLevel60.strength, u.Level);
                s.BaseAgility = Formulas.LevelLerp(c.baseStatsLevel1.agility, c.baseStatsLevel60.agility, u.Level);
                s.BaseStamina = Formulas.LevelLerp(c.baseStatsLevel1.stamina, c.baseStatsLevel60.stamina, u.Level);
                s.BaseIntellect = Formulas.LevelLerp(c.baseStatsLevel1.intellect, c.baseStatsLevel60.intellect, u.Level);
                s.BaseSpirit = Formulas.LevelLerp(c.baseStatsLevel1.spirit, c.baseStatsLevel60.spirit, u.Level);
                if (u.Companion != null && u.Companion.statBonus != null)
                {
                    var sb = u.Companion.statBonus;
                    s.BaseStrength += sb.strength; s.BaseAgility += sb.agility; s.BaseStamina += sb.stamina;
                    s.BaseIntellect += sb.intellect; s.BaseSpirit += sb.spirit;
                }
            }
            else
            {
                float p = CreatureScaling.PrimaryStat(u.Level);
                s.BaseStrength = s.BaseAgility = s.BaseStamina = s.BaseIntellect = s.BaseSpirit = p;
            }

            // ---- items
            float itemBlock = 0f;
            foreach (var kv in u.Equipment.Equipped)
            {
                var it = kv.Value;
                if (it.Def.armor != 0) b.Add(StatId.Armor, it.Def.armor, false);
                if (it.Def.block != 0) itemBlock += it.Def.block;
                foreach (var m in it.Stats) b.Add(m.stat, RankValue(m.value, m.values, 1), m.pct, m.school);
                foreach (var p in it.Def.equipEffects)
                    if (p.type == "Stat" && (p.target == null || p.target == "Self"))
                        b.Add(p.stat, RankValue(p.value, p.values, 1), p.pct, p.school);
            }

            // ---- talents (own, and the owner's pet talents for pets)
            if (u.Db != null)
            {
                foreach (var kv in u.Talents)
                {
                    var t = u.Db.Talent(kv.Key);
                    if (t == null || kv.Value <= 0) continue;
                    foreach (var p in t.effects)
                        if (p.type == "Stat" && !IsPetTarget(p.target))
                            b.Add(p.stat, RankValue(p.value, p.values, kv.Value), p.pct, p.school);
                }
                var owner = u.Owner;
                if (owner != null && u.Kind != UnitKind.Totem)
                {
                    foreach (var kv in owner.Talents)
                    {
                        var t = u.Db.Talent(kv.Key);
                        if (t == null || kv.Value <= 0) continue;
                        foreach (var p in t.effects)
                            if (p.type == "Stat" && IsPetTarget(p.target))
                                b.Add(p.stat, RankValue(p.value, p.values, kv.Value), p.pct, p.school);
                    }
                }
            }

            // ---- auras
            foreach (var a in u.Auras)
            {
                var mods = a.Def.mods;
                for (int i = 0; i < mods.Count; i++)
                {
                    var m = mods[i];
                    if (IsPetTarget(m.target)) continue;
                    float v = i < a.ModValues.Length ? a.ModValues[i] : AuraModValue(m, a.Rank, a.EffLevel, a.LearnLevel, a.EffectMult);
                    b.Add(m.stat, v * Math.Max(1, a.Stacks), m.pct, m.school);
                }
                if (a.ExtraMods != null)
                    foreach (var m in a.ExtraMods)
                        if (!IsPetTarget(m.target)) b.Add(m.stat, m.value * Math.Max(1, a.Stacks), m.pct, m.school);
                if (a.Def.states.Length > 0)
                {
                    foreach (var st in a.Def.states)
                        if (st == UnitState.Daze) b.Add(StatId.MoveSpeed, -50f, true);
                }
            }
            // auras on the owner that buff the pet ("target": "Pet" mods)
            if (u.Owner != null)
            {
                foreach (var a in u.Owner.Auras)
                {
                    var mods = a.Def.mods;
                    for (int i = 0; i < mods.Count; i++)
                    {
                        var m = mods[i];
                        if (!IsPetTarget(m.target)) continue;
                        float v = i < a.ModValues.Length ? a.ModValues[i] : AuraModValue(m, a.Rank, a.EffLevel, a.LearnLevel, a.EffectMult);
                        b.Add(m.stat, v * Math.Max(1, a.Stacks), m.pct, m.school);
                    }
                }
            }

            // ---- creature stat lines
            if (cr != null)
                foreach (var m in cr.stats) b.Add(m.stat, RankValue(m.value, m.values, 1), m.pct, m.school);

            // ---- special passives (talent/aura/item specials may add modifiers)
            Specials.ContributeStats(u, b);

            // ---- primaries
            s.Strength = b.Base(StatId.Strength, s.BaseStrength);
            s.Agility = b.Base(StatId.Agility, s.BaseAgility);
            s.Stamina = b.Base(StatId.Stamina, s.BaseStamina);
            s.Intellect = b.Base(StatId.Intellect, s.BaseIntellect);
            s.Spirit = b.Base(StatId.Spirit, s.BaseSpirit);

            // ---- health / mana
            if (c != null)
            {
                s.BaseHealth = Formulas.LevelLerp(c.baseHealthLevel1, c.baseHealthLevel60, u.Level);
                s.MaxHealth = b.Base(StatId.Health, s.BaseHealth + Formulas.HealthFromStamina(s.Stamina));
                if (c.resource == ResourceType.Mana)
                {
                    s.BaseMana = Formulas.LevelLerp(c.baseManaLevel1, c.baseManaLevel60, u.Level);
                    s.MaxMana = b.Base(StatId.Mana, s.BaseMana + Formulas.ManaFromIntellect(s.Intellect));
                }
            }
            else if (cr != null)
            {
                s.BaseHealth = CreatureScaling.Health(cr, u.Level);
                s.MaxHealth = b.Base(StatId.Health, s.BaseHealth);
                s.BaseMana = CreatureScaling.Mana(cr, u.Level);
                if (s.BaseMana > 0) s.MaxMana = b.Base(StatId.Mana, s.BaseMana);
            }
            s.MaxHealth *= u.MaxHealthMult;
            s.MaxHealth = Math.Max(1f, (float)Math.Round(s.MaxHealth));
            s.MaxMana = Math.Max(0f, (float)Math.Round(s.MaxMana));

            // ---- armor / attack power
            float baseArmor = c != null ? s.Agility * 2f : (cr != null ? CreatureScaling.Armor(cr, u.Level) : 0f);
            s.Armor = Math.Max(0f, b.Base(StatId.Armor, baseArmor));
            if (c != null)
            {
                s.AttackPower = Math.Max(0f, b.Base(StatId.AttackPower, Formulas.MeleeAttackPower(c.meleeAp, s.Strength, s.Agility, u.Level)));
                s.RangedAttackPower = Math.Max(0f, b.Base(StatId.RangedAttackPower, Formulas.RangedAttackPower(c, s.Agility, u.Level)));
            }
            else
            {
                s.AttackPower = Math.Max(0f, b.Base(StatId.AttackPower, 0f));
                s.RangedAttackPower = Math.Max(0f, b.Base(StatId.RangedAttackPower, 0f));
            }
            s.HealingPower = Math.Max(0f, b.Base(StatId.HealingPower, 0f));

            // ---- crit / avoidance
            s.Defense = b.Sum(StatId.Defense);
            float defBonus = s.Defense * 0.04f;
            if (c != null)
            {
                float mc = Formulas.MeleeCritFromAgility(c, s.Agility, u.Level);
                s.MeleeCrit = mc + b.Sum(StatId.MeleeCrit);
                s.RangedCrit = mc + b.Sum(StatId.RangedCrit);
                s.Dodge = Formulas.DodgeFromAgility(c, s.Agility, u.Level) + b.Sum(StatId.Dodge) + defBonus;
                s.CanParry = c.canParry && u.Equipment.HasMeleeWeapon;
                s.Parry = s.CanParry ? 5f + b.Sum(StatId.Parry) + defBonus : 0f;
                s.CanBlock = c.canBlock && u.Equipment.HasShield;
                s.BlockChance = s.CanBlock ? 5f + b.Sum(StatId.Block) + defBonus : 0f;
            }
            else
            {
                s.MeleeCrit = 5f + b.Sum(StatId.MeleeCrit);
                s.RangedCrit = 5f + b.Sum(StatId.RangedCrit);
                s.Dodge = (cr != null && cr.rank == CreatureRank.Totem ? 0f : 5f) + b.Sum(StatId.Dodge) + defBonus;
                s.CanParry = cr != null && CreatureScaling.CanParry(cr) && u.Kind != UnitKind.Pet;
                s.Parry = s.CanParry ? 5f + b.Sum(StatId.Parry) + defBonus : 0f;
                float blk = b.Sum(StatId.Block);
                s.CanBlock = blk > 0f;
                s.BlockChance = s.CanBlock ? blk + defBonus : 0f;
            }
            s.BlockValue = Math.Max(0f, b.Base(StatId.BlockValue, itemBlock + s.Strength / 20f));
            s.MeleeHit = b.Sum(StatId.MeleeHit);
            s.RangedHit = b.Sum(StatId.RangedHit);

            // ---- speed / haste
            s.MoveSpeedPct = b.MoveSpeedPct;
            s.MeleeHaste = b.Multiplier(StatId.MeleeHaste);
            s.RangedHaste = b.Multiplier(StatId.RangedHaste);
            s.CastSpeed = b.Multiplier(StatId.CastSpeed);

            // ---- regen
            s.ManaRegen = b.Base(StatId.ManaRegen, 0f);
            s.HealthRegen = b.Base(StatId.HealthRegen, 0f);
            s.SpiritRegenWhileCasting = MathUtil.Clamp(b.Sum(StatId.SpiritRegenWhileCasting), 0f, 100f);
            if (c != null && c.resource == ResourceType.Mana)
                s.SpiritRegenPerTick = c.manaRegenBase + s.Spirit * c.manaRegenPerSpirit;

            // ---- misc multipliers
            s.HealingDone = b.Multiplier(StatId.HealingDone);
            s.HealingTaken = b.Multiplier(StatId.HealingTaken);
            s.EnergyRegen = b.Multiplier(StatId.EnergyRegen);
            s.RageGenerated = b.Multiplier(StatId.RageGenerated);
            s.ArmorPenetration = b.Sum(StatId.ArmorPenetration);
            s.DodgeChanceAgainstMe = b.Sum(StatId.DodgeChanceAgainstMe);
            s.ChanceToBeHit = b.Sum(StatId.ChanceToBeHit);
            s.StealthDetection = b.Sum(StatId.StealthDetection);

            Specials.AdjustStats(u, s);
            return s;
        }

        static bool IsPetTarget(string target) => string.Equals(target, "Pet", StringComparison.OrdinalIgnoreCase);

        /// <summary>Resolved value of an aura stat modifier for the applying ability's rank/level.</summary>
        public static float AuraModValue(StatModDef m, int rank, int effLevel, int learnLevel, float effectMult)
        {
            float v;
            if (m.values != null && m.values.Length > 0) v = m.values[MathUtil.Clamp(rank, 1, m.values.Length) - 1];
            else v = m.value + m.perLevel * Math.Max(0, effLevel - learnLevel);
            return v * effectMult;
        }

        // ------------------------------------------------------------------ weapons

        /// <summary>Weapon used for an attack in the slot (virtual for creatures, fists when unarmed or disarmed).</summary>
        public static WeaponInfo GetWeapon(Unit u, WeaponSlot slot)
        {
            var w = new WeaponInfo();
            bool disarmed = u.HasStateAura(UnitState.Disarm) && slot != WeaponSlot.Ranged;
            if (u.Class != null)
            {
                ItemInstance it = slot == WeaponSlot.MainHand ? u.Equipment.MainHand : slot == WeaponSlot.OffHand ? u.Equipment.OffHand : u.Equipment.Ranged;
                if (it != null && it.IsWeapon && !disarmed)
                {
                    w.Valid = true; w.Item = it;
                    w.Min = it.Def.minDamage; w.Max = Math.Max(it.Def.minDamage, it.Def.maxDamage);
                    w.Speed = it.Def.speed > 0 ? it.Def.speed : 2f;
                    w.School = it.Def.damageSchool; w.Type = it.Def.weaponType;
                    w.Ranged = slot == WeaponSlot.Ranged;
                    return w;
                }
                if (slot == WeaponSlot.MainHand)
                {
                    w.Valid = true; w.Unarmed = true;
                    w.Min = 1f + u.Level * 0.25f; w.Max = 2f + u.Level * 0.5f; w.Speed = 2f;
                    w.School = School.Physical; w.Type = WeaponType.None;
                }
                return w;
            }
            var cr = u.Creature;
            if (cr == null) return w;
            if (slot == WeaponSlot.OffHand) return w;
            if (slot == WeaponSlot.Ranged && !cr.ranged) return w;
            float avg = CreatureScaling.SwingAverage(cr, u.Level);
            if (disarmed) avg *= 0.4f;
            w.Valid = true;
            w.Min = avg * 0.85f; w.Max = avg * 1.15f;
            w.Speed = Math.Max(0.5f, cr.attackSpeed);
            w.School = cr.meleeSchool;
            w.Type = WeaponType.None;
            w.Ranged = slot == WeaponSlot.Ranged;
            return w;
        }
    }

    /// <summary>Infers a combat role from class, talents, creature AI profile and pet family.</summary>
    public static class RoleInference
    {
        public static UnitRole Infer(Unit u)
        {
            if (u.Class == null)
            {
                if (u.Kind == UnitKind.Totem) return UnitRole.RangedDps;
                var cr = u.Creature;
                if (cr == null) return UnitRole.MeleeDps;
                var fam = (cr.family ?? "").ToLowerInvariant();
                if (u.Kind == UnitKind.Pet && (fam.Contains("voidwalker") || fam.Contains("bear") || fam.Contains("turtle"))) return UnitRole.Tank;
                switch (cr.ai)
                {
                    case AIProfile.Healer: return UnitRole.Healer;
                    case AIProfile.Ranged:
                    case AIProfile.Caster: return UnitRole.RangedDps;
                    default: return cr.ranged ? UnitRole.RangedDps : UnitRole.MeleeDps;
                }
            }
            // talents: dominant tree decides
            string dominant = null;
            int best = 0;
            if (u.Db != null && u.Talents.Count > 0)
            {
                var perTree = new Dictionary<string, int>();
                foreach (var kv in u.Talents)
                {
                    if (!u.Db.TreeOfTalent.TryGetValue(kv.Key, out var tree)) continue;
                    perTree.TryGetValue(tree.id, out var n);
                    perTree[tree.id] = n + kv.Value;
                }
                foreach (var kv in perTree)
                    if (kv.Value > best) { best = kv.Value; dominant = (u.Db.TalentTrees[kv.Key].name + " " + kv.Key).ToLowerInvariant(); }
            }
            var cls = u.Class.id;
            if (dominant != null && best >= 5)
            {
                if (dominant.Contains("protection") && (cls == ClassId.Warrior || cls == ClassId.Paladin)) return UnitRole.Tank;
                if (dominant.Contains("holy") || dominant.Contains("discipline") || dominant.Contains("restoration")) return UnitRole.Healer;
                if (cls == ClassId.Priest && dominant.Contains("shadow")) return UnitRole.RangedDps;
                if (cls == ClassId.Shaman) return dominant.Contains("enhancement") ? UnitRole.MeleeDps : UnitRole.RangedDps;
            }
            switch (cls)
            {
                case ClassId.Warrior: return dominant != null && best >= 5 ? UnitRole.MeleeDps : UnitRole.Tank;
                case ClassId.Priest: return UnitRole.Healer;
                case ClassId.Paladin: return UnitRole.MeleeDps;
                case ClassId.Shaman: return UnitRole.MeleeDps;
                case ClassId.Rogue: return UnitRole.MeleeDps;
                default: return UnitRole.RangedDps;
            }
        }
    }
}
