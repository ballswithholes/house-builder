'use strict';
/* ==========================================================================
   design-v4/engage/catalog.js —— ENGAGEMENT.md 的数据目录（事件、国策、营建、探索地、节庆、成就、初始羁绊与邦交）
   纯数据 + 声明式效果（不含函数），可原样写成 JSON（build-json.js → design-v4/engagement.json），
   实现时由 tools/gen-engage.js 生成 js/engage-data.js；Unity 由同一 JSON 生成 EngageData.g.cs。

   效果词汇（effects / pre / ok / fail 的每一项）：
     { k, ...amount }  amount 之一：
       add: n                绝对值（可负）
       pct: x                按当前值的比例（−0.05 = −5%）
       inc: x                × 本城月收入 inc = 产业×0.12 + 町×0.05（金）
       harv: x               × 本城预期秋收 harv = 土地×2.2（粮）
       range: [a, b]         闭区间整数（用事件流 rng）
     k（作用对象由前缀决定，缺省为本城）：
       城：gold food land industry town population order troops(按比例到驻城武将) training defense
       势力：prestige fame virtue（fame / virtue 记入「事件所得」桶，有上限，见 ENGAGEMENT §3）
       武将（actor = 选项的执行者 / 事件主角）：exp loyalty away(月) gtroops(绝对兵数，受带兵上限)
       officers.loyalty      城中全部非君主武将
       buff: { mod, v, months | untilMonth }   mod ∈ harvest income pop orderTarget eventW:<id> spread caravan foodPrice defense
       rel: { who: 'origin'|'neighbors'|'region'|'subject'|'all', add }
       bond: { add }         事件双方的亲密度
       reveal: 'here'|'near2'|'named:<姓名>'    揭示在野隐士（hidden = false）
       item: { pool, w: { common, uncommon, rare } }   ITEMS 的物品池（见 ENGAGEMENT §12.3）
       flag: { id, months }  城 / 势力标记（plague、levee、amnesty …）
       chain: { id, months } 若干月后触发后续事件
     条件（when）：months cultures families notFamilies scenario minLand minTown minPop minOrder maxOrder
       river coastal minCities minPrestige minVirtue maxVirtue minTier hub needGen needPair neighborWar
       hiddenHere freeHere date{from,to} cityKeys
   选项：{ id, label, cost: [效果], need: { gold, food, troops }, agent, roll: { p: { base, war, intel, pol, virtue, troops, order }, ok, fail },
           effects, ai, aiIf, rec }
     agent：'gov'（太守 / 主政）| 'ruler' | 'pick:war' | 'pick:intel' | 'pick:pol' | 'pick:any' | 'subject'（事件主角）
     ai：电脑选择的基础分（取最高者；aiIf 满足时 ×2，见 engage-proto.js aiChoose）
     rec：玩家界面的「推荐」标记规则（缺省 = 电脑会选的那一项）
   ========================================================================== */

// ------------------------------------------------------------------ 文化 → 城池家族（与 CITY-VIEW / domestic/city-stages.js 相同）--
const FAMILY = {
  han: 'hua', nanman: 'hua', korea: 'han3', wa: 'wa', steppe: 'steppe', sarmatian: 'steppe', tarim: 'oasis',
  kushan: 'indic', india: 'indic', persia: 'persia', arab: 'arabia', moor: 'arabia', africa: 'nile', roman: 'roman',
  celt: 'north', german: 'north', seasia: 'nanhai', yi: 'nanhai',
};

// 临河城（洪水 / 漕运）。临海城由剧本 SeaLinks 的端点得出（实现：CITY-VIEW 的 coastal / river 标记，二者取并）
const RIVER = [
  // 黄河 / 济水 / 淮水 / 汉水 / 长江 / 珠江 / 红河
  'pingyuan', 'puyang', 'chenliu', 'luoyang', 'ye', 'xiapi', 'shouchun', 'xiangyang', 'jiangxia', 'jiangling', 'jiangzhou',
  'yongan', 'lujiang', 'jianye', 'guangling', 'changsha', 'yuzhang', 'chengdu', 'changan', 'jiaozhi', 'panyu', 'cangwu', 'wan',
  // 两河 / 尼罗河 / 印度河 / 恒河 / 莱茵 / 多瑙 / 罗讷 / 台伯
  'ctesiphon', 'charax', 'susa', 'hatra', 'nisibis', 'edessa', 'alexandria', 'thebes', 'meroe', 'napata', 'karanog',
  'taxila', 'barbaricum', 'mathura', 'pataliputra', 'kausambi', 'tamralipti', 'colonia', 'mogontiacum', 'carnuntum',
  'aquincum', 'viminacium', 'sirmium', 'novae', 'lugdunum', 'roma', 'londinium', 'corduba', 'samarkand', 'bactra', 'merv',
  'oceo', 'beikthano', 'srikshetra', 'uraiyur', 'amaravati',
];

// ------------------------------------------------------------------ 事件 --
// cat：nature 天时 · society 民生 · court 朝堂 · people 人物 · global 天下（同月所有势力）
// scope：city / faction / pair / general
// modal：false = 无选项的「喜报」只进月初消息（不占打扰预算）
const E = [];
const ev = o => (E.push(o), o);

ev({ id: 'flood', name: '水灾', cat: 'nature', scope: 'city', icon: 'wave-breach', w: 6, cd: 24, disaster: true,
  when: { months: [5, 6, 7, 8, 9], riverOrCoast: true, minLand: 250, notFamilies: ['nile'] },
  variants: { nanhai: { name: '飓风', text: '海上飓风登岸，咸潮倒灌田庐。' }, steppe: { name: '春汛', text: '冰河解冻，河水漫过牧场。' } },
  text: '连日暴雨，{city}外河水决堤，田庐漂没。',
  pre: [{ k: 'land', pct: -0.05 }, { k: 'food', pct: -0.08 }, { k: 'population', pct: -0.01 }, { k: 'order', add: -5 }],
  options: [
    { id: 'relief', label: '开仓赈济', agent: 'gov', cost: [{ k: 'food', harv: -0.12 }], effects: [{ k: 'order', add: 8 }, { k: 'virtue', add: 1 }, { k: 'prestige', add: 4 }, { k: 'exp', add: 8 }, { k: 'population', pct: 0.005 }], ai: 2, aiIf: { foodOverHarv: 0.5 } },
    { id: 'levee', label: '征发民夫，修筑堤防', agent: 'gov', cost: [{ k: 'gold', inc: -2 }], effects: [{ k: 'land', pct: 0.03 }, { k: 'buff', mod: 'eventW:flood', v: 0.3, months: 36 }, { k: 'order', add: -2 }, { k: 'exp', add: 6 }], ai: 1, aiIf: { goldOverInc: 4 } },
    { id: 'none', label: '听之任之', effects: [{ k: 'order', add: -3 }], ai: 0.5 },
  ],
  src: ['hist:yellow-river'] });

ev({ id: 'drought', name: '旱灾', cat: 'nature', scope: 'city', icon: 'cracked-sun', w: 5, cd: 24, disaster: true,
  when: { months: [4, 5, 6, 7], notFamilies: ['steppe', 'nile'] },
  text: '入夏不雨，{city}田禾焦枯，秋收堪忧。',
  pre: [{ k: 'buff', mod: 'harvest', v: -0.20, untilHarvest: true }, { k: 'order', add: -3 }],
  options: [
    { id: 'canal', label: '开渠引水', agent: 'pick:pol', cost: [{ k: 'gold', inc: -3 }], roll: { p: { base: 0.35, pol: 1 / 200 }, ok: [{ k: 'buff', mod: 'harvest', v: 0.15, untilHarvest: true }, { k: 'exp', add: 10 }], fail: [{ k: 'buff', mod: 'harvest', v: 0.05, untilHarvest: true }, { k: 'exp', add: 5 }] }, ai: 1.5, aiIf: { goldOverInc: 5 } },
    { id: 'pray', label: '设坛祈雨', agent: 'ruler', cost: [{ k: 'gold', inc: -1 }], roll: { p: { base: 0.4 }, ok: [{ k: 'buff', mod: 'harvest', v: 0.10, untilHarvest: true }, { k: 'order', add: 3 }, { k: 'prestige', add: 3 }], fail: [{ k: 'order', add: -1 }] }, ai: 1 },
    { id: 'taxcut', label: '减免赋税', effects: [{ k: 'buff', mod: 'income', v: -0.30, months: 3 }, { k: 'order', add: 7 }, { k: 'virtue', add: 1 }], ai: 0.8, aiIf: { orderBelow: 50 } },
  ] });

ev({ id: 'locust', name: '蝗灾', cat: 'nature', scope: 'city', icon: 'locust-cloud', w: 4, cd: 36, disaster: true,
  when: { months: [6, 7, 8], families: ['hua', 'steppe', 'oasis', 'han3', 'persia', 'arabia', 'nile', 'indic'], minLat: 28 },
  text: '飞蝗蔽天而来，{city}禾稼尽为所食。',
  pre: [{ k: 'buff', mod: 'harvest', v: -0.30, untilHarvest: true }, { k: 'food', pct: -0.05 }],
  options: [
    { id: 'catch', label: '发兵捕蝗', agent: 'pick:war', need: { troops: 1000 }, cost: [{ k: 'gold', inc: -1 }, { k: 'training', add: -5 }], effects: [{ k: 'buff', mod: 'harvest', v: 0.20, untilHarvest: true }, { k: 'order', add: 3 }, { k: 'exp', add: 6 }], ai: 1.6, aiIf: { troopsOver: 2000 } },
    { id: 'pray', label: '祭祀禳灾', agent: 'ruler', cost: [{ k: 'gold', inc: -1 }], roll: { p: { base: 0.35 }, ok: [{ k: 'buff', mod: 'harvest', v: 0.15, untilHarvest: true }], fail: [] }, ai: 1 },
    { id: 'none', label: '无可奈何', effects: [], ai: 0.5 },
  ],
  src: ['wudiji-194-locust'] });

ev({ id: 'plague', name: '疫病', cat: 'nature', scope: 'city', icon: 'miasma', w: 3, wPop: true, cd: 36, disaster: true,
  when: { minPop: 150000 },
  text: '{city}疠气流行，家家有僵尸之痛，室室有号泣之哀。',
  pre: [{ k: 'population', pct: -0.03 }, { k: 'troops', pct: -0.03 }, { k: 'order', add: -6 }, { k: 'flag', id: 'plague', months: 3 }],
  options: [
    { id: 'doctor', label: '延医施药', agent: 'gov', cost: [{ k: 'gold', inc: -2 }], effects: [{ k: 'flag', id: 'plague', months: 0 }, { k: 'population', pct: 0.015 }, { k: 'order', add: 5 }, { k: 'virtue', add: 1 }, { k: 'prestige', add: 5 }, { k: 'exp', add: 6 }], healer: { costK: 0.5, effects: [{ k: 'population', pct: 0.01 }, { k: 'prestige', add: 5 }] }, ai: 2, aiIf: { goldOverInc: 3 } },
    { id: 'quarantine', label: '闭城隔离', effects: [{ k: 'flag', id: 'plague', months: 0 }, { k: 'buff', mod: 'income', v: -0.5, months: 2 }, { k: 'order', add: -2 }], ai: 1 },
    { id: 'charm', label: '悬符厌之', cost: [{ k: 'gold', inc: -0.5 }], roll: { p: { base: 0.3 }, ok: [{ k: 'flag', id: 'plague', months: 0 }], fail: [] }, ai: 0.6 },
  ],
  spread: { p: 0.25, months: 3, blockedBy: ['temple'] },
  src: ['caozhi-shuoyiqi', 'antonine-plague'] });

ev({ id: 'quake', name: '地震', cat: 'nature', scope: 'city', icon: 'split-ground', w: 1, cd: 60, disaster: true,
  text: '{city}地大震，城垣崩裂，民居多坏。',
  pre: [{ k: 'town', pct: -0.03 }, { k: 'buff', mod: 'defense', v: -6, months: 12 }, { k: 'order', add: -4 }, { k: 'population', pct: -0.005 }],
  options: [
    { id: 'rebuild', label: '发帑修缮', agent: 'gov', cost: [{ k: 'gold', inc: -2 }], effects: [{ k: 'town', pct: 0.04 }, { k: 'buff', mod: 'defense', v: 6, months: 12 }, { k: 'order', add: 3 }, { k: 'exp', add: 6 }], ai: 1.5, aiIf: { goldOverInc: 3 } },
    { id: 'none', label: '待来年再修', effects: [], ai: 0.5 },
  ] });

ev({ id: 'bumper', name: '丰年', cat: 'nature', scope: 'city', icon: 'golden-sheaf', w: 4, cd: 24, good: true,
  when: { months: [6, 7], minLand: 400 },
  text: '风调雨顺，{city}禾黍盈畴，今秋可望丰收。',
  pre: [{ k: 'buff', mod: 'harvest', v: 0.15, untilHarvest: true }],
  options: [
    { id: 'store', label: '广积粮', effects: [], ai: 1 },
    { id: 'sell', label: '平价粜粮，充实府库', effects: [{ k: 'gold', inc: 1.5 }, { k: 'food', harv: -0.10 }], ai: 1.2, aiIf: { foodOverHarv: 1 } },
    { id: 'feast', label: '举办庆丰会', cost: [{ k: 'gold', inc: -0.5 }], effects: [{ k: 'order', add: 6 }, { k: 'prestige', add: 4 }, { k: 'officers.loyalty', add: 2 }], ai: 0.9 },
  ] });

ev({ id: 'dzud', name: '白灾', cat: 'nature', scope: 'city', icon: 'snow-skull', w: 5, cd: 24, disaster: true,
  when: { months: [12, 1, 2], families: ['steppe'] },
  text: '大雪没胫，{city}牛羊冻毙无数。',
  pre: [{ k: 'troops', pct: -0.04 }, { k: 'food', pct: -0.10 }, { k: 'order', add: -4 }],
  options: [
    { id: 'share', label: '分畜赈济诸部', cost: [{ k: 'food', harv: -0.10 }], effects: [{ k: 'order', add: 6 }, { k: 'virtue', add: 1 }, { k: 'prestige', add: 3 }], ai: 1.5, aiIf: { foodOverHarv: 0.4 } },
    { id: 'raid', label: '南下就食', effects: [{ k: 'gold', inc: 2 }, { k: 'food', harv: 0.08 }, { k: 'rel', who: 'neighbors', add: -10 }], ai: 1 },
    { id: 'none', label: '熬过此冬', effects: [{ k: 'order', add: -2 }], ai: 0.4 },
  ],
  src: ['dzud'] });

ev({ id: 'nile', name: '尼罗河泛滥', cat: 'nature', scope: 'city', icon: 'nilometer', w: 8, cd: 11,
  when: { months: [8], families: ['nile'], river: true },
  text: '河水将涨，{city}的量水尺前祭司焚香，众人屏息以待。',
  pre: [{ k: 'roll3', p: [0.25, 0.35, 0.40], outcomes: [[{ k: 'buff', mod: 'harvest', v: -0.2, untilHarvest: true }], [], [{ k: 'buff', mod: 'harvest', v: 0.15, untilHarvest: true }]] }],
  options: [
    { id: 'rite', label: '献祭河神', cost: [{ k: 'gold', inc: -0.5 }], effects: [{ k: 'buff', mod: 'harvest', v: 0.07, untilHarvest: true }, { k: 'order', add: 2 }], ai: 1 },
    { id: 'none', label: '听天由命', effects: [], ai: 0.6 },
  ],
  src: ['nilometer'] });

ev({ id: 'snow', name: '瑞雪', cat: 'nature', scope: 'city', icon: 'snowflake', w: 3, cd: 12, good: true, modal: false,
  when: { months: [12, 1], families: ['hua', 'han3', 'steppe', 'north', 'wa'] },
  text: '{city}瑞雪兆丰年。', pre: [{ k: 'buff', mod: 'harvest', v: 0.05, untilHarvest: true }, { k: 'order', add: 1 }] });

ev({ id: 'bandits', name: '山贼', cat: 'society', scope: 'city', icon: 'masked-blade', w: 4, wOrder: 'low', cd: 18,
  variants: { arabia: { name: '劫道游骑', text: '沙漠游骑截断商道，劫掠驼队。' }, steppe: { name: '马贼', text: '一伙马贼出没草原，抢夺牛羊。' }, north: { name: '海寇', text: '海寇驾长船登岸劫掠。' }, nanhai: { name: '海盗', text: '海盗出没港湾，劫掠商船。' } },
  text: '{city}附近有贼众啸聚山林，劫掠乡里。',
  options: [
    { id: 'suppress', label: '发兵讨伐', agent: 'pick:war', need: { troops: 500 }, roll: { p: { base: 0.35, war: 1 / 160, troops: 1 / 20000, cap: 0.95 }, ok: [{ k: 'gold', inc: 1.5 }, { k: 'order', add: 5 }, { k: 'exp', add: 15 }, { k: 'prestige', add: 2 }], fail: [{ k: 'gtroops', pct: -0.08 }, { k: 'order', add: -2 }, { k: 'exp', add: 6 }, { k: 'buff', mod: 'income', v: -0.08, months: 4 }] }, ai: 2, aiIf: { genWar: 70, troopsOver: 2000 } },
    { id: 'amnesty', label: '遣使招安', agent: 'pick:any', cost: [{ k: 'gold', inc: -2 }], roll: { p: { base: 0.30, best: 1 / 200, virtue: 1 / 300 }, ok: [{ k: 'gtroops', range: [400, 900] }, { k: 'order', add: 3 }, { k: 'exp', add: 12 }, { k: 'reveal', who: 'bandit' }], fail: [{ k: 'order', add: -2 }] }, ai: 1.2 },
    { id: 'ignore', label: '闭城自守', effects: [{ k: 'buff', mod: 'income', v: -0.15, months: 4 }, { k: 'order', add: -4 }], ai: 0.3 },
  ] });

ev({ id: 'refugees', name: '流民', cat: 'society', scope: 'city', icon: 'bundle-road', w: 5, cd: 12,
  when: { neighborTrouble: true },
  text: '邻境兵荒马乱，数千流民扶老携幼来到{city}城下。',
  options: [
    { id: 'settle', label: '安置屯垦', agent: 'gov', cost: [{ k: 'food', harv: -0.08 }, { k: 'gold', inc: -1 }], effects: [{ k: 'population', pct: 0.03 }, { k: 'land', add: 10 }, { k: 'order', add: 3 }, { k: 'virtue', add: 1 }, { k: 'exp', add: 6 }], ai: 1.5, aiIf: { foodOverHarv: 0.4 } },
    { id: 'enlist', label: '择其壮者编为新兵', agent: 'gov', effects: [{ k: 'gtroops', range: [600, 1200], training: 25 }, { k: 'population', pct: 0.01 }, { k: 'order', add: -2 }], ai: 1.3, aiIf: { threatened: true } },
    { id: 'refuse', label: '闭门不纳', effects: [{ k: 'virtue', add: -1 }], ai: 0.4 },
  ] });

ev({ id: 'clan', name: '豪族相争', cat: 'society', scope: 'city', icon: 'two-seals', w: 3, cd: 24,
  when: { minTown: 350 },
  text: '{city}两大豪族争夺田产，各聚宾客，几至械斗。',
  options: [
    { id: 'judge', label: '依法裁断', agent: 'gov', roll: { p: { base: 0.30, pol: 1 / 150 }, ok: [{ k: 'order', add: 6 }, { k: 'exp', add: 12 }, { k: 'prestige', add: 3 }], fail: [{ k: 'order', add: -3 }, { k: 'loyalty', add: -2 }, { k: 'exp', add: 4 }] }, ai: 1.4, aiIf: { govPol: 70 } },
    { id: 'favor', label: '偏袒大族，收其献金', effects: [{ k: 'gold', inc: 2 }, { k: 'order', add: -5 }, { k: 'virtue', add: -1 }], ai: 0.9 },
    { id: 'mediate', label: '设宴调停', agent: 'gov', cost: [{ k: 'gold', inc: -1 }], effects: [{ k: 'order', add: 3 }, { k: 'exp', add: 6 }], ai: 1 },
  ] });

ev({ id: 'volunteers', name: '义士来投', cat: 'society', scope: 'city', icon: 'raised-spears', w: 2.5, wOrder: 'high', cd: 18, good: true,
  when: { minOrder: 65 },
  text: '{city}民心归附，乡里义士数百人自备刀甲前来投军。',
  options: [
    { id: 'accept', label: '收为部曲', agent: 'pick:war', effects: [{ k: 'gtroops', range: [300, 800], training: 45 }, { k: 'exp', add: 4 }], ai: 2 },
    { id: 'reward', label: '赐金遣归，以安乡里', cost: [{ k: 'gold', inc: -0.5 }], effects: [{ k: 'order', add: 4 }, { k: 'prestige', add: 2 }], ai: 0.5 },
  ] });

ev({ id: 'merchant', name: '行商过境', cat: 'society', scope: 'city', icon: 'camel-bale', w: 4, cd: 12, good: true,
  when: { minTier: 2 },
  variants: { nanhai: { name: '海舶来港', text: '一艘满载香料的海舶泊于{city}港口。' }, roman: { name: '商船入港', text: '一队商船自远方而来。' } },
  text: '一队远方行商途经{city}，驼铃叮当，货物琳琅。',
  options: [
    { id: 'buy', label: '看看他们的货物', opensShop: 'caravan', effects: [], ai: 0.8, aiIf: { goldOver: 1200 } },
    { id: 'tax', label: '课以关市之税', effects: [{ k: 'gold', inc: 1 }, { k: 'rel', who: 'origin', add: -2 }], ai: 1.2 },
    { id: 'host', label: '盛情款待', cost: [{ k: 'gold', inc: -0.5 }], effects: [{ k: 'rel', who: 'origin', add: 5 }, { k: 'buff', mod: 'caravan', v: 0.05, months: 12 }, { k: 'prestige', add: 1 }], ai: 0.7 },
  ],
  note: 'ITEMS §6.5 的胡商 / 海商到访以本卡呈现：「看看他们的货物」打开该商队的货架。' });

ev({ id: 'fire', name: '火灾', cat: 'society', scope: 'city', icon: 'roof-flame', w: 2, wTown: true, cd: 36, disaster: true,
  when: { minTown: 400 },
  text: '夜半{city}市中失火，延烧数百家。',
  pre: [{ k: 'town', pct: -0.03 }, { k: 'gold', pct: -0.05 }],
  options: [
    { id: 'rebuild', label: '赈灾重建', agent: 'gov', cost: [{ k: 'gold', inc: -1.5 }], effects: [{ k: 'town', pct: 0.045 }, { k: 'order', add: 3 }, { k: 'exp', add: 6 }], ai: 1.3, aiIf: { goldOverInc: 3 } },
    { id: 'none', label: '令灾民自行修葺', effects: [{ k: 'order', add: -2 }], ai: 0.5 },
  ] });

ev({ id: 'praise', name: '民谣颂德', cat: 'society', scope: 'city', icon: 'singing-scroll', w: 2, cd: 24, good: true, modal: false,
  when: { govPol: 75, minOrder: 70 },
  text: '{city}童谣传唱太守{gov}的德政。',
  pre: [{ k: 'prestige', add: 5 }, { k: 'exp', add: 10, who: 'gov' }, { k: 'loyalty', add: 3, who: 'gov' }] });

ev({ id: 'sage', name: '游方高士', cat: 'court', scope: 'faction', icon: 'staff-gourd', w: 3, wVirtue: true, cd: 18, good: true,
  personas: 'SAGES',
  text: '{persona}云游至{city}，愿与{ruler}一晤。',
  options: [
    { id: 'host', label: '延为上宾，请其指点', agent: 'pick:intel', cost: [{ k: 'gold', inc: -2 }], effects: [{ k: 'exp', add: 40, intelBonus: 20 }, { k: 'prestige', add: 5 }], ai: 1.2 },
    { id: 'counsel', label: '问以天下大计', effects: [{ k: 'prestige', add: 8 }, { k: 'reveal', who: 'near2', orMission: true }], ai: 1 },
    { id: 'farewell', label: '赠以盘缠，礼送出境', cost: [{ k: 'gold', add: -30 }], effects: [{ k: 'virtue', add: 1 }], ai: 0.4 },
  ] });

ev({ id: 'talent', name: '荐贤', cat: 'court', scope: 'city', icon: 'hand-scroll', w: 2, cd: 12, good: true, modal: false,
  when: { minVirtue: 60, hiddenHere: true },
  text: '{city}父老荐举隐士{hidden}，称其才堪大用。', pre: [{ k: 'reveal', who: 'here' }] });

ev({ id: 'envoy', name: '天子诏使', cat: 'court', scope: 'faction', icon: 'yellow-edict', w: 1.5, cd: 24,
  when: { scenario: 'classic', minFame: 55, minCities: 3, date: { to: '219-12' } },
  text: '天子遣使至{capital}，嘉{ruler}之功，望其奉表进贡以尊汉室。',
  options: [
    { id: 'tribute', label: '奉表进贡', where: 'capital', cost: [{ k: 'gold', inc: -3 }], effects: [{ k: 'prestige', add: 25 }, { k: 'fame', add: 1 }, { k: 'rel', who: 'region', add: 5 }], ai: 1, aiIf: { goldOverInc: 6 } },
    { id: 'decline', label: '婉言谢绝', effects: [], ai: 0.5 },
  ] });

ev({ id: 'comet', name: '彗星', cat: 'global', scope: 'faction', icon: 'broom-star', w: 0, globalP: 1 / 40, cd: 36,
  variants: { any: { name: '日食', text: '日有食之，天下震恐。' } },
  text: '彗星出于东方，光芒数丈，人心惶惶。',
  options: [
    { id: 'edict', label: '下罪己诏', effects: [{ k: 'virtue', add: 1 }, { k: 'order', add: 2, all: true }], ai: 1, aiIf: { minVirtue: 60 } },
    { id: 'rite', label: '祭天禳灾', where: 'capital', cost: [{ k: 'gold', inc: -1 }], effects: [{ k: 'order', add: 3, all: true }, { k: 'prestige', add: 2 }], ai: 1.1, aiIf: { goldOverInc: 3 } },
    { id: 'ignore', label: '天象不足畏', effects: [{ k: 'order', add: -2, all: true }], ai: 0.6 },
  ],
  note: '天下事件：同月每个势力各自抉择；电脑按 ai 分自动。' });

ev({ id: 'omen', name: '祥瑞', cat: 'court', scope: 'faction', icon: 'qilin', w: 1.5, cd: 24, good: true,
  when: { minAvgOrder: 70, minPrestige: 100 },
  personas: 'OMENS',
  text: '{city}有{omen}出现，百姓奔走相告。',
  options: [
    { id: 'proclaim', label: '布告天下', effects: [{ k: 'prestige', add: 20 }, { k: 'fame', add: 1 }, { k: 'rel', who: 'neighbors', add: -3 }], ai: 1 },
    { id: 'humble', label: '谦让不受', effects: [{ k: 'virtue', add: 2 }, { k: 'prestige', add: 5 }], ai: 1, aiIf: { minVirtue: 70 } },
  ] });

ev({ id: 'deserter', name: '降将来投', cat: 'court', scope: 'general', icon: 'turned-banner', w: 3, cd: 12,
  when: { deserterNear: true },
  text: '{faction}的{subject}不满其主，遣心腹密书，愿率部来投。',
  options: [
    { id: 'accept', label: '纳之', effects: [{ k: 'defect', troopsK: 0.5 }, { k: 'rel', who: 'subject', add: -15 }], ai: 2 },
    { id: 'refuse', label: '婉拒，以全邻好', effects: [{ k: 'rel', who: 'subject', add: 5 }], ai: 0.5 },
  ] });

ev({ id: 'gift', name: '远方来献', cat: 'court', scope: 'faction', icon: 'tribute-box', w: 1.5, cd: 18, good: true,
  when: { minPrestige: 250, farFaction: 4 },
  text: '远在{far}的{farRuler}遣使来朝，献上{item}。',
  options: [
    { id: 'return', label: '厚加回赐', where: 'capital', cost: [{ k: 'gold', inc: -2 }], effects: [{ k: 'item', pool: 'farGift', w: { common: 70, uncommon: 30 } }, { k: 'rel', who: 'far', add: 15 }, { k: 'prestige', add: 8 }], ai: 1 },
    { id: 'accept', label: '受礼', effects: [{ k: 'item', pool: 'farGift', w: { common: 70, uncommon: 30 } }, { k: 'rel', who: 'far', add: 3 }], ai: 1 },
  ] });

ev({ id: 'slander', name: '谗言', cat: 'people', scope: 'pair', icon: 'whisper', w: 2, cd: 12,
  when: { needPair: 'enemies' },
  text: '{a}向主公进言，称{b}心怀异志。',
  options: [
    { id: 'investigate', label: '查明真相', agent: 'ruler', roll: { p: { base: 0.4, intel: 1 / 200 }, ok: [{ k: 'loyalty', add: -5, who: 'a' }, { k: 'loyalty', add: 5, who: 'b' }, { k: 'bond', add: -10 }], fail: [{ k: 'loyalty', add: -6, who: 'b' }] }, ai: 1 },
    { id: 'ignore', label: '置之不理', effects: [{ k: 'loyalty', add: -3, who: 'b' }], ai: 1.1 },
    { id: 'separate', label: '使二人分驻两城', effects: [{ k: 'separate' }], ai: 0.5 },
  ] });

ev({ id: 'spar', name: '切磋武艺', cat: 'people', scope: 'pair', icon: 'crossed-spears', w: 3, cd: 6, pairCd: 24, good: true,
  when: { needPair: 'spar' },
  text: '{a}与{b}在校场比试武艺，引得军士围观喝彩。',
  options: [
    { id: 'watch', label: '击鼓助兴', effects: [{ k: 'exp', add: 15, who: 'a' }, { k: 'exp', add: 15, who: 'b' }, { k: 'bond', add: 10, rivalIfClose: 0.3 }], ai: 1 },
    { id: 'duel', label: '亲临观战（切磋单挑）', playerOnly: true, duel: 'friendly', effects: [{ k: 'bond', add: 10 }], win: [{ k: 'exp', add: 25, who: 'winner' }, { k: 'exp', add: 15, who: 'loser' }, { k: 'loyalty', add: 3, who: 'winner' }] },
  ] });

ev({ id: 'banquet', name: '宴饮', cat: 'people', scope: 'faction', icon: 'wine-jue', w: 2, cd: 6, good: true,
  text: '{ruler}在府中设宴款待诸将，觥筹交错。',
  options: [
    { id: 'touhu', label: '投壶为乐', cost: [{ k: 'gold', inc: -0.3 }], miniGame: 'touhu', effects: [{ k: 'loyalty3', range: [2, 5] }, { k: 'bond3', add: 5 }], ai: 1 },
    { id: 'plain', label: '饮罢早散', effects: [{ k: 'loyalty3', add: 1 }], ai: 0.8 },
  ] });

ev({ id: 'study', name: '求学', cat: 'people', scope: 'general', icon: 'book-bundle', w: 2, cd: 12,
  when: { needGen: { minIntel: 65, maxLevel: 6 } },
  text: '{subject}请求告假，往{place}游学两月。',
  options: [
    { id: 'grant', label: '准其游学', effects: [{ k: 'away', add: 2 }, { k: 'expLater', add: 50 }, { k: 'loyalty', add: 4 }], ai: 1, aiIf: { notThreatened: true } },
    { id: 'deny', label: '军务繁忙，不准', effects: [{ k: 'loyalty', add: -3 }], ai: 0.5 },
  ] });

ev({ id: 'homesick', name: '思乡', cat: 'people', scope: 'general', icon: 'far-moon', w: 1.5, cd: 24,
  when: { needGen: { foreign: true } },
  text: '{subject}远离故土{home}，月下吹笛，思乡之情溢于言表。',
  options: [
    { id: 'gift', label: '厚赐慰问', cost: [{ k: 'gold', inc: -1 }], effects: [{ k: 'loyalty', add: 6 }], ai: 1 },
    { id: 'leave', label: '准其归省', effects: [{ k: 'away', add: 2 }, { k: 'loyalty', add: 10 }], ai: 0.8 },
    { id: 'ignore', label: '大丈夫四海为家', effects: [{ k: 'loyalty', add: -5 }], ai: 0.3 },
  ] });

ev({ id: 'restless', name: '求战心切', cat: 'people', scope: 'general', icon: 'gauntlet', w: 2, cd: 12,
  when: { needGen: { minWar: 80, idleMonths: 12 } },
  text: '{subject}按剑而叹：「髀肉复生，何时方得驰骋沙场？」',
  options: [
    { id: 'promise', label: '许以先锋之任', effects: [{ k: 'loyalty', add: 3 }, { k: 'flag', id: 'vanguard', months: 12 }], ai: 1 },
    { id: 'tourney', label: '荐其出战武林大会', needTourneyWithin: 6, effects: [{ k: 'loyalty', add: 5 }, { k: 'flag', id: 'tourneyPick', months: 12 }], ai: 1.2 },
    { id: 'ignore', label: '静候时机', effects: [{ k: 'loyalty', add: -4 }], ai: 0.3 },
  ] });

ev({ id: 'oath', name: '结义', cat: 'people', scope: 'pair', icon: 'peach-blossom', w: 4, cd: 12, good: true,
  when: { needPair: 'oath' },
  text: '{a}与{b}意气相投，愿焚香结为异姓兄弟。',
  options: [
    { id: 'swear', label: '设宴为二人见证', cost: [{ k: 'gold', add: -100 }], effects: [{ k: 'bondKind', kind: 'sworn' }, { k: 'loyalty', add: 5, who: 'a' }, { k: 'loyalty', add: 5, who: 'b' }, { k: 'prestige', add: 5 }], ai: 1.5 },
    { id: 'later', label: '改日再议', effects: [], ai: 0.3 },
  ] });

ev({ id: 'mentor', name: '拜师', cat: 'people', scope: 'pair', icon: 'brush-stand', w: 3, cd: 12, good: true,
  when: { needPair: 'mentor' },
  text: '{b}仰慕{a}之才，欲执弟子之礼。',
  options: [
    { id: 'accept', label: '行拜师礼', cost: [{ k: 'gold', add: -50 }], effects: [{ k: 'bondKind', kind: 'mentor' }, { k: 'exp', add: 10, who: 'b' }], ai: 1.5 },
    { id: 'decline', label: '各司其职', effects: [], ai: 0.3 },
  ] });

ev({ id: 'quarrel', name: '争功', cat: 'people', scope: 'pair', icon: 'broken-tally', w: 2, cd: 12,
  when: { needPair: 'quarrel' },
  text: '战后{a}与{b}为争头功，在帐中拔剑相向。',
  options: [
    { id: 'reconcile', label: '设宴调解', agent: 'ruler', roll: { p: { base: 0.4, pol: 1 / 200, virtue: 1 / 400 }, ok: [{ k: 'bond', add: 20 }, { k: 'loyalty', add: 2, who: 'a' }, { k: 'loyalty', add: 2, who: 'b' }], fail: [{ k: 'bond', add: -10 }] }, ai: 1.2 },
    { id: 'side', label: '各论其功，赏罚分明', effects: [{ k: 'loyalty', add: 5, who: 'a' }, { k: 'loyalty', add: -6, who: 'b' }, { k: 'bond', add: -15 }], ai: 0.8 },
  ] });

ev({ id: 'proposal', name: '献策', cat: 'court', scope: 'general', icon: 'scroll-map', w: 2, cd: 18, good: true,
  when: { needGen: { minIntel: 85 } },
  personas: 'PROPOSALS',
  text: '{subject}献上「{plan}」之策。',
  options: [
    { id: 'adopt', label: '依计而行', effects: [{ k: 'plan' }, { k: 'exp', add: 15, who: 'subject' }, { k: 'loyalty', add: 2, who: 'subject' }], ai: 1.2 },
    { id: 'shelve', label: '容后再议', effects: [], ai: 0.3 },
  ] });

// 剧本史事（经典剧本；按日期与城池触发，概率 p，其后照常抽取）
const HISTORICAL = [
  { id: 'h194locust', event: 'locust', date: '194-06', cityKeys: ['puyang', 'chenliu', 'xuchang', 'pingyuan', 'xiaopei'], p: 0.7, scenario: 'classic', text: '兴平元年，蝗虫起，百姓大饿。', src: ['wudiji-194-locust'] },
  { id: 'h197famine', event: 'drought', date: '197-05', cityKeys: ['shouchun', 'lujiang', 'guangling', 'runan'], p: 0.6, scenario: 'classic', text: '江淮间旱，民相食。', src: ['yuanshu-197'] },
  { id: 'h208plague', event: 'plague', date: '208-11', cityKeys: ['jiangling', 'jiangxia', 'changsha'], p: 0.6, scenario: 'classic', text: '大疫，吏士多死者。', src: ['wudiji-208'] },
  { id: 'h217plague', event: 'plague', date: '217-02', cityKeys: ['xuchang', 'ye', 'luoyang', 'chenliu'], p: 0.7, scenario: 'classic', text: '建安二十二年，疠气流行。', src: ['caozhi-shuoyiqi'] },
  { id: 'h190antonine', event: 'plague', date: '190-03', cityKeys: ['roma', 'aquileia', 'alexandria'], p: 0.5, scenario: 'world', text: '大疫复起，罗马城中日死者两千人。', src: ['antonine-plague'] },
];

// 人物模板（游方高士、祥瑞、献策）
const SAGES = {
  han: [
    { name: '许劭', title: '月旦评', where: ['runan'], date: { to: '194-12' }, special: 'yuedan', src: ['houhanshu-xushao'] },
    { name: '司马徽', title: '水镜先生', where: ['xiangyang', 'wan', 'jiangling'], date: { to: '207-12' }, special: 'shuijing', src: ['xiangyangji'] },
    { name: '郑玄', title: '经学大师', where: ['beihai', 'pingyuan'], date: { to: '200-06' }, special: 'zhengxuan' },
    { name: '管宁', title: '辽东高士', where: ['beiping', 'xiangping'], special: null },
    { name: '于吉', title: '琅邪道士', where: ['wu', 'kuaiji', 'jianye'], date: { to: '200-04' }, special: 'yuji' },
    { name: '张机', title: '长沙名医', where: ['changsha', 'wan'], special: 'doctor' },
  ],
  roman: [{ name: '盖伦', title: '御医', where: ['roma', 'ephesus'], special: 'doctor', src: ['antonine-plague'] }, { name: '游方智者', title: '诡辩家', where: [], special: null }],
  persia: [{ name: '麻葛', title: '祭司', where: [], special: null }],
  indic: [{ name: '婆罗门', title: '论师', where: [], special: null }, { name: '比丘', title: '沙门', where: [], special: null }],
  steppe: [{ name: '巫觋', title: '萨满', where: [], special: null }],
  north: [{ name: '德鲁伊', title: '智者', where: [], special: null }, { name: '女先知', title: '预言者', where: [], special: null }],
  wa: [{ name: '巫女', title: '鬼道', where: [], special: null }],
  any: [{ name: '隐者', title: '高士', where: [], special: null }],
};
const OMENS = { hua: ['白鹿', '甘露', '凤凰', '麒麟', '嘉禾'], roman: ['雄鹰', '双头牛犊'], persia: ['圣火自燃'], indic: ['白象'], any: ['祥云', '白鹿'] };
const PROPOSALS = [
  { id: 'tuntian', name: '屯田', effect: [{ k: 'projectDiscount', project: 'tuntian', v: 0.4 }], src: ['tuntian-196'] },
  { id: 'yuanjiao', name: '远交近攻', effect: [{ k: 'rel', who: 'far', add: 15 }, { k: 'rel', who: 'neighbors', add: -5 }] },
  { id: 'jiliang', name: '广积粮', effect: [{ k: 'buff', mod: 'foodPrice', v: -0.2, months: 3 }] },
  { id: 'xuanlian', name: '选练精兵', effect: [{ k: 'training', add: 5, where: 'capital' }] },
];

// ------------------------------------------------------------------ 国策（君命） --
// tier：1 易 2 中 3 难；dl：期限（月）；reward 金 = k × 势力月收入；item 概率
const MISSIONS = [
  { id: 'm_dev', name: '兴修', goal: 'cityStatGain', params: { delta: [60, 100, 150] }, dl: [8, 12, 18], weight: 'devRoom' },
  { id: 'm_grain', name: '广积粮', goal: 'foodMonths', params: { months: [6, 10, 14] }, dl: [6, 10, 14], weight: 'lowFood' },
  { id: 'm_talent', name: '求贤', goal: 'hires', params: { n: [1, 2, 3] }, dl: [6, 12, 18], weight: 'fewGens' },
  { id: 'm_train', name: '练兵', goal: 'drilled', params: { n: [1, 2, 4], troops: 3000, training: 90 }, dl: [6, 9, 12] },
  { id: 'm_order', name: '安民', goal: 'minOrder', params: { order: [60, 70, 80] }, dl: [8, 12, 18], weight: 'lowOrder' },
  { id: 'm_trade', name: '通商', goal: 'caravans', params: { n: [1, 2, 4], foreignHard: true }, dl: [8, 12, 18] },
  { id: 'm_ally', name: '结好', goal: 'relation', params: { rel: [40, 60, 80] }, dl: [8, 12, 18] },
  { id: 'm_build', name: '营建', goal: 'project', params: {}, dl: [10, 14, 20] },
  { id: 'm_explore', name: '探索', goal: 'expeditions', params: { n: [1, 2, 2], farHard: true }, dl: [8, 12, 18] },
  { id: 'm_wenhui', name: '文会', goal: 'wenhuiPlace', params: { place: [3, 2, 1] }, dl: [12, 12, 12], season: 9 },
  { id: 'm_tourney', name: '武林', goal: 'tourneyPlace', params: { place: [3, 2, 1] }, dl: [12, 12, 12], season: 1 },
  { id: 'm_collect', name: '集珍', goal: 'items', params: { n: [2, 3, 1], rarity: ['common', 'uncommon', 'rare'] }, dl: [8, 12, 18] },
  { id: 'm_title', name: '授官', goal: 'titled', params: { n: [2, 3, 2], tier: [1, 2, 3] }, dl: [8, 12, 18], needs: 'titles' },
  { id: 'm_level', name: '历练', goal: 'levels', params: { n: [2, 3, 3], level: [3, 4, 5] }, dl: [8, 12, 18] },
  { id: 'm_city', name: '名城', goal: 'cityLevel', params: { level: [2, 3, 4] }, dl: [10, 16, 24] },
  { id: 'm_defend', name: '固守', goal: 'defense', params: { def: [50, 60, 70] }, dl: [6, 10, 14], weight: 'threat' },
  { id: 'm_conquer', name: '拓土', goal: 'conquer', params: { n: [1, 1, 2] }, dl: [6, 9, 12], military: true },
];
const MISSION_REWARD = { goldK: [0.4, 0.8, 1.2], prestige: [10, 20, 35], exp: [20, 35, 50], item: [null, { uncommon: 0.25 }, { uncommon: 0.6, rare: 0.15 }], fame: [0, 0, 1] };

// ------------------------------------------------------------------ 营建 --
// cost 金；work 工作量（每月进度 = 10 + 监工政治 / 8）；req 前提；fx 效果（城）
const PROJECTS = [
  { id: 'irrigation', name: '水利', cost: 400, work: 100, req: { minLand: 300 }, fx: { harvest: 0.12, disasterK: { flood: 0.4, drought: 0.4 }, orderTarget: 2 }, anchor: 'farms',
    names: { hua: '陂塘水渠', persia: '坎儿井', roman: '引水渠', indic: '蓄水池', nile: '水车与渠', oasis: '坎儿井', arabia: '水坝', han3: '堤堰', wa: '水田堰', steppe: '引水渠', north: '排水沟', nanhai: '水渠' },
    src: ['shaobei', 'tuntian-196'] },
  { id: 'tuntian', name: '屯田', cost: 300, work: 80, req: { minTroopsHere: 3000 }, fx: { troopFood: -0.25, harvest: 0.05 }, anchor: 'farms',
    names: { hua: '屯田', roman: '军垦地', persia: '屯垦', steppe: '营田', any: '军屯' }, src: ['tuntian-196'] },
  { id: 'market', name: '扩建市集', cost: 500, work: 100, req: { minIndTown: 600 }, fx: { income: 0.06, shopTier: 1, caravan: 0.04 }, anchor: 'market',
    names: { hua: '大市', roman: '广场集市', persia: '巴扎', arabia: '商队客栈', indic: '集市', any: '市集' } },
  { id: 'roads', name: '驿道', cost: 350, work: 80, req: {}, fx: { caravanRisk: -0.5, caravanTime: -1, income: 0.02, orderTarget: 3 }, anchor: 'walls',
    names: { hua: '驿道与传舍', roman: '罗马大道', persia: '御道驿站', steppe: '驿马站', any: '驿道' } },
  { id: 'academy', name: '书院', cost: 450, work: 100, req: { minTown: 350 }, fx: { intelExp: 2, search: 0.06, revealP: 0.03, wenhui: 5 }, anchor: 'academy',
    names: { hua: '学宫', roman: '学园', indic: '精舍', persia: '书院', oasis: '佛寺讲堂', any: '学馆' } },
  { id: 'drill', name: '演武场', cost: 350, work: 80, req: {}, fx: { train: 3, recruitTraining: 10, tourney: 5 }, anchor: 'drill',
    names: { hua: '讲武堂', roman: '战神校场', steppe: '射猎场', any: '演武场' } },
  { id: 'walls', name: '增筑城墙', cost: 500, work: 120, req: { minTown: 300 }, fx: { defense: 10 }, anchor: 'walls',
    names: { hua: '重城', roman: '城墙与塔楼', steppe: '木栅营垒', north: '土垒木墙', any: '城墙' } },
  { id: 'temple', name: '祠庙', cost: 400, work: 90, req: {}, fx: { orderTarget: 8, plagueK: 0.5, festival: 0.5 }, anchor: 'temple',
    names: { hua: '宗庙', roman: '神殿', persia: '火庙', indic: '佛塔', oasis: '佛寺', arabia: '大神殿', nile: '阿佩德马克神庙', north: '圣林', wa: '祭殿', han3: '祭坛', steppe: '祭天坛', nanhai: '神祠' } },
  { id: 'granary', name: '常平仓', cost: 300, work: 70, req: {}, fx: { disasterFoodK: 0.5, buyK: -0.15, sellK: 0.15 }, anchor: 'granary',
    names: { hua: '常平仓', roman: '国家粮仓', any: '大仓' } },
  { id: 'forge', name: '冶铁工坊', cost: 450, work: 100, req: { minIndustry: 400 }, fx: { devIndustry: 0.10, shopUncommon: 1, recruitCost: -0.10 }, anchor: 'workshop',
    names: { hua: '铁官', roman: '兵器工场', indic: '钢坊', any: '冶铁工坊' } },
  { id: 'harbour', name: '港口', cost: 500, work: 110, req: { coastalOrRiver: true }, fx: { seaCaravan: 0.06, income: 0.03 }, anchor: 'harbour',
    names: { hua: '津渡船坞', roman: '港口与灯塔', indic: '港埠', any: '港口' } },
  { id: 'beacons', name: '烽燧', cost: 250, work: 60, req: { frontier: true }, fx: { warning: 1, defK: 0.05 }, anchor: 'walls',
    names: { hua: '烽燧亭障', roman: '边墙哨塔', steppe: '哨所', any: '烽燧' } },
  { id: 'wonder', name: '奇观', cost: 2000, work: 300, req: { capital: true, oncePerFaction: true, minCityLevel: 3 }, fx: { prestigeMonthly: 5, festival: 0.5, orderTarget: 5, wenhuiHost: 1 }, anchor: 'palace',
    names: { hua: '铜雀台', roman: '凯旋门', persia: '王宫大殿', indic: '大塔', oasis: '大佛寺', arabia: '贝勒神殿', nile: '金字塔', north: '巨石圣环', wa: '大墓', han3: '东盟祭坛', steppe: '龙城金帐', nanhai: '金塔' },
    src: ['tongquetai-210'] },
];

// ------------------------------------------------------------------ 探索地 --
// type：mountain 名山 · ruin 古迹 · wild 荒原 · sea 海外 · holy 圣地 · mine 矿山；near：最近城键；myth：传说（显示「传说」标记）
const SITES = [
  { id: 'taishan', name: '泰山', type: 'holy', near: 'beihai', culture: 'han' },
  { id: 'kunlun', name: '昆仑', type: 'mountain', near: 'yutian', culture: 'tarim', myth: true },
  { id: 'penglai', name: '蓬莱', type: 'sea', near: 'beihai', culture: 'han', myth: true },
  { id: 'songshan', name: '嵩山', type: 'mountain', near: 'luoyang', culture: 'han' },
  { id: 'emei', name: '峨眉', type: 'mountain', near: 'chengdu', culture: 'han' },
  { id: 'yuxue', name: '会稽禹穴', type: 'ruin', near: 'kuaiji', culture: 'han' },
  { id: 'lishan', name: '骊山', type: 'ruin', near: 'changan', culture: 'han' },
  { id: 'dongting', name: '洞庭君山', type: 'mountain', near: 'changsha', culture: 'han' },
  { id: 'nanzhong', name: '南中瘴林', type: 'wild', near: 'yunnan', culture: 'nanman' },
  { id: 'hepuzhu', name: '合浦珠池', type: 'sea', near: 'hepu', culture: 'han' },
  { id: 'changbai', name: '长白山', type: 'mountain', near: 'guonei', culture: 'korea' },
  { id: 'izumo', name: '出云', type: 'holy', near: 'touma', culture: 'wa' },
  { id: 'loulan', name: '楼兰故城', type: 'ruin', near: 'loulan', culture: 'tarim' },
  { id: 'tianshan', name: '天山', type: 'mountain', near: 'qiuci', culture: 'tarim' },
  { id: 'altai', name: '金山', type: 'mine', near: 'jiankun', culture: 'steppe' },
  { id: 'ferghana', name: '贰师城牧场', type: 'wild', near: 'dayuan', culture: 'kushan' },
  { id: 'gandhara', name: '犍陀罗佛塔', type: 'holy', near: 'purushapura', culture: 'kushan' },
  { id: 'ganga', name: '恒河', type: 'holy', near: 'pataliputra', culture: 'india' },
  { id: 'ratnapura', name: '宝石山', type: 'mine', near: 'anuradhapura', culture: 'india' },
  { id: 'persepolis', name: '波斯波利斯废墟', type: 'ruin', near: 'istakhr', culture: 'persia' },
  { id: 'babylon', name: '巴比伦废墟', type: 'ruin', near: 'ctesiphon', culture: 'persia' },
  { id: 'behistun', name: '贝希斯敦', type: 'ruin', near: 'ecbatana', culture: 'persia' },
  { id: 'frankincense', name: '乳香之地', type: 'wild', near: 'zafar', culture: 'arab' },
  { id: 'maribdam', name: '马里卜大坝', type: 'ruin', near: 'marib', culture: 'arab' },
  { id: 'meroepyr', name: '麦罗埃金字塔', type: 'ruin', near: 'meroe', culture: 'africa' },
  { id: 'giza', name: '吉萨金字塔', type: 'ruin', near: 'alexandria', culture: 'roman' },
  { id: 'troy', name: '特洛伊遗址', type: 'ruin', near: 'ephesus', culture: 'roman' },
  { id: 'delphi', name: '德尔斐', type: 'holy', near: 'byzantium', culture: 'roman' },
  { id: 'olympia', name: '奥林匹亚', type: 'holy', near: 'byzantium', culture: 'roman' },
  { id: 'carthruin', name: '迦太基旧港', type: 'ruin', near: 'carthago', culture: 'roman' },
  { id: 'pillars', name: '赫拉克勒斯之柱', type: 'sea', near: 'tingis', culture: 'moor', myth: true },
  { id: 'cassiterides', name: '锡岛', type: 'mine', near: 'londinium', culture: 'celt' },
  { id: 'teutoburg', name: '条顿堡森林', type: 'ruin', near: 'colonia', culture: 'german' },
  { id: 'hercynia', name: '赫西尼亚森林', type: 'wild', near: 'marcomannia', culture: 'german' },
  { id: 'mekong', name: '扶南金洲', type: 'sea', near: 'oceo', culture: 'seasia' },
  { id: 'atlas', name: '阿特拉斯山', type: 'mountain', near: 'baquates', culture: 'moor', myth: true },
  // 经典剧本可达的另外 8 处（中原）
  { id: 'huashan', name: '华山', type: 'mountain', near: 'changan', culture: 'han' },
  { id: 'lushan', name: '庐山', type: 'mountain', near: 'yuzhang', culture: 'han' },
  { id: 'jiuyi', name: '九嶷山舜陵', type: 'holy', near: 'lingling', culture: 'han', myth: true },
  { id: 'qingcheng', name: '青城山', type: 'holy', near: 'chengdu', culture: 'han' },
  { id: 'baima', name: '白马寺', type: 'holy', near: 'luoyang', culture: 'han' },
  { id: 'qilian', name: '祁连山', type: 'mine', near: 'wuwei', culture: 'han' },
  { id: 'qufu', name: '曲阜孔林', type: 'holy', near: 'xiaopei', culture: 'han' },
  { id: 'zhandao', name: '秦岭栈道', type: 'wild', near: 'hanzhong', culture: 'han' },
];
const SITE_TYPES = {
  mountain: { gold: [40, 100], item: { common: 0.25, uncommon: 0.12, rare: 0.03 }, pool: ['medicine', 'amulet', 'music'], exp: 'intel', prestige: 5, mishap: 0.10 },
  ruin: { gold: [60, 150], item: { common: 0.30, uncommon: 0.15, rare: 0.04 }, pool: ['relic', 'milbook', 'civbook', 'seal', 'weapon'], exp: 'intel', prestige: 8, mishap: 0.08, clue: 0.08 },
  wild: { gold: [30, 80], item: { common: 0.30, uncommon: 0.14, rare: 0.035 }, pool: ['mount', 'jewel', 'medicine'], exp: 'war', prestige: 4, mishap: 0.15 },
  sea: { gold: [80, 200], item: { common: 0.30, uncommon: 0.15, rare: 0.04 }, pool: ['jewel', 'relic'], exp: 'war', prestige: 6, mishap: 0.14 },
  holy: { gold: [20, 60], item: { common: 0.20, uncommon: 0.12, rare: 0.03 }, pool: ['relic', 'amulet', 'civbook'], exp: 'pol', prestige: 15, mishap: 0.04, virtue: 1 },
  mine: { gold: [100, 220], item: { common: 0.25, uncommon: 0.10, rare: 0.025 }, pool: ['weapon', 'armour', 'jewel'], exp: 'war', prestige: 3, mishap: 0.12 },
};

// ------------------------------------------------------------------ 节庆 --
// 每个文化家族一个「招牌节庆」（月份、名称、出处、考据等级 A 当时当地 / B 同文化异时期 / C 设计补足）
const FESTIVALS = {
  hua: { id: 'la', name: '腊祭·大傩', month: 12, grade: 'A', src: ['houhanshu-liyi'], text: '先腊一日大傩逐疫，岁终腊祭百神。' },
  han3: { id: 'dongmaeng', name: '东盟', month: 10, grade: 'A', src: ['sanguozhi-dongyi'], text: '以十月祭天，国中大会，名曰东盟。', overrides: { buyeo: { name: '迎鼓', month: 12 }, silla: { name: '十月农功祭', month: 10, grade: 'B' }, baekje: { name: '五月祭', month: 5, grade: 'B' } } },
  wa: { id: 'kinen', name: '祈年祭', month: 2, grade: 'C', src: [], text: '春耕之前，祭祀田神，祈求丰年。' },
  steppe: { id: 'longcheng', name: '茏城大会', month: 5, grade: 'A', src: ['shiji-xiongnu'], text: '五月大会茏城，祭其先、天地、鬼神。', overrides: { xianbei: { name: '饶乐水大会', month: 3, grade: 'A', src: ['houhanshu-xianbei'] }, dongxianbei: { name: '饶乐水大会', month: 3 }, tuoba: { name: '春会', month: 3, grade: 'B' } } },
  oasis: { id: 'xingxiang', name: '行像', month: 4, grade: 'B', src: ['faxian'], text: '四月一日起扫洒道路，庄严巷陌，行像入城。' },
  indic: { id: 'indra', name: '因陀罗节', month: 4, grade: 'B', src: ['silappatikaram'], text: '吉蒂莱月望日，王都举行二十八日的因陀罗大节，祈求雨水与丰收。', overrides: { kushan: { name: '佛诞节', month: 4, grade: 'B' }, kshatrapa: { name: '佛诞节', month: 4, grade: 'B' } } },
  persia: { id: 'nowruz', name: '诺鲁孜', month: 3, grade: 'B', src: ['iranica-mehragan', 'nowruz-arsacid'], text: '春分新年，燃火迎春。' },
  arabia: { id: 'bel', name: '贝勒大祭', month: 4, grade: 'B', src: [], text: '春月尼散，城中举行贝勒神的大祭与游行。' },
  nile: { id: 'inundation', name: '涨水节', month: 8, grade: 'C', src: ['nilometer'], text: '河水上涨之时，举国祭祀，庆祝泛滥带来的沃土。' },
  roman: { id: 'saturnalia', name: '农神节', month: 12, grade: 'A', src: ['saturnalia'], text: '十二月十七日起七日欢宴，互赠小礼，主仆易位。' },
  north: { id: 'samonios', name: '萨莫尼奥斯月', month: 11, grade: 'B', src: ['coligny'], text: '「夏之终」之月，聚会宴饮。', overrides: { marcomanni: { name: '大地母神巡游', month: 4, grade: 'B', src: ['germania-40'] }, goths: { name: '大地母神巡游', month: 4 }, vandals: { name: '大地母神巡游', month: 4 } } },
  nanhai: { id: 'waterfest', name: '水节', month: 4, grade: 'C', src: [], text: '雨季之前，泼水祈雨，舟楫竞渡。' },
};
// 庆典三档：盛大 / 从简 / 免（花费按势力城数；「以后照此办理」可设为自动）
const FESTIVAL_RULE = {
  grand: { cost: { base: 30, perCity: 15, cap: 400 }, order: 6, capitalLoyalty: 3, prestige: 10, miniGame: 'touhu' },
  modest: { costK: 1 / 3, order: 2, prestige: 3 },
  skip: { order: 0, prestige: 0 },
};

// ------------------------------------------------------------------ 成就（功业） --
// cat：dom 内政 · eco 经济 · dip 外交 · cul 文治 · col 收藏 · ppl 人物 · mil 武功 · exp 探索
const ACHIEVEMENTS = [
  // 内政
  { id: 'a_dev999', cat: 'dom', name: '尽地力', cond: 'cityStat999', prestige: 20 },
  { id: 'a_city3', cat: 'dom', name: '郡城气象', cond: 'cityLevel:2', prestige: 10 },
  { id: 'a_city4', cat: 'dom', name: '大郡繁华', cond: 'cityLevel:3', prestige: 25 },
  { id: 'a_city5', cat: 'dom', name: '天下名都', cond: 'cityLevel:4', prestige: 50 },
  { id: 'a_pop1m', cat: 'dom', name: '百万之城', cond: 'cityPop:1000000', prestige: 40 },
  { id: 'a_order80', cat: 'dom', name: '政通人和', cond: 'allOrder:80', prestige: 40, fame: 1 },
  { id: 'a_order95', cat: 'dom', name: '路不拾遗', cond: 'cityOrder:95', prestige: 20 },
  { id: 'a_harvest', cat: 'dom', name: '仓廪实', cond: 'harvestTotal:20000', prestige: 15 },
  { id: 'a_project1', cat: 'dom', name: '鸠工庀材', cond: 'projects:1', prestige: 10 },
  { id: 'a_project10', cat: 'dom', name: '百废俱兴', cond: 'projects:10', prestige: 40 },
  { id: 'a_wonder', cat: 'dom', name: '奇观落成', cond: 'wonder', prestige: 80, fame: 1 },
  { id: 'a_relief', cat: 'dom', name: '救民水火', cond: 'reliefs:5', prestige: 20, virtue: 1 },
  // 经济
  { id: 'a_gold5k', cat: 'eco', name: '府库充盈', cond: 'goldTotal:5000', prestige: 15 },
  { id: 'a_gold20k', cat: 'eco', name: '富可敌国', cond: 'goldTotal:20000', prestige: 40 },
  { id: 'a_caravan1', cat: 'eco', name: '初通商旅', cond: 'caravans:1', prestige: 5 },
  { id: 'a_caravan20', cat: 'eco', name: '商贾云集', cond: 'caravans:20', prestige: 30 },
  { id: 'a_silk', cat: 'eco', name: '丝路驼铃', cond: 'caravanFamilies:4', prestige: 40 },
  { id: 'a_profit', cat: 'eco', name: '陶朱之富', cond: 'tradeProfit:10000', prestige: 50 },
  // 外交
  { id: 'a_ally', cat: 'dip', name: '合纵', cond: 'pacts:3', prestige: 20 },
  { id: 'a_marry', cat: 'dip', name: '秦晋之好', cond: 'marriages:1', prestige: 20 },
  { id: 'a_tribute', cat: 'dip', name: '四夷宾服', cond: 'tributaries:3', prestige: 50 },
  { id: 'a_friends', cat: 'dip', name: '天下归心', cond: 'relAll:30', prestige: 60, fame: 1 },
  { id: 'a_truce', cat: 'dip', name: '化干戈为玉帛', cond: 'truces:3', prestige: 15 },
  { id: 'a_farenvoy', cat: 'dip', name: '万里通使', cond: 'farRel:60', prestige: 30 },
  // 文治
  { id: 'a_wenhui1', cat: 'cul', name: '文采风流', cond: 'wenhuiWin:1', prestige: 30 },
  { id: 'a_wenhui3', cat: 'cul', name: '文坛盟主', cond: 'wenhuiWin:3', prestige: 60, fame: 1 },
  { id: 'a_festival5', cat: 'cul', name: '与民同乐', cond: 'grandFestivals:5', prestige: 20 },
  { id: 'a_sage5', cat: 'cul', name: '礼贤下士', cond: 'sages:5', prestige: 20 },
  { id: 'a_yuedan', cat: 'cul', name: '月旦品题', cond: 'event:yuedan', prestige: 15 },
  { id: 'a_shuijing', cat: 'cul', name: '伏龙凤雏', cond: 'event:shuijing', prestige: 25 },
  // 收藏
  { id: 'a_items10', cat: 'col', name: '奇珍满库', cond: 'itemsOwned:10', prestige: 10 },
  { id: 'a_rare', cat: 'col', name: '稀世之珍', cond: 'rareOwned:1', prestige: 15 },
  { id: 'a_epic', cat: 'col', name: '神品', cond: 'epicOwned:1', prestige: 40 },
  { id: 'a_codex', cat: 'col', name: '博物', cond: 'codexPct:0.5', prestige: 40 },
  { id: 'a_seal', cat: 'col', name: '受命于天', cond: 'item:chuanguoxi', prestige: 80, fame: 2 },
  // 人物
  { id: 'a_sworn', cat: 'ppl', name: '桃园之义', cond: 'sworn:1', prestige: 15 },
  { id: 'a_mentor', cat: 'ppl', name: '薪火相传', cond: 'mentors:2', prestige: 15 },
  { id: 'a_gens30', cat: 'ppl', name: '人才济济', cond: 'gens:30', prestige: 30 },
  { id: 'a_lv10', cat: 'ppl', name: '百战名将', cond: 'level:10', prestige: 25 },
  { id: 'a_t5', cat: 'ppl', name: '位极人臣', cond: 'titleTier:5', prestige: 30, needs: 'titles' },
  { id: 'a_hidden10', cat: 'ppl', name: '野无遗贤', cond: 'hiddenFound:10', prestige: 30 },
  // 武功（也有，但非必需）
  { id: 'a_tourney1', cat: 'mil', name: '天下第一', cond: 'tourneyWin:1', prestige: 30 },
  { id: 'a_defend5', cat: 'mil', name: '金城汤池', cond: 'defenses:5', prestige: 20 },
  { id: 'a_cities10', cat: 'mil', name: '据有十城', cond: 'cities:10', prestige: 30 },
  // 探索
  { id: 'a_exp1', cat: 'exp', name: '跋山涉水', cond: 'expeditions:1', prestige: 5 },
  { id: 'a_exp10', cat: 'exp', name: '足迹天下', cond: 'sitesVisited:10', prestige: 40 },
  { id: 'a_myth', cat: 'exp', name: '寻仙访道', cond: 'mythSites:3', prestige: 30 },
  { id: 'a_map', cat: 'exp', name: '按图索骥', cond: 'clueFound:1', prestige: 20 },
];

// ------------------------------------------------------------------ 初始羁绊（经典；世界剧本在此基础上追加） --
// kind：sworn 结义 · kin 亲族 · spouse 夫妻 · friend 知己 · rival 宿敌 · feud 不和 · mentor 师徒（a 为师）
const BONDS = [
  ['刘备', '关羽', 'sworn', 100, 'sanguo-guanyu'], ['刘备', '张飞', 'sworn', 100, 'sanguo-guanyu'], ['关羽', '张飞', 'sworn', 100, 'sanguo-guanyu'],
  ['关羽', '关平', 'kin', 80], ['孙坚', '孙策', 'kin', 90], ['马腾', '马超', 'kin', 85], ['马超', '马岱', 'kin', 75],
  ['曹操', '夏侯惇', 'kin', 85], ['曹操', '夏侯渊', 'kin', 80], ['曹操', '曹仁', 'kin', 80], ['曹操', '曹洪', 'kin', 75], ['夏侯惇', '夏侯渊', 'kin', 80],
  ['孟获', '祝融', 'spouse', 90], ['孟获', '孟优', 'kin', 80], ['刘焉', '刘璋', 'kin', 80], ['张鲁', '张卫', 'kin', 80],
  ['糜竺', '糜芳', 'kin', 70], ['蒯良', '蒯越', 'kin', 75], ['公孙瓒', '公孙越', 'kin', 80], ['荀彧', '荀攸', 'kin', 75], ['陆康', '陆绩', 'kin', 75],
  ['袁绍', '袁术', 'kin', -40], ['孙策', '周瑜', 'friend', 90, 'zhouyu-zhuan'], ['诸葛亮', '徐庶', 'friend', 70], ['诸葛亮', '庞统', 'friend', 60],
  ['关羽', '张辽', 'friend', 55], ['刘备', '公孙瓒', 'friend', 60], ['曹操', '袁绍', 'friend', 30], ['颜良', '文丑', 'friend', 70],
  ['张郃', '高览', 'friend', 60], ['典韦', '许褚', 'friend', 40], ['黄盖', '程普', 'friend', 60], ['程普', '周瑜', 'feud', -40, 'jiangbiaozhuan-chengpu'],
  ['甘宁', '凌统', 'feud', -70], ['关羽', '黄忠', 'rival', -10], ['张飞', '吕布', 'rival', -40], ['马超', '张飞', 'rival', 0], ['马超', '许褚', 'rival', 0],
  ['孙策', '太史慈', 'rival', 20], ['吕布', '董卓', 'feud', -20], ['张辽', '高顺', 'friend', 50],
  ['卑弥呼', '卑弥弓呼', 'feud', -60],
];

// 初始邦交（势力键对；未列出者 0）
const RELATIONS = [
  ['dong', '*zhongyuan', -40, '反董卓联盟'], ['yuanshao', 'yuanshu', -30, '兄弟不睦'], ['yuanshao', 'cao', 20, '少时相善'],
  ['liubei', 'gongsun', 40, '同学于卢植'], ['sunjian', 'yuanshu', 30, '袁术表孙坚'], ['sunjian', 'liubiao', -30, '荆襄之争'],
  ['gongsun', 'yuanshao', -40, '冀州之争'], ['liubiao', 'yuanshao', 20, '南北相结'], ['taoqian', 'cao', -10, ''], ['liuyan', 'zhanglu', 20, '督义司马'],
  ['mateng', 'dong', 10, '凉州旧谊'], ['kongrong', 'liubei', 20, ''], ['hanfu', 'yuanshao', -20, '冀州之忧'],
  ['roma', 'parthia', -40, '161–166 年罗马安息战争'], ['roma', 'marcomanni', -30, '马科曼尼战争（180 年议和）'], ['roma', 'iazyges', -20, ''],
  ['roma', 'caledonia', -40, '安敦宁长城以北'], ['parthia', 'kushan', -10, ''], ['yamatai', 'kuna', -50, '与狗奴国素不和'],
  ['goguryeo', 'gongsundu', -30, ''], ['xianbei', '*zhongyuan', -15, '檀石槐以来屡寇边'], ['wuhuan', 'yuanshao', 20, '袁绍抚纳乌桓'],
  ['xiongnu', 'yuanshao', -10, ''], ['baekje', 'silla', -20, ''], ['satavahana', 'kshatrapa', -40, '西总督与百乘的世仇'],
  ['chola', 'pandya', -20, ''], ['chera', 'chola', -10, ''], ['persis', 'parthia', -10, ''], ['osroene', 'roma', 30, '罗马属国'], ['hatra', 'parthia', 20, '安息附庸'],
];

module.exports = { FAMILY, RIVER, EVENTS: E, HISTORICAL, SAGES, OMENS, PROPOSALS, MISSIONS, MISSION_REWARD, PROJECTS, SITES, SITE_TYPES, FESTIVALS, FESTIVAL_RULE, ACHIEVEMENTS, BONDS, RELATIONS };
