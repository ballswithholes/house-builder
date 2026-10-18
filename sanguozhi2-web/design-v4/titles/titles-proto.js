'use strict';
/* ==========================================================================
   design-v4/titles/titles-proto.js —— 官爵系统的一次性平衡估算（只在内存里给游戏函数打补丁，不改任何游戏文件）
   规则取自 design-v4/titles.json 与 TITLES.md：家族 = 最高属性；品阶门槛；国势 → 君主位阶 → 各品阶名额；
   电脑按功绩授官（不耗 SG.Random）；带兵上限、训练、开发、本城金收入 / 秋收 / 征兵 / 人口 / 忠诚、全国加成、
   搜索 / 登用、俸禄、出奔判定加成。战术战斗只估算 攻 / 防 / 主将统率 / 计策威力 / 开战士气（不含会心、计策成功率与识破）。
   实验开关（env）：KEEP=1 电脑征兵留出下月俸禄；SALARY=a,b,c,d,e 月俸；GRANTCOST=… 授官礼金；FX_OFF=k1,k2 关闭效果；FX_SCALE=k:倍数；SEED0=种子偏移。
   用法：
     node design-v4/titles/titles-proto.js classic [种子数=8] [年数=25]     全电脑战役：关 / 开 / 开 + 政绩经验
     node design-v4/titles/titles-proto.js world   [种子数=6] [年数=15]
     node design-v4/titles/titles-proto.js tactical [种子数=6]               第 8 年局面的战术战斗（官爵效果 双方 / 无 / 仅攻方 / 仅守方）
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const MODE = process.argv[2] || 'classic';
const root = path.join(__dirname, '..', '..', 'js');
const load = f => vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) load(f);
const SG = global.SG;
const SCEN = MODE === 'world' ? 'world' : 'classic';
SG.ScenarioData = SG.Scenarios[SCEN];
for (const f of ['model.js', 'strategy-ai.js', 'battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js']) load(f);
const { GameState, Commands: C, StrategyAI, BattleModel, Conquest, Balance: B, M } = SG;
const D = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'titles.json'), 'utf8'));
const POST = {}; for (const p of D.posts) POST[p.id] = p;
const TIER = D.tiers.map(t => Object.assign({}, t));
const GRANT = process.env.GRANTCOST ? process.env.GRANTCOST.split(',').map(Number) : TIER.map(t => t.grantGold || 0);
for (const t of TIER) t.salary = 0;   // 定稿不设月俸（TITLES.md §6.3）；env SALARY 可复现对比
if (process.env.SALARY) process.env.SALARY.split(',').forEach((v, i) => { TIER[i].salary = +v; });
const FAMS = ['war', 'intel', 'pol'];

// ------------------------------------------------------------ 规则 --
const T = { on: false, dom: false, battle: 'none', rank: new Map(), seq: 0, domMonth: new Map(), salary: 0, gross: 0, defect: 0 };
const G = () => SG.G;
const stat = (g, f) => (f === 'war' ? g.war : f === 'intel' ? g.intel : g.pol);
function famsOf(g) { const m = Math.max(g.war, g.intel, g.pol); return FAMS.filter(f => stat(g, f) === m); }
function eligible(g, post) {
  const tr = TIER[post.tier - 1];
  return famsOf(g).indexOf(post.family) >= 0 && (g.level | 0) >= tr.reqLevel && stat(g, post.family) > tr.reqStat;
}
const act = g => g._t && !g._t.suspended;
const fxOn = g => act(g) && !g._t.unpaid;
const OFFK = new Set((process.env.FX_OFF || '').split(',').filter(Boolean));
const FXK = {}; for (const kv of (process.env.FX_SCALE || '').split(',').filter(Boolean)) { const [k, v] = kv.split(':'); FXK[k] = +v; }
function fx(g, k) { return g && fxOn(g) && !OFFK.has(k) ? (POST[g._t.post].fx[k] || 0) * (FXK[k] === undefined ? 1 : FXK[k]) : 0; }
const curTier = g => (g._t ? g._t.tier : 0);
function powerOf(f) { let P = 0; for (const c of G().cities) if (c.owner === f) P += 1 + (c.land + c.industry + c.town) / D.rulerRanks.power.devDiv; return P; }
function updateRank(f) {
  const P = powerOf(f), th = D.rulerRanks.thresholds;
  let up = 1; for (let k = 0; k < th.length; k++) if (P >= th[k]) up = k + 1;
  let r = T.rank.get(f) || 1;
  if (up > r) r = up; else while (r > 1 && P < th[r - 1] * D.rulerRanks.hysteresis) r--;
  T.rank.set(f, r); return r;
}
const holders = f => G().generals.filter(x => x._t && x.faction === f && !x.dead);
function validate() {
  const g = G();
  for (const x of g.generals) if (x._t && (x.dead || x.faction !== x._t.faction || x.faction < 0 || g.isRuler(x))) delete x._t;
}
function seatCheck(f) {
  const caps = D.rulerRanks.caps[T.rank.get(f) || 1];
  const hs = holders(f);
  for (let t = 1; t <= 5; t++) {
    const a = hs.filter(x => x._t.tier === t && !x._t.suspended).sort((p, q) => p._t.since - q._t.since);
    const s = hs.filter(x => x._t.tier === t && x._t.suspended).sort((p, q) => p._t.since - q._t.since);
    while (a.length > caps[t - 1]) { const x = a.pop(); x._t.suspended = true; }
    while (a.length < caps[t - 1] && s.length) { const x = s.shift(); x._t.suspended = false; a.push(x); }
  }
}
function seatsFree(f, post) {
  if (!post.seats) return true;
  return holders(f).filter(x => x._t.post === post.id).length < post.seats;
}
function grant(g, post) {
  const tr = TIER[post.tier - 1];
  const best = g._tBest || 0;
  const bonus = Math.max(0, tr.loyGrant - (best ? TIER[best - 1].loyGrant : 0));
  if (!OFFK.has('loyGrant')) g.loyalty = Math.min(100, g.loyalty + bonus);
  g._tBest = Math.max(best, post.tier);
  g._t = { post: post.id, tier: post.tier, family: post.family, since: ++T.seq, faction: g.faction, suspended: false, month: G().monthIndex };
}
function prefBranch(g, fam) {
  if (fam === 'war') return g.war >= 92 || g.war - g.intel >= 35 ? 'B' : 'A';
  if (fam === 'intel') return g.pol >= g.intel - 8 ? 'B' : 'A';
  return g.war > g.intel ? 'B' : 'A';
}
function pickPost(f, g, t) {
  const fams = famsOf(g);
  for (const fam of fams) {
    const list = D.posts.filter(p => p.tier === t && p.family === fam && eligible(g, p) && seatsFree(f, p));
    if (!list.length) continue;
    const pb = prefBranch(g, fam);
    return list.find(p => p.branch === pb) || list[0];
  }
  return null;
}
const merit = g => Math.max(g.war, g.intel, g.pol) + 3 * (g.level | 0);
function grossIncome(f) { let s = 0; for (const c of G().cities) if (c.owner === f) s += c.industry * B.GoldPerIndustry + c.town * B.GoldPerTown; return s; }
function salaryOf(f) { let s = 0; for (const x of holders(f)) if (!x._t.suspended) s += TIER[x._t.tier - 1].salary; return s; }
function aiManage(f) {
  const g = G();
  updateRank(f);
  seatCheck(f);
  const caps = D.rulerRanks.caps[T.rank.get(f)];
  const cands = g.generalsOf(f).filter(x => !g.isRuler(x) && !Conquest.isCaptive(x));
  const income = grossIncome(f);
  for (let t = 5; t >= 1; t--) {
    for (let guard = 0; guard < 12; guard++) {
      const hs = holders(f);
      if (hs.filter(x => x._t.tier === t && !x._t.suspended).length >= caps[t - 1]) break;
      // 俸禄不超过全国金收入的 15%（一、二品不受此限）
      if (t >= 3 && salaryOf(f) + TIER[t - 1].salary > 0.15 * income) break;
      let best = null, bm = -1, bp = null;
      for (const x of cands) {
        if (curTier(x) >= t || (x._t && x._t.month === g.monthIndex)) continue;
        const post = pickPost(f, x, t);
        if (!post) continue;
        const m = merit(x);
        if (m > bm || (m === bm && x.id < best.id)) { bm = m; best = x; bp = post; }
      }
      if (!best) break;
      // 授官礼金（一次性）：由本势力金最多的城支付，须留 100 余额；付不起就不授（玩家同理，按钮置灰）
      const cost = GRANT[t - 1] || 0;
      if (cost > 0) {
        // 付礼金的城须留下 100 + 三个月的基本俸禄（每员 SalaryPerGeneral），免得把重兵屯驻的大城掏空
        const keepOf = cc => 100 + 3 * B.SalaryPerGeneral * g.officersIn(cc).length;
        const rich = g.citiesOf(f).sort((a, b) => (b.gold - keepOf(b)) - (a.gold - keepOf(a)))[0];
        if (!rich || rich.gold < cost + keepOf(rich)) break;
        rich.gold -= cost; T.grantGold = (T.grantGold || 0) + cost;
      }
      grant(best, bp);
    }
  }
}
function aiManageAll() { validate(); for (const f of G().factions) if (f.alive) aiManage(f.id); }
// 主政者：太守；太守是君主时由该城品阶最高的文官代理
function admin(c) {
  const g = G();
  if (c.governor < 0) return null;
  const gov = g.generals[c.governor];
  if (!g.isRuler(gov)) return gov;
  let best = null;
  for (const x of g.officersIn(c)) if (act(x) && x._t.family === 'pol' && (!best || x._t.tier > best._t.tier)) best = x;
  return best;
}
const cityFx = (c, k) => fx(admin(c), k);
function natFx(f, k) { let v = 0; for (const x of holders(f)) v = Math.max(v, fx(x, k)); return v; }

// ------------------------------------------------------------ 补丁 --
const R = (a, b) => SG.Random.rangeInt(a, b + 1);
function domExp(g, amt) {
  if (!T.dom || !g || !(amt > 0)) return;
  const k = g.id, used = T.domMonth.get(k) || 0, add = Math.min(amt, 30 - used);
  if (add <= 0) return;
  T.domMonth.set(k, used + add);
  SG.Progress.gain(g, add, 'domestic');
}
// 镇守：驻城的武官使城防 +garrison（取城中最大值）
const oCD = SG.cityDefense;
SG.cityDefense = function (c) {
  const base = oCD(c);
  if (!T.on || !SG.G) return base;
  let add = 0;
  for (const o of SG.G.officersIn(c)) add = Math.max(add, fx(o, 'garrison'));
  return base + add;
};
const oMax = SG.maxTroops;
SG.maxTroops = g => oMax(g) + (T.on ? fx(g, 'troops') : 0);
const oDev = C.develop;
C.develop = function (c, g, kind) {
  if (!T.on) return oDev.apply(this, arguments);
  if (c.gold < B.DevelopCost) return '金不足，无法开发。';
  c.gold -= B.DevelopCost;
  let gain = B.DevelopBase + M.idiv(g.pol, B.DevelopPolDiv) + R(0, 4);
  gain = Math.round(gain * (1 + fx(g, 'dev')));
  const key = kind === SG.DevKind.Land ? 'land' : kind === SG.DevKind.Industry ? 'industry' : 'town';
  const before = c[key]; c[key] = Math.min(B.StatMax, c[key] + gain);
  domExp(g, 6 + (c[key] - before >= 20 ? 2 : 0));
  return 'ok';
};
const oTrain = C.train;
C.train = function (g) {
  if (!T.on) return oTrain.apply(this, arguments);
  if (g.troops <= 0) return '';
  g.training = Math.min(100, g.training + B.TrainBase + M.idiv(g.war, 10) + R(0, 4) + fx(g, 'train'));
  domExp(g, 3);
  return 'ok';
};
const tpg = c => B.TroopsPerGold * (1 + (T.on ? cityFx(c, 'recruit') : 0));
const oRecMax = C.recruitMax, oRec = C.recruit;
C.recruitMax = function (c, g) {
  if (!T.on) return oRecMax.apply(this, arguments);
  const room = SG.maxTroops(g) - g.troops;
  // 电脑征兵留出下月的基本俸禄与官俸（不把城金花光，免得月末「金不足 → 全城忠诚 −3」）
  const keep = !process.env.KEEP ? 0 : G().officersIn(c).length * B.SalaryPerGeneral + G().officersIn(c).reduce((s, o) => s + (act(o) ? TIER[o._t.tier - 1].salary : 0), 0) + 10;
  const byGold = Math.floor(Math.max(0, Math.min(c.gold - keep, B.RecruitGoldMax)) * tpg(c));
  return Math.max(0, Math.min(room, Math.min(byGold, M.idiv(c.population, 10))));
};
C.recruit = function (c, g, troops) {
  if (!T.on) return oRec.apply(this, arguments);
  troops = Math.min(troops, C.recruitMax(c, g));
  if (troops <= 0) return '';
  const cost = Math.ceil(troops / tpg(c));
  c.gold -= cost; c.population -= troops;
  const total = g.troops + troops;
  g.training = M.idiv(g.training * g.troops + 30 * troops, Math.max(1, total));
  g.troops = total;
  domExp(g, 3);
  return 'ok';
};
const oSearch = C.search;
C.search = function (c, g) {
  if (!T.on) return oSearch.apply(this, arguments);
  C.searchFound = null;
  const f = G().factions[c.owner];
  const hidden = G().generals.filter(x => x.city === c.id && x.hidden && SG.isFree(x));
  const p = 0.22 + g.intel / 260 + f.virtue / 420 + fx(g, 'search');
  if (hidden.length > 0 && SG.Random.value() < p) {
    const h = hidden[SG.Random.rangeInt(0, hidden.length)];
    h.hidden = false; C.searchFound = h; domExp(g, 12); return 'found';
  }
  const r = SG.Random.value();
  if (r < 0.25) c.gold += R(60, 220); else if (r < 0.45) c.food += R(800, 2500);
  domExp(g, 4);
  return '';
};
const oHireCh = C.hireChance, oHire = C.hire;
C.hireChance = function (target, recruiter, faction) {
  const p = oHireCh.apply(this, arguments);
  return T.on ? M.clamp(p + fx(recruiter, 'hire'), 0.05, 0.95) : p;
};
C.hire = function (target, recruiter) {
  const ok = oHire.apply(this, arguments);
  if (T.on) domExp(recruiter, ok ? 10 : 2);
  return ok;
};
const oEnd = StrategyAI.endMonth;
StrategyAI.endMonth = function () {
  const g = G();
  if (!T.on) {
    const bf = g.generals.map(x => x.faction);
    const bl = g.generals.map(x => x.loyalty);
    const nw = oEnd.apply(this, arguments);
    g.generals.forEach((x, i) => { if (x.faction >= 0 && x.faction === bf[i] && x.loyalty < bl[i]) T.loyDrop = (T.loyDrop || 0) + (bl[i] - x.loyalty); });
    g.generals.forEach((x, i) => { if (bf[i] >= 0 && x.faction < 0 && !x.dead && g.factions[bf[i]].alive) T.defect++; });
    return nw;
  }
  const harvest = g.month === B.HarvestMonth;
  const base = g.cities.map(c => (c.owner >= 0 ? c.industry * B.GoldPerIndustry + c.town * B.GoldPerTown : 0));
  const boosted = [];
  for (const x of g.generals) if (!OFFK.has('effLoy') && x._t && x.faction >= 0 && !x.dead && x.loyalty < 40) { const b = TIER[x._t.tier - 1].effLoy; x.loyalty += b; boosted.push([x, b, x.faction]); }
  const before = g.generals.map(x => x.faction);
  const bl = g.generals.map(x => x.loyalty);
  const news = oEnd.apply(this, arguments);
  g.generals.forEach((x, i) => { if (x.faction >= 0 && x.faction === before[i] && x.loyalty < bl[i]) T.loyDrop = (T.loyDrop || 0) + (bl[i] - x.loyalty); });
  for (const [x, b, f] of boosted) if (x.faction === f && !x.dead) x.loyalty = Math.max(0, x.loyalty - b);
  g.generals.forEach((x, i) => { if (before[i] >= 0 && x.faction < 0 && !x.dead && g.factions[before[i]].alive) T.defect++; });
  T.domMonth.clear();
  for (let i = 0; i < g.cities.length; i++) {
    const c = g.cities[i];
    if (c.owner < 0) continue;
    const kI = cityFx(c, 'income') + natFx(c.owner, 'natIncome');
    c.gold += Math.round(base[i] * kI);
    if (harvest) c.food += Math.round(c.land * B.FoodPerLand * (cityFx(c, 'harvest') + natFx(c.owner, 'natHarvest')));
    const kp = cityFx(c, 'pop'); if (kp > 0) c.population = Math.min(2000000, c.population + Math.round(c.population * c.town * B.PopGrowthPerTown / 100 * kp));
    const la = cityFx(c, 'loyAura'); if (la > 0) for (const o of g.officersIn(c)) if (!g.isRuler(o)) o.loyalty = Math.min(100, o.loyalty + la);
    T.gross += base[i];
  }
  for (const x of g.generals) {
    if (!act(x) || x.faction < 0 || x.dead) continue;
    // 俸禄：先由所在城支付，不足部分由本势力金最多的城补足；全国都付不起时只有该持有者忠诚 −2（欠俸），不波及他人
    const c = g.cities[x.city], sal = OFFK.has('salary') ? 0 : TIER[x._t.tier - 1].salary;
    let due = sal;
    // 只动用超出下月基本俸禄（每员 SalaryPerGeneral）+ 20 的余钱，免得把城金掏空、触发第一版的「金不足 → 全城忠诚 −3」
    const reserve = cc => g.officersIn(cc).length * B.SalaryPerGeneral + 20;
    const pay = cc => { const v = Math.min(due, Math.max(0, cc.gold - reserve(cc))); cc.gold -= v; due -= v; };
    pay(c);
    if (due > 0) for (const cc of g.citiesOf(x.faction).sort((a, b) => b.gold - a.gold)) { if (due <= 0) break; pay(cc); }
    T.salary += sal - due;
    x._t.unpaid = due > 0;
    if (due > 0) T.unpaid = (T.unpaid || 0) + 1;
  }
  return news;
};
// 战术战斗（估算）：攻、防、主将统率、计策威力；开战士气在建模后加
const BP = BattleModel.prototype;
const useFx = u => T.battle === 'both' || (T.battle === 'atk' && u.side === 0) || (T.battle === 'def' && u.side === 1);
function cmdFx(Mdl, side, k) { const cu = Mdl.commander(side); return cu && cu.alive && useFx(cu) ? fx(cu.gen, k) : 0; }
const oAtk = BP.atkPower, oDefF = BP.defFactor, oTacK = BP.tacK;
BP.atkPower = function (u) { const v = oAtk.call(this, u); return useFx(u) ? v * (1 + fx(u.gen, 'atk') + cmdFx(this, u.side, 'cmdAtk')) : v; };
BP.defFactor = function (u) { const v = oDefF.call(this, u); return useFx(u) ? v * (1 + fx(u.gen, 'def') + cmdFx(this, u.side, 'cmdDef')) : v; };
BP.tacK = function (u, e) { const v = oTacK.call(this, u, e); return useFx(u) ? v * (1 + fx(u.gen, 'tac')) : v; };

// ------------------------------------------------------------ 统计 --
const avg = a => (a.length ? a.reduce((s, v) => s + v, 0) / a.length : 0);
const median = a => { const b = a.slice().sort((x, y) => x - y); return b.length ? b[b.length >> 1] : 0; };
function campaign(seed, years, variant) {
  T.on = variant !== 'off'; T.dom = variant === 'dom'; T.unpaid = 0; T.grantGold = 0; T.loyDrop = 0; T.lowLoy = 0; T.rank = new Map(); T.seq = 0; T.salary = 0; T.gross = 0; T.defect = 0; T.domMonth.clear();
  SG.Random.seed(seed);
  const key = SG.ScenarioData.Factions[0].split('|')[0];
  seed += +(process.env.SEED0 || 0);
  const g = SG.G = GameState.newGame(key, SCEN);
  g.player = -1;
  const out = { alive: {}, top: {}, first25: null, tiers: {}, titled: {}, lv: {}, rank: {}, T5: {}, gold: {}, troops: {} };
  const N = g.cities.length;
  for (let m = 0; m < 12 * years; m++) {
    if (T.on) aiManageAll();
    StrategyAI.runAI([]);
    StrategyAI.endMonth();
    for (const x of g.generals) if (x.faction >= 0 && !x.dead && !g.isRuler(x) && x.loyalty < 40) T.lowLoy++;
    if (out.first25 == null) for (const f of g.factions) if (f.alive && g.cityCount(f.id) > N * 0.25) { out.first25 = m + 1; break; }
    if ((m + 1) % 12 === 0) {
      const y = (m + 1) / 12;
      out.alive[y] = g.factions.filter(f => f.alive).length;
      out.top[y] = Math.max(...g.factions.map(f => g.cityCount(f.id)));
      const live = g.generals.filter(x => x.faction >= 0 && !x.dead && !g.isRuler(x));
      const tc = [0, 0, 0, 0, 0, 0];
      for (const x of live) if (act(x)) tc[x._t.tier]++;
      out.tiers[y] = tc.slice(1);
      out.titled[y] = live.length ? live.filter(act).length / live.length : 0;
      out.lv[y] = median(live.map(x => x.level));
      out.T5[y] = tc[5];
      const rk = [0, 0, 0, 0, 0, 0]; for (const f of g.factions) if (f.alive) rk[T.on ? updateRank(f.id) : 1]++;
      out.rank[y] = rk.slice(1);
      (out.emp = out.emp || {})[y] = g.generals.filter(x => x.faction >= 0 && !x.dead).length;
      out.gold[y] = g.cities.reduce((s, c) => s + (c.owner >= 0 ? c.gold : 0), 0);
      out.troops[y] = g.generals.reduce((s, x) => s + (x.faction >= 0 && !x.dead ? x.troops : 0), 0);
    }
  }
  out.salaryShare = T.gross > 0 ? (T.salary + (T.grantGold || 0)) / T.gross : 0;
  out.defect = T.defect; out.unpaid = T.unpaid || 0; out.loyDrop = T.loyDrop || 0; out.lowLoy = T.lowLoy;
  out.lvHist = {}; for (const x of g.generals) if (x.faction >= 0 && !x.dead) out.lvHist[x.level] = (out.lvHist[x.level] || 0) + 1;
  return out;
}

function runCampaigns(seeds, years) {
  const variants = ['off', 'on', 'dom'];
  const res = {};
  const t0 = Date.now();
  for (const v of variants) { res[v] = []; for (let s = 1; s <= seeds; s++) res[v].push(campaign(s, years, v)); }
  const ys = SCEN === 'world' ? [1, 3, 5, 10, 15].filter(y => y <= years) : [1, 3, 5, 10, 20, 25].filter(y => y <= years);
  console.log(`== ${SCEN}: ${seeds} seeds × ${years} years, all-AI; variants off / on (titles) / dom (titles + domestic exp)  [${((Date.now() - t0) / 1000).toFixed(1)}s]`);
  for (const v of variants) {
    const r = res[v];
    const f25 = r.map(o => (o.first25 == null ? Infinity : o.first25)).sort((a, b) => a - b);
    console.log(`-- ${v}: factions alive ${ys.map(y => y + 'y ' + avg(r.map(o => o.alive[y])).toFixed(1)).join(', ')}; top cities ${ys.map(y => y + 'y ' + avg(r.map(o => o.top[y])).toFixed(1)).join(', ')}`);
    console.log(`   first > 25% median ${f25[f25.length >> 1] === Infinity ? 'never' : (f25[f25.length >> 1] / 12).toFixed(1) + 'y'} (earliest ${f25[0] === Infinity ? '-' : (f25[0] / 12).toFixed(1) + 'y'}); defections/seed ${avg(r.map(o => o.defect)).toFixed(1)}; unpaid holder-months/seed ${avg(r.map(o => o.unpaid || 0)).toFixed(0)}; endMonth loyalty loss/seed ${avg(r.map(o => o.loyDrop)).toFixed(0)}; gen-months loyalty<40 ${avg(r.map(o => o.lowLoy)).toFixed(0)}; gold ${ys.map(y => y + 'y ' + (avg(r.map(o => o.gold[y])) | 0)).join(' ')}; troops ${ys.map(y => y + 'y ' + (avg(r.map(o => o.troops[y])) / 1000).toFixed(0) + 'k').join(' ')}; employed ${ys.map(y => y + 'y ' + avg(r.map(o => o.emp[y])).toFixed(0)).join(' ')}`);
    if (v !== 'off') {
      console.log(`   titled share of non-ruler officers ${ys.map(y => y + 'y ' + (100 * avg(r.map(o => o.titled[y]))).toFixed(0) + '%').join(', ')}; (salary + grant gifts) / gross income ${(100 * avg(r.map(o => o.salaryShare))).toFixed(1)}%`);
      console.log(`   active titles by tier T1..T5 (avg): ${ys.map(y => y + 'y [' + [0, 1, 2, 3, 4].map(k => avg(r.map(o => o.tiers[y][k])).toFixed(1)).join(' ') + ']').join(' ')}`);
      console.log(`   ruler ranks R1..R5 (alive factions, avg): ${ys.map(y => y + 'y [' + [0, 1, 2, 3, 4].map(k => avg(r.map(o => o.rank[y][k])).toFixed(1)).join(' ') + ']').join(' ')}`);
    }
    console.log(`   median level of officers ${ys.map(y => y + 'y ' + avg(r.map(o => o.lv[y])).toFixed(1)).join(', ')}; end level hist (sum) ${JSON.stringify(r.reduce((h, o) => { for (const k of Object.keys(o.lvHist)) h[k] = (h[k] || 0) + o.lvHist[k]; return h; }, {}))}`);
  }
}

// ---- 第 8 年局面的战术战斗 ----
function fightAI(Mdl) {
  let guard = 0;
  while (Mdl.result === 0 && guard++ < 2000) {
    for (let pass = 0; pass < 4 && Mdl.result === 0; pass++) {
      const order = Mdl.alive(Mdl.side).filter(u => Mdl.canAct(u));
      if (!order.length) break;
      for (const u of order) {
        if (!Mdl.canAct(u) || Mdl.result !== 0) continue;
        const plan = Mdl.planFor(u);
        if (plan.move) Mdl.move(u, plan.move);
        if (Mdl.result !== 0) break;
        const a = Mdl.planAction(u);
        if (a.kind === 'attack') Mdl.attack(u, a.target);
        else if (a.kind === 'tactic') Mdl.useTactic(u, a.tactic, a.target);
        else if (a.kind === 'duel') Mdl.duel(u, a.target);
        else if (a.kind === 'special') Mdl.useSpecial(u, a.target);
        Mdl.spend(u);
      }
    }
    if (Mdl.result === 0) Mdl.endSide();
  }
}
function tactical(seeds) {
  const snaps = [];
  for (let s = 1; s <= seeds; s++) {
    T.on = true; T.dom = true; T.rank = new Map(); T.seq = 0;
    SG.Random.seed(s);
    const g = SG.G = GameState.newGame(SG.ScenarioData.Factions[0].split('|')[0], SCEN);
    g.player = -1;
    for (let m = 0; m < 96; m++) { aiManageAll(); StrategyAI.runAI([]); StrategyAI.endMonth(); }
    snaps.push(JSON.stringify(g));
  }
  const modes = ['none', 'both', 'atk', 'def'];
  const out = {};
  let titledUnits = 0, units = 0;
  for (const mode of modes) {
    let n = 0, w = 0, lossA = 0, lossD = 0, days = 0;
    snaps.forEach((text, si) => {
      for (let k = 0; k < 60; k++) {
        const g = SG.G = GameState.fromJSON(text);
        g.player = -1;
        SG.Random.seed(900 + si * 100 + k);
        const cand = [];
        for (const c of g.cities) {
          if (c.owner < 0) continue;
          for (const j of c.links) { const t = g.cities[j]; if (t.owner >= 0 && t.owner !== c.owner && g.officersIn(c).some(x => x.troops > 500) && g.officersIn(t).length) cand.push([c, t]); }
        }
        if (!cand.length) continue;
        const [c, t] = cand[(k * 5 + si) % cand.length];
        const atk = SG.Seq.orderByDesc(g.officersIn(c).filter(x => x.troops > 0), x => x.troops).slice(0, 5);
        const s = Conquest.prepare(c.owner, c, t, atk, 3000, 0);
        T.battle = mode;
        const Mdl = new BattleModel(s);
        for (const u of Mdl.units) if (useFx(u)) u.morale = Math.min(100, u.morale + fx(u.gen, 'morale'));
        if (mode === 'both') { for (const u of Mdl.units) { units++; if (act(u.gen)) titledUnits++; } }
        const t0s = Mdl.units.map(u => u.troops);
        fightAI(Mdl);
        T.battle = 'none';
        n++; days += Mdl.day; if (Mdl.result === 1) w++;
        Mdl.units.forEach((u, i) => { const l = Math.max(0, t0s[i] - Math.max(0, u.troops)); if (u.side === 0) lossA += l; else lossD += l; });
      }
    });
    out[mode] = { n, w: w / Math.max(1, n), x: lossD / Math.max(1, lossA), days: days / Math.max(1, n) };
  }
  const pct = v => (100 * v).toFixed(1) + '%';
  console.log(`== tactical (${SCEN}, year-8 armies from ${seeds} titled campaigns, ${out.none.n} battles per mode; ${(100 * titledUnits / Math.max(1, units)).toFixed(0)}% of units titled)`);
  for (const mode of modes) console.log(`  title effects ${mode.padEnd(4)}: attacker win ${pct(out[mode].w)}, days ${out[mode].days.toFixed(1)}, defender/attacker losses ${out[mode].x.toFixed(2)}`);
  console.log(`  shift both vs none ${(100 * (out.both.w - out.none.w)).toFixed(1)} pp; swing atk-only vs def-only ${(100 * (out.atk.w - out.def.w)).toFixed(1)} pp`);
}

if (MODE === 'tactical') tactical(parseInt(process.argv[3], 10) || 6);
else runCampaigns(parseInt(process.argv[3], 10) || (SCEN === 'world' ? 6 : 8), parseInt(process.argv[4], 10) || (SCEN === 'world' ? 15 : 25));
