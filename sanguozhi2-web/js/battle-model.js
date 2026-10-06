'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 战术战斗规则
   移植自 Battle/BattleModel.cs。网格坐标为普通对象 {x, y}（按字段比较）。

   第二版新增（DESIGN-V2 §4D 必杀技，招式数据见 js/specials.js / js/specials-data.js）：
     specialUsable(u) → { ok, why }      该部队现在能否施展必杀技（每战一次：u.specialUsed）
     specialTargets(u) → [BUnit]         可选目标（自身为中心的招式返回 [u]）
     useSpecial(u, target) → res         结算；与 attack() 一样不调用 spend(u)，由调用方随后 spend
       res = { success, name, kind, dmg, hits: [{ unit, dmg, primary }], healed: [{ unit, amount }], heal,
               confused: [BUnit], buffed: [BUnit], refreshed: [BUnit], burned: [{x,y}], doused: [{x,y}],
               moved: { from, to } | null, pushed: [{ unit, from, to }], blocked: BUnit | null,
               killed: BUnit | null, gained, routed: [BUnit], affected: [BUnit], area: [{x,y}], center: {x,y} }
     specialPlan(u, target) / specialValue(u, plan) / aiSpecial(u)   纯计算：效果清单、期望价值、电脑决策
     planAction(u) 可返回 kind 'special'（plan.target 为目标）
     限时加成 u.mods = [{ src, atk, def, counter, move, dot, days }]：atkPower / defFactor / 反击 / 机动力 /
       计策伤害都会乘上；每日结束时 days − 1（dot = 每日按兵力比例损兵，即中毒）
     duelAccepts(a, b) → bool、duelFinish(a, b, winnerIsA, rounds) → 结果：duel() 拆成的两步（随机数顺序不变）
     BattleModel.specialsEnabled（默认 true）：false 时规则与电脑完全回到第一版（tests/sim.js 对比平衡用）
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
  // 必杀技所用的能力值与武勇系数
  function statOf(gen, s) { return s === 'intel' ? gen.intel : s === 'pol' ? gen.pol : gen.war; }
  function statScale(v) { return 0.8 + M.clamp(v, 0, 120) / 500; }

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
      this.specialUsed = false;   // 本战已施展必杀技
      this.mods = [];             // 限时加成（见 BattleModel.addMod）
      if (o) Object.assign(this, o);
    }
    get troops() { return this.gen.troops; }
    set troops(v) { this.gen.troops = Math.max(0, v); }
    get form() { return Defs.formations[this.formation]; }
    get mobility() {
      let mv = 0;
      if (this.mods && this.mods.length) for (const m of this.mods) if (typeof m.move === 'number') mv += m.move;
      return 3 + this.form.move + mv;
    }
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
      // 必杀技的限时效果（中毒损兵、加成到期）
      for (const u of this.units) if (u.alive && u.mods && u.mods.length) this.tickMods(u);
      for (let x = 0; x < this.W; x++) for (let y = 0; y < this.H; y++) if (this.burning[x][y] > 0) this.burning[x][y]--;
      this.day++;
      this.checkEnd();
      if (this.result === 0 && this.day > Balance.BattleDays) { this.result = 2; this.resultReason = '攻城期限已过，攻方撤退。'; }
    }

    // ---------------------------------------------------------- 行动 --
    move(u, to) { u.x = to.x; u.y = to.y; this.checkEnd(); }

    atkPower(u) {
      return u.troops * (0.5 + u.gen.war / 200) * u.form.atk * (0.6 + u.gen.training / 250) * (0.6 + u.morale / 250) * this.modK(u, 'atk');
    }
    defFactor(u) { return u.form.def * Defs.terrainDef(this.map[u.x][u.y]) * (0.8 + u.gen.training / 500) * this.modK(u, 'def'); }

    attack(a, t) {
      const r = { dmg: 0, counter: 0, targetRouted: false, selfRouted: false };
      r.dmg = Math.max(10, M.roundToInt(this.atkPower(a) * Balance.DamageK / this.defFactor(t) * SG.Random.rangeFloat(0.85, 1.15)));
      r.dmg = Math.min(r.dmg, t.troops);
      t.troops -= r.dmg;
      t.morale = Math.max(0, t.morale - M.roundToInt(r.dmg * 40 / Math.max(200, t.troops + r.dmg)));
      r.targetRouted = this.checkRout(t);
      if (!r.targetRouted) {
        r.counter = Math.max(5, M.roundToInt(this.atkPower(t) * Balance.DamageK * Balance.CounterRatio * this.modK(t, 'counter') / this.defFactor(a) * SG.Random.rangeFloat(0.85, 1.15)));
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
        baseD /= this.modK(target, 'def');            // 坚守等防御加成（无加成时为 1）
        dmg = Math.min(target.troops, M.roundToInt(baseD * SG.Random.rangeFloat(0.85, 1.15)));
        target.troops -= dmg;
        target.morale = Math.max(0, target.morale - 10);
        this.checkRout(target);
      }
      this.checkEnd();
      return { success: true, dmg };
    }

    // 单挑：是否应战（拒绝时双方士气变化）。随机数调用与第一版 duel() 完全相同
    duelAccepts(a, b) {
      const ok = b.gen.war >= a.gen.war - 8 || SG.Random.value() < 0.25 || (b.commander && SG.Random.value() < 0.4);
      if (!ok) { b.morale = Math.max(0, b.morale - 12); a.morale = Math.min(100, a.morale + 8); }
      return ok;
    }
    // 单挑：结算胜负后果（败者溃散、其军士气 −8、胜者士气 +15）。供格斗单挑画面（js/duel-game.js）结束后调用
    duelFinish(a, b, winnerIsA, rounds) {
      const winner = winnerIsA ? a : b, loser = winnerIsA ? b : a;
      loser.troops = 0; loser.alive = false; this.setup.routed.add(loser.gen.id);
      for (const o of this.alive(loser.side)) o.morale = Math.max(0, o.morale - 8);
      winner.morale = Math.min(100, winner.morale + 15);
      this.checkEnd();
      return { accepted: true, rounds: rounds || [], winner: winnerIsA ? 0 : 1 };
    }
    duel(a, b) {
      const r = { accepted: false, rounds: [], winner: 0 };
      r.accepted = this.duelAccepts(a, b);
      if (!r.accepted) return r;
      let hpA = 100, hpB = 100;
      for (let i = 0; i < 30 && hpA > 0 && hpB > 0; i++) {
        const who = SG.Random.value() < a.gen.war / (a.gen.war + b.gen.war) ? 0 : 1;
        const striker = who === 0 ? a : b;
        const dmg = M.roundToInt(SG.Random.rangeInt(6, 16) * (0.6 + striker.gen.war / 120));
        if (who === 0) hpB = Math.max(0, hpB - dmg); else hpA = Math.max(0, hpA - dmg);
        r.rounds.push({ who, dmg, hpA, hpB });
      }
      r.winner = hpA >= hpB ? 0 : 1;
      this.duelFinish(a, b, r.winner === 0, r.rounds);
      return r;
    }

    changeFormation(u, f) { u.formation = f; }
    formationAllowed(u, f) { const d = Defs.formations[f]; return u.gen.intel >= d.reqInt && u.gen.war >= d.reqWar; }

    // ======================================================== 必杀技 --
    // 招式数据由 SG.Specials.of(gen) 提供（js/specials.js，已按机制补全默认参数）。
    // 未载入 specials.js 或 BattleModel.specialsEnabled = false 时，必杀技一律不可用，电脑也不会考虑。
    specialOf(u) {
      if (!BattleModel.specialsEnabled || !u || !u.gen || !SG.Specials || typeof SG.Specials.of !== 'function') return null;
      return SG.Specials.of(u.gen) || null;
    }

    // ---- 限时加成 ----
    // mod = { src: 来源（招式 id，同来源不叠加、刷新）, atk, def, counter（乘数）, move（加值）, dot（每日损兵比例）, days }
    modK(u, key) {
      const ms = u.mods;
      if (!ms || ms.length === 0) return 1;
      let k = 1;
      for (const m of ms) if (typeof m[key] === 'number') k *= m[key];
      return k;
    }
    addMod(u, mod) {
      if (!u.mods) u.mods = [];
      const i = u.mods.findIndex(m => m.src === mod.src);
      if (i >= 0) u.mods[i] = mod; else u.mods.push(mod);
    }
    // 每日结束时调用：中毒损兵、剩余天数 −1、到期移除
    tickMods(u) {
      for (const m of u.mods) {
        if (m.dot > 0 && u.alive) {
          const d = Math.max(20, M.roundToInt(u.troops * m.dot));
          u.troops -= d;
          this.dayLog.push(u.gen.name + '中毒，损兵 ' + d);
          this.checkRout(u);
        }
        m.days--;
      }
      u.mods = u.mods.filter(m => m.days > 0);
    }

    // ---- 计算基准 ----
    // 一次普通攻击的期望伤害（不含随机浮动）
    atkExpect(a, t) { return this.atkPower(a) * Balance.DamageK / this.defFactor(t); }
    routBonus(t) { return t.commander ? 6000 : 300; }
    // 必杀技伤害基准（期望值）。
    //   武力系（stat 'war'）：一次普通攻击的期望伤害 × 武勇系数（0.8 + 武力/500），随己方兵力、阵型、训练、士气与敌方防御变化；
    //     pierce 时无视地形防御。
    //   智力 / 政治系：与计策同量级 (200 + 能力×9) × 敌兵力系数，不随己方兵力变化；城门 / 本城内 ×0.7。
    spBase(u, t, sp) {
      const stat = sp.stat || 'war';
      const v = statOf(u.gen, stat);
      if (stat === 'intel' || stat === 'pol') {
        let d = (200 + v * 9) * M.clamp(t.troops / 2500, 0.4, 1.4) / this.modK(t, 'def');
        const tt = this.map[t.x][t.y];
        if (tt === Terrain.Castle || tt === Terrain.Gate) d *= 0.7;
        return d;
      }
      let def = this.defFactor(t);
      if (sp.pierce) def /= Defs.terrainDef(this.map[t.x][t.y]);
      return Math.max(30, this.atkPower(u) * Balance.DamageK / def * statScale(v));
    }
    // 火攻的地形修正
    fireMod(x, y) {
      const tt = this.map[x][y];
      return tt === Terrain.Forest ? 1.5 : tt === Terrain.River ? 0.5 : 1;
    }
    nearRiver(x, y) {
      if (this.inBounds(x, y) && this.map[x][y] === Terrain.River) return true;
      for (const d of BattleModel.Dirs) { const nx = x + d.x, ny = y + d.y; if (this.inBounds(nx, ny) && this.map[nx][ny] === Terrain.River) return true; }
      return false;
    }
    // 以 c 为中心、曼哈顿半径 r 内的格子
    areaTiles(c, r) {
      const out = [];
      for (let dx = -r; dx <= r; dx++) for (let dy = -r; dy <= r; dy++) {
        const x = c.x + dx, y = c.y + dy;
        if (Math.abs(dx) + Math.abs(dy) <= r && this.inBounds(x, y)) out.push(P(x, y));
      }
      return out;
    }
    maxTroopsOf(u) { return SG.maxTroops ? SG.maxTroops(u.gen) : Balance.GeneralTroopBase + u.gen.war * Balance.GeneralTroopPerWar; }
    needsHeal(b) { return b.alive && (b.troops < this.maxTroopsOf(b) * 0.95 || b.confused > 0); }
    // 直线突击：t 须与 u 同行 / 同列、距离 1..range，中间各格可通行且无部队。返回落点（敌军前一格；相邻时为原地）
    chargeLanding(u, t, range) {
      if (u.x !== t.x && u.y !== t.y) return null;
      const d = BattleModel.dist(u, t);
      if (d < 1 || d > range) return null;
      const dx = Math.sign(t.x - u.x), dy = Math.sign(t.y - u.y);
      let x = u.x, y = u.y;
      const path = [P(x, y)];
      for (let i = 1; i < d; i++) {
        x += dx; y += dy;
        if (!this.passable(x, y) || this.unitAt(x, y) != null) return null;
        path.push(P(x, y));
      }
      return { x, y, dx, dy, dash: d - 1, path };
    }
    // 疾行：可再行动的友军（已行动、未混乱），相邻敌军者优先，其次兵多者
    hasteCands(u, sp) {
      const dist = BattleModel.dist;
      const list = this.alive(u.side).filter(a => a !== u && a.acted && a.confused <= 0 && dist(a, u) <= sp.radius);
      return Seq.orderByDesc(list, a => (this.adjacentEnemies(a).length > 0 ? 100000 : 0) + a.troops).slice(0, Math.max(1, sp.count));
    }

    // ---- 目标与可用性 ----
    specialTargets(u) {
      const sp = this.specialOf(u);
      if (!sp || !u.alive) return [];
      const dist = BattleModel.dist;
      const foes = this.alive(1 - u.side), friends = this.alive(u.side);
      switch (sp.kind) {
        case 'cleave': return foes.filter(e => dist(e, u) === 1);
        case 'charge': return foes.filter(e => this.chargeLanding(u, e, sp.range) != null);
        case 'blaze': return foes.filter(e => dist(e, u) <= sp.range && this.map[e.x][e.y] !== Terrain.River);
        case 'roar': return foes.some(e => dist(e, u) <= sp.radius) ? [u] : [];
        case 'rally': case 'command': case 'fortify': return [u];
        case 'haste': return this.hasteCands(u, sp).length > 0 ? [u] : [];
        case 'heal': return friends.filter(a => dist(a, u) <= sp.range && friends.some(b => dist(a, b) <= sp.radius && this.needsHeal(b)));
        default: return foes.filter(e => dist(e, u) <= sp.range);
      }
    }
    specialUsable(u) {
      const sp = this.specialOf(u);
      if (!sp) return { ok: false, why: BattleModel.specialsEnabled && SG.Specials ? '无必杀技' : '必杀技未开放' };
      if (this.result !== 0) return { ok: false, why: '战斗已结束' };
      if (!u.alive) return { ok: false, why: '已溃散' };
      if (u.specialUsed) return { ok: false, why: '本战已施展' };
      if (u.confused > 0) return { ok: false, why: '混乱中' };
      if (this.specialTargets(u).length === 0) {
        switch (sp.kind) {
          case 'cleave': case 'roar': return { ok: false, why: '附近无敌军' };
          case 'charge': return { ok: false, why: '直线上无可突击的敌军' };
          case 'heal': return { ok: false, why: '附近无受伤友军' };
          case 'haste': return { ok: false, why: '附近无已行动的友军' };
          default: return { ok: false, why: '射程内无敌军（射程 ' + sp.range + '）' };
        }
      }
      return { ok: true, why: '' };
    }

    // ---- 效果清单（纯计算：不改状态、不耗随机数） ----
    // 返回 plan = { sp, kind, unit, target, center, area, dmg: [{unit, amt, primary, onFail}], foe: [{unit, morale, p, turns, mod}],
    //               ally: [{unit, morale, heal, cure, mod}], burn, douse, refresh, ap, move, push, blocked, kill, drain, pool, seq }
    specialPlan(u, target) {
      const sp = this.specialOf(u);
      if (!sp) return null;
      const dist = BattleModel.dist;
      const t = target || u;
      const foes = this.alive(1 - u.side), friends = this.alive(u.side);
      const pl = {
        sp, kind: sp.kind, unit: u, target: t, center: P(t.x, t.y), area: [], dmg: [], foe: [], ally: [],
        burn: [], douse: [], refresh: [], ap: 0, move: null, push: null, blocked: null, kill: null, drain: 0, pool: null, seq: false,
      };
      const W = u.gen.war, I = u.gen.intel;
      const sv = statOf(u.gen, sp.stat || 'war');
      const kS = 0.75 + sv / 400;                     // 辅助类招式的能力系数（能力 100 → 1.0）
      const base = e => this.spBase(u, e, sp);
      const hit = (e, amt, primary, onFail) => pl.dmg.push({ unit: e, amt: Math.max(0, amt), primary: !!primary, onFail: !!onFail });
      const foeFx = (e, o) => {
        let f = pl.foe.find(q => q.unit === e);
        if (!f) { f = { unit: e, morale: 0, p: 0, turns: 0, mod: null }; pl.foe.push(f); }
        if (o.morale) f.morale += o.morale;
        if (o.p > 0) { f.p = 1 - (1 - f.p) * (1 - M.clamp01(o.p)); f.turns = Math.max(f.turns, o.turns || 1); }
        if (o.mod) f.mod = o.mod;
      };
      const allyFx = (a, o) => {
        let f = pl.ally.find(q => q.unit === a);
        if (!f) { f = { unit: a, morale: 0, heal: 0, cure: false, mod: null }; pl.ally.push(f); }
        if (o.morale) f.morale += o.morale;
        if (o.heal) f.heal += o.heal;
        if (o.cure) f.cure = true;
        if (o.mod) f.mod = o.mod;
      };
      // 命中敌军的通用附加：士气、混乱（概率按所用能力之差修正）
      const extras = e => {
        if (sp.morale) foeFx(e, { morale: sp.morale });
        if (sp.confuse > 0) {
          const diff = (sp.stat === 'intel' || sp.stat === 'pol') ? I - e.gen.intel : W - e.gen.war;
          foeFx(e, { p: M.clamp(sp.confuse * (1 + diff / 120), 0, 0.9), turns: sp.turns });
        }
      };
      const healAmt = a => {
        const max = Math.max(this.maxTroopsOf(a), a.troops);
        return Math.max(0, Math.min(max - a.troops, M.roundToInt(sp.heal * this.maxTroopsOf(a) * kS)));
      };
      const burnable = (x, y) => this.passable(x, y) && this.map[x][y] !== Terrain.River && !friends.some(a => a.x === x && a.y === y);
      const src = sp.id || sp.name;
      switch (sp.kind) {
        case 'smite': case 'drain':
          hit(t, sp.power * base(t), true); extras(t);
          pl.area = [P(t.x, t.y)];
          if (sp.kind === 'drain') pl.drain = sp.drain * (0.7 + u.gen.pol / 333);
          break;
        case 'cleave':
          hit(t, sp.power * base(t), true); extras(t);
          for (const e of foes) if (e !== t && dist(e, u) === 1) { hit(e, sp.power * sp.splash * base(e), false); extras(e); }
          pl.center = P(u.x, u.y); pl.area = this.areaTiles(u, 1);
          break;
        case 'charge': {
          const L = this.chargeLanding(u, t, sp.range);
          if (!L) break;
          if (L.x !== u.x || L.y !== u.y) pl.move = { from: P(u.x, u.y), to: P(L.x, L.y) };
          let amt = sp.power * base(t) * (1 + sp.dash * L.dash);
          if (sp.push) {
            const bx = t.x + L.dx, by = t.y + L.dy;
            if (this.passable(bx, by) && this.unitAt(bx, by) == null && this.map[bx][by] !== Terrain.Castle) pl.push = { unit: t, from: P(t.x, t.y), to: P(bx, by) };
            else { amt *= 1.25; pl.blocked = t; }               // 退无可退：撞击伤害 +25%
          }
          hit(t, amt, true); extras(t);
          pl.area = L.path.concat([P(t.x, t.y)]);
          break;
        }
        case 'rampage': {
          const others = Seq.orderBy(foes.filter(e => e !== t && dist(e, u) <= sp.range), e => dist(e, u) * 100000 - e.troops);
          const pool = pl.pool = [t].concat(others);
          pl.seq = true;
          for (let i = 0; i < sp.strikes; i++) { const e = pool[i % pool.length]; hit(e, sp.power * base(e), i === 0); }
          for (const e of pool.slice(0, Math.min(pool.length, sp.strikes))) extras(e);
          pl.area = pool.slice(0, Math.min(pool.length, sp.strikes)).map(e => P(e.x, e.y));
          break;
        }
        case 'volley':
          hit(t, sp.power * base(t), true); extras(t);
          for (const e of foes) if (e !== t && sp.radius > 0 && dist(e, t) <= sp.radius) { hit(e, sp.power * sp.splash * base(e), false); extras(e); }
          pl.area = this.areaTiles(t, sp.radius);
          break;
        case 'blaze':
          pl.area = this.areaTiles(t, sp.radius);
          for (const e of foes) if (dist(e, t) <= sp.radius) { hit(e, sp.power * (e === t ? 1 : sp.splash) * base(e) * this.fireMod(e.x, e.y), e === t); extras(e); }
          if (sp.burn > 0) for (const p of pl.area) if (burnable(p.x, p.y)) pl.burn.push({ x: p.x, y: p.y, days: sp.burn });
          break;
        case 'storm':
          pl.area = this.areaTiles(t, sp.radius);
          for (const e of foes) if (dist(e, t) <= sp.radius) {
            hit(e, sp.power * base(e) * (sp.burn > 0 ? this.fireMod(e.x, e.y) : 1), e === t); extras(e);
            if (sp.burn > 0 && burnable(e.x, e.y)) pl.burn.push({ x: e.x, y: e.y, days: sp.burn });
          }
          break;
        case 'flood':
          pl.area = this.areaTiles(t, sp.radius);
          for (const e of foes) if (dist(e, t) <= sp.radius) { hit(e, sp.power * base(e) * (this.nearRiver(e.x, e.y) ? 1.6 : 0.85), e === t); extras(e); }
          for (const p of pl.area) if (this.burning[p.x][p.y] > 0) pl.douse.push(P(p.x, p.y));
          break;
        case 'roar':
          pl.center = P(u.x, u.y); pl.area = this.areaTiles(u, sp.radius);
          for (const e of foes) if (dist(e, u) <= sp.radius) {
            if (sp.power > 0) hit(e, sp.power * base(e), false);
            foeFx(e, { morale: M.roundToInt(sp.morale * (0.75 + W / 400)) });
            if (sp.confuse > 0) foeFx(e, { p: M.clamp(sp.confuse * (1 + (W - e.gen.war) / 100), 0, 0.9), turns: sp.turns });
          }
          break;
        case 'rally':
          pl.center = P(u.x, u.y); pl.area = this.areaTiles(u, sp.radius);
          for (const a of friends) if (dist(a, u) <= sp.radius) {
            allyFx(a, { morale: M.roundToInt(sp.morale * kS), heal: healAmt(a), cure: sp.cure > 0 });
            if (sp.atk > 1) allyFx(a, { mod: { src, atk: 1 + (sp.atk - 1) * kS, days: sp.turns } });
          }
          break;
        case 'command':
          pl.center = P(u.x, u.y); pl.area = this.areaTiles(u, sp.radius);
          for (const a of friends) if (dist(a, u) <= sp.radius)
            allyFx(a, { morale: M.roundToInt(sp.morale * kS), mod: { src, atk: 1 + (sp.atk - 1) * kS, def: 1 + (sp.def - 1) * kS, days: sp.turns } });
          break;
        case 'fortify':
          pl.center = P(u.x, u.y); pl.area = this.areaTiles(u, sp.radius);
          for (const a of friends) if (dist(a, u) <= sp.radius)
            allyFx(a, { morale: M.roundToInt(sp.morale * kS), mod: { src, def: 1 + (sp.def - 1) * kS, counter: 1 + (sp.counter - 1) * kS, days: sp.turns } });
          break;
        case 'haste':
          pl.center = P(u.x, u.y); pl.area = this.areaTiles(u, sp.radius);
          pl.refresh = this.hasteCands(u, sp);
          pl.ap = pl.refresh.length;
          for (const a of friends) if (a !== u && dist(a, u) <= sp.radius)
            allyFx(a, { morale: M.roundToInt(sp.morale * kS), mod: sp.move > 0 ? { src, move: sp.move, days: 1 } : null });
          break;
        case 'scheme':
          pl.area = this.areaTiles(t, sp.radius);
          for (const e of foes) if (dist(e, t) <= sp.radius) {
            if (sp.power > 0) hit(e, sp.power * base(e), e === t);
            foeFx(e, { morale: sp.morale, p: M.clamp(sp.chance + (I - e.gen.intel) / 110, 0.08, 0.95), turns: sp.turns });
          }
          break;
        case 'heal':
          pl.area = this.areaTiles(t, sp.radius);
          for (const a of friends) if (dist(a, t) <= sp.radius) allyFx(a, { heal: healAmt(a), morale: M.roundToInt(sp.morale * kS), cure: sp.cure > 0 });
          break;
        case 'assassinate': {
          // 武将行刺看武力差，谋士鸩杀看智力差
          let p = sp.stat === 'intel'
            ? M.clamp(sp.chance + (I - t.gen.intel) / 200 + (W - t.gen.war) / 500, 0.05, 0.6)
            : M.clamp(sp.chance + (W - t.gen.war) / 250 + (I - t.gen.intel) / 300, 0.05, 0.6);
          if (t.commander) p *= 0.5;
          pl.kill = { unit: t, p };
          if (sp.power > 0) hit(t, sp.power * base(t), true, true);   // 失手时造成的伤害
          if (sp.morale) foeFx(t, { morale: sp.morale });
          pl.area = [P(t.x, t.y)];
          break;
        }
        case 'poison':
          pl.area = this.areaTiles(t, sp.radius);
          for (const e of foes) if (dist(e, t) <= sp.radius) {
            if (sp.power > 0) hit(e, sp.power * base(e) * (e === t ? 1 : 0.8), e === t);
            foeFx(e, { morale: sp.morale, mod: { src, dot: sp.dot * (0.8 + I / 500), days: sp.turns } });
          }
          break;
        default:
          break;
      }
      return pl;
    }

    // 电脑用：一份效果清单的期望价值（折算为兵力）
    specialValue(u, pl) {
      if (!pl) return 0;
      const dist = BattleModel.dist;
      const foes = this.alive(1 - u.side);
      const nearestFoe = a => { let b = null, bd = 1e9; for (const e of foes) { const d = dist(e, a); if (d < bd) { bd = d; b = e; } } return b; };
      let v = 0;
      const left = new Map();
      const killP = pl.kill ? pl.kill.p : 0;
      for (const h of pl.dmg) {
        const e = h.unit;
        const rem = left.has(e) ? left.get(e) : e.troops;
        if (rem <= 0) continue;
        const d = Math.min(rem, h.amt) * (h.onFail ? 1 - killP : 1);
        v += d;
        left.set(e, rem - d);
        if (rem - d < 50) v += this.routBonus(e);
      }
      if (pl.kill) { const e = pl.kill.unit; v += pl.kill.p * (e.troops + this.routBonus(e)); }
      for (const f of pl.foe) {
        const e = f.unit;
        const rem = left.has(e) ? left.get(e) : e.troops;
        if (rem <= 0) continue;
        if (f.morale < 0) {
          v += -f.morale * rem / 300;
          if (e.morale + f.morale <= 0) v += rem + this.routBonus(e);
        }
        if (f.p > 0) v += f.p * f.turns * 0.8 * this.threat(e, u);
        if (f.mod && f.mod.dot > 0) v += f.mod.days * Math.max(20, rem * f.mod.dot) * 0.9;
      }
      for (const a of pl.ally) {
        const x = a.unit;
        v += a.heal * 0.9;
        const engaged = foes.some(e => dist(e, x) <= 5);
        if (a.morale > 0) v += Math.min(a.morale, 100 - x.morale) * x.troops / 300 * (x.morale < 40 ? 2 : 1) * (engaged ? 1 : 0.3);
        if (a.cure && x.confused > 0) v += x.confused * 0.5 * this.atkExpect(x, nearestFoe(x) || x);
        const m = a.mod;
        if (m) {
          const near = foes.filter(e => dist(e, x) <= 4);
          if (near.length > 0) {
            const ne = nearestFoe(x);
            if (m.atk > 1) v += (m.atk - 1) * this.atkExpect(x, ne) * m.days * 0.8;
            if (m.def > 1) { let inc = 0; for (const e of near) inc += this.atkExpect(e, x) * 0.5; v += inc * (1 - 1 / m.def) * m.days * 0.8; }
            if (m.counter > 1) v += (m.counter - 1) * this.atkExpect(x, ne) * Balance.CounterRatio * Math.min(2, near.length) * m.days * 0.5;
            if (m.move > 0) v += 30;
          }
        }
      }
      for (const r of pl.refresh) {
        const adj = this.adjacentEnemies(r);
        if (adj.length > 0) v += Math.max(...adj.map(e => Math.min(e.troops, this.atkExpect(r, e))));
        else { const ne = nearestFoe(r); if (ne && dist(ne, r) <= r.mobility + 1) v += 0.5 * this.atkExpect(r, ne); }
      }
      for (const b of pl.burn) { const e = foes.find(q => q.x === b.x && q.y === b.y); if (e) v += b.days * Math.max(30, e.troops / 12) * 0.6; }
      if (pl.drain > 0) for (const h of pl.dmg) if (h.primary) v += h.amt * pl.drain * 0.7;
      return v;
    }

    // 敌军 e 每回合对 t 的威胁（普通攻击，或其火计期望的一半——电脑并非每回合都用计）
    threat(e, t) {
      let v = Math.min(t.troops, this.atkExpect(e, t));
      if (this.tacticUsable(e, Defs.tactics[0]).ok) v = Math.max(v, 0.5 * M.clamp(0.5 + (e.gen.intel - t.gen.intel) / 110, 0.12, 0.92) * (260 + e.gen.intel * 11) * M.clamp(t.troops / 2500, 0.4, 1.4));
      return v;
    }
    // 电脑：不用必杀技时最好的一手（普通攻击扣除反击 / 火计期望）
    aiNormalValue(u) {
      let best = 0;
      for (const t of this.adjacentEnemies(u)) {
        const d = Math.min(t.troops, this.atkExpect(u, t));
        const c = Math.min(u.troops, this.atkExpect(t, u) * Balance.CounterRatio * this.modK(t, 'counter'));
        best = Math.max(best, d - 0.5 * c + (t.troops - d < 50 ? this.routBonus(t) : 0));
      }
      const fire = Defs.tactics[0];
      if (this.tacticUsable(u, fire).ok) {
        for (const t of this.tacticTargets(u, fire)) {
          const p = M.clamp(0.5 + (u.gen.intel - t.gen.intel) / 110, 0.12, 0.92);
          best = Math.max(best, p * Math.min(t.troops, (260 + u.gen.intel * 11) * M.clamp(t.troops / 2500, 0.4, 1.4)));
        }
      }
      return best;
    }

    // 电脑：划算时施展必杀技 → { target, value } 或 null。越到战斗后期、己方越危急，门槛越低
    aiSpecial(u) {
      if (!BattleModel.specialsEnabled || !this.specialUsable(u).ok) return null;
      let best = null, bv = 0;
      for (const t of this.specialTargets(u)) {
        const v = this.specialValue(u, this.specialPlan(u, t));
        if (v > bv) { bv = v; best = t; }
      }
      if (!best) return null;
      const normal = this.aiNormalValue(u);
      let k = this.day >= 20 ? 1.05 : this.day >= 10 ? 1.3 : 1.5;
      if (u.troops < this.maxTroopsOf(u) * 0.3 || u.morale < 30) k = Math.min(k, 1.0);
      return bv >= Math.max(normal * k, 260) ? { target: best, value: bv } : null;
    }

    // ---- 结算 ----
    useSpecial(u, target) {
      const res = {
        success: false, name: '', kind: '', sp: null, why: '', dmg: 0, hits: [], healed: [], heal: 0, confused: [], resisted: [], cursed: [],
        buffed: [], refreshed: [], burned: [], doused: [], moved: null, pushed: [], blocked: null, killed: null, gained: 0,
        routed: [], affected: [], area: [], center: null, target: null,
      };
      const sp = this.specialOf(u);
      if (!sp) { res.why = '无必杀技'; return res; }
      res.name = sp.name; res.kind = sp.kind; res.sp = sp;
      const us = this.specialUsable(u);
      if (!us.ok) { res.why = us.why; return res; }
      const cands = this.specialTargets(u);
      if (!target || cands.indexOf(target) < 0) target = cands[0];
      const pl = this.specialPlan(u, target);
      u.specialUsed = true;
      res.success = true; res.target = target; res.center = pl.center; res.area = pl.area;
      const touch = x => { if (res.affected.indexOf(x) < 0) res.affected.push(x); };
      const routed = x => { if (res.routed.indexOf(x) < 0) res.routed.push(x); };
      // 1. 突击位移
      if (pl.move) { u.x = pl.move.to.x; u.y = pl.move.to.y; res.moved = pl.move; }
      // 2. 暗杀
      let killed = false;
      if (pl.kill) {
        const k = pl.kill.unit;
        if (k.alive && SG.Random.value() < pl.kill.p) {
          const d = k.troops;
          k.troops = 0; k.alive = false; this.setup.routed.add(k.gen.id);
          res.killed = k; res.dmg += d; res.hits.push({ unit: k, dmg: d, primary: true, killed: true });
          routed(k); touch(k); killed = true;
        } else res.success = false;                     // 失手（仍可能造成少量伤害）
      }
      // 3. 伤害（连斩：目标已溃散则转向下一个敌军）
      let primaryDmg = 0;
      for (const h of pl.dmg) {
        if (h.onFail && killed) continue;
        let e = h.unit, amt = h.amt;
        if (!e.alive) {
          if (!pl.seq) continue;
          e = pl.pool.find(q => q.alive);
          if (!e) continue;
          amt = sp.power * this.spBase(u, e, sp);
        }
        const d = Math.min(e.troops, Math.max(10, M.roundToInt(amt * SG.Random.rangeFloat(0.92, 1.08))));
        e.troops -= d;
        e.morale = Math.max(0, e.morale - M.roundToInt(d * 40 / Math.max(200, e.troops + d)));
        res.dmg += d;
        if (h.primary) primaryDmg += d;
        res.hits.push({ unit: e, dmg: d, primary: h.primary });
        touch(e);
        if (this.checkRout(e)) routed(e);
      }
      // 4. 击退
      if (pl.push && pl.push.unit.alive) {
        const pu = pl.push.unit;
        pu.x = pl.push.to.x; pu.y = pl.push.to.y;
        res.pushed.push({ unit: pu, from: pl.push.from, to: pl.push.to });
      }
      if (pl.blocked) res.blocked = pl.blocked;
      // 5. 敌军：士气、混乱、中毒
      for (const f of pl.foe) {
        const e = f.unit;
        if (!e.alive) continue;
        touch(e);
        if (f.morale) e.morale = M.clamp(e.morale + f.morale, 0, 100);
        if (f.p > 0) {
          if (SG.Random.value() < f.p) { e.confused = Math.max(e.confused, f.turns); e.acted = true; res.confused.push(e); }
          else res.resisted.push(e);
        }
        if (f.mod) { this.addMod(e, Object.assign({}, f.mod)); res.cursed.push(e); }
        if (this.checkRout(e)) routed(e);
      }
      // 6. 友军：回复、士气、解除混乱、加成
      for (const a of pl.ally) {
        const x = a.unit;
        if (!x.alive) continue;
        touch(x);
        if (a.heal > 0) { x.troops += a.heal; res.healed.push({ unit: x, amount: a.heal }); res.heal += a.heal; }
        if (a.morale) x.morale = M.clamp(x.morale + a.morale, 0, 100);
        if (a.cure && x.confused > 0) x.confused = 0;
        if (a.mod) { this.addMod(x, Object.assign({}, a.mod)); res.buffed.push(x); }
      }
      // 7. 疾行：再行动
      for (const r of pl.refresh) if (r.alive && r.acted) { r.acted = false; res.refreshed.push(r); touch(r); }
      this.ap += res.refreshed.length;
      // 8. 地形：起火 / 灭火
      for (const b of pl.burn) { this.burning[b.x][b.y] = Math.max(this.burning[b.x][b.y], b.days); res.burned.push(P(b.x, b.y)); }
      for (const d of pl.douse) { this.burning[d.x][d.y] = 0; res.doused.push(P(d.x, d.y)); }
      // 9. 收编
      if (pl.drain > 0 && primaryDmg > 0) {
        const g = Math.max(0, Math.min(Math.max(0, this.maxTroopsOf(u) - u.troops), M.roundToInt(primaryDmg * pl.drain)));
        u.troops += g; res.gained = g;
      }
      if (res.dmg > 0) u.morale = Math.min(100, u.morale + 6);
      this.checkEnd();
      return res;
    }

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
      // 必杀技：划算时优先（不耗随机数；specialsEnabled = false 时完全跳过，行为与第一版一致）
      if (BattleModel.specialsEnabled) {
        const sp = this.aiSpecial(u);
        if (sp) { plan.kind = 'special'; plan.target = sp.target; plan.value = sp.value; return plan; }
      }
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
  BattleModel.specialsEnabled = true;   // false：不使用必杀技（tests/sim.js 平衡对比）

  SG.BUnit = BUnit;
  SG.BattleModel = BattleModel;
})();
