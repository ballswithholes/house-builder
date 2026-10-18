'use strict';
// Original-language names for foreign items that have no hand-written `orig`.
// Generic words per culture and type (attested terms; * = reconstructed form; "汉文史料" = the people's own word is not recorded,
// the Chinese record is the original source).
const T = {
  wa: { tachi: '大刀 tachi', sword: '銅剣 dōken', spear: '鉾 hoko', ge: '戈 ka', bow: '弓 yumi', shield: '楯 tate', leather: '短甲 tankō', cloth: '貫頭衣 kantōi', capInt: '鉢巻 hachimaki', amulet: '勾玉 magatama', relic: '銅鏡 dōkyō', music: '琴 koto' },
  korea: { dao: '환두대도 hwandu daedo', sword: '검 geom', spear: '창 chang', ji: '극 geuk', handaxe: '도끼 dokki', bow: '활 hwal', shield: '방패 bangpae', lamellar: '찰갑 chalgap', cuirass: '판갑 pangap', helm: '투구 tugu', capPol: '절풍 jeolpung', crest: '조우관 jougwan', pony: '과하마 gwahama', horse: '말 mal', jewel: '보물 bomul', tool: '덩이쇠 deongisoe', seal: '인 in' },
  india: { khanda: 'asi', spear: 'śakti / vēl', mace: 'gadā', ankusha: 'aṅkuśa', handaxe: 'paraśu', bow: 'dhanus', longbow: 'cāpa', bigshield: 'phalaka', cloth: 'kavaca (karpāsa)', crown: 'mukuṭa', capPol: 'uṣṇīṣa', elephant: 'gaja', horse: 'aśva', milbook: 'Dhanurveda', civbook: 'Dharmaśāstra', jewel: 'ratna', medicine: 'auṣadha', music: 'yāḻ', drum: 'muracu', amulet: 'cilampu', relic: 'dhātu', tool: 'yantra', sword: 'vāḷ (Tamil) / asi' },
  persia: { lance: 'nēzag', mace: 'warz (gurz)', spatha: 'šamšēr', sword: 'dašnag', handaxe: 'tabar', bow: 'kamān', scale: 'zrēh', cataphract: 'grīw-pān-war', helm: 'xōd', crown: 'kulāf (tiara)', horse: 'asp', warhorse: 'asp-ī Nisāy', banner: 'drafš', medicine: 'Ašk-ān incense', relic: 'ātaxš', music: 'barbat', tool: 'kārēz' },
  arab: { spear: 'rumḥ', sword: 'sayf', bow: 'qaws', lance: 'rumḥ', throw: 'ḥarba', shield: 'turs', leather: 'dirʿ min jild', cataphract: 'kataphraktos (Palmyra)', capInt: 'ʿimāma', camel: 'nāqa', horse: 'faras', jewel: 'lubān / murr', drum: 'daff', civbook: 'Ktābā (Syriac)', seal: 'khātam', relic: 'nuṣb (baetyl)' },
  africa: { spear: 'kʷinat (Ge‘ez)', sword: 'sayf (Ge‘ez)', longbow: 'qast (Ge‘ez)', bow: 'qast (Ge‘ez)', throw: 'kʷinat (Ge‘ez)', bigshield: 'wälta (Ge‘ez)', hide: '—（希腊文 pardalis）', cloth: '—', crown: 'akʷlil (Ge‘ez)', elephant: 'nage (Ge‘ez)', camel: 'gamal (Ge‘ez)', jewel: 'elephas (Greek, Periplus)', relic: 'Apedemak (Meroitic)', amulet: '—（考古）' },
  roman: { gladius: 'gladius', spatha: 'spatha', spear: 'hasta', lance: 'contus', throw: 'pilum / lancea', bow: 'arcus', crossbow: 'gastraphetes', bigshield: 'scutum', shield: 'clipeus', buckler: 'parma', segmentata: 'lorica segmentata', mail: 'lorica hamata', scale: 'lorica squamata', cuirass: 'lorica musculata', cloth: 'subarmalis', robe: 'paludamentum', hide: 'leonté', helm: 'galea', crest: 'galea cristata', mask: 'cassis', crown: 'corona', horse: 'equus', handaxe: 'securis', milbook: 'liber', civbook: 'liber', seal: 'anulus', banner: 'vexillum', drum: 'cornu', jewel: 'vitrum', medicine: 'capsa medici', amulet: 'bulla', tool: 'dolabra', relic: 'sacrum' },
  celt: { spatha: '*kladios (Gaulish)', spear: 'gaison', throw: 'gaesum', shield: 'scíath (Old Irish)', buckler: 'caetra', hide: 'glastum (woad)', cloth: 'brat (Old Irish)', mail: 'lorica (Gaulish make)', helm: 'cathbarr (Old Irish)', crest: 'cathbarr (Old Irish)', pony: 'marcos (Gaulish)', chariot: 'essedon / covinnus', drum: 'carnyx', amulet: 'torques', relic: 'uiscum (mistletoe)', handaxe: 'biáil (Old Irish)', music: 'crot (Old Irish)' },
  german: { spear: 'framea', handaxe: '*akwisī', club: 'fustis (Tacitus)', spatha: '*swerdą', throw: '*gaizaz', shield: '*skelduz', bigshield: '*skelduz', hide: '*wulfaz-fellą', mask: 'nodus Suebicus', helm: '*helmaz', horse: '*hrussą', jewel: 'glaesum', amulet: 'Donar-Amulett', relic: 'harja (runes)', bow: '*bugô' },
  sarmatian: { lance: 'kontos', spatha: 'akinakes / gladius praelongus', sword: 'akinakes', bow: 'toxon', scale: 'lorica squamata', cataphract: 'kataphraktos', helm: 'konos', warhorse: 'hippos Alanos', jewel: 'chrysos', amulet: 'tamga', banner: 'draco', throw: 'seira (lasso)' },
  moor: { throw: 'iaculum', spear: 'lancea', buckler: 'parma (elephant hide)', hide: 'pellis pardalis', cloth: 'sagum', horse: 'equus Maurus', jewel: 'ovum struthionis', sword: 'gladius', bow: 'arcus' },
  kushan: { spatha: 'asi (Gāndhārī)', lance: 'kunta (Sanskrit)', handaxe: 'sagaris (Greek, Saka axe)', mace: 'gadā', ankusha: 'aṅkuśa', bow: 'dhanu (Gāndhārī)', scale: 'kavaca', robe: 'kañcuka', crown: 'mukuṭa', capInt: 'kyrbasia (Greek, Saka cap)', horse: 'aśva', elephant: 'hastin', camel: 'uṣṭra', jewel: 'rājāvarta (lapis)', relic: 'dhātu', civbook: 'sūtra (Gāndhārī)', amulet: 'dinara' },
  tarim: { sword: 'asi (Niya Prakrit)', dao: 'asi (Niya Prakrit)', spear: 'kunta', bow: 'dhanu', leather: 'kavaca', scale: 'kavaca', camel: 'uṭa (Niya Prakrit)', horse: 'aśpa (Niya Prakrit)', capInt: 'kyrbasia', jewel: 'yü (jade)', music: 'vīṇā', civbook: 'lekha (Kharoṣṭhī)' },
  steppe: {}, seasia: {}, nanman: {}, yi: {},
};
const DEFAULT = {
  steppe: '原名不传（汉文史料所记）', tarim: '原名不传（汉文 / 佉卢文史料所记）', kushan: '原名不传（巴克特里亚文未见）', seasia: '原名不传（汉文史料所记）',
  nanman: '原名不传（汉文史料所记）', yi: '原名不传（沈莹《临海水土志》所记）', wa: '原名不传（《魏志倭人传》所记）', korea: '原名不传（汉文史料所记）',
  india: '—', persia: '—', arab: '—', africa: '—', roman: '—', celt: '—', german: '—', sarmatian: '—', moor: '—',
};
// per-item overrides (better than the generic word)
const ID = {
  in_hujiao: 'marica (Sanskrit) / milaku (Tamil)', in_zhenzhu: 'muttu (Tamil)', in_lvzhu: 'vaiḍūrya / beryllos', in_caraka: 'Caraka Saṃhitā', ar_ruxiang: 'lubān', ar_moyao: 'murr',
  af_xiangya: 'elephas (Periplus)', af_xijiao: 'rhinokerōs (Periplus)', ku_qingjin: 'lājward', ku_jinbi: 'dinar', ku_foxiang: 'Buddha (Gāndhārī)', ku_huapi: 'Gāndhārī birch-bark sūtra',
  ta_mudu: 'Kharoṣṭhī wedge tablets', ta_yu: 'yü (Khotan jade)', ta_pipa: 'Kuchean five-string lute', pe_anxixiang: 'Ašk (Arsacid) incense',
  zhumeng_gong: '추모왕의 활 (Jumong)', chiyu_jian: '적옥검 jeogok-geom', silla_huantou: '금장 환두대도', guizhi_jian: '구지봉 철검', guangxing_mao: '広形銅矛 hirogata dōhoko',
  huvishka_gou: 'aṅkuśa (Ooēški / Huvishka)', simha_jian: 'siṃha-asi', anahita_zhang: 'warz ī Anāhīd', amirani_jian: 'ამირანის ხმალი Amiranis khmali',
  shamash_mao: 'rumḥ Šmš (Hatra)', aksum_mao: 'kʷinat (GDRT)', caledonia_jian: 'ingens gladius (Tacitus)', gapt_mao: '*gaizaz Gautis', baquates_mao: 'iaculum regium',
  sarvak_mao: 'nēzag (Elymais)', faw_jian: 'sayf (Qaryat al-Faw)', satavahana_jian: 'asi (Sātavāhana)', shuangyu_jian: 'kayal vāḷ (Tamil)', chera_gong: 'vil (Tamil)',
  lanka_jian: 'kaḍuva (Sinhala)', paratarajas_jian: 'svastika asi', bosporus_jian: 'akinakes chrysous', hasding_jian: '*swerdą Hasdingō', arbela_chui: 'warz (Adiabene)',
  charax_jian: 'sayf al-luʾluʾ', marib_jian: 'sayf nuḥās (Sabaic)', zafar_mao: 'rumḥ (Ḥimyar)', shabwa_dao: 'sayf (Ḥaḍramawt)', edessa_jian: 'saypā (Syriac)',
  linyi_mao: '原名不传（象林）', tianshan_mao: '原名不传（车师）', cuoling_dao: '原名不传（疏勒）', shanshan_jian: 'asi (Gāndhārī, Kroraina)',
  tiannv_dao: '原名不传（拓跋）', wuhuan_dao: '原名不传（乌桓）', langshou_dao: '原名不传（鲜卑）', baoshen_gong: '原名不传（鲜卑）',
  jinglu: '径路（匈奴语；一说即突厥语 qïŋïraq「刀」之音译）', zhurong_feidao: '—（演义人物）', jili_guduo: '—（演义）', songwen_jian: '—（演义）', wugetengjia: '—（演义）',
  buyao_guan: '步摇（慕容之名所出）', kebineng_jia: '原名不传（鲜卑）', fanman_bo: '原名不传（扶南）', hundiangong: 'Kauṇḍinya’s bow (Funan legend)',
  sheri: 'Atayal: two suns legend (口传)', tianma: '汗血马（大宛 Ferghana）', pishamen: 'Vaiśravaṇa', shenshou: '原名不传（拓跋）', kanishka_casket: 'Kaniṣka reliquary',
  anyang_nu: 'Nỏ thần An Dương Vương', hushi_shinu: '楛矢石砮（挹娄）', tillya_crown: 'Tillya Tepe crown', wuxing_hubo: '五星出东方利中国（汉锦）', dianwang_yin: '滇王之印', wonu_jinyin: '漢委奴國王',
  antonine_aureus: 'aureus Antonini Pii', huiwang_yin: '濊王之印', qizhidao: '七支刀 shichishitō',
  ko_tieting: '덩이쇠 deongisoe', ko_lelang_qi: '낙랑 칠기 Nangnang chilgi', ko_diao: '담비 가죽 dambi gajuk',
  sm_tamga: 'tamga', sm_jinshi: 'chrysos (Bosporus)', ge_hupo: 'glaesum', ge_fuwenshu: 'harja', ce_torc: 'torc', ce_haojiao: 'carnyx',
};
function origFor(it) {
  if (it.orig) return it.orig;
  if (it.culture === 'han') return null;
  return ID[it.id] || (T[it.culture] && T[it.culture][it.type]) || DEFAULT[it.culture] || null;
}
module.exports = { origFor };
