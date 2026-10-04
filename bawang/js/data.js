'use strict';
/* ==========================================================================
   霸王的大陆 · 数据
   武将、敌军、策略、道具、装备、地点与剧情。
   ========================================================================== */

// ---------------------------------------------------------------- 我军武将 --
// str 武力  int 智力  agi 速度  sol 基础兵力
const GENERALS = {
  liubei:   { name: '刘备', zi: '玄德', glyph: '備', str: 75, int: 76, agi: 72, sol: 220, color: '#3aa85c', weapon: 'shuanggu', armor: null,
              look: { body: '#2f8f4e', trim: '#f2c14e', hat: 'crown', cape: '#b8323a' } },
  guanyu:   { name: '关羽', zi: '云长', glyph: '羽', str: 97, int: 75, agi: 82, sol: 260, color: '#2b8a6e', weapon: 'qinglong', armor: null,
              look: { body: '#1f7a5a', trim: '#e9d27a', hat: 'hood', cape: '#14523c', beard: true } },
  zhangfei: { name: '张飞', zi: '翼德', glyph: '飛', str: 98, int: 35, agi: 80, sol: 250, color: '#4b4f9c', weapon: 'shemao', armor: null,
              look: { body: '#3b3f86', trim: '#d9d9e8', hat: 'helmet', cape: '#22244f', beard: true } },
  jianyong: { name: '简雍', zi: '宪和', glyph: '雍', str: 38, int: 78, agi: 62, sol: 150, color: '#7a64c8', weapon: null, armor: null,
              look: { body: '#6a55b5', trim: '#e3dcf7', hat: 'scholar' } },
  sunqian:  { name: '孙乾', zi: '公祐', glyph: '乾', str: 33, int: 81, agi: 60, sol: 150, color: '#3d8fd1', weapon: null, armor: null,
              look: { body: '#2f78b8', trim: '#e6f1fb', hat: 'scholar' } },
  mizhu:    { name: '糜竺', zi: '子仲', glyph: '竺', str: 36, int: 73, agi: 58, sol: 170, color: '#c48a2c', weapon: null, armor: null,
              look: { body: '#a87425', trim: '#f6e3b4', hat: 'scholar' } },
  zhoucang: { name: '周仓', zi: '元福', glyph: '倉', str: 84, int: 38, agi: 72, sol: 230, color: '#8c5a32', weapon: null, armor: null,
              look: { body: '#6e4626', trim: '#e2c08d', hat: 'scarf', beard: true } },
  liaohua:  { name: '廖化', zi: '元俭', glyph: '化', str: 74, int: 62, agi: 70, sol: 210, color: '#b0523a', weapon: null, armor: null,
              look: { body: '#93412c', trim: '#f0c9a9', hat: 'helmet' } },
  zhaoyun:  { name: '赵云', zi: '子龙', glyph: '雲', str: 96, int: 76, agi: 94, sol: 270, color: '#dfe6ef', weapon: 'yajiao', armor: null,
              look: { body: '#d7dee8', trim: '#5a8fd6', hat: 'helmet', cape: '#3c6fb8' } },
};

// ---------------------------------------------------------------- 策略 --
// lv 习得等级（全军等级）  int 军师智力要求  tp 消耗策略值
const TACTICS = {
  jiaore:  { name: '焦热',   lv: 1,  int: 0,  tp: 2,  type: 'fire',    target: 'enemy',  pow: 60,  desc: '火攻敌方一队' },
  buji:    { name: '补给',   lv: 3,  int: 0,  tp: 4,  type: 'heal',    target: 'ally',   pow: 120, desc: '补充我军一员兵力' },
  diebao:  { name: '谍报',   lv: 4,  int: 0,  tp: 1,  type: 'scout',   target: 'enemy',  pow: 0,   desc: '探查敌将兵力与能力' },
  hunluan: { name: '混乱',   lv: 6,  int: 60, tp: 4,  type: 'confuse', target: 'enemy',  pow: 0,   desc: '扰乱敌将，使其敌我不分' },
  huoji:   { name: '火计',   lv: 8,  int: 55, tp: 5,  type: 'fire',    target: 'enemy',  pow: 150, desc: '猛火攻击敌方一队' },
  dunjia:  { name: '遁甲',   lv: 10, int: 0,  tp: 3,  type: 'escape',  target: 'none',   pow: 0,   desc: '奇门遁甲，全军必定撤退' },
  shuiji:  { name: '水计',   lv: 12, int: 65, tp: 7,  type: 'water',   target: 'enemy',  pow: 240, desc: '引水淹没敌方一队' },
  liehuo:  { name: '烈火',   lv: 15, int: 70, tp: 9,  type: 'fire',    target: 'enemies',pow: 130, desc: '烈焰席卷敌军全体' },
  dabuji:  { name: '大补给', lv: 18, int: 72, tp: 16, type: 'heal',    target: 'allies', pow: 110, desc: '补充我军全体兵力' },
  luoshi:  { name: '落石',   lv: 21, int: 75, tp: 9,  type: 'rock',    target: 'enemy',  pow: 380, desc: '巨石砸向敌方一队' },
  dashui:  { name: '大水',   lv: 24, int: 80, tp: 14, type: 'water',   target: 'enemies',pow: 260, desc: '洪水吞没敌军全体' },
  yanlong: { name: '炎龙',   lv: 28, int: 86, tp: 18, type: 'fire',    target: 'enemies',pow: 420, desc: '炎龙之火焚尽敌军' },
};
const TACTIC_ORDER = Object.keys(TACTICS);

// ---------------------------------------------------------------- 道具 --
const ITEMS = {
  shangyao: { name: '伤药',   price: 20,  type: 'heal',   pow: 250,  target: 'ally',   desc: '恢复一员兵力 250' },
  liangyao: { name: '良药',   price: 80,  type: 'heal',   pow: 1000, target: 'ally',   desc: '恢复一员兵力 1000' },
  xiandan:  { name: '仙丹',   price: 400, type: 'heal',   pow: 99999,target: 'allies', desc: '全军兵力完全恢复' },
  huanhun:  { name: '还魂丹', price: 250, type: 'revive', pow: 0.3,  target: 'dead',   desc: '使阵亡武将复活' },
  bingshu:  { name: '兵书',   price: 60,  type: 'tp',     pow: 20,   target: 'none',   desc: '恢复策略值 20' },
  huoyao:   { name: '火药',   price: 100, type: 'bomb',   pow: 160,  target: 'enemies',desc: '炸裂敌军全体（战斗中）', battleOnly: true },
};

const WEAPONS = {
  tiejian:    { name: '铁剑',     atk: 8,  price: 90 },
  gangdao:    { name: '钢刀',     atk: 16, price: 260 },
  changqiang: { name: '长枪',     atk: 24, price: 620 },
  dadao:      { name: '大刀',     atk: 34, price: 1300 },
  yinqiang:   { name: '银枪',     atk: 44, price: 2400 },
  qixingdao:  { name: '七星宝刀', atk: 56, price: 4200 },
  shuanggu:   { name: '双股剑',   atk: 12, price: 0, unique: true },
  qinglong:   { name: '青龙偃月刀', atk: 30, price: 0, unique: true },
  shemao:     { name: '丈八蛇矛', atk: 28, price: 0, unique: true },
  yajiao:     { name: '涯角枪',   atk: 36, price: 0, unique: true },
};
const ARMORS = {
  pijia:     { name: '皮甲',   def: 6,  price: 70 },
  suozi:     { name: '锁子甲', def: 14, price: 320 },
  tiejia:    { name: '铁甲',   def: 24, price: 850 },
  mingguang: { name: '明光铠', def: 36, price: 1900 },
  yulin:     { name: '鱼鳞甲', def: 48, price: 3600 },
};

// ---------------------------------------------------------------- 敌军 --
const ENEMIES = {
  // 黄巾军
  hj_bing:    { name: '黄巾兵',   glyph: '兵', lv: 1,  str: 38, int: 15, agi: 40, def: 0,  sol: [50, 80],   exp: 5,   gold: 4,   color: '#d9a21b' },
  hj_lishi:   { name: '黄巾力士', glyph: '力', lv: 2,  str: 52, int: 15, agi: 42, def: 3,  sol: [80, 120],  exp: 9,   gold: 7,   color: '#d9a21b' },
  hj_shushi:  { name: '黄巾术士', glyph: '術', lv: 2,  str: 30, int: 58, agi: 48, def: 2,  sol: [60, 90],   exp: 10,  gold: 8,   color: '#c98a1a', tactics: ['jiaore'] },
  hj_toumu:   { name: '黄巾头目', glyph: '目', lv: 5,  str: 62, int: 35, agi: 55, def: 6,  sol: [200, 260], exp: 24,  gold: 18,  color: '#d9a21b' },
  hj_qibing:  { name: '黄巾骑兵', glyph: '騎', lv: 5,  str: 58, int: 20, agi: 70, def: 5,  sol: [160, 220], exp: 20,  gold: 15,  color: '#d9a21b' },
  hj_yaoshi:  { name: '黄巾妖师', glyph: '妖', lv: 9,  str: 35, int: 70, agi: 58, def: 6,  sol: [220, 300], exp: 38,  gold: 30,  color: '#b46a12', tactics: ['jiaore', 'huoji', 'hunluan'] },
  hj_jingbing:{ name: '黄巾精兵', glyph: '精', lv: 10, str: 66, int: 25, agi: 60, def: 10, sol: [300, 380], exp: 44,  gold: 32,  color: '#d9a21b' },
  peiyuanshao:{ name: '裴元绍',   glyph: '裴', lv: 6,  str: 70, int: 28, agi: 62, def: 8,  sol: [380, 440], exp: 60,  gold: 50,  color: '#e0b030', named: true },
  guanhai:    { name: '管亥',     glyph: '亥', lv: 7,  str: 80, int: 30, agi: 64, def: 9,  sol: [450, 520], exp: 75,  gold: 60,  color: '#e0b030', named: true },
  e_zhoucang: { name: '周仓',     glyph: '倉', lv: 7,  str: 78, int: 35, agi: 66, def: 8,  sol: [420, 480], exp: 70,  gold: 40,  color: '#8c5a32', named: true, recruit: 'zhoucang' },
  bocai:      { name: '波才',     glyph: '波', lv: 10, str: 74, int: 55, agi: 64, def: 12, sol: [700, 800], exp: 120, gold: 90,  color: '#e0b030', named: true },
  heyi:       { name: '何仪',     glyph: '儀', lv: 11, str: 72, int: 40, agi: 66, def: 12, sol: [750, 850], exp: 130, gold: 95,  color: '#e0b030', named: true },
  huangshao:  { name: '黄劭',     glyph: '劭', lv: 11, str: 76, int: 36, agi: 62, def: 13, sol: [780, 880], exp: 135, gold: 100, color: '#e0b030', named: true },
  zhangmancheng:{ name: '张曼成', glyph: '曼', lv: 12, str: 70, int: 68, agi: 64, def: 13, sol: [800, 900], exp: 150, gold: 110, color: '#e0b030', named: true, tactics: ['huoji'] },
  e_liaohua:  { name: '廖化',     glyph: '化', lv: 11, str: 72, int: 60, agi: 68, def: 12, sol: [700, 780], exp: 120, gold: 70,  color: '#b0523a', named: true, recruit: 'liaohua' },
  // 黄巾首领
  dengmao:      { name: '邓茂',   glyph: '茂', lv: 3,  str: 64, int: 30, agi: 52, def: 5,  sol: 360,  exp: 60,   gold: 60,   color: '#f0c040', boss: true },
  chengyuanzhi: { name: '程远志', glyph: '志', lv: 4,  str: 70, int: 35, agi: 55, def: 6,  sol: 560,  exp: 100,  gold: 120,  color: '#f0c040', boss: true },
  zhangbao:     { name: '张宝',   glyph: '寶', lv: 9,  str: 58, int: 82, agi: 62, def: 10, sol: 2200, exp: 420,  gold: 350,  color: '#f5c542', boss: true, tactics: ['jiaore', 'huoji', 'hunluan'] },
  zhangliang:   { name: '张梁',   glyph: '梁', lv: 12, str: 82, int: 55, agi: 66, def: 14, sol: 3200, exp: 640,  gold: 500,  color: '#f5c542', boss: true, tactics: ['huoji'] },
  zhangjiao:    { name: '张角',   glyph: '角', lv: 14, str: 60, int: 94, agi: 70, def: 15, sol: 4600, exp: 1100, gold: 800,  color: '#ffd24a', boss: true, tactics: ['huoji', 'liehuo', 'buji', 'hunluan'] },
  // 西凉军
  xl_bing:    { name: '西凉兵',   glyph: '涼', lv: 13, str: 70, int: 25, agi: 60, def: 14, sol: [420, 520],   exp: 70,  gold: 40,  color: '#7d2f3a' },
  xl_qibing:  { name: '西凉铁骑', glyph: '騎', lv: 14, str: 76, int: 25, agi: 78, def: 15, sol: [480, 560],   exp: 82,  gold: 48,  color: '#7d2f3a' },
  xl_gong:    { name: '西凉弓手', glyph: '弓', lv: 14, str: 72, int: 40, agi: 64, def: 12, sol: [400, 500],   exp: 76,  gold: 45,  color: '#7d2f3a' },
  xl_jingrui: { name: '西凉精锐', glyph: '銳', lv: 17, str: 82, int: 30, agi: 70, def: 20, sol: [640, 760],   exp: 115, gold: 65,  color: '#7d2f3a' },
  xl_huwei:   { name: '西凉虎卫', glyph: '虎', lv: 20, str: 88, int: 35, agi: 74, def: 24, sol: [800, 950],   exp: 150, gold: 85,  color: '#7d2f3a' },
  xl_shushi:  { name: '西凉谋士', glyph: '謀', lv: 19, str: 45, int: 80, agi: 66, def: 16, sol: [600, 700],   exp: 140, gold: 80,  color: '#6a2433', tactics: ['huoji', 'shuiji', 'hunluan'] },
  huzhen:     { name: '胡轸',     glyph: '軫', lv: 15, str: 78, int: 40, agi: 66, def: 16, sol: [1000, 1100], exp: 220, gold: 130, color: '#a23b4a', named: true },
  zhaocen:    { name: '赵岑',     glyph: '岑', lv: 15, str: 74, int: 50, agi: 64, def: 16, sol: [950, 1050],  exp: 210, gold: 120, color: '#a23b4a', named: true },
  lijue:      { name: '李傕',     glyph: '傕', lv: 18, str: 84, int: 55, agi: 70, def: 20, sol: [1300, 1450], exp: 330, gold: 180, color: '#a23b4a', named: true },
  guosi:      { name: '郭汜',     glyph: '汜', lv: 18, str: 86, int: 38, agi: 72, def: 20, sol: [1300, 1450], exp: 330, gold: 180, color: '#a23b4a', named: true },
  xurong:     { name: '徐荣',     glyph: '榮', lv: 18, str: 82, int: 70, agi: 70, def: 20, sol: [1300, 1450], exp: 350, gold: 190, color: '#a23b4a', named: true, tactics: ['huoji'] },
  fanchou:    { name: '樊稠',     glyph: '稠', lv: 21, str: 88, int: 38, agi: 74, def: 24, sol: [1700, 1850], exp: 450, gold: 240, color: '#a23b4a', named: true },
  zhangji:    { name: '张济',     glyph: '濟', lv: 21, str: 84, int: 60, agi: 70, def: 24, sol: [1650, 1800], exp: 440, gold: 240, color: '#a23b4a', named: true },
  lisu:       { name: '李肃',     glyph: '肅', lv: 21, str: 70, int: 80, agi: 72, def: 22, sol: [1500, 1650], exp: 440, gold: 240, color: '#a23b4a', named: true, tactics: ['shuiji', 'hunluan'] },
  // 董卓军首领
  huaxiong:   { name: '华雄',     glyph: '雄', lv: 17, str: 94, int: 45, agi: 78, def: 22, sol: 6500,  exp: 1600, gold: 900,  color: '#c43c4c', boss: true },
  lvbu:       { name: '吕布',     glyph: '布', lv: 22, str: 100,int: 40, agi: 96, def: 30, sol: 11000, exp: 3000, gold: 1500, color: '#d63a3a', boss: true, double: true },
  liru:       { name: '李儒',     glyph: '儒', lv: 21, str: 40, int: 92, agi: 70, def: 22, sol: 4000,  exp: 900,  gold: 600,  color: '#9b3346', boss: true, tactics: ['shuiji', 'liehuo', 'hunluan', 'buji'] },
  dongzhuo:   { name: '董卓',     glyph: '卓', lv: 22, str: 82, int: 70, agi: 55, def: 28, sol: 9000,  exp: 3400, gold: 2000, color: '#e04848', boss: true, tactics: ['liehuo'] },
};

// 各地遭遇的敌军编成（权重, 编成）
const ENCOUNTERS = {
  A:  [[4, ['hj_bing']], [4, ['hj_bing', 'hj_bing']], [3, ['hj_bing', 'hj_bing', 'hj_bing']], [3, ['hj_lishi']],
       [3, ['hj_lishi', 'hj_bing']], [2, ['hj_shushi', 'hj_bing']], [1, ['hj_shushi', 'hj_lishi', 'hj_bing']]],
  B:  [[3, ['hj_lishi', 'hj_lishi']], [3, ['hj_toumu', 'hj_bing', 'hj_bing']], [3, ['hj_shushi', 'hj_shushi', 'hj_lishi']],
       [3, ['hj_qibing', 'hj_qibing']], [2, ['hj_toumu', 'hj_qibing', 'hj_shushi']], [1, ['peiyuanshao', 'hj_lishi', 'hj_lishi']],
       [1, ['guanhai', 'hj_toumu']], [1, ['e_zhoucang', 'hj_lishi']]],
  C:  [[3, ['hj_jingbing', 'hj_jingbing', 'hj_qibing']], [3, ['hj_yaoshi', 'hj_jingbing']], [2, ['hj_jingbing', 'hj_jingbing', 'hj_jingbing']],
       [1, ['bocai', 'hj_jingbing', 'hj_jingbing']], [1, ['heyi', 'hj_qibing', 'hj_yaoshi']], [1, ['huangshao', 'hj_jingbing', 'hj_jingbing']],
       [1, ['zhangmancheng', 'hj_yaoshi', 'hj_qibing']], [1, ['e_liaohua', 'hj_jingbing']]],
  D1: [[3, ['xl_bing', 'xl_bing', 'xl_bing']], [3, ['xl_qibing', 'xl_bing']], [3, ['xl_gong', 'xl_gong', 'xl_bing']],
       [1, ['huzhen', 'xl_bing', 'xl_bing']], [1, ['zhaocen', 'xl_qibing', 'xl_gong']]],
  D2: [[3, ['xl_qibing', 'xl_qibing', 'xl_gong']], [3, ['xl_jingrui', 'xl_jingrui']], [2, ['xl_jingrui', 'xl_bing', 'xl_gong']],
       [1, ['lijue', 'xl_qibing', 'xl_qibing']], [1, ['guosi', 'xl_jingrui', 'xl_gong']], [1, ['xurong', 'xl_jingrui', 'xl_qibing']]],
  D3: [[3, ['xl_jingrui', 'xl_jingrui', 'xl_jingrui']], [3, ['xl_huwei', 'xl_jingrui']], [2, ['xl_shushi', 'xl_huwei']],
       [1, ['fanchou', 'xl_huwei', 'xl_gong']], [1, ['zhangji', 'xl_huwei', 'xl_jingrui']], [1, ['lisu', 'xl_huwei', 'xl_shushi']]],
};

// ---------------------------------------------------------------- 首领战 --
const BOSSES = {
  b_daxing: {
    enemies: ['dengmao', 'chengyuanzhi', 'hj_lishi', 'hj_shushi'], flag: 'f_daxing', bg: 'fort',
    pre: [['chengyuanzhi', '哪里来的织席贩履之徒，也敢闯我大兴山寨！'], ['zhangfei', '燕人张翼德在此！贼将速来受死！'], ['dengmao', '弟兄们，杀！']],
    post: [['liubei', '程远志、邓茂已除，涿郡百姓可以安居了。'], ['guanyu', '兄长，黄巾余党退往青州方向，青州关应已可通行。']],
  },
  b_zhangbao: {
    enemies: ['zhangbao', 'hj_yaoshi', 'hj_toumu', 'hj_toumu'], flag: 'f_zhangbao', bg: 'fort',
    pre: [['zhangbao', '吾乃地公将军张宝！天兵天将，助我破敌！'], ['liubei', '妖术惑众，祸乱天下，今日便是尔等末日！']],
    post: [['zhangbao', '大哥……救我……'], ['liubei', '张宝已败，听闻张梁屯兵于广宗关以南。'], ['guanyu', '广宗关守将已降，我军可南下进兵。']],
  },
  b_zhangliang: {
    enemies: ['zhangliang', 'hj_jingbing', 'hj_jingbing', 'hj_yaoshi'], flag: 'f_zhangliang', bg: 'fort',
    pre: [['zhangliang', '人公将军张梁在此！我兄弟三人替天行道，你等何苦与我为敌？'], ['zhangfei', '少说废话，看矛！']],
    post: [['liubei', '张梁已除，只剩广宗城中的天公将军张角了！']],
  },
  b_zhangjiao: {
    enemies: ['zhangjiao', 'hj_yaoshi', 'hj_yaoshi', 'hj_jingbing', 'hj_jingbing'], flag: 'f_zhangjiao', bg: 'palace',
    pre: [['zhangjiao', '苍天已死，黄天当立！岁在甲子，天下大吉！'], ['liubei', '张角！你以妖言惑众，致使生灵涂炭，今日便替天下除害！'],
          ['zhangjiao', '狂妄！看我呼风唤雨，烈火焚身！']],
    post: [['zhangjiao', '黄天……黄天……'], ['liubei', '黄巾之乱，终于平定了。'],
           [null, '——数月之后——\n朝廷腐败依旧。西凉刺史董卓趁乱进京，废少帝、立献帝，残暴不仁，天下震怒。'],
           [null, '各路诸侯会盟于酸枣，共推袁绍为盟主，起兵讨伐董卓。'],
           ['guanyu', '兄长，酸枣关已开，各路诸侯正在西方集结！'], ['liubei', '好！我等即刻西进，共讨国贼！']],
  },
  b_huaxiong: {
    enemies: ['huaxiong', 'xl_jingrui', 'xl_jingrui', 'xl_gong'], flag: 'f_huaxiong', bg: 'gate',
    pre: [[null, '汜水关前，董卓大将华雄连斩联军数将，诸侯失色。'], ['huaxiong', '关东鼠辈，还有谁敢来送死？'],
          ['guanyu', '酒且斟下，某去便来！']],
    post: [['guanyu', '（掷华雄首级于地）其酒尚温。'], ['liubei', '汜水关已破！前方便是虎牢关了。']],
  },
  b_lvbu: {
    enemies: ['lvbu', 'xl_huwei', 'xl_huwei'], flag: 'f_lvbu', bg: 'gate',
    pre: [[null, '虎牢关前，一将头戴紫金冠，身披百花袍，手持方天画戟，坐下赤兔马——正是吕布吕奉先。'],
          ['lvbu', '人中吕布，马中赤兔！尔等一齐上吧！'], ['zhangfei', '三姓家奴休走！燕人张飞在此！'], ['liubei', '二弟三弟，我等兄弟同心，共战吕布！']],
    post: [['lvbu', '……今日暂且罢了！'], [null, '吕布败退回洛阳，虎牢关大开。'], ['liubei', '董卓就在洛阳，直捣其巢穴！']],
  },
  b_dongzhuo: {
    enemies: ['dongzhuo', 'liru', 'xl_huwei', 'xl_huwei'], flag: 'f_dongzhuo', bg: 'palace',
    pre: [['dongzhuo', '区区织席小儿，也敢犯我天威？'], ['liru', '主公勿忧，有我李儒在，定叫他们有来无回。'],
          ['liubei', '董卓！你欺君罔上，祸国殃民，人人得而诛之！']],
    post: [['dongzhuo', '咱家……竟败于……此等人物之手……'], ['liubei', '国贼已除！'],
           [null, '董卓伏诛，天下震动。然而群雄割据之势已成，一个崭新的乱世，才刚刚开始……']],
  },
};

// ---------------------------------------------------------------- 地点 --
// fac: inn 宿屋  weapon 武器店  armor 防具店  item 道具店  food 粮店  tavern 酒馆  gov 官府
const LOCATIONS = [
  // A · 幽州
  { id: 'lousang',  name: '楼桑村',   kind: 'village', x: 40, y: 10, zone: 'A', fac: ['inn', 'item', 'food', 'tavern'],
    shop: { item: ['shangyao', 'bingshu'] } },
  { id: 'zhuojun',  name: '涿郡',     kind: 'town',    x: 56, y: 13, zone: 'A', fac: ['inn', 'weapon', 'armor', 'item', 'food', 'tavern', 'gov'],
    shop: { weapon: ['tiejian', 'gangdao'], armor: ['pijia', 'suozi'], item: ['shangyao', 'bingshu', 'huanhun'] } },
  { id: 'daxing',   name: '大兴山寨', kind: 'fort',    x: 82, y: 17, zone: 'A', boss: 'b_daxing', theme: 'cave', enc: 'A', size: [9, 7] },
  { id: 'g_qingzhou', name: '青州关', kind: 'gate',    x: 60, y: 26, need: 'f_daxing',
    block: [['守关士兵', '黄巾贼程远志盘踞大兴山寨，关外贼兵横行，未破贼寨之前不得出关！']] },
  // B · 青州
  { id: 'pingyuan', name: '平原',     kind: 'village', x: 52, y: 38, zone: 'B', fac: ['inn', 'item', 'food', 'tavern'],
    shop: { item: ['shangyao', 'liangyao', 'bingshu'] } },
  { id: 'qingzhou', name: '青州',     kind: 'town',    x: 79, y: 34, zone: 'B', fac: ['inn', 'weapon', 'armor', 'item', 'food', 'tavern', 'gov'],
    shop: { weapon: ['gangdao', 'changqiang'], armor: ['suozi', 'tiejia'], item: ['shangyao', 'liangyao', 'bingshu', 'huanhun', 'huoyao'] } },
  { id: 'quyang',   name: '下曲阳',   kind: 'fort',    x: 97, y: 45, zone: 'B', boss: 'b_zhangbao', theme: 'fort', enc: 'B', size: [11, 8] },
  { id: 'g_guangzong', name: '广宗关', kind: 'gate',   x: 70, y: 52, need: 'f_zhangbao',
    block: [['守关黄巾兵', '地公将军张宝坐镇下曲阳，有他在，此关休想通过！']] },
  // C · 冀州
  { id: 'julu',     name: '巨鹿',     kind: 'village', x: 76, y: 60, zone: 'C', fac: ['inn', 'weapon', 'item', 'food', 'tavern'],
    shop: { weapon: ['changqiang', 'dadao'], item: ['liangyao', 'bingshu', 'huanhun', 'huoyao'] } },
  { id: 'zlying',   name: '张梁营',   kind: 'fort',    x: 56, y: 68, zone: 'C', boss: 'b_zhangliang', theme: 'fort', enc: 'C', size: [11, 9] },
  { id: 'guangzong',name: '广宗',     kind: 'fort',    x: 95, y: 72, zone: 'C', boss: 'b_zhangjiao', theme: 'palace', enc: 'C', size: [12, 9],
    need: 'f_zhangliang', block: [['城门守卫', '人公将军张梁屯兵西南营寨，城门紧闭，外人不得入内！']] },
  { id: 'g_suanzao', name: '酸枣关', kind: 'gate',     x: 38, y: 70, need: 'f_zhangjiao',
    block: [['守关官兵', '黄巾未平，西去之路暂且封闭。']] },
  // D · 司隶
  { id: 'chenliu',  name: '陈留',     kind: 'town',    x: 27, y: 72, zone: 'D1', fac: ['inn', 'weapon', 'armor', 'item', 'food', 'tavern', 'gov'],
    shop: { weapon: ['dadao', 'yinqiang'], armor: ['tiejia', 'mingguang'], item: ['liangyao', 'xiandan', 'bingshu', 'huanhun', 'huoyao'] } },
  { id: 'suanzao',  name: '酸枣大营', kind: 'village', x: 10, y: 75, zone: 'D1', fac: ['inn', 'item', 'food', 'tavern'],
    shop: { item: ['liangyao', 'bingshu', 'huanhun'] } },
  { id: 'g_sishui', name: '汜水关',   kind: 'bossgate', x: 18, y: 56, boss: 'b_huaxiong' },
  { id: 'chenggao', name: '成皋',     kind: 'village', x: 8,  y: 49, zone: 'D2', fac: ['inn', 'weapon', 'armor', 'item', 'food', 'tavern'],
    shop: { weapon: ['yinqiang', 'qixingdao'], armor: ['mingguang', 'yulin'], item: ['liangyao', 'xiandan', 'bingshu', 'huanhun', 'huoyao'] } },
  { id: 'g_hulao',  name: '虎牢关',   kind: 'bossgate', x: 18, y: 42, boss: 'b_lvbu' },
  { id: 'beimang',  name: '北邙',     kind: 'village', x: 31, y: 33, zone: 'D3', fac: ['inn', 'item', 'food', 'tavern'],
    shop: { item: ['liangyao', 'xiandan', 'bingshu', 'huanhun', 'huoyao'] } },
  { id: 'luoyang',  name: '洛阳',     kind: 'fort',    x: 15, y: 31, zone: 'D3', boss: 'b_dongzhuo', theme: 'palace', enc: 'D3', size: [12, 9] },
];
const LOC = Object.fromEntries(LOCATIONS.map(l => [l.id, l]));

// 道路连通（建图时会沿这些路线铺设官道）
const ROADS = [
  ['lousang', 'zhuojun'], ['zhuojun', 'daxing'], ['zhuojun', 'g_qingzhou'],
  ['g_qingzhou', 'pingyuan'], ['pingyuan', 'qingzhou'], ['qingzhou', 'quyang'], ['qingzhou', 'g_guangzong'],
  ['g_guangzong', 'julu'], ['julu', 'guangzong'], ['julu', 'zlying'], ['zlying', 'g_suanzao'],
  ['g_suanzao', 'chenliu'], ['chenliu', 'suanzao'], ['chenliu', 'g_sishui'],
  ['g_sishui', 'chenggao'], ['chenggao', 'g_hulao'], ['g_hulao', 'luoyang'], ['luoyang', 'beimang'],
];

// ---------------------------------------------------------------- 招募 --
// 在酒馆中招募的武将
const TAVERN_RECRUIT = {
  zhuojun: { id: 'jianyong', lines: [['jianyong', '在下简雍，与玄德公是同乡旧识。听闻诸位起兵讨贼，愿随军出谋划策！']] },
  qingzhou:{ id: 'sunqian', lines: [['sunqian', '在下北海孙乾，久闻刘玄德仁德之名，愿效犬马之劳。']] },
  pingyuan:{ id: 'mizhu', lines: [['mizhu', '在下东海糜竺，家中略有资财。愿献上军资，追随明公！']], gift: 500 },
  suanzao: { id: 'zhaoyun', need: 'f_zhangjiao', lines: [['zhaoyun', '在下常山赵子龙。公孙瓒非明主，久仰玄德公仁义，愿以此枪相随！']] },
};

// ---------------------------------------------------------------- 人物对白 --
const NPC_LINES = {
  zhuojun: [
    '黄巾贼头裹黄巾，四处劫掠，听说他们的头领叫程远志。',
    '大兴山寨就在涿郡东边的山里，贼人都从那里出来。',
    '刘焉太守正在招募义勇，你们可去官府看看。',
    '武器店新到了钢刀，有了好兵器，打仗才不吃亏。',
    '在城外行军要消耗粮食，粮食吃完了士兵会逃走的！',
    '听说酒馆里有个叫简雍的人，整天念叨着要投军。',
  ],
  lousang: [
    '这里是楼桑村，刘玄德就是在这里长大的。',
    '村东那棵大桑树，远远望去像车盖一样。',
    '受伤了就到宿屋休息，兵力会全部恢复。',
    '战斗中选择「全军突击」，全军会自动进攻，直到你按下按键为止。',
  ],
  pingyuan: [
    '下曲阳在东南方，地公将军张宝会使妖术！',
    '对付会用策略的敌人，要尽快打倒他们。',
    '听说黄巾军里有个叫周仓的猛将，力大无穷。',
    '糜家的公子在酒馆喝酒，他家可是东海首富。',
  ],
  qingzhou: [
    '青州被黄巾围了好久，多亏了义军相救！',
    '军师的智力越高，可以使用的策略越多。',
    '「混乱」可以让敌将敌我不分，自相残杀。',
    '道具店有火药卖，可以炸伤敌军全体。',
  ],
  julu: [
    '这里是张角兄弟的老家巨鹿。',
    '张梁的营寨在西南边，张角则坐镇东南的广宗城。',
    '广宗城门紧闭，听说要先打败张梁才行。',
    '黄巾军中的廖化，据说本是良家子弟。',
  ],
  chenliu: [
    '曹孟德在陈留散家财招募义兵，响应讨董。',
    '汜水关守将华雄勇猛无比，已经连斩数员大将了。',
    '董卓的西凉兵骁勇善战，千万不要轻敌。',
    '陈留的防具店有明光铠，可以大大减少损伤。',
  ],
  suanzao: [
    '各路诸侯都在这里扎营，营中天天议论纷纷。',
    '袁绍被推为盟主，可诸侯各怀心思啊。',
    '听说有个白袍小将，枪法如神，在营中闲住。',
  ],
  chenggao: [
    '虎牢关的吕布，人中吕布，马中赤兔，天下无敌！',
    '和吕布交战，务必准备好良药和仙丹。',
    '成皋的兵器铺有七星宝刀，是难得的好刀。',
  ],
  beimang: [
    '董卓要迁都长安，洛阳的百姓苦不堪言。',
    '洛阳宫中有李儒辅佐董卓，此人诡计多端。',
    '北邙山下多古墓，晚上可别乱走。',
  ],
};

const GOV_LINES = {
  zhuojun: { first: [['刘焉', '你就是刘玄德？好！汉室宗亲，果然一表人才。'], ['刘焉', '黄巾贼将程远志、邓茂盘踞东面的大兴山寨，率众五万来犯涿郡。'],
                     ['刘焉', '这些军资你们拿去，务必破贼！'], [null, '获得了 300 金和伤药×3！']],
             gift: { gold: 300, items: { shangyao: 3 } } },
  qingzhou:{ first: [['龚景', '多谢诸位解青州之围！黄巾首领张宝屯兵东南的下曲阳，还请诸位一并讨平！'], [null, '获得了 600 金！']], gift: { gold: 600 } },
  chenliu: { first: [['曹操', '玄德公来得正好。华雄据守汜水关，联军屡战不利，还望诸公出力！'], [null, '获得了 1500 金和良药×3！']],
             gift: { gold: 1500, items: { liangyao: 3 } } },
};

// 开场
const INTRO = [
  '东汉末年，朝政腐败，宦官专权，天灾连年，民不聊生。',
  '巨鹿人张角，自称「大贤良师」，聚众数十万，头裹黄巾，揭竿而起，史称「黄巾之乱」。',
  '涿郡楼桑村人刘备，字玄德，乃中山靖王之后。与关羽、张飞于桃园结义，誓同生死。',
  '「上报国家，下安黎庶」——三兄弟招募乡勇，踏上了平定乱世的征程。',
];

// 当前目标提示
function objectiveText(f) {
  if (!f.f_gov_zhuojun) return '前往涿郡官府拜见太守刘焉';
  if (!f.f_daxing) return '讨伐东面大兴山寨的程远志';
  if (!f.f_zhangbao) return '出青州关，讨伐下曲阳的张宝';
  if (!f.f_zhangliang) return '南下广宗关，击破张梁营';
  if (!f.f_zhangjiao) return '攻入广宗城，讨伐张角';
  if (!f.f_huaxiong) return '西出酸枣关，攻破汜水关的华雄';
  if (!f.f_lvbu) return '北上虎牢关，迎战吕布';
  if (!f.f_dongzhuo) return '攻入洛阳，讨伐国贼董卓';
  return '天下已定——第一部 完';
}
