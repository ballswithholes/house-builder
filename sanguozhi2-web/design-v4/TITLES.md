# 三国志-霸王的世界 · 第四版官爵设计（TITLES-V4）

Player request 10 of v4: **rulers assign titles to generals**. Titles have level requirements; some need one attribute above 80 and the best need one above 90. Which title family a general can hold depends on his **highest attribute**. Titles give gameplay benefits for domestic development, diplomacy, training, or fighting and duelling, and the families are balanced against each other.

Read `DESIGN-V2.md` §1 (common rules), `DESIGN-V3.md` §4 (levels), `CONTRACT.md`, and the sibling v4 specs `COMBAT-V4.md` (crit hooks, duel caps, its Q3 and Q9) and `ITEMS.md` (gear stats, envoy rule, budgets) first. Where this file conflicts with them, this file wins for the systems it owns: title catalogue, eligibility, ruler rank and seats, title benefits, title loyalty rules, AI granting and the 官制 screen. Platform order: browser first (this repo), then the Unity port (`../sanguozhi2-unity`, §10.5). Every visual assumes the v4 art direction from the art pilot (Lanternvale: stylized low-poly, cozy Ghibli-warm, flat-shaded props, ink outlines, soft painted light with warm lantern pools). The ceremony uses brush strokes, paper, lacquer, seal stamps and warm gold, never neon.

Status: design spec only. No game code was changed and nothing was committed. Supporting files:

| File | What it is |
|---|---|
| `design-v4/titles.json` | **The catalogue.** 3 families × 5 tiers = 21 posts with their numbers; 18 culture name sets (402 names, each with original term, attestation grade A/B/C and source keys); 11 faction overrides (新罗, 百济, 夫余, 朱罗 / 潘地亚 / 哲罗, 师子国, 西总督, 阿克苏姆, 林邑, 狗奴); tiers, ruler ranks, seat caps, effect dictionary, value weights, 51 sources. Generated, do not hand-edit |
| `design-v4/titles/build-titles-json.js` | Builds `titles.json` and self-checks it (unknown effects, sources, duplicate names after per-faction resolution, clashes with ruler-rank names, value parity between families). `node design-v4/titles/build-titles-json.js` |
| `design-v4/titles/cultures.js` | The name tables per culture and the source list (the only place names live) |
| `design-v4/titles/titles-proto.js` | Throw-away balance prototype. It patches the game functions in memory, never on disk. It plays all-AI campaigns with titles off / on / on + domestic exp, and tactical battles from year-8 titled armies. Source of every measured number in §12 |
| `design-v4/titles/proto-results.txt` | The prototype output quoted in §12 |

---

## 0. Key numbers at a glance

| Topic | Rule |
|---|---|
| Families | 武官 (war), 谋臣 (intel), 文官 (pol). A general may hold posts only of the family of his **highest base attribute**. Exact ties open every tied family (3 classic generals, e.g. 姜维 90/90/72; 16 in the world). Gear never changes eligibility |
| Tiers | 九品 T1 · 七品 T2 · 五品 T3 · 三品 T4 · 一品 T5 |
| Requirements | T1 level 1 · T2 level 2 · T3 level 3 and attribute **> 80** · T4 level 5 and **> 80** · T5 level 7 and **> 90** |
| Posts | Per family: one T1, one T2, two T3 (A / B branch), two T4 (A / B), one T5 = **21 posts**. Branches: 武 A 统帅 / B 武勇, 谋 A 军略 / B 外交, 文 A 民政 / B 财政 |
| Names | Culture-specific (汉 军司马 … 大将军; 罗马 centurio … praefectus praetorio; 安息 argbed … spāhbed; 高句丽 先人 … 国相; 倭 卑狗 … 率善中郎将; 匈奴 千长 … 左贤王 …). **Names never change numbers** |
| Ruler rank | 国势 P = Σ own cities (1 + (land + industry + town) / 1500). R1 P < 3 · R2 ≥ 3 · R3 ≥ 6 · R4 ≥ 10 · R5 ≥ 16. Drop only below 90% of a threshold |
| Seats (all families together) | R1 [2,1,0,0,0] · R2 [3,2,1,0,0] · R3 [4,3,2,1,0] · R4 [5,4,3,2,1] · R5 [6,5,4,3,3] (T1…T5). Every T3/T4 post has 2 seats, every T5 post 1 seat |
| Cost | No token. One office change per general per month. One-time **授官礼金** 50 / 100 / 200 / 300 / 500 gold (promotion pays the difference). **No monthly salary** (§6.3 says why) |
| Loyalty | Grant +5 / +8 / +12 / +16 / +20, paid once per tier per general per faction. Revoke −10 / −14 / −18 / −22 / −26. Passing over a clearly better eligible general: −4 to him (≤ 3 generals) |
| Loyalty protection | Defection check uses loyalty + 4 / 8 / 12 / 16 / 20. Enemy 拉拢 × (1 − 0.1 × tier). Enemy 离间 loss × (1 − 0.08 × tier) |
| Best of each family (汉) | 大将军: 攻 / 防 +12%, 主将统率 +3%, 城防 +10, 会心 +2pp, 单挑 会心 +3pp, 伤害 +2%. 军师将军: 计策威力 +22%, 成功 +6pp, 全军识破 +6pp, 登用 +8pp, 同盟 +10pp. 丞相: 开发 +22%, 本城金 / 秋收 +10%, 本城忠诚 +2/月, 全国金 +2.5%, 秋收 +2% |
| Value parity | Value index per tier (§7.8): T1 ≈ 7, T2 ≈ 12, T3 ≈ 20, T4 ≈ 33, T5 ≈ 53. Every post within ±20% of its tier mean (actual spread ≤ 12%) |
| Caps shared with gear | Army crit: title ≤ 0.02 + gear ≤ 0.04, COMBAT cap 0.06. Duel crit: ≤ 0.03 + ≤ 0.05, cap 0.08. Duel damage: ≤ 0.02 + ≤ 0.04, cap 0.06 |
| AI | Deterministic, no `SG.Random` draws. Each month it fills seats from T5 down by merit (attribute + 3 × level). It never revokes |
| Balance (prototype, §12) | Classic 48 seeds × 25 years: factions alive at year 10 are 4.9 with titles off and 4.6 / 4.5 with titles on / on + domestic exp. Median time to the first > 25% faction is 2.3 y (off) and 2.3 / 2.2 y. World 24 × 15 years: no measurable shift. Tactical: titles both sides shift attacker win −0.6 pp; attacker-only vs defender-only swing 2.9 pp |

---

## 1. Design goals and the shape of the system

1. **Every general has a ladder, and the ladder fits him.** A 武 general climbs 军司马 → 校尉 → 偏将军 → 前将军 → 大将军. A 智 general climbs 主簿 → 参军 → 军师祭酒 → 军师中郎将 → 军师将军. A 政 general climbs 从事 → 别驾 → 刺史 → 尚书令 → 丞相. The ladder is visible from day one in the character menu, with progress bars ("等级 4 / 5").
2. **Scarcity you can feel.** There are few high seats, and they open as your realm grows: by conquest, or by developing the cities you already hold. That is a non-battle route to the top (request 13).
3. **Meaningful choices.** At R4 there is one 一品 seat for all three families. Do you make 关羽 大将军, 诸葛亮 军师将军 or 荀彧 丞相? At T3 and T4 each family has two branches (statesman or financier; commander or champion; strategist or diplomat).
4. **Loyalty is the cost of being unfair.** Promote the wrong man and the better one sulks. Strip a title and it hurts. Titled officers are harder for the enemy to poach.
5. **Balanced across families.** The same tier has the same value whether it fights, schemes or governs (§7.8). Names differ by culture, numbers never do.
6. **Safe for the AI and the tests.** The rules draw no random numbers. Titles off reproduces v4-without-titles exactly. The AI economy is not destabilised (§6.3, §12).

---

## 2. Families and eligibility

### 2.1 Family

```
base(g, 'war') = g.war, base(g, 'intel') = g.intel, base(g, 'pol') = g.pol      // stored attributes; never gear, never buffs
families(g)    = [f ∈ war, intel, pol | base(g, f) = max(g.war, g.intel, g.pol)]   // order war, intel, pol
family(g)      = families(g)[0]                                                   // colour and AI default
```

- Attributes in this game never change, so a general's family is fixed for life. Gear raises effective attributes (ITEMS §2.4) but never title eligibility. This answers ITEMS Q3: base attributes, so gold cannot buy titles.
- **Ties** (exactly equal top attributes) open every tied family. Classic: 朱治 70/66/70, 沮鹄 40/60/60, 姜维 90/90/72. World adds 13 more, such as 提梅西修 72/86/86 and 普皮恩努斯 74/70/74. A tied general may switch families by a lateral change (§6.2).
- Examples: 关羽 98/78/62 → 武. 诸葛亮 38/100/96 → 谋. 荀彧 20/96/98 → 文 (政治 98 beats 智力 96). 曹操 72/94/92 → 谋. 刘备 76/76/78 → 文. 张飞 99/34/26 → 武.

### 2.2 Eligibility for a post

```
eligible(g, post) =
     Titles.enabled && !g.dead && g.faction ≥ 0 && !G.isRuler(g) && !Conquest.isCaptive(g)
  && families(g).includes(post.family)
  && g.level ≥ tier(post).reqLevel                        // 1 / 2 / 3 / 5 / 7
  && base(g, post.family) > tier(post).reqStat            // 0 / 0 / 80 / 80 / 90   (strictly greater: 81+, 91+)
```

`requirements(g, post) → [{ key, ok, text }]` lists the checks in UI order: 家族 (最高属性), 等级, 属性, 名额 (§5), 礼金 (§6.1), 本月已变动 (§6.1). Example texts: `需 5 级（现 4 级）`, `武力需高于 80`, `最高属性为智力，只能出任谋臣`, `三品名额已满（2 / 2）——国势达到 10.0 后增加`, `礼金不足（需 300 金）`, `本月已有任免`.

### 2.3 How many generals qualify

| | classic (162) | world (499) |
|---|---|---|
| Highest attribute war / intel / pol | 98 / 41 / 23 | 278 / 111 / 110 |
| … and > 80 (T3, T4) | 49 / 29 / 7 | 66 / 65 / 25 |
| … and > 90 (T5) | 20 / 15 / 3 | 22 / 24 / 7 |

T5-eligible by attribute in the classic scenario. War: 吕布, 张飞, 关羽, 马超, 赵云, 孙策, 典韦, 许褚, 张辽, 文丑, 庞德, 甘宁, 太史慈, 黄忠, 颜良, 孙坚, 兀突骨, 魏延, 夏侯渊, 徐晃. Intel: 诸葛亮, 司马懿, 郭嘉, 贾诩, 庞统, 周瑜, 陆逊, 田丰, 曹操, 荀攸, 法正, 沮授, 徐庶, 鲁肃, 邓艾. Pol: 荀彧, 张昭, 蒋琬. The world adds war 马克西明 and 芬恩; intel 阿尔达希尔, 巴戴桑, 盖伦, 保罗, 奥利金, 犹大亲王, 龙树, 伊兰戈 and 提婆; pol 乙巴素, 帕比尼安, 乌尔比安 and 科马克. The level 7 requirement (§12.4) does most of the gating in practice.

Pol has few 一品 candidates, which is deliberate. A 丞相 should be a 荀彧 or an 乙巴素. Pol generals still fill T1–T4 broadly, because T3 and T4 need only > 80.

---

## 3. Data: `titles.json`

### 3.1 Top level

```js
{
  schema: 'sanguozhi2.titles/1', version: 1,
  families: { war: { zh: '武官', stat: 'war', statZh: '武力', icon, color: '#c8382c', desc }, intel: {…'#2f6db5'}, pol: {…'#2f9a55'} },
  tiers: [ { tier, grade: '九品', reqLevel, reqStat, loyGrant, grantGold, effLoy, persuadeRes, discordRes, revokeLoy, ceremony } ×5 ],
  rulerRanks: { power: { perCity: 1, devDiv: 1500 }, thresholds: [0, 3, 6, 10, 16], hysteresis: 0.9, caps: { 1: [2,1,0,0,0], …, 5: [6,5,4,3,3] } },
  effects: { atk: { unit: 'pct', scope: 'self', where, zh }, … },          // §7.1 dictionary
  posts: [ { id: 'w4a', family: 'war', tier: 4, branch: 'A', seats: 2, role: '统帅', fx: { atk: 0.08, … } } ×21 ],
  valueWeights: { … }, valueBand: 0.2,                                      // §7.8
  cultures: { han: { zh, rulerRanks: [5], rulerRanksOrig?, insignia: [5], insigniaSrc?, partial?, posts: { w1: { names: [Name…] }, … } }, … },
  factionOverrides: { silla: { rulerRanks?, note?, posts: { … } }, … },
  cultureFallback: { yi: 'nanman', moor: 'generic', …, '*': 'generic' },
  sources: { 'hhs-bgz': { cite, url? }, … }
}
Name = { zh: '前将军', orig: 'qián jiāngjūn', attest: 'A' | 'B' | 'C', src: ['hhs-bgz'], rank?: '比二千石', note?, seal? }
```

- **attest**: A means the culture used the term within about ±60 years of 190 AD. B means the culture used it, but earlier or later; the detail page then shows `时代错置：…`. C means a descriptive name made up for the game, with no original term. The counts are A 186, B 105, C 111. The C names sit mostly in poorly documented cultures (阿拉伯, 库施, 萨尔马提亚, 南中) and in the `generic` fallback.
- A post may carry several names. The first is the default; the others are cosmetic picks. A T3 or T4 post with 2 seats gives each holder a different name while names last. For example, two 汉 T4-A holders become 前将军 and 后将军. The player may pick any free name at grant time.

### 3.2 Name resolution

```
nameSet(factionKey, culture, postId) =
     factionOverrides[factionKey]?.posts[postId]
  ?? cultures[culture]?.posts[postId]
  ?? cultures[cultureFallback[culture] ?? 'generic'].posts[postId]   // follow fallback until found, at most 4 steps
  ?? cultures.generic.posts[postId]
rulerRankNames(factionKey, culture) = factionOverrides[factionKey]?.rulerRanks ?? cultures[culture ?? 'generic'].rulerRanks
```

`culture` is `faction.culture` (set by `GameState.init`, 'han' without world data), **not** the general's own culture. A Roman deserter serving 曹操 becomes 偏将军, not legatus. The build check resolves all 70 world factions and asserts two things. Within one faction the 21 default names are distinct. No post name equals one of that faction's ruler-rank names.

### 3.3 The catalogue (default names; full data with originals and sources in `titles.json`)

| Culture | 武官 T1 → T5 (w1, w2, w3a, w3b, w4a, w4b, w5) | 谋臣 T1 → T5 (i1 … i5) | 文官 T1 → T5 (p1 … p5) | Ruler ranks R1 → R5 |
|---|---|---|---|---|
| han 汉 | 军司马、越骑校尉、偏将军、虎贲中郎将、前将军、破虏将军、大将军 | 主簿、参军、军师祭酒、从事中郎、军师中郎将、大鸿胪、军师将军 | 从事、别驾从事、刺史、典农中郎将、尚书令、大司农、丞相 | 郡守→州牧→公→王→皇帝 |
| nanman 南中 | 洞兵头、部曲长、渠帅、藤甲兵长、夷帅、洞主、大夷帅 | 巫、鬼主、大鬼主、通事、巫师长、南中使者、大巫 | 寨老、大姓、耆帅、盐井长、部长、互市长、蛮相 | 寨主→酋王→大王→蛮王→南中大王 |
| korea 高句丽 | 先人、皂衣、小加、大模达、大加、左辅、莫离支 | 使者、主簿、沛者、大使者、对卢、古雏加、相加 | 优台、中畏大夫、傉萨、赈贷使、太大兄、大对卢、国相 | 君长→王→大王→太王→天王 |
| wa 倭 | 卑奴母离、卑狗、物部、靫负、一大率、卫士长、安东大将军 | 卜部、祝、鬼道师、大夫、传辞使、率善校尉、率善中郎将 | 奴佳鞮、弥马获支、弥马升、大倭、伊支马、大连、大臣 | 首长→王→大王→亲魏倭王→治天下大王 |
| steppe 草原 | 什长、千长、万骑长、裨小王、左大将、左大都尉、左贤王 | 当户、且渠、骨都侯、相、左大当户、左谷蠡王、日逐王 | 小帅、封都尉、部大人、僮仆都尉、东部大人、胡市长、单于庭相国 | 部落首领→大人→王→单于→大单于 |
| tarim 西域 | 百长、部千长、左都尉、左骑君、击胡侯、击车师都尉、左将 | 译长、却胡君、奥古、力辅君、却胡都尉、侍子、辅国侯 | 城长、税吏、州长、大禄、大都尉丞、萨宝、安国侯 | 城主→王→大王→王中之王→天子 |
| seasia 扶南 | 百夫、将、大将、象兵长、水军帅、坡、大元帅 | 书记、使者、婆罗门师、译使、国师、大使、辅臣 | 邑长、税官、城主、市舶长、大臣、港务长、宰相 | 渠帅→王→大王→扶南大王→王中之王 |
| kushan 贵霜 | 百骑长、将、大将、铁骑长、边疆长官、翕侯、大元帅 | 书吏、使者、谋士、大使者、国师、外藩长、大辅臣 | 村长、总督、大总督、司库、大臣、商旅长、宰相 | 小王→王→大王→王中之王→天子 |
| india 天竺 | 队长、将、摩诃罗提、象军监、摩诃波阇、大塔拉瓦拉、大将军 | 书记、使者、谋臣、和战大臣、王师、王使、首席谋臣 | 村长、村落长、大臣、司库、大辅、行会长、宰相 | 小王→王→大王→王中之王→转轮王 |
| persia 安息 | 骑队长、堡垒长、方面将军、骑兵统领、总督、卫队长、大将军 | 书吏、使者、智者议员、王使、首席书记、祭司长、大千夫长 | 税吏、城守、副王、计吏、大管家、司库、大宰相 | 堡主→王→大王→万王之王→伊朗万王之王 |
| arab 阿拉伯 | 骑兵队长、骑兵长、将军、骆驼骑长、统军、侍从武官、大军统帅 | 书吏、使者、卜者、商使、祭司长、外交使臣、谋主 | 部族长、领主、总管、商队长、管家、市长、王家总管 | 谢赫→主→王→阿拉伯之王→王中之王 |
| africa 库施 | 弓兵队长、百夫、陆军将、水军将、将军、弓骑长、王子统帅 | 书吏、使者、祭司、大使者、阿蒙大祭司、赴罗马使、王室谋臣 | 村长、书记官、总督、司库、太后府长、商路长、宰相 | 首领→王→大王→王中之王→库施与诸地之王 |
| roman 罗马 | 百夫长、首席百夫长、军团长、骑兵翼长、行省总督、分遣军统领、近卫军长官 | 参谋士官、密使、营务长官、希腊文书信官、随驾参谋、书信总管、御前顾问 | 财务官、代理官、行省执政、粮政长官、罗马城长官、财务总管、执政官 | 军阀→统帅→元首→恺撒→奥古斯都 |
| celt 凯尔特 | 战士、勇士、战团首领、冠军、领主、王之护卫、王储 | 诗人、德鲁伊、大德鲁伊、信使、大诗人、法官、王之贤者 | 牛主、地主、领地主、牧群长、管家、贡赋长、首席法官 | 首领→王→大王→诸王之王→至高王 |
| german 日耳曼 | 百人长、千人长、军酋、亲兵、统军首领、百人精选、战酋 | 卜者、祭司、智者、使者、女先知、议事长、大祭司 | 村长、首领、长官、牧主、统治者、贡赋长、大议首 | 酋长→王→大王→诸部之王→诸族之王 |
| sarmatian 萨尔马提亚 | 骑兵什长、千夫长、骑兵长、铁甲骑长、将军、舰队长、战王 | 书吏、使者、占卜者、通译、王之顾问、宾客总管、国务总管 | 村长、城守、宫廷总管、司库、总督、港务长、王家总管 | 首领→王→大王→诸王之王→博斯普鲁斯之王 |
| moor 毛里 (partial) | (generic) · 部众统领 · … | (generic) | 族首领 (princeps gentis) · (generic) | 族长→王→大王→诸族之王→毛里之王 |
| generic 通用 | 百夫长、千夫长、统兵大将、先锋骁将、方面大将、万夫长、元帅 | 书记、谋士、军谋、使节、参谋长、大使、首席谋臣 | 税吏、城守、郡守、司库、总管、度支长、宰相 | 首领→王→大王→王中之王→天下共主 |

Faction overrides:

- **新罗**: the seventeen 官等 spread over the 21 posts in rank order. 大乌 / 小乌 / 造位 are T1. 伊伐飡 (首位，「兼知兵马事」) is 武 T5, 伊尺飡 is 谋 T5 and 上大等 is 文 T5. The 6th-century 兵部令 / 礼部令 / 仓部令 / 上大等 are grade B. The ruler ranks are 尼师今 / 麻立干.
- **百济**: the sixteen 官品 and the six 佐平 of 260 (B). 兵官佐平 is 武 T5, 内臣佐平 谋 T5 and 上佐平 文 T5. 卫士佐平 / 内法佐平 / 朝廷佐平 / 内头佐平 are the T4-B / T4-A posts. Their duties match the families (宿卫兵事, 礼仪, 刑狱, 库藏).
- **夫余**: 以六畜名官. 马加 / 牛加 are 武 T4 and 猪加 / 狗加 文 T4; 大使 and 大使者 are 谋.
- **朱罗 / 潘地亚 / 哲罗**: the Sangam five assemblies. senāpatiyar is 武 T5, tūtar 谋 T2, oṟṟar (spies) 谋 T3-A, purōkitar 谋 T4-A and amaiccar 文 T3-A. The titles enadi / kaviti / eṭṭi are grade B, pending a check. The ruler ranks are vēḷ → kō → vēntar.
- **师子国**: parumaka (族长) and gamika from the Brāhmī inscriptions; senevi is 武 T5.
- **西总督**: its ruler is himself a mahākṣatrapa, so its officers use 百乘 terms.
- **阿克苏姆**: negus / negusa nagast; ḥaḍani is 武 T5 (B).
- **林邑**: 功曹 (区连 was a 功曹's son).
- **狗奴**: 狗古智卑狗 is 武 T4-B.

### 3.4 Insignia (icon motif per tier)

| Culture | T1 → T5 |
|---|---|
| han | 铜印黄绶 → 铜印黑绶 → 银印青绶 → 金印紫绶 → 金印紫绶·龟钮 (《汉书·百官公卿表》: 比二百石以上铜印黄绶，比六百石以上铜印黑绶，比二千石以上银印青绶；丞相、太尉金印紫绶) |
| roman | phalera → torques → hasta pura → corona aurea → purple paludamentum with gold eagle (dona militaria) |
| persia | agate seal → silver bowl medallion → gold belt clasp → tiara crest → sun-and-moon crown with diadem ribbons |
| wa | 勾玉 → 管玉串 → 铜镜 → 金印 → 金印 with the three-regalia pattern |
| korea | 鸟羽冠饰 → 金铜冠饰 → 银花冠 → 金花冠 → 金冠·玉带 |
| others | see `titles.json` `insignia` (steppe gold belt plaques and the eagle crown, Kushan coin-medallions, Indian armlets and the white parasol, Celtic torcs, Germanic arm-rings …) |

---

## 4. Tiers

| Tier | Grade | Level | Attribute | Grant loyalty | 礼金 | Defection bonus | 拉拢 resistance | 离间 resistance | Revoke | Ceremony |
|---|---|---|---|---|---|---|---|---|---|---|
| T1 | 九品 | 1 | — | +5 | 50 | +4 | 10% | 8% | −10 | small 1.8 s |
| T2 | 七品 | 2 | — | +8 | 100 | +8 | 20% | 16% | −14 | small 1.8 s |
| T3 | 五品 | 3 | > 80 | +12 | 200 | +12 | 30% | 24% | −18 | medium 3.0 s |
| T4 | 三品 | 5 | > 80 | +16 | 300 | +16 | 40% | 32% | −22 | medium 3.0 s |
| T5 | 一品 | 7 | > 90 | +20 | 500 | +20 | 50% | 40% | −26 | grand 4.5 s |

The grades 九品 … 一品 are game labels in the spirit of the later 九品 system. The Han ranked by 秩 (salary grade), and the detail page shows a post's historical 秩 where it is known (`rank`). Examples: 军司马 比千石, 校尉 / 中郎将 比二千石, 刺史 六百石, 尚书令 千石, 大鸿胪 / 大司农 中二千石, 丞相 万石. That 刺史 and 尚书令 were low-paid yet powerful is a note on the detail page, not a rule.

---

## 5. Ruler rank and seats (scarcity)

### 5.1 国势 and rank

```
P(f) = Σ_{c : c.owner = f} ( 1 + (c.land + c.industry + c.town) / 1500 )         // one city = 1.4 … 3.0
up(f) = max k with P(f) ≥ thresholds[k-1], thresholds = [0, 3, 6, 10, 16]
rank(f):  if up > stored → stored = up                                            // rise at once
          else while stored > 1 and P < thresholds[stored-1] × 0.9 → stored −= 1  // fall with 10% hysteresis
```

- A starting city scores 1.4–2.3, and a fully developed city 3.0. Classic start: 11 factions R1, 7 R2. World start: 46 R1, 23 R2, and Rome (22 cities, P 40) R5.
- **Two routes up.** Conquest: about 2 / 4 / 6 / 9 average cities for R2 / R3 / R4 / R5. Development: 2 maxed cities reach R3, 4 reach R4 (one 一品 seat), 6 reach R5. A peaceful player who builds up 4 cities gets an 一品 seat without a war (request 13).
- `faction.rank` is saved. It is recomputed at month start, after a conquest (`Conquest.apply`), after 拉拢 hands over a city, and after a 开发 by the player (so the 国势 bar moves visibly).
- Rank names are only words. 曹操 · 州牧 → 公; 罗马 · 奥古斯都; 贵霜 · 王中之王. A rank-up has its own ceremony (§9.6) and a news line: `曹操晋位为公！可授五品官二人、三品官一人。`

### 5.2 Seats

`caps(f) = rulerRanks.caps[rank(f)]`, five numbers, shared by the three families:

| Rank | T1 | T2 | T3 | T4 | T5 | Total |
|---|---|---|---|---|---|---|
| R1 | 2 | 1 | — | — | — | 3 |
| R2 | 3 | 2 | 1 | — | — | 6 |
| R3 | 4 | 3 | 2 | 1 | — | 10 |
| R4 | 5 | 4 | 3 | 2 | 1 | 15 |
| R5 | 6 | 5 | 4 | 3 | 3 | 21 |

- **Post seats**: T1 and T2 posts are limited only by the tier cap. Each T3 and T4 post has 2 seats; each T5 post has 1. At R5 the three T5 seats are exactly one 大将军, one 军师将军 and one 丞相. At R4 there is one 一品 seat in all, so you choose.
- A seat is **used** by an *active* holder. Suspended holders (§6.6) use no seat.
- Small realms get few titles on purpose. With 15 one-general world factions (the ruler only), titles are a big-power affair. A 1-city realm can still reward its two best men.

---

## 6. Granting, promoting, revoking

### 6.1 Grant

`Titles.canGrant(f, g, postId, nameIdx?) → { ok, why, cost, payFrom, loyalty, passedOver: [{ gen, loss }], name }`
`Titles.grant(f, g, postId, nameIdx?) → { ok, why, office, cost, loyalty, passedOver, ceremony: 'small'|'medium'|'grand' }`

1. Checks (§2.2) plus: the tier has a free seat (`active(f, tier) < caps[tier]`, or the general already holds this tier and is moving laterally). The post has a free seat. `g.office?.month !== G.monthIndex` (one office change per general per month). There is enough gold for the gift.
2. **Gift (礼金)** = `grantGold[new] − (current ? grantGold[current] : 0)` (promotion pays the difference, lateral costs 0). It is paid by the general's city if that city has enough, otherwise by the richest own city. The AI also leaves 100 behind (§8). The cost shows on the confirm card, `礼金 200 金（自 许昌）`.
3. **Loyalty**: `+max(0, loyGrant[new] − loyGrant[g.officeBest])`, then `g.officeBest = max(g.officeBest, new)`. `officeBest` resets to 0 when the general changes faction. So re-granting after a revoke gives no second bonus, and revoke + regrant can never farm loyalty.
4. **Passed over (不平)**: let X get tier t in family F. Every other non-ruler officer Y of the faction with F ∈ families(Y) who could hold a tier-t post (eligibility ignoring seats), currently holds less than t, and has `merit(Y) ≥ merit(X) + 10` loses 4 loyalty. Here `merit = base(F) + 3 × level`. It applies to at most 3 generals, highest merit first, once per grant, floor 0. The confirm card warns first: `张辽、徐晃自恃功高，将心生不平（忠诚 −4）`. The AI grants by merit, so it rarely triggers the rule.
5. The new office is `g.office = { post, name, tier, fam, since: monthIndex, seq, faction, month: monthIndex, suspended: false }`. The game adds a log line (`拜关羽为荡寇将军。`) and plays the ceremony (§9.5).

### 6.2 Promote, move laterally, switch family

- **Promotion** (higher tier, same or tied family) frees the old seat. It pays the gift difference and the loyalty difference. Within the T3/T4 branches the player chooses A or B again.
- **Lateral** (same tier, other branch or a tied family): free, no loyalty change, still the one change per month.
- **Name only** (pick another free name of the same post): free and not counted as a change.

### 6.3 Why the cost is a one-time gift and not a monthly salary

The first design had a monthly 俸禄 of 3 / 5 / 8 / 12 / 18. The prototype rejected it (`titles-proto.js`, env `SALARY`).

- The v3 AI spends a city's gold down to nearly 0 on 征兵. Any monthly title salary then tips stacked cities (up to 40+ officers in late-game capitals) into `gold < 0`. The v3 rule then cuts **every** officer's loyalty by 3, and generals defect. Defections rose from 2 to 21–107 per seed.
- Even with a payment rule that keeps a reserve, any recurring drain sped up consolidation. Factions alive at year 10 fell to 3.5–3.9, against 4.9 off. Rich realms pay salaries easily and poor ones cannot, so the drain snowballs.
- A one-time gift of 50–500 costs about 2.3% of gross income over a game and keeps the campaign within noise of titles-off (§12). It also reads well to the player as 封赏.

### 6.4 Revoke and demote

`Titles.revoke(g, reason?)` and `Titles.demote(g, postId)` are allowed at any time on one's own generals, with no token and within the one-change-per-month limit.

| Action | Loyalty |
|---|---|
| Revoke (罢免) a tier-t office | −revokeLoy[t]: −10 / −14 / −18 / −22 / −26 |
| Demote by k tiers | −(4 + 4k) |
| Lateral / name change | 0 |
| Revoke a *suspended* office (§6.6) | half of the above, rounded up (他也知道是国势所限) |

The confirm card shows the result and, when it applies, the defection risk: `贬后忠诚 31（低于 40 可能出奔）`.

### 6.5 Automatic loss

`Titles.validate(g)` runs at month start, in `checkFactionDeath`, `Conquest.apply / release / execute / succession`, `Commands.persuade / hire`, and wherever `gen.faction` changes.

- **Dead / executed**: the office is cleared (`赵云卒，前将军出缺。`).
- **Faction changed** (defected, persuaded, captured and then hired, faction destroyed): cleared silently, `officeBest = 0`.
- **Became ruler** (succession): cleared. Rulers hold no office; the ruler's rank is the faction's.
- **Captured, still pending**: the office is kept but gives nothing (`isCaptive`). Released home: active again. Hired by the captor: cleared.
- Moving between own cities keeps the office.

### 6.6 Rank drop: suspension (虚衔)

When `rank(f)` falls, tiers are checked from T5 down. While a tier has more active holders than its cap, the **newest** grant (largest `seq`) is suspended. Suspended offices:

- give **no** effects (§7);
- keep their **loyalty protection** (defection bonus, 拉拢 and 离间 resistance), since the title is still held;
- reactivate automatically, oldest first, when seats open (rank recovers, or a holder dies or is revoked). This runs before any new grant can use the seat. The player can also revoke one (half penalty).

News: `国势衰减，张辽的前将军暂为虚衔。` and `国势复振，张辽重领前将军。`

### 6.7 Promise of office when recruiting (许官)

In 登用 (cmdHire), in the search-found dialog and in captive recruitment, the player may add `许以官职` if he can grant the target a post now: eligible, seat free, gift affordable. The offer raises `hireChance` by `0.05 × tier` (T1 +5pp … T5 +25pp), with the overall cap 0.95. On success the office is granted at once (gift paid, ceremony small, counts as the month's change). On failure nothing is paid. The AI does not use 许官 in v4 (open question Q6).

---

## 7. Benefits

### 7.1 Effect dictionary and hooks

All hooks read `SG.Titles?.bonus(gen, key)` and similar, and return 0 or identity when titles are off or the office is suspended or captive. **No hook draws a random number.** Values in `titles.json` are fractions for `pct` / `pp` (0.08 = +8% or +8 pp) and integers for `flat`.

| Key | Unit | Scope | Hook (exact) |
|---|---|---|---|
| `atk` | pct | self | `atkPower(u) ×= 1 + bonus(u.gen,'atk') + sideBonus(M, u.side,'cmdAtk')` |
| `def` | pct | self | `defFactor(u) ×= 1 + bonus(u.gen,'def') + sideBonus(M, u.side,'cmdDef')` |
| `cmdAtk`, `cmdDef` | pct | side | `sideBonus(M, s, k) = bonus(cu.gen, k)` where `cu = M.commander(s)`, alive and not confused; else 0 |
| `morale` | flat | self | `BattleModel` constructor: `u.morale = min(100, u.morale + bonus(gen,'morale'))` after the v3 formula |
| `critArmy` | pp | self | COMBAT-V4 §2 `titleCrit(a)`; summed with gear and clamped to 0.06 there |
| `critDuel`, `dmgDuel` | pp, pct | self | COMBAT-V4 §3 `title.critDuel` (gear + title ≤ 0.08); `dmgDuel` adds to `SG.Gear.duelMods(gen).dmg` (gear + title ≤ 0.06) |
| `train` | flat | self | `Commands.train(g)`: `+ bonus(g,'train')` to the gain (cap 100 unchanged) |
| `garrison` | flat | city | `SG.cityDefense(c) = 20 + town/12 + max_{o ∈ officersIn(c)} bonus(o,'garrison')`. Read by `Conquest.autoResolve` (defK) and the AI's attack test, so titled defenders deter attacks |
| `tac` | pct | self | `tacK(u, e) ×= 1 + bonus(u.gen,'tac')`: the four original tactics, new tactics and intel-based specials |
| `tacHit` | pp | self | original tactics: `p = clamp(0.5 + ΔI/110 + bonus(u,'tacHit') − sideRes(t.side), 0.12, 0.92)`; new tactics: `tacticChance` adds the same before its clamp |
| `tacRes` | pp | side | `sideRes(s) = max over alive units of side s of bonus(gen,'tacRes')`. Enemy tactic success against any unit of that side drops by it |
| `search` | pp | self | `Commands.search`: `p = 0.22 + intel/260 + virtue/420 + bonus(g,'search')` |
| `hire` | pp | self | `hireChance(target, recruiter, f) = clamp(v3 + bonus(recruiter,'hire') + promise, 0.05, 0.95)` |
| `dip` | pp | self | `allyChance(f, target, gift, envoy) += bonus(envoy,'dip')`. The 同盟 flow gains an envoy picker, the same picker ITEMS §3.4 needs (`max(dip of ruler, dip of envoy)` for gear) |
| `persuade` | pp | self | `persuadeChance(agent, target, f) = clamp((v3 + bonus(agent,'persuade')) × (1 − persuadeRes(target)), 0, 0.9)` |
| `discord` | pp | self | `discord(agent, target)`: success `p + bonus(agent,'discord')`; loss `d = max(1, round(d × (1 − discordRes(target))))` |
| `dev` | pct | self | `develop(c, g, k)`: `gain = round((DevelopBase + pol/6 + R(0,4)) × (1 + bonus(g,'dev') + gearPct))`, applied by the general who presides (the general picker shows the expected gain) |
| `income` | pct | city | `endMonth`: `c.gold += round((industry×0.12 + town×0.05) × (1 + cityBonus(c,'income') + natBonus(owner,'natIncome')))` |
| `harvest` | pct | city | `endMonth`, month 7: `harvest × (1 + cityBonus(c,'harvest') + natBonus(owner,'natHarvest'))` |
| `recruit` | pct | city | `recruit` / `recruitMax`: troops per gold `5 × (1 + cityBonus(c,'recruit') + gear)` |
| `pop` | pct | city | `endMonth`: population growth `× (1 + cityBonus(c,'pop'))` |
| `loyAura` | flat | city | `endMonth`, after the defection check: every non-ruler officer in the city `+loyAura`, not above 95 (the same rule as ITEMS `loyal`; the two do not stack: take the max) |
| `natIncome`, `natHarvest` | pct | nation | `natBonus(f, k) = max over active offices in f` (the single best holder, never a sum) |

**City administrator.** `cityBonus(c, k) = bonus(admin(c), k)`. `admin(c)` is the governor (`c.governor`). When the governor is the ruler, who holds no office, it is the highest-tier active 文官 office holder stationed in the city, acting as 长史, with ties going to higher 政治 then lower id. The city panel shows `主政：荀彧（尚书令）`. This differs from gear (ITEMS §3.4 uses the ruler's own gear when he governs) because rulers have no office.

**Loyalty protection** (any office, active or suspended):

```
endMonth defection:  loyEff = loyalty + effLoy[tier]; skip if loyEff ≥ 40; p = (40 − loyEff) / 400 × (1.2 − virtue/100)
persuadeRes(t) = 0.1 × tier, discordRes(t) = 0.08 × tier
```

### 7.2 The 21 posts (汉 names; all cultures share the numbers)

| Post | Tier | Branch | Seats | 汉 names (first = default) | Effects |
|---|---|---|---|---|---|
| w1 | 九品 T1 | — 统兵 | — | 军司马 / 别部司马 | 攻 +3%，防 +3%，城防 +2，训练 +1 |
| w2 | 七品 T2 | — 统兵 | — | 越骑校尉 / 屯骑校尉 / 步兵校尉 / 长水校尉 / 射声校尉 | 攻 +4%，防 +4%，城防 +4，训练 +2，士气 +3 |
| w3a | 五品 T3 | A 统帅 | 2 | 偏将军 / 裨将军 | 攻 +7%，防 +7%，城防 +6，训练 +3，士气 +4，会心 +1pp |
| w3b | 五品 T3 | B 武勇 | 2 | 虎贲中郎将 / 羽林中郎将 / 五官中郎将 | 攻 +8%，防 +4%，城防 +4，训练 +2，士气 +10，会心 +1pp，单挑会心 +2pp，单挑伤害 +2% |
| w4a | 三品 T4 | A 统帅 | 2 | 前将军 / 后将军 / 左将军 / 右将军 | 攻 +8%，防 +8%，城防 +8，训练 +4，士气 +5，会心 +1pp，主将统率 攻 / 防 +2% |
| w4b | 三品 T4 | B 武勇 | 2 | 破虏将军 / 讨逆将军 / 荡寇将军 / 奋武将军 / 虎威将军 | 攻 +11%，防 +6%，城防 +6，训练 +3，士气 +12，会心 +2pp，单挑会心 +3pp，单挑伤害 +2%，主将统率 攻 +1% |
| w5 | 一品 T5 | — 元戎 | 1 | 大将军 / 骠骑将军 / 车骑将军 | 攻 +12%，防 +12%，城防 +10，训练 +5，士气 +10，会心 +2pp，单挑会心 +3pp，单挑伤害 +2%，主将统率 攻 / 防 +3% |
| i1 | 九品 T1 | — 参谋 | — | 主簿 / 记室 | 计策威力 +4%，计策成功 +1pp，搜索 +3pp，登用 +2pp，同盟 +2pp |
| i2 | 七品 T2 | — 参谋 | — | 参军 | 计策威力 +6%，成功 +2pp，搜索 +4pp，登用 +3pp，同盟 +3pp，拉拢 +2pp，离间 +2pp |
| i3a | 五品 T3 | A 军略 | 2 | 军师祭酒 / 军祭酒 | 计策威力 +13%，成功 +4pp，全军识破 +3pp，搜索 +4pp，登用 +3pp，同盟 +2pp |
| i3b | 五品 T3 | B 外交 | 2 | 从事中郎 / 谒者仆射 | 计策威力 +5%，成功 +1pp，搜索 +5pp，登用 +6pp，同盟 +8pp，拉拢 +6pp，离间 +5pp |
| i4a | 三品 T4 | A 军略 | 2 | 军师中郎将 | 计策威力 +17%，成功 +5pp，全军识破 +5pp，搜索 +5pp，登用 +4pp，同盟 +3pp，拉拢 +2pp |
| i4b | 三品 T4 | B 外交 | 2 | 大鸿胪 | 计策威力 +6%，成功 +3pp，搜索 +6pp，登用 +10pp，同盟 +14pp，拉拢 +10pp，离间 +7pp |
| i5 | 一品 T5 | — 帷幄 | 1 | 军师将军 / 军师 | 计策威力 +22%，成功 +6pp，全军识破 +6pp，搜索 +6pp，登用 +8pp，同盟 +10pp，拉拢 +7pp，离间 +6pp |
| p1 | 九品 T1 | — 理事 | — | 从事 / 功曹 | 开发 +5%，本城金 +3%，本城秋收 +3% |
| p2 | 七品 T2 | — 理事 | — | 别驾从事 / 治中从事 | 开发 +8%，本城金 +4%，本城秋收 +5%，征兵 +2%，人口 +3% |
| p3a | 五品 T3 | A 民政 | 2 | 刺史 / 尚书 | 开发 +12%，本城金 +6%，本城秋收 +6%，征兵 +4%，人口 +8%，本城忠诚 +1/月 |
| p3b | 五品 T3 | B 财政 | 2 | 典农中郎将 / 司金中郎将 | 开发 +6%，本城金 +9%，本城秋收 +10%，征兵 +8% |
| p4a | 三品 T4 | A 民政 | 2 | 尚书令 | 开发 +18%，本城金 +8%，本城秋收 +8%，征兵 +5%，人口 +15%，本城忠诚 +1/月，全国金 +1% |
| p4b | 三品 T4 | B 财政 | 2 | 大司农 | 开发 +8%，本城金 +12%，本城秋收 +12%，征兵 +8%，全国金 +1.5%，全国秋收 +1% |
| p5 | 一品 T5 | — 宰辅 | 1 | 丞相 / 司徒 / 司空 | 开发 +22%，本城金 +10%，本城秋收 +10%，征兵 +8%，人口 +15%，本城忠诚 +2/月，全国金 +2.5%，全国秋收 +2% |

### 7.3 What each family does for you (player-facing summary)

- **武官**: stronger units on the board (攻 / 防 +3 … +12%). When the holder is the commander, his whole army gains too (三品 A and 一品, +2 … +3%). Higher starting morale. More army and duel crits; branch B (武勇) is the duellist. Faster 训练. **镇守**: the city he sits in gets +2 … +10 城防, so AI attackers think twice.
- **谋臣**: stronger and surer 计策, and an army-wide 识破 against enemy tactics (branch A, 一品). Better 搜索 and 登用, and as envoy better 同盟, 拉拢 and 离间 (branch B, 一品).
- **文官**: bigger 开发 gains when presiding. As the city's administrator: more gold, harvest and recruits per gold, population growth, and monthly loyalty for everyone in the city. At 三品 and 一品 also a small national bonus.
- **Every office**: harder to poach, slower to defect, +loyalty when granted.

### 7.4 Worked examples

- **荀彧 丞相 governs 许昌** (land 560, industry 500, town 420). His 开发 gains 6 + 16 + R(0..4) ≈ 24 → ×1.22 ≈ 29. Monthly gold rises from 81 to 81 × (1 + 0.10 + 0.025) ≈ 91. The harvest goes from 1232 to 1386. Every officer in 许昌 gains +2 loyalty per month (up to 95). Every other own city gains +2.5% gold and +2% harvest.
- **关羽 大将军 commands 5 units.** His own unit's attack and defence are ×1.15 (×1.12 personal + 0.03 command), and each other unit's ×1.03. Army crit is 2 + 7.2 + 0.4 × (L − 1) + 2% (COMBAT-V4), so about 13% at level 7 in 偃月, before gear. In a duel: crit +3pp, damage +2%.
- **诸葛亮 军师将军**: 火计 power ×1.22, success +6pp; every unit of his side resists enemy tactics by 6pp. As envoy for 同盟 with 400 gold gift: +10pp.
- **A small realm (R2, 2 cities)**: 2 九品, 2 七品 and 1 五品 seat. The 五品 might go to 张辽 (偏将军) to deter attacks (城防 +6) or to 陈登 (刺史) to grow the city.

### 7.5 Duel: numbers only (answers COMBAT-V4 Q9)

Titles change the duel only through `critDuel` (≤ 0.03) and `dmgDuel` (≤ 0.02), inside COMBAT-V4's caps. No title changes duel rules: no guard crush, no longer strings, no extra parry window. The duel stays readable, and each general's 8-hit string (COMBAT-V4 §8) is his own and not his title's.

### 7.6 Caps shared with gear

| Number | Title max (post) | Gear max (ITEMS) | Clamp (COMBAT-V4) |
|---|---|---|---|
| Army crit | 0.02 (w4b, w5) | 0.04 | 0.06 |
| Duel crit | 0.03 (w4b, w5) | 0.05 | 0.08 |
| Duel damage | 0.02 (w3b, w4b, w5) | 0.04 | 0.06 |
| 计策威力 | 0.22 (i5) | 0.40 | none; tactics stay within the v3 ratio checks (§12.3) |
| 攻 / 防 | 0.12 + 0.03 command | 0.40 / 0.45 | none; the combined title + gear + level multiplier is checked in §12.3 |
| 本城忠诚 / 月 | 2 | 3 (`loyal`) | max of the two, not the sum; cap 95 |

### 7.7 Random-number contract and switches

- `SG.Titles.enabled` (default true) works like `Progress.enabled` / `Items.enabled`. With false, every hook is identity, `aiManage` does nothing, the UI hides the 官制 button, and the game is bit-for-bit v4-without-titles.
- Titles never call `SG.Random`. With `enabled` the random stream stays aligned (same draws in the same order); only thresholds and amounts change. Paired on/off runs in tests therefore make sense.
- Culture never affects numbers. Classic with or without `world-data.js` (world.js §3 hash test) stays identical. For 孟获, `faction.culture` is 'nanman' with world data and 'han' without, which changes his officers' names, not their numbers.

### 7.8 Value parity between families

`titles.json valueWeights` converts each effect to "points ≈ +1% of the holder's effectiveness in his role". Examples: 1% attack = 1, 1% of the commander aura = 3 (it hits about 3 units), 1 pp 识破 = 2, 1% 本城金 = 1, 1% 全国金 = 4. The build check requires every post to sit within ±20% of its tier mean, and each tier mean to exceed 1.4 × the one below.

| Tier | 武 | 谋 | 文 | Mean |
|---|---|---|---|---|
| T1 | w1 7.5 | i1 6.5 | p1 7.8 | 7.3 |
| T2 | w2 11.6 | i2 11.9 | p2 12.9 | 12.1 |
| T3 | w3a 19.8 · w3b 19.3 | i3a 21.7 · i3b 18.4 | p3a 22.2 · p3b 21.8 | 20.5 |
| T4 | w4a 35.5 · w4b 30.0 | i4a 31.7 · i4b 29.9 | p4a 34.1 · p4b 35.7 | 32.8 |
| T5 | w5 54.6 | i5 49.0 | p5 54.9 | 52.8 |

For scale: one level of 老练 is +4% attack, defence and tactic power, about 12 points on this scale for a fighting general. So a 一品 title is worth about 4–5 levels in its role, and a 九品 about half a level.

---

## 8. AI granting rules

`Titles.aiManage(f)` runs for every AI faction once a month at the start of `StrategyAI.runAI`, before its city loop. It also runs once in `GameState.newGame` for all AI factions. It costs no token and draws no random numbers.

```
aiManage(f):
  validate officers; update rank; suspend / reactivate (§6.6)
  cands = generalsOf(f) − ruler − pending captives
  for t = 5 … 1:
    while active(f, t) < caps[t]:
      best = argmax over cands with curTier < t, no change this month, and a postable post at tier t
             of merit = maxBase + 3 × level           (ties: lower id)
      post = preferred branch if it has a free seat, else the other branch, else the tier's only post
      gift: the richest own city must hold gift + 100, otherwise stop this tier
      grant(best, post)                                 // a promotion frees a lower seat, filled when the loop reaches it
```

Branch preference, deterministic:

- 武: B 武勇 if `war ≥ 92` or `war − intel ≥ 35` (吕布, 张飞, 典韦, 许褚), else A 统帅 (张辽, 夏侯渊, 曹仁).
- 谋: B 外交 if `pol ≥ intel − 8` (鲁肃, 简雍), else A 军略 (郭嘉, 贾诩).
- 文: B 财政 if `war > intel` (rare practical managers), else A 民政.
- Ties of family: the family whose tier still has a free seat, in the order war, intel, pol.

The AI **never revokes or demotes**. It promotes, which frees seats from below. It always uses the default names, the first free one in list order. The prototype measured the result (§12). Titled share of non-ruler officers: about 68% at year 1 and 44% at year 10 (classic). Tier mix at year 10: T1 19, T2 16, T3 11, T4 6, T5 0.2 across the map (classic, without domestic exp).

**委任**: the player can press `按功绩授官` on the 官制 board. It runs the same algorithm for the player's faction and shows a summary before applying. Nothing is ever automatic for the player.

---

## 9. UI and presentation

### 9.1 Main interface

- Top bar: a **官制** button (seal icon: the faction culture's T5 insignia, small) sits next to the **武将** (Generals) button that request 9 adds. Its badge shows how many seats are free with at least one eligible candidate (`2`). A soft pulse (two 0.6 s glows) plays when the number grows: rank-up, level-up or a death.
- A month-start hint, once per new event: `徐晃已可出任五品官。` with portrait. Clicking opens the board on that seat.
- City panel: officer rows show the office name in the family colour under the name (`关羽 · 荡寇将军`). The panel header shows `主政：荀彧（尚书令）`.
- The 任命 command gains a first menu: `太守` (v3 behaviour) / `官爵` (opens the board filtered to this city's officers).

### 9.2 The 官制 board

Desktop 1280×720:

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ [ruler portrait] 曹操 · 州牧   国势 ▓▓▓▓▓▓▓░░░ 7.4 / 10.0 → 公（再授三品 1、一品 1）  │
│                 已授 9 / 10 · 本年礼金 450 金                    [按功绩授官] [×] │
├──────┬───────────────────┬───────────────────┬───────────────────┤
│ 一品 │ 武官 大将军 [锁·需位阶 王] │ 谋臣 军师将军 [锁]       │ 文官 丞相 [锁]         │
│ 0/0  │                   │                   │                   │
│ 三品 │ 前将军 [夏侯惇]       │ 军师中郎将 [＋]        │ 尚书令 [ ]            │
│ 1/1  │ 破虏将军 [  ]        │ 大鸿胪 [  ]          │ 大司农 [  ]           │
│ 五品 │ 偏将军 [曹仁][  ]     │ 军师祭酒 [郭嘉]       │ 刺史 [ ][ ]           │
│ 2/2  │ 虎贲中郎将 [  ]      │ 从事中郎 [  ]        │ 典农中郎将 [  ]        │
│ 七品 │ 校尉 [乐进][李典]     │ 参军 [程昱]          │ 别驾从事 [ ]           │
│ 3/3  │                   │                   │                   │
│ 九品 │ 军司马 [曹洪][  ]     │ 主簿 [  ]           │ 从事 [  ]             │
│ 3/4  │                   │                   │                   │
└──────┴───────────────────┴───────────────────┴───────────────────┘
```

- Rows run from 一品 at the top to 九品. The row header shows `used / cap` and the requirements (`3 级 · 属性 > 80`).
- Columns have the family colours (vermillion, indigo, jade) as paper strips with a soft edge.
- A **seat chip** is 56×64. Filled: portrait, name, chosen name of the post, small family seal. Empty and fillable: a dotted lantern outline with "+" and a warm glow. Locked: a grey seal with the reason on hover or press (`名额已满`, `需位阶 公`).
- A suspended holder shows a hatched paper overlay and the word `虚衔`.
- Phone 844×390: the header shrinks to one line (`州牧 7.4/10`). The three families become tabs (武 / 谋 / 文), each a vertical list of tiers with horizontal seat chips. All touch targets are ≥ 44 px.

### 9.3 Grant flow (from a seat, from the character menu, from 任命 → 官爵)

1. Tap an empty seat. The **candidate sheet** slides up: a list sorted by merit. Each row shows portrait, name, family attribute (e.g. `武力 98`), `Lv 5`, current office or `无`, loyalty, and a status chip (`可任` in green, or the first failed requirement in grey: `需 5 级（现 4）`, `武力需 > 80`, `最高属性为智力`). Ineligible rows stay visible, so the player sees the ladder.
2. Pick a row. The **confirm card** shows:
   - the large insignia icon and the post name in brush style, with the original term under it (`qián jiāngjūn`, or `legatus legionis`);
   - name chips when there are several free names (`前将军 / 后将军 / 左将军 / 右将军`);
   - effects in green lines, with the *change* from the current office (`攻 +8%（+4%）`);
   - `礼金 300 金（自 许昌）`, `忠诚 +8`;
   - warnings: passed-over generals with portraits (`−4`), `本月不能再任免此人`;
   - buttons `授官` (primary) and `取消`.
3. `授官` plays the ceremony (§9.5). Afterwards a toast (`关羽 拜 荡寇将军`), and the board animates the chip into its seat (0.35 s ease-out with an ink-splash settle).

**Revoke / demote**: from the chip's long-press menu or from the character menu. The confirm card shows the loyalty loss in red and the defection warning.

### 9.4 Character menu → 官爵 tab (request 9 owns the screen; this spec owns the content)

`SG.TitleUI.tab(gen) → HTMLElement`:

- **Current office card**: insignia, name and original term, family and tier, since `191年3月`, effects list, and a status (`在任`, or `虚衔——国势不足`, or `被俘中，暂不生效`).
- **Ladder**: five rungs of his family (or families) with names. For each rung a progress row:
  - `等级 ▓▓▓▓░ 4 / 5`;
  - `武力 98 > 80 ✓`;
  - `名额 2 / 2 ✗ 国势达到 10.0 后增加`.
  - The next reachable rung offers `晋升` (opens the confirm card).
- **Actions**: `晋升` / `改任`（同品其它职位） / `罢免`.
- **History**: up to 8 lines from `gen.officeLog` (`190年5月 拜军司马 → 192年2月 迁偏将军 → 195年8月 迁荡寇将军`).
- **For enemy and free generals**, read-only: office and faction (`袁绍麾下 · 前将军`), and the 拉拢 resistance shown in the 拉拢 success estimate.

### 9.5 Ceremony (授官仪式) — Lanternvale style

`SG.TitleArt.ceremony({ gen, ruler, office, culture, size }) → Promise`. It uses the screen stack (`SG.Gfx.pushScreen`) or a DOM overlay for 'small'. It can be skipped by tap, Space or Esc. `prefers-reduced-motion` or the setting `仪式动画: 简` reduces any size to a 0.6 s fade plus the stamp.

| Size | Length | Picture | Sound and voice |
|---|---|---|---|
| small (T1–T2, 许官) | 1.8 s | A paper strip slides across the dialog. The office name writes on in brush strokes (stroke-reveal mask, 0.7 s). The culture's seal stamps it with a vermillion ink splash and a 4-frame squash. The portrait sits in `win` mood | `title-small`: wood block, bell, a short pluck in the culture's instrument |
| medium (T3–T4, rank R2–R3 rise) | 3.0 s | The screen dims to warm lacquer brown with a lantern glow vignette. An edict scroll unrolls from the top (paper fibres, soft shadow). The insignia icon rises with its ribbon swaying (2-bone spring). The name writes on; the seal stamps (camera nudge 0.06); gold-leaf flecks drift for 1 s | `title-medium`: bell, a culture flourish (guzheng gliss / cornu call / karnay / conch …). Voice lines `SG.Voice?.say(ruler, 'grant', { title })`, then `say(gen, 'accept')` |
| grand (T5, rank R4–R5 rise) | 4.5 s | A 2.5D hall card scene: three painted planes (pillars, a hanging lantern row, a dais) in night light with warm lantern pools. The general's figure bows or kneels (`figure.js` clip `kneel` if present, else `bow`). The ruler's figure holds out the seal. The camera pushes in slowly (5%). Petals or embers fall. A full-width brush name with gold edge, then a big seal stamp | `title-grand`: drum roll, gong, culture fanfare. Voice: ruler grant line and general's oath |

All art is generated in code, flat-shaded with ink outlines and warm-top / cool-teal colour ramps. The fixed paper, lacquer and ink-splash textures are canvas-made and cached. Budget: ≤ 20 draw calls, ≤ 30k triangles for 'grand'. 'small' and 'medium' are DOM + SVG + CSS transforms (GPU-composited) with no layout thrash.

Culture flourishes (names for the audio task; fallback to han): han 编钟 + 古筝 · roman cornu + lyre · persia karnay-like horn + daf · india śaṅkha conch + vīṇā drone · kushan conch + lute · korea 장구 + 대금 · wa 太鼓 + 笙 · steppe horse-head fiddle + throat drone · tarim pipa + hand drum · arab oud + frame drum · africa kettle drum + lyre · celt carnyx-like horn + frame drum · german lur horn · sarmatian horn + bells · seasia gong chime · nanman 铜鼓.

Voice lines (request 12; `SG.Voice?.say(gen, key, vars)`): `grant` (ruler: 「封尔为{title}！」, Latin 「Te {title} facio!」…), `accept`, `promote`, `revoke` (resentful), `passedOver` (muttered), `rankUp` (ruler). Every key needs a culture and language variant; the voices task owns the recordings or synthesis.

### 9.6 Rank-up and other moments

- **Rank-up** (晋位): medium or grand by the new rank. The ruler's portrait sits in a frame that changes with rank (R1 plain wood, R2 lacquer, R3 lacquer with gold corners, R4 gold, R5 gold with dragon / eagle / lion crest by culture). The new seats appear on the board with a light sweep.
- **Rank drop**: a grey ink wash over the affected chips, and a news line (§6.6).
- **Battle**: the unit card shows a small insignia and the office name. The tooltip lists the battle effects. A commander's aura draws a faint banner ring around the commander's tile (only when the side has `cmdAtk > 0`).
- **News and log**: `拜/迁/改任/罢免/虚衔/复任/出缺` lines. Other factions' T4+ grants appear only when that faction borders the player.
- **Diplomacy and 拉拢 lists**: enemy officers show their office, and the 拉拢 estimate already includes the resistance.

### 9.7 Icons

`SG.TitleArt.icon(postId, culture, size) → <canvas>|dataURL`, procedural SVG cached per (post, culture, size):

- Shape by tier: T1 small round token, T2 square seal, T3 seal with ribbon, T4 gold seal with long ribbon and tassel, T5 gold seal on a cushion with a soft halo.
- Motif by culture from `insignia[tier-1]` (印绶 colours for 汉; phalera, torques, corona for 罗马; tiara crest for 安息 …).
- Family accent colour on the ribbon or cushion.
- Branch glyph at the corner: 武 A crossed halberds, 武 B fist with flame, 谋 A feather fan, 谋 B furled scroll, 文 A brush, 文 B grain sheaf with coin.

That makes 21 posts × 18 cultures = 378 recognisable icons from about 10 shape pieces and 18 motif sets. The `titles.html` test page shows all of them.

---

## 10. Modules, APIs, save data, Unity

### 10.1 Files (browser)

| File | Owner | Content |
|---|---|---|
| `tools/gen-titles.js` (new) | titles | `design-v4/titles.json` → `js/titles-data.js` (minified, notes kept for the detail page), plus `check` = the build-script checks |
| `js/titles-data.js` (new, generated) | titles | `SG.TitlesData = {…}` (≈ 70 KB minified). Loads after `world-data.js`, before `model.js` |
| `js/titles.js` (new) | titles | `SG.Titles`: pure logic, Node-testable, no DOM. Loads after `model.js`, before `strategy-ai.js` |
| `js/title-art.js` (new) | titles / art | `SG.TitleArt`: icons, ceremony |
| `js/title-ui.js` (new) | titles / UI | `SG.TitleUI`: 官制 board, candidate sheet, confirm card, character-menu tab, rank-up event |
| `tests/titles.js` (new) | titles | Node tests (§12) |
| `tests/titles.html` (new) | titles | Board with a fake game, all 378 icons, every ceremony size and culture, phone preview |

### 10.2 `SG.Titles` API

```js
SG.Titles = {
  enabled: true,
  data,                                         // SG.TitlesData
  families(gen) → ['war'] | ['war','intel'] …   family(gen) → 'war'|'intel'|'pol'
  postsFor(gen) → [post]                        // posts of his families, all tiers
  eligible(gen, postId) → bool                  requirements(gen, postId, f?) → [{ key, ok, text }]
  power(f) → number                             rank(f) → 1..5 (stored, with hysteresis)   rankInfo(f) → { rank, name, power, next, nextName, unlocks }
  caps(f) → [5]                                 board(f) → [{ tier, used, cap, posts: [{ post, seats: [{ gen|null, name, locked, why }] }] }]
  officeOf(gen) → { post, tier, fam, name, orig, attest, note, since, suspended, active } | null
  nameOf(gen) → '荡寇将军' | ''                  names(f, postId) → [Name]      rulerRankName(f) → '州牧'
  bonus(gen, key) → number                      // 0 when off, suspended, captive or no office
  sideBonus(model, side, key) → number          sideRes(model, side) → number
  cityAdmin(city) → gen|null                    cityBonus(city, key) → number      natBonus(f, key) → number
  garrison(city) → number                       effLoy(gen) → number      persuadeRes(gen) → number      discordRes(gen) → number
  canGrant(f, gen, postId, nameIdx?) → { ok, why, cost, payFrom, loyalty, passedOver, name }
  grant(f, gen, postId, nameIdx?) → { ok, why, office, cost, loyalty, passedOver, ceremony }
  revoke(gen, why?) → { ok, loyalty }           demote(gen, postId) → { ok, loyalty }
  promiseBonus(f, gen) → { postId, tier, add }  // 许官 for the hire dialog (§6.7)
  validate(gen)                                 refresh(f) → { rankFrom, rankTo, suspended: [gen], restored: [gen] }
  monthStart()                                  // validate all, refresh all factions, reactivate; returns news lines for the player
  aiManage(f) → [grant results]                 aiManageAll()
  fillByMerit(f, apply?) → [plan]               // 委任 button
  pending(f) → n                                // badge: free seats with an eligible candidate
};
```

### 10.3 Integration points

| File | Function | Change |
|---|---|---|
| `index.html` | script list | `titles-data.js` after `world-data.js`; `titles.js` after `model.js`; `title-art.js`, `title-ui.js` before `strategy-screen.js` |
| `model.js` | `newGeneral` / `newFaction` | defaults `office: null, officeBest: 0, officeLog: []` / `rank: 1` |
| | `GameState.newGame` | after `autoGovernor`: `SG.Titles?.refresh` for each faction, then `aiManageAll()` |
| | `toJSON` / `fromJSON` | §10.4; migration: offices null, rank recomputed |
| | `SG.maxTroops` | unchanged (no troop-cap effect; §12.1) |
| | `Commands.develop / train / recruit / recruitMax / search / hireChance / hire / allyChance / ally / discord / persuadeChance / persuade` | §7.1 hooks; `ally` and `allyChance` gain an optional `envoy`; `hire` takes an optional `promise` and grants on success; `persuade` / `hire` call `Titles.validate` |
| `strategy-ai.js` | `StrategyAI.endMonth` | income, harvest and population multipliers; defection with `effLoy`; `loyAura` after defections; `Titles.monthStart()` at the end, so offices are valid for the next month |
| | `StrategyAI.runAI` | `Titles.aiManage(f.id)` before each AI faction's city loop |
| | `Conquest.apply / release / execute / succession / aiDecideCaptives`, `GameState.checkFactionDeath` | `Titles.validate` / `refresh` |
| | `SG.cityDefense` (in `model.js`) | `+ Titles.garrison(c)` |
| `battle-model.js` | constructor | `morale` |
| | `atkPower`, `defFactor`, `tacK`, `useTactic1` chance, `tacticChance` | §7.1 |
| | COMBAT-V4 crit code | `SG.Titles?.bonus(gen,'critArmy')` |
| `duel-game.js` | crit / damage (COMBAT-V4 §3, ITEMS `duelMods`) | `critDuel`, `dmgDuel` |
| `strategy-screen.js` | top bar | 官制 button and badge |
| | `cmdAppoint` | 太守 / 官爵 menu |
| | `cmdDiplomacy` 同盟 | envoy picker (shared with ITEMS) |
| | `cmdHire`, the search-found dialog, the captive dialog | 许官 option |
| | city panel | office names, 主政 |
| | month start | `monthStart()` news, rank-up event |
| `battle-controller.js` / `battle-view.js` | unit card | insignia and office name; commander aura ring |
| `ui.js` | `SG.UI.item` | optional `badge` (office name in the family colour) |

### 10.4 Save data

```js
gen.office    = { post: 'w4b', name: 2, tier: 4, fam: 'war', since: 2290, seq: 37, faction: 2, month: 2290, suspended: false } | null
gen.officeBest = 0..5            // highest tier granted by the current faction (loyalty bonus once per tier)
gen.officeLog  = [[monthIndex, postId, nameIdx, kind]]   // kind: 'grant'|'promote'|'move'|'revoke'|'demote'|'suspend'|'restore'|'lost'; ≤ 8 entries
faction.rank   = 1..5
G.officeSeq    = n               // grant counter (tie-break for suspension order)
```

- `name` is an index into the resolved name list. It is clamped on load if the data changed. Names are always re-resolved from data, never stored as text.
- About 30 bytes per titled general, so roughly 6 KB in a late world game.
- The version bump is owned by integration (shared v4 save version with ITEMS and COMBAT). v3 saves load with no offices; `rank` is computed on load, `aiManageAll` runs at the next month start, and `GameState.migratedFrom` already tells the UI to show one hint (`新增官爵制度：主公可在「官制」中授官`).
- **Name clash with ITEMS §15**: `G.items.titles` there counts 武林大会 wins (championship titles). Q8 proposes renaming it `G.items.championships`.

### 10.5 Unity port (after the browser build is stable)

| Unity file | From |
|---|---|
| `Data/Generated/TitlesData.g.cs` | `tools/export-unity-data.js` extended to read `js/titles-data.js` (records for tiers, posts, effects, cultures, overrides) |
| `Model/Titles.cs` (new) | `js/titles.js`, the same function names in PascalCase, the same integer and float order |
| `Model/Commands.cs`, `Model/GameState.cs`, `Strategy/StrategyAI.cs`, `Strategy/Conquest.cs`, `Battle/BattleModel.cs`, `Battle/DuelSim.cs` | the §10.3 hooks |
| `Strategy/StrategyScreen.cs`, `UI/UIKit.cs` | board, sheet, confirm card, character tab |
| `View/TitleArt.cs` | icons via `Raster`; ceremony via UI Toolkit plus a small hall scene prefab built in code |
| `Tests~/titles/` | harness: same seeds, JS and C# print per-month office hashes for 10 classic and 4 world years; must match line for line (mulberry32 stand-in as in v3) |

---

## 11. Dependency: experience for domestic work

Titles need levels, and in v3 only battles give exp. A 文官 who never fights stays level 1 and can never pass 九品. The prototype shows the gap. Without domestic exp the AI's 文官 seldom pass T2. With it, T4 and T5 文官 appear and level 7 or higher is common by year 15 (world).

Request 13 ("make the non-battle game rewarding") belongs to another v4 spec. **If that spec defines domestic exp, its numbers win.** Otherwise this fallback applies (`Progress.gain(gen, n, 'domestic')`, cap **20 per general per month**, AI included):

| Command | Exp to |
|---|---|
| 开发 | presiding general 6 (+2 if the gain ≥ 20) |
| 征兵 / 训练 | the general whose troops 3 |
| 搜索 | searcher 4; 12 if a person or treasure is found |
| 登用 | recruiter 10 on success, 2 on failure |
| 外交 同盟 | envoy 12 on success, 3 on failure |
| 离间 / 拉拢 | agent 6 / 15 on success, 2 on failure |
| 输送 / 移动 / 交易 / 赏赐 | 0 |

At the cap a busy governor reaches level 3 in about 10 months, level 5 in about 2.5 years and level 7 in about 4.5 years. That matches the fighting path, where level-ups come mostly from battles.

The prototype used a cap of 30. In world runs that gave many level-7 officers by year 15 (1503 of 9.8k, against 210 without), so 20 is proposed. Levels raise 老练 (+4%/level) in tactical battles, so the request-13 owner must re-run sim.js (7g)/(7h) with domestic exp on.

---

## 12. Test plan and balance targets

### 12.1 What the prototype measured (`design-v4/titles/proto-results.txt`)

All-AI campaigns, same seeds, variants: off, on (titles), dom (titles + domestic exp, cap 30). The final configuration has no monthly salary, a one-time gift, garrison instead of a troop-cap bonus, and value-tuned numbers.

| Metric | off | on | dom | Target |
|---|---|---|---|---|
| Classic 48 seeds × 25 y: factions alive y5 / y10 / y25 | 6.5 / 4.9 / 4.6 | 6.4 / 4.6 / 4.3 | 6.3 / 4.5 / 4.3 | y10 ≥ 4.0 and ≥ off − 0.8 |
| Classic: median time to the first faction > 25% of cities | 2.3 y | 2.3 y | 2.2 y | ≥ off − 0.5 y and ≥ 1.5 y |
| Classic: largest faction's cities at y10 | 17.1 | 18.4 | 18.9 | ≤ off + 3 |
| Classic: defections per seed (25 y) | 2.0 | 2.6 | 2.2 | ≤ off + 3 |
| Classic: titled share of non-ruler officers y1 / y10 | — | 68% / 44% | 69% / 46% | y10 30–60% |
| Classic: active offices by tier at y10 (whole map) | — | 19 / 16 / 11 / 6 / 0.2 | 19 / 16 / 11 / 7 / 0.7 | T5 0.1–3 |
| Classic: gifts ÷ gross income | — | 2.3% | 2.6% | ≤ 5% |
| World 24 seeds × 15 y: factions alive y5 / y10 / y15 | 50.5 / 45.5 / 44.3 | 49.7 / 45.1 / 44.0 | 49.4 / 44.8 / 43.8 | y15 ≥ off − 2; all world.js checks still pass |
| World: largest faction y10 (Rome) | 24.3 | 24.8 | 25.2 | ≤ off + 3 |
| Tactical, classic year-8 titled armies, 480 battles per mode: attacker win, no title effects / both / attacker only / defender only | 46.3% | 45.6% / 47.9% / 45.0% | — | both − none within ±3 pp; attacker-only − defender-only swing 1–8 pp |
| Tactical: defender ÷ attacker losses, none / both / atk only / def only | 1.12 | 1.12 / 1.18 / 1.08 | — | the titled side trades 3–10% better |

Rejected options, all measured: monthly salary at any level (3/5/8/12/18, 2/3/5/8/12, 1/2/4/6/10, 0/0/4/6/10) cost 0.6–1.4 factions alive at year 10 and multiplied defections. A troop-cap bonus of +100 … +500 (also at half size) cost about 0.5 factions alive at year 10 and sped up the first > 25% faction by 0.3–0.6 y. A gift of 100–800 was too steep. An AI recruit reserve (env `KEEP`) made no measurable difference, so it is not part of the spec.

### 12.2 `tests/titles.js` (new, Node, exit code = failures)

1. **Data**: everything `build-titles-json.js check` checks (schema, effect keys, sources, A/B need a source, distinct names per resolved faction for all 70 world factions and 18 classic, no clash with ruler ranks, value parity ±20%, tier means rising ×1.4, one seat per T5 post). Every culture of every faction resolves all 21 posts.
2. **Culture never changes numbers**: for every faction, `bonus` over all keys equals the han reference for the same post.
3. **Eligibility**: table tests (关羽 → war; 荀彧 → pol; 姜维 → war+intel; level and attribute edges 80/81, 90/91; ruler, captive, dead rejected).
4. **Rank**: `power` on hand-made cities; thresholds; hysteresis (6.0 → R3; drop at 5.39, not at 5.41); caps.
5. **Grant rules**: seats, post seats, one change per month, gift and payer, loyalty once per tier (grant, revoke, regrant gives 0), promotion difference, lateral is free, passed-over penalty (exact set and cap 3), revoke and demote penalties, suspension order (newest first) and reactivation (oldest first), captive inactive, release restores, hire by captor clears, succession clears.
6. **Hooks**: each §7.1 formula with titles on equals the hand computation; with `enabled = false` every hook is identity.
7. **Random contract**: an `SG.Random` call counter wraps `value` / `rangeInt` / `rangeFloat` during `aiManageAll`, `grant`, `monthStart` and `bonus` and must read 0. A 5-year classic all-AI run with titles off must equal v4-without-titles (md5 of the JSON, like world.js §3).
8. **AI determinism**: the same seed gives the same offices; the AI never revokes; no grant without the gift + 100.
9. **Campaign balance** (the §12.1 targets; 48 classic seeds × 25 y and 24 world seeds × 15 y, about 3 minutes; `--quick` uses 12 / 6).
10. **Tactical balance** (the §12.1 targets with the real hooks, including `tacHit` / `tacRes` / `critArmy`, which the prototype left out).
11. **Performance**: `aiManageAll()` ≤ 2 ms per month on the world map (70 factions, 499 generals), and `monthStart` ≤ 1 ms. world.js's 60 ms per-month CPU cap must still pass.

### 12.3 Changes to existing tests

- `tests/sim.js` (7f) compares campaigns with progression on and off and expects identical city ownership. Titles depend on levels, so (7f) sets `SG.Titles.enabled = false` for both runs. (7g)/(7h) measure progression and also run with titles off. New section **(8) titles**: a short version of §12.2 items 6, 7 and 10, plus the ratio checks with titles on — v3 (7g) "damage per battle ≤ 1.35 × off" and the learned-special ratios stay within their bands at year 8 with titles on.
- `tests/world.js` (2) loads only `model.js` and `strategy-ai.js`, so nobody gains exp and only T1 offices appear. Add a variant (`--titles`) that also loads `battle-model.js`, the specials, tactics and titles files, and checks the same region and Rome assertions.
- `tests/duel-bots.js`: a mirror match where one side has a T5 war office (`critDuel` +0.03, `dmgDuel` +0.02) must win 52–58% (an estimated +4 pp, about +4 武力 of DPS) and never more than +8 pp. With ITEMS gear at its caps the combined bonus stays within COMBAT-V4 §15's limits.

### 12.4 Pacing targets (player-facing)

- In a classic player game where the player fights about one battle every 2 months, the player's best general reaches level 5 (T4) by about year 3 and level 7 (T5) by about year 5–6, measured by the random-player harness (sim.js (2)) with a player-side exp log. With domestic exp (§11) a busy governor reaches T4 by about year 3.
- First T3 seat: R2, typically the first conquest or about 18 months of development.
- The 官制 badge is non-zero in at least 60% of the player's months in the first 3 years, so there is always something to grant (random-player harness).

---

## 13. Research notes and sources

The proxy blocked WebFetch in this session for wikipedia.org, wikisource.org and ctext.org (403), so facts were checked through WebSearch results and the sources they cite, plus standard references. Every name in `titles.json` carries `attest` and `src`. Grade A rests on a source of the culture's own period. B is the right culture, wrong period, and the detail page says so. C is invented and descriptive.

- **Han** (A, 47 names): 《后汉书·百官志》 for the 将军 order (大将军, 骠骑, 车骑, 卫; 前后左右), the camp organisation (部校尉 比二千石, 军司马 比千石, 军候, 屯长), the 北军五校, the 中郎将 of the 光禄勋, 谒者仆射, 九卿, 尚书令 / 尚书, 刺史 六百石. 《汉书·百官公卿表》 for seals and cords. 《三国志》 for the attested holders: 诸葛亮 / 庞统 军师中郎将, 诸葛亮 军师将军, 荀攸 军师, 郭嘉 军祭酒, 简雍 从事中郎 「往来使命」, 任峻 典农中郎将, 荀彧 尚书令, 孙坚 破虏, 孙策 讨逆, 关羽 偏将军 / 荡寇 / 前将军, 曹操 奋武 / 丞相, 于禁 虎威. Searched pages: [zdic 墨綬 (表文)](https://www.zdic.net/hans/墨綬), [孙正军《汉武帝朝的秩级整理运动》](https://www.thepaper.cn/newsDetail_forward_9420956), [Nine Ministers](https://en.wikipedia.org/wiki/Nine_Ministers).
- **Korea**: 《三国志·东夷传》 / 《南史》 for the Goguryeo offices (相加, 对卢, 沛者, 古雏加, 主簿, 优台, 使者, 皂衣, 先人; 对卢 and 沛者 not held together) and Buyeo's 六畜 offices. 《三国史记》 for 左辅 / 右辅, 国相 (166; 乙巴素 191, 赈贷法 194), Silla's 17 官等 and Baekje's 佐平 (260). [南史 卷79](https://zh.wikisource.org/wiki/南史/卷79). Late Goguryeo offices (莫离支, 大模达, 傉萨, 太大兄) are B.
- **Wa**: 魏志倭人传 for 卑狗 / 卑奴母离, Yamatai's four officials, 一大率 「于国中有如刺史」, 大倭, 大夫, 率善中郎将 / 率善校尉 (239). [Wajinden](https://en.wikipedia.org/wiki/Wajinden); [京都大学 three seals paper](https://repository.kulib.kyoto-u.ac.jp/dspace/handle/2433/240136). Yamato kabane and 安东大将军 are B.
- **Steppe**: 《史记·匈奴列传》 for the 24 chiefs (左右贤王 … 骨都侯, 万骑, 千长 / 百长 / 什长, 裨小王, 相, 当户, 且渠): [zdic 什长](https://www.zdic.net/hans/什長), [Chinese Notes 史记 110](https://chinesenotes.com/analysis/shiji/shiji110_analysis.html). 《汉书》 for 日逐王 and 僮仆都尉. 《后汉书》 for the Wuhuan 小帅 / 大人 and 檀石槐's three 部大人.
- **Tarim**: 《汉书·西域传》 offices of 龟兹 and 鄯善, and 译长: [儒家中国](https://www.rujiazg.com/index.php/article/31356), [origins of the Western Regions' offices](https://www.sinoss.net/c/2013-04-26/527189.shtml). Niya Kharoṣṭhī documents for cojhbo, sothamga, ogu and the royal style.
- **Parthia / Persis**: Encyclopaedia Iranica [Spahbed](https://iranicaonline.org/articles/spahbed) (hereditary in a great house), [Bidaxš](https://www.iranicaonline.org/?p=9882), [Argbed](https://www.iranicaonline.org/articles/argbed) (Ardashir at Darabgerd), [Hazārbed / dibīrbed](https://iranicaonline.org/articles/hazarbed). Strabo 11.9.3 for the council of kin and of sophoi and magoi; [Aspbed](https://en.wikipedia.org/wiki/Aspbed). Sasanian court titles from ŠKZ are B.
- **Kushan**: [Iranica Jabguya](https://iranicaonline.org/articles/jabguya) (yabgu = 翕侯); kṣatrapa / mahākṣatrapa (Sarnath, year 3); karalrang (Surkh Kotal and Rabatak; `research/v3/india-kushan.md` on Nokonzoko); [UW Silk Road: Kushans](https://depts.washington.edu/silkroad/exhibit/kushans/essay.html).
- **India**: Satavahana amātya, senāpati / mahāsenāpati (Nasik Cave 20, Bhavagopa, about 180 AD, in the game as 跋婆瞿波), mahārathi, mahābhoja, mahātalavara, mahattaraka, bhāṇḍāgārika: [study material](https://iasexamportal.com/civilservices/Study-Material/Indian-History-pg14), [CBC Satavahanas](https://cbc.gov.in/cbcdev/satavahanas/satavahanas.html). Sangam five assemblies: [CBC Sangam](https://cbc.gov.in/cbcdev/sangam/sangam.html). Enadi / kaviti / eṭṭi rest on textbook summaries (B, check). Arthaśāstra terms are B (date disputed).
- **Rome**: [Centurion](https://en.wikipedia.org/wiki/Centurion) (legionary ladder), [Livius: prefect](https://www.livius.org/articles/concept/prefect/), [CSUN equestrian bureaucrats](https://www.csun.edu/~hcfll004/equesbureaucrats.html) and the [Strachan table](https://www.strachan.dk/procurator/proc_praefectus.htm); frumentarii, comites Augusti and consiliarii; Commodus' prefects (Perennis, Laetus).
- **Arabia**: [Hatran inscriptions](https://en.wikipedia.org/wiki/Hatran_inscriptions) (marya → malka 「阿拉伯之王」 from the 170s); Palmyrene and Nabataean strategos, hipparchos, synodiarch; Sabaean qayl and kabir. Several are B pending a check.
- **Kush / Aksum**: Meroitic pesheto, pelmes (of the water / of the land) and apote (envoy); Philae 416, a Meroitic envoy to Roman Egypt: [U Chicago dissertation proposal](https://isac.uchicago.edu/sites/oi.uchicago.edu/files/uploads/shared/docs/bumbaugh_2009_diss_proposal.pdf); [Qore](https://en.wikipedia.org/wiki/Qore_(title)). Aksumite negusa nagast (3rd–4th century inscriptions) and ḥaḍani (later) are B.
- **Celts, Germans, Sarmatians, Moors**: early Irish law (rí, ruiri, rí ruirech, tánaise ríg, aire grades, ollam, brithem, druí; B, 7th–8th century texts); Tacitus, *Germania* 7, 10, 12–14 (reges, duces, principes, comitatus, centeni, priests and lots) and *Historiae* 4.61 (Veleda); Gothic Bible terms (B); Bosporan inscriptions (CIRB: chiliarchos, nauarchos, politarches; [Sauromates II](https://en.wikipedia.org/wiki/Sauromates_II)); the Volubilis altars for princeps gentis Baquatium.
- **Funan / Linyi / Nanzhong**: 《梁书·扶南传》 (范蔓 为大将, 自号扶南大王, 大船, 乘象, 混填 婆罗门); 《晋书·林邑传》 (区连 功曹之子); 《华阳国志·南中志》 (夷帅, 渠帅, 耆帅, 大姓, 部曲, 巫鬼).

**Fact-check list before merge** (B and C items most likely to be challenged): 军师祭酒 versus 军祭酒 wording; 司金中郎将; enadi / kaviti / eṭṭi; nuhadra, rabbaita, maqtawī, epitropos; pelmes adab; pqr as commander; Bosporan ho epi tes basileias and aularches; the dating of Silla's 17 ranks (legendary); 优台丞 versus 优台 (the 三国志 and 南史 texts differ).

---

## 14. Open questions

- **Q1. Domestic exp ownership** (§11). Does the request-13 spec own it? The fallback's 20 per month cap also changes 老练 levels in battles.
- **Q2. Should ruler rank change anything besides seats?** For example, AP bonus, diplomacy weight, or declaring 王 / 皇帝 provoking neighbours. Proposed: v4 seats only, with flavour in names and frames.
- **Q3. Seat caps for the world's one-general factions.** These 15 factions never use titles. Accepted, or give every faction one free 九品 seat for its first hired officer?
- **Q4. Titles in 武林大会** (request 14): should the tournament show offices on the bracket and give a champion's office ceremony? Proposed: show them; prizes stay items (ITEMS §8.9).
- **Q5. A 'grand' ceremony needs `figure.js` kneel / bow clips and a seal prop.** Add them to the art pilot's list?
- **Q6. AI use of 许官.** Off in v4 to keep the AI hire path unchanged. Turn it on later with a merit check?
- **Q7. Gift payer.** The holder's city first, then the richest own city. Should a capital-only treasury be simpler for the player?
- **Q8. Field-name clash with ITEMS §15** (`G.items.titles` = tournament wins). Rename it to `championships`?
- **Q9. Naming conflicts with in-game offices.** The han R1 ruler rank is 郡守 to avoid «太守» (the city office). The han T3-A post is 刺史 while 州牧 is the R2 ruler rank. Acceptable to the writers?
