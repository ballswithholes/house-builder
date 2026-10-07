'use strict';
/* ==========================================================================
   世界剧本「天下大势」检测（Node，无依赖）
   (1) 数据自检：格式、计数、经典剧本原样包含、连线（全图连通、海路是子集、度数）、势力 / 君主 / 都城、
       武将（姓名唯一、隐士忠诚与兵力为 0、无人卒于 190 年前）、资料表（WorldInfo / FactionInfo / CityInfo）、
       城池间距（SG.project ≥ 3）、势力颜色（CIEDE2000 色差）、显示名长度。
   (2) 全电脑模拟：12 个种子 × 25 年（可用参数改）。统计每秒月数、单月最长耗时、各地域势力存活、
       首个势力超过全部城池 25% 的时间、地域在 3 年内全灭、罗马 / 安息 / 贵霜的城数走势。
   model.js / strategy-ai.js 在载入时读取 SG.ScenarioData，故先载入 world-data.js，
   令 SG.ScenarioData = SG.Scenarios.world，再载入二者。
   (3) 经典剧本不受影响：同一种子下，载入与不载入 world-data.js 的经典剧本 5 年全电脑对局 JSON 完全相同。
   用法：node tests/world.js [种子] [年数=25] [种子起点=0]        退出码 = 失败数
     种子：个数 N（跑 起点+1 … 起点+N），或区间列表如 1-12,101-124,1001-1024。
     默认 1-12,101-124,1001-1024（60 个种子；平衡数据按这三段调过）。留出验证：node tests/world.js 5001-5024
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

// 子进程模式：打印经典剧本 5 年全电脑对局的 JSON 摘要（plain = 不载入 world-data.js）
if (process.argv[2] === '--classic-hash') {
  global.window = global;
  const dir = path.join(__dirname, '..', 'js');
  const withWorld = process.argv[3] !== 'plain';
  for (const f of ['core.js'].concat(withWorld ? ['world-geo.js'] : [], ['data.js'], withWorld ? ['world-data.js'] : [], ['model.js', 'strategy-ai.js'])) {
    vm.runInThisContext(fs.readFileSync(path.join(dir, f), 'utf8'), { filename: f });
  }
  const S = global.SG;
  S.Random.seed(7);
  const g = S.G = S.GameState.newGame('cao'); g.player = -1;
  for (let m = 0; m < 60; m++) { S.StrategyAI.runAI([]); S.StrategyAI.endMonth(); }
  const t = JSON.stringify(g);
  console.log(require('crypto').createHash('md5').update(t).digest('hex') + ' ' + t.length);
  process.exit(0);
}

global.window = global;
const root = path.join(__dirname, '..', 'js');
const load = f => vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) load(f);
const SG = global.SG;
const CLASSIC = SG.ScenarioData;
const W = SG.Scenarios.world;
SG.ScenarioData = W;
for (const f of ['model.js', 'strategy-ai.js']) load(f);
const { GameState, StrategyAI } = SG;

// 种子表：区间列表（1-12,101-124）或个数 + 起点
const SEED_LIST = (() => {
  const a = process.argv[2] || '1-12,101-124,1001-1024';
  if (/[-,]/.test(a)) {
    const out = [];
    for (const part of a.split(',')) { const [lo, hi] = part.split('-').map(Number); for (let s = lo; s <= (hi || lo); s++) out.push(s); }
    return out;
  }
  const n = parseInt(a, 10) || 12, base = parseInt(process.argv[4], 10) || 0;
  return Array.from({ length: n }, (_, i) => base + i + 1);
})();
const SEEDS = SEED_LIST.length;
const YEARS = parseInt(process.argv[3], 10) || 25;
let fails = 0;
function check(cond, msg) { if (!cond) { fails++; console.log('  FAIL ' + msg); } return cond; }
const t0 = Date.now();

// =============================================================== (1) 数据 --
console.log('== (1) world scenario data ==');
const CULTURES = ['han', 'nanman', 'wa', 'yi', 'korea', 'steppe', 'seasia', 'tarim', 'kushan', 'persia', 'arab', 'roman', 'celt', 'german', 'sarmatian'];
const C = W.Cities.map(l => l.split('|'));
const F = W.Factions.map(l => l.split('|'));
const G = W.Generals.map(l => l.split('|'));
const nC = CLASSIC.Cities.length, nF = CLASSIC.Factions.length, nG = CLASSIC.Generals.length;
console.log(`cities ${C.length} (classic ${nC} + new ${C.length - nC}), links ${W.Links.length} (sea ${W.SeaLinks.length}), factions ${F.length} (+${F.length - nF}), generals ${G.length} (+${G.length - nG}; hidden ${G.filter(p => p[8] === '1').length}), regions ${W.Regions.length}`);
check(W.StartYear === 190 && W.StartMonth === 1, 'start 190-1');
check(/你将选择一位君主……$/.test(W.Intro), 'Intro ends with 你将选择一位君主……');

// 经典部分原样包含
for (let i = 0; i < nC; i++) check(W.Cities[i].startsWith(CLASSIC.Cities[i] + '|'), 'classic city unchanged ' + CLASSIC.Cities[i]);
for (let i = 0; i < nF; i++) check(W.Factions[i].startsWith(CLASSIC.Factions[i] + '|'), 'classic faction unchanged ' + CLASSIC.Factions[i]);
for (let i = 0; i < nG; i++) check(W.Generals[i].startsWith(CLASSIC.Generals[i] + '|'), 'classic general unchanged ' + CLASSIC.Generals[i]);
for (const l of CLASSIC.Links) check(W.Links.includes(l), 'classic link kept ' + l);

// 城池
const cityKeys = new Set();
const cityNames = new Set();
for (const p of C) {
  check(p.length === 11, 'city has 11 columns: ' + p.join('|'));
  check(!cityKeys.has(p[0]), 'unique city key ' + p[0]); cityKeys.add(p[0]);
  check(!cityNames.has(p[1]), 'unique city name ' + p[1]); cityNames.add(p[1]);
  for (let i = 2; i <= 9; i++) check(Number.isFinite(parseFloat(p[i])), 'numeric city field ' + p[0] + '[' + i + ']');
  check(CULTURES.includes(p[10]), 'city culture ' + p[0] + ' ' + p[10]);
  const [land, ind, town] = [+p[4], +p[5], +p[6]];
  check(land <= 999 && ind <= 999 && town <= 999 && land > 0 && ind > 0 && town > 0, 'city stats in range ' + p[0]);
}
// 连线
const adj = {}; for (const k of cityKeys) adj[k] = [];
const linkSet = new Set();
for (const l of W.Links) {
  const [a, b] = l.split('-');
  check(cityKeys.has(a) && cityKeys.has(b), 'link resolves ' + l);
  check(a !== b, 'no self link ' + l);
  const id = [a, b].sort().join('-');
  check(!linkSet.has(id), 'no duplicate link ' + l); linkSet.add(id);
  if (adj[a] && adj[b]) { adj[a].push(b); adj[b].push(a); }
}
for (const l of W.SeaLinks) check(W.Links.includes(l), 'sea link is in Links ' + l);
{
  const seen = new Set([C[0][0]]); const q = [C[0][0]];
  while (q.length) { const x = q.pop(); for (const y of adj[x]) if (!seen.has(y)) { seen.add(y); q.push(y); } }
  check(seen.size === cityKeys.size, 'graph connected (' + seen.size + '/' + cityKeys.size + ')');
  const degs = [...cityKeys].map(k => adj[k].length);
  const hist = {}; for (const d of degs) hist[d] = (hist[d] || 0) + 1;
  const in25 = degs.filter(d => d >= 2 && d <= 5).length;
  console.log(`degree histogram ${JSON.stringify(hist)}; ${in25}/${degs.length} in 2..5; max ${Math.max(...degs)} (${[...cityKeys].filter(k => adj[k].length >= 6).join(',')})`);
  check(Math.max(...degs) <= 6, 'max degree ≤ 6');
  check(in25 / degs.length >= 0.85, 'degree mostly 2..5');
}
// 势力
const facKeys = new Set();
const regionIds = new Set(W.Regions.map(r => r.id));
const inRegion = {};
for (const r of W.Regions) for (const k of r.factions) { check(!inRegion[k], 'faction in one region ' + k); inRegion[k] = r.id; }
const genByName = {};
for (const p of G) genByName[p[0]] = p;
for (const p of F) {
  check(p.length === 8, 'faction has 8 columns: ' + p.join('|'));
  check(!facKeys.has(p[0]), 'unique faction key ' + p[0]); facKeys.add(p[0]);
  check(/^#[0-9a-f]{6}$/i.test(p[3]), 'faction colour ' + p[0]);
  check(CULTURES.includes(p[6]), 'faction culture ' + p[0]);
  check(regionIds.has(p[7]) && inRegion[p[0]] === p[7], 'faction region ' + p[0] + ' ' + p[7]);
  const r = genByName[p[2]];
  if (check(r && r[4] === p[0], 'ruler ' + p[2] + ' is a general of ' + p[0])) {
    const fi = SG.FactionInfo[p[0]];
    if (fi) check(fi.capital === r[5] && fi.ruler === p[2], 'ruler ' + p[2] + ' sits in capital ' + fi.capital);
  }
}
for (const r of W.Regions) check(r.factions.length > 0 && r.name, 'region ' + r.id);
check(Object.keys(inRegion).length === F.length, 'every faction has a region');
// 武将
const names = new Set();
const born = {};
for (const p of G) {
  check(p.length === 11, 'general has 11 columns: ' + p.join('|'));
  check(!names.has(p[0]), 'unique general name ' + p[0]); names.add(p[0]);
  check(p[4] === '-' || facKeys.has(p[4]), 'general faction ' + p[0] + ' ' + p[4]);
  check(cityKeys.has(p[5]), 'general city ' + p[0] + ' ' + p[5]);
  check(CULTURES.includes(p[9]), 'general culture ' + p[0] + ' ' + p[9]);
  for (let i = 1; i <= 3; i++) check(+p[i] >= 1 && +p[i] <= 100, 'stat range ' + p[0]);
  if (p[8] === '1') check(p[4] === '-' && p[6] === '0' && p[7] === '0', 'hidden general loyalty 0 troops 0 ' + p[0]);
  else check(p[4] !== '-', 'non-hidden general belongs to a faction ' + p[0]);
  if (p[10]) { born[p[0]] = +p[10]; }
}
const newGens = G.slice(nG);
for (const p of newGens) {
  const wi = SG.WorldInfo[p[0]];
  if (!check(wi, 'WorldInfo for ' + p[0])) continue;
  for (const k of ['full', 'orig', 'role', 'note', 'born', 'died', 'attested', 'culture', 'sex']) check(k in wi, 'WorldInfo.' + k + ' ' + p[0]);
  check(wi.sex === 'm' || wi.sex === 'f', 'WorldInfo.sex m/f ' + p[0]);
  check(wi.died == null || wi.died >= 190, 'nobody died before 190: ' + p[0] + ' ' + wi.died);
  check(wi.culture === p[9], 'WorldInfo culture matches ' + p[0]);
  if (p[8] !== '1' && p[4] !== '-') check(+p[7] >= 0 && +p[7] <= 6000, 'troops within the classic range 0..6000 ' + p[0]);
  check(wi.born == null || wi.born <= 205, 'born ≤ 205 ' + p[0]);
  check((wi.born == null ? '' : String(wi.born)) === p[10], 'born column matches WorldInfo ' + p[0]);
}
for (const p of F.slice(nF)) check(SG.FactionInfo[p[0]] && SG.FactionInfo[p[0]].name === p[1] && SG.FactionInfo[p[0]].ruler === p[2], 'FactionInfo name / ruler match ' + p[0]);
// 性别：四位女性；sexOf 覆盖经典武将（祝融）
{
  const fem = newGens.filter(p => SG.WorldInfo[p[0]] && SG.WorldInfo[p[0]].sex === 'f').map(p => p[0]).sort();
  check(fem.join() === ['卑弥呼', '多姆娜', '玛伊莎', '玛琪亚'].sort().join(), 'female generals ' + fem.join());
  check(SG.WorldData.sexOf('玛琪亚') === 'f' && SG.WorldData.sexOf('祝融') === 'f' && SG.WorldData.sexOf('曹操') === 'm' && SG.WorldData.sexOf('塞维鲁') === 'm', 'SG.WorldData.sexOf');
  check(SG.WorldData.bornOf('曹操') === 155 && SG.WorldData.bornOf('塞维鲁') === 145 && SG.WorldData.bornOf('张飞') === null, 'SG.WorldData.bornOf');
}
// 给玩家看的文字：不得残留研究 / 合并阶段的说明、内部键名或 ASCII 引号；【游戏取舍】只放在 liberty 字段
{
  const META = /合并时|去重|研究摘要|JSON|data\.js|DESIGN|notes?\b|本区|中国区|【游戏取舍】|"/;
  const allKeys = new Set([...cityKeys, ...F.map(p => p[0])]);
  let bad = [];
  for (const [tab, T] of [['WorldInfo', SG.WorldInfo], ['FactionInfo', SG.FactionInfo], ['CityInfo', SG.CityInfo]]) {
    for (const k in T) for (const f of ['full', 'role', 'note', 'liberty', 'modern']) {
      const t = T[k][f]; if (typeof t !== 'string') continue;
      if (META.test(t) || (t.match(/\b[a-z_]{4,}\b/g) || []).some(w => allKeys.has(w))) bad.push(tab + '.' + k + '.' + f);
    }
  }
  check(bad.length === 0, 'player-facing text has no research / merge leftovers: ' + bad.join(', '));
  // 武将详情与选君主卡片里的“介绍”（note）只写史事：出处、译名说明、数值理由、规则取舍一律放进 liberty（「游戏取舍」）
  const NOTE_META = /维基|文献作|英文文献|未找到|译名|自拟|数值|三维|按规则|放宽|游戏安排|游戏中|设为空城|在任武将|为凑成|校准|[武智政德望]极?[高低]|忠诚(?:偏|设)?低|故(?:武力|智力|政治|政略|智谋)/;
  const noteBad = [];
  for (const [tab, T] of [['WorldInfo', SG.WorldInfo], ['FactionInfo', SG.FactionInfo]]) {
    for (const k in T) if (NOTE_META.test(T[k].note || '')) noteBad.push(tab + '.' + k + '「' + NOTE_META.exec(T[k].note)[0] + '」');
  }
  check(noteBad.length === 0, 'history notes carry no source / stat / rule remarks: ' + noteBad.join(', '));
  let rare = [];
  for (const T of [SG.WorldInfo, SG.FactionInfo]) for (const k in T) for (const f of ['full', 'role', 'note', 'liberty']) if (/[\u{20000}-\u{3FFFF}]/u.test(T[k][f] || '')) rare.push(k + '.' + f);
  check(rare.length === 0, 'no CJK Extension B+ characters (most system fonts lack them): ' + rare.join(', '));
  let dash = [];
  for (const T of [SG.WorldInfo, SG.FactionInfo]) for (const k in T) for (const f of ['full', 'role', 'note', 'liberty']) if (/–/.test(T[k][f] || '')) dash.push(k + '.' + f);
  check(dash.length === 0, 'date ranges use 「—」, not 「–」: ' + dash.join(', '));
}
for (const p of C.slice(nC)) check(SG.CityInfo[p[0]], 'CityInfo ' + p[0]);
for (const n in SG.GeneralBorn) check(genByName[n] && genByName[n][10] === String(SG.GeneralBorn[n]), 'GeneralBorn ' + n);
for (const n of ['孟获', '祝融', '孟优', '兀突骨', '带来洞主', '沙摩柯']) check(genByName[n][9] === 'nanman', 'nanman ' + n);
check(SG.WorldData.cultureOfGeneral('卑弥呼') === 'wa' && SG.WorldData.regionOfFaction('roma') === 'roma', 'SG.WorldData lookups');
// 统计值与经典剧本同一尺度
{
  const avg = (rows, i) => rows.reduce((s, p) => s + +p[i], 0) / rows.length;
  const cg = G.slice(0, nG), ng = newGens;
  console.log(`general stat means  classic war ${avg(cg, 1).toFixed(0)} int ${avg(cg, 2).toFixed(0)} pol ${avg(cg, 3).toFixed(0)}  |  new war ${avg(ng, 1).toFixed(0)} int ${avg(ng, 2).toFixed(0)} pol ${avg(ng, 3).toFixed(0)}`);
  const cc = C.slice(0, nC), nc = C.slice(nC);
  console.log(`city means  classic land ${avg(cc, 4).toFixed(0)} ind ${avg(cc, 5).toFixed(0)} town ${avg(cc, 6).toFixed(0)} pop ${(avg(cc, 7) / 1e4).toFixed(0)}万 gold ${avg(cc, 8).toFixed(0)} food ${avg(cc, 9).toFixed(0)}  |  new land ${avg(nc, 4).toFixed(0)} ind ${avg(nc, 5).toFixed(0)} town ${avg(nc, 6).toFixed(0)} pop ${(avg(nc, 7) / 1e4).toFixed(0)}万 gold ${avg(nc, 8).toFixed(0)} food ${avg(nc, 9).toFixed(0)}`);
  check(Math.abs(avg(cg, 1) - avg(ng, 1)) < 8 && Math.abs(avg(cg, 2) - avg(ng, 2)) < 8 && Math.abs(avg(cg, 3) - avg(ng, 3)) < 8, 'new general stats on the classic scale');
}
// 间距
{
  let min = 1e9, pair = '';
  const P = C.map(p => ({ k: p[1], q: SG.project(+p[2], +p[3]) }));
  for (let i = 0; i < P.length; i++) for (let j = i + 1; j < P.length; j++) {
    const d = Math.hypot(P[i].q.x - P[j].q.x, P[i].q.y - P[j].q.y);
    if (d < min) { min = d; pair = P[i].k + '–' + P[j].k; }
    if (i >= nC || j >= nC) check(d >= 3, `cities ≥ 3 apart: ${P[i].k}–${P[j].k} ${d.toFixed(2)}`);
  }
  console.log(`closest cities ${pair} ${min.toFixed(2)} map units`);
}
// 颜色（CIEDE2000）
function lab(h) {
  const lin = c => { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
  const r = lin(parseInt(h.slice(1, 3), 16)), g = lin(parseInt(h.slice(3, 5), 16)), b = lin(parseInt(h.slice(5, 7), 16));
  const f = t => t > 0.008856 ? Math.cbrt(t) : 7.787 * t + 16 / 116;
  const x = f((r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047), y = f(r * 0.2126 + g * 0.7152 + b * 0.0722), z = f((r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883);
  return [116 * y - 16, 500 * (x - y), 200 * (y - z)];
}
function de2000([L1, a1, b1], [L2, a2, b2]) {
  const rad = Math.PI / 180, p7 = v => Math.pow(v, 7);
  const Cb = (Math.hypot(a1, b1) + Math.hypot(a2, b2)) / 2, Gk = 0.5 * (1 - Math.sqrt(p7(Cb) / (p7(Cb) + p7(25))));
  const a1p = (1 + Gk) * a1, a2p = (1 + Gk) * a2, C1 = Math.hypot(a1p, b1), C2 = Math.hypot(a2p, b2);
  const h1 = (Math.atan2(b1, a1p) / rad + 360) % 360, h2 = (Math.atan2(b2, a2p) / rad + 360) % 360;
  let dh = h2 - h1; if (C1 * C2 === 0) dh = 0; else if (dh > 180) dh -= 360; else if (dh < -180) dh += 360;
  const dH = 2 * Math.sqrt(C1 * C2) * Math.sin(dh / 2 * rad), Lb = (L1 + L2) / 2, Cbp = (C1 + C2) / 2;
  let hb = h1 + h2; if (C1 * C2 !== 0) { if (Math.abs(h1 - h2) > 180) hb += hb < 360 ? 360 : -360; hb /= 2; }
  const T = 1 - 0.17 * Math.cos((hb - 30) * rad) + 0.24 * Math.cos(2 * hb * rad) + 0.32 * Math.cos((3 * hb + 6) * rad) - 0.2 * Math.cos((4 * hb - 63) * rad);
  const SL = 1 + 0.015 * (Lb - 50) ** 2 / Math.sqrt(20 + (Lb - 50) ** 2), SC = 1 + 0.045 * Cbp, SH = 1 + 0.015 * Cbp * T;
  const RT = -Math.sin(60 * Math.exp(-(((hb - 275) / 25) ** 2)) * rad) * 2 * Math.sqrt(p7(Cbp) / (p7(Cbp) + p7(25)));
  return Math.sqrt((L2 - L1) ** 2 / SL ** 2 + (C2 - C1) ** 2 / SC ** 2 + dH ** 2 / SH ** 2 + RT * ((C2 - C1) / SC) * (dH / SH));
}
{
  let min = 1e9, pair = '', cmin = 1e9, cpair = '';
  const labs = F.map(p => lab(p[3]));
  for (let i = 0; i < F.length; i++) for (let j = i + 1; j < F.length; j++) {
    const d = de2000(labs[i], labs[j]);
    if (j >= nF && d < min) { min = d; pair = F[i][1] + '/' + F[j][1]; }
    if (j < nF && d < cmin) { cmin = d; cpair = F[i][1] + '/' + F[j][1]; }
  }
  console.log(`faction colours: min ΔE2000 for pairs with a new faction ${min.toFixed(1)} (${pair}); classic-only pairs (unchanged) ${cmin.toFixed(1)} (${cpair})`);
  check(min >= 10, 'new faction colours ≥ 10 ΔE2000 from every other faction');
}
// 显示名长度
{
  const longC = C.slice(nC).filter(p => [...p[1]].length > 4).map(p => p[1]);
  const longG = newGens.filter(p => [...p[0]].length > 4).map(p => p[0]);
  const longF = F.slice(nF).filter(p => [...p[1]].length > 4).map(p => p[1]);
  const g6 = newGens.filter(p => [...p[0]].length > 5).map(p => p[0]);
  console.log(`names > 4 chars: cities ${longC.length} [${longC.join(' ')}], generals ${longG.length} [${longG.join(' ')}], factions ${longF.length} [${longF.join(' ')}]`);
  // 显示名尽量 ≤ 4 字；无通称的外国名用完整通行译名（≤ 5 字）。唯一的 6 字名：阿根托科克斯（无通行译名，不宜截断）
  check(C.slice(nC).every(p => [...p[1]].length <= 5) && F.slice(nF).every(p => [...p[1]].length <= 5), 'city / faction display names ≤ 5 chars');
  check(newGens.every(p => [...p[0]].length <= 6) && g6.length <= 1, 'general display names ≤ 5 chars (at most one of 6): ' + g6.join());
}
// 开局状态
let g0;
try {
  SG.Random.seed(1);
  g0 = GameState.newGame('roma');
  for (const f of g0.factions) check(g0.cityCount(f.id) >= 1, 'faction owns a city at start ' + f.name);
  for (const c of g0.cities) if (c.owner >= 0) check(g0.officersIn(c).length > 0, 'owned city has an officer ' + c.name);
  const nNeutral = g0.cities.filter(c => c.owner < 0).length;
  console.log(`start: ${g0.factions.length} factions, ${g0.cities.length - nNeutral} owned cities, ${nNeutral} neutral`);
  // 存档往返
  const g1 = GameState.fromJSON(JSON.stringify(g0));
  check(g1 && g1.cities.length === g0.cities.length && g1.cities.every((c, i) => c.links.length === g0.cities[i].links.length), 'save round trip');
} catch (e) { check(false, 'newGame: ' + (e.stack || e)); }

// =============================================================== (2) 模拟 --
console.log(`== (2) all-AI simulation: ${SEEDS} seeds x ${YEARS} years ==`);
function consistency(g) {
  for (const c of g.cities) {
    if (c.owner >= 0 && !g.factions[c.owner].alive) throw new Error('dead faction owns city ' + c.name);
    if (c.gold < 0 || c.food < 0) throw new Error('negative res ' + c.name);
  }
  for (const x of g.generals) if (x.troops < 0 || (x.faction >= 0 && !g.factions[x.faction].alive)) throw new Error('bad general ' + x.name);
  for (const f of g.factions) if (f.alive) { const r = g.generals[f.ruler]; if (r.dead || r.faction !== f.id) throw new Error('bad ruler ' + f.name); }
}
const regionName = {}; for (const r of W.Regions) regionName[r.id] = r.name;
const CHECK_Y = [1, 3, 5, 10, 15, 20, 25].filter(y => y <= YEARS);
const surv = {}; for (const r of W.Regions) surv[r.id] = CHECK_Y.map(() => 0);
const watch = ['roma', 'parthia', 'kushan'];
const watchN = {}; for (const k of watch) watchN[k] = CHECK_Y.map(() => []);
// “西方”= 欧洲与西亚：罗马、蛮族、波斯、阿拉伯四个地域的开局城池，加上这些文化的无主城（不含中亚草原与贵霜式城邦），
// 用来衡量罗马是否吞并西方
const WEST_REG = ['roma', 'barbar', 'bosi', 'arab'];
const WEST_CULT = ['roman', 'celt', 'german', 'sarmatian', 'persia', 'arab'];
const westShare = CHECK_Y.map(() => []);
let WEST_N = 0;
const first25 = [], wiped = [], wipedFast = [];
let months = 0, simMs = 0, maxMs = 0, maxAt = '';
const wall = [];
const finals = [];
for (const seed of SEED_LIST) {
  SG.Random.seed(seed);
  const g = SG.G = GameState.newGame('roma');
  g.player = -1;
  const total = g.cities.length;
  const regF = {}; for (const r of W.Regions) regF[r.id] = r.factions.map(k => g.factionByKey(k).id);
  const regDead = {};
  const westCities = new Set();
  for (const c of g.cities) {
    if (c.owner >= 0 ? WEST_REG.includes(W.Regions.find(r => r.factions.includes(g.factions[c.owner].key)).id) : WEST_CULT.includes(W.Cities[c.id].split('|')[10])) westCities.add(c.id);
  }
  let f25 = null;
  try {
    for (let m = 0; m < YEARS * 12; m++) {
      const t = process.hrtime.bigint(), cpu = process.cpuUsage();
      const pb = StrategyAI.runAI([]);
      if (pb.length) throw new Error('player battle without player');
      StrategyAI.endMonth();
      const ms = Number(process.hrtime.bigint() - t) / 1e6;
      const cu = process.cpuUsage(cpu), cms = (cu.user + cu.system) / 1000;
      simMs += ms; months++; wall.push(ms);
      if (cms > maxMs) { maxMs = cms; maxAt = `seed ${seed} month ${m}`; }
      consistency(g);
      if (f25 === null) for (const f of g.factions) if (f.alive && g.cityCount(f.id) > total * 0.25) { f25 = { m, f: f.name }; break; }
      for (const r of W.Regions) if (!regDead[r.id] && regF[r.id].every(i => !g.factions[i].alive)) regDead[r.id] = m + 1;
      const yi = CHECK_Y.indexOf((m + 1) / 12);
      if (yi >= 0) {
        for (const r of W.Regions) surv[r.id][yi] += regF[r.id].filter(i => g.factions[i].alive).length;
        for (const k of watch) watchN[k][yi].push(g.cityCount(g.factionByKey(k).id));
        const rid = g.factionByKey('roma').id; let rw = 0; for (const id of westCities) if (g.cities[id].owner === rid) rw++;
        westShare[yi].push(rw / westCities.size); WEST_N = westCities.size;
      }
    }
  } catch (e) { check(false, `seed ${seed}: ${e.stack || e}`); continue; }
  first25.push(f25);
  for (const id in regDead) if (regDead[id] < 36) wiped.push(`seed ${seed} ${regionName[id]} (${regDead[id]} months)`);
  for (const id in regDead) if (regDead[id] < 24) wipedFast.push(`seed ${seed} ${regionName[id]} (${regDead[id]} months)`);
  const top = g.factions.filter(f => f.alive).map(f => ({ f, n: g.cityCount(f.id) })).sort((a, b) => b.n - a.n).slice(0, 6).map(o => o.f.name + ':' + o.n);
  finals.push(`seed ${seed}: alive ${g.factions.filter(f => f.alive).length}/${g.factions.length}, neutral ${g.cities.filter(c => c.owner < 0).length}, first >25% ${f25 ? f25.f + ' @' + (190 + Math.floor(f25.m / 12)) + '-' + (f25.m % 12 + 1) : 'never'}; top [${top.join(', ')}]`);
}
for (const l of finals) console.log(l);
wall.sort((a, b) => a - b);
const pct = p => wall[Math.min(wall.length - 1, Math.floor(wall.length * p))].toFixed(1);
console.log(`speed: ${months} months in ${(simMs / 1000).toFixed(1)}s = ${(months / (simMs / 1000)).toFixed(0)} months/s; wall ms per month avg ${(simMs / months).toFixed(2)}, p99 ${pct(0.99)}, p99.9 ${pct(0.999)}, max ${wall[wall.length - 1].toFixed(1)}; CPU max ${maxMs.toFixed(1)} ms (${maxAt})`);
// 单月耗时以 CPU 时间计（墙钟在 4 核共享机器上会被其他进程抢占，偶有 >60ms 的尖峰）
check(maxMs < 60, 'max per-month processing (CPU time) < 60 ms');
check(+pct(0.999) < 60, 'p99.9 per-month wall time < 60 ms');
console.log('region factions alive (avg over seeds) at years ' + CHECK_Y.join('/') + ':');
for (const r of W.Regions) console.log(`  ${(regionName[r.id] + '　　　　').slice(0, 6)} ${r.factions.length} → ${surv[r.id].map(v => (v / SEEDS).toFixed(1)).join(' / ')}`);
const avgN = a => a.length ? (a.reduce((s, v) => s + v, 0) / a.length).toFixed(1) : '-';
for (const k of watch) {
  const fn = W.Factions.find(l => l.startsWith(k + '|')).split('|')[1];
  console.log(`  ${fn} cities avg [min..max]: ` + CHECK_Y.map((y, i) => `${y}y ${avgN(watchN[k][i])} [${Math.min(...watchN[k][i])}..${Math.max(...watchN[k][i])}]`).join(', '));
}
console.log(`  罗马 share of the West's ${'' + WEST_N} cities avg [max]: ` + CHECK_Y.map((y, i) => `${y}y ${(100 * westShare[i].reduce((a, b) => a + b, 0) / Math.max(1, westShare[i].length)).toFixed(0)}% [${(100 * Math.max(...westShare[i])).toFixed(0)}%]`).join(', '));
for (const k of watch) {
  const fn = W.Factions.find(l => l.startsWith(k + '|')).split('|')[1];
  console.log(`  ${fn} alive in ` + CHECK_Y.map((y, i) => `${y}y ${watchN[k][i].filter(n => n > 0).length}/${watchN[k][i].length}`).join(', ') + ' seeds');
}
const f25m = first25.map(x => x ? x.m : Infinity);
console.log(`first faction > 25% of cities: ${first25.map(x => x ? (x.m / 12).toFixed(1) + 'y ' + x.f : 'never').join(', ')}`);
console.log(`regions wiped out in < 3 years: ${wiped.length ? wiped.join('; ') : 'none'}`);
{
  const hit = first25.filter(Boolean), byF = {};
  for (const x of hit) byF[x.f] = (byF[x.f] || 0) + 1;
  const ys = hit.map(x => x.m / 12).sort((a, b) => a - b);
  console.log(`first > 25%: ${hit.length}/${first25.length} seeds [${Object.entries(byF).map(([k, v]) => k + ' ' + v).join(', ')}], earliest ${ys.length ? ys[0].toFixed(1) + 'y' : '-'}`);
  const alive = finals.map(l => +/alive (\d+)/.exec(l)[1]);
  if (alive.length) console.log(`factions alive after ${YEARS}y: ${Math.min(...alive)}–${Math.max(...alive)} of ${W.Factions.length}`);
}
// 平衡期望：不在 3 年内灭掉整个地域；无人 5 年内吞下四分之一天下；罗马 3 年后仍在且 5 年内没有吞并西方
// 平衡期望（经典剧本同样的全电脑模拟：首个势力 0.8–5.8 年、中位约 2 年超过 25%；25 年后 18 家剩 2–7 家）
const sorted25 = f25m.slice().sort((a, b) => a - b);
console.log(`median time to 25%: ${sorted25[sorted25.length >> 1] === Infinity ? 'never' : (sorted25[sorted25.length >> 1] / 12).toFixed(1) + 'y'} (classic scenario, same harness: ~2y)`);
// 引擎特性：孤身守将会出城夺取空城（经典剧本同样如此），一城一将的小国（西域诸城邦等）偶尔会很快覆灭。
// 在 240 个种子上量得：3 年内某地域全灭约 4% 的对局，2 年内约 0.5–1%。
// 故允许每 12 个种子至多 1 次“地域 3 年内全灭”，每 48 个种子至多 1 次“2 年内全灭”（不足 48 个种子时不允许）。
check(wiped.length <= Math.max(1, Math.floor(SEEDS / 12)), `regions wiped out within 3 years: at most 1 per 12 seeds (got ${wiped.length})`);
check(wipedFast.length <= Math.floor(SEEDS / 48), `regions wiped out within 2 years: at most 1 per 48 seeds (got ${wipedFast.length}${wipedFast.length ? ': ' + wipedFast.join('; ') : ''})`);
check(f25m.every(m => m >= 24), 'no faction above 25% of all cities within 2 years');
check(sorted25[sorted25.length >> 1] >= 48, 'median time to 25% ≥ 4 years');
const yi3 = CHECK_Y.indexOf(3), yi5 = CHECK_Y.indexOf(5);
if (yi3 >= 0) check(watchN.roma[yi3].every(n => n >= 6), 'Rome keeps ≥ 6 cities at year 3 in every seed');
if (yi5 >= 0) {
  const ws5 = westShare[yi5].reduce((a, b) => a + b, 0) / westShare[yi5].length;
  // 西方 = 欧洲与西亚的 40 座城（罗马开局 15 座）。旧版把中亚草原与河中的无主城也算进来（51 座），门槛按同一尺度换算
  check(westShare[yi5].every(v => v < 0.95) && ws5 < 0.7, `Rome holds < 95% of the West at year 5 in every seed and < 70% on average (avg ${(100 * ws5).toFixed(0)}%, max ${(100 * Math.max(...westShare[yi5])).toFixed(0)}%)`);
  check(watchN.roma[yi5].every(n => n >= 12), 'Rome keeps ≥ 12 cities at year 5 in every seed');
  check(surv.barbar[yi5] / SEEDS >= 1.5, 'barbarian factions: ≥ 1.5 alive on average at year 5');
  check(watchN.parthia[yi5].filter(n => n > 0).length >= SEEDS * 0.5, 'Parthia alive at year 5 in ≥ 50% of seeds');
  check(watchN.kushan[yi5].filter(n => n > 0).length >= SEEDS * 0.5, 'Kushan alive at year 5 in ≥ 50% of seeds');
}

// =============================================================== (3) 经典剧本不受影响 --
{
  const cp = require('child_process');
  const run = mode => cp.execFileSync(process.execPath, [__filename, '--classic-hash', mode], { encoding: 'utf8', timeout: 120000 }).trim();
  const a = run('plain'), b = run('world');
  console.log(`== (3) classic 5-year all-AI game, seed 7: without world-data.js ${a} | with ${b}`);
  check(a === b, 'classic scenario identical with and without world-data.js');
}

console.log(`${fails === 0 ? 'ALL OK' : fails + ' FAILED'}  (${((Date.now() - t0) / 1000).toFixed(1)}s)`);
process.exit(fails);
