'use strict';
// v4 items catalogue generator — core tables (rarity, slots, types, stat profiles, palettes, helpers)
// Output is data only; the game reads items.json via js/items-data.js (see ITEMS.md §11).

const RARITY = {
  common:    { tier: 0, name: '普通', alias: '凡品', color: '#9aa3ad', frame: 'slate',    basePrice: 80,   sellK: 0.40 },
  uncommon:  { tier: 1, name: '精良', alias: '良品', color: '#5fae6e', frame: 'jade',     basePrice: 320,  sellK: 0.40 },
  rare:      { tier: 2, name: '稀有', alias: '珍品', color: '#4a8fdc', frame: 'lapis',    basePrice: 1400, sellK: 0.50 },
  epic:      { tier: 3, name: '史诗', alias: '神品', color: '#a464d8', frame: 'amethyst', basePrice: 6000, sellK: 0.50 },
  legendary: { tier: 4, name: '传说', alias: '天宝', color: '#f0a52a', frame: 'goldcinnabar', basePrice: 0, sellK: 0 },
};
const TIERS = ['common', 'uncommon', 'rare', 'epic', 'legendary'];

const SLOTS = {
  weapon:   { name: '兵器', cat: 'weapon', order: 0 },
  offhand:  { name: '副手', cat: 'mixed', order: 1, note: '盾（须单手兵器）或弓 / 弩 / 投掷兵器' },
  armour:   { name: '铠甲', cat: 'gear', order: 2 },
  helmet:   { name: '头盔', cat: 'gear', order: 3, note: '含冠、巾、帻等文官首服' },
  mount:    { name: '坐骑', cat: 'gear', order: 4 },
  treasure: { name: '宝物', cat: 'gear', order: 5, note: '书、印、器具、旗鼓、乐器、珍宝、药、护符' },
};

// type → slot, category (weapon/gear for the ≥250 counts), duel weapon key, figure.js model id, icon silhouette, stat profile
// duel keys marked * and fig ids marked * are NEW entries to add (ITEMS.md §8).
const TYPES = {
  // ---- main weapons ----
  spear:     { slot: 'weapon', cat: 'weapon', name: '枪矛', style: 'pole', duel: 'spear', fig: 'spear', icon: 'spear' },
  lance:     { slot: 'weapon', cat: 'weapon', name: '骑矛', style: 'pole', duel: 'lance', fig: 'lance', icon: 'lance' },
  ji:        { slot: 'weapon', cat: 'weapon', name: '戟', style: 'pole', duel: 'ji', fig: 'halberd', icon: 'ji' },
  ge:        { slot: 'weapon', cat: 'weapon', name: '戈', style: 'pole', duel: 'ji', fig: 'ge*', icon: 'ge' },
  poleblade: { slot: 'weapon', cat: 'weapon', name: '长柄刀', style: 'pole', duel: 'poleblade', fig: 'poleblade', icon: 'glaive' },
  bigblade:  { slot: 'weapon', cat: 'weapon', name: '斩马刀', style: 'pole', duel: 'bigblade', fig: 'bigblade', icon: 'bigblade' },
  axe:       { slot: 'weapon', cat: 'weapon', name: '大斧', style: 'pole', duel: 'axe', fig: 'bigaxe', icon: 'axe2h' },
  trident:   { slot: 'weapon', cat: 'weapon', name: '三叉', style: 'pole', duel: 'ji', fig: 'trident', icon: 'trident' },
  falx:      { slot: 'weapon', cat: 'weapon', name: '镰形刀', style: 'pole', duel: 'bigblade', fig: 'falx*', icon: 'falx' },
  staff:     { slot: 'weapon', cat: 'weapon', name: '杖节', style: 'pole', duel: 'staff*', fig: 'staff', icon: 'staff' },
  sword:     { slot: 'weapon', cat: 'weapon', name: '剑', style: 'one', duel: 'sword', fig: 'jian', icon: 'jian' },
  dao:       { slot: 'weapon', cat: 'weapon', name: '刀', style: 'one', duel: 'dao', fig: 'dao', icon: 'dao' },
  gladius:   { slot: 'weapon', cat: 'weapon', name: '短剑', style: 'one', duel: 'gladius', fig: 'gladius', icon: 'gladius' },
  spatha:    { slot: 'weapon', cat: 'weapon', name: '长剑', style: 'one', duel: 'sword', fig: 'spatha', icon: 'spatha' },
  tachi:     { slot: 'weapon', cat: 'weapon', name: '直刀', style: 'one', duel: 'tachi', fig: 'tachi', icon: 'tachi' },
  khanda:    { slot: 'weapon', cat: 'weapon', name: '阔剑', style: 'one', duel: 'sword', fig: 'khanda', icon: 'khanda' },
  mace:      { slot: 'weapon', cat: 'weapon', name: '锤杵', style: 'one', duel: 'mace', fig: 'mace', icon: 'mace' },
  club:      { slot: 'weapon', cat: 'weapon', name: '棍棒', style: 'one', duel: 'club*', fig: 'club*', icon: 'club' },
  handaxe:   { slot: 'weapon', cat: 'weapon', name: '战斧', style: 'one', duel: 'handaxe', fig: 'handaxe', icon: 'axe1h' },
  shortji:   { slot: 'weapon', cat: 'weapon', name: '手戟', style: 'one', duel: 'shortji', fig: 'shortji', icon: 'shortji' },
  bian:      { slot: 'weapon', cat: 'weapon', name: '鞭', style: 'one', duel: 'mace', fig: 'bian*', icon: 'bian' },
  ankusha:   { slot: 'weapon', cat: 'weapon', name: '象钩', style: 'one', duel: 'mace', fig: 'ankusha*', icon: 'ankusha' },
  vajra:     { slot: 'weapon', cat: 'weapon', name: '金刚杵', style: 'one', duel: 'vajra*', fig: 'vajra*', icon: 'vajra' },
  fan:       { slot: 'weapon', cat: 'weapon', name: '羽扇', style: 'fan', duel: 'fan*', fig: 'fan', icon: 'fan' },
  twinsword: { slot: 'weapon', cat: 'weapon', name: '双剑', style: 'dual', duel: 'twinsword', fig: 'swords', icon: 'twinsword' },
  twindao:   { slot: 'weapon', cat: 'weapon', name: '双刀', style: 'dual', duel: 'twindao', fig: 'twindao', icon: 'twindao' },
  twinji:    { slot: 'weapon', cat: 'weapon', name: '双戟', style: 'dual', duel: 'twinji', fig: 'twinji', icon: 'twinji' },
  // ---- off-hand ranged (count as weapons) ----
  bow:       { slot: 'offhand', cat: 'weapon', name: '弓', style: 'ranged', fig: 'bow', icon: 'bow' },
  longbow:   { slot: 'offhand', cat: 'weapon', name: '长弓', style: 'ranged', fig: 'longbow', icon: 'longbow' },
  crossbow:  { slot: 'offhand', cat: 'weapon', name: '弩', style: 'ranged', fig: 'crossbow*', icon: 'crossbow' },
  throw:     { slot: 'offhand', cat: 'weapon', name: '投掷', style: 'ranged', fig: 'javelins*', icon: 'javelin' },
  // ---- off-hand shields (gear) ----
  shield:    { slot: 'offhand', cat: 'gear', name: '盾', style: 'shield', fig: 'shield', icon: 'shield' },
  bigshield: { slot: 'offhand', cat: 'gear', name: '大盾', style: 'shield', fig: 'shield', icon: 'bigshield' },
  buckler:   { slot: 'offhand', cat: 'gear', name: '小盾', style: 'shield', fig: 'shield', icon: 'buckler' },
  // ---- armour ----
  lamellar:  { slot: 'armour', cat: 'gear', name: '札甲', torso: 'zha', icon: 'lamellar' },
  scale:     { slot: 'armour', cat: 'gear', name: '鳞甲', torso: 'scale', icon: 'scale' },
  mail:      { slot: 'armour', cat: 'gear', name: '锁子甲', torso: 'mail*', icon: 'mail' },
  segmentata:{ slot: 'armour', cat: 'gear', name: '环片甲', torso: 'segm', icon: 'segm' },
  cuirass:   { slot: 'armour', cat: 'gear', name: '胸甲', torso: 'cuirass*', icon: 'cuirass' },
  leather:   { slot: 'armour', cat: 'gear', name: '皮甲', torso: 'zha', icon: 'leather' },
  rattan:    { slot: 'armour', cat: 'gear', name: '藤甲', torso: 'rattan*', icon: 'rattan' },
  cloth:     { slot: 'armour', cat: 'gear', name: '布甲', torso: 'coat', icon: 'cloth' },
  robe:      { slot: 'armour', cat: 'gear', name: '袍服', torso: 'coat', icon: 'robe' },
  cataphract:{ slot: 'armour', cat: 'gear', name: '具装', torso: 'scale', icon: 'cataphract' },
  hide:      { slot: 'armour', cat: 'gear', name: '兽皮彩绘', torso: 'bare', icon: 'hide' },
  // ---- helmet ----
  helm:      { slot: 'helmet', cat: 'gear', name: '盔', icon: 'helm' },
  crest:     { slot: 'helmet', cat: 'gear', name: '缨盔', icon: 'crest' },
  mask:      { slot: 'helmet', cat: 'gear', name: '面甲盔', icon: 'mask' },
  capInt:    { slot: 'helmet', cat: 'gear', name: '巾帻', icon: 'cap' },
  capPol:    { slot: 'helmet', cat: 'gear', name: '冠', icon: 'guan' },
  crown:     { slot: 'helmet', cat: 'gear', name: '王冠', icon: 'crown' },
  // ---- mount ----
  horse:     { slot: 'mount', cat: 'gear', name: '战马', fig: 'horse', icon: 'horse' },
  warhorse:  { slot: 'mount', cat: 'gear', name: '甲马', fig: 'horse', icon: 'warhorse' },
  pony:      { slot: 'mount', cat: 'gear', name: '矮马', fig: 'horse', icon: 'pony' },
  camel:     { slot: 'mount', cat: 'gear', name: '骆驼', fig: 'camel*', icon: 'camel' },
  elephant:  { slot: 'mount', cat: 'gear', name: '战象', fig: 'elephant*', icon: 'elephant' },
  chariot:   { slot: 'mount', cat: 'gear', name: '战车', fig: 'chariot*', icon: 'chariot' },
  // ---- treasure ----
  milbook:   { slot: 'treasure', cat: 'gear', name: '兵书', icon: 'scroll' },
  civbook:   { slot: 'treasure', cat: 'gear', name: '典籍', icon: 'book' },
  seal:      { slot: 'treasure', cat: 'gear', name: '印绶', icon: 'seal' },
  tool:      { slot: 'treasure', cat: 'gear', name: '器械', icon: 'tool' },
  banner:    { slot: 'treasure', cat: 'gear', name: '旗帜', icon: 'banner' },
  drum:      { slot: 'treasure', cat: 'gear', name: '鼓角', icon: 'drum' },
  music:     { slot: 'treasure', cat: 'gear', name: '乐器', icon: 'qin' },
  jewel:     { slot: 'treasure', cat: 'gear', name: '珍宝', icon: 'jewel' },
  medicine:  { slot: 'treasure', cat: 'gear', name: '医药', icon: 'medicine' },
  amulet:    { slot: 'treasure', cat: 'gear', name: '护符', icon: 'amulet' },
  relic:     { slot: 'treasure', cat: 'gear', name: '圣物', icon: 'relic' },
};

// stat profiles by rarity tier index [common, uncommon, rare, epic, legendary]
const P = {
  spear:     { war: [1, 3, 5, 7, 10], atk: [3, 6, 9, 12, 16], crit: [0, 1, 1, 2, 3], dDmg: [0, 1, 2, 3, 3] },
  lance:     { war: [1, 2, 4, 6, 9], atk: [2, 5, 8, 11, 14], charge: [5, 8, 11, 14, 18], dDmg: [0, 1, 2, 3, 3] },
  ji:        { war: [1, 3, 5, 7, 10], atk: [3, 5, 8, 11, 15], counter: [4, 6, 9, 12, 15], dDmg: [0, 1, 2, 3, 3] },
  ge:        { war: [1, 2, 4, 6, 9], atk: [3, 5, 8, 11, 14], counter: [3, 5, 7, 9, 12], dDmg: [0, 1, 2, 3, 3] },
  poleblade: { war: [1, 3, 5, 7, 10], atk: [4, 7, 10, 13, 17], crit: [1, 1, 2, 2, 3], dDmg: [1, 2, 3, 3, 3], dSpd: [-2, -2, -1, -1, 0] },
  bigblade:  { war: [1, 3, 5, 7, 10], atk: [4, 7, 10, 13, 17], critDmg: [5, 10, 15, 20, 25], dDmg: [1, 2, 3, 3, 3], dSpd: [-3, -3, -2, -2, -1] },
  axe:       { war: [1, 3, 5, 7, 10], atk: [5, 8, 11, 14, 18], crit: [1, 1, 2, 2, 3], armorBreak: [3, 5, 7, 9, 12], dDmg: [1, 2, 3, 3, 3], dSpd: [-4, -3, -3, -2, -2] },
  trident:   { war: [1, 3, 5, 7, 10], atk: [3, 6, 9, 12, 15], counter: [3, 5, 7, 9, 12], crit: [0, 1, 1, 2, 2] },
  falx:      { war: [1, 3, 5, 7, 10], atk: [5, 8, 11, 14, 18], armorBreak: [4, 6, 8, 10, 13], dDmg: [1, 2, 3, 3, 3], dSpd: [-3, -3, -2, -2, -1] },
  staff:     { intel: [1, 2, 4, 6, 8], pol: [1, 2, 3, 4, 6], morale: [2, 4, 6, 8, 10], atk: [1, 2, 3, 4, 5] },
  sword:     { war: [1, 2, 4, 6, 9], atk: [2, 4, 7, 10, 14], crit: [1, 1, 2, 2, 3], dParry: [0, 0.01, 0.01, 0.01, 0.01] },
  dao:       { war: [1, 2, 4, 6, 9], atk: [3, 5, 8, 11, 15], crit: [1, 1, 2, 2, 3] },
  gladius:   { war: [1, 2, 4, 6, 9], atk: [3, 5, 8, 11, 14], def: [1, 2, 3, 4, 5] },
  spatha:    { war: [1, 2, 4, 6, 9], atk: [3, 5, 8, 11, 14], charge: [2, 4, 6, 8, 10] },
  tachi:     { war: [1, 2, 4, 6, 9], atk: [3, 5, 8, 11, 15], crit: [1, 1, 2, 3, 3] },
  khanda:    { war: [1, 2, 4, 6, 9], atk: [3, 6, 9, 12, 15], critDmg: [3, 6, 10, 14, 18] },
  mace:      { war: [1, 2, 4, 6, 9], atk: [3, 5, 8, 11, 15], armorBreak: [3, 5, 7, 9, 12], dGuardBreak: [3, 5, 7, 9, 12] },
  club:      { war: [1, 2, 4, 7, 12], atk: [3, 5, 8, 12, 17], armorBreak: [3, 5, 7, 10, 14], dDmg: [1, 2, 3, 3, 3], dGuardBreak: [3, 5, 7, 10, 12] },
  handaxe:   { war: [1, 2, 4, 6, 9], atk: [3, 6, 9, 12, 15], crit: [1, 1, 2, 2, 3] },
  shortji:   { war: [1, 2, 4, 6, 9], atk: [2, 4, 7, 10, 13], crit: [1, 1, 2, 2, 3], first: [3, 5, 7, 9, 12] },
  bian:      { war: [1, 2, 4, 6, 9], atk: [3, 5, 8, 11, 15], armorBreak: [3, 5, 7, 9, 12], dGuardBreak: [3, 5, 7, 9, 12] },
  ankusha:   { war: [1, 2, 3, 5, 7], atk: [2, 4, 7, 10, 13], pol: [0, 1, 2, 3, 4] },
  vajra:     { war: [1, 3, 5, 8, 12], atk: [3, 6, 9, 13, 18], crit: [1, 1, 2, 3, 4] },
  fan:       { intel: [2, 4, 6, 8, 10], tac: [5, 8, 11, 14, 18], tacHit: [0, 2, 3, 4, 6] },
  twinsword: { war: [1, 2, 4, 6, 9], atk: [2, 5, 8, 11, 14], crit: [1, 2, 2, 3, 3] },
  twindao:   { war: [1, 2, 4, 6, 9], atk: [3, 5, 8, 11, 14], crit: [1, 2, 2, 3, 3] },
  twinji:    { war: [1, 2, 4, 6, 9], atk: [3, 5, 8, 11, 15], crit: [1, 1, 2, 2, 3], counter: [2, 3, 4, 5, 6] },
  bow:       { counterGuard: [6, 9, 12, 15, 20], crit: [0, 1, 1, 1, 2] },
  longbow:   { counterGuard: [8, 11, 14, 17, 22], first: [2, 4, 6, 8, 10] },
  crossbow:  { counterGuard: [8, 11, 14, 17, 22], first: [5, 8, 11, 14, 18] },
  throw:     { first: [6, 10, 14, 18, 24], crit: [0, 1, 1, 1, 2] },
  shield:    { def: [3, 5, 8, 11, 14], dGuard: [2, 3, 4, 5, 6] },
  bigshield: { def: [4, 7, 10, 13, 16], dGuard: [3, 4, 5, 6, 6], dSpd: [-2, -2, -1, -1, 0] },
  buckler:   { def: [2, 4, 6, 8, 10], dGuard: [1, 2, 2, 3, 4], dParry: [0.01, 0.01, 0.01, 0.01, 0.01] },
  lamellar:  { def: [4, 7, 10, 13, 17], dHp: [3, 5, 6, 8, 10], dSpd: [-1, -1, -1, 0, 0] },
  scale:     { def: [5, 8, 11, 14, 18], dHp: [4, 5, 7, 9, 10], dSpd: [-2, -2, -1, -1, 0] },
  mail:      { def: [4, 7, 10, 13, 16], dHp: [3, 5, 6, 8, 10], critRes: [1, 2, 2, 3, 3], dSpd: [-1, -1, -1, -1, 0] },
  segmentata:{ def: [6, 9, 12, 15, 19], dHp: [4, 6, 7, 9, 10], dSpd: [-3, -3, -2, -2, -1] },
  cuirass:   { def: [5, 8, 11, 14, 18], dHp: [4, 6, 7, 9, 10], morale: [0, 1, 2, 3, 4], dSpd: [-2, -2, -1, -1, 0] },
  leather:   { def: [3, 5, 7, 9, 12], dHp: [2, 3, 4, 6, 7] },
  rattan:    { def: [5, 8, 11, 18, 20], dHp: [3, 4, 6, 10, 10], fireWeak: [30, 30, 30, 150, 50] },
  cloth:     { def: [2, 4, 6, 8, 10], dHp: [1, 2, 3, 4, 5], dSpd: [1, 1, 2, 2, 3] },
  robe:      { intel: [1, 2, 4, 6, 8], def: [1, 2, 3, 4, 5], tac: [0, 3, 5, 7, 10] },
  cataphract:{ def: [7, 10, 13, 16, 20], dHp: [5, 6, 8, 10, 10], charge: [3, 5, 7, 9, 12], mob: [-1, -1, -1, -1, 0], dSpd: [-4, -4, -3, -3, -2] },
  hide:      { def: [1, 2, 3, 6, 8], awe: [2, 3, 5, 7, 9], dHp: [1, 2, 3, 6, 7] },
  helm:      { def: [2, 3, 5, 7, 9], dHp: [1, 2, 3, 4, 5], critRes: [1, 1, 2, 2, 3] },
  crest:     { def: [2, 3, 4, 6, 8], morale: [2, 3, 5, 7, 9], dHp: [1, 1, 2, 3, 4] },
  mask:      { def: [2, 3, 5, 7, 9], awe: [2, 3, 4, 6, 8] },
  capInt:    { intel: [1, 2, 3, 5, 7], def: [0, 0, 1, 1, 2] },
  capPol:    { pol: [1, 2, 3, 5, 7], def: [0, 0, 1, 1, 2] },
  crown:     { pol: [1, 2, 3, 5, 7], fame: [0, 1, 2, 3, 5] },
  horse:     { mob: [0, 0, 1, 1, 2], charge: [3, 5, 7, 9, 12], dMove: [2, 3, 5, 7, 10], escape: [10, 20, 35, 50, 70] },
  warhorse:  { mob: [0, 0, 0, 1, 1], charge: [5, 8, 11, 14, 18], def: [1, 2, 3, 4, 5], escape: [5, 10, 20, 30, 40] },
  pony:      { mob: [0, 0, 1, 1, 1], dMove: [1, 2, 3, 4, 5], escape: [10, 15, 25, 35, 50], ease: [['hill'], ['hill', 'forest'], ['hill', 'forest'], ['hill', 'forest'], ['hill', 'forest', 'river']] },
  camel:     { mob: [0, 0, 1, 1, 1], antiHorse: [3, 5, 7, 9, 12], dMove: [0, 1, 2, 3, 4], escape: [5, 10, 20, 30, 40] },
  elephant:  { mob: [-1, -1, 0, 0, 0], atk: [5, 8, 11, 14, 18], def: [2, 4, 6, 8, 10], awe: [3, 5, 7, 9, 12], fireWeak: [20, 20, 20, 20, 20], escape: [0, 0, 5, 10, 20] },
  chariot:   { charge: [6, 9, 12, 15, 20], mob: [0, 0, 0, 1, 1], awe: [1, 2, 3, 4, 5], hard: [['forest', 'hill'], ['forest', 'hill'], ['forest', 'hill'], ['forest'], ['forest']] },
  milbook:   { intel: [1, 2, 4, 6, 8], tac: [3, 6, 9, 12, 16], train: [1, 2, 3, 4, 6] },
  civbook:   { pol: [1, 2, 4, 6, 8] },          // + one domain key from opts.dom (devK)
  seal:      { pol: [1, 2, 3, 5, 7], fame: [0, 1, 2, 3, 5], dip: [3, 5, 8, 12, 18], hire: [2, 4, 6, 9, 12] },
  tool:      {},                                 // + one or two domain keys (toolK)
  banner:    { morale: [4, 6, 8, 11, 15] },
  drum:      { morale: [3, 5, 7, 9, 12], awe: [1, 2, 3, 4, 6] },
  music:     { loyal: [0, 1, 1, 2, 2], morale: [1, 2, 3, 4, 5], intel: [0, 1, 2, 3, 4] },
  jewel:     { gift: [1.0, 1.1, 1.25, 1.5, 2.0], pol: [0, 0, 1, 2, 3] },
  medicine:  { heal: [3, 5, 8, 11, 15], intel: [0, 1, 2, 3, 4] },
  amulet:    { exp: [3, 5, 8, 12, 16], crit: [0, 0, 1, 1, 1], escape: [3, 5, 8, 10, 15] },
  relic:     { morale: [2, 3, 5, 7, 9], fame: [0, 1, 1, 2, 3], virtue: [0, 1, 2, 3, 5] },
};
const DEV_K = [5, 10, 15, 20, 30];      // civbook domain %
const TOOL_K = [8, 12, 18, 25, 35];     // tool domain %
const FLAT_K = { train: [1, 2, 3, 4, 6], search: [2, 4, 6, 8, 12], hire: [2, 4, 6, 9, 12], dip: [3, 5, 8, 12, 18], discord: [3, 5, 8, 12, 18], persuade: [3, 5, 8, 12, 18], reward: [1, 2, 3, 4, 6], loyal: [0, 1, 1, 2, 2], heal: [3, 5, 8, 11, 15] };

// price multipliers by type (× rarity basePrice)
const PRICE_K = {
  weapon: 1.0, offhandShield: 0.7, offhandRanged: 0.85, throw: 0.6, armour: 1.15, helmet: 0.6, mount: 1.25, treasure: 1.0,
};

// Lanternvale-style culture palettes: [primary, secondary, accent, ink]
const PAL = {
  han:       { c: ['#b8442e', '#c9a24e', '#5f9e7c', '#2b2622'], metal: '冷灰钢', trim: '朱漆与铜饰', cloth: '赭红绢' },
  nanman:    { c: ['#b08a4a', '#e8dcc0', '#b5652d', '#2a1e14'], metal: '青铜', trim: '兽牙与骨珠', cloth: '靛蓝蜡染布' },
  wa:        { c: ['#a07a3c', '#c8452c', '#4f9a6a', '#241c16'], metal: '暗铁', trim: '朱丹与勾玉', cloth: '白麻' },
  yi:        { c: ['#d8c6a0', '#6f8f7a', '#2f3e66', '#1e1a16'], metal: '磨制青石', trim: '贝珠与羽毛', cloth: '苎麻细布' },
  korea:     { c: ['#6b7078', '#b03a2e', '#d8b04a', '#1e1c1c'], metal: '锻铁', trim: '金铜透雕', cloth: '素白麻' },
  steppe:    { c: ['#8a6440', '#c8b089', '#4aa3a0', '#221810'], metal: '黑铁', trim: '金饰与绿松石', cloth: '毛毡' },
  tarim:     { c: ['#a44a3e', '#a8c8a0', '#3a5aa8', '#22180f'], metal: '亮铁', trim: '和田玉与红绒', cloth: '红地毛织' },
  kushan:    { c: ['#5a4a86', '#d6a640', '#efe2c4', '#1c1626'], metal: '亮钢', trim: '金币纹与青金石', cloth: '紫锦' },
  seasia:    { c: ['#8a5a34', '#d8b04a', '#b8443a', '#1e140c'], metal: '铁', trim: '金片与象牙', cloth: '朱红棉布' },
  india:     { c: ['#e08a2a', '#2a6f8a', '#d8b04a', '#1c1410'], metal: '乌兹钢', trim: '金银错与宝石', cloth: '番红棉' },
  persia:    { c: ['#2c4f9e', '#c9a24e', '#9c2a3a', '#14182a'], metal: '马尔吉亚纳钢', trim: '金线与珍珠', cloth: '深红丝锦' },
  arab:      { c: ['#d8c49a', '#2e3a6a', '#a8743a', '#1e1810'], metal: '印度钢', trim: '青铜与皮绳', cloth: '靛蓝与白羊毛' },
  africa:    { c: ['#efe4cc', '#a84a2a', '#d8b04a', '#2a1e18'], metal: '麦罗埃铁', trim: '象牙与金叶', cloth: '白棉披巾' },
  roman:     { c: ['#a8302a', '#c8a050', '#b9bec6', '#201a18'], metal: '诺里库姆钢', trim: '黄铜饰件', cloth: '茜红羊毛' },
  celt:      { c: ['#2a58b0', '#c8a050', '#3f6e4a', '#1a1a20'], metal: '青铜与铁', trim: '拉坦诺卷纹', cloth: '格纹羊毛' },
  german:    { c: ['#6a5a46', '#a8442a', '#d8902a', '#1c1610'], metal: '熟铁', trim: '琥珀与银丝', cloth: '粗毛布' },
  sarmatian: { c: ['#c9a24e', '#9a3a2a', '#efe4cc', '#1e1610'], metal: '鳞片铁', trim: '金饰与红玉髓', cloth: '红毡' },
  moor:      { c: ['#b08050', '#efe6d4', '#6a2a5a', '#201810'], metal: '铁', trim: '皮条与贝壳', cloth: '白羊毛斗篷' },
};

const CULTURES = Object.keys(PAL);
const CULT_NAME = { han: '汉', nanman: '南中', wa: '倭', yi: '夷洲', korea: '东夷', steppe: '草原', tarim: '西域', kushan: '贵霜·大宛', seasia: '南海', india: '天竺', persia: '安息·波斯', arab: '阿拉伯', africa: '阿克苏姆·库施', roman: '罗马', celt: '凯尔特', german: '日耳曼', sarmatian: '萨尔马提亚', moor: '毛里' };

// trade neighbours: commons of a neighbour culture are stocked in tier≥2 cities (×1.2 price)
const NEIGHBOURS = {
  han: ['nanman', 'korea', 'steppe', 'tarim', 'seasia'], nanman: ['han', 'seasia'], korea: ['han', 'wa', 'steppe'], wa: ['korea', 'han'],
  yi: ['han', 'seasia'], steppe: ['han', 'korea', 'tarim', 'kushan'], tarim: ['han', 'steppe', 'kushan'], kushan: ['tarim', 'steppe', 'persia', 'india'],
  seasia: ['han', 'india', 'nanman', 'yi'], india: ['kushan', 'seasia', 'persia', 'arab'], persia: ['kushan', 'arab', 'roman', 'sarmatian', 'india'],
  arab: ['persia', 'roman', 'africa', 'india'], africa: ['arab', 'roman', 'india'], roman: ['celt', 'german', 'sarmatian', 'arab', 'africa', 'moor', 'persia'],
  celt: ['roman', 'german'], german: ['roman', 'celt', 'sarmatian'], sarmatian: ['german', 'roman', 'persia', 'steppe'], moor: ['roman', 'africa'],
};

function round10(v) { return Math.round(v / 10) * 10; }
function hash(s) { let h = 2166136261 >>> 0; s = String(s); for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h >>> 0; }

// build stats for (type, rarity, opts)
function statsFor(type, rarity, opts) {
  opts = opts || {};
  const t = RARITY[rarity].tier;
  const prof = P[type] || {};
  const s = {};
  for (const k of Object.keys(prof)) {
    const v = prof[k][t];
    if (Array.isArray(v)) { s[k] = v.slice(); continue; }
    if (v !== 0) s[k] = v;
  }
  if (type === 'civbook' && opts.dom) for (const d of [].concat(opts.dom)) s[d] = FLAT_K[d] ? FLAT_K[d][t] : DEV_K[t];
  if (type === 'tool' && opts.dom) for (const d of [].concat(opts.dom)) s[d] = FLAT_K[d] ? FLAT_K[d][t] : TOOL_K[t];
  if (opts.swap) for (const [a, b] of Object.entries(opts.swap)) { if (s[a] !== undefined) { s[b] = (s[b] || 0) + s[a]; delete s[a]; } }
  if (opts.add) for (const [k, v] of Object.entries(opts.add)) s[k] = Math.round(((s[k] || 0) + v) * 100) / 100;
  if (opts.set) for (const [k, v] of Object.entries(opts.set)) { if (v === null) delete s[k]; else s[k] = v; }
  for (const k of Object.keys(s)) if (s[k] === 0) delete s[k];
  return s;
}

function priceFor(type, rarity, opts) {
  const T = TYPES[type];
  let k = PRICE_K.weapon;
  if (T.slot === 'offhand') k = T.cat === 'gear' ? PRICE_K.offhandShield : (type === 'throw' ? PRICE_K.throw : PRICE_K.offhandRanged);
  else if (T.slot !== 'weapon') k = PRICE_K[T.slot];
  const v = 1 + ((opts && opts.priceAdj) || 0);
  return round10(RARITY[rarity].basePrice * k * v);
}

module.exports = { RARITY, TIERS, SLOTS, TYPES, P, DEV_K, TOOL_K, FLAT_K, PRICE_K, PAL, CULTURES, CULT_NAME, NEIGHBOURS, round10, hash, statsFor, priceFor };
