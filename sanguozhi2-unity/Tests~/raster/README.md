# Tests~/raster —— 无头头像测试

`View/Raster.cs`（矢量栅格器）与 `View/Portrait.cs`（头像生成，网页版 `js/portrait.js` 的移植）不依赖
UnityEngine 的部分以 `-define:SANGUO_HEADLESS` 单独编译，在 Mono 下运行。需要 `mcs` / `mono`，比对与联系表还需要 Node。

```bash
Tests~/raster/run.sh             # 编译 + 数据完整性 + SVG 逐条比对 + 联系表 + 性能
Tests~/raster/run.sh --browser   # 另用 Playwright 在网页版 tests/portraits.html 渲染同一批头像并逐格比较
```

输出在 `Tests~/out/raster/`（已 gitignore）：`csharp*.png`（C#）、`browser*.png`（浏览器）、`cases.json` / `js.json` / `sheet*.json`。

| 步骤 | 内容 | 通过标准 |
|---|---|---|
| 外貌数据 | `PortraitData.g.cs` 的条目数与 FNV-1a 哈希（导出工具写入）与 C# 解析后重算的一致 | 一致 |
| SVG 比对 | `cases.js` 在 Node 里载入网页版（core、data、world-data、portrait-data、portrait）生成 490 个用例（经典 162 人 × 尺寸 / 表情 / 镜像 / 边框，15 种文化 × 8 个角色，88 个世界手工条目，120 个随机人物）及 `SG.Portrait.svg` 的输出；C# 生成同样的 SVG 逐字比较（去掉 id 流水号） | 全部逐字相同 |
| 联系表 | 40 人 128px（经典名将 + 每种文化一人 + 世界君主）、16 人 48px（细节档 0）、16 人 256px（细节档 2），报告每张耗时 | 生成成功 |
| 性能 | 300 张 128px（经典武将 + 各文化随机人物，同 tests/portraits.html 的性能测试） | 报告 |
| 浏览器对照 | 同一批头像在网页版里经 `SG.Portrait.canvas` 渲染（CPU 画布），与 C# 联系表逐格计算平均绝对差 | 参考：128px ≈ 1.3/255，48px ≈ 3.7/255，256px ≈ 0.9/255 |

`RasterHarness.cs` 的其它模式：`mono raster.exe svg in.svg out.png px` 把任意（头像所用子集的）SVG 栅格化为 PNG，
用来对照单个图元（细线、细填充、小圆）的抗锯齿。
