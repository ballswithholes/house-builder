'use strict';
// Throw-away estimate: how much does a gear war bonus move AI-vs-AI duel win rates? (no code changes; war bonus only)
const path = require('path'), fs = require('fs'), vm = require('vm');
global.window = global;
const root = '/home/user/house-builder/sanguozhi2-web/js';
for (const f of ['core.js', 'data.js', 'duel-game.js']) vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
const SG = global.SG, D = SG.DuelGame;
SG.Random.seed(11);
const N = +process.argv[2] || 200;
function rate(wa, wb, n) {
  let w = 0;
  for (let i = 0; i < n; i++) {
    const a = { name: '甲' + (i % 7), war: wa, intel: 50, pol: 50 }, b = { name: '乙' + (i % 7), war: wb, intel: 50, pol: 50 };
    // alternate sides to cancel the challenger tie-break
    const swap = i % 2 === 1;
    const r = swap ? D.simulate(b, a) : D.simulate(a, b);
    const aWon = swap ? r.winner === 1 : r.winner === 0;
    if (aWon) w++;
  }
  return Math.round(w / n * 1000) / 10;
}
const rows = [];
for (const base of [60, 80, 95]) for (const d of [2, 4, 6]) rows.push({ base, bonus: d, winPct: rate(base + d, base, N) });
// can gear overturn a 15-war gap?
for (const d of [3, 6]) rows.push({ base: '65+gear vs 80', bonus: d, winPct: rate(65 + d, 80, N) });
console.log(JSON.stringify(rows, null, 0));
