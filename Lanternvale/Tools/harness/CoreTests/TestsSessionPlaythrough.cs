// GameSession: a scripted mini-playthrough of the main quest's first stages through the real content —
// opening, recruiting Kael by dialogue, Elder Maru, travel, the Wayside Shrine, Komorebi, wisp embers,
// Rotheart and the Old Shrine — with a save/load round trip in the middle.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    public static class TestsSessionPlaythrough
    {
        [Test]
        public static void MainQuest_FirstStages()
        {
            var s = SessionTest.NewGame(ClassId.Paladin, 12, seed: 401, opening: true, name: "Yuna");

            // opening → quest
            SessionTest.Pick(s, "Grey? Like ash?");
            SessionTest.Pick(s, "I'll help");
            SessionTest.Finish(s);
            Harness.Assert(s.Quests.GetStage("mq_lanterns") == "elder", "stage elder");

            // recruit Kael through his dialogue
            var kaelNpc = s.Map.Def.npcs.Find(n => n.npc == "kael");
            SessionTest.WalkTo(s, kaelNpc.pos + new Vec2(-1.5f, 0));
            Harness.Assert(s.TalkTo("kael").Ok, "talk to Kael");
            SessionTest.Pick(s, "Come with me");
            SessionTest.Pick(s, "Welcome aboard");
            SessionTest.Finish(s);
            Harness.Assert(s.CompanionStatusOf("kael") == CompanionStatus.Active && s.Flags.IsSet("kael_met"), "Kael recruited by dialogue");
            Harness.Assert(!s.VisibleNpcs().Exists(n => n.npc == "kael"), "Kael's map npc hidden");
            s.Recruit("aldric");   // a healer-ish second companion keeps the test robust

            // Elder Maru → wayshrine
            var elder = s.Map.Def.npcs.Find(n => n.npc == "elder_maru");
            SessionTest.WalkTo(s, elder.pos + new Vec2(1.5f, -1f));
            s.TalkTo("elder_maru");
            SessionTest.Pick(s, "Where do I start");
            SessionTest.Pick(s, "keep it safe");
            SessionTest.Finish(s);
            Harness.Assert(s.Quests.GetStage("mq_lanterns") == "wayshrine" && s.CountItem("kindling_taper") == 1, "stage wayshrine");

            // save/load mid-way
            var json = s.SaveGame();
            Harness.Assert(s.LoadGame(json, out var err), "reload: " + err);
            Harness.Assert(s.SaveGame() == json, "identical after reload");

            // to Whisperwood by walking into the east transition
            var east = s.Map.Def.transitions.Find(t => t.id == "to_whisperwood");
            var tr = SessionTest.WalkTo(s, east.pos);
            Harness.Assert(s.MapId == "whisperwood", $"travelled to Whisperwood ({tr})");

            // the Wayside Shrine (Reach) → komorebi
            int xp = s.Main.Xp + s.Main.Level * 1000000;
            SessionTest.WalkTo(s, new Vec2(41f, 9.5f));
            Harness.Assert(s.Quests.GetStage("mq_lanterns") == "komorebi", $"stage komorebi (at {s.Quests.GetStage("mq_lanterns")}, leader {s.Leader.Position})");
            Harness.Assert(s.Main.Xp + s.Main.Level * 1000000 > xp, "stage reward XP");

            // Komorebi
            Harness.Assert(s.TalkTo("komorebi").Ok, "talk to Komorebi");
            SessionTest.Pick(s, "How can I help");
            SessionTest.Pick(s, "I'll bring them");
            SessionTest.Finish(s);
            Harness.Assert(s.Flags.IsSet("met_komorebi") && s.Quests.GetStage("mq_lanterns") == "embers", "stage embers");

            // wisps drop spirit embers
            foreach (var encId in new[] { "enc_wisps_grove", "enc_wisps_hollow" })
            {
                if (s.CountItem("spirit_ember") >= 3) break;
                SessionTest.Refresh(s);
                var enc = s.Map.FindEncounter(encId);
                SessionTest.WalkTo(s, enc.pos);
                Harness.Assert(s.Map.IsEncounterDone(enc), $"{encId} defeated");
            }
            Harness.Assert(s.CountItem("spirit_ember") >= 3, $"three embers ({s.CountItem("spirit_ember")})");
            Harness.Assert(s.Quests.GetStage("mq_lanterns") == "embers_return", "collect objective done");

            // back to Komorebi: offer the embers → rotheart
            var kom = s.Map.Def.npcs.Find(n => n.npc == "komorebi");
            SessionTest.WalkTo(s, kom.pos + new Vec2(-1.5f, -1f));
            int embers = s.CountItem("spirit_ember");
            s.TalkTo("komorebi");
            SessionTest.Pick(s, "Offer the Spirit Embers");
            SessionTest.Finish(s, "do what I can");
            Harness.Assert(s.Flags.IsSet("embers_gathered") && s.Quests.GetStage("mq_lanterns") == "rotheart", "stage rotheart");
            Harness.Assert(s.CountItem("spirit_ember") == embers - 3, "three embers handed over");

            // Rotheart (now available) → shrine stage, stair unlocked
            var shrineLocked = s.UseTransition("to_shrine");
            Harness.Assert(!shrineLocked.Ok, "stair still blocked before Rotheart");
            SessionTest.Refresh(s);
            var rot = s.Map.FindEncounter("enc_rotheart");
            Harness.Assert(s.Map.IsEncounterAvailable(rot), "Rotheart awakens once the embers are gathered");
            SessionTest.WalkTo(s, rot.pos);
            Harness.Assert(s.Flags.IsSet("rotheart_defeated"), "Rotheart defeated");
            Harness.Assert(s.Quests.GetStage("mq_lanterns") == "shrine", "stage shrine");

            // the Old Shrine: arriving inside the approach region completes the Reach objective
            var go = s.UseTransition("to_shrine");
            Harness.Assert(go.Ok && s.MapId == "shrine", "climbed to the shrine");
            s.MoveLeader(s.Leader.Position + new Vec2(1f, 0f));
            Harness.Assert(s.Quests.GetStage("mq_lanterns") == "warden", $"stage warden (at {s.Quests.GetStage("mq_lanterns")})");
            Harness.Assert(s.Flags.IsSet("reg_shrine_approach"), "approach region flag");
            Harness.Assert(s.TrySaveGame(out var end, out var why), "save at the shrine: " + why);
        }
    }
}
