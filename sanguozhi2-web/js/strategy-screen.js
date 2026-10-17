'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 战略画面
   ← Strategy/StrategyScreen.cs：顶栏、城池情报、指令与月份循环
   协程 → async 函数。new SG.StrategyScreen().run() → Promise（游戏结束时重载页面）。
   第二版 §4F 调动武将：
     「移动」「输送」的目的地 = 经由己方城池相连可达的所有己方城（SG.Commands.moveTargets），
     列表注明路程（相邻 / 经 N 城）与途经城名，行军动画沿路线逐城前进；
     灰色指令按钮被点按时以提示说明原因（addCmd(name, why, …)、explainCmd）；
     第一次占领新城后弹一次「移动」提示并让「移动」按钮闪烁（moveTip / hintCmd，本机只弹一次）；
     只在「移动」此刻真能用时弹出（有令牌、某座己方城有未行动的武将可调），否则留到下个月月初。
   第二版 §4G 世界剧本：
     地图音乐按玩家势力的文化（SG.Sfx.setCulture）；点按城池面板的武将行 → 武将详情（大头像、能力、
     必杀技名称与说明；世界武将另有全名、原名、身份、生卒与史实介绍，见 generalInfo）；
     「势力」列表与外交的同盟对象在世界剧本中按地域分组（groupedFactions）。
     世界地图的城名在远景（拉远到 340）也显示，距离超过约 175 时改为紧凑样式，由避让逻辑按优先级取舍；
     月末消息优先显示与玩家相关的（我方、本地域与邻国），见 pickNews。
   第三版 §5 头像无处不在（非战斗阶段）：
     所有列出武将的列表（指令选人、出征、移动、登用、俘虏、外交、势力一览……）每行带头像（SG.UI.item 的 avatar）；
     指令结果由执行的武将以头像汇报（report）；月末消息由军师汇报，文中提到的武将 / 势力（→ 君主）带小头像（SG.UI.faces）；
     急报以来犯主将的头像示警；俘虏、登用确认显示头像名片（SG.UI.genCard）；「大事」查看记录（带小头像）。
   第三版 §4 成长界面（规则见 model.js 的 SG.Progress）：
     玩家武将不再自动习得（Progress.playerAutoLearn = false）。每场战斗（亲自指挥 / 委任 / 空城）之后：
     growth(s) → 升级通知（头像、「关羽 升至 5 级」、经验条、新解锁的格）→ 对每位有空格的我方武将弹出习得对话框
     （learnDialog：3 个候选必杀 / 计策，显示等阶、说明、数值摘要与武将头像；「稍后再选」保留空格）。
     武将详情：等级、经验条、战绩、全部必杀（专属在前）与计策（含说明）及格数；有空格时「习得新招」按钮。
     城池面板的武将行显示等级（可习得新招时另有「新」标记）；月初若有保留的空格则提示一次。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const CINNABAR = '#c8382c';
  const GREY_SPEAKER = '#666673';               // C# new Color(0.4, 0.4, 0.45)
  const LABEL_HIDE_BEYOND = 170;                // C# WorldFollow.hideBeyond = 170
  const LABEL_HIDE_BEYOND_WORLD = 420;          // 世界地图可拉远到 340：远景也显示（避让后剩下的）城名
  const LABEL_FAR_COMPACT = 175;                // 世界地图镜头距离超过约 175 时城名改为紧凑样式
  const LABEL_FADE = 0.18;                      // 城名标签避让时的淡入淡出时长（秒）

  // 屏幕矩形 { l, t, r, b } 是否相交
  function overlaps(a, b) { return a.l < b.r && a.r > b.l && a.t < b.b && a.b > b.t; }

  function G() { return SG.G; }
  function UI() { return SG.UI; }
  function sfx(name, vol) { try { if (SG.Sfx) SG.Sfx.play(name, vol === undefined ? 0.8 : vol); } catch (e) { /* 无音频 */ } }
  function click() { try { if (SG.Sfx) SG.Sfx.click(); } catch (e) { /* 无音频 */ } }
  function music(kind) { try { if (SG.Sfx) SG.Sfx.music(kind); } catch (e) { /* 无音频 */ } }
  // 地图音乐的地域风格 = 玩家势力的文化（世界剧本；经典剧本为 han）
  function mapMusic() {
    try { const g = G(); if (SG.Sfx && SG.Sfx.setCulture && g && g.playerFaction) SG.Sfx.setCulture(g.playerFaction.culture || 'han'); } catch (e) { /* 无音频 */ }
    music('map');
  }
  // 世界剧本的地域表（经典剧本为 null）
  function regionsOf() {
    const g = G();
    const R = g && g.scenario === 'world' && SG.ScenarioData && SG.ScenarioData.Regions;
    return R && R.length ? R : null;
  }
  // 势力列表按地域分组：分组标题行（不可选）+ 各势力行。玩家所在地域排在最前。
  // fs：势力数组（已排序）；row(f) → SG.UI.item。→ { items, pick: 列表下标 → 势力 | null, heads: 标题行下标 }
  function groupedFactions(fs, row) {
    const regs = regionsOf();
    if (!regs) return { items: fs.map(row), pick: i => fs[i] || null, heads: [] };
    const g = G();
    const mine = g.playerFaction ? g.playerFaction.region : null;
    const near = regionNearness(regs);
    const order = regs.slice().sort((a, b) => ((b.id === mine) - (a.id === mine)) || (near[a.id] - near[b.id]));
    const items = [], map = [], heads = [];
    for (const r of order) {
      const list = fs.filter(f => f.region === r.id);
      if (!list.length) continue;
      heads.push(items.length);
      items.push(item(`<span class="sg-group-head">${SG.esc(r.name)}</span>`, `势力 ${list.length}　城 ${sum(list, f => g.cityCount(f.id))}`, false));
      map.push(null);
      for (const f of list) { items.push(row(f)); map.push(f); }
    }
    const rest = fs.filter(f => !map.includes(f));
    for (const f of rest) { items.push(row(f)); map.push(f); }
    return { items, pick: i => map[i] || null, heads };
  }
  // 各地域离我方的远近：自我方城池沿连线到该地域势力所据城池的最少步数（并列时按地图上的直线距离），
  // 使邻近的地域排在前面，而不是固定把中原十八路诸侯放在第二组
  function regionNearness(regs) {
    const g = G(), out = {};
    const hops = new Array(g.cities.length).fill(Infinity);
    let q = g.citiesOf(g.player).map(c => c.id);
    for (const id of q) hops[id] = 0;
    for (let d = 1; q.length; d++) {
      const next = [];
      for (const id of q) for (const j of g.cities[id].links) if (hops[j] === Infinity) { hops[j] = d; next.push(j); }
      q = next;
    }
    const capC = g.playerFaction && g.ruler(g.player) ? g.cities[g.ruler(g.player).city] : null;
    const cp = capC && SG.mapPos ? SG.mapPos(capC) : null;
    for (const r of regs) {
      let h = Infinity, dx = 0, n = 0;
      for (const c of g.cities) {
        if (c.owner < 0 || g.factions[c.owner].region !== r.id) continue;
        h = Math.min(h, hops[c.id]);
        if (cp) { const p = SG.mapPos(c); dx += Math.hypot(p.x - cp.x, p.y - cp.y); n++; }
      }
      out[r.id] = (h === Infinity ? 1e6 : h * 1e4) + (n ? dx / n : 0);
    }
    return out;
  }
  // 给最上层列表对话框的分组标题行加上样式类
  function markGroupHeads(heads) {
    if (!heads || !heads.length) return;
    const ms = document.querySelectorAll('.sg-modals .sg-modal:not(.is-closing)');
    const m = ms[ms.length - 1];
    if (!m) return;
    const bs = m.querySelectorAll('.sg-item');
    for (const i of heads) if (bs[i]) { bs[i].classList.add('sg-item-group'); bs[i].tabIndex = -1; }
  }
  function item(label, right, enabled, desc, opts) {
    if (SG.UI && SG.UI.item) return SG.UI.item(label, right === undefined ? null : right, enabled === undefined ? true : enabled, desc === undefined ? null : desc, opts);
    const it = { label, right: right === undefined ? null : right, enabled: enabled === undefined ? true : enabled, desc: desc === undefined ? null : desc, selected: false };
    if (opts && opts.avatar) it.avatar = opts.avatar;
    return it;
  }
  // 头像辅助（SG.UI 未载入时退化为纯文字）
  function faces(html, opts) { try { return SG.UI && SG.UI.faces ? SG.UI.faces(html, opts) : html; } catch (e) { return html; } }
  function face(gen) { try { return SG.UI && SG.UI.face ? SG.UI.face(gen) : ''; } catch (e) { return ''; } }
  function genCard(gen, opts) { try { return SG.UI && SG.UI.genCard ? SG.UI.genCard(gen, opts) : ''; } catch (e) { return ''; } }
  // ---- 成长（第三版 §4） ----
  function Prog() { const P = SG.Progress; return P && P.enabled !== false && typeof P.expInfo === 'function' ? P : null; }
  function lvTag(gen, cls) { try { return SG.UI && SG.UI.lvTag ? SG.UI.lvTag(gen, cls) : ''; } catch (e) { return ''; } }
  function expBar(pct, cls) { try { return SG.UI && SG.UI.expBar ? SG.UI.expBar(pct, cls) : ''; } catch (e) { return ''; } }
  // 有空格可学（openings 只在载入 tactics-data.js 时计计策格）
  function canLearn(gen) {
    const P = Prog();
    if (!P || !gen) return false;
    try { return P.openings(gen).total > 0 && P.candidates(gen, 1).length > 0; } catch (e) { return false; }
  }
  function tierText(c) {
    if (c.type === 'special') return SG.Specials && SG.Specials.tierName ? SG.Specials.tierName(c.tier) : c.tier + '阶';
    return SG.Tactics && SG.Tactics.TIER_NAME ? SG.Tactics.TIER_NAME[c.tier] : c.tier + '阶';
  }
  function spColor(x) {
    try { if (SG.Specials && SG.Specials.uiColor) return SG.Specials.uiColor(x.color || '#f3c969'); } catch (e) { /* 原色 */ }
    return x.color || '#f3c969';
  }
  function shortScreen() { return window.innerHeight > 0 && window.innerHeight < 560; }
  // 说明去掉开头的典故（lore）：典故另行显示
  function stripLore(desc, lore) { desc = String(desc || ''); return lore && desc.indexOf(lore) === 0 ? desc.slice(lore.length) : desc; }
  // 空格可学到的等阶：第 k 格至多 k 阶，另受武力 / 智力与等级限制 → 「二阶」或「初阶至中阶」（开着多格时）
  function openTiers(sl, tierName) {
    if (!(sl.open > 0) || !(sl.tier > 0)) return { text: '', top: 0 };
    const top = Math.max(sl.tier, Math.min(sl.statTier, sl.levelTier, sl.used + sl.open));
    return { text: top > sl.tier ? tierName(sl.tier) + '至' + tierName(top) : tierName(sl.tier), top };
  }
  // 格数说明：「已学 1 / 4 · 可习得 1（二阶）· 上限四阶」「已学 1 / 3 · 6 级可学下一招 · 上限三阶」
  // 括号里是眼下空格可学到的等阶（受等级与已学数限制）；「上限」是此人武力 / 智力最终能学到的等阶
  function slotText(gen, type) {
    const P = Prog();
    if (!P) return '';
    const sp = type === 'special';
    const sl = sp ? P.specialSlots(gen) : P.tacticSlots(gen);
    const stat = sp ? '武力' : '智力';
    if (sl.max <= 0) return stat + ' ' + (sp ? P.SPECIAL_SLOT_WAR[0] : P.TACTIC_SLOT_INT[0]) + ' 以上方可习得';
    const lvls = sp ? P.SPECIAL_SLOT_LEVEL : P.TACTIC_SLOT_LEVEL;
    const tierName = t => (sp ? (SG.Specials && SG.Specials.tierName ? SG.Specials.tierName(t) : t + '阶') : (SG.Tactics ? SG.Tactics.TIER_NAME[t] : t + '阶'));
    const p = ['已学 ' + sl.used + ' / ' + sl.max];
    const ot = openTiers(sl, tierName);
    if (sl.open > 0) p.push('<span class="sg-good">可习得 ' + sl.open + (ot.text ? '（' + ot.text + '）' : '') + '</span>');
    else if (sl.unlocked < sl.max) p.push(lvls[sl.unlocked] + ' 级可学下一招');
    else if (sl.used >= sl.max) p.push('已满');
    // 最终上限：第 k 格至多 k 阶，满级时等级不再限制，故为 min(武力 / 智力等阶, 格数)
    const cap = Math.min(sl.statTier, sl.max);
    if (sl.used < sl.max && cap > ot.top) p.push('上限' + tierName(cap));
    return p.join(' · ');
  }

  // 武将的身份小字：君 / 太守 / 势力名
  function genSub(gen) {
    const g = G();
    if (!gen) return '';
    const f = gen.faction >= 0 ? g.factions[gen.faction] : null;
    const fn = f ? `<span style="color:${lifted(f.color)}">${SG.esc(f.name)}</span>` : '<span class="sg-muted">在野</span>';
    const role = f && g.isRuler(gen) ? '君主' : g.cities[gen.city] && g.cities[gen.city].governor === gen.id && f ? '太守' : '';
    return fn + (role ? '·<wbr>' + role : '');   // 窄屏时长势力名在「·」后折行
  }
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  // C# 的 {0:P0}
  function pct(p) { return Math.round(p * 100) + '%'; }
  // C# 的 {0:N0}
  function n0(v) { return Math.round(v).toLocaleString('en-US'); }
  function sum(list, fn) { let s = 0; for (const x of list) s += fn(x); return s; }
  function swatch(color) { return `<span class="sg-dot" style="color:${color}">■</span>`; }
  // 深色势力色作为文字时提亮，保证在漆黑面板上可读（标记 ■● 仍用原色）
  function readable(hex) {
    const c = SG.hexToRgb(hex);
    const L = 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    if (L >= 0.5) return hex;
    const k = M.clamp((0.5 - L) * 1.3, 0, 0.6);
    return SG.rgbToHex(c[0] + (1 - c[0]) * k, c[1] + (1 - c[1]) * k, c[2] + (1 - c[2]) * k);
  }
  // 小字用的势力色：比 readable 提得更亮（说话人身份、名片副标题）
  function lifted(hex) {
    const c = SG.hexToRgb(hex);
    const L = 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    if (L >= 0.62) return hex;
    const k = M.clamp((0.62 - L) * 1.5, 0, 0.75);
    return SG.rgbToHex(c[0] + (1 - c[0]) * k, c[1] + (1 - c[1]) * k, c[2] + (1 - c[2]) * k);
  }
  function dot(color, glyph) { return `<span class="sg-dot" style="color:${color}">${glyph || '●'}</span>`; }

  // ---------------------------------------------------------- 调动武将（第二版 §4F）--
  // 路程文字：相邻 / 经 N 城（N = 途经的己方城数）
  function routeText(rt) { return rt.hops <= 1 ? '相邻' : '经 ' + (rt.hops - 1) + ' 城'; }
  // 途经城名（最多列 4 座）
  function viaText(g, rt) {
    if (rt.hops <= 1) return null;
    const mid = rt.path.slice(1, -1).map(i => SG.esc(g.cities[i].name));
    return '途经 ' + (mid.length > 4 ? mid.slice(0, 4).join('、') + ' 等 ' + mid.length + ' 城' : mid.join('、'));
  }
  // 第一次占领新城后的提示只弹一次（存于本机；无存储时本次会话内只弹一次）
  const TIP_KEY = 'sanguozhi2_tip_move';
  let tipShownMem = false;
  function tipShown() {
    if (tipShownMem) return true;
    try { return !!(window.localStorage && window.localStorage.getItem(TIP_KEY)); } catch (e) { return false; }
  }
  function markTipShown() {
    tipShownMem = true;
    try { if (window.localStorage) window.localStorage.setItem(TIP_KEY, '1'); } catch (e) { /* 忽略 */ }
  }
  // 灰色指令按钮不用原生 disabled（原生禁用的按钮收不到点按，触屏上轻点还会被“触摸校正”吸到相邻的可用按钮），
  // 而用 aria-disabled="true"：外观与 .sg-btn:disabled 相同，悬停 / 按下无反应，但能接住点按并说明原因。
  function injectStyle() {
    if (typeof document === 'undefined' || document.getElementById('sg-strategy-cmd-style')) return;
    const st = document.createElement('style');
    st.id = 'sg-strategy-cmd-style';
    st.textContent = `
.sg-cmdgrid .sg-btn[aria-disabled="true"],
.sg-cmdgrid .sg-btn[aria-disabled="true"]:hover,
.sg-cmdgrid .sg-btn[aria-disabled="true"]:active {
  cursor: default;
  opacity: .5;
  color: rgba(245, 237, 219, .8);
  filter: grayscale(.6) brightness(.85);
  box-shadow: none;
  transform: none;
}
.sg-cmdgrid .sg-btn[aria-disabled="true"]:hover { border-color: rgba(243, 201, 105, .34); }
.sg-cmdgrid .sg-btn.primary[aria-disabled="true"]:hover { border-color: rgba(243, 201, 105, .9); }
.sg-cmdgrid .sg-btn.sg-cmd-deny { animation: sg-cmd-deny .38s ease; }
@keyframes sg-cmd-deny { 0%, 100% { translate: 0 0; } 20% { translate: -5px 0; } 40% { translate: 5px 0; } 60% { translate: -3px 0; } 80% { translate: 2px 0; } }
.sg-cmdgrid .sg-btn.sg-cmd-hint { animation: sg-cmd-hint 1.1s ease-in-out 4; }
.sg-cmdgrid .sg-btn.sg-cmd-hint.sg-cmd-deny { animation: sg-cmd-deny .38s ease; }
@keyframes sg-cmd-hint {
  0%, 100% { box-shadow: 0 0 0 0 rgba(243, 201, 105, 0); }
  50% { border-color: #f3c969; box-shadow: 0 0 0 2px rgba(243, 201, 105, .95), 0 0 1.4rem rgba(243, 201, 105, .65); }
}
.sg-toast .sg-cmd-name { color: #f3c969; font-weight: 700; margin-right: .5em; }`;
    document.head.appendChild(st);
  }

  function layer(name) {
    const ui = SG.UI;
    if (ui) {
      if (typeof ui.layer === 'function') { try { const l = ui.layer(name); if (l) return l; } catch (e) { /* 继续 */ } }
      if (ui.layers && ui.layers[name]) return ui.layers[name];
      if (ui[name] && ui[name].nodeType === 1) return ui[name];
    }
    const root = document.getElementById('ui') || document.body;
    return root.querySelector('[data-layer="' + name + '"], .sg-layer-' + name + ', #sg-' + name + ', .sg-' + name) || root;
  }

  // ========================================================== StrategyScreen --
  // 升级对话框由本画面负责：玩家武将保留空格，战后逐个选择（电脑武将仍自动习得）
  if (SG.Progress) SG.Progress.playerAutoLearn = false;

  class StrategyScreen {
    constructor() {
      this.hud = null; this.cityPanel = null; this.cmdGrid = null; this.genList = null;
      this.topLeft = null; this.topCenter = null; this.cityTitle = null; this.cityStats = null;
      this.endBtn = null; this.saveBtn = null;
      this.labels = new Map();        // cityId → { el, handle, cv, w, h, fade, on, seen }
      this.selected = -1;
      this.busy = false;
      this.endMonth = false;
      this._hint = null;              // 正在闪烁的指令按钮 { name, until }
      this._tipPending = false;       // 已取得新城、「移动」提示待弹（等到「移动」可用时）
      this._tipCities = [];           // 新取得的城（提示时优先选中）
      this._onMapTap = (x, y) => this.onMapTap(x, y);
      // 城名标签避让
      this._labelsOn = true;
      this._occ = [];                 // 遮挡标签的界面矩形（顶栏、城池面板）
      this._occAt = -1e9; this._occDirty = true;
      this._sizeAt = -1e9; this._sizeDirty = true;
      this._declutterLast = 0;
      this._compact = false;
    }

    // ---------------------------------------------------------- 主循环 --
    async run() {
      const game = SG.Game;
      this.buildHud();
      game.rig.onTap.push(this._onMapTap);
      mapMusic();
      const cap = G().cities[G().ruler(G().player).city];
      const p = SG.mapPos(cap);
      game.rig.focusMap(p.x, p.y, 48);
      this.selectCity(cap.id);
      UI().banner(G().year + '年 ' + G().month + '月', G().playerFaction.name + '军');
      this.growthReminder();
      for (;;) {
        this.endMonth = false;
        this.refreshAll();
        while (!this.endMonth) await SG.frame();
        this.busy = true;
        this.refreshAll();
        UI().toast('诸侯行动中……', 1.2);
        await SG.wait(0.4);
        const news = [];
        const battles = SG.StrategyAI.runAI(news);
        game.map.refresh(G()); this.refreshAll();
        for (const b of battles) {
          // 同月先到的进攻可能已使该城易主：不再针对玩家的就按电脑之间结算（或撤兵），不再向玩家告急
          if (!SG.StrategyAI.recheckBattle(b, news)) { game.map.refresh(G()); this.refreshAll(); continue; }
          await this.defend(b);
          if (!G().playerFaction.alive) break;
        }
        if (G().playerFaction.alive) for (const n of SG.StrategyAI.endMonth()) news.push(n);
        game.map.refresh(G()); this.refreshAll();
        if (!G().playerFaction.alive) { await this.gameOver(false); return; }
        if (G().cities.every(c => c.owner === G().player)) { await this.gameOver(true); return; }
        if (news.length > 0) await this.sayNews(news);
        try { G().save(); } catch (e) { console.warn(e); }
        UI().banner(G().year + '年 ' + G().month + '月', '令牌 ' + G().tokens + ' 枚', 1.8);
        // 上月取得新城时「移动」还不能用（武将都已行动 / 令牌用完）：月初横幅之后补弹提示
        if (this._tipPending && this.moveUsableCity()) await SG.wait(1.2);
        this.busy = false;
        this.growthReminder();
        if (this._tipPending) await this.moveTip(false);
      }
    }

    // 月末消息：世界剧本中先列与玩家相关的（提到我方、本地域势力或与我方接壤的势力），至多 6 条
    pickNews(news) {
      const g = G();
      if (g.scenario !== 'world' || news.length <= 6) return news.slice(0, 6).join('\n');
      const me = g.playerFaction;
      const names = new Set([me.name]);
      for (const f of g.factions) if (f.alive && f.region === me.region) names.add(f.name);
      for (const c of g.citiesOf(g.player)) for (const j of c.links) { const o = g.cities[j].owner; if (o >= 0) names.add(g.factions[o].name); }
      const near = n => { for (const k of names) if (n.indexOf(k) >= 0) return true; return false; };
      // 先挑与我方相关的，再按原先的先后顺序排列（同一座城的两次易手不致前后颠倒）
      const idx = news.map((n, i) => i);
      const pick = idx.filter(i => near(news[i])).concat(idx.filter(i => !near(news[i]))).slice(0, 6).sort((a, b) => a - b);
      const list = pick.map(i => news[i]);
      return list.join('\n') + '\n<span class="sg-muted">（各地另有 ' + (news.length - list.length) + ' 条消息）</span>';
    }

    // 月末消息由我方军师（智力最高的部将；没有部将时为君主）汇报，文中武将 / 势力带小头像
    advisor() {
      const g = G();
      const gs = g.generalsOf(g.player).filter(x => !x.dead);
      const ru = g.ruler(g.player);
      const pool = gs.filter(x => x !== ru);
      return (pool.length ? pool.reduce((a, b) => (b.intel > a.intel ? b : a)) : ru) || null;
    }
    sayNews(news) {
      const adv = this.advisor();
      const g = G();
      const role = adv && adv === g.ruler(g.player) ? '君主' : '军师';
      return UI().say(faces(this.pickNews(news), { skip: adv ? [adv.name] : [], max: 10 }), adv, null, { sub: `<span class="sg-say-cmd">${role}</span>本月消息` });
    }

    // 大事记：GameState.log（新的在前），提到的武将 / 势力带小头像
    chronicle() {
      const g = G();
      const log = Array.from(g.log || []).slice().reverse();
      return new Promise(resolve => {
        const m = UI().openModal('sg-chronicle', { width: 820 });
        let done = false;
        const finish = () => { if (done) return; done = true; m.close(); resolve(); };
        m.blocker.addEventListener('click', e => { if (e.target === m.blocker) finish(); });
        m.onKey = k => { if (k === 'Escape' || k === 'Enter' || k === 'Space') { finish(); return true; } return false; };
        const head = h('div', 'sg-dlg-head', null, m.panel);
        h('div', 'sg-dlg-title', '天下大事<small>' + log.length + ' 条</small>', head);
        const close = SG.UI.button(head, '×', finish, { className: 'sg-close', ariaLabel: '关闭' });
        const list = h('div', 'sg-list sg-chron-list', null, m.panel);
        if (!log.length) h('div', 'sg-chron-empty sg-muted', '尚无大事发生。', list);
        for (const line of log) {
          const mt = /^(\d+)年(\d+)月 ([\s\S]*)$/.exec(String(line));
          const row = h('div', 'sg-chron-row', null, list);
          h('span', 'sg-chron-date', mt ? `${mt[1]}<i>年</i>${mt[2]}<i>月</i>` : '', row);
          h('span', 'sg-chron-text', faces(SG.esc(mt ? mt[3] : String(line)), { max: 4 }), row);
        }
        try { close.focus({ preventScroll: true }); } catch (e) { /* 忽略 */ }
      });
    }

    async gameOver(won) {
      const g = G();
      this.busy = true;
      this.refreshAll();
      if (won) {
        sfx('win');
        UI().banner('天下统一', g.ruler(g.player).name + '终成霸业', 4);
        await SG.wait(2.5);
        await UI().say(`${g.year}年${g.month}月，${SG.esc(g.ruler(g.player).name)}平定四海，一统天下。\n麾下武将 ${g.generalsOf(g.player).length} 员，历时 ${g.year - SG.ScenarioData.StartYear} 年。`, g.ruler(g.player), null, { mood: 'win', sub: genSub(g.ruler(g.player)) });
      } else {
        sfx('lose');
        UI().banner('势力灭亡', '霸业未成，身先陨落……', 4);
        await SG.wait(3);
      }
      try {
        if (SG.GameState.deleteSave) SG.GameState.deleteSave();
        else window.localStorage.removeItem('sanguozhi2_save');
      } catch (e) { /* 忽略 */ }
      location.reload();
      await new Promise(() => { /* 等待页面重载 */ });
    }

    onMapTap(x, y) {
      if (UI().anyModal()) return;
      if (this.hud && this.hud.style.display === 'none') return;   // 战斗中（地图隐藏）
      const id = SG.Game.map.pick(x, y);
      this.selectCity(id);
    }

    // ---------------------------------------------------------- 界面 --
    buildHud() {
      const hud = this.hud = SG.UI.screen ? SG.UI.screen('sg-strategy-hud') : h('div', 'sg-screen sg-strategy-hud', null, layer('screens'));

      const top = this.topBar = SG.UI.panel(hud, 'sg-topbar');
      this.topLeft = h('div', 'sg-topbar-left', '', top);
      this.topCenter = h('div', 'sg-topbar-center', '', top);
      const row = h('div', 'sg-topbar-actions', null, top);
      SG.UI.button(row, '势力', () => this.do(() => this.factionInfo()));
      SG.UI.button(row, '大事', () => this.do(() => this.chronicle()), { title: '天下大事（记录）' });
      // 指令或月末处理进行中（令牌已用、军粮已扣、尚未交战等）不可记录，避免存下半途的局面
      this.saveBtn = SG.UI.button(row, '记录', () => {
        if (this.busy || this.endMonth) return;
        let ok = false;
        try { ok = G().save() !== false; } catch (e) { ok = false; }
        UI().toast(ok ? '✦ 进度已记录' : '记录失败');
      });
      SG.UI.button(row, '音乐', () => {
        SG.Sfx.setMusic(!SG.Sfx.musicOn);
        UI().toast(SG.Sfx.musicOn ? '音乐 开' : '音乐 关');
      });
      this.endBtn = SG.UI.button(row, '结束本月', () => { if (!this.busy) this.do(() => this.confirmEnd()); }, { primary: true });

      const cp = this.cityPanel = SG.UI.panel(hud, 'sg-citypanel');
      const head = h('div', 'sg-citypanel-head', null, cp);
      this.cityTitle = h('div', 'sg-citypanel-title', '', head);
      const close = SG.UI.button(head, '×', () => this.selectCity(-1));
      close.classList.add('sg-close');
      close.setAttribute('aria-label', '关闭');
      const body = h('div', 'sg-citypanel-body', null, cp);
      this.cityStats = h('div', 'sg-citystats', '', body);
      this.genList = h('div', 'sg-genlist', null, body);
      this.cmdGrid = h('div', 'sg-cmdgrid', null, cp);
      injectStyle();
      cp.style.display = 'none';

      // 顶栏若因窄屏换行变高，城池面板下移到顶栏之下
      const syncTop = () => {
        cp.style.top = '';
        const tb = top.getBoundingClientRect(), hb = hud.getBoundingClientRect();
        if (!(tb.height > 0)) return;
        const need = Math.ceil(tb.bottom - hb.top + 6);
        const css = parseFloat(getComputedStyle(cp).top) || 0;
        if (need > css) cp.style.top = need + 'px';
      };
      this._syncTop = syncTop;
      const relayout = () => { syncTop(); this._occDirty = true; this._sizeDirty = true; };
      if (window.ResizeObserver) new ResizeObserver(relayout).observe(top);
      window.addEventListener('resize', relayout);

      // 地图上的城名
      this._hideBeyond = G().scenario === 'world' ? LABEL_HIDE_BEYOND_WORLD : LABEL_HIDE_BEYOND;
      for (const cv of SG.Game.map.cities.values()) {
        const el = h('div', 'sg-citylabel', SG.esc(cv.city.name), null);
        const handle = SG.UI.follow(el, () => cv.labelPos, { hideBeyond: this._hideBeyond });
        handle.alpha = 0;             // 首次避让计算后才显示，避免第一帧城名叠在一起
        this.labels.set(cv.city.id, { el, handle, cv, w: 0, h: 0, fade: -1, on: false, seen: false });
      }
      this.startDeclutter();
    }

    setMapLabelsVisible(on) {
      this._labelsOn = !!on;
      for (const l of this.labels.values()) {
        if (l.handle) {
          if (!on) { l.handle.alpha = 0; l.seen = false; }       // 重新显示时由避让逻辑直接定出透明度
        } else l.el.style.visibility = on ? '' : 'hidden';
      }
      if (on) { this._occDirty = true; this.declutterLabels(performance.now(), true); }
    }

    // ---------------------------------------------------------- 城名避让 --
    // 每帧（在主循环投影标签之后）按优先级摆放城名：选中的城 → 我方城池 → 其余（人口多者优先），
    // 与已摆放的标签重叠、或被顶栏 / 城池面板遮住的标签淡出，避免城名互相压住或被界面切掉一半。
    startDeclutter() {
      if (this._declutterOn) return;
      this._declutterOn = true;
      const step = now => {
        requestAnimationFrame(step);
        try { this.declutterLabels(now, false); } catch (e) { if ((this._declutterErr = (this._declutterErr || 0) + 1) <= 3) console.error(e); }
      };
      requestAnimationFrame(step);
    }

    measureLabels(now) {
      for (const l of this.labels.values()) { l.w = l.el.offsetWidth; l.h = l.el.offsetHeight; }
      this._sizeAt = now; this._sizeDirty = false;
    }

    measureOccluders(now) {
      const occ = [];
      const add = (el, toTop) => {
        if (!el || el.style.display === 'none') return;
        const b = el.getBoundingClientRect();
        if (!(b.width > 0 && b.height > 0)) return;
        // 顶栏上方的窄缝也算遮挡：标签伸进去只会露出半截
        occ.push({ l: b.left, t: toTop ? -1e5 : b.top, r: b.right, b: b.bottom });
      };
      if (this.hud && this.hud.style.display !== 'none') { add(this.topBar, true); add(this.cityPanel, false); }
      this._occ = occ; this._occAt = now; this._occDirty = false;
    }

    declutterLabels(now, snap) {
      if (!this.hud || this.labels.size === 0) return;
      const dt = this._declutterLast > 0 ? M.clamp((now - this._declutterLast) / 1000, 0, 0.1) : 0;
      this._declutterLast = now;
      if (!this._labelsOn || this.hud.style.display === 'none') return;
      const Gfx = SG.Gfx;
      if (!Gfx || !Gfx.camera || typeof Gfx.worldToScreen !== 'function') return;
      const g = G();
      if (!g) return;
      // 紧凑模式：视口高度不足 540 且镜头距离超过约 90（带滞后，缩放时不来回切换）
      const dist = SG.Game && SG.Game.rig ? SG.Game.rig.distance : 0;
      const compact = (window.innerHeight < 540 && dist > (this._compact ? 86 : 92)) || dist > LABEL_FAR_COMPACT - (this._compact ? 8 : 0);
      if (compact !== !!this._compact) {
        this._compact = compact;
        for (const [id, l] of this.labels) this.renderLabel(id, l);
        this._sizeDirty = true;
      }
      if (this._sizeDirty || now - this._sizeAt > 1000) this.measureLabels(now);
      if (this._occDirty || now - this._occAt > 200) this.measureOccluders(now);

      const cand = [];
      for (const [id, l] of this.labels) {
        const s = Gfx.worldToScreen(l.cv.labelPos);
        if (!(s && s.visible && s.dist < (this._hideBeyond || LABEL_HIDE_BEYOND) && isFinite(s.x) && isFinite(s.y)) || !(l.w > 0)) {
          // 不在画面内（由 follow 自行隐藏）；再次进入画面时直接取目标透明度，不闪现
          l.seen = false; l.on = false;
          continue;
        }
        const c = g.cities[id];
        const rank = id === this.selected ? 0 : c && c.owner === g.player ? 1 : 2;
        cand.push({ l, id, x: s.x, y: s.y, rank, pop: c ? c.population : 0 });
      }
      cand.sort((a, b) => (a.rank - b.rank) || (b.pop - a.pop) || (a.id - b.id));

      const placed = [];
      const k = dt / LABEL_FADE;
      for (const o of cand) {
        const l = o.l;
        // 滞后：已显示的标签真正压住才隐藏；已隐藏的要留出几像素空隙才重新出现，镜头移动时不闪烁
        const pad = l.on ? -1 : 4;
        const hw = l.w / 2, hh = l.h / 2;
        const test = { l: o.x - hw - pad, t: o.y - hh - pad, r: o.x + hw + pad, b: o.y + hh + pad };
        let ok = true;
        for (const q of this._occ) if (overlaps(test, q)) { ok = false; break; }
        if (ok) for (const q of placed) if (overlaps(test, q)) { ok = false; break; }
        if (ok) placed.push({ l: o.x - hw, t: o.y - hh, r: o.x + hw, b: o.y + hh });
        l.on = ok;
        const target = ok ? 1 : 0;
        if (snap || !l.seen || l.fade < 0) l.fade = target;
        else l.fade = target > l.fade ? Math.min(target, l.fade + k) : Math.max(target, l.fade - k);
        l.seen = true;
        if (l.handle) l.handle.alpha = l.fade;
        else l.el.style.visibility = l.fade > 0 ? '' : 'hidden';
      }
    }

    refreshAll() {
      const g = G();
      if (!this.hud) return;
      const f = g.playerFaction;
      this.topLeft.innerHTML = dot(f.color, '■') + SG.esc(g.ruler(g.player).name) + '军';
      const mine = g.citiesOf(g.player);
      const gold = sum(mine, c => c.gold), food = sum(mine, c => c.food);
      const tokens = '●'.repeat(Math.max(0, g.tokens)) + '○'.repeat(Math.max(0, g.tokensFor(g.player) - g.tokens));
      this.topCenter.innerHTML =
        `<span class="sg-chip"><b>${g.year}</b><i>年</i><b>${g.month}</b><i>月</i></span>` +
        `<span class="sg-chip"><i>令牌</i><span class="sg-tokens">${tokens}</span></span>` +
        `<span class="sg-chip"><i>城</i><b>${g.cityCount(g.player)}</b></span>` +
        `<span class="sg-chip"><i>金</i><b>${gold}</b></span>` +
        `<span class="sg-chip"><i>粮</i><b>${food}</b></span>`;
      this.endBtn.disabled = this.busy;
      this.saveBtn.disabled = this.busy || this.endMonth;
      for (const [id, l] of this.labels) {
        this.renderLabel(id, l);
        l.el.classList.toggle('is-selected', id === this.selected);
      }
      this._sizeDirty = true;         // 兵力数字可能改变标签宽度
      this.refreshCityPanel();
    }

    // 城名标签内容；紧凑模式（矮屏且镜头拉远）字号略小、不显示兵力，减少拥挤
    renderLabel(id, l) {
      const g = G();
      const c = g.cities[id];
      const col = c.owner >= 0 ? g.factions[c.owner].color : '#b3b3b3';
      l.el.innerHTML = dot(col, '●') + SG.esc(c.name) + (c.owner === g.player && !this._compact ? `<small>${g.troopsIn(c)}</small>` : '');
      l.el.classList.toggle('is-mine', c.owner === g.player);
      l.el.style.fontSize = this._compact ? '.92rem' : '';
      l.el.style.padding = this._compact ? '.1rem .5rem .14rem .42rem' : '';
    }

    selectCity(id) {
      this.selected = id;
      SG.Game.map.select(id);
      if (id >= 0) click();
      for (const [cid, l] of this.labels) l.el.classList.toggle('is-selected', cid === id);
      this.refreshCityPanel();
    }

    refreshCityPanel() {
      const g = G();
      if (!this.cityPanel) return;
      if (this.selected < 0) {
        if (this.cityPanel.style.display !== 'none') { this.cityPanel.style.display = 'none'; this._occDirty = true; }
        return;
      }
      if (this.cityPanel.style.display === 'none') {
        this.cityPanel.style.display = '';
        if (this._syncTop) this._syncTop();
        this._occDirty = true;
      }
      const c = g.cities[this.selected];
      const mine = c.owner === g.player;
      const owner = c.owner >= 0 ? g.factions[c.owner] : null;
      this.cityTitle.innerHTML = SG.esc(c.name) + '<small>' +
        (owner ? `<span style="color:${readable(owner.color)}">${SG.esc(owner.name)}</span>` : '空城') + '</small>';
      const govGen = c.governor >= 0 ? g.generals[c.governor] : null;
      const free = g.freeFoundIn(c.id);
      this.cityStats.innerHTML =
        `太守 ${govGen ? face(govGen) + '<b>' + SG.esc(govGen.name) + '</b>' : '<b>—</b>'}　　人口 ${n0(c.population)}\n` +
        `土地 <b>${c.land}</b>　产业 <b>${c.industry}</b>　町 <b>${c.town}</b>\n` +
        `金 <b>${c.gold}</b>　粮 <b>${c.food}</b>　防御 ${SG.cityDefense(c)}\n` +
        `兵力 <b>${g.troopsIn(c)}</b>　武将 ${g.officersIn(c).length} 人` +
        (free.length > 0 && mine ? `　<span class="sg-good">在野 ${free.length} 人</span>` : '');

      // 武将列表（君主优先，其次兵力）
      const scroll = this.genList.scrollTop;
      this.genList.innerHTML = '';
      const offs = g.officersIn(c).map((x, i) => ({ x, r: g.isRuler(x) ? 1 : 0, i }))
        .sort((a, b) => (b.r - a.r) || (b.x.troops - a.x.troops) || (a.i - b.i)).map(o => o.x);
      for (const gen of offs) {
        const row = h('div', 'sg-genrow', null, this.genList);
        // 点按武将行 → 武将详情
        row.setAttribute('role', 'button');
        row.tabIndex = 0;
        row.title = gen.name + ' · 详情';
        row.addEventListener('click', () => this.showGeneral(gen));
        row.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); this.showGeneral(gen); } });
        if (SG.Portrait) row.appendChild(SG.Portrait.el(gen, { size: SG.UI.facePx ? SG.UI.facePx(SG.UI.FACE_REM.item) : 40 }));
        const tag = g.isRuler(gen) ? '<span class="sg-tag sg-tag-ruler">君</span>' : gen.id === c.governor ? '<span class="sg-tag sg-tag-gov">守</span>' : '';
        const lv = lvTag(gen, 'sg-genrow-lv') + (mine && canLearn(gen) ? '<span class="sg-lv-new" title="可习得新招">新</span>' : '');
        h('div', 'sg-genrow-name', tag + '<b>' + SG.esc(gen.name) + '</b>' + lv + (gen.moved ? '<span class="sg-moved">已行动</span>' : ''), row);
        h('div', 'sg-genrow-stats', `武${gen.war} 智${gen.intel} 政${gen.pol}\n兵${gen.troops} 训${gen.training} 忠${g.isRuler(gen) ? 100 : gen.loyalty}`, row);
      }
      this.genList.scrollTop = scroll;

      this.cmdGrid.innerHTML = '';
      if (!mine) return;
      // 每个指令给出“不能用的原因”（null = 可用）：先看是否忙碌，再看规则（城池、武将），最后看令牌
      const C = SG.Commands;
      const busy = this.busy ? (this.endMonth ? '诸侯行动中，请稍候。' : '指令执行中，请稍候。') : null;
      const noTok = g.tokens > 0 ? null : '本月令牌已用完——点「结束本月」进入下个月。';
      const first = (...whys) => { for (const w of whys) if (w) return w; return null; };
      const canAttack = c.links.some(i => g.cities[i].owner !== g.player && !g.allied(g.cities[i].owner, g.player));
      this.addCmd('开发', first(busy, noTok), () => this.do(() => this.cmdDevelop(c)));
      this.addCmd('征兵', first(busy, noTok), () => this.do(() => this.cmdRecruit(c)));
      this.addCmd('训练', first(busy, noTok), () => this.do(() => this.cmdTrain(c)));
      this.addCmd('搜索', first(busy, noTok), () => this.do(() => this.cmdSearch(c)));
      this.addCmd('登用', first(busy, free.length > 0 ? null : '城中没有已发现的在野人才——先用「搜索」寻访。', noTok), () => this.do(() => this.cmdHire(c)));
      this.addCmd('移动', first(busy, C.moveBlocked(c), noTok), () => this.do(() => this.cmdMove(c)));
      this.addCmd('输送', first(busy, C.transportBlocked(c), noTok), () => this.do(() => this.cmdTransport(c)));
      this.addCmd('外交', first(busy, noTok), () => this.do(() => this.cmdDiplomacy(c)));
      this.addCmd('出征', first(busy, canAttack ? null : c.name + '周围没有可攻打的城池（只能攻打相邻的敌城或空城，同盟势力除外）。', noTok), () => this.do(() => this.cmdAttack(c)), true);
      this.addCmd('赏赐', busy, () => this.do(() => this.cmdReward(c)));
      this.addCmd('交易', busy, () => this.do(() => this.cmdTrade(c)));
      this.addCmd('任命', busy, () => this.do(() => this.cmdAppoint(c)));
    }

    // why：不可用的原因（字符串）——按钮显示为灰色（aria-disabled，见 injectStyle），点按时说明原因而不执行；
    // null 为可用。按钮带 data-cmd（指令名），灰色时另带 data-why（原因）。
    addCmd(name, why, action, primary) {
      const b = SG.UI.button(this.cmdGrid, name, () => { if (b._why) this.explainCmd(b); else action(); }, primary ? { primary: true } : undefined);
      b.dataset.cmd = name;
      if (why) {
        b._why = why;
        b.dataset.why = why;
        b.title = why.replace(/<[^>]*>/g, '');
        b.setAttribute('aria-disabled', 'true');
      }
      // 提示闪烁只给可用的按钮（灰色按钮闪金光会让人误以为能用）
      if (!why && this._hint && this._hint.name === name && performance.now() < this._hint.until) b.classList.add('sg-cmd-hint');
      return b;
    }

    explainCmd(b) {
      if (!b || !b._why) return;
      // 闪烁与抖动同用 animation 属性：先停掉闪烁，抖动才看得见
      b.classList.remove('sg-cmd-hint');
      if (this._hint && this._hint.name === b.dataset.cmd) this._hint = null;
      SG.UI.pulse(b, 'sg-cmd-deny');
      // 同一时间只留一条说明，连点不会堆叠
      if (this._denyToast && this._denyToast.parentNode) this._denyToast.parentNode.removeChild(this._denyToast);
      this._denyToast = UI().toast(`<span class="sg-cmd-name">${SG.esc(b.dataset.cmd || '')}</span>${b._why}`, 3.4);
    }
    // 让某个指令按钮闪烁数次以引起注意（提示用）
    hintCmd(name) {
      this._hint = { name, until: performance.now() + 4400 };   // 面板重建时继续闪烁，直到时间到
      if (!this.cmdGrid) return;
      for (const b of this.cmdGrid.children) if (b.dataset && b.dataset.cmd === name && !b._why) SG.UI.pulse(b, 'sg-cmd-hint');
    }

    // 此刻能用「移动」的己方城（有令牌，且城中有未行动的武将可调往相连的己方城）；没有则 null。
    // 依次优先：当前选中的城、新取得的城、其余己方城。
    moveUsableCity() {
      const g = G(), C = SG.Commands;
      if (!g || g.tokens <= 0 || !g.playerFaction.alive) return null;
      const order = [this.selected].concat(this._tipCities, g.citiesOf(g.player).map(c => c.id));
      for (const id of order) {
        const c = id >= 0 ? g.cities[id] : null;
        if (c && c.owner === g.player && !C.moveBlocked(c)) return c;
      }
      return null;
    }
    // 第一次取得新城后弹一次提示：可用「移动」把武将调往其他城池，并让「移动」按钮闪烁。
    // 只在「移动」此刻真能用时弹出（必要时改选一座能用的城）；否则保持待弹，下个月月初再看。
    // fresh：刚取得新城（true）还是延到之后补弹（false），只影响第一句。→ 是否弹出
    async moveTip(fresh) {
      if (tipShown()) { this._tipPending = false; return false; }
      const c = this.moveUsableCity();
      if (!c) return false;
      this._tipPending = false;
      this._tipCities = [];
      markTipShown();
      if (this.selected !== c.id) {
        const p = SG.mapPos(c);
        SG.Game.rig.focusMap(p.x, p.y);
        this.selectCity(c.id);
      }
      this.refreshAll();              // 面板显示此刻的真实状态（「移动」可用）
      await UI().say('【提示】' + (fresh ? '取得了新的城池！' : '领有两座以上相连的城池，可以调动武将了。') + '\n' +
        '用「移动」可把武将调往其他己方城池——只要经由己方城池相连即可，不必相邻（每次消耗 1 枚令牌，可一次调动多人）。\n' +
        '「输送」同样可以在己方城池之间调拨金粮。');
      this.hintCmd('移动');
      return true;
    }

    do(routine) {
      if (this.busy || this.endMonth || UI().anyModal()) return;
      this._wrap(routine);
    }
    async _wrap(routine) {
      const g0 = G();
      const before = new Set(g0.citiesOf(g0.player).map(c => c.id));
      this._hint = null;              // 下达新指令时停止提示闪烁
      let gainedNow = false;
      this.busy = true; this.refreshAll();
      try {
        await routine();
        // 出征攻下 / 敌将献城等使城池增加：第一次时提示「移动」（见 moveTip）
        const g = G();
        if (g.playerFaction.alive && !tipShown()) {
          const gained = g.citiesOf(g.player).filter(c => !before.has(c.id)).map(c => c.id);
          if (gained.length > 0) { gainedNow = true; this._tipPending = true; this._tipCities = gained.concat(this._tipCities); }
        }
      } catch (e) { console.error(e); }
      this.busy = false;
      SG.Game.map.refresh(G());
      this.refreshAll();
      if (this._tipPending && !this.endMonth) {   // 刚点了「结束本月」时不弹，留到月初
        try { await this.moveTip(gainedNow); } catch (e) { console.error(e); }
      }
    }
    useToken() { G().tokens = Math.max(0, G().tokens - 1); }

    async confirmEnd() {
      if (G().tokens > 0) {
        const ok = await UI().confirm('还剩 ' + G().tokens + ' 枚令牌，确定结束本月？', '结束', '继续');
        if (!ok) return;
      }
      this.endMonth = true;
    }

    // ---------------------------------------------------------- 选择武将 --
    async pickGeneral(title, list, info, enabled) {
      const gens = list.slice();
      if (gens.length === 0) { await UI().say('没有可用的武将。'); return null; }
      const gs = G();
      const items = gens.map(g => item(SG.esc(g.name) + (gs.isRuler(g) ? '<span class="sg-tag sg-tag-ruler">君</span>' : ''), info(g), !enabled || enabled(g), null, { avatar: g }));
      const r = await UI().choose(title, items, null, 700);
      return r >= 0 ? gens[r] : null;
    }
    available(c) { return G().officersIn(c).filter(g => !g.moved); }
    // 武将汇报：以该武将的头像说话，文中提到的其他武将带小头像。cmd：指令名（姓名下的小字）；opts：{ mood }
    report(who, text, cmd, opts) {
      opts = opts || {};
      const sub = (cmd ? `<span class="sg-say-cmd">${SG.esc(cmd)}</span>` : '') + (opts.sub || '');
      // 指令结果是第三人称（「刘备主持土地开发……」）：由本人汇报时省去句首的自己的名字
      text = String(text);
      if (who && who.name && text.length > who.name.length + 1 && text.startsWith(who.name) && !/^[，。！？、：]/.test(text.slice(who.name.length))) text = text.slice(who.name.length);
      return UI().say(faces(text, { skip: who ? [who.name] : [] }), who || null, null, { mood: opts.mood, sub: sub || null });
    }

    // ---------------------------------------------------------- 内政 --
    async cmdDevelop(c) {
      const B = SG.Balance, K = SG.DevKind, C = SG.Commands;
      const kinds = [K.Land, K.Industry, K.Town];
      const desc = ['增加秋收的粮食', '增加每月的金收入', '增加人口与城防'];
      const vals = [c.land, c.industry, c.town];
      const r = await UI().choose('开发（花费 ' + B.DevelopCost + ' 金）',
        kinds.map((k, i) => item(C.devName(k), vals[i] + ' / ' + B.StatMax, c.gold >= B.DevelopCost, desc[i])), '金 ' + c.gold);
      if (r < 0) return;
      const who = await this.pickGeneral('由谁主持？', G().officersIn(c), g => '政治 ' + g.pol);
      if (!who) return;
      this.useToken();
      sfx('coin');
      await this.report(who, C.develop(c, who, kinds[r]), '开发');
    }

    async cmdRecruit(c) {
      const B = SG.Balance, C = SG.Commands;
      const who = await this.pickGeneral('为谁征兵？', G().officersIn(c), g => `兵 ${g.troops} / ${SG.maxTroops(g)}`, g => g.troops < SG.maxTroops(g));
      if (!who) return;
      const max = C.recruitMax(c, who);
      const n = await UI().pickNumber('征兵人数', Math.min(100, max), max, 100, max, v => '花费 ' + Math.ceil(v / B.TroopsPerGold) + ' 金（现有 ' + c.gold + '）');
      if (!(n > 0)) return;
      this.useToken();
      sfx('march', 0.5);
      await this.report(who, C.recruit(c, who, n), '征兵');
    }

    async cmdTrain(c) {
      const who = await this.pickGeneral('训练哪支部队？', G().officersIn(c), g => `兵 ${g.troops}　训练 ${g.training}`, g => g.troops > 0 && g.training < 100);
      if (!who) return;
      this.useToken();
      await this.report(who, SG.Commands.train(who), '训练');
    }

    // ---------------------------------------------------------- 人事 --
    async cmdSearch(c) {
      const C = SG.Commands;
      const who = await this.pickGeneral('派谁搜索人才？', this.available(c), g => '智力 ' + g.intel);
      if (!who) return;
      this.useToken();
      const msg = C.search(c, who);
      const found = C.searchFound;
      await this.report(who, msg, '搜索', { mood: found ? 'win' : undefined });
      if (found) {
        const ok = await UI().confirm(`登用${SG.esc(found.name)}？`, '登用', '暂且作罢', {
          body: genCard(found, { sub: '在野人才　成功率约 <b>' + pct(C.hireChance(found, who, G().player)) + '</b>' }),
        });
        if (ok) await this.hireResult(found, who, c);
      }
    }

    async hireResult(target, recruiter, c) {
      if (SG.Commands.hire(target, recruiter, G().player, c.id)) {
        sfx('win', 0.5);
        await UI().say(target.name + '：久闻明公大名，愿效犬马之劳！\n（' + target.name + '加入了我军）', target, CINNABAR, { mood: 'win', sub: genSub(target) });
      } else {
        await UI().say(target.name + '：在下另有志向，请回吧。\n' + faces(`（${recruiter.name}登用未成）`), target, GREY_SPEAKER, { mood: 'angry', sub: genSub(target) });
      }
    }

    async cmdHire(c) {
      const C = SG.Commands;
      const target = await this.pickGeneral('登用哪位在野人才？', G().freeFoundIn(c.id), g => `武${g.war} 智${g.intel} 政${g.pol}`);
      if (!target) return;
      const who = await this.pickGeneral('派谁前去？', G().officersIn(c), g => `智力 ${g.intel}　成功率约 ${pct(C.hireChance(target, g, G().player))}`);
      if (!who) return;
      this.useToken();
      await this.hireResult(target, who, c);
    }

    async cmdReward(c) {
      const B = SG.Balance;
      const who = await this.pickGeneral('赏赐谁？（' + B.RewardGold + ' 金）', G().officersIn(c).filter(g => !G().isRuler(g)), g => '忠诚 ' + g.loyalty, g => g.loyalty < 100 && c.gold >= B.RewardGold);
      if (!who) return;
      sfx('coin');
      UI().toast(faces(SG.Commands.reward(c, who), { gens: [who] }));
    }

    async cmdAppoint(c) {
      const g = G();
      const who = await this.pickGeneral('任命太守', g.officersIn(c), x => `政${x.pol} 智${x.intel}`);
      if (!who) return;
      if (g.officersIn(c).some(x => g.isRuler(x)) && !g.isRuler(who)) { await this.report(g.ruler(g.player), '此城由我亲自坐镇，不另设太守。', '任命'); return; }
      c.governor = who.id;
      UI().toast(face(who) + SG.esc(who.name) + '出任' + SG.esc(c.name) + '太守');
    }

    // 移动：目的地为经由己方城池相连可达的所有己方城（不必相邻），列表注明路程
    async cmdMove(c) {
      const g = G(), C = SG.Commands;
      const blocked = C.moveBlocked(c);
      if (blocked) { await UI().say(blocked); return; }
      const routes = C.moveTargets(c);
      const r = await UI().choose('移往何处？', routes.map(rt => item(SG.esc(rt.city.name),
        routeText(rt) + '　武将 ' + g.officersIn(rt.city).length + '　兵 ' + g.troopsIn(rt.city), true, viaText(g, rt))),
        '经由己方城池相连即可前往，不必相邻（1 枚令牌）');
      if (r < 0) return;
      const rt = routes[r], dest = rt.city;
      const gens = this.available(c);
      if (gens.length === 0) { await UI().say('本月已无可调动的武将。'); return; }
      const sel = await UI().chooseMany('调动武将至' + dest.name + '（' + routeText(rt) + '）', gens.map(x => item(SG.esc(x.name) + (g.isRuler(x) ? '<span class="sg-tag sg-tag-ruler">君</span>' : ''), `兵 ${x.troops}　武${x.war} 智${x.intel}`, true, null, { avatar: x })), 10, '可一次调动多名武将（消耗 1 枚令牌）');
      if (!sel || sel.length === 0) return;
      this.useToken();
      await this.marchRoute(rt.path, g.playerFaction.color);
      const moved = [];
      for (const i of sel) { C.move(gens[i], dest); if (gens[i].city === dest.id) moved.push(gens[i]); }
      UI().toast('<span class="sg-toast-faces">' + moved.slice(0, 6).map(face).join('') + '</span>' + moved.length + ' 名武将移驻' + SG.esc(dest.name), 2.8);
    }

    // 沿己方路线逐城行军：相邻 1.2 秒；路线越长每段越快，总长不超过约 2.6 秒
    async marchRoute(path, color) {
      const hops = path.length - 1;
      if (hops <= 0) return;
      const per = Math.min(1.2, 2.6 / hops);
      for (let i = 0; i < hops; i++) await SG.Game.map.march(path[i], path[i + 1], color, per);
    }

    async cmdTransport(c) {
      const g = G(), C = SG.Commands;
      const blocked = C.transportBlocked(c);
      if (blocked) { await UI().say(blocked); return; }
      const routes = C.transportTargets(c);
      const r = await UI().choose('输送至何处？', routes.map(rt => item(SG.esc(rt.city.name),
        routeText(rt) + '　金 ' + rt.city.gold + '　粮 ' + rt.city.food, true, viaText(g, rt))),
        '经由己方城池相连即可送达，不必相邻（1 枚令牌）');
      if (r < 0) return;
      const dest = routes[r].city;
      const gold = await UI().pickNumber('输送金', 0, c.gold, 50, M.idiv(c.gold, 2), null);
      if (gold < 0) return;
      const food = await UI().pickNumber('输送粮', 0, c.food, 500, M.idiv(c.food, 2), null);
      if (food < 0) return;
      if (gold === 0 && food === 0) { UI().toast('未输送任何金粮。'); return; }
      this.useToken();
      UI().toast(C.transport(c, dest, gold, food));
    }

    async cmdTrade(c) {
      const C = SG.Commands;
      const price = C.foodPrice();
      const r = await UI().choose('交易　（每 100 粮 ' + price + ' 金）', [item('买粮', '金 ' + c.gold, c.gold > 0), item('卖粮', '粮 ' + c.food, c.food > 0)]);
      if (r < 0) return;
      if (r === 0) {
        const n = await UI().pickNumber('花多少金买粮？', 0, c.gold, 10, Math.min(c.gold, 200), v => '可得粮 ' + M.idiv(v * 100, price));
        if (n > 0) { sfx('coin'); UI().toast(C.buyFood(c, n)); }
      } else {
        const n = await UI().pickNumber('卖出多少粮？', 0, c.food, 100, Math.min(c.food, 1000), v => '可得金 ' + M.idiv(M.idiv(v * price, 100) * 9, 10));
        if (n > 0) { sfx('coin'); UI().toast(C.sellFood(c, n)); }
      }
    }

    // ---------------------------------------------------------- 外交 --
    async cmdDiplomacy(c) {
      const g = G(), B = SG.Balance, C = SG.Commands;
      const r = await UI().choose('外交策略', [
        item('同盟', null, true, '与他国缔结 ' + B.AllianceMonths + ' 个月的同盟'),
        item('离间', null, true, '降低敌将的忠诚'),
        item('拉拢', null, true, '劝诱敌将倒戈'),
      ]);
      if (r < 0) return;
      if (r === 0) {
        const fs = g.factions.filter(f => f.alive && f.id !== g.player);
        const grp = groupedFactions(fs, f => item(swatch(f.color) + SG.esc(g.ruler(f.id).name) + (regionsOf() && f.name !== g.ruler(f.id).name ? `<small class="sg-faction-name">${SG.esc(f.name)}</small>` : ''),
          g.allied(f.id, g.player) ? '已同盟' : '城 ' + g.cityCount(f.id) + '　成功率约 ' + pct(C.allyChance(g.player, f.id, B.AllianceGift)),
          !g.allied(f.id, g.player), null, { avatar: g.ruler(f.id) }));
        const p = UI().choose('与谁结盟？', grp.items);
        markGroupHeads(grp.heads);
        const fi = await p;
        const ff = fi >= 0 ? grp.pick(fi) : null;
        if (!ff) return;
        const gift = await UI().pickNumber('赠送礼金', 0, c.gold, 50, Math.min(c.gold, B.AllianceGift), v => '成功率约 ' + pct(C.allyChance(g.player, ff.id, v)));
        if (gift < 0) return;
        this.useToken();
        const res = C.ally(c, g.player, ff.id, gift);
        const ok = g.allied(ff.id, g.player);
        const ru = g.ruler(ff.id);
        await UI().say((ok ? '……愿与贵方结为同盟，共图大业。' : '……此事不必再提。') + '\n' + SG.esc(res), ru, null, { mood: ok ? 'win' : 'angry', sub: genSub(ru) });
        return;
      }
      const agent = await this.pickGeneral('派谁执行？', this.available(c), x => '智力 ' + x.intel);
      if (!agent) return;
      const targets = [];
      for (const i of c.links) {
        const x = g.cities[i];
        if (x.owner >= 0 && x.owner !== g.player) for (const o of g.officersIn(x)) if (!g.isRuler(o)) targets.push(o);
      }
      if (targets.length === 0) { await UI().say('邻近城池中没有可以下手的敌将。'); return; }
      let target;
      if (r === 1) target = await this.pickGeneral('离间哪位敌将？', targets, x => g.factions[x.faction].name + '·' + g.cities[x.city].name + '　智' + x.intel + '　忠诚 ' + x.loyalty);
      else target = await this.pickGeneral('拉拢哪位敌将？', targets, x => g.factions[x.faction].name + '·' + g.cities[x.city].name + '　忠诚 ' + x.loyalty + '　成功率约 ' + pct(C.persuadeChance(agent, x, g.player)));
      if (!target) return;
      this.useToken();
      agent.moved = true;
      const res = r === 1 ? C.discord(agent, target) : C.persuade(agent, target, g.player);
      const won = r === 1 ? res.indexOf('成功') >= 0 : target.faction === g.player;
      await this.report(agent, res, r === 1 ? '离间' : '拉拢', { mood: won ? 'win' : undefined });
    }

    // ---------------------------------------------------------- 战争 --
    async cmdAttack(c) {
      const g = G(), B = SG.Balance;
      const targets = c.links.map(i => g.cities[i]).filter(x => x.owner !== g.player && !g.allied(x.owner, g.player));
      // 每座目标城列出守将的小头像（至多 5 人，再多显示 +N）
      const guards = t => {
        const os = g.officersIn(t).slice().sort((a, b) => (g.isRuler(b) - g.isRuler(a)) || (b.troops - a.troops));
        const n = os.length > 5 ? 4 : os.length;
        return os.length ? '<span class="sg-item-faces">' + os.slice(0, n).map(face).join('') + (os.length > n ? `<span class="sg-muted">+${os.length - n}</span>` : '') + '</span>' : null;
      };
      const r = await UI().choose('攻打何处？', targets.map(t => item(SG.esc(t.name),
        (t.owner >= 0 ? g.factions[t.owner].name : '空城') + '　武将 ' + g.officersIn(t).length + '　兵 ' + g.troopsIn(t) + '　防 ' + SG.cityDefense(t), true, guards(t))));
      if (r < 0) return;
      const target = targets[r];
      const gens = this.available(c).filter(x => x.troops > 0);
      if (gens.length === 0) { await UI().say('没有可以出征的部队（需有兵力且本月未行动）。'); return; }
      const sel = await UI().chooseMany('出征武将（至多 ' + B.MaxSortieGenerals + ' 名）',
        gens.map(x => item(SG.esc(x.name) + (g.isRuler(x) ? '<span class="sg-tag sg-tag-ruler">君</span>' : ''), `兵${x.troops} 武${x.war} 智${x.intel} 训${x.training}`, true, null, { avatar: x })), B.MaxSortieGenerals,
        '君主出征时由君主任主将，否则为列表中最靠前的武将；主将败走则全军撤退。');
      if (!sel || sel.length === 0) return;
      const squad = sel.map(i => gens[i]);
      const troops = sum(squad, x => x.troops);
      const suggest = Math.min(c.food, Math.ceil(troops * 0.14 / 100) * 100 + 200);
      const food = await UI().pickNumber('携带军粮', 0, c.food, 100, suggest,
        v => `约可支撑 ${M.idiv(v, Math.max(1, M.idiv(troops, B.BattleFoodPerTroops)))} 日（兵 ${troops}）`);
      if (food < 0) return;
      this.useToken();
      const setup = SG.Conquest.prepare(g.player, c, target, squad, food, 0);
      sfx('horn');
      await SG.Game.map.march(c, target, g.playerFaction.color, 1.4);
      await this.fight(setup, 0);
    }

    async defend(s) {
      const g = G();
      const p = SG.mapPos(s.target);
      SG.Game.rig.focusMap(p.x, p.y, 45);
      this.selectCity(s.target.id);
      await SG.Game.map.march(s.src, s.target, g.factions[s.attacker].color, 1.4);
      await this.alarm(s);
      await this.fight(s, 1);
    }

    // 急报：来犯主将的头像（怒容、敌方势力色边框）示警，随行武将带小头像
    alarm(s) {
      const g = G();
      const lead = s.atk[0];
      const fac = g.factions[s.attacker];
      const leader = g.ruler(s.attacker).name === lead.name ? SG.esc(lead.name) : SG.esc(fac.name) + '军' + SG.esc(lead.name);
      const rest = s.atk.slice(1);
      const text = `<b class="sg-alarm">急报！</b>${leader}率 ${s.atk.length} 名武将、兵 <b>${n0(sum(s.atk, x => x.troops))}</b> 来犯${SG.esc(s.target.name)}！` +
        (rest.length ? '\n<span class="sg-muted">随行</span> ' + rest.map(x => face(x) + SG.esc(x.name)).join('　') : '');
      return UI().say(text, lead, null, { mood: 'angry', sub: genSub(lead), className: 'is-alarm' });
    }

    // 战斗与战后处理。playerSide：玩家是攻方 0 还是守方 1
    async fight(s, playerSide) {
      const g = G(), Cq = SG.Conquest;
      if (s.def.length === 0 || s.def.every(d => d.troops <= 0)) {
        Cq.autoResolve(s);
        Cq.apply(s);
        SG.Game.map.refresh(g); this.refreshAll();
        let fates = null;
        if (s.captives.length > 0) fates = await this.handleCaptives(s, playerSide === 0);
        SG.Game.map.refresh(g); this.refreshAll();
        // 空城：同样由我方主将汇报（守城武将全被俘时由君主汇报）
        const won = s.attackerWon === (playerSide === 0);
        const who = this.ourReporter(s, playerSide);
        if (who) await this.report(who, s.summary, s.target.name + '之战', { mood: won ? 'win' : 'hurt' });
        else await UI().say(faces(s.summary));
        await this.reportCaptives(fates, s.attackerWon ? s.attacker : s.defender);
        await this.growth(s, playerSide);
        return;
      }
      // 说明区：双方出阵武将的小头像
      const side = (list, cls, name) => `<span class="${cls}">${name}</span> ` + list.slice(0, 5).map(x => face(x)).join('') + (list.length > 5 ? `<span class="sg-muted">+${list.length - 5}</span>` : '');
      const vs = '<span class="sg-vs-faces">' + side(s.atk, 'sg-side0', '攻') + '<span class="sg-sep">　</span>' + side(s.def, 'sg-side1', '守') + '</span>';
      const mode = await UI().choose(SG.esc(s.target.name) + '之战', [item('亲自指挥', '在战场上调兵遣将'), item('委任', '由部将自行作战，立即得出结果')], vs, 560);
      if (mode === 0) {
        this.hud.style.display = 'none';
        this.setMapLabelsVisible(false);
        try {
          await SG.BattleController.run(s, playerSide);
        } catch (e) {
          // 防御：战斗画面出错时由电脑结算，避免整局卡死
          console.error(e);
          Cq.autoResolve(s);
        }
        this.hud.style.display = '';
        this.setMapLabelsVisible(true);
        mapMusic();
      } else Cq.autoResolve(s);
      Cq.apply(s);
      // 先刷新地图与城池面板（处置俘虏的对话期间显示战后兵力与归属）
      SG.Game.map.refresh(g); this.refreshAll();
      const playerWon = s.attackerWon === (playerSide === 0);
      let fates = null;
      if (s.captives.length > 0) {
        if (playerWon) await this.handleCaptives(s, true);
        else fates = Cq.aiDecideCaptives(s, playerSide === 0 ? s.defender : s.attacker);
      }
      for (const f of g.factions) if (f.alive && g.cityCount(f.id) === 0) g.checkFactionDeath(f.id);
      SG.Game.map.refresh(g);
      this.refreshAll();
      const res = playerSide === 0
        ? (s.attackerWon ? '我军攻陷了' + s.target.name + '！' : '攻城失利，全军撤回' + s.src.name + '。')
        : (s.attackerWon ? s.target.name + '失守了……' : '我军成功守住了' + s.target.name + '！');
      // 由我方主将汇报战果（主将阵亡 / 被俘时由其余在籍的武将汇报，都不在时由君主汇报）
      const who = this.ourReporter(s, playerSide);
      if (who) await this.report(who, res, s.target.name + '之战', { mood: playerWon ? 'win' : 'hurt' });
      else await UI().say(res);
      await this.reportCaptives(fates, playerSide === 0 ? s.defender : s.attacker);
      if (s.attackerWon && s.defender >= 0 && !g.factions[s.defender].alive) await UI().say(faces(g.factions[s.defender].name + '势力就此灭亡。'));
      await this.growth(s, playerSide);
    }

    // ---------------------------------------------------------- 成长（第三版 §4） --
    // 战后：我方武将的升级通知（头像、「关羽 升至 5 级」、经验条），然后为有空格的武将逐个弹出习得对话框
    // playerSide：玩家是攻方 0 / 守方 1（缺省不限）。战后归降我方的敌将不算（他们的经验来自对我军作战）
    async growth(s, playerSide) {
      const g = G(), P = Prog();
      if (!P || !g.playerFaction || !g.playerFaction.alive) return;
      const ours = x => x && x.gen && !x.gen.dead && x.gen.faction === g.player && (!(playerSide >= 0) || x.side === undefined || x.side === playerSide);
      const prog = (s && s.progress ? s.progress : []).filter(ours);
      const ups = prog.filter(x => x.to > x.from);
      try {
        if (ups.length) await this.levelUpNotice(ups, prog.filter(x => !(x.to > x.from) && x.exp > 0), s);
        else if (prog.some(x => x.exp > 0)) {
          const tot = prog.reduce((a, x) => a + (x.exp || 0), 0);
          UI().toast('出阵武将获得经验 <b>+' + tot + '</b>', 1.8);
        }
      } catch (e) { console.error(e); }
      // 升级的武将先选，其余保留着空格的随后（例如上次「稍后再选」）
      const order = [];
      for (const x of ups) if (order.indexOf(x.gen) < 0) order.push(x.gen);
      for (const x of prog) if (order.indexOf(x.gen) < 0) order.push(x.gen);
      for (const gen of order) {
        if (!canLearn(gen)) continue;
        const r = await this.learnDialog(gen);
        if (r === 'later-all') break;
      }
      this.refreshAll();
    }
    // 升级通知：每位升级的武将一行（头像、升至 N 级、经验条与本战所得、新解锁的格）；其余出阵武将的经验附在末尾
    levelUpNotice(ups, rest, s) {
      const g = G(), P = Prog();
      const px = SG.UI.facePx ? SG.UI.facePx(3.4) : 56;
      const rows = ups.map(x => {
        const gen = x.gen, e = P.expInfo(gen), op = P.openings(gen);
        let pic = '';
        try { if (SG.Portrait) pic = SG.Portrait.html(gen, { size: px, className: 'sg-lvup-face', mood: 'win' }); } catch (err) { pic = ''; }
        const learn = [];
        const spName = t => (SG.Specials && SG.Specials.tierName ? SG.Specials.tierName(t) : t + '阶');
        const tcName = t => (SG.Tactics && SG.Tactics.TIER_NAME ? SG.Tactics.TIER_NAME[t] : t + '阶');
        if (op.special > 0) learn.push('可习得新必杀' + (op.special > 1 ? ' ' + op.special + ' 招' : '') + '（' + SG.esc(openTiers(P.specialSlots(gen), spName).text) + '）');
        if (op.tactic > 0) learn.push('可习得新计策' + (op.tactic > 1 ? ' ' + op.tactic + ' 计' : '') + '（' + SG.esc(openTiers(P.tacticSlots(gen), tcName).text) + '）');
        const learned = (x.learned || []).map(c => '「' + SG.esc(c.name) + '」').join('');
        return '<div class="sg-lvup-row">' + pic + '<div class="sg-lvup-text">' +
          '<div class="sg-lvup-head"><b class="sg-lvup-name">' + SG.esc(gen.name) + '</b> 升至 <b class="sg-lvup-lv">' + e.level + '</b> 级' +
          (x.to - x.from > 1 ? '<span class="sg-muted">（连升 ' + (x.to - x.from) + ' 级）</span>' : '') + '</div>' +
          '<div class="sg-lvup-exp">' + expBar(e.pct) + '<span>+' + x.exp + '<span class="sg-muted">　' + (e.max ? '已满级' : e.exp + ' / ' + e.next) + '</span></span></div>' +
          (learn.length ? '<div class="sg-lvup-new">' + learn.map(t => '<span class="sg-lvup-slot">' + t + '</span>').join(' ') + '</div>' : '') +
          (learned ? '<div class="sg-lvup-new">习得' + learned + '</div>' : '') +
          '</div></div>';
      });
      const tail = rest.length ? '<div class="sg-lvup-rest"><span class="sg-muted">其余出阵</span> ' +
        rest.map(x => '<span class="sg-lvup-who">' + face(x.gen) + SG.esc(x.gen.name) + ' <b>+' + x.exp + '</b></span>').join(' ') + '</div>' : '';
      const pend = ups.some(x => canLearn(x.gen));
      const title = (s && s.target ? SG.esc(s.target.name) + '之战 · ' : '') + '武将成长';
      return UI().notice(title, '<div class="sg-lvup">' + rows.join('') + tail + '</div>', { ok: pend ? '选择新招' : '确定', width: 640, className: 'sg-lvup-dlg' });
    }
    // 习得对话框：3 个候选（必杀 / 计策）→ learn；可连续学（同时开了多个格）。「稍后再选」→ 保留空格（武将详情里可再学）
    // → 'learned' | 'later' | 'later-all'（× / Esc：本次战后不再逐个询问其余武将）
    async learnDialog(gen) {
      const P = Prog(), g = G();
      if (!P) return 'later';
      let learnedAny = false;
      for (let guard = 0; guard < 10; guard++) {
        const cands = P.candidates(gen, 3);
        if (!cands.length) break;
        const short = shortScreen();
        const items = cands.map(c => {
          const kind = c.type === 'special' ? '必杀' : '计策';
          const label = `<span class="sg-learn-kind is-${c.type}">${kind}</span><span style="color:${spColor(c)}">${SG.esc(c.name)}</span>`;
          const lore = c.ref && c.ref.lore ? c.ref.lore : '';
          const body = stripLore(c.desc, lore);
          // 计策的摘要以等阶开头（右栏已有），去掉
          const rules = String(c.rules || '').replace(/^(初阶|中阶|高阶|绝阶) · /, '');
          // 矮屏（横屏手机）：说明只留第一句（截断），数值摘要另起一行
          let brief = body;
          if (short) { brief = (body.split('。')[0] || body); if (Array.from(brief).length > 30) brief = Array.from(brief).slice(0, 30).join('') + '…'; }
          const desc = SG.esc(brief) + '<span class="sg-learn-rules">' + SG.esc(rules) + '</span>';
          return item(label, `<span class="sg-learn-tier t${c.tier}">${tierText(c)}</span>`, true, desc);
        });
        items.push(item('<span class="sg-muted">稍后再选</span>', '<span class="sg-muted">保留空格</span>', true));
        const e = P.expInfo(gen), op = P.openings(gen);
        const open = [];
        if (op.special > 0) open.push('必杀 ' + op.special);
        if (op.tactic > 0) open.push('计策 ' + op.tactic);
        const sub = lvTag(gen) + ' ' + expBar(e.pct, 'is-inline') + '<span class="sg-muted">　可学：' + open.join('、') + '</span>';
        const info = genCard(gen, { sub, mood: 'win' });
        const r = await UI().choose(SG.esc(gen.name) + ' · 习得新招', items, info, 820);
        // × / Esc：无论本窗口前是否已学过，都不再逐个询问其余武将（已学的招式照常保留）
        if (r < 0) return 'later-all';
        if (r >= cands.length) return learnedAny ? 'learned' : 'later';
        const c = cands[r];
        if (!P.learn(gen, c)) { UI().toast('无法习得「' + SG.esc(c.name) + '」'); break; }
        learnedAny = true;
        try { g.addLog(gen.name + '习得' + (c.type === 'special' ? '必杀' : '计策') + '「' + c.name + '」。'); } catch (err) { /* 无记录 */ }
        sfx('horn', 0.35);
        UI().toast(face(gen) + SG.esc(gen.name) + ' 习得' + (c.type === 'special' ? '必杀' : '计策') + '「<span style="color:' + spColor(c) + '">' + SG.esc(c.name) + '</span>」', 2.2);
        this.refreshAll();
      }
      return learnedAny ? 'learned' : 'later';
    }
    // 月初：有保留着空格的我方武将时提示一次
    growthReminder() {
      const g = G(), P = Prog();
      if (!P || !g || !g.playerFaction || !g.playerFaction.alive) return;
      let list = [];
      try { list = P.pending(g.player); } catch (e) { list = []; }
      if (!list.length) return;
      const key = g.year * 12 + g.month;
      if (this._growthToldAt === key) return;
      this._growthToldAt = key;
      UI().toast(list.slice(0, 3).map(x => face(x) + SG.esc(x.name)).join('、') + (list.length > 3 ? ' 等 ' + list.length + ' 人' : '') + ' 可习得新招<span class="sg-muted">（城池面板中点选武将）</span>', 3.2);
    }

    // 汇报战果的我方武将：出阵（守城）武将中第一位仍在籍、未被俘的；都不在时为君主；我方已灭亡 → null
    ourReporter(s, playerSide) {
      const g = G();
      const caps = new Set((s.captives || []).map(x => x.id));
      const ok = x => x && !x.dead && x.faction === g.player && !caps.has(x.id);
      const who = (playerSide === 0 ? s.atk : s.def).find(ok);
      if (who) return who;
      const pf = g.factions[g.player];
      const ru = pf && pf.alive ? g.ruler(g.player) : null;
      return ok(ru) ? ru : null;
    }

    // 战败后我方被俘武将的下落（fates：Conquest.aiDecideCaptives 的结果；winner：处置俘虏的势力）
    async reportCaptives(fates, winner) {
      const g = G();
      const mine = (fates || []).filter(x => x.from === g.player);
      if (!mine.length || winner < 0) return;
      const wn = g.factions[winner].name;
      const lines = mine.map(({ gen, fate }) => {
        if (fate === 'hired') { g.addLog(`${gen.name}被俘，归降了${wn}。`); return `${gen.name}被俘，归降了${wn}。`; }
        if (fate === 'executed') return `${gen.name}被俘，遭${wn}处斩。`;
        return gen.faction === g.player ? `${gen.name}被俘后获释，回到了${g.cities[gen.city].name}。` : `${gen.name}被俘后获释，流落在野。`;
      });
      await UI().say(faces(lines.join('\n'), { gens: mine.map(x => x.gen), max: 12 }));
    }

    async handleCaptives(s, playerDecides) {
      const g = G(), Cq = SG.Conquest, C = SG.Commands;
      if (!playerDecides) return Cq.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender);
      // 玩家只在获胜后处置俘虏，胜方必然据有 s.target（攻方刚攻下 / 守方守住），降将应编入该城
      const holderCity = s.target;
      const recruiter = g.ruler(g.player);
      for (const cap of s.captives.slice()) {
        const isRuler = cap.faction >= 0 && g.factions[cap.faction].ruler === cap.id;
        const r = await UI().choose('俘虏：' + SG.esc(cap.name) + (isRuler ? '（君主）' : ''), [
          item('登用', isRuler ? '君主不会投降' : '成功率约 ' + pct(C.hireChance(cap, recruiter, g.player)), !isRuler),
          item('释放', '放其归去'),
          item('处斩', '以绝后患'),
        ], genCard(cap, { sub: genSub(cap) + (cap.faction >= 0 && !isRuler ? `　忠诚 ${cap.loyalty}` : ''), mood: 'hurt' }));
        if (r === 0) {
          if (C.hire(cap, recruiter, g.player, holderCity.id)) await UI().say(cap.name + '：败军之将，承蒙不弃，愿降！', cap, CINNABAR, { mood: 'win', sub: genSub(cap) });
          else { await UI().say(cap.name + '：忠臣不事二主！', cap, GREY_SPEAKER, { mood: 'angry', sub: genSub(cap) }); Cq.release(cap); }
        } else if (r === 2) { Cq.execute(cap); await UI().say(SG.esc(cap.name) + '被处斩了。', cap, GREY_SPEAKER, { mood: 'hurt' }); }
        else Cq.release(cap);
      }
    }

    // ---------------------------------------------------------- 武将详情 --
    // 大头像、能力、所属与所在、必杀技（SG.Specials.of）；世界武将另有全名 / 原名 / 身份 / 生卒 / 介绍（SG.WorldInfo）
    showGeneral(gen) {
      if (!gen || UI().anyModal() || this._genInfoOpen) return;
      click();
      this._genInfoOpen = true;
      // 「习得新招」：关闭详情 → 习得对话框 → 重新打开详情（显示新学的招式）
      const loop = async () => {
        for (let k = 0; k < 6; k++) {
          const r = await this.generalInfo(gen);
          if (r !== 'learn') return;
          await this.learnDialog(gen);
          this.refreshAll();
        }
      };
      loop().catch(e => console.error(e)).then(() => { this._genInfoOpen = false; });
    }
    generalInfo(gen) {
      const g = G();
      const W = (SG.WorldInfo || {})[gen.name] || null;
      const f = gen.faction >= 0 ? g.factions[gen.faction] : null;
      const city = g.cities[gen.city];
      const isRuler = g.isRuler(gen);
      return new Promise(resolve => {
        const m = UI().openModal('sg-geninfo', { width: 940 });
        let done = false;
        const finish = v => { if (done) return; done = true; m.close(); resolve(v); };
        m.blocker.addEventListener('click', e => { if (e.target === m.blocker) finish(); });
        m.onKey = k => { if (k === 'Escape' || k === 'Enter' || k === 'Space') { finish(); return true; } return false; };
        const head = h('div', 'sg-dlg-head', null, m.panel);
        const tag = isRuler ? '<span class="sg-tag sg-tag-ruler">君</span>' : city && city.governor === gen.id ? '<span class="sg-tag sg-tag-gov">守</span>' : '';
        h('div', 'sg-dlg-title', tag + SG.esc(gen.name) +
          '<small>' + (f ? `<span style="color:${readable(f.color)}">${SG.esc(f.name)}</span>` : '在野') + (city ? '　' + SG.esc(city.name) : '') + '</small>', head);
        const close = SG.UI.button(head, '×', () => finish(), { className: 'sg-close', ariaLabel: '关闭' });
        const body = h('div', 'sg-geninfo-body', null, m.panel);
        const side = h('div', 'sg-geninfo-side', null, body);
        if (SG.Portrait) { try { side.appendChild(SG.Portrait.el(gen, { size: 168 })); } catch (e) { /* 无头像 */ } }
        // 能力条
        const stats = h('div', 'sg-geninfo-stats', null, side);
        for (const [k, v, col] of [['武力', gen.war, '#ff8c73'], ['智力', gen.intel, '#73bfff'], ['政治', gen.pol, '#73d98c']]) {
          const r = h('div', 'sg-geninfo-stat', `<span>${k}</span><b>${v}</b>`, stats);
          r.appendChild(SG.UI.bar(M.clamp01(v / 100), col));
        }
        const main = h('div', 'sg-geninfo-main', null, body);
        const chips = [];
        const P = Prog();
        if (f) chips.push(`<span class="sg-chip"><i>兵</i><b>${n0(gen.troops)}</b></span>`, `<span class="sg-chip"><i>训练</i><b>${gen.training}</b></span>`,
          `<span class="sg-chip"><i>忠诚</i><b>${isRuler ? 100 : gen.loyalty}</b></span>`);
        if (gen.born > 0) chips.push(`<span class="sg-chip"><i>生年</i><b>${gen.born}</b><i>${g.year >= gen.born ? (g.year - gen.born) + '岁' : ''}</i></span>`);
        if (chips.length) h('div', 'sg-geninfo-chips', chips.join(''), main);
        // 等级、经验、战绩（第三版成长）
        if (P) { try { this.genInfoLevel(main, gen, f && gen.faction === g.player, () => finish('learn')); } catch (e) { console.error(e); } }
        // 必杀：专属在前，其后为习得的可习得必杀
        try { this.genInfoSpecials(main, gen); } catch (e) { console.error(e); }
        // 计策：原四计（按智力）+ 习得的计策
        try { this.genInfoTactics(main, gen); } catch (e) { console.error(e); }
        // 世界武将：史实资料
        if (W) {
          const bio = h('div', 'sg-geninfo-bio', null, main);
          const life = (W.born ? W.born : '?') + '—' + (W.died ? W.died : '?') + '年';
          h('div', 'sg-geninfo-full', SG.esc(W.full || gen.name) + (W.orig ? `<span class="sg-geninfo-orig">${SG.esc(W.orig)}</span>` : ''), bio);
          h('div', 'sg-geninfo-role', SG.esc(W.role || '') + (W.born || W.died ? `<span class="sg-muted">　${SG.esc(life)}</span>` : ''), bio);
          if (W.note) h('div', 'sg-geninfo-note', SG.esc(W.note), bio);
          if (W.liberty) h('div', 'sg-geninfo-liberty', '<b>游戏取舍</b>' + SG.esc(W.liberty), bio);
        }
        try { close.focus({ preventScroll: true }); } catch (e) { /* 忽略 */ }
      });
    }

    // 详情：等级块（Lv、经验条、距下一级、战绩；我方武将有空格时「习得新招」按钮）
    genInfoLevel(main, gen, mine, onLearn) {
      const P = Prog();
      const e = P.expInfo(gen);
      const box = h('div', 'sg-geninfo-lv', null, main);
      const rec = gen.record || {};
      const need = e.max ? '已满级' : '经验 ' + e.exp + ' / ' + e.next + '<span class="sg-muted">（距 ' + (e.level + 1) + ' 级 ' + e.need + '）</span>';
      h('div', 'sg-geninfo-lv-head', '<span class="sg-geninfo-lv-n">Lv<b>' + e.level + '</b></span>' + expBar(e.pct) + '<span class="sg-geninfo-lv-exp">' + need + '</span>', box);
      if (rec.battles > 0) h('div', 'sg-geninfo-rec', `出阵 <b>${rec.battles}</b>　胜 <b>${rec.wins}</b>　击溃 <b>${rec.routs}</b>　单挑胜 <b>${rec.duels}</b>`, box);
      if (mine && canLearn(gen)) {
        const op = P.openings(gen);
        const what = [];
        if (op.special > 0) what.push('必杀 ' + op.special);
        if (op.tactic > 0) what.push('计策 ' + op.tactic);
        const b = SG.UI.button(box, '习得新招<small>' + what.join('、') + '</small>', () => onLearn(), { primary: true, className: 'sg-geninfo-learn' });
        b.title = '从 3 个候选中选择要学的必杀 / 计策';
      }
    }
    // 详情：必杀列表（每招：名称、专属 / 等阶、机制、说明、数值摘要）
    genInfoSpecials(main, gen) {
      const S = SG.Specials;
      if (!S) return;
      let list = [];
      try { list = typeof S.known === 'function' ? S.known(gen) : [S.of(gen)].filter(Boolean); } catch (e) { list = []; }
      if (!list.length) return;
      const P = Prog();
      const sec = h('div', 'sg-geninfo-sec', null, main);
      const slot = P && typeof S.pool === 'function' ? slotText(gen, 'special') : '';
      h('div', 'sg-geninfo-sec-head', '<span class="sg-geninfo-sp-label">必杀</span><span class="sg-muted">' + list.length + ' 招' + (slot ? ' · ' + slot : '') + '</span>', sec);
      for (const sp of list) {
        const col = S.uiColor ? S.uiColor(sp) : (sp.color || '#f3c969');
        const box = h('div', 'sg-geninfo-special' + (sp.pool ? ' is-learned' : ''), null, sec);
        const tier = sp.pool ? (S.tierName ? S.tierName(sp.tier) : sp.tier + '阶') : '专属';
        h('div', 'sg-geninfo-sp-head', `<span class="sg-sp-tier${sp.pool ? '' : ' is-sig'}">${tier}</span><b style="color:${col}">${SG.esc(sp.name)}</b>` +
          (S.kindName ? `<small>${SG.esc(S.kindName(sp.kind))}</small>` : ''), box);
        if (sp.desc) h('div', 'sg-geninfo-sp-desc', SG.esc(sp.desc), box);
        let rules = '';
        try { rules = S.rules ? S.rules(sp) : ''; } catch (e) { rules = ''; }
        if (rules) h('div', 'sg-geninfo-sp-rules', SG.esc(rules), box);
      }
    }
    // 详情：计策列表（原四计按智力自带；习得的计策带等阶与说明）
    genInfoTactics(main, gen) {
      const T = SG.Tactics;
      let list = [];
      if (T && typeof T.known === 'function') { try { list = T.known(gen); } catch (e) { list = []; } }
      else list = SG.Defs.tactics.filter(t => gen.intel >= t.reqInt);
      const P = Prog();
      const sec = h('div', 'sg-geninfo-sec', null, main);
      const slot = P && T && typeof T.learnable === 'function' ? slotText(gen, 'tactic') : '';
      h('div', 'sg-geninfo-sec-head', '<span class="sg-geninfo-sp-label is-tac">计策</span><span class="sg-muted">' + (list.length ? list.length + ' 计' : '未习计策') + (slot ? ' · ' + slot : '') + '</span>', sec);
      if (!list.length) return;
      const grid = h('div', 'sg-geninfo-tacs', null, sec);
      for (const t of list) {
        const isNew = T && T.isNew ? T.isNew(t) : false;
        const tier = T && T.TIER_NAME ? T.TIER_NAME[t.tier] || '' : '';
        const row = h('div', 'sg-geninfo-tac' + (isNew ? ' is-learned' : ''), null, grid);
        h('div', 'sg-geninfo-tac-head', `<b style="color:${spColor(t)}">${SG.esc(t.name)}</b>` +
          `<small>${isNew ? SG.esc(tier) : '自带'}${t.reqInt ? ' · 智' + t.reqInt : ''}</small>`, row);
        const d = isNew ? stripLore(t.desc, t.lore) : (t.desc || '');
        if (t.lore && isNew) h('div', 'sg-geninfo-tac-lore', SG.esc(t.lore), row);
        if (d) h('div', 'sg-geninfo-tac-desc', SG.esc(d), row);
      }
    }

    // ---------------------------------------------------------- 情报 --
    async factionInfo() {
      const g = G();
      const fs = g.factions.filter(f => f.alive).map((f, i) => ({ f, n: g.cityCount(f.id), i }))
        .sort((a, b) => (b.n - a.n) || (a.i - b.i)).map(o => o.f);
      const grp = groupedFactions(fs, f => item(
        swatch(f.color) + SG.esc(g.ruler(f.id).name) + (f.name !== g.ruler(f.id).name && regionsOf() ? `<small class="sg-faction-name">${SG.esc(f.name)}</small>` : '') +
        (f.id === g.player ? '（我方）' : g.allied(f.id, g.player) ? '（同盟）' : ''),
        `城 ${g.cityCount(f.id)}　将 ${g.generalsOf(f.id).length}　兵 ${sum(g.citiesOf(f.id), c => g.troopsIn(c))}`, true, null, { avatar: g.ruler(f.id) }));
      const pr = UI().choose('天下势力', grp.items, regionsOf() ? '按地域分组（我方所在地域在前，其余由近及远）；点选势力，镜头飞往其都城。' : null, 760);
      markGroupHeads(grp.heads);
      const r = await pr;
      if (r < 0) return;
      const f0 = grp.pick(r);
      if (!f0) return;
      const cap = g.cities[g.ruler(f0.id).city];
      const p = SG.mapPos(cap);
      SG.Game.rig.focusMap(p.x, p.y, 45);
      this.selectCity(cap.id);
    }
  }

  SG.StrategyScreen = StrategyScreen;
})();
