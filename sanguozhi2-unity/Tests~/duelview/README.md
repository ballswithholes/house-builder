# Tests~/duelview —— 单挑画面（Battle/DuelGame.cs、DuelFigure.cs、DuelArena.cs）的无头测试

网页版 `js/duel-game.js` 是唯一的事实来源。这里不需要 Unity，只需要 Mono（`mcs`、`mono`）、Node 与 Playwright 的 Chromium
（`/opt/node22/lib/node_modules/playwright`，不要运行 `playwright install`）。输出写到 `$OUT_DIR/duelview/`（缺省 `Tests~/out/duelview/`）。

```bash
Tests~/duelview/run.sh                     # 全部：网页版参考 → 流程测试 → 逐项对照 → 软件光栅 → 拼图；退出码 = 不一致的项数（99 = 环境 / 编译）
Tests~/duelview/run.sh flow gy-lb          # 只跑流程测试与「关羽 对 吕布」
SKIP_WEB=1 Tests~/duelview/run.sh          # 沿用已有的网页版参考（web-*.json / web-*.png）
NO_RENDER=1 Tests~/duelview/run.sh         # 只比对，不画图
FLOW_ONLY=1 Tests~/duelview/run.sh         # 只跑流程测试
```

| 文件 | 内容 |
|---|---|
| `cases.json` | 七对武将（关羽 / 吕布 平原、塞维鲁 / 阿尔达班 河川、黄忠 / 刘备 本城、张飞 / 马超 丘陵、巴洛马尔 / 马克西明 森林、太史慈 / 故国川王、夏侯渊 / 典韦）与镜头（`shots`：两人的 DuelFighter 字段——待机、轻一与轻三命中、格开、被格开 / 反击窗口、满蓄重击、蓄力、击飞、倒地、绝技亮相与第二斩、胜负、弓将开弓 / 放箭；视图推进 40 × 0.05 秒、各镜头前弹簧与人物时钟归零；镜头在两人中点前方）。 |
| `web.mjs` | `gens`：用例武将的能力、文化与势力色（`web-gens.json`）。`refs`：headless Chromium（SwiftShader WebGL，960 × 540）打开 `tests/duel.html`，每对武将开一场观战单挑，场景建好后把 `DuelScreen.update` 换成空函数、隐藏界面，导出 `web-<名>.json`（武将的兵器 / 盾 / 披风 / 弓将、兵器长度校准的全部测量值、场地的实例化景物统计 / 火把 / 两军人数 / 各网格的顶点数与坐标和（旗面先按 `arenaT` 摆好）/ 场地随机数的下一个值；各镜头的片段、片段时刻、键、根节点、转身角、前冲、辉光、刃尖 / 刃根与 7 根骨骼的世界坐标）并截图 `web-<名>-<镜头>.png`。`compose`：与 `unity-<名>-<镜头>.png` 左右拼成 `side-<名>-<镜头>.png`。 |
| `DuelStub.cs` | 在 `../sim/UnityStub.cs`、`../figure/FigureUnityStub.cs` 之上补的 UnityEngine 替身（灯光、天空盒、雾与环境光、镜头、输入、音源、折线、RenderTexture / TextMesh、Transform 的逆变换与自转、DrawMesh 记录），以及 Sanguo 的 `UIKit` / `Sfx` / `Game` 与界面替身 `DuelHud` / `DuelInput`（记录调用）。`run.sh` 把人物替身里的若干类改成 `partial`、让 `StartCoroutine` 登记协程、`Transform.position` 计入父节点缩放（单挑的镜像 x 缩放）。 |
| `DuelViewHarness.cs` | 流程：真实的 `DuelScreen`（`Setup` → 协程 → 逐帧 `Update`）打两整场——关羽（脚本玩家 parry）对 吕布，黄忠 对 刘备（电脑对电脑，开战时双方怒气满）；检查无异常、走完全程、结果与格斗统计、出现过的状态与每个状态用到的动作片段、界面调用（格开！、四周金光、反击小圆、开战、胜负）、音效、胜方欢呼、弓将的换弓 / 插地兵器 / 弓弦、辉光、拆除。对照：与 `web-<名>.json` 逐项比对（场地网格按顶点数配对、比对坐标和；容差见源码）。 |
| `DuelRender.cs` | 软件光栅器：单挑世界根下的物体与这一帧提交的植被（测试时关掉实例化，士兵群走回退路径）；着色为 `Sanguo/DuelGround`、`DuelProp`、`DuelFoliage`、`DuelWater`、`DuelSky` 与 `Sanguo/Figure` 的 C# 参照实现，主光阴影、线性雾，2 × 2 超采样。 |

已知差异（目视对照时忽略）：粒子（光点）、刀光、弓弦不画；草与树冠的摆动按各自的时钟；网页版的阴影只覆盖主光阴影相机（场地 ±7 米），
C#（= Unity）按镜头前 30 米的阴影距离（城墙整面在网页版阴影相机之外，所以 C# 的城墙不接收阴影；其余远处景物仍可能多出影子）；Unity 的轮廓光在 ForwardAdd 通道里单独经过地面着色器的高光肩部（参照实现按总和）。

参考结果（2026-10，网页版 v3）：见 run.sh 的输出（`== 全部通过（N 项）`、最大差一行）。着色器无法在这里编译，只能逐行审查。
