'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 必杀技数据（DESIGN-V2 §4D）

   每位武将一招独有的必杀技。本文件只有数据；机制表、默认参数、兜底生成器与校验在
   js/specials.js，结算规则在 js/battle-model.js（useSpecial），特效在 js/battle-view.js（specialFx）。
   载入顺序：battle-model.js 之后、specials.js 之前（specials.js 缺本文件也能运行，只是全部走兜底生成）。

   ------------------------------------------------------------------ 追加条目
     SG.SpecialsData.add({ '武将名': { …条目… }, '武将名2': { … } })
       可多次调用（第二阶段在世界数据之后为各国武将追加）；同名后写覆盖前写。
     SG.SpecialsData.get(name) → 原始条目或 null；SG.SpecialsData.names() → 全部有条目的武将名。
   没有条目的武将由 SG.Specials 的兜底生成器按「姓名 + 能力 + 文化」确定性地生成一招（名称形如「颜良·断岳斩」）。

   ------------------------------------------------------------------ 条目格式
     name   招式名（必填）。全局唯一，建议 2–5 字，不得含「·」（「·」留给兜底生成的名称）。
     kind   机制（必填），见下表。
     desc   中文说明（desc 与 lore 二选一）：一句典故 + 一句效果，效果要与数值相符。
     color  主色 '#rrggbb'（特写、特效、飘字用）。缺省取机制默认色。
     fx     特效风格（缺省取机制默认）：
              slash 斩击弧光 · dragon 青龙（斩击 + 盘旋龙气）· havoc 无双（血色巨刃 + 冲击波）· sweep 横扫 ·
              dash 突击残影 · whirl 往来连斩 · arrows 箭雨 · arrow 一箭穿杨 · fire 火海 · wind 风助火势 ·
              lightning 雷击 · water 水淹 · shock 怒吼冲击波 · aura 金光号令 · blossom 桃花（鼓舞）·
              spirit 符咒（符纸绕敌、漩涡，混乱）· shield 护盾 · heal 治愈之光 · poison 毒雾 · shadow 暗影刺杀 ·
              drain 吸魂 · haste 疾风 · claw 猛虎爪痕（单体：三道爪痕；自身为中心：兽影扑向四周）· rock 落石
     lore   可代替 desc：一句典故（以句号结尾）。说明文字 = lore + 由数值自动生成的效果说明（与数值永远一致）。
     cry    可选：发动台词（特写画面上显示）。
     stat   可选：数值所依的能力 'war' | 'intel' | 'pol'（缺省取机制默认）。
     其余为数值参数（缺省取机制默认值；超出范围会被校验报出并夹到范围内）。

   通用参数（凡下表列出者可用）：
     morale  命中敌军的士气变化（负数）；辅助类为友军士气回复（正数）
     confuse 命中敌军陷入混乱的基础概率（按双方所用能力之差修正）；turns 混乱 / 加成持续日数
     pierce  1 = 无视地形防御（武力系伤害）     burn 起火日数（火攻类）
   伤害基准：武力系 = 一次普通攻击的期望伤害 × (0.8 + 武力/500)（随己方兵力等变化）；
             智力 / 政治系 = (200 + 能力×9) × 敌兵力系数（与计策同量级）。
   power = 相对上述基准的倍率。平衡目标：强力招式 ≈ 1.8–2.5 次普通攻击（多目标招式按命中 2 支计）。
   范围封顶：同时命中多支敌军时，全部伤害合计 ≤ 2.5 × 基准（SG.Specials.AREA_TOTAL；主目标不缩减，次要目标按比例缩减）。
   辅助类（rally / command / fortify / heal 的士气、回复、攻防加成，roar 的士气降幅）按能力缩放：× (0.75 + 能力/400)，
   表中与说明文字里的数值为能力 100 时的值。
   持续日数对攻守双方对称：混乱 N 日 = 目标失去 N 次本方行动；加成 N 日 = 覆盖敌方 N 次行动；中毒 N 日 = 发作 N 次。

   kind（机制）      默认能力  参数：默认 [最小, 最大]
   smite   单体重击    war    range 1 [1,2] · power 2.0 [1.4,2.6] · morale −10 [−40,0] · pierce 0 [0,1] · confuse 0 [0,0.6] · turns 1 [1,2]
   cleave  横扫相邻    war    power 1.65 [1.1,2.2] · splash 0.75 [0.3,1]（相邻其他敌军的倍率）· morale −8 [−30,0] · confuse 0 [0,0.5] · turns 1 [1,2]
   charge  直线突击    war    range 3 [2,4] · power 1.7 [1.2,2.2] · dash 0.12 [0,0.2]（每冲过一格 +）· push 1 [0,1]（击退；受阻或敌军在本城 / 城门上时改为伤害 +25%；冲刺与撞击加成合计 ≤ +30%）· morale −10 [−30,0]
   rampage 往来连斩    war    range 2 [1,3] · strikes 4 [2,7] · power 0.55 [0.25,1]（每斩）· morale −6 [−20,0]
   volley  远程齐射    war    range 3 [2,5] · power 1.8 [1,2.4] · radius 0 [0,1] · splash 0.6 [0.3,1] · pierce 0 [0,1] · morale −6 [−30,0]
   blaze   范围火攻    intel  range 3 [2,5] · radius 1 [0,2] · power 1.15 [0.65,1.7] · splash 0.75 [0.3,1] · burn 2 [0,3] · morale −8 [−30,0]
   storm   天候        intel  range 5 [3,7] · radius 2 [1,3] · power 0.7 [0.4,1] · burn 0 [0,3] · confuse 0 [0,0.5] · turns 1 [1,2] · morale −10 [−30,0]
   flood   水攻        intel  range 4 [2,6] · radius 1 [1,2] · power 0.85 [0.5,1.2]（近河 ×1.25、否则 ×0.85；并灭火）· confuse 0 [0,0.4] · turns 1 [1,2] · morale −12 [−30,0]
   roar    威吓        war    radius 2 [1,3]（以自身为中心）· power 0.45 [0,0.9] · morale −25 [−40,−5] · confuse 0.35 [0,0.8] · turns 1 [1,2]
   rally   鼓舞        pol    radius 2 [1,3] · morale 25 [10,40] · heal 0.08 [0,0.2]（最大兵力比例）· cure 1 [0,1] · atk 1 [1,1.3] · turns 2 [1,3]
   command 号令        pol    radius 2 [1,3] · atk 1.25 [1,1.4] · def 1.1 [1,1.3] · turns 2 [1,3] · morale 10 [0,30]
   scheme  奇谋        intel  range 4 [2,6] · radius 1 [0,2] · chance 0.55 [0.3,0.9]（+ 智力差/110）· turns 2 [1,2] · morale −10 [−30,0] · power 0 [0,0.5]
   drain   吸收        war    range 1 [1,2] · power 1.6 [1.2,2.2] · drain 0.5 [0.2,0.8]（伤害收编为己方兵力，随政治修正）· morale −10 [−30,0]
   fortify 坚守        pol    radius 1 [0,2] · def 1.35 [1.1,1.7] · counter 1.3 [1,2] · turns 2 [1,3] · morale 10 [0,30]
   haste   疾行        pol    radius 2 [1,3] · count 2 [1,3]（令已行动的友军再动，并补行动力）· move 1 [0,2]（当日机动力 +）· morale 5 [0,20]
   heal    医术        intel  range 2 [0,4] · radius 1 [0,2] · heal 0.2 [0.08,0.35] · morale 10 [0,30] · cure 1 [0,1]
   assassinate 暗杀    war    range 2 [1,3] · chance 0.3 [0.1,0.5]（+ 武力差/250 + 智力差/300；stat 'intel' 时 + 智力差/200 + 武力差/500；
                              上限 0.6，主将减半）· power 0.6 [0,1.2]（失手时）· morale −15 [−40,0]
   poison  毒计        intel  range 3 [2,5] · radius 1 [0,1] · power 0.4 [0,0.8] · dot 0.06 [0.03,0.12]（每日损兵比例）· turns 3 [2,4] · morale −8 [−30,0]

   唯一性：招式名两两不同；(kind, 补全默认值后的全部数值参数, stat) 组合两两不同（tests/sim.js 与 tests/specials.html 自检）。
   SG.Specials.validate(entry) 会列出未知参数、越界数值、缺字段等问题，写完条目后在 tests/specials.html 查看。
   ========================================================================== */
(function () {
  const SG = window.SG;

  // ------------------------------------------------------------ 数据仓库 --
  // 若其他文件先建了仓库（例如测试页），沿用之
  const store = SG.SpecialsData || (function () {
    const entries = Object.create(null);
    const S = {
      version: 0,
      add(map) {
        if (!map || typeof map !== 'object') return S;
        for (const k of Object.keys(map)) {
          const e = map[k];
          if (!e || typeof e !== 'object') continue;
          entries[k] = Object.assign({}, e);
        }
        S.version++;
        return S;
      },
      get(name) { return Object.prototype.hasOwnProperty.call(entries, name) ? entries[name] : null; },
      has(name) { return Object.prototype.hasOwnProperty.call(entries, name); },
      names() { return Object.keys(entries); },
    };
    return S;
  })();
  SG.SpecialsData = store;

  // ============================================================ 手写条目 --
  // 知名武将（第一批）。其余武将的条目按势力分组追加在后面。
  store.add({
    '关羽': {
      name: '青龙偃月斩', kind: 'smite', range: 1, power: 2.4, morale: -20, pierce: 1,
      fx: 'dragon', color: '#3ddc84', cry: '关云长在此！',
      desc: '挥动八十二斤青龙偃月刀，刀光化作青龙破阵而出。对相邻敌军造成约 2.4 倍普通攻击的伤害（无视地形），敌军士气大挫。',
    },
    '张飞': {
      name: '长坂怒吼', kind: 'roar', radius: 2, power: 0.55, morale: -28, confuse: 0.45, turns: 1,
      fx: 'shock', color: '#ff8a3d', cry: '燕人张翼德在此！谁敢与我决一死战？',
      desc: '当阳桥头一声断喝，吓退曹军百万。震伤周围两格内的全部敌军，士气大跌，并可能陷入混乱。',
    },
    '赵云': {
      name: '七进七出', kind: 'rampage', range: 2, strikes: 7, power: 0.34, morale: -8,
      fx: 'whirl', color: '#d6ebff', cry: '常山赵子龙在此！',
      desc: '长坂坡单骑救主，于曹军中七进七出。在两格内的敌军之间往来冲杀，连斩七次（目标溃散则转斩下一支）。',
    },
    '吕布': {
      name: '天下无双', kind: 'smite', range: 1, power: 2.5, morale: -30, pierce: 1, confuse: 0.3, turns: 1,
      fx: 'havoc', color: '#ff3355', cry: '人中吕布，马中赤兔！',
      desc: '方天画戟所向，天下无人能挡。对相邻敌军造成极大伤害（无视地形），敌军士气崩溃，并可能陷入混乱。',
    },
    '诸葛亮': {
      name: '借东风', kind: 'storm', range: 6, radius: 2, power: 0.8, burn: 2, morale: -12,
      fx: 'wind', color: '#ffae42', cry: '东风已至——火攻！',
      desc: '登七星坛借来东风，火借风势席卷敌阵。六格内任选一处，周围两格的所有敌军受风火重创，脚下燃起两日大火。',
    },
    '黄忠': {
      name: '百步穿杨', kind: 'volley', range: 5, power: 2.2, radius: 0, pierce: 1, morale: -10,
      fx: 'arrow', color: '#ffd24a', cry: '老夫这一箭，百步之外取敌首级！',
      desc: '年近七旬仍能百步穿杨。对五格内一支敌军射出致命一箭，造成约 2.2 倍普通攻击的伤害（无视地形）。',
    },
    '典韦': {
      name: '双戟护主', kind: 'fortify', stat: 'war', radius: 1, def: 1.45, counter: 1.8, turns: 2, morale: 12,
      fx: 'shield', color: '#e0a64a', cry: '有典韦在，谁敢近主公一步！',
      desc: '手持双铁戟死守营门，敌不能近。自身与相邻友军防御大增、反击更猛，持续两日。',
    },
    '华佗': {
      name: '麻沸散', kind: 'heal', stat: 'intel', range: 3, radius: 1, heal: 0.25, morale: 12, cure: 1,
      fx: 'heal', color: '#7dffb2', cry: '刮骨疗毒，药到病除。',
      desc: '以麻沸散施术疗伤。三格内任选一支友军，其周围一格的友军回复大量兵力与士气，并解除混乱。',
    },
    '曹操': {
      name: '魏武挥鞭', kind: 'command', radius: 2, atk: 1.3, def: 1.15, turns: 2, morale: 12,
      fx: 'aura', color: '#7d95ff', cry: '东临碣石，以观沧海——全军听令！',
      desc: '魏武挥鞭，号令三军。周围两格内的友军攻击大幅提升、防御提升，持续两日。',
    },
    '刘备': {
      name: '桃园结义', kind: 'rally', radius: 2, morale: 30, heal: 0.1, cure: 1, atk: 1.15, turns: 2,
      fx: 'blossom', color: '#ff9ec7', cry: '不求同年同月同日生，但愿同年同月同日死！',
      desc: '桃园三结义，同心同德。周围两格内的友军士气大振、回复少量兵力并解除混乱，两日内攻击小幅提升。',
    },
    '孙策': {
      name: '霸王冲阵', kind: 'charge', range: 4, power: 1.7, dash: 0.12, push: 1, morale: -15,
      fx: 'dash', color: '#ff5a36', cry: '江东小霸王在此！',
      desc: '江东小霸王纵马突阵。沿直线冲向四格内的敌军，冲得越远威力越大，并将其击退；退路被阻时撞击伤害更高。',
    },
    '周瑜': {
      name: '赤壁业火', kind: 'blaze', range: 4, radius: 1, power: 1.3, splash: 0.85, burn: 3, morale: -12,
      fx: 'fire', color: '#ff6a1a', cry: '樯橹灰飞烟灭！',
      desc: '赤壁一炬，樯橹灰飞烟灭。对四格内一支敌军纵火，周围一格的敌军同受火焚（林地威力倍增），并燃起持续三日的大火。',
    },
  });

  // ------------------------------------------------------------------------
  // 其余武将：按势力分组。条目用 lore（一句典故）代替 desc：说明文字 = lore + 由数值自动生成的效果说明，
  // 保证说明与数值永远一致。
  // ------------------------------------------------------------------------
  // ---- 董卓军 ----
  store.add({
    '董卓': { name: '并吞禁军', kind: 'drain', range: 1, power: 1.8, drain: 0.6, morale: -15, fx: 'drain', color: '#b03a2e', cry: '顺我者昌，逆我者亡！',
      lore: '何进、丁原死后，董卓尽收京师禁军为己用。' },
    '李儒': { name: '鸩酒毒计', kind: 'poison', range: 3, radius: 1, power: 0.35, dot: 0.09, turns: 3, morale: -10, fx: 'poison', color: '#6fbf3a', cry: '酒中有毒，天意如此。',
      lore: '李儒奉董卓之命，以鸩酒毒杀少帝。' },
    '华雄': { name: '汜水连斩', kind: 'rampage', range: 1, strikes: 3, power: 0.72, morale: -12, fx: 'whirl', color: '#ff6b5a', cry: '关东诸侯，谁敢出战！',
      lore: '华雄守汜水关，连斩鲍忠、祖茂、俞涉、潘凤，诸侯失色。' },
    '李傕': { name: '劫掠长安', kind: 'drain', range: 1, power: 1.7, drain: 0.45, morale: -12, fx: 'drain', color: '#9b4d2b',
      lore: '李傕、郭汜攻破长安，纵兵劫掠，裹挟百姓为兵。' },
    '郭汜': { name: '西凉突骑', kind: 'charge', range: 3, power: 1.65, dash: 0.14, push: 1, morale: -10, fx: 'dash', color: '#c46a3a',
      lore: '郭汜率西凉骑兵横行三辅。' },
    '张济': { name: '弘农屯营', kind: 'fortify', stat: 'war', radius: 1, def: 1.3, counter: 1.4, turns: 2, morale: 10, fx: 'shield', color: '#a3845c',
      lore: '张济屯兵弘农，营垒坚固，进退有据。' },
    '樊稠': { name: '长平追击', kind: 'charge', range: 4, power: 1.6, dash: 0.12, push: 0, morale: -8, fx: 'dash', color: '#d07850',
      lore: '樊稠于长平观大破马腾、韩遂，一路追杀至陈仓。' },
    '徐荣': { name: '汴水伏兵', kind: 'volley', range: 4, power: 1.5, radius: 1, splash: 0.7, pierce: 0, morale: -14, fx: 'arrows', color: '#d9b25a',
      lore: '徐荣于汴水设伏，大破曹操，曹操中流矢险些丧命。' },
    '张辽': { name: '威震逍遥津', kind: 'charge', range: 4, power: 1.85, dash: 0.1, push: 1, morale: -25, fx: 'dash', color: '#4fa3ff', cry: '张文远在此！',
      lore: '逍遥津一战，张辽以八百破十万，江东小儿闻其名不敢夜啼。' },
    '高顺': { name: '陷阵破军', kind: 'smite', range: 1, power: 2.2, morale: -14, pierce: 1, fx: 'slash', color: '#c0c8d8',
      lore: '高顺所将七百余兵号为「陷阵营」，所攻无不破者。' },
    '贾诩': { name: '乱武', kind: 'scheme', range: 5, radius: 2, chance: 0.6, turns: 2, morale: -18, power: 0, fx: 'spirit', color: '#8e44ad', cry: '文和一计，可乱天下。',
      lore: '贾诩劝李傕、郭汜收兵反攻长安，天下由此大乱。' },
    '胡轸': { name: '凉州骄骑', kind: 'cleave', power: 1.71, splash: 0.55, morale: -8, fx: 'sweep', color: '#cf7f4f',
      lore: '胡轸率凉州兵迎击孙坚，自恃勇猛，轻敌冒进。' },
    '牛辅': { name: '据守陕县', kind: 'fortify', radius: 1, def: 1.25, counter: 1.1, turns: 2, morale: 6, fx: 'shield', color: '#8f7a5a',
      lore: '牛辅为董卓女婿，屯兵陕县以拒关东。' },
  });

  // ---- 袁绍军 ----
  store.add({
    '袁绍': { name: '盟主号令', kind: 'command', radius: 3, atk: 1.2, def: 1.15, turns: 2, morale: 12, fx: 'aura', color: '#f1c40f', cry: '讨伐国贼，诸君听令！',
      lore: '袁绍四世三公，被推为关东联军盟主。' },
    '颜良': { name: '勇冠三军', kind: 'smite', range: 1, power: 2.25, morale: -18, fx: 'havoc', color: '#e8803a', cry: '河北颜良在此！',
      lore: '颜良为河北名将，白马之战连斩宋宪、魏续。' },
    '文丑': { name: '延津铁骑', kind: 'cleave', power: 1.93, splash: 0.7, morale: -12, confuse: 0.1, turns: 1, fx: 'sweep', color: '#e0a040',
      lore: '文丑与颜良齐名，延津之战率铁骑追击曹军。' },
    '田丰': { name: '奇兵袭许', kind: 'haste', stat: 'intel', radius: 2, count: 2, move: 1, morale: 8, fx: 'haste', color: '#62d2c0',
      lore: '田丰屡劝袁绍分遣精锐，以奇兵乘虚袭许。' },
    '沮授': { name: '持久之策', kind: 'fortify', stat: 'intel', radius: 2, def: 1.3, counter: 1.15, turns: 3, morale: 8, fx: 'shield', color: '#7aa7d8',
      lore: '沮授主张凭河北之富，与曹操持久相拒。' },
    '审配': { name: '死守邺城', kind: 'fortify', stat: 'intel', radius: 1, def: 1.5, counter: 1.35, turns: 2, morale: 15, fx: 'shield', color: '#5f86c4', cry: '吾为袁氏臣，死亦袁氏鬼！',
      lore: '曹操决漳水灌邺城，审配坚守数月，至死不降。' },
    '张郃': { name: '巧变连击', kind: 'rampage', range: 2, strikes: 3, power: 0.7, morale: -10, fx: 'whirl', color: '#7fb2ff',
      lore: '张郃识变数，善处营阵，料战势地形，无不如计。' },
    '高览': { name: '劫营横扫', kind: 'cleave', power: 1.76, splash: 0.6, morale: -10, fx: 'sweep', color: '#d4a96a',
      lore: '官渡之战，高览与张郃率军猛攻曹营。' },
    '逢纪': { name: '谋夺冀州', kind: 'scheme', range: 4, radius: 0, chance: 0.7, turns: 2, morale: -12, power: 0, fx: 'spirit', color: '#a07de0',
      lore: '逢纪献计诱公孙瓒南下，逼韩馥让出冀州。' },
    '郭图': { name: '谗言离间', kind: 'scheme', range: 4, radius: 1, chance: 0.45, turns: 1, morale: -20, power: 0, fx: 'spirit', color: '#9068c8',
      lore: '郭图屡进谗言，逼得张郃、高览阵前倒戈。' },
    '许攸': { name: '献计乌巢', kind: 'blaze', range: 4, radius: 1, power: 1.45, splash: 0.7, burn: 3, morale: -14, fx: 'fire', color: '#ff8a3a', cry: '乌巢屯粮，一把火便可烧尽！',
      lore: '许攸投曹，献计夜袭乌巢，火焚袁军粮草。' },
    '麴义': { name: '界桥强弩', kind: 'volley', range: 4, power: 1.55, radius: 1, splash: 0.75, pierce: 1, morale: -12, fx: 'arrows', color: '#e0c070',
      lore: '界桥之战，麴义以八百先登、千张强弩大破白马义从。' },
  });

  // ---- 曹操军 ----
  store.add({
    '夏侯惇': { name: '拔矢啖睛', kind: 'smite', range: 1, power: 2.1, morale: -25, confuse: 0.25, turns: 1, fx: 'havoc', color: '#b8322e', cry: '父精母血，不可弃也！',
      lore: '夏侯惇左目中箭，拔矢啖睛，挺枪再战，敌军骇然。' },
    '夏侯渊': { name: '虎步关右', kind: 'haste', stat: 'war', radius: 2, count: 2, move: 2, morale: 10, fx: 'haste', color: '#5ad6ff', cry: '三日五百，六日一千！',
      lore: '夏侯渊用兵神速，虎步关右，所向无前。' },
    '曹仁': { name: '八门金锁', kind: 'fortify', stat: 'war', radius: 2, def: 1.4, counter: 1.3, turns: 2, morale: 8, fx: 'shield', color: '#5577cc',
      lore: '曹仁布八门金锁阵，又坚守樊城、江陵，号为天人。' },
    '曹洪': { name: '舍马救主', kind: 'rally', stat: 'war', radius: 1, morale: 28, heal: 0.06, cure: 1, atk: 1.1, turns: 1, fx: 'aura', color: '#ffd27a', cry: '天下可无洪，不可无公！',
      lore: '荥阳兵败，曹洪以己马让曹操，徒步护主脱险。' },
    '乐进': { name: '先登陷阵', kind: 'charge', range: 3, power: 1.75, dash: 0.15, push: 0, morale: -12, fx: 'dash', color: '#ff8f40',
      lore: '乐进以胆烈从军，每战先登。' },
    '李典': { name: '儒将治军', kind: 'command', radius: 2, atk: 1.15, def: 1.2, turns: 2, morale: 8, fx: 'aura', color: '#e8c460',
      lore: '李典好学问、敬贤士，治军严整，屡识破敌军伏兵。' },
  });

  // ---- 刘备军 ----
  store.add({
    '简雍': { name: '谈笑说降', kind: 'scheme', range: 3, radius: 0, chance: 0.6, turns: 2, morale: -20, power: 0, fx: 'spirit', color: '#d6a6ff',
      lore: '简雍入成都游说刘璋，刘璋遂开城出降。' },
  });

  // ---- 孙坚军 ----
  store.add({
    '孙坚': { name: '江东猛虎', kind: 'smite', range: 1, power: 2.25, morale: -16, confuse: 0.15, turns: 1, fx: 'claw', color: '#ff7043', cry: '孙文台在此！',
      lore: '孙坚勇冠诸侯，手持古锭刀，首破董卓、先入洛阳。' },
    '程普': { name: '三世老臣', kind: 'command', radius: 2, atk: 1.2, def: 1.12, turns: 2, morale: 14, fx: 'aura', color: '#e0b050',
      lore: '程普历事孙坚、孙策、孙权三世，军中皆呼「程公」。' },
    '黄盖': { name: '苦肉火船', kind: 'blaze', stat: 'war', range: 3, radius: 1, power: 1.4, splash: 0.7, burn: 2, morale: -12, fx: 'fire', color: '#ff5722', cry: '火船已发，烧尽曹贼！',
      lore: '赤壁之战，黄盖行苦肉计诈降，以火船冲入曹军水寨。' },
    '韩当': { name: '弓马娴熟', kind: 'volley', range: 4, power: 1.95, radius: 0, pierce: 0, morale: -8, fx: 'arrow', color: '#ffd27a',
      lore: '韩当便弓马、有膂力，随孙坚征伐四方。' },
    '祖茂': { name: '赤帻诱敌', kind: 'scheme', stat: 'war', range: 3, radius: 1, chance: 0.45, turns: 1, morale: -8, power: 0.2, fx: 'spirit', color: '#e03a3a',
      lore: '孙坚兵败，祖茂戴其赤帻引开华雄追兵。' },
    '朱治': { name: '镇抚吴郡', kind: 'rally', radius: 2, morale: 22, heal: 0.08, cure: 1, atk: 1, turns: 2, fx: 'aura', color: '#f6d36b',
      lore: '朱治为吴郡太守三十余年，安抚百姓，供给军需。' },
  });

  // ---- 袁术军 ----
  store.add({
    '袁术': { name: '玉玺称帝', kind: 'command', radius: 3, atk: 1.18, def: 1.05, turns: 2, morale: 20, fx: 'aura', color: '#e6c200', cry: '传国玉玺在此，天命归我！',
      lore: '袁术得传国玉玺，于寿春僭号称帝。' },
    '纪灵': { name: '三尖两刃', kind: 'smite', range: 1, power: 2.05, morale: -10, confuse: 0.15, turns: 1, fx: 'slash', color: '#c8a0ff',
      lore: '纪灵使三尖两刃刀，与张飞大战三十合不分胜负。' },
    '张勋': { name: '七路横扫', kind: 'cleave', power: 1.76, splash: 0.65, morale: -10, fx: 'sweep', color: '#d9b040',
      lore: '张勋为袁术大将军，统七路大军进攻吕布。' },
    '桥蕤': { name: '寿春弩阵', kind: 'volley', range: 3, power: 1.85, radius: 0, pierce: 0, morale: -7, fx: 'arrow', color: '#e8c878',
      lore: '桥蕤守蕲阳以拒曹操，力战而死。' },
    '雷薄': { name: '灊山劫掠', kind: 'drain', range: 1, power: 1.6, drain: 0.4, morale: -8, fx: 'drain', color: '#8a6a4a',
      lore: '雷薄背弃袁术，落草灊山，劫掠为生。' },
    '阎象': { name: '决淮灌敌', kind: 'flood', range: 4, radius: 1, power: 0.95, confuse: 0.1, turns: 1, morale: -12, fx: 'water', color: '#4aa8e8',
      lore: '阎象为袁术主簿，熟知淮水地势。' },
    '杨弘': { name: '长史调度', kind: 'haste', radius: 2, count: 1, move: 1, morale: 8, fx: 'haste', color: '#6ae0c8',
      lore: '杨弘为袁术长史，调度军需、转运兵员。' },
  });

  // ---- 公孙瓒军 ----
  store.add({
    '公孙瓒': { name: '白马义从', kind: 'volley', range: 4, power: 1.5, radius: 1, splash: 0.65, pierce: 0, morale: -12, fx: 'arrows', color: '#f0f0ff', cry: '白马义从，随我冲阵！',
      lore: '公孙瓒选善射之士数千，皆乘白马，号「白马义从」，胡人望而避之。' },
    '严纲': { name: '界桥先锋', kind: 'charge', range: 3, power: 1.6, dash: 0.1, push: 1, morale: -8, fx: 'dash', color: '#e8e8e8',
      lore: '界桥之战，严纲率白马先锋冲阵。' },
    '田楷': { name: '青州固守', kind: 'fortify', radius: 1, def: 1.3, counter: 1.2, turns: 2, morale: 10, fx: 'shield', color: '#80b0e0',
      lore: '田楷为公孙瓒所置青州刺史，与袁谭相持连年。' },
    '公孙越': { name: '白马骑射', kind: 'volley', range: 3, power: 1.9, radius: 0, pierce: 1, morale: -6, fx: 'arrow', color: '#f4f4f4',
      lore: '公孙越为公孙瓒从弟，率骑兵助袁术攻周昂。' },
  });

  // ---- 刘表军 ----
  store.add({
    '刘表': { name: '荆襄安民', kind: 'rally', radius: 3, morale: 24, heal: 0.1, cure: 1, atk: 1, turns: 2, fx: 'aura', color: '#f0d080',
      lore: '刘表单骑入荆州，安抚宗贼，开立学官，荆州百姓安居。' },
    '蔡瑁': { name: '荆州水军', kind: 'flood', range: 4, radius: 1, power: 1.05, turns: 1, morale: -10, fx: 'water', color: '#3a9ad9',
      lore: '蔡瑁久督荆州水军，熟习水战。' },
    '张允': { name: '楼船弩阵', kind: 'volley', range: 4, power: 1.45, radius: 1, splash: 0.6, pierce: 0, morale: -8, fx: 'arrows', color: '#c8b878',
      lore: '张允与蔡瑁同掌水军，楼船之上强弩齐发。' },
    '蒯良': { name: '权谋定荆', kind: 'scheme', range: 4, radius: 1, chance: 0.6, turns: 2, morale: -11, power: 0, fx: 'spirit', color: '#b48ae0',
      lore: '蒯良言「治平者先仁义，治乱者先权谋」，助刘表平定荆州。' },
    '蒯越': { name: '鸿门诱斩', kind: 'assassinate', stat: 'intel', range: 3, chance: 0.32, power: 0.5, morale: -15, fx: 'shadow', color: '#7d3cc8',
      lore: '蒯越设宴诱宗贼首领五十五人，尽斩之。' },
    '文聘': { name: '江夏坚城', kind: 'fortify', stat: 'war', radius: 0, def: 1.6, counter: 1.5, turns: 3, morale: 12, fx: 'shield', color: '#4f8fd0',
      lore: '文聘镇守江夏数十年，孙权亲率五万兵围石阳亦不能下。' },
    '黄祖': { name: '岘山落石', kind: 'volley', range: 3, power: 1.45, radius: 1, splash: 0.8, pierce: 1, morale: -12, fx: 'rock', color: '#c8b090',
      lore: '孙坚追击黄祖至岘山，为伏兵落石乱箭所害。' },
    '王威': { name: '奇兵截击', kind: 'charge', range: 3, power: 1.6, dash: 0.15, push: 1, morale: -10, fx: 'dash', color: '#d09060',
      lore: '王威劝刘琮以奇兵数千截击曹操。' },
  });

  // ---- 刘焉军 ----
  store.add({
    '刘焉': { name: '益州牧令', kind: 'command', radius: 2, atk: 1.15, def: 1.15, turns: 2, morale: 10, fx: 'aura', color: '#e8c050',
      lore: '刘焉建议改刺史为州牧，自领益州，据险自守。' },
    '刘璋': { name: '开城安民', kind: 'rally', radius: 1, morale: 18, heal: 0.12, cure: 1, atk: 1, turns: 2, fx: 'aura', color: '#f8e0a0',
      lore: '刘璋性宽柔，不忍百姓再受兵灾。' },
    '张任': { name: '落凤坡', kind: 'volley', range: 5, power: 1.6, radius: 1, splash: 0.75, pierce: 1, morale: -14, fx: 'arrows', color: '#d4a017', cry: '放箭！',
      lore: '张任伏兵落凤坡，乱箭射杀庞统。' },
    '严颜': { name: '断头将军', kind: 'fortify', stat: 'war', radius: 1, def: 1.4, counter: 1.6, turns: 2, morale: 20, fx: 'shield', color: '#a0c8f0', cry: '但有断头将军，无有降将军！',
      lore: '严颜守巴郡，被擒后宁死不降，张飞义而释之。' },
    '黄权': { name: '北岸拒守', kind: 'command', stat: 'intel', radius: 2, atk: 1.1, def: 1.25, turns: 2, morale: 8, fx: 'aura', color: '#d8c070',
      lore: '黄权多谋，夷陵之战督江北诸军以防魏师。' },
    '张松': { name: '献图入川', kind: 'haste', stat: 'intel', radius: 3, count: 1, move: 2, morale: 5, fx: 'haste', color: '#80e8d0',
      lore: '张松暗献西川地形图，为刘备指明入川之路。' },
    '吴懿': { name: '车骑突阵', kind: 'charge', range: 3, power: 1.7, dash: 0.12, push: 1, morale: -11, fx: 'dash', color: '#c87850',
      lore: '吴懿后官至车骑将军，督汉中诸军。' },
    '冷苞': { name: '决堤涪江', kind: 'flood', stat: 'war', range: 3, radius: 1, power: 1.1, confuse: 0.15, turns: 1, morale: -10, fx: 'water', color: '#5ab0e8',
      lore: '冷苞欲决涪江之水，淹刘备营寨。' },
  });

  // ---- 马腾军 ----
  store.add({
    '马腾': { name: '西凉铁骑', kind: 'charge', range: 3, power: 1.75, dash: 0.12, push: 1, morale: -12, fx: 'dash', color: '#e07a40',
      lore: '马腾为西凉之主，羌胡敬服，铁骑精悍。' },
    '马超': { name: '神威天将军', kind: 'charge', range: 4, power: 2.0, dash: 0.15, push: 1, morale: -20, fx: 'dash', color: '#9fd3ff', cry: '曹贼休走！',
      lore: '潼关之战，马超杀得曹操割须弃袍。' },
    '韩遂': { name: '九曲黄河', kind: 'scheme', range: 4, radius: 2, chance: 0.45, turns: 1, morale: -12, power: 0.2, fx: 'spirit', color: '#d8a050',
      lore: '韩遂纵横凉州三十余年，号称「九曲黄河」，机变百出。' },
    '庞德': { name: '抬榇决死', kind: 'smite', range: 1, power: 2.3, morale: -15, fx: 'havoc', color: '#e8e0d0', cry: '今日不是我杀关羽，便是关羽杀我！',
      lore: '庞德抬榇出战关羽，誓以死报国。' },
    '马岱': { name: '吾敢杀汝', kind: 'assassinate', range: 2, chance: 0.32, power: 0.55, morale: -15, fx: 'shadow', color: '#8a5cd0', cry: '吾敢杀汝！',
      lore: '魏延三呼「谁敢杀我」，马岱应声斩之于马下。' },
    '成宜': { name: '关中骑阵', kind: 'cleave', power: 1.65, splash: 0.6, morale: -8, fx: 'sweep', color: '#d0a070',
      lore: '成宜为关中诸将之一，随马超、韩遂起兵。' },
  });

  // ---- 陶谦军 ----
  store.add({
    '陶谦': { name: '三让徐州', kind: 'rally', radius: 2, morale: 20, heal: 0.12, cure: 1, atk: 1, turns: 2, fx: 'aura', color: '#f5e2a0',
      lore: '陶谦临终，三让徐州于刘备。' },
    '糜竺': { name: '倾家资军', kind: 'rally', radius: 2, morale: 16, heal: 0.18, cure: 0, atk: 1, turns: 2, fx: 'aura', color: '#ffe08a',
      lore: '糜竺进奴客二千、金银货币以助刘备军资。' },
    '糜芳': { name: '江陵城守', kind: 'fortify', radius: 1, def: 1.25, counter: 1.0, turns: 3, morale: 5, fx: 'shield', color: '#90a8c0',
      lore: '糜芳守江陵，城池坚固，粮草充足。' },
    '曹豹': { name: '丹阳精兵', kind: 'cleave', power: 1.71, splash: 0.65, morale: -6, fx: 'sweep', color: '#d0b070',
      lore: '曹豹统领丹阳兵，号为徐州精锐。' },
    '陈登': { name: '匡琦疑兵', kind: 'scheme', range: 4, radius: 1, chance: 0.55, turns: 2, morale: -14, power: 0.15, fx: 'spirit', color: '#a890ff',
      lore: '孙策围匡琦，陈登夜燃火把为疑兵，大破吴军。' },
    '臧霸': { name: '泰山群寇', kind: 'drain', range: 2, power: 1.6, drain: 0.55, morale: -10, fx: 'drain', color: '#a07050',
      lore: '臧霸收泰山群寇，雄踞青徐之间。' },
  });

  // ---- 孔融军 ----
  store.add({
    '孔融': { name: '让梨仁德', kind: 'rally', radius: 2, morale: 26, heal: 0.04, cure: 1, atk: 1, turns: 2, fx: 'aura', color: '#ffe9b0',
      lore: '孔融四岁让梨，以仁德文章闻名天下。' },
    '武安国': { name: '流星铁锤', kind: 'smite', range: 1, power: 2.0, morale: -10, confuse: 0.2, turns: 1, fx: 'slash', color: '#c0c0c0',
      lore: '武安国使铁锤，虎牢关前力战吕布，断腕而还。' },
    '王修': { name: '治政安民', kind: 'heal', stat: 'pol', range: 2, radius: 1, heal: 0.18, morale: 10, cure: 1, fx: 'heal', color: '#8de8a8',
      lore: '王修为袁谭别驾，治政清明，深得民心。' },
  });

  // ---- 韩馥军 ----
  store.add({
    '韩馥': { name: '据守冀州', kind: 'fortify', radius: 2, def: 1.2, counter: 1.1, turns: 2, morale: 6, fx: 'shield', color: '#8cb0d8',
      lore: '韩馥为冀州牧，带甲百万，谷支十年。' },
    '潘凤': { name: '上将大斧', kind: 'smite', range: 1, power: 1.95, morale: -8, fx: 'slash', color: '#d0a040', cry: '吾乃上将潘凤！',
      lore: '韩馥夸口「吾有上将潘凤，可斩华雄」，潘凤持大斧出战。' },
    '耿武': { name: '拔刀刺绍', kind: 'assassinate', range: 1, chance: 0.25, power: 0.7, morale: -10, fx: 'shadow', color: '#c04040',
      lore: '韩馥让冀州，耿武拔刀欲刺袁绍，事败身死。' },
    '沮鹄': { name: '邯郸疑兵', kind: 'scheme', range: 3, radius: 1, chance: 0.4, turns: 1, morale: -6, power: 0.25, fx: 'spirit', color: '#b090d0',
      lore: '沮鹄为沮授之子，守邯郸以拒曹军。' },
  });

  // ---- 张鲁军 ----
  store.add({
    '张鲁': { name: '五斗米道', kind: 'heal', stat: 'pol', range: 2, radius: 1, heal: 0.22, morale: 14, cure: 1, fx: 'heal', color: '#9cf0c0', cry: '义舍米肉，信者自取。',
      lore: '张鲁以五斗米道治汉中，设义舍、施米肉，百姓悦服。' },
    '阎圃': { name: '阳平谋略', kind: 'scheme', range: 3, radius: 1, chance: 0.55, turns: 1, morale: -12, power: 0.1, fx: 'spirit', color: '#9c88d8',
      lore: '阎圃为张鲁功曹，多谋善断。' },
    '杨任': { name: '阳平出击', kind: 'charge', range: 2, power: 1.7, dash: 0.1, push: 1, morale: -10, fx: 'dash', color: '#cc8855',
      lore: '杨任守阳平关，出关迎击曹军。' },
    '杨昂': { name: '阳平关弩', kind: 'volley', range: 3, power: 1.75, radius: 0, pierce: 1, morale: -8, fx: 'arrow', color: '#d8c080',
      lore: '杨昂据阳平关险要，以强弩拒敌。' },
    '张卫': { name: '横山筑城', kind: 'fortify', stat: 'war', radius: 1, def: 1.35, counter: 1.25, turns: 2, morale: 8, fx: 'shield', color: '#7898c8',
      lore: '张卫为张鲁之弟，据阳平关横山筑城十余里。' },
  });

  // ---- 张杨军 ----
  store.add({
    '张杨': { name: '护驾还都', kind: 'rally', radius: 2, morale: 22, heal: 0.05, cure: 1, atk: 1.1, turns: 2, fx: 'aura', color: '#ffd890',
      lore: '张杨迎献帝还洛阳，修缮宫室。' },
    '眭固': { name: '黑山劫掠', kind: 'drain', range: 1, power: 1.5, drain: 0.5, morale: -6, fx: 'drain', color: '#906050',
      lore: '眭固出身黑山贼，后附张杨。' },
    '杨丑': { name: '背刺夺营', kind: 'assassinate', range: 1, chance: 0.28, power: 0.4, morale: -12, fx: 'shadow', color: '#904080',
      lore: '杨丑刺杀张杨，欲举众投奔曹操。' },
  });

  // ---- 刘繇军 ----
  store.add({
    '刘繇': { name: '曲阿督战', kind: 'command', radius: 2, atk: 1.12, def: 1.18, turns: 2, morale: 10, fx: 'aura', color: '#e8d070',
      lore: '刘繇为扬州刺史，屯曲阿以拒袁术、孙策。' },
    '张英': { name: '横江拒守', kind: 'fortify', stat: 'war', radius: 1, def: 1.3, counter: 1.3, turns: 2, morale: 10, fx: 'shield', color: '#6890c0',
      lore: '张英屯当利口、横江，拒袁术、孙策经年。' },
    '笮融': { name: '浴佛宴杀', kind: 'assassinate', range: 2, chance: 0.3, power: 0.45, morale: -12, fx: 'shadow', color: '#b05090',
      lore: '笮融广造佛寺，却屡于宴席间杀害收留自己的主人。' },
    '樊能': { name: '牛渚死战', kind: 'cleave', power: 1.59, splash: 0.75, morale: -10, fx: 'sweep', color: '#c88a5a',
      lore: '樊能与于麋屯牛渚，与孙策死战。' },
    '薛礼': { name: '秣陵弓手', kind: 'volley', range: 3, power: 1.7, radius: 0, pierce: 0, morale: -10, fx: 'arrow', color: '#d8c890',
      lore: '薛礼为彭城相，避乱据守秣陵城。' },
  });

  // ---- 陆康军 ----
  store.add({
    '陆康': { name: '庐江死守', kind: 'fortify', radius: 1, def: 1.6, counter: 1.2, turns: 3, morale: 15, fx: 'shield', color: '#6aa0e0',
      lore: '陆康守庐江，孙策围城两年，城陷而死。' },
    '陆绩': { name: '浑天推演', kind: 'storm', range: 4, radius: 1, power: 0.75, confuse: 0.2, turns: 1, morale: -10, fx: 'lightning', color: '#b0d8ff',
      lore: '陆绩博学，作《浑天图》、注《易》，精于星历。' },
  });

  // ---- 孟获军（南中） ----
  store.add({
    '孟获': { name: '蛮王象阵', kind: 'charge', range: 3, power: 1.8, dash: 0.12, push: 1, morale: -15, fx: 'dash', color: '#c97a2b', cry: '南中之地，岂容外人！',
      lore: '孟获为南中蛮王，驱象兵冲阵，七擒七纵方才心服。' },
    '祝融': { name: '火神飞刀', kind: 'volley', range: 3, power: 2.0, radius: 0, pierce: 1, morale: -10, fx: 'arrow', color: '#ff5a2a', cry: '看刀！',
      lore: '祝融夫人自称火神祝融之后，善掷飞刀，百发百中。' },
    '孟优': { name: '诈降夜袭', kind: 'scheme', stat: 'war', range: 2, radius: 1, chance: 0.4, turns: 1, morale: -10, power: 0.35, fx: 'shadow', color: '#a06030',
      lore: '孟优率蛮兵诈降，欲里应外合夜袭蜀营。' },
    '兀突骨': { name: '藤甲不侵', kind: 'fortify', stat: 'war', radius: 1, def: 1.65, counter: 1.4, turns: 2, morale: 5, fx: 'shield', color: '#b08a3a', cry: '藤甲在身，刀箭不入！',
      lore: '兀突骨所部藤甲兵，刀箭不能入，渡水不沉。' },
    '带来洞主': { name: '驱兽冲阵', kind: 'roar', radius: 1, power: 0.6, morale: -22, confuse: 0.3, turns: 1, fx: 'claw', color: '#c06a2d',
      lore: '带来洞主请木鹿大王驱虎豹豺狼冲阵，蜀军大乱。' },
  });

  // ---- 在野 ----
  store.add({
    '庞统': { name: '连环计', kind: 'scheme', range: 5, radius: 2, chance: 0.5, turns: 2, morale: -10, power: 0.25, fx: 'spirit', color: '#ff9f43', cry: '船船相连，如履平地。',
      lore: '庞统献连环计，使曹军战船首尾相连，为火攻铺路。' },
    '徐庶': { name: '识破金锁', kind: 'scheme', range: 4, radius: 1, chance: 0.6, turns: 1, morale: -14, power: 0.35, fx: 'spirit', color: '#c0a0ff',
      lore: '徐庶识破八门金锁阵，自生门入、景门出，阵势大乱。' },
    '荀彧': { name: '王佐之才', kind: 'command', radius: 3, atk: 1.25, def: 1.2, turns: 2, morale: 15, fx: 'aura', color: '#ffe066', cry: '奉天子以令不臣。',
      lore: '荀彧有王佐之才，居中持重，为曹操筹划军国大计。' },
    '郭嘉': { name: '兵贵神速', kind: 'haste', stat: 'intel', radius: 3, count: 3, move: 2, morale: 8, fx: 'haste', color: '#66ffe0', cry: '兵贵神速！',
      lore: '郭嘉劝曹操轻兵兼道远袭乌桓。' },
    '荀攸': { name: '决水灌城', kind: 'flood', range: 5, radius: 2, power: 0.8, confuse: 0.15, turns: 1, morale: -14, fx: 'water', color: '#4fb4ff',
      lore: '荀攸、郭嘉献计决泗、沂之水灌下邳，生擒吕布。' },
    '程昱': { name: '十面埋伏', kind: 'volley', stat: 'intel', range: 5, power: 1.0, radius: 1, splash: 0.9, pierce: 0, morale: -15, fx: 'arrows', color: '#e8b84a',
      lore: '仓亭之战，程昱献十面埋伏之计，大破袁绍。' },
    '许褚': { name: '虎痴裸衣', kind: 'smite', range: 1, power: 2.35, morale: -15, confuse: 0.15, turns: 1, fx: 'claw', color: '#ff9a3c', cry: '虎痴在此！',
      lore: '许褚号「虎痴」，裸衣与马超大战，力能倒拽牛尾。' },
    '于禁': { name: '毅重持军', kind: 'fortify', stat: 'war', radius: 2, def: 1.35, counter: 1.2, turns: 2, morale: 12, fx: 'shield', color: '#7090c0',
      lore: '于禁治军严整，宛城之乱独领所部不乱。' },
    '满宠': { name: '合肥新城', kind: 'fortify', stat: 'intel', radius: 2, def: 1.45, counter: 1.1, turns: 2, morale: 8, fx: 'shield', color: '#88b8e8',
      lore: '满宠移筑合肥新城，以险制敌，吴军屡攻不下。' },
    '徐晃': { name: '长驱直入', kind: 'charge', range: 4, power: 1.8, dash: 0.14, push: 1, morale: -14, fx: 'dash', color: '#6c8ebf', cry: '大斧开路，直解樊城之围！',
      lore: '徐晃长驱直入关羽重围，曹操赞其「有周亚夫之风」。' },
    '司马懿': { name: '鹰视狼顾', kind: 'scheme', range: 5, radius: 2, chance: 0.6, turns: 2, morale: -15, power: 0.3, fx: 'spirit', color: '#5a6cff',
      lore: '司马懿深谋远虑，鹰视狼顾，屡挫诸葛亮北伐。' },
    '钟繇': { name: '送马关中', kind: 'haste', radius: 2, count: 1, move: 2, morale: 6, fx: 'haste', color: '#80f0c8',
      lore: '钟繇镇关中，送马二千余匹以给曹军。' },
    '姜维': { name: '九伐中原', kind: 'cleave', power: 1.87, splash: 0.8, morale: -12, confuse: 0.2, turns: 1, fx: 'sweep', color: '#40c0ff', cry: '丞相之志，维必继之！',
      lore: '姜维继诸葛亮遗志，九伐中原。' },
    '鲁肃': { name: '联刘抗曹', kind: 'rally', radius: 3, morale: 25, heal: 0.06, cure: 1, atk: 1.12, turns: 2, fx: 'aura', color: '#ffd060',
      lore: '鲁肃力主孙刘联合，共抗曹操。' },
    '张昭': { name: '内事不决', kind: 'heal', stat: 'pol', range: 2, radius: 2, heal: 0.15, morale: 12, cure: 1, fx: 'heal', color: '#a0f0b8',
      lore: '孙策临终嘱孙权：「内事不决问张昭。」' },
    '吕蒙': { name: '白衣渡江', kind: 'drain', range: 2, power: 1.65, drain: 0.6, morale: -20, fx: 'drain', color: '#e8e8f0', cry: '白衣摇橹，渡江取荆州！',
      lore: '吕蒙令精兵白衣扮作商贾渡江，袭取荆州，关羽部众闻家眷无恙，皆无斗志。' },
    '周泰': { name: '舍身护主', kind: 'fortify', stat: 'war', radius: 1, def: 1.5, counter: 1.3, turns: 2, morale: 15, fx: 'shield', color: '#d08850',
      lore: '周泰身被十二创，舍命护孙权突围。' },
    '蒋钦': { name: '楼船冲浪', kind: 'flood', stat: 'war', range: 3, radius: 1, power: 1.0, confuse: 0.1, turns: 1, morale: -10, fx: 'water', color: '#3aa0d0',
      lore: '蒋钦为江表虎臣，统水军屡立战功。' },
    '甘宁': { name: '百骑劫营', kind: 'rampage', range: 2, strikes: 5, power: 0.45, morale: -15, fx: 'whirl', color: '#ffcc33', cry: '铃响处，便是甘兴霸！',
      lore: '甘宁率百骑夜劫曹营，往来冲杀，未折一人一骑。' },
    '太史慈': { name: '神箭破围', kind: 'volley', range: 4, power: 2.1, radius: 0, pierce: 0, morale: -12, fx: 'arrow', color: '#7fd1ff',
      lore: '太史慈善射，孔融被围，太史慈射杀追兵、突围求救。' },
    '陆逊': { name: '火烧连营', kind: 'blaze', range: 5, radius: 2, power: 1.1, splash: 0.8, burn: 2, morale: -15, fx: 'fire', color: '#ff4500', cry: '连营七百里，一火可破！',
      lore: '夷陵之战，陆逊火烧刘备连营七百里。' },
    '凌统': { name: '断桥死战', kind: 'cleave', power: 1.87, splash: 0.6, morale: -10, fx: 'sweep', color: '#e09060',
      lore: '逍遥津之战，凌统率亲兵断后死战，护孙权脱险。' },
    '魏延': { name: '子午谷奇袭', kind: 'charge', range: 4, power: 1.75, dash: 0.16, push: 0, morale: -12, fx: 'dash', color: '#e05050', cry: '谁敢杀我！',
      lore: '魏延请以精兵五千出子午谷，直取长安。' },
    '马良': { name: '招抚五溪', kind: 'drain', stat: 'intel', range: 2, power: 1.4, drain: 0.45, morale: -10, fx: 'drain', color: '#e0e0ff',
      lore: '马良眉有白毛，入武陵招纳五溪蛮夷。' },
    '马谡': { name: '攻心为上', kind: 'scheme', range: 4, radius: 1, chance: 0.55, turns: 1, morale: -20, power: 0, fx: 'spirit', color: '#c8a0ff',
      lore: '马谡献南征之策：「用兵之道，攻心为上，攻城为下。」' },
    '法正': { name: '定军奇谋', kind: 'command', stat: 'intel', radius: 2, atk: 1.3, def: 1, turns: 1, morale: 10, fx: 'aura', color: '#ffb840',
      lore: '法正献计据定军山高处，黄忠居高临下，斩夏侯渊。' },
    '孟达': { name: '上庸奔袭', kind: 'charge', range: 3, power: 1.65, dash: 0.12, push: 0, morale: -8, fx: 'dash', color: '#b08860',
      lore: '孟达自秭归北攻房陵，取上庸。' },
    '王平': { name: '兴势拒守', kind: 'fortify', stat: 'war', radius: 1, def: 1.45, counter: 1.2, turns: 3, morale: 10, fx: 'shield', color: '#80a0d0',
      lore: '王平据兴势，以少兵拒曹爽十万之众。' },
    '邓艾': { name: '偷渡阴平', kind: 'charge', range: 4, power: 1.9, dash: 0.1, push: 0, morale: -18, fx: 'dash', color: '#8d6e63', cry: '以毡自裹，推转而下！',
      lore: '邓艾偷渡阴平七百余里，以毡裹身滚下山崖，直取成都。' },
    '孙乾': { name: '奔走联络', kind: 'haste', radius: 2, count: 1, move: 1, morale: 12, fx: 'haste', color: '#70e0c0',
      lore: '孙乾常为刘备使者，奔走于诸侯之间。' },
    '关平': { name: '关家刀法', kind: 'cleave', power: 1.81, splash: 0.7, morale: -10, fx: 'sweep', color: '#40c070',
      lore: '关平随父征战，深得关家刀法。' },
    '周仓': { name: '水擒庞德', kind: 'flood', stat: 'war', range: 2, radius: 1, power: 1.05, turns: 1, morale: -12, fx: 'water', color: '#3a8ad0',
      lore: '水淹七军之时，周仓于水中生擒庞德。' },
    '张绣': { name: '宛城夜袭', kind: 'assassinate', range: 2, chance: 0.35, power: 0.7, morale: -18, fx: 'shadow', color: '#c03050',
      lore: '张绣夜袭曹营，典韦、曹昂战死，曹操仅以身免。' },
    '鲁宗': { name: '连弩齐发', kind: 'volley', range: 3, power: 1.9, radius: 0, pierce: 0, morale: -8, fx: 'arrows', color: '#d8b860',
      lore: '鲁宗以连弩守城，矢如雨下。' },
    '蒋琬': { name: '社稷之器', kind: 'rally', radius: 3, morale: 20, heal: 0.12, cure: 1, atk: 1, turns: 2, fx: 'aura', color: '#f8e8a0',
      lore: '诸葛亮称蒋琬为「社稷之器」，继掌蜀汉国政。' },
    '刘巴': { name: '直百五铢', kind: 'rally', radius: 2, morale: 14, heal: 0.16, cure: 0, atk: 1.05, turns: 2, fx: 'aura', color: '#ffe680',
      lore: '刘巴铸直百钱，数月之间府库充实。' },
    '霍峻': { name: '葭萌死守', kind: 'fortify', stat: 'war', radius: 0, def: 1.6, counter: 1.6, turns: 3, morale: 15, fx: 'shield', color: '#5f9ad8',
      lore: '霍峻以数百人守葭萌，刘璋万余之众攻之一年不下。' },
    '赵范': { name: '桂阳诈降', kind: 'scheme', range: 3, radius: 0, chance: 0.5, turns: 1, morale: -8, power: 0.3, fx: 'spirit', color: '#b898e0',
      lore: '赵范以寡嫂许配赵云结好，暗图诈降。' },
    '金旋': { name: '武陵城守', kind: 'fortify', radius: 1, def: 1.2, counter: 1.2, turns: 2, morale: 5, fx: 'shield', color: '#7898b8',
      lore: '金旋为武陵太守，据城拒刘备。' },
    '刘度': { name: '零陵安民', kind: 'rally', radius: 1, morale: 15, heal: 0.1, cure: 1, atk: 1, turns: 1, fx: 'aura', color: '#f0e0a0',
      lore: '刘度为零陵太守，举郡归降以免百姓涂炭。' },
    '华歆': { name: '割席断交', kind: 'scheme', range: 3, radius: 1, chance: 0.5, turns: 1, morale: -15, power: 0, fx: 'spirit', color: '#a080c0',
      lore: '管宁与华歆同席读书，见其慕荣华，遂割席分坐。' },
    '王朗': { name: '阵前劝降', kind: 'scheme', range: 4, radius: 1, chance: 0.45, turns: 1, morale: -25, power: 0, fx: 'spirit', color: '#c0b0e0',
      lore: '王朗阵前高谈天命，劝诸葛亮倒戈卸甲。' },
    '虞翻': { name: '易象占天', kind: 'storm', range: 4, radius: 1, power: 0.7, confuse: 0.25, turns: 1, morale: -8, fx: 'lightning', color: '#c0e0ff',
      lore: '虞翻精通《易》学，占卜天象，屡有奇验。' },
    '严白虎': { name: '聚众劫掠', kind: 'drain', range: 1, power: 1.55, drain: 0.35, morale: -8, fx: 'drain', color: '#a07858',
      lore: '严白虎聚众万余，自号「东吴德王」。' },
    '张燕': { name: '黑山飞燕', kind: 'rampage', range: 3, strikes: 4, power: 0.55, morale: -8, fx: 'whirl', color: '#90e0ff',
      lore: '张燕剽悍敏捷，号「飞燕」，统黑山军百万之众。' },
    '沙摩柯': { name: '蒺藜骨朵', kind: 'smite', range: 1, power: 2.3, morale: -12, pierce: 1, fx: 'claw', color: '#a0522d', cry: '五溪蛮王在此！',
      lore: '沙摩柯使铁蒺藜骨朵，又一箭射杀甘宁。' },
  });
})();
