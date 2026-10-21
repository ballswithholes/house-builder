'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 招式谱（八式连段，COMBAT-V4 §8、§10–§12）
   每名武将一串固定的 8 步 L 轻 / M 中 / H 重；第 1–7 步为连段步，第 8 步为有名字的终结技。
   数据在 js/combo-data.js（tools/gen-combos.js 生成并冻结：SG.ComboData = { version, entries: { 姓名: 'seq|终结技|变体×8|h/g' } }）；
   本文件解析数据、提供运行时兜底（未在表中的武将，如模组武将）、校验、见识（招式知识）与熟练度。
   纯逻辑，Node 可测；生成工具 tools/gen-combos.js 与运行时兜底共用这里的同一套规则（合法性、适配分、命名、变体）。

   SG.Combos.of(gen | name) → { name, seq, steps: [{ type, variant }]×7, finisher: { type, variant, name }, kStr, nH, src: 'hand'|'gen'|'fallback' }
   SG.Combos.all() → [entry…]（表中全部，按表序）         SG.Combos.has(name)
   SG.Combos.kStr(seq) · raw(seq) · nH(seq) · legal(seq) → '' | 违规原因 · legalSet() → 790 串 · hamming(a, b)
   SG.Combos.fit(g, seq, cls) · classOf(gen) → 'pole'|'heavy'|'one'|'dual' · glyphs(seq, preset) → [{ type, glyph, key }]×8
   SG.Combos.validate() → { ok, problems, n, kStrRange, legal, spare }（C1–C5、R1–R4、kStr 区间）
   见识（§12.2，存档 G.combos = { known: { 姓名: 0..255 }, mastery: { 姓名: { s, done, onBeat, beatPract } } }）：
     known(G, name, viewerFaction?) → bitmask（本势力武将恒为 255）   markSeen(G, name, stepIdx 0–7 | bitmask, isMask)
     markAll(G, name)（俘获 / 登用）   describe(gen, viewerFaction?, G?) → 人物菜单「武艺」页的内容（§12.1）
     mastery(G, name) · addMastery(G, name, { complete, allOnBeat, vsKanpa })
   存档：SG.MigrateV4 自动发现 migrate / init / saveKeys（INTEGRATION §6.2：COMBAT 最后迁移，旧档的见识为空）。
   ========================================================================== */
(function () {
  const SG = window.SG;

  // ---- §8.5 伤害归一化（与 js/duel-game.js 的 STEP 表同值；tests/combos.js 断言两边一致）----
  const BASE = { L: 2.4, M: 3.4, H: 5.0 }, FIN = { L: 5.5, M: 6.5, H: 8.0 };
  const SCALE = [1.00, 1.00, 0.90, 0.80, 0.72, 0.65, 0.60];
  const FIN_FLOOR = 0.75, SPECIAL_FLOOR = 0.60;
  const scaleOf = n => SCALE[Math.min(SCALE.length, Math.max(1, n)) - 1];
  function raw(s) { let r = 0; for (let i = 0; i < 7; i++) r += BASE[s[i]] * SCALE[i]; return r + FIN[s[7]] * FIN_FLOOR; }
  function nH(s) { let n = 0; for (let i = 0; i < 7; i++) if (s[i] === 'H') n++; return n; }
  function kStr(s) { return (28 / 1.31) / raw(s) * (1 + 0.04 * (nH(s) - 1)); }

  // ---- §10.1 合法招式谱 C1–C5 ----
  function legal(s) {
    if (!/^[LMH]{8}$/.test(s || '')) return 'C0 format';
    if (s[0] === 'H') return 'C1 starter must be L or M';
    if (s[1] === 'H') return 'C2 no H at step 2';
    const n = { L: 0, M: 0, H: 0 };
    for (let i = 0; i < 7; i++) n[s[i]]++;
    if (n.L < 2 || n.M < 2 || n.H < 1 || n.H > 3) return 'C3 counts in steps 1-7';
    for (let i = 0; i + 2 < 8; i++) if (s[i] === s[i + 1] && s[i] === s[i + 2]) return 'C4 triple at ' + (i + 1);
    for (let i = 0; i < 6; i++) if (s[i] === 'H' && s[i + 1] === 'H') return 'C5 HH inside steps 1-7';
    return '';
  }
  let ALL = null;
  function legalSet() {
    if (ALL) return ALL;
    ALL = [];
    (function rec(p) { if (p.length === 8) { const s = p.join(''); if (!legal(s)) ALL.push(s); return; } for (const c of 'LMH') { p.push(c); rec(p); p.pop(); } })([]);
    return ALL;
  }
  function hamming(a, b) { let d = 0; for (let i = 0; i < 8; i++) if (a[i] !== b[i]) d++; return d; }

  // ---- §10.3 适配分（兵器类、勇智政、确定性抖动）----
  const CLASS = { guandao: 'pole', snake: 'pole', ji: 'pole', spear: 'pole', poleblade: 'pole', lance: 'pole', staff: 'pole',
    axe: 'heavy', bigblade: 'heavy', mace: 'heavy', handaxe: 'heavy', club: 'heavy',
    sword: 'one', dao: 'one', shortji: 'one', gladius: 'one', tachi: 'one', fan: 'one', vajra: 'one',
    twinsword: 'dual', twinji: 'dual', twindao: 'dual' };
  const W = {
    pole: { L: 0.8, M: 1.2, H: 1.0, F: { L: 0.5, M: 1.0, H: 1.2 } },
    heavy: { L: 0.6, M: 1.0, H: 1.4, F: { L: 0.3, M: 0.8, H: 1.5 } },
    one: { L: 1.2, M: 1.1, H: 0.7, F: { L: 0.9, M: 1.2, H: 0.8 } },
    dual: { L: 1.4, M: 1.0, H: 0.6, F: { L: 1.3, M: 1.0, H: 0.6 } },
  };
  function fnv(str) { let h = 2166136261 >>> 0; for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h >>> 0; }
  function weaponKey(gen) {
    const D = SG.DuelGame;
    if (D && typeof D.lookOf === 'function') { try { return D.lookOf(gen).weapon; } catch (e) { /* 退回 */ } }
    return 'sword';
  }
  function classOf(gen) {
    const k = weaponKey(gen);
    return CLASS[k] || CLASS[String(k).replace(/[0-9_].*$/, '')] || 'one';
  }
  function fit(g, s, cls) {
    const w = W[cls || classOf(g)] || W.one, a = ((+g.war || 50) - (+g.intel || 50)) / 100;   // 勇（武高智低）偏重
    let v = 0;
    for (let i = 0; i < 7; i++) v += s[i] === 'H' ? w.H + 0.3 * a : w[s[i]];
    v += 2 * w.F[s[7]] + (s[7] === 'H' ? 0.3 * a : 0);
    let ch = 0; for (let i = 1; i < 8; i++) if (s[i] !== s[i - 1]) ch++;
    v += 0.05 * ((+g.intel || 50) / 100) * ch;                       // 智：多变
    let per = 0; for (let i = 2; i < 8; i++) if (s[i] === s[i - 2]) per++;
    v += 0.04 * ((+g.pol || 50) / 100) * per;                        // 政：规整
    return v + fnv(g.name + '|' + s) / 4294967296 * 0.9;           // 确定性抖动
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
  function finisherName(name, culture, cls, type, taken) {
    const pre = (CPRE[culture] || []).concat(PRE[cls] || PRE.one), suf = SUF[type];
    const N = pre.length * suf.length;
    let k = fnv(name) % N;
    for (let t = 0; t < N; t++, k = (k + 1) % N) { const nm = pre[Math.floor(k / suf.length)] + suf[k % suf.length]; if (!taken.has(nm)) return nm; }
    for (let i = 2; i < 99; i++) { const nm = String(name).slice(0, 1) + '家' + suf[0] + i; if (!taken.has(nm)) return nm; }
    return String(name) + suf[0];
  }

  // ---- §10.5 / §13.1 动作变体（只影响画面，不影响规则）----
  const VARIANTS = { L: ['l_cut', 'l_rise', 'l_jab', 'l_pommel'], M: ['m_diag', 'm_spin', 'm_thrust', 'm_bash'], H: ['h_over', 'h_rise', 'h_sweep', 'h_lunge'] };
  const FIN_VARIANTS = { pole: ['f_pierce', 'f_cleave', 'f_launch'], heavy: ['f_cleave', 'f_launch', 'f_grab'], one: ['f_pierce', 'f_flurry', 'f_launch'], dual: ['f_flurry', 'f_cleave'] };
  const ALL_FIN_VARIANTS = ['f_cleave', 'f_launch', 'f_pierce', 'f_flurry', 'f_grab', 'f_shot'];
  function variantsFor(name, seq, cls, finPin) {
    const out = [];
    let prev = '';
    for (let i = 0; i < 7; i++) {
      const vs = VARIANTS[seq[i]];
      let j = fnv(name + i) % vs.length;
      if (vs[j] === prev) j = (j + 1) % vs.length;
      out.push(vs[j]); prev = vs[j];
    }
    const fl = FIN_VARIANTS[cls] || FIN_VARIANTS.one;
    out.push(finPin || fl[fnv(name + 'f') % fl.length]);
    return out;
  }

  // ---- 表的解析 ----
  const cache = new Map();          // 姓名 → entry（表）
  const fallback = new Map();       // 姓名 → entry（本次运行的兜底，按首次查询的次序；不入存档）
  function table() { return (SG.ComboData && SG.ComboData.entries) || {}; }
  function mkEntry(name, seq, fin, vars, src) {
    const v = vars && vars.length === 8 ? vars : variantsFor(name, seq, 'one');
    return {
      name, seq,
      steps: Array.from({ length: 7 }, (_, i) => ({ type: seq[i], variant: v[i] })),
      finisher: { type: seq[7], variant: v[7], name: fin },
      kStr: kStr(seq), nH: nH(seq), src,
    };
  }
  function parse(name) {
    if (cache.has(name)) return cache.get(name);
    const raw0 = table()[name];
    if (typeof raw0 !== 'string') return null;
    const p = raw0.split('|');
    const e = mkEntry(name, p[0], p[1], (p[2] || '').split(','), p[3] === 'h' ? 'hand' : 'gen');
    cache.set(name, e);
    return e;
  }

  // ---- 运行时兜底（§10.4 第 5 步）：对冻结表 + 本次已发的兜底，取同势力海明距离 ≥ 2、未被占用、适配分最高的一串 ----
  let takenNames = null;
  function namesTaken() {
    if (takenNames) return takenNames;
    takenNames = new Set();
    for (const n in table()) { const p = table()[n].split('|'); takenNames.add(p[1]); }
    try { for (const t of (SG.Defs && SG.Defs.tactics) || []) takenNames.add(t.name); } catch (e) { /* 无 */ }
    try { if (SG.Specials && SG.Specials.all) for (const sp of SG.Specials.all()) takenNames.add(sp.name); } catch (e) { /* 无 */ }
    try { if (SG.Tactics && SG.Tactics.all) for (const t of SG.Tactics.all()) takenNames.add(t.name); } catch (e) { /* 无 */ }
    return takenNames;
  }
  function cultureOf(gen) {
    if (gen && gen.culture) return SG.cultureBase ? SG.cultureBase(gen.culture) : gen.culture;
    try { if (SG.WorldData && SG.WorldData.cultureOfGeneral) return SG.WorldData.cultureOfGeneral(gen.name) || 'han'; } catch (e) { /* 无 */ }
    return 'han';
  }
  function factionSeqs(gen) {
    const out = [];
    const G = SG.G;
    if (!G || !G.generals || !(gen.faction >= 0)) return out;
    for (const o of G.generals) if (o !== gen && o.faction === gen.faction && o.name !== gen.name) { const e = parse(o.name) || fallback.get(o.name); if (e) out.push(e.seq); }
    return out;
  }
  function makeFallback(gen) {
    const used = new Set();
    for (const n in table()) used.add(table()[n].split('|')[0]);
    for (const e of fallback.values()) used.add(e.seq);
    const cls = classOf(gen), fac = factionSeqs(gen);
    const cand = legalSet().filter(s => !used.has(s)).map(s => [fit(gen, s, cls), s]).sort((a, b) => b[0] - a[0] || (a[1] < b[1] ? -1 : 1));
    let pick = null;
    for (const [, s] of cand) if (fac.every(t => hamming(t, s) >= 2)) { pick = s; break; }
    if (!pick) pick = cand.length ? cand[0][1] : 'LMLMHLMH';   // 790 串用尽（不会发生）：任取一串合法的
    const taken = namesTaken();
    const fin = finisherName(gen.name, cultureOf(gen), cls, pick[7], taken);
    taken.add(fin);
    return mkEntry(gen.name, pick, fin, variantsFor(gen.name, pick, cls), 'fallback');
  }

  function of(gen) {
    if (!gen) return null;
    const name = typeof gen === 'string' ? gen : gen.name;
    const e = parse(name);
    if (e) return e;
    if (fallback.has(name)) return fallback.get(name);
    const g = typeof gen === 'string' ? { name, war: 50, intel: 50, pol: 50 } : gen;
    const f = makeFallback(g);
    fallback.set(name, f);
    return f;
  }
  function all() { return Object.keys(table()).map(parse); }
  function has(name) { return typeof table()[name] === 'string'; }

  // ---- 字形（§8.8 / §12.1）：轻 ● 青、中 ◆ 琥珀、重 ▲ 朱；键位随预设（v4：J K L；v3 习惯：J L K）----
  const GLYPH = { L: '●', M: '◆', H: '▲' };
  const KEYS = { v4: { L: 'J', M: 'K', H: 'L' }, v3: { L: 'J', M: 'L', H: 'K' }, touch: { L: '轻', M: '中', H: '重' } };
  const COLOR = { L: '#5fb3a8', M: '#e0a24a', H: '#d2553a' };
  function glyphs(seq, preset) {
    const k = KEYS[preset] || KEYS.v4;
    return String(seq || '').split('').map(t => ({ type: t, glyph: GLYPH[t], key: k[t], color: COLOR[t] }));
  }
  function glyphText(seq) { const g = String(seq).split('').map(t => GLYPH[t]); return g.slice(0, 7).join(' ') + ' · ' + g[7]; }

  // ---- 校验（tests/combos.js 与 tools/gen-combos.js check）----
  function validate() {
    const problems = [], T = table(), names = Object.keys(T);
    const seqs = new Map(), fins = new Map();
    let kMin = 9, kMax = 0;
    const taken = new Set();
    try { for (const t of (SG.Defs && SG.Defs.tactics) || []) taken.add(t.name); } catch (e) { /* 无 */ }
    try { if (SG.Specials && SG.Specials.all) for (const sp of SG.Specials.all()) taken.add(sp.name); } catch (e) { /* 无 */ }
    try { if (SG.Tactics && SG.Tactics.all) for (const t of SG.Tactics.all()) taken.add(t.name); } catch (e) { /* 无 */ }
    const hand = [];
    for (const n of names) {
      const e = parse(n);
      const why = legal(e.seq);
      if (why) problems.push(`${n} ${e.seq}: ${why}`);
      if (seqs.has(e.seq)) problems.push(`R1 ${n} and ${seqs.get(e.seq)} share ${e.seq}`); else seqs.set(e.seq, n);
      if (!e.finisher.name) problems.push(`${n}: no finisher name`);
      if (fins.has(e.finisher.name)) problems.push(`R4 finisher name ${e.finisher.name} used by ${n} and ${fins.get(e.finisher.name)}`); else fins.set(e.finisher.name, n);
      if (taken.has(e.finisher.name)) problems.push(`R4 finisher name ${e.finisher.name} (${n}) equals a special / tactic`);
      for (let i = 0; i < 7; i++) if (VARIANTS[e.seq[i]].indexOf(e.steps[i].variant) < 0) problems.push(`${n}: step ${i + 1} variant ${e.steps[i].variant} not a ${e.seq[i]} variant`);
      for (let i = 1; i < 7; i++) if (e.steps[i].variant === e.steps[i - 1].variant) problems.push(`${n}: adjacent steps ${i}/${i + 1} share variant ${e.steps[i].variant}`);
      if (ALL_FIN_VARIANTS.indexOf(e.finisher.variant) < 0) problems.push(`${n}: finisher variant ${e.finisher.variant}`);
      kMin = Math.min(kMin, e.kStr); kMax = Math.max(kMax, e.kStr);
      if (e.src === 'hand') hand.push(e);
    }
    for (let i = 0; i < hand.length; i++) for (let j = i + 1; j < hand.length; j++) if (hamming(hand[i].seq, hand[j].seq) < 2) problems.push(`R3 hand-made ${hand[i].name} / ${hand[j].name} differ by one press`);
    // R2：开局同势力（任一剧本）两两海明距离 ≥ 2
    const byFac = {};
    try {
      for (const sc of Object.values(SG.Scenarios || {})) for (const l of sc.Generals || []) {
        const p = l.split('|'); if (!p[4] || p[4] === '-' || !T[p[0]]) continue;
        (byFac[p[4]] = byFac[p[4]] || new Set()).add(p[0]);
      }
    } catch (e) { /* 无剧本数据 */ }
    let r2 = 0;
    for (const f in byFac) {
      const ns = [...byFac[f]];
      for (let i = 0; i < ns.length; i++) for (let j = i + 1; j < ns.length; j++) if (hamming(parse(ns[i]).seq, parse(ns[j]).seq) < 2) { r2++; problems.push(`R2 faction ${f}: ${ns[i]} / ${ns[j]} differ by one press`); }
    }
    return { ok: problems.length === 0, problems, n: names.length, kStrRange: [kMin, kMax], legal: legalSet().length, spare: legalSet().length - seqs.size, factions: Object.keys(byFac).length, r2 };
  }

  // ---- 见识与熟练（§12.2、§12.4；存档 G.combos）----
  function store(G) {
    if (!G) return null;
    if (!G.combos || typeof G.combos !== 'object') G.combos = { known: {}, mastery: {} };
    if (!G.combos.known) G.combos.known = {};
    if (!G.combos.mastery) G.combos.mastery = {};
    return G.combos;
  }
  function genByName(G, name) { return G && G.generals ? G.generals.find(g => g.name === name) : null; }
  function known(G, name, viewerFaction) {
    G = G || SG.G;
    const vf = viewerFaction !== undefined ? viewerFaction : G ? G.player : -1;
    const g = genByName(G, name);
    if (g && vf >= 0 && g.faction === vf) return 255;
    const st = G && G.combos && G.combos.known;
    return st ? (st[name] | 0) & 255 : 0;
  }
  // 见识（§12.2）：本势力武将恒为全知、不入存档（§16.3「本势力武将不存」）；markAll（俘获 / 登用）照存，日后离去也仍记得
  function markSeen(G, name, step, isMask, force) {
    G = G || SG.G;
    if (!force) { const g = genByName(G, name); if (g && G && typeof G.player === 'number' && G.player >= 0 && g.faction === G.player) return 255; }
    const st = store(G); if (!st) return 0;
    const bit = isMask ? (step & 255) : (step >= 0 && step < 8 ? 1 << step : 0);
    if (!bit) return st.known[name] | 0;
    st.known[name] = ((st.known[name] | 0) | bit) & 255;
    return st.known[name];
  }
  function markAll(G, name) { return markSeen(G || SG.G, name, 255, true, true); }
  function mastery(G, name) {
    G = G || SG.G;
    const m = G && G.combos && G.combos.mastery && G.combos.mastery[name];
    return m ? Object.assign({ s: 0, done: 0, onBeat: 0, beatPract: 0 }, m) : { s: 0, done: 0, onBeat: 0, beatPract: 0 };
  }
  // 熟练星（装饰，不加数值）：★ 命中打完一整串 · ★★ 全程踩准打完 5 次 · ★★★ 对「看破」打完（第 4 步迟按 / 迟滞）
  function addMastery(G, name, r) {
    G = G || SG.G;
    const st = store(G); if (!st || !r || !r.complete) return mastery(G, name);
    const m = mastery(G, name);
    m.done++;
    if (r.allOnBeat) { m.onBeat++; if (r.practice) m.beatPract++; }
    let s = m.s;
    if (s < 1) s = 1;
    if (s < 2 && m.onBeat >= 5) s = 2;
    if (s < 3 && r.vsKanpa && s >= 2) s = 3;
    m.s = s;
    st.mastery[name] = m;
    return m;
  }

  // ---- 人物菜单「武艺」页（§12.1）：招式谱、每步伤害占比、安全标签、合计、熟练星；未见识的步显示「？」----
  function describe(gen, viewerFaction, G) {
    G = G || SG.G;
    const e = of(gen);
    if (!e) return null;
    const name = e.name, mask = G ? known(G, name, viewerFaction) : 255;
    const war = (gen && +gen.war) || 76;
    const D = SG.DuelGame;
    const pow = D && D.WEAPONS && D.lookOf ? ((D.WEAPONS[D.lookOf(typeof gen === 'string' ? { name } : gen).weapon] || {}).pow || 1) : 1;
    const dmgAt = (base, n) => base * e.kStr * pow * (0.55 + war / 100) * (n >= 8 ? Math.max(FIN_FLOOR, scaleOf(n)) : scaleOf(n));
    const steps = [];
    let tot = 0;
    for (let i = 0; i < 8; i++) {
      const t = e.seq[i], fin = i === 7, base = fin ? FIN[t] : BASE[t];
      const d = dmgAt(base, i + 1);
      const isKnown = !!(mask & (1 << i));
      const tags = [];
      if (!fin) tags.push(t === 'L' ? '安全' : t === 'M' ? '小破绽' : '大破绽');
      if (t === 'H') tags.push('可迟滞');
      if (i >= 2) tags.push('可被看破');
      steps.push({ i: i + 1, type: isKnown ? t : '?', glyph: isKnown ? GLYPH[t] : '？', known: isKnown, dmgPct: isKnown ? Math.round(d) : null, tags: isKnown ? tags : [],
        variant: isKnown ? (fin ? e.finisher.variant : e.steps[i].variant) : null });
      if (isKnown) tot += d;
    }
    // 用时：第一次按键 → 终结技打中（基准时钟；实际 ÷ 出招速度）
    let time = null;
    const ST = D && D.STEP;
    if (ST && D.MOVES) {
      const allKnown = mask === 255;
      if (allKnown) {
        time = 0;
        for (let i = 0; i < 7; i++) { const mv = i === 0 ? D.MOVES[e.seq[0]] : ST[e.seq[i]]; time += mv.startup + 0.6 * mv.active; }
        time += ST['F' + e.seq[7]].startup;
      }
    }
    return {
      name, seq: mask === 255 ? e.seq : e.seq.split('').map((t, i) => (mask & (1 << i)) ? t : '?').join(''),
      glyphs: steps.map(s => s.glyph), steps,
      finisher: { name: mask & 128 ? e.finisher.name : '？', type: mask & 128 ? e.finisher.type : '?', known: !!(mask & 128) },
      totals: { dmgPct: Math.round(tot), allKnown: mask === 255, time, finCrit: 0.10 },
      stars: G ? mastery(G, name).s : 0, mask, src: e.src,
      hint: mask === 255 ? '' : '在单挑中见识过的招式会记下来',
    };
  }

  const Combos = {
    BASE, FIN, SCALE, FIN_FLOOR, SPECIAL_FLOOR, VARIANTS, FIN_VARIANTS, ALL_FIN_VARIANTS, CLASS, W, PRE, CPRE, SUF, GLYPH, COLOR, KEYS,
    of, all, has, raw, nH, kStr, legal, legalSet, hamming, fit, classOf, weaponKey, fnv, finisherName, variantsFor, cultureOf, scaleOf,
    glyphs, glyphText, validate, known, markSeen, markAll, mastery, addMastery, describe,
    // 测试用：清空本次运行的兜底与缓存（表被替换后）
    _reset() { cache.clear(); fallback.clear(); takenNames = null; },
    _fallbacks: fallback,
    // ---- 存档 v4（INTEGRATION §6.1–§6.2；SG.MigrateV4 自动发现）----
    enabled: true,
    saveKeys: ['combos'],
    // 见识与熟练是懒建的（第一次 markSeen / addMastery 时才有 G.combos）：新游戏与没见识过任何招式谱的存档不带此键（缺省即 0，§12.2）
    migrate(G) { if (G && G.combos) store(G); },
    init(G) { if (G && G.combos) store(G); },
  };
  SG.Combos = Combos;

  // §12.2「玩家俘获或登用的武将从此全知」：挂在规则层事件上（js/registry.js SG.Events；没有事件总线时——如 Node 的单挑测试——不挂）。
  //   'captive.taken' (s, gen)：俘获者 = 出征双方中不是他原属的一方；'command' (cmd, res)：登用 hire、劝诱 persuade 成功且为玩家势力。
  //   markAll 照存进 G.combos（日后离去也记得）；监听者不取随机数（INTEGRATION §1.2）
  let hooked = false;
  function hookEvents() {
    const E = SG.Events;
    if (hooked || !E || typeof E.on !== 'function') return;
    hooked = true;
    const playerOf = G => (G && typeof G.player === 'number' && G.player >= 0 ? G.player : -1);
    E.on('captive.taken', (s, gen) => {
      try {
        const G = SG.G, pf = playerOf(G);
        if (pf < 0 || !s || !gen || !gen.name) return;
        const captor = gen.faction === s.attacker ? s.defender : s.attacker;
        if (captor === pf) markAll(G, gen.name);
      } catch (e) { console.error(e); }
    });
    E.on('command', (cmd, res) => {
      try {
        if ((cmd !== 'hire' && cmd !== 'persuade') || !res || !res.ok) return;
        const G = SG.G, pf = playerOf(G);
        if (pf < 0 || res.f !== pf || res.target === undefined || res.target === null) return;
        const g = G.generals && (G.generals[res.target] && G.generals[res.target].id === res.target ? G.generals[res.target] : G.generals.find(x => x.id === res.target));
        if (g && g.name) markAll(G, g.name);
      } catch (e) { console.error(e); }
    });
  }
  hookEvents();
  Combos.hookEvents = hookEvents;
})();
