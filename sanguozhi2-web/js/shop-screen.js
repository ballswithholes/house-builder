'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 市场与府库画面（ITEMS.md §5.5、§6.6、§6.7、§14.2）
   第一版界面：用现有的 SG.UI 部件（.sg-panel / .sg-btn / openModal）与文字图标（类型的一个字 + 稀有度边框 + 装备配色），
   不需要新美术。每件装备的图标都经过 SG.ShopScreen.icon(id, size)：美术推广做出 SG.ItemArt.icon 之后自动换成正式图标
   （或设 SG.ShopScreen.iconHook = (id, size) => html）。

   SG.ShopScreen.open({ city, gen?, f?, tab? }) → Promise（关闭时 resolve）
       tab: 'buy' 购买 | 'sell' 出售 | 'armoury' 府库 | 'book' 图鉴；f 缺省 = 玩家势力；gen 缺省 = 该城太守
       城不对 f 开放时（他国 / 不相邻的同盟城）只能看不能买（ITEMS §6.1）
   SG.ShopScreen.armoury({ f?, gen? })     直接打开府库页（人物菜单、顶梁的入口用）
   SG.ShopScreen.gift(gen) → Promise<结果 | null>   赏赐的「赐宝物」：从府库挑一件赐给 gen（每件实物只涨一次忠诚）
   SG.ShopScreen.reveal({ id, from, how }) → Promise  神品 / 天宝的揭示（战利品、寻宝、奖品；SG.Items.takeReveals() 的条目）
   SG.ShopScreen.icon(id, size) → HTML      SG.ShopScreen.card(id, opts) → HTML（详情卡）    SG.ShopScreen.statsText(item) → 文字
   SG.ShopScreen.giftMsg(gen, result, html) → 文字      SG.ShopScreen.shieldOff(gen, weaponId) → 会被卸下的盾 | null
   SG.ShopScreen.current → 打开中的画面状态（测试用：{ city, f, gen, tab, sel, el }）；SG.ShopScreen.close()
   载入：ui.js、items.js 之后（INTEGRATION §11：… character-screen.js, shop-screen.js, engage-screen.js …）。
   类名（本文件注入的样式，前缀 .sg-shop-）：.sg-shop（对话框）、-head、-tabs、-body、-keeper、-grid、-tile（.is-sel / .is-sold / .r-<稀有度>）、
     -icon、-side、-gens、-slots、-slot、-card、-stats、-delta（.up / .down）、-actions、-book、-page、-reveal
   ========================================================================== */
(function () {
  const SG = window.SG;
  const esc = s => (SG.esc ? SG.esc(s) : String(s));
  const I = () => SG.Items, Gr = () => SG.Gear, UI = () => SG.UI;
  const D = () => (SG.Items && SG.Items.data) || SG.ItemsData;

  // 类型的一个字（文字图标；美术之前的占位）
  const TYPE_GLYPH = {
    spear: '枪', lance: '槊', ji: '戟', ge: '戈', poleblade: '刀', bigblade: '斩', axe: '斧', trident: '叉', falx: '镰', staff: '杖', sword: '剑', dao: '刀',
    gladius: '剑', spatha: '剑', tachi: '刀', khanda: '剑', mace: '锤', club: '棍', handaxe: '斧', shortji: '戟', bian: '鞭', ankusha: '钩', vajra: '杵', fan: '扇',
    twinsword: '双', twindao: '双', twinji: '双', bow: '弓', longbow: '弓', crossbow: '弩', throw: '镖', shield: '盾', bigshield: '盾', buckler: '盾',
    lamellar: '甲', scale: '鳞', mail: '锁', segmentata: '甲', cuirass: '甲', leather: '革', rattan: '藤', cloth: '衣', robe: '袍', cataphract: '铠', hide: '皮',
    helm: '盔', crest: '盔', mask: '面', capInt: '巾', capPol: '冠', crown: '冕', horse: '马', warhorse: '骑', pony: '驹', camel: '驼', elephant: '象', chariot: '车',
    milbook: '兵', civbook: '书', seal: '印', tool: '器', banner: '旗', drum: '鼓', music: '琴', jewel: '珍', medicine: '药', amulet: '符', relic: '圣',
  };
  const SLOT_GLYPH = { weapon: '兵', offhand: '副', armour: '甲', helmet: '盔', mount: '骑', treasure: '宝' };
  const TABS = [['buy', '购买'], ['sell', '出售'], ['armoury', '府库'], ['book', '图鉴']];
  const TIER_NAME = ['', '村市', '市集', '大市', '都会'];
  const reduced = () => typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ------------------------------------------------------------------ 样式（只注入一次）--
  const CSS = `
.sg-dialog.sg-shop { --w: 66rem; width: min(var(--w), 100%); height: min(44rem, 100%); max-height: 100%; padding: .8rem 1rem .9rem; gap: .5rem; }
.sg-shop-head { display: flex; align-items: center; gap: .7rem; padding-bottom: .4rem; border-bottom: 1px solid var(--sg-gold-dim, rgba(243,201,105,.35)); }
.sg-shop-title { font-family: var(--sg-font-title); font-size: 1.7rem; color: var(--sg-gold, #f3c969); white-space: nowrap; }
.sg-shop-tier { font-size: .95rem; color: var(--sg-muted, #ada392); white-space: nowrap; }
.sg-shop-tier b { color: var(--sg-text, #f5eddb); font-weight: 600; }
.sg-shop-head .sg-spacer { flex: 1; }
.sg-shop-gold { font-variant-numeric: tabular-nums; white-space: nowrap; }
.sg-shop-tabs { display: flex; gap: .4rem; flex-wrap: wrap; }
.sg-shop-tabs .sg-btn { min-height: 44px; min-width: 5.2rem; }
.sg-shop-tabs .sg-btn.is-on { box-shadow: 0 0 0 2px var(--sg-gold, #f3c969) inset, 0 0 12px rgba(243,201,105,.35); color: var(--sg-gold-hi, #fff1c8); }
.sg-shop-body { flex: 1; min-height: 0; display: grid; grid-template-columns: 11rem 1fr 19rem; gap: .7rem; }
.sg-shop-keeper { display: flex; flex-direction: column; align-items: center; gap: .5rem; padding: .6rem .4rem; border-radius: .7rem; background: rgba(255,240,210,.05); text-align: center; overflow: hidden; }
.sg-shop-keeper .sg-shop-seal { width: 5.2rem; height: 5.2rem; border-radius: 1rem; display: flex; align-items: center; justify-content: center; font-family: var(--sg-font-title);
  font-size: 3.2rem; color: #fff2e0; background: radial-gradient(circle at 35% 30%, #d9583f, #8e2419); box-shadow: 0 6px 18px rgba(0,0,0,.45), inset 0 0 0 2px rgba(255,220,170,.25); }
.sg-shop-keeper .sg-shop-line { font-size: .92rem; color: var(--sg-muted, #ada392); line-height: 1.45; }
.sg-shop-keeper .sg-shop-banner { font-family: var(--sg-font-title); font-size: 1.25rem; color: var(--sg-gold, #f3c969); }
.sg-shop-main { min-height: 0; display: flex; flex-direction: column; gap: .4rem; }
.sg-shop-note { font-size: .95rem; color: var(--sg-muted, #ada392); }
.sg-shop-note b { color: var(--c-strong, var(--sg-text, inherit)); }
.sg-shop-grid { flex: 1; min-height: 0; overflow-y: auto; -webkit-overflow-scrolling: touch; display: grid; grid-template-columns: repeat(auto-fill, minmax(8.6rem, 1fr)); gap: .5rem; align-content: start; padding: .15rem; }
.sg-shop-tile { position: relative; display: flex; align-items: center; gap: .5rem; min-height: 56px; padding: .4rem .5rem; border-radius: .6rem; cursor: pointer; text-align: left;
  background: rgba(255,240,210,.06); border: 1px solid rgba(255,240,210,.12); color: var(--sg-text, #f5eddb); font: inherit; transition: transform .12s, box-shadow .12s, background .12s; }
.sg-shop-tile:hover { background: rgba(255,240,210,.11); }
.sg-shop-tile.is-sel { box-shadow: 0 0 0 2px var(--rc, #f3c969), 0 0 14px color-mix(in srgb, var(--rc, #f3c969) 50%, transparent); background: rgba(255,240,210,.13); }
.sg-shop-tile.is-off { opacity: .5; }
.sg-shop-tile .sg-shop-tname { font-size: .98rem; line-height: 1.2; color: var(--rc, #f5eddb); overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; }
.sg-shop-tile .sg-shop-tsub { display: block; font-size: .8rem; color: var(--sg-muted, #ada392); white-space: normal; line-height: 1.35; }
.sg-shop-tile .sg-shop-tag { display: inline-block; white-space: nowrap; margin-left: .35rem; padding: 0 .25rem; border-radius: .25rem; font-size: .72rem; color: #2b2622; background: #e8c88a; }
.sg-shop-icon { --sz: 44px; flex: none; position: relative; width: var(--sz); height: var(--sz); border-radius: 22%; display: inline-flex; align-items: center; justify-content: center;
  background: linear-gradient(145deg, var(--c1, #8e939c), var(--c2, #3a3a44)); box-shadow: 0 0 0 2px var(--rc, #9aa3ad), inset 0 0 0 1px rgba(0,0,0,.45), inset 0 -.4em .8em rgba(0,0,0,.28);
  font-family: var(--sg-font-title); font-size: calc(var(--sz) * .55); color: #fff7e8; text-shadow: 0 1px 0 #2b2622, 0 0 3px #2b2622; overflow: hidden; }
.sg-shop-icon.r-rare { box-shadow: 0 0 0 2px var(--rc), 0 0 10px var(--glow, #4a8fdc), inset 0 0 0 1px rgba(0,0,0,.45); }
.sg-shop-icon.r-epic, .sg-shop-icon.r-legendary { box-shadow: 0 0 0 2px var(--rc), 0 0 16px var(--glow, #a464d8), inset 0 0 0 1px rgba(0,0,0,.45); }
.sg-shop-icon.r-epic::after, .sg-shop-icon.r-legendary::after { content: ''; position: absolute; inset: -40%; background: linear-gradient(115deg, transparent 40%, rgba(255,255,255,.35) 50%, transparent 60%);
  animation: sg-shop-shimmer 3.2s ease-in-out infinite; }
.sg-shop-icon.is-unseen { background: rgba(0,0,0,.35); color: rgba(255,255,255,.25); box-shadow: 0 0 0 1px rgba(255,255,255,.12); text-shadow: none; }
@keyframes sg-shop-shimmer { 0% { transform: translateX(-60%); } 60%, 100% { transform: translateX(60%); } }
.sg-shop-side { min-height: 0; overflow-y: auto; -webkit-overflow-scrolling: touch; display: flex; flex-direction: column; gap: .5rem; padding-right: .2rem; }
.sg-shop-gens { display: flex; gap: .3rem; flex-wrap: wrap; }
.sg-shop-gens .sg-btn { min-height: 44px; padding: .2rem .55rem; font-size: .95rem; }
.sg-shop-gens .sg-btn.is-on { box-shadow: 0 0 0 2px var(--sg-gold, #f3c969) inset; color: var(--sg-gold-hi, #fff1c8); }
.sg-shop-who { display: flex; align-items: center; gap: .55rem; }
.sg-shop-who .sg-portrait { flex: none; border-radius: .5rem; overflow: hidden; }
.sg-shop-who .sg-shop-whoname { font-family: var(--sg-font-title); font-size: 1.25rem; color: var(--sg-gold, #f3c969); }
.sg-shop-who .sg-shop-whostats { font-size: .88rem; color: var(--sg-muted, #ada392); line-height: 1.35; }
.sg-shop-slots { display: grid; grid-template-columns: 1fr 1fr; gap: .35rem; }
.sg-shop-slot { display: flex; align-items: center; gap: .4rem; min-height: 48px; padding: .25rem .35rem; border-radius: .5rem; background: rgba(0,0,0,.22); border: 1px dashed rgba(255,240,210,.15);
  cursor: pointer; color: var(--sg-text, #f5eddb); font: inherit; text-align: left; }
.sg-shop-slot.is-sel { border-style: solid; border-color: var(--sg-gold, #f3c969); }
.sg-shop-slot .sg-shop-sname { font-size: .82rem; line-height: 1.15; color: var(--rc, var(--sg-muted, #ada392)); overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; }
.sg-shop-slot .sg-shop-icon { --sz: 34px; }
.sg-shop-slot.is-lock .sg-shop-sname::after { content: ' 🔒'; }
.sg-shop-card { padding: .55rem .65rem; border-radius: .6rem; background: rgba(255,240,210,.07); border: 1px solid color-mix(in srgb, var(--rc, #f3c969) 55%, transparent); }
.sg-shop-card.is-bought { animation: sg-shop-flip .55s var(--sg-ease, ease) both; }
.sg-shop-card.is-bought.r-rare, .sg-shop-card.is-bought.r-epic, .sg-shop-card.is-bought.r-legendary { animation: sg-shop-flip .55s var(--sg-ease, ease) both, sg-shop-pillar 1.2s ease-out both; }
@keyframes sg-shop-flip { 0% { transform: perspective(600px) rotateY(90deg); filter: brightness(2); } 100% { transform: none; filter: none; } }
@keyframes sg-shop-pillar { 0% { box-shadow: 0 0 0 0 var(--rc); } 30% { box-shadow: 0 0 30px 8px var(--rc); } 100% { box-shadow: 0 0 0 0 transparent; } }
.sg-shop-chead { display: flex; gap: .55rem; align-items: center; }
.sg-shop-chead .sg-shop-icon { --sz: 56px; }
.sg-shop-cname { font-family: var(--sg-font-title); font-size: 1.3rem; color: var(--rc, #f5eddb); line-height: 1.15; }
.sg-shop-csub { font-size: .82rem; color: var(--sg-muted, #ada392); }
.sg-shop-stats { margin: .35rem 0 .2rem; font-size: .92rem; line-height: 1.45; }
.sg-shop-ability { font-size: .88rem; line-height: 1.45; color: #e9dcc0; margin-top: .25rem; }
.sg-shop-ability b { color: var(--rc, #f3c969); }
.sg-shop-lore { font-size: .82rem; color: var(--sg-muted, #ada392); margin-top: .3rem; line-height: 1.4; }
.sg-shop-req { font-size: .88rem; margin-top: .25rem; }
.sg-shop-delta { font-size: .88rem; margin-top: .3rem; line-height: 1.5; }
.sg-shop-delta .up { color: var(--sg-good, #73d98c); } .sg-shop-delta .down { color: var(--sg-bad, #ff7361); }
/* 操作钮一行并排，钉在详情栏的下缘（长卡也不必滚动就看得到；ITEMS §6.7） */
.sg-shop-actions { display: flex; gap: .4rem; flex-wrap: nowrap; margin: .45rem -.4rem -.3rem; padding: .35rem .4rem .3rem; position: sticky; bottom: -.1rem; z-index: 2;
  background: var(--paper, #f3e7cc); box-shadow: 0 -6px 10px -6px rgba(60, 40, 20, .35); border-radius: 0 0 .5rem .5rem; }
.sg-shop-actions .sg-btn { min-height: 44px; flex: 1 1 0; min-width: 0; padding: .2rem .45rem; font-size: .92rem; line-height: 1.15; white-space: normal; }
.sg-shop-price { font-variant-numeric: tabular-nums; color: var(--sg-gold, #f3c969); }
.sg-shop-book { flex: 1; min-height: 0; overflow-y: auto; -webkit-overflow-scrolling: touch; display: flex; flex-direction: column; gap: .7rem; }
.sg-shop-page h4 { margin: 0 0 .3rem; font-family: var(--sg-font-title); font-weight: normal; font-size: 1.15rem; color: var(--sg-gold, #f3c969); }
.sg-shop-page h4 small { font-family: var(--sg-font-body); font-size: .8rem; color: var(--sg-muted, #ada392); margin-left: .5rem; }
.sg-shop-page .sg-shop-pgrid { display: flex; flex-wrap: wrap; gap: .3rem; }
.sg-shop-page .sg-shop-icon { --sz: 34px; cursor: pointer; }
.sg-shop-reveal { position: fixed; inset: 0; z-index: 1500; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 1rem; pointer-events: auto;
  background: radial-gradient(ellipse at 50% 45%, rgba(40,34,30,.92), rgba(10,9,12,.97)); color: var(--sg-text, #f5eddb); animation: sg-shop-ink .5s ease-out both; }
.sg-shop-reveal .sg-shop-icon { --sz: 150px; }
.sg-shop-reveal .sg-shop-rname { font-family: var(--sg-font-title); font-size: 2.6rem; color: var(--rc, #f3c969); text-shadow: 0 0 18px var(--rc); }
.sg-shop-reveal .sg-shop-rstamp { position: absolute; right: 12%; top: 18%; padding: .2rem .6rem; border: 3px solid #c8382c; color: #c8382c; font-family: var(--sg-font-title); font-size: 2.2rem;
  transform: rotate(-12deg); animation: sg-shop-stamp .4s .7s cubic-bezier(.2,.8,.2,1.2) both; }
.sg-shop-reveal .sg-shop-rline { font-size: 1.05rem; color: #e9dcc0; max-width: 40rem; text-align: center; padding: 0 1rem; }
@keyframes sg-shop-ink { from { opacity: 0; filter: blur(8px); } to { opacity: 1; filter: none; } }
@keyframes sg-shop-stamp { from { opacity: 0; transform: rotate(-12deg) scale(2.4); } to { opacity: 1; transform: rotate(-12deg) scale(1); } }
@media (max-height: 540px) {
  .sg-dialog.sg-shop { padding: .45rem .6rem .5rem; gap: .3rem; }
  .sg-shop-title { font-size: 1.3rem; }
  .sg-shop-body { grid-template-columns: 1fr 15.5rem; gap: .45rem; }
  .sg-shop-keeper { display: none; }
  .sg-shop-grid { grid-template-columns: repeat(auto-fill, minmax(7.4rem, 1fr)); gap: .35rem; }
  .sg-shop-tile { min-height: 48px; padding: .25rem .35rem; }
  .sg-shop-tile .sg-shop-tname { -webkit-line-clamp: 1; }
  .sg-shop-icon { --sz: 36px; }
  .sg-shop-who .sg-portrait { display: none; }
  .sg-shop-tabs .sg-btn { min-width: 4rem; }
  /* 手机横屏：选中的货（详情卡与「购买」）排在武将条下面、装备位之上，不必往下滚才看到 */
  .sg-shop-side > .sg-shop-who, .sg-shop-side > .sg-shop-slots { order: 2; }
  .sg-shop-side > .sg-shop-card, .sg-shop-side > .sg-shop-note { order: 1; }
  .sg-shop-gens { flex-wrap: nowrap; overflow-x: auto; -webkit-overflow-scrolling: touch; flex: none; }
  .sg-shop-gens .sg-btn { flex: none; }
  .sg-shop-chead .sg-shop-icon { --sz: 44px; }
  .sg-shop-cname { font-size: 1.1rem; }
  .sg-shop-actions { margin: .35rem -.35rem -.2rem; padding: .3rem .35rem .25rem; }
  .sg-shop-actions .sg-btn { padding: .15rem .35rem; font-size: .85rem; }
}
@media (prefers-reduced-motion: reduce) {
  .sg-shop-icon.r-epic::after, .sg-shop-icon.r-legendary::after { animation: none; display: none; }
  .sg-shop-card.is-bought, .sg-shop-reveal, .sg-shop-reveal .sg-shop-rstamp { animation: none !important; }
}`;
  function injectCss() {
    if (typeof document === 'undefined' || document.getElementById('sg-shop-style')) return;
    const st = document.createElement('style');
    st.id = 'sg-shop-style';
    st.textContent = CSS;
    document.head.appendChild(st);
  }

  // ------------------------------------------------------------------ 图标与文字 --
  const rarityColor = r => { const R = D() && D().rarities[r]; return R ? R.color : '#9aa3ad'; };
  function icon(id, size, opts) {
    opts = opts || {};
    const it = I() && I().get(id);
    if (!it) return '<span class="sg-shop-icon is-unseen" style="--sz:' + (size || 44) + 'px">？</span>';
    if (typeof ShopScreen.iconHook === 'function') { try { const h = ShopScreen.iconHook(id, size); if (h) return h; } catch (e) { /* 占位 */ } }
    if (!opts.unseen && SG.ItemArt && typeof SG.ItemArt.icon === 'function') {
      try { const h = SG.ItemArt.icon(id, size || 44); if (typeof h === 'string') return h; if (h && h.outerHTML) return h.outerHTML; } catch (e) { /* 占位 */ }
    }
    const ic = (it.visual && it.visual.icon) || {};
    const c = ic.colors || ['#8e939c', '#5a5f68', '#3a3a44'];
    const glyph = TYPE_GLYPH[it.type] || SLOT_GLYPH[it.slot] || it.name.charAt(0);
    if (opts.unseen) return '<span class="sg-shop-icon is-unseen" style="--sz:' + (size || 44) + 'px" data-item="' + esc(id) + '">？</span>';
    return '<span class="sg-shop-icon r-' + it.rarity + '" data-item="' + esc(id) + '" style="--sz:' + (size || 44) + 'px;--c1:' + c[0] + ';--c2:' + (c[2] || c[1] || c[0]) +
      ';--rc:' + rarityColor(it.rarity) + (ic.glow ? ';--glow:' + ic.glow : '') + '">' + esc(glyph) + '</span>';
  }
  function statsText(it, opts) {
    const K = D().statKeys;
    const parts = [];
    for (const [k, v] of Object.entries(it.stats || {})) {
      const sk = K[k];
      if (Array.isArray(v)) { parts.push((sk ? sk.name : k) + ' ' + v.join('/')); continue; }
      if (typeof v !== 'number') continue;
      if (k === 'gift' && v === 1) { parts.push('宜作礼物'); continue; }   // 馈赠倍率 ×1：珍玩本身就是礼物（价值按原价算）
      const u = !sk ? '' : sk.unit === '%' ? '%' : sk.unit === 's' ? ' 秒' : sk.unit === 'm' ? ' 米' : '';
      const val = sk && sk.unit === 'x' ? '×' + v : (v > 0 ? '+' : '') + v;
      parts.push((sk ? sk.name : k) + ' ' + val + u);
    }
    return parts.join(opts && opts.sep ? opts.sep : ' · ') || '—';
  }
  function reqText(gen, it) {
    const r = it.req; if (!r) return '';
    const nm = { war: '武力', intel: '智力', pol: '政治' };
    const out = [];
    for (const k of ['war', 'intel', 'pol']) if (r[k]) {
      const okv = gen ? (gen[k] | 0) >= r[k] : true;
      out.push('<span class="' + (okv ? 'sg-good' : 'sg-bad') + '">需' + nm[k] + ' ' + r[k] + (r.below === 'half' ? '（不足时减半）' : '') + '</span>');
    }
    if (r.weaponStyle === 'one') out.push('<span class="sg-muted">须配单手兵器</span>');
    if (r.ruler) out.push('<span class="' + (gen && SG.G && SG.G.isRuler(gen) ? 'sg-good' : 'sg-bad') + '">须君主佩持</span>');
    return out.join(' · ');
  }
  const SRC = { history: '史实', archaeology: '考古', myth: '神话', novel: '演义', game: '游戏取舍', legend: '传说' };
  function card(id, opts) {
    opts = opts || {};
    const it = I().get(id); if (!it) return '';
    const R = D().rarities[it.rarity], T = D().types[it.type] || {};
    const sub = R.name + '（' + R.alias + '）· ' + (D().slots[it.slot] || {}).name + '·' + (T.name || it.type) + ' · ' + ((D().cultures[it.culture] || {}).name || it.culture);
    let h = '<div class="sg-shop-card r-' + it.rarity + (opts.bought ? ' is-bought' : '') + '" style="--rc:' + R.color + '">';
    h += '<div class="sg-shop-chead">' + icon(id, 56) + '<div><div class="sg-shop-cname">' + esc(I().displayName(it)) + (it.orig ? ' <small class="sg-muted" style="font-size:.7em">' + esc(it.orig) + '</small>' : '') +
      '</div><div class="sg-shop-csub">' + esc(sub) + '</div></div></div>';
    h += '<div class="sg-shop-stats">' + esc(statsText(it)) + '</div>';
    const rq = reqText(opts.gen, it); if (rq) h += '<div class="sg-shop-req">' + rq + '</div>';
    if (opts.gen && opts.compare !== false) h += compareHtml(opts.gen, it);
    if (it.ability) h += '<div class="sg-shop-ability"><b>「' + esc(it.ability.name) + '」</b>' + esc(it.ability.desc) + '</div>';
    if (it.source) h += '<div class="sg-shop-lore">' + (SRC[it.source.kind] ? '【' + SRC[it.source.kind] + '】' : '') + esc(it.source.note || '') + (it.source.refs && it.source.refs.length ? '（' + esc(it.source.refs.slice(0, 2).join('；')) + '）' : '') + '</div>';
    if (opts.extra) h += opts.extra;
    h += '</div>';
    return h;
  }
  // 与该武将同一部位现有装备的比较（属性差：绿 / 红）
  function compareHtml(gen, it) {
    const cur = Gr().item(gen, it.slot);
    if (cur && cur.id === it.id) return '<div class="sg-shop-delta sg-muted">' + esc(gen.name) + '正佩着此物</div>';
    const K = D().statKeys, keys = new Set(Object.keys(it.stats || {}).concat(Object.keys((cur && cur.stats) || {})));
    const bad = new Set(['fireWeak']);
    const out = [];
    for (const k of keys) {
      const a = (cur && cur.stats && typeof cur.stats[k] === 'number') ? cur.stats[k] : 0, b = typeof it.stats[k] === 'number' ? it.stats[k] : 0;
      const d = Math.round((b - a) * 1000) / 1000;
      if (!d) continue;
      const good = bad.has(k) ? d < 0 : d > 0;
      const sk = K[k] || {}, u = sk.unit === '%' ? '%' : '';
      out.push('<span class="' + (good ? 'up' : 'down') + '">' + esc(sk.name || k) + (d > 0 ? ' +' : ' ') + d + u + '</span>');
    }
    const sc = SG.Items.score(gen, it.id) - (cur ? SG.Items.score(gen, cur.id) : 0);
    return '<div class="sg-shop-delta">对比' + esc(gen.name) + '的' + (cur ? '「' + esc(I().displayName(cur)) + '」' : '空位') + '：' + (out.length ? out.join(' · ') : '<span class="sg-muted">数值相同</span>') +
      ' <span class="sg-muted">（估值 ' + (sc >= 0 ? '+' : '') + Math.round(sc) + '）</span></div>';
  }

  // ------------------------------------------------------------------ 画面 --
  const ShopScreen = { iconHook: null, current: null };
  function officersOf(st) {
    const g = SG.G;
    let list = st.city && st.city.owner === st.f ? g.officersIn(st.city) : [];
    if (!list.length) list = g.generalsOf(st.f);
    return list.slice(0, 24);
  }
  function genOk(st) { const g = SG.G; if (!st.gen || st.gen.faction !== st.f || st.gen.dead) { const l = officersOf(st); st.gen = l.length ? l.find(x => x.id === (st.city ? st.city.governor : -1)) || l[0] : null; } }
  function goldOf(st) { const pay = st.city ? SG.Items.payCityFor(st.f, st.city) : null; return pay ? pay.gold : 0; }
  function render(st) {
    const g = SG.G, el = st.el, Iv = I();
    genOk(st);
    el.innerHTML = '';
    const c = st.city;
    const tier = c ? Iv.shopTier(c) : 0;
    // 头
    const head = UI().el('div', 'sg-shop-head');
    head.innerHTML = '<span class="sg-shop-title">' + (c ? esc(c.name) + ' · 市场' : '府库') + '</span>' +
      (c ? '<span class="sg-shop-tier">' + TIER_NAME[tier] + ' · <b>' + Iv.slots(c) + '</b> 格 · 城价 ×' + Iv.cityK(c).toFixed(2) + (g.month === 3 ? ' · <span class="sg-goldc">庙会</span>' : '') + '</span>' : '') +
      '<span class="sg-spacer"></span><span class="sg-chip sg-shop-gold"><i>金</i><b>' + goldOf(st) + '</b></span>' +
      '<span class="sg-chip"><i>府库</i><b>' + Iv.armoury(st.f, { vault: false }).length + '/' + Iv.CFG.armouryCap + '</b></span>';
    const close = UI().button(head, '×', () => ShopScreen.close(), { className: 'sg-close', ariaLabel: '关闭' });
    close.style.minWidth = '44px'; close.style.minHeight = '44px';
    el.appendChild(head);
    const tabs = UI().el('div', 'sg-shop-tabs');
    for (const [k, name] of TABS) { const b = UI().button(tabs, name, () => { st.tab = k; st.sel = null; st.msg = ''; render(st); }, { small: true, className: st.tab === k ? 'is-on' : '' }); b.dataset.tab = k; }
    el.appendChild(tabs);
    const body = UI().el('div', 'sg-shop-body');
    el.appendChild(body);
    // 左：店主
    const keeper = UI().el('div', 'sg-shop-keeper');
    keeper.innerHTML = '<div class="sg-shop-seal">' + (st.tab === 'book' ? '鉴' : st.tab === 'armoury' ? '库' : '市') + '</div>' +
      '<div class="sg-shop-banner">' + (c ? TIER_NAME[tier] : '府库') + '</div>' +
      '<div class="sg-shop-line">' + esc(keeperLine(st)) + '</div>';
    body.appendChild(keeper);
    // 货架与详情两栏是纸面（W8 的皮肤）：is-paper 把语义色（正文、<b>、金色字）换成墨色系，保证 ≥ 4.5 : 1（UI-V4）
    const main = UI().el('div', 'sg-shop-main is-paper');
    body.appendChild(main);
    const side = UI().el('div', 'sg-shop-side is-paper');
    body.appendChild(side);
    if (st.tab === 'buy') renderBuy(st, main);
    else if (st.tab === 'sell') renderSell(st, main);
    else if (st.tab === 'armoury') renderArmoury(st, main);
    else renderBook(st, main);
    renderSide(st, side);
  }
  function keeperLine(st) {
    const c = st.city;
    if (st.tab === 'book') return '天下兵器甲胄，见过的、得过的都记在这里。';
    if (st.tab === 'armoury') return '府库里的东西，可以装备、赏赐，也可以变卖。';
    if (!c) return '';
    const why = I().canTrade(st.f, c);
    if (why) return why;
    if (c.shop.merchant && c.shop.merchant.until > SG.G.monthIndex) return '远方的商人带来了货物，价钱高些，东西稀罕。';
    return c.owner === st.f ? '客官看看，新到的货色都在这里了。' : '同盟的商人：价钱高一成五。';
  }
  function tile(parent, id, sub, sel, onClick, extra) {
    const it = I().get(id);
    const b = UI().el('button', 'sg-shop-tile' + (sel ? ' is-sel' : '') + (extra && extra.off ? ' is-off' : ''));
    b.type = 'button';
    b.style.setProperty('--rc', rarityColor(it.rarity));
    b.innerHTML = icon(id, 44) + '<span><span class="sg-shop-tname">' + esc(I().displayName(it)) + '</span><span class="sg-shop-tsub">' + sub + (extra && extra.tag ? '<span class="sg-shop-tag">' + extra.tag + '</span>' : '') + '</span></span>';
    b.addEventListener('click', () => onClick());
    parent.appendChild(b);
    return b;
  }
  function renderBuy(st, main) {
    const c = st.city, Iv = I(), g = SG.G;
    if (!c) { main.innerHTML = '<div class="sg-shop-note">没有市场。</div>'; return; }
    const why = Iv.canTrade(st.f, c);
    const note = UI().el('div', 'sg-shop-note', why ? esc(why) + '（只能看，不能买）' : '金从' + esc((Iv.payCityFor(st.f, c) || c).name) + '出；买下的东西进府库，可以立即装备。' + (st.msg ? ' <b class="sg-goldc">' + st.msg + '</b>' : ''));
    main.appendChild(note);
    const grid = UI().el('div', 'sg-shop-grid');
    main.appendChild(grid);
    const add = (list, merch) => list.forEach((e, i) => {
      Iv.markSeen && st.f === g.player && Iv.markSeen(e.id);
      const price = why ? e.price : Iv.entryPrice(c, merch ? Object.assign({ merchant: true }, e) : e, st.f);
      const sel = st.sel && st.sel.kind === 'stock' && st.sel.i === i && !!st.sel.merch === merch;
      tile(grid, e.id, '<span class="sg-shop-price">' + price + ' 金</span>' + (e.qty > 1 ? ' ×' + e.qty : ''), sel, () => { st.sel = { kind: 'stock', i, merch, id: e.id }; st.msg = ''; render(st); },
        { tag: merch ? '胡商' : e.foreign ? '舶来' : '' });
    });
    add(c.shop.stock, false);
    if (c.shop.merchant && c.shop.merchant.until > g.monthIndex) add(c.shop.merchant.stock, true);
    if (!c.shop.stock.length) grid.appendChild(UI().el('div', 'sg-shop-note', '货已卖空，下月再来。'));
  }
  function renderSell(st, main) {
    const Iv = I(), c = st.city;
    main.appendChild(UI().el('div', 'sg-shop-note', '出售府库中的装备' + (c && c.owner === st.f ? '（在' + esc(c.name) + '的市场，金入本城）' : '') + '。神品、天宝、专属与名将旧物不可变卖。' + (st.msg ? ' <b class="sg-goldc">' + st.msg + '</b>' : '')));
    const grid = UI().el('div', 'sg-shop-grid');
    main.appendChild(grid);
    const list = Iv.armoury(st.f, { vault: false });
    if (!list.length) grid.appendChild(UI().el('div', 'sg-shop-note', '府库是空的。'));
    for (const u of list) {
      const r = Iv.inst(u), it = Iv.get(r.id);
      const ok = Iv.sellable(r.id, u).ok;
      const price = ok ? Iv.sellPrice(u, c && c.owner === st.f ? c : null) : 0;
      tile(grid, r.id, ok ? '<span class="sg-shop-price">卖 ' + price + ' 金</span>' : '<span class="sg-muted">不可变卖</span>', st.sel && st.sel.uid === u, () => { st.sel = { kind: 'inst', uid: u, id: r.id }; st.msg = ''; render(st); }, { off: !ok });
    }
  }
  function renderArmoury(st, main) {
    const Iv = I();
    main.appendChild(UI().el('div', 'sg-shop-note', '府库与本势力各城的镇城之宝。选一件，可装备或赏赐给右边选中的武将。' + (st.msg ? ' <b class="sg-goldc">' + st.msg + '</b>' : '')));
    const grid = UI().el('div', 'sg-shop-grid');
    main.appendChild(grid);
    const list = Iv.armoury(st.f);
    if (!list.length) grid.appendChild(UI().el('div', 'sg-shop-note', '府库是空的。'));
    const slotF = st.slotFilter;
    for (const u of list) {
      const r = Iv.inst(u), it = Iv.get(r.id);
      if (slotF && it.slot !== slotF) continue;
      const tag = r.loc === 'city' ? '镇' : '';
      const ce = st.gen ? SG.Gear.canEquip(st.gen, u) : { ok: true };
      tile(grid, r.id, Iv.SLOT_NAME[it.slot] + (ce.ok ? '' : ' · <span class="sg-bad">' + esc(ce.why || '') + '</span>'), st.sel && st.sel.uid === u, () => { st.sel = { kind: 'inst', uid: u, id: r.id }; st.msg = ''; render(st); }, { tag, off: !ce.ok });
    }
  }
  function renderBook(st, main) {
    const col = I().collection(st.f);
    main.appendChild(UI().el('div', 'sg-shop-note', '图鉴：见过 <b>' + col.seen + '</b> / ' + col.total + '，得过 <b>' + col.owned + '</b>；集齐一种文化的凡品与良品，君主人望 +1（至多 +' + I().CFG.collection.fameMax + '，现 +' + col.fame + '）。神品天宝见过 ' + col.epicSeen + ' / ' + col.epicTotal + '。'));
    const book = UI().el('div', 'sg-shop-book');
    main.appendChild(book);
    for (const p of col.pages) {
      const sec = UI().el('div', 'sg-shop-page');
      sec.innerHTML = '<h4>' + esc(p.name) + '<small>见过 ' + p.seen + ' / ' + p.total + (p.complete ? ' · 集齐' : '') + '</small></h4>';
      const pg = UI().el('div', 'sg-shop-pgrid');
      for (const x of p.items) {
        const span = UI().el('span', '', icon(x.id, 34, { unseen: !x.seen }));
        const ic = span.firstChild;
        if (x.seen && ic) { ic.title = I().displayName(I().get(x.id)); ic.addEventListener('click', () => { st.sel = { kind: 'book', id: x.id }; render(st); }); }
        pg.appendChild(ic || span);
      }
      sec.appendChild(pg);
      book.appendChild(sec);
    }
  }
  function renderSide(st, side) {
    const Iv = I(), Gv = Gr(), g = SG.G, gen = st.gen;
    // 武将条
    const gens = UI().el('div', 'sg-shop-gens');
    for (const x of officersOf(st)) UI().button(gens, esc(x.name), () => { st.gen = x; render(st); }, { small: true, className: gen === x ? 'is-on' : '' });
    side.appendChild(gens);
    if (gen) {
      const who = UI().el('div', 'sg-shop-who');
      if (SG.Portrait && typeof SG.Portrait.el === 'function') { try { who.appendChild(SG.Portrait.el(gen, { size: 56 })); } catch (e) { /* 无头像 */ } }
      const sm = Gv.summary(gen);
      who.appendChild(UI().el('div', '', '<div class="sg-shop-whoname">' + esc(gen.name) + '</div><div class="sg-shop-whostats">武 ' + sm.eff.war + (sm.eff.war !== gen.war ? '<small>（' + gen.war + '）</small>' : '') +
        ' 智 ' + sm.eff.intel + ' 政 ' + sm.eff.pol + ' · 忠 ' + gen.loyalty + '<br>攻 ×' + sm.army.atk.toFixed(2) + ' 防 ×' + sm.army.def.toFixed(2) + ' 会心 ' + Math.round(sm.army.crit * 100) + '%<br>单挑 武 ' + sm.duel.war + ' 体 ' + Math.round(sm.duel.hp) + '</div>'));
      side.appendChild(who);
      const slots = UI().el('div', 'sg-shop-slots');
      const sv = Gv.slots(gen), locks = new Set(sm.locks.map(l => l.slot));
      for (const sl of Iv.SLOTS) {
        const v = sv[sl];
        const b = UI().el('button', 'sg-shop-slot' + (st.slotFilter === sl ? ' is-sel' : '') + (locks.has(sl) ? ' is-lock' : ''));
        b.type = 'button';
        if (v) b.style.setProperty('--rc', rarityColor(v.item.rarity));
        b.innerHTML = (v ? icon(v.id, 34) : '<span class="sg-shop-icon is-unseen" style="--sz:34px">' + SLOT_GLYPH[sl] + '</span>') + '<span class="sg-shop-sname">' + (v ? esc(Iv.displayName(v.item)) : Iv.SLOT_NAME[sl] + ' · 空') + '</span>';
        b.addEventListener('click', () => {
          if (v) { st.sel = { kind: 'inst', uid: v.uid, id: v.id, equipped: true }; }
          st.slotFilter = st.slotFilter === sl ? null : sl;
          if (st.tab !== 'armoury' && !v) st.tab = 'armoury';
          render(st);
        });
        slots.appendChild(b);
      }
      side.appendChild(slots);
    }
    // 选中的装备：详情 + 操作
    if (st.sel) {
      const id = st.sel.id;
      const wrap = UI().el('div', '');
      wrap.innerHTML = card(id, { gen, bought: st.justBought === st.sel.uid && st.sel.uid != null });
      const cardEl = wrap.firstChild;
      const acts = UI().el('div', 'sg-shop-actions');
      actionsFor(st, acts);
      if (cardEl) {
        // 按钮放在属性与比较之后、能力说明与典据之前（长卡在短屏上不必滚到底才看到「购买」）
        const after = cardEl.querySelector('.sg-shop-ability') || cardEl.querySelector('.sg-shop-lore');
        if (after) cardEl.insertBefore(acts, after); else cardEl.appendChild(acts);
        side.appendChild(cardEl);
        if (st.justBought != null && st.sel.uid === st.justBought) st.justBought = null;   // 购买的翻牌只播一次
      }
    } else side.appendChild(UI().el('div', 'sg-shop-note', '点一件货看详情；点右上的武将切换比较对象；点装备位筛选府库。'));
  }
  // 赐宝物的结果：换装的代价与赏赐所得分开写（忠诚 100 → 99（替换「青龙偃月刀」−15，赏赐 +14））
  function giftMsg(gen, r, html) {
    const e = html ? esc : (x => x);
    if (!r || !r.ok) return e(r ? r.why : '');
    let m = gen.name + '受赐「' + I().displayName(r.item) + '」，忠诚 ' + r.before + ' → ' + r.after;
    if (r.eqDelta) m += '（替换「' + (r.displaced || []).map(id => I().displayName(id)).join('、') + '」−' + Math.abs(r.eqDelta) + '，赏赐 +' + r.loy + '）';
    else if (r.loy === 0) m += '（此物赐过，不再涨忠诚）';
    return e(m) + (r.line ? (html ? '<br>' : ' ') + e(r.line) : '');
  }
  // 会顶掉名将旧物或更好的东西时先问（ITEMS §2.6；赐出的东西总是装备上）
  async function giftFlow(gen, uid) {
    const Iv = I();
    let r = Iv.gift(gen, uid);
    if (r && r.needConfirm) {
      const yes = SG.UI && SG.UI.confirm ? await SG.UI.confirm(esc(r.why) + '<br>仍要赐出吗？', '赐出', '再想想') : true;
      if (!yes) return null;
      r = Iv.gift(gen, uid, { confirm: true });
    }
    return r;
  }
  // 换上双手 / 双持 / 扇时，盾要收回府库（ITEMS §2.2「UI asks」）：返回会被卸下的盾（没有则 null）
  function shieldOff(gen, id) {
    const Iv = I(), it = Iv.get(id);
    if (!gen || !it || it.slot !== 'weapon' || Iv.styleOf(it) === 'one') return null;
    const sh = SG.Gear.item(gen, 'offhand'), u = gen.gear ? gen.gear.offhand : null;
    if (!sh || !Iv.isShield(sh) || (u != null && Iv.inst(u) && Iv.inst(u).bound)) return null;
    return sh;
  }
  async function shieldOk(gen, id) {
    const sh = shieldOff(gen, id);
    if (!sh || !SG.UI || !SG.UI.confirm) return true;
    return SG.UI.confirm('「' + esc(I().displayName(id)) + '」须双手持握，' + esc(gen.name) + '的盾「' + esc(I().displayName(sh)) + '」将收回府库。', '换上', '取消');
  }
  function actionsFor(st, acts) {
    const Iv = I(), Gv = Gr(), g = SG.G, gen = st.gen, s = st.sel, c = st.city;
    const done = (m) => { st.msg = m; render(st); };
    if (s.kind === 'stock') {
      const why = Iv.canTrade(st.f, c);
      const list = s.merch ? (c.shop.merchant ? c.shop.merchant.stock : []) : c.shop.stock;
      const e = list[s.i];
      if (!e) return;
      const price = Iv.entryPrice(c, s.merch ? Object.assign({ merchant: true }, e) : e, st.f);
      const buyAnd = async equipTo => {
        if (equipTo && !(await shieldOk(equipTo, s.id))) return;
        const r = Iv.buy(st.f, c, s.i, { merchant: !!s.merch });
        if (!r.ok) { done(r.why || '买不成。'); return; }
        playSfx('coins');
        let m = '购得「' + Iv.displayName(r.item) + '」（−' + r.price + ' 金）';
        if (equipTo) { const q = Gv.equip(equipTo, r.uid); m += q.ok ? '，' + equipTo.name + '装备上了' + (q.loyaltyDelta ? '（忠诚 ' + q.loyaltyDelta + '）' : '') : '，存入府库（' + q.why + '）'; }
        else m += '，存入府库';
        st.justBought = r.uid; st.sel = { kind: 'inst', uid: r.uid, id: r.item.id };
        if (SG.UI && SG.UI.toast) SG.UI.toast(esc(m));
        done(esc(m));
      };
      UI().button(acts, '购买 <span class="sg-shop-price">' + price + ' 金</span>', () => buyAnd(null), { primary: true, disabled: !!why });
      if (gen) UI().button(acts, '买下装备给' + esc(gen.name), () => buyAnd(gen), { disabled: !!why || !Gv.canEquipItem(gen, s.id).ok });
      return;
    }
    if (s.kind === 'inst') {
      const r = Iv.inst(s.uid);
      if (!r) return;
      const holder = Iv.genOf(s.uid);
      if (holder && gen && holder === gen) {
        UI().button(acts, '卸下', () => { const q = Gv.unequip(gen, Iv.get(r.id).slot); done(q.ok ? '已卸下' + (q.loyaltyDelta ? '（忠诚 ' + q.loyaltyDelta + '）' : '') : q.why); }, { disabled: r.bound });
        return;
      }
      if (gen) {
        const ce = Gv.canEquip(gen, s.uid);
        UI().button(acts, '装备给' + esc(gen.name), async () => {
          if (!(await shieldOk(gen, r.id))) return;
          const q = Gv.equip(gen, s.uid); playSfx('equip');
          done(q.ok ? esc(gen.name + '装备上了「' + Iv.displayName(Iv.get(r.id)) + '」' + (q.loyaltyDelta ? '（忠诚 ' + q.loyaltyDelta + '）' : '')) : esc(q.why));
        }, { primary: true, disabled: !ce.ok });
        if (!g.isRuler(gen)) UI().button(acts, r.gifted == null ? '赐给' + esc(gen.name) : '赐给' + esc(gen.name) + '（已赐过）', async () => {
          const q = await giftFlow(gen, s.uid);
          if (q) done(giftMsg(gen, q, true));
        }, { disabled: !ce.ok });
      }
      if (!holder) {
        const ok = Iv.sellable(r.id, s.uid).ok && c && c.owner === st.f;
        UI().button(acts, '变卖 ' + (ok ? Iv.sellPrice(s.uid, c) + ' 金' : ''), async () => {
          const it = Iv.get(r.id);
          if (Iv.tier(it) >= 1 && SG.UI && SG.UI.confirm && !(await SG.UI.confirm('变卖「' + esc(Iv.displayName(it)) + '」，得 ' + Iv.sellPrice(s.uid, c) + ' 金？'))) return;
          const q = Iv.sell(st.f, s.uid, c);
          st.sel = null;
          done(q.ok ? '卖得 ' + q.gold + ' 金' : q.why);
        }, { disabled: !ok });
      }
    }
  }
  // 音效：SG.Audio.play（coin / hit）；ITEMS 的 item-* 音效登记之后（AUDIO 工作流）换成各自的
  function playSfx(kind) { const A = SG.Audio; try { if (A && typeof A.play === 'function') A.play(kind === 'coins' ? 'coin' : 'hit', 0.6); } catch (e) { /* 无音效 */ } }

  ShopScreen.open = function (opts) {
    opts = opts || {};
    const g = SG.G;
    if (!g || !SG.Items || !SG.Items.enabled || !g.items) { if (SG.UI && SG.UI.toast) SG.UI.toast('装备系统未开启。'); return Promise.resolve(); }
    injectCss();
    if (ShopScreen.current) ShopScreen.close();
    const m = UI().openModal('sg-shop', { width: 1056 });
    const st = { city: opts.city || null, f: opts.f !== undefined ? opts.f : g.player, gen: opts.gen || null, tab: opts.tab || (opts.city ? 'buy' : 'armoury'), sel: null, msg: '', slotFilter: null, el: m.panel, modal: m };
    ShopScreen.current = st;
    m.onKey = k => { if (k === 'Escape') { ShopScreen.close(); return true; } return false; };
    render(st);
    return new Promise(res => { st.resolve = res; });
  };
  ShopScreen.close = function () {
    const st = ShopScreen.current;
    if (!st) return;
    ShopScreen.current = null;
    st.modal.close();
    if (st.resolve) st.resolve();
  };
  ShopScreen.refresh = function () { if (ShopScreen.current) render(ShopScreen.current); };
  ShopScreen.armoury = function (opts) { opts = opts || {}; return ShopScreen.open({ f: opts.f, gen: opts.gen, tab: 'armoury', city: opts.city || null }); };
  // 赏赐「赐宝物」：从府库挑一件
  ShopScreen.gift = async function (gen) {
    const Iv = I(), g = SG.G;
    if (!gen || !Iv || !Iv.enabled) return null;
    const list = Iv.armoury(gen.faction).filter(u => SG.Gear.canEquip(gen, u).ok);
    if (!list.length) { if (SG.UI.notice) await SG.UI.notice('赐宝物', '府库里没有' + esc(gen.name) + '能用的装备。'); return null; }
    // 每件写明会替换掉什么（赐 = 给他装上；替换名将旧物要扣忠诚，ITEMS §2.6），空位或估值更高的排在前面
    const rows = list.map(u => {
      const r = Iv.inst(u), it = Iv.get(r.id), cur = SG.Gear.item(gen, it.slot), curU = gen.gear ? gen.gear[it.slot] : null;
      const tc = curU != null && Iv.takeCost ? Iv.takeCost(gen, curU, u) : { cost: 0 };
      const d = Iv.score(gen, it.id) - (cur ? Iv.score(gen, cur.id) : 0);
      const rep = cur ? ' · <span class="' + (tc.cost ? 'sg-bad' : 'sg-muted') + '">替换「' + esc(Iv.displayName(cur)) + '」' + (tc.cost ? '（忠诚 −' + tc.cost + '）' : '') + '</span>' : ' · <span class="sg-good">空位</span>';
      return { u, r, it, d: d - (tc.cost || 0) * 2 + (cur ? 0 : 5), rep };
    }).sort((a, b) => b.d - a.d);
    list.length = 0; for (const x of rows) list.push(x.u);
    const items = rows.map(({ r, it, rep }) => SG.UI.item(icon(r.id, 28) + ' <span style="color:' + rarityColor(it.rarity) + '">' + esc(Iv.displayName(it)) + '</span>', r.gifted != null ? '<span class="sg-muted">已赐过</span>' : '', true, esc(statsText(it)) + rep));
    injectCss();
    const k = await SG.UI.choose('赐给' + gen.name + '哪一件？', items, '每件实物只涨一次忠诚；收回时扣回同样多。', 720);
    if (k < 0) return null;
    const r = await giftFlow(gen, list[k]);
    if (!r) return null;
    if (SG.UI.notice) await SG.UI.notice('赐宝物', giftMsg(gen, r, true));
    return r;
  };
  // 神品 / 天宝的揭示（ITEMS §5.5；占位：墨晕 + 图标 + 朱印）
  ShopScreen.reveal = function (rv) {
    injectCss();
    const it = I().get(rv.id);
    if (!it || typeof document === 'undefined') return Promise.resolve();
    const R = D().rarities[it.rarity];
    const root = document.createElement('div');
    root.className = 'sg-shop-reveal';
    root.style.setProperty('--rc', R.color);
    root.innerHTML = icon(it.id, 150) + '<div class="sg-shop-rname">' + esc(I().displayName(it)) + '</div>' +
      '<div class="sg-shop-rline">' + esc(it.ability ? '「' + it.ability.name + '」' + it.ability.desc : statsText(it)) + '</div>' +
      (rv.from ? '<div class="sg-shop-rline sg-muted">' + esc(rv.from) + (rv.how === 'hidden' ? '寻得' : rv.how === 'executed' || rv.how === 'slain' ? '处得来' : '') + '</div>' : '') +
      '<div class="sg-shop-rstamp">' + esc(R.alias) + '</div>';
    const host = (SG.UI && SG.UI.layer && SG.UI.layer('modals')) || document.body;
    host.appendChild(root);
    return new Promise(res => {
      const done = () => { root.removeEventListener('pointerdown', done); window.removeEventListener('keydown', key, true); if (root.parentNode) root.parentNode.removeChild(root); res(); };
      const key = e => { e.preventDefault(); done(); };
      setTimeout(() => { root.addEventListener('pointerdown', done); window.addEventListener('keydown', key, true); }, reduced() ? 0 : 400);
      setTimeout(done, 6000);
    });
  };
  ShopScreen.icon = icon;
  ShopScreen.card = card;
  ShopScreen.statsText = statsText;
  ShopScreen.giftMsg = giftMsg;      // 测试与人物菜单用：赐宝物结果的文字（换装代价与赏赐所得分开）
  ShopScreen.shieldOff = shieldOff;  // 换上双手兵器会卸下的盾（没有则 null）
  ShopScreen.TYPE_GLYPH = TYPE_GLYPH;
  SG.ShopScreen = ShopScreen;

  // Gear.canEquipItem：按物品编号判断（买下之前就能告诉玩家能不能装备）
  if (SG.Gear && !SG.Gear.canEquipItem) {
    SG.Gear.canEquipItem = function (gen, id) {
      const it = SG.Items.get(id); if (!it) return { ok: false, why: '无此物' };
      const r = it.req || {};
      for (const k of ['war', 'intel', 'pol']) if (r[k] && (gen[k] | 0) < r[k] && r.below !== 'half') return { ok: false, why: '需' + { war: '武力', intel: '智力', pol: '政治' }[k] + ' ' + r[k] };
      const w = SG.Gear.item(gen, 'weapon');
      if (SG.Items.isShield(it) && SG.Items.styleOf(w) !== 'one') return { ok: false, why: '盾须配单手兵器' };
      const cur = gen.gear && gen.gear[it.slot] != null ? SG.Items.inst(gen.gear[it.slot]) : null;
      if (cur && cur.bound) return { ok: false, why: '此位已有专属兵器' };
      return { ok: true };
    };
  }
})();
