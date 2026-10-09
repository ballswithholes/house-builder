using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lanternvale.Data;
using Lanternvale.Util;
using Lanternvale.World;
using Lanternvale.Tests;

static class Sim
{
    static GameDatabase db; static FakeWorldContext ctx; static DialogueRunner run; static int fails;
    static void Check(bool ok, string msg) { if (!ok) { fails++; Console.WriteLine("  !! FAIL: " + msg); } else Console.WriteLine("  ok: " + msg); }
    static string Short(string s) => s.Length > 90 ? s.Substring(0, 90) + "…" : s;

    static void Talk(string dlg, string owner, params string[] picks)
    {
        Console.WriteLine($"-- {dlg} ({owner})");
        if (!run.Start(dlg, owner)) { Check(false, "start " + dlg); return; }
        int pi = 0, guard = 0;
        while (run.IsActive && guard++ < 120)
        {
            var v = run.Current;
            Console.WriteLine($"   [{v.NodeId}] {v.SpeakerName}: {Short(v.Text)}");
            if (v.Choices.Count == 0) { run.Continue(); continue; }
            if (pi >= picks.Length) { Console.WriteLine("   (end) choices: " + string.Join(" | ", v.Choices.Select(c => c.DisplayText))); run.End(); break; }
            var want = picks[pi++];
            int idx = v.Choices.FindIndex(c => c.DisplayText.Contains(want));
            if (idx < 0) { Check(false, $"choice '{want}' in [{string.Join(" | ", v.Choices.Select(c => c.DisplayText))}]"); run.End(); break; }
            Console.WriteLine($"    > {v.Choices[idx].DisplayText}");
            run.Choose(idx);
            if (run.LastCheck != null) Console.WriteLine("      roll: " + run.LastCheck);
        }
    }
    static void Stage(string q, string stage) => Check(ctx.Quests.GetStage(q) == stage && ctx.Quests.IsActive(q), $"{q} at stage {stage} (is {ctx.Quests.GetStage(q)}, {ctx.Quests.GetStatus(q)})");
    static void Done(string q) => Check(ctx.Quests.IsCompleted(q), $"{q} completed ({ctx.Quests.GetStatus(q)})");
    static void Reach(string map, float x, float y) { var m = ctx.World.GetMap(map); m.OnEnterMap(); m.UpdatePartyPosition(new Vec2(x, y)); }
    static void Kill(string map, string enc)
    {
        var m = ctx.World.GetMap(map); var e = m.FindEncounter(enc);
        Check(m.IsEncounterAvailable(e), $"encounter {enc} available");
        foreach (var en in e.enemies) ctx.Quests.OnKill(en.creature);
        m.MarkEncounterDone(enc);
    }

    static int Main(string[] args)
    {
        var dir = "/home/user/house-builder/Lanternvale/Assets/Lanternvale/Resources/Data";
        db = GameDatabase.Load(Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(x => x)
            .Select(f => new KeyValuePair<string, string>(f, File.ReadAllText(f))));
        ctx = new FakeWorldContext(db) { GoldValue = 1000, Bonus = 40 };   // huge bonus: checks pass except natural 1
        run = ctx.World.CreateDialogueRunner(new Rng(7));

        // ---------------- main quest
        Talk("dlg_opening", "", "Whatever is doing this", "I'll help.");
        Stage("mq_lanterns", "elder");
        Talk("dlg_elder_maru", "elder_maru", "Where do I start?", "Any advice", "Goodbye.");
        Stage("mq_lanterns", "wayshrine"); Check(ctx.CountItem("kindling_taper") == 1, "has Kindling Taper");
        Reach("whisperwood", 41f, 9.5f);
        Stage("mq_lanterns", "komorebi");
        Talk("dlg_komorebi", "komorebi", "What happened to the Warden?", "[RELIGION]", "How can I help?", "I'll bring them.", "Goodbye.");
        Stage("mq_lanterns", "embers");
        Kill("whisperwood", "enc_wisps_grove"); ctx.GiveItem("spirit_ember", 2); ctx.GiveItem("spirit_ember", 1);
        Stage("mq_lanterns", "embers_return");
        Check(!ctx.World.GetMap("whisperwood").IsEncounterAvailable(ctx.World.GetMap("whisperwood").FindEncounter("enc_rotheart")), "Rotheart hidden before embers");
        Talk("dlg_komorebi", "komorebi", "Offer the Spirit Embers", "I'll do what I can.");
        Stage("mq_lanterns", "rotheart");
        var ww = ctx.World.GetMap("whisperwood");
        Check(!ww.IsTransitionUnlocked(ww.Def.transitions.First(t => t.id == "to_shrine")), "shrine transition locked before Rotheart");
        Kill("whisperwood", "enc_rotheart");
        Check(ww.IsTransitionUnlocked(ww.Def.transitions.First(t => t.id == "to_shrine")), "shrine transition unlocked after Rotheart");
        Stage("mq_lanterns", "shrine");
        Reach("shrine", 4f, 6f);
        Stage("mq_lanterns", "warden");
        Talk("dlg_keeper_ishiro", "", "Stand aside");
        Talk("dlg_warden_confront", "", "Then come and take it.");
        Kill("shrine", "enc_warden");
        Stage("mq_lanterns", "rekindle");
        Check(ctx.World.GetMap("shrine").IsNpcVisible(ctx.World.GetMap("shrine").Def.npcs.First(n => n.npc == "warden_spirit")), "warden spirit visible");
        Talk("dlg_warden_spirit", "warden_spirit", "This taper carries", "Rest now");
        Stage("mq_lanterns", "home");
        Check(ctx.Flags.IsSet("lanterns_rekindled"), "lanterns_rekindled set");
        Talk("dlg_elder_maru", "elder_maru", "The Warden is at peace", "Goodbye.");
        Done("mq_lanterns");
        Check(ctx.Quests.PendingRewardChoices.Contains("mq_lanterns"), "main quest reward choice pending");

        // ---------------- shepherd
        Talk("dlg_shepherd_bram", "shepherd_bram", "Is something wrong?", "I'll handle it.", "Goodbye.");
        Stage("sq_shepherd", "wolves");
        Kill("lanternvale", "enc_pasture_wolves");
        Stage("sq_shepherd", "report");
        Talk("dlg_shepherd_bram", "shepherd_bram", "What are they running from?", "I'll find him.");
        Stage("sq_shepherd", "greymane");
        Kill("whisperwood", "enc_greymane");
        Stage("sq_shepherd", "return");
        Talk("dlg_shepherd_bram", "shepherd_bram", "Greymane won't", "Goodbye.");
        Done("sq_shepherd");

        // ---------------- little lost light
        Talk("dlg_child_nell", "child_nell", "I'll look for him.", "I'll bring him home.", "Goodbye.");
        Stage("sq_spirit_friend", "find");
        Talk("dlg_moppet", "moppet", "Ring Nell's bell", "Nell sent me", "(Continue.)");
        Stage("sq_spirit_friend", "return");
        Check(ctx.World.GetMap("whisperwood").IsChestAvailable(ctx.World.GetMap("whisperwood").FindChest("chest_moppet_stump")), "Moppet's stump chest appears");
        Talk("dlg_child_nell", "child_nell", "He was hiding", "Goodbye.");
        Done("sq_spirit_friend");

        // ---------------- wicks (peaceful)
        Talk("dlg_lamplighter_tobben", "lamplighter_tobben", "Stealing wicks?", "I'll get your wicks back.", "Goodbye.");
        Stage("sq_wicks", "gather");
        Talk("dlg_puddlecap", "", "Those wicks belong", "[PERSUASION]", "(Leave them to it.)");
        Check(ctx.Flags.IsSet("mossling_camp_done") && ctx.Log.All(l => !l.StartsWith("StartCombat enc_mossling_camp")), "mossling camp resolved peacefully");
        Stage("sq_wicks", "return");
        Talk("dlg_lamplighter_tobben", "lamplighter_tobben", "The Mosslings gave them back", "Goodbye.");
        Done("sq_wicks");

        // ---------------- satchel
        Talk("dlg_postman_fennick", "postman_fennick", "You look shaken.", "I'll get it back.", "Goodbye.");
        Stage("sq_satchel", "find");
        ctx.GiveItem("fennicks_satchel", 1);
        Stage("sq_satchel", "return");
        Talk("dlg_postman_fennick", "postman_fennick", "Here you go.", "I'll take it to her.", "Goodbye.");
        Stage("sq_satchel", "deliver"); Check(ctx.CountItem("ishiro_letter") == 1, "has Ishiro's letter");
        Talk("dlg_elder_maru", "elder_maru", "I have a letter", "I'll tell him.", "Goodbye.");
        Done("sq_satchel");

        // ---------------- bridge (peaceful via lookouts + paladin)
        Talk("dlg_guard_holt", "guard_holt", "Any trouble around?", "I'll deal with them.", "Goodbye.");
        Stage("sq_bridge", "bandits");
        Talk("dlg_bandit_lookouts", "", "Fine. Let's go and see Rusk.", "(Continue.)");
        Talk("dlg_rusk", "", "[PALADIN]", "Lanternvale needs hands", "(Continue.)");
        Check(ctx.Flags.IsSet("bandits_peaceful"), "bandits peaceful");
        Stage("sq_bridge", "report");
        Talk("dlg_guard_holt", "guard_holt", "They were farmers", "Goodbye.");
        Done("sq_bridge");
        Check(ctx.World.GetMap("lanternvale").IsNpcVisible(ctx.World.GetMap("lanternvale").Def.npcs.First(n => n.npc == "rusk")), "reformed Rusk appears in the village");
        Talk("dlg_rusk_reformed", "rusk", "toll refund", "Goodbye.");
        Check(ctx.CountItem("farmers_lucky_coin") == 1, "got Farmer's Lucky Coin");

        // ---------------- companions
        foreach (var (c, picks) in new (string, string[])[] {
            ("kael", new[] { "I'm going up to the Old Shrine", "Pleasant's overrated" }),
            ("aldric", new[] { "I'm going up to the Old Shrine", "Welcome aboard" }),
            ("pip", new[] { "There may be treasure", "Hollowed spirits", "You're hired." }),
            ("rook", new[] { "The Hollow's spreading", "Welcome aboard" }),
            ("seren", new[] { "Why won't it light?", "I'd be honoured" }),
            ("lys", new[] { "I'm going to the Old Shrine", "Glad to have you" }),
            ("torvan", new[] { "We're here to free the Warden", "Climb with us" }),
            ("morwen", new[] { "What are you doing here?", "Your help is welcome." }) })
        {
            Talk("dlg_recruit_" + c, c, picks);
            Check(ctx.Log.Contains("Recruit " + c) && ctx.Flags.IsSet("recruited_" + c), $"recruited {c}");
        }
        ctx.Members.Add(new PartyMemberInfo("seren", "Seren", ClassId.Priest, 5, false));
        Talk("dlg_recruit_seren", "seren", "Wait here for now");
        Check(ctx.Log.Contains("Dismiss seren") && !ctx.Flags.IsSet("recruited_seren"), "dismissed seren clears recruited_seren");

        Console.WriteLine($"\nXP from dialogue/quests: {ctx.Xp}, gold {ctx.GoldValue}");
        Console.WriteLine(fails == 0 ? "ALL OK" : $"{fails} FAILURES");
        return fails == 0 ? 0 : 1;
    }
}
