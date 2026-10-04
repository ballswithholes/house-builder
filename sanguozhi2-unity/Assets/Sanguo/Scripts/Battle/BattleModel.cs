using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sanguo
{
    public class BUnit
    {
        public General gen;
        public int side;          // 0 攻方 1 守方
        public int x, y;
        public int morale = 70;
        public int formation;
        public bool acted;
        public int confused;
        public bool commander;
        public bool alive = true;
        public int Troops { get { return gen.troops; } set { gen.troops = Mathf.Max(0, value); } }
        public FormationDef Form { get { return Defs.Formations[formation]; } }
        public int Mobility { get { return 3 + Form.Move; } }
    }

    public class AttackResult { public int dmg, counter; public bool targetRouted, selfRouted; }
    public class DuelRound { public int who; public int dmg; public int hpA, hpB; }
    public class DuelResult { public bool accepted; public List<DuelRound> rounds = new List<DuelRound>(); public int winner; }

    public class BattleModel
    {
        public BattleSetup setup;
        public int W = Balance.BattleW, H = Balance.BattleH;
        public Terrain[,] map;
        public int[,] burning;
        public float[,] height;
        public List<BUnit> units = new List<BUnit>();
        public int day = 1, side = 0, ap;
        public int[] food = new int[2];
        public Vector2Int castle;
        public int result; // 0 进行中 1 攻方胜 2 守方胜
        public string resultReason = "";
        static GameState G { get { return GameState.Current; } }

        public BattleModel(BattleSetup s)
        {
            setup = s;
            Generate(s.target.id * 31 + G.year);
            food[0] = s.atkFood; food[1] = s.target.food;
            for (int i = 0; i < s.atk.Count; i++)
            {
                var u = new BUnit { gen = s.atk[i], side = 0, x = 0, y = 0 };
                u.commander = i == 0 || G.IsRuler(s.atk[i]);
                u.morale = 60 + s.atk[i].training / 5;
                units.Add(u);
            }
            if (units.Count(u => u.side == 0 && u.commander) > 1) foreach (var u in units.Where(u => u.side == 0 && !G.IsRuler(u.gen))) u.commander = false;
            for (int i = 0; i < s.def.Count; i++)
            {
                var u = new BUnit { gen = s.def[i], side = 1 };
                u.commander = i == 0;
                u.morale = 65 + s.def[i].training / 5;
                units.Add(u);
            }
            Deploy();
            foreach (var u in units) u.formation = AiFormation(u);
            StartSide(0);
        }

        // ---------------------------------------------------------- 地形 --
        void Generate(int seed)
        {
            var rnd = new System.Random(seed);
            map = new Terrain[W, H]; burning = new int[W, H]; height = new float[W, H];
            float ox = (float)rnd.NextDouble() * 100, oy = (float)rnd.NextDouble() * 100;
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    float n = Mathf.PerlinNoise(ox + x * 0.28f, oy + y * 0.28f);
                    float m = Mathf.PerlinNoise(ox + 50 + x * 0.22f, oy + 50 + y * 0.22f);
                    Terrain t = Terrain.Plain;
                    if (n > 0.72f) t = Terrain.Mountain;
                    else if (n > 0.6f) t = Terrain.Hill;
                    else if (m > 0.62f) t = Terrain.Forest;
                    map[x, y] = t;
                    height[x, y] = t == Terrain.Mountain ? 1.1f : t == Terrain.Hill ? 0.45f : n * 0.12f;
                }
            // 河流
            int rx = 4 + rnd.Next(3);
            for (int y = 0; y < H; y++)
            {
                map[rx, y] = Terrain.River; height[rx, y] = -0.15f;
                if (rnd.NextDouble() < 0.3) rx = Mathf.Clamp(rx + (rnd.NextDouble() < 0.5 ? -1 : 1), 3, 7);
                if (y < H - 1 && map[rx, y] != Terrain.River) { map[rx, y] = Terrain.River; height[rx, y] = -0.15f; }
            }
            // 城池
            castle = new Vector2Int(W - 3, H / 2);
            for (int x = W - 5; x < W; x++)
                for (int y = castle.y - 2; y <= castle.y + 2; y++)
                {
                    bool edge = x == W - 5 || y == castle.y - 2 || y == castle.y + 2 || x == W - 1;
                    map[x, y] = edge ? Terrain.Wall : Terrain.Plain;
                    height[x, y] = 0.05f;
                }
            map[W - 5, castle.y] = Terrain.Gate;
            map[W - 3, castle.y - 2] = Terrain.Gate;
            map[W - 3, castle.y + 2] = Terrain.Gate;
            map[castle.x, castle.y] = Terrain.Castle;
            // 出生区域与城门前清理
            for (int y = 0; y < H; y++) for (int x = 0; x < 2; x++) if (map[x, y] == Terrain.Mountain) map[x, y] = Terrain.Plain;
            for (int x = W - 8; x < W - 5; x++) if (map[x, castle.y] == Terrain.Mountain) map[x, castle.y] = Terrain.Plain;
            EnsurePath();
        }

        // 保证攻方能抵达城门
        void EnsurePath()
        {
            var gate = new Vector2Int(W - 5, castle.y);
            for (int guard = 0; guard < 30; guard++)
            {
                var d = Distances(new Vector2Int(0, H / 2), null, 999, -1);
                if (d[gate.x, gate.y] < 999) return;
                // 拆除一座最近的山
                for (int x = 0; x < W - 5; x++) for (int y = 0; y < H; y++)
                        if (map[x, y] == Terrain.Mountain && Mathf.Abs(y - castle.y) <= guard / 3 + 1) { map[x, y] = Terrain.Hill; height[x, y] = 0.45f; }
            }
        }

        void Deploy()
        {
            var atk = units.Where(u => u.side == 0).ToList();
            var spots0 = new List<Vector2Int>();
            for (int i = 0; i < H; i++) { int y = H / 2 + ((i % 2 == 0) ? i / 2 : -(i + 1) / 2); if (y >= 0 && y < H) { spots0.Add(new Vector2Int(1, y)); spots0.Add(new Vector2Int(0, y)); } }
            int k = 0;
            foreach (var u in atk) { while (!Passable(spots0[k].x, spots0[k].y)) k++; u.x = spots0[k].x; u.y = spots0[k].y; k++; }
            var def = units.Where(u => u.side == 1).ToList();
            var spots1 = new List<Vector2Int> { castle, new Vector2Int(W - 4, castle.y), new Vector2Int(W - 6, castle.y), new Vector2Int(W - 4, castle.y - 1), new Vector2Int(W - 4, castle.y + 1), new Vector2Int(W - 2, castle.y - 1), new Vector2Int(W - 2, castle.y + 1) };
            k = 0;
            foreach (var u in def)
            {
                while (k < spots1.Count && (!Passable(spots1[k].x, spots1[k].y) || UnitAt(spots1[k].x, spots1[k].y) != null)) k++;
                var p = k < spots1.Count ? spots1[k] : castle;
                u.x = p.x; u.y = p.y; k++;
            }
        }

        // ---------------------------------------------------------- 查询 --
        public bool InBounds(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H; }
        public bool Passable(int x, int y) { return InBounds(x, y) && Defs.TerrainCost(map[x, y]) < 99; }
        public BUnit UnitAt(int x, int y) { return units.FirstOrDefault(u => u.alive && u.x == x && u.y == y); }
        public IEnumerable<BUnit> Alive(int s) { return units.Where(u => u.alive && u.side == s); }
        public BUnit Commander(int s) { return units.FirstOrDefault(u => u.side == s && u.commander); }
        public static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        public static int Dist(BUnit a, BUnit b) { return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y); }

        // 可移动范围（机动力 BFS）。敌军阻挡，友军可穿越但不可停留。
        public int[,] Distances(Vector2Int from, BUnit mover, int limit, int moverSide)
        {
            var d = new int[W, H];
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) d[x, y] = 999;
            d[from.x, from.y] = 0;
            var open = new List<Vector2Int> { from };
            while (open.Count > 0)
            {
                int bi = 0;
                for (int i = 1; i < open.Count; i++) if (d[open[i].x, open[i].y] < d[open[bi].x, open[bi].y]) bi = i;
                var c = open[bi]; open.RemoveAt(bi);
                foreach (var dir in Dirs)
                {
                    int nx = c.x + dir.x, ny = c.y + dir.y;
                    if (!InBounds(nx, ny)) continue;
                    int cost = Defs.TerrainCost(map[nx, ny]);
                    if (cost >= 99) continue;
                    if (moverSide >= 0) { var o = UnitAt(nx, ny); if (o != null && o.side != moverSide) continue; }
                    int nd = d[c.x, c.y] + cost;
                    if (nd > limit || nd >= d[nx, ny]) continue;
                    d[nx, ny] = nd; open.Add(new Vector2Int(nx, ny));
                }
            }
            return d;
        }
        public List<Vector2Int> MoveTargets(BUnit u)
        {
            var d = Distances(new Vector2Int(u.x, u.y), u, u.Mobility, u.side);
            var list = new List<Vector2Int>();
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++)
                    if (d[x, y] <= u.Mobility && (UnitAt(x, y) == null || UnitAt(x, y) == u)) list.Add(new Vector2Int(x, y));
            return list;
        }
        // 移动路径（含起点与终点），用于画面动画
        public List<Vector2Int> PathTo(BUnit u, Vector2Int to)
        {
            var d = Distances(new Vector2Int(u.x, u.y), u, 999, u.side);
            var path = new List<Vector2Int> { to };
            var cur = to;
            for (int guard = 0; guard < 64 && (cur.x != u.x || cur.y != u.y); guard++)
            {
                Vector2Int best = cur; int bd = 9999;
                foreach (var dir in Dirs)
                {
                    var n = cur + dir;
                    if (!InBounds(n.x, n.y)) continue;
                    if (d[n.x, n.y] < bd && d[n.x, n.y] + Defs.TerrainCost(map[cur.x, cur.y]) == d[cur.x, cur.y]) { bd = d[n.x, n.y]; best = n; }
                }
                if (best == cur) break;
                cur = best; path.Add(cur);
            }
            path.Reverse();
            return path;
        }

        public List<BUnit> AdjacentEnemies(BUnit u) { return units.Where(o => o.alive && o.side != u.side && Dist(o, u) == 1).ToList(); }

        // ---------------------------------------------------------- 回合 --
        public int ActionPoints(int s)
        {
            int f = s == 0 ? setup.attacker : setup.defender;
            int fame = f >= 0 ? G.factions[f].fame : 40;
            return Mathf.Clamp(Balance.ActionPointsBase + fame / Balance.ActionPointsFameDiv, 2, Balance.ActionPointsMax);
        }
        void StartSide(int s)
        {
            side = s; ap = ActionPoints(s);
            foreach (var u in units) if (u.side == s) u.acted = u.confused > 0;
        }
        public bool CanAct(BUnit u) { return u.alive && u.side == side && !u.acted && ap > 0 && u.confused <= 0 && result == 0; }
        public void Spend(BUnit u) { if (!u.acted) { u.acted = true; ap--; } }

        // 结束本方行动，返回 true 表示新的一天开始
        public bool EndSide()
        {
            if (side == 0) { StartSide(1); return false; }
            EndDay();
            StartSide(0);
            return true;
        }

        public List<string> dayLog = new List<string>();
        void EndDay()
        {
            dayLog.Clear();
            for (int s = 0; s < 2; s++)
            {
                int troops = Alive(s).Sum(u => u.Troops);
                food[s] -= Mathf.Max(1, troops / Balance.BattleFoodPerTroops);
                if (food[s] <= 0)
                {
                    food[s] = 0;
                    foreach (var u in Alive(s)) u.morale = Mathf.Max(0, u.morale - 15);
                    dayLog.Add((s == 0 ? "攻方" : "守方") + "粮草断绝，士气大跌！");
                }
            }
            foreach (var u in units.Where(u => u.alive))
            {
                if (burning[u.x, u.y] > 0) { int d = Mathf.Max(30, u.Troops / 12); u.Troops -= d; dayLog.Add(u.gen.name + "被火焰灼伤，损兵 " + d); CheckRout(u); }
                if (u.confused > 0) u.confused--;
            }
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (burning[x, y] > 0) burning[x, y]--;
            day++;
            CheckEnd();
            if (result == 0 && day > Balance.BattleDays) { result = 2; resultReason = "攻城期限已过，攻方撤退。"; }
        }

        // ---------------------------------------------------------- 行动 --
        public void Move(BUnit u, Vector2Int to) { u.x = to.x; u.y = to.y; CheckEnd(); }

        float AtkPower(BUnit u)
        {
            return u.Troops * (0.5f + u.gen.war / 200f) * u.Form.Atk * (0.6f + u.gen.training / 250f) * (0.6f + u.morale / 250f);
        }
        float DefFactor(BUnit u) { return u.Form.Def * Defs.TerrainDef(map[u.x, u.y]) * (0.8f + u.gen.training / 500f); }

        public AttackResult Attack(BUnit a, BUnit t)
        {
            var r = new AttackResult();
            r.dmg = Mathf.Max(10, Mathf.RoundToInt(AtkPower(a) * Balance.DamageK / DefFactor(t) * Random.Range(0.85f, 1.15f)));
            r.dmg = Mathf.Min(r.dmg, t.Troops);
            t.Troops -= r.dmg;
            t.morale = Mathf.Max(0, t.morale - Mathf.RoundToInt(r.dmg * 40f / Mathf.Max(200, t.Troops + r.dmg)));
            r.targetRouted = CheckRout(t);
            if (!r.targetRouted)
            {
                r.counter = Mathf.Max(5, Mathf.RoundToInt(AtkPower(t) * Balance.DamageK * Balance.CounterRatio / DefFactor(a) * Random.Range(0.85f, 1.15f)));
                r.counter = Mathf.Min(r.counter, a.Troops);
                a.Troops -= r.counter;
                a.morale = Mathf.Max(0, a.morale - Mathf.RoundToInt(r.counter * 30f / Mathf.Max(200, a.Troops + r.counter)));
                r.selfRouted = CheckRout(a);
            }
            a.morale = Mathf.Min(100, a.morale + (r.dmg > r.counter ? 3 : 0));
            CheckEnd();
            return r;
        }

        bool CheckRout(BUnit u)
        {
            if (!u.alive) return true;
            if (u.Troops < 50 || u.morale <= 0)
            {
                u.Troops = 0; u.alive = false;
                setup.routed.Add(u.gen.id);
                return true;
            }
            return false;
        }

        public bool TacticUsable(BUnit u, TacticDef t, out string why)
        {
            why = "";
            if (u.gen.intel < t.ReqInt) { why = "智力不足（需 " + t.ReqInt + "）"; return false; }
            if (t.Kind == TacticKind.Rockfall && map[u.x, u.y] != Terrain.Hill) { why = "须立于山丘"; return false; }
            return true;
        }
        public int TacticRange(TacticDef t) { return t.Kind == TacticKind.Inspire ? 0 : t.Kind == TacticKind.Confuse ? 3 : 2; }
        public List<BUnit> TacticTargets(BUnit u, TacticDef t)
        {
            if (t.Kind == TacticKind.Inspire) return new List<BUnit> { u };
            int r = TacticRange(t);
            return units.Where(o => o.alive && o.side != u.side && Dist(o, u) <= r && (t.Kind != TacticKind.Fire || map[o.x, o.y] != Terrain.River)).ToList();
        }

        // 返回 (成功, 损伤)
        public KeyValuePair<bool, int> UseTactic(BUnit u, TacticDef t, BUnit target)
        {
            if (t.Kind == TacticKind.Inspire)
            {
                foreach (var o in units.Where(o => o.alive && o.side == u.side && Dist(o, u) <= 1)) o.morale = Mathf.Min(100, o.morale + 12 + u.gen.intel / 10);
                return new KeyValuePair<bool, int>(true, 0);
            }
            float p = Mathf.Clamp(0.5f + (u.gen.intel - target.gen.intel) / 110f, 0.12f, 0.92f);
            if (Random.value > p) return new KeyValuePair<bool, int>(false, 0);
            int dmg = 0;
            if (t.Kind == TacticKind.Confuse) { target.confused = 2; target.acted = true; }
            else
            {
                float baseD = (t.Kind == TacticKind.Fire ? 260 : 340) + u.gen.intel * (t.Kind == TacticKind.Fire ? 11 : 13);
                var tt = map[target.x, target.y];
                if (t.Kind == TacticKind.Fire) { if (tt == Terrain.Forest) baseD *= 1.8f; if (tt == Terrain.Castle || tt == Terrain.Gate) baseD *= 0.6f; burning[target.x, target.y] = 2; }
                baseD *= Mathf.Clamp(target.Troops / 2500f, 0.4f, 1.4f);
                dmg = Mathf.Min(target.Troops, Mathf.RoundToInt(baseD * Random.Range(0.85f, 1.15f)));
                target.Troops -= dmg;
                target.morale = Mathf.Max(0, target.morale - 10);
                CheckRout(target);
            }
            CheckEnd();
            return new KeyValuePair<bool, int>(true, dmg);
        }

        public DuelResult Duel(BUnit a, BUnit b)
        {
            var r = new DuelResult();
            r.accepted = b.gen.war >= a.gen.war - 8 || Random.value < 0.25f || b.commander && Random.value < 0.4f;
            if (!r.accepted) { b.morale = Mathf.Max(0, b.morale - 12); a.morale = Mathf.Min(100, a.morale + 8); return r; }
            int hpA = 100, hpB = 100;
            for (int i = 0; i < 30 && hpA > 0 && hpB > 0; i++)
            {
                int who = Random.value < a.gen.war / (float)(a.gen.war + b.gen.war) ? 0 : 1;
                var striker = who == 0 ? a : b;
                int dmg = Mathf.RoundToInt(Random.Range(6, 16) * (0.6f + striker.gen.war / 120f));
                if (who == 0) hpB = Mathf.Max(0, hpB - dmg); else hpA = Mathf.Max(0, hpA - dmg);
                r.rounds.Add(new DuelRound { who = who, dmg = dmg, hpA = hpA, hpB = hpB });
            }
            r.winner = hpA >= hpB ? 0 : 1;
            var loser = r.winner == 0 ? b : a;
            var winner = r.winner == 0 ? a : b;
            loser.Troops = 0; loser.alive = false; setup.routed.Add(loser.gen.id);
            foreach (var o in Alive(loser.side)) o.morale = Mathf.Max(0, o.morale - 8);
            winner.morale = Mathf.Min(100, winner.morale + 15);
            CheckEnd();
            return r;
        }

        public void ChangeFormation(BUnit u, int f) { u.formation = f; }
        public bool FormationAllowed(BUnit u, int f) { var d = Defs.Formations[f]; return u.gen.intel >= d.ReqInt && u.gen.war >= d.ReqWar; }

        // ---------------------------------------------------------- 胜负 --
        public void CheckEnd()
        {
            if (result != 0) return;
            var c0 = Commander(0); var c1 = Commander(1);
            if (!Alive(1).Any()) { result = 1; resultReason = "守军全军覆没！"; }
            else if (!Alive(0).Any()) { result = 2; resultReason = "攻方全军覆没！"; }
            else if (c1 != null && !c1.alive) { result = 1; resultReason = "守城主将败走，城池陷落！"; }
            else if (c0 != null && !c0.alive) { result = 2; resultReason = "攻方主将败走，全军撤退！"; }
            else if (Alive(0).Any(u => u.x == castle.x && u.y == castle.y)) { result = 1; resultReason = "攻入本城，城池陷落！"; }
        }

        // ---------------------------------------------------------- 电脑 --
        public int AiFormation(BUnit u)
        {
            int best = 0; float score = -1;
            for (int i = 0; i < Defs.Formations.Length; i++)
            {
                if (!FormationAllowed(u, i)) continue;
                var f = Defs.Formations[i];
                float s = u.side == 0 ? f.Atk * 1.2f + f.Def * 0.8f + f.Move * 0.05f : f.Atk * 0.7f + f.Def * 1.3f;
                if (s > score) { score = s; best = i; }
            }
            return best;
        }

        // 电脑为一个单位决定行动；返回描述行动的对象供画面播放
        public class AiPlan { public BUnit unit; public Vector2Int? move; public string kind; public BUnit target; public TacticDef tactic; }

        public AiPlan PlanFor(BUnit u)
        {
            var plan = new AiPlan { unit = u };
            var enemies = Alive(1 - u.side).ToList();
            if (enemies.Count == 0) return plan;
            Vector2Int goal;
            if (u.side == 0)
            {
                var near = enemies.OrderBy(e => Dist(e, u)).First();
                goal = Dist(near, u) <= 4 ? new Vector2Int(near.x, near.y) : castle;
            }
            else
            {
                var near = enemies.OrderBy(e => Dist(e, u)).First();
                bool threatened = enemies.Any(e => Mathf.Abs(e.x - castle.x) + Mathf.Abs(e.y - castle.y) <= 6);
                goal = threatened || Dist(near, u) <= 3 ? new Vector2Int(near.x, near.y) : new Vector2Int(u.x, u.y);
                if (u.commander && !(Dist(near, u) <= 2)) goal = castle;
            }
            // 移动
            if (AdjacentEnemies(u).Count == 0)
            {
                var targets = MoveTargets(u);
                var dist = Distances(goal, null, 999, -1);
                Vector2Int best = new Vector2Int(u.x, u.y); float bs = Score(u, best, dist, goal);
                foreach (var t in targets) { float s = Score(u, t, dist, goal); if (s < bs) { bs = s; best = t; } }
                if (best.x != u.x || best.y != u.y) plan.move = best;
            }
            return plan;
        }
        float Score(BUnit u, Vector2Int p, int[,] dist, Vector2Int goal)
        {
            float s = dist[p.x, p.y];
            if (s >= 999) s = Mathf.Abs(p.x - goal.x) + Mathf.Abs(p.y - goal.y) + 20;
            s -= Defs.TerrainDef(map[p.x, p.y]) * 0.6f;
            if (p.x == goal.x && p.y == goal.y && UnitAt(goal.x, goal.y) != null && UnitAt(goal.x, goal.y) != u) s += 50;
            return s;
        }

        // 移动后决定攻击方式
        public AiPlan PlanAction(BUnit u)
        {
            var plan = new AiPlan { unit = u, kind = "none" };
            var adj = AdjacentEnemies(u);
            // 策略
            foreach (var t in Defs.Tactics)
            {
                string why;
                if (t.Kind == TacticKind.Inspire || !TacticUsable(u, t, out why)) continue;
                var tg = TacticTargets(u, t).OrderByDescending(o => (t.Kind == TacticKind.Fire && map[o.x, o.y] == Terrain.Forest ? 3000 : 0) + o.Troops).FirstOrDefault();
                if (tg != null && u.gen.intel > tg.gen.intel + 5 && Random.value < (u.gen.intel > 80 ? 0.6f : 0.35f) && (t.Kind != TacticKind.Confuse || adj.Count == 0))
                { plan.kind = "tactic"; plan.tactic = t; plan.target = tg; return plan; }
            }
            if (adj.Count == 0)
            {
                if (u.morale < 45 && u.gen.intel >= 40) { plan.kind = "tactic"; plan.tactic = Defs.Tactics[3]; plan.target = u; }
                return plan;
            }
            var weakest = adj.OrderBy(o => o.Troops).First();
            var duelT = adj.Where(o => u.gen.war >= o.gen.war + 12).OrderByDescending(o => o.commander ? 1 : 0).FirstOrDefault();
            if (duelT != null && Random.value < 0.3f) { plan.kind = "duel"; plan.target = duelT; return plan; }
            plan.kind = "attack"; plan.target = weakest;
            return plan;
        }
    }
}
