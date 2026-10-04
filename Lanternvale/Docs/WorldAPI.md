# Lanternvale — World Core API (`Lanternvale.World`)

Pure C# 9 (no UnityEngine), in `Assets/Lanternvale/Scripts/Core/World/**`. Used by the rules engine /
`GameSession` (Core/Rules) and by the Unity presentation layer (Scripts/Game).

> Status: **STUB — API being implemented.** Signatures below are the contract; details may be refined
> (this file is kept in sync with the code).

Coordinates: world metres. Map ground is `x ∈ [0, width]`, `y ∈ [0, depth]`; `y = 0` is nearest the camera
(bottom of the screen), larger `y` is further back. `Vec2` is `Lanternvale.Util.Vec2`.

| Area | Main types |
|---|---|
| Navigation | `NavGrid`, `NavAgent`, `NavPath`, `PathStatus`, `ReachMap`, `NavGridOptions` |
| Flags | `FlagStore` |
| Dialogue | `IDialogueContext`, `PartyMemberInfo`, `DialogueRunner`, `DialogueView`, `ChoiceView`, `CheckResult`, `SkillChecks`, `DialogueMemory`, `WorldRules` |
| Quests | `QuestLog`, `QuestStatus`, `QuestEvent`, `QuestJournalEntry`, `ObjectiveView` |
| Map state | `MapRuntime` |
| Aggregate + saves | `WorldState`, `WorldSaveData` (+ per-component `*State` DTOs) |

(Full reference follows once implemented.)
