// The Hollow Heart raid (Docs/Expansion.md §8, builder "r1", prefix r1): the map is entered through EnterRaid and every spawn,
// exit, NPC, chest and encounter can be reached; the thorn gates wither boss by boss; the hidden offerings are revealed by
// the corner's Perception check or Hinoki's hint; Quill's and Hinoki's quests are scripted to completion through the real
// GameSession at the band's top level with a raid of ten (talk, walk, props, fights won by the party AI, both endings of
// the choices); every boss is beaten by a fitting raid of ten at levels 21 and 22 (two of three pulls), its raid-wide casts
// landing (they cannot be interrupted), and never by a fitting five; the quest markers follow the flag and level gates.
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
    public static class TestsContentR1
    {
        const string Raid = "raid_hollow_heart", Gate = "mq2_drowned_lanterns_done";
        const string QClear = "r1_into_the_hollow", QLights = "r1_lights_that_went_down", QLore = "r1_seed_on_the_ash";
        const string FThornmaw = "enc_enc_r1_thornmaw", FTwins = "enc_enc_r1_twins", FMire = "enc_enc_r1_mother_mire", FHeart = "enc_enc_r1_hollow_heart";
        static readonly string[] Bosses = { "enc_r1_thornmaw", "enc_r1_twins", "enc_r1_mother_mire", "enc_r1_hollow_heart" };

        /// <summary>A scripted raid leader: walks, talks, pokes props and lets the raid's AI fight (TestsContentMf style).</summary>
        sealed class Raider
        {
            public readonly GameSession S;
            public readonly List<string> Log = new List<string>();
            public readonly Dictionary<string, string[]> EncounterScripts = new Dictionary<string, string[]>(StringComparer.Ordinal);
            public readonly Dictionary<string, (float minHp, int rounds, int deaths)> Fights = new Dictionary<string, (float, int, int)>(StringComparer.Ordinal);
            public long Xp;   // XP the main character earned (fights, dialogue and quest rewards)

            public Raider(ClassId main, int level, ulong seed, int size = 10, bool gate = true) : this(main, level, seed, RaidTest.Ids(size), gate) { }

            /// <summary>The main character and these companions (<paramref name="ids"/> starts with GameSession.MainId).</summary>
            public Raider(ClassId main, int level, ulong seed, List<string> ids, bool gate = true)
            {
                S = RaidTest.Game(main, level, seed);
                S.Settings.CompanionAutoPlay = true;
                if (gate) S.Flags.Set(Gate);
                Assert(S.EnterRaid(Raid, "from_mirefen", ids, true) == null, "enter the raid: " + S.LastError);
                Assert(S.InRaid && S.MapId == Raid && S.Party.Count == ids.Count, "a raid of " + ids.Count);
                S.SetAutoPlay(S.Main, true);
            }

            void Note(string s) => Log.Add(s);
            string Trace => string.Join("\n      ", Log.Skip(Math.Max(0, Log.Count - 25)));
            public long Total => Cumulative(S.Main.Level, S.Main.Xp);

            /// <summary>Plays the running conversation: the first scripted choice offered (alternatives "a|b"), else an exit.</summary>
            public void Converse(params string[] script)
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
                        foreach (var alt in todo[k].Split('|'))
                        {
                            var c = v.Choices.FirstOrDefault(x => x.Text.IndexOf(alt, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (c != null) { pick = c.Index; break; }
                        }
                        if (pick >= 0) { Note("  > " + todo[k]); todo.RemoveAt(k); }
                    }
                    if (pick < 0)
                        foreach (var leave in new[] { "Goodbye", "(Step back", "Not yet" })
                        {
                            var c = v.Choices.FirstOrDefault(x => x.Text.IndexOf(leave, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (c != null) { pick = c.Index; break; }
                        }
                    if (pick < 0 && v.Choices.Count > 0) pick = v.Choices[v.Choices.Count - 1].Index;
                    Assert(pick >= 0, $"no choice at {v.DialogueId}/{v.NodeId}: {SessionTest.Describe(v)} (wanted {string.Join(" | ", todo)})\n      {Trace}");
                    S.ChooseDialogue(pick);
                }
                Assert(!S.Dialogue.IsActive, "dialogue finished");
                Assert(todo.Count == 0, $"every scripted choice was offered (left: {string.Join(" | ", todo)})\n      {Trace}");
                if (S.Mode == SessionMode.Combat) Fight();
                TakeLoot();
            }

            public void TalkTo(string npcId, params string[] script)
            {
                var npc = S.VisibleNpcs().FirstOrDefault(n => n.npc == npcId);
                Assert(npc != null, $"{npcId} stands on {S.MapId}");
                Go(npc.pos, GameSession.InteractionRange - 0.4f);
                var r = S.TalkTo(npcId);
                Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"talk to {npcId}: {r.Message}");
                Note("talk " + npcId);
                Converse(script);
            }

            public void Use(string interactId, params string[] script)
            {
                var p = S.MapDef.props.FirstOrDefault(x => x.interact == interactId && S.Map.IsPropVisible(x));
                Assert(p != null, $"prop {interactId} is there");
                Go(p.pos, GameSession.InteractionRange + 0.3f);
                var r = S.InteractProp(interactId);
                Assert(r.Ok && r.Kind == InteractKind.Dialogue, $"use {interactId}: {r.Message}");
                Note("use " + interactId);
                Converse(script);
            }

            /// <summary>Walks to within <paramref name="near"/> of a point, winning every fight on the way.</summary>
            public void Go(Vec2 dest, float near = 0.8f, int maxLegs = 40)
            {
                for (int leg = 0; leg < maxLegs; leg++)
                {
                    Assert(!S.IsGameOver, "game over\n      " + Trace);
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(EncounterScript()); continue; }
                    TakeLoot();
                    if (Vec2.Distance(S.Leader.Position, dest) <= near) return;
                    var r = S.MoveLeader(dest);
                    if (r.Trigger.Stop)
                    {
                        Assert(r.Trigger.Kind != TriggerKind.Locked, "a locked way: " + r.Trigger.Id);
                        if (r.Trigger.Kind == TriggerKind.Travel || r.Trigger.Kind == TriggerKind.RaidGate) return;
                        continue;
                    }
                    if (Vec2.Distance(S.Leader.Position, dest) <= near + 0.3f) return;
                    if (!r.Moved) break;
                }
                Assert(Vec2.Distance(S.Leader.Position, dest) <= near + 1.5f, $"reached {dest} on {S.MapId} (at {S.Leader.Position})\n      {Trace}");
            }

            string[] EncounterScript()
            {
                var owner = S.Dialogue.OwnerId ?? "";
                if (EncounterScripts.TryGetValue(owner, out var script) && script != null)
                {
                    EncounterScripts[owner] = new string[0];
                    return script;
                }
                return new string[0];
            }

            public void WalkInto(string encId, params string[] script)
            {
                var enc = S.Map.FindEncounter(encId);
                Assert(enc != null, $"{encId} on {S.MapId}");
                if (script.Length > 0) EncounterScripts[encId] = script;
                if (S.Map.IsEncounterDone(enc)) { Note(encId + " already done (met on the way)"); return; }
                Assert(S.Map.IsEncounterAvailable(enc), $"{encId} is there to meet");
                Refresh();
                for (int leg = 0; leg < 16 && !S.Map.IsEncounterDone(enc); leg++)
                {
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(EncounterScript()); continue; }
                    var r = S.MoveLeader(enc.pos);
                    if (r.Trigger.Stop) continue;
                    if (S.Mode == SessionMode.Exploration && !S.Map.IsEncounterDone(enc) && Vec2.Distance(S.Leader.Position, enc.pos) < enc.radius)
                        S.MoveLeader(enc.pos + new Vec2(enc.radius + 2.5f, 0f));
                }
                Assert(S.Map.IsEncounterDone(enc), $"{encId} resolved\n      {Trace}");
            }

            public void Fight()
            {
                var b = S.Battle;
                Assert(b != null, "a battle");
                var id = S.BattleEncounter?.id ?? "?";
                var party = b.Units.Where(u => u.Team == b.PlayerTeam && u.IsCharacter).ToList();
                float max = party.Sum(u => u.MaxHealth), min = 1f;
                int steps = 0;
                while (!b.IsOver && steps++ < 200000 && b.Round <= 100)
                {
                    var step = S.RunAIStep();
                    Assert(step != null || b.IsOver, $"AI step in {id}");
                    min = Math.Min(min, party.Sum(u => Math.Max(0f, u.Health)) / max);
                }
                int deaths = party.Count(u => !u.IsAlive);
                long before = Total;
                var sum = S.FinishBattle();
                Assert(sum != null && sum.Outcome == CombatEndKind.Victory, $"{id}: the raid wins (outcome {sum?.Outcome}, round {b.Round}, level {S.Main.Level})\n      {Trace}");
                Xp += Total - before;
                Fights[id] = (min, sum.Rounds, deaths);
                Note($"   {id}: won in {sum.Rounds} rounds, lowest raid health {min:P0}, {deaths} down, +{sum.Xp} xp");
                TakeLoot();
            }

            public void TakeLoot()
            {
                if (S.PendingLoot != null) S.TakeAllLoot();
                foreach (var q in S.Quests.PendingRewardChoices.ToList())
                {
                    var choice = Db.Quests[q].rewards.choiceItems;
                    Assert(S.ClaimQuestReward(q, choice[0]) == null, "claim " + q);
                }
            }

            /// <summary>Full health and mana between pulls (the raid drinks Quill's potions and sits down).</summary>
            public void Refresh()
            {
                foreach (var u in S.PartyUnits()) u.RestoreFull();
            }

            public string Stage(string q) => S.Quests.GetStage(q);

            /// <summary>Runs a talk/use step and adds the XP it gave (dialogue GiveXP and quest rewards).</summary>
            public void Counting(Action a)
            {
                long before = Total;
                a();
                Xp += Total - before;
            }
        }

        static long Cumulative(int level, int xp)
        {
            long t = xp;
            for (int l = 1; l < level; l++) t += Progression.XpToNextLevel(Db, l);
            return t;
        }

        /// <summary>Marks every trash pack of the current map done (the bosses stay).</summary>
        static void PacifyTrash(GameSession s)
        {
            foreach (var e in s.MapDef.encounters) if (!Bosses.Contains(e.id)) s.Map.MarkEncounterDone(e.id);
        }

        // ===================================================================================================== the map

        [Test]
        public static void Map_EnterTheRaid_ReachEverything_DressedAndLit()
        {
            var m = Db.Maps[Raid];
            foreach (var sp in m.spawns)
            {
                var s = RaidTest.Game(ClassId.Hunter, 22, 11);
                Assert(s.EnterRaid(Raid, sp.id, RaidTest.Ids(10), true) == null && s.MapId == Raid, $"enter at {sp.id}: {s.LastError}");
                Assert(s.Nav.IsWalkable(s.Leader.Position, NavAgent.Default.IgnoringAllUnits()), $"{sp.id}: on walkable ground");
                s.SetPartyPositions(s.Map.SpawnPosition(sp.id));
                Assert(s.UseTransition("to_mirefen").Ok && s.MapId == "mirefen" && !s.InRaid, "and out again to Mirefen");
            }
            var bad = TestsMapsReachable.Unreachable(m);
            Assert(bad.Count == 0, "everything reachable from default: " + string.Join("; ", bad));

            Assert(m.biome == "hollow_heart" && m.environment == "cave" && m.raidSize == 10 && !m.restArea && m.levelMin == 21 && m.levelMax == 23,
                "a hollow_heart cave raid of ten, band 21-23, no rest");
            Assert(m.fill >= 0.4f && m.fill <= 0.6f && m.paths.Count >= 4 && m.water.Count >= 4, "indoor fill, paths and pools");
            Assert(m.props.Count >= 200 && m.props.Count(p => p.light != null) >= 60, $"a dressed, lit cavern ({m.props.Count} props, {m.props.Count(p => p.light != null)} lights)");
            Assert(m.regions.Count(r => !string.IsNullOrEmpty(r.name)) >= 6, "named regions for the Map panel");

            // 4-6 trash packs of Elite rootlings, hounds and knights; four boss encounters
            var trash = m.encounters.Where(e => !Bosses.Contains(e.id)).ToList();
            Assert(trash.Count >= 4 && trash.Count <= 6, "4-6 trash packs: " + trash.Count);
            foreach (var e in trash)
                Assert(e.enemies.Count >= 3 && e.enemies.All(x => Db.Creatures[x.creature].rank == CreatureRank.Elite), $"{e.id}: a pack of Elites");
            var sprites = trash.SelectMany(e => e.enemies).Select(x => Db.Creatures[x.creature].sprite).Distinct().ToList();
            foreach (var key in new[] { "cr_rootling", "cr_hollow_knight", "cr_blight_hound" }) Assert(sprites.Contains(key), "trash uses " + key);
            foreach (var c in Db.Creatures.Values.Where(c => c.id.StartsWith("cr_r1_")))
            {
                Assert(c.scaleToParty && c.levelOffset >= -1 && c.levelOffset <= 2 && c.levelFloor >= 21 && c.levelCap == 24, $"{c.id}: scaled, floored and capped at 24");
                Assert(!string.IsNullOrEmpty(c.material) && !string.IsNullOrEmpty(c.voice), $"{c.id}: material and voice");
                if (c.rank == CreatureRank.Elite) Assert(c.lootTable == "lt_r1_trash", $"{c.id}: trash loot");
            }
            // bosses: loot, one twin carries the table, the heart never moves; lights round every boss
            var loot = new Dictionary<string, string> { ["cr_r1_thornmaw"] = "lt_r1_thornmaw", ["cr_r1_mother_mire"] = "lt_r1_mother_mire", ["cr_r1_hollow_heart"] = "lt_r1_hollow_heart" };
            foreach (var kv in loot) Assert(Db.Creatures[kv.Key].rank == CreatureRank.Boss && Db.Creatures[kv.Key].lootTable == kv.Value, kv.Key + " drops " + kv.Value);
            var twins = new[] { Db.Creatures["cr_r1_twin_sorrow"], Db.Creatures["cr_r1_twin_solace"] };
            Assert(twins.All(t => t.rank == CreatureRank.Boss) && twins.Count(t => t.lootTable == "lt_r1_twins") == 1 && twins.Count(t => !string.IsNullOrEmpty(t.lootTable)) == 1,
                "only one of the Twins carries lt_r1_twins");
            var twinsEnc = m.encounters.First(e => e.id == "enc_r1_twins");
            Assert(twinsEnc.enemies.Select(x => x.creature).OrderBy(x => x).SequenceEqual(new[] { "cr_r1_twin_solace", "cr_r1_twin_sorrow" }), "the Twins in one encounter");
            var heart = Db.Creatures["cr_r1_hollow_heart"];
            Assert(heart.moveSpeed == 0f && heart.ranged && heart.size >= 7.5f && heart.size <= 8f, "the Hollow Heart is static, ranged, 7.5-8 m");
            Assert(Db.Creatures["cr_r1_thornmaw"].size == 6f, "Thornmaw at 6 m");
            foreach (var id in Bosses)
            {
                var e = m.encounters.First(x => x.id == id);
                int lights = m.props.Count(p => p.light != null && string.IsNullOrEmpty(p.requireFlag) && Vec2.Distance(p.pos, e.pos) <= 11f);
                Assert(lights >= 3, $"{id}: lit ({lights} lights within 11 m)");
                Assert(!string.IsNullOrEmpty(e.dialogue), $"{id}: a word before the fight");
            }
            // every boss: a telegraphed big cast, an enrage, adds; a healer check (raid-wide damage) in three of them
            foreach (var cid in new[] { "cr_r1_thornmaw", "cr_r1_twin_sorrow", "cr_r1_mother_mire", "cr_r1_hollow_heart" })
            {
                var c = Db.Creatures[cid];
                var abs = c.abilities.Select(a => Db.Abilities[a.ability]).ToList();
                Assert(abs.Any(a => a.tags.Contains("Telegraph") && a.castTime >= 3f), cid + ": a telegraphed big cast");
                Assert(c.abilities.Any(a => a.condition.StartsWith("selfHpBelow") && Db.Abilities[a.ability].aiHint == "Buff"), cid + ": an enrage");
                Assert(abs.Any(a => a.effects.Any(e => e.type == EffectType.Summon)), cid + ": adds");
            }
            Assert(Db.Creatures["cr_r1_twin_solace"].abilities.Any(a => Db.Abilities[a.ability].tags.Contains("Heal")), "Solace mends her sister");
        }

        [Test]
        public static void Gates_TheThornsWitherBossByBoss()
        {
            var m = Db.Maps[Raid];
            string[] all = { FThornmaw, FTwins, FMire, FHeart };
            Vec2 EncPos(string id) => m.encounters.First(e => e.id == id).pos;
            for (int done = 0; done <= 3; done++)
            {
                var set = new HashSet<string>(all.Take(done));
                var nav = new NavGrid(m, null, expr => TestFlags(expr, set));
                var agent = NavAgent.Default;
                var start = nav.ClampToWalkable(m.spawns.First(s => s.id == "default").pos, agent);
                bool Reach(Vec2 p) => nav.FindPathToRange(start, p, 2f, agent).Status == PathStatus.Complete;
                for (int i = 0; i < Bosses.Length; i++)
                    Assert(Reach(EncPos(Bosses[i])) == (i <= done), $"{done} bosses down: {Bosses[i]} {(i <= done ? "open" : "behind the thorns")}");
                Assert(Reach(m.npcs.First(n => n.npc == "r1_quartermaster").pos) && Reach(m.transitions[0].pos), "the camp and the way out are always open");
            }
        }

        /// <summary>A flag expression ('&amp;'-joined, '!' negates) against a set of set flags.</summary>
        static bool TestFlags(string expr, HashSet<string> set)
        {
            if (string.IsNullOrEmpty(expr)) return true;
            foreach (var part in expr.Split('&'))
            {
                var t = part.Trim();
                if (t.Length == 0) continue;
                bool neg = t[0] == '!';
                var k = neg ? t.Substring(1) : t;
                if (set.Contains(k) == neg) return false;
            }
            return true;
        }

        [Test]
        public static void Offerings_RevealedByPerception_OrHinokisHint()
        {
            var m = Db.Maps[Raid];
            var reg = m.regions.First(r => r.id == "reg_r1_offerings");
            Assert(reg.check != null && reg.check.skill == SkillCheck.Perception && reg.check.dc >= 12 && reg.check.dc <= 16 && reg.checkFlag == "r1_found_offerings",
                "a Perception DC 12-16 check finds the offerings");
            var chest = m.chests.First(c => c.id == "chest_r1_offerings");
            Assert(chest.requireFlag == "r1_found_offerings" && reg.pos.x - reg.size.x / 2 <= chest.pos.x && chest.pos.x <= reg.pos.x + reg.size.x / 2, "the chest sits in the checked corner");
            int found = 0, missed = 0;
            for (ulong seed = 1; seed <= 30 && (found == 0 || missed == 0); seed++)
            {
                var s = RaidTest.Game(ClassId.Warrior, 22, 500 + seed);
                Assert(s.EnterRaid(Raid, "from_mirefen", RaidTest.Ids(2), true) == null, "a raid of two");
                foreach (var f in new[] { FThornmaw }) s.Flags.Set(f);
                RaidTest.Pacify(s);
                s.TakeEvents();
                Assert(!s.Map.IsChestAvailable(chest), "hidden at first");
                SessionTest.WalkTo(s, reg.pos + new Vec2(0f, -2.4f));
                var roll = s.TakeEvents().FirstOrDefault(e => e.Kind == SessionEventKind.SkillCheck && e.Id == reg.id);
                Assert(roll != null, "walking into the corner rolls the check");
                Assert(s.Flags.IsSet("r1_found_offerings") == roll.Check.Success && s.Map.IsChestAvailable(chest) == roll.Check.Success, "the flag and the chest follow the roll");
                if (roll.Check.Success)
                {
                    found++;
                    SessionTest.WalkTo(s, chest.pos + new Vec2(0f, -1.6f));
                    var r = s.OpenChest(chest.id);
                    Assert(r.Ok, "open the offerings: " + r.Message);
                }
                else
                {
                    missed++;
                    var p = new Raider(ClassId.Priest, 22, 900 + seed, 2);
                    p.TalkTo("r1_hinoki", "Is anything else hidden");
                    Assert(p.S.Flags.IsSet("r1_found_offerings") && p.S.Map.IsChestAvailable(chest), "Hinoki's hint reveals the offerings");
                }
            }
            Assert(found > 0 && missed > 0, $"the Perception check can pass and fail ({found} found, {missed} missed)");
        }

        // ===================================================================================================== the quests

        [Test]
        public static void RaidClear_EveryQuest_AtTheBandsTop_GroveAndSolace()
        {
            var p = new Raider(ClassId.Paladin, 23, 2323);
            var s = p.S;
            p.Counting(() => p.TalkTo("r1_quartermaster", "What's down there", "We'll clear it"));
            p.Counting(() => p.TalkTo("r1_quartermaster", "Granny Sen says", "I'll free every light"));
            p.Counting(() => p.TalkTo("r1_hinoki", "What happened to this grove", "I'll find out where"));
            Assert(s.Quests.IsActive(QClear) && s.Quests.IsActive(QLights) && s.Quests.IsActive(QLore), "three quests");
            Assert(s.QuestMarkerOf("r1_quartermaster").Kind != QuestMarker.Available && s.QuestMarkerOf("r1_hinoki").Kind != QuestMarker.Available, "nothing more to offer");

            // the Root Gallery: the ash, the first light, the two packs
            p.Counting(() => p.Use("r1_ash_drift", "Read the glint|This isn't fen ash|Grey snow|(Scoop"));
            Assert(p.Stage(QLore) == "twins", "the ash examined");
            p.WalkInto("enc_r1_gallery_rootlings");
            p.WalkInto("enc_r1_gallery_hounds");
            p.Counting(() => p.Use("r1_captive_light_1", "Pray for the lost light"));
            Assert(s.Flags.IsSet("r1_light_1"), "light 1 free");
            // Thornmaw, and the light behind its den
            p.WalkInto("enc_r1_thornmaw", "(Draw your weapons");
            Assert(p.Stage(QClear) == "twins" && s.Flags.IsSet(FThornmaw), "Thornmaw falls: " + p.Stage(QClear));
            p.Counting(() => p.Use("r1_captive_light_2", "(Cut the thorns slowly"));
            // the Weeping Pools: the vigil, the Twins (their echo: Solace stays), the wardens, the third light
            p.WalkInto("enc_r1_vigil_knights");
            p.WalkInto("enc_r1_twins", "Then we'll stop you");
            Assert(p.Stage(QClear) == "mire", "the Twins at rest");
            Assert(s.VisibleNpcs().Any(n => n.npc == "r1_twin_echo"), "their echo kneels by the water");
            p.Counting(() => p.TalkTo("r1_twin_echo", "What happened here", "Will you stay"));
            Assert(s.Flags.IsSet("r1_solace_stays") && p.Stage(QLore) == "husk" && !s.VisibleNpcs().Any(n => n.npc == "r1_twin_echo"), "Solace stays; the echo is gone");
            p.WalkInto("enc_r1_pool_wardens");
            p.Counting(() => p.Use("r1_captive_light_3", "Pray for the lost light"));
            // Mire Hollow: the hounds, Mother Mire, the last light, her larder
            p.WalkInto("enc_r1_mire_hounds");
            p.WalkInto("enc_r1_mother_mire", "What did the heart promise", "curtseying alone");
            Assert(p.Stage(QClear) == "heart", "Mother Mire defeated");
            p.Counting(() => p.Use("r1_captive_light_4", "Pray for the lost light"));
            Assert(p.Stage(QLights) == "release", "four lights free: " + p.Stage(QLights));
            // the Heart Chamber: the guard, Solace's light, the Hollow Heart
            p.WalkInto("enc_r1_heart_guard");
            Assert(s.VisibleNpcs().Any(n => n.npc == "r1_solace_light"), "Solace waits by the heart");
            foreach (var u in s.PartyUnits()) u.Health = Math.Max(1f, u.MaxHealth * 0.4f);
            p.TalkTo("r1_solace_light", "(Sit a while in her light");
            Assert(s.PartyUnits().All(u => u.Health >= u.MaxHealth - 0.5f), "her light heals the raid");
            p.WalkInto("enc_r1_hollow_heart", "(End it");
            Assert(p.Stage(QClear) == "report" && s.Flags.IsSet(FHeart), "the heart is still");
            // the husk, and the lights in the heart lantern: the grove
            p.Counting(() => p.Use("r1_seed_husk", "(Take the husk"));
            Assert(s.CountItem("r1_hollow_seed_husk") == 1 && p.Stage(QLore) == "return", "the husk");
            int darkBefore = s.MapDef.props.Count(x => x.art == "prop_spirit_lantern" && s.Map.IsPropVisible(x));
            p.Counting(() => p.Use("r1_heart_lantern", "Stay, and light the grove"));
            Assert(s.Flags.IsSet("r1_grove_relit") && p.Stage(QLights) == "return", "the lights stay to light the grove");
            int lit = s.MapDef.props.Count(x => x.art == "prop_spirit_lantern" && s.Map.IsPropVisible(x));
            Assert(lit >= darkBefore + 8 && !s.MapDef.props.Any(x => x.art == "prop_spirit_lantern_dark" && s.Map.IsPropVisible(x)), $"every drowned lantern burns ({darkBefore} → {lit})");
            // home to the camp: Quill and Hinoki
            p.Counting(() => p.TalkTo("r1_quartermaster", "The Hollow Heart has stopped beating"));
            p.Counting(() => p.TalkTo("r1_quartermaster", "They stayed to light the grove"));
            Assert(s.Quests.IsCompleted(QClear) && s.Quests.IsCompleted(QLights) && s.CountItem("r1_grovelight_charm") == 1, "Quill pays, with the kodama's charm");
            p.Counting(() => p.TalkTo("r1_hinoki", "I found the husk"));
            Assert(s.Quests.IsCompleted(QLore) && s.Flags.IsSet("r1_seed_lore_known") && s.CountItem("r1_hollow_seed_husk") == 0, "Seed on the Ash: the ashwyrm named");
            var done = Db.Dialogues["dlg_r1_hinoki"].nodes.First(n => n.id == "h_done").text;
            Assert(done.Contains("Vyrmathra") && done.Contains("Skyreach") && done.Contains("roost"), "the trail leads to Ashwyrm's Roost");
            p.TalkTo("r1_hinoki", "The lights stayed");
            Assert(s.Flags.IsSet("r1_hinoki_thanked"), "Hinoki thanks you");

            foreach (var id in Bosses)
            {
                var f = p.Fights[id];
                Console.WriteLine($"    r1 clear at 23: {id}: {f.rounds} rounds, lowest raid health {f.minHp:P0}, {f.deaths} down");
            }
            Assert(p.Fights["enc_r1_hollow_heart"].rounds >= 10, "the Hollow Heart is a long fight: " + p.Fights["enc_r1_hollow_heart"].rounds);
            Console.WriteLine($"    r1 clear at 23: the main character earned {p.Xp} XP (fights and quests), level {s.Main.Level}");
            Assert(p.Xp >= 30000 && p.Xp <= 90000, "a full clear is worth about two levels of the band: " + p.Xp);
            Assert(s.InRaid && s.MapId == Raid, "the raid goes on");
            p.Go(s.MapDef.transitions.First(t => t.id == "to_mirefen").pos, 0.2f);
            Assert(s.MapId == "mirefen" && !s.InRaid, "and walks home to Mirefen");
        }

        [Test]
        public static void TheOtherEndings_TheTwinsRestTogether_TheLightsGoHome()
        {
            var p = new Raider(ClassId.Mage, 23, 3131, 3);
            var s = p.S;
            p.TalkTo("r1_quartermaster", "What's down there", "We'll clear it");
            p.TalkTo("r1_quartermaster", "Granny Sen says", "I'll free every light");
            p.TalkTo("r1_hinoki", "What happened to this grove", "I'll find out where");
            PacifyTrash(s);
            s.Flags.Set(FThornmaw);
            s.Flags.Set(FTwins);
            Assert(p.Stage(QClear) == "mire", "Defeat objectives follow the done flags: " + p.Stage(QClear));
            p.Use("r1_ash_drift", "Taste the magic");
            p.TalkTo("r1_twin_echo", "Who were you", "What happened here", "Lie down together");
            Assert(s.Flags.IsSet("r1_twins_rested") && s.CountItem("r1_twinned_tear") == 1 && !s.Flags.IsSet("r1_solace_stays"), "the Twins rest together and leave a tear");
            for (int i = 1; i <= 4; i++) s.Flags.Set("r1_light_" + i);
            s.Flags.Set(FMire);
            s.Flags.Set(FHeart);
            Assert(!s.VisibleNpcs().Any(n => n.npc == "r1_solace_light"), "no Solace by the heart");
            p.Use("r1_heart_lantern", "Go home");
            Assert(s.Flags.IsSet("r1_lights_sent_home") && !s.Flags.IsSet("r1_grove_relit"), "the lights go home");
            Assert(s.MapDef.props.Any(x => x.art == "prop_spirit_lantern_dark" && s.Map.IsPropVisible(x)), "the grove's own lanterns stay dark");
            p.Use("r1_seed_husk", "(Take the husk");
            p.TalkTo("r1_quartermaster", "The Hollow Heart has stopped beating");
            p.TalkTo("r1_quartermaster", "I sent them home");
            Assert(s.Quests.IsCompleted(QLights) && s.CountItem("r1_fenlight_lantern") == 1 && s.CountItem("r1_grovelight_charm") == 0, "Bo's lantern for bringing them home");
            p.TalkTo("r1_hinoki", "I found the husk");
            p.TalkTo("r1_hinoki", "gone home to the fen");
            Assert(s.Quests.IsCompleted(QLore) && s.Quests.IsCompleted(QClear), "all three done");
        }

        // ===================================================================================================== the bosses

        /// <summary>A fitting raid of 10: two tanks beside the main, three healers, four damage dealers (as TestsContentR2).</summary>
        static readonly string[] Ten = { "bruna", "ysolde", "liora", "seren", "nanami", "rook", "pip", "lys", "morwen" };
        /// <summary>A fitting party of 5: the main, a tank, a healer and two damage dealers.</summary>
        static readonly string[] Five = { "bruna", "liora", "rook", "lys" };

        /// <summary>What must land in a won fight (CastComplete or AbilityUsed by the boss's side), and what must at least be
        /// attempted (the casts the raid is meant to interrupt: Solace's Mending, Drink the Light).</summary>
        static readonly Dictionary<string, (string[] land, string[] attempt)> Mechanics = new Dictionary<string, (string[], string[])>
        {
            ["enc_r1_thornmaw"] = (new[] { "cr_r1_bramble_burst", "cr_r1_call_thornlings", "cr_r1_rootbound_frenzy" }, new string[0]),
            ["enc_r1_twins"] = (new[] { "cr_r1_veil_of_tears", "cr_r1_weeping_wisps" }, new[] { "cr_r1_solace_mending" }),
            ["enc_r1_mother_mire"] = (new[] { "cr_r1_bog_eruption", "cr_r1_call_mirespawn", "cr_r1_frog_hex" }, new string[0]),
            ["enc_r1_hollow_heart"] = (new[] { "cr_r1_sprout_seedlings", "cr_r1_heartbeat", "cr_r1_final_beat" }, new[] { "cr_r1_drink_the_light" }),
        };

        sealed class PullResult
        {
            public BattleOutcome Outcome;
            public int Rounds, Down;
            public float MinHp = 1f;
            public readonly HashSet<string> Landed = new HashSet<string>(), Started = new HashSet<string>();
            public string Took = "";
        }

        /// <summary>One pull of a boss by the main (a Warrior) and these companions, every member on auto-play.</summary>
        static PullResult Pull(string encId, string[] companions, int level, ulong seed)
        {
            var ids = new List<string> { GameSession.MainId };
            ids.AddRange(companions);
            var r = new Raider(ClassId.Warrior, level, seed, ids);
            var s = r.S;
            RaidTest.Pacify(s);
            s.Map.ResetEncounter(encId);
            var enc = s.Map.FindEncounter(encId);
            s.SetPartyPositions(enc.pos + new Vec2(-enc.radius - 3f, -1.5f));
            var b = s.StartEncounter(encId);
            Assert(b != null, "fight " + encId + ": " + s.LastError);
            var party = b.Units.Where(u => u.Team == b.PlayerTeam && u.IsCharacter).ToList();
            float max = party.Sum(u => u.MaxHealth);
            var res = new PullResult();
            int guard = 0;
            while (!b.IsOver && b.Round <= 80 && guard++ < 300000)
            {
                var u = b.ActiveUnit;
                if (u == null) break;
                AI.RunTurn(b, u);
                if (b.ActiveUnit == u && !b.IsOver) b.EndTurn(u);
                res.MinHp = Math.Min(res.MinHp, party.Sum(x => Math.Max(0f, x.Health)) / max);
            }
            res.Outcome = b.Outcome;
            res.Rounds = b.Round;
            res.Down = party.Count(u => !u.IsAlive || u.IsDeadOrDowned);
            foreach (var e in b.Events)
            {
                if (e.Source == null || e.Source.Team == b.PlayerTeam || string.IsNullOrEmpty(e.AbilityId)) continue;
                if (e.Type == CombatEventType.CastComplete || e.Type == CombatEventType.AbilityUsed) res.Landed.Add(e.AbilityId);
                else if (e.Type == CombatEventType.CastStart) res.Started.Add(e.AbilityId);
            }
            res.Took = DamageBreakdown(b);
            return res;
        }

        [Test]
        public static void Bosses_AFittingTenWins_FiveDoNot_TheMechanicsLand()
        {
            // at the band's bottom (21) and middle (22), three pulls each (different seeds: gear rolls and the fight's dice):
            // a fitting ten wins at least two, and the raid-wide casts land (they cannot be interrupted, so the healers
            // have work to do); a fitting five of the same level wins none
            var bad = new List<string>();
            ulong seed = 41;
            foreach (var id in Bosses)
            {
                var landed = new HashSet<string>();
                var started = new HashSet<string>();
                foreach (int level in new[] { 21, 22 })
                {
                    int tenWins = 0, fiveWins = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        var ten = Pull(id, Ten, level, seed++);
                        Console.WriteLine($"    r1 {id}: ten at {level} → {ten.Outcome} in {ten.Rounds} rounds, lowest raid health {ten.MinHp:P0}, {ten.Down} down; took {ten.Took}");
                        if (ten.Outcome == BattleOutcome.Victory)
                        {
                            tenWins++;
                            if (ten.Rounds < (id == "enc_r1_hollow_heart" ? 12 : 6)) bad.Add($"{id}: not a pushover at {level} ({ten.Rounds} rounds)");
                            landed.UnionWith(ten.Landed);
                        }
                        started.UnionWith(ten.Started);
                        var five = Pull(id, Five, level, seed++);
                        Console.WriteLine($"    r1 {id}: five at {level} → {five.Outcome} in {five.Rounds} rounds");
                        if (five.Outcome == BattleOutcome.Victory) fiveWins++;
                    }
                    if (tenWins < 2) bad.Add($"{id}: a fitting ten at {level} wins ({tenWins}/3)");
                    if (fiveWins > 0) bad.Add($"{id}: five at {level} do not win ({fiveWins}/3)");
                }
                foreach (var a in Mechanics[id].land)
                    if (!landed.Contains(a)) bad.Add($"{id}: {a} lands in a won fight");
                foreach (var a in Mechanics[id].attempt)
                    if (!landed.Contains(a) && !started.Contains(a)) bad.Add($"{id}: {a} is cast");
            }
            Assert(bad.Count == 0, string.Join("\n    ", bad));
        }

        /// <summary>[Sim] win rates over seeds (Tools/check.sh core --sim --filter TestsContentR1.Sim).</summary>
        [Sim]
        public static void Sim_BossWinRates()
        {
            foreach (var id in Bosses)
                foreach (int level in new[] { 21, 22 })
                    foreach (var (name, comp) in new[] { ("10", Ten), ("5", Five) })
                    {
                        int wins = 0, n = 12, rounds = 0;
                        float low = 0f;
                        for (ulong k = 0; k < (ulong)n; k++)
                        {
                            var r = Pull(id, comp, level, 9000 + k * 13);
                            if (r.Outcome == BattleOutcome.Victory) wins++;
                            else if (name == "10") Console.WriteLine($"      loss {id} at {level} seed {9000 + k * 13}: {r.Rounds} rounds, {r.Down} down");
                            rounds += r.Rounds;
                            low += r.MinHp;
                        }
                        Console.WriteLine($"    sim {id} at {level} vs {name}: {wins}/{n} wins, avg {rounds / n} rounds, avg lowest raid health {low / n:P0}");
                    }
        }

        /// <summary>The raid's damage taken, by the enemy ability that dealt it (top 5).</summary>
        static string DamageBreakdown(Battle b)
        {
            var by = new Dictionary<string, float>();
            foreach (var e in b.Events)
                if (e.Type == CombatEventType.Damage && e.Target != null && e.Target.Team == b.PlayerTeam && e.Source != null && e.Source.Team != b.PlayerTeam)
                {
                    var k = !string.IsNullOrEmpty(e.AbilityId) ? e.AbilityId : !string.IsNullOrEmpty(e.AuraId) ? e.AuraId : "melee";
                    by.TryGetValue(k, out var v);
                    by[k] = v + e.Amount;
                }
            return string.Join(", ", by.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key.Replace("cr_r1_", "")} {kv.Value:0}"));
        }

        // ===================================================================================================== markers

        [Test]
        public static void Markers_TheOffersWaitForTheFenAndLevel20()
        {
            var s = SessionTest.NewGame(ClassId.Priest, 19, 81);
            foreach (var id in RaidTest.Companions) s.Recruit(id);
            s.EnterMap("mirefen", "from_raid_hollow_heart");
            Assert(s.EnterRaid(Raid, "from_mirefen", RaidTest.Ids(5), true) == null, "enter");
            Assert(s.QuestMarkerOf("r1_quartermaster").Kind == QuestMarker.None && s.QuestMarkerOf("r1_hinoki").Kind == QuestMarker.None, "no offers before the fen is settled");
            s.Flags.Set(Gate);
            var q = s.QuestMarkerOf("r1_quartermaster");
            Assert(q.Kind == QuestMarker.AvailableLater && q.Level == 20, "Quill: grey ! for level 20: " + q);
            s.GivePartyXp(Progression.XpToNextLevel(Db, s.Main.Level) - s.Main.Xp);
            Assert(s.Main.Level == 20 && s.QuestMarkerOf("r1_quartermaster").Kind == QuestMarker.Available && s.QuestMarkerOf("r1_hinoki").Kind == QuestMarker.Available, "then offered");
            foreach (var quest in Db.Quests.Values.Where(x => x.zone == Raid))
            {
                Assert(quest.minLevel == 20 && Db.Npcs.ContainsKey(quest.giver), $"{quest.id}: level gate and giver");
                Assert(s.QuestMarkerIndex.Starters(quest.id).Contains(quest.giver), $"{quest.id}: {quest.giver} offers it");
                Assert(s.QuestMarkerIndex.Enders(quest.id).Count > 0, $"{quest.id}: has a turn-in");
            }
        }
    }
}
