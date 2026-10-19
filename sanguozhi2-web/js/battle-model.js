'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 战术战斗规则
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
       计策伤害都会乘上；dot（每日按兵力比例损兵，即中毒）在每日结束时发作并 days − 1，其余加成在该部队
       本方行动开始时 days − 1（攻守双方施展的持续时间对称）。混乱同理按 confuseCount 补正，见 confusedPhases
     duelAccepts(a, b) → bool、duelFinish(a, b, winnerIsA, rounds) → 结果：duel() 拆成的两步（随机数顺序不变）
     BattleModel.specialsEnabled（默认 true）：false 时规则与电脑完全回到第一版（tests/sim.js 对比平衡用）

   第三版（DESIGN-V3 §4 成长；成长规则见 js/model.js 的 SG.Progress，计策目录见 js/tactics-data.js）：
     必杀：一支部队可会多招（专属必杀 + 习得的可习得必杀），每招每战一次。
       knownSpecials(u) → [sp]（专属在前）· specialOf(u, id?) · specialUsable(u, id?) · specialTargets(u, id?) ·
       specialPlan(u, target, id?) · useSpecial(u, target, id?)
       id 缺省时：u.spPick（选招菜单或电脑记下的那一招）→ 否则第一招可施展的。specialUsable(u) 不带 id 且未选招时
       回答「是否还有任何一招可用」（「必杀」菜单项用）。已施展的招式记在 u.spUsed（编号数组）；专属必杀施展后
       u.specialUsed 仍为 true（与第二版相同）。aiSpecial(u) → { target, value, special } —— 电脑在全部已会的招式中
       挑最划算的；planAction 的 'special' 计划带 plan.special，并记入 u.spPick（旧控制器不传编号也能用对招）。
     计策：knownTactics(u) → 该部队会的计策（原四计按智力 + 习得的新计策，见 SG.Tactics.known）。
       原四计（kind 为数字）的 tacticUsable / tacticRange / tacticTargets / useTactic 与第一版完全相同；
       新计策（kind 为字符串）：tacticUsable 另查是否习得、冷却（u.tacReady[id] = 可再用的日子）、军粮、地形需求；
       tacticChance(u, t, target) 成功率；tacticPlan(u, t, target) 效果清单（与 specialPlan 同结构）；
       useTactic(u, t, target) → 与 useSpecial 相同结构的 res（另有 res.tactic；失败时 success = false、resisted = [目标]）；
       tacticValue(u, t, target) / aiTactic(u)：电脑按期望价值使用新计策（不耗随机数；原四计的随机判定照旧）。
     经验：每支部队累计 u.xp = { dmg, heal, routs, cmdRouts, duelWin, duelLoss, specials, tactics, tacFail }；
       分出胜负时（checkEnd / 期限）自动结算 SG.Progress.battleExp → SG.Progress.gain，结果在
       Mdl.progress = setup.progress = [{ gen, side, won, exp, from, to, learned, xp }]（电脑武将已自动习得 learned；
       玩家武将见 SG.Progress.pending(G.player)）。setup.progressDone = true，防止同一场再被自动结算计一次。
     载入时包装 SG.Conquest.autoResolve（SG.Progress.hookConquest）：电脑之间自动结算的攻城战也给经验。

   第四版（COMBAT-V4 §2 军队会心；INTEGRATION §3.2 战斗乘数，公式在 js/battle-math.js 的 SG.Mods）：
     attack(a, t)：最先取恰好一个 SG.Random.value() 判定会心（SG.Mods.rollCrit；会心关闭时照取，随机数流不变），
       会心 → 伤害 × critK（1.5，装备至多 1.8）、目标士气再 −5、a.xp.crits++；返回 r.crit / r.critK / r.critP。
       反击、计策、必杀、自动结算永不会心。没有 js/econ.js（如旧的 Node 测试）时不取这个随机数，与第三版完全相同。
     atkPower × SG.Mods.atkMul、defFactor × SG.Mods.defMul（装备 × 官职 × 羁绊 × 光环，各自 ≤ ×1.60）；出阵士气 + 装备 + 官职（≤ 25）；
     行动点 = SG.Mods.actionPoints（人望封顶）。critChance(u) · BattleModel.critChanceOf(gen, formationIdx)（人物菜单）·
     BattleModel.critEnabled（测试开关）· atkExpectCrit（电脑的普通攻击估值含会心期望）· 电脑选阵型时攻方另加 FORM_CRIT × 0.5 × 攻。
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
      this.specialUsed = false;   // 本战已施展专属必杀
      this.spUsed = [];           // 本战已施展的全部必杀（编号）
      this.spPick = null;         // 选招菜单 / 电脑选定、尚未施展的必杀编号
      this.tacReady = {};         // 新计策的冷却：编号 → 可再用的日子
      this.mods = [];             // 限时加成（见 BattleModel.addMod）
      this.xp = { dmg: 0, heal: 0, routs: 0, cmdRouts: 0, duelWin: 0, duelLoss: 0, specials: 0, tactics: 0, tacFail: 0, crits: 0 };   // 本战经验来源（第四版 + crits）
      if (o) Object.assign(this, o);
    }
    get troops() { return this.gen.troops; }
    set troops(v) { this.gen.troops = Math.max(0, v); }
    get form() { return Defs.formations[this.formation]; }
    get mobility() {
      let mv = 0;
      if (this.mods && this.mods.length) for (const m of this.mods) if (typeof m.move === 'number') mv += m.move;
      return Math.max(1, 3 + this.form.move + mv);   // 疑兵等削弱机动力时至少 1
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
      this.progress = null;        // 第三版：战后经验结算（settleProgress）
      this.progressDone = false;

      const g = G();
      // 第三版 §2：战场取自城池真实周边并按来向旋转（SG.BattleTerrain）；不可用时（如 Node 测试）退回旧的随机生成
      if (!(SG.BattleTerrain && SG.BattleTerrain.applyTo(this, s))) this.generate(s.target.id * 31 + g.year);
      this.food[0] = s.atkFood; this.food[1] = s.target.food;
      for (let i = 0; i < s.atk.length; i++) {
        const u = new BUnit({ gen: s.atk[i], side: 0, x: 0, y: 0 });
        u.troops0 = s.atk[i].troops;   // 出阵兵力：回复类必杀不得超过此数（非免费征兵）
        u.commander = i === 0 || g.isRuler(s.atk[i]);
        u.morale = startMorale(u, 60 + M.idiv(s.atk[i].training, 5));   // 第四版：+ 装备 + 官职（≤ 25，INTEGRATION §3.2）
        this.units.push(u);
      }
      if (this.units.filter(u => u.side === 0 && u.commander).length > 1) {
        for (const u of this.units) if (u.side === 0 && !g.isRuler(u.gen)) u.commander = false;
      }
      for (let i = 0; i < s.def.length; i++) {
        const u = new BUnit({ gen: s.def[i], side: 1 });
        u.troops0 = s.def[i].troops;
        u.commander = i === 0;
        u.morale = startMorale(u, 65 + M.idiv(s.def[i].training, 5));
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
      if (SG.Mods && SG.Mods.actionPoints) return SG.Mods.actionPoints(f);   // 第四版：人望 = SG.Mods.fame（声望 + 事件 + 装备 ≤ +10，INTEGRATION §3.4）
      const fame = f >= 0 ? G().factions[f].fame : 40;
      return M.clamp(Balance.ActionPointsBase + M.idiv(fame, Balance.ActionPointsFameDiv), 2, Balance.ActionPointsMax);
    }
    startSide(s) {
      this.side = s; this.ap = this.actionPoints(s);
      for (const u of this.units) if (u.side === s) { u.acted = u.confused > 0; u.spPick = null; }
      for (const u of this.units) if (u.side === s && u.mods && u.mods.length) this.expireMods(u);   // 必杀技加成按本方行动计时
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
      if (this.result === 0 && this.day > Balance.BattleDays) { this.result = 2; this.resultReason = '攻城期限已过，攻方撤退。'; this.settleProgress(); }
    }

    // ---------------------------------------------------------- 行动 --
    move(u, to) { u.x = to.x; u.y = to.y; this.checkEnd(); }

    atkPower(u) {
      return u.troops * (0.5 + u.gen.war / 200) * u.form.atk * (0.6 + u.gen.training / 250) * (0.6 + u.morale / 250) * this.modK(u, 'atk') * this.vetK(u) * this.v4Atk(u);
    }
    defFactor(u) { return u.form.def * Defs.terrainDef(this.map[u.x][u.y]) * (0.8 + u.gen.training / 500) * this.modK(u, 'def') * this.vetK(u) * this.v4Def(u); }
    // 第四版：装备 × 官职 / 主将 × 羁绊 × 光环的乘积（SG.Mods.atkMul / defMul，js/battle-math.js；各自 ≤ ×1.60、负向 ≥ ×0.5；
    // 没有 js/econ.js 或没有提供者时为 1）。clash.js 与人物菜单读同样的数字（SG.BattleMath.unitPreview）。
    v4Atk(u) { const Mo = SG.Mods; return Mo && Mo.atkMul ? Mo.atkMul(u, this) : 1; }
    v4Def(u) { const Mo = SG.Mods; return Mo && Mo.defMul ? Mo.defMul(u, this) : 1; }
    // 第四版：军队会心率（COMBAT-V4 §2.1；人物菜单用 BattleModel.critChanceOf）
    critChance(u) { const Mo = SG.Mods; return Mo && Mo.critChance ? Mo.critChance(u) : 0; }
    // 第三版：老练加成（SG.Progress.vetK：每高 1 级攻、防、计策威力 +4%；1 级与成长关闭时为 1，与第一版相同）
    vetK(u) { const P = SG.Progress; return P && P.vetK && u.gen ? P.vetK(u.gen) : 1; }
    // 计策 / 智力系必杀的威力：施计者老练 ÷ 目标老练
    tacK(u, e) { return this.vetK(u) / this.vetK(e); }

    attack(a, t) {
      const r = { dmg: 0, counter: 0, targetRouted: false, selfRouted: false, crit: false, critK: 1, critP: 0 };
      // 第四版：会心判定最先取恰好一个随机数（COMBAT-V4 §2.2；关掉会心时照取，开 / 关的对局一一配对）
      const cr = SG.Mods && SG.Mods.rollCrit ? SG.Mods.rollCrit(a) : null;
      if (cr) { r.crit = cr.crit; r.critK = cr.k; r.critP = cr.p; }
      r.dmg = Math.max(10, M.roundToInt(this.atkPower(a) * Balance.DamageK / this.defFactor(t) * SG.Random.rangeFloat(0.85, 1.15) * r.critK));
      r.dmg = Math.min(r.dmg, t.troops);
      t.troops -= r.dmg;
      t.morale = Math.max(0, t.morale - M.roundToInt(r.dmg * 40 / Math.max(200, t.troops + r.dmg)) - (r.crit ? Balance.CritMorale : 0));
      if (r.crit && a.xp) a.xp.crits++;
      if (r.crit && a.gen) a.gen.crits = (a.gen.crits | 0) + 1;   // 存档：武将的军队会心次数（INTEGRATION §6.1 gen.crits；人物菜单显示）
      r.targetRouted = this.checkRout(t);
      // 第四版 ITEMS §8.2 阵斩：会心击溃君主的部队时 25 %（SG.Items.rollSlay：只在阵斩概率 > 0 时取一个随机数——缺省只限君主；
      // 装备系统关掉时不取，随机数流与第三版相同）
      if (r.targetRouted && r.crit && slayOn()) r.slain = !!SG.Items.rollSlay(t.gen, 'critRout', a.gen.faction);
      if (!r.targetRouted) {
        r.counter = Math.max(5, M.roundToInt(this.atkPower(t) * Balance.DamageK * Balance.CounterRatio * this.modK(t, 'counter') / this.defFactor(a) * SG.Random.rangeFloat(0.85, 1.15)));
        r.counter = Math.min(r.counter, a.troops);
        a.troops -= r.counter;
        a.morale = Math.max(0, a.morale - M.roundToInt(r.counter * 30 / Math.max(200, a.troops + r.counter)));
        r.selfRouted = this.checkRout(a);
      }
      a.morale = Math.min(100, a.morale + (r.dmg > r.counter ? 3 : 0));
      this.xpHit(a, t, r.dmg);
      if (r.counter > 0) this.xpHit(t, a, r.counter);
      this.checkEnd();
      return r;
    }

    // 经验来源：u 对 victim 造成 dmg（victim 因此溃散时记一次击溃，每支部队只记一次）
    xpHit(u, victim, dmg) {
      if (!u || !u.xp) return;
      u.xp.dmg += Math.max(0, dmg | 0);
      if (victim && !victim.alive && !victim._xpRouted && victim.side !== u.side) {
        victim._xpRouted = true;
        u.xp.routs++;
        if (victim.commander) u.xp.cmdRouts++;
      }
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
      if (typeof t.kind === 'string') return this.newTacticUsable(u, t);
      if (u.gen.intel < t.reqInt) return { ok: false, why: '智力不足（需 ' + t.reqInt + '）' };
      if (t.kind === TacticKind.Rockfall && this.map[u.x][u.y] !== Terrain.Hill) return { ok: false, why: '须立于山丘' };
      return { ok: true, why: '' };
    }
    tacticRange(t) { if (typeof t.kind === 'string') return t.range | 0; return t.kind === TacticKind.Inspire ? 0 : t.kind === TacticKind.Confuse ? 3 : 2; }
    tacticTargets(u, t) {
      if (typeof t.kind === 'string') return this.newTacticTargets(u, t);
      if (t.kind === TacticKind.Inspire) return [u];
      const r = this.tacticRange(t);
      return this.units.filter(o => o.alive && o.side !== u.side && BattleModel.dist(o, u) <= r && (t.kind !== TacticKind.Fire || this.map[o.x][o.y] !== Terrain.River));
    }

    // 返回 {success, dmg}（新计策返回与 useSpecial 同结构的 res，见 useNewTactic）
    useTactic(u, t, target) {
      if (typeof t.kind === 'string') return this.useNewTactic(u, t, target);
      const r = this.useTactic1(u, t, target);
      if (u.xp) {
        if (r.success) { u.xp.tactics++; if (r.dmg > 0) this.xpHit(u, target, r.dmg); }
        else u.xp.tacFail++;
      }
      // 胜负判定放在记经验之后：火计 / 落石击溃最后一支敌军或敌主将时，施计者的经验也计入战后结算
      this.checkEnd();
      return r;
    }
    // 原四计（第一版规则，原样保留；第一版末尾的 checkEnd 移到 useTactic 记经验之后，结果与随机数序列不变）
    useTactic1(u, t, target) {
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
        baseD *= this.tacK(u, target);                 // 老练（1 级对 1 级为 1）
        dmg = Math.min(target.troops, M.roundToInt(baseD * SG.Random.rangeFloat(0.85, 1.15)));
        target.troops -= dmg;
        target.morale = Math.max(0, target.morale - 10);
        this.checkRout(target);
      }
      return { success: true, dmg };
    }

    // ======================================================== 新计策（第三版） --
    // 计策目录与规则字段见 js/tactics-data.js。原四计（kind 为数字）不经过这里。
    knownTactics(u) {
      const T = SG.Tactics;
      if (T && typeof T.known === 'function') return T.known(u.gen);
      return Defs.tactics.filter(t => u.gen.intel >= t.reqInt);
    }
    knowsTactic(u, t) {
      if (typeof t.kind !== 'string') return u.gen.intel >= t.reqInt;
      const P = SG.Progress;
      if (P && P.enabled === false) return false;
      return Array.isArray(u.gen.tactics) && u.gen.tactics.indexOf(t.id) >= 0;
    }
    // 地形需求：river 目标在河上或临河；cover 目标在林地 / 山丘上或紧邻之
    tacNeedOk(t, e) {
      if (t.need === 'river') return this.nearRiver(e.x, e.y);
      if (t.need === 'cover') {
        const cov = (x, y) => this.inBounds(x, y) && (this.map[x][y] === Terrain.Forest || this.map[x][y] === Terrain.Hill);
        if (cov(e.x, e.y)) return true;
        for (const d of BattleModel.Dirs) if (cov(e.x + d.x, e.y + d.y)) return true;
        return false;
      }
      return true;
    }
    newTacticTargets(u, t) {
      if (!u.alive) return [];
      const dist = BattleModel.dist;
      if (t.target === 'enemy') return this.alive(1 - u.side).filter(e => dist(e, u) <= t.range && this.tacNeedOk(t, e));
      if (t.target === 'ally') {
        return this.alive(u.side).filter(a => dist(a, u) <= t.range &&
          (t.heal ? (t.cure ? this.needsHeal(a) : a.troops < this.healCapOf(a) * 0.95) : true));
      }
      return [u];
    }
    newTacticUsable(u, t) {
      if (!this.knowsTactic(u, t)) return { ok: false, why: '尚未习得' };
      if (u.gen.intel < (t.reqInt | 0)) return { ok: false, why: '智力不足（需 ' + t.reqInt + '）' };
      const r = u.tacReady && u.tacReady[t.id];
      if (r && this.day < r) return { ok: false, why: '冷却中（第 ' + r + ' 日起可用）' };
      if (t.food && this.food[u.side] < 50) return { ok: false, why: '军粮不足' };
      if (t.need && this.newTacticTargets(u, t).length === 0) return { ok: false, why: t.need === 'river' ? '射程内无临河的敌军' : '射程内无靠近林地 / 山丘的敌军' };
      return { ok: true, why: '' };
    }
    // 成功率（原四计：火计 / 落石 / 混乱同第一版公式，激励必定成功）
    tacticChance(u, t, target) {
      if (typeof t.kind !== 'string') {
        if (t.kind === TacticKind.Inspire || !target) return 1;
        return M.clamp(0.5 + (u.gen.intel - target.gen.intel) / 110, 0.12, 0.92);
      }
      const r = t.roll;
      if (!r || !target || target.side === u.side) return 1;
      let p = r.base + (u.gen.intel - target.gen.intel) / r.div;
      if (r.forest && this.map[target.x][target.y] === Terrain.Forest) p += r.forest;
      if (r.lowMorale && target.morale < 50) p += r.lowMorale;
      return M.clamp(p, r.min, r.max);
    }
    // 计策伤害（期望值，不含随机浮动）：(base + per × 智力) × 敌兵力系数 ÷ 敌防御加成；林地、城防修正；溅射
    tacDmg(u, e, t, primary) {
      const D = t.dmg;
      let d = (D.base + D.per * u.gen.intel) * M.clamp(e.troops / 2500, 0.4, 1.4) / this.modK(e, 'def') * this.tacK(u, e);
      const tt = this.map[e.x][e.y];
      if (D.forest && tt === Terrain.Forest) d *= D.forest;
      if (!D.pierce && (tt === Terrain.Castle || tt === Terrain.Gate)) d *= 0.6;
      if (!primary) d *= D.splash || 0;
      return d;
    }
    // 效果清单（与 spPlan 同结构，由 applyPlan 结算；纯计算）
    tacticPlan(u, t, target) {
      const dist = BattleModel.dist;
      const I = u.gen.intel;
      const foes = this.alive(1 - u.side), friends = this.alive(u.side);
      const tg = target || u;
      const sp = { id: 'tac:' + t.id, name: t.name, kind: t.id, fx: t.fx, color: t.color, power: 0, tactic: true };
      const pl = {
        sp, kind: t.id, tactic: t, unit: u, target: tg, center: P(tg.x, tg.y), area: [], dmg: [], foe: [], ally: [],
        burn: [], douse: [], refresh: [], ap: 0, move: null, push: null, blocked: null, kill: null, drain: 0, pool: null, seq: false,
      };
      const morale = Math.round((t.morale || 0) + (t.moraleI || 0) * I);
      const src = 'tac:' + t.id;
      if (t.target === 'enemy') {
        const R = t.radius || 0;
        const list = [tg].concat(R > 0 ? foes.filter(e => e !== tg && dist(e, tg) <= R) : []);
        pl.area = this.areaTiles(tg, R);
        for (const e of list) {
          if (t.dmg) { const a = this.tacDmg(u, e, t, e === tg); if (a > 0) pl.dmg.push({ unit: e, amt: a, primary: e === tg, onFail: false }); }
          const f = { unit: e, morale: morale < 0 ? morale : 0, p: 0, turns: 0, mod: null };
          if (t.confuse) { const c = t.confuse; f.p = c.base >= 1 ? 1 : M.clamp(c.base + (I - e.gen.intel) / c.div, c.min, c.max); f.turns = c.turns; }
          // 削弱持续 N 日 = 敌军之后 N 次本方行动（加成在受影响部队本方行动开始时计时，故 +1；中毒按日发作，不加）
          if (t.mod) f.mod = Object.assign({ src }, t.mod, { days: t.mod.days + (t.mod.dot > 0 ? 0 : 1) });
          if (f.morale || f.p > 0 || f.mod) pl.foe.push(f);
        }
        if (t.douse) for (const p of pl.area) if (this.burning[p.x][p.y] > 0) pl.douse.push(P(p.x, p.y));
        // 范围封顶（同必杀技）：次要目标合计不超过 AREA_TOTAL × 主目标 − 主目标
        if (pl.dmg.length > 1) {
          const b = pl.dmg[0].primary ? pl.dmg[0].amt : Math.max(...pl.dmg.map(h => h.amt));
          let S0 = 0;
          for (const h of pl.dmg) if (!h.primary) S0 += Math.min(h.amt, h.unit.troops);
          const P0 = pl.dmg[0].primary ? Math.min(b, tg.troops) : 0;
          const cap = Math.max(((SG.Specials && SG.Specials.AREA_TOTAL) || 2.5) * b - P0, 0.4 * b);
          if (S0 > cap) { const k = cap / S0; for (const h of pl.dmg) if (!h.primary) h.amt *= k; pl.areaK = k; }
        }
      } else {
        const c = t.target === 'self' ? u : tg;
        const R = t.radius || 0;
        pl.center = P(c.x, c.y);
        pl.area = this.areaTiles(c, R);
        for (const a of friends) if (dist(a, c) <= R) {
          const heal = t.heal ? Math.max(0, Math.min(this.healCapOf(a) - a.troops, M.roundToInt((t.heal + (t.healI || 0) * I) * this.maxTroopsOf(a)))) : 0;
          const mod = t.mod ? Object.assign({ src }, t.mod) : null;
          if (heal > 0 || morale > 0 || mod || t.cure) pl.ally.push({ unit: a, morale: morale > 0 ? morale : 0, heal, cure: !!t.cure, mod });
        }
        if (t.foeConfuse) {
          const fc = t.foeConfuse;
          for (const e of foes) if (dist(e, u) <= R) pl.foe.push({ unit: e, morale: 0, p: M.clamp(fc.base + (I - e.gen.intel) / fc.div, fc.min, fc.max), turns: fc.turns, mod: null });
        }
      }
      return pl;
    }
    // 电脑用：期望价值 = 成功率 × (specialValue + 削弱的价值)
    tacticValue(u, t, target) {
      const dist = BattleModel.dist;
      const pl = this.tacticPlan(u, t, target);
      let v = this.specialValue(u, pl);
      const friends = this.alive(u.side);
      for (const f of pl.foe) {
        const m = f.mod;
        if (!m || m.dot > 0) continue;
        const e = f.unit;
        let near = null, bd = 1e9;
        for (const a of friends) { const d = dist(a, e); if (d < bd) { bd = d; near = a; } }
        if (!near) continue;
        // 敌方的削弱在其本方行动开始时 −1 日：除之后 days − 1 次己方行动外，本次行动中尚未出手的友军也受益（计半日）
        const days = m.days - 1 + BattleModel.aiTactic.nowDays;
        if (m.atk && m.atk < 1) v += (1 - m.atk) * this.threat(e, near) * days * 0.8;
        if (m.def && m.def < 1) {
          let inc = 0;
          for (const a of friends.filter(x => dist(x, e) <= 3).slice(0, 2)) inc += Math.min(e.troops, this.atkExpect(a, e));
          v += (1 / m.def - 1) * inc * days * 0.6;
        }
        if (m.counter && m.counter < 1) v += (1 - m.counter) * this.atkExpect(e, near) * Balance.CounterRatio * days * 0.5;
        if (m.move && m.move < 0) v += (bd > 2 ? 25 : 10) * days;
      }
      return v * this.tacticChance(u, t, target);
    }
    // 电脑：划算时使用习得的新计策 → { tactic, target, value } | null（不耗随机数）
    aiTactic(u) {
      const P = SG.Progress;
      if ((P && P.enabled === false) || !u.gen || !Array.isArray(u.gen.tactics)) return null;
      let best = null;
      for (const t of this.knownTactics(u)) {
        if (typeof t.kind !== 'string' || !this.tacticUsable(u, t).ok) continue;
        for (const tg of this.newTacticTargets(u, t)) {
          const v = this.tacticValue(u, t, tg);
          if (!best || v > best.value) best = { tactic: t, target: tg, value: v };
        }
      }
      if (!best) return null;
      // 与本手最好的普通攻击 / 火计期望比较。不与敌相邻时别无他事可做（移动已在前），门槛更低——
      // 否则伪报、挑拨这类削弱计的期望价值很少过线，委任与电脑几乎从不施展（第三版修订）
      const A = BattleModel.aiTactic, adj = this.adjacentEnemies(u).length > 0;
      return best.value >= Math.max(this.aiNormalValue(u) * (adj ? A.kAdj : A.kFree), adj ? A.floorAdj : A.floorFree) ? best : null;
    }
    useNewTactic(u, t, target) {
      const res = this.newRes();
      res.tactic = t; res.kind = t.id; res.name = t.name;
      const us = this.newTacticUsable(u, t);
      if (!us.ok || !u.alive || this.result !== 0) { res.why = us.why || '无法施展'; return res; }
      const cands = this.newTacticTargets(u, t);
      if (!cands.length) { res.why = '范围内无目标'; return res; }
      if (!target || cands.indexOf(target) < 0) target = cands[0];
      res.target = target; res.center = P(target.x, target.y);
      if (t.cd > 0) { if (!u.tacReady) u.tacReady = {}; u.tacReady[t.id] = this.day + t.cd + 1; }   // 「冷却 N 日」：之后 N 日不能用
      const p = this.tacticChance(u, t, target);
      if (p < 1 && SG.Random.value() > p) {
        res.resisted.push(target);
        if (u.xp) u.xp.tacFail++;
        return res;
      }
      const pl = this.tacticPlan(u, t, target);
      res.success = true; res.center = pl.center; res.area = pl.area;
      this.applyPlan(u, pl, res);
      if (t.food && res.heal > 0) this.food[u.side] = Math.max(0, this.food[u.side] - Math.ceil(res.heal / t.food));
      if (u.xp) u.xp.tactics++;
      this.checkEnd();
      return res;
    }

    // ======================================================== 战后经验 --
    // 分出胜负时调用一次（checkEnd / 期限）：按 u.xp 结算经验与战绩（SG.Progress），结果在 this.progress
    settleProgress() {
      if (this.progressDone || this.result === 0) return;
      this.progressDone = true;
      if (this.setup) this.setup.progressDone = true;
      this.progress = [];
      if (this.setup) this.setup.progress = this.progress;     // 与自动结算相同：界面战后读 setup.progress
      const Pg = SG.Progress;
      if (!Pg || !Pg.enabled) return;
      const win = this.result === 1 ? 0 : 1;
      for (const u of this.units) {
        if (!u.gen || !(u.troops0 > 0)) continue;     // 无兵上阵的武将不计（与自动结算 autoBattle 相同）
        const won = u.side === win;
        const x = u.xp || {};
        const e = Pg.battleExp(Object.assign({}, x, { won, commander: !!u.commander }));
        Pg.note(u.gen, won, x.routs | 0, x.duelWin | 0);
        const r = Pg.gain(u.gen, e, 'battle');
        this.progress.push({ gen: u.gen, side: u.side, won, exp: e, from: r ? r.from : u.gen.level, to: r ? r.to : u.gen.level, learned: r ? r.learned : [], xp: Object.assign({}, x) });
      }
    }

    // 单挑：是否应战（拒绝时双方士气变化）。随机数调用与第一版 duel() 完全相同
    duelAccepts(a, b) {
      const ok = b.gen.war >= a.gen.war - 8 || SG.Random.value() < 0.25 || (b.commander && SG.Random.value() < 0.4);
      if (!ok) { b.morale = Math.max(0, b.morale - 12); a.morale = Math.min(100, a.morale + 8); }
      return ok;
    }
    // 单挑：结算胜负后果（败者溃散、其军士气 −8、胜者士气 +15）。供格斗单挑画面（js/duel-game.js）结束后调用
    // info（可缺省）= { kind: 'ko' | 'time' | 'skip', big, … }：格斗画面（DuelGame.perform）与速算单挑传入；big = 击倒的一击是会心、绝技或终结技
    duelFinish(a, b, winnerIsA, rounds, info) {
      const winner = winnerIsA ? a : b, loser = winnerIsA ? b : a;
      loser.troops = 0; loser.alive = false; this.setup.routed.add(loser.gen.id);
      if (winner.xp) { winner.xp.duelWin++; this.xpHit(winner, loser, 0); }
      if (loser.xp) loser.xp.duelLoss++;
      for (const o of this.alive(loser.side)) o.morale = Math.max(0, o.morale - 8);
      winner.morale = Math.min(100, winner.morale + 15);
      // 第四版 ITEMS §8.2 阵斩：君主单挑被击倒——会心 / 绝技 / 终结技一击 35 %，其他击倒 15 %，时间到 0（只在概率 > 0 时取一个随机数）
      let slain = false;
      if (info && info.kind === 'ko' && slayOn()) slain = !!SG.Items.rollSlay(loser.gen, 'duelKO', winner.gen.faction, { big: !!info.big });
      this.checkEnd();
      return { accepted: true, rounds: rounds || [], winner: winnerIsA ? 0 : 1, slain };
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
      // 速算单挑没有会心 / 绝技 / 终结技：体力归零 = 「其他击倒」，时间到 = 不斩（ITEMS §8.2）
      const fin = this.duelFinish(a, b, r.winner === 0, r.rounds, { kind: (r.winner === 0 ? hpB : hpA) <= 0 ? 'ko' : 'time', big: false });
      if (fin.slain) r.slain = true;
      return r;
    }

    changeFormation(u, f) { u.formation = f; }
    formationAllowed(u, f) { const d = Defs.formations[f]; return u.gen.intel >= d.reqInt && u.gen.war >= d.reqWar; }

    // ======================================================== 必杀技 --
    // 招式数据由 SG.Specials.of(gen) 提供（js/specials.js，已按机制补全默认参数）。
    // 未载入 specials.js 或 BattleModel.specialsEnabled = false 时，必杀技一律不可用，电脑也不会考虑。
    // 该部队会的全部必杀（专属在前；第三版起含习得的可习得必杀，见 SG.Specials.known）
    knownSpecials(u) {
      if (!BattleModel.specialsEnabled || !u || !u.gen || !SG.Specials || typeof SG.Specials.of !== 'function') return [];
      if (typeof SG.Specials.known === 'function') return SG.Specials.known(u.gen);
      const sp = SG.Specials.of(u.gen);
      return sp ? [sp] : [];
    }
    spWasUsed(u, sp) { return (Array.isArray(u.spUsed) && u.spUsed.indexOf(sp.id) >= 0) || (!sp.pool && !!u.specialUsed); }
    // id（编号或招式对象）缺省时：u.spPick → 第一招尚可施展且有目标的 → 第一招未施展的 → 专属必杀
    specialOf(u, id) {
      const list = this.knownSpecials(u);
      if (!list.length) return null;
      if (id && typeof id === 'object') id = id.id;
      if (id) return list.find(s => s.id === id) || null;
      if (u.spPick) { const p = list.find(s => s.id === u.spPick); if (p) return p; }
      if (list.length === 1) return list[0];
      for (const s of list) if (!this.spWasUsed(u, s) && this.targetsOf(u, s).length > 0) return s;
      return list.find(s => !this.spWasUsed(u, s)) || list[0];
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
    // 计时规则（攻守双方对称）：
    //   中毒（dot > 0）：每日结束时（endDay）损兵一次、days −1——无论谁施毒，都恰好发作 days 次。
    //   其余加成（攻 / 防 / 反击 / 机动）：在该部队本方下一次行动开始时（startSide）days −1、到 0 移除。
    //     于是「持续 N 日」= 本方剩余的这次行动 + 敌方 N 次行动 + 本方之后 N−1 次行动，攻方施展与守方施展一样长。
    //     （若也按 endDay 计时，守方最后行动、施展后立刻过日，1 日的加成在敌方行动前就会失效。）
    tickMods(u) {
      for (const m of u.mods) {
        if (!(m.dot > 0)) continue;
        if (u.alive) {
          const d = Math.max(20, M.roundToInt(u.troops * m.dot));
          u.troops -= d;
          this.dayLog.push(u.gen.name + '中毒，损兵 ' + d);
          this.checkRout(u);
        }
        m.days--;
      }
      u.mods = u.mods.filter(m => m.days > 0);
    }
    expireMods(u) {
      for (const m of u.mods) if (!(m.dot > 0)) m.days--;
      u.mods = u.mods.filter(m => m.days > 0);
    }
    // 混乱的计数在每日结束时（守方行动之后）统一 −1。必杀技造成的「混乱 N 日」要让目标恰好失去 N 次本方行动：
    //   攻方行动中混乱守军 → 守军当日即失去行动，计数 N；守方行动中混乱攻军 → 过日先 −1，计数须为 N + 1。
    confuseCount(e, turns) { return turns + (e.side === 1 && this.side === 0 ? 0 : 1); }
    // 部队 x 今后还会因混乱失去的本方行动次数（不含正在进行的本方行动）
    confusedPhases(x) {
      const c = x.confused | 0;
      if (c <= 0) return 0;
      return x.side === 1 && this.side === 0 ? c : c - 1;
    }

    // ---- 计算基准 ----
    // 一次普通攻击的期望伤害（不含随机浮动）
    atkExpect(a, t) { return this.atkPower(a) * Balance.DamageK / this.defFactor(t); }
    // 第四版：含会心期望的普通攻击（COMBAT-V4 §2.4；只用于 aiNormalValue，其余 atkExpect 不含会心，必杀 / 计策的设计比例不变）
    atkExpectCrit(a, t) {
      const Mo = SG.Mods;
      if (!Mo || !Mo.critChance || !Mo.critEnabled || !Mo.critEnabled()) return this.atkExpect(a, t);
      return this.atkExpect(a, t) * (1 + Mo.critChance(a) * (Mo.critK(a) - 1));
    }
    routBonus(t) { return t.commander ? 6000 : 300; }
    // 必杀技伤害基准（期望值）。
    //   武力系（stat 'war'）：一次普通攻击的期望伤害 × 武勇系数（0.8 + 武力/500），随己方兵力、阵型、训练、士气与敌方防御变化；
    //     pierce 时无视地形防御。
    //   智力 / 政治系：与计策同量级 (200 + 能力×9) × 敌兵力系数，不随己方兵力变化；城门 / 本城内 ×0.7。
    spBase(u, t, sp) {
      const stat = sp.stat || 'war';
      const v = statOf(u.gen, stat);
      if (stat === 'intel' || stat === 'pol') {
        let d = (200 + v * 9) * M.clamp(t.troops / 2500, 0.4, 1.4) / this.modK(t, 'def') * this.tacK(u, t);
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
      // 河川与水域（8，湖海，SG.BattleTerrain.Water）都算临水：水计与水淹类必杀可用
      const wet = t => t === Terrain.River || t === 8;
      if (this.inBounds(x, y) && wet(this.map[x][y])) return true;
      for (const d of BattleModel.Dirs) { const nx = x + d.x, ny = y + d.y; if (this.inBounds(nx, ny) && wet(this.map[nx][ny])) return true; }
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
    // 回复上限：出阵时的兵力（且不超过统率上限）——必杀只能补回战损，不能凭空增兵
    healCapOf(u) { return Math.min(this.maxTroopsOf(u), u.troops0 != null ? u.troops0 : u.troops); }
    needsHeal(b) { return b.alive && (b.troops < this.healCapOf(b) * 0.95 || this.confusedPhases(b) > 0); }
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
    specialTargets(u, id) { const sp = this.specialOf(u, id); return sp ? this.targetsOf(u, sp) : []; }
    targetsOf(u, sp) {
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
    // id 缺省且未选招时：是否还有任何一招可用（why 为第一招不可用的原因；全部施展过为「本战已施展」）
    specialUsable(u, id) {
      const list = this.knownSpecials(u);
      if (!list.length) return { ok: false, why: BattleModel.specialsEnabled && SG.Specials ? '无必杀技' : '必杀技未开放' };
      if (id == null && !(u.spPick && list.some(s => s.id === u.spPick))) {
        let first = null;
        for (const s of list) {
          const r = this.spUsable1(u, s);
          if (r.ok) return r;
          if (!first && r.why !== '本战已施展') first = r;
        }
        return first || { ok: false, why: '本战已施展' };
      }
      const sp = this.specialOf(u, id);
      if (!sp) return { ok: false, why: '未习得此招' };
      return this.spUsable1(u, sp);
    }
    spUsable1(u, sp) {
      if (this.result !== 0) return { ok: false, why: '战斗已结束' };
      if (!u.alive) return { ok: false, why: '已溃散' };
      if (this.spWasUsed(u, sp)) return { ok: false, why: '本战已施展' };
      if (u.confused > 0) return { ok: false, why: '混乱中' };
      if (this.targetsOf(u, sp).length === 0) {
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
    specialPlan(u, target, id) {
      const sp = this.specialOf(u, id);
      return sp ? this.spPlan(u, sp, target) : null;
    }
    spPlan(u, sp, target) {
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
      const healAmt = a => Math.max(0, Math.min(this.healCapOf(a) - a.troops, M.roundToInt(sp.heal * this.maxTroopsOf(a) * kS)));
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
          let k = 1 + sp.dash * L.dash;
          if (sp.push) {
            // 本城、城门上的部队据守不退（否则一记突击就能把守将撞出本城，随后踏入即破城）
            const tt = this.map[t.x][t.y], held = tt === Terrain.Castle || tt === Terrain.Gate;
            const bx = t.x + L.dx, by = t.y + L.dy;
            if (!held && this.passable(bx, by) && this.unitAt(bx, by) == null && this.map[bx][by] !== Terrain.Castle) pl.push = { unit: t, from: P(t.x, t.y), to: P(bx, by) };
            else { k += 0.25; pl.blocked = t; }                  // 退无可退：撞击伤害 +25%
          }
          // 冲刺与撞击加成合计封顶（SG.Specials.CHARGE_CAP），单体伤害不超出平衡目标太多
          hit(t, sp.power * base(t) * Math.min(k, SG.Specials.CHARGE_CAP || 1.3), true); extras(t);
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
          for (const e of foes) if (dist(e, t) <= sp.radius) { hit(e, sp.power * base(e) * (this.nearRiver(e.x, e.y) ? (SG.Specials.FLOOD_RIVER || 1.4) : (SG.Specials.FLOOD_DRY || 0.85)), e === t); extras(e); }
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
          // 学来的暗杀（招式池，带 tier）成功率减半：第三版战场放大后电脑更易挑出弱将下手，实测倍率偏高
          if (sp.tier) p *= 0.5;
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
      // 范围伤害封顶：命中多支敌军时，次要目标的伤害按比例缩减，使总伤害 ≤ AREA_TOTAL × 基准（主目标的一次 1 倍伤害）；
      // 主目标的伤害不缩减。连斩以斩数计量（strikes × power 已有上限），不在此列。
      if (sp.kind !== 'rampage' && pl.dmg.length > 1) {
        const b = t.side !== u.side ? base(t) : Math.max(...pl.dmg.map(h => base(h.unit)));
        const eff = h => Math.min(h.amt, h.unit.troops);
        let P0 = 0, S0 = 0;
        for (const h of pl.dmg) if (h.primary) P0 += eff(h); else S0 += eff(h);
        const cap = Math.max((SG.Specials.AREA_TOTAL || 2.8) * b - P0, 0.4 * b);
        if (S0 > cap) { const k = cap / S0; for (const h of pl.dmg) if (!h.primary) h.amt *= k; pl.areaK = k; }
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
        if (f.p > 0) v += f.p * Math.max(0, f.turns - this.confusedPhases(e)) * 0.8 * this.threat(e, u);
        if (f.mod && f.mod.dot > 0) v += f.mod.days * Math.max(20, rem * f.mod.dot) * 0.9;
      }
      for (const a of pl.ally) {
        const x = a.unit;
        v += a.heal * 0.9;
        const engaged = foes.some(e => dist(e, x) <= 5);
        if (a.morale > 0) v += Math.min(a.morale, 100 - x.morale) * x.troops / 300 * (x.morale < 40 ? 2 : 1) * (engaged ? 1 : 0.3);
        if (a.cure && x.confused > 0) v += this.confusedPhases(x) * 0.5 * this.atkExpect(x, nearestFoe(x) || x);
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
        const d = Math.min(t.troops, this.atkExpectCrit(u, t));   // 第四版：攻方普通攻击可会心（反击不会）
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
    // 第三版：在全部已会、尚未施展的招式中挑期望价值最高的一手（同值时专属必杀优先）
    aiSpecial(u) {
      if (!BattleModel.specialsEnabled) return null;
      let best = null, bv = 0, bsp = null;
      for (const sp of this.knownSpecials(u)) {
        if (!this.spUsable1(u, sp).ok) continue;
        for (const t of this.targetsOf(u, sp)) {
          const v = this.specialValue(u, this.spPlan(u, sp, t));
          if (v > bv) { bv = v; best = t; bsp = sp; }
        }
      }
      if (!best) return null;
      const normal = this.aiNormalValue(u);
      let k = this.day >= 20 ? 1.05 : this.day >= 10 ? 1.3 : 1.5;
      if (u.troops < this.maxTroopsOf(u) * 0.3 || u.morale < 30) k = Math.min(k, 1.0);
      return bv >= Math.max(normal * k, 260) ? { target: best, value: bv, special: bsp } : null;
    }

    // ---- 结算 ----
    newRes() {
      return {
        success: false, name: '', kind: '', sp: null, why: '', dmg: 0, hits: [], healed: [], heal: 0, confused: [], resisted: [], cursed: [],
        buffed: [], refreshed: [], burned: [], doused: [], moved: null, pushed: [], blocked: null, killed: null, gained: 0,
        routed: [], affected: [], area: [], center: null, target: null,
      };
    }
    useSpecial(u, target, id) {
      const res = this.newRes();
      const sp = this.specialOf(u, id);
      if (!sp) { res.why = this.knownSpecials(u).length ? '未习得此招' : '无必杀技'; return res; }
      res.name = sp.name; res.kind = sp.kind; res.sp = sp;
      const us = this.spUsable1(u, sp);
      if (!us.ok) { res.why = us.why; return res; }
      const cands = this.targetsOf(u, sp);
      if (!target || cands.indexOf(target) < 0) target = cands[0];
      const pl = this.spPlan(u, sp, target);
      if (!Array.isArray(u.spUsed)) u.spUsed = [];
      u.spUsed.push(sp.id);
      if (!sp.pool) u.specialUsed = true;
      u.spPick = null;
      if (u.xp) u.xp.specials++;
      res.success = true; res.target = target; res.center = pl.center; res.area = pl.area;
      this.applyPlan(u, pl, res);
      if (res.dmg > 0) u.morale = Math.min(100, u.morale + 6);
      this.checkEnd();
      return res;
    }

    // 按效果清单结算（必杀与新计策共用；随机数调用顺序与第二版 useSpecial 相同）
    applyPlan(u, pl, res) {
      const sp = pl.sp;
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
          this.xpHit(u, k, d);
          // 第四版 ITEMS §8.2 阵斩：暗杀得手于君主 50 %（只在概率 > 0 时取一个随机数）
          if (slayOn()) res.slain = !!SG.Items.rollSlay(k.gen, 'assassinate', u.gen.faction);
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
        this.xpHit(u, e, d);
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
          if (SG.Random.value() < f.p) { e.confused = Math.max(e.confused, this.confuseCount(e, f.turns)); e.acted = true; res.confused.push(e); }
          else res.resisted.push(e);
        }
        if (f.mod) { this.addMod(e, Object.assign({}, f.mod)); res.cursed.push(e); }
        if (this.checkRout(e)) { routed(e); this.xpHit(u, e, 0); }
      }
      // 6. 友军：回复、士气、解除混乱、加成
      for (const a of pl.ally) {
        const x = a.unit;
        if (!x.alive) continue;
        touch(x);
        if (a.heal > 0) { x.troops += a.heal; res.healed.push({ unit: x, amount: a.heal }); res.heal += a.heal; if (u.xp) u.xp.heal += a.heal; }
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
      if (this.result !== 0) this.settleProgress();
    }

    // ---------------------------------------------------------- 电脑 --
    aiFormation(u) {
      let best = 0; let score = -1;
      for (let i = 0; i < Defs.formations.length; i++) {
        if (!this.formationAllowed(u, i)) continue;
        const f = Defs.formations[i];
        let s = u.side === 0 ? f.atk * 1.2 + f.def * 0.8 + f.move * 0.05 : f.atk * 0.7 + f.def * 1.3;
        // 第四版：攻方略看重阵型会心（+ FORM_CRIT × 0.5 × 攻，COMBAT-V4 §2.4；会心关闭时与第三版相同）
        if (u.side === 0 && f.crit && SG.Mods && SG.Mods.critEnabled && SG.Mods.critEnabled()) s += f.crit * 0.5 * f.atk;
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
        if (sp) {
          plan.kind = 'special'; plan.target = sp.target; plan.value = sp.value; plan.special = sp.special;
          u.spPick = sp.special ? sp.special.id : null;     // 旧控制器不传编号：useSpecial / SG.Specials.perform 缺省用它
          return plan;
        }
      }
      // 第三版：习得的新计策（按期望价值，不耗随机数；原四计的随机判定照旧在后面）
      const lt = this.aiTactic(u);
      if (lt) { plan.kind = 'tactic'; plan.tactic = lt.tactic; plan.target = lt.target; plan.value = lt.value; return plan; }
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
  // 第四版：false 时不会心（照样取会心的随机数，随机数流与开启时相同；SG.Crit.enabled 是全局开关）
  BattleModel.critEnabled = true;
  // 人物菜单的会心率（独自一军、本人为主将；与战斗同一个 SG.Mods.critChance）
  BattleModel.critChanceOf = function (gen, formationIdx) {
    const Mo = SG.Mods;
    if (!Mo || !Mo.critChance) return 0;
    return Mo.critChance({ gen, side: 0, formation: formationIdx === undefined ? (gen.formation | 0) : formationIdx, morale: 70, confused: 0, commander: true });
  };
  // 阵斩（ITEMS §8.2）只在装备系统开着、已载入游戏状态时判定（否则不取随机数）
  function slayOn() { const I = SG.Items; return !!(I && I.enabled !== false && typeof I.rollSlay === 'function' && SG.G && SG.G.items); }
  function startMorale(u, base) { const Mo = SG.Mods; return Mo && Mo.startMorale ? Mo.startMorale(u, base) : base; }
  // 电脑施展习得新计策的门槛（aiTactic）：期望价值 ≥ max(普通一手 × k, floor)；adj = 已与敌相邻
  BattleModel.aiTactic = { kAdj: 0.9, kFree: 0.8, floorAdj: 80, floorFree: 25, nowDays: 0.5 };
  // 第三版：电脑之间自动结算的攻城战也给经验（strategy-ai.js 先于本文件载入；不耗随机数，s.progressDone 防重复）
  if (SG.Progress && SG.Conquest) SG.Progress.hookConquest(SG.Conquest);

  SG.BUnit = BUnit;
  SG.BattleModel = BattleModel;
})();
