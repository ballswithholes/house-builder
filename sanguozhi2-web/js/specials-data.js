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

/* ==========================================================================
   第二阶段：世界剧本「天下大势」新增武将的必杀技（DESIGN-V2 §4G）
   每人一招，取材于其史事或所属文化；格式同上（lore + 由数值生成的效果说明）。
   全部招式名与 (机制, 参数) 与经典武将、彼此之间两两不同（node tests/sim.js 第 (4) 节自检）。
   ========================================================================== */
(function () {
  const store = window.SG.SpecialsData;

  // ---- 辽东公孙氏与辽东名士 ----
  store.add({
    '公孙度': { name: '威行海外', kind: 'command', radius: 2, atk: 1.24, def: 1.14, turns: 2, morale: 14, fx: 'aura', color: '#c0392b', cry: '辽东之地，我自为王！',
      lore: '公孙度东伐高句丽、西击乌丸，又越海取东莱诸县，“威行海外”。' },
    '公孙康': { name: '函首送曹', kind: 'assassinate', range: 1, chance: 0.32, power: 0.65, morale: -16, fx: 'shadow', color: '#7b241c',
      lore: '袁尚、袁熙兵败来投，公孙康伏兵斩之，函其首送于曹操。' },
    '公孙恭': { name: '闭城自保', kind: 'fortify', radius: 0, def: 1.2, counter: 1.05, turns: 2, morale: 6, fx: 'shield', color: '#8e8e7a',
      lore: '公孙恭“劣弱不能治国”，唯能闭城自保。' },
    '柳毅': { name: '东莱水寨', kind: 'fortify', stat: 'war', radius: 1, def: 1.3, counter: 1.35, turns: 2, morale: 8, fx: 'shield', color: '#5d8aa8',
      lore: '柳毅镇守公孙度越海所取的东莱诸县，筑营拒守。' },
    '阳仪': { name: '劝进称王', kind: 'rally', radius: 2, morale: 22, heal: 0.06, cure: 1, atk: 1.08, turns: 2, fx: 'aura', color: '#d4ac0d',
      lore: '190 年阳仪与柳毅共劝公孙度称王，自立于辽东。' },
    '公孙模': { name: '收集遗民', kind: 'drain', range: 1, power: 1.6, drain: 0.55, morale: -8, fx: 'drain', color: '#a04000',
      lore: '公孙模收集乐浪南部遗民，兴兵伐韩濊，开置带方郡。' },
    '张敞': { name: '南伐韩濊', kind: 'charge', range: 3, power: 1.55, dash: 0.1, push: 1, morale: -10, fx: 'dash', color: '#b9770e',
      lore: '张敞与公孙模同往带方，南伐韩、濊诸部。' },
    '王烈': { name: '德化乡里', kind: 'rally', radius: 2, morale: 24, heal: 0.1, cure: 1, atk: 1, turns: 2, fx: 'blossom', color: '#f5cba7',
      lore: '王烈以德化乡里，在辽东使“强不凌弱，众不暴寡”。' },
    '管宁': { name: '讲学化俗', kind: 'rally', radius: 3, morale: 18, heal: 0.05, cure: 1, atk: 1, turns: 1, fx: 'aura', color: '#e8daef',
      lore: '管宁避乱辽东三十七年，讲诗书、陈俎豆，化民成俗。' },
    '邴原': { name: '清操刚正', kind: 'command', radius: 1, atk: 1.1, def: 1.2, turns: 2, morale: 14, fx: 'aura', color: '#d6eaf8',
      lore: '邴原与管宁、王烈并称“辽东三杰”，清操刚正，人莫敢犯。' },
    '国渊': { name: '屯田足食', kind: 'rally', radius: 2, morale: 12, heal: 0.18, cure: 0, atk: 1, turns: 1, fx: 'aura', color: '#a9dfbf',
      lore: '国渊主持曹魏屯田，数年之间仓廪丰实。' },
    '凉茂': { name: '正色拒逆', kind: 'scheme', range: 3, radius: 0, chance: 0.6, turns: 1, morale: -18, power: 0, fx: 'spirit', color: '#aab7b8',
      lore: '公孙度欲留凉茂为己用，凉茂正色拒之，又劝阻公孙康袭邺。' },
  });

  // ---- 高句丽、夫余、百济、新罗、伽倻 ----
  store.add({
    '故国川王': { name: '坐原大破', kind: 'charge', range: 4, power: 1.95, dash: 0.1, push: 1, morale: -18, fx: 'dash', color: '#c0392b', cry: '高句丽精骑，随我破敌！',
      lore: '故国川王身长九尺、力能扛鼎，184 年亲率精骑于坐原大破汉辽东太守之军。' },
    '延优': { name: '丸都山城', kind: 'fortify', radius: 1, def: 1.45, counter: 1.25, turns: 3, morale: 10, fx: 'shield', color: '#7f8c8d',
      lore: '延优即位为山上王，筑丸都山城，倚山为固。' },
    '发歧': { name: '借兵攻都', kind: 'charge', range: 3, power: 1.5, dash: 0.14, push: 0, morale: -6, fx: 'dash', color: '#922b21',
      lore: '发歧争位失败，借辽东兵三万反攻国内城。' },
    '罽须': { name: '裴川追击', kind: 'smite', range: 1, power: 2.2, morale: -17, pierce: 0, confuse: 0.15, turns: 1, fx: 'slash', color: '#e74c3c', cry: '叛贼休走！',
      lore: '罽须大破发歧与辽东援军，追至裴川，发歧自刎。' },
    '於畀留': { name: '恃势掠夺', kind: 'drain', range: 1, power: 1.6, drain: 0.6, morale: -6, fx: 'drain', color: '#6e2c00',
      lore: '於畀留与左可虑执国权柄，子弟恃势掠夺民田。' },
    '左可虑': { name: '聚众攻都', kind: 'rampage', range: 2, strikes: 3, power: 0.68, morale: -11, fx: 'whirl', color: '#a93226',
      lore: '191 年左可虑与四椽那聚众叛乱，攻打王都。' },
    '优居': { name: '貊弓齐射', kind: 'volley', range: 4, power: 1.7, radius: 1, splash: 0.55, pierce: 0, morale: -10, fx: 'arrows', color: '#ca6f1e',
      lore: '高句丽出产名弓“貊弓”，大加优居率弓手助公孙度击破富山贼。' },
    '然人': { name: '主簿调度', kind: 'haste', radius: 2, count: 1, move: 1, morale: 9, fx: 'haste', color: '#76d7c4',
      lore: '主簿然人随优居出征，调度粮械。' },
    '乙巴素': { name: '政教清明', kind: 'command', radius: 3, atk: 1.15, def: 1.25, turns: 2, morale: 16, fx: 'aura', color: '#f9e79f', cry: '赏罚必信，政教清明。',
      lore: '乙巴素“性质刚毅，智虑渊深”，由布衣拜国相，至其卒时政教清明。' },
    '晏留': { name: '举贤自代', kind: 'haste', radius: 2, count: 2, move: 0, morale: 10, fx: 'haste', color: '#82e0aa',
      lore: '王命四部举贤，众举晏留，晏留自辞而荐乙巴素。' },
    '高优娄': { name: '继相安邦', kind: 'rally', radius: 2, morale: 20, heal: 0.1, cure: 0, atk: 1.05, turns: 2, fx: 'aura', color: '#f8c471',
      lore: '高优娄继乙巴素为国相，历山上、东川两朝。' },
    '尉仇台': { name: '结亲辽东', kind: 'command', radius: 2, atk: 1.12, def: 1.2, turns: 2, morale: 8, fx: 'aura', color: '#d35400',
      lore: '夫余王尉仇台娶公孙度宗女以结援，周旋于高句丽与鲜卑之间。' },
    '简位居': { name: '圆栅坚守', kind: 'fortify', radius: 1, def: 1.3, counter: 1.15, turns: 2, morale: 12, fx: 'shield', color: '#a04000',
      lore: '夫余“作城栅皆员”，简位居继父为王，据栅固守。' },
    '肖古王': { name: '蛙山佯退', kind: 'scheme', range: 3, radius: 1, chance: 0.52, turns: 1, morale: -14, power: 0.4, fx: 'spirit', color: '#2e86c1',
      lore: '190 年肖古王袭新罗圆山乡，于蛙山佯退设伏，大破仇道。' },
    '仇首': { name: '熊谷大破', kind: 'cleave', power: 1.9, splash: 0.65, morale: -14, fx: 'sweep', color: '#3498db',
      lore: '仇首身长七尺、威仪秀异，222 年于熊谷大破新罗五千之众。' },
    '真果': { name: '石门奔袭', kind: 'charge', range: 4, power: 1.65, dash: 0.13, push: 1, morale: -12, fx: 'dash', color: '#5dade2',
      lore: '214 年真果领兵一千，奔袭靺鞨石门城，克之。' },
    '古尔': { name: '六佐平制', kind: 'command', radius: 2, atk: 1.18, def: 1.22, turns: 3, morale: 12, fx: 'aura', color: '#85c1e9',
      lore: '古尔王设六佐平、十六官等，整顿律令，奠定百济国制。' },
    '伐休': { name: '占风知雨', kind: 'storm', range: 5, radius: 2, power: 0.65, burn: 0, confuse: 0.2, turns: 1, morale: -8, fx: 'lightning', color: '#f4d03f',
      lore: '伐休王能占风云，预知水旱丰俭，又知人邪正，国人谓之圣。' },
    '仇道': { name: '讨灭召文', kind: 'cleave', power: 1.75, splash: 0.7, morale: -10, fx: 'sweep', color: '#e67e22',
      lore: '185 年仇道与仇须兮为左右军主，讨灭召文国。' },
    '仇须兮': { name: '右军齐射', kind: 'volley', range: 3, power: 1.6, radius: 1, splash: 0.5, pierce: 0, morale: -8, fx: 'arrows', color: '#f0b27a',
      lore: '仇须兮为右军主，与仇道分道夹击召文国。' },
    '薛支': { name: '左军坚阵', kind: 'fortify', stat: 'war', radius: 1, def: 1.32, counter: 1.5, turns: 2, morale: 10, fx: 'shield', color: '#d68910',
      lore: '仇道蛙山失利后，薛支受命为左军主，掌新罗主力。' },
    '奈解': { name: '救援加罗', kind: 'command', radius: 2, atk: 1.2, def: 1.08, turns: 2, morale: 18, fx: 'aura', color: '#f5b041',
      lore: '209 年浦上八国攻加罗，奈解王遣太子于老率兵往救，破之。' },
    '昔于老': { name: '沙道火攻', kind: 'blaze', range: 4, radius: 1, power: 1.25, splash: 0.8, burn: 3, morale: -14, fx: 'fire', color: '#ff5733', cry: '乘风纵火，焚尽倭船！',
      lore: '233 年倭兵来犯，昔于老于沙道乘风纵火，焚其战船，倭兵溺死殆尽。' },
    '利音': { name: '浦上破敌', kind: 'rampage', range: 2, strikes: 3, power: 0.75, morale: -10, fx: 'whirl', color: '#dc7633',
      lore: '利音以伊伐飡兼知内外兵马事，随于老救加罗、破浦上八国。' },
    '助贲': { name: '东征西讨', kind: 'haste', radius: 2, count: 2, move: 1, morale: 10, fx: 'haste', color: '#48c9b0',
      lore: '助贲王在位时，昔于老东征西讨，灭甘文、平沙梁伐。' },
    '首露王': { name: '龟旨迎王', kind: 'rally', radius: 2, morale: 28, heal: 0.08, cure: 1, atk: 1.1, turns: 2, fx: 'aura', color: '#f7dc6f', cry: '龟何龟何，首其现也！',
      lore: '传说首露王自龟旨峰金卵而生，九干迎之为王。' },
    '居登': { name: '伽倻铁甲', kind: 'fortify', radius: 1, def: 1.4, counter: 1.2, turns: 2, morale: 8, fx: 'shield', color: '#839192',
      lore: '金官伽倻以产铁闻名，居登王以铁甲武装其军。' },
  });

  // ---- 倭国（邪马台、狗奴） ----
  store.add({
    '卑弥呼': { name: '鬼道惑众', kind: 'scheme', range: 5, radius: 2, chance: 0.62, turns: 2, morale: -16, power: 0, fx: 'spirit', color: '#e056fd', cry: '鬼道之下，众皆迷惑。',
      lore: '卑弥呼事鬼道，能惑众，倭国大乱后诸国共立为女王。' },
    '难升米': { name: '黄幢告喻', kind: 'command', radius: 2, atk: 1.15, def: 1.15, turns: 2, morale: 20, fx: 'aura', color: '#f1c40f',
      lore: '魏遣张政持诏书、黄幢拜假难升米，为檄告喻，以督对狗奴之战。' },
    '伊声耆': { name: '倭锦献贡', kind: 'rally', radius: 1, morale: 16, heal: 0.12, cure: 0, atk: 1, turns: 1, fx: 'aura', color: '#f0b27a',
      lore: '243 年伊声耆与掖邪狗等八人使魏，献生口、倭锦、丹木。' },
    '都市牛利': { name: '渡海使魏', kind: 'haste', radius: 2, count: 1, move: 2, morale: 7, fx: 'haste', color: '#5dade2',
      lore: '都市牛利随难升米渡海使魏，受率善校尉印绶。' },
    '掖邪狗': { name: '木弓竹箭', kind: 'volley', range: 3, power: 1.5, radius: 1, splash: 0.5, pierce: 0, morale: -6, fx: 'arrows', color: '#a569bd',
      lore: '倭人“兵用矛、楯、木弓”，竹箭或铁镞或骨镞；掖邪狗受魏率善中郎将印绶，统其弓手。' },
    '载斯乌越': { name: '告急带方', kind: 'haste', radius: 1, count: 1, move: 1, morale: 14, fx: 'haste', color: '#7fb3d5',
      lore: '载斯乌越诣带方郡，陈说倭与狗奴国相攻之状，乞援于魏。' },
    '卑弥弓呼': { name: '狗奴猛袭', kind: 'rampage', range: 2, strikes: 4, power: 0.5, morale: -10, fx: 'whirl', color: '#8e44ad',
      lore: '狗奴国男王卑弥弓呼与女王素不和，举兵相攻。' },
    '狗古智卑狗': { name: '矛楯冲阵', kind: 'charge', range: 3, power: 1.75, dash: 0.08, push: 1, morale: -14, fx: 'dash', color: '#6c3483',
      lore: '狗古智卑狗为狗奴国之官（一说即“菊池彦”），执矛楯冲阵。' },
  });

  // ---- 鲜卑、乌桓、南匈奴、拓跋 ----
  store.add({
    '魁头': { name: '弹汗山庭', kind: 'fortify', stat: 'war', radius: 1, def: 1.3, counter: 1.4, turns: 2, morale: 6, fx: 'shield', color: '#839192',
      lore: '魁头继和连为鲜卑大人，据弹汗山王庭以守檀石槐故地。' },
    '骞曼': { name: '争国离散', kind: 'roar', radius: 1, power: 0.3, morale: -18, confuse: 0.25, turns: 1, fx: 'shock', color: '#a3a3a3',
      lore: '骞曼长大后与魁头争国，鲜卑部众遂离散。' },
    '步度根': { name: '鲜卑铁骑', kind: 'charge', range: 3, power: 1.6, dash: 0.12, push: 1, morale: -12, fx: 'dash', color: '#b7950b',
      lore: '步度根继魁头为大人，率鲜卑铁骑往来驰突于并州塞下。' },
    '扶罗韩': { name: '万骑横扫', kind: 'cleave', power: 1.7, splash: 0.8, morale: -8, fx: 'sweep', color: '#ca6f1e',
      lore: '扶罗韩别拥众数万，自为大人，曾领万余骑至桑干迎能臣氐。' },
    '轲比能': { name: '控弦十万', kind: 'volley', range: 4, power: 2.0, radius: 1, splash: 0.65, pierce: 0, morale: -14, fx: 'arrows', color: '#d4ac0d', cry: '控弦十万，饮马长城！',
      lore: '轲比能勒御部众，拟则中国，控弦十余万骑，为漠南霸主。' },
    '泄归泥': { name: '隐忍复仇', kind: 'smite', range: 1, power: 1.85, morale: -12, pierce: 0, confuse: 0.1, turns: 1, fx: 'slash', color: '#b03a2e',
      lore: '泄归泥之父扶罗韩为轲比能所杀，泄归泥隐忍归附，终叛比能而降魏。' },
    '素利': { name: '东部固守', kind: 'fortify', radius: 2, def: 1.3, counter: 1.2, turns: 2, morale: 10, fx: 'shield', color: '#5d6d7e',
      lore: '素利部众多于轲比能，却“道远初不为边患”，与比能相攻而自守东部。' },
    '弥加': { name: '归义射雕', kind: 'volley', range: 3, power: 1.68, radius: 0, pierce: 0, morale: -9, fx: 'arrow', color: '#d98880',
      lore: '弥加为檀石槐所置东部大人之一，后受魏封为归义王。' },
    '阙机': { name: '东部突骑', kind: 'cleave', power: 1.55, splash: 0.75, morale: -10, confuse: 0.1, turns: 1, fx: 'sweep', color: '#af601a',
      lore: '阙机（厥机）为东部鲜卑大人，建安中受曹操表奏封王。' },
    '沙末汗': { name: '遣使献马', kind: 'haste', radius: 2, count: 1, move: 2, morale: 12, fx: 'haste', color: '#a3e4d7',
      lore: '沙末汗嗣父为亲汉王，与素利、弥加各遣使献马。' },
    '成律归': { name: '摄政守部', kind: 'fortify', radius: 1, def: 1.22, counter: 1.3, turns: 1, morale: 12, fx: 'shield', color: '#85929e',
      lore: '素利死时其子年幼，成律归以弟为王，代管部众。' },
    '莫护跋': { name: '步摇冠', kind: 'command', radius: 2, atk: 1.2, def: 1.2, turns: 2, morale: 10, fx: 'aura', color: '#f7dc6f',
      lore: '莫护跋好戴步摇冠，人称“步摇”，音讹为“慕容”，部族由此得名。' },
    '丘力居': { name: '管子城围', kind: 'poison', range: 3, radius: 1, power: 0.3, dot: 0.08, turns: 4, morale: -12, fx: 'drain', color: '#a0522d',
      lore: '丘力居与张纯叛乱，于辽西管子城围困公孙瓒二百余日，城中粮尽。' },
    '蹋顿': { name: '三郡突骑', kind: 'charge', range: 4, power: 1.9, dash: 0.12, push: 1, morale: -16, fx: 'dash', color: '#e74c3c', cry: '乌丸铁骑，踏破幽州！',
      lore: '蹋顿“有武略”，总摄三郡乌丸，出兵助袁绍击破公孙瓒。' },
    '苏仆延': { name: '峭王弓骑', kind: 'volley', range: 3, power: 1.75, radius: 1, splash: 0.5, pierce: 0, morale: -8, fx: 'arrows', color: '#cd6155',
      lore: '苏仆延统辽东属国乌丸千余落，自称峭王。' },
    '乌延': { name: '寇掠边郡', kind: 'drain', range: 1, power: 1.6, drain: 0.4, morale: -12, fx: 'drain', color: '#784212',
      lore: '乌延统右北平乌丸八百余落，自称汗鲁王，寇掠边郡。' },
    '难楼': { name: '上谷大部', kind: 'rally', radius: 3, morale: 18, heal: 0.1, cure: 0, atk: 1.12, turns: 2, fx: 'aura', color: '#f0b27a',
      lore: '难楼统上谷乌丸九千余落，为诸部中最大的一支。' },
    '楼班': { name: '共奉单于', kind: 'rally', radius: 2, morale: 16, heal: 0.04, cure: 1, atk: 1.06, turns: 1, fx: 'aura', color: '#f5cba7',
      lore: '楼班年长后被难楼、苏仆延共奉为单于，蹋顿退而为王。' },
    '寇娄敦': { name: '右北平骑', kind: 'cleave', power: 1.6, splash: 0.85, morale: -6, fx: 'sweep', color: '#d98880',
      lore: '寇娄敦为曹魏时右北平乌丸单于，景初元年毌丘俭征辽东，率五千余人来降。' },
    '阎柔': { name: '招合胡汉', kind: 'drain', range: 2, power: 1.55, drain: 0.5, morale: -12, fx: 'drain', color: '#d35400',
      lore: '阎柔少陷乌丸、鲜卑中，深得胡心，借鲜卑之众杀邢举而代之。' },
    '普富卢': { name: '率王来朝', kind: 'haste', radius: 1, count: 2, move: 1, morale: 8, fx: 'haste', color: '#a2d9ce',
      lore: '普富卢以代郡乌丸行单于率名王来贺曹操，后又与其侯王来朝。' },
    '能臣氐': { name: '盟会反复', kind: 'assassinate', range: 2, chance: 0.25, power: 0.5, morale: -14, fx: 'shadow', color: '#7d3c98',
      lore: '能臣氐叛魏，先求附扶罗韩，又改招轲比能，致扶罗韩于盟会上被杀。' },
    '於夫罗': { name: '寇掠河内', kind: 'drain', range: 1, power: 1.75, drain: 0.5, morale: -12, fx: 'drain', color: '#943126',
      lore: '於夫罗流亡不得归国，与白波军合兵寇掠河内诸郡。' },
    '呼厨泉': { name: '匈奴骑射', kind: 'volley', range: 3, power: 1.65, radius: 0, pierce: 0, morale: -12, fx: 'arrows', color: '#cb4335',
      lore: '呼厨泉继兄为单于，曾与高干合兵据平阳，匈奴骑射驰突河东。' },
    '去卑': { name: '护驾东归', kind: 'fortify', stat: 'war', radius: 1, def: 1.4, counter: 1.35, turns: 2, morale: 14, fx: 'shield', color: '#f7dc6f',
      lore: '右贤王去卑率匈奴骑兵护卫汉献帝东归洛阳，屡拒李傕、郭汜追兵。' },
    '刘豹': { name: '左部帅令', kind: 'command', radius: 2, atk: 1.22, def: 1.06, turns: 2, morale: 10, fx: 'aura', color: '#a93226',
      lore: '刘豹为南匈奴左贤王，后为五部中的左部帅，其子刘渊开创汉赵。' },
    '拓跋力微': { name: '盛乐归附', kind: 'drain', range: 1, power: 1.6, drain: 0.65, morale: -8, fx: 'drain', color: '#1abc9c',
      lore: '拓跋力微迁居盛乐，诸部大人尽数归附，控弦二十余万。' },
  });

  // ---- 西域诸国 ----
  store.add({
    '童格罗伽': { name: '佉卢王令', kind: 'command', radius: 2, atk: 1.12, def: 1.18, turns: 2, morale: 12, fx: 'aura', color: '#f0b27a',
      lore: '鄯善王以佉卢文书下达王令，自称“大王、众王之王、天子”。' },
    '陀阇迦': { name: '骆驼驿传', kind: 'haste', radius: 2, count: 1, move: 2, morale: 10, fx: 'haste', color: '#f8c471',
      lore: '佉卢文书多记鄯善以骆驼驿传公文、调发人马。' },
    '贝比耶': { name: '绿洲分水', kind: 'rally', radius: 2, morale: 14, heal: 0.15, cure: 0, atk: 1, turns: 2, fx: 'aura', color: '#82e0aa',
      lore: '鄯善诸绿洲仰赖水渠灌溉，佉卢文书屡记王命分水。' },
    '安国': { name: '大破拘弥', kind: 'smite', range: 1, power: 2.05, morale: -18, pierce: 0, confuse: 0.2, turns: 1, fx: 'slash', color: '#2ecc71',
      lore: '175 年于阗王安国攻拘弥，大破其国，杀拘弥王。' },
    '和得': { name: '猎场射杀', kind: 'assassinate', range: 3, chance: 0.3, power: 0.7, morale: -16, fx: 'arrow', color: '#c0392b',
      lore: '168 年疏勒王出猎，被季父和得射杀，和得自立为王。' },
    '阿罗多': { name: '奔袭屯田', kind: 'charge', range: 3, power: 1.6, dash: 0.1, push: 0, morale: -14, fx: 'dash', color: '#a04000',
      lore: '阿罗多与戊部候严皓不和，起兵围攻汉朝屯田，兵败投北匈奴。' },
    '壹多杂': { name: '赖城据守', kind: 'fortify', radius: 1, def: 1.28, counter: 1.18, turns: 3, morale: 8, fx: 'shield', color: '#a0522d',
      lore: '车师后部王治于赖城，魏赐其王壹多杂守魏侍中，号大都尉。' },
    '张恭': { name: '敦煌拒乱', kind: 'fortify', radius: 2, def: 1.25, counter: 1.1, turns: 2, morale: 16, fx: 'shield', color: '#d4ac0d',
      lore: '张恭代领敦煌长史之事，遣子张就东诣曹操，又抵御酒泉黄华、张掖张进之乱。' },
  });

  // ---- 交州（士燮、朱符及寄寓交州的名士）----
  store.add({
    '士燮': { name: '南交保境', kind: 'fortify', radius: 2, def: 1.3, counter: 1.1, turns: 3, morale: 16, fx: 'shield', color: '#58d68d', cry: '处大乱之中，保全一郡。',
      lore: '士燮处大乱之中保全交州，二十余年疆场无事，越南尊为“南交学祖”。' },
    '士壹': { name: '勤恪侍送', kind: 'haste', radius: 1, count: 1, move: 1, morale: 16, fx: 'haste', color: '#73c6b6',
      lore: '士壹侍送刺史丁宫勤恪，被司徒丁宫、黄琬先后辟用。' },
    '士䵋': { name: '九真郡守', kind: 'fortify', radius: 1, def: 1.2, counter: 1.15, turns: 2, morale: 10, fx: 'shield', color: '#7dcea0',
      lore: '朱符死后，士燮表士䵋领九真太守，以安南境。' },
    '士武': { name: '南海弓弩', kind: 'volley', range: 3, power: 1.45, radius: 1, splash: 0.6, pierce: 0, morale: -6, fx: 'arrows', color: '#52be80',
      lore: '士武领南海太守，与诸兄弟分据交州诸郡。' },
    '儋萌': { name: '杖杀番歆', kind: 'smite', range: 1, power: 1.65, morale: -8, pierce: 0, fx: 'slash', color: '#a04000',
      lore: '儋萌为岳父设宴，功曹番歆强邀起舞，儋萌怒而杖杀之。' },
    '士徽': { name: '宗兵守海', kind: 'fortify', stat: 'war', radius: 1, def: 1.35, counter: 1.45, turns: 2, morale: 10, fx: 'shield', color: '#1e8449',
      lore: '士燮死后，士徽自署交趾太守，发宗兵守海口以拒吴。' },
    '士匡': { name: '致书劝降', kind: 'scheme', range: 3, radius: 0, chance: 0.48, turns: 1, morale: -12, power: 0, fx: 'spirit', color: '#a9cce3',
      lore: '吕岱使士匡致书劝士徽出降，许以不死。' },
    '程秉': { name: '论语弼正', kind: 'command', radius: 1, atk: 1.12, def: 1.15, turns: 2, morale: 8, fx: 'aura', color: '#e8f6f3',
      lore: '程秉博通五经，著《周易摘》《尚书驳》《论语弼》，后为吴太子太傅。' },
    '薛综': { name: '无犬为蜀', kind: 'scheme', range: 4, radius: 0, chance: 0.55, turns: 1, morale: -20, power: 0, fx: 'spirit', color: '#bb8fce',
      lore: '薛综嘲蜀使张奉：“有犬为独，无犬为蜀”，满座皆笑，张奉无以应对。' },
    '许靖': { name: '月旦品评', kind: 'rally', radius: 2, morale: 18, heal: 0, cure: 1, atk: 1.18, turns: 2, fx: 'aura', color: '#f9e79f',
      lore: '许靖与从弟许劭主持“月旦评”，每月品评乡党人物。' },
    '袁徽': { name: '致书荀彧', kind: 'haste', radius: 3, count: 1, move: 0, morale: 14, fx: 'haste', color: '#d2b4de',
      lore: '袁徽寄寓交州，致书荀彧，称士燮学问优博、许靖英才伟士。' },
    '番苗': { name: '毒箭复仇', kind: 'poison', stat: 'war', range: 3, radius: 0, power: 0.5, dot: 0.1, turns: 3, morale: -10, fx: 'poison', color: '#7dcea0',
      lore: '番苗为兄番歆复仇，率众攻府，以毒箭射杀太守儋萌。' },
    '李进': { name: '奖劝远人', kind: 'rally', radius: 2, morale: 20, heal: 0.08, cure: 0, atk: 1.1, turns: 2, fx: 'aura', color: '#f7dc6f',
      lore: '李进上书请奖劝远人，朝廷遂许交州孝廉、茂才补本州长吏。' },
    '桓晔': { name: '闾里不争', kind: 'rally', radius: 1, morale: 20, heal: 0.06, cure: 1, atk: 1, turns: 3, fx: 'blossom', color: '#d5f5e3',
      lore: '桓晔客居交趾，越人化其节，至闾里不争讼。' },
    '朱符': { name: '横征暴敛', kind: 'drain', range: 1, power: 1.62, drain: 0.7, morale: -6, fx: 'drain', color: '#7b7d7d',
      lore: '朱符任用乡人分作长吏，横征暴敛，百姓怨叛。' },
    '史璜': { name: '苍梧城守', kind: 'fortify', radius: 1, def: 1.24, counter: 1.2, turns: 2, morale: 12, fx: 'shield', color: '#76a5af',
      lore: '史璜久任苍梧太守，汉末交州动荡，独守郡城。' },
    '刘彦': { name: '赴援豫章', kind: 'charge', range: 3, power: 1.45, dash: 0.15, push: 1, morale: -8, fx: 'dash', color: '#935116',
      lore: '朱符之弟豫章太守朱皓为笮融所杀，朱符遣骑都尉刘彦将兵赴之。' },
    '虞褒': { name: '强赋于民', kind: 'drain', range: 1, power: 1.62, drain: 0.55, morale: -14, fx: 'drain', color: '#6e2c00',
      lore: '虞褒与刘彦“侵虐百姓，强赋于民”，交州为之怨叛。' },
    '张津': { name: '绛帕道书', kind: 'scheme', range: 3, radius: 1, chance: 0.42, turns: 1, morale: -8, power: 0.2, fx: 'spirit', color: '#c0392b',
      lore: '张津好鬼神事，常著绛帕头、鼓琴烧香、读道书，自谓可助军化。' },
    '区景': { name: '帐下刺主', kind: 'assassinate', range: 1, chance: 0.35, power: 0.5, morale: -20, fx: 'shadow', color: '#922b21',
      lore: '张津连年北攻刘表，军心离散，为部将区景所杀。' },
    '吴巨': { name: '逐走赖恭', kind: 'charge', range: 2, power: 1.6, dash: 0.1, push: 1, morale: -10, fx: 'dash', color: '#a04000',
      lore: '吴巨为刘表所遣苍梧太守，与刺史赖恭不和，举兵逐之。' },
    '赖恭': { name: '先辈仁谨', kind: 'fortify', radius: 1, def: 1.18, counter: 1, turns: 2, morale: 18, fx: 'shield', color: '#aed6f1',
      lore: '赖恭“先辈仁谨，不晓时事”，为吴巨所逐，后归刘备为太常。' },
    '牟子': { name: '理惑论', kind: 'heal', range: 3, radius: 1, heal: 0.1, morale: 16, cure: 1, fx: 'heal', color: '#fad7a0',
      lore: '牟子兼通儒道佛，著《理惑论》以解世人之惑。' },
    '刘熙': { name: '释名', kind: 'haste', radius: 1, count: 2, move: 0, morale: 6, fx: 'haste', color: '#f5eef8',
      lore: '刘熙著《释名》二十七篇，客授生徒数百人，程秉、薛综、许慈皆出其门。' },
    '许慈': { name: '忿争相攻', kind: 'scheme', range: 2, radius: 1, chance: 0.4, turns: 1, morale: -10, power: 0.1, fx: 'spirit', color: '#d7bde2',
      lore: '许慈与胡潜学派不和，忿争相攻，刘备令倡家扮作二人以讽之。' },
    '步骘': { name: '诱斩吴巨', kind: 'assassinate', stat: 'intel', range: 2, chance: 0.36, power: 0.4, morale: -22, fx: 'shadow', color: '#2874a6',
      lore: '步骘以会面为名诱斩苍梧太守吴巨，威声大震，士燮兄弟相率归附。' },
    '吕岱': { name: '浮海奔袭', kind: 'charge', range: 4, power: 1.8, dash: 0.1, push: 0, morale: -16, fx: 'dash', color: '#1f618d',
      lore: '226 年吕岱浮海奔袭交趾，诛灭士氏，又讨平九真。' },
  });

  // ---- 林邑、扶南 ----
  store.add({
    '区连': { name: '象林起兵', kind: 'charge', range: 3, power: 1.78, dash: 0.12, push: 1, morale: -15, fx: 'dash', color: '#d4ac0d', cry: '自今日起，我为林邑之王！',
      lore: '区连杀象林县令，自号为王，开创延续千余年的林邑（占婆）。' },
    '混盘况': { name: '间诸邑', kind: 'scheme', range: 5, radius: 1, chance: 0.58, turns: 2, morale: -14, power: 0.25, fx: 'spirit', color: '#af7ac5',
      lore: '混盘况以诈力间诸邑，令其相疑阻，再举兵攻并之。' },
    '盘盘': { name: '委政范蔓', kind: 'haste', radius: 1, count: 2, move: 1, morale: 6, fx: 'haste', color: '#f9e79f',
      lore: '盘盘即位后，把国事全部委托给大将范蔓。' },
    '范师蔓': { name: '横渡涨海', kind: 'rampage', range: 3, strikes: 5, power: 0.5, morale: -12, fx: 'water', color: '#16a085', cry: '扶南大王，扬帆涨海！',
      lore: '范师蔓造大船横渡涨海，攻屈都昆、九稚、典孙等十余国，开地五六千里。' },
    '范金生': { name: '战象冲阵', kind: 'charge', range: 3, power: 1.7, dash: 0.06, push: 1, morale: -16, fx: 'dash', color: '#7d6608',
      lore: '扶南以象为战骑，范金生代父领兵讨伐金邻。' },
    '范旃': { name: '诈杀太子', kind: 'assassinate', stat: 'intel', range: 2, chance: 0.32, power: 0.55, morale: -14, fx: 'shadow', color: '#6c3483',
      lore: '范旃遣人诈迎太子金生而杀之，篡立为扶南王。' },
    '苏物': { name: '远航天竺', kind: 'haste', radius: 3, count: 1, move: 2, morale: 10, fx: 'haste', color: '#48c9b0',
      lore: '苏物自扶南出海，航行一年余抵达天竺，天竺王以月支马四匹报聘。' },
  });

  // ---- 贵霜、西部总督 ----
  store.add({
    '胡维色迦': { name: '万神金币', kind: 'rally', radius: 3, morale: 24, heal: 0.08, cure: 1, atk: 1.12, turns: 2, fx: 'aura', color: '#f4d03f', cry: '诸神护佑贵霜！',
      lore: '胡维色迦所铸金币上的神祇之多，为贵霜历代之冠。' },
    '波调': { name: '亲魏月氏王', kind: 'command', radius: 2, atk: 1.18, def: 1.18, turns: 2, morale: 12, fx: 'aura', color: '#e59866',
      lore: '229 年大月氏王波调遣使奉献，魏以波调为“亲魏大月氏王”。' },
    '迦腻色伽': { name: '贵霜重骑', kind: 'charge', range: 3, power: 1.7, dash: 0.1, push: 1, morale: -12, fx: 'dash', color: '#c39bd3',
      lore: '迦腻色伽二世维持北印度统治，贵霜以披甲重骑冲阵。' },
    '鲁德拉辛哈': { name: '善见湖堤', kind: 'flood', range: 3, radius: 1, power: 0.8, confuse: 0.1, turns: 1, morale: -10, fx: 'water', color: '#3498db',
      lore: '鲁德拉辛哈之父重修善见湖大堤，西部总督王朝善用水利。' },
    '吉瓦达曼': { name: '复位总督', kind: 'fortify', radius: 1, def: 1.26, counter: 1.3, turns: 2, morale: 6, fx: 'shield', color: '#ca6f1e',
      lore: '吉瓦达曼两度称大总督，其间由叔父鲁德拉辛哈掌权。' },
    '鲁德拉塞纳': { name: '大总督令', kind: 'command', radius: 2, atk: 1.15, def: 1.22, turns: 2, morale: 10, fx: 'aura', color: '#d68910',
      lore: '鲁德拉塞纳承父位为大总督二十余年，钱币纪年连绵不绝。' },
  });

  // ---- 安息、波西斯、亚美尼亚、伊比利亚 ----
  store.add({
    '沃洛吉斯': { name: '安息回马箭', kind: 'volley', range: 4, power: 1.9, radius: 1, splash: 0.5, pierce: 1, morale: -12, fx: 'arrows', color: '#c0392b', cry: '回马一箭，罗马人哪里逃！',
      lore: '安息骑射佯退之际回身发矢，罗马人称为“帕提亚回马箭”；沃洛吉斯四世曾于埃勒格亚全歼罗马一军团。' },
    '奥斯罗埃斯': { name: '争位铸币', kind: 'drain', range: 1, power: 1.6, drain: 0.45, morale: -10, fx: 'drain', color: '#884ea0',
      lore: '奥斯罗埃斯在伊朗高原自行铸币称王，与沃洛吉斯争夺王位。' },
    '瓦加尔什': { name: '具装铁骑', kind: 'charge', range: 3, power: 1.9, dash: 0.08, push: 1, morale: -14, fx: 'dash', color: '#7f8c8d',
      lore: '安息倚重人马俱披鳞甲的具装重骑，瓦加尔什以之迎战塞维鲁。' },
    '霍斯罗夫': { name: '举国起兵', kind: 'roar', radius: 2, power: 0.4, morale: -24, confuse: 0.3, turns: 1, fx: 'shock', color: '#e67e22',
      lore: '霍斯罗夫被卡拉卡拉诱捕，亚美尼亚人举国起兵，击败罗马将领狄奥克利图斯。' },
    '戈契赫尔': { name: '白城固守', kind: 'fortify', radius: 1, def: 1.33, counter: 1.08, turns: 2, morale: 8, fx: 'shield', color: '#d5d8dc',
      lore: '巴兹兰吉王戈契赫尔居于白城，为安息藩王。' },
    '帕佩克': { name: '火庙起事', kind: 'scheme', range: 3, radius: 1, chance: 0.56, turns: 1, morale: -14, power: 0.3, fx: 'spirit', color: '#ff7043',
      lore: '帕佩克以伊斯塔克尔火庙守护者之家起事，推翻巴兹兰吉王戈契赫尔。' },
    '提里': { name: '达拉布堡', kind: 'fortify', stat: 'war', radius: 0, def: 1.5, counter: 1.4, turns: 2, morale: 10, fx: 'shield', color: '#b2babb',
      lore: '宦官提里为达拉布格尔德城堡长官，幼年的阿尔达希尔由他抚养。' },
    '巴拉什': { name: '退守两河', kind: 'fortify', radius: 2, def: 1.2, counter: 1.25, turns: 2, morale: 12, fx: 'shield', color: '#5499c7',
      lore: '巴拉什与弟阿尔达班争国，退守美索不达米亚，以塞琉西亚铸币至约 228 年。' },
    '阿尔达班': { name: '尼西比斯', kind: 'charge', range: 4, power: 2.0, dash: 0.1, push: 1, morale: -18, fx: 'dash', color: '#b03a2e', cry: '安息未亡！',
      lore: '217 年阿尔达班率具装骑兵与骆驼甲骑于尼西比斯大破罗马皇帝马克里努斯，迫其赔款求和。' },
    '霍瓦萨克': { name: '受环苏萨', kind: 'command', radius: 1, atk: 1.15, def: 1.15, turns: 3, morale: 8, fx: 'aura', color: '#d4ac0d',
      lore: '苏萨石碑上，霍瓦萨克从阿尔达班四世手中接受象征权力的王环。' },
    '梯里达底': { name: '抗拒萨珊', kind: 'fortify', stat: 'war', radius: 1, def: 1.42, counter: 1.55, turns: 3, morale: 8, fx: 'shield', color: '#d35400',
      lore: '亚美尼亚王梯里达底在罗马支持下即位，长期抗拒萨珊。' },
    '阿尔达希尔': { name: '霍尔木兹甘', kind: 'smite', range: 1, power: 2.4, morale: -24, pierce: 1, confuse: 0.25, turns: 1, fx: 'havoc', color: '#8e44ad', cry: '我乃伊朗诸王之王！',
      lore: '224 年阿尔达希尔于霍尔木兹甘之战亲手击杀安息末王阿尔达班，自称“伊朗诸王之王”。' },
    '沙普尔': { name: '波西斯王', kind: 'cleave', power: 1.6, splash: 0.6, morale: -10, fx: 'sweep', color: '#a93226',
      lore: '沙普尔继父帕佩克为波西斯王，与弟阿尔达希尔不和。' },
    '萨珊': { name: '火庙祈福', kind: 'rally', radius: 2, morale: 22, heal: 0.1, cure: 1, atk: 1.05, turns: 2, fx: 'aura', color: '#ffa726',
      lore: '萨珊为伊斯塔克尔阿娜希塔火庙的守护者，萨珊王朝因他得名。' },
    '列夫': { name: '公正者', kind: 'rally', radius: 2, morale: 26, heal: 0.05, cure: 1, atk: 1.04, turns: 2, fx: 'aura', color: '#5dade2',
      lore: '伊比利亚王列夫号“公正者”，禁止以儿童献祭。' },
    '瓦切': { name: '达里亚尔关', kind: 'fortify', radius: 1, def: 1.36, counter: 1.32, turns: 2, morale: 6, fx: 'shield', color: '#45b39d',
      lore: '伊比利亚扼守高加索山中的达里亚尔关隘，瓦切嗣父列夫为王。' },
  });

  // ---- 哈特拉、奥斯若恩、南阿拉伯、阿克苏姆 ----
  store.add({
    '阿布萨米亚': { name: '哈特拉火油', kind: 'blaze', range: 3, radius: 1, power: 1.2, splash: 0.7, burn: 2, morale: -12, fx: 'fire', color: '#e67e22',
      lore: '塞维鲁两度围攻哈特拉，城中以沥青火油浇烧罗马攻城器械，罗马军无功而返。' },
    '萨纳特鲁克': { name: '毒虫陶罐', kind: 'poison', range: 2, radius: 1, power: 0.35, dot: 0.08, turns: 3, morale: -14, fx: 'poison', color: '#7d6608',
      lore: '哈特拉守军以陶罐装毒虫掷向攻城的罗马军，萨纳特鲁克后为“阿拉伯人之王”。' },
    '阿布加尔': { name: '重建埃德萨', kind: 'rally', radius: 2, morale: 18, heal: 0.16, cure: 0, atk: 1, turns: 2, fx: 'aura', color: '#f5b041',
      lore: '201 年埃德萨大水，阿布加尔八世迁居高处并重建城池。' },
    '巴戴桑': { name: '箭绘人像', kind: 'volley', stat: 'intel', range: 5, power: 1.7, radius: 0, pierce: 1, morale: -8, fx: 'arrow', color: '#85c1e9',
      lore: '朱利乌斯·阿非利加努斯记巴戴桑在阿布加尔宫中以箭射盾，箭矢排出少年的面容。' },
    '哈莫尼乌斯': { name: '赞美诗韵', kind: 'rally', radius: 3, morale: 20, heal: 0.04, cure: 1, atk: 1.08, turns: 1, fx: 'blossom', color: '#aed6f1',
      lore: '哈莫尼乌斯以叙利亚语韵律作赞美诗，传播其父巴戴桑的学说。' },
    '阿勒汗': { name: '红海盟约', kind: 'command', radius: 2, atk: 1.16, def: 1.16, turns: 2, morale: 14, fx: 'aura', color: '#c8a951',
      lore: '萨巴王阿勒汗与“阿比西尼亚人之王”加达拉特订立盟约，铭文十余件记其事。' },
    '沙伊鲁姆': { name: '攻陷沙布瓦', kind: 'charge', range: 4, power: 2.0, dash: 0.12, push: 1, morale: -16, fx: 'dash', color: '#d4ac0d',
      lore: '约 225 年沙伊鲁姆·奥塔尔攻入哈德拉毛，俘其王伊勒阿兹，占都城沙布瓦。' },
    '塔兰': { name: '扎法尔城', kind: 'fortify', radius: 1, def: 1.38, counter: 1.22, turns: 2, morale: 14, fx: 'shield', color: '#b7950b',
      lore: '希木叶尔王塔兰据守都城扎法尔，与萨巴争夺“萨巴与祖赖丹王”之号。' },
    '伊勒阿兹': { name: '乳香商路', kind: 'haste', radius: 2, count: 2, move: 1, morale: 14, fx: 'haste', color: '#f8c471',
      lore: '哈德拉毛控扼乳香之路，伊勒阿兹举行王室典礼时，帕尔米拉、迦勒底与印度使节在场。' },
    '加达拉特': { name: '渡红海', kind: 'cleave', power: 1.8, splash: 0.6, morale: -12, confuse: 0.15, turns: 1, fx: 'sweep', color: '#6e2c00',
      lore: '“阿比西尼亚人之王”加达拉特渡红海插手也门诸国之争。' },
    '贝加特': { name: '进逼扎法尔', kind: 'charge', range: 3, power: 1.75, dash: 0.14, push: 0, morale: -12, fx: 'dash', color: '#784212',
      lore: '奈加什之子贝加特率阿比西尼亚军进逼希木叶尔都城扎法尔。' },
  });

  // ---- 罗马帝国 ----
  store.add({
    '康茂德': { name: '角斗士皇帝', kind: 'smite', range: 1, power: 2.15, morale: -14, pierce: 0, confuse: 0.2, turns: 1, fx: 'havoc', color: '#e74c3c', cry: '朕乃罗马的赫拉克勒斯！',
      lore: '康茂德自比赫拉克勒斯，亲自下场角斗，又在竞技场中以标枪连杀百兽。' },
    '克里安德': { name: '卖官鬻爵', kind: 'scheme', range: 3, radius: 1, chance: 0.45, turns: 1, morale: -10, power: 0.2, fx: 'spirit', color: '#d4ac0d',
      lore: '克里安德独揽大权，大肆卖官，一年之内竟出现二十五位执政官。' },
    '佩蒂纳克斯': { name: '整肃军纪', kind: 'command', radius: 2, atk: 1.2, def: 1.25, turns: 2, morale: 12, fx: 'aura', color: '#9b59b6', cry: '诸君，随我重整罗马！',
      lore: '佩蒂纳克斯以被释奴之子起于行伍，马科曼尼战争立功，185 年平定不列颠兵变。' },
    '莱图斯': { name: '宫廷密谋', kind: 'assassinate', stat: 'intel', range: 2, chance: 0.34, power: 0.3, morale: -12, fx: 'shadow', color: '#7d3c98',
      lore: '禁卫军长官莱图斯与玛琪亚、埃克莱图斯密谋，于 192 年除夕刺杀康茂德。' },
    '埃克莱图斯': { name: '殉主护卫', kind: 'fortify', radius: 0, def: 1.55, counter: 1.25, turns: 1, morale: 20, fx: 'shield', color: '#a569bd',
      lore: '193 年禁卫军哗变，埃克莱图斯是唯一留在佩蒂纳克斯身边抵抗的人，力战而死。' },
    '玛琪亚': { name: '毒酒弑君', kind: 'poison', range: 2, radius: 0, power: 0.6, dot: 0.1, turns: 3, morale: -16, fx: 'poison', color: '#c39bd3',
      lore: '玛琪亚发现自己在康茂德的处死名单上，遂在酒中下毒弑君。' },
    '塞维鲁': { name: '多瑙军团', kind: 'command', radius: 3, atk: 1.3, def: 1.2, turns: 2, morale: 16, fx: 'aura', color: '#a01c2c', cry: '和睦相处，厚待士兵，余者皆可不顾！',
      lore: '193 年塞维鲁率多瑙河诸军团起兵，先后击败尤利安努斯、尼格尔与阿尔拜努斯。' },
    '多姆娜': { name: '军营之母', kind: 'rally', radius: 3, morale: 28, heal: 0.1, cure: 1, atk: 1.1, turns: 2, fx: 'blossom', color: '#f1948a',
      lore: '尤利娅·多姆娜随塞维鲁出征，获“军营之母”尊号。' },
    '尤·莱图斯': { name: '卢格杜努姆', kind: 'charge', range: 4, power: 2.05, dash: 0.1, push: 1, morale: -16, fx: 'dash', color: '#cd6155',
      lore: '197 年卢格杜努姆会战胶着之际，尤利乌斯·莱图斯率骑兵杀出，一举定胜负。' },
    '尼格尔': { name: '达契亚之胜', kind: 'cleave', power: 1.95, splash: 0.65, morale: -12, fx: 'sweep', color: '#34495e',
      lore: '尼格尔 183 年于达契亚击败萨尔马提亚人与自由达契亚人而成名，深得罗马民众拥护。' },
    '阿塞利乌斯': { name: '镇守拜占庭', kind: 'fortify', radius: 1, def: 1.4, counter: 1.2, turns: 3, morale: 10, fx: 'shield', color: '#5d6d7e',
      lore: '阿塞利乌斯为尼格尔镇守拜占庭，此城其后抵抗塞维鲁大军围攻近三年。' },
    '阿尔拜努斯': { name: '不列颠军团', kind: 'command', radius: 2, atk: 1.26, def: 1.12, turns: 2, morale: 8, fx: 'aura', color: '#1a5276',
      lore: '阿尔拜努斯受塞维鲁“凯撒”之号，后率不列颠军团渡海称帝，197 年败死于卢格杜努姆。' },
    '尤利安努斯': { name: '宫城筑垒', kind: 'fortify', radius: 1, def: 1.2, counter: 1.02, turns: 2, morale: 4, fx: 'shield', color: '#f4d03f',
      lore: '尤利安努斯在禁卫军的“拍卖”中出价买得帝位，闻塞维鲁兵至，仓皇在宫中筑垒，又欲驱竞技场的大象上阵。' },
    '阿努利努斯': { name: '伊苏斯风暴', kind: 'storm', range: 5, radius: 2, power: 0.75, burn: 0, confuse: 0.2, turns: 1, morale: -14, fx: 'lightning', color: '#85c1e9',
      lore: '194 年伊苏斯会战，阿努利努斯统塞维鲁军，狂风暴雨正扑尼格尔军面，尼格尔大败。' },
    '坎迪杜斯': { name: '基齐库斯', kind: 'rampage', range: 2, strikes: 2, power: 0.98, morale: -14, fx: 'whirl', color: '#5499c7',
      lore: '193 年坎迪杜斯为塞维鲁先锋，先在基齐库斯、后在尼西亚两败尼格尔军。' },
    '卢普斯': { name: '重金买和', kind: 'scheme', range: 4, radius: 1, chance: 0.5, turns: 2, morale: -6, power: 0, fx: 'spirit', color: '#f5b041',
      lore: '卢普斯任不列颠总督时，以重金向迈阿泰人买和，并修复北方要塞。' },
    '普登斯': { name: '龟甲阵', kind: 'fortify', radius: 1, def: 1.5, counter: 1.15, turns: 2, morale: 10, fx: 'shield', color: '#a93226', cry: '举盾——结龟甲阵！',
      lore: '罗马军团举盾相叠为“龟甲阵”，箭石不能入；普登斯历任下日耳曼、不列颠总督，修筑要塞以守边。' },
    '马克西穆斯': { name: '弩炮攻城', kind: 'volley', range: 5, power: 1.6, radius: 1, splash: 0.6, pierce: 1, morale: -10, fx: 'arrow', color: '#aab7b8',
      lore: '193 年马里乌斯·马克西穆斯率默西亚军围攻拜占庭，以弩炮日夜轰击城墙。' },
    '奇洛': { name: '佩林图斯', kind: 'command', radius: 1, atk: 1.24, def: 1.2, turns: 2, morale: 6, fx: 'aura', color: '#c0392b',
      lore: '193 年奇洛为塞维鲁坚守佩林图斯，此后历任诸省总督与罗马城市长官。' },
    '卡斯图斯': { name: '渡海征讨', kind: 'charge', range: 3, power: 1.8, dash: 0.1, push: 1, morale: -12, fx: 'dash', color: '#2e4053',
      lore: '卡斯图斯曾任第六胜利军团长官，率不列颠军团与骑兵渡海讨伐阿莫里卡人。' },
    '提尼乌斯': { name: '埃及粮船', kind: 'rally', radius: 2, morale: 10, heal: 0.2, cure: 0, atk: 1, turns: 1, fx: 'aura', color: '#f9e79f',
      lore: '提尼乌斯为埃及长官，主持输往罗马的粮船。' },
    '鲁弗斯': { name: '第七双子', kind: 'cleave', power: 1.64, splash: 0.7, morale: -8, fx: 'sweep', color: '#5b2c6f',
      lore: '鲁弗斯为近西班牙总督，197 年率第七双子军团投奔阿尔拜努斯。' },
    '奥盖卢': { name: '帕尔米拉弓', kind: 'volley', range: 4, power: 1.75, radius: 0, pierce: 0, morale: -10, fx: 'arrows', color: '#e59866',
      lore: '帕尔米拉弓手闻名罗马军中，奥盖卢屡任将军，征讨游牧民以护商队。' },
    '卡拉卡拉': { name: '屠城示威', kind: 'roar', radius: 2, power: 0.6, morale: -30, confuse: 0.35, turns: 1, fx: 'shock', color: '#922b21',
      lore: '卡拉卡拉残暴好杀，215 年在亚历山大里亚纵兵屠城。' },
    '盖塔': { name: '兄弟共治', kind: 'rally', radius: 1, morale: 14, heal: 0.14, cure: 1, atk: 1, turns: 1, fx: 'aura', color: '#d7bde2',
      lore: '盖塔与兄长卡拉卡拉共治帝国，两人分居宫室，互不相容。' },
    '马克里努斯': { name: '路旁行刺', kind: 'assassinate', stat: 'intel', range: 3, chance: 0.3, power: 0.35, morale: -14, fx: 'shadow', color: '#4a235a',
      lore: '217 年马克里努斯指使卫士于卡拉卡拉途中下马时将其刺杀，随即称帝。' },
    '普劳提安': { name: '权倾一时', kind: 'scheme', range: 3, radius: 1, chance: 0.52, turns: 2, morale: -8, power: 0, fx: 'spirit', color: '#6c3483',
      lore: '普劳提安为禁卫军长官，权倾一时，其像遍立罗马，女儿嫁卡拉卡拉。' },
    '盖伦': { name: '四体液说', kind: 'heal', range: 3, radius: 1, heal: 0.3, morale: 10, cure: 1, fx: 'heal', color: '#76d7a4', cry: '体液调和，百病自消。',
      lore: '盖伦先后为马可·奥勒留、康茂德、塞维鲁诊病，其医学著作统治西方医学一千余年。' },
    '帕比尼安': { name: '法理不屈', kind: 'fortify', radius: 2, def: 1.3, counter: 1, turns: 3, morale: 20, fx: 'shield', color: '#f2f4f4',
      lore: '帕比尼安为罗马五大法学家之首，拒绝为卡拉卡拉弑弟辩护，称“弑亲易，为之辩护难”，遂被处死。' },
    '乌尔比安': { name: '学说汇纂', kind: 'command', radius: 2, atk: 1.06, def: 1.28, turns: 3, morale: 10, fx: 'aura', color: '#d5dbdb',
      lore: '乌尔比安的著作约占《学说汇纂》三分之一，“正义乃使人各得其所”即出其手。' },
    '狄奥': { name: '以史为鉴', kind: 'scheme', range: 4, radius: 0, chance: 0.6, turns: 2, morale: -10, power: 0.15, fx: 'spirit', color: '#aab7b8',
      lore: '狄奥历仕诸帝，所著《罗马史》八十卷是这一时期罗马史最主要的史源。' },
    '马克西明': { name: '连胜十六人', kind: 'rampage', range: 1, strikes: 6, power: 0.42, morale: -14, fx: 'whirl', color: '#cb4335', cry: '再来！下一个！',
      lore: '色雷斯农家子马克西明身材魁梧，在塞维鲁为盖塔庆生的竞技中连胜十六人而入伍。' },
    '戈尔迪安': { name: '巨富散财', kind: 'rally', radius: 2, morale: 20, heal: 0.12, cure: 1, atk: 1.06, turns: 1, fx: 'aura', color: '#d4ac0d',
      lore: '戈尔迪安家资巨万，曾自费举办盛大竞技；238 年在提斯德鲁斯被拥立为帝。' },
    '德西乌斯': { name: '追击哥特', kind: 'charge', range: 3, power: 1.85, dash: 0.06, push: 0, morale: -14, fx: 'dash', color: '#7b241c',
      lore: '德西乌斯于阿伯里图斯追击哥特王尼瓦，陷入沼泽，与其子同殁阵中。' },
    '菲利普': { name: '千年庆典', kind: 'rally', radius: 3, morale: 25, heal: 0.06, cure: 0, atk: 1.1, turns: 2, fx: 'blossom', color: '#f7dc6f',
      lore: '阿拉伯人菲利普在位时，于 248 年主持罗马建城千年庆典。' },
    '瓦勒良': { name: '东西分镇', kind: 'command', radius: 2, atk: 1.14, def: 1.24, turns: 2, morale: 10, fx: 'aura', color: '#566573',
      lore: '瓦勒良即位后与其子伽利埃努斯分镇东西，以御哥特与萨珊。' },
    '玛伊莎': { name: '重金收军', kind: 'drain', stat: 'pol', range: 2, power: 1.5, drain: 0.75, morale: -10, fx: 'drain', color: '#f5b041',
      lore: '218 年玛伊莎以重金收买军队，扶立外孙埃拉伽巴路斯，推翻马克里努斯。' },
    '庞培亚努斯': { name: '北疆宿将', kind: 'command', radius: 2, atk: 1.22, def: 1.22, turns: 2, morale: 12, fx: 'aura', color: '#7d6608',
      lore: '庞培亚努斯是马科曼尼战争中马可·奥勒留最倚重的将领，后两度拒绝让给他的帝位。' },
    '纳尔奇苏斯': { name: '浴室扼杀', kind: 'assassinate', range: 1, chance: 0.42, power: 0.9, morale: -20, fx: 'shadow', color: '#5b2c6f',
      lore: '192 年除夕，摔跤手纳尔奇苏斯受玛琪亚、莱图斯之命，在浴室中扼死康茂德。' },
  });

  // ---- 喀里多尼亚、日耳曼诸族、萨尔马提亚、博斯普鲁斯 ----
  store.add({
    '阿根托科克斯': { name: '高地战车', kind: 'charge', range: 4, power: 1.75, dash: 0.15, push: 1, morale: -12, fx: 'dash', color: '#2471a3',
      lore: '喀里多尼亚人驾战车、持短矛与盾，出没于沼泽山地；阿根托科克斯其名意为“银腿”。' },
    '巴洛马尔': { name: '突入意大利', kind: 'charge', range: 4, power: 2.0, dash: 0.1, push: 0, morale: -16, fx: 'dash', color: '#935116', cry: '翻过群山，直取阿奎莱亚！',
      lore: '巴洛马尔领导十一族联军翻越阿尔卑斯、围攻阿奎莱亚，是自辛布里人以来首支攻入意大利的外敌。' },
    '富尔提乌斯': { name: '日耳曼盾墙', kind: 'fortify', stat: 'war', radius: 1, def: 1.34, counter: 1.4, turns: 2, morale: 6, fx: 'shield', color: '#6e2c00',
      lore: '富尔提乌斯与马可·奥勒留订约，率夸迪人结盾墙守边。' },
    '伽约博马尔': { name: '弗拉梅亚矛', kind: 'smite', range: 2, power: 1.9, morale: -10, pierce: 0, fx: 'slash', color: '#7e5109',
      lore: '日耳曼人惯用短刃长柄的“弗拉梅亚”矛，可刺可掷；伽约博马尔为夸迪王。' },
    '阿里奥盖斯': { name: '亡命夸迪', kind: 'cleave', power: 1.66, splash: 0.72, morale: -10, confuse: 0.12, turns: 1, fx: 'sweep', color: '#873600',
      lore: '夸迪人废富尔提乌斯而立阿里奥盖斯，马可·奥勒留悬赏千金欲活捉之。' },
    '赞提库斯': { name: '冰河会战', kind: 'charge', range: 3, power: 1.85, dash: 0.14, push: 1, morale: -12, fx: 'dash', color: '#aed6f1',
      lore: '雅济吉斯骑兵惯于在封冻的多瑙河上驰突作战，赞提库斯为其王。' },
    '巴纳达斯普': { name: '骑枪突刺', kind: 'smite', range: 2, power: 1.8, morale: -12, pierce: 1, fx: 'slash', color: '#85929e',
      lore: '萨尔马提亚骑兵披鳞甲、双手持长枪冲锋；巴纳达斯普为前雅济吉斯王。' },
    '菲利默': { name: '击破斯帕利', kind: 'cleave', power: 1.85, splash: 0.75, morale: -12, fx: 'sweep', color: '#a04000',
      lore: '传说菲利默率哥特人南迁至斯基泰的“奥伊姆”，过河时桥断，又击败斯帕利人。' },
    '撒罗玛提斯': { name: '肃清黑海', kind: 'rampage', range: 3, strikes: 3, power: 0.8, morale: -10, fx: 'water', color: '#2e86c1',
      lore: '撒罗玛提斯二世与斯基泰人、西拉奇人及海盗作战，193 年塔奈斯铭文称其肃清黑海航路。' },
    '雷斯库波': { name: '潘提卡彭', kind: 'fortify', radius: 1, def: 1.3, counter: 1.25, turns: 1, morale: 14, fx: 'shield', color: '#1abc9c',
      lore: '雷斯库波三世继父为博斯普鲁斯王，据潘提卡彭城，事迹多见于钱币。' },
    '皮耶波鲁斯': { name: '南掠希腊', kind: 'rampage', range: 2, strikes: 4, power: 0.45, morale: -8, fx: 'whirl', color: '#784212',
      lore: '170 年前后科斯托博契人南下劫掠，远至希腊厄琉息斯秘仪圣所。' },
  });
})();
