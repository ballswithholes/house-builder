'use strict';
/* ==========================================================================
   君主乐章 · SG.MusicSuites（AUDIO-V4 甲部；第四版要求 7：不同的君主，不同的音乐）
   每位武将（任何人都可能继承势力）一套乐章 = 主题动机（2 小节 4/4 简谱）+ 调色（文化家族、主奏 / 应答乐器、
   速度系数、移调）。buildSuite() 把它变成真正的 SG.Music 曲目：
     · 包装家族现有的 地图 / 议事 / 战斗 / 守城 曲（music-han.js、music-world.js）：
       Mi（4 小节引子）· Mr（每轮循环开头重述）· Mv（D 段：上二度模进、原形、倒影、扩大）· Mt（循环尾的 2 小节）·
       Mc（战斗循环开头的号令）· MB（汉地战斗 B 段前再奏一次号令）· Md（守城：扩大的号令）；
       主奏替换（模板里最忙的连奏声部改用君主的主奏乐器）、移调、改速。
     · 由动机直接生成短曲：victory · defeat · fanfare · clash · clash-b · clash-short · clash-crit；
       ending 的引子由一统者的动机在合唱上奏出。
   动机按目标曲的调式重新拼写（respell），所以同一动机在五声地图曲、Bhairav 战斗曲与 12/8 草原奔马曲里都保持轮廓与节奏。
   42 套手写（HAND，经典 18 君主 + 4 位继承人 + 20 位世界君主），其余由 generate() 按规则生成并冻结在
   js/music-suites-gen.js（tools/build-suites.js 写入；从不手改）；冻结表里没有的武将（模组）在运行时按同一规则、
   排在冻结表之后生成。全部不取 SG.Random（装饰性种子：fnv1a(name + '|' + attempt) + mulberry32）。

   接口（AUDIO-V4 §A11）
     SG.MusicSuites.suiteOf(gen | name)   → { family, motif, lead, answer, tempo, tr, temper, house?, generated? } | null
     SG.MusicSuites.ensure(name)          → 是否已登记 'suite:<name>:<kind>'（12 种，约 0.6 ms；幂等）；同时最多 4 套（玩家、敌方、
                                             最近的外交对象、单挑的一对），更早的注销（SG.Music.remove）
     SG.MusicSuites.resolve(kind, { suite, foe }) → key | null   kind：map council battle battle-defend victory defeat fanfare
                                             clash clash-b clash-short clash-crit ending
     SG.MusicSuites.headCell(name)        → 动机的头三拍（记谱字符串）
     SG.MusicSuites.duel(a, b)            → 'suite-duel:<a>|<b>'（龙争虎斗 + Ma / Mb 两段决斗动机；上一对注销）
     SG.MusicSuites.pin(name)             玩家的乐章不被注销
     纯函数（tests/suites.js、tools/build-suites.js 用）：HAND FAMILY_OF TEMPLATE_CULTURE HUA FAMILY_INST OCT parseMotif respellNote
       seqDeg invDeg scaleInfo render generate signature distance buildSuite temperOf motifShare head familyOf
   依赖：SG.Music 与 music-inst / music-han / music-world 的曲目（ensure / resolve 时才读；载入顺序见 INTEGRATION §11）。
   ========================================================================== */
(function () {
  const SG = (typeof window !== 'undefined' ? window : globalThis).SG = (typeof window !== 'undefined' ? window : globalThis).SG || {};
  const MS = SG.MusicSuites = SG.MusicSuites || {};

  // ------------------------------------------------------------ culture → family / templates --
  // 12 个文化家族（CITY-VIEW §3）：hua han3 wa steppe oasis indic persia arabia nile roman north nanhai
  const FAMILY_OF = {
    han: 'hua', nanman: 'hua', korea: 'han3', wa: 'wa', steppe: 'steppe', sarmatian: 'steppe', tarim: 'oasis',
    kushan: 'indic', india: 'indic', persia: 'persia', arab: 'arabia', moor: 'arabia', africa: 'nile',
    roman: 'roman', celt: 'north', german: 'north', seasia: 'nanhai', yi: 'nanhai',
  };
  // 家族 → 作为编曲模板的现有地域变奏（SG.Music.resolve 的文化）
  const TEMPLATE_CULTURE = {
    hua: 'han', han3: 'korea', wa: 'wa', steppe: 'steppe', oasis: 'kushan', indic: 'kushan', persia: 'persia',
    arabia: 'arab', nile: 'arab', roman: 'roman', north: 'celt', nanhai: 'seasia',
  };
  // 每个乐器角色的汉地乐器；FAMILY_INST（DOMESTIC-FX §8.3，与 music-stings.js 同表）按家族替换
  const HUA = {
    flute: 'dizi', flute2: 'xiao', bow: 'erhu', pluck: 'zheng', pluck2: 'yangqin', pluck3: 'ruan', pluck4: 'qin',
    reed: 'suona', brass: 'horns', brass1: 'horn', bell: 'bianzhong', pad: 'sheng', strings: 'strings', choir: 'choir',
    bass: 'lowstr', timp: 'timpani', drum: 'dagu', drum2: 'tanggu', gong: 'luo', tamtam: 'tamtam', cymbal: 'bo', wood: 'muyu',
  };
  const FAMILY_INST = {
    hua: {},
    han3: { flute: 'daegeum', flute2: 'daegeum', bow: 'haegeum', pluck: 'gayageum', pluck2: 'gayageum', pluck4: 'gayageum', reed: 'piri', drum: 'buk', drum2: 'janggu', gong: 'jing', gong2: 'kkwaeng', cymbal: 'kkwaeng' },
    wa: { flute: 'shinobue', flute2: 'shakuhachi', bow: 'shakuhachi', pluck: 'koto', pluck2: 'koto', pluck3: 'koto', pluck4: 'koto', reed: 'shinobue', drum: 'taiko', drum2: 'taiko' },
    steppe: { flute: 'whistle', flute2: 'overtone', bow: 'morin', pluck: 'dombra', pluck2: 'dombra', pluck3: 'dombra', pluck4: 'dombra', pad: 'kargyraa', drum: 'shamandrum', drum2: 'shamandrum', wood: 'hoof', wood2: 'hoof' },
    oasis: { flute: 'ney', flute2: 'ney', bow: 'kamancheh', pluck2: 'santur', pluck4: 'oud', pad: 'drones', drum: 'daf', drum2: 'darbuka', wood: 'darbuka' },
    indic: { flute: 'bansuri', flute2: 'bansuri', bow: 'bansuri', pluck: 'sitar', pluck2: 'sitar', pluck3: 'sitar', pluck4: 'sitar', pad: 'drones', drum: 'bayan', drum2: 'dayan', wood: 'dayan', bell: 'gender' },
    persia: { flute: 'ney', flute2: 'ney', bow: 'kamancheh', pluck: 'santur', pluck2: 'santur', pluck3: 'oud', pluck4: 'oud', reed: 'zurna', pad: 'drones', drum: 'daf', drum2: 'daf', wood: 'darbuka' },
    arabia: { flute: 'ney', flute2: 'ney', bow: 'kamancheh', pluck: 'oud', pluck2: 'qanun', pluck3: 'oud', pluck4: 'oud', reed: 'zurna', pad: 'drones', drum: 'darbuka', drum2: 'daf', wood: 'darbuka', cymbal: 'crotala' },
    nile: { flute: 'ney', flute2: 'ney', bow: 'ney', pluck: 'harp', pluck2: 'harp', pluck3: 'harp', pluck4: 'harp', reed: 'aulos', pad: 'drones', drum: 'frame', drum2: 'frame', cymbal: 'crotala', gong2: 'crotala' },
    roman: { flute: 'aulos', flute2: 'aulos', bow: 'aulos', pluck: 'harp', pluck2: 'harp', pluck3: 'harp', pluck4: 'harp', reed: 'aulos', brass: 'cornu', brass1: 'cornu', drum: 'wardrum', drum2: 'frame', cymbal: 'crotala', gong2: 'crotala' },
    north: { flute: 'whistle', flute2: 'whistle', bow: 'whistle', pluck: 'harp', pluck2: 'harp', pluck3: 'harp', pluck4: 'harp', reed: 'chanter', brass1: 'cornu', pad: 'drones', drum: 'bodhran', drum2: 'bodhran', wood: 'frame' },
    nanhai: { flute: 'suling', flute2: 'suling', bow: 'suling', pluck: 'saron', pluck2: 'gender', pluck3: 'saron', pluck4: 'gender', bell: 'bonang', drum: 'kendang', drum2: 'kendang', gong: 'kempul', gong2: 'kenong', cymbal: 'kenong', tamtam: 'gongageng' },
  };
  function inst(fam, role) {
    const T = (SG.MusicStings && SG.MusicStings.FAMILY_INST) || FAMILY_INST;
    return (T[fam] || FAMILY_INST[fam] || {})[role] || HUA[role];
  }
  // 有音高的打击乐在鼓谱轨上需要 note（否则引擎没有音高 → NaN；Chromium 渲染时发现，§A7.3）
  const PITCHED_KIT = { kenong: 1, kempul: 1, gongageng: 1, dayan: 1, saron: 1, gender: 1, bonang: 1, bianzhong: 1, timpani: 1 };
  function kitEntry(i, tonic) { return PITCHED_KIT[i] ? { inst: i, note: tonic } : { inst: i }; }
  // 加入的声部用该乐器在全部基础曲目里最常用的八度（冻结：与 music-han / music-world 的曲目一同核对，tests/suites.js）
  const OCT = { horn: -1, erhu: 0, dizi: 1, xiao: 0, zheng: -1, bianzhong: 0, strings: -1, horns: -1, choir: -1, lowstr: -2, timpani: -2, pipa: 0, ruan: -1, pizz: -2, suona: 0, choirOo: -1, stab: -1, qin: -2, sheng: -1, shakuhachi: 0, koto: 0, shinobue: 0, daegeum: 0, haegeum: 0, gayageum: 0, piri: 0, morin: -1, overtone: 1, kargyraa: -2, dombra: -1, saron: 0, bonang: 1, gender: 0, kenong: 0, kempul: -1, gongageng: -2, suling: 0, sitar: 0, bansuri: 0, tanpura: -1, ney: 0, kamancheh: 0, santur: 0, zurna: 1, cornu: -1, oud: -1, qanun: 0, aulos: 0, harp: 0, whistle: 1, cello: -1, drones: -1, chanter: 0 };

  // ------------------------------------------------------------ the 42 hand-composed suites (AUDIO-V4 §A5) --
  // motif：2 小节 4/4 简谱（1 = 模板调的“1”），最后一音为终止音；lead / answer：乐器角色；tempo：模板速度系数；tr：半音
  const HAND = {
    // ---- 中原（经典剧本 18 君主）----
    '董卓':   { family: 'hua', motif: '6,:2 3> #4_ 3_ | 1 7,_ 6,_ 6,:2 |', lead: 'brass', answer: 'bow', tempo: 0.94, tr: -2, temper: 'tyrant', note: '低音铜管拖着增四度，暴君的阴影' },
    '袁绍':   { family: 'hua', motif: '5,. 1_ 3 5 | 6. 5_ 3 1 |', lead: 'brass1', answer: 'bell', tempo: 0.98, tr: 1, temper: 'noble', note: '四世三公：附点的仪仗，钟声应答' },
    '曹操':   { family: 'hua', motif: '6,_ 3_ 6 5. 3_ | 2 3 6,:2 |', lead: 'reed', answer: 'bow', tempo: 1.04, tr: 0, temper: 'hero', note: '羽调式，上冲八度后回落，雄心与诡谲' },
    '刘备':   { family: 'hua', motif: '3 5 6. 1\'_ | 6 5 3:2 |', lead: 'bow', answer: 'flute', tempo: 0.97, tr: 0, temper: 'benevolent', note: '宫调式，二胡的温厚上行，落在角音' },
    '孙坚':   { family: 'hua', motif: '5,_ 5,_ 1 5. 6_ | 1\' 6 5:2 |', lead: 'brass1', answer: 'reed', tempo: 1.05, tr: -1, temper: 'tiger', note: '孙氏家传动机 5, 1 5：四度 + 五度的猛跃' },
    '袁术':   { family: 'hua', motif: '1\'. 6_ 5 3^ | 2 1 2:2 |', lead: 'reed', answer: 'pluck2', tempo: 1.0, tr: 2, temper: 'vain', note: '华丽下行，倚音虚饰，停在商音不解决' },
    '公孙瓒': { family: 'hua', motif: '5_ 5__ 5__ 6_ 5__ 5__ 1\' 6 | 5. 3_ 5:2 |', lead: 'brass1', answer: 'flute', tempo: 1.06, tr: 0, temper: 'cavalry', note: '白马义从：长短短的马蹄节奏' },
    '刘表':   { family: 'hua', motif: '2. 3_ 5 6 | 5 3 2:2 |', lead: 'pluck4', answer: 'flute2', tempo: 0.95, tr: 0, temper: 'scholar', note: '商调式，古琴与箫的平稳级进' },
    '刘焉':   { family: 'hua', motif: '3 5_ 6_ 3\' 2\'_ 1\'_ | 6:2 3:2 |', lead: 'flute2', answer: 'bell', tempo: 0.96, tr: 1, temper: 'mystic', note: '角调式，蜀中的神秘上扬' },
    '马腾':   { family: 'hua', motif: '6, 3_ 3_ 6 3\'_ 2\'_ | 1\' 6 6:2 |', lead: 'bow', answer: 'reed', tempo: 1.04, tr: -2, temper: 'frontier', note: '西凉：羽调式的马步与大跳' },
    '陶谦':   { family: 'hua', motif: '5 3 2. 1_ | 2 3 1:2 |', lead: 'pad', answer: 'flute', tempo: 0.93, tr: 0, temper: 'elder', note: '老成持重，笙的下行' },
    '孔融':   { family: 'hua', motif: '5 6^ 1\' 6_ 5_ | 3 5 5:2 |', lead: 'pluck4', answer: 'flute2', tempo: 0.96, tr: 2, temper: 'scholar', note: '建安文士：琴的倚音与回环' },
    '韩馥':   { family: 'hua', motif: '2 0_ 3_ 2 1 | 6, 1 2:2 |', lead: 'flute2', answer: 'pluck', tempo: 0.97, tr: -1, temper: 'timid', note: '迟疑的休止，商音悬而未决' },
    '张鲁':   { family: 'hua', motif: '3:2 6 5 | 3 2 3:2 |', lead: 'bell', answer: 'flute2', tempo: 0.94, tr: 0, temper: 'priest', note: '五斗米道：编钟的仪式音型' },
    '张杨':   { family: 'hua', motif: '5 5_ 6_ 5 1\' | 6 5 5:2 |', lead: 'reed', answer: 'pluck3', tempo: 1.0, tr: 1, temper: 'plain', note: '朴实的徵调号子' },
    '刘繇':   { family: 'hua', motif: '1 3 2 1_ 6,_ | 5, 6, 1:2 |', lead: 'flute', answer: 'pluck', tempo: 0.98, tr: -1, temper: 'official', note: '扬州：宫调式的低回' },
    '陆康':   { family: 'hua', motif: '6, 1 3. 2_ | 1 6, 6,:2 |', lead: 'bow', answer: 'pluck4', tempo: 0.95, tr: 2, temper: 'loyal', note: '庐江死守：庄重的羽调' },
    '孟获':   { family: 'hua', motif: '6,_ 6,_ 6,_ 1_ 3 3 | 2_ 1_ 6,_ 5,_ 6,:2 |', lead: 'reed', answer: 'bow', tempo: 1.06, tr: -2, temper: 'wild', note: '南中：铜鼓般的重复音' },
    // ---- 可能的继承者（继承家传动机的头三个音）----
    '孙策':   { family: 'hua', motif: '5,_ 5,_ 1 5 1\' | 2\'. 1\'_ 6 5 |', lead: 'reed', answer: 'brass1', tempo: 1.08, tr: -1, temper: 'tiger', house: '孙坚', note: '小霸王：家传头三音，更高更急' },
    '刘璋':   { family: 'hua', motif: '3 5_ 6_ 5 3 | 2 1 6,:2 |', lead: 'flute2', answer: 'pluck', tempo: 0.93, tr: 1, temper: 'timid', house: '刘焉', note: '刘焉的动机向下塌落' },
    '马超':   { family: 'hua', motif: '6, 3_ 3_ 6 6\' | 5\'. 3\'_ 6:2 |', lead: 'reed', answer: 'bow', tempo: 1.08, tr: -2, temper: 'cavalry', house: '马腾', note: '锦马超：家传马步冲上高八度' },
    '公孙康': { family: 'hua', motif: '2 5 2\' 6 | 5 3 2:2 |', lead: 'brass1', answer: 'flute', tempo: 1.0, tr: 0, temper: 'plain', house: '公孙度', note: '承袭辽东的商调' },
    // ---- 世界剧本 ----
    '公孙度': { family: 'hua', motif: '2 5 2\'. 1\'_ | 6 5 2:2 |', lead: 'brass1', answer: 'bow', tempo: 1.0, tr: 0, temper: 'harsh', note: '辽东：北地的商调，四度上冲' },
    '士燮':   { family: 'hua', motif: '3_ 5_ 6 1\' 2\'_ 1\'_ | 6 5:3 |', lead: 'flute', answer: 'pluck', tempo: 0.95, tr: 2, temper: 'scholar', note: '交趾四十年太平：笛子的长句' },
    '卑弥呼': { family: 'wa', motif: '3:2 4 6 | 7^ 6 4 3 |', lead: 'flute', answer: 'pluck', tempo: 0.92, tr: 0, temper: 'shaman', note: '都节音阶，篠笛的鬼道咒语' },
    '卑弥弓呼': { family: 'wa', motif: '6, 7, 3. 4_ | 3 7, 6,:2 |', lead: 'flute2', answer: 'pluck', tempo: 1.03, tr: -1, temper: 'rival', note: '狗奴：低沉的尺八与太鼓' },
    '故国川王': { family: 'han3', motif: '6,:2 2 1 | 3. 2_ 1 6, |', lead: 'flute', answer: 'bow', tempo: 0.98, tr: 0, temper: 'wise', note: '大笒长音，界面调' },
    '首露王': { family: 'han3', motif: '5 3 5 6_ 5_ | 3 2 1:2 |', lead: 'bow', answer: 'pluck', tempo: 0.97, tr: 1, temper: 'founder', note: '卵生的始祖：奚琴与伽倻琴' },
    '魁头':   { family: 'steppe', motif: '1 5 1\':2 | 6 5 3 5 |', lead: 'bow', answer: 'flute2', tempo: 1.02, tr: 0, temper: 'khan', note: '长调的五度、八度大跳' },
    '於夫罗': { family: 'steppe', motif: '6, 6,_ 3_ 6 5 | 3 2_ 1_ 6,:2 |', lead: 'bow', answer: 'pluck', tempo: 1.04, tr: -2, temper: 'exile', note: '南匈奴单于：马头琴的流亡之歌' },
    '胡维色迦': { family: 'indic', motif: '1 5 4 3 | 2 3 1:2 |', lead: 'pluck', answer: 'flute', tempo: 0.98, tr: 0, temper: 'emperor', note: '贵霜：西塔尔的庄严回旋' },
    '耶若室利': { family: 'indic', motif: '1 3 5. 6_ | 5 4 3:2 |', lead: 'flute', answer: 'pluck', tempo: 1.0, tr: 2, temper: 'king', note: '百乘：班苏里的上行' },
    '迦梨迦罗': { family: 'indic', motif: '1\' 7 6 5_ 6_ | 5 3 5:2 |', lead: 'reed', answer: 'pluck', tempo: 1.04, tr: -1, temper: 'hero', note: '朱罗：筑大堤的英雄' },
    '沃洛吉斯': { family: 'persia', motif: '1 3 2. 1_ | 7, 2 1:2 |', lead: 'flute', answer: 'bow', tempo: 0.98, tr: 0, temper: 'kingofkings', note: '安息：奈伊的 Shur 色彩' },
    '戈契赫尔': { family: 'persia', motif: '1 5 6. 5_ | 4 3 2 1 |', lead: 'bow', answer: 'pluck', tempo: 1.0, tr: 1, temper: 'harsh', note: '波西斯：萨珊前夜' },
    '阿布加尔': { family: 'arabia', motif: '1 4. 3_ 2_ 3_ | 2 1 7,:2 |', lead: 'pluck', answer: 'flute', tempo: 0.97, tr: 0, temper: 'pious', note: '埃德萨：乌德的木卡姆' },
    '加达拉特': { family: 'nile', motif: '1 5 4 5 | 3:2 1:2 |', lead: 'reed', answer: 'pluck', tempo: 1.0, tr: 0, temper: 'king', note: '阿克苏姆：竖琴与阿夫洛斯管' },
    '康茂德': { family: 'roman', motif: '1 1_ 5,_ 1 3 | 5. 4_ 3 1 |', lead: 'brass1', answer: 'flute', tempo: 1.02, tr: 0, temper: 'gladiator', note: '罗马的赫拉克勒斯：号角的竞技场' },
    '阿根托科克斯': { family: 'north', motif: '5, 1 1_ 2_ 1 | 5, 6,_ 7,_ 1:2 |', lead: 'reed', answer: 'pluck', tempo: 1.0, tr: 0, temper: 'chieftain', note: '喀里多尼亚：风笛' },
    '巴洛马尔': { family: 'north', motif: '1 1 5. 4_ | 3 2 1:2 |', lead: 'brass1', answer: 'flute', tempo: 1.02, tr: -1, temper: 'warlord', note: '马科曼尼：卢尔号' },
    '菲利默': { family: 'north', motif: '1 3 5 5 | 6 5 3:2 |', lead: 'reed', answer: 'pluck', tempo: 1.0, tr: 1, temper: 'migrant', note: '哥特：迁徙之歌' },
    '区连':   { family: 'nanhai', motif: '3 5 1\' 5 | 3 2 3:2 |', lead: 'flute', answer: 'pluck', tempo: 1.02, tr: 0, temper: 'rebel', note: '林邑：192 年起兵的苏林笛' },
  };

  // ------------------------------------------------------------ motif parsing / respelling (§A4) --
  const DEG_SEMI = [0, 0, 2, 4, 5, 7, 9, 11];
  const SPELL = ['1', 'b2', '2', 'b3', '3', '4', '#4', '5', 'b6', '6', 'b7', '7'];
  function parseMotif(s) {
    const out = [];
    for (const tk of String(s).trim().split(/\s+/)) {
      if (tk === '|') { out.push({ bar: true }); continue; }
      const m = /^([#b]?)([0-7])([',]*)([_.t]*)(?::([0-9.]+))?([~*^v\/\\><)@&]*)$/.exec(tk);
      if (!m) throw new Error('bad motif token ' + tk);
      let dur = m[5] ? parseFloat(m[5]) : 1;
      for (const c of m[4]) dur *= c === '_' ? 0.5 : c === '.' ? 1.5 : 2 / 3;
      let oct = 0; for (const c of m[3]) oct += c === "'" ? 1 : -1;
      out.push({ rest: m[2] === '0', acc: m[1], deg: +m[2], oct, dur, orn: m[6] || '' });
    }
    return out;
  }
  function scaleInfo(scale) {
    const byDeg = {}, pcs = [];
    for (const t of String(scale).trim().split(/\s+/)) {
      const m = /^([#b]?)([1-7])/.exec(t); if (!m) continue;
      const st = DEG_SEMI[+m[2]] + (m[1] === '#' ? 1 : m[1] === 'b' ? -1 : 0);
      const pc = ((st % 12) + 12) % 12;
      if (byDeg[+m[2]] === undefined) byDeg[+m[2]] = pc;
      if (!pcs.includes(pc)) pcs.push(pc);
    }
    return { byDeg, pcs: pcs.sort((a, b) => a - b) };
  }
  // 保留级数，变音记号取自模板调式；调式里没有的级数移到最近的音阶音（动机里明写的变音如董卓的 #4 保留）
  function respellNote(n, sc) {
    if (n.rest || n.bar) return n;
    if (n.acc) return n;
    const pc = sc.byDeg[n.deg];
    if (pc === undefined) {
      const base = DEG_SEMI[n.deg];
      let best = null;
      for (const p of sc.pcs) for (const q of [p - 12, p, p + 12]) if (best === null || Math.abs(q - base) < Math.abs(best - base) || (Math.abs(q - base) === Math.abs(best - base) && q < best)) best = q;
      const abs = 12 * n.oct + best;
      const o = Math.floor(abs / 12), s = SPELL[((abs % 12) + 12) % 12];
      const mm = /^([#b]?)([1-7])$/.exec(s);
      return Object.assign({}, n, { acc: mm[1], deg: +mm[2], oct: o });
    }
    const d = ((pc - DEG_SEMI[n.deg] + 18) % 12) - 6;
    return Object.assign({}, n, { acc: d === 1 ? '#' : d === -1 ? 'b' : '' });
  }
  const noteHead = n => n.rest ? '0' : n.acc + n.deg + (n.oct > 0 ? "'".repeat(n.oct) : ','.repeat(-n.oct));
  function scaleSteps(sc) { const steps = []; for (let o = -3; o <= 3; o++) for (const pc of sc.pcs) steps.push(12 * o + pc); return steps; }
  const absOfNote = n => 12 * n.oct + DEG_SEMI[n.deg] + (n.acc === '#' ? 1 : n.acc === 'b' ? -1 : 0);
  function nearestIdx(steps, a) { let i = 0, best = Infinity; steps.forEach((v, j) => { if (Math.abs(v - a) < best) { best = Math.abs(v - a); i = j; } }); return i; }
  function spellNote(n, t) {
    const o = Math.floor(t / 12), sp = SPELL[((t % 12) + 12) % 12];
    const mm = /^([#b]?)([1-7])$/.exec(sp);
    return Object.assign({}, n, { acc: mm[1], deg: +mm[2], oct: o });
  }
  // D 段（§A7.1）：沿模板音阶把一个已重拼的音移动 k 级（模进）
  function seqDeg(n, k, sc) {
    if (n.rest || n.bar) return n;
    const steps = scaleSteps(sc);
    const i = nearestIdx(steps, absOfNote(n));
    return spellNote(n, steps[Math.max(0, Math.min(steps.length - 1, i + k))]);
  }
  // 以第一个音为轴、按音阶级数倒影（上行一级变成下行一级）
  function invDeg(ns, sc) {
    const steps = scaleSteps(sc);
    const first = ns.find(n => !n.rest && !n.bar);
    if (!first) return ns;
    const i0 = nearestIdx(steps, absOfNote(first));
    return ns.map(n => (n.rest || n.bar) ? n : spellNote(n, steps[Math.max(0, Math.min(steps.length - 1, 2 * i0 - nearestIdx(steps, absOfNote(n))))]));
  }
  function sectionBars(sec) { let m = 0; for (const v of Object.values(sec)) if (typeof v === 'string') m = Math.max(m, (v.match(/\|/g) || []).length); return m; }
  // 循环里出自动机的小节比例（Mi Mr Mt Mv Mc MB Md）；§A12 目标 ≥ 0.25
  function motifShare(P) {
    const i0 = Math.max(0, P.form.indexOf(P.loopFrom));
    let all = 0, mot = 0;
    for (const name of P.form.slice(i0)) { const b = sectionBars(P.sections[name] || {}); all += b; if (/^M[itrvcBd]$/.test(name)) mot += b; }
    return all ? mot / all : 0;
  }
  // 动机（4/4，8 拍）→ 模板拍子的记谱；复拍子把一拍映射为附点一拍，附点对与平均八分变成长短（2 : 1）
  function render(notes, meter, opts) {
    opts = opts || {};
    const scale = opts.aug || 1;
    let f = meter % 3 === 0 && meter >= 6 ? meter / 4 : 1;
    const ev = notes.filter(n => !n.bar).map(n => Object.assign({}, n, { dur: n.dur * scale }));
    let d = ev.map(n => n.dur * f);
    if (f !== 1) {
      for (let i = 0; i + 1 < ev.length; i++) {
        const a = ev[i].dur, b = ev[i + 1].dur;
        if ((a === 1.5 * scale && b === 0.5 * scale) || (a === 0.5 * scale && b === 0.5 * scale)) {
          const tot = (a + b) * f; d[i] = tot * 2 / 3; d[i + 1] = tot / 3; i++;
        } else if (a === 0.75 * scale && b === 0.25 * scale) { const tot = (a + b) * f; d[i] = tot * 2 / 3; d[i + 1] = tot / 3; i++; }
      }
    }
    if (meter === 3) {          // 3/4（凯尔特地图曲）：8 拍 → 3 小节共 9 拍，最后一音延长
      f = 1; d = ev.map(n => n.dur); const sum = d.reduce((x, y) => x + y, 0); d[d.length - 1] += (3 - (Math.round(sum) % 3)) % 3;
    }
    const bar = meter === 3 ? 3 : (f !== 1 ? meter : 4);
    const toks = [];
    let pos = 0;
    ev.forEach((n, i) => {
      let left = d[i];
      let first = true;
      while (left > 1e-9) {
        const room = bar - (pos % bar);
        const take = Math.min(left, room);
        const tail = left - take > 1e-9;
        let t = noteHead(n) + ':' + (+take.toFixed(4));
        if (first && n.orn && !n.rest) t += n.orn.replace(/\)/g, '');
        if (tail && !n.rest) t += ')';
        toks.push(t);
        pos += take; left -= take; first = false;
        if (Math.abs(pos % bar) < 1e-9) toks.push('|');
      }
    });
    if (toks[toks.length - 1] !== '|') toks.push('|');
    return { str: toks.join(' '), beats: pos, bars: Math.round(pos / bar) };
  }
  function rests(bars, meter) { return Array(bars).fill('0:' + meter).join(' | ') + ' |'; }
  function spellAbs(abs) { const o = Math.floor(abs / 12); return SPELL[((abs % 12) + 12) % 12] + (o > 0 ? "'".repeat(o) : ','.repeat(-o)); }
  function droneBars(bars, meter, tonicAbs, fifth) {
    const t = spellAbs(tonicAbs), v = spellAbs(tonicAbs + (fifth === false ? 12 : 7));
    return Array(bars).fill(`${t}+${v}:${meter}`).join(' | ') + ' |';
  }
  function tonicAbsOf(P) {
    const t = P.tonic || String(P.scale || '1').trim().split(/\s+/)[0];
    const m = /^([#b]?)([1-7])([',]*)/.exec(t);
    let o = 0; for (const c of m[3]) o += c === "'" ? 1 : -1;
    return 12 * o + DEG_SEMI[+m[2]] + (m[1] === '#' ? 1 : m[1] === 'b' ? -1 : 0);
  }
  function drumBar(meter, kind) {
    const step = meter >= 6 ? 0.5 : 0.25, n = Math.round(meter / step);
    const a = Array(n).fill('.');
    const beat = meter >= 6 ? 3 : 1;
    for (let b = 0; b < meter; b += beat) {
      const i = Math.round(b / step);
      a[i] = b === 0 ? 'X' : (kind === 'martial' ? 'x' : 'o');
      if (kind === 'martial' && i + Math.round(beat / step / 2) < n) a[i + Math.round(beat / step / 2)] = 'o';
    }
    return { s: a.join(''), step };
  }
  // 头三拍（fanfare / clash / headCell）
  function head(ns) {
    const h = []; let t = 0;
    for (const n of ns) { if (n.bar) continue; if (t >= 3) break; h.push(Object.assign({}, n, { dur: Math.min(n.dur, 3 - t) })); t += Math.min(n.dur, 3 - t); }
    return h;
  }

  // ------------------------------------------------------------ the generator (§A6) --
  function fnv(s) { let h = 2166136261 >>> 0; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h >>> 0; }
  function rng(seed) { let a = seed | 0; return () => { a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
  function temperOf(g) {
    const m = Math.max(g.war, g.intel, g.pol);
    if (g.war === m && g.war >= g.intel + 6) return 'martial';
    if (g.intel === m && g.intel >= g.pol) return 'scholar';
    if (g.pol === m) return 'statesman';
    return 'martial';
  }
  const RHY = {
    martial: { a: [[1.5, 0.5, 1, 1], [0.5, 0.5, 1, 1.5, 0.5], [1, 0.5, 0.5, 1, 1], [0.75, 0.25, 1, 2], [1, 1, 0.5, 0.5, 1]],
               b: [[1, 1, 2], [1.5, 0.5, 2], [0.5, 0.5, 1, 2], [1, 3]] },
    scholar: { a: [[2, 1, 1], [1, 1, 1, 0.5, 0.5], [1.5, 0.5, 1, 1], [1, 0.5, 0.5, 2]],
               b: [[1, 1, 2], [2, 2], [1, 3], [0.5, 0.5, 1, 2]] },
    statesman: { a: [[1, 1, 1, 1], [2, 1, 1], [1, 1, 2], [1.5, 0.5, 1, 1]],
                 b: [[1, 1, 2], [2, 2], [1, 1, 1, 1], [1, 3]] },
  };
  function degList(scale) {
    return String(scale).trim().split(/\s+/).map(t => /^([#b]?)([1-7])([',]*)$/.exec(t)).filter(Boolean)
      .map(m => { let o = 0; for (const c of m[3]) o += c === "'" ? 1 : -1; return { acc: m[1], deg: +m[2], oct: o, st: 12 * o + DEG_SEMI[+m[2]] + (m[1] === '#' ? 1 : m[1] === 'b' ? -1 : 0) }; })
      .sort((a, b) => a.st - b.st);
  }
  // g：{ name, war, intel, pol, sex, born }；ctx：{ family, virtue, mapScale }
  function generate(g, ctx, attempt) {
    const R = rng(fnv(g.name + '|' + (attempt || 0)));
    const T = temperOf(g);
    const deg = degList(ctx.mapScale);
    const n = deg.length;
    const at = i => { const o = Math.floor(i / n), d = deg[((i % n) + n) % n]; return { acc: d.acc, deg: d.deg, oct: d.oct + o }; };
    const pick = a => a[Math.floor(R() * a.length)];
    const bar1 = pick(RHY[T].a), bar2 = pick(RHY[T].b);
    const durs = bar1.concat(bar2);
    // 终止音：德 ≥ 60 宫 / 徵，≤ 30 羽 / 商，其余宫 徵 羽（按音级，取家族地图调式里最近的音）
    const near = pc => { let b = 0; deg.forEach((d, i) => { if (Math.abs(((d.st % 12) + 12) % 12 - pc) < Math.abs(((deg[b].st % 12) + 12) % 12 - pc)) b = i; }); return b; };
    const finals = ctx.virtue >= 60 ? [near(0), near(0), near(7)] : ctx.virtue <= 30 ? [near(9), near(2)] : [near(0), near(7), near(9)];
    const fin = pick(finals);
    const leapP = T === 'martial' ? 0.45 : T === 'scholar' ? 0.15 : 0.28;
    let idx = pick(T === 'martial' ? [fin - n, fin, fin + 2] : [fin, fin + 1, fin + 2]);
    const notes = [];
    const stOf = j => { const o = Math.floor(j / n); return deg[((j % n) + n) % n].st + 12 * o; };
    for (let i = 0; i < durs.length; i++) {
      if (i === durs.length - 1) idx = fin + (idx > fin + 3 ? n : 0);
      while (stOf(idx) < -7) idx += n;            // 音域 5, … 3''（−7 … +16 半音）
      while (stOf(idx) > 16) idx -= n;
      notes.push(Object.assign(at(idx), { dur: durs[i], orn: '', rest: false }));
      const leap = R() < leapP;
      const dir = R() < (i < durs.length / 2 ? 0.62 : 0.4) ? 1 : -1;
      idx += dir * (leap ? 2 + Math.floor(R() * 2) : 1);
      idx = Math.max(fin - n, Math.min(fin + n + 2, idx));
    }
    if (T === 'scholar') { let L = 0; notes.forEach((x, i) => { if (x.dur > notes[L].dur) L = i; }); notes[L].orn = '~'; }
    if (T === 'martial') notes[0].orn = '>';
    if (g.sex === 'f' && T !== 'martial') notes[1].orn = notes[1].orn || '^';
    const motif = notes.slice(0, bar1.length).map(x => noteHead(x) + ':' + x.dur + x.orn).join(' ') + ' | ' +
      notes.slice(bar1.length).map(x => noteHead(x) + ':' + x.dur + x.orn).join(' ') + ' |';
    const age = g.born ? 190 - g.born : 35;
    const lead = T === 'martial' ? pick(['reed', 'brass1', 'bow']) : T === 'scholar' ? pick(['flute', 'flute2', 'pluck4']) : pick(['bow', 'pluck', 'flute']);
    const answer = lead === 'pluck' || lead === 'pluck4' ? 'flute' : 'pluck';
    const tempo = +((T === 'martial' ? 1.04 : T === 'scholar' ? 0.96 : 1.0) - (age >= 55 ? 0.03 : 0) + (R() - 0.5) * 0.04).toFixed(3);
    const tr = Math.floor(R() * 5) - 2;
    return { motif, lead, answer, tempo, tr, temper: T, generated: true, attempt: attempt || 0 };
  }

  // ------------------------------------------------------------ distinctness (§A6.7) --
  function signature(motifStr) {
    const ns = parseMotif(motifStr).filter(n => !n.bar && !n.rest);
    const st = ns.map(absOfNote);
    const iv = []; for (let i = 1; i < Math.min(st.length, 7); i++) iv.push(Math.sign(st[i] - st[i - 1]) * Math.min(7, Math.abs(st[i] - st[i - 1])));
    const rh = ns.slice(0, 7).map(n => n.dur);
    return { iv, rh };
  }
  function lev(a, b) {
    const d = Array.from({ length: b.length + 1 }, (_, j) => j);
    for (let i = 1; i <= a.length; i++) { let p = d[0]; d[0] = i; for (let j = 1; j <= b.length; j++) { const t = d[j]; d[j] = Math.min(d[j] + 1, d[j - 1] + 1, p + (a[i - 1] === b[j - 1] ? 0 : 1)); p = t; } }
    return d[b.length];
  }
  function distance(A, B) { return lev(A.iv, B.iv) + 0.5 * lev(A.rh, B.rh); }
  const D_MIN = 3, D_MIN_HOUSE = 2, MAX_ATTEMPT = 64;
  // 调色规则（§A6.7）：同家族的两位起始君主在主奏乐器、速度（≥ 0.03）或调上至少差一项
  function paletteDiffers(a, b, family) {
    return inst(family, a.lead) !== inst(family, b.lead) || Math.abs(a.tempo - b.tempo) >= 0.03 || (a.tr || 0) !== (b.tr || 0);
  }
  // 冻结生成（tools/build-suites.js 与运行时补生成共用）：在 sigs（[name, signature]）与已有乐章之外找一个足够不同的动机
  function generateDistinct(g, ctx, sigs, palette) {
    for (let a = 0; a < MAX_ATTEMPT; a++) {
      const c = generate(g, ctx, a);
      const sg = signature(c.motif);
      const pOk = !palette || palette.every(p => p.family !== ctx.family || paletteDiffers(p, c, ctx.family));
      if (pOk && sigs.every(x => distance(sg, x[1]) >= D_MIN)) return { suite: Object.assign({ family: ctx.family }, c), sig: sg };
    }
    return null;
  }

  // §A10 the house cell: a generated heir of the same family name as the ruler keeps the founder's first three beats;
  // the heir's own notes from beat 3 on follow (tools/build-suites.js applies it to the clan of each starting ruler, then
  // re-checks distinctness: ≥ 2 against the founder, ≥ 3 against everyone else)
  function forceHead(heirMotif, founderMotif) {
    const hd = head(parseMotif(founderMotif));
    const ns = parseMotif(heirMotif).filter(n => !n.bar);
    const rest = [];
    let t = 0;
    for (const n of ns) {
      const a = t, b = t + n.dur; t = b;
      if (b <= 3 + 1e-9) continue;
      rest.push(Object.assign({}, n, { dur: +(b - Math.max(a, 3)).toFixed(4), orn: a < 3 ? '' : n.orn }));
    }
    const all = hd.concat(rest);
    const toks = [];
    let pos = 0, barred = false;
    for (const n of all) {
      toks.push(noteHead(n) + ':' + (+n.dur.toFixed(4)) + (n.orn || '').replace(/\)/g, ''));
      pos += n.dur;
      if (!barred && pos >= 4 - 1e-9) { toks.push('|'); barred = true; }
    }
    if (toks[toks.length - 1] !== '|') toks.push('|');
    return toks.join(' ');
  }

  // ------------------------------------------------------------ piece builder (§A7) --
  function clone(o) { return JSON.parse(JSON.stringify(o)); }
  function buildSuite(MU, id, S, OCTT) {
    OCTT = OCTT || OCT;
    const fam = S.family, cul = TEMPLATE_CULTURE[fam] || 'han';
    const T = k => MU.get(MU.resolve(k, cul)) ? MU.resolve(k, cul) : null;
    const leadI = inst(fam, S.lead), ansI = inst(fam, S.answer);
    const pieces = {};
    const motif = parseMotif(S.motif);
    const voiceOf = (i, extra) => Object.assign({ inst: i, oct: OCTT[i] !== undefined ? OCTT[i] : 0 }, extra || {});
    const padI = fam === 'hua' || fam === 'han3' || fam === 'wa' || fam === 'nanhai' ? 'strings' : (fam === 'steppe' ? 'kargyraa' : 'drones');
    function wrap(kind, tplKey, how) {
      const src = MU.get(tplKey);
      if (!src) return;
      const P = clone(src);
      delete P.key_;
      const meter = P.meter || 4;
      const sc = scaleInfo(P.scale || '1 2 3 5 6');
      const ns = motif.map(n => respellNote(n, sc));
      const tAbs = tonicAbsOf(P);
      P.title = id + '·' + P.title; P.transpose = (P.transpose || 0) + (S.tr || 0);
      P.bpm = Math.round(P.bpm * (S.tempo || 1) * (how.bpmK || 1));
      for (const sec of Object.values(P.sections)) {
        if (sec.bpm) sec.bpm = Math.round(sec.bpm * (S.tempo || 1) * (how.bpmK || 1));
        if (sec.bpmTo) sec.bpmTo = Math.round(sec.bpmTo * (S.tempo || 1) * (how.bpmK || 1));
        if (how.dyn) sec.dyn = +((sec.dyn || 1) * how.dyn).toFixed(3);
        if (how.noDrums) delete sec.dr;
      }
      if (how.noDrums) P.gain = +((P.gain || 1) * 1.26).toFixed(3);   // +2 dB：去掉鼓的议事曲更轻（§A12）
      // 主奏替换（§A7.2）：模板里音符最多的连奏声部改用君主的主奏乐器；拨弦 / 钟 / 垫音主奏不替换
      const LEGATO = MU.INST[how.leadInst || leadI] && MU.INST[how.leadInst || leadI].type === 'legato';
      if (LEGATO && how.swap !== false) {
        let best = null, bestN = 0;
        for (const [vn, v] of Object.entries(P.voices)) {
          if (!MU.INST[v.inst] || MU.INST[v.inst].type !== 'legato') continue;
          let n = 0; for (const sec of Object.values(P.sections)) if (typeof sec[vn] === 'string') n += (sec[vn].match(/(^|\s)[#b]?[1-7]/g) || []).length;
          if (n > bestN) { bestN = n; best = vn; }
        }
        if (best) { P.voices[best] = Object.assign({}, P.voices[best], { inst: how.leadInst || leadI }); P._leadSwap = best + ':' + (how.leadInst || leadI); }
      }
      P.voices.mL = voiceOf(how.leadInst || leadI, { pan: 0.08, gain: 0.95 });
      P.voices.mA = voiceOf(how.ansInst || ansI, { pan: -0.18, gain: 0.8 });
      P.voices.mP = voiceOf(padI, { oct: -1, gain: 0.55 });
      P.voices.mB = voiceOf('lowstr', { oct: -2, gain: 0.6 });
      P.kit = P.kit || {}; P.pats = P.pats || {};
      P.kit.mD = kitEntry(inst(fam, 'drum'), spellAbs(tAbs)); P.kit.mG = kitEntry(inst(fam, 'gong'), spellAbs(tAbs - 12));
      const m1 = render(ns, meter, { aug: how.aug || 1 });
      const bars = m1.bars, mbar = meter === 3 ? 3 : meter;
      const add = [];
      for (const [sec, spec] of Object.entries(how.sections)) {
        const o = {};
        if (spec === 'intro') {             // Mi / Mr：主奏 2 小节，应答 2 小节，持续音
          const ans = render(ns, meter);
          o.mL = m1.str + ' ' + rests(ans.bars, mbar); o.mA = rests(bars, mbar) + ' ' + ans.str;
          o.mP = '!p ' + droneBars(bars + ans.bars, mbar, tAbs); o.mB = '!p ' + droneBars(bars + ans.bars, mbar, tAbs - 12, false);
        } else if (spec === 'develop') {    // Mv（D 段）：应答上二度模进 → 主奏原形 → 应答倒影 → 主奏扩大
          const up = render(ns.map(n => seqDeg(n, 1, sc)), meter), dn = render(invDeg(ns, sc), meter);
          const aug = render(ns, meter, { aug: 2 });
          const total = up.bars + bars + dn.bars + aug.bars;
          o.mA = '!mp ' + up.str + ' ' + rests(bars, mbar) + ' ' + dn.str + ' ' + rests(aug.bars, mbar);
          o.mL = rests(up.bars, mbar) + ' !mf ' + m1.str + ' ' + rests(dn.bars, mbar) + ' !f ' + aug.str;
          o.mP = '!pp ' + droneBars(total, mbar, tAbs); o.mB = '!p ' + droneBars(total, mbar, tAbs - 12, false);
        } else if (spec === 'tag') {        // Mt：应答乐器轻奏动机
          o.mA = '!mp ' + m1.str; o.mP = '!pp ' + droneBars(bars, mbar, tAbs);
        } else if (spec === 'call') {       // Mc / MB / Md：主奏在鼓上奏号令，低音脉动
          o.mL = '!f ' + m1.str; o.mB = '!mf ' + droneBars(bars, mbar, tAbs - 12, false);
          const db = drumBar(mbar, how.martial ? 'martial' : 'plain');
          P.pats['p' + sec] = { _step: db.step, mD: Array(bars).fill(db.s).join(' '), mG: Array(bars).fill(0).map((_, i) => i === 0 ? 'x' + '.'.repeat(db.s.length - 1) : '.'.repeat(db.s.length)).join(' ') };
          o.dr = 'p' + sec;
        }
        P.sections[sec] = o; add.push(sec);
      }
      how.place(P, add);
      pieces[kind] = P;
    }
    const mapK = T('map');
    // D 段 Mv 放在模板 B 段之后；循环里没有 B 段的模板放在循环中部
    const midLoop = (P, sec) => { const i0 = P.form.indexOf(P.loopFrom); const iB = P.form.indexOf('B', i0); if (iB >= i0) { P.form.splice(iB + 1, 0, sec); return; } const n = P.form.length - i0; P.form.splice(i0 + Math.max(1, Math.floor(n / 2)), 0, sec); };
    wrap('map', mapK, { sections: { Mi: 'intro', Mr: 'intro', Mt: 'tag', Mv: 'develop' }, place: (P) => {
      midLoop(P, 'Mv'); const i = P.form.indexOf(P.loopFrom); P.form.splice(i, 0, 'Mr'); P.loopFrom = 'Mr';
      P.form = ['Mi'].concat(P.form, ['Mt']); } });
    const councilK = fam === 'hua' ? 'council' : mapK;
    wrap('council', councilK, { bpmK: fam === 'hua' ? 1 : 0.88, noDrums: fam !== 'hua', dyn: 0.9, leadInst: inst(fam, 'pluck'), sections: { Mi: 'intro', Mt: 'tag' }, place: (P) => { P.form = ['Mi'].concat(P.form, ['Mt']); } });
    wrap('ending', 'ending', { leadInst: 'choir', ansInst: inst(fam, 'brass1') === 'horn' ? 'horn' : inst(fam, 'brass1'), swap: false, sections: { Mi: 'intro' }, place: (P) => { P.form = ['Mi'].concat(P.form); } });
    const batK = T('battle');
    wrap('battle', batK, { martial: true, leadInst: inst(fam, S.lead === 'flute' || S.lead === 'flute2' || S.lead === 'pluck' || S.lead === 'pluck4' ? 'reed' : S.lead), sections: Object.assign({ Mc: 'call', Mv: 'develop' }, fam === 'hua' ? { MB: 'call' } : {}), place: (P) => {
      const i = P.form.indexOf(P.loopFrom); P.form.splice(i, 0, 'Mc'); P.loopFrom = 'Mc';
      if (fam === 'hua' && P.form.indexOf('B') > 0) P.form.splice(P.form.indexOf('B'), 0, 'MB'); else delete P.sections.MB;
      midLoop(P, 'Mv');
    } });
    const defK = fam === 'hua' ? 'battle-defend' : batK;
    wrap('battle-defend', defK, { bpmK: fam === 'hua' ? 1 : 0.85, dyn: 0.88, aug: 2, leadInst: inst(fam, 'bow'), sections: { Md: 'call' }, place: (P) => { const i = P.form.indexOf(P.loopFrom); P.form.splice(i, 0, 'Md'); P.loopFrom = 'Md'; } });
    // 短曲：由动机在战斗模板的调式里生成
    const B = MU.get(batK), M = MU.get(mapK);
    const scB = scaleInfo(B.scale), scM = scaleInfo(M.scale);
    const nsB = motif.map(n => respellNote(n, scB)), nsM = motif.map(n => respellNote(n, scM));
    const tB = tonicAbsOf(B), tM = tonicAbsOf(M);
    const kitF = { D: kitEntry(inst(fam, 'drum'), spellAbs(tB)), t: kitEntry(inst(fam, 'drum2'), spellAbs(tB)), G: kitEntry(inst(fam, 'gong'), spellAbs(tB - 12)), C: kitEntry(inst(fam, 'cymbal'), spellAbs(tB)) };
    const brass = inst(fam, 'brass1') === 'horn' && fam !== 'hua' ? inst(fam, 'reed') : inst(fam, 'brass1');
    // 凯旋（6 小节 ≈ 13.8 秒 ♩104）：铜管在滚奏上奏动机 → 主奏高四度 → 长持的终止音、锣与钹
    {
      const r = render(nsM, 4), up = render(nsM.map(n => n.bar || n.rest ? n : respellNote(Object.assign({}, n, { deg: ((n.deg + 2) % 7) + 1, oct: n.oct + (n.deg + 3 > 7 ? 1 : 0), acc: '' }), scM)), 4);
      pieces.victory = { title: id + '·凯旋', key: M.key, bpm: 104, meter: 4, scale: M.scale, tonic: M.tonic, loop: false, gain: 1.0, transpose: S.tr || 0,
        voices: { L: voiceOf(brass, { pan: 0.1 }), A: voiceOf(leadI, { pan: -0.15 }), H: voiceOf(inst(fam, 'brass'), { oct: -1, gain: 0.8 }), P: voiceOf('strings', { oct: -1, gain: 0.7 }), Bs: voiceOf('lowstr', { oct: -2 }), Tp: voiceOf('timpani', { oct: -2 }) },
        kit: kitF, pats: { roll: { D: 'o.o.o.oooxxxxXXX', G: '................' }, hit: { D: 'X.......x...x...', G: 'X...............' }, end: { D: 'X...............', G: 'X...............', C: 'X...............' } },
        sections: {
          A: { L: '!f ' + r.str, P: '!mf ' + droneBars(2, 4, tM), Bs: '!mf ' + droneBars(2, 4, tM - 12, false), Tp: `!mf ${spellAbs(tM - 12)}:4* | ${spellAbs(tM - 12)}:4 |`, dr: 'roll hit' },
          B: { A: '!f ' + up.str, H: '!f ' + droneBars(2, 4, tM + 5), P: '!f ' + droneBars(2, 4, tM + 5), Bs: '!f ' + droneBars(2, 4, tM - 7, false), dr: 'hit*2' },
          C: { L: '!ff ' + r.str, A: '!f ' + r.str, H: '!f ' + droneBars(2, 4, tM), P: '!f ' + droneBars(2, 4, tM), Bs: '!f ' + droneBars(2, 4, tM - 12, false), dr: 'hit*2' },
          E: { L: `!ff ${spellAbs(tM)}:4 | ${spellAbs(tM)}:4 |`, H: '!ff ' + droneBars(2, 4, tM), P: '!f !> ' + droneBars(2, 4, tM) + ' !p', Bs: '!f ' + droneBars(2, 4, tM - 12, false), Tp: `!f ${spellAbs(tM - 12)}:4* | ${spellAbs(tM - 12)}:4 |`, dr: 'end _' },
        }, form: ['A', 'B', 'E'] };
    }
    // 残阳：动机头四音扩大 ×1.5 在弓弦上，落到战斗终止音，合唱垫音
    {
      const evs = render(nsB.filter(n => !n.bar).slice(0, 4), 4, { aug: 1.5 });
      const totalBars = Math.ceil(evs.beats / 4) + 2;
      const pad = Math.ceil(evs.beats / 4) * 4 - evs.beats;
      pieces.defeat = { title: id + '·残阳', key: B.key, bpm: 60, meter: 4, scale: B.scale, tonic: B.tonic, loop: false, gain: 1.05, transpose: S.tr || 0,
        voices: { L: voiceOf(inst(fam, 'bow'), { pan: 0.05 }), P: voiceOf('choirOo', { oct: -1, gain: 0.6 }), Bs: voiceOf('lowstr', { oct: -2, gain: 0.7 }) },
        sections: { A: {
          L: '!mp ' + evs.str.replace(/\s*\|\s*$/, '') + (pad > 1e-9 ? ` 0:${+pad.toFixed(4)} |` : ' |') + ` !p ${spellAbs(tB)}:4~ | 0:4 |`,
          P: '!pp ' + droneBars(totalBars, 4, tB), Bs: '!pp ' + droneBars(totalBars, 4, tB - 12, false) } }, form: ['A'] };
    }
    // 号令（2 小节 ≈ 3.6 秒 ♩132）：动机头三拍在铜管上，终止音长持，定音鼓与锣——君主的「名帖」
    {
      const h = head(nsM);
      const r = render(h.concat([Object.assign({}, nsM.filter(n => !n.bar && !n.rest).slice(-1)[0], { dur: 5, orn: '' })]), 4);
      pieces.fanfare = { title: id + '·号令', key: M.key, bpm: 132, meter: 4, scale: M.scale, tonic: M.tonic, loop: false, gain: 0.85, transpose: S.tr || 0,
        voices: { L: voiceOf(brass, { pan: 0.05 }), H: voiceOf(inst(fam, 'brass'), { oct: -1, gain: 0.7 }), Tp: voiceOf('timpani', { oct: -2 }) },
        kit: kitF, pats: { f: { D: 'X.......o.o.xxXX X...............', G: '................ X...............' } },
        sections: { A: { L: '!f ' + r.str, H: '!mf 0:3 ' + `${spellAbs(tM)}+${spellAbs(tM + 7)}:1 | ${spellAbs(tM)}+${spellAbs(tM + 7)}:4 |`, Tp: `!mf 0:2 ${spellAbs(tM - 12)}* ${spellAbs(tM - 12)}* | ${spellAbs(tM - 12)}:4 |`, dr: 'f' } }, form: ['A'] };
    }
    // 冲锋乐句（第四版时间轴，§A8）：接触时开始；第 2 小节强拍（♩150 时 1.6 秒）= melee1
    {
      const h = head(nsB);
      const cl = (pat, L2) => ({ title: id + '·冲锋', key: B.key, bpm: 150, meter: 4, scale: B.scale, tonic: B.tonic || spellAbs(tB), loop: false, gain: 0.9,
        voices: { L: voiceOf(brass, { pan: 0.1 }), H: voiceOf(inst(fam, 'brass'), { oct: -1 }), Tp: voiceOf('timpani', { oct: -2 }) }, kit: kitF, pats: { c: pat },
        sections: { A: Object.assign({ dr: 'c' }, L2) }, form: ['A'] });
      const hr = render(h.concat([{ rest: true, dur: 1 }]), 4);
      const fifth = `${spellAbs(tB)}+${spellAbs(tB + 7)}`;
      pieces.clash = cl({ D: 'X...o.x.x.xxXXXX X.....x.X.......', t: '....o.o.xxxxrrrr ..x...x.........', C: '................ X...............', G: '................ X...............' },
        { L: '!f ' + hr.str + ` !ff ${spellAbs(tB + 12)}:2 ${spellAbs(tB)}:2 |`, H: `0:4 | !f ${fifth}:4 |`, Tp: `!mf !< ${spellAbs(tB - 12)}* ${spellAbs(tB - 12)}* ${spellAbs(tB - 12)}* ${spellAbs(tB - 12)}* !f | ${spellAbs(tB - 12)}:2 ${spellAbs(tB - 12)}:2 |` });
      pieces['clash-b'] = cl({ D: 'X.x.X.x.X.x.xxXX X.....x.X.......', t: 'x.x.x.x.xxxxrrrr ..x...x.........', C: '................ X...............', G: '................ X...............' },
        { H: `!mf !< ${fifth}:2 ${spellAbs(tB + 2)}+${spellAbs(tB + 7)}:2 | !ff ${fifth}:4 |`, L: `0:4 | !f ${spellAbs(tB + 12)}:4 |`, Tp: `!mf ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)} ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)} | !f ${spellAbs(tB - 12)}:4 |` });
      const hs = render(h.map(n => Object.assign({}, n, { dur: n.dur / 2 })).concat([{ rest: true, dur: 2.5 }]), 4);
      pieces['clash-short'] = Object.assign(cl({ D: 'x.x.xxxxX.......', t: 'xxxxrrrr........', C: '........X.......', G: '........X.......' },
        { L: '!f ' + hs.str, H: `0:2 !f ${fifth}:2 |`, Tp: `!f ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)} ${spellAbs(tB - 12)}:2 |` }), { title: id + '·冲锋·短' });
      // 会心（攻击画面多出 0.43 秒的慢拍）：一拍头音，第二拍铜管强奏与大锣（= 会心一拍的正中，audio.js 按 tlMap(contact + 0.36) ÷ 速度
      // 往前推一拍开始，所以任何速度下都能对准），其余随慢动作延音
      pieces['clash-crit'] = Object.assign(cl({ D: 'X...X.......x... X.....x.X.......', G: '....X........... X...............', C: '....X........... ................' },
        { L: `!f ${noteHead(h[0])}:1 !ff ${spellAbs(tB + 12)}:3 | ${spellAbs(tB + 12)}:2 ${spellAbs(tB)}:2 |`, H: `0:1 !ff ${fifth}:3 | ${fifth}:4 |`, Tp: `!f ${spellAbs(tB - 12)} ${spellAbs(tB - 12)}* 0:2 | ${spellAbs(tB - 12)}:4 |` }), { title: id + '·会心' });
    }
    return pieces;
  }
  // 单挑（§A9）：龙争虎斗前插 Ma（挑战者的动机）与 Mb（应战者），各在自己家族的主奏上（主奏为拨弦时用吹管），左右分开
  function buildDuel(MU, A, B, names, OCTT) {
    OCTT = OCTT || OCT;
    const src = MU.get('duel');
    if (!src) return null;
    const P = clone(src);
    delete P.key_;
    const sc = scaleInfo(P.scale), tAbs = tonicAbsOf(P);
    const ra = render(parseMotif(A.motif).map(n => respellNote(n, sc)), P.meter || 4), rb = render(parseMotif(B.motif).map(n => respellNote(n, sc)), P.meter || 4);
    const lead = S => inst(S.family, /^pluck/.test(S.lead) ? 'reed' : S.lead);
    const la = lead(A), lb = lead(B);
    P.title = names[0] + '·' + names[1] + '·' + P.title;
    P.voices.MaL = { inst: la, oct: OCTT[la] || 0, pan: -0.3, gain: 0.95 };
    P.voices.MbL = { inst: lb, oct: OCTT[lb] || 0, pan: 0.3, gain: 0.95 };
    P.voices.mB = { inst: 'lowstr', oct: -2, gain: 0.6 };
    const m = P.meter || 4;
    P.sections.Ma = { MaL: '!f ' + ra.str, mB: '!mf ' + droneBars(ra.bars, m, tAbs - 12, false) };
    P.sections.Mb = { MbL: '!f ' + rb.str, mB: '!mf ' + droneBars(rb.bars, m, tAbs - 12, false) };
    const ia = P.form.indexOf('A'); P.form.splice(ia >= 0 ? ia : 0, 0, 'Ma');
    const ib = P.form.indexOf('B'); if (ib >= 0) P.form.splice(ib, 0, 'Mb'); else P.form.push('Mb');
    P.loopFrom = 'Ma';
    return P;
  }

  // ------------------------------------------------------------ roster / runtime generation --
  // 武将资料：游戏中的 SG.G.generals，或剧本数据（SG.Scenarios）
  function genInfo(x) {
    if (!x) return null;
    if (typeof x === 'object') return x;
    const G = SG.G;
    if (G && G.generals) { const g = G.generals.find(o => o.name === x); if (g) return g; }
    const sc = SG.Scenarios;
    if (sc) for (const s of [sc.classic, sc.world]) {
      if (!s || !s.Generals) continue;
      for (const l of s.Generals) {
        const p = l.split('|');
        if (p[0] === x) return { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], factionKey: p[4], culture: p[9] || null };
      }
    }
    return { name: String(x), war: 50, intel: 50, pol: 50 };
  }
  function cultureOfGen(g) {
    if (g.culture) return g.culture;
    try { if (SG.WorldData && SG.WorldData.cultureOfGeneral) { const c = SG.WorldData.cultureOfGeneral(g.name); if (c) return c; } } catch (e) { /* 忽略 */ }
    return 'han';
  }
  function familyOf(gOrCulture) {
    if (typeof gOrCulture === 'string') return FAMILY_OF[gOrCulture] || (SG.cultureBase && FAMILY_OF[SG.cultureBase(gOrCulture)]) || 'hua';
    return familyOf(cultureOfGen(gOrCulture));
  }
  function virtueOf(g) {
    try {
      const G = SG.G;
      if (G && G.factions && g.faction >= 0 && G.factions[g.faction]) return G.factions[g.faction].virtue;
      const key = g.factionKey;
      const sc = SG.Scenarios;
      if (key && sc) for (const s of [sc.classic, sc.world]) for (const l of (s && s.Factions) || []) { const p = l.split('|'); if (p[0] === key) return +p[4]; }
    } catch (e) { /* 忽略 */ }
    return 50;
  }
  function sexOf(g) { if (g.sex) return g.sex; try { return SG.WorldData && SG.WorldData.sexOf ? SG.WorldData.sexOf(g.name) : 'm'; } catch (e) { return 'm'; } }
  function bornOf(g) { if (g.born) return g.born; try { return SG.WorldData && SG.WorldData.bornOf ? SG.WorldData.bornOf(g.name) : null; } catch (e) { return null; } }
  let sigCache = null;
  const runtime = {};       // 冻结表之外、运行时生成的乐章（排在冻结表之后，按首次请求的顺序）
  function allSigs() {
    if (!sigCache) {
      sigCache = [];
      for (const [n, s] of Object.entries(HAND)) sigCache.push([n, signature(s.motif)]);
      for (const [n, s] of Object.entries(MS.GEN || {})) sigCache.push([n, signature(s.motif)]);
    }
    return sigCache;
  }
  function suiteOf(x) {
    if (!x) return null;
    const name = typeof x === 'string' ? x : x.name;
    if (!name) return null;
    if (HAND[name]) return HAND[name];
    const G0 = MS.GEN && MS.GEN[name];
    if (G0) return G0;
    if (runtime[name]) return runtime[name];
    const MU = SG.Music;
    const g = genInfo(x);
    const family = familyOf(g);
    let mapScale = '1 2 3 5 6';
    try { const k = MU && MU.resolve('map', TEMPLATE_CULTURE[family]); if (k && MU.get(k)) mapScale = MU.get(k).scale; } catch (e) { /* 默认五声 */ }
    const ctx = { family, virtue: virtueOf(g), mapScale };
    const gi = { name, war: g.war | 0, intel: g.intel | 0, pol: g.pol | 0, sex: sexOf(g), born: bornOf(g) };
    const r = generateDistinct(gi, ctx, allSigs(), null) || { suite: Object.assign({ family }, generate(gi, ctx, 0)), sig: null };
    if (r.sig) allSigs().push([name, r.sig]);
    runtime[name] = r.suite;
    return r.suite;
  }

  // ------------------------------------------------------------ registration (§A11) --
  const KINDS = ['map', 'council', 'battle', 'battle-defend', 'victory', 'defeat', 'fanfare', 'clash', 'clash-b', 'clash-short', 'clash-crit', 'ending'];
  const MAX_ALIVE = 4;
  const alive = [];          // 已登记乐章的君主名（最近使用的在后）
  const pinned = new Set();
  let duelKey = null;
  function keyOf(name, kind) { return 'suite:' + name + ':' + kind; }
  function inUse(name) {
    try { const k = SG.Sfx && SG.Sfx.playingMusic; return !!(k && k.indexOf('suite:' + name + ':') === 0); } catch (e) { return false; }
  }
  function unregister(name) {
    const MU = SG.Music;
    if (!MU) return;
    for (const k of KINDS) { const key = keyOf(name, k); if (MU.remove) MU.remove(key); else if (MU.PIECES) delete MU.PIECES[key]; }
    const i = alive.indexOf(name); if (i >= 0) alive.splice(i, 1);
  }
  function touch(name) { const i = alive.indexOf(name); if (i >= 0) alive.splice(i, 1); alive.push(name); }
  const stats = { built: 0, buildMs: 0, failed: 0 };
  function ensure(x) {
    const MU = SG.Music;
    if (!MU || !MU.get || !MU.get('map')) return false;
    const name = typeof x === 'string' ? x : x && x.name;
    if (!name) return false;
    if (MU.get(keyOf(name, 'map'))) { touch(name); return true; }
    const S = suiteOf(x);
    if (!S) return false;
    let pieces;
    const t0 = (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now();
    try { pieces = buildSuite(MU, name, S); } catch (e) { stats.failed++; if (typeof console !== 'undefined') console.warn('君主乐章生成失败 ' + name, e); return false; }
    for (const [kind, P] of Object.entries(pieces)) MU.add(keyOf(name, kind), P);
    stats.built++; stats.buildMs += ((typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now()) - t0;
    touch(name);
    // 同时最多 MAX_ALIVE 套：注销最久未用、未钉住、未在播放的
    while (alive.length > MAX_ALIVE) {
      const victim = alive.find(n => n !== name && !pinned.has(n) && !inUse(n));
      if (!victim) break;
      unregister(victim);
    }
    return true;
  }
  // kind → key（opts.suite = 君主名或武将；foe 只用于没有 suite 时）
  function resolve(kind, opts) {
    opts = opts || {};
    const who = opts.suite || opts.foe;
    if (!who || !kind) return null;
    const k = kind === 'clash-c' ? 'clash-b' : kind;
    if (KINDS.indexOf(k) < 0) return null;
    if (!ensure(who)) return null;
    const name = typeof who === 'string' ? who : who.name;
    const key = keyOf(name, k);
    return SG.Music.get(key) ? key : null;
  }
  function headCell(x) {
    const S = suiteOf(x);
    if (!S) return null;
    return head(parseMotif(S.motif)).map(n => noteHead(n) + ':' + (+n.dur.toFixed(4)) + (n.orn || '').replace(/\)/g, '')).join(' ');
  }
  function duel(a, b) {
    const MU = SG.Music;
    if (!MU || !MU.get || !MU.get('duel')) return null;
    const na = typeof a === 'string' ? a : a && a.name, nb = typeof b === 'string' ? b : b && b.name;
    if (!na || !nb) return null;
    const key = 'suite-duel:' + na + '|' + nb;
    if (MU.get(key)) return key;
    const A = suiteOf(a), B = suiteOf(b);
    if (!A || !B) return null;
    let P;
    try { P = buildDuel(MU, A, B, [na, nb]); } catch (e) { if (typeof console !== 'undefined') console.warn(e); return null; }
    if (!P) return null;
    if (duelKey && duelKey !== key && !(SG.Sfx && SG.Sfx.playingMusic === duelKey)) { if (MU.remove) MU.remove(duelKey); else delete MU.PIECES[duelKey]; }
    MU.add(key, P);
    duelKey = key;
    return key;
  }
  function pin(name, on) { if (!name) return; if (on === false) pinned.delete(name); else pinned.add(name); }

  Object.assign(MS, {
    HAND, FAMILY_OF, TEMPLATE_CULTURE, HUA, FAMILY_INST, OCT, KINDS, D_MIN, D_MIN_HOUSE, MAX_ALIVE,
    parseMotif, respellNote, seqDeg, invDeg, scaleInfo, render, generate, generateDistinct, signature, distance, paletteDiffers,
    buildSuite, buildDuel, temperOf, motifShare, head, noteHead, inst, familyOf, fnv, forceHead,
    suiteOf, ensure, resolve, headCell, duel, pin, unregister,
    stats,
    _resetCache() { sigCache = null; for (const k of Object.keys(runtime)) delete runtime[k]; },
  });
  // 访问器（Object.assign 会把 getter 求值成一次性的值，所以单独定义）
  Object.defineProperty(MS, 'alive', { get() { return alive.slice(); }, configurable: true });
  Object.defineProperty(MS, 'runtime', { get() { return Object.assign({}, runtime); }, configurable: true });
  if (!MS.GEN) MS.GEN = {};
})();
