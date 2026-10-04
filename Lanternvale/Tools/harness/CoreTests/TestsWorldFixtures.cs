// In-memory fixtures for the world module tests (no dependency on content data files).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    /// <summary>Minimal IDialogueContext recording every action.</summary>
    public sealed class FakeWorldContext : IDialogueContext
    {
        public readonly WorldState World;
        public readonly List<PartyMemberInfo> Members = new List<PartyMemberInfo>();
        public readonly Dictionary<string, int> Items = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Approval = new Dictionary<string, int>();
        public readonly List<string> Log = new List<string>();
        public int GoldValue;
        public int Xp;
        public string Time = "day";
        public int Bonus;

        public FakeWorldContext(GameDatabase db)
        {
            World = new WorldState(db, this);
            Members.Add(new PartyMemberInfo("player", "Tidus", ClassId.Paladin, 5, true,
                new PrimaryStats { strength = 45, agility = 22, stamina = 30, intellect = 18, spirit = 25 }));
        }

        public FlagStore Flags => World.Flags;
        public QuestLog Quests => World.Quests;
        public string PlayerName => Members.Count > 0 ? Members[0].name : "";
        public IReadOnlyList<PartyMemberInfo> Party => Members;
        public int Gold => GoldValue;
        public int CountItem(string itemId) => Items.TryGetValue(itemId, out var n) ? n : 0;
        public int GetApproval(string companionId) => Approval.TryGetValue(companionId, out var n) ? n : 0;
        public string TimeOfDay => Time;
        public int SkillCheckBonus(string memberId, SkillCheck skill) => Bonus;

        public void GiveItem(string itemId, int count)
        {
            Items[itemId] = CountItem(itemId) + count;
            Log.Add($"GiveItem {itemId} {count}");
            Quests.OnItemCount(itemId, CountItem(itemId));
        }

        public void TakeItem(string itemId, int count)
        {
            Items[itemId] = Math.Max(0, CountItem(itemId) - count);
            Log.Add($"TakeItem {itemId} {count}");
            Quests.OnItemCount(itemId, CountItem(itemId));
        }

        public void GiveGold(int copper) { GoldValue += copper; Log.Add($"GiveGold {copper}"); }
        public void TakeGold(int copper) { GoldValue -= copper; Log.Add($"TakeGold {copper}"); }
        public void GiveXP(int amount) { Xp += amount; Log.Add($"GiveXP {amount}"); }
        public void Recruit(string companionId) => Log.Add($"Recruit {companionId}");
        public void Dismiss(string companionId) => Log.Add($"Dismiss {companionId}");
        public void ChangeApproval(string companionId, int delta) { Approval[companionId] = GetApproval(companionId) + delta; Log.Add($"Approval {companionId} {delta}"); }
        public void StartCombat(string encounterId) => Log.Add($"StartCombat {encounterId}");
        public void OpenVendor(string npcId) => Log.Add($"OpenVendor {npcId}");
        public void OpenTrainer(string npcId) => Log.Add($"OpenTrainer {npcId}");
        public void OpenRespec(string npcId) => Log.Add($"OpenRespec {npcId}");
        public void Rest() => Log.Add("Rest");
        public void HealParty() => Log.Add("HealParty");
        public void Teleport(string mapId, string spawnId) => Log.Add($"Teleport {mapId} {spawnId}");
        public void RunSpecial(string specialId, OutcomeDef outcome) => Log.Add($"Special {specialId} {outcome.value}");
    }

    public static class WorldFixtures
    {
        public static ConditionDef Cond(ConditionType t, string key = "", string value = "", int amount = 0) =>
            new ConditionDef { type = t, key = key, value = value, amount = amount };

        public static OutcomeDef Out(OutcomeType t, string key = "", string value = "", int amount = 0) =>
            new OutcomeDef { type = t, key = key, value = value, amount = amount };

        public static DialogueNodeDef Node(string id, string text, string next = "", string speaker = "")
            => new DialogueNodeDef { id = id, text = text, next = next, speaker = speaker };

        public static ChoiceDef Choice(string text, string next = "") => new ChoiceDef { text = text, next = next };

        public static ObjectiveDef Obj(ObjectiveType t, string target, int count = 1, string text = "") =>
            new ObjectiveDef { type = t, target = target, count = count, text = text };

        /// <summary>Database with a couple of npcs/companions/creatures/items and the wolf quest.</summary>
        public static GameDatabase Db()
        {
            var db = new GameDatabase();
            db.Npcs["elder_maren"] = new NpcDef { id = "elder_maren", name = "Elder Maren", portrait = "portrait_maren" };
            db.Npcs["hunter_brann"] = new NpcDef { id = "hunter_brann", name = "Brann" };
            db.Companions["lyra"] = new CompanionDef { id = "lyra", name = "Lyra", classId = ClassId.Mage, portrait = "portrait_lyra" };
            db.Creatures["wolf"] = new CreatureDef { id = "wolf", name = "Grey Wolf" };
            db.Items["wolf_pelt"] = new ItemDef { id = "wolf_pelt", name = "Wolf Pelt" };
            db.Items["lantern_oil"] = new ItemDef { id = "lantern_oil", name = "Lantern Oil" };
            db.Classes[ClassId.Paladin] = new ClassDef { id = ClassId.Paladin, name = "Paladin" };

            var q = new QuestDef
            {
                id = "q_wolves", name = "Wolves at the Door", giver = "hunter_brann", level = 3,
                summary = "Brann wants the wolves thinned out.",
                rewards = new QuestRewardDef { xp = 450, gold = 250, items = new[] { "lantern_oil" }, choiceItems = new[] { "wolf_pelt", "lantern_oil" } },
            };
            q.stages.Add(new QuestStageDef
            {
                id = "hunt", description = "Slay the wolves in Whisperwood.", next = "pelts",
                objectives = { Obj(ObjectiveType.Kill, "wolf", 3, "Wolves slain") },
                onComplete = { Out(OutcomeType.SetFlag, "wolves_thinned") },
            });
            q.stages.Add(new QuestStageDef
            {
                id = "pelts", description = "Bring Brann two wolf pelts.", next = "return",
                objectives = { Obj(ObjectiveType.Collect, "wolf_pelt", 2, "Wolf pelts") },
            });
            q.stages.Add(new QuestStageDef { id = "return", description = "Return to Brann.", objectives = { Obj(ObjectiveType.Talk, "hunter_brann") } });
            db.Quests[q.id] = q;

            var lanterns = new QuestDef { id = "q_lanterns", name = "Rekindle the Lanterns", main = true, giver = "elder_maren" };
            lanterns.stages.Add(new QuestStageDef
            {
                id = "light", description = "Light the three spirit lanterns.", next = "shrine",
                objectives = { Obj(ObjectiveType.Flag, "lanterns_lit", 3, "Lanterns lit") },
            });
            lanterns.stages.Add(new QuestStageDef
            {
                id = "shrine", description = "Enter the Old Lantern Shrine and defeat the Warden.",
                objectives = { Obj(ObjectiveType.Reach, "shrine_gate"), Obj(ObjectiveType.Defeat, "warden") },
            });
            db.Quests[lanterns.id] = lanterns;
            return db;
        }

        /// <summary>Small map: props, a chest, an encounter, npcs with flags, a locked transition and a region.</summary>
        public static MapDef Map()
        {
            var m = new MapDef { id = "whisperwood", name = "Whisperwood", width = 40, depth = 16 };
            m.props.Add(new PropDef { art = "tree_big", pos = new Vec2(10, 8), scale = 1.5f, collider = new ColliderDef { w = 2.4f, h = 1.6f } });
            m.props.Add(new PropDef { art = "lamp", pos = new Vec2(20, 4), collider = new ColliderDef { w = 0.3f, h = 0.2f } });
            m.props.Add(new PropDef { art = "grass", pos = new Vec2(5, 5) }); // no collider
            m.props.Add(new PropDef { art = "rock", pos = new Vec2(30, 10), flip = true, collider = new ColliderDef { w = 1f, h = 0.6f, offset = new Vec2(1f, 0) } });
            m.chests.Add(new ChestDef { id = "chest_a", pos = new Vec2(25, 12), lockCheck = new CheckDef { skill = SkillCheck.SleightOfHand, dc = 12 } });
            m.chests.Add(new ChestDef { id = "chest_b", pos = new Vec2(35, 3), requireFlag = "found_cache" });
            m.encounters.Add(new EncounterDef { id = "wolves1", pos = new Vec2(30, 6), radius = 6 });
            m.encounters.Add(new EncounterDef { id = "warden", pos = new Vec2(36, 12), radius = 4, requireFlag = "shrine_open", doneFlag = "warden_dead", hidden = true });
            m.npcs.Add(new MapNpcDef { npc = "hunter_brann", pos = new Vec2(4, 8) });
            m.npcs.Add(new MapNpcDef { npc = "elder_maren", pos = new Vec2(6, 8), requireFlag = "met_elder", hideFlag = "elder_left" });
            m.npcs.Add(new MapNpcDef { npc = "lyra", pos = new Vec2(8, 8), hideFlag = "recruited_lyra" });
            m.transitions.Add(new TransitionDef { id = "to_shrine", pos = new Vec2(39, 8), size = new Vec2(2, 4), targetMap = "shrine", targetSpawn = "default", requireFlag = "shrine_open", lockedText = "Sealed." });
            m.transitions.Add(new TransitionDef { id = "to_village", pos = new Vec2(1, 8), targetMap = "lanternvale" });
            m.regions.Add(new RegionDef { id = "shrine_gate", pos = new Vec2(36, 8), size = new Vec2(6, 6), enterFlag = "saw_shrine", text = "The Old Lantern Shrine" });
            m.spawns.Add(new SpawnPointDef { id = "default", pos = new Vec2(3, 8) });
            m.spawns.Add(new SpawnPointDef { id = "east", pos = new Vec2(37, 8) });
            return m;
        }
    }
}
