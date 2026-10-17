# 三国志-霸王的世界 · 第三版设计契约（DESIGN-V3）

第三版在第二版（`DESIGN-V2.md`）基础上进行。先读 `DESIGN-V2.md`（共同规则 §1、文件约定、文化代号 §6、
译名 §7）、`CONTRACT.md`、`UI-CLASSES.md`，再读本文件。冲突时以本文件为准。平台：先网页版，稳定后移植 Unity。

## 0. 玩家的要求（原话）

1. The player models suck, it's all polygons — they should be similar to Dynasty Warriors (including art style).
   Character models and troop models should be way more smooth, no jagged-edged polygons; weapon and armor
   models should be a bit more detailed.
2. The map is all jagged-edged, lacking details — make the terrain smooth, more elements.
3. During battles make the maps bigger by 25%; the battle maps should reflect the actual surrounding
   map / landscape / terrain of each city, and take into account which direction the invading army comes from.
   Default turn on animation during battle.
4. Duel: implement parry — a successful parry allows a counter-attack, but parry has a longer cooldown than
   normal attacks and block. The combat experience of the duel is very important.
5. Deep-research more characters, generals and cities outside China; add more characters, leaders and cities.
6. Generals gain experience as they fight; they can learn more unique special attacks — the more 武 the more
   (and more powerful) special attacks they can learn; the higher the 智 the more 计策 they can learn, and more
   powerful ones, for battle.
7. Rename the game to 三国志-霸王的世界.
8. During the non-battle phase, whenever generals are listed, report, or are referred to, show the general's avatar.

## 1. 美术方向（§0.1、§0.2）

**由「低多边形平涂」改为「光滑、细致、真三国无双式」**——所有模型与纹理仍在代码中生成（不下载素材）。

- 着色：角色、士兵、兵器、地形一律平滑法线（不再 flatShading / 每面一色）。角色材质采用
  `MeshStandardMaterial` 或自定义的风格化 PBR：金属有高光与环境反射（程序生成的环境贴图），布料与皮革
  漫反射为主，加轮廓光（rim）。纹理在 Canvas 中程序生成：札甲鳞片、金属划痕与錾花、布纹、皮革、刺绣、
  毛发，必要时附法线贴图。
- 角色（新共享模块 `js/figure.js`，见 §8）：英雄比例（约 7.5 头身），肌肉与躯干用平滑网格（车削、管状、
  细分的胶囊体、带权重的蒙皮或分段骨架），层叠铠甲（胸甲、护肩、腰甲裙、护臂、胫甲、护心镜），
  披风 / 战袍 / 飘带（势力色，可轻微摆动），头盔带缨 / 羽 / 翎，发髻 / 长发 / 胡须；脸部为平滑头部 +
  由头像系统同一外貌参数生成的面部纹理（眼、眉、肤色、胡须、疤）。名将造型与其头像一致（关羽赤面
  长髯绿袍、张飞虬髯、吕布雉翎、赵云白袍银甲……），各文化（§6 of V2）各有衣甲。
- 兵器：细致的刀刃弧度、护手、刃纹、缨穗、彩带、杆上纹饰；名将专属兵器（青龙偃月刀、丈八蛇矛、
  方天画戟、双股剑……）。
- 士兵：同一风格的简化版（LOD），按文化与兵种（枪、刀盾、弓、骑）变化，实例化渲染。
- 性能预算（iPhone 级）：高精度角色（单挑、特写）≤ 15k 三角形；中精度（战场主将、攻击画面将领）
  ≤ 4k；士兵 ≤ 900，实例化；攻击画面整体 ≤ 350k 三角形、≤ 120 次绘制；纹理共用图集。
- 地形（`js/map-view.js`、`js/art.js`）：平滑法线 + 更高网格密度（按距相机分级 LOD），自定义地形着色器按
  坡度 / 高度 / 生物群系混合草地、泥土、岩石、沙地、雪、农田纹理（三平面投影处理峭壁），细节噪声。
  更多元素：近处草丛与野花、岩石、城外农田与村落、道路过河处的桥、关隘、雪峰、海岸沙滩与浪花、
  圆润的树冠（多种树），水面平滑带高光与波纹。城池模型（`culture-art.js`）同样改为平滑、细致。
- 战场（`battle-view.js`）的地块与城墙同样平滑、细致。

## 2. 战场（§0.3）

- 尺寸 +25%：`Balance.BattleW/H` 由 14×10 改为 **18×13**。
- 战场地形取自该城真实周边：以城池在世界地图上的位置为中心，在地图坐标中取一个窗口，采样世界地图的
  高度、坡度、生物群系、河流、湖海、海岸与道路，换算为战场地块（平原 / 森林 / 丘陵 / 山岳 / 河川 /
  不可通行的水域等），本城位于城池所在处。窗口按**进攻方来向**旋转：从出发城指向目标城的方向对准
  攻方的布阵边，攻方从那一边进入。结果对 (城池, 来向) 确定且可复现；新增的水域地块不可通行
  （规则上等同山岳，但外观是水）。经典剧本与世界剧本都适用。
- 电脑与规则须适应更大的战场（行动力、移动范围、30 日期限），`tests/sim.js` 平衡统计保持合理。
- 战斗动画（攻击画面）默认开启：设置存储键升级为新键，旧的「关」设定作废一次。

## 3. 单挑（§0.4）——手感最重要

- 格挡（按住）与**格开（Parry）**分开：格开是一次轻按，有效窗口约 0.18 秒（武力略有影响：0.15–0.22 秒）。
- 格开成功：对手被弹开并硬直（约 0.6 秒），己方获得反击窗口（约 0.7 秒）：此间第一击伤害 ×1.5 且
  不可被格挡；配合慢镜头、火花、震屏、专属音效。
- 格开失败（空挥）：约 0.4 秒破绽。格开冷却约 2.5 秒，明显长于普通攻击与格挡；HUD / 触控按钮显示冷却环。
- 键盘：J 轻击、K 重击（按住蓄力）、L 格挡（按住）、**U 格开**、I 绝技；触控增加「格开」按钮。
  操作说明卡同步更新。
- 电脑会格开（反应与成功率随武力）、会惩罚空挥的格开、会在格开后反击；也会被格开反制。
- 手感：命中顿帧、受击方向、重击有清晰的起手预兆、连段节奏、闪避或后撤步（如有余力）。
- 测试：`tests/duel-bots.js` 增加格开机器人与连打机器人对局；不同武力差的胜率保持合理。

## 4. 成长：经验、必杀与计策（§0.6）

- 每位武将有 `exp` 与 `level`（1–10）。战斗中造成伤害、击溃部队、单挑胜利、施展必杀 / 计策、胜利一方
  全体都获得经验；电脑势力的武将同样成长。
- **必杀**：保留现有的专属必杀（第一招）。随等级可习得更多必杀，来自新的「可习得必杀」池（按等阶 1–4，
  每招名称唯一、有出处或合理的武艺名）。可习得数量与可学的最高等阶由 **武力** 决定（例如 武力 <60 不能
  另学；60+ 一招；70+ 两招；80+ 三招；90+ 四招；等阶上限 ≈ (武力 − 40) / 15），并随等级逐步解锁。
- **计策**：扩展计策目录到约 16 种、分 4 阶（火计、激励、混乱、伪报 / 落石、伏兵、水计、挑拨 /
  连环、落雷、妖术、鼓舞全军 / 天变、八阵、奇门遁甲、…，规则与特效各自不同），可习得数量与最高等阶
  由 **智力** 决定并随等级解锁。原有四计保持数值不变。
- 升级时：玩家武将从 3 个候选中选择要学的招式 / 计策（UI 显示头像、说明）；电脑自动选择。
- 战斗菜单：「必杀」列出该部队全部已学必杀（每招每战一次），「策略」列出已学计策。
- 存档版本升级（v3），读取 v2 存档时补齐成长数据（等级 1、经验 0、只有专属必杀与按智力可用的原四计）。
- 平衡：`tests/sim.js` 增加成长后的平衡统计；成长不能让早期战斗失衡。

## 5. 头像无处不在（§0.8，非战斗阶段）

- 所有列出武将的地方（指令选人、出征选将、任命、登用、搜索结果、俘虏处置、移动 / 输送、外交使者、
  势力一览、城池面板……）每行带头像。
- 武将汇报或被提及时显示头像：指令结果（「关羽开发了土地」等）用该武将作为说话人显示头像；月末新闻与
  记录中提及的武将带小头像；急报中来犯主将带头像。
- 通用实现：`SG.UI.item` 与 `SG.UI.say` 支持头像参数；`SG.Portrait.html` 用于行内小头像。

## 6. 世界扩充（§0.5）

- 深入研究五大地区之外与之内仍薄弱的部分：罗马行省（西班牙、高卢、多瑙河诸省、埃及、阿非利加、
  小亚细亚、叙利亚）、安息诸城与贵族、贵霜（城池与人物明显不足）、印度（百乘王朝、西总督、南印度诸国）、
  阿拉伯与非洲（阿克苏姆、麦罗埃 / 库施）、朝鲜与日本、草原（鲜卑诸部、乌孙、康居）、东南亚（扶南、林邑、骠国）。
- 规则同第二版：只收史有其人（或明确的传说人物），注明出处，按需放宽年代并在说明中注明；事实核查后再合并。

## 7. 改名（§0.7）

- 游戏名改为 **三国志-霸王的世界**：标题画面、加载画面、`<title>`、应用名、README、注释头。
  第一版剧本「董卓专横」等剧本名不变。

## 8. 新模块与文件归属

| 工作 | 文件（独占） |
|---|---|
| 角色 / 士兵 / 兵器模型库 | `js/figure.js`（新）、`tests/figures.html`（新） |
| 地形与城池 | `js/map-view.js`、`js/art.js`（地形 / 水 / 材质部分）、`js/culture-art.js`、`js/world-geo.js` |
| 单挑格斗 | `js/duel-game.js`、`tests/duel.html`、`tests/duel-bots.js` |
| 成长系统（规则） | `js/battle-model.js`、`js/specials.js`、`js/specials-data.js`、`js/model.js`、`tests/sim.js`、`js/tactics-data.js`（新） |
| 改名 + 头像无处不在 | `js/strategy-screen.js`、`js/ui.js`、`js/game.js`、`index.html`、`css/style.css` |
| 研究 | `research/v3/*`（新） |
| 第二阶段：战场（地形取自城池周边、18×13、默认动画） | `js/battle-model.js`（地图生成部分）、`js/battle-view.js`、`js/clash.js`（模式默认） |
| 第二阶段：模型接入 | `js/clash.js`、`js/duel-game.js`、`js/battle-view.js` |
| 第二阶段：成长界面 | `js/battle-controller.js`、`js/strategy-screen.js`、`js/ui.js` |
| 第二阶段：世界数据合并 | `js/world-data.js`、`js/specials-data.js`、`js/portrait-data.js` |

并行时只改自己名下的文件；需要别处配合时写进报告的 INTEGRATION 一节。
