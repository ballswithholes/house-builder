# 界面类名与 SG.UI 用法（css/style.css · js/ui.js）

给搭建画面的模块（`game.js`、`strategy-screen.js`、`battle-controller.js`、
`battle-view.js`）参考。所有颜色、字体、尺寸都在 `css/style.css` 里，JS 只需要
拼出下面的结构、加上类名。

## 0. 尺寸约定

- 根字号 `html { font-size: clamp(13px, .5vw + 1.333vh, 24px) }`，近似 Unity
  CanvasScaler（参考 1600×900，match 0.6）。**1rem = 20 个 Unity 参考像素**。
  1280×720 → 16px，1920×1080 → 24px，1024×768 → 15.4px，手机横屏 → 13px。
- `SG.UI.scale()` = 当前 1 个参考像素对应的 CSS 像素（根字号 / 20）。
- C# 里传给 UIKit 的尺寸（`Choose` 的 width、`Medal` 的 size、`WorldFollow` 的 offset）
  原样传给 `SG.UI` 即可，内部自动换算。
- 所有按钮 / 列表行 ≥ 44px 高。短屏（`max-height: 540px`，手机横屏）自动使用更紧凑的版式。
- `index.html` 的 viewport 需含 `viewport-fit=cover`，安全区由 CSS `env(safe-area-inset-*)` 处理。

## 1. 图层（`SG.UI.init()` 在 `#ui` 内创建，自下而上）

| 类名 | 用途 | `SG.UI.layers.*` / `SG.UI.layer(name)` |
|---|---|---|
| `.sg-layer.sg-labels` | 跟随三维坐标的标签、飘字；不拦截指针 | `labels` |
| `.sg-layer.sg-screens` | 各画面 HUD；避开安全区 | `screens` |
| `.sg-layer.sg-modals` | 模态对话框（全屏遮罩，面板避开安全区） | `modals` |
| `.sg-layer.sg-toasts` | 提示、横幅；避开安全区；不拦截指针 | `toasts` |

`SG.UI.screen(className)` → 在 screens 层建一个 `.sg-screen`（全屏、自身不拦截指针，
**直接子元素**拦截指针）。HUD 面板放在它下面即可，地图在空白处照常可拖动。

## 2. JS 接口（`SG.UI`）

```js
SG.UI.init()                                   // 幂等；其他函数也会自动调用
SG.UI.el(tag, className, html)                 // → HTMLElement
SG.UI.button(parent, html, onClick, { primary, small, className, title, ariaLabel, disabled })
                                               // → <button class="sg-btn">；点击时播放 click 音效
                                               //   btn.disabled = true 即 Unity 的 interactable = false
                                               //   btn.setLabel(html) / btn.setEnabled(bool)
SG.UI.panel(parent, className)                 // → <div class="sg-panel className">
SG.UI.medal(glyph, color, size = 96)           // 圆形徽章；size 为参考像素；color 可为 '#hex' 或 THREE.Color
SG.UI.bar(value01, color)                      // → .sg-bar，带 .setValue(v) / .setColor(c) / .fill
SG.UI.item(label, right = null, enabled = true, desc = null)
SG.UI.pulse(el, className = 'is-hit')          // 重新触发一次性动画类
SG.UI.openModal(className, { width, height, dim = true })
                                               // → { blocker, panel, close() }；计入 anyModal()
SG.UI.cssColor(c)                              // THREE.Color / {r,g,b} / '#hex' → CSS 颜色

// 对话框（Promise）。键盘：Enter/Space 确认或继续，Escape 取消，↑↓ 移动，1–9 直选
SG.UI.say(text, speaker = null, speakerColor = '#c8382c')      → Promise<void>
SG.UI.choose(title, items, info = null, width = 640)           → Promise<index | -1>
SG.UI.chooseMany(title, items, max, info = null)               → Promise<number[] | null>（升序）
SG.UI.pickNumber(title, min, max, step, start, describe = null)→ Promise<number | -1>
SG.UI.confirm(text, yes = '是', no = '否')                       → Promise<boolean>
SG.UI.toast(html, seconds = 2.2)
SG.UI.banner(text, sub = null, seconds = 2.4)
SG.UI.anyModal()                                               → boolean

// 世界坐标标签
SG.UI.follow(el, () => worldVec3, { offsetY = 0, hideBeyond = 9999, align = 'center' })
    // → { el, alpha, remove(), update() }；align: 'center' | 'bottom' | 'top' | 'none'（左上角对准）
    // offsetY 为参考像素、向上为正；alpha 可随时设置（0 = 隐藏）
SG.UI.updateFollowers(camera)                  // 主循环每帧调用一次
SG.UI.floatText(worldVec3, text, color, sizePx)// 上浮淡出 1.2 秒
```

文字参数都是 **HTML**：Unity 富文本 `<color=#x>` → `<span style="color:#x">`，
`<b>` 照用，`<size=N>` → `<small>` 或 `<span style="font-size:…">`。
`say` 的文字保留换行（`\n`）。

## 3. 通用部件

| 类名 | 说明 |
|---|---|
| `.sg-panel` | 漆黑毛玻璃面板，金色细边、内金线、上缘高光 |
| `.sg-btn` | 按钮；`.primary` 朱红主按钮，`.small`，`.ghost`，`.is-on`（切换按钮开启），`.sg-close`（× 关闭） |
| `.sg-kai` | 楷体 |
| `.sg-gold` | 金色渐变字（内部带 `style="color:…"` 的片段保留自身颜色） |
| `.sg-muted` `.sg-good` `.sg-bad` `.sg-goldc` | 灰 / 绿 / 红 / 金色文字 |
| `.sg-side0` `.sg-side1` | 攻方蓝 `#73bfff` / 守方红 `#ff806b` |
| `.sg-dot` | 势力色块 `<span class="sg-dot" style="color:#2f9a55">■</span>` |
| `.sg-tokens` | 令牌 / 行动力圆点 `●●○` |
| `.sg-chip` | 数据小块 `<span class="sg-chip"><i>金</i><b>1200</b></span>` |
| `.sg-tag .sg-tag-ruler / .sg-tag-gov` | “君” “守” 小方印 |
| `.sg-medal` | 圆形徽章（用 `SG.UI.medal` 生成；CSS 变量 `--c` `--size`） |
| `.sg-bar > .sg-bar-fill` | 进度条（CSS 变量 `--c` 颜色、`--v` 0..1） |
| `.sg-hidden` | `display:none` |
| `.sg-num` | 等宽数字 |

## 4. 战略画面

```html
<div class="sg-screen sg-strategy-hud">                <!-- SG.UI.screen('sg-strategy-hud') -->
  <div class="sg-panel sg-topbar">
    <div class="sg-topbar-left"><span class="sg-dot" style="color:#2f9a55">■</span>刘备军</div>
    <div class="sg-topbar-center">                       <!-- 换行友好的 flex 行 -->
      <span class="sg-chip"><b>190</b><i>年</i><b>3</b><i>月</i></span>
      <span class="sg-chip"><i>令牌</i><span class="sg-tokens">●●○</span></span>
      <span class="sg-chip"><i>金</i><b>1860</b></span> …
    </div>
    <div class="sg-topbar-actions"> 势力 记录 音乐 <button class="sg-btn primary">结束本月</button></div>
  </div>

  <div class="sg-panel sg-citypanel">                   <!-- 右侧；手机横屏时宽约 26rem -->
    <div class="sg-citypanel-head">
      <div class="sg-citypanel-title">下邳<small><span style="color:#2f9a55">刘备</span></small></div>
      <button class="sg-btn sg-close">×</button>
    </div>
    <div class="sg-citypanel-body">                     <!-- 短屏时整体滚动 -->
      <div class="sg-citystats">太守 <b>刘备</b>　人口 560,000\n土地 <b>500</b> …</div>   <!-- 保留 \n -->
      <!-- 或者网格：<div class="sg-statgrid"><div class="sg-stat"><span>金</span><b>900</b></div>…</div>
           .sg-stat.wide 占两格，.sg-stat.full 占整行 -->
      <div class="sg-genlist">                          <!-- 可滚动（iOS 惯性） -->
        <div class="sg-genrow">
          <div class="sg-genrow-name"><span class="sg-tag sg-tag-ruler">君</span><b>刘备</b><span class="sg-moved">已行动</span></div>
          <div class="sg-genrow-stats">武79 智75 政78\n兵5000 训70 忠100</div>   <!-- 两行，保留 \n -->
        </div>
      </div>
    </div>
    <div class="sg-cmdgrid"> 12 个 .sg-btn（出征加 .primary） </div>   <!-- 3 列；手机横屏 6 列 -->
  </div>
</div>
```

城名标签（labels 层，`SG.UI.follow(el, () => cv.labelPos, { hideBeyond: 170 })`，默认居中对准）：

```html
<div class="sg-citylabel is-mine is-selected"><span class="sg-dot" style="color:#2f9a55">●</span>下邳<small>14200</small></div>
```

## 5. 战斗画面

```html
<div class="sg-screen sg-battle-hud">
  <div class="sg-panel sg-battle-top">                  <!-- 顶部居中，flex 换行；.sg-sep 为分隔符 -->
    <span>第 <b>3</b>/30 日　<span class="sg-side0">攻方</span>行动　行动力 <span class="sg-tokens">●●○</span></span>
    <span class="sg-sep">｜</span>
    <span><span class="sg-side0">攻</span> <b>12400</b> 粮2600　<span class="sg-side1">守</span> <b>9800</b> 粮4200</span>
  </div>
  <div class="sg-panel sg-battle-card">                 <!-- 左下 -->
    <div><span class="sg-card-name">关羽</span><span class="sg-card-sub">攻方·主将</span></div>
    <div class="sg-bar" style="--c:#73bfff;--v:.8"><i class="sg-bar-fill"></i></div>
    <div>兵力 <b>4800</b>　士气 82 …</div>
  </div>
  <div class="sg-battle-actions">                        <!-- 右下 -->
    <button class="sg-btn">委任作战</button><button class="sg-btn primary">结束回合</button>
  </div>
</div>
```

部队头顶信息（若不用 battle-view.js 自带的 `.sg-uinfo`）：

```html
<div class="sg-unitinfo is-commander" style="--c:#73bfff">   <!-- .is-acted 半透明 -->
  <i class="sg-stripe"></i>
  <div class="sg-unitinfo-name"><span class="sg-star">★</span>关羽<span class="sg-unitinfo-troops">4800</span></div>
  <div class="sg-bar" style="--c:#73bfff;--v:.8"><i class="sg-bar-fill"></i></div>
</div>
<!-- SG.UI.follow(el, () => pos, { hideBeyond: 90, align: 'bottom' }) -->
```

单挑（`const m = SG.UI.openModal('sg-duel', { width: 980, height: 420 })`，内容放进 `m.panel`）：

```html
<div class="sg-duel-title">单　挑</div>
<div class="sg-duel-arena">                             <!-- 三列：左 · VS · 右 -->
  <div class="sg-duel-side"> SG.UI.medal(…, 130) <div class="sg-duel-name">关羽<small>武力 98</small></div> SG.UI.bar(1, c) </div>
  <div class="sg-duel-vs">VS</div>
  <div class="sg-duel-side"> … </div>
</div>
<div class="sg-duel-log">第 1 合　关羽一击，造成 12 伤害</div>
```

每合：`SG.UI.pulse(vs, 'is-hit')`（VS 放大回弹），`SG.UI.pulse(sideEl, 'is-struck')`（被击中一侧抖动）。

## 6. 标题画面

```html
<div class="sg-title">                                   <!-- 放在 screens 层；自带全屏暗角，拦截指针 -->
  <div class="sg-title-hero">
    <div class="sg-title-seal">霸</div>
    <div class="sg-title-sub0">三国志 II</div>
    <div class="sg-title-main">霸王的大陆</div>          <!-- 金色渐变 + 流光 -->
    <div class="sg-title-sub">群雄逐鹿 · 一统天下</div>
  </div>
  <div class="sg-title-menu"> 新的征程(.primary) 继续征程 操作说明 </div>
  <div class="sg-title-foot">操作提示</div>
</div>
```

宽屏：上下排列，菜单在底部；手机横屏：左标题、右菜单。

## 7. 对话框内部结构（由 SG.UI 生成，仅供覆盖样式参考）

`.sg-modal`（遮罩；`.sg-say-modal` 为底部对话）› `.sg-panel.sg-dialog`（`.sg-choose` `.sg-choosemany`
`.sg-number` `.sg-confirm` `.sg-say` `.sg-duel`）› `.sg-dlg-head` › `.sg-dlg-title` + `.sg-close`；
`.sg-dlg-info`；`.sg-list` › `.sg-item`（`.is-on` 多选已选）› `.sg-check` `.sg-item-main`
（`.sg-item-label` `.sg-item-desc`）`.sg-item-right`；`.sg-dlg-foot` › `.sg-count` `.sg-spacer` 按钮。
数值：`.sg-number-row`（− `.sg-number-big` ＋）`.sg-number-desc` `input.sg-range` `.sg-range-ends`。
对话：`.sg-say-speaker`（徽章 + `.sg-say-name`）`.sg-say-text` `.sg-say-hint`（▼）。
提示：`.sg-toast-stack > .sg-toast`；横幅：`.sg-banner > .sg-banner-text + .sg-banner-sub`。
飘字：`.sg-float > .sg-float-text`。
