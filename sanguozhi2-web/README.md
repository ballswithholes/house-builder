# 三国志-霸王的世界 · 网页版

A 3D browser remake of Namco's Famicom strategy game 三国志II 霸王的大陆
(1992), in Chinese, grown into a world game. Everything — terrain, cities,
characters, soldiers, portraits, music and sound — is generated in code. Open
`index.html`; there is no build step and no network access.

## Scenarios

- **董卓专横 · 中原** — the original 190 AD campaign: 38 cities, 18 warlords,
  about 160 generals, with the original's rules (rebuilt from memory; every
  number is in `Balance` in `js/data.js`).
- **天下大势 · 世界** — the same year across Eurasia: China plus Liaodong and
  Korea (高句丽, 百济, 新罗, 伽倻), Japan (邪马台, 狗奴), Taiwan (夷洲, neutral),
  the steppe (鲜卑, 拓跋, 乌桓, 南匈奴), the Tarim city-states, 交州, 林邑 and 扶南,
  the Kushan Empire, the Deccan and Tamil kingdoms and Sri Lanka, Parthia and
  its vassal kings, Arabia, Aksum and Kush, and Rome under 康茂德 with the
  peoples beyond its frontier — 70 factions, about 200 cities and 500 generals
  linked by land and sea routes. The research behind it, with sources and the
  liberties taken for gameplay, is in `research/` (start with `WORLD.md`;
  the v3 additions are in `research/v3/`).

## Features

- **Smooth 3D models** (`js/figure.js`): skinned generals, soldiers and horses
  in a Dynasty Warriors-like style, with layered armour, capes, plumes and
  signature weapons (青龙偃月刀, 丈八蛇矛, 方天画戟 …); faces match the portraits;
  every culture has its own outfits.
- **Smooth world map** with blended terrain, farmland, villages, bridges,
  mountain passes, snow peaks, beaches and several kinds of trees.
- **Battles** are 18×13 and drawn from the real surroundings of each city on
  the world map, turned so the attacker enters from the direction it marched
  in (sea attacks land on the beach). Lakes and sea are impassable water.
- **攻击** cuts to a clash of the two armies with troop counters, like the
  original. Animation is on by default; the 动画 button switches it
  on / fast / off.
- **必杀**: every general has a unique special attack. Generals gain experience
  and levels (1–10) from fighting; with higher 武力 they learn more and stronger
  special attacks, and with higher 智力 more and stronger 计策 (18 tactics in
  4 tiers). At each level-up the player picks from three choices.
- **单挑** is a side-view fighting game (keyboard: ←→ move, ↑ jump, J light,
  K heavy, L guard, U parry, I special; touch: on-screen pad and buttons).
  A parry staggers the opponent and opens an unblockable counter; it has a
  longer cooldown than attacks and guard. Damage and speed come from 武力.
- **Portraits everywhere**: every list of generals, report and news line shows
  the general's portrait.
- **Music**: newly composed pieces in the spirit of the original, synthesized
  with guzheng, erhu, dizi, taiko and strings, with regional variants.
- **移动 / 输送** reach any own city connected through own territory; tapping a
  grey command explains why it is unavailable.

## Tests

- `node tests/sim.js` — rules, AI and progression balance (classic scenario).
- `node tests/move.js` — moving generals between cities.
- `node tests/world.js` — world scenario data and all-AI balance runs.
- `node tests/duel-bots.js` — duel AI, parry and button-mashing bots.
- Browser test pages in `tests/*.html` (figures, portraits, clash, specials,
  duel, music, world map, battle view, UI).

Design notes: `DESIGN-V3.md` (v3 features), `DESIGN-V2.md` (v2 features),
`CONTRACT.md` (module APIs), `UI-CLASSES.md` (UI classes).
