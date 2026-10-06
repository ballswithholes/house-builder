// d20 skill checks (Design.md §6). Pure C#.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.World
{
    public enum StatKind { Strength, Agility, Stamina, Intellect, Spirit }

    /// <summary>Full breakdown of a rolled skill check, for the dice UI.</summary>
    [Serializable]
    public sealed class CheckResult
    {
        public SkillCheck Skill;
        public int Dc;
        public string RollerId = "";
        public string RollerName = "";
        public ClassId RollerClass;
        /// <summary>Natural d20 (1-20).</summary>
        public int Roll;
        /// <summary>floor((stat − 20) / 10).</summary>
        public int StatModifier;
        /// <summary>+2 when the roller's class is proficient, else 0.</summary>
        public int Proficiency;
        /// <summary>Context bonus (items, buffs).</summary>
        public int Bonus;
        /// <summary>StatModifier + Proficiency + Bonus.</summary>
        public int Modifier;
        public int Total;
        public bool Success;
        /// <summary>Natural 20 (always succeeds).</summary>
        public bool Critical;
        /// <summary>Natural 1 (always fails).</summary>
        public bool Fumble;

        public override string ToString() =>
            $"{Skill} DC {Dc}: {RollerName} rolled {Roll} {(Modifier >= 0 ? "+" : "-")} {Math.Abs(Modifier)} = {Total} → {(Success ? "success" : "failure")}{(Critical ? " (critical)" : Fumble ? " (fumble)" : "")}";
    }

    /// <summary>What a check would look like before rolling (choice tooltips).</summary>
    [Serializable]
    public sealed class CheckPreview
    {
        public SkillCheck Skill;
        public int Dc;
        public string RollerId = "";
        public string RollerName = "";
        public int Modifier;
        /// <summary>0..1 including natural 1/20 rules.</summary>
        public float SuccessChance;
    }

    public static class SkillChecks
    {
        public const int ProficiencyBonus = 2;

        /// <summary>Primary stat used by a skill.</summary>
        public static StatKind StatFor(SkillCheck skill)
        {
            switch (skill)
            {
                case SkillCheck.Athletics:
                case SkillCheck.Intimidation:
                    return StatKind.Strength;
                case SkillCheck.Acrobatics:
                case SkillCheck.Stealth:
                case SkillCheck.SleightOfHand:
                    return StatKind.Agility;
                case SkillCheck.Endurance:
                    return StatKind.Stamina;
                case SkillCheck.Arcana:
                case SkillCheck.History:
                case SkillCheck.Investigation:
                    return StatKind.Intellect;
                default:
                    return StatKind.Spirit; // Insight, Persuasion, Religion, Nature, Survival, Perception
            }
        }

        /// <summary>Class proficiencies (Design.md §6).</summary>
        public static bool IsProficient(ClassId c, SkillCheck skill)
        {
            switch (c)
            {
                case ClassId.Warrior:
                case ClassId.Paladin:
                    return skill == SkillCheck.Athletics || skill == SkillCheck.Intimidation;
                case ClassId.Rogue:
                    return skill == SkillCheck.Stealth || skill == SkillCheck.SleightOfHand || skill == SkillCheck.Acrobatics
                        || skill == SkillCheck.Perception;
                case ClassId.Hunter:
                    return skill == SkillCheck.Survival || skill == SkillCheck.Nature || skill == SkillCheck.Perception;
                case ClassId.Mage:
                    return skill == SkillCheck.Arcana || skill == SkillCheck.History;
                case ClassId.Priest:
                    return skill == SkillCheck.Religion || skill == SkillCheck.Insight;
                case ClassId.Warlock:
                    return skill == SkillCheck.Arcana || skill == SkillCheck.Intimidation;
                case ClassId.Shaman:
                    return skill == SkillCheck.Nature || skill == SkillCheck.Insight;
                default:
                    return false;
            }
        }

        public static float StatValue(PrimaryStats s, StatKind kind)
        {
            if (s == null) return 0;
            switch (kind)
            {
                case StatKind.Strength: return s.strength;
                case StatKind.Agility: return s.agility;
                case StatKind.Stamina: return s.stamina;
                case StatKind.Intellect: return s.intellect;
                default: return s.spirit;
            }
        }

        /// <summary>floor((stat − 20) / 10).</summary>
        public static int StatModifier(float stat) => (int)Math.Floor((stat - 20f) / 10f);

        /// <summary>Stat modifier + proficiency (no context bonus).</summary>
        public static int Modifier(PartyMemberInfo m, SkillCheck skill)
        {
            if (m == null) return 0;
            return StatModifier(StatValue(m.stats, StatFor(skill))) + (IsProficient(m.classId, skill) ? ProficiencyBonus : 0);
        }

        /// <summary>Chance (0..1) that d20 + modifier ≥ dc with natural 1 = fail, natural 20 = success.</summary>
        public static float SuccessChance(int modifier, int dc)
        {
            int needed = dc - modifier; // natural roll needed
            if (needed <= 2) return 0.95f;
            if (needed > 20) return 0.05f;
            return (21 - needed) / 20f;
        }

        /// <summary>Party member with the best total modifier (ties → earlier in the party). Null for an empty party.</summary>
        public static PartyMemberInfo BestRoller(IDialogueContext ctx, SkillCheck skill, out int totalModifier)
        {
            totalModifier = 0;
            var party = ctx?.Party;
            if (party == null || party.Count == 0) return null;
            PartyMemberInfo best = null;
            int bestMod = int.MinValue;
            for (int i = 0; i < party.Count; i++)
            {
                var m = party[i];
                if (m == null) continue;
                int mod = Modifier(m, skill) + ctx.SkillCheckBonus(m.id, skill);
                if (mod > bestMod) { bestMod = mod; best = m; }
            }
            totalModifier = best != null ? bestMod : 0;
            return best;
        }

        public static CheckPreview Preview(IDialogueContext ctx, SkillCheck skill, int dc)
        {
            var best = BestRoller(ctx, skill, out int mod);
            return new CheckPreview
            {
                Skill = skill,
                Dc = dc,
                RollerId = best?.id ?? "",
                RollerName = best?.name ?? "",
                Modifier = mod,
                SuccessChance = SuccessChance(mod, dc),
            };
        }

        /// <summary>Rolls d20 for the best party member against dc.</summary>
        public static CheckResult Roll(IDialogueContext ctx, SkillCheck skill, int dc, Rng rng)
        {
            var best = BestRoller(ctx, skill, out _);
            var r = new CheckResult { Skill = skill, Dc = dc };
            if (best != null)
            {
                r.RollerId = best.id;
                r.RollerName = best.name;
                r.RollerClass = best.classId;
                r.StatModifier = StatModifier(StatValue(best.stats, StatFor(skill)));
                r.Proficiency = IsProficient(best.classId, skill) ? ProficiencyBonus : 0;
                r.Bonus = ctx.SkillCheckBonus(best.id, skill);
            }
            r.Modifier = r.StatModifier + r.Proficiency + r.Bonus;
            r.Roll = (rng ?? new Rng()).D20();
            r.Total = r.Roll + r.Modifier;
            r.Critical = r.Roll == 20;
            r.Fumble = r.Roll == 1;
            r.Success = r.Critical || (!r.Fumble && r.Total >= dc);
            return r;
        }

        /// <summary>Upper-case display name: SleightOfHand → "SLEIGHT OF HAND".</summary>
        public static string DisplayName(SkillCheck skill)
        {
            var s = skill.ToString();
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(s[i]));
            }
            return sb.ToString();
        }
    }
}
