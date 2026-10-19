'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 设置画面（UI-V4 §7）
   漆框窗口（≤ 980 × 610），左侧六枚竹简页签（音频 · 画面 · 游戏 · 镜头 · 操作 · 辅助），右侧纸页按登记表（SG.Settings.rows(tab)）
   逐行绘出：分段木牌 / 玉珠滑杆 / 印章开关 / 按钮。改动即时生效、即时保存；滑杆松开时试听（音乐 / 音效 / 语音）。
   页脚：「设置存于本机，不随存档」·「恢复本页默认」（空框，问一次）·「完成」（印钮）。标题画面同一窗口。

   SG.SettingsScreen.open(tab?) → Promise（关闭时兑现）     SG.SettingsScreen.isOpen()
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});
  const esc = s => SG.esc(String(s === undefined || s === null ? '' : s));
  const UI = () => SG.UI;
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html !== undefined && html !== null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  const TAB_ICON = { audio: 'music', video: 'eye', game: 'play', camera: 'camera', controls: 'hand', access: 'access' };
  const PREVIEW = {
    'audio.music': () => { try { const g = SG.G; if (SG.Sfx.fanfare) SG.Sfx.fanfare(g && g.ruler ? g.ruler(g.player) : null, { short: true }); else SG.Sfx.play('ui-month'); } catch (e) { /* 忽略 */ } },
    'audio.sfx': () => UI().sfx('ui-coins'),
    'audio.voice': () => {
      try {
        const g = SG.G;
        const who = g && g.ruler && g.player >= 0 ? g.ruler(g.player) : 'herald';
        if (SG.Voice) SG.Voice.say(who || 'herald', 'ui.generalOpen', { pri: 3, scene: 'strategy', force: true });
      } catch (e) { /* 忽略 */ }
    },
    'audio.master': () => UI().sfx('ui-bead'),
  };

  let cur = null;
  const SettingsScreen = {
    isOpen() { return !!(cur && !cur.closed); },
    open(tab) {
      if (cur && !cur.closed) { if (tab && cur.tabs) cur.tabs.set(tab); return cur.promise; }
      const R = SG.Settings;
      if (!R) return Promise.resolve();
      try { if (SG.UISettings) SG.UISettings.bridge(); } catch (e) { /* 忽略 */ }
      const tabs = R.TABS.filter(t => R.rows(t).length);
      let resolveP;
      const promise = new Promise(r => { resolveP = r; });
      const win = UI().window('settings', { title: '设　置', seal: '设', info: '立即生效 · 自动保存', width: 980, height: 610, side: true, foot: true, className: 'sg-settings',
        onClose: () => { cur = null; resolveP(); } });
      const st = cur = win;
      st.promise = promise;
      const tabApi = st.tabs = UI().tabs(win.side, tabs.map(t => ({ id: t, icon: TAB_ICON[t], label: R.TAB_ZH[t] || t })), id => render(id), { vertical: true, value: tabs.indexOf(tab) >= 0 ? tab : tabs[0], className: 'sg-set-tabs' });
      h('div', 'sg-note', '设置存于本机（sanguozhi2_ui_v4 等），不随存档', win.foot);
      h('div', 'sg-spacer', null, win.foot);
      UI().button(win.foot, '恢复本页默认', async () => {
        const t = tabApi.get();
        const ok = await UI().confirm('恢复「' + (R.TAB_ZH[t] || t) + '」的默认设置？', '恢复默认', '取消');
        if (!ok) return;
        R.reset(t);
        render(t);
        UI().toast('已恢复「' + esc(R.TAB_ZH[t] || t) + '」的默认设置', { seconds: 1.8, seal: '设' });
      }, { kind: 'ghost' });
      UI().button(win.foot, '完　成', () => win.close(), { kind: 'seal' });
      function render(t) {
        const page = win.page;
        page.innerHTML = '';
        page.scrollTop = 0;
        const hd = h('div', 'sg-set-h', UI().icon(TAB_ICON[t], 28) + '<span class="sg-h2">' + esc(R.TAB_ZH[t] || t) + '</span>', page);
        hd.dataset.tab = t;
        const rows = R.rows(t);
        if (!rows.length) h('div', 'sg-empty', '这一页没有可调的项目', page);
        for (const d of rows) SettingsScreen._row(page, d);
        if (t === 'access' && SG.isTouch !== undefined) h('div', 'sg-note sg-set-foot', '减少动态「跟随系统」= 依操作系统的「减少动态效果」设置。', page);
      }
      render(tabApi.get());
      return promise;
    },
    _row(page, d) {
      const R = SG.Settings;
      const row = h('div', 'sg-set-row t-' + d.type, null, page);
      row.dataset.id = d.id;
      h('div', 'sg-set-lbl', '<b>' + esc(d.label) + '</b>' + (d.sub && d.sub !== '试听' ? '<small>' + esc(d.sub) + '</small>' : ''), row);
      const ctl = h('div', 'sg-set-ctl', null, row);
      const v = R.get(d.id);
      if (d.type === 'seg') {
        UI().segmented(ctl, d.options || [], v, nv => { R.set(d.id, nv); flash(row); });
      } else if (d.type === 'toggle') {
        UI().toggle(ctl, v, nv => { R.set(d.id, nv); flash(row); }, { label: d.label });
      } else if (d.type === 'slider') {
        const pv = PREVIEW[d.id];
        UI().slider(ctl, { min: d.min, max: d.max, step: d.step, value: +v, format: d.format, label: d.label,
          onCommit: nv => { R.set(d.id, nv); }, preview: pv ? () => pv() : null });
        if (pv && d.id !== 'audio.master') UI().button(ctl, '试听', () => pv(), { kind: 'paper', small: true, className: 'sg-set-try' });
      } else if (d.type === 'button') {
        // 按钮行：动作在 d.action
      }
      if (d.action && d.action.label) UI().button(ctl, esc(d.action.label), () => { try { d.action.run(); } catch (e) { console.error(e); } }, { kind: 'paper', small: true });
      return row;
    },
  };
  function flash(row) { row.classList.remove('is-flash'); void row.offsetWidth; row.classList.add('is-flash'); }
  SG.SettingsScreen = SettingsScreen;
})();
