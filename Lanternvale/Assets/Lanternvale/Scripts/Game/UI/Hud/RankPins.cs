// Rank pins (WoW Classic downranking on the action bar): a character may pin a lower rank of an ability (Spellbook ▸
// rank list) so the action bar, its hotkey and the spellbook click cast that rank (cheaper, weaker — the healer's mana
// saver). Unpinned abilities follow the highest known rank. The session save has no slot for UI data, so pins live in
// PlayerPrefs keyed by the character's name and class ("lv.bar.ranks.<Name>.<Class>", pets
// "<Owner>.pet.<creature>"): "abilityId:rank,abilityId:rank".
using System;
using System.Collections.Generic;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class RankPins
    {
        const string Prefix = "lv.bar.ranks.";
        static readonly Dictionary<string, Dictionary<string, int>> cache = new Dictionary<string, Dictionary<string, int>>();
        static readonly Dictionary<Unit, string> keys = new Dictionary<Unit, string>();
        static readonly StringBuilder sb = new StringBuilder(128);

        /// <summary>Bumped whenever a pin changes (HUD dirty checks).</summary>
        public static int Version { get; private set; }

        /// <summary>The rank pinned for the ability (0 = none: the highest known rank is used).</summary>
        public static int Pinned(Unit u, string abilityId)
        {
            if (u == null || string.IsNullOrEmpty(abilityId)) return 0;
            var d = Load(KeyOf(u));
            return d != null && d.TryGetValue(abilityId, out var r) ? r : 0;
        }

        /// <summary>
        /// The rank to cast: the pinned rank while it is a known rank below the highest known one, else 0 (= the highest
        /// known rank, as the engine's rank parameter expects).
        /// </summary>
        public static int RankFor(Unit u, AbilityDef a)
        {
            if (u == null || a == null) return 0;
            int pin = Pinned(u, a.id);
            if (pin <= 0) return 0;
            int known;
            try { known = AbilityRules.KnownRanks(u, a); }
            catch (Exception) { return 0; }
            return pin < known ? pin : 0;
        }

        /// <summary>Pins a rank (0, or the highest known rank, clears the pin: the bar then follows new ranks).</summary>
        public static void Pin(Unit u, AbilityDef a, int rank)
        {
            if (u == null || a == null) return;
            int known;
            try { known = AbilityRules.KnownRanks(u, a); }
            catch (Exception) { known = 0; }
            if (rank >= known) rank = 0;
            string key = KeyOf(u);
            var d = Load(key);
            if (d == null) return;
            if (rank <= 0) { if (!d.Remove(a.id)) return; }
            else
            {
                if (d.TryGetValue(a.id, out var old) && old == rank) return;
                d[a.id] = rank;
            }
            Version++;
            Save(key, d);
        }

        /// <summary>Forgets cached unit keys (new game / load: units are new objects, names may repeat).</summary>
        public static void ClearUnitKeys() => keys.Clear();

        static string KeyOf(Unit u)
        {
            if (keys.TryGetValue(u, out var k)) return k;
            if (keys.Count > 64) keys.Clear();
            if (u.Class != null) k = Clean(NameOf(u)) + "." + u.ClassId;
            else
            {
                string owner = u.Owner != null ? Clean(NameOf(u.Owner)) + "." + u.Owner.ClassId : "unit";
                k = owner + ".pet." + (u.Creature != null ? u.Creature.id : Clean(u.Name ?? ""));
            }
            keys[u] = k;
            return k;
        }

        static string NameOf(Unit u)
        {
            var f = GameFlow.Instance;
            try { return f != null ? f.NameOf(u) : u.Name ?? ""; }
            catch (Exception) { return u.Name ?? ""; }
        }

        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            sb.Length = 0;
            foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.ToString();
        }

        static Dictionary<string, int> Load(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (cache.TryGetValue(key, out var d)) return d;
            d = new Dictionary<string, int>();
            try
            {
                var raw = PlayerPrefs.GetString(Prefix + key, "");
                if (!string.IsNullOrEmpty(raw))
                    foreach (var part in raw.Split(','))
                    {
                        int c = part.LastIndexOf(':');
                        if (c <= 0) continue;
                        if (int.TryParse(part.Substring(c + 1), out int r) && r > 0) d[part.Substring(0, c)] = r;
                    }
            }
            catch (Exception) { }
            cache[key] = d;
            return d;
        }

        static void Save(string key, Dictionary<string, int> d)
        {
            try
            {
                if (d.Count == 0) PlayerPrefs.DeleteKey(Prefix + key);
                else
                {
                    sb.Length = 0;
                    foreach (var kv in d)
                    {
                        if (sb.Length > 0) sb.Append(',');
                        sb.Append(kv.Key).Append(':').Append(kv.Value);
                    }
                    PlayerPrefs.SetString(Prefix + key, sb.ToString());
                }
                PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogWarning("[Lanternvale] Saving the pinned ranks failed: " + e.Message); }
        }
    }
}
