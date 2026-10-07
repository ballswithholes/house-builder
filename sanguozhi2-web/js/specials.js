'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 必杀技（DESIGN-V2 §4D）

   数据：js/specials-data.js（SG.SpecialsData，条目格式与机制参数表写在该文件头注释里）。
   规则：js/battle-model.js（specialUsable / specialTargets / useSpecial / aiSpecial）。
   特效：js/battle-view.js（V.specialFx）。本文件：机制表、默认参数、校验、兜底生成器、控制层辅助。
   逻辑部分不碰 DOM（tests/sim.js 在 Node 下载入）；只有 perform / menu / pickTarget / cutIn 需要浏览器。

   公开接口
     SG.Specials.of(gen)              → 该武将的必杀技（冻结对象，已补全默认参数），按姓名缓存：
                                        { id, gen, name, kind, stat, <数值参数…>, desc, lore, color, fx, cry, auto, problems }
                                        auto = true 表示由兜底生成器生成（没有手写条目）
     SG.Specials.all(gens?)           → 全部条目（名单内武将 + 全部手写条目）。gens 缺省为 SG.G.generals，
                                        再缺省为剧本武将表
     SG.Specials.check(gens?)         → 自检报告 { total, hand, auto, dupNames, dupSigs, problems, ok }
     SG.Specials.validate(entry, who) → 条目问题列表（字符串数组，空 = 合格）
     SG.Specials.signature(sp)        → (kind, stat, 全部数值参数) 签名，用于唯一性校验
     SG.Specials.needsTarget(sp)      → true：需要选择一个目标（敌军或友军）；false：以自身为中心
     SG.Specials.targetSide(sp)       → 'enemy' | 'ally' | 'self'
     SG.Specials.explain(sp)          → 由数值生成的效果说明（中文一句）
     SG.Specials.rules(sp)            → 紧凑数值摘要（「单体 · 射程1 · 威力×2.4 · 士气−20 · 无视地形」）
     SG.Specials.kindName(kind)       → 机制中文名
     SG.Specials.uiColor(sp | '#hex') → 界面文字用色（主色偏暗时提亮，深色面板上可读）
     SG.Specials.KINDS / FX / cultureOf(gen) / fallback(gen)（调试用：忽略手写条目，直接生成）

   控制层辅助（bc = SG.BattleController 实例；用到 bc.M、bc.V、bc.pickEnemy、bc.showCard、bc.updateHud）
     SG.Specials.menuItem(bc, u)            → 战斗行动菜单的「必杀」条目（SG.UI.item）
     SG.Specials.cardLine(u)                → 部队情报卡的一行 HTML：必杀「名」可用 / 已施展
     await SG.Specials.pickTarget(bc, u)    → 目标 BUnit | null（自身招式直接返回 u；沿用 bc.pickEnemy，并在棋盘上标出范围）
     await SG.Specials.menu(bc, u, moved)   → 完整流程：选目标 → perform → Mdl.spend(u)；取消时与策略菜单相同（moved 时也 spend）
     await SG.Specials.perform(bc, u, target, opts) → res（Mdl.useSpecial 的结果）
         特写（SG.Clash.cutIn，若无则本文件的简易特写）→ Mdl.useSpecial → V.specialFx → 伤害 / 回复飘字 →
         刷新部队与情报卡 → 溃散动画。与 doAttack / doTactic 一样不调用 spend（由调用方 spend）。
         opts：{ cutIn: true（false 跳过特写）, fast: false（电脑回合可 true：特写缩短）,
                 rig: 镜头（缺省 SG.Game.rig；特效期间推近到施展者与目标之间，结束后恢复距离）, zoom: true }
     await SG.Specials.cutIn({ gen, name, color, side, cry, fast }) → 简易特写（DOM，约 1.1 秒；放在 SG.UI 的 screens 层，点击或按键跳过）
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  // ============================================================ 机制表 --
  // params: key → [默认, 最小, 最大, 步长]（步长 1 的为整数参数）
  const KINDS = {
    smite: {
      label: '单体重击', stat: 'war', target: 'enemy', fx: 'slash', color: '#ffd36b',
      params: { range: [1, 1, 2, 1], power: [2.0, 1.4, 2.6, 0.01], morale: [-10, -40, 0, 1], pierce: [0, 0, 1, 1], confuse: [0, 0, 0.6, 0.01], turns: [1, 1, 2, 1] },
    },
    cleave: {
      label: '横扫', stat: 'war', target: 'enemy', fx: 'sweep', color: '#ffb04a',
      params: { power: [1.65, 1.1, 2.2, 0.01], splash: [0.75, 0.3, 1, 0.01], morale: [-8, -30, 0, 1], confuse: [0, 0, 0.5, 0.01], turns: [1, 1, 2, 1] },
    },
    charge: {
      label: '突击', stat: 'war', target: 'enemy', fx: 'dash', color: '#ff6b4a',
      params: { range: [3, 2, 4, 1], power: [1.7, 1.2, 2.2, 0.01], dash: [0.12, 0, 0.2, 0.01], push: [1, 0, 1, 1], morale: [-10, -30, 0, 1] },
    },
    rampage: {
      label: '连斩', stat: 'war', target: 'enemy', fx: 'whirl', color: '#e8f2ff',
      params: { range: [2, 1, 3, 1], strikes: [4, 2, 7, 1], power: [0.55, 0.25, 1, 0.01], morale: [-6, -20, 0, 1] },
    },
    volley: {
      label: '齐射', stat: 'war', target: 'enemy', fx: 'arrows', color: '#ffe08a',
      params: { range: [3, 2, 5, 1], power: [1.8, 1, 2.4, 0.01], radius: [0, 0, 1, 1], splash: [0.6, 0.3, 1, 0.01], pierce: [0, 0, 1, 1], morale: [-6, -30, 0, 1] },
    },
    blaze: {
      label: '火攻', stat: 'intel', target: 'enemy', fx: 'fire', color: '#ff7a2a',
      params: { range: [3, 2, 5, 1], radius: [1, 0, 2, 1], power: [1.15, 0.65, 1.7, 0.01], splash: [0.75, 0.3, 1, 0.01], burn: [2, 0, 3, 1], morale: [-8, -30, 0, 1] },
    },
    storm: {
      label: '天候', stat: 'intel', target: 'enemy', fx: 'lightning', color: '#9fd8ff',
      params: { range: [5, 3, 7, 1], radius: [2, 1, 3, 1], power: [0.7, 0.4, 1, 0.01], burn: [0, 0, 3, 1], confuse: [0, 0, 0.5, 0.01], turns: [1, 1, 2, 1], morale: [-10, -30, 0, 1] },
    },
    flood: {
      label: '水攻', stat: 'intel', target: 'enemy', fx: 'water', color: '#4fb4ff',
      params: { range: [4, 2, 6, 1], radius: [1, 1, 2, 1], power: [0.85, 0.5, 1.2, 0.01], confuse: [0, 0, 0.4, 0.01], turns: [1, 1, 2, 1], morale: [-12, -30, 0, 1] },
    },
    roar: {
      label: '威吓', stat: 'war', target: 'self', fx: 'shock', color: '#ff9a3c',
      params: { radius: [2, 1, 3, 1], power: [0.45, 0, 0.9, 0.01], morale: [-25, -40, -5, 1], confuse: [0.35, 0, 0.8, 0.01], turns: [1, 1, 2, 1] },
    },
    rally: {
      label: '鼓舞', stat: 'pol', target: 'self', fx: 'aura', color: '#ffd966',
      params: { radius: [2, 1, 3, 1], morale: [25, 10, 40, 1], heal: [0.08, 0, 0.2, 0.01], cure: [1, 0, 1, 1], atk: [1, 1, 1.3, 0.01], turns: [2, 1, 3, 1] },
    },
    command: {
      label: '号令', stat: 'pol', target: 'self', fx: 'aura', color: '#ffcf5a',
      params: { radius: [2, 1, 3, 1], atk: [1.25, 1, 1.4, 0.01], def: [1.1, 1, 1.3, 0.01], turns: [2, 1, 3, 1], morale: [10, 0, 30, 1] },
    },
    scheme: {
      label: '奇谋', stat: 'intel', target: 'enemy', fx: 'spirit', color: '#c08bff',
      params: { range: [4, 2, 6, 1], radius: [1, 0, 2, 1], chance: [0.55, 0.3, 0.9, 0.01], turns: [2, 1, 2, 1], morale: [-10, -30, 0, 1], power: [0, 0, 0.5, 0.01] },
    },
    drain: {
      label: '收编', stat: 'war', target: 'enemy', fx: 'drain', color: '#ff4f7a',
      params: { range: [1, 1, 2, 1], power: [1.6, 1.2, 2.2, 0.01], drain: [0.5, 0.2, 0.8, 0.01], morale: [-10, -30, 0, 1] },
    },
    fortify: {
      label: '坚守', stat: 'pol', target: 'self', fx: 'shield', color: '#79b8ff',
      params: { radius: [1, 0, 2, 1], def: [1.35, 1.1, 1.7, 0.01], counter: [1.3, 1, 2, 0.01], turns: [2, 1, 3, 1], morale: [10, 0, 30, 1] },
    },
    haste: {
      label: '疾行', stat: 'pol', target: 'self', fx: 'haste', color: '#7affd9',
      params: { radius: [2, 1, 3, 1], count: [2, 1, 3, 1], move: [1, 0, 2, 1], morale: [5, 0, 20, 1] },
    },
    heal: {
      label: '医术', stat: 'intel', target: 'ally', fx: 'heal', color: '#7dffa8',
      params: { range: [2, 0, 4, 1], radius: [1, 0, 2, 1], heal: [0.2, 0.08, 0.35, 0.01], morale: [10, 0, 30, 1], cure: [1, 0, 1, 1] },
    },
    assassinate: {
      label: '暗杀', stat: 'war', target: 'enemy', fx: 'shadow', color: '#b04dff',
      params: { range: [2, 1, 3, 1], chance: [0.3, 0.1, 0.5, 0.01], power: [0.6, 0, 1.2, 0.01], morale: [-15, -40, 0, 1] },
    },
    poison: {
      label: '毒计', stat: 'intel', target: 'enemy', fx: 'poison', color: '#8be05a',
      params: { range: [3, 2, 5, 1], radius: [1, 0, 1, 1], power: [0.4, 0, 0.8, 0.01], dot: [0.06, 0.03, 0.12, 0.01], turns: [3, 2, 4, 1], morale: [-8, -30, 0, 1] },
    },
  };
  const FX = ['slash', 'dragon', 'havoc', 'sweep', 'dash', 'whirl', 'arrows', 'arrow', 'fire', 'wind', 'lightning', 'water',
    'shock', 'aura', 'blossom', 'spirit', 'shield', 'heal', 'poison', 'shadow', 'drain', 'haste', 'claw', 'rock'];
  // 水攻：目标在河上或紧邻河流时的伤害倍率（battle-model.js 读取 SG.Specials.FLOOD_RIVER / FLOOD_DRY）
  const FLOOD_RIVER = 1.25, FLOOD_DRY = 0.85;
  // 突击：冲刺（每格 dash）与退路受阻（+25%）的加成合计不超过 CHARGE_CAP 倍
  const CHARGE_CAP = 1.3;
  // 范围招式：同时命中多支敌军时，全部伤害合计不超过 AREA_TOTAL × 基准（主目标不缩减，次要目标按比例缩减）
  const AREA_TOTAL = 2.5;
  const META_KEYS = ['name', 'kind', 'desc', 'lore', 'color', 'fx', 'cry', 'stat', 'note'];
  const STATS = ['war', 'intel', 'pol'];

  // 孟获一族（DESIGN-V2 §6：现有武将无 culture 时视为 han，孟获一族为 nanman）
  const NANMAN = new Set(['孟获', '祝融', '孟优', '兀突骨', '带来洞主', '沙摩柯', '孟节', '朵思大王', '木鹿大王', '金环三结', '董荼那', '阿会喃']);
  function cultureOf(gen) { return (gen && gen.culture) || (gen && NANMAN.has(gen.name) ? 'nanman' : 'han'); }

  // ------------------------------------------------------------ 小工具 --
  function hash(str) {
    let h = 0x811c9dc5;
    for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 0x01000193); }
    return h >>> 0;
  }
  function quant(v, step) { return step >= 1 ? Math.round(v) : Math.round(v / step) * step; }
  function fix(v) { return Math.round(v * 1000) / 1000; }
  function num(v) { return typeof v === 'number' && isFinite(v); }
  function fmt(v) { return String(fix(v)); }
  function pct(v) { return Math.round(v * 100) + '%'; }
  function signed(v) { return v > 0 ? '+' + v : v < 0 ? '−' + Math.abs(v) : '0'; }
  function hexToHsl(hex) {
    const [r, g, b] = SG.hexToRgb(hex);
    const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2;
    if (mx === mn) return [0, 0, l];
    const d = mx - mn, s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
    let h = mx === r ? (g - b) / d + (g < b ? 6 : 0) : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return [h / 6, s, l];
  }
  function hslToHex(h, s, l) {
    const f = (p, q, t) => { t = (t % 1 + 1) % 1; return t < 1 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2 / 3 ? p + (q - p) * (2 / 3 - t) * 6 : p; };
    if (s === 0) return SG.rgbToHex(l, l, l);
    const q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
    return SG.rgbToHex(f(p, q, h + 1 / 3), f(p, q, h), f(p, q, h - 1 / 3));
  }

  // 界面文字用色：招式主色偏暗时提亮（深色面板上保持可读），特效仍用原色
  function uiColor(spOrHex) {
    const hex = typeof spOrHex === 'string' ? spOrHex : (spOrHex && spOrHex.color) || '#f3c969';
    if (!/^#[0-9a-f]{6}$/i.test(hex)) return '#f3c969';
    const [h, s0, l] = hexToHsl(hex);
    return l >= 0.62 ? hex : hslToHex(h, Math.min(1, s0 * 1.05), 0.68);
  }

  // ============================================================ 校验与补全 --
  function validate(e, who) {
    const out = [];
    const tag = who ? who + '：' : '';
    if (!e || typeof e !== 'object') return [tag + '条目不是对象'];
    if (typeof e.name !== 'string' || !e.name.trim()) out.push(tag + '缺少招式名 name');
    else {
      if (e.name.indexOf('·') >= 0) out.push(tag + '招式名不得含「·」（留给兜底生成的名称）');
      if (e.name.length > 7) out.push(tag + '招式名过长（' + e.name.length + ' 字）');
    }
    const K = KINDS[e.kind];
    if (!K) { out.push(tag + '未知机制 kind = ' + e.kind); return out; }
    const hasText = v => typeof v === 'string' && v.trim().length > 0;
    if (!hasText(e.desc) && !hasText(e.lore)) out.push(tag + '缺少中文说明 desc（或典故 lore）');
    if (e.lore !== undefined && !/[。！？」]$/.test(String(e.lore).trim())) out.push(tag + 'lore 应为完整的一句话（以句号结尾）');
    if (e.color !== undefined && !/^#[0-9a-f]{6}$/i.test(String(e.color))) out.push(tag + '颜色应为 #rrggbb：' + e.color);
    if (e.fx !== undefined && FX.indexOf(e.fx) < 0) out.push(tag + '未知特效 fx = ' + e.fx);
    if (e.stat !== undefined && STATS.indexOf(e.stat) < 0) out.push(tag + 'stat 应为 war / intel / pol：' + e.stat);
    for (const k of Object.keys(e)) {
      if (META_KEYS.indexOf(k) >= 0) continue;
      const p = K.params[k];
      if (!p) { out.push(tag + K.label + '（' + e.kind + '）没有参数 ' + k); continue; }
      const v = e[k];
      if (!num(v)) { out.push(tag + '参数 ' + k + ' 应为数字：' + v); continue; }
      if (v < p[1] - 1e-9 || v > p[2] + 1e-9) out.push(tag + '参数 ' + k + ' = ' + v + ' 超出范围 [' + p[1] + ', ' + p[2] + ']');
      if (p[3] >= 1 && Math.round(v) !== v) out.push(tag + '参数 ' + k + ' 应为整数：' + v);
    }
    if (e.kind === 'rampage') {
      const s = num(e.strikes) ? e.strikes : K.params.strikes[0], pw = num(e.power) ? e.power : K.params.power[0];
      if (s * pw > 2.65) out.push(tag + '连斩总威力 strikes×power = ' + fix(s * pw) + ' 过高（建议 ≤ 2.5）');
    }
    return out;
  }

  // 补全默认参数并夹到范围内 → 冻结对象
  function normalize(e, owner, auto) {
    const K = KINDS[e.kind] || KINDS.smite;
    const kind = KINDS[e.kind] ? e.kind : 'smite';
    const sp = {
      id: 'sp:' + owner, gen: owner, name: String(e.name || owner + '·绝技'), kind,
      stat: STATS.indexOf(e.stat) >= 0 ? e.stat : K.stat,
    };
    for (const k of Object.keys(K.params)) {
      const p = K.params[k];
      let v = num(e[k]) ? e[k] : p[0];
      v = M.clamp(v, p[1], p[2]);
      sp[k] = p[3] >= 1 ? Math.round(v) : fix(v);
    }
    sp.color = /^#[0-9a-f]{6}$/i.test(String(e.color || '')) ? String(e.color).toLowerCase() : K.color;
    sp.fx = FX.indexOf(e.fx) >= 0 ? e.fx : K.fx;
    sp.cry = typeof e.cry === 'string' ? e.cry : '';
    // 说明：手写 desc 优先；否则「典故 lore + 由数值生成的效果说明」，效果说明永远与数值一致
    sp.lore = typeof e.lore === 'string' ? e.lore.trim() : '';
    sp.desc = typeof e.desc === 'string' && e.desc.trim() ? e.desc : sp.lore + explain(sp);
    sp.auto = !!auto;
    sp.problems = auto ? [] : validate(e, owner);
    return Object.freeze(sp);
  }

  function signature(sp) {
    const K = KINDS[sp.kind];
    if (!K) return String(sp.kind);
    return sp.kind + '|' + sp.stat + '|' + Object.keys(K.params).sort().map(k => k + '=' + fmt(sp[k])).join(',');
  }

  // ============================================================ 说明文字 --
  function kindName(kind) { return KINDS[kind] ? KINDS[kind].label : String(kind); }
  function targetSide(sp) { const K = KINDS[sp && sp.kind]; return K ? K.target : 'enemy'; }
  function needsTarget(sp) { return targetSide(sp) !== 'self'; }

  function explain(sp) {
    const pw = fmt(sp.power);
    const near = r => (r <= 1 ? '相邻' : ' ' + r + ' 格内');
    const mor = sp.morale < 0 ? '，敌军士气 ' + signed(sp.morale) : '';
    const conf = sp.confuse > 0 ? '，并可能陷入混乱' : '';
    const pierce = sp.pierce ? '（无视地形）' : '';
    // 伤害说法随所依能力而变：武力系以普通攻击为基准，智力 / 政治系以计策为基准
    const war = (sp.stat || 'war') === 'war';
    const dmg = k => war ? '约 ' + fmt(k) + ' 倍普通攻击的伤害' : '计策级伤害 ×' + fmt(k);
    const amt = k => war ? '约 ' + fmt(k) + ' 倍' : ' ×' + fmt(k) + ' 的';
    // 范围伤害封顶（battle-model specialPlan：总伤害 ≤ AREA_TOTAL × 基准）
    const cap = '（同时命中多支时总伤害封顶）';
    // 辅助类数值按能力缩放：kS = 0.75 + 能力/400（能力 100 时为 1）
    const statName = { war: '武力', intel: '智力', pol: '政治' }[sp.stat || 'war'];
    const scaled = '（' + statName + ' 100 时的数值，每低 40 点弱一成）';
    switch (sp.kind) {
      case 'smite': return '对' + near(sp.range) + '一支敌军全力一击，造成' + dmg(sp.power) + pierce + mor + conf + '。';
      case 'cleave': return '横扫相邻敌军：主目标受' + dmg(sp.power) + '，其余相邻敌军受' + amt(sp.power * sp.splash) + cap + mor + conf + '。';
      case 'charge': return '沿直线冲向 ' + sp.range + ' 格内的敌军（途中须无阻挡），造成' + dmg(sp.power) + '，每冲过一格再增 ' + pct(sp.dash) +
        (sp.push ? '，并将其击退一格（退路受阻或敌军据守本城、城门时改为伤害 +25%）' : '') + '，加成合计至多 +' + pct(CHARGE_CAP - 1) + mor + '。';
      case 'rampage': return '在' + (sp.range <= 1 ? '相邻' : ' ' + sp.range + ' 格内的') + '敌军之间往来冲杀，连斩 ' + sp.strikes + ' 次，每斩' + dmg(sp.power) + '（目标溃散则转斩下一支）' + mor + '。';
      case 'volley': return '向 ' + sp.range + ' 格内的敌军发射，造成' + dmg(sp.power) + pierce +
        (sp.radius > 0 ? '，周围一格的敌军受' + amt(sp.power * sp.splash) + '溅射' + cap : '') + mor + '。';
      case 'blaze': return '对 ' + sp.range + ' 格内的敌军纵火，造成' + dmg(sp.power) + (sp.radius > 0 ? '，周围 ' + sp.radius + ' 格的敌军受' + amt(sp.power * sp.splash) + '火焚' + cap : '') +
        '（林地 ×1.5、河上 ×0.5）' + (sp.burn > 0 ? '，燃起 ' + sp.burn + ' 日大火' : '') + mor + '。';
      case 'storm': return sp.range + ' 格内任选一处，周围 ' + sp.radius + ' 格内的所有敌军受天候重创（' + dmg(sp.power) + '）' + cap +
        (sp.burn > 0 ? '，脚下燃起 ' + sp.burn + ' 日大火' : '') + mor + conf + '。';
      case 'flood': return '引水灌向 ' + sp.range + ' 格内一处，周围 ' + sp.radius + ' 格的敌军受水攻（' + dmg(sp.power) + '，近河 ×' + FLOOD_RIVER + '、否则 ×' + FLOOD_DRY + '）' + cap + '，并扑灭火势' + mor + conf + '。';
      case 'roar': return '一声怒吼，周围 ' + sp.radius + ' 格内的敌军' + (sp.power > 0 ? '受震伤、' : '') + '士气 ' + signed(sp.morale) + '（武力 100 时的降幅，每低 40 点弱一成）' + conf + '。';
      case 'rally': return '鼓舞周围 ' + sp.radius + ' 格内的友军：士气 +' + sp.morale + (sp.heal > 0 ? '，回复 ' + pct(sp.heal) + ' 最大兵力' : '') +
        (sp.cure ? '，解除混乱' : '') + (sp.atk > 1 ? '，攻击 +' + pct(sp.atk - 1) + '（' + sp.turns + ' 日）' : '') + scaled + '。';
      case 'command': return '号令周围 ' + sp.radius + ' 格内的友军：攻击 +' + pct(sp.atk - 1) + (sp.def > 1 ? '、防御 +' + pct(sp.def - 1) : '') +
        '，持续 ' + sp.turns + ' 日' + (sp.morale > 0 ? '，士气 +' + sp.morale : '') + scaled + '。';
      case 'scheme': return '对 ' + sp.range + ' 格内一处施展奇谋，' + (sp.radius > 0 ? '周围 ' + sp.radius + ' 格内的' : '') + '敌军可能陷入混乱 ' + sp.turns +
        ' 日（成功率随智力差变化）' + (sp.power > 0 ? '，并受少量伤害' : '') + mor + '。';
      case 'drain': return '击溃' + near(sp.range) + '敌军，造成' + dmg(sp.power) + '，并将约 ' + pct(sp.drain) + ' 的伤亡收编为己方兵力（随政治增减）' + mor + '。';
      case 'fortify': return (sp.radius > 0 ? '自身与周围 ' + sp.radius + ' 格内的友军' : '自身') + '防御 +' + pct(sp.def - 1) +
        (sp.counter > 1 ? '、反击 +' + pct(sp.counter - 1) : '') + '，持续 ' + sp.turns + ' 日' + (sp.morale > 0 ? '，士气 +' + sp.morale : '') + scaled + '。';
      case 'haste': return '令周围 ' + sp.radius + ' 格内至多 ' + sp.count + ' 支已行动的友军再次行动' + (sp.move > 0 ? '，当日机动力 +' + sp.move : '') + '。';
      case 'heal': return '为 ' + (sp.range > 0 ? sp.range + ' 格内' : '自身') + (sp.radius > 0 ? '一支友军及其周围 ' + sp.radius + ' 格的友军' : '一支友军') +
        '疗伤，回复 ' + pct(sp.heal) + ' 最大兵力' + (sp.morale > 0 ? '、士气 +' + sp.morale : '') + scaled + (sp.cure ? '，并解除混乱' : '') + '。';
      case 'assassinate': return (sp.stat === 'intel' ? '遣死士潜入 ' : '潜入 ') + sp.range + ' 格内的敌阵刺杀敌将，得手则该部当即溃散（基础成功率 ' + pct(sp.chance) + '，随' + (sp.stat === 'intel' ? '智力' : '武力') + '差变化，主将减半）；' +
        (sp.power > 0 ? '失手仍造成' + dmg(sp.power) : '失手则无功而返') + mor + '。';
      case 'poison': return '向 ' + sp.range + ' 格内一处施毒' + (sp.radius > 0 ? '，周围一格的敌军同时中毒' : '') + '：每日损兵约 ' + pct(sp.dot) + '，持续 ' + sp.turns + ' 日' + mor + '。';
      default: return '';
    }
  }

  function rules(sp) {
    if (!sp) return '';
    const K = KINDS[sp.kind];
    const parts = [K ? K.label : sp.kind];
    const statName = { war: '武力', intel: '智力', pol: '政治' }[sp.stat];
    const P = K ? K.params : {};
    if ('range' in P) parts.push(sp.range === 0 ? '自身' : '射程' + sp.range);
    if ('radius' in P && sp.radius > 0) parts.push((K.target === 'self' ? '周围' : '范围') + sp.radius);
    if ('strikes' in P) parts.push(sp.strikes + '连斩');
    if ('power' in P && sp.power > 0) parts.push('威力×' + fmt(sp.power));
    if ('splash' in P && (sp.kind === 'cleave' || sp.radius > 0)) parts.push('溅射×' + fmt(sp.splash));
    if ('dash' in P && sp.dash > 0) parts.push('每格+' + pct(sp.dash));
    if (sp.push) parts.push('击退');
    if (sp.pierce) parts.push('无视地形');
    if ('chance' in P) parts.push('成功率' + pct(sp.chance));
    if ('heal' in P && sp.heal > 0) parts.push('回复' + pct(sp.heal));
    if ('atk' in P && sp.atk > 1) parts.push('攻+' + pct(sp.atk - 1));
    if ('def' in P && sp.def > 1) parts.push('防+' + pct(sp.def - 1));
    if ('counter' in P && sp.counter > 1) parts.push('反击+' + pct(sp.counter - 1));
    if ('drain' in P) parts.push('收编' + pct(sp.drain));
    if ('dot' in P) parts.push('每日' + pct(sp.dot));
    if ('count' in P) parts.push('再动' + sp.count);
    if ('move' in P && sp.move > 0) parts.push('机动+' + sp.move);
    if ('burn' in P && sp.burn > 0) parts.push('燃烧' + sp.burn + '日');
    if ('confuse' in P && sp.confuse > 0) parts.push('混乱' + pct(sp.confuse));
    if ('morale' in P && sp.morale !== 0) parts.push('士气' + signed(sp.morale));
    if (sp.cure) parts.push('解除混乱');
    if ('turns' in P && (sp.kind === 'scheme' || sp.kind === 'poison' || sp.kind === 'command' || sp.kind === 'fortify' || sp.atk > 1 || sp.confuse > 0)) parts.push(sp.turns + '日');
    if (statName) parts.push('依' + statName);
    return parts.join(' · ');
  }

  // ============================================================ 兜底生成器 --
  // 为没有手写条目的武将确定性地生成一招：机制取决于能力倾向，参数由能力强弱 + 姓名哈希决定，
  // 名称为「姓名·称号+招式」（含姓名，因此不会与他人或手写条目重名）。
  const EPI = {
    war: ['破阵', '断岳', '裂空', '贯日', '惊雷', '奔雷', '横江', '镇岳', '擎天', '烈风', '猛虎', '飞熊', '苍龙', '怒涛', '摧城', '踏营', '震天', '追风', '铁骑', '血战'],
    intel: ['玄机', '天机', '鬼谋', '神算', '连环', '奇门', '八阵', '星落', '天火', '幽冥', '迷踪', '乱心', '风云', '九宫', '运筹', '离火'],
    pol: ['仁风', '王道', '安民', '怀柔', '抚军', '同心', '济世', '春风', '清流', '礼贤', '德化', '归心'],
    nanman: ['蛮王', '象阵', '藤甲', '毒瘴', '烈山', '火鬃', '蛮荒', '兽王', '赤蛇', '百越'],
    wa: ['八咫', '天照', '勾玉', '镜光', '神风', '大和', '鬼道', '雷神'],
    yi: ['海潮', '飞羽', '贝珠', '云雾', '岛风'],
    korea: ['三足', '鸟羽', '金冠', '白头', '骏骑', '檀弓'],
    steppe: ['苍狼', '鸣镝', '天狼', '鹰扬', '白马', '狼骑', '朔风', '草原'],
    seasia: ['象王', '金饰', '雨林', '季风', '海神', '椰影'],
    tarim: ['沙海', '驼铃', '葱岭', '胡旋', '天山'],
    kushan: ['雪山', '金冠', '佛光', '大夏', '健陀'],
    persia: ['圣火', '不死', '铁甲', '太阳', '王中'],
    arab: ['沙暴', '弯月', '商旅', '绿洲', '沙漠'],
    roman: ['鹰旗', '军团', '铁壁', '凯旋', '雷霆', '元老'],
    celt: ['石阵', '橡叶', '战歌', '荒原', '蓝纹'],
    german: ['森林', '狂战', '铁斧', '寒霜', '狼牙'],
    sarmatian: ['长枪', '鳞甲', '草海', '铁骑', '龙旗'],
  };
  const NOUN = {
    smite: ['斩', '一击', '劈', '重击'], cleave: ['横扫', '旋斩', '回风斩'], charge: ['突击', '冲阵', '奔袭'], rampage: ['连斩', '乱舞', '突围'],
    volley: ['箭雨', '连弩', '齐射'], blaze: ['火攻', '焚营', '烈焰'], storm: ['天变', '风雷', '天罚'], flood: ['水攻', '决堤', '怒潮'],
    roar: ['怒吼', '断喝', '狮吼'], rally: ['鼓舞', '激励', '同心'], command: ['号令', '军令', '统御'], scheme: ['奇谋', '离间', '惑心'],
    drain: ['收编', '降服', '夺营'], fortify: ['坚守', '铁壁', '固阵'], haste: ['疾行', '神速', '急进'], heal: ['回春', '疗伤', '妙手'],
    assassinate: ['刺杀', '绝命', '暗刃'], poison: ['毒计', '毒雾', '鸩毒'],
  };
  const FLAVOR = { war: '奋起神威，', intel: '运筹帷幄，', pol: '德望所至，' };

  function lerp(a, b, t) { return a + (b - a) * M.clamp01(t); }
  // 每种机制的参数生成：s = 主能力强弱 0..1，r = 姓名种子的随机序列。数值落在平衡目标附近
  const GEN = {
    smite: (s, r) => { const range = r.next(5) === 0 ? 2 : 1; return { range, power: lerp(1.9, 2.35, s) - (range - 1) * 0.15, morale: -(8 + r.next(13)), pierce: r.next(3) === 0 ? 1 : 0, confuse: r.next(5) === 0 ? 0.15 + r.next(3) * 0.05 : 0, turns: 1 }; },
    cleave: (s, r) => ({ power: lerp(1.7, 1.95, s), splash: 0.5 + r.next(6) * 0.05, morale: -(6 + r.next(10)), confuse: r.next(6) === 0 ? 0.15 : 0, turns: 1 }),
    charge: (s, r) => ({ range: 2 + r.next(3), power: lerp(1.6, 1.85, s), dash: 0.08 + r.next(5) * 0.01, push: r.next(4) === 0 ? 0 : 1, morale: -(8 + r.next(10)) }),
    rampage: (s, r) => { const strikes = 3 + r.next(4); return { range: 1 + r.next(2) + (strikes >= 5 ? 1 : 0), strikes, power: lerp(2.0, 2.35, s) / strikes, morale: -(4 + r.next(7)) }; },
    volley: (s, r) => { const radius = r.next(3) === 0 ? 1 : 0; return { range: 3 + r.next(3), power: radius ? lerp(1.4, 1.65, s) : lerp(1.8, 2.2, s), radius, splash: 0.5 + r.next(5) * 0.05, pierce: r.next(4) === 0 ? 1 : 0, morale: -(4 + r.next(9)) }; },
    blaze: (s, r) => { const radius = r.next(4) === 0 ? 0 : 1; return { range: 3 + r.next(2), radius, power: radius ? lerp(1.25, 1.45, s) : lerp(1.5, 1.7, s), splash: 0.6 + r.next(5) * 0.05, burn: 1 + r.next(3), morale: -(6 + r.next(8)) }; },
    storm: (s, r) => ({ range: 4 + r.next(3), radius: r.next(3) === 0 ? 1 : 2, power: lerp(0.65, 0.85, s), burn: r.next(4) === 0 ? 1 : 0, confuse: r.next(3) === 0 ? 0.15 + r.next(3) * 0.05 : 0, turns: 1, morale: -(8 + r.next(8)) }),
    flood: (s, r) => ({ range: 3 + r.next(3), radius: r.next(4) === 0 ? 2 : 1, power: lerp(0.9, 1.1, s), confuse: r.next(4) === 0 ? 0.15 : 0, turns: 1, morale: -(10 + r.next(8)) }),
    roar: (s, r) => ({ radius: r.next(4) === 0 ? 1 : 2, power: lerp(0.3, 0.55, s), morale: -Math.round(lerp(18, 28, s)) - r.next(4), confuse: lerp(0.2, 0.4, s), turns: 1 }),
    rally: (s, r) => ({ radius: 1 + r.next(2), morale: Math.round(lerp(18, 30, s)) + r.next(4), heal: lerp(0.04, 0.1, s), cure: 1, atk: r.next(3) === 0 ? 1.1 : 1, turns: 2 }),
    command: (s, r) => ({ radius: 1 + r.next(2), atk: lerp(1.15, 1.28, s), def: 1 + r.next(3) * 0.05, turns: 2, morale: 5 + r.next(8) }),
    scheme: (s, r) => ({ range: 3 + r.next(3), radius: r.next(3) === 0 ? 0 : 1, chance: lerp(0.45, 0.6, s), turns: r.next(3) === 0 ? 1 : 2, morale: -(6 + r.next(8)), power: r.next(3) === 0 ? 0.2 : 0 }),
    drain: (s, r) => ({ range: 1, power: lerp(1.6, 1.95, s), drain: 0.35 + r.next(5) * 0.05, morale: -(8 + r.next(8)) }),
    fortify: (s, r) => ({ radius: r.next(3) === 0 ? 0 : 1, def: lerp(1.25, 1.45, s), counter: 1 + r.next(5) * 0.1, turns: 2, morale: 6 + r.next(8) }),
    haste: (s, r) => ({ radius: 1 + r.next(2), count: s > 0.6 ? 2 : 1 + r.next(2), move: r.next(2), morale: 3 + r.next(6) }),
    heal: (s, r) => ({ range: 1 + r.next(3), radius: r.next(3) === 0 ? 0 : 1, heal: lerp(0.12, 0.22, s), morale: 6 + r.next(8), cure: 1 }),
    assassinate: (s, r) => ({ range: 1 + r.next(2), chance: lerp(0.2, 0.32, s), power: 0.4 + r.next(4) * 0.1, morale: -(10 + r.next(10)) }),
    poison: (s, r) => ({ range: 3 + r.next(2), radius: r.next(2), power: lerp(0.25, 0.45, s), dot: lerp(0.04, 0.07, s), turns: 3, morale: -(5 + r.next(8)) }),
  };
  const STRENGTH = v => M.clamp01((v - 45) / 55);

  function pickKind(gen, r) {
    const W = gen.war | 0, I = gen.intel | 0, Pl = gen.pol | 0;
    let pool;
    if (W >= I + 10 && W >= Pl) pool = W >= 85 ? ['smite', 'cleave', 'charge', 'rampage', 'volley', 'roar', 'drain', 'smite', 'charge']
      : ['smite', 'cleave', 'charge', 'volley', 'roar', 'drain', 'assassinate', 'fortify'];
    else if (Math.abs(W - I) < 10 && W >= 60 && I >= 60) pool = ['charge', 'volley', 'command', 'scheme', 'fortify', 'haste', 'blaze'];
    else if (I >= Pl - 5) pool = I >= 85 ? ['blaze', 'storm', 'flood', 'scheme', 'poison', 'assassinate', 'blaze', 'scheme']
      : ['blaze', 'scheme', 'poison', 'flood', 'heal', 'haste'];
    else pool = ['rally', 'command', 'heal', 'fortify', 'haste'];
    return pool[r.next(pool.length)];
  }

  function fallback(gen) {
    const name = gen.name;
    const culture = cultureOf(gen);
    const r = SG.SeededRandom(hash(name + '|' + culture + '|' + (gen.war | 0) + '/' + (gen.intel | 0) + '/' + (gen.pol | 0)));
    const kind = pickKind(gen, r);
    const K = KINDS[kind];
    // 机制默认能力之外：政治系招式由智力更高者施展时依智力；暗杀由谋士施展时依智力（鸩毒、行刺）
    let stat = K.stat;
    if (K.stat === 'pol' && (gen.intel | 0) > (gen.pol | 0) + 10) stat = 'intel';
    if (kind === 'assassinate' && (gen.intel | 0) > (gen.war | 0)) stat = 'intel';
    const sv = stat === 'intel' ? gen.intel : stat === 'pol' ? gen.pol : gen.war;
    const e = GEN[kind](STRENGTH(sv | 0), r);
    // 细微的个人差异（让 (机制, 参数) 组合不易相同）
    if (num(e.power) && e.power > 0) e.power += (r.next(9) - 4) * 0.01;
    else if (num(e.chance)) e.chance += (r.next(7) - 3) * 0.01;
    else if (num(e.def)) e.def += (r.next(7) - 3) * 0.01;
    else if (num(e.atk)) e.atk += (r.next(7) - 3) * 0.01;
    for (const k of Object.keys(e)) { const p = K.params[k]; if (p) e[k] = p[3] >= 1 ? Math.round(M.clamp(e[k], p[1], p[2])) : fix(M.clamp(quant(e[k], p[3]), p[1], p[2])); }
    const bank = culture === 'han' ? EPI[stat] : (EPI[culture] || EPI[stat]);
    const noun = NOUN[kind][r.next(NOUN[kind].length)];
    let epi = bank[r.next(bank.length)];
    if (epi.length + noun.length > 5) epi = epi.slice(0, 1);
    const [h, s0, l0] = hexToHsl(K.color);
    const color = hslToHex(h + (r.next(41) - 20) / 360, M.clamp(s0 * (0.85 + r.next(4) * 0.05), 0.35, 1), M.clamp(l0 + (r.next(9) - 4) * 0.012, 0.45, 0.8));
    let fx = K.fx;
    if (kind === 'volley' && e.radius === 0) fx = 'arrow';
    if (kind === 'storm' && e.burn > 0) fx = 'wind';
    const entry = Object.assign({ name: name + '·' + epi + noun, kind, stat, color, fx }, e);
    entry.desc = (FLAVOR[stat] || '') + explain(normalize(entry, name, true));
    return entry;
  }

  // ============================================================ 解析与缓存 --
  const cache = new Map();       // 武将名 → 冻结条目
  const sigOwner = new Map();    // 签名 → 武将名
  let dataVersion = -1;
  function data() { return SG.SpecialsData || null; }
  function syncData() {
    const D = data();
    const v = D ? D.version : 0;
    if (v === dataVersion) return;
    dataVersion = v;
    cache.clear(); sigOwner.clear();
    if (!D) return;
    for (const n of D.names()) {
      const sp = normalize(D.get(n), n, false);
      cache.set(n, sp);
      const sig = signature(sp);
      if (!sigOwner.has(sig)) sigOwner.set(sig, n);
    }
  }

  function of(gen) {
    if (!gen || !gen.name) return null;
    syncData();
    const hit = cache.get(gen.name);
    if (hit) return hit;
    const e = fallback(gen);
    let sp = normalize(e, gen.name, true);
    // 与他人（含手写条目）的 (机制, 参数) 撞车时，按确定的步长微调威力 / 成功率直到唯一
    const K = KINDS[sp.kind];
    const tweak = ['power', 'chance', 'def', 'atk', 'heal', 'morale'].find(k => k in K.params && (k !== 'power' || sp.power > 0)) || 'morale';
    for (let i = 1; i < 60 && sigOwner.has(signature(sp)) && sigOwner.get(signature(sp)) !== gen.name; i++) {
      const p = K.params[tweak];
      const step = (p[3] >= 1 ? 1 : 0.01) * (i % 2 ? Math.ceil(i / 2) : -Math.ceil(i / 2));
      const v = M.clamp(e[tweak] + step, p[1], p[2]);
      sp = normalize(Object.assign({}, e, { [tweak]: p[3] >= 1 ? Math.round(v) : fix(v) }), gen.name, true);
    }
    cache.set(gen.name, sp);
    sigOwner.set(signature(sp), gen.name);
    return sp;
  }

  // 名单：SG.G.generals，或剧本武将表（"姓名|武|智|政|势力|…"）
  function roster(gens) {
    if (Array.isArray(gens)) return gens;
    if (SG.G && Array.isArray(SG.G.generals) && SG.G.generals.length) return SG.G.generals;
    const SD = SG.ScenarioData;
    if (SD && Array.isArray(SD.Generals)) {
      return SD.Generals.map(line => {
        const p = String(line).split('|');
        return { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], factionKey: p[4] };
      });
    }
    return [];
  }

  function all(gens) {
    syncData();
    const list = roster(gens);
    const seen = new Set();
    const out = [];
    for (const g of list) { if (!g || !g.name || seen.has(g.name)) continue; seen.add(g.name); out.push(of(g)); }
    const D = data();
    if (D) for (const n of D.names()) if (!seen.has(n)) { seen.add(n); out.push(cache.get(n)); }
    return out;
  }

  function check(gens) {
    const list = all(gens);
    const byName = new Map(), bySig = new Map();
    for (const sp of list) {
      if (!byName.has(sp.name)) byName.set(sp.name, []);
      byName.get(sp.name).push(sp.gen);
      const s = signature(sp);
      if (!bySig.has(s)) bySig.set(s, []);
      bySig.get(s).push(sp.gen);
    }
    const dupNames = [...byName].filter(([, v]) => v.length > 1).map(([k, v]) => ({ name: k, gens: v }));
    const dupSigs = [...bySig].filter(([, v]) => v.length > 1).map(([k, v]) => ({ sig: k, gens: v }));
    const problems = [];
    for (const sp of list) for (const p of sp.problems) problems.push(p);
    const roster0 = roster(gens);
    const missing = roster0.filter(g => g && g.name && !of(g)).map(g => g.name);
    const hand = list.filter(sp => !sp.auto).length;
    return {
      total: list.length, hand, auto: list.length - hand, dupNames, dupSigs, problems, missing,
      ok: dupNames.length === 0 && dupSigs.length === 0 && problems.length === 0 && missing.length === 0,
    };
  }

  // ============================================================ 控制层辅助 --
  function sfx(name, vol) { try { if (SG.Sfx && SG.Sfx.play) SG.Sfx.play(name, vol === undefined ? 0.8 : vol); } catch (e) { /* 无音频 */ } }
  function toast(html, sec) { try { if (SG.UI && SG.UI.toast) SG.UI.toast(html, sec || 1.6); } catch (e) { /* 无界面 */ } }
  function call(obj, fn, ...args) { if (obj && typeof obj[fn] === 'function') { try { return obj[fn](...args); } catch (e) { console.error(e); } } return undefined; }
  function esc(s) { return SG.esc ? SG.esc(s) : String(s); }
  function wait(s) { return SG.wait ? SG.wait(s) : new Promise(r => setTimeout(r, s * 1000)); }
  function uiItem(label, right, enabled, desc) {
    if (SG.UI && SG.UI.item) return SG.UI.item(label, right, enabled, desc);
    return { label, right, enabled, desc, selected: false };
  }

  function menuItem(bc, u) {
    const sp = of(u.gen);
    const us = bc.M.specialUsable(u);
    if (!sp) return uiItem('必杀', '<span class="sg-muted">无</span>', false, null);
    const nm = '<span style="color:' + uiColor(sp) + '">「' + esc(sp.name) + '」</span>';
    return uiItem('必杀', us.ok ? nm : nm + ' <span class="sg-muted">' + esc(us.why) + '</span>', us.ok, esc(sp.desc));
  }

  function cardLine(u) {
    const sp = u && of(u.gen);
    if (!sp) return '';
    return '<div>必杀 <span style="color:' + uiColor(sp) + '">「' + esc(sp.name) + '」</span>' +
      (u.specialUsed ? '<span class="sg-muted">　已施展</span>' : '<span class="sg-good">　可用</span>') + '</div>';
  }

  // 选择目标：沿用控制器的 pickEnemy（只有一个目标时直接返回），并在棋盘上标出射程与可选目标
  async function pickTarget(bc, u) {
    const Mdl = bc.M, V = bc.V;
    const sp = of(u.gen);
    const list = Mdl.specialTargets(u);
    if (!sp || list.length === 0) return null;
    if (!needsTarget(sp)) return u;
    if (V && V.highlight) {
      try {
        V.clearHighlights();
        const ring = [];
        for (let x = 0; x < Mdl.W; x++) for (let y = 0; y < Mdl.H; y++) {
          const d = Math.abs(x - u.x) + Math.abs(y - u.y);
          if (d > 0 && d <= Math.max(1, sp.range) && !list.some(o => o.x === x && o.y === y)) ring.push({ x, y });
        }
        V.highlight(ring, sp.color, 0.28);
        V.highlight(list.map(o => ({ x: o.x, y: o.y })), targetSide(sp) === 'ally' ? '#73d98c' : '#ff4d40', 0.75);
      } catch (e) { console.error(e); }
    }
    let tgt = null;
    try {
      if (list.length === 1) tgt = list[0];
      else if (typeof bc.pickEnemy === 'function') tgt = await bc.pickEnemy(list, sp.name + ' 的目标');
      else tgt = list[0];
    } finally {
      if (V && V.clearHighlights) V.clearHighlights();
    }
    return tgt || null;
  }

  async function menu(bc, u, moved) {
    const Mdl = bc.M;
    const us = Mdl.specialUsable(u);
    if (!us.ok) { toast(us.why); if (moved) Mdl.spend(u); return null; }
    const tgt = await pickTarget(bc, u);
    if (!tgt) { if (moved) Mdl.spend(u); return null; }
    const res = await perform(bc, u, tgt);
    Mdl.spend(u);
    return res;
  }

  // 特效要照顾的部队（主目标在前）
  function fxTargets(res, u) {
    const out = [];
    const add = x => { if (x && out.indexOf(x) < 0) out.push(x); };
    if (res.target && res.target !== u) add(res.target);
    for (const h of res.hits) add(h.unit);
    for (const x of res.affected) add(x);
    if (out.length === 0) add(res.target || u);
    return out;
  }

  async function perform(bc, u, target, opts) {
    opts = opts || {};
    const Mdl = bc && bc.M, V = bc && bc.V;
    if (!Mdl || !u) return null;
    const sp = of(u.gen);
    const us = Mdl.specialUsable(u);
    if (!sp || !us.ok) { toast(esc(us.why || '无法施展')); return null; }
    const cands = Mdl.specialTargets(u);
    if (!target || cands.indexOf(target) < 0) target = cands[0];
    call(bc, 'showCard', u);
    if (V && V.setCursor) V.setCursor(u);
    // 1. 特写
    if (opts.cutIn !== false) {
      const fast = !!opts.fast || !!(SG.Clash && SG.Clash.speed > 1);
      try {
        if (SG.Clash && typeof SG.Clash.cutIn === 'function' && SG.Clash.enabled !== false) {
          await SG.Clash.cutIn({ gen: u.gen, name: sp.name, color: uiColor(sp), side: u.side, cry: sp.cry, special: sp, speed: fast ? 1.6 : 1 });
        } else await cutIn({ gen: u.gen, name: sp.name, color: uiColor(sp), side: u.side, cry: sp.cry, fast });
      } catch (e) { console.error(e); }
    }
    toast(esc(u.gen.name) + '施展必杀「<span style="color:' + uiColor(sp) + '">' + esc(sp.name) + '</span>」！', 1.6);
    // 2. 结算
    const res = Mdl.useSpecial(u, target);
    if (!res.sp) return res;
    // 镜头推近：施展者与主目标的中点；部队名牌暂时淡出，让出画面
    const rig = opts.rig || (SG.Game && SG.Game.rig) || null;
    let rigBack = null;
    if (rig && V && V.tile && opts.zoom !== false && typeof rig.focusWorld === 'function') {
      try {
        const a = V.tile(u.x, u.y), b = V.tile((res.target || u).x, (res.target || u).y);
        const mid = a.clone().lerp(b, 0.5);
        rigBack = rig.desiredDistance;
        const span = a.distanceTo(b) + (needsTarget(sp) ? 0 : 2 * Math.max(1, sp.radius || 1) * 2);
        rig.focusWorld(mid, M.clamp(Math.max(18, span * 2.2), rig.minDist || 12, rigBack));
      } catch (e) { console.error(e); rigBack = null; }
    }
    injectStyle();
    const lbl = SG.UI && SG.UI.layers && SG.UI.layers.labels;
    if (lbl) lbl.classList.add('sg-spfx-on');
    // 3. 特效（specialFx 在每次命中时回调 onHit(i)，显示该次伤害）
    const shown = res.hits.map(() => false);
    const onHit = i => {
      if (!V || i < 0 || i >= res.hits.length || shown[i]) return;
      shown[i] = true;
      const h = res.hits[i];
      try { V.hit(h.unit, h.dmg, true); } catch (e) { console.error(e); }
    };
    if (V) {
      try {
        if (typeof V.specialFx === 'function') await V.specialFx(u, fxTargets(res, u), sp.fx, { sp, res, color: sp.color, model: Mdl, onHit, target });
        else if (typeof V.magicFx === 'function') await V.magicFx(target || u, sp.color);
      } catch (e) { console.error(e); }
      // 突击 / 击退后的位置（特效未处理时直接归位）
      const moved = [];
      if (res.moved) moved.push(u);
      for (const p of res.pushed) moved.push(p.unit);
      for (const x of moved) {
        const v = V.vis && V.vis(x);
        if (v && v.pos && V.tile) { const t = V.tile(x.x, x.y); if (v.pos.distanceTo(t) > 0.05) v.pos.copy(t); }
      }
      // 4. 飘字
      const T3 = typeof THREE !== 'undefined' ? THREE : null;
      const above = (x, k) => { const p = V.tile(x.x, x.y); if (T3) p.add(new T3.Vector3(0, k || 1.6, 0)); return p; };
      const text = (x, s, col, size, k) => { try { V.floatText(above(x, k), s, col, size || 34); } catch (e) { console.error(e); } };
      let n = 0;
      for (let i = 0; i < res.hits.length; i++) if (!shown[i]) { if (n++ > 0) await wait(0.06); onHit(i); }
      if (res.killed) text(res.killed, '一击毙命', '#ff5a5a', 46, 2.3);
      else if (res.kind === 'assassinate' && !res.success && res.target) text(res.target, '失手', '#a8a194', 36, 2.3);
      for (const x of res.confused) text(x, '混乱', '#cc99ff', 38, 2.4);
      for (const x of res.resisted) text(x, '识破', '#a8a194', 32, 2.4);
      for (const x of res.cursed) text(x, '中毒', '#9be06a', 34, 2.6);
      for (const h of res.healed) text(h.unit, '+' + h.amount, '#73d98c', 40, 1.6);
      const buffWord = { fortify: '坚守', command: '攻防提升', rally: '攻击提升', haste: '机动 +' + sp.move }[res.kind];
      for (const x of res.buffed) if (buffWord) text(x, buffWord, res.kind === 'fortify' ? '#8cc8ff' : '#f3c969', 32, 2.3);
      if (res.kind === 'rally') for (const x of res.affected) if (res.buffed.indexOf(x) < 0 && x.side === u.side) text(x, '士气高涨', '#73d98c', 32, 2.3);
      for (const x of res.refreshed) text(x, '再动！', '#7affd9', 38, 2.6);
      if (res.gained > 0) text(u, '+' + res.gained + ' 收编', '#73d98c', 36, 2.4);
      if (res.blocked && res.blocked.alive) text(res.blocked, '撞击', '#ffb04a', 30, 2.5);
    }
    if (lbl) lbl.classList.remove('sg-spfx-on');
    if (rig && rigBack !== null) rig.desiredDistance = rigBack;
    // 5. 刷新
    if (V) for (const x of Mdl.units) call(V, 'refresh', x);
    call(bc, 'updateHud');
    call(bc, 'showCard', u.alive ? u : null);
    if (res.refreshed.length) toast(res.refreshed.map(x => esc(x.gen.name)).join('、') + ' 可再次行动', 1.8);
    if (res.killed) toast(esc(res.killed.gen.name) + '遭刺杀，全军溃散！', 2);
    await wait(0.4);
    // 6. 溃散
    const gone = [];
    for (const x of res.routed.concat(res.affected)) if (x && !x.alive && gone.indexOf(x) < 0) gone.push(x);
    for (const x of gone) {
      if (V && V.isShown && V.isShown(x)) {
        if (!res.killed || x !== res.killed) toast(esc(x.gen.name) + '部溃散！');
        await V.rout(x);
      }
    }
    return res;
  }

  // ------------------------------------------------------------ 简易特写 --
  // 没有 SG.Clash 时使用：斜向色带 + 头像（或姓氏徽章）+ 招式名大字 + 台词，约 1.1 秒
  function injectStyle() {
    if (typeof document === 'undefined' || document.getElementById('sg-specials-style')) return;
    const s = document.createElement('style');
    s.id = 'sg-specials-style';
    s.textContent = `
.sg-spfx-on .sg-uinfo,.sg-spfx-on .sg-stem{opacity:.16 !important;}
.sg-spcut{position:fixed;inset:0;z-index:5;pointer-events:auto;cursor:pointer;overflow:hidden;-webkit-tap-highlight-color:transparent;--c:#f3c969;--cg:rgba(243,201,105,.5);--dur:1.1s;}
.sg-spcut-dim{position:absolute;inset:0;background:radial-gradient(ellipse at 50% 50%,rgba(0,0,0,.1),rgba(0,0,0,.6));opacity:0;animation:sg-spcut-dim var(--dur) ease forwards;}
.sg-spcut-band{position:absolute;left:-12%;right:-12%;top:50%;height:clamp(92px,30vh,230px);margin-top:calc(clamp(92px,30vh,230px) / -2);
  transform:skewY(-6deg) scaleY(0);transform-origin:50% 50%;
  background:linear-gradient(90deg,rgba(8,8,14,0) 0%,rgba(8,8,14,.93) 14%,rgba(14,12,20,.95) 50%,rgba(8,8,14,.93) 86%,rgba(8,8,14,0) 100%);
  border-top:3px solid var(--c);border-bottom:3px solid var(--c);box-shadow:0 0 36px var(--cg),inset 0 0 60px rgba(0,0,0,.6);
  animation:sg-spcut-band var(--dur) cubic-bezier(.2,.9,.25,1) forwards;}
.sg-spcut-band::before{content:"";position:absolute;inset:0;opacity:.35;
  background:repeating-linear-gradient(90deg,transparent 0 46px,var(--cg) 46px 48px,transparent 48px 120px);
  animation:sg-spcut-lines .5s linear infinite;}
.sg-spcut-band::after{content:"";position:absolute;inset:0;background:linear-gradient(90deg,transparent 30%,rgba(255,255,255,.22) 50%,transparent 70%);
  transform:translateX(-100%);animation:sg-spcut-shine var(--dur) ease-out forwards;}
.sg-spcut-row{position:absolute;left:0;right:0;top:50%;transform:translate(-40vw,-50%) skewY(-6deg);opacity:0;display:flex;align-items:center;justify-content:center;gap:clamp(12px,3vw,40px);
  animation:sg-spcut-row var(--dur) cubic-bezier(.15,.85,.3,1) forwards;}
.sg-spcut-face{flex:none;width:clamp(64px,20vh,150px);height:clamp(64px,20vh,150px);border-radius:50%;overflow:hidden;display:flex;align-items:center;justify-content:center;
  border:3px solid var(--c);box-shadow:0 0 22px var(--cg);background:radial-gradient(circle at 40% 35%,#3a3444,#14121a);
  font:700 clamp(34px,11vh,84px)/1 "STKaiti","Kaiti SC","KaiTi","Songti SC","Noto Serif SC",serif,"WenQuanYi Zen Hei";color:#f5eddb;text-shadow:0 2px 6px rgba(0,0,0,.8);}
.sg-spcut-face img,.sg-spcut-face .sg-portrait{width:100%;height:100%;display:block;object-fit:cover;}
.sg-spcut-text{display:flex;flex-direction:column;align-items:flex-start;min-width:0;}
.sg-spcut-who{font:600 clamp(13px,3.2vh,22px)/1.2 "PingFang SC","Microsoft YaHei","Noto Sans SC",system-ui,sans-serif,"WenQuanYi Zen Hei";color:#f5eddb;letter-spacing:.2em;opacity:.9;}
.sg-spcut-name{font:700 clamp(34px,11vh,96px)/1.08 "STKaiti","Kaiti SC","KaiTi","Songti SC","Noto Serif SC",serif,"WenQuanYi Zen Hei";
  color:var(--c);letter-spacing:.08em;white-space:nowrap;
  text-shadow:0 0 18px var(--cg),0 3px 0 rgba(0,0,0,.85),0 0 2px #fff;}
.sg-spcut-cry{font:500 clamp(12px,2.8vh,20px)/1.3 "STKaiti","Kaiti SC","KaiTi","Songti SC","Noto Serif SC",serif,"WenQuanYi Zen Hei";color:#e8dcc0;margin-top:.25em;max-width:62vw;}
@keyframes sg-spcut-dim{0%{opacity:0}12%{opacity:1}80%{opacity:1}100%{opacity:0}}
@keyframes sg-spcut-band{0%{transform:skewY(-6deg) scaleY(0)}12%{transform:skewY(-6deg) scaleY(1.08)}20%{transform:skewY(-6deg) scaleY(1)}80%{transform:skewY(-6deg) scaleY(1);opacity:1}100%{transform:skewY(-6deg) scaleY(0);opacity:0}}
@keyframes sg-spcut-row{0%{transform:translate(-40vw,-50%) skewY(-6deg);opacity:0}18%{transform:translate(0,-50%) skewY(-6deg);opacity:1}
  78%{transform:translate(2vw,-50%) skewY(-6deg);opacity:1}100%{transform:translate(45vw,-50%) skewY(-6deg);opacity:0}}
@keyframes sg-spcut-lines{from{background-position:0 0}to{background-position:-120px 0}}
@keyframes sg-spcut-shine{0%,15%{transform:translateX(-100%)}55%,100%{transform:translateX(100%)}}
@media (max-height:540px){.sg-spcut-cry{max-width:70vw}}
`;
    (document.head || document.documentElement).appendChild(s);
  }
  function rgba(hex, a) { const c = SG.hexToRgb(hex || '#f3c969'); return 'rgba(' + Math.round(c[0] * 255) + ',' + Math.round(c[1] * 255) + ',' + Math.round(c[2] * 255) + ',' + a + ')'; }

  function cutIn(o) {
    o = o || {};
    if (typeof document === 'undefined') return Promise.resolve();
    injectStyle();
    const dur = o.fast ? 0.75 : 1.1;
    const root = document.createElement('div');
    root.className = 'sg-spcut side' + (o.side === 1 ? 1 : 0);
    const col = /^#[0-9a-f]{6}$/i.test(String(o.color || '')) ? o.color : '#f3c969';
    root.style.setProperty('--c', col);
    root.style.setProperty('--cg', rgba(col, 0.55));
    root.style.setProperty('--dur', dur + 's');
    const gen = o.gen || {};
    const face = document.createElement('div');
    face.className = 'sg-spcut-face';
    let pic = null;
    try { if (SG.Portrait && typeof SG.Portrait.el === 'function') pic = SG.Portrait.el(gen, { size: 150, frame: false, mood: 'angry' }); } catch (e) { pic = null; }
    if (pic) face.appendChild(pic); else face.textContent = String(gen.name || '?').substring(0, 1);
    root.innerHTML = '<div class="sg-spcut-dim"></div><div class="sg-spcut-band"></div>';
    const row = document.createElement('div');
    row.className = 'sg-spcut-row';
    const txt = document.createElement('div');
    txt.className = 'sg-spcut-text';
    txt.innerHTML = '<div class="sg-spcut-who">' + esc(gen.name || '') + '　必杀</div><div class="sg-spcut-name">' + esc(o.name || '') + '</div>' +
      (o.cry ? '<div class="sg-spcut-cry">「' + esc(o.cry) + '」</div>' : '');
    row.appendChild(face); row.appendChild(txt);
    root.appendChild(row);
    // 放在界面的 screens 层（在弹窗 / 提示之下，DESIGN-V2 §2）；没有 SG.UI 时退回 body（z-index 5，仍低于 #ui）
    let layer = null;
    try { layer = SG.UI && typeof SG.UI.layer === 'function' ? SG.UI.layer('screens') : null; } catch (e) { layer = null; }
    if (layer) {   // screens 层避开了安全区；特写要铺满整屏，向外撑回安全区
      root.style.position = 'absolute';
      root.style.inset = 'calc(-1 * var(--sg-sat, 0px)) calc(-1 * var(--sg-sar, 0px)) calc(-1 * var(--sg-sab, 0px)) calc(-1 * var(--sg-sal, 0px))';
    }
    (layer || document.body).appendChild(root);
    sfx('horn', 0.55);
    setTimeout(() => sfx('duel', 0.7), 160);
    // 点击 / 触摸 / 任意键跳过（开头 0.15 秒内不接受，以免选目标的那一下直接跳过）
    return new Promise(res => {
      let done = false, armed = false;
      const finish = () => {
        if (done) return;
        done = true;
        clearTimeout(timer);
        root.removeEventListener('pointerdown', onTap);
        window.removeEventListener('keydown', onKey, true);
        if (root.parentNode) root.parentNode.removeChild(root);
        res();
      };
      const onTap = e => { e.preventDefault(); e.stopPropagation(); if (armed) finish(); };
      const onKey = e => { if (armed && !e.repeat) finish(); };
      root.addEventListener('pointerdown', onTap);
      window.addEventListener('keydown', onKey, true);
      setTimeout(() => { armed = true; }, 150);
      const timer = setTimeout(finish, dur * 1000);
    });
  }

  SG.Specials = {
    KINDS, FX, FLOOD_RIVER, FLOOD_DRY, CHARGE_CAP, AREA_TOTAL,
    of, all, check, validate, signature, needsTarget, targetSide, explain, rules, kindName, cultureOf, uiColor,
    fallback(gen) { return normalize(fallback(gen), gen.name, true); },
    menuItem, cardLine, pickTarget, menu, perform, cutIn,
  };
})();
