'use strict';
// JS 端的逐月 / 逐场轨迹（HarnessTrace.cs 调用；也可单独运行）。网页版目录取环境变量 SANGUO_WEB，缺省 ../../../sanguozhi2-web。
//   node trace.js game <种子> <君主键> <月数> <剧本 classic|world>   → 每月一行 JSON.stringify(G)（全电脑对局）
//   node trace.js battles                                             → 400 场战术战斗（必杀技关 / 开各 200 场）的结果与行动记录
const path = require('path'), fs = require('fs'), vm = require('vm');
global.window = global;
const WEB = process.env.SANGUO_WEB || path.join(__dirname, '..', '..', '..', 'sanguozhi2-web');
for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js', 'model.js', 'strategy-ai.js', 'battle-model.js', 'specials-data.js', 'specials.js'])
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
} else { console.error('usage: node trace.js game <seed> <key> <months> <scenario> | battles'); process.exit(2); }
process.stdout.write(out.join('\n') + '\n');
