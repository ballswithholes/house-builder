// Shared rich-text builders for tooltips (abilities, items, auras, talents) used by every UI screen.
// WoW-style tooltip layout on the dark Ink tooltip panel: quality-coloured name, slot/type line, stats in
// green, flavour in gold, requirements in red when unmet. Equip effects are worded by kind (stats, ability mods, procs
// by their trigger with chance / procs per minute / cooldown, specials), PassiveDef.description first; set items show
// their set (pieces the hovered member wears lit, bonuses green while active).
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
        static readonly Color SetGold = Ui.Hex("#ffd100");
        static readonly Color SetLit = Ui.Hex("#ffe9a6");
        static readonly Color SetGrey = Ui.Hex("#7d7d7d");

        static GameDatabase Db => GameRoot.Instance != null ? GameRoot.Instance.Db : null;

        public static string Ability(Unit u, AbilityDef a) => Ability(u, a, 0);

        /// <summary>Full ability tooltip at a rank (0 = the highest known rank; a lower rank describes that rank's cost,
        /// numbers and cast time — downranking).</summary>
        public static string Ability(Unit u, AbilityDef a, int rank)
        {
            if (a == null) return "";
            if (u == null)   // no unit: rank-1 numbers ({0}, {0@3} tokens resolved without stats), never raw tokens
            {
                try { return $"<b>{a.name}</b>\n{Tooltip.Ability(null, a, rank > 0 ? rank : 0)}"; }
                catch (System.Exception) { return $"<b>{a.name}</b>\n{a.description}"; }
            }
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
            if (d.equip != EquipType.None && d.itemLevel > 0) sb.Append(Ui.Rich($"Item Level {d.itemLevel}", Flavour)).Append('\n');
            if (d.unique) sb.Append("Unique\n");
            if (d.equip != EquipType.None)
            {
                string slot = SlotName(d.equip);
                string type = d.weaponType != WeaponType.None ? WeaponTypeName(d.weaponType)
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
                sb.Append(Ui.Rich(EffectLine(p), StatGreen)).Append('\n');
            }
            var setBlock = SetBlock(Db != null ? Db.SetOf(d.id) : null, forUnit);
            if (setBlock.Length > 0) sb.Append('\n').Append(setBlock).Append("\n\n");   // a paragraph of its own (tooltip columns)
            if (!string.IsNullOrEmpty(d.use))
            {
                var useDef = Db != null ? Db.Ability(d.use) : null;
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

        /// <summary>
        /// One equip-effect line with its prefix: "Equip: +5 Strength", "Equip: Reduces the cost of Heal by 5%.",
        /// "Chance on hit: …" (melee / ranged hit procs), "When struck: …" or "Equip: Chance on spell cast: …" (other
        /// procs), followed by the proc's chance, procs per minute and cooldown. A non-empty PassiveDef.description
        /// replaces the generated text; when it starts with its own prefix ("Equip:", "Chance on", "When struck:", "Use:")
        /// it is the whole line.
        /// </summary>
        public static string EffectLine(PassiveDef p)
        {
            if (p == null) return "";
            string tail = p.type == "Proc" ? ProcOdds(p) : "";
            if (!string.IsNullOrEmpty(p.description))
            {
                var desc = p.description.Trim();
                bool own = desc.StartsWith("Equip:") || desc.StartsWith("Chance on") || desc.StartsWith("When struck:") || desc.StartsWith("Use:");
                return (own ? desc : EffectPrefix(p) + desc) + tail;
            }
            return EffectPrefix(p) + PassiveText(p) + tail;
        }

        static string EffectPrefix(PassiveDef p)
        {
            if (p.type != "Proc" || p.proc == null) return "Equip: ";
            switch (p.proc.trigger)
            {
                case ProcTrigger.OnMeleeHit: case ProcTrigger.OnAutoAttackHit: case ProcTrigger.OnRangedHit: return "Chance on hit: ";
                case ProcTrigger.OnStruck: return "When struck: ";
                default: return "Equip: Chance on " + TriggerWords(p.proc.trigger) + ": ";
            }
        }

        /// <summary>A proc trigger in tooltip words ("spell cast", "critical strike"…).</summary>
        public static string TriggerWords(ProcTrigger t)
        {
            switch (t)
            {
                case ProcTrigger.OnMeleeHit: return "melee hit";
                case ProcTrigger.OnAutoAttackHit: return "auto attack hit";
                case ProcTrigger.OnRangedHit: return "ranged hit";
                case ProcTrigger.OnSpellHit: return "spell hit";
                case ProcTrigger.OnSpellCast: return "spell cast";
                case ProcTrigger.OnHealCast: return "healing spell";
                case ProcTrigger.OnCrit: return "critical strike";
                case ProcTrigger.OnMeleeCrit: return "melee critical strike";
                case ProcTrigger.OnSpellCrit: return "spell critical strike";
                case ProcTrigger.OnStruck: return "being struck";
                case ProcTrigger.OnDamaged: return "taking damage";
                case ProcTrigger.OnCritTaken: return "being critically hit";
                case ProcTrigger.OnDodge: return "dodge";
                case ProcTrigger.OnParry: return "parry";
                case ProcTrigger.OnBlock: return "block";
                case ProcTrigger.OnTargetDodged: return "your attack being dodged";
                case ProcTrigger.OnTargetParried: return "your attack being parried";
                case ProcTrigger.OnKill: return "killing blow";
                case ProcTrigger.OnFinisher: return "finishing move";
                case ProcTrigger.OnAbilityUsed: return "ability use";
                case ProcTrigger.OnTurnStart: return "turn start";
                case ProcTrigger.OnTurnEnd: return "turn end";
                case ProcTrigger.OnBattleStart: return "entering battle";
                case ProcTrigger.OnPeriodicDamage: return "periodic damage";
                default: return Spaced(t.ToString()).ToLowerInvariant();
            }
        }

        /// <summary>" (5% chance, 30 sec cooldown)", " (2 procs per minute)", or "" for a sure proc without a cooldown.</summary>
        static string ProcOdds(PassiveDef p)
        {
            if (p.proc == null) return "";
            var parts = new List<string>(2);
            float chance = p.values != null && p.values.Length > 0 ? p.values[0] : p.proc.chance;
            if (p.proc.ppm > 0f) parts.Add(p.proc.ppm.ToString("0.#", CultureInfo.InvariantCulture) + " procs per minute");
            else if (chance > 0f && chance < 100f) parts.Add(chance.ToString("0.#", CultureInfo.InvariantCulture) + "% chance");
            if (p.proc.internalCooldown > 0f) parts.Add(Duration(p.proc.internalCooldown) + " cooldown");
            return parts.Count > 0 ? " (" + string.Join(", ", parts) + ")" : "";
        }

        static string PassiveText(PassiveDef p)
        {
            switch (p.type)
            {
                case "Stat": return $"{(p.value >= 0 ? "+" : "")}{p.value:0.#}{(p.pct ? "%" : "")} {StatName(p.stat, p.school)}";
                case "AbilityMod": return AbilityModText(p);
                case "GrantAbility":
                    var a = Db != null ? Db.Ability(p.ability) : null;
                    return a != null ? a.description : p.ability;
                case "Proc": return ProcText(p.proc);
                default: return string.IsNullOrEmpty(p.special) ? p.type : Spaced(p.special);
            }
        }

        /// <summary>What a proc does, from its first effect (an aura's description, damage, healing, a triggered ability…).</summary>
        static string ProcText(ProcDef proc)
        {
            if (proc == null || proc.effects.Count == 0 || proc.effects[0] == null) return "a special effect.";
            var db = Db;
            var e = proc.effects[0];
            string range = $"{e.min:0}{(e.max > e.min ? $" to {e.max:0}" : "")}";
            switch (e.type)
            {
                case EffectType.ApplyAura:
                {
                    var aura = db != null ? db.Aura(e.aura) : null;
                    if (aura != null && !string.IsNullOrEmpty(aura.description)) return aura.description;
                    if (aura != null) return (e.target == EffectTarget.Self ? "grants " : "inflicts ") + aura.name + ".";
                    break;
                }
                case EffectType.Damage:
                {
                    string chain = e.chainTargets > 0 ? $", jumping to {e.chainTargets} more {(e.chainTargets == 1 ? "target" : "targets")}" : "";
                    return $"deals {range} {(e.school ?? School.Physical)} damage{chain}.";
                }
                case EffectType.Heal:
                    return e.pctOfMax > 0f ? $"heals {e.pctOfMax:0.#}% of maximum health." : $"heals {range}.";
                case EffectType.WeaponDamage: return "grants an extra attack.";
                case EffectType.TriggerAbility:
                {
                    var ab = db != null ? db.Ability(e.ability) : null;
                    if (ab != null && !string.IsNullOrEmpty(ab.description)) return ab.description;
                    if (ab != null) return "casts " + ab.name + ".";
                    break;
                }
            }
            return "a special effect.";
        }

        /// <summary>An AbilityMod in words: "Reduces the cost of Heroic Strike by 50%.", "Increases the damage of your Fire spells by 5%."</summary>
        public static string AbilityModText(PassiveDef p)
        {
            if (p == null) return "";
            float v = StatCalculator.RankValue(p.value, p.values, 1);
            bool up = v >= 0f;
            float a = Mathf.Abs(v);
            string what = ModTargets(p);
            string num = a.ToString("0.#", CultureInfo.InvariantCulture);
            string pct = num + "%", sec = num + " sec";
            string verb = up ? "Increases" : "Reduces";
            switch (p.property)
            {
                case AbilityProperty.Damage: return $"{verb} the damage of {what} by {pct}.";
                case AbilityProperty.Healing: return $"{verb} the healing of {what} by {pct}.";
                case AbilityProperty.Effect: return $"{verb} the effect of {what} by {pct}.";
                case AbilityProperty.CritChance: return $"{verb} the critical strike chance of {what} by {pct}.";
                case AbilityProperty.CritBonus: return $"{verb} the critical strike bonus of {what} by {pct}.";
                case AbilityProperty.Cost: return $"{verb} the cost of {what} by {pct}.";
                case AbilityProperty.CostFlat: return $"{verb} the cost of {what} by {num}.";
                case AbilityProperty.Cooldown: return $"{verb} the cooldown of {what} by {sec}.";
                case AbilityProperty.CastTime: return $"{verb} the casting time of {what} by {sec}.";
                case AbilityProperty.Range: return $"{verb} the range of {what} by {pct}.";
                case AbilityProperty.Radius: return $"{verb} the radius of {what} by {pct}.";
                case AbilityProperty.Duration: return $"{verb} the duration of {what} by {sec}.";
                case AbilityProperty.DurationPct: return $"{verb} the duration of {what} by {pct}.";
                case AbilityProperty.Threat: return $"{verb} the threat caused by {what} by {pct}.";
                case AbilityProperty.HitChance: return $"{verb} the chance to hit with {what} by {pct}.";
                case AbilityProperty.Charges: return up ? $"Adds {num} {(a == 1f ? "charge" : "charges")} to {what}." : $"Removes {num} {(a == 1f ? "charge" : "charges")} from {what}.";
                case AbilityProperty.ComboPoints: return $"{Capital(what)} award {num} {(up ? "extra" : "fewer")} combo {(a == 1f ? "point" : "points")}.";
                case AbilityProperty.Gcd: return $"{verb} the global cooldown of {what} by {sec}.";
                default: return $"{verb} the {Spaced(p.property.ToString()).ToLowerInvariant()} of {what} by {num}.";
            }
        }

        /// <summary>"Heroic Strike", "your Heal abilities", "your Fire spells" or "your abilities" (an AbilityMod's filters).</summary>
        static string ModTargets(PassiveDef p)
        {
            var names = new List<string>();
            var db = Db;
            if (p.abilities != null)
                foreach (var id in p.abilities)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    var ab = db != null ? db.Ability(id) : null;
                    names.Add(ab != null ? ab.name : Spaced(id));
                }
            if (p.tags != null) foreach (var t in p.tags) if (!string.IsNullOrEmpty(t)) names.Add("your " + Spaced(t) + " abilities");
            if (p.schools != null) foreach (var sc in p.schools) names.Add("your " + sc + " spells");
            if (names.Count == 0) return "your abilities";
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1];
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // ------------------------------------------------------------------ item sets

        /// <summary>
        /// The set block of an item tooltip ("" when the item is in no set): the set name with the distinct pieces the
        /// member wears ("Emberwatch Regalia (2/5)"), every piece (lit when the member wears it, else grey), then every
        /// bonus, green while active and grey otherwise ("(2) Set: +8 Intellect"). forUnit null: nothing is worn.
        /// </summary>
        public static string SetBlock(ItemSetDef set, Unit forUnit)
        {
            if (set == null) return "";
            var p = ItemSets.Progress(forUnit, set);
            var db = Db;
            var sb = new StringBuilder();
            sb.Append(Ui.Rich($"{set.name} ({p.Equipped}/{p.Total})", SetGold));
            for (int i = 0; i < set.items.Length; i++)
            {
                var def = db != null ? db.Item(set.items[i]) : null;
                sb.Append("\n  ").Append(Ui.Rich(def != null ? def.name : set.items[i], p.PieceEquipped[i] ? SetLit : SetGrey));
            }
            if (set.bonuses.Count > 0) sb.Append('\n');
            for (int i = 0; i < set.bonuses.Count; i++)
                sb.Append('\n').Append(Ui.Rich($"({set.bonuses[i].pieces}) Set: " + SetBonusText(set.bonuses[i]), p.BonusActive[i] ? StatGreen : SetGrey));
            return sb.ToString();
        }

        /// <summary>What a set bonus does: its description when set, else its stats and effects ("+8 Intellect, …").</summary>
        public static string SetBonusText(SetBonusDef b)
        {
            if (b == null) return "";
            if (!string.IsNullOrEmpty(b.description)) return b.description;
            var parts = new List<string>();
            foreach (var m in b.stats)
                if (m != null) parts.Add($"{(m.value >= 0 ? "+" : "")}{m.value:0.#}{(m.pct ? "%" : "")} {StatName(m.stat, m.school)}");
            foreach (var p in b.equipEffects)
            {
                if (p == null) continue;
                var line = EffectLine(p);
                parts.Add(line.StartsWith("Equip: ") ? line.Substring(7) : line);
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "a special effect.";
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

        /// <summary>
        /// The weapon type shown next to the slot, WoW style: the slot already says the hands ("One-Hand   Sword",
        /// "Off Hand   Dagger", "Two-Hand   Mace"), so one/two-handed types drop that prefix; others are spaced
        /// ("Fist Weapon", "Held In Offhand").
        /// </summary>
        public static string WeaponTypeName(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.OneHandSword: case WeaponType.TwoHandSword: return "Sword";
                case WeaponType.OneHandAxe: case WeaponType.TwoHandAxe: return "Axe";
                case WeaponType.OneHandMace: case WeaponType.TwoHandMace: return "Mace";
                default: return Spaced(w.ToString());
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
