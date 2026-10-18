# Tests~/clash —— 攻击画面（Battle/Clash.cs）的无头测试

网页版 `js/clash.js` 是唯一的事实来源。这里不需要 Unity，只需要 Mono（`mcs`、`mono`）、Node 与 Playwright 的 Chromium
（`/opt/node22/lib/node_modules/playwright`，不要运行 `playwright install`）。输出写到 `$OUT_DIR/clash/`（缺省 `Tests~/out/clash/`）。

```bash
Tests~/clash/run.sh                    # 全部：网页版参考 → 设定与迁移、布景取自战场、各用例的 C# 比对 → 软件光栅 → 拼图 → 播放流程；退出码 = 不一致的项数（上限 98；99 = 环境 / 编译）
Tests~/clash/run.sh han-plain celt-coast   # 只跑这几个用例（另有三个名字：settings = 设定与迁移，sceneof = 布景取自战场，flow = 播放流程）
SKIP_WEB=1 Tests~/clash/run.sh         # 沿用已有的网页版参考（web-*.json / web-*.png）
NO_RENDER=1 Tests~/clash/run.sh        # 只比对，不画图
CLASH_CS=/path/to/Clash.cs Tests~/clash/run.sh   # 用另一份 Clash.cs（草稿）
```

| 文件 | 内容 |
|---|---|
| `cases.json` | 用例（`tests/clash.html` 的参数：`case` 地形-兵力比、`cult` 文化对阵、`special` 必杀、`scene` / `biome` / `coast` 布景；另有 `times` 时刻（缺省 `contact`、`melee`；可选 `march` 0.35 s、`charge` 1.3、`contact` 1.66、`fx` 1.8、`melee` 2.2、`regroup` 2.9、`end` 3.3）与 `screen` 画面尺寸（缺省 [960, 540]））。前四个为必测：汉 平原（六个时刻）、罗马石城 城门、非洲泥砖 本城、凯尔特海岸 登陆；其后为河川 + 借东风、森林 + 火烧连营、山丘 以众击寡、山岳 以寡击众、南蛮木寨 击溃、平原 + 青龙偃月斩（三个必杀另取 `fx` 时刻）；各文化 / 群落 / 兵力比：草原毡帐、南蛮干栏、日耳曼木栅城门、贵霜红砖本城、安息土坯城门 击溃、天竺茅屋 残兵、北方针叶林、高山 反被击溃、倭 海岸、地中海 + 百步穿杨；竖屏 540 × 960 的罗马城门。 |
| `web.mjs` | `refs`：headless Chromium（SwiftShader WebGL，按用例的画面尺寸）打开 `tests/clash.html?embed=1&board=0&freeze=…`，等场景建好、动画依次停在用例的各个时刻，导出 `web-<名>.json`（布景、双方、随机种子、地形高度采样、地面 / 道具 / 水面网格的顶点数与各属性之和、草丛野花实例、两军阵列与每名士兵的计划、箭、兵力碎块、事件、士兵群统计；各时刻的镜头、雾、HUD 顶栏下沿、每名士兵 / 旗手 / 武将的位置、朝向、动作片段、旗面与旗杆位置——three 坐标），截图 `web-<名>-<时刻>.png`（HUD 隐藏）。`compose`：与 `unity-<名>-<时刻>.png` 左右拼成 `side-<名>-<时刻>.png`。 |
| `sceneof.js` | Node（不用浏览器）载入网页版，经典 / 世界两个剧本各建一次战略地图，为每座城池的第一条连线与全部海路连线生成战场，在攻方布阵格与每隔一格的格点上取 `fromBattle(...).scene`（文化、群落、海岸），写 `sceneof.txt`。 |
| `ClashStub.cs` | 在 `../sim/UnityStub.cs`、`../figure/FigureUnityStub.cs` 之上补的 UnityEngine 替身：灯光、天空盒、雾与 Trilight 环境光、镜头的层 / 清屏 / 视口换算、键盘、`Graphics.DrawMesh` 的记录等。`run.sh` 把人物替身里的若干类改成 `partial`、`SystemInfo.supportsInstancing` 改成可写字段。 |
| `ClashHarness.cs` | ① `settings`：设定（开 / 快 / 关）的存储与第三版迁移——`run.sh` 从 `Core/Game.cs` 原样摘出 `MigrateV3` 编译进来，十种起始存储（新键 / 旧键 / 迁移标记）× 两次启动 + 三次切换，对照网页版 `clash.js` 的 `loadMode` + `game.js` 的 `migrateV3`（逐行转写）；② `sceneof`：同样的战场上 `Clash.SceneOf` 与 `sceneof.txt` 逐条比较；③ 各用例：用 C# 移植（`Clash.cs` 以 `CLASH_HEADLESS` 编译：HUD 与特写换成同名空实现，场景代码与游戏内相同）按同一组参数建场景（`ClashScene.BuildSync`），逐项比对上面的全部数据；各时刻按游戏内的方式逐帧 `Tick` 推进后比较镜头（位置、前向、上方、视角、宽高比）、雾、每名士兵（士兵群实例矩阵、片段与起点）、旗手、武将、旗面与帅旗杆。坐标统一换成 three 坐标。④ `flow`：`Clash.Play` 的流程冒烟测试（不对照网页版）——两场排队依次播放、播放时接管主相机 / 太阳 / 雾 / 环境光 / 阴影距离且结束后全部恢复、场景根销毁；播放中按空格跳过（`done(true)`）；必杀场；「关」时立即回调并存进 `ModeKeyV3`。 |
| `ClashRender.cs` | 软件光栅器：按 C# 场景里实际建出的物体（根节点下的 MeshRenderer / SkinnedMeshRenderer，与这一帧 `Draw()` 提交的箭、草丛野花、特效、粒子）画 `unity-<名>-<时刻>.png`。着色为 `Sanguo/ClashGround`、`ClashProp`、`ClashGrass`、`ClashFlag`、`ClashWater`、`Unlit` / `Additive` 与 `Sanguo/Figure` 的逐行 C# 参照实现；太阳阴影（正交阴影贴图 + 3×3 PCF）、线性雾、Sky 天空、2×2 超采样。画图时士兵群走回退路径（每名士兵一个 Figure，按同一时钟摆姿势）。 |

已知差异（目视对照时忽略）：无头编译下旗面没有姓氏字（游戏内用字体画进 RenderTexture）；粒子、扬尘、箭落空的位置、借东风雷电的折线形状
与帧率有关（网页版也一样，每次截图略有不同）；水面与草的摆动按实际时间。借东风的雷：网页版把 three 坐标交给 SG.MeshBuilder（它再翻转 z），
雷落在镜像的 z 上（偏近镜头）；C# 有意建在落点上（`fx` 时刻的并排图里可见）。

不在这里测的：HUD、特写与旗面姓氏字只做编译检查（`CLASH_HEADLESS` 下为空实现）；镜头用到的 HUD 顶栏下沿取网页版导出的值
（`ClashHud.BarNdc`），C# HUD 自己的排版依赖字体，无头时不测；着色器无法在这里编译，只能逐行审查。

参考结果（2026-10，网页版 v3）：全部 66569 项一致——settings 134 项、sceneof 289 张战场 25549 项（海岸 4989）、21 个用例 40871 项、flow 15 项
（四个必测用例 9466 项；54 个时刻的士兵位置最大差 ≤ 2.1e-6，朝向 < 1e-6 rad，镜头前向 / 上方夹角 < 0.05°；雾与网页版一致，竖屏
冲锋时 58.5–159.5）。54 张并排图中除上面的已知差异外布景、军阵、动作、旗面、帅旗、镜头一致。
