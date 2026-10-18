# Tests~/raster —— 无头头像测试

`View/Raster.cs`（矢量栅格器）与 `View/Portrait.cs`（头像生成，网页版 `js/portrait.js` 的移植）不依赖
UnityEngine 的部分以 `-define:SANGUO_HEADLESS` 单独编译，在 Mono 下运行。需要 `mcs` / `mono`，比对与联系表还需要 Node。

```bash
Tests~/raster/run.sh             # 编译 + 数据完整性 + SVG 逐条比对 + 联系表 + 性能
Tests~/raster/run.sh --browser   # 另用 Playwright 在网页版 tests/portraits.html 渲染同一批头像并逐格比较
```

`run.sh` 以 `pipefail` 运行，退出码 = 失败的步骤数（联系表、浏览器对照的失败也计入）。输出在 `$OUT_DIR/raster/`（缺省 `Tests~/out/raster/`，已 gitignore；多人同时测试时各用自己的 `OUT_DIR`）：`csharp*.png`（C#）、
`browser*.png`（浏览器）、`cases.json` / `js.json` / `sheet*.json`。

| 步骤 | 内容 | 通过标准 |
|---|---|---|
| 外貌数据 | `PortraitData.g.cs` 的条目数与 FNV-1a 哈希（导出工具写入）与 C# 解析后重算的一致（第三版：502 条，18 种文化） | 一致 |
| 缓存键拆分 | `Portrait.SplitKey`（`Portrait.Bind` 找替身用）：同一人同配色 / 边框 / 镜像，换尺寸、表情、年份（带出生年的人每年换键）仍是同一张脸；换配色、边框、镜像、人则不是 | 11 项全对 |
| 导出是否过期 | 网页版 `node tools/export-unity-portraits.js --check`：`PortraitData.g.cs` 与当前 `js/portrait-data.js` 一致 | 退出码 0 |
| SVG 比对 | `cases.js` 在 Node 里载入网页版（core、data、world-data、portrait-data、portrait）生成 585 个用例（经典 162 人 × 尺寸 / 表情 / 镜像 / 边框，18 种文化 × 8 个角色，159 个世界手工条目，120 个随机人物）及 `SG.Portrait.svg` 的输出；C# 生成同样的 SVG 逐字比较（去掉 id 流水号） | 全部逐字相同 |
| 联系表 | 43 人 128px（经典名将 + 每种文化一人 + 世界君主）、16 人 48px（细节档 0）、16 人 256px（细节档 2），报告每张耗时 | 生成成功 |
| 第三版联系表 | `csharpv3*.png`：天竺 12 人、非洲 8 人、毛里 2 人、僧人 5 人（龙树、提婆、童受、伊兰戈、笮融）+ 三种新文化各 4 个生成样例，128px；其中 16 人的 48px / 256px | 生成成功 |
| 性能 | 300 张 128px（经典武将 + 各文化随机人物，同 tests/portraits.html 的性能测试） | 报告 |
| 浏览器对照 | 同一批头像（含第三版联系表）在网页版里经 `SG.Portrait.canvas` 渲染（CPU 画布），与 C# 联系表逐格计算平均绝对差 | 每一格不超过上限（`MAX_MAD`，缺省 48px 为 6/255，128 / 256px 为 2.5/255）、页面无错误。现状：整表平均 128px ≈ 1.3、48px ≈ 3.7、256px ≈ 0.9，单格最大 1.9 / 4.3 / 1.25 |

`RasterHarness.cs` 的其它模式：`mono raster.exe svg in.svg out.png px` 把任意（头像所用子集的）SVG 栅格化为 PNG，
用来对照单个图元（细线、细填充、小圆）的抗锯齿。

外貌数据有变时先在 `sanguozhi2-web` 运行 `node tools/export-unity-portraits.js`（`--check` 只比较，过期时退出码 1）。
