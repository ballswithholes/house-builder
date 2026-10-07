'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 武将外貌设定（头像数据，DESIGN-V2 §4B）

   必须在 js/portrait.js 之前载入。没有条目的武将由 portrait.js 按姓名哈希 +
   能力值 + 文化确定性地生成（同名永远同脸）；这里的条目只需写与默认不同的字段，
   其余字段仍由生成器补全。

   追加条目（第二阶段的世界武将等）：
     SG.PortraitData.add({ '卑弥呼': { culture: 'wa', sex: 'f', age: 40, ... } });
   add() 会合并同名旧条目并清空头像缓存。查询：SG.PortraitData.get(name)。

   ---------------------------------------------------------------- 外貌参数 --
   顶层
     culture   文化代号：han nanman wa yi korea steppe seasia tarim kushan persia arab
               roman celt german sarmatian（缺省 han；孟获一族自动为 nanman）
     sex       'm' | 'f'
     role      'warrior' | 'general' | 'strategist' | 'official' | 'ruler' | 'commoner' | 'female'
               （缺省按 武/智/政 推断；影响默认冠服与胡须）
     age       画像年龄（缺省：有 born 时按当前年份推算，否则按角色随机）
     skin      肤色 '#rrggbb'（关羽赤面 '#b8523c'）
     fat       0..1 胖瘦（董卓 0.95）       build  肩宽 0.85..1.2      neck  颈粗 0.7..1.45
     expr      'neutral' | 'calm' | 'stern' | 'fierce' | 'smile' | 'sly'
     blush     腮红倍数（缺省 1）
   face     { w 脸宽 0.9..1.1, jaw 下颌宽 0.8..1.25, chin 下巴宽, len 脸长 0.9..1.12,
              cheek 颧骨 0..1, hollow 两颊凹陷 0..1, brow 眉弓 0..1.4 }
   eyes     { shape, color, size 0.85..1.15, gap 眼距 -0.04..0.04, y 上下 -0.04..0.04 }
              shape：normal sharp phoenix(丹凤眼) round(环眼) narrow gentle big sleepy
   brows    { shape, thick 0.6..1.5, color, y }
              shape：straight arched angled bushy thin knit silkworm(卧蚕眉) sword(剑眉) worried
   nose     { shape, size, len, w }   shape：straight aquiline broad button small bulb flat
   mouth    { w, lips, y, shape }     shape：neutral smile grim smirk
   ears     { size 0.9..1.4, lobe 1..1.6 }（刘备大耳垂肩）
   hair     { style, color, vol, locks 鬓边散发 0..2, recede 发际后退, sideburn, tie 发带色, pin 簪色, flower }
              style：topknot bun twinbun long mallet(椎髻) wild mizura(美豆良) wahair koreanbraid
                     kunfa(髡发) braids highbun bob short curly bushy spiky suebian romanf bald
   beard    { style, len 0.6..1.6, color, jaw 连鬓 }
              style：none stubble goatee short full(络腮) bristle(虬髯) long flowing(美髯) forked curled braided
   mustache { style }                 style：none thin droop thick bristle curl
   hat      { type, color, color2, variant, plume, trim, gem, liang, pearls, neck, ribbon }
              type：none ze(帻) jinxian(进贤冠) futou(幅巾) turban(头巾) headband(抹额) lunjin(纶巾)
                    wuguan(武冠) helmet(兜鍪) crown(束发冠) pheasant(雉翎紫金冠) daoist(莲花冠)
                    feathers chief wahelm shaman yiband jeolpung(折风) birdfeather goldcrown(新罗金冠)
                    felt fur furcap tallcrown flowerwrap pointcap tallhat diadem tiara phrygian
                    keffiyeh arabturban hatra laurel galea celthelm spangen conical
              helmet.variant：plain round spike wing(凤翅) horn lion(狮首) plume
   outfit   { type, color, color2, metal, trim, cape ('faction' | '#hex'), inner, pelt ('tiger'|'leopard') }
              type：robe lamellar(札甲) plate(明光铠) robearmor pelt rattan(藤甲) bare chiefrobe kantoui(贯头衣)
                    tanko(短甲) yivest jacket kaftan fur sash scale(鱼鳞甲) tunic robea segmentata
                    musculata toga plaid
   acc      配饰数组：fan(羽扇) eyepatch earring hoops bigears bones magatama shells torc goldcollar
                     mirror knives(鬓插飞刀) flower
   marks    面部标记：scar scars mole tattoo-wa tattoo-yi woad warpaint freckles scales
   weapon   肩后兵器：guandao(青龙偃月刀) snake(丈八蛇矛) halberd(方天画戟) spear ji(双戟) bow
                     sword swords(双股剑) axe
   简写：字段值可直接写字符串，如 hat: 'turban' 等于 hat: { type: 'turban' }。
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});
  const entries = Object.create(null);
  SG.PortraitData = {
    entries,
    add(map) {
      for (const k in map) entries[k] = Object.assign({}, entries[k] || {}, map[k]);
      if (SG.Portrait && SG.Portrait.clearCache) SG.Portrait.clearCache();
    },
    get(name) { return entries[name] || null; },
    names() { return Object.keys(entries); },
  };

  // 常用色
  const W = '#ece8e0', GRAY = '#b8b4ac', BLACK = '#16131a';
  SG.PortraitData.add({
    // ------------------------------------------------------------- 董卓 --
    '董卓': { age: 52, fat: 0.95, neck: 1.45, face: { w: 1.06, jaw: 1.25 }, eyes: { shape: 'narrow', size: 0.88 }, brows: 'bushy', nose: 'bulb', hair: { color: '#2a2422' },
      beard: { style: 'full', len: 0.75, color: '#231c1c' }, mustache: 'thick', hat: { type: 'jinxian', color: '#2a1a1e' }, outfit: { type: 'robe', color: '#5a1a2a', color2: '#c8a050', cape: '#3a1018' }, expr: 'sly' },
    '吕布': { age: 32, build: 1.15, face: { w: 0.98, jaw: 1.02, len: 1.02 }, eyes: 'sharp', brows: { shape: 'sword', thick: 1.15 }, beard: 'none', mustache: 'none',
      hat: { type: 'pheasant' }, outfit: { type: 'plate', metal: '#b89048', color: '#b02a2a', trim: '#e8c060' }, weapon: 'halberd', expr: 'sly' },
    '李儒': { age: 44, face: { w: 0.94, len: 1.08, hollow: 0.5 }, eyes: 'narrow', brows: 'thin', nose: 'aquiline', beard: { style: 'goatee', len: 1.1 }, mustache: 'thin',
      hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#2a2a3a', color2: '#5a1a2a' }, expr: 'sly' },
    '华雄': { age: 34, build: 1.2, neck: 1.3, face: { w: 1.06, jaw: 1.25, brow: 1.3 }, eyes: 'round', brows: 'bushy', nose: 'broad', beard: { style: 'bristle', len: 1.0 }, mustache: 'bristle',
      hat: { type: 'helmet', variant: 'spike', color: '#4a4e58', plume: '#1a1a1a' }, outfit: { type: 'lamellar', metal: '#4a4e58', color: '#5a1a1a' }, weapon: 'axe', expr: 'fierce' },
    '李傕': { age: 38, face: { jaw: 1.15, hollow: 0.3 }, eyes: 'sharp', brows: 'knit', beard: { style: 'short' }, mustache: 'droop', hat: { type: 'helmet', variant: 'round', color: '#5a5e68' },
      outfit: { type: 'lamellar', metal: '#5a5e68', color: '#6a2a2a' }, expr: 'sly', marks: ['scar'] },
    '郭汜': { age: 38, face: { w: 1.04, chin: 1.1 }, eyes: 'normal', brows: 'bushy', nose: 'broad', beard: { style: 'full', len: 0.8 }, mustache: 'thick', hat: { type: 'ze', color: '#6a2020' },
      outfit: { type: 'lamellar', metal: '#7a6a48', color: '#3a2a2a' }, expr: 'stern' },
    '张济': { age: 50, eyes: 'sleepy', brows: 'straight', beard: { style: 'long', len: 1.0 }, mustache: 'thin', hat: { type: 'helmet', variant: 'plain', color: '#6a6e78' },
      outfit: { type: 'lamellar', metal: '#6a6e78', color: '#2a3a4a' }, expr: 'calm' },
    '樊稠': { age: 36, build: 1.12, eyes: 'round', brows: 'angled', nose: 'broad', beard: { style: 'bristle', len: 0.8 }, mustache: 'thick', hat: { type: 'headband', color: '#2a2a2a' },
      outfit: { type: 'lamellar', metal: '#4a4a50', color: '#7a3a2a' }, expr: 'fierce' },
    '徐荣': { age: 42, eyes: 'sharp', brows: 'straight', beard: { style: 'short', len: 0.9 }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#7a808c' },
      outfit: { type: 'plate', metal: '#7a808c', color: '#2a4a5a' }, expr: 'stern' },
    '张辽': { age: 21, build: 1.08, face: { jaw: 1.08, len: 1.03 }, eyes: 'sharp', brows: { shape: 'sword', thick: 1.15 }, beard: { style: 'short', len: 0.8 }, mustache: 'thick',
      hat: { type: 'helmet', variant: 'wing', color: '#5a5e6a', plume: '#1a1a1a' }, outfit: { type: 'plate', metal: '#5a5e6a', color: '#3a2a5a' }, weapon: 'spear', expr: 'stern' },
    '高顺': { age: 36, face: { w: 0.97, jaw: 1.1 }, eyes: 'narrow', brows: 'straight', beard: { style: 'stubble' }, mustache: 'thin', hat: { type: 'helmet', variant: 'round', color: '#3c3c44', plume: '#1a1a1a' },
      outfit: { type: 'lamellar', metal: '#3c3c44', color: '#2a2a30' }, expr: 'stern' },
    '贾诩': { age: 43, face: { w: 0.94, len: 1.06, hollow: 0.55 }, eyes: { shape: 'narrow', size: 0.92 }, brows: 'thin', nose: 'aquiline', beard: { style: 'goatee', len: 1.2 }, mustache: 'thin',
      hat: { type: 'jinxian', color: '#1e1a1e' }, outfit: { type: 'robe', color: '#3a3a42', color2: '#1a1a22' }, expr: 'sly' },
    '胡轸': { age: 40, eyes: 'normal', brows: 'angled', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'plain', color: '#6a6e78' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#5a2a2a' } },
    '牛辅': { age: 34, fat: 0.4, eyes: 'worried', brows: 'worried', beard: { style: 'goatee', len: 0.6 }, mustache: 'thin', hat: { type: 'ze', color: '#3a2a20' },
      outfit: { type: 'lamellar', metal: '#8a7048', color: '#5a1a2a' }, expr: 'neutral' },

    // ------------------------------------------------------------- 袁绍 --
    '袁绍': { age: 40, face: { w: 0.98, len: 1.04 }, eyes: 'normal', brows: 'arched', nose: 'straight', beard: { style: 'long', len: 1.0 }, mustache: 'thin',
      hat: { type: 'crown', color: '#d8b050', gem: '#2a6db5' }, outfit: { type: 'plate', metal: '#c8a050', color: '#2f6db5', trim: '#f0d080', cape: 'faction' }, expr: 'calm' },
    '颜良': { age: 38, build: 1.18, face: { w: 1.05, jaw: 1.2 }, eyes: 'round', brows: 'bushy', nose: 'broad', beard: { style: 'full', len: 1.1 }, mustache: 'thick',
      hat: { type: 'helmet', variant: 'horn', color: '#5a5e68', plume: '#c0302a' }, outfit: { type: 'lamellar', metal: '#5a5e68', color: '#2f4a7a' }, weapon: 'guandao', expr: 'fierce' },
    '文丑': { age: 36, build: 1.18, face: { w: 1.04, jaw: 1.25, brow: 1.4, len: 1.06 }, eyes: 'sharp', brows: 'knit', nose: 'aquiline', beard: { style: 'bristle', len: 0.9 }, mustache: 'bristle',
      hat: { type: 'helmet', variant: 'spike', color: '#3c3c44', plume: '#1a1a1a' }, outfit: { type: 'lamellar', metal: '#3c3c44', color: '#7a2020' }, weapon: 'spear', expr: 'fierce', marks: ['scar'] },
    '田丰': { age: 55, face: { hollow: 0.6, len: 1.08 }, eyes: 'sharp', brows: { shape: 'straight', thick: 1.1 }, beard: { style: 'long', len: 1.1 }, mustache: 'droop',
      hat: { type: 'jinxian', liang: 2 }, outfit: { type: 'robe', color: '#2a3a4a', color2: '#1a1a22' }, expr: 'stern' },
    '沮授': { age: 46, eyes: 'normal', brows: 'straight', beard: { style: 'long', len: 0.9 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#3a4a3a', color2: '#1e2a1e' }, expr: 'calm' },
    '审配': { age: 44, face: { jaw: 1.12 }, eyes: 'narrow', brows: 'knit', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'jinxian' }, outfit: { type: 'robearmor', color: '#2a3a5a', metal: '#5a5e68' }, expr: 'stern' },
    '张郃': { age: 30, face: { w: 0.97, len: 1.05 }, eyes: 'sharp', brows: 'sword', beard: { style: 'goatee', len: 0.7 }, mustache: 'thin',
      hat: { type: 'helmet', variant: 'plain', color: '#7a808c', plume: '#2a4aa0' }, outfit: { type: 'plate', metal: '#7a808c', color: '#2a3a6a' }, weapon: 'spear', expr: 'stern' },
    '高览': { age: 34, build: 1.1, eyes: 'normal', brows: 'angled', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#6a6e78' },
      outfit: { type: 'lamellar', metal: '#6a6e78', color: '#2a4a6a' }, expr: 'stern' },
    '逢纪': { age: 45, eyes: 'narrow', brows: 'thin', nose: 'aquiline', beard: { style: 'goatee', len: 0.8 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#4a3a2a' }, expr: 'sly' },
    '郭图': { age: 44, face: { w: 0.95, chin: 0.9 }, eyes: 'sleepy', brows: 'arched', beard: { style: 'goatee', len: 0.9 }, mustache: 'thin', hat: { type: 'futou', color: '#3a3a3a' }, outfit: { type: 'robe', color: '#5a3a5a' }, expr: 'sly' },
    '许攸': { age: 44, face: { w: 0.94, hollow: 0.5 }, eyes: 'narrow', brows: 'thin', nose: 'aquiline', beard: { style: 'goatee', len: 1.0 }, mustache: 'curl', hat: { type: 'futou', color: '#6a5a2a' },
      outfit: { type: 'robe', color: '#8a6a2a', color2: '#4a3a1a' }, expr: 'sly' },
    '麴义': { age: 40, build: 1.08, eyes: 'sharp', brows: 'angled', beard: { style: 'short' }, mustache: 'droop', hat: { type: 'helmet', variant: 'round', color: '#7a6a48' },
      outfit: { type: 'lamellar', metal: '#7a6a48', color: '#5a3a2a' }, weapon: 'bow', expr: 'stern' },

    // ------------------------------------------------------------- 曹操 --
    '曹操': { age: 36, face: { w: 0.96, len: 0.98, cheek: 0.6 }, eyes: { shape: 'narrow', size: 0.95 }, brows: 'angled', beard: { style: 'long', len: 0.85 }, mustache: 'thin',
      hat: { type: 'jinxian', color: '#1a1a22' }, outfit: { type: 'plate', metal: '#5a5e6a', color: '#2c3d8f', trim: '#c8a050', cape: 'faction' }, weapon: 'sword', expr: 'sly' },
    '夏侯惇': { age: 34, build: 1.1, face: { jaw: 1.1 }, eyes: 'sharp', brows: 'knit', beard: { style: 'short' }, mustache: 'thick', acc: ['eyepatch'],
      hat: { type: 'helmet', color: '#4a5060', variant: 'wing' }, outfit: { type: 'plate', metal: '#4a5060', color: '#2c3d8f' }, weapon: 'spear', expr: 'fierce' },
    '夏侯渊': { age: 32, face: { w: 0.96, len: 1.05, hollow: 0.3 }, eyes: 'sharp', brows: 'sword', beard: { style: 'goatee', len: 0.9 }, mustache: 'curl',
      hat: { type: 'helmet', variant: 'spike', color: '#5a5e68', plume: '#2a4aa0' }, outfit: { type: 'lamellar', metal: '#5a5e68', color: '#2c3d8f' }, weapon: 'bow', expr: 'stern' },
    '曹仁': { age: 22, build: 1.12, face: { jaw: 1.15 }, eyes: 'normal', brows: 'bushy', beard: { style: 'full', len: 0.8 }, mustache: 'thick',
      hat: { type: 'helmet', variant: 'round', color: '#3c3c44' }, outfit: { type: 'plate', metal: '#3c3c44', color: '#2c3d8f' }, expr: 'stern' },
    '曹洪': { age: 30, fat: 0.35, eyes: 'round', brows: 'arched', nose: 'bulb', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'plain', color: '#a08048' },
      outfit: { type: 'lamellar', metal: '#a08048', color: '#2c3d8f' }, expr: 'smile' },
    '乐进': { age: 32, face: { w: 0.94, len: 0.94 }, build: 0.92, eyes: 'round', brows: 'angled', beard: { style: 'goatee' }, mustache: 'thick', hat: { type: 'ze', color: '#2a2226' },
      outfit: { type: 'lamellar', metal: '#7a808c', color: '#2c3d8f' }, expr: 'fierce' },
    '李典': { age: 28, eyes: 'gentle', brows: 'straight', beard: { style: 'goatee', len: 0.6 }, mustache: 'thin', hat: { type: 'wuguan' }, outfit: { type: 'robearmor', color: '#2c3d8f', metal: '#7a808c' }, expr: 'calm' },

    // ------------------------------------------------------------- 刘备 --
    '刘备': { age: 30, face: { w: 0.98, len: 1.02, jaw: 0.95 }, skin: '#f2d2b0', ears: { size: 1.45, lobe: 2.0 }, eyes: { shape: 'gentle', size: 1.04 }, brows: 'arched',
      mouth: { lips: 1.1 }, beard: { style: 'goatee', len: 0.8 }, mustache: 'thin', hat: { type: 'crown', color: '#d8b050', gem: '#2f9a55' },
      outfit: { type: 'robe', color: '#2a6a3a', color2: '#d8c890', cape: 'faction' }, weapon: 'swords', expr: 'calm' },
    '关羽': { age: 30, skin: '#b8523c', build: 1.12, neck: 1.12, face: { len: 1.08, jaw: 1.05 }, eyes: 'phoenix', brows: { shape: 'silkworm', thick: 1.2 },
      beard: { style: 'flowing', len: 1.1 }, mustache: 'droop', hat: { type: 'turban', color: '#2f7a3a' }, outfit: { type: 'robearmor', color: '#2f7a3a', color2: '#1e5a2a' }, weapon: 'guandao', expr: 'stern' },
    '张飞': { age: 26, skin: '#c99068', build: 1.18, neck: 1.25, face: { w: 1.06, jaw: 1.2, chin: 1.05, len: 0.96, brow: 1.2 }, eyes: { shape: 'round', size: 1.08, color: '#1a0e08' }, brows: { shape: 'bushy', thick: 1.45 },
      nose: 'broad', beard: { style: 'bristle', len: 1.1 }, mustache: 'bristle', hat: { type: 'ze', color: '#1a1a1e' }, outfit: { type: 'lamellar', metal: '#3c3e48', color: '#2a2a30' },
      weapon: 'snake', expr: 'fierce', hair: { locks: 2 } },
    '简雍': { age: 34, face: { w: 1.02, len: 0.98 }, eyes: 'sleepy', brows: 'arched', mouth: { w: 1.08 }, beard: { style: 'stubble' }, mustache: 'droop', hat: { type: 'futou', color: '#6a5a3a' },
      outfit: { type: 'robe', color: '#7a6a4a', color2: '#4a3a2a' }, expr: 'smile' },

    // ------------------------------------------------------------- 孙坚 --
    '孙坚': { age: 35, build: 1.12, face: { w: 1.05, jaw: 1.12, len: 0.98 }, eyes: 'sharp', brows: { shape: 'sword', thick: 1.2 }, beard: { style: 'short', len: 0.9 }, mustache: 'thick',
      hat: { type: 'ze', color: '#b02a24' }, outfit: { type: 'plate', metal: '#8a7048', color: '#c8382c', trim: '#e8c060', cape: 'faction' }, weapon: 'sword', expr: 'stern' },
    '孙策': { age: 22, face: { w: 0.98, len: 1.0 }, eyes: 'sharp', brows: 'sword', beard: 'none', mustache: 'none', hat: { type: 'headband', color: '#c8382c', gem: '#ffd060' },
      outfit: { type: 'plate', metal: '#8a7048', color: '#c8382c' }, weapon: 'spear', expr: 'smile', hair: { locks: 1 } },
    '程普': { age: 45, eyes: 'normal', brows: 'straight', beard: { style: 'long', len: 1.1, jaw: true }, mustache: 'droop', hat: { type: 'helmet', variant: 'plain', color: '#7a6a48', plume: '#c0302a' },
      outfit: { type: 'lamellar', metal: '#7a6a48', color: '#c8382c' }, weapon: 'snake', expr: 'stern' },
    '黄盖': { age: 48, build: 1.1, face: { jaw: 1.15, hollow: 0.3 }, eyes: 'round', brows: 'bushy', beard: { style: 'full', len: 1.0 }, mustache: 'thick',
      hat: { type: 'ze', color: '#7a2424' }, outfit: { type: 'lamellar', metal: '#5a5e68', color: '#c8382c' }, weapon: 'axe', expr: 'fierce' },
    '韩当': { age: 42, eyes: 'sharp', brows: 'angled', beard: { style: 'short' }, mustache: 'droop', hat: { type: 'helmet', variant: 'round', color: '#6a6e78' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#8a2a24' }, weapon: 'bow', expr: 'stern' },
    '祖茂': { age: 38, eyes: 'normal', brows: 'straight', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'ze', color: '#b02a24' }, outfit: { type: 'lamellar', metal: '#7a808c', color: '#8a2a24' }, expr: 'neutral' },
    '朱治': { age: 34, eyes: 'gentle', brows: 'straight', beard: { style: 'goatee' }, mustache: 'thin', hat: { type: 'wuguan' }, outfit: { type: 'robearmor', color: '#8a2a24', metal: '#7a808c' }, expr: 'calm' },

    // ------------------------------------------------------------- 袁术 --
    '袁术': { age: 38, fat: 0.3, face: { w: 1.0, chin: 0.9 }, eyes: 'sleepy', brows: 'arched', nose: 'straight', beard: { style: 'goatee', len: 0.8 }, mustache: 'curl',
      hat: { type: 'crown', color: '#e0c050', gem: '#c02a30', pearls: true }, outfit: { type: 'robe', color: '#c99a2e', color2: '#7a2a2a', cape: 'faction' }, expr: 'sly' },
    '纪灵': { age: 40, build: 1.18, face: { w: 1.06, jaw: 1.2 }, eyes: 'round', brows: 'bushy', beard: { style: 'full', len: 0.9 }, mustache: 'thick', hat: { type: 'helmet', variant: 'horn', color: '#8a7048' },
      outfit: { type: 'plate', metal: '#8a7048', color: '#c99a2e' }, weapon: 'halberd', expr: 'fierce' },
    '桥蕤': { age: 38, build: 1.08, eyes: 'normal', brows: 'straight', nose: 'broad', beard: { style: 'full', len: 0.7 }, mustache: 'droop', hat: { type: 'helmet', variant: 'plain', color: '#8a7048', plume: '#d8a020' },
      outfit: { type: 'lamellar', metal: '#8a7048', color: '#8a6a2a' }, weapon: 'spear', expr: 'stern' },
    '雷薄': { age: 34, build: 1.1, face: { w: 1.04, jaw: 1.15 }, eyes: 'narrow', brows: 'knit', beard: { style: 'stubble' }, mustache: 'thick', hat: { type: 'headband', color: '#a07a2e' },
      outfit: { type: 'lamellar', metal: '#5a5e68', color: '#6a4a2a' }, marks: ['scar'], expr: 'sly' },
    '张勋': { age: 40, eyes: 'normal', brows: 'angled', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#7a6a48' }, outfit: { type: 'lamellar', metal: '#7a6a48', color: '#a07a2e' } },
    '阎象': { age: 50, eyes: 'narrow', brows: 'straight', beard: { style: 'long', len: 1.1 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#4a3a2a' }, expr: 'stern' },
    '杨弘': { age: 44, eyes: 'normal', brows: 'thin', beard: { style: 'goatee' }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#6a5a2a' }, expr: 'calm' },

    // ----------------------------------------------------------- 公孙瓒 --
    '公孙瓒': { age: 38, face: { w: 0.97, len: 1.04 }, eyes: 'sharp', brows: 'sword', beard: { style: 'long', len: 0.9 }, mustache: 'thin',
      hat: { type: 'helmet', variant: 'wing', color: '#d4d8e0', plume: '#e8e8e8' }, outfit: { type: 'plate', metal: '#c8ccd6', color: '#e8e8f0', cape: '#f0f0f4' }, weapon: 'spear', expr: 'stern' },
    '赵云': { age: 23, face: { w: 1.0, jaw: 1.02, len: 1.0 }, eyes: { shape: 'normal', size: 1.06 }, brows: { shape: 'sword', thick: 1.2 }, beard: 'none', mustache: 'none',
      hat: { type: 'helmet', color: '#d4d8e0', plume: '#d03030' }, outfit: { type: 'plate', metal: '#c8ccd6', color: '#e8e8f0', trim: '#c8c8d8' }, weapon: 'spear', expr: 'neutral' },
    '严纲': { age: 40, eyes: 'normal', brows: 'straight', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#9aa0aa' }, outfit: { type: 'lamellar', metal: '#9aa0aa', color: '#d8d8e0' } },
    '田楷': { age: 42, eyes: 'gentle', brows: 'straight', beard: { style: 'goatee' }, mustache: 'thin', hat: { type: 'wuguan' }, outfit: { type: 'robearmor', color: '#c8c8d0', metal: '#7a808c' } },
    '公孙越': { age: 34, eyes: 'sharp', brows: 'sword', beard: { style: 'goatee', len: 0.6 }, mustache: 'thin', hat: { type: 'helmet', variant: 'plain', color: '#c8ccd6' }, outfit: { type: 'lamellar', metal: '#c8ccd6', color: '#e0e0e8' } },

    // ------------------------------------------------------------- 刘表 --
    '刘表': { age: 48, face: { w: 1.0, len: 1.06 }, eyes: 'gentle', brows: 'arched', beard: { style: 'long', len: 1.2 }, mustache: 'thin', hair: { color: '#5a5048' },
      hat: { type: 'jinxian', liang: 3 }, outfit: { type: 'robe', color: '#2a5a5a', color2: '#c8b080', cape: 'faction' }, expr: 'calm' },
    '蔡瑁': { age: 36, eyes: 'narrow', brows: 'angled', nose: 'aquiline', beard: { style: 'goatee', len: 0.9 }, mustache: 'curl', hat: { type: 'helmet', variant: 'round', color: '#5a5e68' },
      outfit: { type: 'robearmor', color: '#3aa5a0', metal: '#5a5e68' }, expr: 'sly' },
    '张允': { age: 34, eyes: 'normal', brows: 'thin', beard: { style: 'goatee' }, mustache: 'thin', hat: { type: 'ze' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#2a6a6a' }, expr: 'sly' },
    '蒯良': { age: 46, eyes: 'narrow', brows: 'straight', beard: { style: 'long', len: 1.0 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#3a4a4a' }, expr: 'calm' },
    '蒯越': { age: 44, face: { len: 1.06 }, eyes: 'sharp', brows: 'thin', beard: { style: 'forked', len: 0.9 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#2a3a3a', color2: '#5a5a3a' }, expr: 'sly' },
    '王威': { age: 42, face: { len: 1.06, hollow: 0.35 }, eyes: 'sharp', brows: 'straight', nose: 'aquiline', beard: { style: 'long', len: 0.9 }, mustache: 'thin',
      hat: { type: 'wuguan', color: '#2a2a30' }, outfit: { type: 'robearmor', color: '#2a5a5a', metal: '#6a6e78' }, expr: 'calm' },
    '文聘': { age: 30, build: 1.08, eyes: 'sharp', brows: 'straight', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'plain', color: '#7a808c', plume: '#2a8a8a' },
      outfit: { type: 'plate', metal: '#7a808c', color: '#3aa5a0' }, weapon: 'spear', expr: 'stern' },
    '黄祖': { age: 52, face: { hollow: 0.5 }, eyes: 'narrow', brows: 'bushy', beard: { style: 'full', len: 0.9 }, mustache: 'droop', hat: { type: 'helmet', variant: 'round', color: '#5a5e68' },
      outfit: { type: 'lamellar', metal: '#5a5e68', color: '#2a5a5a' }, weapon: 'bow', expr: 'stern' },

    // ------------------------------------------------------------- 刘焉 --
    '刘焉': { age: 58, face: { hollow: 0.5, len: 1.04 }, eyes: 'sleepy', brows: 'arched', beard: { style: 'long', len: 1.2 }, mustache: 'thin', hat: { type: 'jinxian', liang: 3 },
      outfit: { type: 'robe', color: '#4a2a6a', color2: '#c8b080', cape: 'faction' }, expr: 'calm' },
    '刘璋': { age: 28, fat: 0.45, face: { w: 1.02, chin: 0.88 }, eyes: { shape: 'gentle', size: 0.95 }, brows: 'worried', beard: { style: 'goatee', len: 0.6 }, mustache: 'thin',
      hat: { type: 'crown', color: '#d0b050', gem: '#8a5fc0' }, outfit: { type: 'robe', color: '#8a5fc0', color2: '#4a2a6a' }, expr: 'neutral' },
    '张任': { age: 34, face: { jaw: 1.08 }, eyes: 'sharp', brows: 'sword', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'wing', color: '#5a5e68', plume: '#8a5fc0' },
      outfit: { type: 'plate', metal: '#5a5e68', color: '#4a2a6a' }, weapon: 'spear', expr: 'stern' },
    '严颜': { age: 66, face: { hollow: 0.6 }, eyes: 'sharp', brows: { shape: 'bushy', thick: 1.2 }, beard: { style: 'long', len: 1.3, jaw: true }, mustache: 'droop',
      hat: { type: 'helmet', variant: 'plain', color: '#8a7048' }, outfit: { type: 'lamellar', metal: '#8a7048', color: '#6a3a8a' }, weapon: 'axe', expr: 'fierce' },
    '黄权': { age: 34, eyes: 'normal', brows: 'straight', beard: { style: 'goatee' }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#3a2a5a' }, expr: 'stern' },
    '张松': { age: 36, face: { w: 0.92, len: 0.92, chin: 0.8 }, build: 0.86, eyes: { shape: 'narrow', size: 0.88, gap: 0.03 }, brows: 'thin', nose: { shape: 'flat', size: 1.15 },
      mouth: { w: 1.1, y: 0.04 }, beard: { style: 'goatee', len: 0.6 }, mustache: 'thin', hat: { type: 'futou', color: '#4a3a2a' }, outfit: { type: 'robe', color: '#5a4a6a' }, expr: 'sly' },
    '吴懿': { age: 34, eyes: 'normal', brows: 'angled', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#6a6e78' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#5a3a7a' } },
    '冷苞': { age: 34, eyes: 'sharp', brows: 'knit', beard: { style: 'stubble' }, mustache: 'droop', hat: { type: 'headband', color: '#4a2a6a' }, outfit: { type: 'lamellar', metal: '#4a4a50', color: '#5a3a7a' }, expr: 'fierce' },

    // ------------------------------------------------------------- 马腾 --
    '马腾': { age: 44, build: 1.12, face: { w: 1.04, len: 1.06, cheek: 0.8 }, eyes: 'sharp', brows: 'bushy', nose: { shape: 'aquiline', size: 1.2 }, beard: { style: 'full', len: 1.1 }, mustache: 'thick',
      hat: { type: 'helmet', variant: 'spike', color: '#7a6a48', plume: '#e8e8e8' }, outfit: { type: 'lamellar', metal: '#7a6a48', color: '#c2702a', cape: 'faction' }, weapon: 'spear', expr: 'stern' },
    '马超': { age: 22, face: { w: 0.98, jaw: 1.0 }, skin: '#f2d4b4', eyes: { shape: 'sharp', size: 1.04 }, brows: 'sword', nose: 'straight', beard: 'none', mustache: 'none',
      hat: { type: 'helmet', color: '#d8dce4', variant: 'lion', plume: '#f0f0f0' }, outfit: { type: 'plate', metal: '#d0d4dc', color: '#e8e8ee' }, weapon: 'spear', expr: 'fierce' },
    '韩遂': { age: 50, face: { hollow: 0.5, len: 1.06 }, eyes: 'narrow', brows: 'bushy', nose: 'aquiline', beard: { style: 'long', len: 1.1, jaw: true }, mustache: 'droop',
      hat: { type: 'felt', color: '#7a5a3a', color2: '#4a3a2a' }, outfit: { type: 'kaftan', color: '#6a4a2a', color2: '#c8a050' }, expr: 'sly' },
    '庞德': { age: 32, build: 1.12, face: { jaw: 1.15 }, eyes: 'sharp', brows: 'knit', beard: { style: 'full', len: 0.8 }, mustache: 'thick',
      hat: { type: 'helmet', variant: 'round', color: '#5a5e68', plume: '#1a1a1a' }, outfit: { type: 'plate', metal: '#5a5e68', color: '#7a4a2a' }, weapon: 'guandao', expr: 'fierce' },
    '马岱': { age: 24, eyes: 'normal', brows: 'sword', beard: { style: 'goatee', len: 0.5 }, mustache: 'thin', hat: { type: 'helmet', variant: 'plain', color: '#c8ccd6' },
      outfit: { type: 'lamellar', metal: '#c8ccd6', color: '#c2702a' }, weapon: 'spear', expr: 'neutral' },
    '成宜': { age: 36, eyes: 'normal', brows: 'bushy', beard: { style: 'short' }, mustache: 'droop', hat: { type: 'felt', color: '#e0d4b8' }, outfit: { type: 'kaftan', color: '#7a5a3a' } },

    // ------------------------------------------------------------- 陶谦 --
    '陶谦': { age: 58, face: { hollow: 0.5 }, eyes: 'gentle', brows: 'worried', hair: { color: GRAY }, beard: { style: 'long', len: 1.2 }, mustache: 'droop', hat: { type: 'jinxian', liang: 3 },
      outfit: { type: 'robe', color: '#5a6a3a', color2: '#c8b080', cape: 'faction' }, expr: 'calm' },
    '糜竺': { age: 32, face: { w: 1.0 }, eyes: 'gentle', brows: 'arched', beard: { style: 'goatee', len: 0.7 }, mustache: 'thin', hat: { type: 'crown', color: '#d8b050', gem: '#2aa080' },
      outfit: { type: 'robe', color: '#7a5a2a', color2: '#c8a050' }, expr: 'smile' },
    '糜芳': { age: 28, eyes: 'worried', brows: 'worried', beard: { style: 'goatee', len: 0.6 }, mustache: 'thin', hat: { type: 'ze', color: '#5a4a3a' }, outfit: { type: 'lamellar', metal: '#8a7048', color: '#6f8f3a' }, expr: 'neutral' },
    '曹豹': { age: 40, fat: 0.4, eyes: 'normal', brows: 'bushy', beard: { style: 'full', len: 0.8 }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#6a6e78' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#6f8f3a' } },
    '陈登': { age: 27, face: { w: 0.96 }, eyes: 'sharp', brows: 'thin', beard: { style: 'goatee', len: 0.7 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#3a5a4a' }, expr: 'sly' },
    '臧霸': { age: 34, build: 1.12, eyes: 'round', brows: 'bushy', beard: { style: 'bristle', len: 0.8 }, mustache: 'thick', hat: { type: 'headband', color: '#2a2a2a' },
      outfit: { type: 'lamellar', metal: '#4a4a50', color: '#5a6a3a' }, weapon: 'axe', expr: 'fierce' },

    // --------------------------------------------------- 孔融 · 韩馥 · 张鲁 --
    '孔融': { age: 37, face: { len: 1.06 }, eyes: 'gentle', brows: 'arched', beard: { style: 'long', len: 1.2 }, mustache: 'thin', hat: { type: 'jinxian', liang: 3 },
      outfit: { type: 'robe', color: '#8a5a7a', color2: '#e8e0d0', cape: 'faction' }, expr: 'calm' },
    '武安国': { age: 34, build: 1.15, eyes: 'round', brows: 'bushy', nose: 'broad', beard: { style: 'full', len: 0.8 }, mustache: 'thick', hat: { type: 'headband', color: '#8a3a5a' },
      outfit: { type: 'lamellar', metal: '#5a5e68', color: '#b05a8a' }, weapon: 'axe', expr: 'fierce' },
    '王修': { age: 32, eyes: 'normal', brows: 'straight', beard: { style: 'goatee' }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#5a3a5a' }, expr: 'calm' },
    '韩馥': { age: 48, eyes: 'worried', brows: 'worried', beard: { style: 'long', len: 0.9 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#3a4a6a', cape: 'faction' }, expr: 'neutral' },
    '潘凤': { age: 36, build: 1.15, face: { jaw: 1.18 }, eyes: 'round', brows: 'angled', beard: { style: 'full', len: 1.0 }, mustache: 'thick', hat: { type: 'helmet', variant: 'horn', color: '#7a6a48' },
      outfit: { type: 'lamellar', metal: '#7a6a48', color: '#5a7a9a' }, weapon: 'axe', expr: 'fierce' },
    '耿武': { age: 40, eyes: 'normal', brows: 'straight', beard: { style: 'short' }, mustache: 'thin', hat: { type: 'wuguan' }, outfit: { type: 'robearmor', color: '#5a7a9a', metal: '#7a808c' } },
    '沮鹄': { age: 30, eyes: 'gentle', brows: 'thin', beard: { style: 'goatee', len: 0.5 }, mustache: 'thin', hat: { type: 'ze' }, outfit: { type: 'robe', color: '#4a5a7a' } },
    '张鲁': { age: 40, face: { len: 1.06 }, eyes: 'narrow', brows: 'thin', beard: { style: 'long', len: 1.2 }, mustache: 'thin', hat: { type: 'daoist', color: '#d8b04a' },
      outfit: { type: 'robe', color: '#c8a050', color2: '#2a2a2a', cape: 'faction' }, expr: 'calm' },
    '阎圃': { age: 40, eyes: 'narrow', brows: 'straight', beard: { style: 'goatee', len: 1.0 }, mustache: 'thin', hat: { type: 'futou', color: '#6a6050' }, outfit: { type: 'robe', color: '#8a7a4a' }, expr: 'calm' },
    '杨任': { age: 36, eyes: 'sharp', brows: 'angled', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#7a6a48' }, outfit: { type: 'lamellar', metal: '#7a6a48', color: '#a08a5a' } },
    '杨昂': { age: 36, eyes: 'normal', brows: 'bushy', beard: { style: 'stubble' }, mustache: 'droop', hat: { type: 'ze', color: '#6a5a2a' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#a08a5a' } },
    '张卫': { age: 34, eyes: 'normal', brows: 'straight', beard: { style: 'goatee' }, mustache: 'thick', hat: { type: 'headband', color: '#c8a030' }, outfit: { type: 'lamellar', metal: '#8a7048', color: '#a08a5a' } },

    // ----------------------------------------------- 张杨 · 刘繇 · 陆康 --
    '张杨': { age: 40, eyes: 'normal', brows: 'straight', beard: { style: 'short', len: 0.9 }, mustache: 'thick', hat: { type: 'helmet', variant: 'plain', color: '#7a808c' },
      outfit: { type: 'lamellar', metal: '#7a808c', color: '#7a6a8a', cape: 'faction' }, expr: 'neutral' },
    '眭固': { age: 34, eyes: 'round', brows: 'knit', beard: { style: 'bristle', len: 0.7 }, mustache: 'bristle', hat: { type: 'headband', color: '#2a2a2a' }, outfit: { type: 'lamellar', metal: '#4a4a50', color: '#5a4a5a' }, expr: 'fierce' },
    '杨丑': { age: 34, eyes: 'narrow', brows: 'angled', beard: { style: 'stubble' }, mustache: 'thin', hat: { type: 'ze', color: '#3a2a20' }, outfit: { type: 'lamellar', metal: '#5a5e68', color: '#6a5a7a' }, expr: 'sly' },
    '刘繇': { age: 34, eyes: 'gentle', brows: 'arched', beard: { style: 'goatee', len: 0.8 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#2a5a7a', color2: '#c8b080', cape: 'faction' }, expr: 'calm' },
    '太史慈': { age: 24, build: 1.12, face: { jaw: 1.08 }, eyes: 'sharp', brows: { shape: 'sword', thick: 1.15 }, beard: { style: 'full', len: 1.0 }, mustache: 'thick',
      hat: { type: 'helmet', variant: 'plain', color: '#7a808c', plume: '#c0302a' }, outfit: { type: 'plate', metal: '#7a808c', color: '#4a8aa8' }, weapon: 'bow', expr: 'stern' },
    '张英': { age: 38, eyes: 'normal', brows: 'bushy', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#5a5e68' }, outfit: { type: 'lamellar', metal: '#5a5e68', color: '#4a8aa8' } },
    '笮融': { age: 40, fat: 0.35, eyes: 'sleepy', brows: 'arched', hair: { style: 'bald' }, beard: 'stubble', mustache: 'none', hat: 'none',
      outfit: { type: 'robe', color: '#c8862a', color2: '#7a2a20' }, expr: 'sly' },
    '樊能': { age: 34, eyes: 'normal', brows: 'angled', beard: { style: 'short' }, mustache: 'droop', hat: { type: 'ze', color: '#2a3040' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#3a6a8a' } },
    '薛礼': { age: 40, eyes: 'narrow', brows: 'straight', beard: { style: 'goatee' }, mustache: 'thin', hat: { type: 'ze' }, outfit: { type: 'robearmor', color: '#3a6a8a', metal: '#7a808c' } },
    '陆康': { age: 64, face: { hollow: 0.6 }, eyes: 'gentle', brows: { shape: 'bushy', thick: 0.9 }, beard: { style: 'long', len: 1.3 }, mustache: 'droop', hat: { type: 'jinxian', liang: 2 },
      outfit: { type: 'robe', color: '#3a6a4a', color2: '#d8c890', cape: 'faction' }, expr: 'calm' },
    '陆绩': { age: 22, face: { w: 0.95 }, eyes: 'big', brows: 'thin', beard: 'none', mustache: 'none', hat: { type: 'futou', color: '#3a4a3a' }, outfit: { type: 'robe', color: '#5aa070', color2: '#2a5a3a' }, expr: 'smile' },

    // ------------------------------------------------------------- 孟获 --
    '孟获': { culture: 'nanman', age: 38, build: 1.18, neck: 1.25, face: { w: 1.06, jaw: 1.2 }, eyes: 'round', brows: 'bushy', nose: 'broad',
      beard: { style: 'full', len: 1.0 }, mustache: 'thick', hair: { style: 'mallet' }, hat: { type: 'chief' }, outfit: { type: 'chiefrobe' }, acc: ['hoops'], weapon: 'axe', expr: 'fierce' },
    '祝融': { culture: 'nanman', sex: 'f', age: 28, skin: '#c48e64', face: { w: 0.97 }, eyes: { shape: 'phoenix', size: 1.08 }, brows: { shape: 'angled', thick: 0.75 },
      hair: { style: 'mallet', tie: '#c03028' }, hat: { type: 'feathers' }, outfit: { type: 'pelt', pelt: 'leopard', color2: '#8a2a20' }, acc: ['knives', 'hoops', 'bones'], marks: ['warpaint'], weapon: 'spear', expr: 'fierce' },
    '孟优': { culture: 'nanman', age: 34, eyes: 'sharp', brows: 'bushy', beard: { style: 'short' }, mustache: 'droop', hair: { style: 'wild' }, hat: 'headband', outfit: { type: 'pelt', pelt: 'tiger' }, acc: ['bones'], expr: 'sly' },
    '兀突骨': { culture: 'nanman', age: 40, build: 1.22, neck: 1.4, skin: '#7a6a4a', face: { w: 1.1, jaw: 1.25, brow: 1.4 }, eyes: { shape: 'round', size: 0.9, color: '#6a5a10' }, brows: 'knit', nose: 'flat',
      beard: 'none', mustache: 'none', hair: { style: 'wild' }, hat: 'none', outfit: { type: 'rattan' }, marks: ['scales'], acc: ['bones'], expr: 'fierce' },
    '带来洞主': { culture: 'nanman', age: 30, eyes: 'sharp', brows: 'angled', beard: { style: 'stubble' }, mustache: 'thin', hair: { style: 'mallet' }, hat: 'feathers', outfit: { type: 'rattan' }, acc: ['hoops'], marks: ['warpaint'] },
    '沙摩柯': { culture: 'nanman', age: 36, build: 1.18, skin: '#b0583c', face: { jaw: 1.2 }, eyes: { shape: 'round', color: '#3a8a6a' }, brows: 'bushy', nose: 'broad',
      beard: { style: 'bristle' }, mustache: 'bristle', hair: { style: 'wild' }, hat: 'feathers', outfit: { type: 'pelt', pelt: 'tiger' }, acc: ['hoops', 'bones'], marks: ['warpaint'], weapon: 'axe', expr: 'fierce' },

    // ------------------------------------------------------- 在野 · 谋士 --
    '诸葛亮': { age: 27, face: { len: 1.06, w: 0.95 }, skin: '#f2d4b4', eyes: 'narrow', brows: 'thin', beard: { style: 'goatee', len: 0.7 }, mustache: 'thin', hat: { type: 'lunjin' },
      outfit: { type: 'robe', color: '#ece8dc', color2: '#2c3a58' }, acc: ['fan'], expr: 'calm' },
    '庞统': { age: 30, skin: '#b88a62', face: { w: 1.04, len: 0.95, jaw: 1.1, brow: 1.2 }, eyes: { shape: 'round', size: 0.92, gap: 0.03 }, brows: { shape: 'bushy', thick: 1.35 },
      nose: { shape: 'button', size: 1.25 }, beard: { style: 'short', len: 0.8 }, mustache: 'thick', hat: { type: 'futou', color: '#3a3a3a' }, outfit: { type: 'robe', color: '#5a4a3a', color2: '#2a2a2a' }, expr: 'sly' },
    '徐庶': { age: 30, face: { w: 0.97 }, eyes: 'sharp', brows: 'sword', beard: { style: 'goatee', len: 0.6 }, mustache: 'thin', hat: { type: 'futou', color: '#2a3a4a' },
      outfit: { type: 'robe', color: '#3a5a6a', color2: '#1a2a3a' }, weapon: 'sword', expr: 'calm' },
    '荀彧': { age: 28, skin: '#f2d4b4', face: { w: 0.96, len: 1.04 }, eyes: 'gentle', brows: 'arched', beard: { style: 'goatee', len: 0.6 }, mustache: 'thin', hat: { type: 'jinxian', liang: 2 },
      outfit: { type: 'robe', color: '#2a3a6a', color2: '#e8e0d0' }, expr: 'calm' },
    '郭嘉': { age: 22, skin: '#f0d8c0', face: { w: 0.93, len: 1.0, hollow: 0.35 }, eyes: { shape: 'sleepy', size: 1.02 }, brows: 'thin', beard: 'none', mustache: 'none', hat: { type: 'futou', color: '#2a2a3a' },
      outfit: { type: 'robe', color: '#5a4a6a', color2: '#2a2a3a' }, expr: 'sly', hair: { locks: 1 } },
    '荀攸': { age: 33, eyes: 'narrow', brows: 'straight', beard: { style: 'long', len: 0.9 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#3a3a4a' }, expr: 'calm' },
    '程昱': { age: 49, face: { len: 1.1, w: 0.97 }, eyes: 'sharp', brows: 'angled', beard: { style: 'long', len: 1.3, jaw: true }, mustache: 'droop', hat: { type: 'jinxian' },
      outfit: { type: 'robe', color: '#2a2a3a', color2: '#5a2a2a' }, expr: 'stern' },
    '司马懿': { age: 40, face: { w: 0.95, len: 1.08, cheek: 0.8, hollow: 0.4 }, eyes: { shape: 'sharp', size: 0.92 }, brows: { shape: 'angled', thick: 0.9 }, nose: 'aquiline',
      beard: { style: 'long', len: 1.1 }, mustache: 'thin', hat: { type: 'jinxian', color: '#14121a', liang: 3 }, outfit: { type: 'robe', color: '#1e2236', color2: '#5a1a2a' }, expr: 'sly' },
    '钟繇': { age: 39, eyes: 'gentle', brows: 'arched', beard: { style: 'long', len: 1.1 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#4a3a2a', color2: '#d8c890' }, expr: 'calm' },
    '姜维': { age: 28, face: { w: 0.99, jaw: 1.04 }, eyes: 'sharp', brows: 'sword', beard: { style: 'goatee', len: 0.5 }, mustache: 'thin',
      hat: { type: 'helmet', variant: 'wing', color: '#7a808c', plume: '#2f9a55' }, outfit: { type: 'plate', metal: '#7a808c', color: '#2f7a4a' }, weapon: 'spear', expr: 'stern' },
    '鲁肃': { age: 36, fat: 0.25, face: { w: 1.04, len: 0.96 }, eyes: 'gentle', brows: 'arched', beard: { style: 'short', len: 0.8 }, mustache: 'thin', hat: { type: 'jinxian' },
      outfit: { type: 'robe', color: '#7a3a2a', color2: '#d8c890' }, expr: 'smile' },
    '张昭': { age: 56, face: { len: 1.06, hollow: 0.5 }, eyes: 'sharp', brows: { shape: 'bushy', thick: 1.0 }, beard: { style: 'long', len: 1.3 }, mustache: 'droop', hat: { type: 'jinxian', liang: 3 },
      outfit: { type: 'robe', color: '#3a2a2a', color2: '#c8a050' }, expr: 'stern' },
    '陆逊': { age: 28, skin: '#f2d4b4', face: { w: 0.96, len: 1.02 }, eyes: 'normal', brows: 'sword', beard: 'none', mustache: 'thin', hat: { type: 'crown', color: '#d8b050', gem: '#c8382c' },
      outfit: { type: 'robearmor', color: '#c8382c', color2: '#7a1a1a', metal: '#8a7048' }, weapon: 'sword', expr: 'calm' },
    '马良': { age: 30, face: { w: 0.97 }, eyes: 'gentle', brows: { shape: 'arched', thick: 1.25, color: '#f0ece4' }, beard: { style: 'goatee', len: 0.6 }, mustache: 'thin',
      hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#2a5a3a', color2: '#e8e0d0' }, expr: 'calm' },
    '马谡': { age: 26, eyes: 'sharp', brows: 'angled', beard: { style: 'goatee', len: 0.5 }, mustache: 'thin', hat: { type: 'futou', color: '#2a3a2a' }, outfit: { type: 'robe', color: '#2f6a4a' }, expr: 'smile' },
    '法正': { age: 34, face: { w: 0.95, hollow: 0.4 }, eyes: 'narrow', brows: 'angled', nose: 'aquiline', beard: { style: 'goatee', len: 0.9 }, mustache: 'curl', hat: { type: 'jinxian' },
      outfit: { type: 'robe', color: '#4a2a3a', color2: '#1a1a22' }, expr: 'sly' },
    '孟达': { age: 32, eyes: 'narrow', brows: 'angled', beard: { style: 'short' }, mustache: 'curl', hat: { type: 'helmet', variant: 'round', color: '#6a6e78' }, outfit: { type: 'robearmor', color: '#5a3a5a', metal: '#6a6e78' }, expr: 'sly' },
    '邓艾': { age: 40, face: { len: 1.04, jaw: 1.06 }, eyes: 'narrow', brows: 'straight', beard: { style: 'short', len: 0.9 }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#5a5e68' },
      outfit: { type: 'plate', metal: '#5a5e68', color: '#2c3d8f' }, expr: 'stern' },
    '孙乾': { age: 36, eyes: 'gentle', brows: 'arched', beard: { style: 'long', len: 0.9 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#3a5a3a' }, expr: 'smile' },
    '蒋琬': { age: 34, eyes: 'normal', brows: 'straight', beard: { style: 'goatee', len: 0.8 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#2a4a3a' }, expr: 'calm' },
    '刘巴': { age: 32, face: { hollow: 0.4 }, eyes: 'narrow', brows: 'thin', beard: { style: 'goatee', len: 0.7 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#3a3a5a' }, expr: 'stern' },
    '华歆': { age: 33, eyes: 'narrow', brows: 'thin', beard: { style: 'goatee', len: 0.9 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#2a2a3a' }, expr: 'sly' },
    '王朗': { age: 62, face: { hollow: 0.5, len: 1.06 }, eyes: 'sleepy', brows: 'bushy', beard: { style: 'long', len: 1.5 }, mustache: 'droop', hat: { type: 'jinxian', liang: 3 },
      outfit: { type: 'robe', color: '#5a4a3a', color2: '#d8c890' }, expr: 'calm' },
    '虞翻': { age: 26, eyes: 'sharp', brows: 'knit', beard: { style: 'short' }, mustache: 'thin', hat: { type: 'futou', color: '#2a3040' }, outfit: { type: 'robe', color: '#3a4a5a' }, expr: 'stern' },
    '满宠': { age: 40, face: { len: 1.06 }, eyes: 'narrow', brows: 'knit', beard: { style: 'short', len: 0.8 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#2a2a2a', color2: '#5a1a1a' }, expr: 'stern' },
    '华佗': { age: 62, face: { hollow: 0.6, len: 1.04 }, eyes: 'gentle', brows: { shape: 'bushy', thick: 0.9 }, hair: { color: '#ece8e0' }, beard: { style: 'long', len: 1.4 }, mustache: 'droop',
      hat: { type: 'futou', color: '#6a5a3a' }, outfit: { type: 'robe', color: '#7a6a4a', color2: '#3a5a3a' }, expr: 'smile' },

    // ------------------------------------------------------- 在野 · 武将 --
    '典韦': { age: 34, build: 1.22, neck: 1.4, skin: '#a87a58', face: { w: 1.1, jaw: 1.25, brow: 1.4, len: 0.98 }, eyes: { shape: 'round', size: 0.9 }, brows: { shape: 'knit', thick: 1.45 },
      nose: 'broad', mouth: { w: 1.12 }, beard: { style: 'bristle', len: 1.0 }, mustache: 'bristle', hat: { type: 'headband', color: '#2a2a2a' },
      outfit: { type: 'lamellar', metal: '#3c3c44', color: '#5a1a1a' }, weapon: 'ji', expr: 'fierce', hair: { locks: 2 }, marks: ['scar'] },
    '许褚': { age: 30, fat: 0.55, build: 1.22, neck: 1.45, face: { w: 1.08, jaw: 1.2 }, eyes: { shape: 'round', size: 0.88 }, brows: 'bushy', nose: 'broad', beard: { style: 'short', len: 0.7 }, mustache: 'thick',
      hat: { type: 'headband', color: '#2c3d8f' }, outfit: { type: 'bare' }, weapon: 'axe', expr: 'fierce' },
    '于禁': { age: 34, face: { len: 1.04 }, eyes: 'narrow', brows: 'straight', beard: { style: 'short', len: 0.8 }, mustache: 'thick', hat: { type: 'helmet', variant: 'plain', color: '#5a5e68' },
      outfit: { type: 'lamellar', metal: '#5a5e68', color: '#2c3d8f' }, expr: 'stern' },
    '徐晃': { age: 32, build: 1.14, face: { jaw: 1.15 }, eyes: 'sharp', brows: 'bushy', beard: { style: 'full', len: 0.9 }, mustache: 'thick', hat: { type: 'helmet', variant: 'spike', color: '#4a4e58' },
      outfit: { type: 'plate', metal: '#4a4e58', color: '#2c3d8f' }, weapon: 'axe', expr: 'fierce' },
    '周瑜': { age: 26, skin: '#f2d6b8', face: { w: 0.96, len: 1.02, jaw: 0.95 }, eyes: { shape: 'phoenix', size: 1.04 }, brows: 'sword', mouth: { lips: 1.05 }, beard: 'none', mustache: 'none',
      hat: { type: 'crown', color: '#d8b050', gem: '#c8382c' }, outfit: { type: 'robearmor', color: '#c8382c', color2: '#f0e0c0', metal: '#c8ccd6', cape: '#e8e0d0' }, weapon: 'sword', expr: 'smile' },
    '吕蒙': { age: 30, build: 1.1, face: { jaw: 1.12 }, eyes: 'sharp', brows: 'angled', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#8a7048' },
      outfit: { type: 'plate', metal: '#8a7048', color: '#c8382c' }, weapon: 'sword', expr: 'stern' },
    '周泰': { age: 32, build: 1.14, skin: '#c99068', face: { jaw: 1.15 }, eyes: 'narrow', brows: 'knit', beard: { style: 'stubble' }, mustache: 'droop', hat: { type: 'ze', color: '#7a2424' },
      outfit: { type: 'lamellar', metal: '#4a4a50', color: '#c8382c' }, marks: ['scars'], weapon: 'sword', expr: 'stern' },
    '蒋钦': { age: 32, eyes: 'normal', brows: 'bushy', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'ze', color: '#3a2a20' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#a83a2a' } },
    '甘宁': { age: 30, build: 1.1, skin: '#c99068', eyes: 'sharp', brows: 'angled', beard: { style: 'goatee', len: 0.7 }, mustache: 'curl', hair: { locks: 2 },
      hat: { type: 'feathers', color: '#c8382c', colors: ['#d8a020', '#c03028', '#ece4d4'] }, outfit: { type: 'lamellar', metal: '#8a7048', color: '#d8a020' }, acc: ['earring'], weapon: 'ji', expr: 'smile' },
    '凌统': { age: 20, eyes: 'normal', brows: 'sword', beard: 'none', mustache: 'none', hat: { type: 'headband', color: '#c8382c' }, outfit: { type: 'lamellar', metal: '#7a808c', color: '#c8382c' }, weapon: 'spear', expr: 'stern' },
    '黄忠': { age: 62, face: { hollow: 0.6 }, eyes: 'sharp', brows: { shape: 'bushy', thick: 1.1 }, hair: { color: W }, beard: { style: 'long', len: 1.2, jaw: true }, mustache: 'droop',
      hat: { type: 'helmet', color: '#a08048' }, outfit: { type: 'lamellar', metal: '#8a7048', color: '#a03030' }, weapon: 'bow', expr: 'stern' },
    '魏延': { age: 34, skin: '#b8603e', build: 1.12, face: { jaw: 1.15, brow: 1.2 }, eyes: 'sharp', brows: { shape: 'knit', thick: 1.25 }, nose: 'aquiline', beard: { style: 'short', len: 0.9 }, mustache: 'thick',
      hat: { type: 'helmet', variant: 'horn', color: '#4a4e58', plume: '#c0302a' }, outfit: { type: 'plate', metal: '#4a4e58', color: '#7a2020' }, weapon: 'guandao', expr: 'fierce' },
    '王平': { age: 30, eyes: 'normal', brows: 'straight', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#6a6e78' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#3a5a3a' }, expr: 'stern' },
    '关平': { age: 20, skin: '#e0a888', face: { len: 1.04 }, eyes: 'phoenix', brows: 'sword', beard: 'none', mustache: 'none', hat: { type: 'turban', color: '#2f7a3a' },
      outfit: { type: 'lamellar', metal: '#7a808c', color: '#2f7a3a' }, weapon: 'spear', expr: 'stern' },
    '周仓': { age: 32, skin: '#7a5a42', build: 1.18, face: { w: 1.06, jaw: 1.2 }, eyes: 'round', brows: 'bushy', nose: 'broad', beard: { style: 'bristle', len: 1.1 }, mustache: 'bristle',
      hat: { type: 'headband', color: '#2f7a3a' }, outfit: { type: 'lamellar', metal: '#3c3c44', color: '#2a4a2a' }, weapon: 'guandao', expr: 'fierce', hair: { locks: 2 } },
    '张绣': { age: 26, eyes: 'sharp', brows: 'sword', beard: { style: 'goatee', len: 0.6 }, mustache: 'thin', hat: { type: 'helmet', variant: 'spike', color: '#7a808c' }, outfit: { type: 'plate', metal: '#7a808c', color: '#5a3a2a' }, weapon: 'spear', expr: 'stern' },
    '霍峻': { age: 30, eyes: 'normal', brows: 'straight', beard: { style: 'short' }, mustache: 'thin', hat: { type: 'helmet', variant: 'round', color: '#7a808c' }, outfit: { type: 'lamellar', metal: '#7a808c', color: '#2a6a3a' } },
    '赵范': { age: 38, eyes: 'narrow', brows: 'thin', beard: { style: 'goatee' }, mustache: 'curl', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#6a5a3a' }, expr: 'sly' },
    '金旋': { age: 40, eyes: 'normal', brows: 'straight', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#a08048' }, outfit: { type: 'lamellar', metal: '#a08048', color: '#6a3a2a' } },
    '刘度': { age: 50, eyes: 'worried', brows: 'worried', beard: { style: 'long', len: 1.0 }, mustache: 'thin', hat: { type: 'jinxian' }, outfit: { type: 'robe', color: '#4a4a3a' } },
    '严白虎': { age: 40, build: 1.12, eyes: 'round', brows: 'bushy', beard: { style: 'full', len: 0.9 }, mustache: 'thick', hat: { type: 'headband', color: '#d8a020' },
      outfit: { type: 'pelt', pelt: 'tiger' }, weapon: 'axe', expr: 'fierce' },
    '张燕': { age: 30, skin: '#b88a62', eyes: 'sharp', brows: 'angled', beard: { style: 'stubble' }, mustache: 'thin', hat: { type: 'headband', color: '#1a1a1a' }, hair: { locks: 2 },
      outfit: { type: 'lamellar', metal: '#3c3c44', color: '#1a1a1a' }, weapon: 'spear', expr: 'sly' },
    '鲁宗': { age: 36, eyes: 'normal', brows: 'bushy', beard: { style: 'full', len: 0.7 }, mustache: 'thick', hat: { type: 'turban', color: '#5a4a3a' }, outfit: { type: 'lamellar', metal: '#6a6e78', color: '#5a4a3a' } },

    // ------------------------------------------- 剧本外的名人（供后续剧本 / 测试） --
    '孙权': { age: 18, face: { w: 1.02, jaw: 1.12, len: 1.0 }, eyes: { shape: 'sharp', color: '#3a7a6a' }, brows: 'straight', beard: { style: 'short', len: 0.9, color: '#6a2a4a' }, mustache: 'thick',
      hair: { color: '#3a1e2a' }, hat: { type: 'crown', color: '#d8b050', gem: '#c8382c' }, outfit: { type: 'robe', color: '#c8382c', color2: '#e8c060', cape: 'faction' }, expr: 'calm' },
    '貂蝉': { sex: 'f', age: 18, skin: '#f6dcc6', eyes: { shape: 'big', size: 1.1 }, brows: 'thin', hair: { style: 'bun', flower: '#f07090' }, outfit: { type: 'robe', color: '#d86a8a', color2: '#f4e0e8', inner: '#ffffff' }, expr: 'smile' },
    '孙尚香': { sex: 'f', age: 20, eyes: { shape: 'phoenix', size: 1.06 }, brows: 'angled', hair: { style: 'twinbun' }, outfit: { type: 'lamellar', metal: '#c8a050', color: '#c8382c' }, weapon: 'bow', expr: 'smile' },
    '卑弥呼': { culture: 'wa', sex: 'f', age: 42, eyes: { shape: 'narrow', size: 1.0 }, brows: 'thin', hair: { style: 'wahair' }, hat: { type: 'shaman' },
      outfit: { type: 'kantoui', color: '#ece6d8', color2: '#b0302a' }, acc: ['magatama', 'mirror'], expr: 'calm' },
  });
})();

/* ==========================================================================
   第二阶段：世界剧本「天下大势」新增武将的外貌（DESIGN-V2 §4G）
   (1) 自动：凡 SG.WorldInfo 中的新增武将（world-data.js 先于本文件载入），按资料补上
       culture（文化）、sex（女性为 'f'）、role（世界剧本诸势力的君主为 'ruler'）与画像年龄
       （生年可考者取 190 年时的年龄，限制在 22–72 岁；年幼者按成年后的样子画）。
       这样即使游戏里的武将对象没有 culture / sex / born 字段，头像也按正确的文化与性别生成。
   (2) 手工：知名人物的个别设定（写在 (1) 之上，同名字段以手工为准）。
   SG.PortraitData.worldHand → 有手工条目的世界武将名（tests/world-portraits.html 用）。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const PD = SG.PortraitData;
  const W = '#ece8e0', GRAY = '#b8b4ac';
  const HAND = {};
  // 按文化分组书写；每条自动带上该文化代号
  function group(culture, map) { for (const k in map) HAND[k] = Object.assign({ culture }, map[k]); }

  // ------------------------------------------------------------- 罗马 --
  group('roman', {
    '康茂德': { role: 'ruler', age: 29, build: 1.12, face: { jaw: 1.06 }, eyes: { shape: 'sleepy', color: '#4a5a6a' }, brows: 'arched', hair: { style: 'curly', color: '#8a5a2a', vol: 1.1 },
      beard: { style: 'short', len: 0.8, color: '#8a5a2a' }, mustache: 'thin', hat: { type: 'helmet', variant: 'lion', color: '#c89a48', plume: '#a02a24' },
      outfit: { type: 'pelt', pelt: 'leopard' }, expr: 'sly' },
    '塞维鲁': { role: 'ruler', age: 45, skin: '#d8a882', face: { len: 1.06, cheek: 0.5 }, eyes: { shape: 'sharp', color: '#2a1a10' }, brows: { shape: 'knit', thick: 1.1 }, nose: 'aquiline',
      hair: { style: 'curly', color: '#2a2018', vol: 1.15 }, beard: { style: 'forked', len: 1.25, color: '#3a2e24', jaw: true }, mustache: 'thick',
      hat: { type: 'laurel' }, outfit: { type: 'musculata', metal: '#b08a50', color: '#8a1a24', cape: 'faction' }, weapon: 'sword', expr: 'stern' },
    '多姆娜': { sex: 'f', age: 30, skin: '#e2b48e', eyes: { shape: 'big', color: '#2a1a10', size: 1.08 }, brows: 'arched', nose: 'straight',
      hair: { style: 'romanf', color: '#1e1614', vol: 1.2 }, hat: { type: 'diadem', color: '#d8b050', gem: '#7a2a8a' }, outfit: { type: 'tunic', color: '#6a2a6a', color2: '#d8b050' },
      acc: ['earring'], expr: 'calm' },
    '玛琪亚': { sex: 'f', age: 27, skin: '#f2d6be', eyes: { shape: 'big', color: '#3a5a4a' }, brows: 'thin', hair: { style: 'romanf', color: '#8a3a1e' },
      outfit: { type: 'tunic', color: '#a8304a', color2: '#e8d8b0' }, acc: ['earring'], expr: 'sly' },
    '玛伊莎': { sex: 'f', age: 36, skin: '#ddae88', eyes: { shape: 'narrow', color: '#2a1a10' }, brows: 'arched', nose: 'aquiline', hair: { style: 'romanf', color: '#221816' },
      hat: { type: 'diadem', color: '#c8a040', gem: '#2a6a5a' }, outfit: { type: 'tunic', color: '#2a4a5a', color2: '#c8a050' }, acc: ['earring'], expr: 'sly' },
    '佩蒂纳克斯': { age: 64, face: { len: 1.04, hollow: 0.3 }, eyes: 'gentle', brows: { shape: 'straight', color: GRAY }, hair: { style: 'curly', color: GRAY, recede: 0.4 },
      beard: { style: 'full', len: 1.25, color: W }, mustache: 'thick', hat: 'none', outfit: { type: 'toga', color: '#ece6d8', color2: '#7a2a5a' }, expr: 'calm' },
    '尼格尔': { age: 55, face: { jaw: 1.08 }, eyes: 'sharp', brows: 'straight', hair: { style: 'short', color: '#4a3a2a' }, beard: { style: 'short', len: 0.9, color: '#5a4a3a' }, mustache: 'thick',
      hat: { type: 'galea', color: '#9aa0aa', plume: '#1a1a1a' }, outfit: { type: 'musculata', metal: '#9aa0aa', color: '#2a2a3a', cape: 'faction' }, weapon: 'sword', expr: 'stern' },
    '阿尔拜努斯': { age: 40, skin: '#f8e6d6', eyes: { shape: 'normal', color: '#5a7a8a' }, brows: 'arched', hair: { style: 'curly', color: '#a8844a' }, beard: { style: 'short', len: 0.8, color: '#a8844a' },
      mustache: 'thin', hat: 'none', outfit: { type: 'musculata', metal: '#c8b070', color: '#e8e0d0', cape: 'faction' }, expr: 'calm' },
    '卡拉卡拉': { age: 24, build: 1.1, neck: 1.25, face: { jaw: 1.12, brow: 1.3 }, eyes: { shape: 'sharp', size: 0.92 }, brows: { shape: 'knit', thick: 1.3 },
      hair: { style: 'curly', color: '#2a1e16', vol: 0.85 }, beard: { style: 'stubble' }, mustache: 'none', hat: 'none',
      outfit: { type: 'musculata', metal: '#8a7048', color: '#8a1a1a', cape: '#5a1010' }, weapon: 'sword', expr: 'fierce' },
    '盖塔': { age: 21, face: { w: 0.97, len: 1.04 }, eyes: 'gentle', brows: 'arched', hair: { style: 'curly', color: '#3a2a1c' }, beard: { style: 'stubble' }, mustache: 'none', hat: 'none',
      outfit: { type: 'toga', color: '#ece6d8', color2: '#a02a24' }, expr: 'neutral' },
    '盖伦': { age: 61, face: { len: 1.06, hollow: 0.4 }, eyes: 'gentle', brows: { shape: 'thin', color: GRAY }, hair: { style: 'short', color: GRAY, recede: 0.7 },
      beard: { style: 'long', len: 1.2, color: W }, mustache: 'droop', hat: 'none', outfit: { type: 'toga', color: '#e8e0cc', color2: '#5a7a5a' }, expr: 'calm' },
    '帕比尼安': { age: 48, eyes: 'narrow', brows: 'straight', hair: { style: 'short', color: '#3a2a1c', recede: 0.3 }, beard: { style: 'short', len: 0.7 }, mustache: 'thin', hat: 'none',
      outfit: { type: 'toga', color: '#ece6d8', color2: '#2a3a6a' }, expr: 'stern' },
    '乌尔比安': { age: 40, skin: '#e2b896', eyes: 'normal', brows: 'straight', hair: { style: 'curly', color: '#1e1614' }, beard: { style: 'short', len: 0.8 }, mustache: 'thick', hat: 'none',
      outfit: { type: 'toga', color: '#ece6d8', color2: '#6a2a2a' }, expr: 'calm' },
    '狄奥': { age: 35, eyes: 'normal', brows: 'arched', nose: 'aquiline', hair: { style: 'curly', color: '#4a3220' }, beard: { style: 'short', len: 0.9 }, mustache: 'thick', hat: 'none',
      outfit: { type: 'toga', color: '#e8e0d0', color2: '#7a2a5a' }, expr: 'calm' },
    '马克西明': { age: 30, build: 1.22, neck: 1.45, face: { w: 1.06, jaw: 1.25, chin: 1.2, len: 1.1, brow: 1.4 }, eyes: { shape: 'narrow', size: 0.88 }, brows: { shape: 'bushy', thick: 1.3 },
      nose: 'bulb', hair: { style: 'short', color: '#4a3a2a' }, beard: { style: 'stubble' }, mustache: 'none', hat: { type: 'galea', color: '#7a808c', plume: '#a02a24' },
      outfit: { type: 'segmentata', metal: '#7a808c', color: '#8a2a24' }, weapon: 'spear', expr: 'fierce' },
    '纳尔奇苏斯': { age: 26, build: 1.2, neck: 1.4, face: { jaw: 1.18 }, eyes: 'round', brows: 'bushy', nose: 'broad', hair: { style: 'short', color: '#2a1e16' }, beard: 'stubble', mustache: 'none',
      hat: 'none', outfit: { type: 'bare' }, expr: 'fierce', marks: ['scar'] },
    '克里安德': { age: 45, skin: '#e8c4a2', face: { w: 0.95, hollow: 0.4 }, eyes: 'narrow', brows: 'thin', nose: 'aquiline', hair: { style: 'short', color: '#3a2a1c' }, beard: 'none', mustache: 'none',
      hat: { type: 'phrygian', color: '#7a2a5a' }, outfit: { type: 'tunic', color: '#c8a050', color2: '#5a2a4a' }, acc: ['earring'], expr: 'sly' },
    '莱图斯': { age: 50, eyes: 'narrow', brows: 'knit', hair: { style: 'short', color: '#3a2a1c', recede: 0.3 }, beard: { style: 'short', len: 0.7 }, mustache: 'thin',
      hat: { type: 'galea', color: '#c8a050', plume: '#e8e0d0' }, outfit: { type: 'musculata', metal: '#c8a050', color: '#2a2a5a' }, expr: 'sly' },
    '埃克莱图斯': { age: 50, eyes: 'worried', brows: 'worried', hair: { style: 'short', color: GRAY, recede: 0.5 }, beard: 'none', mustache: 'none', hat: 'none',
      outfit: { type: 'tunic', color: '#5a4a3a' }, expr: 'neutral' },
    '尤·莱图斯': { age: 35, build: 1.12, eyes: 'sharp', brows: 'angled', hair: { style: 'short', color: '#2a1e16' }, beard: { style: 'short', len: 0.7 }, mustache: 'thick',
      hat: { type: 'galea', color: '#9aa0aa', plume: '#c0302a' }, outfit: { type: 'musculata', metal: '#9aa0aa', color: '#8a2a24' }, weapon: 'spear', expr: 'fierce' },
    '阿努利努斯': { age: 58, face: { hollow: 0.3 }, eyes: 'sharp', brows: { shape: 'straight', color: GRAY }, hair: { style: 'short', color: GRAY }, beard: { style: 'short', color: GRAY }, mustache: 'thick',
      hat: { type: 'galea', color: '#b08a50', plume: '#1a1a1a' }, outfit: { type: 'musculata', metal: '#b08a50', color: '#2a3a5a' }, expr: 'stern' },
    '尤利安努斯': { age: 57, fat: 0.45, eyes: 'sleepy', brows: 'arched', hair: { style: 'short', color: GRAY, recede: 0.6 }, beard: { style: 'short', len: 0.6, color: GRAY }, mustache: 'thin', hat: 'none',
      outfit: { type: 'toga', color: '#ece6d8', color2: '#6a2a5a' }, expr: 'sly' },
    '马克里努斯': { age: 45, skin: '#c8966a', eyes: 'narrow', brows: 'straight', hair: { style: 'curly', color: '#1e1614' }, beard: { style: 'full', len: 1.0 }, mustache: 'thick', hat: 'none',
      outfit: { type: 'toga', color: '#ece6d8', color2: '#2a5a3a' }, acc: ['earring'], expr: 'sly' },
    '普劳提安': { age: 40, skin: '#d8a882', eyes: 'narrow', brows: 'angled', hair: { style: 'short', color: '#1e1614' }, beard: { style: 'short', len: 0.6 }, mustache: 'thin',
      hat: { type: 'galea', color: '#c8a050', plume: '#5a2a6a' }, outfit: { type: 'musculata', metal: '#c8a050', color: '#5a2a6a' }, expr: 'sly' },
    '庞培亚努斯': { age: 65, skin: '#ddb48e', face: { hollow: 0.4 }, eyes: 'gentle', brows: { shape: 'bushy', color: GRAY }, hair: { style: 'short', color: W, recede: 0.5 },
      beard: { style: 'full', len: 1.0, color: W }, mustache: 'thick', hat: 'none', outfit: { type: 'musculata', metal: '#9aa0aa', color: '#2a3a5a', cape: 'faction' }, expr: 'calm' },
    '戈尔迪安': { age: 66, fat: 0.35, eyes: 'gentle', brows: { shape: 'arched', color: GRAY }, hair: { style: 'short', color: W, recede: 0.6 }, beard: { style: 'short', len: 0.7, color: W }, mustache: 'thin',
      hat: 'none', outfit: { type: 'toga', color: '#ece6d8', color2: '#7a2a5a' }, expr: 'smile' },
    '德西乌斯': { age: 48, face: { len: 1.06, hollow: 0.5 }, eyes: 'worried', brows: { shape: 'worried', thick: 1.1 }, hair: { style: 'short', color: '#3a2a1c', recede: 0.4 }, beard: { style: 'stubble' },
      mustache: 'none', hat: { type: 'galea', color: '#7a808c', plume: '#c0302a' }, outfit: { type: 'segmentata', metal: '#7a808c', color: '#8a2a24' }, expr: 'stern' },
    '瓦勒良': { age: 55, eyes: 'normal', brows: 'straight', hair: { style: 'short', color: '#6a5a4a' }, beard: { style: 'short', len: 0.8, color: '#6a5a4a' }, mustache: 'thick',
      hat: { type: 'laurel' }, outfit: { type: 'musculata', metal: '#b08a50', color: '#6a2a5a' }, expr: 'calm' },
    '普登斯': { age: 42, build: 1.08, eyes: 'normal', brows: 'straight', beard: { style: 'short', len: 0.7 }, mustache: 'thick',
      hat: { type: 'galea', color: '#9aa0aa', plume: '#c0302a' }, outfit: { type: 'segmentata', metal: '#9aa0aa', color: '#a02a24' }, weapon: 'spear', expr: 'stern' },
    '卡斯图斯': { age: 40, eyes: 'sharp', brows: 'sword', hair: { style: 'short', color: '#6a4a2a' }, beard: { style: 'short', len: 0.7 }, mustache: 'thick',
      hat: { type: 'galea', color: '#7a808c', plume: '#2a3a6a' }, outfit: { type: 'segmentata', metal: '#7a808c', color: '#2a3a6a' }, weapon: 'sword', expr: 'stern' },
  });

  // ---------------------------------------------- 安息、波西斯、亚美尼亚 --
  group('persia', {
    '沃洛吉斯': { role: 'ruler', age: 62, face: { len: 1.06 }, eyes: 'sharp', brows: { shape: 'bushy', color: GRAY }, nose: 'aquiline', hair: { style: 'bushy', color: '#6a6460' },
      beard: { style: 'long', len: 1.35, color: '#7a7470' }, mustache: 'thick', hat: { type: 'tiara', color: '#d8b050', color2: '#2a3a6a', gem: '#c8382c' },
      outfit: { type: 'tunic', color: '#7a2a5a', color2: '#d8b050' }, acc: ['torc', 'earring'], weapon: 'bow', expr: 'stern' },
    '阿尔达希尔': { role: 'ruler', age: 44, build: 1.1, face: { jaw: 1.08, len: 1.04 }, eyes: { shape: 'sharp', size: 1.04 }, brows: { shape: 'arched', thick: 1.2 }, nose: 'aquiline',
      hair: { style: 'curly', color: '#16131a', vol: 1.3 }, beard: { style: 'curled', len: 1.3 }, mustache: 'curl',
      hat: { type: 'tiara', color: '#6a2a7a', color2: '#d8b050', gem: '#e8e0d0' }, outfit: { type: 'scale', metal: '#c8a040', color: '#5a2a6a', cape: 'faction' }, acc: ['earring'], weapon: 'sword', expr: 'stern' },
    '阿尔达班': { age: 42, eyes: 'sharp', brows: 'knit', nose: 'aquiline', hair: { style: 'bushy', color: '#221a18' }, beard: { style: 'long', len: 1.2 }, mustache: 'thick',
      hat: { type: 'tiara', color: '#b89048', color2: '#2a4a3a' }, outfit: { type: 'scale', metal: '#7a808c', color: '#2a4a3a' }, weapon: 'bow', expr: 'fierce' },
    '瓦加尔什': { age: 38, eyes: 'normal', brows: 'straight', hair: { style: 'bushy', color: '#221a18' }, beard: { style: 'full', len: 1.0 }, mustache: 'curl',
      hat: { type: 'diadem', color: '#d8b050' }, outfit: { type: 'scale', metal: '#8a7048', color: '#8a2a24' }, weapon: 'spear', expr: 'stern' },
    '帕佩克': { age: 52, face: { hollow: 0.4 }, eyes: 'narrow', brows: 'angled', nose: 'aquiline', hair: { style: 'bushy', color: '#4a4440' }, beard: { style: 'forked', len: 1.2, color: '#4a4440' },
      mustache: 'thick', hat: { type: 'phrygian', color: '#a02a24' }, outfit: { type: 'tunic', color: '#5a2a2a', color2: '#c8a040' }, expr: 'sly' },
    '萨珊': { age: 72, face: { hollow: 0.5, len: 1.06 }, eyes: 'sleepy', brows: { shape: 'bushy', color: W }, hair: { style: 'long', color: W }, beard: { style: 'long', len: 1.5, color: W },
      mustache: 'droop', hat: { type: 'tallhat', color: '#ece6d8' }, outfit: { type: 'tunic', color: '#ece6d8', color2: '#c87a2a' }, expr: 'calm' },
    '提里': { age: 48, fat: 0.45, face: { w: 1.04, chin: 0.9 }, eyes: 'sleepy', brows: 'thin', hair: { style: 'bushy', color: '#2a2220' }, beard: 'none', mustache: 'none',
      hat: { type: 'diadem', color: '#9aa0aa' }, outfit: { type: 'kaftan', color: '#3a5a4a' }, expr: 'neutral' },
    '霍斯罗夫': { age: 25, eyes: 'sharp', brows: 'sword', hair: { style: 'curly', color: '#1a1614' }, beard: { style: 'short' }, mustache: 'thick',
      hat: { type: 'tiara', color: '#a02a24', color2: '#d8b050' }, outfit: { type: 'scale', metal: '#7a808c', color: '#a02a24' }, weapon: 'spear', expr: 'fierce' },
    '梯里达底': { age: 32, eyes: 'normal', brows: 'straight', hair: { style: 'curly', color: '#2a2018' }, beard: { style: 'full', len: 0.9 }, mustache: 'thick',
      hat: { type: 'tiara', color: '#d86a2a', color2: '#2a2a3a' }, outfit: { type: 'scale', metal: '#8a7048', color: '#d86a2a' }, expr: 'stern' },
    '列夫': { role: 'ruler', age: 40, skin: '#e8c4a0', eyes: 'gentle', brows: 'arched', hair: { style: 'curly', color: '#3a2a1c' }, beard: { style: 'full', len: 1.0, color: '#3a2a1c' }, mustache: 'thick',
      hat: { type: 'diadem', color: '#d8b050', gem: '#2a6aa0' }, outfit: { type: 'tunic', color: '#2a5a8a', color2: '#e8d8b0' }, expr: 'calm' },
  });

  // ---------------------------------------------- 阿拉伯、哈特拉、阿克苏姆 --
  group('arab', {
    '阿布萨米亚': { role: 'ruler', age: 50, eyes: 'sharp', brows: 'bushy', hair: { style: 'curly', color: '#1a1614', vol: 1.2 }, beard: { style: 'full', len: 1.2 }, mustache: 'thick',
      hat: { type: 'hatra', color: '#e8e0d0', color2: '#c8a040' }, outfit: { type: 'robea', color: '#e8e0d0', color2: '#8a2a24' }, expr: 'stern' },
    '萨纳特鲁克': { age: 28, eyes: 'normal', brows: 'straight', hair: { style: 'curly', color: '#1a1614' }, beard: { style: 'short' }, mustache: 'thick',
      hat: { type: 'hatra', color: '#c8a040', color2: '#2a3a5a' }, outfit: { type: 'scale', metal: '#8a7048', color: '#2a3a5a' }, expr: 'stern' },
    '阿布加尔': { role: 'ruler', age: 46, eyes: 'gentle', brows: 'arched', nose: 'aquiline', hair: { style: 'long', color: '#1a1614' }, beard: { style: 'long', len: 1.3 }, mustache: 'thick',
      hat: { type: 'tiara', color: '#e8dcc0', color2: '#5a2a6a', gem: '#d8b050' }, outfit: { type: 'robea', color: '#5a2a6a', color2: '#d8b050' }, expr: 'calm' },
    '巴戴桑': { age: 36, face: { len: 1.06 }, eyes: { shape: 'gentle', size: 1.05 }, brows: 'thin', hair: { style: 'long', color: '#1a1614' }, beard: { style: 'long', len: 1.0 }, mustache: 'thin',
      hat: { type: 'arabturban', color: '#ece6d8' }, outfit: { type: 'robea', color: '#2a3a5a', color2: '#ece6d8' }, weapon: 'bow', expr: 'calm' },
    '沙伊鲁姆': { age: 32, build: 1.12, eyes: 'sharp', brows: 'sword', hair: { style: 'curly', color: '#141012' }, beard: { style: 'short' }, mustache: 'thick',
      hat: { type: 'keffiyeh', color: '#c8a050', color2: '#8a2a24' }, outfit: { type: 'scale', metal: '#c8a050', color: '#8a2a24' }, weapon: 'spear', expr: 'fierce' },
    '阿勒汗': { role: 'ruler', age: 55, eyes: 'normal', brows: { shape: 'bushy', color: GRAY }, hair: { style: 'curly', color: '#5a5450' }, beard: { style: 'full', len: 1.1, color: '#6a6460' }, mustache: 'thick',
      hat: { type: 'arabturban', color: '#c8a050' }, outfit: { type: 'robea', color: '#c8a050', color2: '#2a3a5a' }, expr: 'calm' },
    '塔兰': { role: 'ruler', age: 48, eyes: 'narrow', brows: 'straight', hair: { style: 'long', color: '#141012' }, beard: { style: 'curled', len: 1.0 }, mustache: 'thick',
      hat: { type: 'keffiyeh', color: '#2a5a3a', color2: '#d8b050' }, outfit: { type: 'robea', color: '#2a5a3a', color2: '#d8b050' }, expr: 'stern' },
    '加达拉特': { role: 'ruler', age: 45, skin: '#7a4a2e', eyes: 'sharp', brows: 'straight', nose: 'straight', hair: { style: 'curly', color: '#100c0c' }, beard: { style: 'short', len: 0.8 }, mustache: 'thin',
      hat: { type: 'diadem', color: '#d8b050' }, outfit: { type: 'robea', color: '#e8e0d0', color2: '#a02a24' }, acc: ['goldcollar'], weapon: 'spear', expr: 'stern' },
    '贝加特': { age: 24, skin: '#704428', eyes: 'sharp', brows: 'angled', hair: { style: 'curly', color: '#100c0c' }, beard: 'stubble', mustache: 'thin', hat: 'none',
      outfit: { type: 'robea', color: '#a02a24', color2: '#e8e0d0' }, weapon: 'spear', expr: 'fierce' },
    '奥盖卢': { age: 45, eyes: 'normal', brows: 'straight', hair: { style: 'curly', color: '#1a1614' }, beard: { style: 'full', len: 0.9 }, mustache: 'thick',
      hat: { type: 'hatra', color: '#e8dcc0', color2: '#7a2a2a' }, outfit: { type: 'scale', metal: '#8a7048', color: '#7a2a2a' }, weapon: 'bow', expr: 'stern' },
    '菲利普': { age: 40, eyes: 'narrow', brows: 'knit', hair: { style: 'short', color: '#1a1614' }, beard: { style: 'stubble' }, mustache: 'none', hat: 'none',
      outfit: { type: 'musculata', metal: '#b08a50', color: '#7a2a5a', cape: 'faction' }, expr: 'stern' },
  });

  // ------------------------------------------------------- 贵霜、西部总督 --
  group('kushan', {
    '胡维色迦': { role: 'ruler', age: 58, fat: 0.35, face: { w: 1.05, jaw: 1.06 }, eyes: { shape: 'round', size: 1.05 }, brows: 'arched', nose: 'aquiline', hair: { style: 'long', color: '#2a2018' },
      beard: { style: 'full', len: 1.1, color: '#4a4040' }, mustache: 'thick', hat: { type: 'tallhat', color: '#d8b050', color2: '#c8382c' },
      outfit: { type: 'kaftan', color: '#8a2a2a', color2: '#d8b050' }, acc: ['earring'], expr: 'calm' },
    '波调': { role: 'ruler', age: 34, eyes: 'normal', brows: 'straight', nose: 'aquiline', hair: { style: 'long', color: '#1a1614' }, beard: { style: 'short', len: 1.0 }, mustache: 'thick',
      hat: { type: 'tallhat', color: '#e8dcc0', color2: '#2a3a6a' }, outfit: { type: 'kaftan', color: '#2a3a6a', color2: '#d8b050' }, acc: ['earring'], weapon: 'spear', expr: 'stern' },
    '鲁德拉辛哈': { role: 'ruler', age: 45, eyes: 'narrow', brows: 'straight', hair: { style: 'long', color: '#1a1614' }, beard: 'none', mustache: 'thick',
      hat: { type: 'pointcap', color: '#c8903a' }, outfit: { type: 'kaftan', color: '#c8903a', color2: '#5a2a2a' }, acc: ['earring'], expr: 'stern' },
  });

  // ------------------------------------------------------- 高句丽、三韩 --
  group('korea', {
    '故国川王': { role: 'ruler', age: 36, build: 1.22, neck: 1.3, face: { w: 1.04, jaw: 1.15, len: 1.04 }, eyes: 'sharp', brows: { shape: 'sword', thick: 1.2 },
      beard: { style: 'goatee', len: 1.0 }, mustache: 'thick', hat: { type: 'birdfeather', color: '#c8382c', color2: '#d8b050' },
      outfit: { type: 'lamellar', metal: '#7a808c', color: '#c8382c', cape: 'faction' }, weapon: 'bow', expr: 'stern' },
    '乙巴素': { age: 50, face: { hollow: 0.4 }, eyes: 'gentle', brows: 'straight', beard: { style: 'long', len: 1.0 }, mustache: 'thin', hat: { type: 'jeolpung', color: '#5a4a3a' },
      outfit: { type: 'jacket', color: '#e8e0d0', color2: '#5a4a3a' }, expr: 'calm' },
    '伐休': { role: 'ruler', age: 52, eyes: 'narrow', brows: 'thin', beard: { style: 'long', len: 1.1, color: '#5a5450' }, mustache: 'thin',
      hat: { type: 'goldcrown' }, outfit: { type: 'jacket', color: '#e8c84a', color2: '#8a2a2a' }, expr: 'calm' },
    '首露王': { role: 'ruler', age: 72, face: { hollow: 0.4 }, eyes: 'sleepy', brows: { shape: 'bushy', color: W }, hair: { color: W }, beard: { style: 'flowing', len: 1.5, color: W },
      mustache: 'droop', hat: { type: 'goldcrown' }, outfit: { type: 'jacket', color: '#3a5a8a', color2: '#d8b050' }, expr: 'calm' },
    '肖古王': { role: 'ruler', age: 50, eyes: 'sharp', brows: 'angled', beard: { style: 'short', len: 0.9 }, mustache: 'thick', hat: { type: 'birdfeather', color: '#2a3a6a', color2: '#d8b050' },
      outfit: { type: 'lamellar', metal: '#8a7048', color: '#2a3a6a' }, expr: 'sly' },
    '昔于老': { age: 28, build: 1.1, eyes: 'sharp', brows: 'sword', beard: 'none', mustache: 'thin', hat: { type: 'helmet', variant: 'plume', color: '#8a7048', plume: '#e8c84a' },
      outfit: { type: 'lamellar', metal: '#8a7048', color: '#e8c84a' }, weapon: 'spear', expr: 'fierce' },
    '罽须': { age: 30, build: 1.15, eyes: 'round', brows: 'bushy', beard: { style: 'short' }, mustache: 'thick', hat: { type: 'helmet', variant: 'spike', color: '#5a5e68', plume: '#c8382c' },
      outfit: { type: 'lamellar', metal: '#5a5e68', color: '#c8382c' }, weapon: 'spear', expr: 'fierce' },
    '尉仇台': { role: 'ruler', age: 45, eyes: 'normal', brows: 'straight', beard: { style: 'short', len: 0.9 }, mustache: 'thick', hat: { type: 'jeolpung', color: '#d8b050' },
      outfit: { type: 'jacket', color: '#8a2a2a', color2: '#d8b050' }, expr: 'calm' },
  });

  // ------------------------------------------------------------- 倭国 --
  group('wa', {
    '难升米': { age: 45, eyes: 'narrow', brows: 'straight', hair: { style: 'mizura' }, beard: { style: 'short', len: 0.8 }, mustache: 'thin', hat: { type: 'headband', color: '#d8b050' },
      outfit: { type: 'kantoui', color: '#e6dcc4', color2: '#3a3a5a' }, acc: ['magatama'], expr: 'calm' },
    '卑弥弓呼': { role: 'ruler', age: 40, build: 1.15, eyes: 'round', brows: 'bushy', hair: { style: 'mizura' }, beard: { style: 'full', len: 0.9 }, mustache: 'thick',
      hat: { type: 'wahelm', color: '#4a4a50' }, outfit: { type: 'tanko', metal: '#4a4a50', color: '#9a3a2a' }, marks: ['tattoo-wa'], weapon: 'spear', expr: 'fierce' },
    '狗古智卑狗': { age: 34, build: 1.12, eyes: 'sharp', brows: 'angled', hair: { style: 'mizura' }, beard: 'stubble', mustache: 'thick',
      hat: { type: 'wahelm', color: '#7a6a48' }, outfit: { type: 'tanko', metal: '#7a6a48', color: '#3a3a5a' }, marks: ['tattoo-wa'], weapon: 'sword', expr: 'stern' },
  });

  // ------------------------------------------------------- 鲜卑、乌桓、匈奴 --
  group('steppe', {
    '轲比能': { age: 30, build: 1.12, eyes: 'sharp', brows: 'sword', hair: { style: 'kunfa' }, beard: { style: 'short' }, mustache: 'droop', hat: { type: 'fur', color: '#5a3a2a' },
      outfit: { type: 'lamellar', metal: '#7a6a48', color: '#7a2a24' }, weapon: 'bow', expr: 'stern' },
    '蹋顿': { age: 34, build: 1.15, eyes: 'round', brows: 'bushy', hair: { style: 'kunfa' }, beard: { style: 'full', len: 0.8 }, mustache: 'droop', hat: 'none',
      outfit: { type: 'fur', color: '#8a5a3a' }, acc: ['earring'], weapon: 'axe', expr: 'fierce' },
    '於夫罗': { role: 'ruler', age: 40, eyes: 'sharp', brows: 'knit', hair: { style: 'braids' }, beard: { style: 'goatee', len: 1.0 }, mustache: 'droop',
      hat: { type: 'felt', color: '#a02a24' }, outfit: { type: 'kaftan', color: '#7a2a24', color2: '#d8b050' }, acc: ['earring'], weapon: 'bow', expr: 'stern' },
    '丘力居': { role: 'ruler', age: 50, eyes: 'narrow', brows: 'angled', hair: { style: 'kunfa' }, beard: { style: 'short', color: '#4a4440' }, mustache: 'droop', hat: { type: 'fur', color: '#3a2a1c' },
      outfit: { type: 'fur', color: '#5a3a2a' }, expr: 'sly' },
    '魁头': { role: 'ruler', age: 34, eyes: 'normal', brows: 'straight', hair: { style: 'kunfa' }, beard: { style: 'goatee' }, mustache: 'droop', hat: { type: 'felt', color: '#3a4a5a' },
      outfit: { type: 'kaftan', color: '#3a4a5a', color2: '#a8743a' }, weapon: 'bow', expr: 'stern' },
    '素利': { role: 'ruler', age: 46, eyes: 'sleepy', brows: 'straight', hair: { style: 'kunfa' }, beard: { style: 'short' }, mustache: 'thin', hat: { type: 'fur', color: '#8a6a40' },
      outfit: { type: 'kaftan', color: '#2a4a3a', color2: '#d8b050' }, expr: 'calm' },
    '拓跋力微': { age: 24, eyes: 'sharp', brows: 'sword', hair: { style: 'braids', color: '#2a1e16' }, beard: 'none', mustache: 'thin', hat: 'none',
      outfit: { type: 'kaftan', color: '#4a3a5a', color2: '#d8b050' }, weapon: 'bow', expr: 'stern' },
    '莫护跋': { age: 40, eyes: 'normal', brows: 'arched', hair: { style: 'braids' }, beard: { style: 'short' }, mustache: 'droop', hat: { type: 'goldcrown' },
      outfit: { type: 'kaftan', color: '#7a2a24', color2: '#d8b050' }, expr: 'smile' },
  });

  // ------------------------------------------------------- 林邑、扶南 --
  group('seasia', {
    '范师蔓': { age: 30, build: 1.2, neck: 1.3, eyes: 'sharp', brows: 'angled', beard: 'none', mustache: 'thin', hat: { type: 'flowerwrap', color: '#c8a030' },
      outfit: { type: 'bare' }, acc: ['goldcollar', 'bigears'], weapon: 'spear', expr: 'fierce' },
    '混盘况': { role: 'ruler', age: 78, face: { hollow: 0.5 }, eyes: 'sleepy', brows: { shape: 'thin', color: W }, hair: { color: W }, beard: { style: 'goatee', len: 0.9, color: W }, mustache: 'thin',
      hat: { type: 'tallcrown' }, outfit: { type: 'sash', color: '#c8a030' }, acc: ['goldcollar', 'bigears'], expr: 'sly' },
    '区连': { role: 'ruler', age: 36, build: 1.12, eyes: 'round', brows: 'bushy', beard: { style: 'stubble' }, mustache: 'thin', hat: { type: 'headband', color: '#a02a3a' },
      outfit: { type: 'sash', color: '#a02a3a' }, acc: ['hoops'], weapon: 'sword', expr: 'fierce' },
  });

  // ------------------------------------------------------------- 西域 --
  group('tarim', {
    '童格罗伽': { role: 'ruler', age: 46, eyes: 'normal', brows: 'arched', hair: { style: 'bob', color: '#5a3a20' }, beard: { style: 'short' }, mustache: 'curl',
      hat: { type: 'diadem', color: '#d8b050', gem: '#2a8a6a' }, outfit: { type: 'kaftan', color: '#2a5a6a', color2: '#d8b050' }, expr: 'calm' },
    '安国': { role: 'ruler', age: 40, eyes: { shape: 'sharp', color: '#4a6a5a' }, brows: 'straight', hair: { style: 'bob', color: '#7a4a28' }, beard: { style: 'goatee' }, mustache: 'curl',
      hat: { type: 'pointcap', color: '#e0d0b0' }, outfit: { type: 'scale', metal: '#7a808c', color: '#3a5a3a' }, weapon: 'sword', expr: 'stern' },
  });

  // ------------------------------------------------- 喀里多尼亚、日耳曼、萨尔马提亚 --
  group('celt', {
    '阿根托科克斯': { role: 'ruler', age: 38, build: 1.12, eyes: { shape: 'sharp', color: '#4a7aa0' }, brows: 'bushy', hair: { style: 'wild', color: '#b8541e' }, beard: 'none', mustache: 'droop',
      hat: 'none', outfit: { type: 'plaid', color: '#3a6a3a', color2: '#7a2a24' }, marks: ['woad'], acc: ['torc'], weapon: 'spear', expr: 'fierce' },
  });
  group('german', {
    '巴洛马尔': { role: 'ruler', age: 52, build: 1.15, eyes: { shape: 'sharp', color: '#5a8ab0' }, brows: { shape: 'bushy', color: '#c8b080' }, hair: { style: 'suebian', color: '#c8b080' },
      beard: { style: 'full', len: 1.2, color: '#c8b080' }, mustache: 'thick', hat: 'none', outfit: { type: 'fur', color: '#5a4a3a' }, acc: ['armring'], weapon: 'axe', expr: 'stern' },
    '菲利默': { role: 'ruler', age: 45, eyes: 'normal', brows: 'straight', hair: { style: 'long', color: '#d8b878' }, beard: { style: 'long', len: 1.1, color: '#d8b878' }, mustache: 'droop',
      hat: { type: 'spangen', color: '#7a808c' }, outfit: { type: 'fur', color: '#3a4a3a' }, weapon: 'spear', expr: 'calm' },
  });
  group('sarmatian', {
    '赞提库斯': { role: 'ruler', age: 40, eyes: 'sharp', brows: 'angled', hair: { style: 'long', color: '#5a3a22' }, beard: { style: 'long', len: 1.0 }, mustache: 'droop',
      hat: { type: 'conical', color: '#7a808c' }, outfit: { type: 'scale', metal: '#7a808c', color: '#7a2a24' }, weapon: 'spear', expr: 'fierce' },
    '撒罗玛提斯': { role: 'ruler', age: 45, eyes: 'gentle', brows: 'arched', hair: { style: 'curly', color: '#3a2a1c' }, beard: { style: 'full', len: 1.0 }, mustache: 'thick',
      hat: { type: 'diadem', color: '#d8b050' }, outfit: { type: 'scale', metal: '#c8a050', color: '#2a4a5a', cape: 'faction' }, expr: 'calm' },
  });

  // ------------------------------------------------------- 辽东、交州的汉人 --
  group('han', {
    '公孙度': { role: 'ruler', age: 40, face: { jaw: 1.1 }, eyes: 'sharp', brows: 'knit', beard: { style: 'long', len: 0.9 }, mustache: 'thick',
      hat: { type: 'crown', color: '#d8b050', gem: '#c0392b' }, outfit: { type: 'plate', metal: '#7a808c', color: '#5a2a2a', cape: 'faction' }, expr: 'stern' },
    '士燮': { role: 'ruler', age: 53, face: { len: 1.04 }, eyes: 'gentle', brows: 'straight', beard: { style: 'long', len: 1.2, color: '#5a5450' }, mustache: 'thin',
      hat: { type: 'jinxian', liang: 2 }, outfit: { type: 'robe', color: '#2a6a4a', color2: '#e8d8b0' }, expr: 'calm' },
    '吕岱': { age: 40, eyes: 'sharp', brows: 'straight', beard: { style: 'long', len: 0.9 }, mustache: 'thick', hat: { type: 'helmet', variant: 'round', color: '#7a808c' },
      outfit: { type: 'lamellar', metal: '#7a808c', color: '#c8382c' }, expr: 'stern' },
    '管宁': { age: 32, eyes: 'gentle', brows: 'thin', beard: { style: 'goatee', len: 0.8 }, mustache: 'thin', hat: { type: 'futou', color: '#3a3a3a' }, outfit: { type: 'robe', color: '#d8d0c0', color2: '#3a3a3a' }, expr: 'calm' },
    '阎柔': { age: 28, eyes: 'sharp', brows: 'sword', beard: { style: 'short' }, mustache: 'droop', hat: { type: 'felt', color: '#7a2a24' },
      outfit: { type: 'kaftan', color: '#5a3a2a', color2: '#d8b050' }, weapon: 'bow', expr: 'stern' },
  });

  // ---------------------------------------------------------- (1) 自动 --
  const auto = {};
  const rulers = new Set();
  try {
    if (SG.Scenarios && SG.Scenarios.world) for (const l of SG.Scenarios.world.Factions) rulers.add(String(l).split('|')[2]);
  } catch (e) { /* 无世界剧本 */ }
  if (SG.WorldInfo) {
    for (const name of Object.keys(SG.WorldInfo)) {
      const wi = SG.WorldInfo[name] || {};
      const e = {};
      if (wi.culture) e.culture = wi.culture;
      if (wi.sex === 'f') e.sex = 'f';
      if (rulers.has(name) && wi.sex !== 'f') e.role = 'ruler';
      // 已有手写年龄的（如第一阶段的卑弥呼）不覆盖
      if (typeof wi.born === 'number' && !(PD.get(name) && PD.get(name).age != null)) e.age = Math.max(22, Math.min(72, 190 - wi.born));
      auto[name] = e;
    }
  }
  const merged = {};
  for (const k of Object.keys(auto)) merged[k] = auto[k];
  for (const k of Object.keys(HAND)) merged[k] = Object.assign({}, auto[k] || {}, HAND[k]);
  PD.add(merged);
  PD.worldHand = Object.keys(HAND);
})();
