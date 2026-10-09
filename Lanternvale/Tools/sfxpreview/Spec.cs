// What the game expects from the synthesizer: the id groups of Docs/Expansion.md §4 with their minimum variant
// counts, the 24 original ids, and the original clips that must stay byte-identical.
using System.Collections.Generic;

namespace Lanternvale.SfxPreview
{
    static class Spec
    {
        public sealed class Group
        {
            public string Name;
            public int MinVariants;
            public string[] Ids;
            /// <summary>Spectral guard applies (≤ 50 % energy below 200 Hz, ≥ 25 % in 1–5 kHz).</summary>
            public bool Guard;
            /// <summary>Members must be pairwise distinct in the spectral/temporal feature space.</summary>
            public bool Distinct;
            /// <summary>Default sheet window in seconds.</summary>
            public float SheetSeconds = 0.6f;
            /// <summary>
            /// Variants must be different sounds, not one waveform with noise on top (max normalised cross-correlation
            /// below <see cref="MaxVariantXcorr"/>). Off for the jingles, whose variants repeat a motif by design.
            /// </summary>
            public bool Varied = true;
        }

        public static readonly Group[] Groups =
        {
            new Group { Name = "weapons", MinVariants = 4, Guard = true, Distinct = true, SheetSeconds = 0.5f, Ids = new[] {
                "hit_blade", "hit_axe", "hit_blunt", "hit_dagger", "hit_fist", "hit_bite", "hit_claw", "hit_slam", "hit_arrow",
                "hit_bolt", "hit_bullet" } },
            new Group { Name = "materials", MinVariants = 4, Guard = true, Distinct = true, SheetSeconds = 0.6f, Ids = new[] {
                "mat_plate", "mat_mail", "mat_leather", "mat_cloth", "mat_flesh", "mat_fur", "mat_chitin", "mat_bone", "mat_wood",
                "mat_stone", "mat_ether", "mat_ice", "mat_scale", "mat_wet" } },
            new Group { Name = "generic", MinVariants = 4, Guard = true, SheetSeconds = 0.6f, Ids = new[] { "hit_physical", "hit_crit" } },
            new Group { Name = "defence", MinVariants = 2, Distinct = true, SheetSeconds = 1f, Ids = new[] {
                "parry", "block_wood", "block_metal", "dodge", "miss", "resist", "immune", "absorb" } },
            new Group { Name = "swings", MinVariants = 4, Distinct = true, SheetSeconds = 0.4f, Ids = new[] { "swing_light", "swing", "swing_heavy" } },
            new Group { Name = "ranged", MinVariants = 2, Distinct = true, SheetSeconds = 0.8f, Ids = new[] {
                "bow", "xbow_release", "gun_fire", "throw_release", "arrow_flight", "wand_zap" } },
            new Group { Name = "spells", MinVariants = 2, Distinct = true, SheetSeconds = 1f, Ids = new[] {
                "cast_fire", "cast_frost", "cast_arcane", "cast_shadow", "cast_holy", "cast_nature", "cast_lightning",
                "impact_lightning", "shout_horn", "stomp" } },
            new Group { Name = "impacts", MinVariants = 2, Distinct = true, SheetSeconds = 1f, Ids = new[] {
                "impact_fire", "impact_frost", "impact_arcane", "impact_shadow", "impact_holy", "impact_nature", "impact_lightning" } },
            new Group { Name = "deaths", MinVariants = 2, Distinct = true, SheetSeconds = 1.4f, Ids = new[] {
                "body_fall_light", "body_fall_heavy", "armor_clatter", "vo_beast_yelp", "vo_humanoid_grunt", "vo_spirit_fade",
                "vo_wood_creak", "vo_stone_crumble", "vo_dragon_roar", "vo_frog_croak", "vo_gnoll_yip" } },
            new Group { Name = "footsteps", MinVariants = 4, Distinct = true, SheetSeconds = 0.25f, Ids = new[] {
                "footstep_grass", "footstep_dirt", "footstep_leaves", "footstep_stone", "footstep_snow", "footstep_mud", "armor_jingle" } },
            new Group { Name = "hooks", MinVariants = 2, Distinct = true, Varied = false, SheetSeconds = 2.4f, Ids = new[] {
                "loot_rare", "loot_epic", "loot_legendary", "set_complete", "secret_found", "door_stone", "boss_pull",
                "raid_warning", "quest_accept", "quest_turnin", "portal_whoosh" } },
        };

        /// <summary>The 24 ids that existed before the expansion (all must keep working).</summary>
        public static readonly string[] Original =
        {
            "ui_click", "ui_open", "ui_close", "hit_physical", "hit_crit", "swing", "bow", "cast_start",
            "impact_fire", "impact_frost", "impact_arcane", "impact_shadow", "impact_holy", "impact_nature",
            "heal", "buff", "debuff", "death", "level_up", "quest", "coin", "footstep_grass", "chest_open", "door",
        };

        /// <summary>Aliases Sfx.Play resolves (Sfx.Alias) — valid names at call sites although not synthesized.</summary>
        public static readonly string[] Aliases =
            { "click", "open", "close", "hit", "impact_physical", "crit", "footstep", "gold", "loot", "levelup", "chest", "cast" };

        /// <summary>
        /// FNV-1a 64 over the float bits of the original clips that stay byte-identical (computed from the pre-expansion
        /// SfxSynth.cs on .NET 8 / x64). The upgraded combat ids are not listed.
        /// </summary>
        public static readonly Dictionary<string, ulong> LegacyHashes = new Dictionary<string, ulong>
        {
            { "buff", 0x8661778425C1B500UL },
            { "cast_start", 0x57CDFED63D21521AUL },
            { "chest_open", 0xE332768212BD43B0UL },
            { "coin", 0xB6658043475A65F1UL },
            { "death", 0xD5D85DE80A6CC29DUL },
            { "debuff", 0x950355162D1A3AF1UL },
            { "door", 0x7457F02CCFF5A8B8UL },
            { "heal", 0x212F6FA94DE955A0UL },
            { "level_up", 0x70F87C049366E9BBUL },
            { "quest", 0x12D0DF1958D7AA4BUL },
            { "ui_click", 0x140FEED9CB4DD2F6UL },
            { "ui_close", 0x19B684FDB6DDEDACUL },
            { "ui_open", 0x3D117DDEF441E17AUL },
        };

        public static Group Find(string name)
        {
            foreach (var g in Groups) if (g.Name == name) return g;
            return null;
        }

        /// <summary>Minimum variant count for an id (the strictest group it belongs to), 1 if it is in none.</summary>
        public static int MinVariants(string id)
        {
            int k = 1;
            foreach (var g in Groups) foreach (var x in g.Ids) if (x == id && g.MinVariants > k) k = g.MinVariants;
            return k;
        }

        /// <summary>Round-robin variants that sound alike defeat the round robin (impact_arcane once correlated at 1.000).</summary>
        public const float MaxVariantXcorr = 0.95f;

        /// <summary>True when the id's variants must be audibly different sounds (a Varied group; not a byte-pinned original).</summary>
        public static bool MustVary(string id)
        {
            if (LegacyHashes.ContainsKey(id)) return false;
            foreach (var g in Groups) if (g.Varied) foreach (var x in g.Ids) if (x == id) return true;
            return false;
        }

        public static bool Guarded(string id)
        {
            foreach (var g in Groups) if (g.Guard) foreach (var x in g.Ids) if (x == id) return true;
            return false;
        }

        /// <summary>Every id the expansion requires (all groups plus the originals), in a stable order.</summary>
        public static List<string> AllIds()
        {
            var seen = new HashSet<string>();
            var l = new List<string>();
            foreach (var id in Original) if (seen.Add(id)) l.Add(id);
            foreach (var g in Groups) foreach (var id in g.Ids) if (seen.Add(id)) l.Add(id);
            return l;
        }
    }
}
