# Tests~/clash —— 攻击画面（Battle/Clash.cs）的无头测试

网页版 `js/clash.js` 是唯一的事实来源。这里不需要 Unity，只需要 Mono（`mcs`、`mono`）、Node 与 Playwright 的 Chromium
（`/opt/node22/lib/node_modules/playwright`，不要运行 `playwright install`）。输出写到 `$OUT_DIR/clash/`（缺省 `Tests~/out/clash/`）。

```bash
Tests~/clash/run.sh                    # 全部用例：网页版参考 → C# 比对 → 软件光栅 → 拼图；退出码 = 不一致的项数（99 = 环境 / 编译）
Tests~/clash/run.sh han-plain celt-coast   # 只跑这几个用例
SKIP_WEB=1 Tests~/clash/run.sh         # 沿用已有的网页版参考（web-*.json / web-*.png）
NO_RENDER=1 Tests~/clash/run.sh        # 只比对，不画图
CLASH_CS=/path/to/Clash.cs Tests~/clash/run.sh   # 用另一份 Clash.cs（草稿）
```

| 文件 | 内容 |
|---|---|
| `cases.json` | 用例（`tests/clash.html` 的参数：`case` 地形-兵力比、`cult` 文化对阵、`special` 必杀、`scene` / `biome` / `coast` 布景）。前四个为必测：汉 平原、罗马石城 城门、非洲泥砖 本城、凯尔特海岸 登陆；其后为河川 + 借东风、森林 + 火烧连营、山丘、山岳、南蛮木寨、平原 + 青龙偃月斩。 |
| `web.mjs` | `refs`：headless Chromium（SwiftShader WebGL，960 × 540）打开 `tests/clash.html?embed=1&board=0&freeze=…`，等场景建好、动画停在接敌（1.66 s）与混战（2.2 s），导出 `web-<名>.json`（布景、双方、随机种子、地形高度采样、地面 / 道具 / 水面网格的顶点数与各属性之和、草丛野花实例、两军阵列与每名士兵的计划、箭、兵力碎块、事件、士兵群统计；两个时刻的镜头、HUD 顶栏下沿、每名士兵 / 旗手 / 武将的位置、朝向、动作片段、旗面与旗杆位置——three 坐标），截图 `web-<名>-<时刻>.png`（HUD 隐藏）。`compose`：与 `unity-<名>-<时刻>.png` 左右拼成 `side-<名>-<时刻>.png`。 |
| `ClashStub.cs` | 在 `../sim/UnityStub.cs`、`../figure/FigureUnityStub.cs` 之上补的 UnityEngine 替身：灯光、天空盒、雾与 Trilight 环境光、镜头的层 / 清屏 / 视口换算、键盘、`Graphics.DrawMesh` 的记录等。`run.sh` 把人物替身里的若干类改成 `partial`、`SystemInfo.supportsInstancing` 改成可写字段。 |
| `ClashHarness.cs` | 用 C# 移植（`Clash.cs` 以 `CLASH_HEADLESS` 编译：HUD 与特写换成同名空实现，场景代码与游戏内相同）按同一组参数建场景（`ClashScene.BuildSync`），逐项比对上面的全部数据；两个时刻按游戏内的方式逐帧 `Tick` 推进后比较镜头（位置、前向、上方）与每名士兵（士兵群实例矩阵、片段与起点）、旗手、武将、旗面与帅旗杆。坐标统一换成 three 坐标。 |
| `ClashRender.cs` | 软件光栅器：按 C# 场景里实际建出的物体（根节点下的 MeshRenderer / SkinnedMeshRenderer，与这一帧 `Draw()` 提交的箭、草丛野花、特效、粒子）画 `unity-<名>-<时刻>.png`。着色为 `Sanguo/ClashGround`、`ClashProp`、`ClashGrass`、`ClashFlag`、`ClashWater`、`Unlit` / `Additive` 与 `Sanguo/Figure` 的逐行 C# 参照实现；太阳阴影（正交阴影贴图 + 3×3 PCF）、线性雾、Sky 天空、2×2 超采样。画图时士兵群走回退路径（每名士兵一个 Figure，按同一时钟摆姿势）。 |

已知差异（目视对照时忽略）：无头编译下旗面没有姓氏字（游戏内用字体画进 RenderTexture）；粒子、扬尘、箭落空的位置
与帧率有关（网页版也一样，每次截图略有不同）；水面与草的摆动按实际时间。

参考结果（2026-10，网页版 v3）：十个用例 18730 项全部一致（四个必测用例 8054 项；士兵位置最大差 ≤ 1.4e-6，朝向 < 1e-6 rad，镜头前向 / 上方夹角 < 0.05°），
20 张并排图中除上面的已知差异外布景、军阵、动作、旗面、帅旗、镜头一致。
着色器无法在这里编译，只能逐行审查。
