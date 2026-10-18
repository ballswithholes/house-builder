'use strict';
/* ==========================================================================
   design-v4/integration/tourney-mod.js — the 武林大会 as the all-on prototype runs it (TOURNAMENT.md §3–§7 rules, fast resolution):
   AI entry (§4.2), single elimination with byes to the top seeds, matches decided by the measured win curve
   p(a) = 1 / (1 + e^(−Δ duelWar / 6)) (TOURNAMENT §4.3; the campaign effect is about prizes, not the duel engine), final BO3,
   third-place match, prizes (§7.1) through ITEMS grants (INTEGRATION §7.2), ENGAGEMENT prestige and story exp, injuries
   (one record `gen.injury = { until, war, away }`, INTEGRATION §6.4), the 3-title relic 金刚杵 (classic and world; the
   5-title 赫拉克勒斯之棍 is world-only and therefore never awarded here: INTEGRATION §7.2). Seeded; no SG.Random draws.
   ========================================================================== */

function createTourney(SG, hooks) {
  const G = () => SG.G;
  const hash32 = s => { let h = 0x811c9dc5; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); } return h >>> 0; };
  const rng = (...k) => SG.SeededRandom(hash32(G().seed + '|' + G().year + '|tourney|' + k.join('|')));
  const ST = { held: 0, entrants: 0, prizeGold: 0, injuries: 0, relic: 0, champions: {}, ms: 0 };
  const PR = [{ gold: 400, prestige: 50, fame: 1, exp: 80, loy: 6, item: 'rare' }, { gold: 200, prestige: 25, exp: 50, loy: 4, item: 'uncommon' }, { gold: 100, prestige: 12, exp: 35, loy: 3, item: 'common' }];
  function eligible(x) {
    const g = G();
    if (x.dead || x.faction < 0 || x.war < 60 || (SG.Conquest.isCaptive && SG.Conquest.isCaptive(x))) return false;
    if (x.injury && x.injury.until > g.monthIndex) return false;
    if (x._away && x._away.until > g.monthIndex) return false;
    if (g.isRuler(x) && x.war < 85 && x.name !== '康茂德') return false;
    return true;
  }
  function seed(x) { const g = G(); const w = (g._tw || {})[x.name] || 0; return hooks.duelWar(x) + 0.6 * (x.level || 1) + 1.5 * Math.min(3, w) + (g._treign === x.name ? 10 : 0) + (hooks.drill(x.faction) ? 5 : 0); }
  function run() {
    const t0 = Date.now();
    const g = G();
    if (!g._tw) g._tw = {};
    const T = g.scenario === 'world' ? 75 : 72;
    let ents = [];
    for (const f of g.factions) {
      if (!f.alive) continue;
      const c = g.generalsOf(f.id).filter(eligible).sort((a, b) => seed(b) - seed(a) || a.id - b.id);
      let pick = c.filter(x => x.war >= T).slice(0, 5);
      if (!pick.length && c[0] && c[0].war >= 65) pick = [c[0]];
      ents.push(...pick);
    }
    if (ents.length < 4) return;
    ents.sort((a, b) => seed(b) - seed(a) || a.id - b.id);
    ST.held++; ST.entrants += ents.length;
    const k = rng('draw');
    const win = (a, b, tag) => { const p = 1 / (1 + Math.exp(-(hooks.duelWar(a) - hooks.duelWar(b)) / 6)); return rng('match', a.id, b.id, tag).nextDouble() < p ? a : b; };
    // byes to the top seeds; the rest paired top-vs-bottom (a stand-in for the seeded draw, TOURNAMENT §5.1)
    let S = 8; while (S < ents.length) S *= 2;
    let round = ents.slice();
    const byes = S - ents.length;
    let next = round.slice(0, byes);
    let rest = round.slice(byes);
    for (let i = 0; i < rest.length / 2; i++) { const a = rest[i], b = rest[rest.length - 1 - i]; const w = win(a, b, 'r1'); next.push(w); const l = w === a ? b : a; loss(l, k); w._tWins = (w._tWins || 0) + 1; }
    let r = 2, semis = null;
    while (next.length > 2) {
      next.sort((a, b) => seed(b) - seed(a) || a.id - b.id);
      const nn = [];
      if (next.length === 4) semis = next.slice();
      for (let i = 0; i < next.length / 2; i++) { const a = next[i], b = next[next.length - 1 - i]; const w = win(a, b, 'r' + r); nn.push(w); const l = w === a ? b : a; loss(l, k); w._tWins = (w._tWins || 0) + 1; if (semis) l._semiLost = true; }
      next = nn; r++;
    }
    // final best of three, third place
    const [a, b] = next;
    let wa = 0, wb = 0, gi = 0; while (wa < 2 && wb < 2) { if (win(a, b, 'f' + gi++) === a) wa++; else wb++; }
    const champ = wa === 2 ? a : b, second = champ === a ? b : a;
    const thirdC = (semis || []).filter(x => x !== champ && x !== second);
    const third = thirdC.length === 2 ? win(thirdC[0], thirdC[1], '3rd') : thirdC[0] || null;
    // rewards (TOURNAMENT §7.1)
    for (const x of ents) { hooks.storyExp(x, 20 + 12 * (x._tWins || 0)); if (!g.isRuler(x)) x.loyalty = Math.min(100, x.loyalty + 2); x._tWins = 0; x._semiLost = false; }
    [champ, second, third].forEach((x, i) => {
      if (!x) return;
      const P = PR[i], f = x.faction, c = g.cities[x.city];
      if (c && c.owner === f) c.gold += P.gold; ST.prizeGold += P.gold;
      hooks.prestige(f, P.prestige); if (P.fame) hooks.fame(f, P.fame);
      hooks.storyExp(x, P.exp); if (!g.isRuler(x)) x.loyalty = Math.min(100, x.loyalty + P.loy);
      if (i === 0 && rng('skill').nextDouble() < 0.25 && !x._bonusSpecial) x._bonusSpecial = true;
      else hooks.grant(f, P.item, 'tourney' + (i + 1), x.culture, rng('prize', i));
      if (i === 0) { g._tw[x.name] = (g._tw[x.name] || 0) + 1; g._treign = x.name; ST.champions[x.name] = (ST.champions[x.name] || 0) + 1;
        if (g._tw[x.name] === 3 && !g._vajra) { g._vajra = x.name; hooks.relic(x, 'vajra'); ST.relic++; } }
    });
    hooks.prestige(hooks.host(), 20);
    ST.ms += Date.now() - t0;
  }
  function loss(x, k) {
    const g = G();
    const p = Math.min(0.22, 0.06 + 0.04);          // average of the crit / finisher terms (TOURNAMENT §6.3)
    if (rng('injury', x.id).nextDouble() < p) { x.injury = { until: g.monthIndex + 2, war: 5, away: false }; ST.injuries++; }
  }
  return { run, ST };
}
module.exports = { createTourney };
