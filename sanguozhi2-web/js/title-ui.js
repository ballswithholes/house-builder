'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 官制画面（TITLES.md §9；UI-V4 §11 的版式）
   第一版界面：用现有的 SG.UI 部件（openModal / .sg-dialog / .sg-btn / choose / confirm / toast）与代码生成的占位印信
   （按品阶的印 / 绶 / 垫 + 家族色 + 官名首字，SVG），不需要新美术。美术推广做出 SG.TitleArt 之后：
   icon 改走 SG.TitleArt.icon(postId, culture, size)，仪式改走 SG.TitleArt.ceremony({ gen, ruler, office, culture, size })。
   规则一律在 js/titles.js（SG.Titles）；本文件只读规则、调用 grant / revoke / demote / setName / fillByMerit。

   SG.TitleUI.open({ f?, tier?, postId?, gen?, city? }) → Promise（关闭时 resolve）
       官制总览（顶梁「官制」、任命 → 官爵、月初提示）；postId（与 tier）给出时直接打开该官位的候选名单；
       gen 给出时打开他的候选官位；city 给出时候选名单只列这座城的武将（任命 → 官爵）
   SG.TitleUI.grant(gen, postId?) → Promise<授官结果 | null>   人物菜单的「授官 / 晋升」：选官位 → 确认 → 仪式
   SG.TitleUI.confirm(f, gen, postId) → Promise<结果 | null>   确认卡（诏书）：效果与变化、礼金与付款城、忠诚、越级不平的警告
   SG.TitleUI.manage(gen) → Promise                         已任官者：晋升 · 改任 · 换名号 · 贬官 · 罢免
   SG.TitleUI.tab(gen) → HTMLElement                         人物菜单「官爵」页（UI-V4 §9.3；本规格出内容）：现任、阶梯与进度、按钮、履历；
                                                             他国 / 在野武将只读（官职、势力、拉拢抗性）
   SG.TitleUI.ceremony(res | { gen, office, size }) → Promise 授官仪式（small 1.8 s · medium 3.0 s · grand 4.5 s；点按 / 空格 / Esc 跳过；
                                                             prefers-reduced-motion 或设置「仪式动画：简」（SG.Settings 'ui.ceremony' = 'short'）→ 0.6 s）
   SG.TitleUI.rankUp(f, from, to) → Promise                  晋位仪式（R2–R3 medium，R4–R5 grand）
   SG.TitleUI.flush() → Promise                              月初：播放 SG.Titles.takeEvents() 里玩家的晋位（strategy-screen 在月末新闻之后调用）
   SG.TitleUI.badge(f?) → n                                  「官制」按钮的角标（= SG.Titles.pending）
   SG.TitleUI.icon(postId, f, size) → HTML                   印信（占位；SG.TitleUI.iconHook = (postId, culture, size) => html 可替换）
   SG.TitleUI.officeTag(gen) → HTML                          城池面板 / 武将列表的官名小标签（家族色）；无官职时 ''
   SG.TitleUI.adminLine(city) → HTML                         「主政：荀彧（尚书令）」（君主坐镇、由文官代理时）
   SG.TitleUI.cardLine(unit, model?) → HTML                  战场情报卡的官职一行：印信 + 官名 + 品阶 + 至多两项战斗效果（+ 主将的「统率+3%」）；无官职时 ''
   SG.TitleUI.current → 打开中的画面状态（测试用：{ f, el, fam, sheet, edict }）；SG.TitleUI.close()
   载入：ui.js、titles.js 之后，strategy-screen.js 之前（INTEGRATION §11）。
   类名（本文件注入的样式，前缀 .sg-office-）：.sg-office（对话框）、-head、-rank、-board、-row、-grade、-cell（.f-war / .f-intel / .f-pol）、
     -post、-seat（.is-free / .is-full / .is-locked / .is-suspended / .is-captive / .is-filled / .has-cand）、-sheet、-cand、-edict、-fx、-warn、
     -tabs、-cer（仪式）、-tab（人物菜单页）、-ins（印信）
   ========================================================================== */
(function () {
  const SG = window.SG;
  const T = () => SG.Titles, UI = () => SG.UI, G = () => SG.G;
  const esc = s => (SG.esc ? SG.esc(s) : String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])));
  const FAM_ZH = { war: '武官', intel: '谋臣', pol: '文官' };
  const FAM_GLYPH = { war: '武', intel: '谋', pol: '文' };
  const STAT_ZH = { war: '武力', intel: '智力', pol: '政治' };
  const COLORS = { war: '#c8382c', intel: '#2f6db5', pol: '#2f9a55' };
  const famColor = f => { const d = SG.TitlesData; return (d && d.families[f] && d.families[f].color) || COLORS[f] || '#888'; };
  const reduced = () => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  const shortCeremony = () => {
    if (reduced()) return true;
    const S = SG.Settings;
    try { if (S && typeof S.has === 'function' && S.has('ui.ceremony')) return S.get('ui.ceremony') === 'short'; } catch (e) { /* 无设置 */ }
    return false;
  };
  const isPhone = () => typeof innerHeight === 'number' && innerHeight <= 540;
  // 现有音效：click hit fire rock horn win lose coin duel magic march（js/audio.js；授官的专属音效由音频工作流登记）
  const sfx = (name, vol) => { try { if (SG.Sfx && name === 'click' && SG.Sfx.click) SG.Sfx.click(); else if (SG.Sfx && SG.Sfx.play) SG.Sfx.play(name, vol || 0.6); } catch (e) { /* 无音频 */ } };
  // DOMESTIC-FX 的授官声（TITLES §9.1）：按下 cmd-press-title；仪式 sting:title，档次 good = 五品、great = 三品 / 一品（其余 normal；
  // 晋位：R2–R3 good、R4–R5 great）。短乐没有登记时（SG.Sfx.cmdSting 返回 null）退回现有的音效
  const stingTier = t => (t >= 4 ? 'great' : t === 3 ? 'good' : 'normal');
  const sting = (tier, fallback) => { let r = null; try { if (SG.Sfx && typeof SG.Sfx.cmdSting === 'function') r = SG.Sfx.cmdSting('title', tier); } catch (e) { r = null; } if (!r) sfx(fallback, 0.5); };
  const voice = (gen, key, vars) => { try { if (SG.Voice && typeof SG.Voice.say === 'function') SG.Voice.say(gen, key, vars); } catch (e) { /* 无人声 */ } };
  const portrait = (gen, size) => {
    if (SG.Portrait && typeof SG.Portrait.el === 'function') { try { return SG.Portrait.el(gen, { size }); } catch (e) { /* 无头像 */ } }
    const d = document.createElement('span');
    d.className = 'sg-office-noface'; d.style.width = d.style.height = size + 'px'; d.textContent = gen ? gen.name.slice(0, 1) : '';
    return d;
  };
  // 效果一览的文字：钩子没接上的项后面注「（未生效）」
  const fxText = postId => T().effects(postId).map(e => e.text + (e.live === false ? '（未生效）' : '')).join('，');
  const numZh = n => ['零', '一', '二', '三', '四', '五', '六', '七', '八', '九', '十'][n] || String(n);
  const pctTxt = v => (Math.round(v * 1000) / 10).toString();

  // ------------------------------------------------------------------ 样式（只注入一次）--
  const CSS = `
.sg-dialog.sg-office { --w: 76rem; width: min(var(--w), 100%); height: min(44rem, 100%); max-height: 100%; padding: .7rem .9rem .8rem; gap: .45rem; position: relative; }
.sg-office-head { display: flex; align-items: center; gap: .7rem; padding-bottom: .35rem; border-bottom: 1px solid var(--sg-gold-dim, rgba(243,201,105,.35)); min-width: 0; }
.sg-office-head .sg-portrait { flex: none; border-radius: 50%; box-shadow: 0 0 0 2px var(--rk, #8a6a3a), 0 0 0 4px rgba(0,0,0,.35); }
.sg-office-title { font-family: var(--sg-font-title); font-size: 1.75rem; letter-spacing: .3em; color: var(--sg-gold, #f3c969); white-space: nowrap; }
.sg-office-rank { display: flex; flex-direction: column; gap: .2rem; min-width: 0; flex: 1; font-size: .9rem; color: var(--sg-muted, #ada392); }
.sg-office-rank b { font-family: var(--sg-font-title); font-weight: normal; font-size: 1.12rem; color: var(--sg-gold-hi, #fff1c8); }
.sg-office-rank .sg-office-bar { position: relative; height: .5rem; border-radius: .25rem; background: rgba(255,240,210,.12); overflow: hidden; max-width: 26rem; }
.sg-office-rank .sg-office-bar i { position: absolute; inset: 0 auto 0 0; width: calc(var(--v, 0) * 100%); background: linear-gradient(90deg, #b98431, #f3c969); border-radius: inherit; transition: width .5s var(--sg-ease, ease); }
.sg-office-rank .sg-office-status { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.sg-office-rank .sg-office-rs { display: none; white-space: nowrap; }
.sg-office-fx span.is-dormant { color: #6b5d4a; }
.sg-office-fx span.is-dormant em { font-style: normal; font-size: .75em; margin-left: .25em; padding: 0 .25em; border: 1px solid #8a7d6a; border-radius: .2rem; }
.sg-office-head .sg-btn { min-height: 44px; }
.sg-office-tabs { display: none; gap: .3rem; }
.sg-office-tabs .sg-btn { min-height: 44px; min-width: 4.4rem; }
.sg-office-tabs .sg-btn.is-on { box-shadow: 0 0 0 2px var(--fc) inset, 0 0 12px color-mix(in srgb, var(--fc) 60%, transparent); color: #fff; }
.sg-office-scroll { flex: 1; min-height: 0; overflow: auto; -webkit-overflow-scrolling: touch; border-radius: .5rem; }
.sg-office-board { min-height: 100%; display: grid; grid-template-columns: 7rem repeat(3, minmax(0, 1fr)); grid-template-rows: auto; grid-auto-rows: auto; align-content: start;
  border-radius: .5rem; background: #efe2c4; color: #2b2622; box-shadow: inset 0 0 0 2px #241a21, inset 0 0 22px rgba(120,90,40,.25); }
.sg-office-fam { position: sticky; top: 0; z-index: 2; display: flex; align-items: center; justify-content: center; gap: .4rem; min-height: 2.4rem; font-family: var(--sg-font-title);
  font-size: 1.15rem; letter-spacing: .3em; color: #fff6e6; background: var(--fc); border-left: 2px solid #241a21; border-bottom: 2px solid #241a21; }
.sg-office-corner { position: sticky; top: 0; z-index: 2; background: #3a2a2e; border-bottom: 2px solid #241a21; }
.sg-office-grade { display: flex; flex-direction: column; justify-content: center; gap: .1rem; padding: .3rem .6rem; min-height: 4.6rem; border-bottom: 1.5px dashed rgba(36,26,33,.3); background: rgba(36,26,33,.06); }
.sg-office-grade b { font-family: var(--sg-font-title); font-weight: normal; font-size: 1.35rem; line-height: 1; }
.sg-office-grade .sg-office-cap { font-variant-numeric: tabular-nums; font-weight: 700; font-size: .85rem; color: #4a3b33; }
.sg-office-grade .sg-office-req { font-size: .72rem; color: #6b5d4a; }
.sg-office-grade.is-locked b { color: #8a7d6a; }
.sg-office-cell { display: flex; flex-wrap: wrap; align-content: center; gap: .3rem .7rem; padding: .3rem .5rem; min-height: 4.6rem; border-left: 2px solid #241a21; border-bottom: 1.5px dashed rgba(36,26,33,.3); min-width: 0;
  background: linear-gradient(90deg, color-mix(in srgb, var(--fc) 13%, transparent), color-mix(in srgb, var(--fc) 4%, transparent)); }
.sg-office-post { display: flex; flex-direction: column; gap: .2rem; min-width: 0; }
.sg-office-pn { display: flex; align-items: center; gap: .3rem; font-family: var(--sg-font-title); font-size: .95rem; color: #3a2d26; white-space: nowrap; }
.sg-office-pn small { font-family: var(--sg-font-body); font-size: .68rem; color: #6b5d4a; }
.sg-office-chips { display: flex; flex-wrap: wrap; gap: .3rem; }
.sg-office-seat { position: relative; width: 3.4rem; min-height: 3.7rem; border-radius: .45rem; display: flex; flex-direction: column; align-items: center; justify-content: flex-start; gap: .05rem; padding: .2rem .1rem;
  background: rgba(255,255,255,.55); box-shadow: inset 0 0 0 1.5px #b89a68, 0 2px 0 rgba(36,26,33,.35); font-family: var(--sg-font-title); font-size: .72rem; line-height: 1.1; color: #2b2622;
  cursor: pointer; border: none; font-weight: normal; text-align: center; transition: transform .12s, box-shadow .12s; }
.sg-office-seat:hover { transform: translateY(-1px); }
.sg-office-seat:focus-visible { outline: 2px solid #b98431; outline-offset: 1px; }
.sg-office-seat .sg-portrait { flex: none; border-radius: .3rem; box-shadow: 0 0 0 1px #241a21; }
.sg-office-seat .sg-office-sname { max-width: 3.3rem; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.sg-office-seat.is-free { justify-content: center; font-size: 1.5rem; color: #c8382c; border: 2px dashed #241a21; background: radial-gradient(closest-side, rgba(255,210,140,.75), rgba(255,210,140,.15)); }
.sg-office-seat.is-free.has-cand { animation: sg-office-breathe 2.2s ease-in-out infinite; }
.sg-office-seat.is-free:not(.has-cand) { color: #8a7d6a; font-size: 1.1rem; }
@keyframes sg-office-breathe { 50% { box-shadow: inset 0 0 0 2px #e0a24a, 0 0 14px rgba(255,196,110,.85); } }
.sg-office-seat.is-full { background: rgba(36,26,33,.08); box-shadow: inset 0 0 0 1px rgba(36,26,33,.2); color: #6b5d4a; justify-content: center; font-size: .66rem; cursor: default; }
.sg-office-seat.is-locked { background: repeating-linear-gradient(135deg, rgba(36,26,33,.09) 0 5px, transparent 5px 10px); box-shadow: inset 0 0 0 1px rgba(36,26,33,.25);
  justify-content: center; color: #6b5d4a; font-size: .62rem; cursor: default; }
.sg-office-seat.is-locked::before { content: '锁'; font-size: .8rem; color: #8a5a3a; border: 1px solid #8a5a3a; border-radius: .2rem; padding: 0 .15rem; margin-bottom: .1rem; }
.sg-office-seat.is-suspended::after { content: '虚衔'; position: absolute; inset: 0; display: grid; place-items: center; font-size: .78rem; color: #3a2d26; border-radius: inherit;
  background: repeating-linear-gradient(135deg, rgba(243,231,204,.82) 0 4px, rgba(184,154,104,.55) 4px 8px); }
.sg-office-seat.is-captive::after { content: '被俘'; position: absolute; right: -.2rem; top: -.2rem; font-size: .6rem; color: #fff; background: #7a2a22; border-radius: .2rem; padding: 0 .15rem; }
.sg-office-seat.is-new { animation: sg-office-settle .45s var(--sg-ease-pop, ease) both; }
@keyframes sg-office-settle { 0% { transform: scale(1.35); filter: brightness(1.6); } 60% { transform: scale(.94); } 100% { transform: none; filter: none; } }
.sg-office-ins { display: inline-block; flex: none; line-height: 0; }
.sg-office-ins svg { display: block; }
.sg-office-noface { display: inline-flex; align-items: center; justify-content: center; border-radius: .3rem; background: #6b5d4a; color: #fff; font-family: var(--sg-font-title); }
/* 候选名单（左侧纸条；手机为底部抽屉） */
.sg-office-layer { position: absolute; inset: 0; z-index: 5; display: flex; align-items: stretch; background: rgba(10,8,10,.45); border-radius: inherit; }
.sg-office-sheet { width: min(24rem, 92%); margin: .5rem; display: flex; flex-direction: column; gap: .35rem; padding: .6rem .6rem .5rem; border-radius: .6rem; background: #efe2c4; color: #2b2622;
  box-shadow: 0 10px 30px rgba(0,0,0,.5), inset 0 0 0 2px #241a21; animation: sg-office-slide .26s var(--sg-ease, ease) both; min-height: 0; }
@keyframes sg-office-slide { from { transform: translateX(-24px); opacity: 0; } to { transform: none; opacity: 1; } }
.sg-office-sheet h3 { margin: 0; display: flex; align-items: center; gap: .5rem; font-family: var(--sg-font-title); font-weight: normal; font-size: 1.3rem; }
.sg-office-sheet h3 small { font-family: var(--sg-font-body); font-size: .78rem; color: #6b5d4a; }
.sg-office-sheet h3 .sg-spacer { flex: 1; }
.sg-office-list { flex: 1; min-height: 0; overflow-y: auto; -webkit-overflow-scrolling: touch; display: flex; flex-direction: column; gap: .25rem; }
.sg-office-cand { display: flex; align-items: center; gap: .5rem; min-height: 3.4rem; padding: .2rem .4rem; border-radius: .4rem; background: rgba(255,255,255,.4); border: none; color: inherit; font: inherit; text-align: left; cursor: pointer; }
.sg-office-cand:hover { background: rgba(255,236,190,.95); }
.sg-office-cand.is-no { opacity: .6; cursor: default; }
.sg-office-cand .sg-portrait { flex: none; border-radius: .3rem; }
.sg-office-cand .sg-office-cmain { flex: 1; min-width: 0; font-size: .8rem; color: #4a3b33; line-height: 1.3; }
.sg-office-cand .sg-office-cmain b { display: block; font-family: var(--sg-font-title); font-weight: normal; font-size: 1.05rem; color: #2b2622; }
.sg-office-cand .sg-office-st { flex: none; font-family: var(--sg-font-title); font-size: .78rem; padding: .05rem .35rem; border-radius: .25rem; border: 1.5px solid #241a21; white-space: nowrap; max-width: 9rem; overflow: hidden; text-overflow: ellipsis; }
.sg-office-cand .sg-office-st.ok { color: #fff; background: #2f9a55; border-color: #1d6b3a; }
/* 诏书（确认卡） */
.sg-office-edict { margin: auto; width: min(31rem, 94%); max-height: calc(100% - 1rem); overflow-y: auto; display: flex; flex-direction: column; align-items: center; gap: .4rem; padding: 1.1rem 1.3rem .9rem;
  border-radius: .3rem; background: linear-gradient(#f6ecd4, #ead9b4); color: #2b2622; box-shadow: 0 12px 36px rgba(0,0,0,.55), inset 0 0 0 2px #8a6a3a, inset 0 0 0 5px #f6ecd4, inset 0 0 0 6px #b89a68;
  position: relative; animation: sg-office-unroll .38s var(--sg-ease, ease) both; }
@keyframes sg-office-unroll { from { clip-path: inset(0 0 100% 0); } to { clip-path: inset(0 0 0 0); } }
.sg-office-edict .sg-office-pname { font-family: var(--sg-font-title); font-size: 2.3rem; letter-spacing: .18em; line-height: 1.05; color: var(--fcd, #8e2419); text-align: center; }
.sg-office-edict .sg-office-orig { font-size: .82rem; color: #6b5d4a; font-style: italic; text-align: center; }
.sg-office-edict .sg-office-who { display: flex; align-items: center; gap: .5rem; font-size: .9rem; }
.sg-office-edict .sg-office-who b { font-family: var(--sg-font-title); font-weight: normal; font-size: 1.2rem; }
.sg-office-names { display: flex; flex-wrap: wrap; justify-content: center; gap: .3rem; }
.sg-office-names button { font-family: var(--sg-font-title); font-size: .92rem; min-height: 36px; padding: .1rem .55rem; border: 1.5px solid #241a21; border-radius: 1rem; background: transparent; color: #2b2622; cursor: pointer; }
.sg-office-names button.is-on { background: var(--fc, #c8382c); color: #fff6e6; }
.sg-office-names button:disabled { opacity: .35; cursor: default; }
.sg-office-fx { width: 100%; display: grid; grid-template-columns: 1fr 1fr; gap: .15rem 1rem; font-size: .88rem; border-top: 1px solid #b89a68; padding-top: .35rem; }
.sg-office-fx .up { color: #1d6b3a; } .sg-office-fx .down { color: #9a2b1f; } .sg-office-fx small { color: #6b5d4a; }
.sg-office-cost { width: 100%; display: flex; flex-wrap: wrap; gap: .2rem 1rem; justify-content: space-between; font-size: .9rem; padding-right: 5rem; box-sizing: border-box; }
.sg-office-warn { width: 100%; display: flex; flex-wrap: wrap; align-items: center; gap: .3rem; font-size: .8rem; color: #9a2b1f; }
.sg-office-warn .sg-portrait { border-radius: .25rem; }
.sg-office-btns { display: flex; gap: .6rem; margin-top: .2rem; }
.sg-office-btns .sg-btn { min-height: 44px; min-width: 6rem; }
.sg-office-stamp { position: absolute; right: 1.1rem; bottom: 3.6rem; width: 3.6rem; height: 3.6rem; display: grid; place-items: center; font-family: var(--sg-font-title); font-size: 1.05rem; line-height: 1.05;
  color: #b8281d; border: 3px solid #b8281d; border-radius: .3rem; transform: rotate(-8deg); opacity: .78; text-align: center; pointer-events: none; }
/* 纸面上的粗体与次要按钮（#ui b 是浅色的）*/
#ui .sg-office-board b, #ui .sg-office-sheet b, #ui .sg-office-edict b { color: inherit; }
#ui .sg-office-edict .sg-office-cost b { color: #8e2419; }
#ui .sg-office-edict .sg-btn.ghost { color: #3a2d26; background: rgba(255,255,255,.45); border-color: #8a6a3a; }
/* 仪式 */
.sg-office-cer { position: fixed; inset: 0; z-index: 1600; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 1rem; pointer-events: auto; overflow: hidden;
  color: #2b2622; background: rgba(20,12,10,.35); animation: sg-office-fade .25s ease-out both; cursor: pointer; }
.sg-office-cer.is-medium { background: radial-gradient(ellipse at 50% 45%, rgba(110,62,34,.82), rgba(28,16,14,.95)); }
.sg-office-cer.is-grand { background: radial-gradient(ellipse at 50% 30%, rgba(255,190,110,.25), transparent 45%),
  linear-gradient(90deg, #2a1712 0 8%, #5a2a1e 8% 10%, #2a1712 10% 90%, #5a2a1e 90% 92%, #2a1712 92%); color: #fff2dc; }
.sg-office-cer.is-out { animation: sg-office-fadeout .3s ease-in both; }
@keyframes sg-office-fade { from { opacity: 0; } to { opacity: 1; } }
@keyframes sg-office-fadeout { to { opacity: 0; } }
.sg-office-cer .sg-office-strip { position: relative; display: flex; align-items: center; gap: 1.2rem; padding: .9rem 2.4rem; background: linear-gradient(#f6ecd4, #e8d4a8);
  box-shadow: 0 10px 30px rgba(0,0,0,.5), inset 0 0 0 2px #8a6a3a; animation: sg-office-stripin .45s var(--sg-ease, ease) both; }
@keyframes sg-office-stripin { from { transform: translateX(-60vw); } to { transform: none; } }
.sg-office-cer .sg-office-cname { font-family: var(--sg-font-title); font-size: 2.6rem; letter-spacing: .2em; color: var(--fcd, #8e2419); clip-path: inset(0 100% 0 0); animation: sg-office-write .7s .35s steps(14) forwards; white-space: nowrap; }
.sg-office-cer.is-grand .sg-office-strip { background: linear-gradient(#4a2418, #2e150f); box-shadow: 0 10px 30px rgba(0,0,0,.6), inset 0 0 0 2px #e4b64a, inset 0 0 0 5px #4a2418, inset 0 0 0 6px #b98431; }
.sg-office-cer.is-grand .sg-office-cname { font-size: 3.4rem; color: #ffe2a0; text-shadow: 0 0 1px #6b4a12, 0 0 14px rgba(255,200,90,.6); }
@keyframes sg-office-write { to { clip-path: inset(0 0 0 0); } }
.sg-office-cer .sg-office-cseal { width: 4.4rem; height: 4.4rem; display: grid; place-items: center; font-family: var(--sg-font-title); font-size: 1.15rem; line-height: 1.05; color: #fff6e6; text-align: center;
  background: radial-gradient(circle at 40% 35%, #d9583f, #8e2419); border-radius: .4rem; box-shadow: 0 0 0 3px rgba(142,36,25,.35); opacity: 0; transform: scale(1.8) rotate(-8deg);
  animation: sg-office-stamp .22s var(--d, 1.1s) cubic-bezier(.3,1.6,.5,1) forwards; }
@keyframes sg-office-stamp { 0% { opacity: 0; transform: scale(1.8) rotate(-8deg); } 70% { opacity: 1; transform: scale(.92, 1.06) rotate(-8deg); } 100% { opacity: 1; transform: scale(1) rotate(-8deg); } }
.sg-office-cer .sg-office-csub { font-size: 1.05rem; color: inherit; opacity: 0; animation: sg-office-fade .4s var(--d2, .9s) forwards; text-align: center; }
.sg-office-cer.is-medium .sg-office-csub, .sg-office-cer.is-grand .sg-office-csub { color: #fff2dc; }
.sg-office-cer .sg-office-rise { animation: sg-office-rise .7s .25s var(--sg-ease, ease) both; }
@keyframes sg-office-rise { from { transform: translateY(40px) scale(.7); opacity: 0; } to { transform: none; opacity: 1; } }
.sg-office-cer .sg-office-fleck { position: absolute; top: -2vh; width: .5rem; height: .35rem; border-radius: 40%; background: #f3c969; opacity: .85; animation: sg-office-fall var(--t, 3s) var(--dl, 0s) linear forwards; }
.sg-office-cer.is-grand .sg-office-fleck { background: #ffb3a8; }
@keyframes sg-office-fall { to { transform: translate(var(--dx, 20px), 105vh) rotate(540deg); } }
.sg-office-cer .sg-office-lanterns { position: absolute; top: 4vh; left: 10%; right: 10%; display: flex; justify-content: space-between; pointer-events: none; }
.sg-office-cer .sg-office-lanterns i { width: 2.2rem; height: 3rem; border-radius: 45%; background: radial-gradient(circle at 50% 45%, #ffd58a, #d9583f 70%); box-shadow: 0 0 30px 10px rgba(255,170,80,.35); }
.sg-office-cer .sg-office-pair { display: flex; align-items: flex-end; gap: 2.4rem; }
.sg-office-cer .sg-office-pair .sg-portrait { border-radius: .5rem; box-shadow: 0 0 0 2px #f3c969, 0 8px 22px rgba(0,0,0,.5); }
.sg-office-cer .sg-office-skip { position: absolute; bottom: 1.2rem; right: 1.6rem; font-size: .8rem; opacity: .7; color: #fff2dc; }
.sg-office-cer.is-small .sg-office-skip { color: #f3e8d2; }
/* 人物菜单「官爵」页 */
.sg-office-tab { display: flex; flex-direction: column; gap: .6rem; color: var(--sg-text, #f5eddb); font-size: .95rem; }
.sg-office-card { display: flex; gap: .7rem; align-items: center; padding: .55rem .7rem; border-radius: .6rem; background: rgba(255,240,210,.07); border: 1px solid color-mix(in srgb, var(--fc, #f3c969) 55%, transparent); }
.sg-office-card .sg-office-cn { font-family: var(--sg-font-title); font-size: 1.45rem; color: var(--fcl, #f3c969); }
.sg-office-card .sg-office-cs { font-size: .85rem; color: var(--sg-muted, #ada392); line-height: 1.45; }
.sg-office-ladder { display: flex; flex-direction: column; gap: .25rem; }
.sg-office-rung { display: grid; grid-template-columns: 3.4rem 1fr auto; align-items: center; gap: .5rem; min-height: 44px; padding: .2rem .45rem; border-radius: .45rem; background: rgba(0,0,0,.2); }
.sg-office-rung.is-cur { box-shadow: inset 0 0 0 2px var(--fc, #f3c969); }
.sg-office-rung .sg-office-rg { font-family: var(--sg-font-title); font-size: 1.05rem; }
.sg-office-rung .sg-office-rr { font-size: .8rem; color: var(--sg-muted, #ada392); line-height: 1.4; }
.sg-office-rung .sg-office-rr .ok { color: var(--sg-good, #73d98c); } .sg-office-rung .sg-office-rr .no { color: var(--sg-bad, #ff7361); }
.sg-office-rung .sg-btn { min-height: 40px; }
.sg-office-lv { display: inline-flex; gap: 2px; margin: 0 .15em; vertical-align: -.05em; }
.sg-office-lv i { width: .5em; height: .82em; border: 1px solid currentColor; border-radius: 1px; box-sizing: border-box; opacity: .5; }
.sg-office-lv i.on { background: currentColor; opacity: 1; }
.sg-office-hist { font-size: .82rem; color: var(--sg-muted, #ada392); line-height: 1.5; }
.sg-office-acts { display: flex; flex-wrap: wrap; gap: .4rem; }
.sg-office-acts .sg-btn { min-height: 44px; }
/* 人物菜单（第四版纸页，character-screen 给本页加 .is-v4）：纸上的深色字，现任官名用家族的深色 */
.sg-office-tab.is-v4 { color: #2b2622; }
.sg-office-tab.is-v4 .sg-office-card { background: rgba(255,255,255,.4); }
.sg-office-tab.is-v4 .sg-office-card .sg-office-cn { color: var(--fcd, #8e2419); }
.sg-office-tab.is-v4 .sg-office-cs, .sg-office-tab.is-v4 .sg-office-hist, .sg-office-tab.is-v4 .sg-office-rung .sg-office-rr { color: #54473b; }
.sg-office-tab.is-v4 .sg-office-rung { background: rgba(36,26,33,.07); }
.sg-office-tab.is-v4 .sg-office-rung .sg-office-rr .ok { color: #1d6b3a; } .sg-office-tab.is-v4 .sg-office-rung .sg-office-rr .no { color: #9a2b1f; }
.sg-office-tag { display: inline-block; margin-left: .35rem; padding: 0 .3rem; border-radius: .25rem; font-family: var(--sg-font-title); font-size: .78em; line-height: 1.35; color: #fff6e6; background: var(--fc); vertical-align: middle; white-space: nowrap; }
.sg-office-tag.is-suspended { opacity: .6; text-decoration: line-through; }
.sg-card-office { display: flex; align-items: center; flex-wrap: wrap; gap: 0 .3rem; line-height: 1.5; }
.sg-card-office .sg-office-ins { width: 18px; height: 18px; flex: 0 0 auto; }
.sg-card-office .sg-office-tag { margin-left: 0; }
.sg-card-office-g { font-size: .82em; opacity: .75; }
.sg-card-office-fx { color: #9fe0a8; font-size: .9em; white-space: nowrap; }
.sg-card-office-off { color: #b9b0a4; font-size: .9em; }
@media (prefers-reduced-motion: reduce) {
  .sg-office-sheet, .sg-office-edict, .sg-office-seat.is-new, .sg-office-seat.is-free.has-cand, .sg-office-rank .sg-office-bar i { animation: none !important; transition: none !important; }
}
@media (max-height: 540px) {
  .sg-dialog.sg-office { height: 100%; width: 100%; padding: .4rem .55rem .5rem; gap: .3rem; }
  .sg-office-title { font-size: 1.3rem; letter-spacing: .15em; }
  .sg-office-head .sg-portrait { display: none; }
  .sg-office-rank .sg-office-bar { display: none; }
  .sg-office-rank b { font-size: 1rem; }
  /* 头一行：「公 7.4/10」，长说明与状态行收起（TITLES §9.2） */
  .sg-office-rank .sg-office-rl, .sg-office-rank .sg-office-status { display: none; }
  .sg-office-rank .sg-office-rs { display: inline; }
  .sg-office-head { flex-wrap: nowrap; gap: .45rem; }
  .sg-office-head .sg-close { min-width: 44px; min-height: 44px; }
  .sg-office-sheet .sg-close { min-width: 44px; min-height: 44px; }
  .sg-office-names button { min-height: 44px; padding: .1rem .7rem; }
  .sg-office-tabs { display: flex; }
  .sg-office-board { grid-template-columns: 4.8rem minmax(0, 1fr); grid-template-rows: none; grid-auto-rows: auto; }
  .sg-office-grade, .sg-office-cell { min-height: 3.9rem; }
  .sg-office-board .sg-office-fam, .sg-office-board .sg-office-corner { display: none; }
  .sg-office-board .sg-office-cell:not(.is-shown) { display: none; }
  .sg-office-grade { padding: .2rem .4rem; } .sg-office-grade b { font-size: 1.1rem; } .sg-office-grade .sg-office-req { display: none; }
  .sg-office-layer { align-items: flex-end; }
  .sg-office-sheet { width: 100%; max-height: 92%; margin: 0; border-radius: .6rem .6rem 0 0; }
  .sg-office-edict { padding: .6rem .8rem .5rem; gap: .25rem; width: min(40rem, 98%); }
  .sg-office-edict .sg-office-pname { font-size: 1.7rem; }
  .sg-office-fx { grid-template-columns: 1fr 1fr 1fr; font-size: .8rem; }
  .sg-office-stamp { display: none; }
  .sg-office-cost { padding-right: 0; }
}
`;
  let styled = false;
  function injectStyle() {
    if (styled || typeof document === 'undefined') return;
    styled = true;
    const st = document.createElement('style');
    st.id = 'sg-office-style';
    st.textContent = CSS;
    document.head.appendChild(st);
  }

  // ------------------------------------------------------------------ 印信（占位；按品阶的形）--
  const METAL = ['', '#b9773e', '#a8743f', '#c9ccd2', '#e4b64a', '#f0c75a'];
  const METAL_LO = ['', '#7a4a22', '#6b4522', '#8a8e96', '#9a7420', '#a87e1a'];
  function insigniaSvg(post, glyph, size) {
    const t = post.tier, fc = famColor(post.family), m = METAL[t], lo = METAL_LO[t];
    const s = size || 40;
    let body = '';
    if (t === 5) body += `<ellipse cx="32" cy="34" rx="30" ry="24" fill="url(#h${t})"/><ellipse cx="32" cy="50" rx="24" ry="8" fill="${fc}" stroke="#241a21" stroke-width="1.5"/>`;
    if (t >= 3) {
      const len = t >= 4 ? 22 : 14;
      body += `<path d="M26 40 q-6 ${len / 2} -4 ${len}" stroke="${fc}" stroke-width="5" fill="none" stroke-linecap="round"/><path d="M38 40 q6 ${len / 2} 4 ${len}" stroke="${fc}" stroke-width="5" fill="none" stroke-linecap="round"/>`;
      if (t >= 4) body += `<circle cx="22" cy="${40 + len + 2}" r="2.6" fill="${m}" stroke="#241a21"/><circle cx="42" cy="${40 + len + 2}" r="2.6" fill="${m}" stroke="#241a21"/>`;
    }
    if (t === 1) body += `<circle cx="32" cy="28" r="17" fill="${m}" stroke="${lo}" stroke-width="3"/><circle cx="32" cy="28" r="12" fill="none" stroke="#241a21" stroke-width="1" opacity=".5"/>`;
    else {
      const w = t >= 4 ? 30 : 26, x = 32 - w / 2, y = t === 5 ? 18 : 13;
      body += `<rect x="${x}" y="${y}" width="${w}" height="${w}" rx="4" fill="${m}" stroke="${lo}" stroke-width="3"/>`;
      // 印钮（龟钮 / 鼻钮）
      body += t === 5 ? `<ellipse cx="32" cy="${y - 2}" rx="8" ry="5" fill="${m}" stroke="${lo}" stroke-width="2"/>` : `<rect x="28" y="${y - 5}" width="8" height="6" rx="2" fill="${m}" stroke="${lo}" stroke-width="1.5"/>`;
    }
    const cy = t === 1 ? 33 : t === 5 ? 39 : t >= 4 ? 34 : 32;
    body += `<text x="32" y="${cy}" text-anchor="middle" font-size="${t === 1 ? 15 : 16}" font-family="STKaiti,KaiTi,serif" fill="#241a21">${esc(glyph)}</text>`;
    if (post.branch) body += `<circle cx="53" cy="12" r="8" fill="${fc}" stroke="#241a21" stroke-width="1.2"/><text x="53" y="16" text-anchor="middle" font-size="10" font-family="STKaiti,KaiTi,serif" fill="#fff">${esc((post.role || '').slice(-1))}</text>`;
    const defs = t === 5 ? `<defs><radialGradient id="h${t}"><stop offset="0" stop-color="#fff2b0" stop-opacity=".9"/><stop offset="1" stop-color="#fff2b0" stop-opacity="0"/></radialGradient></defs>` : '';
    return `<svg width="${s}" height="${s}" viewBox="0 0 64 64" aria-hidden="true">${defs}${body}</svg>`;
  }
  function icon(postId, f, size) {
    const Ti = T(), post = Ti && Ti.post(postId);
    if (!post) return '';
    const cult = Ti.cultureOf(f);
    if (typeof TitleUI.iconHook === 'function') { try { const h = TitleUI.iconHook(postId, cult, size); if (h) return h; } catch (e) { /* 回到占位 */ } }
    if (SG.TitleArt && typeof SG.TitleArt.icon === 'function') {
      try { const r = SG.TitleArt.icon(postId, cult, size); if (typeof r === 'string') return `<span class="sg-office-ins"><img alt="" width="${size}" height="${size}" src="${r}"></span>`; } catch (e) { /* 回到占位 */ }
    }
    const nm = Ti.postName(f, postId, 0) || FAM_GLYPH[post.family];
    return `<span class="sg-office-ins" title="${esc(Ti.insignia(f, post.tier) || '')}">${insigniaSvg(post, nm.slice(0, 1), size)}</span>`;
  }

  // ------------------------------------------------------------------ 小部件 --
  function el(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html !== undefined && html !== null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  function officeTag(gen) {
    const Ti = T();
    if (!Ti || !Ti.enabled || !gen || !Ti.isValid(gen)) return '';
    injectStyle();
    const o = Ti.officeOf(gen);
    return `<span class="sg-office-tag${o.suspended ? ' is-suspended' : ''}" style="--fc:${o.color}" title="${esc(o.familyZh + ' · ' + o.grade + (o.suspended ? ' · 虚衔' : ''))}">${esc(o.name)}</span>`;
  }
  function adminLine(c) {
    const Ti = T(), g = G();
    if (!Ti || !Ti.enabled || !c || !g) return '';
    const a = Ti.cityAdmin(c);
    if (!a || c.governor < 0 || a.id === c.governor) return '';
    const o = Ti.officeOf(a);
    return `主政：${esc(a.name)}${o ? '（' + esc(o.name) + '）' : ''}`;
  }
  // 战场情报卡的一行（TITLES §9.6「Battle」）：小印信 + 官名（家族色）+ 本官的战斗效果；他是主将、本方有统率加成时注「统率」。
  // battle-controller.showCard 调用（INTEGRATION 请求：W5）；无官职 / 官爵关闭时 ''。情报卡不接受指针（pointer-events: none），所以效果直接写出来
  // 情报卡上的短名（效果字典的中文名太长）：最多两项 + 主将的统率，一行放得下
  const CARD_SHORT = { atk: '攻', def: '防', morale: '士气', critArmy: '会心', tac: '计策', tacHit: '计中', tacRes: '识破' };
  const CARD_KEYS = Object.keys(CARD_SHORT);
  function cardFx(e) {
    const D = SG.TitlesData, unit = D && D.effects[e.key] ? D.effects[e.key].unit : '';
    const v = unit === 'flat' ? String(Math.round(e.value)) : pctTxt(e.value) + (unit === 'pp' ? 'pp' : '%');
    return CARD_SHORT[e.key] + '+' + v;
  }
  function cardLine(u, model) {
    const Ti = T();
    if (!Ti || !Ti.enabled || !u || !u.gen || !Ti.isValid(u.gen)) return '';
    const o = Ti.officeOf(u.gen);
    if (!o) return '';
    injectStyle();
    const off = o.captive ? '被俘，暂不生效' : o.suspended ? '虚衔，暂不生效' : '';
    const fx = off ? [] : Ti.effects(o.post).filter(e => CARD_KEYS.indexOf(e.key) >= 0 && e.live !== false && e.value > 0).slice(0, 2).map(cardFx);
    if (!off && model && u.side !== undefined && typeof model.commander === 'function' && model.commander(u.side) === u) {
      const a = Ti.sideBonus(model, u.side, 'cmdAtk'), d = Ti.sideBonus(model, u.side, 'cmdDef');
      if (a > 0 && Math.abs(a - d) < 1e-9) fx.push('统率+' + pctTxt(a) + '%');
      else if (a > 0 || d > 0) fx.push('统率' + (a > 0 ? '攻+' + pctTxt(a) + '%' : '') + (d > 0 ? '防+' + pctTxt(d) + '%' : ''));
    }
    const tail = off ? `<span class="sg-card-office-off">${off}</span>` : fx.length ? `<span class="sg-card-office-fx">${esc(fx.join(' '))}</span>` : '';
    const tip = o.familyZh + ' · ' + o.grade + (fx.length ? '：' + fx.join(' ') : '') + (off ? '（' + off + '）' : '');
    return `<div class="sg-card-office" title="${esc(tip)}">${icon(o.post, o.faction, 18)}<span class="sg-office-tag${o.suspended ? ' is-suspended' : ''}" style="--fc:${o.color}">${esc(o.name)}</span><span class="sg-card-office-g">${esc(o.grade)}</span>${tail}</div>`;
  }
  function badge(f) { const Ti = T(), g = G(); if (!Ti || !Ti.enabled || !g) return 0; return Ti.pending(f === undefined ? g.player : f); }

  // ------------------------------------------------------------------ 官制总览 --
  const state = { cur: null };
  function rankLine(f) {
    const Ti = T(), g = G(), ri = Ti.rankInfo(f), r = g.ruler(f);
    const head = `<b class="sg-office-rl">${esc(r ? r.name : g.factions[f].name)} · ${esc(ri.name)}</b>`;
    const p = ri.power.toFixed(1);
    const next = ri.next ? `　国势 ${p} / ${ri.next.toFixed(1)} → ${esc(ri.nextName)}（${esc(ri.unlocks)}）` : `　国势 ${p}（位阶已极）`;
    // 手机：一行（TITLES §9.2「公 7.4/10」）
    const short = `<span class="sg-office-rs"><b>${esc(ri.name)}</b> ${p}${ri.next ? '/' + fmt1(ri.next) : ''}</span>`;
    return { html: head + '<span class="sg-office-rl">' + next + '</span>' + short, progress: ri.progress, rank: ri.rank };
  }
  const fmt1 = v => (Math.round(v * 10) / 10 === Math.round(v) ? String(Math.round(v)) : v.toFixed(1));
  function statusLine(f) {
    const Ti = T(), g = G();
    const hs = Ti.holders(f);
    const act = hs.filter(x => !x.office.suspended).length;
    const cap = Ti.caps(f).reduce((a, b) => a + b, 0);
    const sus = hs.length - act;
    const gift = Ti.giftsThisYear ? Ti.giftsThisYear(f) : 0;
    return `已授 ${act} / ${cap}` + (sus ? ` · 虚衔 ${sus}` : '') + ` · 本年礼金 ${gift} 金 · 可授空缺 ${Ti.pending(f)} · 本月任免每人一次`;
  }
  const RANK_RING = ['', '#8a6a3a', '#7a2a22', '#b98431', '#e4b64a', '#ffd86b'];
  function open(opts) {
    opts = opts || {};
    injectStyle();
    const Ti = T(), g = G();
    if (!Ti || !g) return Promise.resolve();
    const f = opts.f === undefined ? g.player : opts.f;
    if (state.cur) close();
    sfx('click');
    voice(g.ruler(f), 'ui.titleBoard');
    return new Promise(resolve => {
      const m = UI().openModal('sg-office');
      const cur = state.cur = { f, m, resolve, el: m.panel, fam: 'war', sheet: null, edict: null, city: opts.city === undefined ? null : opts.city, lastSeat: null };
      m.onKey = k => {
        if (k === 'Escape') { if (cur.edict) closeLayer('edict'); else if (cur.sheet) closeLayer('sheet'); else close(); return true; }
        return false;
      };
      render();
      // 月初提示（SG.Titles.lastHints 的一条：{ gen, tier, postId }）：直接打开那个官位的候选名单
      if (opts.hint && opts.hint.postId) openSheet(opts.hint.postId);
      else if (opts.postId) openSheet(opts.postId);
      else if (opts.gen) pickPostFor(opts.gen);
      else if (opts.tier) {
        const row = Ti.board(f).find(r => r.tier === opts.tier);
        const ps = row && row.posts.find(q => q.seats.some(x => x.state === 'free' && x.cand));
        if (ps) openSheet(ps.post.id);
      }
    });
  }
  function close() {
    const cur = state.cur;
    if (!cur) return;
    state.cur = null;
    cur.m.close();
    if (cur.resolve) cur.resolve();
  }
  // 候选名单与诏书都画在官制画面上：人物菜单的「官职 / 授官」、官爵页的按钮在官制画面没有打开时先打开它（同步建好 state.cur），
  // 再执行 fn。返回官制画面关闭时 resolve 的 Promise（本来就开着时为 null），调用者 await 它，关闭后再重绘自己。
  function withBoard(f, fn) {
    const p = state.cur && state.cur.f === f ? null : open({ f });
    if (state.cur) fn();
    return p;
  }
  function closeLayer(which) {
    const cur = state.cur; if (!cur) return;
    const l = cur[which]; if (!l) return;
    if (l.parentNode) l.parentNode.removeChild(l);
    cur[which] = null;
    if (which === 'sheet' && cur.edict) closeLayer('edict');
  }
  function render() {
    const cur = state.cur; if (!cur) return;
    const Ti = T(), g = G(), f = cur.f, P = cur.el;
    const keepSheet = cur.sheet, keepEdict = cur.edict;
    P.innerHTML = '';
    // 头
    const head = el('div', 'sg-office-head', null, P);
    const r = g.ruler(f);
    const rl = rankLine(f);
    if (r) { const pe = portrait(r, isPhone() ? 32 : 52); pe.style.setProperty('--rk', RANK_RING[rl.rank] || '#8a6a3a'); head.appendChild(pe); }
    el('div', 'sg-office-title', '官制', head);
    const rk = el('div', 'sg-office-rank', null, head);
    el('div', '', rl.html, rk);
    const bar = el('div', 'sg-office-bar', '<i></i>', rk); bar.style.setProperty('--v', rl.progress);
    el('div', 'sg-office-status', esc(statusLine(f)), rk);
    const tabs = el('div', 'sg-office-tabs', null, head);
    for (const fam of ['war', 'intel', 'pol']) {
      const b = UI().button(tabs, FAM_GLYPH[fam], () => { cur.fam = fam; render(); }, { small: true });
      b.style.setProperty('--fc', famColor(fam));
      if (cur.fam === fam) b.classList.add('is-on');
    }
    if (f === g.player) UI().button(head, '按功绩授官', () => meritFlow(), { small: true, title: '按电脑的规则为每个可授的空缺推荐人选（先列出方案，确认后才授）' });
    const x = UI().button(head, '×', () => close(), { className: 'sg-close', ariaLabel: '关闭' });
    void x;
    // 总表
    // 表格放在独立的滚动容器里：表格本身高度不定，各行按内容长高（定高的网格不会把 auto 行撑开）
    const board = el('div', 'sg-office-board', null, el('div', 'sg-office-scroll', null, P));
    el('div', 'sg-office-corner', '', board);
    for (const fam of ['war', 'intel', 'pol']) { const fh = el('div', 'sg-office-fam', esc(FAM_ZH[fam]), board); fh.style.setProperty('--fc', famColor(fam)); }
    const rows = Ti.board(f);
    for (const row of rows) {
      const gr = el('div', 'sg-office-grade' + (row.locked ? ' is-locked' : ''), `<b>${esc(row.grade)}</b><span class="sg-office-cap">${row.used} / ${row.cap}</span><span class="sg-office-req">${esc(row.req)}</span>`, board);
      gr.title = row.locked ? row.lockWhy : row.req;
      for (const fam of ['war', 'intel', 'pol']) {
        const cell = el('div', 'sg-office-cell f-' + fam + (cur.fam === fam ? ' is-shown' : ''), null, board);
        cell.style.setProperty('--fc', famColor(fam));
        for (const ps of row.posts.filter(q => q.family === fam)) {
          const post = el('div', 'sg-office-post', null, cell);
          el('div', 'sg-office-pn', icon(ps.post.id, f, 22) + esc(ps.name) + (ps.post.branch ? `<small>${esc(ps.post.role)}</small>` : ''), post);
          const chips = el('div', 'sg-office-chips', null, post);
          for (const s of ps.seats) chips.appendChild(seatChip(ps.post, s));
        }
      }
    }
    if (keepSheet) { P.appendChild(keepSheet); }
    if (keepEdict) { P.appendChild(keepEdict); }
  }
  function seatChip(post, s) {
    const cur = state.cur, g = G();
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'sg-office-seat is-' + s.state + (s.cand ? ' has-cand' : '');
    if (s.gen) {
      b.appendChild(portrait(s.gen, isPhone() ? 26 : 34));
      el('span', 'sg-office-sname', esc(s.gen.name), b);
      b.title = s.gen.name + ' · ' + s.name + (s.why ? '（' + s.why + '）' : '');
      b.setAttribute('aria-label', b.title);
      if (cur.lastSeat === s.gen.id) { b.classList.add('is-new'); cur.lastSeat = null; }
      b.addEventListener('click', () => { if (cur.f === g.player) manage(s.gen); else showGeneral(s.gen); });
    } else if (s.state === 'free') {
      b.textContent = '＋';
      b.title = s.cand ? '空缺 · 有合格人选' : '空缺 · 暂无合格人选';
      b.setAttribute('aria-label', post.id + ' ' + b.title);
      b.addEventListener('click', () => { if (cur.f === g.player) openSheet(post.id); });
    } else {
      b.textContent = s.state === 'locked' ? s.why : '名额满';
      b.title = s.why;
      b.disabled = false;
      b.setAttribute('aria-disabled', 'true');
      b.addEventListener('click', () => UI().toast(esc(s.why || '名额已满')));
    }
    return b;
  }
  function showGeneral(gen) {
    const Ti = T();
    const o = Ti.officeOf(gen);
    UI().notice ? UI().notice(esc(gen.name), tab(gen).outerHTML) : UI().say(gen.name + (o ? ' · ' + o.name : ''));
  }

  // ---- 候选名单 ----
  function openSheet(postId) {
    const cur = state.cur; if (!cur) return;
    const Ti = T(), g = G(), p = Ti.post(postId);
    closeLayer('sheet');
    const layer = el('div', 'sg-office-layer', null, cur.el);
    layer.addEventListener('click', e => { if (e.target === layer) closeLayer('sheet'); });
    const sh = el('div', 'sg-office-sheet', null, layer);
    sh.setAttribute('role', 'dialog');
    const h3 = el('h3', '', icon(postId, cur.f, 30) + esc(Ti.postName(cur.f, postId, 0)) + `<small>${esc(FAM_ZH[p.family] + ' · ' + Ti.tier(p.tier).grade)}</small><span class="sg-spacer"></span>`, sh);
    UI().button(h3, '×', () => closeLayer('sheet'), { className: 'sg-close', ariaLabel: '关闭' });
    const list = el('div', 'sg-office-list', null, sh);
    let cands = Ti.candidates(cur.f, postId);
    if (cur.city !== null && cur.city !== undefined) cands = cands.filter(c => c.gen.city === cur.city);
    if (!cands.length) el('div', '', '<span style="color:#6b5d4a">本势力没有可列出的武将。</span>', list);
    for (const c of cands) {
      const row = document.createElement('button');
      row.type = 'button';
      row.className = 'sg-office-cand' + (c.ok ? '' : ' is-no');
      row.appendChild(portrait(c.gen, isPhone() ? 32 : 40));
      el('div', 'sg-office-cmain', `<b>${esc(c.gen.name)}</b>${STAT_ZH[p.family]} ${c.stat} · Lv${c.level} · ${c.cur ? esc(c.cur) : '无官职'} · 忠 ${c.gen.loyalty}`, row);
      el('span', 'sg-office-st' + (c.ok ? ' ok' : ''), esc(c.ok ? '可任' : c.why), row);
      row.title = c.ok ? '授官' : c.why;
      row.addEventListener('click', () => { if (c.ok) openEdict(c.gen, postId); else UI().toast(esc(c.why)); });
      list.appendChild(row);
    }
    cur.sheet = layer;
    const first = list.querySelector('.sg-office-cand:not(.is-no)') || list.querySelector('button');
    if (first) try { first.focus({ preventScroll: true }); } catch (e) { /* 忽略 */ }
  }
  // 人物菜单「授官」：先选官位（他能任的、有空缺的），再确认
  async function pickPostFor(gen) {
    const Ti = T(), g = G(), f = gen.faction;
    const opts = Ti.postsFor(gen).filter(p => p.tier > Ti.curTier(gen) || (Ti.curTier(gen) === p.tier && (!gen.office || gen.office.post !== p.id)))
      .map(p => ({ p, c: Ti.canGrant(f, gen, p.id) })).sort((a, b) => b.p.tier - a.p.tier);
    if (!opts.length) { await UI().say(gen.name + '眼下没有可以晋升的官位。'); return null; }
    const items = opts.map(o => ({ label: icon(o.p.id, f, 26) + ' ' + esc(Ti.postName(f, o.p.id, 0)), right: esc(Ti.tier(o.p.tier).grade), enabled: o.c.ok, desc: o.c.ok ? esc(fxText(o.p.id)) : esc(o.c.why) }));
    const i = await UI().choose(esc(gen.name) + ' · 授官', items, '可任的官位（灰色的写明了缺什么）');
    if (i < 0) return null;
    return withBoard(f, () => openEdict(gen, opts[i].p.id));
  }

  // ---- 诏书（确认卡）----
  function fxDiff(postId, cur) {
    const Ti = T(), d = SG.TitlesData;
    const now = Ti.post(postId).fx, was = cur ? Ti.post(cur).fx : {};
    const keys = Object.keys(d.effects).filter(k => now[k] || was[k]);
    return keys.map(k => {
      const a = now[k] || 0, b = was[k] || 0, e = d.effects[k];
      const t = Ti.effectText(k, a);
      let diff = '';
      if (cur && a !== b) { const dv = a - b; diff = e.unit === 'flat' ? (dv > 0 ? '+' : '−') + Math.abs(dv) : (dv > 0 ? '+' : '−') + pctTxt(Math.abs(dv)) + (e.unit === 'pp' ? 'pp' : '%'); }
      // 钩子还没接上的效果（计策三项等 W5 的 battle-model 补丁、同盟等 W8 的使者选择）：照列数值，标「未生效」
      const dormant = a && Ti.hookLive && !Ti.hookLive(k);
      return `<span class="${a >= b ? 'up' : 'down'}${dormant ? ' is-dormant' : ''}"${dormant ? ' title="此项效果的规则钩子尚未接入本版本"' : ''}>${esc(a ? t : e.zh + ' —')}${diff ? `<small>（${esc(diff)}）</small>` : ''}${dormant ? '<em>未生效</em>' : ''}</span>`;
    }).join('');
  }
  function openEdict(gen, postId, opts) {
    const cur = state.cur; if (!cur) return;
    opts = opts || {};
    const Ti = T(), g = G(), f = cur.f, p = Ti.post(postId);
    closeLayer('edict');
    const layer = el('div', 'sg-office-layer', null, cur.el);
    layer.addEventListener('click', e => { if (e.target === layer) closeLayer('edict'); });
    let nameIdx = undefined;
    const draw = () => {
      layer.innerHTML = '';
      const c = Ti.canGrant(f, gen, postId, nameIdx);
      const ed = el('div', 'sg-office-edict', null, layer);
      ed.setAttribute('role', 'dialog');
      ed.style.setProperty('--fc', famColor(p.family));
      ed.style.setProperty('--fcd', p.family === 'war' ? '#8e2419' : p.family === 'intel' ? '#1f4a82' : '#1d6b3a');
      el('div', '', icon(postId, f, isPhone() ? 54 : 84), ed);
      const names = Ti.names(f, postId);
      const sel = c.ok ? c.nameIdx : 0;
      const nm = names[sel] || { zh: postId };
      el('div', 'sg-office-pname', esc(nm.zh), ed);
      const orig = [nm.orig, nm.rank, nm.seal || Ti.insignia(f, p.tier), nm.attest === 'B' ? '时代错置' : nm.attest === 'C' ? '拟名' : '', nm.note].filter(Boolean).join(' · ');
      if (orig) el('div', 'sg-office-orig', esc(orig), ed);
      if (names.length > 1) {
        const nb = el('div', 'sg-office-names', null, ed);
        const used = new Set(Ti.holders(f).filter(h => h !== gen && h.office.post === postId).map(h => h.office.name | 0));
        names.forEach((n, i) => {
          const b = el('button', i === sel ? 'is-on' : '', esc(n.zh), nb);
          b.type = 'button'; b.disabled = used.has(i);
          b.addEventListener('click', () => { nameIdx = i; draw(); });
        });
      }
      const who = el('div', 'sg-office-who', null, ed);
      who.appendChild(portrait(gen, 36));
      el('span', '', `<b>${esc(gen.name)}</b> ${STAT_ZH[p.family]} ${Ti.base(gen, p.family)} · Lv${gen.level || 1} · 现任 ${esc(Ti.nameOf(gen) || '无')}`, who);
      el('div', 'sg-office-fx', fxDiff(postId, Ti.isValid(gen) ? gen.office.post : null), ed);
      const cost = el('div', 'sg-office-cost', null, ed);
      const payCity = c.payFrom ? c.payFrom.name : '';
      el('span', '', c.cost > 0 ? `礼金 <b>${c.cost}</b> 金${payCity ? '（自 ' + esc(payCity) + '）' : ''}` : '礼金 —', cost);
      el('span', '', c.kind === 'move' ? '平调：忠诚不变' : `忠诚 <b class="sg-good" style="color:#1d6b3a">+${c.loyalty}</b>`, cost);
      el('span', '', '仪式 ' + ({ small: '小', medium: '中', grand: '大' }[Ti.tier(p.tier).ceremony] || ''), cost);
      if (c.passedOver && c.passedOver.length) {
        const w = el('div', 'sg-office-warn', null, ed);
        el('span', '', '越级者不悦：', w);
        for (const q of c.passedOver) { w.appendChild(portrait(q.gen, 22)); el('span', '', esc(q.gen.name) + ' −' + q.loss, w); }
      }
      el('div', 'sg-office-warn', esc('授官之后本月不能再任免' + gen.name), ed);
      if (!c.ok) el('div', 'sg-office-warn', esc(c.why), ed);
      const rr = Ti.rulerRankName(f);
      el('div', 'sg-office-stamp', esc((g.factions[f].name || '').slice(0, 1) + rr.slice(0, 2)), ed);
      const btns = el('div', 'sg-office-btns', null, ed);
      UI().button(btns, '取消', () => closeLayer('edict'), { className: 'ghost' });
      const go = UI().button(btns, '授　官', () => doGrant(gen, postId, nameIdx, opts), { primary: true });
      if (!c.ok) go.disabled = true;
      try { go.focus({ preventScroll: true }); } catch (e) { /* 忽略 */ }
    };
    cur.edict = layer;
    draw();
  }
  async function doGrant(gen, postId, nameIdx, opts) {
    const cur = state.cur; if (!cur) return null;
    const Ti = T();
    const res = Ti.grant(cur.f, gen, postId, nameIdx);
    if (!res.ok) { UI().toast(esc(res.why)); return null; }
    sfx('cmd-press-title', 0.7);
    closeLayer('sheet'); closeLayer('edict');
    cur.lastSeat = gen.id;
    await ceremony(res);
    if (!state.cur) return res;
    render();
    UI().toast((SG.UI.facePx ? '' : '') + esc(gen.name) + ' 拜 ' + esc(res.office ? res.office.name : ''), 2.4);
    if (opts && opts.resolve) opts.resolve(res);
    return res;
  }
  // 「按功绩授官」：先列出方案（不改动），确认后才授
  async function meritFlow() {
    const cur = state.cur; if (!cur) return;
    const Ti = T();
    const plan = Ti.fillByMerit(cur.f, false);
    if (!plan.length) { await UI().say('眼下没有可授的空缺，或礼金不足。'); return; }
    const total = plan.reduce((s, p) => s + (p.cost || 0), 0);
    const lines = plan.map(p => `<span style="white-space:nowrap">${esc(p.gen.name)} → ${esc(p.name)}${p.cost ? ' · ' + p.cost + '金' : ''}</span>${p.passedOver && p.passedOver.length ? ' <span class="sg-bad" style="white-space:nowrap">' + p.passedOver.map(q => esc(q.gen.name) + ' −' + q.loss).join(' ') + '</span>' : ''}`).join('<br>');
    const okk = await UI().confirm(`按功绩授官：${plan.length} 人，礼金 ${total} 金<div style="font-size:.78em;line-height:1.55;margin-top:.35em">${lines}</div>`, '授官', '取消');
    if (!okk) return;
    const done = Ti.fillByMerit(cur.f, true);
    sfx('coin');
    if (done.length) {
      const best = done.slice().sort((a, b) => (Ti.post(b.post) ? Ti.post(b.post).tier : 0) - (Ti.post(a.post) ? Ti.post(a.post).tier : 0))[0];
      if (best) await ceremony({ gen: best.gen.id, office: Ti.officeOf(best.gen), ceremony: 'small' });
    }
    if (state.cur) render();
    UI().toast(`授官 ${done.length} 人`, 2.2);
  }

  // ---- 已任官者的操作 ----
  async function manage(gen) {
    const Ti = T(), g = G(), f = gen.faction;
    const o = Ti.officeOf(gen);
    if (!o) return pickPostFor(gen);
    const acts = [];
    const ups = Ti.postsFor(gen).filter(p => p.tier > o.tier && Ti.canGrant(f, gen, p.id).ok);
    const lat = Ti.postsFor(gen).filter(p => p.tier === o.tier && p.id !== o.post && Ti.canGrant(f, gen, p.id).ok);
    const names = Ti.names(f, o.post);
    const ch = Ti.changedThisMonth(gen);
    acts.push({ label: '晋升', desc: ups.length ? ups.map(p => Ti.postName(f, p.id, 0)).join('、') : (ch ? '本月已有任免' : '没有可晋升的空缺'), enabled: ups.length > 0, run: () => (ups.length === 1 ? withBoard(f, () => openEdict(gen, ups[0].id)) : choosePost(gen, ups)) });
    acts.push({ label: '改任', desc: lat.length ? lat.map(p => Ti.postName(f, p.id, 0)).join('、') : '同品没有其他可任的职位', enabled: lat.length > 0, run: () => (lat.length === 1 ? withBoard(f, () => openEdict(gen, lat[0].id)) : choosePost(gen, lat)) });
    acts.push({ label: '换名号', desc: names.length > 1 ? names.map(n => n.zh).join('、') : '此职只有一个名号', enabled: names.length > 1, run: () => chooseName(gen) });
    const lows = Ti.postsFor(gen).filter(p => p.tier < o.tier && Ti.eligible(gen, p.id));
    acts.push({ label: '贬官', desc: ch ? '本月已有任免' : lows.length ? '忠诚 −' + Ti.demoteCost(gen, lows[lows.length - 1].id) + ' 起' : '没有更低的职位', enabled: !ch && lows.length > 0, run: () => chooseDemote(gen, lows) });
    const rv = Ti.canRevoke(gen);
    acts.push({ label: '罢免', desc: rv.ok ? `忠诚 −${Ti.revokeCost(gen)}（现 ${gen.loyalty}）` : rv.why, enabled: rv.ok, run: () => revokeFlow(gen) });
    const i = await UI().choose(esc(gen.name) + ' · ' + esc(o.name) + (o.suspended ? '（虚衔）' : ''), acts.map(a => ({ label: a.label, desc: esc(a.desc), enabled: a.enabled })), esc(o.familyZh + ' · ' + o.grade + ' · ' + fxText(o.post)));
    if (i < 0) return null;
    return acts[i].run();
  }
  async function choosePost(gen, posts) {
    const Ti = T(), f = gen.faction;
    const i = await UI().choose('选择官职', posts.map(p => ({ label: icon(p.id, f, 24) + ' ' + esc(Ti.postName(f, p.id, 0)), right: esc(Ti.tier(p.tier).grade), desc: esc(fxText(p.id)) })));
    if (i < 0) return null;
    return withBoard(f, () => openEdict(gen, posts[i].id));
  }
  async function chooseName(gen) {
    const Ti = T(), f = gen.faction, names = Ti.names(f, gen.office.post);
    const i = await UI().choose('名号', names.map((n, k) => ({ label: esc(n.zh), desc: esc([n.orig, n.note].filter(Boolean).join(' · ')), enabled: k !== (gen.office.name | 0) })));
    if (i >= 0) { if (Ti.setName(gen, i)) { UI().toast(esc(gen.name) + ' 改称 ' + esc(Ti.nameOf(gen))); if (state.cur) render(); } else UI().toast('这个名号已有人用'); }
  }
  async function chooseDemote(gen, posts) {
    const Ti = T(), f = gen.faction;
    const i = await UI().choose('贬为', posts.map(p => ({ label: esc(Ti.postName(f, p.id, 0)), right: esc(Ti.tier(p.tier).grade), desc: '忠诚 −' + Ti.demoteCost(gen, p.id) })));
    if (i < 0) return;
    const after = gen.loyalty - Ti.demoteCost(gen, posts[i].id);
    if (!(await UI().confirm(`贬${esc(gen.name)}为${esc(Ti.postName(f, posts[i].id, 0))}？<br><span class="sg-bad">贬后忠诚 ${Math.max(0, after)}${after < 40 ? '（低于 40 可能出奔）' : ''}</span>`, '贬官', '取消'))) return;
    const r = Ti.demote(gen, posts[i].id);
    UI().toast(esc(r.ok ? r.line : r.why));
    if (state.cur) render();
  }
  async function revokeFlow(gen) {
    const Ti = T();
    const cost = Ti.revokeCost(gen), after = Math.max(0, gen.loyalty - cost);
    if (!(await UI().confirm(`罢免${esc(gen.name)}的${esc(Ti.nameOf(gen))}？<br><span class="sg-bad">忠诚 −${cost}，罢后忠诚 ${after}${after < 40 ? '（低于 40 可能出奔）' : ''}</span>`, '罢免', '取消'))) return;
    const r = Ti.revoke(gen);
    voice(gen, 'title.revoke');
    UI().toast(esc(r.ok ? r.line : r.why));
    if (state.cur) render();
  }
  // 人物菜单的「授官 / 晋升」
  async function grant(gen, postId) {
    const g = G();
    if (!gen || gen.faction !== g.player) return null;
    // 先选官位（他能任的），选定后才打开官制画面与诏书；画面关闭时 resolve（授了官为授官结果）
    if (!postId) return pickPostFor(gen);
    return new Promise(resolve => {
      let res = null;
      const p = withBoard(gen.faction, () => openEdict(gen, postId, { resolve: r => { res = r; } }));
      if (p) p.then(() => resolve(res)); else resolve(null);
    });
  }
  function confirmCard(f, gen, postId) {
    return new Promise(resolve => {
      open({ f }).then(() => resolve(null));
      openEdict(gen, postId, { resolve: r => { resolve(r); } });
    });
  }

  // ------------------------------------------------------------------ 仪式 --
  function ceremony(res) {
    injectStyle();
    const Ti = T(), g = G();
    const gen = res && (typeof res.gen === 'number' ? g.generals[res.gen] : res.gen);
    const office = res && (res.office || (gen ? Ti.officeOf(gen) : null));
    if (!gen || !office) return Promise.resolve();
    const size = res.ceremony || res.size || Ti.tier(office.tier).ceremony || 'small';
    const ruler = gen.faction >= 0 ? g.ruler(gen.faction) : null;
    if (SG.TitleArt && typeof SG.TitleArt.ceremony === 'function') {
      try { return Promise.resolve(SG.TitleArt.ceremony({ gen, ruler, office, culture: Ti.cultureOf(gen.faction), size })); } catch (e) { /* 回到占位 */ }
    }
    voice(ruler, 'title.grant', { title: office.name });
    sting(stingTier(office.tier), size === 'grand' ? 'horn' : size === 'medium' ? 'win' : 'coin');
    return stage(size, root => {
      root.style.setProperty('--fcd', office.fam === 'war' ? '#8e2419' : office.fam === 'intel' ? '#1f4a82' : '#1d6b3a');
      if (size === 'grand') {
        const lan = el('div', 'sg-office-lanterns', '<i></i><i></i><i></i><i></i>', root); void lan;
        const pair = el('div', 'sg-office-pair sg-office-rise', null, root);
        if (ruler) pair.appendChild(portrait(ruler, 110));
        el('div', '', icon(office.post, gen.faction, 96), pair);
        pair.appendChild(portrait(gen, 110));
        el('div', 'sg-office-cname', esc(office.name), root);
      } else if (size === 'medium') {
        el('div', 'sg-office-rise', icon(office.post, gen.faction, 110), root);
        const strip = el('div', 'sg-office-strip', null, root);
        strip.appendChild(portrait(gen, 64));
        el('div', 'sg-office-cname', esc(office.name), strip);
      } else {
        const strip = el('div', 'sg-office-strip', null, root);
        strip.appendChild(portrait(gen, 56));
        el('div', 'sg-office-cname', esc(office.name), strip);
      }
      const seal = el('div', 'sg-office-cseal', '册<br>封', root);
      seal.style.setProperty('--d', size === 'small' ? '1.05s' : size === 'medium' ? '1.6s' : '2.4s');
      const sub = el('div', 'sg-office-csub', esc(`拜${gen.name}为${office.name}`) + (ruler ? `<br><small>${esc(ruler.name)}：「封尔为${office.name}！」</small>` : ''), root);
      sub.style.setProperty('--d2', size === 'small' ? '.9s' : '1.4s');
      if (size !== 'small') flecks(root, size === 'grand' ? 26 : 14);
      setTimeout(() => voice(gen, res.kind === 'promote' ? 'title.promote' : 'title.accept'), size === 'small' ? 900 : 1600);
      const po = res.passedOver && res.passedOver[0];
      if (po && po.gen) setTimeout(() => voice(po.gen, 'title.passedOver'), size === 'small' ? 1500 : 2600);
    });
  }
  function rankUp(f, from, to) {
    injectStyle();
    const Ti = T(), g = G(), r = g.ruler(f);
    if (!r || !(to > from)) return Promise.resolve();
    const size = to >= 4 ? 'grand' : to >= 2 ? 'medium' : 'small';
    voice(r, 'title.rankUp');
    sting(to >= 4 ? 'great' : 'good', size === 'grand' ? 'horn' : 'win');
    return stage(size, root => {
      if (size === 'grand') el('div', 'sg-office-lanterns', '<i></i><i></i><i></i><i></i>', root);
      const pe = portrait(r, size === 'grand' ? 130 : 100);
      pe.style.boxShadow = `0 0 0 4px ${RANK_RING[to]}, 0 0 30px ${RANK_RING[to]}`;
      const box = el('div', 'sg-office-rise', null, root); box.appendChild(pe);
      const strip = el('div', 'sg-office-strip', null, root);
      el('div', 'sg-office-cname', esc(r.name + ' 晋位为' + Ti.rulerRankName(f, to)), strip);
      const caps = (SG.TitlesData && SG.TitlesData.rulerRanks.caps[to]) || Ti.caps(f), parts = [];
      for (let t = 5; t >= 1 && parts.length < 2; t--) if (caps[t - 1] > 0) parts.push(Ti.tier(t).grade + '官' + numZh(caps[t - 1]) + '人');
      el('div', 'sg-office-csub', esc('可授' + parts.join('、')), root);
      el('div', 'sg-office-cseal', '晋<br>位', root).style.setProperty('--d', size === 'grand' ? '2.4s' : '1.6s');
      flecks(root, size === 'grand' ? 26 : 12);
    });
  }
  function flecks(root, n) {
    for (let i = 0; i < n; i++) {
      const s = el('i', 'sg-office-fleck', null, root);
      s.style.left = (5 + (i * 37) % 90) + '%';
      s.style.setProperty('--t', (2.4 + (i % 5) * 0.35) + 's');
      s.style.setProperty('--dl', ((i % 7) * 0.18) + 's');
      s.style.setProperty('--dx', ((i % 2 ? 1 : -1) * (10 + (i * 13) % 40)) + 'px');
    }
  }
  const LEN = { small: 1800, medium: 3000, grand: 4500 };
  function stage(size, build) {
    return new Promise(resolve => {
      if (typeof document === 'undefined') { resolve(); return; }
      const short = shortCeremony();
      const root = el('div', 'sg-office-cer is-' + size, null, document.body);
      root.setAttribute('role', 'dialog'); root.setAttribute('aria-label', '授官仪式');
      if (short) {
        root.style.animationDuration = '.2s';
        build(root);
        for (const n of root.querySelectorAll('.sg-office-cname')) n.style.animation = 'none', n.style.clipPath = 'none';
        for (const n of root.querySelectorAll('.sg-office-cseal')) n.style.setProperty('--d', '.25s');
        for (const n of root.querySelectorAll('.sg-office-csub')) n.style.setProperty('--d2', '.1s');
        for (const n of root.querySelectorAll('.sg-office-fleck, .sg-office-lanterns')) n.remove();
      } else build(root);
      el('div', 'sg-office-skip', '点按跳过', root);
      let done = false, timer = null;
      const finish = () => {
        if (done) return; done = true;
        clearTimeout(timer);
        document.removeEventListener('keydown', onKey, true);
        root.classList.add('is-out');
        setTimeout(() => { if (root.parentNode) root.parentNode.removeChild(root); resolve(); }, 300);
      };
      const onKey = e => { if (e.key === ' ' || e.key === 'Escape' || e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); finish(); } };
      root.addEventListener('click', finish);
      document.addEventListener('keydown', onKey, true);
      timer = setTimeout(finish, short ? 600 : LEN[size] || 1800);
      TitleUI.lastCeremony = { size, short, at: Date.now() };
    });
  }
  // 月初：玩家的晋位（Titles 的事件队列；授官仪式已在授官时播过）
  async function flush() {
    const Ti = T();
    if (!Ti || !Ti.takeEvents) return;
    const ev = Ti.takeEvents().filter(e => e.kind === 'rank' && e.up);
    for (const e of ev) await rankUp(e.f, e.from, e.to);
  }

  // ------------------------------------------------------------------ 人物菜单「官爵」页 --
  // 条件的小块：ok 绿色 ✓、否则红色 ✗（文字里另有符号，不只靠颜色）
  function reqSpan(ok, text) { return `<span class="${ok ? 'ok' : 'no'}">${esc(text)} ${ok ? '✓' : '✗'}</span>`; }
  // 等级条（§9.4 的「▓▓▓▓░」）：需 n 级时 n 格，已到的格实心、未到的空框（CSS 小格，颜色随条件的绿 / 红）
  function levelBar(lv, need) {
    const n = Math.max(1, need | 0), k = Math.max(0, Math.min(n, lv | 0));
    return `<span class="sg-office-lv" role="img" aria-label="等级 ${k} / ${n}">` + '<i class="on"></i>'.repeat(k) + '<i></i>'.repeat(n - k) + '</span>';
  }
  function tab(gen) {
    injectStyle();
    const Ti = T(), g = G();
    const root = el('div', 'sg-office-tab');
    if (!Ti || !Ti.enabled || !gen) { el('div', '', '官爵制度未开启。', root); return root; }
    const mine = gen.faction === g.player && gen.faction >= 0;
    const o = Ti.officeOf(gen);
    const f = gen.faction;
    // 页上的按钮走完流程（官制画面关闭、确认或取消）之后，就地换成新的一页（人物菜单不必知道官爵改了什么）
    // 换完之后再发一个冒泡的 'sg-office-change' 事件（detail.gen = 武将编号），宿主（人物菜单的头部官名）可据此重绘
    const rerender = () => {
      if (!root.isConnected) return;
      const fresh = tab(gen); fresh.className = root.className; root.replaceWith(fresh);
      try { fresh.dispatchEvent(new CustomEvent('sg-office-change', { bubbles: true, detail: { gen: gen.id } })); } catch (e) { /* 旧浏览器 */ }
    };
    const after = fn => () => { let p; try { p = fn(); } catch (e) { console.error(e); } Promise.resolve(p).then(rerender, e => { console.error(e); rerender(); }); };
    // 现任
    const card = el('div', 'sg-office-card', null, root);
    if (o) {
      card.style.setProperty('--fc', o.color); card.style.setProperty('--fcl', o.fam === 'war' ? '#ff9a8a' : o.fam === 'intel' ? '#9cc2ff' : '#8fe0a8');
      card.style.setProperty('--fcd', o.fam === 'war' ? '#8e2419' : o.fam === 'intel' ? '#1f4a82' : '#1d6b3a');
      el('div', '', icon(o.post, f, 64), card);
      const y = Math.floor((o.since - 1) / 12), mo = o.since - y * 12;
      const status = o.captive ? '被俘中，暂不生效' : o.suspended ? '虚衔——国势不足' : '在任';
      el('div', '', `<div class="sg-office-cn">${esc(o.name)}</div><div class="sg-office-cs">${esc([o.orig, o.familyZh + ' · ' + o.grade, y + '年' + mo + '月起', status].filter(Boolean).join(' · '))}<br>${esc(fxText(o.post))}${o.note ? '<br>' + esc(o.note) : ''}</div>`, card);
    } else {
      const fam = Ti.families(gen);
      el('div', '', `<div class="sg-office-cn" style="color:var(--sg-muted,#ada392)">无官职</div><div class="sg-office-cs">最高属性：${esc(fam.map(q => STAT_ZH[q]).join(' / '))} → ${esc(fam.map(q => FAM_ZH[q]).join(' / '))}</div>`, card);
    }
    if (!mine) {
      const fa = f >= 0 ? g.factions[f] : null;
      el('div', 'sg-office-cs', esc((fa ? fa.name + '麾下' : '在野') + (o ? ' · ' + o.name : '') + (o ? ' · 拉拢抗性 ' + Math.round(Ti.persuadeRes(gen) * 100) + '%' : '')), root);
      return root;
    }
    // 阶梯（他的家族；平局时每家一条）
    for (const fam of Ti.families(gen)) {
      const lad = el('div', 'sg-office-ladder', null, root);
      el('div', 'sg-office-cs', esc(FAM_ZH[fam] + '之阶'), lad);
      for (let t = 1; t <= 5; t++) {
        const posts = Ti.posts().filter(p => p.family === fam && p.tier === t);
        const tr = Ti.tier(t);
        const rung = el('div', 'sg-office-rung' + (o && o.tier === t && o.fam === fam ? ' is-cur' : ''), null, lad);
        rung.style.setProperty('--fc', famColor(fam));
        el('div', 'sg-office-rg', esc(tr.grade), rung);
        // §9.4 每级一行条件：「等级 ▓▓▓▓░ 4 / 5 ✓」「武力 98 > 80 ✓」「名额 2 / 2 ✗ 国势达到 10.0 后增加」——✓ / ✗ 与颜色并用（色盲也读得出）
        const reqs = [];
        const lv = gen.level || 1;
        reqs.push(`<span class="${lv >= tr.reqLevel ? 'ok' : 'no'}">等级 ${levelBar(lv, tr.reqLevel)} ${lv} / ${tr.reqLevel} ${lv >= tr.reqLevel ? '✓' : '✗'}</span>`);
        if (tr.reqStat) {
          const v = Ti.base(gen, fam);
          reqs.push(reqSpan(v > tr.reqStat, v > tr.reqStat ? `${STAT_ZH[fam]} ${v} > ${tr.reqStat}` : `${STAT_ZH[fam]} ${v}，需 > ${tr.reqStat}`));
        }
        const cap = Ti.caps(f)[t - 1];
        const used = Ti.holders(f).filter(h => h.office.tier === t && !h.office.suspended).length;
        // 他本人就在这一品任职：这一席是他的，算作满足
        const self = !!(o && o.tier === t && !o.suspended);
        const hint = !self && used >= cap && Ti.seatHint ? Ti.seatHint(f, t) : '';
        reqs.push(reqSpan(self || used < cap, `名额 ${used} / ${cap}` + (self ? '（本人在任）' : '') + (hint ? ' ' + hint : '')));
        el('div', 'sg-office-rr', posts.map(p => esc(Ti.postName(f, p.id, 0))).join(' · ') + '<br>' + reqs.join(' · '), rung);
        const canUp = posts.find(p => (!o || t > o.tier) && Ti.canGrant(f, gen, p.id).ok);
        if (canUp) UI().button(rung, o ? '晋升' : '授官', after(() => grant(gen, canUp.id)), { small: true, primary: true });
        else el('span', '', '', rung);
      }
    }
    // 操作
    const acts = el('div', 'sg-office-acts', null, root);
    if (o) {
      UI().button(acts, '改任 / 名号', after(() => manage(gen)), { small: true });
      const rv = Ti.canRevoke(gen);
      const b = UI().button(acts, '罢免', after(() => revokeFlow(gen)), { small: true, className: 'ghost' });
      if (!rv.ok) { b.disabled = true; b.title = rv.why; }
    }
    UI().button(acts, '官制总览', after(() => open({ f })), { small: true });
    // 履历
    const hist = Ti.history(gen);
    if (hist.length) el('div', 'sg-office-hist', esc(hist.join(' → ')), root);
    return root;
  }

  const TitleUI = {
    open, close, grant, confirm: confirmCard, manage, tab, ceremony, rankUp, flush, badge, icon, officeTag, adminLine, cardLine,
    iconHook: null, lastCeremony: null, CEREMONY_MS: LEN,   // 各档仪式的长度（毫秒；测试可临时调长以便截图）
    get current() { const c = state.cur; return c ? { f: c.f, el: c.el, fam: c.fam, sheet: !!c.sheet, edict: !!c.edict } : null; },
    // 测试用
    _openSheet: id => openSheet(id), _openEdict: (gen, id) => openEdict(gen, id),
  };
  SG.TitleUI = TitleUI;
})();
