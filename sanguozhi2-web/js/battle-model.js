'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 战术战斗规则
   移植自 Battle/BattleModel.cs。网格坐标为普通对象 {x, y}（按字段比较）。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;
  const Balance = SG.Balance;
  const Defs = SG.Defs;
  const Terrain = SG.Terrain;
  const TacticKind = SG.TacticKind;
  const Seq = SG.Seq;

  function G() { return SG.G; }
  function P(x, y) { return { x, y }; }

  // ================================================================== BUnit --
  class BUnit {
    constructor(o) {
      this.gen = null;
      this.side = 0;          // 0 攻方 1 守方
      this.x = 0; this.y = 0;
      this.morale = 70;
      this.formation = 0;
      this.acted = false;
      this.confused = 0;
      this.commander = false;
      this.alive = true;
      if (o) Object.assign(this, o);
    }
    get troops() { return this.gen.troops; }
    set troops(v) { this.gen.troops = Math.max(0, v); }
    get form() { return Defs.formations[this.formation]; }
    get mobility() { return 3 + this.form.move; }
  }

  function grid(w, h, v) {
    const a = new Array(w);
    for (let x = 0; x < w; x++) { a[x] = new Array(h); for (let y = 0; y < h; y++) a[x][y] = v; }
    return a;
  }

  // ============================================================ BattleModel --
  class BattleModel {
    constructor(s) {
      this.setup = s;
      this.W = Balance.BattleW; this.H = Balance.BattleH;
      this.map = null; this.burning = null; this.height = null;
      this.units = [];
      this.day = 1; this.side = 0; this.ap = 0;
      this.food = [0, 0];
      this.castle = P(0, 0);
      this.result = 0; // 0 进行中 1 攻方胜 2 守方胜
      this.resultReason = '';
      this.dayLog = [];

      const g = G();
      this.generate(s.target.id * 31 + g.year);
      this.food[0] = s.atkFood; this.food[1] = s.target.food;
      for (let i = 0; i < s.atk.length; i++) {
        const u = new BUnit({ gen: s.atk[i], side: 0, x: 0, y: 0 });
        u.commander = i === 0 || g.isRuler(s.atk[i]);
        u.morale = 60 + M.idiv(s.atk[i].training, 5);
        this.units.push(u);
      }
      if (this.units.filter(u => u.side === 0 && u.commander).length > 1) {
        for (const u of this.units) if (u.side === 0 && !g.isRuler(u.gen)) u.commander = false;
      }
      for (let i = 0; i < s.def.length; i++) {
        const u = new BUnit({ gen: s.def[i], side: 1 });
        u.commander = i === 0;
        u.morale = 65 + M.idiv(s.def[i].training, 5);
        this.units.push(u);
      }
      this.deploy();
      for (const u of this.units) u.formation = this.aiFormation(u);
      this.startSide(0);
    }

    // ---------------------------------------------------------- 地形 --
    generate(seed) {
      const W = this.W, H = this.H;
      const rnd = SG.SeededRandom(seed);
      const map = this.map = grid(W, H, Terrain.Plain);
      this.burning = grid(W, H, 0);
      const height = this.height = grid(W, H, 0);
      const ox = rnd.nextDouble() * 100, oy = rnd.nextDouble() * 100;
      for (let x = 0; x < W; x++) {
        for (let y = 0; y < H; y++) {
          const n = M.perlinNoise(ox + x * 0.28, oy + y * 0.28);
          const m = M.perlinNoise(ox + 50 + x * 0.22, oy + 50 + y * 0.22);
          let t = Terrain.Plain;
          if (n > 0.72) t = Terrain.Mountain;
          else if (n > 0.6) t = Terrain.Hill;
          else if (m > 0.62) t = Terrain.Forest;
          map[x][y] = t;
          height[x][y] = t === Terrain.Mountain ? 1.1 : t === Terrain.Hill ? 0.45 : n * 0.12;
        }
      }
      // 河流
      let rx = 4 + rnd.next(3);
      for (let y = 0; y < H; y++) {
        map[rx][y] = Terrain.River; height[rx][y] = -0.15;
        if (rnd.nextDouble() < 0.3) rx = M.clamp(rx + (rnd.nextDouble() < 0.5 ? -1 : 1), 3, 7);
        if (y < H - 1 && map[rx][y] !== Terrain.River) { map[rx][y] = Terrain.River; height[rx][y] = -0.15; }
      }
      // 城池
      const castle = this.castle = P(W - 3, M.idiv(H, 2));
      for (let x = W - 5; x < W; x++) {
        for (let y = castle.y - 2; y <= castle.y + 2; y++) {
          const edge = x === W - 5 || y === castle.y - 2 || y === castle.y + 2 || x === W - 1;
          map[x][y] = edge ? Terrain.Wall : Terrain.Plain;
          height[x][y] = 0.05;
        }
      }
      map[W - 5][castle.y] = Terrain.Gate;
      map[W - 3][castle.y - 2] = Terrain.Gate;
      map[W - 3][castle.y + 2] = Terrain.Gate;
      map[castle.x][castle.y] = Terrain.Castle;
      // 出生区域与城门前清理
      for (let y = 0; y < H; y++) for (let x = 0; x < 2; x++) if (map[x][y] === Terrain.Mountain) map[x][y] = Terrain.Plain;
      for (let x = W - 8; x < W - 5; x++) if (map[x][castle.y] === Terrain.Mountain) map[x][castle.y] = Terrain.Plain;
      this.ensurePath();
    }

    // 保证攻方能抵达城门
    ensurePath() {
      const W = this.W, H = this.H, map = this.map, castle = this.castle;
      const gate = P(W - 5, castle.y);
      for (let guard = 0; guard < 30; guard++) {
        const d = this.distances(P(0, M.idiv(H, 2)), null, 999, -1);
        if (d[gate.x][gate.y] < 999) return;
        // 拆除一座最近的山
        for (let x = 0; x < W - 5; x++) {
          for (let y = 0; y < H; y++) {
            if (map[x][y] === Terrain.Mountain && Math.abs(y - castle.y) <= M.idiv(guard, 3) + 1) { map[x][y] = Terrain.Hill; this.height[x][y] = 0.45; }
          }
        }
      }
    }

    deploy() {
      const W = this.W, H = this.H, castle = this.castle;
      const atk = this.units.filter(u => u.side === 0);
      const spots0 = [];
      for (let i = 0; i < H; i++) {
        const y = M.idiv(H, 2) + ((i % 2 === 0) ? M.idiv(i, 2) : -M.idiv(i + 1, 2));
        if (y >= 0 && y < H) { spots0.push(P(1, y)); spots0.push(P(0, y)); }
      }
      let k = 0;
      for (const u of atk) {
        while (k < spots0.length && !this.passable(spots0[k].x, spots0[k].y)) k++;
        const p = k < spots0.length ? spots0[k] : P(0, M.idiv(H, 2));
        u.x = p.x; u.y = p.y; k++;
      }
      const def = this.units.filter(u => u.side === 1);
      const spots1 = [P(castle.x, castle.y), P(W - 4, castle.y), P(W - 6, castle.y), P(W - 4, castle.y - 1), P(W - 4, castle.y + 1), P(W - 2, castle.y - 1), P(W - 2, castle.y + 1)];
      k = 0;
      for (const u of def) {
        while (k < spots1.length && (!this.passable(spots1[k].x, spots1[k].y) || this.unitAt(spots1[k].x, spots1[k].y) != null)) k++;
        const p = k < spots1.length ? spots1[k] : castle;
        u.x = p.x; u.y = p.y; k++;
      }
    }

    // ---------------------------------------------------------- 查询 --
    inBounds(x, y) { return x >= 0 && y >= 0 && x < this.W && y < this.H; }
    passable(x, y) { return this.inBounds(x, y) && Defs.terrainCost(this.map[x][y]) < 99; }
    unitAt(x, y) {
      for (const u of this.units) if (u.alive && u.x === x && u.y === y) return u;
      return null;
    }
    alive(s) { return this.units.filter(u => u.alive && u.side === s); }
    commander(s) {
      for (const u of this.units) if (u.side === s && u.commander) return u;
      return null;
    }
    static dist(a, b) { return Math.abs(a.x - b.x) + Math.abs(a.y - b.y); }

    // 可移动范围（机动力 BFS）。敌军阻挡，友军可穿越但不可停留。
    distances(from, mover, limit, moverSide) {
      const W = this.W, H = this.H;
      const d = grid(W, H, 999);
      d[from.x][from.y] = 0;
      const open = [P(from.x, from.y)];
      const dirs = BattleModel.Dirs;
      while (open.length > 0) {
        let bi = 0;
        for (let i = 1; i < open.length; i++) if (d[open[i].x][open[i].y] < d[open[bi].x][open[bi].y]) bi = i;
        const c = open[bi]; open.splice(bi, 1);
        for (const dir of dirs) {
          const nx = c.x + dir.x, ny = c.y + dir.y;
          if (!this.inBounds(nx, ny)) continue;
          const cost = Defs.terrainCost(this.map[nx][ny]);
          if (cost >= 99) continue;
          if (moverSide >= 0) { const o = this.unitAt(nx, ny); if (o != null && o.side !== moverSide) continue; }
          const nd = d[c.x][c.y] + cost;
          if (nd > limit || nd >= d[nx][ny]) continue;
          d[nx][ny] = nd; open.push(P(nx, ny));
        }
      }
      return d;
    }
    moveTargets(u) {
      const d = this.distances(P(u.x, u.y), u, u.mobility, u.side);
      const list = [];
      for (let x = 0; x < this.W; x++) {
        for (let y = 0; y < this.H; y++) {
          if (d[x][y] <= u.mobility) { const o = this.unitAt(x, y); if (o == null || o === u) list.push(P(x, y)); }
        }
      }
      return list;
    }
    // 移动路径（含起点与终点），用于画面动画
    pathTo(u, to) {
      const d = this.distances(P(u.x, u.y), u, 999, u.side);
      const path = [P(to.x, to.y)];
      let cur = P(to.x, to.y);
      for (let guard = 0; guard < 64 && (cur.x !== u.x || cur.y !== u.y); guard++) {
        let best = cur; let bd = 9999;
        for (const dir of BattleModel.Dirs) {
          const n = P(cur.x + dir.x, cur.y + dir.y);
          if (!this.inBounds(n.x, n.y)) continue;
          if (d[n.x][n.y] < bd && d[n.x][n.y] + Defs.terrainCost(this.map[cur.x][cur.y]) === d[cur.x][cur.y]) { bd = d[n.x][n.y]; best = n; }
        }
        if (best === cur) break;
        cur = best; path.push(cur);
      }
      path.reverse();
      return path;
    }

    adjacentEnemies(u) { return this.units.filter(o => o.alive && o.side !== u.side && BattleModel.dist(o, u) === 1); }

    // ---------------------------------------------------------- 回合 --
    actionPoints(s) {
      const f = s === 0 ? this.setup.attacker : this.setup.defender;
      const fame = f >= 0 ? G().factions[f].fame : 40;
      return M.clamp(Balance.ActionPointsBase + M.idiv(fame, Balance.ActionPointsFameDiv), 2, Balance.ActionPointsMax);
    }
    startSide(s) {
      this.side = s; this.ap = this.actionPoints(s);
      for (const u of this.units) if (u.side === s) u.acted = u.confused > 0;
    }
    canAct(u) { return u.alive && u.side === this.side && !u.acted && this.ap > 0 && u.confused <= 0 && this.result === 0; }
    spend(u) { if (!u.acted) { u.acted = true; this.ap--; } }

    // 结束本方行动，返回 true 表示新的一天开始
    endSide() {
      if (this.side === 0) { this.startSide(1); return false; }
      this.endDay();
      this.startSide(0);
      return true;
    }

    endDay() {
      this.dayLog.length = 0;
      for (let s = 0; s < 2; s++) {
        const troops = Seq.sum(this.alive(s), u => u.troops);
        this.food[s] -= Math.max(1, M.idiv(troops, Balance.BattleFoodPerTroops));
        if (this.food[s] <= 0) {
          this.food[s] = 0;
          for (const u of this.alive(s)) u.morale = Math.max(0, u.morale - 15);
          this.dayLog.push((s === 0 ? '攻方' : '守方') + '粮草断绝，士气大跌！');
        }
      }
      for (const u of this.units.filter(x => x.alive)) {
        if (this.burning[u.x][u.y] > 0) { const d = Math.max(30, M.idiv(u.troops, 12)); u.troops -= d; this.dayLog.push(u.gen.name + '被火焰灼伤，损兵 ' + d); this.checkRout(u); }
        if (u.confused > 0) u.confused--;
      }
      for (let x = 0; x < this.W; x++) for (let y = 0; y < this.H; y++) if (this.burning[x][y] > 0) this.burning[x][y]--;
      this.day++;
      this.checkEnd();
      if (this.result === 0 && this.day > Balance.BattleDays) { this.result = 2; this.resultReason = '攻城期限已过，攻方撤退。'; }
    }

    // ---------------------------------------------------------- 行动 --
    move(u, to) { u.x = to.x; u.y = to.y; this.checkEnd(); }

    atkPower(u) {
      return u.troops * (0.5 + u.gen.war / 200) * u.form.atk * (0.6 + u.gen.training / 250) * (0.6 + u.morale / 250);
    }
    defFactor(u) { return u.form.def * Defs.terrainDef(this.map[u.x][u.y]) * (0.8 + u.gen.training / 500); }

    attack(a, t) {
      const r = { dmg: 0, counter: 0, targetRouted: false, selfRouted: false };
      r.dmg = Math.max(10, M.roundToInt(this.atkPower(a) * Balance.DamageK / this.defFactor(t) * SG.Random.rangeFloat(0.85, 1.15)));
      r.dmg = Math.min(r.dmg, t.troops);
      t.troops -= r.dmg;
      t.morale = Math.max(0, t.morale - M.roundToInt(r.dmg * 40 / Math.max(200, t.troops + r.dmg)));
      r.targetRouted = this.checkRout(t);
      if (!r.targetRouted) {
        r.counter = Math.max(5, M.roundToInt(this.atkPower(t) * Balance.DamageK * Balance.CounterRatio / this.defFactor(a) * SG.Random.rangeFloat(0.85, 1.15)));
        r.counter = Math.min(r.counter, a.troops);
        a.troops -= r.counter;
        a.morale = Math.max(0, a.morale - M.roundToInt(r.counter * 30 / Math.max(200, a.troops + r.counter)));
        r.selfRouted = this.checkRout(a);
      }
      a.morale = Math.min(100, a.morale + (r.dmg > r.counter ? 3 : 0));
      this.checkEnd();
      return r;
    }

    checkRout(u) {
      if (!u.alive) return true;
      if (u.troops < 50 || u.morale <= 0) {
        u.troops = 0; u.alive = false;
        this.setup.routed.add(u.gen.id);
        return true;
      }
      return false;
    }

    tacticUsable(u, t) {
      if (u.gen.intel < t.reqInt) return { ok: false, why: '智力不足（需 ' + t.reqInt + '）' };
      if (t.kind === TacticKind.Rockfall && this.map[u.x][u.y] !== Terrain.Hill) return { ok: false, why: '须立于山丘' };
      return { ok: true, why: '' };
    }
    tacticRange(t) { return t.kind === TacticKind.Inspire ? 0 : t.kind === TacticKind.Confuse ? 3 : 2; }
    tacticTargets(u, t) {
      if (t.kind === TacticKind.Inspire) return [u];
      const r = this.tacticRange(t);
      return this.units.filter(o => o.alive && o.side !== u.side && BattleModel.dist(o, u) <= r && (t.kind !== TacticKind.Fire || this.map[o.x][o.y] !== Terrain.River));
    }

    // 返回 {success, dmg}
    useTactic(u, t, target) {
      if (t.kind === TacticKind.Inspire) {
        for (const o of this.units) {
          if (o.alive && o.side === u.side && BattleModel.dist(o, u) <= 1) o.morale = Math.min(100, o.morale + 12 + M.idiv(u.gen.intel, 10));
        }
        return { success: true, dmg: 0 };
      }
      const p = M.clamp(0.5 + (u.gen.intel - target.gen.intel) / 110, 0.12, 0.92);
      if (SG.Random.value() > p) return { success: false, dmg: 0 };
      let dmg = 0;
      if (t.kind === TacticKind.Confuse) { target.confused = 2; target.acted = true; }
      else {
        let baseD = (t.kind === TacticKind.Fire ? 260 : 340) + u.gen.intel * (t.kind === TacticKind.Fire ? 11 : 13);
        const tt = this.map[target.x][target.y];
        if (t.kind === TacticKind.Fire) {
          if (tt === Terrain.Forest) baseD *= 1.8;
          if (tt === Terrain.Castle || tt === Terrain.Gate) baseD *= 0.6;
          this.burning[target.x][target.y] = 2;
        }
        baseD *= M.clamp(target.troops / 2500, 0.4, 1.4);
        dmg = Math.min(target.troops, M.roundToInt(baseD * SG.Random.rangeFloat(0.85, 1.15)));
        target.troops -= dmg;
        target.morale = Math.max(0, target.morale - 10);
        this.checkRout(target);
      }
      this.checkEnd();
      return { success: true, dmg };
    }

    duel(a, b) {
      const r = { accepted: false, rounds: [], winner: 0 };
      r.accepted = b.gen.war >= a.gen.war - 8 || SG.Random.value() < 0.25 || (b.commander && SG.Random.value() < 0.4);
      if (!r.accepted) { b.morale = Math.max(0, b.morale - 12); a.morale = Math.min(100, a.morale + 8); return r; }
      let hpA = 100, hpB = 100;
      for (let i = 0; i < 30 && hpA > 0 && hpB > 0; i++) {
        const who = SG.Random.value() < a.gen.war / (a.gen.war + b.gen.war) ? 0 : 1;
        const striker = who === 0 ? a : b;
        const dmg = M.roundToInt(SG.Random.rangeInt(6, 16) * (0.6 + striker.gen.war / 120));
        if (who === 0) hpB = Math.max(0, hpB - dmg); else hpA = Math.max(0, hpA - dmg);
        r.rounds.push({ who, dmg, hpA, hpB });
      }
      r.winner = hpA >= hpB ? 0 : 1;
      const loser = r.winner === 0 ? b : a;
      const winner = r.winner === 0 ? a : b;
      loser.troops = 0; loser.alive = false; this.setup.routed.add(loser.gen.id);
      for (const o of this.alive(loser.side)) o.morale = Math.max(0, o.morale - 8);
      winner.morale = Math.min(100, winner.morale + 15);
      this.checkEnd();
      return r;
    }

    changeFormation(u, f) { u.formation = f; }
    formationAllowed(u, f) { const d = Defs.formations[f]; return u.gen.intel >= d.reqInt && u.gen.war >= d.reqWar; }

    // ---------------------------------------------------------- 胜负 --
    checkEnd() {
      if (this.result !== 0) return;
      const c0 = this.commander(0); const c1 = this.commander(1);
      const castle = this.castle;
      if (this.alive(1).length === 0) { this.result = 1; this.resultReason = '守军全军覆没！'; }
      else if (this.alive(0).length === 0) { this.result = 2; this.resultReason = '攻方全军覆没！'; }
      else if (c1 != null && !c1.alive) { this.result = 1; this.resultReason = '守城主将败走，城池陷落！'; }
      else if (c0 != null && !c0.alive) { this.result = 2; this.resultReason = '攻方主将败走，全军撤退！'; }
      else if (this.alive(0).some(u => u.x === castle.x && u.y === castle.y)) { this.result = 1; this.resultReason = '攻入本城，城池陷落！'; }
    }

    // ---------------------------------------------------------- 电脑 --
    aiFormation(u) {
      let best = 0; let score = -1;
      for (let i = 0; i < Defs.formations.length; i++) {
        if (!this.formationAllowed(u, i)) continue;
        const f = Defs.formations[i];
        const s = u.side === 0 ? f.atk * 1.2 + f.def * 0.8 + f.move * 0.05 : f.atk * 0.7 + f.def * 1.3;
        if (s > score) { score = s; best = i; }
      }
      return best;
    }

    // 电脑为一个单位决定行动；返回描述行动的对象供画面播放
    planFor(u) {
      const plan = { unit: u, move: null, kind: null, target: null, tactic: null };
      const enemies = this.alive(1 - u.side);
      if (enemies.length === 0) return plan;
      const castle = this.castle;
      const dist = BattleModel.dist;
      let goal;
      if (u.side === 0) {
        const near = Seq.orderBy(enemies, e => dist(e, u))[0];
        goal = dist(near, u) <= 4 ? P(near.x, near.y) : P(castle.x, castle.y);
      } else {
        const near = Seq.orderBy(enemies, e => dist(e, u))[0];
        const threatened = enemies.some(e => Math.abs(e.x - castle.x) + Math.abs(e.y - castle.y) <= 6);
        goal = threatened || dist(near, u) <= 3 ? P(near.x, near.y) : P(u.x, u.y);
        if (u.commander && !(dist(near, u) <= 2)) goal = P(castle.x, castle.y);
      }
      // 移动
      if (this.adjacentEnemies(u).length === 0) {
        const targets = this.moveTargets(u);
        const dmap = this.distances(goal, null, 999, -1);
        let best = P(u.x, u.y); let bs = this.score(u, best, dmap, goal);
        for (const t of targets) { const s = this.score(u, t, dmap, goal); if (s < bs) { bs = s; best = t; } }
        if (best.x !== u.x || best.y !== u.y) plan.move = P(best.x, best.y);
      }
      return plan;
    }
    score(u, p, dist, goal) {
      let s = dist[p.x][p.y];
      if (s >= 999) s = Math.abs(p.x - goal.x) + Math.abs(p.y - goal.y) + 20;
      s -= Defs.terrainDef(this.map[p.x][p.y]) * 0.6;
      if (p.x === goal.x && p.y === goal.y) { const o = this.unitAt(goal.x, goal.y); if (o != null && o !== u) s += 50; }
      return s;
    }

    // 移动后决定攻击方式
    planAction(u) {
      const plan = { unit: u, move: null, kind: 'none', target: null, tactic: null };
      const adj = this.adjacentEnemies(u);
      // 策略
      for (const t of Defs.tactics) {
        if (t.kind === TacticKind.Inspire || !this.tacticUsable(u, t).ok) continue;
        const cands = this.tacticTargets(u, t);
        const tg = cands.length ? Seq.orderByDesc(cands, o => (t.kind === TacticKind.Fire && this.map[o.x][o.y] === Terrain.Forest ? 3000 : 0) + o.troops)[0] : null;
        if (tg != null && u.gen.intel > tg.gen.intel + 5 && SG.Random.value() < (u.gen.intel > 80 ? 0.6 : 0.35) && (t.kind !== TacticKind.Confuse || adj.length === 0)) {
          plan.kind = 'tactic'; plan.tactic = t; plan.target = tg; return plan;
        }
      }
      if (adj.length === 0) {
        if (u.morale < 45 && u.gen.intel >= 40) { plan.kind = 'tactic'; plan.tactic = Defs.tactics[3]; plan.target = u; }
        return plan;
      }
      const weakest = Seq.orderBy(adj, o => o.troops)[0];
      const duelCands = adj.filter(o => u.gen.war >= o.gen.war + 12);
      const duelT = duelCands.length ? Seq.orderByDesc(duelCands, o => (o.commander ? 1 : 0))[0] : null;
      if (duelT != null && SG.Random.value() < 0.3) { plan.kind = 'duel'; plan.target = duelT; return plan; }
      plan.kind = 'attack'; plan.target = weakest;
      return plan;
    }
  }
  BattleModel.Dirs = Object.freeze([P(1, 0), P(-1, 0), P(0, 1), P(0, -1)]);

  SG.BUnit = BUnit;
  SG.BattleModel = BattleModel;
})();
