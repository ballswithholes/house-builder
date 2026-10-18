'use strict';
// JS 端的逐月 / 逐场轨迹（HarnessTrace.cs 调用；也可单独运行）。网页版目录取环境变量 SANGUO_WEB，缺省 ../../../sanguozhi2-web。
//   node trace.js game <种子> <君主键> <月数> <剧本 classic|world>   → 每月一行 JSON.stringify(G)（全电脑对局）
//   node trace.js battles                                             → 400 场战术战斗（必杀技关 / 开各 200 场）的结果与行动记录
//   node trace.js pbattles                                            → 成长后的战术战斗（第三版）：全电脑对局 36 个月后的局面（存档往返后开战）
//                                                                       与随机等级的局面（同 tests/sim.js (7e)），记录用招编号、计策编号与战后经验结算
//   node trace.js learn                                               → 升级选择序列（同 tests/sim.js (7a)）：每步的候选、空格、电脑之选与习得
const path = require('path'), fs = require('fs'), vm = require('vm');
global.window = global;
const WEB = process.env.SANGUO_WEB || path.join(__dirname, '..', '..', '..', 'sanguozhi2-web');
for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js', 'model.js', 'strategy-ai.js', 'battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js'])
  vm.runInThisContext(fs.readFileSync(path.join(WEB, 'js', f), 'utf8'), { filename: f });
const SG = global.SG;
// Unity 的 Random.value 与 Random.Range(float, float) 返回 float（UnityStub 同此：(float)NextDouble()、(float)(min + NextDouble() * (max - min))），
// 网页版为 double。这里用 Math.fround 照样取整，两边逐位一致（否则极少数恰在 .5 舍入边界上的结果会差 1，轨迹随之分岔）。
// rangeInt 两边都按 double 计算，不变。raw 只取一次：seed() 换的是闭包里的 rng，value() 每次都调用当前的 rng。
{ const F = Math.fround, raw = SG.Random.value;
  SG.Random.value = () => F(raw());
  SG.Random.rangeFloat = (a, b) => F(F(a) + raw() * F(F(b) - F(a))); }
const out = [];
if (process.argv[2] === 'game') {
  SG.Random.seed(+process.argv[3]);
  const g = SG.G = SG.GameState.newGame(process.argv[4], process.argv[6] || 'classic');
  g.player = -1;
  for (let m = 0; m < +process.argv[5]; m++) { SG.StrategyAI.runAI([]); SG.StrategyAI.endMonth(); out.push(JSON.stringify(g)); }
} else if (process.argv[2] === 'battles') {
  const pairs = [['chenliu', 'luoyang'], ['pingyuan', 'nanpi'], ['changsha', 'jiangling'], ['beiping', 'nanpi'], ['wan', 'xiangyang'],
    ['xiapi', 'xiaopei'], ['changan', 'tianshui'], ['chengdu', 'jiangzhou'], ['ye', 'jinyang'], ['shouchun', 'lujiang']];
  for (const on of [false, true]) {
    SG.BattleModel.specialsEnabled = on;
    for (let seed = 1; seed <= 200; seed++) {
      const pr = pairs[seed % pairs.length];
      SG.Random.seed((1000 + seed) * 7);
      const g = SG.G = SG.GameState.newGame('cao', 'classic');
      const src = g.cityByKey(pr[0]), tgt = g.cityByKey(pr[1]);
      const s = SG.Conquest.prepare(src.owner, src, tgt, g.officersIn(src).filter(x => x.troops > 0).slice(0, 5), 3000, 0);
      const M = new SG.BattleModel(s);
      let guard = 0; const log = [];
      while (M.result === 0 && guard++ < 2000) {
        for (let pass = 0; pass < 4 && M.result === 0; pass++) {
          const order = M.alive(M.side).filter(u => M.canAct(u));
          if (!order.length) break;
          for (const u of order) {
            if (!M.canAct(u) || M.result !== 0) continue;
            const plan = M.planFor(u);
            if (plan.move) M.move(u, plan.move);
            if (M.result !== 0) break;
            const a = M.planAction(u);
            if (a.kind === 'attack') { const r = M.attack(u, a.target); log.push('A' + r.dmg + '/' + r.counter); }
            else if (a.kind === 'tactic') { const r = M.useTactic(u, a.tactic, a.target); log.push('T' + r.dmg); }
            else if (a.kind === 'duel') { const r = M.duel(u, a.target); log.push('D' + r.winner); }
            else if (a.kind === 'special') { const r = M.useSpecial(u, a.target); log.push('S' + r.kind + r.dmg + '/' + r.heal); }
            M.spend(u);
          }
        }
        if (M.result === 0) M.endSide();
      }
      out.push([on ? 1 : 0, seed, M.result, M.day, M.resultReason, M.units.map(u => [u.troops, u.morale, u.alive ? 1 : 0, u.x, u.y, u.specialUsed ? 1 : 0].join(',')).join(';'), log.join(' ')].join('|'));
    }
  }
} else if (process.argv[2] === 'pbattles') {
  const Pg = SG.Progress, Seq = SG.Seq;
  const pairs = [['chenliu', 'luoyang'], ['pingyuan', 'nanpi'], ['changsha', 'jiangling'], ['beiping', 'nanpi'], ['wan', 'xiangyang']];
  const gstate = x => [x.name, x.exp, x.level, x.specials.join('+'), x.tactics.join('+'), [x.record.battles, x.record.wins, x.record.routs, x.record.duels].join('/')].join(':');
  const fight = (tag, s) => {
    const M = new SG.BattleModel(s);
    let guard = 0; const log = [];
    while (M.result === 0 && guard++ < 2000) {
      for (let pass = 0; pass < 4 && M.result === 0; pass++) {
        const order = M.alive(M.side).filter(u => M.canAct(u));
        if (!order.length) break;
        for (const u of order) {
          if (!M.canAct(u) || M.result !== 0) continue;
          const plan = M.planFor(u);
          if (plan.move) M.move(u, plan.move);
          if (M.result !== 0) break;
          const a = M.planAction(u);
          if (a.kind === 'attack') { const r = M.attack(u, a.target); log.push('A' + r.dmg + '/' + r.counter); }
          else if (a.kind === 'tactic') { const r = M.useTactic(u, a.tactic, a.target); log.push('T' + (a.tactic.id || SG.Progress.INNATE_TACTICS[a.tactic.kind]) + ':' + (r.success ? 1 : 0) + ':' + (r.dmg || 0)); }
          else if (a.kind === 'duel') { const r = M.duel(u, a.target); log.push('D' + r.winner); }
          else if (a.kind === 'special') { const r = M.useSpecial(u, a.target); log.push('S' + (r.sp ? r.sp.id : '-') + ':' + r.kind + r.dmg + '/' + r.heal); }
          M.spend(u);
        }
      }
      if (M.result === 0) M.endSide();
    }
    const pr = (M.progress || []).map(p => [p.gen.name, p.side, p.won ? 1 : 0, p.exp, p.from + '>' + p.to, p.learned.map(c => c.id).join('+'),
      [p.xp.dmg, p.xp.heal, p.xp.routs, p.xp.cmdRouts, p.xp.duelWin, p.xp.duelLoss, p.xp.specials, p.xp.tactics, p.xp.tacFail].join('/')].join(':')).join(';');
    out.push([tag, M.result, M.day, M.resultReason, M.units.map(u => [u.troops, u.morale, u.alive ? 1 : 0, u.x, u.y, u.specialUsed ? 1 : 0, u.spUsed.join('+')].join(',')).join(';'),
      log.join(' '), pr, s.atk.concat(s.def).map(gstate).join(';')].join('|'));
  };
  // (a) 全电脑对局 36 个月后的局面：电脑自然成长、习得；每场从存档文字读回（GameState.fromJSON）后开战
  for (let seed = 1; seed <= 3; seed++) {
    SG.Random.seed(seed);
    const g0 = SG.G = SG.GameState.newGame('liubei', 'classic');
    g0.player = -1;
    for (let m = 0; m < 36; m++) { SG.StrategyAI.runAI([]); SG.StrategyAI.endMonth(); }
    const text = JSON.stringify(g0);
    for (let k = 0; k < 14; k++) {
      const g = SG.G = SG.GameState.fromJSON(text);
      g.player = -1;
      SG.Random.seed(500 + seed * 100 + k);
      const cand = [];
      for (const c of g.cities) {
        if (c.owner < 0) continue;
        for (const j of c.links) { const t = g.cities[j]; if (t.owner >= 0 && t.owner !== c.owner && g.officersIn(c).some(x => x.troops > 500) && g.officersIn(t).length) cand.push([c, t]); }
      }
      if (!cand.length) continue;
      const [c, t] = cand[(k * 7 + seed) % cand.length];
      const atk = Seq.orderByDesc(g.officersIn(c).filter(x => x.troops > 0), x => x.troops).slice(0, 5);
      fight('campaign ' + seed + ' ' + k, SG.Conquest.prepare(c.owner, c, t, atk, 3000, 0));
    }
  }
  // (b) 随机等级（同 tests/sim.js (7e)）
  for (let seed = 1; seed <= 24; seed++) {
    SG.Random.seed(seed * 11);
    const g = SG.G = SG.GameState.newGame('cao', 'classic');
    g.player = -1;
    const r = SG.SeededRandom(seed);
    for (const x of g.generals) Pg.gain(x, r.next(Pg.EXP[10] + 1));
    const pr = pairs[seed % pairs.length];
    const src = g.cityByKey(pr[0]), tgt = g.cityByKey(pr[1]);
    fight('random ' + seed, SG.Conquest.prepare(src.owner, src, tgt, g.officersIn(src).filter(x => x.troops > 0).slice(0, 5), 3000, 0));
  }
} else if (process.argv[2] === 'learn') {
  const Pg = SG.Progress;
  SG.Random.seed(3);
  const g = SG.G = SG.GameState.newGame('cao', 'classic');
  let n = 0;
  const mk = (war, intel, lv, faction) => {
    const x = { id: 99000 + n, name: '测试' + (n++), war, intel, pol: 50, faction: faction === undefined ? -1 : faction, city: 0, troops: 3000, training: 60, loyalty: 80 };
    Pg.ensure(x); x.exp = Pg.EXP[lv || 1]; Pg.ensure(x);
    return x;
  };
  const slot = s => [s.max, s.unlocked, s.used, s.open, s.tier, s.statTier, s.levelTier].join('/');
  const rnd = SG.SeededRandom(77);
  for (let i = 0; i < 300; i++) {
    const x = mk(40 + rnd.next(61), 40 + rnd.next(61), 1 + rnd.next(10));
    const line = [x.name, x.war, x.intel, x.level, x.tactics.join('+')];
    for (let step = 0; step < 6; step++) {
      const c1 = Pg.candidates(x), op = Pg.openings(x), ai = Pg.aiPick(x, c1);
      line.push(c1.map(c => c.type[0] + c.id + '@' + c.tier).join(',') + ';' + op.special + '/' + op.tactic + ';' + slot(Pg.specialSlots(x)) + ';' + slot(Pg.tacticSlots(x)) + ';ai=' + (ai ? ai.id : '-'));
      if (!c1.length) break;
      const pick = c1[rnd.next(c1.length)];
      line.push('learn=' + pick.id + ':' + Pg.learn(x, pick) + ':' + Pg.learn(x, pick));
    }
    out.push(line.join('|'));
  }
  // 电脑 / 玩家武将一口气升到 10 级：自动习得（playerAutoLearn 开 / 关）
  for (let k = 0; k < 40; k++) {
    const war = 55 + (k * 7) % 46, intel = 45 + (k * 11) % 56;
    const ai = mk(war, intel, 1, 3), me = mk(war, intel, 1, g.player), me2 = mk(war, intel, 1, g.player);
    Pg.playerAutoLearn = false;
    let ra, rm;
    try { ra = Pg.gain(ai, Pg.EXP[10]); rm = Pg.gain(me, Pg.EXP[10]); } finally { Pg.playerAutoLearn = true; }
    const rm2 = Pg.gain(me2, Pg.EXP[10]);
    out.push(['auto', war, intel, ra.learned.map(c => c.id).join('+'), rm.learned.length, Pg.openings(me).total, rm2.learned.map(c => c.id).join('+'), ai.specials.join('+'), ai.tactics.join('+')].join('|'));
  }
  out.push('log ' + g.log.slice(-5).join(' / '));
} else { console.error('usage: node trace.js game <seed> <key> <months> <scenario> | battles | pbattles | learn'); process.exit(2); }
process.stdout.write(out.join('\n') + '\n');
