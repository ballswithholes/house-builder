# Data generators

> **WARNING: the class JSON was hand-fixed after generation. Do not re-run these generators blindly.**
>
> The WoW Classic 1.12 class audit and its engine follow-ups edited `Resources/Data/classes/*.json` directly
> (prerequisite arrows, rank levels, costs, descriptions, new specials, `requires`, ...). Several scripts write
> straight into `Assets/Lanternvale/Resources/Data/` (`hunter_shaman/hunter.py`, `hunter_shaman/shaman.py`,
> `paladin_priest/build.py`, `mage_warlock/gen.py`), so running one silently reverts those fixes.
>
> * In sync (re-running reproduces the current JSON; checked after the audit follow-ups):
>   `warrior_rogue/warrior_main.py` and `paladin_priest/build.py paladin` and `hunter_shaman/shaman.py` byte for
>   byte, `warrior_rogue/rogue_main.py` with identical content (one proc is only wrapped differently).
> * **Stale** (re-running would undo audit fixes): `hunter_shaman/hunter.py`, `paladin_priest/build.py priest`,
>   `mage_warlock/gen.py` (mage and warlock). `world_content/` was not compared.
>
> Before re-running anything: write the output to a scratch path, compare it with the committed JSON (every
> difference must be intended), port any JSON-only fix into the script first, then run `Tools/check.sh core`.

The JSON under `Assets/Lanternvale/Resources/Data/` is the source of truth. These are the Python scripts
the design agents used to produce it from WoW Classic reference numbers (rank-1 and max-rank values →
`perLevel`). They are kept for reference and for bulk re-tuning; if you re-run one, re-validate with
`Tools/check.sh data` and review the diff, because hand edits made directly to the JSON would be lost.

| folder | produces |
|---|---|
| `mage_warlock/` | `classes/mage.json`, `classes/warlock.json` |
| `hunter_shaman/` | `classes/hunter.json`, `classes/shaman.json` |
| `paladin_priest/` | `classes/paladin.json`, `classes/priest.json` |
| `warrior_rogue/` | `classes/warrior.json`, `classes/rogue.json` (`warrior_main.py`, `rogue_main.py <out>`) |
| `world_content/` | `content/*.json` (`wn_build.py`; `wn_check.py` runs extra content checks) |
| `lv2/` | the expansion's Lanternvale northern band and the Root Hollows: `content/map_lanternvale.json`, `lv2_content.json`, `dgn_root_hollows.json`, `dgn_root_hollows_content.json` (`gen_lv2.py`; `xp_tally.py` totals the XP) |
| `r1/` | the Hollow Heart raid: `content/raid_hollow_heart.json`, `raid_hollow_heart_creatures.json`, `raid_hollow_heart_people.json` (`gen_r1.py`) |
| `raid_loot/` | the raid sets, epics, legendaries and boss tables: `content/raid_loot.json`, `raid_loot_t1.json`, `raid_loot_t2.json`, `raid_loot_epics.json` (`gen_raid_loot.py`) |

The three expansion generators (`lv2/`, `r1/`, `raid_loot/`) write straight into `Assets/Lanternvale/Resources/Data/content/` (found relative to the script)
and, at the expansion's docs pass, reproduced the committed JSON byte for byte. Edit those files through their script.

Paths inside the scripts may point at the original build machine; adjust before running.
