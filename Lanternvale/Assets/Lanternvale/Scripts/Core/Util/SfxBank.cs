// Pure bookkeeping behind the Game layer's sound player (Game/Audio/Sfx.cs), kept here without Unity types so the
// Core tests can pin it down (Tools/harness/CoreTests/TestsSfxPlayback.cs):
//   * variant banks: the synthesizer publishes "<id>#<n>" (n = 1…K); everything is grouped and keyed by the base id;
//   * round-robin without immediate repeats (a shuffle bag per base id: every variant once per cycle, the first of a
//     new cycle never equals the last of the previous one);
//   * per-play jitter (±3 % pitch, ±1.5 dB) for everything but the stable UI/stinger ids;
//   * the voice rules: a 30 ms rate limit and a 4-voice cap keyed on the base id, a per-frame budget of new voices,
//     and the slot choice in the pooled sources;
//   * a delayed-play queue driven by the caller's clock (the player feeds it scaled time, so it follows
//     Time.timeScale and freezes while paused);
//   * the merge of recorded overrides (Resources/Audio/Sfx/<id>/*.wav) over the synthesized variants.
using System;
using System.Collections.Generic;

namespace Lanternvale.Util
{
    public static class SfxIds
    {
        /// <summary>Separator between a base id and its variant number ("hit_blade#3").</summary>
        public const char VariantSeparator = '#';

        /// <summary>"hit_blade#3" → "hit_blade"; ids without a variant suffix are returned unchanged (null → "").</summary>
        public static string BaseOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int i = id.IndexOf(VariantSeparator);
            return i < 0 ? id : id.Substring(0, i);
        }

        /// <summary>The variant number of "id#n" (0 for a plain id or a suffix that is not a positive number).</summary>
        public static int VariantOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            int i = id.IndexOf(VariantSeparator);
            if (i < 0 || i == id.Length - 1) return 0;
            int n = 0;
            for (int k = i + 1; k < id.Length; k++)
            {
                char c = id[k];
                if (c < '0' || c > '9') return 0;
                if (n > 100000) return 0;
                n = n * 10 + (c - '0');
            }
            return n;
        }

        /// <summary>
        /// Groups published keys by base id. Within a group the plain id comes first, then the variants by number
        /// ("x#2" before "x#10"), then anything else ordinally, so the order is stable whatever the input order.
        /// Empty keys and keys with an empty base ("#2") are skipped.
        /// </summary>
        public static Dictionary<string, List<string>> Group(IEnumerable<string> keys)
        {
            var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (keys == null) return d;
            foreach (var k in keys)
            {
                var b = BaseOf(k);
                if (b.Length == 0) continue;
                if (!d.TryGetValue(b, out var list)) d[b] = list = new List<string>();
                if (!list.Contains(k)) list.Add(k);
            }
            foreach (var list in d.Values) list.Sort(CompareVariants);
            return d;
        }

        static int CompareVariants(string a, string b)
        {
            int va = a.IndexOf(VariantSeparator) < 0 ? -1 : VariantOf(a);
            int vb = b.IndexOf(VariantSeparator) < 0 ? -1 : VariantOf(b);
            // a malformed suffix ("x#a") sorts after the numbered variants
            if (va == 0 && a.IndexOf(VariantSeparator) >= 0) va = int.MaxValue;
            if (vb == 0 && b.IndexOf(VariantSeparator) >= 0) vb = int.MaxValue;
            int c = va.CompareTo(vb);
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }
    }

    /// <summary>
    /// Round-robin over K variants in a fresh random order every cycle (a shuffle bag). Every variant plays once per
    /// cycle and the same variant never plays twice in a row (K ≥ 2).
    /// </summary>
    public sealed class SfxVariantPicker
    {
        readonly int[] order;
        int pos;
        int last = -1;

        public SfxVariantPicker(int count)
        {
            order = new int[Math.Max(1, count)];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            pos = order.Length;   // the first Next shuffles
        }

        public int Count => order.Length;

        /// <summary>The index of the variant to play next (0…Count-1).</summary>
        public int Next(Random rng)
        {
            if (order.Length == 1) return last = 0;
            if (pos >= order.Length)
            {
                // Fisher–Yates, then make sure the new cycle does not start with the variant that just played
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int j = rng != null ? rng.Next(i + 1) : 0;
                    int t = order[i]; order[i] = order[j]; order[j] = t;
                }
                if (order[0] == last)
                {
                    int j = 1 + (rng != null ? rng.Next(order.Length - 1) : 0);
                    int t = order[0]; order[0] = order[j]; order[j] = t;
                }
                pos = 0;
            }
            return last = order[pos++];
        }
    }

    /// <summary>Per-play random variation of pitch and loudness.</summary>
    public static class SfxJitter
    {
        public const float PitchRange = 0.03f;     // ±3 %
        public const float VolumeRangeDb = 1.5f;   // ±1.5 dB

        /// <summary>Ids that always play exactly as authored: UI chimes, music-like stingers and announcements.</summary>
        public static readonly HashSet<string> Stable = new HashSet<string>(StringComparer.Ordinal)
        {
            "ui_click", "ui_open", "ui_close", "level_up", "quest", "quest_accept", "quest_turnin",
            "loot_rare", "loot_epic", "loot_legendary", "set_complete", "secret_found", "raid_warning", "boss_pull",
        };

        public static bool IsJittered(string baseId) => !string.IsNullOrEmpty(baseId) && !Stable.Contains(baseId);

        /// <summary>A pitch multiplier in [1 − 3 %, 1 + 3 %].</summary>
        public static float Pitch(Random rng) => 1f + PitchRange * Uniform(rng);

        /// <summary>A linear gain for a uniform ±1.5 dB offset.</summary>
        public static float Gain(Random rng) => DbToGain(VolumeRangeDb * Uniform(rng));

        public static float DbToGain(float db) => (float)Math.Pow(10.0, db / 20.0);

        static float Uniform(Random rng) => rng == null ? 0f : (float)(rng.NextDouble() * 2.0 - 1.0);
    }

    /// <summary>
    /// Voice rules of the pooled player, keyed on the base id: a minimum gap between two starts of the same base id
    /// (rate limit, unscaled seconds), at most <see cref="MaxPerId"/> overlapping voices of one base id, and at most
    /// <see cref="FrameBudget"/> new voices per rendered frame (UI ids are exempt from the budget so menus never go
    /// quiet during a raid fight).
    /// </summary>
    public sealed class SfxVoiceRules
    {
        public const float MinGap = 0.03f;
        public const int MaxPerId = 4;
        public const int FrameBudget = 6;
        public const int PoolSize = 24;

        readonly Dictionary<string, float> lastStart = new Dictionary<string, float>(StringComparer.Ordinal);
        int frame = int.MinValue;
        int usedThisFrame;

        public int UsedThisFrame(int currentFrame) => currentFrame == frame ? usedThisFrame : 0;

        public static bool IsBudgetExempt(string baseId) => baseId != null && baseId.StartsWith("ui_", StringComparison.Ordinal);

        /// <summary>
        /// Checks the rate limit and the frame budget and, when the voice may start, records it. Returns false when the
        /// sound must be dropped. The rate limit is checked first, so a rate-limited sound costs no budget.
        /// </summary>
        public bool TryStart(string baseId, float now, int currentFrame)
        {
            if (string.IsNullOrEmpty(baseId)) return false;
            if (lastStart.TryGetValue(baseId, out var last) && now - last < MinGap && now >= last) return false;
            if (currentFrame != frame) { frame = currentFrame; usedThisFrame = 0; }
            bool exempt = IsBudgetExempt(baseId);
            if (!exempt && usedThisFrame >= FrameBudget) return false;
            if (!exempt) usedThisFrame++;
            lastStart[baseId] = now;
            return true;
        }

        public void Clear()
        {
            lastStart.Clear();
            frame = int.MinValue;
            usedThisFrame = 0;
        }

        /// <summary>
        /// Picks the pool slot for a new voice of baseId: the oldest voice of the same base id once
        /// <see cref="MaxPerId"/> of them play, else the next free slot after <paramref name="next"/> (round robin),
        /// else the oldest voice overall. Advances <paramref name="next"/> past the chosen slot.
        /// </summary>
        public static int ChooseSlot(string baseId, IReadOnlyList<bool> playing, IReadOnlyList<string> playingBase,
                                     IReadOnlyList<float> startTime, ref int next, int maxPerId = MaxPerId)
        {
            int n = playing.Count;
            if (n == 0) return -1;
            int same = 0, sameOldest = -1;
            for (int i = 0; i < n; i++)
                if (playing[i] && playingBase[i] == baseId)
                {
                    same++;
                    if (sameOldest < 0 || startTime[i] < startTime[sameOldest]) sameOldest = i;
                }
            int slot = -1;
            if (same >= maxPerId) slot = sameOldest;
            else
            {
                int start = ((next % n) + n) % n;
                for (int k = 0; k < n; k++)
                {
                    int i = (start + k) % n;
                    if (!playing[i]) { slot = i; break; }
                }
                if (slot < 0)
                {
                    slot = 0;
                    for (int i = 1; i < n; i++) if (startTime[i] < startTime[slot]) slot = i;
                }
            }
            next = (slot + 1) % n;
            return slot;
        }
    }

    /// <summary>
    /// Sounds waiting for a moment on the caller's clock (body falls, arrow thunks). Feed it scaled time and it follows
    /// the presentation speed and stands still while the game is paused. Items due at the same time keep their order.
    /// </summary>
    public sealed class SfxDelayQueue<T>
    {
        struct Entry { public float Due; public long Seq; public T Item; }

        readonly List<Entry> items = new List<Entry>();
        long seq;

        public int Count => items.Count;

        /// <summary>Queues item for now + delay (a delay ≤ 0 is due immediately at the next pop).</summary>
        public void Add(float now, float delay, T item)
        {
            if (float.IsNaN(delay) || float.IsInfinity(delay)) delay = 0f;
            items.Add(new Entry { Due = now + Math.Max(0f, delay), Seq = seq++, Item = item });
        }

        /// <summary>Moves every item due at or before now into <paramref name="into"/> (oldest due first). Returns the count.</summary>
        public int PopDue(float now, List<T> into)
        {
            if (items.Count == 0) return 0;
            items.Sort((a, b) => a.Due != b.Due ? a.Due.CompareTo(b.Due) : a.Seq.CompareTo(b.Seq));
            int n = 0;
            while (n < items.Count && items[n].Due <= now) n++;
            for (int i = 0; i < n; i++) into?.Add(items[i].Item);
            if (n > 0) items.RemoveRange(0, n);
            return n;
        }

        /// <summary>
        /// The clock went backwards (a new game, a reloaded scene): queued items keep their remaining delay from now.
        /// </summary>
        public void Rebase(float oldNow, float newNow)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];
                e.Due = newNow + Math.Max(0f, e.Due - oldNow);
                items[i] = e;
            }
        }

        public void Clear() => items.Clear();
    }

    public static class SfxOverrides
    {
        /// <summary>
        /// Replaces the variants of every base id for which <paramref name="load"/> returns at least one clip (the
        /// recorded files win over the synthesized variants; a base id that was not synthesized is added). An absent
        /// folder (null or empty result) leaves the bank unchanged. Returns the base ids that were overridden.
        /// </summary>
        public static List<string> Apply<T>(Dictionary<string, T[]> bank, IEnumerable<string> ids, Func<string, T[]> load) where T : class
        {
            var done = new List<string>();
            if (bank == null || ids == null || load == null) return done;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in ids)
            {
                var id = SfxIds.BaseOf(raw);
                if (id.Length == 0 || !seen.Add(id)) continue;
                T[] found;
                try { found = load(id); }
                catch (Exception) { found = null; }
                if (found == null || found.Length == 0) continue;
                var clean = new List<T>(found.Length);
                foreach (var c in found) if (c != null) clean.Add(c);
                if (clean.Count == 0) continue;
                bank[id] = clean.ToArray();
                done.Add(id);
            }
            return done;
        }
    }
}
