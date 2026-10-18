# 三国志-霸王的世界 · 第四版设计契约（DESIGN-V4）

第四版在第三版（`DESIGN-V3.md`）基础上进行。先读 `DESIGN-V2.md`（共同规则 §1、文件约定、文化代号 §6）、`DESIGN-V3.md`、`CONTRACT.md`、`UI-CLASSES.md`，再读本文件，然后读 `design-v4/` 下与自己任务有关的规格。**优先级**：本文件 > `design-v4/INTEGRATION.md`（凡是跨系统的规则：加成上限与合成顺序 `SG.Mods`、内政公式 `SG.Econ`、电脑花钱 `SG.AIBudget`、存档与迁移、月末流程、各类登记表、测试矩阵）> 各系统规格（在自己的领域内）。平台：先网页版，稳定后移植 Unity（`../sanguozhi2-unity`）。美术：一律按美术试点定下的 **Lanternvale 风格**（`js/lowpoly.js` 1.0、`js/lp-props.js`；风格化低多边形、温暖的吉卜力配色、平面着色的建筑道具、上暖下青的软团树冠、墨线、柔和的绘画式光照与夜晚的暖色灯池），所有模型、图标、贴图仍在代码中生成，不下载素材。设计阶段没有改动任何游戏代码，也没有提交。

## 0. 玩家的要求（第四版，原话摘要）

1. Menu and settings buttons on the main interface.
2. Art restyle to match the player's game Lanternvale: stylized low-poly, cozy Ghibli-warm, flat-shaded props / buildings, soft-lump foliage with warm-top / cool-teal ramp, FFX-inspired characters, ink outlines, soft painted lighting with warm lantern pools at night (handled by the art pilot; every visual below assumes this style).
3. Shops in every city selling weapons, armour and other equipment for generals: over 250 weapons and 250 armour / equipment; standard items in many cities, good ones only in a minority, very rare ones in a few cities of the world; unique weapons only obtainable by slaying the ruler of each power; research historical figures' weapons and also use mythology; unique and rare items have unique special abilities (cannot be learned) with epic appearance, animation, effects and impact, each with its own icon, appearance and model; rarities common / uncommon / rare / epic / legendary — exactly 4 legendary and 20 epic.
4. The army attack animation (clash) a bit slower; critical strikes for army attacks and in duels.
5. Duel: block by holding back (like normal fighting games); a combo system — each general has a unique 8-hit combo of light, medium and heavy attacks; combos can be interrupted by parry.
6. Every domestic command (开发, 搜索, 征兵, 训练, 登用, 移动, 输送, 外交, 赏赐, 交易, 任命, 出征 …) gets a cool, helpful animation with unique fitting music and a unique recognizable icon; cool button animations; good results get a more exciting animation and sound.
7. Different rulers get different music suites.
8. Zoom into a city to see its model — farms, stables, barracks etc. look better with more features as the city advances.
9. A character menu per general (all gear, skills, army troops …) for managing each general; a Generals button on the main interface.
10. Rulers assign titles to generals: level requirements, some need one attribute > 80, the best need > 90; the title depends on the highest attribute; gameplay benefits for domestic development, diplomacy, training or fighting / duelling — balanced.
11. The main interface looks ugly — more animated and attractive, less like an AI game, properly designed buttons.
12. Voices for each main-menu action and in battles, depending on the ruler and the language they speak.
13. Make the non-battle game more fun and rewarding so players don't always need to fight.
14. On the first month of each year a 武林大会: each ruler sends up to 5 generals; a grand epic stadium featuring major historical buildings / places; top 3 win prizes: 1st a random rare weapon / armour / skill / equipment, 2nd lower tier, 3rd lower still; all three get gold.

## 1. 文档地图

| 文档（`design-v4/`） | 对应要求 | 负责的系统 |
|---|---|---|
| `INTEGRATION.md` | 全部 | 加成上限与合成（`SG.Mods`）、内政公式（`SG.Econ`）、电脑预算（`SG.AIBudget`）、存档 v4 与迁移、月末流程、登记表、载入顺序、`SG.Balance.CityLevel`、测试矩阵；全开原型的实测 |
| `ITEMS.md` + `items.json` | 3（及 9、13、14 的装备部分） | 602 件装备、商店、获取途径、神兵之能、电脑买装 |
| `COMBAT-V4.md` | 4、5 | 攻击画面放慢、军队会心、单挑 v4（后退防御、中攻、8 连段、看破连段、单挑会心）、`DUEL_ITEM_EFFECTS` |
| `DOMESTIC-FX.md` | 6（及 13 的表现） | 指令演出、结果分级、37 个指令图标、34 段指令短乐 |
| `AUDIO-V4.md` | 7、12 | 君主乐章（499 套）、人声（26 种语言、123 条台词） |
| `CITY-VIEW.md` | 8 | 城池视图：16 种建筑 × 12 个文化家族 × 5 阶 |
| `UI-V4.md` + `mockups/` | 1、9、11（及 3、10、14 的界面） | 漆 · 纸 · 铜 · 灯的界面语言、主界面、菜单、设置、武将录、人物菜单 |
| `TITLES.md` + `titles.json` | 10 | 官爵：21 个官位、18 种文化的官名、席位、授官、效果 |
| `ENGAGEMENT.md` + `engagement.json` | 13（及 3、8、10、14 的部分） | 民心、声望、邦交、羁绊、事件、国策、营建、商队、探索、文会、节庆、功业、王道 |
| `TOURNAMENT.md` | 14 | 武林大会：日程、主办、参赛、赛制、奖励、万国擂台 |
| `ASSETS.md`、`integration/lp-recipe.md` | 2（及所有美术） | 资产总表、阶段与替代品、削减顺序；声明式低多边形配方格式 |

## 2. 美术（要求 2）

- 美术试点已通过的风格是唯一标准：`SG.LP.MeshBuilder`（顶点色、每面亮度抖动 0.04–0.08、贴地 AO、墨线外壳）、`SG.LPProps`（85 个道具配方与建筑部件）、`SG.LP.Mood`（黎明 … 夜，灯池）。
- 第四版的数千个新资产（城池建筑、工程、擂台、装备部件）写成**声明式配方**（`integration/lp-recipe.md`）：数据而非代码，有三角形预算、文化家族的回退链（`id.家族.皮肤 → id.家族 → id`）、自动校验（`tools/lp-recipes.js check`）和 Unity 解释器（同一份 JSON 在 `MeshBuilder.cs` 上生成同样的网格）。
- `ASSETS.md` 列出每一类资产的数量、预算、是否要等美术批准、阶段（P1 / P2）与替代品，并给出削减顺序（地标只做开局都城的、事件画 30 幅、主办门 2 种、单挑新动作先用映射……），**没有一个系统要等美术才能运行**。

## 3. 主界面、菜单、设置（要求 1、11）——`UI-V4.md`

- 视觉语言 **漆 · 纸 · 铜 · 灯**：漆木框、纸页、铜钮图标按钮、灯光表示悬停与选中、朱印确认、墨线勾边；59 个颜色令牌（含连段三色 `--combo-l/m/h` = `#5fb3a8 / #e0a24a / #d2553a`），所有文字对比度 ≥ 4.5 : 1。
- 主界面（1280 × 720）：顶梁 58 px（武将 · 官制 · 势力 · 大事 · 设置 · 菜单）、右侧城池面板 376 px 内的 **4 × 4 指令坞**（治 开发 营建 征兵 训练 · 人 搜索 登用 赏赐 任命 · 运 移动 输送 交易 市场 · 策 通商 探索 外交 出征；快捷键 `1–9 0 - =` `Z X C V`）、左侧案头 ≤ 4 张、结束本月的朱印；手机 844 × 390 另行布局，并在左右各 47 px 安全区下测试（溢出顺序见 UI §15.1）。
- 菜单 7 项、4 个存档位；设置 6 页 **53 行**，由 `SG.Settings` 登记表统一渲染（各模块登记自己的行：第 51 行内政委任、52 行节庆照此办理、53 行武林大会出战方式）。
- 一天一个时钟（INTEGRATION §10.7）：月初 07:00 → 用完令牌 20:00，电脑回合入夜；地图、城池视图、界面共用。
- 月初的非交互演出（年鉴、功业印、升阶、秋收、换月）合计 **≤ 8 秒**，多出的变成案头或新闻。

## 4. 武将录与人物菜单（要求 9）——`UI-V4.md` §8–§9

- 顶梁「武将」按钮 → 武将录（8 列、4 类筛选、5 种状态、8 种排序；我方 · 他国 · 在野）。
- 人物菜单 8 页：总览 · 装备 · 武艺 · 计略 · 部队 · 官爵 · 关系 · 履历；300 px 三维展台（穿着他的装备）；总览里的攻防会心数字由 `SG.BattleMath.unitPreview`（与战斗同一套 `SG.Mods`）算出，绝不与战斗不符。内容分别由 ITEMS、COMBAT、TITLES、ENGAGEMENT、TOURNAMENT 提供。

## 5. 装备与商店（要求 3）——`ITEMS.md`、`items.json`

- **602 件**：兵器 310（主手 239、副手弓弩投掷 71），甲胄装备 292（甲 51、盔冠 40、盾 19、坐骑 44、宝物 138）；普通 273 · 精良 133 · 稀有 172 · **史诗 20 · 传说 4**（赤兔、传国玉玺、金刚杵、赫拉克勒斯之棍）。18 个文化代号都有装备；新增的 35 件汉代与周边兵器各有出处（考工记、过秦论、说文、汉书、史记……）。
- **两个剧本都过 250**：经典剧本在 15 % 的「异域奇珍」之外可得兵器 256、甲胄装备 273（`build.js check` 按剧本检查）；经典剧本只有 3 件传说，赫拉克勒斯之棍只在世界剧本（斩杀康茂德）。
- 商店 4 级（村市 4 格 … 都会 10 格），按产业 + 町与商路枢纽定级；普通货在本文化所有城市，精良约 35 %，稀有只在 1–3 座城，唯一的史诗大宛天马只在大宛（经典：武威）。按月以种子补货，不动 `SG.Random`。
- **专属兵器**：70 个势力（经典 18）各一件，绑定在君主身上，**只有斩杀君主才能得到**（处斩，或战场上会心击溃 / 单挑击倒时的阵斩判定）；任何奖池、赏赐、商人都不会出现专属兵器。
- 稀有及以上 196 件各有不可学的「神兵之能」；**每件稀有都有自己的标志部件**（≤ 200 三角形，部件库 79 种）、一个专属特效（≤ 0.6 秒、1 次绘制）与音效，并能在人物菜单里「演示」。
- 赐宝物的忠诚**每件实物只算一次**，收回时扣回同样多（杜绝一件凡品轮流赏遍全军）。
- 单挑里装备只能通过白名单 `DUEL_ITEM_EFFECTS` 起作用，并受「全合一单挑预算」约束（见 §7）；装备武力在单挑中只算三分之一（上限 +3）。

## 6. 攻击画面与军队会心（要求 4）——`COMBAT-V4.md` §1–§2

- 攻击画面 3.96 → **4.86 秒**（+23 %），用一张分段时间映射 `tlMap` 改全部时刻；快 ×2.5 仍为 2.18 秒，敌方回合 ×1.7、委任 ×2.0。速度 ≥ 1.5 时音乐改用一小节的 `clash-short`，重音对准溃散帧（接触后 1.62 / 速度 秒）。
- 军队会心：`2 % + 0.15 % × (武力 − 50) + 0.4 % × (老练等级 − 1) + 阵型（−2 … +4 %）+ 装备与官职（≤ +6 %）`，上限 25 %，伤害 ×1.5（装备可到 ×1.8）；等级用 `vetLevel`（非战斗经验按一半算）。只有攻击方的普通攻击会心，反击、计策、自动战斗不会。全开实测：会心占攻击 7.0–8.2 %，攻方胜率变化 −0.2 … −1.5 个百分点。

## 7. 单挑 v4（要求 5）——`COMBAT-V4.md` §3–§15

- 按键 **J 轻 · K 中 · L 重**（按住蓄力），K+L 跃劈；**后退即防御**（带近身判定），下 + 后 = 蹲防；下段与跃劈形成上下段二择（可关）。
- **每个武将一串独有的 8 连段**（轻 / 中 / 重组成；790 种合法串用了 499 种，65 串手工编写），终结技有名字；伤害按段数递减（100 … 60 %），全中约 28 HP。
- **看破连段**：从第 3 段起，在受击硬直中格开（窗口按来招轻 / 中 / 重为格开窗口的 0.55 / 0.65 / 0.80）。
- 单挑会心 3–7 %（武力）+ 装备官职（≤ 3 个百分点，计入预算）+ 破绽 15 % · 终结 10 % · 刀尖 8 % · 背水 5 %，上限 35 %。
- **全合一单挑预算**（每名武将）：伤害 % + 0.4 × （装备体力 + 神兵之能折算体力 + 0.5 × 格挡点）≤ 6；官职 `dmgDuel` 与宿敌 +4 % 都并入伤害项。实测（v3 模拟，每格 300 场）：同武力下传说全套对无装备 69–73 %；武力 65 + 传说全套对 80 只有 23 %。武器类型平衡（各类对长剑 45–55 %）要在 COMBAT P1 调好，然后冻结武林大会的 `WIN_SCALE`。

## 8. 内政演出（要求 6）——`DOMESTIC-FX.md`

- 每条指令一段小剧（普通 ≈ 2 秒、好 ≈ 3 秒、大成 ≈ 4 秒，上限 4.5 秒，长度等于它的短乐），在城池视图里、画卷小窗里或纸牌上演；任意键 100 ms 内跳过；设置「内政演出」智能 / 完整 / 精彩 / 快速 / 关。
- 结果分四级（失手 · 普通 · 好 · 大成），是结果记录的纯函数，不取随机数；**按乘成之前的数值分级**（开发用 `g0`，秋收用 `rollRaw`），装备官职只让数字变大、不让每次都「好」。全开实测：开发大成 10.1–10.9 %、好 24.5–25.3 %；秋收歉收 15–17 %、好 25–30 %、丰年 7–10 %。
- **37 个图标**（含 ENGAGEMENT 新指令的 11 个），每个 24 px 剪影与其他图标至少相差 18 %；**34 段短乐 × 4 档 × 12 个文化家族 = 1 284 个键**全部校验通过（`node design-v4/domestic/stings.js`）；营建、通商、探索、外交六项、归附、国策、节庆、文会都有演出、短乐与台词（§5.18–§5.28）。

## 9. 君主乐章（要求 7）——`AUDIO-V4.md` 甲部

- **499 名武将每人一套**（任何人都可能继承势力）：42 套手写主题动机（经典 18 位君主、4 位继承人、20 位世界君主），457 套按规则生成并冻结；每套 12 首（地图、议事、战斗、守城、胜、败、号角、四种交锋短乐、一统天下）。
- 每套的主题动机贯穿全曲：地图开头陈述并在每轮循环开头重述，**每轮循环里有 10 小节的发展段 D**（上二度模进、原形、倒影、扩大），战斗循环开头有号令，经典君主的战斗在 B 段前再奏一次号令。**地图与战斗循环中至少 25 % 的小节出自动机**（实测地图最少 27 %、战斗最少 25 %；`tests/suites.js` 断言）。同家族任意两位君主在主奏乐器、速度或调上至少差一项。

## 10. 城池视图（要求 8）——`CITY-VIEW.md`

- 「入城」进入 160 × 160 米的城池模型：**16 种建筑 × 12 个文化家族 × 5 阶**，每座建筑随实际数值升阶（农田 = 土地，工坊 = 产业，民居 = 町，市 = 商店等级……；阈值 300 / 450 / 600 / 800，回落有 5 % 滞后），29 座名城地标，ENGAGEMENT 的 13 项营建在此可见（脚手架 → 半成 → 落成）。
- 城级（邑 · 县城 · 郡城 · 大郡 · 都会）是规则表 `SG.Balance.CityLevel`，规则（声望、奇观、商店等级、主办资格）和画面读同一张表，美术调整不会改动胜负条件。
- 预算：大建筑每阶 ≤ 3 000、营建 ≤ 900（奇观 ≤ 2 000，中精度减半）；全 S5 并建满 13 项的都城桌面 215 640 / 250 000、手机 128 960 / 150 000（含墨线）。指令坞在桌面靠右，手机为一行按四组色条分组的底栏。

## 11. 官爵（要求 10）——`TITLES.md`、`titles.json`

- 三系（武官 · 谋臣 · 文官）按**最高的基础属性**定系，每系 5 品（九品 … 一品）共 **21 个官位**；要求：T1 1 级 · T2 2 级 · T3 3 级且某属性 > 80 · T4 5 级且 > 80 · T5 7 级且 > 90。18 种文化各有官名（402 个名字，附原文与出处），名字不改变数值。
- 席位随国势（R1–R5）增加；授官一次性礼金 50 … 500，无月俸；忠诚 +5 … +20（每品只给一次），防叛 +4 … +20（与羁绊防护取较大者）。三系价值指数每品相差不超过 ±20 %。
- 所有效果都是 `SG.Mods` / `SG.Econ` 的一项：攻防是战斗乘积里的一个因子（总乘积 ≤ ×1.60）、开发与城收入在统一上限内、单挑只给会心与伤害（计入预算），不改规则。电脑每月在「自由支出」阶段按功绩授官，礼金走 `SG.AIBudget`。

## 12. 人声（要求 12）——`AUDIO-V4.md` 乙部

- 每个人说**自己的语言**：26 种语言（A 级 14 种现代或现代替身语言、B 级 7 种古典语言、C 级 5 种只用已考证的词）覆盖 70 个势力与 18 个文化代号；字幕给原文与中文。
- 台词登记表 **123 条**（20 条核心在每种语言都有；其余有回退链）。玩家最常听到的 **26 条「开始 / 大成」台词翻成全部 14 种 A 级语言**；传令官、胡商用非武将的声音配置。v4.0 共 1 381 段（约 34 分钟，Opus 16 kbps 4.38 MB）。离线神经 TTS 生成，程序化的喊叫补足用力声与群呼。`node design-v4/audio/voice-lines.js` 还会扫描所有规格，找不到的台词编号即报错。

## 13. 非战斗玩法（要求 13）——`ENGAGEMENT.md`

- 新的核心状态：**民心**（0–100，影响收入 / 收成 ±5 %、人口、事件）、**声望**（不衰减；每 150 换 1 点人望，与事件、装备人望合计 ≤ +10）、**邦交**（−100 … +100 与盟、婚、贡、停战、通商各种盟约）、**羁绊**（结义、亲族、夫妻、知己、师徒、宿敌、不和）。
- 内容：33 种事件（每月 ≤ 1 张随机卡 + ≤ 1 张定期卡；武林大会不占名额）、17 种国策、13 项营建、商队（利润 4–30 %）、44 处探索、文会（9 月）、12 种文化的节庆、48 项功业、**王道胜利**（声望 1 500、平均民心 75、两个大郡或一个都会、四邻和睦、24 个月不出兵）。**内政经验**由本规格负责（每人每月常规上限 20，装备经验加成在上限之前乘）；老练按非战斗经验一半计。
- 实测（脚本化的和平玩家，全部系统开启）：经典 20 年存活 89 / 100 %（v3 为 83 / 94 %），王道 14 / 19 %；世界 15 年存活 96 %（v3 92 %），王道 8 %（24 局中 2 局，噪声大，不设门槛）。

## 14. 武林大会（要求 14）——`TOURNAMENT.md`

- 每年正月（首届 191 年，**193 年为第三届**），由声望最高、近两年未主办的势力主办；各势力最多派 5 人（电脑：武力 ≥ 72 / 75），经典开局约 46 人（64 签），世界约 71 人（128 签）。单败淘汰、种子与轮空、同势力尽量分开、决赛三局两胜、另有季军战。比赛用 v4 单挑：不死不俘，KO 负者 6–22 % 轻伤（`gen.injury`，武力 −5 两个月）；玩家亲自出战的比赛不受「单挑速度」设置影响。
- 奖励：冠军稀有装备（75 %）或武林秘传招式（25 %）+ 400 金 + 声望 50 + 人望 1；亚军三选一精良品（或 15 % 秘传）+ 200 金；季军三选一凡品 + 100 金；奖池是「没有任何势力拥有的商店稀有品」加 12 件大会专属奖品，**永不出现专属兵器**；三冠者得金刚杵。
- **万国擂台**：16 个方位，12 个文化家族各有一座地标立面（汉阙与平乐观华盖、罗马斗兽场、帕提亚伊万、佩特拉卡兹尼神殿、桑奇塔门、麦罗埃金字塔、波斯波利斯万国之门……）；单挑视角桌面 ≤ 110 k 三角形、≤ 60 次绘制。

## 15. 整合（跨系统）——`INTEGRATION.md`

- **加成上限**（`SG.Mods`）：战斗中装备 × 官职 × 羁绊 × 光环的乘积 ≤ ×1.60（攻、防各自），负面乘积不低于 ×0.5，老练不在上限内；开发 ≤ +80 %；城收入 −10 … +35 %；收成 −20 … +35 %；征兵每金 ≤ +40 %；搜索 ≤ 0.90、登用 ≤ 0.95、同盟 / 离间 / 拉拢 ≤ 0.90（所有加成之后一次截断）；人望加成 ≤ +10；经验加成 ≤ +40 %。
- **一条内政公式**（`SG.Econ`）：开发、城收入、收成、征兵、训练与五个概率各只有一个函数，玩家与电脑共用；`Commands.lastResult.mods` 给演出显示「装备 +12 %」等来源；557 条测试向量供 Unity 逐位对照。
- **电脑预算**（`SG.AIBudget`）：每城保留 `150 + 8 × 武将数` 金与两个月军粮；每月固定顺序：征兵开发 → 盟约贡金 → 事件 → **授官礼金 → 买装 → 营建 → 节庆**（后四项共用每月总收入 22 % 的自由预算，最多积 12 个月）。实测自由支出后没有一座城跌破 0。
- **存档 v4**：迁移顺序 ITEMS → TITLES → ENGAGEMENT → TOURNAMENT → COMBAT / DOMESTIC；统一 `gen.injury = { until, war, away }`、`G.tourney.wins`。
- **月末流程**：收入收成 → 装备补货 → ENGAGEMENT 月末 → 叛逃（防护取较大者）→ 忠诚光环 → 官制月初 → 自由支出 → 武林大会 → 新闻。

## 16. 跨系统平衡表（全部系统同时开启的实测，`design-v4/integration/v4-all-proto.js`）

| 门槛 | 目标 | 实测（关 → 开） | 结论 |
|---|---|---|---|
| 经典 48 种子 × 25 年（两组）：第 10 年存活势力 | ≥ 关 − 0.8 | 4.94 → 4.77；4.29 → 4.48 | 通过 |
| 经典：首个势力占城 > 25 % 的中位年数 | ≥ 关 − 0.5 年 | 2.33 → 2.08；2.08 → 2.00 | 通过 |
| 经典：每种子叛逃 | ≤ 关 + 2 | 2.04 → 1.54；6.31 → 3.27 | 通过 |
| 世界 24 种子 × 15 年：第 15 年存活 | ≥ 关 − 2 | 44.29 → 43.42（叛逃 3.50 → 2.17） | 通过 |
| 战术（第 8 年全开军队，480 场）：攻方胜率变化 | ≤ 4 个百分点 | −0.2（另两批军队 −0.4、−1.5） | 通过 |
| 战术：每场伤亡 开 / 关 | ≤ 1.35 | ×0.999（×0.990、×1.000） | 通过 |
| 和平玩家：第 20 年存活 | ≥ v3 − 8 个百分点 | 89 / 100 % 对 v3 83 / 94 %；世界 96 % 对 92 % | 通过 |
| 和平玩家：王道达成（经典） | 10–40 % | 14 / 19 %（世界 8 %，不设门槛） | 通过 |
| 电脑支出占总收入 | 买装 8–15 % | 经典 11.2–11.4 %，世界 8.5 %；授官 2 %、营建 5.8–7.0 %、节庆 0.6–1.1 %；自由支出合计 18.4–19.7 %（预算 22 %）；自由支出后跌破 0 的城：0 | 通过 |
| 极限叠加（关羽 L10 + 金刚杵 + 最佳防具 + 赤兔 + 大将军 + 结义） | 不爆表 | 攻 ×1.58、防 ×1.47（上限未触发）；对 L1 无装备的交换比 ×4.30（老练本身 ×1.85） | 见问题 1 |
| 第 10 年地图上最强的现实叠加 | — | 攻 ×1.43、防 ×1.23（最高 ×1.52 / ×1.32） | — |
| 城收入上限 | +35 % | 理论叠加 +51 % 截到 +35 %；实际中位 +1 %，达到上限的城月 0 % | 通过 |
| 人望加成（行动点） | ≤ +10 | 第 25 年平均 9.6（不设上限时 32）；行动点 5.2（不设上限 5.9） | 通过 |
| 单挑：传说全套对无装备（同武力） | ≤ 72–75 % | 69 / 72 / 73 %（武力 60 / 80 / 95） | 通过（噪声 ±2.9） |
| 指令分级：开发大成 / 好；秋收好 | 5–16 % / 18–40 % | 10.1–10.9 % / 24.5–25.3 %；秋收好 25–30 % | 通过 |

## 17. 模块与文件归属（并行实现）

每个文件只有一个负责的工作流；其他工作流需要改它时，按 INTEGRATION 列出的钩子由负责人加。新增文件的载入顺序见 INTEGRATION §11。

| 工作流 | 新文件 | 负责修改的现有文件 | 依赖 |
|---|---|---|---|
| **W0 整合核心**（先行） | `js/econ.js`（`SG.Mods` / `SG.Econ` / `SG.AIBudget`）、`js/battle-math.js`、`js/migrate-v4.js`、`tests/econ.js`、`tests/v4-all.js`、`tests/save-v4.js`、`tests/load-order.js`、`tests/run-all.js` | `js/model.js`（存档 v4、`Commands` 改走 `SG.Econ`、`lastResult` 的 Econ 字段）、`js/strategy-ai.js`（`endMonth` 流程）、`js/data.js`（`SG.Balance.CityLevel`、上限表）、`index.html` | 无 |
| W1 装备 | `tools/gen-items.js`、`js/items-data.js`、`js/items.js`、`js/item-art.js`、`js/shop-screen.js`、`tests/items.js`、`tests/items.html`、`tests/shop.html` | `js/specials.js`（宝具） | W0 |
| W2 官爵 | `tools/gen-titles.js`、`js/titles-data.js`、`js/titles.js`、`js/title-art.js`、`js/title-ui.js`、`tests/titles.js` | — | W0 |
| W3 非战斗玩法 | `tools/gen-engage.js`、`js/engage-data.js`、`js/engage.js`、`js/engage-screen.js`、`tests/engage.js`、`tests/engage.html` | — | W0、W1（奖池） |
| W4 武林大会 | `js/tourney-data.js`、`js/tourney.js`、`js/tourney-flow.js`、`js/stadium-kit.js`、`js/stadium.js`、`js/music-tourney.js`、`tests/tourney.js`、`tests/stadium.html` | — | W0、W1、W5（单挑） |
| W5 战斗 | `tools/gen-combos.js`、`js/combo-data.js`、`js/combos.js`、`tests/combos.js` | `js/duel-game.js`、`js/clash.js`、`js/battle-model.js`、`js/battle-view.js`（只增）、`js/battle-controller.js`、`js/figure.js`（新动作） | W0（`battle-math.js`） |
| W6 内政演出 | `js/domestic-tiers.js`、`js/domestic-fx.js`、`js/cmd-icons.js`、`js/music-stings.js`、`tests/domestic-tiers.js`、`tests/domestic-fx.html` | — | W0（`lastResult`）、W7（舞台） |
| W7 城池视图 | `js/city-layout.js`、`js/city-kit.js`、`js/city-life.js`、`js/city-view.js`、`tests/city-layout.js`、`tests/city-kit.html`、`tests/city-view.html` | — | W0（城级表）、W9（配方） |
| W8 界面 | `js/settings.js`、`js/ui-icons.js`、`js/ui-art.js`、`js/shell.js`、`js/menu-screen.js`、`js/settings-screen.js`、`js/generals-screen.js`、`js/character-screen.js`、`js/tourney-screen.js`、`tests/ui-v4.html`、`tests/ui-layout.html`、`tests/settings.js`、`tests/saves.js` | `js/ui.js`、`js/strategy-screen.js`（壳层与指令坞）、`css/style.css`、`js/game.js`（标题与选君主） | W0 |
| W9 美术推广 | `js/lp-recipe.js`、`tools/lp-recipes.js`、`tests/lp-recipes.html` | `js/lowpoly.js`、`js/lp-props.js`、`js/lp-pilot.js`、`js/art.js`、`js/culture-art.js`、`js/map-view.js` | 美术试点（已通过） |
| W10 音频 | `js/music-suites.js`、`js/music-suites-gen.js`、`js/voice-data.js`、`js/voice.js`、`js/voice-cry.js`、`tools/build-suites.js`、`tools/voice-build/`、`tests/suites.js`、`tests/voice-lines.js`、`tests/voice-pick.js`、`tests/voice.html` | `js/audio.js`、`js/music.js` | — |

## 18. 实施阶段

| 阶段 | 内容 | 能否立即开始 |
|---|---|---|
| **A（立即，网页版）规则与数据** | W0 整合核心（第一周，其他工作流以它为接口）；装备目录与规则、商店、电脑买装；官爵；民心、声望、事件、国策、营建、商队、探索、文会、节庆、功业、王道；武林大会规则（AI 只结果）；会心、单挑 v4 机制与 AI、连段数据与工具、看破连段、`DUEL_ITEM_EFFECTS`、武器平衡（然后冻结 `WIN_SCALE`）；内政分级；音频引擎（君主乐章生成、指令短乐、人声播放与离线配音流程）；设置登记表、存档位 | 可以：只依赖已通过的风格，不需要新美术；全部用 Node 测试 |
| **B（立即）只用现有资产的界面** | 各功能先用 v3 / 试点资产与替代品上线：2D 牌面演出、现有单挑场地当擂台、现有武器模型、文字图标 | 可以 |
| **C（等美术批准，按资产类别分批）** | 城池视图的建筑与地标（先华夏 16 种 × 5 阶和其余家族的 5 种核心建筑）、营建模型、擂台立面与冠顶、装备部件与 10 件专属造型、特效、约 151 个图标（含 37 个指令图标）、界面纸漆质感、事件画 30 幅、单挑新动作、鞠躬与跪拜动作 | 每类先交一张评审图（`tests/lp-recipes.html`），批准后批量生产 |
| D 打磨 | 其余家族的建筑与工程、其余 25 个特效风格、其余主办门、全部冠顶、外语扩展台词 | P2 |
| E Unity 移植 | `Econ.cs` / `BattleMath.cs` / `MigrateV4.cs` 先行，再各系统；配方 JSON 在 `MeshBuilder.cs` 上解释；所有 harness 与网页版参考数据逐位一致 | 网页版稳定之后 |

关键路径：W0 → W1 / W2 / W3 → W4（要等 W5 的单挑与 W1 的奖池）；W5 P1 → 冻结 `WIN_SCALE` → 重测武林大会；W9 配方与评审图 → W7 / W4 擂台 / W1 部件。

## 19. 测试计划

- **矩阵**（INTEGRATION §13）：每个套件都有一次「全部开关打开」的运行；快速 CI（每次推送）≤ **6 分钟**（各套件 `--quick`：经典 12 种子 × 6 年等），夜间 ≤ **45 分钟**（经典 2 × 48 × 25 年、世界 24 × 15 年、和平玩家、战术、单挑每格 300 场）。
- **门槛**：§16 表中的每一行；外加：没有一座电脑城在自由支出后跌破 0 金；叛逃 ≤ 关 + 2；每个 1 月且大会到期时都会出现大会卡；所有规格提到的台词编号都存在；城池全 S5 加满工程不超预算；地图与战斗循环的动机比例 ≥ 25 %；经典剧本 ≥ 255 / 255 件；没有专属兵器出现在任何奖池；赐宝物轮流不涨忠诚；单挑白名单之外的字段加载即报错。
- **一月例外**：正月武林大会的电脑结算不受每月 60 ms 的限制，单独测（世界 ≤ 1.1 秒，分片）。
- **Unity**：`results/econ-refs-*.json`、配方哈希、各 harness 的参考 JSON 逐位一致（浮点 1e-9）。

## 20. 需要玩家决定的问题

1. **完美武将的叠加**：手工凑出的「关羽 L10 + 金刚杵 + 最佳防具 + 赤兔 + 大将军 + 结义」对无装备的 1 级武将交换比 ×4.30（v3 老练本身 ×1.85）；地图上现实出现的最强叠加只有 ×1.4 / ×1.25。要不要再加「攻 × 防 ≤ 2.0」的乘积上限？（建议：不加。）
2. **电脑的自由预算**定为总收入的 22 %（20 % 时世界剧本的买装只有 7.8 %，低于 8–15 %）；要不要再让它随难度变化（易 18 % / 中 22 % / 难 26 %）？
3. **单挑预算**：全合一预算 6（推荐：传说全套 69–73 %），还是放宽到 8（75–77 %，稀有度在单挑里更明显）？
4. **吉祥纹铜剑**（原名卍纹铜剑，帕拉塔钱币上的吉祥纹）：保留安全的名字，还是恢复原名并加说明？
5. **经典剧本的「异域奇珍」**：从第三届起每届 15 % 机会出现 19 件世界独有的宝物之一（每届至多一件史诗），够不够有趣又不破坏 190 年的中国背景？
6. **武林大会的胜率曲线**（`WIN_SCALE` 6）会在单挑 v4 第一阶段完成后重测并冻结，届时冠军分布会变一次——可以接受吗？
7. **外语台词**：26 条常用台词的 13 种外语译文是机器辅助的草稿（会过语音识别检查），v4.0 是否需要母语者审读后才上线？
8. **阵斩只限君主**（推荐）还是所有武将都可能战死（概率为君主的三分之一，可在设置里关掉）？
9. **民心的分量**：±5 % 收入与收成让电脑经济保持中性；要不要为了手感加到 ±10 %（需要重新平衡）？
10. **王道胜利门槛**（声望 1 500、平均民心 75）是用脚本玩家标定的；经典剧本是否要提高到 2 000？
11. **事件画**：先做 30 幅（每个事件一幅，按家族换色），还是要 12 个家族各一套（360 幅）？
12. **武林大会**：要不要东道主「停战」（主办城正月不受攻击）？要不要让更多善战的君主亲自下场？
13. **人声**：雇来的外族武将保留自己的语言（推荐，有「一律汉语」设置）；默认语音频率「全部」还是「精简」？
