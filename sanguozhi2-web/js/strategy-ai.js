'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 攻城结算与电脑势力
   移植自 Strategy/Conquest.cs 与 Strategy/StrategyAI.cs。
   第四版（INTEGRATION §6.3；W0 整合核心）：
     StrategyAI.endMonth()：载入了 js/econ.js（SG.Econ）时走第四版的月末流程 endMonthV4，否则走原样保留的
       endMonthV3（v3 的 Node 测试与 design-v4 的设计原型以文字钩子改写其中几行：endMonthV3、power()、attackNeed()
       的文字请勿改动）。所有第四版开关关闭（没有提供者、没有钩子）时两者结果逐位相同（tests/v4-all.js 断言）。
     月末各相（INTEGRATION §6.3；ctx 见下）：
       0 snapshot      各势力本月总收入 ctx.gross[f]（钱袋用）；羁绊防护快照（ENGAGEMENT）
       1 income        城收入 SG.Econ.cityGold、7 月秋收 SG.Econ.harvest（rollRaw 记入 ctx.harvests）、俸禄、军粮、
                       金 < 0 规则、人口（× SG.Mods.popK）—— 之后调用 'income' 钩子
       2 items         ITEMS 月末（补货、每月装备效果）
       3 engage        ENGAGEMENT 月末（民心、营建、商队、探索、任务、事件、贡金、文会 …）
       4 injuries → 出奔（v3 规则，忠诚用 SG.Mods.effLoy：官爵与羁绊防护取较大者；德用 SG.Mods.virtue）→ 'defections'
       5 loyAura       忠诚光环（官职与宝物取较大者，≤ 95）
         —— 武将 moved 复位，月份 +1（以下各相 g.month 为新月；ctx.ended 为刚结束的月）——
       6 titles        Titles.monthStart
       7 discretionary 每个电脑势力：SG.AIBudget.accrue(f, ctx.gross[f]) → onSpend 的 title → gear → project → festival
       8 tourney       11 → 12 月公告；正月武林大会
       9 news          syncFV、名次、新闻汇总（ctx.stageUps …）
     StrategyAI.on(phase, fn(ctx), { owner, when, order }) → off()        StrategyAI.onSpend(kind, fn(f, ctx), opts) → off()
     ctx = { g, news, ended: { year, month, monthIndex }, next: { … }（第 6 相起）, gross: [按势力], harvests: [], stageUps: [], healed: [武将编号],
             cityGold: [], defections: [] }
     endMonth 的返回值仍是新闻字符串数组，另带 news.harvests（DOMESTIC-FX §9.1：{ city, land, harvest, roll, rollRaw, pct, mods, tier }）、
       news.cityGold（玩家各城的收入记录）、news.stageUps、news.ctx。
     电脑（runAI）与攻城的钩子：提供者 noAttack / cityProtected（不打、不算威胁）、needK（攻城所需兵力比的加项）、
       aiTargetK（目标排序：守军 × (1 + k)）、aiSearchP（电脑搜索概率，缺省 0.15）、autoPower（自动结算战力）；
       事件 conquest.prepare / conquest.resolved / conquest.apply / captive.taken / captive.released / captive.executed /
       faction.succession（js/registry.js 的 SG.Events）。出征也写 Commands.lastResult（cmd 'attack'）。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;
  const Balance = SG.Balance;
  // 当前剧本（经典 / 世界，运行时可换）
  const ScenarioData = new Proxy({}, { get: (t, k) => SG.ScenarioData[k] });
  const Seq = SG.Seq;
  const Commands = SG.Commands;
  const DevKind = SG.DevKind;

  function G() { return SG.G; }

  // 一场攻城战的参与者与结果（C# BattleSetup）
  function newSetup(o) {
    return Object.assign({
      attacker: -1, defender: -1,   // 势力（defender 可为 -1 空城）
      src: null, target: null,
      atk: [], def: [],
      atkFood: 0, atkGold: 0,
      attackerWon: false,
      routed: new Set(),           // 兵力耗尽或单挑落败的武将
      captives: [],
      summary: '',
    }, o);
  }

  // ================================================================ Conquest --
  const Conquest = {
    // 本场战斗中被俘、尚待处置的武将（C# 声明了此集合却从未写入，致使 IsCaptive 恒为假；
    // 此处按其本意在 capture() 中登记，于释放/处斩/处置完毕时移除，并在每场结算开始时清空）
    pendingCaptives: new Set(),

    prepare(attacker, src, target, atk, food, gold) {
      const g = G();
      const s = newSetup({ attacker, defender: target.owner, src, target, atkFood: food, atkGold: gold, atk: [], def: [], routed: new Set(), captives: [] });
      for (const x of atk) s.atk.push(x);
      src.food -= food; src.gold -= gold;
      for (const x of atk) x.moved = true;
      s.def = pickDefenders(target);
      // 第四版：出征的结果记录（DOMESTIC-FX §3.2）与事件
      if (Commands) Commands.lastResult = { cmd: 'attack', ok: true, city: src.id, gen: atk.length ? atk[0].id : null, month: g.monthIndex, f: attacker,
        gens: atk.length, troops: Seq.sum(atk, x => x.troops), ruler: atk.some(x => g.isRuler(x)), to: target.id, food, gold, tier: null };
      emit('conquest.prepare', s);
      return s;
    },

    // 电脑之间（或委任）快速结算
    autoResolve(s) {
      const g = G();
      if (s.def.length === 0 || s.def.every(d => d.troops <= 0)) { s.attackerWon = true; s.summary = s.target.name + '无人防守，不战而下。'; emit('conquest.resolved', s); return; }
      const defK = 1 + SG.cityDefense(s.target) / 160;
      let pa = Seq.sum(s.atk, powerOf), pd = Seq.sum(s.def, powerOf) * defK;
      for (let round = 0; round < 12; round++) {
        const ra = pa / Math.max(1, pa + pd);
        for (const d of s.def) {
          if (!(d.troops > 0)) continue;
          d.troops = Math.max(0, d.troops - M.roundToInt(d.troops * 0.16 * ra * 2 * SG.Random.rangeFloat(0.7, 1.3)));
        }
        for (const a of s.atk) {
          if (!(a.troops > 0)) continue;
          a.troops = Math.max(0, a.troops - M.roundToInt(a.troops * 0.16 * (1 - ra) * 2 * SG.Random.rangeFloat(0.7, 1.3)));
        }
        for (const x of s.atk.concat(s.def)) if (x.troops < 80) { x.troops = 0; s.routed.add(x.id); }
        pa = Seq.sum(s.atk, powerOf); pd = Seq.sum(s.def, powerOf) * (1 + SG.cityDefense(s.target) / 160);
        if (pa <= 1 || pd <= 1) break;
      }
      s.attackerWon = pd < pa * 0.6 || s.def.every(d => d.troops <= 0);
      s.summary = `${g.factions[s.attacker].name}军${s.attackerWon ? '攻陷了' : '未能攻下'}${s.target.name}！`;
      emit('conquest.resolved', s);
    },

    // 结算归属、撤退与俘虏
    apply(s) {
      const g = G();
      // 防御：守方以城池当前归属为准（排队的进攻若遇城池易主，守军撤退须撤往真正守方的城）
      if (s.target.owner !== s.defender) s.defender = s.target.owner;
      const A = s.attacker; const D = s.defender;
      Conquest.pendingCaptives.clear();
      if (s.attackerWon) {
        // 守军撤退或被俘
        const retreatTo = D >= 0 ? Seq.first(s.target.links.map(i => g.cities[i]), c => c.owner === D) : null;
        for (const d of g.officersIn(s.target)) {
          const routed = s.routed.has(d.id) || d.troops <= 0;
          if (retreatTo != null && (!routed || SG.Random.value() < 0.5)) { d.city = retreatTo.id; }
          else if (routed || retreatTo == null) { if (SG.Random.value() < 0.75 || retreatTo == null) capture(s, d); else d.city = retreatTo.id; }
        }
        s.target.owner = A;
        s.target.gold = M.idiv(s.target.gold, 2) + s.atkGold;
        s.target.food = M.idiv(s.target.food, 2) + s.atkFood;
        for (const a of s.atk) if (!s.routed.has(a.id) || a.troops > 0) a.city = s.target.id;
        for (const a of s.atk) if (s.routed.has(a.id) && a.troops <= 0) a.city = s.src.id;
        s.target.governor = -1;
        g.autoGovernor(s.target);
        if (retreatTo != null) g.autoGovernor(retreatTo);
        g.autoGovernor(s.src);
        g.addLog(`${g.factions[A].name}攻陷${s.target.name}。`);
        if (D >= 0) { Conquest.handleRulerLoss(D); g.checkFactionDeath(D); }
      } else {
        for (const a of s.atk) {
          if (s.routed.has(a.id) && SG.Random.value() < 0.35 && D >= 0) capture(s, a);
          else a.city = s.src.id;
        }
        s.src.food += s.atkFood; s.src.gold += s.atkGold;
        g.autoGovernor(s.src);
        if (D >= 0) g.addLog(`${g.factions[D].name}击退了${g.factions[A].name}的进攻。`);
        Conquest.handleRulerLoss(A);
      }
      emit('conquest.apply', s, { attacker: A, defender: D, won: !!s.attackerWon });
    },

    // 君主被俘或身亡后的继承
    handleRulerLoss(f) {
      const g = G();
      if (f < 0 || !g.factions[f].alive) return;
      const r = g.ruler(f);
      const lost = r.dead || r.faction !== f || g.cities[r.city].owner !== f;
      if (!lost) return;
      if (!r.dead && r.faction === f) {
        // 君主仍在，但所在城已失：转移到己方城池（世界剧本取路程最近的一座，免得君主一步跳到另一片大陆；经典剧本保持原样）
        const c = g.scenario === 'world' ? nearestOwnCity(f, r.city) : Seq.first(g.citiesOf(f));
        if (c != null && !Conquest.isCaptive(r)) { r.city = c.id; g.autoGovernor(c); return; }
      }
    },
    isCaptive(gen) { return Conquest.pendingCaptives.has(gen.id); },
    // 俘虏被俘时的身份 → { faction, wasRuler }（无记录时按现状）
    captiveInfo(s, c) {
      const g = G();
      const i = s && s.captiveInfo && s.captiveInfo[c.id];
      if (i) return i;
      return { faction: c.faction, wasRuler: c.faction >= 0 && g.factions[c.faction].ruler === c.id };
    },

    succession(f) {
      const g = G();
      if (f < 0 || !g.factions[f].alive) return;
      const cands = g.generalsOf(f).filter(x => g.cities[x.city].owner === f);
      const heir = cands.length ? Seq.orderByDesc(cands, x => x.war + x.intel + x.pol)[0] : null;
      if (heir == null) {
        for (const c of g.citiesOf(f)) { c.owner = -1; c.governor = -1; }
        g.checkFactionDeath(f);
        emit('faction.succession', f, null);
        return;
      }
      g.factions[f].ruler = heir.id;
      heir.loyalty = 100;
      g.addLog(heir.name + '继承了' + g.factions[f].name + '的基业。');
      emit('faction.succession', f, heir);
    },

    // 俘虏处置（AI）：先试图登用，否则释放为在野。
    // → [{ gen, from: 原势力, fate: 'hired' | 'released' | 'executed' }]（供画面告知玩家其被俘武将的下落）
    aiDecideCaptives(s, winner) {
      const g = G();
      const recruiter = g.ruler(winner);
      const out = [];
      for (const c of s.captives) {
        const info = Conquest.captiveInfo(s, c);
        const oldF = c.faction >= 0 ? c.faction : info.faction;
        const wasRuler = info.wasRuler;
        if (wasRuler) {
          if (SG.Random.value() < 0.5) { Conquest.execute(c); out.push({ gen: c, from: oldF, fate: 'executed' }); }
          else { Conquest.release(c); out.push({ gen: c, from: oldF, fate: 'released' }); }
          continue;
        }
        if (Commands.hire(c, recruiter, winner, s.target.id)) out.push({ gen: c, from: oldF, fate: 'hired' });
        else { Conquest.release(c); out.push({ gen: c, from: oldF, fate: 'released' }); }
      }
      for (const c of s.captives) Conquest.pendingCaptives.delete(c.id);
      return out;
    },

    release(c) {
      const g = G();
      Conquest.pendingCaptives.delete(c.id);
      const oldF = c.faction;
      const wasRuler = oldF >= 0 && g.factions[oldF].ruler === c.id;
      const home = oldF >= 0 ? Seq.first(g.citiesOf(oldF)) : null;
      if (home != null) { c.city = home.id; g.autoGovernor(home); }
      else { c.faction = -1; c.loyalty = 0; }
      if (wasRuler && home == null) g.checkFactionDeath(oldF);
      emit('captive.released', c);
    },

    execute(c) {
      const g = G();
      Conquest.pendingCaptives.delete(c.id);
      const oldF = c.faction;
      const wasRuler = oldF >= 0 && g.factions[oldF].ruler === c.id;
      c.dead = true; c.troops = 0; c.faction = -1;
      g.addLog(c.name + '被处斩。');
      if (wasRuler) Conquest.succession(oldF);
      // 处斩者不得仍挂太守之职；新君主坐镇其所在之城
      for (const city of g.cities) if (city.governor === c.id) g.autoGovernor(city);
      if (wasRuler && g.factions[oldF].alive) g.autoGovernor(g.cities[g.ruler(oldF).city]);
      emit('captive.executed', c, { faction: oldF, wasRuler });
    },
  };

  // 按连线路程（BFS）离 fromId 最近的势力 f 城池；无则 null
  function nearestOwnCity(f, fromId) {
    const g = G();
    const seen = new Set([fromId]); let q = [fromId];
    while (q.length) {
      const next = [];
      for (const id of q) for (const j of g.cities[id].links) {
        if (seen.has(j)) continue;
        seen.add(j);
        if (g.cities[j].owner === f) return g.cities[j];
        next.push(j);
      }
      q = next;
    }
    return Seq.first(g.citiesOf(f));
  }

  // 守城出战的武将：君主优先，其余按兵力，至多 MaxSortieGenerals 名
  function pickDefenders(target) {
    const g = G();
    if (target.owner < 0) return [];
    return Seq.orderByDesc(g.officersIn(target), x => (g.isRuler(x) ? 9999 : x.troops)).slice(0, Balance.MaxSortieGenerals);
  }

  function power(gen) { return gen.troops * (0.55 + gen.war / 220) * (0.7 + gen.training / 330); }
  // 第四版：自动结算的战力可由提供者 autoPower 改写（ITEMS 的装备；缺省 = 上面 v3 的 power）
  function powerOf(gen) { const Mo = SG.Mods; if (Mo) { const v = Mo.autoPower(gen); if (v !== undefined) return v; } return power(gen); }
  function emit(name, a, b, c) { const E = SG.Events; if (E) E.emit(name, a, b, c); }
  // 第四版的电脑钩子（无提供者时：false / 0 / 0.15，与 v3 相同）
  function noAttack(o, f) { return o >= 0 && !!SG.Mods && SG.Mods.noAttack(o, f); }
  function cityProtected(c, f) { return !!SG.Mods && SG.Mods.cityProtected(c, f); }
  function needK(f, t) { return SG.Mods ? SG.Mods.needK(f, t) : 0; }
  function aiTargetK(f, t) { return SG.Mods ? SG.Mods.aiTargetK(f, t) : 0; }
  function aiSearchP(c, f) { return SG.Mods ? SG.Mods.aiSearchP(c, f) : 0.15; }

  // 被俘时记下原势力与是否君主（s.captiveInfo[id] = { faction, wasRuler }）：攻下守方最后一城时 apply 随即
  // checkFactionDeath，俘虏的 faction 已成 −1，处置俘虏时仍须知道「君主不会投降」与原属势力
  function capture(s, gen) {
    const g = G();
    if (!s.captiveInfo) s.captiveInfo = {};
    if (!s.captiveInfo[gen.id]) s.captiveInfo[gen.id] = { faction: gen.faction, wasRuler: gen.faction >= 0 && g.factions[gen.faction].ruler === gen.id };
    gen.troops = 0;
    s.captives.push(gen);
    gen.city = s.target.id;
    Conquest.pendingCaptives.add(gen.id);
    emit('captive.taken', s, gen);
  }

  // ============================================================== 第四版月末钩子 --
  const phaseHooks = {};
  for (const k of ['snapshot', 'income', 'items', 'engage', 'injuries', 'defections', 'loyAura', 'titles', 'discretionary', 'tourney', 'news']) phaseHooks[k] = [];
  const SPEND_KINDS = ['title', 'gear', 'project', 'festival'];
  const spendHooks = { title: [], gear: [], project: [], festival: [] };
  function addHook(list, fn, opts) {
    if (typeof fn !== 'function') throw new Error('StrategyAI hook must be a function');
    opts = opts || {};
    const h = { fn, owner: opts.owner || '', when: typeof opts.when === 'function' ? opts.when : null, order: typeof opts.order === 'number' ? opts.order : 100, seq: list.length };
    list.push(h);
    list.sort((a, b) => a.order - b.order || a.seq - b.seq);
    return () => { const i = list.indexOf(h); if (i >= 0) list.splice(i, 1); };
  }
  function activeHook(h) { if (!h.when) return true; try { return !!h.when(); } catch (e) { return false; } }
  function runPhase(name, ctx) {
    emit('month.phase', name, ctx);
    for (const h of phaseHooks[name].slice()) {
      if (!activeHook(h)) continue;
      try { h.fn(ctx); } catch (e) { console.error('StrategyAI phase ' + name + ' ' + (h.owner || ''), e); }
    }
  }

  // ============================================================== StrategyAI --
  // 月末结算与电脑势力的行动
  const StrategyAI = {
    // ---------------------------------------------------------- 月末 --
    // 第四版：载入了 js/econ.js 时走 INTEGRATION §6.3 的月末流程，否则走第三版（见文件头）
    endMonth() { return SG.Econ ? StrategyAI.endMonthV4() : StrategyAI.endMonthV3(); },

    // 第三版的月末，原样保留（design-v4 的设计原型以文字钩子改写其中几行，勿改动）
    endMonthV3() {
      const g = G();
      const news = [];
      for (const c of g.cities) {
        if (c.owner < 0) { c.population += M.roundToInt(c.population * 0.001); continue; }
        const offs = g.officersIn(c);
        c.gold += M.roundToInt(c.industry * Balance.GoldPerIndustry + c.town * Balance.GoldPerTown);
        c.gold -= offs.length * Balance.SalaryPerGeneral;
        const troops = Seq.sum(offs, o => o.troops);
        c.food -= M.idiv(troops, Balance.TroopsPerFood);
        if (g.month === Balance.HarvestMonth) {
          const harvest = M.roundToInt(c.land * Balance.FoodPerLand * SG.Random.rangeFloat(0.85, 1.15));
          c.food += harvest;
          if (c.owner === g.player) news.push(`${c.name}秋收，得粮 ${harvest}。`);
        }
        if (c.food < 0) {
          c.food = 0;
          for (const o of offs) o.troops = M.idiv(o.troops * 9, 10);
          if (c.owner === g.player) news.push(c.name + '粮草断绝，士兵逃亡！');
        }
        if (c.gold < 0) {
          c.gold = 0;
          for (const o of offs) if (!g.isRuler(o)) o.loyalty = Math.max(0, o.loyalty - 3);
        }
        c.population = Math.min(2000000, c.population + M.roundToInt(c.population * c.town * Balance.PopGrowthPerTown / 100));
      }
      // 低忠诚武将出奔
      for (const gen of g.generals.filter(x => x.faction >= 0 && !x.dead)) {
        // （C# 中若本循环内某势力灭亡，其余武将 faction 已为 -1，此处跳过以免越界）
        if (gen.faction < 0) continue;
        if (g.isRuler(gen) || gen.loyalty >= 40) continue;
        const p = (40 - gen.loyalty) / 400 * (1.2 - g.factions[gen.faction].virtue / 100);
        if (SG.Random.value() < p) {
          const c = g.cities[gen.city];
          const f = gen.faction;
          gen.faction = -1; gen.troops = 0; gen.loyalty = 0;
          g.autoGovernor(c);
          if (f === g.player) news.push(gen.name + '不满待遇，弃官而去！');
          g.addLog(gen.name + '离开了' + g.factions[f].name + '。');
          if (g.officersIn(c).length === 0) { c.owner = -1; g.checkFactionDeath(f); }
        }
      }
      for (const gen of g.generals) gen.moved = false;
      g.month++;
      if (g.month > 12) { g.month = 1; g.year++; }
      g.tokens = g.tokensFor(g.player);
      return news;
    },

    // ---------------------------------------------------------- 第四版月末流程（INTEGRATION §6.3）--
    PHASES: Object.freeze(['snapshot', 'income', 'items', 'engage', 'injuries', 'defections', 'loyAura', 'titles', 'discretionary', 'tourney', 'news']),
    // 某一相的钩子：fn(ctx)；opts = { owner, when: () => bool（假则跳过）, order（同相内的先后，缺省 100）}
    on(phase, fn, opts) {
      if (!phaseHooks[phase]) throw new Error('StrategyAI.on: unknown phase ' + phase + ' (' + StrategyAI.PHASES.join(', ') + ')');
      return addHook(phaseHooks[phase], fn, opts);
    },
    // 自由支出（第 7 相）：fn(f, ctx)，kind ∈ title / gear / project / festival（固定次序，INTEGRATION §5.1）
    onSpend(kind, fn, opts) {
      if (!spendHooks[kind]) throw new Error('StrategyAI.onSpend: unknown kind ' + kind + ' (title, gear, project, festival)');
      return addHook(spendHooks[kind], fn, opts);
    },
    hooks() {
      const o = {};
      for (const k of Object.keys(phaseHooks)) o[k] = phaseHooks[k].map(h => h.owner || '?');
      for (const k of Object.keys(spendHooks)) o['spend:' + k] = spendHooks[k].map(h => h.owner || '?');
      return o;
    },
    stats: { defections: 0, months: 0 },
    endMonthV4() {
      const g = G();
      const news = [];
      const ctx = {
        g, news,
        ended: { year: g.year, month: g.month, monthIndex: g.monthIndex },
        next: null,
        gross: [], harvests: [], stageUps: [], cityGold: [], defections: [], healed: [],
      };
      // 0 各势力本月总收入（乘成之前，钱袋按它的 22 % 计）
      for (const f of g.factions) ctx.gross[f.id] = 0;
      for (const c of g.cities) if (c.owner >= 0) ctx.gross[c.owner] += c.industry * Balance.GoldPerIndustry + c.town * Balance.GoldPerTown;
      runPhase('snapshot', ctx);
      // 1 收入、秋收、俸禄、军粮、金 < 0、人口
      StrategyAI.phaseIncome(g, ctx);
      runPhase('income', ctx);
      // 2 ITEMS · 3 ENGAGEMENT
      runPhase('items', ctx);
      runPhase('engage', ctx);
      // 4 伤病与外出到期（各模块）→ 出奔
      StrategyAI.phaseInjuries(g, ctx, 'list');
      runPhase('injuries', ctx);
      StrategyAI.phaseInjuries(g, ctx, 'clear');
      StrategyAI.phaseDefections(g, ctx);
      runPhase('defections', ctx);
      // 5 忠诚光环
      StrategyAI.phaseLoyAura(g, ctx);
      runPhase('loyAura', ctx);
      // —— 新的一月 ——
      for (const gen of g.generals) gen.moved = false;
      g.month++;
      if (g.month > 12) { g.month = 1; g.year++; }
      ctx.next = { year: g.year, month: g.month, monthIndex: g.monthIndex };
      // 6 官制月初 · 7 自由支出 · 8 武林大会 · 9 新闻
      runPhase('titles', ctx);
      StrategyAI.phaseDiscretionary(g, ctx);
      runPhase('discretionary', ctx);
      runPhase('tourney', ctx);
      runPhase('news', ctx);
      g.tokens = g.tokensFor(g.player);
      StrategyAI.stats.months++;
      if (SG.Clock) { SG.Clock.monthStart(g); SG.Clock.setPhase('news'); }
      news.harvests = ctx.harvests; news.cityGold = ctx.cityGold; news.stageUps = ctx.stageUps; news.ctx = ctx;
      return news;
    },
    phaseIncome(g, ctx) {
      const E = SG.Econ, Mo = SG.Mods, news = ctx.news;
      for (const c of g.cities) {
        if (c.owner < 0) { c.population += M.roundToInt(c.population * 0.001); continue; }
        const offs = g.officersIn(c);
        const gi = E.cityGold(c);
        c.gold += gi.gold;
        if (c.owner === g.player) ctx.cityGold.push({ city: c.id, gold: gi.gold, b: gi.b, pct: gi.pct, ev: gi.ev, mods: gi.mods });
        c.gold -= offs.length * Balance.SalaryPerGeneral;
        const troops = Seq.sum(offs, o => o.troops);
        c.food -= M.idiv(troops, Balance.TroopsPerFood);
        if (g.month === Balance.HarvestMonth) {
          const rollRaw = SG.Random.rangeFloat(0.85, 1.15);
          const hr = E.harvest(c, rollRaw);
          c.food += hr.h;
          if (c.owner === g.player) {
            news.push(`${c.name}秋收，得粮 ${hr.h}。`);
            ctx.harvests.push({ city: c.id, land: c.land, harvest: hr.h, roll: c.land > 0 ? hr.h / (c.land * Balance.FoodPerLand) : 0, rollRaw, pct: hr.pct, mods: hr.mods,
              tier: E.tierHarvest({ rollRaw, land: c.land }) });
          }
        }
        if (c.food < 0) {
          c.food = 0;
          for (const o of offs) o.troops = M.idiv(o.troops * 9, 10);
          if (c.owner === g.player) news.push(c.name + '粮草断绝，士兵逃亡！');
        }
        if (c.gold < 0) {
          c.gold = 0;
          for (const o of offs) if (!g.isRuler(o)) o.loyalty = Math.max(0, o.loyalty - 3);
        }
        c.population = Math.min(2000000, c.population + M.roundToInt(c.population * c.town * Balance.PopGrowthPerTown / 100 * Mo.popK(c)));
      }
    },
    // 伤病到期（INTEGRATION §6.4 的统一记录 gen.injury = { until, war, away }）：先列出下个月不再受伤的武将（ctx.healed，
    // 供 'injuries' 钩子与新闻用），钩子之后删掉这些过期的记录。gen.away（商队、探索……）由 ENGAGEMENT 自己结算。
    phaseInjuries(g, ctx, step) {
      const next = ctx.ended.monthIndex + 1;
      if (step === 'list') { for (const gen of g.generals) if (gen.injury && gen.injury.until <= next) ctx.healed.push(gen.id); return; }
      for (const id of ctx.healed) { const gen = g.generals[id]; if (gen && gen.injury && gen.injury.until <= next) delete gen.injury; }
    },
    // 低忠诚武将出奔：v3 规则，忠诚取 SG.Mods.effLoy（官爵与羁绊的防护取较大者），德取 SG.Mods.virtue
    phaseDefections(g, ctx) {
      const Mo = SG.Mods, news = ctx.news;
      for (const gen of g.generals.filter(x => x.faction >= 0 && !x.dead)) {
        if (gen.faction < 0) continue;
        const L = Mo.effLoy(gen);
        if (g.isRuler(gen) || L >= 40) continue;
        const p = (40 - L) / 400 * (1.2 - Mo.virtue(gen.faction) / 100);
        if (SG.Random.value() < p) {
          const c = g.cities[gen.city];
          const f = gen.faction;
          gen.faction = -1; gen.troops = 0; gen.loyalty = 0;
          g.autoGovernor(c);
          if (f === g.player) news.push(gen.name + '不满待遇，弃官而去！');
          g.addLog(gen.name + '离开了' + g.factions[f].name + '。');
          ctx.defections.push({ gen: gen.id, from: f, city: c.id });
          StrategyAI.stats.defections++;
          if (g.officersIn(c).length === 0) { c.owner = -1; g.checkFactionDeath(f); }
        }
      }
    },
    // 忠诚光环：主政者的官职与宝物光环取较大者，不把忠诚抬到 95 以上（INTEGRATION §3.4）
    phaseLoyAura(g) {
      const Mo = SG.Mods, cap = Mo.CAPS.loyAuraCap;
      for (const c of g.cities) {
        if (c.owner < 0) continue;
        const la = Mo.loyAura(c);
        if (!(la > 0)) continue;
        for (const o of g.officersIn(c)) if (!g.isRuler(o) && o.loyalty < cap) o.loyalty = Math.min(cap, o.loyalty + la);
      }
    },
    // 自由支出：每个电脑势力先 accrue，再按固定次序 title → gear → project → festival（INTEGRATION §5.1）
    phaseDiscretionary(g, ctx) {
      const AIB = SG.AIBudget;
      if (!AIB) return;
      let any = false;
      for (const k of SPEND_KINDS) if (spendHooks[k].some(activeHook)) { any = true; break; }
      if (!any) return;
      for (const f of g.factions) {
        if (!f.alive || f.id === g.player) continue;
        AIB.accrue(f.id, ctx.gross[f.id] || 0);
        for (const k of SPEND_KINDS) for (const h of spendHooks[k].slice()) {
          if (!activeHook(h)) continue;
          if (!f.alive) break;
          try { h.fn(f.id, ctx); } catch (e) { console.error('StrategyAI.onSpend ' + k + ' ' + (h.owner || ''), e); }
        }
      }
    },

    // ---------------------------------------------------------- 电脑行动 --
    // 返回电脑对玩家发起的进攻（由控制器交给玩家应战）
    runAI(news) {
      const g = G();
      if (!news) news = [];
      if (SG.Clock) SG.Clock.setPhase('ai');
      const playerBattles = [];
      for (const f of g.factions.slice()) {
        if (!f.alive || f.id === g.player) continue;
        let tokens = g.tokensFor(f.id);
        const cities = Seq.orderBy(g.citiesOf(f.id), c => -threat(c, f.id));
        for (const c of cities) {
          if (tokens <= 0 || c.owner !== f.id) break;
          const offs = g.officersIn(c).filter(o => !o.moved);
          if (offs.length === 0) continue;
          tokens -= actCity(f.id, c, offs, playerBattles, news);
        }
      }
      return playerBattles;
    },

    // 月末排队、待玩家应战的电脑进攻，在轮到它时重新核对（同月先结算的进攻可能已使城池易主、守军溃散或被俘）。
    // 返回 true：仍是对玩家的进攻（守军已按城中现有武将重建），交玩家应战。
    // 返回 false：已不再针对玩家——按电脑之间结算（兵力不足则撤兵），或攻方已无力出征而取消（退还军粮）。
    recheckBattle(s, news) {
      const g = G();
      const A = s.attacker, t = s.target;
      const refund = () => { if (s.src.owner === A) { s.src.food += s.atkFood; s.src.gold += s.atkGold; } };
      s.atk = s.atk.filter(a => !a.dead && a.faction === A && a.city === s.src.id && a.troops > 0);
      if (!g.factions[A].alive || s.src.owner !== A || s.atk.length === 0) { refund(); return false; }
      if (t.owner === A || (t.owner >= 0 && (g.allied(t.owner, A) || noAttack(t.owner, A))) || cityProtected(t, A)) { refund(); return false; }
      s.defender = t.owner;
      s.def = pickDefenders(t);
      s.routed = new Set(); s.captives = []; s.captiveInfo = {};
      if (t.owner === g.player) return true;
      // 城已落入他人之手：兵力仍占优（或城已空）才继续进攻，否则撤兵
      const sum = Seq.sum(s.atk, a => a.troops);
      const defTroops = g.troopsIn(t);
      const empty = t.owner < 0 || defTroops === 0;
      if (!empty && sum / Math.max(1, defTroops * (1 + SG.cityDefense(t) / 200)) < attackNeed(A, t) + needK(A, t)) { refund(); return false; }
      resolveAI(s, news);
      return false;
    },
  };

  // 攻城所需兵力比（打城少者更积极）
  function attackNeed(f, t) { const g = G(); return t.owner >= 0 && g.cityCount(t.owner) < g.cityCount(f) ? 1.4 : 1.6; }

  // 电脑之间（或已不涉及玩家的）攻城：结算、处置俘虏、写入月末消息
  function resolveAI(s, news) {
    const g = G();
    const f = s.attacker, t = s.target;
    Conquest.autoResolve(s);
    Conquest.apply(s);
    if (s.captives.length > 0) { if (s.attackerWon) Conquest.aiDecideCaptives(s, f); else if (s.defender >= 0) Conquest.aiDecideCaptives(s, s.defender); }
    if (s.attackerWon && (s.defender >= 0 || isNear(t))) {
      news.push(s.defender >= 0
        ? `${g.factions[f].name}攻陷了${g.factions[s.defender].name}的${t.name}。`
        : `${g.factions[f].name}占领了${t.name}。`);
    }
  }

  function threat(c, f) {
    const g = G();
    let t = 0;
    for (const i of c.links) {
      const n = g.cities[i];
      if (n.owner >= 0 && n.owner !== f && !g.allied(n.owner, f) && !noAttack(n.owner, f)) t += g.troopsIn(n);
    }
    return t;
  }

  // 返回消耗的令牌数
  function actCity(f, c, offs, playerBattles, news) {
    const g = G();
    let used = 0;
    const gov = c.governor >= 0 ? g.generals[c.governor] : offs[0];
    // 1. 进攻
    const grace = g.monthIndex < ScenarioData.StartYear * 12 + ScenarioData.StartMonth + Balance.PlayerGraceMonths;
    const targets = c.links.map(i => g.cities[i]).filter(n => n.owner !== f && !g.allied(n.owner, f) && !(grace && n.owner === g.player) && !noAttack(n.owner, f) && !cityProtected(n, f));
    for (const t of Seq.orderBy(targets, t => g.troopsIn(t) * (1 + aiTargetK(f, t)))) {
      const avail = Seq.orderByDesc(offs.filter(o => !o.moved && o.troops > 600), o => o.troops * (o.war + 50));
      const keepRuler = true;
      if (avail.length === 0) break;
      const defTroops = g.troopsIn(t);
      let squad = [];
      let sum = 0;
      // 世界剧本：城中只剩君主一人时君主不出城（许多小国只有君主一名武将，否则孤身君主会连夺空城、
      // 沿海路与草原链远走他乡，开局一年内即有也门之王坐镇拜占庭之类的乱象）。经典剧本保持原样。
      const loneRuler = g.scenario === 'world' && offs.length === 1;
      for (const o of avail) {
        if (squad.length >= Balance.MaxSortieGenerals) break;
        if (keepRuler && g.isRuler(o) && (avail.length > 1 || loneRuler)) continue;
        squad.push(o); sum += o.troops;
      }
      // 至少留一人守城
      if (squad.length === offs.length && squad.length > 1) { sum -= squad[squad.length - 1].troops; squad.pop(); }
      const ratio = sum / Math.max(1, defTroops * (1 + SG.cityDefense(t) / 200));
      const need = attackNeed(f, t) + needK(f, t);
      const empty = t.owner < 0 || defTroops === 0;
      if (squad.length === 0 || (!empty && ratio < need)) continue;
      if (empty) squad = squad.slice(0, 1);
      const food = Math.min(M.idiv(c.food, 2), M.idiv(sum, 8) + 200);
      if (!empty && food < M.idiv(sum, 20)) continue;
      const s = Conquest.prepare(f, c, t, squad, food, 0);
      used++;
      if (t.owner === g.player && !empty) { playerBattles.push(s); }
      else resolveAI(s, news);
      return used;
    }
    // 2. 征兵
    const weakList = offs.filter(o => !o.moved && o.troops < SG.maxTroops(o) * 0.7);
    const weak = weakList.length ? Seq.orderBy(weakList, o => o.troops)[0] : null;
    if (weak != null && c.gold > 220) { Commands.recruit(c, weak, Commands.recruitMax(c, weak)); used++; if (used >= 2) return used; }
    // 3. 搜索 / 登用
    const free = Seq.first(g.freeFoundIn(c.id));
    if (free != null) { Commands.hire(free, gov, f, c.id); return used + 1; }
    if (SG.Random.value() < aiSearchP(c, f) && g.generals.some(x => x.city === c.id && x.hidden && SG.isFree(x))) {
      Commands.search(c, gov);
      if (Commands.searchFound != null) Commands.hire(Commands.searchFound, gov, f, c.id);
      return used + 1;
    }
    // 4. 开发
    if (c.gold > 160) {
      const k = c.land <= c.industry && c.land <= c.town ? DevKind.Land : c.industry <= c.town ? DevKind.Industry : DevKind.Town;
      Commands.develop(c, gov, k);
      used++;
    }
    // 5. 训练
    const lowList = offs.filter(o => o.troops > 0 && o.training < 70);
    const lowTrain = lowList.length ? Seq.orderBy(lowList, o => o.training)[0] : null;
    if (lowTrain != null && used < 2) { Commands.train(lowTrain); used++; }
    // 6. 后方武将调往前线
    if (threat(c, f) <= 0 && offs.length > 1) {
      // 路程 ≤ 2 的己方城（第二版 §4F：经由己方城池调动；电脑只看两步之内）
      const fronts = Commands.aiMoveTargets(c, f);
      const front = fronts.length ? Seq.orderByDesc(fronts, n => threat(n, f))[0] : null;
      if (front != null && threat(front, f) > 0) {
        const movers = offs.filter(o => !g.isRuler(o) && o.id !== c.governor);
        const mover = movers.length ? Seq.orderByDesc(movers, o => o.troops)[0] : null;
        if (mover != null) { Commands.move(mover, front); used++; }
      }
    }
    return Math.max(1, used);
  }

  function isNear(t) { const g = G(); return t.links.some(i => g.cities[i].owner === g.player); }

  SG.Conquest = Conquest;
  SG.StrategyAI = StrategyAI;
  SG.newBattleSetup = newSetup;
})();
