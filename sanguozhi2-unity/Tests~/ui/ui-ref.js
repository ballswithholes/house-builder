'use strict';
// 网页版的界面文字（ui.js 的 UI.faces，strategy-screen.js 的 openTiers / slotText / stripLore 与 report / learnDialog 中的文字规则）
// 对一组确定的输入算出结果，供 HarnessUI.cs 逐行对照（Tests~/ui/run.sh 调用）。网页版目录取环境变量 SANGUO_WEB，缺省 ../../../sanguozhi2-web。
//   node ui-ref.js            → 标准输出：每行一条结果（与 HarnessUI.cs 的输出同序同格式）
// 头像：SG.Portrait 用替身，html(gen) → 「[[F:姓名]]」；C# 侧把 UIText 的头像标记换成同样的文字。
const path = require('path'), fs = require('fs'), vm = require('vm');
global.window = global;
const WEB = process.env.SANGUO_WEB || path.join(__dirname, '..', '..', '..', 'sanguozhi2-web');
for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js', 'model.js', 'strategy-ai.js', 'battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js', 'ui.js'])
  vm.runInThisContext(fs.readFileSync(path.join(WEB, 'js', f), 'utf8'), { filename: f });
// strategy-screen.js：闭包里的文字工具导出给本脚本（只改导出那一行，函数原样）
{
  let src = fs.readFileSync(path.join(WEB, 'js', 'strategy-screen.js'), 'utf8');
  const tail = 'SG.StrategyScreen = StrategyScreen;';
  if (src.indexOf(tail) < 0) throw new Error('strategy-screen.js：找不到导出行');
  src = src.replace(tail, tail + ' SG.__ssText = { openTiers, slotText, stripLore, canLearn, tierText };');
  vm.runInThisContext(src, { filename: 'strategy-screen.js' });
}
const SG = global.SG;
// 与 HarnessTrace 相同：Unity 的 Random.value / Range(float, float) 为 float
{ const F = Math.fround, raw = SG.Random.value;
  SG.Random.value = () => F(raw());
  SG.Random.rangeFloat = (a, b) => F(F(a) + raw() * F(F(b) - F(a))); }
SG.Portrait = {
  find(name) { const g = SG.G; return g ? g.generals.find(x => x.name === name) || null : null; },
  html(gen) { return '[[F:' + gen.name + ']]'; },
};
const UI = SG.UI, T = SG.__ssText;
const out = [];
const put = s => out.push(String(s).replace(/\n/g, '\\n'));
const strip = s => String(s).replace(/<[^>]*>/g, '');

// ---------------------------------------------------------- 1. UI.faces --
// 句式：势力（与城同名时按上下文）、城名、武将名，以及月末消息与记录
function synth(g) {
  const L = [];
  const fs = g.factions, cs = g.cities, gs = g.generals;
  for (let i = 0; i < fs.length; i++) {
    const f = fs[i], c = cs[(i * 7) % cs.length], c2 = cs[(i * 13 + 5) % cs.length];
    const a = gs[(i * 11) % gs.length], b = gs[(i * 17 + 3) % gs.length];
    L.push(`${f.name}攻陷了${c.name}，${a.name}被俘。`);
    L.push(`${c.name}被${f.name}军占领，${b.name}离开了${f.name}。`);
    L.push(`${f.name}的${a.name}与${b.name}同赴${c2.name}；${f.name}势力扩张。`);
    L.push(`攻陷了${f.name}，${f.name}无人防守，${c.name}拒绝${f.name}。`);
    L.push(`${a.name}、${b.name}、${a.name}在<b>${b.name}</b>处与${f.name}同意结盟`);
  }
  for (let i = 0; i < cs.length; i++) {
    const c = cs[i];
    L.push(`${c.name}继承了${c.name}，${c.name}的城防加强。离开了${c.name}`);
  }
  return L;
}
function facesLines(g, lines) {
  const ru = g.factions.map(f => g.ruler(f.id));
  for (let i = 0; i < lines.length; i++) {
    const s = lines[i];
    put('F0 ' + UI.faces(s));
    if (i % 3 === 0) put('F1 ' + UI.faces(s, { max: 2 }));
    if (i % 4 === 1) put('F2 ' + UI.faces(s, { factions: false, max: 8 }));
    if (i % 5 === 2) { const gg = g.generals[(i * 7) % g.generals.length]; put('F3 ' + UI.faces(s, { gens: [gg, g.generals[(i * 3) % g.generals.length]], max: 12 })); }
    if (i % 6 === 3) { const sk = ru[i % ru.length]; put('F4 ' + UI.faces(s, { skip: sk ? [sk.name] : [], max: 10 })); }
  }
}
for (const [scen, key, seed, months] of [['classic', 'liubei', 1, 18], ['world', 'roma', 3, 14]]) {
  SG.Random.seed(seed);
  const g = SG.G = SG.GameState.newGame(key, scen);
  g.player = -1;
  put('== ' + scen + ' seed ' + seed);
  facesLines(g, synth(g));
  for (let m = 0; m < months; m++) {
    const news = [];
    SG.StrategyAI.runAI(news);
    for (const n of SG.StrategyAI.endMonth()) news.push(n);
    put('M' + m + ' ' + news.length);
    facesLines(g, [news.join('\n')].concat(news));
  }
  facesLines(g, g.log.slice());
}

// ---------------------------------------------------------- 2. 成长文字 --
const P = SG.Progress;
for (const [scen, key] of [['classic', 'liubei'], ['world', 'roma']]) {
  SG.Random.seed(7);
  const g = SG.G = SG.GameState.newGame(key, scen);
  g.seed = 12345;
  put('== growth ' + scen);
  for (let i = 0; i < g.generals.length; i++) {
    const gen = g.generals[i];
    if (i % 3 === 2) continue;
    gen.exp = P.EXP[1 + (i % 10)] + (i % 7) * 3;
    P.ensure(gen);
    const k = i % 4;
    for (let j = 0; j < k; j++) { const c = P.candidates(gen, 3); if (!c.length) break; P.learn(gen, c[(i + j) % c.length]); }
    const ss = P.specialSlots(gen), ts = P.tacticSlots(gen);
    const spT = t => SG.Specials.tierName(t), tcT = t => SG.Tactics.TIER_NAME[t];
    put([gen.name, gen.level, strip(T.slotText(gen, 'special')), strip(T.slotText(gen, 'tactic')), T.openTiers(ss, spT).text, T.openTiers(ss, spT).top,
      T.openTiers(ts, tcT).text, T.openTiers(ts, tcT).top, T.canLearn(gen) ? 1 : 0].join('|'));
    const cands = P.candidates(gen, 3);
    for (const c of cands) {
      const lore = c.ref && c.ref.lore ? c.ref.lore : '';
      const body = T.stripLore(c.desc, lore);
      const rules = String(c.rules || '').replace(/^(初阶|中阶|高阶|绝阶) · /, '');
      let brief = (body.split('。')[0] || body); if (Array.from(brief).length > 30) brief = Array.from(brief).slice(0, 30).join('') + '…';
      put('  ' + [c.type, c.id, T.tierText(c), body, rules, brief].join('|'));
    }
  }
  put('pending ' + P.pending(g.factions[0].id).length + ' ' + P.pending(g.factions[1].id).length);
}

// ---------------------------------------------------------- 3. 汇报去名 --
{
  const g = SG.G;
  const fakes = ['刘备主持土地开发，土地增至 50。', '刘备，你好', '刘备', '刘备。', '关羽训练部队', '刘备！大喜', '刘备、关羽出征'];
  const who = g.generals.find(x => x.name === '刘备') || g.generals[0];
  for (const t0 of fakes.concat(g.generals.slice(0, 40).map(x => x.name + '搜索人才，一无所获。'))) {
    let text = String(t0);
    const w = who;
    if (w && w.name && text.length > w.name.length + 1 && text.startsWith(w.name) && !/^[，。！？、：]/.test(text.slice(w.name.length))) text = text.slice(w.name.length);
    put('R ' + text);
  }
}
process.stdout.write(out.join('\n') + '\n');
