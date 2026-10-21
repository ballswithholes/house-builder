'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版战斗公式（INTEGRATION §3.2，COMBAT-V4 §2）
   战斗里所有第四版乘数只在这里合成一次：battle-model.js 的 atkPower × atkMul、defFactor × defMul，
   clash.js 与人物菜单读同样的数字（unitPreview），绝不与战斗不符。纯逻辑，Node 可测。
   载入：strategy-ai.js 之后、battle-model.js 之前（需要 js/econ.js 的 SG.Mods）。

   挂到 SG.Mods 上（并以 SG.BattleMath 为别名）：
     capped(pos, neg, cap)          min(cap, max(1, Π pos)) × max(CAPS.negFloor, min(1, Π neg))
     atkFactors(u, model) → { pos, neg }     四组各一个因子 (1 + x)：1 装备 gearAtk · 2 官职 titleAtk + 主将 cmdAtk ·
                                             3 羁绊 bondAtk · 4 光环 auraAtk；条件项由调用者以 cond 传入
     atkMul(u, model, cond) / defMul(u, model, cond)    封顶 CAPS.atk / CAPS.def（1.60），负向不低于 ×0.5；老练不在上限内
     productCap() / pairScale(a, d) DESIGN-V4 §20 问题 1：Balance.Decisions.stackProductCap（推荐 null = 不加）；设为数值（如 2.0）时
                                    攻 × 防 之积超过它，两边各乘 √(cap / (攻 × 防))（atkMul、defMul、unitPreview 一致）
     startMorale(u, base)           min(100, base + min(25, 装备 + 官职))
     FORM_CRIT                      阵型会心（SG.Defs.formations[i].crit）
     critChance(u, t?, ctx?)        clamp(2 % + 0.15 % × max(0, 武力 − 50) + 0.4 % × (vetLevel − 1) + 阵型
                                         + min(6 %, 装备 + 装备条件会心 gearCritCond(gen, ctx) + 官职) + (士气 ≥ 90 ? 1 % : 0) − (混乱 ? 1 : 0), 0, 25 %)
                                    有目标 t 时再 max(0, p − 抗会心 gearCritRes) × (1 − 护身 gearCritWard)（ITEMS §3.2 / §7.2）
     critK(u, ctx?)                 1.5 + min(0.30, 装备会心伤害 + 条件项 gearCritMulCond)
     critEnabled()                  SG.Crit.enabled !== false 且 SG.BattleModel.critEnabled !== false
     rollCrit(u, enabled?, t?, ctx?) → { roll, p, crit, k }
                                    **每次调用恰好取一次 SG.Random.value()**（COMBAT-V4 §2.2：即使 p = 0 或会心关闭），
                                    放在 attack() 的第一个随机数；关掉会心时随机数流不变，开 / 关的对局一一配对
     vetLevel(gen) / vetK(gen)      老练等级（非战斗经验按一半，ENGAGEMENT §4.4）与乘数（SG.Progress）
     unitPreview(gen, opts?) → { atk, def, atkMul, defMul, vetK, crit, critK, morale, formation, parts }
                                    人物菜单「总览」的攻防会心（独自一军、本人为主将；opts.formation / morale / model）
   SG.BattleMathCore.extend(Mods, SG, P)   把同样的函数装到另一个 SG.EconCore 实例上（测试、设计原型回放）
   ========================================================================== */
(function () {
  const SG = window.SG;

  function extend(Mods, SGx, P) {
    const CAPS = Mods.CAPS;
    const z = (fn, a, b, c) => (fn ? fn(a, b, c) || 0 : 0);
    const B = () => SGx.Balance;
    const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
    const FORM_CRIT = (SGx.Defs && SGx.Defs.formations && SGx.Defs.formations.length && SGx.Defs.formations.every(f => typeof f.crit === 'number'))
      ? SGx.Defs.formations.map(f => f.crit)
      : [-0.02, 0, 0.02, 0.01, 0.03, 0.04, 0.01, 0];

    // DESIGN-V4 §20 问题 1 的可选乘积上限（缺省 null：不加，atkMul / defMul 与上限表的结果完全相同）
    const productCap = () => { const D = B() && B().Decisions, v = D ? D.stackProductCap : null; return typeof v === 'number' && v > 0 ? v : null; };
    const pairScale = (a, d) => { const pc = productCap(); return pc && a * d > pc ? Math.sqrt(pc / (a * d)) : 1; };
    const pushTo = (pos, neg, v) => { if (v > 0) pos.push(1 + v); else if (v < 0) neg.push(1 + v); };
    Object.assign(Mods, {
      FORM_CRIT,
      capped(pos, neg, cap) {
        let p = 1; for (const x of pos) p *= x;
        let n = 1; for (const x of neg) n *= x;
        return Math.min(cap, Math.max(1, p)) * Math.max(CAPS.negFloor, Math.min(1, n));
      },
      atkFactors(u, model) {
        const pos = [], neg = [];
        pushTo(pos, neg, z(P.gearAtk, u.gen));                                         // ITEMS atk % (≤ 0.40)
        pushTo(pos, neg, z(P.titleAtk, u.gen) + z(P.cmdAtk, model, u.side));           // TITLES personal + commander aura
        pushTo(pos, neg, z(P.bondAtk, u, model));                                      // ENGAGEMENT bonds (−0.08 … +0.08) + 义愤
        pushTo(pos, neg, z(P.auraAtk, u, model));                                      // ITEMS auras / battle abilities
        return { pos, neg };
      },
      defFactors(u, model) {
        const pos = [], neg = [];
        pushTo(pos, neg, z(P.gearDef, u.gen));
        pushTo(pos, neg, z(P.titleDef, u.gen) + z(P.cmdDef, model, u.side));
        pushTo(pos, neg, z(P.bondDef, u, model));
        pushTo(pos, neg, z(P.auraDef, u, model));
        return { pos, neg };
      },
      productCap,
      pairScale,
      atkMul(u, model, cond) {
        const f = Mods.atkFactors(u, model);
        for (const k of cond || []) pushTo(f.pos, f.neg, k);
        const a = Mods.capped(f.pos, f.neg, CAPS.atk);
        if (!productCap()) return a;
        const e = Mods.defFactors(u, model);
        return a * pairScale(a, Mods.capped(e.pos, e.neg, CAPS.def));
      },
      defMul(u, model, cond) {
        const f = Mods.defFactors(u, model);
        for (const k of cond || []) pushTo(f.pos, f.neg, k);
        const d = Mods.capped(f.pos, f.neg, CAPS.def);
        if (!productCap()) return d;
        const e = Mods.atkFactors(u, model);
        return d * pairScale(Mods.capped(e.pos, e.neg, CAPS.atk), d);
      },
      startMorale(u, base) { return Math.min(100, base + Math.min(CAPS.morale, z(P.gearMorale, u.gen) + z(P.titleMorale, u.gen))); },
      vetLevel(gen) {
        const v = P.vetLevel ? P.vetLevel(gen) : undefined;
        if (v !== undefined) return v;
        const Pg = SGx.Progress;
        if (Pg && typeof Pg.vetLevel === 'function') return Pg.vetLevel(gen);
        return (gen && gen.level) || 1;
      },
      vetK(gen) {
        const Pg = SGx.Progress;
        return Pg && typeof Pg.vetK === 'function' ? Pg.vetK(gen) : 1;
      },
      // COMBAT-V4 §2.1 with the level term on 老练 level (ENGAGEMENT §4.4)
      // t / ctx（可缺省）：目标与 ITEMS 的条件会心（ctx = { charge, vsCommander, moreTroops, plain, outnumbered }，gearCritCond 仍在 critAdd 0.06 之内）；
      // 目标的抗会心 gearCritRes（减 pp，≥ 0）与护身 gearCritWard（× (1 − k)）在封顶之后
      critChance(u, t, ctx) {
        const g = u.gen, Bb = B();
        const lvl = SGx.Progress && SGx.Progress.enabled ? Mods.vetLevel(g) : 1;
        let p = clamp(Bb.CritBase + Bb.CritPerWar * Math.max(0, g.war - Bb.CritWarFrom) + Bb.CritPerLevel * (lvl - 1) + (FORM_CRIT[u.formation] || 0)
          + Math.min(CAPS.critAdd, z(P.gearCrit, g) + (ctx ? z(P.gearCritCond, g, ctx) : 0) + z(P.titleCrit, g)) + (u.morale >= 90 ? 0.01 : 0) - (u.confused > 0 ? 1 : 0), 0, Bb.CritCap);
        if (t && t.gen && p > 0) p = Math.max(0, p - z(P.gearCritRes, t.gen)) * (1 - Math.min(1, z(P.gearCritWard, t.gen)));
        return p;
      },
      critK(u, ctx) { return B().CritK + Math.min(CAPS.critMul, z(P.gearCritMul, u.gen) + (ctx ? z(P.gearCritMulCond, u.gen, ctx) : 0)); },
      critEnabled() {
        if (SGx.Crit && SGx.Crit.enabled === false) return false;
        if (SGx.BattleModel && SGx.BattleModel.critEnabled === false) return false;
        return true;
      },
      // exactly one SG.Random.value() per call, first in attack() (COMBAT-V4 §2.2)
      rollCrit(u, enabled, t, ctx) {
        const roll = SGx.Random.value();
        const on = enabled === undefined ? Mods.critEnabled() : !!enabled;
        if (!on) return { roll, p: 0, crit: false, k: 1 };
        const p = Mods.critChance(u, t, ctx);
        const crit = roll < p;
        return { roll, p, crit, k: crit ? Mods.critK(u, ctx) : 1 };
      },
      // the character sheet's numbers (UI-V4 §9): a lone army led by gen, no state change, no random draw
      unitPreview(gen, opts) {
        opts = opts || {};
        const formation = opts.formation !== undefined ? opts.formation : (gen.formation | 0);
        const u = { gen, side: 0, alive: true, formation, morale: opts.morale !== undefined ? opts.morale : 70, confused: 0, commander: true, troops: gen.troops };
        const model = opts.model || { commander: () => u, units: [u] };
        const a = Mods.atkFactors(u, model), d = Mods.defFactors(u, model);
        const a0 = Mods.capped(a.pos, a.neg, CAPS.atk), d0 = Mods.capped(d.pos, d.neg, CAPS.def), ps = pairScale(a0, d0);
        const atkMul = a0 * ps, defMul = d0 * ps;
        const vk = Mods.vetK(gen);
        const form = SGx.Defs && SGx.Defs.formations ? SGx.Defs.formations[formation] : null;
        return {
          atk: vk * atkMul, def: vk * defMul, atkMul, defMul, vetK: vk, vetLevel: Mods.vetLevel(gen),
          crit: Mods.critChance(u), critK: Mods.critK(u),
          morale: Mods.startMorale(u, 0),
          formation: form ? { index: formation, name: form.name, atk: form.atk, def: form.def, crit: form.crit || 0 } : null,
          parts: {
            atk: { gear: z(P.gearAtk, gen), title: z(P.titleAtk, gen) + z(P.cmdAtk, model, 0), bond: z(P.bondAtk, u, model), aura: z(P.auraAtk, u, model) },
            def: { gear: z(P.gearDef, gen), title: z(P.titleDef, gen) + z(P.cmdDef, model, 0), bond: z(P.bondDef, u, model), aura: z(P.auraDef, u, model) },
            crit: { gear: z(P.gearCrit, gen), title: z(P.titleCrit, gen), mul: z(P.gearCritMul, gen) },
          },
          capped: { atk: a.pos.reduce((s, x) => s * x, 1) > CAPS.atk, def: d.pos.reduce((s, x) => s * x, 1) > CAPS.def },
        };
      },
    });
    return Mods;
  }

  SG.BattleMathCore = { extend };
  if (!SG.Crit) SG.Crit = { enabled: true };
  if (SG.Mods) {
    extend(SG.Mods, SG, SG.Mods.P);
    const M = SG.Mods;
    SG.BattleMath = {
      capped: M.capped, atkFactors: M.atkFactors, defFactors: M.defFactors, atkMul: M.atkMul, defMul: M.defMul,
      startMorale: M.startMorale, FORM_CRIT: M.FORM_CRIT, critChance: M.critChance, critK: M.critK, critEnabled: M.critEnabled,
      rollCrit: M.rollCrit, vetLevel: M.vetLevel, vetK: M.vetK, unitPreview: M.unitPreview, productCap: M.productCap, pairScale: M.pairScale,
    };
  }
})();
