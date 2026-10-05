'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 战略画面
   ← Strategy/StrategyScreen.cs：顶栏、城池情报、指令与月份循环
   协程 → async 函数。new SG.StrategyScreen().run() → Promise（游戏结束时重载页面）。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const CINNABAR = '#c8382c';
  const GREY_SPEAKER = '#666673';               // C# new Color(0.4, 0.4, 0.45)

  function G() { return SG.G; }
  function UI() { return SG.UI; }
  function sfx(name, vol) { try { if (SG.Sfx) SG.Sfx.play(name, vol === undefined ? 0.8 : vol); } catch (e) { /* 无音频 */ } }
  function click() { try { if (SG.Sfx) SG.Sfx.click(); } catch (e) { /* 无音频 */ } }
  function music(kind) { try { if (SG.Sfx) SG.Sfx.music(kind); } catch (e) { /* 无音频 */ } }
  function item(label, right, enabled, desc) {
    if (SG.UI && SG.UI.item) return SG.UI.item(label, right === undefined ? null : right, enabled === undefined ? true : enabled, desc === undefined ? null : desc);
    return { label, right: right === undefined ? null : right, enabled: enabled === undefined ? true : enabled, desc: desc === undefined ? null : desc, selected: false };
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
  function dot(color, glyph) { return `<span class="sg-dot" style="color:${color}">${glyph || '●'}</span>`; }

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
  class StrategyScreen {
    constructor() {
      this.hud = null; this.cityPanel = null; this.cmdGrid = null; this.genList = null;
      this.topLeft = null; this.topCenter = null; this.cityTitle = null; this.cityStats = null;
      this.endBtn = null;
      this.labels = new Map();        // cityId → { el, text, handle }
      this.selected = -1;
      this.busy = false;
      this.endMonth = false;
      this._onMapTap = (x, y) => this.onMapTap(x, y);
    }

    // ---------------------------------------------------------- 主循环 --
    async run() {
      const game = SG.Game;
      this.buildHud();
      game.rig.onTap.push(this._onMapTap);
      music('map');
      const cap = G().cities[G().ruler(G().player).city];
      const p = SG.mapPos(cap);
      game.rig.focusMap(p.x, p.y, 48);
      this.selectCity(cap.id);
      UI().banner(G().year + '年 ' + G().month + '月', G().playerFaction.name + '军');
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
        for (const b of battles) { await this.defend(b); if (!G().playerFaction.alive) break; }
        if (G().playerFaction.alive) for (const n of SG.StrategyAI.endMonth()) news.push(n);
        game.map.refresh(G()); this.refreshAll();
        if (!G().playerFaction.alive) { await this.gameOver(false); return; }
        if (G().cities.every(c => c.owner === G().player)) { await this.gameOver(true); return; }
        if (news.length > 0) await UI().say(news.slice(0, 6).join('\n'));
        try { G().save(); } catch (e) { console.warn(e); }
        UI().banner(G().year + '年 ' + G().month + '月', '令牌 ' + G().tokens + ' 枚', 1.8);
        this.busy = false;
      }
    }

    async gameOver(won) {
      const g = G();
      this.busy = true;
      this.refreshAll();
      if (won) {
        sfx('win');
        UI().banner('天下统一', g.ruler(g.player).name + '终成霸业', 4);
        await SG.wait(2.5);
        await UI().say(`${g.year}年${g.month}月，${g.ruler(g.player).name}平定四海，一统天下。\n麾下武将 ${g.generalsOf(g.player).length} 员，历时 ${g.year - SG.ScenarioData.StartYear} 年。`);
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
      SG.UI.button(row, '记录', () => {
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
      if (window.ResizeObserver) new ResizeObserver(syncTop).observe(top);
      window.addEventListener('resize', syncTop);

      // 地图上的城名
      for (const cv of SG.Game.map.cities.values()) {
        const el = h('div', 'sg-citylabel', SG.esc(cv.city.name), null);
        const handle = SG.UI.follow(el, () => cv.labelPos, { hideBeyond: 170 });
        this.labels.set(cv.city.id, { el, handle });
      }
    }

    setMapLabelsVisible(on) {
      for (const l of this.labels.values()) {
        if (l.handle) l.handle.alpha = on ? 1 : 0;
        else l.el.style.visibility = on ? '' : 'hidden';
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
      for (const [id, l] of this.labels) {
        const c = g.cities[id];
        const col = c.owner >= 0 ? g.factions[c.owner].color : '#b3b3b3';
        l.el.innerHTML = dot(col, '●') + SG.esc(c.name) + (c.owner === g.player ? `<small>${g.troopsIn(c)}</small>` : '');
        l.el.classList.toggle('is-mine', c.owner === g.player);
        l.el.classList.toggle('is-selected', id === this.selected);
      }
      this.refreshCityPanel();
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
      if (this.selected < 0) { this.cityPanel.style.display = 'none'; return; }
      if (this.cityPanel.style.display === 'none') {
        this.cityPanel.style.display = '';
        if (this._syncTop) this._syncTop();
      }
      const c = g.cities[this.selected];
      const mine = c.owner === g.player;
      const owner = c.owner >= 0 ? g.factions[c.owner] : null;
      this.cityTitle.innerHTML = SG.esc(c.name) + '<small>' +
        (owner ? `<span style="color:${readable(owner.color)}">${SG.esc(owner.name)}</span>` : '空城') + '</small>';
      const gov = c.governor >= 0 ? g.generals[c.governor].name : '—';
      const free = g.freeFoundIn(c.id);
      this.cityStats.innerHTML =
        `太守 <b>${SG.esc(gov)}</b>　　人口 ${n0(c.population)}\n` +
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
        const tag = g.isRuler(gen) ? '<span class="sg-tag sg-tag-ruler">君</span>' : gen.id === c.governor ? '<span class="sg-tag sg-tag-gov">守</span>' : '';
        h('div', 'sg-genrow-name', tag + '<b>' + SG.esc(gen.name) + '</b>' + (gen.moved ? '<span class="sg-moved">已行动</span>' : ''), row);
        h('div', 'sg-genrow-stats', `武${gen.war} 智${gen.intel} 政${gen.pol}\n兵${gen.troops} 训${gen.training} 忠${g.isRuler(gen) ? 100 : gen.loyalty}`, row);
      }
      this.genList.scrollTop = scroll;

      this.cmdGrid.innerHTML = '';
      if (!mine) return;
      const tok = g.tokens > 0 && !this.busy;
      const hasOwnLink = c.links.some(i => g.cities[i].owner === g.player);
      this.addCmd('开发', tok, () => this.do(() => this.cmdDevelop(c)));
      this.addCmd('征兵', tok, () => this.do(() => this.cmdRecruit(c)));
      this.addCmd('训练', tok, () => this.do(() => this.cmdTrain(c)));
      this.addCmd('搜索', tok, () => this.do(() => this.cmdSearch(c)));
      this.addCmd('登用', tok && free.length > 0, () => this.do(() => this.cmdHire(c)));
      this.addCmd('移动', tok && hasOwnLink, () => this.do(() => this.cmdMove(c)));
      this.addCmd('输送', tok && hasOwnLink, () => this.do(() => this.cmdTransport(c)));
      this.addCmd('外交', tok, () => this.do(() => this.cmdDiplomacy(c)));
      this.addCmd('出征', tok && c.links.some(i => g.cities[i].owner !== g.player && !g.allied(g.cities[i].owner, g.player)), () => this.do(() => this.cmdAttack(c)), true);
      this.addCmd('赏赐', !this.busy, () => this.do(() => this.cmdReward(c)));
      this.addCmd('交易', !this.busy, () => this.do(() => this.cmdTrade(c)));
      this.addCmd('任命', !this.busy, () => this.do(() => this.cmdAppoint(c)));
    }

    addCmd(name, enabled, action, primary) {
      const b = SG.UI.button(this.cmdGrid, name, action, primary ? { primary: true } : undefined);
      b.disabled = !enabled;
      return b;
    }

    do(routine) {
      if (this.busy || this.endMonth || UI().anyModal()) return;
      this._wrap(routine);
    }
    async _wrap(routine) {
      this.busy = true; this.refreshAll();
      try { await routine(); } catch (e) { console.error(e); }
      this.busy = false;
      SG.Game.map.refresh(G());
      this.refreshAll();
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
      const items = gens.map(g => item(SG.esc(g.name), info(g), !enabled || enabled(g)));
      const r = await UI().choose(title, items, null, 700);
      return r >= 0 ? gens[r] : null;
    }
    available(c) { return G().officersIn(c).filter(g => !g.moved); }

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
      await UI().say(C.develop(c, who, kinds[r]));
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
      await UI().say(C.recruit(c, who, n));
    }

    async cmdTrain(c) {
      const who = await this.pickGeneral('训练哪支部队？', G().officersIn(c), g => `兵 ${g.troops}　训练 ${g.training}`, g => g.troops > 0 && g.training < 100);
      if (!who) return;
      this.useToken();
      await UI().say(SG.Commands.train(who));
    }

    // ---------------------------------------------------------- 人事 --
    async cmdSearch(c) {
      const C = SG.Commands;
      const who = await this.pickGeneral('派谁搜索人才？', this.available(c), g => '智力 ' + g.intel);
      if (!who) return;
      this.useToken();
      const msg = C.search(c, who);
      const found = C.searchFound;
      await UI().say(msg);
      if (found) {
        const ok = await UI().confirm(`登用${found.name}？（武${found.war} 智${found.intel} 政${found.pol}，成功率约 ${pct(C.hireChance(found, who, G().player))}）`, '登用', '暂且作罢');
        if (ok) await this.hireResult(found, who, c);
      }
    }

    async hireResult(target, recruiter, c) {
      if (SG.Commands.hire(target, recruiter, G().player, c.id)) {
        sfx('win', 0.5);
        await UI().say(target.name + '：久闻明公大名，愿效犬马之劳！\n（' + target.name + '加入了我军）', target.name, CINNABAR);
      } else {
        await UI().say(target.name + '：在下另有志向，请回吧。', target.name, GREY_SPEAKER);
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
      UI().toast(SG.Commands.reward(c, who));
    }

    async cmdAppoint(c) {
      const g = G();
      const who = await this.pickGeneral('任命太守', g.officersIn(c), x => `政${x.pol} 智${x.intel}`);
      if (!who) return;
      if (g.officersIn(c).some(x => g.isRuler(x)) && !g.isRuler(who)) { await UI().say('君主所在之城，由君主亲自坐镇。'); return; }
      c.governor = who.id;
      UI().toast(who.name + '出任' + c.name + '太守');
    }

    async cmdMove(c) {
      const g = G();
      const dests = c.links.map(i => g.cities[i]).filter(x => x.owner === g.player);
      const r = await UI().choose('移往何处？', dests.map(d => item(SG.esc(d.name), '武将 ' + g.officersIn(d).length + '　兵 ' + g.troopsIn(d))));
      if (r < 0) return;
      const dest = dests[r];
      const gens = this.available(c);
      if (gens.length === 0) { await UI().say('本月已无可调动的武将。'); return; }
      const sel = await UI().chooseMany('调动武将至' + dest.name, gens.map(x => item(SG.esc(x.name), '兵 ' + x.troops)), 10, '可一次调动多名武将（消耗 1 枚令牌）');
      if (!sel || sel.length === 0) return;
      this.useToken();
      await SG.Game.map.march(c, dest, g.playerFaction.color, 1.2);
      for (const i of sel) SG.Commands.move(gens[i], dest);
      UI().toast(sel.length + ' 名武将移驻' + dest.name);
    }

    async cmdTransport(c) {
      const g = G();
      const dests = c.links.map(i => g.cities[i]).filter(x => x.owner === g.player);
      const r = await UI().choose('输送至何处？', dests.map(d => item(SG.esc(d.name), '金 ' + d.gold + '　粮 ' + d.food)));
      if (r < 0) return;
      const gold = await UI().pickNumber('输送金', 0, c.gold, 50, M.idiv(c.gold, 2), null);
      if (gold < 0) return;
      const food = await UI().pickNumber('输送粮', 0, c.food, 500, M.idiv(c.food, 2), null);
      if (food < 0) return;
      this.useToken();
      UI().toast(SG.Commands.transport(c, dests[r], gold, food));
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
        const fi = await UI().choose('与谁结盟？', fs.map(f => item(SG.esc(g.ruler(f.id).name),
          g.allied(f.id, g.player) ? '已同盟' : '城 ' + g.cityCount(f.id) + '　成功率约 ' + pct(C.allyChance(g.player, f.id, B.AllianceGift)),
          !g.allied(f.id, g.player))));
        if (fi < 0) return;
        const gift = await UI().pickNumber('赠送礼金', 0, c.gold, 50, Math.min(c.gold, B.AllianceGift), v => '成功率约 ' + pct(C.allyChance(g.player, fs[fi].id, v)));
        if (gift < 0) return;
        this.useToken();
        await UI().say(C.ally(c, g.player, fs[fi].id, gift));
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
      await UI().say(r === 1 ? C.discord(agent, target) : C.persuade(agent, target, g.player));
    }

    // ---------------------------------------------------------- 战争 --
    async cmdAttack(c) {
      const g = G(), B = SG.Balance;
      const targets = c.links.map(i => g.cities[i]).filter(x => x.owner !== g.player && !g.allied(x.owner, g.player));
      const r = await UI().choose('攻打何处？', targets.map(t => item(SG.esc(t.name),
        (t.owner >= 0 ? g.factions[t.owner].name : '空城') + '　武将 ' + g.officersIn(t).length + '　兵 ' + g.troopsIn(t) + '　防 ' + SG.cityDefense(t))));
      if (r < 0) return;
      const target = targets[r];
      const gens = this.available(c).filter(x => x.troops > 0);
      if (gens.length === 0) { await UI().say('没有可以出征的部队（需有兵力且本月未行动）。'); return; }
      const sel = await UI().chooseMany('出征武将（至多 ' + B.MaxSortieGenerals + ' 名）',
        gens.map(x => item(SG.esc(x.name), `兵${x.troops} 武${x.war} 智${x.intel} 训${x.training}`)), B.MaxSortieGenerals,
        '第一位选中的武将为主将；主将败走则全军撤退。');
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
      const leader = g.ruler(s.attacker).name === s.atk[0].name ? g.ruler(s.attacker).name : g.factions[s.attacker].name + '军' + s.atk[0].name;
      await UI().say(`急报！${leader}率 ${s.atk.length} 名武将、兵 ${sum(s.atk, x => x.troops)} 来犯${s.target.name}！`);
      await this.fight(s, 1);
    }

    // 战斗与战后处理。playerSide：玩家是攻方 0 还是守方 1
    async fight(s, playerSide) {
      const g = G(), Cq = SG.Conquest;
      if (s.def.length === 0 || s.def.every(d => d.troops <= 0)) {
        Cq.autoResolve(s);
        Cq.apply(s);
        if (s.captives.length > 0) await this.handleCaptives(s, playerSide === 0);
        await UI().say(s.summary);
        return;
      }
      const mode = await UI().choose(s.target.name + '之战', [item('亲自指挥', '在战场上调兵遣将'), item('委任', '由部将自行作战，立即得出结果')], null, 560);
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
        music('map');
      } else Cq.autoResolve(s);
      Cq.apply(s);
      const playerWon = s.attackerWon === (playerSide === 0);
      if (s.captives.length > 0) {
        if (playerWon) await this.handleCaptives(s, true);
        else Cq.aiDecideCaptives(s, playerSide === 0 ? s.defender : s.attacker);
      }
      for (const f of g.factions) if (f.alive && g.cityCount(f.id) === 0) g.checkFactionDeath(f.id);
      SG.Game.map.refresh(g);
      this.refreshAll();
      const res = playerSide === 0
        ? (s.attackerWon ? '我军攻陷了' + s.target.name + '！' : '攻城失利，全军撤回' + s.src.name + '。')
        : (s.attackerWon ? s.target.name + '失守了……' : '我军成功守住了' + s.target.name + '！');
      await UI().say(res);
      if (s.attackerWon && s.defender >= 0 && !g.factions[s.defender].alive) await UI().say(g.factions[s.defender].name + '势力就此灭亡。');
    }

    async handleCaptives(s, playerDecides) {
      const g = G(), Cq = SG.Conquest, C = SG.Commands;
      if (!playerDecides) { Cq.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender); return; }
      const holderCity = s.attackerWon ? s.target : s.src;
      const recruiter = g.ruler(g.player);
      for (const cap of s.captives.slice()) {
        const isRuler = cap.faction >= 0 && g.factions[cap.faction].ruler === cap.id;
        const r = await UI().choose('俘虏：' + cap.name + (isRuler ? '（君主）' : ''), [
          item('登用', isRuler ? '君主不会投降' : '成功率约 ' + pct(C.hireChance(cap, recruiter, g.player)), !isRuler),
          item('释放', '放其归去'),
          item('处斩', '以绝后患'),
        ], `武${cap.war} 智${cap.intel} 政${cap.pol}`);
        if (r === 0) {
          if (C.hire(cap, recruiter, g.player, holderCity.id)) await UI().say(cap.name + '：败军之将，承蒙不弃，愿降！', cap.name, CINNABAR);
          else { await UI().say(cap.name + '：忠臣不事二主！', cap.name, GREY_SPEAKER); Cq.release(cap); }
        } else if (r === 2) { Cq.execute(cap); await UI().say(cap.name + '被处斩了。'); }
        else Cq.release(cap);
      }
    }

    // ---------------------------------------------------------- 情报 --
    async factionInfo() {
      const g = G();
      const fs = g.factions.filter(f => f.alive).map((f, i) => ({ f, n: g.cityCount(f.id), i }))
        .sort((a, b) => (b.n - a.n) || (a.i - b.i)).map(o => o.f);
      const r = await UI().choose('天下势力', fs.map(f => item(
        swatch(f.color) + SG.esc(g.ruler(f.id).name) + (f.id === g.player ? '（我方）' : g.allied(f.id, g.player) ? '（同盟）' : ''),
        `城 ${g.cityCount(f.id)}　将 ${g.generalsOf(f.id).length}　兵 ${sum(g.citiesOf(f.id), c => g.troopsIn(c))}`)), null, 760);
      if (r < 0) return;
      const cap = g.cities[g.ruler(fs[r].id).city];
      const p = SG.mapPos(cap);
      SG.Game.rig.focusMap(p.x, p.y, 45);
      this.selectCity(cap.id);
    }
  }

  SG.StrategyScreen = StrategyScreen;
})();
