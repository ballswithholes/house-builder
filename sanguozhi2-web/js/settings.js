'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 设置（UI-V4 §7；INTEGRATION §10.1）
   SG.Settings 登记表本身在 js/registry.js（W0）；本文件：
     1. 设置的总表 SG.UISettings.TABLE —— UI-V4 §7 的 53 行（加 ITEMS 的「阵斩」）：行号、页、编号、拥有者、存储键。
        每行的默认值只写在这里（界面的行）或拥有者模块里（音频、单挑……）；玩家要改的默认值都在 DEFAULTS 一处。
     2. 登记界面自己的行（存于 localStorage 'sanguozhi2_ui_v4'），带即时生效的 apply；
     3. 「桥接行」：拥有者模块已载入、却没有自己登记的行（攻击画面 = SG.Clash.mode、单挑键位 = sanguozhi2_duel_v4……），
        由本文件按拥有者的存储键代为登记，界面照样能改；拥有者没载入的行不显示（UI-V4 §7：登记表即契约）。
   纯逻辑：载入时不碰 DOM（Node 测试 tests/settings.js 直接载入）；apply 里的 DOM 操作都有 typeof document 守卫。

   SG.UISettings.KEY  DEFAULTS  TABLE  TABS  TAB_ZH
   SG.UISettings.define()          登记界面的行（幂等；载入时自动执行）
   SG.UISettings.bridge()          补登已载入、未自登记的拥有者行（game.js 启动时、设置画面打开时调用；幂等）
   SG.UISettings.applyAll()        把当前值施加一遍（启动时）
   SG.UISettings.get(id) / set(id, v)   = SG.Settings.get / set（未登记时回到 DEFAULTS）
   SG.UISettings.textCps() → 每秒字数（Infinity = 即时）   longPressMs()   reducedMotion() → bool   motionLevel() → 'full'|'lite'|'off'
   SG.UISettings.rowCount() → { defined, visible, table }   tests 用
   ========================================================================== */
(function () {
  const W = typeof window !== 'undefined' ? window : globalThis;
  const SG = (W.SG = W.SG || {});
  const KEY = 'sanguozhi2_ui_v4';
  // 规则层不直接写「document.」（tests/load-order.js）：经 root() 取 <html>，Node 下为 null
  const root = () => (typeof document !== 'undefined' && document ? document['documentElement'] : null);
  const hasDoc = () => !!root();
  const isTouch = () => !!SG.isTouch;

  // ---------------------------------------------------------------- 界面行的默认值（一处）--
  const DEFAULTS = {
    'ui.sfx': true,            // 5  界面音效
    'ui.muteHidden': true,     // 10 后台静音
    'ui.pixelRatio': 'auto',   // 12 渲染精度
    'ui.fps': '60',            // 13 帧率上限
    'ui.daynight': true,       // 14 昼夜光影
    'ui.motion': 'full',       // 15 界面动效
    'ui.particles': true,      // 16 季节粒子
    'ui.labels': 'all',        // 17 地图城名
    'ui.ceremony': 'full',     // 20 仪式动画（TITLES / ITEMS / 武林大会读）
    'ui.aiSpeed': 'fast',      // 21 电脑回合速度（battle-controller 读）
    'ui.duelWatch': 'highlights', // 22 单挑观战（单挑 / 武林大会读）
    'ui.textSpeed': 'fast',    // 23 文字速度
    'ui.autoAdvance': false,   // 24 对话自动推进
    'ui.autoSave': true,       // 25 月初自动记录
    'ui.endConfirm': 'ask',    // 26 结束本月确认
    'ui.tips': true,           // 27 提示与引导
    'cam.zoomSpeed': 100,      // 28 缩放速度 %
    'cam.panSpeed': 100,       // 29 平移速度 %
    'cam.invert': false,       // 30 拖动方向反转
    'cam.edgePan': false,      // 31 屏幕边缘平移（桌面）
    'cam.fly': 'smooth',       // 32 镜头飞行
    'cam.idleOrbit': true,     // 33 空闲环绕
    'cam.shake': 'normal',     // 34 画面震动
    'ui.touchSize': 'std',     // 39 触控按钮尺寸
    'ui.longPress': '0.45',    // 40 长按时长
    'ui.hand': 'right',        // 41 惯用手
    'ui.textScale': '100',     // 43 界面字号
    'ui.reduceMotion': 'system', // 44 减少动态
    'ui.contrast': false,      // 45 高对比度
    'ui.colorAid': 'off',      // 46 色觉辅助
    'ui.flash': 'normal',      // 47 闪光
    'ui.duelSpeed': '100',     // 48 单挑速度（duel-game 读；武林大会亲自出战不受影响）
    'ui.subPlate': 'clear',    // 50 字幕底板
    // 拥有者的行：拥有者模块没登记时由本文件桥接（默认值与拥有者相同）
    'fx.quality': 'auto',      // 11 画质（CITY-VIEW / DOMESTIC-FX 的键 sanguozhi2_fx_v4.quality）
    'fx.domestic': 'smart',    // 19 内政演出（DOMESTIC-FX）
    'engage.delegate': 'minor',// 51 内政委任（ENGAGEMENT）
    'tourney.entry': 'ask',    // 53 武林大会出战方式（TOURNAMENT）
  };

  const TABS = ['audio', 'video', 'game', 'camera', 'controls', 'access'];
  const TAB_ZH = { audio: '音频', video: '画面', game: '游戏', camera: '镜头', controls: '操作', access: '辅助' };
  const TAB_ICON = { audio: 'music', video: 'eye', game: 'play', camera: 'camera', controls: 'hand', access: 'access' };

  // ---------------------------------------------------------------- 总表（UI-V4 §7 + INTEGRATION §10.1）--
  // owner：'ui' 本文件登记；其余为拥有者（'audio' 'clash' 'duel' 'fx' 'domestic' 'engage' 'tourney' 'items'），id 为拥有者用的编号
  const TABLE = [
    [1, 'audio', 'audio.master', '主音量', 'audio'], [2, 'audio', 'audio.music', '音乐', 'audio'], [3, 'audio', 'audio.sfx', '音效', 'audio'],
    [4, 'audio', 'audio.voice', '语音', 'audio'], [5, 'audio', 'ui.sfx', '界面音效', 'ui'], [6, 'audio', 'audio.musicStyle', '音乐风格', 'audio'],
    [7, 'audio', 'audio.voiceLang', '语音语言', 'audio'], [8, 'audio', 'audio.voiceFreq', '语音频率', 'audio'], [9, 'audio', 'audio.subtitles', '字幕', 'audio'],
    [10, 'audio', 'ui.muteHidden', '后台静音', 'ui'],
    [11, 'video', 'fx.quality', '画质', 'fx'], [12, 'video', 'ui.pixelRatio', '渲染精度', 'ui'], [13, 'video', 'ui.fps', '帧率上限', 'ui'],
    [14, 'video', 'ui.daynight', '昼夜光影', 'ui'], [15, 'video', 'ui.motion', '界面动效', 'ui'], [16, 'video', 'ui.particles', '季节粒子', 'ui'],
    [17, 'video', 'ui.labels', '地图城名', 'ui'],
    [18, 'game', 'clash.mode', '攻击画面', 'clash'], [19, 'game', 'fx.domestic', '内政演出', 'domestic'], [20, 'game', 'ui.ceremony', '仪式动画', 'ui'],
    [21, 'game', 'ui.aiSpeed', '电脑回合速度', 'ui'], [22, 'game', 'ui.duelWatch', '单挑观战', 'ui'], [23, 'game', 'ui.textSpeed', '文字速度', 'ui'],
    [24, 'game', 'ui.autoAdvance', '对话自动推进', 'ui'], [25, 'game', 'ui.autoSave', '月初自动记录', 'ui'], [26, 'game', 'ui.endConfirm', '结束本月确认', 'ui'],
    [27, 'game', 'ui.tips', '提示与引导', 'ui'],
    [28, 'camera', 'cam.zoomSpeed', '缩放速度', 'ui'], [29, 'camera', 'cam.panSpeed', '平移速度', 'ui'], [30, 'camera', 'cam.invert', '拖动方向', 'ui'],
    [31, 'camera', 'cam.edgePan', '屏幕边缘平移', 'ui'], [32, 'camera', 'cam.fly', '镜头飞行', 'ui'], [33, 'camera', 'cam.idleOrbit', '空闲环绕', 'ui'],
    [34, 'camera', 'cam.shake', '画面震动', 'ui'],
    [35, 'controls', 'duel.keys', '单挑键位', 'duel'], [36, 'controls', 'duel.guardBtn', '格挡键', 'duel'], [37, 'controls', 'duel.hints', '招式提示', 'duel'],
    [38, 'controls', 'duel.ribbon', '招式谱条', 'duel'], [39, 'controls', 'ui.touchSize', '触控按钮尺寸', 'ui'], [40, 'controls', 'ui.longPress', '长按时长', 'ui'],
    [41, 'controls', 'ui.hand', '惯用手', 'ui'], [42, 'controls', 'ui.keymap', '快捷键一览', 'ui'],
    [43, 'access', 'ui.textScale', '界面字号', 'ui'], [44, 'access', 'ui.reduceMotion', '减少动态', 'ui'], [45, 'access', 'ui.contrast', '高对比度', 'ui'],
    [46, 'access', 'ui.colorAid', '色觉辅助', 'ui'], [47, 'access', 'ui.flash', '闪光', 'ui'], [48, 'access', 'ui.duelSpeed', '单挑速度', 'ui'],
    [49, 'access', 'audio.readAloud', '朗读', 'audio'], [50, 'access', 'ui.subPlate', '字幕底板', 'ui'],
    [51, 'game', 'engage.delegate', '内政委任', 'engage'], [52, 'game', 'engage.festivalMemory', '节庆照此办理', 'engage'], [53, 'game', 'tourney.entry', '武林大会', 'tourney'],
  ].map(([row, tab, id, label, owner]) => ({ row, tab, id, label, owner }));

  // 拥有者模块是否载入（未载入：该行不显示）
  const OWNER_LOADED = {
    ui: () => true,
    audio: () => !!SG.Sfx,
    fx: () => !!(SG.Gfx || SG.DomesticFx || SG.CityView),
    clash: () => !!SG.Clash,
    duel: () => !!SG.DuelGame,
    domestic: () => !!SG.DomesticFx,
    engage: () => !!(SG.Engage && SG.Engage.enabled !== false),
    tourney: () => !!(SG.Tourney && SG.Tourney.enabled !== false),
    items: () => !!(SG.Items && SG.Items.enabled !== false),
  };

  // ---------------------------------------------------------------- apply（DOM 守卫）--
  function htmlClass(cls, on) { const r = root(); if (r) r.classList.toggle(cls, !!on); }
  function htmlVar(name, v) { const r = root(); if (r) r.style.setProperty(name, v); }
  let mqReduce = null;
  function sysReduced() {
    try { if (!mqReduce && typeof matchMedia === 'function') mqReduce = matchMedia('(prefers-reduced-motion: reduce)'); return !!(mqReduce && mqReduce.matches); } catch (e) { return false; }
  }
  function applyMotion() {
    const lvl = get('ui.motion'), rm = get('ui.reduceMotion');
    const reduced = rm === 'on' || (rm === 'system' && sysReduced());
    htmlClass('sg-reduced', reduced);
    htmlClass('sg-motion-lite', lvl === 'lite');
    htmlClass('sg-motion-off', lvl === 'off' || reduced);
    if (SG.Motion) { SG.Motion.reduced = reduced; SG.Motion.level = reduced ? 'off' : lvl; }
  }
  function applyPixelRatio() {
    const Gfx = SG.Gfx;
    if (!Gfx || !Gfx.renderer || typeof W.devicePixelRatio === 'undefined') return;
    const v = get('ui.pixelRatio'), dpr = W.devicePixelRatio || 1;
    const q = get('fx.quality');
    let cap = v === 'auto' ? (q === 'low' ? 1 : SG.isTouch ? 1.5 : 2) : Math.max(0.5, dpr * (+v / 100));
    if (v !== 'auto') cap = Math.min(cap, 2);
    Gfx.maxPixelRatio = cap;
    try { if (typeof Gfx.resize === 'function') Gfx.resize(); } catch (e) { /* 忽略 */ }
  }
  function applyQuality() {
    const Gfx = SG.Gfx;
    if (!Gfx || !Gfx.renderer) return;
    const q = get('fx.quality');
    try {
      Gfx.renderer.shadowMap.enabled = q !== 'low';
      if (Gfx.sun && Gfx.sun.shadow && q === 'mid' && Gfx.sun.shadow.mapSize.x > 1024) {
        Gfx.sun.shadow.mapSize.set(1024, 1024);
        if (Gfx.sun.shadow.map) { Gfx.sun.shadow.map.dispose(); Gfx.sun.shadow.map = null; }
      }
    } catch (e) { /* 忽略 */ }
    applyPixelRatio();
  }
  const APPLY = {
    'ui.sfx': v => { if (SG.Sfx) SG.Sfx.uiOn = !!v; },
    'ui.pixelRatio': applyPixelRatio,
    'fx.quality': applyQuality,
    'ui.daynight': v => { htmlClass('sg-no-daynight', !v); },
    'ui.motion': applyMotion,
    'ui.reduceMotion': applyMotion,
    'ui.particles': v => htmlClass('sg-no-particles', !v),
    'ui.contrast': v => htmlClass('sg-hc', v),
    'ui.colorAid': v => { htmlClass('sg-cvd-rg', v === 'rg'); htmlClass('sg-cvd-by', v === 'by'); },
    'ui.flash': v => htmlClass('sg-noflash', v === 'none'),
    'ui.subPlate': v => htmlClass('sg-sub-solid', v === 'solid'),
    'ui.touchSize': v => htmlClass('sg-touch-big', v === 'big'),
    'ui.hand': v => htmlClass('sg-lefty', v === 'left'),
    'ui.textScale': v => { htmlVar('--sg-text-k', String((+v || 100) / 100)); try { if (SG.UI && SG.UI.rescale) SG.UI.rescale(); } catch (e) { /* 忽略 */ } },
    'ui.labels': () => { try { const s = SG.Game && SG.Game.strategy; if (s && s.refreshAll && s.hud) s.refreshAll(); } catch (e) { /* 忽略 */ } },
  };

  // ---------------------------------------------------------------- 界面行的定义 --
  const seg = opts => opts.map(o => (Array.isArray(o) ? o : [o, o]));
  const UI_ROWS = [
    { id: 'ui.sfx', tab: 'audio', row: 5, label: '界面音效', sub: '按钮、页签、计数器的音效', type: 'toggle' },
    { id: 'ui.muteHidden', tab: 'audio', row: 10, label: '后台静音', sub: '切到别的窗口时静音', type: 'toggle' },
    { id: 'ui.pixelRatio', tab: 'video', row: 12, label: '渲染精度', sub: '像素比上限：越低越省电', type: 'seg', options: seg([['auto', '自动'], ['100', '100%'], ['75', '75%'], ['50', '50%']]) },
    { id: 'ui.fps', tab: 'video', row: 13, label: '帧率上限', type: 'seg', options: seg([['60', '60'], ['30', '30']]) },
    { id: 'ui.daynight', tab: 'video', row: 14, label: '昼夜光影', sub: '一月之中由晨至夜；夜里挂灯亮起', type: 'toggle' },
    { id: 'ui.motion', tab: 'video', row: 15, label: '界面动效', sub: '精简 = 无环境动画', type: 'seg', options: seg([['full', '完整'], ['lite', '精简'], ['off', '关']]) },
    { id: 'ui.particles', tab: 'video', row: 16, label: '季节粒子', sub: '花瓣、萤火、落叶、飞雪', type: 'toggle' },
    { id: 'ui.labels', tab: 'video', row: 17, label: '地图城名', type: 'seg', options: seg([['all', '全部'], ['near', '我方与邻近'], ['sel', '仅选中']]) },
    { id: 'ui.ceremony', tab: 'game', row: 20, label: '仪式动画', sub: '授官、神兵揭晓、换月、颁奖', type: 'seg', options: seg([['full', '完整'], ['short', '简']]) },
    { id: 'ui.aiSpeed', tab: 'game', row: 21, label: '电脑回合速度', sub: '战场上电脑的行动与委任', type: 'seg', options: seg([['normal', '正常'], ['fast', '快'], ['vfast', '极快']]) },
    { id: 'ui.duelWatch', tab: 'game', row: 22, label: '单挑观战', sub: '电脑之间的单挑（战场、武林大会）', type: 'seg', options: seg([['watch', '观看'], ['highlights', '精彩片段'], ['result', '只看结果']]) },
    { id: 'ui.textSpeed', tab: 'game', row: 23, label: '文字速度', type: 'seg', options: seg([['slow', '慢'], ['mid', '中'], ['fast', '快'], ['instant', '即时']]) },
    { id: 'ui.autoAdvance', tab: 'game', row: 24, label: '对话自动推进', sub: '2.5 秒 + 每字 0.05 秒', type: 'toggle' },
    { id: 'ui.autoSave', tab: 'game', row: 25, label: '月初自动记录', sub: '记于「自动」档', type: 'toggle' },
    { id: 'ui.endConfirm', tab: 'game', row: 26, label: '结束本月确认', type: 'seg', options: seg([['ask', '有令牌时询问'], ['never', '不询问']]) },
    { id: 'ui.tips', tab: 'game', row: 27, label: '提示与引导', type: 'toggle', action: { label: '重置提示', run: resetTips } },
    { id: 'cam.zoomSpeed', tab: 'camera', row: 28, label: '缩放速度', type: 'slider', min: 50, max: 200, step: 10, format: v => Math.round(v) + '%' },
    { id: 'cam.panSpeed', tab: 'camera', row: 29, label: '平移速度', type: 'slider', min: 50, max: 200, step: 10, format: v => Math.round(v) + '%' },
    { id: 'cam.invert', tab: 'camera', row: 30, label: '拖动方向反转', type: 'toggle' },
    { id: 'cam.edgePan', tab: 'camera', row: 31, label: '屏幕边缘平移', sub: '鼠标贴近屏幕边缘时平移地图', type: 'toggle', when: () => !isTouch() },
    { id: 'cam.fly', tab: 'camera', row: 32, label: '镜头飞行', type: 'seg', options: seg([['smooth', '平滑'], ['fast', '快速'], ['cut', '直接切换']]) },
    { id: 'cam.idleOrbit', tab: 'camera', row: 33, label: '空闲环绕', sub: '标题画面、城池视图', type: 'toggle' },
    { id: 'cam.shake', tab: 'camera', row: 34, label: '画面震动', sub: '攻击画面、单挑、盖印', type: 'seg', options: seg([['normal', '正常'], ['low', '减弱'], ['off', '关']]) },
    { id: 'ui.touchSize', tab: 'controls', row: 39, label: '触控按钮尺寸', type: 'seg', options: seg([['std', '标准'], ['big', '大']]), when: isTouch },
    { id: 'ui.longPress', tab: 'controls', row: 40, label: '长按时长', type: 'seg', options: seg([['0.35', '0.35 秒'], ['0.45', '0.45 秒'], ['0.6', '0.6 秒']]), when: isTouch },
    { id: 'ui.hand', tab: 'controls', row: 41, label: '惯用手', sub: '手机：城池面板与结束本月左右互换', type: 'seg', options: seg([['right', '右手'], ['left', '左手']]) },
    { id: 'ui.keymap', tab: 'controls', row: 42, label: '快捷键一览', type: 'button', action: { label: '查看', run: () => { try { if (SG.MenuScreen && SG.MenuScreen.keyCard) SG.MenuScreen.keyCard(); } catch (e) { /* 忽略 */ } } } },
    { id: 'ui.textScale', tab: 'access', row: 43, label: '界面字号', type: 'seg', options: seg([['90', '90%'], ['100', '100%'], ['115', '115%'], ['130', '130%']]) },
    { id: 'ui.reduceMotion', tab: 'access', row: 44, label: '减少动态', type: 'seg', options: seg([['system', '跟随系统'], ['on', '开'], ['off', '关']]) },
    { id: 'ui.contrast', tab: 'access', row: 45, label: '高对比度', sub: '纸面不透明、无纹理、粗墨线、无光晕', type: 'toggle' },
    { id: 'ui.colorAid', tab: 'access', row: 46, label: '色觉辅助', sub: '旗帜与属性条加纹样，增减另加 ▲ ▼', type: 'seg', options: seg([['off', '关'], ['rg', '红绿'], ['by', '蓝黄']]) },
    { id: 'ui.flash', tab: 'access', row: 47, label: '闪光', type: 'seg', options: seg([['normal', '正常'], ['none', '无闪光']]) },
    { id: 'ui.duelSpeed', tab: 'access', row: 48, label: '单挑速度', sub: '整场单挑放慢（武林大会亲自出战时不适用）', type: 'seg', options: seg([['100', '100%'], ['85', '85%'], ['70', '70%']]) },
    { id: 'ui.subPlate', tab: 'access', row: 50, label: '字幕底板', type: 'seg', options: seg([['clear', '透明'], ['solid', '实底']]) },
  ];

  // 拥有者行的桥接定义（拥有者没登记时）
  const BRIDGES = [
    { id: 'fx.quality', owner: 'fx', tab: 'video', row: 11, label: '画质', sub: '阴影与渲染精度（城池视图、内政演出另行细分）', type: 'seg',
      options: seg([['auto', '自动'], ['high', '高'], ['mid', '中'], ['low', '低']]), store: { key: 'sanguozhi2_fx_v4', path: 'quality' } },
    { id: 'clash.mode', owner: 'clash', tab: 'game', row: 18, label: '攻击画面', sub: '军队交锋的动画', type: 'seg', options: seg([['on', '开'], ['fast', '快速'], ['off', '关']]),
      default: () => (SG.Clash ? SG.Clash.mode : 'on'), apply: v => { if (SG.Clash) SG.Clash.mode = v; } },
    { id: 'fx.domestic', owner: 'domestic', tab: 'game', row: 19, label: '内政演出', sub: '指令动画的长短', type: 'seg',
      options: seg([['smart', '智能'], ['full', '完整'], ['highlights', '精彩'], ['fast', '快速'], ['off', '关']]), store: { key: 'sanguozhi2_fx_v4', path: 'domestic' },
      apply: v => { try { if (SG.DomesticFx && SG.DomesticFx.setSetting) SG.DomesticFx.setSetting('domestic', v); } catch (e) { /* 忽略 */ } } },
    { id: 'duel.keys', owner: 'duel', tab: 'controls', row: 35, label: '单挑键位', sub: 'v4：J 轻 · K 中 · L 重；v3 习惯：K 重 · L 中', type: 'seg', options: seg([['v4', 'v4'], ['v3', 'v3习惯']]), store: { key: 'sanguozhi2_duel_v4', path: 'keys' }, defaultV: 'v4' },
    { id: 'duel.guardBtn', owner: 'duel', tab: 'controls', row: 36, label: '格挡键', sub: '另设 O 键 / 「防」按钮（后退即格挡照常）', type: 'toggle', store: { key: 'sanguozhi2_duel_v4', path: 'guardBtn' }, defaultV: false },
    { id: 'duel.hints', owner: 'duel', tab: 'controls', row: 37, label: '招式提示', type: 'toggle', store: { key: 'sanguozhi2_duel_v4', path: 'hints' }, defaultV: true },
    { id: 'duel.ribbon', owner: 'duel', tab: 'controls', row: 38, label: '招式谱条', sub: '自动 = 前 5 场与演武场', type: 'seg', options: seg([['auto', '自动'], ['on', '开'], ['off', '关']]), store: { key: 'sanguozhi2_duel_v4', path: 'ribbon' }, defaultV: 'auto' },
    { id: 'engage.delegate', owner: 'engage', tab: 'game', row: 51, label: '内政委任', sub: '小事委任 = 次要事件按 ★ 自动回答并列入新闻', type: 'seg', options: seg([['minor', '小事委任'], ['ask', '全部询问']]), store: { key: 'sanguozhi2_engage_v4', path: 'delegate' } },
    { id: 'engage.festivalMemory', owner: 'engage', tab: 'game', row: 52, label: '节庆照此办理', type: 'button',
      action: { label: '清除记忆', run: () => { try { const g = SG.G, f = g && g.playerFaction; if (f && f.eng) delete f.eng.festivalAuto; } catch (e) { /* 忽略 */ } } } },
    { id: 'tourney.entry', owner: 'tourney', tab: 'game', row: 53, label: '武林大会', sub: '出战方式', type: 'seg', options: seg([['ask', '每次询问'], ['auto', '自动派遣（推荐五人）'], ['result', '只看结果']]), store: { key: 'sanguozhi2_tourney_v4', path: 'entry' } },
  ];

  function resetTips() {
    try { if (typeof localStorage !== 'undefined') { localStorage.removeItem('sanguozhi2_tip_move'); localStorage.removeItem('sanguozhi2_tips_v4'); } } catch (e) { /* 忽略 */ }
    try { if (SG.UI && SG.UI.toast) SG.UI.toast('提示已重置', 1.6); } catch (e) { /* 忽略 */ }
  }

  function S() { return SG.Settings || null; }
  function get(id) {
    const R = S();
    if (R && R.has(id)) { const v = R.get(id); if (v !== undefined) return v; }
    return DEFAULTS[id];
  }
  function set(id, v) { const R = S(); return R && R.has(id) ? R.set(id, v) : false; }

  let defined = false;
  function define() {
    const R = S();
    if (!R || defined) return defined;
    defined = true;
    for (const r of UI_ROWS) {
      if (R.has(r.id)) continue;
      R.define(Object.assign({ order: r.row, owner: 'ui', default: DEFAULTS[r.id], store: r.type === 'button' ? undefined : { key: KEY, path: r.id }, apply: APPLY[r.id] }, r));
    }
    return true;
  }
  // 补登拥有者的行（拥有者已载入、却没有自己登记时）；返回补登的编号
  function bridge() {
    const R = S();
    if (!R) return [];
    define();
    const out = [];
    for (const b of BRIDGES) {
      if (R.has(b.id)) continue;
      const loaded = OWNER_LOADED[b.owner];
      if (!loaded || !loaded()) continue;
      const d = Object.assign({ order: b.row, default: b.defaultV !== undefined ? b.defaultV : DEFAULTS[b.id], when: () => { const f = OWNER_LOADED[b.owner]; return !f || f(); } }, b);
      if (!d.apply && APPLY[b.id]) d.apply = APPLY[b.id];
      delete d.defaultV;
      R.define(d);
      out.push(b.id);
    }
    return out;
  }
  function applyAll() {
    define();
    for (const id of Object.keys(APPLY)) { try { APPLY[id](get(id)); } catch (e) { /* 忽略 */ } }
    try { if (typeof matchMedia === 'function' && !applyAll._mq) { applyAll._mq = true; const mq = matchMedia('(prefers-reduced-motion: reduce)'); const fn = () => applyMotion(); if (mq.addEventListener) mq.addEventListener('change', fn); else if (mq.addListener) mq.addListener(fn); } } catch (e) { /* 忽略 */ }
  }

  const CPS = { slow: 20, mid: 40, fast: 80, instant: Infinity };
  const UISettings = {
    KEY, DEFAULTS, TABLE, TABS, TAB_ZH, TAB_ICON, UI_ROWS, BRIDGES, OWNER_LOADED,
    define, bridge, applyAll, get, set,
    textCps() { return CPS[get('ui.textSpeed')] || 80; },
    longPressMs() { return Math.round((+get('ui.longPress') || 0.45) * 1000); },
    reducedMotion() { const rm = get('ui.reduceMotion'); return rm === 'on' || (rm === 'system' && sysReduced()); },
    motionLevel() { return UISettings.reducedMotion() ? 'off' : get('ui.motion'); },
    // 行数（tests/settings.js）：定义了的、当前可见的、总表
    rowCount() { const R = S(); return { defined: R ? R.all().length : 0, visible: R ? R.rows().length : 0, table: TABLE.length }; },
  };
  SG.UISettings = UISettings;
  define();
})();
