# Tests~/audio —— 背景音乐的无头测试

`Core/MusicEngine.cs`、`Core/MusicInstruments.cs`、`Data/Generated/MusicScores.g.cs` 不引用 UnityEngine，
这里把它们和 `AudioHarness.cs` 一起用 `mcs` 编译成 `Tests~/out/audio/audio.exe`（Mono 运行），
并与网页版（事实来源）的同一批乐曲逐项比对。

```bash
Tests~/audio/run.sh             # 数据、编译比对、采样比对、离线渲染、实时模式脚本
Tests~/audio/run.sh --browser   # 另用 Playwright 在 tests/music.html 渲染同一批乐曲与 81 个乐器试听片段并比对
```

| 步骤 | 内容 | 通过条件 |
|---|---|---|
| 乐谱数据 | `MusicScores.g.cs`（由 `../sanguozhi2-web/tools/export-unity-music.js` 生成）解析后重新序列化的 FNV-1a 哈希 | 曲数、哈希与导出工具一致 |
| 乐器表 | C# 手工移植的 81 种乐器的缺省参数（gain 含混音校准、rev、pan、ring、sr、variants…）对照导出的 JS `INST` | 全部一致 |
| 编译比对 | `compile-dump.js`（Node 沙箱跑网页版 `compile()`）与 `audio.exe dump` 逐条导出每段的全部事件（拍、时值、力度、音高、装饰、倚音 / 刮奏音高、鼓击法、采样键） | 逐行一致（约 2.4 万行） |
| 采样比对 | `samples.js` 用网页版的渲染函数渲染全部 49 种采样型乐器（每种最多 4 个音高 / 击法）；`audio.exe samples` 逐点比较 | 最大差 < 1e-3（实测 3e-7） |
| 离线渲染 | 每首（含全部地域变奏、冲锋乐句）前 20 秒 → `cs/<key>.wav`，`stats.json` | 峰值 ≤ 0.98、−40 dB < RMS < −9 dB、无 NaN、记谱无误；同时报告渲染耗时 |
| 实时模式 | 后台线程准备采样、地图 → 战场交叉淡入淡出、冲锋乐句（叠加、移调、对拍、压低主曲）、胜利短曲结束回调；`live.wav` | 都开始、都有结束回调、无 NaN、峰值不超限；报告每块渲染耗时 |
| 网页版比对（`--browser`） | `browser.mjs`：`window.__renderPcm`（OfflineAudioContext）→ `js/*.wav`；`compare.mjs`：峰值、RMS、倍频程频带（63 Hz–16 kHz）、波形相关（允许 ±400 帧平移），声谱图 `png/<key>.png`（上 JS、下 C#） | 频带差 ≤ 3 dB（只计比最强频带低不到 45 dB 的频带）、RMS 差 ≤ 1.5 dB |

实测（2026-10）：31 首 RMS 与网页版相差 ≤ 0.1 dB，频带差 ≤ 0.8 dB，波形相关 0.90–1.00（Chromium 的输出恰好晚 128 帧 = 一个渲染量子）；
81 个乐器试听片段全部在容差内（连奏 / 复音乐器几乎逐样本一致；拨弦只在能量很低的 63 / 125 Hz 频带差 1–2 dB）。
Mono 单线程渲染约 14× 实时；实时模式平均每块（1024 帧 = 23 ms）1.6 ms，最长约 9 ms。

依赖：`mcs` / `mono`；比对需要 Node（无第三方依赖）；`--browser` 需要全局安装的 Playwright
（`/opt/node22/lib/node_modules/playwright`，Chromium 用 SwiftShader 参数启动）。
