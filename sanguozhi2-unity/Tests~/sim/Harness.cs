// (1) 全电脑模拟：6 个种子 × 40 年，每月检查一致性；外加 40 场电脑对电脑的战术战斗（对应 tests/sim.js (1)(1b)）。
using System; using System.Linq; using System.Collections.Generic; using Sanguo;
class P { static int Main(string[] a){
 Console.OutputEncoding=new System.Text.UTF8Encoding(false); int seeds = 6; int fails=0;
 for (int seed=1; seed<=seeds; seed++){
  UnityEngine.Random.InitState(seed);
  var g = GameState.Current = GameState.NewGame("liubei");
  g.player = -1; // 让所有势力由电脑控制（RunAI 跳过 player）
  var sw = System.Diagnostics.Stopwatch.StartNew();
  int month=0; int battles=0;
  try {
   for (; month<12*40; month++){
     var news=new List<string>();
     // player=-1 时所有势力都会行动；RunAI 中对 player 的进攻不会发生
     var pb = StrategyAI.RunAI(news);
     if (pb.Count>0) throw new Exception("player battles with no player");
     StrategyAI.EndMonth();
     // 一致性检查
     foreach (var c in g.cities){ if (c.owner>=0 && !g.factions[c.owner].alive) throw new Exception("dead faction owns city "+c.name);
        if (c.owner>=0 && c.governor>=0 && g.generals[c.governor].city!=c.id) {}
        if (c.gold<0||c.food<0) throw new Exception("negative res "+c.name); }
     foreach (var gen in g.generals) { if (gen.troops<0) throw new Exception("neg troops"); if (gen.faction>=0 && !g.factions[gen.faction].alive) throw new Exception("gen in dead faction "+gen.name); }
     foreach (var f in g.factions.Where(f=>f.alive)) { var r=g.generals[f.ruler]; if (r.dead||r.faction!=f.id) throw new Exception("bad ruler "+f.name+" -> "+r.name+" dead="+r.dead+" fac="+r.faction); }
     int alive = g.factions.Count(f=>f.alive);
     if (month==119) Console.Write($"[10y alive {alive}] ");
     if (alive<=1) break;
   }
   var top = g.factions.Where(f=>f.alive).OrderByDescending(f=>g.CityCount(f.id)).Select(f=>f.name+":"+g.CityCount(f.id)).Take(5);
   Console.WriteLine($"seed {seed}: {month} months, alive {g.factions.Count(f=>f.alive)}, neutral {g.cities.Count(c=>c.owner<0)}, top [{string.Join(", ",top)}]  {sw.ElapsedMilliseconds}ms");
  } catch (Exception e) { fails++; Console.WriteLine($"seed {seed} FAILED at month {month}: {e}"); }
 }
 // 战术战斗：电脑对电脑完整打完
 int bw=0, bl=0, bdays=0;
 for (int seed=1; seed<=40; seed++){
  UnityEngine.Random.InitState(seed*7);
  var g = GameState.Current = GameState.NewGame("cao");
  var pairs = new[]{("chenliu","luoyang"),("pingyuan","nanpi"),("changsha","jiangling"),("beiping","nanpi"),("wan","xiangyang")};
  var pr = pairs[seed%pairs.Length];
  var src=g.CityByKey(pr.Item1); var tgt=g.CityByKey(pr.Item2);
  var atk = g.OfficersIn(src).Where(x=>x.troops>0).Take(5).ToList();
  try {
   var s = Conquest.Prepare(src.owner, src, tgt, atk, 3000, 0);
   var M = new BattleModel(s);
   int guard=0;
   while (M.result==0 && guard++<2000){
     var order = M.Alive(M.side).Where(M.CanAct).ToList();
     foreach (var u in order){ if (!M.CanAct(u)||M.result!=0) continue;
        var plan=M.PlanFor(u); if (plan.move.HasValue){ var path=M.PathTo(u,plan.move.Value); if (path.Count==0||path[0]!=new UnityEngine.Vector2Int(u.x,u.y)||path[path.Count-1]!=plan.move.Value) throw new Exception("bad path"); if (M.UnitAt(plan.move.Value.x,plan.move.Value.y)!=null) throw new Exception("move onto unit"); M.Move(u,plan.move.Value);} 
        if (M.result!=0) break;
        var act=M.PlanAction(u);
        if (act.kind=="attack") M.Attack(u,act.target); else if (act.kind=="tactic") M.UseTactic(u,act.tactic,act.target); else if (act.kind=="duel") M.Duel(u,act.target);
        M.Spend(u); }
     if (M.result==0) M.EndSide();
   }
   if (M.result==0) throw new Exception("battle never ended");
   bdays+=M.day; if (M.result==1) bw++; else bl++;
   s.attackerWon = M.result==1; Conquest.Apply(s); if (s.captives.Count>0) Conquest.AiDecideCaptives(s, s.attackerWon? s.attacker : s.defender);
   if (seed<=5) Console.WriteLine($"battle {pr.Item1}->{pr.Item2}: result {M.result} day {M.day} reason {M.resultReason} routed {s.routed.Count} captives {s.captives.Count}");
  } catch (Exception e){ fails++; Console.WriteLine("battle seed "+seed+" FAILED: "+e); }
 }
 Console.WriteLine($"tactical battles: attacker won {bw}, defender won {bl}, avg days {bdays/(double)(bw+bl):0.0}");
 Console.WriteLine(fails==0?"ALL OK":"FAILURES: "+fails);
 return fails;
}}
