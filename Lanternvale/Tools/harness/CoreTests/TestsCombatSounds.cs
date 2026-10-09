// Combat sound mapping (Core/Rules/Combat/CombatSounds.cs, played by Game/Audio/CombatSfx.cs): the Expansion.md §4 id
// manifest, weapon → layer / swing / release, creature material and voice (authored first, inference for every §9 art
// key and every creature in data), character armour, defence outcomes, spells by school and tag, deaths, footsteps by
// biome, loot fanfares, loudness — and that every id the mapping or the Game code can ask for is a known id.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Lanternvale.Data;
using Lanternvale.Rules;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsCombatSounds
    {
        // Docs/Expansion.md §4, verbatim
        static readonly string[] ContractIds =
        {
            "hit_blade", "hit_axe", "hit_blunt", "hit_dagger", "hit_fist", "hit_bite", "hit_claw", "hit_slam", "hit_arrow", "hit_bolt", "hit_bullet",
            "mat_plate", "mat_mail", "mat_leather", "mat_cloth", "mat_flesh", "mat_fur", "mat_chitin", "mat_bone", "mat_wood", "mat_stone", "mat_ether", "mat_ice", "mat_scale", "mat_wet",
            "parry", "block_wood", "block_metal", "dodge", "miss", "resist", "immune", "absorb",
            "swing_light", "swing", "swing_heavy",
            "bow", "xbow_release", "gun_fire", "throw_release", "arrow_flight", "wand_zap",
            "cast_fire", "cast_frost", "cast_arcane", "cast_shadow", "cast_holy", "cast_nature", "cast_lightning", "impact_lightning", "shout_horn", "stomp", "cast_start",
            "body_fall_light", "body_fall_heavy", "armor_clatter", "vo_beast_yelp", "vo_humanoid_grunt", "vo_spirit_fade", "vo_wood_creak", "vo_stone_crumble", "vo_dragon_roar", "vo_frog_croak", "vo_gnoll_yip",
            "footstep_dirt", "footstep_leaves", "footstep_stone", "footstep_snow", "footstep_mud", "armor_jingle",
            "loot_rare", "loot_epic", "loot_legendary", "set_complete", "secret_found", "door_stone", "boss_pull", "raid_warning", "quest_accept", "quest_turnin", "portal_whoosh",
        };

        // Docs/Expansion.md §9 (models-a, -b, -c)
        static readonly string[] ModelKeys =
        {
            "cr_gnoll", "cr_gnoll_archer", "cr_gnoll_mystic", "cr_gnoll_chief", "cr_tunneler", "cr_tunneler_geomancer",
            "cr_mireling", "cr_mireling_hunter", "cr_mireling_oracle", "cr_mire_hag", "cr_ogre", "cr_ogre_mage", "cr_dragonsworn", "cr_mossling_king",
            "cr_skeleton", "cr_skeleton_archer", "cr_barrow_wight", "cr_bog_ghoul", "cr_hollow_knight",
            "cr_lantern_lich", "cr_drowned_sentinel", "cr_tidewitch", "cr_dg4_king_aldwin",
            "cr_ice_elemental", "cr_ash_elemental", "cr_fen_wisp", "cr_rimeheart",
            "cr_r1_twin", "cr_r1_mother_mire", "cr_r2_varkas",
            "cr_hawk", "cr_harpy", "cr_crocolisk", "cr_wolf_frost", "cr_yeti", "cr_rootling", "cr_blight_hound",
            "cr_spider_giant", "cr_rootwarden", "cr_drake_whelp", "cr_frost_drake",
            "cr_r1_thornmaw", "cr_r1_hollow_heart", "cr_r2_frostclaw", "cr_r2_cinder_drake", "cr_r2_vyrmathra",
        };

        static CreatureDef Def(string sprite, CreatureType type = CreatureType.Beast, string id = null, string family = "", float size = 1.8f,
                               CreatureRank rank = CreatureRank.Normal, string material = "", string voice = "")
            => new CreatureDef { id = id ?? "cr_test_" + sprite, sprite = sprite, type = type, family = family, size = size, rank = rank, material = material, voice = voice };

        static ItemDef Armor(string id, ArmorType t, EquipType slot) => new ItemDef { id = id, name = id, kind = ItemKind.Armor, equip = slot, armorType = t };

        static ItemDef Weapon(string id, WeaponType w, EquipType slot) =>
            new ItemDef { id = id, name = id, kind = ItemKind.Weapon, equip = slot, weaponType = w, minDamage = 2, maxDamage = 4, speed = 2f };

        static Unit Bare(ClassId cls)
        {
            var u = UnitFactory.CreateCharacter(Db, cls, "Tester", 10);
            u.Equipment.Clear();
            return u;
        }

        static void Wear(Unit u, ItemDef d, EquipSlot slot) => EquipmentRules.Equip(u, new ItemInstance(d), slot);

        // ------------------------------------------------------------------ manifest

        [Test]
        public static void ManifestHoldsEveryContractIdOnceAndKeepsTheOriginal24()
        {
            var all = CombatSounds.AllIds;
            Assert(all.Length == all.Distinct().Count(), "no duplicates in AllIds");
            Assert(CombatSounds.LegacyIds.Length == 24, "24 original ids");
            foreach (var id in CombatSounds.LegacyIds) Assert(CombatSounds.IsKnownId(id), "legacy id kept: " + id);
            foreach (var id in ContractIds) Assert(CombatSounds.IsKnownId(id), "contract id known: " + id);
            var extra = all.Except(ContractIds).Except(CombatSounds.LegacyIds).ToList();
            Assert(extra.Count == 0, "no ids beyond the contract and the original set: " + string.Join(",", extra));
            Assert(!CombatSounds.IsKnownId("hit_banana") && !CombatSounds.IsKnownId(null), "unknown ids are unknown");
            Assert(CombatSounds.Materials.SequenceEqual(new[] { "plate", "mail", "leather", "cloth", "flesh", "fur", "chitin", "bone", "wood", "stone", "ether", "ice", "scale", "wet" }),
                "material set is the contract's (Expansion.md §2.4)");
            Assert(CombatSounds.Voices.SequenceEqual(new[] { "beast", "humanoid", "spirit", "wood", "stone", "dragon", "frog", "gnoll", "none" }),
                "voice set is the contract's (Expansion.md §2.4)");
            foreach (var m in CombatSounds.Materials) Assert(CombatSounds.IsKnownId(CombatSounds.MaterialLayer(m)), "material layer " + m);
            Assert(CombatSounds.MaterialLayer("velvet") == "mat_flesh" && CombatSounds.MaterialLayer(null) == "mat_flesh", "unknown material → flesh layer");
        }

        // ------------------------------------------------------------------ weapons

        [Test]
        public static void EveryWeaponTypeHasALayerASwingAndRangedTypesARelease()
        {
            foreach (WeaponType w in Enum.GetValues(typeof(WeaponType)))
            {
                Assert(CombatSounds.WeaponLayers.Contains(CombatSounds.WeaponLayerOf(w)), "weapon layer for " + w);
                Assert(new[] { "swing_light", "swing", "swing_heavy" }.Contains(CombatSounds.SwingOf(w, false)), "swing for " + w);
            }
            Assert(CombatSounds.WeaponLayerOf(WeaponType.Dagger) == "hit_dagger" && CombatSounds.SwingOf(WeaponType.Dagger, false) == "swing_light", "dagger: stab, light");
            Assert(CombatSounds.WeaponLayerOf(WeaponType.FistWeapon) == "hit_fist", "fist");
            Assert(CombatSounds.WeaponLayerOf(WeaponType.OneHandSword) == "hit_blade" && CombatSounds.SwingOf(WeaponType.OneHandSword, false) == "swing", "1H sword");
            Assert(CombatSounds.WeaponLayerOf(WeaponType.TwoHandSword) == "hit_blade" && CombatSounds.SwingOf(WeaponType.TwoHandSword, true) == "swing_heavy", "2H sword: heavy");
            Assert(CombatSounds.WeaponLayerOf(WeaponType.OneHandAxe) == "hit_axe" && CombatSounds.WeaponLayerOf(WeaponType.TwoHandAxe) == "hit_axe", "axes");
            Assert(CombatSounds.WeaponLayerOf(WeaponType.OneHandMace) == "hit_blunt" && CombatSounds.WeaponLayerOf(WeaponType.Staff) == "hit_blunt", "blunt");
            Assert(CombatSounds.SwingOf(WeaponType.Staff, false) == "swing_heavy" && CombatSounds.SwingOf(WeaponType.Polearm, false) == "swing_heavy", "staff/polearm heavy");
            Assert(CombatSounds.WeaponLayerOf(WeaponType.None) == "hit_fist" && CombatSounds.SwingOf(WeaponType.None, false) == "swing_light", "bare hands");
            Assert(CombatSounds.WeaponLayerOf(WeaponType.Bow) == "hit_arrow" && CombatSounds.WeaponLayerOf(WeaponType.Crossbow) == "hit_bolt" &&
                   CombatSounds.WeaponLayerOf(WeaponType.Gun) == "hit_bullet", "projectiles thunk differently");
            Assert(CombatSounds.ReleaseOf(WeaponType.Bow) == "bow" && CombatSounds.ReleaseOf(WeaponType.Crossbow) == "xbow_release" &&
                   CombatSounds.ReleaseOf(WeaponType.Gun) == "gun_fire" && CombatSounds.ReleaseOf(WeaponType.Thrown) == "throw_release" &&
                   CombatSounds.ReleaseOf(WeaponType.Wand) == "wand_zap", "guns, crossbows, throws and wands no longer twang");
            Assert(CombatSounds.ReleaseOf(WeaponType.OneHandSword) == null, "melee weapons have no release");
            Assert(CombatSounds.HasFlightWhoosh(WeaponType.Bow) && !CombatSounds.HasFlightWhoosh(WeaponType.Gun) && !CombatSounds.HasFlightWhoosh(WeaponType.Wand), "flight whoosh");
            foreach (var it in Db.Items.Values.Where(d => d.kind == ItemKind.Weapon))
                Assert(CombatSounds.IsKnownId(CombatSounds.WeaponLayerOf(it.weaponType)), "data weapon " + it.id);
        }

        [Test]
        public static void CharactersSoundLikeTheirWeaponAndArmour()
        {
            var w = Bare(ClassId.Warrior);
            Assert(CombatSounds.MaterialOf(w) == "cloth" && CombatSounds.ArmorMaterialOf(w) == "cloth", "nothing worn: cloth");
            Assert(CombatSounds.SwingOf(w, false) == "swing_light" && CombatSounds.AttackLayerOf(w, false, false) == "hit_fist", "bare hands");
            Wear(w, Armor("t_legs_mail", ArmorType.Mail, EquipType.Legs), EquipSlot.Legs);
            Assert(CombatSounds.MaterialOf(w) == "mail", "no chest: the heaviest piece worn");
            Wear(w, Armor("t_chest_leather", ArmorType.Leather, EquipType.Chest), EquipSlot.Chest);
            Assert(CombatSounds.MaterialOf(w) == "leather", "the chest piece wins");
            Wear(w, Armor("t_chest_plate", ArmorType.Plate, EquipType.Chest), EquipSlot.Chest);
            Assert(CombatSounds.MaterialOf(w) == "plate" && CombatSounds.ClattersOf(w), "plate clatters");
            Assert(CombatSounds.VoiceOf(w) == "humanoid", "characters are humanoid");
            Wear(w, Weapon("t_greatsword", WeaponType.TwoHandSword, EquipType.TwoHand), EquipSlot.MainHand);
            Assert(CombatSounds.SwingOf(w, false) == "swing_heavy" && CombatSounds.AttackLayerOf(w, false, false) == "hit_blade", "two-hander: heavy blade");
            Wear(w, Weapon("t_mace", WeaponType.OneHandMace, EquipType.MainHand), EquipSlot.MainHand);
            Wear(w, Weapon("t_dagger", WeaponType.Dagger, EquipType.OneHand), EquipSlot.OffHand);
            Assert(CombatSounds.AttackLayerOf(w, false, false) == "hit_blunt" && CombatSounds.AttackLayerOf(w, true, false) == "hit_dagger", "main and off hand separately");
            Assert(CombatSounds.SwingOf(w, true) == "swing_light" && CombatSounds.SwingOf(w, false) == "swing", "off-hand dagger swings light");
            Assert(CombatSounds.BodyFallOf(w) == "body_fall_light", "a character falls light (and clatters)");
            Assert(CombatSounds.SizePitchOf(w) == 1f, "characters keep their pitch");

            var h = Bare(ClassId.Hunter);
            Wear(h, Weapon("t_gun", WeaponType.Gun, EquipType.Ranged), EquipSlot.Ranged);
            Assert(CombatSounds.ReleaseOf(h, School.Physical) == "gun_fire" && CombatSounds.AttackLayerOf(h, false, true) == "hit_bullet", "white shot with a gun pops");
            Assert(!CombatSounds.FlightWhooshOf(h, School.Physical), "bullets do not whoosh");
            Wear(h, Weapon("t_bow", WeaponType.Bow, EquipType.Ranged), EquipSlot.Ranged);
            var arcaneShot = Db.Ability("hunter_arcane_shot");
            Assert(CombatSounds.ReleaseOf(h, School.Arcane, arcaneShot) == "bow", "Arcane Shot still twangs the bow");
            Assert(CombatSounds.FlightWhooshOf(h, School.Arcane, arcaneShot), "and the arrow whooshes");
            Assert(CombatSounds.ReleaseOf(h, School.Fire, Db.Ability("mage_fireball")) == "wand_zap", "a character's ranged spell zaps");
            Assert(CombatSounds.ReleaseOf(h, School.Physical, Db.Ability("warrior_throw")) == "throw_release", "Thrown abilities throw");
            Wear(h, Weapon("t_xbow", WeaponType.Crossbow, EquipType.Ranged), EquipSlot.Ranged);
            Assert(CombatSounds.ReleaseOf(h, School.Physical) == "xbow_release" && CombatSounds.AttackLayerOf(h, false, true) == "hit_bolt", "crossbow");
            var m = Bare(ClassId.Mage);
            Wear(m, Weapon("t_wand", WeaponType.Wand, EquipType.Ranged), EquipSlot.Ranged);
            Assert(CombatSounds.ReleaseOf(m, School.Arcane, new AbilityDef { id = "t_shoot", special = "Shoot" }) == "wand_zap", "wand Shoot zaps");
            Assert(!CombatSounds.FlightWhooshOf(m, School.Arcane, new AbilityDef { id = "t_shoot", special = "Shoot" }), "wand bolts do not whoosh");
        }

        [Test]
        public static void ShieldBlocksThunkOnWoodAndRingOnMetal()
        {
            var p = Bare(ClassId.Paladin);
            Assert(CombatSounds.BlockOf(p) == "block_wood", "no shield, cloth: wood");
            foreach (var id in new[] { "wooden_buckler", "round_oak_shield", "oaken_heater", "sun_painted_kite_shield", "heartwood_bulwark" })
            {
                var d = Db.Item(id);
                if (d == null) continue;
                Assert(!CombatSounds.IsMetalShield(d), id + " is wooden");
                Wear(p, d, EquipSlot.OffHand);
                Assert(CombatSounds.BlockOf(p) == "block_wood", id + " blocks with a wooden thunk");
            }
            var iron = new ItemDef { id = "t_iron_tower_shield", name = "Iron Tower Shield", kind = ItemKind.Weapon, equip = EquipType.OffHand, weaponType = WeaponType.Shield };
            Assert(CombatSounds.IsMetalShield(iron), "iron is metal");
            Wear(p, iron, EquipSlot.OffHand);
            Assert(CombatSounds.BlockOf(p) == "block_metal", "a metal shield rings");
            Assert(!CombatSounds.IsMetalShield(new ItemDef { id = "t_oak_iron_rim", name = "Oak Shield with Iron Rim" }), "a wooden shield with a rim stays wood");
            Assert(CombatSounds.BlockOf(UnitFactory.CreateCreature(Db, Def("cr_hollow_knight", CreatureType.Undead))) == "block_metal", "a plated creature blocks metal");
            Assert(CombatSounds.BlockOf(UnitFactory.CreateCreature(Db, Def("cr_wolf"))) == "block_wood", "a furry one blocks soft");
        }

        // ------------------------------------------------------------------ creatures

        [Test]
        public static void EveryExpansionArtKeyHasAValidProfile()
        {
            foreach (var key in ModelKeys)
            {
                var p = CombatSounds.ProfileOf(Def(key));
                Assert(CombatSounds.IsMaterial(p.Material), key + " material " + p.Material);
                Assert(CombatSounds.IsVoice(p.Voice), key + " voice " + p.Voice);
                Assert(CombatSounds.WeaponLayers.Contains(p.Attack), key + " attack " + p.Attack);
                Assert(p.Source == "sprite", key + " is in the art-key table (got " + p.Source + ")");
            }
        }

        [Test]
        public static void ExpectedMaterialsAndVoicesForTheNewCreatures()
        {
            void Expect(string key, string mat, string voice, string attack = null)
            {
                var p = CombatSounds.ProfileOf(Def(key));
                Assert(p.Material == mat, $"{key}: material {p.Material}, expected {mat}");
                Assert(p.Voice == voice, $"{key}: voice {p.Voice}, expected {voice}");
                if (attack != null) Assert(p.Attack == attack, $"{key}: attack {p.Attack}, expected {attack}");
            }
            Expect("cr_gnoll", "fur", "gnoll", "hit_axe");
            Expect("cr_gnoll_chief", "fur", "gnoll");
            Expect("cr_mireling", "wet", "frog");
            Expect("cr_mireling_oracle", "wet", "frog", "hit_blunt");
            Expect("cr_skeleton", "bone", "stone", "hit_blade");
            Expect("cr_skeleton_archer", "bone", "stone");
            Expect("cr_ogre", "flesh", "humanoid", "hit_blunt");
            Expect("cr_yeti", "fur", "beast", "hit_claw");
            Expect("cr_r2_frostclaw", "fur", "beast");
            Expect("cr_drake_whelp", "scale", "dragon", "hit_bite");
            Expect("cr_frost_drake", "scale", "dragon");
            Expect("cr_r2_vyrmathra", "scale", "dragon");
            Expect("cr_ice_elemental", "ice", "stone", "hit_slam");
            Expect("cr_rimeheart", "ice", "stone");
            Expect("cr_ash_elemental", "stone", "stone");
            Expect("cr_fen_wisp", "ether", "spirit");
            Expect("cr_hollow_wisp", "ether", "spirit");
            Expect("cr_hollow_treant", "wood", "wood", "hit_slam");
            Expect("cr_rootwarden", "wood", "wood", "hit_slam");
            Expect("cr_crocolisk", "scale", "beast", "hit_bite");
            Expect("cr_spider_giant", "chitin", "beast", "hit_bite");
            Expect("cr_hollow_knight", "plate", "spirit", "hit_blade");
            Expect("cr_dragonsworn", "mail", "humanoid", "hit_blade");
            Expect("cr_wolf", "fur", "beast", "hit_bite");
            Expect("cr_bandit", "leather", "humanoid", "hit_dagger");
            Expect("cr_training_dummy", "wood", "none");
            Expect("demon_infernal", "stone", "stone", "hit_slam");
        }

        [Test]
        public static void TintSuffixesAndIdsResolveToTheirBaseKey()
        {
            var a = CombatSounds.ProfileOf(Def("cr_r2_cinder_drake_emberjaw"));
            Assert(a.Material == "scale" && a.Voice == "dragon" && a.Source == "sprite", "tinted drake: " + a);
            var b = CombatSounds.ProfileOf(Def("cr_ogre_mage_elite"));
            Assert(b.Material == "flesh" && b.Source == "sprite", "suffix on a two-part key: " + b);
            var c = CombatSounds.ProfileOf(Def("cr_unknown_art", CreatureType.Humanoid, id: "cr_dg1_rootwarden"));
            Assert(c.Material == "wood" && c.Source == "word", "id words when the sprite is unknown: " + c);
            var d = CombatSounds.ProfileOf(Def("cr_mf_mystery", CreatureType.Beast, id: "cr_mf_gnoll_raider"));
            Assert(d.Voice == "gnoll" && d.Source == "word", "a gnoll by its id: " + d);
            var e = CombatSounds.ProfileOf(Def("cr_mf_giant_spider", CreatureType.Beast));
            Assert(e.Material == "chitin", "a giant spider is a spider, not an ogre: " + e);
            var f = CombatSounds.ProfileOf(Def("cr_sr_frost_wolf_alpha", CreatureType.Beast));
            Assert(f.Material == "fur", "a frost wolf is a wolf, not ice: " + f);
            var g = CombatSounds.ProfileOf(Def("cr_zz", CreatureType.Beast, id: "cr_zz", family: "Crocolisk"));
            Assert(g.Material == "scale" && g.Source == "family", "family fallback: " + g);
            var h = CombatSounds.ProfileOf(Def("cr_zz2", CreatureType.Undead, id: "cr_zz2"));
            Assert(h.Material == "bone" && h.Voice == "spirit" && h.Source == "type", "type fallback: " + h);
            foreach (CreatureType t in Enum.GetValues(typeof(CreatureType)))
            {
                var p = CombatSounds.ProfileOf(Def("cr_qq_" + t, t, id: "cr_qq_" + t));
                Assert(CombatSounds.IsMaterial(p.Material) && CombatSounds.IsVoice(p.Voice) && CombatSounds.WeaponLayers.Contains(p.Attack), "type fallback valid for " + t);
            }
        }

        [Test]
        public static void AuthoredMaterialAndVoiceWinOverInference()
        {
            var d = Def("cr_wolf", material: "plate", voice: "stone");
            var p = CombatSounds.ProfileOf(d);
            Assert(p.Material == "plate" && p.Voice == "stone" && p.Source == "data" && p.Attack == "hit_bite", "data wins, attack still inferred: " + p);
            var bad = Def("cr_wolf", material: "velvet", voice: "kazoo");
            var q = CombatSounds.ProfileOf(bad);
            Assert(q.Material == "fur" && q.Voice == "beast" && q.Source == "sprite", "unknown authored values fall back to inference: " + q);
            // a def edited in place is honoured (the cache holds only the inference)
            d.material = "";
            Assert(CombatSounds.ProfileOf(d).Material == "fur", "clearing the authored material returns to inference");
            d.voice = "none";
            Assert(CombatSounds.ProfileOf(d).Voice == "none" && CombatSounds.VocalOf("none") == null, "voice none is silent");
            var only = Def("cr_wolf", voice: "dragon");
            var r = CombatSounds.ProfileOf(only);
            Assert(r.Voice == "dragon" && r.Material == "fur", "voice alone authored: " + r);
        }

        [Test]
        public static void EveryCreatureInDataMapsToKnownSounds()
        {
            int n = 0;
            foreach (var c in Db.Creatures.Values)
            {
                n++;
                var p = CombatSounds.ProfileOf(c);
                Assert(CombatSounds.IsMaterial(p.Material) && CombatSounds.IsVoice(p.Voice), c.id + " profile " + p);
                Assert(CombatSounds.IsKnownId(p.Attack), c.id + " attack " + p.Attack);
                Assert(CombatSounds.IsKnownId(CombatSounds.MaterialLayer(p.Material)), c.id + " material layer");
                var vocal = CombatSounds.VocalOf(p.Voice);
                Assert(vocal == null ? p.Voice == "none" : CombatSounds.IsKnownId(vocal), c.id + " vocal " + vocal);
                if (!string.IsNullOrEmpty(c.material)) Assert(p.Material == c.material, c.id + " authored material honoured");
                if (!string.IsNullOrEmpty(c.voice)) Assert(p.Voice == c.voice, c.id + " authored voice honoured");
                var u = UnitFactory.CreateCreature(Db, c, Math.Max(1, c.levelMin));
                foreach (var id in new[] { CombatSounds.SwingOf(u, false), CombatSounds.AttackLayerOf(u, false, false), CombatSounds.AttackLayerOf(u, false, true),
                                           CombatSounds.ReleaseOf(u, School.Physical), CombatSounds.ReleaseOf(u, c.meleeSchool), CombatSounds.BlockOf(u) })
                    Assert(CombatSounds.IsKnownId(id), c.id + " → " + id);
                var fall = CombatSounds.BodyFallOf(u);
                Assert(fall == null || fall == "body_fall_light" || fall == "body_fall_heavy", c.id + " fall " + fall);
            }
            Assert(n >= 50, "the data has its creatures (" + n + ")");
        }

        [Test]
        public static void CreatureUnitsSwingReleaseAndFallTheirWay()
        {
            var wolf = RulesTestUtil.Mob("cr_wolf", 5);
            Assert(CombatSounds.MaterialOf(wolf) == "fur" && CombatSounds.VoiceOf(wolf) == "beast" && CombatSounds.AttackLayerOf(wolf, false, false) == "hit_bite", "wolf");
            Assert(CombatSounds.SwingOf(wolf, false) == "swing_light" && CombatSounds.BodyFallOf(wolf) == "body_fall_light" && !CombatSounds.ClattersOf(wolf), "a small beast");
            var archer = RulesTestUtil.Mob("cr_bandit_archer", 5);
            Assert(CombatSounds.ReleaseOf(archer, School.Physical) == "bow" && CombatSounds.AttackLayerOf(archer, false, true) == "hit_arrow", "the archer twangs and the arrow thunks");
            Assert(CombatSounds.FlightWhooshOf(archer, School.Physical), "arrows whoosh");
            foreach (var id in new[] { "cr_bandit_hexer", "cr_hollow_wisp", "cr_hollow_keeper", "cr_mossling_shaman", "cr_mossling_chief", "cr_hollow_wisp_minion" })
            {
                var caster = RulesTestUtil.Mob(id, 5);
                Assert(CombatSounds.ReleaseOf(caster, School.Physical) == "wand_zap", id + ": a missed magic bolt still zaps (never a bow twang)");
                Assert(CombatSounds.ReleaseOf(caster, caster.Creature.meleeSchool) == "wand_zap", id + ": magic bolt zaps");
                Assert(!CombatSounds.FlightWhooshOf(caster, caster.Creature.meleeSchool), id + ": no arrow whoosh");
            }
            var wisp = RulesTestUtil.Mob("cr_hollow_wisp", 5);
            Assert(CombatSounds.BodyFallOf(wisp) == null && CombatSounds.VocalOf(CombatSounds.VoiceOf(wisp)) == "vo_spirit_fade", "spirits fade instead of falling");
            var treant = RulesTestUtil.Mob("cr_hollow_treant", 8);
            Assert(CombatSounds.BodyFallOf(treant) == "body_fall_heavy" && CombatSounds.SwingOf(treant, false) == "swing_heavy", "a treant slams and falls heavy");
            Assert(CombatSounds.SizePitchOf(treant) < 1f, "an elite treant sounds deeper");
            var boss = RulesTestUtil.Mob("cr_hollow_warden", 12);
            Assert(CombatSounds.SizePitchOf(boss) == 0.9f, "the boss is lowest");
            var totemDef = Db.Creatures.Values.FirstOrDefault(c => c.type == CreatureType.Totem);
            if (totemDef != null)
                Assert(CombatSounds.BodyFallOf(UnitFactory.CreateCreature(Db, totemDef, 5)) == null, "totems do not fall");
            var knight = UnitFactory.CreateCreature(Db, Def("cr_hollow_knight", CreatureType.Undead), 5);
            Assert(CombatSounds.ClattersOf(knight) && CombatSounds.BodyFallOf(knight) == "body_fall_light", "a plated knight clatters as it falls");
            var dragon = UnitFactory.CreateCreature(Db, Def("cr_r2_vyrmathra", CreatureType.Dragonkin, size: 6f, rank: CreatureRank.Boss), 30);
            Assert(CombatSounds.BodyFallOf(dragon) == "body_fall_heavy" && CombatSounds.VocalOf(CombatSounds.VoiceOf(dragon)) == "vo_dragon_roar", "the dragon roars and lands heavy");
            Assert(CombatSounds.BodyFallOf(null) == null && CombatSounds.MaterialOf(null) == "flesh" && CombatSounds.VoiceOf(null) == "none", "null units are safe");
        }

        [Test]
        public static void DeathVocalsAndFallsFollowTheBody()
        {
            Unit M(string id) => RulesTestUtil.Mob(id, 20);
            // dragons: whelps squeal high and soft, drakes and Vyrmathra boom; only the big ones land heavy
            var whelp = M("cr_r2_ashborn_whelp");
            var drake = M("cr_r2_emberjaw");
            var vyr = M("cr_r2_vyrmathra");
            foreach (var d in new[] { whelp, drake, vyr }) Assert(CombatSounds.VocalOf(d) == "vo_dragon_roar", d.Creature.id + " roars");
            AssertNear(CombatSounds.VocalPitchOf(whelp), 1.8f, 0.01f, "a 1.1 m whelp roars at 1.8 (a squeal, not Vyrmathra's boom)");
            AssertNear(CombatSounds.VocalPitchOf(drake), 1f, 0.01f, "a 4.2 m drake roars at 1");
            AssertNear(CombatSounds.VocalPitchOf(vyr), 0.85f, 0.01f, "Vyrmathra is the deepest");
            Assert(CombatSounds.VocalVolumeOf(whelp, false) < CombatSounds.VocalVolumeOf(vyr, false), "whelps are softer");
            foreach (var id in new[] { "cr_r2_ashborn_whelp", "cr_r2_drake_whelp", "cr_sr_drake_whelp", "cr_sr_drake_whelp_minion" })
                Assert(CombatSounds.BodyFallOf(M(id)) == "body_fall_light", id + ": a whelp lands light");
            Assert(CombatSounds.BodyFallOf(drake) == "body_fall_heavy" && CombatSounds.BodyFallOf(vyr) == "body_fall_heavy", "drakes land heavy");

            // big beasts roar (not a 650 Hz yelp); small ones still yelp
            var yeti = M("cr_sr_yeti");
            var frostclaw = M("cr_r2_frostclaw");
            Assert(CombatSounds.VocalOf(yeti) == "vo_dragon_roar" && CombatSounds.VocalOf(frostclaw) == "vo_dragon_roar", "yetis and Frostclaw roar");
            AssertNear(CombatSounds.VocalPitchOf(yeti), 1.6f, 0.01f, "a 2.8 m yeti roars high");
            AssertNear(CombatSounds.VocalPitchOf(frostclaw), 1.34f, 0.01f, "5 m Frostclaw roars lower");
            var wolf = M("cr_wolf");
            Assert(CombatSounds.VocalOf(wolf) == "vo_beast_yelp" && CombatSounds.VocalPitchOf(wolf) == CombatSounds.SizePitchOf(wolf), "a wolf still yelps");

            // spiders and crocolisks do not yelp
            foreach (var id in new[] { "cr_spider", "cr_dg2_silkwidow", "cr_dg2_spiderling", "cr_mf_crocolisk", "cr_dg5_vault_snapjaw" })
            {
                var u = M(id);
                Assert(CombatSounds.VocalOf(u) == null, id + ": no yelp");
                Assert(CombatSounds.BodyFallOf(u) == "body_fall_light", id + ": still lands");
            }

            // the barrow's dart trap: no body to drop, a spring click-thump instead of a thrown rock
            var trap = M("cr_dg4_dart_trap");
            Assert(CombatSounds.BodyFallOf(trap) == null && CombatSounds.VocalOf(trap) == null, "a dart trap vanishes silently");
            Assert(CombatSounds.ReleaseOf(trap, School.Physical, Db.Ability("cr_dg4_poison_dart")) == "xbow_release", "the trap shoots with a click-thump");
            Assert(CombatSounds.AttackLayerOf(trap, false, true) == "hit_bolt", "and the dart lands like a bolt");
            var dummy = M("cr_training_dummy");
            Assert(CombatSounds.BodyFallOf(dummy) == "body_fall_light", "the training dummy still topples");
            Assert(CombatSounds.BodyFallOf(M("cr_r2_ash_elemental")) == "body_fall_heavy", "a big stone elemental lands heavy");

            // skeletons rattle as they fall (they have no voice); a beast does not
            foreach (var id in new[] { "cr_dg3_skeleton", "cr_dg4_huscarl", "cr_sh2_restless_bones", "cr_dg3_drowned_dead" })
            {
                var u = M(id);
                Assert(CombatSounds.VocalOf(u) == null && CombatSounds.RattlesOf(u), id + ": bones rattle");
            }
            Assert(!CombatSounds.RattlesOf(wolf) && !CombatSounds.RattlesOf(null), "flesh does not rattle");

            // female bodies grunt higher (heroes, companions, witches); male ones keep the pitch
            var hunter = Bare(ClassId.Hunter);
            var warrior = Bare(ClassId.Warrior);
            Assert(CombatSounds.IsFemaleArt(hunter) && !CombatSounds.IsFemaleArt(warrior), "the Hunter hero has a female body, the Warrior a male one");
            AssertNear(CombatSounds.VocalPitchOf(hunter), 1.35f, 1e-4f, "a female hero grunts a fourth higher");
            AssertNear(CombatSounds.VocalPitchOf(warrior), 1f, 1e-4f, "a male hero keeps pitch 1");
            var lys = Bare(ClassId.Mage);
            lys.Sprite = "comp_lys";
            Assert(CombatSounds.VocalOf(lys) == "vo_humanoid_grunt" && CombatSounds.VocalPitchOf(lys) > 1.3f, "companion Lys grunts higher");
            var torvan = Bare(ClassId.Warrior);
            torvan.Sprite = "comp_torvan";
            AssertNear(CombatSounds.VocalPitchOf(torvan), 1f, 1e-4f, "Torvan does not");
            foreach (var id in new[] { "cr_mf_mire_hag", "cr_mf_auntie_gall", "cr_r1_mother_mire" })
            {
                var hag = M(id);
                Assert(CombatSounds.VocalPitchOf(hag) > CombatSounds.SizePitchOf(hag) * 1.3f && CombatSounds.VocalPitchOf(hag) <= 1.4f, id + " grunts higher (≤ 1.4)");
            }
            Assert(CombatSounds.VocalOf((Unit)null) == null && CombatSounds.VocalPitchOf(null) == 1f, "null units are safe");

            // every creature in data: a known (or no) vocal at a playable pitch
            foreach (var c in Db.Creatures.Values)
            {
                var u = UnitFactory.CreateCreature(Db, c, Math.Max(1, c.levelMin));
                var vocal = CombatSounds.VocalOf(u);
                Assert(vocal == null || CombatSounds.IsKnownId(vocal), c.id + " vocal " + vocal);
                float p = CombatSounds.VocalPitchOf(u);
                Assert(p >= 0.8f && p <= 1.8f, c.id + " vocal pitch " + p);
            }
        }

        // ------------------------------------------------------------------ outcomes, spells

        [Test]
        public static void DefenceOutcomesAllHaveASound()
        {
            var t = Bare(ClassId.Rogue);
            Assert(CombatSounds.AvoidOf(CombatEventType.Miss, t) == "miss", "miss");
            Assert(CombatSounds.AvoidOf(CombatEventType.Dodge, t) == "dodge" && CombatSounds.AvoidOf(CombatEventType.Evade, t) == "dodge", "dodge, evade");
            Assert(CombatSounds.AvoidOf(CombatEventType.Parry, t) == "parry", "parry");
            Assert(CombatSounds.AvoidOf(CombatEventType.Block, t) == "block_wood", "block");
            Assert(CombatSounds.AvoidOf(CombatEventType.Resist, t) == "resist" && CombatSounds.AvoidOf(CombatEventType.Immune, t) == "immune" &&
                   CombatSounds.AvoidOf(CombatEventType.Absorb, t) == "absorb", "resist, immune, absorb");
            foreach (CombatEventType k in Enum.GetValues(typeof(CombatEventType)))
            {
                var id = CombatSounds.AvoidOf(k, t);
                bool outcome = k == CombatEventType.Miss || k == CombatEventType.Dodge || k == CombatEventType.Parry || k == CombatEventType.Block ||
                               k == CombatEventType.Resist || k == CombatEventType.Immune || k == CombatEventType.Evade || k == CombatEventType.Absorb;
                Assert(outcome ? CombatSounds.IsKnownId(id) : id == null, "AvoidOf " + k);
            }
        }

        [Test]
        public static void SpellsWindUpAndLandBySchoolAndTag()
        {
            var bolt = Db.Ability("shaman_lightning_bolt");
            Assert(bolt != null && bolt.school == School.Nature, "Lightning Bolt is Nature");
            Assert(CombatSounds.CastWindupOf(bolt, bolt.school) == "cast_lightning" && CombatSounds.SpellImpactOf(bolt, bolt.school) == "impact_lightning",
                "lightning crackles instead of the nature harp");
            Assert(CombatSounds.SpellImpactOf(Db.Ability("shaman_chain_lightning"), School.Nature) == "impact_lightning", "Chain Lightning too");
            var fb = Db.Ability("mage_fireball");
            Assert(CombatSounds.CastWindupOf(fb, fb.school) == "cast_fire" && CombatSounds.SpellImpactOf(fb, fb.school) == "impact_fire", "fireball");
            Assert(CombatSounds.CastWindupOf(Db.Ability("mage_frostbolt"), School.Frost) == "cast_frost", "frostbolt");
            Assert(CombatSounds.CastWindupOf(Db.Ability("priest_lesser_heal"), School.Holy) == "cast_holy", "heals wind up holy");
            Assert(CombatSounds.CastWindupOf(null, School.Shadow) == "cast_shadow" && CombatSounds.CastWindupOf(null, School.Arcane) == "cast_arcane" &&
                   CombatSounds.CastWindupOf(null, School.Nature) == "cast_nature", "schools without an ability");
            Assert(CombatSounds.CastWindupOf(null, School.Physical) == "cast_start", "physical casts keep cast_start");
            Assert(CombatSounds.SpellImpactOf(null, School.Physical) == null, "physical hits have no spell impact");
            foreach (var id in new[] { "warrior_battle_shout", "warrior_demoralizing_shout", "warrior_challenging_shout", "warrior_piercing_howl" })
            {
                var a = Db.Ability(id);
                Assert(a != null && CombatSounds.IsShout(a) && CombatSounds.AreaBurstOf(a, a.school) == "shout_horn", id + " is a horn, not a swing");
            }
            Assert(CombatSounds.AreaBurstOf(Db.Ability("warrior_thunder_clap"), School.Physical) == "stomp", "Thunder Clap stomps");
            Assert(CombatSounds.AreaBurstOf(Db.Ability("cr_antler_stomp"), School.Physical) == "stomp", "Antler Stomp stomps");
            Assert(CombatSounds.AreaBurstOf(Db.Ability("warrior_whirlwind"), School.Physical) == null, "Whirlwind: the hits speak");
            Assert(CombatSounds.AreaBurstOf(Db.Ability("mage_blizzard"), School.Frost) == "impact_frost", "Blizzard");
            Assert(CombatSounds.TickOf(Db.Ability("warrior_rend"), School.Physical) == "mat_wet", "bleeds squelch softly");
            Assert(CombatSounds.TickOf(Db.Ability("warlock_corruption"), School.Shadow) == "impact_shadow", "dots fizz in their school");
            foreach (var a in Db.Abilities.Values)
            {
                foreach (var id in new[] { CombatSounds.CastWindupOf(a, a.school), CombatSounds.SpellImpactOf(a, a.school), CombatSounds.AreaBurstOf(a, a.school), CombatSounds.TickOf(a, a.school) })
                    Assert(id == null || CombatSounds.IsKnownId(id), a.id + " → " + id);
            }
        }

        // ------------------------------------------------------------------ world, loot, loudness

        [Test]
        public static void FootstepsFollowTheBiomeAndKeywords()
        {
            var expect = new Dictionary<string, string>
            {
                ["meadow"] = "footstep_grass", ["village"] = "footstep_dirt", ["highlands"] = "footstep_dirt", ["forest"] = "footstep_leaves",
                ["shrine"] = "footstep_stone", ["fen"] = "footstep_mud", ["peaks"] = "footstep_snow", ["cave"] = "footstep_stone",
                ["ice_cave"] = "footstep_snow", ["crypt"] = "footstep_stone", ["hollow_heart"] = "footstep_leaves", ["roost"] = "footstep_stone",
            };
            foreach (var kv in expect)
                Assert(CombatSounds.FootstepOf(new MapDef { id = "t", biome = kv.Key, ground = "ground_meadow" }) == kv.Value, "biome " + kv.Key);
            // maps without a biome: keywords in the ground / id / music
            Assert(CombatSounds.FootstepOf(new MapDef { id = "lanternvale", ground = "ground_village" }) == "footstep_dirt", "village ground");
            Assert(CombatSounds.FootstepOf(new MapDef { id = "whisperwood", ground = "ground_forest" }) == "footstep_leaves", "forest ground");
            Assert(CombatSounds.FootstepOf(new MapDef { id = "shrine", ground = "ground_shrine" }) == "footstep_stone", "shrine ground");
            Assert(CombatSounds.FootstepOf(new MapDef { id = "t", ground = "ground_snow" }) == "footstep_snow", "snow ground");
            Assert(CombatSounds.FootstepOf(new MapDef { id = "t", ground = "ground_meadow" }) == "footstep_grass", "meadow ground");
            Assert(CombatSounds.FootstepOf(null) == "footstep_grass", "no map");
            foreach (var m in Db.Maps.Values)
                Assert(CombatSounds.IsKnownId(CombatSounds.FootstepOf(m)), "map " + m.id + " → " + CombatSounds.FootstepOf(m));
            Db.Maps.TryGetValue("lanternvale", out var lv);
            if (lv != null) Assert(CombatSounds.FootstepOf(lv) == "footstep_dirt", "Lanternvale walks on dirt now (was always grass)");
            Db.Maps.TryGetValue("whisperwood", out var ww);
            if (ww != null) Assert(CombatSounds.FootstepOf(ww) == "footstep_leaves", "Whisperwood rustles");
            Assert(CombatSounds.IsMetal("mail") && CombatSounds.IsMetal("plate") && !CombatSounds.IsMetal("leather"), "jingle only for metal");
        }

        [Test]
        public static void LootFanfareByQuality()
        {
            Assert(CombatSounds.LootOf(Quality.Poor) == null && CombatSounds.LootOf(Quality.Common) == null && CombatSounds.LootOf(Quality.Uncommon) == null, "plain pickup");
            Assert(CombatSounds.LootOf(Quality.Rare) == "loot_rare" && CombatSounds.LootOf(Quality.Epic) == "loot_epic" && CombatSounds.LootOf(Quality.Legendary) == "loot_legendary", "fanfares");
        }

        [Test]
        public static void LoudnessScalesWithTheShareOfHealthTaken()
        {
            float prev = 0f;
            for (int i = 0; i <= 40; i++)
            {
                float v = CombatSounds.HitVolume(i * 10f, 1000f, false, false);
                Assert(v >= prev - 1e-6f, "monotonic in damage");
                Assert(v >= 0.65f - 1e-6f && v <= 1f + 1e-6f, "within 0.65..1 without crit or kill: " + v);
                prev = v;
            }
            AssertNear(CombatSounds.HitVolume(0f, 100f, false, false), 0.65f, 1e-5f, "a scratch is quietest");
            AssertNear(CombatSounds.HitVolume(25f, 100f, false, false), 1f, 1e-5f, "a quarter of the health is full volume");
            AssertNear(CombatSounds.HitVolume(500f, 100f, false, false), 1f, 1e-5f, "clamped");
            AssertNear(CombatSounds.HitVolume(25f, 100f, true, false), 1.15f, 1e-5f, "crit ×1.15");
            AssertNear(CombatSounds.HitVolume(25f, 100f, true, true), 1.15f * 1.1f, 1e-5f, "kill ×1.1");
            Assert(!float.IsNaN(CombatSounds.HitVolume(float.NaN, 0f, false, false)) && CombatSounds.HitVolume(-5f, -1f, false, false) == 0.65f, "garbage in, quiet out");
            AssertNear(CombatSounds.AreaLayerGain(1), 1f, 1e-6f, "first target full");
            AssertNear(CombatSounds.AreaLayerGain(2), (float)(1 / Math.Sqrt(2)), 1e-6f, "second 1/√2");
            AssertNear(CombatSounds.AreaLayerGain(3), (float)(1 / Math.Sqrt(3)), 1e-6f, "third 1/√3");
            Assert(CombatSounds.AreaLayerGain(4) == 0f && CombatSounds.AreaLayerGain(10) == 0f, "no material layer beyond three targets");
            Assert(CombatSounds.AreaLayerGain(0) == 1f, "index clamps to 1");
            Assert(CombatSounds.DeathFallDelay > CombatSounds.DownedFallDelay && CombatSounds.DeathFallDelay > 0.36f && CombatSounds.DeathFallDelay <= 0.72f,
                "the body lands inside the death animation's lie window (0.36–0.72 s)");
        }

        [Test]
        public static void SkeletonRattleKeepsBothKnocksAtEveryPresentationSpeed()
        {
            // CombatSfx.Death queues two mat_bone knocks on SCALED time (Sfx.PlayAfter → SfxDelayQueue on Time.time); the
            // player pops due items once per frame and SfxVoiceRules rate-limits per base id on UNSCALED time. Speeds
            // are AnimationSpeed (≤ 3) × fast forward (2.5), capped at 8.
            foreach (float fps in new[] { 30f, 60f, 144f })
                foreach (float speed in new[] { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 3.75f, 5f, 6f, 7.5f, 8f })
                {
                    var rnd = new Random(7);
                    for (int t = 0; t < 40; t++)
                    {
                        var q = new Lanternvale.Util.SfxDelayQueue<int>();
                        var rules = new Lanternvale.Util.SfxVoiceRules();
                        float dt = 1f / fps, phase = (float)rnd.NextDouble() * dt;
                        float scaled = phase * speed, unscaled = phase;
                        q.Add(scaled, CombatSounds.DeathFallDelay, 1);
                        q.Add(scaled, CombatSounds.DeathFallDelay + CombatSounds.BoneRattleScaledGap(speed), 2);
                        int started = 0;
                        var due = new List<int>();
                        for (int f = 1; f < 2000 && q.Count > 0; f++)
                        {
                            unscaled += dt; scaled += dt * speed;
                            due.Clear();
                            q.PopDue(scaled, due);
                            foreach (var d in due) if (rules.TryStart("mat_bone", unscaled, f)) started++;
                        }
                        Assert(started == 2, $"both rattle knocks heard at {speed}x, {fps} fps (trial {t}): {started}");
                    }
                }
            AssertNear(CombatSounds.BoneRattleScaledGap(1f), CombatSounds.BoneRattleGap, 1e-6f, "1x: the plain gap");
            AssertNear(CombatSounds.BoneRattleScaledGap(0f), CombatSounds.BoneRattleGap, 1e-6f, "paused: the plain gap");
            AssertNear(CombatSounds.BoneRattleScaledGap(float.NaN), CombatSounds.BoneRattleGap, 1e-6f, "garbage: the plain gap");
            AssertNear(CombatSounds.BoneRattleScaledGap(8f), CombatSounds.BoneRattleGap * 8f, 1e-5f, "8x: the same real gap");
        }

        // ------------------------------------------------------------------ the Game code asks only for known ids

        static readonly HashSet<string> LegacyAliases = new HashSet<string> { "click", "open", "close", "hit", "impact_physical", "crit", "footstep", "gold", "loot", "levelup", "chest", "cast" };

        [Test]
        public static void EveryLiteralIdInTheGameCodeIsKnown()
        {
            var scripts = Path.GetFullPath(Path.Combine(DataDir, "..", "..", "Scripts", "Game"));
            if (!Directory.Exists(scripts)) { Console.WriteLine("    (Game scripts not found at " + scripts + "; skipped)"); return; }
            var rx = new Regex(@"(?:Sfx\.Play(?:After)?|Ui\.Sfx\?\.Invoke)\(\s*""([^""]+)""", RegexOptions.Compiled);
            var rxTernary = new Regex(@"Sfx\.Play(?:After)?\(\s*[^;""]*\?\s*""([^""]+)""\s*:\s*""([^""]+)""", RegexOptions.Compiled);
            int found = 0;
            var unknown = new List<string>();
            foreach (var f in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(f);
                foreach (Match m in rx.Matches(text)) Check(m.Groups[1].Value, f);
                foreach (Match m in rxTernary.Matches(text)) { Check(m.Groups[1].Value, f); Check(m.Groups[2].Value, f); }
            }
            void Check(string id, string file)
            {
                found++;
                if (!CombatSounds.IsKnownId(id) && !LegacyAliases.Contains(id)) unknown.Add(Path.GetFileName(file) + ": " + id);
            }
            Assert(found >= 40, "the scan found the call sites (" + found + ")");
            Assert(unknown.Count == 0, "unknown sfx ids in Game code: " + string.Join(", ", unknown));
        }
    }
}
