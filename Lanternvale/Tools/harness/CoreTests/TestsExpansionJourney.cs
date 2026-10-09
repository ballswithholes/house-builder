// XP PACING OF THE EXPANSION AS A PLAYER LIVES IT (Docs/Expansion.md §1, §8 "XP"): one save, from the end of the 1-12
// slice to the end of Skyreach, on two routes.
//   * the 1-12 slice is played for real (TestsSessionFullPlaythrough's Playthrough, by reflection) and the party it
//     leaves (main + Kael, Aldric, Seren, Rook; Torvan at camp) walks on, at the level and with the gear the slice gives;
//   * the FULL route: the deepened north bands and the three early hidden dungeons, then The Ember Road (Amberfield and
//     the Barrow), Brightwater, The Drowned Lanterns (Mirefen and the Drowned Vault), Ash on the Wind (Skyreach and the
//     Frozen Sanctum), with most side quests; the LEAN route skips the north bands and their dungeons;
//   * everything through dialogues, walking, props, chests and fights (the party AI on everyone, AutoResolve); no flags
//     poked, no items given, no levels granted, no grinding (every encounter is met once);
//   * between fights the party rests where the map allows it (LongRest), else regenerates (Tick); the main character
//     trains at a trainer when one is near; loot upgrades are equipped; quest rewards are claimed;
//   * a wipe at an encounter is reloaded from the save made before it, as a player would (ReloadOnWipe: the pacing tests
//     measure XP; the bosses have their own tests, and the sims below report wipes);
//   * at each zone exit: level, average item level, gold, fights, deaths and XP are recorded.
// The windows (Progression.RateLevel: content pays at its own level, so being ahead no longer compounds):
//   * lean route - a zone and its dungeon take the party to about its band top: Amberfield 17-19 (the Barrow is offered
//     at 17, Penhallow's next chapter at 18), Mirefen 23-25 (the Vault at 23, Ash on the Wind at 24), Skyreach 29-31
//     (the Sanctum at 28); no grinding, so every level gate on the way has to open by itself;
//   * full route - the north bands and their dungeons take 12 to 14-15 (their bosses are met at their bands: the
//     Mossking at 14, the Lich at 15), which is about one level ahead of the lean route by Amberfield and no more than
//     that later: Amberfield 18-20, Mirefen 24-26, Skyreach 30-31 (the raids, 31-33, are not reached by the zones alone).
// Quest steps are grouped in blocks; a block whose offer is not there yet (level gate) is retried later in the zone; a
// block that breaks is recorded as a journey problem and the run goes on (the report lists them all).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsExpansionJourney
    {
        // ================================================================== the report

        public sealed class ZoneExit
        {
            public string Zone;
            public int Level, BandMin, BandMax;
            public float AvgIlvl, MinIlvl;
            public int EmptySlots, Gold, Fights, Deaths, Wipes;
            public long XpGained, KillXp;
            public int Useless;
            public string UselessList = "";
            public List<string> Unfinished = new List<string>();
            public string Party;
            public override string ToString() =>
                $"{Zone,-22} L{Level,2} (band {BandMin}-{BandMax}) avg ilvl {AvgIlvl,5:0.0} (worst member {MinIlvl,4:0.0}), empty slots {EmptySlots}, " +
                $"gold {Gold / 100}s, {Fights} fights, {Deaths} deaths, xp +{XpGained} ({KillXp} from kills)" +
                (Unfinished.Count > 0 ? "; unfinished: " + string.Join(", ", Unfinished) : "") + (Useless > 0 ? $"; {Useless} drops nobody can use ({UselessList})" : "");
        }

        sealed class Deferred : Exception { public Deferred(string m) : base(m) { } }
        sealed class StopRun : Exception { }

        // ================================================================== the run

        public sealed class Journey
        {
            public GameSession S;
            public readonly List<string> Log = new List<string>();
            public readonly List<string> Problems = new List<string>();
            public readonly List<ZoneExit> Exits = new List<ZoneExit>();
            public readonly Dictionary<string, string[]> EncounterScripts = new Dictionary<string, string[]>(StringComparer.Ordinal);
            public readonly Dictionary<string, (int rounds, float minHp, int deaths)> FightLog = new Dictionary<string, (int, float, int)>();
            public int Fights, Deaths, ZoneFights, ZoneDeaths, TrainGoldSpent;
            public long ZoneKillXp, LastXp;
            readonly List<(string name, Action body)> deferred = new List<(string, Action)>();
            readonly Dictionary<string, string> deferredWhy = new Dictionary<string, string>();
            public bool SkipNorth;
            /// <summary>A wipe at a WalkInto encounter reloads the save made just before it (another RNG) and tries again,
            /// up to 3 times, as a player would; every wipe is listed in Wipes.</summary>
            public bool ReloadOnWipe;
            public readonly List<string> Wipes = new List<string>();
            /// <summary>Buy vendor gear in Brightwater (on by default: a player with silver in town shops).</summary>
            public bool Shopping = true;
            /// <summary>Stop the run (StopRun) right after this zone's exit is recorded.</summary>
            public string StopAt;
            public readonly ClassId Cls;
            public readonly ulong Seed;

            public Journey(ClassId cls, ulong seed) { Cls = cls; Seed = seed; }

            public void Note(string s) => Log.Add(s);
            public string Tail(int n = 30) => string.Join("\n      ", Log.Skip(Math.Max(0, Log.Count - n)));
            void Check(bool c, string msg) { if (!c) throw new Exception(msg + "\n      " + Tail(20)); }

            // ---------------------------------------------------------------- the slice

            public void PlaySlice()
            {
                var t = typeof(TestsSessionFullPlaythrough).GetNestedType("Playthrough", BindingFlags.NonPublic);
                var p = Activator.CreateInstance(t, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { Cls, Seed }, null);
                var play = t.GetMethod("Play");
                try
                {
                    play.Invoke(p, new object[] { TestsSessionFullPlaythrough.BridgeRoute.Fight, TestsSessionFullPlaythrough.WicksRoute.Fight, TestsSessionFullPlaythrough.KeeperRoute.Letter });
                }
                catch (TargetInvocationException e) { throw e.InnerException ?? e; }
                Attach((GameSession)t.GetField("S").GetValue(p));
            }

            void Attach(GameSession s)
            {
                S = s;
                S.Settings.CompanionAutoPlay = true;
                foreach (var u in S.Roster) S.SetAutoPlay(u, true);
                S.EventRaised += e =>
                {
                    if (e.Kind == SessionEventKind.LevelUp && e.Unit == S.Main) Note($"[LevelUp] {S.Main.Level}");
                    if (e.Kind == SessionEventKind.QuestCompleted) Note($"[QuestCompleted] {e.Id}");
                    if (e.Kind == SessionEventKind.QuestStarted) Note($"[QuestStarted] {e.Id}");
                };
                S.TakeEvents();
            }

            /// <summary>Loads <paramref name="save"/> into a new session (as the game's Load does), with another RNG.</summary>
            void Reload(string save, int attempt)
            {
                var s = new GameSession(Db, Seed + 1000 + (ulong)attempt);
                Check(s.LoadGame(save, out var err), "reload: " + err);
                s.Rng.s0 = 0x9E3779B97F4A7C15UL * (Seed + (ulong)attempt + 1); s.Rng.s1 = 0xBF58476D1CE4E5B9UL ^ (Seed + (ulong)attempt);
                Attach(s);
            }

            // ---------------------------------------------------------------- blocks

            /// <summary>Runs a block of quest steps: Deferred (an offer not there yet) queues it for Retry; anything else is a
            /// journey problem (logged, the run goes on).</summary>
            public void Block(string name, Action body)
            {
                try { Settle(); body(); Note($"block {name}: done at L{S.Main.Level} (total xp {TotalXp()})"); }
                catch (Deferred d) { deferred.Add((name, body)); deferredWhy[name] = d.Message; Note($"block {name}: deferred ({d.Message}) at L{S.Main.Level}"); Settle(); }
                catch (Exception e) { Problems.Add($"{S.MapId} block '{name}' at L{S.Main.Level}: {e.Message.Split('\n')[0]}"); Note($"block {name}: FAILED {e.Message}"); Settle(); }
            }

            /// <summary>Retries deferred blocks (the level may have caught up); the ones still deferred become problems when final.</summary>
            public void Retry(bool final)
            {
                for (int pass = 0; pass < 3 && deferred.Count > 0; pass++)
                {
                    var todo = deferred.ToList();
                    deferred.Clear();
                    foreach (var (n, b) in todo) Block(n, b);
                }
                if (final)
                {
                    foreach (var (n, _) in deferred) Problems.Add($"{S.MapId} block '{n}' never offered at L{S.Main.Level} ({(deferredWhy.TryGetValue(n, out var why) ? why : "?")})");
                    deferred.Clear();
                }
            }

            /// <summary>Ends any dialogue/battle/loot left over by a broken block.</summary>
            void Settle()
            {
                for (int i = 0; i < 4; i++)
                {
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Dialogue.IsActive) { Converse(); continue; }
                    break;
                }
                TakeLoot();
            }

            public void Offer(string npc, string quest, params string[] script)
            {
                if (S.Quests.IsActive(quest) || S.Quests.IsCompleted(quest)) return;
                if (S.Main.Level < Db.Quests[quest].minLevel) throw new Deferred($"{quest} needs level {Db.Quests[quest].minLevel}");
                TalkTo(npc, script);
                if (!S.Quests.IsActive(quest) && !S.Quests.IsCompleted(quest)) throw new Deferred($"{quest} not offered by {npc}");
            }

            // ---------------------------------------------------------------- dialogue

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
                        foreach (var leave in new[] { "Goodbye", "(Continue.)", "(Leave", "(Step back", "(Back away", "Not right now", "Not yet", "Never mind", "Let's keep moving" })
                        {
                            var c = v.Choices.FirstOrDefault(x => x.Text.IndexOf(leave, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (c != null) { pick = c.Index; break; }
                        }
                    if (pick < 0 && v.Choices.Count > 0) pick = v.Choices[v.Choices.Count - 1].Index;
                    Check(pick >= 0, $"no choice at {v.DialogueId}/{v.NodeId}: {SessionTest.Describe(v)}");
                    S.ChooseDialogue(pick);
                }
                Check(!S.Dialogue.IsActive, "dialogue finished");
                if (todo.Count > 0) Note($"  (not offered: {string.Join(" | ", todo)})");
                CloseOverlays();
                if (S.Mode == SessionMode.Combat) Fight();
                TakeLoot();
            }

            void CloseOverlays()
            {
                if (S.ActiveTrainer != null) S.CloseTrainer();
            }

            public void TalkTo(string npcId, params string[] script)
            {
                var npc = S.VisibleNpcs().FirstOrDefault(n => n.npc == npcId);
                Check(npc != null, $"{npcId} stands on {S.MapId}");
                Go(npc.pos, GameSession.InteractionRange - 0.4f);
                var r = S.TalkTo(npcId);
                Check(r.Ok && r.Kind == InteractKind.Dialogue, $"talk to {npcId}: {r.Message}");
                Note("talk " + npcId);
                Converse(script);
            }

            public void Use(string interactId, params string[] script)
            {
                var p = S.MapDef.props.FirstOrDefault(x => x.interact == interactId && S.Map.IsPropVisible(x));
                Check(p != null, $"prop {interactId} is there on {S.MapId}");
                Go(p.pos, GameSession.InteractionRange - 0.3f);
                var r = S.InteractProp(interactId);
                Check(r.Ok && r.Kind == InteractKind.Dialogue, $"use {interactId}: {r.Message}");
                Note("use " + interactId);
                Converse(script);
            }

            // ---------------------------------------------------------------- movement and encounters

            bool expectTravel;

            public void Go(Vec2 dest, float near = 0.8f, int maxLegs = 40)
            {
                string startMap = S.MapId;
                int crossings = 0;
                Recover(false);   // between errands the party gets its breath back (real time regenerates; MoveLeader does not tick)
                for (int leg = 0; leg < maxLegs; leg++)
                {
                    Check(!S.IsGameOver, "game over");
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(EncounterScript()); continue; }
                    TakeLoot();
                    if (Vec2.Distance(S.Leader.Position, dest) <= near) return;
                    var r = S.MoveLeader(dest);
                    if (r.Trigger.Stop)
                    {
                        Check(r.Trigger.Kind != TriggerKind.Locked, "a locked way: " + r.Trigger.Id);
                        if (r.Trigger.Kind == TriggerKind.RaidGate) return;
                        if (r.Trigger.Kind == TriggerKind.Travel)
                        {
                            if (expectTravel || S.MapId == startMap) return;
                            // walked over another exit (a dungeon door on the way): straight back out, then round it
                            Note($"   (walking to {dest} on {startMap} crossed into {S.MapId}; back)");
                            var back = S.MapDef.transitions.First(x => x.targetMap == startMap);
                            S.UseTransition(back.id);
                            Check(S.MapId == startMap && ++crossings < 3, $"back on {startMap} ({crossings} crossings)");
                            var side = new Vec2(S.Leader.Position.x + (dest.x > S.Leader.Position.x ? 4f : -4f), S.Leader.Position.y - 4f);
                            S.MoveLeader(side);
                            continue;
                        }
                        continue;
                    }
                    if (Vec2.Distance(S.Leader.Position, dest) <= near + 0.3f) return;
                    if (!r.Moved) break;
                }
                Check(Vec2.Distance(S.Leader.Position, dest) <= near + 1.5f, $"reached {dest} on {S.MapId} (at {S.Leader.Position})");
            }

            string[] EncounterScript()
            {
                var owner = S.Dialogue.OwnerId ?? "";
                if (EncounterScripts.TryGetValue(owner, out var script) && script != null)
                {
                    EncounterScripts[owner] = new string[0];
                    return script;
                }
                return new[] { "(Attack.)", "(Draw your weapon", "(Draw your weapons", "Put the goat down" };
            }

            public void WalkInto(string encId, params string[] script)
            {
                var enc = S.Map.FindEncounter(encId);
                Check(enc != null, $"{encId} on {S.MapId}");
                if (script.Length > 0) EncounterScripts[encId] = script;
                if (S.Map.IsEncounterDone(enc)) { Note(encId + " already done"); return; }
                Check(S.Map.IsEncounterAvailable(enc), $"{encId} is there to meet");
                var preview = S.PreviewEncounter(encId);
                Recover(preview.Any(x => x.Rank >= CreatureRank.Elite));
                // sims: a save right before a chosen encounter (JOURNEY_SNAPENC), written to JOURNEY_SNAPDIR
                var snapDir = Environment.GetEnvironmentVariable("JOURNEY_SNAPDIR");
                if (!string.IsNullOrEmpty(snapDir) && Environment.GetEnvironmentVariable("JOURNEY_SNAPENC") == encId)
                    System.IO.File.WriteAllText(System.IO.Path.Combine(snapDir, $"{Cls}_{Seed}_L{S.Main.Level}.json"), S.SaveGame());
                string save = ReloadOnWipe && S.Mode == SessionMode.Exploration ? S.SaveGame() : null;
                for (int attempt = 0; ; attempt++)
                {
                    try { Meet(encId, enc); return; }
                    catch (Exception e) when (save != null && S.IsGameOver && attempt < 3)
                    {
                        Wipes.Add($"{encId} at L{S.Main.Level}");
                        Note($"   WIPE at {encId} (L{S.Main.Level}): reloading ({e.Message.Split('\n')[0]})");
                        Reload(save, attempt);
                        if (script.Length > 0) EncounterScripts[encId] = script;
                        enc = S.Map.FindEncounter(encId);
                    }
                }
            }

            void Meet(string encId, EncounterDef enc)
            {
                for (int leg = 0; leg < 16 && !S.Map.IsEncounterDone(enc); leg++)
                {
                    if (S.Mode == SessionMode.Combat) { Fight(); continue; }
                    if (S.Mode == SessionMode.Dialogue) { Converse(EncounterScript()); continue; }
                    string startMap = S.MapId;
                    var r = S.MoveLeader(enc.pos);
                    if (S.MapId != startMap)
                    {
                        Note($"   (walking to {encId} crossed into {S.MapId}; back)");
                        S.UseTransition(S.MapDef.transitions.First(x => x.targetMap == startMap).id);
                        Check(S.MapId == startMap, "back on " + startMap);
                        S.MoveLeader(new Vec2(S.Leader.Position.x + (enc.pos.x > S.Leader.Position.x ? 4f : -4f), S.Leader.Position.y - 4f));
                        enc = S.Map.FindEncounter(encId);
                        continue;
                    }
                    if (r.Trigger.Stop) continue;
                    if (S.Mode == SessionMode.Exploration && !S.Map.IsEncounterDone(enc) && Vec2.Distance(S.Leader.Position, enc.pos) < enc.radius)
                        S.MoveLeader(enc.pos + new Vec2(enc.radius + 2.5f, 0f));
                }
                Check(S.Map.IsEncounterDone(enc), $"{encId} resolved");
            }

            /// <summary>Every hostile encounter of the map still there (hidden ambushes and flag-gated ones excluded).</summary>
            public void ClearRemaining(params string[] except)
            {
                string map = S.MapId;
                foreach (var e in S.MapDef.encounters.ToList())
                {
                    if (S.MapId != map) break;
                    if (except.Contains(e.id) || e.hidden) continue;
                    if (S.Map.IsEncounterDone(e) || !S.Map.IsEncounterAvailable(e)) continue;
                    if (S.IsPracticeEncounter(e)) continue;
                    try { WalkInto(e.id); }
                    catch (Exception ex) { Note($"WALKER {S.MapId}: clearing {e.id} at L{S.Main.Level}: {ex.Message.Split('\n')[0]}"); Settle(); }
                }
            }

            public void Chest(string chestId)
            {
                var c = S.Map.FindChest(chestId);
                Check(c != null && S.Map.IsChestAvailable(c), $"chest {chestId} is there");
                if (S.Map.IsChestOpened(chestId)) return;
                Go(c.pos, GameSession.InteractionRange - 0.4f);
                for (int i = 0; i < 20 && S.Map.IsChestLocked(c); i++) S.TryUnlockChest(chestId);
                if (S.Map.IsChestLocked(c)) { Note($"chest {chestId} stays locked"); return; }
                if (S.Map.IsChestOpened(chestId)) { TakeLoot(); Upkeep(); return; }
                var r = S.OpenChest(chestId);
                Check(r.Ok, $"open {chestId}: {r.Message}");
                TakeLoot();
                Upkeep();
            }

            public void Travel(string transitionId, string expectMap)
            {
                var t = S.Map.Def.transitions.FirstOrDefault(x => x.id == transitionId);
                Check(t != null && S.Map.IsTransitionVisible(t), $"{transitionId} is there on {S.MapId}");
                string from = S.MapId;
                expectTravel = true;
                try { Go(t.pos, 0.1f); } finally { expectTravel = false; }
                if (S.MapId != expectMap && S.MapId != from)
                {
                    // the walk crossed another exit (a dungeon door on the way): come back and take the marker directly
                    Note($"   (the way to {transitionId} crossed into {S.MapId}; back)");
                    var back = S.MapDef.transitions.First(x => x.targetMap == from);
                    S.UseTransition(back.id);
                    Check(S.MapId == from, "back on " + from);
                }
                if (S.MapId != expectMap)
                {
                    var r = S.UseTransition(transitionId);
                    Check(r.Ok, $"use {transitionId}: {r.Message}");
                }
                Check(S.MapId == expectMap, $"travelled to {expectMap} (on {S.MapId})");
                Note("== " + expectMap);
            }

            // ---------------------------------------------------------------- fights and upkeep

            public void Fight()
            {
                var b = S.Battle;
                Check(b != null, "a battle");
                var id = S.BattleEncounter?.id ?? "?";
                var party = b.Units.Where(u => u.Team == b.PlayerTeam && u.IsCharacter).ToList();
                float max = party.Sum(u => u.MaxHealth), min = 1f;
                int steps = 0;
                while (!b.IsOver && steps++ < 60000 && b.Round <= 100)
                {
                    var step = S.RunAIStep();
                    Check(step != null || b.IsOver, $"AI step in {id}");
                    min = Math.Min(min, party.Sum(u => Math.Max(0f, u.Health)) / max);
                }
                int deaths = party.Count(u => !u.IsAlive || u.Dead || u.Downed);
                var sum = S.FinishBattle();
                Fights++; ZoneFights++; Deaths += deaths; ZoneDeaths += deaths; ZoneKillXp += sum?.Xp ?? 0;
                FightLog[id] = (sum?.Rounds ?? b.Round, min, deaths);
                Note($"   {id}: {sum?.Outcome} in {sum?.Rounds} rounds, low {min:P0}, {deaths} down, +{sum?.Xp} xp, L{S.Main.Level}");
                Check(sum != null && sum.Outcome == CombatEndKind.Victory, $"{id}: the party wins (outcome {sum?.Outcome}, round {b.Round}, level {S.Main.Level}, party {SessionTest.PartyHp(S)})");
                TakeLoot();
                Upkeep();
            }

            public void TakeLoot()
            {
                if (S.PendingLoot != null) S.TakeAllLoot();
                foreach (var q in S.PendingQuestRewards.ToList())
                {
                    var choices = S.QuestRewardChoices(q);
                    if (choices.Count == 0) continue;
                    var pick = choices.OrderByDescending(d => S.Party.Any(u => EquipmentRules.CannotUseReason(u, d) == null && IsUpgradeFor(u, d)) ? 1 : 0)
                                      .ThenByDescending(d => StartingGear.Score(d)).First();
                    S.ClaimQuestReward(q, pick.id);
                    Note($"   reward {q}: {pick.name} (ilvl {pick.itemLevel} {pick.quality})");
                }
            }

            bool IsUpgradeFor(Unit u, ItemDef d)
            {
                if (d.equip == EquipType.None) return false;
                var slot = EquipmentRules.ChooseSlot(u, d);
                if (!slot.HasValue) return false;
                var cur = u.Equipment[slot.Value];
                return cur == null || IsUpgrade(cur.Def, d);
            }

            public void Upkeep()
            {
                foreach (var u in S.Roster)
                    if (S.TalentPointsAvailable(u) > 0) S.AutoAllocateTalents(u);
                EquipUpgrades();
            }

            /// <summary>Equips bag items that are better for an active (or raid) member: an empty slot, or the same kind of
            /// item at a higher item level × quality.</summary>
            public void EquipUpgrades()
            {
                for (int pass = 0; pass < 4; pass++)
                {
                    bool any = false;
                    foreach (var it in S.Inventory.Items.ToList())
                    {
                        if (it.Def.equip == EquipType.None || !S.Inventory.Items.Contains(it)) continue;
                        foreach (var u in S.Roster.OrderBy(x => S.Party.Contains(x) ? 0 : 1))
                        {
                            if (S.CanEquip(u, it) != null) continue;
                            var slot = EquipmentRules.ChooseSlot(u, it.Def);
                            if (!slot.HasValue) continue;
                            var cur = u.Equipment[slot.Value];
                            if (cur != null && !IsUpgrade(cur.Def, it.Def)) continue;
                            if (cur == null && (slot.Value == EquipSlot.MainHand || slot.Value == EquipSlot.OffHand) && u.Equipment.HasTwoHander) continue;
                            if (S.Equip(u, it, slot) == null) { any = true; Note($"   {u.Name} equips {it.Name} (ilvl {it.Def.itemLevel})"); break; }
                        }
                    }
                    if (!any) break;
                }
            }

            static bool IsUpgrade(ItemDef cur, ItemDef d)
            {
                // a One-Hand weapon held in the off hand and an Off Hand weapon compete for the same slot
                bool offHand = (EquipmentRules.IsOffHandWeapon(d) && cur.equip == EquipType.OneHand) || (EquipmentRules.IsOffHandWeapon(cur) && d.equip == EquipType.OneHand);
                if (cur.equip != d.equip && !offHand) return false;
                if (d.weaponType != cur.weaponType) return false;
                if (d.armorType < cur.armorType) return false;
                return StartingGear.Score(d) > StartingGear.Score(cur) + 0.5f;
            }

            /// <summary>A long rest where the map allows it before an elite fight or when hurt; else regeneration.</summary>
            public void Recover(bool bigFight)
            {
                if (S.Mode != SessionMode.Exploration) return;
                if ((bigFight || NeedsRecovery(70f)) && S.CannotRestReason() == null)
                {
                    S.LongRest();
                    foreach (var u in S.Party)
                        if (u.ClassId == ClassId.Hunter && u.Pet == null && u.Knows("hunter_call_pet")) S.UseAbility(u, "hunter_call_pet");
                    return;
                }
                for (int i = 0; i < 300 && NeedsRecovery(95f); i++) S.Tick(2f);
            }

            bool NeedsRecovery(float hpPct)
            {
                foreach (var u in S.PartyUnits())
                {
                    if (u.IsDeadOrDowned) return true;
                    if (u.HealthPct < hpPct) return true;
                    if (u.MaxMana > 0 && u.PowerType == ResourceType.Mana && u.ManaPct < hpPct - 5f) return true;
                }
                return false;
            }

            /// <summary>The main character trains at a trainer of its class on the current map (companions train themselves).</summary>
            public void Train()
            {
                var npc = S.VisibleNpcs().Select(n => Db.Npcs.TryGetValue(n.npc, out var d) ? d : null)
                           .FirstOrDefault(d => d != null && d.trains != null && d.trains.Contains(S.Main.ClassId));
                if (npc == null) return;
                if (S.TrainerOffers(S.Main).Count(o => o.CanTrain) == 0) return;
                int gold = S.Gold;
                S.OpenTrainer(npc.id);
                int n = S.ActiveTrainer != null ? S.TrainAll(S.Main) : 0;
                S.CloseTrainer();
                TrainGoldSpent += gold - S.Gold;
                int left = S.TrainerOffers(S.Main).Count(o => !o.CanTrain && o.Level <= S.Main.Level);
                Note($"   trained {n} ranks at L{S.Main.Level} for {(gold - S.Gold) / 100}s ({left} unaffordable)");
                Upkeep();
            }

            public int GearGoldSpent, JunkGoldEarned, ItemsBought;
            public readonly List<(string id, int level, int empty)> Recruits = new List<(string, int, int)>();

            /// <summary>Shopping in town: junk sold; then, for every recruited character, vendor gear bought for empty
            /// armour/weapon slots and for pieces at least 25 % better than what is worn (best affordable first).</summary>
            public void Shop()
            {
                var here = S.VisibleNpcs().Select(n => n.npc).Distinct().Where(id => S.GetVendor(id) != null).ToList();
                foreach (var vid in here)
                {
                    S.OpenVendor(vid);
                    if (S.ActiveVendor == null) continue;
                    foreach (var it in S.Inventory.Items.Where(i => i.Def.kind == ItemKind.Junk).ToList())
                    {
                        int g0 = S.Gold;
                        if (S.Sell(it, it.Count) == null) JunkGoldEarned += S.Gold - g0;
                    }
                    var offers = S.ActiveVendor.Offers().Where(o => o.Item.equip != EquipType.None).OrderByDescending(o => StartingGear.Score(o.Item)).ToList();
                    foreach (var u in S.Roster.OrderBy(x => S.Party.Contains(x) ? 0 : 1))
                        foreach (var off in offers)
                        {
                            if (off.Price > S.Gold || off.Stock == 0) continue;
                            if (EquipmentRules.CannotUseReason(u, off.Item) != null) continue;
                            var slot = EquipmentRules.ChooseSlot(u, off.Item);
                            if (!slot.HasValue) continue;
                            var cur = u.Equipment[slot.Value];
                            if (cur != null && !(IsUpgrade(cur.Def, off.Item) && StartingGear.Score(off.Item) >= StartingGear.Score(cur.Def) * 1.25f)) continue;
                            if (cur == null && (slot.Value == EquipSlot.MainHand || slot.Value == EquipSlot.OffHand) && u.Equipment.HasTwoHander) continue;
                            int g = S.Gold;
                            if (S.Buy(off.Item.id) != null) continue;
                            var inst = S.Inventory.Items.LastOrDefault(i => i.Def.id == off.Item.id);
                            if (inst != null && S.Equip(u, inst, slot) == null)
                            {
                                GearGoldSpent += g - S.Gold; ItemsBought++;
                                Note($"   bought {off.Item.name} (ilvl {off.Item.itemLevel}, {off.Price / 100}s) for {u.Name}");
                            }
                        }
                    S.CloseVendor();
                }
                Note($"   shopping: {ItemsBought} items so far, {GearGoldSpent / 100}s on gear, {JunkGoldEarned / 100}s from junk; {S.Gold / 100}s left");
            }

            public void RecruitHere(string id, params string[] script)
            {
                if (S.CompanionStatusOf(id) != CompanionStatus.NotRecruited) return;
                if (!S.VisibleNpcs().Any(n => n.npc == id)) { Problems.Add($"{id} is not on {S.MapId}"); return; }
                TalkTo(id, script.Length > 0 ? script : new[] { "Come with me" });
                if (S.CompanionStatusOf(id) == CompanionStatus.NotRecruited) TalkTo(id, "Come with me", "Welcome aboard", "Join us");
                if (S.CompanionStatusOf(id) == CompanionStatus.NotRecruited) Problems.Add($"{id}: could not be recruited through dialogue on {S.MapId} at L{S.Main.Level}");
                else
                {
                    var u = S.Roster.First(x => S.MemberId(x) == id);
                    Recruits.Add((id, u.Level, EmptySlots(u)));
                    Note($"   recruited {id} L{u.Level}: ilvl {AvgIlvl(new[] { u }):0.0}, empty slots {EmptySlots(u)}");
                }
            }

            // ---------------------------------------------------------------- measurement

            public static readonly EquipSlot[] ArmourSlots = { EquipSlot.Head, EquipSlot.Shoulder, EquipSlot.Chest, EquipSlot.Hands, EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Wrist, EquipSlot.Waist };

            public static float AvgIlvl(IEnumerable<Unit> units)
            {
                var all = units.SelectMany(u => u.Equipment.Equipped.Select(kv => kv.Value)).Where(x => x != null).Select(x => (float)x.Def.itemLevel).ToList();
                return all.Count == 0 ? 0f : all.Average();
            }

            public static int EmptySlots(Unit u) => ArmourSlots.Count(s => u.Equipment[s] == null) + (u.Equipment.MainHand == null ? 1 : 0);

            public ZoneExit Exit(string zone, params string[] questZones)
            {
                var m = Db.Maps.TryGetValue(zone, out var md) ? md : null;
                var e = new ZoneExit
                {
                    Zone = zone, Level = S.Main.Level, BandMin = m?.levelMin ?? 0, BandMax = m?.levelMax ?? 0,
                    AvgIlvl = AvgIlvl(S.Party), MinIlvl = S.Party.Min(u => AvgIlvl(new[] { u })),
                    EmptySlots = S.Party.Sum(EmptySlots), Gold = S.Gold, Fights = ZoneFights, Deaths = ZoneDeaths,
                    Party = string.Join(", ", S.Party.Select(u => $"{u.Name}({u.ClassId}) L{u.Level} i{AvgIlvl(new[] { u }):0}")),
                };
                var useless = S.Inventory.Items.Where(it => it.Def.equip != EquipType.None && S.Roster.All(u => EquipmentRules.CannotUseReason(u, it.Def) != null)).ToList();
                e.Useless = useless.Count;
                e.UselessList = string.Join(", ", useless.GroupBy(it => $"{it.Def.armorType}/{it.Def.weaponType}/{(it.Def.classes != null && it.Def.classes.Length > 0 ? string.Join("+", it.Def.classes) : "-")} r{it.Def.requiredLevel}").Select(g => $"{g.Count()}x {g.Key}"));
                long xp = TotalXp();
                e.XpGained = xp - LastXp; e.KillXp = ZoneKillXp;
                LastXp = xp; ZoneKillXp = 0;
                foreach (var q in Db.Quests.Values.Where(q => questZones.Contains(q.zone)).OrderBy(q => q.id))
                {
                    if (S.Quests.IsCompleted(q.id)) continue;
                    string st = S.Quests.IsActive(q.id) ? "stuck at '" + S.Quests.GetStage(q.id) + "'" : "never started";
                    e.Unfinished.Add($"{q.id} ({st}, min {q.minLevel})");
                    if (S.Quests.IsActive(q.id) && !q.id.StartsWith("mq2_")) Problems.Add($"{zone}: {q.id} {st} when leaving at L{S.Main.Level}");
                }
                Exits.Add(e);
                ZoneFights = 0; ZoneDeaths = 0;
                if (StopAt == zone) throw new StopRun();
                Note("EXIT " + e);
                return e;
            }

            public long TotalXp()
            {
                long t = S.Main.Xp;
                for (int l = 1; l < S.Main.Level; l++) t += Progression.XpToNextLevel(Db, l);
                return t;
            }
        }

        // ================================================================== the zones

        static void NorthBands(Journey j)
        {
            if (j.SkipNorth)
            {
                // the lean route: straight from the slice to the Elder's offer and the west road
                j.Train();
                j.Block("mq2_ember_road offer", () => j.Offer("elder_maru", "mq2_ember_road", "Ash, drifting down from the north", "What lies north", "I'll take the west road."));
                j.Exit("north bands skipped");
                return;
            }
            j.Check0(j.S.MapId == "lanternvale", "the slice ends in Lanternvale");
            j.RecruitHere("pip", "Come with me|Welcome aboard|Want to come along");
            j.Train();

            j.Block("lv2_singing_roots", () =>
            {
                j.Offer("child_nell", "lv2_singing_roots", "Moppet looks worried", "I'll go and listen.");
                j.Use("lv2_listening_root", "Just listen.");
                j.TalkTo("child_nell", "I listened to the roots", "I heard it, but");
            });
            j.Block("lv2_orchard_thieves", () =>
            {
                j.Offer("lv2_hana_pipp", "lv2_orchard_thieves", "Somebody else's?", "I'll find your thief.");
                j.Use("lv2_spilled_crate", "Wait among the trees");
                j.WalkInto("enc_lv2_orchard_rootlings");
                j.TalkTo("lv2_hana_pipp", "Rootlings", "The earth spirits ask you to share");
            });
            j.Block("lv2_lantern_oil", () =>
            {
                j.Offer("lamplighter_tobben", "lv2_lantern_oil", "Is anything else going missing", "I'll find your oil.");
                j.Go(j.S.MapDef.regions.First(r => r.id == "reg_lv2_oil_tracks").pos);
                j.WalkInto("enc_lv2_duskmane_scouts", "Put them down");
                j.TalkTo("lamplighter_tobben", "About your lamp oil", "won't be back");
            });
            j.ClearRemaining();
            j.Block("dg1_heart_of_kusu", () =>
            {
                if (!j.S.Flags.IsSet("found_root_hollows")) throw new Exception("the Root Hollows were not revealed");
                j.Travel("to_dgn_root_hollows", "dgn_root_hollows");
                j.Offer("dg1_hotaru", "dg1_heart_of_kusu", "Before what?", "I'll free him.");
                j.ClearRemaining("enc_dg1_rootwarden");
                j.WalkInto("enc_dg1_rootwarden");
                foreach (var c in j.S.MapDef.chests) if (j.S.Map.IsChestAvailable(c)) j.Chest(c.id);
                j.TalkTo("dg1_hotaru", "Here it is", "I'll take it to Elder Maru");
                j.Travel("to_lanternvale", "lanternvale");
                j.TalkTo("elder_maru", "I've brought something up from under Old Kusu's roots");
            });
            if (j.S.MapId == "dgn_root_hollows") j.Travel("to_lanternvale", "lanternvale");
            j.Exit("lanternvale", "lanternvale", "dgn_root_hollows");

            // Whisperwood's old forest and Mossdeep
            j.Travel("to_whisperwood", "whisperwood");
            j.RecruitHere("lys", "Come with me|Welcome aboard");
            j.Block("ww2_last_watch", () =>
            {
                j.Offer("ww2_pell", "ww2_last_watch", "What company?", "Lead the way");
                j.TalkTo("ww2_pell", "Tell me about Sir Corwin", "On to the Kestrel Stone");
                j.WalkInto("enc_ww2_kestrel_ambush");
                for (int i = 0; i < 6 && j.S.CountItem("ww2_aubrics_dispatch") == 0; i++) j.Use("ww2_kestrel_cairn", "Search the whole cairn", "Take the dispatch");
                j.TalkTo("ww2_pell", "Show him the charcoal heron", "Read him every word", "light it together");
            });
            j.Block("ww2_kings_glow", () =>
            {
                j.Offer("ww2_sprig", "ww2_kings_glow", "What spiders?", "I'll clear the glade");
                j.WalkInto("enc_ww2_glade_webs");
                j.TalkTo("ww2_sprig", "Show me the way under the moss");
                j.Travel("to_dgn_mossdeep", "dgn_mossdeep");
                foreach (var e in new[] { "enc_dg2_glowlings", "enc_dg2_web_gallery", "enc_dg2_pool_tenders", "enc_dg2_silk_hall", "enc_dg2_kings_guard" }) j.WalkInto(e);
                foreach (var c in new[] { "chest_dg2_cocoon", "chest_dg2_pool_cache" }) j.Chest(c);
                j.WalkInto("enc_dg2_mossking", "Sprig sent us");
                j.Chest("chest_dg2_kings_hoard");
                j.TalkTo("dg2_umbercap", "What was that grey ember", "Go home safe");
                j.Travel("to_whisperwood", "whisperwood");
                j.TalkTo("ww2_sprig", "He talked to us");
            });
            if (j.S.MapId == "dgn_mossdeep") j.Travel("to_whisperwood", "whisperwood");
            j.ClearRemaining();
            j.Exit("whisperwood", "whisperwood", "dgn_mossdeep");

            // the Shrine's terraces and the Lantern Catacombs
            j.Travel("to_shrine", "shrine");
            j.RecruitHere("morwen", "Come with me|Welcome aboard");
            j.Block("sh2_keepers_rest", () =>
            {
                j.Offer("sh2_novice_aiko", "sh2_keepers_rest", "I'll fetch the ledger");
                j.Chest("chest_sh2_keepers_lodge");
                j.TalkTo("sh2_novice_aiko", "Here. Keeper Ishiro's ledger");
                j.Travel("to_dgn_lantern_catacombs", "dgn_lantern_catacombs");
                if (!j.S.Map.IsEncounterDone("enc_dg3_gallery_bones")) j.WalkInto("enc_dg3_gallery_bones");
                j.WalkInto("enc_dg3_keepers_hall");
                j.Use("dg3_lantern_hana", "(Open the lantern's little door");
                j.Use("dg3_lantern_tomo", "(Tell Keeper Tomo");
                j.Use("dg3_lantern_rin", "(Count aloud");
                j.Chest("chest_dg3_ossuary");
                j.EncounterScripts["enc_dg3_chapel_guard"] = new[] { "Your keepers are free" };
                j.WalkInto("enc_dg3_chapel_guard");
                j.WalkInto("enc_dg3_lantern_lich", "They wanted to go home");
                j.Use("dg3_first_lantern", "(Open the cage");
                j.Chest("chest_dg3_first_keepers_hoard");
                j.ClearRemaining();
                j.Travel("to_shrine", "shrine");
                j.TalkTo("sh2_novice_aiko", "We opened the First Keeper's lantern");
            });
            if (j.S.MapId == "dgn_lantern_catacombs") j.Travel("to_shrine", "shrine");
            j.ClearRemaining();
            j.Retry(true);
            j.Exit("shrine", "shrine", "dgn_lantern_catacombs");

            // home, and the Elder's offer
            j.Travel("to_whisperwood", "whisperwood");
            j.Travel("to_lanternvale", "lanternvale");
            j.Retry(true);
            j.Train();
            j.Block("mq2_ember_road offer", () =>
            {
                j.Offer("elder_maru", "mq2_ember_road", "Ash, drifting down from the north", "What lies north", "I'll take the west road.");
            });
            j.Exit("north bands + dg1-3");
        }

        static void Amberfield(Journey j)
        {
            j.Travel("to_amberfield", "amberfield");
            j.RecruitHere("bruna");
            j.Block("mq2 reeve", () => { j.TalkTo("am_reeve_hester", "Who's stealing it?", "Then I'll follow the carts", "North of the Amber Bridge"); });
            j.Block("am_q_lamp_oil", () => j.Offer("am_miller_pell", "am_q_lamp_oil", "You're out of oil?", "I'll find your casks"));
            j.Block("am_q_bell", () => j.Offer("am_hedda_haybright", "am_q_bell", "You look like you've lost something", "I'll get Biscuit's bell back"));
            j.Block("am_q_hawks+queen", () =>
            {
                j.Offer("am_hob_waxley", "am_q_queen", "You look worried, Hob", "I'll bring your queen home");
                j.Offer("am_ida_crook", "am_q_hawks", "Are you missing lambs", "Six hawks");
                j.WalkInto("enc_am_hawks_south");
                j.WalkInto("enc_am_hawks_ring");
                j.WalkInto("enc_am_hawks_lookout");
                j.Use("am_swarm", "(Puff Old Hob's smoker");
                j.TalkTo("am_hob_waxley", "Your queen is back");
                j.TalkTo("am_ida_crook", "Six hawks won't take");
            });
            j.Block("am_q_ditchwater+bell", () =>
            {
                j.Offer("am_constable_pip", "am_q_ditchwater", "The noticeboard says", "Consider them dealt with");
                j.WalkInto("enc_am_ditchwater", "(Attack.)");
                j.Chest("chest_am_strongbox");
                j.TalkTo("am_constable_pip", "The Ditchwater Gang won't trouble the road again");
                j.TalkTo("am_hedda_haybright", "Biscuit's bell, back where it belongs");
            });
            j.Block("am_q_scarecrow", () =>
            {
                j.Offer("am_tam_haybright", "am_q_scarecrow", "Suspicious? The scarecrow?", "I'll take a look at your scarecrow");
                j.Use("am_scarecrow", "(Give the scarecrow a good firm shake.)");
                j.TalkTo("am_nib", "What happens to you now, Nib?|Why did you run away", "What happens to you now, Nib?", "Hedda Haybright could use a farmhand");
                j.TalkTo("am_tam_haybright", "It was a gnoll pup called Nib");
            });
            j.Block("am_q_sinkholes", () =>
            {
                j.Offer("am_wat_turnbull", "am_q_sinkholes", "That's a very round hole", "I'll go to the quarry");
                j.WalkInto("enc_am_quarry_parley", "(Attack.)");
                j.WalkInto("enc_am_quarry_deep");
                j.TalkTo("am_wat_turnbull", "The Mudpaw diggers won't undermine");
            });
            j.Block("am_q_corlan offer", () => j.Offer("am_ser_corlan", "am_q_corlan", "There's a heron carved over your door", "Then I'll get it back for you"));
            j.Block("mq2 caravan+corlan", () =>
            {
                j.WalkInto("enc_am_oil_caravan");
                j.TalkTo("am_ser_corlan", "It came off the caravan boss", "Skyreach?", "Skarra's warcamp");
            });
            j.Block("am casks 2+3", () =>
            {
                j.WalkInto("enc_am_gnoll_scouts");
                j.Use("am_cask_2", "(Chalk Pell's mark");
                j.WalkInto("enc_am_grave_robbers");
                j.Use("am_cask_3", "(Chalk Pell's mark");
            });
            j.Block("mq2 warcamp", () =>
            {
                j.WalkInto("enc_am_warcamp_gate");
                j.WalkInto("enc_am_warcamp_fire");
                j.WalkInto("enc_am_warcamp_archers");
                j.Use("am_cask_1", "(Chalk Pell's mark");
                j.WalkInto("enc_am_warchief", "Bruna, anything to add?");
                if (j.S.Map.IsChestAvailable(j.S.Map.FindChest("chest_am_trophies"))) j.Chest("chest_am_trophies");
            });
            j.Retry(false);
            j.Block("hand-ins", () =>
            {
                if (j.S.Quests.GetStage("am_q_lamp_oil") == "return") j.TalkTo("am_miller_pell", "All three casks");
                if (j.S.Quests.GetStage("am_q_corlan") == "return") j.TalkTo("am_ser_corlan", "Hang it over your door again");
                j.TalkTo("am_reeve_hester", "Skarra Duskmane is dead", "Who would know", "I'll take the road to Brightwater");
            });
            j.Block("am_q_harvest", () =>
            {
                j.Offer("am_reeve_hester", "am_q_harvest", "The warchief is dead and the oil is coming home", "I'll light the lanes");
                for (int i = 1; i <= 4; i++) j.Use("am_lane_lamp_" + i, "(Fill the lantern");
                j.TalkTo("am_reeve_hester", "All four lane lanterns are lit");
            });
            j.ClearRemaining();
            j.Retry(false);
            j.Block("am_q_barrow", () =>
            {
                j.Offer("am_ida_crook", "am_q_barrow", "Something's wrong under the king's hill", "I'll go down into the barrow");
                if (!j.S.Flags.IsSet("found_barrow")) j.TalkTo("am_ida_crook", "Tell me about the King's Ring", "Is there a way into the hill");
                j.Travel("to_dgn_barrow", "dgn_barrow");
                j.WalkInto("enc_dg4_robbers");
                j.Go(new Vec2(23.5f, 22f), 0.8f);
                j.WalkInto("enc_dg4_wight_guard");
                j.WalkInto("enc_dg4_ossuary");
                j.Chest("chest_dg4_ossuary");
                j.WalkInto("enc_dg4_ember_hearth");
                j.Use("dg4_ember", "(Smother the coal");
                j.Chest("chest_dg4_hearth");
                j.WalkInto("enc_dg4_huscarls");
                j.WalkInto("enc_dg4_king_aldwin", "(Draw your weapon.)");
                j.Chest("chest_dg4_hoard");
                j.TalkTo("dg4_aldwin_shade", "What was the fire under your hill?", "Then I'll follow him north");
                j.ClearRemaining();
                j.Travel("to_amberfield", "amberfield");
                j.TalkTo("am_ida_crook", "King Aldwin sleeps again");
            });
            if (j.S.MapId == "dgn_barrow") j.Travel("to_amberfield", "amberfield");
            j.Retry(true);
            j.Exit("amberfield", "amberfield", "dgn_barrow");
        }

        static void Brightwater(Journey j)
        {
            j.Travel("to_brightwater", "brightwater");
            j.Block("mq2_ember_road hand-in", () => j.TalkTo("bw_archivist_penhallow", "Elder Maru sent me"));
            j.RecruitHere("ysolde");
            j.RecruitHere("liora");
            j.Train();
            if (j.Shopping) j.Shop();
            j.Block("bw_q_parcels", () =>
            {
                j.Offer("bw_dockmaster_hobb", "bw_q_parcels", "Need a hand on the docks", "I'll carry them");
                j.TalkTo("bw_mother_wren", "parcel of lamp wicks");
                j.TalkTo("bw_trainer_vell", "clock springs");
                j.TalkTo("bw_captain_rowan", "sealed letter");
                j.TalkTo("bw_dockmaster_hobb", "All three parcels");
            });
            j.Block("bw_q_lost_heron", () =>
            {
                j.Offer("bw_child_tamsin", "bw_q_lost_heron", "Lost something", "find your Admiral");
                j.TalkTo("bw_dockhand_nettie");
                j.Use("bw_heron_reeds", "bucket of minnows", "wait as long as it takes");
                j.TalkTo("bw_child_tamsin", "Here he is", "Dame Ysolde");
            });
            j.Block("bw_q_forty_names", () =>
            {
                j.Offer("bw_trainer_bertram", "bw_q_forty_names", "Is something troubling you", "gather the lanterns");
                j.TalkTo("bw_ferryman_gideon", "gathering the lanterns");
                j.TalkTo("bw_captain_rowan", "gathering the lanterns");
                j.TalkTo("bw_dockhand_nettie", "gathering the lanterns", "[PERSUASION]", "carry it for your mother");
                j.Use("bw_heronguard_memorial", "Light the three lanterns");
                j.TalkTo("bw_trainer_bertram", "The lanterns are lit");
            });
            j.Retry(false);
            j.Exit("brightwater (first visit)");
        }

        static void Mirefen(Journey j)
        {
            j.Block("mq2_drowned_lanterns offer", () =>
            {
                if (j.S.MapId != "brightwater") throw new Exception("not in Brightwater");
                j.Offer("bw_archivist_penhallow", "mq2_drowned_lanterns", "about the fens");
            });
            j.Block("bw_q_fen_road offer", () => j.Offer("bw_captain_rowan", "bw_q_fen_road", "work for the Watch", "walk the fen road"));
            j.Travel("to_mirefen", "mirefen");
            j.RecruitHere("nanami");
            j.Block("mq2_drowned_lanterns", () =>
            {
                if (!j.S.Quests.IsActive("mq2_drowned_lanterns")) throw new Deferred("The Drowned Lanterns not started");
                j.TalkTo("mf_reeve_tamsin", "Penhallow of Brightwater sent me", "Then I'll go and stare", "I'll go now");
                for (int i = 1; i <= 3; i++) j.Use("mf_drowned_lantern_" + i, "(Cut the cord");
                j.TalkTo("mf_reeve_tamsin", "I cut these from the drowned lanterns", "Then I'll go and have a word", "I'll be careful");
                j.WalkInto("enc_mf_auntie_gall", "It ends now");
                j.TalkTo("mf_granny_sen", "I'll sing it");
            });
            j.Block("mf offers", () =>
            {
                j.Offer("mf_lily", "mf_lily_duke", "Who are you looking for", "I'll find your Duke");
                j.Offer("mf_bo_puddlefoot", "mf_bo_nets", "Rough day", "I'll deal with your crocolisks");
                j.Offer("mf_obi_wick", "mf_obi_glass", "Lowlantern looks short of lanterns", "I'll find you some");
            });
            j.Block("mf_hettie_soup offer", () => j.Offer("mf_hettie_brine", "mf_hettie_soup", "That chowder smells", "I'll fetch them"));
            j.Block("mf_coven_bounty offer", () => j.Offer("mf_reeve_tamsin", "mf_coven_bounty", "Is anyone paying for coven hags", "Consider it done"));
            j.Block("mf_bo_nets", () =>
            {
                foreach (var e in new[] { "enc_mf_shallows_crocs", "enc_mf_shallows_crocs_east", "enc_mf_channel_crocs" }) j.WalkInto(e);
                if (!j.S.Map.IsEncounterDone(j.S.Map.FindEncounter("enc_mf_old_gnasher"))) j.WalkInto("enc_mf_old_gnasher");
                j.TalkTo("mf_bo_puddlefoot", "Old Gnasher's done");
            });
            j.Block("mf_lily_duke", () =>
            {
                j.Use("mf_duke_ring", "(Look for a trail");
                j.Use("mf_duke_willow", "(Look for the trail again");
                j.WalkInto("enc_mf_stewpot_camp");
                j.Use("mf_duke_pot", "(Watch the two frogs", "(Lift them both");
                j.TalkTo("mf_lily", "He's safe");
            });
            j.Block("mf_corwin_rest", () =>
            {
                j.Offer("mf_corwin", "mf_corwin_rest", "What keeps you out here", "I'll help you lay them down");
                foreach (var e in new[] { "enc_mf_mere_ghouls", "enc_mf_barrow_ghouls", "enc_mf_barrow_ghouls_north" }) j.WalkInto(e);
                j.Use("mf_low_bell", "(Ring the Low Bell");
                j.WalkInto("enc_mf_low_bell");
                j.TalkTo("mf_corwin", "The barrows are quiet");
            });
            j.Block("mf_oracle_totems", () =>
            {
                j.Go(new Vec2(31f, 10f));
                j.Offer("mf_oracle_bloop", "mf_oracle_totems", "What do the coven totems do", "I'll break them");
                foreach (int i in new[] { 2, 4, 3, 1 })
                    for (int tries = 0; tries < 12 && !j.S.Flags.IsSet($"mf_totem_{i}_broken"); tries++)
                        j.Use("mf_totem_" + i, "(Smash the skull|(Unpick the binding");
                j.Go(new Vec2(31f, 10f));
                j.TalkTo("mf_oracle_bloop", "The four totems are broken");
            });
            j.Block("mf_reeve_chief (fought)", () =>
            {
                j.Go(new Vec2(31f, 10f));
                j.Offer("mf_reeve_tamsin", "mf_reeve_chief", "frog problem", "I'll go and talk to him");
                j.WalkInto("enc_mf_croaker_camp");
                j.WalkInto("enc_mf_croaking_stones", "(Attack.)|Then we fight");
                j.TalkTo("mf_reeve_tamsin", "Gubbagulp won't be cutting|Gubbagulp");
            });
            j.Block("mf_coven_bounty", () =>
            {
                // offered at 21: on the lean route the offer block is still deferred here, and a hand-in talk before
                // the quest starts would pass silently and leave the bounty at 'return' (Defeat objectives are pulled
                // from the encounters' done state when it starts)
                if (!j.S.Quests.IsActive("mf_coven_bounty") && !j.S.Quests.IsCompleted("mf_coven_bounty")) throw new Deferred("mf_coven_bounty not offered yet");
                foreach (var e in new[] { "enc_mf_coven_watch", "enc_mf_statue_hags", "enc_mf_gate_road_hag" }) j.WalkInto(e);
                j.TalkTo("mf_reeve_tamsin", "The coven is five hags fewer");
            });
            j.ClearRemaining("enc_mf_auntie_gall");
            j.Retry(false);
            j.Block("mf_hettie_soup", () =>
            {
                for (int i = 1; i <= 3; i++) j.Use("mf_truffle_" + i, "(Dig gently");
                j.Note($"   croc tails {j.S.CountItem("mf_croc_tail")}/4");
                if (j.S.CountItem("mf_croc_tail") < 4) throw new Exception($"only {j.S.CountItem("mf_croc_tail")} of 4 crocolisk tails after every crocolisk of the fen");
                j.TalkTo("mf_hettie_brine", "Four crocolisk tails");
            });
            j.Block("mf_obi_glass", () =>
            {
                j.Note($"   fen-glass {j.S.CountItem("mf_fen_glass")}/5, wisp-glow {j.S.CountItem("mf_wisp_glow")}/4");
                if (j.S.CountItem("mf_fen_glass") < 5 || j.S.CountItem("mf_wisp_glow") < 4)
                    throw new Exception($"only {j.S.CountItem("mf_fen_glass")} of 5 fen-glass and {j.S.CountItem("mf_wisp_glow")} of 4 wisp-glows after every mireling and wisp of the fen");
                j.TalkTo("mf_obi_wick", "Five lumps of fen-glass");
            });
            j.Block("mf_obi_posts", () =>
            {
                j.Offer("mf_obi_wick", "mf_obi_posts", "What will you make", "I'll hang them");
                for (int i = 1; i <= 4; i++) j.Use("mf_post_" + i, "(Hang one");
                j.WalkInto("enc_mf_post_ambush");
                j.TalkTo("mf_obi_wick", "The Fen Road is lit");
            });
            j.Block("dg5_undertow", () =>
            {
                j.Go(new Vec2(31f, 10f));
                j.Offer("mf_oracle_bloop", "dg5_undertow", "Where does the undertow go", "I'll go down and stop her");
                j.Travel("to_dgn_drowned_vault", "dgn_drowned_vault");
                foreach (var e in new[] { "enc_dg5_entry_thralls", "enc_dg5_alcove_thralls", "enc_dg5_causeway", "enc_dg5_sentinels", "enc_dg5_snapjaws", "enc_dg5_captain" }) j.WalkInto(e);
                j.WalkInto("enc_dg5_tidewitch", "Give the lights back");
                foreach (var c in j.S.MapDef.chests) if (j.S.Map.IsChestAvailable(c)) j.Chest(c.id);
                j.Travel("to_mirefen", "mirefen");
                j.Go(new Vec2(31f, 10f));
                j.TalkTo("mf_oracle_bloop", "Here is her pearl");
            });
            if (j.S.MapId == "dgn_drowned_vault") j.Travel("to_mirefen", "mirefen");
            j.Retry(true);
            j.Exit("mirefen", "mirefen", "dgn_drowned_vault");
            j.Travel("to_brightwater", "brightwater");
            j.Train();
            if (j.Shopping) j.Shop();
            j.Block("mq2_drowned_lanterns hand-in", () => j.TalkTo("bw_archivist_penhallow", "I've been to Mirefen"));
            j.Block("bw_q_fen_road hand-in", () => { if (j.S.Quests.IsActive("bw_q_fen_road")) j.TalkTo("bw_captain_rowan", "walked the fen road"); });
            j.Retry(true);
        }

        static void Skyreach(Journey j)
        {
            j.Block("mq2_ash_on_the_wind offer", () => j.Offer("bw_archivist_penhallow", "mq2_ash_on_the_wind", "Where does the ash come from"));
            j.Block("bw_q_high_road offer", () => j.Offer("bw_captain_rowan", "bw_q_high_road", "Anything else the Watch needs", "climb to Skyreach"));
            j.Travel("to_skyreach", "skyreach");
            j.Block("mq2 guide/bones/scout", () =>
            {
                if (!j.S.Quests.IsActive("mq2_ash_on_the_wind")) throw new Deferred("Ash on the Wind not started");
                j.TalkTo("sr_guide_odran", "Pleased to meet you", "Archivist Penhallow sent me", "Who are the Dragonsworn", "I'll head for the Bone Field");
                j.TalkTo("sr_ottoline", "Penhallow sent me to read the ash", "What does that mean", "I'll find Ivo");
                j.TalkTo("sr_scout_ivo", "Ottoline sent me", "I'll get those orders");
            });
            j.Block("sr offers", () =>
            {
                j.Offer("sr_pema", "sr_woolly_business", "I'll find your goats");
                j.Offer("sr_guide_odran", "sr_ogre_toll", "Pleased to meet you", "Is the road west safe", "I'll deal with Gorrum");
                j.Offer("sr_guide_odran", "sr_lamplighters_climb", "You keep looking north-east", "I'll find him");
                j.Offer("sr_brannoc", "sr_warm_coats", "Why does the village need coats", "Six pelts");
            });
            j.Block("sr_whiteout_bells offer", () => j.Offer("sr_hilde", "sr_whiteout_bells", "Why does the line outside have no bells", "I'll bring your bells back"));
            j.Block("sr_warm_coats", () =>
            {
                foreach (var e in new[] { "enc_sr_wolves_road", "enc_sr_wolf_den", "enc_sr_rimefang" }) j.WalkInto(e);
                j.Note($"   pelts {j.S.CountItem("sr_frost_wolf_pelt")}/6");
                if (j.S.CountItem("sr_frost_wolf_pelt") < 6) throw new Deferred("pelts short");
                j.TalkTo("sr_brannoc", "Six pelts, as promised");
            });
            j.Block("sr_lamplighters_climb", () =>
            {
                j.TalkTo("sr_pell", "Can you walk", "Lean on me");
                j.TalkTo("sr_guide_odran", "Brother Pell is safe");
            });
            j.Block("sr_ogre_toll", () =>
            {
                j.WalkInto("enc_sr_ogre_toll", "Then we do this the hard way");
                j.TalkTo("sr_guide_odran", "Gorrum won't be charging anyone");
            });
            j.Block("sr_woolly_business", () =>
            {
                j.TalkTo("sr_goat_bramble", "(Wait for her to come down");
                j.WalkInto("enc_sr_whitebrow", "(Attack.)|Fight");
                j.TalkTo("sr_goat_clover", "(Wake her gently");
                j.TalkTo("sr_goat_turnip", "(Charge the camp|(Take Turnip home");
                if (!j.S.Flags.IsSet("sr_goat_turnip")) j.TalkTo("sr_goat_turnip", "(Take Turnip home");
                j.TalkTo("sr_pema", "Every last goat");
            });
            j.Block("sr_whiteout_bells", () =>
            {
                foreach (var e in new[] { "enc_sr_harpies_a", "enc_sr_harpies_b", "enc_sr_tower_harpies" }) j.WalkInto(e);
                j.Note($"   bells {j.S.CountItem("sr_hut_bell")}/5");
                j.TalkTo("sr_hilde", "Five bells");
            });
            j.Block("mq2 marshal+gate", () =>
            {
                j.WalkInto("enc_sr_marshal");
                j.Go(j.S.Map.SpawnPosition("from_raid_ashwyrm_roost"));
            });
            j.Block("sr_twenty_nine", () =>
            {
                j.Offer("sr_lumi", "sr_twenty_nine", "Who are the flags for", "Is there anything I can do", "I'll go to the tower");
                j.Go(new Vec2(40.2f, 44.6f));
                if (!j.S.Map.IsEncounterDone(j.S.Map.FindEncounter("enc_sr_tower_harpies"))) j.WalkInto("enc_sr_tower_harpies");
                j.Use("sr_heron_tower", "Ysolde, where would he have kept it|Search");
                j.TalkTo("sr_lumi", "(Read the last page aloud", "Ysolde is right here");
            });
            j.Block("sr_ice_and_ember", () =>
            {
                j.Offer("sr_glimmerwing", "sr_ice_and_ember", "Hello", "What troubles you", "I'll thin the whelps");
                foreach (var e in new[] { "enc_sr_brood_shelf", "enc_sr_brood_huts", "enc_sr_drake_pen" }) j.WalkInto(e);
                j.TalkTo("sr_icicle", "(Unlock the collar");
                j.TalkTo("sr_glimmerwing", "She's safe now");
            });
            j.Block("sr_singing_bones", () =>
            {
                j.Offer("sr_ottoline", "sr_singing_bones", "What are you measuring", "I'll get your cores");
                foreach (var e in new[] { "enc_sr_bone_ice", "enc_sr_rime_drift", "enc_sr_falls_ice" }) j.WalkInto(e);
                foreach (var side in new[] { "w", "n", "e" }) j.Use("sr_stone_" + side, "(Strike the stone hard");
                j.TalkTo("sr_ottoline", "I heard it");
            });
            j.Block("sr_strike_the_colours", () =>
            {
                j.Offer("sr_scout_ivo", "sr_strike_the_colours", "Anything else I can do", "Four banners");
                for (int i = 1; i <= 4; i++) j.Use("sr_banner_" + i, "(Tear it down");
                foreach (var e in new[] { "enc_sr_vanguard_pickets", "enc_sr_vanguard_west", "enc_sr_vanguard_altar" }) j.WalkInto(e);
                j.TalkTo("sr_scout_ivo", "The mountain belongs to itself");
            });
            j.ClearRemaining();
            j.Retry(false);
            j.Block("dg6_the_long_watch", () =>
            {
                j.Offer("sr_lumi", "dg6_the_long_watch", "The knights who went into the temple", "I'll go into the Frozen Sanctum");
                if (!j.S.Flags.IsSet("found_frozen_sanctum")) j.TalkTo("sr_guide_odran", "Pleased to meet you", "Aubric's journal says");
                j.Travel("to_dgn_frozen_sanctum", "dgn_frozen_sanctum");
                foreach (var e in new[] { "enc_dg6_statue_ambush", "enc_dg6_hymn_hall", "enc_dg6_hoarfang", "enc_dg6_colossus" }) j.WalkInto(e);
                j.WalkInto("enc_dg6_vael", "Then we will end the Heart");
                j.WalkInto("enc_dg6_rimeheart");
                j.TalkTo("dg6_aubric", "Ysolde is here|(Continue");
                foreach (var c in j.S.MapDef.chests) if (j.S.Map.IsChestAvailable(c)) j.Chest(c.id);
                j.Travel("to_skyreach", "skyreach");
                j.TalkTo("sr_lumi", "They're free, Lumi");
            });
            if (j.S.MapId == "dgn_frozen_sanctum") j.Travel("to_skyreach", "skyreach");
            j.Retry(true);
            j.Exit("skyreach", "skyreach", "dgn_frozen_sanctum");
            j.Travel("to_brightwater", "brightwater");
            j.Train();
            if (j.Shopping) j.Shop();
            j.Block("mq2_ash_on_the_wind hand-in", () => j.TalkTo("bw_archivist_penhallow", "I've been to Skyreach"));
            j.Block("bw_q_high_road hand-in", () => { if (j.S.Quests.IsActive("bw_q_high_road")) j.TalkTo("bw_captain_rowan", "up the high road"); });
        }

        // ================================================================== raids

        public sealed class RaidCheck
        {
            public string Raid, Boss;
            public int Level, Recruited, TenEmptySlots;
            public float TenIlvl;
            public string Composition = "";
            public bool QuestsOffered;
            public BattleOutcome Ten, Five, TenLevelGear;
            public int TenRounds, FiveRounds;
            public override string ToString() => $"{Raid}/{Boss} at L{Level} ({Recruited} recruited, quests offered {QuestsOffered}; the ten: avg ilvl {TenIlvl:0.0}, {TenEmptySlots} empty armour/weapon slots) [{Composition}]: ten {Ten} in {TenRounds}r, five {Five} in {FiveRounds}r; the same ten in level gear (veteran rules): {TenLevelGear}";
        }

        /// <summary>The raid the player would pick (RaidPickerScreen shows the roles): Main, then the best-geared 2 tanks and
        /// 3 healers (1 and 1 below ten), then the best-geared damage dealers, then anyone left. Role first, so a poorly
        /// geared healer still makes the ten and the raid lines measure the gear gap, not a one-healer composition.</summary>
        public static List<string> PickRaid(GameSession s, int n)
        {
            var c = s.RaidCandidates();
            var pick = new List<Unit> { s.Main };
            var rest = c.Skip(1).OrderByDescending(x => Journey.AvgIlvl(new[] { x })).ToList();
            int Count(UnitRole r) => pick.Count(u => u.Role == r);
            void Fill(UnitRole role, int cap)
            {
                foreach (var u in rest)
                    if (pick.Count < n && Count(role) < cap && u.Role == role && !pick.Contains(u)) pick.Add(u);
            }
            Fill(UnitRole.Tank, n >= 10 ? 2 : 1);
            Fill(UnitRole.Healer, n >= 10 ? 3 : 1);
            foreach (var u in rest)
                if (pick.Count < n && u.Role != UnitRole.Tank && u.Role != UnitRole.Healer && !pick.Contains(u)) pick.Add(u);
            foreach (var u in c) if (pick.Count < n && !pick.Contains(u)) pick.Add(u);
            return pick.Select(s.MemberId).ToList();
        }

        /// <summary>From a save of the journey: the raid party of <paramref name="n"/> (main + the best-geared recruits) fights
        /// <paramref name="boss"/> straight away (trash pacified), the AI on everyone.</summary>
        static (BattleOutcome, int) RaidBoss(string save, string raid, string spawn, string boss, int n, ulong seed, bool levelGear = false)
        {
            var s = new GameSession(Db, seed);
            Assert(s.LoadGame(save, out var err), err);
            s.Settings.CompanionAutoPlay = true;
            var ids = PickRaid(s, n);
            var why = s.EnterRaid(raid, spawn, ids, true);
            Assert(why == null, $"enter {raid} with {n}: {why}");
            s.SetAutoPlay(s.Main, true);
            if (levelGear) foreach (var u in s.Party) StartingGear.EquipLevelGear(Db, u, s.Rng);   // what a veteran start (and the raid tests) give
            foreach (var e in s.MapDef.encounters) if (e.id != boss) s.Map.MarkEncounterDone(e.id);
            var bd = s.MapDef.encounters.First(e => e.id == boss);
            if (!string.IsNullOrEmpty(bd.requireFlag) && !bd.requireFlag.StartsWith("!")) s.Flags.Set(bd.requireFlag);
            foreach (var u in s.PartyUnits()) u.RestoreFull();
            { var be = s.Map.FindEncounter(boss); s.SetPartyPositions(be.pos + new Vec2(-be.radius - 3f, -1.5f)); }   // the party walked up to it (as TestsContentR1)
            var b = s.StartEncounter(boss);
            Assert(b != null, $"{boss} starts: {s.LastError}");
            var o = s.AutoResolve(150);
            return (o, b.Round);
        }

        static RaidCheck CheckRaid(Journey j, string raid, string spawn, string boss, string[] questGivers)
        {
            var S = j.S;
            var rc = new RaidCheck { Raid = raid, Boss = boss, Level = S.Main.Level, Recruited = S.RaidCandidates().Count };
            var tenIds = PickRaid(S, 10);
            var ten = S.RaidCandidates().Where(u => tenIds.Contains(S.MemberId(u))).ToList();
            rc.Composition = string.Join(",", ten.Select(u => $"{S.MemberId(u)}:{u.Role}"));
            rc.TenIlvl = Journey.AvgIlvl(ten);
            rc.TenEmptySlots = ten.Sum(Journey.EmptySlots);
            var save = S.SaveGame();
            // the offers: enter with ten, talk to the givers
            var s = new GameSession(Db, j.Seed + 99);
            Assert(s.LoadGame(save, out var err), err);
            var ids = s.RaidCandidates().Take(10).Select(s.MemberId).ToList();
            Assert(s.EnterRaid(raid, spawn, ids, true) == null, "enter " + raid + ": " + s.LastError);
            int offered = 0;
            foreach (var g in questGivers)
                offered += Db.Quests.Values.Count(q => q.giver == g && s.QuestMarkerOf(g).Kind == QuestMarker.Available) > 0 ? 1 : 0;
            rc.QuestsOffered = offered > 0;
            (rc.Ten, rc.TenRounds) = RaidBoss(save, raid, spawn, boss, 10, j.Seed + 7);
            (rc.Five, rc.FiveRounds) = RaidBoss(save, raid, spawn, boss, 5, j.Seed + 7);
            (rc.TenLevelGear, _) = RaidBoss(save, raid, spawn, boss, 10, j.Seed + 7, levelGear: true);
            j.Note("RAID " + rc);
            return rc;
        }

        // ================================================================== the whole journey

        public sealed class Result
        {
            public Journey J;
            public List<RaidCheck> Raids = new List<RaidCheck>();
            public string Error;
        }

        public static Result Run(ClassId cls, ulong seed, bool stopOnError = false, bool skipNorth = false, string stopAt = null, bool raids = false, bool reloadOnWipe = false)
        {
            var j = new Journey(cls, seed) { SkipNorth = skipNorth, StopAt = stopAt, ReloadOnWipe = reloadOnWipe };
            var res = new Result { J = j };
            try
            {
                j.PlaySlice();
                j.Exit("slice (1-12)");
                NorthBands(j);
                Amberfield(j);
                Brightwater(j);
                Mirefen(j);
                if (raids)
                {
                    res.Raids.Add(CheckRaid(j, "raid_hollow_heart", "from_mirefen", "enc_r1_hollow_heart", new[] { "r1_quartermaster", "r1_hinoki" }));
                    res.Raids.Add(CheckRaid(j, "raid_hollow_heart", "from_mirefen", "enc_r1_thornmaw", new[] { "r1_quartermaster" }));
                }
                Skyreach(j);
                if (raids)
                {
                    var r2givers = Db.Quests.Values.Where(q => q.id.StartsWith("r2_")).Select(q => q.giver).Distinct().ToArray();
                    res.Raids.Add(CheckRaid(j, "raid_ashwyrm_roost", "from_skyreach", "enc_r2_vyrmathra", r2givers));
                    res.Raids.Add(CheckRaid(j, "raid_ashwyrm_roost", "from_skyreach", "enc_r2_frostclaw", r2givers));
                }
            }
            catch (StopRun) { }
            catch (Exception e)
            {
                res.Error = e.Message.Split('\n')[0] + "\n      " + j.Tail(40);
                if (stopOnError) throw;
            }
            return res;
        }

        static void Print(Result r)
        {
            var j = r.J;
            Console.WriteLine($"    === journey {j.Cls} seed {j.Seed}{(j.SkipNorth ? " (north bands skipped)" : "")}: {j.Fights} fights, {j.Deaths} deaths, training cost {j.TrainGoldSpent / 100}s, " +
                              $"vendor gear {j.ItemsBought} items for {j.GearGoldSpent / 100}s, junk sold {j.JunkGoldEarned / 100}s");
            foreach (var e in j.Exits) Console.WriteLine("      " + e);
            if (j.Exits.Count > 0) Console.WriteLine("      party: " + j.Exits.Last().Party);
            foreach (var rc in r.Raids) Console.WriteLine("      " + rc);
            foreach (var p in j.Problems) Console.WriteLine("      PROBLEM " + p);
            if (j.Wipes.Count > 0) Console.WriteLine("      WIPES (reloaded) " + string.Join(", ", j.Wipes));
            if (r.Error != null) Console.WriteLine("      ERROR " + r.Error);
            if (Environment.GetEnvironmentVariable("JOURNEY_LOG") == "1") Console.WriteLine(string.Join("\n", j.Log));
        }

        static ZoneExit At(Journey j, string zone) => j.Exits.FirstOrDefault(e => e.Zone == zone);

        // ================================================================== the XP rule

        static long TotalXpOf(GameSession s)
        {
            long t = s.Main.Xp;
            for (int l = 1; l < s.Main.Level; l++) t += Progression.XpToNextLevel(Db, l);
            return t;
        }

        /// <summary>Progression.RateLevel: kill and quest XP are paid at the XP rate of the CONTENT's level (never above
        /// the receiver's), so the same content pays the same whatever level the party arrives at, and the WoW
        /// level-difference reduction makes out-levelled kills pay less.</summary>
        [Test]
        public static void Xp_PaidAtTheContentLevel()
        {
            Assert(Progression.RateLevel(18, 22) == 18 && Progression.RateLevel(22, 18) == 18 && Progression.RateLevel(0, 20) == 20 && Progression.RateLevel(5, 0) == 1,
                   "RateLevel: the content's level, capped at the receiver's (0 = the receiver's)");

            // kills: a capped Amberfield gnoll (18) pays rate 6 to a level-18 and to a level-21 character (who also gets
            // Formulas.MobXp's level-difference reduction), never the level-21 rate
            var gnoll = UnitFactory.CreateCreature(Db, Db.Creature("cr_am_gnoll"), 18);
            float mult = Progression.XpRankMult(gnoll.Creature.rank) * gnoll.Creature.xpMult;
            int k18 = Progression.KillXp(Db, 18, gnoll), k21 = Progression.KillXp(Db, 21, gnoll);
            Assert(k18 == (int)Math.Round(Formulas.MobXp(18, 18) * mult * 6f), $"a level-18 gnoll pays a level-18 character at rate 6 ({k18})");
            Assert(k21 == (int)Math.Round(Formulas.MobXp(21, 18) * mult * 6f) && k21 < k18, $"and a level-21 character at rate 6 too, less for the level difference ({k21} < {k18})");
            // ... and a character below the creature is paid at its own level's rate (the WoW formula adds 5 % a level)
            int k15 = Progression.KillXp(Db, 15, gnoll);
            Assert(k15 == (int)Math.Round(Formulas.MobXp(15, 18) * mult * Progression.XpRate(Db, 15)), $"a level-15 character: rate 5 ({k15})");
            // the 1-12 slice keeps its pacing: its creatures scale above a level-12 party, still rate 4
            var warden = UnitFactory.CreateCreature(Db, Db.Creature("cr_hollow_warden"), 14);
            Assert(Progression.KillXp(Db, 12, warden) == (int)Math.Round(Formulas.MobXp(12, 14) * Progression.XpRankMult(warden.Creature.rank) * warden.Creature.xpMult * 4f),
                   "the Hollow Warden at 14 pays a level-12 party rate 4");

            // quests: minLevel, else the zone's band bottom, else the quest's own level
            Assert(Progression.QuestLevel(Db, Db.Quests["am_q_barrow"]) == 17, "am_q_barrow: its minLevel");
            Assert(Progression.QuestLevel(Db, Db.Quests["mq2_ember_road"]) == 12, "mq2_ember_road (no minLevel): Amberfield's band bottom");
            Assert(Progression.QuestLevel(Db, Db.Quests["sq_bridge"]) == Db.Quests["sq_bridge"].level, "a slice quest (no minLevel, no zone): its level");

            // the session: a quest's reward is paid at the quest's level, whatever the party's level
            var q = Db.Quests["am_q_harvest"];
            Assert(q.minLevel == 16 && q.stages.All(st => st.onComplete == null || st.onComplete.All(o => o.type != OutcomeType.GiveXP)), "am_q_harvest: minLevel 16, reward only");
            long Paid(int level)
            {
                var s = SessionTest.NewGame(ClassId.Mage, level, seed: 7);
                long before = TotalXpOf(s);
                Assert(s.Quests.Complete(q.id), "complete " + q.id);
                return TotalXpOf(s) - before;
            }
            long p16 = Paid(16), p21 = Paid(21), expected = (int)Math.Round(q.rewards.xp * Progression.XpRate(Db, 16));
            Assert(p16 == expected && p21 == expected, $"The Harvest Lanes pays {expected} at 16 and at 21 (got {p16} / {p21})");
            // other data XP (a dialogue's GiveXP) at the band bottom of the map it happens on
            var m = SessionTest.NewGame(ClassId.Mage, 21, seed: 7);
            m.Teleport("mirefen", "default");
            long b = TotalXpOf(m);
            m.GiveXP(100);
            Assert(m.MapId == "mirefen" && TotalXpOf(m) - b == 600, $"GiveXP(100) in Mirefen (band 18-24) at 21 = 600 ({TotalXpOf(m) - b})");
        }

        // ================================================================== the pacing tests

        /// <summary>Zone exits of the full route and their level windows (see the file header).</summary>
        public static readonly (string zone, int min, int max)[] FullWindows =
            { ("north bands + dg1-3", 14, 15), ("amberfield", 18, 20), ("mirefen", 24, 26), ("skyreach", 30, 31) };
        /// <summary>Zone exits of the lean route (north bands skipped) and their level windows.</summary>
        public static readonly (string zone, int min, int max)[] LeanWindows =
            { ("amberfield", 17, 19), ("mirefen", 23, 25), ("skyreach", 29, 31) };

        static List<string> PacingFailures(Result r, (string zone, int min, int max)[] windows)
        {
            var j = r.J;
            var fails = new List<string>();
            if (r.Error != null) fails.Add("the journey runs to the end of Skyreach: " + r.Error);
            foreach (var (zone, min, max) in windows)
            {
                var e = At(j, zone);
                if (e == null) fails.Add($"{zone}: no exit recorded");
                else if (e.Level < min || e.Level > max) fails.Add($"{zone} exit at L{e.Level} (+{e.XpGained} XP), window {min}-{max}");
            }
            // no grinding: every level gate on the route opens by itself, and the main story's three chapters are done
            foreach (var p in j.Problems.Where(p => p.Contains("needs level"))) fails.Add("a level gate the route does not reach: " + p);
            if (r.Error == null)
                foreach (var q in new[] { "mq2_ember_road", "mq2_drowned_lanterns", "mq2_ash_on_the_wind" })
                    if (!j.S.Quests.IsCompleted(q)) fails.Add(q + " completed");
            return fails;
        }

        static void PacingTest((ClassId cls, ulong seed)[] runs, bool skipNorth)
        {
            var fails = new List<string>();
            foreach (var (cls, seed) in runs)
            {
                var r = Run(cls, seed, skipNorth: skipNorth, reloadOnWipe: true);
                Print(r);
                fails.AddRange(PacingFailures(r, skipNorth ? LeanWindows : FullWindows).Select(f => $"{cls} seed {seed}: {f}"));
            }
            Assert(fails.Count == 0, $"{fails.Count} pacing checks failed:\n      " + string.Join("\n      ", fails));
        }

        /// <summary>The full route (north bands and their dungeons first), two class mixes: each exit inside its window.
        /// Review before Progression.RateLevel: north L18, Amberfield L23, Mirefen L28, Skyreach L34.</summary>
        [Test]
        public static void Journey_XpPacing_FullRoute() =>
            PacingTest(new[] { (ClassId.Warrior, 1001UL), (ClassId.Hunter, 4004UL) }, false);

        /// <summary>The lean route (straight from the slice to Amberfield), two class mixes: each exit inside its window,
        /// every level gate reached without grinding. Review before: Amberfield L19, Mirefen L25, Skyreach L32.</summary>
        [Test]
        public static void Journey_XpPacing_LeanRoute() =>
            PacingTest(new[] { (ClassId.Warrior, 3004UL), (ClassId.Mage, 3007UL) }, true);

        // ================================================================== reports (--sim)

        /// <summary>Balance report (--sim): the canonical journey (a Warrior and the slice's companions, the whole expansion,
        /// the raids checked from its saves), every way it falls short of Docs/Expansion.md §1/§8 listed (no asserts:
        /// boss balance, gear and quest softlocks belong to their own tests).</summary>
        [Sim]
        public static void Journey_Report()
        {
            var r = Run(ClassId.Warrior, 1001, raids: true);
            Print(r);
            var j = r.J;
            var notes = new List<string>(PacingFailures(r, FullWindows));
            foreach (var (id, lvl, empty) in j.Recruits)
                if (empty > 2) notes.Add($"{id} recruited at L{lvl} with {empty} of 9 armour/weapon slots empty");
            foreach (var p in j.Problems) notes.Add("journey problem: " + p);
            foreach (var rc in r.Raids)
            {
                if (!rc.QuestsOffered) notes.Add($"{rc.Raid}: its quests are not offered at L{rc.Level}");
                if (rc.Recruited < 10) notes.Add($"{rc.Raid}: only {rc.Recruited} heroes to choose from");
                if (rc.Ten != BattleOutcome.Victory) notes.Add($"{rc.Raid}/{rc.Boss}: the journey's ten lose at L{rc.Level} ({rc.Ten})");
                if (rc.Five == BattleOutcome.Victory) notes.Add($"{rc.Raid}/{rc.Boss}: five win");
            }
            Console.WriteLine($"    {notes.Count} findings" + (notes.Count > 0 ? ":\n      " + string.Join("\n      ", notes) : ""));
        }

        /// <summary>Balance report (--sim): the lean route for every main class (wipes reloaded): levels at the exits.</summary>
        [Sim]
        public static void Journey_LeanRoute()
        {
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Paladin, ClassId.Hunter, ClassId.Rogue, ClassId.Priest, ClassId.Shaman, ClassId.Mage, ClassId.Warlock })
                Print(Run(c, 3003 + (ulong)c, skipNorth: true, reloadOnWipe: true));
        }

        /// <summary>Balance report (--sim): the full journey with other mains and seeds (JOURNEY_CLASSES), wipes reloaded.</summary>
        [Sim]
        public static void Journey_ClassMixes()
        {
            var list = (Environment.GetEnvironmentVariable("JOURNEY_CLASSES") ?? "Priest,Mage,Hunter,Paladin").Split(',');
            ulong seed = 2002;
            foreach (var c in list)
            {
                Print(Run((ClassId)Enum.Parse(typeof(ClassId), c), seed, reloadOnWipe: true));
                seed += 1001;
            }
        }

        /// <summary>Balance report (--sim): XP of each zone and its dungeon totalled from the data with the real formulas
        /// (Progression.KillXp's rule; each encounter once, at the running level; quests at Progression.QuestLevel),
        /// against Docs/Expansion.md §8's budgets. Dialogue XP outside quests is left out (a few % of a zone).</summary>
        [Sim]
        public static void Xp_ZoneBudgets()
        {
            var zones = new (string name, string[] maps, int from, int budget)[]
            {
                ("north bands + dg1-3", new[] { "lanternvale", "dgn_root_hollows", "whisperwood", "dgn_mossdeep", "shrine", "dgn_lantern_catacombs" }, 12, 0),
                ("amberfield + barrow", new[] { "amberfield", "dgn_barrow" }, 12, 85000),
                ("brightwater", new[] { "brightwater" }, 18, 0),
                ("mirefen + vault", new[] { "mirefen", "dgn_drowned_vault" }, 18, 150000),
                ("skyreach + sanctum", new[] { "skyreach", "dgn_frozen_sanctum" }, 24, 220000),
            };
            foreach (var (name, maps, from, budget) in zones)
            {
                int level = from; long xp = 0, total = 0, kills = 0, quests = 0;
                void Give(double amount, bool kill)
                {
                    long a = (long)Math.Round(amount);
                    total += a; if (kill) kills += a; else quests += a;
                    xp += a;
                    while (xp >= Progression.XpToNextLevel(Db, level)) { xp -= Progression.XpToNextLevel(Db, level); level++; }
                }
                foreach (var mapId in maps)
                    foreach (var e in Db.Maps[mapId].encounters)
                    {
                        if (mapId == "lanternvale" || mapId == "whisperwood" || mapId == "shrine")
                            if (e.pos.y <= 15f) continue;   // the slice's own band
                        foreach (var x in e.enemies)
                        {
                            var c = Db.Creatures[x.creature];
                            int ml = UnitFactory.CreatureLevel(c, x.level, level, null);
                            Give(Formulas.MobXp(level, ml) * Progression.XpRankMult(c.rank) * c.xpMult * Progression.XpRate(Db, Progression.RateLevel(ml, level)), true);
                        }
                    }
                foreach (var q in Db.Quests.Values.Where(q => maps.Contains(q.zone)))
                {
                    int ql = Progression.QuestLevel(Db, q);
                    foreach (var st in q.stages)
                        if (st.onComplete != null)
                            foreach (var o in st.onComplete.Where(o => o.type == OutcomeType.GiveXP)) Give(Progression.ContentXp(Db, o.amount, ql, Math.Max(level, ql)), false);
                    if (q.rewards != null) Give(Progression.ContentXp(Db, q.rewards.xp, ql, Math.Max(level, ql)), false);
                }
                Console.WriteLine($"    {name,-22} from L{from}: {total,7} XP ({kills} kills, {quests} quests) -> L{level}" + (budget > 0 ? $"; §8 budget ~{budget}" : ""));
            }
        }

        /// <summary>Balance report (--sim): every main class, three seeds, through the slice and the north bands with their
        /// dungeons (no reloads): who survives the Rootwarden, the Mossking and the Lantern Lich at the level the journey
        /// brings?</summary>
        [Sim]
        public static void Journey_NorthBosses()
        {
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Paladin, ClassId.Hunter, ClassId.Rogue, ClassId.Priest, ClassId.Shaman, ClassId.Mage, ClassId.Warlock })
            {
                var notes = new List<string>();
                for (ulong k = 0; k < 3; k++)
                {
                    var r = Run(c, 9100 + 17 * k + (ulong)c, stopAt: "shrine");
                    string F(string e) => r.J.FightLog.TryGetValue(e, out var f) ? $"{f.deaths}d" : "-";
                    notes.Add($"L{r.J.S?.Main.Level} {(r.Error == null ? "ok" : "WIPE")} [rootwarden {F("enc_dg1_rootwarden")}, mossking {F("enc_dg2_mossking")}, lich {F("enc_dg3_lantern_lich")}]");
                }
                Console.WriteLine($"    {c}: " + string.Join(" | ", notes));
            }
        }

        /// <summary>Balance report (--sim): the first hidden dungeon with the party the slice really gives: a Mage main
        /// (+ Kael, Aldric, Seren, Rook) through the Lanternvale north band and the Root Hollows.</summary>
        [Sim]
        public static void Journey_Mage_RootHollowsWithTheSliceParty()
        {
            var r = Run(ClassId.Mage, 9100 + (ulong)ClassId.Mage, stopAt: "lanternvale");
            Print(r);
            var f = r.J.FightLog.TryGetValue("enc_dg1_rootwarden", out var x) ? $"{x.rounds} rounds, low {x.minHp:P0}, {x.deaths} down" : "not fought";
            Console.WriteLine($"    Root Hollows cleared: {r.Error == null && r.J.S.Quests.IsCompleted("dg1_heart_of_kusu")} at L{r.J.S.Main.Level} (Rootwarden: {f})");
        }

        /// <summary>Balance report (--sim): straight from the slice to Mirefen (no north bands), a Warlock main: the Drowned
        /// Vault's Tidewitch at the level the lean route brings.</summary>
        [Sim]
        public static void Journey_Warlock_LeanRouteThroughTheDrownedVault()
        {
            var r = Run(ClassId.Warlock, 7300 + (ulong)ClassId.Warlock * 31, skipNorth: true, stopAt: "mirefen");
            Print(r);
            var f = r.J.FightLog.TryGetValue("enc_dg5_tidewitch", out var x) ? $"{x.rounds} rounds, low {x.minHp:P0}, {x.deaths} down" : "not fought";
            Console.WriteLine($"    Drowned Vault cleared: {r.Error == null && r.J.S.Quests.IsCompleted("dg5_undertow")} at L{r.J.S.Main.Level} (Tidewitch: {f})");
        }

        /// <summary>Balance report (--sim): every main class, three seeds, through the slice and the Lanternvale north band
        /// and the Root Hollows (the first hidden dungeon): who survives the Rootwarden with the party the slice gives?</summary>
        [Sim]
        public static void Journey_FirstHiddenDungeon()
        {
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Paladin, ClassId.Hunter, ClassId.Rogue, ClassId.Priest, ClassId.Shaman, ClassId.Mage, ClassId.Warlock })
            {
                int ok = 0, n = 0;
                var notes = new List<string>();
                for (ulong k = 0; k < 3; k++)
                {
                    var r = Run(c, 9100 + 17 * k + (ulong)c, stopAt: "lanternvale");
                    n++;
                    var boss = r.J.FightLog.TryGetValue("enc_dg1_rootwarden", out var f) ? $"{f.rounds}r low {f.minHp:P0} {f.deaths} down" : "-";
                    if (r.Error == null && r.J.Problems.Count == 0) ok++;
                    notes.Add($"L{r.J.S?.Main.Level} ilvl {Journey.AvgIlvl(r.J.S.Party):0.0}: {(r.Error == null && r.J.Problems.Count == 0 ? "ok" : "FAIL " + (r.J.Problems.FirstOrDefault() ?? r.Error).Split('\n')[0])} [rootwarden {boss}]");
                }
                Console.WriteLine($"    {c}: {ok}/{n} through the Root Hollows; " + string.Join(" | ", notes));
            }
        }

        /// <summary>Balance report (--sim): the lean route to the end of Mirefen for every main class: the zone and
        /// dungeon bosses at the level and with the gear the journey gives (no reloads).</summary>
        [Sim]
        public static void Journey_BossesOnTheLeanRoute()
        {
            var bosses = new[] { "enc_am_warchief", "enc_dg4_king_aldwin", "enc_mf_auntie_gall", "enc_dg5_tidewitch" };
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Paladin, ClassId.Hunter, ClassId.Rogue, ClassId.Priest, ClassId.Shaman, ClassId.Mage, ClassId.Warlock })
            {
                var r = Run(c, 7300 + (ulong)c * 31, skipNorth: true, stopAt: "mirefen");
                var j = r.J;
                var parts = bosses.Select(b => j.FightLog.TryGetValue(b, out var f) ? $"{b.Substring(4)} {f.rounds}r low {f.minHp:P0} {f.deaths} down" : b.Substring(4) + " -");
                string end = r.Error != null ? "ERROR " + r.Error.Split('\n')[0] : "ok";
                Console.WriteLine($"    {c}: L{j.S?.Main.Level} ilvl {Journey.AvgIlvl(j.S.Party):0.0}; {string.Join("; ", parts)}; {end}");
            }
        }

        /// <summary>Balance report (--sim): the "natural" ten (everyone recruited on the main path: the slice's four, Torvan,
        /// and the expansion's Bruna, Ysolde, Liora, Nanami; no detour for Pip, Lys or Morwen) vs the raid tests' ten
        /// (RaidTest.Companions order: six damage dealers), both in level gear (veteran rules) at the raid's band.</summary>
        [Sim]
        public static void Journey_NaturalTenVsRaidBosses()
        {
            var natural = new[] { GameSession.MainId, "kael", "aldric", "seren", "rook", "torvan", "bruna", "ysolde", "liora", "nanami" };
            foreach (var (raid, spawn, lvl, bosses) in new[]
            {
                ("raid_hollow_heart", "from_mirefen", 23, new[] { "enc_r1_thornmaw", "enc_r1_twins", "enc_r1_mother_mire", "enc_r1_hollow_heart" }),
                ("raid_ashwyrm_roost", "from_skyreach", 32, new[] { "enc_r2_frostclaw", "enc_r2_cinder_drakes", "enc_r2_varkas", "enc_r2_vyrmathra" }),
            })
                foreach (var main in new[] { ClassId.Warrior, ClassId.Priest, ClassId.Mage })
                {
                    var g = RaidTest.Game(main, lvl, 61 + (ulong)main);
                    g.Settings.CompanionAutoPlay = true;
                    var save = g.SaveGame();
                    var roles = string.Join(",", natural.Select(id => g.Roster.First(u => g.MemberId(u) == id)).Select(u => u.Role.ToString().Replace("Dps", "")));
                    var line = new List<string>();
                    foreach (var b in bosses)
                    {
                        int nat = 0, std = 0;
                        for (ulong k = 0; k < 3; k++)
                        {
                            if (RaidBossWith(save, raid, spawn, b, natural, 100 + k) == BattleOutcome.Victory) nat++;
                            if (RaidBossWith(save, raid, spawn, b, RaidTest.Ids(10), 100 + k) == BattleOutcome.Victory) std++;
                        }
                        line.Add($"{b.Substring(7)} natural {nat}/3, tests' ten {std}/3");
                    }
                    Console.WriteLine($"    {raid} at {lvl}, {main} main [natural roles {roles}]: " + string.Join("; ", line));
                }
        }

        static BattleOutcome RaidBossWith(string save, string raid, string spawn, string boss, IList<string> ids, ulong seed)
        {
            var s = new GameSession(Db, seed);
            Assert(s.LoadGame(save, out var err), err);
            s.Rng.s0 = 0x9E3779B97F4A7C15UL * (seed + 1); s.Rng.s1 = 0xBF58476D1CE4E5B9UL ^ seed;
            Assert(s.EnterRaid(raid, spawn, ids.ToList(), true) == null, s.LastError);
            s.SetAutoPlay(s.Main, true);
            foreach (var e in s.MapDef.encounters) if (e.id != boss) s.Map.MarkEncounterDone(e.id);
            var bd = s.MapDef.encounters.First(e => e.id == boss);
            if (!string.IsNullOrEmpty(bd.requireFlag) && !bd.requireFlag.StartsWith("!")) s.Flags.Set(bd.requireFlag);
            foreach (var u in s.PartyUnits()) u.RestoreFull();
            { var be = s.Map.FindEncounter(boss); s.SetPartyPositions(be.pos + new Vec2(-be.radius - 3f, -1.5f)); }   // the party walked up to it (as TestsContentR1)
            var b = s.StartEncounter(boss);
            Assert(b != null, $"{boss} starts: {s.LastError}");
            return s.AutoResolve(150);
        }

        /// <summary>Balance report (--sim): RaidBoss's save/load path against the raid tests' own ten (control).</summary>
        [Sim]
        public static void Journey_RaidControl()
        {
            foreach (int lvl in new[] { 22, 26 })
                foreach (var main in new[] { ClassId.Paladin, ClassId.Warrior })
                {
                    var s = RaidTest.Game(main, lvl, 61);
                    s.Settings.CompanionAutoPlay = true;
                    var save = s.SaveGame();
                    var ten = string.Join(",", s.RaidCandidates().Take(10).Select(u => $"{s.MemberId(u)}:{u.ClassId}"));
                    var (o1, r1) = RaidBoss(save, "raid_hollow_heart", "from_mirefen", "enc_r1_thornmaw", 10, 7);
                    var (o2, r2) = RaidBoss(save, "raid_hollow_heart", "from_mirefen", "enc_r1_hollow_heart", 10, 7);
                    Console.WriteLine($"    control {main} at {lvl} [{ten}]: thornmaw {o1} {r1}r, hollow heart {o2} {r2}r");
                }
        }

        /// <summary>Balance report (--sim): the gear the raid tests give (veteran start at 22 / 32) vs what the journey's ten wear.</summary>
        [Sim]
        public static void Journey_RaidTestGear()
        {
            foreach (int lvl in new[] { 22, 26, 32 })
            {
                var s = RaidTest.Game(ClassId.Paladin, lvl, 61);
                var ten = s.RaidCandidates().Take(10).ToList();
                Console.WriteLine($"    RaidTest.Game at {lvl}: the ten average ilvl {Journey.AvgIlvl(ten):0.0}, {ten.Sum(Journey.EmptySlots)} empty armour/weapon slots");
            }
        }

        /// <summary>Balance report (--sim): replays JOURNEY_SNAPENC from every save in JOURNEY_SNAPDIR, 4 RNG seeds each
        /// (run Journey_FirstHiddenDungeon / Journey_BossesOnTheLeanRoute first with the same variables to write them).</summary>
        [Sim]
        public static void Journey_ReplayBoss()
        {
            var dir = Environment.GetEnvironmentVariable("JOURNEY_SNAPDIR");
            var enc = Environment.GetEnvironmentVariable("JOURNEY_SNAPENC");
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(enc)) { Console.WriteLine("    set JOURNEY_SNAPDIR and JOURNEY_SNAPENC"); return; }
            int total = 0, wins = 0;
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.json").OrderBy(x => x))
            {
                var save = System.IO.File.ReadAllText(f);
                var res = new List<string>();
                for (ulong k = 0; k < 4; k++)
                {
                    var s = new GameSession(Db, 77 + k);
                    Assert(s.LoadGame(save, out var err), err);
                    s.Rng.s0 = 0x9E3779B97F4A7C15UL * (k + 1); s.Rng.s1 = 0xBF58476D1CE4E5B9UL ^ k;
                    s.Settings.CompanionAutoPlay = true;
                    foreach (var u in s.Roster) s.SetAutoPlay(u, true);
                    foreach (var u in s.PartyUnits()) u.RestoreFull();
                    var e = s.Map.FindEncounter(enc);
                    s.SetPartyPositions(e.pos + new Vec2(-e.radius - 3f, -1.5f));
                    var b = s.StartEncounter(enc);
                    Assert(b != null, enc + ": " + s.LastError);
                    var o = s.AutoResolve(100);
                    total++; if (o == BattleOutcome.Victory) wins++;
                    res.Add($"{o} {b.Round}r");
                }
                Console.WriteLine($"    {System.IO.Path.GetFileNameWithoutExtension(f)}: {string.Join(", ", res)}");
            }
            Console.WriteLine($"    {enc}: {wins}/{total} won");
        }
    }

    static class JourneyExt
    {
        public static void Check0(this TestsExpansionJourney.Journey j, bool c, string m) { if (!c) throw new Exception(m); }
    }
}
