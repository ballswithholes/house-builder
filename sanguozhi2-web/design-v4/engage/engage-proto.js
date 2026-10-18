'use strict';
/* ==========================================================================
   design-v4/engage/engage-proto.js —— ENGAGEMENT.md 的参考实现与平衡估算（一次性；只在内存里给游戏函数打补丁，不改任何游戏文件）
   规则：民心（order）、声望（prestige → 人望）、内政经验与政绩、随机事件（catalog.js 的声明式效果）、节庆、文会、
         邦交（好感、赠礼、联姻、纳贡 / 索贡、质子、停战、通商）、羁绊（亲密度、结义、师徒、宿敌 …）、
         营建、商队、探索、国策、成就、王道胜利条件。全部走自己的种子流（不耗 SG.Random）。
   用法：
     node design-v4/engage/engage-proto.js campaign classic [种子数=24] [年数=25]   全电脑：off / on / on+aiDip / on+aiProj
     node design-v4/engage/engage-proto.js campaign world   [种子数=8]  [年数=15]
     node design-v4/engage/engage-proto.js player   classic [种子数=6]  [年数=20]   脚本玩家：ai（交给电脑）/ v3（只内政）/ v4（内政 + 第四版玩法）
     node design-v4/engage/engage-proto.js player   world   [种子数=4]  [年数=15]
   环境变量：VARIANTS=off,on,…（只跑部分变体）；PLAYERS=liubei,cao,…；POL=ai,v3,v4；SEED0=种子偏移
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const MODE = process.argv[2] || 'campaign';
const SCEN = process.argv[3] === 'world' ? 'world' : 'classic';
const root = path.join(__dirname, '..', '..', 'js');
const load = (f, patch) => { let src = fs.readFileSync(path.join(root, f), 'utf8'); if (patch) src = patch(src); vm.runInThisContext(src, { filename: f }); };
for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) load(f);
const SG = global.SG;
SG.ScenarioData = SG.Scenarios[SCEN];
load('model.js');
// strategy-ai.js：在 attackNeed 里加一个钩子（内存中改源码文本；仓库文件不变）
load('strategy-ai.js', src => {
  const a = 'function attackNeed(f, t) { const g = G(); return t.owner >= 0 && g.cityCount(t.owner) < g.cityCount(f) ? 1.4 : 1.6; }';
  if (src.indexOf(a) < 0) throw new Error('attackNeed text changed');
  return src.replace(a, 'function attackNeed(f, t) { const g = G(); const b = t.owner >= 0 && g.cityCount(t.owner) < g.cityCount(f) ? 1.4 : 1.6; return SG.__needK ? b + SG.__needK(f, t) : b; }');
});
for (const f of ['battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js']) load(f);
const { GameState, Commands: C, StrategyAI, Conquest, Balance: B, M, Progress } = SG;
const CAT = require('./catalog.js');
const CS = require('../domestic/city-stages.js');

// ------------------------------------------------------------------ 工具 --
function hash32(str) { let h = 0x811c9dc5; for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 0x01000193); } return h >>> 0; }
const G = () => SG.G;
const rng = (...k) => SG.SeededRandom(hash32(G().seed + '|' + G().monthIndex + '|' + k.join('|')));
const inc = c => c.industry * B.GoldPerIndustry + c.town * B.GoldPerTown;
const harv = c => c.land * B.FoodPerLand;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const sum = (a, fn) => a.reduce((s, x) => s + (fn ? fn(x) : x), 0);
const avg = a => (a.length ? sum(a) / a.length : 0);
const famOf = c => CAT.FAMILY[c.culture] || 'hua';
const facFam = f => CAT.FAMILY[G().factions[f].culture] || 'hua';
const facInc = f => sum(G().citiesOf(f), inc);
const power = f => sum(G().generalsOf(f), x => x.troops);
const RIVER = new Set(CAT.RIVER);
const EV = Object.fromEntries(CAT.EVENTS.map(e => [e.id, e]));
const PROJ = Object.fromEntries(CAT.PROJECTS.map(p => [p.id, p]));

// ------------------------------------------------------------------ 开关 --
const E = {
  on: false,        // 第四版玩法总开关（off = 原版，结果应与未打补丁一致）
  order: true, events: true, domexp: true, prestige: true, rel: true, bonds: true,
  aiDip: false,     // 电脑之间的纳贡 / 联姻
  aiProj: false,    // 电脑营建
  playerPol: null,  // 'v3' | 'v4' | null（null = 无玩家 / 电脑代管）
  stats: null,
};
// 子系统开关（诊断用）：OFFSYS=order,events,rel,need,bonds,domexp,prestige,soft
const OFFSYS = new Set((process.env.OFFSYS || '').split(',').filter(Boolean));
function applyOff() { for (const k of ['order', 'events', 'rel', 'bonds', 'domexp', 'prestige']) E[k] = !OFFSYS.has(k); E.need = !OFFSYS.has('need'); E.soft = !OFFSYS.has('soft'); }
function resetStats() {
  E.stats = { events: {}, eventsByFac: 0, facMonths: 0, evValue: 0, evValueN: 0, gross: 0, orderAdj: 0, defect: 0, levelUps: 0, domExp: 0, pacts: {}, aiTribute: 0, aiMarriage: 0, aiProj: 0, demand: 0 };
}
resetStats();

// ------------------------------------------------------------------ 状态 --
function initState(g) {
  const n = g.factions.length;
  g._e = { rel: new Float64Array(n * n), war: new Int32Array(n * n).fill(-999), pacts: [], bonds: new Map(), expUsed: new Map(), fac: g.factions.map(() => ({ prestige: 0, fameEv: 0, virtueEv: 0, cd: {}, baseFame: 0, baseVirtue: 0 })), mission: [], caravans: [], exped: [], log: [], counters: {}, ach: new Set(), visited: new Set(), seq: 0 };
  g.factions.forEach((f, i) => { g._e.fac[i].baseFame = f.fame; g._e.fac[i].baseVirtue = f.virtue; });
  for (const c of g.cities) {
    const v = c.owner >= 0 ? g.factions[c.owner].virtue : 40;
    c._e = { order: clamp(Math.round(45 + v / 5 + c.town / 150), 30, 75), buffs: [], flags: {}, cd: {}, lastDis: -999, done: new Set(), proj: null, stage: 0 };
  }
  // 初始邦交
  const keyId = k => { const f = g.factionByKey(k); return f ? f.id : -1; };
  for (const [a, b, v] of CAT.RELATIONS) {
    const ia = keyId(a);
    if (ia < 0) continue;
    if (b.startsWith('*')) { const reg = b.slice(1); for (const f of g.factions) if (f.id !== ia && f.region === reg) setRel(ia, f.id, v); }
    else { const ib = keyId(b); if (ib >= 0) setRel(ia, ib, v); }
  }
  // 初始羁绊
  const byName = new Map(g.generals.map(x => [x.name, x]));
  for (const [a, b, kind, aff] of CAT.BONDS) {
    const x = byName.get(a), y = byName.get(b);
    if (x && y) g._e.bonds.set(bkey(x.id, y.id), { k: kind, a: aff, m: kind === 'mentor' ? x.id : -1 });
  }
}
const idx = (a, b) => a * G().factions.length + b;
function rel(a, b) { return a < 0 || b < 0 || a === b ? 0 : G()._e.rel[idx(a, b)]; }
function setRel(a, b, v) { if (a < 0 || b < 0 || a === b) return; v = clamp(v, -100, 100); G()._e.rel[idx(a, b)] = v; G()._e.rel[idx(b, a)] = v; }
function addRel(a, b, d) { if (!E.rel) return; setRel(a, b, rel(a, b) + d); }
function atWar(a, b) { return a >= 0 && b >= 0 && G().monthIndex - G()._e.war[idx(a, b)] < 12; }
function pactOf(a, b, kind) { return G()._e.pacts.find(p => ((p.a === a && p.b === b) || (p.a === b && p.b === a)) && (!kind || p.kind === kind) && p.until > G().monthIndex); }
const NONAGGRO = new Set(['marriage', 'tribute', 'truce']);
function nonAggro(a, b) { return G()._e.pacts.some(p => ((p.a === a && p.b === b) || (p.a === b && p.b === a)) && NONAGGRO.has(p.kind) && p.until > G().monthIndex); }
function addPact(a, b, kind, months, extra) { const p = Object.assign({ a, b, kind, until: G().monthIndex + months, since: G().monthIndex }, extra || {}); G()._e.pacts.push(p); E.stats.pacts[kind] = (E.stats.pacts[kind] || 0) + 1; return p; }
const bkey = (a, b) => (a < b ? a + '-' + b : b + '-' + a);
function bond(a, b) { return G()._e.bonds.get(bkey(a.id, b.id)) || null; }
function addAff(a, b, d) {
  if (!E.bonds || a.id === b.id) return;
  const k = bkey(a.id, b.id), m = G()._e.bonds;
  let o = m.get(k);
  if (!o) { if (Math.abs(d) < 1) return; o = { k: null, a: 0, m: -1 }; m.set(k, o); }
  o.a = clamp(o.a + d, -100, 100);
  if (!o.k && o.a >= 50) o.k = 'friend';
  if (!o.k && o.a <= -50) o.k = 'feud';
  if (o.k === 'friend' && o.a < 30) o.k = null;
  if (o.k === 'feud' && o.a > -30) o.k = null;
}
// 声望 → 人望（上限 +10，含事件所得）；德的事件所得 ±10
function syncFV(f) {
  const e = G()._e.fac[f], F = G().factions[f];
  if (!E.on || !E.prestige) return;
  const bonus = Math.min(10, Math.floor(e.prestige / 150) + e.fameEv);
  F.fame = clamp(e.baseFame + bonus, 0, 100);
  F.virtue = clamp(e.baseVirtue + clamp(e.virtueEv, -10, 10), 0, 100);
}
function addPrestige(f, v) { if (f < 0 || !E.prestige) return; const e = G()._e.fac[f]; e.prestige = Math.max(0, e.prestige + v); syncFV(f); }

// ------------------------------------------------------------------ 内政经验（路线：每月每人至多 20） --
const ROUTINE_CAP = +(process.env.CAP || 20);
const RECRUIT_ORDER = +(process.env.RECORD || 3000);   // 每征兵 RECRUIT_ORDER 人民心 −1（向上取整）
const ORDER_STEP = +(process.env.OSTEP || 2);         // 民心低于目标时每月回升；高于目标时每月 −1
function domExp(gen, amt) {
  if (!E.on || !E.domexp || !gen || !(amt > 0)) return;
  const m = G()._e.expUsed, used = m.get(gen.id) || 0, add = Math.min(amt, ROUTINE_CAP - used);
  if (add <= 0) return;
  m.set(gen.id, used + add);
  storyExp(gen, add, true);
}
function storyExp(gen, amt, routine) {
  if (!gen || !(amt > 0) || gen.dead) return;
  const e0 = gen.exp;
  const r = Progress.gain(gen, amt, routine ? 'domestic' : 'story');
  gen._dexp = (gen._dexp || 0) + (gen.exp - e0);      // 非战斗经验（老练只按一半计，见 ENGAGEMENT §4.4）
  if (r && r.to > r.from) E.stats.levelUps++;
  E.stats.domExp += amt;
}

// ------------------------------------------------------------------ 补丁：指令 --
const oDev = C.develop, oTrain = C.train, oRec = C.recruit, oSearch = C.search, oHire = C.hire, oAlly = C.ally, oDiscord = C.discord, oPersuade = C.persuade, oAllyCh = C.allyChance;
C.develop = function (c, g, kind) {
  const key = kind === SG.DevKind.Land ? 'land' : kind === SG.DevKind.Industry ? 'industry' : 'town';
  const before = c[key];
  const r = oDev.apply(this, arguments);
  if (E.on && c[key] > before) {
    if (c._e && c._e.done.has('forge') && key === 'industry') c[key] = Math.min(B.StatMax, c[key] + Math.round((c[key] - before) * 0.10));
    domExp(g, 6 + (c[key] - before >= 20 ? 2 : 0));
  }
  return r;
};
C.train = function (g) { const b = g.training; const r = oTrain.apply(this, arguments); if (E.on && g.training > b) { const c = G().cities[g.city]; if (c._e && c._e.done.has('drill')) g.training = Math.min(100, g.training + 3); domExp(g, 3); } return r; };
C.recruit = function (c, g, troops) {
  const b = g.troops;
  const r = oRec.apply(this, arguments);
  const n = g.troops - b;
  if (E.on && n > 0) { domExp(g, 3); if (E.order && c._e) c._e.order = clamp(c._e.order - Math.ceil(n / RECRUIT_ORDER), 0, 100); }
  return r;
};
C.search = function (c, g) {
  const r = oSearch.apply(this, arguments);
  if (E.on) { domExp(g, C.searchFound ? 12 : 4); if (C.searchFound && g.faction >= 0) cnt(g.faction, 'hiddenFound', 1); }
  return r;
};
C.hire = function (target, recruiter, faction) { const ok = oHire.apply(this, arguments); if (E.on) domExp(recruiter, ok ? 10 : 2); if (E.on && ok) cnt(faction, 'hires', 1); return ok; };
C.allyChance = function (f, target, gift) { const p = oAllyCh.apply(this, arguments); return E.on && E.rel ? M.clamp(p + rel(f, target) / 400, 0.05, 0.9) : p; };
C.ally = function (from, f, target, gift) { const r = oAlly.apply(this, arguments); if (E.on && G().allied(f, target)) addRel(f, target, 10); return r; };
C.discord = function (agent, target) { const b = target.loyalty; const r = oDiscord.apply(this, arguments); if (E.on) { domExp(agent, target.loyalty < b ? 6 : 2); addRel(agent.faction, target.faction, -5); } return r; };
C.persuade = function (agent, target, f) { const of = target.faction; const r = oPersuade.apply(this, arguments); if (E.on) { domExp(agent, target.faction === f ? 15 : 2); addRel(f, of, -8); } return r; };

// 外出（商队、探索、质子、游学）的武将不在城中：generalsIn 过滤
const oGI = GameState.prototype.generalsIn;
GameState.prototype.generalsIn = function (city, f) { const a = oGI.call(this, city, f); return E.on ? a.filter(x => !x._away || x._away.until <= this.monthIndex) : a; };
// 互不侵犯（联姻、纳贡、停战）与好感 ≥ 60 视同同盟（电脑不攻打）
const oAllied = GameState.prototype.allied;
GameState.prototype.allied = function (a, b) {
  if (oAllied.call(this, a, b)) return true;
  if (!E.on || !this._e || a < 0 || b < 0 || a === b) return false;
  return nonAggro(a, b) || (E.rel && E.soft && rel(a, b) >= 60);
};
SG.__needK = function (f, t) {
  if (!E.on || !E.rel || !E.need || t.owner < 0) return 0;
  let k = 0;
  // （试过：好感 ≤ −50 时攻城所需兵力比 −0.2——全电脑战役的兼并速度加快一倍，故不采用；坏关系只减少示好，不增加侵略）
  if (process.env.NEED_EAGER && rel(f, t.owner) <= -50) k -= 0.2;
  const e = G()._e.fac[t.owner];
  if (e && e.refused && e.refused[f] > G().monthIndex) k -= 0.15;
  return k;
};
const oCD = SG.cityDefense;
SG.cityDefense = function (c) {
  let d = oCD(c);
  if (E.on && c._e) { if (c._e.done.has('walls')) d += 10; d += buffSum(c, 'defense'); }
  return Math.max(10, d);
};

// ------------------------------------------------------------------ 补丁：战争 --
// 声望来源（ENGAGEMENT §3.2）
const PR = { conquest: 5, destroy: 20, defend: 2, project: 10, wonder: 50, cityLevel: 10, alliance: 5, marriage: 20, truce: 5, tributeIn: 1 };
const oPrep = Conquest.prepare, oApply = Conquest.apply;
Conquest.prepare = function (attacker, src, target) {
  const s = oPrep.apply(this, arguments);
  if (E.on && target.owner >= 0) {
    const g = G();
    g._e.war[idx(attacker, target.owner)] = g._e.war[idx(target.owner, attacker)] = g.monthIndex;
    addRel(attacker, target.owner, -25);
    // 背约：攻打联姻 / 纳贡 / 停战的对象
    const p = g._e.pacts.find(q => ((q.a === attacker && q.b === target.owner) || (q.b === attacker && q.a === target.owner)) && q.until > g.monthIndex && NONAGGRO.has(q.kind));
    if (p) { p.until = g.monthIndex; const e = g._e.fac[attacker]; e.virtueEv -= p.kind === 'marriage' ? 15 : 8; addPrestige(attacker, p.kind === 'marriage' ? -50 : -20); syncFV(attacker); }
    if (attacker === g.player) g._e.lastAttack = g.monthIndex;
  }
  return s;
};
const oAuto = Conquest.autoResolve;
Conquest.autoResolve = function (s) { s.atkTroops0 = sum(s.atk, x => x.troops); s.defTroops0 = sum(s.def, x => x.troops); return oAuto.apply(this, arguments); };
Conquest.apply = function (s) {
  const ownerBefore = s.target.owner;
  if (s.atkTroops0 === undefined) { s.atkTroops0 = sum(s.atk, x => x.troops); s.defTroops0 = sum(s.def, x => x.troops); }
  const r = oApply.apply(this, arguments);
  if (!E.on) return r;
  const g = G();
  if (s.attackerWon && s.target._e) {
    s.target._e.order = Math.max(15, s.target._e.order - 15);
    if (ownerBefore >= 0) { addRel(s.attacker, ownerBefore, -15); addPrestige(s.attacker, PR.conquest); if (!g.factions[ownerBefore].alive) addPrestige(s.attacker, PR.destroy); }
    s.target._e.trouble = g.monthIndex;
  } else if (s.target._e) { s.target._e.order = Math.max(0, s.target._e.order - 3); if (s.defender >= 0) { if (s.atkTroops0 >= s.defTroops0) addPrestige(s.defender, PR.defend); cnt(s.defender, 'defenses', 1); } }
  for (const x of s.atk.concat(s.def)) x._lastBattle = g.monthIndex;
  // 并肩作战且得胜：亲密度 +4
  const winners = s.attackerWon ? s.atk : s.def;
  for (let i = 0; i < winners.length; i++) for (let j = i + 1; j < winners.length; j++) if (winners[i].faction === winners[j].faction) addAff(winners[i], winners[j], 4);
  return r;
};

// ------------------------------------------------------------------ 城：增益 / 民心 --
function buffSum(c, mod) { let v = 0; for (const b of c._e.buffs) if (b.mod === mod && b.until > G().monthIndex) v += b.v; return v; }
function buffMul(c, mod) { let v = 1; for (const b of c._e.buffs) if (b.mod === mod && b.until > G().monthIndex) v *= b.v; return v; }
function nextHarvestIdx() { const g = G(); return g.month <= B.HarvestMonth ? g.year * 12 + B.HarvestMonth + 2 : (g.year + 1) * 12 + B.HarvestMonth + 2; }
function orderTarget(c) {
  const g = G();
  if (c.owner < 0) return 40;
  const v = g.factions[c.owner].virtue;
  let t = 45 + v / 5 + buffSum(c, 'orderTarget');
  const gov = c.governor >= 0 ? g.generals[c.governor] : null;
  if (gov && gov.pol >= 80) t += 3;
  for (const id of c._e.done) { const p = PROJ[id]; if (p && p.fx.orderTarget) t += p.fx.orderTarget; }
  return clamp(t, 0, 95);
}

// ------------------------------------------------------------------ 事件引擎 --
function amount(o, c, k, cur) {
  if (o.add !== undefined) return o.add;
  if (o.pct !== undefined) return cur * o.pct;
  if (o.inc !== undefined) return inc(c) * o.inc;
  if (o.harv !== undefined) return harv(c) * o.harv;
  if (o.range) return o.range[0] + Math.floor(k.nextDouble() * (o.range[1] - o.range[0] + 1));
  return 0;
}
function gv(c, key) { return c[key]; }
// 金价值（统计用）：粮 0.12、开发点 2.6、兵 0.2、民心 1 点 = 本城收入 × 0.3
function valueOf(key, d, c) {
  switch (key) {
    case 'gold': return d; case 'food': return d * 0.12;
    case 'land': case 'industry': case 'town': return d * 2.6;
    case 'troops': case 'gtroops': return d * 0.2;
    case 'population': return d / 10 * 0.2 * 0.2;
    case 'order': return d * inc(c) * 0.3;
    default: return 0;
  }
}
function applyEffects(list, ctx) {
  const g = G(), c = ctx.c;
  let val = 0;
  for (const o of list || []) {
    const k = o.k;
    const who = o.who === 'a' ? ctx.a : o.who === 'b' ? ctx.b : o.who === 'gov' ? ctx.gov : o.who === 'subject' ? ctx.subject : o.who === 'winner' ? ctx.a : o.who === 'loser' ? ctx.b : ctx.actor;
    if (['gold', 'food', 'land', 'industry', 'town', 'population'].includes(k)) {
      let d = amount(o, c, ctx.k, gv(c, k));
      if (d < 0 && (k === 'food' || k === 'gold') && ctx.disaster && c._e.done.has('granary') && k === 'food') d *= 0.5;
      const before = c[k];
      const cap = k === 'land' || k === 'industry' || k === 'town' ? B.StatMax : k === 'population' ? 2000000 : Infinity;
      c[k] = Math.round(clamp(c[k] + d, 0, cap));
      val += valueOf(k, c[k] - before, c);
    } else if (k === 'order') {
      const d = amount(o, c, ctx.k, 0);
      const cs = o.all ? g.citiesOf(ctx.f) : [c];
      for (const cc of cs) { cc._e.order = clamp(cc._e.order + d, 0, 100); val += valueOf('order', d, cc); }
    } else if (k === 'troops') {
      for (const x of g.officersIn(c)) { const d = Math.round(x.troops * (o.pct || 0)); x.troops = Math.max(0, x.troops + d); val += valueOf('troops', d, c); }
    } else if (k === 'training') {
      for (const x of g.officersIn(c)) if (x.troops > 0) x.training = clamp(x.training + (o.add || 0), 0, 100);
    } else if (k === 'gtroops') {
      if (!who) continue;
      if (o.pct !== undefined) { const d = Math.round(who.troops * o.pct); who.troops = Math.max(0, who.troops + d); val += valueOf('troops', d, c); continue; }
      const room = Math.max(0, SG.maxTroops(who) - who.troops);
      const n = Math.min(room, Math.round(amount(o, c, ctx.k, 0)));
      if (n > 0) { const tr = o.training || 30; who.training = Math.round((who.training * who.troops + tr * n) / (who.troops + n)); who.troops += n; val += valueOf('troops', n, c); }
    } else if (k === 'prestige') { addPrestige(ctx.f, amount(o, c, ctx.k, 0)); }
    else if (k === 'fame') { g._e.fac[ctx.f].fameEv += o.add; syncFV(ctx.f); }
    else if (k === 'virtue') { g._e.fac[ctx.f].virtueEv += o.add; syncFV(ctx.f); }
    else if (k === 'exp') { if (who) storyExp(who, (o.add || 0) + (o.intelBonus && who.intel >= 70 ? o.intelBonus : 0)); }
    else if (k === 'expLater') { if (who) storyExp(who, o.add); }
    else if (k === 'loyalty') { if (who && !g.isRuler(who)) who.loyalty = clamp(who.loyalty + o.add, 0, 100); }
    else if (k === 'officers.loyalty') { for (const x of g.officersIn(c)) if (!g.isRuler(x)) x.loyalty = clamp(x.loyalty + o.add, 0, 100); }
    else if (k === 'loyalty3') { const xs = g.generalsIn(c.id, ctx.f).filter(x => !g.isRuler(x)).sort((p, q) => p.loyalty - q.loyalty).slice(0, 3); for (const x of xs) x.loyalty = clamp(x.loyalty + Math.round(amount(o, c, ctx.k, 0)), 0, 100); }
    else if (k === 'bond3') { const xs = g.generalsIn(c.id, ctx.f).slice(0, 4); for (let i = 0; i < xs.length; i++) for (let j = i + 1; j < xs.length; j++) addAff(xs[i], xs[j], o.add); }
    else if (k === 'buff') { const until = o.untilHarvest ? nextHarvestIdx() : g.monthIndex + (o.months || 1); c._e.buffs.push({ mod: o.mod, v: o.v, until }); }
    else if (k === 'flag') { c._e.flags[o.id] = g.monthIndex + (o.months || 0); }
    else if (k === 'rel') {
      const targets = o.who === 'neighbors' ? neighbors(ctx.f) : o.who === 'region' ? g.factions.filter(x => x.alive && x.region === g.factions[ctx.f].region && x.id !== ctx.f).map(x => x.id) : o.who === 'far' ? (ctx.far >= 0 ? [ctx.far] : []) : o.who === 'subject' ? (ctx.subject && ctx.subjectFac >= 0 ? [ctx.subjectFac] : []) : (ctx.origin >= 0 ? [ctx.origin] : []);
      for (const t of targets) addRel(ctx.f, t, o.add);
    } else if (k === 'bond') { if (ctx.a && ctx.b) { addAff(ctx.a, ctx.b, o.add); if (o.rivalIfClose && Math.abs(ctx.a.war - ctx.b.war) <= 6 && ctx.a.war >= 85 && ctx.k.nextDouble() < o.rivalIfClose) { const bo = bond(ctx.a, ctx.b); if (bo && !bo.k) bo.k = 'rival'; } } }
    else if (k === 'bondKind') { if (ctx.a && ctx.b) { const bo = bond(ctx.a, ctx.b) || { a: 0, m: -1 }; bo.k = o.kind; if (o.kind === 'mentor') bo.m = ctx.a.id; g._e.bonds.set(bkey(ctx.a.id, ctx.b.id), bo); cnt(ctx.f, o.kind === 'sworn' ? 'sworn' : 'mentors', 1); } }
    else if (k === 'reveal') {
      let pool = [];
      const near = o.who === 'near2' ? new Set([c.id, ...c.links, ...c.links.flatMap(i => g.cities[i].links)]) : new Set([c.id]);
      pool = g.generals.filter(x => x.hidden && SG.isFree(x) && near.has(x.city) && (o.who !== 'bandit' || (x.war >= 60 && x.intel < 60)));
      if (pool.length) { const h = pool[Math.floor(ctx.k.nextDouble() * pool.length)]; h.hidden = false; cnt(ctx.f, 'revealed', 1); }
      else if (o.orMission && ctx.f === g.player) offerMission(ctx.f, true);
    } else if (k === 'item') { gotItem(ctx.f, rollRarity(o.w, ctx.k), 'event'); }
    else if (k === 'away') { if (who) who._away = { kind: 'leave', until: g.monthIndex + o.add }; }
    else if (k === 'defect') {
      const s = ctx.subject; if (!s || s.faction < 0) continue;
      const of = s.faction, oc = g.cities[s.city];
      s.faction = ctx.f; s.city = c.id; s.troops = Math.round(s.troops * (o.troopsK || 0.5)); s.loyalty = clamp(55 + Math.round(g.factions[ctx.f].virtue / 5), 40, 95); s.moved = true;
      g.autoGovernor(oc); g.autoGovernor(c);
      if (g.officersIn(oc).length === 0) { oc.owner = -1; g.checkFactionDeath(of); }
    } else if (k === 'separate') { /* 原型：只记录 */ }
    else if (k === 'plan') { /* 献策：原型里只计数 */ cnt(ctx.f, 'plans', 1); }
    else if (k === 'roll3') { const r = ctx.k.nextDouble(); let i = 0, acc = 0; for (; i < o.p.length; i++) { acc += o.p[i]; if (r < acc) break; } val += applyEffects(o.outcomes[Math.min(i, o.outcomes.length - 1)], ctx); }
  }
  return val;
}
function neighbors(f) {
  const g = G(), s = new Set();
  for (const c of g.citiesOf(f)) for (const j of c.links) { const o = g.cities[j].owner; if (o >= 0 && o !== f) s.add(o); }
  return [...s];
}
function threatened(f, c) { const g = G(); let t = 0; for (const j of c.links) { const n = g.cities[j]; if (n.owner >= 0 && n.owner !== f && !g.allied(n.owner, f)) t += g.troopsIn(n); } return t > g.troopsIn(c) * 1.1; }

function condOK(ev, c, f, ctx) {
  const w = ev.when || {}, g = G();
  if (w.scenario && w.scenario !== g.scenario) return false;
  if (w.months && w.months.indexOf(g.month) < 0) return false;
  const fam = famOf(c);
  if (w.families && w.families.indexOf(fam) < 0) return false;
  if (w.notFamilies && w.notFamilies.indexOf(fam) >= 0) return false;
  if (w.minLat && c.lat < w.minLat && fam === 'hua') return false;
  if (w.river && !RIVER.has(c.key)) return false;
  if (w.riverOrCoast && !RIVER.has(c.key) && !isCoast(c)) return false;
  if (w.minLand && c.land < w.minLand) return false;
  if (w.minTown && c.town < w.minTown) return false;
  if (w.minPop && c.population < w.minPop) return false;
  if (w.minOrder && c._e.order < w.minOrder) return false;
  if (w.minTier && CS.shopTier({ c }) < w.minTier) return false;
  const F = g.factions[f];
  if (w.minVirtue && F.virtue < w.minVirtue) return false;
  if (w.minFame && F.fame < w.minFame) return false;
  if (w.minCities && g.cityCount(f) < w.minCities) return false;
  if (w.minPrestige && g._e.fac[f].prestige < w.minPrestige) return false;
  if (w.date && w.date.to) { const [y, m] = w.date.to.split('-').map(Number); if (g.year * 12 + g.month > y * 12 + m) return false; }
  if (w.govPol) { const gov = c.governor >= 0 ? g.generals[c.governor] : null; if (!gov || gov.pol < w.govPol) return false; }
  if (w.hiddenHere && !g.generals.some(x => x.hidden && SG.isFree(x) && x.city === c.id)) return false;
  if (w.neighborTrouble && !c.links.some(j => { const n = g.cities[j]._e; return n && (g.monthIndex - (n.trouble || -99) <= 2 || g.monthIndex - n.lastDis <= 2); })) return false;
  if (w.minAvgOrder && avg(g.citiesOf(f).map(x => x._e.order)) < w.minAvgOrder) return false;
  if (w.farFaction && !farFaction(f)) return false;
  return true;
}
let COAST = null;
function isCoast(c) { if (!COAST) { COAST = new Set(); for (const l of SG.ScenarioData.SeaLinks || []) for (const k of l.split('-')) COAST.add(k); for (const k of ['beihai', 'guangling', 'wu', 'kuaiji', 'nanpi', 'beiping']) COAST.add(k); } return COAST.has(c.key); }
function farFaction(f) {
  const g = G(), me = g.factions[f];
  const cands = g.factions.filter(x => x.alive && x.id !== f && x.culture !== me.culture && !neighbors(f).includes(x.id));
  return cands.length ? cands[hash32(g.monthIndex + '|' + f) % cands.length].id : -1;
}
function weightOf(ev, c, f) {
  let w = ev.w;
  if (ev.wOrder === 'low') w *= 1.6 - c._e.order / 100;
  if (ev.wOrder === 'high') w *= c._e.order / 65;
  if (ev.wPop) w *= clamp(c.population / 400000, 0.3, 3);
  if (ev.wTown) w *= c.town / 600;
  if (ev.wVirtue) w *= 1 + G().factions[f].virtue / 100;
  w *= buffMul(c, 'eventW:' + ev.id);
  if (ev.disaster) { for (const id of c._e.done) { const p = PROJ[id]; if (p && p.fx.disasterK && p.fx.disasterK[ev.id]) w *= p.fx.disasterK[ev.id]; } }
  return Math.max(0, w);
}
// 成对 / 武将事件的主角
const OATH_AFF = +(process.env.OATH || 70);
function pickPair(kind, f, k) {
  const g = G(), gens = g.generalsOf(f).filter(x => !x._away || x._away.until <= g.monthIndex);
  const out = [];
  for (let i = 0; i < gens.length && out.length < 40; i++) for (let j = i + 1; j < gens.length; j++) {
    const a = gens[i], b = gens[j];
    if (a.city !== b.city && kind !== 'enemies') continue;
    const bo = bond(a, b);
    if (kind === 'spar' && a.war >= 70 && b.war >= 70 && !g.isRuler(a) && !g.isRuler(b)) out.push([a, b]);
    else if (kind === 'oath' && bo && bo.a >= OATH_AFF && bo.k !== 'sworn' && bo.k !== 'kin' && bo.k !== 'spouse') out.push([a, b]);
    else if (kind === 'mentor' && (!bo || !bo.k) && Math.max(a.war, a.intel, a.pol) >= 80 && a.level >= 5 && b.level <= a.level - 3) out.push([a, b]);
    else if (kind === 'enemies' && bo && bo.a <= -30) out.push([a, b]);
    else if (kind === 'quarrel' && bo && bo.k === 'rival') out.push([a, b]);
  }
  return out.length ? out[Math.floor(k.nextDouble() * out.length)] : null;
}
function pickGen(need, f, k) {
  const g = G(), me = g.factions[f];
  const gens = g.generalsOf(f).filter(x => !g.isRuler(x) && (!x._away || x._away.until <= g.monthIndex)).filter(x =>
    (!need.minIntel || x.intel >= need.minIntel) && (!need.minWar || x.war >= need.minWar) && (!need.maxLevel || x.level <= need.maxLevel) &&
    (!need.foreign || CAT.FAMILY[x.culture] !== CAT.FAMILY[me.culture]) && (!need.idleMonths || g.monthIndex - (x._lastBattle || g._e.start) >= need.idleMonths));
  return gens.length ? gens[Math.floor(k.nextDouble() * gens.length)] : null;
}
function deserterNear(f, k) {
  const g = G(), out = [];
  for (const c of g.citiesOf(f)) for (const j of c.links) { const n = g.cities[j]; if (n.owner < 0 || n.owner === f || rel(f, n.owner) > 0) continue; for (const x of g.officersIn(n)) if (!g.isRuler(x) && x.loyalty <= 45 && Math.max(x.war, x.intel) >= 60 && g.officersIn(n).length > 1) out.push([x, c]); }
  return out.length ? out[Math.floor(k.nextDouble() * out.length)] : null;
}
function capitalOf(f) { const g = G(); const r = g.ruler(f); return r && g.cities[r.city].owner === f ? g.cities[r.city] : g.citiesOf(f)[0]; }

// 选一个事件（城 + 主角），无则 null
function drawEvent(f, k) {
  const g = G(), cands = [];
  const cities = g.citiesOf(f);
  if (!cities.length) return null;
  const cap = capitalOf(f);
  const fe = g._e.fac[f];
  for (const ev of CAT.EVENTS) {
    if (ev.globalP) continue;
    // R6：同一事件对同一势力 6 个月内不重复（不论哪座城）
    if (fe.recent && fe.recent[ev.id] !== undefined && g.monthIndex - fe.recent[ev.id] < 6) continue;
    if (ev.scope === 'city') {
      for (const c of cities) {
        if ((c._e.cd[ev.id] || -999) + ev.cd > g.monthIndex) continue;
        if (ev.disaster && g.monthIndex - c._e.lastDis < 12) continue;
        if (!condOK(ev, c, f)) continue;
        const w = weightOf(ev, c, f); if (w > 0) cands.push({ ev, c, w });
      }
    } else {
      if ((fe.cd[ev.id] || -999) + ev.cd > g.monthIndex) continue;
      if (!condOK(ev, cap, f)) continue;
      const w = ev.when && ev.when.needPair ? ev.w * 0.5 : ev.w;   // 人物事件按势力一次（不按城数放大）
      cands.push({ ev, c: cap, w: w * Math.max(1, Math.sqrt(cities.length)) });
    }
  }
  if (!cands.length) return null;
  const tot = sum(cands, x => x.w);
  let r = k.nextDouble() * tot;
  for (const x of cands) { r -= x.w; if (r < 0) return x; }
  return cands[cands.length - 1];
}
function runEvent(f, pick, k, chooser) {
  const g = G(), ev = pick.ev, c = pick.c;
  const ctx = { f, c, k, gov: c.governor >= 0 ? g.generals[c.governor] : g.ruler(f), ruler: g.ruler(f), disaster: !!ev.disaster, origin: -1, far: -1 };
  if (ev.scope === 'pair') { const p = pickPair(ev.when.needPair, f, k); if (!p) return null; ctx.a = p[0]; ctx.b = p[1]; }
  if (ev.scope === 'general') {
    if (ev.id === 'deserter') { const d = deserterNear(f, k); if (!d) return null; ctx.subject = d[0]; ctx.subjectFac = d[0].faction; ctx.c = d[1]; }
    else { const s = pickGen(ev.when.needGen || {}, f, k); if (!s) return null; ctx.subject = s; }
  }
  if (ev.id === 'merchant') { const ns = neighbors(f); ctx.origin = ns.length ? ns[Math.floor(k.nextDouble() * ns.length)] : -1; }
  if (ev.id === 'gift') ctx.far = farFaction(f);
  // 冷却与灾害记录
  if (ev.scope === 'city') { c._e.cd[ev.id] = g.monthIndex; if (ev.disaster) c._e.lastDis = g.monthIndex; }
  else g._e.fac[f].cd[ev.id] = g.monthIndex;
  const fr = g._e.fac[f]; fr.recent = fr.recent || {}; fr.recent[ev.id] = g.monthIndex;
  let val = applyEffects(ev.pre, ctx);
  const opts = (ev.options || []).filter(o => !o.playerOnly || f === g.player);
  if (opts.length) {
    const o = chooser(ev, opts, ctx);
    ctx.actor = agentFor(o, ctx);
    val += applyEffects(o.cost, ctx);
    if (o.roll) {
      const p = rollP(o.roll.p, ctx);
      val += applyEffects(k.nextDouble() < p ? o.roll.ok : o.roll.fail, ctx);
    }
    val += applyEffects(o.effects, ctx);
    if (o.id === 'relief' || o.id === 'doctor' || o.id === 'settle') cnt(f, 'reliefs', 1);
    if (ev.id === 'sage' && o.id === 'host') cnt(f, 'sages', 1);
  }
  E.stats.events[ev.id] = (E.stats.events[ev.id] || 0) + 1;
  if (f === g.player && ev.modal !== false && opts.length) card(ev.cat === 'global' ? 'global' : 'random');
  E.stats.evValue += val; E.stats.evValueN++;
  if (f === g.player) cnt(f, 'eventValue', Math.round(val));
  return { ev, ctx, val };
}
function agentFor(o, ctx) {
  const g = G();
  if (!o.agent || o.agent === 'gov') return ctx.gov;
  if (o.agent === 'ruler') return ctx.ruler;
  if (o.agent === 'subject') return ctx.subject;
  const gens = g.generalsIn(ctx.c.id, ctx.f);
  const key = o.agent.split(':')[1];
  if (!gens.length) return ctx.gov;
  const sc = x => key === 'war' ? x.war * 2 + x.troops / 200 : key === 'intel' ? x.intel : key === 'pol' ? x.pol : Math.max(x.intel, x.pol);
  return gens.reduce((a, b) => (sc(b) > sc(a) ? b : a));
}
function rollP(p, ctx) {
  const a = ctx.actor || ctx.gov || ctx.ruler, F = G().factions[ctx.f];
  let v = p.base || 0;
  if (a) v += (p.war || 0) * a.war + (p.intel || 0) * a.intel + (p.pol || 0) * a.pol + (p.best || 0) * Math.max(a.intel, a.pol) + (p.troops || 0) * a.troops;
  v += (p.virtue || 0) * F.virtue;
  return clamp(v, 0.05, p.cap || 0.9);
}
function optCost(o, ctx) {
  let gold = 0, food = 0;
  for (const e of o.cost || []) { if (e.k === 'gold') gold -= amount(e, ctx.c, ctx.k, ctx.c.gold); if (e.k === 'food') food -= amount(e, ctx.c, ctx.k, ctx.c.food); }
  return { gold, food };
}
// 电脑（与委任）保留：选项花费之后城中至少留 60 + 6 × 武将数 金、两个月军粮，免得下月俸禄不足（全城忠诚 −3 → 出奔连锁）
function reserveOf(c) { const g = G(); return { gold: 60 + 6 * g.officersIn(c).length, food: 2 * M.idiv(g.troopsIn(c), B.TroopsPerFood) }; }
function affordable(o, ctx) {
  const { gold, food } = optCost(o, ctx);
  const r = reserveOf(ctx.c);
  if (gold > 0 && gold + r.gold > ctx.c.gold) return false;
  if (food > 0 && food + r.food > ctx.c.food) return false;
  if (o.need && o.need.troops && sum(G().generalsIn(ctx.c.id, ctx.f), x => x.troops) < o.need.troops) return false;
  // 离城（游学、归省）：不能带走城中最后一员武将，受威胁的城也不放人
  if ((o.effects || []).some(e => e.k === 'away') && ctx.subject) {
    const home = G().cities[ctx.subject.city];
    if (G().officersIn(home).length < 2 || threatened(ctx.f, home)) return false;
  }
  return true;
}
// 电脑（与玩家「委任」）的选择：可负担的选项中 ai 分最高者；aiIf 满足时 ×2
function aiChoose(ev, opts, ctx) {
  let best = null, bs = -1;
  for (const o of opts) {
    if (!affordable(o, ctx)) continue;
    let s = o.ai || 0;
    const q = o.aiIf || {};
    let okIf = Object.keys(q).length > 0;
    if (q.foodOverHarv !== undefined && !(ctx.c.food > harv(ctx.c) * q.foodOverHarv)) okIf = false;
    if (q.goldOverInc !== undefined && !(ctx.c.gold > inc(ctx.c) * q.goldOverInc)) okIf = false;
    if (q.goldOver !== undefined && !(ctx.c.gold > q.goldOver)) okIf = false;
    if (q.troopsOver !== undefined && !(G().troopsIn(ctx.c) > q.troopsOver)) okIf = false;
    if (q.orderBelow !== undefined && !(ctx.c._e.order < q.orderBelow)) okIf = false;
    if (q.minVirtue !== undefined && !(G().factions[ctx.f].virtue >= q.minVirtue)) okIf = false;
    if (q.govPol !== undefined && !(ctx.gov && ctx.gov.pol >= q.govPol)) okIf = false;
    if (q.genWar !== undefined && !G().generalsIn(ctx.c.id, ctx.f).some(x => x.war >= q.genWar)) okIf = false;
    if (q.threatened !== undefined && !threatened(ctx.f, ctx.c)) okIf = false;
    if (q.notThreatened !== undefined && threatened(ctx.f, ctx.c)) okIf = false;
    if (okIf) s *= 2;
    if (s > bs) { bs = s; best = o; }
  }
  return best || opts[opts.length - 1];
}
// 玩家（脚本，第四版）：按期望价值挑选——与电脑相同的规则，但偏好长期收益（民心、声望、经验）
function playerChoose(ev, opts, ctx) {
  let best = null, bs = -1e9;
  for (const o of opts) {
    if (!affordable(o, ctx)) continue;
    const { gold, food } = optCost(o, ctx);
    let s = -(gold + food * 0.12);
    const effs = (o.effects || []).concat(o.roll ? o.roll.ok.map(e => Object.assign({}, e, { _p: rollP(o.roll.p, Object.assign({}, ctx, { actor: agentFor(o, ctx) })) })) : []);
    for (const e of effs) {
      const p = e._p === undefined ? 1 : e._p;
      const d = e.k === 'order' ? (e.add || 0) * inc(ctx.c) * 0.6 * (e.all ? G().cityCount(ctx.f) : 1) : e.k === 'prestige' ? (e.add || 0) * 6 : e.k === 'virtue' ? (e.add || 0) * 30 : e.k === 'fame' ? (e.add || 0) * 60 : e.k === 'exp' ? (e.add || 0) * 1.5 : e.k === 'gold' ? amount(e, ctx.c, SG.SeededRandom(1), ctx.c.gold) : e.k === 'food' ? amount(e, ctx.c, SG.SeededRandom(1), ctx.c.food) * 0.12 : e.k === 'gtroops' ? 0.2 * (e.range ? avg(e.range) : 0) : e.k === 'buff' && e.mod === 'harvest' ? e.v * harv(ctx.c) * 0.12 : e.k === 'buff' && e.mod === 'income' ? e.v * inc(ctx.c) * (e.months || 1) : e.k === 'land' || e.k === 'town' ? 2.6 * amount(e, ctx.c, SG.SeededRandom(1), ctx.c[e.k]) : e.k === 'loyalty' ? (e.add || 0) * 4 : e.k === 'rel' ? (e.add || 0) * 3 : e.k === 'item' ? 120 : e.k === 'reveal' ? 150 : e.k === 'defect' ? 300 : e.k === 'bondKind' ? 120 : 0;
      s += d * p;
    }
    if (s > bs) { bs = s; best = o; }
  }
  return best || opts[opts.length - 1];
}

// ------------------------------------------------------------------ 月度结算（套在原 endMonth 外） --
const oEnd = StrategyAI.endMonth;
StrategyAI.endMonth = function () {
  const g = G();
  if (!E.on) { const fb0 = g.generals.map(x => x.faction); const nw = oEnd.apply(this, arguments); g.generals.forEach((x, i) => { if (fb0[i] >= 0 && x.faction < 0 && !x.dead && g.factions[fb0[i]].alive) E.stats.defect++; }); return nw; }
  const month = g.month;
  const base = g.cities.map(c => (c.owner >= 0 ? inc(c) : 0));
  const popB = g.cities.map(c => c.population);
  const foodB = g.cities.map(c => c.food);
  // 羁绊：结义 / 亲族 / 夫妻的出奔保护（在原出奔判定前临时加忠诚，判定后还原）
  const boosted = [];
  if (E.bonds) for (const [key, bo] of g._e.bonds) {
    if (!['sworn', 'kin', 'spouse'].includes(bo.k)) continue;
    const [ia, ib] = key.split('-').map(Number), a = g.generals[ia], b = g.generals[ib];
    for (const [x, y] of [[a, b], [b, a]]) if (x.faction >= 0 && x.faction === y.faction && !x.dead && !y.dead && x.loyalty < 40) { const add = bo.k === 'sworn' ? 30 : 15; x.loyalty += add; boosted.push([x, add]); }
  }
  const fb = g.generals.map(x => x.faction);
  const news = oEnd.apply(this, arguments);
  for (const [x, add] of boosted) if (!x.dead) x.loyalty = Math.max(0, x.loyalty - add);
  g.generals.forEach((x, i) => { if (fb[i] >= 0 && x.faction < 0 && !x.dead && g.factions[fb[i]].alive) E.stats.defect++; });
  g._e.expUsed.clear();
  const harvested = month === B.HarvestMonth;
  for (let i = 0; i < g.cities.length; i++) {
    const c = g.cities[i];
    if (c.owner < 0) continue;
    E.stats.gross += base[i];
    // 民心对收入（±5%）与人口增长（×0.5 … ×1.5）
    let kI = 0;
    if (E.order) kI += c._e.order / 1000 - 0.05;
    kI += buffSum(c, 'income');
    for (const id of c._e.done) { const p = PROJ[id]; if (p && p.fx.income) kI += p.fx.income; }
    const adj = Math.round(base[i] * kI);
    c.gold = Math.max(0, c.gold + adj); E.stats.orderAdj += adj;
    if (E.order) { const grow = c.population - popB[i]; if (grow > 0) c.population = Math.min(2000000, c.population + Math.round(grow * (c._e.order / 100 - 0.5))); }
    // 屯田：本城驻军耗粮 −25%
    if (c._e.done.has('tuntian')) c.food += Math.round(M.idiv(g.troopsIn(c), B.TroopsPerFood) * 0.25);
    if (harvested) {
      let kH = buffSum(c, 'harvest');
      for (const id of c._e.done) { const p = PROJ[id]; if (p && p.fx.harvest) kH += p.fx.harvest; }
      if (E.order) kH += (c._e.order - 50) / 1000;
      const add = Math.round(harv(c) * kH);
      c.food = Math.max(0, c.food + add);
      c._e.buffs = c._e.buffs.filter(b => !(b.mod === 'harvest'));
      cnt(c.owner, 'harvestTotal', Math.max(0, c.food - foodB[i]));
    }
    // 民心向目标值每月移动 1
    if (E.order) { const t = orderTarget(c); c._e.order = c._e.order < t ? Math.min(t, c._e.order + ORDER_STEP) : c._e.order > t ? c._e.order - 1 : c._e.order; }
    c._e.buffs = c._e.buffs.filter(b => b.until > g.monthIndex);
    // 疫病蔓延
    if (c._e.flags.plague > g.monthIndex) for (const j of c.links) { const n = g.cities[j]; if (n.owner < 0 || n._e.flags.plague > g.monthIndex || n._e.done.has('temple')) continue; if (rng('spread', c.id, j).nextDouble() < 0.25) { n._e.flags.plague = g.monthIndex + 3; n.population = Math.round(n.population * 0.98); n._e.order = Math.max(0, n._e.order - 4); } }
    // 政绩：太守每月 +3（民心 ≥ 50），民心 ≥ 75 再 +1
    if (E.domexp && c.governor >= 0 && c._e.order >= 50) domExp(g.generals[c.governor], 3 + (c._e.order >= 75 ? 1 : 0));
    // 书院：城中智力 ≥ 60 的武将 +2
    if (c._e.done.has('academy')) for (const x of g.officersIn(c)) if (x.intel >= 60) domExp(x, 2);
    // 营建进度
    if (c._e.proj) { const p = c._e.proj, sup = g.generals[p.sup]; const here = sup && !sup.dead && sup.city === c.id && sup.faction === c.owner; p.work += here ? 10 + sup.pol / 8 : 5; if (here) domExp(sup, 3); if (p.work >= PROJ[p.id].work) { c._e.done.add(p.id); c._e.proj = null; cnt(c.owner, 'projects', 1); addPrestige(c.owner, p.id === 'wonder' ? PR.wonder : PR.project); if (p.id === 'wonder') cnt(c.owner, 'wonder', 1); } }
    // 城池升级（邑→县城→郡城→大郡→都会）：首次达到某级 +10 × 级（玩家每月算；电脑每年正月算，省时）
    if ((c.owner === g.player || g.month === 1) && E.prestige) { const lv = cityLevel(c); if (lv > (c._e.maxLv || 0)) { if (c._e.maxLv !== undefined) addPrestige(c.owner, PR.cityLevel * lv); c._e.maxLv = lv; } }
    if (c._e.done.has('wonder')) addPrestige(c.owner, 5);
  }
  // 同城武将两两建立亲密度记录（每城至多前 6 名武将，避免组合爆炸）
  if (E.bonds) for (const c of g.cities) { if (c.owner < 0) continue; const os = g.officersIn(c).slice(0, 6); for (let i = 0; i < os.length; i++) for (let j = i + 1; j < os.length; j++) { const k2 = bkey(os[i].id, os[j].id); if (!g._e.bonds.has(k2)) g._e.bonds.set(k2, { k: null, a: 0, m: -1 }); } }
  // 羁绊：知己 / 夫妻同城每月忠诚 +1（≤ 90）；不和同城 −1；师徒：徒弟 +4 经验
  if (E.bonds) for (const [key, bo] of g._e.bonds) {
    const [ia, ib] = key.split('-').map(Number), a = g.generals[ia], b = g.generals[ib];
    if (a.dead || b.dead || a.faction < 0 || a.faction !== b.faction || a.city !== b.city) { if (bo.a > 0 && !bo.k) bo.a = Math.max(0, bo.a - 0.5); continue; }
    if (bo.a < 60 && bo.a >= 0 && bo.k !== 'feud') bo.a += 1;   // 同城共事：亲密度每月 +1（至 60）
    if (bo.k === 'friend' || bo.k === 'spouse') for (const x of [a, b]) if (!g.isRuler(x) && x.loyalty < 90) x.loyalty++;
    if (bo.k === 'feud') for (const x of [a, b]) if (!g.isRuler(x) && x.loyalty > 60) x.loyalty--;   // 不和只把忠诚压到 60，不单独逼人出奔
    if (bo.k === 'mentor') { const st = bo.m === a.id ? b : a; storyExp(st, 4); }
  }
  // 邦交：好感每月向 0 回归 1（有约者不低于约定下限）；到期的约
  if (E.rel) {
    const n = g.factions.length, R = g._e.rel;
    const even = g.monthIndex % 2 === 0, third = g.monthIndex % 3 === 0;
    for (let a = 0; a < n; a++) for (let b = a + 1; b < n; b++) {
      let v = R[a * n + b]; if (v === 0) continue;
      // 回归：|v| ≤ 30 时每两月向 0 移 1；v > 30 时每三月向 30 移 1；v < −30 时每三月向 −30 移 1
      if (v > 30) { if (third) v -= 1; } else if (v < -30) { if (third) v += 1; } else if (even) v += v > 0 ? -1 : 1;
      // 约定的下限：联姻 50、同盟 30、纳贡 / 通商 20
      if (pactOf(a, b, 'marriage')) v = Math.max(v, 50); else if (oAllied.call(g, a, b)) v = Math.max(v, 30); else if (nonAggro(a, b) || pactOf(a, b, 'trade')) v = Math.max(v, 20);
      R[a * n + b] = R[b * n + a] = v;
    }
    // 纳贡支付
    for (const p of g._e.pacts) {
      if (p.kind !== 'tribute' || p.until <= g.monthIndex || !p.pay) continue;
      const payer = p.payer, recv = p.payer === p.a ? p.b : p.a;
      const src = g.citiesOf(payer).sort((x, y) => y.gold - x.gold)[0], dst = capitalOf(recv);
      if (!src || !dst || src.gold < p.pay) { p.until = g.monthIndex; addRel(payer, recv, -20); continue; }
      src.gold -= p.pay; dst.gold += p.pay; cnt(recv, 'tributeIn', p.pay); addPrestige(recv, PR.tributeIn); if (payer === g.player) spend('tribute', p.pay);
    }
  }
  // 事件：每个势力每月一次判定
  if (E.events) {
    for (const F of g.factions) {
      if (!F.alive) continue;
      E.stats.facMonths++;
      const k = rng('evt', F.id);
      const p = Math.min(0.55, 0.28 + 0.025 * g.cityCount(F.id));
      if (k.nextDouble() >= p) continue;
      const pick = drawEvent(F.id, k);
      if (!pick) continue;
      const chooser = F.id === g.player && E.playerPol === 'v4' ? playerChoose : aiChoose;
      const r = runEvent(F.id, pick, k, chooser);
      if (r) E.stats.eventsByFac++;
    }
    // 天下事件
    const kg = rng('global');
    if (kg.nextDouble() < 1 / 40) for (const F of g.factions) if (F.alive) runEvent(F.id, { ev: EV.comet, c: capitalOf(F.id) }, rng('comet', F.id), F.id === g.player && E.playerPol === 'v4' ? playerChoose : aiChoose);
    // 剧本史事
    for (const h of CAT.HISTORICAL) {
      if (h.scenario !== g.scenario) continue;
      const [y, m] = h.date.split('-').map(Number);
      if (g.year !== y || g.month !== m) continue;
      for (const key of h.cityKeys) { const c = g.cityByKey(key); if (!c || c.owner < 0) continue; if (rng('hist', h.id, key).nextDouble() < h.p) runEvent(c.owner, { ev: EV[h.event], c }, rng('histev', h.id, key), c.owner === g.player && E.playerPol === 'v4' ? playerChoose : aiChoose); }
    }
  }
  // 节庆（各文化家族一个招牌节庆）
  for (const F of g.factions) {
    if (!F.alive) continue;
    const fest = festivalOf(F);
    if (!fest || fest.month !== g.month) continue;
    const cap = capitalOf(F.id); if (!cap) continue;
    const R = CAT.FESTIVAL_RULE, n = g.cityCount(F.id);
    const cost = Math.min(R.grand.cost.cap, R.grand.cost.base + R.grand.cost.perCity * n);
    const isP = F.id === g.player && E.playerPol;
    if (F.id === g.player) card('festival');
    const grand = isP ? (E.playerPol === 'v4' && cap.gold >= cost + 250) : cap.gold >= cost * 3;
    const modest = !grand && cap.gold >= cost / 3 + reserveOf(cap).gold + 50 && (!isP || E.playerPol === 'v4');
    if (!grand && !modest) continue;
    const temple = g.citiesOf(F.id).some(c => c._e.done.has('temple') || c._e.done.has('wonder')) ? 1.5 : 1;
    cap.gold -= Math.round(grand ? cost : cost / 3);
    if (F.id === g.player) spend('festival', Math.round(grand ? cost : cost / 3));
    for (const c of g.citiesOf(F.id)) c._e.order = clamp(c._e.order + Math.round((grand ? R.grand.order : R.modest.order) * (c._e.done.has('temple') ? 1.5 : 1)), 0, 100);
    if (grand) { for (const x of g.officersIn(cap)) if (!g.isRuler(x)) x.loyalty = Math.min(100, x.loyalty + R.grand.capitalLoyalty); cnt(F.id, 'grandFestivals', 1); }
    addPrestige(F.id, Math.round((grand ? R.grand.prestige : R.modest.prestige) * temple));
  }
  // 文会（九月）
  if (g.month === 9) wenhui();
  // 武林大会（正月；比赛本身归 TOURNAMENT 规格，这里只估算奖励流量）
  if (g.month === 1 && g.monthIndex > g._e.start) tourney();
  // 归附（让州）：弱小势力自愿并入强大而有德的邻邦（ENGAGEMENT §10.8）
  if (E.rel) submissions();
  // 电脑外交 / 营建（变体）
  if (E.aiDip) aiDiplomacy();
  if (E.aiProj) aiProjects();
  // 玩家：国策、商队、探索的月结
  if (g.player >= 0 && E.playerPol === 'v4') playerMonthEnd();
  for (const F of g.factions) if (F.alive) syncFV(F.id);
  return news;
};
const SUB = { ratio: +(process.env.SUBR || 2), virtue: 50, rel: +(process.env.SUBREL || 50) };
function submissions() {
  const g = G();
  for (const H of g.factions) {
    if (!H.alive || H.id === g.player || g.cityCount(H.id) > 3) continue;
    const hp = power(H.id) + 1;
    let best = null, bp = 0;
    for (const F of neighbors(H.id)) {
      if (F !== g.player && !E.aiDip) continue;
      if (g.monthIndex - (g._e.fac[F].lastSubmit === undefined ? -999 : g._e.fac[F].lastSubmit) < 36) continue;   // 归附之后 36 个月内不再有人来投（消化期）
      const FF = g.factions[F];
      const ratio = (power(F) + 1) / hp;
      const pact = pactOf(F, H.id, 'marriage') || g._e.pacts.find(q => q.kind === 'tribute' && q.payer === H.id && (q.a === F || q.b === F) && q.until > g.monthIndex && g.monthIndex - q.since >= 12);
      if (ratio < SUB.ratio || FF.virtue < SUB.virtue || (rel(F, H.id) < SUB.rel && !pact)) continue;
      const p = clamp(0.01 + Math.max(0, rel(F, H.id) - SUB.rel) / 1000 + 0.005 * Math.min(10, ratio - SUB.ratio) + FF.virtue / 2000 + g._e.fac[F].prestige / 40000, 0, 0.06);
      if (p > bp) { bp = p; best = F; }
    }
    if (best === null || rng('submit', H.id).nextDouble() >= bp) continue;
    if (best === g.player && E.playerPol !== 'v4') continue;
    if (best === g.player) card('submit');
    absorb(best, H.id);
  }
}
function absorb(F, H) {
  const g = G();
  const rulerH = g.ruler(H);
  for (const c of g.citiesOf(H)) { c.owner = F; c._e.order = Math.min(100, c._e.order + 5); }
  for (const x of g.generalsOf(H)) { x.faction = F; x.loyalty = clamp(60 + Math.round(g.factions[F].virtue / 5) + (x === rulerH ? 10 : 0), 50, 95); }
  g.factions[H].alive = false;
  for (const c of g.citiesOf(F)) g.autoGovernor(c);
  addPrestige(F, 30); cnt(F, 'submissions', 1); g._e.fac[F].lastSubmit = g.monthIndex; E.stats.submit = (E.stats.submit || 0) + 1;
  g.addLog(g.factions[H].name + '举州归附' + g.factions[F].name + '。');
}
function festivalOf(F) {
  const fam = CAT.FAMILY[F.culture] || 'hua';
  const base = CAT.FESTIVALS[fam];
  if (!base) return null;
  const o = base.overrides && base.overrides[F.key];
  return o ? Object.assign({}, base, o) : base;
}
// 玩家这个月会看到的卡片（R1 统计）：random = 随机事件卡，其余 = 排期卡
function card(kind) { const g = G(); if (g.player < 0 || E.playerPol !== 'v4') return; const m = g._e.cards = g._e.cards || {}; const key = g.monthIndex; (m[key] = m[key] || []).push(kind); }
function spend(k, v) { const g = G(); if (g.player < 0) return; cnt(g.player, 'spend:' + k, v); }
function cnt(f, k, v) { if (f < 0) return; const c = G()._e.counters; const key = f + ':' + k; c[key] = (c[key] || 0) + v; }
function getCnt(f, k) { return G()._e.counters[f + ':' + k] || 0; }
function rollRarity(w, k) { const r = k.nextDouble() * 100; const c = w.common || 0, u = w.uncommon || 0; return r < c ? 'common' : r < c + u ? 'uncommon' : 'rare'; }
function gotItem(f, rar, why) { cnt(f, 'item:' + rar, 1); cnt(f, 'items', 1); cnt(f, 'itemFrom:' + why, 1); }

// ------------------------------------------------------------------ 文会 / 武林大会 --
function wenhui() {
  const g = G(), k = rng('wenhui');
  if (g.player >= 0) card('wenhui');
  const ents = [];
  for (const F of g.factions) {
    if (!F.alive) continue;
    const gens = g.generalsOf(F.id).filter(x => (x.intel >= 70 || x.pol >= 70) && (!x._away || x._away.until <= g.monthIndex)).sort((a, b) => (b.intel + b.pol) - (a.intel + a.pol)).slice(0, 3);
    const acad = g.citiesOf(F.id).some(c => c._e.done.has('academy')) ? 5 : 0;
    for (const x of gens) {
      const s1 = 0.5 * x.pol + 0.5 * x.intel + 2 * x.level + acad + k.nextDouble() * 20;
      const s2 = 0.7 * x.intel + 0.3 * x.pol + 2 * x.level + acad + k.nextDouble() * 30;
      const s3 = 0.6 * x.intel + 0.4 * x.pol + 2 * x.level + acad + k.nextDouble() * 25 + (F.id === g.player && E.playerPol === 'v4' ? 6 : 0);  // 玩家亲自论辩：平均 +6
      ents.push({ x, f: F.id, s: s1 + s2 + s3 });
    }
  }
  ents.sort((a, b) => b.s - a.s);
  const prize = [{ gold: 300, prestige: 50, exp: 80, item: 'rare', fame: 1 }, { gold: 150, prestige: 25, exp: 50, item: 'uncommon' }, { gold: 80, prestige: 12, exp: 35, item: 'common' }];
  for (const e of ents) storyExp(e.x, 20);
  ents.slice(0, 3).forEach((e, i) => {
    const P = prize[i], cap = capitalOf(e.f);
    if (cap) cap.gold += P.gold;
    if (e.f === g.player) cnt(g.player, 'prizeGold', P.gold);
    addPrestige(e.f, P.prestige); storyExp(e.x, P.exp);
    if (P.fame) { g._e.fac[e.f].fameEv += P.fame; }
    gotItem(e.f, P.item, 'wenhui'); cnt(e.f, 'wenhuiPlace' + (i + 1), 1); if (i === 0) cnt(e.f, 'wenhuiWin', 1);
    if (e.f === g.player) cnt(e.f, 'wenhuiBest', 0);
  });
  const host = g.factions.filter(F => F.alive).sort((a, b) => g._e.fac[b.id].prestige - g._e.fac[a.id].prestige)[0];
  if (host) { addPrestige(host.id, 20); const cap = capitalOf(host.id); if (cap) cap._e.order = Math.min(100, cap._e.order + 5); }
  E.stats.wenhui = (E.stats.wenhui || 0) + 1;
}
function tourney() {
  const g = G(), k = rng('tourney'), ents = [];
  if (g.player >= 0) card('tourney');
  for (const F of g.factions) {
    if (!F.alive) continue;
    const gens = g.generalsOf(F.id).filter(x => !x._away || x._away.until <= g.monthIndex).sort((a, b) => b.war - a.war).slice(0, 5);
    const drill = g.citiesOf(F.id).some(c => c._e.done.has('drill')) ? 5 : 0;
    for (const x of gens) ents.push({ x, f: F.id, s: x.war + 0.6 * x.level + drill + k.nextDouble() * 15 });
  }
  ents.sort((a, b) => b.s - a.s);
  const P = [{ gold: 400, prestige: 50, item: 'rare' }, { gold: 200, prestige: 25, item: 'uncommon' }, { gold: 100, prestige: 12, item: 'common' }];
  ents.slice(0, 3).forEach((e, i) => { const cap = capitalOf(e.f); if (cap) cap.gold += P[i].gold; if (e.f === g.player) cnt(g.player, 'prizeGold', P[i].gold); addPrestige(e.f, P[i].prestige); gotItem(e.f, P[i].item, 'tourney'); if (i === 0) cnt(e.f, 'tourneyWin', 1); });
}

// ------------------------------------------------------------------ 电脑外交与营建（变体） --
function aiDiplomacy() {
  const g = G();
  for (const F of g.factions) {
    if (!F.alive || F.id === g.player) continue;
    const k = rng('aidip', F.id);
    const myP = power(F.id) + 1;
    for (const h of neighbors(F.id)) {
      if (h === g.player) continue;
      if (nonAggro(F.id, h) || oAllied.call(g, F.id, h)) continue;
      const hp = power(h);
      // 弱者向强邻纳贡（月收入 8%，12 个月）
      if (hp >= 2.5 * myP && rel(F.id, h) >= -30 && !atWar(F.id, h) && k.nextDouble() < 0.12) {
        const pAcc = clamp(0.5 + rel(F.id, h) / 200, 0.1, 0.9);
        if (k.nextDouble() < pAcc) { addPact(F.id, h, 'tribute', 12, { payer: F.id, pay: Math.max(5, Math.round(facInc(F.id) * 0.08)) }); addRel(F.id, h, 10); E.stats.aiTribute++; }
        break;
      }
      // 联姻：好感 ≥ 50
      if (rel(F.id, h) >= 50 && !pactOf(F.id, h, 'marriage') && k.nextDouble() < 0.05) { addPact(F.id, h, 'marriage', 48); addRel(F.id, h, 30); E.stats.aiMarriage++; break; }
    }
  }
}
function aiProjects() {
  const g = G();
  for (const c of g.cities) {
    if (c.owner < 0 || c.owner === g.player || c._e.proj) continue;
    const k = rng('aiproj', c.id);
    if (k.nextDouble() > 1 / 6) continue;
    const front = c.links.some(j => { const o = g.cities[j].owner; return o >= 0 && o !== c.owner && !g.allied(o, c.owner); });
    const order = front ? ['walls', 'beacons', 'drill', 'market', 'irrigation'] : ['market', 'irrigation', 'temple', 'granary', 'academy'];
    for (const id of order) {
      const p = PROJ[id];
      if (c._e.done.has(id) || !projReqOK(p, c)) continue;
      if (c.gold < p.cost + 600) break;
      c.gold -= p.cost; c._e.proj = { id, work: 0, sup: c.governor >= 0 ? c.governor : g.ruler(c.owner).id }; E.stats.aiProj++;
      break;
    }
  }
}
function projReqOK(p, c) {
  const g = G(), r = p.req || {};
  if (r.minLand && c.land < r.minLand) return false;
  if (r.minTown && c.town < r.minTown) return false;
  if (r.minIndustry && c.industry < r.minIndustry) return false;
  if (r.minIndTown && c.industry + c.town < r.minIndTown) return false;
  if (r.minTroopsHere && g.troopsIn(c) < r.minTroopsHere) return false;
  if (r.coastalOrRiver && !isCoast(c) && !RIVER.has(c.key)) return false;
  if (r.frontier && !c.links.some(j => { const o = g.cities[j].owner; return o >= 0 && o !== c.owner; })) return false;
  if (r.capital && capitalOf(c.owner) !== c) return false;
  if (r.oncePerFaction && g.citiesOf(c.owner).some(x => x._e.done.has(p.id) || (x._e.proj && x._e.proj.id === p.id))) return false;
  if (r.minCityLevel !== undefined && cityLevel(c) < r.minCityLevel) return false;
  return true;
}
function cityLevel(c) { const g = G(); return CS.cityLevel(CS.stages(CS.ctxFor(g, c, { capital: capitalOf(c.owner) === c, coastal: isCoast(c), river: RIVER.has(c.key) }))); }

// ------------------------------------------------------------------ 玩家：国策、商队、探索、外交、营建 --
function offerMission(f, bonus) {
  const g = G(), e = g._e;
  if (e.mission.filter(m => !m.done && m.until > g.monthIndex).length >= 3) return;
  const k = rng('mission', e.seq++);
  const list = CAT.MISSIONS.filter(m => !m.military && !m.needs && (!m.season || true));
  const t = list[Math.floor(k.nextDouble() * list.length)];
  const tier = 1 + Math.floor(k.nextDouble() * 3);
  const cities = g.citiesOf(f);
  const c = cities[Math.floor(k.nextDouble() * cities.length)];
  const m = { id: t.id, tier, city: c.id, since: g.monthIndex, until: g.monthIndex + t.dl[tier - 1], done: false, start: snapshot(f, c), rewardBase: facInc(f) };
  e.mission.push(m);
  cnt(f, 'missionsOffered', 1);
}
function snapshot(f, c) { const g = G(); return { stat: Math.min(c.land, c.industry, c.town), statKey: ['land', 'industry', 'town'].reduce((a, b) => (c[b] < c[a] ? b : a)), hires: getCnt(f, 'hires'), caravans: getCnt(f, 'caravans'), exped: getCnt(f, 'expeditions'), projects: getCnt(f, 'projects'), items: getCnt(f, 'items'), w1: getCnt(f, 'wenhuiPlace1'), w2: getCnt(f, 'wenhuiPlace2'), w3: getCnt(f, 'wenhuiPlace3') }; }
function missionMet(m, f) {
  const g = G(), c = g.cities[m.city], P = CAT.MISSIONS.find(x => x.id === m.id).params, t = m.tier - 1, s = m.start;
  switch (m.id) {
    case 'm_dev': return c.owner === f && c[s.statKey] >= s.stat + P.delta[t];
    case 'm_grain': { const cons = Math.max(1, M.idiv(sum(g.citiesOf(f), x => g.troopsIn(x)), B.TroopsPerFood)); return sum(g.citiesOf(f), x => x.food) >= cons * P.months[t] + 2000; }
    case 'm_talent': return getCnt(f, 'hires') - s.hires >= P.n[t];
    case 'm_train': return g.generalsOf(f).filter(x => x.troops >= P.troops && x.training >= P.training).length >= P.n[t];
    case 'm_order': return g.citiesOf(f).every(x => x._e.order >= P.order[t]);
    case 'm_trade': return getCnt(f, 'caravans') - s.caravans >= P.n[t];
    case 'm_ally': return neighbors(f).some(h => rel(f, h) >= P.rel[t]) || g.factions.some(h => h.alive && rel(f, h.id) >= P.rel[t]);
    case 'm_build': return getCnt(f, 'projects') > s.projects;
    case 'm_explore': return getCnt(f, 'expeditions') - s.exped >= P.n[t];
    case 'm_wenhui': { const need = P.place[t]; let d = getCnt(f, 'wenhuiPlace1') - s.w1; if (need >= 2) d += getCnt(f, 'wenhuiPlace2') - s.w2; if (need >= 3) d += getCnt(f, 'wenhuiPlace3') - s.w3; return d > 0; }
    case 'm_tourney': return getCnt(f, 'tourneyWin') > 0;
    case 'm_collect': return getCnt(f, 'items') - s.items >= P.n[t];
    case 'm_level': return g.generalsOf(f).filter(x => x.level >= P.level[t]).length >= P.n[t];
    case 'm_city': return g.citiesOf(f).some(x => cityLevel(x) >= P.level[t]);
    case 'm_defend': return g.citiesOf(f).every(x => SG.cityDefense(x) >= P.def[t] || !x.links.some(j => { const o = g.cities[j].owner; return o >= 0 && o !== f; }));
    default: return false;
  }
}
function playerMonthEnd() {
  const g = G(), f = g.player, e = g._e;
  // 国策：每 3 个月补到 3 项；完成即自动领赏
  if (g.monthIndex % 3 === 0) offerMission(f);
  for (const m of e.mission) {
    if (m.done || m.until <= g.monthIndex) continue;
    if (!missionMet(m, f)) continue;
    m.done = true; cnt(f, 'missionsDone', 1);
    const R = CAT.MISSION_REWARD, t = m.tier - 1, k = rng('mreward', m.since, m.id);
    const gold = Math.round(R.goldK[t] * m.rewardBase);
    capitalOf(f).gold += gold; cnt(f, 'missionGold', gold);
    addPrestige(f, R.prestige[t]);
    const gov = g.cities[m.city].governor >= 0 ? g.generals[g.cities[m.city].governor] : g.ruler(f);
    storyExp(gov, R.exp[t]);
    if (R.fame[t]) e.fac[f].fameEv += R.fame[t];
    const it = R.item[t];
    if (it) { const r = k.nextDouble(); if (it.rare && r < it.rare) gotItem(f, 'rare', 'mission'); else if (r < (it.rare || 0) + it.uncommon) gotItem(f, 'uncommon', 'mission'); }
  }
  // 商队归来
  for (const cv of e.caravans) {
    if (cv.back || cv.until > g.monthIndex) continue;
    cv.back = true;
    const home = g.cities[cv.from];
    const k = rng('caravan', cv.id);
    const lost = k.nextDouble() < cv.risk;
    const ret = lost ? Math.round(cv.inv * 0.5) : Math.round(cv.inv * (1 + cv.m));
    const dest = home.owner === f ? home : capitalOf(f);
    dest.gold += ret; cnt(f, 'tradeProfit', ret - cv.inv); cnt(f, 'caravans', 1);
    const L = g.generals[cv.leader]; storyExp(L, lost ? 5 : 10 + 4 * cv.hops);
    if (!lost) { addRel(f, cv.to, 3); if (cv.foreign) addPrestige(f, 2); if (k.nextDouble() < 0.35 + 0.05 * cv.hops) { const r = k.nextDouble(); gotItem(f, r < 0.80 ? 'common' : r < 0.99 ? 'uncommon' : 'rare', 'caravan'); } }
  }
  // 探索归来
  for (const ex of e.exped) {
    if (ex.back || ex.until > g.monthIndex) continue;
    ex.back = true;
    const S = CAT.SITES.find(s => s.id === ex.site), T = CAT.SITE_TYPES[S.type], k = rng('exped', ex.id);
    const L = g.generals[ex.leader];
    cnt(f, 'expeditions', 1); e.visited.add(S.id); if (S.myth) cnt(f, 'mythSites', 1);
    if (k.nextDouble() < T.mishap) { if (L) L._away = { kind: 'injured', until: g.monthIndex + 2 }; cnt(f, 'mishaps', 1); storyExp(L, 15); continue; }
    const gold = Math.round((T.gold[0] + k.nextDouble() * (T.gold[1] - T.gold[0])) * (1 + ex.hops / 4));
    capitalOf(f).gold += gold; cnt(f, 'expGold', gold);
    const rr = k.nextDouble(), rare = Math.min(0.06, T.item.rare + 0.005 * ex.hops);
    if (rr < rare) gotItem(f, 'rare', 'exped'); else if (rr < rare + T.item.uncommon) gotItem(f, 'uncommon', 'exped'); else if (rr < rare + T.item.uncommon + T.item.common) gotItem(f, 'common', 'exped');
    storyExp(L, 30 + 10 * ex.hops);
    addPrestige(f, T.prestige + (S.myth ? 10 : 0));
    if (T.virtue) e.fac[f].virtueEv += T.virtue;
    if (k.nextDouble() < 0.06) { const h = g.generals.find(x => x.hidden && SG.isFree(x)); if (h) { h.hidden = false; cnt(f, 'revealed', 1); } }
    if (T.clue && k.nextDouble() < T.clue) cnt(f, 'clueFound', 1);
  }
  achievements(f);
  victoryCheck(f);
}

// ------------------------------------------------------------------ 成就（原型：只统计可算的那些） --
function achievements(f) {
  const g = G(), e = g._e, cs = g.citiesOf(f);
  const lv = cs.length ? Math.max(...cs.map(cityLevel)) : 0;
  const test = {
    a_dev999: () => cs.some(c => c.land >= 999 || c.industry >= 999 || c.town >= 999),
    a_city3: () => lv >= 2, a_city4: () => lv >= 3, a_city5: () => lv >= 4,
    a_pop1m: () => cs.some(c => c.population >= 1000000), a_order80: () => cs.length && cs.every(c => c._e.order >= 80), a_order95: () => cs.some(c => c._e.order >= 95),
    a_harvest: () => getCnt(f, 'harvestTotal') >= 20000, a_project1: () => getCnt(f, 'projects') >= 1, a_project10: () => getCnt(f, 'projects') >= 10, a_wonder: () => getCnt(f, 'wonder') >= 1,
    a_relief: () => getCnt(f, 'reliefs') >= 5, a_caravan1: () => getCnt(f, 'caravans') >= 1, a_caravan20: () => getCnt(f, 'caravans') >= 20, a_profit: () => getCnt(f, 'tradeProfit') >= 10000,
    a_ally: () => g.factions.filter(h => h.alive && h.id !== f && (oAllied.call(g, f, h.id) || nonAggro(f, h.id))).length >= 3,
    a_marry: () => getCnt(f, 'marriages') >= 1, a_truce: () => getCnt(f, 'truces') >= 3,
    a_friends: () => { const al = g.factions.filter(h => h.alive && h.id !== f); return al.length && al.every(h => rel(f, h.id) >= 30); },
    a_wenhui1: () => getCnt(f, 'wenhuiWin') >= 1, a_wenhui3: () => getCnt(f, 'wenhuiWin') >= 3, a_festival5: () => getCnt(f, 'grandFestivals') >= 5, a_sage5: () => getCnt(f, 'sages') >= 5,
    a_items10: () => getCnt(f, 'items') >= 10, a_rare: () => getCnt(f, 'item:rare') >= 1, a_sworn: () => getCnt(f, 'sworn') >= 1, a_mentor: () => getCnt(f, 'mentors') >= 2,
    a_gens30: () => g.generalsOf(f).length >= 30, a_lv10: () => g.generalsOf(f).some(x => x.level >= 10), a_hidden10: () => getCnt(f, 'hiddenFound') + getCnt(f, 'revealed') >= 10,
    a_tourney1: () => getCnt(f, 'tourneyWin') >= 1, a_defend5: () => getCnt(f, 'defenses') >= 5, a_cities10: () => cs.length >= 10,
    a_exp1: () => getCnt(f, 'expeditions') >= 1, a_exp10: () => e.visited.size >= 10, a_myth: () => getCnt(f, 'mythSites') >= 3, a_map: () => getCnt(f, 'clueFound') >= 1,
  };
  for (const a of CAT.ACHIEVEMENTS) {
    if (e.ach.has(a.id) || !test[a.id] || !test[a.id]()) continue;
    e.ach.add(a.id); addPrestige(f, a.prestige); if (a.fame) e.fac[f].fameEv += a.fame; if (a.virtue) e.fac[f].virtueEv += a.virtue;
    e.log.push({ m: g.monthIndex - e.start, ach: a.id });
  }
}
// 王道：声望 ≥ P、平均民心 ≥ 75、≥ 3 座城达「大郡」（等级 ≥ 3）、24 个月未主动出征、≥ 50% 的在世势力好感 ≥ 30 或有约；连续 6 个月
const FRIENDS_N = +(process.env.FRIENDS_N || 4);
function victoryCheck(f) {
  const g = G(), e = g._e, cs = g.citiesOf(f);
  const al = g.factions.filter(h => h.alive && h.id !== f);
  const conds = {
    prestige: e.fac[f].prestige,
    order: cs.length ? avg(cs.map(c => c._e.order)) >= 75 : false,
    cities: cs.filter(c => cityLevel(c) >= 3).length >= 3,
    // 繁荣：≥ 2 座「大郡」或 1 座「都会」
    prosper: (() => { const L = cs.map(cityLevel); return L.filter(v => v >= 3).length >= 2 || L.some(v => v >= 4); })(),
    peace: g.monthIndex - (e.lastAttack || -999) >= 24,
    // 邦交：所有接壤势力好感 ≥ 30 或有约，且至少 FRIENDS_N 个势力如此
    friends: (() => { const ok = h => rel(f, h) >= 30 || oAllied.call(g, f, h) || nonAggro(f, h); const ns = neighbors(f); return ns.every(ok) && al.filter(h => ok(h.id)).length >= Math.min(FRIENDS_N, al.length); })(),
  };
  e.vlog = e.vlog || {};
  for (const k of ['order', 'cities', 'peace', 'friends', 'prosper']) if (conds[k] && e.vlog[k] === undefined) e.vlog[k] = g.monthIndex - e.start;
  for (const P of [1000, 1500, 2000, 2500, 3000]) if (conds.prestige >= P && e.vlog['p' + P] === undefined) e.vlog['p' + P] = g.monthIndex - e.start;
  for (const P of [1000, 1500, 2000, 2500]) {
    const all = conds.prestige >= P && conds.order && conds.prosper && conds.peace && conds.friends;
    const key = 'run' + P; e.vlog[key] = all ? (e.vlog[key] || 0) + 1 : 0;
    if (e.vlog[key] >= 6 && e.vlog['win' + P] === undefined) e.vlog['win' + P] = g.monthIndex - e.start;
  }
}

// ------------------------------------------------------------------ 脚本玩家 --
function cityThreat(f, c) { const g = G(); let t = 0; for (const j of c.links) { const n = g.cities[j]; if (n.owner >= 0 && n.owner !== f && !g.allied(n.owner, f)) t += g.troopsIn(n); } return t; }
function playerTurn(pol) {
  const g = G(), f = g.player;
  let tokens = g.tokensFor(f);
  const use = () => tokens-- > 0;
  const cities = g.citiesOf(f).slice().sort((a, b) => cityThreat(f, b) - cityThreat(f, a));
  const free = c => g.officersIn(c).filter(x => !x.moved && (!x._away || x._away.until <= g.monthIndex));
  const RES = 120;
  // 第四版：外交与经营（先做，每月至多 2 枚令牌）
  if (pol === 'v4') {
    let dipT = 0;
    const cap = capitalOf(f);
    const ns = neighbors(f).sort((a, b) => rel(f, a) - rel(f, b) || power(b) - power(a));
    const envoy = g.generalsOf(f).filter(x => !x._away || x._away.until <= g.monthIndex).sort((a, b) => Math.max(b.intel, b.pol) - Math.max(a.intel, a.pol))[0] || g.ruler(f);
    const envK = 0.8 + Math.max(envoy.intel, envoy.pol) / 250;
    for (const h of ns) {
      if (dipT >= 2 || tokens <= 1) break;
      const k = rng('pdip', h);
      const strong = power(h) > power(f) * 0.9;
      // 停战
      if (atWar(f, h) && strong && !nonAggro(f, h)) {
        const gold = Math.min(cap.gold - RES, Math.round(3 * facInc(h)));
        if (gold > 50) { use(); dipT++; cap.gold -= gold; spend('truce', gold); const p = clamp(0.2 + gold / (facInc(h) * 6 + 1) + rel(f, h) / 300 + clamp(power(f) / (power(h) + 1) - 1, -0.5, 0.5) * 0.2, 0.05, 0.8); if (k.nextDouble() < p) { addPact(f, h, 'truce', 12); addRel(f, h, 15); cnt(f, 'truces', 1); addPrestige(f, PR.truce); } continue; }
      }
      // 联姻
      if (rel(f, h) >= 40 && !pactOf(f, h, 'marriage') && getCnt(f, 'marriages') < 3) {
        const cost = 300 + 50 * g.cityCount(f);
        if (cap.gold >= cost + RES) { use(); dipT++; cap.gold -= cost; spend('marriage', cost); const p = clamp(0.20 + rel(f, h) / 250 + g.factions[f].virtue / 400 + (g.cityCount(h) <= g.cityCount(f) ? 0.1 : 0) + Math.min(0.1, g._e.fac[f].prestige / 5000), 0.05, 0.85); if (k.nextDouble() < p) { addPact(f, h, 'marriage', 48); addRel(f, h, 30); cnt(f, 'marriages', 1); addPrestige(f, PR.marriage); addPrestige(h, PR.marriage); g._e.fac[f].virtueEv += 2; } continue; }
      }
      // 通商协定
      if (rel(f, h) >= 10 && !pactOf(f, h, 'trade') && !atWar(f, h)) { use(); dipT++; const p = clamp(0.35 + rel(f, h) / 200 + envoy.pol / 400, 0.1, 0.9); if (k.nextDouble() < p) { addPact(f, h, 'trade', 24); addRel(f, h, 5); } continue; }
      // 赠礼（对强邻、好感 < 60）
      const lg = (g._e.lastGift || {})[h];
      if (!atWar(f, h) && rel(f, h) < (strong ? 60 : 35) && cap.gold >= 150 + RES && (lg === undefined || g.monthIndex - lg >= 3)) {
        use(); dipT++; const G0 = 150; cap.gold -= G0; spend('gift', G0); g._e.lastGift = g._e.lastGift || {}; const half = g.monthIndex - (g._e.lastGift[h] === undefined ? -99 : g._e.lastGift[h]) < 6 ? 0.5 : 1; g._e.lastGift[h] = g.monthIndex;
        addRel(f, h, giftRel(f, h, G0, envoy) * half); cnt(f, 'gifts', 1);
        continue;
      }
    }
    // 营建
    for (const c of g.citiesOf(f)) {
      if (tokens <= 1 || c._e.proj) continue;
      const front = cityThreat(f, c) > 0;
      const order = front ? ['walls', 'beacons', 'irrigation', 'market', 'drill', 'temple', 'academy', 'granary', 'tuntian', 'forge', 'roads', 'harbour', 'wonder'] : ['market', 'irrigation', 'temple', 'academy', 'wonder', 'granary', 'forge', 'roads', 'harbour', 'drill', 'tuntian'];
      for (const id of order) {
        const p = PROJ[id];
        if (c._e.done.has(id) || !projReqOK(p, c)) continue;
        if (c.gold < p.cost + 200) break;
        use(); c.gold -= p.cost; spend('project', p.cost); const sup = g.officersIn(c).sort((a, b) => b.pol - a.pol)[0] || g.ruler(f); c._e.proj = { id, work: 0, sup: sup.id }; cnt(f, 'projStarted', 1); break;
      }
    }
    // 商队
    const active = g._e.caravans.filter(x => !x.back).length;
    const maxC = Math.min(4, 1 + Math.floor(g.cityCount(f) / 4));
    if (active < maxC && tokens > 1) {
      const src = g.citiesOf(f).sort((a, b) => b.gold - a.gold)[0];
      const leader = g.officersIn(src).length >= 2 ? free(src).filter(x => !g.isRuler(x)).sort((a, b) => b.pol - a.pol)[0] : null;
      if (src && leader && src.gold >= 500 + RES) {
        const best = caravanDest(f, src, leader);
        if (best) {
          use(); const tier = CS.shopTier({ c: src }) + (src._e.done.has('market') ? 1 : 0); const inv = Math.min(src.gold - RES - 100, 200 + 150 * Math.min(4, tier));
          src.gold -= inv; leader._away = { kind: 'caravan', until: g.monthIndex + best.T }; leader.moved = true;
          g._e.caravans.push({ id: g._e.seq++, from: src.id, to: best.owner, hops: best.hops, inv, m: best.m, risk: best.risk, until: g.monthIndex + best.T, leader: leader.id, foreign: best.foreign });
        }
      }
    }
    // 探索
    const activeX = g._e.exped.filter(x => !x.back).length;
    if (activeX < (g.cityCount(f) >= 8 ? 2 : 1) && tokens > 1) {
      const cap2 = capitalOf(f);
      const leader = g.officersIn(cap2).length >= 2 ? free(cap2).filter(x => !g.isRuler(x)).sort((a, b) => (b.intel + b.war) - (a.intel + a.war))[0] : null;
      if (leader && cap2.gold >= 300 + RES) {
        const site = siteFor(f, cap2);
        if (site) { use(); cap2.gold -= 100 + 25 * site.hops; spend('exped', 100 + 25 * site.hops); leader._away = { kind: 'exped', until: g.monthIndex + site.T }; leader.moved = true; g._e.exped.push({ id: g._e.seq++, site: site.s.id, hops: site.hops, until: g.monthIndex + site.T, leader: leader.id }); }
      }
    }
  }
  // 不流血的扩张（v3 与 v4 共用）：占领相邻空城、劝降孤城守将（献城）
  for (const c of g.citiesOf(f)) {
    if (tokens <= 0) break;
    for (const j of c.links) {
      const t = g.cities[j];
      if (tokens <= 0) break;
      if (t.owner < 0) {
        const offs = free(c).filter(x => !g.isRuler(x) && x.troops >= 1000);
        if (offs.length && g.officersIn(c).length >= 2 && cityThreat(f, c) < g.troopsIn(c)) { use(); const s = Conquest.prepare(f, c, t, [offs[0]], Math.min(c.food, 500), 0); Conquest.autoResolve(s); Conquest.apply(s); cnt(f, 'emptyTaken', 1); }
      } else if (t.owner !== f && !g.allied(t.owner, f)) {
        const os = g.officersIn(t);
        if (os.length === 1 && !g.isRuler(os[0])) {
          const agent = free(c).sort((a, b) => b.intel - a.intel)[0];
          if (agent && C.persuadeChance(agent, os[0], f) >= 0.25) { use(); agent.moved = true; const of = t.owner; C.persuade(agent, os[0], f); if (t.owner === f) cnt(f, 'persuadeCity', 1); }
        }
      }
    }
  }
  // 原版内政（v3 与 v4 共用）
  for (const c of cities) {
    if (tokens <= 0) break;
    const offs = free(c);
    const thr = cityThreat(f, c), mine = g.troopsIn(c);
    // 1. 受威胁：征兵 / 训练
    if (thr > mine * 0.8 && offs.length) {
      const weak = offs.filter(x => x.troops < SG.maxTroops(x) * 0.8).sort((a, b) => a.troops - b.troops)[0];
      if (weak && c.gold > 150 && use()) { C.recruit(c, weak, C.recruitMax(c, weak)); continue; }
      const low = offs.filter(x => x.troops > 0 && x.training < 85).sort((a, b) => a.training - b.training)[0];
      if (low && use()) { C.train(low); continue; }
    }
    // 2. 搜索（每城每 4 个月一次）
    if (offs.length && (g.monthIndex + c.id) % 4 === 0 && use()) {
      const sr = offs.sort((a, b) => b.intel - a.intel)[0];
      C.search(c, sr);
      if (C.searchFound && tokens > 0) { use(); C.hire(C.searchFound, sr, f, c.id); }
      continue;
    }
    // 3. 开发最弱的一项
    if (c.gold > 50 + RES && offs.length && use()) {
      const gov = c.governor >= 0 ? g.generals[c.governor] : offs[0];
      const k = c.land <= c.industry && c.land <= c.town ? SG.DevKind.Land : c.industry <= c.town ? SG.DevKind.Industry : SG.DevKind.Town;
      C.develop(c, gov, k);
      continue;
    }
    // 4. 训练
    const low = offs.filter(x => x.troops > 0 && x.training < 70)[0];
    if (low && use()) { C.train(low); continue; }
  }
  // 5. 第二轮开发 / 征兵（有余令牌）
  for (const c of cities) {
    if (tokens <= 0) break;
    const offs = free(c);
    if (!offs.length) continue;
    if (c.gold > 50 + RES * 2 && use()) { const gov = c.governor >= 0 ? g.generals[c.governor] : offs[0]; const k = c.land <= c.industry && c.land <= c.town ? SG.DevKind.Land : c.industry <= c.town ? SG.DevKind.Industry : SG.DevKind.Town; C.develop(c, gov, k); }
    else { const weak = offs.filter(x => x.troops < SG.maxTroops(x) * 0.6)[0]; if (weak && c.gold > 220 && use()) C.recruit(c, weak, Math.min(C.recruitMax(c, weak), 1500)); }
  }
  // 赏赐（不耗令牌）
  for (const x of g.generalsOf(f)) if (!g.isRuler(x) && x.loyalty < 60) { const c = g.cities[x.city]; if (c.gold > 220) C.reward(c, x); }
  // 在野武将：登用（已发现者）
  for (const c of g.citiesOf(f)) for (const x of g.freeFoundIn(c.id)) { const r = g.officersIn(c)[0]; if (r && tokens > 0) { use(); C.hire(x, r, f, c.id); } }
}
// 商队利润率（ENGAGEMENT §9.1）
const CV = { base: 0.04, hop: 0.02, foreign: 0.05, tier: 0.02, polDiv: 800, cap: 0.30 };
// 赠礼的好感增量：min(25, 金 / (8 + 2 × 对方城数) × 使者系数 × 声望系数)
function giftRel(f, h, gold, envoy) { const g = G(); const envK = 0.8 + Math.max(envoy.intel, envoy.pol) / 250; return Math.min(25, Math.round(gold / (8 + 2 * g.cityCount(h)) * envK * Math.min(1.5, 1 + g._e.fac[f].prestige / 2000))); }
function caravanDest(f, src, leader) {
  const g = G();
  const seen = new Map([[src.id, 0]]); let q = [src.id]; const out = [];
  for (let h = 1; h <= 6 && q.length; h++) {
    const nx = [];
    for (const id of q) for (const j of g.cities[id].links) {
      if (seen.has(j)) continue; const c = g.cities[j]; seen.set(j, h);
      const o = c.owner;
      const pass = o < 0 || o === f || oAllied.call(g, o, f) || nonAggro(o, f) || (rel(f, o) >= 0 && !atWar(f, o));
      if (!pass) continue;
      nx.push(j);
      if (o >= 0 && o !== f) {
        const foreign = CAT.FAMILY[c.culture] !== CAT.FAMILY[src.culture];
        let m = CV.base + CV.hop * h + (foreign ? CV.foreign : 0) + CV.tier * (CS.shopTier({ c }) - 1) + (leader.pol - 50) / CV.polDiv + (src._e.done.has('market') ? 0.04 : 0) + (pactOf(f, o, 'trade') ? 0.05 : 0) + buffSum(src, 'caravan');
        m = Math.min(CV.cap, m);
        let risk = clamp(0.02 * h * (1 - leader.war / 200) * (1.3 - src._e.order / 100), 0, 0.3);
        if (src._e.done.has('roads')) risk *= 0.5;
        const T = Math.max(1, 1 + Math.ceil(h / 2) - (src._e.done.has('roads') ? 1 : 0));
        out.push({ c, owner: o, hops: h, m, risk, T, foreign, score: (m * (1 - risk) - risk * 0.5) / T });
      }
    }
    q = nx;
  }
  out.sort((a, b) => b.score - a.score);
  return out[0] || null;
}
function siteFor(f, cap) {
  const g = G();
  const dist = new Map([[cap.id, 0]]); let q = [cap.id];
  for (let h = 1; h <= 8 && q.length; h++) { const nx = []; for (const id of q) for (const j of g.cities[id].links) { if (dist.has(j)) continue; const o = g.cities[j].owner; if (o >= 0 && o !== f && atWar(f, o)) continue; dist.set(j, h); nx.push(j); } q = nx; }
  const cands = [];
  for (const s of CAT.SITES) {
    const c = g.cityByKey(s.near); if (!c || !dist.has(c.id)) continue;
    if (g._e.visitedAt && g._e.visitedAt[s.id] > g.monthIndex - 24) continue;
    const hops = dist.get(c.id);
    cands.push({ s, hops, T: Math.min(6, 2 + Math.ceil(hops / 2)), score: (g._e.visited.has(s.id) ? 0.5 : 1) / (1 + hops) });
  }
  cands.sort((a, b) => b.score - a.score);
  const pick = cands[0];
  if (pick) { g._e.visitedAt = g._e.visitedAt || {}; g._e.visitedAt[pick.s.id] = g.monthIndex; }
  return pick || null;
}
// 电脑对玩家：强邻索贡、示好结盟（第四版玩家脚本的应对：受威胁则接受）
function aiTowardPlayer() {
  const g = G(), f = g.player;
  for (const h of neighbors(f)) {
    const k = rng('aitp', h);
    const e = g._e.fac[h];
    if (power(h) >= 2.5 * (power(f) + 1) && rel(h, f) <= 20 && !nonAggro(f, h) && (e.lastDemand || -99) + 12 <= g.monthIndex && k.nextDouble() < 1 / 12) {
      e.lastDemand = g.monthIndex; E.stats.demand++; card('demand');
      const pay = Math.max(5, Math.round(facInc(f) * 0.08));
      const accept = E.playerPol === 'v4' && cityThreatFrom(f, h) > g.troopsIn(capitalOf(f)) * 0.8;
      if (accept) { addPact(f, h, 'tribute', 12, { payer: f, pay }); addRel(f, h, 10); cnt(f, 'tributeOut', 1); }
      else { addRel(f, h, -15); const fe = g._e.fac[f]; fe.refused = fe.refused || {}; fe.refused[h] = g.monthIndex + 12; }
    }
    if (rel(h, f) >= 40 && !oAllied.call(g, f, h) && k.nextDouble() < 0.08 && E.playerPol === 'v4') { g.setAlliance(f, h, g.monthIndex + B.AllianceMonths); cnt(f, 'aiOffers', 1); card('offer'); }
  }
}
function cityThreatFrom(f, h) { const g = G(); let t = 0; for (const c of g.citiesOf(f)) for (const j of c.links) if (g.cities[j].owner === h) t += g.troopsIn(g.cities[j]); return t; }

// ------------------------------------------------------------------ 运行 --
function newGame(key, seed) {
  SG.Random.seed(seed);
  const g = SG.G = GameState.newGame(key);
  initState(g);
  g._e.start = g.monthIndex;
  return g;
}
function campaign(seed, years, variant) {
  applyOff();
  E.on = variant !== 'off'; E.aiDip = variant === 'dip' || variant === 'all'; E.aiProj = variant === 'proj' || variant === 'all'; E.playerPol = null;
  if (variant === 'nodom') E.domexp = false;
  const g = newGame(SCEN === 'world' ? 'roma' : 'liubei', seed);
  g.player = -1;
  const out = { alive: {}, top: {}, f25: null, lv: {}, order: {}, prestige: {}, gold: {} };
  const total = g.cities.length;
  for (let m = 0; m < years * 12; m++) {
    StrategyAI.runAI([]);
    StrategyAI.endMonth();
    if (out.f25 === null) for (const f of g.factions) if (f.alive && g.cityCount(f.id) > total * 0.25) { out.f25 = (m + 1) / 12; break; }
    const y = (m + 1) / 12;
    if ([1, 3, 5, 10, 15, 20, 25].includes(y)) {
      out.alive[y] = g.factions.filter(f => f.alive).length;
      out.top[y] = Math.max(...g.factions.map(f => g.cityCount(f.id)));
      const lv = g.generals.filter(x => x.faction >= 0 && !x.dead).map(x => x.level);
      const vl = g.generals.filter(x => x.faction >= 0 && !x.dead).map(x => Progress.levelFor(x.exp - 0.5 * (x._dexp || 0)));
      out.lv[y] = { avg: avg(lv), p5: lv.filter(v => v >= 5).length, p7: lv.filter(v => v >= 7).length, n: lv.length, vavg: avg(vl), vk: avg(vl.map(v => 1 + 0.04 * (v - 1))), k: avg(lv.map(v => 1 + 0.04 * (v - 1))) };
      if (g.scenario === 'world') { out.watch = out.watch || {}; out.watch[y] = ['roma', 'parthia', 'kushan'].map(k => g.cityCount(g.factionByKey(k).id)); }
      const own = g.cities.filter(c => c.owner >= 0);
      out.order[y] = avg(own.map(c => c._e ? c._e.order : 50));
      out.prestige[y] = E.on ? avg(g.factions.filter(f => f.alive).map(f => g._e.fac[f.id].prestige)) : 0;
      out.gold[y] = avg(own.map(c => c.gold));
    }
  }
  return out;
}
function playerRun(key, seed, years, pol) {
  applyOff();
  E.on = pol === 'v4' || pol === 'v4ai'; E.aiDip = false; E.aiProj = false; E.playerPol = pol === 'v4' ? 'v4' : null;
  const g = newGame(key, seed);
  const f = g.player;
  const out = { alive: true, months: 0, cities: {}, income: {}, prestige: {}, ach: {}, gens: {}, lv: {}, order: {} };
  let gross = 0, engageGold = 0;
  for (let m = 0; m < years * 12 && g.playerFaction.alive; m++) {
    if (pol === 'ai' || pol === 'v4ai') { const p = g.player; g.player = -1; StrategyAI.runAI([]); g.player = p; }
    else {
      playerTurn(pol);
      if (pol === 'v4') aiTowardPlayer();
      const pb = StrategyAI.runAI([]);
      for (const s of pb) {
        if (!StrategyAI.recheckBattle(s, [])) continue;
        Conquest.autoResolve(s); Conquest.apply(s);
        if (s.captives.length) Conquest.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender);
        for (const F of g.factions) if (F.alive && g.cityCount(F.id) === 0) g.checkFactionDeath(F.id);
        if (!g.playerFaction.alive) break;
      }
    }
    if (!g.playerFaction.alive) break;
    gross += facInc(f);
    StrategyAI.endMonth();
    out.months = m + 1;
    const y = (m + 1) / 12;
    if ([1, 3, 5, 10, 15, 20].includes(y)) {
      out.cities[y] = g.cityCount(f); out.income[y] = facInc(f); out.prestige[y] = E.on ? g._e.fac[f].prestige : 0;
      out.ach[y] = E.on ? g._e.ach.size : 0; out.gens[y] = g.generalsOf(f).length;
      out.lv[y] = avg(g.generalsOf(f).map(x => x.level)); out.order[y] = E.on ? avg(g.citiesOf(f).map(c => c._e.order)) : 0;
    }
  }
  out.alive = g.playerFaction.alive;
  out.gross = gross;
  if (E.on) {
    const c = k => getCnt(f, k);
    out.cnt = { missionsOffered: c('missionsOffered'), missionsDone: c('missionsDone'), missionGold: c('missionGold'), caravans: c('caravans'), tradeProfit: c('tradeProfit'), expeditions: c('expeditions'), expGold: c('expGold'), items: c('items'), rare: c('item:rare'), uncommon: c('item:uncommon'), common: c('item:common'), projects: c('projects'), projStarted: c('projStarted'), wenhuiWin: c('wenhuiWin'), wen1: c('wenhuiPlace1'), wen2: c('wenhuiPlace2'), wen3: c('wenhuiPlace3'), tourneyWin: c('tourneyWin'), marriages: c('marriages'), truces: c('truces'), gifts: c('gifts'), tributeOut: c('tributeOut'), grandFestivals: c('grandFestivals'), sworn: c('sworn'), mentors: c('mentors'), reliefs: c('reliefs'), revealed: c('revealed'), hires: c('hires'), aiOffers: c('aiOffers'), emptyTaken: c('emptyTaken'), persuadeCity: c('persuadeCity'), submissions: c('submissions') };
    out.vlog = g._e.vlog || {};
    for (const k of ['festival', 'gift', 'marriage', 'truce', 'project', 'exped', 'tribute']) out.cnt['spend:' + k] = c('spend:' + k);
    out.cnt.prizeGold = c('prizeGold'); out.cnt.eventValue = c('eventValue');
    const cards = g._e.cards || {}; const months = out.months; let none = 0, rnd = 0, sched = 0, over = 0, maxM = 0;
    for (let m = g._e.start; m < g._e.start + months; m++) { const l = cards[m] || []; const r = l.filter(x => x === 'random' || x === 'global').length, sc = l.length - r; if (!l.length) none++; rnd += Math.min(1, r); sched += Math.min(1, sc); if (sc > 1 || r > 1) over++; maxM = Math.max(maxM, Math.min(1, r) + Math.min(1, sc)); }
    out.cards = { none: none / months, rndPerYear: rnd / months * 12, schedPerYear: sched / months * 12, overflowMonths: over / months, maxShown: maxM };
    out.achList = g._e.log.slice();
    out.pacts = g._e.pacts.filter(p => p.a === f || p.b === f).map(p => p.kind);
  }
  return out;
}

// ------------------------------------------------------------------ 主程序 --
const fmt = (v, d = 1) => (typeof v === 'number' ? v.toFixed(d) : String(v));
const t0 = Date.now();
if (MODE === 'campaign') {
  const seeds = parseInt(process.argv[4], 10) || (SCEN === 'world' ? 8 : 24);
  const years = parseInt(process.argv[5], 10) || (SCEN === 'world' ? 15 : 25);
  const variants = (process.env.VARIANTS || 'off,on,dip,proj').split(',');
  const s0 = parseInt(process.env.SEED0 || '0', 10);
  const res = {};
  for (const v of variants) {
    resetStats();
    const runs = [];
    const perSeed = [];
    for (let s = 1; s <= seeds; s++) { const d0 = E.stats.defect; runs.push(campaign(s0 + s, years, v)); perSeed.push(E.stats.defect - d0); }
    if (process.env.PERSEED) console.log(v + ' defections per seed: ' + perSeed.join(' '));
    res[v + '_casc'] = perSeed.filter(x => x >= 10).length;
    res[v] = { runs, stats: E.stats };
  }
  const ys = [1, 3, 5, 10, 15, 20, 25].filter(y => y <= years);
  console.log(`== campaign ${SCEN}: ${seeds} seeds × ${years} y, all-AI; variants ${variants.join(' / ')}  (${((Date.now() - t0) / 1000).toFixed(1)}s)`);
  for (const v of variants) {
    const { runs, stats } = res[v];
    const al = ys.map(y => fmt(avg(runs.map(r => r.alive[y])))).join(' / ');
    const tp = ys.map(y => fmt(avg(runs.map(r => r.top[y])))).join(' / ');
    const f25 = runs.map(r => (r.f25 === null ? 99 : r.f25)).sort((a, b) => a - b);
    const lvl = ys.map(y => fmt(avg(runs.map(r => r.lv[y].avg)), 2) + ` (L5+ ${fmt(avg(runs.map(r => r.lv[y].p5)), 1)}, L7+ ${fmt(avg(runs.map(r => r.lv[y].p7)), 1)})`).join(' / ');
    console.log(`[${v}] alive y${ys.join('/')}: ${al}`);
    console.log(`      largest faction cities: ${tp};  first >25%: median ${fmt(f25[f25.length >> 1])} y, min ${fmt(f25[0])} y`);
    console.log(`      generals level avg: ${lvl}`);
    console.log(`      老练 multiplier avg (level / vet level with non-battle exp at half): ${ys.map(y => fmt(avg(runs.map(r => r.lv[y].k)), 3) + '/' + fmt(avg(runs.map(r => r.lv[y].vk)), 3)).join('  ')}`);
    if (runs[0].watch) console.log(`      Rome / Parthia / Kushan cities avg: ${ys.map(y => 'y' + y + ' ' + [0, 1, 2].map(i => fmt(avg(runs.map(r => r.watch[y][i])))).join('/')).join(', ')}; min Rome y3 ${Math.min(...runs.map(r => r.watch[3] ? r.watch[3][0] : 99))}, y5 ${Math.min(...runs.map(r => r.watch[5] ? r.watch[5][0] : 99))}`);
    if (v !== 'off') {
      console.log(`      order avg: ${ys.map(y => fmt(avg(runs.map(r => r.order[y])))).join(' / ')};  prestige avg per faction: ${ys.map(y => fmt(avg(runs.map(r => r.prestige[y])), 0)).join(' / ')}`);
      const evN = Object.values(stats.events).reduce((a, b) => a + b, 0);
      console.log(`      events: ${fmt(stats.eventsByFac / Math.max(1, stats.facMonths) * 12, 2)} per faction-year; value ${fmt(stats.evValue / Math.max(1, stats.gross) * 100, 2)}% of gross gold income (gold-equivalent); order income adj ${fmt(stats.orderAdj / Math.max(1, stats.gross) * 100, 2)}%`);
      console.log(`      by event: ${Object.entries(stats.events).sort((a, b) => b[1] - a[1]).map(([k, n]) => k + ' ' + fmt(n / evN * 100, 0) + '%').join(', ')}`);
      console.log(`      defections per seed ${fmt(stats.defect / runs.length)}; level-ups ${fmt(stats.levelUps / runs.length, 0)}/seed; pacts ${JSON.stringify(stats.pacts)}; aiTribute ${stats.aiTribute} aiMarriage ${stats.aiMarriage} aiProj ${stats.aiProj}`);
    } else console.log(`      defections per seed ${fmt(stats.defect / runs.length)}`);
    console.log(`      seeds with a defection cascade (≥ 10 in one game): ${res[v + '_casc']} / ${runs.length}`);
    console.log(`      city gold avg: ${ys.map(y => fmt(avg(runs.map(r => r.gold[y])), 0)).join(' / ')}`);
  }
} else if (MODE === 'player') {
  const seeds = parseInt(process.argv[4], 10) || (SCEN === 'world' ? 4 : 6);
  const years = parseInt(process.argv[5], 10) || (SCEN === 'world' ? 15 : 20);
  const keys = (process.env.PLAYERS || (SCEN === 'world' ? 'goguryeo,yamatai,khotan,satavahana,caledonia,saba' : 'liubei,cao,kongrong,sunjian,taoqian,menghuo')).split(',');
  const pols = (process.env.POL || 'ai,v3,v4').split(',');
  const s0 = parseInt(process.env.SEED0 || '0', 10);
  console.log(`== player ${SCEN}: ${keys.length} rulers × ${seeds} seeds × ${years} y; policies ${pols.join(' / ')}`);
  const ys = [1, 3, 5, 10, 15, 20].filter(y => y <= years);
  const agg = {};
  for (const pol of pols) {
    resetStats();
    const runs = [];
    for (const key of keys) for (let s = 1; s <= seeds; s++) runs.push(Object.assign(playerRun(key, s0 + s * 7 + key.length, years, pol), { key }));
    agg[pol] = { runs, stats: E.stats };
    const at = y => runs.filter(r => r.months >= y * 12);
    console.log(`[${pol}] survival y${ys.join('/')}: ${ys.map(y => fmt(at(y).length / runs.length * 100, 0) + '%').join(' / ')}`);
    console.log(`      cities (survivors): ${ys.map(y => fmt(avg(at(y).map(r => r.cities[y])))).join(' / ')};  income/month: ${ys.map(y => fmt(avg(at(y).map(r => r.income[y])), 0)).join(' / ')};  gens: ${ys.map(y => fmt(avg(at(y).map(r => r.gens[y])), 1)).join(' / ')};  avg level: ${ys.map(y => fmt(avg(at(y).map(r => r.lv[y])), 2)).join(' / ')}`);
    if (pol === 'v4') {
      console.log(`      prestige: ${ys.map(y => fmt(avg(at(y).map(r => r.prestige[y])), 0)).join(' / ')};  achievements: ${ys.map(y => fmt(avg(at(y).map(r => r.ach[y])), 1)).join(' / ')};  order: ${ys.map(y => fmt(avg(at(y).map(r => r.order[y])), 1)).join(' / ')}`);
      const C2 = k => avg(runs.map(r => r.cnt[k] / Math.max(1, r.months / 12)));
      console.log(`      per year: missions offered ${fmt(C2('missionsOffered'), 2)} done ${fmt(C2('missionsDone'), 2)}; caravans ${fmt(C2('caravans'), 2)}; expeditions ${fmt(C2('expeditions'), 2)}; projects ${fmt(C2('projects'), 2)}; items ${fmt(C2('items'), 2)} (rare ${fmt(C2('rare'), 3)}, uncommon ${fmt(C2('uncommon'), 2)}); festivals grand ${fmt(C2('grandFestivals'), 2)}; gifts ${fmt(C2('gifts'), 2)}; marriages ${fmt(C2('marriages'), 3)}; truces ${fmt(C2('truces'), 3)}; tribute paid ${fmt(C2('tributeOut'), 3)}; AI ally offers ${fmt(C2('aiOffers'), 3)}`);
      const share = k => avg(runs.map(r => r.cnt[k] / Math.max(1, r.gross) * 100));
      console.log(`      spending share of gross: festivals ${fmt(share('spend:festival'), 1)}%, gifts ${fmt(share('spend:gift'), 1)}%, projects ${fmt(share('spend:project'), 1)}%, marriages ${fmt(share('spend:marriage'), 1)}%, truces ${fmt(share('spend:truce'), 1)}%, expedition supplies ${fmt(share('spend:exped'), 1)}%, tribute ${fmt(share('spend:tribute'), 1)}%; income: prize gold ${fmt(share('prizeGold'), 1)}%, events (gold-eq) ${fmt(share('eventValue'), 1)}%`);
      const cd = k => avg(runs.map(r => r.cards[k]));
      console.log(`      cards seen: months with none ${fmt(cd('none') * 100, 0)}%; random cards ${fmt(cd('rndPerYear'), 2)}/y; scheduled cards ${fmt(cd('schedPerYear'), 2)}/y; months where the cap (1+1) deferred something ${fmt(cd('overflowMonths') * 100, 1)}%; max shown in a month ${Math.max(...runs.map(r => r.cards.maxShown))}`);
      console.log(`      gold share of gross income: missions ${fmt(share('missionGold'), 1)}%, caravan profit ${fmt(share('tradeProfit'), 1)}%, expeditions ${fmt(share('expGold'), 1)}%`);
      console.log(`      peaceful growth per run: empty cities taken ${fmt(avg(runs.map(r => r.cnt.emptyTaken)), 2)}, 献城 ${fmt(avg(runs.map(r => r.cnt.persuadeCity)), 2)}, 归附 ${fmt(avg(runs.map(r => r.cnt.submissions)), 2)}`);
      console.log(`      文会 placings per 10 y: 1st ${fmt(C2('wen1') * 10, 2)}, 2nd ${fmt(C2('wen2') * 10, 2)}, 3rd ${fmt(C2('wen3') * 10, 2)}; 武林 wins per 10 y ${fmt(C2('tourneyWin') * 10, 2)}; sworn ${fmt(C2('sworn') * 10, 2)}/10y; mentors ${fmt(C2('mentors') * 10, 2)}/10y; reliefs ${fmt(C2('reliefs'), 2)}/y; talents revealed ${fmt(C2('revealed'), 2)}/y`);
      const vt = k => { const v = runs.map(r => r.vlog[k]).filter(x => x !== undefined).map(x => x / 12).sort((a, b) => a - b); return v.length ? `${v.length}/${runs.length} median ${fmt(v[v.length >> 1])} y` : `0/${runs.length}`; };
      console.log(`      王道 conditions first met: order ${vt('order')}; 3 大郡 ${vt('cities')}; prosperity ${vt('prosper')}; friends ${vt('friends')}; prestige 1000 ${vt('p1000')}, 1500 ${vt('p1500')}, 2000 ${vt('p2000')}, 2500 ${vt('p2500')}, 3000 ${vt('p3000')}`);
      console.log(`      王道 all held 6 months: P1000 ${vt('win1000')}; P1500 ${vt('win1500')}; P2000 ${vt('win2000')}; P2500 ${vt('win2500')}`);
      const achY = {}; for (const r of runs) for (const a of r.achList) { const y = Math.floor(a.m / 12) + 1; achY[y] = (achY[y] || 0) + 1; }
      console.log(`      achievements unlocked by game year (avg per run): ${Object.keys(achY).sort((a, b) => a - b).map(y => 'y' + y + ' ' + fmt(achY[y] / runs.length, 1)).join(', ')}`);
      const evN = Object.values(E.stats.events).reduce((a, b) => a + b, 0);
      console.log(`      world events ${fmt(E.stats.eventsByFac / Math.max(1, E.stats.facMonths) * 12, 2)}/faction-year; event value ${fmt(E.stats.evValue / Math.max(1, E.stats.gross) * 100, 2)}% of gross (all factions)`);
    }
  }
} else {
  console.log('usage: engage-proto.js campaign|player classic|world [seeds] [years]');
}
console.log(`done ${((Date.now() - t0) / 1000).toFixed(1)}s`);
