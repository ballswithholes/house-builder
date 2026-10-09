# Lanternvale expansion — "The Ember Road" (binding contract)

This document is the contract for the expansion the player asked for:

1. **Quest markers.** WoW-style symbols over NPC heads: quest available, ready to hand in, in progress. The same symbols
   appear on the Map panel (M), and the Map panel's overlapping NPC names are fixed.
2. **Combat sound effects** that sound physical and real.
3. **Deeper maps**, with a **hidden dungeon** in every map (harder monsters, better loot).
4. **New zones for levels 12–30.**
5. **A maximum party size of 5.**
6. **Raids** for a raid party of up to 10, with harder monsters, better loot, more epics, legendaries and **set armour**.

Everything here is binding for builders. If something must deviate, do it in your own files, keep every id and
interface in this document stable, and report it in `requestsForLead`. Engineering briefs with file:line detail live in
`/tmp/claude-0/-home-user-house-builder/d5030ac9-66dd-50b8-9e25-7a354a6cc315/scratchpad/expansion/brief_<area>.md`
(areas: `quests`, `audio`, `maps`, `party`, `items`, `content`, `models`). Read the brief for your area before you start.

Paths: `S/` = `Lanternvale/Assets/Lanternvale/Scripts/`, `D/` = `Lanternvale/Assets/Lanternvale/Resources/Data/`,
`T/` = `Lanternvale/Tools/harness/CoreTests/`, `Docs/` = `Lanternvale/Docs/`.

---------------------------------------------------------------------------------------------------------------------

## 0. Process (every builder)

* **Branch:** `claude/vigilant-cray-eghq5c`. Builders work in an isolated git worktree. Worktrees may start on an
  unrelated base, so **first run** `git reset --hard claude/vigilant-cray-eghq5c` and
  `mkdir -p Lanternvale/Tools/.cache && cp -rn /home/user/house-builder/Lanternvale/Tools/.cache/unityrefs Lanternvale/Tools/.cache/`.
* **Ownership.** Edit only the files you own (§12). Other builders edit the rest in parallel. A *hunk* grant
  ("you may add N lines in function X of file Y") is the only exception. Put everything else in `requestsForLead`.
* **Verify before committing:**
  `cd Lanternvale && LV_BUILD_DIR=/tmp/claude-lv-<yourkey> Tools/check.sh all`.
  The result must show `Data validation: OK`, every test passing (the count grows; nothing may fail), `compile OK`
  twice, every shader stage OK and `sfxpreview check: N passed, 0 failed`. Delete your `LV_BUILD_DIR` when you are done, because disk space is limited.
  Visual work also runs `Lanternvale/Tools/preview3d/preview3d.sh` and is judged by looking at the renders with the
  Read tool. Audio work uses `Lanternvale/Tools/sfxpreview` (created by the audio-synth builder).
* **Integrate continuously (rebase + fast-forward).** When your work is committed and green in your worktree:
  1. `git rebase claude/vigilant-cray-eghq5c` in your worktree. Resolve conflicts, keeping both intents.
  2. Re-run `check.sh all`.
  3. Take the lock: `until mkdir /tmp/claude-0/lv-integrate.lock 2>/dev/null; do sleep 20; done`. Remove a lock
     directory older than 60 minutes; it is stale.
  4. If `claude/vigilant-cray-eghq5c` moved since your rebase, release the lock (`rmdir`) and go back to step 1.
  5. Otherwise run `git -C /home/user/house-builder merge --ff-only <your-worktree-branch>`, then
     `rmdir /tmp/claude-0/lv-integrate.lock`.
  
  Never push. Never edit files in `/home/user/house-builder` directly. Never force anything.
* **Commit messages** end with:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_011LArcyrBqJEmJ4Jb4bxsuA
  ```
* **Strict JSON.** Unknown keys fail `check.sh`. Every field in §2 exists after the contract commit. Do not invent
  new keys. Keys starting with `_` are comments and are allowed.
* **Do not regress the 1–12 slice.** `T/TestsSessionFullPlaythrough.cs` and the other existing tests must stay green.
  Never move or rename existing ids, coordinates the tests use, or existing dialogue choice texts. Add, don't change.
* **Art direction.** The style is stylized, whimsical and cozy, Ghibli-inspired, with a warm hand-painted feel that
  reacts to soft light, clean ink edges and FFX-inspired heroes. The contract is `Docs/ThreeD.md`: ground = XY,
  **up = −Z**, models authored Y-up and stood with `World3D.Upright`/`Yaw`, props face local −Z.

---------------------------------------------------------------------------------------------------------------------

## 1. World topology

| id | name | W×D m | biome | environment | levels | music | rest | raidSize |
|---|---|---|---|---|---|---|---|---|
| `lanternvale` | Lanternvale (deepened) | 90×**44** | village | outdoor | 1–12 | music_lanternvale | no | 0 |
| `whisperwood` | Whisperwood (deepened) | 100×**46** | forest | outdoor | 4–12 | music_whisperwood | yes | 0 |
| `shrine` | The Old Lantern Shrine (deepened) | 70×**40** | shrine | outdoor | 9–12 | music_shrine | yes | 0 |
| `dgn_root_hollows` | The Root Hollows | 56×40 | cave | cave | 11–13 | music_dungeon | no | 0 |
| `dgn_mossdeep` | Mossdeep Grotto | 60×40 | cave | cave | 12–14 | music_dungeon | no | 0 |
| `dgn_lantern_catacombs` | The Lantern Catacombs | 56×44 | crypt | crypt | 13–15 | music_dungeon | no | 0 |
| `amberfield` | Amberfield Downs | 130×56 | highlands | outdoor | 12–18 | music_highlands | yes | 0 |
| `dgn_barrow` | The Barrow of King Aldwin | 60×44 | crypt | crypt | 18–20 | music_dungeon | no | 0 |
| `brightwater` | Brightwater | 84×44 | village | outdoor | hub 12–30 | music_town | yes | 0 |
| `mirefen` | Mirefen | 120×56 | fen | outdoor | 18–24 | music_fen | yes | 0 |
| `dgn_drowned_vault` | The Drowned Vault | 60×44 | cave | cave | 24–26 | music_dungeon | no | 0 |
| `raid_hollow_heart` | The Hollow Heart (raid) | 110×60 | hollow_heart | cave | 21–23 | music_raid | no | 10 |
| `skyreach` | Skyreach Peaks | 120×60 | peaks | outdoor | 24–30 | music_peaks | yes | 0 |
| `dgn_frozen_sanctum` | The Frozen Sanctum | 60×44 | ice_cave | cave | 30–32 | music_dungeon | no | 0 |
| `raid_ashwyrm_roost` | Ashwyrm's Roost (raid) | 110×64 | roost | outdoor | 31–33 | music_raid | no | 10 |

**Links.** Positions of edge exits are fixed. Spawns may be moved by the owner of their map, but their ids are fixed.
The table is **as built** (from the data; the contract's original hidden-entrance spots moved north with the deepened
maps where noted).

| from map | transition id | pos | size | marker | to map / spawn | arrival spawn beside the entrance |
|---|---|---|---|---|---|---|
| lanternvale | `to_amberfield` | (0.7, 32) | (1.4, 6) | auto (side) | amberfield / `from_lanternvale` | `from_amberfield` (3.4, 32) |
| lanternvale | `to_dgn_root_hollows` (hidden, reveal `found_root_hollows`) | **(23.6, 35)** (contract: 20, 19; moved north of Kusu's canopy) | (2, 2) | cave | dgn_root_hollows / `from_lanternvale` | `from_dgn_root_hollows` (23.6, 32.4) |
| whisperwood | `to_dgn_mossdeep` (hidden, `found_mossdeep`) | (46, 38) | (2, 2) | cave | dgn_mossdeep / `from_whisperwood` | `from_dgn_mossdeep` (46, 35.5) |
| shrine | `to_dgn_lantern_catacombs` (hidden, `found_lantern_catacombs`) | **(36, 35)** (contract: 36, 32) | (2, 2) | stairs | dgn_lantern_catacombs / `from_shrine` | `from_dgn_lantern_catacombs` (36, 32.4) |
| amberfield | `to_lanternvale` | (129.3, 30) | (1.4, 6) | auto | lanternvale / `from_amberfield` | `from_lanternvale` (126, 30) = `default` |
| amberfield | `to_brightwater` | (0.7, 22) | (1.4, 6) | auto | brightwater / `from_amberfield` | `from_brightwater` (3.4, 22) |
| amberfield | `to_dgn_barrow` (hidden, `found_barrow`) | **(64, 50.5)** (contract: 64, 46; the barrow door north of the King's Ring) | (2, 2) | door | dgn_barrow / `from_amberfield` | `from_dgn_barrow` (64, 48) |
| brightwater | `to_amberfield` | (83.3, 22) | (1.4, 6) | auto | amberfield / `from_brightwater` | `from_amberfield` (80, 22) = `default` |
| brightwater | `to_mirefen` | (0.7, 12) | (1.4, 6) | auto | mirefen / `from_brightwater` | `from_mirefen` (3.4, 12) |
| brightwater | `to_skyreach` | (0.7, 34) | (1.4, 6) | auto | skyreach / `from_brightwater` | `from_skyreach` (3.4, 34) |
| mirefen | `to_brightwater` | (119.3, 16) | (1.4, 6) | auto | brightwater / `from_mirefen` | `from_brightwater` (116, 16) = `default` |
| mirefen | `to_dgn_drowned_vault` (hidden, `found_drowned_vault`) | **(30, 46.4)** (contract: 30, 44) | (2, 2) | stairs | dgn_drowned_vault / `from_mirefen` | `from_dgn_drowned_vault` (30, 43.9) |
| mirefen | `to_raid_hollow_heart` | (12, 48) | (3, 3) | portal | raid_hollow_heart / `from_mirefen` | `from_raid_hollow_heart` (12, 45.5) |
| skyreach | `to_brightwater` | (119.3, 10) | (1.4, 6) | auto | brightwater / `from_skyreach` | `from_brightwater` (116, 10) = `default` |
| skyreach | `to_dgn_frozen_sanctum` (hidden, `found_frozen_sanctum`) | (86, 50) | (2, 2) | cave | dgn_frozen_sanctum / `from_skyreach` | `from_dgn_frozen_sanctum` (86, 47.5) |
| skyreach | `to_raid_ashwyrm_roost` (**hidden** as built, reveal `sr_roost_revealed`, set when `mq2_ash_on_the_wind`'s `gate` stage completes at `reg_sr_roost_gate`) | (20, 56) | (3, 3) | portal | raid_ashwyrm_roost / `from_skyreach` | `from_raid_ashwyrm_roost` (20, 53.5) |
| every dungeon / raid | `to_<parent>` | (0.7, D/2) — 20 in the 40 m caves, 22 in the 44 m maps, **30 / 32** in the raids (60 / 64 m) | (1.4, 6) | auto | parent / `from_<dungeon or raid id>` | `default` = `from_<parent>` at (3.4, same y) |

The original slice's links are unchanged: lanternvale `to_whisperwood` (89.3, 5) → `from_village`; whisperwood
`to_lanternvale` (0.7, 6) and `to_shrine` (99.3, 7, `requireFlag rotheart_defeated`); shrine `to_whisperwood` (0.7, 6).
Extra spawns as built: lanternvale `camphor` (23.2, 5.4), shrine `sanctum` (54, 6), brightwater `bw_ferry_west`
(24.6, 9) / `bw_ferry_east` (39.6, 1.8) (the ferryman's crossing), skyreach `cairnhollow` (92, 13.2).

**Deepening rule** (existing maps). Keep every existing coordinate in y ∈ [0, 15] exactly. Add the new northern band
at y 15 → D. Never place locked or hidden transitions or new encounters on the routes `TestsSessionFullPlaythrough`
walks (they stay in the southern band).

**Companions placed by the contract stubs.** Each NPC entry uses `hideFlag recruited_<id>`. Zone builders keep the
entry and may move it within their map. As built:
* `bruna` — amberfield (100, 20), at Haybright Farm (contract spot kept)
* `ysolde` — brightwater (26.2, 18.4), by the Heronguard Memorial (contract: 40, 26)
* `liora` — brightwater (24, 24.8), by the Chapel of Small Lights (contract: 52, 18)
* `nanami` — mirefen (99.6, 35.2), in Lowlantern (contract: 96, 30)

---------------------------------------------------------------------------------------------------------------------

## 2. Data contract (lands first, in the contract commit)

All fields are optional. Their defaults keep today's behaviour. Each is documented in `Docs/DataSchema.md`, and the
validator rules in §2.9 are implemented in `S/Core/Data/DataValidator.cs` by the contract builder. **Nobody else
edits `Defs.cs`, `Enums.cs`, `SessionTypes.cs`, `SaveData.cs` or `DataValidator.cs`.** If you need a change there,
put it in `requestsForLead`.

### 2.1 Quests and NPCs
* `QuestDef.minLevel` (int, 0 = none): the main character's level needed to be offered the quest. This drives the
  grey `!`.
* `QuestDef.zone` (string map id, "" = none): groups the quest in the journal.
* `QuestStageDef.turnIn` (npc or companion id, "" = infer): the NPC the marker points at for this stage.
* `NpcDef.shortName` (string): the Map panel label.
* `RegionDef.name` (string): the Map panel cluster label.

### 2.2 Hidden things and discovery
* `TransitionDef.hidden` (bool), `revealFlag` (string), and `marker` (`""` auto | `none` | `cave` | `door` | `stairs`
  | `portal`). A hidden transition is visible and usable only when `Flags.Test(revealFlag)`.
* `PropDef.requireFlag`, `PropDef.hideFlag`, `PropDef.dialogue` (dialogue id started when the prop is clicked; the
  owner is the prop's `interact` id).
* `RegionDef.check` (`CheckDef`; only `skill` and `dc` are used), `RegionDef.checkFlag` (set on success),
  `successText`, `failText`, `requireFlag`. The roll happens once per save, on the first entry while `requireFlag`
  holds, and is rolled by the best party member.
* `SkillCheck.Perception` is appended to the enum. Stat: Spirit. Proficient: Hunter and Rogue.

### 2.3 Map look and terrain
* `MapDef.biome` (string, "" = keyword fallback): `meadow | village | forest | shrine | highlands | fen | peaks | cave |
  ice_cave | crypt | hollow_heart | roost`.
* `MapDef.environment` (`outdoor` default | `cave` | `crypt`). Indoor maps have no sky, sun or hills. They use rock or
  masonry walls, an indoor light preset and near, dark fog.
* `MapDef.paths`: list of `PathDef {string art = "decal_path_dirt"; List<Vec2> points; float width = 2.2f}`.
  Polylines go in any direction.
* `MapDef.water`: list of `WaterDef {List<Vec2> points; float halfWidth = 1.5f; bool closed; List<RectDef> crossings;
  bool blocksMovement = true}`. A closed polygon is a pond. Crossings are walkable rects such as fords and bridges.
  `RectDef {Vec2 pos; Vec2 size}`.
* `MapDef.fill` (float 0–1): density of procedural interior ground cover. 0 is the old behaviour.
* `MapDef.levelMin` / `levelMax` (int): the zone band, shown on the Map panel and in the journal.
* `MapDef.raidSize` (int, 0 = not a raid), `raidReturnMap`, `raidReturnSpawn`.
* `MapDef.dungeon` (bool): a hidden dungeon. The UI shows "Hidden dungeon" and the band.
* `AmbientDef.snow`, `ash`, `dust`, `drips` (bool): new particle kinds. The terrain builder implements them in
  `AmbientParticles`.

### 2.4 Creatures and loot
* `CreatureDef.levelFloor` / `levelCap` (int, 0 = none). They clamp `scaleToParty` levels:
  `clamp(partyLevel + levelOffset, max(1, floor), cap > 0 ? cap : 63)`.
* `CreatureDef.material` (string, "" = infer): one of `plate | mail | leather | cloth | flesh | fur | chitin | bone |
  wood | stone | ether | ice | scale | wet`. It drives the impact sound.
* `CreatureDef.voice` (string, "" = infer): one of `beast | humanoid | spirit | wood | stone | dragon | frog | gnoll
  | none`. It drives the death and vocal sounds.
* `LootEntryDef.pool` (string[]), `weights` (float[]), `skipOwned` (bool), `perMembers` (int), `partyUsable` (bool).
  With a pool, the entry picks one id from it. `partyUsable` filters the pool to items some party member can equip.

### 2.5 Items, sets and legendaries
* `PassiveDef.description` (string): the tooltip text, used first when it is set.
* `SetBonusDef {int pieces = 2; List<StatModDef> stats; List<PassiveDef> equipEffects; string description}`.
* `ItemSetDef {string id, name; string[] items; List<SetBonusDef> bonuses}`.
* `DataBundle.itemSets`.
* Legendary convention: `quality: Legendary` ⇒ `unique: true`. A set's items list is the only source of membership.

### 2.6 Config, XP and party
* `config.partySize = 5`.
* `GameConfigDef.maxRaidSize = 10`.
* `GameConfigDef.xpRateByLevel`: list of `XpRatePoint {int level; float rate}`, linearly interpolated by level and
  applied to kill **and** quest XP. When the list is empty, `xpRate` is used (old behaviour). The data is
  `[{1,4},{12,4},{18,6},{24,8},{30,9},{60,9}]`, so levels 1–12 are unchanged. **The level is the content's**, never
  above the receiver's (`Progression.RateLevel`, as built after the review): a kill uses the creature's level (after
  its `levelFloor`/`levelCap` clamp), quest XP (rewards and stage `GiveXP`) the quest's level (`minLevel`, else its
  zone map's `levelMin`), other `GiveXP` outcomes the current map's `levelMin`. The same content pays the same
  whatever level the party arrives at, so a party that is ahead stays about as far ahead and does not run further
  away (the contract's first rule, the receiver's level, made the same Amberfield content pay 100 k from 12 and 131 k
  from 18).

### 2.7 Session API additions (stubs in the contract commit; real bodies come from the owners)
* `SessionEventKind`, appended at the end: `RaidPartyRequested` (Id = map, Id2 = spawn, Amount = raidSize),
  `RaidStarted`, `RaidEnded`, `SecretFound` (Id = flag, Text = banner).
* `TriggerKind.RaidGate`.
* `SaveData.raid` (`RaidSaveData {int size; List<string> normalParty; string normalLeader; List<string>
  normalAutoPlay}`, sorted lists; null when not in a raid).
* `MapRuntimeState.checkedRegions` (`List<string>`).
* `GameSession.Raid.cs` signatures: `bool InRaid`, `int RaidSize`, `int MaxPartySizeOn(string mapId)`,
  `List<Unit> RaidCandidates()`, `string CannotEnterRaidReason(string mapId, IReadOnlyList<string> ids)`,
  `string EnterRaid(string mapId, string spawnId, IReadOnlyList<string> ids, bool companionsAutoPlay)`. The stubs
  return defaults. The raid-core builder owns the bodies.
* `MapRuntime.IsTransitionVisible(TransitionDef)` and `MapRuntime.IsPropVisible(PropDef)`, fully implemented in the
  contract commit. `TransitionAt` skips invisible transitions.

### 2.8 Expansion hooks for parallel art (contract commit)
* `S/Game/Units/UnitRecipes.cs`: the `Build` switch calls
  `TryBuildExpansion(key)` **before** the prefix fallbacks. It is a dictionary filled by four partial methods,
  `RegisterCreaturesA/B/C(Dictionary<string, Func<UnitModel>> d)` and `RegisterPeopleX(d)`, each implemented in its
  own new partial file by its builder.
* `S/Game/World/Props/PropKit.cs` gets the same pattern: partial `RegisterWild(r)` and `RegisterDungeon(r)`.
* `Tools/preview3d/UnitSheet.cs` groups list every key in §9: groups `exp_a`, `exp_b`, `exp_c` and `exp_people`.
  The props sheet lists every key in §10.

### 2.9 Validator rules (contract builder; all must pass on today's data)
Quests:
* `giver` exists.
* A Talk objective's npc target has a dialogue.
* Every quest has a start source.
* Flag/Talk stages have a resolvable source, or a `turnIn`.
* `turnIn` exists.
* `minLevel > 1` ⇒ each `StartQuest` choice or node for it carries `Level ≥ minLevel`.

Maps:
* `hidden` ⇒ `revealFlag` is not empty.
* `marker` is in its set.
* `biome` and `environment` are in their sets.
* `raidSize` is in [0, `maxRaidSize`], and a raid has a valid return map and spawn.
* Water and path points have length ≥ 2, or ≥ 3 when closed.
* Region `check` ⇒ `checkFlag` is not empty.
* Prop `dialogue` exists.
* Ids are unique per map across interact, chest, transition and region ids.
* **Encounter ids are unique across all maps.**

Creatures and loot:
* `levelFloor ≤ levelCap` when both are set.
* `material` and `voice` are in their sets.
* Loot: pool ids exist; `item` and `pool` are exclusive; weights are empty or match the pool in length;
  `perMembers ≥ 0`; `random && quality ≥ Epic` is an error.

Sets and items:
* A set has a name and at least 2 equipable items.
* No item belongs to two sets.
* Bonus `pieces` values are increasing, unique and in [1, n].
* Bonus effect types are only Stat, AbilityMod, Proc or Special.
* Legendary ⇒ unique.
* Item `equipEffects` types are known.

---------------------------------------------------------------------------------------------------------------------

## 3. Quest markers (builder **markers**)

* Use the dry-run walk and static hand-in index from `brief_quests.md` §6.
* States and priority: `ReadyToTurnIn` (yellow ?) > `Available` (yellow !) > `InProgress` (grey ?) > `AvailableLater`
  (grey !).
* Grey ! = the level gate is not met and `minLevel − level ≤ 3`.
* Grey ? = the next hand-in NPC, with lookahead.
* Main-story quests (`QuestDef.main`) use the same glyph, 15 % larger, with a thin gold ring.
* **Core:** `S/Core/World/Quests/QuestMarkers.cs` and `S/Core/Session/GameSession.Markers.cs`
  (`QuestMarkersVersion`, `QuestMarkerOf`, `QuestMarkersOnMap`). Use lazy hooks, and refresh on `GameLoaded`.
  **Do not bump FlagsVersion.**
* **3D:** `S/Game/Units/QuestMarker3D.cs` is a chunky low-poly glyph with ink outline and emission, bobbing slightly
  and turning toward the camera. It must be preview-safe. `S/Game/Flow/GameFlow.QuestMarkers.cs` attaches the markers
  to NPC views and hides them in combat, game over and for the speaker in dialogue. Add **one** call in
  `GameFlow.Lifecycle.cs UpdateWorldTimers`.
* **Hover:** `NameplatesHud` shows a quest line ("Quest: …" / "Turn in: …"). This needs `GameFlow.HoveredNpcId`,
  which is additive.
* **Map panel** (the markers builder owns `MapPanel.cs`):
  * glyphs above NPC dots;
  * a de-cluttered label layout: short names, priorities, greedy placement with candidate anchors, clusters of
    service NPCs with a region name, labels on hover;
  * mouse-wheel zoom 1–3× and drag pan (needed for 40–60 m deep maps);
  * objective hints: encounters holding an active Kill target, Reach regions, chests with Collect items;
  * hidden transitions skipped until revealed (`IsTransitionVisible`);
  * the zone level band and a "Hidden dungeon" / "Raid (10)" badge in the title;
  * legend entries.
* **Tracker and journal:** "→ Return to <NPC>".
* **Preview:** `preview3d.sh map … --markers auto` draws the 3D markers.
* **Tests:** `T/TestsQuestMarkers.cs` with the real-data scenarios from the brief's §6.5 table.

## 4. Combat sound (builders **audio-synth** and **audio-play**)

Read `brief_audio.md`. Keep every existing id. Variants are published by `SfxSynth.GeneratePcm` as `"<id>#<n>"`
(n = 1…K), and `Sfx` groups them by base id and plays them round-robin, never repeating a variant immediately.
New ids, all procedural, with physically informed synthesis (layered transient, body and tail, modal metal, grains,
a small baked room):

* **Weapon layers (K ≥ 4):** `hit_blade hit_axe hit_blunt hit_dagger hit_fist hit_bite hit_claw hit_slam hit_arrow
  hit_bolt hit_bullet`
* **Material layers (K ≥ 4):** `mat_plate mat_mail mat_leather mat_cloth mat_flesh mat_fur mat_chitin mat_bone
  mat_wood mat_stone mat_ether mat_ice mat_scale mat_wet`
* **Defence:** `parry block_wood block_metal dodge miss resist immune absorb`
* **Swings:** `swing_light swing swing_heavy`
* **Ranged:** `bow xbow_release gun_fire throw_release arrow_flight wand_zap`
* **Spells:** `cast_fire cast_frost cast_arcane cast_shadow cast_holy cast_nature cast_lightning impact_lightning
  shout_horn stomp`. Keep `cast_start` as a fallback.
* **Death and vocals:** `body_fall_light body_fall_heavy armor_clatter vo_beast_yelp vo_humanoid_grunt vo_spirit_fade
  vo_wood_creak vo_stone_crumble vo_dragon_roar vo_frog_croak vo_gnoll_yip`
* **Footsteps (K ≥ 4):** `footstep_dirt footstep_leaves footstep_stone footstep_snow footstep_mud armor_jingle`
* **Hooks:** `loot_rare loot_epic loot_legendary set_complete secret_found door_stone boss_pull raid_warning
  quest_accept quest_turnin portal_whoosh`
* **Music:** new moods `highlands town fen peaks dungeon raid`. An explicit `music_<mood>` key wins over map-id
  keywords.

**audio-synth** owns:
* `S/Game/Audio/SfxSynth.cs`, plus new `SynthDsp.cs` and `SfxRecipes.*.cs`;
* `Lanternvale/Tools/sfxpreview/**`: offline render to WAV, spectrogram sheets and a `check` mode (determinism,
  no clipping or NaN, the spectral guard, the synthesis time budget);
* `S/Game/Audio/MusicEngine.cs` and `Music.cs` (new moods).

**audio-play** owns:
* `S/Game/Audio/Sfx.cs`: variants, round-robin, the base-id rate limit and cap, pool 24, a per-frame voice budget, a
  scaled-time delayed-play queue, and `Resources/Audio/Sfx/<id>/*.wav` overrides;
* new `S/Game/Audio/CombatSfx.cs`: weapon, material and creature mapping, using `CreatureDef.material`/`voice`
  first and inference second;
* the call-site lines in `CombatController.Present.cs`, `FieldPresenter.cs`, `Exploration.cs` (footsteps),
  `GameFlow.Events.cs` (loot quality, quest accept and turn-in, `SecretFound`, raid events) and `LootScreens.cs`;
* `Docs/PresentationAPI.md` §6.

Fix all of the brief's §4.3 defects. Acceptance criteria: §9.4 of the audio brief.

## 5. Items, sets, loot (builder **items**)

Follow `brief_items.md` §5–§9:
* `GameDatabase` set index;
* `S/Core/Rules/Items/ItemSets.cs`;
* `Equipment.Version`;
* set bonuses in `UnitStats`, `Battle.Procs`, `Specials` and `AbilityMods`;
* loot `pool`, `weights`, `skipOwned`, `perMembers` and `partyUsable` through a `LootContext`, with
  battle/chest wiring (hunk grants: `GameSession.Combat.cs` at battle creation, `GameSession.World.cs OpenChest`);
* `StartingGear` reserves pool and set items;
* the item pickers in tests skip proc and epic+ items;
* the tooltip set block, proc wording by trigger, `PassiveDef.description`, an item-level line, the `PanelKit.ItemTip`
  stamp and `CompareText`;
* a CharacterPanel set-bonus block, and a LootScreens "Usable by" line;
* tests in `T/TestsItemSetsAndLoot.cs`;
* docs: CoreAPI §6 and a DataSchema items section.

## 6. Party of 5 and raids (builders **raid-core** and **raid-ui**)

* `partySize` 5 lands in the contract commit, together with the test fixes.
* **raid-core** owns:
  * `S/Core/Session/GameSession.Raid.cs` (real bodies) and `GameSession.Party.cs` (`PartySize` override in raids;
    `FormationOffsets` → 14 entries);
  * `GameSession.Save.cs` (raid save/restore, and clamping positions to walkable ground on load);
  * the raid gate in `GameSession.World.cs` `EnterMap`/`CheckTriggers`/`UseTransition` (hunk grant; keep the
    world-core builder's edits);
  * `GameSession.Combat.cs`: battle formation by role, skipped on stealth or opener, and an encounter health scale on
    non-raid maps of `1 + 0.2·max(0, n − 4)`;
  * `S/Core/Rules/AI/AI.Companion.cs`: assist the nearest tank; no double heals on one target in the same round;
  * tests in `T/TestsSessionRaid.cs` and `T/TestsSessionBattleFormation.cs`, plus a `[Sim]` raid sim.
* **raid-ui** owns:
  * new `S/Game/UI/Panels/RaidPickerScreen.cs`: portraits, roles, level warnings, the role summary, and an
    "auto-play companions" toggle that defaults on;
  * new `S/Game/UI/Hud/RaidFramesHud.cs` (2×5 compact frames that keep the click/targeting contract);
  * one line in `PartyFramesHud.Visible`;
  * `PartyPanel.cs` (raid wording; hide Dismiss in a raid);
  * `PanelKit.MemberTabs` shrinking;
  * `TurnOrderHud.cs` (compact strip in big battles; `Bottom` becomes a property, so update `TargetFrameHud` and
    `ToastsHud`);
  * `TurnHud.cs` ("Auto: all companions" and "Auto-battle" toggles);
  * `CombatController.AI.cs` (faster pacing above 14 units; fast-forward on by default in raids);
  * the `GameFlow.Events.cs` cases for `RaidPartyRequested`, `RaidStarted` and `RaidEnded`.
* Raid rules:
  * Entering a raid map opens the picker.
  * Leaving restores the previous party, leader and auto-play.
  * A wipe in a raid sends the party to `raidReturnMap`, fully healed, with a "The raid has wiped" notice, instead of
    game over.
  * No lockouts.

## 7. Hidden dungeons, deeper maps, new looks (builders **world-core**, **terrain**, **scene**)

* **world-core** owns:
  * `S/Core/World/Map/MapRuntime.cs` (beyond the contract), `S/Core/World/Nav/NavGrid.cs`, and new
    `S/Core/Session/GameSession.Discovery.cs`;
  * hunk grants in `GameSession.World.cs`: CheckTriggers region checks, hidden-transition handling,
    InspectProp → dialogue, and nav rebuild on FlagsChanged out of combat (same NavGrid instance, `Version++`);
  * water blocking and flag-filtered obstacles in nav;
  * `S/Game/Flow/Exploration.cs` (the prop dialogue branch and `PropNames` for new props);
  * the `SecretFound` banner and sparkle in `GameFlow.Events.cs` (hunk grant);
  * `T/TestsWorldHiddenThings.cs`, plus a navigability test for every new map (hard asserts: every spawn and
    transition is reachable from `default`).
* **terrain** owns:
  * `S/Game/World/MapTerrain.cs`: polyline paths in any direction, back and front exit corridors, data-driven water,
    the interior `fill` pass, biome palettes for highlands, fen, peaks, cave, ice_cave, crypt, hollow_heart and roost,
    and indoor walls (rock or masonry 6–10 m, with the void never visible at camera pitch 24–58°);
  * new `S/Game/World/Biomes.cs`: a biome style table that replaces the `Contains("forest"|"shrine"|"village")`
    sniffing (`brief_models.md` §9.1.3). Keyword fallback stays for maps without `biome`. Add the file to
    `Tools/preview3d/Preview3D.csproj` (hunk grant);
  * the `MapView.TerrainMaterial`/`Average` hunk (side texture by biome);
  * `S/Game/World/WorldTextures.cs` and `AmbientParticles.cs` (snow, dust, drips, fen motes, falling ash);
  * the new ground textures: either procedural in `WorldTextures`, or artgen painters
    (`Tools/artgen/environment.py`) producing `Resources/Art/Ground/ground_<biome>.png`;
  * the Ground rows of `Docs/ArtKeys.md`.
  
  Only the terrain builder runs `Tools/artgen/generate.py`, and only with `--only ground_`. It rewrites
  `art_manifest.json`. If a rebase conflicts in that file, re-run the generator instead of merging by hand.
* **scene** owns:
  * `S/Game/World/MapView.cs`: hidden transitions with a reveal sparkle; marker styles cave, door, stairs, portal and
    none; prop flags; a 128-halo cap; indoor handling;
  * `MapSky.cs`, `DayNight.cs` (indoor light preset), `MapBackdrop.cs` (backdrops for highlands, fen, peaks and roost;
    none indoors), `Props/Waymarker.cs`;
  * `S/Game/Rendering/CameraRig.cs` (per-map zoom clamp indoors);
  * `GameFlow.Lifecycle.cs` (the menu camera stays on the village);
  * `Tools/preview3d/**` except `UnitSheet.cs` (mirror everything; extend `render_previews.sh` with every new map).

## 8. Content (data builders)

**General rules:**
* **Id prefixes.** Flags, quests, encounters, regions, chests, dialogues, npcs, abilities, auras, items and loot
  tables all use the owner's prefix:
  * `am` amberfield, `bw` brightwater, `mf` mirefen, `sr` skyreach;
  * `dg1` root hollows, `dg2` mossdeep, `dg3` catacombs, `dg4` barrow, `dg5` drowned vault, `dg6` frozen sanctum;
  * `r1` hollow heart, `r2` ashwyrm roost;
  * `mq2` main story (zone builders, see below);
  * `lv2`, `ww2`, `sh2` for additions to the existing maps.
  
  Creature ids are `cr_<prefix>_*`, encounters `enc_<prefix>_*`, loot tables `lt_<prefix>_*`, dialogues
  `dlg_<prefix>_*`, regions `reg_<prefix>_*`, chests `chest_<prefix>_*`.
* **Files.** Each map's data lives in its own files, e.g. `D/content/zone_amberfield*.json` or
  `D/content/dgn_barrow*.json` (any lists per file). The contract commit creates the stub files; their owners
  expand them.
* **Levels.** New creatures use `scaleToParty: true` with `levelFloor`/`levelCap` set to their map band and an
  offset of −1…+2. Dungeons use +2 levels with elite packs. Raids use rank Elite trash and Boss bosses.
  * Lone Elite/Rare: `healthMult ≥ 2`.
  * Instant-damage and heal abilities: `cooldown ≥ 6`.
  * Big casts: tag `Telegraph`. Raid-wide casts the healers must heal through also get `Uninterruptible` (keep heals
    and the casts whose text says "interrupt" interruptible, so interrupts still matter).
* **Quests and markers.** Follow `brief_quests.md` §13:
  * offers are gated by `Level` and by the previous quest;
  * a hand-in is a Talk objective, or a Flag set by the hand-in NPC;
  * completion is `CompleteQuest` at the turn-in NPC;
  * keep `giver` accurate;
  * cross-zone gating uses **flags**, not quest ids from other files.
* **XP.** Rates come from `xpRateByLevel` at the content's level (§2.6). A zone's quests plus its normal fights must
  take a character through the zone band, and its dungeon adds about one level. Tune quest `xp` and creature
  `xpMult` with a script that totals the zone (`TestsExpansionJourney.Xp_ZoneBudgets`, `--sim`).
  * Amberfield 12→18 needs about 85 k XP after rate.
  * Mirefen 18→24 about 150 k.
  * Skyreach 24→30 about 220 k.
  * The north bands and their three dungeons (§1) are optional catch-up content worth about 3 levels at 12–15, so
    their bosses are met at their bands (the Mossking at 14, the Lantern Lich at 15). Both were retuned for the
    slice's party at those levels: the Mossking's `damageMult` 1.15; Hazama `healthMult` 1.1 / `damageMult` 1.45 and his
    lanterns' Tithe 30–36 + 2.4/level (a two-healer party no longer stalls against the lanterns: replay of 24 L15
    journey saves × 4 seeds 96/96, was 78/96; `Journey_NorthBosses` 24/24, was 20/24).
  * **As tuned (xp-pacing review).** Totals from the data, each encounter once: north + dg1–3 32 k; Amberfield +
    Barrow 84 k; Brightwater's errands 32 k; Mirefen + Vault 140 k; Skyreach + Sanctum 199 k. Played
    (`TestsExpansionJourney`, the slice's party, every side quest, no grinding), zone exit levels:

    | route | north + dg1–3 | Amberfield + Barrow | Mirefen + Vault | Skyreach + Sanctum |
    |---|---|---|---|---|
    | lean (north skipped): measured / window | — | 18 / 17–19 | 24 / 23–25 | 30 / 29–31 |
    | full: measured / window | 15 / 14–15 | 20 / 18–20 | 26 / 24–26 | 31 / 30–31 |
    | review, before | 18 (lean —) | 23 (lean 19) | 28 (lean 25) | 34 (lean 32) |

    The lean route reaches every level gate by itself (the Barrow at 17, The Drowned Lanterns at 18, the Vault at
    23, Ash on the Wind at 24, the Sanctum at 28) and leaves each zone at its band top; the full route is about one
    level ahead (its zone exits sit at the dungeon bands' tops) and the raids (31–33) are not reached by the zones
    alone. Creature `xpMult` as tuned: north band trash 0.12–0.2, Amberfield 0.85 (the caravan boss and the warchief 1.27), Barrow 0.17–0.45,
    Mirefen 0.92 (Auntie Gall 1.2), Drowned Vault 0.38–0.7, Skyreach 0.8, Frozen Sanctum 0.45–0.59. Amberfield's
    level gates (16 and 17) need its early quests and fights to pay about 61 k before the Barrow: keep the
    pre-Barrow content front-loaded when retuning.
* **Gear budget.** Stats are `0.55 × ilvl × Qa × SlotBudgetMult`, with Qa = Uncommon 1.1, Rare 1.6, Epic 2.1,
  Legendary 2.8. Weapon DPS uses `WeaponDps` × {.72, .79, .86, .95}. Armour uses `ArmorValue`.
  * Every authored green has stats.
  * Do not use `use` on equipables or GrantAbility.
  * Armour types must be wearable at their level: Warrior and Paladin wear Mail below 40; Hunter and Shaman wear
    Leather below 40.
* **Hidden dungeons:**
  * a region `check` (Perception DC 12–16) near the entrance reveals it;
  * plus a second way in: an NPC hint that sets the flag, and/or an inspect prop with an Investigation check;
  * 4–7 encounters, at least 2 elite packs, a named boss with an Epic chance and a guaranteed Rare;
  * 2–3 chests;
  * `restArea: false`;
  * `dungeon: true`.
* **Look.** Use props and models from §9 and §10 (fallbacks show until they merge). Use `biome`, `environment`,
  `paths`, `water` and `fill`. Check your maps with `preview3d.sh map <id> --at x,y --flags '*'` at several spots,
  and add them to `render_previews.sh` (ask scene for it).
* **Tests.** Each content builder adds `T/TestsContent<Prefix>.cs`:
  * enter each map;
  * every spawn and exit is reachable;
  * at the band's top level the zone's quest chain can be scripted to completion (TalkTo/Go/fight via AutoResolve);
  * the dungeon reveal works.

**Story.** After *The Lanterns Go Dark*, the Heart Lantern shows a vision: the Hollow was a seed carried on ash from
the north, where the Ashwyrm **Vyrmathra** drowses beneath Skyreach.
* `mq2_ember_road` (level 12, giver `elder_maru`; the LV builder adds the offer to Elder Maru behind
  `QuestComplete mq_lanterns`). Its first stage, `road`, is a Reach of `amberfield`. Through Amberfield (Duskmane
  gnolls stealing lantern oil, Mudpaw tunnelers, the barrow) it ends by talking to `bw_archivist_penhallow` in
  Brightwater. Owner: **amberfield**.
* `mq2_drowned_lanterns` (minLevel 18, giver `bw_archivist_penhallow`): the drowned lanterns of Mirefen, the
  Mirelings and Mother Mire's coven. It ends by pointing at the Hollow Heart (the raid is optional). Owner:
  **mirefen**; the offer is in Penhallow's dialogue (**brightwater**).
* `mq2_ash_on_the_wind` (minLevel 24, giver Penhallow): Skyreach, the Dragonsworn and Highlord Varkas's vanguard. It
  ends with the Roost revealed. Owner: **skyreach**.
* The raids carry their own quests, given by NPCs at each raid's entrance hall: `r1_*` and `r2_*`. Defeating
  Vyrmathra is the epic finale.
* Cross-file flags:
  * `mq2_ember_road_done`, set by its last stage's `onComplete`, gates Penhallow's Mirefen offer;
  * `mq2_drowned_lanterns_done` gates the Skyreach offer;
  * the raid quest givers check `mq2_drowned_lanterns_done` and `mq2_ash_on_the_wind_done`.

**Content builders:**

| builder | owns | delivers |
|---|---|---|
| **lv** | `map_lanternvale.json`, `dialogues_village.json` (additions only), `D/content/lv2_*.json`, `dgn_root_hollows*.json` | LV deepened to 44 m (north meadow quarter, orchard, Kusu roots path, farm lane, the west road to Amberfield); the Elder's `mq2_ember_road` offer; 1–2 small LV side quests in the north band; the Root Hollows dungeon (cave under Old Kusu: rootlings, spirits, boss `cr_dg1_rootwarden`) |
| **ww** | `map_whisperwood.json`, `dialogues_whisperwood.json` (additions), `ww2_*.json`, `dgn_mossdeep*.json` | WW deepened to 46 m (the old forest in the north; brook as `water` with a ford and the bridge); 1–2 side quests; Mossdeep Grotto (mosslings, giant spiders, boss `cr_dg2_mossking`) |
| **sh** | `map_shrine.json`, `dialogues_shrine.json` (additions), `sh2_*.json`, `dgn_lantern_catacombs*.json` | Shrine deepened to 40 m (terraced switchback climb north); 1 side quest; the Lantern Catacombs (hollow knights, skeletons, boss `cr_dg3_lantern_lich`) |
| **amberfield** | `zone_amberfield*.json`, `dgn_barrow*.json` | golden downs, farmsteads, windmills, standing stones, a gnoll warcamp, tunnelers' quarry; 10–12 quests including `mq2_ember_road`; companion `bruna`'s spot; the Barrow (skeletons, wights, boss `cr_dg4_king_aldwin`) |
| **brightwater** | `zone_brightwater*.json` | river trade town: inn (rest), general goods, weaponsmith, armourer (Common/Uncommon gear for 12–30), reagents and potions for 12–30, 4 trainers (2 classes each, new NPC ids), Archivist Penhallow (the mq2 hub), ferryman, 4–6 town quests; companions `ysolde` and `liora` |
| **mirefen** | `zone_mirefen*.json`, `dgn_drowned_vault*.json` | misty fen with boardwalks and stilt villages: mirelings, crocolisks, bog ghouls, fen wisps, Mother Mire's coven; 10–12 quests including `mq2_drowned_lanterns`; companion `nanami`; the raid door; the Drowned Vault (drowned sentinels, boss `cr_dg5_tidewitch`) |
| **skyreach** | `zone_skyreach*.json`, `dgn_frozen_sanctum*.json` | snowy peaks, pines, ice, a mountain hut village: yetis, frost wolves, harpies, ogres, ice elementals, drakes, the Dragonsworn; 10–12 quests including `mq2_ash_on_the_wind`; the raid gate; the Frozen Sanctum (ice elementals, frost wights, boss `cr_dg6_rimeheart`) |
| **r1** | `raid_hollow_heart*.json` (not its loot tables) | a corrupted spirit-grove cavern; the entrance hall NPC and quests; 4 bosses (`cr_r1_thornmaw`, the Weeping Twins `cr_r1_twin_sorrow` + `cr_r1_twin_solace`, `cr_r1_mother_mire`, final `cr_r1_hollow_heart`) with real mechanics (adds, AoE, telegraphs, enrage); 4–6 trash packs |
| **r2** | `raid_ashwyrm_roost*.json` (not its loot tables) | a mountain-top ruin and dragon roost; 4 bosses (`cr_r2_frostclaw`, the Cinder Drakes `cr_r2_emberjaw` + `cr_r2_ashtongue`, `cr_r2_varkas`, final `cr_r2_vyrmathra`); trash; the finale |
| **raidloot** | `D/content/raid_loot*.json` | set items (8 classes × 5 slots × 2 tiers), set bonuses at 2, 4 and 5 pieces, about 12 raid epics per raid, 5 legendaries with auras and procs; boss tables `lt_r1_thornmaw lt_r1_twins lt_r1_mother_mire lt_r1_hollow_heart lt_r1_trash lt_r2_frostclaw lt_r2_cinder_drakes lt_r2_varkas lt_r2_vyrmathra lt_r2_trash` |
| **companions** | `companions.json` (append), `dialogues_companions.json` (append), `items_companions2.json`, `T/TestsSessionParty.cs`, `T/TestsCompanionBuilds.cs` | 4 companions: `bruna` (Warrior, Tank), `ysolde` (Paladin, Tank — use Warrior if the role inference cannot make a Paladin a Tank), `liora` (Priest, Healer), `nanami` (Shaman, Healer). Each gets preferredTalents that give the right role at L10–60, 2–3 signature items, and a full recruit/in-party dialogue |


**As built** (one line per row above; maps and links in §1, discovery per dungeon in `Docs/WorldAPI.md` §6, content
per zone in `Docs/Design.md` §7):
* **lv** — Lanternvale is 90×44 with the west road to Amberfield, the millpond, the Lantern Meadow, Pipp Orchard,
  Hollyhock Farm and Lantern Hill; 3 side quests (`lv2_orchard_thieves`, `lv2_singing_roots`, `lv2_lantern_oil`) and
  Elder Maru's `mq2_ember_road` offer. The Root Hollows' mouth sits under Kusu's roots at (23.6, 35), revealed by
  `reg_lv2_kusu_roots` (Perception 13), Nell or the Listening Root; 6 encounters, boss **The Rootwarden**, 3 chests,
  quest `dg1_heart_of_kusu` (Hotaru). Generated by `Tools/datagen/lv2/gen_lv2.py`.
* **ww** — Whisperwood is 100×46: the old forest, the brook as `water` with Heron Ford, Mossy Ford and the Old Bridge;
  quests `ww2_last_watch` (Pell) and `ww2_kings_glow` (Sprig). Mossdeep Grotto: 6 encounters, boss **King Umbercap, the
  Moss King** (with his court), 3 chests.
* **sh** — the shrine is 70×40 with the terraced Lantern Steps, Pilgrims' Rest, the Garden of Small Stones, the
  Keeper's Lodge and the High Terrace; quest `sh2_keepers_rest` (Aiko, after the Heart Lantern is lit). The Lantern
  Catacombs: 7 encounters, boss **Hazama, the Lantern Lich**, 3 chests, three keepers' lanterns to free.
* **amberfield** — 130×56 golden downs: Haybright Farm, Amberfield Cross, the King's Ring, Duskmane Warcamp (Warchief
  Skarra), Mudpaw Quarry (fight or make peace), Aldwin's Watch (the Ditchwater Gang), Turnbull's Turnips; 12 quests incl.
  `mq2_ember_road`; Bruna at Haybright. The Barrow of King Aldwin: 7 encounters (a dart-trap ambush a Perception check
  disarms), boss **King Aldwin the Unquiet**, 3 chests.
* **brightwater** — 84×44 river town: the market square, the Lantern & Heron inn, Smiths' Row (weapons, armour), a
  general store, an apothecary, a fishmonger and a greengrocer, the harbour and Old Gideon's ferry, the Chapel of Small
  Lights, the Heronguard Memorial; four trainers (Sir Bertram Oakes: Warrior, Paladin; Warden Mags Tidewell: Hunter,
  Shaman; Magister Orrin Vell: Mage, Warlock; Corwin Ashby: Priest, Rogue); Archivist Penhallow; 6 town quests; Ysolde
  and Liora.
* **mirefen** — 120×56 fen: Lowlantern stilt village, the Sinking Mere and its drowned lanterns, the Drowned Barrows,
  the Croaking Stones (Chief Gubbagulp: fight or make peace), Heron Bridge, Willow Hollow, the Gnashing Shallows and the
  Moon-Gate with the Hollow Heart portal; 11 quests incl. `mq2_drowned_lanterns` and the coven boss **Auntie Gall**;
  Nanami in Lowlantern. The Drowned Vault: 7 encounters, boss **The Tidewitch**, 3 chests, quest `dg5_undertow`.
* **skyreach** — 120×60 peaks: Cairnhollow hut village (an inn), Two-Belly Ledge (the ogres: fight or pacify),
  Whitebrow Hollow, the Harpy Crags, the Bone Field's standing stones, Heronguard Tower, the Frozen Falls and the
  Dragonsworn Vanguard Camp (boss **Vanguard-Marshal Kaedric**) before the Roost Gate; 10 quests incl.
  `mq2_ash_on_the_wind`, whose `gate` stage (finding the Roost Gate) reveals the Roost portal (`sr_roost_revealed`). The Frozen Sanctum: 6 encounters, boss
  **The Rimeheart**, 3 chests, quest `dg6_the_long_watch` (Old Lumi).
* **r1** — the Hollow Heart (110×60, levels 21–23): the Lantern Stair camp (quartermaster Bettany Quill, Hinoki), the
  Root Gallery, Thornmaw's Den, the Weeping Pools, Mire Hollow and the Heart Chamber; thorn walls open as each boss
  falls; 6 trash packs (Elite) and 4 boss fights: **Thornmaw the Rootbound**, **the Weeping Twins** (Sorrow and
  Solace), **Mother Mire**, **The Hollow Heart**; 3 quests; 2 chests (one behind a Perception check). Generated by
  `Tools/datagen/r1/gen_r1.py`.
* **r2** — Ashwyrm's Roost (110×64, levels 31–33): Ember Gate Camp (Marta Hobb, Kesta Highfold, Sir Hamon Reede), the
  Heron Road, the Last Stand, Frostclaw's Shelf, the Cinder Terraces, the Highlord's Court, the Wyrm Stair and the
  Summit; 6 trash packs and 4 boss fights: **Frostclaw the Matriarch**, **the Cinder Drakes** (Emberjaw and
  Ashtongue), **Highlord Varkas**, **Vyrmathra the Ashwyrm** (summoned with Varkas's horn); 6 quests, the finale
  `r2_last_ember`; 3 chests.
* **raidloot** — 80 set pieces (16 sets: 8 classes × 2 tiers, bonuses at 2/4/5), 36 raid epics, 5 legendaries
  (Kindlewood, Solace; Embersong, Dawnstring, Vyrmathra's Last Scale) and the 10 tables, all generated by
  `Tools/datagen/raid_loot/gen_raid_loot.py`.
* **companions** — Bruna (Warrior, Tank), Ysolde (Paladin, Tank), Liora (Priest, Healer), Nanami (Shaman, Healer),
  each with 3 signature items (`items_companions2.json`) and a full recruit and in-party dialogue.

**Raid loot design:**
* Tier 1 (Hollow Heart) has required level 20 and ilvl 26–28. Tier 2 (Ashwyrm's Roost) has required level 30 and
  ilvl 36–38.
* A set is head, shoulders, chest, hands and legs, class-restricted, in the class's wearable armour type.
* Slot drops:
  * boss 1: hands
  * boss 2: shoulders
  * boss 3: legs
  * final boss: head + chest
* Every boss table entry is `pool` + `perMembers: 5` + `partyUsable: true`, so a 10-raid gets 2 pieces per slot.
* Each boss also drops an epic pool, gold and a Rare random.
* Legendaries are `skipOwned`, at 4–6 % on the final bosses:
  * R1: 2, a caster staff and a healer's mace.
  * R2: 3, a two-handed sword, a bow and a tank shield.
* Bonuses fit the class: tank mitigation and threat, healer mana and throughput, DPS damage and procs. Use only
  Stat, AbilityMod, Proc or Special with existing specials.

---------------------------------------------------------------------------------------------------------------------

## 9. New unit model keys (builders **models-a**, **models-b**, **models-c**, **models-people**)

Each builder implements its keys in a new partial file, `S/Game/Units/UnitRecipes.<Group>.cs`, registered through
§2.8. Builders choose rigs and gaits; read `brief_models.md`. Every model must read from the game camera at zoom
3–10, have a clean ink outline, be animated by the existing animator (walk, idle, wind-up, strike, shoot, cast, hit,
death), and fit the cozy style. Bosses must be imposing at their data `size`.

**models-a** (humanoid bipeds):
* `cr_gnoll cr_gnoll_archer cr_gnoll_mystic cr_gnoll_chief`
* `cr_tunneler cr_tunneler_geomancer`
* `cr_mireling cr_mireling_hunter cr_mireling_oracle`
* `cr_mire_hag cr_ogre cr_ogre_mage cr_dragonsworn cr_mossling_king`

**models-b** (undead, elementals, humanoid bosses):
* `cr_skeleton cr_skeleton_archer cr_barrow_wight cr_bog_ghoul cr_hollow_knight`
* `cr_lantern_lich cr_drowned_sentinel cr_tidewitch cr_dg4_king_aldwin`
* `cr_ice_elemental cr_ash_elemental cr_fen_wisp cr_rimeheart`
* `cr_r1_twin cr_r1_mother_mire cr_r2_varkas`

**models-c** (beasts, fliers, giants, dragons):
* `cr_hawk cr_harpy cr_crocolisk cr_wolf_frost cr_yeti cr_rootling cr_blight_hound`
* `cr_spider_giant cr_rootwarden cr_drake_whelp cr_frost_drake`
* `cr_r1_thornmaw cr_r1_hollow_heart cr_r2_frostclaw cr_r2_cinder_drake cr_r2_vyrmathra`

**models-people:**
* `comp_bruna comp_ysolde comp_liora comp_nanami`
* `npc_archivist npc_ferryman npc_fen_villager npc_mountain_guide npc_quartermaster npc_town_guard npc_dockhand`

Content builders reference these keys in `sprite`. Tinted variants are distinguished by creature-data `size`, and
by keys with an `_<suffix>` the builder maps to the same recipe (e.g. `cr_r1_twin`, `cr_r2_cinder_drake`).

* **models-c** also owns the **winged-quad rig extension**: `UnitRig.cs` (QB wing bones), `CreatureKit.cs`
  (`QuadKit.Wings`) and `UnitAnimator.Creatures.cs` (flapping and hovering), per `brief_models.md` §9.1.5.
  Existing quads must render exactly as before; compare the `units_beasts` and `units_pets` sheets before and after.
* No builder edits the existing `UnitRecipes.People.cs` or `UnitRecipes.Creatures.cs`. Copy a helper into your own
  file, or call it: static members of the partial class are shared.
* **Portraits.**
  * New creatures fall back to the closest existing creature portrait in data (`portrait` key).
  * The **models-people** builder may add companion and NPC portrait painters in `Tools/artgen/characters.py`
    and the `Docs/ArtKeys.md` rows, but it does **not** run the generator. The lead runs
    `generate.py --only portrait_` once at integration.
  * Until then, the HUD falls back to the class portrait.
* **ArtKeys.md 3D-only keys table:** each model and prop builder appends its keys, in its own block, at the end of
  the table.

## 10. New prop keys (builders **props-wild** and **props-dungeon**)

**props-wild**, `S/Game/World/Props/PropWild*.cs`:
* highlands: `prop_standing_stone prop_scarecrow prop_beehive prop_wheat prop_watchtower_ruin prop_gnoll_tent
  prop_gnoll_totem prop_bonepile prop_tree_golden prop_cairn prop_farmhouse prop_quarry_cart`
* fen: `prop_reeds prop_cattails prop_mangrove prop_willow prop_lilypads prop_stilt_hut prop_boardwalk
  prop_fen_lantern prop_mire_totem prop_sunken_statue prop_mushroom_giant prop_fishing_rack`
* peaks: `prop_pine_snow prop_rock_snow prop_ice_spire prop_snowdrift prop_mountain_hut prop_prayer_flags
  prop_ruined_tower prop_dragon_bones`
* town: `prop_dock prop_boat prop_river_house prop_town_hall prop_fountain prop_bridge_stone prop_market_awning`

**props-dungeon**, `S/Game/World/Props/PropDungeon*.cs`:
* caves: `prop_cave_mouth prop_stalagmite prop_crystal_cluster prop_glow_mushroom prop_root_column prop_cave_wall`
* crypts: `prop_bones prop_rubble prop_brazier prop_torch_sconce prop_coffin prop_sarcophagus prop_crypt_pillar
  prop_crypt_door prop_stairs_down prop_ice_pillar prop_frozen_statue prop_drowned_arch prop_treasure_pile`
* raids: `prop_raid_portal prop_hollow_heart_core prop_thorn_wall prop_root_arch prop_dragon_skull prop_roost_nest
  prop_ash_banner prop_altar`

Every prop has a collider ellipse that fits its footprint, sits on the ground, faces −Z, and looks right from every
yaw (maps are deep now). Lights belong in data. Check props with the props sheet and in a scene.

## 11. Acceptance (lead review)

* `check.sh all` passes on the merged branch.
* `render_previews.sh --all` covers every map, with several spots on deep maps, plus the units and props sheets; the
  default run renders the curated set committed in `Docs/previews3d`.
* Reviewers judge the renders.
* The quest marker scenarios pass.
* The sfx `check` passes, and the spectrogram sheets show distinct materials.
* A scripted level-12→30 smoke run passes in the core tests: zone chains, dungeons, and both raids won via
  AutoResolve at their level with a 10-member raid.
* Docs are updated: `Design.md` §7 and l.17, `DataSchema.md`, `SessionAPI.md`, `WorldAPI.md`, `CoreAPI.md`, `UI_HUD.md`,
  `UI_Panels.md`, `ThreeD.md`, `PresentationAPI.md`, `ArtKeys.md` (3D-only keys), and the README feature list.

## 12. Ownership summary

| builder | exclusive files (plus hunk grants named in §3–§8) |
|---|---|
| contract | `Defs.cs Enums.cs SessionTypes.cs SaveData.cs DataValidator.cs GameDatabase.cs (itemSets list only) Progression.cs UnitFactory.cs (CreatureLevel) SkillChecks.cs config.json`, stub data files, the `UnitRecipes.cs`/`PropKit.cs`/`UnitSheet.cs` hooks, the test fixes for party 5 and the playthrough filters, `Docs/DataSchema.md` (new fields) |
| markers | §3 |
| audio-synth / audio-play | §4 |
| items | §5 (`GameDatabase.cs` set index; `GameSession.Items.cs`) |
| raid-core / raid-ui | §6 |
| world-core / terrain / scene | §7 |
| props-wild / props-dungeon | §10 |
| models-a / -b / -c / -people | §9 |
| content builders | §8 |

---------------------------------------------------------------------------------------------------------------------

## 13. Contract commit checklist (builder **contract**, serial, before everyone else)

1. **Defs, enums, events, saves.** Every field in §2, with defaults that keep today's behaviour exactly:
   * `Defs.cs`: the new defs `PathDef`, `WaterDef`, `RectDef`, `SetBonusDef`, `ItemSetDef` and `XpRatePoint`, plus
     the `DataBundle.itemSets` list;
   * `Enums.cs`: `SkillCheck.Perception`, appended;
   * `SessionTypes.cs`: the event and trigger kinds, appended;
   * `SaveData.cs`: `raid`;
   * `MapRuntimeState.checkedRegions`.
2. **Small Core logic:**
   * `SkillChecks.StatFor/IsProficient` for Perception;
   * `UnitFactory.CreatureLevel` floor and cap;
   * `Progression` `xpRateByLevel`, for kills and quests;
   * `MapRuntime.IsTransitionVisible/IsPropVisible`, with `TransitionAt` skipping invisible transitions;
   * the `GameSession.Raid.cs` stub with the §2.7 signatures returning defaults;
   * `GameDatabase` loads `itemSets` into a dictionary. Its index and accessors are the items builder's.
3. **Validator rules** from §2.9. Every rule passes on today's data plus the stubs.
4. **Config:** `partySize: 5`, `maxRaidSize: 10`, `xpRateByLevel` as in §2.6.
5. **Test fixes:**
   * `TestsSessionFullPlaythrough`: only the 6 original quests must complete; coordinates are looked up from data
     (region and encounter positions); the party-size assertions use `PartySize` (Rook joins the active party at
     5; Torvan's camp assertion is re-checked).
   * `TestsSessionParty` and `TestsCompanionBuilds`: 12 companions, and roles for the 4 stubs.
   * `TestsSessionSave` and `TestsBuffsSlowsApproval`: party-size literals.
   * Everything else that the new data or config breaks. Never weaken an assertion's intent.
6. **Stub data files**, valid and minimal, each later expanded by its owner:
   * `zone_amberfield.json`, `zone_brightwater.json`, `zone_mirefen.json`, `zone_skyreach.json`;
   * `dgn_root_hollows.json`, `dgn_mossdeep.json`, `dgn_lantern_catacombs.json`, `dgn_barrow.json`,
     `dgn_drowned_vault.json`, `dgn_frozen_sanctum.json`;
   * `raid_hollow_heart.json`, `raid_ashwyrm_roost.json`, `raid_loot.json`.
   
   Each map has its id, name, subtitle, the §1 size, biome, environment, ground (an existing key until terrain lands:
   `ground_meadow`/`ground_forest`/`ground_shrine`/`ground_village`), sky, ambient, music key, band, raid fields,
   **all §1 transitions and spawns**, and the companion NPC entries.
   
   On the existing maps: raise `depth` (44/46/40) and add the §1 transitions and spawns (hidden ones with
   `revealFlag`). Touch nothing else.
   
   Quest stubs:
   * `mq2_ember_road` (giver `elder_maru`, level 12, main; stage `road` Reach `amberfield`, then stage `archivist`
     Talk `bw_archivist_penhallow`; `onComplete` of the last stage sets `mq2_ember_road_done`).
   * `mq2_drowned_lanterns` (giver `bw_archivist_penhallow`, minLevel 18, main; stage `fen` Reach `mirefen`, then
     `report` Talk Penhallow; sets `mq2_drowned_lanterns_done`).
   * `mq2_ash_on_the_wind` (minLevel 24, the same pattern with `skyreach`; sets `mq2_ash_on_the_wind_done`).
   
   Start sources:
   * an Elder Maru choice, appended at the end of a hub node and gated by `QuestComplete mq_lanterns`, with
     `StartQuest mq2_ember_road`;
   * NPC `bw_archivist_penhallow` (sprite `npc_archivist`, portrait `portrait_trainer`) with a stub dialogue
     `dlg_bw_penhallow` offering the two later quests behind `Flag mq2_*_done` and `Level ≥ minLevel`, and taking the
     Talk turn-ins;
   * placed in brightwater.
   
   Companion stubs `bruna ysolde liora nanami` (§8 classes; sprite `comp_<id>`; portrait = the class portrait) with
   minimal recruit dialogues `dlg_recruit_<id>` (join + dismiss) and no starting items.
   
   Raid boss loot table stubs (§8 ids), each with gold only.
7. **Art hooks** (§2.8):
   * the `UnitRecipes.cs` registry and partial methods `RegisterCreaturesA/B/C` and `RegisterPeopleX`;
   * `PropKit.cs` `RegisterWild`/`RegisterDungeon`;
   * the `UnitSheet.cs` groups (`exp_a`, `exp_b`, `exp_c`, `exp_people`) and the props sheet listing all §9/§10 keys.
     Unknown keys fall back, which is fine for now.
8. **Docs:** `Docs/DataSchema.md` documents every new field; `Docs/Design.md` l.17 becomes "Party of up to 5"; a
   short pointer to `Docs/Expansion.md` in README.
9. **Gate:** `check.sh all` is green. Then make one commit on `claude/vigilant-cray-eghq5c` in the main checkout.
   Do not push.
