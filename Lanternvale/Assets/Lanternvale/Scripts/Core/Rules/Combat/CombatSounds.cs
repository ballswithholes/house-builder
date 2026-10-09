// Which sound a combat moment makes (Docs/Expansion.md §4, brief_audio §4.2 / §6): the weapon layer of a hit, the
// material the blow lands on, a creature's voice, the swing, the ranged release, the school wind-up and impact, the
// defence outcome, footsteps by ground and the loot fanfare. Pure data → id mapping with no Unity types; the Game
// layer (Game/Audio/CombatSfx.cs) plays the ids. Tests: Tools/harness/CoreTests/TestsCombatSounds.cs.
//
// Creatures: CreatureDef.material / CreatureDef.voice win when they hold a known value; otherwise the sound profile is
// inferred from the art key (exact key, or the key with a tint suffix such as "cr_r2_cinder_drake_emberjaw"), then the
// creature id, then words in either ("gnoll", "skeleton", "wisp"…), then the family, then the creature type.
// Characters and companions sound like the armour they wear and the weapon they hold.
using System;
using System.Collections.Generic;
using Lanternvale.Data;

namespace Lanternvale.Rules
{
    /// <summary>The inferred (or authored) sound profile of a creature.</summary>
    public struct CreatureSoundProfile
    {
        /// <summary>Material layer key (plate|mail|leather|cloth|flesh|fur|chitin|bone|wood|stone|ether|ice|scale|wet).</summary>
        public string Material;
        /// <summary>Voice key (beast|humanoid|spirit|wood|stone|dragon|frog|gnoll|none).</summary>
        public string Voice;
        /// <summary>Weapon layer id of its natural attack ("hit_bite", "hit_slam", "hit_blade"…).</summary>
        public string Attack;
        /// <summary>Where the profile came from (for tests and debugging): "data", "sprite", "id", "word", "family", "type".</summary>
        public string Source;

        public CreatureSoundProfile(string material, string voice, string attack, string source = "")
        {
            Material = material; Voice = voice; Attack = attack; Source = source;
        }

        public override string ToString() => $"{Material}/{Voice}/{Attack} ({Source})";
    }

    public static class CombatSounds
    {
        // ------------------------------------------------------------------ id manifest

        /// <summary>The 24 ids of the original game (all kept; UI code and docs use them).</summary>
        public static readonly string[] LegacyIds =
        {
            "ui_click", "ui_open", "ui_close", "hit_physical", "hit_crit", "swing", "bow", "cast_start",
            "impact_fire", "impact_frost", "impact_arcane", "impact_shadow", "impact_holy", "impact_nature",
            "heal", "buff", "debuff", "death", "level_up", "quest", "coin", "footstep_grass", "chest_open", "door",
        };

        public static readonly string[] WeaponLayers =
            { "hit_blade", "hit_axe", "hit_blunt", "hit_dagger", "hit_fist", "hit_bite", "hit_claw", "hit_slam", "hit_arrow", "hit_bolt", "hit_bullet" };

        public static readonly string[] Materials =
            { "plate", "mail", "leather", "cloth", "flesh", "fur", "chitin", "bone", "wood", "stone", "ether", "ice", "scale", "wet" };

        public static readonly string[] Voices = { "beast", "humanoid", "spirit", "wood", "stone", "dragon", "frog", "gnoll", "none" };

        /// <summary>Every id added by the expansion (Docs/Expansion.md §4), in the contract's order.</summary>
        public static readonly string[] ExpansionIds = BuildExpansionIds();

        /// <summary>Legacy plus expansion ids: everything the game may ask the player for.</summary>
        public static readonly string[] AllIds = Concat(LegacyIds, ExpansionIds);

        static readonly HashSet<string> AllIdSet = new HashSet<string>(AllIds, StringComparer.Ordinal);
        static readonly HashSet<string> MaterialSet = new HashSet<string>(Materials, StringComparer.Ordinal);
        static readonly HashSet<string> VoiceSet = new HashSet<string>(Voices, StringComparer.Ordinal);

        public static bool IsKnownId(string id) => id != null && AllIdSet.Contains(id);
        public static bool IsMaterial(string m) => m != null && MaterialSet.Contains(m);
        public static bool IsVoice(string v) => v != null && VoiceSet.Contains(v);

        static string[] BuildExpansionIds()
        {
            var l = new List<string>(WeaponLayers);
            foreach (var m in Materials) l.Add("mat_" + m);
            l.AddRange(new[]
            {
                "parry", "block_wood", "block_metal", "dodge", "miss", "resist", "immune", "absorb",
                "swing_light", "swing", "swing_heavy",
                "bow", "xbow_release", "gun_fire", "throw_release", "arrow_flight", "wand_zap",
                "cast_fire", "cast_frost", "cast_arcane", "cast_shadow", "cast_holy", "cast_nature", "cast_lightning",
                "impact_lightning", "shout_horn", "stomp",
                "body_fall_light", "body_fall_heavy", "armor_clatter",
                "vo_beast_yelp", "vo_humanoid_grunt", "vo_spirit_fade", "vo_wood_creak", "vo_stone_crumble", "vo_dragon_roar",
                "vo_frog_croak", "vo_gnoll_yip",
                "footstep_dirt", "footstep_leaves", "footstep_stone", "footstep_snow", "footstep_mud", "armor_jingle",
                "loot_rare", "loot_epic", "loot_legendary", "set_complete", "secret_found", "door_stone", "boss_pull",
                "raid_warning", "quest_accept", "quest_turnin", "portal_whoosh",
            });
            // "swing" and "bow" are legacy ids the expansion re-uses: listed once in AllIds
            var seen = new HashSet<string>(LegacyIds, StringComparer.Ordinal);
            var r = new List<string>();
            foreach (var id in l) if (seen.Add(id)) r.Add(id);
            return r.ToArray();
        }

        static string[] Concat(string[] a, string[] b)
        {
            var r = new string[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }

        // ------------------------------------------------------------------ timing (Game/Units/UnitAnimator.Biped.cs LieWeights)

        /// <summary>Seconds (scaled, presentation time) from PlayDeath until the body lies on the ground (lie 0.36 → 0.72 s).</summary>
        public const float DeathFallDelay = 0.62f;
        /// <summary>Seconds from PlayDowned until the party member lies down (lie 0.18 → 0.56 s).</summary>
        public const float DownedFallDelay = 0.46f;
        /// <summary>Real seconds between the two bone knocks of a skeleton's death rattle: well clear of the 30 ms per-id
        /// rate limit (SfxVoiceRules.MinGap), which counts real (unscaled) time.</summary>
        public const float BoneRattleGap = 0.09f;

        /// <summary>
        /// The second rattle knock's extra delay in scaled seconds (Sfx.PlayAfter queues on scaled time) at this
        /// Time.timeScale: <see cref="BoneRattleGap"/> of real time at any presentation speed (up to 8× with fast
        /// forward), so the knock is not rate-limited away; the plain gap at 1× or slower.
        /// </summary>
        public static float BoneRattleScaledGap(float timeScale) =>
            BoneRattleGap * (timeScale > 1f && !float.IsInfinity(timeScale) ? timeScale : 1f);

        // ------------------------------------------------------------------ weapons

        /// <summary>The contact layer of a hit by a weapon of this type (None = bare hands).</summary>
        public static string WeaponLayerOf(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Dagger: return "hit_dagger";
                case WeaponType.FistWeapon: return "hit_fist";
                case WeaponType.OneHandAxe:
                case WeaponType.TwoHandAxe: return "hit_axe";
                case WeaponType.OneHandSword:
                case WeaponType.TwoHandSword:
                case WeaponType.Polearm: return "hit_blade";
                case WeaponType.OneHandMace:
                case WeaponType.TwoHandMace:
                case WeaponType.Staff:
                case WeaponType.Shield:
                case WeaponType.HeldInOffhand:
                case WeaponType.Wand: return "hit_blunt";
                case WeaponType.Bow: return "hit_arrow";
                case WeaponType.Crossbow: return "hit_bolt";
                case WeaponType.Gun: return "hit_bullet";
                case WeaponType.Thrown: return "hit_dagger";
                default: return "hit_fist";
            }
        }

        /// <summary>The whoosh of a melee swing with this weapon: light (daggers, fists), normal (one-handers), heavy.</summary>
        public static string SwingOf(WeaponType w, bool twoHand)
        {
            switch (w)
            {
                case WeaponType.None:
                case WeaponType.Dagger:
                case WeaponType.FistWeapon:
                case WeaponType.Wand:
                case WeaponType.HeldInOffhand:
                case WeaponType.Thrown: return "swing_light";
                case WeaponType.TwoHandAxe:
                case WeaponType.TwoHandMace:
                case WeaponType.TwoHandSword:
                case WeaponType.Polearm:
                case WeaponType.Staff: return "swing_heavy";
                default: return twoHand ? "swing_heavy" : "swing";
            }
        }

        /// <summary>The release of a ranged weapon (null for melee types).</summary>
        public static string ReleaseOf(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.Bow: return "bow";
                case WeaponType.Crossbow: return "xbow_release";
                case WeaponType.Gun: return "gun_fire";
                case WeaponType.Thrown: return "throw_release";
                case WeaponType.Wand: return "wand_zap";
                default: return null;
            }
        }

        /// <summary>Physical projectiles that whistle through the air (bullets are too fast, magic has its own sound).</summary>
        public static bool HasFlightWhoosh(WeaponType w) => w == WeaponType.Bow || w == WeaponType.Crossbow || w == WeaponType.Thrown;

        /// <summary>
        /// The weapon a unit attacks with in this slot: the equipped item for characters, None for creatures and for
        /// empty hands. Disarm is not considered (the swing still sounds like the weapon it would have used).
        /// </summary>
        public static WeaponType WeaponTypeOf(Unit u, bool offHand, bool ranged)
        {
            if (u == null || u.Class == null) return WeaponType.None;
            var it = ranged ? u.Equipment.Ranged : offHand ? u.Equipment.OffHand : u.Equipment.MainHand;
            if (it == null || it.Def == null) return WeaponType.None;
            if (!ranged && !it.IsWeapon && it.Def.weaponType != WeaponType.Shield) return WeaponType.None;
            return it.Def.weaponType;
        }

        static bool IsTwoHand(Unit u) => u != null && u.Class != null && u.Equipment.HasTwoHander;

        // ------------------------------------------------------------------ units

        /// <summary>The material a blow on this unit sounds like (armour for characters, the creature profile otherwise).</summary>
        public static string MaterialOf(Unit u)
        {
            if (u == null) return "flesh";
            if (u.Class != null && u.Creature == null) return ArmorMaterialOf(u);
            if (u.Creature != null) return ProfileOf(u.Creature).Material;
            return "flesh";
        }

        /// <summary>The voice of a unit (characters and companions are humanoid).</summary>
        public static string VoiceOf(Unit u)
        {
            if (u == null) return "none";
            if (u.Creature != null) return ProfileOf(u.Creature).Voice;
            return "humanoid";
        }

        /// <summary>
        /// A character's armour sound: the chest piece's armour type, else the heaviest armour worn; nothing worn
        /// sounds like cloth.
        /// </summary>
        public static string ArmorMaterialOf(Unit u)
        {
            if (u == null) return "cloth";
            var chest = u.Equipment[EquipSlot.Chest];
            var t = chest != null && chest.Def != null ? chest.Def.armorType : ArmorType.None;
            if (t == ArmorType.None)
                foreach (var kv in u.Equipment.Equipped)
                {
                    var d = kv.Value != null ? kv.Value.Def : null;
                    if (d == null || kv.Key == EquipSlot.OffHand || kv.Key == EquipSlot.MainHand || kv.Key == EquipSlot.Ranged) continue;
                    if (d.armorType > t) t = d.armorType;
                }
            switch (t)
            {
                case ArmorType.Plate: return "plate";
                case ArmorType.Mail: return "mail";
                case ArmorType.Leather: return "leather";
                default: return "cloth";
            }
        }

        /// <summary>Plate and mail wearers jingle when they walk and clatter when they fall.</summary>
        public static bool IsMetal(string material) => material == "plate" || material == "mail";

        /// <summary>The weapon layer of a hit dealt by u (its weapon, or a creature's natural attack).</summary>
        public static string AttackLayerOf(Unit u, bool offHand, bool ranged)
        {
            if (u == null) return "hit_blunt";
            if (u.Class != null && u.Creature == null)
            {
                var w = WeaponTypeOf(u, offHand, ranged);
                if (ranged && w == WeaponType.None) return "hit_dagger";   // thrown rocks, unarmed ranged
                return WeaponLayerOf(w);
            }
            var c = u.Creature;
            if (c == null) return "hit_blunt";
            if (ranged) return CreatureRangedLayer(c);
            return ProfileOf(c).Attack;
        }

        /// <summary>The whoosh of u's melee attack.</summary>
        public static string SwingOf(Unit u, bool offHand)
        {
            if (u == null) return "swing";
            if (u.Class != null && u.Creature == null) return SwingOf(WeaponTypeOf(u, offHand, false), IsTwoHand(u));
            var c = u.Creature;
            if (c == null) return "swing";
            var attack = ProfileOf(c).Attack;
            bool big = c.size >= 2.4f;
            switch (attack)
            {
                case "hit_slam": return "swing_heavy";
                case "hit_bite":
                case "hit_claw":
                case "hit_dagger":
                case "hit_fist": return big ? "swing" : "swing_light";
                default: return big ? "swing_heavy" : "swing";
            }
        }

        /// <summary>
        /// The release of u's ranged attack (a = null for a white ranged swing). Characters: the bow, crossbow, gun,
        /// throw or wand of their weapon attacks (Arcane Shot still twangs), a zap for ranged spells. Creatures: the
        /// projectile kind, and a zap for magic bolts — casters never twang a bowstring.
        /// </summary>
        public static string ReleaseOf(Unit u, School school, AbilityDef a = null)
        {
            if (a != null && HasTag(a, "Thrown")) return "throw_release";
            if (u != null && u.Class != null && u.Creature == null)
            {
                bool weapon = a == null || a.special == "Shoot" || AbilityRules.IsRangedWeaponAbility(a) || HasTag(a, "Shot");
                if (weapon) return ReleaseOf(WeaponTypeOf(u, false, true)) ?? "throw_release";
                return school != School.Physical ? "wand_zap" : "throw_release";
            }
            if (school != School.Physical) return "wand_zap";
            var c = u != null ? u.Creature : null;
            if (c == null) return "bow";
            if (a == null && c.meleeSchool != School.Physical) return "wand_zap";
            switch (CreatureRangedLayer(c))
            {
                case "hit_arrow": return "bow";
                case "hit_bolt": return "xbow_release";
                case "hit_bullet": return "gun_fire";
                default: return "throw_release";
            }
        }

        /// <summary>True when the release sends a physical projectile that whooshes through the air (arrows, bolts, throws).</summary>
        public static bool FlightWhooshOf(Unit u, School school, AbilityDef a = null)
        {
            var r = ReleaseOf(u, school, a);
            return r == "bow" || r == "xbow_release" || r == "throw_release";
        }

        static string CreatureRangedLayer(CreatureDef c)
        {
            var p = (c.projectile ?? "").ToLowerInvariant();
            if (p.Contains("arrow")) return "hit_arrow";
            if (p.Contains("dart") || HasWord(c.sprite, "dart") || HasWord(c.id, "dart")) return "hit_bolt";   // spring-shot darts click and thump
            if (p.Contains("bullet") || p.Contains("shot")) return "hit_bullet";
            if (p.Contains("bolt")) return c.meleeSchool == School.Physical ? "hit_bolt" : "hit_blunt";
            if (p.Contains("spear") || p.Contains("knife") || p.Contains("dagger") || p.Contains("axe")) return "hit_dagger";
            if (HasWord(c.sprite, "archer") || HasWord(c.id, "archer") || HasWord(c.sprite, "hunter") || HasWord(c.id, "hunter")) return "hit_arrow";
            return "hit_blunt";   // rocks, slings, spit
        }

        // ------------------------------------------------------------------ creature profiles

        static CreatureSoundProfile P(string m, string v, string a, string source = "") => new CreatureSoundProfile(m, v, a, source);

        /// <summary>Art keys (Expansion.md §9 and the original game). Tinted variants "<key>_<suffix>" match their key.</summary>
        static readonly Dictionary<string, CreatureSoundProfile> ByKey = new Dictionary<string, CreatureSoundProfile>(StringComparer.Ordinal)
        {
            // original game
            ["cr_bandit"] = P("leather", "humanoid", "hit_dagger"),
            ["cr_bandit_archer"] = P("leather", "humanoid", "hit_blade"),
            ["cr_bandit_hexer"] = P("cloth", "humanoid", "hit_blunt"),
            ["cr_bandit_chief"] = P("leather", "humanoid", "hit_blunt"),
            ["cr_wolf"] = P("fur", "beast", "hit_bite"),
            ["cr_wolf_greymane"] = P("fur", "beast", "hit_bite"),
            ["cr_wolf_blighted"] = P("fur", "beast", "hit_bite"),
            ["cr_boar"] = P("fur", "beast", "hit_bite"),
            ["cr_spider"] = P("chitin", "beast", "hit_bite"),
            ["cr_owl"] = P("fur", "beast", "hit_claw"),
            ["cr_hollow_spirit"] = P("ether", "spirit", "hit_claw"),
            ["cr_hollow_keeper"] = P("ether", "spirit", "hit_blunt"),
            ["cr_hollow_wisp"] = P("ether", "spirit", "hit_fist"),
            ["cr_hollow_treant"] = P("wood", "wood", "hit_slam"),
            ["cr_hollow_warden"] = P("fur", "spirit", "hit_slam"),
            ["cr_mossling"] = P("wood", "wood", "hit_blunt"),
            ["cr_mossling_shaman"] = P("wood", "wood", "hit_blunt"),
            ["cr_training_dummy"] = P("wood", "none", "hit_blunt"),
            ["pet_wolf"] = P("fur", "beast", "hit_bite"),
            ["pet_boar"] = P("fur", "beast", "hit_bite"),
            ["pet_cat"] = P("fur", "beast", "hit_claw"),
            ["pet_bear"] = P("fur", "beast", "hit_claw"),
            ["pet_owl"] = P("fur", "beast", "hit_claw"),
            ["pet_spider"] = P("chitin", "beast", "hit_bite"),
            ["demon_imp"] = P("flesh", "humanoid", "hit_claw"),
            ["demon_voidwalker"] = P("ether", "spirit", "hit_slam"),
            ["demon_succubus"] = P("leather", "humanoid", "hit_blade"),
            ["demon_felhunter"] = P("flesh", "beast", "hit_bite"),
            ["demon_infernal"] = P("stone", "stone", "hit_slam"),
            ["totem_earth"] = P("wood", "none", "hit_blunt"),
            ["totem_fire"] = P("wood", "none", "hit_blunt"),
            ["totem_water"] = P("wood", "none", "hit_blunt"),
            ["totem_air"] = P("wood", "none", "hit_blunt"),
            ["fx_rune_circle"] = P("stone", "none", "hit_blunt"),
            ["prop_spirit_lantern"] = P("ether", "none", "hit_fist"),

            // models-a: humanoid bipeds
            ["cr_gnoll"] = P("fur", "gnoll", "hit_axe"),
            ["cr_gnoll_archer"] = P("fur", "gnoll", "hit_blade"),
            ["cr_gnoll_mystic"] = P("fur", "gnoll", "hit_blunt"),
            ["cr_gnoll_chief"] = P("fur", "gnoll", "hit_axe"),
            ["cr_tunneler"] = P("leather", "humanoid", "hit_axe"),
            ["cr_tunneler_geomancer"] = P("leather", "humanoid", "hit_blunt"),
            ["cr_mireling"] = P("wet", "frog", "hit_blade"),
            ["cr_mireling_hunter"] = P("wet", "frog", "hit_dagger"),
            ["cr_mireling_oracle"] = P("wet", "frog", "hit_blunt"),
            ["cr_mire_hag"] = P("cloth", "humanoid", "hit_claw"),
            ["cr_ogre"] = P("flesh", "humanoid", "hit_blunt"),
            ["cr_ogre_mage"] = P("flesh", "humanoid", "hit_blunt"),
            ["cr_dragonsworn"] = P("mail", "humanoid", "hit_blade"),
            ["cr_mossling_king"] = P("wood", "wood", "hit_blunt"),

            // models-b: undead, elementals, humanoid bosses
            ["cr_skeleton"] = P("bone", "stone", "hit_blade"),
            ["cr_skeleton_archer"] = P("bone", "stone", "hit_blade"),
            ["cr_barrow_wight"] = P("mail", "spirit", "hit_blade"),
            ["cr_bog_ghoul"] = P("wet", "beast", "hit_claw"),
            ["cr_hollow_knight"] = P("plate", "spirit", "hit_blade"),
            ["cr_lantern_lich"] = P("cloth", "spirit", "hit_blunt"),
            ["cr_drowned_sentinel"] = P("plate", "spirit", "hit_blade"),
            ["cr_tidewitch"] = P("wet", "humanoid", "hit_blunt"),
            ["cr_dg4_king_aldwin"] = P("plate", "spirit", "hit_blade"),
            ["cr_ice_elemental"] = P("ice", "stone", "hit_slam"),
            ["cr_ash_elemental"] = P("stone", "stone", "hit_slam"),
            ["cr_fen_wisp"] = P("ether", "spirit", "hit_fist"),
            ["cr_rimeheart"] = P("ice", "stone", "hit_slam"),
            ["cr_r1_twin"] = P("ether", "spirit", "hit_claw"),
            ["cr_r1_mother_mire"] = P("wet", "humanoid", "hit_slam"),
            ["cr_r2_varkas"] = P("plate", "humanoid", "hit_blade"),

            // models-c: beasts, fliers, giants, dragons
            ["cr_hawk"] = P("fur", "beast", "hit_claw"),
            ["cr_harpy"] = P("fur", "beast", "hit_claw"),
            ["cr_crocolisk"] = P("scale", "beast", "hit_bite"),
            ["cr_wolf_frost"] = P("fur", "beast", "hit_bite"),
            ["cr_yeti"] = P("fur", "beast", "hit_claw"),
            ["cr_rootling"] = P("wood", "wood", "hit_blunt"),
            ["cr_blight_hound"] = P("fur", "beast", "hit_bite"),
            ["cr_spider_giant"] = P("chitin", "beast", "hit_bite"),
            ["cr_rootwarden"] = P("wood", "wood", "hit_slam"),
            ["cr_drake_whelp"] = P("scale", "dragon", "hit_bite"),
            ["cr_frost_drake"] = P("scale", "dragon", "hit_bite"),
            ["cr_r1_thornmaw"] = P("wood", "wood", "hit_bite"),
            ["cr_r1_hollow_heart"] = P("wood", "spirit", "hit_slam"),
            ["cr_r2_frostclaw"] = P("fur", "beast", "hit_claw"),
            ["cr_r2_cinder_drake"] = P("scale", "dragon", "hit_bite"),
            ["cr_r2_emberjaw"] = P("scale", "dragon", "hit_bite"),
            ["cr_r2_ashtongue"] = P("scale", "dragon", "hit_bite"),
            ["cr_r2_vyrmathra"] = P("scale", "dragon", "hit_bite"),

            // models-people (normally characters or NPCs; a creature using the art sounds like them)
            ["comp_bruna"] = P("mail", "humanoid", "hit_blade"),
            ["comp_ysolde"] = P("mail", "humanoid", "hit_blunt"),
            ["comp_liora"] = P("cloth", "humanoid", "hit_blunt"),
            ["comp_nanami"] = P("leather", "humanoid", "hit_blunt"),
            ["npc_town_guard"] = P("mail", "humanoid", "hit_blade"),
            ["npc_quartermaster"] = P("leather", "humanoid", "hit_fist"),
        };

        /// <summary>Whole words in the art key or id, most specific first (an ice elemental is ice before it is an elemental).</summary>
        static readonly KeyValuePair<string[], CreatureSoundProfile>[] ByWord =
        {
            W(P("fur", "gnoll", "hit_axe"), "gnoll"),
            W(P("wet", "frog", "hit_blade"), "mireling", "murloc", "frog", "toad"),
            W(P("bone", "stone", "hit_blade"), "skeleton", "skeletal", "bone", "bones", "skull"),
            W(P("wet", "beast", "hit_claw"), "ghoul", "zombie", "drowned"),
            W(P("plate", "spirit", "hit_blade"), "knight", "sentinel", "highlord", "aldwin", "revenant"),
            W(P("mail", "spirit", "hit_blade"), "wight"),
            W(P("cloth", "spirit", "hit_blunt"), "lich", "necromancer"),
            W(P("ether", "spirit", "hit_claw"), "wisp", "spirit", "ghost", "wraith", "shade", "phantom", "specter", "spectre", "twin", "banshee"),
            W(P("fur", "beast", "hit_claw"), "yeti", "sasquatch", "frostclaw", "bear", "cat", "lynx", "panther", "tiger", "owl", "hawk", "harpy", "eagle", "bat"),
            W(P("flesh", "humanoid", "hit_blunt"), "ogre", "troll", "brute"),   // not "giant": a giant spider is a spider
            W(P("scale", "dragon", "hit_bite"), "drake", "dragon", "wyrm", "whelp", "wyvern", "vyrmathra"),
            W(P("scale", "beast", "hit_bite"), "croc", "crocolisk", "lizard", "basilisk", "turtle", "serpent", "snake", "raptor"),
            W(P("chitin", "beast", "hit_bite"), "spider", "scorpid", "scorpion", "beetle", "crab", "spiderling"),
            W(P("fur", "beast", "hit_bite"), "wolf", "hound", "dog", "boar", "rat", "stag", "hyena", "fox", "worg"),
            W(P("wood", "wood", "hit_slam"), "treant", "rootwarden", "ancient", "thornmaw"),
            W(P("wood", "wood", "hit_blunt"), "rootling", "root", "thorn", "moss", "mossling", "bark", "sapling", "dummy", "totem"),
            W(P("ice", "stone", "hit_slam"), "ice", "rime", "rimeheart", "glacial"),
            W(P("stone", "stone", "hit_slam"), "elemental", "golem", "stone", "rock", "earth", "ash", "ember", "magma", "infernal", "gargoyle"),
            W(P("wet", "humanoid", "hit_blunt"), "tidewitch", "hag", "mire"),
            W(P("plate", "humanoid", "hit_blade"), "varkas", "paladin", "guard", "champion"),
            W(P("mail", "humanoid", "hit_blade"), "dragonsworn", "soldier", "captain", "warrior", "vanguard", "footman"),
            W(P("cloth", "humanoid", "hit_blunt"), "witch", "mage", "shaman", "priest", "oracle", "mystic", "cultist", "acolyte", "hexer", "geomancer", "warlock", "sage"),
            W(P("leather", "humanoid", "hit_dagger"), "bandit", "thief", "rogue", "cutthroat", "brigand", "thug", "poacher"),
            W(P("leather", "humanoid", "hit_blade"), "raider", "archer", "hunter", "tunneler", "mudpaw", "scout", "smuggler"),
        };

        static KeyValuePair<string[], CreatureSoundProfile> W(CreatureSoundProfile p, params string[] words) =>
            new KeyValuePair<string[], CreatureSoundProfile>(words, p);

        static readonly Dictionary<string, CreatureSoundProfile> ByFamily = new Dictionary<string, CreatureSoundProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wolf"] = P("fur", "beast", "hit_bite"),
            ["Boar"] = P("fur", "beast", "hit_bite"),
            ["Hyena"] = P("fur", "beast", "hit_bite"),
            ["Cat"] = P("fur", "beast", "hit_claw"),
            ["Bear"] = P("fur", "beast", "hit_claw"),
            ["Owl"] = P("fur", "beast", "hit_claw"),
            ["Bat"] = P("fur", "beast", "hit_bite"),
            ["Raptor"] = P("scale", "beast", "hit_bite"),
            ["Crocolisk"] = P("scale", "beast", "hit_bite"),
            ["Turtle"] = P("scale", "beast", "hit_bite"),
            ["Spider"] = P("chitin", "beast", "hit_bite"),
            ["Scorpid"] = P("chitin", "beast", "hit_claw"),
            ["Crab"] = P("chitin", "beast", "hit_claw"),
            ["Imp"] = P("flesh", "humanoid", "hit_claw"),
            ["Voidwalker"] = P("ether", "spirit", "hit_slam"),
            ["Succubus"] = P("leather", "humanoid", "hit_blade"),
            ["Felhunter"] = P("flesh", "beast", "hit_bite"),
            ["Infernal"] = P("stone", "stone", "hit_slam"),
        };

        static CreatureSoundProfile ByType(CreatureType t)
        {
            switch (t)
            {
                case CreatureType.Humanoid: return P("leather", "humanoid", "hit_blade");
                case CreatureType.Beast: return P("fur", "beast", "hit_bite");
                case CreatureType.Undead: return P("bone", "spirit", "hit_claw");
                case CreatureType.Demon: return P("flesh", "beast", "hit_claw");
                case CreatureType.Elemental: return P("stone", "stone", "hit_slam");
                case CreatureType.Spirit: return P("ether", "spirit", "hit_claw");
                case CreatureType.Dragonkin: return P("scale", "dragon", "hit_bite");
                case CreatureType.Giant: return P("flesh", "humanoid", "hit_slam");
                case CreatureType.Mechanical: return P("wood", "none", "hit_blunt");
                case CreatureType.Critter: return P("fur", "beast", "hit_bite");
                case CreatureType.Totem: return P("wood", "none", "hit_blunt");
                default: return P("flesh", "beast", "hit_blunt");
            }
        }

        // inference per def (a pure function of sprite, id, family and type); the authored fields are applied on every
        // call, so a def edited in place (tests, hot-reloaded data) is honoured
        static readonly Dictionary<CreatureDef, CreatureSoundProfile> inferred = new Dictionary<CreatureDef, CreatureSoundProfile>();
        static readonly object cacheLock = new object();

        /// <summary>The creature's sound profile: the authored material/voice when they are known values, inference for the rest.</summary>
        public static CreatureSoundProfile ProfileOf(CreatureDef c)
        {
            if (c == null) return P("flesh", "beast", "hit_blunt", "default");
            CreatureSoundProfile p;
            bool have;
            lock (cacheLock) have = inferred.TryGetValue(c, out p);
            if (!have)
            {
                p = Infer(c);
                lock (cacheLock) inferred[c] = p;
            }
            bool m = IsMaterial(c.material), v = IsVoice(c.voice);
            if (m) p.Material = c.material;
            if (v) p.Voice = c.voice;
            if (m || v) p.Source = "data";
            return p;
        }

        /// <summary>Forgets the inferred profiles (tests that change a def's sprite, id, family or type in place).</summary>
        public static void ClearCache() { lock (cacheLock) inferred.Clear(); }

        /// <summary>The inferred profile, ignoring the authored material and voice.</summary>
        public static CreatureSoundProfile Infer(CreatureDef c)
        {
            if (c == null) return P("flesh", "beast", "hit_blunt", "default");
            CreatureSoundProfile p;
            if (TryKey(c.sprite, out p)) { p.Source = "sprite"; return p; }
            if (TryKey(c.id, out p)) { p.Source = "id"; return p; }
            if (TryWords(c.sprite, out p) || TryWords(c.id, out p)) { p.Source = "word"; return p; }
            if (!string.IsNullOrEmpty(c.family) && ByFamily.TryGetValue(c.family, out p)) { p.Source = "family"; return p; }
            p = ByType(c.type);
            p.Source = "type";
            return p;
        }

        /// <summary>The exact key, else the longest known key the string starts with at a '_' boundary ("cr_ogre_mage_elite" → "cr_ogre_mage").</summary>
        static bool TryKey(string key, out CreatureSoundProfile p)
        {
            p = default;
            if (string.IsNullOrEmpty(key)) return false;
            if (ByKey.TryGetValue(key, out p)) return true;
            for (int i = key.Length - 1; i > 0; i--)
            {
                if (key[i] != '_') continue;
                if (ByKey.TryGetValue(key.Substring(0, i), out p)) return true;
            }
            return false;
        }

        static bool TryWords(string key, out CreatureSoundProfile p)
        {
            p = default;
            if (string.IsNullOrEmpty(key)) return false;
            var words = key.ToLowerInvariant().Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var kv in ByWord)
                foreach (var w in kv.Key)
                    if (Array.IndexOf(words, w) >= 0) { p = kv.Value; return true; }
            return false;
        }

        static bool HasWord(string key, string word)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var words = key.ToLowerInvariant().Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
            return Array.IndexOf(words, word) >= 0;
        }

        // ------------------------------------------------------------------ materials, voices, deaths

        /// <summary>"mat_<material>" for a known material, else "mat_flesh".</summary>
        public static string MaterialLayer(string material) => "mat_" + (IsMaterial(material) ? material : "flesh");

        /// <summary>The death/pain vocal of a voice key (null for "none" and unknown keys).</summary>
        public static string VocalOf(string voice)
        {
            switch (voice)
            {
                case "beast": return "vo_beast_yelp";
                case "humanoid": return "vo_humanoid_grunt";
                case "spirit": return "vo_spirit_fade";
                case "wood": return "vo_wood_creak";
                case "stone": return "vo_stone_crumble";
                case "dragon": return "vo_dragon_roar";
                case "frog": return "vo_frog_croak";
                case "gnoll": return "vo_gnoll_yip";
                default: return null;
            }
        }

        /// <summary>
        /// The death vocal of a unit: its voice's vocal (<see cref="VocalOf(string)"/>), refined by body. Spiders and
        /// other chitin beasts die without a cry, and so do small reptiles (scale); big beasts (≥ 2.4 m: yetis,
        /// Frostclaw) roar instead of yelping.
        /// </summary>
        public static string VocalOf(Unit u)
        {
            if (u == null) return null;
            var voice = VoiceOf(u);
            var c = u.Class == null ? u.Creature : null;
            if (c != null && voice == "beast")
            {
                var m = ProfileOf(c).Material;
                if (m == "chitin") return null;
                if (c.size >= BigBeastSize) return "vo_dragon_roar";
                if (m == "scale") return null;
            }
            return VocalOf(voice);
        }

        const float BigBeastSize = 2.4f;

        /// <summary>
        /// The pitch of a unit's death vocal. Roars follow the body: dragons sqrt(4.2/size) in [0.85, 1.8] (whelp 1.1 m →
        /// 1.8, drake 4.2 m → 1, Vyrmathra 7.5 m → 0.85), big beasts sqrt(9/size) in [1, 1.6] (yeti 2.8 m → 1.6,
        /// Frostclaw 5 m → 1.34). A grunt from a female body (<see cref="IsFemaleArt"/>) sits about a fourth higher (×1.35,
        /// at most 1.4: the shift moves the formants too). Everything else uses <see cref="SizePitchOf"/>.
        /// </summary>
        public static float VocalPitchOf(Unit u)
        {
            var vocal = VocalOf(u);
            var c = u != null && u.Class == null ? u.Creature : null;
            if (vocal == "vo_dragon_roar" && c != null)
            {
                float size = Math.Max(0.3f, c.size);
                if (VoiceOf(u) == "dragon") return Clamp((float)Math.Sqrt(4.2 / size), 0.85f, 1.8f);
                return Clamp((float)Math.Sqrt(9.0 / size), 1f, 1.6f);
            }
            float p = SizePitchOf(u);
            if (vocal == "vo_humanoid_grunt" && IsFemaleArt(u)) p = Math.Min(1.4f, p * 1.35f);
            return p;
        }

        /// <summary>The volume of a death vocal: 0.8, 0.6 for a party member going down; small roarers (whelps) are softer.</summary>
        public static float VocalVolumeOf(Unit u, bool downed)
        {
            float v = downed ? 0.6f : 0.8f;
            var c = u != null && u.Class == null ? u.Creature : null;
            if (c != null && c.size < 2f && VocalOf(u) == "vo_dragon_roar") v *= 0.7f;
            return v;
        }

        static float Clamp(float x, float lo, float hi) => x < lo ? lo : x > hi ? hi : x;

        /// <summary>
        /// Art keys built on a female body (Game/Units, BipedKit female = true) whose units can grunt: the Hunter, Mage
        /// and Priest heroes, eight companions, the succubus and the witches. Tinted variants "&lt;key&gt;_&lt;suffix&gt;" match.
        /// </summary>
        static readonly HashSet<string> FemaleArt = new HashSet<string>(StringComparer.Ordinal)
        {
            "char_hunter", "char_mage", "char_priest",
            "comp_lys", "comp_seren", "comp_morwen", "comp_pip", "comp_bruna", "comp_ysolde", "comp_liora", "comp_nanami",
            "demon_succubus", "cr_mire_hag", "cr_tidewitch", "cr_dg5_tidewitch", "cr_r1_mother_mire", "cr_r1_twin",
        };

        /// <summary>True when the unit's art (Unit.Sprite, else its creature's sprite) is one of the female bodies.</summary>
        public static bool IsFemaleArt(Unit u)
        {
            if (u == null) return false;
            if (MatchesKey(FemaleArt, u.Sprite)) return true;
            return u.Creature != null && MatchesKey(FemaleArt, u.Creature.sprite);
        }

        static bool MatchesKey(HashSet<string> keys, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (keys.Contains(key)) return true;
            for (int i = key.Length - 1; i > 0; i--)
                if (key[i] == '_' && keys.Contains(key.Substring(0, i))) return true;
            return false;
        }

        /// <summary>
        /// The thud of the body landing (null for things that do not fall: ether spirits fade, totems and mechanical traps
        /// vanish). Heavy for big creatures (size ≥ 2.2 m), giants and stone bodies of 1.2 m and more; light otherwise (a
        /// dragon whelp lands light).
        /// </summary>
        public static string BodyFallOf(Unit u)
        {
            if (u == null) return null;
            if (u.Kind == UnitKind.Totem) return null;
            var c = u.Class == null ? u.Creature : null;
            if (c == null) return "body_fall_light";
            if (c.type == CreatureType.Totem || c.rank == CreatureRank.Totem) return null;
            if (IsTrap(c)) return null;
            var m = ProfileOf(c).Material;
            if (m == "ether") return null;
            if (c.size >= 2.2f || c.type == CreatureType.Giant || (m == "stone" && c.size >= 1.2f)) return "body_fall_heavy";
            return "body_fall_light";
        }

        /// <summary>A mechanical trap (the barrow's dart trap, drawn as an fx_ plate on the ground): no body to drop.</summary>
        static bool IsTrap(CreatureDef c) =>
            c.type == CreatureType.Mechanical &&
            (HasWord(c.id, "trap") || HasWord(c.sprite, "trap") || (c.sprite ?? "").StartsWith("fx_", StringComparison.Ordinal));

        /// <summary>
        /// Bones rattle after the fall: skeletons (bone, or a skeleton body such as the drowned dead) have no voice, so the
        /// clatter of bone is their death cry (CombatSfx plays two quick mat_bone hits).
        /// </summary>
        public static bool RattlesOf(Unit u)
        {
            if (u == null || BodyFallOf(u) == null) return false;
            if (MaterialOf(u) == "bone") return true;
            var c = u.Class == null ? u.Creature : null;
            return c != null && (HasWord(c.sprite, "skeleton") || HasWord(c.id, "skeleton"));
        }

        /// <summary>Armour clatters on the ground after the fall (plate and mail).</summary>
        public static bool ClattersOf(Unit u) => u != null && u.Kind != UnitKind.Totem && IsMetal(MaterialOf(u));

        // ------------------------------------------------------------------ defence outcomes

        /// <summary>The sound of an avoided or soaked blow (null for event types that are not defence outcomes).</summary>
        public static string AvoidOf(CombatEventType type, Unit target)
        {
            switch (type)
            {
                case CombatEventType.Miss: return "miss";
                case CombatEventType.Dodge:
                case CombatEventType.Evade: return "dodge";
                case CombatEventType.Parry: return "parry";
                case CombatEventType.Block: return BlockOf(target);
                case CombatEventType.Resist: return "resist";
                case CombatEventType.Immune: return "immune";
                case CombatEventType.Absorb: return "absorb";
                default: return null;
            }
        }

        static readonly string[] MetalWords = { "iron", "steel", "bronze", "silver", "golden", "mithril", "thorium", "metal", "plated", "tower", "aegis", "bulwark_of_iron" };
        static readonly string[] WoodWords = { "wood", "wooden", "oak", "oaken", "heartwood", "buckler", "willow", "ash", "pine", "bark" };

        /// <summary>A shield block: metal when the shield's id or name says so, wood otherwise (every original shield is wooden).
        /// A creature without a shield blocks with its hide: metal for plate/mail/stone/ice, wood otherwise.</summary>
        public static string BlockOf(Unit target)
        {
            if (target == null) return "block_wood";
            if (target.Class != null && target.Creature == null)
            {
                var sh = target.Equipment.OffHand;
                if (sh != null && sh.Def != null && sh.Def.weaponType == WeaponType.Shield) return IsMetalShield(sh.Def) ? "block_metal" : "block_wood";
                return IsMetal(ArmorMaterialOf(target)) ? "block_metal" : "block_wood";
            }
            var m = MaterialOf(target);
            return IsMetal(m) || m == "stone" || m == "ice" ? "block_metal" : "block_wood";
        }

        public static bool IsMetalShield(ItemDef d)
        {
            if (d == null) return false;
            var words = ((d.id ?? "") + "_" + (d.name ?? "")).ToLowerInvariant().Split(new[] { '_', ' ', '-', '\'' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in WoodWords) if (Array.IndexOf(words, w) >= 0) return false;
            foreach (var w in MetalWords) if (Array.IndexOf(words, w) >= 0) return true;
            return false;
        }

        // ------------------------------------------------------------------ spells

        public static bool HasTag(AbilityDef a, string tag)
        {
            if (a == null || a.tags == null) return false;
            foreach (var t in a.tags) if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsLightning(AbilityDef a) => HasTag(a, "Lightning");
        public static bool IsShout(AbilityDef a) => HasTag(a, "Shout");

        /// <summary>A ground-shaking physical area attack (Thunder Clap, Antler Stomp, slams and quakes).</summary>
        public static bool IsStomp(AbilityDef a)
        {
            if (a == null || a.school != School.Physical) return false;
            if (HasTag(a, "Stomp")) return true;
            var s = ((a.id ?? "") + "_" + (a.name ?? "")).ToLowerInvariant().Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var w in new[] { "stomp", "clap", "slam", "quake", "earthquake", "smash", "tremor", "shockwave" })
                if (Array.IndexOf(s, w) >= 0) return true;
            return false;
        }

        /// <summary>The wind-up of a cast: cast_<school>, cast_lightning for Lightning-tagged spells, cast_start for physical.</summary>
        public static string CastWindupOf(AbilityDef a, School school)
        {
            if (IsLightning(a)) return "cast_lightning";
            switch (school)
            {
                case School.Fire: return "cast_fire";
                case School.Frost: return "cast_frost";
                case School.Arcane: return "cast_arcane";
                case School.Shadow: return "cast_shadow";
                case School.Holy: return "cast_holy";
                case School.Nature: return "cast_nature";
                default: return "cast_start";
            }
        }

        /// <summary>impact_<school> (impact_lightning for Lightning-tagged spells); null for Physical (the weapon and material layers speak).</summary>
        public static string SpellImpactOf(AbilityDef a, School school)
        {
            if (IsLightning(a)) return "impact_lightning";
            switch (school)
            {
                case School.Fire: return "impact_fire";
                case School.Frost: return "impact_frost";
                case School.Arcane: return "impact_arcane";
                case School.Shadow: return "impact_shadow";
                case School.Holy: return "impact_holy";
                case School.Nature: return "impact_nature";
                default: return null;
            }
        }

        /// <summary>
        /// The burst of an area ability as it goes off: the shout horn, a stomp, the school impact; null for other
        /// physical areas (Whirlwind, Cleave: the hits on each target make the sound).
        /// </summary>
        public static string AreaBurstOf(AbilityDef a, School school)
        {
            if (IsShout(a)) return "shout_horn";
            if (school == School.Physical) return IsStomp(a) ? "stomp" : null;
            return SpellImpactOf(a, school);
        }

        /// <summary>A damage-over-time tick: a soft wet squelch for bleeds, the school impact (quiet) otherwise.</summary>
        public static string TickOf(AbilityDef a, School school) => school == School.Physical ? "mat_wet" : SpellImpactOf(a, school);

        // ------------------------------------------------------------------ loudness

        /// <summary>
        /// Hit loudness from the share of the target's health the blow took: lerp(0.65, 1, sqrt(clamp01(4·amount/maxHp))),
        /// ×1.15 for crits and ×1.1 for killing blows.
        /// </summary>
        public static float HitVolume(float amount, float maxHealth, bool crit, bool killingBlow)
        {
            if (float.IsNaN(amount) || amount < 0f) amount = 0f;
            float frac = amount / Math.Max(1f, float.IsNaN(maxHealth) ? 1f : maxHealth);
            float x = (float)Math.Sqrt(Math.Max(0f, Math.Min(1f, frac * 4f)));
            float v = 0.65f + 0.35f * x;
            if (crit) v *= 1.15f;
            if (killingBlow) v *= 1.1f;
            return v;
        }

        /// <summary>A pitch tilt by size and rank: bosses and big creatures sound deeper, tiny ones brighter.</summary>
        public static float SizePitchOf(Unit u)
        {
            var c = u != null && u.Class == null ? u.Creature : null;
            if (c == null) return 1f;
            if (c.rank == CreatureRank.Boss || c.size >= 3f) return 0.9f;
            if (c.size >= 2.2f || c.rank == CreatureRank.Elite) return 0.95f;
            if (c.size < 0.95f) return 1.06f;
            return 1f;
        }

        /// <summary>Material layers on AoE beats: only the first 3 targets of one moment, softened by 1/sqrt(n).</summary>
        public static float AreaLayerGain(int targetIndex)
        {
            if (targetIndex < 1) targetIndex = 1;
            if (targetIndex > 3) return 0f;
            return (float)(1.0 / Math.Sqrt(targetIndex));
        }

        // ------------------------------------------------------------------ world

        /// <summary>The footstep id of a map: by biome, else by keywords in the ground key, the id and the music key.</summary>
        public static string FootstepOf(MapDef map)
        {
            if (map == null) return "footstep_grass";
            switch (map.biome ?? "")
            {
                case "meadow": return "footstep_grass";
                case "village": return "footstep_dirt";
                case "highlands": return "footstep_dirt";
                case "forest": return "footstep_leaves";
                case "hollow_heart": return "footstep_leaves";
                case "shrine":
                case "cave":
                case "crypt":
                case "roost": return "footstep_stone";
                case "peaks":
                case "ice_cave": return "footstep_snow";
                case "fen": return "footstep_mud";
            }
            return FootstepByWords((map.ground ?? "") + "_" + (map.id ?? "") + "_" + (map.music ?? "") + "_" + (map.environment ?? ""));
        }

        static string FootstepByWords(string s)
        {
            var words = s.ToLowerInvariant().Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
            bool Any(params string[] ws) { foreach (var w in ws) if (Array.IndexOf(words, w) >= 0) return true; return false; }
            if (Any("snow", "peaks", "peak", "ice", "frost", "frozen", "glacier", "skyreach")) return "footstep_snow";
            if (Any("fen", "mire", "bog", "swamp", "marsh", "mud", "mirefen")) return "footstep_mud";
            if (Any("shrine", "stone", "cave", "crypt", "dungeon", "catacombs", "barrow", "vault", "sanctum", "ruin", "roost", "cobble")) return "footstep_stone";
            if (Any("forest", "wood", "woods", "grove", "whisperwood", "leaves")) return "footstep_leaves";
            if (Any("village", "town", "dirt", "road", "farm", "lanternvale", "brightwater")) return "footstep_dirt";
            return "footstep_grass";
        }

        /// <summary>The fanfare for receiving an item of this quality (null = the plain pickup sound).</summary>
        public static string LootOf(Quality q)
        {
            switch (q)
            {
                case Quality.Rare: return "loot_rare";
                case Quality.Epic: return "loot_epic";
                case Quality.Legendary: return "loot_legendary";
                default: return null;
            }
        }
    }
}
