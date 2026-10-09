# Lanternvale — World Core API (`Lanternvale.World`)

Pure C# 9 (no UnityEngine), in `Assets/Lanternvale/Scripts/Core/World/**`. Used by the rules engine /
`GameSession` (Core/Rules) and by the Unity presentation layer (Scripts/Game). Tests:
`Tools/harness/CoreTests/TestsWorld*.cs` (`Tools/check.sh core --filter World`).

Coordinates: world metres. Map ground is `x ∈ [0, width]`, `y ∈ [0, depth]`; `y = 0` is nearest the camera
(bottom of the screen), larger `y` is further back. `Vec2` is `Lanternvale.Util.Vec2`. Unit ids for navigation
occupancy are `int` (`Rules.Unit.Id`, which starts at 1). Rectangles in map data (transitions, regions) are
**centre `pos` + full `size`**.

| Area | Files | Main types |
|---|---|---|
| Navigation | `Nav/NavGrid*.cs`, `Nav/ReachMap.cs`, `Nav/NavTypes.cs` | `NavGrid`, `NavGridOptions`, `NavAgent`, `NavPath`, `PathStatus`, `ReachMap` |
| Flags | `Flags/FlagStore.cs` | `FlagStore`, `FlagStoreState` |
| Dialogue | `Dialogue/*.cs` | `IDialogueContext`, `PartyMemberInfo`, `DialogueRunner`, `DialogueView`, `ChoiceView`, `SkillChecks`, `CheckResult`, `CheckPreview`, `StatKind`, `DialogueMemory`, `WorldRules` |
| Quests | `Quests/QuestLog.cs` | `QuestLog`, `QuestStatus`, `QuestEvent`, `QuestEventKind`, `QuestJournalEntry`, `ObjectiveView` |
| Map state | `Map/MapRuntime.cs` | `MapRuntime`, `MapRuntimeState` |
| Aggregate + saves | `WorldState.cs` | `WorldState`, `WorldSaveData` |

---

## 1. Typical wiring (GameSession)

A simplified sketch of how a host wires the world module (`ShowToast`, `Travel`, `StartBattle` stand for host code).
The real host is `Core/Session/GameSession*.cs` — see `SessionAPI.md`.

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
        World = new WorldState(db, this);      // wires FlagStore.Changed → QuestLog.OnFlag
        Dialogue = World.CreateDialogueRunner(rng);   // = new DialogueRunner(db, this, World.DialogueMemory, rng)
        World.Quests.Changed += e => { /* toast: e.Kind, e.QuestName, e.Text */ };
    }

    void EnterMap(string mapId, string spawnId)
    {
        Map = World.GetMap(mapId);             // cached; state persists in saves
        Nav = new NavGrid(Map.Def);            // static obstacles from props/chests/walkable polygon
        var spawn = Map.SpawnPosition(spawnId);
        var spots = new List<Vec2>();
        Nav.FindStandingSpots(spawn, partyCount, 1.2f, NavAgent.Default, spots);
        Map.OnEnterMap();                      // Reach objectives for the map id
    }

    void OnLeaderMoved(Vec2 p)
    {
        foreach (var r in Map.UpdatePartyPosition(p)) ShowToast(r.text);   // first entries only
        var t = Map.TransitionAt(p);
        if (t != null) { if (Map.IsTransitionUnlocked(t)) Travel(t.targetMap, t.targetSpawn); else ShowToast(t.lockedText); }
        var enc = Map.FindTriggeredEncounter(partyPositions, partyStealthed);
        if (enc != null) { Map.MarkEncounterTriggered(enc.id); StartBattle(enc); }
    }

    void OnBattleWon(EncounterDef enc) { Map.MarkEncounterDone(enc.id); /* + Quests.OnKill per creature killed */ }

    // IDialogueContext: flags/quests come from the WorldState
    FlagStore IDialogueContext.Flags => World.Flags;
    QuestLog  IDialogueContext.Quests => World.Quests;
    // ... party / inventory / action members (see §4)
}
```

Inventory changes: call `World.Quests.OnItemCount(itemId, newCount)` (or `World.Quests.Refresh()` after bulk
changes / loading). Kills: `World.Quests.OnKill(creatureId)`.

Save: `JsonWriter.Serialize(World.Save())`. Load: `World.Load(JsonMapper.FromJson<WorldSaveData>(json))`
(existing `MapRuntime` objects are updated in place; call `Quests.Refresh()` afterwards if inventory changed).

---

## 2. Navigation — `NavGrid`

```csharp
var nav = new NavGrid(mapDef);                               // or new NavGrid(mapDef, new NavGridOptions { CellSize = 0.5f })
var nav = new NavGrid(width: 40, depth: 20);                 // empty field (tests, arenas)
nav.AddObstacleEllipse(center, w, h); nav.AddObstacleRect(min, max); nav.AddLosBlocker(center, w, h);
nav.SetWalkablePolygon(points); nav.ClearObstacles();
nav.Rebuild();                                               // after adding static obstacles (Version++)
```

**Static walkability** (built once): a cell is walkable when its centre is inside the ground rect shrunk by
`EdgeMargin`, inside the optional `walkable` polygon, not under blocking water, and outside every prop collider and
chest footprint.
Prop colliders: ellipse `w × h` (full extents) × `prop.scale`, centred at `pos + offset × scale`, `offset.x`
mirrored when `flip`. A missing axis defaults to `h = w/2` (or `w = 2h`). The cell containing a collider's centre is
always blocked (tiny colliders still block). `foreground` props are ignored. NPCs are *not* static obstacles:
register them as units if they should block.

**Water** (`MapDef.water`, only entries with `blocksMovement`): cells whose centre lies within `halfWidth` of an open
polyline, or inside a `closed` polygon's **shore as the terrain draws it** (`Spline.PondShore`: a closed Catmull-Rom loop
through the corners, sampled every 0.5 m, which bulges a little past convex corners), are blocked — except inside that
water's `crossings` rects (fords, stepping stones, bridges). Water with `blocksMovement: false` is only drawn.

**Flag-gated props and chests.** The plain `NavGrid(mapDef)` blocks every prop and chest, whatever its flags. The
session builds `new NavGrid(mapDef, null, flags.Test)`: a prop whose `requireFlag` fails or whose `hideFlag` holds, and a
chest whose `requireFlag` fails, is not there. After flags change, `RefreshFlags()` re-tests them and rebuilds the
**same instance** in place (`Version++`) only when one appeared or vanished (the map panel repaints on `Version`).

```csharp
var nav = new NavGrid(mapDef, options, flagTest);            // flagTest: Func<string, bool> (FlagStore.Test); null = all block
bool nav.RefreshFlags()                                      // true = rebuilt (Version++); false = nothing changed
bool nav.IsPropPresent(PropDef p); bool nav.IsChestPresent(ChestDef c)   // under FlagTest
bool nav.IsWater(Vec2 p); bool nav.IsCellWater(cx, cy)      // blocking water (crossings excluded)
// ClearObstacles() also drops the map's props, chests and water; RefreshFlags is a no-op afterwards
```

**Agent radius**: an exact Euclidean distance transform gives every cell a `Clearance` (metres from its centre
to the nearest blocked cell edge or the ground edge). A cell is usable by an agent of radius `r` when
`clearance ≥ r`, so one grid serves all unit sizes and agents keep their body inside the ground.

**Dynamic units** (combat occupancy), avoided by every query unless ignored:

```csharp
nav.SetUnit(id, pos, radius);     // register or move; radius <= 0 removes (dead units)
nav.RemoveUnit(id); nav.ClearUnits(); nav.UnitCount; nav.GetUnitIds(list)
bool nav.TryGetUnit(id, out Vec2 pos, out float radius)
int  nav.UnitAt(point, agent)      // first non-ignored unit overlapping the agent at point, or NavAgent.NoId
bool nav.IsOccupied(point, agent)
```

**`NavAgent`** (struct): `Radius`, `SelfId` (always ignored), `IgnoreId` (one extra id, allocation-free),
`Ignore` (`ICollection<int>`: int[], List, HashSet), `IgnoreUnits` (ignore all units — exploration).

```csharp
NavAgent.Default                                  // radius 0.35, no self, respects units
NavAgent.ForUnit(radius, selfId, ignoreId = NoId) // allocation-free; negative ignoreId = none
new NavAgent(radius, selfId, ignoreList, ignoreUnits)
agent.Ignoring(id1, id2) / agent.IgnoringAllUnits()   // copies
NavAgent.NoId == int.MinValue; NavAgent.DefaultRadius == 0.35f
```

Units touching exactly (`distance == r1 + r2`) do not block each other (`NavGrid.UnitEpsilon` = 0.01 m slack).

### Path queries

```csharp
NavPath p = nav.FindPath(start, goal, agent, maxLength = +inf, result = null);
NavPath p = nav.FindPathToRange(start, target, range, agent, maxLength = +inf, requireLineOfSight = false, result = null);

p.Status        // PathStatus.Complete | Partial | NoPath
p.Points        // List<Vec2>: Points[0] == start; smoothed (string-pulled) polyline; last = goal when Complete
p.Length        // metres along Points (after truncation)
p.FullLength    // metres before truncation (UI: "12.4 m / 9 m")
p.Truncated     // cut to maxLength
p.End; p.Count; p.IsEmpty (nothing to walk); p.ReachedGoal (Complete && !Truncated)
p.Truncate(maxLength)          // in-place cut
p.PointAt(distance)            // point along the polyline
p.RecomputeLength(); p.CopyFrom(other); p.Clear()
```

* A* (8-directional, no corner cutting, octile heuristic, binary heap, all big buffers reused per grid) then
  line-of-walk smoothing (string pulling). Pass the same `NavPath` back as `result` for zero allocations.
* **Blocked/unreachable goal** → path to the closest reachable point, `Status = Partial`. Static
  connectivity labels (cached per radius) detect unreachable goals without flooding the map; near-equal
  candidates prefer the start's side. `nav.AreConnected(a, b, radius)` exposes the static test.
* **Range** (`FindPathToRange`): stops as soon as the agent is within `range` of `target` (minus
  `NavGrid.RangeEpsilon` = 0.02 m so later `distance <= range` checks succeed). Melee: `range = meleeReachMetres`
  (centre to centre); the target's own circle can stay registered (or ignore it via `IgnoreId`). Spells:
  `requireLineOfSight: true`. Already in range ⇒ `Points = [start]`, `Length = 0`, `Complete`.
* `maxLength` truncates (movement budget): `Truncated = true`, `FullLength` keeps the untruncated length.
* Start inside an obstacle/unit: the search starts from the nearest usable cell (`Points[0]` is still `start`).
* Performance (200×60 m map, 400×120 cells, 300 obstacles, 12 units; optimized JIT ≈ IL2CPP): cross-map path
  avg ≈ 0.9 ms (worst ≈ 3 ms), enclosed/unreachable goal ≈ 0.5 ms, 9 m reach flood ≈ 0.3 ms, build ≈ 3 ms.
  (Debug/tier-0 JIT in the harness is ~2× slower.)

### Movement range — `ReachMap`

```csharp
ReachMap reach = nav.ReachableWithin(start, maxMetres, agent, into: reusedOrNull);
bool  reach.CanReach(point)            // walkable for the agent and within maxMetres of walking
float reach.DistanceTo(point)          // walking metres (any-angle), +inf when unreachable
NavPath reach.PathTo(point, result)    // path from the flood tree (Complete; NoPath when unreachable)
bool  reach.IsCellReachable(cx, cy); float reach.CellDistance(cx, cy)
void  reach.GetReachableCells(List<Vec2> centres)
void  reach.GetBorderCells(List<Vec2> centres)    // reachable cells with an unreachable 4-neighbour
void  reach.GetOutline(List<Vec2> segmentPairs)   // (a0,b0,a1,b1,...) cell edges around the area, world metres
reach.Start, reach.MaxMetres, reach.CellSize, reach.ReachableCount, reach.Grid
```

Lazy Theta* flood: distances are any-angle (within a few cm of Euclidean in open ground), so the area is round,
not octagonal. **For combat moves inside the displayed range use `reach.PathTo(p)`** — its length equals
`DistanceTo(p)` and is consistent with what the UI showed (an A* path to the same point may differ slightly).

### Point queries & helpers

```csharp
bool nav.IsWalkable(point, agent)          // static clearance + exact unit circles
bool nav.IsWalkable(point, radius)         // static only
bool nav.LineWalkable(a, b, agent)         // straight walk a→b is clear (cells along the segment)
bool nav.LineWalkable(a, b, radius)        // static only
bool nav.HasLineOfSight(a, b)              // blocked only by big props (below)
Vec2 nav.ClampToWalkable(point, agent)     // point if walkable, else nearest walkable cell centre
Vec2 nav.ClampToBounds(point)
bool nav.RandomWalkablePointNear(center, maxDist, rng, agent, out Vec2 p)   // wander (straight-walkable)
bool nav.FleePoint(from, awayFrom, distance, rng, agent, out Vec2 p)        // fear: away, then ±30..120°, then shorter
int  nav.FindStandingSpots(center, count, spacing, agent, List<Vec2> into)  // nearest-first, reachable, spaced
// cells
nav.Width, nav.Height (cells); nav.CellSize, nav.WorldWidth, nav.WorldDepth, nav.Version, nav.Map, nav.Options
bool nav.WorldToCell(p, out cx, out cy); Vec2 nav.CellCenter(cx, cy); bool nav.InBounds(...)
bool nav.IsCellWalkable(cx, cy, radius = 0); bool nav.IsCellBlocked(cx, cy); float nav.Clearance(cx, cy)
int nav.LastExpandedNodes                   // diagnostics
```

**Line of sight**: props whose collider's larger axis × scale ≥ `NavGridOptions.LosBlockerMinSize` (default
2.5 m: houses, big rocks, large trees) block sight; tested as segment vs ellipse shrunk to 80% (units next to a
blocker can see past its edge). Add more with `AddLosBlocker`. `NavGridOptions.LineOfSight = false` makes
`HasLineOfSight` always true.

`NavGridOptions`: `CellSize = 0.5`, `EdgeMargin = 0.25`, `ChestFootprint = (1.0, 0.6)`, `IncludeChests = true`,
`LineOfSight = true`, `LosBlockerMinSize = 2.5`, `MaxSearchNodes = 200000`.

### Adapting to `Lanternvale.Rules.IPathfinder`

```csharp
public sealed class NavGridPathfinder : IPathfinder
{
    public readonly NavGrid Grid;
    readonly NavPath tmp = new NavPath();
    public NavGridPathfinder(NavGrid grid) { Grid = grid; }

    PathResult Convert(NavPath p)
    {
        // Found = Complete keeps ReachedGoal strict (range checks). Partial paths lead to the closest reachable
        // point; if a click on a blocked spot should still walk there (BG3-style), accept Partial for player moves.
        var r = new PathResult { Length = p.Length, FullLength = p.FullLength, Truncated = p.Truncated,
                                 Found = p.Status == PathStatus.Complete };
        r.Points.AddRange(p.Points);
        return r;
    }
    public PathResult FindPath(Vec2 from, Vec2 to, float radius, int selfId, int ignoreId, float maxLength) =>
        Convert(Grid.FindPath(from, to, NavAgent.ForUnit(radius, selfId, ignoreId), maxLength, tmp));
    public PathResult FindPathToRange(Vec2 from, Vec2 target, float range, float radius, int selfId, int ignoreId, float maxLength) =>
        Convert(Grid.FindPathToRange(from, target, range, NavAgent.ForUnit(radius, selfId, ignoreId), maxLength, false, tmp));
    public bool IsWalkable(Vec2 p, float radius, int selfId) => Grid.IsWalkable(p, NavAgent.ForUnit(radius, selfId));
    public bool HasLineOfSight(Vec2 a, Vec2 b) => Grid.HasLineOfSight(a, b);
    public Vec2 ClampToWalkable(Vec2 p, float radius, int selfId) => Grid.ClampToWalkable(p, NavAgent.ForUnit(radius, selfId));
    public void SetUnit(int id, Vec2 pos, float radius) => Grid.SetUnit(id, pos, radius);   // radius <= 0 removes
    public void ClearUnits() => Grid.ClearUnits();
}
```

---

## 3. Flags — `FlagStore`

```csharp
int  Get(string key)               // 0 when unset ("" / null keys are ignored)
bool IsSet(string key)             // Get != 0
void Set(string key, int value=1)  // value 0 clears
void Add(string key, int delta)    // counters ("lanterns_lit")
void Clear(string key); void ClearAll()
bool Test(string expr)             // "" → true, "flag" → IsSet, "!flag" → !IsSet, "a&!b" → all terms
event Action<string,int,int> Changed   // (key, oldValue, newValue)
IEnumerable<KeyValuePair<string,int>> All; int Count
FlagStoreState Save(); void Load(FlagStoreState)   // Load raises no events
```

`Test` is the evaluator for every `requireFlag` / `hideFlag` field (also usable as `MapView.Build(def, flags.Test)`).
Conventions written by the world module: `enc_<encounterId>` (encounter defeated — default `doneFlag`),
`recruited_<companionId>` (set by the `Recruit` outcome — use it as a `hideFlag` for the companion's map NPC),
region `enterFlag`s.

---

## 4. Dialogue

### `IDialogueContext` (implemented by GameSession)

```csharp
public interface IDialogueContext
{
    FlagStore Flags { get; }
    QuestLog Quests { get; }

    string PlayerName { get; }
    IReadOnlyList<PartyMemberInfo> Party { get; }   // active party, index 0 = main character; characters only
    int Gold { get; }                               // copper
    int CountItem(string itemId);
    int GetApproval(string companionId);
    string TimeOfDay { get; }                       // "dawn" | "day" | "dusk" | "night"
    int SkillCheckBonus(string memberId, SkillCheck skill);   // extra bonus from items/buffs (usually 0)

    void GiveItem(string itemId, int count);        // also call Quests.OnItemCount(itemId, newCount)
    void TakeItem(string itemId, int count);
    void GiveGold(int copper);
    void TakeGold(int copper);
    void GiveXP(int amount);                        // raw amount from data (apply config.xpRate here if desired)
    void Recruit(string companionId);
    void Dismiss(string companionId);
    void ChangeApproval(string companionId, int delta);
    void StartCombat(string encounterId);           // "" = the dialogue owner's encounter
    void OpenVendor(string npcId);
    void OpenTrainer(string npcId);
    void OpenRespec(string npcId);
    void Rest();
    void HealParty();
    void Teleport(string mapId, string spawnId);
    void RunSpecial(string specialId, OutcomeDef outcome);
}

public sealed class PartyMemberInfo   // [Serializable]
{
    public string id;        // "player" for the main character (any id works), else companion id
    public string name;
    public ClassId classId;
    public int level;
    public bool isMain;      // else Party[0] is treated as the main character
    public PrimaryStats stats;   // current effective primary stats (gear + buffs)
    public PartyMemberInfo(string id, string name, ClassId classId, int level, bool isMain, PrimaryStats stats = null)
}
```

### Conditions (`ConditionDef`) — `WorldRules.Check(cond, ctx)` / `WorldRules.CheckAll(list, ctx)`

| type | meaning |
|---|---|
| `Flag` | flag `key` set (≠0); `amount > 0` → value ≥ amount; integer `value` → value == it |
| `NotFlag` | negation of `Flag` |
| `QuestState` | quest `key`: `value` = status name (`NotStarted`/`Active`/`Completed`/`Failed`; also `done`, `started`…) → status equals; otherwise `value` = stage id → quest active on that stage; empty `value` → active |
| `QuestNotStarted` / `QuestActive` / `QuestComplete` | status test (`QuestActive` with a `value` stage id also requires that stage) |
| `HasItem` / `NotHasItem` | `CountItem(key) ≥ max(1, amount)` / negation |
| `Gold` | `Gold ≥ amount` (copper) |
| `Class` / `NotClass` | main character's class is `key` (`value: "party"` → anyone in the party) |
| `Level` | main character level ≥ `amount` |
| `InParty` / `NotInParty` | companion `key` is (not) in `Party` |
| `Companion` | approval of companion `key` ≥ `amount` |
| `TimeOfDay` | `TimeOfDay` equals `key` (or `value`); lists allowed: `"dusk,night"` |

### Outcomes (`OutcomeDef`) — `WorldRules.Execute(outcome, ctx, ownerId)` / `ExecuteAll`

| type | effect |
|---|---|
| `SetFlag` | `Flags.Set(key, amount ≠ 0 ? amount : (integer value or 1))` |
| `ClearFlag` | `Flags.Clear(key)` |
| `StartQuest` / `SetQuestStage` (`value` = stage) / `CompleteQuest` / `FailQuest` | `Quests.Start / SetStage / Complete / Fail` |
| `GiveItem` / `TakeItem` | `key` item × `max(1, amount)` |
| `GiveGold` / `TakeGold` | `amount` copper |
| `GiveXP` | `amount` |
| `Recruit` / `Dismiss` | companion `key` (`Recruit` also sets flag `recruited_<key>`) |
| `Approval` | companion `key`, delta `amount` |
| `StartCombat` | encounter `key` |
| `OpenVendor` / `OpenTrainer` / `OpenRespec` | npc `key` (`""` = dialogue owner) |
| `Rest`, `HealParty` | — |
| `Teleport` | map `key`, spawn `value` (default `"default"`) |
| `EndDialogue` | ends the conversation (choice: immediately; node: after its text, choices hidden) |
| `Special` | `ctx.RunSpecial(key, outcome)` |

`WorldRules.IsInterrupting(type)`: `StartCombat`, `Teleport`, `OpenVendor`, `OpenTrainer`, `OpenRespec`, `Rest`.
A `DialogueRunner` **defers** those until the dialogue ends (`runner.DeferInterruptingOutcomes = false` runs them
immediately). Quest `onComplete` outcomes always run immediately (if one starts combat mid-dialogue, the session
should queue it while `runner.IsActive`).

### `DialogueRunner`

```csharp
var runner = new DialogueRunner(db, ctx, memory /*DialogueMemory or null*/, rng);   // or World.CreateDialogueRunner(rng)
bool runner.Start(string dialogueId, string ownerId = "")   // ownerId: npc/companion talked to; false if unknown
bool runner.Start(DialogueDef def, string ownerId = "")
bool runner.IsActive; bool runner.IsFinished               // IsFinished == !IsActive
DialogueView runner.Current                                // null when not active
bool runner.Choose(int index)                              // index into Current.Choices
bool runner.Continue()                                     // only when Current.Choices is empty
void runner.End()                                          // close now (deferred outcomes still run)
CheckResult runner.LastCheck                               // set by a check choice; cleared by the next Choose/Continue
ChoiceView runner.LastChoice; DialogueDef runner.Dialogue; DialogueNodeDef runner.CurrentNode; string runner.OwnerId
IReadOnlyList<OutcomeDef> runner.PendingOutcomes           // deferred, run at the end
events: NodeEntered(DialogueView), ChoiceMade(ChoiceView), CheckRolled(CheckResult), Ended(string dialogueId)
string runner.Substitute(string text)                      // {player} {class} {companion:<id>}; unknown tokens kept
runner.Memory, runner.Rng, runner.Db, runner.Context, runner.MaxAutoSteps (64)
```

`DialogueView`: `DialogueId, NodeId, SpeakerId, SpeakerName, Portrait, Text, Choices, CanContinue (no choices),
IsLast (Continue ends the dialogue)`.
`ChoiceView`: `Index` (visible list), `SourceIndex` (node.choices), `Text` (substituted, no tag), `DisplayText`
(`"[PALADIN] text"`; a check without tag shows `"[PERSUASION] text"`), `Tag` (upper case, brackets stripped),
`Once`, `PreviouslyChosen` (grey out), `Check` (`CheckPreview` or null), `Ends` (no next and no check), `Def`.

Behaviour:
* Entering a node checks `conditions`; failing → `fallback` (chains allowed; no fallback → dialogue ends). Then the
  node's `outcomes` run, choices are filtered by `conditions`, and used `once` choices are hidden.
* Nodes with empty text and no visible choices are auto-skipped to `next` (logic/router nodes).
* `Choose`: records the choice in `DialogueMemory`, raises `ChoiceMade`, runs the choice's outcomes, then rolls its
  `check` (raises `CheckRolled`, goes to `success`/`failure`) or goes to `next` (`""` ends).
* When a dialogue with an owner ends: `ctx.Quests.OnTalk(ownerId)`, then deferred outcomes, then `Ended`.
* Speakers: `"player"` → `ctx.PlayerName`; `"narrator"` → `""`; npc/companion ids → name + portrait from the
  database; `""` → the owner; other ids → party member name or the id itself.

### Skill checks — `SkillChecks`

`d20 + modifier ≥ dc`; modifier = `floor((stat − 20) / 10)` + 2 if the member's class is proficient (Design §6)
+ `ctx.SkillCheckBonus`. The party member with the best modifier rolls (ties → earlier in `Party`). Natural 20
always succeeds, natural 1 always fails. Stats: Athletics/Intimidation → Strength; Acrobatics/Stealth/
SleightOfHand → Agility; Endurance → Stamina; Arcana/History/Investigation → Intellect; Insight/Persuasion/
Religion/Nature/Survival → Spirit.

```csharp
CheckResult  SkillChecks.Roll(IDialogueContext ctx, SkillCheck skill, int dc, Rng rng)
CheckPreview SkillChecks.Preview(ctx, skill, dc)            // RollerId, RollerName, Modifier, SuccessChance (0..1)
PartyMemberInfo SkillChecks.BestRoller(ctx, skill, out int totalModifier)
int   SkillChecks.Modifier(PartyMemberInfo m, SkillCheck skill)   // stat mod + proficiency (no ctx bonus)
int   SkillChecks.StatModifier(float stat)
bool  SkillChecks.IsProficient(ClassId c, SkillCheck skill)
StatKind SkillChecks.StatFor(SkillCheck skill); float SkillChecks.StatValue(PrimaryStats s, StatKind k)
float SkillChecks.SuccessChance(int modifier, int dc)
string SkillChecks.DisplayName(SkillCheck skill)            // "SLEIGHT OF HAND"
```

`CheckResult`: `Skill, Dc, RollerId, RollerName, RollerClass, Roll (natural d20), StatModifier, Proficiency,
Bonus, Modifier (total), Total, Success, Critical (nat 20), Fumble (nat 1)`; `ToString()` for logs.

### `DialogueMemory`

`HasChosen(dialogueId, nodeId, choiceIndex)`, `MarkChosen(...)`, `TimesStarted(dialogueId)`, `MarkStarted(id)`,
`ChosenCount`, `Clear()`, `Save() → DialogueMemoryState`, `Load(state)`. Keys are `"dialogue/node/choiceIndex"`
(so reordering a node's choices in data shifts `once` history for that node).

---

## 5. Quests — `QuestLog`

```csharp
var quests = new QuestLog(db, ctx);           // WorldState creates it; ctx settable later: quests.Context = ctx
QuestStatus GetStatus(id); string GetStage(id); int GetProgress(id, objectiveIndex)
bool IsActive(id); bool IsCompleted(id); IEnumerable<string> KnownQuests()
bool Start(id); bool SetStage(id, stageId); bool Complete(id); bool Fail(id)
// notifications
void OnKill(string creatureId, int count = 1)
void OnItemCount(string itemId, int count)    // absolute inventory count
void OnTalk(string npcId)                     // DialogueRunner calls this when a dialogue with an owner ends
void OnFlag(string flag)                      // WorldState wires FlagStore.Changed → OnFlag
void OnReach(string regionOrMapId)            // MapRuntime.UpdatePartyPosition / OnEnterMap call this
void OnDefeat(string encounterId)             // MapRuntime.MarkEncounterDone calls this
void Refresh()                                // re-pull Collect/Flag/Defeat objectives from ctx
// rewards
IReadOnlyList<string> PendingRewardChoices    // quest ids whose choiceItems await a pick
bool ClaimRewardChoice(string questId, string itemId)
// journal
List<QuestJournalEntry> GetJournal(bool includeFinished = true)
QuestJournalEntry GetEntry(string questId)    // also for not-started quests (Status NotStarted)
string ObjectiveText(ObjectiveDef o); string ObjectiveDisplay(ObjectiveDef o, int progress)
event Action<QuestEvent> Changed
QuestLogState Save(); void Load(QuestLogState)   // Load raises no events; unknown quests are dropped
```

Rules:
* `Start` begins at the first stage (no-op/false if already started, completed or failed — quests never restart).
* A stage completes when all its objectives are complete. Stages **without objectives wait** for an outcome
  (`SetQuestStage` / `CompleteQuest`). Leaving a stage **forward** (objectives done, `SetQuestStage` to a later
  stage, `CompleteQuest`) runs its `onComplete` outcomes once (re-entrancy safe); then the `next` stage, or quest
  completion when `next` is `""`. `SetQuestStage` backwards or on a not-started quest runs no `onComplete`.
* Objectives: `Kill` (creature id; only kills while the stage is active count), `Collect` (item id; progress =
  current inventory count, can drop; pulled from `ctx.CountItem` on stage start), `Talk` (npc/companion id),
  `Reach` (region id or map id; completes while the party is inside during the stage), `Flag` (progress = flag
  value, so counters work: count 3 ⇒ value 3), `Defeat` (encounter id; also satisfied by flag `enc_<id>`, even if
  won before the stage started; custom `doneFlag`s are reported through `OnDefeat`).
* Completion grants `rewards` via ctx: `GiveXP(xp)`, `GiveGold(gold)`, `GiveItem(item, 1)` per item; a non-empty
  `choiceItems` adds the quest to `PendingRewardChoices` (UI picks one → `ClaimRewardChoice`).
* `QuestEvent`: `Kind` (`Started, ObjectiveProgress, ObjectiveCompleted, StageAdvanced, Completed, Failed,
  RewardChoicePending`), `QuestId, QuestName, StageId, ObjectiveIndex, Progress, Count, Text` — toast-ready
  ("Wolves slain 3/6", the new stage description, or the quest name).
* `QuestJournalEntry`: `Id, Title, Summary, GiverId, GiverName, Level, Main, Status, StageId, StageText,
  Objectives (List<ObjectiveView>), History (descriptions of completed stages, oldest first), Rewards,
  RewardChoicePending`. Journal order: active (main first, then start order), completed, failed (newest first).
* `ObjectiveView`: `Type, Target, Text, Progress, Count, Complete, Display` ("Wolves slain 3/6"; count 1 → just the
  text). Missing objective text defaults to "Slay Grey Wolf", "Collect Wolf Pelt", "Speak with Brann",
  "Reach Whisperwood", "Defeat <id>".

### Quest markers — `QuestMarkers` (`World/Quests/QuestMarkers.cs`)

The WoW-style "!" / "?" over NPCs and on the Map panel (Docs/Expansion.md §3). Pure and read-only.

```csharp
enum QuestMarker { None, AvailableLater /* grey ! */, InProgress /* grey ? */, Available /* yellow ! */, ReadyToTurnIn /* yellow ? */ }
class QuestMarkerInfo { string NpcId, QuestId, QuestName; QuestMarker Kind; bool Main; int Level;
                        bool Yellow, Question, IsNone; string Glyph /* "!" "?" "" */; string Describe() /* "Quest: …", "Turn in: …" */ }

// dry run of a dialogue graph: every outcome a conversation could reach now (no outcome runs, no dice, memory read only)
List<ReachedOutcome> DialogueReach.Collect(DialogueDef d, IDialogueContext ctx, DialogueMemory memory, ReachMode mode = Normal)
   // ReachedOutcome { OutcomeDef Outcome; int LevelGate /* max Level condition on the path */; string NodeId }
   // ReachMode.IgnoreLevel treats Level conditions as met (what the NPC will offer at a higher level)

var index = new QuestMarkerIndex(db);          // static facts, built once (GameSession.QuestMarkerIndex)
index.DialogueOf(personId); index.People; index.Offers(personId)
index.Starters(q); index.Completers(q); index.FlagSetters(flag); index.Enders(q); index.Touching(q)
index.DirectHandIn(q, stage)                    // who resolves this stage (empty: it resolves on its own)
index.HandIn(q, stage)                          // ... with lookahead along `next` (16 hops)
index.TurnInOf(q, stage)                        // HandIn, else the first ender, else the giver

List<QuestMarkerInfo> QuestMarkers.Evaluate(index, ctx, memory, npcId)   // best first, one per quest
QuestMarkerInfo QuestMarkers.Best(list, npcId)
```

* The walk follows `EnterNode` exactly: node conditions with fallback chains (64 hops), visible choices (conditions
  met; used `once` choices hidden), both branches of a check, `next` links; `EndDialogue` stops the path. Outcomes
  on a path do not change later conditions.
* Hand-in of a stage: its `turnIn`; else the union of its Talk targets, the setters of its Flag targets, the
  completers (last stage only) and whoever sets exactly its `next` stage. Kill / Collect / Reach stages look ahead.
* **ReadyToTurnIn** — an active quest whose current stage this NPC can resolve now: every incomplete objective is a
  Talk objective on the NPC (it has a dialogue) or a Flag objective its conversation can set, or the conversation
  reaches `CompleteQuest` or a `SetQuestStage` to a later stage. **Available** — a not-started quest its
  conversation can start now (`StartQuest`, or `SetQuestStage`, which starts it) with `minLevel` ≤ the main
  character's level. **InProgress** — an active quest whose `TurnInOf` is this NPC. **AvailableLater** — an offer
  behind `minLevel` or Level conditions, missed by at most `QuestMarkers.LaterLevelWindow` (3) levels. Priority:
  ReadyToTurnIn > Available > InProgress > AvailableLater; ties: main quests, then data order.
* `QuestMapHint { Kind (Encounter | Region | Chest | Transition), Id, Pos, Size, QuestId, QuestName, Main, Text }`
  — an objective on the current map (`GameSession.QuestHintsOnMap`).

### Map labels — `MapLabels`, `LabelLayout` (`World/Map/MapLabels.cs`, `Util/LabelLayout.cs`)

The Map panel's geometry and label plan, pure so tests check every map. `MapViewport` maps metres to the map rect
with zoom 1–3× (`ZoomAt` keeps the point under the cursor, `PanBy`, `Clamp`). `MapLabels.MapSize(uiW, uiH, w, d)`
is the panel's map rect; `ShortName` (NpcDef.shortName, else the name without its honorific, first word when longer
than 16), `RegionName` (RegionDef.name, else the id made readable), `Plan(MapLabelInput)` → `MapLabelPlan`
(`Labels`, `ByNpc`, `Glyphs`). Order: quest NPCs (by marker), exits, service crowds (all members named when they
fit, else one label with the region's name), everyone else. `LabelLayout` places each label at the first free
candidate round its anchor (8 directions, growing rings, leader line beyond the first) inside the bounds, never over
an obstacle or an earlier label; a label that fits nowhere is left out (the panel shows it on hover).

---

## 6. Map runtime — `MapRuntime`

Engine-independent bookkeeping for one map (`WorldState.GetMap(mapId)`; state persisted in saves).

```csharp
MapDef Def; string Id; FlagStore Flags; QuestLog Quests; MapRuntimeState State
void OnEnterMap()                                 // visited + Quests.OnReach(mapId)
Vec2 SpawnPosition(string spawnId)                // falls back to "default", then the map centre
// encounters
static string DoneFlag(EncounterDef e)            // e.doneFlag or "enc_<id>"
EncounterDef FindEncounter(id); bool IsEncounterDone(id | def)
bool IsEncounterAvailable(EncounterDef e)         // !done && Flags.Test(requireFlag)
bool IsEncounterVisible(EncounterDef e)           // available && (!hidden || triggered) → draw its enemies
IEnumerable<EncounterDef> AvailableEncounters()
EncounterDef FindTriggeredEncounter(IReadOnlyList<Vec2> partyPositions, IReadOnlyList<bool> stealthed = null)
EncounterDef FindTriggeredEncounter(Vec2 position, bool stealthed = false)
   // first available encounter with a member within radius; stealthed members only within min(radius, 3 m)
void MarkEncounterTriggered(id); bool IsEncounterTriggered(id)
void MarkEncounterDone(id)                        // sets the done flag + Quests.OnDefeat(id)
void ResetEncounter(id)                           // respawn / scripted reset
// npcs
bool IsNpcVisible(MapNpcDef n)                    // Flags.Test(requireFlag) && !(hideFlag != "" && Flags.Test(hideFlag))
IEnumerable<MapNpcDef> VisibleNpcs()
// chests
ChestDef FindChest(id); bool IsChestAvailable(ChestDef c)   // requireFlag
bool IsChestOpened(id); void MarkChestOpened(id)
bool IsChestLocked(ChestDef c)                    // lockCheck != null, not unlocked, not opened
void MarkChestUnlocked(id)
CheckResult TryUnlockChest(ChestDef c, IDialogueContext ctx, Rng rng)   // null if not locked; success unlocks; retries allowed
// transitions & regions
bool IsTransitionUnlocked(TransitionDef t)        // Flags.Test(requireFlag); show t.lockedText otherwise
bool IsTransitionVisible(TransitionDef t)         // !hidden || Flags.Test(revealFlag) (a hidden one needs a revealFlag)
static bool IsTransitionVisible(TransitionDef t, FlagStore flags)   // the same for any map's transitions
TransitionDef TransitionAt(Vec2 p)                // first VISIBLE transition containing p; size axes <= 0 default to 2 m
TransitionDef FindTransition(string id)
static Vec2 TransitionSize(TransitionDef t); static bool RectContains(center, size, p)
IReadOnlyList<RegionDef> UpdatePartyPosition(Vec2 leaderPos)
   // Quests.OnReach(map id + every region containing the leader), sets enterFlags, returns regions entered
   // for the FIRST time (toast r.text). The list is reused: valid until the next call.
bool HasEnteredRegion(id); IEnumerable<RegionDef> RegionsAt(Vec2 p)
// props
bool IsPropVisible(PropDef p)                     // Flags.Test(requireFlag) && !(hideFlag != "" && Flags.Test(hideFlag))
PropDef FindProp(string interactId, bool visibleOnly = true)   // props, then foreground; the first visible one by default
// passive region checks (RegionDef.check: skill + dc; checkFlag; successText/failText; requireFlag)
bool IsRegionChecked(id); void MarkRegionChecked(id)           // State.checkedRegions (saved: once per save)
bool IsRegionCheckDue(RegionDef r, Vec2 leaderPos)  // has a check, not checked yet, Flags.Test(requireFlag), leader inside
CheckResult RollRegionCheck(RegionDef r, IDialogueContext ctx, Rng rng)
   // marks it checked; null without a roll when checkFlag already holds (found another way); else the best party
   // member's SkillChecks.Roll, success sets checkFlag. GameSession rolls it and raises the events (SessionAPI §5).
```

**Hidden passages.** A `hidden` transition does not exist until `Flags.Test(revealFlag)`: `TransitionAt` skips it,
`GameSession.UseTransition` refuses it ("There is no way through here."), the UI does not hover, click or draw it.
Ways to set the reveal flag: a region `check` (`checkFlag`), a region `enterFlag`, a dialogue `SetFlag` (an NPC hint
or a prop dialogue with an Investigation check, `PropDef.dialogue`), any other flag source. The session announces each
passage once (`SessionEventKind.SecretFound`).

**Discovery in the shipped content** (Docs/Expansion.md §8): every hidden dungeon has a Perception region next to its
entrance plus at least one other way in. Once the flag holds, the transition's entrance marker (cave mouth, barrow
door, stairs or portal; ThreeD.md) appears; in Lanternvale and Whisperwood the bushes hiding the mouth (`hideFlag`)
vanish too, and the nav grid follows them (`RefreshFlags`).

| map → dungeon | reveal flag | Perception region (DC) | other ways |
|---|---|---|---|
| lanternvale → `dgn_root_hollows` | `found_root_hollows` | `reg_lv2_kusu_roots` (13) | Nell's hint (`dlg_child_nell`), the Listening Root log (`dlg_lv2_listening_root`) |
| whisperwood → `dgn_mossdeep` | `found_mossdeep` | `reg_ww2_mossy_hollow` (14) | Komorebi, Sprig, Tamsin, the King-stone (`dlg_ww2_kingstone`); the quest `ww2_kings_glow` |
| shrine → `dgn_lantern_catacombs` | `found_lantern_catacombs` | `reg_sh2_high_terrace` (14, once `heart_lantern_lit`) | Aiko, the cracked plinth (`dlg_sh2_cracked_plinth`) |
| amberfield → `dgn_barrow` | `found_barrow` | `reg_am_kings_ring` (14) | Nib, Mother Delve, Old Ida (`am_q_barrow`), the King's Stone, Archivist Penhallow |
| mirefen → `dgn_drowned_vault` | `found_drowned_vault` | `reg_mf_sunken_statue` (14) | Old Bloop, the sunken statue (`dlg_mf_sunken_statue`), Penhallow |
| skyreach → `dgn_frozen_sanctum` | `found_frozen_sanctum` | `reg_sr_frozen_falls` (14) | Odran, the frozen falls (`dlg_sr_frozen_falls`), Penhallow |
| skyreach → `raid_ashwyrm_roost` | `sr_roost_revealed` | — | the end of `mq2_ash_on_the_wind` |

Smaller secrets use the same machinery with chests: e.g. `reg_am_lookout` → `am_lookout_cache`, `reg_bw_river_steps` →
`bw_river_cache_found`, `reg_sr_vista` → `sr_found_cache`, and inside dungeons and raids `reg_dg4_tripwire` (spotting
it sets the dart-trap ambush's `doneFlag`), `reg_dg6_chantry`, `reg_r1_offerings`, `reg_r2_last_stand`.

Loot generation, combat setup and actually moving between maps are the session's job.

---

## 7. `WorldState` and save data

```csharp
var world = new WorldState(db, ctx);    // Flags, Quests, DialogueMemory, per-map runtimes
world.Context                            // get/set (forwards to Quests.Context)
MapRuntime world.GetMap(string mapId)    // null + warning when unknown
MapRuntime world.GetMap(MapDef def)      // registers ad-hoc maps
IEnumerable<MapRuntime> world.LoadedMaps
DialogueRunner world.CreateDialogueRunner(Rng rng = null)
WorldSaveData world.Save(); void world.Load(WorldSaveData data)   // null/empty data resets
```

All DTOs are `[Serializable]` plain classes with public fields (`JsonWriter.Serialize` / `JsonMapper.FromJson`);
a save → load → save round trip is byte-identical.

```csharp
public sealed class WorldSaveData { int version = 1; FlagStoreState flags; QuestLogState quests; DialogueMemoryState dialogue; List<MapRuntimeState> maps; }
public sealed class FlagStoreState { Dictionary<string,int> flags; }                           // sorted keys
public sealed class QuestLogState { List<QuestRecordState> quests; List<string> pendingRewardChoices; int nextOrder; }
public sealed class QuestRecordState { string id; QuestStatus status; string stage; int[] progress; List<string> history; int order; }
public sealed class DialogueMemoryState { List<string> chosen; Dictionary<string,int> started; }
public sealed class MapRuntimeState { string mapId; bool visited; List<string> openedChests, unlockedChests, enteredRegions, triggeredEncounters; }
```
