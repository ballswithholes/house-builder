# 三国志II 霸王的大陆 · 网页版 · 第二版设计契约（DESIGN-V2）

本文件是第二版所有新功能的共同契约。每个模块的作者都要先读本文件，再读
`CONTRACT.md`（模块接口、载入顺序、C# 移植约定）与 `UI-CLASSES.md`（界面类名与
`SG.UI` 用法）。本文件与 `CONTRACT.md` 冲突时，以本文件为准。

## 0. 玩家的要求（原话）

1. Music sucks — use original music from the game but make it modern.
   （原曲不可用：无法取得、版权属 Namco。决定：**按原作风格全新作曲**，用现代合成乐器演奏。）
2. Each general needs an avatar — use the original game art style but modernize it.
3. 攻击 should trigger a battle screen like the original game.
4. Each general should have a unique special attack that they can use during battle.
5. 单挑 should trigger its own screen — make it into a fighting game, show controls.
   Attack and damage are based off of 武力.
6. "I don't see how I can move generals between my own cities."
7. Expand the game to Japan, Taiwan, Vietnam, Thailand, Korea, Mongolia, West Asia,
   Middle East, Europe — researched empires, kings, generals and main cities of that period (≈190 AD).

平台：先做网页版（本目录），稳定后再移植回 Unity（`../sanguozhi2-unity`）。本轮**不改** Unity 工程。

## 1. 共同规则

- 经典脚本 + IIFE + 全局命名空间 `window.SG`，不用 ES module、不加构建步骤。
  只依赖已内置的 `vendor/three.min.js`（r158）。不得从网络载入任何资源。
- 所有美术、音乐、音效都在代码里生成（程序化网格、SVG / Canvas、WebAudio 合成）。
- 界面文字一律简体中文。外国人名、地名用中文史学界通行译名（见 §7）。
- 同时支持：桌面（鼠标 + 键盘）、iPad、iPhone 横屏（触摸）。触控目标 ≥ 44px。
  手机横屏参考尺寸 844×390，桌面 1280×720 与 1920×1080，iPad 1024×768。
- 性能预算：iPhone 级设备 60fps 目标、最低 30fps；单帧 JS 不超过 8ms；
  任何同步卡顿 ≤ 50ms（大计算拆成异步分片）。显存与内存保持克制（纹理复用、及时 dispose）。
- 不得出现页面错误（`pageerror`）与 `console.error`。所有 `localStorage` 访问包在 try/catch 里。
- `?seed=N` 让随机数可复现（`SG.Random.seed`）；新模块里的随机也必须走 `SG.Random`
  （纯装饰性动画可以用自己的 `SG.SeededRandom`，但不能用 `Math.random` 影响规则）。
- 规则改动后 `node tests/sim.js` 必须仍然 ALL OK（如数据变化导致期望值改变，同步更新测试并说明原因）。
- 代码注释用中文，风格与周围代码一致。
- 每个新模块自带一个独立测试页 `tests/<模块>.html`（`file://` 直接打开即可运行），
  并在模块文件头注释里写清公开接口。
- 不要提交 git；由总控统一提交。

## 2. 已就绪的共享基础设施（总控已加入）

`SG.Gfx`（`js/art.js`）新增**全屏场景栈**：

```js
SG.Gfx.pushScreen(screen)   // screen = { scene, camera, update?(dt), render?(renderer), resize?(w, h) }
SG.Gfx.popScreen(screen)
SG.Gfx.topScreen()          // → 栈顶 screen 或 null
```

栈顶有场景时，`Gfx.render()` 只渲染栈顶场景（主地图 / 战场暂停绘制），游戏主循环每帧调用
`screen.update(dt)`；`<html>` 加上 `sg-screen-active` 类，CSS 会隐藏世界标签层（`.sg-labels`）。
相机宽高比在推入与窗口尺寸变化时自动更新。全屏场景自己的 HUD 用 DOM：放进
`SG.UI.layers.screens`（或自建 `position:fixed` 容器，z-index 介于 screens 与 modals 之间），
退出时务必移除。同一渲染器、同一 canvas —— **不要**再创建 `WebGLRenderer`。

## 3. 文件归属（并行开发时只改自己名下的文件）

| 功能 | 新建文件（独占） | 允许修改的现有文件 |
|---|---|---|
| A 音乐 | `js/music.js`（可拆多个 `js/music-*.js`）、`tests/music.html` | `js/audio.js`（只改音乐部分；音效 API 不变） |
| B 头像 | `js/portrait.js`、`js/portrait-data.js`、`tests/portraits.html` | 无 |
| C 攻击画面 | `js/clash.js`、`tests/clash.html` | 无 |
| D 必杀技 | `js/specials.js`、`js/specials-data.js`、`tests/specials.html` | `js/battle-model.js`、`js/battle-view.js`（只增不删）、`tests/sim.js` |
| E 单挑格斗 | `js/duel-game.js`、`tests/duel.html` | 无 |
| F 调动武将 | — | `js/strategy-screen.js`（指令按钮与移动 / 输送部分）、`js/model.js`、`js/game.js`（仅 HelpText） |
| G 世界扩展 | `js/world-data.js`、`js/world-map.js`、`js/culture-art.js`、`research/*` | 第二阶段另行分配 |
| 集成 | — | `index.html`、`js/battle-controller.js`、`js/ui.js`、`css/style.css` 及其余 |

需要改别人名下的文件时：在自己的模块里提供一个现成的函数，并在返回报告的
**INTEGRATION** 一节写清楚“在哪个文件哪个函数里插入哪几行”，由集成阶段完成。

各模块的 CSS：写成模块内注入的 `<style>`（参考 `battle-view.js` 的做法，带 id 防重复注入），
或新建 `css/<模块>.css` 并在 INTEGRATION 里注明要加的 `<link>`。

## 4. 各功能规格

### A 音乐（js/music.js + js/audio.js）

现有 `audio.js` 把一段简单的拨弦旋律离线渲染成 PCM，玩家评价“很难听”。全部重做：

- 实时 WebAudio 合成 + 前瞻调度器（例如每 25ms 排程未来 0.15s 的音符），
  或按乐器预渲染采样（如 Karplus-Strong 拨弦按音高缓存 AudioBuffer）再调度——自行选择，
  但不得在主线程产生 > 50ms 的同步卡顿，切换曲目要淡入淡出（≈0.8s 交叉）。
- 乐器（全部合成）：古筝 / 琵琶（拨弦、轮指）、二胡（锯齿 + 揉弦 + 共鸣滤波）、
  笛子（正弦 + 气声）、笙 / 弦乐垫音、太鼓 / 大鼓、小锣与大锣、编钟、拍板、低音。
  另为异域曲风准备：日本筝 / 尺八 / 太鼓，伽倻琴，马头琴 + 呼麦式持续低音，
  甘美兰式金属打击乐，西塔尔式拨弦 + 持续音，乌德 / 桑图尔 + 手鼓，
  罗马号角（cornu）/ 里拉琴 / 鼓，凯尔特风笛式持续音 + 框鼓。
- 混音：总线压缩 / 限幅，卷积混响（程序生成脉冲响应），立体声声像，力度与时值人性化。
- 作曲：按原作《三国志II 霸王的大陆》的气质（英雄、苍茫、五声调式）**全新创作**，
  不抄袭任何现有曲目。每首有完整曲式（引子、A、B、过渡，无缝循环），主旋律、对位、低音、打击乐分层。
- 曲目（`SG.Sfx.music(kind)` 的 kind；旧 kind 保持可用）：
  `title`（标题，庄严宏大）、`map`（战略地图，沉思从容）、`battle`（战场，进攻）、
  `battle-defend`（守城，紧张）、`clash`（攻击画面短促的冲锋乐句，可选）、`duel`（单挑，激烈快速）、
  `victory`（胜利短曲，不循环）、`defeat`（战败短曲，不循环）、`ending`（一统天下）、`council`（外交 / 事件，可选）。
- 地域曲风：`SG.Sfx.setCulture(culture)` 设定当前地域（见 §6 文化代号），
  `map` 与 `battle` 随之换成该地域的变奏（同一套引擎，不同调式与乐器）。至少覆盖：
  `han`、`wa`、`korea`、`steppe`、`seasia`、`kushan`、`persia`、`arab`、`roman`、`celt`/`german`（可共用一首）。
  未覆盖的文化回退到 `han`。
- 保持现有接口：`SG.Sfx.init() / unlock() / play(name, vol) / click() / music(kind) / setMusic(on) / musicOn`。
  `music(kind)` 在音频尚未解锁时只记住 kind，解锁后自动开始（现有行为）。
- 测试页 `tests/music.html`：列出全部曲目与文化变奏，可逐一播放；
  并用 `OfflineAudioContext` 离线渲染每首前 20 秒，检查无 NaN、峰值 ≤ 0.98（限幅后）、非静音（RMS 合理）。

### B 头像（js/portrait.js + js/portrait-data.js）

每位武将一张头像：**原作（FC《霸王的大陆》）的头像风格——深色背景、四分之三侧面胸像、
粗轮廓、有限色板——现代化**：高分辨率矢量绘制、赛璐璐分层阴影、轮廓光、细致的发须与甲胄纹样，
装在统一风格的边框里（边框颜色 = 势力色）。

```js
SG.Portrait.url(gen, opts)        // → 图片 URL（dataURL 或 blob URL），按 (name, size, factionColor) 缓存
SG.Portrait.el(gen, opts)         // → HTMLElement（<div class="sg-portrait"><img></div>），opts.size 为 CSS 像素
SG.Portrait.preload(gens)         // → Promise，空闲时分片生成，避免卡顿
SG.Portrait.spec(gen)             // → 解析后的外貌参数（调试用）
// gen 至少含 { name, war, intel, pol }；可选 { culture, born, faction, color }。
// opts：{ size = 96, color = 势力色或灰, frame = true, flip = false, mood = 'neutral'|'angry'|'hurt'|'win' }
```

- 生成器以“外貌参数”驱动：脸型、肤色、年龄（由 born 与当前年份推算，缺省 30 岁）、眉眼鼻口、
  发型 / 发髻、胡须（无 / 短 / 长髯 / 虬髯 / 络腮）、冠帽头盔（文官冠、武将盔、纶巾、羽冠、
  头巾、桂冠、王冠、提亚拉、胡帽、毡帽……）、甲胄 / 衣袍、配饰（羽扇、眼罩、耳环、勾玉……）、
  表情。无人工设定时由 `name` 的哈希 + 能力值（武力高 → 武将甲胄、智力高 → 文士冠）+ 文化
  确定性地生成，**同名永远同脸**，不同武将之间要明显可区分。
- `js/portrait-data.js`：为有名武将手工设定外貌（至少本作现有的全部 ~162 人里所有知名人物：
  关羽赤面长髯绿巾、张飞豹头环眼虬髯、刘备大耳、诸葛亮纶巾羽扇、曹操、吕布雉羽冠、
  赵云白袍银盔、孙权碧眼紫髯、黄忠白须老将、夏侯惇眼罩、典韦、许褚、马超狮盔、周瑜、董卓肥胖……）。
  数据格式写在文件头注释里，第二阶段会为世界各国武将追加条目：
  `SG.PortraitData.add({ '卑弥呼': { culture: 'wa', ... } })`。
- 文化外观（§6）：每种文化有自己的冠服、发型、肤色范围、配色，第一阶段就要实现全部文化的基础样式
  （第二阶段只需追加个别人物）。
- 渲染：SVG 字符串 → Image → Canvas（或直接 SVG dataURL）。生成 300 张 128px 头像总耗时
  在桌面 < 1.5s，且分片执行。
- 测试页 `tests/portraits.html`：全部现有武将的头像总览（含势力色边框与名字），以及每种文化的样例。

### C 攻击画面（js/clash.js）

原作中部队“攻击”时会切换到一个横向的交战画面：两军士兵对冲、兵力数字递减。现代化重制：

```js
await SG.Clash.play({
  attacker: { gen, side, color, troopsBefore, troopsAfter, formation: '鱼鳞', culture },
  defender: { gen, side, color, troopsBefore, troopsAfter, formation: '方圆', culture },
  terrain,              // SG.Terrain 值（防守方所在格），决定场景：平原 / 森林 / 山丘 / 河川 / 城门 / 本城（城墙）
  special: null,        // 可选：{ name, color, kind } —— 必杀技发动时的特写与特效
  playerSide: 0 | 1 | null,
})
SG.Clash.enabled        // 布尔，默认 true（设置里可关；关时 play 立即返回）
SG.Clash.speed          // 1 = 正常；委任 / 电脑回合可用 2
await SG.Clash.cutIn({ gen, name, color, side })   // 必杀技 / 单挑用的全屏特写：头像滑入 + 招式名大字
```

- 用 `SG.Gfx.pushScreen` 的独立三维场景：一条与地形相符的横向战场（草地、树林、丘陵、浅滩、城墙与城门），
  天空与光照与主场景同调；双方士兵为低多边形小人（可复用 `SG.Art` 的 `soldier` / `commander` 构造器），
  队伍色区分，人数与兵力成比例（例如每 300 兵 1 人，每方 6–28 人），按阵型排列。
- 流程（总长约 3–4 秒，`speed` 可加速）：入场 → 弓箭齐射 → 冲锋 → 混战（尘土、火花、碰撞）→
  按伤亡比例倒下 / 消散 → 收队。顶部 HUD：双方头像（`SG.Portrait`）、姓名、阵型、兵力数字随伤亡滚动、
  “−1234” 伤亡飘字。轻点 / 点击 / 空格 / 回车可跳过（立即显示结果后退出）。
- 头像模块尚未载入时（`!SG.Portrait`）用姓氏首字的圆形徽章代替。
- 测试页 `tests/clash.html`：每种地形 × 不同兵力比各播放一次，并有手机尺寸预览。

### D 必杀技（js/specials.js + js/specials-data.js + battle-model.js + battle-view.js）

**每位武将一招独有的必杀技**，战斗中可用。

```js
SG.Specials.of(gen)          // → { id, name, kind, power, range, radius, ..., desc, color, fx } —— 该武将的必杀技
SG.Specials.all()            // → 全部已定义条目（校验唯一性用）
SG.SpecialsData.add({ '关羽': {...}, ... })   // 追加条目（第二阶段为世界武将追加）
```

- 机制目录（`kind`）至少包括：单体重击 smite、横扫相邻 cleave、直线突击 charge、远程齐射 volley、
  范围火攻 blaze、威吓 roar（降敌士气 / 可致混乱）、鼓舞 rally（回复己方士气 / 少量兵力）、
  奇谋 scheme（多目标混乱）、吸收 drain（伤敌并收编部分兵力）、坚守 fortify（数日防御加成）、
  疾行 haste（令相邻友军可再行动）、天候 storm（大范围伤害，如借东风）、医术 heal（如华佗）、
  暗杀 assassinate（低概率直接击溃敌将）。可以再加。每招的数值随武将的 武力 / 智力 / 政治 缩放。
- 每位武将的招式**名称唯一**，且 (kind, 参数) 组合不得与他人完全相同。知名武将用符合其事迹的招式
  （关羽「青龙偃月斩」、张飞「长坂怒吼」、赵云「七进七出」、吕布「天下无双」、诸葛亮「借东风」、
  黄忠「百步穿杨」、典韦「双戟护主」、华佗「麻沸散」……）。现有 ~162 名武将全部手写条目；
  另提供**确定性的兜底生成器**，给任何没有条目的武将生成独有招式（第二阶段的世界武将会手写补全）。
- 规则（写进 `battle-model.js`，保持纯逻辑、可被 `tests/sim.js` 测试）：
  `specialUsable(u) → {ok, why}`、`specialTargets(u)`、`useSpecial(u, target) → { success, dmg, hits:[{unit, dmg}], ... }`。
  每场战斗每位武将限用 1 次（`u.specialUsed`），消耗该部队本日行动（与攻击相同）。
  电脑方（`aiPlan`）在划算时使用（新 plan.kind = 'special'）。数值要平衡：一招强力必杀约等于 1.8–2.5 次普通攻击。
- 同时把 `duel(a, b)` 拆成可供格斗单挑使用的两步（`duel()` 保留，内部调用这两步，**随机数调用顺序不变**，
  使 `tests/sim.js` 结果不变）：
  `duelAccepts(a, b) → bool`（含拒绝时的士气变化），`duelFinish(a, b, winnerIsA, rounds)`（结算胜负后果）。
- `battle-view.js` 新增必杀技特效函数（只增不删），如 `V.specialFx(u, targets, fx) → Promise`：
  按 `fx`（颜色、形状：斩击弧光、冲击波、箭雨、火海、雷电、金光、毒雾……）播放，长约 1–1.5 秒。
- `specials.js` 提供控制层辅助：`await SG.Specials.perform(bc, u, target)` —— 播放特写
  （`SG.Clash.cutIn`，若存在）→ 调 `Mdl.useSpecial` → 播放特效与伤害飘字 → 处理溃散；
  集成阶段只需在战斗菜单加一项「必杀」并调用它。
- 测试页 `tests/specials.html`：列出全部武将与招式（名称唯一性、参数唯一性自检结果显示在页首），
  并可在一个小战场上逐个演示特效。

### E 单挑格斗（js/duel-game.js）

单挑变成一场**横版格斗游戏**，有独立画面并显示操作方法；攻击力与伤害取决于 **武力**。

```js
const res = await SG.DuelGame.play({
  a: { gen, side: 0, color, culture },     // a = 挑战者
  b: { gen, side: 1, color, culture },
  playerSide: 0 | 1 | null,                // 玩家操作哪一方；null = 电脑对电脑（观战，可跳过）
  terrain,                                 // 背景
  special: { a: SG.Specials?.of(a.gen), b: ... },   // 绝技名称与配色（可选）
})
// → { winner: 0 | 1（0 = a 胜）, kind: 'ko' | 'time' | 'skip', hpA, hpB, log: [...] }
SG.DuelGame.auto       // 测试用：true 时玩家一方也由电脑操作（也可用 URL 参数 ?duelauto=1）
```

- 用 `SG.Gfx.pushScreen` 的 2.5D 横版场景：两名低多边形武将（按文化与武将设定的盔甲、颜色、兵器——
  关羽青龙偃月刀、张飞丈八蛇矛、吕布方天画戟、刘备双股剑……，无设定时按能力值与文化选择），
  程序化关键帧动画（待机、走、跑、跳、轻击三连、重击蓄力、格挡、受击、倒地、胜利姿势）。
- 操作：键盘 ←/→ 或 A/D 移动、↑/W/空格 跳、J 轻击、K 重击（按住蓄力）、L 格挡、I 绝技（怒气满）；
  触摸：左下方向键（◀ ▶ ▲），右下按钮（轻击 / 重击 / 格挡 / 绝技），支持多点触控。
  开场显示**操作说明卡**（键盘与触摸两套，按设备显示对应的一套），战斗中屏幕边缘保留简短图例。
- 数值：体力 100；每击伤害 = 基础 × (0.55 + 武力/100) × (1 + (己武力 − 敌武力)/150)，限制在合理范围；
  高武力出招略快、硬直略短；格挡减伤 80%；怒气随命中 / 受击积累，满时可放绝技（名称取自必杀技）。
  电脑难度由其武力决定（反应时间、格挡率、连段与绝技使用）。限时 60 秒，KO 或时间到体力百分比高者胜。
- 结束后由集成阶段调用 `Mdl.duelFinish(a, b, res.winner === 0, res.log)` 结算；
  电脑对电脑或“委任”时不进格斗画面，照旧用 `Mdl.duel()`。
- 特效：命中火花、屏幕震动、慢镜头 KO、连击数、“胜”字大书。音乐用 `SG.Sfx.music('duel')`（若存在）。
- 测试页 `tests/duel.html`：选择任意两名武将对战（含 `?duelauto=1` 自动对打），手机尺寸可用。

### F 调动武将（strategy-screen.js / model.js）

玩家反馈找不到在己方城池之间调动武将的方法。现状：「移动」只能去**相邻**的己方城，
开局只有一座城时按钮是灰的，也没有任何说明。改为：

- 「移动」「输送」的目的地 = 经由己方领土（相连的己方城池链）可达的所有己方城（BFS），列表里注明路程（经过几城）。
- 任何灰色指令按钮被点按时，用提示说明原因（如「只有一座城池，攻下第二座城后即可调动武将」）。
- 第一次占领新城后弹一次提示：可用「移动」把武将调往其他城池。
- 操作说明（`game.js` 的 HelpText）补上「移动 / 输送」的说明。
- 规则变化写进 `model.js` 的命令函数，`tests/sim.js` 保持通过。

### G 世界扩展（第二阶段，研究完成后开工）

把地图从中国扩展为欧亚大陆：日本（倭）、台湾（夷洲）、越南（交州、林邑）、泰国（扶南属国）、
朝鲜半岛（高句丽、百济、新罗、伽倻）、蒙古（鲜卑、乌桓、匈奴）、西亚（贵霜、安息）、中东、欧洲（罗马与诸蛮族）。

- 投影：`SG.project(lon, lat) → {x, y}` 分段线性，中国本部保持现有比例（每经度 5、每纬度 5.6，
  经度 100° 纬度 23° 为原点），远方地区压缩；`SG.mapPos(city)` 与地图生成共用这一函数。
- 地形：多个陆地多边形（大陆、日本诸岛、台湾、海南、不列颠、斯里兰卡、西西里……）与内海（地中海、
  黑海、里海、波斯湾、红海），主要河流、山脉、沙漠、雪山；分块地形与视锥剔除，三角形总数与生成耗时受控。
- 数据：`js/world-data.js` 追加城池、连线（含海路）、势力、武将（含 `culture`、`born`、`died`、`orig` 原名、`note`）。
  存档键升级（旧存档不兼容时提示“新版本，旧存档无法读取”）。
- 选择君主时按地域分组；各文化的城池模型、战场城墙与士兵外观、头像、音乐风格。

## 5. 集成阶段的接线清单（集成者使用）

- `index.html` 按依赖顺序加入新脚本：`portrait-data.js → portrait.js → music.js → clash.js →
  specials-data.js → specials.js → duel-game.js`（在 `battle-controller.js` 之前），以及世界扩展文件。
- `battle-controller.js`：`doAttack` 播放 `SG.Clash.play`；战斗菜单加「必杀」→ `SG.Specials.perform`；
  电脑 plan.kind 'special'；`doDuel` 在玩家参与且非委任时走 `SG.DuelGame.play` + `Mdl.duelFinish`；
  部队信息卡显示头像。
- `ui.js`：`say(text, speaker)` 有说话人时显示其头像；`choose` 的条目支持头像图标。
- `strategy-screen.js`：城池面板的武将列表、俘虏 / 登用对话显示头像；音乐 kind 与文化切换。
- `game.js`：选择君主列表带头像、按地域分组。
- 设置：顶栏「设置」或「音乐」旁加「动画」开关（攻击画面 开 / 快 / 关）。

## 6. 文化代号（culture）

| 代号 | 范围 | 视觉要点 |
|---|---|---|
| `han` | 汉人（含交州士燮、辽东公孙度） | 冠帻、札甲、袍服 |
| `nanman` | 南中诸族（孟获等） | 椎髻、兽皮、骨饰、藤甲 |
| `wa` | 倭（邪马台、狗奴等） | 美豆良发型、贯头衣、勾玉、铜镜、木盾 |
| `yi` | 夷洲（台湾）诸部 | 文身、羽饰、贝珠 |
| `korea` | 高句丽、夫余、百济、新罗、伽倻 | 折风帽、鸟羽冠、金冠（新罗）、札甲 |
| `steppe` | 鲜卑、乌桓、匈奴、丁零 | 髡发 / 辫发、毡帽、皮袍、弓骑 |
| `seasia` | 林邑、扶南、顿逊、金邻、狼牙修 | 缠腰布、金饰、高冠、深肤色 |
| `tarim` | 西域诸国（于阗、龟兹、疏勒、鄯善…） | 胡帽、长袍、塞种 / 吐火罗相貌 |
| `kushan` | 贵霜、康居、大宛 | 高尖帽、长外套、靴、贵霜王冠 |
| `persia` | 安息、亚美尼亚、波斯（帕佩克） | 提亚拉冠、卷须长发、鱼鳞甲 |
| `arab` | 哈特拉、奥斯若恩、卡拉塞尼、萨巴、帕尔米拉 | 头巾、长袍、卷须 |
| `roman` | 罗马帝国（含希腊语东部） | 桂冠、披风、环片甲、短发剃须或短须 |
| `celt` | 喀里多尼亚等 | 彩绘、辫须、方格斗篷 |
| `german` | 马科曼尼、夸迪、哥特等 | 苏维汇发髻、长发、毛皮 |
| `sarmatian` | 雅济吉斯、阿兰、博斯普鲁斯 | 尖盔、鳞甲、长枪骑兵 |

现有剧本的武将没有 culture 字段时一律视为 `han`（孟获一族为 `nanman`）。

## 7. 译名规范

- 用中文史学界与中文维基通行译名；显示名尽量 ≤ 4 字（必要时用通称，如「塞维鲁」「尼格尔」「康茂德」）。
- 数据里另存原文名（`orig`）与全名（`full`），在武将详情 / 头像测试页显示。
- 日朝越等汉字文化圈人物用史书汉字原名（卑弥呼、难升米、故国川王、乙巴素、肖古王、士燮、区连、范师蔓……）。
