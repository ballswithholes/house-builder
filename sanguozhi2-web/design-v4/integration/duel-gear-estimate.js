'use strict';
/* ==========================================================================
   design-v4/integration/duel-gear-estimate.js — INTEGRATION.md §8.3: how much do item duel numbers move AI-vs-AI duels?
   (one-off design tooling; uses the v3 duel Sim read-only, nothing is written to the game files)

   The v4 duel does not exist yet, so this measures with the v3 Sim and models every gear number the v4 duel will read:
     duelWar   = war + min(warCap, floor(gear war / warDiv))                (ITEMS §2.4)
     HP        = 100 + min(hpCap, dHp) + abilities' HP budget (≤ abilityCap per fighter)
     damage    = × (1 + min(dmgCap, dDmg) + crit-equivalent 0.5 × min(5, crit) %)   modelled as the foe's HP ÷ that factor
     parry     = window + min(0.02, dParry);  guard dGuard pp → +0.5 HP per pp (chip saved)
     and the combined duel budget   dDmg % + 0.4 × dHp ≤ budget   (INTEGRATION §8.3: 5 HP = 2 % damage)
   Best-in-slot sets per rarity are the per-slot maxima of items.json for that rarity or lower.
   Usage: node design-v4/integration/duel-gear-estimate.js [duels per cell = 300] [config = all]
   ========================================================================== */
const path = require('path'), fs = require('fs'), vm = require('vm');
global.window = global;
const root = path.join(__dirname, '..', '..', 'js');
for (const f of ['core.js', 'data.js', 'duel-game.js']) vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
const SG = global.SG, D = SG.DuelGame;
const ITEMS = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'items.json'), 'utf8'));
const N = +process.argv[2] || 300;
const TIER = { common: 0, uncommon: 1, rare: 2, epic: 3, legendary: 4 };
const SLOTS = ['weapon', 'offhand', 'armour', 'helmet', 'mount', 'treasure'];
function bis(R) {
  const t = TIER[R], keys = ['war', 'dHp', 'dDmg', 'crit', 'dParry', 'dGuard'], s = {};
  for (const k of keys) s[k] = 0;
  let abil = 0;
  for (const slot of SLOTS) {
    for (const k of keys) { let m = 0; for (const it of ITEMS.items) if (it.slot === slot && TIER[it.rarity] <= t) m = Math.max(m, (it.stats || {})[k] || 0); s[k] += m; }
    abil += [0, 0, 4, 8, 12][t];          // one ability of the set's rarity per slot (ITEMS §3.3 budgets)
  }
  s.abil = abil; return s;
}
const CONFIGS = {
  // the catalogue caps as written before consolidation (ITEMS §2.4 / §2.5), abilities summed per slot
  before: { warDiv: 2, warCap: 6, hpCap: 15, dmgCap: 4, parryCap: 0.02, guardCap: 8, abilityCap: 99, budget: 99 },
  // INTEGRATION §8.3 as adopted: combined budget 8, abilities ≤ 8 HP per fighter, gear war counts a third (cap +4)
  adopted: { warDiv: 3, warCap: 4, hpCap: 15, dmgCap: 4, parryCap: 0.02, guardCap: 8, abilityCap: 8, budget: 8 },
  // consolidation result (INTEGRATION §8.3, final): ONE all-in duel budget per fighter — gear HP + ability HP + guard
  // chip-equivalent + dmg % all count: dmg % + 0.4 × (HP + ability HP + guard HP) ≤ 6; abilities ≤ 4 HP; gear war ÷3 cap +3
  final: { warDiv: 3, warCap: 3, hpCap: 10, dmgCap: 3, parryCap: 0.01, guardCap: 6, abilityCap: 4, budget: 6, allIn: true },
  // a looser alternative kept for the open question (budget 8 all-in)
  loose: { warDiv: 3, warCap: 3, hpCap: 12, dmgCap: 4, parryCap: 0.02, guardCap: 8, abilityCap: 6, budget: 8, allIn: true },
};
function gearOf(R, C) {
  const s = bis(R);
  if (C.allIn) {
    let hp = Math.min(C.hpCap, s.dHp) + Math.min(C.abilityCap, s.abil) + 0.5 * Math.min(C.guardCap, s.dGuard), dmg = Math.min(C.dmgCap, s.dDmg);
    while (dmg + 0.4 * hp > C.budget && hp > 0) hp = Math.max(0, hp - 1);
    while (dmg + 0.4 * hp > C.budget && dmg > 0) dmg--;
    return { war: Math.min(C.warCap, Math.floor(s.war / C.warDiv)), hp, dmg: dmg / 100 + 0.005 * Math.min(3, s.crit), parry: Math.min(C.parryCap, s.dParry), raw: s };
  }
  let hp = Math.min(C.hpCap, s.dHp), dmg = Math.min(C.dmgCap, s.dDmg);
  // combined budget: dmg % + 0.4 × HP ≤ budget (trim HP first)
  while (dmg + 0.4 * hp > C.budget && hp > 0) hp--;
  while (dmg + 0.4 * hp > C.budget && dmg > 0) dmg--;
  return {
    war: Math.min(C.warCap, Math.floor(s.war / C.warDiv)),
    hp: hp + Math.min(C.abilityCap, s.abil) + 0.5 * Math.min(C.guardCap, s.dGuard),
    dmg: dmg / 100 + 0.005 * Math.min(5, s.crit),
    parry: Math.min(C.parryCap, s.dParry), raw: s,
  };
}
function duel(wa, wb, gear, keyA, keyB, i) {
  const a = { name: '甲' + (i % 7), war: wa + (gear ? gear.war : 0), intel: 50, pol: 50 }, b = { name: '乙' + (i % 7), war: wb, intel: 50, pol: 50 };
  const la = D.lookOf(a), lb = D.lookOf(b);
  if (keyA) la.weapon = keyA; if (keyB) lb.weapon = keyB;
  const swap = i % 2 === 1;
  const sim = swap ? new D._Sim(b, a, lb, la) : new D._Sim(a, b, la, lb);
  const fa = sim.f[swap ? 1 : 0], fb = sim.f[swap ? 0 : 1];
  sim.f[0].ctrl = new D._AI(sim.f[0]); sim.f[1].ctrl = new D._AI(sim.f[1]);
  if (gear) { fa.hp = 100 + gear.hp; fb.hp = 100 / (1 + gear.dmg); fa.parryW += gear.parry; }
  sim.runToEnd();
  const r = sim.outcome();
  return swap ? r.winner === 1 : r.winner === 0;
}
function rate(wa, wb, gear, keyA, keyB, n) { let w = 0; for (let i = 0; i < n; i++) if (duel(wa, wb, gear, keyA, keyB, i)) w++; return 100 * w / n; }
const out = [];
const log = s => { out.push(s); console.log(s); };
SG.Random.seed(17);
const which = process.argv[3] || 'all';
log(`== duel gear estimate (v3 Sim, AI vs AI, ${N} duels per cell, sides alternated)`);
for (const [name, C] of Object.entries(CONFIGS)) {
  if (which !== 'all' && which !== name && which !== 'cfg' && !(which.split(',').includes(name))) continue;
  log(`-- config ${name}: ${JSON.stringify(C)}`);
  for (const R of ['common', 'uncommon', 'rare', 'epic', 'legendary']) {
    const g = gearOf(R, C);
    const cells = [60, 80, 95].map(w => rate(w, w, g, null, null, N).toFixed(1) + '%');
    const gap = rate(65, 80, g, null, null, N).toFixed(1) + '%';
    log(`   ${R.padEnd(9)} BiS (duelWar +${g.war}, HP +${g.hp.toFixed(0)}, dmg +${(100 * g.dmg).toFixed(1)}%, parry +${g.parry}): equal war 60 / 80 / 95 → ${cells.join(' / ')}; 65 + gear vs 80 → ${gap}`);
  }
}
if (which === 'all' || which === 'weapons') {
  log(`-- weapon-type parity: each duel key vs 长剑 sword at equal war 80, no gear`);
  const res = [];
  for (const k of Object.keys(D.WEAPONS)) res.push([k, rate(80, 80, null, k, 'sword', N)]);
  for (const [k, v] of res) log(`   ${k.padEnd(10)} ${D.WEAPONS[k].style.padEnd(5)} reach ${D.WEAPONS[k].reach} pow ${D.WEAPONS[k].pow || 1}: ${v.toFixed(1)}%`);
  const off = res.filter(([, v]) => Math.abs(v - 50) > 5).map(([k, v]) => k + ' ' + v.toFixed(1));
  log(`   outside 45–55 %: ${off.length ? off.join(', ') : 'none'}`);
}
fs.writeFileSync(path.join(__dirname, 'results', 'duel-gear-estimate' + (which === 'all' ? '' : '-' + which.replace(/,/g, '_')) + '.txt'), out.join('\n') + '\n');
