'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 游戏状态与战略指令
   移植自 Model/GameState.cs 与 Model/Commands.cs。
   城 / 武将 / 势力为普通对象，字段名与 C# 相同。当前游戏为 SG.G。
   第二版（调动武将）新增：
     G.routesFrom(city, f?, maxHops?) → [{ city, hops, path }]   经由己方城池链可达的己方城（BFS；maxHops 限制路程）
     G.routeBetween(from, to, f?) → 城编号数组 | null
     Commands.moveTargets(c) / transportTargets(c)       移动 / 输送的目的地（= routesFrom）
     Commands.moveBlocked(c) / transportBlocked(c)       不能使用的原因（中文）或 null
     Commands.move / transport 拒绝不可达的目的地（返回说明文字，不改变状态）
     Commands.aiMoveTargets(c, f)                        电脑调动武将可选的目的地（路程 ≤ 2；供 strategy-ai.js 接线用）
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;
  const Balance = SG.Balance;
  const ScenarioData = SG.ScenarioData;
  const Seq = SG.Seq;

  const SAVE_KEY = 'sanguozhi2_save';

  // ------------------------------------------------------- 数据对象工厂 --
  function newCity(o) {
    return Object.assign({
      id: 0, key: '', name: '', lon: 0, lat: 0,
      land: 0, industry: 0, town: 0, population: 0, gold: 0, food: 0,
      owner: -1,     // 势力编号，-1 为空城
      governor: -1,  // 太守（武将编号）
      links: [],     // 不存档，Init() 时重建
    }, o);
  }
  function newGeneral(o) {
    return Object.assign({
      id: 0, name: '', war: 0, intel: 0, pol: 0,
      faction: -1,   // -1 为在野
      city: 0, loyalty: 0, troops: 0, training: 50,
      hidden: false, // 隐士，须搜索发现
      dead: false,
      formation: 0,  // 当前阵型
      moved: false,  // 本月已行动（移动/出征）
    }, o);
  }
  function newFaction(o) {
    return Object.assign({ id: 0, key: '', name: '', color: '#ffffff', ruler: -1, virtue: 0, fame: 0, alive: true }, o);
  }

  // C# 属性的替代
  SG.mapPos = function (c) { return { x: (c.lon - 100) * 5, y: (c.lat - 23) * 5.6 }; };
  SG.cityDefense = function (c) { return 20 + M.idiv(c.town, 12); };
  SG.maxTroops = function (g) { return Balance.GeneralTroopBase + g.war * Balance.GeneralTroopPerWar; };
  SG.isFree = function (g) { return g.faction < 0 && !g.dead; };
  SG.factionColor = function (f) { return f.color; };

  function storage() {
    try {
      if (typeof localStorage === 'undefined' || !localStorage) return null;
      return localStorage;
    } catch (e) { return null; }
  }

  // C# 中 GameState 既有字段 log（List<string>）又有方法 Log(string)。
  // 这里 G.log 是一个可调用的“数组”：G.log('文字') 记录一条日志（同 addLog），
  // 同时 G.log.length / G.log[i] / G.log.slice() / for…of 等数组用法照常可用。
  function makeLog(owner, entries) {
    const target = () => {};
    return new Proxy(target, {
      apply(t, thisArg, args) { owner.addLog(args[0]); },
      get(t, prop) {
        if (prop === 'toJSON') return () => entries.slice();
        const v = entries[prop];
        return typeof v === 'function' ? v.bind(entries) : v;
      },
      set(t, prop, value) { entries[prop] = value; return true; },
      has(t, prop) { return prop in entries; },
      deleteProperty(t, prop) { return delete entries[prop]; },
    });
  }

  // ================================================================ GameState --
  class GameState {
    constructor() {
      this.year = 0; this.month = 0;
      this.player = -1;
      this.tokens = 0;
      this.seed = 0;
      this.cities = [];
      this.generals = [];
      this.factions = [];
      this.alliance = []; // n×n，值为同盟到期的月序号
      this.log = makeLog(this, []);
    }

    get monthIndex() { return this.year * 12 + this.month; }

    // ------------------------------------------------------------ 创建 --
    static newGame(playerFactionKey) {
      const g = new GameState();
      g.year = ScenarioData.StartYear; g.month = ScenarioData.StartMonth; g.seed = SG.Random.rangeInt(1, 999999);
      for (const line of ScenarioData.Cities) {
        const p = line.split('|');
        g.cities.push(newCity({
          id: g.cities.length, key: p[0], name: p[1], lon: parseFloat(p[2]), lat: parseFloat(p[3]),
          land: parseInt(p[4], 10), industry: parseInt(p[5], 10), town: parseInt(p[6], 10), population: parseInt(p[7], 10),
          gold: parseInt(p[8], 10), food: parseInt(p[9], 10),
        }));
      }
      for (const line of ScenarioData.Factions) {
        const p = line.split('|');
        g.factions.push(newFaction({ id: g.factions.length, key: p[0], name: p[1], color: p[3], virtue: parseInt(p[4], 10), fame: parseInt(p[5], 10), ruler: -1 }));
      }
      for (const line of ScenarioData.Generals) {
        const p = line.split('|');
        const gen = newGeneral({
          id: g.generals.length, name: p[0], war: parseInt(p[1], 10), intel: parseInt(p[2], 10), pol: parseInt(p[3], 10),
          faction: p[4] === '-' ? -1 : g.factionByKey(p[4]).id, city: g.cityByKey(p[5]).id,
          loyalty: parseInt(p[6], 10), troops: parseInt(p[7], 10), hidden: p[8] === '1',
        });
        gen.training = gen.faction >= 0 ? 55 + SG.Random.rangeInt(0, 20) : 40;
        g.generals.push(gen);
      }
      // 君主
      for (let i = 0; i < g.factions.length; i++) {
        const rulerName = ScenarioData.Factions[i].split('|')[2];
        const r = g.generals.find(x => x.name === rulerName);
        if (!r) throw new Error('ruler not found: ' + rulerName);
        g.factions[i].ruler = r.id;
      }
      // 城主归属
      for (const gen of g.generals) if (gen.faction >= 0) g.cities[gen.city].owner = gen.faction;
      g.init();
      for (const c of g.cities) g.autoGovernor(c);
      g.player = g.factionByKey(playerFactionKey).id;
      const n = g.factions.length;
      for (let i = 0; i < n * n; i++) g.alliance.push(0);
      g.tokens = g.tokensFor(g.player);
      return g;
    }

    // 读档或新建后重建非序列化数据
    init() {
      for (const c of this.cities) c.links = [];
      for (const l of ScenarioData.Links) {
        const p = l.split('-');
        const a = this.cityByKey(p[0]); const b = this.cityByKey(p[1]);
        a.links.push(b.id); b.links.push(a.id);
      }
    }

    // ------------------------------------------------------------ 查询 --
    cityByKey(k) { return this.cities.find(c => c.key === k); }
    factionByKey(k) { return this.factions.find(f => f.key === k); }
    get playerFaction() { return this.factions[this.player]; }
    citiesOf(f) { return this.cities.filter(c => c.owner === f); }
    generalsOf(f) { return this.generals.filter(x => x.faction === f && !x.dead); }
    generalsIn(city, f) { return this.generals.filter(x => x.city === city && x.faction === f && !x.dead); }
    officersIn(c) { return c.owner < 0 ? [] : this.generalsIn(c.id, c.owner); }
    freeFoundIn(city) { return this.generals.filter(x => x.city === city && SG.isFree(x) && !x.hidden); }
    troopsIn(c) { return Seq.sum(this.officersIn(c), x => x.troops); }
    isRuler(g) { return g.faction >= 0 && this.factions[g.faction].ruler === g.id; }
    ruler(f) { return this.generals[this.factions[f].ruler]; }
    cityCount(f) { let n = 0; for (const c of this.cities) if (c.owner === f) n++; return n; }

    // 经由 f 方城池相连可达的所有 f 方城（广度优先；不含出发城，不穿过他国或空城）。
    // → [{ city, hops, path }]：hops 为路程（相邻 = 1，途经城数 = hops − 1），
    //   path 为城编号数组（含起点与终点）。按路程、再按发现顺序（links 顺序）排列，结果确定。
    // from 不属于 f 时返回空数组。f 缺省为 from.owner。maxHops（可选，> 0）只取路程不超过它的城。
    routesFrom(from, f, maxHops) {
      if (f === undefined || f === null) f = from ? from.owner : -1;
      const out = [];
      if (!from || f < 0 || from.owner !== f) return out;
      const lim = maxHops > 0 ? maxHops : Infinity;
      const prev = new Map();
      prev.set(from.id, -1);
      let frontier = [from.id];
      for (let hops = 1; frontier.length > 0 && hops <= lim; hops++) {
        const next = [];
        for (const id of frontier) {
          for (const j of this.cities[id].links) {
            if (prev.has(j) || this.cities[j].owner !== f) continue;
            prev.set(j, id);
            next.push(j);
            const path = [];
            for (let k = j; k >= 0; k = prev.get(k)) path.push(k);
            path.reverse();
            out.push({ city: this.cities[j], hops, path });
          }
        }
        frontier = next;
      }
      return out;
    }
    // from → to 的最短己方路线（城编号数组，含两端）；不可达或 to 即 from 时为 null
    routeBetween(from, to, f) {
      if (!from || !to || from.id === to.id) return null;
      const r = this.routesFrom(from, f).find(x => x.city.id === to.id);
      return r ? r.path : null;
    }

    tokensFor(f) {
      const n = this.cityCount(f);
      if (n <= 0) return 0;
      return Math.min(Balance.TokensMax, Balance.TokensBase + M.idiv(n - 1, Balance.CitiesPerExtraToken));
    }

    allied(a, b) {
      if (a < 0 || b < 0 || a === b) return false;
      return this.alliance[a * this.factions.length + b] > this.monthIndex;
    }
    setAlliance(a, b, untilMonth) {
      this.alliance[a * this.factions.length + b] = untilMonth;
      this.alliance[b * this.factions.length + a] = untilMonth;
    }

    autoGovernor(c) {
      if (c.owner < 0) { c.governor = -1; return; }
      const offs = this.officersIn(c);
      if (offs.length === 0) { c.governor = -1; return; }
      const ruler = Seq.first(offs, o => this.isRuler(o));
      if (c.governor >= 0 && offs.some(o => o.id === c.governor) && ruler == null) return;
      c.governor = (ruler || Seq.orderByDesc(offs, o => o.pol + o.intel)[0]).id;
    }

    // 势力灭亡检查（C# 中为 GameStateExt.CheckFactionDeath）
    checkFactionDeath(f) {
      if (f < 0 || !this.factions[f].alive) return;
      if (this.cityCount(f) > 0) return;
      this.factions[f].alive = false;
      for (const gen of this.generalsOf(f)) { gen.faction = -1; gen.troops = 0; gen.loyalty = 0; }
      this.addLog(this.factions[f].name + '势力灭亡了。');
    }

    // C# 的 Log(string)。G.log(text) 与 G.addLog(text) 等价（见 makeLog）。
    addLog(s) {
      this.log.push(this.year + '年' + this.month + '月 ' + s);
      if (this.log.length > 80) this.log.shift();
    }

    // ------------------------------------------------------------ 存档 --
    toJSON() {
      return {
        year: this.year, month: this.month, player: this.player, tokens: this.tokens, seed: this.seed,
        cities: this.cities.map(c => {
          const o = {};
          for (const k of Object.keys(c)) if (k !== 'links') o[k] = c[k];
          return o;
        }),
        generals: this.generals.map(g => Object.assign({}, g)),
        factions: this.factions.map(f => Object.assign({}, f)),
        alliance: this.alliance.slice(),
        log: this.log.slice(),
      };
    }
    save() {
      const st = storage();
      if (!st) return false;
      try { st.setItem(SAVE_KEY, JSON.stringify(this)); return true; } catch (e) { console.log('save failed', e); return false; }
    }
    static hasSave() {
      const st = storage();
      if (!st) return false;
      try { return !!st.getItem(SAVE_KEY); } catch (e) { return false; }
    }
    static deleteSave() {
      const st = storage();
      if (!st) return;
      try { st.removeItem(SAVE_KEY); } catch (e) { /* 忽略 */ }
    }
    static fromJSON(text) {
      const d = typeof text === 'string' ? JSON.parse(text) : text;
      if (!d || !Array.isArray(d.cities) || !Array.isArray(d.generals) || !Array.isArray(d.factions)) return null;
      const g = new GameState();
      g.year = d.year | 0; g.month = d.month | 0; g.player = d.player == null ? -1 : d.player | 0;
      g.tokens = d.tokens | 0; g.seed = d.seed | 0;
      g.cities = d.cities.map(c => newCity(Object.assign({}, c, { links: [] })));
      g.generals = d.generals.map(x => newGeneral(x));
      g.factions = d.factions.map(x => newFaction(x));
      g.alliance = Array.isArray(d.alliance) ? d.alliance.slice() : [];
      const n = g.factions.length;
      while (g.alliance.length < n * n) g.alliance.push(0);
      g.log = makeLog(g, Array.isArray(d.log) ? d.log.map(String) : []);
      g.init();
      return g;
    }
    static load() {
      const st = storage();
      if (!st) return null;
      try {
        const text = st.getItem(SAVE_KEY);
        if (!text) return null;
        return GameState.fromJSON(text);
      } catch (e) { console.log('load failed', e); return null; }
    }
  }
  GameState.SAVE_KEY = SAVE_KEY;

  // ================================================================ Commands --
  const DevKind = Object.freeze({ Land: 0, Industry: 1, Town: 2 });

  function G() { return SG.G; }
  const AI_MOVE_HOPS = 2;   // 电脑调动武将的最远路程（相邻 = 1）
  function R(a, b) { return SG.Random.rangeInt(a, b + 1); }

  // 战略指令（玩家与电脑共用）
  const Commands = {
    searchFound: null,

    devName(k) { return k === DevKind.Land ? '土地' : k === DevKind.Industry ? '产业' : '町'; },

    // ---------------------------------------------------------- 开发 --
    develop(c, g, kind) {
      if (c.gold < Balance.DevelopCost) return '金不足，无法开发。';
      c.gold -= Balance.DevelopCost;
      let gain = Balance.DevelopBase + M.idiv(g.pol, Balance.DevelopPolDiv) + R(0, 4);
      let before;
      switch (kind) {
        case DevKind.Land: before = c.land; c.land = Math.min(Balance.StatMax, c.land + gain); gain = c.land - before; break;
        case DevKind.Industry: before = c.industry; c.industry = Math.min(Balance.StatMax, c.industry + gain); gain = c.industry - before; break;
        default: before = c.town; c.town = Math.min(Balance.StatMax, c.town + gain); gain = c.town - before; break;
      }
      const n = Commands.devName(kind);
      return `${g.name}主持${n}开发，${n} +${gain}。`;
    },

    // ---------------------------------------------------------- 征兵 --
    recruitMax(c, g) {
      const room = SG.maxTroops(g) - g.troops;
      const byGold = Math.min(c.gold, Balance.RecruitGoldMax) * Balance.TroopsPerGold;
      const byPop = M.idiv(c.population, 10);
      return Math.max(0, Math.min(room, Math.min(byGold, byPop)));
    },
    recruit(c, g, troops) {
      troops = Math.min(troops, Commands.recruitMax(c, g));
      if (troops <= 0) return '无法征兵（金、人口或带兵上限不足）。';
      const cost = Math.ceil(troops / Balance.TroopsPerGold);
      c.gold -= cost; c.population -= troops;
      const total = g.troops + troops;
      g.training = M.idiv(g.training * g.troops + 30 * troops, Math.max(1, total));
      g.troops = total;
      return `${g.name}征得新兵 ${troops} 人（花费 ${cost} 金）。`;
    },

    // ---------------------------------------------------------- 训练 --
    train(g) {
      if (g.troops <= 0) return g.name + '麾下无兵，无法训练。';
      const before = g.training;
      g.training = Math.min(100, g.training + Balance.TrainBase + M.idiv(g.war, 10) + R(0, 4));
      return `${g.name}操练兵马，训练度 ${before} → ${g.training}。`;
    },

    // ---------------------------------------------------------- 搜索 --
    search(c, g) {
      Commands.searchFound = null;
      const f = G().factions[c.owner];
      const hidden = G().generals.filter(x => x.city === c.id && x.hidden && SG.isFree(x));
      const p = 0.22 + g.intel / 260 + f.virtue / 420;
      if (hidden.length > 0 && SG.Random.value() < p) {
        const h = hidden[SG.Random.rangeInt(0, hidden.length)];
        h.hidden = false;
        Commands.searchFound = h;
        return `${g.name}四处寻访，发现了在野的人才——${h.name}！`;
      }
      const r = SG.Random.value();
      if (r < 0.25) { const v = R(60, 220); c.gold += v; return `${g.name}在城中搜索，发现了 ${v} 金。`; }
      if (r < 0.45) { const v = R(800, 2500); c.food += v; return `${g.name}在城中搜索，发现了 ${v} 粮。`; }
      return g.name + '四处寻访，一无所获。';
    },

    // ---------------------------------------------------------- 登用 --
    hireChance(target, recruiter, faction) {
      const f = G().factions[faction];
      let p = 0.32 + f.virtue / 220 + recruiter.intel / 450 - (target.war + target.intel + target.pol) / 900;
      if (target.faction >= 0) p -= target.loyalty / 160; // 俘虏
      return M.clamp(p, 0.05, 0.95);
    },
    hire(target, recruiter, faction, city) {
      if (SG.Random.value() > Commands.hireChance(target, recruiter, faction)) return false;
      const f = G().factions[faction];
      target.faction = faction; target.city = city; target.hidden = false;
      target.loyalty = M.clamp(60 + M.idiv(f.virtue, 4) + R(-5, 10), 40, 100);
      target.training = 40;
      target.moved = true;
      return true;
    },

    // ---------------------------------------------------------- 赏赐 --
    reward(c, g) {
      if (c.gold < Balance.RewardGold) return '金不足。';
      c.gold -= Balance.RewardGold;
      const before = g.loyalty;
      g.loyalty = Math.min(100, g.loyalty + Balance.RewardLoyalty + R(0, 4));
      return `赏赐${g.name} ${Balance.RewardGold} 金，忠诚 ${before} → ${g.loyalty}。`;
    },

    // ---------------------------------------------------------- 移动 / 输送 --
    // 第二版：目的地不必相邻——经由己方城池链可达的己方城都可前往（GameState.routesFrom）。
    // 每次仍只消耗 1 枚令牌（由界面扣除）。
    moveTargets(c) { return G().routesFrom(c, c.owner); },
    // 电脑把后方武将调往前线时可选的目的地（城池数组，近者在前）：路程不超过 AI_MOVE_HOPS。
    // 玩家可调往任意相连的己方城；电脑只看两步之内，实测（800 局 × 20 年）不改变势力存亡与对玩家的压力。
    aiMoveTargets(c, f) { return G().routesFrom(c, f === undefined ? c.owner : f, AI_MOVE_HOPS).map(r => r.city); },
    transportTargets(c) { return G().routesFrom(c, c.owner); },

    // 不能从 c 调动武将的原因（规则层面；令牌、忙碌等界面状态不在此列）。null = 可以
    moveBlocked(c) {
      const g = G();
      if (c.owner < 0) return '这座城不属于任何势力。';
      if (g.cityCount(c.owner) <= 1) return '只有一座城池——取得第二座城（出征攻取或「拉拢」敌将献城）后即可调动武将。';
      if (g.routesFrom(c, c.owner).length === 0) return `${c.name}与其他己方城池之间隔着他国或空城，无法调动（须经由己方城池相连）。`;
      const offs = g.officersIn(c);
      if (offs.length === 0) return `${c.name}没有武将。`;
      if (offs.every(x => x.moved)) return '本城武将本月都已行动，没有可调动的武将。';
      return null;
    },
    // 不能从 c 输送金粮的原因。null = 可以
    transportBlocked(c) {
      const g = G();
      if (c.owner < 0) return '这座城不属于任何势力。';
      if (g.cityCount(c.owner) <= 1) return '只有一座城池——取得第二座城（出征攻取或「拉拢」敌将献城）后即可输送金粮。';
      if (g.routesFrom(c, c.owner).length === 0) return `${c.name}与其他己方城池之间隔着他国或空城，无法输送（须经由己方城池相连）。`;
      if (c.gold <= 0 && c.food <= 0) return `${c.name}没有可输送的金粮。`;
      return null;
    },

    move(g, to) {
      const from = G().cities[g.city];
      // 只能经由己方城池移往己方城（不可达时不做任何改变）
      if (!G().routeBetween(from, to, g.faction)) return `${to.name}与${from.name}之间没有己方城池相连，${g.name}无法移驻。`;
      g.city = to.id; g.moved = true;
      G().autoGovernor(from); G().autoGovernor(to);
      return `${g.name}率兵 ${g.troops} 人移驻${to.name}。`;
    },
    transport(from, to, gold, food) {
      if (!G().routeBetween(from, to, from.owner)) return `${to.name}与${from.name}之间没有己方城池相连，无法输送。`;
      gold = M.clamp(gold, 0, from.gold); food = M.clamp(food, 0, from.food);
      from.gold -= gold; from.food -= food; to.gold += gold; to.food += food;
      return `自${from.name}向${to.name}输送金 ${gold}、粮 ${food}。`;
    },

    // ---------------------------------------------------------- 交易 --
    // 每 100 粮的价格（金）。秋收后粮价低。
    foodPrice() {
      const m = G().month;
      let baseP = 12;
      if (m >= 7 && m <= 9) baseP = 8; else if (m >= 4 && m <= 6) baseP = 16;
      return baseP;
    },
    buyFood(c, gold) {
      gold = M.clamp(gold, 0, c.gold);
      const food = M.idiv(gold * 100, Commands.foodPrice());
      c.gold -= gold; c.food += food;
      return `以 ${gold} 金购入粮 ${food}。`;
    },
    sellFood(c, food) {
      food = M.clamp(food, 0, c.food);
      const gold = M.idiv(M.idiv(food * Commands.foodPrice(), 100) * 9, 10);
      c.food -= food; c.gold += gold;
      return `卖出粮 ${food}，得金 ${gold}。`;
    },

    // ---------------------------------------------------------- 外交策略 --
    allyChance(f, target, gift) {
      const a = G().factions[f]; const b = G().factions[target];
      let p = 0.25 + a.virtue / 300 + gift / 1200 + (a.fame - b.fame) / 300;
      if (G().cityCount(f) > G().cityCount(target) * 2) p += 0.15; // 弱者愿与强者结盟
      return M.clamp(p, 0.05, 0.9);
    },
    ally(from, f, target, gift) {
      gift = Math.min(gift, from.gold);
      from.gold -= gift;
      if (SG.Random.value() < Commands.allyChance(f, target, gift)) {
        G().setAlliance(f, target, G().monthIndex + Balance.AllianceMonths);
        return `${G().factions[target].name}同意结盟！同盟期限 ${Balance.AllianceMonths} 个月。`;
      }
      return `${G().factions[target].name}拒绝了结盟的提议。`;
    },

    discord(agent, target) {
      if (G().isRuler(target)) return '离间君主是不可能的。';
      const p = M.clamp(0.4 + (agent.intel - target.intel) / 100, 0.1, 0.9);
      if (SG.Random.value() > p) return `${target.name}识破了离间之计。`;
      let d = M.idiv(R(6, 16) * agent.intel, Math.max(30, target.intel));
      d = M.clamp(d, 3, 25);
      target.loyalty = Math.max(0, target.loyalty - d);
      return `离间成功！${target.name}的忠诚下降了 ${d}（现为 ${target.loyalty}）。`;
    },

    persuadeChance(agent, target, f) {
      if (G().isRuler(target)) return 0;
      const p = (100 - target.loyalty) / 110 * (0.45 + agent.intel / 180) * (0.55 + G().factions[f].virtue / 220);
      return M.clamp(p, 0, 0.9);
    },
    persuade(agent, target, f) {
      if (SG.Random.value() > Commands.persuadeChance(agent, target, f)) return `${target.name}拒绝了劝诱。`;
      const g = G();
      const oldCity = g.cities[target.city];
      const oldF = target.faction;
      const alone = g.officersIn(oldCity).length === 1;
      target.faction = f;
      target.loyalty = 65 + R(0, 15);
      if (alone) {
        oldCity.owner = f;
        g.autoGovernor(oldCity);
        g.checkFactionDeath(oldF);
        return `${target.name}献城归降！${oldCity.name}归入我方。`;
      }
      target.city = agent.city; target.troops = M.idiv(target.troops, 2); target.moved = true;
      g.autoGovernor(oldCity); g.autoGovernor(g.cities[agent.city]);
      return `${target.name}率部来投！`;
    },
  };

  SG.GameState = GameState;
  SG.DevKind = DevKind;
  SG.Commands = Commands;
  if (!('G' in SG)) SG.G = null;
})();
