// (2) 随机玩家模拟：6 个势力 × 4 个种子 × 25 年，玩家每月随机下达指令（对应 tests/sim.js (2)）。
using System; using System.Linq; using System.Collections.Generic; using Sanguo;
class P2 { static int Main(){
 Console.OutputEncoding=new System.Text.UTF8Encoding(false); int fails=0;
 foreach (var key in new[]{"liubei","cao","dong","kongrong","menghuo","lukang"})
 for (int seed=1; seed<=4; seed++){
  UnityEngine.Random.InitState(seed*13+key.Length);
  var g = GameState.Current = GameState.NewGame(key);
  var rnd = new SeededRandom(seed); // 与 tests/sim.js (2) 的 SG.SeededRandom 相同
  int month=0, attacks=0, defends=0, execs=0, hires=0;
  try {
   for (; month<12*25 && g.PlayerFaction.alive; month++){
     g.tokens = g.TokensFor(g.player);
     int guard=0;
     while (g.tokens>0 && guard++<30){
       var cities = g.CitiesOf(g.player).ToList(); if (cities.Count==0) break;
       var c = cities[rnd.Next(cities.Count)];
       var offs = g.OfficersIn(c).ToList(); if (offs.Count==0) { g.tokens--; continue; }
       var gen = offs[rnd.Next(offs.Count)];
       int op = rnd.Next(9);
       switch(op){
        case 0: Commands.Develop(c, gen, (DevKind)rnd.Next(3)); break;
        case 1: Commands.Recruit(c, gen, Commands.RecruitMax(c, gen)); break;
        case 2: Commands.Train(gen); break;
        case 3: Commands.Search(c, gen); if (Commands.SearchFound!=null && Commands.Hire(Commands.SearchFound, gen, g.player, c.id)) hires++; break;
        case 4: { var d = c.links.Select(i=>g.cities[i]).FirstOrDefault(x=>x.owner==g.player); if (d!=null && !gen.moved) Commands.Move(gen, d); break; }
        case 5: { var t = c.links.Select(i=>g.cities[i]).Where(x=>x.owner>=0&&x.owner!=g.player).SelectMany(x=>g.OfficersIn(x)).FirstOrDefault(x=>!g.IsRuler(x)); if (t!=null){ if (rnd.Next(2)==0) Commands.Discord(gen,t); else Commands.Persuade(gen,t,g.player);} break; }
        case 6: { var f = g.factions.Where(x=>x.alive&&x.id!=g.player).OrderBy(x=>rnd.Next()).FirstOrDefault(); if (f!=null) Commands.Ally(c, g.player, f.id, 100); break; }
        case 7: Commands.BuyFood(c, Math.Min(c.gold, 50)); break;
        default: {
          var t = c.links.Select(i=>g.cities[i]).Where(x=>x.owner!=g.player && !g.Allied(x.owner,g.player)).OrderBy(x=>g.TroopsIn(x)).FirstOrDefault();
          var squad = offs.Where(x=>!x.moved&&x.troops>0).Take(5).ToList();
          if (t!=null && squad.Count>0){ attacks++; var s = Conquest.Prepare(g.player, c, t, squad, Math.Min(c.food,2000), 0); Conquest.AutoResolve(s); Conquest.Apply(s);
            foreach (var cap in s.captives.ToList()){ if (!s.attackerWon) { Conquest.AiDecideCaptives(s, s.defender); break; }
               int ch=rnd.Next(3); bool ruler = cap.faction>=0 && g.factions[cap.faction].ruler==cap.id;
               if (ch==0 && !ruler){ if (!Commands.Hire(cap, g.Ruler(g.player), g.player, s.target.id)) Conquest.Release(cap);} else if (ch==2){ Conquest.Execute(cap); execs++;} else Conquest.Release(cap); }
            foreach (var f in g.factions) if (f.alive && g.CityCount(f.id)==0) g.CheckFactionDeath(f.id); }
          break; }
       }
       g.tokens--;
     }
     var news = new List<string>();
     var pb = StrategyAI.RunAI(news);
     foreach (var s in pb){ defends++; Conquest.AutoResolve(s); Conquest.Apply(s); if (s.captives.Count>0) Conquest.AiDecideCaptives(s, s.attackerWon? s.attacker : s.defender); foreach (var f in g.factions) if (f.alive && g.CityCount(f.id)==0) g.CheckFactionDeath(f.id); if (!g.PlayerFaction.alive) break; }
     if (g.PlayerFaction.alive) StrategyAI.EndMonth();
     foreach (var c in g.cities){ if (c.owner>=0 && !g.factions[c.owner].alive) throw new Exception("dead faction owns "+c.name); if (c.gold<0||c.food<0) throw new Exception("negative res"); }
     foreach (var f in g.factions.Where(f=>f.alive)) { var r=g.generals[f.ruler]; if (r.dead||r.faction!=f.id) throw new Exception("bad ruler "+f.name+" "+r.name+" dead="+r.dead+" fac="+r.faction); }
     foreach (var gen in g.generals) if (gen.faction>=0 && !g.factions[gen.faction].alive) throw new Exception("gen in dead faction "+gen.name);
   }
   Console.WriteLine($"{key} seed {seed}: {month} mo, alive={g.PlayerFaction.alive} cities={g.CityCount(g.player)} gens={g.GeneralsOf(g.player).Count()} atk={attacks} def={defends} hires={hires} execs={execs}");
  } catch (Exception e){ fails++; Console.WriteLine($"{key} seed {seed} FAILED month {month}: {e.Message}\n{e.StackTrace}"); }
 }
 Console.WriteLine(fails==0?"ALL OK":"FAILURES "+fails); return fails; }}
