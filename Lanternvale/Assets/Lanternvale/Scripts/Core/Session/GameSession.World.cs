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
            if (PendingLoot != null) CloseLoot(true);
            CloseVendor();
            CloseTrainer();
            CloseRespec();
            SetMap(rt);
            if (string.IsNullOrEmpty(spawnId)) spawnId = "default";
            var spawn = rt.SpawnPosition(spawnId);
            var facing = spawn.x < rt.Def.width * 0.5f ? Vec2.Right : new Vec2(-1, 0);
            PlacePartyAt(spawn, facing);
            transitionArmed = Map.TransitionAt(Leader.Position) == null;
            Map.OnEnterMap();
            RebuildField();
            Raise(new SessionEvent { Kind = SessionEventKind.MapEntered, Id = rt.Id, Id2 = spawnId, Text = rt.Def.name });
            if (LanternsRekindled)
                Raise(new SessionEvent { Kind = SessionEventKind.SpecialOutcome, Id = RekindleLanternsSpecial, Amount = 0 });
            CheckTimeOfDay();
            foreach (var r in Map.UpdatePartyPosition(Leader.Position)) RaiseRegion(r);
        }

        void SetMap(MapRuntime rt)
        {
            Map = rt;
            MapId = rt.Id;
            Nav = new NavGrid(rt.Def);
            pathfinder = new NavGridPathfinder(Nav);
            fieldPathfinder = new NavGridPathfinder(Nav) { AcceptPartial = true, TrackUnits = false };
            suppressedEncounters.Clear();
        }

        /// <summary>Rebuilds the exploration context with the current party units (no-op during combat).</summary>
        void RebuildField()
        {
            if (Battle != null) return;
            if (Field != null)
            {
                Field.EventRaised -= ForwardCombatEvent;
                Field.UnitAdded -= OnFieldUnitAdded;
                Field.UnitRemoved -= OnFieldUnitRemoved;
            }
            if (!hasGame || roster.Count == 0) { Field = null; return; }
            IPathfinder pf = fieldPathfinder != null ? (IPathfinder)fieldPathfinder : new StraightLinePathfinder { UnitsBlock = false };
            var f = new Battle(Db, Rng, pf, Inventory, false) { RecordEvents = false };
            foreach (var u in PartyUnits()) f.AddUnit(u);
            f.EventRaised += ForwardCombatEvent;
            f.UnitAdded += OnFieldUnitAdded;
            f.UnitRemoved += OnFieldUnitRemoved;
            Field = f;
            f.RefreshAreaAuras();
        }

        /// <summary>Spirit lanterns are lit (flag lanterns_rekindled).</summary>
        public bool LanternsRekindled => World.Flags.IsSet(LanternsFlag);

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
            return CheckTriggers();
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
                foreach (var u in party)
                {
                    if (!u.IsAlive) continue;
                    float r = u.IsStealthed ? Math.Min(e.radius, MapRuntime.StealthDetectRadius) : e.radius;
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
            var drop = LootGenerator.Roll(Db, c.lootTable, PartyLevel, Rng);
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
            if (t == null) return InteractResult.Fail("There is no way through here.");
            if (!Map.IsTransitionUnlocked(t))
            {
                var text = string.IsNullOrEmpty(t.lockedText) ? "The way is blocked." : t.lockedText;
                Raise(new SessionEvent { Kind = SessionEventKind.TransitionLocked, Id = t.id, Text = text });
                return new InteractResult { Ok = false, Kind = InteractKind.Locked, Id = t.id, Message = text };
            }
            EnterMap(t.targetMap, t.targetSpawn);
            return new InteractResult { Ok = true, Kind = InteractKind.Travel, Id = t.targetMap };
        }

        /// <summary>Text of a prop with an interact id (signs, shrines). Also raised as a Toast.</summary>
        public string InspectProp(string interactId)
        {
            if (Map?.Def == null || string.IsNullOrEmpty(interactId)) return "";
            foreach (var list in new[] { Map.Def.props, Map.Def.foreground })
            {
                if (list == null) continue;
                foreach (var p in list)
                    if (p != null && p.interact == interactId)
                    {
                        Toast(p.text);
                        return p.text ?? "";
                    }
            }
            return "";
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
            return Dialogue.Start(dialogueId, ownerId ?? "");
        }

        public bool ChooseDialogue(int index) => Dialogue.Choose(index);
        public bool ContinueDialogue() => Dialogue.Continue();
        public void EndDialogue() { if (Dialogue.IsActive) Dialogue.End(); }

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

        /// <summary>Data `Special` outcomes. RekindleLanterns: flag lanterns_rekindled + SpecialOutcome event (Amount 1 = animate).</summary>
        public void RunSpecial(string specialId, OutcomeDef outcome)
        {
            if (resetting || string.IsNullOrEmpty(specialId)) return;
            if (specialId == RekindleLanternsSpecial)
            {
                World.Flags.Set(LanternsFlag, 1);
                Raise(new SessionEvent { Kind = SessionEventKind.SpecialOutcome, Id = specialId, Amount = 1, Text = "The lanterns of Lanternvale are lit!" });
                return;
            }
            Log.Warn($"GameSession: special outcome '{specialId}' has no session handler (forwarded to the UI)");
            Raise(new SessionEvent { Kind = SessionEventKind.SpecialOutcome, Id = specialId, Id2 = outcome?.value ?? "", Amount = outcome?.amount ?? 0 });
        }

        /// <summary>Special outcome ids handled by the session (the data validator may treat these as implemented).</summary>
        public static readonly string[] SessionSpecials = { RekindleLanternsSpecial };
    }
}
