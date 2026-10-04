# Lanternvale — World Core API (`Lanternvale.World`)

Pure C# 9 (no UnityEngine), in `Assets/Lanternvale/Scripts/Core/World/**`. Used by the rules engine /
`GameSession` (Core/Rules) and by the Unity presentation layer (Scripts/Game).

> Status: **API contract — implementation in progress.** Signatures are stable; small additions may appear.

Coordinates: world metres. Map ground is `x ∈ [0, width]`, `y ∈ [0, depth]`; `y = 0` is nearest the camera
(bottom of the screen), larger `y` is further back. `Vec2` is `Lanternvale.Util.Vec2`. Unit ids for
navigation occupancy are `int` (the rules engine's unit ids).

| Area | Main types |
|---|---|
| Navigation | `NavGrid`, `NavGridOptions`, `NavAgent`, `NavPath`, `PathStatus`, `ReachMap` |
| Flags | `FlagStore` |
| Dialogue | `IDialogueContext`, `PartyMemberInfo`, `DialogueRunner`, `DialogueView`, `ChoiceView`, `CheckResult`, `CheckPreview`, `SkillChecks`, `DialogueMemory`, `WorldRules` |
| Quests | `QuestLog`, `QuestStatus`, `QuestEvent`, `QuestEventKind`, `QuestJournalEntry`, `ObjectiveView` |
| Map state | `MapRuntime` |
| Aggregate + saves | `WorldState`, `WorldSaveData` (+ `FlagStoreState`, `QuestLogState`, `DialogueMemoryState`, `MapRuntimeState`) |

---

## 1. Typical wiring (GameSession)

```csharp
using Lanternvale.World;

public sealed class GameSession : IDialogueContext
{
    public readonly WorldState World;          // flags + quest log + dialogue memory + per-map runtimes
    public readonly DialogueRunner Dialogue;
    public NavGrid Nav;                        // rebuilt when the map changes
    public MapRuntime Map;                     // runtime state of the current map

    public GameSession(GameDatabase db, Rng rng)
    {
        World = new WorldState(db, this);      // ctx may also be assigned later: World.Context = this
        Dialogue = new DialogueRunner(db, this, World.DialogueMemory, rng);
    }

    void EnterMap(string mapId) {
        Map = World.GetMap(mapId);             // cached; state persists in saves
        Nav = new NavGrid(Map.Def);            // static obstacles from props/chests/walkable polygon
    }

    // IDialogueContext: flags/quests come from the WorldState
    FlagStore IDialogueContext.Flags => World.Flags;
    QuestLog  IDialogueContext.Quests => World.Quests;
    // ... party / inventory / action members (see §4)
}
```

Save: `WorldSaveData data = World.Save();` → `JsonWriter.Serialize(data)`.
Load: `World.Load(JsonMapper.FromJson<WorldSaveData>(json));`

---

## 2. Navigation — `NavGrid`

Built from a `MapDef` (or an empty rectangle for tests). Cells are `CellSize` metres (default 0.5).
A cell is statically **walkable** when its centre is inside the ground rect shrunk by `EdgeMargin`, inside the
optional `walkable` polygon, and outside every prop collider (ellipse `w×h` × `prop.scale`, centred at
`pos + offset × scale`, offset.x mirrored when `flip`) and chest footprint. The cell containing a collider's
centre is always blocked (so tiny colliders still block). Foreground props are ignored.

Agents have a **radius**: a precomputed clearance field (exact Euclidean distance transform) makes a cell usable
by an agent of radius `r` when `clearance(cell) ≥ r`. Dynamic **unit circles** (other combatants) are avoided
by every query unless ignored.

```csharp
var nav = new NavGrid(mapDef);                               // or new NavGrid(mapDef, new NavGridOptions { CellSize = 0.5f })
var nav = new NavGrid(width: 40, depth: 20);                 // empty field (tests, arenas)
nav.AddObstacleEllipse(center, w, h);                        // extra static obstacles, then call nav.Rebuild()
nav.AddObstacleRect(min, max); nav.Rebuild();

// dynamic occupancy (combat): id, position, radius
nav.SetUnit(id, pos, radius);   nav.RemoveUnit(id);   nav.ClearUnits();
bool nav.TryGetUnit(id, out Vec2 pos, out float radius);

// agent description (struct, cheap to copy)
var agent = new NavAgent(radius: 0.35f, selfId: unitId);     // selfId is always ignored
agent = agent.Ignoring(targetId);                            // up to any number of extra ids (IReadOnlyCollection<int> Ignore)
agent.IgnoreUnits = true;                                    // exploration: ignore all dynamic units
NavAgent.Default                                             // radius 0.35, no self, units respected
```

### Path queries

```csharp
NavPath p = nav.FindPath(start, goal, agent, maxLength: float.PositiveInfinity, result: reusedPathOrNull);
NavPath p = nav.FindPathToRange(start, target, range, agent, maxLength, requireLineOfSight: false, result: null);
p.Status        // PathStatus.Complete | Partial | NoPath
p.Points        // List<Vec2>: Points[0] == start; smoothed (string-pulled) polyline
p.Length        // metres along Points (after truncation)
p.FullLength    // metres before truncation (UI: "12.4 m / 9 m")
p.Truncated     // true when cut to maxLength
p.End           // last point
p.ReachedGoal   // Status == Complete && !Truncated
NavPath.Truncate(p, maxLength)                    // in-place cut of any path
Vec2 NavPath.PointAt(p, distance)                 // point along the polyline
```

* A* (8-directional, no corner cutting, octile heuristic, binary heap, reused buffers) then line-of-walk
  smoothing. Typical 400×120-cell queries take well under a few ms with zero big allocations.
* **Blocked/unreachable goal**: path to the closest reachable point (`Status = Partial`). A blocked goal is
  first snapped to the nearest walkable cell.
* **Range** (`FindPathToRange`): stops as soon as the agent is within `range` of `target` (minus
  `NavGrid.RangeEpsilon` = 0.02 m so later `distance <= range` checks succeed). Use it for melee approach
  (`range = meleeReachMetres`, ignore the target id or not — the target's own circle is usually in the way, so
  ranged approach works either way) and spell range (`requireLineOfSight: true` to also need LOS).
  Already in range ⇒ `Points = [start]`, `Length = 0`, `Complete`.
* `maxLength` truncates (combat movement budget); `Truncated = true`, `FullLength` keeps the untruncated length.
* Start cell inside an obstacle/unit: the search starts from the nearest usable cell.

### Movement range — `ReachMap`

```csharp
ReachMap reach = nav.ReachableWithin(start, maxMetres, agent, into: reusedOrNull);
bool  reach.CanReach(point)            // reachable within maxMetres
float reach.DistanceTo(point)          // path metres (Any-angle Dijkstra / Theta*-style), +inf when unreachable
bool  reach.IsCellReachable(cx, cy); float reach.CellDistance(cx, cy)
void  reach.GetReachableCells(List<Vec2> centresInto)
void  reach.GetBorderCells(List<Vec2> centresInto)      // reachable cells with an unreachable 4-neighbour
void  reach.GetOutline(List<Vec2> segmentPairsInto)     // pairs (a,b) of cell-edge segments around the area
NavPath reach.PathTo(point, NavPath result = null)       // path from the flood tree (no new search)
reach.Start, reach.MaxMetres, reach.CellSize
```

Distances are any-angle (near-Euclidean in open ground), so the outline looks round, not octagonal.
`CanReach(p) ⇒ FindPath(start, p).Length ≤ maxMetres` (the A* smoothed path is never longer).

### Point queries

```csharp
bool nav.IsWalkable(point, agent)                       // static clearance + units (exact circle test)
bool nav.IsWalkable(point, radius)                      // static only
bool nav.LineWalkable(a, b, agent)                      // straight walk a→b is clear
bool nav.HasLineOfSight(a, b)                           // blocked only by big props (see below)
Vec2 nav.ClampToWalkable(point, agent)                  // point if walkable, else nearest walkable cell centre
bool nav.RandomWalkablePointNear(center, maxDist, rng, agent, out Vec2 p)        // wander
bool nav.FleePoint(from, awayFrom, distance, rng, agent, out Vec2 p)             // fear
int  nav.FindStandingSpots(center, count, spacing, agent, List<Vec2> into)       // formation/party placement
// cells
int nav.Width, nav.Height (cells); float nav.CellSize, nav.WorldWidth, nav.WorldDepth
bool nav.WorldToCell(p, out cx, out cy); Vec2 nav.CellCenter(cx, cy)
bool nav.IsCellWalkable(cx, cy, radius = 0); float nav.Clearance(cx, cy)
```

**Line of sight**: props whose collider's larger axis × scale ≥ `NavGridOptions.LosBlockerMinSize`
(default 2.5 m: houses, big rocks, large trees) block sight (segment vs ellipse shrunk to 80%). Add more with
`nav.AddLosBlocker(center, w, h)`. Everything else is see-through. Set `NavGridOptions.LineOfSight = false`
to make `HasLineOfSight` always true.

`NavGridOptions`: `CellSize = 0.5`, `EdgeMargin = 0.25`, `ChestFootprint = (1.0, 0.6)`,
`IncludeChests = true`, `LineOfSight = true`, `LosBlockerMinSize = 2.5`, `MaxSearchNodes = 200000`.

---

## 3. Flags — `FlagStore`

```csharp
int  Get(string key)               // 0 when unset
bool IsSet(string key)             // Get != 0
void Set(string key, int value=1)  // value 0 clears
void Add(string key, int delta)    // counters
void Clear(string key)
bool Test(string expr)             // "" → true, "flag" → IsSet, "!flag" → !IsSet  (used by requireFlag/hideFlag)
event Action<string,int,int> Changed   // (key, oldValue, newValue)
IEnumerable<KeyValuePair<string,int>> All; int Count; void ClearAll()
FlagStoreState Save(); void Load(FlagStoreState)
```

Conventions written by the world module: `enc_<encounterId>` (encounter defeated, default doneFlag),
`recruited_<companionId>` (set by the `Recruit` outcome), region `enterFlag`s.

---

## 4. Dialogue

### `IDialogueContext` (implemented by GameSession)

```csharp
public interface IDialogueContext
{
    FlagStore Flags { get; }
    QuestLog Quests { get; }

    string PlayerName { get; }
    IReadOnlyList<PartyMemberInfo> Party { get; }   // active party, index 0 = main character
    int Gold { get; }                               // copper
    int CountItem(string itemId);
    int GetApproval(string companionId);
    string TimeOfDay { get; }                       // "dawn" | "day" | "dusk" | "night"
    int SkillCheckBonus(string memberId, SkillCheck skill);   // extra bonus from items/buffs (usually 0)

    void GiveItem(string itemId, int count);
    void TakeItem(string itemId, int count);
    void GiveGold(int copper);
    void TakeGold(int copper);
    void GiveXP(int amount);                        // raw amount from data (apply xpRate yourself if desired)
    void Recruit(string companionId);
    void Dismiss(string companionId);
    void ChangeApproval(string companionId, int delta);
    void StartCombat(string encounterId);           // "" = the dialogue's encounter / owner
    void OpenVendor(string npcId);
    void OpenTrainer(string npcId);
    void OpenRespec(string npcId);
    void Rest();
    void HealParty();
    void Teleport(string mapId, string spawnId);
    void RunSpecial(string specialId, OutcomeDef outcome);
}

public sealed class PartyMemberInfo
{
    public string id = "";            // "player" for the main character (any id works), else companion id
    public string name = "";
    public ClassId classId;
    public int level = 1;
    public bool isMain;
    public PrimaryStats stats = new PrimaryStats();   // current effective primary stats (gear + buffs)
}
```

### Conditions (`ConditionDef`) — `WorldRules.Check(cond, ctx)` / `WorldRules.CheckAll(list, ctx)`

| type | meaning |
|---|---|
| `Flag` | flag `key` set (≠0); with `amount > 0`: value ≥ amount |
| `NotFlag` | negation of `Flag` |
| `QuestState` | quest `key`: `value` is a status name (`NotStarted`/`Active`/`Completed`/`Failed`) → status equals; otherwise `value` is a stage id → quest active and on that stage |
| `QuestNotStarted` / `QuestActive` / `QuestComplete` | status test (`QuestActive` with `value` = stage id also requires that stage) |
| `HasItem` / `NotHasItem` | `CountItem(key) ≥ max(1, amount)` / negation |
| `Gold` | `Gold ≥ amount` (copper) |
| `Class` / `NotClass` | main character's class is `key` (`value: "party"` → anyone in the party) |
| `Level` | main character level ≥ `amount` |
| `InParty` / `NotInParty` | companion `key` is (not) in `Party` |
| `Companion` | approval of companion `key` ≥ `amount` |
| `TimeOfDay` | `TimeOfDay` equals `key` (or `value`), case-insensitive |

### Outcomes (`OutcomeDef`) — `WorldRules.Execute(outcome, ctx, ownerId)`

| type | effect |
|---|---|
| `SetFlag` | `Flags.Set(key, amount != 0 ? amount : int(value) or 1)` |
| `ClearFlag` | `Flags.Clear(key)` |
| `StartQuest` / `SetQuestStage` (`value` = stage) / `CompleteQuest` / `FailQuest` | `Quests.*` |
| `GiveItem` / `TakeItem` | `key` item × `max(1, amount)` |
| `GiveGold` / `TakeGold` | `amount` copper |
| `GiveXP` | `amount` |
| `Recruit` / `Dismiss` | companion `key` (`Recruit` also sets flag `recruited_<key>`) |
| `Approval` | companion `key`, delta `amount` |
| `StartCombat` | encounter `key` |
| `OpenVendor` / `OpenTrainer` / `OpenRespec` | npc `key` ("" = dialogue owner npc) |
| `Rest`, `HealParty` | — |
| `Teleport` | map `key`, spawn `value` (default `"default"`) |
| `EndDialogue` | ends the conversation (after the node's text when used on a node) |
| `Special` | `ctx.RunSpecial(key, outcome)` |

In a `DialogueRunner`, the *interrupting* outcomes `StartCombat`, `Teleport`, `OpenVendor`, `OpenTrainer`,
`OpenRespec`, `Rest` are **deferred until the dialogue ends** (set `runner.DeferInterruptingOutcomes = false`
to run them immediately). Everything else runs immediately.

### `DialogueRunner`

```csharp
var runner = new DialogueRunner(db, ctx, memory /*DialogueMemory or null*/, rng);
bool  runner.Start(string dialogueId, string ownerId = "")   // ownerId: npc/companion talked to
bool  runner.IsActive; bool runner.IsFinished
DialogueView runner.Current          // null when not active
bool  runner.Choose(int index)       // index into Current.Choices (visible choices only)
bool  runner.Continue()              // node without choices: go to `next` (or end)
void  runner.End()                   // abort/close
CheckResult runner.LastCheck         // set by a check choice (for dice animation), cleared on next Choose
events: NodeEntered(DialogueView), CheckRolled(CheckResult), ChoiceMade(ChoiceView), Ended(string dialogueId)
string runner.Substitute(string text)    // {player} {class} {companion:<id>}
```

`DialogueView`: `DialogueId, NodeId, SpeakerId, SpeakerName, Portrait, Text, Choices (List<ChoiceView>),
CanContinue (no choices), IsLast (Continue will end)`.
`ChoiceView`: `Index` (in visible list), `SourceIndex` (in node.choices), `Text` (substituted, no tag),
`DisplayText` (`"[PALADIN] text"`; checks without tag show `"[PERSUASION] text"`), `Tag`, `Once`,
`PreviouslyChosen` (grey out), `Check` (`CheckPreview` or null: skill, dc, best roller, modifier,
successChance 0..1), `Def` (the ChoiceDef).

Behaviour: entering a node checks `conditions` (fail → `fallback`, chained; no fallback → dialogue ends),
runs `outcomes`, filters choices by `conditions`, hides used `once` choices. Nodes with empty text and no
visible choices are auto-skipped (logic/router nodes). Choosing runs the choice's outcomes, then rolls its
`check` (→ `success`/`failure` node) or goes to `next` (`""` ends). When the dialogue ends with an owner, the
runner calls `ctx.Quests.OnTalk(ownerId)`. Speakers: `"player"` → `ctx.PlayerName`, `"narrator"` → "",
npc/companion ids → names from the database, `""` → the owner.

### Skill checks — `SkillChecks`

`d20 + modifier ≥ dc`; modifier = `floor((stat − 20) / 10)` + 2 if the member's class is proficient (Design §6)
+ `ctx.SkillCheckBonus`. The best party member rolls (ties → earlier in `Party`). Natural 20 always succeeds,
natural 1 always fails.

```csharp
CheckResult SkillChecks.Roll(IDialogueContext ctx, SkillCheck skill, int dc, Rng rng)
CheckPreview SkillChecks.Preview(IDialogueContext ctx, SkillCheck skill, int dc)
int  SkillChecks.Modifier(PartyMemberInfo m, SkillCheck skill)       // without ctx bonus
bool SkillChecks.IsProficient(ClassId c, SkillCheck skill)
StatKind SkillChecks.StatFor(SkillCheck skill)                      // Strength/Agility/Stamina/Intellect/Spirit
float SkillChecks.SuccessChance(int modifier, int dc)
```

`CheckResult`: `Skill, Dc, RollerId, RollerName, Roll (natural d20), StatModifier, Proficiency, Bonus,
Modifier (total), Total, Success, Critical (nat 20), Fumble (nat 1)`.

### `DialogueMemory`

Remembers chosen choices (`once` hiding + "previously chosen" greying) and started dialogues.
`HasChosen(dialogueId, nodeId, choiceIndex)`, `MarkChosen(...)`, `TimesStarted(dialogueId)`,
`Save() → DialogueMemoryState`, `Load(state)`.

---

## 5. Quests — `QuestLog`

```csharp
var quests = new QuestLog(db, ctx);           // ctx may be null and set later: quests.Context = ctx
QuestStatus GetStatus(id); string GetStage(id); int GetProgress(id, objectiveIndex)
bool IsActive(id); bool IsCompleted(id)
bool Start(id); bool SetStage(id, stageId); bool Complete(id); bool Fail(id)
// notifications (call from the session / rules engine)
void OnKill(string creatureId, int count = 1)
void OnItemCount(string itemId, int count)    // absolute inventory count
void OnTalk(string npcId)                     // DialogueRunner calls this when a dialogue with an owner ends
void OnFlag(string flag)                      // WorldState wires FlagStore.Changed → OnFlag automatically
void OnReach(string regionOrMapId)            // MapRuntime.UpdatePartyPosition calls this
void OnDefeat(string encounterId)             // MapRuntime.MarkEncounterDone calls this
void Refresh()                                // re-pull Flag/Collect/Defeat objectives from ctx
// rewards
IReadOnlyList<string> PendingRewardChoices    // quest ids whose choiceItems await a pick
bool ClaimRewardChoice(string questId, string itemId)
// journal
List<QuestJournalEntry> GetJournal(bool includeFinished = true)   // active first (main first), then completed, failed
QuestJournalEntry GetEntry(string questId)
event Action<QuestEvent> Changed
QuestLogState Save(); void Load(QuestLogState)
```

Rules:
* A stage completes when all its objectives are complete (stages with no objectives wait for an outcome:
  `SetQuestStage` / `CompleteQuest`). Leaving a stage forward (objectives done, `SetQuestStage`,
  `CompleteQuest`) runs its `onComplete` outcomes once. Then `next` stage (or quest completion when `next` is "").
* Objectives: `Kill` (target creature id, counts kills while the stage is active), `Collect` (item id; progress =
  current inventory count, pulled from `ctx.CountItem` on stage start), `Talk` (npc id), `Reach` (region id or
  map id; completes while the party is inside during the stage), `Flag` (progress = flag value, so counters
  work: count 3 ⇒ flag value 3), `Defeat` (encounter id; also satisfied when flag `enc_<id>` is set).
* Completion grants `rewards` via ctx: `GiveXP(xp)`, `GiveGold(gold)`, `GiveItem(item,1)` for each item; a
  non-empty `choiceItems` adds the quest to `PendingRewardChoices` (UI picks one → `ClaimRewardChoice`).
* `QuestEvent`: `Kind` (`Started, ObjectiveProgress, ObjectiveCompleted, StageAdvanced, Completed, Failed,
  RewardChoicePending`), `QuestId, QuestName, StageId, ObjectiveIndex, Progress, Count, Text` (toast-ready,
  e.g. "Wolves slain 3/6").
* `QuestJournalEntry`: `Id, Title, Summary, Giver, Level, Main, Status, StageId, StageText,
  Objectives (List<ObjectiveView>), History (texts of completed stages), Rewards`.
  `ObjectiveView`: `Text, Progress, Count, Complete, Display` ("Wolves slain 3/6"; count 1 → just the text).

---

## 6. Map runtime — `MapRuntime`

Engine-independent bookkeeping for one map (from `WorldState.GetMap(mapId)`).

```csharp
MapDef Def
// encounters
string DoneFlag(EncounterDef e)                  // e.doneFlag or "enc_<id>"
bool IsEncounterDone(string id); bool IsEncounterAvailable(EncounterDef e)   // !done && requireFlag
IEnumerable<EncounterDef> AvailableEncounters()  // enemies to show (skip e.hidden until triggered)
EncounterDef FindTriggeredEncounter(IReadOnlyList<Vec2> partyPositions, IReadOnlyList<bool> stealthed = null)
   // first available encounter with a party member within radius (stealthed members: within min(radius, 3 m))
void MarkEncounterDone(string id)                // sets done flag + Quests.OnDefeat(id)
// npcs
bool IsNpcVisible(MapNpcDef n)                   // Flags.Test(requireFlag) && !(hideFlag != "" && Flags.Test(hideFlag))
IEnumerable<MapNpcDef> VisibleNpcs()
// chests
bool IsChestAvailable(ChestDef c)                // requireFlag
bool IsChestOpened(string id); void MarkChestOpened(string id)
bool IsChestLocked(ChestDef c)                   // lockCheck != null && not unlocked
CheckResult TryUnlockChest(ChestDef c, IDialogueContext ctx, Rng rng)   // SleightOfHand (lockCheck.skill) roll
// transitions & regions (pos = centre, size = full extents)
bool IsTransitionUnlocked(TransitionDef t)       // Flags.Test(requireFlag)
TransitionDef TransitionAt(Vec2 p)
List<RegionDef> UpdatePartyPosition(Vec2 leaderPos)   // returns regions entered for the FIRST time (toasts)
bool HasEnteredRegion(string id); IEnumerable<RegionDef> RegionsAt(Vec2 p)
MapRuntimeState State
```

---

## 7. Save data

All DTOs are plain classes with public fields (JsonWriter/JsonMapper compatible):

```csharp
public sealed class WorldSaveData { FlagStoreState flags; QuestLogState quests; DialogueMemoryState dialogue; List<MapRuntimeState> maps; }
public sealed class FlagStoreState { Dictionary<string,int> flags; }
public sealed class QuestLogState { List<QuestRecordState> quests; List<string> pendingRewardChoices; }
public sealed class QuestRecordState { string id; QuestStatus status; string stage; int[] progress; List<string> history; int order; }
public sealed class DialogueMemoryState { List<string> chosen; Dictionary<string,int> started; }
public sealed class MapRuntimeState { string mapId; List<string> openedChests; List<string> unlockedChests; List<string> enteredRegions; List<string> triggeredEncounters; }
```
