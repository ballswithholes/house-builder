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
   第二版 §4G 世界剧本：
     SG.ScenarioData 在运行时可换（SG.Scenarios.classic / world）；本文件每次读取都取当前剧本。
     GameState.newGame(playerKey, scenarioId?)  scenarioId 'classic' | 'world'（缺省 = 当前 SG.ScenarioData）
     G.scenario = 'classic' | 'world'（存档带剧本键与版本号；读档时先切换 SG.ScenarioData）
     剧本的附加列（城 culture；势力 culture / region；武将 culture / born）与 SG.WorldData 的查询
       在 init() 里写到对象上，不进存档（不可枚举，由剧本数据重建）：
       city.culture；faction.culture、faction.region；gen.culture、gen.born、gen.sex（'m' / 'f'）、gen.female
     G.seaLinks = Set('小编号-大编号')（剧本 SeaLinks）；SG.linkIsSea(aId, bId)
     SG.mapPos(c) = SG.project(c.lon, c.lat)（中国本部与旧版公式相同）
     SAVE_KEY = 'sanguozhi2_save_v2'；GameState.loadError：最近一次读档失败的原因（中文）；
       GameState.hasLegacySave()：只有旧版存档（键 'sanguozhi2_save'）
     GameState.saveInfo() → { scenario, year, month, faction } | null（标题画面显示用）
   第三版 §4 成长（经验、等级、可习得必杀与计策）：
     武将新增存档字段：exp（经验）、level（1–10，由 exp 推出）、specials（已习得的「可习得必杀」编号，
       不含专属必杀）、tactics（已会的计策编号：按智力自带的原四计 + 习得的新计策）、
       record { battles, wins, routs, duels }（战绩）。
     SAVE_KEY = 'sanguozhi2_save_v3'、SAVE_VERSION = 3；读到第二版存档（键 'sanguozhi2_save_v2'，version 2）时补齐：
       等级 1、经验 0、只有专属必杀、原四计按智力（GameState.migratedFrom = 2，非枚举，界面可提示一次）。
     SG.Progress（本文件末尾，接口说明见该处注释）：经验与等级、按武力 / 智力的格数与等阶上限、
       升级候选（3 选 1）、习得、电脑自动习得、自动结算战斗的经验。
   第四版（INTEGRATION §4、§6；W0 整合核心）：
     存档 v4：SAVE_KEY = 'sanguozhi2_save_v4'（自动档），SAVE_VERSION = 4；读档接受版本 2、3、4，
       2 / 3 先补齐成长数据，再 SG.MigrateV4.run（ITEMS → TITLES → ENGAGEMENT → TOURNAMENT → COMBAT / DOMESTIC）；
       v4 存档读入后 SG.MigrateV4.load；新局末尾 SG.MigrateV4.init。旧键（v3 / v2）只读，v4 从不写它们。
       顶层存档字段由 GameState.addSaveKey(key) 登记（INTEGRATION §6.1 的 items / officeSeq / engage / tourney /
       combos / comboSeen / comboMastery / fxStages / aiBudget 已预先登记），只有 G 上确有该字段时才写入
       （关掉的系统不建状态，存档与 v3 只差版本号）。武将 / 城 / 势力对象上的可枚举字段照旧全部存档。
     存档位（UI-V4 §6）：GameState.SLOTS = ['auto', 1, 2, 3]；g.saveSlot(slot, extra?) → bool、GameState.loadSlot(slot)、
       GameState.listSaves() → [{ slot, scenario, factionKey, factionName, ruler, year, month, cities, generals, gold,
       savedAt, playMs, version, thumb }]（另含旧版记录 { slot: 'legacy', version }）、GameState.deleteSlot(slot)；
       先写临时键再替换，写不下（配额）时旧档不动，GameState.saveError = '存储空间不足：请删除一份记录'。
     Commands 的公式一律经由 SG.Econ（js/econ.js；未载入时用本文件里与之逐位相同的 v3 公式），玩家与电脑共用；
       每条指令结束时写 Commands.lastResult（DOMESTIC-FX §3.2 + Econ 字段 g0 / pct / mods / tier，INTEGRATION §4.7），
       然后 SG.Events.emit('command', cmd, res)（= Commands.onCommand(fn)）。不多取任何随机数。
       新增可选参数：ally(from, f, target, gift, envoy)、allyChance(f, target, gift, envoy)、hire(target, recruiter, f, city, { promise, via })、
       hireChance(target, recruiter, f, promise)；新指令 Commands.appoint(c, gen)（任命太守）。
     GameState.generalsIn 跳过外出（gen.away.until > 月序）与「伤病且不能行动」（gen.injury.away）的武将（INTEGRATION §6.4）。
     GameState.allied 另读提供者 allied(a, b)（ENGAGEMENT 的盟约）；SG.cityDefense 加 SG.Mods.cityDefBonus(c)（官爵驻军、营建城墙）。
     Progress.vetLevel(gen) = levelFor(exp − 0.5 × dexp)（无内政经验时 = level）；vetK 用它；gain(gen, n, why)：
       why ≠ 'battle' 的经验记入 gen.dexp；why 为 'battle' / 'tourney' / 'story' 时先乘 SG.Mods.exp（'domestic' 由 ENGAGEMENT
       在每月上限之前自己乘）；battleExp 的 crits 每次 +2。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;
  const Balance = SG.Balance;
  // 当前剧本（选择剧本时 game.js 替换 SG.ScenarioData，这里始终读取最新的那一份）
  const ScenarioData = new Proxy({}, { get: (t, k) => SG.ScenarioData[k] });
  const Seq = SG.Seq;

  const SAVE_KEY = 'sanguozhi2_save_v4';        // 第四版：自动档（INTEGRATION §6.1）
  const V3_SAVE_KEY = 'sanguozhi2_save_v3';     // 第三版存档：只读，读取时迁移（SG.MigrateV4.run）
  const V2_SAVE_KEY = 'sanguozhi2_save_v2';     // 第二版存档：只读，读取时迁移（补齐成长数据，再走第四版迁移）
  const LEGACY_SAVE_KEY = 'sanguozhi2_save';
  const SAVE_VERSION = 4;
  // 存档位（UI-V4 §6）：自动档用 SAVE_KEY，手动档 1–3 各一个键，另有一个索引键
  const SLOTS = Object.freeze(['auto', 1, 2, 3]);
  const SLOT_PREFIX = 'sanguozhi2_v4_save_';
  const SLOT_INDEX_KEY = 'sanguozhi2_v4_saves';
  const TMP_SUFFIX = '.tmp';
  function slotKey(slot) { return slot === 'auto' || slot === undefined || slot === null ? SAVE_KEY : SLOT_PREFIX + slot; }
  // 第四版的顶层存档字段（INTEGRATION §6.1）；各模块可再登记（GameState.addSaveKey）
  const SAVE_KEYS = new Set(['items', 'officeSeq', 'engage', 'tourney', 'combos', 'comboSeen', 'comboMastery', 'fxStages', 'aiBudget', 'playMs']);
  const CORE_KEYS = new Set(['version', 'scenario', 'year', 'month', 'player', 'tokens', 'seed', 'cities', 'generals', 'factions', 'alliance', 'log']);
  function emit(name, a, b, c) { const E = SG.Events; if (E) E.emit(name, a, b, c); }
  // 当前剧本的键：'classic' | 'world'
  function scenarioId() {
    const S = SG.Scenarios;
    if (S) for (const k of Object.keys(S)) if (S[k] === SG.ScenarioData) return k;
    return 'classic';
  }
  // 由剧本数据重建、不进存档的字段（不可枚举：JSON.stringify / Object.assign 都不会带上）
  function derived(o, k, v) { Object.defineProperty(o, k, { value: v, writable: true, configurable: true, enumerable: false }); }

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
      // 第三版：成长（SG.Progress.ensure 补齐 tactics 等；level 恒由 exp 推出）
      exp: 0, level: 1,
      specials: [],  // 已习得的「可习得必杀」编号（专属必杀不在此列）
      tactics: null, // 已会的计策编号（null = 尚未补齐：ensure 按智力填入原四计）
      record: null,  // 战绩 { battles, wins, routs, duels }
    }, o);
  }
  function newFaction(o) {
    return Object.assign({ id: 0, key: '', name: '', color: '#ffffff', ruler: -1, virtue: 0, fame: 0, alive: true }, o);
  }

  // C# 属性的替代
  // 第二版：经纬度 → 地图坐标与地形生成共用 SG.project（js/world-geo.js；中国本部与旧版公式完全相同）
  SG.mapPos = function (c) { return SG.project ? SG.project(c.lon, c.lat) : { x: (c.lon - 100) * 5, y: (c.lat - 23) * 5.6 }; };
  // 两城之间是否为海路（剧本 SeaLinks；地图据此画航线、行军沿航线前进）
  SG.linkIsSea = function (a, b) { const g = SG.G; return !!(g && g.seaLinks && g.seaLinks.has(Math.min(a, b) + '-' + Math.max(a, b))); };
  // 第四版：+ 提供者 cityDef（TITLES 驻军 garrison、ENGAGEMENT 城墙 / 地震；无提供者时为 0）
  SG.cityDefense = function (c) { const d = 20 + M.idiv(c.town, 12); return SG.Mods ? d + SG.Mods.cityDefBonus(c) : d; };
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
      this.scenario = scenarioId();
      this.seaLinks = new Set();
    }

    get monthIndex() { return this.year * 12 + this.month; }

    // ------------------------------------------------------------ 创建 --
    static newGame(playerFactionKey, scenario) {
      if (scenario && SG.Scenarios && SG.Scenarios[scenario]) SG.ScenarioData = SG.Scenarios[scenario];
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
        Progress.ensure(gen);
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
      // 第四版：各模块的新局初始化（ITEMS 起始装备 → TITLES 授官 → ENGAGEMENT 民心 / 邦交 → TOURNAMENT → COMBAT / DOMESTIC）
      if (SG.MigrateV4) SG.MigrateV4.init(g);
      if (SG.Clock) SG.Clock.monthStart(g);
      return g;
    }

    // 读档或新建后重建非序列化数据：连线、海路、文化 / 地域 / 生年 / 性别（剧本附加列与 SG.WorldData）
    init() {
      const byKey = new Map(this.cities.map(c => [c.key, c]));
      const city = k => { const c = byKey.get(k); if (!c) throw new Error('city not in scenario: ' + k); return c; };
      for (const c of this.cities) c.links = [];
      for (const l of ScenarioData.Links) {
        const p = l.split('-');
        const a = city(p[0]); const b = city(p[1]);
        a.links.push(b.id); b.links.push(a.id);
      }
      this.seaLinks = new Set();
      for (const l of ScenarioData.SeaLinks || []) {
        const p = l.split('-');
        const a = city(p[0]).id, b = city(p[1]).id;
        this.seaLinks.add(Math.min(a, b) + '-' + Math.max(a, b));
      }
      const WD = SG.WorldData, WI = SG.WorldInfo || {};
      const cityCult = {}, facCult = {}, facReg = {}, genCult = {}, genBorn = {};
      for (const l of ScenarioData.Cities) { const p = l.split('|'); if (p[10]) cityCult[p[0]] = p[10]; }
      for (const l of ScenarioData.Factions) { const p = l.split('|'); if (p[6]) facCult[p[0]] = p[6]; if (p[7]) facReg[p[0]] = p[7]; }
      for (const l of ScenarioData.Generals) { const p = l.split('|'); if (p[9]) genCult[p[0]] = p[9]; if (p[10]) genBorn[p[0]] = parseInt(p[10], 10); }
      for (const c of this.cities) derived(c, 'culture', cityCult[c.key] || (WD ? WD.cultureOfCity(c.key) : 'han'));
      for (const f of this.factions) {
        derived(f, 'culture', facCult[f.key] || (WD ? WD.cultureOfFaction(f.key) : 'han'));
        derived(f, 'region', facReg[f.key] || (WD ? WD.regionOfFaction(f.key) : 'zhongyuan'));
      }
      for (const x of this.generals) {
        derived(x, 'culture', genCult[x.name] || (WD ? WD.cultureOfGeneral(x.name) : 'han'));
        let born = genBorn[x.name];
        if (!(born > 0) && WD) born = WD.bornOf(x.name);
        derived(x, 'born', born > 0 ? born : null);
        const sex = (WI[x.name] && WI[x.name].sex) || (WD ? WD.sexOf(x.name) : 'm');
        derived(x, 'sex', sex === 'f' ? 'f' : 'm');
        derived(x, 'female', sex === 'f');
      }
    }

    // ------------------------------------------------------------ 查询 --
    cityByKey(k) { return this.cities.find(c => c.key === k); }
    factionByKey(k) { return this.factions.find(f => f.key === k); }
    get playerFaction() { return this.factions[this.player]; }
    citiesOf(f) { return this.cities.filter(c => c.owner === f); }
    generalsOf(f) { return this.generals.filter(x => x.faction === f && !x.dead); }
    // 第四版：外出（商队、探索、人质、告假）与伤病不能行动的武将不算在城中（INTEGRATION §6.4）
    generalsIn(city, f) {
      const mi = this.monthIndex;
      return this.generals.filter(x => x.city === city && x.faction === f && !x.dead
        && !(x.away && x.away.until > mi) && !(x.injury && x.injury.away && x.injury.until > mi));
    }
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
      if (this.alliance[a * this.factions.length + b] > this.monthIndex) return true;
      return SG.Mods ? SG.Mods.alliedExtra(a, b) : false;   // 第四版：ENGAGEMENT 的盟约（提供者 allied）
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
      emit('faction.death', f);
    }

    // C# 的 Log(string)。G.log(text) 与 G.addLog(text) 等价（见 makeLog）。
    addLog(s) {
      this.log.push(this.year + '年' + this.month + '月 ' + s);
      if (this.log.length > 80) this.log.shift();
    }

    // ------------------------------------------------------------ 存档 --
    // 第四版：核心字段 + 登记过的顶层字段（G 上确有时才写，INTEGRATION §6.1）
    toJSON() {
      const o = {
        version: SAVE_VERSION, scenario: this.scenario || 'classic',
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
      for (const k of SAVE_KEYS) if (this[k] !== undefined && !CORE_KEYS.has(k)) o[k] = this[k];
      return o;
    }
    static addSaveKey(k) {
      if (typeof k !== 'string' || !k || CORE_KEYS.has(k)) throw new Error('GameState.addSaveKey: bad key ' + k);
      SAVE_KEYS.add(k);
    }
    static saveKeys() { return Array.from(SAVE_KEYS); }

    // 自动档（v3 的 save() 照旧可用）
    save() { return this.saveSlot('auto'); }
    // 写入存档位：先写临时键，成功后替换正式键；写不下时旧档不动（UI-V4 §6）
    saveSlot(slot, extra) {
      GameState.saveError = null;
      slot = normSlot(slot);
      if (slot === null) { GameState.saveError = '没有这个存档位。'; return false; }
      const st = storage();
      if (!st) { GameState.saveError = '此浏览器不允许本地存档'; return false; }
      let text;
      try { text = JSON.stringify(this); } catch (e) { console.log('save failed', e); GameState.saveError = '记录失败。'; return false; }
      const key = slotKey(slot), tmp = key + TMP_SUFFIX;
      try { st.setItem(tmp, text); } catch (e) {
        try { st.removeItem(tmp); } catch (e2) { /* 忽略 */ }
        GameState.saveError = isQuota(e) ? '存储空间不足：请删除一份记录' : '记录失败。';
        console.log('save failed', e);
        return false;
      }
      try { st.removeItem(key); st.setItem(key, text); try { st.removeItem(tmp); } catch (e) { /* 忽略 */ } } catch (e) { /* 新档留在临时键里，读档时会用它 */ }
      writeIndexEntry(st, summaryOf(this, slot, extra));
      return true;
    }
    // 读档时要读的文字：第四版自动档 → 第三版键 → 第二版键（只读，读取时迁移）
    static saveText(st) {
      st = st || storage();
      if (!st) return null;
      try { return st.getItem(SAVE_KEY) || st.getItem(SAVE_KEY + TMP_SUFFIX) || st.getItem(V3_SAVE_KEY) || st.getItem(V2_SAVE_KEY) || null; } catch (e) { return null; }
    }
    static hasSave() {
      const st = storage();
      if (!st) return false;
      if (GameState.saveText(st)) return true;
      for (const s of SLOTS) if (s !== 'auto' && readSlotText(st, s)) return true;
      return false;
    }
    // 只有旧版（第一版）存档：读不了，标题画面提示“新版本，旧存档无法读取”
    static hasLegacySave() {
      const st = storage();
      if (!st) return false;
      try { return !GameState.hasSave() && !!st.getItem(LEGACY_SAVE_KEY); } catch (e) { return false; }
    }
    // 存档摘要（不完整读档）：{ scenario, year, month, faction, version } | null —— load() 将读的那一份
    static saveInfo() {
      const st = storage();
      if (!st) return null;
      try {
        const t = GameState.saveText(st) || newestManualText(st);
        const d = JSON.parse(t || 'null');
        if (!d || !Array.isArray(d.factions)) return null;
        const f = d.factions[d.player];
        return { scenario: d.scenario || 'classic', year: d.year | 0, month: d.month | 0, faction: f ? f.name : '', version: d.version | 0 };
      } catch (e) { return null; }
    }
    // 删除「当前存档」：自动档与只读的旧版键（玩家明确要删时）；手动档不动
    static deleteSave() {
      const st = storage();
      if (!st) return;
      for (const k of [SAVE_KEY, SAVE_KEY + TMP_SUFFIX, V3_SAVE_KEY, V2_SAVE_KEY]) { try { st.removeItem(k); } catch (e) { /* 忽略 */ } }
      removeIndexEntry(st, 'auto');
    }
    // ---- 存档位（UI-V4 §6）----
    static listSaves() {
      const st = storage();
      if (!st) return [];
      let idx = readIndex(st);
      const out = [];
      for (const s of SLOTS) {
        const text = readSlotText(st, s);
        if (!text) continue;
        let e = idx && idx.find(x => x && String(x.slot) === String(s));
        if (!e) { try { e = summaryOf(JSON.parse(text), s, null, true); } catch (err) { e = { slot: s, broken: true }; } }
        out.push(e);
      }
      // 旧版记录（第三版 / 第二版）：只读，读取时迁移
      try {
        const t3 = st.getItem(V3_SAVE_KEY), t2 = t3 ? null : st.getItem(V2_SAVE_KEY);
        const t = t3 || t2;
        if (t) { let e; try { e = summaryOf(JSON.parse(t), 'legacy', null, true); } catch (err) { e = { slot: 'legacy', broken: true }; } e.version = t3 ? 3 : 2; e.legacy = true; out.push(e); }
      } catch (e) { /* 忽略 */ }
      return out;
    }
    static loadSlot(slot) {
      GameState.loadError = null;
      const st = storage();
      if (!st) { GameState.loadError = '浏览器不允许读取存档。'; return null; }
      let text = null;
      try { text = slot === 'legacy' ? (st.getItem(V3_SAVE_KEY) || st.getItem(V2_SAVE_KEY)) : (normSlot(slot) === null ? null : readSlotText(st, normSlot(slot))); } catch (e) { text = null; }
      if (!text) { GameState.loadError = '没有存档。'; return null; }
      return GameState.loadText(text);
    }
    static deleteSlot(slot) {
      const st = storage();
      if (!st) return false;
      if (slot === 'legacy') { for (const k of [V3_SAVE_KEY, V2_SAVE_KEY]) { try { st.removeItem(k); } catch (e) { /* 忽略 */ } } return true; }
      slot = normSlot(slot);
      if (slot === null) return false;
      for (const k of [slotKey(slot), slotKey(slot) + TMP_SUFFIX]) { try { st.removeItem(k); } catch (e) { /* 忽略 */ } }
      removeIndexEntry(st, slot);
      return true;
    }
    // 不合格的存档抛出带中文说明的 Error（load() 把说明放进 GameState.loadError）
    static fromJSON(text) {
      const d = typeof text === 'string' ? JSON.parse(text) : text;
      if (!d || !Array.isArray(d.cities) || !Array.isArray(d.generals) || !Array.isArray(d.factions)) return null;
      // 第二版、第三版存档可读：补齐成长数据（Progress.ensure），再走第四版迁移（SG.MigrateV4.run）
      if (typeof d.version === 'number' && d.version > SAVE_VERSION) throw new Error('这份存档来自更新的版本，无法读取。');
      if (d.version !== SAVE_VERSION && d.version !== 3 && d.version !== 2) throw new Error('新版本，旧存档无法读取。');
      const sid = d.scenario || 'classic';
      const scen = SG.Scenarios ? SG.Scenarios[sid] : (sid === 'classic' ? SG.ScenarioData : null);
      if (!scen) throw new Error('存档所用的剧本（' + sid + '）不存在，无法读取。');
      // 城池必须与剧本一致（连线、海路、文化都按剧本重建）
      const keys = new Set(scen.Cities.map(l => l.slice(0, l.indexOf('|'))));
      if (d.cities.length !== keys.size || d.cities.some(c => !c || !keys.has(c.key))) throw new Error('存档与当前版本的剧本数据不符，无法读取。');
      SG.ScenarioData = scen;
      const g = new GameState();
      g.scenario = sid;
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
      for (const x of g.generals) Progress.ensure(x);
      derived(g, 'migratedFrom', d.version === SAVE_VERSION ? 0 : d.version | 0);
      if (d.version === SAVE_VERSION) {
        // 第四版的顶层字段（未登记的也原样保留：读档不丢任何模块的数据）
        for (const k of Object.keys(d)) {
          if (CORE_KEYS.has(k) || d[k] === undefined) continue;
          if (!SAVE_KEYS.has(k)) SAVE_KEYS.add(k);
          g[k] = d[k];
        }
        if (SG.MigrateV4) SG.MigrateV4.load(g, d);
      } else if (SG.MigrateV4) SG.MigrateV4.run(g, d.version | 0);
      if (SG.Clock) SG.Clock.monthStart(g, Math.max(g.tokens, g.player >= 0 ? g.tokensFor(g.player) : 0));
      return g;
    }
    static loadText(text) {
      GameState.loadError = null;
      const prev = SG.ScenarioData;
      try {
        const g = GameState.fromJSON(text);
        if (!g) GameState.loadError = '存档已损坏，无法读取。';
        return g;
      } catch (e) {
        SG.ScenarioData = prev;
        console.log('load failed', e);
        GameState.loadError = e && /[一-鿿]/.test(e.message || '') ? e.message : '存档已损坏，无法读取。';
        return null;
      }
    }
    // 自动档 → 第三版 / 第二版（迁移）→ 最近的手动档
    static load() {
      GameState.loadError = null;
      const st = storage();
      if (!st) { GameState.loadError = '浏览器不允许读取存档。'; return null; }
      let text = null;
      try { text = GameState.saveText(st) || newestManualText(st); } catch (e) { text = null; }
      if (!text) {
        let legacy = false;
        try { legacy = !!st.getItem(LEGACY_SAVE_KEY); } catch (e) { /* 忽略 */ }
        GameState.loadError = legacy ? '新版本，旧存档无法读取。' : '没有存档。';
        return null;
      }
      return GameState.loadText(text);
    }
  }
  GameState.SAVE_KEY = SAVE_KEY;
  GameState.V3_SAVE_KEY = V3_SAVE_KEY;
  GameState.V2_SAVE_KEY = V2_SAVE_KEY;
  GameState.LEGACY_SAVE_KEY = LEGACY_SAVE_KEY;
  GameState.SAVE_VERSION = SAVE_VERSION;
  GameState.SLOTS = SLOTS;
  GameState.SLOT_INDEX_KEY = SLOT_INDEX_KEY;
  GameState.slotKey = slotKey;
  GameState.loadError = null;
  GameState.saveError = null;

  // ---- 存档位的工具 ----
  function normSlot(slot) {
    if (slot === undefined || slot === null || slot === 'auto') return 'auto';
    const n = typeof slot === 'number' ? slot : parseInt(slot, 10);
    return n >= 1 && n <= 3 ? n : null;
  }
  function isQuota(e) { return !!e && (e.name === 'QuotaExceededError' || e.name === 'NS_ERROR_DOM_QUOTA_REACHED' || e.code === 22 || e.code === 1014 || /quota/i.test(e.message || '')); }
  function readSlotText(st, slot) {
    const k = slotKey(slot);
    try { return st.getItem(k) || st.getItem(k + TMP_SUFFIX) || null; } catch (e) { return null; }
  }
  function newestManualText(st) {
    let best = null, bestT = -1;
    const idx = readIndex(st) || [];
    for (const s of SLOTS) {
      if (s === 'auto') continue;
      const t = readSlotText(st, s);
      if (!t) continue;
      const e = idx.find(x => x && String(x.slot) === String(s));
      const at = e && e.savedAt > 0 ? e.savedAt : 0;
      if (at > bestT) { bestT = at; best = t; }
    }
    return best;
  }
  function readIndex(st) {
    try { const a = JSON.parse(st.getItem(SLOT_INDEX_KEY) || 'null'); return Array.isArray(a) ? a : null; } catch (e) { return null; }
  }
  function writeIndex(st, arr) {
    try { st.setItem(SLOT_INDEX_KEY, JSON.stringify(arr)); return true; } catch (e) { return false; }
  }
  function writeIndexEntry(st, e) {
    const arr = (readIndex(st) || []).filter(x => x && String(x.slot) !== String(e.slot));
    arr.push(e);
    arr.sort((a, b) => SLOTS.indexOf(a.slot) - SLOTS.indexOf(b.slot));
    if (!writeIndex(st, arr)) { e.thumb = null; writeIndex(st, arr); }   // 写不下缩略图时不带缩略图
  }
  function removeIndexEntry(st, slot) {
    const arr = readIndex(st);
    if (!arr) return;
    writeIndex(st, arr.filter(x => x && String(x.slot) !== String(slot)));
  }
  // 存档位卡片的摘要（d 为 GameState 或存档 JSON）
  function summaryOf(d, slot, extra, fromJSON) {
    extra = extra || {};
    const facs = d.factions || [], f = facs[d.player], gens = d.generals || [], cities = d.cities || [];
    const ruler = f && f.ruler >= 0 ? gens[f.ruler] : null;
    let nc = 0, gold = 0, ng = 0;
    for (const c of cities) if (f && c.owner === f.id) { nc++; gold += c.gold | 0; }
    for (const x of gens) if (f && x.faction === f.id && !x.dead) ng++;
    return {
      slot, scenario: d.scenario || 'classic', factionKey: f ? f.key : '', factionName: f ? f.name : '', ruler: ruler ? ruler.name : '',
      year: d.year | 0, month: d.month | 0, cities: nc, generals: ng, gold,
      savedAt: fromJSON ? 0 : Date.now(), playMs: extra.playMs !== undefined ? extra.playMs : (d.playMs | 0),
      version: fromJSON ? d.version | 0 : SAVE_VERSION, thumb: extra.thumb || null,
    };
  }

  // ================================================================ Commands --
  const DevKind = Object.freeze({ Land: 0, Industry: 1, Town: 2 });

  function G() { return SG.G; }
  const AI_MOVE_HOPS = 2;   // 电脑调动武将的最远路程（相邻 = 1）
  function R(a, b) { return SG.Random.rangeInt(a, b + 1); }

  // ---- 第四版：一条内政公式（INTEGRATION §4）。js/econ.js 未载入时（v3 的 Node 测试、设计原型）用下面这份 v3 公式，
  //      它与「没有任何提供者的 SG.Econ」逐位相同（tests/econ.js 断言）。----
  const KIND_KEYS = ['land', 'industry', 'town'];
  const V3Econ = {
    develop(c, g, kind, roll) {
      const key = typeof kind === 'number' ? KIND_KEYS[kind] || 'town' : kind === 'land' || kind === 'industry' ? kind : 'town';
      const g0 = Balance.DevelopBase + M.idiv(g.pol, Balance.DevelopPolDiv) + roll;
      return { kind: key, g0, pct: 0, gain: g0, roll, mods: { gear: 0, title: 0, project: 0 } };
    },
    troopsPerGold() { return Balance.TroopsPerGold; },
    recruitMods() { return { gear: 0, title: 0, project: 0, pct: 0 }; },
    trainGain(g, roll) { return Balance.TrainBase + M.idiv(g.war, 10) + roll; },
    trainMods() { return { gear: 0, title: 0, project: 0, bonus: 0 }; },
    searchP(c, g) { return 0.22 + g.intel / 260 + G().factions[c.owner].virtue / 420; },
    searchMods() { return { gear: 0, title: 0, project: 0 }; },
    hireP(target, recruiter, faction, promise) {
      const f = G().factions[faction];
      let p = 0.32 + f.virtue / 220 + recruiter.intel / 450 - (target.war + target.intel + target.pol) / 900;
      if (target.faction >= 0) p -= target.loyalty / 160; // 俘虏
      return M.clamp(p + (promise || 0), 0.05, 0.95);
    },
    allyP(f, target, gift) {
      const a = G().factions[f]; const b = G().factions[target];
      let p = 0.25 + a.virtue / 300 + gift / 1200 + (a.fame - b.fame) / 300;
      if (G().cityCount(f) > G().cityCount(target) * 2) p += 0.15; // 弱者愿与强者结盟
      return M.clamp(p, 0.05, 0.9);
    },
    discordP(agent, target) { return M.clamp(0.4 + (agent.intel - target.intel) / 100, 0.1, 0.9); },
    persuadeP(agent, target, f) {
      if (G().isRuler(target)) return 0;
      const p = (100 - target.loyalty) / 110 * (0.45 + agent.intel / 180) * (0.55 + G().factions[f].virtue / 220);
      return M.clamp(p, 0, 0.9);
    },
    rewardLoy(g, roll) { return Balance.RewardLoyalty + roll; },
    tradeK() { return 0; },
    allianceMonths() { return Balance.AllianceMonths; },
  };
  function econ() { return SG.Econ || V3Econ; }
  function virtueOf(f) { return SG.Mods ? SG.Mods.virtue(f) : G().factions[f].virtue; }
  function effOf(gen, k) { return SG.Mods ? SG.Mods.eff(gen, k) : gen[k]; }
  function zMod(name, a, b) { return SG.Mods ? SG.Mods.z(name, a, b) : 0; }
  // 结果记录：写好 Commands.lastResult，分级（SG.DomesticTiers 或 SG.Econ 的开发分级），再通知监听者
  const cmdHooks = [];
  function finish(cmd, city, gen, res) {
    const g = G();
    res.cmd = cmd; res.city = city ? city.id : null; res.gen = gen ? gen.id : null; res.month = g ? g.monthIndex : 0;
    res.f = city ? city.owner : gen ? gen.faction : -1;
    if (res.tier === undefined) res.tier = tierOf(res);
    Commands.lastResult = res;
    for (const fn of cmdHooks.slice()) { try { fn(cmd, res); } catch (e) { console.error(e); } }
    emit('command', cmd, res);
    return res;
  }
  function tierOf(res) {
    const T = SG.DomesticTiers;
    if (T && typeof T.tier === 'function') {
      try { const t = T.tier(res); return t && typeof t === 'object' ? t.tier : t || null; } catch (e) { return null; }
    }
    const E = SG.Econ;
    if (E && res.ok !== false && res.cmd === 'develop' && E.tierDevelop) return E.tierDevelop(res);
    return null;
  }
  function borderEnemy(c, f) {
    const g = G();
    return c.links.some(j => { const o = g.cities[j].owner; return o >= 0 && o !== f && !g.allied(o, f); });
  }

  // 战略指令（玩家与电脑共用）
  const Commands = {
    searchFound: null,
    // 第四版：最近一条指令的结果记录（DOMESTIC-FX §3.2；INTEGRATION §4.7 的 Econ 字段）
    lastResult: null,
    // 第四版：指令结束后的监听者（ENGAGEMENT 的内政经验、民心、计数……）；返回取消函数
    onCommand(fn) { cmdHooks.push(fn); return () => { const i = cmdHooks.indexOf(fn); if (i >= 0) cmdHooks.splice(i, 1); }; },
    // 第四版：ITEMS 的搜索寻宝（§8.5）。设为 (c, g) => ({ item, name }) | null；在寻人之后、金粮之前调用
    searchItem: null,
    econ,
    V3Econ,     // 测试用：没有 js/econ.js 时的 v3 公式（tests/econ.js 断言它与无提供者的 SG.Econ 逐位相同）

    devName(k) { return k === DevKind.Land ? '土地' : k === DevKind.Industry ? '产业' : '町'; },

    // ---------------------------------------------------------- 开发 --
    develop(c, g, kind) {
      if (c.gold < Balance.DevelopCost) { finish('develop', c, g, { ok: false, kind: KIND_KEYS[kind] || 'town', reason: 'gold' }); return '金不足，无法开发。'; }
      c.gold -= Balance.DevelopCost;
      const roll = R(0, 4);
      const r = econ().develop(c, g, kind, roll);
      const key = r.kind;
      const before = c[key];
      c[key] = Math.min(Balance.StatMax, before + r.gain);
      const gain = c[key] - before;
      finish('develop', c, g, { ok: true, kind: key, roll, before, after: c[key], gain, cost: Balance.DevelopCost, g0: r.g0, pct: r.pct, mods: r.mods });
      const n = Commands.devName(kind);
      return `${g.name}主持${n}开发，${n} +${gain}。`;
    },

    // ---------------------------------------------------------- 征兵 --
    recruitMax(c, g) {
      const room = SG.maxTroops(g) - g.troops;
      const tpg = econ().troopsPerGold(c, g);
      const gold = Math.min(c.gold, Balance.RecruitGoldMax);
      const byGold = tpg === Balance.TroopsPerGold ? gold * Balance.TroopsPerGold : Math.floor(gold * tpg);
      const byPop = M.idiv(c.population, 10);
      return Math.max(0, Math.min(room, Math.min(byGold, byPop)));
    },
    recruit(c, g, troops) {
      troops = Math.min(troops, Commands.recruitMax(c, g));
      if (troops <= 0) { finish('recruit', c, g, { ok: false, n: 0, reason: 'limit' }); return '无法征兵（金、人口或带兵上限不足）。'; }
      const tpg = econ().troopsPerGold(c, g);
      const cost = Math.ceil(troops / tpg);
      const before = g.troops, trainingBefore = g.training, cityBefore = G().troopsIn(c);
      c.gold -= cost; c.population -= troops;
      const total = g.troops + troops;
      g.training = M.idiv(g.training * g.troops + (30 + zMod('recruitDrill', c)) * troops, Math.max(1, total));
      g.troops = total;
      const rm = econ().recruitMods(c, g);
      finish('recruit', c, g, { ok: true, n: troops, cost, before, after: g.troops, max: SG.maxTroops(g), cityBefore, cityAfter: G().troopsIn(c),
        trainingBefore, trainingAfter: g.training, tpg, g0: troops, pct: rm.pct, mods: { gear: rm.gear, title: rm.title, project: rm.project } });
      return `${g.name}征得新兵 ${troops} 人（花费 ${cost} 金）。`;
    },

    // ---------------------------------------------------------- 训练 --
    train(g) {
      const c = G().cities[g.city];
      if (g.troops <= 0) { finish('train', c, g, { ok: false, reason: 'troops' }); return g.name + '麾下无兵，无法训练。'; }
      const before = g.training;
      const roll = R(0, 4);
      g.training = Math.min(100, g.training + econ().trainGain(g, roll, c));
      const tm = econ().trainMods(g, c);
      finish('train', c, g, { ok: true, roll, before, after: g.training, troops: g.troops, max: SG.maxTroops(g),
        g0: Balance.TrainBase + M.idiv(effOf(g, 'war'), 10) + roll, pct: 0, bonus: tm.bonus, mods: { gear: tm.gear, title: tm.title, project: tm.project } });
      return `${g.name}操练兵马，训练度 ${before} → ${g.training}。`;
    },

    // ---------------------------------------------------------- 搜索 --
    search(c, g) {
      Commands.searchFound = null;
      const hidden = G().generals.filter(x => x.city === c.id && x.hidden && SG.isFree(x));
      const p = econ().searchP(c, g);
      const mods = econ().searchMods(c, g);
      if (hidden.length > 0 && SG.Random.value() < p) {
        const h = hidden[SG.Random.rangeInt(0, hidden.length)];
        h.hidden = false;
        Commands.searchFound = h;
        finish('search', c, g, { ok: true, outcome: 'talent', found: h.id, value: 0, p, mods });
        return `${g.name}四处寻访，发现了在野的人才——${h.name}！`;
      }
      if (typeof Commands.searchItem === 'function') {
        const it = Commands.searchItem(c, g);
        if (it) {
          finish('search', c, g, { ok: true, outcome: 'item', item: it.item !== undefined ? it.item : null, itemId: it.id || null, value: 0, p, mods });
          return `${g.name}在城中寻访，得到了宝物「${it.name || '宝物'}」！`;
        }
      }
      const r = SG.Random.value();
      if (r < 0.25) { const v = R(60, 220); c.gold += v; finish('search', c, g, { ok: true, outcome: 'gold', value: v, p, mods }); return `${g.name}在城中搜索，发现了 ${v} 金。`; }
      if (r < 0.45) { const v = R(800, 2500); c.food += v; finish('search', c, g, { ok: true, outcome: 'food', value: v, p, mods }); return `${g.name}在城中搜索，发现了 ${v} 粮。`; }
      finish('search', c, g, { ok: false, outcome: 'none', value: 0, p, mods });
      return g.name + '四处寻访，一无所获。';
    },

    // ---------------------------------------------------------- 登用 --
    // promise：许官等许诺的概率加成（TITLES）
    hireChance(target, recruiter, faction, promise) { return econ().hireP(target, recruiter, faction, promise || 0); },
    // opts = { promise, via: 'hire' | 'search' | 'captive' }
    hire(target, recruiter, faction, city, opts) {
      opts = opts || {};
      const p = Commands.hireChance(target, recruiter, faction, opts.promise);
      const via = opts.via || (target.faction >= 0 ? 'captive' : Commands.searchFound === target ? 'search' : 'hire');
      const cityObj = G().cities[city] || null;
      if (SG.Random.value() > p) { finish('hire', cityObj, recruiter, { ok: false, p, target: target.id, via }); return false; }
      target.faction = faction; target.city = city; target.hidden = false;
      target.loyalty = M.clamp(60 + M.idiv(virtueOf(faction), 4) + R(-5, 10), 40, 100);
      target.training = 40;
      target.moved = true;
      finish('hire', cityObj, recruiter, { ok: true, p, target: target.id, via, loyalty: target.loyalty });
      return true;
    },

    // ---------------------------------------------------------- 赏赐 --
    reward(c, g) {
      if (c.gold < Balance.RewardGold) { finish('reward', c, g, { ok: false, reason: 'gold' }); return '金不足。'; }
      c.gold -= Balance.RewardGold;
      const before = g.loyalty;
      const roll = R(0, 4);
      g.loyalty = Math.min(100, g.loyalty + econ().rewardLoy(g, roll, c.owner >= 0 ? G().ruler(c.owner) : null));
      finish('reward', c, g, { ok: true, roll, before, after: g.loyalty, cost: Balance.RewardGold, gift: null, giftFits: false });
      return `赏赐${g.name} ${Balance.RewardGold} 金，忠诚 ${before} → ${g.loyalty}。`;
    },

    // ---------------------------------------------------------- 任命 --
    // 第四版：任命太守也是一条指令（DOMESTIC-FX §5.15 的结果记录）
    appoint(c, who) {
      const prev = c.governor >= 0 ? G().generals[c.governor] : null;
      c.governor = who.id;
      finish('appoint', c, who, { ok: true, pol: who.pol, prevPol: prev ? prev.pol : 0, prev: prev ? prev.id : null });
      return `任命${who.name}为${c.name}太守。`;
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
      const path = G().routeBetween(from, to, g.faction);
      if (!path) { finish('move', from, g, { ok: false, to: to.id, reason: 'route' }); return `${to.name}与${from.name}之间没有己方城池相连，${g.name}无法移驻。`; }
      g.city = to.id; g.moved = true;
      G().autoGovernor(from); G().autoGovernor(to);
      finish('move', from, g, { ok: true, gens: 1, ids: [g.id], troops: g.troops, hops: path.length - 1, to: to.id, frontline: borderEnemy(to, g.faction), ruler: G().isRuler(g) });
      return `${g.name}率兵 ${g.troops} 人移驻${to.name}。`;
    },
    transport(from, to, gold, food) {
      if (!G().routeBetween(from, to, from.owner)) { finish('transport', from, null, { ok: false, to: to.id, reason: 'route' }); return `${to.name}与${from.name}之间没有己方城池相连，无法输送。`; }
      gold = M.clamp(gold, 0, from.gold); food = M.clamp(food, 0, from.food);
      const destFoodBefore = to.food, destNeed = M.idiv(G().troopsIn(to), Balance.TroopsPerFood);
      from.gold -= gold; from.food -= food; to.gold += gold; to.food += food;
      finish('transport', from, null, { ok: true, to: to.id, gold, food, destFoodBefore, destNeed });
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
    // 第四版：城中商才的折扣 t（ITEMS §3.4；买价 × (1 − t)、卖价 × (1 + t)）
    buyPrice(c) { const p = Commands.foodPrice(), t = econ().tradeK(c); return t > 0 ? p * (1 - t) : p; },
    sellPrice(c) { const p = Commands.foodPrice(), t = econ().tradeK(c); return t > 0 ? p * (1 + t) : p; },
    buyFood(c, gold) {
      gold = M.clamp(gold, 0, c.gold);
      const price = Commands.buyPrice(c);
      const food = M.idiv(gold * 100, price);
      c.gold -= gold; c.food += food;
      finish('trade', c, null, { ok: gold > 0, side: 'buy', price: Commands.foodPrice(), priceK: price, gold, food, goldOut: gold });
      return `以 ${gold} 金购入粮 ${food}。`;
    },
    sellFood(c, food) {
      food = M.clamp(food, 0, c.food);
      const price = Commands.sellPrice(c);
      const gold = M.idiv(M.idiv(food * price, 100) * 9, 10);
      c.food -= food; c.gold += gold;
      finish('trade', c, null, { ok: food > 0, side: 'sell', price: Commands.foodPrice(), priceK: price, gold, food, goldOut: gold });
      return `卖出粮 ${food}，得金 ${gold}。`;
    },

    // ---------------------------------------------------------- 外交策略 --
    // envoy：出使的武将（ITEMS / TITLES 的外交加成；缺省无）
    allyChance(f, target, gift, envoy) { return econ().allyP(f, target, gift, envoy || null); },
    ally(from, f, target, gift, envoy) {
      gift = Math.min(gift, from.gold);
      from.gold -= gift;
      const p = Commands.allyChance(f, target, gift, envoy);
      const rec = { gift, target, p, targetCities: G().cityCount(target), ourCities: G().cityCount(f), envoy: envoy ? envoy.id : null };
      if (SG.Random.value() < p) {
        const months = econ().allianceMonths(f, target, envoy || null);
        G().setAlliance(f, target, G().monthIndex + months);
        finish('ally', from, envoy || null, Object.assign(rec, { ok: true, months }));
        return `${G().factions[target].name}同意结盟！同盟期限 ${months} 个月。`;
      }
      finish('ally', from, envoy || null, Object.assign(rec, { ok: false }));
      return `${G().factions[target].name}拒绝了结盟的提议。`;
    },

    discord(agent, target) {
      const c = G().cities[agent.city];
      if (G().isRuler(target)) { finish('discord', c, agent, { ok: false, target: target.id, d: 0, loyaltyAfter: target.loyalty, p: 0, reason: 'ruler' }); return '离间君主是不可能的。'; }
      const p = econ().discordP(agent, target);
      if (SG.Random.value() > p) { finish('discord', c, agent, { ok: false, target: target.id, d: 0, loyaltyAfter: target.loyalty, p }); return `${target.name}识破了离间之计。`; }
      let d = M.idiv(R(6, 16) * effOf(agent, 'intel'), Math.max(30, effOf(target, 'intel')));
      d = M.clamp(d, 3, 25);
      target.loyalty = Math.max(0, target.loyalty - d);
      finish('discord', c, agent, { ok: true, target: target.id, d, loyaltyAfter: target.loyalty, p });
      return `离间成功！${target.name}的忠诚下降了 ${d}（现为 ${target.loyalty}）。`;
    },

    persuadeChance(agent, target, f) { return econ().persuadeP(agent, target, f); },
    persuade(agent, target, f) {
      const c0 = G().cities[agent.city];
      const p = Commands.persuadeChance(agent, target, f);
      if (SG.Random.value() > p) { finish('persuade', c0, agent, { ok: false, target: target.id, city: false, p }); return `${target.name}拒绝了劝诱。`; }
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
        finish('persuade', c0, agent, { ok: true, target: target.id, city: true, cityId: oldCity.id, p, from: oldF });
        return `${target.name}献城归降！${oldCity.name}归入我方。`;
      }
      target.city = agent.city; target.troops = M.idiv(target.troops, 2); target.moved = true;
      g.autoGovernor(oldCity); g.autoGovernor(g.cities[agent.city]);
      finish('persuade', c0, agent, { ok: true, target: target.id, city: false, p, from: oldF });
      return `${target.name}率部来投！`;
    },
  };

  // ================================================================ Progress --
  // 第三版 §4 成长：经验、等级、可习得必杀与计策（纯逻辑，Node 下可测）。
  //
  //   经验与等级（每位武将，电脑势力同样成长）
  //     Progress.EXP[L]                 升到 L 级所需的累计经验（L = 1..10）
  //     levelFor(exp) → L               expInfo(gen) → { level, exp, cur, next, need, pct, max }（界面进度条用）
  //     gain(gen, amount, why?) → { gen, exp, from, to, learned: [cand] } | null
  //                                     加经验；电脑武将（非玩家势力）有空格即自动习得（learned）
  //     battleExp(x) → 一战所得经验：x = { dmg, heal, routs, cmdRouts, duelWin, duelLoss, specials, tactics, tacFail, crits, won, commander }
  //       参战 10 + 每击杀 40 兵 1 + 每回复 80 兵 1 + 击溃敌部 12（敌主将再 +12）+ 单挑胜 25 / 负 5 +
  //       每次必杀 6 + 每次计策成功 4（失败 1）+ 胜方 25（主将再 +10），一战至多 BATTLE_CAP。
  //       战术战斗（battle-model.js）在分出胜负时自动结算（Mdl.progress）；自动结算的攻城战见 autoBattle。
  //   格数与等阶（升级时解锁；原四计按智力自带、不占格）
  //     specialSlots(gen) / tacticSlots(gen) → { max, unlocked, used, open, tier, statTier, levelTier }
  //       必杀：武力 60/70/80/90 各 1 格（至多 4）；等阶上限 武力 60→1、70→2、80→3、92→4（≈(武力−40)/15）；
  //             第 k 格与第 k 阶在 2/4/6/8 级解锁。
  //       计策：智力 50/60/70/80/90 各 1 格（至多 5）；等阶上限 智力 50→1、65→2、80→3、92→4（≈(智力−35)/15）；
  //             第 k 格在 3/5/7/9/10 级、第 k 阶在 3/5/7/9 级解锁。
  //       第 k 格最高只能学 k 阶（tier = 下一招可学的最高等阶 = min(能力上限, 等级上限, 已学 + 1)），
  //       故招式按 1、2、3、4 阶逐级习得；推迟选择也不能一口气全学高阶。可习得的暗杀（kind assassinate）每人至多一招。
  //     openings(gen) → { special, tactic, total }   现在可学的空格数
  //   升级选择（玩家从 3 个候选中选 1；电脑自动选）
  //     candidates(gen, n = 3) → [{ type: 'special'|'tactic', id, tier, name, desc, rules, color, fx, ref }]
  //       候选确定（按姓名、等级、已学、本局种子），不耗 SG.Random；两类都有空格时两类都会出现；
  //       每类至少一个候选是当前可学的最高等阶。
  //     learn(gen, typeOrCand, id?) → bool        aiPick(gen, cands) → cand        autoLearn(gen) → [cand]
  //     pending(f) → [gen]                       f 势力中有空格且有候选的武将（玩家：战后逐个弹出升级对话框）
  //       对话框循环条件请用 candidates(gen).length > 0（openings 只在载入 tactics-data.js 时计计策格）
  //     playerAutoLearn（缺省 true）：玩家武将也由 aiPick 自动习得；升级对话框上线时设为 false
  //     known(gen) → { specials: [sp], tactics: [tactic] }（专属必杀在前；需 specials.js / tactics-data.js）
  //   自动结算的攻城战（strategy-ai.js 的 Conquest.autoResolve）
  //     snapshot(s) → 战前兵力；autoBattle(s, snap) → [{ gen, exp, from, to, learned, side, won }]
  //       （按战损与战前战力分摊；结果同时写入 s.progress；s.progressDone 防重复）
  //     hookConquest(Conquest)：包装 autoResolve 一次（battle-model.js 载入时调用），不耗随机数
  //   老练（第三版修订）：战术战斗中每高 1 级，攻击、防御与计策威力各 +VET_PER_LEVEL（4%；1 级为 ×1，10 级 ×1.36）
  //     vetK(gen) → 乘数（enabled = false 时恒为 1）；只用于 battle-model.js 的战术战斗，不影响自动结算（autoResolve）
  //   Progress.enabled = false：不再获得经验，战斗只用专属必杀与原四计（tests/sim.js 对比平衡用）
  const INNATE_TACTICS = Object.freeze(['fire', 'rockfall', 'confuse', 'inspire']);   // 与 Defs.tactics 同序
  const INNATE_SET = new Set(INNATE_TACTICS);
  function countAtLeast(list, v) { let n = 0; for (const t of list) if (v >= t) n++; return n; }
  function phash(str) {
    let h = 0x811c9dc5;
    for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 0x01000193); }
    return h >>> 0;
  }
  function genPower(x) { return Math.max(0, x.troops) * (0.55 + x.war / 220) * (0.7 + x.training / 330); }

  const Progress = {
    enabled: true,
    // 玩家武将也自动习得（与电脑相同的选择）。升级对话框接好后（第二阶段，strategy-screen.js）设为 false，
    // 玩家武将改为保留空格，由 pending(G.player) / candidates / learn 逐个选择。
    playerAutoLearn: true,
    MAX_LEVEL: 10,
    EXP: Object.freeze([0, 0, 80, 200, 360, 560, 800, 1080, 1400, 1760, 2160]),
    BATTLE_CAP: 240,
    SPECIAL_SLOT_WAR: Object.freeze([60, 70, 80, 90]),
    SPECIAL_TIER_WAR: Object.freeze([60, 70, 80, 92]),
    SPECIAL_SLOT_LEVEL: Object.freeze([2, 4, 6, 8]),
    SPECIAL_TIER_LEVEL: Object.freeze([2, 4, 6, 8]),
    TACTIC_SLOT_INT: Object.freeze([50, 60, 70, 80, 90]),
    TACTIC_TIER_INT: Object.freeze([50, 65, 80, 92]),
    TACTIC_SLOT_LEVEL: Object.freeze([3, 5, 7, 9, 10]),
    TACTIC_TIER_LEVEL: Object.freeze([3, 5, 7, 9]),
    INNATE_TACTICS,
    VET_PER_LEVEL: 0.04,

    vetK(gen) {
      if (!Progress.enabled || !gen) return 1;
      const L = Progress.vetLevel(gen);
      return L > 1 ? 1 + Progress.VET_PER_LEVEL * (Math.min(L, Progress.MAX_LEVEL) - 1) : 1;
    },
    // 第四版（ENGAGEMENT §4.4）：老练等级只把非战斗经验（gen.dexp）算一半；没有非战斗经验时就是 level（与第三版相同）。
    // 军队会心的等级项也用它（INTEGRATION §3.2）。
    vetLevel(gen) {
      if (!gen) return 1;
      if (!(gen.dexp > 0)) return gen.level | 0 || 1;
      return Progress.levelFor(Math.max(0, gen.exp - 0.5 * gen.dexp));
    },

    levelFor(exp) {
      let L = 1;
      for (let i = 2; i <= Progress.MAX_LEVEL; i++) if (exp >= Progress.EXP[i]) L = i;
      return L;
    },
    expInfo(gen) {
      Progress.ensure(gen);
      const L = gen.level, max = L >= Progress.MAX_LEVEL;
      const cur = Progress.EXP[L], next = max ? cur : Progress.EXP[L + 1];
      return { level: L, exp: gen.exp, cur, next, need: max ? 0 : next - gen.exp, pct: max ? 1 : M.clamp01((gen.exp - cur) / Math.max(1, next - cur)), max };
    },

    // 原四计中按智力会的（与 Defs.tactics 的需求智力相同）
    innateTactics(gen) {
      const out = [];
      const T = SG.Defs.tactics;
      for (let i = 0; i < INNATE_TACTICS.length; i++) if (T[i] && (gen.intel | 0) >= T[i].reqInt) out.push(INNATE_TACTICS[i]);
      return out;
    },
    isInnateTactic(id) { return INNATE_SET.has(id); },

    // 补齐 / 规范成长字段（幂等：已规范的数据原样不变，存档往返一致）
    ensure(gen) {
      if (!gen) return gen;
      if (!(gen.exp >= 0)) gen.exp = 0;
      gen.exp = Math.min(Math.round(gen.exp), Progress.EXP[Progress.MAX_LEVEL]);
      gen.level = Progress.levelFor(gen.exp);
      const Sp = SG.Specials && typeof SG.Specials.byId === 'function' ? SG.Specials : null;
      const Tc = SG.Tactics && typeof SG.Tactics.byId === 'function' ? SG.Tactics : null;
      const sp0 = Array.isArray(gen.specials) ? gen.specials : [];
      const sp = [];
      for (const id of sp0) if (typeof id === 'string' && sp.indexOf(id) < 0 && (!Sp || Sp.byId(id))) sp.push(id);
      if (!Array.isArray(gen.specials) || sp.length !== sp0.length) gen.specials = sp;
      const t0 = Array.isArray(gen.tactics) ? gen.tactics : [];
      const tac = Progress.innateTactics(gen);
      for (const id of t0) if (typeof id === 'string' && !INNATE_SET.has(id) && tac.indexOf(id) < 0 && (!Tc || Tc.byId(id))) tac.push(id);
      if (!Array.isArray(gen.tactics) || tac.join() !== t0.join()) gen.tactics = tac;
      const r = gen.record;
      if (!r || typeof r !== 'object') gen.record = { battles: 0, wins: 0, routs: 0, duels: 0 };
      else for (const k of ['battles', 'wins', 'routs', 'duels']) if (!(r[k] >= 0)) r[k] = 0;
      return gen;
    },
    learnedTactics(gen) { Progress.ensure(gen); return gen.tactics.filter(id => !INNATE_SET.has(id)); },

    specialSlots(gen) {
      Progress.ensure(gen);
      const P = Progress, w = gen.war | 0;
      const max = countAtLeast(P.SPECIAL_SLOT_WAR, w);
      const unlocked = Math.min(max, countAtLeast(P.SPECIAL_SLOT_LEVEL, gen.level));
      const statTier = countAtLeast(P.SPECIAL_TIER_WAR, w);
      const levelTier = countAtLeast(P.SPECIAL_TIER_LEVEL, gen.level);
      const used = gen.specials.length;
      const tier = Math.min(statTier, levelTier, used + 1);
      return { max, unlocked, used, open: Math.max(0, unlocked - used), tier, statTier, levelTier };
    },
    tacticSlots(gen) {
      Progress.ensure(gen);
      const P = Progress, v = gen.intel | 0;
      const max = countAtLeast(P.TACTIC_SLOT_INT, v);
      const unlocked = Math.min(max, countAtLeast(P.TACTIC_SLOT_LEVEL, gen.level));
      const statTier = countAtLeast(P.TACTIC_TIER_INT, v);
      const levelTier = countAtLeast(P.TACTIC_TIER_LEVEL, gen.level);
      const used = Progress.learnedTactics(gen).length;
      const tier = Math.min(statTier, levelTier, used + 1);
      return { max, unlocked, used, open: Math.max(0, unlocked - used), tier, statTier, levelTier };
    },
    // 计策格只在载入了 js/tactics-data.js（有可学的计策）时计入，否则 total 会停在 > 0 而 candidates() 为空
    openings(gen) {
      const T = SG.Tactics;
      const s = Progress.specialSlots(gen).open, t = T && typeof T.learnable === 'function' ? Progress.tacticSlots(gen).open : 0;
      return { special: s, tactic: t, total: s + t };
    },

    // 可学的条目（未学、等阶不超过上限）
    eligible(gen, type) {
      Progress.ensure(gen);
      if (type === 'special') {
        const S = SG.Specials;
        if (!S || typeof S.pool !== 'function') return [];
        const sl = Progress.specialSlots(gen);
        if (sl.open <= 0) return [];
        // 可习得的暗杀每人至多一招（得手即整支溃散，多招叠加时主将被刺、一战定局的机会过大）
        const hasAssn = gen.specials.some(id => { const k = S.byId(id); return k && k.kind === 'assassinate'; });
        return S.pool().filter(sp => sp.tier <= sl.tier && gen.specials.indexOf(sp.id) < 0 && !(hasAssn && sp.kind === 'assassinate'));
      }
      const T = SG.Tactics;
      if (!T || typeof T.learnable !== 'function') return [];
      const sl = Progress.tacticSlots(gen);
      if (sl.open <= 0) return [];
      return T.learnable().filter(t => t.tier <= sl.tier && gen.tactics.indexOf(t.id) < 0 && (gen.intel | 0) >= (t.reqInt | 0));
    },
    cand(type, x) {
      const S = SG.Specials, T = SG.Tactics;
      if (type === 'special') return { type, id: x.id, tier: x.tier, name: x.name, desc: x.desc, rules: S && S.rules ? S.rules(x) : '', color: x.color, fx: x.fx, ref: x };
      return { type, id: x.id, tier: x.tier, name: x.name, desc: x.desc, rules: T && T.rules ? T.rules(x) : '', color: x.color, fx: x.fx, ref: x };
    },
    candidates(gen, n) {
      n = n > 0 ? n : 3;
      Progress.ensure(gen);
      const seed = SG.G && SG.G.seed ? SG.G.seed : 0;
      const r = SG.SeededRandom(phash(gen.name + '|' + gen.level + '|' + gen.specials.join(',') + '|' + gen.tactics.join(',') + '|' + seed));
      const pools = [];
      for (const type of ['special', 'tactic']) {
        const list = Progress.eligible(gen, type);
        if (!list.length) continue;
        const cap = type === 'special' ? Progress.specialSlots(gen).tier : Progress.tacticSlots(gen).tier;
        pools.push({ type, list: list.slice(), cap });
      }
      if (!pools.length) return [];
      // 加权不放回抽取：本等阶上限的条目 ×4、低一阶 ×2、其余 ×1；每类的第一个候选必为本等阶上限的条目
      // （新解锁的等阶一定出现在候选里，电脑 aiPick 也就总能学到当前最高阶）
      const draw = p => {
        let tot = 0;
        const capOnly = !p.drawn && p.list.some(x => x.tier >= p.cap);
        p.drawn = true;
        const w = p.list.map(x => { const k = x.tier >= p.cap ? 4 : capOnly ? 0 : x.tier === p.cap - 1 ? 2 : 1; tot += k; return k; });
        let roll = r.nextDouble() * tot;
        for (let i = 0; i < p.list.length; i++) { roll -= w[i]; if ((roll < 0 && w[i] > 0) || i === p.list.length - 1) return Progress.cand(p.type, p.list.splice(i, 1)[0]); }
        return null;
      };
      const out = [];
      // 两类都有空格时交替抽取（武力 / 智力相对更高的一类先抽），保证两类都出现
      if (pools.length === 2 && (gen.intel | 0) > (gen.war | 0)) pools.reverse();
      for (let guard = 0; out.length < n && guard < 32; guard++) {
        const live = pools.filter(p => p.list.length);
        if (!live.length) break;
        const c = draw(live[out.length % live.length]);
        if (c) out.push(c);
      }
      return out;
    },
    learn(gen, a, b) {
      const type = typeof a === 'string' ? a : a && a.type;
      const id = typeof a === 'string' ? b : a && a.id;
      if (!gen || !id || (type !== 'special' && type !== 'tactic')) return false;
      if (!Progress.eligible(gen, type).some(x => x.id === id)) return false;
      if (type === 'special') gen.specials.push(id); else gen.tactics.push(id);
      return true;
    },
    // 电脑的选择：高阶优先；同阶按武将所长（武力系伤害 / 智力系 / 辅助）与姓名哈希
    aiPick(gen, cands) {
      if (!cands || !cands.length) return null;
      const W = gen.war | 0, I = gen.intel | 0, Pl = gen.pol | 0;
      const SUPPORT = { rally: 1, command: 1, fortify: 1, haste: 1, heal: 1 };
      let best = null, bs = -1e9;
      for (const c of cands) {
        let s = c.tier * 10;
        const k = c.ref && c.ref.kind;
        if (c.type === 'special') s += SUPPORT[k] ? (Pl >= W - 5 ? 3 : 0) : (W >= Pl ? 3 : 0);
        else s += c.ref && c.ref.dmg ? (I >= 80 ? 3 : 1) : 2;
        s += (phash(gen.name + '|' + c.id) % 100) / 100;
        if (s > bs) { bs = s; best = c; }
      }
      return best;
    },
    autoLearn(gen) {
      const out = [];
      for (let guard = 0; guard < 12 && Progress.openings(gen).total > 0; guard++) {
        const c = Progress.aiPick(gen, Progress.candidates(gen, 3));
        if (!c || !Progress.learn(gen, c)) break;
        out.push(c);
      }
      return out;
    },
    isPlayerGen(gen) { const g = SG.G; return !!(g && g.player >= 0 && gen && gen.faction === g.player); },
    pending(f) {
      const g = SG.G;
      if (!g) return [];
      return g.generals.filter(x => x.faction === f && !x.dead && Progress.openings(x).total > 0 && Progress.candidates(x, 1).length > 0);
    },
    known(gen) {
      const S = SG.Specials, T = SG.Tactics;
      return {
        specials: S && typeof S.known === 'function' ? S.known(gen) : [],
        tactics: T && typeof T.known === 'function' ? T.known(gen) : [],
      };
    },

    // 第四版：why = 'battle' | 'tourney' | 'story' 先乘经验倍率 SG.Mods.exp（ITEMS / INTEGRATION §3.4）；
    //   'domestic'（常规内政经验）由 ENGAGEMENT 在每月上限 20 之前自己乘，这里不再乘。
    //   why ≠ 'battle' 的经验记入 gen.dexp（老练只算一半，vetLevel）。
    EXP_MUL_WHY: Object.freeze({ battle: 1, tourney: 1, story: 1 }),
    gain(gen, amount, why) {
      if (!Progress.enabled || !gen || !(amount > 0)) return null;
      Progress.ensure(gen);
      const from = gen.level;
      if (SG.Mods && Progress.EXP_MUL_WHY[why || 'battle']) amount = SG.Mods.exp(gen, amount);
      const add = Math.round(amount);
      const e0 = gen.exp;
      gen.exp = Math.min(gen.exp + add, Progress.EXP[Progress.MAX_LEVEL]);
      if (why && why !== 'battle' && gen.exp > e0) gen.dexp = (gen.dexp || 0) + (gen.exp - e0);
      gen.level = Progress.levelFor(gen.exp);
      const res = { gen, exp: add, why: why || '', from, to: gen.level, learned: [] };
      const player = Progress.isPlayerGen(gen);
      if (!player || Progress.playerAutoLearn) res.learned = Progress.autoLearn(gen);
      if (player && gen.level > from && SG.G && typeof SG.G.addLog === 'function') {
        SG.G.addLog(gen.name + '升至 ' + gen.level + ' 级' + (res.learned.length ? '，习得' + res.learned.map(c => '「' + c.name + '」').join('') : '') + '。');
      }
      return res;
    },
    battleExp(x) {
      let e = 10;
      e += (x.dmg || 0) / 40 + (x.heal || 0) / 80;
      e += (x.routs || 0) * 12 + (x.cmdRouts || 0) * 12;
      e += (x.duelWin || 0) * 25 + (x.duelLoss || 0) * 5;
      e += (x.specials || 0) * 6 + (x.tactics || 0) * 4 + (x.tacFail || 0);
      e += (x.crits || 0) * 2;   // 第四版：每次军队会心 +2（COMBAT-V4 §2.1）
      if (x.won) e += 25 + (x.commander ? 10 : 0);
      return Math.round(Math.min(Progress.BATTLE_CAP, e));
    },
    // 战绩（战术战斗与自动结算共用）
    note(gen, won, routs, duels) {
      Progress.ensure(gen);
      const r = gen.record;
      r.battles++;
      if (won) r.wins++;
      r.routs += routs | 0;
      r.duels += duels | 0;
    },

    // ---- 自动结算的攻城战 ----
    snapshot(s) {
      const m = new Map();
      for (const x of (s.atk || []).concat(s.def || [])) m.set(x.id, { troops: Math.max(0, x.troops | 0), power: genPower(x) });
      return m;
    },
    autoBattle(s, snap) {
      if (!Progress.enabled || !s || s.progressDone || !snap) return [];
      s.progressDone = true;
      const out = s.progress = [];          // 与战术战斗的 setup.progress 同格式（界面战后统一读取）
      const sides = [s.atk || [], s.def || []];
      // 无人防守，不战而下：攻方只得少量经验
      if (sides[1].length === 0 || sides[1].every(d => !(snap.get(d.id) && snap.get(d.id).troops > 0))) {
        for (const a of sides[0]) { const r = Progress.gain(a, 5, 'battle'); if (r) out.push(Object.assign(r, { side: 0, won: true })); }
        return out;
      }
      for (let k = 0; k < 2; k++) {
        const mine = sides[k], foes = sides[1 - k];
        const won = k === 0 ? !!s.attackerWon : !s.attackerWon;
        let loss = 0, routs = 0, cmd = 0;
        foes.forEach((e, i) => {
          const b = snap.get(e.id);
          if (!b) return;
          loss += Math.max(0, b.troops - Math.max(0, e.troops));
          if (b.troops > 0 && (s.routed.has(e.id) || e.troops <= 0)) { routs++; if (i === 0) cmd++; }
        });
        let tot = 0;
        for (const a of mine) tot += snap.get(a.id) ? snap.get(a.id).power : 0;
        mine.forEach((a, i) => {
          const b = snap.get(a.id);
          if (!b || !(b.troops > 0)) return;
          const share = tot > 0 ? b.power / tot : 1 / mine.length;
          const e = Progress.battleExp({ dmg: loss * share, routs: routs * share, cmdRouts: cmd * share, won, commander: i === 0 });
          Progress.note(a, won, Math.round(routs * share), 0);
          const from = a.level;
          const r = Progress.gain(a, e, 'battle');
          if (r) out.push(Object.assign(r, { side: k, won }));
          else out.push({ gen: a, exp: 0, from, to: from, learned: [], side: k, won });
        });
      }
      return out;
    },
    hookConquest(C) {
      if (!C || C._progressHook || typeof C.autoResolve !== 'function') return false;
      const orig = C.autoResolve;
      C.autoResolve = function (s) {
        let snap = null;
        try { snap = Progress.snapshot(s); } catch (e) { console.error(e); }
        const r = orig.apply(this, arguments);
        try { Progress.autoBattle(s, snap); } catch (e) { console.error(e); }
        return r;
      };
      C._progressHook = true;
      return true;
    },
  };

  SG.GameState = GameState;
  SG.DevKind = DevKind;
  SG.Commands = Commands;
  SG.Progress = Progress;
  if (!('G' in SG)) SG.G = null;
})();
