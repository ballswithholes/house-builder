# Data generators

The JSON under `Assets/Lanternvale/Resources/Data/` is the source of truth. These are the Python scripts
the design agents used to produce it from WoW Classic reference numbers (rank-1 and max-rank values →
`perLevel`). They are kept for reference and for bulk re-tuning; if you re-run one, re-validate with
`Tools/check.sh data` and review the diff, because hand edits made directly to the JSON would be lost.

| folder | produces |
|---|---|
| `mage_warlock/` | `classes/mage.json`, `classes/warlock.json` |
| `hunter_shaman/` | `classes/hunter.json`, `classes/shaman.json` |
| `paladin_priest/` | `classes/paladin.json`, `classes/priest.json` |
| `world_content/` | `content/*.json` (`wn_build.py`; `wn_check.py` runs extra content checks) |

Paths inside the scripts may point at the original build machine; adjust before running.
