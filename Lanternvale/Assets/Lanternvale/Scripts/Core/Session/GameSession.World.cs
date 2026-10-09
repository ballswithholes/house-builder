// Maps, movement, triggers (regions, encounters, transitions), interactions, dialogue and IDialogueContext.
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
        public string MapId { get; private set; } = "";
        /// <summary>Runtime state of the current map (encounters, chests, regions).</summary>
        public MapRuntime Map { get; private set; }
        public MapDef MapDef => Map?.Def;
        /// <summary>Navigation grid of the current map (combat units are registered while fighting).</summary>
        public NavGrid Nav { get; private set; }
        NavGridPathfinder pathfinder;        // battles (tracks unit occupancy)
        NavGridPathfinder fieldPathfinder;   // exploration context (units never block)
        /// <summary>Pathfinder used by battles on this map.</summary>
        public IPathfinder Pathfinder => pathfinder;
        /// <summary>Exploration context (InCombat = false) holding PartyUnits(): out-of-combat abilities and items.</summary>
        public Battle Field { get; private set; }

        /// <summary>Distance at which the UI should consider the leader next to an NPC/chest (not enforced).</summary>
        public const float InteractionRange = 2.5f;

        readonly HashSet<string> suppressedEncounters = new HashSet<string>(StringComparer.Ordinal);
        bool transitionArmed;
        string dialogueEncounterId = "";
        readonly List<Action> afterDialogue = new List<Action>();

        // ================================================================= maps

        /// <summary>Travels to a map spawn point (transitions, Teleport outcome). Not during combat.</summary>
        public void EnterMap(string mapId, string spawnId = "default")
        {
            if (resetting) return;
            if (Battle != null) { LastError = "Cannot travel during combat."; return; }
            if (Dialogue.IsActive) { afterDialogue.Add(() => EnterMap(mapId, spawnId)); return; }
            var rt = World.GetMap(mapId);
            if (rt == null) { LastError = $"Unknown map '{mapId}'."; return; }
            if (RaidGate(rt.Id, spawnId)) return;   // a raid map without a raid party: RaidPartyRequested (GameSession.Raid.cs)
            if (PendingLoot != null) CloseLoot(true);
            CloseVendor();
            CloseTrainer();
            CloseRespec();
            DetachField();
            ClearOwnedSummons();
            string raidLeft = LeaveRaidFor(rt.Def);   // leaving a raid: the normal party comes back before it is placed
            SetMap(rt);
            if (string.IsNullOrEmpty(spawnId)) spawnId = "default";
            var spawn = rt.SpawnPosition(spawnId);
            var facing = spawn.x < rt.Def.width * 0.5f ? Vec2.Right : new Vec2(-1, 0);
            PlacePartyAt(spawn, facing);
            transitionArmed = Map.TransitionAt(Leader.Position) == null;
            Map.OnEnterMap();
            RebuildField();
            Raise(new SessionEvent { Kind = SessionEventKind.MapEntered, Id = rt.Id, Id2 = spawnId, Text = rt.Def.name });
            if (raidLeft != null) RaiseRaidEnded(raidLeft);
            if (LanternsLitHere)
                Raise(new SessionEvent { Kind = SessionEventKind.SpecialOutcome, Id = RekindleLanternsSpecial, Amount = 0 });
            CheckTimeOfDay();
            foreach (var r in Map.UpdatePartyPosition(Leader.Position)) RaiseRegion(r);
            UpdateDiscovery();
        }

        void SetMap(MapRuntime rt)
        {
            OnDiscoveryMapSet(Nav == null);   // ResetState clears Nav: the first map of a new or loaded game
            Map = rt;
            MapId = rt.Id;
            Nav = new NavGrid(rt.Def, null, World.Flags.Test);   // flag-hidden props/chests do not block (GameSession.Discovery)
            pathfinder = new NavGridPathfinder(Nav);
            fieldPathfinder = new NavGridPathfinder(Nav) { AcceptPartial = true, TrackUnits = false };
            suppressedEncounters.Clear();
            soothedEncounters.Clear();
        }

        /// <summary>Rebuilds the exploration context with the current party units and pets (no-op during combat).</summary>
        void RebuildField()
        {
            if (Battle != null) return;
            RefreshNavIfDirty();
            var old = Field;
            DetachField();
            if (!hasGame || roster.Count == 0) return;
            IPathfinder pf = fieldPathfinder != null ? (IPathfinder)fieldPathfinder : new StraightLinePathfinder { UnitsBlock = false };
            var f = Battle.CreateField(Db, Rng, party, pf, Inventory);
            f.RecordEvents = false;
            foreach (var s in OwnedSummons()) f.AddUnit(s);   // totems/guardians placed earlier keep working
            f.EventRaised += ForwardCombatEvent;
            f.UnitAdded += OnFieldUnitAdded;
            f.UnitRemoved += OnFieldUnitRemoved;
            f.FieldEventRaised += OnFieldSpecialEvent;
            Field = f;
            f.RefreshAreaAuras();
            StripAreaChildrenOutside(f, old);
        }

        /// <summary>
        /// Units that are no longer in the exploration context (companions sent to camp or dismissed, their pets, summons
        /// left behind) lose the area-aura children they carry (Blood Pact, paladin auras…): the new field's orphan pass
        /// only walks its own units, so they would keep the buff (and the source aura alive) forever.
        /// </summary>
        void StripAreaChildrenOutside(Battle f, Battle old)
        {
            foreach (var m in roster)
            {
                StripAreaChildren(f, m);
                StripAreaChildren(f, m.Pet);
            }
            if (old != null) foreach (var u in old.Units) StripAreaChildren(f, u);
        }

        static void StripAreaChildren(Battle f, Unit u)
        {
            if (u == null || f.Units.Contains(u)) return;
            for (int i = u.Auras.Count - 1; i >= 0; i--)
                if (i < u.Auras.Count && u.Auras[i].IsAreaChild) f.RemoveAura(u.Auras[i], AuraRemoveReason.SourceGone);
        }

        /// <summary>Living totems and temporary summons of the active party (not pets): spawn views for them in exploration;
        /// they join the next battle, are kept by saves (UnitSaveData.summons) and are cleared on map change.</summary>
        public List<Unit> OwnedSummons()
        {
            var list = new List<Unit>();
            foreach (var m in party)
            {
                foreach (var t in m.Totems.Values) if (t != null && t.IsAlive && !list.Contains(t)) list.Add(t);
                foreach (var x in m.Summons) if (x != null && x.IsAlive && x != m.Pet && !list.Contains(x)) list.Add(x);
            }
            return list;
        }

        /// <summary>Removes the party's totems and temporary summons (map change).</summary>
        void ClearOwnedSummons()
        {
            foreach (var m in roster)
            {
                foreach (var t in m.Totems.Values) if (t != null) t.Dead = true;
                m.Totems.Clear();
                foreach (var x in m.Summons) if (x != null && x != m.Pet) x.Dead = true;
                m.Summons.RemoveAll(x => x == null || x != m.Pet);
            }
        }

        void DetachField()
        {
            if (Field == null) return;
            Field.EventRaised -= ForwardCombatEvent;
            Field.UnitAdded -= OnFieldUnitAdded;
            Field.UnitRemoved -= OnFieldUnitRemoved;
            Field.FieldEventRaised -= OnFieldSpecialEvent;
            Field = null;
        }

        /// <summary>Spirit lanterns are lit (flag lanterns_rekindled).</summary>
        public bool LanternsRekindled => World.Flags.IsSet(LanternsFlag);

        /// <summary>The valley the Heart Lantern relights (the first slice's outdoor maps).</summary>
        public static readonly string[] RekindleMapIds = { "lanternvale", "whisperwood", "shrine" };

        /// <summary>
        /// True when the valley-wide relight (flag lanterns_rekindled) applies to this map. Maps beyond the valley
        /// keep their own dark lanterns and flag-driven dark/lit pairs (Brightwater's memorial, Mirefen's rising
        /// lanterns, the Drowned Vault, the Hollow Heart's grove).
        /// </summary>
        public static bool IsRekindleMap(MapDef def) => def != null && Array.IndexOf(RekindleMapIds, def.id) >= 0;

        /// <summary>Flag lanterns_rekindled is set and the relight applies to the current map.</summary>
        public bool LanternsLitHere => LanternsRekindled && IsRekindleMap(MapDef);

        public List<MapNpcDef> VisibleNpcs()
        {
            var list = new List<MapNpcDef>();
            if (Map != null) list.AddRange(Map.VisibleNpcs());
            return list;
        }

        /// <summary>Encounters whose enemies should be drawn (available and not hidden, or triggered).</summary>
        public List<EncounterDef> VisibleEncounters()
        {
            var list = new List<EncounterDef>();
            if (Map?.Def?.encounters == null) return list;
            foreach (var e in Map.Def.encounters)
                if (Map.IsEncounterVisible(e) && !(Battle != null && BattleEncounter == e)) list.Add(e);
            return list;
        }

        /// <summary>
        /// The enemies an encounter would field if the fight started now (current map first, else any map), for exploration
        /// nameplates: creature id, name, level (scaled to the party like the battle will be), rank, position, idle facing. Changes
        /// nothing and draws no random numbers. Empty when the encounter is unknown.
        /// </summary>
        public List<EncounterEnemyPreview> PreviewEncounter(string encounterId)
        {
            var list = new List<EncounterEnemyPreview>();
            if (string.IsNullOrEmpty(encounterId)) return list;
            var enc = Map?.FindEncounter(encounterId);
            if (enc == null)
                foreach (var m in Db.Maps.Values)
                {
                    if (m?.encounters == null) continue;
                    foreach (var e in m.encounters) if (e != null && e.id == encounterId) { enc = e; break; }
                    if (enc != null) break;
                }
            if (enc?.enemies == null) return list;
            int partyLevel = PartyLevel;
            foreach (var ed in enc.enemies)
            {
                var def = ed != null ? Db.Creature(ed.creature) : null;
                if (def == null) continue;
                int lvl = UnitFactory.CreatureLevel(def, ed.level, partyLevel, null);
                int lo = lvl, hi = lvl;
                if (ed.level <= 0 && !def.scaleToParty) { lo = def.levelMin; hi = Math.Max(def.levelMin, def.levelMax); }
                list.Add(new EncounterEnemyPreview
                {
                    CreatureId = def.id, Name = def.name ?? def.id, Level = lvl, MinLevel = lo, MaxLevel = hi, Rank = def.rank, Type = def.type,
                    Position = ed.pos, Facing = EncounterIdleFacing(enc, ed), Passive = def.ai == AIProfile.Passive,
                });
            }
            return list;
        }

        public string NpcName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (Db.Npcs.TryGetValue(id, out var n)) return n.name;
            if (Db.Companions.TryGetValue(id, out var c)) return c.name;
            return id;
        }

        /// <summary>Dialogue id for an npc (NpcDef.dialogue) or a companion (CompanionDef.recruitDialogue).</summary>
        public string DialogueOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (Db.Npcs.TryGetValue(id, out var n) && !string.IsNullOrEmpty(n.dialogue)) return n.dialogue;
            if (Db.Companions.TryGetValue(id, out var c)) return c.recruitDialogue ?? "";
            return "";
        }

        // ================================================================= movement

        /// <summary>Exploration path (units never block).</summary>
        public NavPath FindPath(Vec2 from, Vec2 to)
        {
            if (Nav == null) return new NavPath();
            return Nav.FindPath(from, to, ExploreAgent, float.PositiveInfinity, new NavPath());
        }

        /// <summary>Plans a party move: the leader's path and each follower's formation slot and path. Changes nothing.</summary>
        public PartyMovePlan PlanPartyMove(Vec2 destination)
        {
            var plan = new PartyMovePlan { Leader = Leader };
            if (!IsExploring || Nav == null || Leader == null) return plan;
            var p = FindPath(Leader.Position, destination);
            plan.Reachable = p.Status != PathStatus.NoPath && p.Count > 0;
            plan.LeaderPath.AddRange(p.Points);
            plan.Length = p.Length;
            var end = p.Count > 0 ? p.End : Leader.Position;
            var dir = p.Count >= 2 ? (end - p.Points[p.Count - 2]) : Leader.Facing;
            var others = new List<Unit>();
            foreach (var u in PartyUnits()) if (u != Leader) others.Add(u);
            var slots = FormationSlots(end, dir, others.Count);
            for (int i = 0; i < others.Count; i++)
            {
                var fp = FindPath(others[i].Position, slots[i]);
                var mp = new UnitMovePlan { Unit = others[i], Destination = fp.Count > 0 ? fp.End : slots[i] };
                mp.Path.AddRange(fp.Points);
                plan.Followers.Add(mp);
            }
            return plan;
        }

        /// <summary>
        /// Reports the party's positions while the Unity layer animates movement, then runs region, encounter and
        /// transition checks. <paramref name="others"/>: positions of the other PartyUnits() in order (null = snap to
        /// formation slots). Stop == true: stop animating (dialogue/combat started, travelled, transition locked).
        /// </summary>
        public TriggerResult UpdatePartyPositions(Vec2 leaderPos, IReadOnlyList<Vec2> others = null)
        {
            if (!IsExploring || Leader == null) return TriggerResult.Nothing;
            ApplyPartyPositions(leaderPos, others);
            return CheckTriggers();
        }

        /// <summary>
        /// Sets the party's positions WITHOUT running any trigger (regions, encounters, transitions): scripted placement,
        /// snapping views after a cutscene or a load, dragging the party in an editor. Same rules as
        /// <see cref="UpdatePartyPositions"/> for <paramref name="others"/> (the other PartyUnits() in order; null = formation
        /// slots), facing and seated food/drink. A leader placed inside a transition does not travel until it has left it.
        /// Not during combat (the battle owns positions). Null on success, else the reason.
        /// </summary>
        public string SetPartyPositions(Vec2 leaderPos, IReadOnlyList<Vec2> others = null)
        {
            if (!hasGame || Leader == null) return "No game.";
            if (gameOver) return "The game is over.";
            if (Battle != null) return "The battle owns positions during combat.";
            ApplyPartyPositions(leaderPos, others);
            if (Map != null && Map.TransitionAt(leaderPos) != null) transitionArmed = false;
            return null;
        }

        void ApplyPartyPositions(Vec2 leaderPos, IReadOnlyList<Vec2> others)
        {
            var l = Leader;
            var units = PartyUnits();
            var d = leaderPos - l.Position;
            if (d.SqrLength > 1e-4f)
            {
                l.Facing = d.Normalized;
                CancelSeatedAuras(l);
            }
            l.Position = leaderPos;
            if (others != null)
            {
                int k = 0;
                foreach (var u in units)
                {
                    if (u == l) continue;
                    if (k < others.Count)
                    {
                        var od = others[k] - u.Position;
                        if (od.SqrLength > 1e-4f) { u.Facing = od.Normalized; CancelSeatedAuras(u); }
                        u.Position = others[k];
                    }
                    k++;
                }
            }
            else if (d.SqrLength > 1e-4f)
            {
                var followers = new List<Unit>();
                foreach (var u in units) if (u != l) followers.Add(u);
                var slots = FormationSlots(leaderPos, l.Facing, followers.Count);
                for (int i = 0; i < followers.Count; i++)
                {
                    if ((followers[i].Position - slots[i]).SqrLength > 1e-4f) CancelSeatedAuras(followers[i]);
                    followers[i].Position = slots[i];
                    followers[i].Facing = l.Facing;
                }
            }
            if (Field != null) Field.RefreshAreaAuras();
        }

        /// <summary>Instant move along the exploration path in small steps (tests, fast travel), stopping at a trigger.</summary>
        public MoveResult MoveLeader(Vec2 destination)
        {
            var res = new MoveResult();
            if (!IsExploring || Leader == null || Nav == null) { res.Reason = "Cannot move now."; return res; }
            var p = FindPath(Leader.Position, destination);
            if (p.Count < 2 || p.Length < 1e-3f)
            {
                res.End = Leader.Position;
                res.Trigger = CheckTriggers();
                return res;
            }
            const float step = 0.25f;
            float walked = 0f;
            res.Moved = true;
            while (walked < p.Length)
            {
                walked = Math.Min(p.Length, walked + step);
                var pos = PointAlong(p.Points, walked);
                var tr = UpdatePartyPositions(pos);
                res.End = pos;
                res.Distance = walked;
                if (tr.Stop) { res.Trigger = tr; return res; }
                if (!IsExploring) break;
            }
            return res;
        }

        static Vec2 PointAlong(List<Vec2> pts, float dist)
        {
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vec2.Distance(pts[i - 1], pts[i]);
                if (acc + seg >= dist) return Vec2.MoveTowards(pts[i - 1], pts[i], dist - acc);
                acc += seg;
            }
            return pts[pts.Count - 1];
        }

        /// <summary>Region entries, encounter triggers (stealth rule) and transitions at the current positions.</summary>
        public TriggerResult CheckTriggers()
        {
            if (!IsExploring || Map == null || Leader == null) return TriggerResult.Nothing;
            var l = Leader;
            foreach (var r in Map.UpdatePartyPosition(l.Position)) RaiseRegion(r);
            UpdateDiscovery();   // passive region checks (may reveal a hidden transition); nav refresh after combat

            // encounters
            UpdateSuppression();
            var enc = FindTriggeredEncounter();
            if (enc != null) return TriggerEncounter(enc);

            // transitions (armed once the leader has been outside every transition since arriving)
            var t = Map.TransitionAt(l.Position);
            if (t == null) { transitionArmed = true; return TriggerResult.Nothing; }
            if (!transitionArmed) return TriggerResult.Nothing;
            transitionArmed = false;
            if (!Map.IsTransitionUnlocked(t))
            {
                Raise(new SessionEvent { Kind = SessionEventKind.TransitionLocked, Id = t.id, Text = string.IsNullOrEmpty(t.lockedText) ? "The way is blocked." : t.lockedText });
                return new TriggerResult { Stop = true, Kind = TriggerKind.Locked, Id = t.id };
            }
            if (RaidGate(t.targetMap, t.targetSpawn)) return new TriggerResult { Stop = true, Kind = TriggerKind.RaidGate, Id = t.targetMap };
            EnterMap(t.targetMap, t.targetSpawn);
            return new TriggerResult { Stop = true, Kind = TriggerKind.Travel, Id = t.targetMap };
        }

        void RaiseRegion(RegionDef r)
        {
            Raise(new SessionEvent { Kind = SessionEventKind.RegionEntered, Id = r.id, Text = r.text ?? "" });
        }

        EncounterDef FindTriggeredEncounter()
        {
            if (Map?.Def?.encounters == null) return null;
            foreach (var e in Map.Def.encounters)
            {
                if (e == null || !Map.IsEncounterAvailable(e) || suppressedEncounters.Contains(e.id)) continue;
                float radius = TriggerRadius(e);
                foreach (var u in party)
                {
                    if (!u.IsAlive) continue;
                    float r = u.IsStealthed ? Math.Min(radius, MapRuntime.StealthDetectRadius) : radius;
                    if ((u.Position - e.pos).SqrLength <= r * r) return e;
                }
            }
            return null;
        }

        /// <summary>Releases suppressed encounters once no party member is within radius + 1 m.</summary>
        void UpdateSuppression()
        {
            if (suppressedEncounters.Count == 0 || Map == null) return;
            List<string> release = null;
            foreach (var id in suppressedEncounters)
            {
                var e = Map.FindEncounter(id);
                bool near = false;
                if (e != null)
                    foreach (var u in party)
                        if ((u.Position - e.pos).Length <= e.radius + 1f) { near = true; break; }
                if (!near) (release ??= new List<string>()).Add(id);
            }
            if (release != null) foreach (var id in release) suppressedEncounters.Remove(id);
        }

        TriggerResult TriggerEncounter(EncounterDef enc)
        {
            if (!string.IsNullOrEmpty(enc.dialogue) && Db.Dialogues.ContainsKey(enc.dialogue))
            {
                Map.MarkEncounterTriggered(enc.id);
                suppressedEncounters.Add(enc.id);
                dialogueEncounterId = enc.id;
                if (StartDialogue(enc.dialogue, enc.id))
                    return new TriggerResult { Stop = true, Kind = Dialogue.IsActive ? TriggerKind.Dialogue : (Battle != null ? TriggerKind.Combat : TriggerKind.None), Id = enc.id };
                dialogueEncounterId = "";
            }
            var b = StartEncounter(enc.id);
            if (b == null) { suppressedEncounters.Add(enc.id); return TriggerResult.Nothing; }
            return new TriggerResult { Stop = true, Kind = TriggerKind.Combat, Id = enc.id };
        }

        // ================================================================= field specials (Mind Soothe, Pick Lock)

        /// <summary>Mind Soothe: encounter id -> real seconds left of its reduced trigger radius (current map only).</summary>
        readonly Dictionary<string, float> soothedEncounters = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly List<string> soothedScratch = new List<string>();
        Unit encounterStandIn;           // the encounter enemy UseAbilityOnEncounter targets, while the ability resolves
        string encounterStandInId = "";
        bool encounterStandInHit;

        /// <summary>Mind Soothe shrinks an encounter's trigger radius by 10 yards (4 m)...</summary>
        public static readonly float SootheRadiusReduction = MathUtil.Yd(10f);
        /// <summary>...but not below 1 m.</summary>
        public const float SoothedMinRadius = 1f;
        public const string MindSootheSpecial = "PriestMindSoothe";
        public const string PickLockSpecial = "RoguePickLock";

        /// <summary>Radius (metres) at which an encounter of the current map triggers now: EncounterDef.radius, reduced while
        /// soothed (stealthed members are only noticed within MapRuntime.StealthDetectRadius of it). 0 when unknown.</summary>
        public float EncounterTriggerRadius(string encounterId)
        {
            var e = Map?.FindEncounter(encounterId);
            return e != null ? TriggerRadius(e) : 0f;
        }

        float TriggerRadius(EncounterDef e) =>
            soothedEncounters.ContainsKey(e.id) ? Math.Max(Math.Min(e.radius, SoothedMinRadius), e.radius - SootheRadiusReduction) : e.radius;

        /// <summary>Real seconds left of Mind Soothe on an encounter of the current map (0 = not soothed).</summary>
        public float EncounterSoothedSeconds(string encounterId) =>
            encounterId != null && soothedEncounters.TryGetValue(encounterId, out var t) ? t : 0f;

        /// <summary>True when the ability is cast on an encounter out of combat without starting the fight (Mind Soothe):
        /// use <see cref="UseAbilityOnEncounter"/> (EngageEncounter with it as the opener does the same).</summary>
        public bool IsEncounterFieldAbility(string abilityId) => Specials.TargetsEncounterOutOfCombat(Db.Ability(abilityId));

        /// <summary>
        /// Casts an encounter field ability (<see cref="IsEncounterFieldAbility"/>: Mind Soothe) out of combat on enemy
        /// <paramref name="targetIndex"/> of an encounter of the current map (EncounterDef.enemies order) without starting
        /// the fight. The enemy is a stand-in built like the battle's (level scaled to the party, at its map position): the
        /// exploration context checks cost, cooldown, range, line of sight and requirements (Humanoid only), pays the cost and
        /// rolls the hit. Mind Soothe that lands shrinks the encounter's trigger radius by 4 m (min 1 m) for 15 s of real
        /// time (Toast; a resist is toasted too). Fails (reason) in combat, during dialogue, for a finished or unknown
        /// encounter, and for abilities that would start the fight.
        /// </summary>
        public ActionResult UseAbilityOnEncounter(Unit u, string abilityId, string encounterId, int targetIndex = 0, int rank = 0)
        {
            if (!hasGame || gameOver) return ActionResult.Fail("No game.");
            if (Battle != null) return ActionResult.Fail("Not during combat.");
            if (Dialogue.IsActive) return ActionResult.Fail("Not during a conversation.");
            var a = Db.Ability(abilityId);
            if (a == null) return ActionResult.Fail($"Unknown ability '{abilityId}'.");
            if (!Specials.TargetsEncounterOutOfCombat(a)) return ActionResult.Fail($"{a.name} would start the fight.");
            var enc = Map?.FindEncounter(encounterId);
            if (enc == null || Map.IsEncounterDone(enc) || !Map.IsEncounterAvailable(enc)) return ActionResult.Fail("There is nobody there.");
            var ctx = ContextFor(u);
            if (ctx == null || ctx != Field) return ActionResult.Fail("That unit is not in the party.");
            var standIn = EncounterStandIn(enc, targetIndex);
            if (standIn == null) return ActionResult.Fail("There is nobody there.");
            encounterStandIn = standIn;
            encounterStandInId = enc.id;
            encounterStandInHit = false;
            try
            {
                var r = ctx.UseAbility(u, abilityId, standIn, null, rank);
                if (!r.Ok) return r;
                if (!encounterStandInHit) Toast($"{standIn.Name} resists {a.name}.");
                AfterFieldAction();
                return r;
            }
            finally
            {
                encounterStandIn = null;
                encounterStandInId = "";
            }
        }

        Unit EncounterStandIn(EncounterDef enc, int index)
        {
            if (enc.enemies == null || enc.enemies.Count == 0) return null;
            var ed = enc.enemies[MathUtil.Clamp(index, 0, enc.enemies.Count - 1)];
            var def = ed != null ? Db.Creature(ed.creature) : null;
            if (def == null) return null;
            var e = UnitFactory.CreateCreature(Db, def, UnitFactory.CreatureLevel(def, ed.level, PartyLevel, null), Team.Enemy);
            e.Position = ed.pos;
            return e;
        }

        /// <summary>Specials.FieldEvent of the exploration context: Mind Soothe on an encounter, Pick Lock used from the bar.</summary>
        void OnFieldSpecialEvent(Unit u, string name, Unit target)
        {
            if (resetting || !hasGame || Battle != null) return;
            switch (name)
            {
                case MindSootheSpecial:
                {
                    if (target == null || target != encounterStandIn || string.IsNullOrEmpty(encounterStandInId)) return;
                    AuraInstance landed = null;
                    foreach (var x in target.Auras) if (!x.IsPassive && x.Caster == u) { landed = x; break; }
                    if (landed == null) return;   // resisted: UseAbilityOnEncounter says so
                    encounterStandInHit = true;
                    float secs = landed.Remaining > 0f ? landed.Remaining : (landed.Duration > 0f ? landed.Duration : 15f);
                    soothedEncounters[encounterStandInId] = secs;
                    Toast($"{target.Name} is soothed: it will only notice you up close ({secs:0} s).");
                    break;
                }
                case PickLockSpecial:
                {
                    // Pick Lock from the action bar: the nearest locked chest within reach of the rogue
                    if (u == null || Map?.Def?.chests == null) return;
                    ChestDef best = null;
                    float bestD = float.MaxValue;
                    foreach (var c in Map.Def.chests)
                    {
                        if (c == null || !Map.IsChestAvailable(c) || !Map.IsChestLocked(c)) continue;
                        float d = Vec2.Distance(u.Position, c.pos);
                        if (d <= InteractionRange + 0.5f && d < bestD) { best = c; bestD = d; }
                    }
                    if (best == null) { Toast("There is no lock to pick here."); return; }
                    PickLock(best.id, u);
                    break;
                }
            }
        }

        /// <summary>Counts Mind Soothe down in real time (exploration).</summary>
        void ElapseSoothedEncounters(float dt)
        {
            if (soothedEncounters.Count == 0) return;
            soothedScratch.Clear();
            foreach (var kv in soothedEncounters) soothedScratch.Add(kv.Key);
            foreach (var id in soothedScratch)
            {
                float left = soothedEncounters[id] - dt;
                if (left <= 1e-3f) soothedEncounters.Remove(id); else soothedEncounters[id] = left;
            }
            soothedScratch.Clear();
        }

        /// <summary>Cancels Food/Drink auras of a unit that moves ("must remain seated").</summary>
        void CancelSeatedAuras(Unit u)
        {
            if (u == null || u.Auras.Count == 0) return;
            for (int i = u.Auras.Count - 1; i >= 0; i--)
            {
                if (i >= u.Auras.Count) continue;
                var a = u.Auras[i];
                if (a.IsPassive || !(a.HasTag("Food") || a.HasTag("Drink"))) continue;
                if (Field != null) Field.RemoveAura(a, AuraRemoveReason.Cancelled);
                else { u.Auras.RemoveAt(i); u.InvalidateStats(); }
            }
        }

        // ================================================================= interactions

        /// <summary>Talks to a map NPC or companion: starts its dialogue (owner = id), or shows its bark.</summary>
        public InteractResult TalkTo(string npcId)
        {
            if (!IsExploring) return InteractResult.Fail("Not now.");
            var dlg = DialogueOf(npcId);
            if (string.IsNullOrEmpty(dlg) || !Db.Dialogues.ContainsKey(dlg))
            {
                if (Db.Npcs.TryGetValue(npcId ?? "", out var n) && !string.IsNullOrEmpty(n.bark))
                {
                    Toast(n.bark);
                    return new InteractResult { Ok = true, Kind = InteractKind.Text, Id = npcId, Message = n.bark };
                }
                return InteractResult.Fail($"{NpcName(npcId)} has nothing to say.");
            }
            if (!StartDialogue(dlg, npcId)) return InteractResult.Fail(LastError);
            return new InteractResult { Ok = true, Kind = InteractKind.Dialogue, Id = dlg };
        }

        /// <summary>Opens a chest: loot window. Kind == Locked when a lock check is needed first.</summary>
        public InteractResult OpenChest(string chestId)
        {
            if (!IsExploring) return InteractResult.Fail("Not now.");
            var c = Map?.FindChest(chestId);
            if (c == null || !Map.IsChestAvailable(c)) return InteractResult.Fail("There is no chest here.");
            if (Map.IsChestOpened(c.id)) return InteractResult.Fail("It's empty.");
            if (Map.IsChestLocked(c))
                return new InteractResult
                {
                    Ok = false, Kind = InteractKind.Locked, Id = c.id,
                    Message = $"Locked ({SkillChecks.DisplayName(c.lockCheck.skill)} DC {c.lockCheck.dc}).",
                };
            Map.MarkChestOpened(c.id);
            var w = new LootWindow { Source = c.id, Title = "Chest" };
            var drop = LootGenerator.Roll(Db, c.lootTable, PartyLevel, Rng, BuildLootContext());
            w.Items.AddRange(drop.Items);
            w.Gold = drop.Gold + Math.Max(0, c.gold);
            if (c.items != null)
                foreach (var id in c.items)
                {
                    var def = Db.Item(id);
                    if (def != null) w.Items.Add(new ItemInstance(def));
                }
            Raise(new SessionEvent { Kind = SessionEventKind.ChestOpened, Id = c.id });
            if (w.Items.Count == 0 && w.Gold == 0) Toast("The chest is empty.");
            OpenLoot(w);
            return new InteractResult { Ok = true, Kind = InteractKind.Loot, Id = c.id };
        }

        /// <summary>Rolls the chest's lock check for the best party member; success unlocks and opens it. Null when not locked.</summary>
        public CheckResult TryUnlockChest(string chestId)
        {
            var c = Map?.FindChest(chestId);
            if (c == null || !IsExploring || !Map.IsChestLocked(c)) return null;
            var res = Map.TryUnlockChest(c, this, Rng);
            if (res == null) return null;
            Raise(new SessionEvent { Kind = SessionEventKind.SkillCheck, Check = res, Id = c.id, Text = res.ToString() });
            if (res.Success) OpenChest(c.id);
            return res;
        }

        /// <summary>
        /// Rogue Pick Lock (rogue_pick_lock): d20 + Sleight of Hand modifier + floor(level / 5) vs the lock DC
        /// (natural 20 succeeds, natural 1 fails). Without a rogue who knows it, falls back to <see cref="TryUnlockChest"/>.
        /// </summary>
        public CheckResult PickLock(string chestId, Unit rogue = null)
        {
            var c = Map?.FindChest(chestId);
            if (c == null || !IsExploring || !Map.IsChestLocked(c)) return null;
            if (rogue == null)
                foreach (var u in party) if (u.IsAlive && u.Knows(PickLockAbility)) { rogue = u; break; }
            if (rogue == null || !rogue.Knows(PickLockAbility) || !party.Contains(rogue)) return TryUnlockChest(chestId);
            var info = MemberInfo(rogue);
            var skill = c.lockCheck.skill;
            int statMod = SkillChecks.StatModifier(SkillChecks.StatValue(info.stats, SkillChecks.StatFor(skill)));
            int prof = SkillChecks.IsProficient(rogue.ClassId, skill) ? SkillChecks.ProficiencyBonus : 0;
            int bonus = rogue.Level / 5 + SkillCheckBonus(info.id, skill);
            int roll = Rng.D20();
            var res = new CheckResult
            {
                Skill = skill, Dc = c.lockCheck.dc, RollerId = info.id, RollerName = rogue.Name, RollerClass = rogue.ClassId,
                Roll = roll, StatModifier = statMod, Proficiency = prof, Bonus = bonus, Modifier = statMod + prof + bonus,
                Critical = roll == 20, Fumble = roll == 1,
            };
            res.Total = roll + res.Modifier;
            res.Success = res.Critical || (!res.Fumble && res.Total >= res.Dc);
            Raise(new SessionEvent { Kind = SessionEventKind.SkillCheck, Check = res, Id = c.id, Text = "Pick Lock: " + res });
            if (res.Success)
            {
                Map.MarkChestUnlocked(c.id);
                OpenChest(c.id);
            }
            return res;
        }

        public const string PickLockAbility = "rogue_pick_lock";

        /// <summary>Uses a map transition: travel, or TransitionLocked with its locked text.</summary>
        public InteractResult UseTransition(string transitionId)
        {
            if (!IsExploring) return InteractResult.Fail("Not now.");
            TransitionDef t = null;
            if (Map?.Def?.transitions != null)
                foreach (var x in Map.Def.transitions) if (x != null && x.id == transitionId) { t = x; break; }
            if (t == null || !Map.IsTransitionVisible(t)) return InteractResult.Fail("There is no way through here.");
            if (!Map.IsTransitionUnlocked(t))
            {
                var text = string.IsNullOrEmpty(t.lockedText) ? "The way is blocked." : t.lockedText;
                Raise(new SessionEvent { Kind = SessionEventKind.TransitionLocked, Id = t.id, Text = text });
                return new InteractResult { Ok = false, Kind = InteractKind.Locked, Id = t.id, Message = text };
            }
            if (RaidGate(t.targetMap, t.targetSpawn))   // no travel: the raid picker opens (RaidPartyRequested)
                return new InteractResult { Ok = true, Kind = InteractKind.None, Id = t.targetMap, Message = LastError };
            EnterMap(t.targetMap, t.targetSpawn);
            return new InteractResult { Ok = true, Kind = InteractKind.Travel, Id = t.targetMap };
        }

        /// <summary>Text of a prop with an interact id (signs, shrines). Also raised as a Toast. A prop with a dialogue
        /// (PropDef.dialogue) starts it instead (owner = the interact id) and returns ""; a hidden prop returns "".
        /// <see cref="InteractProp"/> tells the cases apart.</summary>
        public string InspectProp(string interactId)
        {
            var r = InteractProp(interactId);
            return r.Ok && r.Kind == InteractKind.Text ? r.Message : "";
        }

        public bool InInteractionRange(Vec2 p) => Leader != null && Vec2.Distance(Leader.Position, p) <= InteractionRange;

        // ================================================================= dialogue

        /// <summary>Starts a dialogue (ownerId = npc/companion/encounter talked to). False when unknown or not possible now.</summary>
        public bool StartDialogue(string dialogueId, string ownerId = "")
        {
            LastError = "";
            if (!hasGame || gameOver) { LastError = "No game."; return false; }
            if (Battle != null) { LastError = "Not during combat."; return false; }
            if (string.IsNullOrEmpty(dialogueId) || !Db.Dialogues.ContainsKey(dialogueId)) { LastError = $"Unknown dialogue '{dialogueId}'."; return false; }
            if (Dialogue.IsActive) Dialogue.End();
            Raise(new SessionEvent { Kind = SessionEventKind.DialogueStarted, Id = dialogueId, Id2 = ownerId ?? "" });
            bool ok = Dialogue.Start(dialogueId, ownerId ?? "");
            FlushFlagsChanged();
            return ok;
        }

        public bool ChooseDialogue(int index)
        {
            bool ok = Dialogue.Choose(index);
            FlushFlagsChanged();
            return ok;
        }

        public bool ContinueDialogue()
        {
            bool ok = Dialogue.Continue();
            FlushFlagsChanged();
            return ok;
        }

        public void EndDialogue()
        {
            if (Dialogue.IsActive) Dialogue.End();
            FlushFlagsChanged();
        }

        void OnDialogueEnded(string dialogueId)
        {
            if (resetting) return;
            dialogueEncounterId = "";
            Raise(new SessionEvent { Kind = SessionEventKind.DialogueEnded, Id = dialogueId ?? "" });
            if (afterDialogue.Count > 0)
            {
                var actions = afterDialogue.ToArray();
                afterDialogue.Clear();
                foreach (var a in actions) a();
            }
        }

        void OnCheckRolled(CheckResult r)
        {
            if (resetting || r == null) return;
            Raise(new SessionEvent { Kind = SessionEventKind.SkillCheck, Check = r, Text = r.ToString() });
        }

        // ================================================================= IDialogueContext

        FlagStore IDialogueContext.Flags => World.Flags;
        QuestLog IDialogueContext.Quests => World.Quests;
        /// <summary>Story flags (World.Flags).</summary>
        public FlagStore Flags => World.Flags;
        /// <summary>Quest log (World.Quests).</summary>
        public QuestLog Quests => World.Quests;

        public string PlayerName => Main != null ? Main.Name : "";

        /// <summary>Extra skill check bonus hook (items/buffs). Default: none.</summary>
        public Func<string, SkillCheck, int> ExtraSkillCheckBonus;

        public int SkillCheckBonus(string memberId, SkillCheck skill) => ExtraSkillCheckBonus != null ? ExtraSkillCheckBonus(memberId, skill) : 0;

        /// <summary>Starts an encounter of the current map ("" = the encounter whose dialogue is running). Deferred while talking.</summary>
        public void StartCombat(string encounterId)
        {
            if (resetting) return;
            string id = string.IsNullOrEmpty(encounterId) ? dialogueEncounterId : encounterId;
            if (Dialogue.IsActive) { afterDialogue.Add(() => StartCombat(id)); return; }
            if (string.IsNullOrEmpty(id) || Battle != null) return;
            var enc = Map?.FindEncounter(id);
            if (enc == null) { Log.Warn($"GameSession: StartCombat: no encounter '{id}' on map '{MapId}'"); return; }
            if (Map.IsEncounterDone(enc)) { Log.Info($"GameSession: encounter '{id}' is already done; no combat."); return; }
            StartEncounter(id);
        }

        public void Teleport(string mapId, string spawnId)
        {
            if (resetting) return;
            if (Dialogue.IsActive) { afterDialogue.Add(() => Teleport(mapId, spawnId)); return; }
            EnterMap(mapId, string.IsNullOrEmpty(spawnId) ? "default" : spawnId);
        }

        /// <summary>
        /// Data `Special` outcomes: run through the rules engine's content specials (Specials.RunContentSpecial with the
        /// session as IContentContext → flags + SpecialOutcome events). RekindleLanterns: flag lanterns_rekindled +
        /// SpecialOutcome (Amount 1 = animate now; 0 is raised after every map load while the flag is set).
        /// </summary>
        public void RunSpecial(string specialId, OutcomeDef outcome)
        {
            if (resetting || string.IsNullOrEmpty(specialId)) return;
            pendingSpecialArg = outcome?.value ?? "";
            try
            {
                if (Specials.RunContentSpecial(specialId, this)) return;
            }
            finally { pendingSpecialArg = ""; }
            if (specialId == RekindleLanternsSpecial)
            {
                World.Flags.Set(LanternsFlag, 1);
                Raise(new SessionEvent { Kind = SessionEventKind.SpecialOutcome, Id = specialId, Amount = 1, Text = LanternsText });
                return;
            }
            Log.Warn($"GameSession: special outcome '{specialId}' has no handler (forwarded to the UI)");
            Raise(new SessionEvent { Kind = SessionEventKind.SpecialOutcome, Id = specialId, Id2 = outcome?.value ?? "", Amount = outcome?.amount ?? 0 });
        }

        const string LanternsText = "The lanterns of Lanternvale are lit!";
        string pendingSpecialArg = "";

        bool IContentContext.GetFlag(string flag) => World.Flags.IsSet(flag);

        void IContentContext.SetFlag(string flag, bool value)
        {
            if (value) World.Flags.Set(flag, 1);
            else World.Flags.Clear(flag);
        }

        void IContentContext.RaiseEvent(string name, string arg)
        {
            Raise(new SessionEvent
            {
                Kind = SessionEventKind.SpecialOutcome, Id = name ?? "", Id2 = string.IsNullOrEmpty(arg) ? pendingSpecialArg : arg, Amount = 1,
                Text = name == RekindleLanternsSpecial ? LanternsText : "",
            });
        }

        /// <summary>Special outcome ids handled by the session (the data validator may treat these as implemented).</summary>
        public static readonly string[] SessionSpecials = { RekindleLanternsSpecial };
    }
}
