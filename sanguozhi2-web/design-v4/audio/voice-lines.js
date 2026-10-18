'use strict';
/* ==========================================================================
   AUDIO-V4.md Part B reference data: 语音 (voiced barks).
   The implementation copies LINGUA / CUES / LINES / FACTION_LINGUA into js/voice-data.js;
   tools/voice-build/ (Python, offline) renders every line into the voice packs (§B7).

   LINGUA        29 speech languages: historical language, modern stand-in, phonemizer route,
                 TTS voice per sex (tested in tts-eval.json), translation confidence A / B / C.
   CUES          the cue catalogue: id → zh master line(s) + English gloss + speaker + priority +
                 fallback cue. 20 core cues must exist in every lingua; extended cues fall back.
   LINES         translations: lingua → cue → [variants]. '~' + kind = a procedural wordless cry
                 (formant bark, §B5) where the language has no attested word that fits.
   FACTION_LINGUA / CULTURE_LINGUA   who speaks what (§B3).

   node design-v4/audio/voice-lines.js   → coverage check, clip count, duration and size estimates
   ========================================================================== */

// ------------------------------------------------------------ languages --
// conf: A modern language (or modern stand-in) · B classical, well attested · C sparse: attested words only
// route: phonemizer — 'pinyin' (authored pinyin, pypinyin fallback) · 'openjtalk' · 'espeak:<code>' · 'ipa' (authored IPA)
// voices: m / f → '<model>#<speaker>' ; models are sherpa-onnx tts-models mirrors of Piper / Mimic3 / Kokoro voices
const LINGUA = {
  zh:  { name: '汉语', hist: '东汉洛阳雅言', standIn: '现代普通话（文白相间的古风台词）', conf: 'A', route: 'pinyin',
         voices: { m: 'kokoro-multi-lang-v1_0#zm_yunjian', m2: 'kokoro-multi-lang-v1_0#zm_yunxi', y: 'vits-piper-zh_CN-chaowen-medium', f: 'kokoro-multi-lang-v1_0#zf_xiaoyi' } },
  ja:  { name: '倭语', hist: '上代日本语（弥生—古坟）', standIn: '现代日语（武家口吻）', conf: 'A', route: 'openjtalk',
         voices: { m: 'kokoro-multi-lang-v1_0#jm_kumo', f: 'kokoro-multi-lang-v1_0#jf_alpha' } },
  ko:  { name: '韩语', hist: '夫余—高句丽语、三韩诸语', standIn: '现代韩语（史剧体）', conf: 'A', route: 'espeak:ko',
         voices: { f: 'vits-mimic3-ko_KO-kss_low', m: null }, gap: '无可用男声；KSS 数据为 CC BY-NC-SA' },
  vi:  { name: '扶南语（越语代）', hist: '孟—高棉语（扶南，多数学者之说）', standIn: '越南语（同属南亚语系）', conf: 'A', route: 'espeak:vi',
         voices: { f: 'vits-piper-vi_VN-vais1000-medium', m: null }, gap: '无男声' },
  ms:  { name: '占语（马来语代）', hist: '占语（南岛语系，林邑）', standIn: '马来 / 印尼语', conf: 'A', route: 'espeak:id',
         voices: { f: 'vits-piper-id_ID-news_tts-medium', m: null }, gap: '无男声' },
  mn:  { name: '鲜卑语（蒙古语代）', hist: '准蒙古语（鲜卑、乌桓、拓跋）', standIn: '喀尔喀蒙古语', conf: 'A', route: 'espeak:mn',
         voices: { m: 'vits-piper-kk_KZ-issai-high' }, gap: '借哈萨克语声库读蒙古语音素，可懂度未验证' },
  kk:  { name: '匈奴语（突厥语代）', hist: '匈奴语（叶尼塞 / 突厥假说，未定）', standIn: '哈萨克语', conf: 'A', route: 'espeak:kk',
         voices: { m: 'vits-piper-kk_KZ-issai-high', m2: 'vits-piper-kk_KZ-iseke-x_low' } },
  pra: { name: '犍陀罗俗语', hist: '尼雅俗语 / 犍陀罗语（鄯善、于阗钱铭）；摩诃剌侘俗语（百乘）', standIn: '戏剧俗语（绍罗塞尼）程式句', conf: 'B', route: 'espeak:hi',
         voices: { m: 'vits-piper-ur_PK-fasih-medium', saka: 'vits-piper-fa_IR-amir-medium', f: null }, gap: '女声只有 NC 许可（hi priyamvada）' },
  sa:  { name: '梵语', hist: '古典梵语（西总督鲁德拉达曼朱纳格特铭文）', standIn: '古典梵语', conf: 'B', route: 'espeak:hi',
         voices: { m: 'vits-piper-ur_PK-fasih-medium', f: null } },
  txb: { name: '吐火罗语', hist: '吐火罗语（车师 / 焉耆 / 龟兹；文献 6 世纪起）', standIn: '俗语正文 + 吐火罗乙种词（walo 王、kartse 善、ñakte 诸神）', conf: 'C', route: 'ipa',
         voices: { m: 'vits-piper-kk_KZ-issai-high' } },
  xbc: { name: '大夏语', hist: '大夏语（贵霜官方语言，拉巴塔克铭文）', standIn: '俗语正文 + 大夏语尊号（šao 王、šaonanošao 王中之王、bago 主）', conf: 'C', route: 'ipa',
         voices: { m: 'vits-piper-fa_IR-reza_ibrahim-medium' } },
  ta:  { name: '泰米尔语', hist: '古泰米尔语（桑伽姆）', standIn: '泰米尔语（古雅口吻）', conf: 'A', route: 'espeak:ta',
         voices: { m: 'vits-piper-ml_IN-arjun-medium', f: 'vits-piper-ml_IN-meera-medium' } },
  si:  { name: '僧伽罗语', hist: '僧伽罗俗语（埃卢语）', standIn: '僧伽罗语', conf: 'A', route: 'espeak:si',
         voices: { m: 'vits-piper-ml_IN-arjun-medium' } },
  fa:  { name: '波斯语', hist: '帕提亚语（安息）、中古波斯语（波西斯）', standIn: '波斯语（正式体）+ 帕提亚 / 中古波斯尊号（šāhān šāh、pērōz）', conf: 'A', route: 'espeak:fa',
         voices: { m: 'vits-piper-fa_IR-amir-medium', m2: 'vits-piper-fa_IR-reza_ibrahim-medium' } },
  syc: { name: '阿拉米语', hist: '叙利亚语（埃德萨）、哈特拉阿拉米语、埃利迈斯阿拉米语', standIn: '古典叙利亚语（拉丁转写 → IPA）', conf: 'B', route: 'ipa',
         voices: { m: 'vits-piper-ar_JO-kareem-medium' } },
  ka:  { name: '格鲁吉亚语', hist: '古格鲁吉亚语（卡特维利语）', standIn: '格鲁吉亚语', conf: 'A', route: 'espeak:ka',
         voices: { f: 'vits-piper-ka_GE-natia-medium', m: null }, gap: '无男声' },
  ar:  { name: '阿拉伯语', hist: '古阿拉伯语（金达）；萨巴诸国用示巴语', standIn: '古典阿拉伯语 + 示巴语神名尊号（ʾAlmaqah、mlk Sbʾ）', conf: 'A', route: 'espeak:ar',
         voices: { m: 'vits-piper-ar_JO-kareem-medium' } },
  gez: { name: '吉兹语', hist: '吉兹语（阿克苏姆，与希腊语并用）', standIn: '吉兹语（埃塞俄比亚字母，借阿姆哈拉语音系注音）', conf: 'B', route: 'espeak:am',
         voices: { m: 'vits-piper-ar_JO-kareem-medium' } },
  xmr: { name: '麦罗埃语', hist: '麦罗埃语（文字可读，语义大多未解）', standIn: '只用已解读的词：qore 王、kdke 王后、Amani 阿蒙、Apedemak 狮神', conf: 'C', route: 'ipa',
         voices: { m: 'vits-piper-sw_CD-lanfrica-medium' } },
  ber: { name: '柏柏尔语', hist: '利比亚—柏柏尔语（巴夸特）', standIn: '卡拜尔语少量词（agellid 王、azul、tanemmirt）', conf: 'C', route: 'ipa',
         voices: { m: 'vits-piper-ar_JO-kareem-medium' } },
  la:  { name: '拉丁语', hist: '古典拉丁语', standIn: '古典拉丁语（意大利语声库读拉丁音素）', conf: 'B', route: 'espeak:la',
         voices: { m: 'vits-piper-it_IT-riccardo-x_low', f: 'vits-piper-it_IT-paola-medium' } },
  grc: { name: '希腊语', hist: '通用希腊语（博斯普鲁斯、罗马东方）', standIn: '通用希腊语（意大利语声库读古希腊音素）', conf: 'B', route: 'espeak:grc',
         voices: { m: 'vits-piper-it_IT-riccardo-x_low', f: 'vits-piper-it_IT-paola-medium' }, note: 'el_GR-rapunzelina-low 遇希腊文本会胡言 5–8 秒，弃用' },
  os:  { name: '萨尔马提亚语（奥塞梯语代）', hist: '萨尔马提亚语（雅济吉斯）', standIn: '奥塞梯语少量词（хорз 好、размæ 前进、уæлахиз 胜利、бузныг 谢谢、æлдар 主）', conf: 'C', route: 'ipa',
         voices: { m: 'vits-piper-kk_KZ-issai-high' } },
  got: { name: '哥特语', hist: '哥特语（乌尔菲拉圣经，4 世纪；马科曼尼为易北日耳曼语）', standIn: '哥特语', conf: 'B', route: 'espeak:is',
         voices: { m: 'vits-piper-de_DE-thorsten_emotional-medium#neutral', mBattle: 'vits-piper-de_DE-thorsten_emotional-medium#angry' } },
  cy:  { name: '布立吞语（威尔士语代）', hist: '布立吞语 / 皮克特语（喀里多尼亚）', standIn: '威尔士语', conf: 'A', route: 'espeak:cy',
         voices: { m: 'vits-piper-cy_GB-bu_tts-medium#4', f: 'vits-piper-cy_GB-bu_tts-medium#0' } },
  ga:  { name: '盖尔语（爱尔兰语代）', hist: '原始爱尔兰语（欧甘铭文）', standIn: '爱尔兰语', conf: 'A', route: 'espeak:ga',
         voices: { m: 'vits-piper-cy_GB-bu_tts-medium#4', f: 'vits-piper-cy_GB-bu_tts-medium#0' } },
};

// ------------------------------------------------------------ who speaks what --
const FACTION_LINGUA = {
  // 经典 18 势力 + 汉人诸侯
  dong: 'zh', yuanshao: 'zh', cao: 'zh', liubei: 'zh', sunjian: 'zh', yuanshu: 'zh', gongsun: 'zh', liubiao: 'zh', liuyan: 'zh',
  mateng: 'zh', taoqian: 'zh', kongrong: 'zh', hanfu: 'zh', zhanglu: 'zh', zhangyang: 'zh', liuyao: 'zh', lukang: 'zh', menghuo: 'zh',
  gongsundu: 'zh', shixie: 'zh', zhufu: 'zh',
  goguryeo: 'ko', buyeo: 'ko', baekje: 'ko', silla: 'ko', gaya: 'ko', yamatai: 'ja', kuna: 'ja',
  xianbei: 'mn', dongxianbei: 'mn', wuhuan: 'mn', tuoba: 'mn', xiongnu: 'kk',
  kroraina: 'pra', khotan: 'pra', kashgar: 'pra', jushi: 'txb', linyi: 'ms', funan: 'vi',
  kushan: 'xbc', kshatrapa: 'sa', paratarajas: 'pra', satavahana: 'pra', chola: 'ta', pandya: 'ta', chera: 'ta', lanka: 'si',
  parthia: 'fa', persis: 'fa', iberia: 'ka', adiabene: 'syc', elymais: 'syc', hatra: 'syc', osroene: 'syc', characene: 'syc',
  saba: 'ar', himyar: 'ar', hadramawt: 'ar', kinda: 'ar', aksum: 'gez', kush: 'xmr', baquates: 'ber',
  roma: 'la', bosporus: 'grc', iazyges: 'os', caledonia: 'cy', eriu: 'ga', marcomanni: 'got', goths: 'got', vandals: 'got',
};
// a general whose culture differs from his starting faction's speaks his own culture's lingua
const CULTURE_LINGUA = {
  han: 'zh', nanman: 'zh', korea: 'ko', wa: 'ja', steppe: 'mn', sarmatian: 'os', tarim: 'pra', kushan: 'xbc', india: 'pra',
  persia: 'fa', arab: 'ar', moor: 'ber', africa: 'gez', roman: 'la', celt: 'cy', german: 'got', seasia: 'ms', yi: 'ms',
};
// factions whose Prakrit speakers get the Iranian (Saka) voice
const SAKA_VOICE = { khotan: 1, kashgar: 1 };

// ------------------------------------------------------------ cue catalogue --
// who: gen (the acting general) · ruler · target (the other side) · any
// pri: 3 critical (never dropped) · 2 important · 1 routine (dropped when busy / rate-limited)
// fb: fallback cue when a lingua lacks this one; '~x' = procedural cry of kind x
const CUES = {
  // ---- core 20 (every lingua) ----
  ack:      { core: 1, who: 'gen', pri: 1, zh: ['遵命！', '末将领命！', '交给我吧！'], en: 'As you command!' },
  work:     { core: 1, who: 'gen', pri: 1, zh: ['这就去办！', '即刻动身！'], en: 'On it at once!' },
  recruit:  { core: 1, who: 'gen', pri: 1, zh: ['招募义勇！'], en: 'Raise volunteers!' },
  train:    { core: 1, who: 'gen', pri: 1, zh: ['操练！', '列阵！'], en: 'Drill!' },
  search:   { core: 1, who: 'gen', pri: 1, zh: ['出城寻访。'], en: "I'll go and seek them out." },
  move:     { core: 1, who: 'gen', pri: 1, zh: ['出发！', '开拔！'], en: 'Move out!' },
  attack:   { core: 1, who: 'ruler', pri: 2, zh: ['全军出击！', '出征！'], en: 'All forces, attack!' },
  great:    { core: 1, who: 'gen', pri: 2, zh: ['大功告成！', '幸不辱命！'], en: 'Splendidly done!' },
  miss:     { core: 1, who: 'gen', pri: 1, zh: ['惭愧……'], en: 'Forgive me…' },
  charge:   { core: 1, who: 'gen', pri: 2, zh: ['冲啊！', '杀！', '随我冲锋！'], en: 'Charge!' },
  retreat:  { core: 1, who: 'gen', pri: 2, zh: ['撤退！'], en: 'Fall back!' },
  crit:     { core: 1, who: 'gen', pri: 2, zh: ['看招！', '着！'], en: 'Take this!' },
  special:  { core: 1, who: 'gen', pri: 3, zh: ['接我绝技！'], en: 'Behold my art!' },
  victory:  { core: 1, who: 'gen', pri: 3, zh: ['我军大胜！', '胜了！'], en: 'Victory is ours!' },
  defeat:   { core: 1, who: 'gen', pri: 3, zh: ['可恶……'], en: 'Curses…' },
  duel:     { core: 1, who: 'gen', pri: 3, zh: ['谁敢与我一战！'], en: 'Who dares face me?' },
  allyYes:  { core: 1, who: 'ruler', pri: 2, zh: ['愿结盟好。'], en: 'Let us be allies.' },
  allyNo:   { core: 1, who: 'ruler', pri: 2, zh: ['休想！'], en: 'Never!' },
  grant:    { core: 1, who: 'ruler', pri: 2, zh: ['封赏于你！'], en: 'I reward you with rank!' },
  thanks:   { core: 1, who: 'gen', pri: 2, zh: ['谢主公隆恩！'], en: 'Thank you, my lord!' },
  // ---- extended: strategy commands (DOMESTIC-FX §5 cue ids) ----
  'cmd.develop.start':   { who: 'gen', pri: 1, fb: 'work', zh: ['开垦良田！'], en: 'Break new fields!' },
  'cmd.develop.great':   { who: 'gen', pri: 2, fb: 'great', zh: ['五谷丰登！'], en: 'A bumper harvest!' },
  'cmd.industry.start':  { who: 'gen', pri: 1, fb: 'work', zh: ['起炉鼓铸！'], en: 'Fire the furnaces!' },
  'cmd.industry.great':  { who: 'gen', pri: 2, fb: 'great', zh: ['百炼成钢！'], en: 'Steel of a hundred foldings!' },
  'cmd.town.start':      { who: 'gen', pri: 1, fb: 'work', zh: ['修缮闾里！'], en: 'Mend the wards!' },
  'cmd.town.great':      { who: 'gen', pri: 2, fb: 'great', zh: ['市井繁华！'], en: 'The markets are thriving!' },
  'cmd.recruit.start':   { who: 'gen', pri: 1, fb: 'recruit', zh: ['招募义勇！'], en: 'Raise volunteers!' },
  'cmd.recruit.great':   { who: 'gen', pri: 2, fb: 'great', zh: ['万众归心！'], en: 'Thousands flock to us!' },
  'cmd.train.start':     { who: 'gen', pri: 1, fb: 'train', zh: ['操练！'], en: 'Drill!' },
  'cmd.train.great':     { who: 'gen', pri: 2, fb: 'great', zh: ['百战之师！'], en: 'An army of a hundred battles!' },
  'cmd.search.start':    { who: 'gen', pri: 1, fb: 'search', zh: ['出城寻访。'], en: 'Off to seek talent.' },
  'cmd.search.none':     { who: 'gen', pri: 1, fb: 'miss', zh: ['一无所获……'], en: 'Nothing found…' },
  'cmd.search.talent':   { who: 'target', pri: 2, fb: 'ack', zh: ['何人扰我清梦？'], en: 'Who disturbs my dreams?' },
  'cmd.hire.ask':        { who: 'gen', pri: 1, fb: 'ack', zh: ['先生可愿助我一臂之力？'], en: 'Will you lend us your strength?' },
  'cmd.hire.yes':        { who: 'target', pri: 2, fb: 'thanks', zh: ['愿效犬马之劳！'], en: 'I will serve you with all I have!' },
  'cmd.hire.no':         { who: 'target', pri: 1, fb: 'allyNo', zh: ['恕难从命。'], en: 'I must decline.' },
  'cmd.move.start':      { who: 'gen', pri: 1, fb: 'move', zh: ['移驻！'], en: 'Redeploy!' },
  'cmd.transport.start': { who: 'gen', pri: 1, fb: 'work', zh: ['押运粮草！'], en: 'Escort the supplies!' },
  'cmd.ally.yes':        { who: 'target', pri: 2, fb: 'allyYes', zh: ['愿结盟好。'], en: 'Let us be allies.' },
  'cmd.ally.no':         { who: 'target', pri: 2, fb: 'allyNo', zh: ['休想！'], en: 'Never!' },
  'cmd.discord.ok':      { who: 'gen', pri: 2, fb: 'great', zh: ['离间之计已成。'], en: 'The seeds of discord are sown.' },
  'cmd.persuade.yes':    { who: 'target', pri: 2, fb: 'thanks', zh: ['愿效犬马之劳！'], en: 'I will serve you!' },
  'cmd.attack.start':    { who: 'ruler', pri: 2, fb: 'attack', zh: ['出征！'], en: 'To war!' },
  'cmd.reward.thanks':   { who: 'gen', pri: 1, fb: 'thanks', zh: ['谢主公赏赐！'], en: 'Thank you for the gift, my lord!' },
  'cmd.trade.start':     { who: 'gen', pri: 1, fb: 'work', zh: ['买卖公道！'], en: 'A fair price!' },
  'cmd.appoint.yes':     { who: 'gen', pri: 2, fb: 'thanks', zh: ['定不负所托！'], en: 'I will not fail your trust!' },
  'cmd.market.buy':      { who: 'gen', pri: 1, fb: 'great', zh: ['好兵器！'], en: 'A fine weapon!' },
  'cmd.endMonth':        { who: 'ruler', pri: 1, fb: 'ack', zh: ['今月之事已毕。', '传令各城，依计行事。'], en: "This month's work is done." },
  'ui.generalOpen':      { who: 'gen', pri: 1, fb: 'ack', zh: ['主公有何吩咐？', '末将在！'], en: 'What are your orders, my lord?' },
  'news.succession':     { who: 'ruler', pri: 3, fb: 'victory', zh: ['先主之志，吾当继之！'], en: "I shall carry on my predecessor's cause!" },
  // ---- extended: titles (TITLES.md §?), items (ITEMS.md), tournament (request 14) ----
  'title.grant':      { who: 'ruler', pri: 2, fb: 'grant', zh: ['封赏于你！', '加官进爵！'], en: 'I raise you in rank!' },
  'title.accept':     { who: 'gen', pri: 2, fb: 'thanks', zh: ['谢主公隆恩！'], en: 'Thank you for your grace!' },
  'title.promote':    { who: 'gen', pri: 2, fb: 'great', zh: ['更上一层楼！'], en: 'Higher still!' },
  'title.revoke':     { who: 'gen', pri: 2, fb: 'miss', zh: ['臣……遵命。'], en: 'I… obey.' },
  'title.passedOver': { who: 'gen', pri: 1, fb: '~grumble', zh: ['哼……'], en: 'Hmph…' },
  'title.rankUp':     { who: 'ruler', pri: 3, fb: 'victory', zh: ['威加四海！'], en: 'Our might spans the four seas!' },
  'item.epic':        { who: 'gen', pri: 2, fb: 'great', zh: ['好一件宝物！'], en: 'What a treasure!' },
  'item.legendary':   { who: 'gen', pri: 3, fb: 'special', zh: ['神兵在手，天下无敌！'], en: 'With this divine weapon, none can stand against me!' },
  'tourney.enter':    { who: 'gen', pri: 2, fb: 'duel', zh: ['天下英雄，尽在此地！'], en: 'All the heroes under heaven are here!' },
  'tourney.win':      { who: 'gen', pri: 3, fb: 'victory', zh: ['天下第一！'], en: 'First under heaven!' },
  // ---- extended: battle (board), clash, duel (COMBAT-V4 §2.6, §8.7, §9.4, §13.3) ----
  'bat.select':      { who: 'gen', pri: 1, fb: 'ack', zh: ['在！', '何事？', '请下令！'], en: 'Here!' },
  'bat.move':        { who: 'gen', pri: 1, fb: 'move', zh: ['前进！', '跟上！'], en: 'Advance!' },
  'bat.attack':      { who: 'gen', pri: 1, fb: 'charge', zh: ['攻击！', '杀过去！'], en: 'Attack!' },
  'bat.tactic':      { who: 'gen', pri: 2, fb: 'special', zh: ['看我妙计！', '中我计了！'], en: 'Fall to my stratagem!' },
  'bat.tacticFail':  { who: 'gen', pri: 1, fb: 'miss', zh: ['计策被识破了……'], en: 'They saw through it…' },
  'bat.fire':        { who: 'gen', pri: 2, fb: 'bat.tactic', zh: ['火攻！'], en: 'Fire attack!' },
  'bat.enemyRuler':  { who: 'gen', pri: 2, fb: 'charge', zh: ['敌军主将在此！'], en: "The enemy's lord is here!" },
  'bat.reinforce':   { who: 'gen', pri: 2, fb: 'great', zh: ['援军到了！'], en: 'Reinforcements!' },
  'bat.lowMorale':   { who: 'gen', pri: 1, fb: 'miss', zh: ['士气低落……'], en: 'Morale is failing…' },
  'bat.rout':        { who: 'gen', pri: 2, fb: 'defeat', zh: ['我军败了……'], en: 'We are routed…' },
  'bat.captured':    { who: 'gen', pri: 3, fb: 'defeat', zh: ['我……被俘了。'], en: 'I am… taken.' },
  'bat.cityTaken':   { who: 'gen', pri: 3, fb: 'victory', zh: ['城池已破！'], en: 'The city has fallen!' },
  'bat.hurt':        { who: 'gen', pri: 1, fb: '~hurt', zh: ['呃！'], en: '(pain)' },
  'clash.crit':      { who: 'gen', pri: 2, fb: 'crit', zh: ['看招！'], en: 'Take this!' },
  'duel.accept':     { who: 'target', pri: 3, fb: 'duel', zh: ['来得好！'], en: 'Come on, then!' },
  'duel.refuse':     { who: 'target', pri: 2, fb: 'allyNo', zh: ['不与匹夫斗！'], en: 'I do not fight common brutes!' },
  'duel.win':        { who: 'gen', pri: 3, fb: 'victory', zh: ['胜负已分！'], en: 'It is decided!' },
  'duel.lose':       { who: 'gen', pri: 3, fb: 'defeat', zh: ['我输了……'], en: 'I am beaten…' },
  'duel.parry':      { who: 'gen', pri: 2, fb: 'crit', zh: ['看破！'], en: 'Seen through!' },
  'duel.guardcrush': { who: 'gen', pri: 2, fb: 'crit', zh: ['破！'], en: 'Break!' },
  'duel.finisher':   { who: 'gen', pri: 3, fb: 'special', zh: ['到此为止！'], en: 'This ends now!' },
  'duel.crit':       { who: 'gen', pri: 2, fb: 'crit', zh: ['着！'], en: 'Hit!' },
  // efforts are always procedural (§B5): light / heavy / hurt / ko / breath — no lines
};

// ------------------------------------------------------------ translations of the core 20 --
// order of the arrays below = the core cue order (ack … thanks)
const CORE = ['ack', 'work', 'recruit', 'train', 'search', 'move', 'attack', 'great', 'miss', 'charge', 'retreat', 'crit', 'special', 'victory', 'defeat', 'duel', 'allyYes', 'allyNo', 'grant', 'thanks'];
const T = {
  ja: ['ははっ！|かしこまりました！', 'ただちに取りかかります！', '兵を募れ！', '鍛えよ！', '人材を探してまいります。', '出立！', '全軍、進め！', '見事、成し遂げました！', '面目ない……', 'かかれ！|者ども、かかれ！', '退け！', 'くらえ！', '我が秘技を見よ！', '勝ったぞ！', '無念……', '我と勝負せよ！', '盟を結ぼう。', '断る！', 'そなたに位を授ける！', 'ありがたき幸せ！'],
  ko: ['분부대로 하겠습니다!', '즉시 시행하겠습니다!', '병사를 모아라!', '훈련하라!', '인재를 찾아 나서겠습니다.', '출발하라!', '전군, 진격하라!', '큰 공을 세웠습니다!', '면목이 없습니다……', '돌격하라!', '후퇴하라!', '받아라!', '나의 비기를 보아라!', '우리가 이겼다!', '분하다……', '누가 나와 겨루겠느냐!', '동맹을 맺읍시다.', '어림없다!', '그대에게 벼슬을 내리노라!', '성은이 망극하옵니다!'],
  vi: ['Tuân lệnh!', 'Thần đi làm ngay!', 'Chiêu mộ nghĩa binh!', 'Luyện binh!', 'Thần đi tìm hiền tài.', 'Xuất phát!', 'Toàn quân xuất kích!', 'Đại công cáo thành!', 'Thần hổ thẹn…', 'Xung phong!', 'Rút lui!', 'Đỡ này!', 'Hãy xem tuyệt kỹ của ta!', 'Ta thắng rồi!', 'Đáng hận…', 'Ai dám đấu với ta!', 'Xin kết đồng minh.', 'Đừng hòng!', 'Ta phong tước cho ngươi!', 'Đội ơn chúa công!'],
  ms: ['Siap, Tuanku!', 'Segera dilaksanakan!', 'Kumpulkan pasukan!', 'Berlatih!', 'Hamba pergi mencari orang berbakat.', 'Berangkat!', 'Seluruh pasukan, serbu!', 'Berhasil gemilang!', 'Ampun, Tuanku…', 'Serbu!', 'Mundur!', 'Rasakan ini!', 'Lihatlah jurus hamba!', 'Kita menang!', 'Celaka…', 'Siapa berani melawanku!', 'Mari bersekutu.', 'Tidak sudi!', 'Kuberi engkau gelar!', 'Terima kasih, Tuanku!'],
  mn: ['Зарлигаар!', 'Одоо хийнэ!', 'Цэрэг дайчил!', 'Бэлтгэл хий!', 'Хүн олохоор явъя.', 'Мордъё!', 'Бүх цэрэг, довтол!', 'Амжилттай!', 'Уучлаарай…', 'Хурай!|Довтол!', 'Ухар!', 'Энийг ав!', 'Миний ур чадварыг хар!', 'Бид яллаа!', 'Харамсалтай…', 'Хэн надтай тулалдана!', 'Холбоотон болъё.', 'Үгүй!', 'Чамд цол олгоё!', 'Баярлалаа, хаан минь!'],
  kk: ['Құп болады!', 'Қазір орындаймын!', 'Әскер жина!', 'Жаттық!', 'Дарын іздеп барамын.', 'Аттан!', 'Барша әскер, шабуыл!', 'Ұлы жеңіс!', 'Кешіріңіз…', 'Алға!|Шабуыл!', 'Шегін!', 'Мә, ал!', 'Менің өнерімді көр!', 'Жеңдік!', 'Қап…', 'Кім менімен жекпе-жекке шығады!', 'Одақ құрайық.', 'Жоқ!', 'Саған атақ беремін!', 'Рақмет, тақсыр!'],
  pra: ['जं देवो आणवेदि!', 'दाणिं करेमि!', 'सेण्णं संगेण्हध!', 'अब्भसह!', 'गुणिं अण्णेसामि।', 'गच्छामो!', 'सव्वे, पहरह!', 'सिद्धं!', 'खमीअदु…', 'जअदु!|पहरह!', 'ओसरह!', 'गेण्ह!', 'पेक्ख मे कोसल्लं!', 'विजओ अम्हाणं!', 'हद्धी…', 'को मए सह जुज्झिस्सदि!', 'संधिं करेम्ह।', 'ण कदा वि!', 'पदं दे देमि!', 'अणुग्गहीदो म्हि!'],
  sa: ['यथाज्ञापयति देवः!', 'इदानीमेव करोमि!', 'सैन्यं संगृह्णीत!', 'अभ्यसत!', 'गुणिनमन्विष्यामि।', 'प्रस्थीयताम्!', 'सर्वसेने, आक्रमत!', 'सिद्धम्!', 'क्षम्यताम्…', 'जय!|आक्रमत!', 'अपसरत!', 'गृहाण!', 'पश्य मे कौशलम्!', 'विजयोऽस्माकम्!', 'हा धिक्…', 'को मया सह योत्स्यते!', 'सन्धिं कुर्मः।', 'न कदापि!', 'पदं ते ददामि!', 'अनुगृहीतोऽस्मि!'],
  ta: ['உத்தரவு, அரசே!', 'உடனே செய்கிறேன்!', 'படை திரட்டு!', 'பயிற்சி செய்!', 'அறிஞரைத் தேடிச் செல்கிறேன்.', 'புறப்படு!', 'படையே, தாக்கு!', 'பெருவெற்றி!', 'மன்னியுங்கள்…', 'முன்னேறு!|தாக்கு!', 'பின்வாங்கு!', 'வாங்கிக்கொள்!', 'என் கலையைப் பார்!', 'நாம் வென்றோம்!', 'ஐயோ…', 'என்னுடன் போரிட யார் துணிவார்!', 'நட்புக் கொள்வோம்.', 'ஒருபோதும் இல்லை!', 'உனக்குப் பட்டம் தருகிறேன்!', 'நன்றி, அரசே!'],
  si: ['එසේය, දේවයන් වහන්ස!', 'දැන්ම කරනවා!', 'හමුදාව රැස් කරනු!', 'පුහුණු වෙනු!', 'දක්ෂයන් සොයා යමි.', 'පිටත් වෙමු!', 'මුළු හමුදාවම, පහර දෙනු!', 'ජයග්‍රහණයයි!', 'සමාවෙන්න…', 'ඉදිරියට!', 'පසු බසිනු!', 'මෙන්න!', 'මගේ හැකියාව බලනු!', 'අපි දිනුවා!', 'අහෝ…', 'මා සමඟ සටනට කවුද එන්නේ!', 'මිත්‍ර වෙමු.', 'කිසිදා නැත!', 'ඔබට පදවියක් දෙමි!', 'ජය වේවා!'],
  fa: ['فرمان شماست!', 'هم‌اکنون!', 'سپاه گرد آورید!', 'تمرین کنید!', 'به جستجوی کاردانان می‌روم.', 'برویم!', 'همه، یورش!', 'آفرین!', 'ببخشید…', 'به پیش!|یورش!', 'عقب‌نشینی!', 'بگیر!', 'هنر مرا ببین!', 'پیروز شدیم!', 'دریغا…', 'کیست که با من نبرد کند؟', 'پیمان ببندیم.', 'هرگز!', 'تو را لقب می‌دهم!', 'سپاس، شاهنشاه!'],
  syc: ['ʔēn, māry!', 'hāšā ʕābed nā!', 'kannešw ḥaylā!', 'ʔetdarrašw!', 'ʔāzel nā wa-bāʕē nā.', 'nēzal!', 'kulkon, qrubw!', 'šappīr!', 'šbuq lī…', 'lqrābā!', 'hpukw!', 'sab!', 'ḥzaw ʔummānūtī!', 'zkayn!', 'way lī…', 'mannū neqrob ʕammī?', 'nqīm qyāmā.', 'lā lʕālam!', 'yāheb nā lāk ʔīqārā!', 'ṭaybūtāk, māry!'],
  ka: ['დიახ, ბატონო!', 'ახლავე!', 'შეკრიბეთ ლაშქარი!', 'ივარჯიშეთ!', 'წავალ და მოვძებნი.', 'წავედით!', 'ყველამ, შეუტიეთ!', 'საუცხოოა!', 'მაპატიეთ…', 'წინ!|გაუმარჯოს!', 'უკან დაიხიეთ!', 'აჰა!', 'იხილეთ ჩემი ხელოვნება!', 'გავიმარჯვეთ!', 'ვაიმე…', 'ვინ შემებრძოლება?', 'მოკავშირეები ვიყოთ.', 'არასოდეს!', 'წოდებას გიბოძებ!', 'მადლობა, ბატონო!'],
  ar: ['سمعاً وطاعة!', 'حالاً!', 'اجمعوا الجند!', 'تدرّبوا!', 'سأبحث عن الأكفاء.', 'هيا بنا!', 'يا قوم، احملوا!', 'تمّ الأمر!', 'عفواً…', 'إلى الأمام!|احملوا!', 'تراجعوا!', 'خذها!', 'انظر إلى مهارتي!', 'انتصرنا!', 'يا للخسارة…', 'من يبارزني؟', 'فلنتحالف.', 'أبداً!', 'أمنحك لقباً!', 'شكراً يا مولاي!'],
  gez: ['እወ፡ እግዚእየ!', 'ይእዜ እገብር!', 'አስተጋብኡ ሐራ!', 'ተመሀሩ!', 'አሐውር ወአኀሥሥ።', 'ንሑር!', 'ኵልክሙ፡ ተቃተሉ!', 'ሠናይ!', 'ስረይ ሊተ…', 'ሞዐ!', 'ተመየጡ!', 'ንሣእ!', 'ርኢ ጥበብየ!', 'ሞዕነ!', 'አሌ ሊተ…', 'መኑ ይትቃተለኒ?', 'ንግበር ኪዳነ።', 'አልቦ!', 'እሁበከ ክብረ!', 'ብሩክ፡ ንጉሥ!'],
  la: ['Ita, domine!|Fiat!', 'Statim faciam!', 'Milites conscribite!', 'Exercemini!', 'Viros idoneos quaeram.', 'Proficiscimur!', 'Omnes, impetum facite!', 'Optime factum!', 'Ignosce mihi…', 'Ad arma!|Feri!', 'Receptui canite!', 'Hoc habe!', 'Ecce artem meam!', 'Vicimus!', 'Vae mihi…', 'Quis mecum pugnabit?', 'Foedus faciamus.', 'Numquam!', 'Te honore orno!', 'Gratias tibi ago, domine!'],
  grc: ['Ναί, δέσποτα!', 'Εὐθέως ποιήσω!', 'Στρατολογεῖτε!', 'Γυμνάζεσθε!', 'Ζητήσω ἄνδρας ἀγαθούς.', 'Ἄγωμεν!', 'Πάντες, ὁρμᾶτε!', 'Κάλλιστα!', 'Συγγνώμην…', 'Ἀλαλαί!|Ὁρμᾶτε!', 'Ἀναχωρεῖτε!', 'Λαβέ!', 'Ἰδοὺ ἡ τέχνη μου!', 'Νενικήκαμεν!', 'Οἴμοι…', 'Τίς μοι μαχεῖται;', 'Σπονδὰς ποιησώμεθα.', 'Οὐδέποτε!', 'Τιμῶ σε ἀξιώματι!', 'Χάρις σοι, δέσποτα!'],
  got: ['Ja, frauja!', 'Suns taujau!', 'Galaþoþ hari!', 'Arbaidjiþ!', 'Sokja mannans godans.', 'Gaggam!', 'Gaggiþ du waihjon!', 'Waila!', 'Wai…', '~roar|Gaggiþ!', 'Gawandeiþ!', 'Nim!', 'Saiƕ!', 'Sigis!', 'Wai mis…', 'Ƕas weihiþ miþ mis?', 'Gawairþi!', 'Ni!', 'Giba þus mizdon!', 'Awiliuþ, frauja!'],
  cy: ['Ie, arglwydd!', 'Ar unwaith!', 'Casglwch y llu!', 'Ymarferwch!', 'Af i chwilio.', 'Ymaith!', 'Pawb, ymosodwch!', 'Campus!', "Mae'n ddrwg gen i…", "I'r gad!", 'Encilio!', 'Cymer hwn!', 'Gwelwch fy nawn!', 'Buddugoliaeth!', 'Gwae fi…', 'Pwy a ymladd â mi?', 'Byddwn gyfeillion.', 'Byth!', 'Rhoddaf it urddas!', 'Diolch, arglwydd!'],
  ga: ['Sea, a thiarna!', 'Láithreach!', 'Bailígí an slua!', 'Cleachtaigí!', 'Rachaidh mé ar lorg.', 'Ar aghaidh linn!', 'Ionsaígí go léir!', 'Ar fheabhas!', 'Is oth liom…', 'Abú!', 'Cúlaígí!', 'Seo duit!', 'Féach ar mo chumas!', 'Bua!', 'Mo léan…', 'Cé a throidfidh liom?', 'Bímis inár gcairde.', 'Choíche!', 'Bronnaim céim ort!', 'Go raibh maith agat, a thiarna!'],
  // ---- C languages: attested words only; the rest are procedural cries, the subtitle carries the meaning ----
  txb: ['Walo!', 'Kartse!', '~shout', '~shout', '~hm', '~shout', 'Walo! ~roar', 'Kartse!', '~sigh', '~roar', '~shout', '~shout', 'Ñakte!', 'Kartse! Kartse!', '~sigh', '~roar', 'Kartse.', '~no', 'Walo!', 'Walo!'],
  xbc: ['Bago šao!', '~hm', '~shout', '~shout', '~hm', '~shout', 'Šaonanošao!', '~cheer', '~sigh', 'Šaonanošao!', '~shout', '~shout', 'Bago!', 'Šaonanošao!', '~sigh', '~roar', '~hm', '~no', 'Bago šao!', 'Bago šao!'],
  xmr: ['Qore!', '~hm', '~shout', '~shout', '~hm', '~shout', 'Apedemak!', 'Amani!', '~sigh', 'Apedemak!', '~shout', '~shout', 'Apedemak!', 'Amani! Amani!', '~sigh', 'Apedemak!', 'Qore.', '~no', 'Qore!', 'Qore! Qore!'],
  ber: ['Ih, agellid!', 'Ih!', '~shout', '~shout', '~hm', 'Ɣer zdat!', 'Ɣer zdat!', 'Agellid!', '~sigh', 'Ɣer zdat!', '~shout', '~shout', '~roar', 'Agellid! Agellid!', '~sigh', '~roar', 'Azul.', 'Ala!', 'Agellid!', 'Tanemmirt, agellid!'],
  os:  ['Хорз, æлдар!', 'Хорз!', '~shout', '~shout', '~hm', 'Размæ!', 'Размæ!', 'Хорз!', '~sigh', 'Размæ!', '~shout', '~shout', '~roar', 'Уæлахиз!', '~sigh', '~roar', 'Хорз.', 'Нæ!', 'Æлдар!', 'Бузныг, æлдар!'],
};
// ruler interjection overrides (title-words of the court language, spoken before / instead of the line)
const RULER_TAG = {
  fa: { attack: 'شاهنشاه! به پیش!', victory: 'پیروز!' },        // Parthian / Middle Persian pērōz, šāhān šāh
  ar: { attack: 'المقه! احملوا!', victory: 'حمد المقه!' },        // Sabaean ʾAlmaqah (Saba, Himyar, Hadramawt only)
  pra: { ack: 'महाणुभाव महाराय!' },                               // Niya letters: mahanuava maharaya (Kroraina only)
};

// ------------------------------------------------------------ signature lines (the 42 hand-suite rulers) --
const SIGNATURE = {
  '董卓': '顺我者昌，逆我者亡！', '袁绍': '四世三公，谁敢不从！', '曹操': '周公吐哺，天下归心！', '刘备': '汉室可兴！',
  '孙坚': '江东子弟，随我来！', '袁术': '天命在我！', '公孙瓒': '白马义从，随我冲锋！', '刘表': '保境安民，荆州无忧。',
  '刘焉': '益州，天府之国。', '马腾': '西凉铁骑，所向披靡！', '陶谦': '徐州百姓，托付于君。', '孔融': '座上客常满，樽中酒不空。',
  '韩馥': '冀州……容我三思。', '张鲁': '五斗米道，济世安民。', '张杨': '并州儿郎，随我来！', '刘繇': '扬州之地，岂容外人！',
  '陆康': '庐江城在人在！', '孟获': '南中男儿，谁敢来犯！', '孙策': '小霸王在此！', '刘璋': '益州之民，休养生息。',
  '马超': '锦马超在此！', '公孙康': '辽东自守，静观其变。', '公孙度': '辽东之王，岂受汉制！', '士燮': '交趾四十年太平。',
  '卑弥呼': 'ja:鬼道をもって国を治めん。', '卑弥弓呼': 'ja:女王など認めぬ！', '故国川王': 'ko:백성이 나라의 근본이다!', '首露王': 'ko:구지봉에 하늘의 뜻이 내렸노라!',
  '魁头': 'mn:Мордъё!', '於夫罗': 'kk:Тәңірі жар болсын!', '胡维色迦': 'xbc:Šaonanošao!', '耶若室利': 'pra:जअदु महाराओ!',
  '迦梨迦罗': 'ta:காவிரி எம் உயிர்!', '沃洛吉斯': 'fa:شاهنشاه منم!', '戈契赫尔': 'fa:پارس از آن ماست!', '阿布加尔': 'syc:Māry ʔAbgar malkā!',
  '加达拉特': 'gez:ንጉሠ አክሱም!', '康茂德': 'la:Hercules Romanus sum!', '阿根托科克斯': "cy:Ni ddaw Rhufain i'r mynyddoedd!", '巴洛马尔': 'got:Hails, þiudan!',
  '菲利默': 'got:Gaggam!', '区连': 'ms:Kami bebas!',
};

// ------------------------------------------------------------ build LINES --
const LINES = { zh: {} };
for (const [id, c] of Object.entries(CUES)) LINES.zh[id] = c.zh;
for (const [lg, arr] of Object.entries(T)) {
  LINES[lg] = {};
  CORE.forEach((id, i) => { LINES[lg][id] = String(arr[i]).split('|'); });
}

// ------------------------------------------------------------ Node check --
const SPEED = { zh: 4.6, ja: 7.5, ko: 5.5, vi: 4.4, ms: 5.5, mn: 11, kk: 11, pra: 11, sa: 11, ta: 11, si: 11, fa: 11, syc: 12, ka: 12, ar: 11, gez: 6, la: 13, grc: 12, got: 12, cy: 13, ga: 13, txb: 10, xbc: 10, xmr: 10, ber: 10, os: 10 };
// SPEED = characters per second at the bark delivery rate (measured on the tts-eval clips: zh ≈ 4.6 汉字/s,
// ja ≈ 7.5 kana/s, Latin script ≈ 12–13 letters/s); used only for the size estimate
function check() {
  const fails = [];
  const ids = Object.keys(CUES);
  for (const id of CORE) if (!CUES[id] || !CUES[id].core) fails.push('core cue missing in catalogue: ' + id);
  for (const [lg] of Object.entries(LINGUA)) {
    if (!LINES[lg]) { fails.push('no lines for lingua ' + lg); continue; }
    for (const id of CORE) if (!LINES[lg][id] || !LINES[lg][id].length || LINES[lg][id].some(s => !s)) fails.push(`${lg}: core cue ${id} missing`);
  }
  for (const [id, c] of Object.entries(CUES)) {
    if (!c.core) {
      let f = c.fb, guard = 0;
      while (f && !f.startsWith('~') && !(CUES[f] && CUES[f].core) && guard++ < 5) f = CUES[f] && CUES[f].fb;
      if (!f || (!f.startsWith('~') && !(CUES[f] && CUES[f].core))) fails.push(`cue ${id}: fallback chain does not end in a core cue or a cry`);
    }
    if (!c.en) fails.push('cue ' + id + ' has no gloss');
  }
  for (const f of Object.values(FACTION_LINGUA)) if (!LINGUA[f]) fails.push('faction lingua unknown ' + f);
  for (const [r, s] of Object.entries(SIGNATURE)) { const m = /^([a-z]+):/.exec(s); if (m && !LINGUA[m[1]]) fails.push('signature lingua unknown ' + r); }
  // clip count, duration, size (Opus 16 kbps mono ≈ 2.2 KB/s incl. container, measured §B6.4)
  let clips = 0, secs = 0, cries = 0;
  const per = [];
  for (const [lg, L] of Object.entries(LINGUA)) {
    const voices = Object.entries(L.voices).filter(([k, v]) => v && !/^(m2|y|saka|mBattle)$/.test(k)).length + (L.voices.mBattle ? 1 : 0);
    let n = 0, s = 0;
    for (const lines of Object.values(LINES[lg])) for (const t of lines) {
      if (t.startsWith('~')) { cries++; continue; }
      n++; s += 0.25 + [...t.replace(/~\w+/g, '').replace(/[\s,.!?…，。！？、;:·|]/g, '')].length / (SPEED[lg] || 10);
    }
    clips += n * voices; secs += s * voices;
    per.push([lg, L.conf, voices, n, +(s * voices).toFixed(0), +(s * voices * 2.2).toFixed(0)]);
  }
  const sig = Object.keys(SIGNATURE).length;
  clips += sig; secs += sig * 2.2;
  console.log('lingua conf voices lines  sec   KB(16k)');
  for (const r of per) console.log(r.map((x, i) => String(x).padEnd([7, 5, 7, 6, 6, 6][i])).join(''));
  console.log(`cues: ${ids.length} (${CORE.length} core); linguas ${Object.keys(LINGUA).length}; procedural cries in C languages: ${cries}`);
  console.log(`clips ${clips}  ≈ ${(secs / 60).toFixed(1)} min  ≈ ${(secs * 2.2 / 1024).toFixed(2)} MB at Opus 16 kbps  (MP3 32 kbps fallback ≈ ${(secs * 4.1 / 1024).toFixed(2)} MB)`);
  console.log(fails.length ? 'FAIL\n  ' + fails.join('\n  ') : 'ALL OK');
  process.exitCode = fails.length ? 1 : 0;
}
module.exports = { LINGUA, FACTION_LINGUA, CULTURE_LINGUA, SAKA_VOICE, CUES, CORE, LINES, RULER_TAG, SIGNATURE };
if (require.main === module) check();
