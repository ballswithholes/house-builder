# Tests~/figure —— 人物模型库（View/Figure*.cs）的无头测试

网页版 `js/figure.js` 是唯一的事实来源；这里的检查都不需要 Unity，只需要 Mono（`mcs`、`mono`）与 Node，
贴图与画面对照另需 Playwright 的 Chromium（`/opt/node22/lib/node_modules/playwright`，不要运行 `playwright install`）。
输出写到 `$OUT_DIR/figure/`（缺省 `Tests~/out/figure/`，已 gitignore）。

| 脚本 | 内容 | 退出码 |
|---|---|---|
| `run.sh [过滤]` | Node 端 `refs.js`（`load-web.js` 载入 `vendor/three.min.js` + figure.js，Canvas 用桩）导出参考值；C#（`FigureMesh.cs`、`FigureRig.cs`、`FigureAtlas.cs` 与头像 / 数据文件，`-define:SANGUO_HEADLESS`）重建并逐项比对（`FigureHarness.cs`）：图集布局；39 名武将 × 3 档 LOD 的外观解析、模板键、三角形数与压缩级别、各部位三角形、网格逐顶点（位置 / 法线 / UV / 顶点色 / 蒙皮）、兵器信息；18 文化 × 4 兵种的士兵；25 种战马；34 种兵器 × 3 档、盾；27 个片段 × 3 个时刻的骨骼矩阵（含 ik / blend / lower / clamp 选项、grip、ikHand、弹簧）；骑乘；士兵群烘焙（半精度逐位）；换手系（x 镜像后绕序与法线一致）。过滤：`atlas generals soldiers horses weapons poses mounted bakes`；`budget` 另跑三角形预算（世界剧本全部武将 high ≤ 15000、mid ≤ 4000，士兵 ≤ 900，约半分钟）。`SKIP_REFS=1` 沿用已有参考值。 | 失败项数（99 = 环境 / 编译） |
| `tex.sh` | `tex-web.mjs` 在 headless Chromium 的真 Canvas2D 上画出图集（map / normal / ORM）、18 张名将面部贴图、环境贴图；C# 用 `View/Raster.cs` 画同样的贴图，逐像素比较（`FigureTex.cs`，平均差 / 大差像素占比，按区域），写出对照 PNG `tex-*.png`（左 网页版、中 C#、右 差 × 4）。另外：`tex-web.mjs` 用 WebGL 取 three 的 PMREM 预滤波环境贴图（`textureCubeUV`，= MeshStandardMaterial 的反射与辐照度取样）在 256 个方向 × 16 个粗糙度上的值（`web-pmrem.json`），与 C# 的 `FigAtlas.MakeEnvCubes`（Unity 的 `_EnvCube` / `_EnvIrr`，按着色器的 `sgEnvLod` 取级别）逐方向比较——同时检查立方体贴图各面的朝向（含太阳的水平方位）。`SKIP_WEB=1` 沿用已有的网页版输出。 | 超出阈值的项数 |
| `render.sh` | 画面对照：`render.mjs shots` 打开 `tests/figures.html?shot=1`（SwiftShader WebGL）截 12 个视角（关羽正面 / 四分之三 / 侧面 / 背面，四个动作帧，两排名将，三种文化的士兵，四名骑将与战马），并按 figures.html 的摆放写出 `scenes.json`；`FigureRender.cs`（软件光栅器：Unity 坐标、Unity 的背面剔除约定、蒙皮、地面阴影、2×2 超采样；着色是 `Sanguo/Figure` 着色器光照的逐行 C# 参照实现——three r158 PhysicalMaterial 的直接光、半球光、`MakeEnvCubes` 的环境立方体贴图、多次散射、AO / 镜面遮蔽、几何粗糙度、轮廓光，灯光按 Unity 约定 = JS 强度 / π）画同样的镜头，并写出人物像素遮罩 `mask-<视角>.png`；`render.mjs compose` 拼成 `side-<视角>.png`（左 网页版、右 C#），并在人物像素上统计平均绝对差与平均亮度差。人工查看。 | 99 = 环境 / 编译 |
| `unity.sh` | Unity 接口冒烟测试：`View/Figure.cs`、`FigureCrowd.cs`、`FigureAtlas.cs` 的 Unity 部分与 UnityEngine 替身（`../sim/UnityStub.cs` + `FigureUnityStub.cs`）一起编译运行（`FigureUnityHarness.cs`）：骨骼 Transform 与 FigRig 的换算、Unity 蒙皮公式（bindposes）、root 位置 / 朝向、WeaponTip、Grip / IkHand 参数、SetPose 之后改骨骼 Transform 的语义（Update 只写弹簧骨、dt ≤ 0 不写；Grip / IkHand 读回改动、只写双臂）、骑乘与下马（挂到战马的父节点下；销毁途中只解除关系）、换兵器、受击着色与地面贴合开关、模板与面部贴图的引用计数、Weapon / Shield、士兵群（分组、超过 1023 分批、移除、战马模板按士兵群持有、战马选项叠加、ClipDur、烘焙数据上传后丢弃）、回退路径、Preload 协程（含环境贴图分步）、图集 CPU 数据上传后丢弃、材质自带的环境立方体贴图。 | 失败项数（99 = 编译） |

编译检查仍用 `Tests~/build.sh`（真实的 Unity 引用程序集）。着色器（`Resources/Shaders/Figure.shader`、`FigureCrowd.shader`）
无法在这里编译，只能逐行审查。

参考结果（2026-10，网页版 v3）：`run.sh` 3783 项全部通过（位置最大差 1e-17、骨骼矩阵 3e-14、烘焙半精度 100 % 逐位相同）；
`tex.sh` 图集各通道平均差 0.17–0.44 / 255、面部贴图 0.05–0.34、环境 0.53，PMREM 对照：反射 1.05–2.70 / 255、辐照度 1.93；
`render.sh` 人物像素平均亮度差（网页 − C#）−1.1 … 1.7 / 255、平均绝对差 4.7–11（边缘抗锯齿为主）；`unity.sh` 59 项全部通过。
