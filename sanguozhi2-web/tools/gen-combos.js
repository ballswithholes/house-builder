'use strict';
/* ==========================================================================
   tools/gen-combos.js — 八式连段（招式谱）的生成工具（COMBAT-V4 §10.4；原型 design-v4/combat/gen-combos-proto.js）
   规则（合法串 C1–C5、适配分、终结技命名、动作变体）全在 js/combos.js，运行时兜底与本工具共用同一套。

   用法：
     node tools/gen-combos.js build     第一次：写 js/combo-data.js 与 tests/refs/combo-data.v4.json（已存在时拒绝，除非 --force）
     node tools/gen-combos.js append    只为表里没有的新武将分配（已有条目一律不动：冻结），重写两个文件
     node tools/gen-combos.js check     CI：表与 refs 一致（改动须登记在 tools/combo-migrations.json）、两个剧本的武将都有招式、
                                        C1–C5 / R1–R4 / kStr 区间；退出码 = 问题数
     node tools/gen-combos.js --cs out.cs   另写 Unity 的 ComboData.g.cs（Assets/Sanguo/Scripts/Data/，COMBAT-V4 §16.4）
   分配（§10.4）：先放 65 条手写（design-v4/combat/hand-combos.json），其余按（武力降序、姓名码位升序）依次取
   适配分最高、未被占用、且与同一开局势力（两个剧本）已分配者海明距离 ≥ 2 的合法串；终结技名全局唯一，且不与任何必杀 / 计策同名。
   ========================================================================== */
const fs = require('fs'), path = require('path'), vm = require('vm');
global.window = global;
const ROOT = path.join(__dirname, '..');
const JS = path.join(ROOT, 'js');
const OUT_JS = path.join(JS, 'combo-data.js');
const OUT_REF = path.join(ROOT, 'tests', 'refs', 'combo-data.v4.json');
const HAND_FILE = path.join(ROOT, 'design-v4', 'combat', 'hand-combos.json');
const MIGR = path.join(__dirname, 'combo-migrations.json');

const args = process.argv.slice(2);
const mode = args.find(a => !a.startsWith('--')) || 'check';
const force = args.includes('--force');
const csIdx = args.indexOf('--cs');

function loadGame(withTable) {
  for (const f of ['core.js', 'data.js', 'world-data.js', 'model.js', 'battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js', 'duel-game.js']) vm.runInThisContext(fs.readFileSync(path.join(JS, f), 'utf8'), { filename: f });
  if (withTable && fs.existsSync(OUT_JS)) vm.runInThisContext(fs.readFileSync(OUT_JS, 'utf8'), { filename: 'combo-data.js' });
  vm.runInThisContext(fs.readFileSync(path.join(JS, 'combos.js'), 'utf8'), { filename: 'combos.js' });
  return global.SG;
}

// 两个剧本的武将（世界剧本含经典全部 162 人），开局势力取两个剧本的并集（二者对同一武将相同）
function roster(SG) {
  const out = new Map();
  for (const sc of ['world', 'classic']) {
    for (const l of SG.Scenarios[sc].Generals) {
      const p = l.split('|');
      if (out.has(p[0])) { const g = out.get(p[0]); if (p[4] && p[4] !== '-') g.facs.add(p[4]); continue; }
      out.set(p[0], { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], facs: new Set(p[4] && p[4] !== '-' ? [p[4]] : []), culture: p[9] || 'han' });
    }
  }
  return [...out.values()];
}

function takenNames(SG) {
  const taken = new Set(SG.Defs.tactics.map(t => t.name));
  try { for (const sp of SG.Specials.all()) taken.add(sp.name); } catch (e) { /* 无 */ }
  try { if (SG.Tactics && SG.Tactics.all) for (const t of SG.Tactics.all()) taken.add(t.name); } catch (e) { /* 无 */ }
  return taken;
}

// existing: Map 姓名 → 'seq|fin|vars|src'（冻结的条目，append 时保留）
function assign(SG, existing) {
  const C = SG.Combos;
  const HAND = JSON.parse(fs.readFileSync(HAND_FILE, 'utf8')).entries;
  const gens = roster(SG);
  const res = new Map(existing || []);
  const problems = [];
  const used = new Set([...res.values()].map(v => v.split('|')[0]));
  const names = takenNames(SG);
  for (const v of res.values()) names.add(v.split('|')[1]);
  const byFac = {};
  const addFac = (g, seq) => { for (const f of g.facs) (byFac[f] = byFac[f] || []).push(seq); };
  for (const g of gens) if (res.has(g.name)) addFac(g, res.get(g.name).split('|')[0]);
  // 1. 手写条目（§11）：合法、两两海明 ≥ 2、终结技名唯一且不与必杀 / 计策同名；变体可固定终结技
  for (const e of HAND) {
    if (res.has(e.name)) continue;
    const g = gens.find(x => x.name === e.name);
    if (!g) { problems.push('hand entry for unknown general ' + e.name); continue; }
    const why = C.legal(e.seq);
    if (why) problems.push(`hand ${e.name} ${e.seq}: ${why}`);
    if (used.has(e.seq)) problems.push(`hand duplicate string ${e.seq} (${e.name})`);
    if (names.has(e.finisher)) problems.push(`hand finisher name taken: ${e.name} ${e.finisher}`);
    used.add(e.seq); names.add(e.finisher); addFac(g, e.seq);
    const vars = C.variantsFor(e.name, e.seq, C.classOf(g), e.finisherVariant);
    res.set(e.name, [e.seq, e.finisher, vars.join(','), 'h'].join('|'));
  }
  const handSeqs = HAND.map(e => e.seq);
  for (let i = 0; i < handSeqs.length; i++) for (let j = i + 1; j < handSeqs.length; j++) if (C.hamming(handSeqs[i], handSeqs[j]) < 2) problems.push(`hand hamming < 2: ${HAND[i].name} / ${HAND[j].name}`);
  // 2.–3. 其余：武力降序、姓名码位升序
  const order = gens.filter(g => !res.has(g.name)).sort((a, b) => b.war - a.war || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
  let relax = 0;
  const ALL = C.legalSet();
  for (const g of order) {
    const cls = C.classOf(g);
    const cand = ALL.filter(s => !used.has(s)).map(s => [C.fit(g, s, cls), s]).sort((a, b) => b[0] - a[0] || (a[1] < b[1] ? -1 : 1));
    const fac = [];
    for (const f of g.facs) for (const s of byFac[f] || []) fac.push(s);
    let pick = null;
    for (const [, s] of cand) if (fac.every(t => C.hamming(t, s) >= 2)) { pick = s; break; }
    if (!pick) { relax++; pick = cand[0][1]; }
    used.add(pick); addFac(g, pick);
    const fin = C.finisherName(g.name, g.culture, cls, pick[7], names);
    names.add(fin);
    res.set(g.name, [pick, fin, C.variantsFor(g.name, pick, cls).join(','), 'g'].join('|'));
  }
  return { res, problems, relax, order: order.length, gens };
}

function writeOut(res, gens) {
  // 表序：两个剧本的名单次序（世界剧本次序），手写与生成混排；文件头注明冻结规则
  const lines = [];
  for (const g of gens) if (res.has(g.name)) lines.push(`    ${JSON.stringify(g.name)}: ${JSON.stringify(res.get(g.name))},`);
  for (const [n, v] of res) if (!gens.find(g => g.name === n)) lines.push(`    ${JSON.stringify(n)}: ${JSON.stringify(v)},`);
  const js = `'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 招式谱数据（COMBAT-V4 §8.1、§10–§11）—— 由 tools/gen-combos.js 生成，勿手改
   条目：姓名 → '招式谱（8 个 L/M/H）|终结技名|7 步变体 + 终结变体|来源 h 手写 / g 生成'
   冻结：一经发布，已有条目永不改动（存档、玩家的见识与肌肉记忆都依赖它）；新武将用 \`node tools/gen-combos.js append\` 追加；
   改动已有条目须登记在 tools/combo-migrations.json（tests/combos.js 对照 tests/refs/combo-data.v4.json）。
   ========================================================================== */
(function () {
  window.SG.ComboData = { version: 1, entries: {
${lines.join('\n')}
  } };
})();
`;
  fs.writeFileSync(OUT_JS, js);
  const ref = { version: 1, n: res.size, entries: Object.fromEntries([...res.entries()]) };
  fs.mkdirSync(path.dirname(OUT_REF), { recursive: true });
  fs.writeFileSync(OUT_REF, JSON.stringify(ref, null, 0) + '\n');
}

function readTable() {
  if (!fs.existsSync(OUT_JS)) return null;
  const sb = { window: { SG: {} } };
  vm.runInNewContext(fs.readFileSync(OUT_JS, 'utf8'), sb);
  return new Map(Object.entries(sb.window.SG.ComboData.entries));
}

function writeCs(file, res) {
  const esc = s => s.replace(/\\/g, '\\\\').replace(/"/g, '\\"');
  const rows = [...res.entries()].map(([n, v]) => `            { "${esc(n)}", "${esc(v)}" },`);
  const cs = `// <auto-generated> tools/gen-combos.js --cs：由 js/combo-data.js 生成（COMBAT-V4 §16.4），勿手改 </auto-generated>
using System.Collections.Generic;
namespace Sanguo.Data {
    public static class ComboData {
        public const int Version = 1;
        public static readonly Dictionary<string, string> Entries = new Dictionary<string, string> {
${rows.join('\n')}
        };
    }
}
`;
  fs.writeFileSync(file, cs);
}

function main() {
  let problems = 0;
  if (mode === 'build' || mode === 'append') {
    const existing = readTable();
    if (mode === 'build' && existing && !force) { console.log('js/combo-data.js exists: the table is frozen. Use `append` for new generals (or --force to rebuild from scratch).'); process.exitCode = 1; return; }
    const SG = loadGame(false);
    const out = assign(SG, mode === 'append' ? existing : null);
    for (const p of out.problems) console.log('  PROBLEM: ' + p);
    writeOut(out.res, out.gens);
    console.log(`${mode}: ${out.res.size} entries (${mode === 'append' && existing ? out.res.size - existing.size + ' new' : 'all new'}), generated ${out.order}, faction-rule relaxations ${out.relax}, spare strings ${SG.Combos.legalSet().length - out.res.size}`);
    problems += out.problems.length;
  }
  // check（build / append 之后也跑一遍）
  {
    const SG = loadGame(true);
    const C = SG.Combos;
    const v = C.validate();
    for (const p of v.problems) console.log('  PROBLEM: ' + p);
    problems += v.problems.length;
    const gens = roster(SG);
    const missing = gens.filter(g => !C.has(g.name));
    if (missing.length) { problems += missing.length; console.log('  PROBLEM: generals without a string: ' + missing.map(g => g.name).join(' ')); }
    if (!(v.kStrRange[0] >= 0.83 && v.kStrRange[1] <= 1.03)) { problems++; console.log('  PROBLEM: kStr range ' + v.kStrRange.map(x => x.toFixed(3)).join('–')); }
    // 冻结：与 refs 对照
    if (fs.existsSync(OUT_REF)) {
      const ref = JSON.parse(fs.readFileSync(OUT_REF, 'utf8')).entries;
      const allowed = fs.existsSync(MIGR) ? (JSON.parse(fs.readFileSync(MIGR, 'utf8')).changes || []).map(c => c.name) : [];
      const cur = SG.ComboData.entries;
      for (const n in ref) {
        if (!(n in cur)) { problems++; console.log(`  PROBLEM: frozen entry removed: ${n}`); }
        else if (cur[n] !== ref[n] && allowed.indexOf(n) < 0) { problems++; console.log(`  PROBLEM: frozen entry changed without a migration: ${n} ${ref[n]} → ${cur[n]}`); }
      }
    } else { problems++; console.log('  PROBLEM: tests/refs/combo-data.v4.json missing'); }
    const fin = { L: 0, M: 0, H: 0 }; for (const e of C.all()) fin[e.seq[7]]++;
    console.log(`check: ${v.n} entries, ${v.legal} legal strings, ${v.spare} spare, kStr ${v.kStrRange.map(x => x.toFixed(3)).join('–')}, finishers L/M/H ${fin.L}/${fin.M}/${fin.H}, ${v.factions} starting factions; ${problems ? problems + ' problems' : 'OK'}`);
    if (csIdx >= 0 && args[csIdx + 1]) { writeCs(args[csIdx + 1], new Map(Object.entries(SG.ComboData.entries))); console.log('wrote ' + args[csIdx + 1]); }
  }
  process.exitCode = problems;
}
main();
