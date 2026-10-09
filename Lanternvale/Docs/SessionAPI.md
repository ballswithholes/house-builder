# Lanternvale — Session API (`Lanternvale.Session`)

`GameSession` is **the single object the Unity game-flow and UI layers talk to**. It owns the party, the shared
bags and gold, the world state (flags, quests, dialogue memory, map runtimes), the current map's navigation grid,
the exploration ("field") rules context, the current battle, the game clock and the RNG — and it saves/loads all
of it as one JSON string.

Pure C# 9 in `Assets/Lanternvale/Scripts/Core/Session/**` (no UnityEngine). Tests:
`Tools/harness/CoreTests/TestsSession*.cs` (`Tools/check.sh core --filter Session`). **Full-game playthrough**:
`TestsSessionFullPlaythrough.cs` plays the whole slice through this API for every class — level 1 `NewGame` with the
opening, every main-quest stage to the Hollow Warden and the `RekindleLanterns` outcome, back to Elder Maru, and every
side quest (routes varied per class: fight / intimidate / pay / paladin's word; Puddlecap fought or befriended by a shaman;
Keeper Ishiro calmed by the letter or a priest, or fought), recruiting companions by dialogue, walking through
transitions, triggering encounters by walking, fights won by the party AI (`AutoPlay` on everyone), training, talents,
loot, upgrades, quest rewards and rests. `--sim` adds `FullPlaythrough_WinRates` (many seeds per class; where runs fail)
and `Warden_Replay` (the boss fight replayed from a save).

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
| `GameSession.Raid.cs` | raids (gate, `EnterRaid`, leaving, wipe, raid save state), the battle formation, the encounter health scale |
| `GameSession.Save.cs`, `SaveData.cs` | save/load and the versioned DTOs |
| `SessionTypes.cs` | `SessionMode`, `SessionEvent(Kind)`, `NewGameOptions`, `SessionSettings`, `LootWindow`, `BattleSummary`, `InteractResult`, `TriggerResult`, `MoveResult`, `PartyMovePlan`, `EncounterEnemyPreview`, `SaveHeader` |
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

`int FlagsVersion` — incremented on every story-flag change (and when `NewGame`/`LoadGame` replace the flags): cache
anything derived from flags and rebuild it when the number differs (cheaper than listening to every change).

`int QuestMarkersVersion` — moves whenever a quest marker may have changed: quest progress, flags, bags, gold, dialogue
choices, level, party, approval, time of day, map, dialogue end, new game and **load** (QuestLog.Load raises nothing).
It never touches `FlagsVersion` (that would rebuild the whole world view on every kill). Poll it; see "Quest markers".

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
| `Toast` | info line in `Text` (NPC bark, sign text, "The chest is empty.", an opener that failed …). A **set bonus** that switches on after an equip is a Toast with `Id` `set_complete` (`GameSession.SetCompleteToastId`), `Id2` the set id, `Amount` the pieces of the highest newly active bonus, `Unit` the wearer, `Text` "Bruna: Rootwall Battlegear (4/5) set bonus active" (§4) |
| `GameStarted` / `GameLoaded` | after `NewGame` / `LoadGame` (a `MapEntered` follows) |
| `FlagsChanged` | story flags changed — `Amount` = `FlagsVersion`; raised **at most once per `Tick` / dialogue step** (also after a battle, `NewGame`, `LoadGame`), however many flags changed. Rebuild flag-dependent views (visible NPCs, chests, props, journal) |
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
| `SpecialOutcome` | `Id` special id (`Id2` argument). **`RekindleLanterns`**: `Amount` 1 = the story moment (`MapView.SetLanternLit(id, true, animate: true)` for every lantern + banner `Text`); `Amount` 0 = raised after every `MapEntered` while flag `lanterns_rekindled` is set and the map is in the valley (`IsRekindleMap`: lanternvale, whisperwood, shrine; relight silently) |
| `TimeOfDayChanged` | `Id`/`Text` new phase (`dawn` `day` `dusk` `night`) |
| `SecretFound` | a hidden transition (any map) became visible: `Id` its `revealFlag`, `Id2` the transition id, `Text` "You discovered a hidden passage: <label, else the target map's name>". Once per passage per game, whatever set the flag (region check, dialogue, NPC hint); never on `LoadGame` for passages the save already knew (§5 Discovery) |
| `RaidPartyRequested` | the party tried to travel into a raid map without a raid party (walking, clicking the exit, `EnterMap`, a dialogue `Teleport` once the conversation ends): `Id` raid map, `Id2` spawn, `Amount` its `raidSize` — open the raid picker, answer with `EnterRaid(Id, Id2, …)`. Nobody travelled (§3 "Raids") |
| `RaidStarted` | `EnterRaid` formed the raid and arrived (after `MapEntered`): `Id` map, `Amount` raid size, `Text` |
| `RaidEnded` | the raid party was dissolved on arrival in a non-raid map (after `MapEntered`): `Id` the raid map left, `Amount` 1 after a wipe, `Text` |

---

## 3. Characters, party, companions, pets

```csharp
Unit Main                              // main character, member id "player" (GameSession.MainId)
IReadOnlyList<Unit> Party              // active characters, Party[0] == Main, at most PartySize (config.partySize; the raid's size in a raid)
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

**Raids.** A map with `MapDef.raidSize > 0` is a raid (10 players). It is entered only with a raid party:

```csharp
bool InRaid; int RaidSize                        // a raid party is active (0 when not)
int MaxPartySizeOn(string mapId)                 // the map's raidSize for a raid map, else config.partySize
List<Unit> RaidCandidates()                      // Main, the active party in its order, then the camp in roster order (never Away)
string CannotEnterRaidReason(string mapId, IReadOnlyList<string> ids)   // null = OK (member ids, Main included)
string EnterRaid(string mapId, string spawnId, IReadOnlyList<string> ids, bool companionsAutoPlay)   // null = entered
```

* **The gate.** Travelling to a raid map without a raid party — walking into its exit (`CheckTriggers` returns
  `Stop`, `Kind = RaidGate`, `Id` = the map), clicking it (`UseTransition`: `Ok`, `Kind = None`), `EnterMap`, or a
  dialogue `Teleport` (deferred until the conversation ends, like any teleport) — raises `RaidPartyRequested` and
  does **not** travel. The leader stays in the exit rectangle, so walking in again asks again only after stepping
  out. The picker answers with `EnterRaid(e.Id, e.Id2, ids, autoPlay)`.
* **Forming.** `CannotEnterRaidReason` refuses in combat, in a conversation, after game over, for an unknown or
  non-raid map, while a raid is already formed, for no ids, without Main, for an unknown, duplicated or Away
  member, and for more than `raidSize` ids. `EnterRaid` remembers the party (order), leader and every recruited
  character's auto-play, makes the party Main + the chosen ids in their order (camp companions join, unchosen
  members wait at camp), keeps the leader when chosen (else Main), turns auto-play on for the companions when
  asked (the picker's default), travels (`MapEntered`) and raises `RaidStarted` + `PartyChanged`. Main is never
  switched to auto-play. `PartySize` is the raid's size meanwhile: `SetPartyMemberActive` and `Recruit` fill the
  raid up to it (a recruit's own auto-play is the one it keeps afterwards). Moving between two raid maps keeps the
  raid.
* **Leaving.** Arriving in a non-raid map (exit, `EnterMap`, teleport, a wipe) restores the party from before the
  raid — its members in order, minus companions dismissed meanwhile (Away), at most `config.partySize` — its leader
  (else Main) and every recruited character's auto-play; raid-only members go back to camp. `RaidEnded` +
  `PartyChanged` follow `MapEntered`.
* **Wipe.** A defeat on a raid map is not game over: `CombatEnded` (`Defeat`) is raised, the encounter is reset (no
  lockouts), the party travels to `raidReturnMap`/`raidReturnSpawn` (the raid ends: `RaidEnded`, `Amount` 1), every
  recruited character is revived and fully restored like a long rest without the clock (dead hunter pets too), and
  `PartyHealed` carries the notice ("The raid has wiped…"). `BattleSummary.Outcome` is `Defeat`, no XP, no loot.
* **Saves** keep the raid (`SessionSaveData.raid`: size, the normal party in order, its leader, the sorted ids
  with auto-play on; null when not in a raid). A raid saved on a map that is no longer a raid map ends on load.

**`RaidPlanning`** (`Session/RaidPlanning.cs`, static, pure — it changes no session state) holds the rules the raid UI
shares with the tests:

```csharp
const int BigBattleUnits = 14; bool IsBigBattle(int units) / IsBigBattle(Battle b)   // > 14 units, every side, pets and totems:
                                                  // compact turn strip, Auto toggles, AI pacing halved (CombatFlow.md)
RoleCounts CountRoles(IEnumerable<Unit> | IEnumerable<UnitRole>)   // Tanks, Healers, Melee, Ranged, Dps, Total (characters only)
string RoleSummary(RoleCounts c)                  // "2 tanks · 2 healers · 6 dps"
int WantedTanks(int size) / WantedHealers(int size)   // one tank per five, one healer per four (at least one from 2 up)
string RoleAdvice(RoleCounts c)                   // advice when 3+ chosen have no tank or no healer, else null
List<string> DefaultSelection(GameSession s, IReadOnlyList<Unit> candidates, int size, Func<Unit,UnitRole> roleOf = null)
                                                  // the picker's suggestion: Main, the travelling party in order, then camp
                                                  // companions — missing tanks and healers first, then roster order
string LevelWarning(int level, int levelMin)      // "Level 18 · the raid asks for 21", or null
string BandText(MapDef m)                         // "levels 21–23" / "level 21+" / ""
const string WipeNotice = "The raid has wiped."; bool WipeSendsHome(GameDatabase db, MapDef map); string WipeDetail(string text)
                                                  // the wipe rule FinishBattle applies, and the toast's second line
AutoPlaySnapshot.Capture(units) → Restore(set[, current, fallback]) / Matches() / FlagOf(u)
                                                  // what the "Auto: all companions" / "Auto-battle" toggles put back
```

**Pets.** Hunter pets (Call Pet) and warlock demons are summoned with abilities (in the field or in battle) and stay
as the owner's `Unit.Pet`: listed in `PartyUnits()`, following in formation, joining every battle right after their
owner, kept across maps and saves. A pet that dies is removed after the battle (`PetChanged`); a hunter's dead pet
stays dead (`Unit.HunterPet.Dead`) until Revive Pet or a long rest. Totems and temporary guardians placed out of
combat join the next battle and are cleared on map change.

**XP.** `GiveXP(raw)` (dialogue outcome / quest reward amount) applies the XP rate of the content's level, never
above the main character's (`Progression.RateLevel`): a quest's stage outcomes and reward at `Progression.QuestLevel`
(`QuestLog.Paying` names the quest being paid), anything else at the current map's band bottom; battles give the main
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
const string SetCompleteToastId = "set_complete"

// abilities & items — combat: the active unit through the Battle; exploration: the Field context
List<AbilityStatus> GetAbilityBar(Unit u, bool includeTooltips = true)   // false: Tooltip = "" (frequent HUD refreshes)
   // AbilityStatus.KnownRanks / CanDownrank (rank picker), InRangeOfAttackTarget (bool?, tinting) — Docs/CoreAPI.md §2
ActionResult UseAbility(Unit u, string abilityId, Unit target = null, Vec2? point = null, int rank = 0)
   // buffs, Call Pet, Stealth…; rank 0 = highest known, 1..known = WoW downranking (cheaper, weaker)
string CannotUseItemReason(Unit user, ItemInstance item, Unit target = null)  // Battle.CanUseItem: level/class restriction first, then the use ability
ActionResult UseItem(Unit user, ItemInstance item, Unit target = null, Vec2? point = null)
Battle Field                                   // exploration context (InCombat = false) of PartyUnits()
```

**Set bonuses** (`ItemSets`, CoreAPI.md §6). After a successful `Equip`, every set whose bonus tier rose for that
unit (a bonus became active that was not before) raises one `Toast` with `Id` = `set_complete`, `Id2` = the set id,
`Amount` = the `pieces` of the highest newly active bonus, `Unit` = the wearer and `Text` = "<name>: <set name>
(<worn>/<total>) set bonus active". Losing a tier (unequip, swap) is silent, and loading a save or a veteran start never
announces. The Game layer plays `set_complete` for it (GameFlow.md).

Food/drink (`requires.notInCombat`) only work out of combat and are cancelled when their eater moves ("must remain
seated"); potions share `cooldownGroup` `potion`; cooldowns keep running in real time out of combat.

```csharp
LootWindow PendingLoot                         // { Source ("battle" | chest id), Title, Items, Gold } or null
string TakeLoot(ItemInstance item); void TakeAllLoot(); void CloseLoot(bool takeAll = false)
// gold is added when the window opens; items left in a closed window are lost (travel/combat take them all)
LootContext BuildLootContext()                 // the loot context of one fight or one chest (CoreAPI.md §6)
```

`BuildLootContext()` makes the `LootContext` the expansion's loot keys read (`pool`, `perMembers`, `partyUsable`,
`skipOwned`; DataSchema.md): the members are the **active party's characters** (a raid of ten rolls a `perMembers: 5`
entry twice), and an item counts as owned when it is in the bags, in the open loot window (`PendingLoot`) or worn by
anyone in the roster (camp included). The session builds one per battle (`Battle.LootContext`, set at battle creation,
so two bosses of one fight never drop the same pool item twice) and one per chest. Its `QuestNeed` (for
`whileQuestNeeds` quest-item entries) is `Quests.CollectNeed(id)` minus the bags and the open loot window. Tables
without the new keys roll exactly as before.

```csharp
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
List<EncounterEnemyPreview> PreviewEncounter(string encounterId)
   // exploration nameplates: { CreatureId, Name, Level (as the battle will scale it now), MinLevel, MaxLevel, Rank
   // (Normal/Elite/Rare/Boss/Minion/Critter), Type, Position, Passive } per enemy; current map first, else any map;
   // draws no random numbers; empty when unknown
List<Unit> OwnedSummons()                                   // living totems/temporary guardians of the party (not pets):
                                                            // spawn views for them; they join the next battle (which despawns
                                                            // them when it ends, so this is empty after CombatEnded); cleared on map change
string NpcName(string id); string DialogueOf(string id)     // npcs and companions
bool LanternsRekindled                                      // flag lanterns_rekindled (also see SpecialOutcome)
bool LanternsLitHere                                        // LanternsRekindled and IsRekindleMap(MapDef)
static bool IsRekindleMap(MapDef def)                       // the valley the relight covers (RekindleMapIds)
```

The session is authoritative for positions (`Unit.Position`/`Facing`); the Unity layer animates.

```csharp
NavPath FindPath(Vec2 from, Vec2 to)                         // exploration agent (units never block)
PartyMovePlan PlanPartyMove(Vec2 destination)
   // { Leader, LeaderPath (List<Vec2>), Length, Reachable, Followers: [{ Unit, Destination, Path }] } — changes nothing
TriggerResult UpdatePartyPositions(Vec2 leaderPos, IReadOnlyList<Vec2> others = null)
   // report positions while animating (whenever the leader moved ~0.25 m): others = the other PartyUnits() in order,
   // null = followers snap to formation slots. Then runs CheckTriggers().
string SetPartyPositions(Vec2 leaderPos, IReadOnlyList<Vec2> others = null)
   // the same placement WITHOUT any trigger (regions, encounters, transitions): scripted placement, snapping views after a
   // cutscene/load. Null = OK; refused during combat. A leader placed inside a transition must step out before it travels.
TriggerResult CheckTriggers()      // region first entries, encounter triggers, transitions
   // TriggerResult { Stop, Kind (None | Dialogue | Combat | Travel | Locked | RaidGate), Id } — Stop: stop animating
   // (RaidGate: a raid map's exit without a raid party, Id = the raid map; see §3 "Raids")
MoveResult MoveLeader(Vec2 destination)   // instant walk in 0.25 m steps (tests, fast travel); stops at a trigger
List<Vec2> FormationSlots(Vec2 leaderPos, Vec2 facing, int count)   // GameSession.FormationOffsets (14 slots) behind the leader
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
InteractResult UseTransition(string transitionId)          // Kind Travel, or Kind Locked + TransitionLocked event, or
                                                           // (raid exit without a raid party) Ok + Kind None + RaidPartyRequested;
                                                           // a hidden, unrevealed one fails: "There is no way through here."
InteractResult InteractProp(string interactId)  // a prop with PropDef.dialogue starts it (owner = the interact id,
                                                // exploration only) → Kind Dialogue; else its text (Toast) → Kind Text,
                                                // Message = text ("" = none: the UI says "Nothing of note."); a flag-hidden
                                                // or unknown prop fails
string InspectProp(string interactId)           // sign/shrine text (also a Toast); starts a prop's dialogue and returns ""
const float InteractionRange = 2.5f; bool InInteractionRange(Vec2 p)   // the UI walks there first; not enforced
```

`InteractResult`: `Ok`, `Kind` (`None` `Dialogue` `Loot` `Locked` `Travel` `Text`), `Message`, `Id`.

**Discovery** (`GameSession.Discovery.cs`, Docs/Expansion.md §2.2):

* **Region checks.** A region with a `check` rolls it the first time the leader stands inside it while its
  `requireFlag` holds (map arrival or any position update; once per save, `MapRuntimeState.checkedRegions`). The best
  party member rolls (`SkillChecks.Roll`); the session raises `SkillCheck` (`Id` = region id) and toasts `successText`
  or `failText`; success sets `checkFlag`. A region whose `checkFlag` already holds is marked checked without a roll.
* **Hidden passages.** Every flag change re-tests the hidden transitions of every map; one that became visible raises
  `SecretFound` (after the roll and its text, when a region check revealed it). Passages already visible when a game
  starts or loads are not announced. A passage revealed under the leader's feet does not travel until the leader
  steps out and back in. `bool IsPassageRevealed(mapId, transitionId)`; `string SecretFoundText(TransitionDef t)`;
  `const string SecretFoundPrefix`.
* **Navigation.** `Nav` honours prop `requireFlag`/`hideFlag` and chest `requireFlag`. When a flag change makes one
  appear or vanish out of combat, `Nav` is rebuilt in place (the **same** instance, `Nav.Version++`); during a battle
  the change waits until the battle ends (or the next position update). `MapDef.water` blocks movement (except at
  its crossings).

**Quest markers** (`GameSession.Markers.cs`; rules in WorldAPI §5 "Quest markers"), cached per `QuestMarkersVersion`:

```csharp
QuestMarkerIndex QuestMarkerIndex                           // static hand-in index of this database
QuestMarkerInfo QuestMarkerOf(string npcId)                 // the NPC's best marker (Kind None: nothing); any map
IReadOnlyList<QuestMarkerInfo> QuestMarkersOf(string npcId) // all of them, best first; empty for recruited companions
IReadOnlyList<QuestMarkerInfo> QuestMarkersOnMap()          // non-None markers of VisibleNpcs() on the current map
QuestMarkerInfo QuestTurnInOf(string questId)               // where an active quest goes next: ReadyToTurnIn (hand it
                                                            // in now: "» Return to …"), InProgress (its hand-in NPC
                                                            // waits for later steps) or None
IReadOnlyList<QuestMapHint> QuestHintsOnMap()               // visible encounters with a Kill/Defeat target or whose
                                                            // done flag is a Flag target, Reach regions and exits,
                                                            // unopened chests with a Collect item
```

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
   // starts the fight now (encounter dialogue skipped). With an opener (Charge, Cheap Shot, Ambush, Pyroblast…) the
   // attacker acts first: it is used on enemy targetIndex (EncounterDef.enemies order) via Battle.BeginWithOpener, and
   // an Opener from stealth surprises the enemies. Without one there is no first strike: Battle.Begin rolls normal
   // initiative (d20 + Agility) for everyone, and the enemies are surprised only when the attacker (default leader)
   // is stealthed.
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
placeholder enemy views on `CombatStarted`. **Encounter health scale**: on ordinary maps every enemy's health is
× `GameSession.EncounterHealthScale(n, false)` = `1 + 0.2 · max(0, n − 4)` for the n party characters joining (×1.2
for a party of 5); raid maps use ×1 (raid creatures are tuned in data). **Battle formation**: the party (characters
and their pets; totems and summons stay put) steps into rows facing the enemies along the party → enemies axis —
tanks `FormationFrontGap` (2.5 m) short of the nearest enemy (the party moves at most `FormationMaxAdvance`, 4 m,
forward), melee 1 m behind them, ranged 3.5 m, healers 5 m; each row is centred on the axis, 1.5 m apart in party
order; pets beside their owner — each unit on the free walkable spot nearest its slot (`NavGrid.FindStandingSpots`,
never on another unit; a unit keeps its place when that spot is not connected to the leader's ground). It is
skipped when a stealthed attacker or an opener starts the fight (`EngageEncounter`), and at training dummies. The
views walk to the new positions on `CombatStarted`. Overlapping units are moved to free walkable spots. The pathfinder is
`NavGridPathfinder` over the map's `NavGrid`. Player input goes straight to the `Battle` (`UseAbility`, `UseItem`,
`Move`, `EndTurn`, …) or through `session.UseAbility/UseItem`.

`BattleSummary` { `Outcome` (`Victory` | `Defeat` | `Left`), `EncounterId`, `Rounds`, `Xp`, `LevelUps`, `Killed`
(creature ids), `Loot` }:
* **Victory**: quest kill notifications, XP (level-ups), the encounter's done flag (Defeat objectives), downed/dead
  allies back at 1 HP, dead pets removed, then `CombatEnded` and the loot window (`LootOpened`, gold added).
  Practice encounters (every enemy Passive: `enc_training_dummy`) are reset instead of marked done.
* **Left** (`LeaveCombat`, or a battle that ended `Fled`): no XP/loot for the passive targets; the encounter is
  reset (fight it again later; it does not re-trigger while you stand in it).
* **Defeat**: `Mode = GameOver`, `CombatEnded` + `GameOver` events — except on a raid map (a wipe, §3 "Raids").

After any battle the party stands where it fought; the field context is rebuilt. A leader who finished the fight inside a
transition rectangle must step out of it before it travels (as on map entry), so the first step after a battle never
changes the map by surprise.

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
time, RNG state, `Settings` and the raid in progress (`raid`, §3). Party characters and pets saved on ground that
is not walkable any more (maps changed) load on the nearest walkable cell. `SessionSaveData.version` = `GameSession.SaveVersion` (1); newer versions are
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
