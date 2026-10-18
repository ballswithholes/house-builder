'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 单挑格斗（DESIGN-V2 §4E，第三版格开与手感：DESIGN-V3 §3）

   单挑 = 一场横版 2.5D 格斗：独立的三维场景（SG.Gfx.pushScreen），两名 js/figure.js 的高精度武将（平滑蒙皮、
   按头像外貌生成的面部、层叠衣甲、披风、名将专属兵器；未载入 figure.js 时退回旧的低多边形模型），
   由本文件的时钟与状态机驱动 figure.js 的动作片段，键盘 / 多点触控操作，攻击力与伤害取决于 武力。
   场地同为平滑、细致的第三版画风（地形着色器、平滑植被、实例化的两军士兵）。

   公开接口
   --------
   const res = await SG.DuelGame.play({
     a: { gen, side: 0, color, culture },        // a = 挑战者（画面左侧）
     b: { gen, side: 1, color, culture },        // b = 应战者（画面右侧）
     playerSide: 0 | 1 | null,                   // 玩家操作哪一方（0 = a，1 = b）；null = 电脑对电脑（观战，可跳过）
     terrain,                                    // SG.Terrain 值：平原 / 森林 / 丘陵 / 山地 / 河川 / 城墙 / 城门 / 本城
     special: { a: SG.Specials?.of(a.gen), b: … }// 可选：绝技名称与配色 { name, color }
   })
   // → { winner: 0 | 1（0 = a 胜）, kind: 'ko' | 'time' | 'skip', hpA, hpB, log: [{ who, dmg, hpA, hpB, move, guarded, t }],
   //     stats: [{ hits, parries, parryTries, counters, whiffs, parryBy: { stance, guard, hitstun } }, …]（a、b 各一项：可供成长系统计经验） }
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
   格开（第三版，与按住格挡分开）：轻按 U，有效窗口 0.15–0.22 秒（武力越高越宽）；来招落在窗口内 → 对手被弹开
   硬直 0.6 秒（重击 0.7），己方得到 0.7 秒反击窗口：此间第一击 ×1.5、不可格挡也不可格开（慢镜头、火花、震屏、
   金铁交鸣）。窗口内没有来招 → 0.4 秒落空破绽（挨打算「破绽」×1.2）。冷却 2.5 秒（从按下算起），明显长于攻击与格挡。
   可在站立 / 走动 / 格挡 / 挡下一击的硬直中格开；被连段打中时只能看破第三段：挨了第二段后须踩准节奏，按在第三段
   打到前 0.6 × 窗口（约 0.09–0.13 秒）之内才格开，按早了白费（第三段照样打中，提示「过早」）；没等到第三段则
   硬直结束时短暂破绽。绝技与箭矢不可格开。按住格挡的「硬挡」（武力远胜者挨连段时有机会挡下后续一击）保留，但不算格开。
   重击松开后有清晰的起手（刃口闪光 + 「铮」声）：轻点约 0.15 秒（看不及，只能预判），满蓄约 0.27 秒（举刀一顿再劈下，
   人的反应来得及——满蓄破防的重击可以看准格开）；电脑看重击与看其他来招的反应时间相同。绝技起手（定格亮相与突进）无敌。
   限时 60 秒，KO 或时间到时体力高者胜（相同则挑战者胜，与 BattleModel.duel 一致）。
   电脑的反应时间、格挡率、连段与绝技使用随其武力提升；武力 40 以上的电脑会看破一味连按的对手
   （预判举防、后撤诱空、格开连段、抓连段挥空后的破绽），武力越高越准。电脑也会格开：看破连段第三段、挡下一击后
   格开连按者的下一击、看清重击起手当场格开（武力越高越常用、越准），看见对手摆出格开架势就停手等其落空并抓破绽，
   格开得手即反击；对惯于格开第三段的对手，会只打两段诱其落空。

   操作：←/→ 或 A/D 移动（向前双击冲刺、向后双击后撤步）、↑/W/空格 跳、J 轻击（连按三连击）、K 重击（按住蓄力，
   满蓄破防）、L 格挡（↓/S 亦可）、U 格开、I 绝技（怒气满）。触摸：左下 ◀ ▶ ▲，右下 轻击 / 重击 / 格挡 / 格开
  （带冷却环）/ 绝技，支持多点触控。

   第三版接入 js/figure.js（FigWarrior）：出招的 起手 / 判定 / 收招 映射到 figure.js 片段时间（判定开始 = 兵器尖端前伸最远的一刻；
   对手更近时取尖端刚越过对手中线的一刻，命中定格时刃口停在对手身上），状态切换按骨骼交叉淡化；兵器按 weaponInfo 实测的
   前伸距离缩放、出招前冲（随起手确定性地迈到位，短兵最多 0.9 米）补足，使刃口与规则判定距离一致（规则与平衡不变）。
   旧的 Warrior（低多边形、姿势向量 + IK）保留为未载入 figure.js 时的退路。

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
  // 格开（DESIGN-V3 §3）：轻按 U（触摸「格开」），有效窗口 0.15–0.22 秒（武力越高越宽）。来招落在窗口内 → 对手被弹开硬直，
  // 己方得到反击窗口：此间第一击 ×1.5、不可格挡也不可格开。窗口内没有来招 → 落空破绽。冷却明显长于攻击与格挡。
  const PARRY_CD = 2.5;            // 格开冷却（从按下算起）
  const PARRY_WHIFF = 0.4;         // 落空破绽
  const PARRY_STAGGER = 0.6;       // 被格开者的硬直（重击 +0.1）
  const PARRY_RIPOSTE = 0.16;      // 格开成功后的弹刀姿势（可随时出招取消）
  const COUNTER_WIN = 0.7;         // 反击窗口
  const COUNTER_K = 1.5;           // 反击第一击的伤害倍率
  function parryWindow(war) { return 0.15 + 0.07 * M.clamp((war - 30) / 70, 0, 1); }   // 武力 30 → 0.15 秒，100 → 0.22 秒
  // 受击硬直中预先按下的格开（看破连段第三段）：只认第三段打到前 0.6 × 窗口内的按键（约 0.09–0.13 秒）——
  // 须踩准第三段的节奏；按早了白费（照样挨打，冷却照算）
  const HS_PARRY_K = 0.6;
  // 重击松手后的起手随蓄力变长：0.13 秒（基准，按出招速度缩放）+ 举刀亮刃的一顿 0.15 × 蓄力（实际秒数，不随出招速度缩短）
  // → 轻点 ≈ 0.12–0.15 秒（看不及，只能预判）、满蓄 ≈ 0.25–0.28 秒。看得见（刃口闪光 +「铮」一声）也来得及反应：
  // 满蓄重击（破防）可以看准当场格开，双方同一规则
  const HEAVY_HOLD = 0.15;
  const HEAVY_PARRY_CAP = 0.45;    // 电脑看准格开满蓄重击的概率上限（武力 100 时）
  // 重击起手（招式时钟，即 × 出招速度 spd 后的基准秒）
  function heavySU(charge, spd) { return 0.13 + HEAVY_HOLD * M.clamp(charge || 0, 0, 1) * (spd || 1); }
  // 当前招式的起手时长（重击随蓄力）
  function suOf(f) { return f.state === 'heavy' ? heavySU(f.charge, f.spd) : MOVES[f.state].startup; }
  // 当前招式的总时长（基准秒）
  function totalOf(f) { const mv = MOVES[f.state]; return suOf(f) + mv.active + mv.recovery; }
  const BACKSTEP_T = 0.26;         // 后撤步（向后双击）

  // 招式时序（基准秒；出招时钟按 spd 缩放）。reachK × 兵器长度 = 判定距离
  const MOVES = {
    // hitH：受击部位（high 头 / mid 躯干），决定受击反应的方向
    light1: { kind: 'light', startup: 0.10, active: 0.07, recovery: 0.19, dmg: 2.4, stun: 0.30, push: 2.6, reachK: 0.92, next: 'light2', hitstop: 0.065, hmax: 0.85, hitH: 'mid' },
    light2: { kind: 'light', startup: 0.09, active: 0.07, recovery: 0.21, dmg: 2.4, stun: 0.32, push: 2.9, reachK: 0.95, next: 'light3', hitstop: 0.07, hmax: 0.85, hitH: 'high' },
    light3: { kind: 'light', startup: 0.15, active: 0.09, recovery: 0.44, dmg: 4, stun: 0.46, push: 5.0, reachK: 1.0, hitstop: 0.10, hmax: 1.45, big: true, lunge: 1.8, hitH: 'high' },
    // 重击：松开后有一段清晰的起手（刃口闪光预兆），可被看准格开
    heavy: { kind: 'heavy', startup: 0.13, active: 0.10, recovery: 0.40, dmg: 4.8, dmgCharge: 5, stun: 0.5, push: 4.6, reachK: 1.08, hitstop: 0.12, hmax: 1.5, big: true, lunge: 2.2, hitH: 'mid' },
    air: { kind: 'air', startup: 0.05, active: 0.20, recovery: 0.10, dmg: 3, stun: 0.36, push: 3.0, reachK: 0.88, hitstop: 0.08, hmax: 9, hitH: 'high' },
    // 绝技：定格起手 → 突进 → 三段斩 + 终结一击（倒地）
    special: { kind: 'special', startup: SPECIAL_FREEZE + 0.1, active: 0.62, recovery: 0.45, dmg: 4.5, finisher: 7, stun: 0.5, push: 1.6, reachK: 1.0, hitstop: 0.09, hmax: 9, big: true, hitH: 'mid',
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
    // 第三版：天竺（坎达剑、长枪、长盾）、阿克苏姆 / 库施（长枪、长皮盾）、毛里（标枪、小圆盾）
    india: { helm: 'goldcrown', skin: '#a8724a', hair: '#141012', bareArms: true, gold: true, weapons: ['sword', 'spear', 'sword'], shield: 'long', beard: 'none' },
    africa: { helm: 'kufiya', helmColor: '#ece6d8', skin: '#6c4026', hair: '#0c0a0a', torso: 'coat', coat: '#ece6d8', weapons: ['spear', 'spear', 'sword'], shield: 'hide', beard: 'short' },
    moor: { helm: 'kufiya', helmColor: '#e8dcc0', skin: '#b07e54', hair: '#141012', torso: 'coat', coat: '#e0d4bc', weapons: ['spear', 'sword'], shield: 'round', beard: 'short' },
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
    if (!CULTURE[cul]) cul = SG.cultureBase && CULTURE[SG.cultureBase(cul)] ? SG.cultureBase(cul) : 'han';   // 未知文化按 core.js 的回退表
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
  const NOHELD = { x: 0, up: false, light: false, heavy: false, guard: false, special: false, parry: false, dash: 0 };

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
      this.prev = { x: 0, up: false, light: false, heavy: false, special: false, guard: false, parry: false };
      // 格开
      this.parryW = parryWindow(this.war);   // 有效窗口（秒）
      this.parryReadyAt = 0;  // 冷却结束的时刻（sim.clock）
      this.counterUntil = 0;  // 反击窗口截止（sim.clock）：此间第一击 ×1.5、不可挡
      this.hsParryAt = -9;    // 受击硬直中按下格开的时刻（只对连段第三段有效）
      this.hsParrySeq = -1;
      this.hitH = 'mid';      // 最近一次受击的部位与方向（受击动画用）
      this.hitBack = false;
      this.buf = { light: -9, jump: -9, special: -9, heavy: -9, dash: -9, parry: -9 };
      this.lastTap = { dir: 0, t: -9 };
      this.stats = { swings: 0, hits: 0, guarded: 0, taken: 0, parryTries: 0, parries: 0, parried: 0, counters: 0, whiffs: 0, parryBy: { stance: 0, guard: 0, hitstun: 0 } };
      this.parryFrom = '';    // 进入格开架势前的状态（统计格开途径）
      this.bow = !!look.bow;  // 弓将（黄忠、夏侯渊）：绝技为连珠箭（远程），不突进
    }
    get grounded() { return this.y <= 0 && this.vy <= 0; }
    // 出招阶段：'startup' | 'active' | 'recovery' | null
    get phase() {
      const mv = MOVES[this.state];
      if (!mv) return null;
      const su = this.state === 'heavy' ? heavySU(this.charge, this.spd) : mv.startup;
      if (this.t < su) return 'startup';
      if (this.t < su + mv.active) return 'active';
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
    parryReady(f) { return this.clock >= f.parryReadyAt; }
    parryCd(f) { return Math.max(0, f.parryReadyAt - this.clock); }   // 剩余冷却（秒）
    countering(f) { return f.counterUntil > this.clock; }
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
        H.push({ clock: this.clock, state: f.state, x: f.x, y: f.y, seq: f.seq, t: f.t, phase: f.phase, atk: !!mv && f.phase !== 'recovery', charge: f.charge });
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
      if (edge('parry')) {
        f.buf.parry = now;
        // 受击硬直中按下格开：看破连段——只对随后的第三段有效（每段硬直只认第一次按下，同样进入冷却）
        if (f.state === 'hitstun' && f.hsParrySeq !== f.seq && this.parryReady(f) && f.grounded) {
          f.hsParrySeq = f.seq; f.hsParryAt = now; f.parryReadyAt = now + PARRY_CD; f.stats.parryTries++; f.buf.parry = -9;
          this.emit({ type: 'parryStart', who: f.idx, stunned: true });
        }
      }
      const xNew = h.x !== 0 && h.x !== p.x;
      if (xNew || (taps && taps.dir)) {
        const dir = xNew ? h.x : taps.dir;
        if (f.lastTap.dir === dir && now - f.lastTap.t < 0.28) { f.buf.dash = now; f.dashDir = dir; f.lastTap = { dir: 0, t: -9 }; }
        else f.lastTap = { dir, t: now };
      }
      if (h.dash) { f.buf.dash = now; f.dashDir = h.dash; }
      if (taps) { taps.light = 0; taps.up = 0; taps.special = 0; taps.heavy = 0; taps.dir = 0; taps.parry = 0; }
      f.holdGuard = !!h.guard;
      p.x = h.x; p.up = h.up; p.light = h.light; p.heavy = h.heavy; p.special = h.special; p.guard = !!h.guard; p.parry = !!h.parry;
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
          if (this.tryParry(f)) break;
          if (this.fresh(f, 'special') && f.rage >= 100) { this.use(f, 'special'); this.startSpecial(f, o); break; }
          if (this.fresh(f, 'dash') && f.dashDir) { this.use(f, 'dash'); this.startDash(f); break; }
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
          if (this.tryParry(f)) break;
          if (this.fresh(f, 'dash') && f.dashDir && !h.guard) { this.use(f, 'dash'); this.startDash(f); break; }
          if (this.fresh(f, 'light')) { this.use(f, 'light'); this.startAttack(f, 'light1'); break; }
          if (!h.guard) this.setState(f, 'idle');
          break;
        case 'guardstun':
          // 挡下一击后可立即格开（看准连段的下一击）：举防 → 格开 → 反击
          if (this.tryParry(f)) break;
          if (f.t >= f.stun) this.setState(f, h.guard ? 'guard' : 'idle');
          break;
        case 'guardbreak':
          if (f.t >= f.stun) this.setState(f, 'idle');
          break;
        case 'parry':
          // 格开架势：窗口内来招即弹开（在 hit() 里结算）；窗口过后露出破绽
          f.vx *= 0.7;
          if (f.t >= f.parryW) { this.setState(f, 'parrywhiff'); f.whiffT = PARRY_WHIFF; f.stats.whiffs++; this.emit({ type: 'parryWhiff', who: f.idx }); }
          break;
        case 'parrywhiff':
          if (f.t >= (f.whiffT || PARRY_WHIFF)) this.setState(f, h.guard ? 'guard' : 'idle');
          break;
        case 'parryok':
          // 弹刀姿势：随时可出招（反击）、跳、冲刺
          f.vx = 0;
          if (this.fresh(f, 'special') && f.rage >= 100) { this.use(f, 'special'); this.startSpecial(f, o); break; }
          if (this.fresh(f, 'light')) { this.use(f, 'light'); this.startAttack(f, 'light1'); break; }
          if (this.fresh(f, 'heavy')) { this.use(f, 'heavy'); this.setState(f, 'charge'); f.charge = 0; f.tapHeavy = !h.heavy; break; }
          if (this.fresh(f, 'dash') && f.dashDir) { this.use(f, 'dash'); this.startDash(f); break; }
          if (f.t >= PARRY_RIPOSTE) this.setState(f, h.guard ? 'guard' : 'idle');
          break;
        case 'parried':
          // 被格开：兵器被弹开、身形不稳
          if (f.t >= f.stun) this.setState(f, h.guard && f.grounded ? 'guard' : 'idle');
          break;
        case 'backstep':
          // 后撤步：快速后跃拉开距离，后半段可接轻击（迎击追来的对手）
          f.vx = f.dashDir * 6.8 * Math.max(0.2, 1 - f.t / BACKSTEP_T);
          if (f.t > 0.15 && this.fresh(f, 'light')) { this.use(f, 'light'); this.startAttack(f, 'light1'); break; }
          if (f.t >= BACKSTEP_T) this.setState(f, h.guard ? 'guard' : 'idle');
          break;
        case 'charge':
          f.charge = Math.min(1, f.t / CHARGE_MAX);
          if (f.charge >= 1 && !f.chargeFull) { f.chargeFull = true; this.emit({ type: 'chargeFull', who: f.idx }); }
          if ((!h.heavy || f.tapHeavy) && f.t >= 0.12 || f.t >= CHARGE_MAX + 0.3) {
            const c = f.charge;
            f.tapHeavy = false; f.chargeFull = false;
            this.startAttack(f, 'heavy');
            f.charge = c;
            this.emit({ type: 'heavyGo', who: f.idx, charge: c, su: heavySU(c, f.spd) / f.spd });   // 起手预兆：刃口闪光（su：到打中还有几秒）
          }
          break;
        case 'light1':
        case 'light2':
        case 'light3':
        case 'heavy': {
          if (f.state !== 'light3' && f.state !== 'heavy' && this.fresh(f, 'light') && f.t > 0.03) { this.use(f, 'light'); f.chain = true; }
          const su = suOf(f), total = su + mv.active + mv.recovery;
          // 突进：起手最后 0.4 段才开始（满蓄重击举刀那一顿原地不动，突进距离不变）
          if (mv.lunge && f.t >= su - mv.startup * 0.4 && f.t < su + mv.active) f.vx = f.face * mv.lunge;
          if (f.chain && mv.next && f.t >= su + mv.active * 0.6) { this.startAttack(f, mv.next); break; }
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
          // 硬直结束时若按住格挡，直接进入格挡（无空隙）；硬直中按了格开却没等到第三段 → 收势不及，短暂破绽
          if (f.t >= f.stun) {
            const wasted = f.hsParrySeq === f.seq;
            this.setState(f, h.guard && f.grounded ? 'guard' : 'idle'); f.combo = 0; o.combo = 0;
            if (wasted && f.grounded) { this.setState(f, 'parrywhiff'); f.whiffT = PARRY_WHIFF * 0.6; f.stats.whiffs++; this.emit({ type: 'parryWhiff', who: f.idx }); }
            f.hsParrySeq = -1;
          }
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

    // 格开：按下（缓冲内）且冷却已好 → 进入格开架势
    tryParry(f) {
      if (!this.fresh(f, 'parry') || !this.parryReady(f) || !f.grounded) return false;
      this.use(f, 'parry');
      f.parryFrom = f.state;
      this.setState(f, 'parry');
      f.parryReadyAt = this.clock + PARRY_CD;
      f.stats.parryTries++;
      f.chargeFull = false;
      this.emit({ type: 'parryStart', who: f.idx });
      return true;
    }
    // 双击方向：朝对手为冲刺，背向对手为后撤步
    startDash(f) {
      this.setState(f, f.dashDir === -f.face ? 'backstep' : 'dash');
      this.emit({ type: f.state === 'backstep' ? 'backstep' : 'dash', who: f.idx });
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
      } else if (f.state !== 'walk' && f.state !== 'dash' && f.state !== 'backstep' && !(f.state === 'special' && f.vx !== 0) && !(MOVES[f.state] && MOVES[f.state].lunge && f.t < suOf(f) + MOVES[f.state].active)) {
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
    // 无敌：倒地 / 起身 / 绝技起手（定格亮相与突进期间不会被对手解冻后的出招打断）
    immune(o) { return o.invuln > 0 || o.state === 'knockdown' || o.state === 'down' || o.state === 'getup' || o.ko || (o.state === 'special' && o.t < MOVES.special.startup); }

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

    // 这一击是否被格开：格开架势的窗口内（绝技与箭矢不可格开；反击的第一击不可格开）
    parries(f, o, h) {
      if (h.name === 'special' || h.arrow || o.hp <= 0 || this.countering(f)) return false;
      if (o.state === 'parry') return o.t <= o.parryW + 1e-6;
      // 受击硬直中预先按下的格开：只对连段第三段有效，且须按在第三段打到前 HS_PARRY_K × 窗口之内（踩准节奏）
      return this.hsArmed(f, o, h) && this.clock - o.hsParryAt <= o.parryW * HS_PARRY_K + 1e-6;
    }
    hsArmed(f, o, h) { return h.name === 'light3' && o.state === 'hitstun' && o.grounded && o.hsParrySeq === o.seq; }
    // 格开成功：攻方兵器被弹开、硬直；守方弹刀，获得反击窗口
    parrySuccess(f, o, h) {
      const dir = f.face;
      // 格开的途径：stance 站立 / 走动中按下 · guard 举防或挡下一击后按下 · hitstun 受击硬直中看破第三段
      const route = o.state === 'hitstun' ? 'hitstun' : o.parryFrom === 'guard' || o.parryFrom === 'guardstun' ? 'guard' : 'stance';
      o.stats.parryBy[route]++;
      this.setState(f, 'parried');
      f.stun = PARRY_STAGGER + (h.name === 'heavy' ? 0.1 : 0);
      f.vx = -dir * 1.8; f.charge = 0; f.chargeFull = false;
      if (f.y > 0) f.vy = Math.min(f.vy, 0);
      this.setState(o, 'parryok');
      o.vx = 0; o.hsParrySeq = -1;
      o.counterUntil = this.clock + COUNTER_WIN;
      o.rage = Math.min(100, o.rage + 8); f.rage = Math.min(100, f.rage + 2);
      o.stats.parries++; f.stats.parried++;
      f.combo = 0; o.combo = 0;
      this.hitstop = Math.max(this.hitstop, 0.13);
      const gap = Math.abs(o.x - f.x);
      this.emit({ type: 'parry', att: f.idx, def: o.idx, move: h.name, route, x: o.x - dir * Math.min(gap * 0.45, 0.75), y: o.y + (h.name === 'air' ? 1.5 : 1.3) });
    }

    hit(f, o, h) {
      const mv = h.mv, name = h.name, finisher = h.fin, chg = h.charge;
      if (this.parries(f, o, h)) { this.parrySuccess(f, o, h); return; }
      // 受击硬直中按早了：第三段照样打中（提示「过早」，计一次落空）
      if (this.hsArmed(f, o, h)) { o.hsParrySeq = -1; o.stats.whiffs++; this.emit({ type: 'parryEarly', who: o.idx }); }
      let base = mv.dmg * (f.wpn.pow || 1);
      if (name === 'heavy') base += mv.dmgCharge * chg;
      if (name === 'special' && finisher) base = mv.finisher;
      if (h.arrow) base *= 0.8;                     // 箭矢可远距离命中，威力略低
      // 反击：格开成功后的第一击 ×1.5、不可格挡（用掉即止）
      const riposte = this.countering(f) && !h.arrow;
      if (riposte) { f.counterUntil = 0; f.stats.counters++; base *= COUNTER_K; }
      // 破绽：打在对手出招起手 / 蓄力 / 格开落空上
      const counter = !riposte && (o.state === 'charge' || o.state === 'parrywhiff' || (MOVES[o.state] && o.phase === 'startup' && o.state !== 'special'));
      if (counter) base *= 1.2;
      let dmg = damage(base, f.war, o.war) * SG.Random.rangeFloat(0.92, 1.08);
      let guarded = !riposte && (o.state === 'guard' || o.state === 'guardstun') && o.grounded;
      // 硬挡：被连段打中后按住格挡，武力远高于攻方时有机会挡下后续一击（武力差 30 → 18%，70 → 50%），挡下后立刻能还手
      let deflect = false;
      if (!guarded && !riposte && o.state === 'hitstun' && o.holdGuard && o.grounded && o.hp > 0 && name !== 'special') {
        const p = M.clamp((o.war - f.war - 12) / 100, 0, 0.5);
        if (p > 0 && rnd() < p) { guarded = true; deflect = true; }
      }
      const charged = name === 'heavy' && chg >= 0.85;
      const breaks = guarded && (charged || (name === 'special' && finisher));
      if (guarded) dmg *= 0.2;
      dmg = M.clamp(Math.round(dmg), guarded ? 0 : 1, 40);
      o.hp = Math.max(0, o.hp - dmg);
      const dir = f.face;
      const big = !!mv.big || charged || riposte;
      let hs = mv.hitstop * (guarded ? 0.6 : 1) * (charged ? 1.3 : 1);
      if (riposte) hs = Math.max(hs * 1.4, 0.15);
      // 受击部位与方向（受击动画）：从背后挨打则向前扑
      o.hitH = mv.hitH || 'mid'; o.hitBack = o.face === dir;
      f.stats.hits++;
      if (name === 'air') f.airHit = true;
      if (guarded) f.stats.guarded++;
      o.stats.taken++;

      let knock = false;
      if (o.hp <= 0) {
        knock = true; o.ko = true;
      } else if (guarded) {
        if (breaks) { this.setState(o, 'guardbreak'); o.stun = 0.8 * o.stunK; this.emit({ type: 'guardbreak', who: o.idx }); }
        else { this.setState(o, 'guardstun'); o.stun = deflect ? 0.1 : (0.12 + mv.stun * 0.35) * o.stunK; }
        o.vx = dir * mv.push * (deflect ? 0.15 : mv.big ? 0.5 : 0.85);     // 大招被挡时推开得少：收招破绽可被反击；硬挡几乎不退、立刻能还手
        o.rage = Math.min(100, o.rage + 2 + dmg);
        f.rage = Math.min(100, f.rage + 1.5);
      } else {
        knock = (name === 'heavy' && chg >= 0.6) || (name === 'special' && finisher) || o.y > 0.35;
        f.combo = o.state === 'hitstun' ? f.combo + 1 : 1;
        o.rage = Math.min(100, o.rage + 3 + dmg * 1.2);
        f.rage = Math.min(100, f.rage + 3 + dmg * 0.8);
        if (!knock) {
          this.setState(o, 'hitstun');
          o.stun = mv.stun * o.stunK * (name === 'heavy' ? 1 + chg * 0.3 : 1) * (riposte ? 1.25 : 1);
          o.vx = dir * mv.push * (riposte ? 1.15 : 1);
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
      this.emit({ type: 'hit', att: f.idx, def: o.idx, dmg, guarded, deflect, riposte, move: name, big, counter, breaks, charge: chg, arrow: !!h.arrow, hitH: o.hitH, back: o.hitBack,
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
      const st = f => ({ hits: f.stats.hits, parries: f.stats.parries, parryTries: f.stats.parryTries, counters: f.stats.counters, whiffs: f.stats.whiffs, parryBy: Object.assign({}, f.stats.parryBy) });
      return { winner: r.winner, kind: kind || r.kind, hpA: this.f[0].hp, hpB: this.f[1].hp, log: this.log.slice(), stats: [st(this.f[0]), st(this.f[1])] };
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
      // 临场判断（抢回合、不撞对手的出手节奏、场外蓄力、看绝技举防）：武力 25 以下 0.2、50 → 0.47、76 → 0.75，90 以上封顶 0.9——
      // 弱将多半不懂，名将也会失手，不至于滴水不漏
      this.gs = 0.2 + 0.7 * M.clamp((me.war - 25) / 65, 0, 1);
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
      this.held = { x: 0, up: false, light: false, heavy: false, guard: false, special: false, parry: false, dash: 0 };
      this.taps = { light: 0, up: 0, special: 0, heavy: 0, dir: 0, parry: 0 };
      // 格开：武力 35 以下几乎不用；越高越常用、拿捏越准。按节奏预约的格开（连段的下一击），按下时刻的误差为三角分布
      // ±(0.18 + 0.25·(1−s)) 秒：对比整个有效窗口（±0.075–0.11 秒）武力 100 ≈ 85% 成功、75 ≈ 67%；受击硬直中看破第三段
      // 只认最后 0.6 × 窗口（±0.045–0.066 秒），但连段节奏固定、误差 × 0.65：武力 100 ≈ 80%、75 ≈ 65%（失手即被打中）。
      // 重击则是反应：与看见其他来招同样的反应时间（this.react），看清即按——只有满蓄重击来得及
      this.parryP = 0.6 * M.clamp((me.war - 35) / 65, 0, 1);
      this.parryErr = 0.18 + 0.25 * (1 - s);
      this.parryAt = -1; this.hvSeq = -1; this.pwSeq = -1; this.stanceSeq = -1;
      this.next = 0; this.planX = 0; this.guardUntil = 0; this.seenSeq = -1; this.comboSeq = -1; this.comboGo = false;
      this.chargeTo = 0; this.backUntil = 0;
      this.stunSeq = -1; this.stunGuard = false; this.stunHold = 0;
      this.aa = null; this.punSeq = -1; this.pokeAt = 0; this.lastGuarded = 0; this.zone = false; this.caut = false; this.cautAt = 0; this.patSeq = -1; this.patient = false;
      // 读招：识破连按 / 不看命中的固定连段。武力 40 以下不会，越高越准
      this.rk = M.clamp((me.war - 40) / 40, 0, 0.85);   // 武力 40 → 0、60 → 0.5、74 以上 → 0.85
      this.mash = 0.3;                   // 对手「没打中也照样连下去」的倾向（指数平均，0..1）
      this.oSeq = -1; this.oState = ''; this.oHit = false;
      this.ctrSeq = -1; this.mzAt = 0;
      this.p3 = 0; this.hsSeen = -1;     // 对手「挨了第二段就格开第三段」的倾向（指数平均，0..1）
      this.baitUntil = 0;                // 故意只打两段诱对手格开落空：此前料定，无需反应即可抓破绽
      // 回合感与对手的试探习惯（记忆而非反应：只统计已发生的动作，慢慢淡忘；武力越高越会用）
      this.pkN = 0.5; this.pkT = 1;      // 自己站在对手轻击距离内、对手可出手时，对手主动起手的次数 / 时长 → 试探频率（次/秒）
      this.repoke = 0.3;                 // 对手的出手被挡后立刻再出手（抢回合）的倾向
      this.turtle = 0.2;                 // 近身时对手举防不动的时间比例
      this.hvBad = 0;                    // 自己的满蓄重击被格开 / 蓄力中被打的记忆（越多越少用）
      this.blk = null;                   // 刚挡下的一击 { seq, move }：防御硬直一结束就轮到自己
      this.rpWatch = 0;                  // 挡下后观察对手是否立刻再出手（截止时刻）
      this.ttAt = -1;                    // 抢回合的出手时刻（略有迟疑，武力越低越慢）
      this.ttBad = 0; this.ttMine = null; // 抢回合的那一刀被挡 / 被格开 / 被抢先的记忆：越多越常改为慢半拍或不抢
      this.chgFar = false; this.chgAt = 0; // 场外蓄力：在对手够不着处蓄满，待其走进重击距离再松手
      this.spSeq = -1; this.spGuard = false;
      this.hitN = 0; this.quietAt = 0;   // 最近一次交手（打中 / 被挡 / 挨打）的时刻：相持久了会按捺不住
      this.ttLate = false; this.ttDash = false; this.hvWait = 0; this.chaseFar = false; this.lastAtk = ''; this.stopAt = 0;
      // 对手一见自己松开蓄力重击（六成以上）就按格开的倾向（记忆：每次松手看他按没按，慢慢淡忘）。惯于格开时不往他就绪的
      // 格开上送满蓄重击，改为在他够不着处蓄满佯攻（骗他格开落空），趁他格开冷却（看得见）再真打
      this.hvPar = 0.15; this.hvRel = -1; this.hvWatch = null; this.fnt = false; this.fntHold = 0;
      this.ttBack = 0; this.ttChase = false; this.chaseUntil = 0; this.kdSeq = -1; this.okiPick = 1; // 抢回合那一刀被他退开让空的记忆：越多越常改为冲刺斩追击（再追不上就少抢）
    }
    // 他的格开还要多久才好：看得见他按格开（架势 / 冷却圈），有反应延迟——刚按下、还没看清的不算
    foeCd(sim) {
      const f = sim.other(this.me);
      return f.parryReadyAt - PARRY_CD <= sim.clock - this.react ? Math.max(0, f.parryReadyAt - sim.clock) : 0;
    }
    // 时间紧迫 0..1：剩下 45 秒起越来越沉不住气（剩 15 秒时最急）；落后（同分算守擂一方落后：超时判挑战者胜）更急，领先则耗得起。
    // 电脑对电脑（无画面模拟 / 观战）：两边都等对方先出手只会耗到时间——双方都按「急」打
    urg(sim) {
      const me = this.me, foe = sim.other(me);
      const behind = me.hp < foe.hp || (me.hp === foe.hp && me.idx === 1);
      const u = M.clamp((45 - (sim.limit - sim.time)) / 30, 0, 1) * (behind ? 1 : 0.35);
      return foe.ctrl instanceof AI ? Math.max(u, 0.8) : u;
    }
    // 倒地 / 起身中的对手还要多久才打得到（倒地 0.62 秒 → 起身 0.42 秒 → 无敌 0.2 秒；招式节奏是看得出来的）
    hittableIn(foe) {
      if (foe.state === 'knockdown') return 9;
      if (foe.state === 'down') return Math.max(0, 0.62 - foe.t) + 0.42 + 0.2;
      if (foe.state === 'getup') return Math.max(0, 0.42 - foe.t) + 0.2;
      return Math.max(0, foe.invuln);
    }
    // 对手的试探频率（次/秒）与「此刻抢攻会撞上他出手」的风险 0..1
    pokeRate() { return this.pkN / this.pkT; }
    pokeRisk() { return 1 - Math.exp(-this.pokeRate() * 0.2); }
    // 预约一次格开：瞄准有效区间的正中（站立格开 = 来招前半个窗口；受击硬直中 = 前 0.3 个窗口）按下，误差随武力；
    // 冷却赶不上就不预约。误差算出来早于此刻 = 本该更早按 → 只能此刻按（多半仍落在窗口里，仅限站立格开）
    planParry(sim, T, k, ek) {
      const me = this.me, now = sim.clock;
      const at = Math.max(now, T - me.parryW * (k === undefined ? 0.5 : k) + (rnd() + rnd() - 1) * this.parryErr * (ek || 1));
      if (sim.parryCd(me) > at - now) return false;
      this.parryAt = at;
      return true;
    }
    // 对手正在出的连段若接下一段，下一击打到的时刻
    nextChainHit(sim, foe) {
      const cur = MOVES[foe.state];
      return sim.clock + (Math.max(0, cur.startup + cur.active * 0.6 - foe.t) + MOVES[cur.next].startup) / foe.spd;
    }
    // 识破程度 0..1：武力 × 对手连按的证据
    read(foe) { return this.rkVs(foe) * this.mashN(); }
    mashN() { return M.clamp((this.mash - 0.5) / 0.3, 0, 1); }   // 连按证据：0.5 以下视为正常打法
    // 对手武力高出越多（出招越快越难看清），读招越不灵；武力 85 以上的名将连按起来也快而难读
    rkVs(foe) { return this.rk * M.clamp(1 + (this.me.war - foe.war) / 30, 0, 1) * (1 - 0.35 * M.clamp((foe.war - 85) / 15, 0, 1)); }
    // 识破连按时不在对手的攻击距离里抢攻（对手一进距离就会挥刀，抢攻只会被打断）→ 改为举防，等他打完再还手
    holdOff(sim, seen, rd) {
      const me = this.me, foe = sim.other(me);
      if (rd <= 0 || !seen || seen.phase === 'recovery' || !(seen.state === 'walk' || seen.state === 'idle' || seen.state === 'dash' || ATTACKS.has(seen.state))) return false;
      if (Math.abs(foe.x - me.x) > foe.reach * MOVES.light1.reachK + BODY * 0.5 + 0.25 || rnd() >= rd * 0.6) return false;
      this.guardUntil = Math.max(this.guardUntil, sim.clock + 0.25 + rnd() * 0.25); this.held.guard = true; this.held.x = 0; this.planX = 0;
      return true;
    }
    // 不往对手的出手节奏里撞：对手可出手、自己在其轻击距离内，而他惯于频频试探 → 此刻抢攻多半被他抢先打在起手上。
    // 武力越高越懂得改为举防等他出手（挡下后轮到自己）。far：冲刺切入之前（只决定不冲，不举防）
    wary(sim, seen, far) {
      const me = this.me, foe = sim.other(me);
      // 他此刻（或转眼之间）就能出手：硬直 / 收招的长短是看得出来的（自己打出来的硬直更清楚）
      const left = foe.state === 'hitstun' || foe.state === 'guardstun' ? foe.stun - foe.t : MOVES[foe.state] && foe.phase === 'recovery' ? (totalOf(foe) - foe.t) / foe.spd : -1;
      const ready = (seen && (seen.state === 'idle' || seen.state === 'walk' || seen.state === 'guard' || seen.state === 'dash' || seen.atk)) || (left >= 0 && left < 0.12);
      if (!ready) return false;
      const foeR = foe.reach * MOVES.light1.reachK + BODY * 0.5;
      if (!far && Math.abs(foe.x - me.x) > foeR + 0.3) return false;
      // 名将也有一成多按捺不住；相持 2 秒以上越来越沉不住气
      const calm = M.clamp(1 - (sim.clock - this.quietAt - 2) / 3, 0.35, 1) * (1 - 0.8 * this.urg(sim));
      if (rnd() >= Math.min(0.85, (far ? this.gs * 2.6 : this.gs * this.gs * 2.4) * this.pokeRisk()) * calm) return false;
      if (far) return true;
      this.held.x = 0; this.planX = 0;
      // 他这一刀眼看就来（频频试探、此刻正好够得着）：偶尔看准节奏格开它（格开冷却长，失手要挨打，所以不常用），否则举防等他出手
      if (sim.parryReady(me) && sim.actionable(me) && seen && (seen.state === 'idle' || seen.state === 'walk' || seen.state === 'guard') && rnd() < this.parryP * this.pokeRisk() * 0.6) { this.parryAt = sim.clock + 0.03 + rnd() * (0.06 + 0.1 * (1 - this.s)); return true; }
      this.guardUntil = Math.max(this.guardUntil, sim.clock + 0.3 + rnd() * 0.35);
      this.held.guard = true;
      return true;
    }
    // 对手缩着举防的程度 0..1（近身时举防的时间多、又很少主动出手）
    shellK() { return M.clamp((this.turtle - 0.4) / 0.35, 0, 1) * M.clamp(1.5 - 0.6 * this.pokeRate(), 0, 1); }
    // 蓄力的站位：他轻击够不着、自己满蓄重击（含突进）够得着
    chargeSpot(foe) {
      const me = this.me;
      return { lo: foe.reach * MOVES.light1.reachK + BODY * 0.5 + 0.06, hi: me.reach * MOVES.heavy.reachK + BODY * 0.5 + 0.22 - 0.08 };
    }
    // 场外蓄力：对手惯于走进来试探时，在他轻击够不着处先蓄满重击，待他走进重击距离（重击比轻击远、且带突进）再松手；
    // 对手一味举防不出手时，走到重击够得着而他轻击够不着处蓄满破防。满蓄重击被格开 / 蓄力中挨打的次数越多越少用
    // 他惯于见刃光就格开、格开又已就绪：满蓄重击改为佯攻（在他够不着处蓄满松手）。看见他的格开在冷却、蓄满打到之前好不了 → 真打
    feintSpot(foe) { const hi = this.chargeSpot(foe).hi + 0.08; return { lo: hi + 0.07, hi: hi + 0.32 }; }
    // 走到蓄力站位、蓄满、打到之前他的格开都好不了
    cdWin(sim) {
      const me = this.me, foe = sim.other(me), walk = Math.max(0, Math.abs(foe.x - me.x) - this.chargeSpot(foe).hi + 0.05) / me.walk;
      return this.foeCd(sim) > walk + CHARGE_MAX + heavySU(1, me.spd) / me.spd + 0.06;
    }
    wantFeint(sim) { return this.hvPar > 0.35 && this.foeCd(sim) === 0; }
    farCharge(sim, seen, dist) {
      const me = this.me, foe = sim.other(me), now = sim.clock, s = this.gs;
      const win = this.cdWin(sim);    // 他格不开（不受蓄力间隔的限制：机不可失）
      if ((now < this.chgAt && !win) || !sim.actionable(me) || !seen || !(seen.state === 'walk' || seen.state === 'idle' || seen.state === 'guard' || (win && (seen.state === 'parry' || seen.state === 'parrywhiff')))) return false;
      const sp = this.chargeSpot(foe), hvR = sp.hi + 0.08;
      const approach = M.clamp((this.pokeRate() - 0.6) / 1.2, 0, 1), shell = this.shellK();
      const pr = win ? 0 : this.hvPar;
      if (shell > 0 && !win && this.wantFeint(sim)) {
        // 佯攻：蓄满了照样松手（够不着、挥空），他一按格开就落空、进冷却
        const fs = this.feintSpot(foe);
        if (dist < fs.lo || dist > fs.hi) return false;
        this.chgAt = now + 0.4;
        if (rnd() >= (0.3 + 0.6 * s) * s * shell * Math.min(1, pr * 1.5) * (1 - this.urg(sim) * 0.5)) return false;
        this.chgAt = now + 0.8 + rnd() * 1.2;
        this.chgFar = 'shell'; this.fnt = true; this.fntHold = rnd() * 0.22; this.heavy(9);
        return true;
      }
      // 他正走过来：估计他走到重击距离还要多久（有反应延迟），约 0.4–1 秒（蓄得够深）才开始蓄
      const prev = sim.seen(foe.idx, this.react + 0.12);
      const vc = prev && seen.clock > prev.clock ? (Math.abs(prev.x - me.x) - Math.abs(seen.x - me.x)) / (seen.clock - prev.clock) : 0;
      const tArr = vc > 0.8 ? (dist - vc * this.react - (hvR - 0.1)) / vc : 9;
      const mode = approach > 0 && dist > sp.lo + 0.1 && tArr > 0.4 && tArr < 1.0 ? 'approach' : shell > 0 && dist >= sp.lo && dist <= sp.hi ? 'shell' : '';
      if (!mode) return false;
      if (!win) this.chgAt = now + 0.4;
      let p = (0.1 + 0.5 * s) * s * (mode === 'shell' ? shell : approach) * (1 - 0.8 * this.hvBad) * (1 - 0.9 * pr);
      if (win && mode === 'shell') p = Math.max(p, (0.35 + 0.6 * s) * Math.min(1, 0.4 + shell) * (1 - 0.5 * this.hvBad));
      if (rnd() >= p) return false;
      this.chgAt = now + 2.2 + rnd() * 2.2;
      this.chgFar = mode; this.fnt = false; this.heavy(9);
      return true;
    }
    // 观察对手的出招习惯（记忆而非反应：只统计已发生的动作）
    observe(sim) {
      const me = this.me, foe = sim.other(me);
      // 对手在受击硬直中按格开（看得见的出手）/ 自己的第三段被格开：记下这个习惯，慢慢淡忘
      if (foe.state === 'hitstun' && foe.hsParrySeq === foe.seq && this.hsSeen !== foe.seq) { this.hsSeen = foe.seq; this.p3 += (1 - this.p3) * 0.6; }
      this.p3 *= 1 - DT * 0.05;
      // 试探频率：自己在对手轻击距离内、对手可出手的时长（约 14 秒的记忆）；近身时对手举防的时间比例
      const d = Math.abs(foe.x - me.x), foeR = foe.reach * MOVES.light1.reachK + BODY * 0.5;
      const fade = 1 - DT * 0.07;
      this.pkN *= fade; this.pkT *= fade; this.hvBad *= 1 - DT * 0.03;
      const expo = d <= foeR + 0.15 && (sim.actionable(me) || me.state === 'guard');   // 自己站在他轻击距离内、没在出招
      if ((foe.state === 'idle' || foe.state === 'walk' || foe.state === 'guard') && expo) this.pkT += DT;
      const fg = foe.state === 'guard' || foe.state === 'guardstun';
      if (d < foeR + 1 && (fg || foe.state === 'idle' || foe.state === 'walk')) this.turtle += ((fg ? 1 : 0) - this.turtle) * DT * 0.5;
      // 挡下之后：对手被自己打中 → 无从得知；观察期过了也没出手 → 他不抢回合
      if (this.rpWatch) {
        if (foe.state === 'hitstun' || foe.state === 'guardstun' || foe.state === 'knockdown' || foe.state === 'parried') this.rpWatch = 0;
        else if (sim.clock > this.rpWatch) { this.repoke -= this.repoke * 0.2; this.rpWatch = 0; }
      }
      // 抢回合那一刀的结果：打中 → 淡忘；被挡 / 被格开 / 被他抢先打中 → 记下
      const hn = me.stats.hits + me.stats.taken;
      if (hn !== this.hitN) { this.hitN = hn; this.quietAt = sim.clock; }
      // 观察到收招后 0.35 秒：挥空（多半是他挡下就退开）也记下，退开后抓自己挥空的破绽算「被抢先」
      const tm = this.ttMine;
      if (tm) {
        const st = me.stats;
        if (tm.seq < 0 && me.state === 'light1') tm.seq = me.seq;
        if (foe.state === 'backstep' || ((foe.state === 'walk' || foe.state === 'idle') && foe.vx * me.face > 0.5)) tm.back = true;
        if (st.guarded > tm.guarded || st.parried > tm.parried || st.taken > tm.taken) {
          this.ttBad += (1 - this.ttBad) * 0.4;
          if (tm.end) this.ttBack += (1 - this.ttBack) * 0.25;
          this.ttMine = null;
        }
        else if (st.hits > tm.hits) { this.ttBad *= 0.75; if (!tm.dash) this.ttBack *= 0.7; this.ttMine = null; }
        else if (tm.seq >= 0 && !tm.end && (me.seq !== tm.seq || (me.phase === 'recovery' && !me.hitIdx))) { tm.end = sim.clock; this.ttBack += (1 - this.ttBack) * (tm.back ? 0.45 : 0.2); }
        else if (tm.end ? sim.clock > tm.end + 0.35 : tm.seq < 0 && sim.clock > tm.t + 0.3) this.ttMine = null;
      }
      this.ttBack *= 1 - DT * 0.01;
      // 自己松开的蓄力重击（六成以上，他的格开当时已就绪——事后看冷却圈就知道；佯攻也算）：他按没按格开
      if (me.state === 'heavy' && me.seq !== this.hvRel) {
        this.hvRel = me.seq;
        this.hvWatch = me.charge >= 0.6 && sim.parryReady(foe) ? { until: sim.clock + heavySU(me.charge, me.spd) / me.spd + 0.12, tries: foe.stats.parryTries } : null;
      }
      const hw = this.hvWatch;
      if (hw) {
        if (foe.stats.parryTries > hw.tries) { this.hvPar += (1 - this.hvPar) * 0.5; this.hvWatch = null; }
        else if (sim.clock > hw.until) { this.hvPar -= this.hvPar * 0.3; this.hvWatch = null; }
      }
      this.hvPar *= 1 - DT * 0.004;
      if (MOVES[foe.state] && foe.hitIdx > 0 && (me.state === 'hitstun' || me.state === 'knockdown')) this.oHit = true;
      if (foe.seq === this.oSeq) return;
      const prev = this.oState;
      // 主动起手（轻击 / 蓄力 / 冲刺斩）：计入试探频率；挡下后的观察期内起手 = 抢回合
      if ((foe.state === 'light1' || foe.state === 'charge') && (prev === 'idle' || prev === 'walk' || prev === 'guard' || prev === 'dash' || prev === 'backstep' || prev === 'land')) {
        if (expo || (d <= foeR + 0.4 && (me.state === 'walk' || me.state === 'idle' || me.state === 'dash'))) this.pkN += 1;
        if (this.rpWatch && sim.clock <= this.rpWatch) { this.repoke += (1 - this.repoke) * 0.35; this.rpWatch = 0; }
      }
      if ((prev === 'light1' || prev === 'light2') && !this.oHit) {
        // 上一段挥空 / 被挡：接着出下一段 = 不看命中的连按；停手 = 有判断
        if (foe.state === MOVES[prev].next) this.mash += (1 - this.mash) * 0.3; else this.mash -= this.mash * 0.12;
      }
      if (foe.state === 'light1' && me.state !== 'hitstun' && Math.abs(foe.x - me.x) > foe.reach * MOVES.light1.reachK + BODY * 0.5 + 0.08) this.mash += (1 - this.mash) * 0.12;   // 够不着也出手
      this.oSeq = foe.seq; this.oState = foe.state; this.oHit = false;
    }
    press(k) { this.taps[k] = (this.taps[k] || 0) + 1; }
    // 冲刺斩：先冲刺，冲到够得着再出斩（冲刺中一出斩就失去冲势——同一帧按下只多走半步，够不着）
    dashStrike(now, far) { this.held.dash = this.me.face; this.chaseUntil = now + 0.3; this.chaseFar = !!far; }
    // 重击：同时按住，蓄力到 to 秒后松开（只点按会被当作轻点重击）
    // 举防中按下的重击要等放下防御（下一步）才起手：这期间一直按住，免得被当作轻点
    heavy(to) { this.press('heavy'); this.held.heavy = true; this.chargeTo = to; this.hvWait = 0.12; }

    update(dt, sim) {
      this.observe(sim);
      this.think(dt, sim);
      if (this.parryAt >= 0 && sim.clock >= this.parryAt - 1e-9) { this.press('parry'); this.parryAt = -1; }
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
      // 冲刺斩：冲到够得着（追退开的对手要更近）或冲势将尽再出斩
      if (now < this.chaseUntil && me.state === 'dash') { if (dist <= reach - (this.chaseFar ? 0.35 : 0.1) || me.t >= 0.19) { this.chaseUntil = 0; this.press('light'); } return; }

      // 上一步是否正在蓄力 / 满蓄重击起手（被打断或被格开时记下）
      const lastAtk = this.lastAtk;
      this.lastAtk = me.state === 'charge' || (me.state === 'heavy' && me.charge >= 0.85 && me.phase === 'startup') ? 'charge' : '';
      // 蓄力：到达预定蓄力量后松开
      if (me.state === 'charge') {
        H.x = 0; H.guard = false;
        this.hvWait = 0;
        if (!this.chgFar) { H.heavy = me.t < this.chargeTo; return; }
        // 场外蓄力：按看到的对手位置与来势（有反应延迟）估计松手后打到时的距离——蓄满且他将进入重击距离才松手；
        // 他逼到轻击距离边上（再等就要挨他一刀）则提前松手；蓄满后他迟迟不来、或摆出格开架势，也就松手了
        const hvR = me.reach * MOVES.heavy.reachK + BODY * 0.5 + 0.22, foeR = foe.reach * MOVES.light1.reachK + BODY * 0.5;
        const prev = sim.seen(foe.idx, this.react + 0.12);
        const vc = seen && prev && seen.clock > prev.clock ? Math.max(0, (Math.abs(prev.x - me.x) - Math.abs(seen.x - me.x)) / (seen.clock - prev.clock)) : 0;
        const dNow = (seen ? Math.abs(seen.x - me.x) : dist) - vc * this.react;
        const dImp = dNow - vc * (heavySU(Math.min(1, me.t / CHARGE_MAX), me.spd) / me.spd);
        // 迎着走来的对手：蓄到六成以上（打中即击倒）就可以松手；对着缩着举防的对手：非蓄满（破防）不松手；
        // 倒地的对手：松手后打到时他刚好起身无敌结束
        const full = me.t >= (this.chgFar === 'shell' ? CHARGE_MAX : CHARGE_MAX * 0.65);
        const tImp = heavySU(Math.min(1, me.t / CHARGE_MAX), me.spd) / me.spd;
        const okiGo = this.chgFar === 'oki' && this.hittableIn(foe) <= tImp + 0.03;
        const go = okiGo || (this.chgFar !== 'oki' && full && dImp <= hvR - 0.06) || ((dNow <= foeR - 0.05 || (vc > 0.5 && dNow <= foeR + 0.08)) && me.t >= 0.45) || me.t >= CHARGE_MAX + 0.26 ||
          (this.fnt && me.t >= CHARGE_MAX + this.fntHold) ||
          (seen && seen.state === 'parry') || (seen && MOVES[seen.state] && seen.phase !== 'recovery' && dist < foe.reach + 0.6 && me.t >= 0.3);
        H.heavy = !go;
        if (go) { this.chgFar = false; this.fnt = false; }
        return;
      }
      if (this.hvWait > 0 && (sim.actionable(me) || me.state === 'guard')) { this.hvWait -= dt; H.heavy = true; H.guard = false; H.x = 0; return; }
      this.hvWait = 0; H.heavy = false; this.chgFar = false; this.fnt = false;
      // 受击 / 被挡 / 倒地：高武力会按住格挡，一恢复就举防（并有机会格开弱者的连段）
      if (me.state === 'hitstun' || me.state === 'guardstun' || me.state === 'guardbreak' || me.state === 'knockdown' || me.state === 'down' || me.state === 'getup' ||
        me.state === 'parried' || me.state === 'parrywhiff') {
        if (this.stunSeq !== me.seq) {
          this.stunSeq = me.seq; this.stunGuard = rnd() < this.stunGuardP; this.stunHold = 0.1 + rnd() * 0.22;
          this.parryAt = -1; this.ttAt = -1;
          // 挡下的是哪一招：硬直一结束就轮到自己（见下方「抢回合」）；挨打则作罢
          this.blk = me.state === 'guardstun' && ATTACKS.has(foe.state) && foe.state !== 'special' && foe.state !== 'air' ? { seq: foe.seq, move: foe.state } : null;
          // 满蓄重击被格开 / 蓄力中被打：记下，往后少用
          if ((me.state === 'parried' || me.state === 'hitstun' || me.state === 'knockdown') && lastAtk === 'charge') this.hvBad += (1 - this.hvBad) * 0.45;
          const rk = this.rkVs(foe);
          // 看破连段：被第二段打中，料定对手会接第三段 → 算准第三段打到的时刻按下格开（兵器较短、近不了身的更要靠格开）
          if (me.state === 'hitstun' && me.grounded && foe.state === 'light2' && rk > 0 && sim.parryReady(me)) {
            const outK = 1 + 2 * M.clamp((foe.reach - me.reach) / 0.25, 0, 1);
            if (rnd() < this.parryP * (0.3 + 0.15 * this.mashN()) * (0.5 + 0.5 * rk) * outK) this.planParry(sim, this.nextChainHit(sim, foe), HS_PARRY_K * 0.5, 0.65);
          }
          // 挡下连段的一击：料定对手还会接着打 → 格开下一击，然后反击（对手停手则落空，露出破绽）
          if (me.state === 'guardstun' && (foe.state === 'light1' || foe.state === 'light2') && sim.parryReady(me) &&
            rnd() < this.parryP * (0.05 + 0.6 * this.mashN())) this.planParry(sim, this.nextChainHit(sim, foe));
        }
        // 挡下对手的连段末段或重击：记下，硬直一结束就反击其收招破绽
        if (me.state === 'guardstun' && this.ctrSeq !== foe.seq && (foe.state === 'light3' || foe.state === 'heavy') && rnd() < this.punishP * this.rkVs(foe) * (0.125 + 0.375 * this.read(foe))) this.ctrSeq = foe.seq;
        H.guard = this.stunGuard; H.x = 0;
        if (foe.state === 'special' && seen && seen.state === 'special' && seen.seq === foe.seq) {
          if (this.spSeq !== foe.seq) { this.spSeq = foe.seq; this.spGuard = rnd() < 0.3 + 0.5 * this.gs; }
          H.guard = H.guard || this.spGuard;
        }
        if (this.stunGuard) this.guardUntil = Math.max(this.guardUntil, now + this.stunHold);
        return;
      }
      // 连段：高武力只在命中时继续（命中确认），低武力随机
      if (me.state === 'light1' || me.state === 'light2') {
        if (this.comboSeq !== me.seq) { this.comboSeq = me.seq; this.comboRoll = rnd(); this.comboDone = false; }   // 每一段各自判定
        const mv = MOVES[me.state];
        if (!this.comboDone && (me.hitIdx > 0 || me.t >= mv.startup + mv.active)) {   // 命中即确认；挥空则在判定结束时决定
          const landed = me.hitIdx > 0 && foe.state === 'hitstun';
          // 对手被打退后还够不够得着下一段（够不着就别接，免得挥空露破绽；武力越高越会算）
          const away = foe.vx * me.face > 0 ? foe.vx * foe.vx / (2 * FRICTION) : 0;
          const reachOk = dist + away <= me.reach * MOVES[mv.next].reachK + BODY * 0.5 + 0.03 + (MOVES[mv.next].lunge || 0) * 0.12;
          let go = landed ? this.comboRoll < this.comboP && (reachOk || rnd() > s) : this.comboRoll < this.comboP * (1 - s) * 0.6;
          // 对手惯于格开第三段且格开已就绪：只打两段就收（让他落空，再抓破绽）
          if (go && me.state === 'light2' && landed && sim.parryReady(foe) && rnd() < this.rkVs(foe) * this.p3 * 1.5) { go = false; this.baitUntil = sim.clock + 0.9; }
          if (go) this.press('light');
          this.comboDone = true;
        }
        H.x = 0; H.guard = false;
        return;
      }
      this.comboDone = false;
      const free = sim.actionable(me) || me.state === 'guard';
      // 格开成功：趁对手硬直反击（第一击 ×1.5 不可挡）——近则重击或连段，远则冲刺斩
      if (me.state === 'parryok' || (sim.countering(me) && free)) {
        this.guardUntil = 0; H.guard = false; this.parryAt = -1;
        if (dist <= reach + 0.05) {
          if (foe.state === 'parried' && rnd() < 0.25 + 0.4 * s) this.heavy(0.14 + rnd() * 0.12);
          else this.press('light');
          H.x = 0;
        } else if (dist <= reach + 0.85) {
          // 弹刀姿势里先出的是轻击（够不着就挥空）：先冲刺，冲刺中再出斩
          this.dashStrike(now);
        } else H.x = me.face;
        return;
      }
      // 诱招得手：对手果然格开落空 → 立即抓破绽（早有预料，不必等看清）
      if (now < this.baitUntil && foe.state === 'parrywhiff' && free) {
        this.baitUntil = 0; this.guardUntil = 0; H.guard = false;
        if (dist <= reach) { this.press('light'); H.x = 0; return; }
        if (dist <= reach + 1.2) { this.dashStrike(now); return; }
      }
      // 已预约格开：举防不动，等那一刻
      if (this.parryAt >= 0) { this.blk = null; if (free) { H.guard = true; H.x = 0; } return; }
      if (me.state === 'parry') { H.x = 0; H.guard = false; return; }
      // 对手放绝技（定格期间看得清清楚楚）：绝技不可格开，也躲不开突进——举防硬吃（被终结一击破防也比连挨四刀轻）
      if (foe.state === 'special' && seen && seen.state === 'special' && seen.seq === foe.seq) {
        if (this.spSeq !== foe.seq) { this.spSeq = foe.seq; this.spGuard = rnd() < 0.3 + 0.5 * this.gs; }
        if (this.spGuard && (free || me.state === 'guardstun')) { this.blk = null; this.ttAt = -1; H.guard = true; H.x = 0; return; }
      }
      // 抢回合：挡下对手的一击、防御硬直刚结束（连段的下一击早该到了，没到就是打完了）——
      // 大招（第三段 / 重击）收招慢，按招式节奏算得出来得及就反击；轻击被挡后双方几乎同时能动：对手不惯于立刻再出手
      // 就趁他收刀抢先出手（略有迟疑，武力越低越慢、越常错过），惯于立刻再出手的则改为格开他那一刀或继续举防
      if (this.blk && free) {
        const b = this.blk; this.blk = null;
        const same = foe.seq === b.seq && !!MOVES[foe.state];
        const left = same ? Math.max(0, (totalOf(foe) - foe.t) / foe.spd) : 0;
        const need = MOVES.light1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
        const big = b.move === 'light3' || b.move === 'heavy';
        // 对手武力高出越多（出手快、收招也快）越难抢到回合；弱将（临场判断差）多半想不到
        const gapK = M.clamp(1 + (me.war - foe.war) / 45, 0.3, 1) * Math.min(1, this.gs / 0.6);
        this.rpWatch = now + left + 0.14;    // 他收刀后 0.14 秒内再出手 = 惯于立刻抢回合
        if (big && same) {
          // 这一判定取代了防御硬直中「记下大招」的那一次（不重复掷骰）
          const go = left > need - 0.02 && dist <= reach + 1.0 && rnd() < this.punishP * gapK * 0.6;
          this.ctrSeq = -1;
          if (go) {
            this.guardUntil = 0; H.guard = false;
            if (dist <= reach) { this.press('light'); H.x = 0; return; }
            this.dashStrike(now); return;
          }
        }
        if (!big && dist <= reach + 0.8 && foe.state !== 'special') {
          const rp = this.repoke;
          if (rp > 0.45 && sim.parryReady(me) && rnd() < this.parryP * (rp - 0.3) * 1.6 &&
            this.planParry(sim, now + left + MOVES.light1.startup / foe.spd + 0.03)) { this.guardUntil = 0; H.guard = true; H.x = 0; return; }
          // 够不着（对手兵器长、挡下后又被推开）：冲刺斩抢回合
          const out = dist > reach;
          // 他惯于挡下就退开（站着出的这一刀多半挥空、反被他抓）：改为冲刺斩追上去（立刻出手，冲刺比后撤快）；连冲刺斩也追不上就少抢
          const tb = this.ttBack, chase = rnd() < tb * (0.5 + 0.5 * this.gs);
          if (rnd() < 0.85 * this.gs * gapK * (1 - 0.9 * rp) * (1 - 0.45 * this.ttBad) * (1 - 0.5 * tb * tb)) {
            // 他惯于挡住（或格开）立刻还手的那一刀：多半改为慢半拍——举防等他放下防御再出手
            this.ttLate = rnd() < this.ttBad * 0.8;
            this.guardUntil = 0; H.guard = this.ttLate; H.x = 0;
            this.ttAt = now + (this.ttLate ? 0.28 + rnd() * 0.27 : rnd() * (0.05 + 0.14 * (1 - this.gs))); this.ttDash = out || (chase && !this.ttLate); this.ttChase = this.ttDash && !out;
          }
        }
      }
      if (this.ttAt >= 0) {
        if (!free) this.ttAt = -1;
        else if (foe.state === 'special') this.ttAt = -1;
        else if (now >= this.ttAt) {
          this.ttAt = -1;
          // 慢半拍出手时他已退开：近则冲刺斩，远了就作罢
          if (dist > reach + (this.ttDash ? 0.85 : this.ttLate ? 0.7 : 9)) { H.guard = false; return; }
          H.guard = false; H.x = 0;
          // 追击退开的对手：先冲刺，冲到够得着再出斩（冲刺一出斩就失去冲势，只多走半步）
          if (this.ttChase) this.dashStrike(now, true);
          else if ((this.ttDash || this.ttLate) && dist > reach) this.dashStrike(now);
          else this.press('light');
          this.ttMine = { t: now, hits: me.stats.hits, guarded: me.stats.guarded, parried: me.stats.parried, taken: me.stats.taken, seq: -1, end: 0, back: false, dash: !!H.dash };
          return;
        }
        else { H.guard = !!this.ttLate; H.x = 0; return; }
      }
      // 看清重击起手（刃口闪光）：反应时间与看见其他来招相同。看清即按（手上再慢 0–0.05 秒，武力越低越慢）；
      // 熟知招式节奏，算得出来不来得及——轻点重击（约 0.15 秒）看到时已打到，来不及就举防；满蓄重击（约 0.27 秒，会破防）
      // 武力高者常能当场格开，但至多不到一半（HEAVY_PARRY_CAP），其余照样被破防或挨打
      if (foe.state === 'heavy' && foe.phase === 'startup' && foe.seq !== this.hvSeq && free && sim.parryReady(me)) {
        const sn = sim.seen(foe.idx, this.react);
        if (sn && sn.state === 'heavy' && sn.seq === foe.seq) {
          this.hvSeq = foe.seq;
          const mvH = MOVES.heavy, at = now + rnd() * 0.05 * (1.6 - s), T = now + (suOf(foe) - foe.t) / foe.spd;
          if (at < T - 0.01 && dist <= foe.reach * mvH.reachK + BODY * 0.5 + 0.6 && rnd() < Math.min(HEAVY_PARRY_CAP, this.parryP * (0.55 + 0.45 * foe.charge))) {
            this.parryAt = at; H.guard = true; H.x = 0; return;
          }
        }
      }
      // 看见对手摆出格开架势：不往刀口上撞，停手等他落空（落空后由下面一段抓破绽）
      if (seen && seen.state === 'parry' && free) {
        if (seen.seq !== this.stanceSeq) { this.stanceSeq = seen.seq; this.stanceHold = rnd() < 0.1 + 0.8 * s; }
        if (this.stanceHold) { this.planX = 0; H.x = 0; H.guard = false; this.guardUntil = 0; return; }
      }
      // 对手格开落空：抓破绽（按剩余破绽时间判断来不来得及，远则冲刺斩；时间充裕时用重击）
      if (seen && seen.state === 'parrywhiff' && seen.seq !== this.pwSeq && free) {
        this.pwSeq = seen.seq;
        const left = foe.state === 'parrywhiff' && foe.seq === seen.seq ? PARRY_WHIFF - foe.t : 0;
        const need = MOVES.light1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
        if (left > need - 0.02 && dist <= reach + 1.2 && rnd() < this.punishP) {
          this.guardUntil = 0; H.guard = false;
          if (dist <= reach) {
            if (left > (0.12 + heavySU(0.13 / CHARGE_MAX, me.spd)) / me.spd + 0.03 && rnd() < 0.5 * s) this.heavy(0.13); else this.press('light');
            H.x = 0; return;
          }
          this.dashStrike(now); return;
        }
      }
      // 自己的攻击被挡：高武力预料对手会反击，收招后先举防
      if (me.stats.guarded !== this.lastGuarded) {
        this.lastGuarded = me.stats.guarded;
        const mv = MOVES[me.state];
        const left = mv ? (totalOf(me) - me.t) / (me.state === 'special' ? 1 : me.spd) : 0;
        if (rnd() < 0.1 + 0.7 * s) this.guardUntil = Math.max(this.guardUntil, now + left + 0.18 + rnd() * 0.25);
      }
      // 反击：刚挡下 / 格开的大招还在收招（按招式节奏算好来得及）
      if (this.ctrSeq >= 0 && free) {
        const fm = MOVES[foe.state];
        if (this.ctrSeq === foe.seq && fm && foe.phase === 'recovery') {
          const left = (totalOf(foe) - foe.t) / foe.spd;
          const need = MOVES.light1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
          if (left > need - 0.02 && dist <= reach + 1.1) {
            this.ctrSeq = -1; this.guardUntil = 0; H.guard = false;
            if (dist <= reach) { this.press('light'); H.x = 0; return; }
            this.dashStrike(now); return;
          }
        }
        if (this.ctrSeq !== foe.seq || !fm) this.ctrSeq = -1;
      }
      // 对手被破防：立即追击
      if (foe.state === 'guardbreak' && free && foe.t < foe.stun - MOVES.light1.startup / me.spd) {
        this.guardUntil = 0; H.guard = false;
        if (dist <= reach) { this.press('light'); H.x = 0; return; }
        if (dist <= reach + 1.2 && me.state !== 'guard') { this.dashStrike(now); return; }
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
          if (dist <= reach + 1.2) { this.dashStrike(now); this.aa = 'done'; H.guard = false; return; }
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
      // 看见对手收招破绽（挥空 / 被挡）：抢攻，距离稍远则冲刺斩。识破连按时早有准备，反应更快
      const rd = this.read(foe);
      const seenR = rd > 0 ? sim.seen(foe.idx, this.react * (1 - 0.25 * rd)) : seen;
      if (seenR && seenR.phase === 'recovery' && seenR.state !== 'air' && seenR.seq !== this.punSeq && free && dist <= reach + 1.1) {
        const seen = seenR;
        this.punSeq = seen.seq;
        // 对手收招还剩多少时间（熟知招式节奏）：来不及冲过去就不冒险
        const fm = MOVES[foe.state];
        const left = fm && foe.seq === seen.seq ? (totalOf(foe) - foe.t) / (foe.state === 'special' ? 1 : foe.spd) : 0;
        const need = MOVES.light1.startup / me.spd + (dist <= reach ? 0 : 0.07 + (dist - reach) / 7.4);
        // 对手武力高出越多（收招也快）越常错过
        if (left > need - 0.03 && rnd() < this.punishP * (0.5 + 0.5 * M.clamp(1 + (me.war - foe.war) / 45, 0.3, 1))) {
          this.guardUntil = 0; H.guard = false;
          if (dist <= reach) { this.press('light'); H.x = 0; return; }
          this.dashStrike(now); return;
        }
      }
      // 识破连按：对手一进入其攻击距离就会乱挥 → 预判举防等他打完，或后撤（冲刺）让他挥空，再抓收招
      if (rd > 0 && seen && free && now >= this.mzAt) {
        const foeR = foe.reach * MOVES.light1.reachK + BODY * 0.5;
        const busy = seen.phase === 'recovery' || seen.state === 'hitstun' || seen.state === 'guardstun' || seen.state === 'guardbreak' ||
          seen.state === 'knockdown' || seen.state === 'down' || seen.state === 'getup' || seen.state === 'land';
        if (!busy && dist < foeR + 0.5) {
          this.mzAt = now + 0.3 + rnd() * 0.2;
          const r = rnd(), room = Math.abs(me.x - me.face * 1.6) < ARENA - 0.3;
          if (r < rd * 0.6 && room && me.state !== 'guard') {     // 后撤半步：他够不着照样挥，挥空就抓
            this.guardUntil = 0; H.guard = false;
            this.backUntil = now + 0.14 + rnd() * 0.14; H.x = -me.face; return;
          }
          if (r < rd * 0.735) { this.guardUntil = Math.max(this.guardUntil, now + 0.3 + rnd() * 0.35); }
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
          if (rnd() < this.pokeP) { if (this.holdOff(sim, seen, rd) || this.wary(sim, seen)) return; this.press('light'); H.x = 0; this.planX = 0; return; }
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
        if (this.patSeq !== seen.seq) { this.patSeq = seen.seq; this.patient = rnd() < (0.15 + 0.8 * s) * (1 - 0.4 * this.urg(sim)); }
        if (this.patient) { this.planX = 0; H.x = 0; return; }
      }
      if (now < this.next) {
        // 走进攻击距离即停步，避免贴身
        // （停在蓄力站位上：松开方向后还会滑行一段，提前松开）
        if (this.planX === me.face && (dist <= reach * 0.92 || dist - (me.vx * me.face > 0 ? me.vx * me.vx / (2 * FRICTION) : 0) <= this.stopAt)) this.planX = 0;
        H.x = this.planX;
        return;
      }
      this.next = now + this.period * (0.7 + rnd() * 0.6);
      this.planX = 0; this.stopAt = 0;
      H.x = 0;

      // 对手倒地：退到他起身后轻击够不着、自己满蓄重击够得着处，算准他起身无敌结束的那一刻打到（起身就举防的会被破防，
      // 起身就往前走的正撞上去）；武力低的只会走近了等
      if (foe.state === 'knockdown' || foe.state === 'down' || foe.state === 'getup') {
        const sp = this.chargeSpot(foe);
        // 他起身就格开（惯于见刃光就按、格开届时已好）：少用
        const parryOk = this.foeCd(sim) < this.hittableIn(foe) + 0.1 ? this.hvPar : 0;
        // 每次击倒定一次（不在两个站位之间来回改主意）
        if (foe.state === 'knockdown' && this.kdSeq !== foe.seq) { this.kdSeq = foe.seq; this.okiPick = rnd(); }
        const oki = sp.hi >= sp.lo + 0.04 && this.okiPick < (0.2 + 0.6 * this.gs * (1 - 0.6 * this.hvBad)) * (1 - 0.8 * parryOk);
        if (dist > (oki ? sp.hi : reach * 1.1)) this.planX = me.face;
        else if (dist < (oki ? sp.lo : reach * 0.6)) this.planX = -me.face;
        else if (oki && foe.state !== 'knockdown' && sim.actionable(me) && this.hittableIn(foe) > CHARGE_MAX * 0.75 + 0.27) { this.chgFar = 'oki'; this.heavy(9); }
        else if (!oki && rnd() < 0.3 * s * (1 - 0.6 * parryOk)) this.heavy(0.3 + rnd() * 0.5);   // 走近了蓄一刀等他起身
        H.x = this.planX;
        return;
      }
      // 绝技
      if (me.rage >= 100 && (me.bow ? dist > reach * 0.6 : dist <= reach + 1.8) && rnd() < 0.3 + 0.55 * s) { this.press('special'); return; }
      if (dist > reach * 1.02) {
        if (this.farCharge(sim, seen, dist)) return;
        // 对手缩着举防：停在蓄力的站位上（他够不着、自己的满蓄重击够得着），等蓄力的时机
        // 他惯于见刃光就格开而格开已就绪：改站佯攻的位置（他够不着、自己满蓄也差一点够不着）；看见他的格开在冷却：赶紧上前真打
        const shell = this.shellK(), win = shell > 0 && this.cdWin(sim);
        const sp = shell > 0 && !win && this.wantFeint(sim) ? this.feintSpot(foe) : this.chargeSpot(foe);
        if (shell > 0 && rnd() < (win ? 0.5 + 0.5 * this.gs : Math.min(1, shell * (0.12 + this.gs)) * (1 - this.urg(sim)))) {
          if (dist >= sp.lo && dist <= sp.hi) { this.planX = 0; H.x = 0; return; }
          if (dist > sp.hi && sp.hi >= sp.lo + 0.04) this.stopAt = (sp.lo + sp.hi) / 2;   // 走过去，停在站位上
          else if (dist < sp.lo && Math.abs(me.x - me.face * 0.5) < ARENA - 0.3) { this.planX = -me.face; H.x = this.planX; this.next = now + 0.1 + rnd() * 0.1; return; }
        }
        // 高武力：对手兵器够得着时不贸然闯入，在其攻击距离外踱步，等对手先出手露出破绽
        const foeR = foe.reach + BODY * 0.5 + 0.15;
        const pushy = seen && (seen.state === 'walk' || seen.state === 'dash' || MOVES[seen.state]);
        if (pushy && dist < foeR + 0.5 && rnd() < (0.1 + 0.65 * s) * (1 - 0.6 * this.urg(sim))) {
          this.planX = dist < foeR + 0.12 ? -me.face : 0; H.x = this.planX;
          return;
        }
        // 兵器较短：抓对手收招的破绽，或冲刺切入（冲刺中轻击 = 突进斩）
        const gap = foe.reach - me.reach;
        if (gap > 0.03 && dist < reach + 0.85) {
          const k = Math.min(1, 0.35 + gap / 0.3);
          // 破绽还剩多少（熟知招式节奏与硬直长短）：冲过去来不及就不算破绽
          const lft = foe.state === 'hitstun' || foe.state === 'guardstun' ? foe.stun - foe.t : MOVES[foe.state] && foe.phase === 'recovery' ? (totalOf(foe) - foe.t) / foe.spd : 0;
          const opening = seen && (seen.phase === 'recovery' || seen.state === 'hitstun' || seen.state === 'guardstun') && lft > 0.07 + MOVES.light1.startup / me.spd + Math.max(0, dist - reach) / 7.4;
          if ((opening && rnd() < (0.35 + 0.5 * s) * k) || (rnd() < (0.08 + 0.18 * s) * k && !this.wary(sim, seen, true))) { this.dashStrike(now); return; }
        }
        if (gap > -0.06 && dist < reach + 0.75 && rnd() < 0.05 + 0.1 * s && !this.wary(sim, seen, true)) { this.dashStrike(now); return; }   // 兵器相当：偶尔冲刺抢攻
        if (dist < reach * 1.45 && rnd() < this.whiffP) { this.press('light'); return; }   // 冒失出手
        if (dist > 3.4 && rnd() < 0.22 + 0.2 * s) { H.dash = me.face; return; }
        if (dist > 1.9 && dist < 3.3 && rnd() < 0.05 + 0.05 * (1 - s)) { this.press('up'); H.x = me.face; this.planX = me.face; return; }
        this.planX = me.face; H.x = this.planX;
        return;
      }
      // 贴得太近：长兵器施展不开，拉开距离
      if (dist < reach * 0.55 && rnd() < 0.35 + 0.4 * s) { this.planX = -me.face; H.x = this.planX; this.next = now + 0.18 + rnd() * 0.15; return; }
      // 进入攻击距离：高武力会预判举防
      if (dist <= foe.reach + BODY && rnd() < this.guardP * 0.45 * (1 - 0.5 * this.urg(sim))) { this.guardUntil = now + 0.25 + rnd() * 0.3; H.guard = true; return; }
      // 对手一味举防不出手：退到他轻击够不着处，再蓄满重击破防
      const shell = this.shellK();
      if (shell > 0 && dist < this.chargeSpot(foe).lo && rnd() < shell * this.gs && Math.abs(me.x - me.face * 0.5) < ARENA - 0.3) {
        this.planX = -me.face; H.x = this.planX; this.next = now + 0.12 + rnd() * 0.12; return;
      }
      const st = me.stats;
      const foeTurtles = (st.hits >= 4 && st.guarded / st.hits > 0.45) || (seen && seen.state === 'guard' && foe.state === 'guard');
      // 近身蓄满破防：对手爱试探（蓄力会被打断）、惯于格开（格开已就绪）就少用——临场判断差的弱将想不到
      const jk = Math.min(1, this.gs / 0.6);
      if (foeTurtles && rnd() < (0.25 + 0.4 * s) * (1 - 0.8 * this.hvBad * jk) * (1 - shell * jk) * (this.pokeRate() < 0.5 ? 1 : 1 - this.gs) * (this.cdWin(sim) ? 1 : 1 - 0.9 * this.hvPar * jk)) { this.heavy(CHARGE_MAX + 0.05); return; }
      const r = rnd();
      if (r < this.aggr) {
        if (this.holdOff(sim, seen, rd) || this.wary(sim, seen)) return;
        if (rnd() < 1 - 0.26 * Math.max(0, 1 - 3 * this.gs * this.pokeRisk())) this.press('light');   // 对手爱试探：近身少用重击（蓄力会被打断）
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

  // ============================================================ 测试用脚本玩家 --
  // tests/duel-bots.js（Node）与 tests/duel.html 共用的「模拟玩家」（固定套路，规则随机数走 SG.Random，可复现）：
  //   jumper 反复跳入空中斩 · masher 走近连按轻击（怒气满放绝技）· spam 冷却一好、对手在兵器距离内就按格开，其余时间连按 ·
  //   guard 谨慎的防守反击（反应 0.2 秒：看见来招就挡，抓收招与落空破绽，平时隔一阵试探一下，命中才接连段）·
  //   parry 同 guard，另按人的节奏与反应格开：挨了第二段格开第三段（瞄准有效区间正中，按键误差正态 σ 0.045 秒）、
  //   挡下一击后格开连段的下一击（同上）、看见重击松手（反应 0.22 ± 0.04 秒）就按——只有满蓄重击来得及；得手即反击 ·
  //   heavy 同 parry，但试探时六成改用重击（一半轻点、一半满蓄）：量电脑格开重击的频率
  function scriptBot(kind, idx) {
    idx = idx || 0;
    const c = { held: { x: 0, up: false, light: false, heavy: false, guard: false, special: false, parry: false, dash: 0 }, taps: { light: 0, up: 0, special: 0, heavy: 0, dir: 0, parry: 0 } };
    let nt = 0, pAt = -1, gsSeq = -1, hvSeq = -1, relAt = 0;
    const nextHit = (sim, o, cur) => sim.clock + (Math.max(0, cur.startup + cur.active * 0.6 - o.t) + MOVES[cur.next].startup) / o.spd;
    const gz = () => (rnd() + rnd() + rnd() + rnd() - 2) * 1.732;   // 近似标准正态
    const HRT = 0.22, SD = 0.045;
    c.update = (dt, sim) => {
      const me = sim.f[idx], o = sim.f[1 - idx], d = Math.abs(o.x - me.x), now = sim.clock, H = c.held, T = c.taps;
      if (kind === 'jumper') {
        H.x = me.face;
        if (now < nt) return;
        if (sim.actionable(me) && d < 3.2 && d > 1.2) { T.up++; nt = now + 0.1; }
        else if (me.state === 'jump' && d < me.reach + 0.3) { T.light++; nt = now + 0.1; }
        else if (me.state !== 'jump' && d < me.reach) { T.light++; nt = now + 0.1; }
        return;
      }
      if (kind === 'masher' || kind === 'spam') {
        H.x = d > me.reach * 0.9 ? me.face : 0;
        if (kind === 'spam' && sim.parryReady(me) && d < o.reach + 0.5 && (sim.actionable(me) || me.state === 'guardstun' || me.state === 'hitstun')) T.parry++;
        else if (now > nt && d < me.reach * 1.1) { T.light++; nt = now + 0.12; }
        if (me.rage >= 100) T.special++;
        return;
      }
      const useParry = kind === 'parry' || kind === 'heavy';
      const reach = me.reach * MOVES.light1.reachK + BODY * 0.5 - 0.03;
      if (me.state === 'charge') { H.heavy = now < relAt; H.x = 0; H.guard = false; return; }
      H.heavy = false;
      if (pAt >= 0 && now >= pAt) { T.parry++; pAt = -1; }
      H.x = 0;
      if (me.state === 'parryok' || sim.countering(me)) { H.guard = false; if (d <= reach + 0.05) { if (now > nt) { T.light++; nt = now + 0.08; } } else H.x = me.face; return; }
      if ((me.state === 'light1' || me.state === 'light2') && me.hitIdx > 0 && o.state === 'hitstun') { T.light++; return; }
      if (useParry && me.state === 'hitstun' && me.seq !== gsSeq && o.state === 'light2' && sim.parryReady(me)) {
        gsSeq = me.seq;
        pAt = Math.max(now, nextHit(sim, o, MOVES.light2) - me.parryW * HS_PARRY_K * 0.5 + gz() * SD);
      }
      if (useParry && me.state === 'guardstun' && me.seq !== gsSeq) {
        gsSeq = me.seq;
        if ((o.state === 'light1' || o.state === 'light2') && sim.parryReady(me)) pAt = Math.max(now, nextHit(sim, o, MOVES[o.state]) - me.parryW * 0.5 + gz() * SD);
      }
      // 重击：对手蓄力蓄得久（看得见）才打算格开；松手（刃口闪光）后过了反应时间 HRT ± 0.04 秒才按得下去
      if (useParry && o.state === 'heavy' && o.seq !== hvSeq) {
        hvSeq = o.seq;
        if (o.charge >= 0.6 && sim.parryReady(me) && d < o.reach * MOVES.heavy.reachK + 1.2) {
          const at = now - o.t / o.spd + Math.max(0.12, HRT + 0.04 * gz());
          pAt = pAt >= 0 ? Math.min(pAt, at) : at;
        }
      }
      const seen = sim.seen(o.idx, 0.2);
      if (pAt >= 0) { H.guard = me.state !== 'hitstun'; return; }
      if (seen && seen.atk && d < o.reach + 0.8) { H.guard = true; return; }
      if (seen && o.seq === seen.seq && (seen.phase === 'recovery' || seen.state === 'parrywhiff' || seen.state === 'parried') && d <= reach + 0.1 && sim.actionable(me)) { H.guard = false; T.light++; return; }
      H.guard = false;
      if (me.rage >= 100 && d < reach + 1) { T.special++; return; }
      if (d > reach * 0.95) H.x = me.face;
      else if (now > nt && sim.actionable(me)) {
        if (kind === 'heavy' && rnd() < 0.6) { T.heavy++; H.heavy = true; relAt = now + (rnd() < 0.5 ? 0.13 : 0.78 + rnd() * 0.12); }
        else T.light++;
        nt = now + 0.45 + rnd() * 0.3;
      }
    };
    return c;
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
    // 逐顶点着色的三角形（颜色在面内平滑过渡）
    triV(a, b, c, ca, cb, cc) {
      this._v(a[0], a[1], a[2]); this._v(b[0], b[1], b[2]); this._v(c[0], c[1], c[2]);
      for (const k of [ca, cb, cc]) { const q = C(k); this.c.push(q.r, q.g, q.b); }
    }
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
    } else if (shape === 'oval' || shape === 'round' || shape === 'wood' || shape === 'long' || shape === 'hide') {   // 第三版的天竺长盾、非洲皮盾按木盾 / 椭圆盾画
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
    const nan = look.culture === 'nanman' || look.culture === 'yi' || look.culture === 'seasia' || look.culture === 'india';
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
      c.victory = [[0, I], [0.35, P({ gx: 0.2, gy: 1.42, wa: 200, twist: -0.25, lean: -0.02 })], [0.7, P({ gx: 0.44, gy: 1.36, wa: 93, lean: -0.08, head: -0.12, twist: 0.04, hy: 0.93, cape: 0.2 })], [1.6, P({ gx: 0.46, gy: 1.38, wa: 94, lean: -0.1, head: -0.1, twist: 0.06, hy: 0.93, cape: 0.2 })]];
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
      c.victory = [[0, I], [0.35, D({ gx: 0.25, gy: 1.4, wa: 160, twist: -0.25 })], [0.7, D({ gx: 0.25, gy: 1.86, wa: 86, ox: 0.06, oy: 1.0, lean: -0.12, head: -0.2, hy: 0.93 })], [1.6, D({ gx: 0.26, gy: 1.88, wa: 88, ox: 0.06, oy: 1.0, lean: -0.13, head: -0.18, hy: 0.93 })]];
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
    // 受击方向（DESIGN-V3 §3）：打头 → 仰头后折；打躯干 → 弓身；从背后挨打 → 向前扑
    c.hitBack = pose(I, { hx: 0.1, lean: 0.55, head: 0.42, hy: 0.84, gx: I[PI_.gx] + 0.12, gy: I[PI_.gy] - 0.16, wa: I[PI_.wa] - 30, twist: -0.1, cape: 0.7 });
    // 格开（时间轴为秒）：架势 → 兵器外撩迎击（窗口内保持）；弹刀 → 反击起势；被格开 → 兵器被震飞到肩后、后仰踉跄；落空 → 前倾收势不及
    const pole = style === 'pole';
    const Q = o => pose(I, Object.assign(dual ? { ox: 0.3, oy: 1.12, wa2: 40 } : {}, o));
    const PAR = pole ? Q({ gx: 0.44, gy: 1.32, gz: 0.1, wa: 64, twist: -0.02, lean: 0.14, hy: 0.85, head: 0.04, fLx: 0.42, fRx: -0.42 })
      : dual ? Q({ gx: 0.44, gy: 1.36, wa: 104, ox: 0.46, oy: 1.3, wa2: 66, twist: -0.02, lean: 0.12, hy: 0.85, fLx: 0.42, fRx: -0.42 })
        : Q({ gx: 0.46, gy: 1.4, wa: 96, ox: 0.34, oy: 1.28, oz: -0.04, twist: -0.02, lean: 0.12, hy: 0.85, fLx: 0.42, fRx: -0.42 });
    c.parry = [[0, c.guard], [0.05, PAR], [0.3, pose(PAR, { lean: 0.16, gy: PAR[PI_.gy] - 0.03 })]];
    const RIP = pose(PAR, { gx: PAR[PI_.gx] + 0.06, gy: PAR[PI_.gy] + 0.2, wa: PAR[PI_.wa] + 42, twist: 0.18, lean: 0.06, hy: 0.88, cape: 0.4 });
    c.parryok = [[0, RIP], [PARRY_RIPOSTE, pose(RIP, { wa: RIP[PI_.wa] - 18, lean: 0.2, twist: 0.02 })]];
    const STG = Q({ hx: -0.13, hy: 0.86, lean: -0.44, head: -0.32, twist: -0.8, gx: -0.24, gy: 1.6, wa: pole ? 152 : 165, ox: dual ? -0.05 : 0.12, oy: dual ? 1.5 : 1.26, wa2: 150,
      fLx: 0.14, fLy: 0.14, fRx: -0.5, fRy: 0.08, cape: 0.6 });
    c.parried = [[0, pose(STG, { lean: -0.3, head: -0.2 })], [0.1, STG], [0.4, pose(STG, { lean: -0.36, fLy: 0.08, hy: 0.84 })], [PARRY_STAGGER, pose(I, { lean: -0.05 })]];
    const OVR = Q({ hx: 0.12, hy: 0.8, lean: 0.48, head: 0.16, twist: 0.12, gx: 0.56, gy: 0.98, wa: pole ? -18 : -30, ox: dual ? 0.46 : 0.2, oy: dual ? 0.96 : 1.0, wa2: -28,
      fLx: 0.56, fRx: -0.44 });
    c.parrywhiff = [[0, PAR], [0.1, OVR], [0.28, pose(OVR, { lean: 0.38, hy: 0.82 })], [PARRY_WHIFF, I]];
    c.backstep = Q({ hx: -0.08, hy: 0.88, lean: -0.24, head: -0.04, fLx: 0.42, fLy: 0.2, fRx: -0.18, fRy: 0.12, gx: pole ? 0.12 : 0.24, gy: 1.2, wa: pole ? 52 : 80, twist: -0.4, cape: 0.6 });
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
  // 出招时间 → 相位（0..3）。hold：起手前先定住的时长（满蓄重击举刀亮刃的一顿），之后按原节奏劈下
  function phaseOf(mv, t, hold) {
    if (hold > 0) { if (t < hold) return 0; t -= hold; }
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

  const BLEND = { idle: 0.14, walk: 0.12, guard: 0.06, guardstun: 0.03, hitstun: 0.035, knockdown: 0.07, down: 0.1, getup: 0.08, victory: 0.2, defeat: 0.2, charge: 0.1, dash: 0.06, jump: 0.08, land: 0.04,
    parry: 0.025, parryok: 0.02, parried: 0.02, parrywhiff: 0.05, backstep: 0.05 };

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
      this.parryCol = new THREE.Color('#bfe6ff'); this.ctrCol = new THREE.Color('#ffc850');
      this.ctr = 0;        // 反击窗口剩余（由画面每帧写入，用于金光）
      this.intro = -1;     // ≥0：开场亮相动画计时
      this.V = { S: new THREE.Vector3(), T: new THREE.Vector3(), E: new THREE.Vector3(), O: new THREE.Vector3(), H: new THREE.Vector3(), hint: new THREE.Vector3(), d: new THREE.Vector3(), tmp: new THREE.Vector3() };
      this.tipW = [new THREE.Vector3(), new THREE.Vector3()];
      this.baseW = [new THREE.Vector3(), new THREE.Vector3()];
      this.chestW = new THREE.Vector3();
      // 解算后的关节（身体局部坐标，每帧由 apply() 写入）——第二阶段 js/figure.js 的骨架可直接按此驱动：
      //   位置：pelvis 骨盆 · neck 颈根 · shoulder/elbow/hand R/L · hip/knee/ankle R/L；
      //   旋转：pelvisYaw / torsoYaw / torsoLean / headYaw / headPitch（弧度）；兵器握点与朝向见 this.wpn / this.wpn2 / this.shield
      const v3 = () => new THREE.Vector3();
      this.J = { pelvis: v3(), neck: v3(), shoulderR: v3(), elbowR: v3(), handR: v3(), shoulderL: v3(), elbowL: v3(), handL: v3(),
        hipR: v3(), kneeR: v3(), ankleR: v3(), hipL: v3(), kneeL: v3(), ankleL: v3(), pelvisYaw: 0, torsoYaw: 0, torsoLean: 0, headYaw: 0, headPitch: 0 };
      this.apply(this.cur);
    }

    // 根据逻辑状态选片段并采样（返回目标姿势与混合时长）
    sample(dt) {
      const f = this.f, c = this.clips, out = this.tgt;
      let key = f.state + ':' + f.seq, dur = BLEND[f.state] !== undefined ? BLEND[f.state] : 0.05;
      const mv = MOVES[f.state];
      if (this.intro >= 0) { key = 'intro'; sampleKeys(c.intro, this.intro, out); dur = 0.1; }
      else if (mv && f.state !== 'special') { sampleKeys(c[f.state], phaseOf(mv, f.t, f.state === 'heavy' ? heavySU(f.charge, f.spd) - mv.startup : 0), out); }
      else switch (f.state) {
        case 'special': sampleKeys(f.bow ? c.bowSpecial : c.special, f.t, out); break;
        case 'charge': out.set(c.charge); break;
        case 'guard': out.set(c.guard); break;
        case 'guardstun': out.set(c.guardstun); break;
        case 'guardbreak': out.set(c.guardbreak); break;
        case 'hitstun': out.set(f.hitBack ? c.hitBack : f.hitH === 'high' ? c.hitA : c.hitB); break;
        case 'parry': sampleKeys(c.parry, f.t, out); break;
        case 'parryok': sampleKeys(c.parryok, f.t, out); break;
        case 'parried': sampleKeys(c.parried, f.t * PARRY_STAGGER / Math.max(0.1, f.stun), out); break;
        case 'parrywhiff': sampleKeys(c.parrywhiff, f.t * PARRY_WHIFF / Math.max(0.1, f.whiffT || PARRY_WHIFF), out); break;
        case 'backstep': out.set(c.backstep); break;
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
      // 重击举刀的一顿：兵器再往上提一点、身子后仰蓄势（看得出「要劈下来了」）
      const hold = st === 'heavy' ? heavySU(f.charge, f.spd) - MOVES.heavy.startup : 0;
      if (hold > 0 && f.t < hold) { const u = f.t / hold; P[PI_.gy] += 0.06 * u; P[PI_.lean] -= 0.06 * u; P[PI_.hy] -= 0.03 * u; }
      if (st === 'hitstun' && f.t < 0.12) P[PI_.hx] += Math.sin(f.t * 120) * 0.025 * (1 - f.t / 0.12);
      if (st === 'parried') {   // 被震开：手臂发麻的抖动、脚下不稳
        const k = Math.max(0, 1 - f.t / Math.max(0.1, f.stun));
        P[PI_.gx] += Math.sin(f.t * 70) * 0.02 * k; P[PI_.gy] += Math.cos(f.t * 55) * 0.02 * k; P[PI_.hx] += Math.sin(f.t * 9) * 0.025 * k;
      }
      if (st === 'dash' || st === 'backstep') P[PI_.cape] += 0.4;
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
      // 受击闪光 / 蓄力发光 / 格开架势的冷光 / 反击窗口的金光
      if (this.flash > 0) this.flash = Math.max(0, this.flash - dt * 7);
      let fl = this.flash;
      if (st === 'parry' && f.t <= f.parryW) { fl = Math.max(fl, 0.32 * (1 - f.t / f.parryW) + 0.08); this.mat.flashColor = this.parryCol; }
      else if (this.ctr > 0) { fl = Math.max(fl, 0.14 + 0.1 * Math.sin(this.time * 26)); this.mat.flashColor = this.ctrCol; }
      else if (this.flash > 0) this.mat.flashColor = this.flashColor;
      this.mat.flash = fl;
      let em = 0;
      if (st === 'charge') em = f.charge * 0.25 + (f.charge >= 1 ? 0.12 * (0.5 + 0.5 * Math.sin(this.time * 30)) : 0);
      else if (hold > 0 && f.t < hold + MOVES.heavy.startup) em = 0.2 + 0.25 * f.charge;
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
      const J = this.J;
      J.pelvis.set(hx, hy, 0); J.neck.set(0.01, 0.53, 0).applyMatrix4(this.torso.matrix);
      J.pelvisYaw = tw * 0.35; J.torsoYaw = tw; J.torsoLean = P[PI_.lean]; J.headYaw = -tw * 0.35; J.headPitch = -P[PI_.head] + P[PI_.lean] * 0.35;
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
        const k = sd > 0 ? 'R' : 'L';
        J['shoulder' + k].copy(S); J['elbow' + k].copy(V.E); J['hand' + k].copy(V.H);
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
        const k = sd > 0 ? 'R' : 'L';
        J['hip' + k].copy(Hp); J['knee' + k].copy(V.E); J['ankle' + k].copy(V.H);
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

  // ============================================================ 视图：武将（第三版 · js/figure.js 高精度模型） --
  // 每名武将 = SG.Figure.general(…, { lod: 'high' })（平滑蒙皮网格、按头像外貌生成的面部、层叠衣甲、披风、专属兵器）。
  // 动作取 figure.js 的程序化片段（idle / walk / light1–3 / heavy / block / parry / hit / hitB / knockdown / down / getup /
  // guardbreak / victory / defeat / jump / dash / bowDraw / bowShoot …），由单挑自己的时钟与状态机驱动：
  //   出招按 MOVES 的 起手 / 判定 / 收招 三段映射到片段时间，判定开始的一刻 = 片段里兵器尖端伸得最远（「打到」）的一刻；
  //   状态切换时按骨骼四元数交叉淡化（BLEND 表的时长），任何姿势之间都不跳变。
  // 兵器长度对齐规则判定距离（weaponInfo）：构建时逐片段测量尖端在单挑画面里的前伸距离，按 武将.reach × reachK 缩放兵器
  //（长兵 0.9–1.15 倍，短兵 1–1.5 倍，真三国无双式的大兵器），仍够不到的部分由出招时的前冲步补足（按与对手的实际距离，
  // 只冲到刃口刚好碰到对手为止；起手期间按包络确定性地迈步，判定开始的一帧必定到位）。对手比兵器全伸更近时，
  // 「打到」的一刻改取挥击段里尖端第一次越过对手中线的时刻（contactT），定格时长兵不会整片穿出对手背后。受击闪光、格开架势的冷光、反击窗口的金光、蓄力 / 绝技的发光都叠加在人物自己的材质副本上
  //（fig.setTint：自发光）。
  const FIG_WEAPON = { guandao: 'guandao', snake: 'snake', ji: 'halberd', spear: 'spear', poleblade: 'poleblade', axe: 'axe', bigblade: 'bigblade',
    sword: 'jian', dao: 'dao', shortji: 'dao', mace: 'mace', twinsword: 'swords', twinji: 'twinji', twindao: 'twindao',
    gladius: 'gladius', lance: 'lance', handaxe: 'handaxe', tachi: 'tachi' };
  // 西方 / 草原 / 南亚文化的「长剑」用 spatha 造型
  const FIG_SPATHA = new Set(['roman', 'celt', 'german', 'persia', 'arab', 'kushan', 'sarmatian', 'africa', 'moor']);
  const FIG_SHIELD = { wood: 'wa', round: 'round', scutum: 'scutum', oval: 'oval', pai: 'pai', rattan: 'rattan', long: 'long', hide: 'hide' };
  // 兵器：规则的兵器种类决定流派与判定距离；名将若有同流派的专属兵器（青龙偃月刀、方天画戟、涯角枪……）则用专属造型。
  // 规则的「短戟」是单手兵器：figure.js 没有单支短戟，一般武将用单手环首刀造型（流派一致）；
  // 唯一的例外是 太史慈 的专属「双短戟」（figure.js 的 sig 兵器，双持造型，规则仍按单手计时与判定）
  function figWeaponOf(look, gen) {
    const F = SG.Figure, want = (WEAPONS[look.weapon] || WEAPONS.sword).style;
    let w = FIG_WEAPON[look.weapon] || 'jian';
    if (w === 'jian' && FIG_SPATHA.has(look.culture)) w = 'spatha';
    if (w === 'jian' && look.culture === 'india') w = 'khanda';   // 天竺的长剑是坎达剑
    try {
      const fl = F.lookOf(gen, { culture: look.culture });
      const sig = fl && fl.sig && fl.sig.weapon;
      const def = sig && F.weapons.find(x => x.id === sig);
      if (def && (def.style === want || (want === 'one' && def.style === 'dual' && look.weapon === 'shortji'))) w = sig;
    } catch (e) { /* 用缺省 */ }
    return w;
  }
  // 出招片段的挥击段 [挥起, 劈到]（figure.js 关键帧时刻）；判定开始的一刻在这一段里取尖端前伸最远处
  const FIG_SWING = { light1: [0.08, 0.2], light2: [0.08, 0.21], light3: [0.12, 0.26], heavy: [0.42, 0.56] };
  const FIG_SEC = new Set(['cape1', 'cape2', 'cape3', 'plume1', 'plume2', 'hair1']);   // 弹簧随动的骨（不参与交叉淡化）
  // 前冲步的上限：一般兵器 ≤ 0.55 米；短兵（日耳曼战斧等）按需放宽到 0.9 米，保证判定距离处刃口真的够到
  const FIG_LUNGE_MAX = 0.55, FIG_LUNGE_CAP = 0.9;
  const FIG_BITE = 0.04;           // 近身时「打到」的姿势：尖端刚越过对手身体中线这么多（米）
  let FIG_HEAD_DEPTH = null;
  function figHeadDepth() {
    if (FIG_HEAD_DEPTH) return FIG_HEAD_DEPTH;
    const m = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking });
    // 阴影渲染每帧把物体材质的 map 抄到深度材质上；这里锁成 null（深度不需要贴图，也就不会引用面部贴图）
    Object.defineProperty(m, 'map', { get: () => null, set: () => {}, configurable: true });
    m.name = 'SG.Duel.headDepth';
    return (FIG_HEAD_DEPTH = m);
  }
  const lerpU = (a, b, t) => a + (b - a) * t;
  const ease2 = t => t * t * (3 - 2 * t);

  class FigWarrior {
    constructor(f, team) {
      const F = SG.Figure;
      this.f = f;
      this.look = f.look;
      this.style = f.wpn.style;
      this.figWeapon = figWeaponOf(f.look, f.gen);
      const wdef = F.weapons.find(x => x.id === this.figWeapon);
      const shield = this.style === 'one' && f.look.shield && (!wdef || wdef.style === 'one') ? (FIG_SHIELD[f.look.shield] || 'round') : false;
      const fig = this.fig = F.general(f.gen, { culture: f.look.culture, color: team, lod: 'high', weapon: this.figWeapon, shield });
      this.fstyle = fig.style;
      fig.setTint('#000000', 0);                       // 先建立人物自己的材质副本（闪光不影响同色的另一人）
      // 头部的阴影用不带贴图的深度材质：three.js 的共用深度材质会记住面部贴图，单挑结束释放贴图后，
      // 下一场的阴影渲染可能把这张已释放的贴图重新上传（每场泄漏一张 512² 贴图）
      if (fig.head) fig.head.customDepthMaterial = figHeadDepth();
      this.root = new THREE.Group();
      this.root.name = 'Duelist_' + (f.gen && f.gen.name);
      this.root.add(fig.root);
      fig.root.rotation.y = Math.PI / 2 - TURN;        // 面朝 +Z 的模型 → 面向对手并向镜头侧转 3/4
      this.bones = fig.skeleton.bones;
      this.snapQ = this.bones.map(b => b.quaternion.clone());
      this.snapP = this.bones.map(b => b.position.clone());
      this.blendMask = this.bones.map(b => !FIG_SEC.has(b.name));
      this._q = new THREE.Quaternion();
      this._v = new THREE.Vector3(); this._v2 = new THREE.Vector3();
      this.key = ''; this.blend = 1; this.blendDur = 0.1;
      this.walkPhase = 0; this.time = f.idx * 1.7; this.lungeX = 0; this.turn = TURN;
      this.flash = 0; this.flashColor = new THREE.Color(1, 1, 1);
      this.parryCol = new THREE.Color('#bfe6ff'); this.ctrCol = new THREE.Color('#ffc850'); this.emCol = new THREE.Color('#ffcf8a');
      this.tint = new THREE.Color(); this._tc = new THREE.Color();
      this.ctr = 0; this.intro = -1;
      this.tipW = [new THREE.Vector3(), new THREE.Vector3()];
      this.baseW = [new THREE.Vector3(), new THREE.Vector3()];
      this.chestW = new THREE.Vector3();
      this.wpn2 = fig.weaponMesh2 || null;
      this.extra = [];                                 // 自建的网格（插地的兵器、弓弦、搭箭），dispose 时释放
      this.calibrate();
      // 弓将（黄忠、夏侯渊）：绝技时换弓连射，主兵器插在身旁地上
      this.bowOn = false;
      if (f.bow) this.buildBowKit(team);
      this.opp = null;
      this.update(0, false);
    }

    // 兵器长度与判定距离对齐（见本节说明）
    calibrate() {
      const fig = this.fig, f = this.f, v = this._v;
      fig.root.updateMatrixWorld(true);
      const tipX = (mesh, tip) => { if (!mesh) return -9; v.fromArray(tip).applyMatrix4(mesh.matrixWorld); return v.x; };
      const cal = {};
      const wi = fig.weaponInfo || { tip: [0, 1, 0] };
      const ms = [fig.weaponMesh, fig.weaponMesh2], g = this._v2;
      for (const clip in FIG_SWING) {
        const sw = FIG_SWING[clip], info = fig.clipInfo(clip) || { dur: 0.6 };
        let best = -9, bt = sw[1], gx = 0;
        const samp = [];                               // 起手到挥击段逐帧：[t, 尖端 x, y, 握点 x, y]×两件兵器（未缩放）
        for (let t = clip === 'heavy' ? sw[0] : 0; t <= sw[1] + 1e-6; t += 0.005) {
          fig.setPose(clip, t);
          fig.root.updateMatrixWorld(true);
          const a = tipX(fig.weaponMesh, wi.tip), b = tipX(fig.weaponMesh2, wi.tip);
          const m = a >= b ? fig.weaponMesh : fig.weaponMesh2, x = Math.max(a, b);
          if (t >= sw[0] - 1e-6 && x > best + 1e-4) { best = x; bt = t; gx = m ? v.setFromMatrixPosition(m.matrixWorld).x : 0; }
          const row = [t];
          for (const mm of ms) {
            if (!mm) { row.push(-9, 9, 0, 0); continue; }
            v.fromArray(wi.tip).applyMatrix4(mm.matrixWorld); g.setFromMatrixPosition(mm.matrixWorld);
            row.push(v.x, v.y, g.x, g.y);
          }
          samp.push(row);
        }
        cal[clip] = { tc: bt, te: Math.max(sw[1], bt + 0.02), dur: info.dur, tip: best, grip: gx, target: f.reach * MOVES[clip].reachK, samp };
      }
      // 兵器缩放：使各招的尖端前伸接近判定距离（取平均，限制在合理范围）
      let sum = 0, n = 0;
      for (const k in cal) { const c = cal[k], arm = c.tip - c.grip; if (arm > 0.15) { sum += (c.target - c.grip) / arm; n++; } }
      // 长兵 0.9–1.15 倍；短兵 1 倍起，上限按兵器本身长短（短剑可放大到 1.5 倍，长剑 1.3 倍：真三国无双式的大兵器）
      const pole = this.fstyle === 'pole', len = (fig.weaponInfo && fig.weaponInfo.len) || 0.9;
      const s = M.clamp(n ? sum / n : 1, pole ? 0.9 : 1, pole ? 1.15 : M.clamp(1.12 / len, 1.3, 1.5));
      this.wScale = s;
      for (const m of [fig.weaponMesh, fig.weaponMesh2]) if (m) m.scale.setScalar(s);
      // 前冲上限：短兵放大到上限仍不够长时，按需放宽（最多 FIG_LUNGE_CAP）
      let need = 0;
      for (const k in cal) { const c = cal[k]; need = Math.max(need, c.target - (c.grip + (c.tip - c.grip) * s)); }
      const lmax = this.lungeMax = M.clamp(need, FIG_LUNGE_MAX, FIG_LUNGE_CAP);
      for (const k in cal) {
        const c = cal[k];
        c.tip = c.grip + (c.tip - c.grip) * s;
        c.lunge = M.clamp(c.target - c.tip, 0, lmax);   // 判定距离处仍够不到的部分：出招时前冲补足
        // 逐帧尖端前伸（缩放后，只留到最远处为止），供近身时找「刚好打到」的一刻
        const n = c.samp.findIndex(r => r[0] >= c.tc - 1e-6) + 1 || c.samp.length;
        const T = new Float32Array(n), X = new Float32Array(n), Y = new Float32Array(n);
        for (let i = 0; i < n; i++) {
          const r = c.samp[i];
          T[i] = r[0];
          let bx = -9, by = 9;
          for (let k2 = 0; k2 < 2; k2++) {
            const o = 1 + k2 * 4, x = r[o + 2] + (r[o] - r[o + 2]) * s, y = r[o + 3] + (r[o + 1] - r[o + 3]) * s;
            if (r[o] > -8 && x > bx) { bx = x; by = y; }
          }
          X[i] = bx; Y[i] = by;
        }
        c.samp = { n, T, X, Y };
      }
      cal.air = Object.assign({}, cal.heavy, { target: f.reach * MOVES.air.reachK });
      cal.air.lunge = M.clamp(cal.air.target - cal.heavy.tip, 0, lmax);
      this.cal = cal;
      fig.setPose('idle', 0);
    }

    // 弓与弦（绝技「连珠箭」）
    buildBowKit(team) {
      const F = SG.Figure, fig = this.fig;
      this.bowName = this.f.gen && this.f.gen.name === '黄忠' ? 'huangbow' : 'bow';
      // 插在地上的主兵器（独立网格）
      const pw = F.weapon(this.figWeapon, { color: team });
      pw.visible = false; pw.scale.setScalar(this.wScale);
      this.root.add(pw); this.planted = pw; this.extra.push(pw);
      // 拉开的弓弦（折线，世界坐标随弓与右手）与搭在弦上的箭
      const sg = new THREE.BufferGeometry();
      sg.setAttribute('position', new THREE.BufferAttribute(new Float32Array(9), 3));
      const sm = new THREE.LineBasicMaterial({ color: 0xf0e8d0, fog: true });
      this.bowString = new THREE.Line(sg, sm); this.bowString.frustumCulled = false; this.bowString.visible = false;
      this.extra.push(this.bowString);
      const ag = arrowGeometry(false);
      this.nocked = new THREE.Mesh(ag, envMat()); this.nocked.visible = false; this.nocked.castShadow = true;
      this.extra.push({ geometry: ag, parent: null, isGeo: true });
    }
    setBow(on) {
      if (on === this.bowOn || !this.planted) return;
      this.bowOn = on;
      const fig = this.fig;
      fig.setWeapon(on ? this.bowName : this.figWeapon);
      if (!on) for (const m of [fig.weaponMesh, fig.weaponMesh2]) if (m) m.scale.setScalar(this.wScale);
      this.planted.visible = on;
      if (on) {
        // 长兵刃朝上斜插、短兵插地（在身后偏镜头一侧，不挡住开弓）
        const pole = this.fstyle === 'pole', p = this.planted;
        p.position.set(-0.42, pole ? -0.25 : -0.18, 0.22);
        p.rotation.set(pole ? 0.12 : Math.PI - 0.12, 0, pole ? -0.16 : 0.16);
      }
      // 弓弦与箭放在场景根下（世界坐标）
      const scene = this.root.parent;
      if (scene && this.bowString.parent !== scene) { scene.add(this.bowString); scene.add(this.nocked); }
      this.bowString.visible = this.nocked.visible = false;
    }
    updateBowString() {
      if (!this.bowOn) return;
      const fig = this.fig, wi = fig.weaponInfo, m = fig.weaponMesh;
      if (!m || !wi.nockTop) return;
      m.updateWorldMatrix(true, false);
      const top = this._v.fromArray(wi.nockTop).applyMatrix4(m.matrixWorld), bot = this._v2.fromArray(wi.nockBot).applyMatrix4(m.matrixWorld);
      const a = this.bowString.geometry.attributes.position;
      const hand = fig.sockets.gripR; hand.updateWorldMatrix(true, false);
      const H = TMP.hb || (TMP.hb = new THREE.Vector3()); H.setFromMatrixPosition(hand.matrixWorld);
      const mid = TMP.hm || (TMP.hm = new THREE.Vector3()); mid.addVectors(top, bot).multiplyScalar(0.5);
      // 右手离弓把足够远且与弓同高附近 → 视为拉弦
      const drawn = H.distanceTo(mid) > 0.2 && H.distanceTo(mid) < 0.95;
      const N = drawn ? H : mid;
      a.setXYZ(0, top.x, top.y, top.z); a.setXYZ(1, N.x, N.y, N.z); a.setXYZ(2, bot.x, bot.y, bot.z);
      a.needsUpdate = true;
      this.bowString.visible = true;
      this.nocked.visible = drawn;
      if (drawn) {
        this.nocked.position.copy(H);
        const d = TMP.hd || (TMP.hd = new THREE.Vector3()); d.subVectors(mid, H).normalize();
        this.nocked.quaternion.setFromUnitVectors(TMP.hx || (TMP.hx = new THREE.Vector3(1, 0, 0)), d);
      }
    }

    // 弓将绝技：插刀 → 开弓 → 四连射 → 收弓
    bowSpecialPose(t) {
      const sp = MOVES.special, t0 = sp.startup, h = sp.hits, end = sp.startup + sp.active + sp.recovery;
      this.setBow(t > 0.14 && t < end - 0.22);
      if (t <= 0.14) return ['idle', this.time, 'bs0'];
      if (!this.bowOn) return ['idle', this.time, 'bs9'];
      if (t < t0 + h[0]) return ['bowDraw', Math.min(0.8, (t - 0.14) / (t0 + h[0] - 0.2) * 0.8), 'bs1'];
      for (let i = 0; i < h.length; i++) {
        const T = t0 + h[i], next = i + 1 < h.length ? t0 + h[i + 1] : end;
        if (t < T + 0.06) return ['bowShoot', (t - T) / 0.06 * 0.08, 'bsS' + i];
        if (t < next && i + 1 < h.length) return ['bowDraw', 0.35 + 0.45 * M.clamp01((t - T - 0.06) / Math.max(0.04, next - T - 0.08)), 'bsD' + i];
      }
      return ['bowShoot', 0.08 + Math.min(0.42, (t - t0 - h[h.length - 1] - 0.06) * 1.2), 'bsE'];
    }

    // 近身时的「打到」时刻：对手比兵器全伸的距离近时，取起手 / 挥击段里尖端（在头顶以下）向前越过对手中线的一刻，
    // 定格（打击停顿）时刃口停在对手身上，而不是整片穿出对手背后；够不到中线时仍取全伸的一刻（远处由前冲补足）
    contactT(c) {
      const o = this.opp, S = c.samp;
      if (!o || !S || S.n < 2) return c.tc;
      const want = (o.x - this.f.x) * this.f.face + FIG_BITE;
      if (want >= c.tip - 0.06) return c.tc;
      // 向前挥出时尖端由近及远越过 want 的一刻（长兵架势里尖端本就在前方，先收后刺，取刺出时的那次越过）
      for (let i = 1; i < S.n; i++) {
        if (S.X[i] >= want && S.X[i - 1] < want && S.Y[i] <= 2.0) return lerpU(S.T[i - 1], S.T[i], (want - S.X[i - 1]) / (S.X[i] - S.X[i - 1]));
      }
      return c.tc;
    }

    // 近战绝技：蓄势亮相（高举兵器）→ 突进 → 轻一 / 轻二 / 轻三 / 重击 四连斩（每段的「打到」与规则的命中时刻对齐）
    specialPose(t) {
      const sp = MOVES.special, t0 = sp.startup, h = sp.hits, C = this.cal;
      if (t < SPECIAL_FREEZE) return ['heavy', Math.min(0.42, t / 0.3 * 0.42), 'sp0', null, 0];
      const seq = [['light1', 0], ['light2', 0], ['light3', 0], ['heavy', 0.2]];
      let prev = SPECIAL_FREEZE;
      for (let i = 0; i < h.length; i++) {
        const T = t0 + h[i], [clip, s0] = seq[i], c = C[clip];
        if (t < T) {
          // 提前一帧到达「打到」的姿势：命中时进入下一段（交叉淡化在打击停顿中不推进），定格的正是这一帧
          const u = M.clamp01((t - prev) / Math.max(0.03, T - prev - 1 / 60));
          return [clip, lerpU(s0, this.contactT(c), u), 'sp' + (i + 1), i === 0 ? { lower: 'dash', lowerT: t } : null, i === 0 ? ease2(M.clamp01((u - 0.2) / 0.8)) : 1, c];
        }
        prev = T;
      }
      const c = C.heavy, end = t0 + sp.active + sp.recovery;
      const u = M.clamp01((t - prev) / Math.max(0.05, end - prev));
      return ['heavy', lerpU(c.tc, c.dur, u), 'sp5', null, 1 - u, c];
    }

    // 状态 → [片段, 片段时间, 键, setPose 选项, 前冲包络 0..1, 出招的测量值 cal]
    pick() {
      const f = this.f, C = this.cal, mv = MOVES[f.state];
      let clip = 'idle', t = this.time, key = f.state + ':' + f.seq;
      if (this.intro >= 0) {
        const it = this.intro;
        if (it < 0.55) { clip = 'victory'; t = it / 0.55 * 0.7; key = 'in0'; }
        else if (it < 0.95) { clip = 'block'; t = it; key = 'in1'; }
        else { clip = 'idle'; key = 'idle'; }
        return [clip, t, key, null, 0];
      }
      if (f.state === 'special') return f.bow ? this.bowSpecialPose(f.t).concat([null, 0]) : this.specialPose(f.t);
      if (mv && f.state !== 'air') {
        const c = C[f.state];
        const hold = f.state === 'heavy' ? heavySU(f.charge, f.spd) - mv.startup : 0;
        const ph = phaseOf(mv, f.t, hold);
        const s0 = f.state === 'heavy' ? FIG_SWING.heavy[0] : 0;
        clip = f.state;
        // 「打到」的片段时刻：起手期间按与对手的距离逐帧取（连续变化），进入判定后锁定（打击停顿时姿势不跳）
        if (ph < 1 || this.tcKey !== key) { this.tcKey = key; this.tcHit = this.contactT(c); }
        const tc = this.tcHit;
        // 判定段先贴住「打到」的姿势再加速挥过（命中多在判定开始的一两帧，挥击末段极快，线性推进会在定格前就挥过头）
        t = ph < 1 ? lerpU(s0, tc, f.state === 'heavy' ? ph : ease2(ph) * 0.35 + ph * 0.65) : ph < 2 ? lerpU(tc, c.te, (ph - 1) * (ph - 1)) : lerpU(c.te, c.dur, ease2(ph - 2));
        // 前冲包络：整个起手里迈步（重击在举刀蓄势的停顿之后才迈），判定开始时正好到位（命中定格的一帧刃口贴住对手），收招时退回
        const env = ph < 1 ? ease2(ph) : ph < 2 ? 1 : 1 - ease2(ph - 2);
        return [clip, t, key, null, env, c];
      }
      const jumpT = () => 0.12 + M.clamp01((JUMP_V - f.vy) / (2 * JUMP_V)) * 0.5;
      switch (f.state) {
        case 'air': {
          const c = C.air, ph = phaseOf(MOVES.air, f.t, 0);
          t = ph < 1 ? lerpU(0.36, c.tc, ph) : ph < 2 ? lerpU(c.tc, c.te, (ph - 1) * (ph - 1)) : lerpU(c.te, 0.74, ph - 2);
          return ['heavy', t, key, { lower: 'jump', lowerT: jumpT() }, ph < 1 ? ph : ph < 2 ? 1 : 3 - ph, c];
        }
        case 'walk': return ['idle', this.time, 'walk', { lower: 'walk', lowerT: this.walkPhase / (Math.PI * 2) * 1.0 }, 0];
        case 'guard': return ['block', this.time, 'guard', null, 0];
        case 'guardstun': return ['block', this.time, key, { blend: { clip: 'hit', t: Math.min(0.07, f.t * 1.2), w: 0.72 } }, 0];
        case 'guardbreak': return ['guardbreak', f.t < 0.08 ? f.t / 0.08 * 0.1 : 0.1 + (f.t - 0.08) / Math.max(0.1, f.stun - 0.08) * 0.5, key, null, 0];
        case 'hitstun': {
          clip = f.hitBack || f.hitH !== 'high' ? 'hitB' : 'hit';
          t = f.t < 0.07 ? f.t : 0.07 + (f.t - 0.07) / Math.max(0.08, f.stun - 0.07) * 0.35;
          return [clip, t, key + (f.hitH || ''), null, 0];
        }
        case 'parry': return ['parry', f.t < 0.04 ? f.t / 0.04 * 0.06 : 0.06 + Math.min(0.02, (f.t - 0.04) * 0.12), key, null, 0];
        case 'parryok': return ['parry', 0.08 + Math.min(1, f.t / 0.06) * 0.06 + Math.max(0, f.t - 0.06) * 0.3, key, null, 0];
        case 'parrywhiff': {
          const T = f.whiffT || PARRY_WHIFF, u = f.t / T;
          t = u < 0.25 ? 0.08 + u / 0.25 * 0.06 : 0.14 + (u - 0.25) / 0.75 * 0.22;
          const w = u < 0.25 ? 1 - 0.55 * (u / 0.25) : 0.45 + 0.55 * ease2((u - 0.25) / 0.75);
          return ['parry', t, key, { blend: { clip: 'hitB', t: 0.07, w } }, 0];
        }
        case 'parried': return ['guardbreak', f.t < 0.08 ? f.t / 0.08 * 0.1 : 0.1 + (f.t - 0.08) / Math.max(0.1, f.stun - 0.08) * 0.5, key, null, 0];
        case 'backstep': return ['block', this.time, key, { lower: 'jump', lowerT: 0.1 + f.t / BACKSTEP_T * 0.62 }, 0];
        case 'knockdown': {
          // 估计落地时间：腾空段播完「仰面倒下」（片段 0 → 0.55），落地时正好躺平
          const g = GRAV, rem = f.y > 0 || f.vy > 0 ? (f.vy + Math.sqrt(Math.max(0, f.vy * f.vy + 2 * g * Math.max(0, f.y)))) / g : 0;
          const tot = Math.max(0.2, f.t + rem);
          return ['knockdown', Math.min(0.55, f.t / tot * 0.55), key, null, 0];
        }
        case 'down': return f.ko ? ['down', 0.9 + f.t, key, null, 0] : ['knockdown', 0.55 + Math.min(0.45, f.t), key, null, 0];
        case 'getup': return ['getup', Math.min(0.8, f.t / 0.42 * 0.8), key, null, 0];
        case 'dash': return ['dash', f.t, key, null, 0];
        case 'jump': return ['jump', jumpT(), 'jump', null, 0];
        case 'land': return ['jump', Math.min(0.8, 0.66 + f.t * 1.2), key, null, 0];
        case 'charge': return ['heavy', Math.min(0.42, f.t / 0.16 * 0.42), 'charge', null, 0];
        case 'victory': return ['victory', f.t, key, null, 0];
        case 'defeat': return f.ko ? ['down', 0.9 + f.t, key, null, 0] : ['defeat', f.t, key, null, 0];
        default: return ['idle', this.time, 'idle', null, 0];
      }
    }

    update(dt, frozen) {
      const f = this.f, fig = this.fig, st = f.state;
      this.time += dt;
      if (this.intro >= 0) this.intro += dt;
      if (st === 'walk') this.walkPhase += f.vx * f.face * dt * (Math.PI * 2 / 1.25);
      if (this.bowOn && st !== 'special') this.setBow(false);   // 绝技被打断 / 结束：收弓、拔回主兵器
      const [clip, t, key, opts, env, cal] = this.pick();
      const dur = BLEND[st] !== undefined ? BLEND[st] : 0.05;
      if (key !== this.key) {
        for (let i = 0; i < this.bones.length; i++) { this.snapQ[i].copy(this.bones[i].quaternion); this.snapP[i].copy(this.bones[i].position); }
        this.blend = this.key ? 0 : 1;
        this.blendDur = key.startsWith('sp') || key.startsWith('bs') ? 0.04 : dur;
        this.key = key;
      }
      fig.setPose(clip, t, opts || undefined);
      if (!frozen) this.blend += dt;
      if (this.blend < this.blendDur) {
        const w = ease2(M.clamp01(this.blend / this.blendDur)), B = this.bones, q = this._q;
        for (let i = 0; i < B.length; i++) {
          if (!this.blendMask[i]) continue;
          q.copy(B[i].quaternion);
          B[i].quaternion.slerpQuaternions(this.snapQ[i], q, w);
          B[i].position.lerpVectors(this.snapP[i], this._v.copy(B[i].position), w);
        }
      }
      // 程序化叠加：重击举刀的一顿（后仰蓄势）、蓄力颤动、受击 / 被格开的抖动
      const bn = fig.bones;
      const hold = st === 'heavy' ? heavySU(f.charge, f.spd) - MOVES.heavy.startup : 0;
      if (hold > 0 && f.t < hold) { const u = f.t / hold; bn.spine.rotation.x -= 0.1 * u; }
      let jx = 0, jy = 0;
      if (st === 'charge') { const q = f.charge, n = this.time * 61; jx += Math.sin(n) * 0.008 * q; jy += Math.cos(n * 1.3) * 0.006 * q; }
      if (st === 'hitstun' && f.t < 0.12) jx += Math.sin(f.t * 120) * 0.03 * (1 - f.t / 0.12);
      if (st === 'guardstun' && f.t < 0.1) jx += Math.sin(f.t * 110) * 0.02 * (1 - f.t / 0.1);
      if (st === 'parried') { const k = Math.max(0, 1 - f.t / Math.max(0.1, f.stun)); jx += Math.sin(f.t * 9) * 0.025 * k; bn.foreArmR.rotation.z += Math.sin(f.t * 70) * 0.05 * k; }
      fig.update(frozen ? 0 : dt);
      this.hangCape(st, dt);
      // 前冲：只冲到刃口刚好碰到对手的身体（对手更近时少冲或不冲；够不着时最多冲到判定距离处）
      const o = this.opp;
      let want = 0;
      if (env > 0 && cal && cal.lunge > 0) {
        const dx = o ? (o.x - f.x) * f.face : cal.target + BODY * 0.5;
        want = env * M.clamp(dx - BODY * 0.5 - cal.tip, 0, cal.lunge);
      }
      // 出招中跟住包络（确定性：判定开始的一帧必定到位，打击停顿时也不落后；限速 14 米/秒防止换招时跳变）；
      // 招式被打断 / 换招时先平滑退回，等包络追上当前位置再跟住
      if (key !== this.lungeKey) { this.lungeKey = key; this.lungeLock = false; }
      if (env > 0 && want >= this.lungeX - 0.02) this.lungeLock = true;
      if (this.lungeLock && env > 0) { const mx = 14 * Math.max(dt, 1 / 60); this.lungeX += M.clamp(want - this.lungeX, -mx, mx); }
      else if (!frozen) this.lungeX += (want - this.lungeX) * (1 - Math.exp(-9 * dt));
      // 位置与朝向（root.scale.x = 朝向：两人都以握兵器的右手一侧朝向镜头）；胜利姿势时转身面向镜头亮相
      const turnTo = st === 'victory' ? 1.15 : this.intro >= 0 && this.intro < 0.6 ? 0.75 : TURN;
      this.turn += (turnTo - this.turn) * (1 - Math.exp(-(st === 'victory' ? 3 : 8) * dt));
      fig.root.rotation.y = Math.PI / 2 - this.turn;
      this.root.position.set(f.x + f.face * this.lungeX + jx * f.face, f.y + jy, 0);
      this.root.scale.x = f.face;
      // 受击闪光 / 格开架势的冷光 / 反击窗口的金光 / 蓄力与绝技的发光
      // 第三版修订：着色改为以轮廓为主的辉光（figure.js setTint），受击闪光封顶 0.6、衰减更快；反击窗口为脉动的金色轮廓
      if (this.flash > 0) this.flash = Math.max(0, this.flash - dt * 10);
      const T = this.tint.setRGB(0, 0, 0);
      if (this.flash > 0) T.copy(this.flashColor).multiplyScalar(Math.min(0.6, this.flash * 0.75));
      if (st === 'parry' && f.t <= f.parryW) T.add(this._tc.copy(this.parryCol).multiplyScalar(0.34 * (1 - f.t / f.parryW) + 0.1));
      else if (this.ctr > 0) T.add(this._tc.copy(this.ctrCol).multiplyScalar(0.32 + 0.22 * Math.sin(this.time * 26)));
      let em = 0;
      if (st === 'charge') em = f.charge * 0.22 + (f.charge >= 1 ? 0.1 * (0.5 + 0.5 * Math.sin(this.time * 30)) : 0);
      else if (hold > 0 && f.t < hold + MOVES.heavy.startup) em = 0.16 + 0.2 * f.charge;
      else if (st === 'special' && f.t < SPECIAL_FREEZE + 0.1) em = 0.24 + 0.08 * Math.sin(this.time * 25);
      else if (f.rage >= 100) em = 0.04 + 0.04 * Math.sin(this.time * 8);
      if (em > 0) T.add(this._tc.copy(this.emCol).multiplyScalar(em));
      fig.setTint(T, 1);
      // 世界坐标（刀光拖尾与特效用）
      this.root.updateMatrixWorld(true);
      const wi = fig.weaponInfo || { tip: [0, 1, 0], base: [0, 0.2, 0] };
      const ms = [fig.weaponMesh, fig.weaponMesh2];
      for (let k = 0; k < 2; k++) {
        const m = ms[k];
        if (!m) { if (k === 0) { this.tipW[0].set(f.x, 1.2, 0); this.baseW[0].copy(this.tipW[0]); } continue; }
        this.tipW[k].fromArray(wi.tip).applyMatrix4(m.matrixWorld);
        this.baseW[k].fromArray(wi.base || [0, 0.2, 0]).applyMatrix4(m.matrixWorld);
      }
      bn.chest.getWorldPosition(this.chestW); this.chestW.y += 0.12;
      if (this.bowOn) this.updateBowString();
    }

    flashHit(col, k) { this.flash = k || 0.85; this.flashColor.set(col || '#ffffff'); }

    // 披风下垂：figure.js 的披风随躯干前倾一起扬起（弓身、跪地时会向后平伸成一片）。静止类姿势里限制披风偏离铅垂的角度，
    // 冲刺、突进、劈砍、腾空时放宽（披风可以飘起）
    hangCape(st, dt) {
      const bn = this.fig.bones, c1 = bn.cape1, c3 = bn.cape3;
      if (!c1 || !c3 || !this.fig._hasCape) return;
      const f = this.f, mv = MOVES[st];
      const dyn = st === 'dash' || st === 'backstep' || st === 'jump' || st === 'air' || st === 'knockdown' || st === 'special' || (mv && f.phase !== 'startup') || (st === 'walk' && Math.abs(f.vx) > 1.5);
      const want = dyn ? 0.95 : st === 'down' || st === 'getup' ? 3 : 0.42;
      this.capeMax = this.capeMax === undefined ? want : this.capeMax + (want - this.capeMax) * (1 - Math.exp(-6 * Math.max(dt, 0.016)));
      const R = this.fig.root;
      R.updateMatrixWorld(true);
      const inv = TMP.cinv || (TMP.cinv = new THREE.Matrix4());
      inv.copy(R.matrixWorld).invert();
      const a = c1.getWorldPosition(this._v).applyMatrix4(inv), b = c3.getWorldPosition(this._v2).applyMatrix4(inv);
      const ang = Math.atan2(a.z - b.z, a.y - b.y);   // 0 = 铅垂下垂，π/2 = 向后水平
      if (ang > this.capeMax) c1.rotation.x -= (ang - this.capeMax) * 0.9;
    }

    dispose() {
      if (this.root.parent) this.root.parent.remove(this.root);
      for (const e of this.extra) {
        if (e.isGeo) { e.geometry.dispose(); continue; }
        if (e.parent) e.parent.remove(e);
        if (e.userData && e.userData.dispose) e.userData.dispose();
        else if (e.geometry) e.geometry.dispose();
        if (e.material && e.material !== this.fig.mesh.material && e.isLine) e.material.dispose();
      }
      if (this.nocked && this.nocked.parent) this.nocked.parent.remove(this.nocked);
      this.extra.length = 0;
      this.fig.dispose();
    }
  }

  // 平滑的箭（杆 + 箭镞 + 尾羽），+X 为箭头方向，原点在箭尾
  function arrowGeometry(big) {
    const mb = new SG.MeshBuilder(), L = big ? 1.0 : 0.82, k = big ? 1.25 : 1;
    const V = (x, y, z) => new THREE.Vector3(x, y, z);
    const shaftCol = new THREE.Color('#8a6a40'), head = new THREE.Color('#c8ced8'), fl = new THREE.Color(big ? '#d84030' : '#ece6d6');
    mb.tube([V(0, 0, 0), V(L * 0.5, 0, 0), V(L, 0, 0)], [0.007 * k, 0.007 * k, 0.007 * k], 6, shaftCol);
    mb.tube([V(L, 0, 0), V(L + 0.035 * k, 0, 0), V(L + 0.09 * k, 0, 0)], [0.012 * k, 0.016 * k, 0.001], 6, head);
    for (const a of [0, 2.09, 4.19]) {
      const y = Math.cos(a) * 0.028 * k, z = Math.sin(a) * 0.028 * k, n = V(0, Math.cos(a), Math.sin(a));
      const nn = V(0, -Math.sin(a), Math.cos(a));
      mb.triN(V(0.02, 0, 0), V(0.16 * k, 0, 0), V(0.04, y, z), nn, nn, nn, fl);
      mb.triN(V(0.16 * k, 0, 0), V(0.02, 0, 0), V(0.04, y, z), nn.clone().negate(), nn.clone().negate(), nn.clone().negate(), fl);
      void n;
    }
    return mb.toGeometry();
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

  // 第三版场地：平滑、细致（与新地图同一画风）——
  //   地面：非均匀细分的高度场（场中 0.4 米一格，远处渐疏），平滑法线，SG.Gfx.terrainMaterial 的程序化草地 / 泥土 / 农田 /
  //   沙 / 岩石 / 雪混合（与战略地图同一套着色器）；对决场地是踩实的泥土，四周起伏的草坡，远处是雾中的群山。
  //   景物：平滑树冠的阔叶树 / 松（叶簇明暗与法线起伏）、圆润的岩石、随风摆动的草丛与野花、芦苇、倒木与灌丛、
  //   断枪残旗与弃盾、旗杆（车削）与飘动的帅旗；城墙为砌砖纹理的城墙、城门楼、火把。
  //   两军：js/figure.js 的实例化士兵群（各自的文化衣甲与势力色，待机呼吸，胜方欢呼）；未载入 figure.js 时退回旧的简易士兵。
  //   植被与石块的几何体、材质跨场次共用（不随单挑释放）；地面、远山、景物网格每场释放。
  const VEG_VS = '#include <color_vertex>\n#if defined( USE_INSTANCING_COLOR ) && defined( USE_COLOR )\n\tvColor.xyz = color.xyz * mix( vec3( 1.0 ), instanceColor.xyz, sgLeaf );\n#endif';
  const VEG_VS_PARS = 'attribute float sgLeaf;\nvarying float vSgLeaf;\nvarying vec3 vSgLeafP;\nuniform float sgWindT;\nuniform float sgWind;\n';
  const VEG_VS_BEGIN = '#include <begin_vertex>\n\tvSgLeaf = sgLeaf;\n\tvSgLeafP = transformed;\n#ifdef USE_INSTANCING\n\tvSgLeafP += instanceMatrix[3].xyz * 0.37;\n\tfloat sgPh = instanceMatrix[3].x * 0.7 + instanceMatrix[3].z * 0.5;\n\ttransformed.x += sin( sgWindT * 1.9 + sgPh ) * transformed.y * sgWind;\n\ttransformed.z += cos( sgWindT * 1.4 + sgPh * 1.3 ) * transformed.y * sgWind * 0.55;\n#endif';
  const VEG_FS_PARS = 'varying float vSgLeaf;\nvarying vec3 vSgLeafP;\nfloat sgLN( vec3 p ) { return sin( p.x + sin( p.y * 1.3 ) * 1.2 ) * sin( p.y * 1.1 + sin( p.z * 1.2 ) * 1.2 ) * sin( p.z * 0.9 + sin( p.x * 1.4 ) * 1.2 ); }\n';
  const VEG_FS_COLOR = `#include <color_fragment>
	vec3 sgLq = vSgLeafP * SG_LEAF_F;
	float sgL1 = sgLN( sgLq ), sgL2 = sgLN( sgLq * 2.63 + 5.3 ), sgL3 = sgLN( sgLq * 6.1 + 1.7 );
#ifdef SG_ROCK
	float sgLc = sgL1 * 0.25 + sgL2 * 0.35 + sgL3 * 0.4;
	diffuseColor.rgb *= mix( 1.0, 0.86 + 0.22 * sgLc + 0.1 * sin( vSgLeafP.y * 41.0 + sgL1 * 2.0 ), vSgLeaf );
#else
	float sgLc = sgL1 * 0.5 + sgL2 * 0.32 + sgL3 * 0.18;
	diffuseColor.rgb *= mix( 1.0, 0.84 + 0.34 * sgLc + 0.16 * smoothstep( 0.35, 0.85, sgL2 * 0.6 + sgL3 * 0.4 ), vSgLeaf );
#endif
`;
  const VEG_FS_NORMAL = '#include <normal_fragment_maps>\n\tnormal = normalize( normal + vSgLeaf * SG_LEAF_N * vec3( sgL2 + sgL3 * 0.5, sgL1 * 0.6, sgL3 - sgL1 * 0.4 ) );';
  const VEGM = {};
  // kind：'leaf' 树冠 / 灌丛 · 'rock' 石块 · 'grass' 草丛 / 野花 / 芦苇（随风摆）
  function vegMaterial(kind) {
    if (VEGM[kind]) return VEGM[kind];
    const mat = SG.Gfx.newLowPoly(), base = mat.onBeforeCompile;
    const wind = { value: kind === 'grass' ? 0.22 : kind === 'leaf' ? 0.012 : 0 };
    const time = SG.Gfx.water().uniforms.uTime;
    mat.defines = kind === 'rock' ? { SG_ROCK: '', SG_LEAF_F: '3.2', SG_LEAF_N: '0.3' } : kind === 'leaf' ? { SG_LEAF_F: '3.6', SG_LEAF_N: '0.42' } : { SG_LEAF_F: '14.0', SG_LEAF_N: '0.1' };
    mat.onBeforeCompile = function duelVegCompile(shader, renderer) {
      base.call(this, shader, renderer);
      shader.uniforms.sgWindT = time; shader.uniforms.sgWind = wind;
      shader.vertexShader = VEG_VS_PARS + shader.vertexShader.replace('#include <color_vertex>', VEG_VS).replace('#include <begin_vertex>', VEG_VS_BEGIN);
      shader.fragmentShader = VEG_FS_PARS + shader.fragmentShader.replace('#include <color_fragment>', VEG_FS_COLOR).replace('#include <normal_fragment_maps>', VEG_FS_NORMAL);
    };
    mat.customProgramCacheKey = () => 'sgDuelVeg:' + kind;
    mat.name = 'DuelVeg_' + kind;
    VEGM[kind] = mat;
    return mat;
  }
  // 合并「保持原色」（树干）与「乘实例色、带叶簇细节」（树冠）两部分
  function mergeVeg(keep, tint) {
    const a = keep && keep.count ? keep.toGeometry() : null, b = tint.toGeometry();
    const na = a ? a.getAttribute('position').count : 0, nb = b.getAttribute('position').count;
    const g = new THREE.BufferGeometry();
    for (const name of ['position', 'normal', 'color']) {
      const arr = new Float32Array((na + nb) * 3);
      if (a) arr.set(a.getAttribute(name).array, 0);
      arr.set(b.getAttribute(name).array, na * 3);
      g.setAttribute(name, new THREE.BufferAttribute(arr, 3));
    }
    const lf = new Float32Array(na + nb); lf.fill(1, na);
    g.setAttribute('sgLeaf', new THREE.BufferAttribute(lf, 1));
    g.computeBoundingBox(); g.computeBoundingSphere();
    if (a) a.dispose();
    b.dispose();
    return g;
  }
  const VEGG = {};
  const V3 = (x, y, z) => new THREE.Vector3(x, y, z);
  // 树（高约 1，实例缩放）：broad 圆冠阔叶 · broad2 双团阔叶 · pine 松 · spruce 杉 · shrub 灌丛；名称后缀 F = 远景简化版
  function treeGeo(kind) {
    if (VEGG[kind]) return VEGG[kind];
    const far = kind.endsWith('F'), K = far ? kind.slice(0, -1) : kind;
    const tk = new SG.MeshBuilder(), lf = new SG.MeshBuilder();
    const W = new THREE.Color(0.93, 0.93, 0.93), W2 = new THREE.Color(0.8, 0.82, 0.8), BARK = new THREE.Color(0.36, 0.26, 0.17);
    const crown = (c, r, col, seed, rough) => {
      const ro = rough === undefined ? 0.1 : rough;
      lf.sphere(c, r, col || W, far ? 1 : 2, { seed, rough: ro, bottomShade: 0.34, top: 0.08 });
      for (let k = 0; k < (far ? 2 : 4); k++) {
        const a = seed * 1.7 + k * 1.57, up = 0.2 + 0.32 * ((k + seed) % 2);
        lf.sphere(V3(c.x + Math.cos(a) * r.x * 0.66, c.y + up * r.y, c.z + Math.sin(a) * r.z * 0.66), V3(r.x * 0.48, r.y * 0.5, r.z * 0.48), k % 2 ? W2 : W, far ? 0 : 1, { seed: seed + 7 * k + 1, rough: ro * 0.8, bottomShade: 0.3, top: 0.1 });
      }
    };
    const trunk = (h, r0) => tk.tube([V3(0, 0, 0), V3(0.012, h * 0.5, 0.008), V3(-0.01, h, 0)], [r0, r0 * 0.82, r0 * 0.6], far ? 5 : 9, BARK);
    const tier = (y, r, h, col) => lf.lathe(V3(0, y, 0), far ? [[0, h * 0.1], [r, 0, null, true], [r * 0.45, h * 0.55], [0, h]] : [[0, h * 0.12], [r * 0.9, h * 0.035], [r, 0, null, true], [r * 0.72, h * 0.2], [r * 0.46, h * 0.5], [r * 0.2, h * 0.8], [0, h]], far ? 9 : 16, col || W);
    switch (K) {
      case 'broad': trunk(0.46, 0.06); crown(V3(0, 0.68, 0), V3(0.44, 0.38, 0.44), W, 3); break;
      case 'broad2': trunk(0.42, 0.055); crown(V3(0.06, 0.6, 0), V3(0.38, 0.33, 0.38), W, 5); crown(V3(-0.16, 0.82, 0.05), V3(0.28, 0.25, 0.28), W2, 9); break;
      case 'pine': trunk(0.34, 0.05); tier(0.2, 0.44, 0.6, W2); tier(0.52, 0.34, 0.54, W); tier(0.82, 0.22, 0.46, W); break;
      case 'spruce': trunk(0.22, 0.045); tier(0.12, 0.34, 0.5, W2); tier(0.42, 0.27, 0.46, W); tier(0.72, 0.19, 0.44, W); break;
      default: crown(V3(0, 0.13, 0), V3(0.3, 0.2, 0.3), W, 17, 0.14); break;   // shrub
    }
    return (VEGG[kind] = mergeVeg(tk, lf));
  }
  function rockGeo(v) {
    const key = 'rock' + v;
    if (VEGG[key]) return VEGG[key];
    const mb = new SG.MeshBuilder(), g = new THREE.Color(0.92, 0.92, 0.92);
    const R = [V3(0.34, 0.22, 0.28), V3(0.26, 0.3, 0.24), V3(0.42, 0.15, 0.3)][v];
    mb.sphere(V3(0, R.y * 0.55, 0), R, g, 2, { seed: v * 13 + 5, rough: 0.22, flat: -0.45, top: 0.1, bottomShade: 0.4 });
    if (v === 0) mb.sphere(V3(0.27, 0.07, 0.12), V3(0.14, 0.1, 0.12), g, 1, { seed: 77, rough: 0.2, flat: -0.4, bottomShade: 0.4 });
    return (VEGG[key] = mergeVeg(null, mb));
  }
  // 草丛：八片弯曲的草叶（叶根暗、叶尖亮，两面）；野花：花茎 + 五瓣花（花瓣乘实例色）；芦苇：高而细的叶与穗
  function tuftGeo(reed) {
    const key = reed ? 'reed' : 'tuft';
    if (VEGG[key]) return VEGG[key];
    const mb = new SG.MeshBuilder(), rnd = SG.SeededRandom(reed ? 19 : 91);
    const dark = new THREE.Color(0.64, 0.64, 0.62), lite = new THREE.Color(1.06, 1.06, 1.0);
    const n = reed ? 7 : 6;
    for (let k = 0; k < n; k++) {
      const a = k * 0.8 + rnd.nextDouble() * 0.6, ca = Math.cos(a), sa = Math.sin(a);
      const r0 = 0.02 + rnd.nextDouble() * 0.05, h = reed ? 0.7 + rnd.nextDouble() * 0.5 : 0.13 + rnd.nextDouble() * 0.12, lean = reed ? 0.06 + rnd.nextDouble() * 0.08 : 0.05 + rnd.nextDouble() * 0.07, w = reed ? 0.016 : 0.026;
      const bx = ca * r0, bz = sa * r0;
      // 两段弯曲的叶片
      const b0 = V3(bx - sa * w, 0, bz + ca * w), b1 = V3(bx + sa * w, 0, bz - ca * w);
      const m = V3(bx + ca * lean * 0.35, h * 0.55, bz + sa * lean * 0.35), m0 = V3(m.x - sa * w * 0.6, m.y, m.z + ca * w * 0.6), m1 = V3(m.x + sa * w * 0.6, m.y, m.z - ca * w * 0.6);
      const tip = V3(bx + ca * lean, h, bz + sa * lean);
      const nf = V3(ca * 0.3, 1, sa * 0.3).normalize(), nb = V3(-ca * 0.3, 1, -sa * 0.3).normalize();
      const mid = new THREE.Color(0.8, 0.8, 0.78);
      for (const nn of [nf, nb]) {
        mb.triN(b0, b1, m1, nn, nn, nn, dark, dark, mid); mb.triN(b0, m1, m0, nn, nn, nn, dark, mid, mid);
        mb.triN(m0, m1, tip, nn, nn, nn, mid, mid, lite);
      }
      if (reed && k % 3 === 0) mb.sphere(V3(tip.x, tip.y, tip.z), V3(0.018, 0.07, 0.018), new THREE.Color(0.62, 0.48, 0.32), 0, { rough: 0 });
    }
    return (VEGG[key] = mergeVeg(null, mb));
  }
  function flowerGeo() {
    if (VEGG.flower) return VEGG.flower;
    const stem = new SG.MeshBuilder(), pet = new SG.MeshBuilder(), up = V3(0, 1, 0), green = new THREE.Color(0.3, 0.5, 0.2);
    for (const [x, z, h] of [[0, 0, 0.17], [0.07, 0.04, 0.13], [-0.05, 0.06, 0.15], [0.02, -0.06, 0.12]]) {
      stem.triN(V3(x - 0.01, 0, z), V3(x + 0.01, 0, z), V3(x, h, z), V3(0, 0.3, 1), V3(0, 0.3, 1), up, green);
      stem.triN(V3(x + 0.01, 0, z), V3(x - 0.01, 0, z), V3(x, h, z), V3(0, 0.3, -1), V3(0, 0.3, -1), up, green);
      const r = 0.035;
      for (let k = 0; k < 5; k++) {
        const a0 = k * Math.PI * 2 / 5, a1 = a0 + Math.PI * 2 / 5;
        pet.triN(V3(x, h, z), V3(x + Math.cos(a0) * r, h + 0.006, z + Math.sin(a0) * r), V3(x + Math.cos(a1) * r, h + 0.006, z + Math.sin(a1) * r), up, up, up, new THREE.Color(1, 1, 1));
      }
      pet.triN(V3(x, h + 0.012, z), V3(x + 0.012, h + 0.01, z), V3(x, h + 0.01, z + 0.012), up, up, up, new THREE.Color(1.0, 0.9, 0.4));
    }
    return (VEGG.flower = mergeVeg(stem, pet));
  }
  // 地面材质：地形着色器（顶点版），高度换算为「海拔」（场地 y = 0 不当作海滩，远山山顶才见岩石）
  let GROUNDM = null;
  function groundMaterial() {
    if (GROUNDM) return GROUNDM;
    const mat = SG.Gfx.terrainMaterial(), base = mat.onBeforeCompile;
    mat.onBeforeCompile = function duelGroundCompile(shader, renderer) {
      base.call(this, shader, renderer);
      shader.fragmentShader = shader.fragmentShader.replace('float sgH = vSgW.y;', 'float sgH = 1.2 + vSgW.y * 0.32;');
    };
    mat.customProgramCacheKey = () => 'sgDuelGround';
    mat.name = 'DuelGround';
    GROUNDM = mat;
    return mat;
  }
  // 城墙砖石：竖直面为错缝砖（0.64 × 0.32 米），平顶为方砖；只作用于低饱和度的石色
  let BRICKM = null;
  function brickMaterial() {
    if (BRICKM) return BRICKM;
    const mat = SG.Gfx.newLowPoly(), base = mat.onBeforeCompile;
    mat.onBeforeCompile = function duelBrickCompile(shader, renderer) {
      base.call(this, shader, renderer);
      shader.vertexShader = 'varying vec3 vSgBW;\nvarying vec3 vSgBN;\n' + shader.vertexShader.replace('#include <project_vertex>',
        '#include <project_vertex>\n\tvSgBW = ( modelMatrix * vec4( transformed, 1.0 ) ).xyz;\n\tvSgBN = normalize( mat3( modelMatrix ) * objectNormal );');
      shader.fragmentShader = 'varying vec3 vSgBW;\nvarying vec3 vSgBN;\nfloat sgBH( vec2 p ) { return fract( sin( dot( p, vec2( 127.1, 311.7 ) ) ) * 43758.5453 ); }\n' +
        shader.fragmentShader.replace('#include <color_fragment>', `#include <color_fragment>
	{
		vec3 c = diffuseColor.rgb, n = normalize( vSgBN ), p = vSgBW;
		float mx = max( max( c.r, c.g ), c.b ), mn = min( min( c.r, c.g ), c.b );
		float stone = clamp( 1.0 - ( ( mx - mn ) / max( mx, 0.001 ) - 0.22 ) * 3.0, 0.0, 1.0 );
		float k = 1.0;
		if ( abs( n.y ) < 0.5 ) {
			float u = abs( n.x ) > abs( n.z ) ? p.z : p.x;
			float row = floor( p.y / 0.21 ), uu = u / 0.44 + mod( row, 2.0 ) * 0.5;
			float fy = fract( p.y / 0.21 ), fu = fract( uu );
			float mort = 1.0 - smoothstep( 0.03, 0.09, min( fy, 1.0 - fy ) ) * smoothstep( 0.015, 0.045, min( fu, 1.0 - fu ) );
			float h = sgBH( vec2( floor( uu ), row ) );
			float wear = 0.5 + 0.5 * sin( p.x * 0.7 + sin( p.y * 1.3 ) * 2.0 ) * sin( p.y * 0.9 + p.x * 0.21 );
			k = ( 0.84 + 0.22 * h ) * ( 1.0 - 0.32 * mort ) * ( 0.9 + 0.14 * wear ) * ( 0.74 + 0.26 * smoothstep( 0.0, 2.2, p.y ) );
			// 墙根青苔（第三版修订）
			float moss = ( 1.0 - smoothstep( 0.0, 1.4, p.y + wear * 0.6 ) ) * stone;
			diffuseColor.rgb = mix( diffuseColor.rgb, diffuseColor.rgb * vec3( 0.72, 0.86, 0.6 ), moss * 0.6 );
		} else {
			vec2 g = p.xz / 0.8; vec2 f = abs( fract( g ) - 0.5 );
			k = ( 0.9 + 0.12 * sgBH( floor( g ) ) ) * ( 0.84 + 0.16 * ( 1.0 - smoothstep( 0.44, 0.49, max( f.x, f.y ) ) ) );
		}
		diffuseColor.rgb *= mix( 1.0, k, stone );
	}
`);
    };
    mat.customProgramCacheKey = () => 'sgDuelBrick';
    mat.name = 'DuelBrick';
    BRICKM = mat;
    return mat;
  }
  const ss = (a, b, x) => { const t = M.clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); };

  class Arena {
    // 背景两军的士兵群（预烘焙动作用）
    static crowdSpecs(kind, cA, cB) {
      const out = [], seen = new Set();
      for (const c of [cA, cB]) for (const type of ['spear', 'sword']) {
        const k = c + type;
        if (!seen.has(k) && SG.Figure && SG.Figure.cultures.includes(c)) { seen.add(k); out.push({ type, culture: c, seed: 0 }); }
      }
      return out;
    }
    constructor(scene, kind, colA, colB, glyphA, glyphB, seed, culA, culB) {
      this.scene = scene;
      this.kind = kind;
      this.pal = PALETTES[kind];
      this.group = new THREE.Group();
      this.group.name = 'DuelArena';
      scene.add(this.group);
      this.geos = []; this.mats = []; this.texs = [];
      this.banners = []; this.torches = []; this.flags = [];
      this.rnd = SG.SeededRandom(seed || 7);
      this.cul = [culA || 'han', culB || 'han'];
      const pal = this.pal;
      scene.background = C(pal.fog);
      scene.fog = new THREE.Fog(C(pal.fog), pal.fogN, pal.fogF);
      // 天空
      const sm = new THREE.ShaderMaterial({
        uniforms: { uTop: { value: C(pal.top).clone() }, uHor: { value: C(pal.hor).clone() }, uLow: { value: C(pal.low).clone() }, uSun: { value: C(pal.sun).clone() }, uSunDir: { value: new THREE.Vector3().fromArray(pal.sunDir).normalize() } },
        vertexShader: SKY_VS, fragmentShader: SKY_FS, side: THREE.BackSide, depthWrite: false, depthTest: false, fog: false,
      });
      const sg = new THREE.SphereGeometry(300, 32, 16);
      this.geos.push(sg); this.mats.push(sm);
      this.sky = new THREE.Mesh(sg, sm);
      this.sky.renderOrder = -1000; this.sky.frustumCulled = false;
      this.group.add(this.sky);
      this.buildGround();
      this.buildFarRidge();
      this.inst = {};                       // 实例化景物：种类 → [{ x, y, z, s, yaw, sx?, sy?, col }]
      const mb = new SG.MeshBuilder();      // 杂项（Unity 坐标：z 取反）
      this.buildProps(mb, colA, colB);
      this.flushInstances();
      if (mb.count > 0) this.addMesh(mb.toGeometry(), envMat(), true, true);
      this.buildArmies(colA, colB);
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
    // ---------------------------------------------------------- 地形 --
    noise(x, z) {
      const P = M.perlinNoise;
      return P(x * 0.045 + 11, z * 0.045 + 5) * 0.6 + P(x * 0.11 + 3, z * 0.11 + 9) * 0.3 + P(x * 0.27 + 7, z * 0.27 + 1) * 0.1;
    }
    height(x, z) {
      const kind = this.kind, ax = Math.abs(x);
      const lane = (1 - ss(2.4, 5.5, Math.abs(z))) * (1 - ss(13, 19, ax));
      const n = this.noise(x, z) - 0.47;
      const amp = kind === 'hill' ? 2.6 : kind === 'forest' ? 0.9 : kind === 'castle' ? 0.25 : kind === 'river' ? 0.7 : 1.1;
      let h;
      if (z < 0) {
        const b = Math.max(0, -z - 3);
        h = n * 2.6 * amp * Math.min(1, b / 7) + b * 0.035 * amp;
        // 远处的群山（雾中）
        const far = ss(38, 95, b) * (8 + 18 * M.perlinNoise(x * 0.018 + 5, 3.3) + 9 * M.perlinNoise(x * 0.05 + 1, 7.1)) * (kind === 'hill' ? 1.7 : kind === 'castle' ? 0.6 : 1);
        h += far;
        if (kind === 'river') {
          // 河道：z ∈ (−26, −3.4) 低于地面，两岸缓坡
          const bed = ss(-3.0, -4.6, z) * (1 - ss(-24.5, -27.5, z));
          h = h * (1 - bed) - 0.85 * bed;
        }
        if (kind === 'castle') h *= ss(13.5, 16, -z) * 0.3 + (1 - ss(1, 9, -z)) * 0.7;
      } else {
        const f = Math.max(0, z - 3);
        h = f * 0.05 * amp + n * 0.7 * amp * Math.min(1, f / 3);
      }
      h += Math.max(0, ax - 17) * 0.05 * amp;
      return h * (1 - lane);
    }
    buildGround() {
      const pal = this.pal, kind = this.kind;
      const axis = (lo, hi, c0, c1, fine, coarse, grow) => {
        const out = [];
        for (let v = lo; v < hi;) {
          out.push(v);
          const d = v < c0 ? c0 - v : v > c1 ? v - c1 : 0;
          v += Math.min(coarse, fine + d * grow);
        }
        out.push(hi);
        return out;
      };
      const xs = axis(-115, 115, -13, 13, 0.5, 8, 0.14), zs = axis(-150, 22, -6, 7, 0.5, 8, 0.12);
      const nx = xs.length, nz = zs.length, N = nx * nz;
      const pos = new Float32Array(N * 3), col = new Float32Array(N * 3), mat = new Float32Array(N * 4);
      const gc = C(pal.ground), gc2 = C(pal.ground2), lane = C(pal.lane);
      const tc = new THREE.Color(), t2 = new THREE.Color();
      const P = M.perlinNoise;
      let i = 0;
      for (let iz = 0; iz < nz; iz++) {
        for (let ix = 0; ix < nx; ix++, i++) {
          const x = xs[ix], z = zs[iz], y = this.height(x, z);
          pos[i * 3] = x; pos[i * 3 + 1] = y; pos[i * 3 + 2] = z;
          tc.copy(gc).lerp(gc2, M.clamp01(P(x * 0.09 + 2, z * 0.09) * 1.3 - 0.15));
          // 对决场地：踩实的泥土（边缘带噪声，两头逐渐过渡到草）
          const lk = (1 - ss(1.0 + P(x * 0.3, 7) * 1.4, 3.4 + P(x * 0.21, 3) * 1.5, Math.abs(z))) * (1 - ss(11, 16, Math.abs(x)));
          tc.lerp(lane, lk * 0.92);
          let sand = 0, rockT = 0, snow = 0, farm = 1;
          if (kind === 'hill') { rockT = 0.55; snow = ss(17, 26, y); }
          if (kind === 'river') { const bank = (1 - ss(-2.6, -4.2, z)) * ss(-1.0, -2.6, z) + ss(-23.5, -26.5, z) * (1 - ss(-27, -30, z)); sand = Math.max(sand, bank * 0.9); if (z < -3.2 && z > -26.8) sand = 1; }
          if (kind === 'castle') { t2.set(pal.lane); tc.lerp(t2, 0.4 * (1 - ss(6, 12, -z))); }
          // 远处的农田（平原、河川）
          if (kind === 'plain' && z < -30 && z > -75) farm = 1 - 0.8 * ss(0.42, 0.6, P(x * 0.03 + 9, z * 0.03 + 4) + ss(-30, -40, z) * 0.25 - 0.2);
          col[i * 3] = tc.r; col[i * 3 + 1] = tc.g; col[i * 3 + 2] = tc.b;
          mat[i * 4] = sand; mat[i * 4 + 1] = rockT; mat[i * 4 + 2] = snow; mat[i * 4 + 3] = farm;
        }
      }
      const idx = new Uint32Array((nx - 1) * (nz - 1) * 6);
      let k = 0;
      for (let iz = 0; iz < nz - 1; iz++) for (let ix = 0; ix < nx - 1; ix++) {
        const a = iz * nx + ix, b = a + 1, c = a + nx, d = c + 1;
        idx[k++] = a; idx[k++] = c; idx[k++] = b; idx[k++] = b; idx[k++] = c; idx[k++] = d;
      }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
      g.setAttribute('color', new THREE.BufferAttribute(col, 3));
      g.setAttribute('aMat', new THREE.BufferAttribute(mat, 4));
      g.setIndex(new THREE.BufferAttribute(idx, 1));
      g.computeVertexNormals();
      g.computeBoundingSphere();
      this.ground = this.addMesh(g, groundMaterial(), false, true);
      this.groundTris = idx.length / 3;
    }
    // 天际线：最远处一道雾中的山脊（平滑带状网格）
    buildFarRidge() {
      const pal = this.pal, kind = this.kind, n = 160, rows = 4;
      const pos = [], col = [], idx = [];
      const c0 = C(pal.far), c1 = C(pal.mid);
      const hgt = kind === 'hill' ? 46 : kind === 'castle' ? 22 : 30;
      for (let i = 0; i <= n; i++) {
        const x = -330 + 660 * i / n;
        const crest = hgt * (0.45 + 0.75 * M.perlinNoise(x * 0.008 + 3, 1.7) + 0.25 * M.perlinNoise(x * 0.03, 4.2));
        const z = -185 + Math.sin(x * 0.012) * 18;
        for (let r = 0; r <= rows; r++) {
          const u = r / rows, y = -8 + (crest + 8) * (1 - (1 - u) * (1 - u));
          pos.push(x, y, z + u * 6);
          const c = c1.clone().lerp(c0, 0.5 + 0.5 * u);
          col.push(c.r, c.g, c.b);
        }
      }
      for (let i = 0; i < n; i++) for (let r = 0; r < rows; r++) {
        const a = i * (rows + 1) + r, b = a + 1, c = a + rows + 1, d = c + 1;
        idx.push(a, c, b, b, c, d);
      }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
      g.setIndex(idx);
      g.computeVertexNormals();
      const m = envMat();
      this.addMesh(g, m, false, false);
    }
    // ---------------------------------------------------------- 景物 --
    place(kind, x, z, s, opt) {
      (this.inst[kind] || (this.inst[kind] = [])).push(Object.assign({ x, y: this.height(x, z), z, s, yaw: this.rnd.nextDouble() * Math.PI * 2 }, opt || {}));
    }
    flushInstances() {
      const m4 = new THREE.Matrix4(), q = new THREE.Quaternion(), p = new THREE.Vector3(), sc = new THREE.Vector3(), e = new THREE.Euler(), c = new THREE.Color();
      for (const kind in this.inst) {
        const L = this.inst[kind];
        if (!L.length) continue;
        const geo = kind.startsWith('rock') ? rockGeo(+kind.slice(4)) : kind === 'tuft' || kind === 'reed' ? tuftGeo(kind === 'reed') : kind === 'flower' ? flowerGeo() : treeGeo(kind);
        const mat = vegMaterial(kind.startsWith('rock') ? 'rock' : kind === 'tuft' || kind === 'reed' || kind === 'flower' ? 'grass' : 'leaf');
        const im = new THREE.InstancedMesh(geo, mat, L.length);
        L.forEach((o, i) => {
          p.set(o.x, o.y - (o.sink || 0) * o.s, o.z);
          q.setFromEuler(e.set(o.tilt || 0, o.yaw, 0));
          sc.set(o.s * (o.sx || 1), o.s * (o.sy || 1), o.s * (o.sx || 1));
          im.setMatrixAt(i, m4.compose(p, q, sc));
          im.setColorAt(i, c.set(o.col || '#ffffff'));
        });
        im.instanceMatrix.needsUpdate = true;
        if (im.instanceColor) im.instanceColor.needsUpdate = true;
        im.castShadow = kind.startsWith('rock');
        im.receiveShadow = true;
        im.computeBoundingSphere();
        im.name = 'Duel_' + kind;
        this.group.add(im);
        this.instMeshes = this.instMeshes || [];
        this.instMeshes.push(im);
      }
    }
    tree(x, z, s, pine) {
      const r = this.rnd, leaf = C(this.pal.leaf), j = (r.nextDouble() - 0.5) * 0.22;
      const kind = (pine ? (r.nextDouble() < 0.5 ? 'pine' : 'spruce') : (r.nextDouble() < 0.5 ? 'broad' : 'broad2')) + (z < -22 ? 'F' : '');
      const cc = leaf.clone().multiplyScalar(1 + j);
      this.place(kind, x, z, s * (pine ? 5.2 : 5.6), { col: '#' + cc.getHexString(), sink: 0.01 });
    }
    rock(x, z, s, col) {
      const c = C(col || this.pal.rock).clone().multiplyScalar(0.95 + this.rnd.nextDouble() * 0.15);
      this.place('rock' + Math.floor(this.rnd.nextDouble() * 3), x, z, s * 2.4, { col: '#' + c.getHexString(), sink: 0.08, sy: 0.85 + this.rnd.nextDouble() * 0.4 });
    }
    grass(n, zMin, zMax, xr, density) {
      const r = this.rnd, pal = this.pal;
      const g0 = C(pal.leaf), g1 = C(pal.ground2);
      for (let i = 0; i < n; i++) {
        const x = (r.nextDouble() * 2 - 1) * xr, z = zMin + r.nextDouble() * (zMax - zMin);
        const laneK = (1 - ss(1.6, 3.6, Math.abs(z))) * (1 - ss(11, 15, Math.abs(x)));
        if (r.nextDouble() < laneK * (density || 0.97)) continue;          // 场中踩实，少草
        if (z > 2.5 && (Math.abs(x) < 4 || r.nextDouble() < 0.5)) continue;     // 镜头近处稀疏（近大远小，免得草叶挡住画面）
        if (z > 2.2) continue;                                                // 第三版修订：镜头前最近的一带不放草（不再是画面下缘的大黑草叶）
        const k = r.nextDouble();
        const c = g0.clone().lerp(g1, k * 0.55).multiplyScalar(0.95 + r.nextDouble() * 0.3);
        this.place('tuft', x, z, (1.1 + r.nextDouble() * 1.1) * (z > 2.5 ? 0.7 : 1), { col: '#' + c.getHexString() });
        if (r.nextDouble() < 0.08) {
          const fc = ['#f4f0e4', '#f2d24a', '#d8483a', '#b07ad8', '#f0a0c0'][Math.floor(r.nextDouble() * 5)];
          this.place('flower', x + (r.nextDouble() - 0.5) * 0.6, z + (r.nextDouble() - 0.5) * 0.6, 1.6 + r.nextDouble() * 0.8, { col: fc });
        }
      }
    }
    buildProps(mb, colA, colB) {
      const r = this.rnd, kind = this.kind, pal = this.pal;
      const scatter = (n, fn, zMin, zMax, xr) => {
        for (let i = 0; i < n; i++) {
          const x = (r.nextDouble() * 2 - 1) * (xr || 30), z = zMin + r.nextDouble() * (zMax - zMin);
          if (Math.abs(z) < 2.4 && Math.abs(x) < 12) continue;
          if (z > 2.2 && Math.abs(x) < 5) continue;          // 镜头前方中央留空
          fn(x, z, r.nextDouble());
        }
      };
      const U = (x, y, z) => new THREE.Vector3(x, y, -z);   // MeshBuilder 的 Unity 坐标
      const wood = C('#5a3a22'), steel = C('#b8c0ca');
      // 断枪残旗、弃盾（平原、丘陵）
      const debris = (n, z0, z1) => {
        for (let i = 0; i < n; i++) {
          const x = (r.nextDouble() * 2 - 1) * 11, z = z0 - r.nextDouble() * (z0 - z1);
          const a = (r.nextDouble() - 0.5) * 0.7, y0 = this.height(x, z) - 0.15;
          const L = 1.5 + r.nextDouble() * 0.6;
          const top = U(x + Math.sin(a) * L, y0 + Math.cos(a) * L, z);
          mb.tube([U(x, y0, z), U(x + Math.sin(a) * L * 0.5, y0 + Math.cos(a) * L * 0.5, z), top], [0.024, 0.022, 0.02], 7, wood);
          mb.lathe(top, [[0.022, 0], [0.034, 0.05], [0, 0.26]], 8, steel);
          if (i % 3 === 0) {
            const fc = C(i % 2 ? colA : colB), fx = top.x, fy = top.y - 0.1;
            const p0 = U(fx, fy, z), p1 = U(fx + 0.5, fy - 0.1, z + 0.03), p2 = U(fx + 0.46, fy - 0.45, z + 0.05), p3 = U(fx, fy - 0.38, z);
            const n0 = V3(0, 0, 1);
            mb.triN(p0, p1, p2, n0, n0, n0, fc); mb.triN(p0, p2, p3, n0, n0, n0, fc);
            mb.triN(p0, p2, p1, n0.clone().negate(), n0.clone().negate(), n0.clone().negate(), fc); mb.triN(p0, p3, p2, n0.clone().negate(), n0.clone().negate(), n0.clone().negate(), fc);
          }
        }
        for (let i = 0; i < 2; i++) {   // 弃盾（斜倒在地上的圆盾：铜边、盾面、铜钉）
          const x = (r.nextDouble() * 2 - 1) * 9, z = -3.4 - r.nextDouble() * 2.6, y = this.height(x, z);
          const c = C(i % 2 ? colA : colB).clone().multiplyScalar(0.7), rim = C('#8a6a3a'), boss = C('#c9a24e');
          const sb = new SG.MeshBuilder();
          sb.lathe(V3(0, 0, 0), [[0.36, -0.01, rim], [0.37, 0.02, rim, true], [0.33, 0.04, c], [0.18, 0.07, c], [0.08, 0.08, boss], [0.05, 0.12, boss], [0, 0.13, boss]], 20, c);
          const geo = sb.toGeometry();
          geo.rotateX(-1.25); geo.rotateY(r.nextDouble() * 6.28); geo.translate(x, y + 0.2, z);
          this.addMesh(geo, envMat(), true, true);
        }
      };
      switch (kind) {
        case 'plain':
          this.grass(900, -14, 8, 24);
          scatter(16, (x, z, k) => this.rock(x, z, 0.12 + k * 0.2), -14, 6);
          debris(9, -2.8, -6.4);
          for (let i = 0; i < 16; i++) this.tree((r.nextDouble() * 2 - 1) * 42, -17 - r.nextDouble() * 20, 0.85 + r.nextDouble() * 0.55, r.nextDouble() < 0.35);
          for (let i = 0; i < 10; i++) this.place('shrub', (r.nextDouble() * 2 - 1) * 30, -9 - r.nextDouble() * 9, 3 + r.nextDouble() * 2, { col: '#' + C(pal.leaf).clone().multiplyScalar(0.9 + r.nextDouble() * 0.25).getHexString() });
          break;
        case 'forest':
          for (let i = 0; i < 70; i++) {
            const x = (r.nextDouble() * 2 - 1) * 44, z = -5.5 - r.nextDouble() * 34;
            if (z > -11 && Math.abs(x) < 5.5 && r.nextDouble() < 0.6) continue;
            this.tree(x, z, 0.9 + r.nextDouble() * 0.7, r.nextDouble() < 0.55);
          }
          for (const x of [-11.5, -9.2, 9.6, 12.2]) this.tree(x, -3.8 - r.nextDouble(), 1.1 + r.nextDouble() * 0.25, x > 0);
          this.grass(800, -12, 8, 22, 0.9);
          for (let i = 0; i < 26; i++) {
            const x = (r.nextDouble() * 2 - 1) * 26, z = -3.4 - r.nextDouble() * 9;
            if (Math.abs(z) < 3 && Math.abs(x) < 12) continue;
            this.place('shrub', x, z, 2.2 + r.nextDouble() * 1.6, { col: '#' + C(pal.leaf).clone().multiplyScalar(0.95 + r.nextDouble() * 0.3).getHexString() });
          }
          scatter(10, (x, z, k) => this.rock(x, z, 0.15 + k * 0.25, '#6a6a5a'), -10, 5);
          {   // 倒木（树干 + 断枝）
            const y = this.height(-3, -4.6) + 0.18;
            mb.tube([U(-4.6, y, -4.4), U(-3.2, y + 0.05, -4.6), U(-1.4, y + 0.02, -4.9)], [0.24, 0.22, 0.17], 12, C('#5a4030'));
            mb.tube([U(-3.0, y + 0.12, -4.6), U(-2.7, y + 0.6, -4.3)], [0.06, 0.03], 7, C('#5a4030'));
          }
          break;
        case 'hill':
          for (let i = 0; i < 14; i++) this.rock((r.nextDouble() * 2 - 1) * 30, -6 - r.nextDouble() * 12, 0.5 + r.nextDouble() * 1.2);
          for (const [x, z, s] of [[-19, -17, 4.2], [-10, -22, 5.5], [15, -18, 5], [24, -24, 6], [3, -28, 5]]) this.rock(x, z, s, '#6a655c');
          scatter(26, (x, z, k) => this.rock(x, z, 0.1 + k * 0.2), -10, 6);
          for (let i = 0; i < 20; i++) this.tree((r.nextDouble() * 2 - 1) * 42, -12 - r.nextDouble() * 24, 0.75 + r.nextDouble() * 0.5, true);
          this.grass(650, -12, 8, 22, 0.95);
          debris(5, -2.8, -6);
          break;
        case 'river': {
          for (let i = 0; i < 120; i++) {
            const x = (r.nextDouble() * 2 - 1) * 34, z = -2.9 - r.nextDouble() * 1.4;
            this.place('reed', x, z, 1.0 + r.nextDouble() * 0.6, { col: r.nextDouble() < 0.5 ? '#a8a86a' : '#86985a' });
          }
          for (let i = 0; i < 40; i++) this.place('reed', (r.nextDouble() * 2 - 1) * 40, -25.5 - r.nextDouble() * 1.6, 1.1 + r.nextDouble() * 0.5, { col: '#8a9a5c' });
          scatter(30, (x, z, k) => this.rock(x, z, 0.08 + k * 0.14), -3, 6);
          for (let i = 0; i < 26; i++) this.tree((r.nextDouble() * 2 - 1) * 52, -28 - r.nextDouble() * 12, 1 + r.nextDouble() * 0.6, r.nextDouble() < 0.4);
          this.grass(650, -2.6, 8, 24);
          const wg = new THREE.PlaneGeometry(160, 24, 40, 8);
          wg.rotateX(-Math.PI / 2);
          wg.translate(0, -0.22, -15);
          const wp = wg.attributes.position, wc = new Float32Array(wp.count * 3);
          for (let i = 0; i < wp.count; i++) { const z = wp.getZ(i); wc[i * 3] = M.clamp01(1 - Math.min(-3.4 - z, z + 26.6) / 5); }
          wg.setAttribute('color', new THREE.BufferAttribute(wc, 3));
          this.geos.push(wg);
          const water = new THREE.Mesh(wg, SG.Gfx.water());
          water.receiveShadow = false;
          this.group.add(water);
          break;
        }
        case 'castle': {
          // 城墙（砖石纹理）、垛口、马面、城门与门钉、城门楼（两重檐）、角楼、火把
          const stone = C('#7a7266'), z = -12.5, H = 6.6;
          const wall = new SG.MeshBuilder();
          wall.box(U(0, H / 2, z), V3(110, H, 2.4), stone);
          for (let x = -55; x <= 55; x += 1.4) wall.box(U(x, H + 0.35, z + 1.0), V3(0.8, 0.7, 0.4), stone.clone().multiplyScalar(1.05));
          for (const x of [-14, 14]) wall.box(U(x, H / 2 + 0.7, z + 0.2), V3(4.4, H + 1.4, 3.2), stone.clone().multiplyScalar(0.96));
          // 第三版修订：墙身每隔约 3 米一道扶壁（墙面有进深）
          for (let x = -48; x <= 48; x += 3.2) { if (Math.abs(x) < 4.6 || Math.abs(Math.abs(x) - 14) < 2.8) continue; wall.box(U(x, H * 0.42, z + 1.45), V3(0.7, H * 0.84, 0.55), stone.clone().multiplyScalar(0.93)); }
          wall.box(U(0, 0.05, z + 1.4), V3(6.0, 0.1, 1.2), stone.clone().multiplyScalar(0.9));
          this.addMesh(wall.toGeometry(), brickMaterial(), true, true);
          // 城门：拱形门洞（车削半圆）+ 门板与门钉
          mb.box(U(0, 1.9, z + 1.25), V3(3.2, 3.8, 0.1), C('#2a1a10'));
          for (const yy of [0.7, 1.4, 2.1, 2.8, 3.4]) for (const xx of [-1.1, -0.37, 0.37, 1.1]) mb.sphere(U(xx, yy, z + 1.32), V3(0.06, 0.06, 0.04), C('#b8963c'), 1, {});
          mb.box(U(0, 4.0, z + 1.27), V3(3.8, 0.34, 0.14), C('#4a3a2a'));
          mb.box(U(0, H + 1.2, z), V3(8.5, 2.4, 3.4), C('#7a2418'));
          for (const x of [-3.7, -1.25, 1.25, 3.7]) mb.lathe(U(x, H, z + 1.72), [[0.15, 0], [0.14, 2.4]], 10, C('#5a1810'));
          mb.chineseRoof(U(0, H + 2.4, z), 10.8, 5.0, 2.8, C('#262a36'));
          mb.box(U(0, H + 3.5, z), V3(5.6, 1.2, 2.4), C('#7a2418'));
          mb.chineseRoof(U(0, H + 4.1, z), 7.4, 3.6, 2.4, C('#262a36'));
          for (const x of [-14, 14]) mb.chineseRoof(U(x, H + 1.4, z + 0.2), 5.4, 4.0, 1.8, C('#262a36'));
          // 第三版修订：城头垂下的长幡（两军颜色），远处再一道城内的屋脊（视差层次）
          for (const [x, cc] of [[-8.2, colA], [-5.6, colB], [5.6, colA], [8.2, colB]]) {
            const c0 = C(cc || '#a02a24');
            mb.box(U(x, H - 1.5, z + 1.28), V3(0.95, 2.9, 0.05), c0);
            mb.box(U(x, H - 3.0, z + 1.29), V3(0.95, 0.12, 0.06), c0.clone().multiplyScalar(0.6));
            mb.box(U(x, H - 0.06, z + 1.29), V3(1.15, 0.1, 0.1), C('#3a2a1a'));
          }
          for (const x of [-30, -20, 22, 32]) mb.chineseRoof(U(x, H + 0.6, z - 6), 7.0, 4.0, 2.0, C('#2a2e38'));
          for (const x of [-3.6, 3.6]) {
            mb.tube([U(x, 0, -9.6), U(x, 1.2, -9.6), U(x, 2.4, -9.6)], [0.07, 0.065, 0.06], 8, C('#2a1e14'));
            mb.lathe(U(x, 2.4, -9.6), [[0.06, 0], [0.2, 0.18], [0.22, 0.26], [0.16, 0.28]], 12, C('#2a2424'));
            this.torches.push(new THREE.Vector3(x, 2.85, -9.6));
          }
          scatter(20, (x, zz, k) => this.rock(x, zz, 0.08 + k * 0.14), -8, 6);
          this.grass(350, -8, 8, 22, 1);
          break;
        }
      }
    }
    // ------------------------------------------------------ 两军阵列 --
    buildArmies(colA, colB) {
      const r = this.rnd, kind = this.kind;
      const blocks = kind === 'castle'
        ? [[1, -10, -4.5, -12.2, -12.8, 6.6, 2, 5], [1, 4.5, 10, -12.2, -12.8, 6.6, 2, 5], [0, -17, -9, -8.2, -10.4, 0, 3, 6]]
        : [[0, -15.5, -8.5, -11, -14, 0, 3, 6], [1, 8.5, 15.5, -11, -14, 0, 3, 6]];
      const cols = [colA, colB];
      if (!SG.Figure || !SG.Figure.crowd) {
        // 旧版：简易士兵（Unity 坐标的 MeshBuilder）
        const mb = new SG.MeshBuilder();
        for (const [s, x0, x1, z0, z1, y, rows, n] of blocks)
          for (let i = 0; i < rows; i++) for (let j = 0; j < n; j++) {
            const x = x0 + (x1 - x0) * j / (n - 1) + (r.nextDouble() - 0.5) * 0.4, z = z0 + (z1 - z0) * i / Math.max(1, rows - 1);
            SG.Models.soldier(mb, new THREE.Vector3(x, y || this.height(x, z), -z), SG.Gfx.color(cols[s]), true, 3.3);
          }
        this.addMesh(mb.toGeometry(), envMat(), true, true);
        return;
      }
      const crowd = this.crowd = SG.Figure.crowd({ lod: 'low', shadow: false });
      this.group.add(crowd.root);
      this.troops = [[], []];
      for (const [s, x0, x1, z0, z1, y0, rows, n] of blocks)
        for (let i = 0; i < rows; i++) for (let j = 0; j < n; j++) {
          const x = x0 + (x1 - x0) * j / (n - 1) + (r.nextDouble() - 0.5) * 0.35, z = z0 + (z1 - z0) * i / Math.max(1, rows - 1) + (r.nextDouble() - 0.5) * 0.3;
          const type = (i + j) % 3 === 2 ? 'sword' : 'spear';
          const id = crowd.add({ type, culture: this.cul[s], color: cols[s], seed: (i * 7 + j) % 3 });
          const y = y0 || this.height(x, z);
          crowd.setTransform(id, x, y, z, Math.atan2(-x * 0.5, -z) , 1);
          crowd.play(id, 'idle', { phase: r.nextDouble(), speed: 0.85 + r.nextDouble() * 0.3 });
          this.troops[s].push(id);
        }
    }
    // 胜方欢呼
    cheer(side, t) {
      if (!this.crowd || this.cheered) return;
      this.cheered = true;
      for (const id of this.troops[side] || []) this.crowd.play(id, 'cheer', { t0: t - this.rnd.nextDouble() * 0.4, speed: 0.9 + this.rnd.nextDouble() * 0.25 });
    }
    addBanner(x, z, poleH, w, h, color, glyph, pennant, phase) {
      const tex = bannerTexture(SG.UI && SG.UI.cssColor ? SG.UI.cssColor(color) : color, glyph, pennant);
      this.texs.push(tex);
      const mat = new THREE.MeshLambertMaterial({ map: tex, side: THREE.DoubleSide });
      this.mats.push(mat);
      const g = new THREE.PlaneGeometry(w, h, 10, 8);
      g.translate(w / 2, -h / 2, 0);
      this.geos.push(g);
      const cloth = new THREE.Mesh(g, mat);
      cloth.castShadow = false; cloth.receiveShadow = false;
      const y0 = this.height(x, z);
      cloth.position.set(x + 0.05, y0 + poleH - 0.1, z);
      this.group.add(cloth);
      const mb = new SG.MeshBuilder(), U = (px, py, pz) => new THREE.Vector3(px, py, -pz);
      const wood = C('#4a2c1a');
      mb.lathe(U(x, y0 - 0.1, z), [[0.06, 0], [0.05, 0.3], [0.045, poleH * 0.5], [0.04, poleH + 0.2]], 10, wood);
      mb.tube([U(x, y0 + poleH - 0.05, z), U(x + w * 0.5, y0 + poleH - 0.05, z), U(x + w + 0.05, y0 + poleH - 0.05, z)], [0.025, 0.025, 0.025], 6, wood);
      mb.lathe(U(x, y0 + poleH + 0.1, z), [[0.05, 0], [0.075, 0.08], [0.05, 0.16], [0, 0.42]], 10, C(GOLD));
      mb.sphere(U(x, y0 + poleH + 0.02, z), V3(0.09, 0.11, 0.09), C(RED), 1, { rough: 0.1 });
      this.addMesh(mb.toGeometry(), envMat(), true, false);
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
      if (this.crowd) this.crowd.setTime(t);
    }
    dispose() {
      for (const g of this.geos) g.dispose();
      for (const m of this.mats) m.dispose();
      for (const t of this.texs) t.dispose();
      for (const im of this.instMeshes || []) im.dispose();
      if (this.crowd) this.crowd.dispose();
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
  // 刀光拖尾（第三版修订）：每次推入新样本时，在上一样本与新样本之间按「绕刃根旋转」插入细分样本（刃尖走弧线而不是弦），
  // 不再是几段长直线拼成的白色纸扇；横向四列顶点：刃根一侧透明 → 中段淡 → 刃口亮 → 最外一道近白的亮芯；按样本年龄淡出
  class Trail {
    constructor(n) {
      this.n = n; this.base = []; this.tip = []; this.life = [];
      for (let i = 0; i < n; i++) { this.base.push(new THREE.Vector3()); this.tip.push(new THREE.Vector3()); this.life.push(0); }
      this.count = 0;
      const verts = n * 4;
      this.posA = new Float32Array(verts * 3); this.colA = new Float32Array(verts * 3);
      const idx = [];
      for (let i = 0; i < n - 1; i++) for (let c = 0; c < 3; c++) { const a = i * 4 + c, b = a + 1, d = a + 5, e = a + 4; idx.push(a, b, d, a, d, e); }
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
      this._d0 = new THREE.Vector3(); this._d1 = new THREE.Vector3(); this._q = new THREE.Quaternion(); this._qi = new THREE.Quaternion();
      this._b = new THREE.Vector3(); this._t = new THREE.Vector3();
    }
    _shift(b, t, life) {
      const B = this.base, T = this.tip, L = this.life, last = this.n - 1;
      const rb = B[last], rt = T[last];
      for (let i = last; i > 0; i--) { B[i] = B[i - 1]; T[i] = T[i - 1]; L[i] = L[i - 1]; }
      B[0] = rb.copy(b); T[0] = rt.copy(t); L[0] = life;
      this.count = Math.min(this.n, this.count + 1);
    }
    push(b, t) {
      // 定格（顿帧、暂停）时刃口不动：不推入重复样本（否则拖尾会塌成一条线）
      if (this.count > 0 && this.base[0].distanceToSquared(b) < 1e-8 && this.tip[0].distanceToSquared(t) < 1e-8) return;
      if (this.on && this.count > 0 && this.life[0] > 0) {
        const b0 = this.base[0], t0 = this.tip[0];
        const d0 = this._d0.subVectors(t0, b0), d1 = this._d1.subVectors(t, b);
        const l0 = d0.length(), l1 = d1.length();
        if (l0 > 1e-4 && l1 > 1e-4) {
          d0.multiplyScalar(1 / l0); d1.multiplyScalar(1 / l1);
          const ang = Math.acos(M.clamp(d0.dot(d1), -1, 1));
          const ns = Math.min(7, Math.max(1, Math.ceil(ang / 0.09)));
          if (ns > 1) {
            this._q.setFromUnitVectors(d0, d1);
            const p0 = b0.clone(), dd = d0.clone();
            for (let s = 1; s < ns; s++) {
              const f = s / ns;
              this._qi.identity().slerp(this._q, f);
              this._b.lerpVectors(p0, b, f);
              this._t.copy(dd).applyQuaternion(this._qi).multiplyScalar(l0 + (l1 - l0) * f).add(this._b);
              this._shift(this._b, this._t, 1);
            }
          }
        }
      }
      this._shift(b, t, this.on ? 1 : 0);
    }
    update(dt) {
      let any = false;
      for (let i = 0; i < this.n; i++) { this.life[i] = Math.max(0, this.life[i] - dt * 3.2); if (this.life[i] > 0) any = true; }
      const c = this.color, P = this.posA, Cc = this.colA;
      const W = [0.12, 0.55, 0.9, 1.0], K = [0, 0.18, 0.8, 1.0];
      for (let i = 0; i < this.n; i++) {
        const age = i / (this.n - 1), k = this.life[i] * Math.pow(1 - age, 0.8);
        const B = this.base[i], T = this.tip[i];
        for (let q = 0; q < 4; q++) {
          const w = W[q], o = (i * 4 + q) * 3;
          P[o] = B.x + (T.x - B.x) * w; P[o + 1] = B.y + (T.y - B.y) * w; P[o + 2] = B.z + (T.z - B.z) * w;
          const kk = k * K[q];
          if (q === 3) { const wh = 0.55 * (1 - age); Cc[o] = (c.r * (1 - wh) + wh) * kk; Cc[o + 1] = (c.g * (1 - wh) + wh) * kk; Cc[o + 2] = (c.b * (1 - wh) + wh) * kk; }
          else { Cc[o] = c.r * kk; Cc[o + 1] = c.g * kk; Cc[o + 2] = c.b * kk; }
        }
      }
      this.geometry.attributes.position.needsUpdate = true;
      this.geometry.attributes.color.needsUpdate = true;
      this.geometry.setDrawRange(0, any && this.count > 1 ? (Math.min(this.count, this.n) - 1) * 18 : 0);
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
.sgd-btns{right:calc(var(--sar) + 1.3*var(--u));width:calc(var(--b)*3.5);height:calc(var(--b)*2.3);pointer-events:none;}
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
.sgd-tb.pa{border-color:rgba(150,210,255,.8);background:radial-gradient(circle at 50% 35%,rgba(70,110,160,.6),rgba(10,18,34,.62));color:#e6f4ff;}
.sgd-tb.pa .cd{position:absolute;inset:-6px;border-radius:50%;pointer-events:none;
  background:conic-gradient(#9fd8ff calc(var(--p,1)*360deg),rgba(255,255,255,.12) 0);
  -webkit-mask:radial-gradient(farthest-side,transparent calc(100% - 5px),#000 calc(100% - 4px));mask:radial-gradient(farthest-side,transparent calc(100% - 5px),#000 calc(100% - 4px));}
.sgd-tb.pa:not(.ready){color:rgba(230,244,255,.45);border-color:rgba(150,210,255,.3);}
.sgd-tb.pa.ready .cd{background:#bfe6ff;box-shadow:0 0 .6em rgba(160,215,255,.9);}
.sgd-tb.pa.flash{animation:sgd-paflash .45s ease-out;}
.sgd-pchip{position:relative;display:inline-grid;place-items:center;flex:none;width:calc(1.75*var(--u));height:calc(1.75*var(--u));border-radius:50%;
  font:700 max(10px,calc(.95*var(--u)))/1 ${KAI};color:rgba(230,244,255,.5);background:rgba(10,16,30,.7);}
.sgd-pchip::before{content:"";position:absolute;inset:-2px;border-radius:50%;background:conic-gradient(#9fd8ff calc(var(--p,1)*360deg),rgba(255,255,255,.14) 0);
  -webkit-mask:radial-gradient(farthest-side,transparent calc(100% - 3px),#000 calc(100% - 2px));mask:radial-gradient(farthest-side,transparent calc(100% - 3px),#000 calc(100% - 2px));}
.sgd-pchip.ready{color:#e6f4ff;box-shadow:0 0 .45em rgba(160,215,255,.75);}
.sgd-pchip.ready::before{background:#bfe6ff;}
.sgd-pchip.ctr{color:#2a1600;background:radial-gradient(circle,#fff2b0,#ffb020);box-shadow:0 0 .8em rgba(255,200,80,1);animation:sgd-blink .18s ease-in-out infinite alternate;}
.sgd-pchip.ctr::before{background:#ffd860;}
.sgd.sgd-compact .sgd-pchip{width:max(24px,calc(2.3*var(--u)));height:max(24px,calc(2.3*var(--u)));font-size:max(12px,calc(1.15*var(--u)));background:rgba(8,14,28,.85);}
.sgd.sgd-compact .sgd-pchip::before{inset:-3px;background:conic-gradient(#c4e8ff calc(var(--p,1)*360deg),rgba(255,255,255,.22) 0);
  -webkit-mask:radial-gradient(farthest-side,transparent calc(100% - 4px),#000 calc(100% - 3px));mask:radial-gradient(farthest-side,transparent calc(100% - 4px),#000 calc(100% - 3px));}
.sgd.sgd-compact .sgd-pchip.ready::before{background:#dff3ff;}
.sgd.sgd-compact .sgd-pchip.ctr::before{background:#ffd860;}
.sgd-k.pk{background:linear-gradient(180deg,#d8f0ff,#8cc4f0);box-shadow:0 2px 0 #2a5a80,0 3px 3px rgba(0,0,0,.4);}
.sgd-k.pk.cool{opacity:.4;}
.sgd-dmg.lbl.parry{font-size:calc(3.4*var(--u));color:#fff2c8;letter-spacing:.14em;text-shadow:0 .06em 0 #6a1a0a,0 0 .35em rgba(255,150,40,.75),0 .1em .3em rgba(0,0,0,.85),0 0 1px #000;}
.sgd-edge{position:fixed;inset:0;pointer-events:none;opacity:0;background:radial-gradient(ellipse at center,rgba(255,230,160,0) 55%,rgba(255,214,120,.55) 85%,rgba(255,250,230,.9) 100%);}
.sgd-dmg.lbl.ctr{font-size:calc(2.1*var(--u));color:#ffd860;text-shadow:0 2px 0 #4a1a00,0 0 .45em rgba(255,170,40,.9);}
.sgd-dmg.lbl.miss{color:#c8c0d8;font-size:calc(1.25*var(--u));}
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
.sgd-chip.pa{background:rgba(40,70,110,.85);border-color:rgba(150,210,255,.85);}
.sgd-vs{position:absolute;inset:0;display:flex;align-items:flex-end;justify-content:center;gap:calc(3*var(--u));pointer-events:none;padding-bottom:calc(var(--sab) + 4.5*var(--u));}
/* VS 卡放在下三分之一、垫一层暗色渐变：不压住上方亮相的 3D 武将，武力 / 兵器一行也有底 */
.sgd-vs::before{content:"";position:absolute;left:0;right:0;bottom:0;height:min(58%,calc(30*var(--u)));pointer-events:none;
  background:linear-gradient(0deg,rgba(8,6,10,.78) 0%,rgba(8,6,10,.5) 45%,rgba(8,6,10,0) 100%);animation:sgd-vsbg .4s ease-out both;}
.sgd-vs .x{align-self:center;margin-bottom:calc(3.2*var(--u));}
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
  .sgd-card{--u:12px;padding:calc(.9*var(--u)) calc(1.3*var(--u));width:min(94vw,calc(64*var(--u)));}
  .sgd-card .sgd-chip{font-size:max(11px,.9em);}
  .sgd-card h2{margin-bottom:.3em;}
  .sgd-card .vs{margin-bottom:.4em;}
  .sgd-ann{top:30%;}
  .sgd-vs{padding-bottom:calc(var(--sab) + 2.2*var(--u));}
  .sgd-vs .p .sgd-port{width:calc(7.6*var(--u));height:calc(7.6*var(--u));}
  .sgd-vs .x{font-size:calc(4.4*var(--u));margin-bottom:calc(2.4*var(--u));}
}
@keyframes sgd-paflash{0%{transform:scale(1.25);box-shadow:0 0 1.6em rgba(160,220,255,1)}100%{transform:scale(1);box-shadow:none}}
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
@keyframes sgd-vsbg{from{opacity:0}to{opacity:1}}
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
      } else if (kind === 'parry') {
        // 格开：金铁交鸣——几个不谐和的正弦泛音长长回响，加一声高频刮擦与低沉的震响
        for (const [fq, a, d] of [[1870, 0.3, 0.95], [2795, 0.2, 0.7], [4120, 0.12, 0.45], [1240, 0.16, 0.6], [210, 0.35, 0.18]]) {
          const o = ctx.createOscillator(), og = ctx.createGain();
          o.type = fq < 400 ? 'triangle' : 'sine';
          o.frequency.setValueAtTime(fq, t); o.frequency.exponentialRampToValueAtTime(fq * (fq < 400 ? 0.6 : 0.985), t + d);
          og.gain.setValueAtTime(0.0001, t); og.gain.exponentialRampToValueAtTime(a * vol, t + 0.003); og.gain.exponentialRampToValueAtTime(0.0001, t + d);
          o.connect(og); og.connect(ctx.destination); o.start(t); o.stop(t + d + 0.02);
          o.onended = () => { try { o.disconnect(); og.disconnect(); } catch (e) { /* 忽略 */ } };
        }
        f.type = 'highpass'; f.frequency.value = 3200;
        g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.32 * vol, t + 0.002); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.12);
        src.start(t, 0.2, 0.14);
      } else if (kind === 'shing') {
        // 重击起手的预兆：刃口一声尖锐的「铮」
        const o = ctx.createOscillator(), og = ctx.createGain();
        o.type = 'sine'; o.frequency.setValueAtTime(2400, t); o.frequency.exponentialRampToValueAtTime(3300, t + 0.12);
        og.gain.setValueAtTime(0.0001, t); og.gain.exponentialRampToValueAtTime(0.12 * vol, t + 0.01); og.gain.exponentialRampToValueAtTime(0.0001, t + 0.3);
        o.connect(og); og.connect(ctx.destination); o.start(t); o.stop(t + 0.32);
        o.onended = () => { try { o.disconnect(); og.disconnect(); } catch (e) { /* 忽略 */ } };
        f.type = 'bandpass'; f.Q.value = 3; f.frequency.setValueAtTime(3500, t); f.frequency.exponentialRampToValueAtTime(8000, t + 0.15);
        g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.25 * vol, t + 0.02); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.18);
        src.start(t, 0.4, 0.2);
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
    KeyJ: 'light', KeyK: 'heavy', KeyL: 'guard', ArrowDown: 'guard', KeyS: 'guard', KeyI: 'special', KeyU: 'parry' };
  class PlayerInput {
    constructor() {
      this.held = { x: 0, up: false, light: false, heavy: false, guard: false, special: false, parry: false, dash: 0 };
      this.taps = { light: 0, up: 0, special: 0, heavy: 0, dir: 0, parry: 0 };
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
      for (const k of ['up', 'light', 'heavy', 'guard', 'special', 'parry']) h[k] = this.enabled && !!(K.has(k) || T[k]);
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

  // 触摸按键：左下 ◀ ▶ ▲（同一指可滑动切换），右下 轻击 / 重击 / 格挡 / 格开 / 绝技；每个触点独立（多点触控）
  //   格开紧挨格挡（同为防守，拇指一滑即到），按钮外圈为冷却环
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
    // 每个触点归哪个按钮（own）：按下时归所按的按钮；格挡与格开之间拇指一滑即换手（按住格挡滑到格开 = 松开格挡、轻触格开，
    // 滑回来又按住格挡），其余按钮按下后滑出仍算按住（防误触）
    const own = new Map(), recs = {};
    const SLIDE = { guard: 'parry', parry: 'guard' };
    const take = (id, r) => {
      const was = own.get(id);
      if (was === r) return;
      if (was) { was.ids.delete(id); was.set(); }
      if (r) { r.ids.add(id); own.set(id, r); r.set(); } else own.delete(id);
    };
    const inside = (el, e) => { const q = el.getBoundingClientRect(); return e.clientX >= q.left && e.clientX <= q.right && e.clientY >= q.top && e.clientY <= q.bottom; };
    const mk = (cls, label, key, css) => {
      const b = h('sgd-tb ' + cls, label, btns);
      b.style.cssText = css;
      const r = { b, key, ids: new Set() };
      r.set = () => { input.setTouch(key, r.ids.size > 0); b.classList.toggle('on', r.ids.size > 0); };
      recs[key] = r;
      b.addEventListener('pointerdown', e => { e.preventDefault(); try { b.setPointerCapture(e.pointerId); } catch (err) { /* 忽略 */ } take(e.pointerId, r); }, opt);
      b.addEventListener('pointermove', e => {
        const cur = own.get(e.pointerId);
        if (!cur || !SLIDE[cur.key]) return;
        const to = recs[SLIDE[cur.key]];
        if (to && inside(to.b, e) && !inside(cur.b, e)) { e.preventDefault(); take(e.pointerId, to); }
      }, opt);
      const up = e => { if (own.has(e.pointerId)) take(e.pointerId, null); };
      for (const ev of ['pointerup', 'pointercancel', 'lostpointercapture']) b.addEventListener(ev, up);
      return b;
    };
    const B = {
      light: mk('big', '轻击', 'light', 'right:0;bottom:0;'),
      heavy: mk('', '重击', 'heavy', 'right:calc(var(--b)*1.32);bottom:calc(var(--b)*.02);'),
      guard: mk('', '格挡', 'guard', 'right:calc(var(--b)*.1);bottom:calc(var(--b)*1.3);'),
      parry: mk('pa ready', '格开<i class="cd"></i>', 'parry', 'right:calc(var(--b)*1.3);bottom:calc(var(--b)*1.2);'),
      special: mk('sp', '绝技', 'special', 'right:calc(var(--b)*2.5);bottom:calc(var(--b)*.62);'),
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
      this.scripted = !!(opts.ctrl && this.player !== null && opts.ctrl[this.player]);   // 测试：玩家一方由脚本操作
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
      // 手机竖屏时 index.html 的「请横置设备」遮罩盖住画面：此时比试暂停（matchMedia.matches 不触发样式重算，可每帧读）
      this._rotHint = typeof document !== 'undefined' ? document.getElementById('rotate-hint') : null;
      try { this._rotMQ = window.matchMedia ? window.matchMedia('(orientation: portrait) and (max-width: 600px) and (pointer: coarse)') : null; } catch (e) { this._rotMQ = null; }
      this._pause = false; this._resumeAt = 0;
    }

    // 遮罩可见或页面隐藏 → 暂停
    get paused() {
      if (typeof document !== 'undefined' && document.hidden) return true;
      const h = this._rotHint || (this._rotHint = document.getElementById('rotate-hint'));
      return !!(h && this._rotMQ && this._rotMQ.matches && !h.classList.contains('sg-dismissed'));
    }

    async build() {
      const sim = this.sim;
      // 操作者
      // 玩家一方：键盘 / 触摸；自动测试（DuelGame.auto）时仍显示操作界面，但交给电脑
      this.input = this.player !== null ? new PlayerInput() : null;
      if (this.input) this.input.setEnabled(false);       // 开场 / 操作说明期间不接受出招（开战时才启用）
      // 测试钩子 opts.ctrl：[a, b] 各为 (fighter, sim) → 操作者（如 DuelGame.scriptBot），用于在真实画面里看脚本玩家打
      const cf = this.opts.ctrl || [];
      for (const f of sim.f) f.ctrl = cf[f.idx] ? cf[f.idx](f, sim) : (this.player === f.idx && !DuelGame.auto) ? this.input : new AI(f);
      // 场景
      const scene = new THREE.Scene();
      this.scene = scene;
      const cam = new THREE.PerspectiveCamera(30, (SG.Gfx.width || 16) / (SG.Gfx.height || 9), 0.3, 400);
      this.camera = cam;
      this.camPos = new THREE.Vector3(0, 1.8, 10);
      this.camLook = new THREE.Vector3(0, 1, 0);
      // 模型库的共用贴图图集、双方文化的士兵群动作（背景两军）分片预生成（黑场渐入期间进行，不卡画面）
      if (SG.Figure && SG.Figure.preload) {
        try { await SG.Figure.preload({ cultures: [...new Set([this.lookA.culture, this.lookB.culture])], types: [], lods: [], crowd: Arena.crowdSpecs(this.kind, this.lookA.culture, this.lookB.culture) }); } catch (e) { console.warn('figure preload', e); }
      }
      await SG.frame();
      this.arena = new Arena(scene, this.kind, this.colA, this.colB, String(this.genA.name || '甲').charAt(0), String(this.genB.name || '乙').charAt(0), hashStr(this.genA.name + this.genB.name), this.lookA.culture, this.lookB.culture);
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
      // 第三版：js/figure.js 的高精度武将（未载入 figure.js 时退回旧的低多边形模型）
      const Fw = SG.Figure ? FigWarrior : Warrior;
      this.war = [new Fw(sim.f[0], this.colA), new Fw(sim.f[1], this.colB)];
      this.war[0].opp = sim.f[1]; this.war[1].opp = sim.f[0];
      for (const w of this.war) scene.add(w.root);
      // 特效
      this.sparks = new Particles(420, THREE.AdditiveBlending, 1.0, 12);
      this.dust = new Particles(240, THREE.NormalBlending, 1.6, 11);
      this.motes = new Particles(110, THREE.AdditiveBlending, 1.4, 10);
      scene.add(this.sparks.points); scene.add(this.dust.points); scene.add(this.motes.points);
      this.trails = [];
      for (let i = 0; i < 4; i++) { const t = new Trail(28); this.trails.push(t); scene.add(t.mesh); }
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
      this.arrowGeo = SG.MeshBuilder ? [arrowGeometry(false), arrowGeometry(true)] : [buildArrow(false).geo(), buildArrow(true).geo()];
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
      this.topEl = top;
      this.hud = [];
      const sideEl = (i) => {
        const gen = i === 0 ? this.genA : this.genB, col = i === 0 ? this.colA : this.colB;
        const s = h('sgd-side ' + (i === 0 ? 'a' : 'b'), null, top);
        s.style.setProperty('--fc', col);
        const port = portraitEl(gen, col, 96, 'angry', i === 1);
        s.appendChild(port);
        const info = h('sgd-info', null, s);
        const isP = this.player === i;
        h('sgd-name', '<b>' + SG.esc(gen.name || '') + '</b><span class="sgd-war"><i>武力</i>' + warOf(gen) + '</span>' + (isP ? '<span class="sgd-tag">' + (DuelGame.auto || this.scripted ? '自动' : '玩家') + '</span>' : ''), info);
        const hp = h('sgd-hp', '<i class="trail"></i><i class="fill"></i>', info);
        for (let k = 1; k < 4; k++) { const t = document.createElement('i'); t.className = 'tick'; t.style.left = (k * 25) + '%'; t.style.width = '1px'; hp.appendChild(t); }
        const rr = h('sgd-ragerow', null, info);
        const pchip = h('sgd-pchip ready', '格', rr);
        pchip.title = '格开冷却';
        const rage = h('sgd-rage', '<i></i>', rr);
        const lbl = h('sgd-ragelbl', '怒', rr);
        return { s, hp, rage, lbl, pchip, pp: 1, pst: '', trail: 1, trailDelay: 0, lastHp: 100, full: false };
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
      this.edgeEl = h('sgd-edge');
      if (this.player !== null && (!SG.isTouch || this.hybrid)) {
        this.legend = h('sgd-legend', '<span><b class="sgd-k">←</b><b class="sgd-k">→</b>移动</span><span><b class="sgd-k">↑</b>跳</span><span><b class="sgd-k">J</b>轻击</span>' +
          '<span><b class="sgd-k">K</b>重击·按住蓄力</span><span><b class="sgd-k">L</b>格挡</span><span><b class="sgd-k pk">U</b>格开</span><span><b class="sgd-k r">I</b>绝技</span>');
        this.legendU = this.legend.querySelector('.pk');
        this.legend.style.opacity = '0';
      }
      if (this.player !== null && SG.isTouch && this.input) {
        this.touch = buildTouch(root, this.input);
        for (const el of [this.touch.pad, this.touch.btns]) { el.style.opacity = '0'; el.style.visibility = 'hidden'; }
        if (!this.hybrid) {
          this.touchHint = h('sgd-touchhint', '双击 ◀ ▶ 冲刺 / 后撤 · 按住「重击」蓄力 · 看准来招轻触「格开」');
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
      const big = /parry/.test(cls || '');
      this.dmgs.push({ el, p: new THREE.Vector3(x, y, 0.3), t: 0, vx: big ? 0 : (this.fxR.nextDouble() - 0.5) * 0.6, life: big ? 1.25 : 0.9, pop: big });
      if (this.dmgs.length > 10) { const o = this.dmgs.shift(); o.el.remove(); }
    }
    // 画面四周一圈金光（格开得手的提示，约 0.08 秒亮起后淡去）
    edgeFlash() {
      if (!this.edgeEl) return;
      const f = this.edgeEl;
      f.style.transition = 'none'; f.style.opacity = '1';
      void f.offsetWidth;
      f.style.transition = 'opacity .3s ease-out .08s'; f.style.opacity = '0';
    }
    screenFlash(a) {
      const f = this.flashEl;
      f.style.transition = 'none'; f.style.opacity = String(a);
      void f.offsetWidth;
      f.style.transition = 'opacity .28s ease-out'; f.style.opacity = '0';
    }

    resize(w, h) {
      this.rootRect = null; this.hudBot = null;
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
            this.shake = Math.max(this.shake, e.guarded ? 0.04 : 0.05 + e.dmg * 0.012 + (e.big ? 0.06 : 0) + (e.riposte ? 0.06 : 0));
            if (!e.guarded) this.war[def.idx].flashHit(e.big ? '#ffd0a0' : '#ffffff', e.big ? 0.95 : 0.8);
            if (e.guarded) { sfx('duel', 0.8); this.floatDmg(e.x, e.y + 0.3, e.dmg > 0 ? String(e.dmg) : '格', 'g'); if (e.deflect) this.floatDmg(def.x, 2.4, '硬挡', 'lbl'); }
            else {
              sfx('hit', Math.min(1, 0.55 + e.dmg * 0.03));
              if (e.big) synth('thud', 0.5);
              this.floatDmg(e.x, e.y + 0.3, String(e.dmg), e.big ? 'big' : '');
              if (e.counter) this.floatDmg(e.x, e.y + 0.75, '破绽', 'lbl');
              if (e.riposte) {
                // 反击得手：金色爆散 + 闪白 + 重击闷响
                this.floatDmg(e.x, e.y + 0.85, '反击', 'lbl ctr');
                this.burst(e.x, e.y, 0.2, '#ffd860', 34, 6.5, { dir: att.face, size: 0.12, life: 0.45 });
                this.ring(def.x, '#ffd060', 4, 0.5);
                this.screenFlash(0.28);
                synth('thud', 0.9);
              }
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
          case 'parryStart': {
            // 格开架势：刃口一点冷光（受击硬直中按下时在胸前）
            const w = this.war[e.who];
            const p = e.stunned ? w.chestW : w.tipW[0];
            this.star(p.x, p.y, '#cfeeff', e.stunned ? 0.9 : 1.2);
            if (!e.stunned) synth('whoosh', 0.3);
            break;
          }
          case 'parry': {
            const att = sim.f[e.att], def = sim.f[e.def];
            // 火花四溅（冷白 + 金）、大星芒、地面冲击环、闪白、震屏、慢镜头、镜头推近
            this.burst(e.x, e.y, 0.25, '#e4f6ff', 48, 7.5, { dir: att.face, size: 0.11, life: 0.5, grav: 6 });
            this.burst(e.x, e.y, 0.25, '#ffd27a', 28, 5.5, { dir: att.face, size: 0.08, life: 0.45 });
            this.burst(e.x, e.y, 0.25, '#ffffff', 10, 2.5, { size: 0.2, life: 0.16, grav: 0 });
            this.star(e.x, e.y, '#eaf7ff', 4.2);
            this.star(e.x, e.y, '#ffd27a', 2.6);
            this.burst(e.x, e.y, 0.25, '#fff4d0', 20, 9.5, { size: 0.07, life: 0.38, grav: 3 });
            this.ring(def.x, '#a8d8ff', 4.4, 0.45);
            this.screenFlash(0.32);
            this.edgeFlash();
            this.shake = Math.max(this.shake, 0.17);
            if (!sim.result) { this.slow = Math.min(this.slow, 0.28); this.slowT = Math.max(this.slowT, 0.34); }
            this.zoom = { who: def.idx, until: this.realT + 0.6, parry: true };
            this.war[att.idx].flashHit('#a8d8ff', 0.55);
            synth('parry', 1);
            sfx('duel', 0.5);
            this.floatDmg(def.x, def.y + 2.35, '格开！', 'lbl parry');
            this.hud[def.idx].pchip.classList.add('ctr');
            if (this.player === def.idx) {
              if (this.touch) { const b = this.touch.B.parry; b.classList.remove('flash'); void b.offsetWidth; b.classList.add('flash'); }
              try { if (navigator.vibrate) navigator.vibrate([18, 30, 18]); } catch (err) { /* 忽略 */ }
            }
            break;
          }
          case 'parryWhiff': { const f = sim.f[e.who]; this.floatDmg(f.x, f.y + 2.2, '落空', 'lbl miss'); synth('whoosh', 0.25); break; }
          case 'parryEarly': { const f = sim.f[e.who]; this.floatDmg(f.x, f.y + 2.5, '过早', 'lbl miss'); break; }
          case 'heavyGo': {
            // 重击起手预兆：刃口闪出一点红光并「铮」一声（满蓄更亮）
            const w = this.war[e.who];
            this.star(w.tipW[0].x, w.tipW[0].y, e.charge >= 0.85 ? '#ff6a3a' : '#ffb070', 1.2 + e.charge * 1.0);
            synth('shing', 0.5 + e.charge * 0.5);
            break;
          }
          case 'backstep': this.puff(sim.f[e.who].x, 5, 0.6, sim.f[e.who].face); synth('whoosh', 0.35); break;
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
      // 暂停（竖屏遮罩 / 页面隐藏）：模拟与计时停住，清掉按住的输入；恢复后「继续」倒数片刻再开打
      let hold = false;
      if (sim.running && !sim.result) {
        if (this.paused) {
          if (!this._pause) { this._pause = true; this._inWas = !!(this.input && this.input.enabled); if (this.input) this.input.setEnabled(false); }
          this._resumeAt = 0;
          hold = true;
        } else if (this._pause) {
          this._pause = false;
          this._resumeAt = this.realT + 1.0;
          this.announce('继　续', 'small', null, 1.0);
        }
        if (this._resumeAt) {
          if (this.realT < this._resumeAt) hold = true;
          else { this._resumeAt = 0; if (this.input && this._inWas) this.input.setEnabled(true); }
        }
      }
      if (hold) {
        this.acc = 0;
        if (this.zoom) this.zoom.until += dt;
        for (let i = 0; i < 2; i++) if (this.trailOn[i] > this.realT - dt) this.trailOn[i] += dt;
      } else if (this.slowT > 0) { this.slowT -= dt; if (this.slowT <= 0) this.slow = 1; }
      const ts = hold ? 0 : DuelGame.speed * this.slow;
      this.acc += dt * ts;
      let n = 0;
      while (this.acc >= DT && n < 24) { sim.step(DT); this.acc -= DT; n++; }
      if (n >= 24) this.acc = 0;
      this.events();
      const adt = dt * ts;
      const frozen = sim.hitstop > 0;
      for (let i = 0; i < 2; i++) {
        const w = this.war[i];
        w.ctr = sim.countering(sim.f[i]) ? sim.f[i].counterUntil - sim.clock : 0;
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
      // 环境（帅旗、两军；城头火把）。胜负已分：胜方阵中欢呼
      if (sim.result && !this.arena.cheered && sim.endT > 0.6) this.arena.cheer(sim.result.winner, this.realT);
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
      // 第三版修订：取景更紧（两人约占画面高度的一半），两人拉开时镜头随距离后退
      const frac = this.touch ? 0.56 : this.compact ? 0.66 : 0.76;
      let dist = M.clamp((sep + 2.2) / frac / (2 * tanH), 5.8, 15);
      dist = Math.max(dist, (this.touch ? 2.8 : 2.62) / (2 * tanV * (this.touch ? 0.64 : 0.74)));
      let hc = 1.55 + dist * 0.045;
      let ground = this.touch ? 0.42 : 0.46;     // 地平线（脚下）在屏幕中的位置：NDC −ground
      // 特写：绝技发动 / KO
      const z = this.zoom;
      if (z && this.realT < z.until) {
        const f = sim.f[z.who];
        if (z.parry) {
          // 格开：镜头轻推近两人之间（偏向格开者），让弹开的一瞬看得清，随后缓缓拉回
          mid = M.lerp(mid, f.x, 0.3);
          const k = M.clamp01((z.until - this.realT) / 0.6);
          dist *= 1 - 0.2 * Math.sin(Math.min(1, k * 1.6) * Math.PI * 0.5); hc -= 0.06;
        } else if (z.ko) {
          // KO 慢镜：轻推近，镜头不压低——两人全身留在顶栏之下；被击飞者腾空时地平线下移，把他留在画面里
          mid = M.lerp(mid, f.x, 0.55);
          dist *= 0.84; hc += 0.1;
          ground = (this.touch ? 0.48 : 0.5) + Math.min(0.28, 0.14 * Math.max(0, f.y));
        } else { mid = M.lerp(mid, f.x, 0.75); dist *= 0.62; hc -= 0.1; ground = 0.38; }
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
      // 格开得手：约 3° 的镜头侧倾，0.35 秒内回正
      if (z && z.parry && this.realT < z.until) { const u = M.clamp01(1 - (z.until - this.realT) / 0.6); if (u < 0.6) cam.rotateZ(0.055 * Math.sin(u / 0.6 * Math.PI) * (sim.f[z.who].face || 1)); }
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
        // 格开冷却环 / 反击窗口
        const pp = Math.round((1 - sim.parryCd(f) / PARRY_CD) * 50) / 50;
        const pst = sim.countering(f) ? 'ctr' : pp >= 1 ? 'ready' : '';
        if (pp !== H.pp || pst !== H.pst) {
          H.pp = pp; H.pst = pst;
          H.pchip.style.setProperty('--p', String(pp));
          H.pchip.classList.toggle('ready', pst === 'ready'); H.pchip.classList.toggle('ctr', pst === 'ctr');
          if (this.player === i) {
            if (this.touch) { const b = this.touch.B.parry; b.style.setProperty('--p', String(pp)); b.classList.toggle('ready', pp >= 1); }
            if (this.legendU) this.legendU.classList.toggle('cool', pp < 1);
          }
        }
      }
      const left = Math.max(0, Math.ceil(sim.limit - sim.time));
      if (left !== this.lastLeft) { this.lastLeft = left; this.timerB.textContent = String(left); this.timerEl.classList.toggle('low', left <= 10); }
      // 伤害飘字
      const W = SG.Gfx.width || 1, Hh = SG.Gfx.height || 1;
      const v = TMP.v3 || (TMP.v3 = new THREE.Vector3());
      for (let i = this.dmgs.length - 1; i >= 0; i--) {
        const d = this.dmgs[i];
        d.t += dt;
        const life = d.life || 0.9;
        if (d.t > life) { d.el.remove(); this.dmgs.splice(i, 1); continue; }
        v.copy(d.p); v.x += d.vx * d.t; v.y += d.t * (d.pop ? 0.35 : 0.9);
        v.project(this.camera);
        const rr = this.root.getBoundingClientRect ? this.rootRect || (this.rootRect = this.root.getBoundingClientRect()) : { left: 0, top: 0 };
        // 飘字不进顶栏（体力条 / 计时）：夹在顶栏下沿之下
        if (this.hudBot == null) { const tb = this.topEl && this.topEl.getBoundingClientRect ? this.topEl.getBoundingClientRect() : null; this.hudBot = tb && tb.height ? tb.bottom + 18 : 0; }
        const x = (v.x * 0.5 + 0.5) * W, y = Math.max(this.hudBot, (-v.y * 0.5 + 0.5) * Hh);
        const sc = d.pop ? (d.t < 0.14 ? 2.1 - d.t * 7.86 : 1 + 0.04 * Math.sin(d.t * 9)) : d.t < 0.12 ? 1.5 - d.t * 4 : 1;
        d.el.style.transform = 'translate(' + (x - rr.left).toFixed(1) + 'px,' + (y - rr.top).toFixed(1) + 'px) translate(-50%,-50%) scale(' + sc.toFixed(2) + ')';
        d.el.style.opacity = String(M.clamp01((life - d.t) * 3));
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
          '<div class="row"><span class="sgd-chip">◀</span><span class="sgd-chip">▶</span>移动（双击冲刺 / 后撤步）</div>' +
          '<div class="row"><span class="sgd-chip">▲</span>跳跃（空中可轻击）</div></div><div>' +
          '<div class="row"><span class="sgd-chip big">轻击</span>连按三连击</div>' +
          '<div class="row"><span class="sgd-chip">重击</span>按住蓄力，满蓄破防</div>' +
          '<div class="row"><span class="sgd-chip">格挡</span>按住减伤八成</div>' +
          '<div class="row"><span class="sgd-chip pa">格开</span>来招打到前一瞬轻触：弹开对手，反击 ×1.5 不可挡。落空有破绽，冷却 2.5 秒；挨了第二击后踩准第三击再触可格开（早了无效）；满蓄重击见闪光即触；按住格挡滑过来亦可</div>' +
          '<div class="row"><span class="sgd-chip">绝技</span>怒气满时「' + SG.esc(sp.name) + '」</div></div></div>' +
          '<div class="go">轻触开始</div>';
      } else {
        card.innerHTML = '<h2>操作方法</h2>' + vs + '<div class="sgd-rows">' +
          '<div class="keys">' + k('←') + k('→') + '<span style="opacity:.6">/</span>' + k('A') + k('D') + '</div><div class="desc">移动<small>向前双击冲刺，向后双击后撤步</small></div>' +
          '<div class="keys">' + k('↑') + k('W') + k('空格') + '</div><div class="desc">跳跃<small>空中可轻击</small></div>' +
          '<div class="keys">' + k('J') + '</div><div class="desc">轻击<small>连按三连击</small></div>' +
          '<div class="keys">' + k('K') + '</div><div class="desc">重击<small>按住蓄力，满蓄破防</small></div>' +
          '<div class="keys">' + k('L') + '<span style="opacity:.6">/</span>' + k('↓') + '</div><div class="desc">格挡<small>按住减伤八成；满蓄重击与绝技终结会破防</small></div>' +
          '<div class="keys"><b class="sgd-k pk">U</b></div><div class="desc">格开<small>来招打到前一瞬轻按（约 ' + Math.round(parryWindow(warOf(me)) * 100) / 100 + ' 秒）：弹开对手，反击一击 ×1.5 不可挡；落空有破绽，冷却 2.5 秒。挨了连段第二击，踩准第三击的节奏再按可格开（按早无效）；满蓄重击见刃口闪光即按</small></div>' +
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
        if (DuelGame.auto || this.scripted) setTimeout(go, 700 / Math.max(0.1, DuelGame.speed));
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
    // 第 5 个参数（第三版）：格斗细节，供成长系统（DESIGN-V3 §4）按单挑表现给经验；现有 duelFinish 忽略多余参数
    const r = Mdl.duelFinish(a, b, res.winner === 0, res.log, { kind: res.kind, hpA: res.hpA, hpB: res.hpB, stats: res.stats || null });
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
    parryWindow,
    scriptBot,
    PARRY: { CD: PARRY_CD, WHIFF: PARRY_WHIFF, STAGGER: PARRY_STAGGER, COUNTER_WIN, COUNTER_K, HS_K: HS_PARRY_K, HEAVY_CAP: HEAVY_PARRY_CAP },
    heavySU,
    MOVES, WEAPONS, LOOKS,
    _Sim: Sim, _AI: AI,
  };
  try {
    if (typeof location !== 'undefined' && location.search && new URLSearchParams(location.search).get('duelauto') === '1') DuelGame.auto = true;
  } catch (e) { /* 无 location */ }
  DuelGame.play = play;
  SG.DuelGame = DuelGame;
})();
