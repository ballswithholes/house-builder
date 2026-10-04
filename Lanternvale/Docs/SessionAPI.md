# Lanternvale — Session API (`Lanternvale.Session`)

`GameSession` is **the single object the Unity game-flow and UI layers talk to**. It owns the party, the shared
inventory and gold, the world state (flags, quests, dialogue memory, map runtimes), the current map's navigation
grid, the current battle, the game clock and the RNG, and it saves/loads all of it as one JSON string.

Pure C# 9 in `Assets/Lanternvale/Scripts/Core/Session/**` (no UnityEngine). Tests:
`Tools/harness/CoreTests/TestsSession*.cs` (`Tools/check.sh core --filter Session`).

It builds on the rules engine (`Lanternvale.Rules`: `Unit`, `Battle`, `AI`, `Inventory`, `ItemInstance`, `VendorShop`,
`Progression`, `TrainerOffer`, `LevelUpInfo`, `AbilityStatus`, `CombatEvent` — see `Docs/CoreAPI.md`) and the world
module (`Lanternvale.World`: `DialogueRunner`, `DialogueView`, `QuestLog`, `MapRuntime`, `NavGrid`, `CheckResult` — see
`Docs/WorldAPI.md`). Those types are handed to the UI as they are; the session never wraps them in copies.

Coordinates are world metres (`Lanternvale.Util.Vec2`, same as `WorldAPI.md`). Money is copper.
Every command that can fail returns `null` on success or a **reason string** for the UI ("Not enough money.").

| File | Contents |
|---|---|
| `GameSession.cs` | construction, `NewGame`, mode, events, settings, queries |
| `GameSession.Party.cs` | party/roster/camp, companions, leader, formation, pets, XP & levels, approval |
| `GameSession.Items.cs` | inventory, equip, item use, loot window, vendors, trainers, talents, respec |
| `GameSession.World.cs` | maps, movement, triggers, interactions, dialogue, `IDialogueContext` |
| `GameSession.Combat.cs` | encounters → `Battle`, AI stepping, leave combat, `FinishBattle` |
| `GameSession.Time.cs` | `Tick`, game clock, out-of-combat regen/cooldowns/auras, resting |
| `GameSession.Save.cs`, `SaveData.cs` | save/load DTOs (versioned) |
| `SessionTypes.cs` | `SessionEvent`, `SessionEventKind`, `SessionMode`, `NewGameOptions`, `SessionSettings`, results |
| `NavGridPathfinder.cs` | `IPathfinder` adapter over `World.NavGrid` used by every battle |
| `StartingGear.cs` | level-appropriate ("veteran") gear selection |

---

## 1. Lifecycle

```csharp
using Lanternvale.Session;

var db = GameDatabase.Load(files);                 // Unity: TextAssets under Resources/Data
var session = new GameSession(db, seed: 0);        // seed 0 = time based
session.EventRaised += OnSessionEvent;             // or poll session.TakeEvents() once per frame

session.NewGame(new NewGameOptions {
    Name = "Aria", Class = ClassId.Paladin,
    StartLevel = 1,                                // 1..60; >1 = "veteran start" (see §3)
    Sprite = "", Portrait = "",                    // appearance overrides ("" = class art)
    VeteranGear = true,                            // StartLevel > 1: level-appropriate gear
});
// or: session.NewGame("Aria", ClassId.Paladin, startLevel: 20);

// every frame
session.Tick(Time.deltaTime);                      // clock, regen, cooldowns, buffs (exploration only)
foreach (var e in session.TakeEvents()) Handle(e); // if you poll instead of subscribing
```

`NewGame` creates the main character, puts the party on `config.startMap` / `config.startSpawn`
(`MapEntered` event) and, when `NewGameOptions.PlayOpening` (default true) and `config.startDialogue` is set,
starts the opening dialogue (`DialogueStarted`). The clock starts at `SessionSettings.StartHour` (18.5 — dusk,
matching the opening scene) on day 1.

### Mode

`session.Mode` (`SessionMode`): `None` (no game yet) · `Exploration` · `Dialogue` · `Combat` · `GameOver`.
Overlays such as the vendor, trainer, respec and loot windows do not change the mode — they are separate state
(`ActiveVendor`, `ActiveTrainer`, `ActiveRespecNpc`, `PendingLoot`), each opened with an event.

| Mode | What the UI shows | Allowed |
|---|---|---|
| `Exploration` | map, party, NPCs, encounters | move, interact, abilities/items (field), inventory, vendors, trainers, rest, save |
| `Dialogue` | dialogue window bound to `session.Dialogue.Current` | `ChooseDialogue`, `ContinueDialogue`, `EndDialogue` |
| `Combat` | battle HUD bound to `session.Battle` | battle actions, AI stepping, `FinishBattle` once `Battle.IsOver`, `LeaveCombat` |
| `GameOver` | "load last save" | `LoadGame`, `NewGame` |

---

## 2. Events

```csharp
event Action<SessionEvent> EventRaised;      // raised synchronously
List<SessionEvent> TakeEvents();             // the same events, queued (cleared by TakeEvents); QueueEvents = false disables the queue
event Action<CombatEvent> CombatEventRaised; // every CombatEvent of the field (exploration) context and of battles
```

`SessionEvent` fields: `Kind`, `Text` (toast-ready, may be ""), `Id`, `Id2`, `Amount`, `Unit`, `Item`
(`ItemInstance`), `Quest` (`World.QuestEvent`), `LevelUp` (`Rules.LevelUpInfo`), `Check` (`World.CheckResult`),
`Battle`, `Loot` (`LootWindow`), `Outcome` (`CombatEndKind`).

| Kind | Meaning / fields |
|---|---|
| `Toast` | generic info line (`Text`) — e.g. "Your bags are full", a sign's text |
| `GameStarted`, `GameLoaded` | after `NewGame` / `LoadGame` (a `MapEntered` follows) |
| `GameOver` | the whole party was defeated (`Mode == GameOver`) |
| `MapEntered` | `Id` map id, `Id2` spawn id ("" after load). Rebuild the `MapView`, spawn views for `PartyUnits()`, `VisibleNpcs()`, `VisibleEncounters()` |
| `RegionEntered` | first entry into a region: `Id` region id, `Text` region text |
| `TransitionLocked` | `Id` transition id, `Text` = its `lockedText` |
| `DialogueStarted` / `DialogueEnded` | `Id` dialogue id, `Id2` owner (npc/companion/encounter id or "") |
| `SkillCheck` | a d20 roll happened (dialogue choice, lock): `Check` (`CheckResult`), `Text` summary |
| `QuestStarted` `QuestUpdated` `QuestCompleted` `QuestFailed` | `Quest` = the World `QuestEvent` (`Text` is toast-ready), `Id` quest id |
| `QuestRewardChoice` | `Id` quest id: show the pick-one dialog (`QuestRewardChoices(id)` → `ClaimQuestReward`) |
| `ItemReceived` / `ItemLost` | `Id` item id, `Amount` count, `Item` (instance when known), `Text` "Received: Kindling Taper" |
| `GoldChanged` | `Amount` delta (copper), `Text` "+1s 20c" |
| `XpGained` | `Amount` xp, `Text` |
| `LevelUp` | `Unit`, `Amount` new level, `LevelUp` (`LevelUpInfo`: health/mana gained, talent points, `NewTrainable`), `Text` |
| `AbilityLearned` | `Unit`, `Id` ability id, `Amount` rank (training, talents, auto-trained companions) |
| `TalentPointsAvailable` | `Unit`, `Amount` unspent points |
| `CompanionRecruited` | `Id` companion id, `Unit`, `Text` ("Kael joins the party." / "… waits at camp (party full).") |
| `CompanionDismissed` | `Id`, `Unit` |
| `PartyChanged` | active party / camp changed (rebuild portraits, spawn/despawn views) |
| `LeaderChanged` | `Unit` = new leader |
| `ApprovalChanged` | `Id` companion id, `Amount` delta, `Text` "Kael approves." |
| `PetChanged` | `Unit` = owner (its `Pet` appeared, died or was dismissed) |
| `VendorOpened`/`VendorClosed`, `TrainerOpened`/`TrainerClosed`, `RespecOpened`/`RespecClosed` | `Id` npc id |
| `LootOpened` / `LootClosed` | `Loot` (`LootWindow`) |
| `ChestOpened` | `Id` chest id (`MapView.SetChestOpen`) |
| `CombatStarted` | `Battle`, `Id` encounter id, `Text` the first creature bark |
| `CombatEnded` | `Outcome` (`Victory`, `Defeat`, `Left`), `Id` encounter id |
| `Rested` | long rest finished (`Text`) |
| `PartyHealed` | `HealParty` outcome |
| `SpecialOutcome` | data `Special` outcome / session special: `Id` special id. `RekindleLanterns`: `Amount` 1 = story moment (animate `MapView.SetLanternLit(id, true, animate: true)` for every lantern), 0 = silent re-apply after a map load |
| `TimeOfDayChanged` | `Text` new phase (`dawn`/`day`/`dusk`/`night`) |

---

## 3. Characters, party, companions

```csharp
Unit Main                          // main character (member id "player")
IReadOnlyList<Unit> Party          // active characters, Party[0] == Main, max config.partySize
IReadOnlyList<Unit> Roster         // Main + every companion ever recruited (active, camp or away)
List<Unit> Camp()                  // recruited (flag recruited_<id> set) but not in the active party
List<Unit> PartyUnits()            // active characters + their persistent pets (spawn a UnitView for each)
Unit Leader; string SetLeader(Unit u)            // leader = the unit the camera follows / the player moves
string MemberId(Unit u)            // "player" | companion id | "" (pets: "")
Unit FindMember(string memberId)   // in the roster
CompanionStatus CompanionStatusOf(string companionId)   // NotRecruited | Active | Camp | Away (dismissed)
string SetPartyMemberActive(string companionId, bool active)   // swap camp <-> party (out of combat)
int PartyLevel                     // Main.Level (companions are level-synced)
int GetApproval(string companionId); void ChangeApproval(string companionId, int delta)
void SetAutoPlay(Unit u, bool on)  // companion AI plays this unit in combat (Settings.CompanionAutoPlay = default for recruits)
```

**New game.** `UnitFactory.CreateCharacter` + class starting abilities/items/stance; `config.startingItems` go to
the bags, `config.startingGold` to the purse. **Veteran start** (`StartLevel > 1`): level set, every trainable
rank learned, talents auto-allocated from `ClassDef.defaultBuild` (level ≥ 10), then (with `VeteranGear`) one
level-appropriate piece per slot — the best class-usable database item by quality/item level, or a generated
"Veteran's …" green/blue (`ItemGenerator.VeteranGear`) when the database has nothing close to the level — and
extra gold (`25 × level²` copper).

**Companions.** `Recruit(id)` (dialogue outcome) creates the companion at the party level from its `CompanionDef`
(class, `statBonus`, starting items — leftovers go to the bags — all trainable ranks, `preferredTalents` /
`defaultBuild` talents, veteran gear when the game was a veteran start), sets flag `recruited_<id>` (hides its map
NPC) and adds it to the active party, or to camp when the party is full. Recruiting a camp/away companion again
brings back the same unit (gear, approval kept). `Dismiss(id)` removes it from the party and clears
`recruited_<id>` (its NPC reappears at its map spot; status `Away`). Flags `<id>_met` are set by dialogues.
Companions are **level-synced**: they level with the main character (`Settings.CompanionAutoTrain`: learn new
ranks for free; `Settings.AutoAllocateCompanionTalents`: spend new points on their build).

**Pets.** Hunter pets (Call Pet) and warlock demons are summoned by abilities (field or battle) and stay as the
owner's `Unit.Pet` across fights, map changes and saves; they appear in `PartyUnits()`, follow their owner and join
every battle. Dead demons are gone (summon again); a dead hunter pet stays dead (`Unit.HunterPet.Dead`) until
Revive Pet or a long rest.

**XP.** `GiveXP(raw)` (dialogue/quest data amount) applies `config.xpRate` (`Progression.QuestXp`); battles give
the main character's `BattleResult` XP (rate already applied). The main character gains it (`Progression.GiveXp`)
and every companion is set to the same level → `LevelUp` events (one per level and character), `TalentPointsAvailable`.

---

## 4. Items, loot, vendors, trainers, talents

```csharp
Inventory Inventory                // shared bags (Rules) — Items, Count(id); do not Add/Remove directly, use the session
int Gold                           // copper
int CountItem(string itemId)       // bags
void GiveItem(string itemId, int count) / TakeItem / GiveGold(int) / TakeGold(int)   // also used by dialogue outcomes

string CanEquip(Unit u, ItemInstance item, EquipSlot? slot = null)   // reason or null
string Equip(Unit u, ItemInstance item, EquipSlot? slot = null)      // from the bags; displaced items return to the bags
string Unequip(Unit u, EquipSlot slot)
string DestroyItem(ItemInstance item, int count = 1)

string CannotUseItemReason(Unit user, ItemInstance item, Unit target = null)
ActionResult UseItem(Unit user, ItemInstance item, Unit target = null, Vec2? point = null)
   // in combat: the active unit only (Battle.UseItem); out of combat: field context.
   // Food/Drink (requires notInCombat) only out of combat; potions share cooldownGroup "potion".

LootWindow PendingLoot             // { Source, Title, Items (List<ItemInstance>), Gold } or null
string TakeLoot(ItemInstance item); void TakeAllLoot(); void CloseLoot(bool takeAll = false)
   // gold is added as soon as the window opens; items left in a closed window are lost

// vendors (NpcDef.vendor; stock and buyback persist per vendor in saves)
VendorShop ActiveVendor            // Rules VendorShop: Offers(), Buyback, Npc
void OpenVendor(string npcId); void CloseVendor()
string Buy(string itemId, int count = 1); string Sell(ItemInstance item, int count = 1); string BuyBack(ItemInstance item)

// trainers (NpcDef.trains)
NpcDef ActiveTrainer
void OpenTrainer(string npcId); void CloseTrainer()
bool CanTrainHere(Unit u)                          // trainer teaches u's class
List<TrainerOffer> TrainerOffers(Unit u)           // Progression.TrainerOffers (gold checked)
string Train(Unit u, string abilityId, int rank = 0)   // 0 = highest rank available now (lower ranks paid too)
int TrainAll(Unit u)                               // every affordable rank; returns how many

// talents & respec
int TalentPointsAvailable(Unit u)
string LearnTalent(Unit u, string talentId)        // one point (Progression.CannotLearnTalent reasons)
int AutoAllocateTalents(Unit u)                    // preferredTalents / defaultBuild
string ActiveRespecNpc; void OpenRespec(string npcId); void CloseRespec()
int RespecCost(Unit u); string Respec(Unit u)      // pays gold, resets talents (then LearnTalent / AutoAllocateTalents)
```

Abilities out of combat (buffs, summons, Call Pet, food via items) and in combat go through one entry point:

```csharp
List<AbilityStatus> GetAbilityBar(Unit u)          // Battle.GetAbilityBar of the battle or the field context
ActionResult UseAbility(Unit u, string abilityId, Unit target = null, Vec2? point = null)
Battle Field                                       // exploration context (InCombat = false) holding PartyUnits()
```

---

## 5. Maps, movement, interaction

```csharp
string MapId; MapDef MapDef; MapRuntime Map; NavGrid Nav; IPathfinder Pathfinder
void EnterMap(string mapId, string spawnId = "default")      // travel (also Teleport outcome / transitions)
List<MapNpcDef> VisibleNpcs()                                // requireFlag/hideFlag applied (companions hide when recruited)
List<EncounterDef> VisibleEncounters()                       // spawn enemy views for these (MapRuntime.IsEncounterVisible)
string NpcName(string npcOrCompanionId); string DialogueOf(string npcOrCompanionId)
bool LanternsRekindled                                       // flag lanterns_rekindled

// movement — the session is authoritative for positions (Unit.Position), the Unity layer animates
NavPath FindPath(Vec2 from, Vec2 to)                         // exploration agent (ignores units)
PartyMovePlan PlanPartyMove(Vec2 destination)                // leader path + each follower's formation slot and path
TriggerResult UpdatePartyPositions(Vec2 leaderPos, IReadOnlyList<Vec2> others = null)
   // call while animating (every frame or every ~0.25 m); others = the other PartyUnits() in PartyUnits() order
   // (null: they snap to their formation slots). Runs region entries, encounter triggers (stealth rule) and
   // transitions; Stop == true → stop animating (dialogue/combat started, map changed, transition locked).
MoveResult MoveLeader(Vec2 destination)                      // instant version (tests, click-to-teleport, fast travel)
List<Vec2> FormationSlots(Vec2 leaderPos, Vec2 facing, int count)
```

Encounter triggers: the first available encounter with an active party member inside its radius; stealthed
members only count within 3 m (`MapRuntime.FindTriggeredEncounter`). Encounters with a `dialogue` play it first
(`DialogueStarted`, owner = encounter id): peaceful branches set the encounter's done flag; fight branches use
`StartCombat <encounterId>` (`StartCombat("")` = the encounter whose dialogue is running). An encounter whose
dialogue ended without a fight or a done flag does not re-trigger until the party has left its radius. Combat is
never started for an encounter that is already done.

```csharp
InteractResult TalkTo(string npcId)                 // npc or companion map NPC → its dialogue (owner = npcId)
InteractResult OpenChest(string chestId)            // loot window (lootTable rolled at party level + items + gold)
                                                    // Kind == Locked: offer TryUnlockChest / PickLock
CheckResult TryUnlockChest(string chestId)          // best party member's SleightOfHand check; success opens it
CheckResult PickLock(string chestId, Unit rogue = null)  // rogue with Pick Lock: + floor(level/5); success opens it
InteractResult UseTransition(string transitionId)   // travel or TransitionLocked
string InspectProp(string interactId)               // sign/shrine text (also raised as Toast)
float InteractionRange (2.5 m) ; bool InInteractionRange(Vec2 p)   // the session does not enforce range
```

`InteractResult`: `Ok`, `Kind` (`None`, `Dialogue`, `Loot`, `Locked`, `Travel`, `Text`), `Message`, `Id`.

---

## 6. Dialogue

```csharp
DialogueRunner Dialogue                         // World runner; Dialogue.Current is the DialogueView to show
bool StartDialogue(string dialogueId, string ownerId = "")
bool ChooseDialogue(int index)                  // index into Dialogue.Current.Choices
bool ContinueDialogue()                         // when Current.CanContinue
void EndDialogue()
```

Outcomes run through the session's `IDialogueContext`: flags/quests (world), items/gold (bags), `GiveXP` (rate
applied), `Recruit`/`Dismiss`, `Approval`, `StartCombat` (starts the encounter after the dialogue closes),
`OpenVendor`/`OpenTrainer`/`OpenRespec` (open the windows after the dialogue), `Rest` (long rest), `HealParty`,
`Teleport` (`EnterMap`), `Special` (`RekindleLanterns`: sets `lanterns_rekindled`, raises `SpecialOutcome`).
Skill checks use the active party's effective primary stats (`Unit.Stats`) + class proficiency (Design §6); the
best member rolls. The clock is paused while talking.

---

## 7. Combat

```csharp
Battle Battle                       // current battle (null when exploring); keep it until FinishBattle
EncounterDef BattleEncounter
Battle StartEncounter(string encounterId, SurpriseMode surprise = SurpriseMode.None)
   // enemies from EncounterDef (CreatureScaling: scaleToParty + levelOffset), party + pets, NavGrid pathfinder,
   // Battle.Begin(surprised team). Null when unknown / done / unavailable (reason in LastError).
Battle EngageEncounter(string encounterId)   // the player attacks first: enemies surprised if the leader is stealthed
bool IsPlayerTurn                   // Battle.NeedsPlayerInput
AIStep RunAIStep()                  // one step for the active AI-controlled unit (enemy, pet, auto-played companion)
int RunAITurn()                     // the rest of its turn
BattleOutcome AutoResolve(int maxRounds = 60)   // AI plays everyone (tests, "auto battle")
string CannotLeaveCombatReason()    // null when every remaining hostile is Passive (training dummy)
bool LeaveCombat()                  // ends that fight without marking the encounter done (it can be fought again)
BattleSummary FinishBattle()        // call once Battle.IsOver
void ResetEncounter(string encounterId)
```

Player actions during the battle go straight to the `Battle` (`UseAbility`, `UseItem`, `Move`, `EndTurn`, …)
or through `session.UseAbility/UseItem`. Animate `Battle.Events` (`TakeEvents(ref cursor)`) or
`CombatEventRaised`.

`FinishBattle()` → `BattleSummary` { `Outcome` (`Victory`/`Defeat`/`Left`), `EncounterId`, `Xp`, `LevelUps`,
`Loot`, `Killed` (creature ids), `Rounds` }:
* **Victory**: XP, quest kill notifications, the encounter's done flag (Defeat objectives), loot window
  (`LootOpened`, gold added), downed/dead allies get up at 1 HP, dead pets cleaned up. Practice encounters
  (every enemy Passive, e.g. `enc_training_dummy`) are reset instead of marked done.
* **Defeat**: `Mode = GameOver`, `GameOver` event.
* After `LeaveCombat` the summary is returned by `LeaveCombat`'s `CombatEnded` event as well (`Outcome = Left`).

---

## 8. Time & resting

```csharp
void Tick(float realSeconds)        // Exploration only: clock, out-of-combat regen, cooldowns, auras (food/drink ticks)
float GameHour; int Day; string TimeOfDay   // phase per DayNight: dawn [5,8) day [8,17.5) dusk [17.5,20.5) night
                                    // maps without ambient.dayNightCycle report their fixed ambient.timeOfDay
float PlaySeconds
string CannotRestReason(); bool LongRest()  // restArea maps (or the Rest dialogue outcome at an inn)
void Rest(); void HealParty()       // IDialogueContext outcomes
```

`Settings.GameHoursPerRealMinute` (default 1 → `DayNight.HoursPerSecond = 1/60`). Push `session.GameHour` into
`DayNight.WorldHour` each frame (the session is the clock; don't let DayNight run its own). Moving cancels
Food/Drink auras ("must remain seated"). A long rest restores health/mana/energy/focus, clears cooldowns and
debuffs, revives a dead hunter pet, and advances the clock to 08:00 the next morning.

---

## 9. Save / load

```csharp
string CannotSaveReason()                  // null, or "Cannot save during combat." / "...during a conversation."
string SaveGame()                          // JSON (throws InvalidOperationException when not allowed)
bool TrySaveGame(out string json, out string reason)
bool LoadGame(string json, out string error)   // replaces this session's state; raises GameLoaded + MapEntered
static SaveHeader ReadSaveHeader(string json)  // for save-slot lists: name, class, level, map, day/hour, play time
```

Everything needed to restore the session exactly: party units (class/companion ids, level, xp, name, art,
health and resources, abilities + ranks, talents, respec count, equipment item instances incl. random suffix
stats and generated item definitions, auras with remaining time, cooldowns, pet + hunter pet state, positions,
auto-play), roster/active party/leader, approvals, bags and gold, vendor stock + buyback, `WorldSaveData`
(flags, quests, dialogue memory, map runtimes), map id, game time, RNG state, pending loot, settings.
`SessionSaveData.version` = `GameSession.SaveVersion` (1). Save → load → save produces identical JSON.
The Unity layer owns files (`Application.persistentDataPath`).

---

## 10. Typical flows

```csharp
// new game → opening dialogue
session.NewGame("Aria", ClassId.Mage);
while (session.Mode == SessionMode.Dialogue)
{
    var v = session.Dialogue.Current;                  // show v.SpeakerName, v.Text, v.Choices[i].DisplayText
    if (v.CanContinue) session.ContinueDialogue(); else session.ChooseDialogue(0);
}

// explore: click on the ground
var plan = session.PlanPartyMove(click);              // animate plan.LeaderPath, plan.Followers[i].Path
// ... each frame while animating:
var tr = session.UpdatePartyPositions(leaderView.Position, followerPositions);
if (tr.Stop) StopAnimating();                         // dialogue / combat / travel happened (events tell which)

// talk / trade / train
session.TalkTo("elder_maru");                         // DialogueStarted
session.TalkTo("merchant_tilly"); /* choose "trade" → VendorOpened */ session.Buy("potion_minor_healing", 2);
session.TalkTo("trainer_quillon"); /* → TrainerOpened */ session.Train(session.Main, "mage_frostbolt");

// combat
session.StartEncounter("enc_pasture_wolves");        // or triggered by walking in
while (!session.Battle.IsOver)
{
    if (session.IsPlayerTurn) { /* wait for input → session.Battle.UseAbility(...) / EndTurn */ }
    else { var step = session.RunAIStep(); /* animate step */ }
}
var summary = session.FinishBattle();                  // XP, LevelUp events, LootOpened
session.TakeAllLoot();

// level up → spend talent points → train
if (session.TalentPointsAvailable(session.Main) > 0) session.LearnTalent(session.Main, "mage_fire_improved_fireball");

// save / load
if (session.TrySaveGame(out var json, out var why)) File.WriteAllText(path, json);
session.LoadGame(File.ReadAllText(path), out var err);
```
