# Tests~ —— 离线编译检查与无头测试

Unity 会忽略名字以 `~` 结尾的目录，所以这里的文件不会被导入工程。这里的检查都不需要 Unity，
只需要 Mono（`mcs`、`mono`），生成数据与网页版参考值时还需要 Node（不需要装任何依赖）；画面对照另需全局的
Playwright Chromium（`/opt/node22/lib/node_modules/playwright`，不要运行 `playwright install`）。

每个脚本都认 `OUT_DIR`（缺省 `Tests~/out`，已 gitignore）：几个人同时跑测试时各用自己的输出目录，例如
`OUT_DIR=/tmp/me/out Tests~/build.sh`。

## 编译检查：`Tests~/build.sh`

```bash
Tests~/build.sh          # 退出码 0 = 全部通过
```

- 用 `mcs`（C# 7.2）对照 Unity 引用程序集编译 `Assets/Sanguo/Scripts/**/*.cs`（输出 `$OUT_DIR/game.dll`），
  再编译 `Assets/Sanguo/Editor/*.cs`（引用 UnityEditor 和 game.dll）。参数与最初的 `/tmp/unityref/build.sh` 相同
  （另加 Mono 的 `netstandard.dll` facade）。
- 引用程序集放在 `Tests~/refs/`（已 gitignore）。缺失时脚本先从 `$UNITYREF_CACHE`（缺省 `/tmp/unityref`）复制，
  否则从 NuGet 下载并解压：
  | 包 | 版本 | 用途 |
  |---|---|---|
  | `UnityEngine.Modules` | 2021.3.33 | 运行时模块（CoreModule、AudioModule、UIModule…） |
  | `Unity3D.SDK` | 2021.1.14.1 | `UnityEditor.dll` |
  | `Unity3D.UnityEngine.UI` | 2020.3.21 | UGUI |

  下载先走 `api.nuget.org/v3-flatcontainer`，失败再走 `www.nuget.org/api/v2/package`（后者重定向到 globalcdn，
  在部分代理环境下被拒）。`UNITYREF_CACHE= Tests~/build.sh` 可强制下载；`REFS_DIR`、`OUT_DIR` 可改路径。
- 只报告错误与警告；错误时退出码非 0。着色器不在编译范围内（这里编译不了 ShaderLab），只能逐行审查。

## 无头规则测试：`Tests~/sim/run.sh`

```bash
Tests~/sim/run.sh                 # 运行全部 Harness*.cs 与 JS 对照
Tests~/sim/run.sh Harness2 Data   # 只运行 Harness2.cs、HarnessData.cs（可省去 "Harness" 前缀）
```

每个 `Harness*.cs` 是一个独立的程序（各有 `Main`），与下列文件一起编译（`mcs -r:System.Core.dll -r:System.Numerics.dll`）、
运行；退出码 = 失败的程序数（编译失败也算）。各程序的输出另存为 `$OUT_DIR/sim/<程序名>.txt`。

- 规则文件：`Data/*.cs`，`Data/Generated/` 里规则用的 `SpecialsData.g.cs`、`WorldData.g.cs`、`WorldGeo.g.cs`、`TacticsData.g.cs`
  （头像、乐谱、人物模型等画面数据不编译进来），`Model/*.cs`、`Strategy/StrategyAI.cs`、`Strategy/Conquest.cs`、
  `Battle/BattleModel.cs`、`Battle/Specials.cs`、`Battle/Tactics.cs`（存在时），以及 `BattleTerrainStub.cs`
  （战场地形的替身：同网页版 `tests/sim.js` 在 Node 下没有战略地图，一律退回旧的随机地形）。
  规则文件只能用 `UnityStub.cs` 里有的 UnityEngine API；缺什么就按 Unity 的签名补进替身。
- `UnityStub.cs`：UnityEngine 的最小替身。`Random` 与网页版 `SG.Random.seed(s)` 是同一个 mulberry32 序列
  （`Random.InitState(seed)` 设种子，Unity 里同名 API 也存在），`Mathf.PerlinNoise` 移植自 `core.js`，
  `Mathf.RoundToInt` 与 Unity 一样用银行家舍入。
- `SimLib.cs`：`SeededRandom`（= `SG.SeededRandom`，替代 `System.Random`）、`DataHash`（生成数据的校验哈希）、`Check`。

| 程序 | 内容 |
|---|---|
| `Harness.cs` | `tests/sim.js` (1)–(1c)：全电脑模拟 6 种子 × 40 年（每月一致性检查）、40 场电脑对电脑战术战斗、必杀技开 / 关的平衡统计 |
| `Harness2.cs` | (2)–(3)：随机玩家模拟 6 势力 × 4 种子 × 25 年；存档往返（`SIM_DUMP=路径` 导出对局 JSON） |
| `HarnessSpecials.cs` | (4)–(6)：必杀技数据与 `ref/specials-ref.txt`、可习得必杀池与 `ref/pool-ref.txt` 逐字段对照，压力测试与伤害倍率 |
| `HarnessProgress.cs` | 第三版成长 (7a–c, 7e–h) 与 (exp-ref)：等级表、格数与等阶、升级候选、存档 v3 与第二版迁移、成长后的平衡；经验结算对照 `ref/progress-ref.txt` |
| `HarnessTactics.cs` | 第三版计策 (ref, 7d, menu, fmt)：18 种计策的说明与 `ref/tactics-ref.txt` 逐字对照、新计策规则、控制层辅助函数、JS `toFixed` 的精确实现 |
| `HarnessWorld.cs` | `tests/world.js`：投影、世界剧本数据自检、全电脑模拟（`WORLD_SEEDS` / `WORLD_YEARS`）、经典剧本不受影响 |
| `HarnessMove.cs` | `tests/move.js`：经由己方城池链的移动 / 输送 |
| `HarnessTrace.cs` | JS ↔ C# 逐步对照（`trace.js`）：模式 `game`（整局逐月 JSON）、`battles`（战术战斗）、`pbattles`（成长后的战斗）、`learn`（升级选择序列） |
| `HarnessData.cs` | 生成数据自检：重算 `.g.cs` 的哈希并与 `Hash` 常量比对；世界剧本 / 必杀技 / 地理的一致性检查 |
| `HarnessStub.cs` | 替身自检：随机数、Perlin 噪声与 JS 的参考值一致 |

**JS 对照**（`jscompare.js`，算作一个程序「JS 对照」）：与 C# 程序并行运行网页版 `tests/sim.js`、`world.js`、`move.js`
（只运行所选程序对应的那些：sim.js 的 (1)–(1c) → Harness，(2)–(3) → Harness2，(4)–(6) → HarnessSpecials，(7a–c, e–h) → HarnessProgress，
(7d) → HarnessTactics），最后要求 JS 的每一行输出（去掉计时）都出现在对应 C# 程序的输出里；JS 多出没有 C# 程序负责的节、
或 JS 测试本身失败时判失败。环境变量：

- `SANGUO_WEB`：网页版目录（缺省 `../../../sanguozhi2-web`，相对 `Tests~/sim`）；
- `SANGUO_ALLOW_NO_NODE=1`：找不到 node 时跳过（否则判失败；HarnessTrace 同样）；
- `SANGUO_JS_COMPARE=0`：不做对照。

新增检查（移植 `tests/*.js` 的检查项）时另建 `HarnessXxx.cs`，不要改别人的文件。

### 参考结果 `Tests~/sim/ref/`

由导出工具从 JS 直接算出，供 C# 移植逐条对照：

- `specials-ref.txt`：`SG.Specials.of(gen)` 对世界剧本全部武将（前 162 名即经典剧本）以及 72 名没有条目的
  假武将（走兜底生成器）的结果：招式名、机制、能力、签名（`SG.Specials.signature`）、主色、特效、台词、说明文字
  （制表符分隔）。必须按文件顺序依次调用（兜底生成器为避免签名撞车有去重状态）。注意兜底生成器的姓名哈希按
  UTF-16 码元（`charCodeAt`）计算，`Math.round` 是四舍五入（C# 的 `Math.Round` 缺省是银行家舍入）。
- `pool-ref.txt`：第三版可习得必杀池（编号、等阶、补全后的字段、签名、说明、摘要）。
- `tactics-ref.txt`：18 种计策的说明与数值摘要。
- `progress-ref.txt`：`SG.Progress.battleExp` 的 126 组探针输入与 6 场 `autoBattle` 的经验结算。
- `project-ref.txt`：`SG.project` / `SG.unproject` 的采样（含分段断点处与图外外推）、`SG.World` 的范围与各地区矩形。

## 界面文字：`Tests~/ui/run.sh`

`UI/UIText.cs`（行内头像标记、`UI.faces` 的移植、成长界面文字、城池情报流排）与规则文件一起编译为 `HarnessUI.exe`，
输出与 `node Tests~/ui/ui-ref.js`（网页版 `ui.js` / `strategy-screen.js`）逐行比较，另有占位往返、打字机前缀与流排自检。
退出码 0 = 通过。界面本身（UGUI 的对话框、行内头像的叠放）不能无头运行，只做编译检查与代码审查。

## 第二版的无头测试

| 命令 | 内容 |
|---|---|
| `Tests~/raster/run.sh [--browser]` | 头像（`View/Raster.cs`、`View/Portrait.cs`）：外貌数据完整性、585 例 SVG 与网页版逐条比对、联系表 PNG 与耗时；`--browser` 时在 `tests/portraits.html` 渲染同一批头像逐格比较（见 `Tests~/raster/README.md`） |
| `Tests~/audio/run.sh [--browser]` | 音乐（`Core/MusicEngine.cs`、`Core/MusicInstruments.cs`）：乐谱完整性、编译结果与采样型乐器和 JS 逐点比对、离线渲染 WAV、实时模式脚本；`--browser` 时与 `tests/music.html` 比较频谱 |

## 第三版新增的无头测试

| 命令 | 内容 |
|---|---|
| `Tests~/map/run.sh` | 世界地图（`View/WorldGeo.cs`、`View/MapView.cs`、`CultureArt.cs`、`Art.cs`）：投影、距离场、平滑场、高度、数据纹理（逐字节）、`TerrainInfo`、树木 / 石块 / 村舍（逐个实例）、道路 / 桥 / 关隘、航线，及地形块四叉树、各类网格的三角形数与构建 / 刷新冒烟测试，与网页版逐项比对 |
| `Tests~/map/render.sh` | 世界地图的软件光栅器渲染（`MapRender.cs`），与网页版截图并排比较 `side-<视角>.png`（先运行 `map/run.sh`；`SKIP_WEB=1` 沿用已有截图） |
| `Tests~/battlemap/run.sh` | 战场地形（`Battle/BattleTerrain.cs`）：经典 / 世界两个剧本全部（城池, 来向）的战场与网页版 `battle-terrain.js` 逐格比对，另查可玩性（城门可达、布阵格 ≥ 6）与 BattleModel 接线。`SKIP_REFS=1` 沿用已有参考值；`BM_DUMP=剧本:城:来,…` 另导出这几张战场的取样栅格，逐字节不一致时打印 `[G 诊断]`（不能与 `SKIP_REFS` 同用） |
| `Tests~/duel/run.sh [场数 40] [种子 7] [容差 0]` | 单挑规则与电脑（`Battle/DuelSim.cs`：格开、反击、新 AI）：`tests/duel-bots.js` 全部各节的胜率表与 `node tests/duel-bots.js` 逐行对照 |
| `Tests~/figure/run.sh`、`tex.sh`、`render.sh`、`unity.sh` | 人物模型库（`View/Figure*.cs`）：几何 / 骨骼 / 动作与网页版逐项比对、三角形预算、图集与脸部贴图比对、软件光栅器并排截图、Unity API 冒烟测试（详见 `Tests~/figure/README.md`） |
| `Tests~/ui/run.sh` | 界面文字（见上节） |
| `Tests~/battleview/run.sh` | 战场画面（`Battle/BattleLook.cs` 的 `BattleBoard`）：21 个用例（12 种城墙样式、草原骑兵）与网页版 `battle-view.js` 逐项比对（地图、统计、高度、实例批次、各网格按三角形展开的属性之和、部队位置、战马根与士兵矩阵），软件光栅器并排截图 `side-<名>.png`。`SKIP_WEB=1` 沿用已有的网页版参考，`NO_RENDER=1` 不画图 |
| `Tests~/clash/run.sh` | 攻击画面（`Battle/Clash.cs`）：设定与第三版迁移（含 `Core/Game.cs` 的 `MigrateV3`）、布景取自战场（`Clash.SceneOf`，289 张战场）、21 个用例在 1–6 个时刻的布景 / 网格 / 军阵 / 箭 / 镜头 / 雾 / 每名士兵与网页版 `tests/clash.html` 逐项比对、播放流程冒烟测试，软件光栅器并排截图（详见 `Tests~/clash/README.md`） |
| `Tests~/duelview/run.sh` | 单挑画面（`DuelGame` / `DuelFigure` / `DuelArena`）：与网页版逐项对照（兵器校准、场地景物与网格和、各镜头姿势）、两整场流程、软件光栅并排图；`FLOW_ONLY=1` 只跑流程（详见 `Tests~/duelview/README.md`） |

画面测试（map/render、figure/render、battleview、clash、duelview）的并排图左边是网页版、右边是 C#，需要人工查看；
它们的数值比对部分自动判定。着色器、Unity 运行时（实例化绘制、阴影、界面布局、触控）无法无头测试，见 `README.md` 的首次运行清单。

### 一次跑完全部

```bash
export OUT_DIR=/tmp/me/out
Tests~/build.sh && Tests~/sim/run.sh && Tests~/ui/run.sh
Tests~/map/run.sh && Tests~/map/render.sh && Tests~/battlemap/run.sh && Tests~/duel/run.sh
Tests~/figure/run.sh && Tests~/figure/tex.sh && Tests~/figure/render.sh && Tests~/figure/unity.sh
Tests~/raster/run.sh --browser && Tests~/audio/run.sh
Tests~/battleview/run.sh && Tests~/duelview/run.sh && Tests~/clash/run.sh     # 要生成网页版参考，较慢（4 核约 6 + 7 + 19 分钟）
```

## 生成数据：`sanguozhi2-web/tools/export-unity-*.js`

```bash
cd ../sanguozhi2-web
node tools/export-unity-data.js           # 重新生成 Data/Generated/ 的规则数据与 Tests~/sim/ref/*.txt
node tools/export-unity-data.js --check   # 只比较：数据改过却没重新导出时退出码 1
node tools/export-unity-figure.js         # FigureData.g.cs（figure.js 的外观表与动作关键帧；--check 同上）
node tools/export-unity-portraits.js      # PortraitData.g.cs
node tools/export-unity-music.js          # MusicScores.g.cs
```

`export-unity-data.js` 生成 `Assets/Sanguo/Scripts/Data/Generated/` 下的 `SpecialsData.g.cs`、`WorldData.g.cs`、`WorldGeo.g.cs`、
`TacticsData.g.cs`（文件头「自动生成，勿手改」，各带 `Summary` 与 `Hash` 常量）。改了网页版的 `world-data.js`、`specials-data.js`、
`specials.js` 的机制表或词库、`tactics-data.js`、`model.js` 的成长表、`world-geo.js` 后重新运行，再跑 `Tests~/build.sh` 和 `Tests~/sim/run.sh Data`。
