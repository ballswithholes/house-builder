// FULL-GAME PLAYTHROUGH: proves the vertical slice is completable from a level 1 NewGame, for every starting class,
// through every main-quest stage to the Hollow Warden's defeat, the RekindleLanterns outcome and the return to Elder
// Maru, plus every side quest's completion path — all through the public GameSession API:
//   * the opening and every quest step are played as dialogues (choices picked by their text, no flags poked);
//   * companions are recruited through their dialogues (party of config.partySize = 5, later recruits wait at camp);
//   * the party walks between maps through transitions and triggers encounters by walking into them;
//   * every fight is played to the end by the party AI (AutoPlay on everyone) — a defeat fails the test;
//   * between fights: loot is taken, level-ups are applied, the main character trains at the class trainer and
//     spends talent points (companions auto-train), upgrades from loot/rewards are equipped, and the party recovers
//     (real-time regeneration through Tick, the inn or a camp long rest) before the next fight.
// Side-quest routes vary per run (fight / intimidate / pay / persuade-by-class) so different completion paths are proven.
using System;
using System.Collections.Generic;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsSessionFullPlaythrough
    {
        // every class, with the side-quest routes varied between runs
        [Test] public static void FullPlaythrough_Warrior() => Run(ClassId.Warrior, 1001, BridgeRoute.Intimidate, WicksRoute.Fight, KeeperRoute.Letter);
        [Test] public static void FullPlaythrough_Priest() => Run(ClassId.Priest, 2002, BridgeRoute.Fight, WicksRoute.Fight, KeeperRoute.Priest);
        [Test] public static void FullPlaythrough_Mage() => Run(ClassId.Mage, 3003, BridgeRoute.Pay, WicksRoute.Fight, KeeperRoute.Fight);
        [Test] public static void FullPlaythrough_Paladin() => Run(ClassId.Paladin, 4004, BridgeRoute.Paladin, WicksRoute.Fight, KeeperRoute.Letter);
        [Test] public static void FullPlaythrough_Hunter() => Run(ClassId.Hunter, 5005, BridgeRoute.Fight, WicksRoute.Fight, KeeperRoute.Fight);
        [Test] public static void FullPlaythrough_Rogue() => Run(ClassId.Rogue, 6006, BridgeRoute.Intimidate, WicksRoute.Fight, KeeperRoute.Letter);
        [Test] public static void FullPlaythrough_Shaman() => Run(ClassId.Shaman, 7007, BridgeRoute.Pay, WicksRoute.Shaman, KeeperRoute.Fight);
        [Test] public static void FullPlaythrough_Warlock() => Run(ClassId.Warlock, 8008, BridgeRoute.Fight, WicksRoute.Fight, KeeperRoute.Letter);

        public enum BridgeRoute { Fight, Intimidate, Pay, Paladin }
        public enum WicksRoute { Fight, Shaman }
        public enum KeeperRoute { Letter, Priest, Fight }

        /// <summary>The quests of the 1-12 slice this playthrough completes (expansion quests live in other zones).</summary>
        public static readonly string[] SliceQuests = { "mq_lanterns", "sq_shepherd", "sq_spirit_friend", "sq_wicks", "sq_satchel", "sq_bridge" };

        /// <summary>Balance report (--sim): every class through the whole slice with many seeds; where do runs fail?</summary>
        [Sim]
        public static void FullPlaythrough_WinRates()
        {
            int seeds = int.TryParse(Environment.GetEnvironmentVariable("PLAYTHROUGH_SEEDS"), out var n) ? n : 20;
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Paladin, ClassId.Hunter, ClassId.Rogue, ClassId.Priest, ClassId.Shaman, ClassId.Mage, ClassId.Warlock })
            {
                int ok = 0;
                var fails = new Dictionary<string, int>();
                for (int i = 0; i < seeds; i++)
                {
                    var err = TryRun(c, (ulong)(7919 * (i + 1) + (int)c), i);
                    if (err == null) { ok++; continue; }
                    var m = err.Split('\n')[0];
                    int cut = m.IndexOf('(');
                    var key = cut > 0 ? m.Substring(0, cut) : m;
                    fails.TryGetValue(key, out var k);
                    fails[key] = k + 1;
                }
                Console.WriteLine($"    {c}: {ok}/{seeds} complete" + (fails.Count > 0 ? "; failures: " + string.Join("; ", fails.Select(kv => $"{kv.Value}x {kv.Key}")) : ""));
            }
        }

        /// <summary>One run with routes picked from the seed index (class-specific routes for paladins, shamans and
        /// priests); null when it completed, else the failure and the end of its log.</summary>
        internal static string TryRun(ClassId c, ulong seed, int i)
        {
            var bridge = c == ClassId.Paladin ? BridgeRoute.Paladin : (BridgeRoute)(i % 3);          // Fight, Intimidate, Pay
            var wicks = c == ClassId.Shaman ? WicksRoute.Shaman : WicksRoute.Fight;
            var keeper = i % 3 == 0 ? KeeperRoute.Fight : (c == ClassId.Priest ? KeeperRoute.Priest : KeeperRoute.Letter);
            var p = new Playthrough(c, seed);
            try
            {
                p.Play(bridge, wicks, keeper);
                return null;
            }
            catch (Exception e) { return e.Message + "\n" + p.Tail(80); }
        }

        static void Run(ClassId cls, ulong seed, BridgeRoute bridge, WicksRoute wicks, KeeperRoute keeper)
        {
            var p = new Playthrough(cls, seed);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                p.Play(bridge, wicks, keeper);
            }
            catch (Exception e)
            {
                throw new Exception($"{cls} playthrough failed: {e.Message}\n--- log (last 60 lines) ---\n{p.Tail(60)}", e);
            }
            if (Environment.GetEnvironmentVariable("PLAYTHROUGH_LOG") == "1") Console.WriteLine(p.Tail(100000));
            Console.WriteLine($"    {cls}: level {p.S.Main.Level}, {p.Fights} fights won, {p.Rests} long rests, {p.Trainings} trainer visits, " +
                              $"{p.S.Inventory.Gold / 100}s, day {p.S.Day} {p.S.GameHour:0.0}h, {sw.ElapsedMilliseconds} ms; party {string.Join(", ", p.S.Party.Select(u => $"{u.Name} L{u.Level}"))}");
        }

        sealed class StopException : Exception { }

        /// <summary>Balance report (--sim): replays the Hollow Warden fight from a save made right before it.</summary>
        [Sim]
        public static void Warden_Replay()
        {
            int tries = int.TryParse(Environment.GetEnvironmentVariable("WARDEN_TRIES"), out var n) ? n : 40;
            var clsEnv = Environment.GetEnvironmentVariable("WARDEN_CLASS");
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Priest, ClassId.Mage, ClassId.Warlock })
            {
                if (!string.IsNullOrEmpty(clsEnv) && c.ToString() != clsEnv) continue;
                var p = new Playthrough(c, 4242 + (ulong)c) { StopBefore = "enc_warden" };
                try { p.Play(BridgeRoute.Fight, WicksRoute.Fight, KeeperRoute.Letter); }
                catch (StopException) { }
                Assert(p.Snapshot != null, "reached the Warden");
                int wins = 0, rounds = 0;
                string lossLog = null;
                for (int i = 0; i < tries; i++)
                {
                    var s = new GameSession(Db, 99);
                    Assert(s.LoadGame(p.Snapshot, out var err), err);
                    s.Rng.s0 = 0x9E3779B97F4A7C15UL * (ulong)(i + 1); s.Rng.s1 = 0xBF58476D1CE4E5B9UL ^ (ulong)i;
                    s.StartEncounter("enc_warden");
                    var b = s.Battle;
                    int guard = 0;
                    while (!b.IsOver && guard++ < 40000 && b.Round <= 100) s.RunAIStep();
                    rounds += b.Round;
                    if (b.Outcome == BattleOutcome.Victory) wins++;
                    else if (lossLog == null && Environment.GetEnvironmentVariable("WARDEN_LOG") == "1")
                        lossLog = string.Join("\n", b.Events.Where(e => e.Type != CombatEventType.Move && e.Type != CombatEventType.ResourceChange && e.Type != CombatEventType.Threat).Select(e => $"R{e.Round} {e.Text}"));
                }
                var view = new GameSession(Db, 1);
                view.LoadGame(p.Snapshot, out _);
                var party = string.Join(", ", view.Party.Select(u => $"{u.Name}({u.ClassId}) L{u.Level} {u.MaxHealth:0}hp"));
                Console.WriteLine($"    Warden vs {c} party [{party}]: {wins}/{tries} wins, avg {rounds / (float)tries:0.0} rounds");
                if (lossLog != null) Console.WriteLine(lossLog);
            }
        }

        /// <summary>One scripted run through the slice.</summary>
        sealed class Playthrough
        {
            public readonly GameSession S;
            readonly ClassId cls;
            readonly List<string> lines = new List<string>();
            public int Fights, Rests, Trainings;
            /// <summary>How the party answers each encounter's dialogue, whenever it triggers (walking anywhere near it).</summary>
            readonly Dictionary<string, string[]> encounterScripts = new Dictionary<string, string[]>();
            /// <summary>Sims: stop (StopException) right before walking into this encounter, saving the game in Snapshot.</summary>
            public string StopBefore, Snapshot;
            readonly HashSet<string> visitedTrainer = new HashSet<string>();

            public Playthrough(ClassId c, ulong seed)
            {
                cls = c;
                S = new GameSession(Db, seed);
                S.Settings.CompanionAutoPlay = true;   // the party AI plays everyone in this test
                S.EventRaised += e =>
                {
                    switch (e.Kind)
                    {
                        case SessionEventKind.QuestStarted: case SessionEventKind.QuestUpdated: case SessionEventKind.QuestCompleted:
                        case SessionEventKind.LevelUp: case SessionEventKind.CompanionRecruited: case SessionEventKind.MapEntered:
                        case SessionEventKind.CombatStarted: case SessionEventKind.CombatEnded: case SessionEventKind.SpecialOutcome:
                        case SessionEventKind.GameOver:
                            Note($"[{e.Kind}] {e.Id} {e.Text}");
                            break;
                    }
                };
            }

            void Note(string s) { lines.Add(s); }
            public string Tail(int n) => string.Join("\n", lines.Skip(Math.Max(0, lines.Count - n)));

            string Stage(string q) => S.Quests.GetStage(q) ?? "";

            // =========================================================== the route

            public void Play(BridgeRoute bridge, WicksRoute wicksRoute, KeeperRoute keeper)
            {
                // how encounter dialogues are answered on this run (they may trigger while walking past)
                encounterScripts["enc_mossling_camp"] = wicksRoute == WicksRoute.Shaman ? new[] { "Speak in the slow", "(Leave them to it.)" } : new[] { "(Attack.)" };
                encounterScripts["enc_bandit_lookouts"] = new[] { "Fine. Let's go and see Rusk", "(Continue.)" };
                switch (bridge)
                {
                    // warriors plant their weapon; everyone else tries the Intimidation check (a failed check means a fight)
                    case BridgeRoute.Intimidate: encounterScripts["enc_bridge_toll"] = cls == ClassId.Warrior ? new[] { "Plant your weapon", "(Continue.)" } : new[] { "Walk away. Now.", "(Continue.)" }; break;
                    case BridgeRoute.Paladin: encounterScripts["enc_bridge_toll"] = new[] { "Lay down your arms", "Lanternvale needs hands", "(Continue.)" }; break;
                    case BridgeRoute.Pay: encounterScripts["enc_bridge_toll"] = new[] { "Five silver? Fine", "(Continue.)" }; break;
                    default: encounterScripts["enc_bridge_toll"] = new[] { "No toll. Stand aside." }; break;
                }
                switch (keeper)
                {
                    // Keeper Ishiro: the letter (read by Maru), a priest's light or the fight
                    case KeeperRoute.Letter: encounterScripts["enc_keeper"] = new[] { "Your letter reached Maru", "(Continue.)" }; break;
                    case KeeperRoute.Priest: encounterScripts["enc_keeper"] = new[] { "Lay a hand on his shoulder", "(Continue.)" }; break;
                    default: encounterScripts["enc_keeper"] = new[] { "Stand aside, spirit." }; break;
                }
                encounterScripts["enc_warden"] = new[] { "(Let Seren speak.)" };

                S.NewGame(new NewGameOptions { Name = "Hero", Class = cls, StartLevel = 1, PlayOpening = true });
                S.SetAutoPlay(S.Main, true);
                int startAbilities = S.Main.Abilities.Count;
                Assert(S.Mode == SessionMode.Dialogue, "the opening plays");
                Converse("Grey? Like ash?", "I'll help.");
                Assert(Stage("mq_lanterns") == "elder", "main quest: elder");
                Assert(S.Main.Level == 1 && S.MapId == "lanternvale", "level 1 in Lanternvale");

                // ---- Lanternvale: companions, the Elder, side quests
                TalkTo("kael", "I'm going up to the Old Shrine", "Welcome aboard");
                Assert(S.CompanionStatusOf("kael") == CompanionStatus.Active, "Kael recruited");
                TalkTo("aldric", "I'm going up to the Old Shrine", "Welcome aboard");
                Assert(S.CompanionStatusOf("aldric") == CompanionStatus.Active, "Aldric recruited");
                TalkTo("elder_maru", "Where do I start", "I'll keep it safe", "Goodbye");
                Assert(Stage("mq_lanterns") == "wayshrine" && S.CountItem("kindling_taper") == 1, "main quest: wayshrine, taper received");

                TalkTo("shepherd_bram", "Is something wrong", "I'll handle it", "Goodbye");
                TalkTo("child_nell", "I'll look for him", "I'll bring him home", "Goodbye");
                TalkTo("lamplighter_tobben", "Stealing wicks", "I'll get your wicks back", "Goodbye");
                TalkTo("postman_fennick", "You look shaken", "I'll get it back", "Goodbye");
                TalkTo("guard_holt", "Any trouble around", "I'll deal with them", "Goodbye");
                foreach (var q in new[] { "sq_shepherd", "sq_spirit_friend", "sq_wicks", "sq_satchel", "sq_bridge" })
                    Assert(S.Quests.IsActive(q), $"side quest {q} started");
                Assert(S.CountItem("nells_bell") == 1, "Nell's bell");

                OpenChest("chest_village_oak");
                Train();

                // the pasture wolves (walk into them)
                WalkInto("enc_pasture_wolves");
                Assert(Stage("sq_shepherd") == "report", "wolves driven off: " + Stage("sq_shepherd"));
                TalkTo("shepherd_bram", "What are they running from", "I'll find him");
                Assert(Stage("sq_shepherd") == "greymane", "hunt Greymane");
                Train();

                // ---- Whisperwood, first visit
                Travel("to_whisperwood", "whisperwood");
                WalkInto("enc_boars_edge");
                WalkInto("enc_greymane");
                Assert(Stage("sq_shepherd") == "return", "Greymane put to rest");
                WalkInto("enc_forest_wolves");

                // Moppet under the mushrooms (Nell's bell), and his secret stump
                TalkTo("moppet", "Ring Nell's bell", "Nell sent me", "(Continue.)");
                Assert(S.Flags.IsSet("moppet_found") && Stage("sq_spirit_friend") == "return", "Moppet found");
                OpenChest("chest_moppet_stump");

                // the Wayside Shrine → Komorebi; Seren and Rook join (the party of 5 is now full)
                Go(RegionPos("reg_wayshrine"));
                Assert(Stage("mq_lanterns") == "komorebi", "main quest: komorebi (" + Stage("mq_lanterns") + ")");
                TalkTo("komorebi", "How can I help", "I'll bring them", "Goodbye");
                Assert(Stage("mq_lanterns") == "embers" && S.Flags.IsSet("met_komorebi"), "main quest: embers");
                TalkTo("seren", "I'm going up to the Old Shrine too", "I'd be honoured");
                Assert(S.CompanionStatusOf("seren") == CompanionStatus.Active && S.Party.Count == Math.Min(4, S.PartySize), "Seren joins, party of 4");
                bool roomForRook = S.Party.Count < S.PartySize;
                TalkTo("rook", "The Hollow's spreading", "Welcome aboard");
                if (roomForRook) Assert(S.CompanionStatusOf("rook") == CompanionStatus.Active && S.Party.Count == Math.Min(5, S.PartySize), "Rook joins, party of 5");
                else Assert(S.CompanionStatusOf("rook") == CompanionStatus.Camp, "Rook waits at camp (party full)");

                // Mosslings: the scamps, then Puddlecap Hollow (wicks)
                WalkInto("enc_mossling_scamps");
                OpenChest("chest_mossling_stash");
                WalkInto("enc_mossling_camp");
                Assert(S.CountItem("lantern_wick") >= 6 && Stage("sq_wicks") == "return", $"wicks recovered ({S.CountItem("lantern_wick")}, {Stage("sq_wicks")})");

                // the Old Bridge: lookouts and Rusk
                WalkInto("enc_bandit_lookouts");
                WalkInto("enc_bridge_toll");
                Assert(S.Flags.IsSet("bandits_dealt_with") && Stage("sq_bridge") == "report", "bandits dealt with: " + Stage("sq_bridge"));
                if (bridge == BridgeRoute.Pay) Assert(S.Flags.IsSet("bandits_paid"), "paid");
                if (bridge == BridgeRoute.Paladin) Assert(S.Flags.IsSet("bandits_peaceful"), "the farmers go home to work");
                if (bridge == BridgeRoute.Intimidate && cls == ClassId.Warrior) Assert(S.Flags.IsSet("bandits_scared"), "scared off");
                if (bridge == BridgeRoute.Fight) Assert(S.Map.IsEncounterDone(S.Map.FindEncounter("enc_bridge_toll")) && !S.Flags.IsSet("bandits_peaceful"), "fought");

                // the spiders' hollow (hidden ambush) and Fennick's satchel
                WalkInto("enc_boars_road");
                Go(S.Map.FindEncounter("enc_spiders").pos);
                Assert(S.Map.IsEncounterDone(S.Map.FindEncounter("enc_spiders")), "the spiders ambushed us and lost");
                OpenChest("chest_satchel");
                Assert(S.CountItem("fennicks_satchel") == 1 && Stage("sq_satchel") == "return", "satchel recovered");
                TryLock("chest_bandit_strongbox");

                // ---- back to the village: turn in, train, rest at the inn
                Travel("to_lanternvale", "lanternvale");
                TalkTo("shepherd_bram", "Greymane won't trouble", "Goodbye");
                Assert(S.Quests.IsCompleted("sq_shepherd"), "Wolves at the Fold complete");
                TalkTo("child_nell", "He was hiding under the glowing mushrooms", "Goodbye");
                Assert(S.Quests.IsCompleted("sq_spirit_friend"), "Little Lost Light complete");
                TalkTo("lamplighter_tobben", wicksRoute == WicksRoute.Shaman ? "The Mosslings gave them back" : "Here. Six wicks", "Goodbye");
                Assert(S.Quests.IsCompleted("sq_wicks"), "Wicks Gone Walkabout complete");
                TalkTo("postman_fennick", "Here you go", "I'll take it to her", "Goodbye");
                Assert(Stage("sq_satchel") == "deliver" && S.CountItem("ishiro_letter") == 1, "letter for the Elder");
                TalkTo("elder_maru", "I have a letter for you", "I'll tell him", "Goodbye");
                Assert(S.Quests.IsCompleted("sq_satchel") && S.Flags.IsSet("ishiro_letter_read"), "Return to Sender complete");
                string report = "I paid them|They won't be back|They were farmers|The bandits won't trouble anyone";
                TalkTo("guard_holt", report, "Goodbye");
                Assert(S.Quests.IsCompleted("sq_bridge"), "Toll at the Old Bridge complete");
                ClaimRewards();
                Train();
                InnRest();

                // ---- Whisperwood, second visit: embers, Komorebi, Rotheart
                Travel("to_whisperwood", "whisperwood");
                WalkInto("enc_wisps_grove");
                if (S.CountItem("spirit_ember") < 3) WalkInto("enc_wisps_hollow");
                Assert(S.CountItem("spirit_ember") >= 3 && Stage("mq_lanterns") == "embers_return", $"three embers ({S.CountItem("spirit_ember")}, {Stage("mq_lanterns")})");
                WalkInto("enc_blighted_wolves");
                TalkTo("komorebi", "Offer the Spirit Embers", "I'll do what I can");
                Assert(S.Flags.IsSet("embers_gathered") && Stage("mq_lanterns") == "rotheart", "main quest: rotheart");
                var locked = S.UseTransition("to_shrine");
                Assert(!locked.Ok && locked.Kind == InteractKind.Locked, "the stair is sealed until Rotheart falls");
                WalkInto("enc_rotheart");
                Assert(S.Flags.IsSet("rotheart_defeated") && Stage("mq_lanterns") == "shrine", "main quest: shrine");
                if (!S.Map.IsEncounterDone(S.Map.FindEncounter("enc_wisps_hollow"))) WalkInto("enc_wisps_hollow");
                TryLock("chest_grey_offering");

                // ---- the Old Lantern Shrine
                Travel("to_shrine", "shrine");
                Go(S.Leader.Position + new Vec2(2f, 0f));
                Assert(Stage("mq_lanterns") == "warden", "main quest: warden (" + Stage("mq_lanterns") + ")");
                Assert(S.Party.Count == S.PartySize, $"the party is full ({S.Party.Count}/{S.PartySize})");
                TalkTo("torvan", "We're here to free the Warden", "Climb with us");
                Assert(S.CompanionStatusOf("torvan") == CompanionStatus.Camp, "Torvan waits at camp");
                WalkInto("enc_hollow_pilgrims");
                OpenChest("chest_shrine_terrace");
                WalkInto("enc_terrace_wisps");
                WalkInto("enc_keeper");
                Assert(S.Flags.IsSet("keeper_done"), "Keeper Ishiro dealt with");
                if (keeper != KeeperRoute.Fight) Assert(S.Flags.IsSet("ishiro_at_peace") && Owned("ishiros_prayer_beads"), "Keeper Ishiro at peace (his beads given)");
                else Assert(!S.Flags.IsSet("ishiro_at_peace") && S.Map.IsEncounterDone(S.Map.FindEncounter("enc_keeper")), "Keeper Ishiro fought");
                TryLock("chest_keepers_offering");
                WalkInto("enc_warden");
                Assert(S.Flags.IsSet("warden_defeated") && S.Battle == null, "the Hollow Warden defeated");
                Assert(S.Quests.GetEntry("mq_lanterns") != null && Stage("mq_lanterns") == "rekindle", "main quest: rekindle (" + Stage("mq_lanterns") + ")");

                // the Warden's spirit: rekindle the Heart Lantern (RekindleLanterns) and go home
                var rekindled = new List<SessionEvent>();
                Action<SessionEvent> watch = e => { if (e.Kind == SessionEventKind.SpecialOutcome && e.Id == GameSession.RekindleLanternsSpecial) rekindled.Add(e); };
                S.EventRaised += watch;
                TalkTo("warden_spirit", "(Look to Seren.)|This taper carries a spark", "Seren, wait", "(Return to Lanternvale.)");
                S.EventRaised -= watch;
                Assert(rekindled.Any(e => e.Amount == 1), "RekindleLanterns story moment raised");
                Assert(S.LanternsRekindled && S.Flags.IsSet("heart_lantern_lit"), "the lanterns of Lanternvale are lit");
                Assert(S.MapId == "lanternvale", "returned to Lanternvale");
                Assert(Stage("mq_lanterns") == "home", "main quest: home (" + Stage("mq_lanterns") + ")");
                TalkTo("elder_maru", "Seren performed the Kindling|The Warden is at peace", "Goodbye");
                Assert(S.Quests.IsCompleted("mq_lanterns"), "The Lanterns Go Dark complete");
                ClaimRewards();

                foreach (var q in SliceQuests) Assert(S.Quests.IsCompleted(q), $"quest {q} complete");
                // the party grew on the way: levels, trained abilities, talents, loot, gold
                Assert(S.Main.Level >= 10, $"levelled from 1 to {S.Main.Level}");
                Assert(S.Main.Abilities.Count > startAbilities, $"the main character trained ({startAbilities} → {S.Main.Abilities.Count} abilities)");
                Assert(S.Main.Talents.Count > 0 && S.TalentPointsAvailable(S.Main) == 0, "talent points spent");
                Assert(Fights >= 12 && Trainings >= 2 && Rests >= 2, $"fights {Fights}, trainer visits {Trainings}, long rests {Rests}");
                Assert(Owned("lanternbough") || Owned("antler_lantern_charm"), "the Warden's loot was taken");
                Assert(S.PendingQuestRewards.Count == 0 && S.PendingLoot == null, "nothing left unclaimed");
                Assert(S.Mode == SessionMode.Exploration && !S.IsGameOver, "still playing");
                Assert(S.TrySaveGame(out var json, out var why), "save at the end: " + why);
                Assert(S.LoadGame(json, out var err) && S.LanternsRekindled && S.Quests.IsCompleted("mq_lanterns"), "the finished game reloads: " + err);
            }

            /// <summary>Centre of a region of the current map (looked up in data, so maps can be re-laid out).</summary>
            Vec2 RegionPos(string regionId)
            {
                var r = S.Map.Def.regions.Find(x => x.id == regionId);
                Assert(r != null, $"region {regionId} on {S.MapId}");
                return r.pos;
            }

            /// <summary>In the bags or worn by a roster member.</summary>
            bool Owned(string itemId) => S.CountItem(itemId) > 0 || S.Roster.Any(u => u.Equipment.Equipped.Any(kv => kv.Value.Id == itemId));

            // =========================================================== helpers: dialogue

            /// <summary>
            /// Plays the running dialogue: at each node with choices, picks the first visible choice containing the first
            /// script entry that matches (entries are used once); without a match, leaves politely (Goodbye / Continue).
            /// </summary>
            void Converse(params string[] script)
            {
                var todo = new List<string>(script);
                for (int guard = 0; guard < 200 && S.Dialogue.IsActive; guard++)
                {
                    var v = S.Dialogue.Current;
                    if (v == null) break;
                    if (v.CanContinue) { S.ContinueDialogue(); continue; }
                    int pick = -1;
                    for (int k = 0; k < todo.Count && pick < 0; k++)
                    {
                        foreach (var alt in todo[k].Split('|'))   // "a|b": whichever is offered
                        {
                            foreach (var c in v.Choices)
                                if (c.Text.IndexOf(alt, StringComparison.OrdinalIgnoreCase) >= 0) { pick = c.Index; break; }
                            if (pick >= 0) break;
                        }
                        if (pick >= 0) { Note($"  > {todo[k]}"); todo.RemoveAt(k); }
                    }
                    if (pick < 0)
                        foreach (var leave in new[] { "Goodbye", "(Continue.)", "Let's keep moving", "(Leave", "Rest", "Just passing by", "Never mind" })
                        {
                            var c = v.Choices.FirstOrDefault(x => x.Text.IndexOf(leave, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (c != null) { pick = c.Index; break; }
                        }
                    Assert(pick >= 0, $"no choice to pick at {v.DialogueId}/{v.NodeId}: {SessionTest.Describe(v)} (wanted: {string.Join(" | ", todo)})");
                    S.ChooseDialogue(pick);
                }
                Assert(!S.Dialogue.IsActive, "dialogue finished");
                Assert(todo.Count == 0 || todo.All(t => t.StartsWith("(Continue") || t == "Goodbye" || t.StartsWith("(Leave")),
                       $"every scripted choice was offered (left: {string.Join(" | ", todo)})");
                AfterDialogue();
            }

            /// <summary>Walks next to an NPC of the current map and talks to it.</summary>
            void TalkTo(string npcId, params string[] script)
            {
                var npc = S.VisibleNpcs().FirstOrDefault(n => n.npc == npcId);
                Assert(npc != null, $"{npcId} is on {S.MapId}");
                Go(npc.pos + new Vec2(-1.2f, -0.8f), stopNear: npc.pos, near: GameSession.InteractionRange);
                var r = S.TalkTo(npcId);
                Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"talk to {npcId}: {r}");
                Note($"talk {npcId} ({r.Id})");
                Converse(script);
            }

            /// <summary>Overlays opened by a dialogue (vendors, trainers) are closed by the caller; loot is taken.</summary>
            void AfterDialogue()
            {
                if (S.Mode == SessionMode.Combat) Fight();
                TakeLoot();
            }

            // =========================================================== helpers: movement & encounters

            /// <summary>Walks the leader to a point, fighting/answering whatever triggers on the way; returns when there.</summary>
            void Go(Vec2 dest, Vec2? stopNear = null, float near = 0.6f, int maxLegs = 30)
            {
                Vec2 goal = stopNear ?? dest;
                for (int leg = 0; leg < maxLegs; leg++)
                {
                    if (S.IsGameOver) Assert(false, "game over");
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(EncounterScript()); continue; }
                    TakeLoot();
                    if (Vec2.Distance(S.Leader.Position, goal) <= near) return;
                    var r = S.MoveLeader(dest);
                    if (r.Trigger.Stop)
                    {
                        Note($"  trigger {r.Trigger}");
                        if (r.Trigger.Kind == TriggerKind.Locked) Assert(false, "a locked transition on the way: " + r.Trigger.Id);
                        if (r.Trigger.Kind == TriggerKind.Travel) return;
                        continue;
                    }
                    if (Vec2.Distance(S.Leader.Position, goal) <= near + 0.3f) return;
                    if (!r.Moved) break;
                }
                Assert(Vec2.Distance(S.Leader.Position, goal) <= near + 1.5f, $"reached {goal} on {S.MapId} (at {S.Leader.Position})");
            }

            /// <summary>The script for the running encounter dialogue (none for other dialogues).</summary>
            string[] EncounterScript()
            {
                var owner = S.Dialogue.OwnerId ?? "";
                if (encounterScripts.TryGetValue(owner, out var script) && script != null)
                {
                    encounterScripts[owner] = new string[0];   // a re-triggered dialogue (peaceful branch already taken) just ends
                    return script;
                }
                return new string[0];
            }

            /// <summary>Prepares, then walks into an encounter; its dialogue (if any) is answered with the run's script for it;
            /// the fight (if any) is won by the party AI.</summary>
            void WalkInto(string encId)
            {
                var enc = S.Map.FindEncounter(encId);
                Assert(enc != null, $"{encId} on {S.MapId}");
                if (S.Map.IsEncounterDone(enc)) { Note($"{encId} already done"); return; }
                Assert(S.Map.IsEncounterAvailable(enc), $"{encId} is available");
                var preview = S.PreviewEncounter(encId);
                Assert(preview.Count == enc.enemies.Count, "nameplate preview");
                Recover(preview.Any(x => x.Rank >= CreatureRank.Elite));
                if (StopBefore == encId) { Snapshot = S.SaveGame(); throw new StopException(); }
                Note($"-> {encId}: {string.Join(", ", preview)} | party {SessionTest.PartyHp(S)}");
                for (int leg = 0; leg < 12 && !S.Map.IsEncounterDone(enc); leg++)
                {
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(EncounterScript()); continue; }
                    var r = S.MoveLeader(enc.pos);
                    if (r.Trigger.Stop) continue;
                    if (S.Mode == SessionMode.Exploration && !S.Map.IsEncounterDone(enc))
                    {
                        // standing inside an encounter whose dialogue ended peacefully without marking it done: step out and back
                        if (Vec2.Distance(S.Leader.Position, enc.pos) < enc.radius) S.MoveLeader(enc.pos + new Vec2(enc.radius + 2f, 0f));
                    }
                }
                Assert(S.Map.IsEncounterDone(enc), $"{encId} resolved (done flag {enc.doneFlag}: {S.Flags.IsSet(enc.doneFlag)})");
                TakeLoot();
            }

            void Travel(string transitionId, string expectMap)
            {
                var t = S.Map.Def.transitions.Find(x => x.id == transitionId);
                Assert(t != null, $"transition {transitionId} on {S.MapId}");
                Go(t.pos, near: 0.1f);
                if (S.MapId != expectMap)
                {
                    // spawned inside it? step out and walk back in
                    S.MoveLeader(t.pos + new Vec2(t.pos.x < S.Map.Def.width * 0.5f ? 3f : -3f, 0f));
                    Go(t.pos, near: 0.1f);
                }
                Assert(S.MapId == expectMap, $"travelled to {expectMap} (on {S.MapId})");
                Note($"== {expectMap}");
            }

            // =========================================================== helpers: combat & upkeep

            void Fight()
            {
                var b = S.Battle;
                Assert(b != null, "a battle");
                Fights++;
                int steps = 0;
                while (!b.IsOver && steps++ < 40000 && b.Round <= 100)
                {
                    var u = b.ActiveUnit;
                    Assert(u != null, "an active unit");
                    var step = S.RunAIStep();
                    Assert(step != null || b.IsOver, $"AI step for {u.Name} (needs player input: {b.NeedsPlayerInput})");
                }
                Assert(b.IsOver, $"battle {S.BattleEncounter?.id} ended (round {b.Round}, party {SessionTest.PartyHp(S)})");
                var enc = S.BattleEncounter?.id;
                var sum = S.FinishBattle();
                Assert(sum != null, "battle finished: " + S.LastError);
                Note($"   {enc}: {sum.Outcome} in {sum.Rounds} rounds, +{sum.Xp} xp, party {SessionTest.PartyHp(S)}");
                Assert(sum.Outcome == CombatEndKind.Victory || (sum.Outcome == CombatEndKind.Left && S.IsPracticeEncounter(Db.Maps[S.MapId].encounters.Find(e => e.id == enc))),
                       $"{enc}: the party AI must win (outcome {sum.Outcome}, level {S.Main.Level})");
                TakeLoot();
                Upkeep();
            }

            void TakeLoot()
            {
                if (S.PendingLoot != null) S.TakeAllLoot();
            }

            /// <summary>Talent points for everyone, upgrades from the bags.</summary>
            void Upkeep()
            {
                foreach (var u in S.Roster)
                    if (S.TalentPointsAvailable(u) > 0) S.AutoAllocateTalents(u);
                EquipUpgrades();
            }

            /// <summary>Equips bag items that are clearly better for a roster member (empty slot or higher item level).</summary>
            void EquipUpgrades()
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    bool any = false;
                    foreach (var it in S.Inventory.Items.ToList())
                    {
                        if (it.Def.equip == EquipType.None || !S.Inventory.Items.Contains(it)) continue;
                        foreach (var u in S.Party)
                        {
                            if (S.CanEquip(u, it) != null) continue;
                            var slot = EquipmentRules.ChooseSlot(u, it.Def);
                            if (!slot.HasValue) continue;
                            var cur = u.Equipment[slot.Value];
                            if (cur != null && !IsUpgrade(cur.Def, it.Def)) continue;
                            if (cur == null && (slot.Value == EquipSlot.MainHand || slot.Value == EquipSlot.OffHand) && u.Equipment.HasTwoHander) continue;
                            if (S.Equip(u, it, slot) == null) { any = true; Note($"   {u.Name} equips {it.Name}"); break; }
                        }
                    }
                    if (!any) break;
                }
            }

            /// <summary>Same kind of item (weapon type / armour class), higher item level × quality.</summary>
            static bool IsUpgrade(ItemDef cur, ItemDef d)
            {
                if (cur.equip != d.equip) return false;
                if (d.weaponType != cur.weaponType) return false;
                if (d.armorType < cur.armorType) return false;
                return Score(d) > Score(cur) + 0.5f;
            }

            static float Score(ItemDef d) => Math.Max(1, d.itemLevel) * (1f + (int)d.quality * 0.25f);

            /// <summary>Real-time recovery out of combat (Tick regenerates health/mana); a long rest before elite fights
            /// when possible (camp maps), else the inn in Lanternvale.</summary>
            void Recover(bool bigFight)
            {
                if (bigFight)
                {
                    if (S.CannotRestReason() == null) { CampRest(); return; }
                }
                for (int i = 0; i < 300 && NeedsRecovery(); i++) S.Tick(2f);
            }

            bool NeedsRecovery()
            {
                foreach (var u in S.PartyUnits())
                {
                    if (u.IsDeadOrDowned) return true;
                    if (u.HealthPct < 95f) return true;
                    if (u.MaxMana > 0 && u.PowerType == ResourceType.Mana && u.ManaPct < 90f) return true;
                }
                return false;
            }

            void CampRest()
            {
                Assert(S.CannotRestReason() == null, "can camp here: " + S.CannotRestReason());
                Assert(S.LongRest(), "long rest");
                Rests++;
                Note($"   camp rest on {S.MapId}");
                foreach (var u in S.Party)
                    if (u.ClassId == ClassId.Hunter && u.Pet == null && u.Knows("hunter_call_pet")) S.UseAbility(u, "hunter_call_pet");
            }

            void InnRest()
            {
                TalkTo("innkeeper_dorrit", "I'd like a room for the night");
                Rests++;
                Assert(S.PartyUnits().All(u => u.Health >= u.MaxHealth - 0.5f), "rested at the inn");
            }

            /// <summary>Trains the main character at its class trainer in Lanternvale (companions train themselves).</summary>
            void Train()
            {
                var trainer = Db.Npcs.Values.FirstOrDefault(n => n.trains != null && n.trains.Contains(S.Main.ClassId));
                Assert(trainer != null, "a trainer for " + S.Main.ClassId);
                if (S.MapId != "lanternvale") return;
                if (S.TrainerOffers(S.Main).Count == 0 && visitedTrainer.Contains(trainer.id + S.Main.Level)) return;
                TalkTo(trainer.id, "I'd like to train");
                Assert(S.ActiveTrainer != null && S.CanTrainHere(S.Main), "trainer window open");
                int before = S.Main.Abilities.Count;
                int n = S.TrainAll(S.Main);
                Trainings++;
                visitedTrainer.Add(trainer.id + S.Main.Level);
                Note($"   trained {n} ranks at level {S.Main.Level} ({S.Main.Abilities.Count - before} new abilities, {S.Inventory.Gold}c left)");
                S.CloseTrainer();
                Upkeep();
            }

            void OpenChest(string chestId)
            {
                var c = S.Map.FindChest(chestId);
                Assert(c != null, $"chest {chestId} on {S.MapId}");
                Go(c.pos + new Vec2(-1f, -0.6f), stopNear: c.pos, near: GameSession.InteractionRange);
                var r = S.OpenChest(chestId);
                Assert(r.Ok && r.Kind == InteractKind.Loot, $"open {chestId}: {r}");
                TakeLoot();
                Upkeep();
            }

            /// <summary>Locked chests: the best lock-picker retries until the lock gives (or gives up after a few tries).</summary>
            void TryLock(string chestId)
            {
                var c = S.Map.FindChest(chestId);
                Assert(c != null, $"chest {chestId} on {S.MapId}");
                Go(c.pos + new Vec2(-1f, -0.6f), stopNear: c.pos, near: GameSession.InteractionRange);
                var r = S.OpenChest(chestId);
                for (int i = 0; i < 20 && r.Kind == InteractKind.Locked && !S.Map.IsChestOpened(chestId); i++)
                {
                    var chk = S.PickLock(chestId);
                    Assert(chk != null, "a lock check");
                }
                Note($"   {chestId}: {(S.Map.IsChestOpened(chestId) ? "opened" : "stayed locked")}");
                TakeLoot();
                Upkeep();
            }

            void ClaimRewards()
            {
                foreach (var q in S.PendingQuestRewards.ToList())
                {
                    var choices = S.QuestRewardChoices(q);
                    Assert(choices.Count > 0, "reward choices for " + q);
                    var pick = choices.FirstOrDefault(d => S.Party.Any(u => EquipmentRules.CannotUseReason(u, d) == null)) ?? choices[0];
                    Assert(S.ClaimQuestReward(q, pick.id) == null, $"claim {pick.id} for {q}");
                    Note($"   reward {q}: {pick.name}");
                }
                Upkeep();
            }
        }
    }
}
