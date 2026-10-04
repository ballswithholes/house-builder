// Story/world flags: string -> int. Pure C#.
using System;
using System.Collections.Generic;

namespace Lanternvale.World
{
    /// <summary>Serializable snapshot of a <see cref="FlagStore"/>.</summary>
    [Serializable]
    public sealed class FlagStoreState
    {
        public Dictionary<string, int> flags = new Dictionary<string, int>();
    }

    /// <summary>
    /// Named integer flags used by dialogue conditions/outcomes, quests and map state. A flag is "set" when its
    /// value is non-zero; setting a flag to 0 removes it. Counters are supported via <see cref="Add"/>.
    /// </summary>
    public sealed class FlagStore
    {
        readonly Dictionary<string, int> values = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Raised after a flag value changes: (key, oldValue, newValue).</summary>
        public event Action<string, int, int> Changed;

        public int Count => values.Count;

        /// <summary>All non-zero flags (live view; do not modify the store while iterating).</summary>
        public IEnumerable<KeyValuePair<string, int>> All => values;

        public int Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0;
            return values.TryGetValue(key, out var v) ? v : 0;
        }

        public bool IsSet(string key) => Get(key) != 0;

        /// <summary>Sets a flag (default 1). A value of 0 clears it.</summary>
        public void Set(string key, int value = 1)
        {
            if (string.IsNullOrEmpty(key)) return;
            int old = Get(key);
            if (old == value) return;
            if (value == 0) values.Remove(key);
            else values[key] = value;
            Changed?.Invoke(key, old, value);
        }

        /// <summary>Adds delta to a counter flag.</summary>
        public void Add(string key, int delta)
        {
            if (delta == 0) return;
            Set(key, Get(key) + delta);
        }

        public void Clear(string key) => Set(key, 0);

        /// <summary>
        /// Evaluates a flag expression as used by requireFlag/hideFlag fields:
        /// "" or null → true, "flag" → IsSet(flag), "!flag" → !IsSet(flag).
        /// Several terms may be combined with '&amp;' (all must hold), e.g. "met_elder&amp;!elder_angry".
        /// </summary>
        public bool Test(string expr)
        {
            if (string.IsNullOrEmpty(expr)) return true;
            if (expr.IndexOf('&') >= 0)
            {
                foreach (var part in expr.Split('&'))
                    if (!TestTerm(part.Trim())) return false;
                return true;
            }
            return TestTerm(expr.Trim());
        }

        bool TestTerm(string term)
        {
            if (term.Length == 0) return true;
            if (term[0] == '!') return !IsSet(term.Substring(1).Trim());
            return IsSet(term);
        }

        /// <summary>Removes every flag (raises Changed for each).</summary>
        public void ClearAll()
        {
            if (values.Count == 0) return;
            var keys = new List<string>(values.Keys);
            foreach (var k in keys) Clear(k);
        }

        public FlagStoreState Save()
        {
            var s = new FlagStoreState();
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) s.flags[k] = values[k];
            return s;
        }

        /// <summary>Replaces the contents with the snapshot. Does not raise Changed.</summary>
        public void Load(FlagStoreState state)
        {
            values.Clear();
            if (state?.flags == null) return;
            foreach (var kv in state.flags)
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value != 0) values[kv.Key] = kv.Value;
        }
    }
}
