# 三国志-霸王的世界 · Unity 第三版移植契约（UNITY-V3）

第三版功能已在网页版（`../sanguozhi2-web`）完成并经过测试。本文件规定如何把它们移植回本 Unity 工程。
先读 `UNITY-V2.md`（§0 原则、§1 JS → C# 对照、§2 数据导出、§4 验证仍然有效），再读网页版的
`DESIGN-V3.md`（功能规格）和对应的 JS 源文件。冲突时以本文件为准。

## 0. 原则（在 UNITY-V2 §0 之上的变化）

- **网页版仍是唯一的事实来源**：规则、数值、数据、文字、几何参数、动作关键帧、配色、着色器的光照公式都以 JS 为准。
  移植是翻译，不是重新设计。
- **美术方向改变**：DESIGN-V3 §1 取代 V2 的「低多边形平涂」约定。角色、士兵、兵器、马、地形、战场、城池一律
  **平滑法线**（不再每面一色）、程序化纹理。仍然全部在代码里生成，不引入外部包或素材。
- 新的着色器放在 `Assets/Sanguo/Resources/Shaders/`（内置渲染管线的 ShaderLab / CG 表面着色器，
  `Art.LoadShader` 用 `Resources.Load` 加载，保证打包时包含）。这里无法编译着色器，所以：
  照抄现有 `LowPoly.shader` 的写法（`#pragma surface ... fullforwardshadows addshadow`、`target 3.0`），
  只用 `UnityCG.cginc` / `Lighting.cginc` 里的标准函数，每个着色器带 `FallBack "Diffuse"`；
  法线贴图在着色器里手动解码（`n = tex.rgb * 2 - 1`），不用 `UnpackNormal`（运行时生成的 RGBA 纹理不是 DXT5nm 格式）。
- 不能运行 Unity：编译检查（`Tests~/build.sh`）+ 无头测试（几何、纹理、规则逐项与 JS 对照）+ 逐段代码审查。
  无头测试要能把 C# 生成的网格**画成 PNG**（见 §4 的软件光栅器），与网页版截图并排比较。
- 代码注释用中文，风格与现有 C# 一致。名称改为 **三国志-霸王的世界**（DESIGN-V3 §7）。

## 1. JS → C# 对照（新增）

| JS（网页版 v3） | C#（Unity） |
|---|---|
| three 右手系坐标 `(x, y, z)` | Unity `(x, y, −z)`；法线同样翻转 z；三角形绕序反转（`a, b, c` → `a, c, b`）；四元数 `(x, y, z, w)` → `(−x, −y, z, w)` |
| three `Euler(x, y, z)`（缺省顺序 XYZ：`R = Rx·Ry·Rz`） | 显式合成 `qx * qy * qz` 再换手系；**不要**用 `Quaternion.Euler`（Unity 的顺序是 ZXY） |
| figure.js 模型朝 +Z、右手在 −X（three） | 换手系后朝 −Z、右手在 −X；根节点再绕 Y 转 180° → Unity 习惯的朝 +Z、右手在 +X |
| `THREE.SkinnedMesh` + `Skeleton`（28 根骨骼）+ `skinIndex/skinWeight`（每顶点 4 个） | `SkinnedMeshRenderer` + `Transform[] bones` + `Mesh.bindposes` + `Mesh.boneWeights`（`BoneWeight` 4 个影响）；`updateWhenOffscreen = false`，手动设 `localBounds` |
| `fig.setPose(clip, t, {ik})`：每帧直接写骨骼的局部旋转 / 位置 | 同名 C# 方法，直接写 `Transform.localRotation / localPosition`；不用 Animator / AnimationClip |
| `MeshStandardMaterial` + `onBeforeCompile` 补丁（图集、法线、AO / 粗糙度 / 金属度、环境贴图、轮廓光、势力色遮罩、受击闪白） | `Sanguo/Figure` 表面着色器（`StandardSpecular` 或 `Standard` 光照模型 + 手写轮廓光与势力色），属性名照 JS 的 uniform 名 |
| 程序生成的环境贴图（`EquirectangularReflectionMapping`） | 代码生成的 `Cubemap`，设为 `RenderSettings.customReflection`（`defaultReflectionMode = Custom`）；着色器用 `UNITY_SAMPLE_TEXCUBE(unity_SpecCube0, …)` |
| `CanvasTexture`（图集 1024²、脸部纹理） | `View/Raster.cs` 栅格化到 `Texture2D`（必要时给 Raster 加 Canvas2D 的等价操作）；`DataTexture` → `Texture2D.SetPixels32` |
| `THREE.InstancedMesh`（草丛、树、人群） | `Graphics.DrawMeshInstanced`（材质 `enableInstancing = true`，每批 ≤ 1023）或合并网格；与 JS 一样的批次划分 |
| 地形 `ShaderMaterial`（按坡度 / 高度 / 群落混合、三平面投影、细节噪声） | `Sanguo/Terrain` 表面着色器，逐行移植 GLSL → CG；顶点属性 `aMat` 放进 `uv2`（`TEXCOORD1`）或顶点色 |
| 颜色：JS 关闭 ColorManagement、线性输出、不做色调映射（伽马工作流） | Gamma 色彩空间（工程没有 ProjectSettings，`Editor/SanguoSetup.cs` 显式设 `PlayerSettings.colorSpace = ColorSpace.Gamma`）；颜色值直接照抄，不做 sRGB 转换 |
| `SG.Gfx.pushScreen` 全屏场景 | 同 UNITY-V2：独立根 GameObject + 自己的 Camera，结束时释放网格 / 材质 / 纹理 |

## 2. 数据：用导出工具生成，不手抄

- 重新运行 `../sanguozhi2-web/tools/export-unity-*.js`：世界数据（第三版研究合并后约 70 个势力、200 座城、500 名武将）、
  必杀（含 38 招「可习得必杀」池与等阶）、头像外貌（新武将）。
- 新增导出：`TacticsData.g.cs`（`js/tactics-data.js` 的 18 种计策：名称、等阶、智力门槛、机制参数、文字），
  成长表（经验表 0/80/200/…/2160、各来源的经验值、可学数量与等阶上限的公式参数）进 `SpecialsData.g.cs` 或新文件。
- figure.js 的大表（各文化衣甲、31 种兵器、名将造型、兵种外观、动作关键帧）：表格类数据优先导出为 `FigureData.g.cs`；
  生成几何的算法手工翻译。
- 导出工具同时输出校验摘要与 `Tests~/*/ref/` 参考值（见 §4）。

## 3. 文件计划

| 区域 | C# 文件 | 对应 JS |
|---|---|---|
| 规则：成长、计策、18×13 战场、学来的必杀 | `Battle/BattleModel.cs`、`Battle/Specials.cs`、`Battle/Tactics.cs`（新）、`Model/GameState.cs`（`Progress`、存档 v3 与 v2 迁移）、`Strategy/StrategyAI.cs`、`Strategy/Conquest.cs`、`Data/Defs.cs` | battle-model.js、specials.js、specials-data.js、tactics-data.js、model.js、strategy-ai.js、data.js |
| 战场地形取自城池周边 | `Battle/BattleTerrain.cs`（新） | battle-terrain.js |
| 单挑规则：格开与反击 | `Battle/DuelSim.cs` | duel-game.js（规则与 AI 部分） |
| 角色 / 士兵 / 马 / 兵器模型库 | `View/Figure.cs`（对外接口）、`View/FigureMesh.cs`（几何）、`View/FigureRig.cs`（骨骼、动作、IK、弹簧）、`View/FigureAtlas.cs`（图集与脸部纹理）、`Data/Generated/FigureData.g.cs`、`Resources/Shaders/Figure.shader` | figure.js |
| 世界地图：平滑地形与更多元素 | `View/MapView.cs`（含 `TerrainInfo / SurfaceY / RoadDist`）、`View/Art.cs`、`View/CultureArt.cs`、`View/WorldGeo.cs`、`Resources/Shaders/Terrain.shader`、`Water.shader` | map-view.js、art.js、culture-art.js、world-geo.js |
| 战场画面 | `Battle/BattleView.cs`、`Battle/BattleViewFx.cs`、`Battle/BattleLook.cs` | battle-view.js |
| 攻击画面 | `Battle/Clash.cs` | clash.js |
| 单挑画面 | `Battle/DuelGame.cs`、`Battle/DuelHud.cs`、`Battle/DuelArena.cs`（`DuelModels.cs`、`DuelAnim.cs` 由 Figure 取代后删除） | duel-game.js（画面、HUD、输入） |
| 战斗流程与成长界面 | `Battle/BattleController.cs` | battle-controller.js |
| 改名、头像无处不在、成长界面 | `Strategy/StrategyScreen.cs`、`Core/Game.cs`、`UI/UIKit.cs`、`Editor/SanguoSetup.cs`（productName）、`README.md` | strategy-screen.js、game.js、ui.js、style.css、index.html |

并行开发时只改自己名下的文件；需要别处配合时写进报告的 INTEGRATION 一节。

## 4. 验证

- `Tests~/build.sh`：全部脚本与 Editor 脚本编译通过。
- `Tests~/sim/`：移植 `tests/sim.js` 第 7 节（成长：7a–h）、18×13 战场的平衡统计、学来的暗杀倍率；`tests/world.js`
  （新世界数据）；与 JS 统计**逐条一致**（mulberry32 替身，同 V2）。新增 `HarnessProgress.cs`、`HarnessTactics.cs`。
- `Tests~/duel/`：`tests/duel-bots.js` 的格开机器人、连打机器人对局，胜率与 JS 逐行对照。
- `Tests~/battlemap/`（新）：Node 端用网页版的 world-geo.js + map-view.js（terrainInfo 不依赖 WebGL 的部分）+ battle-terrain.js
  为全部（城池, 来向）生成 `ascii` 参考；C# 的 `BattleTerrain.Generate` 结果逐格一致。
- `Tests~/figure/`（新）：
  - 几何：Node 端用 `vendor/three.min.js` + figure.js（Canvas 用桩替代）导出若干武将 / 士兵 / 马 / 兵器在 3 档 LOD 的
    顶点数、三角形数、包围盒与位置校验和，以及若干动作在若干时刻的骨骼世界矩阵；C# 对照（容差 1e-3）。
  - 纹理：图集与脸部纹理由 Raster 栅格化为 PNG，与网页版 Canvas 输出并排比较（同 `Tests~/raster/`）。
  - 画面：一个小的软件光栅器（z 缓冲 + 半兰伯特 + 图集采样）把 C# 生成并摆好姿势的模型画成 PNG
    （正面、侧面、四分之三、动作帧），与网页版 `tests/figures.html?shot=1` 的截图并排比较——这是看不到 Unity 画面时
    检查模型的唯一途径，必须做。
- `Tests~/map/`：地形网格三角形数、各类元素数量（树、岩石、村落、桥、关隘）与网页版一致。
- 无法无头测试的部分（着色器、攻击画面、单挑画面、界面）：编译通过 + 逐段对照 JS 的代码审查；README 的首次运行清单补上第三版要看的内容。
