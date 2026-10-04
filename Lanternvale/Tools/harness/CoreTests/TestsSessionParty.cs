// GameSession: companions (recruit, camp, dismiss, re-recruit), level sync, approval, leader, pets.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Tests
{
    public static class TestsSessionParty
    {
        [Test]
        public static void Recruit_AllCompanions_PartyAndCamp()
        {
            var db = Harness.Db;
            var s = SessionTest.NewGame(ClassId.Paladin, 1, seed: 11);
            var ids = new List<string>(db.Companions.Keys);
            Harness.Assert(ids.Count == 8, "8 companions in the data");
            foreach (var id in ids)
            {
                s.Recruit(id);
                var u = s.FindMember(id);
                var def = db.Companions[id];
                Harness.Assert(u != null && u.Companion == def && u.ClassId == def.classId, $"{id}: unit created");
                Harness.Assert(u.Level == s.Main.Level, $"{id}: level synced");
                Harness.Assert(s.Flags.IsSet("recruited_" + id), $"{id}: recruited flag");
                Harness.Assert(s.MemberId(u) == id, $"{id}: member id");
                foreach (var a in u.Class.startingAbilities) Harness.Assert(db.Ability(a) == null || u.Knows(a), $"{id}: knows {a}");
                foreach (var kv in u.Equipment.Equipped)
                    Harness.Assert(EquipmentRules.CannotEquipReason(u, kv.Value.Def, kv.Key) == null, $"{id}: legal {kv.Value.Name}");
                Harness.Assert(u.Equipment.Count >= 1, $"{id}: signature gear equipped");
                Harness.Assert(Math.Abs(u.Health - u.MaxHealth) < 0.01f, $"{id}: full health");
                Harness.Assert(u.AutoPlay == s.Settings.CompanionAutoPlay, $"{id}: auto-play default");
            }
            int size = db.Config.partySize;
            Harness.Assert(s.Party.Count == size, $"active party capped at {size}");
            Harness.Assert(s.Roster.Count == 9, "roster: main + 8");
            Harness.Assert(s.Camp().Count == 8 - (size - 1), "the rest wait at camp");
            foreach (var u in s.Camp()) Harness.Assert(s.CompanionStatusOf(u.Companion.id) == CompanionStatus.Camp, "camp status");
            // recruited companions' map NPCs are hidden
            foreach (var n in s.VisibleNpcs()) Harness.Assert(!db.Companions.ContainsKey(n.npc), $"companion npc {n.npc} hidden after recruit");

            // swap camp <-> party
            var active = s.Party[1];
            var waiting = s.Camp()[0];
            Harness.Assert(s.SetPartyMemberActive(waiting.Companion.id, true) != null, "cannot add to a full party");
            Harness.Assert(s.SetPartyMemberActive(active.Companion.id, false) == null, "send to camp");
            Harness.Assert(s.SetPartyMemberActive(waiting.Companion.id, true) == null, "join from camp");
            Harness.Assert(s.IsInParty(waiting) && !s.IsInParty(active), "swapped");
            Harness.Assert(s.Field.Units.Contains(waiting) && !s.Field.Units.Contains(active), "field context follows the party");

            // dismiss → away (npc visible again); recruit again → same unit
            var kael = s.FindMember("kael");
            kael.Xp = 0;
            s.Dismiss("kael");
            Harness.Assert(s.CompanionStatusOf("kael") == CompanionStatus.Away, "dismissed → away");
            Harness.Assert(!s.Flags.IsSet("recruited_kael"), "recruited flag cleared");
            bool npcBack = false;
            foreach (var n in s.VisibleNpcs()) if (n.npc == "kael") npcBack = true;
            Harness.Assert(npcBack, "kael's map npc reappears");
            if (s.Party.Count >= size) s.SetPartyMemberActive(s.Party[s.Party.Count - 1].Companion.id, false);
            s.Recruit("kael");
            Harness.Assert(s.FindMember("kael") == kael && s.IsInParty(kael), "same unit comes back");

            // leader
            Harness.Assert(s.SetLeader(kael) == null && s.Leader == kael, "kael leads");
            s.Dismiss("kael");
            Harness.Assert(s.Leader == s.Main, "leader falls back to the main character");
        }

        [Test]
        public static void Recruit_Veteran_Level20_TalentsAndGear()
        {
            var s = SessionTest.NewGame(ClassId.Rogue, 20, seed: 21);
            s.Recruit("seren");
            var u = s.FindMember("seren");
            Harness.Assert(u.Level == 20, "companion at party level");
            int spent = Progression.TalentPointsSpent(u);
            Harness.Assert(spent == Progression.TalentPointsTotal(20), $"talents auto-allocated ({spent})");
            foreach (var a in Harness.Db.Abilities.Values)
            {
                if (a.classId != ClassId.Priest || a.hidden || a.fromTalent) continue;
                int r = AbilityRules.MaxRankAtLevel(a, 20);
                if (r > 0) Harness.Assert(u.RankOf(a.id) >= r, $"seren knows {a.id} rank {r}");
            }
            Harness.Assert(u.Equipment[EquipSlot.Chest] != null && u.Equipment[EquipSlot.Chest].Def.itemLevel >= 14, "level-appropriate chest");
            foreach (var kv in u.Equipment.Equipped)
                Harness.Assert(EquipmentRules.CannotEquipReason(u, kv.Value.Def, kv.Key) == null, $"legal {kv.Value.Name}");
        }

        [Test]
        public static void LevelSync_XpLevelsTheWholeRoster()
        {
            var s = SessionTest.NewGame(ClassId.Mage, 1, seed: 31);
            s.Recruit("kael");
            s.Recruit("pip");
            s.TakeEvents();
            int need = 0;
            for (int l = 1; l < 11; l++) need += Progression.XpToNextLevel(Harness.Db, l);
            var ups = s.GivePartyXp(need + 10);
            Harness.Assert(s.Main.Level == 11, $"main reached 11 (got {s.Main.Level})");
            foreach (var u in s.Roster) Harness.Assert(u.Level == 11, $"{u.Name} synced to 11");
            var ev = s.TakeEvents();
            Harness.Assert(SessionTest.CountEvents(ev, SessionEventKind.LevelUp) == 30, $"one LevelUp per level and member ({SessionTest.CountEvents(ev, SessionEventKind.LevelUp)})");
            Harness.Assert(SessionTest.FindEvent(ev, SessionEventKind.TalentPointsAvailable) != null, "talent points toast for the main character");
            Harness.Assert(Progression.TalentPointsAvailable(s.Main) == 2, "main keeps its points for the player");
            var kael = s.FindMember("kael");
            Harness.Assert(Progression.TalentPointsAvailable(kael) == 0, "companion talents auto-allocated");
            Harness.Assert(SessionTest.FindEvent(ev, SessionEventKind.AbilityLearned) != null, "companions auto-trained new ranks");
            var e = SessionTest.FindEvent(ev, SessionEventKind.LevelUp);
            Harness.Assert(e.LevelUp != null && e.Unit != null && e.Text.Length > 0, "LevelUp event payload");

            // dialogue/quest XP applies xpRate
            int xp = s.Main.Xp;
            s.GiveXP(10);
            Harness.Assert(s.Main.Xp == xp + Progression.QuestXp(Harness.Db, 10), "quest xp scaled by xpRate");
        }

        [Test]
        public static void Approval_And_PartyInfo()
        {
            var s = SessionTest.NewGame(ClassId.Warlock, 1, seed: 41);
            s.Recruit("seren");
            s.ChangeApproval("seren", 5);
            s.ChangeApproval("seren", -2);
            Harness.Assert(s.GetApproval("seren") == 3, "approval sums");
            var ev = s.TakeEvents();
            var a = SessionTest.FindEvent(ev, SessionEventKind.ApprovalChanged, "seren");
            Harness.Assert(a != null && a.Amount == 5 && a.Text.Contains("approves"), "approval toast");
            IDialogueContext ctx = s;
            Harness.Assert(ctx.Party.Count == 2 && ctx.Party[0].isMain && ctx.Party[0].id == "player" && ctx.Party[1].id == "seren", "dialogue party snapshot");
            Harness.Assert(ctx.Party[1].stats.spirit > 20f, "effective stats in snapshot");
            Harness.Assert(WorldRules.Check(new ConditionDef { type = ConditionType.InParty, key = "seren" }, ctx), "InParty condition");
            Harness.Assert(WorldRules.Check(new ConditionDef { type = ConditionType.Companion, key = "seren", amount = 3 }, ctx), "Companion approval condition");
        }

        [Test]
        public static void Pets_SummonPersistAcrossBattles()
        {
            var s = SessionTest.NewGame(ClassId.Warlock, 10, seed: 51);
            var w = s.Main;
            var r = s.UseAbility(w, "warlock_summon_imp");
            Harness.Assert(r.Ok, "summon imp out of combat: " + r.Reason);
            Harness.Assert(w.Pet != null && w.Pet.IsPersistentPet && w.Pet.Owner == w, "imp is the warlock's persistent pet");
            Harness.Assert(s.PartyUnits().Contains(w.Pet), "pet listed in PartyUnits");
            var ev = s.TakeEvents();
            Harness.Assert(SessionTest.FindEvent(ev, SessionEventKind.PetChanged) != null, "PetChanged event");
            var imp = w.Pet;
            // follows the party
            var plan = s.PlanPartyMove(w.Position + new Vec2(6, 0));
            Harness.Assert(plan.Followers.Count == 1 && plan.Followers[0].Unit == imp, "pet follows in formation");
            // joins a battle and survives it
            var b = s.StartEncounter("enc_training_dummy");
            Harness.Assert(b != null && b.Units.Contains(imp), "pet joins the battle");
            Harness.Assert(b.TurnOrder.IndexOf(imp) == b.TurnOrder.IndexOf(w) + 1, "pet acts right after its owner");
            Harness.Assert(s.LeaveCombat() != null, "leave the dummy");
            Harness.Assert(w.Pet == imp && s.Field.Units.Contains(imp), "pet persists after combat");
            // map change keeps it
            s.EnterMap("whisperwood", "from_village");
            Harness.Assert(w.Pet == imp && s.PartyUnits().Contains(imp), "pet persists across maps");
        }
    }
}
