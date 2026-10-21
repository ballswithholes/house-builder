'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 武林大会的画面（UI-V4 §12；内容与规则归 TOURNAMENT：SG.Tourney / SG.TourneyData）
   英雄帖（正月诏告，§12.1）· 选派出战者（五面势力旗，§12.2）· 签表（三轮一窗、左右对称、决赛居中、全图缩略、下一场，§12.3）·
   对阵（赛场背景、怒容立绘、巨大的朱笔「对」，§12.4）· 颁奖（三级奖台依次升起、「天下第一」朱印，§12.6）· 英雄榜（§12.8）。
   手机（html.sg-short）：签表两轮一窗（八强 → 决赛）+ 236 px 的下一场；选将时旗位在上方一行；对阵立绘 120 px；颁奖缩至 0.62。

   武林大会的规则模块（W4：tourney-data.js / tourney.js）尚未载入时，画面照样能打开：preview(view) 用当前局面生成一份
   示意赛事（各势力武力 ≥ 72 的武将，按武力定种子，标准签位，胜负用 winChance 与固定种子的随机数，不碰 SG.Random），
   供测试（SG.UITest.open('tourney', { view })）与 tests/ui-v4.html 检查版式。

   SG.TourneyScreen.announce(t) → Promise<'enter'|'later'|'skip'>
   SG.TourneyScreen.entry(t) → Promise<[{ gen, mode }] | null | 'later'>   null = 不参加；'later' = 稍后（案头留签）；
                                    mode 'play' 亲自 | 'watch' 观战 | 'auto' 自动；「全部自动」时全为 'auto' 且数组带 allAuto = true（TOURNAMENT §4.3）
   SG.TourneyScreen.bracket(t, { round }) → Promise<'play'|'watch'|'result'|null>   下一场的选择（null = 收起）
   SG.TourneyScreen.match(m, t) → Promise<'play'|'watch'|'result'>
   SG.TourneyScreen.podium(t) → Promise                            SG.TourneyScreen.hall() → Promise
   SG.TourneyScreen.preview(view) → Promise    view: 'announce' | 'entry' | 'bracket' | 'mine' | 'match' | 'podium' | 'hall'
   SG.TourneyScreen.model(t) → 画面用的赛事（t = SG.Tourney 的 T；缺省 = 示意赛事）     SG.TourneyScreen.isOpen()
   t 的字段按 TOURNAMENT §10.2：{ year, n（参赛人数）, host, city（城 id 或 key）, entrants[], S, slots[], rounds[], round }；
   届数 = G.tourney.count + 1（或 t.edition）；读不到的字段一律从缺省补上。奖金、声望、门槛等数字读 SG.TourneyData，
   未载入时读本文件的 FALLBACK（同一份数字只写在那里）。全屏场景（对阵、颁奖）也进 SG.UI 的模态栈：快捷键与「返回」手势不会落到地图上。
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});
  const esc = s => SG.esc(String(s === undefined || s === null ? '' : s));
  const UI = () => SG.UI;
  const G = () => SG.G;
  const icon = (id, size) => (SG.UIIcons ? SG.UIIcons.svg(id, size) : '');
  const n0 = v => Math.round(v || 0).toLocaleString('en-US');
  const sfx = (n, v) => { try { if (SG.UI && SG.UI.sfx) SG.UI.sfx(n, v); } catch (e) { /* 无音频 */ } };
  const reduced = () => !!(SG.Motion && (SG.Motion.reduced || SG.Motion.level === 'off'));
  const short = () => document.documentElement.classList.contains('sg-short');
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html !== undefined && html !== null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  const CN = '〇一二三四五六七八九';
  // 一 · 十五 · 六十四 · 一百 · 一百零五 · 一百一十二 · 一百二十八（届数、人数、冠数）
  function cnNum(n) {
    n = Math.max(0, n | 0);
    if (n < 10) return CN[n];
    if (n < 20) return '十' + (n % 10 ? CN[n % 10] : '');
    if (n < 100) return CN[(n / 10) | 0] + '十' + (n % 10 ? CN[n % 10] : '');
    if (n < 1000) { const r = n % 100; return CN[(n / 100) | 0] + '百' + (r === 0 ? '' : r < 10 ? '零' + CN[r] : r < 20 ? '一' + cnNum(r) : cnNum(r)); }
    return String(n);
  }
  const cnCount = cnNum;
  function hash32(...parts) { let x = 2166136261 >>> 0; for (const p of parts) { const s = String(p); for (let i = 0; i < s.length; i++) { x ^= s.charCodeAt(i); x = Math.imul(x, 16777619) >>> 0; } x ^= 0x9e3779b9; } return x >>> 0; }
  function rng(seed) { let s = (seed >>> 0) || 1; return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return (s & 0xffffff) / 16777216; }; }
  // 规则模块（SG.TourneyData，TOURNAMENT §10.1）未载入时的缺省——画面用到的数字只写在这里；载入后一律读 SG.TourneyData
  const FALLBACK = {
    MAX_PER_FACTION: 5, MIN_WAR: 60, AI_WAR: { classic: 72, world: 75 }, WIN_SCALE: 6,
    SEED: { level: 0.6, title: 1.5, titleCap: 3, reigning: 10, drillHall: 5 },
    PRIZES: [
      { place: 1, gold: 400, prestige: 50, item: 'rare', skillP: 0.25 },
      { place: 2, gold: 200, prestige: 25, item: 'uncommon', offers: 3, skillP: 0.15 },
      { place: 3, gold: 100, prestige: 12, item: 'common', offers: 3, skillP: 0 },
    ],
    PREDICT: { opensAt: 16 },
    SPONSOR: [{ id: 'small', gold: 100 }, { id: 'mid', gold: 300 }, { id: 'name', gold: 800 }],
    ROUND_NAMES: { playIn: '预赛', first: '首轮', 32: '三十二强', 16: '十六强', 8: '八强', 4: '四强', 2: '决赛', third: '季军战' },
  };
  const td = k => (SG.TourneyData && SG.TourneyData[k] !== undefined ? SG.TourneyData[k] : FALLBACK[k]);
  // 只给示意赛事（规则模块未载入）用的名字与件数
  const DEMO = {
    count: 3, reignTitles: 2, venueLuoyang: '平乐观', venueOther: '万国擂台', relic: '金刚杵', relicAt: 3,
    prize1: { name: '越女剑', rarity: 'rare', slot: 'weapon' }, prize2: ['鱼鳞甲', 'uncommon', 'armour'], prize3: ['西凉毡盔', 'common', 'helmet'],
  };
  const WIN_SCALE = () => td('WIN_SCALE') || 6;
  const winChance = (a, b) => (SG.Tourney && SG.Tourney.winChance ? SG.Tourney.winChance(a.gen || a, b.gen || b) : 1 / (1 + Math.exp(-((a.war | 0) - (b.war | 0)) / WIN_SCALE())));
  const RN = () => td('ROUND_NAMES');
  // 会址：城 id、key（'luoyang'）、名字或城对象 → 城名
  function cityName(c, g) {
    if (c === undefined || c === null || c === '') return '';
    if (typeof c === 'object' && c.name) return c.name;
    if (g && g.cities) {
      if (typeof c === 'number') return g.cities[c] ? g.cities[c].name : '';
      const x = g.cities.find(y => y.key === c || y.name === c);
      if (x) return x.name;
    }
    return String(c);
  }
  const venueOf = city => (/洛阳/.test(city) ? DEMO.venueLuoyang : DEMO.venueOther);

  // ================================================================ 画面用的赛事 ==
  function duelWar(gen) { try { if (SG.Gear && SG.Items && SG.Items.enabled !== false && G() && G().items) return SG.Gear.duelWar(gen) | 0; } catch (e) { /* 无装备 */ } return gen.war | 0; }
  function levelOf(gen) { try { return SG.Progress && SG.Progress.expInfo ? SG.Progress.expInfo(gen).level : 1; } catch (e) { return 1; } }
  function weaponOf(gen) {
    try { if (SG.Gear && SG.Items && SG.Items.enabled !== false && G().items) { const it = SG.Gear.item(gen, 'weapon'); if (it) return { name: SG.Items.displayName ? SG.Items.displayName(it) : it.name, rarity: it.rarity }; } } catch (e) { /* 无装备 */ }
    return null;
  }
  // 招式谱的字形（● 轻 · ◆ 中 · ▲ 重），对手未见识的步写「？」
  function glyphsOf(gen) {
    try {
      if (SG.Combos && SG.Combos.describe) { const d = SG.Combos.describe(gen, G() ? G().player : undefined, G()); if (d && d.glyphs) return d.glyphs.slice(0, 7).join('') + '·' + d.glyphs[7]; }
      if (SG.Combos && SG.Combos.of) { const c = SG.Combos.of(gen); if (c && c.seq) return SG.Combos.glyphText(c.seq).replace(/ /g, ''); }
    } catch (e) { /* 无连段 */ }
    return '';
  }
  function entryOf(e, g) {
    const gen = e.gen || e;
    const f = gen.faction >= 0 && g && g.factions[gen.faction] ? g.factions[gen.faction] : null;
    const ru = f && g.ruler ? g.ruler(f.id) : null;
    return {
      gen, name: gen.name, war: e.war !== undefined ? e.war : duelWar(gen), level: levelOf(gen), seed: e.seed || 0,
      fc: f ? f.color : '#8a6a48', fname: f ? (ru && f.name === ru.name ? f.name + '军' : f.name) : '在野', fid: f ? f.id : -1,
      mine: !!(g && gen.faction === g.player), mode: e.mode || null, injured: !!(gen.injury && g && gen.injury.until > g.year * 12 + g.month),
    };
  }
  // 种子分（TOURNAMENT §4.2）：规则模块的 seedScore；未载入时按同一公式（讲武堂一项要 ENGAGEMENT，缺省不计）
  function seedScore(e) {
    const g = G();
    if (SG.Tourney && SG.Tourney.seedScore) { try { return +SG.Tourney.seedScore(g, e.gen) || 0; } catch (err) { /* 退回公式 */ } }
    const S = td('SEED'), T = g && g.tourney ? g.tourney : null;
    const titles = T && T.wins ? T.wins[e.name] | 0 : 0;
    return (e.war | 0) + S.level * (e.level | 0) + S.title * Math.min(S.titleCap, titles) + (T && T.reigning === e.name ? S.reigning : 0);
  }
  const bySeed = (a, b) => seedScore(b) - seedScore(a) || ((a.gen.id | 0) - (b.gen.id | 0));
  // 标准签位：order(2) = [1, 2]；order(2n) = 对 order(n) 的每个 x：x, 2n + 1 − x
  function order(S) { let o = [1, 2]; while (o.length < S) { const n2 = o.length * 2; const nx = []; for (const x of o) nx.push(x, n2 + 1 - x); o = nx; } return o; }
  function roundName(S, players, idx, matches) {
    const R = RN();
    if (players === 2) return R[2];
    if (idx === 0 && S >= 16 && matches.filter(m => m.a && m.b).length < S / 8) return R.playIn;
    if (R[players] && players <= 32) return R[players];
    return R.first;
  }
  // 示意赛事（规则模块未载入时）：各势力至多 5 人（我方 ≥ 60，电脑 ≥ 72）；按武力定种子；比到 upto 人那一轮
  function demoModel(upto) {
    const g = G();
    const year = g ? g.year : 191, host = g ? g.factions.find(f => f.alive && g.cities.some(c => c.owner === f.id && /洛阳/.test(c.name))) || g.factions.find(f => f.alive) : null;
    const pool = [];
    if (g) for (const f of g.factions) {
      if (!f.alive) continue;
      const min = f.id === g.player ? 60 : 72;
      const list = g.generalsOf(f.id).filter(x => !x.dead && (x.war | 0) >= min).sort((a, b) => duelWar(b) - duelWar(a) || (a.name < b.name ? -1 : 1)).slice(0, 5);
      for (const x of list) pool.push(entryOf({ gen: x }, g));
    }
    pool.sort((a, b) => b.war - a.war || (a.name < b.name ? -1 : 1));
    pool.forEach((e, i) => { e.seed = i + 1; });
    const N = pool.length;
    const S = Math.max(8, Math.min(128, 1 << Math.ceil(Math.log2(Math.max(2, N)))));
    const ord = order(S);
    const slots = ord.map(r => (r <= N ? pool[r - 1] : null));
    const r0 = rng(hash32('tourney-demo', year, N));
    const rounds = [];
    let cur = [];
    for (let i = 0; i < S / 2; i++) cur.push({ a: slots[2 * i], b: slots[2 * i + 1] });
    let ri = 0;
    const stopAt = upto || 16;
    let current = -1;
    for (let players = S; players >= 2; players /= 2, ri++) {
      const ms = cur.map((p, j) => ({ id: ri + ':' + j, a: p.a, b: p.b, w: null, bye: !(p.a && p.b) }));
      const rd = { players, matches: ms, name: '' };
      rd.name = roundName(S, players, ri, ms);
      rounds.push(rd);
      const resolve = players > stopAt;
      for (const m of ms) {
        if (!m.a && !m.b) { m.w = null; continue; }
        if (m.bye) { m.w = m.a ? 0 : 1; continue; }
        if (resolve) {
          const p = winChance(m.a, m.b);
          m.w = r0() < p ? 0 : 1;
          const fin = ['终结技', '会心', '看破', '判定'][(r0() * 4) | 0];
          m.how = '第 ' + (1 + ((r0() * 3) | 0)) + ' 合 · ' + fin;
        }
      }
      if (!resolve && current < 0) current = ri;
      if (players === 2) break;
      const nx = [];
      for (let j = 0; j < ms.length; j += 2) {
        const wa = ms[j].w === null ? null : ms[j].w === 0 ? ms[j].a : ms[j].b;
        const wb = ms[j + 1].w === null ? null : ms[j + 1].w === 0 ? ms[j + 1].a : ms[j + 1].b;
        nx.push({ a: resolve ? wa : null, b: resolve ? wb : null });
      }
      cur = nx;
    }
    if (current < 0) current = rounds.length - 1;
    const rd = rounds[current];
    const next = rd.matches.find(m => m.a && m.b && m.w === null && (m.a.mine || m.b.mine)) || rd.matches.find(m => m.a && m.b && m.w === null) || null;
    if (next) next.now = true;
    const log = [];
    for (const m of (rounds[current - 1] || { matches: [] }).matches) {
      if (m.bye || m.w === null) continue;
      const w = m.w === 0 ? m.a : m.b, l = m.w === 0 ? m.b : m.a;
      if (log.length < 4 && (w.seed <= 4 || w.mine || l.mine)) log.push(w.name + ' 胜 ' + l.name + '（' + m.how + '）');
    }
    const cityName = g ? ((g.cities.find(c => /洛阳/.test(c.name)) || g.cities[0]).name) : '洛阳';
    const ru = host && g ? g.ruler(host.id) : null;
    // 奖池按 TOURNAMENT §7.2：没有专属兵器（availability.kind === 'ruler'）、不是绑定物
    const items = SG.Items && SG.Items.all ? SG.Items.all().filter(it => it.rarity === 'rare' && it.slot === 'weapon' && !it.bound && !(it.availability && Object.values(it.availability).some(a => a && a.kind === 'ruler'))) : [];
    const prize = items.length ? items[(hash32('prize', year) % items.length)] : DEMO.prize1;
    const count = g && g.tourney && g.tourney.count ? g.tourney.count : DEMO.count;
    return {
      demo: true, n: count + 1, year, month: 1, city: cityName, venue: venueOf(cityName), hostName: ru ? ru.name : '董卓', hostColor: host ? host.color : '#7a1e2c',
      N, S, rounds, cur: current, next, log, entrants: pool, mine: pool.filter(e => e.mine), prize: { name: SG.Items && SG.Items.displayName && prize.id ? SG.Items.displayName(prize) : prize.name, rarity: prize.rarity || 'rare', slot: prize.slot || 'weapon', id: prize.id },
      reigning: pool[0] ? { name: pool[0].name, titles: DEMO.reignTitles } : null,
    };
  }
  // SG.Tourney 的 T → 画面用的赛事（字段缺失时按示意赛事补）
  function fromT(T) {
    const g = G();
    if (!T || !T.rounds) return demoModel();
    const E = new Map();
    const ent = x => { if (!x) return null; const k = (x.gen || x).name; if (!E.has(k)) E.set(k, entryOf(x, g)); return E.get(k); };
    const S = T.S || 64;
    const rounds = T.rounds.map((r, ri) => {
      const ms = (r.matches || r).map((m, j) => {
        const a = ent(m.a), b = ent(m.b);
        const w = m.winner ? ((m.winner.gen || m.winner).name === (a && a.name) ? 0 : 1) : null;
        return { id: m.id || ri + ':' + j, a, b, w, bye: !(a && b), how: m.how || '', games: m.games || [] };
      });
      const players = ms.length * 2;
      return { players, matches: ms, name: r.name || (SG.Tourney && SG.Tourney.roundName ? SG.Tourney.roundName(T, ri) : roundName(S, players, ri, ms)) };
    });
    const cur = Math.max(0, Math.min(rounds.length - 1, T.round | 0));
    const next = rounds[cur].matches.find(m => m.a && m.b && m.w === null && (m.a.mine || m.b.mine)) || rounds[cur].matches.find(m => m.a && m.b && m.w === null) || null;
    if (next) next.now = true;
    const host = T.host >= 0 && g ? g.factions[T.host] : null;
    const ru = host ? g.ruler(host.id) : null;
    const entrants = (T.entrants || []).map(ent);
    // 届数：G.tourney.count + 1（UI-V4 §12.1；T.n 是参赛人数，不是届数）；会址：城 id 或 key（FIRST_HOST_CITY 用 key）
    const edition = T.edition || (g && g.tourney ? (g.tourney.count | 0) + 1 : 1);
    const city = cityName(T.city, g) || (ru && g.cities[ru.city] ? g.cities[ru.city].name : '');
    return { demo: false, n: edition, year: T.year || (g ? g.year : 191), month: 1, city, venue: T.venue || venueOf(city), hostName: ru ? ru.name : '', hostColor: host ? host.color : '#7a1e2c',
      N: entrants.length || T.n || 0, S, rounds, cur, next, log: T.log || [], entrants, mine: entrants.filter(e => e && e.mine),
      prize: T.prize || null, reigning: g && g.tourney && g.tourney.reigning ? { name: g.tourney.reigning, titles: (g.tourney.wins || {})[g.tourney.reigning] || 1 } : null };
  }
  function model(t) { return t && t.rounds && !t.demo ? fromT(t) : t && t.demo ? t : demoModel(); }

  // ================================================================ 赛场背景（UI-V4 §12.4；样张 mockups/10-tourney.html 的 arena()）==
  // 平乐观大坛与「十二重五采华盖」、四周各地地标的立面剪影、彩旗、环形看台与人群；装饰随机数用固定种子
  function stadium(cv, opts) {
    opts = opts || {};
    const W = cv.clientWidth || innerWidth, H = cv.clientHeight || innerHeight, dpr = Math.min(2, devicePixelRatio || 1);
    cv.width = Math.round(W * dpr); cv.height = Math.round(H * dpr);
    const g = cv.getContext('2d');
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    const r = rng(4);
    const sky = g.createLinearGradient(0, 0, 0, H);
    if (opts.night) { sky.addColorStop(0, '#1f2850'); sky.addColorStop(0.55, '#4a3a5a'); sky.addColorStop(1, '#7a5c48'); }
    else { sky.addColorStop(0, '#f4c98a'); sky.addColorStop(0.55, '#f7dfb2'); sky.addColorStop(1, '#c9a874'); }
    g.fillStyle = sky; g.fillRect(0, 0, W, H);
    const ink = '#241a21', y0 = H * 0.5;
    g.lineJoin = 'round';
    const fac = (x, w, hh, kind, col) => {
      g.fillStyle = col; g.strokeStyle = ink; g.lineWidth = 2;
      if (kind === 'arch') { g.fillRect(x, y0 - hh, w, hh); g.strokeRect(x, y0 - hh, w, hh); g.fillStyle = 'rgba(36,26,33,.5)'; for (let i = 0; i < 4; i++) { const cx = x + w * (i + 0.5) / 4; g.beginPath(); g.arc(cx, y0 - hh * 0.35, w / 11, Math.PI, 0); g.lineTo(cx + w / 11, y0); g.lineTo(cx - w / 11, y0); g.fill(); } }
      if (kind === 'stupa') { g.beginPath(); g.arc(x + w / 2, y0 - hh * 0.3, w / 2, Math.PI, 0); g.fill(); g.stroke(); g.fillRect(x + w * 0.44, y0 - hh, w * 0.12, hh * 0.4); g.strokeRect(x + w * 0.44, y0 - hh, w * 0.12, hh * 0.4); }
      if (kind === 'que') { g.fillRect(x, y0 - hh, w * 0.35, hh); g.strokeRect(x, y0 - hh, w * 0.35, hh); g.fillRect(x + w * 0.65, y0 - hh, w * 0.35, hh); g.strokeRect(x + w * 0.65, y0 - hh, w * 0.35, hh); g.fillStyle = '#808da6'; g.fillRect(x - 4, y0 - hh - 10, w * 0.35 + 8, 10); g.fillRect(x + w * 0.65 - 4, y0 - hh - 10, w * 0.35 + 8, 10); }
      if (kind === 'gate') { g.fillRect(x, y0 - hh, w, hh); g.strokeRect(x, y0 - hh, w, hh); g.fillStyle = ink; g.fillRect(x + w * 0.35, y0 - hh * 0.7, w * 0.3, hh * 0.7); }
    };
    fac(W * 0.04, W * 0.14, H * 0.17, 'arch', '#d9c7a4'); fac(W * 0.2, W * 0.07, H * 0.2, 'stupa', '#e6d2a6'); fac(W * 0.72, W * 0.1, H * 0.2, 'gate', '#c9b28a'); fac(W * 0.86, W * 0.1, H * 0.16, 'que', '#d6c09a');
    const cx = W / 2, by = y0;
    g.strokeStyle = ink; g.lineWidth = 2;
    for (let i = 0; i < 3; i++) { const w = W * (0.2 - i * 0.04), y = by - i * 14; g.fillStyle = ['#c9b28a', '#d9c39a', '#e6d3ad'][i]; g.fillRect(cx - w / 2, y - 14, w, 14); g.strokeRect(cx - w / 2, y - 14, w, 14); }
    g.beginPath(); g.moveTo(cx, by - 42); g.lineTo(cx, by - H * 0.42); g.lineWidth = 4; g.stroke(); g.lineWidth = 2;
    const cols = ['#c8382c', '#e8b54a', '#3f8f5f', '#3e6aa8', '#f3e7cc'];
    for (let i = 0; i < 12; i++) {
      const y = by - H * 0.42 + i * H * 0.028, w = W * (0.03 + i * 0.006);
      g.fillStyle = cols[i % 5]; g.beginPath(); g.moveTo(cx - w, y + H * 0.022); g.quadraticCurveTo(cx, y - H * 0.012, cx + w, y + H * 0.022); g.lineTo(cx + w * 0.9, y + H * 0.028); g.lineTo(cx - w * 0.9, y + H * 0.028); g.closePath(); g.fill(); g.stroke();
    }
    g.fillStyle = '#8c6a48'; g.beginPath(); g.ellipse(cx, H * 1.05, W * 0.75, H * 0.55, 0, Math.PI, 0); g.fill(); g.stroke();
    g.fillStyle = '#a6825a'; g.beginPath(); g.ellipse(cx, H * 1.05, W * 0.66, H * 0.46, 0, Math.PI, 0); g.fill();
    const crowd = ['#c8382c', '#3e6aa8', '#3f8f5f', '#e8b54a', '#f3e7cc', '#5a3e2c'];
    for (let i = 0; i < 900; i++) { const a = Math.PI + r() * Math.PI, rr = 0.66 + r() * 0.09; const x = cx + Math.cos(a) * W * rr, y = H * 1.05 + Math.sin(a) * H * 0.55 * rr / 0.75; if (y > H * 0.62) { g.fillStyle = crowd[(r() * 6) | 0]; g.fillRect(x, y, 3, 4); } }
    g.fillStyle = '#d6b783'; g.beginPath(); g.ellipse(cx, H * 1.02, W * 0.52, H * 0.3, 0, Math.PI, 0); g.fill(); g.stroke();
    g.strokeStyle = 'rgba(36,26,33,.25)'; g.beginPath(); g.ellipse(cx, H * 1.02, W * 0.3, H * 0.17, 0, Math.PI, 0); g.stroke();
    for (let i = 0; i < 14; i++) { const x = W * (0.06 + i * 0.067), y = H * 0.52; g.strokeStyle = ink; g.lineWidth = 2; g.beginPath(); g.moveTo(x, y); g.lineTo(x, y - 60); g.stroke(); g.fillStyle = cols[i % 5]; g.beginPath(); g.moveTo(x, y - 60); g.lineTo(x + 26, y - 54); g.lineTo(x, y - 40); g.closePath(); g.fill(); g.stroke(); }
    if (opts.night) { for (let i = 0; i < 18; i++) { const x = W * (0.05 + i * 0.053), y = H * (0.44 + 0.03 * Math.sin(i)); const gr = g.createRadialGradient(x, y, 0, x, y, 26); gr.addColorStop(0, 'rgba(255,200,120,.75)'); gr.addColorStop(1, 'rgba(255,200,120,0)'); g.fillStyle = gr; g.fillRect(x - 26, y - 26, 52, 52); } }
    if (opts.dim) { g.fillStyle = 'rgba(30,18,10,.35)'; g.fillRect(0, 0, W, H); }
  }

  // ================================================================ 小件 ==
  const frame = (gen, size, mood) => UI().frame(gen, { size, mood });
  function seal(text, cls) { return '<span class="sg-sealmark ' + (cls || '') + '">' + esc(text) + '</span>'; }
  function pennant(color) { return '<i class="sg-flag" style="--fc:' + esc(color) + '"></i>'; }
  function slotHtml(prize, size) { return UI().slot(prize && prize.id ? prize : prize ? { id: prize.id, rarity: prize.rarity, slot: prize.slot } : null, { size: size || 44, slot: (prize && prize.slot) || 'weapon' }); }
  const RAR_TXT = { common: 'common', uncommon: 'uncommon', rare: 'rare', epic: 'epic', legendary: 'legend', legend: 'legend' };
  // 奖品一行（UI-V4 §12.1）：按 PRIZES 拼——「冠军 稀有兵器 / 甲 / 宝物或招式 + 400 金 + 声望 50 · 亚军 精良品 + 200 金 · 季军 凡品 + 100 金」
  const ITEM_ZH = { common: '凡品', uncommon: '精良品', rare: '稀有兵器 / 甲 / 宝物', epic: '珍品', legendary: '神品' };
  function prizeLine() {
    const PL = ['冠军', '亚军', '季军'];
    return (td('PRIZES') || []).slice(0, 3).map((p, i) => PL[i] + ' ' + (ITEM_ZH[p.item] || '奖品') + (i === 0 && p.skillP ? '或招式' : '') + ' + ' + p.gold + ' 金' + (i === 0 && p.prestige ? ' + 声望 ' + p.prestige : '')).join(' · ');
  }
  function header(win, vm, sub) {
    win.title.innerHTML = '第' + cnNum(vm.n) + '届　武林大会';
    win.title.classList.add('sg-goldleaf');
    win.setInfo(esc(vm.city) + ' · ' + esc(vm.venue) + ' · ' + vm.year + '年正月 · ' + cnCount(vm.N) + '人' + (sub ? ' · ' + sub : ''));
  }

  let cur = null;
  function closeCur() { if (cur && cur.close) { try { cur.close(); } catch (e) { /* 忽略 */ } } cur = null; }

  // ================================================================ 英雄帖（§12.1）==
  function announce(t) {
    const vm = model(t);
    return new Promise(resolve => {
      let out = 'later';
      const win = UI().window('tourney-announce', { title: '', seal: '武', width: 'min(760px, calc(100vw - 16px))', className: 'sg-tourney sg-tny-ann', foot: true, onClose: () => { cur = null; resolve(out); } });
      cur = win;
      header(win, vm);
      const card = h('div', 'sg-tny-card', null, win.page);
      const vig = h('div', 'sg-tny-vig', '<canvas></canvas><span class="sg-tny-vig-cap">' + esc(vm.city) + ' · ' + esc(vm.venue) + '</span>', card);
      const body = h('div', 'sg-tny-ann-body', null, card);
      h('div', 'sg-tny-edict', '诏告天下：第' + cnNum(vm.n) + '届武林大会于' + esc(vm.city) + esc(vm.venue) + '举行，天下英雄，各遣勇士五人。', body);
      h('div', 'sg-tny-line', icon('trophy', 22) + '<span>' + esc(prizeLine()) + '</span>', body);
      if (vm.reigning) {
        const rg = (G() && G().generals.find(x => x.name === vm.reigning.name)) || { name: vm.reigning.name };
        h('div', 'sg-tny-line sg-tny-reign', frame(rg, 34) + '<span>卫冕：<b>' + esc(vm.reigning.name) + '</b>（' + cnNum(vm.reigning.titles) + '届魁首）</span>', body);
      }
      h('div', 'sg-tny-line sg-muted', icon('lock', 18) + '<span>' + cnNum(DEMO.relicAt) + '冠者得「' + DEMO.relic + '」 · 主办：' + esc(vm.hostName || '—') + ' · 参赛不耗令牌</span>', body);
      UI().button(win.foot, '不参加', () => { out = 'skip'; win.close(); }, { kind: 'ghost' });
      h('div', 'sg-spacer', null, win.foot);
      UI().button(win.foot, '稍后', () => { out = 'later'; win.close(); }, { kind: 'paper' });
      UI().button(win.foot, '选派武将', () => { out = 'enter'; win.close(); }, { kind: 'seal', icon: 'tourney' });
      requestAnimationFrame(() => { try { stadium(vig.querySelector('canvas'), {}); } catch (e) { /* 无画布 */ } });
      try { if (SG.Voice) SG.Voice.say('herald', 'ui.tourneyAnnounce', { pri: 2, scene: 'strategy' }); } catch (e) { /* 无人声 */ }
    });
  }

  // 每人的出战方式记在本机（TOURNAMENT §4.3：sanguozhi2_tourney_modes，读写失败不碍事）
  const MODES_KEY = 'sanguozhi2_tourney_modes';
  const MODE_ZH = [['play', '亲自'], ['watch', '观战'], ['auto', '自动']];
  function readModes() { try { const o = JSON.parse(localStorage.getItem(MODES_KEY) || '{}'); return o && typeof o === 'object' ? o : {}; } catch (e) { return {}; } }
  function writeModes(o) { try { localStorage.setItem(MODES_KEY, JSON.stringify(o)); } catch (e) { /* 不能存就不存 */ } }
  // 不能出赛的原因（TOURNAMENT §4.1 的说法）：规则模块的 eligible；未载入时按同一条规则
  function eligWhy(e, g) {
    if (SG.Tourney && SG.Tourney.eligible) { try { const r = SG.Tourney.eligible(g, e.gen); return r && r.ok ? '' : (r && r.why) || '不合资格'; } catch (err) { /* 退回规则 */ } }
    const x = e.gen, mi = g ? g.year * 12 + g.month : 0;
    if ((x.war | 0) < td('MIN_WAR')) return '武力不足 ' + td('MIN_WAR');
    if (x.injury && x.injury.until > mi) return '负伤未愈';
    if (SG.Conquest && typeof SG.Conquest.isCaptive === 'function') { try { if (SG.Conquest.isCaptive(x)) return '身陷敌营'; } catch (err) { /* 忽略 */ } }
    const away = x.away && x.away.until > mi ? x.away.kind : null;
    if (away === 'caravan') return '率商队在外';
    if (away === 'exped' || away === 'expedition') return '探索未归';
    if (away === 'hostage') return '身陷敌营';
    if (away) return '外出未归';
    return '';
  }

  // ================================================================ 选派出战者（§12.2；内容按 TOURNAMENT §4.3）==
  // 左：本势力武将（按种子分排）；入选者右侧一个三段开关 亲自 · 观战 · 自动（缺省：种子分前两名亲自，其余自动；按人记住）。
  // 右：五面旗位、预计种子、奖品、上届冠军、不上擂台的参与方式。页脚：一键推荐 · 不参加 · 稍后 · 全部自动 · 报名。
  // 键盘：↑ ↓ 在名单里移动，Space 选 / 不选，Enter = 报名（UI-V4 §12.2）
  function entry(t) {
    const vm = model(t);
    const g = G();
    return new Promise(resolve => {
      let out = null;
      const ph = short();
      const MAX = td('MAX_PER_FACTION') || 5;
      const win = UI().window('tourney-entry', { title: '', seal: '武', width: 'calc(100vw - 16px)', height: 'calc(100vh - 16px)', className: 'sg-tourney sg-tny-entry', side: true, sideFirst: ph, foot: true, onClose: () => { cur = null; resolve(out); } });
      cur = win;
      header(win, vm);
      const mineAll = g ? g.generalsOf(g.player).filter(x => !x.dead).map(x => entryOf({ gen: x }, g)).sort(bySeed) : [];
      const why = new Map(mineAll.map(e => [e, eligWhy(e, g)]));
      const elig = e => why.get(e) || '';
      // 预选：规则模块的 defaultPick（= 电脑的选法，门槛 T = MIN_WAR）；未载入时同一条规则
      const defaultPick = () => {
        if (SG.Tourney && SG.Tourney.defaultPick && g) { try { const a = SG.Tourney.defaultPick(g, g.player).map(x => mineAll.find(e => e.gen === x)).filter(Boolean); if (a.length) return a.slice(0, MAX); } catch (err) { /* 退回规则 */ } }
        return mineAll.filter(e => !elig(e) && (e.gen.war | 0) >= td('MIN_WAR')).slice(0, MAX);
      };
      let picked = !vm.demo && vm.mine.length ? vm.mine.map(e => mineAll.find(x => x.name === e.name)).filter(Boolean).slice(0, MAX) : defaultPick();
      const saved = readModes();
      const modeOf = e => {
        if (saved[e.name] && MODE_ZH.some(m => m[0] === saved[e.name])) return saved[e.name];
        const rank = picked.slice().sort(bySeed).indexOf(e);
        return rank >= 0 && rank < 2 ? 'play' : 'auto';
      };
      const stars = e => {
        if (SG.Tourney && SG.Tourney.estimate) { try { return '★'.repeat(SG.Tourney.estimate(g, e.gen, picked.map(p => p.gen)).stars | 0) || '—'; } catch (err) { /* 估计缺省 */ } }
        const rank = vm.entrants.filter(x => !x.mine && seedScore(x) > seedScore(e)).length + picked.filter(x => x !== e && seedScore(x) > seedScore(e)).length + 1;
        return rank <= 8 ? '★★★' : rank <= 16 ? '★★' : rank <= 32 ? '★' : '—';
      };
      // 左：名单（行一次建好，之后只改状态：焦点不丢）
      h('div', 'sg-tny-h', '选派出战者（至多 ' + MAX + ' 人）<small>按种子分排序；单挑按武力（装备武力计三分之一）；同势力首轮尽量不相遇</small>', win.page);
      const list = h('div', 'sg-tny-cands', null, win.page);
      const rows = [];
      for (const e of mineAll) {
        const w = weaponOf(e.gen), no = elig(e);
        const wrap = h('div', 'sg-tny-crw', null, list);
        const r = h('button', 'sg-tny-cr' + (no ? ' is-off' : ''), frame(e.gen, ph ? 34 : 44) +
          '<span class="sg-tny-cn"><b>' + esc(e.name) + '</b><small>Lv ' + e.level + ' · 忠 ' + (e.gen.loyalty | 0) + (no ? ' · ' + esc(no) : '') + '</small></span>' +
          '<span class="sg-tny-dw sg-num">' + e.war + '</span><span class="sg-tny-gl">' + esc(glyphsOf(e.gen)) + '</span>' +
          '<span class="sg-tny-wp' + (w ? ' sg-rar ' + (RAR_TXT[w.rarity] || 'common') : '') + '">' + esc(w ? w.name : '—') + '</span><span class="sg-tny-star"></span>', wrap);
        r.type = 'button';
        r.dataset.name = e.name;
        r.dataset.enter = 'window';                 // Enter 在名单上也是「报名」；Space 才是选 / 不选
        r.addEventListener('click', () => {
          if (no) { UI().deny(r, esc(e.name + '：' + no)); return; }
          const i = picked.indexOf(e);
          if (i >= 0) picked.splice(i, 1); else if (picked.length < MAX) picked.push(e); else { UI().deny(r, '至多 ' + MAX + ' 人。'); return; }
          sfx('ui-press-paper'); paint();
        });
        const seg = UI().segmented(wrap, MODE_ZH, modeOf(e), v => { saved[e.name] = v; writeModes(saved); paint(); });
        seg.classList.add('sg-tny-mode');
        seg.setAttribute('aria-label', e.name + ' 出战方式');
        rows.push({ e, r, seg, star: r.querySelector('.sg-tny-star') });
      }
      if (!mineAll.length) h('div', 'sg-empty', '麾下无人可派', list);
      // 右：五面势力旗
      const side = win.side;
      h('div', 'sg-tny-h', '我方旗位', side);
      const bn = h('div', 'sg-tny-banners', null, side);
      const info = h('div', 'sg-tny-info', null, side);
      const pf = g ? g.playerFaction : null;
      const paint = () => {
        for (const x of rows) {
          const on = picked.includes(x.e);
          x.r.classList.toggle('is-on', on);
          x.r.setAttribute('aria-pressed', on ? 'true' : 'false');
          x.seg.classList.toggle('is-idle', !on);
          if (on) x.seg.set(modeOf(x.e));
          x.star.textContent = elig(x.e) ? '' : stars(x.e);
        }
        bn.innerHTML = '';
        for (let i = 0; i < MAX; i++) {
          const e = picked[i];
          const md = e ? MODE_ZH.find(m => m[0] === modeOf(e)) : null;
          const b = h('button', 'sg-tny-flag' + (e ? '' : ' is-empty'), e ? frame(e.gen, ph ? 30 : 38) + '<span>' + esc(e.name) + '</span><em class="sg-tny-fmode">' + esc(md ? md[1] : '') + '</em>' : '<span class="sg-tny-plus">＋</span>', bn);
          b.type = 'button';
          b.style.setProperty('--fc', pf ? pf.color : '#2f9a55');
          b.setAttribute('aria-label', e ? '移出 ' + e.name : '空旗位');
          b.addEventListener('click', () => { if (e) { picked.splice(picked.indexOf(e), 1); sfx('ui-press-paper'); paint(); } else { const nx = mineAll.find(x => !picked.includes(x) && !elig(x)); if (nx) { picked.push(nx); sfx('ui-press-paper'); paint(); } } });
        }
        // 预计种子：电脑的出赛者 + 我方当前的选择，按种子分排
        const field = vm.entrants.filter(x => !x.mine).concat(picked);
        const seeds = picked.map(e => e.name + ' ' + (field.filter(x => x !== e && seedScore(x) > seedScore(e)).length + 1) + ' 号');
        const R = RN(), P = td('PREDICT') || {}, SP = td('SPONSOR') || [];
        info.innerHTML = '<p>预计种子：' + (seeds.length ? esc(seeds.join(' · ')) : '—') + '</p><p>奖：' + esc(prizeLine()) + '</p>' +
          (vm.reigning ? '<p class="sg-muted">上届冠军：' + esc(vm.reigning.name) + '（' + cnNum(vm.reigning.titles) + '冠）· ' + cnNum(DEMO.relicAt) + '冠者得「' + DEMO.relic + '」</p>' : '') +
          '<p class="sg-muted">亲自：你来操作 · 观战：电脑出战、播放比赛 · 自动：只看结果。亲自出战不受「单挑速度」设置影响（一律 100 %）。</p>' +
          '<p class="sg-muted">不上擂台也能参与：竞猜（' + esc(R[P.opensAt] || '十六强') + '时开放）· 赞助 ' + SP.map(x => x.gold).join(' / ') + ' 金</p>';
      };
      paint();
      h('div', 'sg-note', '「★」为推荐（预计名次）；不合资格者灰显并写明原因。参赛不耗令牌。', win.foot);
      h('div', 'sg-spacer', null, win.foot);
      UI().button(win.foot, '一键推荐', () => { picked = defaultPick(); sfx('ui-press-paper'); paint(); }, { kind: 'paper' });
      UI().button(win.foot, '不参加', () => { out = null; win.close(); }, { kind: 'ghost' });
      UI().button(win.foot, '稍后', () => { out = 'later'; win.close(); }, { kind: 'paper' });
      const need = () => { if (picked.length) return true; UI().toast('请至少选派一人，或点「不参加」。', { seal: '武', seconds: 2 }); return false; };
      UI().button(win.foot, '全部自动', () => { if (!need()) return; out = picked.map(e => ({ gen: e.gen, mode: 'auto' })); out.allAuto = true; win.close(); }, { kind: 'plaque' });
      UI().button(win.foot, '报　名', () => { if (!need()) return; out = picked.map(e => ({ gen: e.gen, mode: modeOf(e) })); sfx('ui-stamp'); win.close(); }, { kind: 'seal' });
      // ↑ ↓：名单里移动焦点（Space 选 / 不选，Enter = 报名，由 SG.UI.window 处理）
      win.onKey = k => {
        if (k !== 'ArrowDown' && k !== 'ArrowUp') return false;
        const rs = rows.map(x => x.r);
        if (!rs.length) return false;
        let i = rs.indexOf(document.activeElement);
        i = i < 0 ? 0 : Math.max(0, Math.min(rs.length - 1, i + (k === 'ArrowDown' ? 1 : -1)));
        try { rs[i].focus({ preventScroll: true }); rs[i].scrollIntoView({ block: 'nearest' }); } catch (err) { /* 忽略 */ }
        return true;
      };
    });
  }

  // ================================================================ 签表（§12.3）==
  const BOX = () => (short() ? { w: 118, h: 48, row: 23 } : { w: 150, h: 58, row: 28 });
  function bracket(t, opts) {
    opts = opts || {};
    const vm = model(t);
    const g = G();
    return new Promise(resolve => {
      let out = null;
      const ph = short();
      const win = UI().window('tourney-bracket', { title: '', seal: '武', width: 'calc(100vw - 16px)', height: 'calc(100vh - 16px)', className: 'sg-tourney sg-tny-bracket', side: true, onClose: () => { cur = null; resolve(out); } });
      cur = win;
      header(win, vm, esc(vm.hostName) + '主办');
      // 轮次页签 + 我方
      const semi = vm.rounds.findIndex(r => r.players === 4);
      const firstShown = Math.max(0, semi - (ph ? 1 : 2));
      const tabs = vm.rounds.map((r, i) => ({ id: String(i), label: r.name })).concat([{ id: 'mine', label: '我方' }]);
      const start = opts.round !== undefined ? String(opts.round) : opts.view === 'mine' ? 'mine' : String(Math.min(vm.cur, firstShown));
      const tb = UI().tabs(win.tools, tabs, id => render(id), { value: start, className: 'sg-tny-rtabs' });
      win.tools.insertBefore(tb.el, win.tools.firstChild);
      const page = win.page;
      page.classList.add('sg-tny-brk');
      const canvas = h('div', 'sg-tny-canvas', null, page);
      let mini = null;
      const render = id => {
        canvas.innerHTML = '';
        canvas.scrollTop = 0;
        if (id === 'mine') { mineView(); return; }
        const r0 = +id;
        drawBracket(r0);
        drawMini(r0);
      };
      // 下一场（右栏）
      const next = win.side;
      next.classList.add('sg-tny-next');
      drawNext(next, vm, ph, mode => { out = mode; win.close(); });
      mini = h('div', 'sg-tny-mini', null, next);
      const resultTxt = (m, e) => (m.w === null || m.bye ? '' : (m.w === 0) === (e === m.a) ? 'won' : 'lose');
      const pl = (m, e) => {
        if (!e) return '<div class="sg-tny-p is-tbd">' + (m.bye && (m.a || m.b) ? '轮空' : '待定') + '</div>';
        const rs = resultTxt(m, e);
        return '<div class="sg-tny-p ' + rs + (e.mine ? ' mine' : '') + '" style="--fc:' + esc(e.fc) + '"><i></i>' + frame(e.gen, ph ? 18 : 22) + '<span class="sg-tny-pn">' + esc(e.name) + '</span><b class="sg-num">' + e.war + '</b></div>';
      };
      const box = (m, x, y) => {
        const el = h('div', 'sg-tny-m' + (m.now ? ' is-now' : ''), pl(m, m.a) + pl(m, m.b), canvas);
        el.style.left = x + 'px'; el.style.top = y + 'px';
        return el;
      };
      function drawBracket(r0) {
        const B = BOX();
        canvas.style.removeProperty('--bw');
        const W = canvas.clientWidth || 800, H = canvas.clientHeight || 500;
        const last = semi;                                   // 四强一轮（每半区一场）
        const ncol = ph ? 2 : 3;
        const mirrored = r0 >= last - (ncol - 1);
        let lines = '';
        if (mirrored) {
          // 左右对称：左半区从左往右、右半区从右往左；两场四强在中线的高度，决赛的签框夹在它们中间，奖杯与「决赛」在上、奖品台在下
          const cols = last - r0 + 1;
          const fw = ph ? 104 : 140, gapX = ph ? 10 : 16;
          const bw = Math.max(ph ? 92 : 104, Math.min(B.w, Math.floor((W / 2 - fw / 2 - gapX - 8 - (cols - 1) * gapX) / cols)));
          canvas.style.setProperty('--bw', bw + 'px');
          const pos = {};
          const unit = Math.max(B.h + 8, (H - 24) / Math.max(1, vm.rounds[r0].matches.length / 2));
          for (let side = 0; side < 2; side++) {
            for (let c = 0; c < cols; c++) {
              const rd = vm.rounds[r0 + c];
              const per = rd.matches.length / 2;
              const ms = rd.matches.slice(side * per, side * per + per);
              ms.forEach((m, j) => {
                let y;
                if (c === 0) y = 12 + j * unit + (unit - B.h) / 2;
                else { const a = pos[(c - 1) + ':' + side + ':' + (2 * j)], b = pos[(c - 1) + ':' + side + ':' + (2 * j + 1)]; y = (a.y + b.y) / 2; }
                const x = side === 0 ? 8 + c * (bw + gapX) : W - 8 - bw - c * (bw + gapX);
                pos[c + ':' + side + ':' + j] = { x, y };
                box(m, x, y);
                if (c > 0) {
                  const a = pos[(c - 1) + ':' + side + ':' + (2 * j)], b = pos[(c - 1) + ':' + side + ':' + (2 * j + 1)];
                  const ex = side === 0 ? a.x + bw : a.x, mx = side === 0 ? x - gapX / 2 : x + bw + gapX / 2, tx = side === 0 ? x : x + bw;
                  lines += '<path d="M' + ex + ' ' + (a.y + B.h / 2) + ' H' + mx + ' V' + (b.y + B.h / 2) + ' M' + ex + ' ' + (b.y + B.h / 2) + ' H' + mx + ' M' + mx + ' ' + (y + B.h / 2) + ' H' + tx + '"/>';
                }
              });
            }
          }
          // 决赛
          const fin = vm.rounds[last + 1];
          const fm = fin ? fin.matches[0] : { a: null, b: null, w: null };
          const sL = pos[(cols - 1) + ':0:0'], sR = pos[(cols - 1) + ':1:0'];
          const fy = sL ? sL.y : H / 2 - B.h / 2, fx = W / 2 - fw / 2;
          const fb = box(fm, fx, fy);
          fb.classList.add('sg-tny-fm'); fb.style.width = fw + 'px';
          if (sL) lines += '<path d="M' + (sL.x + bw) + ' ' + (fy + B.h / 2) + ' H' + fx + '"/>';
          if (sR) lines += '<path d="M' + sR.x + ' ' + (fy + B.h / 2) + ' H' + (fx + fw) + '"/>';
          const top = h('div', 'sg-tny-final', icon('trophy', ph ? 34 : 58) + '<div class="sg-tny-fl">决　赛</div><div class="sg-tny-fs">三局两胜 · 另有季军战</div>', canvas);
          top.style.bottom = (H - fy + 8) + 'px';
          const bot = h('div', 'sg-tny-final is-below', null, canvas);
          bot.style.top = (fy + B.h + 10) + 'px';
          if (vm.prize) h('div', 'sg-tny-ped', slotHtml(vm.prize, ph ? 34 : 40) + '<div><b class="sg-rar ' + (RAR_TXT[vm.prize.rarity] || 'rare') + '">' + esc(vm.prize.name) + '</b><small>冠军奖品（颁奖时揭晓）</small></div>', bot);
          h('div', 'sg-tny-fs', DEMO.relic + ' · ' + cnNum(DEMO.relicAt) + '冠者得', bot);
        } else {
          // 早的轮次（签太多）：三列从左往右，可上下滚动；右侧的缩略图标出所在
          const cols = Math.min(ncol, vm.rounds.length - r0);
          const unit = B.h + 10;
          const gapX = Math.max(16, (W - 16 - cols * B.w) / Math.max(1, cols));
          const pos = {};
          let maxY = 0;
          for (let c = 0; c < cols; c++) {
            const rd = vm.rounds[r0 + c];
            rd.matches.forEach((m, j) => {
              const y = c === 0 ? 8 + j * unit : (pos[(c - 1) + ':' + (2 * j)].y + pos[(c - 1) + ':' + (2 * j + 1)].y) / 2;
              const x = 8 + c * (B.w + gapX);
              pos[c + ':' + j] = { x, y };
              maxY = Math.max(maxY, y + B.h);
              box(m, x, y);
              if (c > 0) {
                const a = pos[(c - 1) + ':' + (2 * j)], b = pos[(c - 1) + ':' + (2 * j + 1)], mx = x - gapX / 2;
                lines += '<path d="M' + (a.x + B.w) + ' ' + (a.y + B.h / 2) + ' H' + mx + ' V' + (b.y + B.h / 2) + ' M' + (b.x + B.w) + ' ' + (b.y + B.h / 2) + ' H' + mx + ' M' + mx + ' ' + (y + B.h / 2) + ' H' + x + '"/>';
              }
            });
          }
          const spacer = h('div', 'sg-tny-spacer', null, canvas); spacer.style.height = (maxY + 12) + 'px';
        }
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('class', 'sg-tny-lines'); svg.setAttribute('aria-hidden', 'true');
        svg.innerHTML = '<g fill="none" stroke="#7a685d" stroke-width="2" stroke-linecap="round">' + lines + '</g>';
        svg.style.height = Math.max(canvas.scrollHeight, canvas.clientHeight) + 'px';
        canvas.insertBefore(svg, canvas.firstChild);
        const now = canvas.querySelector('.is-now');
        if (now && !mirrored) now.scrollIntoView({ block: 'center' });
      }
      // 全图缩略：整张签的树（左右对称），红框 = 当前一窗
      function drawMini(r0) {
        if (ph || !mini) return;
        mini.style.display = '';
        const R = vm.rounds.length, w = 120, hh = 120;
        let s = '<svg viewBox="0 0 ' + w + ' ' + hh + '" width="' + w + '" height="' + hh + '" aria-hidden="true"><g stroke="#7a685d" stroke-width="1" fill="none">';
        for (let ri = 0; ri < R - 1; ri++) {
          const per = vm.rounds[ri].matches.length / 2;
          for (let side = 0; side < 2; side++) for (let j = 0; j < per; j++) {
            const x = side === 0 ? 4 + ri * (52 / Math.max(1, R - 1)) : w - 4 - ri * (52 / Math.max(1, R - 1));
            const y = 6 + (j + 0.5) * (hh - 12) / per;
            s += '<path d="M' + x.toFixed(1) + ' ' + y.toFixed(1) + ' h' + (side === 0 ? 3 : -3) + '"/>';
          }
        }
        const x0 = 4 + r0 * (52 / Math.max(1, R - 1)) - 3;
        s += '</g><rect x="' + x0.toFixed(1) + '" y="2" width="' + (w - 2 * x0).toFixed(1) + '" height="' + (hh - 4) + '" fill="none" stroke="#b23b2a" stroke-width="2" rx="3"/></svg>';
        mini.innerHTML = s + '<span>全图 ' + vm.S + ' · 点轮次换页</span>';
      }
      // 我方：本势力出赛者的路线
      function mineView() {
        if (mini) mini.style.display = 'none';
        const box2 = h('div', 'sg-tny-minelist', null, canvas);
        const mine = vm.entrants.filter(e => e.mine);
        if (!mine.length) { h('div', 'sg-empty', icon('tourney', 40) + '<div>本届我方无人出赛</div>', box2); return; }
        for (const e of mine) {
          const path = [];
          for (const rd of vm.rounds) {
            const m = rd.matches.find(x => x.a === e || x.b === e);
            if (!m) break;
            const op = m.a === e ? m.b : m.a;
            const st = m.bye ? '轮空' : m.w === null ? (m.now ? '下一场' : '待赛') : ((m.w === 0) === (m.a === e) ? '胜' : '负');
            path.push('<span class="sg-tny-step is-' + (st === '胜' ? 'won' : st === '负' ? 'lose' : 'tbd') + '"><small>' + esc(rd.name) + '</small>' + (op ? esc(op.name) : '—') + ' <b>' + esc(st) + '</b></span>');
            if (st === '负') break;
          }
          h('div', 'sg-tny-mrow', frame(e.gen, 40) + '<div class="sg-tny-mn"><b>' + esc(e.name) + '</b><small>种子 ' + e.seed + ' · 单挑 ' + e.war + '</small></div><div class="sg-tny-path">' + path.join('<i>›</i>') + '</div>', box2);
        }
      }
      requestAnimationFrame(() => render(start));
      win.onKey = k => { if (k === 'Enter') { const b = next.querySelector('.k-seal'); if (b) { b.click(); return true; } } return false; };
    });
  }
  function drawNext(el, vm, ph, choose) {
    el.innerHTML = '';
    const m = vm.next;
    const rd = vm.rounds[vm.cur];
    h('div', 'sg-tny-h', '下一场 · ' + esc(rd ? rd.name : ''), el);
    if (!m) { h('div', 'sg-empty', '本轮已赛毕', el); return; }
    const p = winChance(m.a, m.b);
    const vs = h('div', 'sg-tny-vs', '<div class="sg-tny-side">' + frame(m.a.gen, ph ? 44 : 64) + '<b>' + esc(m.a.name) + '</b><small class="sg-num">武 ' + m.a.war + ' · Lv ' + m.a.level + '</small></div><div class="sg-tny-x">对</div>' +
      '<div class="sg-tny-side">' + frame(m.b.gen, ph ? 44 : 64) + '<b>' + esc(m.b.name) + '</b><small class="sg-num">武 ' + m.b.war + ' · Lv ' + m.b.level + '</small></div>', el);
    vs.setAttribute('aria-label', m.a.name + ' 对 ' + m.b.name);
    h('div', 'sg-tny-odds', '<i style="width:' + Math.round(p * 100) + '%;background:' + esc(m.a.fc) + '"></i><b style="background:' + esc(m.b.fc) + '"></b>', el);
    const opp = m.a.mine ? m.b : m.b.mine ? m.a : m.b;
    let seen = 8;
    try { if (SG.Combos && SG.Combos.known && G()) { const k = SG.Combos.known(G(), opp.name, G().player); seen = 0; for (let i = 0; i < 8; i++) if (k & (1 << i)) seen++; } } catch (e) { seen = 8; }
    h('div', 'sg-tny-oddsl', '<span>胜率约 ' + Math.round(p * 100) + '%</span><span>已见' + esc(opp.name) + '招式 ' + seen + ' / 8</span>', el);
    if (!ph) h('div', 'sg-tny-glyphs', esc(glyphsOf(m.a.gen)) + '<br>' + esc(glyphsOf(m.b.gen)), el);
    const mine = m.a.mine || m.b.mine;
    const r1 = h('div', 'sg-tny-row', null, el);
    UI().button(r1, '亲自出战', () => choose('play'), { kind: 'seal', why: mine ? null : '这一场没有我方武将。' });
    const r2 = h('div', 'sg-tny-row', null, el);
    const pref = watchPref();                     // 「单挑观战」设置（第 22 行）的缺省：金框标出
    UI().button(r2, '观战', () => choose('watch'), { kind: 'plaque', className: pref === 'watch' ? 'is-pref' : '' });
    UI().button(r2, '只看结果', () => choose('result'), { kind: 'plaque', className: pref === 'result' ? 'is-pref' : '' });
    if (vm.log && vm.log.length && !ph) h('div', 'sg-tny-log', vm.log.map(s => '· ' + esc(s)).join('<br>'), el);
  }

  // ================================================================ 全屏场景（对阵、颁奖）==
  // 场景也进 SG.UI 的模态栈（openModal，不压暗）：外壳的快捷键（G T P H O、指令坞）与手机「返回」手势不会落到地图上；
  // Esc / 返回 = 场景自己的 onKey('Escape')
  function scene(cls, night, label) {
    const m = UI().openModal('sg-tny-scene ' + cls, { blockerClass: 'sg-tny-scene-modal', dim: false });
    m.panel.classList.remove('sg-panel', 'sg-dialog');
    const el = m.panel;
    el.innerHTML = '<canvas class="sg-tny-arena"></canvas>';
    if (label) el.setAttribute('aria-label', label);
    const cv = el.querySelector('canvas');
    requestAnimationFrame(() => { try { stadium(cv, { night }); } catch (e) { /* 无画布 */ } });
    return { el, m };
  }
  // 电脑之间的比赛：按「单挑观战」（设置第 22 行）——只看结果 → 'result'，观看 / 精彩片段 → 'watch'
  const watchPref = () => { let v = 'highlights'; try { if (SG.UISettings && SG.UISettings.duelWatch) v = SG.UISettings.duelWatch(); } catch (e) { /* 缺省 */ } return v === 'result' ? 'result' : 'watch'; };
  // 对阵（§12.4）：观战的 2 秒后按「单挑观战」自动继续；我方的等待选择
  function match(m, t) {
    const vm = model(t);
    m = m || vm.next;
    return new Promise(resolve => {
      if (!m) { resolve('result'); return; }
      const mine = m.a.mine || m.b.mine;
      const sc = scene('sg-tny-vsscene', false, m.a.name + ' 对 ' + m.b.name);
      const el = sc.el;
      const ph = short();
      const rd = vm.rounds[vm.cur];
      const card = e => '<div class="sg-tny-vcard">' + frame(e.gen, ph ? 120 : 210, 'angry') + '<div class="sg-tny-vn">' + esc(e.name) + '</div>' +
        '<div class="sg-tny-vinfo is-paper"><span style="color:' + esc(e.fc) + '" class="sg-tny-vf">' + esc(e.fname) + '</span><span class="sg-num">武 ' + e.war + ' · Lv ' + e.level + '</span><span class="sg-tny-gl">' + esc(glyphsOf(e.gen)) + '</span></div></div>';
      const body = h('div', 'sg-tny-vsbig', card(m.a) + '<div class="sg-tny-mid"><div class="sg-tny-rd">' + esc(rd ? rd.name : '') + ' · 第' + cnNum(1 + Math.max(0, rd ? rd.matches.indexOf(m) : 0)) + '场</div><div class="sg-tny-bigvs">对</div><div class="sg-tny-row sg-tny-ra"></div><div class="sg-tny-row sg-tny-rb"></div></div>' + card(m.b), el);
      let done = false, autoT = 0;
      const finish = v => { if (done) return; done = true; clearTimeout(autoT); el.classList.add('is-out'); sc.m.close(); cur = null; resolve(v); };
      // Esc / 返回 = 只看结果；Enter = 亲自出战（我方的比赛、焦点不在别的按钮上时）；Space / Enter 在按钮上 = 按下它（SG.UI）
      sc.m.onKey = k => {
        if (k === 'Escape') { finish('result'); return true; }
        if (k === 'Enter' && mine && !UI().keyFocused(el)) { finish('play'); return true; }
        return false;
      };
      const r1 = body.querySelector('.sg-tny-ra'), r2 = body.querySelector('.sg-tny-rb');
      const pref = watchPref();
      if (mine) UI().button(r1, '亲自出战', () => finish('play'), { kind: 'seal', size: 'big' });
      UI().button(r2, '观战', () => finish('watch'), { kind: 'plaque', className: pref === 'watch' && !mine ? 'is-pref' : '' });
      UI().button(r2, '只看结果', () => finish('result'), { kind: 'plaque', className: pref === 'result' && !mine ? 'is-pref' : '' });
      cur = { close: () => finish('result') };
      sfx('ui-stamp', 0.6);
      try { if (SG.Voice) { if (m.a.mine) SG.Voice.say(m.a.gen, 'tourney.enter', { pri: 1 }); else SG.Voice.say(m.b.gen, 'duel.accept', { pri: 1 }); } } catch (e) { /* 无人声 */ }
      if (!mine) autoT = setTimeout(() => finish(pref), 2000);
    });
  }
  // 颁奖奖品（§12.6）：规则模块的 rewards(T)；示意赛事按 PRIZES 的金与声望 + 示意物品
  function podiumPrizes(t, vm) {
    const g = G();
    let rw = null;
    try { if (SG.Tourney && SG.Tourney.rewards && t && !t.demo) rw = SG.Tourney.rewards(t); } catch (e) { rw = null; }
    const itemInfo = id => {
      try { if (SG.Items && SG.Items.get) { const it = SG.Items.get(id); if (it) return { id: it.id, name: SG.Items.displayName ? SG.Items.displayName(it) : it.name, rarity: it.rarity, slot: it.slot }; } } catch (e) { /* 无装备 */ }
      return { name: String(id || ''), rarity: 'common', slot: 'treasure' };
    };
    const realItem = (name, rarity, slot) => {
      try { if (SG.Items && SG.Items.all) { const a = SG.Items.all(); const it = a.find(x => x.name === name) || a.find(x => x.rarity === rarity && x.slot === slot && !x.bound); if (it) return { id: it.id, name: SG.Items.displayName ? SG.Items.displayName(it) : it.name, rarity: it.rarity, slot: it.slot }; } } catch (e) { /* 无装备 */ }
      return { name, rarity, slot };
    };
    const P = td('PRIZES') || [];
    const champs = n => { try { if (SG.Tourney && SG.Tourney.championships) return SG.Tourney.championships(n) | 0; } catch (e) { /* 缺省 */ } return g && g.tourney && g.tourney.wins ? g.tourney.wins[n] | 0 : 0; };
    const applied = !!(t && !t.demo && g && g.tourney && g.tourney.held === t.year);
    return [0, 1, 2].map(i => {
      const r = rw ? rw.find(x => x && x.place === i + 1) : null;
      const base = P[i] || {};
      let item = null, label = '';
      if (r && r.prize) {
        if (r.prize.kind === 'item') item = itemInfo(r.prize.id);
        else if (r.prize.kind === 'offers') { item = itemInfo((r.prize.ids || [])[0]); label = (r.prize.ids || []).length + ' 件任选其一'; }
        else if (r.prize.kind === 'skill') { let nm = ''; try { const sp = SG.Specials && SG.Specials.byId ? SG.Specials.byId(r.prize.id) : null; nm = sp ? sp.name : ''; } catch (e) { nm = ''; } item = { name: '武林秘传' + (nm ? '「' + nm + '」' : ''), rarity: 'rare', slot: 'treasure' }; }
      } else if (!rw) item = i === 0 ? vm.prize : realItem.apply(null, i === 1 ? DEMO.prize2 : DEMO.prize3);
      const who = r && r.gen ? (r.gen.name || r.gen) : null;
      return { item, label, gold: r ? r.gold | 0 : base.gold | 0, prestige: r ? r.prestige | 0 : base.prestige | 0, crown: i === 0 ? (who ? champs(who) + (applied ? 0 : 1) : 1) : 0 };
    });
  }
  // 颁奖（§12.6）：2 → 3 → 1 依次升起，然后「天下第一」盖下；点按继续（≥ 2.6 秒；Esc 随时）
  function podium(t) {
    const vm = model(t);
    return new Promise(resolve => {
      // 前三名：规则模块给出 podium(T)；示意赛事取种子前三
      let top = null;
      try { if (SG.Tourney && SG.Tourney.podium && t && !t.demo) top = SG.Tourney.podium(t).map(e => entryOf(e, G())); } catch (e) { top = null; }
      if (!top) top = vm.entrants.slice(0, 3);
      if (top.length < 3) { resolve(); return; }
      const sc = scene('sg-tny-podscene', true, '颁奖');
      const el = sc.el;
      const ph = short();
      const prizes = podiumPrizes(t, vm);
      const tier = (i, size, hgt) => {
        const e = top[i], p = prizes[i];
        const d = h('div', 'sg-tny-tier t' + (i + 1), frame(e.gen, size, 'win') + '<div class="sg-tny-tn">' + esc(e.name) + '</div>' +
          '<div class="sg-tny-prize is-paper">' + slotHtml(p.item, ph ? 34 : 44) + '<div><b class="sg-rar ' + (RAR_TXT[p.item && p.item.rarity] || 'common') + '">' + esc(p.item ? p.item.name : '') + (p.label ? '<small>' + esc(p.label) + '</small>' : '') + '</b><small class="sg-num">+' + p.gold + ' 金 · 声望 +' + p.prestige + (p.crown ? ' · 冠 ×' + p.crown : '') + '</small></div></div>' +
          '<div class="sg-tny-blk" style="height:' + hgt + 'px">' + '一二三'[i] + '</div>', null);
        return d;
      };
      const pod = h('div', 'sg-tny-pod', null, el);
      const k = ph ? 0.62 : 1;
      const t2 = tier(1, Math.round(104 * k), Math.round(110 * k)), t1 = tier(0, Math.round(140 * k), Math.round(160 * k)), t3 = tier(2, Math.round(96 * k), Math.round(80 * k));
      pod.appendChild(t2); pod.appendChild(t1); pod.appendChild(t3);
      const stamp = h('div', 'sg-tny-tianxia', '天下第一', el);
      const order = [t2, t3, t1];
      let t0 = performance.now(), done = false;
      if (!reduced()) {
        order.forEach((d, i) => { d.style.opacity = '0'; SG.Motion.play(d, [{ opacity: 0, transform: 'translateY(60px)' }, { opacity: 1, transform: 'none' }], { dur: 800, ease: 'spring', delay: i * 500 }); });
        // 朱印在 1.9 秒盖下（动画自带延迟，跳过时可一步到终态）；同时一声印、画面轻震 3 px
        SG.Motion.play(stamp, [{ transform: 'translateX(-50%) rotate(-4deg) scale(1.8)', opacity: 0 }, { transform: 'translateX(-50%) rotate(-4deg) scale(1)', opacity: 1 }], { dur: 160, ease: 'stamp', delay: 1900 });
        // 轻震 ≤ 3 px、0.12 秒（UI-V4 §13.2 第 6 条；设置第 34 行「画面震动」缩放）。用 Web Animations：换 CSS 动画类会把场景的淡入从头再放一遍
        setTimeout(() => {
          if (done) return;
          sfx('ui-stamp');
          let k = 1; try { if (SG.UISettings && SG.UISettings.shakeK) k = SG.UISettings.shakeK(); } catch (e) { k = 1; }
          if (k > 0 && el.animate) el.animate([{ transform: 'none' }, { transform: 'translate(' + 3 * k + 'px,' + -2 * k + 'px)' }, { transform: 'translate(' + -3 * k + 'px,' + 2 * k + 'px)' }, { transform: 'translate(' + 2 * k + 'px,' + k + 'px)' }, { transform: 'none' }], { duration: 120 });
        }, 1900);
      } else sfx('ui-stamp');
      const finish = () => { if (done || performance.now() - t0 < (reduced() ? 300 : 2600)) return; done = true; el.classList.add('is-out'); sc.m.close(); cur = null; resolve(); };
      sc.m.onKey = k2 => {
        if (k2 === 'Escape') { t0 = -1e9; finish(); return true; }
        if (k2 === 'Enter' || k2 === 'Space') { finish(); return true; }
        return false;
      };
      el.addEventListener('click', finish);
      h('div', 'sg-tny-cont', '点按继续', el);
      cur = { close: () => { t0 = -1e9; finish(); } };
      try { if (SG.Voice) SG.Voice.say(top[0].gen, 'tourney.win', { pri: 1 }); } catch (e) { /* 无人声 */ }
    });
  }

  // ================================================================ 英雄榜（§12.8）==
  function hall() {
    const g = G();
    const T = g && g.tourney ? g.tourney : null;
    let hist = T && T.history && T.history.length ? T.history.slice() : null;
    let demo = false;
    if (!hist) {
      demo = true;
      const vm = demoModel(2);
      const e = vm.entrants;
      hist = e.length >= 3 ? [{ y: vm.year - 2, city: vm.city, p: [e[0].name, e[2].name, e[1].name], fin: '2:1' }, { y: vm.year - 1, city: vm.city, p: [e[0].name, e[1].name, e[3] ? e[3].name : e[2].name], fin: '2:0' }] : [];
    }
    return new Promise(resolve => {
      const win = UI().window('tourney-hall', { title: '英雄榜', seal: '榜', width: 'min(860px, calc(100vw - 16px))', height: 'min(560px, calc(100vh - 16px))', className: 'sg-tourney sg-tny-hall', onClose: () => { cur = null; resolve(); } });
      cur = win;
      win.setInfo('历届武林大会' + (demo ? ' · 示意' : ''));
      if (!hist.length) { h('div', 'sg-empty', icon('trophy', 40) + '<div>尚未举办武林大会</div>', win.page); return; }
      const nameOf = x => (typeof x === 'string' ? x : x && x.name ? x.name : (g && g.generals[x] ? g.generals[x].name : String(x)));
      const genOf = n => (g && g.generals.find(x => x.name === n)) || { name: n };
      const tb = h('div', 'sg-tny-htable', '<div class="sg-tny-hr is-head"><span>届</span><span>年 · 会址</span><span>冠军</span><span>亚军</span><span>季军</span><span>决赛</span></div>', win.page);
      // 届数：历史只留最近 40 届（TOURNAMENT §10.4），第一行是第 count − 留下的届数 + 1 届
      const total = T && T.count >= hist.length ? T.count | 0 : hist.length;
      hist.forEach((r, i) => {
        const p = (r.p || []).map(nameOf);
        h('div', 'sg-tny-hr', '<span class="sg-num">' + cnNum(total - hist.length + i + 1) + '</span><span>' + (r.y || '') + '年 · ' + esc(cityName(r.city, g)) + '</span>' +
          '<span class="sg-tny-champ">' + (p[0] ? frame(genOf(p[0]), 26) + esc(p[0]) : '—') + '</span><span>' + esc(p[1] || '—') + '</span><span>' + esc(p[2] || '—') + '</span><span class="sg-num">' + esc(r.fin || '') + '</span>', tb);
      });
      const wins = {};
      for (const r of hist) { const c = nameOf((r.p || [])[0]); if (c) wins[c] = (wins[c] || 0) + 1; }
      const rec = Object.keys(wins).sort((a, b) => wins[b] - wins[a]);
      h('div', 'sg-tny-h', '冠数', win.page);
      h('div', 'sg-tny-wins', rec.map(n => '<span class="sg-tny-w">' + frame(genOf(n), 30) + esc(n) + ' <b>冠 ×' + wins[n] + '</b>' + (wins[n] >= DEMO.relicAt ? seal(DEMO.relic, 'round') : '') + '</span>').join(''), win.page);
    });
  }

  // ================================================================ 预览（测试与样张）==
  async function preview(view) {
    closeCur();
    const vm = demoModel(view === 'podium' ? 2 : 16);
    switch (view) {
      case 'announce': return announce(vm);
      case 'entry': return entry(vm);
      case 'match': return match(vm.next, vm);
      case 'podium': return podium(vm);
      case 'hall': return hall();
      case 'mine': return bracket(vm, { view: 'mine' });
      default: return bracket(vm);
    }
  }

  SG.TourneyScreen = {
    announce, entry, bracket, match, podium, hall, preview, model, stadium,
    demoModel, order, winChance, cnNum, prizeLine, seedScore,
    FALLBACK, DEMO,
    isOpen() { return !!cur; },
    close: closeCur,
  };
})();
