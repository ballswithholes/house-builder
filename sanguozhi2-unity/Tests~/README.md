# Tests~ —— 离线编译检查与无头测试

Unity 会忽略名字以 `~` 结尾的目录，所以这里的文件不会被导入工程。这里的检查都不需要 Unity，
只需要 Mono（`mcs`、`mono`），生成数据时还需要 Node（不需要装任何依赖）。

## 编译检查：`Tests~/build.sh`

```bash
Tests~/build.sh          # 退出码 0 = 全部通过
```

- 用 `mcs`（C# 7.2）对照 Unity 引用程序集编译 `Assets/Sanguo/Scripts/**/*.cs`（输出 `Tests~/out/game.dll`），
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
- 只报告错误与警告；错误时退出码非 0。

## 无头规则测试：`Tests~/sim/run.sh`

```bash
Tests~/sim/run.sh                 # 运行全部 Harness*.cs
Tests~/sim/run.sh Harness2 Data   # 只运行 Harness2.cs、HarnessData.cs（可省去 "Harness" 前缀）
```

每个 `Harness*.cs` 是一个独立的程序（各有 `Main`），与下列文件一起编译、运行；退出码 = 失败的程序数。

- 规则文件：`Data/*.cs`、`Data/Generated/*.cs`、`Model/*.cs`、`Strategy/StrategyAI.cs`、`Strategy/Conquest.cs`、
  `Battle/BattleModel.cs`、`Battle/Specials.cs`（存在时）。规则文件只能用 `UnityStub.cs` 里有的 UnityEngine API；
  缺什么就按 Unity 的签名补进替身。
- `UnityStub.cs`：UnityEngine 的最小替身。`Random` 与网页版 `SG.Random.seed(s)` 是同一个 mulberry32 序列
  （`Random.InitState(seed)` 设种子，Unity 里同名 API 也存在），`Mathf.PerlinNoise` 移植自 `core.js`，
  `Mathf.RoundToInt` 与 Unity 一样用银行家舍入。
- `SimLib.cs`：`SeededRandom`（= `SG.SeededRandom`，替代 `System.Random`）、`DataHash`（生成数据的校验哈希）、`Check`。

| 程序 | 内容 |
|---|---|
| `Harness.cs` | 全电脑模拟 6 种子 × 40 年（每月一致性检查）+ 40 场电脑对电脑战术战斗 |
| `Harness2.cs` | 随机玩家模拟：6 势力 × 4 种子 × 25 年 |
| `HarnessData.cs` | 生成数据自检：重算三个 `.g.cs` 的哈希并与 `Hash` 常量比对；世界剧本 / 必杀技 / 地理的一致性检查 |
| `HarnessStub.cs` | 替身自检：随机数、Perlin 噪声与 JS 的参考值一致 |

新增检查（移植 `tests/sim.js`、`tests/move.js`、`tests/world.js` 的检查项）时另建 `HarnessXxx.cs`，不要改别人的文件。

### 参考结果 `Tests~/sim/ref/`

由导出工具从 JS 直接算出，供 C# 移植逐条对照：

- `specials-ref.txt`：`SG.Specials.of(gen)` 对世界剧本全部 361 名武将（前 162 名即经典剧本）以及 72 名没有条目的
  假武将（走兜底生成器）的结果：招式名、机制、能力、签名（`SG.Specials.signature`）、主色、特效、台词、说明文字
  （制表符分隔）。必须按文件顺序依次调用（兜底生成器为避免签名撞车有去重状态）。注意兜底生成器的姓名哈希按
  UTF-16 码元（`charCodeAt`）计算，`Math.round` 是四舍五入（C# 的 `Math.Round` 缺省是银行家舍入）。
- `project-ref.txt`：`SG.project` / `SG.unproject` 的采样（含分段断点处与图外外推）、`SG.World` 的范围与各地区矩形。

## 生成数据：`sanguozhi2-web/tools/export-unity-data.js`

```bash
cd ../sanguozhi2-web
node tools/export-unity-data.js           # 重新生成 Data/Generated/*.g.cs 与 Tests~/sim/ref/*.txt
node tools/export-unity-data.js --check   # 只比较：数据改过却没重新导出时退出码 1
```

生成 `Assets/Sanguo/Scripts/Data/Generated/` 下的 `SpecialsData.g.cs`、`WorldData.g.cs`、`WorldGeo.g.cs`
（文件头「自动生成，勿手改」，各带 `Summary` 与 `Hash` 常量）。改了网页版的 `world-data.js`、`specials-data.js`、
`specials.js` 的机制表或词库、`world-geo.js` 后重新运行，再跑 `Tests~/build.sh` 和 `Tests~/sim/run.sh Data`。
