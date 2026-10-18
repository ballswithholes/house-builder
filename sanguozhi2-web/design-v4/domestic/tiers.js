'use strict';
/* ==========================================================================
   DOMESTIC-FX.md reference prototype: result tiers of every strategy command.
   Pure logic, no RNG: a tier is a function of the command's result record (Commands.lastResult,
   DOMESTIC-FX.md §4.1), so the vignette never draws from SG.Random and tests/sim.js stays bit-identical.

   tier(res) → { tier: 'miss' | 'normal' | 'good' | 'great', why: [codes], stamp: '印文' | null }

   node design-v4/domestic/tiers.js [seeds=12] [months=60]
     Monte-Carlo with a scripted domestic player (classic: 8 rulers, world: 10 rulers) on the real rules
     (js/model.js, js/strategy-ai.js); prints the tier distribution per command and checks the
     DOMESTIC-FX.md §13 targets. Exit code = number of failed checks.
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');
const STG = require('./city-stages.js');

// ------------------------------------------------------------------ rules --
const T = { MISS: 'miss', N: 'normal', G: 'good', X: 'great' };
const RANK = { miss: -1, normal: 0, good: 1, great: 2 };
const up = (a, b) => (RANK[b] > RANK[a] ? b : a);
const crossed = (before, after, marks) => marks.some(m => before < m && after >= m);
const hundreds = (before, after) => Math.floor(after / 100) > Math.floor(before / 100);
const maxStat = g => Math.max(g.war, g.intel, g.pol);
// odds bump for chance-based commands: beating long odds is exciting
const oddsTier = p => (p < 0.25 ? T.X : p < 0.45 ? T.G : T.N);

// Training ranks shown on the drill ground and in the vignette
const TRAIN_RANKS = [[0, '新募'], [40, '操练'], [60, '精练'], [80, '精锐'], [100, '百战']];
const trainRank = v => { let r = TRAIN_RANKS[0][1]; for (const [t, n] of TRAIN_RANKS) if (v >= t) r = n; return r; };

const STAMPS = {   // great-tier seal text (unique per command)
  'develop.land': '丰穰', 'develop.industry': '百炼', 'develop.town': '殷阜', recruit: '云集', train: '精锐', search: '得贤',
  'search.item': '宝现', hire: '归心', move: '星驰', transport: '辇输', ally: '盟成', discord: '离心', persuade: '倒戈',
  attack: '出师', reward: '恩泽', trade: '居奇', appoint: '得人', harvest: '大有', market: '神兵', title: '册封', stageUp: '升格',
};

function tier(r) {
  const why = [];
  let t = T.N;
  switch (r.cmd) {
    case 'develop': {
      // roll = R(0,4) of Commands.develop (exposed in lastResult); great = the linked building reaches a new stage
      if (r.stAfter > r.stBefore) { t = T.X; why.push('stageUp'); }
      else if (r.roll === 4 || hundreds(r.before, r.after)) { t = T.G; why.push(r.roll === 4 ? 'roll4' : 'hundred'); }
      if (r.after >= 999 && r.before < 999) { t = T.X; why.push('max'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS['develop.' + r.kind] : null };
    }
    case 'recruit': {
      // player-chosen amount: magnitude + milestones (general full, city crosses a 10 000 mark)
      const full = r.after >= r.max;
      if (crossed(r.cityBefore, r.cityAfter, [10000, 20000, 30000, 40000, 50000, 60000, 80000, 100000])) { t = T.X; why.push('wan'); }
      else if (full && r.n >= 3000) { t = T.X; why.push('full3000'); }
      else if (r.n >= 2000 || full) { t = T.G; why.push(full ? 'full' : 'n2000'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.recruit : null };
    }
    case 'train': {
      // 百战 (100) with a near-full army is the drill-ground spectacle; any other rank-up or a top roll is good
      if (r.after >= 100 && r.before < 100 && r.troops >= 0.8 * r.max) { t = T.X; why.push('hundred+full'); }
      else if (r.roll === 4 || (r.after >= 100 && r.before < 100) || (crossed(r.before, r.after, [80]) && r.roll >= 3)) { t = T.G; why.push('rank'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.train : null };
    }
    case 'search': {
      if (r.outcome === 'talent') { t = T.X; why.push(maxStat(r.found) >= 90 ? 'star' : 'talent'); return { tier: t, why, stamp: STAMPS.search }; }
      if (r.outcome === 'item') { t = T.X; why.push('item'); return { tier: t, why, stamp: STAMPS['search.item'] }; }
      if (r.outcome === 'gold') { t = r.value >= 160 ? T.G : T.N; why.push('gold'); }
      else if (r.outcome === 'food') { t = r.value >= 1900 ? T.G : T.N; why.push('food'); }
      else { t = T.MISS; why.push('none'); }
      return { tier: t, why, stamp: null };
    }
    case 'hire': {
      if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
      const m = maxStat(r.target);
      t = m >= 90 ? T.X : m >= 75 ? T.G : T.N;
      if (r.p < 0.45) t = up(t, T.G);                    // a long shot is at least good
      if (r.p < 0.2) t = T.X;
      why.push('max' + m, 'p' + Math.round(r.p * 100));
      return { tier: t, why, stamp: t === T.X ? STAMPS.hire : null };
    }
    case 'move': {
      if ((r.frontline && r.troops >= 20000) || r.gens >= 5) { t = T.X; why.push('massing'); }
      else if (r.gens >= 3 || r.troops >= 10000 || r.hops >= 3) { t = T.G; why.push('big'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.move : null };
    }
    case 'transport': {
      const rescue = r.destNeed > 0 && r.destFoodBefore < 2 * r.destNeed && r.destFoodBefore + r.food >= 6 * r.destNeed;
      if (rescue || r.gold >= 1500 || r.food >= 15000) { t = T.X; why.push(rescue ? 'rescue' : 'huge'); }
      else if (r.gold >= 500 || r.food >= 5000) { t = T.G; why.push('big'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.transport : null };
    }
    case 'ally': {
      if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
      t = oddsTier(r.p);
      if (r.targetCities >= r.ourCities) { t = up(t, T.G); why.push('peer'); }
      if (r.targetCities >= 2 * r.ourCities && r.targetCities >= 4) { t = T.X; why.push('great power'); }
      why.push('p' + Math.round(r.p * 100));
      return { tier: t, why, stamp: t === T.X ? STAMPS.ally : null };
    }
    case 'discord': {
      if (!r.ok) return { tier: T.MISS, why: ['seen through'], stamp: null };
      if (r.d >= 18 || (r.loyaltyAfter < 40 && r.loyaltyAfter + r.d >= 40)) { t = T.X; why.push(r.d >= 18 ? 'deep' : 'wavering'); }
      else if (r.d >= 10) { t = T.G; why.push('d10'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.discord : null };
    }
    case 'persuade': {
      if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
      t = (r.city || maxStat(r.target) >= 85) ? T.X : T.G;
      why.push(r.city ? 'city' : 'defector');
      return { tier: t, why, stamp: t === T.X ? STAMPS.persuade : null };
    }
    case 'attack': {
      if (r.troops >= 40000 || (r.ruler && r.gens >= 5 && r.troops >= 20000)) { t = T.X; why.push('grand army'); }
      else if ((r.gens >= 3 && r.troops >= 12000) || r.troops >= 20000) { t = T.G; why.push('big'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.attack : null };
    }
    case 'reward': {
      if (r.giftFits || (r.after >= 100 && r.before < 100 && r.roll === 4)) { t = T.X; why.push(r.giftFits ? 'fit gift' : 'loyal100+roll4'); }
      else if (r.roll === 4 || (r.after >= 100 && r.before <= 90) || r.gift) { t = T.G; why.push(r.gift ? 'gift' : 'roll4/100'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.reward : null };
    }
    case 'trade': {
      const fav = r.side === 'buy' ? r.price <= 8 : r.price >= 16;
      const volume = r.side === 'buy' ? r.gold : r.goldOut;
      if (fav && volume >= 1000) { t = T.X; why.push('corner'); }
      else if (fav || volume >= 600) { t = T.G; why.push(fav ? 'price' : 'volume'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.trade : null };
    }
    case 'appoint': {
      if (r.pol >= 90) { t = T.X; why.push('pol90'); }
      else if (r.pol >= 75 || r.pol - r.prevPol >= 15) { t = T.G; why.push('fit'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.appoint : null };
    }
    case 'harvest': {
      if (r.roll >= 1.1 && r.land >= 600) { t = T.X; why.push('bumper'); }
      else if (r.roll >= 1.08 || r.land >= 700) { t = T.G; why.push('good year'); }
      else if (r.roll < 0.9) { t = T.MISS; why.push('lean year'); }
      return { tier: t, why, stamp: t === T.X ? STAMPS.harvest : null };
    }
    case 'market': {   // ITEMS.md purchase: tier by rarity
      t = r.rarity === 'legendary' || r.rarity === 'epic' ? T.X : r.rarity === 'rare' ? T.G : T.N;
      return { tier: t, why: [r.rarity], stamp: t === T.X ? STAMPS.market : null };
    }
    case 'title': {    // TITLES.md: tier 1–5 of the granted title
      t = r.titleTier >= 4 ? T.X : r.titleTier >= 3 ? T.G : T.N;
      return { tier: t, why: ['t' + r.titleTier], stamp: t === T.X ? STAMPS.title : null };
    }
    default: return { tier: T.N, why: ['unknown'], stamp: null };
  }
}

// ------------------------------------------------------------------ simulation --
function loadGame() {
  global.window = global;
  const root = path.join(__dirname, '..', '..', 'js');
  const load = f => vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
  for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) load(f);
  const SG = global.SG;
  SG.ScenarioData = SG.Scenarios.classic;
  for (const f of ['model.js', 'strategy-ai.js']) load(f);
  if (SG.Progress) SG.Progress.playerAutoLearn = true;
  return SG;
}

const WHYS = {};   // WHY=<command> env: breakdown of the tier reasons
function simulate(SG, sc, key, seed, months, tally) {
  const M = SG.M, B = SG.Balance, C = SG.Commands, K = SG.DevKind;
  SG.Random.seed(seed);
  const g = SG.G = SG.GameState.newGame(key, sc);
  const P = g.player;
  // a decorative RNG for the scripted player's choices (does not touch SG.Random's role in rules beyond the commands themselves)
  const pick = SG.SeededRandom(seed * 7919 + key.length);
  const rnd = () => pick.nextDouble();
  const rec = (cmd, res) => {
    const o = tier(res);
    const k = cmd;
    tally[k] = tally[k] || { miss: 0, normal: 0, good: 0, great: 0, n: 0 };
    tally[k][o.tier]++; tally[k].n++;
    if (process.env.WHY === k) { const w = o.tier + ':' + o.why.join(','); WHYS[w] = (WHYS[w] || 0) + 1; }
  };
  const stageCtx = c => STG.ctxFor(g, c, {});
  for (let m = 0; m < months; m++) {
    if (!g.playerFaction.alive) break;
    let guard = 0;
    while (g.tokens > 0 && guard++ < 20) {
      const mine = g.citiesOf(P);
      if (!mine.length) break;
      const c = mine[Math.floor(rnd() * mine.length)];
      const offs = g.officersIn(c);
      const avail = offs.filter(x => !x.moved);
      if (!offs.length) { g.tokens--; continue; }
      const r = rnd();
      const best = (arr, f) => arr.slice().sort((a, b) => f(b) - f(a))[0];
      if (r < 0.40 && c.gold >= B.DevelopCost) {
        // 开发: the lowest of the three, with some noise
        const kinds = [[K.Land, 'land', 'farms'], [K.Industry, 'industry', 'workshop'], [K.Town, 'town', 'houses']];
        const kk = rnd() < 0.6 ? kinds.slice().sort((a, b) => c[a[1]] - c[b[1]])[0] : kinds[Math.floor(rnd() * 3)];
        const who = best(offs, x => x.pol);
        const before = c[kk[1]];
        const stB = STG.stageOf(kk[2], stageCtx(c));
        C.develop(c, who, kk[0]);
        const after = c[kk[1]];
        const base = B.DevelopBase + M.idiv(who.pol, B.DevelopPolDiv);
        const roll = after >= B.StatMax ? 4 : (after - before) - base;
        rec('develop.' + kk[1], { cmd: 'develop', kind: kk[1], roll, before, after, stBefore: stB, stAfter: STG.stageOf(kk[2], stageCtx(c)) });
        g.tokens--; continue;
      }
      if (r < 0.55) {
        const tr = offs.filter(x => x.troops > 0 && x.training < 100);
        if (tr.length) {
          const who = tr[Math.floor(rnd() * tr.length)];
          const before = who.training;
          C.train(who);
          const roll = who.training >= 100 ? 4 : (who.training - before) - B.TrainBase - M.idiv(who.war, 10);
          rec('train', { cmd: 'train', roll, before, after: who.training, troops: who.troops, max: SG.maxTroops(who) });
          g.tokens--; continue;
        }
      }
      if (r < 0.66) {
        const rc = offs.filter(x => x.troops < SG.maxTroops(x) * 0.7);
        if (rc.length && c.gold >= 200) {
          const who = rc[0];
          const max = C.recruitMax(c, who);
          const n = rnd() < 0.5 ? max : Math.max(100, Math.floor(max * 0.5 / 100) * 100);
          if (n > 0) {
            const cityBefore = g.troopsIn(c), before = who.troops;
            C.recruit(c, who, n);
            rec('recruit', { cmd: 'recruit', n: who.troops - before, before, after: who.troops, max: SG.maxTroops(who), cityBefore, cityAfter: g.troopsIn(c) });
            g.tokens--; continue;
          }
        }
      }
      if (r < 0.78 && avail.length) {
        const who = best(avail, x => x.intel);
        const goldB = c.gold, foodB = c.food;
        C.search(c, who);
        const f = C.searchFound;
        const res = f ? { cmd: 'search', outcome: 'talent', found: f } : c.gold > goldB ? { cmd: 'search', outcome: 'gold', value: c.gold - goldB } : c.food > foodB ? { cmd: 'search', outcome: 'food', value: c.food - foodB } : { cmd: 'search', outcome: 'none' };
        rec('search', res);
        g.tokens--;
        if (f && g.tokens > 0) {
          const p = C.hireChance(f, who, P);
          const ok = C.hire(f, who, P, c.id);
          rec('hire', { cmd: 'hire', ok, p, target: f });
          g.tokens--;
        }
        continue;
      }
      const free = g.freeFoundIn(c.id);
      if (free.length) {
        const t = free[0], who = best(offs, x => x.intel);
        const p = C.hireChance(t, who, P);
        rec('hire', { cmd: 'hire', ok: C.hire(t, who, P, c.id), p, target: t });
        g.tokens--; continue;
      }
      if (r < 0.84) {
        const low = offs.filter(x => !g.isRuler(x) && x.loyalty < (rnd() < 0.5 ? 85 : 100));
        if (low.length && c.gold >= B.RewardGold) {
          const who = low[0], before = who.loyalty;
          C.reward(c, who);
          rec('reward', { cmd: 'reward', roll: (who.loyalty - before) - B.RewardLoyalty, before, after: who.loyalty });
          continue;   // no token
        }
      }
      if (r < 0.88) {
        const price = C.foodPrice();
        if (price <= 8 && c.gold > 400) { const gold = Math.floor(c.gold * (0.3 + rnd() * 0.5)); C.buyFood(c, gold); rec('trade', { cmd: 'trade', side: 'buy', price, gold }); continue; }
        if (price >= 16 && c.food > 12000) { const food = Math.floor(c.food * 0.3); const g0 = c.gold; C.sellFood(c, food); rec('trade', { cmd: 'trade', side: 'sell', price, food, goldOut: c.gold - g0 }); continue; }
        const side = rnd() < 0.5 ? 'buy' : 'sell';
        if (side === 'buy' && c.gold > 300) { const gold = 150 + Math.floor(rnd() * 300); C.buyFood(c, gold); rec('trade', { cmd: 'trade', side, price, gold }); continue; }
      }
      if (r < 0.92) {
        // diplomacy
        const d = rnd();
        if (d < 0.35) {
          const fs2 = g.factions.filter(f => f.alive && f.id !== P && !g.allied(f.id, P));
          if (fs2.length && c.gold >= 200) {
            const ff = fs2[Math.floor(rnd() * fs2.length)];
            const gift = Math.min(c.gold, 200);
            const p = C.allyChance(P, ff.id, gift);
            C.ally(c, P, ff.id, gift);
            rec('ally', { cmd: 'ally', ok: g.allied(ff.id, P), p, targetCities: g.cityCount(ff.id), ourCities: g.cityCount(P) });
            g.tokens--; continue;
          }
        } else if (avail.length) {
          const agent = best(avail, x => x.intel);
          const targets = [];
          for (const i of c.links) { const x = g.cities[i]; if (x.owner >= 0 && x.owner !== P) for (const o of g.officersIn(x)) if (!g.isRuler(o)) targets.push(o); }
          if (targets.length) {
            const tg = targets[Math.floor(rnd() * targets.length)];
            agent.moved = true;
            if (d < 0.7) {
              const lb = tg.loyalty;
              const msg = C.discord(agent, tg);
              rec('discord', { cmd: 'discord', ok: msg.indexOf('成功') >= 0, d: lb - tg.loyalty, loyaltyAfter: tg.loyalty });
            } else {
              const oldOwner = g.cities[tg.city].owner, alone = g.officersIn(g.cities[tg.city]).length === 1, cityId = tg.city;
              C.persuade(agent, tg, P);
              const ok = tg.faction === P;
              rec('persuade', { cmd: 'persuade', ok, city: ok && alone && g.cities[cityId].owner === P && oldOwner !== P, target: tg });
            }
            g.tokens--; continue;
          }
        }
      }
      if (r < 0.96 && mine.length > 1) {
        const routes = C.moveTargets(c);
        if (routes.length && avail.length) {
          const rt = routes[Math.floor(rnd() * routes.length)];
          if (rnd() < 0.5) {
            const gens = avail.filter(x => !g.isRuler(x)).slice(0, 1 + Math.floor(rnd() * 3));
            if (gens.length) {
              const troops = gens.reduce((a, x) => a + x.troops, 0);
              const frontline = rt.city.links.some(i => g.cities[i].owner !== P && g.cities[i].owner >= 0 && !g.allied(g.cities[i].owner, P));
              for (const x of gens) C.move(x, rt.city);
              rec('move', { cmd: 'move', gens: gens.length, troops, hops: rt.hops, frontline });
              g.tokens--; continue;
            }
          } else if (c.gold > 200 || c.food > 3000) {
            const gold = Math.floor(c.gold * 0.4), food = Math.floor(c.food * 0.4);
            const need = Math.max(1, Math.trunc(g.troopsIn(rt.city) / B.TroopsPerFood));
            const destFoodBefore = rt.city.food;
            C.transport(c, rt.city, gold, food);
            rec('transport', { cmd: 'transport', gold, food, destFoodBefore, destNeed: need });
            g.tokens--; continue;
          }
        }
      }
      if (r < 0.98) {
        // 任命: best governor (no token in the game; recorded only)
        if (!offs.some(x => g.isRuler(x))) {
          const prev = c.governor >= 0 ? g.generals[c.governor] : null;
          const who = best(offs, x => x.pol);
          if (prev && who.id !== prev.id) { c.governor = who.id; rec('appoint', { cmd: 'appoint', pol: who.pol, prevPol: prev.pol }); continue; }
        }
      }
      // otherwise: 出征 record (departure tier only; the attack is not executed in this sim)
      const army = avail.filter(x => x.troops > 0).slice(0, 5);
      if (army.length) {
        rec('attack', { cmd: 'attack', gens: army.length, troops: army.reduce((a, x) => a + x.troops, 0), ruler: army.some(x => g.isRuler(x)) });
      }
      g.tokens--;
    }
    // AI turn and month end (player battles auto-resolved)
    const news = [];
    const pb = SG.StrategyAI.runAI(news);
    for (const s of pb) { SG.Conquest.autoResolve(s); SG.Conquest.apply(s); if (s.captives.length) SG.Conquest.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender); }
    if (g.month === B.HarvestMonth) {
      // harvest tier (roll recovered from the news line: harvest / (land × 2.2))
      const lands = new Map(g.citiesOf(P).map(c => [c.id, { land: c.land, food: c.food, need: Math.trunc(g.troopsIn(c) / B.TroopsPerFood) }]));
      SG.StrategyAI.endMonth();
      for (const [id, o] of lands) {
        const c = g.cities[id];
        if (c.owner !== P) continue;
        const harvest = c.food - o.food + o.need;
        rec('harvest', { cmd: 'harvest', land: o.land, harvest, roll: harvest / (o.land * B.FoodPerLand) });
      }
    } else SG.StrategyAI.endMonth();
  }
}

function run() {
  const seeds = parseInt(process.argv[2], 10) || 12;
  const months = parseInt(process.argv[3], 10) || 60;
  const SG = loadGame();
  const tally = {};
  const runs = { classic: ['cao', 'liubei', 'sunjian', 'yuanshao', 'liubiao', 'mateng', 'gongsun', 'kongrong'] };
  const W = SG.Scenarios.world.Factions.map(l => l.split('|')[0]);
  runs.world = [W[0], W[3], W[20], W[30], W[40], W[45], W[50], W[55], W[60], W[66]].filter(Boolean);
  for (const sc of Object.keys(runs)) for (const key of runs[sc]) for (let s = 1; s <= seeds; s++) simulate(SG, sc, key, s * 101 + key.length, months, tally);
  const rows = Object.keys(tally).sort();
  const pct = (a, n) => (100 * a / Math.max(1, n)).toFixed(1).padStart(5);
  console.log('command            n      miss  normal   good  great');
  const fails = [];
  for (const k of rows) {
    const o = tally[k];
    console.log(k.padEnd(18) + String(o.n).padStart(6) + '  ' + pct(o.miss, o.n) + '   ' + pct(o.normal, o.n) + '  ' + pct(o.good, o.n) + '  ' + pct(o.great, o.n));
    // §13 targets (only for commands with enough samples and outcome variance)
    const great = o.great / o.n, good = o.good / o.n;
    if (o.n >= 200) {
      if (great > 0.30) fails.push(k + ' great > 30%');
      if (['develop.land', 'develop.industry', 'develop.town', 'train'].includes(k)) {
        if (great < 0.05 || great > 0.16) fails.push(k + ' great outside 5–16%');
        if (good < 0.18 || good > 0.40) fails.push(k + ' good outside 18–40%');
      }
    }
  }
  if (process.env.WHY) console.log(process.env.WHY, JSON.stringify(Object.entries(WHYS).sort((a, b) => b[1] - a[1]).slice(0, 12)));
  console.log(fails.length ? 'FAIL ' + fails.join('; ') : 'ALL OK');
  process.exitCode = fails.length;
}

module.exports = { tier, STAMPS, TRAIN_RANKS, trainRank, oddsTier };
if (require.main === module) run();
