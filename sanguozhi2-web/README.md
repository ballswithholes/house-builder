# 三国志II 霸王的大陆 · 网页版

A 3D low-poly browser remake of Namco's Famicom strategy game 三国志II 霸王的大陆
(1992), in Chinese. Everything — terrain, cities, soldiers, portraits, music and
sound — is generated in code. Open `index.html`; there is no build step and no
network access.

## Scenarios

- **董卓专横 · 中原** — the original 190 AD campaign: 38 cities, 18 warlords,
  about 160 generals, with the original's rules (rebuilt from memory; every
  number is in `Balance` in `js/data.js`).
- **天下大势 · 世界** — the same year across Eurasia: China plus Liaodong and
  Korea (高句丽, 百济, 新罗, 伽倻), Japan (邪马台, 狗奴), Taiwan (夷洲, neutral),
  the steppe (鲜卑, 乌桓, 南匈奴), the Tarim city-states, 交州, 林邑 and 扶南,
  the Kushan Empire, Parthia, Arabia and Aksum, and Rome under 康茂德 with the
  peoples beyond its frontier — 55 factions, about 140 cities and 360 generals
  linked by land and sea routes. The research behind it, with sources and the
  liberties taken for gameplay, is in `research/` (start with `WORLD.md`).

## Features

- **Portraits** for every general, in the original's bust-portrait style.
- **攻击** cuts to a side-view clash of the two armies with troop counters,
  like the original. The 动画 button in battle switches it on / fast / off.
- **必杀**: every general has a unique special attack, usable once per battle.
- **单挑** is a side-view fighting game (keyboard: ←→ move, ↑ jump, J light,
  K heavy, L guard, I special; touch: on-screen pad and buttons). Damage and
  speed come from 武力.
- **Music**: newly composed pieces in the spirit of the original, synthesized
  with guzheng, erhu, dizi, taiko and strings, with regional variants.
- **移动 / 输送** reach any own city connected through own territory; tapping a
  grey command explains why it is unavailable.

## Tests

- `node tests/sim.js` — rules and AI (classic scenario).
- `node tests/move.js` — moving generals between cities.
- `node tests/world.js` — world scenario data and all-AI balance runs.
- Browser test pages in `tests/*.html` (portraits, clash, specials, duel,
  music, world map, battle view, UI).

Design notes: `DESIGN-V2.md` (v2 features), `CONTRACT.md` (module APIs),
`UI-CLASSES.md` (UI classes).
