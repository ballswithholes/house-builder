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
     desc   中文说明（必填）：一句典故 + 一句效果，效果要与数值相符。
     color  主色 '#rrggbb'（特写、特效、飘字用）。缺省取机制默认色。
     fx     特效风格（缺省取机制默认）：
              slash 斩击弧光 · dragon 青龙（斩击 + 盘旋龙气）· havoc 无双（血色巨刃 + 冲击波）· sweep 横扫 ·
              dash 突击残影 · whirl 往来连斩 · arrows 箭雨 · arrow 一箭穿杨 · fire 火海 · wind 风助火势 ·
              lightning 雷击 · water 水淹 · shock 怒吼冲击波 · aura 金光号令 · blossom 桃花（鼓舞）·
              spirit 符阵（混乱）· shield 护盾 · heal 治愈之光 · poison 毒雾 · shadow 暗影刺杀 ·
              drain 吸魂 · haste 疾风
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

   kind（机制）      默认能力  参数：默认 [最小, 最大]
   smite   单体重击    war    range 1 [1,2] · power 2.0 [1.4,2.6] · morale −10 [−40,0] · pierce 0 [0,1] · confuse 0 [0,0.6] · turns 1 [1,2]
   cleave  横扫相邻    war    power 1.5 [1,2] · splash 0.75 [0.3,1]（相邻其他敌军的倍率）· morale −8 [−30,0] · confuse 0 [0,0.5] · turns 1 [1,2]
   charge  直线突击    war    range 3 [2,4] · power 1.7 [1.2,2.2] · dash 0.12 [0,0.2]（每冲过一格 +）· push 1 [0,1]（击退；受阻时伤害 ×1.25）· morale −10 [−30,0]
   rampage 往来连斩    war    range 2 [1,3] · strikes 4 [2,7] · power 0.55 [0.25,1]（每斩）· morale −6 [−20,0]
   volley  远程齐射    war    range 3 [2,5] · power 1.8 [1,2.4] · radius 0 [0,1] · splash 0.6 [0.3,1] · pierce 0 [0,1] · morale −6 [−30,0]
   blaze   范围火攻    intel  range 3 [2,5] · radius 1 [0,2] · power 0.9 [0.5,1.3] · splash 0.75 [0.3,1] · burn 2 [0,3] · morale −8 [−30,0]
   storm   天候        intel  range 5 [3,7] · radius 2 [1,3] · power 0.7 [0.4,1] · burn 0 [0,3] · confuse 0 [0,0.5] · turns 1 [1,2] · morale −10 [−30,0]
   flood   水攻        intel  range 4 [2,6] · radius 1 [1,2] · power 0.85 [0.5,1.2]（近河 ×1.6、否则 ×0.85；并灭火）· confuse 0 [0,0.4] · turns 1 [1,2] · morale −12 [−30,0]
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
      name: '赤壁业火', kind: 'blaze', range: 4, radius: 1, power: 1.0, splash: 0.85, burn: 3, morale: -12,
      fx: 'fire', color: '#ff6a1a', cry: '樯橹灰飞烟灭！',
      desc: '赤壁一炬，樯橹灰飞烟灭。对四格内一支敌军纵火，周围一格的敌军同受火焚（林地威力倍增），并燃起持续三日的大火。',
    },
  });

  // ------------------------------------------------------------------------
  // 其余武将的条目按势力分组追加于此（董卓 / 袁绍 / 曹操 / 刘备 / 孙坚 / 袁术 / 公孙瓒 / 刘表 / 刘焉 /
  // 马腾 / 陶谦 / 孔融 / 韩馥 / 张鲁 / 张杨 / 刘繇 / 陆康 / 孟获 / 在野）。格式同上，例如：
  //   store.add({
  //     '颜良': { name: '…', kind: 'charge', range: 3, power: 1.6, …, fx: 'dash', color: '#…', desc: '…' },
  //   });
  // ------------------------------------------------------------------------
})();
