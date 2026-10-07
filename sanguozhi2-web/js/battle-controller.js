'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 战斗流程
   ← Battle/BattleController.cs：玩家操作、电脑行动、动画与界面
   协程 → async 函数。SG.BattleController.run(setup, playerSide) → Promise。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  // ------------------------------------------------------------ 小工具 --
  const GOLD = '#f3c969';
  const MUTED = '#a8a194';
  const GOOD = '#73d98c';                       // UIKit.Good (0.45,0.85,0.55)
  // C# SideColor：攻方 (0.45,0.75,1)，守方 (1,0.5,0.42)
  function sideColor(s) { return s === 0 ? '#73bfff' : '#ff806b'; }
  function G() { return SG.G; }
  function UI() { return SG.UI; }
  function sfx(name, vol) { try { if (SG.Sfx) SG.Sfx.play(name, vol === undefined ? 0.8 : vol); } catch (e) { /* 无音频 */ } }
  function click() { try { if (SG.Sfx) SG.Sfx.click(); } catch (e) { /* 无音频 */ } }
  function music(kind) { try { if (SG.Sfx) SG.Sfx.music(kind); } catch (e) { /* 无音频 */ } }
  // 背景音乐引擎是否在用（有曲库、音乐开、音频已解锁）：此时胜负由乐曲表现，不再叠旧的 win / lose 音效
  function musicLive() {
    try {
      const S = SG.Sfx, ctx = S && S.context;
      return !!(S && S.musicOn && ctx && ctx.state === 'running' && SG.Music && SG.Music.resolve && SG.Music.resolve('victory'));
    } catch (e) { return false; }
  }
  // 敌方势力的地域文化（world-data 的 cultureOfFaction / cultureOfCity；缺少时为 null）
  function cultureOfFoe(s, playerSide) {
    try {
      const W = SG.WorldData;
      if (!W) return null;
      const foe = playerSide === 1 ? s.attacker : s.defender;
      const f = foe >= 0 && G().factions ? G().factions[foe] : null;
      if (f && f.key && typeof W.cultureOfFaction === 'function') return W.cultureOfFaction(f.key) || null;
      const c = s.target;                               // 无主城：按城市所在地域
      if (c && c.key && typeof W.cultureOfCity === 'function') return W.cultureOfCity(c.key) || null;
    } catch (e) { /* 无地域资料 */ }
    return null;
  }
  // 胜负乐曲（一次性短曲）播完之前，把回到地图时的 music('map') 请求暂缓，曲终再接地图曲。
  // 其它曲目请求（下一场战斗、标题……）立即放行并取消暂缓。
  function deferMapMusicAfterOneShot() {
    const S = SG.Sfx;
    if (!S || typeof S.music !== 'function' || S._sgMusicHold) return;
    const kind = S.musicKind;
    if (kind !== 'victory' && kind !== 'defeat') return;
    let maxSec = 22;
    try { const d = SG.Music.duration(SG.Music.resolve(kind, S.culture)); if (d && d.total > 0) maxSec = d.total + 4; } catch (e) { /* 默认 */ }
    const orig = S.music, own = Object.prototype.hasOwnProperty.call(S, 'music');
    const t0 = Date.now();
    let pending = null, done = false, timer = 0;
    const release = play => {
      if (done) return;
      done = true;
      clearInterval(timer);
      if (S.music === wrapped) { if (own) S.music = orig; else delete S.music; }
      S._sgMusicHold = null;
      if (play && pending) { try { orig.apply(S, pending); } catch (e) { /* 无音频 */ } }
    };
    const wrapped = function (k) {
      if (!done && k === 'map') { pending = Array.prototype.slice.call(arguments); return; }
      release(false);
      return orig.apply(S, arguments);
    };
    S.music = wrapped;
    S._sgMusicHold = release;
    timer = setInterval(() => {
      let busy = false;
      try { const ctx = S.context; busy = S.musicOn && ctx && ctx.state === 'running' && S.musicKind === kind && !S.musicFinished; } catch (e) { busy = false; }
      if (!busy || Date.now() - t0 > maxSec * 1000) release(true);
    }, 250);
  }
  const ANIM_NAME = { on: '开', fast: '快', off: '关' };
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
  function setLabel(btn, html) {
    if (typeof btn.setLabel === 'function') btn.setLabel(html); else btn.innerHTML = html;
  }
  // 全屏 HUD 容器（screens 层，自身不拦截指针）
  function screen(cls) {
    if (SG.UI && typeof SG.UI.screen === 'function') return SG.UI.screen(cls);
    const s = h('div', 'sg-screen ' + cls, null, layer('screens'));
    s.style.pointerEvents = 'none';
    return s;
  }
  // 模态面板（计入 UI.anyModal()）
  function openModal(cls, width, height) {
    if (SG.UI && typeof SG.UI.openModal === 'function') return SG.UI.openModal(cls, { width, height });
    const blocker = h('div', 'sg-modal', null, layer('modals'));
    const panel = h('div', 'sg-panel sg-dialog ' + cls, null, blocker);
    return { blocker, panel, close() { if (blocker.parentNode) blocker.parentNode.removeChild(blocker); } };
  }
  function setBar(bar, v) {
    v = M.clamp01(v);
    if (bar && typeof bar.setValue === 'function') bar.setValue(v);
    else if (bar) bar.style.setProperty('--v', String(v));
  }
  // 重新触发一次性动画类
  function pulse(el, cls) {
    if (SG.UI && typeof SG.UI.pulse === 'function') { try { SG.UI.pulse(el, cls); return; } catch (e) { /* 后备 */ } }
    el.classList.remove(cls);
    void el.offsetWidth;
    el.classList.add(cls);
  }
  function sumTroops(list) { let s = 0; for (const u of list) s += u.troops; return s; }
  function up(v, k) { return v.clone().add(new THREE.Vector3(0, k, 0)); }
  function samePoint(a, b) { return a.x === b.x && a.y === b.y; }

  // UI 图层（ui.js 建立的 labels / screens / modals / toasts）
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

  // ======================================================== BattleController --
  class BattleController {
    constructor(playerSide) {
      this.playerSide = playerSide;
      this.autoPlayer = false;
      this.endTurn = false;
      this.tap = null;
      this.M = null; this.V = null;
      this.hud = null; this.top = null; this.card = null; this.actions = null;
      this.autoBtn = null; this.endBtn = null; this._cardUnit = null;
      this.awaitingInput = false;
      this._aiMovedCam = false;
      this._onTap = (x, y) => { this.tap = { x, y }; };
    }

    static async run(s, playerSide) {
      const bc = new BattleController(playerSide);
      BattleController.current = bc;            // 测试 / 调试用
      try { await bc.main(s); } finally { if (BattleController.current === bc) BattleController.current = null; }
    }

    async main(s) {
      const Mdl = this.M = new SG.BattleModel(s);
      const V = this.V = new SG.BattleView(Mdl);
      const game = SG.Game;
      const rig = game.rig;
      const oldBounds = Object.assign({}, rig.bounds);
      const oldTarget = rig.desired.clone();
      const oldDist = rig.desiredDistance, oldMin = rig.minDist, oldMax = rig.maxDist;
      game.battleView = V;
      if (game.map && game.map.root) game.map.root.visible = false;
      const O = SG.BattleView.Origin, T = SG.BattleView.T;
      // Unity Rect(Origin.x, Origin.z, W*T, H*T)，地图坐标（Unity x / z）
      rig.bounds = { xMin: O.x, yMin: O.z, xMax: O.x + Mdl.W * T, yMax: O.z + Mdl.H * T };
      rig.minDist = 12; rig.maxDist = 62;
      rig.focusWorld(V.boardCenter.clone().sub(SG.U(0, 0, 2)), 40, true);
      rig.onTap.push(this._onTap);
      // 战斗音乐：守城用「孤城」（battle-defend），进攻用「出阵」；地域取敌方势力的文化
      const oldCulture = SG.Sfx ? SG.Sfx.culture : null;
      try {
        const cul = cultureOfFoe(s, this.playerSide);
        if (cul && SG.Sfx && typeof SG.Sfx.setCulture === 'function' && cul !== SG.Sfx.culture) SG.Sfx.culture = cul;
      } catch (e) { /* 无音频 */ }
      music(this.playerSide === 1 ? 'battle-defend' : 'battle');
      this._onKey = e => this.onKey(e);
      window.addEventListener('keydown', this._onKey);
      this.buildHud();
      this.updateHud();                          // 开场横幅期间顶栏即显示日数 / 兵力 / 粮草
      try {
        const an = G().factions[s.attacker].name;
        const dn = s.defender >= 0 ? G().factions[s.defender].name : '守军';
        UI().banner(s.target.name + '之战', an + '军 进攻 ' + dn + '军', 2.2);
        sfx('horn');
        await SG.wait(1.6);

        while (Mdl.result === 0) {
          this.updateHud();
          const human = !this.autoPlayer && Mdl.side === this.playerSide;
          if (human) await this.playerPhase(); else await this.aiPhase();
          if (Mdl.result !== 0) break;
          V.clearHighlights(); V.setCursor(null);
          const newDay = Mdl.endSide();
          for (const u of Mdl.units) V.refresh(u);
          for (const u of Mdl.units.filter(x => !x.alive && V.isShown(x))) await V.rout(u);
          if (newDay) {
            for (const l of Mdl.dayLog) UI().toast(l, 2.5);
            if (Mdl.result === 0) UI().toast('第 ' + Mdl.day + ' 日', 1.2);
          }
        }
        V.clearHighlights(); V.setCursor(null);
        this.updateHud();
        s.attackerWon = Mdl.result === 1;
        const playerWon = this.playerSide >= 0 && (Mdl.result === 1) === (this.playerSide === 0);
        // 胜负乐曲（凯旋 / 残阳）：一次性短曲，回到地图后继续播完再接地图曲；没有音乐引擎时用旧音效
        if (this.playerSide >= 0 && musicLive()) music(playerWon ? 'victory' : 'defeat');
        else sfx(this.playerSide < 0 ? 'horn' : playerWon ? 'win' : 'lose');
        UI().banner(Mdl.result === 1 ? '攻方胜利' : '守方胜利', Mdl.resultReason, 3);
        await SG.wait(2.6);
      } finally {
        if (this._onKey) { window.removeEventListener('keydown', this._onKey); this._onKey = null; }
        const i = rig.onTap.indexOf(this._onTap);
        if (i >= 0) rig.onTap.splice(i, 1);
        if (this.hud && this.hud.parentNode) this.hud.parentNode.removeChild(this.hud);
        if (game.battleView === V) game.battleView = null;
        try { V.dispose(); } catch (e) { console.error(e); }
        if (game.map && game.map.root) game.map.root.visible = true;
        rig.bounds = oldBounds; rig.minDist = oldMin; rig.maxDist = oldMax;
        rig.focusWorld(oldTarget, oldDist, true);
        try { if (SG.Sfx && oldCulture) SG.Sfx.culture = oldCulture; } catch (e) { /* 无音频 */ }
        deferMapMusicAfterOneShot();
        music('map');
      }
    }

    // ---------------------------------------------------------- 界面 --
    buildHud() {
      const hud = this.hud = screen('sg-battle-hud');
      this.top = SG.UI.panel(hud, 'sg-battle-top');
      const actions = this.actions = h('div', 'sg-battle-actions', null, hud);
      // 攻击画面 开 / 快 / 关（SG.Clash.mode；快捷键 V）
      if (SG.Clash && typeof SG.Clash.cycleMode === 'function') {
        this.animBtn = SG.UI.button(actions, '', () => this.cycleAnim(), { className: 'sg-battle-anim', ariaLabel: '攻击动画：开 / 快 / 关（V）' });
        this.animBtn.title = '攻击动画：开 / 快 / 关（快捷键 V）';
        this.syncAnimBtn();
      }
      const auto = SG.UI.button(actions, '委任作战', () => {
        this.autoPlayer = !this.autoPlayer;
        this.endTurn = true;
        setLabel(auto, this.autoPlayer ? '亲自指挥' : '委任作战');
        auto.classList.toggle('is-on', this.autoPlayer);
      });
      const end = SG.UI.button(actions, '结束回合', () => { this.endTurn = true; }, { primary: true });
      this.autoBtn = auto; this.endBtn = end;
      if (this.playerSide < 0) actions.style.display = 'none';
      this.card = SG.UI.panel(hud, 'sg-battle-card');
      this.card.style.display = 'none';
      this.card.style.pointerEvents = 'none';     // 仅显示情报：点击穿透到棋盘
      this._cardUnit = null;
    }

    updateHud() {
      const Mdl = this.M, B = SG.Balance;
      const side = Mdl.side === 0 ? '<span class="sg-side0">攻方</span>' : '<span class="sg-side1">守方</span>';
      const dots = '●'.repeat(Math.max(0, Mdl.ap)) + '○'.repeat(Math.max(0, Mdl.actionPoints(Mdl.side) - Mdl.ap));
      const t0 = sumTroops(Mdl.alive(0)), t1 = sumTroops(Mdl.alive(1));
      // 两组：窄屏换行时在分隔符处断开
      this.top.innerHTML =
        `<span>第 <b>${Math.min(Mdl.day, B.BattleDays)}</b>/${B.BattleDays} 日　${side}行动　行动力 <span class="sg-tokens">${dots}</span></span>` +
        `<span class="sg-sep">｜</span>` +
        `<span><span class="sg-side0">攻</span> <b>${t0}</b>&ensp;粮${Mdl.food[0]}　<span class="sg-side1">守</span> <b>${t1}</b>&ensp;粮${Mdl.food[1]}</span>`;
      const sep = this.top.querySelector('.sg-sep');
      if (sep && sep.previousElementSibling && sep.nextElementSibling) {
        sep.style.display = '';
        if (sep.nextElementSibling.offsetTop > sep.previousElementSibling.offsetTop + 2) sep.style.display = 'none';
      }
      if (this.endBtn) this.endBtn.disabled = !(Mdl.side === this.playerSide && !this.autoPlayer);
      this.syncAnimBtn();
    }

    // ---------------------------------------------------------- 动画开关 --
    animMode() { return SG.Clash && SG.Clash.mode ? SG.Clash.mode : 'off'; }
    syncAnimBtn() {
      const b = this.animBtn;
      if (!b || !SG.Clash) return;
      const m = this.animMode();
      if (b._mode === m) return;
      b._mode = m;
      setLabel(b, '动画<b>' + ANIM_NAME[m] + '</b>');
      b.dataset.mode = m;
    }
    cycleAnim() {
      if (!SG.Clash || typeof SG.Clash.cycleMode !== 'function') return;
      const m = SG.Clash.cycleMode();
      this.syncAnimBtn();
      UI().toast('攻击动画：' + ANIM_NAME[m] + (m === 'fast' ? '（加速播放）' : m === 'off' ? '（直接显示结果）' : ''), 1.4);
      click();
    }
    // 键盘：V 切换攻击动画（对话框、单挑格斗、攻击画面进行中时不响应）
    onKey(e) {
      if (!e || e.repeat || e.ctrlKey || e.metaKey || e.altKey) return;
      if (!(e.code === 'KeyV' || e.key === 'v' || e.key === 'V')) return;
      const t = e.target;
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
      if (SG.UI && typeof SG.UI.anyModal === 'function' && SG.UI.anyModal()) return;
      if ((SG.DuelGame && SG.DuelGame.active) || (SG.Clash && (SG.Clash.active || SG.Clash._cut))) return;
      if (this.playerSide < 0) return;
      e.preventDefault();
      this.cycleAnim();
    }

    showCard(u) {
      if (!u) { this.card.style.display = 'none'; this._cardUnit = null; return; }
      const f = u.form;
      const max = Math.max(1, SG.maxTroops(u.gen));
      // 换了部队时重播滑入动画
      if (this._cardUnit !== u || this.card.style.display === 'none') {
        this.card.style.display = 'none'; void this.card.offsetWidth; this.card.style.display = '';
      }
      this._cardUnit = u;
      this.card.innerHTML =
        `<div><span class="sg-card-name">${SG.esc(u.gen.name)}</span><span class="sg-card-sub">${u.side === 0 ? '攻方' : '守方'}${u.commander ? '·主将' : ''}</span></div>` +
        `<div class="sg-bar" style="--c:${sideColor(u.side)};--v:${M.clamp01(u.troops / max).toFixed(3)}"><i class="sg-bar-fill"></i></div>` +
        `<div>兵力 <b>${u.troops}</b>　士气 ${u.morale}　训练 ${u.gen.training}</div>` +
        `<div>武力 ${u.gen.war}　智力 ${u.gen.intel}</div>` +
        `<div>阵型 <span class="sg-goldc">${f.name}</span>（攻×${f.atk.toFixed(2)} 防×${f.def.toFixed(2)}）</div>` +
        `<div>地形 ${SG.Defs.terrainName(this.M.map[u.x][u.y])}${u.confused > 0 ? '　<span style="color:#c79bff">混乱中</span>' : ''}</div>` +
        this.specialLine(u);
      if (SG.Portrait) this.card.prepend(SG.Portrait.el(u.gen, { size: 70, className: u.alive ? '' : 'is-dead' }));
    }

    // 情报卡的必杀一行：招式名 + 可用 / 已施展
    specialLine(u) {
      try { return SG.Specials && typeof SG.Specials.cardLine === 'function' ? SG.Specials.cardLine(u) : ''; } catch (e) { console.error(e); return ''; }
    }

    // ---------------------------------------------------------- 玩家 --
    async playerPhase() {
      const Mdl = this.M, V = this.V;
      this.endTurn = false; this.tap = null;
      let sel = null;
      this.updateHud();
      // 镜头回到我军（仅当电脑行动时镜头跟随了敌军；开战首回合保持 Unity 的全局视角）
      if (this._aiMovedCam) {
        this._aiMovedCam = false;
        const lead = Mdl.commander(Mdl.side);
        const focusU = lead && lead.alive ? lead : Mdl.alive(Mdl.side).find(u => Mdl.canAct(u));
        if (focusU) SG.Game.rig.focusWorld(V.tile(focusU.x, focusU.y));
      }
      UI().toast('我军行动：点选部队', 1.4);
      while (!this.endTurn && Mdl.result === 0 && Mdl.ap > 0 && Mdl.alive(Mdl.side).some(u => Mdl.canAct(u))) {
        this.updateHud();
        if (sel) { V.setCursor(sel); this.showCard(sel); }
        this.tap = null;
        this.awaitingInput = true;               // 测试 / 调试用：正在等待玩家点选
        while (this.tap == null && !this.endTurn) await SG.frame();
        this.awaitingInput = false;
        if (this.endTurn) break;
        const tp = this.tap;
        // 先看是否点在部队头顶的名牌上（窄屏上名牌比部队本身更显眼，常被当作点选目标），否则按地面取格
        const lu = this.unitFromLabel(tp.x, tp.y);
        const t = lu ? { x: lu.x, y: lu.y } : V.tileFromScreen(tp.x, tp.y);
        if (!t) continue;
        const at = Mdl.unitAt(t.x, t.y);
        if (sel == null) {
          if (at) this.showCard(at);
          if (at && at.side === Mdl.side && Mdl.canAct(at)) { sel = at; this.showRange(sel); click(); }
          continue;
        }
        if (at === sel) { V.clearHighlights(); await this.actionMenu(sel); sel = null; V.setCursor(null); continue; }
        const moves = Mdl.moveTargets(sel);
        if (at == null && moves.some(p => samePoint(p, t))) {
          V.clearHighlights();
          const path = Mdl.pathTo(sel, t);
          await V.moveUnit(sel, path);
          Mdl.move(sel, t);
          if (Mdl.result !== 0) break;
          V.setCursor(sel);
          await this.actionMenu(sel, true);
          sel = null; V.setCursor(null);
          continue;
        }
        if (at && at.side !== Mdl.side && SG.BattleModel.dist(at, sel) === 1) {
          V.clearHighlights();
          await this.doAttack(sel, at);
          Mdl.spend(sel); sel = null; V.setCursor(null);
          continue;
        }
        if (at && at.side === Mdl.side && Mdl.canAct(at)) { sel = at; this.showRange(sel); continue; }
        if (at) this.showCard(at);
        sel = null; V.clearHighlights(); V.setCursor(null);
      }
      V.clearHighlights(); V.setCursor(null); this.showCard(null);
    }

    // 屏幕点 (x, y) 落在哪支部队的名牌（.sg-uinfo）上：按名牌当前层级从上往下找
    // （与 BattleView._layoutLabels 的排布一致：选中部队 > 主将 > 其他），只认清晰显示的名牌——
    // 因避让失败而淡化压到下层的名牌不拦截点击，免得挡住它下面的地块。没有命中返回 null。
    unitFromLabel(x, y) {
      const Mdl = this.M, V = this.V;
      if (!V || !V.visuals || typeof document === 'undefined') return null;
      const cands = [];
      for (const v of V.visuals.values()) {
        const I = v.info, L = v.lbl, u = v.u;
        if (!I || !I.card || !L || !L.shown || !v.follow || !u || !u.alive) continue;
        if (Mdl.unitAt(u.x, u.y) !== u) continue;
        if (!(v.labelAlpha * L.fade >= 0.45)) continue;
        if (I.anchor && (I.anchor.style.visibility === 'hidden' || !I.anchor.isConnected)) continue;
        cands.push({ u, z: typeof L.z === 'number' ? L.z : -1e9 });
      }
      cands.sort((a, b) => b.z - a.z);
      for (const c of cands) {
        const r = V.visuals.get(c.u).info.card.getBoundingClientRect();
        if (r.width > 0 && r.height > 0 && x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) return c.u;
      }
      return null;
    }

    showRange(u) {
      const Mdl = this.M, V = this.V;
      V.clearHighlights();
      V.highlight(Mdl.moveTargets(u).filter(p => p.x !== u.x || p.y !== u.y), '#59b3ff', 0.55);   // (0.35,0.7,1,0.55)
      V.highlight(Mdl.adjacentEnemies(u).map(e => ({ x: e.x, y: e.y })), '#ff4d40', 0.7);           // (1,0.3,0.25,0.7)
    }

    async actionMenu(u, moved) {
      const Mdl = this.M;
      moved = !!moved;
      const adj = Mdl.adjacentEnemies(u);
      const items = [
        item('攻击', adj.length > 0 ? adj.length + ' 支敌军相邻' : '无相邻敌军', adj.length > 0),
        item('策略', '智力 ' + u.gen.intel, u.gen.intel >= 40),
        item('单挑', '武力 ' + u.gen.war, adj.length > 0),
        item('阵型', u.form.name),
        item('待机'),
      ];
      // 必杀：插在「策略」之后，显示招式名；已施展 / 无目标时灰显并注明原因
      const SP = this.specialsOn() ? 1 : 0;
      if (SP) {
        let it = null;
        try { it = SG.Specials.menuItem(this, u); } catch (e) { console.error(e); }
        if (it) items.splice(2, 0, it);
        else items.splice(2, 0, item('必杀', '无', false));
      }
      let r = await UI().choose(u.gen.name + ' 的行动', items, null, 520);
      if (SP && r === 2) {
        const us = Mdl.specialUsable(u);
        if (!us.ok) { UI().toast(SG.esc(us.why || '无法施展')); if (moved) Mdl.spend(u); }
        else {
          const tgt = await SG.Specials.pickTarget(this, u);
          if (tgt) { await this.doSpecial(u, tgt, false); Mdl.spend(u); } else if (moved) Mdl.spend(u);
        }
        for (const x of Mdl.units) this.V.refresh(x);
        return;
      }
      if (SP && r > 2) r--;
      switch (r) {
        case 0: {
          const tgt = await this.pickEnemy(adj, '攻击目标');
          if (tgt) { await this.doAttack(u, tgt); Mdl.spend(u); } else if (moved) Mdl.spend(u);
          break;
        }
        case 1: await this.tacticMenu(u, moved); break;
        case 2: {
          const tgt = await this.pickEnemy(adj, '单挑对手');
          if (tgt) { await this.doDuel(u, tgt); Mdl.spend(u); } else if (moved) Mdl.spend(u);
          break;
        }
        case 3: await this.formationMenu(u); break;
        case 4: Mdl.spend(u); break;
        default: if (moved) Mdl.spend(u); break;
      }
      for (const x of Mdl.units) this.V.refresh(x);
    }

    async pickEnemy(list, title) {
      if (list.length === 1) return list[0];
      const r = await UI().choose(title, list.map(e => item(e.gen.name, '兵 ' + e.troops + '　武 ' + e.gen.war + '　智 ' + e.gen.intel)), null, 560);
      return r >= 0 ? list[r] : null;
    }

    async tacticMenu(u, moved) {
      const Mdl = this.M;
      const items = [];
      for (const t of SG.Defs.tactics) {
        const us = Mdl.tacticUsable(u, t);
        let why = us.why || '';
        const ok = us.ok && Mdl.tacticTargets(u, t).length > 0;
        if (why === '' && !ok) why = '范围内无目标';
        items.push(item(t.name, ok ? t.desc : why, ok));
      }
      const r = await UI().choose('策略', items, null, 640);
      if (r < 0) { if (moved) Mdl.spend(u); return; }
      const tac = SG.Defs.tactics[r];
      const targets = Mdl.tacticTargets(u, tac);
      let tgt;
      if (tac.kind === SG.TacticKind.Inspire) tgt = u;
      else tgt = await this.pickEnemy(targets, tac.name + '的目标');
      if (!tgt) { if (moved) Mdl.spend(u); return; }
      await this.doTactic(u, tac, tgt);
      Mdl.spend(u);
    }

    async formationMenu(u) {
      const Mdl = this.M, V = this.V;
      const sign = v => (v > 0 ? '+' + v : v < 0 ? '-' + Math.abs(v) : '0');
      const items = SG.Defs.formations.map((f, i) => item(
        f.name + (i === u.formation ? '（当前）' : ''),
        Mdl.formationAllowed(u, i) ? `攻×${f.atk.toFixed(2)} 防×${f.def.toFixed(2)} 机动${sign(f.move)}` : (f.reqInt > 0 ? '需智力 ' + f.reqInt : '需武力 ' + f.reqWar),
        Mdl.formationAllowed(u, i), f.desc));
      const r = await UI().choose('变换阵型（消耗本回合行动）', items, null, 760);
      if (r < 0 || r === u.formation) return;
      Mdl.changeFormation(u, r);
      V.floatText(up(V.tile(u.x, u.y), 1.8), SG.Defs.formations[r].name + '之阵', GOLD, 34);
      sfx('horn', 0.4);
      Mdl.spend(u);
    }

    // ---------------------------------------------------------- 动作 --
    // 当前是否在「看」电脑行动（敌方回合、委任或旁观）：攻击画面 / 必杀特写加速
    watching() { return this.autoPlayer || this.playerSide < 0 || this.M.side !== this.playerSide; }
    specialsOn() {
      return !!(SG.Specials && typeof SG.Specials.perform === 'function' && SG.BattleModel.specialsEnabled !== false &&
        this.M && typeof this.M.specialUsable === 'function');
    }

    async doAttack(a, t) {
      const Mdl = this.M, V = this.V;
      this.showCard(a);
      await V.lunge(a, t);
      const beforeA = a.troops, beforeT = t.troops;
      const r = Mdl.attack(a, t);
      // 攻击画面（SG.Clash：开 / 快 / 关）。电脑行动时 ×1.5，委任时 ×1.8（再乘「快」的 ×2）
      if (SG.Clash && SG.Clash.enabled && typeof SG.Clash.play === 'function') {
        try {
          const speed = this.autoPlayer ? 1.8 : this.watching() ? 1.5 : 1;
          await SG.Clash.play(SG.Clash.fromBattle(Mdl, a, t, beforeA, beforeT, {
            playerSide: this.playerSide >= 0 ? this.playerSide : null,
            speed,
          }));
        } catch (e) { console.error(e); }
        this.syncAnimBtn();                      // 攻击画面里的「动画」按钮也能切换
      }
      sfx('hit');
      V.hit(t, r.dmg);
      if (r.counter > 0) { await SG.wait(0.15); V.hit(a, r.counter); }
      V.refresh(a); V.refresh(t);
      await SG.wait(0.35);
      if (!t.alive) { UI().toast(t.gen.name + '部溃散！'); await V.rout(t); }
      if (!a.alive) { UI().toast(a.gen.name + '部溃散！'); await V.rout(a); }
    }

    async doTactic(u, tac, t) {
      const Mdl = this.M, V = this.V, K = SG.TacticKind;
      UI().toast(u.gen.name + '施展「' + tac.name + '」！', 1.5);
      const res = Mdl.useTactic(u, tac, t);
      switch (tac.kind) {
        case K.Fire: await V.fireFx(t); break;
        case K.Rockfall: await V.rockFx(t); break;
        case K.Confuse: await V.magicFx(t, '#bf80ff'); break;        // (0.75,0.5,1)
        case K.Inspire: await V.magicFx(u, '#ffd966'); break;        // (1,0.85,0.4)
      }
      if (!res.success) V.floatText(up(V.tile(t.x, t.y), 1.6), '识破', MUTED, 34);
      else if (res.dmg > 0) V.hit(t, res.dmg, true);
      else if (tac.kind === K.Confuse) V.floatText(up(V.tile(t.x, t.y), 1.6), '混乱', '#cc99ff', 38);
      else if (tac.kind === K.Inspire) V.floatText(up(V.tile(u.x, u.y), 1.6), '士气高涨', GOOD, 34);
      for (const x of Mdl.units) V.refresh(x);
      await SG.wait(0.4);
      if (!t.alive) { UI().toast(t.gen.name + '部溃散！'); await V.rout(t); }
    }

    // 必杀技：特写（由控制器按动画设定播放）→ SG.Specials.perform（结算、特效、伤害、溃散）。不消耗行动（调用方 spend）
    async doSpecial(u, target, ai) {
      const Mdl = this.M, V = this.V, SP = SG.Specials;
      const sp = SP.of(u.gen);
      const us = Mdl.specialUsable(u);
      if (!sp || !us.ok) { UI().toast(SG.esc(us.why || '无法施展')); return null; }
      this.showCard(u); V.setCursor(u);
      await this.specialCutIn(u, sp, ai);
      let res = null;
      try { res = await SP.perform(this, u, target, { cutIn: false, fast: !!ai }); } catch (e) { console.error(e); }
      for (const x of Mdl.units) V.refresh(x);
      this.updateHud();
      return res;
    }
    // 特写时长：「开」1.1 秒（电脑 / 委任 0.85 秒），「快」0.75 秒；「关」不播。
    // SG.Clash.cutIn 自己会再除以 SG.Clash.speed，这里换算成最终时长，避免「快」时被加速两次。
    async specialCutIn(u, sp, ai) {
      const SP = SG.Specials, C = SG.Clash;
      const quick = !!ai || this.autoPlayer;
      let color = sp.color;
      try { if (typeof SP.uiColor === 'function') color = SP.uiColor(sp); } catch (e) { /* 原色 */ }
      try {
        if (C && typeof C.cutIn === 'function') {
          if (!C.enabled) return;
          const k = Math.max(0.1, +C.speed || 1);
          const dur = k > 1.01 ? 0.75 : quick ? 0.85 : 1.1;
          await C.cutIn({ gen: u.gen, name: sp.name, color, side: u.side, cry: sp.cry, special: sp, speed: 1.1 / (dur * k) });
        } else if (typeof SP.cutIn === 'function') {
          await SP.cutIn({ gen: u.gen, name: sp.name, color, side: u.side, cry: sp.cry, fast: quick });
        }
      } catch (e) { console.error(e); }
    }

    // 单挑：玩家一方参与且未委任时进入格斗画面（SG.DuelGame.perform：应战判定 → 格斗 → Mdl.duelFinish → 溃散）；
    // 电脑对电脑 / 委任时照旧 Mdl.duel() + 单挑对话框。格斗画面出错时退回脚本单挑 + 旧对话框。
    async doDuel(a, b) {
      const Mdl = this.M, V = this.V, DG = SG.DuelGame;
      let fight = false;
      try { fight = !!(DG && typeof DG.perform === 'function' && typeof DG.shouldPlay === 'function' && DG.shouldPlay(this, a, b)); } catch (e) { console.error(e); }
      if (fight) {
        try {
          await DG.perform(this, a, b);
          for (const x of Mdl.units) V.refresh(x);
          this.updateHud();
          return;
        } catch (e) {
          console.error(e);
          for (const x of Mdl.units) V.refresh(x);
          if (!a.alive || !b.alive || Mdl.result !== 0) {   // 已结算：只补溃散
            for (const x of [a, b]) if (!x.alive && V.isShown(x)) await V.rout(x);
            return;
          }
          UI().toast('单挑画面出错，改为简易单挑', 1.6);
          await this.duelDialog(a, b, this.scriptedDuel(a, b), false);
          return;
        }
      }
      const res = Mdl.duel(a, b);
      if (!res.accepted) {
        await UI().say(b.gen.name + '：哼，匹夫之勇，不足与战！\n（' + b.gen.name + '拒绝单挑，其部士气下降）', b.gen.name, sideColor(b.side));
        for (const x of Mdl.units) V.refresh(x);
        return;
      }
      await this.duelDialog(a, b, res, true);
    }

    // 格斗画面失败时的后备：第一版的回合制单挑（不再做应战判定），并结算后果
    scriptedDuel(a, b) {
      const Mdl = this.M;
      let hpA = 100, hpB = 100;
      const rounds = [];
      for (let i = 0; i < 30 && hpA > 0 && hpB > 0; i++) {
        const who = SG.Random.value() < a.gen.war / (a.gen.war + b.gen.war) ? 0 : 1;
        const striker = who === 0 ? a : b;
        const dmg = M.roundToInt(SG.Random.rangeInt(6, 16) * (0.6 + striker.gen.war / 120));
        if (who === 0) hpB = Math.max(0, hpB - dmg); else hpA = Math.max(0, hpA - dmg);
        rounds.push({ who, dmg, hpA, hpB });
      }
      return Mdl.duelFinish(a, b, hpA >= hpB, rounds);
    }

    // 旧单挑对话框：按 res.rounds 播放（胜负已由模型结算），最后败者溃散
    async duelDialog(a, b, res, challenge) {
      const Mdl = this.M, V = this.V;
      const winner = res.winner === 0 ? a : b, loser = res.winner === 0 ? b : a;
      // Mdl.duel 已判定胜负（败者 alive = false）；单挑画面播完前保留败者头顶标签，免得提前泄露结果
      const lv = V.vis(loser);
      if (lv) lv.holdLabel = true;
      let modal = null;
      try {
        if (challenge) await UI().say(a.gen.name + '：' + b.gen.name + '，可敢与我一战？', a.gen.name, sideColor(a.side));

        // 单挑画面
        modal = openModal('sg-duel', 980, 420);
        const panel = modal.panel;
        h('div', 'sg-duel-title', '单　挑', panel);
        const arena = h('div', 'sg-duel-arena', null, panel);
        const side = u => {
          const col = h('div', 'sg-duel-side sg-side-' + u.side, null, arena);
          col.appendChild(SG.UI.medal(u.gen.name.substring(0, 1), sideColor(u.side), 130));
          h('div', 'sg-duel-name', SG.esc(u.gen.name) + '<small>武力 ' + u.gen.war + '</small>', col);
          const bar = SG.UI.bar(1, sideColor(u.side));
          col.appendChild(bar);
          return { col, bar };
        };
        const sa = side(a);
        const vs = h('div', 'sg-duel-vs', 'VS', arena);
        const sb = side(b);
        const log = h('div', 'sg-duel-log', '', panel);
        await SG.wait(0.6);
        let round = 0;
        for (const rd of res.rounds) {
          round++;
          sfx('duel');
          setBar(sa.bar, rd.hpA / 100); setBar(sb.bar, rd.hpB / 100);
          const striker = rd.who === 0 ? a : b;
          log.innerHTML = `第 ${round} 合　${SG.esc(striker.gen.name)}一击，造成 ${rd.dmg} 伤害`;
          pulse((rd.who === 0 ? sb : sa).col, 'is-struck');
          pulse(vs, 'is-hit');
          await SG.wait(0.32);
        }
        (res.winner === 0 ? sb : sa).col.style.opacity = '0.55';
        log.innerHTML = `<b>${SG.esc(winner.gen.name)}</b>击败了${SG.esc(loser.gen.name)}！`;
        sfx('win', 0.5);
        await SG.wait(1.4);
      } finally {
        if (modal) modal.close();
        for (const x of Mdl.units) V.refresh(x);
        if (lv) lv.holdLabel = false;          // 单挑结束：标签随溃散动画一起消失
      }
      await V.rout(loser);
    }

    // ---------------------------------------------------------- 电脑 --
    async aiPhase() {
      const Mdl = this.M, V = this.V, dist = SG.BattleModel.dist;
      await SG.wait(0.3);
      const enemyDist = u => {
        const es = Mdl.alive(1 - Mdl.side);
        if (es.length === 0) return 99;
        let m = Infinity;
        for (const e of es) m = Math.min(m, dist(e, u));
        return m;
      };
      // OrderBy(主将 ? 1 : 0).ThenBy(与最近敌军的距离)，稳定排序
      const order = Mdl.alive(Mdl.side).filter(u => Mdl.canAct(u))
        .map((u, i) => ({ u, k1: u.commander ? 1 : 0, k2: enemyDist(u), i }))
        .sort((p, q) => (p.k1 - q.k1) || (p.k2 - q.k2) || (p.i - q.i))
        .map(o => o.u);
      for (const u of order) {
        if (Mdl.result !== 0 || Mdl.ap <= 0) break;
        if (!Mdl.canAct(u)) continue;
        if (this.endTurn && this.playerSide >= 0 && Mdl.side === this.playerSide && !this.autoPlayer) break;
        const plan = Mdl.planFor(u);
        SG.Game.rig.focusWorld(V.tile(u.x, u.y));
        this._aiMovedCam = true;
        this.showCard(u); V.setCursor(u);
        if (plan.move) {
          const path = Mdl.pathTo(u, plan.move);
          await V.moveUnit(u, path);
          Mdl.move(u, plan.move);
          V.setCursor(u);
          if (Mdl.result !== 0) break;
        }
        const act = Mdl.planAction(u);
        if (act.kind === 'attack') await this.doAttack(u, act.target);
        else if (act.kind === 'tactic') await this.doTactic(u, act.tactic, act.target);
        else if (act.kind === 'duel') await this.doDuel(u, act.target);
        else if (act.kind === 'special' && this.specialsOn()) {
          const res = await this.doSpecial(u, act.target, true);
          if (res && res.refreshed) for (const x of res.refreshed) order.push(x);   // 疾行：再动的友军排到队尾（canAct 防止重复行动）
        }
        Mdl.spend(u);
        this.updateHud();
        await SG.wait(0.2);
      }
      V.setCursor(null); this.showCard(null);
    }
  }

  BattleController.sideColor = sideColor;
  BattleController.cultureOfFoe = cultureOfFoe;     // 测试用：战斗音乐的地域
  BattleController.current = null;
  SG.BattleController = BattleController;
})();
