// GameSession save/load: rich state round trip (save -> load -> save identical JSON), restored state, rules.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    public static class TestsSessionSave
    {
        /// <summary>A session with a bit of everything: companions (active/camp/away), pet, suffix items, auras from
        /// other members, cooldowns, quests, flags, approval, vendor stock/buyback, an open loot window, another map.</summary>
        static GameSession RichSession()
        {
            var db = Harness.Db;
            var s = SessionTest.NewGame(ClassId.Hunter, 20, seed: 301, name: "Rikku");
            s.Recruit("seren");
            s.Recruit("kael");
            s.Recruit("lys");
            s.Recruit("aldric");   // the party of 5 is full
            Harness.Assert(s.Party.Count == s.PartySize, "the rich session fills the party");
            s.Recruit("pip");      // camp (party full)
            s.Recruit("torvan");
            s.Dismiss("torvan");   // away
            s.Quests.Start("mq_lanterns");
            s.Flags.Set("met_elder");
            s.ChangeApproval("seren", 7);
            s.ChangeApproval("kael", -3);
            // random suffix items in the bags and on a companion
            for (int i = 0; i < 4; i++) s.Inventory.Add(ItemGenerator.RandomItem(db, s.Rng, 18 + i, Quality.Uncommon));
            var kael = s.FindMember("kael");
            foreach (var it in new List<ItemInstance>(s.Inventory.Items))
                if (it.SuffixId.Length > 0 && s.CanEquip(kael, it) == null) { s.Equip(kael, it); break; }
            // pet
            var r = s.UseAbility(s.Main, "hunter_call_pet");
            Harness.Assert(r.Ok && s.Main.Pet != null, "call pet: " + r.Reason);
            s.Main.Pet.Health = s.Main.Pet.MaxHealth * 0.6f;
            // a buff from another member, a food aura mid-way, a cooldown
            var seren = s.FindMember("seren");
            var pw = s.UseAbility(seren, "priest_power_word_fortitude", kael);
            Harness.Assert(pw.Ok, "fortitude: " + pw.Reason);
            s.Main.Health = s.Main.MaxHealth * 0.5f;
            Harness.Assert(s.UseItem(s.Main, s.Inventory.Find("food_rice_ball")).Ok, "eat");
            Harness.Assert(s.UseItem(seren, s.Inventory.Find("potion_minor_healing")).Ok, "potion");
            s.Tick(4.3f);
            // vendor
            s.OpenVendor("merchant_tilly");
            s.Buy("potion_minor_healing", 1);
            s.Sell(s.Inventory.Find("bandage_linen"), 1);
            s.CloseVendor();
            // another map, a chest left open in the loot window
            s.EnterMap("whisperwood", "from_village");
            s.MoveLeader(new Vec2(12f, 7f));
            s.OpenChest("chest_mossling_stash");
            s.Tick(2.5f);
            return s;
        }

        [Test]
        public static void SaveLoadSave_IsIdentical()
        {
            var s = RichSession();
            Harness.Assert(s.CannotSaveReason() == null, "can save: " + s.CannotSaveReason());
            string json1 = s.SaveGame();
            Harness.Assert(json1.Length > 1000, "non-trivial save");

            var s2 = new GameSession(Harness.Db, 999);
            Harness.Assert(s2.LoadGame(json1, out var err), "load: " + err);
            string json2 = s2.SaveGame();
            if (json1 != json2) Harness.Assert(false, "save -> load -> save differs at: " + FirstDiff(json1, json2));

            // load into an existing session too
            var s3 = SessionTest.NewGame(ClassId.Warrior, 5, seed: 5);
            Harness.Assert(s3.LoadGame(json1, out err), "load into a used session: " + err);
            Harness.Assert(s3.SaveGame() == json1, "identical after loading into a used session");

            // restored state
            Harness.Assert(s2.Main.Name == "Rikku" && s2.Main.Level == 20 && s2.Main.ClassId == ClassId.Hunter, "main character");
            Harness.Assert(s2.Party.Count == s.Party.Count && s2.Roster.Count == s.Roster.Count, "party and roster");
            for (int i = 0; i < s.Party.Count; i++) Harness.Assert(s2.MemberId(s2.Party[i]) == s.MemberId(s.Party[i]), "party order");
            Harness.Assert(s2.CompanionStatusOf("pip") == CompanionStatus.Camp && s2.CompanionStatusOf("torvan") == CompanionStatus.Away, "camp/away");
            Harness.Assert(s2.Main.Pet != null && s2.Main.Pet.Creature.id == s.Main.Pet.Creature.id && Math.Abs(s2.Main.Pet.Health - s.Main.Pet.Health) < 0.01f, "pet restored");
            Harness.Assert(s2.PartyUnits().Count == s.PartyUnits().Count, "party units incl. pet");
            foreach (var u in s.Roster)
            {
                var v = s2.FindMember(s.MemberId(u));
                Harness.Assert(v != null, "member " + u.Name);
                Harness.Assert(Math.Abs(u.MaxHealth - v.MaxHealth) < 0.01f && Math.Abs(u.Health - v.Health) < 0.01f, $"{u.Name} health");
                Harness.Assert(Math.Abs(u.Stats.AttackPower - v.Stats.AttackPower) < 0.01f && Math.Abs(u.Stats.Armor - v.Stats.Armor) < 0.01f, $"{u.Name} stats");
                Harness.Assert(u.Abilities.Count == v.Abilities.Count && u.Talents.Count == v.Talents.Count, $"{u.Name} abilities/talents");
                Harness.Assert(u.Equipment.Count == v.Equipment.Count, $"{u.Name} equipment");
                Harness.Assert(u.Auras.Count == v.Auras.Count, $"{u.Name} auras ({u.Auras.Count} vs {v.Auras.Count})");
                Harness.Assert(Vec2.Distance(u.Position, v.Position) < 1e-3f, $"{u.Name} position");
            }
            var kael2 = s2.FindMember("kael");
            var fort = kael2.FindAura("priest_power_word_fortitude");
            Harness.Assert(fort != null && fort.Caster == s2.FindMember("seren"), "buff caster restored");
            bool suffix = false;
            foreach (var kv in kael2.Equipment.Equipped) if (kv.Value.SuffixId.Length > 0 && kv.Value.SuffixStats.Count > 0) suffix = true;
            foreach (var it in s2.Inventory.Items) if (it.SuffixId.Length > 0 && it.SuffixStats.Count > 0) suffix = true;
            Harness.Assert(suffix, "random suffix stats restored");
            Harness.Assert(s2.Gold == s.Gold && s2.Inventory.Items.Count == s.Inventory.Items.Count, "bags and gold");
            Harness.Assert(s2.GetApproval("seren") == 7 && s2.GetApproval("kael") == -3, "approval");
            Harness.Assert(s2.Quests.IsActive("mq_lanterns") && s2.Flags.IsSet("met_elder"), "world state");
            Harness.Assert(s2.MapId == "whisperwood" && s2.Map.IsChestOpened("chest_mossling_stash"), "map state");
            Harness.Assert((s2.PendingLoot == null) == (s.PendingLoot == null), "loot window");
            Harness.Assert(Math.Abs(s2.GameHour - s.GameHour) < 1e-4f && s2.Day == s.Day, "clock");
            Harness.Assert(s2.Main.CooldownLeft(Harness.Db.Ability("use_potion_minor_healing")) == s.Main.CooldownLeft(Harness.Db.Ability("use_potion_minor_healing")), "cooldowns");
            Harness.Assert(s2.Rng.NextULong() == s.Rng.NextULong(), "rng state continues identically");
            Harness.Assert(s2.Mode == SessionMode.Exploration && s2.Field != null && s2.Field.Units.Count == s2.PartyUnits().Count, "playable after load");
            var ev = s2.TakeEvents();
            Harness.Assert(SessionTest.FindEvent(ev, SessionEventKind.GameLoaded) != null && SessionTest.FindEvent(ev, SessionEventKind.MapEntered) != null, "load events");

            // the vendor remembers its stock and buyback
            s2.OpenVendor("merchant_tilly");
            Harness.Assert(s2.ActiveVendor.Buyback.Count == 1, "buyback restored");

            // header
            var h = GameSession.ReadSaveHeader(json1);
            Harness.Assert(h != null && h.playerName == "Rikku" && h.playerLevel == 20 && h.mapId == "whisperwood" && h.party.Count == s.Party.Count, "save header");
            Harness.Assert(h.version == GameSession.SaveVersion, "versioned");
        }

        [Test]
        public static void Save_Rules_And_BadInput()
        {
            var s = SessionTest.NewGame(ClassId.Warrior, 1, seed: 311);
            s.StartEncounter("enc_training_dummy");
            Harness.Assert(s.CannotSaveReason() != null && !s.TrySaveGame(out var j, out var why) && why.Length > 0, "no save in combat");
            bool threw = false;
            try { s.SaveGame(); } catch (InvalidOperationException) { threw = true; }
            Harness.Assert(threw, "SaveGame throws when not allowed");
            s.LeaveCombat();
            Harness.Assert(s.TrySaveGame(out var json, out why), "save after combat");
            var name = s.Main.Name;
            Harness.Assert(!s.LoadGame("{ not json", out var err) && err.Length > 0, "bad json refused");
            Harness.Assert(!s.LoadGame("{\"version\": 99}", out err) && err.Contains("newer"), "newer version refused");
            Harness.Assert(s.HasGame && s.Main.Name == name && s.Mode == SessionMode.Exploration, "session unchanged after a refused load");
            Harness.Assert(GameSession.ReadSaveHeader("garbage") == null, "header of garbage");
            // a save of a fresh game round-trips too
            var s2 = new GameSession(Harness.Db, 1);
            Harness.Assert(s2.LoadGame(json, out err) && s2.SaveGame() == json, "fresh-game round trip");
        }

        static string FirstDiff(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < n && a[i] == b[i]) i++;
            int from = Math.Max(0, i - 200);
            return $"char {i}:\n--- saved ---\n{a.Substring(from, Math.Min(400, a.Length - from))}\n--- re-saved ---\n{b.Substring(from, Math.Min(400, b.Length - from))}";
        }
    }
}
