// Human-readable combat log lines for CombatEvents.
using System;
using System.Globalization;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    public static class CombatLog
    {
        static string Who(Unit u) => u == null ? "" : u.Name;
        static string N(float v) => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);

        static string Possessive(Unit u, string thing)
        {
            if (string.IsNullOrEmpty(thing)) return Who(u);
            if (u == null) return thing;
            return u.Name.EndsWith("s") ? $"{u.Name}' {thing}" : $"{u.Name}'s {thing}";
        }

        public static string Format(CombatEvent e)
        {
            var s = e.Source; var t = e.Target;
            string what = string.IsNullOrEmpty(e.Name) ? (e.AutoAttack ? "attack" : "") : e.Name;
            switch (e.Type)
            {
                case CombatEventType.BattleStart: return "Combat begins!";
                case CombatEventType.BattleEnd: return e.Reason == "Victory" ? "Victory!" : e.Reason == "Defeat" ? "Defeat..." : "Combat ends.";
                case CombatEventType.RoundStart: return $"— Round {e.Round} —";
                case CombatEventType.TurnStart: return $"{Who(s)}'s turn.";
                case CombatEventType.TurnEnd: return "";
                case CombatEventType.TurnSkipped: return $"{Who(s)} loses the turn ({e.Reason}).";
                case CombatEventType.Initiative: return $"{Who(s)} rolls initiative {e.Count} ({e.Amount:0.#}).";
                case CombatEventType.Damage:
                {
                    if (e.Reason == "self") return $"{Who(t)} pays {N(e.Amount)} health for {what}.";
                    string extra = "";
                    if (e.Absorbed > 0) extra += $" ({N(e.Absorbed)} absorbed)";
                    if (e.Resisted > 0) extra += $" ({N(e.Resisted)} resisted)";
                    if (e.Blocked > 0) extra += $" ({N(e.Blocked)} blocked)";
                    string school = e.School == School.Physical ? "" : " " + e.School;
                    if (e.Periodic) return $"{Who(t)} suffers {N(e.Amount)}{school} damage from {Possessive(s, what)}.{extra}";
                    if (e.AutoAttack) return $"{Who(s)} {(e.Crit ? "crits" : "hits")} {Who(t)} for {N(e.Amount)}{school}{(e.OffHand ? " (off hand)" : "")}.{extra}";
                    return $"{Possessive(s, what)} {(e.Crit ? "crits" : "hits")} {Who(t)} for {N(e.Amount)}{school}.{extra}";
                }
                case CombatEventType.Heal:
                    if (e.Reason == "regen") return "";
                    if (e.Periodic) return $"{Who(t)} gains {N(e.Amount)} health from {Possessive(s, what)}.";
                    return $"{Possessive(s, what)} {(e.Crit ? "critically heals" : "heals")} {Who(t)} for {N(e.Amount)}{(e.Overheal > 0 ? $" ({N(e.Overheal)} overheal)" : "")}.";
                case CombatEventType.Miss: return $"{Possessive(s, what)} misses {Who(t)}.";
                case CombatEventType.Dodge: return $"{Who(t)} dodges {Possessive(s, what)}.";
                case CombatEventType.Parry: return $"{Who(t)} parries {Possessive(s, what)}.";
                case CombatEventType.Block: return $"{Who(t)} blocks {Possessive(s, what)}.";
                case CombatEventType.Resist: return $"{Who(t)} resists {Possessive(s, what)}.";
                case CombatEventType.Absorb: return $"{what} absorbs {N(e.Amount)} damage on {Who(t)}.";
                case CombatEventType.Immune: return $"{Who(t)} is immune to {Possessive(s, string.IsNullOrEmpty(what) ? "attack" : what)}.";
                case CombatEventType.Evade: return $"{Who(t)} evades.";
                case CombatEventType.AuraApplied:
                    return s != null && s != t ? $"{Who(t)} is afflicted by {what}." : $"{Who(t)} gains {what}.";
                case CombatEventType.AuraRefreshed: return $"{what} on {Who(t)} is refreshed.";
                case CombatEventType.AuraStack: return e.Reason == "charges" ? $"{what} on {Who(t)}: {e.Count} charges left." : $"{what} on {Who(t)} ({e.Count}).";
                case CombatEventType.AuraRemoved:
                    if (e.Reason == "OutOfRange" || e.Reason == "SourceGone" || e.Reason == "Death") return "";
                    return $"{what} fades from {Who(t)}.";
                case CombatEventType.AuraBroken: return $"{what} on {Who(t)} is broken.";
                case CombatEventType.Dispel: return $"{Who(s)} removes {what} from {Who(t)}.";
                case CombatEventType.AbilityUsed: return $"{Who(s)} uses {what}.";
                case CombatEventType.CastStart:
                    if (e.Reason == "continuing") return $"{Who(s)} continues casting {what} ({e.Seconds:0.#} s left).";
                    return e.Reason == "channel" ? $"{Who(s)} channels {what}." : $"{Who(s)} begins casting {what}.";
                case CombatEventType.CastComplete: return "";
                case CombatEventType.CastInterrupted: return s != null && s != t ? $"{Who(s)} interrupts {Possessive(t, what)}{(e.Seconds > 0 ? $" ({e.School} locked {e.Seconds:0.#} s)" : "")}." : $"{Possessive(t, what)} is interrupted ({e.Reason}).";
                case CombatEventType.CastFailed: return $"{Possessive(s, what)} fails: {e.Reason}";
                case CombatEventType.ChannelTick: return "";
                case CombatEventType.SwingQueued: return $"{Who(s)} readies {what}.";
                case CombatEventType.AutoAttackToggled: return e.Amount > 0 ? $"{Who(s)} starts attacking {Who(t)}." : $"{Who(s)} stops attacking.";
                case CombatEventType.ResourceChange:
                    if (e.Periodic || e.Resource == ResourceType.None) return "";
                    return e.Amount >= 0 ? $"{Who(t)} gains {N(e.Amount)} {e.Resource}." : $"{Who(t)} loses {N(-e.Amount)} {e.Resource}.";
                case CombatEventType.ComboPoints: return e.Count < 0 ? "" : $"{Who(s)} has {N(e.Amount)} combo point{(Math.Abs(e.Amount - 1) < 0.01f ? "" : "s")} on {Who(t)}.";
                case CombatEventType.Move: return "";
                case CombatEventType.Teleport: return $"{Who(s)} {(string.IsNullOrEmpty(what) ? "teleports" : "uses " + what)}.";
                case CombatEventType.Charge: return $"{Who(s)} charges {Who(t)}.";
                case CombatEventType.Knockback: return $"{Who(t)} is knocked back.";
                case CombatEventType.Summon: return $"{Who(s)} summons {Who(t)}.";
                case CombatEventType.Despawn: return e.Reason == "expired" || e.Reason == "replaced" || e.Reason == "dismissed" ? $"{Who(s)} {(e.Reason == "dismissed" ? "is dismissed" : "fades")}." : "";
                case CombatEventType.Death: return $"{Who(t)} dies.";
                case CombatEventType.Downed: return $"{Who(t)} is downed!";
                case CombatEventType.Revive: return $"{Who(t)} is back on their feet.";
                case CombatEventType.Threat: return "";
                case CombatEventType.Taunt: return $"{Who(s)} taunts {Who(t)}.";
                case CombatEventType.TargetChanged: return $"{Who(s)} turns on {Who(t)}.";
                case CombatEventType.ItemCreated: return $"{Who(s)} creates {what}{(e.Count > 1 ? " x" + e.Count : "")}.";
                case CombatEventType.ItemConsumed: return "";
                case CombatEventType.CooldownReset: return $"{Possessive(s, what)} resets {e.Count} cooldown{(e.Count == 1 ? "" : "s")}.";
                case CombatEventType.Log: return e.Text ?? "";
                default: return "";
            }
        }
    }
}
