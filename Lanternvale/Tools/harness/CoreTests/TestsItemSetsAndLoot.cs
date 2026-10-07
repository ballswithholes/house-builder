// Item sets, set bonuses, legendary procs and party-aware loot (Docs/Expansion.md §5; brief_items §8 a-h): bonuses at 2/3/4
// distinct pieces through stats, AbilityMods, Specials and procs (with their own cooldown key), surviving a save; set
// progress for tooltips; loot pools (pick one, weights, skipOwned, perMembers, partyUsable) with RNG use identical to
// today for tables without the new keys; no duplicate pool drops across one battle's bosses; the session's loot context
// at battle creation and in chests; starting gear never hands out pool or set items; and the authored stat-budget
// guardrail for content items of item level 12+.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsItemSetsAndLoot
    {
        /// <summary>Test items, sets, a legendary and loot tables, loaded on top of the real data (ids xs_* / set_xs_* / lt_xs_*).</summary>
        const string Bundle = @"{
  ""items"": [
    {""id"": ""xs_helm"", ""name"": ""Testguard Helm"", ""icon"": ""helm"", ""kind"": ""Armor"", ""quality"": ""Epic"", ""itemLevel"": 26, ""requiredLevel"": 20,
     ""equip"": ""Head"", ""armorType"": ""Mail"", ""armor"": 300, ""stats"": [{""stat"": ""Stamina"", ""value"": 10}, {""stat"": ""Strength"", ""value"": 4}], ""classes"": [""Warrior""], ""price"": 1000},
    {""id"": ""xs_chest"", ""name"": ""Testguard Hauberk"", ""icon"": ""chest"", ""kind"": ""Armor"", ""quality"": ""Epic"", ""itemLevel"": 26, ""requiredLevel"": 20,
     ""equip"": ""Chest"", ""armorType"": ""Mail"", ""armor"": 400, ""stats"": [{""stat"": ""Stamina"", ""value"": 10}, {""stat"": ""Strength"", ""value"": 4}], ""classes"": [""Warrior""], ""price"": 1000},
    {""id"": ""xs_legs"", ""name"": ""Testguard Legplates"", ""icon"": ""legs"", ""kind"": ""Armor"", ""quality"": ""Epic"", ""itemLevel"": 26, ""requiredLevel"": 20,
     ""equip"": ""Legs"", ""armorType"": ""Mail"", ""armor"": 350, ""stats"": [{""stat"": ""Stamina"", ""value"": 10}], ""classes"": [""Warrior""], ""price"": 1000},
    {""id"": ""xs_hands"", ""name"": ""Testguard Gauntlets"", ""icon"": ""gloves"", ""kind"": ""Armor"", ""quality"": ""Epic"", ""itemLevel"": 26, ""requiredLevel"": 20,
     ""equip"": ""Hands"", ""armorType"": ""Mail"", ""armor"": 200, ""stats"": [{""stat"": ""Stamina"", ""value"": 8}], ""classes"": [""Warrior""], ""price"": 1000},
    {""id"": ""xs_ring"", ""name"": ""Testguard Signet"", ""icon"": ""ring"", ""kind"": ""Accessory"", ""quality"": ""Epic"", ""itemLevel"": 26, ""requiredLevel"": 20,
     ""equip"": ""Finger"", ""stats"": [{""stat"": ""Stamina"", ""value"": 6}], ""price"": 1000},
    {""id"": ""xs_r_hood"", ""name"": ""Testshade Hood"", ""icon"": ""helm"", ""kind"": ""Armor"", ""quality"": ""Rare"", ""itemLevel"": 24, ""requiredLevel"": 18,
     ""equip"": ""Head"", ""armorType"": ""Leather"", ""armor"": 90, ""stats"": [{""stat"": ""Agility"", ""value"": 12}], ""classes"": [""Rogue""], ""price"": 800},
    {""id"": ""xs_r_vest"", ""name"": ""Testshade Vest"", ""icon"": ""chest"", ""kind"": ""Armor"", ""quality"": ""Rare"", ""itemLevel"": 24, ""requiredLevel"": 18,
     ""equip"": ""Chest"", ""armorType"": ""Leather"", ""armor"": 110, ""stats"": [{""stat"": ""Agility"", ""value"": 12}], ""classes"": [""Rogue""], ""price"": 800},
    {""id"": ""lg_xs_stormbrand"", ""name"": ""Stormbrand"", ""icon"": ""sword"", ""kind"": ""Weapon"", ""quality"": ""Legendary"", ""itemLevel"": 30, ""requiredLevel"": 20,
     ""equip"": ""TwoHand"", ""weaponType"": ""TwoHandSword"", ""minDamage"": 60, ""maxDamage"": 90, ""speed"": 3.4,
     ""stats"": [{""stat"": ""Strength"", ""value"": 20}, {""stat"": ""Stamina"", ""value"": 15}],
     ""equipEffects"": [{""type"": ""Proc"", ""description"": ""Chance on hit: lightning arcs to 3 foes."",
       ""proc"": {""trigger"": ""OnMeleeHit"", ""chance"": 100, ""effects"": [{""type"": ""Damage"", ""min"": 30, ""max"": 30, ""school"": ""Nature"", ""chainTargets"": 2}]}}],
     ""price"": 50000, ""unique"": true},
    {""id"": ""xs_pool_a"", ""name"": ""Pool Mail Gloves"", ""icon"": ""gloves"", ""kind"": ""Armor"", ""quality"": ""Epic"", ""itemLevel"": 26, ""requiredLevel"": 20,
     ""equip"": ""Hands"", ""armorType"": ""Mail"", ""armor"": 200, ""stats"": [{""stat"": ""Strength"", ""value"": 12}], ""classes"": [""Warrior""], ""price"": 900},
    {""id"": ""xs_pool_b"", ""name"": ""Pool Cloth Gloves"", ""icon"": ""gloves"", ""kind"": ""Armor"", ""quality"": ""Epic"", ""itemLevel"": 26, ""requiredLevel"": 20,
     ""equip"": ""Hands"", ""armorType"": ""Cloth"", ""armor"": 40, ""stats"": [{""stat"": ""Intellect"", ""value"": 12}], ""classes"": [""Mage""], ""price"": 900},
    {""id"": ""xs_pool_c"", ""name"": ""Pool Cloak"", ""icon"": ""cloak"", ""kind"": ""Armor"", ""quality"": ""Uncommon"", ""itemLevel"": 38, ""requiredLevel"": 20,
     ""equip"": ""Back"", ""armorType"": ""Cloth"", ""armor"": 40, ""stats"": [{""stat"": ""Stamina"", ""value"": 12}], ""price"": 900},
    {""id"": ""xs_free_cloak"", ""name"": ""Free Cloak"", ""icon"": ""cloak"", ""kind"": ""Armor"", ""quality"": ""Uncommon"", ""itemLevel"": 34, ""requiredLevel"": 20,
     ""equip"": ""Back"", ""armorType"": ""Cloth"", ""armor"": 36, ""stats"": [{""stat"": ""Stamina"", ""value"": 10}], ""price"": 800},
    {""id"": ""xs_green_boots"", ""name"": ""Greenset Boots"", ""icon"": ""boots"", ""kind"": ""Armor"", ""quality"": ""Uncommon"", ""itemLevel"": 38, ""requiredLevel"": 20,
     ""equip"": ""Feet"", ""armorType"": ""Mail"", ""armor"": 200, ""stats"": [{""stat"": ""Stamina"", ""value"": 12}], ""price"": 900},
    {""id"": ""xs_green_belt"", ""name"": ""Greenset Belt"", ""icon"": ""belt"", ""kind"": ""Armor"", ""quality"": ""Uncommon"", ""itemLevel"": 38, ""requiredLevel"": 20,
     ""equip"": ""Waist"", ""armorType"": ""Mail"", ""armor"": 180, ""stats"": [{""stat"": ""Stamina"", ""value"": 12}], ""price"": 900},
    {""id"": ""xs_free_boots"", ""name"": ""Free Boots"", ""icon"": ""boots"", ""kind"": ""Armor"", ""quality"": ""Uncommon"", ""itemLevel"": 34, ""requiredLevel"": 20,
     ""equip"": ""Feet"", ""armorType"": ""Mail"", ""armor"": 190, ""stats"": [{""stat"": ""Stamina"", ""value"": 10}], ""price"": 800}
  ],
  ""itemSets"": [
    {""id"": ""set_xs_guard"", ""name"": ""Testguard Battlegear"", ""items"": [""xs_helm"", ""xs_chest"", ""xs_legs"", ""xs_hands"", ""xs_ring""],
     ""bonuses"": [
       {""pieces"": 2, ""stats"": [{""stat"": ""Stamina"", ""value"": 20}], ""equipEffects"": [{""type"": ""Stat"", ""stat"": ""Strength"", ""value"": 5}]},
       {""pieces"": 3, ""equipEffects"": [{""type"": ""AbilityMod"", ""property"": ""Cost"", ""value"": -50, ""abilities"": [""warrior_heroic_strike""]}]},
       {""pieces"": 4, ""description"": ""Your melee hits have a chance to scorch the target."",
        ""equipEffects"": [{""type"": ""Proc"", ""proc"": {""trigger"": ""OnMeleeHit"", ""chance"": 100, ""internalCooldown"": 30,
          ""effects"": [{""type"": ""Damage"", ""min"": 10, ""max"": 10, ""school"": ""Fire""}]}}]}
     ]},
    {""id"": ""set_xs_shade"", ""name"": ""Testshade Garb"", ""items"": [""xs_r_hood"", ""xs_r_vest""],
     ""bonuses"": [{""pieces"": 2, ""equipEffects"": [{""type"": ""Special"", ""special"": ""RogueVigor"", ""value"": 10}]}]},
    {""id"": ""set_xs_green"", ""name"": ""Greenset"", ""items"": [""xs_green_boots"", ""xs_green_belt""],
     ""bonuses"": [{""pieces"": 2, ""stats"": [{""stat"": ""Stamina"", ""value"": 5}]}]}
  ],
  ""lootTables"": [
    {""id"": ""lt_xs_pool"", ""entries"": [{""pool"": [""xs_pool_a"", ""xs_pool_b"", ""xs_pool_c""]}]},
    {""id"": ""lt_xs_skip"", ""entries"": [{""pool"": [""xs_pool_a"", ""xs_pool_b"", ""xs_pool_c""], ""skipOwned"": true}]},
    {""id"": ""lt_xs_per5"", ""entries"": [{""pool"": [""xs_pool_a"", ""xs_pool_b"", ""xs_pool_c""], ""perMembers"": 5}]},
    {""id"": ""lt_xs_weights"", ""entries"": [{""pool"": [""xs_pool_a"", ""xs_pool_b"", ""xs_pool_c""], ""weights"": [0, 1, 0]}]},
    {""id"": ""lt_xs_usable"", ""entries"": [{""pool"": [""xs_pool_a"", ""xs_pool_b""], ""partyUsable"": true}]},
    {""id"": ""lt_xs_named_per"", ""entries"": [{""item"": ""xs_pool_c"", ""perMembers"": 5}]},
    {""id"": ""lt_xs_boss"", ""goldMin"": 10, ""goldMax"": 20, ""entries"": [{""pool"": [""xs_pool_a"", ""xs_pool_b""], ""perMembers"": 5}]},
    {""id"": ""lt_xs_legend"", ""entries"": [{""pool"": [""lg_xs_stormbrand""], ""skipOwned"": true}]}
  ],
  ""creatures"": [
    {""id"": ""cr_xs_boss"", ""name"": ""Test Boss"", ""sprite"": ""cr_wolf"", ""type"": ""Beast"", ""rank"": ""Boss"", ""levelMin"": 20, ""levelMax"": 20, ""lootTable"": ""lt_xs_boss""},
    {""id"": ""cr_xs_legend"", ""name"": ""Test Legend"", ""sprite"": ""cr_wolf"", ""type"": ""Beast"", ""rank"": ""Boss"", ""levelMin"": 20, ""levelMax"": 20, ""lootTable"": ""lt_xs_legend""}
  ]
}";

        static GameDatabase setDb;

        /// <summary>The real data plus <see cref="Bundle"/> (a database of its own: Harness.Db is not touched).</summary>
        static GameDatabase SetDb
        {
            get
            {
                if (setDb != null) return setDb;
                var files = ReadDataFiles(DataDir).ToList();
                files.Add(new KeyValuePair<string, string>("zz_items_sets_test.json", Bundle));
                setDb = GameDatabase.Load(files);
                Assert(setDb.Problems.Count == 0, "the test bundle parses: " + string.Join("; ", setDb.Problems));
                return setDb;
            }
        }

        static Unit Character(ClassId cls, int level, string name = null)
        {
            var u = UnitFactory.CreateCharacter(SetDb, cls, name ?? cls.ToString(), level, learnAll: true);
            Progression.AutoAllocateTalents(u);
            Progression.LearnAllAvailable(u);
            UnitFactory.AttachPassives(u);
            u.InvalidateStats();
            u.RestoreFull();
            return u;
        }

        static void Wear(Unit u, string itemId, EquipSlot? slot = null)
        {
            var def = SetDb.Item(itemId);
            Assert(def != null, "test item " + itemId);
            var s = slot ?? EquipmentRules.ChooseSlot(u, def);
            Assert(s.HasValue && EquipmentRules.CannotEquipReason(u, def, s.Value) == null, $"{u.Name} can wear {itemId}: {(s.HasValue ? EquipmentRules.CannotEquipReason(u, def, s.Value) : "no slot")}");
            EquipmentRules.Equip(u, new ItemInstance(def), s.Value);
        }

        static void Remove(Unit u, string itemId)
        {
            foreach (var kv in u.Equipment.Equipped.ToList())
                if (kv.Value.Def.id == itemId) { EquipmentRules.Unequip(u, kv.Key); return; }
            throw new Exception("not equipped: " + itemId);
        }

        // ------------------------------------------------------------------ database and validation

        [Test]
        public static void SetIndex_AndTestBundleValidates()
        {
            var db = SetDb;
            Assert(db.ItemSet("set_xs_guard") != null && db.ItemSet("nope") == null && db.ItemSet(null) == null, "ItemSet by id");
            Assert(db.SetOf("xs_helm")?.id == "set_xs_guard" && db.SetOf("xs_ring")?.id == "set_xs_guard", "SetOf: members of set_xs_guard");
            Assert(db.SetOf("xs_r_vest")?.id == "set_xs_shade", "SetOf: a member of set_xs_shade");
            Assert(db.SetOf("xs_pool_a") == null && db.SetOf(null) == null && db.SetOf("") == null, "SetOf: not in a set");
            // the real data has no stray set membership: every SetOf comes from a set's items list
            foreach (var it in Harness.Db.Items.Values)
            {
                var set = Harness.Db.SetOf(it.id);
                if (set != null) Assert(Array.IndexOf(set.items, it.id) >= 0, $"{it.id} is listed by {set.id}");
            }
            // the bundle itself is valid data: the validator finds nothing in it
            var problems = DataValidator.Validate(db).Where(p => p.Contains("xs_")).ToList();
            Assert(problems.Count == 0, "test bundle is valid:\n  " + string.Join("\n  ", problems));

            // a set added by hand joins the index once IndexItemSets runs
            var extra = new ItemSetDef { id = "set_xs_hand", name = "By Hand", items = new[] { "xs_free_cloak", "xs_free_boots" } };
            var copy = GameDatabase.Load(ReadDataFiles(DataDir).Append(new KeyValuePair<string, string>("zz.json", Bundle)));
            int v = copy.SetIndexVersion;
            copy.ItemSets[extra.id] = extra;
            Assert(copy.SetOf("xs_free_cloak") == null, "not indexed before IndexItemSets");
            copy.IndexItemSets();
            Assert(copy.SetOf("xs_free_cloak") == extra && copy.SetIndexVersion == v + 1, "indexed (and the index version bumped)");
        }

        /// <summary>(f) The validator rejects an unknown set item, a GrantAbility set bonus and bad pool entries.</summary>
        [Test]
        public static void Validator_RejectsBadSetsAndPools()
        {
            const string bad = @"{
  ""itemSets"": [
    {""id"": ""set_xs_bad"", ""name"": ""Bad"", ""items"": [""xs_free_cloak"", ""xs_free_boots"", ""xs_nope""],
     ""bonuses"": [{""pieces"": 2, ""equipEffects"": [{""type"": ""GrantAbility"", ""ability"": ""attack""}]}]},
    {""id"": ""set_xs_dup"", ""name"": ""Dup"", ""items"": [""xs_free_cloak"", ""xs_pool_c""]}
  ],
  ""lootTables"": [
    {""id"": ""lt_xs_bad"", ""entries"": [{""pool"": [""xs_pool_a"", ""xs_nope""]}, {""pool"": [""xs_pool_a""], ""weights"": [1, 2]}]}
  ]
}";
            var files = ReadDataFiles(DataDir).ToList();
            files.Add(new KeyValuePair<string, string>("zz_items_sets_test.json", Bundle));
            files.Add(new KeyValuePair<string, string>("zz_items_sets_bad.json", bad));
            var db = GameDatabase.Load(files);
            Assert(db.Problems.Count == 0, "parses: " + string.Join("; ", db.Problems));
            var p = DataValidator.Validate(db);
            bool Has(string where, string text) => p.Any(x => x.StartsWith(where, StringComparison.Ordinal) && x.Contains(text));
            Assert(Has("item set set_xs_bad", "unknown item 'xs_nope'"), "unknown set item: " + string.Join("\n", p));
            Assert(Has("item set set_xs_bad", "'GrantAbility' is not allowed"), "GrantAbility set bonus");
            Assert(Has("item set set_xs_dup", "already belongs to set"), "an item in two sets");
            Assert(Has("loot lt_xs_bad", "pool: unknown item 'xs_nope'"), "unknown pool item");
            Assert(Has("loot lt_xs_bad", "weights has 2 entries but the pool has 1"), "weights length");
            // the set with the smaller id keeps a doubly listed item (independent of file order)
            Assert(db.SetOf("xs_free_cloak")?.id == "set_xs_bad", "set_xs_bad < set_xs_dup keeps xs_free_cloak");
        }

        // ------------------------------------------------------------------ (a) stats, mods, specials, save

        [Test]
        public static void SetBonus_StatsApplyAtTwoPieces_AndVanishOnUnequip()
        {
            var u = Character(ClassId.Warrior, 25);
            Assert(ItemSets.Active(u).Count == 0, "no set worn");
            int v0 = u.Equipment.Version;
            float sta0 = u.Stats.Stamina, str0 = u.Stats.Strength;
            Wear(u, "xs_helm");
            Assert(u.Equipment.Version > v0, "Equipment.Version bumps on equip");
            float sta1 = u.Stats.Stamina, str1 = u.Stats.Strength;
            float kSta = (sta1 - sta0) / 10f, kStr = (str1 - str0) / 4f;   // talent multipliers (e.g. % stamina) apply to every point
            Assert(kSta > 0.9f && kStr > 0.9f, $"one piece: its own stats only ({kSta}, {kStr})");
            Assert(ItemSets.Active(u).Count == 0, "one piece: no bonus");

            Wear(u, "xs_chest");
            var act = ItemSets.Active(u);
            Assert(act.Count == 1 && act[0].Set.id == "set_xs_guard" && act[0].Index == 0 && act[0].Bonus.pieces == 2, "two pieces: the (2) bonus");
            AssertNear(u.Stats.Stamina - sta1, (10f + 20f) * kSta, 0.6f, "chest stamina + the (2) bonus's stats");
            AssertNear(u.Stats.Strength - str1, (4f + 5f) * kStr, 0.6f, "chest strength + the (2) bonus's Stat effect");
            Assert(ReferenceEquals(act, ItemSets.Active(u)), "cached while the equipment does not change");

            Remove(u, "xs_chest");
            Assert(ItemSets.Active(u).Count == 0, "the bonus goes with the second piece");
            AssertNear(u.Stats.Stamina, sta1, 0.01f, "stamina back to one piece");
            AssertNear(u.Stats.Strength, str1, 0.01f, "strength back to one piece");

            // two copies of a set ring count once
            Wear(u, "xs_ring", EquipSlot.Finger1);
            Wear(u, "xs_ring", EquipSlot.Finger2);
            Assert(ItemSets.EquippedPieces(u, SetDb.ItemSet("set_xs_guard")) == 2, "helm + ring (twice) = 2 distinct pieces");
            Remove(u, "xs_helm");
            Assert(ItemSets.EquippedPieces(u, SetDb.ItemSet("set_xs_guard")) == 1 && ItemSets.Active(u).Count == 0, "two copies of one ring are 1 piece");

            // creatures and units without a database never have set bonuses
            var wolf = UnitFactory.CreateCreature(SetDb, SetDb.Creature("cr_wolf"), 20, Team.Enemy);
            Assert(ItemSets.Active(wolf).Count == 0 && ItemSets.Active(null).Count == 0, "no bonuses for creatures / null");
            u.Equipment.Clear();
            Assert(ItemSets.EquippedPieces(u, SetDb.ItemSet("set_xs_guard")) == 0 && ItemSets.Active(u).Count == 0, "Clear bumps the version: cache dropped");
        }

        [Test]
        public static void SetBonus_AbilityModAndSpecial()
        {
            var w = Character(ClassId.Warrior, 25);
            var hs = SetDb.Ability("warrior_heroic_strike");
            Assert(hs != null, "heroic strike exists");
            float cost0 = AbilityMods.For(w, hs).CostPct;
            Wear(w, "xs_helm"); Wear(w, "xs_chest");
            AssertNear(AbilityMods.For(w, hs).CostPct, cost0, 0.01f, "(3) inactive at 2 pieces");
            Wear(w, "xs_legs");
            AssertNear(AbilityMods.For(w, hs).CostPct, cost0 - 50f, 0.01f, "(3): Heroic Strike costs 50% less");
            var other = SetDb.Ability("warrior_battle_shout") ?? SetDb.Ability("attack");
            AssertNear(AbilityMods.For(w, other).CostPct, AbilityMods.For(Character(ClassId.Warrior, 25), other).CostPct, 0.01f, "the mod's ability filter holds");

            var r = Character(ClassId.Rogue, 25);
            float e0 = r.Stats.ExtraMaxEnergy;
            Wear(r, "xs_r_hood");
            AssertNear(r.Stats.ExtraMaxEnergy, e0, 0.01f, "one piece: no Special");
            Wear(r, "xs_r_vest");
            AssertNear(r.Stats.ExtraMaxEnergy, e0 + 10f, 0.01f, "(2) Special RogueVigor: +10 maximum energy");
            Assert(ItemSets.SourceId(SetDb.ItemSet("set_xs_shade")) == "set:set_xs_shade", "Special source id");
            Remove(r, "xs_r_hood");
            AssertNear(r.Stats.ExtraMaxEnergy, e0, 0.01f, "the Special goes with the piece");
        }

        [Test]
        public static void SetBonus_SurvivesSaveRoundTrip()
        {
            var db = SetDb;
            var s = new GameSession(db, 41);
            s.NewGame(new NewGameOptions { Name = "Setsy", Class = ClassId.Warrior, StartLevel = 25, VeteranGear = true, PlayOpening = false });
            var m = s.Main;
            foreach (var id in new[] { "xs_helm", "xs_chest", "xs_legs", "xs_hands" })
            {
                s.Inventory.Add(db.Item(id));
                Assert(s.Equip(m, s.Inventory.Find(id)) == null, "equip " + id);
            }
            Assert(ItemSets.Active(m).Count == 3, "4 pieces: the (2), (3) and (4) bonuses");
            var key = ItemSets.ProcKey(ItemSets.Active(m)[2], 0);
            Assert(key == "s:set_xs_guard:2:0", "proc key format: " + key);
            m.ProcCooldowns[key] = 12f;
            float sta = m.Stats.Stamina, str = m.Stats.Strength, hp = m.MaxHealth;
            float cost = AbilityMods.For(m, db.Ability("warrior_heroic_strike")).CostPct;

            var json = s.SaveGame();
            Assert(!string.IsNullOrEmpty(json), "saved");
            var s2 = new GameSession(db, 42);
            Assert(s2.LoadGame(json, out var err), "loaded: " + err);
            var m2 = s2.Main;
            Assert(ItemSets.Active(m2).Count == 3, "the bonuses are back after loading");
            AssertNear(m2.Stats.Stamina, sta, 0.01f, "stamina after load");
            AssertNear(m2.Stats.Strength, str, 0.01f, "strength after load");
            AssertNear(m2.MaxHealth, hp, 0.01f, "max health after load");
            AssertNear(AbilityMods.For(m2, db.Ability("warrior_heroic_strike")).CostPct, cost, 0.01f, "ability mod after load");
            Assert(m2.ProcCooldowns.TryGetValue(key, out var cd) && Math.Abs(cd - 12f) < 0.01f, "the set proc cooldown is saved");

            // the session's Unequip drops the bonus too
            Assert(s2.Unequip(m2, EquipSlot.Hands) == null && ItemSets.Active(m2).Count == 2, "unequip a piece: (4) goes");
        }

        [Test]
        public static void Session_Equip_AnnouncesEachNewSetTier()
        {
            var db = SetDb;
            var s = new GameSession(db, 43);
            s.NewGame(new NewGameOptions { Name = "Toasty", Class = ClassId.Warrior, StartLevel = 25, VeteranGear = true, PlayOpening = false });
            var m = s.Main;
            List<SessionEvent> EquipAndTake(string id)
            {
                s.TakeEvents();
                s.Inventory.Add(db.Item(id));
                Assert(s.Equip(m, s.Inventory.Find(id)) == null, "equip " + id);
                return s.TakeEvents().Where(e => e.Kind == SessionEventKind.Toast && e.Id == GameSession.SetCompleteToastId).ToList();
            }
            Assert(EquipAndTake("xs_helm").Count == 0, "one piece: no set toast");
            var t2 = EquipAndTake("xs_chest");
            Assert(t2.Count == 1 && t2[0].Id2 == "set_xs_guard" && t2[0].Amount == 2 && t2[0].Unit == m, "two pieces: the (2) tier is announced");
            Assert(t2[0].Text.Contains("Testguard Battlegear") && t2[0].Text.Contains("(2/5)"), "toast text names the set and pieces: " + t2[0].Text);
            var t3 = EquipAndTake("xs_legs");
            Assert(t3.Count == 1 && t3[0].Amount == 3, "three pieces: the (3) tier");
            var t4 = EquipAndTake("xs_ring");
            Assert(t4.Count == 1 && t4[0].Amount == 4 && t4[0].Text.Contains("(4/5)"), "the ring: four pieces, the (4) tier");
            s.TakeEvents();
            Assert(s.Unequip(m, EquipSlot.Legs) == null, "unequip legs");
            Assert(!s.TakeEvents().Any(e => e.Id == GameSession.SetCompleteToastId), "losing a tier is silent");
            var again = EquipAndTake("xs_legs");
            Assert(again.Count == 1 && again[0].Id2 == "set_xs_guard" && again[0].Amount == 4, "regaining a tier announces it again");
            // a second copy of the ring adds no distinct piece and no tier; an item of no set is silent too
            s.Inventory.Add(db.Item("xs_ring"));
            s.TakeEvents();
            var free = m.Equipment[EquipSlot.Finger1]?.Id == "xs_ring" ? EquipSlot.Finger2 : EquipSlot.Finger1;
            Assert(s.Equip(m, s.Inventory.Find("xs_ring"), free) == null, "second ring");
            Assert(m.Equipment[EquipSlot.Finger1]?.Id == "xs_ring" && m.Equipment[EquipSlot.Finger2]?.Id == "xs_ring", "both rings worn");
            Assert(!s.TakeEvents().Any(e => e.Id == GameSession.SetCompleteToastId), "a duplicate piece is silent");
            Assert(EquipAndTake("xs_free_boots").Count == 0, "no set, no toast");
        }

        // ------------------------------------------------------------------ (b) set proc, (g) legendary proc

        static Battle NewBattle(int seed) => new Battle(SetDb, new Rng((ulong)seed), new StraightLinePathfinder(80f, 60f), new Inventory(), true);

        static int ProcEvents(Battle b, Unit src, School school, int from) =>
            b.Events.Skip(from).Count(e => e.Source == src && e.School == school &&
                (e.Type == CombatEventType.Damage || e.Type == CombatEventType.Miss || e.Type == CombatEventType.Resist ||
                 e.Type == CombatEventType.Absorb || e.Type == CombatEventType.Immune));

        [Test]
        public static void SetProc_FiresAtFourPieces_AndRespectsItsCooldown()
        {
            var b = NewBattle(3);
            var w = Character(ClassId.Warrior, 25);
            w.Position = new Vec2(20f, 20f);
            var foe = UnitFactory.CreateCreature(SetDb, SetDb.Creature("cr_wolf"), 20, Team.Enemy);
            foe.Position = new Vec2(21.5f, 20f);
            foe.MaxHealthMult = 50f; foe.InvalidateStats(); foe.Health = foe.MaxHealth;
            foreach (var id in new[] { "xs_helm", "xs_chest", "xs_legs" }) Wear(w, id);
            b.AddUnit(w); b.AddUnit(foe);
            b.Begin();

            int n = b.Events.Count;
            b.FireProcs(ProcTrigger.OnMeleeHit, w, foe, new ProcInfo());
            Assert(ProcEvents(b, w, School.Fire, n) == 0, "3 pieces: the (4) proc is inactive");

            w.Equipment[EquipSlot.Hands] = null;   // (equipment cannot change in combat in the session; the rules allow it)
            Wear(w, "xs_hands");
            string key = "s:set_xs_guard:2:0";
            n = b.Events.Count;
            b.FireProcs(ProcTrigger.OnMeleeHit, w, foe, new ProcInfo());
            Assert(ProcEvents(b, w, School.Fire, n) == 1, "4 pieces: the proc fires once (chance 100)");
            Assert(w.ProcCooldowns.TryGetValue(key, out var cd) && Math.Abs(cd - 30f) < 0.01f, "its internal cooldown is set under " + key);

            n = b.Events.Count;
            b.FireProcs(ProcTrigger.OnMeleeHit, w, foe, new ProcInfo());
            Assert(ProcEvents(b, w, School.Fire, n) == 0, "on cooldown: no second proc");
            b.FireProcs(ProcTrigger.OnSpellHit, w, foe, new ProcInfo());
            Assert(ProcEvents(b, w, School.Fire, n) == 0, "another trigger never fires it");

            w.ProcCooldowns[key] = 0f;
            n = b.Events.Count;
            b.FireProcs(ProcTrigger.OnMeleeHit, w, foe, new ProcInfo { OffHand = true });
            Assert(ProcEvents(b, w, School.Fire, n) == 1, "cooldown over: it fires again (any hand: set bonuses act like trinkets)");
        }

        [Test]
        public static void LegendaryChainProc_FiresInABattle()
        {
            var b = NewBattle(9);
            var w = Character(ClassId.Warrior, 25);
            w.Position = new Vec2(20f, 20f);
            Wear(w, "lg_xs_stormbrand", EquipSlot.MainHand);
            w.AutoPlay = true;
            var foes = new List<Unit>();
            foreach (var p in new[] { new Vec2(21.6f, 20f), new Vec2(22.4f, 21.4f), new Vec2(22.4f, 18.6f) })
            {
                var f = UnitFactory.CreateCreature(SetDb, SetDb.Creature("cr_wolf"), 18, Team.Enemy);
                f.Position = p;
                f.MaxHealthMult = 200f; f.InvalidateStats(); f.Health = f.MaxHealth;
                foes.Add(f);
            }
            b.AddUnit(w);
            b.AddUnits(foes);
            RulesTestUtil.Run(b, 4);
            var hit = new HashSet<Unit>();
            foreach (var e in b.Events)
                if (e.Source == w && e.School == School.Nature && e.Type == CombatEventType.Damage && e.Target != null) hit.Add(e.Target);
            Assert(hit.Count >= 2, $"the legendary's Nature chain hit {hit.Count} foes (expected 2+)");
            Assert(SetDb.Item("lg_xs_stormbrand").unique && SetDb.Item("lg_xs_stormbrand").quality == Quality.Legendary, "legendary convention: unique");
        }

        // ------------------------------------------------------------------ (c) progress

        [Test]
        public static void SetProgress_FlagsPiecesAndBonuses()
        {
            var set = SetDb.ItemSet("set_xs_guard");
            var none = ItemSets.Progress(null, set);
            Assert(none.Equipped == 0 && none.Total == 5 && none.PieceEquipped.All(x => !x) && none.BonusActive.All(x => !x) && none.ActiveTier == 0,
                "no unit: nothing equipped");
            Assert(ItemSets.Progress(null, null) == null, "no set: null");

            var u = Character(ClassId.Warrior, 25);
            Wear(u, "xs_helm"); Wear(u, "xs_legs"); Wear(u, "xs_ring", EquipSlot.Finger2);
            var p = ItemSets.Progress(u, set);
            Assert(p.Equipped == 3, "3 distinct pieces");
            Assert(p.PieceEquipped.SequenceEqual(new[] { true, false, true, false, true }), "helm, legs, ring lit");
            Assert(p.BonusActive.SequenceEqual(new[] { true, true, false }), "(2) and (3) active, (4) not");
            Assert(p.ActiveCount == 2 && p.ActiveTier == 3, "2 bonuses active, tier 3");

            var worn = ItemSets.Worn(u);
            Assert(worn.Count == 1 && worn[0].Set == set && worn[0].Equipped == 3, "Worn: the one set");
            Wear(Character(ClassId.Rogue, 25) is var r ? r : null, "xs_r_hood");
            Assert(ItemSets.Worn(r).Count == 1 && ItemSets.Worn(r)[0].ActiveCount == 0, "a single piece is worn but inactive");

            // what-if (tooltip compare): swap the legs for the hands, add the chest
            var ids = ItemSets.EquippedIds(u);
            ids.Remove("xs_legs");
            ids.Add("xs_hands");
            ids.Add("xs_chest");
            Assert(ItemSets.ActiveFor(SetDb, ids).Count == 3, "helm, ring, hands, chest: 4 pieces → 3 bonuses");
            Assert(ItemSets.ActiveFor(SetDb, new[] { "xs_ring", "xs_ring" }).Count == 0, "duplicates count once");
        }

        // ------------------------------------------------------------------ (d) pools, (e) one battle

        static LootDrop RollSeed(string table, int seed, LootContext ctx = null) => LootGenerator.Roll(SetDb, table, 20, new Rng((ulong)seed), ctx);

        [Test]
        public static void Pool_PicksExactlyOne_WeightsAndSkipOwned()
        {
            var seen = new HashSet<string>();
            for (int seed = 1; seed <= 60; seed++)
            {
                var d = RollSeed("lt_xs_pool", seed);
                Assert(d.Items.Count == 1, "a pool entry gives exactly one item");
                seen.Add(d.Items[0].Id);
            }
            Assert(seen.SetEquals(new[] { "xs_pool_a", "xs_pool_b", "xs_pool_c" }), "uniform: every pool id drops sometimes (" + string.Join(",", seen) + ")");

            for (int seed = 1; seed <= 30; seed++)
                Assert(RollSeed("lt_xs_weights", seed).Items.Single().Id == "xs_pool_b", "weights 0 never drop");

            var ctx = new LootContext { Owned = id => id == "xs_pool_a" };
            for (int seed = 1; seed <= 40; seed++)
            {
                var c = new LootContext { Owned = ctx.Owned };
                var d = RollSeed("lt_xs_skip", seed, c);
                Assert(d.Items.Count == 1 && d.Items[0].Id != "xs_pool_a", "skipOwned: never an owned id");
                Assert(c.Dropped.Contains(d.Items[0].Id), "the context records the drop");
            }
            var all = new LootContext { Owned = id => true };
            Assert(RollSeed("lt_xs_skip", 5, all).Items.Count == 0, "skipOwned: nothing drops when the party owns every id");
            var legend = new LootContext { Owned = id => false };
            legend.Dropped.Add("lg_xs_stormbrand");
            Assert(RollSeed("lt_xs_legend", 5, legend).Items.Count == 0, "skipOwned: nothing that already dropped with this context");
            Assert(RollSeed("lt_xs_legend", 5, new LootContext()).Items.Single().Id == "lg_xs_stormbrand", "a legendary pool of one drops it");
            // without skipOwned an owned id can still drop (set pieces for a second raider)
            bool ownedDrops = false;
            for (int seed = 1; seed <= 40 && !ownedDrops; seed++) ownedDrops = RollSeed("lt_xs_pool", seed, new LootContext { Owned = ctx.Owned }).Items[0].Id == "xs_pool_a";
            Assert(ownedDrops, "without skipOwned owned ids still drop");
        }

        [Test]
        public static void Pool_PerMembersAndPartyUsable()
        {
            int Count(string table, int members, int seed = 3) => RollSeed(table, seed, new LootContext { Members = members }).Items.Count;
            Assert(Count("lt_xs_per5", 1) == 1 && Count("lt_xs_per5", 5) == 1, "perMembers 5: 1 roll for 1-5 members");
            Assert(Count("lt_xs_per5", 6) == 2 && Count("lt_xs_per5", 10) == 2, "perMembers 5: 2 rolls for 6-10 members");
            Assert(Count("lt_xs_per5", 11) == 3, "perMembers 5: 3 rolls for 11");
            Assert(Count("lt_xs_named_per", 10) == 2 && Count("lt_xs_named_per", 4) == 1, "perMembers works on a named item too");
            Assert(Count("lt_xs_pool", 10) == 1, "no perMembers: once whatever the party size");
            Assert(RollSeed("lt_xs_per5", 3).Items.Count == 1, "no context: a party of one");
            for (int seed = 1; seed <= 30; seed++)
            {
                var d = RollSeed("lt_xs_per5", seed, new LootContext { Members = 10 });
                Assert(d.Items[0].Id != d.Items[1].Id, "a 10-raid's two rolls give different pieces while the pool allows");
            }

            var mage = Character(ClassId.Mage, 25);
            var warrior = Character(ClassId.Warrior, 25);
            for (int seed = 1; seed <= 20; seed++)
            {
                var cm = LootContext.For(new[] { mage }, null);
                Assert(RollSeed("lt_xs_usable", seed, cm).Items.Single().Id == "xs_pool_b", "partyUsable, a mage: the cloth gloves");
                var cw = LootContext.For(new[] { warrior }, null);
                Assert(RollSeed("lt_xs_usable", seed, cw).Items.Single().Id == "xs_pool_a", "partyUsable, a warrior: the mail gloves");
            }
            var priest = LootContext.For(new[] { Character(ClassId.Priest, 25) }, null);
            Assert(RollSeed("lt_xs_usable", 4, priest).Items.Count == 0, "partyUsable: nothing nobody can use");
            // the required level is ignored (gear a few levels ahead); class, armour and weapon rules are not
            var young = Character(ClassId.Warrior, 15);
            Assert(LootContext.CanUse(young, SetDb.Item("xs_pool_a")) && EquipmentRules.CannotUseReason(young, SetDb.Item("xs_pool_a")) != null,
                "CanUse ignores requiredLevel");
            Assert(!LootContext.CanUse(young, SetDb.Item("xs_pool_b")) && !LootContext.CanUse(Character(ClassId.Mage, 25), SetDb.Item("lg_xs_stormbrand")),
                "CanUse keeps class and weapon rules");
            Assert(LootContext.For(new[] { mage, warrior, mage }, null).Members == 2, "members: distinct characters");
        }

        [Test]
        public static void Roll_RngUseUnchangedWithoutNewKeys()
        {
            // every real table today (no pool / perMembers): identical drops and RNG state with or without a context
            foreach (var t in Harness.Db.LootTables.Values.Where(t => t.entries.All(e => (e.pool == null || e.pool.Length == 0) && e.perMembers == 0)))
                for (int seed = 1; seed <= 6; seed++)
                {
                    var r1 = new Rng((ulong)(seed * 7919)); var r2 = new Rng((ulong)(seed * 7919));
                    var ctx = new LootContext { Members = 10, Owned = id => true };
                    ctx.Party.Add(Character(ClassId.Priest, 25));
                    var a = LootGenerator.Roll(Harness.Db, t.id, 20, r1);
                    var b = LootGenerator.Roll(Harness.Db, t.id, 20, r2, ctx);
                    Assert(a.Gold == b.Gold && a.Items.Select(i => i.Name).SequenceEqual(b.Items.Select(i => i.Name)), $"{t.id}: same drops with a context");
                    Assert(r1.NextULong() == r2.NextULong(), $"{t.id}: same RNG use with a context");
                }
        }

        static Unit DeadBoss(string creature)
        {
            var c = UnitFactory.CreateCreature(SetDb, SetDb.Creature(creature), 20, Team.Enemy);
            c.Health = 0f;
            c.Dead = true;
            return c;
        }

        [Test]
        public static void OneBattle_TwoBosses_NoDuplicatePoolDrops()
        {
            for (int seed = 1; seed <= 25; seed++)
            {
                var b = NewBattle(seed);
                var w = Character(ClassId.Warrior, 25);
                b.AddUnit(w);
                b.AddUnit(DeadBoss("cr_xs_boss"));
                b.AddUnit(DeadBoss("cr_xs_boss"));
                b.LootContext = new LootContext { Members = 5 };
                b.Outcome = BattleOutcome.Victory;
                var r = BattleResult.Compute(b);
                var ids = r.Loot.Items.Select(i => i.Id).ToList();
                Assert(ids.Count == 2 && ids.Distinct().Count() == 2, $"two bosses of one fight: two different pool items ({string.Join(",", ids)})");
                Assert(r.Loot.Gold >= 20, "both bosses' gold");
            }
            // without a session context, the battle's own characters make one (members = 1 here: one roll per boss)
            var b2 = NewBattle(4);
            var w2 = Character(ClassId.Warrior, 25);
            b2.AddUnit(w2);
            b2.AddUnit(DeadBoss("cr_xs_legend"));
            b2.AddUnit(DeadBoss("cr_xs_legend"));
            b2.Outcome = BattleOutcome.Victory;
            var r2 = BattleResult.Compute(b2);
            Assert(r2.Loot.Items.Count(i => i.Id == "lg_xs_stormbrand") == 1, "a skipOwned legendary drops once per fight");
            // ... and not at all when a party member wears it
            var b3 = NewBattle(4);
            var w3 = Character(ClassId.Warrior, 25);
            Wear(w3, "lg_xs_stormbrand", EquipSlot.MainHand);
            b3.AddUnit(w3);
            b3.AddUnit(DeadBoss("cr_xs_legend"));
            b3.Outcome = BattleOutcome.Victory;
            Assert(BattleResult.Compute(b3).Loot.Items.Count == 0, "skipOwned: the wielder's party gets no second copy");
        }

        // ------------------------------------------------------------------ session wiring, starting gear

        [Test]
        public static void Session_LootContext_BattleAndChest()
        {
            var db = SetDb;
            var s = new GameSession(db, 77);
            s.NewGame(new NewGameOptions { Name = "Looty", Class = ClassId.Warrior, StartLevel = 20, VeteranGear = true, PlayOpening = false });
            s.Recruit("seren");
            s.Recruit("kael");
            var ctx = s.BuildLootContext();
            Assert(ctx.Members == s.Party.Count(u => u.IsCharacter) && ctx.Members == 3, "members = the party's characters: " + ctx.Members);
            s.Inventory.Add(db.Item("xs_pool_c"));
            Assert(ctx.IsOwned("xs_pool_c"), "owned: in the bags");
            var kael = s.FindMember("kael");
            var ring = new ItemInstance(db.Item("xs_ring"));
            EquipmentRules.Equip(kael, ring, EquipSlot.Finger1);
            Assert(ctx.IsOwned("xs_ring") && !ctx.IsOwned("xs_pool_a"), "owned: worn by a roster member");

            // a fight's battle carries the session's context
            var b = s.StartEncounter("enc_training_dummy");
            Assert(b != null, "fight starts: " + s.LastError);
            Assert(b.LootContext != null && b.LootContext.Members == 3 && b.LootContext.Party.Contains(kael), "the battle's loot context is the party's");
            Assert(b.LootContext.IsOwned("xs_ring") && b.LootContext.IsOwned("xs_pool_c"), "the battle's context sees the bags and the roster");
            Assert(s.LeaveCombat() != null, "left the dummy: " + s.LastError);

            // a chest rolls with it: skipOwned keeps what the party has out of the chest
            var chest = db.Maps["whisperwood"].chests.First(c => c.id == "chest_mossling_stash");
            chest.lootTable = "lt_xs_skip";
            chest.items = new string[0];
            chest.gold = 0;
            s.EnterMap("whisperwood", "from_village");
            s.Inventory.Add(db.Item("xs_pool_a"));
            var res = s.OpenChest("chest_mossling_stash");
            Assert(res.Ok, "chest opens: " + res.Message);
            Assert(s.PendingLoot != null && s.PendingLoot.Items.Count == 1 && s.PendingLoot.Items[0].Id == "xs_pool_b", "the chest gives the one pool item the party lacks");
        }

        [Test]
        public static void StartingGear_ReservesPoolAndSetItems()
        {
            var s = new GameSession(SetDb, 5);
            s.NewGame(new NewGameOptions { Name = "Vet", Class = ClassId.Warrior, StartLevel = 20, VeteranGear = true, PlayOpening = false });
            var eq = s.Main.Equipment;
            Assert(!eq.Contains("xs_pool_c") && !eq.Contains("xs_green_boots") && !eq.Contains("xs_green_belt"), "no pool or set item as starting gear");
            Assert(eq.Contains("xs_free_cloak") && eq.Contains("xs_free_boots"), "control: the plain greens of the same kind are taken: " + string.Join(", ", eq.Equipped.Select(kv => kv.Key + "=" + kv.Value.Id)));
        }

        // ------------------------------------------------------------------ (h) authored stat budget

        /// <summary>ItemGenerator's per-point stat cost (kept in sync by hand: ItemGenerator.StatCost is private).</summary>
        static float StatCost(StatId s)
        {
            switch (s)
            {
                case StatId.AttackPower: case StatId.RangedAttackPower: return 0.5f;
                case StatId.SpellDamage: return 0.86f;
                case StatId.HealingPower: return 0.45f;
                case StatId.MeleeCrit: case StatId.SpellCrit: case StatId.RangedCrit: case StatId.MeleeHit: case StatId.SpellHit: case StatId.Dodge: case StatId.Parry: return 14f;
                case StatId.Armor: return 0.1f;
                case StatId.ManaRegen: case StatId.HealthRegen: return 2.5f;
                default: return 1f;
            }
        }

        /// <summary>
        /// (h) Guardrail for content builders (Docs/Expansion.md §8 gear budget): every Uncommon+ equipable item of item
        /// level 12+ in the real data carries stats whose cost (stats + Stat equip effects, by ItemGenerator's cost per
        /// point) is within [0.8, 3.2] × ItemGenerator.StatBudget for its level, quality and slot.
        /// </summary>
        [Test]
        public static void AuthoredStatBudget_WithinBounds()
        {
            var bad = new List<string>();
            int n = 0;
            foreach (var d in Harness.Db.Items.Values.OrderBy(x => x.id, StringComparer.Ordinal))
            {
                if (d.equip == EquipType.None || d.quality < Quality.Uncommon || d.itemLevel < 12) continue;
                n++;
                float cost = 0f;
                foreach (var m in d.stats) cost += m.value * StatCost(m.stat);
                foreach (var p in d.equipEffects) if (p.type == "Stat") cost += p.value * StatCost(p.stat);
                float budget = ItemGenerator.StatBudget(d.itemLevel, d.quality, d.equip);
                float ratio = cost / budget;
                if (ratio < 0.8f || ratio > 3.2f) bad.Add($"{d.id} ({d.quality} ilvl {d.itemLevel} {d.equip}): stats cost {cost:0.#} = {ratio:0.00} × budget {budget:0.#}");
            }
            Assert(n >= 20, "the slice has 20+ such items: " + n);
            Assert(bad.Count == 0, "authored stats outside [0.8, 3.2] × StatBudget:\n  " + string.Join("\n  ", bad));
        }
    }
}
