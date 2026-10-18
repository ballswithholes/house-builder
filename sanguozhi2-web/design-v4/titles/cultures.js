'use strict';
/* ==========================================================================
   design-v4/titles/cultures.js —— 各文化的官爵名称、原文、可信度与出处（只有名称，没有数值）
   由 build-titles-json.js 组装进 design-v4/titles.json。
   名称条目 n(zh, orig, attest, src[], extra?)：
     zh      游戏显示名（简体，≤ 7 字）
     orig    原文 / 转写（详情页小字）
     attest  A = 该文化在 190 年前后（±60 年）确有此称；B = 该文化确有此称但年代较晚或较早（时代错置，详情页注明）；
             C = 为游戏所拟的描述性称呼（无原文依据）
     src     SOURCES 的键（A / B 必填）
     extra   { rank: 秩 / 品级说明, note: 史实说明（详情页显示）, seal: 覆盖该品阶的印信样式 }
   一个职位可有多个名称：第一个是默认名；其余是「名号可选」（同一席位授官时玩家可挑，电脑取第一个空着的）。
   职位 seats > 1 时，同一势力内的持有者各取一个不同的名称（名称不够时共用默认名）。
   缺项（partial: true 的文化）逐个职位回退到 FALLBACK 指定的文化，最后回退到 generic。
   ========================================================================== */

function n(zh, orig, attest, src, extra) { return Object.assign({ zh, orig: orig || '', attest, src: src || [] }, extra || {}); }
function p(...names) { return { names }; }

// ------------------------------------------------------------------ 出处 --
// WebFetch 在本次会话中无法打开维基 / 维基文库 / ctext（代理 403），故 url 只列检索时确认可达或在检索结果中出现的地址；
// cite 写明原典或学术参考，供合并前的事实核查（TITLES.md §12 列出待核对项）。
const SOURCES = {
  'hhs-bgz': { cite: '《后汉书》志第二十四—二十八·百官志（司马彪《续汉书》）：将军「比公者四：第一大将军，次骠骑将军，次车骑将军，次卫将军；又有前、后、左、右将军」；大将军营五部，部校尉一人比二千石、军司马一人比千石，曲军候比六百石，屯长比二百石；北军五校尉；光禄勋属五官 / 左 / 右 / 虎贲 / 羽林中郎将比二千石、谒者仆射比千石；九卿中二千石；尚书令千石、尚书六百石；州刺史六百石。', url: 'https://zh.wikisource.org/wiki/後漢書/志第二十四' },
  'hs-bgb': { cite: '《汉书·百官公卿表》：相国、丞相、太尉金印紫绶；「凡吏秩比二千石以上，皆银印青绶」「秩比六百石以上，皆铜印黑绶」「比二百石以上，皆铜印黄绶」。汉典「墨绶」条引表文。', url: 'https://www.zdic.net/hans/墨綬' },
  'hanseal': { cite: '孙正军《汉武帝朝的秩级整理运动》（《文史哲》2020）：比秩、中二千石、真二千石的形成；《续汉书·舆服志》九卿、中二千石、二千石青绶。', url: 'https://www.thepaper.cn/newsDetail_forward_9420956' },
  'nine-ministers': { cite: 'Nine Ministers / Government of the Han dynasty（大鸿胪掌诸侯与归义蛮夷，大司农掌钱谷）；Chinese Notes「大鸿胪」词条。', url: 'https://en.wikipedia.org/wiki/Nine_Ministers' },
  'sgz-shu': { cite: '《三国志·蜀书》：诸葛亮传（赤壁后为军师中郎将；成都平，为军师将军，署左将军府事；221 年丞相）；庞统传（与亮并为军师中郎将）；关羽传（建安五年拜偏将军；后为荡寇将军、襄阳太守；219 年前将军）；简雍传（与麋竺、孙乾同为从事中郎，常为谈客，往来使命）。' },
  'sgz-wei': { cite: '《三国志·魏书》：武帝纪（190 年行奋武将军；208 年为丞相）；荀彧传（守尚书令）；荀攸传（太祖以攸为军师）；郭嘉传（表为司空军祭酒）；任峻传（以峻为典农中郎将，募民屯田）；于禁传（虎威将军）；王允于初平元年（190）为司徒（后汉书·王允传）。' },
  'sgz-wu': { cite: '《三国志·吴书》：孙坚传（袁术表坚行破虏将军）；孙策传（199 年讨逆将军）。' },
  'hhs-yuan': { cite: '《后汉书·袁绍传》：建安二年（197）袁绍为大将军；何进中平六年（189）为大将军（后汉书·何进传）。' },
  'sgz-dongyi': { cite: '《三国志·魏书·乌丸鲜卑东夷传》：高句丽「其官有相加、对卢、沛者、古雏加、主簿、优台丞、使者、皂衣先人」「置对卢则不置沛者」；夫余「以六畜名官，有马加、牛加、猪加、狗加、大使、大使者、使者」；倭人条（见 wajinden）。《南史》卷七十九高句丽条有同一官名表（古邹加、优台，无「丞」字）。', url: 'https://zh.wikisource.org/wiki/南史/卷79' },
  'wajinden': { cite: '《三国志·魏书·东夷传·倭人条》（魏志倭人传）：对马、一支等国「大官曰卑狗，副曰卑奴母离」；邪马台「官有伊支马，次曰弥马升，次曰弥马获支，次曰奴佳鞮」；「自女王国以北，特置一大率检察诸国，诸国畏惮之，常治伊都国，于国中有如刺史」；「使大倭监之」（市）；狗奴国「官有狗古智卑狗」；使者「自称大夫」；景初三年（239）难升米为率善中郎将、都市牛利为率善校尉。京都大学论文论三颗印（率善印属魏晋授周边诸族印的最低一等）。', url: 'https://en.wikipedia.org/wiki/Wajinden' },
  'wa-seal': { cite: '京都大学学术报告：魏志倭人传所载三印（亲魏倭王、率善中郎将、率善校尉）。', url: 'https://repository.kulib.kyoto-u.ac.jp/dspace/handle/2433/240136' },
  'songshu-wa': { cite: '《宋书·夷蛮传》：倭王武（478）自称并受封「使持节都督倭新罗任那加罗秦韩慕韩六国诸军事安东大将军倭王」。稻荷山铁剑铭（471）「治天下獲加多支鹵大王」。时代晚于 190 年（B）。' },
  'yamato-kabane': { cite: '大和朝廷的大臣（Ōomi）、大连（Ōmuraji）、物部、靫负、卜部、祝：《日本书纪》所载，5—6 世纪制度（B）。' },
  'ssgs': { cite: '《三国史记》：高句丽大武神王以乙豆智为右辅、左辅「委以军国之事」；新大王二年（166）改左右辅为国相，明临答夫为国相；故国川王十三年（191）乙巴素为国相，十六年（194）行赈贷法；新罗儒理尼师今九年设十七官等（伊伐飡至造位，传说年代）；百济古尔王二十七年（260）置六佐平、十六官品；上佐平见阿莘王时（408）；新罗兵部令（516）、上大等（531）、仓部、礼部（6 世纪）。本库研究：research/v3/east-steppe-sea.md（伊伐飡兼知兵马事、国相乙巴素）。' },
  'goguryeo-late': { cite: '《翰苑》引《高丽记》、《旧唐书·高丽传》：大模达、末客、大对卢、太大兄、莫离支（渊盖苏文）、傉萨（大城置傉萨一，比都督）。均为 6—7 世纪制度（B）。' },
  'shiji-xn': { cite: '《史记·匈奴列传》：置左右贤王、左右谷蠡王、左右大将、左右大都尉、左右大当户、左右骨都侯；「大者万骑，小者数千，凡二十四长，立号曰万骑」；「诸二十四长亦各自置千长、百长、什长、裨小王、相、封都尉、当户、且渠之属」。汉典「什长」条；Chinese Notes 史记卷一一〇词表。', url: 'https://www.zdic.net/hans/什長' },
  'hs-xiongnu': { cite: '《汉书·匈奴传》《汉书·西域传》：日逐王置僮仆都尉，领西域，赋税诸国；《后汉书·南匈奴列传》：异姓大臣左右骨都侯辅政。' },
  'hhs-xianbei': { cite: '《后汉书·乌桓鲜卑列传》：乌桓「邑落各有小帅」「大人」；檀石槐分其地为东、中、西三部，各置大人主之。' },
  'hs-xiyu': { cite: '《汉书·西域传》：诸国「自译长、城长、君、监、吏、大禄、百长、千长、都尉、且渠、当户、将、相至侯、王，皆佩汉印绶，凡三百七十六人」；龟兹有大都尉丞、辅国侯、安国侯、击胡侯、却胡都尉、击车师都尉、左右将、左右都尉、左右骑君、左右力辅君、东西南北部千长、却胡君、译长；鄯善有击车师都尉。研究：译长为译人之长，每国一至四名。', url: 'https://www.rujiazg.com/index.php/article/31356' },
  'xiyu-origin': { cite: '论西域诸国职官的来源（汉制、匈奴制与本土传统共同作用：都尉、将出于汉；且渠、当户出于匈奴；大禄、监为本土）。', url: 'https://www.sinoss.net/c/2013-04-26/527189.shtml' },
  'niya': { cite: '尼雅佉卢文书（鄯善 / 楼兰国，3 世纪）：cojhbo（州长）、sothamga（税吏）、ogu（高官）、kala（王子）；国王称 mahanuava maharaya … devaputra（Burrow 1940 译本）。' },
  'iranica-spahbed': { cite: 'Encyclopaedia Iranica「Spahbed」：帕提亚七大家族之一世袭的军队统帅称号，萨珊沿用。', url: 'https://iranicaonline.org/articles/spahbed' },
  'iranica-bidaxsh': { cite: 'Encyclopaedia Iranica「Bidaxš」：帕提亚时代的位阶，副王 / 边侯，代表国王治理，见于格鲁吉亚阿尔马济铭文。', url: 'https://www.iranicaonline.org/?p=9882' },
  'iranica-argbed': { cite: 'Encyclopaedia Iranica「Argbed」：帕提亚与萨珊的高级称号，多半意为要塞长官；塔巴里记达拉卜吉尔德要塞的 argbed 由帕佩克之子阿尔达希尔继任。', url: 'https://www.iranicaonline.org/articles/argbed' },
  'iranica-hazarbed': { cite: 'Encyclopaedia Iranica「Hazārbed」：见于沙普尔一世卡巴·扎尔多什特三语铭文与纳尔塞的帕伊库利铭文，原形或为 hazāruft，与 spahbed 分立；同条与「Dibīr」条：dibīrbed「首席书记」（ŠKZ 希腊文本 archigrammateus）。', url: 'https://iranicaonline.org/articles/hazarbed' },
  'aspbed': { cite: 'Aspbed：萨珊的骑兵统领称号（B）。', url: 'https://en.wikipedia.org/wiki/Aspbed' },
  'strabo-parthia': { cite: 'Strabo, Geography 11.9.3：帕提亚的议事会由两部分组成，一为王族亲属（syngeneis），一为智者与祭司（sophoi kai magoi），国王由他们拥立。' },
  'parthia-greek': { cite: '帕提亚治下美索不达米亚的希腊语职称：杜拉欧罗普斯文书中的「美索不达米亚与帕拉波塔米亚的 strategos」（2 世纪）。帕提亚钱币「BASILEUS BASILEON」（万王之王）。' },
  'sasanian-court': { cite: '沙普尔一世卡巴·扎尔多什特铭文（ŠKZ，约 262 年）所列帕佩克与阿尔达希尔朝廷：argbed、hazārbed、dibīrbed、ganzwar（司库）、framādār；wuzurg framādār（大宰相）、pushtigbān-sālār（卫队长）、mowbed（祭司长）、āmārgar（计吏）、šahrab 为萨珊制度（B）。' },
  'kushan-yabgu': { cite: 'Encyclopaedia Iranica「Jabguya」：丘就却钱币上的 yavuga / zaoou 即汉文「翕侯」；《汉书·西域传》大月氏五翕侯。', url: 'https://iranicaonline.org/articles/jabguya' },
  'kushan-epi': { cite: '贵霜铭文：鹿野苑迦腻色伽三年铭（大总督 mahākṣatrapa 卡拉帕拉纳、总督 kṣatrapa 瓦纳斯帕拉）；马尼基亚拉铭文（daṇḍanāyaka 拉拉）；苏尔赫科塔尔与拉巴塔克大夏文铭文（karalrang 边疆长官诺孔佐科、沙法尔）；王号 mahārāja rājātirāja devaputra。本库研究 research/v3/india-kushan.md（诺孔佐科为边疆长官、骑兵长，约 158 年主持修复神庙）。华盛顿大学丝路展「贵霜」。', url: 'https://depts.washington.edu/silkroad/exhibit/kushans/essay.html' },
  'satavahana': { cite: '百乘王朝铭文：amātya（amaca，掌一 āhāra 的官员）、mahāsenāpati、senāpati、mahārathi 与 mahābhoja（藩属称号）、mahattaraka、mahātalavara、bhāṇḍāgārika（司库）；纳西克第 20 窟铭文记耶若室利在位第 7 年（约 180 年）大将军（mahāsenāpati）跋婆瞿波之妻婆薮完成石窟（本库 research/v3/india-kushan.md）；纳西克铭文中的行会（śreṇī）。', url: 'https://iasexamportal.com/civilservices/Study-Material/Indian-History-pg14' },
  'arthashastra': { cite: '《政事论》（Arthaśāstra）：gulma 队长、hastyadhyakṣa（象军监）、dūta（使者）、mantrin、purohita、mahāmātra（成书年代有争议，B）。' },
  'gupta-titles': { cite: 'sandhivigrahika（和战大臣）为笈多时代（4 世纪起）的称号（B）。' },
  'sangam': { cite: '桑伽姆时代的五大议（Aimperunkuzhu）：amaichar（大臣）、purohitar（祭司）、thuthar / dūtar（使者）、senapatiyar（将军）、orrar（侦探）；CBC 桑伽姆页；Drishti IAS 笔记。enadi（授予将领的称号）、kaviti（授予官员的称号）、etti（授予富商的称号）见印度通行教材摘要，待核。', url: 'https://cbc.gov.in/cbcdev/sangam/sangam.html' },
  'lanka-brahmi': { cite: '斯里兰卡早期婆罗米文铭文：parumaka（首领）、gamika（村长）；《大史》中的 senāpati / senevi（将军）、amacca（大臣）。' },
  'roman-army': { cite: '罗马军团职阶：legatus legionis、tribunus laticlavius、praefectus castrorum（多由首席百夫长 primus pilus 升任）、tribuni angusticlavii、centurio；辅军 praefectus cohortis / alae；vexillatio 分遣队由 praepositus 统领。', url: 'https://en.wikipedia.org/wiki/Centurion' },
  'roman-equites': { cite: '骑士阶层官职：praefectus praetorio、praefectus annonae、praefectus Aegypti、praefectus vigilum；2 世纪 ab epistulis、a libellis 主官皆为骑士；a rationibus（财务）；Livius「Prefect」；CSUN「Equestrian Bureaucrats」；Strachan 历任表。', url: 'https://www.livius.org/articles/concept/prefect/' },
  'roman-misc': { cite: 'frumentarii（2 世纪起的军中信使 / 密探，驻 castra peregrina）；beneficiarius（军中参谋 / 驻站士官）；comites Augusti（马可·奥勒留出征的随驾参谋）；consiliarii（哈德良起的御前顾问，有俸）；quaestor、procurator、proconsul、praefectus urbi、consul ordinarius；康茂德朝近卫军长官佩伦尼斯、克里安德（a pugione）、莱图斯（192）。' },
  'tacitus-germania': { cite: 'Tacitus, Germania 7（reges ex nobilitate, duces ex virtute）、10（祭司与占卜）、12（principes 在各 pagus 断案，各有百人陪审 centeni）、13–14（comites / comitatus 亲兵）；Historiae 4.61（女先知维勒达）。' },
  'gothic': { cite: '乌菲拉《哥特文圣经》（4 世纪）：þiudans（王）、reiks（统治者）、kindins（总督 / 长官）、hundafaþs（百夫长）、þūsundifaþs（千夫长）（B）；古英语 heretoga（军队首领）。' },
  'irish-law': { cite: '早期爱尔兰法律文献（7—8 世纪，B）：rí túaithe、ruiri、rí ruirech、ard rí；tánaise ríg（王储）；aire 等级（bó-aire、aire déso、aire forgill）；ollam、fili、brithem、druí、rechtaire（管家）、techtaire（使者）；fían 与 rígfénnid、trénfher（冠军）、curad（勇士）。凯撒《高卢战记》：druides、vergobretus。' },
  'bosporus': { cite: '博斯普鲁斯王国希腊文铭文（CIRB）：chiliarchos、nauarchos、politarches、aularches、grammateus、ho epi tes basileias（国务总管）；索罗马特斯二世（172—210）与 193 年塔奈斯铭文。部分称号年代与职掌待核（B）。', url: 'https://en.wikipedia.org/wiki/Sauromates_II' },
  'baquates': { cite: '沃吕比利斯的罗马祭坛铭文：「princeps gentis Baquatium」（巴夸特族首领）与罗马总督的会盟（2—3 世纪）。' },
  'meroe': { cite: '下努比亚麦罗埃铭文：pesheto（下努比亚总督，常为王子）、pelmes（strategos，分「水」与「陆 / 沙漠」）、apote（使者）；菲莱 416 号世俗体铭文为麦罗埃使者赴罗马埃及的自述（3 世纪）。qore（王）、kandake（王后 / 王太后）。', url: 'https://isac.uchicago.edu/sites/oi.uchicago.edu/files/uploads/shared/docs/bumbaugh_2009_diss_proposal.pdf' },
  'qore': { cite: 'Qore：麦罗埃时期库施统治者的称号，不分男女。', url: 'https://en.wikipedia.org/wiki/Qore_(title)' },
  'aksum': { cite: '阿克苏姆铭文（RIE）：negus、negusa nagast（王中之王，3—4 世纪埃扎纳铭文）；ḥaḍani（王族摄政 / 统帅称号，较晚，B）；baḥr nagāš（海疆之王，更晚，B）。' },
  'hatra': { cite: 'Hatran inscriptions：早期统治者称 marya（主），170 年代起称 malka（王），多作「阿拉伯之王」；铭文 450 余件（1—3 世纪）。nuhadra（统军）、rabbaita（管家）等职称见 Bahaa Amer 哈特拉阿拉姆语铭文词汇研究（B，待核）。', url: 'https://en.wikipedia.org/wiki/Hatran_inscriptions' },
  'arab-epi': { cite: '纳巴泰与帕尔米拉的希腊 / 阿拉姆语职称：strategos、hipparchos、synodiarches（rb šyrtʾ 商队长）、agoranomos；萨巴语铭文：qyl（qayl，部族领主）、kbr（kabir，总管 / 长官）、mqtwy（侍从武官）（B，待核）。' },
  'funan': { cite: '《梁书·诸夷传·扶南国》：范蔓（范师蔓）「为大将」，国人共举为王，「自号扶南大王」，作大船攻伐旁国；混填为外国婆罗门；王出乘象。《晋书·林邑传》：区连为功曹之子，杀县令自立。' },
  'nanzhong': { cite: '《华阳国志·南中志》《三国志·蜀书》：南中「夷帅」「渠帅」「耆帅」「大姓」「部曲」，俗好巫鬼。' },
};

// ------------------------------------------------------------ 各文化 --
// rulerRanks：君主位阶 R1–R5 的称号（只是称呼；规则相同）。insignia：五个品阶的印信 / 徽章（图标母题）。
const CULTURES = {
  han: {
    zh: '汉', rulerRanks: ['郡守', '州牧', '公', '王', '皇帝'],
    insignia: ['铜印黄绶', '铜印黑绶', '银印青绶', '金印紫绶', '金印紫绶·龟钮'],
    insigniaSrc: ['hs-bgb', 'hanseal'],
    posts: {
      w1: p(n('军司马', 'jūn sīmǎ', 'A', ['hhs-bgz'], { rank: '比千石', note: '大将军营每部设校尉一人、军司马一人。' }), n('别部司马', 'biébù sīmǎ', 'A', ['sgz-wu', 'sgz-shu'], { note: '孙坚、刘备起兵之初皆为别部司马。' })),
      w2: p(n('越骑校尉', 'yuèqí xiàowèi', 'A', ['hhs-bgz'], { rank: '比二千石' }), n('屯骑校尉', '', 'A', ['hhs-bgz'], { rank: '比二千石' }), n('步兵校尉', '', 'A', ['hhs-bgz'], { rank: '比二千石' }), n('长水校尉', '', 'A', ['hhs-bgz'], { rank: '比二千石' }), n('射声校尉', '', 'A', ['hhs-bgz'], { rank: '比二千石', note: '北军五校，掌宿卫兵。' })),
      w3a: p(n('偏将军', 'piān jiāngjūn', 'A', ['sgz-shu'], { note: '建安五年关羽拜偏将军。' }), n('裨将军', 'pí jiāngjūn', 'A', ['sgz-shu'])),
      w3b: p(n('虎贲中郎将', 'hǔbēn zhōngláng jiàng', 'A', ['hhs-bgz'], { rank: '比二千石', note: '主虎贲宿卫。' }), n('羽林中郎将', '', 'A', ['hhs-bgz'], { rank: '比二千石' }), n('五官中郎将', '', 'A', ['hhs-bgz'], { rank: '比二千石' })),
      w4a: p(n('前将军', 'qián jiāngjūn', 'A', ['hhs-bgz'], { note: '219 年关羽为前将军。', seal: '金印紫绶' }), n('后将军', '', 'A', ['hhs-bgz']), n('左将军', '', 'A', ['hhs-bgz'], { note: '刘备曾为左将军。' }), n('右将军', '', 'A', ['hhs-bgz'])),
      w4b: p(n('破虏将军', 'pòlǔ jiāngjūn', 'A', ['sgz-wu'], { note: '袁术表孙坚行破虏将军。' }), n('讨逆将军', '', 'A', ['sgz-wu'], { note: '孙策。' }), n('荡寇将军', '', 'A', ['sgz-shu'], { note: '关羽。' }), n('奋武将军', '', 'A', ['sgz-wei'], { note: '190 年曹操行奋武将军。' }), n('虎威将军', '', 'A', ['sgz-wei'], { note: '于禁。' })),
      w5: p(n('大将军', 'dà jiāngjūn', 'A', ['hhs-bgz', 'hhs-yuan'], { rank: '位比三公', note: '何进、袁绍。' }), n('骠骑将军', 'piàoqí jiāngjūn', 'A', ['hhs-bgz']), n('车骑将军', 'chēqí jiāngjūn', 'A', ['hhs-bgz'])),
      i1: p(n('主簿', 'zhǔbù', 'A', ['hhs-bgz']), n('记室', 'jìshì', 'A', ['hhs-bgz'])),
      i2: p(n('参军', 'cānjūn', 'A', ['sgz-wei'], { note: '「参某某军事」。' })),
      i3a: p(n('军师祭酒', 'jūnshī jìjiǔ', 'B', ['sgz-wei'], { note: '郭嘉本传作「司空军祭酒」；「军师祭酒」见后世称呼。' }), n('军祭酒', '', 'A', ['sgz-wei'])),
      i3b: p(n('从事中郎', 'cóngshì zhōngláng', 'A', ['sgz-shu'], { note: '简雍、孙乾、麋竺「常为谈客，往来使命」。' }), n('谒者仆射', 'yèzhě púyè', 'A', ['hhs-bgz'], { rank: '比千石', note: '谒者之长，掌宾赞受事、出使。' })),
      i4a: p(n('军师中郎将', 'jūnshī zhōngláng jiàng', 'A', ['sgz-shu'], { note: '赤壁之后诸葛亮、庞统并为军师中郎将。' })),
      i4b: p(n('大鸿胪', 'dà hónglú', 'A', ['hhs-bgz', 'nine-ministers'], { rank: '中二千石', note: '九卿之一，掌诸侯与四方归义蛮夷。' })),
      i5: p(n('军师将军', 'jūnshī jiāngjūn', 'A', ['sgz-shu'], { note: '214 年诸葛亮为军师将军，署左将军府事。' }), n('军师', 'jūnshī', 'A', ['sgz-wei'], { note: '荀攸。' })),
      p1: p(n('从事', 'cóngshì', 'A', ['hhs-bgz']), n('功曹', 'gōngcáo', 'A', ['hhs-bgz'])),
      p2: p(n('别驾从事', 'biéjià cóngshì', 'A', ['hhs-bgz']), n('治中从事', 'zhìzhōng cóngshì', 'A', ['hhs-bgz'])),
      p3a: p(n('刺史', 'cìshǐ', 'A', ['hhs-bgz'], { rank: '六百石' }), n('尚书', 'shàngshū', 'A', ['hhs-bgz'], { rank: '六百石' })),
      p3b: p(n('典农中郎将', 'diǎnnóng zhōngláng jiàng', 'A', ['sgz-wei'], { note: '196 年任峻为典农中郎将，募民屯田许下。' }), n('司金中郎将', 'sījīn zhōngláng jiàng', 'B', ['sgz-wei'], { note: '魏国司金官，待核。' })),
      p4a: p(n('尚书令', 'shàngshū lìng', 'A', ['hhs-bgz', 'sgz-wei'], { rank: '千石', note: '荀彧守尚书令，居中持重。' })),
      p4b: p(n('大司农', 'dà sīnóng', 'A', ['hhs-bgz', 'nine-ministers'], { rank: '中二千石', note: '九卿之一，掌钱谷。' })),
      p5: p(n('丞相', 'chéngxiàng', 'A', ['hs-bgb', 'sgz-wei'], { rank: '万石', seal: '金印紫绶', note: '208 年曹操为丞相；221 年诸葛亮为丞相。' }), n('司徒', 'sītú', 'A', ['hhs-bgz'], { note: '三公；190 年王允为司徒。' }), n('司空', 'sīkōng', 'A', ['hhs-bgz'])),
    },
  },

  nanman: {
    zh: '南中', rulerRanks: ['寨主', '酋王', '大王', '蛮王', '南中大王'], partial: true,
    insignia: ['骨珠', '铜鼓纹佩', '虎牙项圈', '金臂钏', '铜鼓·金冠'],
    posts: {
      w1: p(n('洞兵头', '', 'C')), w2: p(n('部曲长', '', 'A', ['nanzhong'])), w3a: p(n('渠帅', '', 'A', ['nanzhong'])),
      w3b: p(n('藤甲兵长', '', 'C', [], { note: '藤甲出自《三国演义》。' })), w4a: p(n('夷帅', '', 'A', ['nanzhong'])), w4b: p(n('洞主', '', 'C', [], { note: '「洞主」出自《三国演义》。' })), w5: p(n('大夷帅', '', 'C')),
      i1: p(n('巫', '', 'A', ['nanzhong'])), i2: p(n('鬼主', '', 'B', ['nanzhong'], { note: '西南夷「鬼主」见唐宋记载。' })), i3a: p(n('大鬼主', '', 'B', ['nanzhong'])), i3b: p(n('通事', '', 'C')),
      i4a: p(n('巫师长', '', 'C')), i4b: p(n('南中使者', '', 'C')), i5: p(n('大巫', '', 'C')),
      p1: p(n('寨老', '', 'C')), p2: p(n('大姓', '', 'A', ['nanzhong'])), p3a: p(n('耆帅', '', 'A', ['nanzhong'])), p3b: p(n('盐井长', '', 'C')),
      p4a: p(n('部长', '', 'C')), p4b: p(n('互市长', '', 'C')), p5: p(n('蛮相', '', 'C')),
    },
  },

  korea: {
    zh: '高句丽', rulerRanks: ['君长', '王', '大王', '太王', '天王'],
    insignia: ['鸟羽冠饰', '金铜冠饰', '银花冠', '金花冠', '金冠·玉带'],
    posts: {
      w1: p(n('先人', 'seonin', 'A', ['sgz-dongyi'])), w2: p(n('皂衣', 'joui', 'A', ['sgz-dongyi'])),
      w3a: p(n('小加', 'soga', 'A', ['sgz-dongyi'], { note: '诸「加」为各部贵族，战时各领其众。' })), w3b: p(n('大模达', 'daemodal', 'B', ['goguryeo-late'])),
      w4a: p(n('大加', 'daega', 'A', ['sgz-dongyi'])), w4b: p(n('左辅', 'jwabo', 'B', ['ssgs'], { note: '「委以军国之事」；166 年改为国相。' }), n('右辅', 'ubo', 'B', ['ssgs'])),
      w5: p(n('莫离支', 'mangniji', 'B', ['goguryeo-late'], { note: '7 世纪渊盖苏文所任的最高军政长官。' })),
      i1: p(n('使者', 'saja', 'A', ['sgz-dongyi'])), i2: p(n('主簿', 'jubu', 'A', ['sgz-dongyi'])),
      i3a: p(n('沛者', 'paeja', 'A', ['sgz-dongyi'], { note: '与对卢不并置。' })), i3b: p(n('大使者', 'daesaja', 'B', ['goguryeo-late'])),
      i4a: p(n('对卢', 'daero', 'A', ['sgz-dongyi'])), i4b: p(n('古雏加', 'gochuga', 'A', ['sgz-dongyi'], { note: '王族与前王族的尊号。' })), i5: p(n('相加', 'sangga', 'A', ['sgz-dongyi'])),
      p1: p(n('优台', 'utae', 'A', ['sgz-dongyi'])), p2: p(n('中畏大夫', 'jungoe daebu', 'B', ['ssgs'])),
      p3a: p(n('傉萨', 'yoksal', 'B', ['goguryeo-late'], { note: '大城置傉萨一，比都督（唐代记载）。' })), p3b: p(n('赈贷使', '', 'C', [], { note: '取 194 年国相乙巴素行赈贷法之意。' })),
      p4a: p(n('太大兄', 'taedaehyeong', 'B', ['goguryeo-late'])), p4b: p(n('大对卢', 'daedaero', 'B', ['goguryeo-late'])),
      p5: p(n('国相', 'guksang', 'A', ['ssgs'], { note: '166 年明临答夫首任；191 年乙巴素为国相。' })),
    },
  },

  wa: {
    zh: '倭', rulerRanks: ['首长', '王', '大王', '亲魏倭王', '治天下大王'],
    insignia: ['勾玉', '管玉串', '铜镜', '金印', '三神器纹·金印'],
    insigniaSrc: ['wa-seal'],
    posts: {
      w1: p(n('卑奴母离', 'hinamori', 'A', ['wajinden'], { note: '诸国副官（一说「夷守」）。' })), w2: p(n('卑狗', 'hiko', 'A', ['wajinden'], { note: '诸国大官。' })),
      w3a: p(n('物部', 'mononobe', 'B', ['yamato-kabane'])), w3b: p(n('靫负', 'yugei', 'B', ['yamato-kabane'], { note: '负箭的近卫。' })),
      w4a: p(n('一大率', 'ichi-daisotsu', 'A', ['wajinden'], { note: '常治伊都国，检察诸国，「于国中有如刺史」。' })), w4b: p(n('卫士长', '', 'C')),
      w5: p(n('安东大将军', 'Andō daishōgun', 'B', ['songshu-wa'], { note: '478 年倭王武受封。' })),
      i1: p(n('卜部', 'urabe', 'B', ['yamato-kabane'], { note: '倭人「灼骨而卜」。' })), i2: p(n('祝', 'hafuri', 'B', ['yamato-kabane'])),
      i3a: p(n('鬼道师', '', 'C', [], { note: '取卑弥呼「事鬼道，能惑众」。' })), i3b: p(n('大夫', 'taifu', 'A', ['wajinden'], { note: '倭使「自称大夫」。' })),
      i4a: p(n('传辞使', '', 'C', [], { note: '女王宫中「唯有男子一人给饮食，传辞出入」。' })),
      i4b: p(n('率善校尉', 'sotsuzen kōi', 'A', ['wajinden', 'wa-seal'], { note: '239 年魏授都市牛利。' })),
      i5: p(n('率善中郎将', 'sotsuzen chūrōshō', 'A', ['wajinden', 'wa-seal'], { note: '239 年魏授难升米，银印青绶。' })),
      p1: p(n('奴佳鞮', 'nakato', 'A', ['wajinden'], { note: '邪马台第四官。' })), p2: p(n('弥马获支', 'mimakaki', 'A', ['wajinden'], { note: '邪马台第三官。' })),
      p3a: p(n('弥马升', 'mimasho', 'A', ['wajinden'], { note: '邪马台第二官。' })), p3b: p(n('大倭', 'daiwa', 'A', ['wajinden'], { note: '「国国有市，交易有无，使大倭监之」。' })),
      p4a: p(n('伊支马', 'ikima', 'A', ['wajinden'], { note: '邪马台第一官。' })), p4b: p(n('大连', 'ōmuraji', 'B', ['yamato-kabane'])),
      p5: p(n('大臣', 'ōomi', 'B', ['yamato-kabane'])),
    },
  },

  steppe: {
    zh: '草原', rulerRanks: ['部落首领', '大人', '王', '单于', '大单于'],
    insignia: ['骨镞', '鎏金带扣', '金耳饰', '鹿首金冠饰', '鹰顶金冠'],
    posts: {
      w1: p(n('什长', '', 'A', ['shiji-xn']), n('百长', '', 'A', ['shiji-xn'])), w2: p(n('千长', '', 'A', ['shiji-xn'])),
      w3a: p(n('万骑长', '', 'A', ['shiji-xn'], { note: '二十四长「立号曰万骑」。' })), w3b: p(n('裨小王', '', 'A', ['shiji-xn'])),
      w4a: p(n('左大将', '', 'A', ['shiji-xn']), n('右大将', '', 'A', ['shiji-xn'])), w4b: p(n('左大都尉', '', 'A', ['shiji-xn']), n('右大都尉', '', 'A', ['shiji-xn'])),
      w5: p(n('左贤王', 'tuqi', 'A', ['shiji-xn'], { note: '匈奴谓贤曰「屠耆」，常以太子为左屠耆王。' }), n('右贤王', '', 'A', ['shiji-xn'])),
      i1: p(n('当户', '', 'A', ['shiji-xn'])), i2: p(n('且渠', '', 'A', ['shiji-xn'])), i3a: p(n('骨都侯', '', 'A', ['hs-xiongnu'], { note: '异姓大臣，辅政。' })), i3b: p(n('相', '', 'A', ['shiji-xn'])),
      i4a: p(n('左大当户', '', 'A', ['shiji-xn']), n('右大当户', '', 'A', ['shiji-xn'])), i4b: p(n('左谷蠡王', 'luli', 'A', ['shiji-xn']), n('右谷蠡王', '', 'A', ['shiji-xn'])),
      i5: p(n('日逐王', '', 'A', ['hs-xiongnu'], { note: '领西域，置僮仆都尉。' })),
      p1: p(n('小帅', '', 'A', ['hhs-xianbei'], { note: '乌桓「邑落各有小帅」。' })), p2: p(n('封都尉', '', 'B', ['shiji-xn'], { note: '《史记》此句断句有异说。' })),
      p3a: p(n('部大人', '', 'A', ['hhs-xianbei'])), p3b: p(n('僮仆都尉', '', 'A', ['hs-xiongnu'], { note: '赋税西域诸国。' })),
      p4a: p(n('东部大人', '', 'A', ['hhs-xianbei'], { note: '檀石槐分三部，各置大人。' }), n('中部大人', '', 'A', ['hhs-xianbei']), n('西部大人', '', 'A', ['hhs-xianbei'])),
      p4b: p(n('胡市长', '', 'C', [], { note: '取乌桓、鲜卑与汉互市。' })), p5: p(n('单于庭相国', '', 'C')),
    },
  },

  tarim: {
    zh: '西域', rulerRanks: ['城主', '王', '大王', '王中之王', '天子'],
    insignia: ['汉印·铜', '汉印·铜黑绶', '汉印·银青绶', '汉印·金', '汉印·金紫绶'],
    insigniaSrc: ['hs-xiyu'],
    posts: {
      w1: p(n('百长', '', 'A', ['hs-xiyu'])), w2: p(n('部千长', '', 'A', ['hs-xiyu'], { note: '龟兹东西南北部千长各二人。' })),
      w3a: p(n('左都尉', '', 'A', ['hs-xiyu']), n('右都尉', '', 'A', ['hs-xiyu'])), w3b: p(n('左骑君', '', 'A', ['hs-xiyu']), n('右骑君', '', 'A', ['hs-xiyu'])),
      w4a: p(n('击胡侯', '', 'A', ['hs-xiyu'])), w4b: p(n('击车师都尉', '', 'A', ['hs-xiyu'], { note: '鄯善、龟兹皆有。' })),
      w5: p(n('左将', '', 'A', ['hs-xiyu']), n('右将', '', 'A', ['hs-xiyu'])),
      i1: p(n('译长', '', 'A', ['hs-xiyu'], { note: '译人之长，每国一至四人。' })), i2: p(n('却胡君', '', 'A', ['hs-xiyu'])),
      i3a: p(n('奥古', 'ogu', 'A', ['niya'])), i3b: p(n('力辅君', '', 'A', ['hs-xiyu'])),
      i4a: p(n('却胡都尉', '', 'A', ['hs-xiyu'])), i4b: p(n('侍子', '', 'B', ['hs-xiyu'], { note: '入侍汉廷的王子，通好之本。' })),
      i5: p(n('辅国侯', '', 'A', ['hs-xiyu'])),
      p1: p(n('城长', '', 'A', ['hs-xiyu'])), p2: p(n('税吏', 'sothamga', 'A', ['niya'])),
      p3a: p(n('州长', 'cojhbo', 'A', ['niya'])), p3b: p(n('大禄', '', 'A', ['xiyu-origin'], { note: '本土旧官（乌孙、龟兹等）。' })),
      p4a: p(n('大都尉丞', '', 'A', ['hs-xiyu'])), p4b: p(n('萨宝', 'sartapau', 'B', ['arthashastra'], { note: '粟特商队之长（梵语 sārthavāha），汉文「萨宝」见于北朝。' })),
      p5: p(n('安国侯', '', 'A', ['hs-xiyu'])),
    },
  },

  seasia: {
    zh: '扶南', rulerRanks: ['渠帅', '王', '大王', '扶南大王', '王中之王'], partial: true,
    insignia: ['贝串', '金指环', '金耳珰', '金冠', '金象·伞盖'],
    posts: {
      w1: p(n('百夫', '', 'C')), w2: p(n('将', '', 'C')), w3a: p(n('大将', '', 'A', ['funan'], { note: '范蔓「为大将」，后被拥立为王。' })),
      w3b: p(n('象兵长', '', 'B', ['funan'], { note: '扶南「王出乘象」。' })), w4a: p(n('水军帅', '', 'B', ['funan'], { note: '范蔓「作大船，穷涨海」攻伐旁国。' })),
      w4b: p(n('坡', 'poñ', 'B', ['funan'], { note: '前吴哥时代高棉称号（7 世纪），借用。' })), w5: p(n('大元帅', 'senāpati', 'B', ['arthashastra'])),
      i1: p(n('书记', '', 'C')), i2: p(n('使者', '', 'C')), i3a: p(n('婆罗门师', 'brāhmaṇa', 'A', ['funan'], { note: '混填为外国婆罗门。' })), i3b: p(n('译使', '', 'C')),
      i4a: p(n('国师', 'purohita', 'B', ['arthashastra'])), i4b: p(n('大使', '', 'C')), i5: p(n('辅臣', 'mantrin', 'B', ['arthashastra'])),
      p1: p(n('邑长', '', 'C')), p2: p(n('税官', '', 'C')), p3a: p(n('城主', 'pura', 'C')), p3b: p(n('市舶长', '', 'C', [], { note: '取俄厄港的海上贸易。' })),
      p4a: p(n('大臣', 'amātya', 'B', ['arthashastra'])), p4b: p(n('港务长', '', 'C')), p5: p(n('宰相', '', 'C')),
    },
  },

  kushan: {
    zh: '贵霜', rulerRanks: ['小王', '王', '大王', '王中之王', '天子'],
    rulerRanksOrig: ['', 'rāja', 'mahārāja', 'rājātirāja', 'devaputra'], rulerRanksSrc: ['kushan-epi'],
    insignia: ['铜指环印', '金币纹佩', '火坛纹金佩', '王冠纹金牌', '日轮·天子冠'],
    posts: {
      w1: p(n('百骑长', '', 'C')), w2: p(n('将', 'daṇḍanāyaka', 'B', ['kushan-epi'])),
      w3a: p(n('大将', 'mahādaṇḍanāyaka', 'B', ['kushan-epi'])), w3b: p(n('铁骑长', '', 'C')),
      w4a: p(n('边疆长官', 'karalrang', 'A', ['kushan-epi'], { note: '诺孔佐科、沙法尔。' })), w4b: p(n('翕侯', 'yabgu', 'A', ['kushan-yabgu'], { note: '贵霜本为大月氏五翕侯之一。' })),
      w5: p(n('大元帅', 'mahāsenāpati', 'B', ['satavahana'])),
      i1: p(n('书吏', 'lekhaka', 'B', ['arthashastra'])), i2: p(n('使者', 'dūta', 'B', ['arthashastra'])), i3a: p(n('谋士', 'mantrin', 'B', ['arthashastra'])), i3b: p(n('大使者', 'mahādūta', 'C')),
      i4a: p(n('国师', '', 'B', ['kushan-epi'], { note: '汉译佛典传说马鸣为迦腻色伽之师。' })), i4b: p(n('外藩长', '', 'C')), i5: p(n('大辅臣', 'mahāmantrin', 'B', ['arthashastra'])),
      p1: p(n('村长', 'grāmika', 'B', ['arthashastra'])), p2: p(n('总督', 'kṣatrapa', 'A', ['kushan-epi'])),
      p3a: p(n('大总督', 'mahākṣatrapa', 'A', ['kushan-epi'])), p3b: p(n('司库', 'gañjavara', 'B', ['kushan-epi'])),
      p4a: p(n('大臣', 'amātya', 'B', ['satavahana'])), p4b: p(n('商旅长', 'sārthavāha', 'B', ['arthashastra'])), p5: p(n('宰相', 'mahāmātya', 'B', ['arthashastra'])),
    },
  },

  india: {
    zh: '天竺', rulerRanks: ['小王', '王', '大王', '王中之王', '转轮王'],
    rulerRanksOrig: ['bhoja', 'rāja', 'mahārāja', 'rājarāja', 'cakravartin'],
    insignia: ['铜指环', '银臂钏', '金臂钏', '宝石冠', '白伞盖·宝冠'],
    posts: {
      w1: p(n('队长', 'gaulmika', 'B', ['arthashastra'])), w2: p(n('将', 'senāpati', 'A', ['satavahana'])),
      w3a: p(n('摩诃罗提', 'mahārathi', 'A', ['satavahana'], { note: '百乘的藩属大族。' })), w3b: p(n('象军监', 'hastyadhyakṣa', 'B', ['arthashastra'])),
      w4a: p(n('摩诃波阇', 'mahābhoja', 'A', ['satavahana'])), w4b: p(n('大塔拉瓦拉', 'mahātalavara', 'A', ['satavahana'])),
      w5: p(n('大将军', 'mahāsenāpati', 'A', ['satavahana'], { note: '约 180 年耶若室利的大将军跋婆瞿波。' })),
      i1: p(n('书记', 'lekhaka', 'B', ['arthashastra'])), i2: p(n('使者', 'dūta', 'B', ['arthashastra'])), i3a: p(n('谋臣', 'mantrin', 'B', ['arthashastra'])),
      i3b: p(n('和战大臣', 'sandhivigrahika', 'B', ['gupta-titles'])), i4a: p(n('王师', 'purohita', 'B', ['arthashastra'])), i4b: p(n('王使', 'rājadūta', 'C')),
      i5: p(n('首席谋臣', 'mahāmantrin', 'B', ['arthashastra'])),
      p1: p(n('村长', 'grāmika', 'B', ['arthashastra'])), p2: p(n('村落长', 'mahattaraka', 'A', ['satavahana'])),
      p3a: p(n('大臣', 'amātya', 'A', ['satavahana'], { note: '掌一 āhāra（州郡）。' })), p3b: p(n('司库', 'bhāṇḍāgārika', 'A', ['satavahana'])),
      p4a: p(n('大辅', 'mahāmātra', 'B', ['arthashastra'])), p4b: p(n('行会长', 'śreṣṭhin', 'A', ['satavahana'], { note: '纳西克铭文中的行会。' })),
      p5: p(n('宰相', 'mahāmātya', 'B', ['arthashastra'])),
    },
  },

  persia: {
    zh: '安息', rulerRanks: ['堡主', '王', '大王', '万王之王', '伊朗万王之王'],
    rulerRanksOrig: ['dizpat', 'šāh', 'wuzurg šāh', 'šāhān šāh', 'šāhān šāh ērān'], rulerRanksSrc: ['parthia-greek'],
    insignia: ['玛瑙印章', '银碗纹佩', '金扣带', '提亚拉冠饰', '日月冠·绶带'],
    posts: {
      w1: p(n('骑队长', '', 'C')), w2: p(n('堡垒长', 'argbed', 'A', ['iranica-argbed'], { note: '阿尔达希尔曾任达拉卜吉尔德的 argbed。' })),
      w3a: p(n('方面将军', 'strategos', 'B', ['parthia-greek'])), w3b: p(n('骑兵统领', 'aspbed', 'B', ['aspbed'])),
      w4a: p(n('总督', 'satrap', 'A', ['iranica-bidaxsh'])), w4b: p(n('卫队长', 'pushtigbān-sālār', 'B', ['sasanian-court'])),
      w5: p(n('大将军', 'spāhbed', 'A', ['iranica-spahbed'], { note: '七大家族之一世袭的统帅之职。' })),
      i1: p(n('书吏', 'dibīr', 'B', ['iranica-hazarbed'])), i2: p(n('使者', 'frēstag', 'B', ['sasanian-court'])),
      i3a: p(n('智者议员', 'sophos', 'A', ['strabo-parthia'], { note: '帕提亚议事会的「智者与祭司」。' })), i3b: p(n('王使', '', 'C')),
      i4a: p(n('首席书记', 'dibīrbed', 'B', ['iranica-hazarbed'])), i4b: p(n('祭司长', 'mowbed', 'B', ['sasanian-court'])),
      i5: p(n('大千夫长', 'hazāruft', 'B', ['iranica-hazarbed'], { note: '萨珊初年的宰辅之职。' })),
      p1: p(n('税吏', '', 'C')), p2: p(n('城守', 'šahrab', 'B', ['sasanian-court'])),
      p3a: p(n('副王', 'bidaxš', 'A', ['iranica-bidaxsh'])), p3b: p(n('计吏', 'āmārgar', 'B', ['sasanian-court'])),
      p4a: p(n('大管家', 'framādār', 'B', ['sasanian-court'])), p4b: p(n('司库', 'ganzwar', 'B', ['sasanian-court'])),
      p5: p(n('大宰相', 'wuzurg framādār', 'B', ['sasanian-court'])),
    },
  },

  arab: {
    zh: '阿拉伯', rulerRanks: ['谢赫', '主', '王', '阿拉伯之王', '王中之王'],
    rulerRanksOrig: ['šayḫ', 'marya', 'malka', 'malka dy ʿrb', ''], rulerRanksSrc: ['hatra'], partial: true,
    insignia: ['红玉髓戒', '银护符', '金新月佩', '金冠饰', '鹰顶金冠'],
    posts: {
      w1: p(n('骑兵队长', '', 'C')), w2: p(n('骑兵长', 'hipparchos', 'A', ['arab-epi'])), w3a: p(n('将军', 'strategos', 'A', ['arab-epi'])), w3b: p(n('骆驼骑长', '', 'C')),
      w4a: p(n('统军', 'nuhadra', 'B', ['hatra'])), w4b: p(n('侍从武官', 'maqtawī', 'B', ['arab-epi'])), w5: p(n('大军统帅', 'rab ḥaylā', 'C')),
      i1: p(n('书吏', 'sāprā', 'C')), i2: p(n('使者', '', 'C')), i3a: p(n('卜者', 'kāhin', 'C')), i3b: p(n('商使', '', 'C')),
      i4a: p(n('祭司长', '', 'C')), i4b: p(n('外交使臣', '', 'C')), i5: p(n('谋主', '', 'C')),
      p1: p(n('部族长', '', 'C')), p2: p(n('领主', 'qayl', 'B', ['arab-epi'])), p3a: p(n('总管', 'kabīr', 'B', ['arab-epi'])), p3b: p(n('商队长', 'synodiarches', 'A', ['arab-epi'], { note: '帕尔米拉的 rb šyrtʾ。' })),
      p4a: p(n('管家', 'rabbaita', 'B', ['hatra'])), p4b: p(n('市长', 'agoranomos', 'B', ['arab-epi'])), p5: p(n('王家总管', 'epitropos', 'B', ['arab-epi'])),
    },
  },

  africa: {
    zh: '库施', rulerRanks: ['首领', '王', '大王', '王中之王', '库施与诸地之王'],
    rulerRanksOrig: ['', 'qore', '', '', ''], rulerRanksSrc: ['qore'], partial: true,
    insignia: ['象牙环', '银蛇纹臂钏', '金臂钏', '双蛇冠', '金双蛇冠·权杖'],
    posts: {
      w1: p(n('弓兵队长', '', 'C', [], { note: '库施古称「弓之地」。' })), w2: p(n('百夫', '', 'C')),
      w3a: p(n('陆军将', 'pelmes adab', 'B', ['meroe'])), w3b: p(n('水军将', 'pelmes ate', 'A', ['meroe'])),
      w4a: p(n('将军', 'pelmes', 'A', ['meroe'])), w4b: p(n('弓骑长', '', 'C')), w5: p(n('王子统帅', 'pqr', 'B', ['meroe'])),
      i1: p(n('书吏', '', 'C')), i2: p(n('使者', 'apote', 'A', ['meroe'])), i3a: p(n('祭司', '', 'C')), i3b: p(n('大使者', 'apote-lh', 'B', ['meroe'])),
      i4a: p(n('阿蒙大祭司', '', 'C')), i4b: p(n('赴罗马使', '', 'B', ['meroe'], { note: '菲莱 416 号铭文的麦罗埃使者。' })), i5: p(n('王室谋臣', '', 'C')),
      p1: p(n('村长', '', 'C')), p2: p(n('书记官', '', 'C')), p3a: p(n('总督', 'pesheto', 'A', ['meroe'], { note: '下努比亚总督，常为王子。' })), p3b: p(n('司库', '', 'C')),
      p4a: p(n('太后府长', '', 'C', [], { note: '取 kandake（王太后）的宫府。' })), p4b: p(n('商路长', '', 'C')), p5: p(n('宰相', '', 'C')),
    },
  },

  roman: {
    zh: '罗马', rulerRanks: ['军阀', '统帅', '元首', '恺撒', '奥古斯都'],
    rulerRanksOrig: ['dux', 'imperator', 'princeps', 'Caesar', 'Augustus'], rulerRanksSrc: ['roman-misc'],
    insignia: ['勋牌 phalera', '金项圈 torques', '银矛 hasta pura', '金冠 corona aurea', '紫袍金鹰'],
    posts: {
      w1: p(n('百夫长', 'centurio', 'A', ['roman-army'])), w2: p(n('首席百夫长', 'primus pilus', 'A', ['roman-army']), n('辅军大队长', 'praefectus cohortis', 'A', ['roman-army'])),
      w3a: p(n('军团长', 'legatus legionis', 'A', ['roman-army'])), w3b: p(n('骑兵翼长', 'praefectus alae', 'A', ['roman-army']), n('近卫大队长', 'tribunus cohortis praetoriae', 'A', ['roman-army'])),
      w4a: p(n('行省总督', 'legatus Augusti pro praetore', 'A', ['roman-army'], { note: '统率行省驻军；193 年塞维鲁即以上潘诺尼亚总督起兵。' })),
      w4b: p(n('分遣军统领', 'praepositus vexillationum', 'A', ['roman-army']), n('舰队长官', 'praefectus classis', 'A', ['roman-equites'])),
      w5: p(n('近卫军长官', 'praefectus praetorio', 'A', ['roman-equites', 'roman-misc'], { note: '康茂德朝佩伦尼斯、莱图斯。' })),
      i1: p(n('参谋士官', 'beneficiarius', 'A', ['roman-misc'])), i2: p(n('密使', 'frumentarius', 'A', ['roman-misc'])),
      i3a: p(n('营务长官', 'praefectus castrorum', 'A', ['roman-army'])), i3b: p(n('希腊文书信官', 'ab epistulis Graecis', 'A', ['roman-equites'], { note: '掌东方诸国的往来书信与使节。' })),
      i4a: p(n('随驾参谋', 'comes Augusti', 'A', ['roman-misc'])), i4b: p(n('书信总管', 'ab epistulis', 'A', ['roman-equites'])),
      i5: p(n('御前顾问', 'consiliarius Augusti', 'A', ['roman-misc'])),
      p1: p(n('财务官', 'quaestor', 'A', ['roman-misc'])), p2: p(n('代理官', 'procurator', 'A', ['roman-misc'])),
      p3a: p(n('行省执政', 'proconsul', 'A', ['roman-misc'])), p3b: p(n('粮政长官', 'praefectus annonae', 'A', ['roman-equites'])),
      p4a: p(n('罗马城长官', 'praefectus urbi', 'A', ['roman-misc'])), p4b: p(n('财务总管', 'a rationibus', 'A', ['roman-equites']), n('埃及长官', 'praefectus Aegypti', 'A', ['roman-equites'])),
      p5: p(n('执政官', 'consul ordinarius', 'A', ['roman-misc'])),
    },
  },

  celt: {
    zh: '凯尔特', rulerRanks: ['首领', '王', '大王', '诸王之王', '至高王'],
    rulerRanksOrig: ['', 'rí túaithe', 'ruiri', 'rí ruirech', 'ard rí'], rulerRanksSrc: ['irish-law'],
    insignia: ['铁胸针', '银胸针', '金项环 torc', '金冠', '金项环·宝剑'],
    posts: {
      w1: p(n('战士', 'fénnid', 'B', ['irish-law'])), w2: p(n('勇士', 'curad', 'B', ['irish-law'])),
      w3a: p(n('战团首领', 'rígfénnid', 'B', ['irish-law'])), w3b: p(n('冠军', 'trénfher', 'B', ['irish-law'])),
      w4a: p(n('领主', 'aire forgill', 'B', ['irish-law'])), w4b: p(n('王之护卫', '', 'C')), w5: p(n('王储', 'tánaise ríg', 'B', ['irish-law'])),
      i1: p(n('诗人', 'fili', 'B', ['irish-law'])), i2: p(n('德鲁伊', 'druí', 'A', ['irish-law'], { note: '凯撒记高卢与不列颠的德鲁伊。' })),
      i3a: p(n('大德鲁伊', 'ard-druí', 'B', ['irish-law'])), i3b: p(n('信使', 'techtaire', 'B', ['irish-law'])),
      i4a: p(n('大诗人', 'ollam', 'B', ['irish-law'])), i4b: p(n('法官', 'brithem', 'B', ['irish-law'])), i5: p(n('王之贤者', '', 'C')),
      p1: p(n('牛主', 'bó-aire', 'B', ['irish-law'])), p2: p(n('地主', 'aire déso', 'B', ['irish-law'])),
      p3a: p(n('领地主', 'flaith', 'B', ['irish-law'])), p3b: p(n('牧群长', '', 'C')),
      p4a: p(n('管家', 'rechtaire', 'B', ['irish-law'])), p4b: p(n('贡赋长', '', 'C')), p5: p(n('首席法官', 'vergobretos', 'B', ['irish-law'], { note: '高卢的最高执政官（凯撒）。' })),
    },
  },

  german: {
    zh: '日耳曼', rulerRanks: ['酋长', '王', '大王', '诸部之王', '诸族之王'],
    rulerRanksOrig: ['princeps', 'rex / þiudans', '', '', ''], rulerRanksSrc: ['tacitus-germania', 'gothic'],
    insignia: ['铁臂环', '银臂环', '金臂环', '金颈环', '金冠·长枪'],
    posts: {
      w1: p(n('百人长', 'hundafaþs', 'B', ['gothic'])), w2: p(n('千人长', 'þūsundifaþs', 'B', ['gothic'])),
      w3a: p(n('军酋', 'dux', 'A', ['tacitus-germania'], { note: '「duces ex virtute」。' })), w3b: p(n('亲兵', 'comes', 'A', ['tacitus-germania'], { note: 'comitatus 亲兵团。' })),
      w4a: p(n('统军首领', 'heretoga', 'B', ['gothic'])), w4b: p(n('百人精选', 'centeni', 'A', ['tacitus-germania'])), w5: p(n('战酋', 'dux bellorum', 'C')),
      i1: p(n('卜者', '', 'A', ['tacitus-germania'], { note: '以签卜问吉凶。' })), i2: p(n('祭司', 'sacerdos', 'A', ['tacitus-germania'])),
      i3a: p(n('智者', '', 'C')), i3b: p(n('使者', 'legatus', 'C')), i4a: p(n('女先知', '', 'A', ['tacitus-germania'], { note: '如布鲁克特里的维勒达。' })), i4b: p(n('议事长', '', 'C')), i5: p(n('大祭司', '', 'C')),
      p1: p(n('村长', '', 'C')), p2: p(n('首领', 'princeps', 'A', ['tacitus-germania'], { note: '在各 pagus 断案。' })),
      p3a: p(n('长官', 'kindins', 'B', ['gothic'])), p3b: p(n('牧主', '', 'C')), p4a: p(n('统治者', 'reiks', 'B', ['gothic'])), p4b: p(n('贡赋长', '', 'C')), p5: p(n('大议首', '', 'C')),
    },
  },

  sarmatian: {
    zh: '萨尔马提亚', rulerRanks: ['首领', '王', '大王', '诸王之王', '博斯普鲁斯之王'],
    rulerRanksOrig: ['', 'basileus', '', 'basileus basileōn', ''], partial: true,
    insignia: ['青铜镜', '金扣饰', '金鹿佩', '金冠', '金冠·龙旗'],
    posts: {
      w1: p(n('骑兵什长', '', 'C')), w2: p(n('千夫长', 'chiliarchos', 'A', ['bosporus'])), w3a: p(n('骑兵长', 'hipparchos', 'B', ['bosporus'])), w3b: p(n('铁甲骑长', 'kataphraktos', 'C')),
      w4a: p(n('将军', 'strategos', 'B', ['bosporus'])), w4b: p(n('舰队长', 'nauarchos', 'A', ['bosporus'])), w5: p(n('战王', '', 'C')),
      i1: p(n('书吏', 'grammateus', 'B', ['bosporus'])), i2: p(n('使者', '', 'C')), i3a: p(n('占卜者', '', 'C')), i3b: p(n('通译', 'hermeneus', 'C')),
      i4a: p(n('王之顾问', '', 'C')), i4b: p(n('宾客总管', '', 'C')), i5: p(n('国务总管', 'ho epi tēs basileias', 'B', ['bosporus'])),
      p1: p(n('村长', '', 'C')), p2: p(n('城守', 'politarches', 'B', ['bosporus'])), p3a: p(n('宫廷总管', 'aularches', 'B', ['bosporus'])), p3b: p(n('司库', '', 'C')),
      p4a: p(n('总督', '', 'C')), p4b: p(n('港务长', '', 'C')), p5: p(n('王家总管', '', 'C')),
    },
  },

  moor: {
    zh: '毛里', rulerRanks: ['族长', '王', '大王', '诸族之王', '毛里之王'],
    rulerRanksOrig: ['princeps gentis', 'rex', '', '', ''], rulerRanksSrc: ['baquates'], partial: true,
    insignia: ['皮护符', '银环', '金环', '金冠饰', '金冠·标枪'],
    posts: {
      p2: p(n('族首领', 'princeps gentis', 'A', ['baquates'], { note: '「princeps gentis Baquatium」。' })),
      w3a: p(n('部众统领', 'dux gentis', 'C')),
    },
  },

  // 兜底：描述性称呼（全部 C）
  generic: {
    zh: '通用', rulerRanks: ['首领', '王', '大王', '王中之王', '天下共主'],
    insignia: ['铜牌', '银牌', '金牌', '金冠饰', '金冠'],
    posts: {
      w1: p(n('百夫长', '', 'C')), w2: p(n('千夫长', '', 'C')), w3a: p(n('统兵大将', '', 'C')), w3b: p(n('先锋骁将', '', 'C')), w4a: p(n('方面大将', '', 'C')), w4b: p(n('万夫长', '', 'C')), w5: p(n('元帅', '', 'C')),
      i1: p(n('书记', '', 'C')), i2: p(n('谋士', '', 'C')), i3a: p(n('军谋', '', 'C')), i3b: p(n('使节', '', 'C')), i4a: p(n('参谋长', '', 'C')), i4b: p(n('大使', '', 'C')), i5: p(n('首席谋臣', '', 'C')),
      p1: p(n('税吏', '', 'C')), p2: p(n('城守', '', 'C')), p3a: p(n('郡守', '', 'C')), p3b: p(n('司库', '', 'C')), p4a: p(n('总管', '', 'C')), p4b: p(n('度支长', '', 'C')), p5: p(n('宰相', '', 'C')),
    },
  },
};

// 文化代号 → 名称表（未列出的文化与缺项按此回退，最后到 generic）。yi（夷洲）无势力，借南中。
const FALLBACK = { yi: 'nanman', moor: 'generic', nanman: 'generic', seasia: 'generic', arab: 'generic', africa: 'generic', sarmatian: 'generic', '*': 'generic' };

// ------------------------------------------------------ 势力专属覆盖 --
// 同一文化下不同王国的制度不同（新罗十七官等、百济佐平、夫余以六畜名官、泰米尔诸国、师子国、西总督、阿克苏姆、林邑、狗奴）。
const silla = ['大乌', '大舍', '一吉飡', '阿飡', '兵部令', '迊飡', '伊伐飡', '小乌', '舍知', '级伐飡', '沙飡', '波珍飡', '礼部令', '伊尺飡', '造位', '吉士', '奈麻', '大奈麻', '大阿飡', '仓部令', '上大等'];
const baekje = ['克虞', '振武', '佐军', '武督', '达率', '卫士佐平', '兵官佐平', '对德', '季德', '将德', '施德', '恩率', '内法佐平', '内臣佐平', '文督', '固德', '奈率', '扞率', '朝廷佐平', '内头佐平', '上佐平'];
const ORDER = ['w1', 'w2', 'w3a', 'w3b', 'w4a', 'w4b', 'w5', 'i1', 'i2', 'i3a', 'i3b', 'i4a', 'i4b', 'i5', 'p1', 'p2', 'p3a', 'p3b', 'p4a', 'p4b', 'p5'];
function table(list, attestFor, src, notes) {
  const o = {};
  ORDER.forEach((id, k) => { o[id] = p(n(list[k], '', attestFor(list[k]), src, notes && notes[list[k]] ? { note: notes[list[k]] } : undefined)); });
  return o;
}
const FACTION_OVERRIDES = {
  silla: {
    rulerRanks: ['渠帅', '尼师今', '尼师今', '麻立干', '大王'],
    note: '新罗十七官等（伊伐飡—造位，《三国史记》系于儒理尼师今九年）；兵部令、礼部令、仓部令、上大等为 6 世纪官（B）。',
    posts: table(silla, z => (/部令|上大等/.test(z) ? 'B' : 'A'), ['ssgs'], { 伊伐飡: '首位官等；「兼知兵马事」。', 上大等: '531 年置，和白会议之首。' }),
  },
  baekje: {
    rulerRanks: ['渠帅', '王', '大王', '大王', '大王'],
    note: '百济十六官品与六佐平：古尔王二十七年（260）置（B，晚于 190 年 70 年）；上佐平见于 408 年。',
    posts: table(baekje, () => 'B', ['ssgs'], { 兵官佐平: '掌内外兵马事。', 内臣佐平: '掌宣纳事。', 内头佐平: '掌库藏事。', 内法佐平: '掌礼仪事。', 卫士佐平: '掌宿卫兵事。', 朝廷佐平: '掌刑狱事。' }),
  },
  buyeo: {
    note: '夫余「以六畜名官」（《三国志·东夷传》）。',
    posts: {
      w4a: p(n('马加', '', 'A', ['sgz-dongyi'])), w4b: p(n('牛加', '', 'A', ['sgz-dongyi'])), p4a: p(n('猪加', '', 'A', ['sgz-dongyi'])), p4b: p(n('狗加', '', 'A', ['sgz-dongyi'])),
      i2: p(n('大使', '', 'A', ['sgz-dongyi'])), i3b: p(n('大使者', '', 'A', ['sgz-dongyi'])),
    },
  },
  chola: { tamil: true }, pandya: { tamil: true }, chera: { tamil: true },
  lanka: {
    rulerRanks: ['首领', '王', '大王', '王中之王', '转轮王'],
    posts: { w2: p(n('族长', 'parumaka', 'A', ['lanka-brahmi'])), w5: p(n('大将军', 'senevi', 'B', ['lanka-brahmi'])), p1: p(n('村长', 'gamika', 'A', ['lanka-brahmi'])), p3a: p(n('大臣', 'amacca', 'B', ['lanka-brahmi'])) },
  },
  kshatrapa: {
    rulerRanks: ['总督', '大总督', '王', '大王', '王中之王'],
    note: '西总督的君主本身即 mahākṣatrapa，其属下改用百乘 / 天竺官名。',
    posts: { p2: p(n('村落长', 'mahattaraka', 'A', ['satavahana'])), p3a: p(n('州大臣', 'āhāra amātya', 'A', ['satavahana'])) },
  },
  aksum: {
    rulerRanks: ['首领', '王', '大王', '王中之王', '王中之王'],
    rulerRanksOrig: ['', 'negus', '', 'negusa nagast', ''],
    posts: { w5: p(n('摄政统帅', 'ḥaḍani', 'B', ['aksum'])), p3a: p(n('海疆之王', 'baḥr nagāš', 'B', ['aksum'])) },
  },
  linyi: { posts: { p2: p(n('功曹', '', 'A', ['funan'], { note: '区连为功曹之子。' })) } },
  kuna: { posts: { w4b: p(n('狗古智卑狗', 'kukochihiku', 'A', ['wajinden'], { note: '狗奴国之官。' })) } },
};
// 泰米尔三国：桑伽姆称号
const TAMIL = {
  rulerRanks: ['首领', '王', '大王', '三冠之主', '转轮王'],
  rulerRanksOrig: ['vēḷ', 'kō', 'vēntar', '', ''],
  note: '桑伽姆时代的五大议与称号。',
  posts: {
    w3a: p(n('埃纳迪', 'enadi', 'B', ['sangam'], { note: '授予将领的称号（待核）。' })), w5: p(n('统帅', 'senāpatiyar', 'A', ['sangam'])),
    i2: p(n('使者', 'tūtar', 'A', ['sangam'])), i3a: p(n('侦探长', 'oṟṟar', 'A', ['sangam'])), i4a: p(n('王师', 'purōkitar', 'A', ['sangam'])),
    p3a: p(n('大臣', 'amaiccar', 'A', ['sangam'])), p4a: p(n('卡维提', 'kaviti', 'B', ['sangam'], { note: '授予官员的称号（待核）。' })), p4b: p(n('埃蒂', 'eṭṭi', 'B', ['sangam'], { note: '授予富商的称号（待核）。' })),
  },
};
for (const k of ['chola', 'pandya', 'chera']) FACTION_OVERRIDES[k] = TAMIL;

module.exports = { CULTURES, FACTION_OVERRIDES, FALLBACK, SOURCES };
