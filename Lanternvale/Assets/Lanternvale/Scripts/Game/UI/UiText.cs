// Shared rich-text builders for tooltips (abilities, items, auras, talents) used by every UI screen.
// WoW-style tooltip layout on the dark Ink tooltip panel: quality-coloured name, slot/type line, stats in
// green, flavour in gold, requirements in red when unmet.
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class UiText
    {
        static readonly Color StatGreen = Ui.Hex("#5ef25e");
        static readonly Color Flavour = Ui.Hex("#ffd27a");
        static readonly Color Muted = Ui.Hex("#b8acc9");
        static readonly Color Red = Ui.Hex("#ff6b5e");

        public static string Ability(Unit u, AbilityDef a) => Ability(u, a, 0);

        /// <summary>Full ability tooltip at a rank (0 = the highest known rank; a lower rank describes that rank's cost,
        /// numbers and cast time — downranking).</summary>
        public static string Ability(Unit u, AbilityDef a, int rank)
        {
            if (a == null) return "";
            if (u == null) return $"<b>{a.name}</b>\n{a.description}";
            try { return Tooltip.AbilityFull(u, a, rank > 0 ? rank : 0); }
            catch (System.Exception) { return $"<b>{a.name}</b>\n{a.description}"; }
        }

        public static string Talent(TalentDef t, int rank)
        {
            if (t == null) return "";
            var sb = new StringBuilder();
            sb.Append("<b>").Append(t.name).Append("</b>\n");
            sb.Append(Ui.Rich($"Rank {rank}/{t.maxRank}", Muted)).Append('\n');
            try { sb.Append(Tooltip.Talent(t, rank)); }
            catch (System.Exception) { sb.Append(t.description); }
            return sb.ToString();
        }

        public static string Aura(AuraInstance a)
        {
            if (a == null || a.Def == null) return "";
            var sb = new StringBuilder();
            var col = a.Def.kind == AuraKind.Debuff ? Red : StatGreen;
            sb.Append(Ui.Rich("<b>" + a.Def.name + "</b>", col));
            if (a.Stacks > 1) sb.Append($" ({a.Stacks})");
            sb.Append('\n');
            if (!string.IsNullOrEmpty(a.Def.description)) sb.Append(a.Def.description).Append('\n');
            if (a.Def.dispel != DispelType.None) sb.Append(Ui.Rich(a.Def.dispel.ToString(), Muted)).Append('\n');
            if (a.Remaining > 0f && a.Duration > 0f) sb.Append(Ui.Rich(Duration(a.Remaining) + " remaining", Muted));
            return sb.ToString().TrimEnd('\n');
        }

        public static string Duration(float seconds)
        {
            if (seconds >= 3600f) return $"{Mathf.CeilToInt(seconds / 3600f)} hr";
            if (seconds >= 60f) return $"{Mathf.CeilToInt(seconds / 60f)} min";
            return $"{Mathf.CeilToInt(seconds)} sec";
        }

        /// <summary>Full item tooltip. compareTo = the item currently in that slot (shows a "Currently equipped" hint).</summary>
        public static string Item(ItemInstance it, Unit forUnit = null, ItemInstance compareTo = null)
        {
            if (it == null || it.Def == null) return "";
            var d = it.Def;
            var sb = new StringBuilder();
            sb.Append(Ui.Rich("<b>" + it.Name + "</b>", Ui.QualityColor(d.quality))).Append('\n');
            if (d.unique) sb.Append("Unique\n");
            if (d.equip != EquipType.None)
            {
                string slot = SlotName(d.equip);
                string type = d.weaponType != WeaponType.None ? Spaced(d.weaponType.ToString())
                            : d.armorType != ArmorType.None ? d.armorType.ToString() : "";
                sb.Append(slot);
                if (type.Length > 0) sb.Append("   ").Append(type);
                sb.Append('\n');
            }
            if (d.maxDamage > 0f)
            {
                var school = d.damageSchool != School.Physical ? " " + d.damageSchool : "";
                sb.Append($"{d.minDamage:0} - {d.maxDamage:0}{school} Damage     Speed {d.speed.ToString("0.00", CultureInfo.InvariantCulture)}\n");
                if (d.speed > 0f) sb.Append(Ui.Rich($"({(d.minDamage + d.maxDamage) * 0.5f / d.speed:0.0} damage per second)", Muted)).Append('\n');
            }
            if (d.armor > 0f) sb.Append($"{d.armor:0} Armor\n");
            if (d.block > 0f) sb.Append($"{d.block:0} Block\n");
            foreach (var s in it.Stats) sb.Append(StatLine(s)).Append('\n');
            foreach (var p in d.equipEffects)
            {
                if (p == null) continue;
                var line = p.type == "Proc" ? "Chance on hit: " : "Equip: ";
                sb.Append(Ui.Rich(line + PassiveText(p), StatGreen)).Append('\n');
            }
            if (!string.IsNullOrEmpty(d.use))
            {
                var useDef = GameRoot.Instance != null ? GameRoot.Instance.Db.Ability(d.use) : null;
                var useText = useDef != null ? (forUnit != null ? SafeAbilityText(forUnit, useDef) : useDef.description) : d.use;
                sb.Append(Ui.Rich("Use: " + useText, StatGreen)).Append('\n');
            }
            if (d.classes != null && d.classes.Length > 0)
            {
                var names = new List<string>();
                foreach (var c in d.classes) names.Add(c.ToString());
                bool ok = forUnit == null || System.Array.IndexOf(d.classes, forUnit.ClassId) >= 0;
                sb.Append(Ui.Rich("Classes: " + string.Join(", ", names), ok ? Color.white : Red)).Append('\n');
            }
            if (d.requiredLevel > 1)
            {
                bool ok = forUnit == null || forUnit.Level >= d.requiredLevel;
                sb.Append(Ui.Rich($"Requires Level {d.requiredLevel}", ok ? Color.white : Red)).Append('\n');
            }
            if (!string.IsNullOrEmpty(d.description)) sb.Append(Ui.Rich("\"" + d.description + "\"", Flavour)).Append('\n');
            if (it.Count > 1) sb.Append(Ui.Rich($"Stack: {it.Count}", Muted)).Append('\n');
            if (it.SellPrice > 0) sb.Append(Ui.Rich("Sell: ", Muted)).Append(Ui.Money(it.SellPrice * Mathf.Max(1, it.Count))).Append('\n');
            if (compareTo != null && compareTo != it) sb.Append(Ui.Rich("Currently equipped: " + compareTo.Name, Muted));
            return sb.ToString().TrimEnd('\n');
        }

        static string SafeAbilityText(Unit u, AbilityDef a)
        {
            try { return Tooltip.Ability(u, a); }
            catch (System.Exception) { return a.description; }
        }

        public static string StatLine(StatModDef s)
        {
            string name = StatName(s.stat, s.school);
            string sign = s.value >= 0 ? "+" : "";
            bool primary = s.stat <= StatId.AllStats || s.stat == StatId.Armor || s.stat == StatId.Resistance;
            string text = s.pct ? $"{sign}{s.value:0.#}% {name}" : $"{sign}{s.value:0.#} {name}";
            if (primary) return text;
            return Ui.Rich("Equip: " + text, StatGreen);
        }

        static string PassiveText(PassiveDef p)
        {
            switch (p.type)
            {
                case "Stat": return $"{(p.value >= 0 ? "+" : "")}{p.value:0.#}{(p.pct ? "%" : "")} {StatName(p.stat, p.school)}";
                case "GrantAbility":
                    var a = GameRoot.Instance != null ? GameRoot.Instance.Db.Ability(p.ability) : null;
                    return a != null ? a.description : p.ability;
                case "Proc":
                    if (p.proc != null && p.proc.effects.Count > 0)
                    {
                        var e = p.proc.effects[0];
                        var aura = e.type == EffectType.ApplyAura && GameRoot.Instance != null ? GameRoot.Instance.Db.Aura(e.aura) : null;
                        if (aura != null && !string.IsNullOrEmpty(aura.description)) return aura.description;
                        if (e.type == EffectType.Damage) return $"deals {e.min:0}{(e.max > e.min ? $" to {e.max:0}" : "")} {(e.school ?? School.Physical)} damage.";
                        if (e.type == EffectType.Heal) return $"heals {e.min:0}{(e.max > e.min ? $" to {e.max:0}" : "")}.";
                    }
                    return "a special effect.";
                default: return string.IsNullOrEmpty(p.special) ? p.type : Spaced(p.special);
            }
        }

        public static string StatName(StatId s, School? school)
        {
            switch (s)
            {
                case StatId.SpellDamage: return school.HasValue ? $"{school} Spell Damage" : "Spell Damage and Healing";
                case StatId.HealingPower: return "Healing";
                case StatId.Resistance: return school.HasValue ? $"{school} Resistance" : "All Resistances";
                case StatId.MeleeCrit: return "Critical Strike (melee)";
                case StatId.RangedCrit: return "Critical Strike (ranged)";
                case StatId.SpellCrit: return "Spell Critical Strike";
                case StatId.MeleeHit: return "Hit (melee)";
                case StatId.SpellHit: return "Spell Hit";
                case StatId.ManaRegen: return "Mana per 5 sec.";
                case StatId.HealthRegen: return "Health per 5 sec.";
                case StatId.AllStats: return "All Stats";
                default: return Spaced(s.ToString());
            }
        }

        public static string SlotName(EquipType e)
        {
            switch (e)
            {
                case EquipType.OneHand: return "One-Hand";
                case EquipType.TwoHand: return "Two-Hand";
                case EquipType.MainHand: return "Main Hand";
                case EquipType.OffHand: return "Off Hand";
                default: return e.ToString();
            }
        }

        /// <summary>"TwoHandSword" → "Two Hand Sword".</summary>
        public static string Spaced(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
