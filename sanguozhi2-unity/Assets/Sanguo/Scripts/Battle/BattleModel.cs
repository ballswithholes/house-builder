// 三国志II 霸王的大陆 · 战术战斗规则（对应网页版 js/battle-model.js）
//
// 第二版新增（必杀技，招式数据见 Battle/Specials.cs）：
//   SpecialUsable(u, out why)            该部队现在能否施展必杀技（每战一次：u.specialUsed）
//   SpecialTargets(u) → List<BUnit>      可选目标（自身为中心的招式返回 [u]）
//   UseSpecial(u, target) → SpecialResult 结算；与 Attack() 一样不调用 Spend(u)，由调用方随后 Spend
//   SpecialPlan(u, target) / SpecialValue(u, plan) / AiSpecial(u)   纯计算：效果清单、期望价值、电脑决策（不改状态、不耗随机数）
//   PlanAction(u) 可返回 kind "special"（plan.target 为目标）
//   限时加成 u.mods（BMod：atk / def / counter 乘数、move 加值、dot 每日损兵比例、days）：攻击力、防御系数、反击、机动力、
//     计策伤害都会乘上；dot（中毒）在每日结束时发作并 days − 1，其余加成在该部队本方行动开始时 days − 1（攻守双方施展的持续
//     时间对称）。混乱同理按 ConfuseCount 补正，见 ConfusedPhases
//   DuelAccepts(a, b) → bool、DuelFinish(a, b, winnerIsA, rounds) → DuelResult：Duel() 拆成的两步（随机数顺序不变），
//     供格斗单挑画面结束后结算
//   BattleModel.specialsEnabled：false 时规则与电脑完全回到第一版（平衡对比测试用）。默认 true（同网页版）；
//     无头测试（Tests~/sim）一律显式置 true 与网页版对照
// 数值按 double 计算（与网页版一致）；战场地形由 SeededRandom 生成，同一种子下与网页版完全相同。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sanguo
{
    // 限时加成（必杀技施加）。src = 来源（招式 id，同来源不叠加、刷新）；未设的项为 null
    public class BMod
    {
        public string src;
        public double? atk, def, counter, dot;
        public int? move;
        public int days;
        public BMod Clone() { return (BMod)MemberwiseClone(); }
    }

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
        public bool specialUsed;                      // 本战已施展必杀技
        public List<BMod> mods = new List<BMod>();    // 限时加成
        public int troops0 = -1;                      // 出阵兵力：回复类必杀不得超过此数（-1 = 未知）
        public int Troops { get { return gen.troops; } set { gen.troops = Mathf.Max(0, value); } }
        public FormationDef Form { get { return Defs.Formations[formation]; } }
        public int Mobility
        {
            get
            {
                int mv = 0;
                if (mods != null) foreach (var m in mods) if (m.move.HasValue) mv += m.move.Value;
                return 3 + Form.Move + mv;
            }
        }
    }

    public class AttackResult { public int dmg, counter; public bool targetRouted, selfRouted; }
    public class DuelRound { public int who; public int dmg; public int hpA, hpB; }
    public class DuelResult { public bool accepted; public List<DuelRound> rounds = new List<DuelRound>(); public int winner; }

    // ---- 必杀技的效果清单（SpecialPlan 的结果；纯计算）----
    public class SpHit { public BUnit unit; public double amt; public bool primary, onFail; }
    public class SpFoe { public BUnit unit; public int morale; public double p; public int turns; public BMod mod; }
    public class SpAlly { public BUnit unit; public int morale, heal; public bool cure; public BMod mod; }
    public class SpBurn { public int x, y, days; }
    public class SpMove { public Vector2Int from, to; }
    public class SpPush { public BUnit unit; public Vector2Int from, to; }
    public class SpKill { public BUnit unit; public double p; }
    public class ChargeLand { public int x, y, dx, dy, dash; public List<Vector2Int> path; }
    public class SpPlan
    {
        public Special sp; public string kind; public BUnit unit, target;
        public Vector2Int center; public List<Vector2Int> area = new List<Vector2Int>();
        public List<SpHit> dmg = new List<SpHit>(); public List<SpFoe> foe = new List<SpFoe>(); public List<SpAlly> ally = new List<SpAlly>();
        public List<SpBurn> burn = new List<SpBurn>(); public List<Vector2Int> douse = new List<Vector2Int>(); public List<BUnit> refresh = new List<BUnit>();
        public int ap; public SpMove move; public SpPush push; public BUnit blocked; public SpKill kill;
        public double drain; public List<BUnit> pool; public bool seq; public double areaK = 1;
    }

    // ---- 必杀技的结算结果（UseSpecial）----
    public class SpecialHit { public BUnit unit; public int dmg; public bool primary, killed; }
    public class SpecialHeal { public BUnit unit; public int amount; }
    public class SpecialResult
    {
        public bool success; public string name = "", kind = "", why = ""; public Special sp;
        public int dmg, heal, gained;
        public List<SpecialHit> hits = new List<SpecialHit>(); public List<SpecialHeal> healed = new List<SpecialHeal>();
        public List<BUnit> confused = new List<BUnit>(), resisted = new List<BUnit>(), cursed = new List<BUnit>(), buffed = new List<BUnit>(), refreshed = new List<BUnit>();
        public List<Vector2Int> burned = new List<Vector2Int>(), doused = new List<Vector2Int>();
        public SpMove moved; public List<SpPush> pushed = new List<SpPush>(); public BUnit blocked, killed;
        public List<BUnit> routed = new List<BUnit>(), affected = new List<BUnit>();
        public List<Vector2Int> area = new List<Vector2Int>(); public Vector2Int? center; public BUnit target;
    }
    public class AiSpecialChoice { public BUnit target; public double value; }

    public class BattleModel
    {
        // false：不使用必杀技（平衡对比测试）。默认 true（同网页版）：BattleController 已接入必杀技
        // （行动菜单「必杀」、AiPhase 的 "special" 分支、特写与特效）。
        // 只影响亲自指挥 / 观战的战术战斗；电脑之间的战斗走 Conquest.AutoResolve，与此无关。
        public static bool specialsEnabled = true;

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
        public List<string> dayLog = new List<string>();
        static GameState G { get { return GameState.Current; } }

        // float 常数还原为十进制写法的 double（0.85f → 0.85），与网页版数值一致
        static double D(float f) { return (double)(decimal)f; }
        static readonly double DamageK = D(Balance.DamageK), CounterRatio = D(Balance.CounterRatio);
        static readonly double[] FormAtk = Defs.Formations.Select(f => D(f.Atk)).ToArray(), FormDef = Defs.Formations.Select(f => D(f.Def)).ToArray();
        static double TerrainDef(Terrain t) { return D(Defs.TerrainDef(t)); }
        static int RInt(double v) { return (int)Math.Round(v); }   // = Mathf.RoundToInt（银行家舍入）
        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        static double Clamp01(double v) { return Clamp(v, 0, 1); }
        static int StatOf(General g, string s) { return s == "intel" ? g.intel : s == "pol" ? g.pol : g.war; }
        static double StatScale(int v) { return 0.8 + Mathf.Clamp(v, 0, 120) / 500.0; }

        public BattleModel(BattleSetup s)
        {
            setup = s;
            Generate(s.target.id * 31 + G.year);
            food[0] = s.atkFood; food[1] = s.target.food;
            for (int i = 0; i < s.atk.Count; i++)
            {
                var u = new BUnit { gen = s.atk[i], side = 0, x = 0, y = 0 };
                u.troops0 = s.atk[i].troops;
                u.commander = i == 0 || G.IsRuler(s.atk[i]);
                u.morale = 60 + s.atk[i].training / 5;
                units.Add(u);
            }
            if (units.Count(u => u.side == 0 && u.commander) > 1) foreach (var u in units.Where(u => u.side == 0 && !G.IsRuler(u.gen))) u.commander = false;
            for (int i = 0; i < s.def.Count; i++)
            {
                var u = new BUnit { gen = s.def[i], side = 1 };
                u.troops0 = s.def[i].troops;
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
            var rnd = new SeededRandom(seed);
            map = new Terrain[W, H]; burning = new int[W, H]; height = new float[W, H];
            float ox = (float)(rnd.NextDouble() * 100), oy = (float)(rnd.NextDouble() * 100);
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
            foreach (var u in atk)
            {
                while (k < spots0.Count && !Passable(spots0[k].x, spots0[k].y)) k++;
                var p = k < spots0.Count ? spots0[k] : new Vector2Int(0, H / 2);
                u.x = p.x; u.y = p.y; k++;
            }
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
        public BUnit UnitAt(int x, int y) { foreach (var u in units) if (u.alive && u.x == x && u.y == y) return u; return null; }
        public IEnumerable<BUnit> Alive(int s) { return units.Where(u => u.alive && u.side == s); }
        public BUnit Commander(int s) { foreach (var u in units) if (u.side == s && u.commander) return u; return null; }
        public static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        public static int Dist(BUnit a, BUnit b) { return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y); }
        static int Dist(BUnit a, Vector2Int b) { return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y); }

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
            int mob = u.Mobility;
            var d = Distances(new Vector2Int(u.x, u.y), u, mob, u.side);
            var list = new List<Vector2Int>();
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++)
                    if (d[x, y] <= mob) { var o = UnitAt(x, y); if (o == null || o == u) list.Add(new Vector2Int(x, y)); }
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
        // 开始一方的行动（测试与特殊流程也可直接调用）
        public void StartSide(int s)
        {
            side = s; ap = ActionPoints(s);
            foreach (var u in units) if (u.side == s) u.acted = u.confused > 0;
            foreach (var u in units) if (u.side == s && u.mods != null && u.mods.Count > 0) ExpireMods(u);   // 必杀技加成按本方行动计时
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
            foreach (var u in units.Where(x => x.alive).ToList())
            {
                if (burning[u.x, u.y] > 0) { int d = Mathf.Max(30, u.Troops / 12); u.Troops -= d; dayLog.Add(u.gen.name + "被火焰灼伤，损兵 " + d); CheckRout(u); }
                if (u.confused > 0) u.confused--;
            }
            // 必杀技的限时效果（中毒损兵、加成到期）
            foreach (var u in units) if (u.alive && u.mods != null && u.mods.Count > 0) TickMods(u);
            for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (burning[x, y] > 0) burning[x, y]--;
            day++;
            CheckEnd();
            if (result == 0 && day > Balance.BattleDays) { result = 2; resultReason = "攻城期限已过，攻方撤退。"; }
        }

        // ---------------------------------------------------------- 行动 --
        public void Move(BUnit u, Vector2Int to) { u.x = to.x; u.y = to.y; CheckEnd(); }

        public double AtkPower(BUnit u)
        {
            return u.Troops * (0.5 + u.gen.war / 200.0) * FormAtk[u.formation] * (0.6 + u.gen.training / 250.0) * (0.6 + u.morale / 250.0) * ModK(u, "atk");
        }
        public double DefFactor(BUnit u) { return FormDef[u.formation] * TerrainDef(map[u.x, u.y]) * (0.8 + u.gen.training / 500.0) * ModK(u, "def"); }

        public AttackResult Attack(BUnit a, BUnit t)
        {
            var r = new AttackResult();
            r.dmg = Math.Max(10, RInt(AtkPower(a) * DamageK / DefFactor(t) * UnityEngine.Random.Range(0.85f, 1.15f)));
            r.dmg = Math.Min(r.dmg, t.Troops);
            t.Troops -= r.dmg;
            t.morale = Math.Max(0, t.morale - RInt(r.dmg * 40.0 / Math.Max(200, t.Troops + r.dmg)));
            r.targetRouted = CheckRout(t);
            if (!r.targetRouted)
            {
                r.counter = Math.Max(5, RInt(AtkPower(t) * DamageK * CounterRatio * ModK(t, "counter") / DefFactor(a) * UnityEngine.Random.Range(0.85f, 1.15f)));
                r.counter = Math.Min(r.counter, a.Troops);
                a.Troops -= r.counter;
                a.morale = Math.Max(0, a.morale - RInt(r.counter * 30.0 / Math.Max(200, a.Troops + r.counter)));
                r.selfRouted = CheckRout(a);
            }
            a.morale = Math.Min(100, a.morale + (r.dmg > r.counter ? 3 : 0));
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

        public bool TacticUsable(BUnit u, TacticDef t) { string why; return TacticUsable(u, t, out why); }
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
            double p = Clamp(0.5 + (u.gen.intel - target.gen.intel) / 110.0, 0.12, 0.92);
            if (UnityEngine.Random.value > p) return new KeyValuePair<bool, int>(false, 0);
            int dmg = 0;
            if (t.Kind == TacticKind.Confuse) { target.confused = 2; target.acted = true; }
            else
            {
                double baseD = (t.Kind == TacticKind.Fire ? 260 : 340) + u.gen.intel * (t.Kind == TacticKind.Fire ? 11 : 13);
                var tt = map[target.x, target.y];
                if (t.Kind == TacticKind.Fire)
                {
                    if (tt == Terrain.Forest) baseD *= 1.8;
                    if (tt == Terrain.Castle || tt == Terrain.Gate) baseD *= 0.6;
                    burning[target.x, target.y] = 2;
                }
                baseD *= Clamp(target.Troops / 2500.0, 0.4, 1.4);
                baseD /= ModK(target, "def");            // 坚守等防御加成（无加成时为 1）
                dmg = Math.Min(target.Troops, RInt(baseD * UnityEngine.Random.Range(0.85f, 1.15f)));
                target.Troops -= dmg;
                target.morale = Mathf.Max(0, target.morale - 10);
                CheckRout(target);
            }
            CheckEnd();
            return new KeyValuePair<bool, int>(true, dmg);
        }

        // 单挑：是否应战（拒绝时双方士气变化）。随机数调用与第一版 Duel() 完全相同
        public bool DuelAccepts(BUnit a, BUnit b)
        {
            bool ok = b.gen.war >= a.gen.war - 8 || UnityEngine.Random.value < 0.25 || (b.commander && UnityEngine.Random.value < 0.4);
            if (!ok) { b.morale = Mathf.Max(0, b.morale - 12); a.morale = Mathf.Min(100, a.morale + 8); }
            return ok;
        }
        // 单挑：结算胜负后果（败者溃散、其军士气 −8、胜者士气 +15）。供格斗单挑画面结束后调用
        public DuelResult DuelFinish(BUnit a, BUnit b, bool winnerIsA, List<DuelRound> rounds = null)
        {
            var winner = winnerIsA ? a : b; var loser = winnerIsA ? b : a;
            loser.Troops = 0; loser.alive = false; setup.routed.Add(loser.gen.id);
            foreach (var o in Alive(loser.side)) o.morale = Mathf.Max(0, o.morale - 8);
            winner.morale = Mathf.Min(100, winner.morale + 15);
            CheckEnd();
            return new DuelResult { accepted = true, rounds = rounds ?? new List<DuelRound>(), winner = winnerIsA ? 0 : 1 };
        }
        public DuelResult Duel(BUnit a, BUnit b)
        {
            var r = new DuelResult();
            r.accepted = DuelAccepts(a, b);
            if (!r.accepted) return r;
            int hpA = 100, hpB = 100;
            for (int i = 0; i < 30 && hpA > 0 && hpB > 0; i++)
            {
                int who = UnityEngine.Random.value < (double)a.gen.war / (a.gen.war + b.gen.war) ? 0 : 1;
                var striker = who == 0 ? a : b;
                int dmg = RInt(UnityEngine.Random.Range(6, 16) * (0.6 + striker.gen.war / 120.0));
                if (who == 0) hpB = Math.Max(0, hpB - dmg); else hpA = Math.Max(0, hpA - dmg);
                r.rounds.Add(new DuelRound { who = who, dmg = dmg, hpA = hpA, hpB = hpB });
            }
            r.winner = hpA >= hpB ? 0 : 1;
            DuelFinish(a, b, r.winner == 0, r.rounds);
            return r;
        }

        public void ChangeFormation(BUnit u, int f) { u.formation = f; }
        public bool FormationAllowed(BUnit u, int f) { var d = Defs.Formations[f]; return u.gen.intel >= d.ReqInt && u.gen.war >= d.ReqWar; }

        // ======================================================== 必杀技 --
        // 招式数据由 Specials.Of(gen) 提供。specialsEnabled = false 时必杀技一律不可用，电脑也不会考虑。
        public Special SpecialOf(BUnit u)
        {
            if (!specialsEnabled || u == null || u.gen == null) return null;
            return Specials.Of(u.gen);
        }

        // ---- 限时加成 ----
        public double ModK(BUnit u, string key)
        {
            var ms = u.mods;
            if (ms == null || ms.Count == 0) return 1;
            double k = 1;
            foreach (var m in ms)
            {
                double? v = key == "atk" ? m.atk : key == "def" ? m.def : key == "counter" ? m.counter : key == "dot" ? m.dot : null;
                if (v.HasValue) k *= v.Value;
            }
            return k;
        }
        public void AddMod(BUnit u, BMod mod)
        {
            if (u.mods == null) u.mods = new List<BMod>();
            int i = u.mods.FindIndex(m => m.src == mod.src);
            if (i >= 0) u.mods[i] = mod; else u.mods.Add(mod);
        }
        // 计时规则（攻守双方对称）：
        //   中毒（dot > 0）：每日结束时（EndDay）损兵一次、days −1——无论谁施毒，都恰好发作 days 次。
        //   其余加成（攻 / 防 / 反击 / 机动）：在该部队本方下一次行动开始时（StartSide）days −1、到 0 移除。
        //     于是「持续 N 日」= 本方剩余的这次行动 + 敌方 N 次行动 + 本方之后 N−1 次行动，攻方施展与守方施展一样长。
        void TickMods(BUnit u)
        {
            foreach (var m in u.mods)
            {
                if (!(m.dot > 0)) continue;
                if (u.alive)
                {
                    int d = Math.Max(20, RInt(u.Troops * m.dot.Value));
                    u.Troops -= d;
                    dayLog.Add(u.gen.name + "中毒，损兵 " + d);
                    CheckRout(u);
                }
                m.days--;
            }
            u.mods = u.mods.Where(m => m.days > 0).ToList();
        }
        void ExpireMods(BUnit u)
        {
            foreach (var m in u.mods) if (!(m.dot > 0)) m.days--;
            u.mods = u.mods.Where(m => m.days > 0).ToList();
        }
        // 混乱的计数在每日结束时（守方行动之后）统一 −1。必杀技造成的「混乱 N 日」要让目标恰好失去 N 次本方行动：
        //   攻方行动中混乱守军 → 守军当日即失去行动，计数 N；守方行动中混乱攻军 → 过日先 −1，计数须为 N + 1。
        public int ConfuseCount(BUnit e, int turns) { return turns + (e.side == 1 && side == 0 ? 0 : 1); }
        // 部队 x 今后还会因混乱失去的本方行动次数（不含正在进行的本方行动）
        public int ConfusedPhases(BUnit x)
        {
            int c = x.confused;
            if (c <= 0) return 0;
            return x.side == 1 && side == 0 ? c : c - 1;
        }

        // ---- 计算基准 ----
        // 一次普通攻击的期望伤害（不含随机浮动）
        public double AtkExpect(BUnit a, BUnit t) { return AtkPower(a) * DamageK / DefFactor(t); }
        public double RoutBonus(BUnit t) { return t.commander ? 6000 : 300; }
        // 必杀技伤害基准（期望值）。
        //   武力系：一次普通攻击的期望伤害 × 武勇系数（0.8 + 武力/500）；pierce 时无视地形防御。
        //   智力 / 政治系：与计策同量级 (200 + 能力×9) × 敌兵力系数，不随己方兵力变化；城门 / 本城内 ×0.7。
        public double SpBase(BUnit u, BUnit t, Special sp)
        {
            string stat = sp.stat ?? "war";
            int v = StatOf(u.gen, stat);
            if (stat == "intel" || stat == "pol")
            {
                double d = (200 + v * 9) * Clamp(t.Troops / 2500.0, 0.4, 1.4) / ModK(t, "def");
                var tt = map[t.x, t.y];
                if (tt == Terrain.Castle || tt == Terrain.Gate) d *= 0.7;
                return d;
            }
            double def = DefFactor(t);
            if (sp.pierce != 0) def /= TerrainDef(map[t.x, t.y]);
            return Math.Max(30, AtkPower(u) * DamageK / def * StatScale(v));
        }
        // 火攻的地形修正
        public double FireMod(int x, int y)
        {
            var tt = map[x, y];
            return tt == Terrain.Forest ? 1.5 : tt == Terrain.River ? 0.5 : 1;
        }
        public bool NearRiver(int x, int y)
        {
            if (InBounds(x, y) && map[x, y] == Terrain.River) return true;
            foreach (var d in Dirs) { int nx = x + d.x, ny = y + d.y; if (InBounds(nx, ny) && map[nx, ny] == Terrain.River) return true; }
            return false;
        }
        // 以 c 为中心、曼哈顿半径 r 内的格子
        public List<Vector2Int> AreaTiles(int cx, int cy, int r)
        {
            var outList = new List<Vector2Int>();
            for (int dx = -r; dx <= r; dx++) for (int dy = -r; dy <= r; dy++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (Math.Abs(dx) + Math.Abs(dy) <= r && InBounds(x, y)) outList.Add(new Vector2Int(x, y));
                }
            return outList;
        }
        List<Vector2Int> AreaTiles(BUnit c, int r) { return AreaTiles(c.x, c.y, r); }
        public int MaxTroopsOf(BUnit u) { return u.gen.MaxTroops; }
        // 回复上限：出阵时的兵力（且不超过统率上限）——必杀只能补回战损，不能凭空增兵
        public int HealCapOf(BUnit u) { return Math.Min(MaxTroopsOf(u), u.troops0 >= 0 ? u.troops0 : u.Troops); }
        bool NeedsHeal(BUnit b) { return b.alive && (b.Troops < HealCapOf(b) * 0.95 || ConfusedPhases(b) > 0); }
        // 直线突击：t 须与 u 同行 / 同列、距离 1..range，中间各格可通行且无部队。返回落点（敌军前一格；相邻时为原地）
        public ChargeLand ChargeLanding(BUnit u, BUnit t, int range)
        {
            if (u.x != t.x && u.y != t.y) return null;
            int d = Dist(u, t);
            if (d < 1 || d > range) return null;
            int dx = Math.Sign(t.x - u.x), dy = Math.Sign(t.y - u.y);
            int x = u.x, y = u.y;
            var path = new List<Vector2Int> { new Vector2Int(x, y) };
            for (int i = 1; i < d; i++)
            {
                x += dx; y += dy;
                if (!Passable(x, y) || UnitAt(x, y) != null) return null;
                path.Add(new Vector2Int(x, y));
            }
            return new ChargeLand { x = x, y = y, dx = dx, dy = dy, dash = d - 1, path = path };
        }
        // 疾行：可再行动的友军（已行动、未混乱），相邻敌军者优先，其次兵多者
        List<BUnit> HasteCands(BUnit u, Special sp)
        {
            var list = Alive(u.side).Where(a => a != u && a.acted && a.confused <= 0 && Dist(a, u) <= sp.radius).ToList();
            return list.OrderByDescending(a => (AdjacentEnemies(a).Count > 0 ? 100000 : 0) + a.Troops).Take(Math.Max(1, sp.Count)).ToList();
        }

        // ---- 目标与可用性 ----
        public List<BUnit> SpecialTargets(BUnit u)
        {
            var sp = SpecialOf(u);
            if (sp == null || !u.alive) return new List<BUnit>();
            var foes = Alive(1 - u.side).ToList(); var friends = Alive(u.side).ToList();
            switch (sp.kind)
            {
                case "cleave": return foes.Where(e => Dist(e, u) == 1).ToList();
                case "charge": return foes.Where(e => ChargeLanding(u, e, sp.Range) != null).ToList();
                case "blaze": return foes.Where(e => Dist(e, u) <= sp.range && map[e.x, e.y] != Terrain.River).ToList();
                case "roar": return foes.Any(e => Dist(e, u) <= sp.radius) ? new List<BUnit> { u } : new List<BUnit>();
                case "rally": case "command": case "fortify": return new List<BUnit> { u };
                case "haste": return HasteCands(u, sp).Count > 0 ? new List<BUnit> { u } : new List<BUnit>();
                case "heal": return friends.Where(a => Dist(a, u) <= sp.range && friends.Any(b => Dist(a, b) <= sp.radius && NeedsHeal(b))).ToList();
                default: return foes.Where(e => Dist(e, u) <= sp.range).ToList();
            }
        }
        public bool SpecialUsable(BUnit u) { string why; return SpecialUsable(u, out why); }
        public bool SpecialUsable(BUnit u, out string why)
        {
            why = "";
            var sp = SpecialOf(u);
            if (sp == null) { why = specialsEnabled ? "无必杀技" : "必杀技未开放"; return false; }
            if (result != 0) { why = "战斗已结束"; return false; }
            if (!u.alive) { why = "已溃散"; return false; }
            if (u.specialUsed) { why = "本战已施展"; return false; }
            if (u.confused > 0) { why = "混乱中"; return false; }
            if (SpecialTargets(u).Count == 0)
            {
                switch (sp.kind)
                {
                    case "cleave": case "roar": why = "附近无敌军"; break;
                    case "charge": why = "直线上无可突击的敌军"; break;
                    case "heal": why = "附近无受伤友军"; break;
                    case "haste": why = "附近无已行动的友军"; break;
                    default: why = "射程内无敌军（射程 " + sp.Range + "）"; break;
                }
                return false;
            }
            return true;
        }

        // ---- 效果清单（纯计算：不改状态、不耗随机数） ----
        public SpPlan SpecialPlan(BUnit u, BUnit target)
        {
            var sp = SpecialOf(u);
            if (sp == null) return null;
            var t = target ?? u;
            var foes = Alive(1 - u.side).ToList(); var friends = Alive(u.side).ToList();
            var pl = new SpPlan { sp = sp, kind = sp.kind, unit = u, target = t, center = new Vector2Int(t.x, t.y) };
            int W = u.gen.war, I = u.gen.intel;
            int sv = StatOf(u.gen, sp.stat ?? "war");
            double kS = 0.75 + sv / 400.0;                     // 辅助类招式的能力系数（能力 100 → 1.0）
            Func<BUnit, double> bse = e => SpBase(u, e, sp);
            Action<BUnit, double, bool, bool> hit = (e, amt, primary, onFail) => pl.dmg.Add(new SpHit { unit = e, amt = Math.Max(0, amt), primary = primary, onFail = onFail });
            Func<BUnit, SpFoe> foeOf = e => { var f = pl.foe.Find(q => q.unit == e); if (f == null) { f = new SpFoe { unit = e }; pl.foe.Add(f); } return f; };
            Action<BUnit, int> foeMorale = (e, m) => { var f = foeOf(e); if (m != 0) f.morale += m; };
            Action<BUnit, double, int> foeConfuse = (e, p, turns) => { var f = foeOf(e); if (p > 0) { f.p = 1 - (1 - f.p) * (1 - Clamp01(p)); f.turns = Math.Max(f.turns, turns > 0 ? turns : 1); } };
            Action<BUnit, BMod> foeMod = (e, m) => { var f = foeOf(e); if (m != null) f.mod = m; };
            Func<BUnit, SpAlly> allyOf = a => { var f = pl.ally.Find(q => q.unit == a); if (f == null) { f = new SpAlly { unit = a }; pl.ally.Add(f); } return f; };
            Action<BUnit, int, int, bool, BMod> allyFx = (a, morale, heal, cure, mod) =>
            {
                var f = allyOf(a);
                if (morale != 0) f.morale += morale;
                if (heal != 0) f.heal += heal;
                if (cure) f.cure = true;
                if (mod != null) f.mod = mod;
            };
            // 命中敌军的通用附加：士气、混乱（概率按所用能力之差修正）
            Action<BUnit> extras = e =>
            {
                if (sp.morale != 0) foeMorale(e, (int)sp.morale);
                if (sp.confuse > 0)
                {
                    int diff = (sp.stat == "intel" || sp.stat == "pol") ? I - e.gen.intel : W - e.gen.war;
                    foeConfuse(e, Clamp(sp.confuse * (1 + diff / 120.0), 0, 0.9), sp.Turns);
                }
            };
            Func<BUnit, int> healAmt = a => Math.Max(0, Math.Min(HealCapOf(a) - a.Troops, RInt(sp.heal * MaxTroopsOf(a) * kS)));
            Func<int, int, bool> burnable = (x, y) => Passable(x, y) && map[x, y] != Terrain.River && !friends.Any(a => a.x == x && a.y == y);
            string src = sp.id ?? sp.name;
            switch (sp.kind)
            {
                case "smite":
                case "drain":
                    hit(t, sp.power * bse(t), true, false); extras(t);
                    pl.area = new List<Vector2Int> { new Vector2Int(t.x, t.y) };
                    if (sp.kind == "drain") pl.drain = sp.drain * (0.7 + u.gen.pol / 333.0);
                    break;
                case "cleave":
                    hit(t, sp.power * bse(t), true, false); extras(t);
                    foreach (var e in foes) if (e != t && Dist(e, u) == 1) { hit(e, sp.power * sp.splash * bse(e), false, false); extras(e); }
                    pl.center = new Vector2Int(u.x, u.y); pl.area = AreaTiles(u, 1);
                    break;
                case "charge":
                    {
                        var L = ChargeLanding(u, t, sp.Range);
                        if (L == null) break;
                        if (L.x != u.x || L.y != u.y) pl.move = new SpMove { from = new Vector2Int(u.x, u.y), to = new Vector2Int(L.x, L.y) };
                        double k = 1 + sp.dash * L.dash;
                        if (sp.push != 0)
                        {
                            // 本城、城门上的部队据守不退（否则一记突击就能把守将撞出本城，随后踏入即破城）
                            var tt = map[t.x, t.y]; bool held = tt == Terrain.Castle || tt == Terrain.Gate;
                            int bx = t.x + L.dx, by = t.y + L.dy;
                            if (!held && Passable(bx, by) && UnitAt(bx, by) == null && map[bx, by] != Terrain.Castle) pl.push = new SpPush { unit = t, from = new Vector2Int(t.x, t.y), to = new Vector2Int(bx, by) };
                            else { k += 0.25; pl.blocked = t; }                  // 退无可退：撞击伤害 +25%
                        }
                        // 冲刺与撞击加成合计封顶（ChargeCap），单体伤害不超出平衡目标太多
                        hit(t, sp.power * bse(t) * Math.Min(k, Specials.ChargeCap), true, false); extras(t);
                        pl.area = new List<Vector2Int>(L.path) { new Vector2Int(t.x, t.y) };
                        break;
                    }
                case "rampage":
                    {
                        var others = foes.Where(e => e != t && Dist(e, u) <= sp.range).OrderBy(e => Dist(e, u) * 100000 - e.Troops).ToList();
                        var pool = pl.pool = new List<BUnit> { t };
                        pool.AddRange(others);
                        pl.seq = true;
                        for (int i = 0; i < sp.Strikes; i++) { var e = pool[i % pool.Count]; hit(e, sp.power * bse(e), i == 0, false); }
                        var touched = pool.Take(Math.Min(pool.Count, sp.Strikes)).ToList();
                        foreach (var e in touched) extras(e);
                        pl.area = touched.Select(e => new Vector2Int(e.x, e.y)).ToList();
                        break;
                    }
                case "volley":
                    hit(t, sp.power * bse(t), true, false); extras(t);
                    foreach (var e in foes) if (e != t && sp.radius > 0 && Dist(e, t) <= sp.radius) { hit(e, sp.power * sp.splash * bse(e), false, false); extras(e); }
                    pl.area = AreaTiles(t, sp.Radius);
                    break;
                case "blaze":
                    pl.area = AreaTiles(t, sp.Radius);
                    foreach (var e in foes) if (Dist(e, t) <= sp.radius) { hit(e, sp.power * (e == t ? 1 : sp.splash) * bse(e) * FireMod(e.x, e.y), e == t, false); extras(e); }
                    if (sp.burn > 0) foreach (var p in pl.area) if (burnable(p.x, p.y)) pl.burn.Add(new SpBurn { x = p.x, y = p.y, days = sp.Burn });
                    break;
                case "storm":
                    pl.area = AreaTiles(t, sp.Radius);
                    foreach (var e in foes)
                        if (Dist(e, t) <= sp.radius)
                        {
                            hit(e, sp.power * bse(e) * (sp.burn > 0 ? FireMod(e.x, e.y) : 1), e == t, false); extras(e);
                            if (sp.burn > 0 && burnable(e.x, e.y)) pl.burn.Add(new SpBurn { x = e.x, y = e.y, days = sp.Burn });
                        }
                    break;
                case "flood":
                    pl.area = AreaTiles(t, sp.Radius);
                    foreach (var e in foes) if (Dist(e, t) <= sp.radius) { hit(e, sp.power * bse(e) * (NearRiver(e.x, e.y) ? Specials.FloodRiver : Specials.FloodDry), e == t, false); extras(e); }
                    foreach (var p in pl.area) if (burning[p.x, p.y] > 0) pl.douse.Add(p);
                    break;
                case "roar":
                    pl.center = new Vector2Int(u.x, u.y); pl.area = AreaTiles(u, sp.Radius);
                    foreach (var e in foes)
                        if (Dist(e, u) <= sp.radius)
                        {
                            if (sp.power > 0) hit(e, sp.power * bse(e), false, false);
                            foeMorale(e, RInt(sp.morale * (0.75 + W / 400.0)));
                            if (sp.confuse > 0) foeConfuse(e, Clamp(sp.confuse * (1 + (W - e.gen.war) / 100.0), 0, 0.9), sp.Turns);
                        }
                    break;
                case "rally":
                    pl.center = new Vector2Int(u.x, u.y); pl.area = AreaTiles(u, sp.Radius);
                    foreach (var a in friends)
                        if (Dist(a, u) <= sp.radius)
                        {
                            allyFx(a, RInt(sp.morale * kS), healAmt(a), sp.cure > 0, null);
                            if (sp.atk > 1) allyFx(a, 0, 0, false, new BMod { src = src, atk = 1 + (sp.atk - 1) * kS, days = sp.Turns });
                        }
                    break;
                case "command":
                    pl.center = new Vector2Int(u.x, u.y); pl.area = AreaTiles(u, sp.Radius);
                    foreach (var a in friends)
                        if (Dist(a, u) <= sp.radius)
                            allyFx(a, RInt(sp.morale * kS), 0, false, new BMod { src = src, atk = 1 + (sp.atk - 1) * kS, def = 1 + (sp.def - 1) * kS, days = sp.Turns });
                    break;
                case "fortify":
                    pl.center = new Vector2Int(u.x, u.y); pl.area = AreaTiles(u, sp.Radius);
                    foreach (var a in friends)
                        if (Dist(a, u) <= sp.radius)
                            allyFx(a, RInt(sp.morale * kS), 0, false, new BMod { src = src, def = 1 + (sp.def - 1) * kS, counter = 1 + (sp.counter - 1) * kS, days = sp.Turns });
                    break;
                case "haste":
                    pl.center = new Vector2Int(u.x, u.y); pl.area = AreaTiles(u, sp.Radius);
                    pl.refresh = HasteCands(u, sp);
                    pl.ap = pl.refresh.Count;
                    foreach (var a in friends)
                        if (a != u && Dist(a, u) <= sp.radius)
                            allyFx(a, RInt(sp.morale * kS), 0, false, sp.move > 0 ? new BMod { src = src, move = sp.Move, days = 1 } : null);
                    break;
                case "scheme":
                    pl.area = AreaTiles(t, sp.Radius);
                    foreach (var e in foes)
                        if (Dist(e, t) <= sp.radius)
                        {
                            if (sp.power > 0) hit(e, sp.power * bse(e), e == t, false);
                            foeMorale(e, (int)sp.morale);
                            foeConfuse(e, Clamp(sp.chance + (I - e.gen.intel) / 110.0, 0.08, 0.95), sp.Turns);
                        }
                    break;
                case "heal":
                    pl.area = AreaTiles(t, sp.Radius);
                    foreach (var a in friends) if (Dist(a, t) <= sp.radius) allyFx(a, RInt(sp.morale * kS), healAmt(a), sp.cure > 0, null);
                    break;
                case "assassinate":
                    {
                        // 武将行刺看武力差，谋士鸩杀看智力差
                        double p = sp.stat == "intel"
                            ? Clamp(sp.chance + (I - t.gen.intel) / 200.0 + (W - t.gen.war) / 500.0, 0.05, 0.6)
                            : Clamp(sp.chance + (W - t.gen.war) / 250.0 + (I - t.gen.intel) / 300.0, 0.05, 0.6);
                        if (t.commander) p *= 0.5;
                        pl.kill = new SpKill { unit = t, p = p };
                        if (sp.power > 0) hit(t, sp.power * bse(t), true, true);   // 失手时造成的伤害
                        if (sp.morale != 0) foeMorale(t, (int)sp.morale);
                        pl.area = new List<Vector2Int> { new Vector2Int(t.x, t.y) };
                        break;
                    }
                case "poison":
                    pl.area = AreaTiles(t, sp.Radius);
                    foreach (var e in foes)
                        if (Dist(e, t) <= sp.radius)
                        {
                            if (sp.power > 0) hit(e, sp.power * bse(e) * (e == t ? 1 : 0.8), e == t, false);
                            foeMorale(e, (int)sp.morale);
                            foeMod(e, new BMod { src = src, dot = sp.dot * (0.8 + I / 500.0), days = sp.Turns });
                        }
                    break;
            }
            // 范围伤害封顶：命中多支敌军时，次要目标的伤害按比例缩减，使总伤害 ≤ AreaTotal × 基准（主目标的一次 1 倍伤害）；
            // 主目标的伤害不缩减。连斩以斩数计量（strikes × power 已有上限），不在此列。
            if (sp.kind != "rampage" && pl.dmg.Count > 1)
            {
                double b = t.side != u.side ? bse(t) : pl.dmg.Max(h => bse(h.unit));
                double P0 = 0, S0 = 0;
                foreach (var h in pl.dmg) { double eff = Math.Min(h.amt, h.unit.Troops); if (h.primary) P0 += eff; else S0 += eff; }
                double cap = Math.Max(Specials.AreaTotal * b - P0, 0.4 * b);
                if (S0 > cap) { double k = cap / S0; foreach (var h in pl.dmg) if (!h.primary) h.amt *= k; pl.areaK = k; }
            }
            return pl;
        }

        // 电脑用：一份效果清单的期望价值（折算为兵力）
        public double SpecialValue(BUnit u, SpPlan pl)
        {
            if (pl == null) return 0;
            var foes = Alive(1 - u.side).ToList();
            Func<BUnit, BUnit> nearestFoe = a => { BUnit b = null; int bd = int.MaxValue; foreach (var e in foes) { int d = Dist(e, a); if (d < bd) { bd = d; b = e; } } return b; };
            double v = 0;
            var left = new Dictionary<BUnit, double>();
            double killP = pl.kill != null ? pl.kill.p : 0;
            foreach (var h in pl.dmg)
            {
                var e = h.unit;
                double rem; if (!left.TryGetValue(e, out rem)) rem = e.Troops;
                if (rem <= 0) continue;
                double d = Math.Min(rem, h.amt) * (h.onFail ? 1 - killP : 1);
                v += d;
                left[e] = rem - d;
                if (rem - d < 50) v += RoutBonus(e);
            }
            if (pl.kill != null) { var e = pl.kill.unit; v += pl.kill.p * (e.Troops + RoutBonus(e)); }
            foreach (var f in pl.foe)
            {
                var e = f.unit;
                double rem; if (!left.TryGetValue(e, out rem)) rem = e.Troops;
                if (rem <= 0) continue;
                if (f.morale < 0)
                {
                    v += -f.morale * rem / 300;
                    if (e.morale + f.morale <= 0) v += rem + RoutBonus(e);
                }
                if (f.p > 0) v += f.p * Math.Max(0, f.turns - ConfusedPhases(e)) * 0.8 * Threat(e, u);
                if (f.mod != null && f.mod.dot > 0) v += f.mod.days * Math.Max(20, rem * f.mod.dot.Value) * 0.9;
            }
            foreach (var a in pl.ally)
            {
                var x = a.unit;
                v += a.heal * 0.9;
                bool engaged = foes.Any(e => Dist(e, x) <= 5);
                if (a.morale > 0) v += Math.Min(a.morale, 100 - x.morale) * (double)x.Troops / 300 * (x.morale < 40 ? 2 : 1) * (engaged ? 1 : 0.3);
                if (a.cure && x.confused > 0) v += ConfusedPhases(x) * 0.5 * AtkExpect(x, nearestFoe(x) ?? x);
                var m = a.mod;
                if (m != null)
                {
                    var near = foes.Where(e => Dist(e, x) <= 4).ToList();
                    if (near.Count > 0)
                    {
                        var ne = nearestFoe(x);
                        if (m.atk > 1) v += (m.atk.Value - 1) * AtkExpect(x, ne) * m.days * 0.8;
                        if (m.def > 1) { double inc = 0; foreach (var e in near) inc += AtkExpect(e, x) * 0.5; v += inc * (1 - 1 / m.def.Value) * m.days * 0.8; }
                        if (m.counter > 1) v += (m.counter.Value - 1) * AtkExpect(x, ne) * CounterRatio * Math.Min(2, near.Count) * m.days * 0.5;
                        if (m.move > 0) v += 30;
                    }
                }
            }
            foreach (var r in pl.refresh)
            {
                var adj = AdjacentEnemies(r);
                if (adj.Count > 0) v += adj.Max(e => Math.Min(e.Troops, AtkExpect(r, e)));
                else { var ne = nearestFoe(r); if (ne != null && Dist(ne, r) <= r.Mobility + 1) v += 0.5 * AtkExpect(r, ne); }
            }
            foreach (var b in pl.burn) { var e = foes.FirstOrDefault(q => q.x == b.x && q.y == b.y); if (e != null) v += b.days * Math.Max(30, e.Troops / 12.0) * 0.6; }
            if (pl.drain > 0) foreach (var h in pl.dmg) if (h.primary) v += h.amt * pl.drain * 0.7;
            return v;
        }

        // 敌军 e 每回合对 t 的威胁（普通攻击，或其火计期望的一半——电脑并非每回合都用计）
        public double Threat(BUnit e, BUnit t)
        {
            double v = Math.Min(t.Troops, AtkExpect(e, t));
            if (TacticUsable(e, Defs.Tactics[0])) v = Math.Max(v, 0.5 * Clamp(0.5 + (e.gen.intel - t.gen.intel) / 110.0, 0.12, 0.92) * (260 + e.gen.intel * 11) * Clamp(t.Troops / 2500.0, 0.4, 1.4));
            return v;
        }
        // 电脑：不用必杀技时最好的一手（普通攻击扣除反击 / 火计期望）
        public double AiNormalValue(BUnit u)
        {
            double best = 0;
            foreach (var t in AdjacentEnemies(u))
            {
                double d = Math.Min(t.Troops, AtkExpect(u, t));
                double c = Math.Min(u.Troops, AtkExpect(t, u) * CounterRatio * ModK(t, "counter"));
                best = Math.Max(best, d - 0.5 * c + (t.Troops - d < 50 ? RoutBonus(t) : 0));
            }
            var fire = Defs.Tactics[0];
            if (TacticUsable(u, fire))
            {
                foreach (var t in TacticTargets(u, fire))
                {
                    double p = Clamp(0.5 + (u.gen.intel - t.gen.intel) / 110.0, 0.12, 0.92);
                    best = Math.Max(best, p * Math.Min(t.Troops, (260 + u.gen.intel * 11) * Clamp(t.Troops / 2500.0, 0.4, 1.4)));
                }
            }
            return best;
        }

        // 电脑：划算时施展必杀技 → { target, value } 或 null。越到战斗后期、己方越危急，门槛越低
        public AiSpecialChoice AiSpecial(BUnit u)
        {
            if (!specialsEnabled || !SpecialUsable(u)) return null;
            BUnit best = null; double bv = 0;
            foreach (var t in SpecialTargets(u))
            {
                double v = SpecialValue(u, SpecialPlan(u, t));
                if (v > bv) { bv = v; best = t; }
            }
            if (best == null) return null;
            double normal = AiNormalValue(u);
            double k = day >= 20 ? 1.05 : day >= 10 ? 1.3 : 1.5;
            if (u.Troops < MaxTroopsOf(u) * 0.3 || u.morale < 30) k = Math.Min(k, 1.0);
            return bv >= Math.Max(normal * k, 260) ? new AiSpecialChoice { target = best, value = bv } : null;
        }

        // ---- 结算 ----
        public SpecialResult UseSpecial(BUnit u, BUnit target)
        {
            var res = new SpecialResult();
            var sp = SpecialOf(u);
            if (sp == null) { res.why = "无必杀技"; return res; }
            res.name = sp.name; res.kind = sp.kind; res.sp = sp;
            string why;
            if (!SpecialUsable(u, out why)) { res.why = why; return res; }
            var cands = SpecialTargets(u);
            if (target == null || cands.IndexOf(target) < 0) target = cands[0];
            var pl = SpecialPlan(u, target);
            u.specialUsed = true;
            res.success = true; res.target = target; res.center = pl.center; res.area = pl.area;
            Action<BUnit> touch = x => { if (!res.affected.Contains(x)) res.affected.Add(x); };
            Action<BUnit> routed = x => { if (!res.routed.Contains(x)) res.routed.Add(x); };
            // 1. 突击位移
            if (pl.move != null) { u.x = pl.move.to.x; u.y = pl.move.to.y; res.moved = pl.move; }
            // 2. 暗杀
            bool killed = false;
            if (pl.kill != null)
            {
                var k = pl.kill.unit;
                if (k.alive && UnityEngine.Random.value < pl.kill.p)
                {
                    int d = k.Troops;
                    k.Troops = 0; k.alive = false; setup.routed.Add(k.gen.id);
                    res.killed = k; res.dmg += d; res.hits.Add(new SpecialHit { unit = k, dmg = d, primary = true, killed = true });
                    routed(k); touch(k); killed = true;
                }
                else res.success = false;                     // 失手（仍可能造成少量伤害）
            }
            // 3. 伤害（连斩：目标已溃散则转向下一个敌军）
            int primaryDmg = 0;
            foreach (var h in pl.dmg)
            {
                if (h.onFail && killed) continue;
                var e = h.unit; double amt = h.amt;
                if (!e.alive)
                {
                    if (!pl.seq) continue;
                    e = pl.pool.FirstOrDefault(q => q.alive);
                    if (e == null) continue;
                    amt = sp.power * SpBase(u, e, sp);
                }
                int dd = Math.Min(e.Troops, Math.Max(10, RInt(amt * UnityEngine.Random.Range(0.92f, 1.08f))));
                e.Troops -= dd;
                e.morale = Math.Max(0, e.morale - RInt(dd * 40.0 / Math.Max(200, e.Troops + dd)));
                res.dmg += dd;
                if (h.primary) primaryDmg += dd;
                res.hits.Add(new SpecialHit { unit = e, dmg = dd, primary = h.primary });
                touch(e);
                if (CheckRout(e)) routed(e);
            }
            // 4. 击退
            if (pl.push != null && pl.push.unit.alive)
            {
                var pu = pl.push.unit;
                pu.x = pl.push.to.x; pu.y = pl.push.to.y;
                res.pushed.Add(new SpPush { unit = pu, from = pl.push.from, to = pl.push.to });
            }
            if (pl.blocked != null) res.blocked = pl.blocked;
            // 5. 敌军：士气、混乱、中毒
            foreach (var f in pl.foe)
            {
                var e = f.unit;
                if (!e.alive) continue;
                touch(e);
                if (f.morale != 0) e.morale = Mathf.Clamp(e.morale + f.morale, 0, 100);
                if (f.p > 0)
                {
                    if (UnityEngine.Random.value < f.p) { e.confused = Math.Max(e.confused, ConfuseCount(e, f.turns)); e.acted = true; res.confused.Add(e); }
                    else res.resisted.Add(e);
                }
                if (f.mod != null) { AddMod(e, f.mod.Clone()); res.cursed.Add(e); }
                if (CheckRout(e)) routed(e);
            }
            // 6. 友军：回复、士气、解除混乱、加成
            foreach (var a in pl.ally)
            {
                var x = a.unit;
                if (!x.alive) continue;
                touch(x);
                if (a.heal > 0) { x.Troops += a.heal; res.healed.Add(new SpecialHeal { unit = x, amount = a.heal }); res.heal += a.heal; }
                if (a.morale != 0) x.morale = Mathf.Clamp(x.morale + a.morale, 0, 100);
                if (a.cure && x.confused > 0) x.confused = 0;
                if (a.mod != null) { AddMod(x, a.mod.Clone()); res.buffed.Add(x); }
            }
            // 7. 疾行：再行动
            foreach (var r in pl.refresh) if (r.alive && r.acted) { r.acted = false; res.refreshed.Add(r); touch(r); }
            ap += res.refreshed.Count;
            // 8. 地形：起火 / 灭火
            foreach (var b in pl.burn) { burning[b.x, b.y] = Math.Max(burning[b.x, b.y], b.days); res.burned.Add(new Vector2Int(b.x, b.y)); }
            foreach (var d in pl.douse) { burning[d.x, d.y] = 0; res.doused.Add(d); }
            // 9. 收编
            if (pl.drain > 0 && primaryDmg > 0)
            {
                int g = Math.Max(0, Math.Min(Math.Max(0, MaxTroopsOf(u) - u.Troops), RInt(primaryDmg * pl.drain)));
                u.Troops += g; res.gained = g;
            }
            if (res.dmg > 0) u.morale = Math.Min(100, u.morale + 6);
            CheckEnd();
            return res;
        }

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
            int best = 0; double score = -1;
            for (int i = 0; i < Defs.Formations.Length; i++)
            {
                if (!FormationAllowed(u, i)) continue;
                var f = Defs.Formations[i];
                double s = u.side == 0 ? FormAtk[i] * 1.2 + FormDef[i] * 0.8 + f.Move * 0.05 : FormAtk[i] * 0.7 + FormDef[i] * 1.3;
                if (s > score) { score = s; best = i; }
            }
            return best;
        }

        // 电脑为一个单位决定行动；返回描述行动的对象供画面播放。
        // kind："none" | "attack" | "tactic" | "duel" | "special"（第二版；target 为目标，value 为期望价值）
        public class AiPlan { public BUnit unit; public Vector2Int? move; public string kind; public BUnit target; public TacticDef tactic; public double value; }

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
                Vector2Int best = new Vector2Int(u.x, u.y); double bs = Score(u, best, dist, goal);
                foreach (var t in targets) { double s = Score(u, t, dist, goal); if (s < bs) { bs = s; best = t; } }
                if (best.x != u.x || best.y != u.y) plan.move = best;
            }
            return plan;
        }
        double Score(BUnit u, Vector2Int p, int[,] dist, Vector2Int goal)
        {
            double s = dist[p.x, p.y];
            if (s >= 999) s = Mathf.Abs(p.x - goal.x) + Mathf.Abs(p.y - goal.y) + 20;
            s -= TerrainDef(map[p.x, p.y]) * 0.6;
            if (p.x == goal.x && p.y == goal.y) { var o = UnitAt(goal.x, goal.y); if (o != null && o != u) s += 50; }
            return s;
        }

        // 移动后决定攻击方式
        public AiPlan PlanAction(BUnit u)
        {
            var plan = new AiPlan { unit = u, kind = "none" };
            // 必杀技：划算时优先（不耗随机数；specialsEnabled = false 时完全跳过，行为与第一版一致）
            if (specialsEnabled)
            {
                var sp = AiSpecial(u);
                if (sp != null) { plan.kind = "special"; plan.target = sp.target; plan.value = sp.value; return plan; }
            }
            var adj = AdjacentEnemies(u);
            // 策略
            foreach (var t in Defs.Tactics)
            {
                if (t.Kind == TacticKind.Inspire || !TacticUsable(u, t)) continue;
                var tg = TacticTargets(u, t).OrderByDescending(o => (t.Kind == TacticKind.Fire && map[o.x, o.y] == Terrain.Forest ? 3000 : 0) + o.Troops).FirstOrDefault();
                if (tg != null && u.gen.intel > tg.gen.intel + 5 && UnityEngine.Random.value < (u.gen.intel > 80 ? 0.6 : 0.35) && (t.Kind != TacticKind.Confuse || adj.Count == 0))
                { plan.kind = "tactic"; plan.tactic = t; plan.target = tg; return plan; }
            }
            if (adj.Count == 0)
            {
                if (u.morale < 45 && u.gen.intel >= 40) { plan.kind = "tactic"; plan.tactic = Defs.Tactics[3]; plan.target = u; }
                return plan;
            }
            var weakest = adj.OrderBy(o => o.Troops).First();
            var duelT = adj.Where(o => u.gen.war >= o.gen.war + 12).OrderByDescending(o => o.commander ? 1 : 0).FirstOrDefault();
            if (duelT != null && UnityEngine.Random.value < 0.3) { plan.kind = "duel"; plan.target = duelT; return plan; }
            plan.kind = "attack"; plan.target = weakest;
            return plan;
        }
    }
}
