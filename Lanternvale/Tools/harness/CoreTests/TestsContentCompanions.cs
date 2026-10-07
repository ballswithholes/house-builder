// The four Ember Road companions (Docs/Expansion.md §8): Bruna and Ysolde tank, Liora and Nanami heal. Their data
// (bio, tastes, talents, signature gear in items_companions2.json), their recruit / in-party dialogues played through the
// real GameSession (recruit, party of PartySize, approval, the approval-gated story, dismiss and come back), dialogue
// termination, gear legality at the level they are met, and their roles while levelling.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.World;
using static Lanternvale.Tests.Harness;

namespace Lanternvale.Tests
{
    public static class TestsContentCompanions
    {
        sealed class Spec
        {
            public string Id, Prefix, Friend, Map;
            public ClassId Class;
            public UnitRole Role;
            public int MetLevel;      // the lowest level of the zone band they are met in
            public string KeyAbility; // the build's capstone talent ability (a 31-point tier-7 talent)
            public int KeyLevel;      // the level its build reaches it
            public string ClassChoice, FriendChoice, CheckChoice, Pitch, JoinWarm, JoinCold, Ask, AskBest, AskBad, Gift, Story, StoryBest;
        }

        static readonly Spec[] All =
        {
            new Spec
            {
                Id = "bruna", Prefix = "bru", Class = ClassId.Warrior, Role = UnitRole.Tank, Friend = "kael", Map = "amberfield", MetLevel = 12,
                KeyAbility = "warrior_shield_slam", KeyLevel = 44,
                ClassChoice = "right for a shield-wall", FriendChoice = "Kael, meet a fellow wall", CheckChoice = "watching that hill",
                Pitch = "Ember Road west", JoinWarm = "come and be my gate", JoinCold = "try not to slow me down",
                Ask = "chewing on something", AskBest = "send it home with a message", AskBad = "Make an example of it",
                Gift = "honey cake", Story = "night you held the gate", StoryBest = "came back the next night anyway",
            },
            new Spec
            {
                Id = "ysolde", Prefix = "yso", Class = ClassId.Paladin, Role = UnitRole.Tank, Friend = "aldric", Map = "brightwater", MetLevel = 12,
                KeyAbility = "paladin_holy_shield", KeyLevel = 44,
                ClassChoice = "knight of an old order", FriendChoice = "Aldric, you've gone very quiet", CheckChoice = "read the chronicles",
                Pitch = "bound north", JoinWarm = "honoured, Dame Ysolde", JoinCold = "leave your old ghosts",
                Ask = "quiet all morning", AskBest = "brings her people home", AskBad = "Orders are orders",
                Gift = "faded heron", Story = "really disbanded", StoryBest = "brave enough to be called a coward",
            },
            new Spec
            {
                Id = "liora", Prefix = "lio", Class = ClassId.Priest, Role = UnitRole.Healer, Friend = "seren", Map = "brightwater", MetLevel = 12,
                KeyAbility = "priest_power_infusion", KeyLevel = 40,
                ClassChoice = "burn like prayers", FriendChoice = "Seren keeps lanterns too", CheckChoice = "boatman's blessing",
                Pitch = "drowning lights", JoinWarm = "welcome a light on the road", JoinCold = "won't slow down for prayers",
                Ask = "want to ask something", AskBest = "Remembering is a kind of love", AskBad = "waste of good lamp oil",
                Gift = "pot of tea", Story = "basket you came down the river in", StoryBest = "Nobody stitches a song",
            },
            new Spec
            {
                Id = "nanami", Prefix = "nan", Class = ClassId.Shaman, Role = UnitRole.Healer, Friend = "torvan", Map = "mirefen", MetLevel = 18,
                KeyAbility = "shaman_mana_tide_totem", KeyLevel = 44,
                ClassChoice = "old tide-magic", FriendChoice = "long way from your snows", CheckChoice = "taste the water",
                Pitch = "sing them home with me", JoinWarm = "road could use a song", JoinCold = "don't slow us down, kid",
                Ask = "stopped humming", AskBest = "let them go", AskBad = "drain the whole fen",
                Gift = "walking song for stairs", Story = "grandmother's sending", StoryBest = "loved her too much",
            },
        };

        /// <summary>Exit words the scripted playthroughs use to leave a conversation (TestsSessionFullPlaythrough.Converse).</summary>
        static readonly string[] LeaveWords = { "Goodbye", "(Continue.)", "Let's keep moving", "(Leave", "Rest", "Just passing by", "Never mind" };

        static bool Has(DialogueView v, string text) => SessionTest.ChoiceIndex(v, text) >= 0;

        static GameSession Game(int level, ClassId main = ClassId.Mage, ulong seed = 701, bool veteranGear = true) =>
            SessionTest.NewGame(main, level, seed, veteranGear: veteranGear);

        static void Talk(GameSession s, Spec c)
        {
            Assert(s.StartDialogue("dlg_recruit_" + c.Id, c.Id), $"{c.Id}: dialogue starts ({s.LastError})");
            SessionTest.SkipText(s);
        }

        /// <summary>First meeting straight to the pitch and the warm answer (+5): the companion joins.</summary>
        static void RecruitWarmly(GameSession s, Spec c)
        {
            Talk(s, c);
            Assert(s.Dialogue.Current.NodeId == c.Prefix + "2", $"{c.Id}: first meeting greets ({s.Dialogue.Current.NodeId})");
            SessionTest.Pick(s, c.Pitch);
            SessionTest.Pick(s, c.JoinWarm);
            SessionTest.Finish(s);
        }

        // ------------------------------------------------------------------ data

        [Test]
        public static void Data_FleshedOut_TastesAndPlacement()
        {
            foreach (var c in All)
            {
                Assert(Db.Companions.TryGetValue(c.Id, out var def), $"{c.Id} exists");
                Assert(def.classId == c.Class && def.recruitDialogue == "dlg_recruit_" + c.Id, $"{c.Id}: {c.Class}, dlg_recruit_{c.Id}");
                foreach (var (what, text) in new[] { ("bio", def.bio), ("personality", def.personality), ("inspiration", def.inspiration) })
                    Assert(text.Length >= 120 && !text.Contains("STUB"), $"{c.Id}: real {what} ({text.Length} chars)");
                Assert(def.inspiration.Contains("FFX") && def.inspiration.Contains("Original character"), $"{c.Id}: FFX-inspired, original");
                Assert(def.likes.Length == 4 && def.dislikes.Length == 4 && def.likes.Concat(def.dislikes).All(t => t.Length > 0), $"{c.Id}: 4 likes, 4 dislikes");
                Assert(def.likes.Intersect(def.dislikes).Count() == 0, $"{c.Id}: likes and dislikes differ");
                var b = def.statBonus;
                int sum = (int)(b.strength + b.agility + b.stamina + b.intellect + b.spirit);
                Assert(sum >= 5 && sum <= 6, $"{c.Id}: stat bonus like the valley companions (5-6, got {sum})");
                Assert(def.startingItems.Length >= 2 && def.startingItems.Length <= 3, $"{c.Id}: 2-3 signature items");
                Assert(def.preferredTalents.Length == Progression.TalentPointsTotal(Db.Config.maxLevel), $"{c.Id}: a full 51-point build");
                var placed = Db.Maps.Values.Where(m => m.npcs.Any(n => n.npc == c.Id)).ToList();
                Assert(placed.Count == 1 && placed[0].id == c.Map, $"{c.Id} is placed once, in {c.Map}");
                Assert(placed[0].npcs.First(n => n.npc == c.Id).hideFlag == WorldRules.RecruitedFlag(c.Id), $"{c.Id}: hidden once recruited");
            }
        }

        /// <summary>Budget cost per stat point (ItemGenerator.StatCost).</summary>
        static float StatCost(StatId s)
        {
            switch (s)
            {
                case StatId.AttackPower: case StatId.RangedAttackPower: return 0.5f;
                case StatId.SpellDamage: return 0.86f;
                case StatId.HealingPower: return 0.45f;
                case StatId.MeleeCrit: case StatId.SpellCrit: case StatId.RangedCrit: case StatId.MeleeHit: case StatId.SpellHit: case StatId.Dodge: case StatId.Parry: return 14f;
                case StatId.ManaRegen: case StatId.HealthRegen: return 2.5f;
                default: return 1f;
            }
        }

        [Test]
        public static void SignatureItems_FollowTheExpansionBudget()
        {
            var seen = new HashSet<string>();
            foreach (var c in All)
            {
                var def = Db.Companions[c.Id];
                foreach (var id in def.startingItems)
                {
                    var it = Db.Item(id);
                    string tag = $"{c.Id}: {id}";
                    Assert(it != null && seen.Add(id), $"{tag} exists and belongs to one companion");
                    Assert(it.itemLevel >= 14 && it.itemLevel <= 20, $"{tag}: item level 14-20 ({it.itemLevel})");
                    Assert(it.quality == Quality.Uncommon || it.quality == Quality.Rare, $"{tag}: Uncommon or Rare");
                    Assert(it.unique && it.equip != EquipType.None && string.IsNullOrEmpty(it.use) && it.equipEffects.Count == 0, $"{tag}: unique equipable, no use or effects");
                    Assert(it.requiredLevel == it.itemLevel - 5 && it.requiredLevel <= c.MetLevel, $"{tag}: required level ilvl−5 ({it.requiredLevel}) ≤ met level {c.MetLevel}");
                    Assert(it.classes == null || it.classes.Length == 0, $"{tag}: no class lock (any companion may borrow it)");
                    Assert(it.price > 0 && it.description.Length > 20, $"{tag}: price and flavour");

                    // stats: 0.55 × ilvl × Qa × slot (Qa Uncommon 1.1, Rare 1.6) within ±15%
                    float qa = it.quality == Quality.Rare ? 1.6f : 1.1f;
                    float target = ItemGenerator.StatBudget(it.itemLevel, it.quality, it.equip) / ItemGenerator.QualityMult(it.quality) * qa;
                    float costed = it.stats.Sum(st => st.value * StatCost(st.stat));
                    Assert(costed >= 0.85f * target && costed <= 1.15f * target, $"{tag}: stats {costed:0.0} within 15% of the budget {target:0.0}");
                    if (it.weaponType != WeaponType.None && it.weaponType != WeaponType.Shield && it.weaponType != WeaponType.HeldInOffhand)
                    {
                        float dps = (it.minDamage + it.maxDamage) * 0.5f / it.speed;
                        float want = ItemGenerator.WeaponDps(it.weaponType, it.itemLevel, it.quality) * (it.quality == Quality.Rare ? 0.79f : 0.72f);
                        Assert(Math.Abs(dps - want) <= 0.06f * want, $"{tag}: DPS {dps:0.00} ≈ {want:0.00}");
                    }
                    if (it.armorType != ArmorType.None)
                    {
                        float want = ItemGenerator.ArmorValue(it.armorType, it.equip, it.itemLevel, it.quality);
                        Assert(Math.Abs(it.armor - want) <= 0.06f * want, $"{tag}: armour {it.armor} ≈ {want}");
                    }
                    if (it.weaponType == WeaponType.Shield)
                        Assert(it.armor > 0 && it.block > 0, $"{tag}: a shield has armour and block");
                }
            }
        }

        // ------------------------------------------------------------------ gear on the companion

        [Test]
        public static void SignatureGear_EquipsLegally_WhereTheyAreMet()
        {
            foreach (var c in All)
            {
                // plain recruit at the met level: every signature item is worn, legally
                var s = Game(c.MetLevel, veteranGear: false);
                s.Recruit(c.Id);
                var u = s.FindMember(c.Id);
                foreach (var id in Db.Companions[c.Id].startingItems)
                    Assert(u.Equipment.Equipped.Any(kv => kv.Value.Def.id == id), $"{c.Id} L{c.MetLevel}: wears {id}");
                foreach (var kv in u.Equipment.Equipped)
                    Assert(EquipmentRules.CannotEquipReason(u, kv.Value.Def, kv.Key) == null, $"{c.Id}: legal {kv.Value.Name} in {kv.Key}");
                Assert(u.Equipment.MainHand != null, $"{c.Id}: armed");
                bool shield = u.Equipment.OffHand != null && u.Equipment.OffHand.Def.weaponType == WeaponType.Shield;
                Assert(shield == (c.Role == UnitRole.Tank), $"{c.Id}: tanks carry a shield, healers do not");

                // veteran gear on top never loses a signature item: worn (better than the level gear) or in the bags
                foreach (int level in new[] { c.MetLevel, 30 })
                {
                    var v = Game(level, seed: (ulong)(710 + level));
                    v.Recruit(c.Id);
                    var w = v.FindMember(c.Id);
                    foreach (var kv in w.Equipment.Equipped)
                        Assert(EquipmentRules.CannotEquipReason(w, kv.Value.Def, kv.Key) == null, $"{c.Id} L{level}: legal {kv.Value.Name}");
                    foreach (var id in Db.Companions[c.Id].startingItems)
                        Assert(w.Equipment.Equipped.Any(kv => kv.Value.Def.id == id) || v.Inventory.Has(id), $"{c.Id} L{level}: {id} worn or in the bags");
                }
            }
        }

        // ------------------------------------------------------------------ roles

        [Test]
        public static void Roles_AtFourteenTwentyThirty_AndLevellingToSixty()
        {
            foreach (int level in new[] { 12, 14, 20, 30 })
            {
                var s = Game(level, seed: (ulong)(720 + level));
                foreach (var c in All) s.Recruit(c.Id);
                foreach (var c in All)
                {
                    var u = s.FindMember(c.Id);
                    Assert(Progression.TalentPointsAvailable(u) == 0, $"{c.Id} L{level}: talents spent");
                    // a Paladin's or Shaman's tree decides its role from 5 points (level 14); below that they fight in melee
                    bool exempt = level < 14 && (c.Class == ClassId.Paladin || c.Class == ClassId.Shaman);
                    if (exempt) Assert(u.Role == UnitRole.MeleeDps, $"{c.Id} L{level}: class fallback role (got {u.Role})");
                    else Assert(u.Role == c.Role, $"{c.Id} L{level}: {c.Role} (got {u.Role})");
                }
                // a raid-ready core: two tanks and two healers among the four
                if (level >= 14)
                {
                    var roles = All.Select(c => s.FindMember(c.Id).Role).ToList();
                    Assert(roles.Count(r => r == UnitRole.Tank) == 2 && roles.Count(r => r == UnitRole.Healer) == 2, $"L{level}: 2 tanks + 2 healers");
                }
            }

            var g = Game(12, ClassId.Rogue, seed: 731);
            foreach (var c in All) g.Recruit(c.Id);
            int max = Math.Max(1, Db.Config.maxLevel);
            while (g.Main.Level < max)
            {
                g.GivePartyXp(Progression.XpToNextLevel(Db, g.Main.Level) - g.Main.Xp);
                int level = g.Main.Level;
                foreach (var c in All)
                {
                    var u = g.FindMember(c.Id);
                    Assert(u.Level == level && Progression.TalentPointsAvailable(u) == 0, $"{c.Id} L{level}: synced, points spent");
                    if (level >= 14) Assert(u.Role == c.Role, $"{c.Id} L{level}: {c.Role} (got {u.Role})");
                    Assert(u.Knows(c.KeyAbility) == (level >= c.KeyLevel), $"{c.Id} L{level}: {c.KeyAbility} from level {c.KeyLevel}");
                }
            }
        }

        // ------------------------------------------------------------------ recruiting through the dialogue

        [Test]
        public static void Recruit_ThroughDialogue_JoinsUpToPartySize_ThenCamp()
        {
            var s = Game(18, seed: 741);
            foreach (var c in All)
            {
                s.EnterMap(c.Map, "default");
                Assert(s.MapId == c.Map && s.VisibleNpcs().Any(n => n.npc == c.Id), $"{c.Id} waits in {c.Map}");
                // first meeting: a polite exit leaves them met but not recruited
                Talk(s, c);
                Assert(s.Dialogue.Current.NodeId == c.Prefix + "2" && s.Flags.IsSet(c.Id + "_met"), $"{c.Id}: met at the first greeting");
                SessionTest.Pick(s, "Never mind");
                Assert(!s.Dialogue.IsActive && s.CompanionStatusOf(c.Id) == CompanionStatus.NotRecruited, $"{c.Id}: 'Never mind' ends, not recruited");
                // second talk: the short way in
                Talk(s, c);
                Assert(s.Dialogue.Current.NodeId == c.Prefix + "_met", $"{c.Id}: remembers you ({s.Dialogue.Current.NodeId})");
                SessionTest.Pick(s, "Come with me");
                SessionTest.Finish(s);
                Assert(s.Flags.IsSet(WorldRules.RecruitedFlag(c.Id)), $"{c.Id}: recruited flag");
                Assert(s.CompanionStatusOf(c.Id) == CompanionStatus.Active, $"{c.Id}: joins the active party");
                Assert(s.GetApproval(c.Id) == 0, $"{c.Id}: the short way earns no approval");
                Assert(!s.VisibleNpcs().Any(n => n.npc == c.Id), $"{c.Id}: map npc hidden");
            }
            Assert(s.Party.Count == s.PartySize && s.PartySize == 5, $"party of {s.PartySize}: you and the four");

            // a fifth companion waits at camp; dismissing one makes room, and the dismissed one comes back by talking
            s.Recruit("kael");
            Assert(s.CompanionStatusOf("kael") == CompanionStatus.Camp, "kael waits at camp (party full)");
            var bruna = All[0];
            var unit = s.FindMember(bruna.Id);
            Talk(s, bruna);
            Assert(s.Dialogue.Current.NodeId == "bru_party", "in-party talk opens the party branch");
            SessionTest.Pick(s, "Wait here for now");
            SessionTest.Finish(s);
            Assert(s.CompanionStatusOf(bruna.Id) == CompanionStatus.Away && !s.Flags.IsSet(WorldRules.RecruitedFlag(bruna.Id)), "dismissed: away, flag cleared");
            s.EnterMap(bruna.Map, "default");
            Assert(s.VisibleNpcs().Any(n => n.npc == bruna.Id), "her npc is back at her farm gate");
            Assert(s.SetPartyMemberActive("kael", true) == null && s.Party.Count == s.PartySize, "kael takes the free place");
            Talk(s, bruna);
            Assert(s.Dialogue.Current.NodeId == "bru_met", "talking again: she remembers you");
            SessionTest.Pick(s, "Come with me");
            SessionTest.Finish(s);
            Assert(s.FindMember(bruna.Id) == unit && s.CompanionStatusOf(bruna.Id) == CompanionStatus.Camp, "the same Bruna returns, to camp (party full)");
        }

        // ------------------------------------------------------------------ approval and the personal story

        [Test]
        public static void Approval_UnlocksThePersonalStory_OnTheGuaranteedPath()
        {
            foreach (var c in All)
            {
                var s = Game(20, seed: 751);
                RecruitWarmly(s, c);
                Assert(s.CompanionStatusOf(c.Id) == CompanionStatus.Active && s.GetApproval(c.Id) == 5, $"{c.Id}: warm welcome +5 ({s.GetApproval(c.Id)})");

                Talk(s, c);
                Assert(s.Dialogue.Current.NodeId == c.Prefix + "_party", $"{c.Id}: in-party branch");
                Assert(!Has(s.Dialogue.Current, c.Story), $"{c.Id}: the story waits for approval 20");
                Assert(Has(s.Dialogue.Current, "How are you holding up") && Has(s.Dialogue.Current, "Tell me about"), $"{c.Id}: banter and lore");
                SessionTest.Pick(s, c.Ask);
                SessionTest.Pick(s, c.AskBest);
                Assert(s.GetApproval(c.Id) == 13 && s.Flags.IsSet(c.Id + "_asked"), $"{c.Id}: the right answer +8 ({s.GetApproval(c.Id)})");
                SessionTest.Pick(s, c.Gift);
                Assert(s.GetApproval(c.Id) == 20 && s.Flags.IsSet(c.Id + "_gifted"), $"{c.Id}: the kindness +7 ({s.GetApproval(c.Id)})");
                SessionTest.SkipText(s);
                var hub = s.Dialogue.Current;
                Assert(hub.NodeId == c.Prefix + "_party_hub", $"{c.Id}: back at the hub");
                Assert(!Has(hub, c.Ask) && !Has(hub, c.Gift), $"{c.Id}: question and kindness happen once");
                SessionTest.Pick(s, c.Story);
                Assert(s.Flags.IsSet(c.Id + "_story_told") && s.GetApproval(c.Id) == 23, $"{c.Id}: the story is told (+3)");
                SessionTest.Pick(s, c.StoryBest);
                Assert(s.GetApproval(c.Id) == 28, $"{c.Id}: the kindest answer +5 ({s.GetApproval(c.Id)})");
                SessionTest.SkipText(s);
                Assert(!Has(s.Dialogue.Current, c.Story), $"{c.Id}: told once");
                SessionTest.Finish(s);
                Assert(s.CompanionStatusOf(c.Id) == CompanionStatus.Active, $"{c.Id}: still with you");
            }
        }

        [Test]
        public static void Approval_ColdChoicesCost_ButNobodyLeaves()
        {
            foreach (var c in All)
            {
                var s = Game(20, seed: 761);
                Talk(s, c);
                SessionTest.Pick(s, c.Pitch);
                SessionTest.Pick(s, c.JoinCold);
                SessionTest.Finish(s);
                Assert(s.CompanionStatusOf(c.Id) == CompanionStatus.Active && s.GetApproval(c.Id) == -3, $"{c.Id}: a cold welcome still recruits, −3 ({s.GetApproval(c.Id)})");
                Talk(s, c);
                SessionTest.Pick(s, c.Ask);
                SessionTest.Pick(s, c.AskBad);
                Assert(s.GetApproval(c.Id) == -8 && s.Flags.IsSet(c.Id + "_asked"), $"{c.Id}: the cruel answer −5 ({s.GetApproval(c.Id)})");
                SessionTest.Finish(s);
                Talk(s, c);
                Assert(!Has(s.Dialogue.Current, c.Ask) && !Has(s.Dialogue.Current, c.Story), $"{c.Id}: asked once; no story at {s.GetApproval(c.Id)}");
                Assert(Has(s.Dialogue.Current, c.Gift), $"{c.Id}: a kindness can still mend things");
                SessionTest.Finish(s, "Let's keep moving");
                Assert(s.CompanionStatusOf(c.Id) == CompanionStatus.Active, $"{c.Id}: disapproval does not make her leave");
            }
        }

        [Test]
        public static void FirstMeeting_ClassOption_SkillCheck_AndCompanionCameo()
        {
            foreach (var c in All)
            {
                // the class option: only for the matching class, +5
                var other = ClassId.Mage;
                var s = Game(20, other, seed: 771);
                Talk(s, c);
                Assert(!Has(s.Dialogue.Current, c.ClassChoice), $"{c.Id}: no [{c.Class.ToString().ToUpperInvariant()}] option for a {other}");
                Assert(!Has(s.Dialogue.Current, c.FriendChoice), $"{c.Id}: no cameo without {c.Friend}");
                s.EndDialogue();
                var m = Game(20, c.Class, seed: 772);
                Talk(m, c);
                var opt = m.Dialogue.Current.Choices.FirstOrDefault(x => x.Text.Contains(c.ClassChoice));
                Assert(opt != null && opt.Tag == c.Class.ToString().ToUpperInvariant(), $"{c.Id}: [{c.Class}] option for a {c.Class}");
                m.ChooseDialogue(opt.Index);
                Assert(m.GetApproval(c.Id) == 5, $"{c.Id}: class option +5 ({m.GetApproval(c.Id)})");
                SessionTest.SkipText(m);
                Assert(m.Dialogue.Current.NodeId == c.Prefix + "_pitch", $"{c.Id}: then the pitch");
                SessionTest.Pick(m, c.JoinWarm);
                SessionTest.Finish(m);
                Assert(m.CompanionStatusOf(c.Id) == CompanionStatus.Active && m.GetApproval(c.Id) == 10, $"{c.Id}: joins at 10");

                // the cameo: the friend in the party speaks up, both approve (+3 each)
                var f = Game(20, seed: 773);
                f.Recruit(c.Friend);
                Talk(f, c);
                SessionTest.Pick(f, c.FriendChoice);
                Assert(f.GetApproval(c.Id) == 3 && f.GetApproval(c.Friend) == 3, $"{c.Id}: cameo with {c.Friend} (+3 each: {f.GetApproval(c.Id)}, {f.GetApproval(c.Friend)})");
                SessionTest.SkipText(f);
                Assert(f.Dialogue.Current.NodeId == c.Prefix + "_pitch", $"{c.Id}: cameo leads to the pitch");
                f.EndDialogue();

                // the skill check: success or failure, both reach the pitch; offered once
                for (ulong seed = 781; seed <= 784; seed++)
                {
                    var k = Game(20, seed: seed);
                    Talk(k, c);
                    var chk = k.Dialogue.Current.Choices.FirstOrDefault(x => x.Text.Contains(c.CheckChoice));
                    Assert(chk != null && chk.Once && chk.Check != null, $"{c.Id}: a once-only skill check");
                    k.ChooseDialogue(chk.Index);
                    string node = k.Dialogue.Current.NodeId;
                    Assert(node == c.Prefix + "_chk_s" || node == c.Prefix + "_chk_f", $"{c.Id}: check resolves ({node})");
                    Assert(k.GetApproval(c.Id) == (node.EndsWith("_s") ? 5 : 0), $"{c.Id}: success +5, failure nothing");
                    SessionTest.SkipText(k);
                    Assert(k.Dialogue.Current.NodeId == c.Prefix + "_pitch", $"{c.Id}: the check leads to the pitch");
                    SessionTest.Pick(k, "Never mind");
                    Talk(k, c);
                    Assert(k.Dialogue.Current.NodeId == c.Prefix + "_met", $"{c.Id}: met after leaving");
                    k.EndDialogue();
                }
            }
        }

        // ------------------------------------------------------------------ structure and termination

        static IEnumerable<string> Targets(DialogueNodeDef n)
        {
            if (!string.IsNullOrEmpty(n.next)) yield return n.next;
            if (!string.IsNullOrEmpty(n.fallback)) yield return n.fallback;
            foreach (var ch in n.choices)
            {
                if (!string.IsNullOrEmpty(ch.next)) yield return ch.next;
                if (ch.check != null) { yield return ch.check.success; yield return ch.check.failure; }
            }
        }

        static bool IsExit(ChoiceDef ch) => string.IsNullOrEmpty(ch.next) && ch.check == null;

        [Test]
        public static void Dialogues_ShapeReachabilityAndExits()
        {
            var leave = new Regex("goodbye|rest|\\(leave|never mind|just passing by", RegexOptions.IgnoreCase);
            foreach (var c in All)
            {
                var d = Db.Dialogues["dlg_recruit_" + c.Id];
                var byId = d.nodes.ToDictionary(n => n.id);
                Assert(d.nodes.Count >= 18 && d.nodes.Count <= 22, $"{d.id}: 18-22 nodes ({d.nodes.Count})");
                Assert(d.start == c.Prefix + "_party", $"{d.id}: starts at the in-party check");
                foreach (var stub in new[] { "_party", "_dismissed", "_hello", "_join" })
                    Assert(byId.ContainsKey(c.Prefix + stub), $"{d.id}: keeps the contract node {c.Prefix}{stub}");
                Assert(byId[c.Prefix + "_join"].outcomes.Any(o => o.type == OutcomeType.Recruit && o.key == c.Id), $"{d.id}: join recruits");
                Assert(d.nodes.SelectMany(n => n.choices).Any(ch => ch.outcomes.Any(o => o.type == OutcomeType.Dismiss && o.key == c.Id)
                    && ch.outcomes.Any(o => o.type == OutcomeType.ClearFlag && o.key == WorldRules.RecruitedFlag(c.Id))), $"{d.id}: a dismiss choice");
                Assert(d.nodes.SelectMany(n => n.choices).Any(ch => ch.conditions.Any(k => k.type == ConditionType.Companion && k.key == c.Id && k.amount >= 20)),
                    $"{d.id}: an approval-gated story");
                Assert(d.nodes.SelectMany(n => n.choices).Any(ch => ch.conditions.Any(k => k.type == ConditionType.Class)), $"{d.id}: a class option");
                Assert(!d.nodes.SelectMany(n => n.outcomes.Concat(n.choices.SelectMany(ch => ch.outcomes)))
                    .Any(o => o.type == OutcomeType.StartQuest || o.type == OutcomeType.CompleteQuest || o.type == OutcomeType.StartCombat), $"{d.id}: no quest or combat side effects");

                // every node reachable from the start; from every node an end is reachable
                var seen = new HashSet<string> { d.start };
                var todo = new Queue<string>(seen);
                while (todo.Count > 0) foreach (var t in Targets(byId[todo.Dequeue()])) if (seen.Add(t)) todo.Enqueue(t);
                foreach (var n in d.nodes) Assert(seen.Contains(n.id), $"{d.id}.{n.id}: reachable");
                var ends = new HashSet<string>(d.nodes.Where(n => (n.choices.Count == 0 && string.IsNullOrEmpty(n.next)) || n.choices.Any(IsExit)).Select(n => n.id));
                for (bool grew = true; grew;)
                {
                    grew = false;
                    foreach (var n in d.nodes)
                        if (!ends.Contains(n.id) && ((n.choices.Count == 0 && !string.IsNullOrEmpty(n.next) && ends.Contains(n.next))
                            || n.choices.Any(ch => !string.IsNullOrEmpty(ch.next) && ends.Contains(ch.next))))
                            grew = ends.Add(n.id) || grew;
                }
                foreach (var n in d.nodes) Assert(ends.Contains(n.id), $"{d.id}.{n.id}: can reach an end");

                // the scripted walkers' exit words are only ever real exits, and every choice node offers one
                foreach (var n in d.nodes)
                {
                    foreach (var ch in n.choices)
                        if (leave.IsMatch(ch.text)) Assert(IsExit(ch) && ch.outcomes.Count == 0, $"{d.id}.{n.id}: '{ch.text}' is a real exit");
                    if (n.choices.Count > 0)
                        Assert(n.choices.Any(ch => IsExit(ch) && ch.conditions.Count == 0 && LeaveWords.Any(w => ch.text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)),
                            $"{d.id}.{n.id}: offers an unconditional exit a walker recognises");
                    if (n.speaker != "narrator") Assert(n.speaker == c.Id || Db.Companions.ContainsKey(n.speaker), $"{d.id}.{n.id}: speaker {n.speaker}");
                }
            }
        }

        [Test]
        public static void Dialogues_TerminateUnderRandomPlay_InEveryState()
        {
            foreach (var c in All)
                for (ulong seed = 1; seed <= 6; seed++)
                {
                    var s = Game(20, seed % 2 == 0 ? c.Class : ClassId.Mage, seed: 790 + seed);
                    if (seed >= 3) s.Recruit(c.Friend);
                    if (seed >= 5) { s.Recruit(c.Id); s.ChangeApproval(c.Id, 30); }
                    var pick = new Lanternvale.Util.Rng(seed * 7919);
                    for (int talk = 0; talk < 3; talk++)
                    {
                        Talk(s, c);
                        int steps = 0;
                        while (s.Dialogue.IsActive && steps++ < 60)
                        {
                            var v = s.Dialogue.Current;
                            if (v.CanContinue) s.ContinueDialogue();
                            else s.ChooseDialogue(pick.Range(0, v.Choices.Count - 1));
                        }
                        if (s.Dialogue.IsActive)
                        {
                            // hub menus may loop under random play; the walker's exit always closes them
                            SessionTest.Finish(s, "Let's keep moving");
                        }
                        Assert(!s.Dialogue.IsActive, $"{c.Id} seed {seed}: the dialogue ends");
                    }
                }
        }
    }
}
