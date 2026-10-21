'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 指令图标与指令按钮（DOMESTIC-FX §6、§7；INTEGRATION §10.4）
   图标本身是「数据」，与界面共用一份：js/ui-icons.js（SG.UIIcons，W8）里的 37 个指令 / 时刻图标
   （22 个指令与子指令 + 4 个时刻 + ENGAGEMENT 增补的 11 个）。本文件不另画一套，只在其上加：
     · 每个指令的元数据（中文名、主色、短乐、按下声、指令坞的名字），
     · 图标的状态（idle / disabled / great 回响 …）与 160 px 牌面上的四拍动画（cue · action · reveal · great，§6.3），
     · 指令按钮组件（§7：经由 SG.UI.button 的 'tile' 印，保留 data-cmd / data-why / aria-disabled / explainCmd 的行为），
     · 指令坞没有的按下声（营建 / 通商 / 探索）的合成配方（SG.Sfx.defineSfx）。
   SG.UIIcons 不在时（Node、单测）退回一个带首字的简单印面，从不抛错。

   SG.CmdIcons.IDS                           37 个编号
   SG.CmdIcons.META[id]                      { n 名, a 主色（CSS 色值）, tok 主色令牌, sting, press, dock, token }
   SG.CmdIcons.svg(id, { size, state, fc, title, cls }) → SVG 字符串（state：'idle'|'disabled'|'great'|'card'）
   SG.CmdIcons.el(id, opts) → <svg> 元素
   SG.CmdIcons.button(parent, id, label, onClick, { primary, token, why, hotkey }) → HTMLButtonElement
     btn.setWhy(why|null) · btn.echo(tier)（大成：角上留 4 秒小印）· btn.dataset.cmd = label · btn.dataset.icon = id
   SG.CmdIcons.card(id, { size = 160, fc }) → 牌面图标元素（.ci-card）；CmdIcons.beat(el, 'cue'|'action'|'reveal'|'great', { a, t })
   SG.CmdIcons.pressSound(id) → 按下声的名字    playPress(id)    accent(id) → 色值    label(id) → 中文名
   SG.CmdIcons.silhouette(id) → 24 px 剪影用的 SVG 字符串（tests/domestic-fx.html 比较两两差异 ≥ 18 %）
   ========================================================================== */
(function () {
  const W = typeof window !== 'undefined' ? window : globalThis;
  const SG = (W.SG = W.SG || {});
  const hasDoc = typeof document !== 'undefined';

  // 主色（DOMESTIC-FX §6.1 的令牌；与 css/style.css 的 :root 同名，缺省时用规格的色值）
  const TOK = {
    jade: '#4f9a76', ember: '#e07a3a', amber: '#d9933a', cinnabar: '#c8382c', bronze: '#a7743a', indigo: '#3b4f7a', ochre: '#c99a2e',
    plum: '#8a5fc0', gold: '#e8b54a', teal: '#3aa5a0', tealBlue: '#3a7fa0', plumGold: '#a8742e', paper: '#f3e7cc', faction: '#3f7f4a',
  };
  // id: [中文名, 主色令牌, 短乐, 按下声, 指令坞名, 用令牌]
  const RAW = {
    develop: ['开发', 'jade', 'develop.land', 'cmd-press-develop', '开发', 1],
    'develop.land': ['土地', 'jade', 'develop.land', 'cmd-press-develop', '开发', 1],
    'develop.industry': ['产业', 'ember', 'develop.industry', 'cmd-press-industry', '开发', 1],
    'develop.town': ['町', 'amber', 'develop.town', 'cmd-press-town', '开发', 1],
    recruit: ['征兵', 'cinnabar', 'recruit', 'cmd-press-recruit', '征兵', 1],
    train: ['训练', 'bronze', 'train', 'cmd-press-train', '训练', 1],
    search: ['搜索', 'indigo', 'search', 'cmd-press-search', '搜索', 1],
    hire: ['登用', 'ochre', 'hire', 'cmd-press-hire', '登用', 1],
    move: ['移动', 'faction', 'move', 'cmd-press-move', '移动', 1],
    transport: ['输送', 'ochre', 'transport', 'cmd-press-transport', '输送', 1],
    diplomacy: ['外交', 'plum', 'ally', 'cmd-press-diplomacy', '外交', 1],
    ally: ['同盟', 'plum', 'ally', 'cmd-press-diplomacy', '外交', 1],
    discord: ['离间', 'plum', 'discord', 'cmd-press-diplomacy', '外交', 1],
    persuade: ['拉拢', 'plum', 'persuade', 'cmd-press-diplomacy', '外交', 1],
    attack: ['出征', 'gold', 'attack', 'cmd-press-attack', '出征', 1],
    reward: ['赏赐', 'gold', 'reward', 'cmd-press-reward', '赏赐', 0],
    trade: ['交易', 'bronze', 'trade', 'cmd-press-trade', '交易', 0],
    'trade.buy': ['买粮', 'bronze', 'trade', 'cmd-press-trade', '交易', 0],
    'trade.sell': ['卖粮', 'bronze', 'trade', 'cmd-press-trade', '交易', 0],
    appoint: ['任命', 'tealBlue', 'appoint', 'cmd-press-appoint', '任命', 0],
    market: ['市场', 'teal', 'market', 'cmd-press-market', '市场', 0],
    title: ['授官', 'plumGold', 'title', 'cmd-press-title', null, 0],
    harvest: ['秋收', 'ochre', 'harvest', null, null, 0],
    stageUp: ['城池升阶', 'gold', 'stageUp', null, null, 0],
    month: ['月初', 'paper', 'month', null, null, 0],
    endMonth: ['结束本月', 'amber', null, 'cmd-press-end', null, 0],
    build: ['营建', 'ochre', 'build', 'cmd-press-build', '营建', 1],
    caravan: ['通商', 'amber', 'caravan', 'cmd-press-caravan', '通商', 1],
    explore: ['探索', 'indigo', 'explore', 'cmd-press-explore', '探索', 1],
    gift: ['赠礼', 'cinnabar', 'gift', 'cmd-press-diplomacy', '外交', 1],
    marriage: ['联姻', 'cinnabar', 'marriage', 'cmd-press-diplomacy', '外交', 1],
    tribute: ['贡赋', 'bronze', 'tribute', 'cmd-press-diplomacy', '外交', 1],
    hostage: ['质子', 'plum', 'hostage', 'cmd-press-diplomacy', '外交', 0],
    truce: ['停战', 'jade', 'truce', 'cmd-press-diplomacy', '外交', 1],
    tradepact: ['互市', 'teal', 'tradepact', 'cmd-press-diplomacy', '外交', 1],
    submit: ['归附', 'gold', 'submit', null, null, 0],
    mission: ['国策', 'cinnabar', 'mission', null, null, 0],
  };
  // 时刻（节庆、文会）没有自己的指令图标：用界面的图标（季节、书卷）
  const EXTRA_ICON = { festival: 'spring', wenhui: 'chronicle' };
  const IDS = Object.freeze(Object.keys(RAW));
  const META = {};
  for (const [id, r] of Object.entries(RAW)) META[id] = Object.freeze({ n: r[0], tok: r[1], a: TOK[r[1]], sting: r[2], press: r[3], dock: r[4], token: !!r[5] });
  // 节庆 / 文会：只给元数据（短乐、主色），图标借用界面图标
  META.festival = Object.freeze({ n: '节庆', tok: 'cinnabar', a: TOK.cinnabar, sting: 'festival', press: null, dock: null, token: false });
  META.wenhui = Object.freeze({ n: '文会', tok: 'indigo', a: TOK.indigo, sting: 'wenhui', press: null, dock: null, token: false });

  // ---------------------------------------------------------------- 图标 --
  const UIc = () => (SG.UIIcons && typeof SG.UIIcons.svg === 'function' ? SG.UIIcons : null);
  const iconId = id => EXTRA_ICON[id] || id;
  const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  function fallbackSvg(id, size, cls) {
    const m = META[id] || { n: '?', a: TOK.paper };
    const sz = size ? ' width="' + size + '" height="' + size + '"' : '';
    return '<svg class="ico ci-fallback' + (cls ? ' ' + cls : '') + '" viewBox="0 0 64 64"' + sz + ' xmlns="http://www.w3.org/2000/svg" aria-hidden="true">' +
      '<rect x="6" y="6" width="52" height="52" rx="12" fill="' + m.a + '" stroke="#241a21" stroke-width="2.5"/>' +
      '<text x="32" y="42" font-size="26" text-anchor="middle" fill="#f3e7cc" font-family="STKaiti,KaiTi,serif">' + esc(m.n.charAt(0)) + '</text></svg>';
  }
  function svg(id, opts) {
    opts = opts || {};
    const st = opts.state && opts.state !== 'idle' ? ' ci-st-' + opts.state : '';
    const cls = 'ci ci-' + String(id).replace(/\./g, '-') + st + (opts.cls ? ' ' + opts.cls : '');
    const U = UIc();
    if (!U || !U.has(iconId(id))) return fallbackSvg(id, opts.size, cls);
    try { return U.svg(iconId(id), opts.size, { cls, fc: opts.fc, title: opts.title }); } catch (e) { return fallbackSvg(id, opts.size, cls); }
  }
  function el(id, opts) {
    if (!hasDoc) return null;
    const t = document.createElement('template');
    t.innerHTML = svg(id, opts);
    return t.content.firstElementChild;
  }
  // 24 px 剪影：所有填色与描边换成墨色（tests/domestic-fx.html 栅格化后比较两两差异）
  function silhouette(id) {
    return svg(id, { size: 24 }).replace(/fill="(?!none)[^"]*"/g, 'fill="#000"').replace(/stroke="(?!none)[^"]*"/g, 'stroke="#000"').replace(/opacity="[^"]*"/g, '');
  }
  const accent = id => (META[id] ? META[id].a : TOK.paper);
  const label = id => (META[id] ? META[id].n : String(id));

  // ---------------------------------------------------------------- 按下声 --
  const PRESS_FALLBACK = { 'cmd-press-build': 'cmd-press-town', 'cmd-press-caravan': 'cmd-press-move', 'cmd-press-explore': 'cmd-press-search' };
  function pressSound(id) {
    const m = META[id];
    let n = m && m.press;
    if (!n) return null;
    const R = SG.Sfx && SG.Sfx._recipes;
    if (R && !R[n] && PRESS_FALLBACK[n] && R[PRESS_FALLBACK[n]]) n = PRESS_FALLBACK[n];
    return n;
  }
  function playPress(id) { try { const n = pressSound(id); if (n && SG.Sfx && SG.Sfx.play) SG.Sfx.play(n, 0.9); } catch (e) { /* 无音频 */ } }
  // 营建（槌敲木）、通商（驼铃）、探索（杖点石）三个按下声（DOMESTIC-FX §5.18–§5.20）：合成配方，登记一次
  function defineSounds() {
    const S = SG.Sfx;
    if (!S || typeof S.defineSfx !== 'function' || !S._recipes) return;
    const TWO_PI = 2 * Math.PI;
    const env = (t, a, d) => (t < a ? t / a : Math.exp(-(t - a) / d));
    const add = (name, dur, level, build) => { if (!S._recipes[name]) try { S.defineSfx(name, { dur, level, build }); } catch (e) { /* 已有 */ } };
    add('cmd-press-build', 0.16, -15, (d, R, Rate) => {
      for (const t0 of [0, 0.07]) {
        const i0 = Math.round(t0 * Rate);
        let lp = 0;
        for (let i = 0; i + i0 < d.length && i < 0.06 * Rate; i++) {
          const t = i / Rate; lp += ((R.nextDouble() * 2 - 1) - lp) * 0.35;
          d[i0 + i] += (lp * 0.7 + Math.sin(TWO_PI * 180 * t) * 0.6) * env(t, 0.0008, 0.014);
        }
      }
    });
    add('cmd-press-caravan', 0.42, -18, (d, R, Rate) => {
      const P = [[1046, 0.6, 0.18], [2510, 0.3, 0.12], [3700, 0.15, 0.08]];
      for (let i = 0; i < d.length; i++) { const t = i / Rate; let s = 0; for (const p of P) s += Math.sin(TWO_PI * p[0] * t) * p[1] * env(t, 0.002, p[2]); d[i] += s * (1 + 0.3 * Math.sin(TWO_PI * 7 * t)); }
    });
    add('cmd-press-explore', 0.18, -16, (d, R, Rate) => {
      let lp = 0;
      for (let i = 0; i < d.length; i++) {
        const t = i / Rate; lp += ((R.nextDouble() * 2 - 1) - lp) * 0.6;
        d[i] += (lp * 0.5 * env(t, 0.0005, 0.01) + Math.sin(TWO_PI * 2300 * t) * 0.35 * env(t, 0.001, 0.03) + Math.sin(TWO_PI * 140 * t) * 0.4 * env(t, 0.001, 0.025));
      }
    });
  }
  defineSounds();

  // ---------------------------------------------------------------- 样式（一次注入）--
  // 牌面四拍（§6.3）：cue 盖印入场 · action 图标的动作部分（.ci-a）按短乐的拍子做 2–3 次 · reveal 让位一跳 · great 主色漫过
  const CSS = `
.ci-card { display: inline-grid; place-items: center; position: relative; width: var(--ci-size, 160px); height: var(--ci-size, 160px); }
.ci-card > .ico { width: 100%; height: 100%; overflow: visible; filter: drop-shadow(0 3px 0 rgba(36,26,33,.35)); }
.ci-card .ci-a { transform-box: fill-box; transform-origin: 50% 70%; }
.ci-card.is-cue > .ico { animation: ci-k-cue .2s cubic-bezier(.34,1.56,.64,1) both; }
.ci-card.is-cue::before { content: ""; position: absolute; inset: 8%; border-radius: 50%; border: 6px solid rgba(36,26,33,.55); animation: ci-k-ring .45s ease-out both; pointer-events: none; }
.ci-card.is-action .ci-a { animation: var(--ci-k, ci-k-pop) var(--ci-beat, .5s) ease-in-out var(--ci-n, 3); }
.ci-card.is-reveal > .ico { animation: ci-k-reveal .3s cubic-bezier(.34,1.56,.64,1) both; }
.ci-card.is-great::after { content: ""; position: absolute; inset: -12%; border-radius: 50%; background: radial-gradient(closest-side, var(--ci-accent, #e8b54a), transparent); opacity: 0; animation: ci-k-flood 1.2s ease-out both; pointer-events: none; z-index: -1; }
@keyframes ci-k-cue { 0% { transform: scale(1.25); opacity: 0; } 60% { opacity: 1; } 100% { transform: scale(1); } }
@keyframes ci-k-ring { 0% { transform: scale(.6); opacity: .8; } 100% { transform: scale(1.35); opacity: 0; } }
@keyframes ci-k-reveal { 0% { transform: scale(1); } 40% { transform: scale(1.08) translateY(-4px); } 100% { transform: scale(1); } }
@keyframes ci-k-flood { 0% { opacity: 0; transform: scale(.6); } 35% { opacity: .75; } 100% { opacity: .25; transform: scale(1.1); } }
@keyframes ci-k-sway { 30% { transform: rotate(-12deg); } 65% { transform: rotate(9deg); } }
@keyframes ci-k-tap { 35% { transform: translateY(5px) rotate(-6deg) scale(.95); } 60% { transform: translateY(-2px); } }
@keyframes ci-k-swing { 35% { transform: rotate(-18deg); } 70% { transform: rotate(9deg); } }
@keyframes ci-k-shake { 20% { transform: translateX(-3px) rotate(-4deg); } 45% { transform: translateX(3px) rotate(4deg); } 70% { transform: translateX(-1px); } }
@keyframes ci-k-step { 40% { transform: translateX(6px); } 70% { transform: translateX(-2px); } }
@keyframes ci-k-pop { 40% { transform: scale(1.22); } 70% { transform: scale(.96); } }
@keyframes ci-k-hop { 40% { transform: translateY(-8px); } 70% { transform: translateY(2px); } }
@keyframes ci-k-sink { 45% { transform: translateY(6px); } }
@keyframes ci-k-spin { to { transform: rotate(360deg); } }
@keyframes ci-k-unroll { 0% { transform: scaleY(.6); } 60% { transform: scaleY(1.06); } }
.sg-btn.sg-cmdbtn.is-echo::before { content: attr(data-echo); position: absolute; inset: auto; top: -6px; left: -6px; right: auto; bottom: auto; width: auto; height: auto; z-index: 3; opacity: 1;
  transform: rotate(-10deg); animation: none; padding: 1px 3px; border-radius: 3px; background: #c8382c; color: #fff4e0; font: 700 11px/1.1 "STKaiti","KaiTi",serif;
  letter-spacing: 0; border: 1.5px solid #241a21; box-shadow: none; mask: none; -webkit-mask: none; }
html.sg-reduced .ci-card *, html.sg-motion-off .ci-card *, html.sg-reduced .ci-card, html.sg-motion-off .ci-card { animation: none !important; }
html.sg-reduced .ci-card::before, html.sg-reduced .ci-card::after, html.sg-motion-off .ci-card::before, html.sg-motion-off .ci-card::after { display: none; }
`;
  function injectCss() {
    if (!hasDoc || document.getElementById('sg-cmdicons-css')) return;
    const s = document.createElement('style');
    s.id = 'sg-cmdicons-css';
    s.textContent = CSS;
    (document.head || document.documentElement).appendChild(s);
  }
  // 图标的悬停微动画（ui-icons.js 的 h）→ 牌面的动作关键帧
  const MOTION = {
    sway: 'sway', wave: 'sway', flutter: 'sway', flap: 'sway', tap: 'tap', nod: 'tap', bow: 'tap', stamp: 'tap', drop: 'tap', snap: 'tap',
    swing: 'swing', tilt: 'swing', tip: 'swing', turn: 'swing', wobble: 'shake', shake: 'shake', crack: 'shake', step: 'step', slide: 'step',
    nudge: 'step', drift: 'step', roll: 'step', pulse: 'pop', pop: 'pop', twinkle: 'pop', gleam: 'pop', blink: 'pop', hop: 'hop', bounce: 'hop',
    rise: 'hop', lift: 'hop', sink: 'sink', spin: 'spin', unroll: 'unroll',
  };
  function motionOf(id) {
    const U = UIc();
    const d = U && U.get ? U.get(iconId(id)) : null;
    return MOTION[(d && d.h) || 'pop'] || 'pop';
  }
  function card(id, opts) {
    opts = opts || {};
    if (!hasDoc) return null;
    injectCss();
    const box = document.createElement('div');
    box.className = 'ci-card';
    box.style.setProperty('--ci-size', (opts.size || 160) + 'px');
    box.style.setProperty('--ci-accent', opts.fc && id === 'move' ? opts.fc : accent(id));
    box.style.setProperty('--ci-k', 'ci-k-' + motionOf(id));
    box.innerHTML = svg(id, { state: 'card', fc: opts.fc });
    box.dataset.icon = id;
    return box;
  }
  // 牌面的一拍：a = 动作段长（秒），t = 全长；action 按短乐的拍子做 n 次
  function beat(box, name, o) {
    if (!box) return;
    o = o || {};
    if (name === 'action') {
      const a = Math.max(0.3, o.a || 1);
      const n = Math.max(1, Math.min(4, Math.round(a / 0.45)));
      box.style.setProperty('--ci-n', String(n));
      box.style.setProperty('--ci-beat', (a / n).toFixed(3) + 's');
    }
    box.classList.remove('is-cue', 'is-action', 'is-reveal');
    void box.offsetWidth;   // 重新触发
    box.classList.add('is-' + name);
  }

  // ---------------------------------------------------------------- 指令按钮（§7）--
  function button(parent, id, labelText, onClick, opts) {
    opts = opts || {};
    injectCss();
    const m = META[id] || {};
    const text = labelText === undefined || labelText === null ? m.n || id : labelText;
    let b;
    if (SG.UI && typeof SG.UI.button === 'function') {
      b = SG.UI.button(null, SG.esc ? SG.esc(text) : esc(text), onClick, { kind: 'tile', icon: iconId(id), token: opts.token !== undefined ? !!opts.token : !!m.token, hotkey: opts.hotkey, sound: 'none', why: opts.why || null, className: 'sg-cmdbtn' });
    } else {
      b = document.createElement('button');
      b.type = 'button';
      b.className = 'sg-btn k-tile sg-cmdbtn';
      b.innerHTML = svg(id) + '<span class="sg-tile-lb">' + esc(text) + '</span>';
      if (onClick) b.addEventListener('click', e => { if (!b._why) onClick(e); });
      b.setEnabled = on => { b._why = typeof on === 'string' ? on : null; b.disabled = on === false; return b; };
      if (opts.why) b.setEnabled(opts.why);
    }
    b.dataset.cmd = text;
    b.dataset.icon = id;
    if (opts.primary) b.classList.add('primary-tile');
    b.addEventListener('pointerdown', e => { if ((e.button === 0 || e.button === undefined) && !b._why && !b.disabled) playPress(id); });
    b.addEventListener('keydown', e => { if ((e.key === 'Enter' || e.key === ' ') && !e.repeat && !b._why && !b.disabled) playPress(id); });
    b.setWhy = why => { if (b.setEnabled) b.setEnabled(why ? String(why) : true); return b; };
    b.echo = tier => {
      if (tier !== 'great') return b;
      const st = SG.DomesticTiers && SG.DomesticTiers.STAMPS;
      b.dataset.echo = (st && st[id]) || '大成';
      b.classList.add('is-echo');
      clearTimeout(b._echoT);
      b._echoT = setTimeout(() => b.classList.remove('is-echo'), 4000);
      return b;
    };
    if (parent) parent.appendChild(b);
    return b;
  }

  SG.CmdIcons = { IDS, META, TOK, svg, el, silhouette, card, beat, button, pressSound, playPress, accent, label, iconId, motionOf, injectCss };
})();
