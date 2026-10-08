# 三国志II 霸王的大陆 · Unity 第二版移植契约（UNITY-V2）

第二版功能已在网页版（`../sanguozhi2-web`）完成并经过测试。本文件规定如何把它们
**原样**移植回本 Unity 工程。每位移植者先读本文件，再读网页版的 `DESIGN-V2.md`
（功能规格）、`CONTRACT.md`（当初 C# → JS 的移植约定，本次反向使用）和对应的 JS 源文件。

## 0. 原则

- **网页版是唯一的事实来源**：规则、数值、数据、文字、作曲、外观参数都以 JS 为准。
  移植是翻译，不是重新设计。发现 JS 的明显 bug 时照样修，但在报告里说明。
- 本工程的现有约定不变：内置渲染管线、程序化低多边形网格（`View/Art.cs` 的 MeshBuilder）、
  代码搭建的 UGUI（`UI/UIKit.cs`，系统中文字体）、`RuntimeInitializeOnLoadMethod` 启动、
  协程（`IEnumerator`）而非 async/await、所有美术与音频在代码里生成，不引入任何外部包或素材。
- 只依赖 Unity 2021.3+ 自带模块（CoreModule、UI、AudioModule…）。不能用 com.unity.vectorgraphics
  等需要下载的包（本环境无法访问 Unity 包服务器）。
- 不能运行 Unity：每个文件都必须通过 `Tests~/build.sh` 的编译检查；规则与可离线计算的部分
  （头像栅格化、音乐合成）必须通过 `Tests~/` 的无头测试验证。
- 代码注释用中文，风格与现有 C# 一致。

## 1. JS → C# 对照

| JS（网页版） | C#（Unity） |
|---|---|
| three 坐标 `(x, y, -z)` | Unity `(x, y, z)`（网页版的 MeshBuilder 接受 Unity 坐标并自行翻转 z，几何代码可直接照搬） |
| `SG.Random.value() / rangeInt(a, b) / rangeFloat(a, b)` | `Random.value / Random.Range(int a, int b)`（不含 b）/ `Random.Range(float, float)` |
| `M.idiv(a, b)` | `a / b`（int） |
| `M.roundToInt` 等 | `Mathf.RoundToInt` 等 |
| `async` / `await SG.wait(s)` | 协程 `yield return new WaitForSeconds(s)`；等待子流程用 `yield return StartCoroutine(...)` |
| DOM 界面（`SG.UI.*`、CSS） | `UIKit` 的面板 / 按钮 / 对话框；新增的通用控件加进 UIKit |
| `SG.Gfx.pushScreen(screen)` | 独立的根 GameObject + 自己的 Camera（depth 高于主相机），结束时 `Destroy` 并释放网格 / 材质 / 纹理 |
| WebAudio 合成 | `AudioClip.Create` 写入 PCM；音符采样或整段离线渲染放在后台线程；播放用 `AudioSource.PlayScheduled` / `AudioSettings.dspTime` |
| Canvas2D / SVG 头像 | `View/Raster.cs`：自写的矢量栅格器（路径、贝塞尔、填充、线性/径向渐变、描边、透明度、变换）→ `Texture2D` |
| `localStorage` | `PlayerPrefs`（存档键与版本号同网页版） |

## 2. 数据：用导出工具生成，不手抄

`../sanguozhi2-web/tools/export-unity-*.js`（Node，无依赖；按领域分文件：`-data`（必杀、世界剧本、地理）、`-portraits`、`-music`）从 JS 读取数据并生成 C# 源文件，
写入 `Assets/Sanguo/Scripts/Data/Generated/`，文件头标注「自动生成，勿手改」：

- `SpecialsData.g.cs`：全部武将的必杀技条目（经典 + 世界）。
- `WorldData.g.cs`：`Scenarios.world` 的城池、连线、海路、势力、武将、地域；`WorldInfo`、`FactionInfo`、`GeneralBorn`。
- `PortraitData.g.cs`：头像外貌设定。
- `WorldGeo.g.cs`：地理数据（陆地环、内海、岛屿、河流、湖泊、山脉、高原、生物群系）。
- `MusicScores.g.cs`：全部乐曲的乐谱数据（与 JS 记谱一一对应）。
- 经典剧本 `ScenarioData.cs` 保持手写不变（与 JS `data.js` 一致）。

数据有变时重新运行导出工具即可。导出工具同时输出一个校验摘要（条目数、哈希），C# 测试据此确认数据完整。

## 3. 文件计划

| 区域 | C# 文件 | 对应 JS |
|---|---|---|
| 规则 | `Battle/BattleModel.cs`、`Battle/Specials.cs`、`Model/GameState.cs`、`Model/Commands.cs`、`Strategy/StrategyAI.cs`、`Strategy/Conquest.cs` | battle-model.js、specials.js、model.js、strategy-ai.js |
| 头像 | `View/Raster.cs`、`View/Portrait.cs` | portrait.js、portrait-data.js |
| 攻击画面 | `Battle/Clash.cs` | clash.js |
| 必杀特效与各文化战场 | `Battle/BattleView.cs` | battle-view.js |
| 单挑格斗 | `Battle/DuelGame.cs` | duel-game.js |
| 战斗流程 | `Battle/BattleController.cs` | battle-controller.js |
| 音乐 | `Core/MusicEngine.cs`、`Core/MusicInstruments.cs`、`Core/Sfx.cs` | music.js、music-inst.js、audio.js |
| 世界地图 | `View/WorldGeo.cs`（投影与查询代码）、`View/MapView.cs`、`View/CultureArt.cs`、`View/CameraRig.cs` | world-geo.js、map-view.js、culture-art.js、camera.js |
| 战略画面与流程 | `Strategy/StrategyScreen.cs`、`Core/Game.cs`、`UI/UIKit.cs` | strategy-screen.js、game.js、ui.js、style.css |

并行开发时只改自己名下的文件；需要别处配合时写进报告的 INTEGRATION 一节。

## 4. 验证

- `Tests~/build.sh`：用 mcs 对照 UnityEngine.Modules 2021.3.33、Unity3D.UnityEngine.UI、Unity3D.SDK
  （NuGet）编译全部脚本与 Editor 脚本；引用程序集不存在时脚本自行从 NuGet 下载到 `Tests~/refs/`（已 gitignore）。
- `Tests~/sim/`：无头规则测试。`UnityStub.cs` 提供 UnityEngine 的最小替身（确定性随机数）；
  移植 `tests/sim.js`、`tests/move.js`、`tests/world.js` 的检查项（不变量、平衡统计），与 JS 的统计结果对照（容差内一致）。
- `Tests~/raster/`：无头渲染若干头像为 PNG，与网页版 `tests/portraits.html` 的截图并排比较。
- `Tests~/audio/`：离线渲染每首乐曲前 20 秒为 WAV，检查峰值 / RMS / NaN，并与 JS 离线渲染比对频谱。
- 无法无头测试的部分（地图、攻击画面、单挑、界面）：编译通过 + 逐段对照 JS 的代码审查。

## 5. 第二阶段的模块接口（并行开发时以此为准）

已有：`MapProjection`（`Model/GameState.cs`，投影）、`WorldLookup`、`Portrait`（`View/Portrait.cs`，
`Portrait.Texture/Sprite/PreloadAsync`）、`Raster`、规则层（`BattleModel` 的必杀 / 计时效果 / `DuelAccepts` /
`DuelFinish`，`Specials`，`Commands` 的路线与灰色原因）。`BattleModel.specialsEnabled` 暂为 false，
战斗控制器接好必杀后改回 true。

新模块对外接口（名称固定，参数可增不可改）：

```csharp
// 音乐（Core/MusicEngine.cs、Core/MusicInstruments.cs、Data/Generated/MusicScores.g.cs；Sfx.cs 只改音乐部分）
Sfx.Music(string kind);                 // title map battle battle-defend duel victory defeat ending council（clash 为 stinger）
Sfx.SetCulture(string culture);         // map / battle 换成地域变奏；未知文化回退 han
Sfx.Stinger(string kind, float speed = 1f);
Sfx.SetMusic(bool on); Sfx.MusicOn;     // 原接口不变
Sfx.PlayOneShotMusic(string kind, System.Action done = null); // 胜利 / 战败短曲播完再回调

// 世界地图（View/WorldGeo.cs、View/MapView.cs、View/CultureArt.cs、View/CameraRig.cs）
IEnumerator MapView.BuildAsync(GameState g, string region /* "auto"|"china"|"world" */, System.Action<float, string> progress);
void MapView.Build(GameState g);        // 原接口保留（同步，经典地图）
bool MapView.LinkIsSea(int a, int b);
CameraRig: SetBounds(Rect r); SetRegion(string key, float pad); IEnumerator FlyTo(Vector2 mapPos, float dist); UseMap(MapView m); bool WorldMode;

// 攻击画面（Battle/Clash.cs）
public enum ClashMode { On, Fast, Off }  // Clash.Mode 读写，存 PlayerPrefs
IEnumerator Clash.Play(ClashSide attacker, ClashSide defender, Terrain terrain, ClashSpecial special, int playerSide, float speed, System.Action<bool /*skipped*/> done);
ClashSide Clash.FromUnit(BattleModel m, BUnit u, int troopsBefore, int troopsAfter);
IEnumerator Clash.CutIn(General g, string moveName, Color color, int side, string cry, float speed);

// 单挑格斗（Battle/DuelGame.cs）
public class DuelResult { public int winner; public string kind; public int hpA, hpB; public List<string> log; }
IEnumerator DuelGame.Play(General a, int sideA, Color colA, General b, int sideB, Color colB, int playerSide /* -1 = 电脑对电脑 */, Terrain terrain, System.Action<DuelResult> done);
bool DuelGame.Auto;                     // 测试用：玩家方也由电脑操作

// 战场与流程（Battle/BattleView.cs 只增不删、Battle/BattleController.cs）
IEnumerator BattleView.SpecialFx(BUnit caster, List<BUnit> targets, string fx, Color color);
BattleView.SetCulture(string culture);  // 城墙、城门、本阵、地面配色按城池文化

// 战略画面与流程（Strategy/StrategyScreen.cs、Core/Game.cs、UI/UIKit.cs）
// 剧本选择、两步选君主、武将详情、灰色指令说明、头像、按地域分组的列表、首次攻城提示、存档错误提示、地图构建进度。
```

模块各自的 CSS 等价物（字号下限 11 参考像素、触控目标 ≥ 44 参考像素、安全区）放在 UIKit 的通用样式里。
