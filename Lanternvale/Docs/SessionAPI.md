# Lanternvale — Session API (`Lanternvale.Session`)

`GameSession` is **the single object the Unity game-flow and UI layers talk to**. It owns the party, the shared
bags and gold, the world state (flags, quests, dialogue memory, map runtimes), the current map's navigation grid,
the exploration ("field") rules context, the current battle, the game clock and the RNG — and it saves/loads all
of it as one JSON string.

Pure C# 9 in `Assets/Lanternvale/Scripts/Core/Session/**` (no UnityEngine). Tests:
`Tools/harness/CoreTests/TestsSession*.cs` (`Tools/check.sh core --filter Session`).

It hands out the rules engine's and the world module's own types unchanged — `Unit`, `Battle`, `AIStep`,
`ItemInstance`, `VendorShop`, `TrainerOffer`, `LevelUpInfo`, `AbilityStatus`, `CombatEvent` (`Docs/CoreAPI.md`);
`DialogueRunner`/`DialogueView`, `QuestLog`, `QuestJournalEntry`, `MapRuntime`, `NavGrid`/`NavPath`, `CheckResult`
(`Docs/WorldAPI.md`). Coordinates are world metres (`Lanternvale.Util.Vec2`); money is copper.

**Conventions.** Commands that can fail return `null` on success or a **reason string** for the UI ("Not enough
money."). Commands returning an object return `null` on failure and put the reason in `session.LastError`.
Everything the UI should react to is also announced as a `SessionEvent` (§2).

| File | Contents |
|---|---|
| `GameSession.cs` | construction, `NewGame`, `Mode`, events |
| `GameSession.Party.cs` | party/roster/camp, companions, leader, formation, pets, XP & level sync, approval |
| `GameSession.Items.cs` | bags, equipment, ability/item use, loot window, vendors, trainers, quest rewards, talents, respec |
| `GameSession.World.cs` | maps, movement, triggers, interactions, dialogue, `IDialogueContext`, content specials |
| `GameSession.Combat.cs` | encounters → `Battle`, openers, AI stepping, leave combat, `FinishBattle` |
| `GameSession.Time.cs` | `Tick`, clock / time of day, resting |
| `GameSession.Save.cs`, `SaveData.cs` | save/load and the versioned DTOs |
| `SessionTypes.cs` | `SessionMode`, `SessionEvent(Kind)`, `NewGameOptions`, `SessionSettings`, `LootWindow`, `BattleSummary`, `InteractResult`, `TriggerResult`, `MoveResult`, `PartyMovePlan`, `SaveHeader` |
| `NavGridPathfinder.cs` | `IPathfinder` adapter over `World.NavGrid` (every battle uses it) |
| `StartingGear.cs` | level-appropriate ("veteran") gear selection |

---

## 1. Lifecycle

```csharp
using Lanternvale.Session;

var session = new GameSession(db, seed: 0);         // seed 0 = time based; same seed + same inputs = same game
session.EventRaised += OnSessionEvent;              // and/or poll session.TakeEvents() once per frame

session.NewGame(new NewGameOptions {
    Name = "Aria", Class = ClassId.Paladin,
    StartLevel = 1,                                 // 1..60; > 1 = veteran start (§3)
    Sprite = "", Portrait = "",                     // art key overrides ("" = class art)
    VeteranGear = true,                             // veteran starts: level-appropriate gear (+ for later recruits)
    AutoAllocateTalents = true,                     // veteran starts: ClassDef.defaultBuild
    PlayOpening = true,                             // start config.startDialogue right away
});
// shorthand: session.NewGame("Aria", ClassId.Paladin, startLevel: 20);

void Update() { session.Tick(Time.deltaTime); }     // clock, regen, cooldowns, buffs — exploration only
```

`NewGame` resets everything, creates the main character (member id `"player"`), fills the bags
(`config.startingItems`) and purse (`config.startingGold`), raises `GameStarted`, puts the party on
`config.startMap`/`config.startSpawn` (`MapEntered`) and, with `PlayOpening`, starts `config.startDialogue`
(`DialogueStarted`). The clock starts at `Settings.StartHour` (18.5, dusk — the opening scene) on day 1.

### Mode

`session.Mode` (`SessionMode`): `None` (no game) · `Exploration` · `Dialogue` · `Combat` · `GameOver`.
Overlays do not change the mode; they are state opened/closed with events: `ActiveVendor`, `ActiveTrainer`,
`ActiveRespecNpc` (`""` = closed), `PendingLoot`.

| Mode | UI | Allowed |
|---|---|---|
| `Exploration` | map, party, NPCs, encounters | move, interact, field abilities/items, equipment, vendors, trainers, talents, rest, save |
| `Dialogue` | dialogue box bound to `session.Dialogue.Current` | `ChooseDialogue`, `ContinueDialogue`, `EndDialogue` (clock paused, no saving) |
| `Combat` | battle HUD bound to `session.Battle` | battle actions, `RunAIStep`, `LeaveCombat`, `FinishBattle` once `Battle.IsOver` (no saving) |
| `GameOver` | "load last save" | `LoadGame`, `NewGame` |

Helpers: `HasGame`, `IsExploring`, `InCombat`, `IsGameOver`.

---

## 2. Events

```csharp
event Action<SessionEvent> EventRaised;       // synchronous
List<SessionEvent> TakeEvents();              // the same events, queued; QueueEvents = false disables the queue
IReadOnlyList<SessionEvent> PendingEvents;    // peek
event Action<CombatEvent> CombatEventRaised;  // every CombatEvent of the field context and of battles
```

`SessionEvent`: `Kind`, `Text` (toast-ready, may be ""), `Id`, `Id2`, `Amount`, `Unit`, `Item` (`ItemInstance`),
`Quest` (`World.QuestEvent`), `LevelUp` (`Rules.LevelUpInfo`), `Check` (`World.CheckResult`), `Battle`, `Loot`
(`LootWindow`), `Outcome` (`CombatEndKind`).

| Kind | Meaning / payload |
|---|---|
| `Toast` | info line in `Text` (NPC bark, sign text, "The chest is empty.", an opener that failed …) |
| `GameStarted` / `GameLoaded` | after `NewGame` / `LoadGame` (a `MapEntered` follows) |
| `GameOver` | the party was defeated (`Mode == GameOver`) |
| `MapEntered` | `Id` map id, `Id2` spawn id ("" after a load), `Text` map name — rebuild the `MapView`, then spawn views for `PartyUnits()`, `VisibleNpcs()`, `VisibleEncounters()` |
| `RegionEntered` | first entry: `Id` region id, `Text` region text |
| `TransitionLocked` | `Id` transition id, `Text` its `lockedText` |
| `DialogueStarted` / `DialogueEnded` | `Id` dialogue id, `Id2` owner (npc, companion or encounter id, or "") |
| `SkillCheck` | a d20 roll (dialogue choice, lock, Pick Lock): `Check`, `Text` summary |
| `QuestStarted` `QuestUpdated` `QuestCompleted` `QuestFailed` | `Id` quest id, `Quest` (World `QuestEvent`), `Text` toast ("Quest started: …", "Grey Wolves driven off 2/3", new stage text) |
| `QuestRewardChoice` | `Id` quest id — show the pick-one dialog (§4) |
| `ItemReceived` / `ItemLost` | `Id` item id, `Amount` count, `Item` (when known), `Text` ("Received: Kindling Taper") |
| `GoldChanged` | `Amount` delta, `Text` ("+1s 20c") |
| `XpGained` | `Amount`, `Unit` = main character |
| `LevelUp` | `Unit`, `Amount` new level, `LevelUp` (health/mana gained, `TalentPointsGained`, `NewTrainable` offers), `Text` — one per level and character (companions too) |
| `AbilityLearned` | `Unit`, `Id` ability, `Amount` rank (training, talent abilities, auto-trained companions) |
| `TalentPointsAvailable` | `Unit`, `Amount` unspent points |
| `CompanionRecruited` | `Id`, `Unit`, `Text` ("Kael joins the party." / "… waits at camp (the party is full).") |
| `CompanionDismissed` | `Id`, `Unit` |
| `PartyChanged` | active party / camp changed: rebuild portraits, spawn/despawn views |
| `LeaderChanged` | `Unit` = new leader |
| `ApprovalChanged` | `Id` companion, `Amount` delta, `Text` ("Seren approves.") |
| `PetChanged` | `Unit` = owner; its `Pet` appeared, died or was dismissed (spawn/despawn its view) |
| `VendorOpened`/`Closed`, `TrainerOpened`/`Closed`, `RespecOpened`/`Closed` | `Id` npc id |
| `LootOpened` / `LootClosed` | `Loot` |
| `ChestOpened` | `Id` chest id → `MapView.SetChestOpen` |
| `CombatStarted` | `Battle`, `Id` encounter id, `Text` first creature bark — create the combat presenter |
| `CombatEnded` | `Outcome` (`Victory` / `Defeat` / `Left`), `Id` encounter id, `Battle` |
| `Rested` / `PartyHealed` | `Text` |
| `SpecialOutcome` | `Id` special id (`Id2` argument). **`RekindleLanterns`**: `Amount` 1 = the story moment (`MapView.SetLanternLit(id, true, animate: true)` for every lantern + banner `Text`); `Amount` 0 = raised after every `MapEntered` while flag `lanterns_rekindled` is set (relight silently) |
| `TimeOfDayChanged` | `Id`/`Text` new phase (`dawn` `day` `dusk` `night`) |

---

## 3. Characters, party, companions, pets

```csharp
Unit Main                              // main character, member id "player" (GameSession.MainId)
IReadOnlyList<Unit> Party              // active characters, Party[0] == Main, at most PartySize (config.partySize)
IReadOnlyList<Unit> Roster             // Main + every companion ever recruited
List<Unit> Camp()                      // recruited, waiting at camp
List<Unit> PartyUnits()                // active characters + their persistent pets — spawn a UnitView for each
bool IsInParty(Unit u); int PartyLevel; int PartySize
Unit Leader; string SetLeader(Unit u)  // the unit the player moves / the camera follows (falls back to Main)
string MemberId(Unit u)                // "player" | companion id | "" (pets, enemies)
Unit FindMember(string memberId)       // roster lookup
CompanionStatus CompanionStatusOf(string id)    // NotRecruited | Active | Camp | Away (dismissed)
string SetPartyMemberActive(string companionId, bool active)   // camp <-> party, out of combat
void SetAutoPlay(Unit u, bool on)      // companion AI plays the unit (and its pet) in combat
int GetApproval(string id); void ChangeApproval(string id, int delta); IReadOnlyDictionary<string,int> Approvals
PartyMemberInfo MemberInfo(Unit u)     // the dialogue/skill-check snapshot (effective primary stats)
```

**Veteran start** (`StartLevel > 1`): every trainable rank up to the level is learned; at level ≥ 10 the talent
points follow `ClassDef.defaultBuild`; with `VeteranGear`, every slot gets the better of (a) the best class-usable
common/uncommon database item for the level (not a quest reward, companion signature item, guaranteed/named drop,
unique or quest item) and (b) a generated "Journeyman's/Veteran's/Champion's …" piece
(`ItemGenerator.VeteranGear`: green, blue from level 50, role-aware stats and weapons); displaced starting items go
to the bags; extra gold `25 × level²` copper (`StartingGear.VeteranGold`).

**Companions.** `Recruit(id)` (the dialogue outcome) creates the companion from its `CompanionDef` at the party
level (class, `statBonus`, starting items equipped — leftovers to the bags — all trainable ranks,
`preferredTalents`/`defaultBuild` talents, veteran gear in veteran games), sets `recruited_<id>` (its map NPC
hides) and adds it to the active party — or to camp when the party is full. Recruiting a camp or away companion
returns the same unit (gear, level, approval kept). `Dismiss(id)` removes it from the party and clears
`recruited_<id>` (the NPC reappears at its map spot, status `Away`; talk to it to recruit again). Content sets
`<id>_met` itself. Companions are **level-synced** with the main character; on level-up they learn new ranks for
free (`Settings.CompanionAutoTrain`) and spend talent points on their build
(`Settings.AutoAllocateCompanionTalents`). Newly recruited companions get `AutoPlay = Settings.CompanionAutoPlay` (default false — BG3-style, the player controls every member; the main character is
always player-controlled unless `SetAutoPlay(Main, true)`). `Settings` (auto-play, auto-train, clock speed) survive
`NewGame` and are saved with the game.

**Pets.** Hunter pets (Call Pet) and warlock demons are summoned with abilities (in the field or in battle) and stay
as the owner's `Unit.Pet`: listed in `PartyUnits()`, following in formation, joining every battle right after their
owner, kept across maps and saves. A pet that dies is removed after the battle (`PetChanged`); a hunter's dead pet
stays dead (`Unit.HunterPet.Dead`) until Revive Pet or a long rest. Totems and temporary guardians placed out of
combat join the next battle and are cleared on map change.

**XP.** `GiveXP(raw)` (dialogue outcome / quest reward amount) applies `config.xpRate`; battles give the main
character's `BattleResult` XP (rate already applied); `GivePartyXp(scaled)` grants an exact amount. The main
character gains it and every roster companion is raised to the same level → `XpGained`, `LevelUp`×n,
`AbilityLearned`, `TalentPointsAvailable`.

---

## 4. Items, abilities, loot, vendors, trainers, quests, talents

```csharp
Inventory Inventory; int Gold                 // shared bags (Items, Count(id), Find(id)) — read freely, mutate via the session
int CountItem(string itemId)
void GiveItem(string id, int n); void TakeItem(string id, int n); void GiveGold(int c); void TakeGold(int c)
string CanEquip(Unit u, ItemInstance item, EquipSlot? slot = null)    // reason or null
string Equip(Unit u, ItemInstance item, EquipSlot? slot = null)       // from the bags (one of a stack); displaced → bags
string Unequip(Unit u, EquipSlot slot)
string DestroyItem(ItemInstance item, int count = 1)                  // quest items refuse

// abilities & items — combat: the active unit through the Battle; exploration: the Field context
List<AbilityStatus> GetAbilityBar(Unit u)
ActionResult UseAbility(Unit u, string abilityId, Unit target = null, Vec2? point = null)   // buffs, Call Pet, Stealth…
string CannotUseItemReason(Unit user, ItemInstance item, Unit target = null)  // Battle.CanUseItem: level/class restriction first, then the use ability
ActionResult UseItem(Unit user, ItemInstance item, Unit target = null, Vec2? point = null)
Battle Field                                   // exploration context (InCombat = false) of PartyUnits()
```

Food/drink (`requires.notInCombat`) only work out of combat and are cancelled when their eater moves ("must remain
seated"); potions share `cooldownGroup` `potion`; cooldowns keep running in real time out of combat.

```csharp
LootWindow PendingLoot                         // { Source ("battle" | chest id), Title, Items, Gold } or null
string TakeLoot(ItemInstance item); void TakeAllLoot(); void CloseLoot(bool takeAll = false)
// gold is added when the window opens; items left in a closed window are lost (travel/combat take them all)

VendorShop ActiveVendor; VendorShop GetVendor(string npcId)   // Rules VendorShop: Offers(), Buyback, Npc, Stock
void OpenVendor(string npcId); void CloseVendor()
string Buy(string itemId, int count = 1); string Sell(ItemInstance item, int count = 1); string BuyBack(ItemInstance item)
// stock and buyback persist per vendor (saved)

NpcDef ActiveTrainer; void OpenTrainer(string npcId); void CloseTrainer()
bool CanTrainHere(Unit u)                      // the open trainer teaches u's class (NpcDef.trains)
List<TrainerOffer> TrainerOffers(Unit u)       // Progression.TrainerOffers (CanTrain / Reason, gold checked)
string Train(Unit u, string abilityId, int rank = 0)   // 0 = highest rank available now; missing lower ranks paid too
int TrainAll(Unit u)

IReadOnlyList<string> PendingQuestRewards      // quests whose choiceItems await a pick
List<ItemDef> QuestRewardChoices(string questId)
string ClaimQuestReward(string questId, string itemId)
List<QuestJournalEntry> Journal(bool includeFinished = true)
FlagStore Flags; QuestLog Quests               // World.Flags / World.Quests (read; e.g. Quests.GetEntry(id))

int TalentPointsAvailable(Unit u); string LearnTalent(Unit u, string talentId); int AutoAllocateTalents(Unit u)
string ActiveRespecNpc; void OpenRespec(string npcId); void CloseRespec()
int RespecCost(Unit u); string Respec(Unit u)  // needs the respec window; pays 1g, 5g, 10g…; then LearnTalent/AutoAllocate
```

---

## 5. Maps, movement, interaction

```csharp
string MapId; MapDef MapDef; MapRuntime Map; NavGrid Nav; IPathfinder Pathfinder
void EnterMap(string mapId, string spawnId = "default")     // travel; places the party on standing spots around the spawn
List<MapNpcDef> VisibleNpcs()                               // requireFlag/hideFlag applied (recruited companions hidden)
List<EncounterDef> VisibleEncounters()                      // draw their enemies (hidden ambushes only once triggered)
string NpcName(string id); string DialogueOf(string id)     // npcs and companions
bool LanternsRekindled                                      // flag lanterns_rekindled (also see SpecialOutcome)
```

The session is authoritative for positions (`Unit.Position`/`Facing`); the Unity layer animates.

```csharp
NavPath FindPath(Vec2 from, Vec2 to)                         // exploration agent (units never block)
PartyMovePlan PlanPartyMove(Vec2 destination)
   // { Leader, LeaderPath (List<Vec2>), Length, Reachable, Followers: [{ Unit, Destination, Path }] } — changes nothing
TriggerResult UpdatePartyPositions(Vec2 leaderPos, IReadOnlyList<Vec2> others = null)
   // report positions while animating (whenever the leader moved ~0.25 m): others = the other PartyUnits() in order,
   // null = followers snap to formation slots. Then runs CheckTriggers().
TriggerResult CheckTriggers()      // region first entries, encounter triggers, transitions
   // TriggerResult { Stop, Kind (None | Dialogue | Combat | Travel | Locked), Id } — Stop: stop animating
MoveResult MoveLeader(Vec2 destination)   // instant walk in 0.25 m steps (tests, fast travel); stops at a trigger
List<Vec2> FormationSlots(Vec2 leaderPos, Vec2 facing, int count)   // GameSession.FormationOffsets behind the leader
```

**Encounters.** The first available encounter with an active party member inside its radius triggers; stealthed
members count only within 3 m. An encounter with a `dialogue` plays it first (owner = encounter id): peaceful
branches set its done flag; fight branches use `StartCombat <encounterId>`, which starts the fight when the dialogue
closes. An encounter whose dialogue ended unresolved does not re-trigger until the whole party has left its radius
(+1 m). Combat never starts for an encounter that is done. **Transitions** trigger once the leader has been outside
every transition rectangle since arriving (so spawning next to one never bounces you back).

```csharp
InteractResult TalkTo(string npcId)             // npc or companion → dialogue (Kind Dialogue), or its bark (Kind Text)
InteractResult OpenChest(string chestId)        // Kind Loot (LootOpened); Kind Locked (Ok false, Message "Locked (Sleight of Hand DC 13).")
CheckResult TryUnlockChest(string chestId)      // best member's lock check (retries allowed); success opens the chest
CheckResult PickLock(string chestId, Unit rogue = null)   // rogue_pick_lock: + floor(level/5); falls back to TryUnlockChest
InteractResult UseTransition(string transitionId)          // Kind Travel, or Kind Locked + TransitionLocked event
string InspectProp(string interactId)           // sign/shrine text (also a Toast)
const float InteractionRange = 2.5f; bool InInteractionRange(Vec2 p)   // the UI walks there first; not enforced
```

`InteractResult`: `Ok`, `Kind` (`None` `Dialogue` `Loot` `Locked` `Travel` `Text`), `Message`, `Id`.

---

## 6. Dialogue

```csharp
DialogueRunner Dialogue                          // Dialogue.Current: SpeakerName, Portrait, Text, Choices[i].DisplayText,
                                                 // .Check (CheckPreview: roller, modifier, SuccessChance), .PreviouslyChosen
bool StartDialogue(string dialogueId, string ownerId = "")
bool ChooseDialogue(int index)                   // index into Dialogue.Current.Choices
bool ContinueDialogue()                          // when Dialogue.Current.CanContinue
void EndDialogue()
```

Outcomes run against the session's `IDialogueContext`: flags/quests, items and gold (toasts, quest Collect
objectives), `GiveXP` (rate applied), `Recruit`/`Dismiss`, `Approval`, and — after the dialogue closes —
`StartCombat`, `OpenVendor`/`OpenTrainer`/`OpenRespec`, `Rest` (long rest anywhere: inns), `Teleport`
(`EnterMap`). `HealParty` heals at once. `Special` outcomes run the rules engine's content specials
(`Specials.RunContentSpecial`, the session is the `IContentContext`) and raise `SpecialOutcome`. Skill checks use the
active party's effective primary stats + class proficiency (Design §6); the best member rolls;
`ExtraSkillCheckBonus` (`Func<memberId, SkillCheck, int>`) can add item/buff bonuses. `TimeOfDay` conditions see the
session clock.

---

## 7. Combat

```csharp
Battle Battle; EncounterDef BattleEncounter     // set from CombatStarted until FinishBattle/LeaveCombat
Battle StartEncounter(string encounterId, SurpriseMode surprise = SurpriseMode.None)   // None | EnemiesSurprised | PartySurprised
Battle EngageEncounter(string encounterId, Unit attacker = null, string openerAbilityId = null, int targetIndex = 0)
   // the player strikes first (dialogue skipped): with an opener (Charge, Cheap Shot, Ambush, Pyroblast…) on
   // enemy targetIndex (EncounterDef.enemies order) via Battle.BeginWithOpener — an Opener from stealth surprises
   // the enemies; without one, enemies are surprised when the attacker (default leader) is stealthed.
bool IsPlayerTurn; Unit ActiveUnit
AIStep RunAIStep()          // one step of the active AI unit (enemy, pet, auto-played companion); null on a player turn
int RunAITurn()
BattleOutcome AutoResolve(int maxRounds = 60)   // AI plays everyone (tests, "auto battle")
List<CombatEvent> TakeBattleEvents()            // cursor kept by the session (or Battle.TakeEvents(ref cursor))
string CannotLeaveCombatReason()                // null when every remaining hostile is Passive (training dummy)
BattleSummary LeaveCombat()                     // Battle.Disengage + FinishBattle; Outcome Left; encounter not done
BattleSummary FinishBattle()                    // once Battle.IsOver
void ResetEncounter(string encounterId); bool IsPracticeEncounter(EncounterDef e)
```

Battles contain the active party (by reference — the same `Unit`s as in exploration), their pets and owned
totems/summons, and **new** enemy `Unit`s built from the `EncounterDef` (`UnitFactory.CreatureLevel`: explicit level,
or `scaleToParty` + `levelOffset`, or the creature's range) at the encounter's positions; replace the encounter's
placeholder enemy views on `CombatStarted`. Overlapping units are moved to free walkable spots. The pathfinder is
`NavGridPathfinder` over the map's `NavGrid`. Player input goes straight to the `Battle` (`UseAbility`, `UseItem`,
`Move`, `EndTurn`, …) or through `session.UseAbility/UseItem`.

`BattleSummary` { `Outcome` (`Victory` | `Defeat` | `Left`), `EncounterId`, `Rounds`, `Xp`, `LevelUps`, `Killed`
(creature ids), `Loot` }:
* **Victory**: quest kill notifications, XP (level-ups), the encounter's done flag (Defeat objectives), downed/dead
  allies back at 1 HP, dead pets removed, then `CombatEnded` and the loot window (`LootOpened`, gold added).
  Practice encounters (every enemy Passive: `enc_training_dummy`) are reset instead of marked done.
* **Left** (`LeaveCombat`, or a battle that ended `Fled`): no XP/loot for the passive targets; the encounter is
  reset (fight it again later; it does not re-trigger while you stand in it).
* **Defeat**: `Mode = GameOver`, `CombatEnded` + `GameOver` events.

After any battle the party stands where it fought; the field context is rebuilt.

---

## 8. Time & resting

```csharp
void Tick(float realSeconds)       // Exploration only: clock + Field.TickOutOfCombat (cooldowns, aura ticks/expiry,
                                   // food/drink, summon lifetimes, regen: health 2%/s + 0.25×Spirit, mana by Spirit…)
float GameHour; int Day; float PlaySeconds
string TimeOfDay                   // dawn [5,8) day [8,17.5) dusk [17.5,20.5) night; fixed-time maps report theirs
static string PhaseOf(float hour); void AdvanceClock(float hours); void SetTime(float hour, int day = 0)
string CannotRestReason(); bool LongRest()   // camp on restArea maps
void Rest()                         // the Rest outcome (innkeeper): long rest anywhere
void HealParty()                    // the HealParty outcome
```

`Settings.GameHoursPerRealMinute` (default 1 = `DayNight.HoursPerSecond` 1/60). The session is the clock: push
`session.GameHour` into `DayNight.WorldHour` (maps with `ambient.dayNightCycle`) instead of letting DayNight run.
The clock pauses in dialogue and combat. A long rest restores health/mana/energy/focus (rage 0), clears cooldowns
and debuffs, revives a dead hunter pet (Call Pet again) and advances to 08:00 the next morning (`Rested`).
Companions at camp are not ticked.

---

## 9. Save / load

```csharp
string CannotSaveReason()                    // null | "Cannot save during combat." | "…during a conversation." | game over
string SaveGame()                            // JSON (InvalidOperationException when not allowed)
bool TrySaveGame(out string json, out string reason)
bool LoadGame(string json, out string error) // replaces this session's state (same object; events GameLoaded + MapEntered)
static SaveHeader ReadSaveHeader(string json)   // slot lists: version, playerName, playerClass, playerLevel, mapId,
                                                // mapName, day, gameHour, playSeconds, party (member ids); null if unreadable
SessionSaveData BuildSaveData()              // the DTO (debugging)
```

A save holds everything needed to restore the session exactly: party units (class/companion ids, name, art, level,
xp, health and resources, abilities + ranks, talents, respec count, equipment — database items by id, generated
items with their whole `ItemDef`, random suffix id/name/stats — non-passive auras with remaining time, stacks,
charges and caster (party member refs), cooldowns, lockouts, auto-play, position/facing, hunter pet state, the
persistent pet with its own state), roster order, active party, leader, approval, bags and gold, vendor stock and
buyback, an open loot window, `WorldSaveData` (flags, quests, dialogue memory, map runtimes), map id, clock, play
time, RNG state and `Settings`. `SessionSaveData.version` = `GameSession.SaveVersion` (1); newer versions are
refused. Save → load → save is byte-identical. A save that cannot be read or validated leaves the session unchanged
(if applying a valid-looking save throws, the previous state is restored when it was saveable). The Unity layer owns
files (`Application.persistentDataPath`).

---

## 10. Typical flows

```csharp
// new game → opening
session.NewGame("Aria", ClassId.Mage);
while (session.Mode == SessionMode.Dialogue)
{
    var v = session.Dialogue.Current;     // v.SpeakerName, v.Text, v.Choices[i].DisplayText
    if (v.CanContinue) session.ContinueDialogue(); else session.ChooseDialogue(PlayerPick(v));
}

// explore: click on the ground
var plan = session.PlanPartyMove(click);               // animate plan.LeaderPath, plan.Followers[i].Path
// every frame while walking:
var tr = session.UpdatePartyPositions(leaderView.Position, otherViewPositions);
if (tr.Stop) StopWalking();                            // dialogue / combat / travel / locked (events say which)

// talk, trade, train
session.TalkTo("elder_maru");                          // DialogueStarted … DialogueEnded
session.TalkTo("merchant_tilly");                      // the trade choice (OpenVendor outcome) → VendorOpened after the dialogue
session.Buy("potion_minor_healing", 2);  session.Sell(item);  session.CloseVendor();
session.TalkTo("trainer_quillon");                     // → TrainerOpened
foreach (var o in session.TrainerOffers(session.Main)) if (o.CanTrain) session.Train(session.Main, o.Ability.id, o.Rank);

// combat (walked into an encounter, a dialogue said StartCombat, or the player attacked)
session.EngageEncounter("enc_boars_edge", rogue, "rogue_cheap_shot");
while (!session.Battle.IsOver)
{
    if (session.IsPlayerTurn) { /* input → session.Battle.UseAbility(...), Move(...), EndTurn(...) */ }
    else { var step = session.RunAIStep(); /* animate step (Path / AbilityId / Target) */ }
}
var summary = session.FinishBattle();                  // CombatEnded, XP → LevelUp…, LootOpened
session.TakeAllLoot();
if (session.CannotLeaveCombatReason() == null) session.LeaveCombat();   // training dummy

// level up → talents → trainer
if (session.TalentPointsAvailable(session.Main) > 0) session.LearnTalent(session.Main, "mage_fire_improved_fireball");

// quest reward pick
foreach (var q in session.PendingQuestRewards) ShowPicker(q, session.QuestRewardChoices(q));   // → ClaimQuestReward(q, id)

// rest, save, load
session.LongRest();
if (session.TrySaveGame(out var json, out var why)) File.WriteAllText(path, json); else Toast(why);
if (!session.LoadGame(File.ReadAllText(path), out var err)) Toast(err);
```
