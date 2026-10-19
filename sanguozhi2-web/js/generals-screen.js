'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 武将录（UI-V4 §8）
   漆框窗口（四边 8 px；手机全屏）。页首：印「将」· 武将录 · 人数一行 · 我方 / 他国 / 在野 页签 · 收。
   筛选条：姓名 / 城名搜索 · 家族（全部 / 武 / 谋 / 文，按最高属性）· 状态（可行动 · 可习得 · 无官 · 低忠 · 外出，可多选）·
   城 · 排序（武力 智力 政治 等级 兵力 忠诚 官品 姓名；点列头排序，再点反向）· 列表 / 名片。
   列表：立绘（已行动盖印）· 姓名 + 君 / 守 / 新 + 官名 · 等级与经验 · 武 智 政（装备加成金色上标 + 斜纹）· 兵 训 · 忠 · 所在 · 六格装备。
   行高 52 px，超过 40 行时只画看得见的部分。单击选中（右栏更新），双击 / Enter / 「详情」打开人物菜单（以当前筛选与排序为 ‹ › 顺序）。
   右栏（300 px）：小立绘台（3D，待机，无操作）、姓名、官职、摘要一行、详情 · 赏赐 · 装备 · 移动。
   批量（Shift / Ctrl 点选、触屏长按开始）：同城的几名武将 → 底部条：赏赐（各 n 金）· 移动（一个目的地、一枚令牌）· 清除选择。
   手机：无右栏；列：立绘 · 姓名 + 官 · 武智政 · 兵训 · 忠；点按即打开人物菜单；筛选与排序在「筛选」抽屉里。

   SG.GeneralsScreen.open({ city, filter, tab }) → Promise     SG.GeneralsScreen.isOpen()   .current（测试）
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});
  const esc = s => SG.esc(String(s === undefined || s === null ? '' : s));
  const UI = () => SG.UI;
  const G = () => SG.G;
  const icon = (id, size) => (SG.UIIcons ? SG.UIIcons.svg(id, size) : '');
  const n0 = v => Math.round(v || 0).toLocaleString('en-US');
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html !== undefined && html !== null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  const ROW_H = 52;
  const SORTS = [['war', '武力'], ['intel', '智力'], ['pol', '政治'], ['level', '等级'], ['troops', '兵力'], ['loyalty', '忠诚'], ['office', '官品'], ['name', '姓名']];
  const STATUS = [['ready', '可行动'], ['learn', '可习得'], ['nooffice', '无官'], ['lowloy', '低忠'], ['away', '外出']];
  // 会话内保留的筛选与排序
  const session = { tab: 'mine', fam: 'all', status: {}, city: -1, sort: 'war', dir: -1, view: 'list', q: '' };

  function famOf(x) {
    try { if (SG.Titles && SG.Titles.family) return SG.Titles.family(x); } catch (e) { /* 忽略 */ }
    return x.war >= x.intel && x.war >= x.pol ? 'war' : x.intel >= x.pol ? 'intel' : 'pol';
  }
  function levelOf(x) { try { return SG.Progress && SG.Progress.expInfo ? SG.Progress.expInfo(x) : { level: x.level || 1, pct: 0 }; } catch (e) { return { level: 1, pct: 0 }; } }
  function canLearn(x) { try { return !!(SG.Progress && SG.Progress.candidates && SG.Progress.candidates(x, 3).length); } catch (e) { return false; } }
  function officeTier(x) { try { return SG.Titles && SG.Titles.curTier ? SG.Titles.curTier(x) : (x.office ? x.office.tier | 0 : 0); } catch (e) { return 0; } }
  function gearOn() { const g = G(); return !!(SG.Gear && SG.Items && SG.Items.enabled !== false && g && g.items); }
  function effStat(x, k) { if (!gearOn()) return x[k] | 0; try { return SG.Gear.eff(x, k); } catch (e) { return x[k] | 0; } }

  let cur = null;
  const GeneralsScreen = {
    get current() { return cur; },
    isOpen() { return !!(cur && !cur.win.closed); },
    open(opts) {
      opts = opts || {};
      if (cur && !cur.win.closed) return cur.promise;
      const g = G();
      if (!g) return Promise.resolve();
      if (opts.tab) session.tab = opts.tab;
      if (opts.city !== undefined && opts.city !== null) session.city = typeof opts.city === 'object' ? opts.city.id : opts.city;
      else if (opts.keepCity !== true) session.city = -1;
      if (opts.filter) { session.status = {}; session.status[opts.filter] = true; }
      let resolveP;
      const promise = new Promise(r => { resolveP = r; });
      const short = document.documentElement.classList.contains('sg-short');
      const st = cur = { promise, sel: null, multi: new Set(), list: [], short };
      const win = st.win = UI().window('generals', { title: '武将录', seal: '将', width: 'calc(100vw - 16px)', height: 'calc(100vh - 16px)', className: 'sg-gens', side: !short, foot: true,
        onClose: () => { if (st.stage) st.stage.dispose(); cur = null; resolveP(); } });
      // 页签：我方 · 他国 · 在野
      st.ftabs = UI().tabs(win.tools, [{ id: 'mine', label: '我方' }, { id: 'other', label: '他国' }, { id: 'free', label: '在野' }], id => { session.tab = id; st.sel = null; st.multi.clear(); refresh(); }, { value: session.tab, className: 'sg-gens-ftabs' });
      win.tools.insertBefore(st.ftabs.el, win.tools.firstChild);
      // 筛选条
      const bar = h('div', 'sg-gens-bar', null, win.page);
      const search = h('label', 'sg-search', icon('mirror', 18) + '<input type="search" placeholder="姓名 / 城名" aria-label="搜索武将" />', bar);
      const inp = search.querySelector('input');
      inp.value = session.q;
      inp.addEventListener('input', () => { session.q = inp.value.trim(); refresh(); });
      inp.addEventListener('keydown', e => { if (e.key === 'Escape') { if (inp.value) { inp.value = ''; session.q = ''; refresh(); e.stopPropagation(); } else inp.blur(); } });
      const filt = h('div', 'sg-gens-filters', null, bar);
      const famSeg = UI().segmented(filt, [['all', '全部'], ['war', '武'], ['intel', '谋'], ['pol', '文']], session.fam, v => { session.fam = v; refresh(); });
      famSeg.classList.add('sg-gens-fam');
      const chips = h('div', 'sg-seg sg-gens-status', null, filt);
      for (const [id, lb] of STATUS) {
        const b = h('button', 'sg-seg-o' + (session.status[id] ? ' is-on' : ''), esc(lb), chips);
        b.type = 'button';
        b.setAttribute('aria-pressed', session.status[id] ? 'true' : 'false');
        b.addEventListener('click', () => { session.status[id] = !session.status[id]; b.classList.toggle('is-on', !!session.status[id]); b.setAttribute('aria-pressed', session.status[id] ? 'true' : 'false'); UI().sfx('ui-tab', 0.8); refresh(); });
      }
      h('div', 'sg-spacer', null, bar);
      st.citySel = h('select', 'sg-select sg-gens-city', null, bar);
      st.citySel.setAttribute('aria-label', '所在城');
      st.citySel.addEventListener('change', () => { session.city = +st.citySel.value; refresh(); });
      const sortSel = h('select', 'sg-select sg-gens-sort', SORTS.map(s => '<option value="' + s[0] + '">' + s[1] + '</option>').join(''), bar);
      sortSel.value = session.sort;
      sortSel.setAttribute('aria-label', '排序');
      sortSel.addEventListener('change', () => { session.sort = sortSel.value; session.dir = session.sort === 'name' ? 1 : -1; refresh(); });
      st.sortSel = sortSel;
      const viewSeg = UI().segmented(win.tools, [['list', '列表'], ['cards', '名片']], session.view, v => { session.view = v; refresh(); });
      win.tools.insertBefore(viewSeg, st.ftabs.el);
      viewSeg.classList.add('sg-gens-view');
      if (short) {
        // 手机：筛选与排序进抽屉
        filt.style.display = 'none'; st.citySel.style.display = 'none'; sortSel.style.display = 'none'; viewSeg.style.display = 'none';
        UI().button(bar, '筛选', () => {
          const s = UI().sheet({ title: '筛选与排序' });
          s.body.appendChild(filt); filt.style.display = '';
          const r2 = h('div', 'sg-gens-sheetrow', null, s.body); r2.appendChild(st.citySel); r2.appendChild(sortSel); st.citySel.style.display = ''; sortSel.style.display = '';
        }, { kind: 'paper', icon: 'filter', small: true });
      }
      // 列表头（可排序）
      st.head = h('div', 'sg-gl-head', null, win.page);
      const cols = short ? [['', ''], ['name', '姓名 · 官职'], ['war', '武 · 智 · 政'], ['troops', '兵 · 训'], ['loyalty', '忠']]
        : [['', ''], ['name', '姓名 · 官职'], ['level', '等级'], ['war', '武 · 智 · 政'], ['troops', '兵 · 训'], ['loyalty', '忠'], ['', '所在'], ['', '装备']];
      for (const [k, lb] of cols) {
        const c = h('span', 'sg-gl-hc' + (k ? ' is-sort' : ''), esc(lb), st.head);
        if (k) { c.dataset.k = k; c.addEventListener('click', () => { if (session.sort === k) session.dir = -session.dir; else { session.sort = k; session.dir = k === 'name' ? 1 : -1; } sortSel.value = session.sort; UI().sfx('ui-tab', 0.7); refresh(); }); }
      }
      st.scroll = h('div', 'sg-gl-scroll', null, win.page);
      st.inner = h('div', 'sg-gl-inner', null, st.scroll);
      st.scroll.addEventListener('scroll', () => drawRows());
      // 右栏
      if (win.side) {
        st.sideStage = h('div', 'sg-gens-stage', null, win.side);
        st.sideInfo = h('div', 'sg-gens-info', null, win.side);
        st.sideActs = h('div', 'sg-gens-acts', null, win.side);
      }
      // 批量条
      st.batch = h('div', 'sg-gens-batch', null, win.foot);
      st.footNote = h('div', 'sg-note', SG.isTouch ? '点按：详情 · 长按：多选（同城）' : '单击：选中 · 双击 / Enter：人物菜单 · Shift / Ctrl：多选（同城）', win.foot);
      win.onKey = (k, e) => {
        const a = document.activeElement;
        if (a && a.tagName === 'INPUT') return false;
        const L = st.list;
        if (!L.length) return false;
        let i = st.sel ? L.indexOf(st.sel) : -1;
        if (k === 'ArrowDown' || k === 'ArrowUp') { i = Math.max(0, Math.min(L.length - 1, i + (k === 'ArrowDown' ? 1 : -1))); select(L[i], true); return true; }
        if (k === 'Enter' && st.sel) { openChar(st.sel); return true; }
        if (k === '/' || (e && e.key === 'f' && (e.ctrlKey || e.metaKey))) { inp.focus(); return true; }
        return false;
      };
      function pool() {
        const gg = G();
        if (session.tab === 'mine') return gg.generalsOf(gg.player);
        if (session.tab === 'other') return gg.generals.filter(x => !x.dead && x.faction >= 0 && x.faction !== gg.player);
        return gg.generals.filter(x => !x.dead && SG.isFree(x) && !x.hidden);
      }
      function matches(x) {
        const gg = G(), mi = gg.monthIndex;
        if (session.fam !== 'all' && famOf(x) !== session.fam) return false;
        if (session.city >= 0 && x.city !== session.city) return false;
        if (session.q) { const c = gg.cities[x.city]; if (x.name.indexOf(session.q) < 0 && !(c && c.name.indexOf(session.q) >= 0)) return false; }
        const S = session.status;
        if (S.ready && (x.moved || gg.absent(x))) return false;
        if (S.learn && !canLearn(x)) return false;
        if (S.nooffice && (officeTier(x) > 0 || gg.isRuler(x))) return false;
        if (S.lowloy && (gg.isRuler(x) || x.loyalty >= 60)) return false;
        if (S.away && !(x.away && x.away.until > mi)) return false;
        return true;
      }
      function sortKey(x) {
        const k = session.sort;
        if (k === 'level') return levelOf(x).level * 1e6 + (x.exp | 0);
        if (k === 'office') return G().isRuler(x) ? 99 : officeTier(x);
        if (k === 'name') return x.name;
        if (k === 'war' || k === 'intel' || k === 'pol') return effStat(x, k);
        return x[k] | 0;
      }
      function refresh() {
        const gg = G();
        const all = pool();
        // 城的下拉：本页签中出现的城
        const cities = Array.from(new Set(all.map(x => x.city))).filter(i => i >= 0 && gg.cities[i]).sort((a, b) => gg.cities[a].name.localeCompare(gg.cities[b].name, 'zh'));
        if (session.city >= 0 && cities.indexOf(session.city) < 0) session.city = -1;
        st.citySel.innerHTML = '<option value="-1">全部城</option>' + cities.map(i => '<option value="' + i + '">' + esc(gg.cities[i].name) + '</option>').join('');
        st.citySel.value = String(session.city);
        const L = all.filter(matches);
        const dir = session.dir;
        L.sort((a, b) => { const ka = sortKey(a), kb = sortKey(b); return (typeof ka === 'string' ? ka.localeCompare(kb, 'zh') : ka - kb) * dir || a.id - b.id; });
        st.list = L;
        for (const c of st.head.querySelectorAll('.is-sort')) { c.classList.toggle('is-on', c.dataset.k === session.sort); c.dataset.dir = session.dir > 0 ? '▲' : '▼'; }
        // 人数一行
        if (session.tab === 'mine') {
          const mine = gg.generalsOf(gg.player);
          const ready = mine.filter(x => !x.moved && !gg.absent(x)).length;
          const learn = mine.filter(canLearn).length;
          const noOff = mine.filter(x => !gg.isRuler(x) && officeTier(x) === 0).length;
          win.setInfo('麾下 <b>' + mine.length + '</b> 人 · 本月可行动 ' + ready + ' · 可习得 ' + learn + (SG.Titles && SG.Titles.enabled !== false ? ' · 无官 ' + noOff : ''));
        } else win.setInfo((session.tab === 'other' ? '他国武将' : '已寻访到的在野人才') + ' ' + L.length + ' 人 · 只能查看');
        if (!st.sel || L.indexOf(st.sel) < 0) st.sel = L[0] || null;
        st.scroll.classList.toggle('is-cards', session.view === 'cards' && !short);
        st.head.style.display = session.view === 'cards' && !short ? 'none' : '';
        st.inner.innerHTML = '';
        st.drawn = null;
        if (!L.length) h('div', 'sg-empty', icon('generals', 40) + '<div>' + (session.tab === 'free' ? '还没有寻访到在野人才——用「搜索」。' : '没有符合条件的武将') + '</div>', st.inner);
        st.inner.style.height = session.view === 'cards' && !short ? '' : L.length > 40 ? (L.length * ROW_H) + 'px' : '';
        drawRows(true);
        renderSide();
        renderBatch();
      }
      st.refresh = refresh;
      // 行：超过 40 行时只画可见部分（上下各多画 6 行）
      function drawRows(force) {
        const L = st.list;
        if (!L.length) return;
        if (session.view === 'cards' && !short) {
          if (!force && st.drawn) return;
          st.drawn = true;
          const grid = h('div', 'sg-gcards', null, st.inner);
          for (const x of L) grid.appendChild(cardEl(x));
          return;
        }
        let a = 0, b = L.length;
        if (L.length > 40) {
          const top = st.scroll.scrollTop, vh = st.scroll.clientHeight || 600;
          a = Math.max(0, Math.floor(top / ROW_H) - 6); b = Math.min(L.length, Math.ceil((top + vh) / ROW_H) + 6);
          if (!force && st.drawn && st.drawn[0] === a && st.drawn[1] === b) return;
          st.inner.innerHTML = '';
        } else if (!force && st.drawn) return;
        st.drawn = [a, b];
        for (let i = a; i < b; i++) {
          const r = rowEl(L[i]);
          if (L.length > 40) { r.style.position = 'absolute'; r.style.top = (i * ROW_H) + 'px'; r.style.left = '0'; r.style.right = '0'; }
          st.inner.appendChild(r);
        }
      }
      function rowEl(x) {
        const gg = G(), c = gg.cities[x.city];
        const r = document.createElement('div');
        r.className = 'sg-genrow2' + (x === st.sel ? ' is-sel' : '') + (st.multi.has(x) ? ' is-multi' : '') + (x.moved ? ' is-moved' : '');
        r.tabIndex = -1;
        r.dataset.id = x.id;
        const lv = levelOf(x);
        const isR = gg.isRuler(x), gov = c && c.governor === x.id;
        let off = '';
        try { off = SG.TitleUI && SG.TitleUI.officeTag ? SG.TitleUI.officeTag(x) || '' : ''; } catch (e) { off = ''; }
        const tags = (isR ? '<span class="sg-tag sg-tag-ruler">君</span>' : '') + (gov && !isR ? '<span class="sg-tag sg-tag-gov">守</span>' : '') + (canLearn(x) && x.faction === gg.player ? '<span class="sg-newtag">新</span>' : '');
        const stat = k => {
          const b = x[k] | 0, e = effStat(x, k), d = e - b;
          const col = k === 'war' ? 'var(--war)' : k === 'intel' ? 'var(--int)' : 'var(--pol)';
          return '<span class="sg-gl-st"><b class="sg-num">' + e + (d ? '<sup>' + (d > 0 ? '+' : '') + d + '</sup>' : '') + '</b>' + UI().brushBar(Math.min(1, b / 110), col, { gear: d > 0 ? Math.min(1, d / 110) : 0 }) + '</span>';
        };
        const away = gg.absent(x) ? '<small class="sg-muted">外出</small>' : gov ? '<small class="sg-muted">太守</small>' : '';
        let gear = '';
        if (!short) {
          if (gearOn()) {
            const known = x.faction === gg.player || x.faction >= 0;
            for (const sl of SG.Items.SLOTS) gear += known ? UI().slot(SG.Gear.item(x, sl), { size: 26, slot: sl }) : UI().slot(null, { size: 26 });
          } else gear = '<span class="sg-muted">—</span>';
        }
        const loy = isR ? 100 : x.loyalty;
        r.innerHTML = '<span class="sg-gl-face">' + UI().frame(x, { size: 42 }) + (x.moved ? '<i class="sg-stamp-moved">已行动</i>' : '') + '</span>' +
          '<span class="sg-gl-name"><b>' + esc(x.name) + '</b>' + tags + '<small>' + (off || (isR ? '君主' : '<span class="sg-muted">—</span>')) + '</small></span>' +
          (short ? '' : '<span class="sg-gl-lv"><b>Lv ' + lv.level + '</b>' + UI().brushBar((lv.pct || 0) / 100, 'var(--gold)', { cls: 'is-exp' }) + '</span>') +
          '<span class="sg-gl-stats">' + stat('war') + stat('intel') + stat('pol') + '</span>' +
          '<span class="sg-gl-tr"><b class="sg-num">' + n0(x.troops) + '</b><small>训 ' + (x.training | 0) + '</small></span>' +
          '<span class="sg-gl-loy' + (!isR && loy < 60 ? ' is-low' : '') + '">' + icon('loyalty', 16) + '<b class="sg-num">' + (x.faction >= 0 ? loy : '—') + '</b></span>' +
          (short ? '' : '<span class="sg-gl-city"><b>' + esc(c ? c.name : '—') + '</b>' + away + '</span><span class="sg-gl-gear">' + gear + '</span>');
        r.addEventListener('click', e => {
          if (short && !(e.shiftKey || e.ctrlKey || e.metaKey) && !st.multi.size) { openChar(x); return; }
          if (e.shiftKey || e.ctrlKey || e.metaKey || st.multi.size) { toggleMulti(x); return; }
          select(x);
        });
        r.addEventListener('dblclick', () => openChar(x));
        if (UI().longPress) UI().longPress(r, () => toggleMulti(x));
        return r;
      }
      function cardEl(x) {
        const gg = G(), lv = levelOf(x);
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'sg-gcard' + (x === st.sel ? ' is-sel' : '') + (x.moved ? ' is-moved' : '');
        let off = '';
        try { off = SG.TitleUI && SG.TitleUI.officeTag ? SG.TitleUI.officeTag(x) || '' : ''; } catch (e) { off = ''; }
        const pip = (k, col) => '<i style="--v:' + Math.min(1, effStat(x, k) / 110).toFixed(3) + ';--c:' + col + '"></i>';
        b.innerHTML = UI().frame(x, { size: 96 }) + '<span class="sg-lvseal is-small"><b>' + lv.level + '</b></span><b class="sg-gcard-n">' + esc(x.name) + '</b><small>' + (off || (gg.isRuler(x) ? '君主' : '—')) + '</small>' +
          '<span class="sg-gcard-pips">' + pip('war', 'var(--war)') + pip('intel', 'var(--int)') + pip('pol', 'var(--pol)') + '</span>';
        b.addEventListener('click', () => select(x));
        b.addEventListener('dblclick', () => openChar(x));
        return b;
      }
      function select(x, scroll) {
        if (st.sel === x) return;
        st.sel = x;
        UI().sfx('ui-press-paper', 0.6);
        for (const r of st.inner.querySelectorAll('.is-sel')) r.classList.remove('is-sel');
        const r = st.inner.querySelector('[data-id="' + x.id + '"]');
        if (r) r.classList.add('is-sel');
        if (scroll) {
          const i = st.list.indexOf(x), top = i * ROW_H;
          if (top < st.scroll.scrollTop) st.scroll.scrollTop = top;
          else if (top + ROW_H > st.scroll.scrollTop + st.scroll.clientHeight) st.scroll.scrollTop = top + ROW_H - st.scroll.clientHeight;
          drawRows();
          const r2 = st.inner.querySelector('[data-id="' + x.id + '"]');
          if (r2) r2.classList.add('is-sel');
        }
        renderSide();
      }
      function toggleMulti(x) {
        const gg = G();
        if (x.faction !== gg.player) return;
        const first = st.multi.values().next().value;
        if (first && first.city !== x.city) { UI().deny(null, '批量只限同一座城的武将。'); return; }
        if (st.multi.has(x)) st.multi.delete(x); else st.multi.add(x);
        UI().sfx('ui-press-paper', 0.7);
        for (const r of st.inner.querySelectorAll('.sg-genrow2')) r.classList.toggle('is-multi', st.multi.has(gg.generals[+r.dataset.id]));
        renderBatch();
      }
      function openChar(x) {
        if (!SG.CharacterScreen) return;
        UI().sfx('ui-paper-open', 0.6);
        SG.CharacterScreen.open(x, { list: st.list.slice() }).then(() => { if (!st.win.closed) refresh(); });
      }
      st.openChar = openChar;
      function renderSide() {
        if (!win.side) return;
        const x = st.sel, gg = G();
        if (!x) { st.sideInfo.innerHTML = ''; st.sideActs.innerHTML = ''; return; }
        if (!st.stage) st.stage = SG.CharStage ? SG.CharStage.create(st.sideStage, x, { poses: false, controls: false, compact: true }) : null;
        else st.stage.setGen(x);
        const c = gg.cities[x.city];
        let off = '';
        try { off = SG.TitleUI && SG.TitleUI.officeTag ? SG.TitleUI.officeTag(x) || '' : ''; } catch (e) { off = ''; }
        const W = (SG.WorldInfo || {})[x.name];
        const parts = [];
        try { const w = gearOn() ? SG.Gear.item(x, 'weapon') : null; if (w) parts.push(esc(SG.Items.displayName ? SG.Items.displayName(w) : w.name)); } catch (e) { /* 忽略 */ }
        try { const sp = SG.Specials && SG.Specials.known ? SG.Specials.known(x) : []; parts.push('必杀 ' + sp.length); } catch (e) { /* 忽略 */ }
        try { const tc = SG.Tactics && SG.Tactics.known ? SG.Tactics.known(x) : []; parts.push('计策 ' + tc.length); } catch (e) { /* 忽略 */ }
        try { if (SG.Combos) { const m = SG.Combos.mastery(gg, x.name); parts.push('连段 ' + ('★'.repeat(m.s) || '☆')); } } catch (e) { /* 忽略 */ }
        st.sideInfo.innerHTML = '<div class="sg-gens-name"><b>' + esc(x.name) + '</b><small>' + (W && W.full && W.full !== x.name ? esc(W.full) + ' · ' : '') + esc(c ? c.name : '') + (x.born > 0 ? ' · 生年 ' + x.born : '') + '</small></div>' +
          '<div class="sg-gens-off">' + (off || (gg.isRuler(x) ? '君主' : '<span class="sg-muted">无官职</span>')) + '</div>' +
          '<div class="sg-gens-sum">' + parts.join(' · ') + '</div>' +
          '<div class="sg-gens-bars">' + [['war', '武', 'var(--war)'], ['intel', '智', 'var(--int)'], ['pol', '政', 'var(--pol)']].map(([k, zh, col]) => {
            const b = x[k] | 0, e = effStat(x, k), d = e - b;
            return '<span>' + zh + '</span><b class="sg-num">' + e + (d ? '<sup>+' + d + '</sup>' : '') + '</b>' + UI().brushBar(Math.min(1, b / 110), col, { gear: d > 0 ? Math.min(1, d / 110) : 0 });
          }).join('') + '</div>' +
          '<div class="sg-gens-sum">兵 ' + n0(x.troops) + ' · 训 ' + (x.training | 0) + (x.faction >= 0 ? ' · 忠 ' + (gg.isRuler(x) ? 100 : x.loyalty) : '') + (x.moved ? ' · <span class="sg-gens-moved">本月已行动</span>' : '') + '</div>';
        const A = st.sideActs;
        A.innerHTML = '';
        UI().button(A, '详　情', () => openChar(x), { kind: 'seal', icon: 'dossier' });
        const mine = x.faction === gg.player;
        const ss = SG.Game && SG.Game.strategy;
        const busy = !ss || !ss.hud ? '战斗中只能查看。' : ss.busy || ss.endMonth ? '指令执行中，请稍候。' : null;
        const run = async fn => { try { if (ss && ss._wrap) await ss._wrap(fn); else await fn(); } catch (e) { console.error(e); } if (!st.win.closed) refresh(); };
        if (mine) {
          const isR = gg.isRuler(x);
          UI().button(A, '赏　赐', () => run(() => ss.cmdReward(c, x)), { kind: 'plaque', icon: 'reward', why: busy || (isR ? '君主无需赏赐。' : x.loyalty >= 100 ? '忠诚已满。' : null) });
          UI().button(A, '装　备', () => SG.CharacterScreen.open(x, { list: st.list.slice(), tab: 'gear' }).then(() => { if (!st.win.closed) refresh(); }), { kind: 'plaque', icon: 'armour', why: gearOn() ? null : '装备系统未开启。' });
          UI().button(A, '移　动', () => run(() => ss.cmdMove(c, x)), { kind: 'plaque', icon: 'move', why: busy || (x.moved ? '本月已行动。' : gg.tokens > 0 ? null : '本月令牌已用完。') });
        }
      }
      function renderBatch() {
        const B = st.batch;
        B.innerHTML = '';
        st.footNote.style.display = st.multi.size ? 'none' : '';
        B.style.display = st.multi.size ? '' : 'none';
        if (!st.multi.size) return;
        const gg = G(), list = Array.from(st.multi);
        const c = gg.cities[list[0].city];
        const Bal = SG.Balance;
        const ss = SG.Game && SG.Game.strategy;
        const busy = !ss || !ss.hud || ss.busy || ss.endMonth ? '指令执行中，请稍候。' : null;
        h('span', 'sg-gens-bcount', '已选 <b>' + list.length + '</b> 人 · ' + esc(c ? c.name : ''), B);
        const run = async fn => { try { if (ss && ss._wrap) await ss._wrap(fn); else await fn(); } catch (e) { console.error(e); } st.multi.clear(); if (!st.win.closed) refresh(); };
        const rw = list.filter(x => !gg.isRuler(x) && x.loyalty < 100);
        UI().button(B, '赏赐（各 ' + (Bal ? Bal.RewardGold : '') + ' 金）', () => run(async () => {
          const ok = await UI().confirm('赏赐 ' + rw.length + ' 人，共 ' + (rw.length * (Bal ? Bal.RewardGold : 0)) + ' 金？', '赏　赐', '取消');
          if (!ok) return;
          let n = 0;
          for (const x of rw) { if (c.gold < Bal.RewardGold) break; SG.Commands.reward(c, x); n++; }
          UI().sfx('ui-coins');
          UI().toast('赏赐 ' + n + ' 人' + (n < rw.length ? '（金不足，余者未赏）' : ''), 2.2);
        }), { kind: 'plaque', icon: 'reward', why: busy || (rw.length ? null : '选中的都是君主或忠诚已满。') });
        const mv = list.filter(x => !x.moved);
        UI().button(B, '移动（1 令）', () => run(async () => {
          const C = SG.Commands;
          const bl = C.moveBlocked(c);
          if (bl) { await UI().say(bl); return; }
          const rts = C.moveTargets(c);
          const k = await UI().choose('调动 ' + mv.length + ' 人移往何处？', rts.map(rt => UI().item(esc(rt.city.name), '武将 ' + gg.officersIn(rt.city).length, true)), '经由己方城池相连即可前往（1 枚令牌）');
          if (k < 0) return;
          ss.useToken();
          for (const x of mv) C.move(x, rts[k].city);
          UI().toast(mv.length + ' 名武将移驻' + esc(rts[k].city.name), 2.4);
        }), { kind: 'plaque', icon: 'move', why: busy || (!mv.length ? '选中的武将本月都已行动。' : gg.tokens > 0 ? null : '本月令牌已用完。') });
        UI().button(B, '清除选择', () => { st.multi.clear(); refresh(); }, { kind: 'ghost' });
      }
      refresh();
      try { if (SG.Voice) { const r = g.ruler(g.player); SG.Voice.say(r, 'ui.generalsOpen', { pri: 1, scene: 'strategy' }); } } catch (e) { /* 无人声 */ }
      return promise;
    },
  };
  SG.GeneralsScreen = GeneralsScreen;
})();
