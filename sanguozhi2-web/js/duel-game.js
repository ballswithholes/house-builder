'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 单挑格斗（DESIGN-V2 §4E）

   单挑 = 一场横版 2.5D 格斗：独立的三维场景（SG.Gfx.pushScreen），两名程序化建模的
   低多边形武将，程序化关键帧动画，键盘 / 多点触控操作，攻击力与伤害取决于 武力。

   公开接口
   --------
   const res = await SG.DuelGame.play({
     a: { gen, side: 0, color, culture },        // a = 挑战者（画面左侧）
     b: { gen, side: 1, color, culture },        // b = 应战者（画面右侧）
     playerSide: 0 | 1 | null,                   // 玩家操作哪一方（0 = a，1 = b）；null = 电脑对电脑（观战，可跳过）
     terrain,                                    // SG.Terrain 值：平原 / 森林 / 丘陵 / 山地 / 河川 / 城墙 / 城门 / 本城
     special: { a: SG.Specials?.of(a.gen), b: … }// 可选：绝技名称与配色 { name, color }
   })
   // → { winner: 0 | 1（0 = a 胜）, kind: 'ko' | 'time' | 'skip', hpA, hpB, log: [{ who, dmg, hpA, hpB, move, guarded, t }] }
   //   log 的每一项兼容 BattleModel.duel 的 rounds（who / dmg / hpA / hpB），可直接交给 Mdl.duelFinish。

   SG.DuelGame.auto        // 测试用：true 时玩家一方也由电脑操作（URL 参数 ?duelauto=1 等效）
   SG.DuelGame.speed       // 测试用：时间倍率（默认 1）
   SG.DuelGame.active      // 正在进行的单挑（调试 / 测试：.sim 为格斗逻辑）或 null
   SG.DuelGame.simulate(genA, genB, opts?)   // 不渲染、电脑对电脑快速模拟一整场（平衡测试），返回同 play 的结果
   SG.DuelGame.lookOf(gen, culture?)         // 外观与兵器设定（调试用）
   SG.DuelGame.damage(base, war, foeWar)     // 伤害公式（见下）

   规则（DESIGN-V2 §4E）
   ---------------------
   体力 100；每击伤害 = 基础 × (0.55 + 武力/100) × (1 + (己武力 − 敌武力)/150)（后一项限制在 0.6–1.5，
   单击 1–40）；高武力出招略快、硬直略短；格挡减伤 80%；怒气随命中 / 受击积累，满时可放绝技；
   限时 60 秒，KO 或时间到时体力高者胜（相同则挑战者胜，与 BattleModel.duel 一致）。
   电脑的反应时间、格挡率、连段与绝技使用随其武力提升。

   操作：←/→ 或 A/D 移动（双击冲刺）、↑/W/空格 跳、J 轻击（连按三连击）、K 重击（按住蓄力，满蓄破防）、
   L 格挡（↓/S 亦可）、I 绝技（怒气满）。触摸：左下 ◀ ▶ ▲，右下 轻击 / 重击 / 格挡 / 绝技，支持多点触控。

   结构：Sim（纯逻辑，固定 60Hz 步长，可在 Node 中运行）+ AI + 视图（场景、武将模型与动画、特效）+ HUD（DOM）+ 输入。
   规则相关的随机数一律走 SG.Random；纯装饰（粒子等）用 SG.SeededRandom。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;
  const rnd = () => SG.Random.value();

  // ============================================================ 常量 --
  const DT = 1 / 60;               // 逻辑步长
  const ARENA = 7.2;               // 场地半宽（米）
  const BODY = 0.4;                // 推挤半径
  const GRAV = 26;
  const JUMP_V = 7.4;
  const FRICTION = 14;
  const BUFFER = 0.16;             // 输入缓冲（秒）
  const TIME_LIMIT = 60;
  const CHARGE_MAX = 0.85;         // 满蓄所需时间
  const SPECIAL_FREEZE = 0.62;     // 绝技起手时对手定格的时间

  // 招式时序（基准秒；出招时钟按 spd 缩放）。reachK × 兵器长度 = 判定距离
  const MOVES = {
    light1: { kind: 'light', startup: 0.10, active: 0.07, recovery: 0.19, dmg: 2.4, stun: 0.30, push: 2.6, reachK: 0.92, next: 'light2', hitstop: 0.065, hmax: 0.85 },
    light2: { kind: 'light', startup: 0.09, active: 0.07, recovery: 0.21, dmg: 2.4, stun: 0.32, push: 2.9, reachK: 0.95, next: 'light3', hitstop: 0.07, hmax: 0.85 },
    light3: { kind: 'light', startup: 0.15, active: 0.09, recovery: 0.44, dmg: 4, stun: 0.46, push: 5.0, reachK: 1.0, hitstop: 0.10, hmax: 1.45, big: true, lunge: 1.8 },
    heavy: { kind: 'heavy', startup: 0.09, active: 0.10, recovery: 0.40, dmg: 4.8, dmgCharge: 5, stun: 0.5, push: 4.6, reachK: 1.08, hitstop: 0.12, hmax: 1.5, big: true, lunge: 2.2 },
    air: { kind: 'air', startup: 0.05, active: 0.20, recovery: 0.10, dmg: 3, stun: 0.36, push: 3.0, reachK: 0.88, hitstop: 0.08, hmax: 9 },
    // 绝技：定格起手 → 突进 → 三段斩 + 终结一击（倒地）
    special: { kind: 'special', startup: SPECIAL_FREEZE + 0.1, active: 0.62, recovery: 0.45, dmg: 4.5, finisher: 7, stun: 0.5, push: 1.6, reachK: 1.0, hitstop: 0.09, hmax: 9, big: true,
      hits: [0.0, 0.17, 0.34, 0.56] },
  };
  const ATTACKS = new Set(['light1', 'light2', 'light3', 'heavy', 'air', 'special']);
  const STYLE_SPD = { pole: 1.0, one: 1.14, dual: 1.18 };

  // ============================================================ 数值 --
  function warOf(gen) { const w = Number(gen && gen.war); return M.clamp(isFinite(w) ? w : 50, 1, 120); }
  // 每击伤害 = 基础 × (0.55 + 武力/100) × (1 + (己武力 − 敌武力)/150)
  function damage(base, war, foeWar) {
    const k = M.clamp(1 + (war - foeWar) / 150, 0.6, 1.5);
    return base * (0.55 + war / 100) * k;
  }
  function actSpeed(war) { return 0.9 + M.clamp(war, 0, 110) / 100 * 0.22; }   // 出招速度（武力 100 → 1.12）
  function stunK(war) { return 1.12 - M.clamp(war, 0, 110) / 100 * 0.2; }      // 受击硬直倍率（武力 100 → 0.92）
  function walkSpeed(war) { return 2.3 * (0.92 + M.clamp(war, 0, 110) / 100 * 0.16); }

  // ============================================================ 兵器与外观 --
  // style：pole 双手长兵 / one 单手兵 / dual 双持；reach 判定距离（米）；pow 兵器威力系数（缺省 1，重兵器略高，用以抵消出手较慢）
  const WEAPONS = {
    guandao: { name: '青龙偃月刀', style: 'pole', reach: 2.0, pow: 1.05 },
    snake: { name: '丈八蛇矛', style: 'pole', reach: 2.05 },
    ji: { name: '方天画戟', style: 'pole', reach: 2.0, pow: 1.06 },
    spear: { name: '长枪', style: 'pole', reach: 2.05 },
    poleblade: { name: '大刀', style: 'pole', reach: 1.95, pow: 1.07 },
    axe: { name: '大斧', style: 'pole', reach: 1.88, pow: 1.12 },
    bigblade: { name: '斩马刀', style: 'pole', reach: 1.88, pow: 1.14 },
    sword: { name: '长剑', style: 'one', reach: 1.78 },
    dao: { name: '环首刀', style: 'one', reach: 1.75 },
    shortji: { name: '短戟', style: 'one', reach: 1.8 },
    mace: { name: '骨朵', style: 'one', reach: 1.72, pow: 0.97 },
    twinsword: { name: '双股剑', style: 'dual', reach: 1.72, pow: 0.96 },
    twinji: { name: '双铁戟', style: 'dual', reach: 1.75, pow: 0.97 },
    twindao: { name: '双刀', style: 'dual', reach: 1.68 },
    // 异域兵器（DESIGN-V2 §6）
    gladius: { name: '短剑', style: 'one', reach: 1.66, pow: 1.03 },        // 罗马
    lance: { name: '长矛', style: 'pole', reach: 2.12 },                    // 萨尔马提亚 / 安息 / 贵霜的骑矛
    handaxe: { name: '战斧', style: 'one', reach: 1.7, pow: 1.03 },        // 日耳曼
    tachi: { name: '直刀', style: 'one', reach: 1.76 },                     // 倭
  };

  // 知名武将的外观（未列出的按能力值与姓名哈希确定性生成）
  // helm：han 兜鍪 / plume 白缨盔 / crown 金冠盔 / pheasant 雉翎冠 / lion 狮盔 / scarf 头巾 / bald 光头 / guan 纶巾 / topknot 椎髻（南中）……
  // beard：none / short / long / bushy / stubble；armor / metal / cape / robe / skin / hair 为颜色
  const LOOKS = {
    '关羽': { weapon: 'guandao', helm: 'scarf', helmColor: '#2f7d4a', robe: '#2f7d4a', skin: '#b4503c', beard: 'long', bulk: 1.06, height: 1.06, cape: '#24653b', metal: '#d4b25a' },
    '张飞': { weapon: 'snake', helm: 'han', armor: '#2c2c33', metal: '#55565e', beard: 'bushy', skin: '#9c7258', bulk: 1.16, cape: '#3a1f1a' },
    '吕布': { weapon: 'ji', helm: 'pheasant', metal: '#e0b048', armor: '#a3302a', cape: '#b8302a', bulk: 1.08, height: 1.07, beard: 'none' },
    '刘备': { weapon: 'twinsword', helm: 'crown', beard: 'short', metal: '#d8b860' },
    '赵云': { weapon: 'spear', weaponName: '涯角枪', helm: 'plume', plume: '#f4f4f4', armor: '#e4e6ec', metal: '#c8d0da', cape: '#eef0f4', beard: 'none' },
    '马超': { weapon: 'spear', weaponName: '虎头湛金枪', goldTip: true, helm: 'lion', armor: '#e2e4ea', metal: '#d0d6de', cape: '#f2f2f6', beard: 'none' },
    '典韦': { weapon: 'twinji', helm: 'scarf', helmColor: '#c9a23a', skin: '#8e6448', beard: 'bushy', bulk: 1.2, bareArms: true },
    '许褚': { weapon: 'bigblade', helm: 'bald', bare: true, bulk: 1.24, belly: true, beard: 'stubble', skin: '#c99a74' },
    '黄忠': { weapon: 'poleblade', weaponName: '凤嘴刀', bow: true, beard: 'long', hair: '#e8e4dc', helm: 'han' },
    '孙策': { weapon: 'spear', weaponName: '霸王枪', helm: 'plume', plume: '#d43a2a', beard: 'none' },
    '孙坚': { weapon: 'dao', weaponName: '古锭刀', helm: 'scarf', helmColor: '#c8382c', beard: 'short' },
    '太史慈': { weapon: 'shortji', helm: 'han', beard: 'short' },
    '夏侯惇': { weapon: 'spear', helm: 'han', eyepatch: true, beard: 'short' },
    '夏侯渊': { weapon: 'dao', bow: true, beard: 'short' },
    '张辽': { weapon: 'poleblade', weaponName: '钩镰刀', beard: 'short' },
    '徐晃': { weapon: 'axe', weaponName: '大斧', beard: 'short' },
    '潘凤': { weapon: 'axe', weaponName: '开山大斧', beard: 'bushy' },
    '颜良': { weapon: 'poleblade', beard: 'bushy', bulk: 1.1 },
    '文丑': { weapon: 'spear', beard: 'bushy', bulk: 1.1 },
    '华雄': { weapon: 'poleblade', bulk: 1.12, beard: 'bushy' },
    '庞德': { weapon: 'poleblade', beard: 'short' },
    '魏延': { weapon: 'poleblade', skin: '#a8705a', beard: 'short' },
    '周仓': { weapon: 'poleblade', skin: '#7e5a40', beard: 'bushy', bulk: 1.1 },
    '关平': { weapon: 'poleblade', beard: 'none' },
    '甘宁': { weapon: 'dao', bells: true, beard: 'short' },
    '凌统': { weapon: 'twindao', beard: 'none' },
    '周泰': { weapon: 'dao', beard: 'short' },
    '姜维': { weapon: 'spear', beard: 'short' },
    '张郃': { weapon: 'spear', beard: 'short' },
    '高顺': { weapon: 'spear', beard: 'short' },
    '纪灵': { weapon: 'ji', weaponName: '三尖刀', beard: 'bushy' },
    '张绣': { weapon: 'spear', beard: 'short' },
    '公孙瓒': { weapon: 'spear', helm: 'plume', plume: '#f0f0f0', armor: '#dcdce4', beard: 'short' },
    '董卓': { weapon: 'sword', bulk: 1.3, belly: true, beard: 'bushy', helm: 'crown' },
    '曹操': { weapon: 'sword', weaponName: '倚天剑', beard: 'short', helm: 'crown' },
    '袁绍': { weapon: 'sword', helm: 'crown', beard: 'short' },
    '诸葛亮': { weapon: 'sword', helm: 'guan', robe: '#e6e2d4', beard: 'short' },
    '周瑜': { weapon: 'sword', beard: 'none' },
    '吕蒙': { weapon: 'dao', beard: 'short' },
    // 南中诸族
    '孟获': { culture: 'nanman', weapon: 'bigblade', bulk: 1.16, beard: 'bushy' },
    '祝融': { culture: 'nanman', weapon: 'twindao', female: true, beard: 'none' },
    '孟优': { culture: 'nanman', weapon: 'mace' },
    '兀突骨': { culture: 'nanman', weapon: 'mace', rattan: true, bulk: 1.3, height: 1.1, skin: '#6a4a36' },
    '带来洞主': { culture: 'nanman', weapon: 'spear' },
    '沙摩柯': { culture: 'nanman', weapon: 'mace', weaponName: '铁蒺藜骨朵', skin: '#8a4a34' },
  };

  // 文化缺省外观（DESIGN-V2 §6）：冠帽 helm、甲胄 torso（zha 札甲 / segm 环片甲 / scale 鱼鳞甲 / coat 袍服 / bare 赤膊）、
  // 袍色 coat（与势力色相混）、兵器 weapons（按姓名哈希挑选）、盾 shield（单手兵器时左手持盾）、饰物
  const CULTURE = {
    han: { helm: 'han', skin: '#e8b996', hair: '#16120f' },
    nanman: { helm: 'topknot', skin: '#a9714c', hair: '#120e0b', bareArms: true, hide: true },
    yi: { helm: 'yifeather', skin: '#a9714c', hair: '#120e0b', bareArms: true, paint: '#1e2c48', shells: true, weapons: ['spear', 'mace'] },
    wa: { helm: 'mizura', skin: '#e2b48e', hair: '#141010', torso: 'coat', coat: '#d8ccb0', weapons: ['tachi', 'tachi', 'spear'], shield: 'wood', magatama: true, beard: 'short' },
    korea: { helm: 'feather', skin: '#e8bb98', hair: '#151111', weapons: ['dao', 'spear', 'dao', 'shortji'] },
    steppe: { helm: 'fur', skin: '#d9a77e', hair: '#1b1410', torso: 'coat', coat: '#7a5634', fur: true, weapons: ['dao', 'spear', 'mace'], bow: true },
    seasia: { helm: 'goldcrown', skin: '#9b6a48', hair: '#120e0b', bareArms: true, gold: true, weapons: ['spear', 'dao'], shield: 'round', beard: 'none' },
    tarim: { helm: 'hutmao', skin: '#e3b494', hair: '#3a2416', torso: 'coat', coat: '#9a4a4a', weapons: ['sword', 'spear'] },
    kushan: { helm: 'pointed', skin: '#d6a07a', hair: '#24170f', torso: 'coat', coat: '#5a4a86', weapons: ['sword', 'lance'], gold: true },
    persia: { helm: 'tiara', skin: '#d9a47c', hair: '#1a110c', torso: 'scale', weapons: ['lance', 'mace', 'sword'], beard: 'bushy' },
    arab: { helm: 'kufiya', helmColor: '#ece4d0', skin: '#c58f66', hair: '#1a110c', torso: 'coat', coat: '#e8dfc8', weapons: ['spear', 'sword'], shield: 'round', beard: 'bushy' },
    roman: { helm: 'galea', skin: '#e4b48e', hair: '#3a2618', torso: 'segm', armor: '#b9bec6', weapons: ['gladius', 'gladius', 'sword'], shield: 'scutum', beard: 'none' },
    celt: { helm: 'celtic', skin: '#f0c8a6', hair: '#c0682e', torso: 'bare', paint: '#2a58b0', plaid: true, weapons: ['sword'], shield: 'oval', beard: 'long' },
    german: { helm: 'suebian', skin: '#efc6a4', hair: '#c49a4c', torso: 'coat', coat: '#6a5a46', fur: true, weapons: ['handaxe', 'spear', 'handaxe'], shield: 'round', beard: 'bushy' },
    sarmatian: { helm: 'conical', skin: '#e2b08a', hair: '#2a1a10', torso: 'scale', weapons: ['lance', 'lance', 'sword'] },
  };

  function hashStr(s) {
    let h = 2166136261 >>> 0;
    s = String(s || '');
    for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; }
    return h >>> 0;
  }

  // 解析外观：知名设定 > 文化缺省 > 能力值 + 姓名哈希
  function lookOf(gen, culture) {
    const name = (gen && gen.name) || '无名';
    const fix = LOOKS[name] || {};
    const WD = SG.WorldData;
    let cul = culture || fix.culture || (gen && gen.culture);
    if (!cul && WD && typeof WD.cultureOfGeneral === 'function') { try { cul = WD.cultureOfGeneral(name); } catch (e) { cul = null; } }
    if (!CULTURE[cul]) cul = 'han';
    let female = !!fix.female || (gen && (gen.sex === 'f' || gen.female === true));
    if (!female && !fix.culture && WD && typeof WD.sexOf === 'function' && SG.WorldInfo && SG.WorldInfo[name]) { try { female = WD.sexOf(name) === 'f'; } catch (e) { /* 忽略 */ } }
    const base = CULTURE[cul] || CULTURE.han;
    const war = warOf(gen), intel = Number(gen && gen.intel) || 50;
    const h = hashStr(name);
    let weapon = fix.weapon;
    if (!weapon) {
      if (war >= 85) weapon = ['poleblade', 'spear', 'ji', 'spear', 'axe', 'poleblade'][h % 6];
      else if (intel > war + 15) weapon = 'sword';
      else weapon = ['spear', 'sword', 'dao', 'spear', 'shortji'][h % 5];
      if (cul === 'nanman') weapon = war >= 80 ? 'bigblade' : 'mace';
      else if (base.weapons) weapon = base.weapons[(h >>> 5) % base.weapons.length];
    }
    const beards = ['none', 'short', 'short', 'long', 'bushy'];
    const look = {
      culture: cul,
      weapon,
      weaponName: fix.weaponName || WEAPONS[weapon].name,
      helm: fix.helm || (cul === 'han' && intel > war + 25 && war < 55 ? 'guan' : base.helm),
      helmColor: fix.helmColor || base.helmColor || null,
      plume: fix.plume || null,
      armor: fix.armor || base.armor || null,          // null = 势力色
      torso: fix.torso || (fix.bare ? 'bare' : base.torso) || 'zha',
      coat: fix.coat || base.coat || null,
      shield: WEAPONS[weapon].style === 'one' ? (fix.shield !== undefined ? fix.shield : base.shield || null) : null,
      paint: fix.paint || base.paint || null, plaid: !!base.plaid, fur: !!base.fur, magatama: !!base.magatama, shells: !!base.shells, gold: !!base.gold,
      metal: fix.metal || (war >= 90 ? '#c9a24e' : '#8e939c'),
      cape: fix.cape || null,
      robe: fix.robe || null,
      skin: fix.skin || base.skin,
      hair: fix.hair || base.hair,
      beard: female ? 'none' : fix.beard || (base.beard && (h >>> 3) % 4 ? base.beard : beards[(h >>> 3) % beards.length]),
      bulk: fix.bulk || M.clamp(0.95 + (war - 50) / 100 * 0.14 + ((h >>> 7) % 7 - 3) * 0.01, 0.9, 1.12),
      height: fix.height || (1 + ((h >>> 11) % 5 - 2) * 0.012),
      bare: !!fix.bare || (!fix.torso && base.torso === 'bare'), belly: !!fix.belly, bareArms: !!(fix.bareArms || base.bareArms),
      hide: !!(fix.hide || base.hide || (cul === 'nanman' && !fix.rattan)), rattan: !!fix.rattan,
      eyepatch: !!fix.eyepatch, bow: !!(fix.bow || (base.bow && !LOOKS[name])), bells: !!fix.bells, female, goldTip: !!fix.goldTip,
    };
    return look;
  }

  // 绝技名称：优先 opts.special / SG.Specials，否则按兵器给一个兜底名称
  const FALLBACK_SPECIAL = {
    '关羽': '青龙偃月斩', '张飞': '长坂怒吼', '赵云': '七进七出', '吕布': '天下无双', '黄忠': '百步穿杨', '典韦': '双戟护主',
    '马超': '锦马超枪', '许褚': '虎痴裸衣', '刘备': '双股连环', '孙策': '小霸王', '太史慈': '神亭酣斗',
  };
  const STYLE_SPECIAL = {
    guandao: '偃月斩', snake: '蛇矛乱刺', ji: '画戟横扫', spear: '百鸟朝凤', poleblade: '力劈华山', axe: '开山裂石', bigblade: '斩马横行',
    sword: '剑气纵横', dao: '旋风刀', shortji: '短戟连环', mace: '碎岳一击', twinsword: '双龙乱舞', twinji: '双戟乱舞', twindao: '双刀旋斩',
    gladius: '盾阵突刺', lance: '铁骑冲阵', handaxe: '旋斧劈', tachi: '直刀一闪',
  };
  function specialOf(gen, given, look) {
    let s = given || null;
    if (!s && SG.Specials && typeof SG.Specials.of === 'function') {
      try { s = SG.Specials.of(gen); } catch (e) { s = null; }
    }
    const name = (s && s.name) || FALLBACK_SPECIAL[gen && gen.name] || STYLE_SPECIAL[look.weapon] || '奋武一击';
    let color = s && (s.color || (s.fx && s.fx.color));
    if (typeof color !== 'string') color = null;
    return { name, color };
  }

  // ============================================================ 逻辑：武将 --
  const NOHELD = { x: 0, up: false, light: false, heavy: false, guard: false, special: false, dash: 0 };

  class Fighter {
    constructor(idx, gen, look) {
      this.idx = idx;
      this.gen = gen;
      this.war = warOf(gen);
      this.look = look;
      this.wpn = WEAPONS[look.weapon] || WEAPONS.sword;
      this.reach = this.wpn.reach;
      this.spd = actSpeed(this.war) * (STYLE_SPD[this.wpn.style] || 1);
      this.stunK = stunK(this.war);
      this.walk = walkSpeed(this.war);
      this.x = idx === 0 ? -2.3 : 2.3;
      this.y = 0; this.vx = 0; this.vy = 0;
      this.face = idx === 0 ? 1 : -1;
      this.hp = 100; this.rage = 0;
      this.state = 'idle'; this.t = 0; this.seq = 0;
      this.hitIdx = 0;        // 本招已命中段数
      this.charge = 0;        // 重击蓄力 0..1
      this.stun = 0;          // 当前硬直时长
      this.invuln = 0;
      this.combo = 0;         // 连击数（对手连续处于受击硬直）
      this.chain = false;     // 连段输入
      this.airUsed = false;
      this.dashDir = 0;
      this.ko = false;
      this.counterHit = false;
      this.ctrl = null;       // { held, taps }
      this.prev = { x: 0, up: false, light: false, heavy: false, special: false };
      this.buf = { light: -9, jump: -9, special: -9, heavy: -9, dash: -9 };
      this.lastTap = { dir: 0, t: -9 };
      this.stats = { swings: 0, hits: 0, guarded: 0, taken: 0 };
      this.bow = !!look.bow;  // 弓将（黄忠、夏侯渊）：绝技为连珠箭（远程），不突进
    }
    get grounded() { return this.y <= 0 && this.vy <= 0; }
    // 出招阶段：'startup' | 'active' | 'recovery' | null
    get phase() {
      const mv = MOVES[this.state];
      if (!mv) return null;
      if (this.t < mv.startup) return 'startup';
      if (this.t < mv.startup + mv.active) return 'active';
      return 'recovery';
    }
  }

  // ============================================================ 逻辑：对战 --
  class Sim {
    constructor(genA, genB, lookA, lookB) {
      this.f = [new Fighter(0, genA, lookA), new Fighter(1, genB, lookB)];
      this.time = 0;              // 战斗计时（秒）
      this.clock = 0;             // 逻辑总时钟（含定格），供输入缓冲与 AI 感知
      this.limit = TIME_LIMIT;
      this.running = false;       // false：开场 / 结束演出（不接受操作）
      this.result = null;         // { winner, kind }
      this.endT = 0;              // 结果判定后经过的时间
      this.hitstop = 0;
      this.freeze = [0, 0];       // 绝技起手：对手定格
      this.events = [];
      this.log = [];
      this.hist = [[], []];       // 供 AI 延迟感知的历史快照
      this.histN = 90;
      this.steps = 0;
      this.shots = [];            // 飞行中的箭（弓将绝技）：{ id, owner, x, y, vx, fin, n }
      this.shotId = 0;
    }
    other(f) { return this.f[1 - f.idx]; }
    actionable(f) { return f.state === 'idle' || f.state === 'walk'; }
    emit(e) { this.events.push(e); if (this.events.length > 200) this.events.shift(); }

    // 推进一个逻辑步长
    step(dt) {
      dt = dt || DT;
      this.steps++;
      if (this.hitstop > 0) { this.hitstop -= dt; return; }
      this.clock += dt;
      const [a, b] = this.f;
      if (this.running && !this.result) {
        for (const f of this.f) if (f.ctrl && f.ctrl.update) f.ctrl.update(dt, this);
      }
      // 定格状态在步首统一判定（与更新顺序无关，避免先后手偏差）
      const fz0 = this.freeze[0] > 0, fz1 = this.freeze[1] > 0;
      if (fz0) this.freeze[0] -= dt;
      if (fz1) this.freeze[1] -= dt;
      if (!fz0) this.updateFighter(a, b, dt);
      if (!fz1) this.updateFighter(b, a, dt);
      if (!fz0) this.physics(a, dt);
      if (!fz1) this.physics(b, dt);
      this.separate(a, b);
      const ha = !fz0 ? this.hitCheck(a, b) : null;
      const hb = !fz1 ? this.hitCheck(b, a) : null;
      if (ha) this.hit(a, b, ha);
      if (hb) this.hit(b, a, hb);
      if (this.shots.length) this.updateShots(dt);
      if (this.running && !this.result) {
        this.time += dt;
        for (const f of this.f) f.rage = Math.min(100, f.rage + dt * 0.7);
        if (this.time >= this.limit) this.timeUp();
      }
      if (this.result) this.endT += dt;
      this.record();
    }

    record() {
      for (const f of this.f) {
        const H = this.hist[f.idx];
        const mv = MOVES[f.state];
        H.push({ clock: this.clock, state: f.state, x: f.x, y: f.y, seq: f.seq, phase: f.phase, atk: !!mv && f.phase !== 'recovery', charge: f.charge });
        if (H.length > this.histN) H.shift();
      }
    }
    // 延迟 delay 秒之前看到的对手状态
    seen(idx, delay) {
      const H = this.hist[idx];
      if (!H.length) return null;
      const want = this.clock - delay;
      for (let i = H.length - 1; i >= 0; i--) if (H[i].clock <= want) return H[i];
      return H[0];
    }

    setState(f, s) {
      f.state = s; f.t = 0; f.seq++;
      f.hitIdx = 0; f.chain = false;
    }

    // 读取输入沿：按下瞬间或期间的点击计数
    readInput(f) {
      const c = f.ctrl;
      const h = (this.running && !this.result && c && c.held) ? c.held : NOHELD;
      const taps = (this.running && !this.result && c && c.taps) ? c.taps : null;
      const now = this.clock, p = f.prev;
      const edge = k => (h[k] && !p[k]) || (taps && taps[k] > 0);
      if (edge('light')) f.buf.light = now;
      if (edge('up')) f.buf.jump = now;
      if (edge('special')) f.buf.special = now;
      if (edge('heavy')) f.buf.heavy = now;
      const xNew = h.x !== 0 && h.x !== p.x;
      if (xNew || (taps && taps.dir)) {
        const dir = xNew ? h.x : taps.dir;
        if (f.lastTap.dir === dir && now - f.lastTap.t < 0.28) { f.buf.dash = now; f.dashDir = dir; f.lastTap = { dir: 0, t: -9 }; }
        else f.lastTap = { dir, t: now };
      }
      if (h.dash) { f.buf.dash = now; f.dashDir = h.dash; }
      if (taps) { taps.light = 0; taps.up = 0; taps.special = 0; taps.heavy = 0; taps.dir = 0; }
      f.holdGuard = !!h.guard;
      p.x = h.x; p.up = h.up; p.light = h.light; p.heavy = h.heavy; p.special = h.special;
      return h;
    }
    fresh(f, k) { return this.clock - f.buf[k] <= BUFFER; }
    use(f, k) { f.buf[k] = -9; }

    updateFighter(f, o, dt) {
      const h = this.readInput(f);
      const mv = MOVES[f.state];
      f.t += dt * (mv ? (f.state === 'special' ? 1 : f.spd) : 1);
      if (f.invuln > 0) f.invuln -= dt;
      if (f.grounded && !f.ko && f.state !== 'knockdown' && f.state !== 'down') f.face = o.x >= f.x ? 1 : -1;

      switch (f.state) {
        case 'idle':
        case 'walk': {
          if (this.fresh(f, 'special') && f.rage >= 100) { this.use(f, 'special'); this.startSpecial(f, o); break; }
          if (this.fresh(f, 'dash') && f.dashDir) { this.use(f, 'dash'); this.setState(f, 'dash'); this.emit({ type: 'dash', who: f.idx }); break; }
          if (this.fresh(f, 'jump')) {
            this.use(f, 'jump');
            this.setState(f, 'jump'); f.vy = JUMP_V; f.airUsed = false; f.airHit = false;
            f.vx = h.x * f.walk * 0.95;
            this.emit({ type: 'jump', who: f.idx });
            break;
          }
          if (this.fresh(f, 'light')) { this.use(f, 'light'); this.startAttack(f, 'light1'); break; }
          if (this.fresh(f, 'heavy') && h.heavy) { this.use(f, 'heavy'); this.setState(f, 'charge'); f.charge = 0; break; }
          if (this.fresh(f, 'heavy') && !h.heavy) { this.use(f, 'heavy'); this.setState(f, 'charge'); f.charge = 0; f.tapHeavy = true; break; }
          if (h.guard) { this.setState(f, 'guard'); f.vx = 0; break; }
          if (h.x !== 0) {
            if (f.state !== 'walk') { f.state = 'walk'; f.seq++; f.t = 0; }
            const fwd = h.x === f.face;
            f.vx = h.x * f.walk * (fwd ? 1 : 0.72);
          } else if (f.state !== 'idle') { f.state = 'idle'; f.seq++; f.t = 0; }
          break;
        }
        case 'guard':
          f.vx = 0;
          if (this.fresh(f, 'dash') && f.dashDir && !h.guard) { this.use(f, 'dash'); this.setState(f, 'dash'); this.emit({ type: 'dash', who: f.idx }); break; }
          if (this.fresh(f, 'light')) { this.use(f, 'light'); this.startAttack(f, 'light1'); break; }
          if (!h.guard) this.setState(f, 'idle');
          break;
        case 'guardstun':
        case 'guardbreak':
          if (f.t >= f.stun) this.setState(f, h.guard && f.state === 'guardstun' ? 'guard' : 'idle');
          break;
        case 'charge':
          f.charge = Math.min(1, f.t / CHARGE_MAX);
          if (f.charge >= 1 && !f.chargeFull) { f.chargeFull = true; this.emit({ type: 'chargeFull', who: f.idx }); }
          if ((!h.heavy || f.tapHeavy) && f.t >= 0.12 || f.t >= CHARGE_MAX + 0.3) {
            const c = f.charge;
            f.tapHeavy = false; f.chargeFull = false;
            this.startAttack(f, 'heavy');
            f.charge = c;
          }
          break;
        case 'light1':
        case 'light2':
        case 'light3':
        case 'heavy': {
          if (f.state !== 'light3' && f.state !== 'heavy' && this.fresh(f, 'light') && f.t > 0.03) { this.use(f, 'light'); f.chain = true; }
          const total = mv.startup + mv.active + mv.recovery;
          if (mv.lunge && f.t >= mv.startup * 0.6 && f.t < mv.startup + mv.active) f.vx = f.face * mv.lunge;
          if (f.chain && mv.next && f.t >= mv.startup + mv.active * 0.6) { this.startAttack(f, mv.next); break; }
          if (f.t >= total) this.setState(f, 'idle');
          break;
        }
        case 'special': {
          // 起手定格之后突进到对手面前（弓将原地连射）
          const dx = (o.x - f.x) * f.face;
          if (!f.bow && f.t >= SPECIAL_FREEZE && f.t < mv.startup + mv.active) f.vx = dx > f.reach * 0.7 ? f.face * 10 : 0;
          else f.vx = 0;
          if (f.t >= mv.startup + mv.active + mv.recovery) this.setState(f, 'idle');
          break;
        }
        case 'dash':
          f.vx = f.dashDir * 7.4;
          if (f.t > 0.07 && this.fresh(f, 'light')) { this.use(f, 'light'); this.startAttack(f, 'light1'); f.vx *= 0.6; break; }
          if (f.t >= 0.22) this.setState(f, 'idle');
          break;
        case 'jump':
          f.vx = M.clamp(f.vx + h.x * 9 * dt, -f.walk, f.walk);
          if (!f.airUsed && this.fresh(f, 'light')) { this.use(f, 'light'); f.airUsed = true; this.startAttack(f, 'air'); }
          break;
        case 'air': {
          const total = mv.startup + mv.active + mv.recovery;
          if (f.t >= total) { f.state = 'jump'; f.seq++; f.t = 0.3; }
          break;
        }
        case 'land':
          // 落地硬直：空中出过招则较长（命中 / 被挡 0.2 秒，挥空 0.3 秒），跳入后可被反击
          if (f.t >= (f.landT || 0.07)) this.setState(f, h.guard ? 'guard' : 'idle');
          break;
        case 'hitstun':
          // 硬直结束时若按住格挡，直接进入格挡（无空隙）
          if (f.t >= f.stun) { this.setState(f, h.guard && f.grounded ? 'guard' : 'idle'); f.combo = 0; o.combo = 0; }
          break;
        case 'knockdown':
          break;   // 落地在 physics 里处理
        case 'down':
          if (!f.ko && f.t >= 0.62) this.setState(f, 'getup');
          break;
        case 'getup':
          f.invuln = Math.max(f.invuln, 0.05);
          if (f.t >= 0.42) { this.setState(f, h.guard ? 'guard' : 'idle'); f.invuln = 0.2; o.combo = 0; }
          break;
        default: break;
      }

      // 结果判定后的演出：胜者摆出胜利姿势，败者跪倒 / 倒地不起
      if (this.result) {
        const win = this.result.winner === f.idx;
        if (win && this.endT > (this.result.kind === 'ko' ? 1.0 : 0.5) && (f.state === 'idle' || f.state === 'walk') && f.grounded) { this.setState(f, 'victory'); f.vx = 0; }
        if (!win && !f.ko && this.endT > 0.5 && (f.state === 'idle' || f.state === 'walk' || f.state === 'guard') && f.grounded) { this.setState(f, 'defeat'); f.vx = 0; }
      }
    }

    startAttack(f, name) {
      this.setState(f, name);
      f.stats.swings++;
      if (name !== 'air') f.vx *= 0.3;
    }

    startSpecial(f, o) {
      this.setState(f, 'special');
      f.rage = 0; f.vx = 0;
      f.stats.swings++;
      // 对手定格；弓将的定格延续到第一支箭射到为止（近战绝技靠突进追上对手）
      this.freeze[o.idx] = SPECIAL_FREEZE + (f.bow ? 0.12 + Math.max(0, Math.abs(o.x - f.x) - 0.6) / 24 : 0);
      this.emit({ type: 'special', who: f.idx });
    }

    physics(f, dt) {
      if (f.y > 0 || f.vy > 0) {
        f.vy -= GRAV * dt;
        f.y += f.vy * dt;
        if (f.y <= 0) {
          f.y = 0; f.vy = 0;
          if (f.state === 'knockdown') { this.setState(f, 'down'); f.vx *= 0.35; this.emit({ type: 'down', who: f.idx, x: f.x }); }
          else if (f.state === 'jump' || f.state === 'air') {
            this.setState(f, 'land'); f.vx *= 0.3;
            f.landT = f.airUsed ? (f.airHit ? 0.2 : 0.3) : 0.07;
            this.emit({ type: 'land', who: f.idx, x: f.x });
          }
          else if (f.state === 'hitstun') f.vx *= 0.5;
        }
      } else if (f.state !== 'walk' && f.state !== 'dash' && !(f.state === 'special' && f.vx !== 0) && !(MOVES[f.state] && MOVES[f.state].lunge && f.t < MOVES[f.state].startup + MOVES[f.state].active)) {
        const s = Math.sign(f.vx), v = Math.abs(f.vx) - FRICTION * dt;
        f.vx = v > 0 ? s * v : 0;
      }
      f.x += f.vx * dt;
      if (f.x < -ARENA) { f.x = -ARENA; if (f.vx < 0) f.vx = 0; }
      if (f.x > ARENA) { f.x = ARENA; if (f.vx > 0) f.vx = 0; }
    }

    // 推挤：双方不得重叠、不得互相穿越
    separate(a, b) {
      const d = b.x - a.x, min = BODY * 2;
      if (Math.abs(d) >= min) return;
      const s = d === 0 ? (a.face > 0 ? 1 : -1) : Math.sign(d);
      const over = min - Math.abs(d);
      a.x -= s * over * 0.5; b.x += s * over * 0.5;
      if (a.x < -ARENA) { b.x += -ARENA - a.x; a.x = -ARENA; }
      if (a.x > ARENA) { b.x -= a.x - ARENA; a.x = ARENA; }
      if (b.x < -ARENA) { a.x += -ARENA - b.x; b.x = -ARENA; }
      if (b.x > ARENA) { a.x -= b.x - ARENA; b.x = ARENA; }
    }

    // 判定命中（只判定不结算，双方同一步内的命中视为相打）
    hitCheck(f, o) {
      const mv = MOVES[f.state];
      if (!mv) return null;
      if (f.state === 'special') {
        const idx = f.hitIdx;
        if (idx >= mv.hits.length || f.t < mv.startup + mv.hits[idx]) return null;
        f.hitIdx++;
        const fin = idx === mv.hits.length - 1;
        if (f.bow) {
          // 放箭：箭矢独立飞行，命中在 updateShots 里结算
          const sh = { id: ++this.shotId, owner: f.idx, x: f.x + f.face * 0.55, y: fin ? 1.3 : 1.42, vx: f.face * (fin ? 30 : 24), fin, n: idx };
          this.shots.push(sh);
          this.emit({ type: 'arrow', who: f.idx, id: sh.id, fin, n: idx });
          return null;
        }
        this.emit({ type: 'swing', who: f.idx, move: 'special', n: idx });
        const dx = (o.x - f.x) * f.face;
        if (dx > -0.2 && dx <= f.reach + BODY + 0.4 && !this.immune(o)) return { mv, name: 'special', fin, charge: 0 };
        return null;
      }
      if (f.phase !== 'active' || f.hitIdx > 0) return null;
      if (f.swungSeq !== f.seq) { f.swungSeq = f.seq; this.emit({ type: 'swing', who: f.idx, move: f.state, charge: f.charge }); }
      const dx = (o.x - f.x) * f.face;
      const reach = f.reach * mv.reachK + BODY * 0.5;
      const heightOk = f.state === 'air' ? Math.abs(o.y - f.y) < 1.7 : o.y < mv.hmax;
      if (dx > -0.15 && dx <= reach && heightOk && !this.immune(o)) { f.hitIdx = 1; return { mv, name: f.state, fin: false, charge: f.state === 'heavy' ? f.charge : 0 }; }
      return null;
    }
    immune(o) { return o.invuln > 0 || o.state === 'knockdown' || o.state === 'down' || o.state === 'getup' || o.ko; }

    // 箭矢飞行与命中（穿过倒地 / 无敌的对手，飞出场外后消失）
    updateShots(dt) {
      const S = this.shots;
      let n = 0;
      for (let i = 0; i < S.length; i++) {
        const sh = S[i], f = this.f[sh.owner], o = this.f[1 - sh.owner];
        const x0 = sh.x;
        sh.x += sh.vx * dt;
        const dir = Math.sign(sh.vx);
        const crossed = (o.x - dir * BODY * 0.5 - x0) * dir >= -0.05 && (o.x - dir * BODY * 0.5 - sh.x) * dir <= 0;
        if (crossed && !this.immune(o) && o.y < 1.9 && !sh.hit) {
          sh.hit = true;
          this.hit(f, o, { mv: MOVES.special, name: 'special', fin: sh.fin, charge: 0, px: o.x - dir * 0.25, py: sh.y + o.y * 0.6, arrow: true });
          this.emit({ type: 'arrowEnd', id: sh.id, hit: true });
          continue;
        }
        if (Math.abs(sh.x) > ARENA + 6) { this.emit({ type: 'arrowEnd', id: sh.id, hit: false }); continue; }
        S[n++] = sh;
      }
      S.length = n;
    }

    hit(f, o, h) {
      const mv = h.mv, name = h.name, finisher = h.fin, chg = h.charge;
      let base = mv.dmg * (f.wpn.pow || 1);
      if (name === 'heavy') base += mv.dmgCharge * chg;
      if (name === 'special' && finisher) base = mv.finisher;
      if (h.arrow) base *= 0.8;                     // 箭矢可远距离命中，威力略低
      const counter = o.state === 'charge' || (MOVES[o.state] && o.phase === 'startup' && o.state !== 'special');
      if (counter) base *= 1.2;
      let dmg = damage(base, f.war, o.war) * SG.Random.rangeFloat(0.92, 1.08);
      let guarded = (o.state === 'guard' || o.state === 'guardstun') && o.grounded;
      // 格开：连段中的后续一击，受击方按住格挡且武力远高于攻方时，有机会格开（武力差 30 → 18%，70 → 50%）
      let parry = false;
      if (!guarded && o.state === 'hitstun' && o.holdGuard && o.grounded && o.hp > 0 && name !== 'special') {
        const p = M.clamp((o.war - f.war - 12) / 100, 0, 0.5);
        if (p > 0 && rnd() < p) { guarded = true; parry = true; }
      }
      const charged = name === 'heavy' && chg >= 0.85;
      const breaks = guarded && (charged || (name === 'special' && finisher));
      if (guarded) dmg *= 0.2;
      dmg = M.clamp(Math.round(dmg), guarded ? 0 : 1, 40);
      o.hp = Math.max(0, o.hp - dmg);
      const dir = f.face;
      const big = !!mv.big || charged;
      let hs = mv.hitstop * (guarded ? 0.6 : 1) * (charged ? 1.3 : 1);
      f.stats.hits++;
      if (name === 'air') f.airHit = true;
      if (guarded) f.stats.guarded++;
      o.stats.taken++;

      let knock = false;
      if (o.hp <= 0) {
        knock = true; o.ko = true;
      } else if (guarded) {
        if (breaks) { this.setState(o, 'guardbreak'); o.stun = 0.8 * o.stunK; this.emit({ type: 'guardbreak', who: o.idx }); }
        else { this.setState(o, 'guardstun'); o.stun = (0.12 + mv.stun * 0.35) * o.stunK; }
        o.vx = dir * mv.push * (mv.big ? 0.5 : 0.85);     // 大招被挡时推开得少：收招破绽可被反击
        o.rage = Math.min(100, o.rage + 2 + dmg);
        f.rage = Math.min(100, f.rage + 1.5);
      } else {
        knock = (name === 'heavy' && chg >= 0.6) || (name === 'special' && finisher) || o.y > 0.35;
        f.combo = o.state === 'hitstun' ? f.combo + 1 : 1;
        o.rage = Math.min(100, o.rage + 3 + dmg * 1.2);
        f.rage = Math.min(100, f.rage + 3 + dmg * 0.8);
        if (!knock) {
          this.setState(o, 'hitstun');
          o.stun = mv.stun * o.stunK * (name === 'heavy' ? 1 + chg * 0.3 : 1);
          o.vx = dir * mv.push;
          if (o.y > 0) o.vy = Math.max(o.vy, 2);
        }
      }
      if (knock) {
        this.setState(o, 'knockdown');
        o.vx = dir * (o.ko ? 4.2 : 3.4); o.vy = o.ko ? 6.2 : 5.0; o.y = Math.max(o.y, 0.01);
        o.chargeFull = false;
        hs = Math.max(hs, o.ko ? 0.16 : 0.12);
        this.emit({ type: 'knockdown', who: o.idx });
      }
      // 角落：对手退无可退时，反推攻击方
      if (!h.arrow && Math.abs(o.x) >= ARENA - 0.05 && Math.sign(o.x) === dir) f.vx = -dir * mv.push * 0.7;
      this.hitstop = Math.max(this.hitstop, hs);
      const entry = { who: f.idx, dmg, hpA: this.f[0].hp, hpB: this.f[1].hp, move: name, guarded, t: Math.round(this.time * 100) / 100 };
      this.log.push(entry);
      this.emit({ type: 'hit', att: f.idx, def: o.idx, dmg, guarded, parry, move: name, big, counter, breaks, charge: chg, arrow: !!h.arrow,
        finisher, combo: guarded ? 0 : f.combo,
        x: h.px !== undefined ? h.px : f.x + dir * Math.min(Math.abs(o.x - f.x) * 0.75, f.reach * 0.8),
        y: h.py !== undefined ? h.py : o.y + (mv.kind === 'air' ? 1.1 : 1.25) });
      if (o.ko && !this.result) {
        this.result = { winner: f.idx, kind: 'ko' };
        this.endT = 0;
        this.emit({ type: 'ko', who: o.idx, winner: f.idx });
      }
    }

    timeUp() {
      const [a, b] = this.f;
      const winner = a.hp >= b.hp ? 0 : 1;     // 相同则挑战者胜（同 BattleModel.duel）
      this.result = { winner, kind: 'time' };
      this.endT = 0;
      this.emit({ type: 'timeup', winner });
    }

    // 立即算完（观战跳过 / 无画面模拟）：双方交给电脑，直到分出胜负
    runToEnd(maxSteps) {
      maxSteps = maxSteps || 60 * 75;
      this.running = true;
      for (let i = 0; i < maxSteps && !this.result; i++) this.step(DT);
      if (!this.result) this.timeUp();
    }

    outcome(kind) {
      const r = this.result || { winner: this.f[0].hp >= this.f[1].hp ? 0 : 1, kind: 'time' };
      return { winner: r.winner, kind: kind || r.kind, hpA: this.f[0].hp, hpB: this.f[1].hp, log: this.log.slice() };
    }
  }

  // ============================================================ 电脑 --
  // 难度由武力决定：反应时间（延迟感知对手动作）、格挡率、连段、出手积极度、绝技使用
  class AI {
    constructor(me) {
      this.me = me;
      // 技巧：武力 20 → 0.4、武力 60 → 0.7、武力 100 → 1（下限较高：普通武将也会防守反击，强将近乎无隙可乘）
      const s = 0.4 + 0.6 * M.clamp((me.war - 20) / 80, 0, 1);
      this.s = s;
      this.react = 0.44 - 0.29 * s;      // 反应时间：武力 100 → 0.15 秒，武力 20 → 0.44 秒
      this.guardP = 0.10 + 0.70 * s;     // 看见来招时举防的概率
      this.comboP = 0.30 + 0.65 * s;     // 命中后接续连段
      this.aggr = 0.28 + 0.40 * s;       // 进入距离后出手的积极度
      this.period = 0.30 - 0.16 * s;     // 决策间隔
      this.whiffP = 0.32 * (1 - s);
      this.antiP = 0.06 + 0.86 * s;      // 识破跳入（对空 / 挡下后反击落地硬直）
      this.punishP = 0.12 + 0.78 * s;    // 抓对手收招的破绽
      this.pokeP = 0.08 + 0.52 * s;      // 对手走进攻击距离时迎击
      this.stunGuardP = 0.08 + 0.84 * s; // 受击 / 倒地后一恢复就举防
      this.held = { x: 0, up: false, light: false, heavy: false, guard: false, special: false, dash: 0 };
      this.taps = { light: 0, up: 0, special: 0, heavy: 0, dir: 0 };
      this.next = 0; this.planX = 0; this.guardUntil = 0; this.seenSeq = -1; this.comboSeq = -1; this.comboGo = false;
      this.chargeTo = 0; this.backUntil = 0;
      this.stunSeq = -1; this.stunGuard = false; this.stunHold = 0;
      this.aa = null; this.punSeq = -1; this.pokeAt = 0; this.lastGuarded = 0; this.zone = false; this.caut = false; this.cautAt = 0; this.patSeq = -1; this.patient = false;
    }
    press(k) { this.taps[k] = (this.taps[k] || 0) + 1; }
    // 重击：同时按住，蓄力到 to 秒后松开（只点按会被当作轻点重击）
    heavy(to) { this.press('heavy'); this.held.heavy = true; this.chargeTo = to; }

    update(dt, sim) {
      this.think(dt, sim);
      // 逼近过滤：向前走将进入对手的攻击距离、而对手正在逼近或出招时，高武力改为举防停步（不硬闯）
      const me = this.me, foe = sim.other(me), H = this.held, T = this.taps;
      const dist = Math.abs(foe.x - me.x), foeR = foe.reach * MOVES.light2.reachK + BODY * 0.5;
      if (dist >= foeR + 0.6) this.zone = false;
      if (H.x === me.face && !H.guard && !H.dash && !T.light && !T.heavy && !T.up && !T.special && sim.actionable(me) && dist < foeR + 0.4) {
        const seen = sim.seen(foe.idx, this.react);
        const pushy = seen && seen.state !== 'guard' && seen.phase !== 'recovery';
        if (!this.zone || sim.clock >= this.cautAt) { this.zone = true; this.cautAt = sim.clock + 0.35; this.caut = rnd() < 0.1 + 0.75 * this.s; }
        if (pushy && this.caut) {
          H.x = 0; this.planX = 0; H.guard = true;
          this.guardUntil = Math.max(this.guardUntil, sim.clock + 0.25 + rnd() * 0.2);
        }
      }
    }

    think(dt, sim) {
      const me = this.me, foe = sim.other(me), now = sim.clock, H = this.held, s = this.s;
      H.dash = 0;
      const dist = Math.abs(foe.x - me.x);
      const reach = me.reach * MOVES.light1.reachK + BODY * 0.5 - 0.03;   // 轻击实际判定距离（略保守）
      const seen = sim.seen(foe.idx, this.react);

      // 蓄力：到达预定蓄力量后松开
      if (me.state === 'charge') { H.heavy = me.t < this.chargeTo; H.x = 0; H.guard = false; return; }
      H.heavy = false;
      // 受击 / 被挡 / 倒地：高武力会按住格挡，一恢复就举防（并有机会格开弱者的连段）
      if (me.state === 'hitstun' || me.state === 'guardstun' || me.state === 'guardbreak' || me.state === 'knockdown' || me.state === 'down' || me.state === 'getup') {
        if (this.stunSeq !== me.seq) { this.stunSeq = me.seq; this.stunGuard = rnd() < this.stunGuardP; this.stunHold = 0.1 + rnd() * 0.22; }
        H.guard = this.stunGuard; H.x = 0;
        if (this.stunGuard) this.guardUntil = Math.max(this.guardUntil, now + this.stunHold);
        return;
      }
      // 连段：高武力只在命中时继续（命中确认），低武力随机
      if (me.state === 'light1' || me.state === 'light2') {
        if (this.comboSeq !== me.seq) { this.comboSeq = me.seq; this.comboRoll = rnd(); this.comboDone = false; }   // 每一段各自判定
        const mv = MOVES[me.state];
        if (!this.comboDone && (me.hitIdx > 0 || me.t >= mv.startup + mv.active)) {   // 命中即确认；挥空则在判定结束时决定
          const landed = me.hitIdx > 0 && foe.state === 'hitstun';
          const go = landed ? this.comboRoll < this.comboP : this.comboRoll < this.comboP * (1 - s) * 0.6;
          if (go) this.press('light');
          this.comboDone = true;
        }
        H.x = 0; H.guard = false;
        return;
      }
      this.comboDone = false;
      const free = sim.actionable(me) || me.state === 'guard';
      // 自己的攻击被挡：高武力预料对手会反击，收招后先举防
      if (me.stats.guarded !== this.lastGuarded) {
        this.lastGuarded = me.stats.guarded;
        const mv = MOVES[me.state];
        const left = mv ? (mv.startup + mv.active + mv.recovery - me.t) / (me.state === 'special' ? 1 : me.spd) : 0;
        if (rnd() < 0.1 + 0.7 * s) this.guardUntil = Math.max(this.guardUntil, now + left + 0.18 + rnd() * 0.25);
      }
      // 对手被破防：立即追击
      if (foe.state === 'guardbreak' && free && foe.t < foe.stun - MOVES.light1.startup / me.spd) {
        this.guardUntil = 0; H.guard = false;
        if (dist <= reach) { this.press('light'); H.x = 0; return; }
        if (dist <= reach + 1.2 && me.state !== 'guard') { H.dash = me.face; this.press('light'); return; }
        H.x = me.face; return;
      }

      // 对空：看见对手跳向自己（有反应延迟）→ 按武力决定是否识破：迎击下落中的对手，或挡下空中斩再反击落地硬直
      const foeAir = foe.y > 0.02 || foe.state === 'jump' || foe.state === 'air';
      if (!foeAir && foe.state !== 'land') this.aa = null;
      if (!this.aa && foeAir && seen && (seen.state === 'jump' || seen.state === 'air')) {
        const toward = (me.x - foe.x) * foe.vx > 0.2 || dist < 1.4;
        if (toward && dist < foe.reach + 3) this.aa = rnd() < this.antiP ? (rnd() < 0.5 ? 'strike' : 'guard') : 'miss';
      }
      if ((this.aa === 'strike' || this.aa === 'guard') && free) {
        if (foeAir) {
          if (this.aa === 'strike') {
            const lead = MOVES.light1.startup / me.spd + DT;
            const ty = foe.y + foe.vy * lead - 0.5 * GRAV * lead * lead;
            const dx = (foe.x + foe.vx * lead - me.x) * me.face;
            if (foe.vy < 1.5 && ty < 0.8 && ty > -0.2 && dx > -0.1 && dx <= me.reach * MOVES.light1.reachK + BODY * 0.5 - 0.05) {
              this.press('light'); this.aa = 'done'; H.guard = false; H.x = 0; return;
            }
            if (Math.abs(foe.x - me.x) < 0.9 && foe.y > 0.7) this.aa = 'guard';   // 太近 / 越过头顶：改为格挡
            else { H.guard = false; H.x = 0; return; }
          }
          H.guard = true; H.x = 0; return;
        }
        if (foe.state === 'land') {      // 落地硬直：反击
          if (dist <= reach * 1.05) { this.press('light'); this.aa = 'done'; H.guard = false; H.x = 0; return; }
          if (dist <= reach + 1.2) { H.dash = me.face; this.press('light'); this.aa = 'done'; H.guard = false; return; }
        }
      }

      // 正在格挡而对手的连段还在继续（看到对手仍在出招）：不放下防御
      if ((me.state === 'guard' || me.state === 'guardstun') && seen && seen.atk && Math.abs(seen.x - me.x) <= foe.reach + BODY + 0.6 && rnd() < 0.3 + 0.7 * s)
        this.guardUntil = Math.max(this.guardUntil, now + 0.12);
      // 看见对手出招：按格挡率决定是否举防
      if (seen && seen.atk && seen.seq !== this.seenSeq) {
        this.seenSeq = seen.seq;
        const threat = Math.abs(seen.x - me.x) <= foe.reach + BODY + 0.5;
        if (threat && rnd() < this.guardP) this.guardUntil = now + 0.2 + rnd() * 0.25;
      }
      // 看见对手收招破绽（挥空 / 被挡）：抢攻，距离稍远则冲刺斩
      if (seen && seen.phase === 'recovery' && seen.state !== 'air' && seen.seq !== this.punSeq && free && dist <= reach + 1.1) {
        this.punSeq = seen.seq;
        // 对手收招还剩多少时间（熟知招式节奏）：来不及冲过去就不冒险
        const fm = MOVES[foe.state];
        const left = fm && foe.seq === seen.seq ? (fm.startup + fm.active + fm.recovery - foe.t) / (foe.state === 'special' ? 1 : foe.spd) : 0;
        const need = MOVES.light1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
        if (left > need - 0.03 && rnd() < this.punishP) {
          this.guardUntil = 0; H.guard = false;
          if (dist <= reach) { this.press('light'); H.x = 0; return; }
          H.dash = me.face; this.press('light'); return;
        }
      }
      // 迎击：按看到的对手位置与速度预判，对手将进入自己的攻击距离时抢先出手（兵器长者占便宜）
      if (seen && sim.actionable(me) && now >= this.pokeAt && (seen.state === 'walk' || seen.state === 'dash' || seen.state === 'idle')) {
        const prev = sim.seen(foe.idx, this.react + 0.05);
        const vx = prev && seen.clock > prev.clock ? (seen.x - prev.x) / (seen.clock - prev.clock) : 0;
        const lead = MOVES.light1.startup / me.spd;
        const pd = Math.abs(seen.x + vx * (this.react + lead) - (me.x + me.vx * lead * 0.3));
        if (pd <= reach) {
          this.pokeAt = now + 0.3;
          if (rnd() < this.pokeP) { this.press('light'); H.x = 0; this.planX = 0; return; }
        }
      }
      // 看见对手蓄力：高武力抢攻（反击判定）或后撤，低武力傻站
      // 看见对手蓄力：高武力抢攻（打断蓄力）；蓄得浅就举防，蓄得深（将破防）就后撤；低武力傻站
      if (seen && seen.state === 'charge' && dist < foe.reach * 1.08 + BODY * 0.5 + 0.9 && now >= this.next) {
        this.next = now + this.period;
        const r = rnd();
        if (r < s * 0.6 && dist <= reach && sim.actionable(me)) { this.press('light'); return; }
        if (r < s * 0.9) {
          if (seen.charge > 0.55) { this.backUntil = now + 0.4; this.guardUntil = 0; }
          else this.guardUntil = Math.max(this.guardUntil, now + 0.3);
        }
      }
      // 看见对手冲刺切入：兵器较长者迎头一击（抢先出手）
      if (seen && seen.state === 'dash' && seen.seq !== this.dashSeq && sim.actionable(me)) {
        this.dashSeq = seen.seq;
        if (dist <= reach + 0.5 && me.reach >= foe.reach && rnd() < 0.15 + 0.45 * s) { this.press('light'); return; }
      }
      const canGuard = free || me.state === 'guardstun';
      if (now < this.guardUntil && canGuard) { H.guard = true; H.x = 0; return; }
      H.guard = false;
      if (now < this.backUntil && sim.actionable(me)) { H.x = -me.face; return; }
      // 空中：接近时出空中斩
      if (me.state === 'jump') {
        if (!me.airUsed && dist < reach * 0.95 + 0.3 && foe.y < 1.2 && me.vy < 2) this.press('light');
        H.x = dist > reach * 0.7 ? me.face : 0;
        return;
      }
      if (!sim.actionable(me) && me.state !== 'guard') { H.x = 0; return; }
      // 对手正在出招、自己在其攻击距离边缘：高武力不硬闯，停步等其收招再反击
      if (seen && MOVES[seen.state] && seen.phase !== 'recovery' && seen.state !== 'special' && dist < foe.reach + BODY * 0.5 + 0.7) {
        if (this.patSeq !== seen.seq) { this.patSeq = seen.seq; this.patient = rnd() < 0.15 + 0.8 * s; }
        if (this.patient) { this.planX = 0; H.x = 0; return; }
      }
      if (now < this.next) {
        // 走进攻击距离即停步，避免贴身
        if (this.planX === me.face && dist <= reach * 0.92) this.planX = 0;
        H.x = this.planX;
        return;
      }
      this.next = now + this.period * (0.7 + rnd() * 0.6);
      this.planX = 0;
      H.x = 0;

      // 对手倒地：保持距离，伺机蓄力
      if (foe.state === 'knockdown' || foe.state === 'down' || foe.state === 'getup') {
        if (dist > reach * 1.1) this.planX = me.face;
        else if (dist < reach * 0.6) this.planX = -me.face;
        else if (rnd() < 0.3 * s) this.heavy(0.3 + rnd() * 0.5);
        H.x = this.planX;
        return;
      }
      // 绝技
      if (me.rage >= 100 && (me.bow ? dist > reach * 0.6 : dist <= reach + 1.8) && rnd() < 0.3 + 0.55 * s) { this.press('special'); return; }
      if (dist > reach * 1.02) {
        // 高武力：对手兵器够得着时不贸然闯入，在其攻击距离外踱步，等对手先出手露出破绽
        const foeR = foe.reach + BODY * 0.5 + 0.15;
        const pushy = seen && (seen.state === 'walk' || seen.state === 'dash' || MOVES[seen.state]);
        if (pushy && dist < foeR + 0.5 && rnd() < 0.1 + 0.65 * s) {
          this.planX = dist < foeR + 0.12 ? -me.face : 0; H.x = this.planX;
          return;
        }
        // 兵器较短：抓对手收招的破绽，或冲刺切入（冲刺中轻击 = 突进斩）
        const gap = foe.reach - me.reach;
        if (gap > 0.03 && dist < reach + 0.85) {
          const k = Math.min(1, 0.35 + gap / 0.3);
          const opening = seen && (seen.phase === 'recovery' || seen.state === 'hitstun' || seen.state === 'guardstun');
          if ((opening && rnd() < (0.35 + 0.5 * s) * k) || rnd() < (0.08 + 0.18 * s) * k) { H.dash = me.face; this.press('light'); return; }
        }
        if (gap > -0.06 && dist < reach + 0.75 && rnd() < 0.05 + 0.1 * s) { H.dash = me.face; this.press('light'); return; }   // 兵器相当：偶尔冲刺抢攻
        if (dist < reach * 1.45 && rnd() < this.whiffP) { this.press('light'); return; }   // 冒失出手
        if (dist > 3.4 && rnd() < 0.22 + 0.2 * s) { H.dash = me.face; return; }
        if (dist > 1.9 && dist < 3.3 && rnd() < 0.05 + 0.05 * (1 - s)) { this.press('up'); H.x = me.face; this.planX = me.face; return; }
        this.planX = me.face; H.x = this.planX;
        return;
      }
      // 贴得太近：长兵器施展不开，拉开距离
      if (dist < reach * 0.55 && rnd() < 0.35 + 0.4 * s) { this.planX = -me.face; H.x = this.planX; this.next = now + 0.18 + rnd() * 0.15; return; }
      // 进入攻击距离：高武力会预判举防
      if (dist <= foe.reach + BODY && rnd() < this.guardP * 0.45) { this.guardUntil = now + 0.25 + rnd() * 0.3; H.guard = true; return; }
      const st = me.stats;
      const foeTurtles = (st.hits >= 4 && st.guarded / st.hits > 0.45) || (seen && seen.state === 'guard' && foe.state === 'guard');
      if (foeTurtles && rnd() < 0.25 + 0.4 * s) { this.heavy(CHARGE_MAX + 0.05); return; }
      const r = rnd();
      if (r < this.aggr) {
        if (rnd() < 0.74) this.press('light');
        else this.heavy(0.12 + rnd() * (0.25 + 0.45 * s));
      } else if (r < this.aggr + 0.3) {
        // 进退试探
        this.planX = dist < reach * 0.75 || rnd() < 0.6 ? -me.face : me.face;
        this.next = now + 0.15 + rnd() * 0.25;
      } else if (r < this.aggr + 0.42) {
        this.guardUntil = now + 0.2 + rnd() * 0.3;
      }
      H.x = this.planX;
    }
  }

  // 无画面快速模拟（平衡测试 / 跳过）
  function simulate(genA, genB, opts) {
    opts = opts || {};
    const sim = new Sim(genA, genB, lookOf(genA, opts.cultureA), lookOf(genB, opts.cultureB));
    sim.f[0].ctrl = new AI(sim.f[0]);
    sim.f[1].ctrl = new AI(sim.f[1]);
    sim.runToEnd();
    const r = sim.outcome();
    r.time = Math.round(sim.time * 10) / 10;
    return r;
  }

  // ============================================================ 视图：网格构建 --
  // 平面着色、顶点色的小型网格构建器。直接使用 three 坐标（不做 Unity 的 z 翻转），逆时针为正面。
  let T_ = null;
  function tmp() {
    if (!T_) T_ = { v: new THREE.Vector3(), m: new THREE.Matrix4(), q: new THREE.Quaternion(), e: new THREE.Euler(), s: new THREE.Vector3(1, 1, 1) };
    return T_;
  }
  const colCache = new Map();
  function C(hex) {
    if (hex && hex.isColor) return hex;
    let c = colCache.get(hex);
    if (!c) { c = SG.Gfx.color(hex); colCache.set(hex, c); }
    return c;
  }
  function sh(c, k) { return SG.Gfx.shade(C(c), k); }
  function mixC(a, b, t) { return SG.Gfx.lerpColor(C(a), C(b), t); }

  class PB {
    constructor() { this.p = []; this.c = []; this.M = null; }
    // 局部变换：位置 + 欧拉角（XYZ）+ 缩放
    at(x, y, z, rx, ry, rz, s) {
      if (x === undefined) { this.M = null; return this; }
      const t = tmp();
      t.e.set(rx || 0, ry || 0, rz || 0);
      t.q.setFromEuler(t.e);
      t.s.set(s || 1, s || 1, s || 1);
      this.M = new THREE.Matrix4().compose(new THREE.Vector3(x, y, z), t.q.clone(), t.s.clone());
      return this;
    }
    _v(x, y, z) {
      if (this.M) { const v = tmp().v.set(x, y, z).applyMatrix4(this.M); this.p.push(v.x, v.y, v.z); }
      else this.p.push(x, y, z);
    }
    tri(a, b, c, col) {
      this._v(a[0], a[1], a[2]); this._v(b[0], b[1], b[2]); this._v(c[0], c[1], c[2]);
      col = C(col);
      this.c.push(col.r, col.g, col.b, col.r, col.g, col.b, col.r, col.g, col.b);
    }
    quad(a, b, c, d, col) { this.tri(a, b, c, col); this.tri(a, c, d, col); }
    // 任意凸六面体：P[i]，i = x位 + 2·y位 + 4·z位
    hex(P, col, top, bot) {
      const q = (i, j, k, l, c) => this.quad(P[i], P[j], P[k], P[l], c);
      q(0, 4, 6, 2, col); q(1, 3, 7, 5, col); q(4, 5, 7, 6, sh(col, 0.04)); q(0, 2, 3, 1, sh(col, -0.06));
      q(2, 6, 7, 3, top || sh(col, 0.1)); q(0, 1, 5, 4, bot || sh(col, -0.3));
    }
    box(cx, cy, cz, sx, sy, sz, col, top) {
      const x0 = cx - sx / 2, x1 = cx + sx / 2, y0 = cy - sy / 2, y1 = cy + sy / 2, z0 = cz - sz / 2, z1 = cz + sz / 2;
      this.hex([[x0, y0, z0], [x1, y0, z0], [x0, y1, z0], [x1, y1, z0], [x0, y0, z1], [x1, y0, z1], [x0, y1, z1], [x1, y1, z1]], col, top);
    }
    // 竖直棱台：底面 (sx0 × sz0) 在 y0，顶面 (sx1 × sz1) 在 y1（y0 < y1），顶面可偏移 (ox, oz)
    taper(cx, cz, y0, y1, sx0, sz0, sx1, sz1, col, ox, oz, top) {
      ox = ox || 0; oz = oz || 0;
      const a = sx0 / 2, b = sz0 / 2, c = sx1 / 2, d = sz1 / 2, tx = cx + ox, tz = cz + oz;
      this.hex([[cx - a, y0, cz - b], [cx + a, y0, cz - b], [tx - c, y1, tz - d], [tx + c, y1, tz - d],
        [cx - a, y0, cz + b], [cx + a, y0, cz + b], [tx - c, y1, tz + d], [tx + c, y1, tz + d]], col, top);
    }
    // 竖直棱柱 / 圆台
    prism(cx, cz, y0, y1, r0, r1, seg, col) {
      col = C(col);
      const top = sh(col, 0.1), bot = sh(col, -0.3);
      for (let i = 0; i < seg; i++) {
        const a0 = (i / seg) * Math.PI * 2, a1 = ((i + 1) / seg) * Math.PI * 2;
        const c0 = Math.cos(a0), s0 = Math.sin(a0), c1 = Math.cos(a1), s1 = Math.sin(a1);
        const b0 = [cx + c0 * r0, y0, cz - s0 * r0], b1 = [cx + c1 * r0, y0, cz - s1 * r0];
        const t0 = [cx + c0 * r1, y1, cz - s0 * r1], t1 = [cx + c1 * r1, y1, cz - s1 * r1];
        if (r1 > 1e-4) this.quad(b0, b1, t1, t0, i % 2 ? col : sh(col, -0.05)); else this.tri(b0, b1, t0, i % 2 ? col : sh(col, -0.05));
        if (r1 > 1e-4) this.tri([cx, y1, cz], t0, t1, top);
        if (r0 > 1e-4) this.tri([cx, y0, cz], b1, b0, bot);
      }
    }
    // 任意方向的棱柱：a → b
    rod(a, b, r0, r1, seg, col) {
      const A = new THREE.Vector3(a[0], a[1], a[2]), B = new THREE.Vector3(b[0], b[1], b[2]);
      const len = A.distanceTo(B);
      if (len < 1e-5) return;
      const dir = B.clone().sub(A).normalize();
      const q = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
      const local = new THREE.Matrix4().compose(A, q, new THREE.Vector3(1, 1, 1));
      const save = this.M;
      this.M = save ? save.clone().multiply(local) : local;
      this.prism(0, 0, 0, len, r0, r1, seg, col);
      this.M = save;
    }
    // x-y 平面上的多边形沿 z 拉伸（刀刃、旗面、披风）；以重心扇形三角化（适用于星形多边形）
    slab(pts, z0, z1, col, edge) {
      if (z0 > z1) { const t = z0; z0 = z1; z1 = t; }
      col = C(col); edge = edge ? C(edge) : sh(col, -0.15);
      let cx = 0, cy = 0;
      for (const p of pts) { cx += p[0]; cy += p[1]; }
      cx /= pts.length; cy /= pts.length;
      const n = pts.length;
      for (let i = 0; i < n; i++) {
        const p = pts[i], q = pts[(i + 1) % n];
        this.tri([cx, cy, z1], [p[0], p[1], z1], [q[0], q[1], z1], col);
        this.tri([cx, cy, z0], [q[0], q[1], z0], [p[0], p[1], z0], sh(col, -0.08));
        this.quad([p[0], p[1], z0], [q[0], q[1], z0], [q[0], q[1], z1], [p[0], p[1], z1], edge);
      }
    }
    // 低多边形椭球（八面体细分一次）
    blob(cx, cy, cz, rx, ry, rz, col, seed) {
      col = C(col);
      const r = SG.SeededRandom(seed || 1);
      const P = [[0, 1, 0], [0, -1, 0], [-1, 0, 0], [1, 0, 0], [0, 0, 1], [0, 0, -1]];
      const F = [[0, 4, 3], [0, 3, 5], [0, 5, 2], [0, 2, 4], [1, 3, 4], [1, 5, 3], [1, 2, 5], [1, 4, 2]];
      const nrm = (a, b) => { const x = a[0] + b[0], y = a[1] + b[1], z = a[2] + b[2], l = Math.hypot(x, y, z) || 1; return [x / l, y / l, z / l]; };
      for (const f of F) {
        const a = P[f[0]], b = P[f[1]], c = P[f[2]];
        const ab = nrm(a, b), bc = nrm(b, c), ca = nrm(c, a);
        for (const tr of [[a, ab, ca], [ab, b, bc], [ca, bc, c], [ab, bc, ca]]) {
          const k = 0.94 + r.nextDouble() * 0.12;
          const Q = tr.map(p => [cx + p[0] * rx * k, cy + p[1] * ry * k, cz + p[2] * rz * k]);
          this.tri(Q[0], Q[1], Q[2], sh(col, (tr[0][1] + tr[1][1] + tr[2][1]) * 0.05));
        }
      }
    }
    get empty() { return this.p.length === 0; }
    geo() {
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(this.p, 3));
      g.setAttribute('color', new THREE.Float32BufferAttribute(this.c, 3));
      g.computeVertexNormals();
      g.computeBoundingSphere();
      return g;
    }
  }

  // ============================================================ 视图：兵器 --
  // 兵器在局部坐标中沿 +y 伸出，握点（右手）在原点；+x 一侧为刃口。返回 { pb, tip, blade }
  const STEEL = '#cdd3da', EDGE = '#eef2f6', GOLD = '#c9a24e', RED = '#c8382c';
  function tassel(pb, y, col) {
    pb.blob(0, y, 0, 0.05, 0.035, 0.05, col, 3);
    for (let i = 0; i < 5; i++) {
      const a = i / 5 * Math.PI * 2;
      pb.rod([Math.cos(a) * 0.03, y - 0.01, Math.sin(a) * 0.03], [Math.cos(a) * 0.06, y - 0.14 - (i % 2) * 0.03, Math.sin(a) * 0.05], 0.014, 0.004, 3, col);
    }
  }
  function leaf(pb, y0, len, w, col) {
    pb.slab([[0, y0], [w, y0 + len * 0.22], [w * 0.7, y0 + len * 0.6], [0, y0 + len], [-w * 0.7, y0 + len * 0.6], [-w, y0 + len * 0.22]], -0.009, 0.009, col, EDGE);
    pb.box(0, y0 + len * 0.42, 0, 0.012, len * 0.7, 0.022, sh(col, -0.2));
  }
  function shaft(pb, y0, y1, col, r) {
    r = r || 0.022;
    pb.prism(0, 0, y0, y1, r, r * 0.92, 6, col);
    pb.prism(0, 0, y0 - 0.06, y0, r * 0.5, r * 1.1, 6, GOLD);   // 鐏
  }
  function jiHead(pb, y, k, col) {
    leaf(pb, y, 0.34 * k, 0.04 * k, col);
    pb.prism(0, 0, y - 0.05 * k, y + 0.02, 0.03 * k, 0.03 * k, 6, GOLD);
    const cres = s => [[0.02 * s, y - 0.06 * k], [0.11 * s * k, y - 0.1 * k], [0.21 * s * k, y - 0.04 * k], [0.25 * s * k, y + 0.05 * k], [0.19 * s * k, y + 0.12 * k], [0.12 * s * k, y + 0.06 * k], [0.03 * s, y + 0.03 * k]];
    pb.slab(cres(1), -0.008, 0.008, col, EDGE);
    pb.slab(cres(-1).reverse(), -0.008, 0.008, col, EDGE);
  }
  function buildWeapon(type, look) {
    const pb = new PB();
    const blade = look.goldTip ? '#e0b64a' : STEEL;
    let tip = 1.8, len = 0.45;
    switch (type) {
      case 'spear':
        shaft(pb, -0.6, 1.45, look.goldTip ? '#7a2a1c' : '#5a1e18');
        pb.prism(0, 0, 1.4, 1.48, 0.03, 0.026, 6, GOLD);
        tassel(pb, 1.42, look.weaponName === '涯角枪' ? '#e8e8ee' : RED);
        leaf(pb, 1.48, 0.42, 0.05, blade);
        tip = 1.9; len = 0.42;
        break;
      case 'snake': {
        shaft(pb, -0.6, 1.5, '#1e1a1a', 0.024);
        pb.prism(0, 0, 1.45, 1.53, 0.032, 0.028, 6, '#6a6a72');
        tassel(pb, 1.46, '#2a2a2a');
        // 蜿蜒的蛇形矛刃
        const N = 9, y0 = 1.52, L = 0.55;
        for (let i = 0; i < N; i++) {
          const t0 = i / N, t1 = (i + 1) / N;
          const xc = t => Math.sin(t * Math.PI * 3) * 0.045 * (1 - t * 0.6);
          const w = t => 0.04 * (1 - t) + 0.004;
          pb.slab([[xc(t0) - w(t0), y0 + t0 * L], [xc(t0) + w(t0), y0 + t0 * L], [xc(t1) + w(t1), y0 + t1 * L], [xc(t1) - w(t1), y0 + t1 * L]], -0.009, 0.009, STEEL, EDGE);
        }
        tip = y0 + L; len = L;
        break;
      }
      case 'ji':
        shaft(pb, -0.6, 1.42, '#7a2a1c');
        jiHead(pb, 1.46, 1, blade);
        tassel(pb, 1.32, RED);
        tip = 1.8; len = 0.45;
        break;
      case 'guandao':
        shaft(pb, -0.65, 1.22, '#3b2a1c', 0.024);
        pb.prism(0, 0, -0.66, -0.56, 0.0, 0.03, 6, GOLD);
        pb.blob(0, 1.24, 0, 0.055, 0.06, 0.05, '#2f7d4a', 5);          // 龙吞口
        pb.box(0.04, 1.27, 0, 0.05, 0.04, 0.03, GOLD);
        tassel(pb, 1.17, RED);
        pb.slab([[-0.03, 1.24], [0.07, 1.22], [0.17, 1.36], [0.21, 1.56], [0.18, 1.76], [0.06, 1.92], [0.02, 1.82], [-0.015, 1.62], [-0.04, 1.5], [-0.1, 1.46], [-0.05, 1.36]], -0.01, 0.01, STEEL, EDGE);
        pb.slab([[0.0, 1.32], [0.08, 1.33], [0.1, 1.42], [0.03, 1.44]], 0.01, 0.014, '#2f7d4a');
        tip = 1.9; len = 0.68;
        break;
      case 'poleblade':
        shaft(pb, -0.62, 1.2, '#4a2a1a');
        pb.prism(0, 0, 1.16, 1.24, 0.032, 0.03, 6, GOLD);
        tassel(pb, 1.14, RED);
        pb.slab([[-0.03, 1.22], [0.06, 1.22], [0.14, 1.4], [0.16, 1.62], [0.08, 1.82], [0.0, 1.72], [-0.03, 1.46]], -0.01, 0.01, STEEL, EDGE);
        tip = 1.8; len = 0.6;
        break;
      case 'axe':
        shaft(pb, -0.6, 1.48, '#4a2a1a', 0.025);
        leaf(pb, 1.48, 0.2, 0.03, STEEL);
        pb.slab([[0.02, 1.16], [0.1, 1.1], [0.24, 1.06], [0.3, 1.26], [0.25, 1.48], [0.11, 1.44], [0.02, 1.38]], -0.012, 0.012, STEEL, EDGE);
        pb.slab([[-0.02, 1.38], [-0.1, 1.33], [-0.02, 1.22]], -0.01, 0.01, '#8e939c');
        pb.prism(0, 0, 1.12, 1.44, 0.034, 0.034, 6, '#5c5c64');
        tip = 1.68; len = 0.4;
        break;
      case 'bigblade':
        pb.prism(0, 0, -0.5, 0.32, 0.026, 0.026, 6, '#3a2418');
        for (let y = -0.4; y < 0.3; y += 0.12) pb.prism(0, 0, y, y + 0.04, 0.03, 0.03, 6, '#7a2a1c');
        pb.box(0, 0.33, 0, 0.16, 0.035, 0.06, GOLD);
        pb.slab([[-0.025, 0.35], [0.075, 0.35], [0.1, 0.62], [0.115, 1.2], [0.09, 1.45], [0.0, 1.52], [-0.03, 1.32], [-0.035, 0.62]], -0.012, 0.012, STEEL, EDGE);
        tip = 1.5; len = 1.1;
        break;
      case 'sword':
      case 'twinsword':
        pb.prism(0, 0, -0.12, 0.06, 0.02, 0.02, 6, '#2a1c14');
        pb.blob(0, -0.14, 0, 0.03, 0.03, 0.03, GOLD, 2);
        pb.box(0, 0.075, 0, 0.13, 0.03, 0.045, GOLD);
        pb.slab([[-0.022, 0.09], [0.022, 0.09], [0.02, 0.84], [0, 0.93], [-0.02, 0.84]], -0.007, 0.007, STEEL, EDGE);
        pb.box(0, 0.48, 0, 0.008, 0.72, 0.016, sh(STEEL, -0.2));
        tip = 0.93; len = 0.82;
        break;
      case 'dao':
      case 'twindao':
        pb.prism(0, 0, -0.13, 0.06, 0.02, 0.02, 6, '#2a1c14');
        pb.prism(0, 0, -0.2, -0.13, 0.04, 0.04, 6, GOLD);            // 环首
        pb.box(0, 0.07, 0, 0.1, 0.03, 0.05, GOLD);
        pb.slab([[-0.02, 0.09], [0.025, 0.09], [0.042, 0.5], [0.05, 0.74], [0.02, 0.88], [-0.025, 0.8], [-0.022, 0.42]], -0.007, 0.007, STEEL, EDGE);
        tip = 0.86; len = 0.78;
        break;
      case 'shortji':
      case 'twinji':
        shaft(pb, -0.32, 0.72, type === 'twinji' ? '#2a2a30' : '#5a1e18');
        jiHead(pb, 0.76, 0.85, STEEL);
        tassel(pb, 0.66, RED);
        tip = 1.05; len = 0.36;
        break;
      case 'mace':
        pb.prism(0, 0, -0.28, 0.56, 0.024, 0.024, 6, '#3a2418');
        pb.blob(0, 0.66, 0, 0.1, 0.12, 0.1, '#55565e', 4);
        for (let i = 0; i < 8; i++) {
          const a = i / 8 * Math.PI * 2, y = 0.62 + (i % 2) * 0.08;
          pb.rod([Math.cos(a) * 0.08, y, Math.sin(a) * 0.08], [Math.cos(a) * 0.17, y + 0.02, Math.sin(a) * 0.17], 0.025, 0, 4, '#8e939c');
        }
        pb.rod([0, 0.76, 0], [0, 0.88, 0], 0.03, 0, 4, '#8e939c');
        tip = 0.86; len = 0.3;
        break;
      case 'gladius':   // 罗马短剑：宽刃尖头、圆柄头、骨柄
        pb.prism(0, 0, -0.11, 0.05, 0.022, 0.02, 6, '#e8dcc0');
        pb.blob(0, -0.13, 0, 0.04, 0.035, 0.04, '#c8a050', 3);
        pb.box(0, 0.065, 0, 0.1, 0.035, 0.05, '#c8a050');
        pb.slab([[-0.03, 0.08], [0.03, 0.08], [0.028, 0.5], [0.033, 0.58], [0, 0.7], [-0.033, 0.58], [-0.028, 0.5]], -0.008, 0.008, STEEL, EDGE);
        tip = 0.7; len = 0.6;
        break;
      case 'tachi':     // 倭直刀：单刃直身、圆镡、长柄
        pb.prism(0, 0, -0.2, 0.04, 0.02, 0.02, 6, '#2a1c14');
        pb.prism(0, 0, 0.04, 0.06, 0.06, 0.06, 8, '#4a4a50');
        pb.slab([[-0.018, 0.06], [0.024, 0.06], [0.024, 0.8], [-0.005, 0.9], [-0.018, 0.82]], -0.006, 0.006, STEEL, EDGE);
        tip = 0.9; len = 0.84;
        break;
      case 'handaxe':   // 日耳曼战斧：短柄 + 胡形斧头
        pb.prism(0, 0, -0.25, 0.66, 0.022, 0.02, 6, '#5a3a22');
        pb.slab([[0.0, 0.44], [0.12, 0.38], [0.2, 0.36], [0.22, 0.52], [0.18, 0.66], [0.1, 0.62], [0.0, 0.62]], -0.012, 0.012, STEEL, EDGE);
        pb.prism(0, 0, 0.42, 0.66, 0.03, 0.03, 6, '#5c5c64');
        tip = 0.7; len = 0.3;
        break;
      case 'lance':     // 骑矛：更长的矛杆、小矛头、三角旗
        shaft(pb, -0.7, 1.62, '#6a4a2a', 0.022);
        pb.prism(0, 0, 1.58, 1.65, 0.028, 0.024, 6, '#5c5c64');
        leaf(pb, 1.64, 0.36, 0.042, blade);
        pb.slab([[0, 1.32], [0.3, 1.4], [0, 1.52]], -0.004, 0.004, RED);
        tip = 2.0; len = 0.36;
        break;
      default:
        pb.box(0, 0.4, 0, 0.04, 0.8, 0.02, STEEL);
        tip = 0.8; len = 0.6;
    }
    return { pb, tip, len };
  }

  // 盾（左手持；局部 +x 为盾面朝向，原点在握把）。shape：scutum 罗马长方弧盾 / round 圆盾 / oval 椭圆盾 / wood 倭木盾
  function buildShield(shape, team) {
    const pb = new PB();
    const face = C(team), rim = C(GOLD);
    if (shape === 'scutum') {
      for (const k of [-1, 0, 1]) {   // 三片拼出弧面
        pb.at(-0.02 * Math.abs(k), 0, k * 0.15, 0, k * 0.32, 0);
        pb.box(0.0, 0, 0, 0.03, 0.78, 0.155, face);
        pb.box(0.017, 0.37, 0, 0.012, 0.03, 0.16, rim); pb.box(0.017, -0.37, 0, 0.012, 0.03, 0.16, rim);
        pb.at();
      }
      pb.blob(0.035, 0, 0, 0.06, 0.06, 0.06, rim, 71);
      for (const y of [-0.22, 0.22]) pb.box(0.018, y, 0, 0.012, 0.025, 0.4, sh(face, 0.35));
    } else if (shape === 'oval' || shape === 'round' || shape === 'wood') {
      const R = shape === 'round' ? 0.27 : 0.25, ry = shape === 'oval' ? 1.7 : shape === 'wood' ? 1.9 : 1;
      const pts = [];
      for (let i = 0; i < 12; i++) { const a = i / 12 * Math.PI * 2; pts.push([Math.cos(a) * R * (shape === 'wood' ? 0.85 : 1), Math.sin(a) * R * ry]); }
      if (shape === 'wood') { pts.length = 0; pts.push([-0.2, -0.45], [0.2, -0.45], [0.22, 0.45], [-0.22, 0.45]); }
      // slab 在 xy 平面挤出 z；绕 y 转 90° 让盾面朝 +x
      pb.at(0, 0, 0, 0, Math.PI / 2, 0);
      pb.slab(pts, -0.02, 0.02, shape === 'wood' ? C('#8a6038') : face, shape === 'wood' ? '#5a3a20' : sh(face, 0.3));
      if (shape === 'wood') { for (const y of [-0.25, 0, 0.25]) pb.box(0, y, 0.024, 0.4, 0.03, 0.01, '#3a2a1a'); pb.box(0, 0, 0.026, 0.05, 0.86, 0.01, '#c8382c'); }
      pb.at();
      if (shape !== 'wood') pb.blob(0.04, 0, 0, 0.065, 0.065, 0.065, rim, 73);
      if (shape === 'oval') pb.box(0.03, 0, 0, 0.012, 0.7, 0.04, sh(face, 0.4));
    }
    return pb;
  }

  // 手持的弓（握把在原点，弓臂沿 ±y，弓背朝 +x；弦另画）。返回弓梢位置供挂弦
  const BOW = { tipX: -0.075, tipY: 0.66 };
  function buildBow() {
    const pb = new PB();
    const lim = y => -0.12 * Math.pow(Math.min(1, Math.abs(y) / 0.55), 1.6);
    for (const sg of [1, -1]) {
      let prev = [0, 0, 0];
      for (let i = 1; i <= 6; i++) {
        const y = sg * 0.55 * i / 6, q = [lim(y), y, 0];
        pb.rod(prev, q, 0.02 - i * 0.0015, 0.02 - (i + 1) * 0.0015, 5, i % 3 === 0 ? GOLD : '#3a1e14');
        prev = q;
      }
      pb.rod(prev, [BOW.tipX, sg * BOW.tipY, 0], 0.01, 0.007, 4, '#e8dcc0');      // 反曲弓梢（角质）
    }
    pb.prism(0, 0, -0.07, 0.07, 0.024, 0.024, 6, '#c8a050');                       // 握把缠绳
    return pb;
  }
  // 箭（沿 +x，箭尾在原点）
  function buildArrow(fin) {
    const pb = new PB();
    pb.rod([0, 0, 0], [0.8, 0, 0], 0.008, 0.008, 4, '#d8c8a0');
    pb.slab([[0.78, -0.022], [0.92, 0], [0.78, 0.022], [0.8, 0]], -0.004, 0.004, fin ? '#ffe08a' : STEEL, EDGE);
    for (const k of [-1, 1]) pb.slab([[0.02, 0], [0.16, 0], [0.12, k * 0.035], [0.0, k * 0.035]], -0.003, 0.003, k > 0 ? RED : '#f4efe4');
    pb.slab([[0.02, -0.003], [0.16, -0.003], [0.12, 0.003], [0.0, 0.003]], -0.035, 0.035, '#f4efe4');
    return pb;
  }

  // ============================================================ 视图：武将模型 --
  // 局部坐标：+x 为正面，+y 向上，+z 为右侧（面向右时朝向镜头）。所有肢体都直接挂在 body 下，由 IK 摆放。
  const LEN = { thigh: 0.46, shin: 0.44, upper: 0.3, fore: 0.28 };
  const TURN = 0.42;          // 3/4 侧身角度（向镜头转）

  function buildParts(look, team, war) {
    const bk = look.bulk;
    const teamC = C(team);
    const armor = look.armor ? C(look.armor) : teamC;
    const armorD = sh(armor, -0.42);
    const metal = C(look.metal);
    const trim = C(war >= 90 ? '#dcb85c' : '#b89a58');
    // 袍服文化：袍色与势力色相混，保证两边仍可分辨
    const robe = look.robe ? C(look.robe) : look.coat && look.torso === 'coat' ? mixC(C(look.coat), teamC, 0.2) : null;
    const cloth = robe ? sh(robe, -0.25) : look.torso === 'segm' ? sh(teamC, -0.08) : mixC(sh(teamC, -0.6), C('#3a2e28'), 0.5);
    const skin = C(look.skin), hair = C(look.hair);
    const leather = C('#4a3222'), boot = C('#241b16');
    const capeC = look.cape ? C(look.cape) : sh(teamC, -0.18);
    const nan = look.culture === 'nanman' || look.culture === 'yi' || look.culture === 'seasia';
    const P = {};

    // ---- 骨盆 / 腰裙
    let pb = new PB();
    pb.taper(0, 0, -0.1, 0.06, 0.24 * bk, 0.31 * bk, 0.25 * bk, 0.31 * bk, cloth);
    pb.taper(0, 0, 0.01, 0.09, 0.27 * bk, 0.34 * bk, 0.27 * bk, 0.34 * bk, look.bare || look.belly ? C('#5a3a24') : leather);
    pb.box(0.14 * bk, 0.05, 0, 0.035, 0.075, 0.1, trim);
    if (robe) {
      pb.at(0.1 * bk, 0.02, 0, 0, 0, 0.1); pb.taper(0, 0, -0.5, 0, 0.03, 0.3 * bk, 0.03, 0.26 * bk, robe); pb.at();
      pb.at(-0.11 * bk, 0.02, 0, 0, 0, -0.12); pb.taper(0, 0, -0.52, 0, 0.03, 0.32 * bk, 0.03, 0.28 * bk, sh(robe, -0.1)); pb.at();
    } else if (nan) {
      pb.at(0.12 * bk, 0.02, 0, 0, 0, 0.1); pb.taper(0, 0, -0.3, 0, 0.025, 0.2 * bk, 0.025, 0.18 * bk, '#8a6a3a'); pb.at();
      pb.at(-0.12 * bk, 0.02, 0, 0, 0, -0.1); pb.taper(0, 0, -0.3, 0, 0.025, 0.22 * bk, 0.025, 0.2 * bk, '#6a4a2a'); pb.at();
    } else {
      pb.at(0.125 * bk, 0.02, 0, 0, 0, 0.12); pb.taper(0, 0, -0.3, 0, 0.03, 0.22 * bk, 0.03, 0.2 * bk, armor); pb.box(0.016, -0.15, 0, 0.012, 0.02, 0.21 * bk, armorD); pb.at();
      pb.at(-0.125 * bk, 0.02, 0, 0, 0, -0.12); pb.taper(0, 0, -0.32, 0, 0.03, 0.24 * bk, 0.03, 0.22 * bk, sh(armor, -0.08)); pb.at();
    }
    if (look.bells) for (const z of [-0.12, 0.12]) pb.blob(0.08, 0.0, z * bk, 0.025, 0.025, 0.025, GOLD, 7);
    P.pelvis = pb;

    // ---- 躯干（原点在腰）
    pb = new PB();
    const chestC = look.bare ? skin : robe || (look.rattan ? C('#b08a4a') : nan ? skin : armor);
    if (look.belly) {
      pb.taper(0.02, 0, 0.0, 0.2, 0.3 * bk, 0.34 * bk, 0.31 * bk, 0.36 * bk, chestC);
      pb.blob(0.1 * bk, 0.13, 0, 0.13 * bk, 0.13, 0.17 * bk, chestC, 9);
    } else pb.taper(0, 0, 0.0, 0.17, 0.22 * bk, 0.29 * bk, 0.24 * bk, 0.32 * bk, chestC);
    pb.taper(0, 0, 0.17, 0.44, 0.24 * bk, 0.32 * bk, 0.27 * bk, 0.44 * bk, chestC);
    pb.taper(0, 0, 0.44, 0.5, 0.27 * bk, 0.44 * bk, 0.2 * bk, 0.26 * bk, look.bare || nan ? skin : robe ? sh(robe, -0.1) : armorD);
    if (look.bare) {
      for (const z of [-0.09, 0.09]) pb.box(0.12 * bk, 0.34, z * bk, 0.04, 0.1, 0.15 * bk, sh(skin, -0.06));
      pb.at(0.0, 0.3, 0, 0.6, 0, 0); pb.box(0, 0, 0, 0.29 * bk, 0.06, 0.5 * bk, '#5a3a24'); pb.at();   // 斜挎带
    } else if (look.rattan) {
      for (let y = 0.04; y < 0.44; y += 0.07) pb.taper(0, 0, y, y + 0.02, 0.25 * bk, 0.34 * bk, 0.26 * bk, 0.36 * bk, '#8a6a34');
    } else if (nan) {
      // 兽皮斜披 + 骨饰项链
      pb.at(0.0, 0.3, 0.02, -0.55, 0, 0); pb.box(0, 0, 0, 0.29 * bk, 0.14, 0.47 * bk, '#a07a44'); pb.at();
      for (let i = 0; i < 4; i++) pb.blob(0.1 * bk + (i % 2) * 0.01, 0.2 + i * 0.07, 0.06 - i * 0.04, 0.02, 0.02, 0.02, '#5a3a20', 11 + i);
      for (let i = -3; i <= 3; i++) pb.rod([0.12 * bk, 0.47, i * 0.035], [0.14 * bk, 0.4 - Math.abs(i) * 0.005, i * 0.04], 0.012, 0.002, 3, '#efe6d0');
    } else if (look.torso === 'segm' && !robe) {
      // 罗马环片甲：钢片横带（深色接缝）+ 胸前搭扣
      for (let y = 0.04; y < 0.44; y += 0.055) pb.taper(0, 0, y, y + 0.01, 0.226 * bk + y * 0.06, 0.3 * bk + y * 0.3, 0.226 * bk + y * 0.06, 0.3 * bk + y * 0.3, armorD);
      for (const z of [-0.05, 0.05]) pb.box(0.13 * bk, 0.34, z * bk, 0.012, 0.16, 0.02, GOLD);
      pb.box(0.125 * bk, 0.02, 0, 0.012, 0.05, 0.27 * bk, leather);
    } else if (look.torso === 'scale' && !robe) {
      // 鱼鳞甲：错位的小甲片
      for (let r = 0; r < 6; r++) {
        const y = 0.06 + r * 0.065, x = 0.115 * bk + r * 0.005;
        for (let k = -2; k <= 2; k++) pb.box(x, y, (k + (r % 2) * 0.5 - 0.25) * 0.055 * bk, 0.014, 0.05, 0.045, (k + r) % 2 ? metal : armorD);
      }
      pb.box(0.125 * bk, 0.02, 0, 0.012, 0.05, 0.27 * bk, leather);
    } else {
      // 札甲：横向甲片带 + 护心镜
      for (const y of [0.05, 0.12, 0.24, 0.31, 0.38]) pb.taper(0, 0, y, y + 0.012, 0.226 * bk + y * 0.06, 0.3 * bk + y * 0.3, 0.226 * bk + y * 0.06, 0.3 * bk + y * 0.3, robe ? sh(robe, -0.2) : armorD);
      pb.at(0.135 * bk, 0.31, 0, 0, 0, -Math.PI / 2); pb.prism(0, 0, -0.01, 0.025, 0.075, 0.07, 8, metal); pb.prism(0, 0, 0.024, 0.032, 0.05, 0.04, 8, trim); pb.at();
      if (!robe) pb.box(0.125 * bk, 0.22, 0, 0.012, 0.26, 0.035, leather);
      if (robe) {  // 外袍 + 胸甲
        pb.at(0.125 * bk, 0.32, 0.1 * bk, 0, 0.0, 0); pb.box(0, 0, 0, 0.025, 0.14, 0.12, metal); pb.at();
      }
    }
    pb.prism(0, 0, 0.48, 0.58, 0.058, 0.052, 6, skin);
    if (!look.bare && !nan) pb.taper(0, 0, 0.46, 0.53, 0.17 * bk, 0.21 * bk, 0.14, 0.17, robe ? robe : look.torso === 'segm' ? cloth : C('#8a2a20'));
    // 文化饰物：彩绘 / 毛皮领 / 勾玉 / 金饰
    if (look.paint) {
      const pc = C(look.paint);
      for (const z of [-0.07, 0.07]) { pb.at(0.148 * bk, 0.26, z * bk, 0, 0, 0); pb.box(0, 0, 0, 0.014, 0.2, 0.025, pc); pb.box(0, 0.06, z > 0 ? 0.03 : -0.03, 0.014, 0.025, 0.07, pc); pb.at(); }
      pb.box(0.14 * bk, 0.12, 0, 0.014, 0.03, 0.2 * bk, pc);
      for (const y of [0.32, 0.38]) pb.box(0.0, y, 0.15 * bk, 0.12, 0.022, 0.014, pc);
    }
    if (look.fur) { pb.taper(0, 0, 0.42, 0.52, 0.29 * bk, 0.46 * bk, 0.24 * bk, 0.34 * bk, '#8a6a46'); pb.blob(-0.02, 0.48, 0, 0.16 * bk, 0.06, 0.24 * bk, '#a08058', 41); }
    if (look.magatama || look.shells) for (let i = -2; i <= 2; i++) pb.blob(0.14 * bk, 0.4 - Math.abs(i) * 0.025, i * 0.045, 0.018, 0.026, 0.016, look.shells ? (i % 2 ? '#d8703a' : '#f4ece0') : i % 2 ? '#e8dcc0' : '#3aa060', 43 + i);
    if (look.gold) { pb.taper(0, 0, 0.44, 0.49, 0.29 * bk, 0.45 * bk, 0.25 * bk, 0.36 * bk, GOLD); pb.box(0.135 * bk, 0.38, 0, 0.014, 0.06, 0.1, GOLD); }
    if (look.bow) {   // 箭囊（背弓单独成件，放箭时取到手上）
      pb.at(-0.17 * bk, 0.28, 0.1, 0.4, 0, 0); pb.box(0, 0, 0, 0.08, 0.36, 0.08, '#6a4a2a'); for (let i = 0; i < 3; i++) pb.rod([0, 0.18, -0.02 + i * 0.02], [0, 0.3, -0.02 + i * 0.02], 0.006, 0.006, 3, '#e8dcc0'); pb.at();
      const bb = new PB();
      for (let i = 0; i < 6; i++) {
        const a0 = -1.1 + i * 0.37, a1 = a0 + 0.37;
        bb.rod([-0.17 * bk + Math.cos(a0) * 0.04, 0.24 + Math.sin(a0) * 0.42, -0.04 + Math.cos(a0) * 0.18], [-0.17 * bk + Math.cos(a1) * 0.04, 0.24 + Math.sin(a1) * 0.42, -0.04 + Math.cos(a1) * 0.18], 0.012, 0.012, 4, '#5a3a20');
      }
      P.backbow = bb;
    }
    P.torso = pb;

    // ---- 头（原点在颈根）
    pb = new PB();
    const face = look.skin;
    pb.taper(0.012, 0, 0.04, 0.13, 0.15, 0.13, 0.19, 0.17, face);
    pb.taper(0.0, 0, 0.13, 0.28, 0.19, 0.17, 0.18, 0.165, face);
    pb.box(0.105, 0.135, 0, 0.035, 0.055, 0.035, sh(face, -0.06));                   // 鼻
    for (const z of [-0.042, 0.042]) {
      pb.box(0.094, 0.168, z, 0.012, 0.022, 0.034, '#1a1414');                      // 眼
      pb.box(0.097, 0.195, z, 0.016, 0.016, 0.052, hair);                           // 眉
      pb.box(-0.005, 0.15, z * 2.25, 0.045, 0.06, 0.022, sh(face, -0.04));           // 耳
    }
    pb.box(0.096, 0.08, 0, 0.012, 0.012, 0.055, '#6a2a22');
    pb.box(-0.055, 0.17, 0, 0.1, 0.17, 0.17, hair);                                   // 脑后发
    if (look.eyepatch) { pb.box(0.1, 0.168, 0.044, 0.018, 0.04, 0.044, '#101010'); pb.at(0.0, 0.2, 0, 0, 0, -0.35); pb.box(0, 0, 0, 0.21, 0.012, 0.18, '#101010'); pb.at(); }
    switch (look.beard) {
      case 'short': pb.box(0.075, 0.055, 0, 0.065, 0.06, 0.12, hair); pb.box(0.104, 0.096, 0, 0.02, 0.016, 0.08, hair); break;
      case 'long':
        pb.box(0.104, 0.096, 0, 0.02, 0.016, 0.09, hair);
        pb.taper(0.085, 0, -0.26, 0.08, 0.035, 0.05, 0.07, 0.15, hair, -0.01, 0);
        pb.box(0.05, 0.08, 0, 0.08, 0.08, 0.17, hair);
        break;
      case 'bushy':
        pb.blob(0.055, 0.065, 0, 0.1, 0.075, 0.115, hair, 21);
        for (let i = 0; i < 7; i++) {
          const a = -1.2 + i * 0.4;
          pb.rod([0.07, 0.06, Math.sin(a) * 0.07], [0.12 + Math.cos(a) * 0.04, -0.04 - Math.cos(a) * 0.03, Math.sin(a) * 0.16], 0.035, 0.0, 4, hair);
        }
        break;
      case 'stubble': pb.box(0.06, 0.06, 0, 0.09, 0.06, 0.16, sh(face, -0.25)); break;
      default: break;
    }
    buildHelm(pb, look, { metal, trim, armorD, team: teamC, hair, skin, war });
    P.head = pb;

    // ---- 手臂（sd：+1 右 / −1 左；局部 +z 为外侧）
    for (const sd of [1, -1]) {
      pb = new PB();
      const bareA = look.bareArms || look.bare || nan;
      pb.taper(0, 0, -LEN.upper, 0.02, 0.085 * bk, 0.09 * bk, 0.105 * bk, 0.11 * bk, bareA ? skin : cloth);
      if (!bareA || look.culture === 'han') {
        // 披膊：肩顶 + 外侧垂片
        pb.taper(0, sd * 0.02, -0.03, 0.07, 0.17 * bk, 0.16 * bk, 0.12 * bk, 0.11 * bk, look.rattan ? '#8a6a34' : armor);
        pb.at(0, -0.01, sd * 0.07 * bk, sd * -0.32, 0, 0);
        pb.taper(0, 0, -0.19, 0, 0.18 * bk, 0.026, 0.17 * bk, 0.026, look.rattan ? '#8a6a34' : armor);
        pb.box(0, -0.1, sd * 0.016, 0.18 * bk, 0.014, 0.012, armorD);
        pb.box(0, -0.185, 0, 0.185 * bk, 0.02, 0.03, trim);
        pb.at();
      } else if (nan) pb.blob(0, 0.0, sd * 0.03, 0.09 * bk, 0.06, 0.08 * bk, '#a07a44', 31);
      P[sd > 0 ? 'upperR' : 'upperL'] = pb;
      pb = new PB();
      pb.taper(0, 0, -0.245, 0, 0.08 * bk, 0.08 * bk, 0.09 * bk, 0.09 * bk, bareA ? skin : (look.rattan ? '#8a6a34' : leather));
      if (!bareA) pb.taper(0, 0, -0.2, -0.04, 0.095 * bk, 0.095 * bk, 0.1 * bk, 0.1 * bk, metal);
      else pb.taper(0, 0, -0.24, -0.17, 0.09 * bk, 0.09 * bk, 0.09 * bk, 0.09 * bk, leather);
      pb.box(0, -LEN.fore, 0, 0.095, 0.095, 0.09, skin);
      P[sd > 0 ? 'foreR' : 'foreL'] = pb;
    }
    // ---- 腿
    for (const sd of [1, -1]) {
      pb = new PB();
      pb.taper(0, 0, -LEN.thigh, 0.02, 0.12 * bk, 0.12 * bk, 0.155 * bk, 0.15 * bk, cloth);
      if (!look.bare && !nan && !robe) {
        pb.at(0.005, -0.0, sd * 0.085 * bk, sd * -0.12, 0, 0);
        pb.taper(0, 0, -0.32, 0.02, 0.19 * bk, 0.03, 0.2 * bk, 0.03, armor);
        for (const y of [-0.1, -0.2]) pb.box(0, y, sd * 0.017, 0.2 * bk, 0.014, 0.01, armorD);
        pb.box(0, -0.315, 0, 0.2 * bk, 0.02, 0.034, trim);
        pb.at();
      }
      P[sd > 0 ? 'thighR' : 'thighL'] = pb;
      pb = new PB();
      pb.taper(0, 0, -0.42, 0.0, 0.1 * bk, 0.1 * bk, 0.12 * bk, 0.115 * bk, nan ? skin : leather);
      if (!nan) pb.taper(0.012, 0, -0.33, -0.04, 0.1 * bk, 0.11 * bk, 0.11 * bk, 0.12 * bk, metal);
      pb.box(0.05, -0.02, 0, 0.06, 0.08, 0.1, nan ? sh(skin, -0.1) : metal);
      P[sd > 0 ? 'shinR' : 'shinL'] = pb;
      pb = new PB();
      pb.box(0.05, -0.035, 0, 0.24, 0.07, 0.11, nan ? sh(skin, -0.12) : boot);
      if (!nan) { pb.box(0.0, 0.02, 0, 0.13, 0.08, 0.12, boot); pb.box(0.175, -0.02, 0, 0.04, 0.05, 0.09, sh(boot, 0.15)); }
      P[sd > 0 ? 'footR' : 'footL'] = pb;
    }
    // ---- 披风（两段，分别摆动）
    if (look.plaid) {   // 方格斗篷（凯尔特）：赤膊也披
      const lc = sh(capeC, 0.35), dc = sh(capeC, -0.35);
      pb = new PB(); pb.taper(-0.015, 0, -0.5, 0.0, 0.03, 0.46 * bk, 0.03, 0.4 * bk, capeC);
      for (const z of [-0.12, 0.0, 0.12]) pb.box(-0.032, -0.25, z * bk, 0.012, 0.5, 0.03, lc);
      for (const y of [-0.1, -0.3]) pb.box(-0.034, y, 0, 0.012, 0.03, 0.42 * bk, dc);
      P.cape1 = pb;
      pb = new PB(); pb.taper(0, 0, -0.42, 0.0, 0.025, 0.5 * bk, 0.03, 0.46 * bk, sh(capeC, -0.12));
      for (const z of [-0.12, 0.0, 0.12]) pb.box(-0.018, -0.21, z * bk, 0.012, 0.42, 0.03, lc);
      pb.box(-0.02, -0.2, 0, 0.012, 0.03, 0.46 * bk, dc);
      P.cape2 = pb;
    } else if (!nan && !look.bare) {
      pb = new PB(); pb.taper(-0.015, 0, -0.5, 0.0, 0.03, 0.46 * bk, 0.03, 0.4 * bk, capeC); P.cape1 = pb;
      pb = new PB(); pb.taper(0, 0, -0.42, 0.0, 0.025, 0.5 * bk, 0.03, 0.46 * bk, sh(capeC, -0.12)); P.cape2 = pb;
    }
    return P;
  }

  // 头盔 / 冠帽 / 发型（写在头部网格里；原点在颈根，头顶约 y = 0.28）
  function buildHelm(pb, look, K) {
    const helmC = look.helmColor ? C(look.helmColor) : K.metal;
    const plume = (col) => {
      pb.rod([0, 0.42, 0], [-0.08, 0.5, 0], 0.035, 0.03, 5, col);
      pb.rod([-0.08, 0.5, 0], [-0.22, 0.5, 0], 0.03, 0.022, 5, sh(col, -0.06));
      pb.rod([-0.22, 0.5, 0], [-0.34, 0.42, 0], 0.022, 0.004, 5, sh(col, -0.12));
    };
    const hanHelm = (col, crest) => {
      pb.taper(0, 0, 0.19, 0.3, 0.215, 0.2, 0.17, 0.16, col);
      pb.taper(0, 0, 0.3, 0.37, 0.17, 0.16, 0.07, 0.07, sh(col, 0.06));
      pb.taper(0, 0, 0.185, 0.225, 0.225, 0.21, 0.222, 0.208, K.trim);
      pb.box(0.115, 0.205, 0, 0.04, 0.016, 0.19, K.trim);                         // 眉庇
      pb.at(-0.105, 0.22, 0, 0, 0, -0.32); pb.taper(0, 0, -0.15, 0, 0.03, 0.22, 0.03, 0.2, K.armorD); pb.at();   // 顿项
      for (const s of [-1, 1]) { pb.at(0.0, 0.22, s * 0.1, s * 0.25, 0, 0); pb.taper(0, 0, -0.13, 0, 0.13, 0.025, 0.15, 0.025, K.armorD); pb.at(); }
      pb.prism(0, 0, 0.36, 0.45, 0.014, 0.008, 4, K.trim);
      if (crest !== false) {
        pb.blob(0, 0.44, 0, 0.05, 0.03, 0.05, RED, 13);
        for (let i = 0; i < 6; i++) { const a = i / 6 * Math.PI * 2; pb.rod([0, 0.44, 0], [Math.cos(a) * 0.07 - 0.03, 0.33, Math.sin(a) * 0.07], 0.016, 0.004, 3, RED); }
      }
    };
    switch (look.helm) {
      case 'han': hanHelm(helmC); break;
      case 'plume': hanHelm(helmC, false); plume(C(look.plume || '#f0f0f0')); break;
      case 'crown': {
        hanHelm(C(GOLD), false);
        // 凤翅：头盔两侧向上后方翻卷的金翅
        for (const s of [-1, 1]) pb.slab([[0.04, 0.26], [0.02, 0.34], [-0.06, 0.44], [-0.1, 0.42], [-0.08, 0.32], [-0.03, 0.25]], s * 0.105, s * 0.117, GOLD, '#e8cc80');
        pb.blob(0.0, 0.4, 0, 0.035, 0.03, 0.035, RED, 17);
        break;
      }
      case 'pheasant': {
        // 吕布：紫金冠 + 两根长雉翎
        hanHelm(C('#d9a83e'), false);
        pb.box(0.1, 0.3, 0, 0.03, 0.08, 0.08, RED);
        for (const s of [-1, 1]) {
          let prev = [0.03, 0.36, s * 0.04];
          for (let i = 1; i <= 12; i++) {
            const t = i / 12;
            const p = [0.03 - 0.62 * t + 0.25 * t * t, 0.36 + 0.95 * t - 0.42 * t * t, s * (0.04 + 0.06 * t)];
            pb.rod(prev, p, 0.016 * (1 - t * 0.6), 0.016 * (1 - t * 0.6), 4, i % 2 ? '#8a5a2a' : '#e8dcc0');
            prev = p;
          }
        }
        break;
      }
      case 'lion': {
        // 马超：狮盔
        pb.taper(0, 0, 0.19, 0.31, 0.22, 0.205, 0.17, 0.16, '#e8eaee');
        pb.taper(0, 0, 0.31, 0.37, 0.17, 0.16, 0.08, 0.08, '#e8eaee');
        pb.box(0.115, 0.27, 0, 0.04, 0.09, 0.14, GOLD);
        pb.box(0.137, 0.29, 0, 0.012, 0.02, 0.1, '#101010');
        for (let i = 0; i < 9; i++) {
          const a = -1.3 + i * 0.32;
          pb.rod([-0.03, 0.3, Math.sin(a) * 0.08], [-0.2 - Math.cos(a) * 0.05, 0.2 + Math.cos(a) * 0.12, Math.sin(a) * 0.2], 0.045, 0.0, 4, i % 2 ? '#f4f4f4' : '#dcdcdc');
        }
        pb.prism(0, 0, 0.37, 0.43, 0.014, 0.008, 4, GOLD);
        break;
      }
      case 'scarf': {
        pb.taper(0, 0, 0.19, 0.3, 0.215, 0.2, 0.2, 0.185, helmC);
        pb.blob(-0.02, 0.32, 0, 0.09, 0.06, 0.09, sh(helmC, 0.05), 19);
        pb.blob(-0.1, 0.27, 0, 0.04, 0.04, 0.04, sh(helmC, -0.1), 23);
        for (const s of [-1, 1]) { pb.at(-0.12, 0.26, s * 0.035, 0, 0, -0.45 - s * 0.08); pb.box(0, -0.12, 0, 0.022, 0.24, 0.045, sh(helmC, -0.15)); pb.at(); }
        break;
      }
      case 'guan':
        pb.taper(0, 0, 0.2, 0.4, 0.2, 0.185, 0.17, 0.13, '#2a2a3a', -0.03, 0);
        pb.box(-0.02, 0.37, 0, 0.18, 0.02, 0.15, '#3a3a50');
        break;
      case 'bald':
        pb.taper(0, 0, 0.27, 0.3, 0.17, 0.155, 0.12, 0.1, K.skin);
        pb.blob(-0.03, 0.32, 0, 0.04, 0.04, 0.04, K.hair, 29);
        break;
      case 'topknot': {
        pb.taper(0, 0, 0.2, 0.3, 0.2, 0.18, 0.17, 0.16, K.hair);
        pb.blob(0.0, 0.35, 0, 0.06, 0.07, 0.06, K.hair, 33);
        pb.taper(0, 0, 0.21, 0.25, 0.205, 0.188, 0.205, 0.188, '#b8302a');
        for (const z of [-0.07, 0, 0.07]) pb.rod([0.1, 0.23, z], [0.135, 0.2, z * 1.1], 0.012, 0.0, 3, '#efe6d0');   // 兽牙
        const fc = ['#d43a2a', '#e8b030', '#2a7ad4'];
        for (let i = 0; i < 3; i++) pb.rod([-0.02, 0.36, (i - 1) * 0.03], [-0.12 - i * 0.04, 0.62 - i * 0.06, (i - 1) * 0.08], 0.02, 0.004, 4, fc[i]);
        break;
      }
      case 'fur':
        pb.taper(0, 0, 0.2, 0.42, 0.19, 0.18, 0.06, 0.06, '#7a4a2a');
        pb.taper(0, 0, 0.18, 0.25, 0.24, 0.22, 0.23, 0.21, '#c8b090');
        break;
      case 'feather':
        pb.taper(0, 0, 0.2, 0.36, 0.2, 0.18, 0.12, 0.1, '#3a2a20');
        for (const s of [-1, 1]) pb.rod([0, 0.34, s * 0.04], [-0.02, 0.6, s * 0.08], 0.018, 0.004, 4, '#f0ead8');
        break;
      case 'mizura':
        pb.taper(0, 0, 0.2, 0.3, 0.2, 0.18, 0.17, 0.16, K.hair);
        for (const s of [-1, 1]) pb.blob(0.0, 0.1, s * 0.11, 0.035, 0.08, 0.03, K.hair, 37);
        pb.taper(0, 0, 0.22, 0.25, 0.205, 0.188, 0.205, 0.188, '#e8e0cc');
        break;
      case 'galea':
        // 罗马头盔：铜盔 + 横向红缨 + 护颊 + 护颈
        pb.taper(0, 0, 0.19, 0.32, 0.22, 0.2, 0.16, 0.15, '#c8a050');
        pb.slab([[0.08, 0.32], [0.02, 0.44], [-0.08, 0.46], [-0.16, 0.38], [-0.1, 0.32]], -0.02, 0.02, RED);
        pb.box(0.11, 0.21, 0, 0.03, 0.02, 0.2, '#b08a40');
        for (const z of [-0.1, 0.1]) pb.box(0.05, 0.12, z, 0.1, 0.13, 0.018, '#c8a050');
        pb.at(-0.1, 0.2, 0, 0, 0, -0.5); pb.box(0, -0.04, 0, 0.02, 0.1, 0.24, '#b08a40'); pb.at();
        break;
      case 'tiara':
        // 安息提亚拉冠：高圆冠 + 金箍 + 护颈垂巾；冠下露出卷发
        pb.taper(0, 0, 0.19, 0.42, 0.21, 0.19, 0.12, 0.12, '#e0d8c8', 0.03, 0);
        pb.taper(0, 0, 0.19, 0.23, 0.22, 0.2, 0.22, 0.2, GOLD);
        for (let i = 0; i < 4; i++) pb.blob(0.0, 0.3 + i * 0.03, 0, 0.012, 0.012, 0.012, i % 2 ? RED : GOLD, 51 + i);
        pb.at(-0.1, 0.2, 0, 0, 0, -0.25); pb.taper(0, 0, -0.16, 0, 0.03, 0.24, 0.03, 0.2, '#d8ccb0'); pb.at();
        for (const s of [-1, 1]) pb.blob(-0.02, 0.15, s * 0.1, 0.05, 0.07, 0.035, K.hair, 53);
        break;
      case 'yifeather': {
        // 夷洲：长发 + 编织头带插一圈羽毛
        pb.taper(0, 0, 0.19, 0.3, 0.205, 0.19, 0.18, 0.17, K.hair);
        pb.box(-0.09, 0.08, 0, 0.08, 0.26, 0.17, K.hair);
        pb.taper(0, 0, 0.21, 0.25, 0.212, 0.196, 0.212, 0.196, '#d8b060');
        for (let i = 0; i < 7; i++) {
          const a = -1.25 + i * 0.42;
          pb.rod([-0.02, 0.26, Math.sin(a) * 0.1], [-0.06 - Math.cos(a) * 0.02, 0.5 + Math.cos(a) * 0.06, Math.sin(a) * 0.2], 0.022, 0.004, 4, i % 2 ? '#f4f0e8' : '#1a1a1a');
        }
        break;
      }
      case 'goldcrown':
        // 林邑 / 扶南：尖塔形金高冠 + 耳饰
        pb.taper(0, 0, 0.2, 0.28, 0.2, 0.18, 0.18, 0.165, GOLD);
        pb.taper(0, 0, 0.28, 0.52, 0.16, 0.15, 0.02, 0.02, '#e8c060');
        for (let i = 0; i < 3; i++) pb.taper(0, 0, 0.32 + i * 0.06, 0.34 + i * 0.06, 0.15 - i * 0.04, 0.14 - i * 0.04, 0.14 - i * 0.04, 0.13 - i * 0.04, sh(C(GOLD), -0.2));
        for (const z of [-0.11, 0.11]) pb.blob(0.0, 0.1, z, 0.02, 0.03, 0.02, GOLD, 57);
        break;
      case 'hutmao':
        // 西域胡帽：翻檐尖顶毡帽
        pb.taper(0, 0, 0.19, 0.25, 0.235, 0.22, 0.225, 0.21, '#e8dcc0');
        pb.taper(0.0, 0, 0.24, 0.5, 0.19, 0.18, 0.03, 0.03, helmC === K.metal ? C('#a0383a') : helmC, -0.05, 0);
        pb.box(0.11, 0.23, 0, 0.03, 0.08, 0.12, '#e8dcc0');
        break;
      case 'pointed':
        // 贵霜 / 康居：高尖帽（顶向后弯）+ 金箍
        pb.taper(0, 0, 0.19, 0.24, 0.215, 0.2, 0.21, 0.195, GOLD);
        pb.taper(0, 0, 0.24, 0.44, 0.2, 0.19, 0.08, 0.08, '#e8d8b0', -0.03, 0);
        pb.rod([-0.03, 0.44, 0], [-0.12, 0.6, 0], 0.04, 0.012, 5, '#e8d8b0');
        break;
      case 'kufiya':
        // 阿拉伯头巾：裹头 + 垂到肩后的头巾 + 黑色头箍
        pb.taper(0, 0, 0.19, 0.31, 0.225, 0.21, 0.2, 0.19, helmC);
        pb.blob(-0.01, 0.31, 0, 0.1, 0.05, 0.1, helmC, 59);
        pb.taper(0, 0, 0.24, 0.27, 0.232, 0.216, 0.232, 0.216, '#1a1414');
        pb.at(-0.11, 0.26, 0, 0, 0, -0.18); pb.taper(0, 0, -0.3, 0, 0.03, 0.24, 0.03, 0.2, sh(helmC, -0.08)); pb.at();
        for (const sd of [-1, 1]) { pb.at(-0.02, 0.24, sd * 0.11, sd * 0.1, 0, 0); pb.box(0, -0.12, 0, 0.12, 0.24, 0.02, sh(helmC, -0.05)); pb.at(); }
        break;
      case 'celtic':
        // 凯尔特：石灰硬化的蓬乱长发，向后刺出
        pb.taper(0, 0, 0.19, 0.3, 0.205, 0.19, 0.18, 0.17, K.hair);
        for (let i = 0; i < 7; i++) {
          const a = -1.2 + i * 0.4;
          pb.rod([-0.02, 0.28, Math.sin(a) * 0.06], [-0.2 - Math.cos(a) * 0.04, 0.4 + Math.cos(a) * 0.06, Math.sin(a) * 0.16], 0.04, 0.0, 4, sh(K.hair, 0.15 * (i % 2)));
        }
        pb.box(-0.09, 0.06, 0, 0.08, 0.26, 0.17, K.hair);   // 披肩长发
        break;
      case 'suebian':
        // 苏维汇发髻：长发向一侧束成结
        pb.taper(0, 0, 0.19, 0.31, 0.21, 0.195, 0.17, 0.16, K.hair);
        pb.blob(-0.02, 0.3, 0.12, 0.06, 0.06, 0.05, K.hair, 61);
        pb.blob(-0.03, 0.34, 0.15, 0.035, 0.05, 0.03, sh(K.hair, -0.15), 63);
        pb.box(-0.09, 0.08, 0, 0.08, 0.24, 0.17, K.hair);
        break;
      case 'conical':
        // 萨尔马提亚尖盔：分片铆合尖顶盔 + 护鼻 + 锁子护颈
        pb.taper(0, 0, 0.19, 0.3, 0.22, 0.205, 0.17, 0.16, K.metal);
        pb.taper(0, 0, 0.3, 0.5, 0.17, 0.16, 0.0, 0.0, sh(K.metal, 0.06));
        for (const z of [-0.06, 0.06]) pb.rod([0.06, 0.2, z], [0.0, 0.48, 0], 0.012, 0.006, 3, K.trim);
        pb.box(0.112, 0.15, 0, 0.012, 0.11, 0.022, K.metal);
        pb.at(-0.1, 0.2, 0, 0, 0, -0.2); pb.taper(0, 0, -0.17, 0, 0.03, 0.24, 0.03, 0.22, sh(K.metal, -0.25)); pb.at();
        break;
      default: hanHelm(helmC);
    }
  }

  // ============================================================ 视图：姿势 --
  // 姿势向量（身体局部坐标，单位米 / 弧度 / 兵器角为度）：
  //   hx hy 骨盆位置 · lean 前倾 · twist 扭身 · head 低头 · gx gy gz 右手握点 · wa 兵器角（0 = 向前水平，90 = 竖直向上）· wy 兵器偏航
  //   ox oy oz 左手（双持时为第二把兵器的握点）· wa2 第二把兵器角 · fLx fLy 前脚（左）· fRx fRy 后脚（右）· fall 倒地（0..1）· cape 披风扬起
  const PK = ['hx', 'hy', 'lean', 'twist', 'head', 'gx', 'gy', 'gz', 'wa', 'wy', 'ox', 'oy', 'oz', 'wa2', 'fLx', 'fLy', 'fRx', 'fRy', 'fall', 'cape'];
  const PI_ = {};
  PK.forEach((k, i) => { PI_[k] = i; });
  function pose(base, o) {
    const a = new Float32Array(PK.length);
    if (base) a.set(base);
    if (o) for (const k in o) a[PI_[k]] = o[k];
    return a;
  }
  const IDLE_POLE = pose(null, { hx: 0, hy: 0.9, lean: 0.1, twist: -0.5, head: -0.06, gx: 0.02, gy: 1.0, gz: 0.08, wa: 30, ox: 0.4, oy: 1.15, oz: 0, wa2: 0, fLx: 0.3, fLy: 0.08, fRx: -0.3, fRy: 0.08 });
  const IDLE_ONE = pose(null, { hx: 0, hy: 0.9, lean: 0.08, twist: -0.22, head: -0.05, gx: 0.3, gy: 1.12, gz: 0.2, wa: 55, ox: 0.16, oy: 1.04, oz: -0.16, wa2: 30, fLx: 0.28, fLy: 0.08, fRx: -0.3, fRy: 0.08 });

  // 招式片段：键为 [相位, 姿势]；相位 0..1 = 起手，1..2 = 判定，2..3 = 收招（与 MOVES 的时序对齐）
  function makeClips(style) {
    const I = style === 'pole' ? IDLE_POLE : IDLE_ONE;
    const P = o => pose(I, o);
    const dual = style === 'dual';
    const c = {};
    if (style === 'pole') {
      c.light1 = [[0, I], [0.7, P({ gx: -0.14, gy: 1.06, wa: 22, twist: -0.78, lean: 0.02, hx: -0.04 })], [1, P({ gx: 0.5, gy: 1.16, wa: 6, twist: -0.22, lean: 0.3, hx: 0.1, fLx: 0.46 })], [2, P({ gx: 0.55, gy: 1.14, wa: 4, twist: -0.18, lean: 0.32, hx: 0.12, fLx: 0.47 })], [3, I]];
      c.light2 = [[0, P({ gx: 0.4, gy: 1.14, wa: 6, twist: -0.25, lean: 0.28, hx: 0.08 })], [0.7, P({ gx: 0.12, gy: 0.82, wa: -28, twist: -0.62, lean: 0.25, hy: 0.84 })], [1, P({ gx: 0.36, gy: 1.3, wa: 62, twist: -0.15, lean: 0.06 })], [2, P({ gx: 0.26, gy: 1.42, wa: 92, twist: 0, lean: -0.02 })], [3, I]];
      c.light3 = [[0, P({ gx: 0.25, gy: 1.4, wa: 90, twist: 0, lean: 0 })], [0.75, P({ gx: -0.05, gy: 1.62, wa: 128, lean: -0.16, twist: -0.36, hy: 0.95, fLx: 0.32, head: -0.16 })], [1, P({ gx: 0.48, gy: 0.98, wa: -12, lean: 0.4, hy: 0.78, fLx: 0.58, fRx: -0.42, twist: -0.3, head: 0.1, hx: 0.08 })], [2, P({ gx: 0.5, gy: 0.86, wa: -24, lean: 0.42, hy: 0.75, fLx: 0.6, fRx: -0.42, hx: 0.1 })], [3, I]];
      c.charge = P({ gx: -0.32, gy: 1.2, wa: 168, twist: -1.05, lean: 0.06, hy: 0.82, fLx: 0.44, fRx: -0.42, head: 0.06 });
      c.heavy = [[0, c.charge], [1, P({ gx: 0.56, gy: 1.18, wa: -2, twist: 0.45, lean: 0.36, hy: 0.82, fLx: 0.56, fRx: -0.45, hx: 0.08 })], [2, P({ gx: 0.42, gy: 1.08, wa: -26, twist: 0.65, lean: 0.38, hy: 0.8, fLx: 0.56, fRx: -0.45, hx: 0.1 })], [3, I]];
      c.guard = P({ gx: 0.22, gy: 1.16, gz: 0.08, wa: 84, twist: -0.25, lean: -0.02, hy: 0.86, head: 0.06, fLx: 0.32, fRx: -0.36 });
      c.jump = P({ hy: 0.96, lean: 0.05, fLx: 0.22, fLy: 0.42, fRx: -0.12, fRy: 0.3, gx: 0.05, gy: 1.22, wa: 60 });
      c.air = [[0, c.jump], [0.6, pose(c.jump, { gx: -0.02, gy: 1.48, wa: 115, lean: -0.1 })], [1, pose(c.jump, { gx: 0.45, gy: 0.98, wa: -35, lean: 0.35 })], [2, pose(c.jump, { gx: 0.42, gy: 0.9, wa: -42, lean: 0.32 })], [3, c.jump]];
      c.power = P({ gx: -0.18, gy: 1.62, wa: 150, twist: -0.9, lean: -0.12, hy: 0.86, head: -0.18, fLx: 0.42, fRx: -0.42, cape: 0.6 });
      c.victory = [[0, I], [0.35, P({ gx: 0.2, gy: 1.42, wa: 200, twist: -0.25, lean: -0.02 })], [0.7, P({ gx: 0.1, gy: 1.86, wa: 78, lean: -0.12, head: -0.26, twist: -0.12, hy: 0.93 })], [1.6, P({ gx: 0.1, gy: 1.88, wa: 80, lean: -0.13, head: -0.24, twist: -0.12, hy: 0.93 })]];
      c.defeat = [[0, I], [0.5, P({ hy: 0.55, lean: 0.4, head: 0.36, fLx: 0.36, fLy: 0.08, fRx: -0.34, fRy: 0.05, gx: 0.32, gy: 0.72, wa: -58, twist: -0.2 })]];
      c.dash = P({ lean: 0.42, hy: 0.84, fLx: 0.5, fLy: 0.12, fRx: -0.52, fRy: 0.1, gx: -0.06, gy: 0.98, wa: 12, twist: -0.62, cape: 0.8 });
    } else {
      // 单手 / 双持
      const D = o => pose(I, Object.assign(dual ? { ox: 0.28, oy: 0.98, wa2: 22 } : {}, o));
      c.light1 = [[0, I], [0.7, D({ gx: -0.02, gy: 1.42, wa: 150, twist: -0.55, lean: 0.0, hx: -0.04 })], [1, D({ gx: 0.46, gy: 1.08, wa: 2, twist: 0.25, lean: 0.25, hx: 0.08, fLx: 0.42 })], [2, D({ gx: 0.36, gy: 0.94, wa: -45, twist: 0.42, lean: 0.28, hx: 0.08, fLx: 0.42 })], [3, I]];
      if (dual) {
        // 双持：第二击由左手出
        c.light2 = [[0, D({ gx: 0.36, gy: 0.94, wa: -45, twist: 0.42, lean: 0.28 })], [0.7, D({ gx: 0.25, gy: 0.95, wa: -30, ox: -0.06, oy: 1.4, wa2: 150, twist: 0.45, lean: 0.05 })], [1, D({ gx: 0.2, gy: 1.0, wa: -10, ox: 0.5, oy: 1.08, wa2: 0, twist: -0.45, lean: 0.27, fLx: 0.44 })], [2, D({ gx: 0.2, gy: 1.0, wa: 10, ox: 0.4, oy: 0.95, wa2: -42, twist: -0.55, lean: 0.28, fLx: 0.44 })], [3, I]];
        c.light3 = [[0, I], [0.7, D({ gx: 0.0, gy: 1.2, wa: 8, ox: -0.02, oy: 1.1, wa2: 6, hx: -0.1, lean: -0.02, twist: -0.3 })], [1, D({ gx: 0.7, gy: 1.24, wa: 4, ox: 0.66, oy: 1.1, wa2: 0, hx: 0.16, lean: 0.38, twist: 0.0, fLx: 0.62, fRx: -0.4, hy: 0.84 })], [2, D({ gx: 0.74, gy: 1.22, wa: 2, ox: 0.68, oy: 1.08, wa2: -2, hx: 0.18, lean: 0.4, fLx: 0.62, fRx: -0.4, hy: 0.84 })], [3, I]];
      } else {
        c.light2 = [[0, D({ gx: 0.36, gy: 0.94, wa: -45, twist: 0.42, lean: 0.28 })], [0.7, D({ gx: 0.32, gy: 0.84, wa: -55, twist: 0.42, lean: 0.22 })], [1, D({ gx: 0.42, gy: 1.42, wa: 78, twist: -0.3, lean: 0.02 })], [2, D({ gx: 0.22, gy: 1.54, wa: 118, twist: -0.42 })], [3, I]];
        c.light3 = [[0, D({ gx: 0.22, gy: 1.5, wa: 110, twist: -0.4 })], [0.7, D({ gx: 0.0, gy: 1.2, wa: 8, hx: -0.1, lean: -0.02, twist: -0.6 })], [1, D({ gx: 0.72, gy: 1.22, wa: 3, hx: 0.16, lean: 0.38, twist: 0.1, fLx: 0.62, fRx: -0.4, hy: 0.84 })], [2, D({ gx: 0.75, gy: 1.2, wa: 1, hx: 0.18, lean: 0.4, twist: 0.12, fLx: 0.62, fRx: -0.4, hy: 0.84 })], [3, I]];
      }
      c.charge = D({ gx: -0.2, gy: 1.68, wa: 135, ox: dual ? -0.25 : -0.12, oy: dual ? 1.5 : 1.62, oz: -0.1, wa2: 150, lean: -0.1, hy: 0.85, twist: -0.42, fLx: 0.4, fRx: -0.4 });
      c.heavy = [[0, c.charge], [1, D({ gx: 0.48, gy: 0.98, wa: -32, ox: dual ? 0.46 : 0.42, oy: dual ? 0.9 : 1.0, oz: -0.06, wa2: -40, lean: 0.42, hy: 0.78, fLx: 0.56, fRx: -0.42, twist: 0, hx: 0.08 })], [2, D({ gx: 0.44, gy: 0.86, wa: -52, ox: 0.4, oy: 0.88, oz: -0.06, wa2: -55, lean: 0.44, hy: 0.76, fLx: 0.56, fRx: -0.42, hx: 0.1 })], [3, I]];
      c.guard = D(dual ? { gx: 0.3, gy: 1.26, wa: 116, ox: 0.3, oy: 1.26, wa2: 64, twist: -0.1, hy: 0.86, lean: -0.02 } : { gx: 0.3, gy: 1.3, wa: 102, ox: 0.3, oy: 1.42, oz: -0.05, twist: -0.12, hy: 0.86, lean: -0.02 });
      c.jump = D({ hy: 0.96, lean: 0.05, fLx: 0.22, fLy: 0.42, fRx: -0.12, fRy: 0.3, gx: 0.15, gy: 1.4, wa: 90 });
      c.air = [[0, c.jump], [0.6, pose(c.jump, { gx: -0.06, gy: 1.52, wa: 145, lean: -0.1 })], [1, pose(c.jump, { gx: 0.42, gy: 0.95, wa: -40, lean: 0.35 })], [2, pose(c.jump, { gx: 0.38, gy: 0.88, wa: -50, lean: 0.32 })], [3, c.jump]];
      c.power = D({ gx: -0.1, gy: 1.7, wa: 120, ox: 0.25, oy: 1.25, wa2: 40, twist: -0.6, lean: -0.12, hy: 0.86, head: -0.18, fLx: 0.42, fRx: -0.42, cape: 0.6 });
      c.victory = [[0, I], [0.35, D({ gx: 0.25, gy: 1.4, wa: 160, twist: -0.25 })], [0.7, D({ gx: 0.14, gy: 1.86, wa: 86, ox: 0.06, oy: 1.0, lean: -0.12, head: -0.26, hy: 0.93 })], [1.6, D({ gx: 0.14, gy: 1.88, wa: 88, ox: 0.06, oy: 1.0, lean: -0.13, head: -0.24, hy: 0.93 })]];
      c.defeat = [[0, I], [0.5, D({ hy: 0.55, lean: 0.4, head: 0.36, fLx: 0.36, fLy: 0.08, fRx: -0.34, fRy: 0.05, gx: 0.36, gy: 0.62, wa: -80, ox: 0.2, oy: 0.8, twist: -0.1 })]];
      c.dash = D({ lean: 0.42, hy: 0.84, fLx: 0.5, fLy: 0.12, fRx: -0.52, fRy: 0.1, gx: 0.1, gy: 1.0, wa: 170, twist: -0.4, cape: 0.8 });
    }
    c.idle = I;
    c.land = pose(I, { hy: 0.76, lean: 0.25 });
    c.guardstun = pose(c.guard, { hx: -0.07, lean: -0.12, head: -0.1 });
    c.hitA = pose(I, { hx: -0.08, lean: -0.32, head: -0.32, gx: I[PI_.gx] - 0.1, gy: I[PI_.gy] + 0.05, wa: I[PI_.wa] + 20, twist: -0.25, hy: 0.88, cape: 0.3 });
    c.hitB = pose(I, { lean: 0.38, head: 0.32, hy: 0.84, gx: I[PI_.gx] + 0.05, gy: I[PI_.gy] - 0.12, wa: I[PI_.wa] - 20 });
    c.guardbreak = pose(I, { lean: -0.38, head: -0.36, gx: -0.1, gy: 1.38, wa: 120, ox: 0.0, oy: 1.3, hx: -0.12, fLx: 0.14, hy: 0.88 });
    c.knock = pose(I, { lean: -0.45, head: -0.4, gx: -0.25, gy: 1.42, wa: 150, ox: 0.1, oy: 1.45, fLx: 0.38, fLy: 0.4, fRx: 0.05, fRy: 0.3, cape: 0.8 });
    c.down = pose(I, { fall: 1, lean: -0.1, head: -0.15, gx: 0.05, gy: 1.42, wa: 165, ox: 0.25, oy: 1.4, fLx: 0.32, fLy: 0.12, fRx: 0.08, fRy: 0.16, hy: 0.86, twist: -0.3 });
    c.getup = [[0, c.down], [0.18, pose(I, { fall: 0.45, hy: 0.62, lean: 0.55, head: 0.2, fLx: 0.35, fRx: -0.25 })], [0.42, I]];
    // 绝技（秒）：蓄势定格 → 突进 → 四连斩
    const L1 = c.light1[2][1], L2 = c.light2[2][1], L3 = c.light3[2][1], H = c.heavy[1][1];
    const t0 = SPECIAL_FREEZE + 0.1, h = MOVES.special.hits;
    c.special = [[0, I], [0.25, c.power], [SPECIAL_FREEZE, c.power], [SPECIAL_FREEZE + 0.06, c.dash], [t0 + h[0] - 0.03, c.light1[1][1]], [t0 + h[0], L1],
      [t0 + h[1] - 0.06, c.light2[1][1]], [t0 + h[1], L2], [t0 + h[2] - 0.06, c.light3[1][1]], [t0 + h[2], L3], [t0 + h[3] - 0.1, c.charge], [t0 + h[3], H],
      [t0 + h[3] + 0.2, c.heavy[2][1]], [t0 + MOVES.special.active + MOVES.special.recovery, I]];
    c.intro = [[0, I], [0.25, pose(I, { wa: I[PI_.wa] + 180, gy: I[PI_.gy] + 0.3, twist: I[PI_.twist] + 0.3 })], [0.5, pose(I, { wa: I[PI_.wa] + 360, gy: I[PI_.gy] + 0.1 })], [0.85, c.guard], [1.3, I]];
    // 弓将绝技「连珠箭」：插刀于地 → 侧身开弓 → 三连射 → 满弓重箭 → 收弓拔刀（g* = 拉弦的右手，o* = 持弓的左手）
    const B0 = { twist: -1.05, lean: 0.03, hy: 0.86, head: 0.02, fLx: 0.44, fLy: 0.08, fRx: -0.42, fRy: 0.08, oz: -0.1, gz: 0.12, cape: 0.3 };
    const DRAW = pose(I, Object.assign({}, B0, { gx: -0.02, gy: 1.47, ox: 0.6, oy: 1.46 }));
    const FULL = pose(I, Object.assign({}, B0, { gx: -0.1, gy: 1.5, ox: 0.62, oy: 1.5, lean: -0.06, hy: 0.84, head: -0.06, cape: 0.6 }));
    const REL = pose(I, Object.assign({}, B0, { gx: -0.16, gy: 1.52, gz: 0.2, ox: 0.62, oy: 1.47 }));
    const NOCK = pose(I, Object.assign({}, B0, { gx: 0.28, gy: 1.36, ox: 0.58, oy: 1.44 }));
    const RAISE = pose(I, Object.assign({}, B0, { gx: 0.2, gy: 1.62, ox: 0.44, oy: 1.72, twist: -0.8, lean: -0.08, head: -0.14, cape: 0.6 }));
    const bk = [[0, I], [0.22, RAISE], [0.45, NOCK], [SPECIAL_FREEZE, DRAW]];
    for (let i = 0; i < h.length; i++) {
      const th = t0 + h[i], last = i === h.length - 1;
      if (i > 0) { bk.push([t0 + h[i - 1] + 0.05, NOCK]); bk.push([th - (last ? 0.04 : 0.02), last ? FULL : DRAW]); }
      else bk.push([th - 0.02, DRAW]);
      bk.push([th + 0.02, REL]);
    }
    bk.push([t0 + h[h.length - 1] + 0.3, REL], [t0 + MOVES.special.active + MOVES.special.recovery, I]);
    c.bowSpecial = bk;
    return c;
  }
  const CLIPS = {};
  function clipsFor(style) { return CLIPS[style] || (CLIPS[style] = makeClips(style)); }

  // 片段采样：相邻关键帧间用平滑插值
  function sampleKeys(keys, t, out) {
    if (t <= keys[0][0]) { out.set(keys[0][1]); return out; }
    for (let i = 1; i < keys.length; i++) {
      if (t <= keys[i][0]) {
        const a = keys[i - 1], b = keys[i];
        let u = (t - a[0]) / Math.max(1e-6, b[0] - a[0]);
        u = u * u * (3 - 2 * u);
        const A = a[1], B = b[1];
        for (let j = 0; j < out.length; j++) out[j] = A[j] + (B[j] - A[j]) * u;
        return out;
      }
    }
    out.set(keys[keys.length - 1][1]);
    return out;
  }
  // 出招时间 → 相位（0..3）
  function phaseOf(mv, t) {
    if (t < mv.startup) return t / mv.startup;
    if (t < mv.startup + mv.active) return 1 + (t - mv.startup) / mv.active;
    return Math.min(3, 2 + (t - mv.startup - mv.active) / mv.recovery);
  }

  // 两段 IK：S（根）→ T（末端），返回中间关节 E（膝 / 肘）；hint 为弯曲方向
  const _ik = {};
  function ikInit() {
    if (_ik.d) return;
    for (const k of ['d', 'p', 'x', 'y', 'z', 'e', 't', 'h']) _ik[k] = new THREE.Vector3();
    _ik.m = new THREE.Matrix4();
  }
  function solveIK(S, T, l1, l2, hint, outE, outT) {
    const d = _ik.d.subVectors(T, S);
    let len = d.length();
    const maxL = (l1 + l2) * 0.999, minL = Math.abs(l1 - l2) + 0.02;
    if (len < 1e-5) { d.set(0, -1, 0); len = 1e-5; }
    const L = M.clamp(len, minL, maxL);
    d.multiplyScalar(1 / len);
    outT.copy(S).addScaledVector(d, L);
    const x = (l1 * l1 - l2 * l2 + L * L) / (2 * L);
    const h = Math.sqrt(Math.max(0, l1 * l1 - x * x));
    const p = _ik.p.copy(hint).addScaledVector(d, -hint.dot(d));
    if (p.lengthSq() < 1e-8) p.set(0, 0, 1);
    p.normalize();
    outE.copy(S).addScaledVector(d, x).addScaledVector(p, h);
  }
  // 让骨骼的局部 −y 指向 from → to，局部 +x 尽量朝前
  function orient(obj, from, to) {
    const y = _ik.y.subVectors(from, to);
    if (y.lengthSq() < 1e-10) y.set(0, 1, 0);
    y.normalize();
    const x = _ik.x.set(y.y, -y.x, 0);
    if (x.lengthSq() < 1e-6) x.set(1, 0, 0);
    x.addScaledVector(y, -x.dot(y)).normalize();
    const z = _ik.z.crossVectors(x, y);
    _ik.m.makeBasis(x, y, z);
    obj.quaternion.setFromRotationMatrix(_ik.m);
    obj.position.copy(from);
  }

  // ============================================================ 视图：武将（动画） --
  // 材质跨场次复用（不随单挑释放；几何体与贴图每场释放）
  const MAT = { f: [null, null], env: null, trail: null };
  function fighterMat(i) {
    if (!MAT.f[i]) MAT.f[i] = SG.Gfx.newLowPoly();
    MAT.f[i].flash = 0; MAT.f[i].emission = 0;
    return MAT.f[i];
  }
  function envMat() { if (!MAT.env) MAT.env = SG.Gfx.newLowPoly(); return MAT.env; }

  const BLEND = { idle: 0.14, walk: 0.12, guard: 0.06, guardstun: 0.03, hitstun: 0.035, knockdown: 0.07, down: 0.1, getup: 0.08, victory: 0.2, defeat: 0.2, charge: 0.1, dash: 0.06, jump: 0.08, land: 0.04 };

  class Warrior {
    constructor(f, team) {
      ikInit();
      this.f = f;
      this.look = f.look;
      this.style = f.wpn.style;
      this.clips = clipsFor(this.style);
      this.idlePose = this.style === 'pole' ? IDLE_POLE : IDLE_ONE;
      this.mat = fighterMat(f.idx);
      this.geos = [];
      const bk = this.look.bulk;
      this.bk = bk;
      this.root = new THREE.Group();
      this.root.name = 'Duelist_' + (f.gen && f.gen.name);
      this.body = new THREE.Group();
      this.root.add(this.body);
      this.body.rotation.order = 'YZX';
      this.body.scale.setScalar(this.look.height);
      const parts = buildParts(this.look, team, f.war);
      this.m = {};
      const mk = (name, parent) => {
        const pb = parts[name];
        if (!pb || pb.empty) return null;
        const g = pb.geo(); this.geos.push(g);
        const o = new THREE.Mesh(g, this.mat);
        o.castShadow = true; o.receiveShadow = false;
        (parent || this.body).add(o);
        this.m[name] = o;
        return o;
      };
      for (const n of ['pelvis', 'upperR', 'foreR', 'upperL', 'foreL', 'thighR', 'shinR', 'thighL', 'shinL', 'footR', 'footL']) mk(n);
      this.torso = new THREE.Group(); this.torso.rotation.order = 'YZX'; this.body.add(this.torso); mk('torso', this.torso);
      this.head = new THREE.Group(); this.head.position.set(0.01, 0.53, 0); this.torso.add(this.head); mk('head', this.head);
      if (parts.cape1) {
        this.cape1 = new THREE.Group(); this.cape1.position.set(-0.14 * bk, 0.47, 0); this.torso.add(this.cape1); mk('cape1', this.cape1);
        this.cape2 = new THREE.Group(); this.cape2.position.set(-0.015, -0.5, 0); this.cape1.add(this.cape2); mk('cape2', this.cape2);
      }
      const w = buildWeapon(this.look.weapon, this.look);
      const mkW = () => {
        const grp = new THREE.Group(); grp.rotation.order = 'YZX';
        const g = w.pb.geo(); this.geos.push(g);
        const o = new THREE.Mesh(g, this.mat); o.castShadow = true;
        grp.add(o); this.body.add(grp);
        return grp;
      };
      this.wpn = mkW();
      if (this.style === 'dual') this.wpn2 = mkW();
      if (this.look.shield && this.style === 'one') {
        const sg = buildShield(this.look.shield, team).geo(); this.geos.push(sg);
        this.shield = new THREE.Mesh(sg, this.mat); this.shield.castShadow = true; this.shield.rotation.order = 'YZX';
        this.body.add(this.shield);
      }
      this.tipLen = w.tip; this.bladeLen = w.len;
      // 弓将：背弓 + 手持弓（弦为折线，满弓时随右手拉开）+ 搭在弦上的箭
      this.mats = [];
      this.bowK = 0;
      if (parts.backbow) mk('backbow', this.torso);
      if (this.look.bow) {
        const g = buildBow().geo(); this.geos.push(g);
        this.handBow = new THREE.Mesh(g, this.mat); this.handBow.castShadow = true; this.handBow.visible = false;
        this.body.add(this.handBow);
        const sg = new THREE.BufferGeometry();
        sg.setAttribute('position', new THREE.BufferAttribute(new Float32Array(9), 3));
        this.geos.push(sg);
        const sm = new THREE.LineBasicMaterial({ color: 0xf0e8d0 }); this.mats.push(sm);
        this.bowString = new THREE.Line(sg, sm); this.bowString.frustumCulled = false; this.bowString.visible = false;
        this.body.add(this.bowString);
        const ag = buildArrow(false).geo(); this.geos.push(ag);
        this.nocked = new THREE.Mesh(ag, this.mat); this.nocked.visible = false; this.nocked.rotation.order = 'YZX';
        this.body.add(this.nocked);
        this.handL = new THREE.Vector3(); this.handR = new THREE.Vector3();
      }
      this.cur = pose(this.idlePose); this.from = pose(this.idlePose); this.tgt = pose(this.idlePose);
      this.key = ''; this.blend = 1; this.blendDur = 0.1;
      this.walkPhase = 0; this.time = f.idx * 1.7; this.capeA = -0.1; this.capeV = 0; this.cape2A = 0;
      this.flash = 0; this.flashColor = new THREE.Color(1, 1, 1);
      this.intro = -1;     // ≥0：开场亮相动画计时
      this.V = { S: new THREE.Vector3(), T: new THREE.Vector3(), E: new THREE.Vector3(), O: new THREE.Vector3(), H: new THREE.Vector3(), hint: new THREE.Vector3(), d: new THREE.Vector3(), tmp: new THREE.Vector3() };
      this.tipW = [new THREE.Vector3(), new THREE.Vector3()];
      this.baseW = [new THREE.Vector3(), new THREE.Vector3()];
      this.chestW = new THREE.Vector3();
      this.apply(this.cur);
    }

    // 根据逻辑状态选片段并采样（返回目标姿势与混合时长）
    sample(dt) {
      const f = this.f, c = this.clips, out = this.tgt;
      let key = f.state + ':' + f.seq, dur = BLEND[f.state] !== undefined ? BLEND[f.state] : 0.05;
      const mv = MOVES[f.state];
      if (this.intro >= 0) { key = 'intro'; sampleKeys(c.intro, this.intro, out); dur = 0.1; }
      else if (mv && f.state !== 'special') { sampleKeys(c[f.state], phaseOf(mv, f.t), out); }
      else switch (f.state) {
        case 'special': sampleKeys(f.bow ? c.bowSpecial : c.special, f.t, out); break;
        case 'charge': out.set(c.charge); break;
        case 'guard': out.set(c.guard); break;
        case 'guardstun': out.set(c.guardstun); break;
        case 'guardbreak': out.set(c.guardbreak); break;
        case 'hitstun': out.set(f.seq % 2 ? c.hitA : c.hitB); break;
        case 'knockdown': out.set(c.knock); out[PI_.fall] = Math.min(0.9, f.t * 2.4); break;
        case 'down': out.set(c.down); out[PI_.fall] = 1; break;
        case 'getup': sampleKeys(c.getup, f.t, out); break;
        case 'dash': out.set(c.dash); break;
        case 'jump': {
          out.set(c.jump);
          const k = M.clamp01((2 - f.vy) / 8);            // 下落时腿伸直准备落地
          out[PI_.fLy] -= k * 0.26; out[PI_.fRy] -= k * 0.14; out[PI_.fLx] += k * 0.05;
          break;
        }
        case 'land': out.set(c.land); break;
        case 'victory': sampleKeys(c.victory, f.t, out); break;
        case 'defeat': if (f.ko) { out.set(c.down); out[PI_.fall] = 1; } else sampleKeys(c.defeat, f.t, out); break;
        case 'walk': out.set(this.idlePose); key = 'walk'; break;
        default: out.set(this.idlePose); key = 'idle'; break;
      }
      if (key !== this.key) {
        this.from.set(this.cur);
        this.blend = 0;
        this.blendDur = dur;
        this.key = key;
      }
      return out;
    }

    update(dt, frozen) {
      const f = this.f, P = this.cur;
      this.time += dt;
      if (this.intro >= 0) this.intro += dt;
      const tgt = this.sample(dt);
      if (!frozen) this.blend += dt;
      const w = this.blendDur > 0 ? M.clamp01(this.blend / this.blendDur) : 1;
      const k = w * w * (3 - 2 * w);
      for (let i = 0; i < P.length; i++) P[i] = this.from[i] + (tgt[i] - this.from[i]) * k;
      const st = f.state;
      // 程序化叠加：呼吸、步伐、蓄力颤动、受击抖动
      if (st === 'idle' || st === 'guard' || st === 'walk' || st === 'charge') {
        const b = Math.sin(this.time * 2.3);
        P[PI_.hy] += b * 0.012; P[PI_.lean] += Math.sin(this.time * 2.3 + 0.6) * 0.015;
        P[PI_.gy] += b * 0.01; P[PI_.wa] += Math.sin(this.time * 1.25) * 2.2;
      }
      if (st === 'walk') {
        const v = f.vx * f.face;
        this.walkPhase += v * dt * (Math.PI * 2 / 1.0);
        const s = Math.sin(this.walkPhase), c = Math.cos(this.walkPhase);
        P[PI_.fLx] += s * 0.2; P[PI_.fLy] += Math.max(0, c) * 0.11;
        P[PI_.fRx] -= s * 0.2; P[PI_.fRy] += Math.max(0, -c) * 0.11;
        P[PI_.hy] -= Math.abs(s) * 0.025; P[PI_.lean] += v * 0.03; P[PI_.cape] += Math.abs(v) * 0.12;
      }
      if (st === 'charge') {
        const q = f.charge, n = this.time * 61;
        P[PI_.gx] += Math.sin(n) * 0.012 * q; P[PI_.gy] += Math.cos(n * 1.3) * 0.012 * q; P[PI_.hy] -= q * 0.04;
      }
      if (st === 'hitstun' && f.t < 0.12) P[PI_.hx] += Math.sin(f.t * 120) * 0.025 * (1 - f.t / 0.12);
      if (st === 'dash') P[PI_.cape] += 0.4;
      // 弓将绝技期间：刀插在地上，弓在手中
      const sp = MOVES.special;
      this.bowK = f.bow && st === 'special' && f.t > 0.14 && f.t < sp.startup + sp.active + sp.recovery - 0.22 ? 1 : 0;
      this.apply(P);
      if (this.handBow) this.updateBow();
      // 位置与朝向
      this.root.position.set(f.x, f.y + P[PI_.fall] * 0.12, 0);
      this.root.scale.x = f.face;
      this.body.rotation.y = -TURN * (1 - P[PI_.fall] * 0.7);
      this.body.rotation.z = P[PI_.fall] * 1.5;
      // 披风：弹簧追随
      const tgtA = -0.1 - Math.abs(f.vx) * 0.06 - P[PI_.cape] * 0.55 - (f.y > 0 ? Math.min(0.5, Math.abs(f.vy) * 0.05) : 0);
      this.capeV += ((tgtA - this.capeA) * 60 - this.capeV * 9) * dt;
      this.capeA += this.capeV * dt;
      if (this.capeA < -1.15) { this.capeA = -1.15; if (this.capeV < 0) this.capeV = 0; }   // 披风最多扬到身后近水平
      else if (this.capeA > 0.35) { this.capeA = 0.35; if (this.capeV > 0) this.capeV = 0; }
      if (this.cape1) {
        this.cape1.rotation.z = P[PI_.lean] + this.capeA + Math.sin(this.time * 5.3) * 0.03;
        this.cape2.rotation.z = this.capeA * 0.6 + Math.sin(this.time * 6.1 + 1) * 0.06 * (1 + Math.abs(f.vx) * 0.3);
      }
      // 受击闪光 / 蓄力发光
      if (this.flash > 0) this.flash = Math.max(0, this.flash - dt * 7);
      this.mat.flash = this.flash;
      let em = 0;
      if (st === 'charge') em = f.charge * 0.25 + (f.charge >= 1 ? 0.12 * (0.5 + 0.5 * Math.sin(this.time * 30)) : 0);
      else if (st === 'special' && f.t < SPECIAL_FREEZE + 0.1) em = 0.3 + 0.1 * Math.sin(this.time * 25);
      else if (f.rage >= 100) em = 0.06 + 0.05 * Math.sin(this.time * 8);
      this.mat.emission = em;
      // 世界坐标（刀光拖尾与特效用）
      this.root.updateMatrixWorld(true);
      this.wpn.localToWorld(this.tipW[0].set(0, this.tipLen, 0));
      this.wpn.localToWorld(this.baseW[0].set(0, this.tipLen - this.bladeLen, 0));
      if (this.wpn2) {
        this.wpn2.localToWorld(this.tipW[1].set(0, this.tipLen, 0));
        this.wpn2.localToWorld(this.baseW[1].set(0, this.tipLen - this.bladeLen, 0));
      }
      this.torso.localToWorld(this.chestW.set(0.05, 0.3, 0));
    }

    flashHit(col, k) { this.flash = k || 0.85; this.flashColor.set(col || '#ffffff'); this.mat.flashColor = this.flashColor; }

    // 手持弓、弓弦与搭箭（身体局部坐标）
    updateBow() {
      const on = this.bowK > 0;
      this.handBow.visible = this.bowString.visible = on;
      if (this.m.backbow) this.m.backbow.visible = !on;
      if (!on) { this.nocked.visible = false; return; }
      const L = this.handL, R = this.handR;
      this.handBow.position.copy(L);
      this.handBow.rotation.set(0, 0, 0);
      const tx = L.x + BOW.tipX, ty0 = L.y + BOW.tipY, ty1 = L.y - BOW.tipY;
      // 右手在弓后方、与握把同高附近时视为拉弦
      const drawn = R.x < L.x - 0.12 && Math.abs(R.y - L.y) < 0.28;
      const nx = drawn ? R.x : tx, ny = drawn ? R.y : L.y, nz = drawn ? R.z : L.z;
      const a = this.bowString.geometry.attributes.position;
      a.setXYZ(0, tx, ty0, L.z); a.setXYZ(1, nx, ny, nz); a.setXYZ(2, tx, ty1, L.z);
      a.needsUpdate = true;
      this.nocked.visible = drawn;
      if (drawn) {
        this.nocked.position.set(nx, ny, nz);
        const dx = L.x + 0.1 - nx, dy = L.y - ny, dz = L.z - nz;
        this.nocked.rotation.set(0, Math.atan2(-dz, dx), Math.atan2(dy, Math.hypot(dx, dz)));
      }
    }

    // 把姿势向量应用到各部件（身体局部坐标 + IK）
    apply(P) {
      const V = this.V, bk = this.bk, m = this.m;
      const hx = P[PI_.hx], hy = P[PI_.hy], tw = P[PI_.twist];
      if (m.pelvis) { m.pelvis.position.set(hx, hy, 0); m.pelvis.rotation.set(0, tw * 0.35, 0); }
      this.torso.position.set(hx, hy + 0.04, 0);
      this.torso.rotation.set(0, tw, -P[PI_.lean]);
      this.head.rotation.set(0, -tw * 0.35, -P[PI_.head] + P[PI_.lean] * 0.35);
      this.torso.updateMatrix();
      // 兵器
      const wa = P[PI_.wa] * M.deg2rad, wy = P[PI_.wy] * M.deg2rad;
      const G = V.T.set(P[PI_.gx], P[PI_.gy], P[PI_.gz]);
      if (this.bowK) {
        // 插刀于地：长兵器刃朝上、短兵器刃朝下
        const pole = this.style === 'pole';
        this.wpn.position.set(0.34, pole ? 0.63 : 0.9, 0.36);
        this.wpn.rotation.set(0, 0.2, (pole ? 94 : -86) * M.deg2rad - Math.PI / 2);
      } else {
        this.wpn.position.copy(G);
        this.wpn.rotation.set(0, wy, wa - Math.PI / 2);
      }
      // 左手目标
      const O = V.O;
      if (this.style === 'pole' && !this.bowK) {
        const dir = V.d.set(Math.cos(wa) * Math.cos(wy), Math.sin(wa), -Math.cos(wa) * Math.sin(wy));
        const SL = V.S.set(0, 0.47, -0.22 * bk).applyMatrix4(this.torso.matrix);
        const reach = (LEN.upper + LEN.fore) * 0.97;
        let best = 0.12;
        for (let s = 0.46; s >= 0.12; s -= 0.04) {
          O.copy(G).addScaledVector(dir, s);
          if (O.distanceTo(SL) <= reach) { best = s; break; }
        }
        O.copy(G).addScaledVector(dir, best);
      } else O.set(P[PI_.ox], P[PI_.oy], P[PI_.oz]);
      if (this.wpn2) {
        this.wpn2.position.copy(O);
        this.wpn2.rotation.set(0, 0, P[PI_.wa2] * M.deg2rad - Math.PI / 2);
      }
      if (this.shield) {   // 盾挂在左手前方，盾面朝前并略转向镜头
        this.shield.position.set(O.x + 0.07, O.y - 0.02, O.z + 0.02);
        this.shield.rotation.set(0, -0.5 + tw * 0.4, -P[PI_.lean] * 0.4);
      }
      // 手臂 IK
      for (const sd of [1, -1]) {
        const S = V.S.set(0, 0.47, sd * 0.22 * bk).applyMatrix4(this.torso.matrix);
        const tgt = sd > 0 ? G : O;
        V.hint.set(-0.35, -1, sd * 0.55);
        solveIK(S, tgt, LEN.upper, LEN.fore, V.hint, V.E, V.H);
        const up = m[sd > 0 ? 'upperR' : 'upperL'], fo = m[sd > 0 ? 'foreR' : 'foreL'];
        if (up) orient(up, S, V.E);
        if (fo) orient(fo, V.E, V.H);
        if (this.handL) (sd > 0 ? this.handR : this.handL).copy(V.H);
      }
      // 腿 IK
      for (const sd of [1, -1]) {
        const Hp = V.S.set(hx, hy - 0.06, sd * 0.1 * bk);
        const F = V.tmp.set(sd > 0 ? P[PI_.fRx] : P[PI_.fLx], sd > 0 ? P[PI_.fRy] : P[PI_.fLy], sd * 0.13 * bk);
        V.hint.set(1, 0, sd * 0.15);
        solveIK(Hp, F, LEN.thigh, LEN.shin, V.hint, V.E, V.H);
        const th = m[sd > 0 ? 'thighR' : 'thighL'], sn = m[sd > 0 ? 'shinR' : 'shinL'], ft = m[sd > 0 ? 'footR' : 'footL'];
        if (th) orient(th, Hp, V.E);
        if (sn) orient(sn, V.E, V.H);
        if (ft) { ft.position.copy(V.H); ft.rotation.set(0, 0, -Math.max(0, (V.H.y - 0.1) * 0.8)); }
      }
    }

    dispose() {
      for (const g of this.geos) g.dispose();
      for (const m of this.mats) m.dispose();
      this.geos.length = 0; this.mats.length = 0;
      if (this.root.parent) this.root.parent.remove(this.root);
      this.mat.flash = 0; this.mat.emission = 0;
    }
  }

  // ============================================================ 视图：场地 --
  // 背景随地形：平原 / 森林 / 丘陵（山地）/ 河川 / 城墙（城门、本城）
  const PALETTES = {
    plain: { top: '#1f355e', hor: '#e89a5c', low: '#5a4232', sun: '#ffcf8a', sunDir: [-0.35, 0.14, -1], fog: '#a87a5c', fogN: 30, fogF: 150, key: '#ffd9a8', keyI: 0.86, rim: '#ff8c50', rimI: 0.8, hemiS: '#8aa0c8', hemiG: '#4a3626', hemiI: 0.42,
      ground: '#5c6e32', ground2: '#7c6c3c', lane: '#7e6646', far: '#5a5a78', mid: '#44523a', leaf: '#3f6a2c', rock: '#7c7466' },
    forest: { top: '#16383c', hor: '#a8c890', low: '#2a3a26', sun: '#fff0c0', sunDir: [0.3, 0.45, -1], fog: '#4c6a50', fogN: 14, fogF: 80, key: '#fff0c8', keyI: 0.82, rim: '#c8f0a0', rimI: 0.6, hemiS: '#7ca48c', hemiG: '#2a2a18', hemiI: 0.42,
      ground: '#3a5a26', ground2: '#4c4a28', lane: '#5e5038', far: '#3a5a4c', mid: '#24402a', leaf: '#2f5a24', rock: '#5e5c50' },
    hill: { top: '#2c4c80', hor: '#dcc8a8', low: '#6a5a4a', sun: '#fff0d0', sunDir: [-0.5, 0.3, -1], fog: '#9c958c', fogN: 30, fogF: 160, key: '#fff0d8', keyI: 0.9, rim: '#ffc890', rimI: 0.65, hemiS: '#98a8c8', hemiG: '#4a3c2e', hemiI: 0.42,
      ground: '#6c6440', ground2: '#7c7056', lane: '#857052', far: '#646c80', mid: '#545642', leaf: '#4a6030', rock: '#706b60' },
    river: { top: '#2e5a92', hor: '#ecd6b2', low: '#5a7a8a', sun: '#fff4d8', sunDir: [0.4, 0.22, -1], fog: '#9cacae', fogN: 30, fogF: 160, key: '#fff4e0', keyI: 0.88, rim: '#ffd8a0', rimI: 0.6, hemiS: '#a0b8d8', hemiG: '#4a4a3a', hemiI: 0.45,
      ground: '#5e6c40', ground2: '#857e66', lane: '#8a7e62', far: '#62788a', mid: '#4a6a4a', leaf: '#3e6a30', rock: '#8a8478' },
    castle: { top: '#121234', hor: '#d8643e', low: '#3a2222', sun: '#ff9a5a', sunDir: [0.5, 0.1, -1], fog: '#5c3a3c', fogN: 24, fogF: 120, key: '#ffc088', keyI: 0.86, rim: '#ff6a3a', rimI: 0.95, hemiS: '#5a5a90', hemiG: '#3a2620', hemiI: 0.4,
      ground: '#4c4238', ground2: '#5a4e42', lane: '#62584a', far: '#3a2e44', mid: '#463a3c', leaf: '#2e4228', rock: '#5c544a' },
  };
  function terrainKind(t) {
    const T = SG.Terrain || { Plain: 0, Forest: 1, Hill: 2, Mountain: 3, River: 4, Wall: 5, Gate: 6, Castle: 7 };
    if (typeof t === 'string') return PALETTES[t] ? t : 'plain';
    if (t === T.Forest) return 'forest';
    if (t === T.Hill || t === T.Mountain) return 'hill';
    if (t === T.River) return 'river';
    if (t === T.Wall || t === T.Gate || t === T.Castle) return 'castle';
    return 'plain';
  }

  const SKY_VS = 'varying vec3 vDir;\nvoid main(){ vDir = position; vec4 p = projectionMatrix * modelViewMatrix * vec4(position, 1.0); gl_Position = p.xyww; }';
  const SKY_FS = [
    'uniform vec3 uTop; uniform vec3 uHor; uniform vec3 uLow; uniform vec3 uSun; uniform vec3 uSunDir;',
    'varying vec3 vDir;',
    'void main(){',
    '  vec3 d = normalize(vDir);',
    '  float y = d.y;',
    '  vec3 c = y > 0.0 ? mix(uHor, uTop, pow(clamp(y * 1.6, 0.0, 1.0), 0.6)) : mix(uHor, uLow, clamp(-y * 4.0, 0.0, 1.0));',
    '  float s = max(dot(d, normalize(uSunDir)), 0.0);',
    '  c += uSun * (pow(s, 220.0) * 1.2 + pow(s, 12.0) * 0.28 + pow(s, 3.0) * 0.1);',
    '  float band = sin(d.x * 9.0 + d.z * 4.0) * 0.5 + 0.5;',
    '  c = mix(c, c * 1.06 + vec3(0.02), smoothstep(0.08, 0.2, y) * (1.0 - smoothstep(0.2, 0.45, y)) * band * 0.6);',
    '  gl_FragColor = vec4(c, 1.0);',
    '}'].join('\n');

  // 旗面贴图：势力色底 + 白圆中写姓氏
  function bannerTexture(color, glyph, pennant) {
    const cv = document.createElement('canvas');
    cv.width = 128; cv.height = 256;
    const g = cv.getContext('2d');
    g.fillStyle = color; g.fillRect(0, 0, 128, 256);
    g.fillStyle = 'rgba(0,0,0,.28)'; g.fillRect(0, 0, 10, 256);
    g.strokeStyle = 'rgba(255,236,190,.75)'; g.lineWidth = 5; g.strokeRect(14, 10, 104, 216);
    // 锯齿边
    g.fillStyle = 'rgba(0,0,0,.35)';
    for (let y = 0; y < 256; y += 16) { g.beginPath(); g.moveTo(128, y); g.lineTo(116, y + 8); g.lineTo(128, y + 16); g.fill(); }
    g.beginPath(); g.moveTo(0, 256); for (let x = 0; x <= 128; x += 16) { g.lineTo(x, 240); g.lineTo(x + 8, 256); } g.fill();
    if (!pennant) {
      g.fillStyle = '#f4ead2'; g.beginPath(); g.arc(66, 112, 46, 0, Math.PI * 2); g.fill();
      g.strokeStyle = 'rgba(0,0,0,.25)'; g.lineWidth = 3; g.stroke();
      g.fillStyle = '#1a1210';
      g.font = 'bold 66px "STKaiti","Kaiti SC","KaiTi","Songti SC","Noto Serif SC","WenQuanYi Zen Hei",serif';
      g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillText(glyph || '将', 66, 116);
    }
    const t = new THREE.CanvasTexture(cv);
    t.anisotropy = 2;
    return t;
  }

  class Arena {
    constructor(scene, kind, colA, colB, glyphA, glyphB, seed) {
      this.scene = scene;
      this.kind = kind;
      this.pal = PALETTES[kind];
      this.group = new THREE.Group();
      this.group.name = 'DuelArena';
      scene.add(this.group);
      this.geos = []; this.mats = []; this.texs = [];
      this.banners = [];
      this.torches = [];
      this.rnd = SG.SeededRandom(seed || 7);
      const pal = this.pal;
      scene.background = C(pal.fog);
      scene.fog = new THREE.Fog(C(pal.fog), pal.fogN, pal.fogF);
      // 天空
      const sm = new THREE.ShaderMaterial({
        uniforms: { uTop: { value: C(pal.top).clone() }, uHor: { value: C(pal.hor).clone() }, uLow: { value: C(pal.low).clone() }, uSun: { value: C(pal.sun).clone() }, uSunDir: { value: new THREE.Vector3().fromArray(pal.sunDir).normalize() } },
        vertexShader: SKY_VS, fragmentShader: SKY_FS, side: THREE.BackSide, depthWrite: false, depthTest: false, fog: false,
      });
      const sg = new THREE.SphereGeometry(300, 24, 12);
      this.geos.push(sg); this.mats.push(sm);
      this.sky = new THREE.Mesh(sg, sm);
      this.sky.renderOrder = -1000; this.sky.frustumCulled = false;
      this.group.add(this.sky);
      // 地面 + 远山 + 景物
      const pb = new PB();
      this.buildGround(pb);
      this.buildRidges(pb);
      const extra = new SG.MeshBuilder();   // Unity 坐标（士兵、城楼屋顶）
      this.buildProps(pb, extra, colA, colB);
      this.addMesh(pb.geo(), envMat(), true, true);
      if (extra.count > 0) this.addMesh(extra.toGeometry(), envMat(), true, true);
      // 帅旗（势力色 + 姓氏）
      const glyphs = [glyphA, glyphB], cols = [colA, colB];
      const bx = 6.6;
      for (const s of [0, 1]) {
        const sx = s === 0 ? -1 : 1;
        this.addBanner(sx * bx - 0.5, -5.4, 4.7, 1.05, 2.2, cols[s], glyphs[s], false, s * 2.1);
        this.addBanner(sx * (bx + 3.8), -9.6, 4.6, 0.8, 1.7, cols[s], glyphs[s], true, s * 1.3 + 0.7);
        this.addBanner(sx * (bx + 7.6), -12.5, 5.0, 0.8, 1.7, cols[s], glyphs[s], true, s * 0.9 + 1.4);
      }
    }
    addMesh(g, mat, cast, recv) {
      this.geos.push(g);
      const o = new THREE.Mesh(g, mat);
      o.castShadow = !!cast; o.receiveShadow = !!recv;
      this.group.add(o);
      return o;
    }
    // 地面：中间平坦的对决场地，两侧与后方起伏
    buildGround(pb) {
      const pal = this.pal, r = this.rnd, kind = this.kind;
      const X0 = -44, X1 = 44, Z0 = -44, Z1 = 10, S = 2;
      const hill = kind === 'hill' ? 2.2 : kind === 'forest' ? 0.8 : kind === 'castle' ? 0.2 : 1;
      const h = (x, z) => {
        if (kind === 'river') {
          if (z < -3.4 && z > -26) return -0.8;
          if (z <= -26) return (-26 - z) * 0.15 + M.perlinNoise(x * 0.08 + 3, z * 0.08) * 2;
        }
        if (Math.abs(z) < 2.6 && Math.abs(x) < 13) return 0;
        const back = z < 0 ? Math.max(0, -z - 3) : Math.max(0, z - 3) * 0.4;
        const n = M.perlinNoise(x * 0.07 + 11, z * 0.07 + 5) - 0.45;
        return back * 0.09 * hill + n * 1.6 * Math.min(1, back / 4) * hill + Math.max(0, Math.abs(x) - 13) * 0.05 * hill;
      };
      const gc = C(pal.ground), gc2 = C(pal.ground2), lane = C(pal.lane);
      for (let x = X0; x < X1; x += S) {
        for (let z = Z0; z < Z1; z += S) {
          const a = [x, h(x, z), z], b = [x, h(x, z + S), z + S], c = [x + S, h(x + S, z + S), z + S], d = [x + S, h(x + S, z), z];
          const cz = z + S / 2, cx = x + S / 2;
          let col = mixC(gc, gc2, M.clamp01(M.perlinNoise(cx * 0.15, cz * 0.15) * 1.2 - 0.1));
          const laneK = 1 - M.smoothStep(0, 1, (Math.abs(cz) - 1.2) / 2.2);
          if (Math.abs(cx) < 14) col = mixC(col, lane, laneK);
          if (kind === 'castle' && Math.abs(cz) < 3.6 && Math.abs(cx) < 16) col = ((Math.floor(cx / 2) + Math.floor(cz / 2)) & 1) ? sh(lane, 0.05) : sh(lane, -0.06);
          if (kind === 'river' && cz < -3.4 && cz > -26) col = C('#5a6a5a');
          const j = (r.nextDouble() - 0.5) * 0.08;
          this.quadSplit(pb, a, b, c, d, sh(col, j), sh(col, j - 0.03));
        }
      }
    }
    quadSplit(pb, a, b, c, d, c1, c2) { pb.tri(a, b, c, c1); pb.tri(a, c, d, c2); }
    // 远山（两层剪影，雾中淡去）
    buildRidges(pb) {
      const pal = this.pal, kind = this.kind;
      const layers = kind === 'castle' ? [[-60, 7, pal.mid], [-110, 16, pal.far]] : kind === 'hill' ? [[-46, 12, pal.mid], [-95, 30, pal.far]] : [[-55, 8, pal.mid], [-105, 22, pal.far]];
      layers.forEach(([z, hgt, col], li) => {
        const c = C(col);
        let prev = null;
        for (let x = -200; x <= 200; x += 12) {
          const n = M.perlinNoise(x * 0.012 + li * 7, li * 3.1);
          const y = hgt * (0.35 + n * 0.9) + (li === 1 ? Math.max(0, 1 - Math.abs(x + 30) / 60) * hgt * 0.5 : 0);
          const p = [x, y, z + Math.sin(x * 0.05) * 6];
          if (prev) {
            pb.quad([prev[0], -4, prev[2]], [p[0], -4, p[2]], p, prev, sh(c, (n - 0.5) * 0.15));
            pb.tri(prev, [(prev[0] + p[0]) / 2, (prev[1] + p[1]) / 2 - hgt * 0.15, (prev[2] + p[2]) / 2 + 3], p, sh(c, -0.08));
          }
          prev = p;
        }
      });
    }
    tree(pb, x, z, s, pine) {
      const y = 0;
      const leaf = C(this.pal.leaf);
      const j = (this.rnd.nextDouble() - 0.5) * 0.15;
      pb.prism(x, z, y, y + 1.2 * s, 0.14 * s, 0.1 * s, 5, '#4a3424');
      if (pine) {
        pb.prism(x, z, y + 0.8 * s, y + 2.6 * s, 1.0 * s, 0, 7, sh(leaf, j - 0.05));
        pb.prism(x, z, y + 1.7 * s, y + 3.4 * s, 0.75 * s, 0, 7, sh(leaf, j + 0.04));
        pb.prism(x, z, y + 2.6 * s, y + 4.1 * s, 0.45 * s, 0, 7, sh(leaf, j + 0.1));
      } else {
        pb.blob(x, y + 2.0 * s, z, 1.2 * s, 1.0 * s, 1.2 * s, sh(leaf, j), (x * 31 + z * 7) | 0);
        pb.blob(x + 0.5 * s, y + 2.6 * s, z + 0.2 * s, 0.8 * s, 0.7 * s, 0.8 * s, sh(leaf, j + 0.08), (x * 13 + z) | 0);
      }
    }
    rock(pb, x, z, s, col) {
      pb.blob(x, s * 0.35, z, s, s * 0.7, s * 0.9, col || this.pal.rock, (x * 17 + z * 5) | 0);
    }
    // 两军阵列（Unity 坐标的 MeshBuilder：three z = −unity z）
    army(mb, x0, x1, z0, z1, col, y, rows, cols) {
      const team = SG.Gfx.color(col);
      for (let i = 0; i < rows; i++)
        for (let j = 0; j < cols; j++) {
          const x = x0 + (x1 - x0) * (cols > 1 ? j / (cols - 1) : 0.5) + (this.rnd.nextDouble() - 0.5) * 0.4;
          const z = z0 + (z1 - z0) * (rows > 1 ? i / (rows - 1) : 0.5) + (this.rnd.nextDouble() - 0.5) * 0.3;
          SG.Models.soldier(mb, new THREE.Vector3(x, y || 0, -z), team, true, 3.3);
        }
    }
    buildProps(pb, mb, colA, colB) {
      const r = this.rnd, kind = this.kind, pal = this.pal;
      const scatter = (n, fn, zMin, zMax, xr) => {
        for (let i = 0; i < n; i++) {
          const x = (r.nextDouble() * 2 - 1) * (xr || 30), z = zMin + r.nextDouble() * (zMax - zMin);
          if (Math.abs(z) < 2.2 && Math.abs(x) < 12) continue;
          if (z > 2.2 && Math.abs(x) < 5) continue;          // 镜头前方中央留空
          fn(x, z, r.nextDouble());
        }
      };
      const tuft = (x, z, k) => {
        const g = mixC(pal.leaf, pal.ground2, k * 0.6);
        for (let i = 0; i < 3; i++) pb.rod([x + (i - 1) * 0.06, 0, z], [x + (i - 1) * 0.16, 0.32 + k * 0.25, z + (k - 0.5) * 0.1], 0.04, 0, 3, sh(g, i * 0.05));
      };
      if (kind !== 'castle') {
        this.army(mb, -16, -8.5, -11, -14, colA, 0, 3, 7);
        this.army(mb, 8.5, 16, -11, -14, colB, 0, 3, 7);
      }
      switch (kind) {
        case 'plain':
          scatter(130, tuft, -16, 6);
          scatter(18, (x, z, k) => this.rock(pb, x, z, 0.2 + k * 0.35), -14, 6);
          for (let i = 0; i < 9; i++) {           // 战场上插着的断枪残旗
            const x = (r.nextDouble() * 2 - 1) * 11, z = -2.8 - r.nextDouble() * 3.5;
            const a = (r.nextDouble() - 0.5) * 0.6;
            pb.rod([x, 0, z], [x + Math.sin(a) * 1.5, Math.cos(a) * 1.5, z], 0.025, 0.02, 4, '#5a3a22');
            if (i % 3 === 0) pb.slab([[x + Math.sin(a) * 1.4, Math.cos(a) * 1.4], [x + Math.sin(a) * 1.4 + 0.45, Math.cos(a) * 1.4 - 0.1], [x + Math.sin(a) * 1.4 + 0.4, Math.cos(a) * 1.4 - 0.4], [x + Math.sin(a) * 1.2, Math.cos(a) * 1.2 - 0.3]], z - 0.01, z + 0.01, i % 2 ? colA : colB);
          }
          for (let i = 0; i < 14; i++) this.tree(pb, (r.nextDouble() * 2 - 1) * 40, -16 - r.nextDouble() * 18, 0.9 + r.nextDouble() * 0.6, r.nextDouble() < 0.4);
          break;
        case 'forest':
          for (let i = 0; i < 70; i++) {
            const x = (r.nextDouble() * 2 - 1) * 42, z = -5.5 - r.nextDouble() * 32;
            if (z > -11 && Math.abs(x) < 5.5 && r.nextDouble() < 0.6) continue;
            this.tree(pb, x, z, 0.9 + r.nextDouble() * 0.8, r.nextDouble() < 0.55);
          }
          for (const x of [-11.5, -9.2, 9.6, 12.2]) this.tree(pb, x, -3.6 - r.nextDouble(), 1.2 + r.nextDouble() * 0.3, x > 0);
          scatter(90, tuft, -12, 6);
          scatter(16, (x, z, k) => pb.blob(x, 0.15, z, 0.4 + k * 0.4, 0.25, 0.4, sh(pal.leaf, 0.1), (x * 3) | 0), -10, 5);
          pb.rod([-4.5, 0.2, -4.4], [-1.2, 0.25, -4.9], 0.22, 0.18, 6, '#5a4030');   // 倒木
          break;
        case 'hill':
          for (let i = 0; i < 12; i++) this.rock(pb, (r.nextDouble() * 2 - 1) * 30, -6 - r.nextDouble() * 12, 0.6 + r.nextDouble() * 1.4, sh(pal.rock, (r.nextDouble() - 0.5) * 0.2));
          for (const [x, z, s] of [[-19, -17, 5], [-10, -22, 7], [15, -18, 6], [24, -24, 8], [3, -28, 6]]) this.rock(pb, x, z, s, sh(pal.rock, -0.1));
          scatter(26, (x, z, k) => this.rock(pb, x, z, 0.15 + k * 0.25), -10, 6);
          for (let i = 0; i < 16; i++) this.tree(pb, (r.nextDouble() * 2 - 1) * 40, -12 - r.nextDouble() * 20, 0.8 + r.nextDouble() * 0.5, true);
          scatter(60, tuft, -12, 6);
          break;
        case 'river': {
          for (let i = 0; i < 70; i++) {     // 芦苇
            const x = (r.nextDouble() * 2 - 1) * 30, z = -3.0 - r.nextDouble() * 0.9;
            pb.rod([x, -0.3, z], [x + (r.nextDouble() - 0.5) * 0.3, 0.9 + r.nextDouble() * 0.7, z], 0.025, 0.006, 3, r.nextDouble() < 0.5 ? '#9a9a5a' : '#7a8a4a');
          }
          scatter(40, (x, z, k) => this.rock(pb, x, z, 0.1 + k * 0.18), -3, 6);
          for (let i = 0; i < 26; i++) this.tree(pb, (r.nextDouble() * 2 - 1) * 50, -28 - r.nextDouble() * 10, 1 + r.nextDouble() * 0.6, r.nextDouble() < 0.4);
          scatter(50, tuft, 1.5, 6);
          // 水面（共享的水面材质，不释放）
          const wg = new THREE.PlaneGeometry(120, 23, 24, 6);
          wg.rotateX(-Math.PI / 2);
          wg.translate(0, -0.12, -14.8);
          const pos = wg.attributes.position, cols = new Float32Array(pos.count * 3);
          for (let i = 0; i < pos.count; i++) { const z = pos.getZ(i); cols[i * 3] = M.clamp01(1 - (-3.4 - z) / 6); cols[i * 3 + 1] = 0; cols[i * 3 + 2] = 0; }
          wg.setAttribute('color', new THREE.BufferAttribute(cols, 3));
          this.geos.push(wg);
          const water = new THREE.Mesh(wg, SG.Gfx.water());
          water.receiveShadow = false;
          this.group.add(water);
          break;
        }
        case 'castle': {
          // 城墙、城门楼、火把
          const stone = '#6e665c', z = -12.5, H = 6.6;
          pb.box(0, H / 2, z, 100, H, 2.4, stone);
          for (let x = -50; x <= 50; x += 1.4) pb.box(x, H + 0.35, z + 1.0, 0.8, 0.7, 0.4, sh(stone, 0.06));
          for (let x = -50; x <= 50; x += 6) pb.box(x, H / 2, z + 1.25, 0.12, H, 0.05, sh(stone, -0.12));
          for (let y = 1; y < H; y += 1.1) pb.box(0, y, z + 1.22, 100, 0.04, 0.05, sh(stone, -0.08));
          pb.box(0, 2.0, z + 1.26, 3.4, 4.0, 0.1, '#1e140e');                 // 城门
          for (const yy of [0.8, 1.6, 2.4, 3.2]) for (const xx of [-1.2, -0.4, 0.4, 1.2]) pb.box(xx, yy, z + 1.33, 0.12, 0.12, 0.06, '#8a7040');
          pb.box(0, 4.15, z + 1.27, 3.8, 0.3, 0.12, sh(stone, -0.2));
          for (const x of [-14, 14]) pb.box(x, H / 2 + 0.7, z + 0.2, 4.4, H + 1.4, 3.2, sh(stone, -0.04));
          // 城门楼（Unity 坐标：z 取反）
          mb.box(new THREE.Vector3(0, H + 1.2, -z), new THREE.Vector3(8.5, 2.4, 3.4), SG.Gfx.color('#7a2418'));
          for (const x of [-3.7, -1.25, 1.25, 3.7]) mb.box(new THREE.Vector3(x, H + 1.2, -z - 1.72), new THREE.Vector3(0.28, 2.4, 0.1), SG.Gfx.color('#4a140e'));
          mb.chineseRoof(new THREE.Vector3(0, H + 2.4, -z), 10.8, 5.0, 2.8, SG.Gfx.color('#262a36'));
          mb.box(new THREE.Vector3(0, H + 3.5, -z), new THREE.Vector3(5.6, 1.2, 2.4), SG.Gfx.color('#7a2418'));
          mb.chineseRoof(new THREE.Vector3(0, H + 4.1, -z), 7.4, 3.6, 2.4, SG.Gfx.color('#262a36'));
          for (const x of [-14, 14]) mb.chineseRoof(new THREE.Vector3(x, H + 1.4, -z - 0.2), 5.4, 4.0, 1.8, SG.Gfx.color('#262a36'));
          // 城头守军（b 方）与城下攻军（a 方）
          this.army(mb, -10, -4.5, -12.2, -12.8, colB, H, 2, 5);
          this.army(mb, 4.5, 10, -12.2, -12.8, colB, H, 2, 5);
          this.army(mb, -17, -9, -8.2, -10.4, colA, 0, 3, 6);
          for (const x of [-3.6, 3.6]) {
            pb.prism(x, -9.6, 0, 2.4, 0.07, 0.06, 5, '#2a1e14');
            pb.prism(x, -9.6, 2.4, 2.65, 0.14, 0.24, 6, '#2a2424');
            this.torches.push(new THREE.Vector3(x, 2.85, -9.6));
          }
          scatter(20, (x, zz, k) => this.rock(pb, x, zz, 0.12 + k * 0.2), -8, 6);
          break;
        }
      }
    }
    addBanner(x, z, poleH, w, h, color, glyph, pennant, phase) {
      const tex = bannerTexture(SG.UI && SG.UI.cssColor ? SG.UI.cssColor(color) : color, glyph, pennant);
      this.texs.push(tex);
      const mat = new THREE.MeshLambertMaterial({ map: tex, side: THREE.DoubleSide });
      this.mats.push(mat);
      const g = new THREE.PlaneGeometry(w, h, 6, 6);
      g.translate(w / 2, -h / 2, 0);
      this.geos.push(g);
      const cloth = new THREE.Mesh(g, mat);
      cloth.castShadow = false; cloth.receiveShadow = false;
      cloth.position.set(x + 0.05, poleH - 0.1, z);
      this.group.add(cloth);
      const pb = new PB();
      pb.prism(x, z, 0, poleH + 0.1, 0.05, 0.04, 6, '#4a2c1a');
      pb.rod([x, poleH - 0.05, z], [x + w + 0.05, poleH - 0.05, z], 0.025, 0.025, 4, '#4a2c1a');
      pb.prism(x, z, poleH + 0.1, poleH + 0.42, 0.06, 0, 5, GOLD);
      pb.blob(x, poleH + 0.02, z, 0.08, 0.1, 0.08, RED, 41);
      this.addMesh(pb.geo(), envMat(), true, false);
      const base = Float32Array.from(g.attributes.position.array);
      this.banners.push({ cloth, g, base, w, phase });
    }
    update(t) {
      for (const b of this.banners) {
        const pos = b.g.attributes.position, a = pos.array, base = b.base;
        for (let i = 0; i < pos.count; i++) {
          const x = base[i * 3], y = base[i * 3 + 1];
          const u = x / b.w;
          const wave = Math.sin(t * 3.0 + u * 4.4 + y * 0.6 + b.phase) * 0.16 + Math.sin(t * 5.3 + u * 7.0 + b.phase * 2) * 0.05;
          a[i * 3] = x - u * u * 0.05;
          a[i * 3 + 1] = y - u * u * 0.06;
          a[i * 3 + 2] = wave * u;
        }
        pos.needsUpdate = true;
        b.g.computeVertexNormals();
      }
    }
    dispose() {
      for (const g of this.geos) g.dispose();
      for (const m of this.mats) m.dispose();
      for (const t of this.texs) t.dispose();
      this.geos.length = this.mats.length = this.texs.length = 0;
      if (this.group.parent) this.group.parent.remove(this.group);
    }
  }

  // ============================================================ 视图：特效 --
  const PART_VS = 'attribute vec4 aColor;\nattribute float aSize;\nuniform float uScale;\nvarying vec4 vColor;\nvoid main(){ vColor = aColor; vec4 mv = modelViewMatrix * vec4(position, 1.0); gl_PointSize = max(1.0, aSize * uScale / max(0.05, -mv.z)); gl_Position = projectionMatrix * mv; }';
  const PART_FS = 'uniform sampler2D uMap;\nuniform float uSoft;\nvarying vec4 vColor;\nvoid main(){ float a = pow(texture2D(uMap, gl_PointCoord).a, uSoft) * vColor.a; if (a < 0.004) discard; gl_FragColor = vec4(vColor.rgb, a); }';
  class Particles {
    constructor(max, blending, soft, order) {
      this.max = max; this.parts = [];
      this.pos = new Float32Array(max * 3); this.col = new Float32Array(max * 4); this.size = new Float32Array(max);
      const g = new THREE.BufferGeometry();
      this.aPos = new THREE.BufferAttribute(this.pos, 3).setUsage(THREE.DynamicDrawUsage);
      this.aCol = new THREE.BufferAttribute(this.col, 4).setUsage(THREE.DynamicDrawUsage);
      this.aSize = new THREE.BufferAttribute(this.size, 1).setUsage(THREE.DynamicDrawUsage);
      g.setAttribute('position', this.aPos); g.setAttribute('aColor', this.aCol); g.setAttribute('aSize', this.aSize);
      g.setDrawRange(0, 0);
      this.geometry = g;
      this.material = new THREE.ShaderMaterial({
        uniforms: { uMap: { value: SG.Gfx.softDotTexture }, uScale: { value: 400 }, uSoft: { value: soft } },
        vertexShader: PART_VS, fragmentShader: PART_FS, transparent: true, depthWrite: false, blending, fog: false,
      });
      this.points = new THREE.Points(g, this.material);
      this.points.frustumCulled = false;
      this.points.renderOrder = order;
    }
    add(p) {
      if (this.parts.length >= this.max) this.parts.shift();
      p.age = 0;
      this.parts.push(p);
    }
    update(dt, scale) {
      this.material.uniforms.uScale.value = scale;
      const P = this.parts;
      let n = 0;
      for (let i = 0; i < P.length; i++) {
        const p = P[i];
        p.age += dt;
        if (p.age >= p.life) continue;
        p.vy -= (p.grav || 0) * dt;
        if (p.drag) { const k = Math.max(0, 1 - p.drag * dt); p.vx *= k; p.vy *= k; p.vz *= k; }
        p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
        if (p.floor && p.y < 0.02) { p.y = 0.02; p.vy = -p.vy * 0.3; p.vx *= 0.6; }
        P[n++] = p;
      }
      P.length = n;
      for (let i = 0; i < n; i++) {
        const p = P[i], t = p.age / p.life;
        this.pos[i * 3] = p.x; this.pos[i * 3 + 1] = p.y; this.pos[i * 3 + 2] = p.z;
        this.col[i * 4] = p.r; this.col[i * 4 + 1] = p.g; this.col[i * 4 + 2] = p.b;
        const fin = p.fadeIn ? Math.min(1, t / p.fadeIn) : 1;
        this.col[i * 4 + 3] = p.a * (1 - Math.pow(t, p.ac || 1)) * fin;
        this.size[i] = p.size * (1 + ((p.grow || 0) * t));
      }
      this.geometry.setDrawRange(0, n);
      if (n > 0) { this.aPos.needsUpdate = true; this.aCol.needsUpdate = true; this.aSize.needsUpdate = true; }
    }
    dispose() { this.geometry.dispose(); this.material.dispose(); }
  }

  // 刀光拖尾：记录兵器刃根与刃尖的轨迹，生成渐隐的叠加色带
  class Trail {
    constructor(n) {
      this.n = n; this.base = []; this.tip = []; this.life = [];
      for (let i = 0; i < n; i++) { this.base.push(new THREE.Vector3()); this.tip.push(new THREE.Vector3()); this.life.push(0); }
      this.count = 0;
      const verts = n * 2;
      this.posA = new Float32Array(verts * 3); this.colA = new Float32Array(verts * 3);
      const idx = [];
      for (let i = 0; i < n - 1; i++) { const a = i * 2, b = a + 1, c = a + 2, d = a + 3; idx.push(a, b, d, a, d, c); }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(this.posA, 3).setUsage(THREE.DynamicDrawUsage));
      g.setAttribute('color', new THREE.BufferAttribute(this.colA, 3).setUsage(THREE.DynamicDrawUsage));
      g.setIndex(idx);
      g.setDrawRange(0, 0);
      this.geometry = g;
      this.material = new THREE.MeshBasicMaterial({ vertexColors: true, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide, fog: false });
      this.mesh = new THREE.Mesh(g, this.material);
      this.mesh.frustumCulled = false;
      this.mesh.renderOrder = 20;
      this.color = new THREE.Color(1, 1, 1);
      this.on = false;
    }
    push(b, t) {
      for (let i = this.n - 1; i > 0; i--) { this.base[i].copy(this.base[i - 1]); this.tip[i].copy(this.tip[i - 1]); this.life[i] = this.life[i - 1]; }
      this.base[0].copy(b); this.tip[0].copy(t); this.life[0] = this.on ? 1 : 0;
      this.count = Math.min(this.n, this.count + 1);
    }
    update(dt) {
      let any = false;
      for (let i = 0; i < this.n; i++) { this.life[i] = Math.max(0, this.life[i] - dt * 2.5); if (this.life[i] > 0) any = true; }
      const c = this.color;
      for (let i = 0; i < this.n; i++) {
        const k = this.life[i] * (1 - i / this.n);
        const B = this.base[i], T = this.tip[i];
        // 刃根一侧较暗，刃尖最亮
        this.posA.set([B.x + (T.x - B.x) * 0.15, B.y + (T.y - B.y) * 0.15, B.z + (T.z - B.z) * 0.15], i * 6);
        this.posA.set([T.x, T.y, T.z], i * 6 + 3);
        this.colA.set([c.r * k * 0.25, c.g * k * 0.25, c.b * k * 0.25], i * 6);
        this.colA.set([c.r * k, c.g * k, c.b * k], i * 6 + 3);
      }
      this.geometry.attributes.position.needsUpdate = true;
      this.geometry.attributes.color.needsUpdate = true;
      this.geometry.setDrawRange(0, any && this.count > 1 ? (Math.min(this.count, this.n) - 1) * 6 : 0);
    }
    dispose() { this.geometry.dispose(); this.material.dispose(); }
  }

  // 命中闪光的四芒星贴图（模块级缓存，不随单挑释放）
  let starTex = null;
  function starTexture() {
    if (starTex) return starTex;
    const n = 64, data = new Uint8Array(n * n * 4);
    for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) {
      const u = (x + 0.5) / n * 2 - 1, v = (y + 0.5) / n * 2 - 1;
      const r = Math.hypot(u, v);
      const ray = Math.max(Math.exp(-Math.abs(u) * 18) * (1 - Math.abs(v)), Math.exp(-Math.abs(v) * 18) * (1 - Math.abs(u)));
      const a = M.clamp01(ray * 1.1 + Math.max(0, 1 - r * 2.2) ** 2);
      const i = (y * n + x) * 4;
      data[i] = data[i + 1] = data[i + 2] = 255; data[i + 3] = Math.round(a * 255);
    }
    starTex = new THREE.DataTexture(data, n, n, THREE.RGBAFormat);
    starTex.magFilter = THREE.LinearFilter; starTex.minFilter = THREE.LinearFilter;
    starTex.needsUpdate = true;
    return starTex;
  }
  // ============================================================ HUD 样式 --
  const KAI = '"STKaiti","Kaiti SC","KaiTi","Songti SC","Noto Serif SC","WenQuanYi Zen Hei",serif';
  const SANS = '"PingFang SC","Microsoft YaHei","Noto Sans SC",system-ui,sans-serif,"WenQuanYi Zen Hei"';
  function injectStyle() {
    if (typeof document === 'undefined' || document.getElementById('sg-duel-game-style')) return;
    const s = document.createElement('style');
    s.id = 'sg-duel-game-style';
    s.textContent = `
.sgd{position:absolute;inset:0;pointer-events:none;overflow:hidden;color:#f5eddb;font-family:${SANS};
  --u:clamp(10px,min(1.2vw,2.15vh),18px);--b:clamp(54px,min(15.5vh,18vw),86px);--sal:0px;--sar:0px;--sab:0px;--sat:0px;
  -webkit-user-select:none;user-select:none;-webkit-touch-callout:none;z-index:5;}
.sgd.sgd-fixed{position:fixed;z-index:60;--sal:env(safe-area-inset-left,0px);--sar:env(safe-area-inset-right,0px);--sab:env(safe-area-inset-bottom,0px);--sat:env(safe-area-inset-top,0px);}
.sgd *{box-sizing:border-box;}
.sgd-vig{position:fixed;inset:-2px;pointer-events:none;background:radial-gradient(ellipse 75% 70% at 50% 52%,rgba(0,0,0,0) 55%,rgba(10,4,0,.42) 100%);}
.sgd-top{position:absolute;top:calc(var(--sat) + .7*var(--u));left:calc(var(--sal) + 1.1*var(--u));right:calc(var(--sar) + 1.1*var(--u));
  display:grid;grid-template-columns:1fr auto 1fr;column-gap:calc(1.1*var(--u));align-items:start;transition:opacity .35s ease, transform .35s ease;}
.sgd.sgd-nohud .sgd-top{opacity:0;transform:translateY(-1.2em);}
.sgd-side{display:flex;gap:calc(.8*var(--u));align-items:center;min-width:0;}
.sgd-side.b{flex-direction:row-reverse;}
.sgd-port{position:relative;width:calc(5.4*var(--u));height:calc(5.4*var(--u));flex:none;border-radius:calc(.7*var(--u));overflow:hidden;
  border:2px solid var(--fc);background:radial-gradient(circle at 50% 35%,#3a3040,#121018);box-shadow:0 0 0 1px rgba(0,0,0,.7),0 .3em 1em rgba(0,0,0,.55);}
.sgd-port>*{width:100%!important;height:100%!important;}
.sgd-port img{width:100%;height:100%;object-fit:cover;display:block;}
.sgd-port .sgd-medal{display:grid;place-items:center;width:100%;height:100%;font:700 calc(3*var(--u))/1 ${KAI};color:#fff3da;
  background:var(--fc);background:radial-gradient(circle at 50% 30%,color-mix(in srgb,var(--fc) 80%,#fff),color-mix(in srgb,var(--fc) 60%,#000));text-shadow:0 2px 0 rgba(0,0,0,.5);}
.sgd-side.hit .sgd-port{animation:sgd-shake .28s ease;}
.sgd-info{flex:1;min-width:0;display:flex;flex-direction:column;gap:calc(.32*var(--u));}
.sgd-side.b .sgd-info{align-items:flex-end;}
.sgd-name{display:flex;align-items:baseline;gap:calc(.6*var(--u));white-space:nowrap;}
.sgd-side.b .sgd-name{flex-direction:row-reverse;}
.sgd-name b{font:700 calc(1.7*var(--u))/1.05 ${KAI};color:#fff4dc;letter-spacing:.06em;text-shadow:0 2px 0 rgba(0,0,0,.75),0 0 10px rgba(0,0,0,.6);}
.sgd-war{font:700 max(11px,calc(.95*var(--u)))/1 ${SANS};color:#1a1208;background:linear-gradient(180deg,#ffe7a0,#d9a640);padding:.18em .45em;border-radius:.3em;
  box-shadow:0 1px 0 rgba(0,0,0,.6);letter-spacing:.04em;}
.sgd-war i{font-style:normal;opacity:.75;margin-right:.2em;}
.sgd-tag{font:600 max(11px,calc(.85*var(--u)))/1 ${SANS};color:#ffd98a;opacity:.9;}
.sgd-hp{position:relative;width:100%;height:calc(1.55*var(--u));transform:skewX(-22deg);background:linear-gradient(180deg,#120a0c,#2a1618);
  border:1px solid rgba(243,201,105,.75);box-shadow:0 0 0 1px rgba(0,0,0,.8),0 .2em .6em rgba(0,0,0,.5);overflow:hidden;}
.sgd-side.b .sgd-hp{transform:skewX(22deg);}
.sgd-hp i{position:absolute;top:0;bottom:0;left:0;width:calc(var(--v,1)*100%);}
.sgd-side.b .sgd-hp i{left:auto;right:0;}
.sgd-hp .trail{background:linear-gradient(180deg,#fff6d8,#ff9a6a);}
.sgd-hp .fill{background:linear-gradient(180deg,#fff1a8 0%,#f2b33a 38%,#d9741c 70%,#a8460e 100%);box-shadow:inset 0 1px 0 rgba(255,255,255,.55);}
.sgd-hp.low .fill{background:linear-gradient(180deg,#ffb0a0 0%,#ff4a3a 40%,#b8180e 100%);animation:sgd-low .5s ease-in-out infinite alternate;}
.sgd-hp .fill::after{content:"";position:absolute;left:0;right:0;top:12%;height:22%;background:rgba(255,255,255,.35);}
.sgd-hp .tick{position:absolute;top:0;bottom:0;width:1px;background:rgba(0,0,0,.35);}
.sgd-rage{position:relative;width:78%;height:calc(.62*var(--u));transform:skewX(-22deg);background:rgba(0,0,0,.6);border:1px solid rgba(255,255,255,.2);overflow:hidden;}
.sgd-side.b .sgd-rage{transform:skewX(22deg);}
.sgd-rage i{position:absolute;top:0;bottom:0;left:0;width:calc(var(--v,0)*100%);background:linear-gradient(90deg,#5a1a9a,#d8389a 60%,#ff6a3a);}
.sgd-side.b .sgd-rage i{left:auto;right:0;background:linear-gradient(270deg,#5a1a9a,#d8389a 60%,#ff6a3a);}
.sgd-rage.full{border-color:#ffe08a;box-shadow:0 0 .7em rgba(255,200,80,.85);}
.sgd-rage.full i{background:linear-gradient(90deg,#ffd040,#fff2b0,#ffb020);background-size:200% 100%;animation:sgd-sweep .7s linear infinite;}
.sgd-ragerow{display:flex;align-items:center;gap:calc(.5*var(--u));width:100%;}
.sgd-side.b .sgd-ragerow{flex-direction:row-reverse;}
.sgd-ragelbl{font:700 max(11px,calc(.85*var(--u)))/1 ${KAI};color:#d8a8ff;white-space:nowrap;text-shadow:0 1px 0 #000;}
.sgd-ragelbl.full{color:#ffe08a;animation:sgd-blink .6s ease-in-out infinite alternate;}
.sgd-timer{position:relative;display:flex;flex-direction:column;align-items:center;gap:calc(.2*var(--u));}
.sgd-timer b{display:grid;place-items:center;width:calc(4.8*var(--u));height:calc(4.8*var(--u));border-radius:50%;
  background:radial-gradient(circle at 50% 35%,#3a2c34,#120c12 70%);border:2px solid #f3c969;box-shadow:0 0 0 2px rgba(0,0,0,.7),0 .3em 1em rgba(0,0,0,.6),inset 0 0 .8em rgba(243,201,105,.25);
  font:700 calc(2.35*var(--u))/1 ${KAI};color:#ffe9b0;text-shadow:0 2px 0 #4d1f05;font-variant-numeric:tabular-nums;}
.sgd-timer.low b{color:#ff8c73;animation:sgd-pulse .5s ease-in-out infinite alternate;}
.sgd-timer small{font:700 max(11px,calc(.85*var(--u)))/1 ${KAI};color:#f3c969;letter-spacing:.3em;text-shadow:0 1px 0 #000;padding-left:.3em;}
.sgd-combo{position:absolute;top:28%;display:flex;flex-direction:column;align-items:flex-start;opacity:0;transition:opacity .25s ease;}
.sgd-combo.a{left:calc(var(--sal) + 2.2*var(--u));}
.sgd-combo.b{right:calc(var(--sar) + 2.2*var(--u));align-items:flex-end;}
.sgd-combo.on{opacity:1;}
.sgd-combo b{font:italic 900 calc(4.2*var(--u))/1 ${KAI};background:linear-gradient(180deg,#fff6c8,#ffc23a 50%,#e0601a);-webkit-background-clip:text;background-clip:text;color:transparent;
  -webkit-text-stroke:1px rgba(60,20,0,.6);filter:drop-shadow(0 3px 0 rgba(0,0,0,.6));}
.sgd-combo span{font:700 calc(1.3*var(--u))/1 ${KAI};color:#ffe08a;letter-spacing:.2em;text-shadow:0 2px 0 #000;}
.sgd-combo.pop b{animation:sgd-pop .22s cubic-bezier(.17,.89,.32,1.4);}
.sgd-ann{position:absolute;left:0;right:0;top:36%;text-align:center;pointer-events:none;}
.sgd-ann>div{display:inline-block;font:700 calc(6*var(--u))/1 ${KAI};letter-spacing:.18em;padding-left:.18em;color:#fff2c8;
  text-shadow:0 .06em 0 #6a1a0a,0 0 .3em rgba(255,120,40,.6),0 .12em .3em rgba(0,0,0,.8);animation:sgd-ann 1s cubic-bezier(.2,.8,.2,1) both;}
.sgd-ann>div.small{font-size:calc(3*var(--u));}
.sgd-ann>div.red{color:#ff6a50;text-shadow:0 .06em 0 #2a0000,0 0 .4em rgba(255,40,20,.6),0 .12em .3em rgba(0,0,0,.8);}
.sgd-ann small{display:block;font:600 calc(1.2*var(--u))/1.4 ${SANS};letter-spacing:.1em;color:#f5eddb;margin-top:.5em;text-shadow:0 1px 3px #000;}
.sgd-legend{position:absolute;left:50%;bottom:calc(var(--sab) + .9*var(--u));transform:translateX(-50%);display:flex;flex-wrap:wrap;justify-content:center;gap:calc(.35*var(--u)) calc(.9*var(--u));
  padding:.45em .9em;border-radius:.6em;background:rgba(12,10,16,.55);border:1px solid rgba(243,201,105,.25);font:600 calc(.95*var(--u))/1.2 ${SANS};color:#e8dfca;white-space:nowrap;transition:opacity .3s ease;}
.sgd-legend span{display:inline-flex;align-items:center;gap:.3em;}
.sgd-k{display:inline-grid;place-items:center;min-width:1.7em;height:1.7em;padding:0 .35em;border-radius:.3em;background:linear-gradient(180deg,#f5ecd8,#cfc3a6);color:#1a1410;
  font:700 .9em/1 ${SANS};box-shadow:0 2px 0 #6a5a40,0 3px 3px rgba(0,0,0,.4);}
.sgd-k.r{background:linear-gradient(180deg,#ffb0a0,#e0503a);color:#fff;box-shadow:0 2px 0 #7a1a10,0 3px 3px rgba(0,0,0,.4);}
.sgd-touchhint{position:absolute;left:50%;bottom:calc(var(--sab) + .5*var(--u));transform:translateX(-50%);font:600 max(11px,calc(.9*var(--u)))/1.2 ${SANS};color:rgba(245,237,219,.75);
  white-space:nowrap;text-shadow:0 1px 2px #000;}
.sgd-pad,.sgd-btns{position:absolute;bottom:calc(var(--sab) + .9*var(--u));pointer-events:auto;touch-action:none;transition:opacity .3s ease;}
.sgd-pad{left:calc(var(--sal) + 1.3*var(--u));width:calc(var(--b)*2.25);height:calc(var(--b)*2.05);}
.sgd-btns{right:calc(var(--sar) + 1.3*var(--u));width:calc(var(--b)*2.45);height:calc(var(--b)*2.2);pointer-events:none;}
.sgd-btns .sgd-tb{pointer-events:auto;}
@media (max-aspect-ratio:1/1){.sgd-touchhint,.sgd-tag{display:none;}}
.sgd-tb{position:absolute;display:grid;place-items:center;width:var(--b);height:var(--b);border-radius:50%;
  background:radial-gradient(circle at 50% 35%,rgba(70,60,80,.55),rgba(14,12,20,.55));border:2px solid rgba(243,201,105,.6);
  box-shadow:0 .2em .6em rgba(0,0,0,.45),inset 0 1px 0 rgba(255,255,255,.15);color:#fff4dc;font:700 calc(var(--b)*.3)/1 ${KAI};
  text-shadow:0 1px 2px #000;transition:transform .06s ease, background .06s ease;-webkit-tap-highlight-color:transparent;}
.sgd-tb.dir{font:700 calc(var(--b)*.36)/1 ${SANS};}
.sgd-tb.on{transform:scale(.92);background:radial-gradient(circle at 50% 35%,rgba(243,201,105,.75),rgba(140,90,30,.6));}
.sgd-tb.big{width:calc(var(--b)*1.18);height:calc(var(--b)*1.18);font-size:calc(var(--b)*.34);border-color:rgba(255,140,110,.85);background:radial-gradient(circle at 50% 35%,rgba(200,56,44,.75),rgba(80,14,10,.65));}
.sgd-tb.sp{border-color:rgba(200,160,255,.5);color:rgba(255,255,255,.55);}
.sgd-tb.sp.ready{border-color:#ffe08a;color:#fff;background:radial-gradient(circle at 50% 35%,rgba(255,210,90,.85),rgba(160,60,20,.75));animation:sgd-glow .6s ease-in-out infinite alternate;}
.sgd-skip{position:absolute;right:calc(var(--sar) + 1.2*var(--u));top:calc(var(--sat) + 7.6*var(--u));pointer-events:auto;cursor:pointer;
  font:700 calc(1.05*var(--u))/1 ${SANS};color:#fff4dc;background:rgba(12,10,16,.65);border:1px solid rgba(243,201,105,.6);border-radius:2em;padding:.6em 1.1em;min-height:44px;display:flex;align-items:center;gap:.4em;}
.sgd-watch{position:absolute;left:50%;top:calc(var(--sat) + 7.8*var(--u));transform:translateX(-50%);font:700 calc(1*var(--u))/1 ${KAI};letter-spacing:.3em;color:#f3c969;text-shadow:0 1px 3px #000;}
.sgd-card{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);pointer-events:auto;cursor:pointer;width:min(92vw,calc(46*var(--u)));max-height:92%;overflow:hidden;
  background:linear-gradient(180deg,rgba(30,26,36,.94),rgba(12,10,16,.94));border:1px solid rgba(243,201,105,.75);border-radius:calc(1*var(--u));
  box-shadow:0 1em 3em rgba(0,0,0,.6),inset 0 0 0 1px rgba(243,201,105,.18);padding:calc(1.2*var(--u)) calc(1.6*var(--u));animation:sgd-cardin .35s cubic-bezier(.2,.8,.2,1) both;}
.sgd-card h2{margin:0 0 .5em;text-align:center;font:700 calc(2*var(--u))/1.1 ${KAI};letter-spacing:.4em;padding-left:.4em;color:#f3c969;text-shadow:0 2px 0 #4d1f05;}
.sgd-card .vs{text-align:center;font:600 calc(1*var(--u))/1.4 ${SANS};color:#d8cfbb;margin-bottom:.7em;}
.sgd-card .vs b{color:#ffe08a;}
.sgd-rows{display:grid;grid-template-columns:auto 1fr;gap:calc(.45*var(--u)) calc(1*var(--u));align-items:center;font:600 calc(1.05*var(--u))/1.25 ${SANS};}
.sgd-rows .keys{display:flex;gap:.3em;justify-content:flex-end;flex-wrap:nowrap;white-space:nowrap;}
.sgd-rows .desc small{color:#ada392;font-weight:500;margin-left:.4em;}
.sgd-card .go{margin-top:.9em;text-align:center;font:700 calc(1.25*var(--u))/1 ${KAI};letter-spacing:.3em;color:#fff4dc;animation:sgd-blink .7s ease-in-out infinite alternate;}
.sgd-tl{display:grid;grid-template-columns:1fr 1fr;gap:calc(1*var(--u));}
.sgd-tl>div{display:flex;flex-direction:column;gap:.5em;font:600 calc(1*var(--u))/1.3 ${SANS};}
.sgd-tl .row{display:flex;align-items:center;gap:.6em;}
.sgd-chip{display:inline-grid;place-items:center;min-width:2.6em;height:2.6em;border-radius:50%;border:2px solid rgba(243,201,105,.7);background:rgba(40,34,48,.8);font:700 .9em/1 ${KAI};color:#fff4dc;flex:none;}
.sgd-chip.big{background:rgba(160,40,30,.85);border-color:rgba(255,140,110,.85);}
.sgd-vs{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;gap:calc(3*var(--u));pointer-events:none;}
.sgd-vs .p{display:flex;flex-direction:column;align-items:center;gap:.4em;animation:sgd-vsl .55s cubic-bezier(.2,.8,.2,1) both;}
.sgd-vs .p.b{animation-name:sgd-vsr;}
.sgd-vs .p .sgd-port{width:calc(10*var(--u));height:calc(10*var(--u));border-width:3px;}
.sgd-vs .p .n{font:700 calc(2.4*var(--u))/1 ${KAI};letter-spacing:.12em;color:#fff4dc;text-shadow:0 3px 0 #000,0 0 12px rgba(0,0,0,.7);}
.sgd-vs .p .w{font:700 calc(1.05*var(--u))/1 ${SANS};color:#ffe08a;text-shadow:0 1px 2px #000;}
.sgd-vs .x{font:900 italic calc(5.5*var(--u))/1 ${KAI};color:#fff;text-shadow:0 0 .2em #ff5030,0 .08em 0 #6a1000;animation:sgd-vsx .6s .25s cubic-bezier(.17,.89,.32,1.4) both;}
.sgd-cut{position:absolute;left:-10%;right:-10%;top:40%;height:calc(9*var(--u));transform:translateY(-50%) rotate(-4deg);display:flex;align-items:center;gap:calc(1.5*var(--u));
  padding:0 16%;background:linear-gradient(90deg,rgba(0,0,0,0),var(--sc) 18%,var(--sc) 82%,rgba(0,0,0,0));animation:sgd-cutl .9s cubic-bezier(.2,.8,.2,1) both;}
.sgd-cut.b{flex-direction:row-reverse;animation-name:sgd-cutr;}
.sgd-cut .sgd-port{width:calc(7.6*var(--u));height:calc(7.6*var(--u));border-color:#fff2c8;}
.sgd-cut .t{display:flex;flex-direction:column;gap:.15em;}
.sgd-cut.b .t{align-items:flex-end;}
.sgd-cut .t small{font:700 calc(1.1*var(--u))/1 ${SANS};letter-spacing:.3em;color:rgba(255,255,255,.85);text-shadow:0 1px 2px #000;}
.sgd-cut .t b{font:900 calc(4.4*var(--u))/1 ${KAI};letter-spacing:.1em;color:#fff;text-shadow:0 .06em 0 rgba(0,0,0,.7),0 0 .3em rgba(255,255,255,.4);white-space:nowrap;}
.sgd-win{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;pointer-events:none;}
.sgd-win .w{position:relative;display:flex;flex-direction:column;align-items:center;}
.sgd-win .ink{position:absolute;left:50%;top:44%;width:calc(26*var(--u));height:calc(26*var(--u));transform:translate(-50%,-50%);animation:sgd-ink .5s ease-out both;}
.sgd-win .g{position:relative;font:900 calc(17*var(--u))/1 ${KAI};color:#c8221a;filter:url(#sgd-rough);
  text-shadow:0 0 0 #000,.02em .03em 0 rgba(60,0,0,.65);animation:sgd-stamp .45s cubic-bezier(.2,1.4,.4,1) both;}
.sgd-win.lose .g{color:#3a3a46;}
.sgd-win .n{position:relative;margin-top:-.2em;font:700 calc(2.4*var(--u))/1.2 ${KAI};letter-spacing:.2em;color:#fff4dc;text-shadow:0 3px 0 #000,0 0 12px rgba(0,0,0,.8);animation:sgd-fadeup .5s .3s both;}
.sgd-win .s{position:relative;font:600 calc(1.1*var(--u))/1.4 ${SANS};color:#e8dfca;text-shadow:0 1px 3px #000;animation:sgd-fadeup .5s .45s both;}
.sgd-win .h{position:relative;margin-top:.8em;font:600 calc(.95*var(--u))/1 ${SANS};color:rgba(245,237,219,.7);animation:sgd-fadeup .5s .9s both;}
.sgd-dmg{position:absolute;left:0;top:0;font:900 italic calc(1.6*var(--u))/1 ${SANS};color:#fff;text-shadow:0 2px 0 #000,0 0 6px rgba(0,0,0,.8);white-space:nowrap;will-change:transform,opacity;}
.sgd-dmg.g{color:#9cc8ff;font-size:calc(1.2*var(--u));}
.sgd-dmg.big{color:#ffd040;font-size:calc(2.1*var(--u));}
.sgd-dmg.lbl{font:700 calc(1.4*var(--u))/1 ${KAI};font-style:normal;color:#ff9a6a;letter-spacing:.1em;}
.sgd-flash{position:fixed;inset:-20px;background:#fff;opacity:0;pointer-events:none;}
.sgd-fade{position:fixed;inset:0;background:#07060a;opacity:0;pointer-events:auto;z-index:70;transition:opacity .3s ease;}
.sgd-fade.off{pointer-events:none;}
html.sgd-on .sg-screens>:not(.sgd){visibility:hidden!important;}
.sgd.sgd-compact .sgd-name b{font-size:calc(1.55*var(--u));}
.sgd.sgd-compact .sgd-legend{font-size:max(11px,calc(.85*var(--u)));padding:.3em .7em;gap:calc(.25*var(--u)) calc(.6*var(--u));bottom:calc(var(--sab) + .4*var(--u));}
.sgd.sgd-compact .sgd-skip{top:calc(var(--sat) + 6.8*var(--u));}
@media (max-height:540px){
  .sgd-card{padding:calc(.9*var(--u)) calc(1.3*var(--u));width:min(94vw,calc(64*var(--u)));}
  .sgd-card h2{margin-bottom:.3em;}
  .sgd-card .vs{margin-bottom:.4em;}
  .sgd-ann{top:30%;}
}
@keyframes sgd-shake{0%,100%{transform:translateX(0)}20%{transform:translateX(-5px)}40%{transform:translateX(5px)}60%{transform:translateX(-3px)}80%{transform:translateX(2px)}}
@keyframes sgd-low{from{filter:brightness(1)}to{filter:brightness(1.35)}}
@keyframes sgd-sweep{from{background-position:0 0}to{background-position:200% 0}}
@keyframes sgd-blink{from{opacity:.55}to{opacity:1}}
@keyframes sgd-pulse{from{transform:scale(1)}to{transform:scale(1.08)}}
@keyframes sgd-pop{0%{transform:scale(1.6)}100%{transform:scale(1)}}
@keyframes sgd-glow{from{box-shadow:0 0 .3em rgba(255,200,80,.5)}to{box-shadow:0 0 1.2em rgba(255,200,80,1)}}
@keyframes sgd-ann{0%{opacity:0;transform:scale(2.2);filter:blur(6px)}18%{opacity:1;transform:scale(1);filter:blur(0)}80%{opacity:1;transform:scale(1.04)}100%{opacity:0;transform:scale(1.1)}}
@keyframes sgd-cardin{from{opacity:0;transform:translate(-50%,-44%) scale(.96)}to{opacity:1;transform:translate(-50%,-50%) scale(1)}}
@keyframes sgd-vsl{from{opacity:0;transform:translateX(-40vw)}to{opacity:1;transform:none}}
@keyframes sgd-vsr{from{opacity:0;transform:translateX(40vw)}to{opacity:1;transform:none}}
@keyframes sgd-vsx{from{opacity:0;transform:scale(3)}to{opacity:1;transform:scale(1)}}
@keyframes sgd-cutl{0%{opacity:0;transform:translate(-30%,-50%) rotate(-4deg)}18%{opacity:1;transform:translate(0,-50%) rotate(-4deg)}82%{opacity:1;transform:translate(3%,-50%) rotate(-4deg)}100%{opacity:0;transform:translate(25%,-50%) rotate(-4deg)}}
@keyframes sgd-cutr{0%{opacity:0;transform:translate(30%,-50%) rotate(-4deg)}18%{opacity:1;transform:translate(0,-50%) rotate(-4deg)}82%{opacity:1;transform:translate(-3%,-50%) rotate(-4deg)}100%{opacity:0;transform:translate(-25%,-50%) rotate(-4deg)}}
@keyframes sgd-ink{from{opacity:0;transform:translate(-50%,-50%) scale(.3) rotate(-20deg)}to{opacity:1;transform:translate(-50%,-50%) scale(1) rotate(0)}}
@keyframes sgd-stamp{from{opacity:0;transform:scale(2.4)}to{opacity:1;transform:scale(1)}}
@keyframes sgd-fadeup{from{opacity:0;transform:translateY(.6em)}to{opacity:1;transform:none}}
`;
    (document.head || document.documentElement).appendChild(s);
    // 笔触粗糙边缘滤镜（“胜”字）
    if (!document.getElementById('sgd-svgdefs')) {
      const d = document.createElement('div');
      d.id = 'sgd-svgdefs';
      d.style.cssText = 'position:absolute;width:0;height:0;overflow:hidden;';
      d.innerHTML = '<svg width="0" height="0" aria-hidden="true"><filter id="sgd-rough" x="-10%" y="-10%" width="120%" height="120%"><feTurbulence type="fractalNoise" baseFrequency="0.045" numOctaves="2" seed="7" result="n"/><feDisplacementMap in="SourceGraphic" in2="n" scale="10" xChannelSelector="R" yChannelSelector="G"/></filter></svg>';
      (document.body || document.documentElement).appendChild(d);
    }
  }

  // 墨迹（“胜”字背后）
  function inkSplash(color, seed) {
    const r = SG.SeededRandom(seed || 3);
    let d = '';
    const blob = (cx, cy, rad, n) => {
      let s = '';
      for (let i = 0; i <= n; i++) {
        const a = i / n * Math.PI * 2, rr = rad * (0.75 + r.nextDouble() * 0.45);
        s += (i ? 'L' : 'M') + (cx + Math.cos(a) * rr).toFixed(1) + ' ' + (cy + Math.sin(a) * rr).toFixed(1);
      }
      return s + 'Z';
    };
    d += blob(100, 100, 70, 26);
    for (let i = 0; i < 9; i++) { const a = r.nextDouble() * Math.PI * 2, dist = 78 + r.nextDouble() * 22; d += blob(100 + Math.cos(a) * dist, 100 + Math.sin(a) * dist, 4 + r.nextDouble() * 9, 8); }
    return '<svg viewBox="0 0 200 200" width="100%" height="100%"><path d="' + d + '" fill="' + color + '" opacity=".9" filter="url(#sgd-rough)"/></svg>';
  }

  function portraitEl(gen, color, size, mood, flip) {
    const wrap = document.createElement('div');
    wrap.className = 'sgd-port';
    wrap.style.setProperty('--fc', color);
    let el = null;
    if (SG.Portrait && typeof SG.Portrait.el === 'function') {
      try { el = SG.Portrait.el(gen, { size, color, frame: false, mood: mood || 'angry', flip: !!flip }); } catch (e) { el = null; }
    }
    if (!el) {
      el = document.createElement('div');
      el.className = 'sgd-medal';
      el.textContent = String((gen && gen.name) || '?').charAt(0);
    }
    wrap.appendChild(el);
    return wrap;
  }

  // ============================================================ 音效 --
  // 优先用 SG.Sfx 的现成音效；挥击风声与重击闷响用 WebAudio 即时合成
  let noiseBuf = null;
  function sfx(name, vol) { try { if (SG.Sfx && SG.Sfx.play) SG.Sfx.play(name, vol); } catch (e) { /* 无音频 */ } }
  function synth(kind, vol) {
    try {
      const S = SG.Sfx;
      if (!S || !S.soundOn) return;
      const ctx = S.context;
      if (!ctx || ctx.state !== 'running') return;
      if (!noiseBuf) {
        noiseBuf = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
        const d = noiseBuf.getChannelData(0), r = SG.SeededRandom(99);
        for (let i = 0; i < d.length; i++) d[i] = r.nextDouble() * 2 - 1;
      }
      const t = ctx.currentTime;
      const src = ctx.createBufferSource(); src.buffer = noiseBuf;
      const f = ctx.createBiquadFilter();
      const g = ctx.createGain();
      src.connect(f); f.connect(g); g.connect(ctx.destination);
      if (kind === 'twang') {
        // 弓弦：短促的拨弦声
        const o = ctx.createOscillator(), og = ctx.createGain();
        o.type = 'triangle'; o.frequency.setValueAtTime(240, t); o.frequency.exponentialRampToValueAtTime(150, t + 0.25);
        og.gain.setValueAtTime(0.0001, t); og.gain.exponentialRampToValueAtTime(0.35 * vol, t + 0.005); og.gain.exponentialRampToValueAtTime(0.0001, t + 0.3);
        o.connect(og); og.connect(ctx.destination); o.start(t); o.stop(t + 0.32);
        o.onended = () => { try { o.disconnect(); og.disconnect(); } catch (e) { /* 忽略 */ } };
        f.type = 'highpass'; f.frequency.value = 2500;
        g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.15 * vol, t + 0.004); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.06);
        src.start(t, 0.1, 0.08);
      } else if (kind === 'whoosh') {
        f.type = 'bandpass'; f.Q.value = 1.4;
        f.frequency.setValueAtTime(500, t); f.frequency.exponentialRampToValueAtTime(2600, t + 0.12); f.frequency.exponentialRampToValueAtTime(700, t + 0.22);
        g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.22 * vol, t + 0.05); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.24);
        src.start(t, Math.random() * 0.5, 0.26);
      } else {
        f.type = 'lowpass'; f.frequency.setValueAtTime(900, t); f.frequency.exponentialRampToValueAtTime(90, t + 0.35);
        g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.7 * vol, t + 0.01); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.45);
        src.start(t, 0, 0.5);
        const o = ctx.createOscillator(), og = ctx.createGain();
        o.type = 'sine'; o.frequency.setValueAtTime(110, t); o.frequency.exponentialRampToValueAtTime(38, t + 0.35);
        og.gain.setValueAtTime(0.5 * vol, t); og.gain.exponentialRampToValueAtTime(0.0001, t + 0.4);
        o.connect(og); og.connect(ctx.destination); o.start(t); o.stop(t + 0.42);
        o.onended = () => { try { o.disconnect(); og.disconnect(); } catch (e) { /* 忽略 */ } };
      }
      src.onended = () => { try { src.disconnect(); f.disconnect(); g.disconnect(); } catch (e) { /* 忽略 */ } };
    } catch (e) { /* 无音频 */ }
  }

  // ============================================================ 输入 --
  const KEYMAP = { ArrowLeft: 'left', KeyA: 'left', ArrowRight: 'right', KeyD: 'right', ArrowUp: 'up', KeyW: 'up', Space: 'up',
    KeyJ: 'light', KeyK: 'heavy', KeyL: 'guard', ArrowDown: 'guard', KeyS: 'guard', KeyI: 'special' };
  class PlayerInput {
    constructor() {
      this.held = { x: 0, up: false, light: false, heavy: false, guard: false, special: false, dash: 0 };
      this.taps = { light: 0, up: 0, special: 0, heavy: 0, dir: 0 };
      this.keys = new Set();
      this.touch = {};
      this.anyKey = null;
      this.enabled = true;
      this._down = e => this.onDown(e);
      this._up = e => this.onUp(e);
      this._blur = () => { this.keys.clear(); this.touch = {}; this.recompute(); };
      window.addEventListener('keydown', this._down, true);
      window.addEventListener('keyup', this._up, true);
      window.addEventListener('blur', this._blur);
    }
    onDown(e) {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      const t = e.target;
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT')) return;
      const k = KEYMAP[e.code];
      if (this.anyKey && !e.repeat) { const f = this.anyKey; this.anyKey = null; f(); e.preventDefault(); e.stopPropagation(); return; }
      if (!k) return;
      e.preventDefault(); e.stopPropagation();
      if (!this.enabled) return;
      if (!e.repeat) this.press(k);
      this.keys.add(k);
      this.recompute();
    }
    onUp(e) {
      const k = KEYMAP[e.code];
      if (!k) return;
      e.preventDefault();
      this.keys.delete(k);
      this.recompute();
    }
    press(k) {
      if (k === 'left') this.taps.dir = -1;
      else if (k === 'right') this.taps.dir = 1;
      else if (this.taps[k] !== undefined) this.taps[k]++;
    }
    setTouch(k, on) {
      if (!!this.touch[k] === !!on) return;
      this.touch[k] = !!on;
      if (on && this.enabled) this.press(k);
      this.recompute();
    }
    recompute() {
      const K = this.keys, T = this.touch, h = this.held;
      const L = K.has('left') || T.left, R = K.has('right') || T.right;
      h.x = this.enabled ? (R ? 1 : 0) - (L ? 1 : 0) : 0;
      for (const k of ['up', 'light', 'heavy', 'guard', 'special']) h[k] = this.enabled && !!(K.has(k) || T[k]);
    }
    // 启用 / 停用：停用期间的按键与触摸一律丢弃，启用时清空残留的点击与按住状态
    setEnabled(on) {
      this.enabled = !!on;
      this.keys.clear(); this.touch = {};
      for (const k in this.taps) this.taps[k] = 0;
      this.recompute();
    }
    dispose() {
      window.removeEventListener('keydown', this._down, true);
      window.removeEventListener('keyup', this._up, true);
      window.removeEventListener('blur', this._blur);
    }
  }

  // 触摸按键：左下 ◀ ▶ ▲（同一指可滑动切换），右下 轻击 / 重击 / 格挡 / 绝技；每个触点独立（多点触控）
  function buildTouch(root, input) {
    const h = (cls, html, parent) => { const e = document.createElement('div'); e.className = cls; if (html) e.innerHTML = html; parent.appendChild(e); return e; };
    const pad = h('sgd-pad', null, root);
    const L = h('sgd-tb dir', '◀', pad), R = h('sgd-tb dir', '▶', pad), U = h('sgd-tb dir', '▲', pad);
    L.style.cssText = 'left:0;bottom:0;'; R.style.cssText = 'left:calc(var(--b)*1.22);bottom:0;'; U.style.cssText = 'left:calc(var(--b)*.61);bottom:calc(var(--b)*1.04);';
    const zones = new Map();
    const zoneAt = (x, y) => {
      const r = pad.getBoundingClientRect();
      if (x < r.left - 40 || x > r.right + 40 || y < r.top - 50 || y > r.bottom + 40) return null;
      const b = r.height / 2.05;
      if (y < r.bottom - b * 1.02) return 'up';
      return x < r.left + r.width / 2 ? 'left' : 'right';
    };
    const sync = () => {
      const on = { left: false, right: false, up: false };
      for (const z of zones.values()) if (z) on[z] = true;
      input.setTouch('left', on.left); input.setTouch('right', on.right); input.setTouch('up', on.up);
      L.classList.toggle('on', on.left); R.classList.toggle('on', on.right); U.classList.toggle('on', on.up);
    };
    const opt = { passive: false };
    pad.addEventListener('pointerdown', e => { e.preventDefault(); try { pad.setPointerCapture(e.pointerId); } catch (err) { /* 忽略 */ } zones.set(e.pointerId, zoneAt(e.clientX, e.clientY)); sync(); }, opt);
    pad.addEventListener('pointermove', e => { if (!zones.has(e.pointerId)) return; e.preventDefault(); const z = zoneAt(e.clientX, e.clientY); if (z !== zones.get(e.pointerId)) { zones.set(e.pointerId, z); sync(); } }, opt);
    const end = e => { if (zones.delete(e.pointerId)) sync(); };
    for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) pad.addEventListener(ev, end);
    const btns = h('sgd-btns', null, root);
    const mk = (cls, label, key, css) => {
      const b = h('sgd-tb ' + cls, label, btns);
      b.style.cssText = css;
      const ids = new Set();
      const set = () => { input.setTouch(key, ids.size > 0); b.classList.toggle('on', ids.size > 0); };
      b.addEventListener('pointerdown', e => { e.preventDefault(); try { b.setPointerCapture(e.pointerId); } catch (err) { /* 忽略 */ } ids.add(e.pointerId); set(); }, opt);
      const up = e => { if (ids.delete(e.pointerId)) set(); };
      for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) b.addEventListener(ev, up);
      return b;
    };
    const B = {
      light: mk('big', '轻击', 'light', 'right:0;bottom:0;'),
      heavy: mk('', '重击', 'heavy', 'right:calc(var(--b)*1.32);bottom:calc(var(--b)*.02);'),
      guard: mk('', '格挡', 'guard', 'right:calc(var(--b)*.1);bottom:calc(var(--b)*1.3);'),
      special: mk('sp', '绝技', 'special', 'right:calc(var(--b)*1.36);bottom:calc(var(--b)*1.16);'),
    };
    for (const el of [pad, btns]) for (const ev of ['touchstart', 'touchmove', 'touchend', 'contextmenu']) el.addEventListener(ev, e => { if (e.cancelable) e.preventDefault(); }, opt);
    return { pad, btns, B };
  }

  // ============================================================ 单挑画面 --
  const TMP = {};
  class DuelScreen {
    constructor(opts) {
      this.opts = opts;
      const A = opts.a || {}, B = opts.b || {};
      this.genA = A.gen || { name: '甲', war: 70 };
      this.genB = B.gen || { name: '乙', war: 70 };
      this.colA = SG.UI && SG.UI.cssColor ? SG.UI.cssColor(A.color || '#3a78c8') : (A.color || '#3a78c8');
      this.colB = SG.UI && SG.UI.cssColor ? SG.UI.cssColor(B.color || '#c8382c') : (B.color || '#c8382c');
      // 双方势力色过于接近时，应战者改用对比色，免得分不清敌我
      try {
        const ca = new THREE.Color(this.colA), cb = new THREE.Color(this.colB);
        const dist = (x, y) => Math.hypot(x.r - y.r, x.g - y.g, x.b - y.b);
        if (dist(ca, cb) < 0.22) {
          const red = new THREE.Color('#c8382c'), blue = new THREE.Color('#3a78c8');
          this.colB = dist(ca, red) > dist(ca, blue) ? '#c8382c' : '#3a78c8';
        }
      } catch (e) { /* 保持原色 */ }
      this.lookA = lookOf(this.genA, A.culture);
      this.lookB = lookOf(this.genB, B.culture);
      const sp = opts.special || {};
      this.spA = specialOf(this.genA, sp.a, this.lookA);
      this.spB = specialOf(this.genB, sp.b, this.lookB);
      this.player = opts.playerSide === 0 || opts.playerSide === 1 ? opts.playerSide : null;
      // 触摸屏笔记本：既有触摸也有精确指针（鼠标 / 触控板）→ 同时给键盘说明与触摸按键
      this.hybrid = false;
      try { this.hybrid = !!SG.isTouch && !!window.matchMedia && window.matchMedia('(any-pointer: fine)').matches && window.matchMedia('(any-hover: hover)').matches; } catch (e) { this.hybrid = false; }
      this.kind = terrainKind(opts.terrain);
      this.sim = new Sim(this.genA, this.genB, this.lookA, this.lookB);
      this.slow = 1; this.slowT = 0;
      this.acc = 0; this.realT = 0;
      this.shake = 0; this.zoom = null;
      this.skipped = false;
      this.compact = false;
      this.dmgs = [];
      this.disposed = false;
      this.introCam = 1;
      this.trailOn = [0, 0];
    }

    async build() {
      const sim = this.sim;
      // 操作者
      // 玩家一方：键盘 / 触摸；自动测试（DuelGame.auto）时仍显示操作界面，但交给电脑
      this.input = this.player !== null ? new PlayerInput() : null;
      if (this.input) this.input.setEnabled(false);       // 开场 / 操作说明期间不接受出招（开战时才启用）
      for (const f of sim.f) f.ctrl = (this.player === f.idx && !DuelGame.auto) ? this.input : new AI(f);
      // 场景
      const scene = new THREE.Scene();
      this.scene = scene;
      const cam = new THREE.PerspectiveCamera(30, (SG.Gfx.width || 16) / (SG.Gfx.height || 9), 0.3, 400);
      this.camera = cam;
      this.camPos = new THREE.Vector3(0, 1.8, 10);
      this.camLook = new THREE.Vector3(0, 1, 0);
      await SG.frame();
      this.arena = new Arena(scene, this.kind, this.colA, this.colB, String(this.genA.name || '甲').charAt(0), String(this.genB.name || '乙').charAt(0), hashStr(this.genA.name + this.genB.name));
      const pal = this.arena.pal;
      const hemi = new THREE.HemisphereLight(C(pal.hemiS), C(pal.hemiG), Math.PI * pal.hemiI);
      scene.add(hemi);
      const key = new THREE.DirectionalLight(C(pal.key), Math.PI * pal.keyI);
      key.castShadow = true;
      key.shadow.mapSize.set(1024, 1024);
      key.shadow.bias = -0.0008; key.shadow.normalBias = 0.03;
      const sc = key.shadow.camera;
      sc.left = -7; sc.right = 7; sc.top = 6; sc.bottom = -4; sc.near = 1; sc.far = 40;
      scene.add(key); scene.add(key.target);
      const rim = new THREE.DirectionalLight(C(pal.rim), Math.PI * pal.rimI);
      scene.add(rim); scene.add(rim.target);
      this.lights = { hemi, key, rim };
      await SG.frame();
      this.war = [new Warrior(sim.f[0], this.colA), new Warrior(sim.f[1], this.colB)];
      for (const w of this.war) scene.add(w.root);
      // 特效
      this.sparks = new Particles(420, THREE.AdditiveBlending, 1.0, 12);
      this.dust = new Particles(240, THREE.NormalBlending, 1.6, 11);
      this.motes = new Particles(110, THREE.AdditiveBlending, 1.4, 10);
      scene.add(this.sparks.points); scene.add(this.dust.points); scene.add(this.motes.points);
      this.trails = [];
      for (let i = 0; i < 4; i++) { const t = new Trail(12); this.trails.push(t); scene.add(t.mesh); }
      this.stars = [];
      for (let i = 0; i < 6; i++) {
        const m = new THREE.SpriteMaterial({ map: starTexture(), color: 0xffffff, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, depthTest: false, fog: false });
        const s = new THREE.Sprite(m); s.visible = false; s.renderOrder = 30; scene.add(s);
        this.stars.push({ s, t: 1, life: 0.15, size: 1 });
      }
      this.rings = [];
      const rg = new THREE.PlaneGeometry(1, 1); rg.rotateX(-Math.PI / 2);
      this.ringGeo = rg;
      for (let i = 0; i < 3; i++) {
        const m = new THREE.MeshBasicMaterial({ map: SG.Gfx.ringTexture, color: 0xffffff, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, fog: false });
        const o = new THREE.Mesh(rg, m); o.visible = false; o.renderOrder = 9; scene.add(o);
        this.rings.push({ o, t: 1, life: 0.5, size: 4 });
      }
      // 飞箭（弓将绝技）：小对象池，位置每帧取自 sim.shots
      this.arrowGeo = [buildArrow(false).geo(), buildArrow(true).geo()];
      this.arrows = [];
      for (let i = 0; i < 6; i++) {
        const o = new THREE.Mesh(this.arrowGeo[0], envMat()); o.visible = false; o.castShadow = true; scene.add(o);
        this.arrows.push({ o, id: 0 });
      }
      this.moteR = SG.SeededRandom(5);
      for (let i = 0; i < 70; i++) this.spawnMote(true);
      this.fxR = SG.SeededRandom(17);
      // 屏幕对象
      this.screen = { scene, camera: cam, update: dt => this.update(dt), resize: (w, h) => this.resize(w, h) };
      this.buildHud();
    }

    // ------------------------------------------------------------ HUD --
    buildHud() {
      const layer = SG.UI && SG.UI.layers && SG.UI.layers.screens;
      const root = document.createElement('div');
      root.className = 'sgd sgd-nohud' + (layer ? '' : ' sgd-fixed') + (SG.isTouch ? ' sgd-touch' : '');
      (layer || document.body).appendChild(root);
      this.root = root;
      // 单挑期间隐藏同层的其他画面 HUD（战场顶栏、部队卡、按钮等）
      document.documentElement.classList.add('sgd-on');
      const h = (cls, html, parent) => { const e = document.createElement('div'); if (cls) e.className = cls; if (html != null) e.innerHTML = html; (parent || root).appendChild(e); return e; };
      h('sgd-vig');
      const top = h('sgd-top');
      this.hud = [];
      const sideEl = (i) => {
        const gen = i === 0 ? this.genA : this.genB, col = i === 0 ? this.colA : this.colB;
        const s = h('sgd-side ' + (i === 0 ? 'a' : 'b'), null, top);
        s.style.setProperty('--fc', col);
        const port = portraitEl(gen, col, 96, 'angry', i === 1);
        s.appendChild(port);
        const info = h('sgd-info', null, s);
        const isP = this.player === i;
        h('sgd-name', '<b>' + SG.esc(gen.name || '') + '</b><span class="sgd-war"><i>武力</i>' + warOf(gen) + '</span>' + (isP ? '<span class="sgd-tag">' + (DuelGame.auto ? '自动' : '玩家') + '</span>' : ''), info);
        const hp = h('sgd-hp', '<i class="trail"></i><i class="fill"></i>', info);
        for (let k = 1; k < 4; k++) { const t = document.createElement('i'); t.className = 'tick'; t.style.left = (k * 25) + '%'; t.style.width = '1px'; hp.appendChild(t); }
        const rr = h('sgd-ragerow', null, info);
        const rage = h('sgd-rage', '<i></i>', rr);
        const lbl = h('sgd-ragelbl', '怒', rr);
        return { s, hp, rage, lbl, trail: 1, trailDelay: 0, lastHp: 100, full: false };
      };
      this.hud[0] = sideEl(0);
      const tm = h('sgd-timer', '<b>60</b><small>单挑</small>', top);
      this.hud[1] = sideEl(1);
      top.insertBefore(tm, this.hud[1].s);
      this.timerEl = tm;
      this.timerB = tm.querySelector('b');
      this.combo = [h('sgd-combo a', '<b>2</b><span>连击</span>'), h('sgd-combo b', '<b>2</b><span>连击</span>')];
      this.ann = h('sgd-ann');
      this.dmgLayer = h(null);
      this.dmgLayer.style.cssText = 'position:absolute;inset:0;pointer-events:none;';
      this.flashEl = h('sgd-flash');
      if (this.player !== null && (!SG.isTouch || this.hybrid)) {
        this.legend = h('sgd-legend', '<span><b class="sgd-k">←</b><b class="sgd-k">→</b>移动</span><span><b class="sgd-k">↑</b>跳</span><span><b class="sgd-k">J</b>轻击</span>' +
          '<span><b class="sgd-k">K</b>重击·按住蓄力</span><span><b class="sgd-k">L</b>格挡</span><span><b class="sgd-k r">I</b>绝技</span>');
        this.legend.style.opacity = '0';
      }
      if (this.player !== null && SG.isTouch && this.input) {
        this.touch = buildTouch(root, this.input);
        for (const el of [this.touch.pad, this.touch.btns]) { el.style.opacity = '0'; el.style.visibility = 'hidden'; }
        if (!this.hybrid) {
          this.touchHint = h('sgd-touchhint', '双击 ◀ ▶ 冲刺 · 按住「重击」蓄力');
          this.touchHint.style.opacity = '0';
        }
      }
      if (this.player === null) {
        this.watch = h('sgd-watch', '观　战');
        this.skipBtn = h('sgd-skip', '跳过 <span style="letter-spacing:-.2em">▶▶</span>');
        this.skipBtn.addEventListener('click', () => this.doSkip());
        this.skipBtn.addEventListener('pointerdown', e => e.stopPropagation());
      }
      this.resize(SG.Gfx.width, SG.Gfx.height);
    }
    showControls(on) {
      const v = on ? '1' : '0';
      if (this.legend) this.legend.style.opacity = v;
      if (this.touch) for (const el of [this.touch.pad, this.touch.btns]) { el.style.opacity = v; el.style.visibility = on ? 'visible' : 'hidden'; }
      if (this.touchHint) this.touchHint.style.opacity = on ? '1' : '0';
    }
    announce(text, cls, sub, sec) {
      const d = document.createElement('div');
      if (cls) d.className = cls;
      d.innerHTML = SG.esc(text) + (sub ? '<small>' + SG.esc(sub) + '</small>' : '');
      const dur = sec || 1.0;
      d.style.animationDuration = dur + 's';
      this.ann.innerHTML = '';
      this.ann.appendChild(d);
      setTimeout(() => { if (d.parentNode) d.parentNode.removeChild(d); }, dur * 1000 + 50);
    }
    floatDmg(x, y, text, cls) {
      const el = document.createElement('div');
      el.className = 'sgd-dmg' + (cls ? ' ' + cls : '');
      el.textContent = text;
      this.dmgLayer.appendChild(el);
      this.dmgs.push({ el, p: new THREE.Vector3(x, y, 0.3), t: 0, vx: (this.fxR.nextDouble() - 0.5) * 0.6 });
      if (this.dmgs.length > 10) { const o = this.dmgs.shift(); o.el.remove(); }
    }
    screenFlash(a) {
      const f = this.flashEl;
      f.style.transition = 'none'; f.style.opacity = String(a);
      void f.offsetWidth;
      f.style.transition = 'opacity .28s ease-out'; f.style.opacity = '0';
    }

    resize(w, h) {
      this.rootRect = null;
      this.compact = h < 540 || w < 700;
      if (this.root) this.root.classList.toggle('sgd-compact', this.compact);
      if (this.camera) { this.camera.aspect = Math.max(0.2, w / Math.max(1, h)); this.camera.updateProjectionMatrix(); }
    }

    // ------------------------------------------------------------ 特效 --
    spawnMote(init) {
      const r = this.moteR;
      const c = C(this.kind === 'forest' ? '#e8ffb0' : this.kind === 'castle' ? '#ffb070' : '#fff0c8');
      const life = 6 + r.nextDouble() * 6;
      this.motes.add({ x: (r.nextDouble() * 2 - 1) * 12, y: 0.2 + r.nextDouble() * 4, z: -3 + r.nextDouble() * 5, vx: 0.15 + r.nextDouble() * 0.2, vy: (r.nextDouble() - 0.3) * 0.12, vz: 0,
        r: c.r, g: c.g, b: c.b, a: 0.35 + r.nextDouble() * 0.35, size: 0.035 + r.nextDouble() * 0.04, life, ac: 2, fadeIn: 0.2 });
      if (init) this.motes.parts[this.motes.parts.length - 1].age = r.nextDouble() * life;
    }
    burst(x, y, z, color, n, speed, opts) {
      const r = this.fxR, c = C(color);
      opts = opts || {};
      for (let i = 0; i < n; i++) {
        const a = r.nextDouble() * Math.PI * 2, e = (r.nextDouble() - 0.35) * Math.PI * 0.8;
        const v = speed * (0.4 + r.nextDouble() * 0.8);
        this.sparks.add({ x, y, z, vx: Math.cos(a) * Math.cos(e) * v + (opts.dir || 0) * speed * 0.6, vy: Math.sin(e) * v + speed * 0.25, vz: Math.sin(a) * Math.cos(e) * v * 0.5,
          r: c.r, g: c.g, b: c.b, a: 1, size: (opts.size || 0.09) * (0.6 + r.nextDouble() * 0.8), life: (opts.life || 0.35) * (0.6 + r.nextDouble() * 0.7), grav: opts.grav === undefined ? 9 : opts.grav, drag: 2.2, ac: 1.5, floor: true });
      }
    }
    puff(x, n, scale, dir) {
      const r = this.fxR, pal = this.arena.pal;
      const c = mixC(pal.lane, '#e8dcc8', 0.35);
      for (let i = 0; i < n; i++) {
        const v = (0.6 + r.nextDouble() * 1.4) * scale;
        this.dust.add({ x: x + (r.nextDouble() - 0.5) * 0.4, y: 0.08 + r.nextDouble() * 0.15, z: (r.nextDouble() - 0.5) * 0.6, vx: (dir || (r.nextDouble() < 0.5 ? -1 : 1)) * v * (0.4 + r.nextDouble()), vy: 0.3 + r.nextDouble() * 0.7 * scale, vz: (r.nextDouble() - 0.5) * v,
          r: c.r, g: c.g, b: c.b, a: 0.45, size: (0.35 + r.nextDouble() * 0.4) * scale, grow: 2.2, life: 0.7 + r.nextDouble() * 0.6, drag: 2.5, grav: -0.2, ac: 1.2 });
      }
    }
    star(x, y, color, size) {
      const s = this.stars.find(o => o.t >= 1) || this.stars[0];
      s.s.position.set(x, y, 0.35); s.s.material.color.set(color); s.t = 0; s.size = size; s.s.visible = true;
      s.s.material.rotation = this.fxR.nextDouble() * 0.8 - 0.4;
    }
    ring(x, color, size, life) {
      const g = this.rings.find(o => o.t >= 1) || this.rings[0];
      g.o.position.set(x, 0.06, 0); g.o.material.color.set(color); g.t = 0; g.size = size; g.life = life || 0.5; g.o.visible = true;
    }

    // 处理逻辑事件 → 画面、声音、HUD
    events() {
      const E = this.sim.events;
      if (!E.length) return;
      const sim = this.sim;
      for (const e of E) {
        switch (e.type) {
          case 'hit': {
            const att = sim.f[e.att], def = sim.f[e.def];
            const sp = e.move === 'special';
            const spc = (att.idx === 0 ? this.spA : this.spB).color || (att.idx === 0 ? this.colA : this.colB);
            const col = e.guarded ? '#bfe0ff' : sp ? spc : e.big ? '#ffc040' : '#fff0b0';
            const n = e.guarded ? 10 : Math.round(10 + e.dmg * 2.2);
            this.burst(e.x, e.y, 0.2, col, n, e.guarded ? 3.5 : 5 + e.dmg * 0.25, { dir: att.face, size: e.big ? 0.12 : 0.09 });
            if (!e.guarded) this.burst(e.x, e.y, 0.2, '#ffffff', 6, 2.5, { dir: att.face, size: 0.14, life: 0.18, grav: 0 });
            this.star(e.x, e.y, e.guarded ? '#a8d0ff' : sp ? spc : e.big ? '#ffd27a' : '#ffffff', (e.guarded ? 0.9 : 1.1) + Math.min(1.6, e.dmg * 0.09));
            this.shake = Math.max(this.shake, e.guarded ? 0.04 : 0.05 + e.dmg * 0.012 + (e.big ? 0.06 : 0));
            if (!e.guarded) this.war[def.idx].flashHit(e.big ? '#ffd0a0' : '#ffffff', e.big ? 0.95 : 0.8);
            if (e.guarded) { sfx('duel', 0.8); this.floatDmg(e.x, e.y + 0.3, e.dmg > 0 ? String(e.dmg) : '格', 'g'); if (e.parry) this.floatDmg(def.x, 2.4, '格开', 'lbl'); }
            else {
              sfx('hit', Math.min(1, 0.55 + e.dmg * 0.03));
              if (e.big) synth('thud', 0.5);
              this.floatDmg(e.x, e.y + 0.3, String(e.dmg), e.big ? 'big' : '');
              if (e.counter) this.floatDmg(e.x, e.y + 0.75, '破绽', 'lbl');
            }
            if (e.breaks) { this.floatDmg(def.x, 2.4, '破防', 'lbl'); sfx('rock', 0.6); }
            if (e.big && !e.guarded) { this.ring(def.x, col, 3.2, 0.45); this.puff(def.x, 6, 0.8, att.face); }
            if (sp && e.finisher) { this.screenFlash(0.5); this.ring(def.x, spc, 6, 0.7); }
            const H = this.hud[def.idx];
            H.trailDelay = 0.45;
            H.s.classList.remove('hit'); void H.s.offsetWidth; H.s.classList.add('hit');
            if (e.combo >= 2) {
              const cb = this.combo[att.idx];
              cb.querySelector('b').textContent = e.combo;
              cb.classList.add('on'); cb.classList.remove('pop'); void cb.offsetWidth; cb.classList.add('pop');
              cb.hideAt = this.realT + 1.3;
            }
            if (this.player === def.idx && !e.guarded) { try { if (navigator.vibrate) navigator.vibrate(e.big ? 30 : 12); } catch (err) { /* 忽略 */ } }
            break;
          }
          case 'swing': {
            const f = sim.f[e.who];
            this.trailOn[e.who] = this.realT + (e.move === 'heavy' || e.move === 'special' ? 0.2 : 0.14);
            synth('whoosh', e.move === 'heavy' ? 1 : 0.65);
            if (e.move === 'heavy' && e.charge > 0.6) this.puff(f.x, 4, 0.6, f.face);
            break;
          }
          case 'arrow': {
            const f = sim.f[e.who];
            synth('twang', e.fin ? 1 : 0.7);
            synth('whoosh', e.fin ? 0.9 : 0.5);
            if (e.fin) { this.shake = Math.max(this.shake, 0.06); this.burst(f.x + f.face * 0.7, 1.4, 0.1, '#fff0c0', 12, 3, { dir: f.face, grav: 0, life: 0.25 }); }
            break;
          }
          case 'jump': this.puff(sim.f[e.who].x, 4, 0.5); break;
          case 'land': this.puff(e.x, 5, 0.6); break;
          case 'dash': this.puff(sim.f[e.who].x, 5, 0.6, -sim.f[e.who].face); synth('whoosh', 0.4); break;
          case 'down': this.puff(e.x, 14, 1.2); this.shake = Math.max(this.shake, 0.12); synth('thud', 0.8); this.ring(e.x, '#e8d8b8', 3.4, 0.5); break;
          case 'chargeFull': { const f = sim.f[e.who]; this.burst(f.x + f.face * 0.3, 1.4, 0.2, '#ffe08a', 16, 2.5, { grav: -2, life: 0.5 }); sfx('coin', 0.5); break; }
          case 'guardbreak': this.shake = Math.max(this.shake, 0.12); break;
          case 'special': {
            const f = sim.f[e.who], spx = f.idx === 0 ? this.spA : this.spB;
            this.cutIn(f.idx, spx);
            this.zoom = { who: f.idx, until: this.realT + SPECIAL_FREEZE / Math.max(0.1, DuelGame.speed) + 0.15 };
            this.ring(f.x, spx.color || '#ffd040', 5, 0.8);
            this.burst(f.x, 1.2, 0, spx.color || '#ffd040', 40, 4, { grav: -3, life: 0.7, size: 0.1 });
            sfx('horn', 0.6);
            break;
          }
          case 'ko': {
            this.slow = 0.22; this.slowT = 1.25;
            this.screenFlash(0.75);
            this.shake = 0.25;
            this.zoom = { who: e.who, until: this.realT + 1.6, ko: true };
            this.announce('击　破', 'red', null, 1.6);
            synth('thud', 1);
            sfx('rock', 0.8);
            break;
          }
          case 'timeup': this.announce('时间到', 'small', null, 1.4); sfx('horn', 0.5); break;
          default: break;
        }
      }
      E.length = 0;
    }

    cutIn(idx, sp) {
      const gen = idx === 0 ? this.genA : this.genB, col = idx === 0 ? this.colA : this.colB;
      const el = document.createElement('div');
      el.className = 'sgd-cut ' + (idx === 0 ? 'a' : 'b');
      el.style.setProperty('--sc', sp.color || col);
      el.appendChild(portraitEl(gen, '#fff2c8', 120, 'angry', idx === 1));
      const t = document.createElement('div');
      t.className = 't';
      t.innerHTML = '<small>' + SG.esc(gen.name) + ' · 绝技</small><b>' + SG.esc(sp.name) + '</b>';
      el.appendChild(t);
      el.style.animationDuration = Math.max(0.5, 1.0 / Math.max(0.5, DuelGame.speed)) + 's';
      this.root.appendChild(el);
      setTimeout(() => el.remove(), 1100);
    }

    // ------------------------------------------------------------ 每帧 --
    update(dt) {
      if (this.disposed) return;
      const t0 = performance.now();
      const sim = this.sim;
      this.realT += dt;
      if (this.slowT > 0) { this.slowT -= dt; if (this.slowT <= 0) this.slow = 1; }
      const ts = DuelGame.speed * this.slow;
      this.acc += dt * ts;
      let n = 0;
      while (this.acc >= DT && n < 24) { sim.step(DT); this.acc -= DT; n++; }
      if (n >= 24) this.acc = 0;
      this.events();
      const adt = dt * ts;
      const frozen = sim.hitstop > 0;
      for (let i = 0; i < 2; i++) {
        const w = this.war[i];
        w.update(frozen || sim.freeze[i] > 0 ? 0 : adt, frozen);
        // 刀光
        const f = sim.f[i];
        const on = this.realT < this.trailOn[i] || (f.state === 'special' && f.t > SPECIAL_FREEZE && !f.bow);
        const trs = [this.trails[i * 2], this.trails[i * 2 + 1]];
        const spc = (i === 0 ? this.spA : this.spB).color;
        const tc = f.state === 'special' ? (spc || '#ffd060') : f.state === 'heavy' ? (f.charge > 0.8 ? '#ffb040' : '#ffe0a0') : '#dfe8ff';
        for (let k = 0; k < (w.wpn2 ? 2 : 1); k++) {
          trs[k].on = on; trs[k].color.set(tc);
          if (!frozen) trs[k].push(w.baseW[k], w.tipW[k]);
          trs[k].update(adt);
        }
        // 蓄力 / 怒气满：上升的光点
        if (!frozen && (f.state === 'charge' || f.rage >= 100 || (f.state === 'special' && f.t < SPECIAL_FREEZE))) {
          const spx = i === 0 ? this.spA : this.spB;
          const rate = f.state === 'charge' ? 40 * (0.3 + f.charge) : f.state === 'special' ? 90 : 10;
          const cnt = this.fxR.nextDouble() < rate * adt % 1 ? Math.ceil(rate * adt) : Math.floor(rate * adt);
          const c = C(f.state === 'charge' ? '#ffd27a' : spx.color || '#ffb040');
          for (let j = 0; j < cnt; j++) this.sparks.add({ x: f.x + (this.fxR.nextDouble() - 0.5) * 0.7, y: 0.1 + this.fxR.nextDouble() * 1.4, z: (this.fxR.nextDouble() - 0.5) * 0.5, vx: 0, vy: 1.2 + this.fxR.nextDouble() * 1.6, vz: 0,
            r: c.r, g: c.g, b: c.b, a: 0.9, size: 0.06 + this.fxR.nextDouble() * 0.05, life: 0.5 + this.fxR.nextDouble() * 0.4, ac: 1.5 });
        }
      }
      this.updateArrows(adt);
      // 环境（城头火把）
      this.arena.update(this.realT);
      for (const p of this.arena.torches) {
        const k = Math.floor(dt * 34 + this.fxR.nextDouble());
        for (let j = 0; j < k; j++) this.sparks.add({ x: p.x + (this.fxR.nextDouble() - 0.5) * 0.15, y: p.y, z: p.z, vx: (this.fxR.nextDouble() - 0.5) * 0.3, vy: 0.8 + this.fxR.nextDouble() * 0.8, vz: 0,
          r: 1, g: 0.55 + this.fxR.nextDouble() * 0.25, b: 0.2, a: 0.85, size: 0.22 + this.fxR.nextDouble() * 0.12, grow: -0.6, life: 0.45 + this.fxR.nextDouble() * 0.3, ac: 1.2 });
      }
      if (this.motes.parts.length < 70) this.spawnMote(false);
      const scale = (SG.Gfx.height || 720) / (2 * Math.tan(this.camera.fov * M.deg2rad / 2)) * (SG.Gfx.renderer ? SG.Gfx.renderer.getPixelRatio() : 1);
      this.sparks.update(adt, scale); this.dust.update(adt, scale); this.motes.update(dt, scale);
      for (const s of this.stars) {
        if (s.t >= 1) { s.s.visible = false; continue; }
        s.t = Math.min(1, s.t + adt / s.life);
        const k = Math.sin(s.t * Math.PI);
        s.s.scale.setScalar(s.size * (0.4 + s.t * 0.9));
        s.s.material.opacity = k;
      }
      for (const g of this.rings) {
        if (g.t >= 1) { g.o.visible = false; continue; }
        g.t = Math.min(1, g.t + adt / g.life);
        g.o.scale.setScalar(g.size * (0.2 + g.t));
        g.o.material.opacity = (1 - g.t) * 0.9;
      }
      this.updateCamera(dt);
      this.updateHud(dt);
      this.cost = performance.now() - t0;
    }

    updateArrows(dt) {
      const S = this.sim.shots;
      for (const a of this.arrows) {
        const sh = a.id ? S.find(x => x.id === a.id) : null;
        if (!sh) { a.id = 0; a.o.visible = false; }
      }
      for (const sh of S) {
        let a = this.arrows.find(x => x.id === sh.id);
        if (!a) {
          a = this.arrows.find(x => !x.id);
          if (!a) continue;
          a.id = sh.id; a.o.geometry = this.arrowGeo[sh.fin ? 1 : 0];
          a.o.scale.setScalar(sh.fin ? 1.9 : 1.45);
        }
        a.o.visible = true;
        const d = Math.sign(sh.vx) || 1;
        a.o.position.set(sh.x - d * 0.95 * a.o.scale.x, sh.y, 0.08);
        a.o.rotation.set(0, d > 0 ? 0 : Math.PI, 0);
        // 箭尾流光
        if (dt > 0) {
          const spx = this.sim.f[sh.owner].idx === 0 ? this.spA : this.spB;
          const c = C(sh.fin ? (spx.color || '#ffd040') : '#fff4d8');
          for (let j = 0; j < (sh.fin ? 3 : 2); j++) {
            this.sparks.add({ x: sh.x - d * (0.2 + this.fxR.nextDouble() * 0.6), y: sh.y + (this.fxR.nextDouble() - 0.5) * 0.05, z: 0.08, vx: -d * 0.6, vy: 0, vz: 0,
              r: c.r, g: c.g, b: c.b, a: 0.8, size: (sh.fin ? 0.13 : 0.07) * (0.6 + this.fxR.nextDouble() * 0.6), life: 0.22, ac: 1.2 });
          }
        }
      }
    }

    updateCamera(dt) {
      const sim = this.sim, a = sim.f[0], b = sim.f[1], cam = this.camera;
      const aspect = cam.aspect, tanV = Math.tan(cam.fov * M.deg2rad / 2), tanH = tanV * aspect;
      let mid = (a.x + b.x) / 2;
      const sep = Math.abs(a.x - b.x);
      const frac = this.touch ? 0.54 : this.compact ? 0.62 : 0.7;
      let dist = M.clamp((sep + 2.6) / frac / (2 * tanH), 6.2, 15);
      dist = Math.max(dist, (this.touch ? 3.0 : 2.9) / (2 * tanV * (this.touch ? 0.64 : 0.72)));
      let hc = 1.55 + dist * 0.045;
      let ground = this.touch ? 0.42 : 0.46;     // 地平线（脚下）在屏幕中的位置：NDC −ground
      // 特写：绝技发动 / KO
      const z = this.zoom;
      if (z && this.realT < z.until) {
        const f = sim.f[z.who];
        mid = M.lerp(mid, f.x, z.ko ? 0.55 : 0.75);
        dist *= z.ko ? 0.72 : 0.62; hc -= z.ko ? 0.35 : 0.1; ground = z.ko ? 0.3 : 0.38;
      } else this.zoom = null;
      // 开场：从侧后方推近
      if (this.introCam < 1) {
        const k = this.introCam, e = 1 - Math.pow(1 - k, 3);
        mid = M.lerp(mid - 4.5, mid, e); dist = M.lerp(dist * 0.55, dist, e); hc = M.lerp(0.9, hc, e);
      }
      const halfW = dist * tanH;
      const lim = Math.max(0, ARENA + 1.6 - halfW);
      mid = M.clamp(mid, -lim, lim);
      const alpha = Math.atan2(hc, dist), theta = alpha - Math.atan(ground * tanV);
      const k = 1 - Math.exp(-dt * (this.zoom ? 10 : 5));
      const want = TMP.v2 || (TMP.v2 = new THREE.Vector3());
      want.set(mid, hc, dist);
      if (this.snapCam) { this.camPos.copy(want); this.snapCam = false; } else this.camPos.lerp(want, k);
      const lookY = this.camPos.y - this.camPos.z * Math.tan(theta);
      this.camLook.set(this.camPos.x, lookY, 0);
      // 震屏
      this.shake = Math.max(0, this.shake - dt * 0.6) * Math.exp(-dt * 6);
      const s = this.shake, r = this.fxR;
      cam.position.set(this.camPos.x + (r.nextDouble() - 0.5) * s * 2, this.camPos.y + (r.nextDouble() - 0.5) * s * 2, this.camPos.z);
      cam.lookAt(this.camLook.x + (r.nextDouble() - 0.5) * s, this.camLook.y + (r.nextDouble() - 0.5) * s, 0);
      // 阴影与光照跟随镜头
      const L = this.lights;
      L.key.target.position.set(this.camPos.x, 0, 0);
      L.key.position.set(this.camPos.x - 6, 11, 8);
      L.rim.target.position.set(this.camPos.x, 1, 0);
      L.rim.position.set(this.camPos.x + 5, 5, -9);
      L.key.target.updateMatrixWorld(); L.rim.target.updateMatrixWorld();
    }

    updateHud(dt) {
      const sim = this.sim;
      for (let i = 0; i < 2; i++) {
        const f = sim.f[i], H = this.hud[i];
        const v = f.hp / 100;
        if (H.trail < v) H.trail = v;
        if (H.trailDelay > 0) H.trailDelay -= dt;
        else if (H.trail > v) H.trail = Math.max(v, H.trail - dt * 0.7);
        if (H.v !== v) { H.hp.querySelector('.fill').style.setProperty('--v', v.toFixed(3)); H.v = v; H.hp.classList.toggle('low', v <= 0.3); }
        if (H.tv !== H.trail) { H.hp.querySelector('.trail').style.setProperty('--v', H.trail.toFixed(3)); H.tv = H.trail; }
        const rv = Math.round(f.rage) / 100;
        if (H.rv !== rv) { H.rage.querySelector('i').style.setProperty('--v', rv.toFixed(2)); H.rv = rv; }
        const full = f.rage >= 100;
        if (full !== H.full) {
          H.full = full;
          H.rage.classList.toggle('full', full);
          H.lbl.classList.toggle('full', full);
          const sp = i === 0 ? this.spA : this.spB;
          const isP = this.player === i && !DuelGame.auto;
          H.lbl.innerHTML = full ? (this.compact ? '绝技' : '绝技「' + SG.esc(sp.name) + '」') + (isP && !SG.isTouch ? ' <b class="sgd-k r" style="font-size:.8em">I</b>' : '') : '怒';
          if (full && sim.running && !sim.result) this.floatDmg(f.x, 2.5, '怒气满', 'lbl');
          if (isP && this.touch) this.touch.B.special.classList.toggle('ready', full);
        }
        const cb = this.combo[i];
        if (cb.hideAt && this.realT > cb.hideAt) { cb.classList.remove('on'); cb.hideAt = 0; }
      }
      const left = Math.max(0, Math.ceil(sim.limit - sim.time));
      if (left !== this.lastLeft) { this.lastLeft = left; this.timerB.textContent = String(left); this.timerEl.classList.toggle('low', left <= 10); }
      // 伤害飘字
      const W = SG.Gfx.width || 1, Hh = SG.Gfx.height || 1;
      const v = TMP.v3 || (TMP.v3 = new THREE.Vector3());
      for (let i = this.dmgs.length - 1; i >= 0; i--) {
        const d = this.dmgs[i];
        d.t += dt;
        if (d.t > 0.9) { d.el.remove(); this.dmgs.splice(i, 1); continue; }
        v.copy(d.p); v.x += d.vx * d.t; v.y += d.t * 0.9;
        v.project(this.camera);
        const x = (v.x * 0.5 + 0.5) * W, y = (-v.y * 0.5 + 0.5) * Hh;
        const rr = this.root.getBoundingClientRect ? this.rootRect || (this.rootRect = this.root.getBoundingClientRect()) : { left: 0, top: 0 };
        const sc = d.t < 0.12 ? 1.5 - d.t * 4 : 1;
        d.el.style.transform = 'translate(' + (x - rr.left).toFixed(1) + 'px,' + (y - rr.top).toFixed(1) + 'px) translate(-50%,-50%) scale(' + sc.toFixed(2) + ')';
        d.el.style.opacity = String(M.clamp01((0.9 - d.t) * 3));
      }
    }

    // ------------------------------------------------------------ 流程 --
    wait(sec) {
      const until = this.realT + sec / Math.max(0.05, DuelGame.speed);
      return new Promise(res => {
        const tick = () => { if (this.disposed || this.realT >= until || this.skipped) res(); else requestAnimationFrame(tick); };
        tick();
      });
    }
    until(fn) {
      return new Promise(res => {
        const tick = () => { if (this.disposed || fn()) res(); else requestAnimationFrame(tick); };
        tick();
      });
    }
    doSkip() {
      if (this.skipped || this.player !== null) return;
      this.skipped = true;
      const sim = this.sim;
      sim.events.length = 0;
      if (!sim.result) sim.runToEnd();
      this.skipKind = true;
    }

    async intro() {
      // 开场亮相：镜头推近、两将挥舞兵器、VS 头像
      this.introCam = 0;
      this.snapCam = true;
      for (const w of this.war) w.intro = 0;
      const vs = document.createElement('div');
      vs.className = 'sgd-vs';
      const side = (gen, col, i) => {
        const p = document.createElement('div');
        p.className = 'p ' + (i ? 'b' : 'a');
        p.appendChild(portraitEl(gen, col, 200, 'angry', i === 1));
        p.insertAdjacentHTML('beforeend', '<div class="n">' + SG.esc(gen.name) + '</div><div class="w">武力 ' + warOf(gen) + ' · ' + SG.esc((i ? this.lookB : this.lookA).weaponName) + '</div>');
        return p;
      };
      vs.appendChild(side(this.genA, this.colA, 0));
      vs.insertAdjacentHTML('beforeend', '<div class="x">VS</div>');
      vs.appendChild(side(this.genB, this.colB, 1));
      this.root.appendChild(vs);
      sfx('horn', 0.55);
      const t0 = this.realT, dur = 1.5 / Math.max(0.05, DuelGame.speed);
      await this.until(() => {
        this.introCam = M.clamp01((this.realT - t0) / dur);
        return this.introCam >= 1 || this.skipped;
      });
      this.introCam = 1;
      vs.style.transition = 'opacity .35s ease'; vs.style.opacity = '0';
      setTimeout(() => vs.remove(), 400);
      for (const w of this.war) w.intro = -1;
      this.root.classList.remove('sgd-nohud');
    }

    async controlsCard() {
      const sp = this.player === 0 ? this.spA : this.spB;
      const me = this.player === 0 ? this.genA : this.genB, foe = this.player === 0 ? this.genB : this.genA;
      const card = document.createElement('div');
      card.className = 'sgd-card';
      const vs = '<div class="vs">攻击力与伤害取决于武力：<b>' + SG.esc(me.name) + ' ' + warOf(me) + '</b> 对 <b>' + SG.esc(foe.name) + ' ' + warOf(foe) + '</b></div>';
      const k = s => '<b class="sgd-k">' + s + '</b>';
      if (SG.isTouch && !this.hybrid) {
        card.innerHTML = '<h2>操作方法</h2>' + vs + '<div class="sgd-tl"><div>' +
          '<div class="row"><span class="sgd-chip">◀</span><span class="sgd-chip">▶</span>移动（双击冲刺）</div>' +
          '<div class="row"><span class="sgd-chip">▲</span>跳跃（空中可轻击）</div></div><div>' +
          '<div class="row"><span class="sgd-chip big">轻击</span>连按三连击</div>' +
          '<div class="row"><span class="sgd-chip">重击</span>按住蓄力，满蓄破防</div>' +
          '<div class="row"><span class="sgd-chip">格挡</span>按住减伤八成</div>' +
          '<div class="row"><span class="sgd-chip">绝技</span>怒气满时「' + SG.esc(sp.name) + '」</div></div></div>' +
          '<div class="go">轻触开始</div>';
      } else {
        card.innerHTML = '<h2>操作方法</h2>' + vs + '<div class="sgd-rows">' +
          '<div class="keys">' + k('←') + k('→') + '<span style="opacity:.6">/</span>' + k('A') + k('D') + '</div><div class="desc">移动<small>双击冲刺</small></div>' +
          '<div class="keys">' + k('↑') + k('W') + k('空格') + '</div><div class="desc">跳跃<small>空中可轻击</small></div>' +
          '<div class="keys">' + k('J') + '</div><div class="desc">轻击<small>连按三连击</small></div>' +
          '<div class="keys">' + k('K') + '</div><div class="desc">重击<small>按住蓄力，满蓄破防</small></div>' +
          '<div class="keys">' + k('L') + '<span style="opacity:.6">/</span>' + k('↓') + '</div><div class="desc">格挡<small>按住减伤八成</small></div>' +
          '<div class="keys"><b class="sgd-k r">I</b></div><div class="desc">绝技「' + SG.esc(sp.name) + '」<small>怒气满时</small></div>' +
          '</div>' + (this.hybrid ? '<div class="vs">触摸屏：左下 ◀ ▶ ▲ 移动，右下按钮出招</div>' : '') + '<div class="go">' + (this.hybrid ? '按任意键或轻触开始' : '按任意键开始') + '</div>';
      }
      this.root.appendChild(card);
      await new Promise(res => {
        let done = false;
        const go = () => { if (done) return; done = true; res(); };
        if (this.input) this.input.anyKey = go;
        // 轻触画面任意处即开始（不只卡片本身）
        this._cardTap = e => { if (e.target && e.target.closest && e.target.closest('.sgd-skip')) return; e.preventDefault(); go(); };
        window.addEventListener('pointerdown', this._cardTap, true);
        this.cardGo = go;
        if (DuelGame.auto) setTimeout(go, 700 / Math.max(0.1, DuelGame.speed));
      });
      if (this.input) this.input.anyKey = null;
      if (this._cardTap) { window.removeEventListener('pointerdown', this._cardTap, true); this._cardTap = null; }
      card.style.transition = 'opacity .2s ease'; card.style.opacity = '0';
      setTimeout(() => card.remove(), 250);
    }

    async outro() {
      const sim = this.sim, r = sim.result;
      if (!this.skipped) {
        // 等胜者摆出胜利姿势
        await this.until(() => this.skipped || (sim.endT > (r.kind === 'ko' ? 1.6 : 1.1)));
      }
      const win = r.winner, gen = win === 0 ? this.genA : this.genB, loser = win === 0 ? this.genB : this.genA;
      const lose = this.player !== null && !DuelGame.auto && this.player !== win;
      const el = document.createElement('div');
      el.className = 'sgd-win' + (lose ? ' lose' : '');
      const perfect = r.kind === 'ko' && sim.f[win].hp >= 100;
      const sub = this.skipped ? '（观战跳过）' : r.kind === 'ko' ? (perfect ? '完胜 · 毫发无伤击破' : '击破') + loser.name : '时间到 · 体力占优';
      el.innerHTML = '<div class="w"><div class="ink">' + inkSplash(lose ? 'rgba(20,20,28,.85)' : 'rgba(10,8,8,.82)', hashStr(gen.name)) + '</div>' +
        '<div class="g">' + (lose ? '败' : '胜') + '</div><div class="n">' + SG.esc(gen.name) + (lose ? ' 获胜' : '') + '</div><div class="s">' + SG.esc(sub) + '</div><div class="h">' + (SG.isTouch ? '轻触继续' : '按任意键继续') + '</div></div>';
      this.root.appendChild(el);
      this.showControls(false);
      sfx(lose ? 'lose' : 'win', 0.6);
      await this.wait(0.8);
      await new Promise(res => {
        let done = false;
        const go = () => { if (done) return; done = true; res(); };
        if (this.input) this.input.anyKey = go;
        else { this._anyKey = e => { if (!e.repeat) go(); }; window.addEventListener('keydown', this._anyKey, true); }
        this._tap = () => go();
        window.addEventListener('pointerdown', this._tap, true);
        const t = setTimeout(go, (this.player === null || DuelGame.auto ? 1600 : 3200) / Math.max(0.1, DuelGame.speed));
        this._outroTimer = t;
      });
      if (this._anyKey) window.removeEventListener('keydown', this._anyKey, true);
      if (this._tap) window.removeEventListener('pointerdown', this._tap, true);
      clearTimeout(this._outroTimer);
    }

    dispose() {
      if (this.disposed) return;
      this.disposed = true;
      try { SG.Gfx.popScreen(this.screen); } catch (e) { /* 忽略 */ }
      if (this.input) this.input.dispose();
      for (const w of this.war || []) w.dispose();
      if (this.arena) this.arena.dispose();
      for (const p of [this.sparks, this.dust, this.motes]) if (p) p.dispose();
      for (const t of this.trails || []) t.dispose();
      for (const s of this.stars || []) s.s.material.dispose();
      for (const g of this.rings || []) g.o.material.dispose();
      if (this.ringGeo) this.ringGeo.dispose();
      for (const g of this.arrowGeo || []) g.dispose();
      if (this.lights) { this.lights.key.dispose(); this.lights.rim.dispose(); this.lights.hemi.dispose(); }
      if (this.scene) { this.scene.clear(); this.scene.fog = null; this.scene.background = null; }
      if (this.root && this.root.parentNode) this.root.parentNode.removeChild(this.root);
      document.documentElement.classList.remove('sgd-on');
      this.dmgs.length = 0;
    }

    async run() {
      injectStyle();
      // 让出焦点：避免空格 / 回车误触战场上仍带焦点的按钮
      try { const ae = document.activeElement; if (ae && ae !== document.body && typeof ae.blur === 'function') ae.blur(); } catch (e) { /* 忽略 */ }
      const fade = document.createElement('div');
      fade.className = 'sgd-fade';
      (document.getElementById('ui') || document.body).appendChild(fade);
      void fade.offsetWidth;
      fade.style.opacity = '1';
      await SG.sleep(260);
      const rig = SG.Game && SG.Game.rig;
      const rigWas = rig ? rig.inputEnabled : null;
      const musicWas = SG.Sfx ? SG.Sfx.musicKind : null;
      let ownLoop = false;
      try {
        await this.build();
        if (rig) rig.inputEnabled = false;
        SG.Gfx.pushScreen(this.screen);
        // 主循环未运行（独立测试页）时自己驱动渲染
        if (!(SG.Game && SG.Game._running)) {
          ownLoop = true;
          let last = performance.now();
          const loop = now => {
            if (this.disposed) return;
            requestAnimationFrame(loop);
            const dt = Math.min(0.1, Math.max(0, (now - last) / 1000));
            last = now;
            try { this.update(dt); SG.Gfx.render(); } catch (e) { if (!this._err) { this._err = true; console.error(e); } }
          };
          requestAnimationFrame(loop);
        }
        try { if (SG.Sfx && SG.Sfx.music) SG.Sfx.music('duel'); } catch (e) { /* 无音频 */ }
        await SG.frame();
        fade.style.opacity = '0';
        fade.classList.add('off');
        await this.intro();
        if (this.player !== null && !this.skipped) await this.controlsCard();
        if (!this.skipped) {
          this.announce('开　战', null, null, 0.9);
          sfx('duel', 0.9);
          await this.wait(0.45);
          this.showControls(true);
          if (this.input) this.input.setEnabled(true);
          this.sim.running = true;
        }
        if (this.player === null && !this.skipped) {
          this._skipKey = e => { if (!e.repeat && !e.ctrlKey && !e.metaKey && !e.altKey) this.doSkip(); };
          this._skipTap = e => { if (e.target && e.target.closest && e.target.closest('.sgd-skip')) return; this.doSkip(); };
          window.addEventListener('keydown', this._skipKey, true);
          this.root.addEventListener('pointerdown', this._skipTap);
          this.root.style.pointerEvents = 'auto';
        }
        await this.until(() => this.skipped || !!this.sim.result);
        if (this._skipKey) { window.removeEventListener('keydown', this._skipKey, true); this.root.style.pointerEvents = ''; }
        this.showControls(false);
        await this.outro();
        fade.classList.remove('off');
        fade.style.opacity = '1';
        await SG.sleep(320);
      } finally {
        this.dispose();
        if (rig && rigWas !== null) rig.inputEnabled = rigWas;
        try { if (SG.Sfx && SG.Sfx.music && musicWas && musicWas !== 'duel') SG.Sfx.music(musicWas); } catch (e) { /* 无音频 */ }
        if (ownLoop) { try { SG.Gfx.render(); } catch (e) { /* 忽略 */ } }
        fade.style.opacity = '0';
        setTimeout(() => fade.remove(), 350);
      }
      return this.sim.outcome(this.skipped ? 'skip' : null);
    }
  }

  // 公开：开始一场单挑
  async function play(opts) {
    opts = opts || {};
    if (!SG.Gfx.renderer) SG.Gfx.init(document.getElementById('app') || document.body);
    while (DuelGame.active) await DuelGame.active.done;
    const d = new DuelScreen(opts);
    let resolve;
    d.done = new Promise(r => { resolve = r; });
    DuelGame.active = d;
    try {
      return await d.run();
    } finally {
      DuelGame.active = null;
      resolve();
    }
  }

  // ============================================================ 战斗接线 --
  // 供 battle-controller.js 的 doDuel 使用（集成阶段接线，见文件头“接线”一节）
  function sideColorOf(s) {
    const BC = SG.BattleController;
    try { if (BC && typeof BC.sideColor === 'function') return BC.sideColor(s); } catch (e) { /* 回退 */ }
    return s === 0 ? '#73bfff' : '#ff806b';
  }
  function factionColorOf(u) {
    try {
      const f = SG.G && SG.G.factions && u.gen.faction >= 0 ? SG.G.factions[u.gen.faction] : null;
      if (f && f.color) return f.color;
    } catch (e) { /* 回退 */ }
    return sideColorOf(u.side);
  }
  // 是否进入格斗画面：玩家参与、未委任、未关闭（DuelGame.enabled）。电脑对电脑 / 委任时照旧 Mdl.duel()
  function shouldPlay(bc, a, b) {
    if (!DuelGame.enabled || !bc || !bc.M || !a || !b) return false;
    const ps = bc.playerSide;
    if (ps !== 0 && ps !== 1) return false;
    if (bc.autoPlayer) return false;
    return a.side === ps || b.side === ps;
  }
  // 完整的单挑流程（替代 doDuel 中的 Mdl.duel + 旧单挑对话框）：
  // 应战判定（Mdl.duelAccepts）→ 挑战台词 → 格斗画面 → Mdl.duelFinish → 刷新部队 → 败者溃散
  async function perform(bc, a, b, extra) {
    const Mdl = bc.M, V = bc.V, UI = SG.UI;
    const refresh = () => { if (V && typeof V.refresh === 'function') for (const x of Mdl.units) { try { V.refresh(x); } catch (e) { console.error(e); } } };
    if (!Mdl.duelAccepts(a, b)) {
      if (UI && UI.say) await UI.say(b.gen.name + '：哼，匹夫之勇，不足与战！\n（' + b.gen.name + '拒绝单挑，其部士气下降）', b.gen.name, sideColorOf(b.side));
      refresh();
      if (typeof bc.updateHud === 'function') bc.updateHud();
      return { accepted: false, rounds: [], winner: 0 };
    }
    if (UI && UI.say) await UI.say(a.gen.name + '：' + b.gen.name + '，可敢与我一战？', a.gen.name, sideColorOf(a.side));
    let terrain = 0;
    try { terrain = Mdl.map[b.x][b.y]; } catch (e) { terrain = 0; }
    const spOf = g => { try { return SG.Specials && typeof SG.Specials.of === 'function' ? SG.Specials.of(g) : null; } catch (e) { return null; } };
    // 文化：武将自带 culture（世界剧本由 model.js 派生），否则查 SG.WorldData（lookOf 内亦有兜底）
    const culOf = g => { if (g.culture) return g.culture; try { return SG.WorldData && SG.WorldData.cultureOfGeneral ? SG.WorldData.cultureOfGeneral(g.name) : undefined; } catch (e) { return undefined; } };
    let res;
    try {
      res = await play(Object.assign({
        a: { gen: a.gen, side: a.side, color: factionColorOf(a), culture: culOf(a.gen) },
        b: { gen: b.gen, side: b.side, color: factionColorOf(b), culture: culOf(b.gen) },
        playerSide: a.side === bc.playerSide ? 0 : 1,
        terrain,
        special: { a: spOf(a.gen), b: spOf(b.gen) },
      }, extra || {}));
    } catch (e) {
      // 格斗画面出错：记录错误，改用无画面模拟（同一套规则）分出胜负，保证士气与溃散照常结算
      console.error('单挑画面出错，改为直接结算：', e);
      try { res = simulate(a.gen, b.gen); } catch (e2) { res = scriptedDuel(a.gen, b.gen); }
    }
    const r = Mdl.duelFinish(a, b, res.winner === 0, res.log);
    const loser = res.winner === 0 ? b : a;
    // 败者的名牌留到溃散动画时再消失（与旧流程一致）
    const lv = V && typeof V.vis === 'function' ? V.vis(loser) : null;
    if (lv) lv.holdLabel = true;
    refresh();
    if (lv) lv.holdLabel = false;
    if (typeof bc.updateHud === 'function') bc.updateHud();
    if (UI && UI.toast) UI.toast((res.winner === 0 ? a : b).gen.name + '于单挑中击败了' + loser.gen.name + '！', 2);
    if (V && typeof V.rout === 'function') await V.rout(loser);
    return Object.assign(r, { kind: res.kind, hpA: res.hpA, hpB: res.hpB });
  }

  // 最后的兜底：旧版的体力交换（只用于格斗逻辑本身也出错时）
  function scriptedDuel(ga, gb) {
    let hpA = 100, hpB = 100;
    const log = [];
    for (let i = 0; i < 30 && hpA > 0 && hpB > 0; i++) {
      const who = rnd() < warOf(ga) / (warOf(ga) + warOf(gb)) ? 0 : 1;
      const dmg = Math.round((6 + Math.floor(rnd() * 11)) * (0.6 + warOf(who === 0 ? ga : gb) / 120));
      if (who === 0) hpB = Math.max(0, hpB - dmg); else hpA = Math.max(0, hpA - dmg);
      log.push({ who, dmg, hpA, hpB, move: 'light1', guarded: false, t: i });
    }
    return { winner: hpA >= hpB ? 0 : 1, kind: 'time', hpA, hpB, log };
  }

  const DuelGame = {
    auto: false,
    speed: 1,
    enabled: true,
    active: null,
    shouldPlay,
    perform,
    simulate,
    lookOf,
    damage,
    MOVES, WEAPONS, LOOKS,
    _Sim: Sim, _AI: AI,
  };
  try {
    if (typeof location !== 'undefined' && location.search && new URLSearchParams(location.search).get('duelauto') === '1') DuelGame.auto = true;
  } catch (e) { /* 无 location */ }
  DuelGame.play = play;
  SG.DuelGame = DuelGame;
})();
