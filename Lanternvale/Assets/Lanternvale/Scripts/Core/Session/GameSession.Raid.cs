// Raids (Docs/Expansion.md §2.7, §6; Docs/SessionAPI.md §3 "Raids"): a raid map (MapDef.raidSize > 0) is entered with a
// party the player picks (EnterRaid, up to raidSize characters); walking, clicking or teleporting into one without a
// selection raises RaidPartyRequested instead of travelling. Leaving to a non-raid map restores the party, leader and
// auto-play from before the raid (companions dismissed meanwhile stay away). A wipe on a raid map sends the party,
// healed, to the raid's return map instead of ending the game. No lockouts.
// Also here, because raids made them necessary: the battle formation by role (PrepareEncounter) and the encounter
// health scale for parties above 4 on ordinary maps.
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
        /// <summary>The raid in progress and the normal party to restore when it ends.</summary>
        sealed class RaidState
        {
            /// <summary>The main character when the raid began: NewGame replaces it, which retires a stale state.</summary>
            public Unit Main;
            public int Size;
            /// <summary>Member ids of the party before the raid, in party order (Main first).</summary>
            public readonly List<string> NormalParty = new List<string>();
            public string NormalLeader = "";
            /// <summary>Member ids of every recruited character that had auto-play on before the raid (sorted).</summary>
            public readonly List<string> NormalAutoPlay = new List<string>();
        }

        RaidState raid;
        /// <summary>FinishBattle is sending a wiped raid home: the RaidEnded event says so (Amount 1).</summary>
        bool raidWiping;

        RaidState ActiveRaid => raid != null && Main != null && raid.Main == Main ? raid : null;

        /// <summary>True while a raid party is active (on a raid map).</summary>
        public bool InRaid => ActiveRaid != null;

        /// <summary>Size of the active raid (0 when not in a raid).</summary>
        public int RaidSize => ActiveRaid?.Size ?? 0;

        /// <summary>The most characters the active party may hold on <paramref name="mapId"/>: the map's raidSize for a
        /// raid map, else config.partySize.</summary>
        public int MaxPartySizeOn(string mapId)
        {
            var def = RaidMapDef(mapId);
            return def != null ? def.raidSize : Math.Max(1, Db.Config.partySize);
        }

        /// <summary>The map when it is a raid map (raidSize > 0), else null.</summary>
        MapDef RaidMapDef(string mapId) =>
            !string.IsNullOrEmpty(mapId) && Db.Maps.TryGetValue(mapId, out var m) && m != null && m.raidSize > 0 ? m : null;

        /// <summary>Characters the player may take into a raid: Main, the active party in its order, then the companions
        /// waiting at camp in roster order (Away ones are excluded).</summary>
        public List<Unit> RaidCandidates()
        {
            var list = new List<Unit>();
            if (!hasGame || Main == null) return list;
            list.Add(Main);
            foreach (var u in party) if (u != Main && !list.Contains(u)) list.Add(u);
            foreach (var u in Camp()) if (!list.Contains(u)) list.Add(u);
            return list;
        }

        /// <summary>Null when the party <paramref name="ids"/> (member ids, Main included) can enter the raid map now,
        /// else the reason.</summary>
        public string CannotEnterRaidReason(string mapId, IReadOnlyList<string> ids)
        {
            if (!hasGame || Main == null) return "No game.";
            if (gameOver) return "The party has fallen.";
            if (Battle != null) return "Not during combat.";
            if (Dialogue.IsActive) return "Not during a conversation.";
            if (!Db.Maps.TryGetValue(mapId ?? "", out var def) || def == null) return $"Unknown map '{mapId}'.";
            if (def.raidSize <= 0) return $"{(string.IsNullOrEmpty(def.name) ? def.id : def.name)} is not a raid.";
            if (InRaid) return "The raid party is already formed.";
            if (ids == null || ids.Count == 0) return "Choose who comes along.";
            var candidates = RaidCandidates();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool main = false;
            foreach (var id in ids)
            {
                var u = FindMember(id);
                if (u == null) return $"Unknown party member '{id}'.";
                if (!seen.Add(id)) return $"{u.Name} is chosen twice.";
                if (u == Main) { main = true; continue; }
                if (!candidates.Contains(u)) return $"{u.Name} is not with you. Find them and ask them to come along.";
            }
            if (!main) return $"{Main.Name} leads the raid and must come along.";
            if (ids.Count > def.raidSize) return $"The raid takes at most {def.raidSize}.";
            return null;
        }

        /// <summary>Remembers the current party, forms the raid party from <paramref name="ids"/> (Main first, at most
        /// raidSize), optionally turns auto-play on for the companions, then travels to mapId/spawnId.
        /// Null on success, else the reason.</summary>
        public string EnterRaid(string mapId, string spawnId, IReadOnlyList<string> ids, bool companionsAutoPlay)
        {
            var why = CannotEnterRaidReason(mapId, ids);
            if (why != null) { LastError = why; return why; }
            var def = Db.Maps[mapId];
            var st = new RaidState { Main = Main, Size = def.raidSize, NormalLeader = MemberId(Leader) };
            foreach (var u in party) st.NormalParty.Add(MemberId(u));
            foreach (var u in roster) if (u.AutoPlay) st.NormalAutoPlay.Add(MemberId(u));
            st.NormalAutoPlay.Sort(StringComparer.Ordinal);

            var members = new List<Unit> { Main };
            foreach (var id in ids)
            {
                var u = FindMember(id);
                if (u != null && !members.Contains(u)) members.Add(u);
            }
            party.Clear();
            party.AddRange(members);
            if (leader == null || !party.Contains(leader)) leader = Main;
            if (companionsAutoPlay) foreach (var u in party) if (u != Main) SetAutoPlay(u, true);
            raid = st;

            EnterMap(mapId, string.IsNullOrEmpty(spawnId) ? "default" : spawnId);
            if (MapId != mapId)
            {
                // cannot happen after CannotEnterRaidReason; never leave a half-formed raid behind
                why = string.IsNullOrEmpty(LastError) ? "The raid could not be entered." : LastError;
                EndRaidParty();
                RebuildField();
                LastError = why;
                return why;
            }
            Raise(new SessionEvent
            {
                Kind = SessionEventKind.RaidStarted, Id = mapId, Amount = st.Size,
                Text = $"The raid gathers: {party.Count} heroes enter {def.name}.",
            });
            Raise(new SessionEvent { Kind = SessionEventKind.PartyChanged });
            return null;
        }

        /// <summary>
        /// The raid gate (EnterMap, CheckTriggers, UseTransition; Teleport through EnterMap after its dialogue): travelling
        /// to a raid map without a raid party raises RaidPartyRequested (Id = map, Id2 = spawn, Amount = raidSize) and
        /// returns true — the caller does not travel. The picker answers with EnterRaid.
        /// </summary>
        bool RaidGate(string mapId, string spawnId)
        {
            var def = RaidMapDef(mapId);
            if (def == null || InRaid) return false;
            LastError = "Choose the raid party first.";
            Raise(new SessionEvent
            {
                Kind = SessionEventKind.RaidPartyRequested, Id = def.id, Id2 = string.IsNullOrEmpty(spawnId) ? "default" : spawnId,
                Amount = def.raidSize, Text = $"{def.name}: choose up to {def.raidSize} heroes for the raid.",
            });
            return true;
        }

        /// <summary>EnterMap, before the party is placed: travelling from a raid to a non-raid map restores the normal party
        /// (no events yet). Returns the raid map left, or null.</summary>
        string LeaveRaidFor(MapDef target)
        {
            if (!InRaid || target == null || target.raidSize > 0) return null;
            string left = MapId;
            EndRaidParty();
            return left;
        }

        /// <summary>EnterMap, after MapEntered: announces the end of the raid left by <see cref="LeaveRaidFor"/>.</summary>
        void RaiseRaidEnded(string mapLeft)
        {
            Raise(new SessionEvent
            {
                Kind = SessionEventKind.RaidEnded, Id = mapLeft ?? "", Amount = raidWiping ? 1 : 0,
                Text = raidWiping ? "The raid has wiped." : "The raid is over. The party regroups.",
            });
            Raise(new SessionEvent { Kind = SessionEventKind.PartyChanged });
        }

        /// <summary>
        /// Restores the party from before the raid: its members in their order (Main first; companions dismissed during
        /// the raid are Away and dropped; at most config.partySize), the leader (else Main) and every recruited
        /// character's auto-play. The raid state is cleared. Raises nothing (the field is rebuilt by the caller).
        /// </summary>
        void EndRaidParty()
        {
            var st = ActiveRaid;
            raid = null;
            if (st == null) return;
            var restored = new List<Unit> { Main };
            int cap = Math.Max(1, Db.Config.partySize);
            foreach (var id in st.NormalParty)
            {
                if (restored.Count >= cap) break;
                var u = FindMember(id);
                if (u == null || restored.Contains(u)) continue;
                if (!World.Flags.IsSet(WorldRules.RecruitedFlag(id))) continue;   // dismissed during the raid: Away
                restored.Add(u);
            }
            party.Clear();
            party.AddRange(restored);
            var l = FindMember(st.NormalLeader);
            leader = l != null && party.Contains(l) ? l : Main;
            foreach (var u in roster) SetAutoPlay(u, st.NormalAutoPlay.Contains(MemberId(u)));
        }

        /// <summary>Recruit during a raid: the newcomer's auto-play is the one it keeps after the raid.</summary>
        void NoteRecruitedInRaid(Unit u)
        {
            var st = ActiveRaid;
            if (st == null || u == null || !u.AutoPlay) return;
            string id = MemberId(u);
            if (st.NormalAutoPlay.Contains(id)) return;
            st.NormalAutoPlay.Add(id);
            st.NormalAutoPlay.Sort(StringComparer.Ordinal);
        }

        // ================================================================= raid wipe

        /// <summary>
        /// FinishBattle on a Defeat: on a raid map with a valid return map the party is not lost. CombatEnded (Defeat) is
        /// raised, the encounter is reset, the party travels to raidReturnMap/raidReturnSpawn (which ends the raid:
        /// RaidEnded with Amount 1), every recruited character is revived and fully restored like a long rest without the
        /// clock, and PartyHealed carries the notice. False (game over as anywhere else) on any other map.
        /// </summary>
        bool TryRaidWipe(Battle b, EncounterDef enc, BattleSummary s)
        {
            var def = MapDef;
            if (def == null || def.raidSize <= 0) return false;
            var home = string.IsNullOrEmpty(def.raidReturnMap) ? null : World.GetMap(def.raidReturnMap);
            if (home == null || home.Def.raidSize > 0) return false;

            if (enc != null && Map != null) Map.ResetEncounter(enc.id);   // no lockouts: the fight waits for the next attempt
            foreach (var m in roster)
            {
                m.Dead = false;
                m.Downed = false;
                m.Health = Math.Max(1f, m.Health);
                if (m.Pet != null && (m.Pet.Dead || !m.Pet.IsAlive || !m.Pet.IsPersistentPet))
                {
                    m.Pet = null;
                    Raise(new SessionEvent { Kind = SessionEventKind.PetChanged, Unit = m });
                }
            }
            Raise(new SessionEvent { Kind = SessionEventKind.CombatEnded, Outcome = CombatEndKind.Defeat, Id = s.EncounterId, Battle = b, Text = "The raid has wiped..." });

            raidWiping = true;
            try { EnterMap(home.Id, string.IsNullOrEmpty(def.raidReturnSpawn) ? "default" : def.raidReturnSpawn); }
            finally { raidWiping = false; }
            if (InRaid) EndRaidParty();   // EnterMap refused (cannot happen out of combat and dialogue): still end the raid
            foreach (var u in roster)
            {
                RestUnit(u);
                if (u.HunterPet != null && u.HunterPet.Dead) { u.HunterPet.Dead = false; u.HunterPet.HealthFraction = 1f; }
                if (u.Pet != null && u.Pet.IsAlive) RestUnit(u.Pet);
            }
            string where = MapDef?.name ?? home.Def.name;
            Raise(new SessionEvent { Kind = SessionEventKind.PartyHealed, Text = $"The raid has wiped. You come to in {where}, your wounds tended." });
            FlushFlagsChanged();
            return true;
        }

        // ================================================================= save

        /// <summary>SessionSaveData.raid: null when not in a raid.</summary>
        RaidSaveData SaveRaid()
        {
            var st = ActiveRaid;
            if (st == null) return null;
            var d = new RaidSaveData { size = st.Size, normalLeader = st.NormalLeader ?? "" };
            d.normalParty.AddRange(st.NormalParty);
            d.normalAutoPlay.AddRange(st.NormalAutoPlay);
            d.normalAutoPlay.Sort(StringComparer.Ordinal);
            return d;
        }

        /// <summary>
        /// ApplySave, after the party: restores the raid state. A raid saved on a map that is no longer a raid map (the
        /// data changed) ends at once: the normal party is restored silently.
        /// </summary>
        void LoadRaid(RaidSaveData d)
        {
            raid = null;
            if (d == null || Main == null) return;
            var st = new RaidState { Main = Main, Size = Math.Max(1, d.size), NormalLeader = d.normalLeader ?? "" };
            if (d.normalParty != null)
                foreach (var id in d.normalParty) if (!string.IsNullOrEmpty(id)) st.NormalParty.Add(id);
            if (st.NormalParty.Count == 0 || st.NormalParty[0] != MainId) { st.NormalParty.Remove(MainId); st.NormalParty.Insert(0, MainId); }
            if (d.normalAutoPlay != null)
                foreach (var id in d.normalAutoPlay) if (!string.IsNullOrEmpty(id) && !st.NormalAutoPlay.Contains(id)) st.NormalAutoPlay.Add(id);
            st.NormalAutoPlay.Sort(StringComparer.Ordinal);
            raid = st;
            if (RaidMapDef(MapId) == null) EndRaidParty();
        }

        // ================================================================= encounters at party scale

        /// <summary>Party characters beyond this make ordinary encounters tougher.</summary>
        public const int EncounterScaleBaseParty = 4;
        /// <summary>Extra enemy health per party character beyond <see cref="EncounterScaleBaseParty"/>.</summary>
        public const float EncounterHealthPerExtraMember = 0.2f;

        /// <summary>
        /// Enemy health multiplier of an encounter fought by <paramref name="characters"/> party characters:
        /// 1 + 0.2 × max(0, n − 4) on ordinary maps (a party of 5 → 1.2); always 1 on raid maps, whose creatures are tuned
        /// for the raid in data.
        /// </summary>
        public static float EncounterHealthScale(int characters, bool raidMap) =>
            raidMap ? 1f : 1f + EncounterHealthPerExtraMember * Math.Max(0, characters - EncounterScaleBaseParty);

        // ================================================================= battle formation

        /// <summary>Front row (tanks): this far short of the nearest enemy, along the party → enemies axis.</summary>
        public const float FormationFrontGap = 2.5f;
        /// <summary>The formation moves the party at most this far towards the enemies (and at most 3 m away from them).</summary>
        public const float FormationMaxAdvance = 4f;
        /// <summary>Lateral spacing inside a row.</summary>
        public const float FormationSpacing = 1.5f;

        /// <summary>Metres behind the front row by role: tanks, melee, ranged, healers.</summary>
        static float FormationRowDepth(UnitRole role)
        {
            switch (role)
            {
                case UnitRole.Tank: return 0f;
                case UnitRole.MeleeDps: return 1f;
                case UnitRole.Healer: return 5f;
                default: return 3.5f;   // ranged (and anything unknown)
            }
        }

        readonly List<Vec2> formationSpots = new List<Vec2>();

        /// <summary>
        /// PrepareEncounter (not when the party is unseen or an opener is armed, not against training dummies): stands the
        /// party in rows facing the enemies — tanks in front (2.5 m short of the nearest enemy, the party moving at most 4 m
        /// forward), melee 1 m behind them, ranged 3.5 m, healers 5 m, each row centred on the party → enemies axis
        /// (party order, alternating sides, 1.5 m apart) — and pets beside their owner. Each unit takes the free walkable
        /// spot nearest its slot (Nav.FindStandingSpots, never on another unit), and keeps its place when that spot is not
        /// connected to the leader's ground (across a river or wall). Totems and summons stay where they were placed.
        /// Registers every unit with Nav; enemies keep their positions (moved only off unwalkable ground).
        /// </summary>
        void ArrangeBattleFormation(List<Unit> units, List<Unit> enemies)
        {
            if (Nav == null || units == null || enemies == null || enemies.Count == 0) return;
            var chars = new List<Unit>();
            foreach (var u in units) if (u.IsCharacter) chars.Add(u);
            if (chars.Count == 0) return;
            var from = Leader != null && chars.Contains(Leader) ? Leader.Position : chars[0].Position;

            var p = Centroid(chars);
            var f = (Centroid(enemies) - p).Normalized;
            if (f.SqrLength < 1e-6f) f = (Leader?.Facing ?? Vec2.Right).Normalized;
            if (f.SqrLength < 1e-6f) f = Vec2.Right;
            var side = new Vec2(-f.y, f.x);
            float front = float.MaxValue;
            foreach (var e in enemies) front = Math.Min(front, Vec2.Dot(e.Position - p, f));
            var anchor = p + f * MathUtil.Clamp(front - FormationFrontGap, -3f, FormationMaxAdvance);

            Nav.ClearUnits();
            foreach (var e in enemies)
            {
                var agent = NavAgent.ForUnit(e.Radius, e.Id);
                if (!Nav.IsWalkable(e.Position, agent))
                {
                    formationSpots.Clear();
                    Nav.FindStandingSpots(e.Position, 1, e.Radius * 2f, agent, formationSpots);
                    if (formationSpots.Count > 0) e.Position = formationSpots[0];
                }
                Nav.SetUnit(e.Id, e.Position, e.Radius);
            }
            foreach (var u in units)
                if (!u.IsCharacter && !(u.Kind == UnitKind.Pet && u.Owner != null && chars.Contains(u.Owner)))
                    Nav.SetUnit(u.Id, u.Position, u.Radius);   // totems, summons, ownerless pets: where they stand

            var lateral = new Dictionary<Unit, float>();
            foreach (var role in new[] { UnitRole.Tank, UnitRole.MeleeDps, UnitRole.RangedDps, UnitRole.Healer })
            {
                int k = 0;
                foreach (var u in chars)
                {
                    if (u.Role != role) continue;
                    float lat = (k + 1) / 2 * FormationSpacing * (k % 2 == 1 ? 1f : -1f);
                    k++;
                    lateral[u] = lat;
                    PlaceInFormation(u, anchor - f * FormationRowDepth(role) + side * lat, from, f);
                }
            }
            foreach (var u in units)
            {
                if (u.Kind != UnitKind.Pet || u.Owner == null || !chars.Contains(u.Owner)) continue;
                lateral.TryGetValue(u.Owner, out var lat);
                PlaceInFormation(u, u.Owner.Position + side * (lat > 0f ? 1.1f : -1.1f), from, f);
            }
        }

        void PlaceInFormation(Unit u, Vec2 slot, Vec2 from, Vec2 facing)
        {
            var agent = NavAgent.ForUnit(u.Radius, u.Id);
            formationSpots.Clear();
            Nav.FindStandingSpots(Nav.ClampToBounds(slot), 1, u.Radius * 2f, agent, formationSpots);
            if (formationSpots.Count > 0 && Nav.AreConnected(from, formationSpots[0], u.Radius))
                u.Position = formationSpots[0];
            else if (!Nav.IsWalkable(u.Position, agent))
            {
                formationSpots.Clear();
                Nav.FindStandingSpots(u.Position, 1, u.Radius * 2f, agent, formationSpots);
                if (formationSpots.Count > 0) u.Position = formationSpots[0];
            }
            u.Facing = facing;
            Nav.SetUnit(u.Id, u.Position, u.Radius);
        }
    }
}
