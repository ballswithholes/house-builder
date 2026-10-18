'use strict';
/* ==========================================================================
   COMBAT-V4 §10 参考原型：八式连段（招式谱）的合法集合、手写条目校验、其余武将的确定性分配、终结技命名。
   只读游戏数据（js/core.js、data.js、world-data.js、duel-game.js），不改任何游戏文件；正式实现见 §10 的 tools/gen-combos.js。
   用法：node design-v4/combat/gen-combos-proto.js [--dump 输出文件.json]
   ========================================================================== */
const fs = require('fs'), path = require('path'), vm = require('vm');
global.window = global;
const ROOT = path.join(__dirname, '..', '..');
for (const f of ['core.js', 'data.js', 'model.js', 'battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js', 'world-data.js', 'duel-game.js']) vm.runInThisContext(fs.readFileSync(path.join(ROOT, 'js', f), 'utf8'), { filename: f });
const SG = global.SG;
const HAND = JSON.parse(fs.readFileSync(path.join(__dirname, 'hand-combos.json'), 'utf8')).entries;

// ---- §10.1 合法招式谱 C1–C6 ----
function legal(s) {
  if (!/^[LMH]{8}$/.test(s)) return 'C0 format';
  if (s[0] === 'H') return 'C1 starter must be L or M';
  if (s[1] === 'H') return 'C2 no H at step 2';
  const n = { L: 0, M: 0, H: 0 };
  for (const c of s.slice(0, 7)) n[c]++;
  if (n.L < 2 || n.M < 2 || n.H < 1 || n.H > 3) return 'C3 counts in steps 1-7';
  for (let i = 0; i + 2 < 8; i++) if (s[i] === s[i + 1] && s[i] === s[i + 2]) return 'C4 triple at ' + (i + 1);
  for (let i = 0; i < 6; i++) if (s[i] === 'H' && s[i + 1] === 'H') return 'C5 HH inside steps 1-7';
  return '';
}
const ALL = [];
(function rec(p) { if (p.length === 8) { const s = p.join(''); if (!legal(s)) ALL.push(s); return; } for (const c of 'LMH') { p.push(c); rec(p); p.pop(); } })([]);
const ham = (a, b) => { let d = 0; for (let i = 0; i < 8; i++) if (a[i] !== b[i]) d++; return d; };

// ---- §8.5 归一化 ----
const BASE = { L: 2.4, M: 3.4, H: 5.0 }, FIN = { L: 5.5, M: 6.5, H: 8.0 }, SCALE = [1, 1, 0.9, 0.8, 0.72, 0.65, 0.6];
const RAW = s => { let r = 0; for (let i = 0; i < 7; i++) r += BASE[s[i]] * SCALE[i]; return r + FIN[s[7]] * 0.75; };
const nH = s => [...s.slice(0, 7)].filter(c => c === 'H').length;
const kStr = s => (28 / 1.31) / RAW(s) * (1 + 0.04 * (nH(s) - 1));

// ---- §10.3 适配分 ----
const CLASS = { guandao: 'pole', snake: 'pole', ji: 'pole', spear: 'pole', poleblade: 'pole', lance: 'pole', axe: 'heavy', bigblade: 'heavy', mace: 'heavy', handaxe: 'heavy',
  sword: 'one', dao: 'one', shortji: 'one', gladius: 'one', tachi: 'one', twinsword: 'dual', twinji: 'dual', twindao: 'dual' };
const W = {
  pole: { L: 0.8, M: 1.2, H: 1.0, F: { L: 0.5, M: 1.0, H: 1.2 } },
  heavy: { L: 0.6, M: 1.0, H: 1.4, F: { L: 0.3, M: 0.8, H: 1.5 } },
  one: { L: 1.2, M: 1.1, H: 0.7, F: { L: 0.9, M: 1.2, H: 0.8 } },
  dual: { L: 1.4, M: 1.0, H: 0.6, F: { L: 1.3, M: 1.0, H: 0.6 } },
};
function fnv(str) { let h = 2166136261 >>> 0; for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h >>> 0; }
function classOf(g) { return CLASS[SG.DuelGame.lookOf(g).weapon] || 'one'; }
function fit(g, s) {
  const w = W[classOf(g)], a = (g.war - g.intel) / 100;            // 勇（武高智低）偏重
  let v = 0;
  for (let i = 0; i < 7; i++) v += s[i] === 'H' ? w.H + 0.3 * a : w[s[i]];
  v += 2 * w.F[s[7]] + (s[7] === 'H' ? 0.3 * a : 0);
  let ch = 0; for (let i = 1; i < 8; i++) if (s[i] !== s[i - 1]) ch++;
  v += 0.05 * (g.intel / 100) * ch;                                  // 智：多变
  let per = 0; for (let i = 2; i < 8; i++) if (s[i] === s[i - 2]) per++;
  v += 0.04 * (g.pol / 100) * per;                                   // 政：规整
  return v + fnv(g.name + '|' + s) / 4294967296 * 0.9;              // 确定性抖动
}

// ---- §10.5 终结技命名 ----
const PRE = {
  pole: ['横扫', '回马', '破阵', '穿云', '裂地', '惊龙', '追风', '断岳', '贯日', '奔雷', '镇岳', '凌霄', '摧城', '踏营', '卷旗', '定边', '长虹', '飞龙', '扫营', '截云', '撼阵', '拦江', '挑灯', '荡寇'],
  heavy: ['碎岳', '崩山', '震地', '开碑', '裂石', '撼岳', '断碑', '摧锋', '雷霆', '轰顶', '镇魔', '破甲', '覆城', '伏虎', '塌天', '劈关', '撞钟', '击鼓', '落锤', '断锁', '镇山', '摧枯', '震天', '断江'],
  one: ['流星', '回风', '断水', '惊鸿', '落月', '残雪', '雷光', '游龙', '寒芒', '照胆', '秋水', '映雪', '穿杨', '截江', '分光', '飞霜', '青锋', '白虹', '紫电', '鸣鸿', '归鞘', '拂柳', '惊雷', '断魂', '星河', '霜降', '孤月', '横江', '剑影', '风雷', '卧龙', '藏锋'],
  dual: ['双龙', '乱舞', '飞燕', '交错', '连环', '蝶影', '双星', '雁翎', '比翼', '风车', '回旋', '双月', '影分', '剪羽', '对影', '流萤'],
};
const CPRE = { han: ['燕赵', '吴越', '荆楚', '齐鲁', '秦川', '巴山', '中原', '江淮'], roman: ['军团', '罗马', '凯旋', '角斗'], persia: ['安息', '波斯', '帕提亚', '火坛'], steppe: ['草原', '控弦', '狼骑', '风雪'], wa: ['倭国', '勾玉', '八咫', '神风'],
  korea: ['三韩', '鸟羽', '白头', '鸭绿'], india: ['天竺', '恒河', '象兵', '金轮'], arab: ['沙漠', '绿洲', '驼铃', '新月'], africa: ['阿克苏姆', '尼罗', '金叶', '库施'],
  kushan: ['贵霜', '大夏', '雪山', '佛光'], celt: ['迷雾', '橡树', '高地', '风笛'], german: ['森林', '雷神', '铁盾', '狼群'], sarmatian: ['长矛', '鳞甲', '冰河', '草海'],
  tarim: ['西域', '楼兰', '葱岭', '流沙'], seasia: ['南海', '象牙', '金邻', '季风'], nanman: ['南中', '瘴林', '藤甲', '象阵'], moor: ['阿特拉斯', '标枪', '沙丘', '游骑'], yi: ['夷洲', '鹿角', '海潮', '鸟羽'] };
const SUF = { L: ['斩', '闪', '切', '舞', '连斩', '疾风', '乱刃', '落叶'], M: ['击', '刺', '破', '旋', '突', '穿心', '回旋', '贯击'], H: ['劈', '杀', '灭', '崩', '断', '裂空', '镇压', '破军'] };

function main() {
  const out = { legal: ALL.length, problems: [] };
  // 手写条目：合法、唯一、两两海明距离 ≥ 2（§10.2 R3）、终结技名唯一
  const hand = new Map();
  for (const e of HAND) {
    const why = legal(e.seq);
    if (why) out.problems.push(`hand ${e.name} ${e.seq}: ${why}`);
    if (hand.has(e.seq)) out.problems.push(`hand duplicate ${e.seq}`);
    hand.set(e.seq, e.name);
  }
  const H = [...hand.keys()];
  for (let i = 0; i < H.length; i++) for (let j = i + 1; j < H.length; j++) if (ham(H[i], H[j]) < 2) out.problems.push(`hand hamming<2 ${hand.get(H[i])} ${hand.get(H[j])}`);
  const names = new Set(HAND.map(e => e.finisher));
  if (names.size !== HAND.length) out.problems.push('hand finisher names not unique');
  // 终结技名不得与任何必杀 / 计策同名（§10.5）
  const taken = new Set(SG.Defs.tactics.map(t => t.name));
  try { for (const sp of SG.Specials.all()) taken.add(sp.name); } catch (e) { /* 无必杀模块 */ }
  try { const T = SG.Tactics; if (T && T.all) for (const t of T.all()) taken.add(t.name); } catch (e) { /* 无计策模块 */ }
  for (const e of HAND) if (taken.has(e.finisher)) out.problems.push('hand finisher name equals a special / tactic: ' + e.name + ' ' + e.finisher);
  for (const n of taken) names.add(n);

  // 其余武将：按（武力降序、姓名码位升序）依次取适配分最高、未被占用、且与同一开局势力已分配者海明距离 ≥ 2 的招式谱（§10.2 R1、R2）
  const gens = SG.Scenarios.world.Generals.map(l => { const p = l.split('|'); return { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], fac: p[4], culture: p[9] || 'han' }; });
  const used = new Set(H), byFac = {}, res = {};
  for (const g of gens) { const e = HAND.find(x => x.name === g.name); if (e) { res[g.name] = { seq: e.seq, fin: e.finisher, src: 'hand' }; if (g.fac !== '-') (byFac[g.fac] = byFac[g.fac] || []).push(e.seq); } }
  const order = gens.filter(g => !res[g.name]).sort((a, b) => b.war - a.war || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
  let relax = 0;
  for (const g of order) {
    const cand = ALL.filter(s => !used.has(s)).map(s => [fit(g, s), s]).sort((a, b) => b[0] - a[0] || (a[1] < b[1] ? -1 : 1));
    const fac = g.fac !== '-' ? (byFac[g.fac] || []) : [];
    let pick = null;
    for (const [, s] of cand) if (fac.every(t => ham(t, s) >= 2)) { pick = s; break; }
    if (!pick) { relax++; pick = cand[0][1]; }
    used.add(pick); if (g.fac !== '-') (byFac[g.fac] = byFac[g.fac] || []).push(pick);
    // 终结技名：(文化前缀 ∪ 兵器前缀) × 按终结类型的后缀，按姓名哈希起点线性探测，直到全局唯一
    const pre = (CPRE[g.culture] || []).concat(PRE[classOf(g)]), suf = SUF[pick[7]];
    const N = pre.length * suf.length;
    let k = fnv(g.name) % N, fin = null;
    for (let t = 0; t < N; t++, k = (k + 1) % N) { const nm = pre[Math.floor(k / suf.length)] + suf[k % suf.length]; if (!names.has(nm)) { fin = nm; break; } }
    if (!fin) { fin = g.name.slice(0, 1) + '家' + suf[0]; out.problems.push('finisher name pool exhausted for ' + g.name); }
    names.add(fin);
    res[g.name] = { seq: pick, fin, src: 'gen' };
  }
  out.assigned = Object.keys(res).length; out.spare = ALL.length - used.size; out.relaxedFactionRule = relax;
  const ks = ALL.map(kStr); out.kStrRange = [Math.min(...ks).toFixed(3), Math.max(...ks).toFixed(3)];
  const fin = { L: 0, M: 0, H: 0 }; for (const n in res) fin[res[n].seq[7]]++; out.finisherTypes = fin;
  const cls = {};
  for (const g of gens) { const c = classOf(g), s = res[g.name].seq, k = cls[c] = cls[c] || { n: 0, L: 0, M: 0, H: 0 }; k.n++; for (const ch of s.slice(0, 7)) k[ch]++; }
  out.avgStepsByClass = Object.fromEntries(Object.entries(cls).map(([c, k]) => [c, { n: k.n, L: +(k.L / k.n).toFixed(2), M: +(k.M / k.n).toFixed(2), H: +(k.H / k.n).toFixed(2) }]));
  out.samples = order.slice(0, 10).map(g => g.name + ' ' + res[g.name].seq + ' ' + res[g.name].fin);
  console.log(JSON.stringify(out, null, 1));
  const i = process.argv.indexOf('--dump');
  if (i > 0 && process.argv[i + 1]) fs.writeFileSync(process.argv[i + 1], JSON.stringify(res, null, 0));
  process.exitCode = out.problems.length;
}
main();
