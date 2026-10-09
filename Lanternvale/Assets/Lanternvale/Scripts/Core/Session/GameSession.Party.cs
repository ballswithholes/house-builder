// Party, roster ("camp"), companions, leader, formation, pets, XP & level sync, approval.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Session
{
    public sealed partial class GameSession
    {
        readonly List<Unit> roster = new List<Unit>();
        readonly List<Unit> party = new List<Unit>();
        Unit leader;
        readonly Dictionary<string, int> approval = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<PartyMemberInfo> partyInfo = new List<PartyMemberInfo>();

        /// <summary>Formation slots behind the leader: (metres behind, metres to the side). 14 slots: a raid of 10 with
        /// its pets follows without falling back to the stretched overflow rule of <see cref="FormationSlots"/>.</summary>
        public static readonly Vec2[] FormationOffsets =
        {
            new Vec2(1.3f, 0.9f), new Vec2(1.3f, -0.9f), new Vec2(2.5f, 0f), new Vec2(2.5f, 1.7f), new Vec2(2.5f, -1.7f),
            new Vec2(3.7f, 0.85f), new Vec2(3.7f, -0.85f), new Vec2(4.9f, 0f), new Vec2(4.9f, 1.7f), new Vec2(4.9f, -1.7f),
            new Vec2(6.1f, 0.85f), new Vec2(6.1f, -0.85f), new Vec2(7.3f, 0f), new Vec2(7.3f, 1.7f),
        };

        // ================================================================= queries

        /// <summary>The main character (member id "player").</summary>
        public Unit Main => roster.Count > 0 ? roster[0] : null;
        /// <summary>Active characters; Party[0] is the main character.</summary>
        public IReadOnlyList<Unit> Party => party;
        /// <summary>Main character + every companion ever recruited (active, camp or away).</summary>
        public IReadOnlyList<Unit> Roster => roster;
        /// <summary>The unit the player moves / the camera follows.</summary>
        public Unit Leader => leader != null && party.Contains(leader) ? leader : Main;
        /// <summary>Companions are level-synced with the main character.</summary>
        public int PartyLevel => Main != null ? Main.Level : 1;
        /// <summary>The most active characters: config.partySize, or the raid's size while in a raid (GameSession.Raid.cs).</summary>
        public int PartySize => InRaid ? RaidSize : Math.Max(1, Db.Config.partySize);

        public string MemberId(Unit u)
        {
            if (u == null) return "";
            if (u == Main) return MainId;
            if (u.Companion != null && u.Class != null) return u.Companion.id;
            return "";
        }

        /// <summary>Roster member by id ("player" or companion id), or null.</summary>
        public Unit FindMember(string memberId)
        {
            if (string.IsNullOrEmpty(memberId)) return null;
            if (memberId == MainId) return Main;
            foreach (var u in roster) if (u.Companion != null && u.Companion.id == memberId) return u;
            return null;
        }

        public CompanionStatus CompanionStatusOf(string companionId)
        {
            var u = FindMember(companionId);
            if (u == null || u == Main) return CompanionStatus.NotRecruited;
            if (party.Contains(u)) return CompanionStatus.Active;
            return World.Flags.IsSet(WorldRules.RecruitedFlag(companionId)) ? CompanionStatus.Camp : CompanionStatus.Away;
        }

        /// <summary>Recruited companions waiting at camp (not in the active party).</summary>
        public List<Unit> Camp()
        {
            var list = new List<Unit>();
            foreach (var u in roster)
                if (u != Main && !party.Contains(u) && CompanionStatusOf(u.Companion.id) == CompanionStatus.Camp) list.Add(u);
            return list;
        }

        /// <summary>Active characters followed by their persistent living pets (spawn a view for each).</summary>
        public List<Unit> PartyUnits()
        {
            var list = new List<Unit>(party);
            foreach (var m in party)
                if (m.Pet != null && !m.Pet.Dead && m.Pet.IsPersistentPet && !list.Contains(m.Pet)) list.Add(m.Pet);
            return list;
        }

        /// <summary>Active characters only (no pets).</summary>
        public bool IsInParty(Unit u) => u != null && party.Contains(u);

        public string SetLeader(Unit u)
        {
            if (u == null || !party.Contains(u)) return "Only an active party member can lead.";
            if (Battle != null) return "Cannot change the leader during combat.";
            if (leader == u) return null;
            leader = u;
            Raise(new SessionEvent { Kind = SessionEventKind.LeaderChanged, Unit = u, Text = $"{u.Name} leads the way." });
            return null;
        }

        /// <summary>Moves a recruited companion between camp and the active party (out of combat).</summary>
        public string SetPartyMemberActive(string companionId, bool active)
        {
            var u = FindMember(companionId);
            if (u == null || u == Main) return "Not a recruited companion.";
            if (Battle != null) return "Cannot change the party during combat.";
            if (Dialogue.IsActive) return "Not during a conversation.";
            var st = CompanionStatusOf(companionId);
            if (active)
            {
                if (st == CompanionStatus.Active) return null;
                if (st == CompanionStatus.Away) return $"{u.Name} is not with you. Find them and ask them to come along.";
                if (party.Count >= PartySize) return $"The party is full ({PartySize}).";
                party.Add(u);
                PlaceNearLeader(u);
            }
            else
            {
                if (st != CompanionStatus.Active) return null;
                party.Remove(u);
                if (leader == u) leader = Main;
            }
            RebuildField();
            Raise(new SessionEvent { Kind = SessionEventKind.PartyChanged, Unit = u, Id = companionId, Text = active ? $"{u.Name} joins the party." : $"{u.Name} returns to camp." });
            return null;
        }

        public void SetAutoPlay(Unit u, bool on)
        {
            if (u == null) return;
            u.AutoPlay = on;
            if (u.Pet != null) u.Pet.AutoPlay = on;
        }

        // ================================================================= companions

        /// <summary>Recruits a companion (dialogue outcome): creates it at the party level, or brings back the existing unit.</summary>
        public void Recruit(string companionId)
        {
            if (resetting || string.IsNullOrEmpty(companionId) || !hasGame) return;
            if (!Db.Companions.TryGetValue(companionId, out var def))
            {
                Log.Warn($"GameSession: unknown companion '{companionId}'");
                return;
            }
            World.Flags.Set(WorldRules.RecruitedFlag(companionId), 1);
            var u = FindMember(companionId);
            if (u == null)
            {
                u = CreateCompanionUnit(def);
                roster.Add(u);
                NoteRecruitedInRaid(u);
            }
            bool joined = party.Contains(u);
            if (!joined && party.Count < PartySize && Battle == null)
            {
                party.Add(u);
                PlaceNearLeader(u);
                joined = true;
            }
            RebuildField();
            Raise(new SessionEvent
            {
                Kind = SessionEventKind.CompanionRecruited, Id = companionId, Unit = u,
                Text = joined ? $"{u.Name} joins the party." : $"{u.Name} waits at camp (the party is full).",
            });
            Raise(new SessionEvent { Kind = SessionEventKind.PartyChanged, Id = companionId, Unit = u });
        }

        /// <summary>Dismisses a companion (dialogue outcome): leaves the party, clears recruited_&lt;id&gt; (its map NPC reappears).</summary>
        public void Dismiss(string companionId)
        {
            if (resetting || string.IsNullOrEmpty(companionId)) return;
            var u = FindMember(companionId);
            World.Flags.Clear(WorldRules.RecruitedFlag(companionId));
            if (u == null || u == Main) return;
            if (party.Remove(u))
            {
                if (leader == u) leader = Main;
                RebuildField();
            }
            Raise(new SessionEvent { Kind = SessionEventKind.CompanionDismissed, Id = companionId, Unit = u, Text = $"{u.Name} leaves the party." });
            Raise(new SessionEvent { Kind = SessionEventKind.PartyChanged, Id = companionId, Unit = u });
        }

        Unit CreateCompanionUnit(CompanionDef def)
        {
            var leftovers = new List<ItemInstance>();
            var u = UnitFactory.CreateCompanion(Db, def, PartyLevel, true, leftovers);
            Progression.LearnAllAvailable(u);   // ranks of talent-granted abilities
            if (Settings.VeteranGear && u.Level > 1) leftovers.AddRange(StartingGear.EquipLevelGear(Db, u, Rng));
            u.Xp = Main != null ? Main.Xp : 0;
            u.AutoPlay = Settings.CompanionAutoPlay;
            u.InvalidateStats();
            u.RestoreFull();
            foreach (var it in leftovers) if (it != null) Inventory.Add(it);
            if (Main != null) { u.Position = Main.Position; u.Facing = Main.Facing; }
            return u;
        }

        // ================================================================= approval

        public int GetApproval(string companionId) =>
            companionId != null && approval.TryGetValue(companionId, out var v) ? v : 0;

        /// <summary>
        /// Changes a companion's approval (dialogue outcome), BG3-style: only a companion who witnesses the scene reacts —
        /// one in the active party, or the companion the party is talking to (its own recruitment talk). A companion not
        /// met yet, waiting at camp or away is unaffected and raises no ApprovalChanged event.
        /// </summary>
        public void ChangeApproval(string companionId, int delta)
        {
            if (resetting || string.IsNullOrEmpty(companionId) || delta == 0) return;
            if (!WitnessesScene(companionId)) return;
            approval[companionId] = GetApproval(companionId) + delta;
            string name = Db.Companions.TryGetValue(companionId, out var c) ? c.name : companionId;
            Raise(new SessionEvent
            {
                Kind = SessionEventKind.ApprovalChanged, Id = companionId, Amount = delta, Unit = FindMember(companionId),
                Text = delta > 0 ? $"{name} approves." : $"{name} disapproves.",
            });
        }

        /// <summary>The companion is present for what the party does now: in the active party, or the one being talked to.</summary>
        public bool WitnessesScene(string companionId)
        {
            if (string.IsNullOrEmpty(companionId)) return false;
            var u = FindMember(companionId);
            if (u != null && u != Main && party.Contains(u)) return true;
            return Dialogue != null && Dialogue.IsActive && Dialogue.OwnerId == companionId && Db.Companions.ContainsKey(companionId);
        }

        /// <summary>All approval values (companion id → value).</summary>
        public IReadOnlyDictionary<string, int> Approvals => approval;

        // ================================================================= xp & levels

        /// <summary>Raw XP from data (dialogue outcome / quest reward), paid at the XP rate of the CONTENT's level
        /// (config.xpRateByLevel, else config.xpRate): a quest's stage outcomes and rewards at Progression.QuestLevel,
        /// other outcomes at the current map's band bottom (Progression.MapLevel); never above the main character's
        /// level (Progression.RateLevel).</summary>
        public void GiveXP(int amount)
        {
            if (resetting || amount <= 0) return;
            var paying = World?.Quests?.Paying;
            int content = paying != null ? Progression.QuestLevel(Db, paying) : Progression.MapLevel(MapDef);
            GivePartyXp(Progression.ContentXp(Db, amount, content, PartyLevel));
        }

        /// <summary>Gives already-scaled XP to the party (main character; companions are level-synced). Returns the level-ups.</summary>
        public List<LevelUpInfo> GivePartyXp(int amount)
        {
            var ups = new List<LevelUpInfo>();
            var main = Main;
            if (main == null || amount <= 0) return ups;
            if (main.Level >= Math.Max(1, Db.Config.maxLevel)) return ups;
            Raise(new SessionEvent { Kind = SessionEventKind.XpGained, Amount = amount, Unit = main, Text = $"+{amount} XP" });
            var mainUps = Progression.GiveXp(main, amount);
            foreach (var info in mainUps)
            {
                ups.Add(info);
                RaiseLevelUp(main, info);
            }
            foreach (var c in roster)
            {
                if (c == main) continue;
                SyncCompanionLevel(c, ups);
            }
            if (mainUps.Count > 0)
            {
                int pts = Progression.TalentPointsAvailable(main);
                if (pts > 0) Raise(new SessionEvent { Kind = SessionEventKind.TalentPointsAvailable, Unit = main, Amount = pts, Text = $"{main.Name} has {pts} unspent talent point{(pts == 1 ? "" : "s")}." });
            }
            return ups;
        }

        void RaiseLevelUp(Unit u, LevelUpInfo info)
        {
            string text = $"{u.Name} reached level {info.NewLevel}!";
            if (info.TalentPointsGained > 0) text += $" +{info.TalentPointsGained} talent point{(info.TalentPointsGained == 1 ? "" : "s")}.";
            if (u == Main && info.NewTrainable.Count > 0) text += " New abilities can be trained.";
            Raise(new SessionEvent { Kind = SessionEventKind.LevelUp, Unit = u, Amount = info.NewLevel, LevelUp = info, Text = text });
        }

        /// <summary>Raises a companion to the main character's level (auto-training and talents per settings).</summary>
        void SyncCompanionLevel(Unit c, List<LevelUpInfo> into)
        {
            var main = Main;
            if (main == null || c == null) return;
            bool changed = false;
            while (c.Level < main.Level)
            {
                var info = Progression.SetLevel(c, c.Level + 1);
                into?.Add(info);
                RaiseLevelUp(c, info);
                changed = true;
            }
            c.Xp = main.Xp;
            if (!changed) return;
            if (Settings.CompanionAutoTrain) LearnAllWithEvents(c);
            if (Settings.AutoAllocateCompanionTalents && Progression.TalentPointsAvailable(c) > 0)
            {
                var before = new Dictionary<string, int>(c.Abilities);
                Progression.AutoAllocateTalents(c);
                RaiseNewAbilities(c, before);
                if (Settings.CompanionAutoTrain) LearnAllWithEvents(c);
            }
            int pts = Progression.TalentPointsAvailable(c);
            if (pts > 0) Raise(new SessionEvent { Kind = SessionEventKind.TalentPointsAvailable, Unit = c, Amount = pts, Text = $"{c.Name} has {pts} unspent talent point{(pts == 1 ? "" : "s")}." });
        }

        void LearnAllWithEvents(Unit u)
        {
            var before = new Dictionary<string, int>(u.Abilities);
            Progression.LearnAllAvailable(u);
            RaiseNewAbilities(u, before);
        }

        void RaiseNewAbilities(Unit u, Dictionary<string, int> before)
        {
            var keys = new List<string>(u.Abilities.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var id in keys)
            {
                int r = u.Abilities[id];
                before.TryGetValue(id, out var old);
                if (r <= old) continue;
                var a = Db.Ability(id);
                if (a == null || a.hidden || a.passive) continue;
                Raise(new SessionEvent
                {
                    Kind = SessionEventKind.AbilityLearned, Unit = u, Id = id, Amount = r,
                    Text = AbilityRules.RankCount(a) > 1 ? $"{u.Name} learned {a.name} (Rank {r})." : $"{u.Name} learned {a.name}.",
                });
            }
        }

        // ================================================================= formation

        /// <summary>Follower positions behind <paramref name="leaderPos"/> facing <paramref name="facing"/> (walkable, clamped).</summary>
        public List<Vec2> FormationSlots(Vec2 leaderPos, Vec2 facing, int count)
        {
            var list = new List<Vec2>(count);
            var f = facing.SqrLength > 1e-6f ? facing.Normalized : Vec2.Right;
            var side = new Vec2(-f.y, f.x);
            for (int i = 0; i < count; i++)
            {
                var o = i < FormationOffsets.Length ? FormationOffsets[i] : new Vec2(1.3f * (1 + i / 2), (i % 2 == 0 ? 1f : -1f) * 0.9f);
                var p = leaderPos - f * o.x + side * o.y;
                if (Nav != null) p = Nav.ClampToWalkable(Nav.ClampToBounds(p), ExploreAgent);
                list.Add(p);
            }
            return list;
        }

        static NavAgent ExploreAgent => NavAgent.Default.IgnoringAllUnits();

        /// <summary>Places the party units on standing spots around a point (map entry).</summary>
        void PlacePartyAt(Vec2 center, Vec2 facing)
        {
            var units = PartyUnits();
            if (units.Count == 0) return;
            var spots = new List<Vec2>();
            if (Nav != null) Nav.FindStandingSpots(center, units.Count, 1.2f, ExploreAgent, spots);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                u.Position = i < spots.Count ? spots[i] : center;
                u.Facing = facing;
            }
            // the leader takes the spot closest to the spawn point
            var l = Leader;
            if (l != null && units.Count > 1 && spots.Count > 0)
            {
                int li = units.IndexOf(l);
                if (li > 0) { var tmp = units[0].Position; units[0].Position = l.Position; l.Position = tmp; }
            }
        }

        /// <summary>Puts a unit on a free spot next to the leader.</summary>
        void PlaceNearLeader(Unit u)
        {
            var l = Leader;
            if (l == null || l == u) return;
            var spots = new List<Vec2>();
            if (Nav != null) Nav.FindStandingSpots(l.Position, 16, 1.0f, ExploreAgent, spots);   // 16: room for a raid of 10 and pets
            foreach (var s in spots)
            {
                bool free = true;
                foreach (var o in PartyUnits())
                    if (o != u && Vec2.Distance(o.Position, s) < 0.9f) { free = false; break; }
                if (!free) continue;
                u.Position = s;
                u.Facing = l.Facing;
                return;
            }
            u.Position = spots.Count > 0 ? spots[spots.Count - 1] : l.Position;
            u.Facing = l.Facing;
        }

        // ================================================================= pets

        void OnFieldUnitAdded(Unit u)
        {
            if (u == null || u.Owner == null || u.Kind != UnitKind.Pet) return;
            if (!party.Contains(u.Owner)) return;
            u.AutoPlay = u.Owner.AutoPlay;
            Raise(new SessionEvent { Kind = SessionEventKind.PetChanged, Unit = u.Owner, Text = $"{u.Owner.Name}: {u.Name} appears." });
        }

        void OnFieldUnitRemoved(Unit u)
        {
            if (u == null || u.Owner == null || u.Kind != UnitKind.Pet) return;
            Raise(new SessionEvent { Kind = SessionEventKind.PetChanged, Unit = u.Owner });
        }

        // ================================================================= dialogue party snapshot

        IReadOnlyList<PartyMemberInfo> IDialogueContext.Party
        {
            get
            {
                partyInfo.Clear();
                foreach (var u in party)
                {
                    var st = u.Stats;
                    partyInfo.Add(new PartyMemberInfo(MemberId(u), u.Name, u.ClassId, u.Level, u == Main,
                        new PrimaryStats { strength = st.Strength, agility = st.Agility, stamina = st.Stamina, intellect = st.Intellect, spirit = st.Spirit }));
                }
                return partyInfo;
            }
        }

        /// <summary>Dialogue/skill-check snapshot of one party member.</summary>
        public PartyMemberInfo MemberInfo(Unit u)
        {
            if (u == null) return null;
            var st = u.Stats;
            return new PartyMemberInfo(MemberId(u), u.Name, u.ClassId, u.Level, u == Main,
                new PrimaryStats { strength = st.Strength, agility = st.Agility, stamina = st.Stamina, intellect = st.Intellect, spirit = st.Spirit });
        }
    }
}
