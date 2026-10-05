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
      music('battle');
      this.buildHud();
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
        sfx(this.playerSide < 0 ? 'horn' : playerWon ? 'win' : 'lose');
        UI().banner(Mdl.result === 1 ? '攻方胜利' : '守方胜利', Mdl.resultReason, 3);
        await SG.wait(2.6);
      } finally {
        const i = rig.onTap.indexOf(this._onTap);
        if (i >= 0) rig.onTap.splice(i, 1);
        if (this.hud && this.hud.parentNode) this.hud.parentNode.removeChild(this.hud);
        if (game.battleView === V) game.battleView = null;
        try { V.dispose(); } catch (e) { console.error(e); }
        if (game.map && game.map.root) game.map.root.visible = true;
        rig.bounds = oldBounds; rig.minDist = oldMin; rig.maxDist = oldMax;
        rig.focusWorld(oldTarget, oldDist, true);
        music('map');
      }
    }

    // ---------------------------------------------------------- 界面 --
    buildHud() {
      const hud = this.hud = screen('sg-battle-hud');
      this.top = SG.UI.panel(hud, 'sg-battle-top');
      const actions = this.actions = h('div', 'sg-battle-actions', null, hud);
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
        `<div>地形 ${SG.Defs.terrainName(this.M.map[u.x][u.y])}${u.confused > 0 ? '　<span style="color:#c79bff">混乱中</span>' : ''}</div>`;
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
        const t = V.tileFromScreen(tp.x, tp.y);
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
      const r = await UI().choose(u.gen.name + ' 的行动', items, null, 520);
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
    async doAttack(a, t) {
      const Mdl = this.M, V = this.V;
      this.showCard(a);
      await V.lunge(a, t);
      const r = Mdl.attack(a, t);
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

    async doDuel(a, b) {
      const Mdl = this.M, V = this.V;
      const res = Mdl.duel(a, b);
      if (!res.accepted) {
        await UI().say(b.gen.name + '：哼，匹夫之勇，不足与战！\n（' + b.gen.name + '拒绝单挑，其部士气下降）', b.gen.name, sideColor(b.side));
        for (const x of Mdl.units) V.refresh(x);
        return;
      }
      await UI().say(a.gen.name + '：' + b.gen.name + '，可敢与我一战？', a.gen.name, sideColor(a.side));

      // 单挑画面
      const modal = openModal('sg-duel', 980, 420);
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
      const winner = res.winner === 0 ? a : b, loser = res.winner === 0 ? b : a;
      (res.winner === 0 ? sb : sa).col.style.opacity = '0.55';
      log.innerHTML = `<b>${SG.esc(winner.gen.name)}</b>击败了${SG.esc(loser.gen.name)}！`;
      sfx('win', 0.5);
      await SG.wait(1.4);
      modal.close();
      for (const x of Mdl.units) V.refresh(x);
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
        Mdl.spend(u);
        this.updateHud();
        await SG.wait(0.2);
      }
      V.setCursor(null); this.showCard(null);
    }
  }

  BattleController.sideColor = sideColor;
  BattleController.current = null;
  SG.BattleController = BattleController;
})();
