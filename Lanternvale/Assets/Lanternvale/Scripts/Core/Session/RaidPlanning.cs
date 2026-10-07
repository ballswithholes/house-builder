// Raid planning helpers for the Game layer's raid UI (Docs/Expansion.md §6 raid-ui; Docs/UI_Panels.md "Raid party";
// Docs/UI_HUD.md "Raid frames"): the raid picker's suggested party and role summary, the level warning, the "big battle"
// threshold the turn strip and the AI pacing use, the raid-wipe notice the toasts merge, and the auto-play snapshot the
// combat HUD's "Auto: all companions" / "Auto-battle" toggles restore. Pure C#: no session state is changed here.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;

namespace Lanternvale.Session
{
    public static class RaidPlanning
    {
        // ================================================================= battle size

        /// <summary>Battles with more units than this are "big": a compact turn strip and faster AI pacing.</summary>
        public const int BigBattleUnits = 14;

        public static bool IsBigBattle(int units) => units > BigBattleUnits;

        /// <summary>True when the battle holds more than <see cref="BigBattleUnits"/> units (every side, pets and totems).</summary>
        public static bool IsBigBattle(Battle b) => b != null && IsBigBattle(b.Units.Count);

        // ================================================================= roles

        /// <summary>How many characters of each role a selection holds.</summary>
        public struct RoleCounts
        {
            public int Tanks, Healers, Melee, Ranged;
            public int Dps => Melee + Ranged;
            public int Total => Tanks + Healers + Melee + Ranged;

            public void Add(UnitRole role)
            {
                switch (role)
                {
                    case UnitRole.Tank: Tanks++; break;
                    case UnitRole.Healer: Healers++; break;
                    case UnitRole.RangedDps: Ranged++; break;
                    default: Melee++; break;   // MeleeDps (and Auto, which RoleInference never returns)
                }
            }
        }

        public static RoleCounts CountRoles(IEnumerable<UnitRole> roles)
        {
            var c = new RoleCounts();
            if (roles != null) foreach (var r in roles) c.Add(r);
            return c;
        }

        /// <summary>Roles of the characters among <paramref name="units"/> (pets, totems and summons are not counted).</summary>
        public static RoleCounts CountRoles(IEnumerable<Unit> units)
        {
            var c = new RoleCounts();
            if (units != null) foreach (var u in units) if (u != null && u.IsCharacter) c.Add(u.Role);
            return c;
        }

        /// <summary>"2 tanks · 2 healers · 6 dps" (singular for one).</summary>
        public static string RoleSummary(RoleCounts c) =>
            $"{c.Tanks} {(c.Tanks == 1 ? "tank" : "tanks")} · {c.Healers} {(c.Healers == 1 ? "healer" : "healers")} · {c.Dps} dps";

        /// <summary>Tanks a raid of <paramref name="size"/> wants: one per five (at least one from two characters up).</summary>
        public static int WantedTanks(int size) => size <= 1 ? 0 : Math.Max(1, size / 5);

        /// <summary>Healers a raid of <paramref name="size"/> wants: one per four (at least one from two characters up).</summary>
        public static int WantedHealers(int size) => size <= 1 ? 0 : Math.Max(1, size / 4);

        /// <summary>
        /// A word of advice for the picker when the chosen party (<paramref name="c"/>, at least 3 characters) has no tank or
        /// no healer; null otherwise.
        /// </summary>
        public static string RoleAdvice(RoleCounts c)
        {
            if (c.Total < 3) return null;
            if (c.Tanks == 0 && c.Healers == 0) return "No tank and no healer: the raid will not last long.";
            if (c.Tanks == 0) return "No tank: the bosses will tear into whoever they like.";
            if (c.Healers == 0) return "No healer: nobody will mend the wounds.";
            return null;
        }

        // ================================================================= the picker's suggestion

        /// <summary>
        /// The party the raid picker suggests from <paramref name="candidates"/> (GameSession.RaidCandidates order: Main, the
        /// active party, then camp): Main first, then the party the player travels with in its order, then camp companions —
        /// first the tanks and healers the raid still lacks (<see cref="WantedTanks"/>, <see cref="WantedHealers"/>), then
        /// the rest in roster order — up to <paramref name="size"/> characters. Returns member ids (GameSession.MemberId).
        /// <paramref name="roleOf"/> defaults to Unit.Role (pass a cache: role inference is not free).
        /// </summary>
        public static List<string> DefaultSelection(GameSession s, IReadOnlyList<Unit> candidates, int size, Func<Unit, UnitRole> roleOf = null)
        {
            var ids = new List<string>();
            if (s == null || s.Main == null) return ids;
            size = Math.Max(1, size);
            roleOf ??= u => u.Role;
            var chosen = new List<Unit> { s.Main };
            var counts = new RoleCounts();
            counts.Add(roleOf(s.Main));

            void Take(Unit u)
            {
                chosen.Add(u);
                counts.Add(roleOf(u));
            }

            if (candidates != null)
            {
                foreach (var u in candidates)
                    if (chosen.Count < size && u != null && !chosen.Contains(u) && s.IsInParty(u)) Take(u);
                foreach (var u in candidates)
                    if (chosen.Count < size && u != null && !chosen.Contains(u) && counts.Tanks < WantedTanks(size) && roleOf(u) == UnitRole.Tank) Take(u);
                foreach (var u in candidates)
                    if (chosen.Count < size && u != null && !chosen.Contains(u) && counts.Healers < WantedHealers(size) && roleOf(u) == UnitRole.Healer) Take(u);
                foreach (var u in candidates)
                    if (chosen.Count < size && u != null && !chosen.Contains(u)) Take(u);
            }
            foreach (var u in chosen) ids.Add(s.MemberId(u));
            return ids;
        }

        /// <summary>"Level 18 · the raid asks for 21" when <paramref name="level"/> is below the map's levelMin, else null.</summary>
        public static string LevelWarning(int level, int levelMin) =>
            levelMin > 0 && level < levelMin ? $"Level {level} · the raid asks for {levelMin}" : null;

        /// <summary>"levels 21–23", "level 21+", or "" for a map without a band.</summary>
        public static string BandText(MapDef m)
        {
            if (m == null || m.levelMin <= 0) return "";
            if (m.levelMax > m.levelMin) return $"levels {m.levelMin}–{m.levelMax}";
            return $"level {m.levelMin}+";
        }

        // ================================================================= raid wipes

        /// <summary>The first sentence of the PartyHealed text of a raid wipe (GameSession.TryRaidWipe).</summary>
        public const string WipeNotice = "The raid has wiped.";

        /// <summary>
        /// True when a defeat on <paramref name="map"/> is a raid wipe that sends the party home (a raid map whose return map
        /// exists and is not a raid) rather than a game over — the rule GameSession's FinishBattle applies.
        /// </summary>
        public static bool WipeSendsHome(GameDatabase db, MapDef map)
        {
            if (db == null || map == null || map.raidSize <= 0 || string.IsNullOrEmpty(map.raidReturnMap)) return false;
            return db.Maps.TryGetValue(map.raidReturnMap, out var home) && home != null && home.raidSize <= 0;
        }

        /// <summary>The raid wipe's PartyHealed text without its first sentence ("You come to in Mirefen, …"); other texts
        /// are returned as they are.</summary>
        public static string WipeDetail(string partyHealedText)
        {
            if (string.IsNullOrEmpty(partyHealedText)) return "";
            if (!partyHealedText.StartsWith(WipeNotice, StringComparison.Ordinal)) return partyHealedText;
            return partyHealedText.Substring(WipeNotice.Length).Trim();
        }
    }

    /// <summary>
    /// The auto-play flags of some characters and their pets, captured before a "turn auto-play on for everyone" toggle and
    /// put back when it is turned off. Restore runs the owners first, then each pet (an owner's SetAutoPlay also sets its pet).
    /// </summary>
    public sealed class AutoPlaySnapshot
    {
        readonly List<Unit> units = new List<Unit>();
        readonly List<bool> flags = new List<bool>();

        public int Count => units.Count;

        public static AutoPlaySnapshot Capture(IEnumerable<Unit> characters)
        {
            var snap = new AutoPlaySnapshot();
            if (characters == null) return snap;
            var pets = new List<Unit>();
            foreach (var c in characters)
            {
                if (c == null || snap.units.Contains(c)) continue;
                snap.units.Add(c);
                snap.flags.Add(c.AutoPlay);
                if (c.Pet != null && !pets.Contains(c.Pet)) pets.Add(c.Pet);
            }
            foreach (var p in pets)
            {
                if (snap.units.Contains(p)) continue;
                snap.units.Add(p);
                snap.flags.Add(p.AutoPlay);
            }
            return snap;
        }

        public bool Contains(Unit u) => u != null && units.Contains(u);

        /// <summary>The captured flag of <paramref name="u"/> (false when it was not captured).</summary>
        public bool FlagOf(Unit u)
        {
            int i = u != null ? units.IndexOf(u) : -1;
            return i >= 0 && flags[i];
        }

        /// <summary>Puts every captured flag back through <paramref name="set"/> (e.g. CombatController.SetAutoPlay).</summary>
        public void Restore(Action<Unit, bool> set) => Restore(set, null, false);

        /// <summary>
        /// Puts every captured flag back through <paramref name="set"/>; then each of <paramref name="current"/> that was not
        /// captured (it joined since, or the snapshot belongs to a game that was replaced) gets <paramref name="fallback"/>.
        /// </summary>
        public void Restore(Action<Unit, bool> set, IEnumerable<Unit> current, bool fallback)
        {
            if (set == null) return;
            for (int i = 0; i < units.Count; i++) set(units[i], flags[i]);
            if (current == null) return;
            foreach (var u in current) if (u != null && !units.Contains(u)) set(u, fallback);
        }

        /// <summary>True while every captured unit still has its captured flag.</summary>
        public bool Matches()
        {
            for (int i = 0; i < units.Count; i++) if (units[i].AutoPlay != flags[i]) return false;
            return true;
        }
    }
}
