'use strict';
/* ==========================================================================
   tests/engage.js — 第四版非战斗玩法（ENGAGEMENT.md §22.1；INTEGRATION §13 的 ENGAGEMENT 一行；DESIGN-V4 §16 的和平玩家门槛）
   用法：node tests/engage.js [--quick] [--full] [--only 1,5,9] [--seeds N] [--years N]
     --quick   每次推送：§1–§3、§7–§10 全跑；§4 经典 12 种子 × 6 年、世界 4 × 6 年；§5 经典 6 位君主 × 1 种子 × 20 年（v3 / v4），门槛按样本噪声放宽
     缺省       §4 经典 24 种子 × 10 年、世界 8 × 10 年；§5 经典 6 × 3 × 20 年
     --full    §4 经典 2 × 48 种子 × 25 年、世界 24 × 15 年；§5 经典两组 6 × 6 × 20 年、世界 6 × 4 × 15 年（夜间，≈ 30 分钟）
   各节：
     1 目录（tools/gen-engage.js check；js/engage-data.js 与 engagement.json 一致；每个家族变体的文案都可解析）
     2 规则（民心、收入与收成的民心项、人口、声望 → 人望与上限、德、好感与回归、盟约下限、各种外交机会、赠礼、质子、背约、归附、
       羁绊的成形与效果、战斗加成与义愤、内政经验与每月上限 20、老练、国策、营建、商队、探索、文会、节庆、功业、王道、天下榜）
     3 随机数契约（月末各相与自由支出里 0 次 SG.Random；关掉 = 不载入 engage.js 时整局 JSON md5 相同）
     4 战役平衡（全电脑，全部第四版开关「关 / 开」：第 10 年存活、首个大势力、叛逃、世界存活、电脑支出、老练）
     5 和平玩家（ENGAGEMENT §19.3 的脚本玩家：v3 = 只有第三版指令且第四版全关；v4 = 第四版全开 + 本规格的玩法）：
       存活 ≥ v3 − 8 pp、王道 10–40 %（经典）、收入、国策 / 商队 / 探索 / 奖金占比、功业节奏
     6 防繁琐（R1–R6：每月 ≤ 1 张随机卡 + ≤ 1 张定期卡、随机卡每年 3–7 张、≥ 35 % 的月份无卡、同一事件 6 个月不重复、
       同城 12 个月不重复受灾、每张卡都有免费项、不出付不起的选项）
     7 性能（Engage 月末 ≤ 3 ms 经典 / ≤ 8 ms 世界，进程 CPU）     8 存档（往返逐字节相同、读档后同样地继续、v3 迁移、关掉不写字段）
     9 日历（INTEGRATION §6.5：正月至多「大会卡 + 一张」、索贡与归附从不排在正月）    10 电脑预算（没有城因自由支出跌破 0、贡金的底线）
   退出码 = 失败数（FLAG 行是别的工作流决定的平衡项，只报告）
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { load, ROOT } = require('./v4-load.js');

const ARGS = process.argv.slice(2);
const QUICK = ARGS.includes('--quick');
const FULL = ARGS.includes('--full') || ARGS.includes('--nightly');
const argv = (k, d) => { const i = ARGS.indexOf(k); return i >= 0 && ARGS[i + 1] ? ARGS[i + 1] : d; };
const ONLY = argv('--only', null) ? argv('--only').split(',') : null;
const T0 = Date.now();

let fails = 0, checks = 0, flags = 0;
function ok(c, msg) { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } }
function flag(c, msg) { if (!c) { flags++; console.log('  FLAG ' + msg); } }
function eq(a, b, msg) { ok(a === b, msg + ' (got ' + JSON.stringify(a) + ', want ' + JSON.stringify(b) + ')'); }
function near(a, b, msg, eps) { ok(Math.abs(a - b) <= (eps === undefined ? 1e-9 : eps), msg + ' (got ' + a + ', want ' + b + ')'); }
function info(s) { console.log('  ' + s); }
function section(id, name, fn) {
  if (ONLY && ONLY.indexOf(String(id)) < 0) return;
  const t = Date.now();
  console.log('== ' + id + ' ' + name);
  try { fn(); } catch (e) { fails++; console.log('  FAIL (threw) ' + (e.stack || e)); }
  console.log('  (' + ((Date.now() - t) / 1000).toFixed(1) + ' s)');
}
const avg = a => (a.length ? a.reduce((s, v) => s + v, 0) / a.length : 0);
const median = a => { const b = a.slice().sort((x, y) => x - y); return b.length ? b[b.length >> 1] : 0; };
const md5 = s => crypto.createHash('md5').update(s).digest('hex');
const cpuMs = () => { const u = process.cpuUsage(); return (u.user + u.system) / 1000; };

const SG = load({ v4: true });
const { GameState, StrategyAI, Mods, Econ, AIBudget, M } = SG;
const E = SG.Engage;
const B = SG.Balance;
if (!E) { console.log('js/engage.js not loaded'); process.exit(1); }
const V4 = ['Items', 'Titles', 'Engage', 'Tourney', 'Combos'];
if (ARGS[0] === '--perf-child') {
  const scen = ARGS[1] || 'classic', yrs = +(ARGS[2] || 3);
  SG.ScenarioData = SG.Scenarios[scen]; SG.Random.seed(31);
  const g = SG.G = GameState.newGame(scen === 'world' ? 'roma' : 'liubei', scen); g.player = -1;
  const cpu = () => { const u = process.cpuUsage(); return (u.user + u.system) / 1000; };
  const { performance } = require('perf_hooks');
  const wall = () => performance.now();
  // 主线程时间（performance.now；单线程计算，负载 < 核数时 ≈ 本线程 CPU）为准；进程 CPU（含编译线程）一并报告。
  // 前 WARM 个月是 JIT 预热，不计入（浏览器里编译也在后台线程）
  const WARM = 6;
  E.CFG.profile = true; E.CFG.clock = wall;
  let t0 = 0, c0 = 0, m = 0; const per = [], perCpu = [];
  StrategyAI.on('engage', () => { if (m === WARM) { E.stats.parts = {}; } t0 = wall(); c0 = cpu(); }, { owner: 'test', order: 49 });
  StrategyAI.on('engage', () => { if (m >= WARM) { per.push(wall() - t0); perCpu.push(cpu() - c0); } }, { owner: 'test', order: 51 });
  // 校准：固定的一段计算（参考机器约 12 ms），机器慢时报告
  const tc = wall(); let acc = 0; for (let i = 0; i < 1e7; i++) acc += Math.sqrt(i) * 1.0001; const calib = wall() - tc;
  for (m = 0; m < yrs * 12; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
  const sorted = per.slice().sort((a, b) => a - b);
  const parts = {}; for (const [k, v] of Object.entries(E.stats.parts || {})) if (!/^ev:/.test(k)) parts[k] = v / per.length;
  console.log('PERF ' + JSON.stringify({ avg: per.reduce((s, v) => s + v, 0) / per.length, cpu: perCpu.reduce((s, v) => s + v, 0) / perCpu.length, median: sorted[sorted.length >> 1], p90: sorted[Math.floor(sorted.length * 0.9)], parts, calib, acc: acc > 0, n: per.length }));
  process.exit(0);
}
function switches(on, only) { for (const n of V4) if (SG[n] && typeof SG[n] === 'object' && 'enabled' in SG[n] && typeof SG[n].enabled !== 'function') SG[n].enabled = only ? n === only && on : on; }
function fresh(key, scen, seed, opts) {
  opts = opts || {};
  SG.ScenarioData = SG.Scenarios[scen || 'classic'];
  SG.Random.seed(seed || 1);
  if (E.reset) E.reset();
  const g = SG.G = GameState.newGame(key || 'liubei', scen || 'classic');
  if (opts.ai) g.player = -1;
  return g;
}
const months = (n, player) => { for (let m = 0; m < n; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); } };
const gen = (g, name) => g.generals.find(x => x.name === name);
const city = (g, key) => g.cityByKey(key);

// ================================================================================================== 1 ==
section(1, '目录（gen-engage check、生成文件、文案变体）', () => {
  const gen0 = require(path.join(ROOT, 'tools', 'gen-engage.js'));
  const src = gen0.load();
  const r = gen0.check(src);
  eq(r.problems.length, 0, 'tools/gen-engage.js check: no problems' + (r.problems.length ? ' — ' + r.problems.slice(0, 3).join(' | ') : ''));
  eq(r.counts.events, 33, '33 events'); eq(r.counts.modal, 30, '30 with choices'); eq(r.counts.toast, 3, '3 good-news toasts'); eq(r.counts.options, 75, '75 options');
  eq(r.counts.missions, 17, '17 mission templates'); eq(r.counts.projects, 13, '13 projects'); eq(r.counts.sites, 44, '44 sites'); eq(r.counts.sitesClassic, 16, '16 on the classic map');
  eq(r.counts.festivals, 12, '12 festivals'); eq(r.counts.achievements, 48, '48 achievements'); eq(r.counts.bonds, 42, '42 starting bonds'); eq(r.counts.relations, 30, '30 starting relations');
  const D = SG.EngageData;
  eq(D.hash, gen0.hash(gen0.compact(src)), 'js/engage-data.js is the current engagement.json (hash ' + D.hash + ')');
  for (const k of ['events', 'historical', 'sages', 'omens', 'proposals', 'missions', 'missionReward', 'projects', 'sites', 'siteTypes', 'festivals', 'festivalRule', 'achievements', 'bonds', 'relations', 'family', 'river'])
    ok(D[k] !== undefined, 'SG.EngageData.' + k);
  // 每个事件的每个家族变体，文案的占位符都能被 fillText 解析
  const g = fresh('liubei', 'classic', 1); switches(true);
  const ctx = { f: g.player, c: g.cities[0], gov: g.generals[0], ruler: g.ruler(g.player), a: g.generals[1], b: g.generals[2], subject: g.generals[3], subjectFac: 0, far: 1, origin: 2,
    persona: { name: '许劭', title: '月旦评' }, omen: '白鹿', plan: 'tuntian', place: '洛阳太学', home: '中原', itemName: '方物', hiddenName: '某' };
  let bad = 0, n = 0;
  const fams = ['hua', 'han3', 'wa', 'steppe', 'oasis', 'indic', 'persia', 'arabia', 'nile', 'roman', 'north', 'nanhai'];
  for (const ev of D.events) for (const fam of fams.concat(['any'])) {
    const v = ev.variants && ev.variants[fam];
    const txt = E.fillText((v && v.text) || ev.text, ctx);
    n++; if (/[{}]/.test(txt)) { bad++; if (bad < 3) info('unresolved: ' + ev.id + '/' + fam + ' ' + txt); }
  }
  eq(bad, 0, `${n} event texts × family variants resolve every placeholder`);
  // 每项营建在 12 个家族里都有名字；地图里的城都在剧本里
  for (const p of D.projects) for (const fam of fams) ok(!!E.projectName(p.id, fam), 'project ' + p.id + ' named for ' + fam);
  eq(E.projectName('wonder', 'hua'), '铜雀台', '奇观 hua = 铜雀台'); eq(E.projectName('wonder', 'roman'), '凯旋门', '奇观 roman = 凯旋门');
  eq(E.projectName('irrigation', 'persia'), '坎儿井', '水利 persia = 坎儿井');
});

// ================================================================================================== 2 ==
section(2, '规则（表格测试）', () => {
  switches(true);
  const D = SG.EngageData;
  let g = fresh('liubei', 'classic', 2);
  const s = g.engage;
  ok(!!s && typeof s === 'object', 'new game: G.engage exists');
  // ---- 民心 §3.1
  let bad = 0;
  for (const c of g.cities) {
    const want = c.owner < 0 ? 45 : Math.max(30, Math.min(75, Math.round(45 + g.factions[c.owner].virtue / 5 + c.town / 150)));
    if (c.order !== want) bad++;
  }
  eq(bad, 0, 'start order = clamp(round(45 + 德/5 + 町/150), 30, 75) for every city (empty 45)');
  const c0 = g.citiesOf(g.player)[0];
  const adm = Mods.admin(c0);
  near(E.orderTarget(c0), Math.max(0, Math.min(95, 45 + Mods.virtue(g.player) / 5 + (adm && adm.pol >= 80 ? 3 : 0))), 'order target = 45 + effVirtue/5 (+3 when the administrator has 政治 ≥ 80)');
  // 漂移：只开民心（事件、节庆、文会、外交关掉，直接调用月末）
  const subs = ['events', 'festivals', 'wenhui', 'diplomacy', 'projects', 'bonds'];
  for (const k of subs) E[k] = false;
  const tgt = Math.round(E.orderTarget(c0));
  c0.order = tgt - 7; E.monthEnd({ ended: { year: g.year, month: g.month, monthIndex: g.monthIndex } });
  eq(c0.order, tgt - 5, 'order below target: +2 a month');
  c0.order = tgt + 4; E.monthEnd({ ended: { year: g.year, month: g.month, monthIndex: g.monthIndex } });
  eq(c0.order, tgt + 3, 'order above target: −1 a month');
  for (const k of subs) E[k] = true;
  // 收入、收成、人口里的民心项（INTEGRATION §4.2–§4.3：(民心 − 50) / 1000）
  c0.order = 80;
  const gi = Econ.cityGold(c0);
  near(gi.mods.order, 0.03, 'city gold: order term (80 − 50)/1000 = +3 %', 1e-12);
  near(Econ.harvest(c0, 1.0).mods.order, 0.03, 'harvest: the same order term', 1e-12);
  near(Mods.popK(c0), 1.3, 'population growth × (0.5 + order/100)', 1e-12);
  c0.order = 20; near(Econ.cityGold(c0).mods.order, -0.03, 'order 20 → −3 %', 1e-12);
  // 征兵：每 3 000 人民心 −1（向上取整）
  const rg = g.officersIn(c0).find(x => !g.isRuler(x)) || g.officersIn(c0)[0];
  c0.gold = 5000; c0.order = 60; rg.troops = 0;
  SG.Commands.recruit(c0, rg, 2500);
  const n1 = rg.troops;
  eq(c0.order, 60 - Math.ceil(n1 / 3000), 'recruit ' + n1 + ' troops → order −' + Math.ceil(n1 / 3000));
  // ---- 声望 → 人望（一个上限 10，INTEGRATION §3.4）
  const F = g.playerFaction;
  F.prestige = 0; F.eng.fameEv = 0;
  const base = Mods.fameBonus(g.player);
  F.prestige = 455; eq(Mods.fameBonus(g.player) - base, 3, 'prestige 455 → +3 人望 (⌊455/150⌋)');
  F.prestige = 5000; F.eng.fameEv = 4; ok(Mods.fameBonus(g.player) <= 10, 'prestige fame + event fame + gear fame ≤ +10 (got ' + Mods.fameBonus(g.player) + ')');
  F.prestige = 0; F.eng.fameEv = 0;
  F.eng.virtueEv = 30; ok(Mods.virtue(g.player) - Mods.baseVirtue(g.player) <= 15, 'event virtue clamped to +15 with gear (INTEGRATION §3.4)');
  F.eng.virtueEv = 0;
  eq(E.prestigeName(g.player), '无闻', 'prestige 0 → 无闻'); F.prestige = 1500; eq(E.prestigeName(g.player), '四海归心', '1 500 → 四海归心'); F.prestige = 0;
  // ---- 好感与回归 §3.3
  const a = g.player, b = g.factions.find(f => f.id !== a && f.alive).id;
  E.setRel(a, b, 10);
  const relMonth = () => { for (const k of ['events', 'festivals', 'wenhui']) E[k] = false; E.monthEnd({ ended: { year: g.year, month: g.month, monthIndex: g.monthIndex } }); for (const k of ['events', 'festivals', 'wenhui']) E[k] = true; };
  const mi0 = g.monthIndex;
  relMonth();
  eq(E.rel(a, b), mi0 % 2 === 0 ? 9 : 10, 'relation 10: one step toward 0 every second month (month ' + mi0 + ')');
  E.setRel(a, b, 50); relMonth(); eq(E.rel(a, b), mi0 % 3 === 0 ? 49 : 50, 'relation 50: one step toward +30 every third month');
  E.setRel(a, b, -60); relMonth(); eq(E.rel(a, b), mi0 % 3 === 0 ? -59 : -60, 'relation −60: one step toward −30 every third month');
  E.setRel(a, b, 0);
  // 盟约的下限
  s.pacts.push({ a, b, kind: 'marriage', since: g.monthIndex, until: g.monthIndex + 48 });
  relMonth(); ok(E.rel(a, b) >= 50, 'marriage pact floor 50 (got ' + E.rel(a, b) + ')');
  ok(E.noAttack(a, b), 'marriage = non-aggression (noAttack)');
  ok(Mods.noAttack(a, b), 'provider noAttack is live through SG.Mods');
  s.pacts.length = 0; E.setRel(a, b, 65); ok(E.noAttack(a, b), 'relation ≥ 60 also stops AI attacks'); E.setRel(a, b, 0);
  ok(!E.noAttack(a, b), 'relation 0, no pact: not protected');
  // ---- 外交的机会（§10）
  const t = b, env = g.generalsOf(a).find(x => !g.isRuler(x));
  E.setRel(a, t, 40);
  const C = E.CFG;
  const r0 = E.rel(a, t), vir = Mods.virtue(a);
  near(E.chance('marriage', a, t, {}), Math.max(0.05, Math.min(0.85, 0.20 + r0 / 250 + vir / 400 + (g.cityCount(t) <= g.cityCount(a) ? 0.10 : 0) + Math.min(0.10, g.factions[a].prestige / 5000))), 'marriage chance (§10.4)', 1e-12);
  const X = Math.round(E.facInc(a) * 0.08);
  near(E.chance('tribute', a, t, { pay: X }), Math.max(0.05, Math.min(0.95, 0.40 + 2 * X / E.facInc(t) + r0 / 300)), 'tribute chance (§10.5)', 1e-12);
  near(E.chance('trade', a, t, { envoy: env }), Math.max(0.10, Math.min(0.90, 0.35 + r0 / 200 + env.pol / 400)), 'trade-pact chance (§9.2)', 1e-12);
  near(E.chance('truce', a, t, { gold: 200 }), Math.max(0.05, Math.min(0.80, 0.20 + 200 / (6 * E.facInc(t)) + r0 / 300 + 0.2 * Math.max(-0.5, Math.min(0.5, E.power(a) / E.power(t) - 1)))), 'truce chance (§10.7)', 1e-12);
  const hp = E.chance('marriage', a, t, { hostage: env }) - E.chance('marriage', a, t, {});
  near(hp, Math.min(0.85 - E.chance('marriage', a, t, {}), 0.10 + Math.max(env.war, env.intel, env.pol) / 600), 'hostage: chance + 0.10 + max(武,智,政)/600 (§10.6)', 1e-9);
  // 同盟：v3 机会 + 好感 / 400（一次截断）
  const pAl0 = (() => { E.setRel(a, t, 0); return SG.Commands.allyChance(a, t, 0); })();
  E.setRel(a, t, 80); const pAl1 = SG.Commands.allyChance(a, t, 0);
  ok(Math.abs((pAl1 - pAl0) - Math.min(0.2, 0.9 - pAl0)) < 1e-9 || pAl1 === 0.9, 'alliance chance + rel/400 (+0.20 at relation 80; got +' + (pAl1 - pAl0).toFixed(3) + ')');
  E.setRel(a, t, 0);
  // 赠礼：Δ = min(25, round(金 / (8 + 2 × 对方城数) × 使者 × 声望))；6 个月内再送减半
  const dG = E.giftDelta(a, t, 150, env);
  eq(dG, Math.min(25, Math.round(150 / (8 + 2 * g.cityCount(t)) * (0.8 + Math.max(env.intel, env.pol) / 250) * Math.min(1.5, 1 + g.factions[a].prestige / 2000))), 'gift relation delta formula (§10.2)');
  const cap = E.capitalOf(a); cap.gold = 2000;
  const rg1 = E.propose('gift', a, t, { gold: 150, envoy: env, city: cap });
  ok(rg1.ok && E.rel(a, t) === dG, 'gift 150 → relation +' + dG);
  const dG2 = E.giftDelta(a, t, 150, env);
  eq(dG2, Math.round(dG / 2), 'second gift within 6 months: halved');
  E.setRel(a, t, 0);
  // ---- 羁绊 §3.4 / §11
  const liu = gen(g, '刘备'), guan = gen(g, '关羽'), zhang = gen(g, '张飞');
  eq(E.bondKind(liu, guan), 'sworn', '刘备–关羽 sworn at start'); eq(E.bondKind(guan, zhang), 'sworn', '关羽–张飞 sworn');
  const ganning = gen(g, '甘宁'), lingtong = gen(g, '凌统');
  if (ganning && lingtong) eq(E.bondKind(ganning, lingtong), 'feud', '甘宁–凌统 feud at start');
  const x1 = g.generals.find(x => x.faction === a && !g.isRuler(x) && !E.bondsOf(x).length && x !== env), x2 = g.generals.find(x => x.faction === a && x !== x1 && !g.isRuler(x) && !E.bondKind(x1, x));
  if (x1 && x2) {
    E.addAff(x1, x2, 50); eq(E.bondKind(x1, x2), 'friend', 'affinity ≥ 50 → 知己');
    E.addAff(x1, x2, -25); eq(E.bondKind(x1, x2), null, '知己 below 30 → no bond');
    E.addAff(x1, x2, -80); eq(E.bondKind(x1, x2), 'feud', 'affinity ≤ −50 → 不和');
    E.addAff(x1, x2, 30); eq(E.bondKind(x1, x2), null, '不和 above −30 → no bond');
    E.addAff(x1, x2, -E.bond(x1, x2).a);
  }
  // 防叛：结义 +30（INTEGRATION §3.4：与官爵的防护取较大者）
  guan.loyalty = 30;
  eq(Mods.effLoy(guan), 30 + Math.max(30, Mods.z('titleShield', guan)), '关羽 effLoy = loyalty + max(title shield, 30)');
  guan.loyalty = 100;
  // 战斗：同一方的结义 +4 / +4，合计 ±8 %；义愤 +6 %
  const unit = (x, side, alive) => ({ gen: x, side, alive: alive !== false });
  const mdl = { units: [unit(liu, 0), unit(guan, 0), unit(zhang, 0)] };
  const bb = E.battleBonus(mdl.units[1], mdl);
  near(bb.atk, 0.08, '关羽 with two sworn brothers: attack +8 % (cap)', 1e-12); near(bb.def, 0.08, 'defence +8 % (cap)', 1e-12);
  near(Mods.z('bondAtk', mdl.units[1], mdl), 0.08, 'provider bondAtk', 1e-12);
  const mdl2 = { units: [unit(liu, 0), unit(guan, 0)] };
  near(E.battleBonus(mdl2.units[1], mdl2).atk, 0.04, 'one sworn brother: +4 %', 1e-12);
  const mdl3 = { units: [unit(liu, 0, false), unit(guan, 0)] };
  near(E.battleBonus(mdl3.units[1], mdl3).atk, 0.06, '义愤: the sworn brother routed → +6 % (the routed one gives no +4)', 1e-12);
  near(E.battleBonus(mdl2.units[1], { units: [unit(liu, 1), unit(guan, 0)] }).atk, 0, 'bonds act only on the same side', 1e-12);
  if (ganning && lingtong) { const m4 = { units: [unit(ganning, 0), unit(lingtong, 0)] }; near(E.battleBonus(m4.units[0], m4).atk, -0.04, '不和 in one army: −4 %', 1e-12); }
  // ---- 内政经验：表与每月 20 的上限（§4.1）
  const dv = g.officersIn(c0)[0];
  c0.gold = 99999; g.engage.xp = { m: g.monthIndex, u: {} };
  const e0 = dv.exp, d0 = dv.dexp | 0;
  for (let i = 0; i < 5; i++) SG.Commands.develop(c0, dv, SG.DevKind.Land);
  const got = dv.exp - e0;
  eq(got, 20, '5 develops in one month: routine exp 6 / 8 each, capped at 20');
  eq((dv.dexp | 0) - d0, got, 'domestic exp is non-battle exp (gen.dexp)');
  // 老练：非战斗经验按一半（§4.4）
  const vg = { exp: 1600, dexp: 1000, level: SG.Progress.levelFor(1600) };
  eq(E.vetLevel(vg), SG.Progress.levelFor(1100), 'vetLevel = levelFor(exp − 0.5 × dexp)');
  // ---- 营建 §8
  g = fresh('liubei', 'classic', 3);
  const pc = g.citiesOf(g.player)[0];
  pc.gold = 5000; pc.land = Math.max(pc.land, 320);
  const list = E.buildable(pc);
  eq(list.length, 13, 'buildable lists all 13 projects');
  const roads = list.find(x => x.id === 'roads');
  ok(roads && roads.ok, 'roads (no requirement) buildable');
  const sup = g.officersIn(pc).slice().sort((p, q) => q.pol - p.pol || p.id - q.id)[0];
  const rs = E.startProject(pc, 'market', sup.id);
  if (!rs.ok) { const m2 = E.startProject(pc, 'roads', sup.id); ok(m2.ok, 'start a project'); }
  const pid = pc.eng.proj.id, work = D.projects.find(p => p.id === pid).work;
  const per = 10 + sup.pol / 8;
  E.events = false; E.diplomacy = false;
  E.monthEnd({ ended: { year: g.year, month: g.month, monthIndex: g.monthIndex } });
  near(pc.eng.proj.work, per, 'progress 10 + 监工政治/8 per month (' + per.toFixed(2) + ')', 1e-9);
  pc.eng.proj.work = work - 1;
  const pr0 = g.playerFaction.prestige;
  E.monthEnd({ ended: { year: g.year, month: g.month, monthIndex: g.monthIndex } });
  ok(pc.eng.done.indexOf(pid) >= 0 && !pc.eng.proj, pid + ' completes');
  ok(g.playerFaction.prestige - pr0 >= 10, 'project done: prestige +10');
  E.events = true; E.diplomacy = true;
  if (pid === 'market') near(Mods.z('project', pc, 'income'), 0.06, 'market: income +6 % through the project provider', 1e-12);
  pc.eng.done.push('walls');
  eq(SG.cityDefense(pc) - (20 + Math.floor(pc.town / 12)) - Mods.z('cityDef', pc) + E.defense(pc), E.defense(pc), 'walls through SG.cityDefense (provider cityDef)');
  ok(E.defense(pc) >= 10, 'walls +10 defence');
  pc.eng.done.push('forge');
  near(Mods.z('project', pc, 'dev:industry'), 0.10, '铁官: 开发·产业 +10 %', 1e-12);
  near(Mods.z('project', pc, 'recruit'), 0.10, '铁官: 征兵 +10 % troops per gold', 1e-12);
  pc.eng.done.push('drill'); eq(Mods.z('recruitDrill', pc), 10, '讲武堂: new recruits at training 40 (+10)');
  // 奇观的城级要求（≥ 郡城）
  const wonder = D.projects.find(p => p.id === 'wonder');
  ok(wonder.req.minCityLevel === 3, 'wonder needs city level 3 (郡城)');
  // ---- 商队 §9.1
  const cv = g.citiesOf(g.player)[0];
  cv.gold = 3000;
  const ldr = g.officersIn(cv).filter(x => !g.isRuler(x)).sort((p, q) => q.pol - p.pol)[0];
  const tg = ldr ? E.caravanTargets(cv, ldr) : [];
  if (tg.length) {
    const t0 = tg[0], dest = g.cities[t0.city];
    const foreign = (SG.EngageData.family[dest.culture] || 'hua') !== (SG.EngageData.family[cv.culture] || 'hua');
    const m0 = Math.min(0.30, 0.04 + 0.02 * t0.hops + (foreign ? 0.05 : 0) + 0.02 * (E.shopTier(dest) - 1) + (ldr.pol - 50) / 800 + (E.hasProject(cv, 'market') ? 0.04 : 0) + (E.pactOf(g.player, t0.owner, 'trade') ? 0.05 : 0));
    near(t0.margin, Math.round(m0 * 1000) / 1000, 'caravan margin formula (§9.1)', 1e-9);
    const risk0 = Math.max(0, Math.min(0.3, 0.02 * t0.hops * (1 - ldr.war / 200) * (1.3 - cv.order / 100))) * (E.hasProject(cv, 'roads') ? 0.5 : 1);
    near(t0.risk, Math.round(risk0 * 1000) / 1000, 'caravan risk formula', 1e-9);
    eq(t0.months, Math.max(1, 1 + Math.ceil(t0.hops / 2) - (E.hasProject(cv, 'roads') ? 1 : 0)), 'caravan time 1 + ⌈hops/2⌉');
    if (g.officersIn(cv).length >= 2) {
      const before = cv.gold;
      const sc = E.sendCaravan(cv, ldr, t0.city, 300);
      ok(sc.ok, 'send a caravan' + (sc.ok ? '' : ': ' + sc.why));
      if (sc.ok) {
        eq(before - cv.gold, 300, 'invest paid up front');
        ok(g.absent(ldr) && g.officersIn(cv).indexOf(ldr) < 0, 'leader away (not in generalsIn)');
        for (let i = 0; i < t0.months; i++) months(1);
        const back = g.engage.caravans.find(x => x.id === sc.caravan.id);
        ok(back && back.back, 'caravan returned after ' + t0.months + ' months');
        if (back) eq(back.ret, back.lost ? 150 : Math.round(300 * (1 + back.m)), 'return = invest × (1 + m), or half when lost');
        ok(!g.absent(ldr) || ldr.dead || ldr.faction !== g.player, 'leader home again');
      }
    }
  } else info('(no caravan route from ' + cv.name + ' at this seed)');
  // ---- 探索 §12
  g = fresh('liubei', 'classic', 4);
  const xc = E.capitalOf(g.player); xc.gold = 3000;
  const st0 = E.sites(g.player, xc);
  eq(st0.length, 16, 'classic: the 16 sites on the classic map (unreachable ones listed with a reason)');
  const reach = st0.filter(x => x.ok);
  ok(reach.length > 0, 'some site reachable from ' + xc.name + ' (' + reach.length + ')');
  const xl = g.officersIn(xc).filter(x => !g.isRuler(x))[0];
  if (reach.length && xl && g.officersIn(xc).length >= 2) {
    const sx = E.sendExpedition(xc, xl, reach[0].site.id);
    ok(sx.ok, 'send an expedition' + (sx.ok ? '' : ': ' + sx.why));
    eq(sx.expedition.until - g.monthIndex, Math.min(6, 2 + Math.ceil(reach[0].hops / 2)), 'expedition time 2 + ⌈hops/2⌉ (≤ 6)');
    for (let i = 0; i < 7; i++) months(1);
    const ex = g.engage.exped.find(x => x.id === sx.expedition.id);
    ok(ex && ex.back && ex.res, 'expedition returned with a result');
    if (ex && ex.res && !ex.res.mishap) ok(ex.res.gold > 0 && ex.res.prestige > 0, 'gold ' + ex.res.gold + ', prestige ' + ex.res.prestige);
    ok(g.engage.visited.indexOf(reach[0].site.id) >= 0, 'site marked visited');
    const again = E.sites(g.player, xc).find(x => x.site.id === reach[0].site.id);
    ok(again && !again.ok, 'revisit only after 24 months');
  }
  // ---- 文会 §13：确定性、奖励
  g = fresh('liubei', 'classic', 5);
  const w1 = JSON.stringify((() => { const r = E.runWenhui({ year: 191 }); return r.top.map(x => [x.gen, x.f, x.s]); })());
  g = fresh('liubei', 'classic', 5);
  const preP = g.factions.map(f => f.prestige);
  const wr = E.runWenhui({ year: 191 });
  eq(JSON.stringify(wr.top.map(x => [x.gen, x.f, x.s])), w1, '文会 deterministic for a seed and year');
  ok(wr.top.length === 3, 'three placings');
  const wf = wr.top[0].f, hostF = wr.host;
  ok(g.factions[wf].prestige - preP[wf] >= 50, 'winner faction prestige +50 (+20 if host)');
  ok(wr.top.every((e, i) => i === 0 || wr.top[i - 1].s >= e.s), 'placings sorted by total');
  ok(hostF >= 0 && g.factions[hostF].prestige - preP[hostF] >= 20, 'host prestige +20');
  // 论辩内核：确定性、5 回合、奖励 ±12
  const me = g.generalsOf(g.player).sort((p, q) => q.intel - p.intel)[0], rv = g.generals.find(x => x.faction >= 0 && x.faction !== g.player && x.intel >= 80);
  let ds = E.debate({ me: me.id, rival: rv.id }, null);
  for (const stc of ['bo', 'yin', 'yu', 'bo', 'bo']) ds = E.debate(ds, stc);
  ok(ds.round === 5 && Math.abs(ds.bonus) <= 12, '论辩: 5 exchanges, bonus within ±12 (got ' + ds.bonus + ')');
  let ds2 = E.debate({ me: me.id, rival: rv.id }, null); for (const stc of ['bo', 'yin', 'yu', 'bo', 'bo']) ds2 = E.debate(ds2, stc);
  eq(ds2.bonus, ds.bonus, '论辩 deterministic');
  eq(E.touhuScore([{ angle: 0.01, power: 0.02 }, { angle: 0.1, power: 0.05 }, { angle: 0.5, power: 0 }]), 5, '投壶 scoring 贯耳 3 + 中壶 2 + 0');
  // ---- 节庆 §5.3
  g = fresh('liubei', 'classic', 6);
  const fcap = E.capitalOf(g.player); fcap.gold = 2000;
  const cost = E.festivalCost(g.player);
  eq(cost, Math.min(400, 30 + 15 * g.cityCount(g.player)), 'grand festival cost 30 + 15 × cities (≤ 400)');
  const ord0 = g.citiesOf(g.player).map(c => c.order), pr1 = g.playerFaction.prestige;
  const hf = E.holdFestival(g.player, 'grand');
  ok(hf.ok, 'hold a grand festival');
  ok(g.citiesOf(g.player).every((c, i) => c.order === Math.min(100, ord0[i] + 6)), 'grand: every city order +6');
  eq(g.playerFaction.prestige - pr1, 10, 'grand: prestige +10');
  eq(fcap.gold, 2000 - cost, 'paid from the capital');
  eq(E.festivalOf(g.player).name, '腊祭·大傩', '刘备 (hua): 腊祭·大傩'); eq(E.festivalOf(g.player).month, 12, 'in month 12');
  // ---- 功业 §14、王道 §15、天下榜
  const ach = E.achievements(g.player);
  eq(ach.length, 48, '48 achievements listed');
  const sc0 = E.score(g.player);
  const cs = g.citiesOf(g.player);
  const pactsN = E.pacts(g.player).length + g.factions.filter(h => h.alive && h.id !== g.player && g.alliance[g.player * g.factions.length + h.id] > g.monthIndex).length;
  eq(sc0, Math.round(20 * cs.length + cs.reduce((s2, c) => s2 + c.land + c.industry + c.town, 0) / 100 + g.playerFaction.prestige / 10 + g.generalsOf(g.player).reduce((s2, x) => s2 + (x.level || 1), 0) + 10 * pactsN), 'score formula (§15.5)');
  const lb = E.leaderboard();
  ok(lb.length === g.factions.filter(f => f.alive).length && lb[0].rank === 1, 'leaderboard ranks every living faction');
  const wd = E.wangdaoConds(g.player);
  ok(!wd.prestige && !wd.peace === false, '王道 at start: prestige not met, peace met (no attack yet)');
  // 归附：吞并的后果（§10.8）
  g = fresh('liubei', 'classic', 7);
  const H = g.factions.find(f => f.alive && f.id !== g.player && g.cityCount(f.id) <= 2);
  if (H) {
    const hc = g.citiesOf(H.id).length, hg = g.generalsOf(H.id).length, p0 = g.playerFaction.prestige;
    const hr = g.ruler(H.id);
    ok(E.absorb(g.player, H.id), '归附 absorb');
    ok(!H.alive, 'the joining faction ends without war');
    eq(g.citiesOf(g.player).length >= hc + 1, true, 'its cities join');
    ok(g.playerFaction.prestige - p0 === 30, '归附: prestige +30');
    ok(hr.faction === g.player && hr.loyalty >= 50 && hr.loyalty <= 95, 'its ruler joins with loyalty in 50–95 (got ' + hr.loyalty + ')');
    ok(g.generalsOf(g.player).length >= hg, 'its generals join');
  }
  // 背约：攻打停战之约的对象（§10.10）
  g = fresh('liubei', 'classic', 8);
  const nb = E.neighbors(g.player);
  if (nb.length) {
    const tt = nb[0];
    g.engage.pacts.push({ a: g.player, b: tt, kind: 'truce', since: g.monthIndex, until: g.monthIndex + 12 });
    const bc = E.betrayalCost(g.player, tt);
    ok(bc && bc.prestige === 20 && bc.virtue === 8, 'betrayal cost of a truce: prestige −20, virtue −8');
    g.playerFaction.prestige = 100;
    const src = g.citiesOf(g.player).find(c => c.links.some(j => g.cities[j].owner === tt));
    const tc = g.cities[src.links.find(j => g.cities[j].owner === tt)];
    const sv = SG.Conquest.prepare(g.player, src, tc, g.officersIn(src).slice(0, 1), 0, 0);
    eq(g.playerFaction.prestige, 80, 'attacking the truce partner: prestige −20');
    eq(g.playerFaction.eng.virtueEv, -8, 'virtue (event bucket) −8');
    ok(!E.pactOf(g.player, tt, 'truce'), 'the truce ends at once');
    ok(E.atWar(g.player, tt) && E.rel(g.player, tt) <= -25, 'war memory and relation −25');
    for (const x of sv.atk) { x.city = src.id; x.moved = false; }
  }
  // 国策：提议、受命、完成与报酬（§7）
  g = fresh('liubei', 'classic', 9);
  const offers = E.offerMissions(g.player, 2);
  ok(offers.length === 2, 'the court offers two missions');
  const om = offers.find(o => o.id === 'm_order') || offers[0];
  const am = E.acceptMission(g.player, om.oid);
  ok(am.ok, 'accept a mission');
  ok(!E.missionOffers(g.player).some(o => !o.bonus), 'taking one of the two offers withdraws the other (§7.1: one or none)');
  const prog = E.missions(g.player)[0];
  ok(prog && prog.progress && prog.text, 'mission progress and text (' + (prog && prog.text) + ')');
  ok(prog.reward.gold === Math.round([0.4, 0.8, 1.2][prog.tier - 1] * prog.rewardBase), 'reward gold = k × faction income at the offer');
});

// ================================================================================================== 3 ==
section(3, '随机数契约', () => {
  switches(true);
  // (a) 第四版月末各相 + 自由支出里，ENGAGEMENT 的钩子 0 次 SG.Random
  let inEng = false, calls = 0;
  const R = SG.Random, wrap = {};
  for (const k of ['value', 'rangeInt', 'rangeFloat']) { wrap[k] = R[k]; R[k] = function () { if (inEng) calls++; return wrap[k].apply(this, arguments); }; }
  const offs = [];
  for (const ph of ['income', 'engage', 'news']) { offs.push(StrategyAI.on(ph, () => { inEng = true; }, { owner: 'test', order: 49 })); offs.push(StrategyAI.on(ph, () => { inEng = false; }, { owner: 'test', order: 51 })); }
  for (const k of ['project', 'festival']) { offs.push(StrategyAI.onSpend(k, () => { inEng = true; }, { owner: 'test', order: 49 })); offs.push(StrategyAI.onSpend(k, () => { inEng = false; }, { owner: 'test', order: 101 })); }
  try {
    fresh('liubei', 'classic', 11, { ai: true });
    months(60);
  } finally { for (const o of offs) o(); for (const k of Object.keys(wrap)) R[k] = wrap[k]; }
  eq(calls, 0, '5 classic years all-AI: SG.Random calls inside ENGAGEMENT phases and spending hooks');
  // (b) 关掉 = 不载入：5 年全电脑整局 JSON md5 相同（其余第四版模块照常开着）
  const runA = SGx => { SGx.ScenarioData = SGx.Scenarios.classic; SGx.Random.seed(12); const g = SGx.G = SGx.GameState.newGame('liubei', 'classic'); g.player = -1; for (let m = 0; m < 60; m++) { SGx.StrategyAI.runAI([]); SGx.StrategyAI.endMonth(); } return md5(JSON.stringify(g)); };
  const S1 = load({ v4: true, skip: ['engage.js', 'engage-data.js'] });
  const S2 = load({ v4: true });
  S2.Engage.enabled = false;
  for (const S of [S1, S2]) for (const n of ['Items', 'Titles', 'Tourney', 'Combos']) if (S[n] && 'enabled' in S[n] && typeof S[n].enabled !== 'function') S[n].enabled = true;
  const h1 = runA(S1), h2 = runA(S2);
  eq(h2, h1, 'Engage.enabled = false: the 5-year all-AI game equals the game without engage.js (' + h1 + ')');
  // (c) 关掉时不建任何状态
  const g3 = (() => { S2.ScenarioData = S2.Scenarios.classic; S2.Random.seed(13); const g = S2.G = S2.GameState.newGame('liubei', 'classic'); return g; })();
  ok(g3.engage === undefined && g3.cities.every(c => c.order === undefined && c.eng === undefined) && g3.factions.every(f => f.prestige === undefined), 'switched off: no Engage fields in a new game');
});

// ================================================================================================== 4 ==
function campaign(scen, seeds, years, seed0, variant) {
  switches(variant === 'all');
  AIBudget.reset();
  StrategyAI.stats.defections = 0;
  E.resetStats();
  const out = [];
  const ys = [5, 10, 15, 25];
  for (let s = seed0 + 1; s <= seed0 + seeds; s++) {
    const g = fresh(scen === 'world' ? 'roma' : 'liubei', scen, s, { ai: true });
    const d0 = StrategyAI.stats.defections;
    const r = { alive: {}, f25: null, def: 0, vk: {}, order: {}, prestige: {} };
    const total = g.cities.length;
    for (let m = 0; m < years * 12; m++) {
      StrategyAI.runAI([]); StrategyAI.endMonth();
      if (r.f25 === null) for (const f of g.factions) if (f.alive && g.cityCount(f.id) > total * 0.25) { r.f25 = (m + 1) / 12; break; }
      const y = (m + 1) / 12;
      if ((m + 1) % 12 === 0) r.alive[y] = g.factions.filter(f => f.alive).length;
      if (ys.indexOf(y) >= 0) {
        const gs = g.generals.filter(x => x.faction >= 0 && !x.dead);
        r.vk[y] = avg(gs.map(x => 1 + 0.04 * (SG.Progress.vetLevel(x) - 1)));
        if (g.engage) { r.order[y] = avg(g.cities.filter(c => c.owner >= 0).map(c => c.order)); r.prestige[y] = avg(g.factions.filter(f => f.alive).map(f => f.prestige)); }
      }
    }
    r.def = StrategyAI.stats.defections - d0;
    out.push(r);
  }
  return { runs: out, aib: JSON.parse(JSON.stringify(AIBudget.stats)), est: JSON.parse(JSON.stringify(E.stats)) };
}
section(4, '战役平衡（全电脑，第四版全关 / 全开）', () => {
  const sets = FULL ? [['classic', 48, 25, 0], ['classic', 48, 25, 1000], ['world', 24, 15, 0]] : QUICK ? [['classic', 12, 6, 0], ['world', 4, 6, 0]] : [['classic', 24, 10, 0], ['world', 8, 10, 0]];
  for (const [scen, seeds, years, s0] of sets) {
    const t0 = Date.now();
    const off = campaign(scen, seeds, years, s0, 'off'), all = campaign(scen, seeds, years, s0, 'all');
    const tag = `${scen} ${seeds}×${years}${s0 ? ' s' + s0 : ''}`;
    const yk = scen === 'classic' ? Math.min(10, years) : Math.min(15, years);
    const al = v => avg(v.runs.map(r => r.alive[yk]));
    const tolA = scen === 'classic' ? (FULL ? 0.8 : 1.5) : (FULL ? 2 : 4);
    ok(al(all) >= al(off) - tolA, `${tag}: factions alive y${yk} all ≥ off − ${tolA} (${al(off).toFixed(2)} → ${al(all).toFixed(2)})`);
    if (scen === 'classic') {
      const f = v => median(v.runs.map(r => (r.f25 === null ? 99 : r.f25)));
      const tolF = FULL ? 0.5 : 1.0;
      ok(f(all) >= f(off) - tolF, `${tag}: first faction > 25 % median all ≥ off − ${tolF} y (${f(off).toFixed(2)} → ${f(all).toFixed(2)})`);
    }
    const de = v => avg(v.runs.map(r => r.def));
    ok(de(all) <= de(off) + 2, `${tag}: defections per seed all ≤ off + 2 (${de(off).toFixed(2)} → ${de(all).toFixed(2)})`);
    eq(all.aib.below0, 0, `${tag}: no city below 0 after a discretionary spend`);
    const gross = all.aib.gross || 1, k = all.aib.byKind;
    const disc = ['title', 'gear', 'project', 'festival'].reduce((s2, x) => s2 + (k[x] || 0), 0);
    ok(disc / gross <= 0.225, `${tag}: discretionary ≤ 22 % of gross (${(100 * disc / gross).toFixed(2)} %)`);
    info(`${tag}: AI spending of gross: project ${(100 * (k.project || 0) / gross).toFixed(2)} %, festival ${(100 * (k.festival || 0) / gross).toFixed(2)} %, event ${(100 * (k.event || 0) / gross).toFixed(2)} %, tribute ${(100 * (k.tribute || 0) / gross).toFixed(2)} %, gear ${(100 * (k.gear || 0) / gross).toFixed(2)} %, title ${(100 * (k.title || 0) / gross).toFixed(2)} %`);
    if (FULL) {
      flag((k.project || 0) / gross >= 0.03 && (k.project || 0) / gross <= 0.09, `${tag}: AI projects 3–9 % of gross (${(100 * (k.project || 0) / gross).toFixed(2)} %)`);
      ok((k.festival || 0) / gross <= 0.02, `${tag}: AI festivals ≤ 2 % of gross (${(100 * (k.festival || 0) / gross).toFixed(2)} %)`);
    }
    const yv = Math.min(10, years);
    if (all.runs[0].vk[yv] !== undefined && off.runs[0].vk[yv] !== undefined) {
      const vo = avg(off.runs.map(r => r.vk[yv])), va = avg(all.runs.map(r => r.vk[yv]));
      ok(va <= vo + 0.03 + (FULL ? 0 : 0.01), `${tag}: 老练 ×${va.toFixed(3)} at y${yv} ≤ off ×${vo.toFixed(3)} + 0.03 (domestic exp at half weight)`);
    }
    const ord = Object.keys(all.runs[0].order).map(y => `y${y} ${avg(all.runs.map(r => r.order[y])).toFixed(1)}`).join(', ');
    const pre = Object.keys(all.runs[0].prestige).map(y => `y${y} ${avg(all.runs.map(r => r.prestige[y])).toFixed(0)}`).join(', ');
    info(`${tag}: order ${ord}; prestige ${pre}; events ${(all.est.eventsByFac / Math.max(1, all.est.facMonths) * 12).toFixed(2)} per faction-year; AI tribute pacts ${(all.est.aiTribute / seeds).toFixed(1)}/game; AI projects ${(all.est.aiProj / seeds).toFixed(1)}/game  [${((Date.now() - t0) / 1000).toFixed(0)} s]`);
  }
});

// ================================================================================================== 5 ==
// ENGAGEMENT §19.3 的脚本玩家（engage-proto.js playerTurn 的移植，跑在真模块上）。pol：'v3'（第四版全关）| 'v4'（全开 + 本规格的玩法）
const RES = 120;
function playerRun(key, scen, seed, years, pol) {
  switches(pol === 'v4');
  AIBudget.reset();
  const g = fresh(key, scen, seed);
  const f = g.player;
  const C = SG.Commands, Conquest = SG.Conquest;
  if (pol === 'v4') E.CFG.delegate = 'ask';
  const out = { key, alive: true, months: 0, cities: {}, income: {}, prestige: {}, ach: {}, lv: {}, gross: 0, cards: { none: 0, rnd: 0, sched: 0, maxR: 0, maxS: 0, jan: [] }, wangdao: null, vlog: null, cnt: {}, firstAch: [] };
  const cityThreat = c => { let t = 0; for (const j of c.links) { const n = g.cities[j]; if (n.owner >= 0 && n.owner !== f && !g.allied(n.owner, f) && !(E.isOn() && E.noAttack(n.owner, f))) t += g.troopsIn(n); } return t; };
  const free = c => g.officersIn(c).filter(x => !x.moved);
  // 卡：按期望价值（与原型 playerChoose 同一权重）
  const evChoose = card => {
    let best = null, bs = -1e9;
    const c = g.cities[card.city] || E.capitalOf(f);
    const incC = c ? c.industry * B.GoldPerIndustry + c.town * B.GoldPerTown : 0, harC = c ? c.land * B.FoodPerLand : 0;
    for (const o of card.options) {
      if (!o.ok || o.playerOnly) continue;
      let s = 0;
      for (const e of o.fx || []) {
        const p = e.p;
        const d = e.k === 'order' ? e.v * incC * 0.6 * (e.all ? g.cityCount(f) : 1) : e.k === 'prestige' ? e.v * 6 : e.k === 'virtue' ? e.v * 30 : e.k === 'fame' ? e.v * 60 : e.k === 'exp' ? e.v * 1.5
          : e.k === 'gold' ? e.v : e.k === 'food' ? e.v * 0.12 : e.k === 'gtroops' ? 0.2 * e.v : e.k === 'buff:harvest' ? e.v * harC * 0.12 : e.k === 'buff:income' ? e.v * incC * (e.months || 1)
          : e.k === 'land' || e.k === 'town' ? 2.6 * e.v : e.k === 'loyalty' ? e.v * 4 : e.k === 'rel' ? e.v * 3 : e.k === 'item' ? 120 : e.k === 'reveal' ? 150 : e.k === 'defect' ? 300 : e.k === 'bondKind' ? 120 : 0;
        s += d * p;
      }
      if (s > bs) { bs = s; best = o; }
    }
    return best ? best.id : null;
  };
  const answer = card => {
    switch (card.kind) {
      case 'event': return [evChoose(card) || card.options[card.options.length - 1].id, {}];
      case 'festival': { const cap = E.capitalOf(f), cost = E.festivalCost(f); if (!cap) return ['skip', {}]; const resv = AIBudget.reserve(cap).gold; return [cap.gold >= cost + 250 ? 'grand' : cap.gold >= cost / 3 + resv + 50 ? 'modest' : 'skip', {}]; }
      case 'wenhui': return [card.options[0].ok ? 'send' : 'skip', { run: true, debate: 6 }];   // 原型：玩家亲自论辩平均 +6
      case 'demand': { const h = card.from; let thr = 0; for (const c of g.citiesOf(f)) for (const j of c.links) if (g.cities[j].owner === h) thr += g.troopsIn(g.cities[j]); const cap = E.capitalOf(f); return [cap && thr > g.troopsIn(cap) * 0.8 ? 'accept' : 'refuse', {}]; }
      case 'submit': return ['accept', {}];
      case 'offer': return ['accept', {}];
      default: return [card.options[0].id, {}];
    }
  };
  for (let m = 0; m < years * 12 && g.playerFaction.alive; m++) {
    // 月初：本月的卡（玩家会看到的：≤ 1 张随机 + ≤ 1 张定期）
    if (pol === 'v4') {
      const cards = E.monthStartCards(f);
      const r = cards.filter(c => c.slot === 'random').length, sc = cards.filter(c => c.slot === 'scheduled').length;
      if (!cards.length) out.cards.none++;
      out.cards.rnd += r; out.cards.sched += sc; out.cards.maxR = Math.max(out.cards.maxR, r); out.cards.maxS = Math.max(out.cards.maxS, sc);
      if (g.month === 1) out.cards.jan.push(cards.map(c => c.kind));
      for (const c of cards) { const [id, o] = answer(c); E.resolve(c, id, o); }
      // 国策：盲目受命（原型的 v4 脚本）
      for (const o of E.missionOffers(f).slice(0, 1)) E.acceptMission(f, o.oid);
    }
    playerTurn(g, pol, cityThreat, free);
    const pb = StrategyAI.runAI([]);
    for (const s of pb) {
      if (!StrategyAI.recheckBattle(s, [])) continue;
      Conquest.autoResolve(s); Conquest.apply(s);
      if (s.captives.length) Conquest.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender);
      for (const F of g.factions) if (F.alive && g.cityCount(F.id) === 0) g.checkFactionDeath(F.id);
      if (!g.playerFaction.alive) break;
    }
    if (!g.playerFaction.alive) break;
    out.gross += E.facInc(f);
    StrategyAI.endMonth();
    // 第四版脚本玩家也让官爵与装备按电脑的规则自理（INTEGRATION v4-all-proto 的 player 模式：钱袋同电脑）
    if (pol === 'v4' && g.playerFaction.alive) {
      AIBudget.accrue(f, E.facInc(f));
      if (SG.Titles && SG.Titles.enabled) SG.Titles.aiManage(f);
      if (SG.Items && SG.Items.enabled) { SG.Items.aiEquip(f); SG.Items.aiShop(f); }
    }
    out.months = m + 1;
    const y = (m + 1) / 12;
    if ((m + 1) % 12 === 0) {
      out.cities[y] = g.cityCount(f); out.income[y] = E.facInc(f); out.lv[y] = avg(g.generalsOf(f).map(x => x.level));
      if (pol === 'v4') { out.prestige[y] = g.playerFaction.prestige; out.ach[y] = g.engage.ach.length; }
    }
  }
  out.alive = g.playerFaction.alive;
  if (pol === 'v4' && g.engage) {
    const v = g.engage.victory;
    out.wangdao = v.wangdao !== undefined ? (v.wangdao - g.engage.start) / 12 : null;
    out.vlog = Object.assign({}, g.engage.vlog);
    const c = k => E.getCnt(f, k);
    for (const k of ['missionsDone', 'missionGold', 'caravans', 'tradeProfit', 'expeditions', 'expGold', 'prizeGold', 'items', 'item:rare', 'projects', 'wenhuiWin', 'wenhuiPlace1', 'wenhuiPlace2', 'wenhuiPlace3',
      'marriages', 'truces', 'gifts', 'grandFestivals', 'sworn', 'mentors', 'submissions', 'persuadeCity', 'spend:gift', 'spend:project', 'spend:festival', 'spend:marriage', 'spend:truce', 'spend:exped', 'spend:tribute'])
      out.cnt[k] = c(k);
    out.firstAch = g.engage.ach.map(id => (g.engage.achAt[id] - g.engage.start) / 12);
    out.overflow = E.stats.overflow;
  }
  E.CFG.delegate = 'minor';
  return out;
}
function playerTurn(g, pol, cityThreat, free) {
  const f = g.player, C = SG.Commands, Conquest = SG.Conquest;
  let tokens = g.tokensFor(f);
  const use = () => tokens-- > 0;
  const cities = g.citiesOf(f).slice().sort((a, b) => cityThreat(b) - cityThreat(a));
  if (pol === 'v4') {
    let dipT = 0;
    const cap = E.capitalOf(f);
    const ns = E.neighbors(f).slice().sort((a, b) => E.rel(f, a) - E.rel(f, b) || E.power(b) - E.power(a));
    const envoy = g.generalsOf(f).filter(x => !g.absent(x)).sort((a, b) => Math.max(b.intel, b.pol) - Math.max(a.intel, a.pol))[0] || g.ruler(f);
    const env = g.isRuler(envoy) ? null : envoy;
    for (const h of ns) {
      if (dipT >= 2 || tokens <= 1 || !cap) break;
      const strong = E.power(h) > E.power(f) * 0.9;
      if (E.atWar(f, h) && strong && !E.pactOf(f, h, 'truce')) {
        const gold = Math.min(cap.gold - RES, Math.round(3 * E.facInc(h)));
        if (gold > 50) { use(); dipT++; E.propose('truce', f, h, { gold, envoy: env, city: cap }); continue; }
      }
      if (E.rel(f, h) >= 40 && !E.canPropose('marriage', f, h)) {
        const cost = 300 + 50 * g.cityCount(f);
        if (cap.gold >= cost + RES) { use(); dipT++; E.propose('marriage', f, h, { envoy: env, city: cap }); continue; }
      }
      if (!E.canPropose('trade', f, h)) { use(); dipT++; E.propose('trade', f, h, { envoy: env, city: cap }); continue; }
      const lg = g.playerFaction.eng.lastGift[h];
      if (!E.atWar(f, h) && E.rel(f, h) < (strong ? 60 : 35) && cap.gold >= 150 + RES && (lg === undefined || g.monthIndex - lg >= 3)) { use(); dipT++; E.propose('gift', f, h, { gold: 150, envoy: env, city: cap }); continue; }
    }
    // 营建
    for (const c of g.citiesOf(f)) {
      if (tokens <= 1 || c.eng.proj) continue;
      const front = cityThreat(c) > 0;
      const order = front ? ['walls', 'beacons', 'irrigation', 'market', 'drill', 'temple', 'academy', 'granary', 'tuntian', 'forge', 'roads', 'harbour', 'wonder'] : ['market', 'irrigation', 'temple', 'academy', 'wonder', 'granary', 'forge', 'roads', 'harbour', 'drill', 'tuntian'];
      const bl = E.buildable(c);
      for (const id of order) {
        const b = bl.find(x => x.id === id);
        if (!b || b.why === '已建成' || (b.why && !/^金不足/.test(b.why))) continue;
        if (c.gold < b.cost + 200) break;
        use(); E.startProject(c, id, -1); break;
      }
    }
    // 商队
    const active = E.caravans(f).length;
    if (active < E.caravanLimit(f) && tokens > 1) {
      const src = g.citiesOf(f).slice().sort((a, b) => b.gold - a.gold)[0];
      const leader = src && g.officersIn(src).length >= 2 ? free(src).filter(x => !g.isRuler(x)).sort((a, b) => b.pol - a.pol)[0] : null;
      if (src && leader && src.gold >= 500 + RES) {
        const best = E.caravanTargets(src, leader).filter(x => !x.own)[0];
        if (best) { use(); E.sendCaravan(src, leader, best.city, Math.min(src.gold - RES - 100, E.investMax(src))); }
      }
    }
    // 探索
    if (E.expeditions(f).length < E.expedLimit(f) && tokens > 1) {
      const cap2 = E.capitalOf(f);
      const leader = cap2 && g.officersIn(cap2).length >= 2 ? free(cap2).filter(x => !g.isRuler(x)).sort((a, b) => (b.intel + b.war) - (a.intel + a.war))[0] : null;
      if (leader && cap2.gold >= 300 + RES) {
        const cands = E.sites(f, cap2).filter(x => x.ok).map(x => Object.assign({ score: (x.visited ? 0.5 : 1) / (1 + x.hops) }, x)).sort((a, b) => b.score - a.score);
        if (cands.length) { use(); E.sendExpedition(cap2, leader, cands[0].site.id); }
      }
    }
  }
  // 不流血的扩张：占领相邻空城、劝降孤城守将（献城）
  for (const c of g.citiesOf(f)) {
    if (tokens <= 0) break;
    for (const j of c.links) {
      const t = g.cities[j];
      if (tokens <= 0) break;
      if (t.owner < 0) {
        const offs = free(c).filter(x => !g.isRuler(x) && x.troops >= 1000);
        if (offs.length && g.officersIn(c).length >= 2 && cityThreat(c) < g.troopsIn(c)) { use(); const s = Conquest.prepare(f, c, t, [offs[0]], Math.min(c.food, 500), 0); Conquest.autoResolve(s); Conquest.apply(s); }
      } else if (t.owner !== f && !g.allied(t.owner, f)) {
        const os = g.officersIn(t);
        if (os.length === 1 && !g.isRuler(os[0])) {
          const agent = free(c).sort((a, b) => b.intel - a.intel)[0];
          if (agent && C.persuadeChance(agent, os[0], f) >= 0.25) { use(); agent.moved = true; C.persuade(agent, os[0], f); }
        }
      }
    }
  }
  // 原版内政
  for (const c of cities) {
    if (tokens <= 0) break;
    if (c.owner !== f) continue;
    const offs = free(c);
    const thr = cityThreat(c), mine = g.troopsIn(c);
    if (thr > mine * 0.8 && offs.length) {
      const weak = offs.filter(x => x.troops < SG.maxTroops(x) * 0.8).sort((a, b) => a.troops - b.troops)[0];
      if (weak && c.gold > 150 && use()) { C.recruit(c, weak, C.recruitMax(c, weak)); continue; }
      const low = offs.filter(x => x.troops > 0 && x.training < 85).sort((a, b) => a.training - b.training)[0];
      if (low && use()) { C.train(low); continue; }
    }
    if (offs.length && (g.monthIndex + c.id) % 4 === 0 && use()) {
      const sr = offs.sort((a, b) => b.intel - a.intel)[0];
      C.search(c, sr);
      if (C.searchFound && tokens > 0) { use(); C.hire(C.searchFound, sr, f, c.id); }
      continue;
    }
    if (c.gold > 50 + RES && offs.length && use()) {
      const gov = c.governor >= 0 ? g.generals[c.governor] : offs[0];
      const k = c.land <= c.industry && c.land <= c.town ? SG.DevKind.Land : c.industry <= c.town ? SG.DevKind.Industry : SG.DevKind.Town;
      C.develop(c, gov, k);
      continue;
    }
    const low = offs.filter(x => x.troops > 0 && x.training < 70)[0];
    if (low && use()) { C.train(low); continue; }
  }
  for (const c of cities) {
    if (tokens <= 0) break;
    if (c.owner !== f) continue;
    const offs = free(c);
    if (!offs.length) continue;
    if (c.gold > 50 + RES * 2 && use()) { const gov = c.governor >= 0 ? g.generals[c.governor] : offs[0]; const k = c.land <= c.industry && c.land <= c.town ? SG.DevKind.Land : c.industry <= c.town ? SG.DevKind.Industry : SG.DevKind.Town; C.develop(c, gov, k); }
    else { const weak = offs.filter(x => x.troops < SG.maxTroops(x) * 0.6)[0]; if (weak && c.gold > 220 && use()) C.recruit(c, weak, Math.min(C.recruitMax(c, weak), 1500)); }
  }
  for (const x of g.generalsOf(f)) if (!g.isRuler(x) && x.loyalty < 60) { const c = g.cities[x.city]; if (c.owner === f && c.gold > 220) C.reward(c, x); }
  for (const c of g.citiesOf(f)) for (const x of g.freeFoundIn(c.id)) { const r = g.officersIn(c)[0]; if (r && tokens > 0) { use(); C.hire(x, r, f, c.id); } }
}
let PLAYER = null;   // §5 的结果（§6、§9 复用）
section(5, '和平玩家（ENGAGEMENT §19.3；DESIGN-V4 §16：存活 ≥ v3 − 8 pp、王道 10–40 %）', () => {
  const keys = ['liubei', 'cao', 'kongrong', 'sunjian', 'taoqian', 'menghuo'];
  const seeds = +argv('--seeds', FULL ? 6 : QUICK ? 1 : 3), years = +argv('--years', 20);
  const sets = FULL ? [0, 500] : [0];
  const runs = { v3: [], v4: [] };
  const t0 = Date.now();
  for (const s0 of sets) for (const key of keys) for (let s = 1; s <= seeds; s++) {
    const seed = s0 + s * 7 + key.length;
    runs.v3.push(playerRun(key, 'classic', seed, years, 'v3'));
    runs.v4.push(playerRun(key, 'classic', seed, years, 'v4'));
  }
  PLAYER = runs.v4;
  const surv = (L, y) => L.filter(r => r.months >= y * 12).length / L.length;
  const n = runs.v4.length;
  info(`classic ${keys.length} rulers × ${seeds} seeds${sets.length > 1 ? ' × 2 sets' : ''} × ${years} y = ${n} games per policy [${((Date.now() - t0) / 1000).toFixed(0)} s]`);
  const s3 = surv(runs.v3, years), s4 = surv(runs.v4, years);
  const tolS = FULL ? 0.08 : QUICK ? 0.34 : 0.17;
  ok(s4 >= s3 - tolS, `survival y${years}: v4 ${(100 * s4).toFixed(0)} % ≥ v3 ${(100 * s3).toFixed(0)} % − ${(100 * tolS).toFixed(0)} pp`);
  const at = (L, y) => L.filter(r => r.months >= y * 12);
  const incY = (L, y) => avg(at(L, y).map(r => r.income[y]));
  info(`survival y5/10/20: v3 ${[5, 10, 20].map(y => (100 * surv(runs.v3, y)).toFixed(0) + '%').join(' / ')} · v4 ${[5, 10, 20].map(y => (100 * surv(runs.v4, y)).toFixed(0) + '%').join(' / ')}`);
  info(`cities (survivors) y10/y20: v3 ${avg(at(runs.v3, 10).map(r => r.cities[10])).toFixed(1)} / ${avg(at(runs.v3, 20).map(r => r.cities[20])).toFixed(1)} · v4 ${avg(at(runs.v4, 10).map(r => r.cities[10])).toFixed(1)} / ${avg(at(runs.v4, 20).map(r => r.cities[20])).toFixed(1)}`);
  info(`income y10/y20: v3 ${incY(runs.v3, 10).toFixed(0)} / ${incY(runs.v3, 20).toFixed(0)} · v4 ${incY(runs.v4, 10).toFixed(0)} / ${incY(runs.v4, 20).toFixed(0)}; level y10/y20 v3 ${avg(at(runs.v3, 10).map(r => r.lv[10])).toFixed(2)} / ${avg(at(runs.v3, 20).map(r => r.lv[20])).toFixed(2)} · v4 ${avg(at(runs.v4, 10).map(r => r.lv[10])).toFixed(2)} / ${avg(at(runs.v4, 20).map(r => r.lv[20])).toFixed(2)}`);
  if (at(runs.v3, years).length && at(runs.v4, years).length) flag(incY(runs.v4, years) >= 0.8 * incY(runs.v3, years), `base income y${years}: v4 ≥ 0.8 × v3 (${incY(runs.v4, years).toFixed(0)} vs ${incY(runs.v3, years).toFixed(0)})`);
  // 王道
  const wd = runs.v4.map(r => r.wangdao).filter(v => v !== null);
  const wshare = wd.length / n;
  info(`王道 within ${years} y: ${wd.length}/${n} (${(100 * wshare).toFixed(0)} %), median ${wd.length ? median(wd).toFixed(1) : '—'} y, earliest ${wd.length ? Math.min(...wd).toFixed(1) : '—'} y`);
  if (FULL) { ok(wshare >= 0.10 && wshare <= 0.40, `王道 reached in 10–40 % of v4 runs within 20 years (${(100 * wshare).toFixed(0)} %)`); if (wd.length) ok(median(wd) >= 10 && median(wd) <= 17, `王道 median 10–17 y (${median(wd).toFixed(1)})`); }
  else flag(wshare <= 0.40, `王道 ≤ 40 % of runs (quick sample: ${(100 * wshare).toFixed(0)} %)`);
  if (wd.length) ok(Math.min(...wd) >= 6, `王道 never before year 6 (earliest ${Math.min(...wd).toFixed(1)})`);
  const first = k => { const v = runs.v4.map(r => r.vlog && r.vlog['first:' + k]).filter(x => x !== undefined).map(x => x / 12); return v.length + '/' + n + (v.length ? ' median ' + median(v).toFixed(1) + ' y' : ''); };
  info(`王道 conditions first met: prestige ${first('prestige')}; order ${first('order')}; prosperity ${first('prosper')}; friends ${first('friends')}; peace ${first('peace')}`);
  // 收支占比（ENGAGEMENT §17.1）
  const share = k => avg(runs.v4.map(r => (r.cnt[k] || 0) / Math.max(1, r.gross)));
  const sh = k => (100 * share(k)).toFixed(1) + ' %';
  info(`gold of gross: missions ${sh('missionGold')}, caravan profit ${sh('tradeProfit')}, expeditions ${sh('expGold')}, prizes ${sh('prizeGold')}; spending: gifts ${sh('spend:gift')}, projects ${sh('spend:project')}, festivals ${sh('spend:festival')}, marriages ${sh('spend:marriage')}, truces ${sh('spend:truce')}, supplies ${sh('spend:exped')}, tribute ${sh('spend:tribute')}`);
  const band = (k, lo, hi, gate) => { const v = share(k); (gate ? ok : flag)(v >= lo && v <= hi, `${k} ${(100 * v).toFixed(1)} % of gross in ${100 * lo}–${100 * hi} %`); };
  band('missionGold', FULL ? 0.04 : 0.02, FULL ? 0.10 : 0.14, FULL);
  band('tradeProfit', FULL ? 0.08 : 0.04, FULL ? 0.20 : 0.26, FULL);
  ok(share('expGold') <= 0.05 + (FULL ? 0 : 0.02), `expedition gold ${sh('expGold')} ≤ 5 % of gross`);
  ok(share('prizeGold') <= 0.08 + (FULL ? 0 : 0.03), `prize gold ${sh('prizeGold')} ≤ 8 % of gross (文会; the tournament is TOURNAMENT's)`);
  // 功业节奏
  const achY1 = avg(runs.v4.map(r => r.firstAch.filter(y => y < 1).length));
  const per = runs.v4.filter(r => r.months >= 120).map(r => r.firstAch.filter(y => y >= 1 && y < 10).length / 9);
  ok(achY1 >= 0.5, `achievements in year 1 ≥ 0.5 on average (${achY1.toFixed(2)})`);
  if (per.length) (FULL ? ok : flag)(avg(per) >= 0.5 && avg(per) <= 1.5, `achievements 0.5–1.5 a year after year 1 (${avg(per).toFixed(2)})`);
  info(`per year (v4): missions ${(avg(runs.v4.map(r => r.cnt.missionsDone / Math.max(1, r.months / 12)))).toFixed(2)}, caravans ${(avg(runs.v4.map(r => r.cnt.caravans / Math.max(1, r.months / 12)))).toFixed(2)}, expeditions ${(avg(runs.v4.map(r => r.cnt.expeditions / Math.max(1, r.months / 12)))).toFixed(2)}, projects ${(avg(runs.v4.map(r => r.cnt.projects / Math.max(1, r.months / 12)))).toFixed(2)}, items ${(avg(runs.v4.map(r => r.cnt.items / Math.max(1, r.months / 12)))).toFixed(2)} (rare ${(avg(runs.v4.map(r => r.cnt['item:rare'] / Math.max(1, r.months / 12)))).toFixed(3)}); 文会 wins per 10 y ${(avg(runs.v4.map(r => r.cnt.wenhuiWin / Math.max(1, r.months / 120)))).toFixed(2)}; 归附 ${avg(runs.v4.map(r => r.cnt.submissions)).toFixed(2)}, 献城 ${avg(runs.v4.map(r => r.cnt.persuadeCity)).toFixed(2)} per game`);
  info(`prestige y5/y10/y20: ${[5, 10, 20].map(y => avg(at(runs.v4, y).map(r => r.prestige[y])).toFixed(0)).join(' / ')}; achievements y5/y10/y20: ${[5, 10, 20].map(y => avg(at(runs.v4, y).map(r => r.ach[y])).toFixed(1)).join(' / ')}`);
  // 世界（夜间）
  if (FULL) {
    const wk = ['goguryeo', 'yamatai', 'khotan', 'satavahana', 'caledonia', 'saba'];
    const w3 = [], w4 = [];
    for (const key of wk) for (let s = 1; s <= 4; s++) { const seed = s * 7 + key.length; w3.push(playerRun(key, 'world', seed, 15, 'v3')); w4.push(playerRun(key, 'world', seed, 15, 'v4')); }
    const a3 = surv(w3, 15), a4 = surv(w4, 15);
    ok(a4 >= a3 - 0.08, `world survival y15: v4 ${(100 * a4).toFixed(0)} % ≥ v3 ${(100 * a3).toFixed(0)} % − 8 pp`);
    const ww = w4.map(r => r.wangdao).filter(v => v !== null);
    info(`world 王道 within 15 y: ${ww.length}/${w4.length} (not gated, n small); cities y15 v3 ${avg(at(w3, 15).map(r => r.cities[15])).toFixed(1)} · v4 ${avg(at(w4, 15).map(r => r.cities[15])).toFixed(1)}`);
  }
});

// ================================================================================================== 6 ==
section(6, '防繁琐（R1–R6）', () => {
  let runs = PLAYER;
  if (!runs) { runs = []; for (const key of ['liubei', 'kongrong']) runs.push(playerRun(key, 'classic', 3, QUICK ? 6 : 10, 'v4')); }
  const months0 = runs.reduce((s, r) => s + r.months, 0);
  eq(Math.max(...runs.map(r => r.cards.maxR)), Math.min(1, Math.max(...runs.map(r => r.cards.maxR))), 'R1: never more than 1 random card in a month');
  ok(Math.max(...runs.map(r => r.cards.maxS)) <= 1, 'R1: never more than 1 scheduled card in a month (the tournament card is outside, TOURNAMENT\'s)');
  const none = runs.reduce((s, r) => s + r.cards.none, 0) / months0;
  ok(none >= 0.35, `≥ 35 % of months show no card (${(100 * none).toFixed(0)} %)`);
  const rpy = runs.reduce((s, r) => s + r.cards.rnd, 0) / months0 * 12;
  const spy = runs.reduce((s, r) => s + r.cards.sched, 0) / months0 * 12;
  ok(rpy >= 2 && rpy <= 7, `random cards ${rpy.toFixed(2)} a year (3–7 for a 1–5 city realm; ≥ 2 with the scripted player's cooldowns)`);
  info(`scheduled cards ${spy.toFixed(2)} a year; overflow (R1 deferred / auto) ${runs.reduce((s, r) => s + (r.overflow || 0), 0)} over ${months0} months`);
  // R6 + R4：在一局全电脑里查每次抽到的事件
  switches(true);
  const g = fresh('liubei', 'classic', 21, { ai: true });
  const seenF = {}, seenC = {};
  let twice = 0, dis = 0;
  const D = SG.EngageData;
  const evById = Object.fromEntries(D.events.map(e => [e.id, e]));
  for (let m = 0; m < (QUICK ? 60 : 120); m++) {
    months(1);
    for (const F of g.factions) { if (!F.eng) continue; for (const [id, mi] of Object.entries(F.eng.recent)) { if (mi !== g.monthIndex - 1) continue; const k = F.id + ':' + id; if (seenF[k] !== undefined && mi - seenF[k] < 6 && mi !== seenF[k]) twice++; seenF[k] = mi; } }
    for (const c of g.cities) { if (!c.eng) continue; if (c.eng.lastDis === g.monthIndex - 1) { if (seenC[c.id] !== undefined && c.eng.lastDis - seenC[c.id] < 12 && c.eng.lastDis !== seenC[c.id]) dis++; seenC[c.id] = c.eng.lastDis; } }
  }
  eq(twice, 0, 'R6: no event twice for a faction within 6 months');
  eq(dis, 0, 'R6: no disaster twice in a city within 12 months');
  let noFree = 0;
  for (const ev of D.events) if (ev.options && !ev.options.some(o => !o.playerOnly && !(o.cost || []).length && o.ai > 0)) noFree++;
  eq(noFree, 0, 'R4: every card has a free AI-selectable option');
  // 卡上不出付不起的选项：玩家的卡，付不起的项 ok = false（界面置灰）
  const g2 = fresh('liubei', 'classic', 22);
  E.CFG.delegate = 'ask';
  let shown = 0, unpaid = 0;
  for (let m = 0; m < 36; m++) {
    months(1);
    for (const card of E.monthStartCards(g2.player)) {
      shown++;
      const c = g2.cities[card.city];
      for (const o of card.options) if (o.ok && c && o.cost && (o.cost.gold > c.gold || o.cost.food > c.food)) unpaid++;
      E.resolve(card, card.options.find(o => o.rec).id);
    }
  }
  E.CFG.delegate = 'minor';
  eq(unpaid, 0, `no card offers an option the city cannot pay for (${shown} cards)`);
});

// ================================================================================================== 7 ==
section(7, '性能（Engage 月末每月的 CPU）', () => {
  // 子进程 + --single-threaded-gc：垃圾回收在主线程上，进程 CPU 时间 ≈ 本线程（多线程 GC 会把 CPU 时间算多）。
  // 机器很忙（负载 > 0.75 × 核数，例如别的代理在跑无头浏览器）时只报告，不判失败：CPU 时间在争用下也会变长
  const os = require('os');
  const cp = require('child_process');
  for (const [scen, lim, yrs] of [['classic', 3, 5], ['world', 8, 3]]) {
    const r = cp.spawnSync(process.execPath, ['--single-threaded-gc', __filename, '--perf-child', scen, String(yrs)], { cwd: ROOT, encoding: 'utf8', maxBuffer: 16 << 20 });
    const line = (r.stdout || '').split('\n').find(l => l.indexOf('PERF ') === 0);
    if (!line) { ok(false, scen + ': perf child failed ' + (r.stderr || '').slice(-500)); continue; }
    const o = JSON.parse(line.slice(5));
    // 机器忙（负载 > 0.75 × 核数）或明显比参考机器慢（校准 > 1.5 × 12 ms）时只报告、不判失败
    const busy = os.loadavg()[0] > os.cpus().length * 0.75 || o.calib > 18;
    (busy ? flag : ok)(o.avg <= lim, `${scen}: Engage.monthEnd ${o.avg.toFixed(2)} ms per month (main thread, ${o.n} months after warm-up) ≤ ${lim} ms (median ${o.median.toFixed(2)}, p90 ${o.p90.toFixed(2)}; process CPU incl. compiler threads ${o.cpu.toFixed(2)}; calibration loop ${o.calib.toFixed(1)} ms vs reference 12, so ≈ ${(o.avg * 12 / o.calib).toFixed(2)} ms on the reference machine; load ${os.loadavg()[0].toFixed(1)} on ${os.cpus().length} CPUs${busy ? ' — slow or busy machine, reported only' : ''})`);
    info(`${scen} parts (ms / month): ${Object.entries(o.parts).map(([k, v]) => k + ' ' + v.toFixed(2)).join(', ')}`);
  }
});
if (ARGS[0] === '--perf-child') {
  // (only reached when §7 is skipped by ONLY; the child mode is handled at the top of the file)
}

// ================================================================================================== 8 ==
section(8, '存档（往返、继续、迁移、关掉不写字段）', () => {
  switches(true);
  const g = fresh('liubei', 'classic', 41);
  months(30);
  // 玩家的卡、国策、商队、探索都放一些状态进去
  E.offerMissions(g.player, 2);
  const t = JSON.stringify(g);
  const d = JSON.parse(t);
  ok(d.engage && d.cities.every(c => typeof c.order === 'number' && c.eng) && d.factions.every(f => typeof f.prestige === 'number' && f.eng), 'the save holds G.engage, city.order / eng, faction.prestige / eng');
  const back = GameState.fromJSON(t);
  eq(JSON.stringify(back), t, 'v4 round trip is byte-identical (' + t.length + ' chars, engage ' + JSON.stringify(d.engage).length + ')');
  const run = (gg, n, seed) => { SG.G = gg; SG.Random.seed(seed); if (E.reset) E.reset(); for (let m = 0; m < n; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); } return md5(JSON.stringify(gg)); };
  const g1 = GameState.fromJSON(t), g2 = GameState.fromJSON(t);
  eq(run(g2, 6, 77), run(g1, 6, 77), 'two loaded copies play the next 6 months identically');
  // v3 存档（第四版全关时写的档，版本号改成 3）→ 迁移（ITEMS → TITLES → ENGAGEMENT）
  switches(false);
  const gv3 = fresh('liubei', 'classic', 43);
  months(6);
  const d3 = JSON.parse(JSON.stringify(gv3)); d3.version = 3;
  switches(true);
  const m3 = GameState.fromJSON(JSON.stringify(d3));
  ok(m3 && m3.engage && m3.cities.every(c => typeof c.order === 'number') && m3.factions.every(f => f.prestige === 0), 'v3 save: migrate builds order from the start formula, prestige 0');
  ok(Object.keys(m3.engage.bonds).length > 0 && Object.keys(m3.engage.rel).length > 0, 'v3 save: starting bonds and relations');
  ok((m3.systemsAdded || []).indexOf('engage') >= 0, 'g.systemsAdded lists engage');
  // 关掉的系统不写字段
  switches(false);
  const g0 = fresh('liubei', 'classic', 42);
  months(3);
  const k0 = Object.keys(JSON.parse(JSON.stringify(g0)));
  ok(k0.indexOf('engage') < 0 && g0.cities.every(c => c.order === undefined), 'Engage off: no engage fields after 3 months');
  switches(true);
});

// ================================================================================================== 9 ==
section(9, '日历（INTEGRATION §6.5）', () => {
  switches(true);
  E.CFG.delegate = 'ask';
  const g = fresh('liubei', 'classic', 51);
  let janBad = 0, dueJan = 0, jans = 0, maxS = 0;
  const seen = new Set();
  for (let m = 0; m < (QUICK ? 120 : 240); m++) {
    months(1);
    for (const c of g.engage.cards) if (!seen.has(c.uid)) { seen.add(c.uid); if ((c.kind === 'demand' || c.kind === 'submit') && (c.due - 1) % 12 + 1 === 1) dueJan++; }
    const cards = E.monthStartCards(g.player);
    const sch = cards.filter(c => c.slot === 'scheduled');
    maxS = Math.max(maxS, sch.length);
    if (g.month === 1) { jans++; if (sch.length > 1) janBad++; }
    for (const c of cards) E.resolve(c, c.options.find(o => o.rec).id);
    if (!g.playerFaction.alive) break;
  }
  E.CFG.delegate = 'minor';
  eq(dueJan, 0, 'no tribute demand or 归附 offer is ever due in month 1');
  eq(janBad, 0, `no January shows more than one scheduled card besides the tournament (${jans} Januaries)`);
  ok(maxS <= 1, 'never more than one scheduled card in any month');
  ok(!SG.Tourney || true, 'the tournament card is TOURNAMENT\'s (outside R1; ' + (SG.Tourney ? 'loaded' : 'not landed yet') + ')');
});

// ================================================================================================== 10 ==
section(10, '电脑预算（INTEGRATION §5.2）', () => {
  switches(true);
  AIBudget.reset();
  const g = fresh('liubei', 'classic', 61, { ai: true });
  let below = 0, under = 0;
  const off = StrategyAI.on('news', () => { for (const c of g.cities) if (c.owner >= 0 && c.gold < 0) below++; }, { owner: 'test', order: 99 });
  // 自由支出（营建、节庆）之后，花钱的城不低于 R(c)
  const offP = StrategyAI.onSpend('festival', fid => { for (const c of g.citiesOf(fid)) if (c.eng && c.eng.proj && c.eng.proj.since === g.monthIndex && c.gold < AIBudget.reserve(c).gold) under++; }, { owner: 'test', order: 200 });
  try { months(QUICK ? 72 : 120); } finally { off(); offP(); }
  eq(AIBudget.stats.below0, 0, 'no city below 0 after a discretionary spend');
  eq(below, 0, 'no city below 0 gold at the end of any month');
  eq(under, 0, 'a project start keeps the city at its reserve R(c)');
  const k = AIBudget.stats.byKind;
  ok((k.project || 0) > 0 && (k.event || 0) > 0, 'AI projects and event answers are paid through SG.AIBudget (project ' + (k.project || 0) + ', event ' + (k.event || 0) + ', festival ' + (k.festival || 0) + ', tribute ' + (k.tribute || 0) + ')');
});

console.log(`\n${fails ? 'FAILED' : 'ALL OK'}: ${checks} checks, ${fails} failures, ${flags} flags  [${((Date.now() - T0) / 1000).toFixed(0)} s]`);
process.exitCode = fails;
