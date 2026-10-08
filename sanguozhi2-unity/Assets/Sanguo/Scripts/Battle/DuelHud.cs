// ==========================================================================
// 三国志II 霸王的大陆 · 单挑格斗 · 界面与操作（网页版 js/duel-game.js「HUD / 输入 / 触摸按键」与样式表 .sgd-* 的移植）
//
// 尺寸按网页版的 CSS 计算：--u = clamp(10px, min(1.2vw, 2.15vh), 18px)、--b（触摸按键）= clamp(54px, min(15.5vh, 18vw), 86px)，
// 以「CSS 像素」（移动设备 = dpi / 160，桌面 = dpi / 96）换算成画布单位；安全区由 Screen.safeArea 得出。
// 渐变文字、模糊、粗糙笔触滤镜等 CSS 效果以纯色 + 描边 / 阴影近似。
// ==========================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sanguo
{
    // 平行四边形条（CSS 的 skewX 体力条 / 怒气条）：x 方向 [from, to] 的一段，纵向或横向渐变
    public sealed class DuelBar : MaskableGraphic
    {
        public float from = 0, to = 1;          // 沿条方向的范围（0..1）
        public bool fromRight;                  // 从右端起算
        public float skew;                      // 每单位高度的水平偏移（tan 角）；正值 = 上缘右移
        public bool horizontal;                 // 渐变方向：false = 纵向（0 = 上缘），true = 沿条方向（相对所画的一段）
        public float yFrom = 0, yTo = 1;        // 纵向范围（0 = 上缘）
        public Color[] stops = { Color.white };
        public float[] pos;                     // 渐变位置（缺省均分）

        public void Set(float a, float b) { if (Mathf.Abs(a - from) > 1e-4f || Mathf.Abs(b - to) > 1e-4f) { from = a; to = b; SetVerticesDirty(); } }
        public void SetStops(Color[] s, float[] p = null) { stops = s; pos = p; SetVerticesDirty(); }

        float P(int i) { return pos != null && i < pos.Length ? pos[i] : (stops.Length > 1 ? (float)i / (stops.Length - 1) : 0); }
        Color At(float t)
        {
            if (stops.Length == 1 || t <= P(0)) return stops[0];
            for (int i = 1; i < stops.Length; i++) if (t <= P(i)) { float a = P(i - 1), b = P(i); return Color.Lerp(stops[i - 1], stops[i], b > a ? (t - a) / (b - a) : 1); }
            return stops[stops.Length - 1];
        }
        Vector2 Pt(Rect r, float fx, float fy)
        {
            // fx：沿条方向（0..1），fy：0 = 上缘
            float x = fromRight ? 1 - fx : fx;
            float y = r.yMax - fy * r.height;
            return new Vector2(r.xMin + x * r.width + skew * (y - r.center.y), y);
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (to <= from || yTo <= yFrom) return;
            var r = GetPixelAdjustedRect();
            var cuts = new List<float>();
            if (horizontal) { cuts.Add(0); for (int i = 0; i < stops.Length; i++) { float p = P(i); if (p > 0 && p < 1) cuts.Add(p); } cuts.Add(1); }
            else { cuts.Add(yFrom); for (int i = 0; i < stops.Length; i++) { float p = P(i); if (p > yFrom && p < yTo) cuts.Add(p); } cuts.Add(yTo); }
            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                float a = cuts[i], b = cuts[i + 1];
                if (b <= a) continue;
                Vector2 p0, p1, p2, p3; Color c0, c1, c2, c3;
                if (horizontal)
                {
                    float x0 = from + (to - from) * a, x1 = from + (to - from) * b;
                    p0 = Pt(r, x0, yTo); p1 = Pt(r, x0, yFrom); p2 = Pt(r, x1, yFrom); p3 = Pt(r, x1, yTo);
                    c0 = c1 = At(a); c2 = c3 = At(b);
                }
                else
                {
                    p0 = Pt(r, from, b); p1 = Pt(r, from, a); p2 = Pt(r, to, a); p3 = Pt(r, to, b);
                    c1 = c2 = At(a); c0 = c3 = At(b);
                }
                int k = vh.currentVertCount;
                vh.AddVert(p0, c0 * color, Vector2.zero); vh.AddVert(p1, c1 * color, Vector2.zero);
                vh.AddVert(p2, c2 * color, Vector2.zero); vh.AddVert(p3, c3 * color, Vector2.zero);
                vh.AddTriangle(k, k + 1, k + 2); vh.AddTriangle(k, k + 2, k + 3);
            }
        }
    }

    // 玩家输入：键盘（←/→ 或 A/D、↑/W/空格、J、K、L/↓/S、I）+ 触摸按键；停用期间一律丢弃
    public sealed class DuelInput : IDuelCtrl
    {
        readonly DuelHeld held = new DuelHeld();
        readonly DuelTaps taps = new DuelTaps();
        public DuelHeld Held { get { return held; } }
        public DuelTaps Taps { get { return taps; } }
        public void Update(double dt, DuelSim sim) { }
        public bool enabled = true;
        readonly HashSet<string> keys = new HashSet<string>();
        readonly Dictionary<string, bool> touch = new Dictionary<string, bool>();
        static readonly KeyValuePair<KeyCode, string>[] Map =
        {
            new KeyValuePair<KeyCode, string>(KeyCode.LeftArrow, "left"), new KeyValuePair<KeyCode, string>(KeyCode.A, "left"),
            new KeyValuePair<KeyCode, string>(KeyCode.RightArrow, "right"), new KeyValuePair<KeyCode, string>(KeyCode.D, "right"),
            new KeyValuePair<KeyCode, string>(KeyCode.UpArrow, "up"), new KeyValuePair<KeyCode, string>(KeyCode.W, "up"), new KeyValuePair<KeyCode, string>(KeyCode.Space, "up"),
            new KeyValuePair<KeyCode, string>(KeyCode.J, "light"), new KeyValuePair<KeyCode, string>(KeyCode.K, "heavy"),
            new KeyValuePair<KeyCode, string>(KeyCode.L, "guard"), new KeyValuePair<KeyCode, string>(KeyCode.DownArrow, "guard"), new KeyValuePair<KeyCode, string>(KeyCode.S, "guard"),
            new KeyValuePair<KeyCode, string>(KeyCode.I, "special"),
        };
        readonly HashSet<KeyCode> down = new HashSet<KeyCode>();

        // 每帧（逻辑步长之前）读取键盘
        public void Poll()
        {
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.LeftCommand)) return;
            bool changed = false;
            foreach (var kv in Map)
            {
                if (Input.GetKeyDown(kv.Key))
                {
                    if (enabled) { Press(kv.Value); down.Add(kv.Key); changed = true; }
                }
                else if (down.Contains(kv.Key) && !Input.GetKey(kv.Key)) { down.Remove(kv.Key); changed = true; }
            }
            if (changed) { keys.Clear(); foreach (var kv in Map) if (down.Contains(kv.Key)) keys.Add(kv.Value); Recompute(); }
        }
        void Press(string k)
        {
            switch (k)
            {
                case "left": taps.dir = -1; break;
                case "right": taps.dir = 1; break;
                case "light": taps.light++; break;
                case "up": taps.up++; break;
                case "special": taps.special++; break;
                case "heavy": taps.heavy++; break;
            }
        }
        public void SetTouch(string k, bool on)
        {
            bool was; touch.TryGetValue(k, out was);
            if (was == on) return;
            touch[k] = on;
            if (on && enabled) Press(k);
            Recompute();
        }
        bool T(string k) { bool v; return touch.TryGetValue(k, out v) && v; }
        void Recompute()
        {
            bool L = keys.Contains("left") || T("left"), R = keys.Contains("right") || T("right");
            held.x = enabled ? (R ? 1 : 0) - (L ? 1 : 0) : 0;
            held.up = enabled && (keys.Contains("up") || T("up"));
            held.light = enabled && (keys.Contains("light") || T("light"));
            held.heavy = enabled && (keys.Contains("heavy") || T("heavy"));
            held.guard = enabled && (keys.Contains("guard") || T("guard"));
            held.special = enabled && (keys.Contains("special") || T("special"));
        }
        // 启用 / 停用：停用期间的按键与触摸一律丢弃，启用时清空残留的点击与按住状态
        public void SetEnabled(bool on)
        {
            enabled = on;
            keys.Clear(); down.Clear(); touch.Clear();
            taps.light = taps.up = taps.special = taps.heavy = taps.dir = 0;
            Recompute();
        }
    }

    // 单挑画面的全部界面
    public sealed class DuelHud
    {
        readonly DuelScreen S;
        public RectTransform root, fadeRoot;
        RectTransform staticLayer, fxLayer, dmgLayer, annLayer, pauseLayer;
        Image fadeImg, flashImg;
        float fadeTarget, fadeA, flashA;
        // 尺寸
        public float k, u, b, sal, sar, sat, sab, W, H, minF;
        public bool compact, portrait;
        Vector2Int lastScreen; Rect lastSafe;
        // 顶栏
        sealed class Side
        {
            public RectTransform s, port, portInner; public DuelBar hpFill, hpTrail, hpHi, rageFill, rageBorder; public Text lbl;
            public float trail = 1, trailDelay, shakeT = 9; public bool full, low; public float v = -1, tv = -1, rv = -1; public string lblText;
        }
        readonly Side[] side = { new Side(), new Side() };
        CanvasGroup topGroup; RectTransform topRt; float hudShow, hudShowTarget;
        Text timerT; RectTransform timerRt; int lastLeft = -1; bool timerLow;
        float hudBot;
        // 连击
        sealed class Combo { public RectTransform rt; public CanvasGroup g; public Text num; public float hideAt, popT = 9, alpha, target; public int n; }
        readonly Combo[] combo = { new Combo(), new Combo() };
        // 操作
        CanvasGroup legendG, touchG, hintG; RectTransform padRt; readonly Dictionary<string, RectTransform> btn = new Dictionary<string, RectTransform>();
        readonly Dictionary<string, Image> btnFace = new Dictionary<string, Image>(), btnRing = new Dictionary<string, Image>();
        readonly Dictionary<string, Text> btnText = new Dictionary<string, Text>();
        bool controlsOn; float controlsA;
        public bool hasTouch;
        RectTransform skipRt;
        // 动画
        sealed class Anim { public float t, dur, delay; public Action<float> f; public Action done; public UnityEngine.Object owner; public bool hasOwner; }
        readonly List<Anim> anims = new List<Anim>();
        // 飘字
        sealed class Dmg { public RectTransform rt; public CanvasGroup g; public Vector3 p; public float t, vx; }
        readonly List<Dmg> dmgs = new List<Dmg>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly Dictionary<string, Texture> portraitCache = new Dictionary<string, Texture>();

        static Color C(string h) { return DuelXf.C(h); }
        static Color CA(string h, float a) { var c = DuelXf.C(h); c.a = a; return c; }

        public DuelHud(DuelScreen s) { S = s; }

        // ------------------------------------------------------------ 尺寸 --
        void Measure()
        {
            var rr = UIKit.Root.rect;
            W = rr.width; H = rr.height;
            float dpr = Screen.dpi <= 0 ? 1 : Application.isMobilePlatform ? Mathf.Max(1, Screen.dpi / 160f) : Mathf.Max(1, Screen.dpi / 96f);
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            float cssW = sw / dpr, cssH = sh / dpr;
            k = H / sh * dpr;
            u = Mathf.Clamp(Mathf.Min(cssW * 0.012f, cssH * 0.0215f), 10, 18) * k;
            b = Mathf.Clamp(Mathf.Min(cssH * 0.155f, cssW * 0.18f), 54, 86) * k;
            compact = cssH < 540 || cssW < 700;
            portrait = sh > sw;
            minF = 11 * k;
            var sa = Screen.safeArea;
            sal = sa.xMin * W / sw; sar = (sw - sa.xMax) * W / sw; sab = sa.yMin * H / sh; sat = (sh - sa.yMax) * H / sh;
            lastScreen = new Vector2Int(Screen.width, Screen.height); lastSafe = sa;
        }
        float F(float x) { return Mathf.Max(minF, x); }
        int Fs(float x) { return Mathf.Max(1, Mathf.RoundToInt(x)); }

        // ------------------------------------------------------------ 小工具 --
        static RectTransform R(string n, Transform p) { return UIKit.NewRect(n, p); }
        static void TL(RectTransform rt, float x, float y, float w, float h) { UIKit.Place(rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h)); }
        static void At(RectTransform rt, Vector2 anchor, Vector2 pivot, float x, float y, float w, float h) { UIKit.Place(rt, anchor, pivot, new Vector2(x, y), new Vector2(w, h)); }
        Image Img(Transform p, Sprite s, Color c, string n = "Img") { var i = UIKit.Img(p, s, c, n); i.raycastTarget = false; return i; }
        Text Txt(Transform p, string text, float size, Color col, TextAnchor al = TextAnchor.MiddleCenter, bool kai = false, FontStyle st = FontStyle.Normal, string n = "Text")
        {
            var t = UIKit.Label(p, text, Fs(size), col, al, kai, n);
            t.fontStyle = st; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
        static T Fx<T>(Graphic g, Color c, float dx, float dy) where T : Shadow { var e = g.gameObject.AddComponent<T>(); e.effectColor = c; e.effectDistance = new Vector2(dx, dy); return e; }
        DuelBar Bar(Transform p, string n, float skewV, Color[] stops, float[] pos = null, bool horiz = false)
        {
            var rt = R(n, p); UIKit.Stretch(rt);
            var g = rt.gameObject.AddComponent<DuelBar>(); g.skew = skewV; g.stops = stops; g.pos = pos; g.horizontal = horiz; g.raycastTarget = false;
            return g;
        }
        static float TextW(Text t) { return t.preferredWidth; }

        // 三次贝塞尔缓动（CSS cubic-bezier）
        public static float Bez(float x1, float y1, float x2, float y2, float x)
        {
            if (x <= 0) return 0; if (x >= 1) return 1;
            float t = x;
            for (int i = 0; i < 8; i++)
            {
                float cx = 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t - x;
                float dx = 3 * x1 * (1 - t) * (1 - t) + 6 * (x2 - x1) * t * (1 - t) + 3 * (1 - x2) * t * t;
                if (Mathf.Abs(dx) < 1e-5f) break;
                t = Mathf.Clamp01(t - cx / dx);
            }
            return 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t;
        }
        static float Out(float x) { return Bez(0.2f, 0.8f, 0.2f, 1, x); }          // cubic-bezier(.2,.8,.2,1)
        static float Pop(float x) { return Bez(0.17f, 0.89f, 0.32f, 1.4f, x); }      // cubic-bezier(.17,.89,.32,1.4)
        static float Ease(float x) { return Bez(0.25f, 0.1f, 0.25f, 1, x); }         // ease

        public void Run(float dur, Action<float> f, float delay = 0, Action done = null, UnityEngine.Object owner = null)
        {
            f(0);
            anims.Add(new Anim { dur = Mathf.Max(1e-4f, dur), delay = delay, f = f, done = done, owner = owner, hasOwner = owner != null });
        }
        static void Kill(Component c) { if (c != null) UnityEngine.Object.Destroy(c.gameObject); }

        // ------------------------------------------------------------ 构建 --
        public void Build()
        {
            Measure();
            root = R("DuelHud", UIKit.Root); UIKit.Stretch(root);
            var screens = UIKit.Screens;
            root.SetSiblingIndex(screens != null ? screens.GetSiblingIndex() + 1 : UIKit.Root.childCount - 1);
            // 挡住下层界面的点击
            var blocker = Img(root, null, new Color(0, 0, 0, 0), "Blocker"); UIKit.Stretch(blocker.rectTransform); blocker.raycastTarget = true;
            var vig = Img(root, UIKit.Vignette, new Color(0.04f, 0.016f, 0, 0.62f), "Vignette"); UIKit.Stretch(vig.rectTransform, -2, -2, -2, -2);
            staticLayer = R("Static", root); UIKit.Stretch(staticLayer);
            dmgLayer = R("Damage", root); UIKit.Stretch(dmgLayer);
            fxLayer = R("Fx", root); UIKit.Stretch(fxLayer);
            annLayer = R("Announce", root); UIKit.Stretch(annLayer);
            pauseLayer = R("Pause", root); UIKit.Stretch(pauseLayer);
            flashImg = Img(root, null, new Color(1, 1, 1, 0), "Flash"); UIKit.Stretch(flashImg.rectTransform, -20, -20, -20, -20);
            fadeRoot = R("DuelFade", UIKit.Root); UIKit.Stretch(fadeRoot); fadeRoot.SetAsLastSibling();
            fadeImg = Img(fadeRoot, null, new Color(0.027f, 0.024f, 0.04f, 0), "Fade"); UIKit.Stretch(fadeImg.rectTransform);
        }
        bool staticBuilt;
        public void BuildHud() { Measure(); BuildStatic(); staticBuilt = true; }

        // 顶栏、连击、图例、触摸按键、观战标签（尺寸随屏幕改变时重建）
        void BuildStatic()
        {
            for (int i = staticLayer.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(staticLayer.GetChild(i).gameObject);
            btn.Clear(); btnFace.Clear(); btnRing.Clear(); btnText.Clear();
            // ---- 顶栏
            topRt = R("Top", staticLayer);
            float tl = sal + 1.1f * u, tr = sar + 1.1f * u, tt = sat + 0.7f * u;
            float Wt = W - tl - tr;
            topRt.anchorMin = new Vector2(0, 1); topRt.anchorMax = new Vector2(1, 1); topRt.pivot = new Vector2(0.5f, 1);
            topRt.offsetMin = new Vector2(tl, -tt - 6 * u); topRt.offsetMax = new Vector2(-tr, -tt);
            topGroup = topRt.gameObject.AddComponent<CanvasGroup>(); topGroup.blocksRaycasts = false; topGroup.interactable = false;
            float timerW = 4.8f * u, gap = 1.1f * u, sw = (Wt - timerW - 2 * gap) / 2, P = 5.4f * u;
            for (int i = 0; i < 2; i++) BuildSide(i, i == 0 ? 0 : Wt - sw, sw, P);
            // 计时
            timerRt = R("Timer", topRt); TL(timerRt, (Wt - timerW) / 2, 0, timerW, timerW + 0.2f * u + F(0.85f * u) * 1.2f);
            var ring = Img(timerRt, UIKit.Circle, C("#f3c969"), "Ring"); TL(ring.rectTransform, 0, 0, timerW, timerW);
            Fx<Shadow>(ring, new Color(0, 0, 0, 0.6f), 0, -0.3f * u);
            var inner = Img(ring.transform, UIKit.Circle, C("#22181f"), "Inner"); UIKit.Stretch(inner.rectTransform, 2 * k, 2 * k, 2 * k, 2 * k);
            var innerHi = Img(inner.transform, UIKit.Circle, new Color(0.23f, 0.17f, 0.2f, 0.7f), "Hi"); UIKit.Stretch(innerHi.rectTransform, 0.15f * timerW, 0.3f * timerW, 0.15f * timerW, 0.02f * timerW);
            timerT = Txt(ring.transform, "60", 2.35f * u, C("#ffe9b0"), TextAnchor.MiddleCenter, true, FontStyle.Bold, "Num"); UIKit.Stretch(timerT.rectTransform);
            Fx<Shadow>(timerT, C("#4d1f05"), 0, -2 * k);
            var small = Txt(timerRt, "单挑", F(0.85f * u), C("#f3c969"), TextAnchor.UpperCenter, true, FontStyle.Bold, "Small");
            TL(small.rectTransform, 0, timerW + 0.2f * u, timerW, F(0.85f * u) * 1.2f);
            Fx<Shadow>(small, Color.black, 0, -1 * k);
            lastLeft = -1; timerLow = false;
            hudBot = tt + P + 18 * k;
            // ---- 连击
            for (int i = 0; i < 2; i++)
            {
                var cb = combo[i];
                cb.rt = R("Combo" + i, staticLayer);
                float x = i == 0 ? sal + 2.2f * u : -(sar + 2.2f * u);
                At(cb.rt, new Vector2(i == 0 ? 0 : 1, 0.72f), new Vector2(i == 0 ? 0 : 1, 1), x, 0, 12 * u, 6 * u);
                cb.g = cb.rt.gameObject.AddComponent<CanvasGroup>(); cb.g.alpha = cb.alpha; cb.g.blocksRaycasts = false;
                var al = i == 0 ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
                cb.num = Txt(cb.rt, cb.n.ToString(), 4.2f * u, C("#ffc23a"), al, true, FontStyle.BoldAndItalic, "Num");
                At(cb.num.rectTransform, new Vector2(i == 0 ? 0 : 1, 1), new Vector2(i == 0 ? 0 : 1, 1), 0, 0, 10 * u, 4.4f * u);
                Fx<Outline>(cb.num, new Color(0.24f, 0.08f, 0, 0.6f), k, k); Fx<Shadow>(cb.num, new Color(0, 0, 0, 0.6f), 0, -3 * k);
                var lb = Txt(cb.rt, "连击", 1.3f * u, C("#ffe08a"), al, true, FontStyle.Bold, "Label");
                At(lb.rectTransform, new Vector2(i == 0 ? 0 : 1, 1), new Vector2(i == 0 ? 0 : 1, 1), 0, -4.4f * u, 10 * u, 1.5f * u);
                Fx<Shadow>(lb, Color.black, 0, -2 * k);
            }
            // ---- 操作图例 / 触摸按键
            legendG = touchG = hintG = null; padRt = null;
            if (S.player >= 0 && (!S.isTouch || S.hybrid)) BuildLegend();
            if (S.player >= 0 && S.isTouch && S.input != null)
            {
                BuildTouch();
                if (!S.hybrid && !portrait)
                {
                    var hint = Txt(staticLayer, "双击 ◀ ▶ 冲刺 · 按住「重击」蓄力", F(0.9f * u), new Color(0.96f, 0.93f, 0.86f, 0.75f), TextAnchor.LowerCenter, false, FontStyle.Bold, "TouchHint");
                    At(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), 0, sab + 0.5f * u, 30 * u, F(0.9f * u) * 1.3f);
                    Fx<Shadow>(hint, Color.black, 0, -k);
                    hintG = hint.gameObject.AddComponent<CanvasGroup>();
                }
            }
            hasTouch = touchG != null;
            ApplyControls(controlsA);
            // ---- 观战
            skipRt = null;
            if (S.player < 0)
            {
                var watch = Txt(staticLayer, "观　战", u, C("#f3c969"), TextAnchor.UpperCenter, true, FontStyle.Bold, "Watch");
                At(watch.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), 0, -(sat + 7.8f * u), 10 * u, 1.4f * u);
                Fx<Shadow>(watch, Color.black, 0, -k);
                float fs = 1.05f * u, h = Mathf.Max(44 * k, fs * 1.2f + 1.2f * fs);
                skipRt = Img(staticLayer, UIKit.RR, new Color(0.05f, 0.04f, 0.06f, 0.65f), "Skip").rectTransform;
                var t = Txt(skipRt, "跳过 ▶▶", fs, C("#fff4dc"), TextAnchor.MiddleCenter, false, FontStyle.Bold, "Label");
                float w = TextW(t) + 2.2f * fs;
                At(skipRt, new Vector2(1, 1), new Vector2(1, 1), -(sar + 1.2f * u), -(sat + (compact ? 6.8f : 7.6f) * u), w, h);
                UIKit.Stretch(t.rectTransform);
                var o = Img(skipRt, UIKit.RROutline, CA("#f3c969", 0.6f), "Outline"); UIKit.Stretch(o.rectTransform);
            }
            // 顶栏的显隐保持
            ApplyHudShow();
        }

        void BuildSide(int i, float x, float sw, float P)
        {
            var sd = side[i];
            bool B = i == 1;
            var gen = B ? S.genB : S.genA; Color col = B ? S.colB : S.colA;
            sd.s = R("Side" + i, topRt); TL(sd.s, x, 0, sw, P);
            // 头像（势力色描边、圆角裁切）
            sd.port = Img(sd.s, UIKit.RR, col, "Portrait").rectTransform;
            At(sd.port, new Vector2(B ? 1 : 0, 1), new Vector2(B ? 1 : 0, 1), 0, 0, P, P);
            Fx<Shadow>(sd.port.GetComponent<Image>(), new Color(0, 0, 0, 0.55f), 0, -0.3f * u);
            var mask = Img(sd.port, UIKit.RR, C("#1c1822"), "Mask"); UIKit.Stretch(mask.rectTransform, 2 * k, 2 * k, 2 * k, 2 * k);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            sd.portInner = mask.rectTransform;
            PortraitInto(mask.rectTransform, gen, col, B);
            // 信息栏
            float ix = P + 0.8f * u, iw = sw - ix;
            float nameH = 1.7f * u * 1.05f, hpH = 1.55f * u, rageH = Mathf.Max(0.62f * u, F(0.85f * u));
            float infoH = nameH + 0.32f * u + hpH + 0.32f * u + rageH, y0 = Mathf.Max(0, (P - infoH) / 2);
            var info = R("Info", sd.s); At(info, new Vector2(B ? 1 : 0, 1), new Vector2(B ? 1 : 0, 1), B ? -ix : ix, -y0, iw, infoH);
            // 姓名 · 武力 · 玩家
            var name = Txt(info, gen.name ?? "", 1.7f * u, C("#fff4dc"), B ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, true, FontStyle.Bold, "Name");
            float nw = TextW(name);
            At(name.rectTransform, new Vector2(B ? 1 : 0, 1), new Vector2(B ? 1 : 0, 1), 0, 0, nw + 2, nameH);
            Fx<Shadow>(name, new Color(0, 0, 0, 0.75f), 0, -2 * k);
            float fx = nw + 0.6f * u;
            float bf = F(0.95f * u);
            var badge = R("War", info);
            var bbg = badge.gameObject.AddComponent<DuelBar>(); bbg.stops = new[] { C("#ffe7a0"), C("#d9a640") }; bbg.raycastTarget = false;
            var bl = Txt(badge, "<size=" + Fs(bf * 0.85f) + "><color=#1a1208bf>武力</color></size> " + DuelLooks.WarOf(gen).ToString("0"), bf, C("#1a1208"), TextAnchor.MiddleCenter, false, FontStyle.Bold, "Label");
            float bw = TextW(bl) + 0.9f * bf, bh = bf * 1.36f;
            At(badge, new Vector2(B ? 1 : 0, 1), new Vector2(B ? 1 : 0, 0.5f), B ? -fx : fx, -nameH * 0.55f, bw, bh);
            UIKit.Stretch(bl.rectTransform);
            fx += bw + 0.6f * u;
            if (S.player == i)
            {
                var tag = Txt(info, DuelGame.Auto ? "自动" : "玩家", F(0.85f * u), CA("#ffd98a", 0.9f), B ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, false, FontStyle.Bold, "Tag");
                At(tag.rectTransform, new Vector2(B ? 1 : 0, 1), new Vector2(B ? 1 : 0, 0.5f), B ? -fx : fx, -nameH * 0.55f, TextW(tag) + 2, nameH);
                if (portrait) tag.gameObject.SetActive(false);
            }
            // 体力条（平行四边形：a 向右倾，b 向左倾）
            float skewV = (B ? -1 : 1) * 0.404f;
            var hp = R("HP", info); At(hp, new Vector2(0, 1), new Vector2(0, 1), 0, -(nameH + 0.32f * u), iw, hpH);
            var hb = Bar(hp, "Border", skewV, new[] { CA("#f3c969", 0.75f) });
            Fx<Shadow>(hb, new Color(0, 0, 0, 0.5f), 0, -0.2f * u);
            var inner = R("Inner", hp); UIKit.Stretch(inner, k, k, k, k);
            Bar(inner, "Bg", skewV, new[] { C("#120a0c"), C("#2a1618") });
            sd.hpTrail = Bar(inner, "Trail", skewV, new[] { C("#fff6d8"), C("#ff9a6a") });
            sd.hpFill = Bar(inner, "Fill", skewV, HpStops(false), new[] { 0, 0.38f, 0.7f, 1 });
            sd.hpHi = Bar(inner, "Hi", skewV, new[] { new Color(1, 1, 1, 0.35f) }); sd.hpHi.yFrom = 0.12f; sd.hpHi.yTo = 0.34f;
            foreach (var bb in new[] { sd.hpTrail, sd.hpFill, sd.hpHi }) bb.fromRight = B;
            for (int t = 1; t < 4; t++)
            {
                var tick = Bar(inner, "Tick", skewV, new[] { new Color(0, 0, 0, 0.35f) });
                float f0 = t * 0.25f, dw = k / Mathf.Max(1, iw);
                tick.from = f0; tick.to = f0 + dw;
            }
            sd.v = sd.tv = -1; sd.low = false;
            // 怒气条 + 标签
            float rw = iw * 0.78f, ry = nameH + 0.32f * u + hpH + 0.32f * u;
            var rage = R("Rage", info); At(rage, new Vector2(B ? 1 : 0, 1), new Vector2(B ? 1 : 0, 1), 0, -(ry + (rageH - 0.62f * u) / 2), rw, 0.62f * u);
            sd.rageBorder = Bar(rage, "Border", skewV, new[] { new Color(1, 1, 1, 0.2f) });
            var rin = R("Inner", rage); UIKit.Stretch(rin, k, k, k, k);
            Bar(rin, "Bg", skewV, new[] { new Color(0, 0, 0, 0.6f) });
            sd.rageFill = Bar(rin, "Fill", skewV, new[] { C("#5a1a9a"), C("#d8389a"), C("#ff6a3a") }, new[] { 0, 0.6f, 1 }, true);
            sd.rageFill.fromRight = B;
            sd.rv = -1; sd.full = false;
            sd.lbl = Txt(info, "怒", F(0.85f * u), C("#d8a8ff"), B ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, true, FontStyle.Bold, "RageLabel");
            At(sd.lbl.rectTransform, new Vector2(B ? 1 : 0, 1), new Vector2(B ? 1 : 0, 1), B ? -(rw + 0.5f * u) : rw + 0.5f * u, -ry, iw - rw - 0.5f * u, rageH);
            sd.lbl.supportRichText = true;
            Fx<Shadow>(sd.lbl, Color.black, 0, -k);
            sd.lblText = null;
        }
        static Color[] HpStops(bool low)
        {
            return low ? new[] { C("#ffb0a0"), C("#ff4a3a"), C("#b8180e"), C("#b8180e") } : new[] { C("#fff1a8"), C("#f2b33a"), C("#d9741c"), C("#a8460e") };
        }

        // 头像（Portrait.Texture；出错时退回势力色圆章 + 姓氏）
        void PortraitInto(RectTransform parent, General gen, Color col, bool flip)
        {
            var tex = PortraitTex(gen, col, flip);
            if (tex != null) { var ri = R("Img", parent).gameObject.AddComponent<RawImage>(); ri.texture = tex; ri.raycastTarget = false; UIKit.Stretch(ri.rectTransform); return; }
            var med = Img(parent, null, col, "Medal"); UIKit.Stretch(med.rectTransform);
            var t = Txt(med.transform, string.IsNullOrEmpty(gen.name) ? "?" : gen.name.Substring(0, 1), parent.rect.height > 0 ? parent.rect.height * 0.55f : 3 * u, C("#fff3da"), TextAnchor.MiddleCenter, true, FontStyle.Bold);
            UIKit.Stretch(t.rectTransform); Fx<Shadow>(t, new Color(0, 0, 0, 0.5f), 0, -2 * k);
        }
        Texture PortraitTex(General gen, Color col, bool flip)
        {
            string key = (gen.name ?? "") + (flip ? "|f" : "|n");
            Texture t;
            if (portraitCache.TryGetValue(key, out t)) return t;
            try { t = Portrait.Texture(gen, 256, col, PortraitMood.Angry, false, flip); }
            catch (Exception e) { Debug.LogWarning("单挑头像：" + e.Message); t = null; }
            portraitCache[key] = t;
            return t;
        }
        public void Preload()
        {
            PortraitTex(S.genA, S.colA, false); PortraitTex(S.genB, S.colB, true);
        }

        // ------------------------------------------------------------ 图例与触摸 --
        RectTransform KeyCap(Transform p, string s, float fs, bool red)
        {
            var cap = Img(p, UIKit.RR, red ? C("#e8604a") : C("#e8dfc8"), "Key");
            var t = Txt(cap.transform, s, fs * 0.9f, red ? Color.white : C("#1a1410"), TextAnchor.MiddleCenter, false, FontStyle.Bold);
            UIKit.Stretch(t.rectTransform);
            float w = Mathf.Max(1.7f * fs, TextW(t) + 0.7f * fs);
            cap.rectTransform.sizeDelta = new Vector2(w, 1.7f * fs);
            Fx<Shadow>(cap, red ? C("#7a1a10") : C("#6a5a40"), 0, -2 * k);
            return cap.rectTransform;
        }
        // 一行「键帽 + 说明」的排版：返回宽度（元素按顺序放在 parent 内 x 起）
        float Row(Transform parent, float x, float yMid, float fs, string[] keys, bool[] reds, string text, Color col, string small = null)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == "/") { var sl = Txt(parent, "/", fs, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleLeft); At(sl.rectTransform, new Vector2(0, 1), new Vector2(0, 0.5f), x, -yMid, TextW(sl) + 2, fs * 1.4f); x += TextW(sl) + 0.3f * fs; continue; }
                var cap = KeyCap(parent, keys[i], fs, reds != null && reds[i]);
                cap.anchorMin = cap.anchorMax = new Vector2(0, 1); cap.pivot = new Vector2(0, 0.5f); cap.anchoredPosition = new Vector2(x, -yMid);
                x += cap.sizeDelta.x + 0.3f * fs;
            }
            if (!string.IsNullOrEmpty(text))
            {
                var t = Txt(parent, text + (small != null ? "<size=" + Fs(fs * 0.88f) + "><color=#ada392>  " + small + "</color></size>" : ""), fs, col, TextAnchor.MiddleLeft, false, FontStyle.Bold);
                t.supportRichText = true;
                float tw = TextW(t);
                At(t.rectTransform, new Vector2(0, 1), new Vector2(0, 0.5f), x, -yMid, tw + 2, fs * 1.4f);
                x += tw;
            }
            return x;
        }
        void BuildLegend()
        {
            float fs = compact ? F(0.85f * u) : 0.95f * u;
            var lg = Img(staticLayer, UIKit.RR, new Color(0.05f, 0.04f, 0.06f, 0.55f), "Legend").rectTransform;
            var o = Img(lg, UIKit.RROutline, CA("#f3c969", 0.25f), "Outline"); UIKit.Stretch(o.rectTransform);
            var items = new[]
            {
                new object[] { new[] { "←", "→" }, "移动" }, new object[] { new[] { "↑" }, "跳" }, new object[] { new[] { "J" }, "轻击" },
                new object[] { new[] { "K" }, "重击·按住蓄力" }, new object[] { new[] { "L" }, "格挡" }, new object[] { new[] { "I" }, "绝技" },
            };
            float padX = (compact ? 0.7f : 0.9f) * fs, padY = (compact ? 0.3f : 0.45f) * fs, gapX = (compact ? 0.6f : 0.9f) * u, rowH = 1.8f * fs, gapY = (compact ? 0.25f : 0.35f) * u;
            float maxW = W * 0.96f - 2 * padX;
            // 先量宽度，按行折排（flex-wrap），每行居中
            var widths = new List<float>();
            foreach (var it in items)
            {
                var tmp = R("Tmp", lg);
                widths.Add(Row(tmp, 0, 0, fs, (string[])it[0], ((string[])it[0]).Length == 1 && (string)it[1] == "绝技" ? new[] { true } : null, (string)it[1], C("#e8dfca")));
                UnityEngine.Object.DestroyImmediate(tmp.gameObject);
            }
            var rows = new List<List<int>> { new List<int>() }; float cw = 0;
            for (int i = 0; i < items.Length; i++)
            {
                float add = (rows[rows.Count - 1].Count > 0 ? gapX : 0) + widths[i];
                if (rows[rows.Count - 1].Count > 0 && cw + add > maxW) { rows.Add(new List<int>()); cw = 0; add = widths[i]; }
                rows[rows.Count - 1].Add(i); cw += add;
            }
            float totalW = 0;
            var rowW = new List<float>();
            foreach (var r in rows) { float w = 0; for (int j = 0; j < r.Count; j++) w += widths[r[j]] + (j > 0 ? gapX : 0); rowW.Add(w); totalW = Mathf.Max(totalW, w); }
            float h = rows.Count * rowH + (rows.Count - 1) * gapY + 2 * padY;
            At(lg, new Vector2(0.5f, 0), new Vector2(0.5f, 0), 0, sab + (compact ? 0.4f : 0.9f) * u, totalW + 2 * padX, h);
            for (int ri = 0; ri < rows.Count; ri++)
            {
                float x = padX + (totalW - rowW[ri]) / 2, y = padY + ri * (rowH + gapY) + rowH / 2;
                foreach (var i in rows[ri])
                {
                    var it = items[i];
                    Row(lg, x, y, fs, (string[])it[0], (string)it[1] == "绝技" ? new[] { true } : null, (string)it[1], C("#e8dfca"));
                    x += widths[i] + gapX;
                }
            }
            legendG = lg.gameObject.AddComponent<CanvasGroup>(); legendG.blocksRaycasts = false;
        }

        void BuildTouch()
        {
            var layer = R("Touch", staticLayer); UIKit.Stretch(layer);
            touchG = layer.gameObject.AddComponent<CanvasGroup>(); touchG.blocksRaycasts = false;
            padRt = R("Pad", layer);
            At(padRt, new Vector2(0, 0), new Vector2(0, 0), sal + 1.3f * u, sab + 0.9f * u, b * 2.25f, b * 2.05f);
            TouchBtn(padRt, "left", "◀", false, 0, 0, b, true, false);
            TouchBtn(padRt, "right", "▶", false, 1.22f * b, 0, b, true, false);
            TouchBtn(padRt, "up", "▲", false, 0.61f * b, 1.04f * b, b, true, false);
            var btns = R("Buttons", layer);
            At(btns, new Vector2(1, 0), new Vector2(1, 0), -(sar + 1.3f * u), sab + 0.9f * u, b * 2.45f, b * 2.2f);
            TouchBtn(btns, "light", "轻击", true, 0, 0, b * 1.18f, false, true, "big");
            TouchBtn(btns, "heavy", "重击", true, 1.32f * b, 0.02f * b, b, false, true);
            TouchBtn(btns, "guard", "格挡", true, 0.1f * b, 1.3f * b, b, false, true);
            TouchBtn(btns, "special", "绝技", true, 1.36f * b, 1.16f * b, b, false, true, "sp");
        }
        // left / bottom（pad）或 right / bottom（按钮）定位的圆形按键
        void TouchBtn(RectTransform parent, string key, string label, bool kai, float x, float y, float size, bool leftAnchored, bool rightAnchored, string cls = "")
        {
            var ring = Img(parent, UIKit.Circle, cls == "big" ? new Color(1, 0.55f, 0.43f, 0.85f) : cls == "sp" ? new Color(0.78f, 0.63f, 1, 0.5f) : CA("#f3c969", 0.6f), "Btn_" + key);
            var rt = ring.rectTransform;
            if (rightAnchored) At(rt, new Vector2(1, 0), new Vector2(1, 0), -x, y, size, size);
            else At(rt, new Vector2(0, 0), new Vector2(0, 0), x, y, size, size);
            Fx<Shadow>(ring, new Color(0, 0, 0, 0.45f), 0, -0.2f * u);
            var face = Img(rt, UIKit.Circle, cls == "big" ? new Color(0.55f, 0.13f, 0.1f, 0.7f) : new Color(0.12f, 0.1f, 0.16f, 0.55f), "Face");
            UIKit.Stretch(face.rectTransform, 2 * k, 2 * k, 2 * k, 2 * k);
            var t = Txt(rt, label, b * (cls == "big" ? 0.34f : kai ? 0.3f : 0.36f), cls == "sp" ? new Color(1, 1, 1, 0.55f) : C("#fff4dc"), TextAnchor.MiddleCenter, kai, FontStyle.Bold);
            UIKit.Stretch(t.rectTransform); Fx<Shadow>(t, Color.black, 0, -k);
            btn[key] = rt; btnFace[key] = face; btnRing[key] = ring; btnText[key] = t;
        }
        // 触摸按键的视觉：按下缩小变金；绝技在怒气满时发光
        void StyleBtn(string key, bool on, bool ready, float time)
        {
            RectTransform rt; if (!btn.TryGetValue(key, out rt) || rt == null) return;
            rt.localScale = Vector3.one * (on ? 0.92f : 1);
            bool big = key == "light", sp = key == "special";
            Color face = on ? new Color(0.85f, 0.65f, 0.3f, 0.72f) : big ? new Color(0.55f, 0.13f, 0.1f, 0.7f) : new Color(0.12f, 0.1f, 0.16f, 0.55f);
            if (sp && ready && !on) face = Color.Lerp(new Color(0.9f, 0.6f, 0.25f, 0.8f), new Color(1, 0.78f, 0.4f, 0.85f), 0.5f + 0.5f * Mathf.Sin(time * Mathf.PI / 0.6f));
            btnFace[key].color = face;
            if (sp)
            {
                btnRing[key].color = ready ? C("#ffe08a") : new Color(0.78f, 0.63f, 1, 0.5f);
                btnText[key].color = ready ? Color.white : new Color(1, 1, 1, 0.55f);
            }
        }

        // 多点触控：每个触点独立；方向键可同一指滑动切换（指针捕获：按下时在哪个按钮上，就一直算那个按钮）
        readonly Dictionary<int, string> zones = new Dictionary<int, string>();
        readonly Dictionary<int, string> owner = new Dictionary<int, string>();
        readonly HashSet<string> btnOn = new HashSet<string>();
        public void PollTouch(DuelInput input, float time, bool specialReady)
        {
            if (!hasTouch || input == null) return;
            var seen = new HashSet<int>();
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                Pointer(t.fingerId, t.position, t.phase == TouchPhase.Began, t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled);
                seen.Add(t.fingerId);
            }
            if (Input.touchCount == 0 && Input.mousePresent)
            {
                bool dn = Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0);
                if (dn || held || zones.ContainsKey(-1) || owner.ContainsKey(-1)) Pointer(-1, Input.mousePosition, dn, !held);
                if (held) seen.Add(-1);
            }
            // 消失的触点
            foreach (var id in new List<int>(zones.Keys)) if (!seen.Contains(id)) zones.Remove(id);
            foreach (var id in new List<int>(owner.Keys)) if (!seen.Contains(id)) owner.Remove(id);
            bool L = false, Rr = false, U = false;
            foreach (var z in zones.Values) { if (z == "left") L = true; else if (z == "right") Rr = true; else if (z == "up") U = true; }
            input.SetTouch("left", L); input.SetTouch("right", Rr); input.SetTouch("up", U);
            btnOn.Clear(); foreach (var v in owner.Values) btnOn.Add(v);
            foreach (var kk in new[] { "light", "heavy", "guard", "special" }) input.SetTouch(kk, btnOn.Contains(kk));
            StyleBtn("left", L, false, time); StyleBtn("right", Rr, false, time); StyleBtn("up", U, false, time);
            foreach (var kk in new[] { "light", "heavy", "guard", "special" }) StyleBtn(kk, btnOn.Contains(kk), kk == "special" && specialReady, time);
        }
        void Pointer(int id, Vector2 pos, bool began, bool ended)
        {
            if (ended) { zones.Remove(id); owner.Remove(id); return; }
            if (began)
            {
                if (controlsA < 0.5f) return;
                foreach (var kk in new[] { "light", "heavy", "guard", "special" })
                {
                    RectTransform rt; if (!btn.TryGetValue(kk, out rt) || rt == null) continue;
                    var c = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                    var e = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(new Vector3(rt.rect.xMax, rt.rect.center.y)));
                    if (Vector2.Distance(pos, c) <= Vector2.Distance(e, c)) { owner[id] = kk; return; }
                }
                var z = ZoneAt(pos);
                if (z != null) zones[id] = z;
                return;
            }
            if (zones.ContainsKey(id)) zones[id] = ZoneAt(pos);
        }
        string ZoneAt(Vector2 p)
        {
            if (padRt == null) return null;
            Vector3[] cs = new Vector3[4]; padRt.GetWorldCorners(cs);   // 屏幕覆盖画布：世界坐标即屏幕像素
            float left = cs[0].x, bottom = cs[0].y, right = cs[2].x, top = cs[2].y;
            float px = Screen.height / Mathf.Max(1, H) * k;     // 1 CSS 像素对应的屏幕像素
            if (p.x < left - 40 * px || p.x > right + 40 * px || p.y > top + 50 * px || p.y < bottom - 40 * px) return null;
            float bb = (top - bottom) / 2.05f;
            if (p.y > bottom + bb * 1.02f) return "up";
            return p.x < (left + right) / 2 ? "left" : "right";
        }

        public void ShowControls(bool on) { controlsOn = on; }
        void ApplyControls(float a)
        {
            if (legendG != null) legendG.alpha = a;
            if (touchG != null) { touchG.alpha = a; touchG.gameObject.SetActive(a > 0.001f); }
            if (hintG != null) hintG.alpha = a;
        }

        // ------------------------------------------------------------ 顶栏显隐 --
        public void ShowHud(bool on) { hudShowTarget = on ? 1 : 0; }
        void ApplyHudShow()
        {
            if (topGroup == null) return;
            float e = Ease(hudShow);
            topGroup.alpha = e;
            float tl = sal + 1.1f * u, tr = sar + 1.1f * u;
            topRt.anchoredPosition = new Vector2((tl - tr) / 2, -(sat + 0.7f * u) + (1 - e) * 1.2f * 16 * k);
        }

        // ------------------------------------------------------------ 公告与飘字 --
        public void Announce(string text, string cls, string sub, float sec)
        {
            for (int i = annLayer.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(annLayer.GetChild(i).gameObject);
            var box = R("Ann", annLayer);
            bool small = cls == "small", red = cls == "red";
            float fs = (small ? 3 : 6) * u;
            At(box, new Vector2(0.5f, H < 540 * k ? 0.7f : 0.64f), new Vector2(0.5f, 1), 0, 0, W, fs * 1.2f + (sub != null ? 2.4f * u : 0));
            var g = box.gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false;
            var t = Txt(box, text, fs, red ? C("#ff6a50") : C("#fff2c8"), TextAnchor.UpperCenter, true, FontStyle.Bold);
            At(t.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), 0, 0, W, fs * 1.2f);
            Fx<Outline>(t, red ? new Color(1, 0.16f, 0.08f, 0.45f) : new Color(1, 0.47f, 0.16f, 0.45f), 0.08f * fs, 0.08f * fs);
            Fx<Shadow>(t, red ? C("#2a0000") : C("#6a1a0a"), 0, -0.06f * fs);
            if (sub != null)
            {
                var st = Txt(box, sub, 1.2f * u, C("#f5eddb"), TextAnchor.UpperCenter, false, FontStyle.Bold);
                At(st.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), 0, -(fs * 1.2f + 0.5f * 1.2f * u), W, 1.6f * u);
                Fx<Shadow>(st, Color.black, 0, -k);
            }
            box.pivot = new Vector2(0.5f, 1 - 0.5f * fs * 1.2f / Mathf.Max(1, box.sizeDelta.y));
            box.anchoredPosition = new Vector2(0, -0.5f * fs * 1.2f);
            Run(sec, p =>
            {
                if (box == null) return;
                float a, s;
                if (p < 0.18f) { float q = Out(p / 0.18f); a = q; s = Mathf.Lerp(2.2f, 1, q); }
                else if (p < 0.8f) { float q = Out((p - 0.18f) / 0.62f); a = 1; s = Mathf.Lerp(1, 1.04f, q); }
                else { float q = Out((p - 0.8f) / 0.2f); a = 1 - q; s = Mathf.Lerp(1.04f, 1.1f, q); }
                g.alpha = a; box.localScale = Vector3.one * s;
            }, 0, () => Kill(box));
        }

        public void FloatDmg(Vector3 threePos, string text, string cls, float vx)
        {
            var rt = R("Dmg", dmgLayer);
            float fs = cls == "g" ? 1.2f * u : cls == "big" ? 2.1f * u : cls == "lbl" ? 1.4f * u : 1.6f * u;
            Color col = cls == "g" ? C("#9cc8ff") : cls == "big" ? C("#ffd040") : cls == "lbl" ? C("#ff9a6a") : Color.white;
            var t = Txt(rt, text, fs, col, TextAnchor.MiddleCenter, cls == "lbl", cls == "lbl" ? FontStyle.Bold : FontStyle.BoldAndItalic);
            UIKit.Stretch(t.rectTransform);
            Fx<Shadow>(t, Color.black, 0, -2 * k); Fx<Outline>(t, new Color(0, 0, 0, 0.5f), k, k);
            At(rt, new Vector2(0, 0), new Vector2(0.5f, 0.5f), -9999, -9999, TextW(t) + 4, fs * 1.3f);
            var g = rt.gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false;
            dmgs.Add(new Dmg { rt = rt, g = g, p = new Vector3(threePos.x, threePos.y, 0.3f), vx = vx });
            if (dmgs.Count > 10) { Kill(dmgs[0].rt); dmgs.RemoveAt(0); }
        }

        public void ScreenFlash(float a) { flashA = a; }
        public void SetFade(float a) { fadeTarget = a; }
        public bool FadeDone { get { return Mathf.Abs(fadeA - fadeTarget) < 0.01f; } }
        public void HitShake(int idx) { side[idx].shakeT = 0; side[idx].trailDelay = 0.45f; }
        public void ShowCombo(int idx, int n, float realT)
        {
            var cb = combo[idx]; cb.n = n;
            if (cb.num != null) cb.num.text = n.ToString();
            cb.target = 1; cb.popT = 0; cb.hideAt = realT + 1.3f;
        }

        // 绝技特写横幅：头像滑入 + 招式名大字
        public void CutIn(int idx, DuelSpecialInfo sp, float speed)
        {
            var gen = idx == 0 ? S.genA : S.genB; Color col = idx == 0 ? S.colA : S.colB;
            Color sc = sp.color != null ? C(sp.color) : col;
            bool B = idx == 1;
            var band = R("Cut", fxLayer);
            float bw = W * 1.2f, bh = 9 * u;
            At(band, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), 0, 0, bw, bh);
            band.localRotation = Quaternion.Euler(0, 0, 4);
            var g = band.gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false;
            Bar(band, "Bg", 0, new[] { new Color(sc.r, sc.g, sc.b, 0), sc, sc, new Color(sc.r, sc.g, sc.b, 0) }, new[] { 0, 0.18f, 0.82f, 1 }, true);
            float pad = bw * 0.16f, P = 7.6f * u;
            var port = Img(band, UIKit.RR, C("#fff2c8"), "Portrait").rectTransform;
            At(port, new Vector2(B ? 1 : 0, 0.5f), new Vector2(B ? 1 : 0, 0.5f), B ? -pad : pad, 0, P, P);
            var mask = Img(port, UIKit.RR, C("#1c1822"), "Mask"); UIKit.Stretch(mask.rectTransform, 2 * k, 2 * k, 2 * k, 2 * k);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            PortraitInto(mask.rectTransform, gen, C("#fff2c8"), B);
            float tx = pad + P + 1.5f * u;
            var small = Txt(band, gen.name + " · 绝技", 1.1f * u, new Color(1, 1, 1, 0.85f), B ? TextAnchor.LowerRight : TextAnchor.LowerLeft, false, FontStyle.Bold);
            At(small.rectTransform, new Vector2(B ? 1 : 0, 0.5f), new Vector2(B ? 1 : 0, 0), B ? -tx : tx, 4.4f * u * 0.5f, 40 * u, 1.4f * u);
            Fx<Shadow>(small, Color.black, 0, -k);
            var big = Txt(band, sp.name, 4.4f * u, Color.white, B ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, true, FontStyle.Bold);
            At(big.rectTransform, new Vector2(B ? 1 : 0, 0.5f), new Vector2(B ? 1 : 0, 0.5f), B ? -tx : tx, -0.6f * u, 60 * u, 4.8f * u);
            Fx<Shadow>(big, new Color(0, 0, 0, 0.7f), 0, -0.06f * 4.4f * u); Fx<Outline>(big, new Color(1, 1, 1, 0.25f), k, k);
            float dur = Mathf.Max(0.5f, 1.0f / Mathf.Max(0.5f, speed)), dir = B ? -1 : 1;
            Run(dur, p =>
            {
                if (band == null) return;
                float a, x;
                if (p < 0.18f) { float q = Out(p / 0.18f); a = q; x = Mathf.Lerp(-0.3f, 0, q); }
                else if (p < 0.82f) { float q = Out((p - 0.18f) / 0.64f); a = 1; x = Mathf.Lerp(0, 0.03f, q); }
                else { float q = Out((p - 0.82f) / 0.18f); a = 1 - q; x = Mathf.Lerp(0.03f, 0.25f, q); }
                g.alpha = a;
                // translate(x%) 在旋转前：沿横幅方向
                var off = Quaternion.Euler(0, 0, 4) * new Vector3(x * bw * dir, 0, 0);
                band.anchoredPosition = new Vector2(off.x, off.y);
            }, 0, null);
            Run(1.1f, p => { }, 0, () => Kill(band));
        }

        // ------------------------------------------------------------ 开场 VS --
        public Action IntroVS()
        {
            bool shortH = H < 540 * k;
            var vs = R("VS", fxLayer); UIKit.Stretch(vs);
            var bgH = Mathf.Min(0.58f * H, 30 * u);
            var bg = R("Bg", vs); At(bg, new Vector2(0.5f, 0), new Vector2(0.5f, 0), 0, 0, W + 4, bgH);
            var bgb = bg.gameObject.AddComponent<DuelBar>(); bgb.raycastTarget = false;
            bgb.stops = new[] { new Color(0.03f, 0.024f, 0.04f, 0), new Color(0.03f, 0.024f, 0.04f, 0.5f), new Color(0.03f, 0.024f, 0.04f, 0.78f) }; bgb.pos = new[] { 0, 0.55f, 1 };
            var bgG = bg.gameObject.AddComponent<CanvasGroup>();
            float P = (shortH ? 7.6f : 10) * u, padB = sab + (shortH ? 2.2f : 4.5f) * u, gap = 3 * u;
            float vsF = (shortH ? 4.4f : 5.5f) * u;
            var vsT = Txt(vs, "VS", vsF, Color.white, TextAnchor.MiddleCenter, true, FontStyle.BoldAndItalic, "X");
            float vsW = TextW(vsT) + 4;
            var nameF = 2.4f * u; var wF = 1.05f * u;
            float colH = P + 0.4f * nameF + nameF * 1.1f + 0.4f * nameF + wF * 1.2f;
            var panels = new RectTransform[2];
            for (int i = 0; i < 2; i++)
            {
                var gen = i == 0 ? S.genA : S.genB; Color col = i == 0 ? S.colA : S.colB; var look = i == 0 ? S.lookA : S.lookB;
                var p = R("P" + i, vs);
                float pw = Mathf.Max(P, 14 * u);
                At(p, new Vector2(0.5f, 0), new Vector2(i == 0 ? 1 : 0, 0), (i == 0 ? -1 : 1) * (vsW / 2 + gap), padB, pw, colH);
                var port = Img(p, UIKit.RR, col, "Portrait").rectTransform; At(port, new Vector2(0.5f, 1), new Vector2(0.5f, 1), 0, 0, P, P);
                Fx<Shadow>(port.GetComponent<Image>(), new Color(0, 0, 0, 0.55f), 0, -0.3f * u);
                var mask = Img(port, UIKit.RR, C("#1c1822"), "Mask"); UIKit.Stretch(mask.rectTransform, 3 * k, 3 * k, 3 * k, 3 * k);
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                PortraitInto(mask.rectTransform, gen, col, i == 1);
                var n = Txt(p, gen.name, nameF, C("#fff4dc"), TextAnchor.UpperCenter, true, FontStyle.Bold, "Name");
                At(n.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), 0, -(P + 0.4f * nameF), 20 * u, nameF * 1.2f);
                Fx<Shadow>(n, Color.black, 0, -3 * k);
                var w = Txt(p, "武力 " + DuelLooks.WarOf(gen).ToString("0") + " · " + look.weaponName, wF, C("#ffe08a"), TextAnchor.UpperCenter, false, FontStyle.Bold, "War");
                At(w.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), 0, -(P + 0.8f * nameF + nameF * 1.1f), 24 * u, wF * 1.3f);
                Fx<Shadow>(w, Color.black, 0, -k);
                panels[i] = p;
            }
            At(vsT.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), 0, padB + colH / 2 + 3.2f * u * 0.5f, vsW + 2 * vsF, vsF * 1.3f);
            Fx<Outline>(vsT, new Color(1, 0.31f, 0.19f, 0.8f), 0.1f * vsF, 0.1f * vsF); Fx<Shadow>(vsT, C("#6a1000"), 0, -0.08f * vsF);
            var gA = panels[0].gameObject.AddComponent<CanvasGroup>(); var gB = panels[1].gameObject.AddComponent<CanvasGroup>(); var gX = vsT.gameObject.AddComponent<CanvasGroup>();
            var baseA = panels[0].anchoredPosition; var baseB = panels[1].anchoredPosition;
            Run(0.4f, q => { if (bgG != null) bgG.alpha = q; });
            Run(0.55f, q => { if (gA == null) return; float e = Out(q); gA.alpha = e; gB.alpha = e; panels[0].anchoredPosition = baseA + new Vector2(-(1 - e) * 0.4f * W, 0); panels[1].anchoredPosition = baseB + new Vector2((1 - e) * 0.4f * W, 0); });
            var xs = vsT.rectTransform;
            Run(0.6f, q => { if (gX == null) return; float e = Pop(q); gX.alpha = Mathf.Clamp01(e); xs.localScale = Vector3.one * Mathf.LerpUnclamped(3, 1, e); }, 0.25f);
            var all = vs.gameObject.AddComponent<CanvasGroup>(); all.blocksRaycasts = false;
            return () => { Run(0.35f, q => { if (all != null) all.alpha = 1 - Ease(q); }, 0, () => Kill(vs)); };
        }

        // ------------------------------------------------------------ 操作说明卡 --
        public Action ControlsCard(DuelSpecialInfo sp, General me, General foe)
        {
            bool shortH = H < 540 * k;
            float uu = shortH ? 12 * k : u;
            float cw = Mathf.Min((shortH ? 0.94f : 0.92f) * W, (shortH ? 64 : 46) * uu);
            var card = Img(fxLayer, UIKit.RR, new Color(0.08f, 0.07f, 0.1f, 0.94f), "Card").rectTransform;
            card.GetComponent<Image>().raycastTarget = true;
            var ol = Img(card, UIKit.RROutline, CA("#f3c969", 0.75f), "Outline"); UIKit.Stretch(ol.rectTransform);
            Fx<Shadow>(card.GetComponent<Image>(), new Color(0, 0, 0, 0.6f), 0, -1 * uu);
            float padX = (shortH ? 1.3f : 1.6f) * uu, padY = (shortH ? 0.9f : 1.2f) * uu, iw = cw - 2 * padX, y = padY;
            var h2 = Txt(card, "操 作 方 法", 2 * uu, C("#f3c969"), TextAnchor.UpperCenter, true, FontStyle.Bold, "Title");
            TL(h2.rectTransform, padX, y, iw, 2.4f * uu); Fx<Shadow>(h2, C("#4d1f05"), 0, -2 * k);
            y += 2.2f * uu + (shortH ? 0.3f : 0.5f) * 2 * uu;
            var vs = Txt(card, "攻击力与伤害取决于武力：<color=#ffe08a><b>" + me.name + " " + DuelLooks.WarOf(me).ToString("0") + "</b></color> 对 <color=#ffe08a><b>" + foe.name + " " + DuelLooks.WarOf(foe).ToString("0") + "</b></color>", uu, C("#d8cfbb"), TextAnchor.UpperCenter, false, FontStyle.Bold, "Vs");
            vs.horizontalOverflow = HorizontalWrapMode.Wrap;
            TL(vs.rectTransform, padX, y, iw, 1.5f * uu);
            y += 1.4f * uu + (shortH ? 0.4f : 0.7f) * uu;
            float fs = 1.05f * uu;
            if (S.isTouch && !S.hybrid)
            {
                // 触摸版：左列方向键，右列四个按钮
                float colW = (iw - uu) / 2, ry = y, chip = 2.6f * fs * 0.95f;
                System.Func<float, float, string[], bool[], string, float> chipRow = (x0, yy, chips, bigs, desc) =>
                {
                    float x = x0;
                    for (int i = 0; i < chips.Length; i++)
                    {
                        var c = Img(card, UIKit.Circle, bigs[i] ? new Color(1, 0.55f, 0.43f, 0.85f) : CA("#f3c969", 0.7f), "Chip").rectTransform;
                        TL(c, x, yy, chip, chip);
                        var cf = Img(c, UIKit.Circle, bigs[i] ? new Color(0.63f, 0.16f, 0.12f, 0.85f) : new Color(0.16f, 0.13f, 0.19f, 0.8f), "Face"); UIKit.Stretch(cf.rectTransform, 2 * k, 2 * k, 2 * k, 2 * k);
                        var ct = Txt(c, chips[i], fs * 0.9f, C("#fff4dc"), TextAnchor.MiddleCenter, true, FontStyle.Bold); UIKit.Stretch(ct.rectTransform);
                        x += chip + 0.6f * fs;
                    }
                    var d = Txt(card, desc, fs * 0.95f, C("#f5eddb"), TextAnchor.MiddleLeft, false, FontStyle.Bold); d.horizontalOverflow = HorizontalWrapMode.Wrap;
                    TL(d.rectTransform, x, yy, Mathf.Max(4 * fs, x0 + colW - x), chip);
                    return chip + 0.5f * fs;
                };
                float ly = ry;
                ly += chipRow(padX, ly, new[] { "◀", "▶" }, new[] { false, false }, "移动（双击冲刺）");
                ly += chipRow(padX, ly, new[] { "▲" }, new[] { false }, "跳跃（空中可轻击）");
                float rx = padX + colW + uu, ry2 = ry;
                ry2 += chipRow(rx, ry2, new[] { "轻击" }, new[] { true }, "连按三连击");
                ry2 += chipRow(rx, ry2, new[] { "重击" }, new[] { false }, "按住蓄力，满蓄破防");
                ry2 += chipRow(rx, ry2, new[] { "格挡" }, new[] { false }, "按住减伤八成，武力远胜可格开");
                ry2 += chipRow(rx, ry2, new[] { "绝技" }, new[] { false }, "怒气满时「" + sp.name + "」");
                y = Mathf.Max(ly, ry2);
            }
            else
            {
                var rows = new[]
                {
                    new object[] { new[] { "←", "→", "/", "A", "D" }, "移动", "双击冲刺" },
                    new object[] { new[] { "↑", "W", "空格" }, "跳跃", "空中可轻击" },
                    new object[] { new[] { "J" }, "轻击", "连按三连击" },
                    new object[] { new[] { "K" }, "重击", "按住蓄力，满蓄破防" },
                    new object[] { new[] { "L", "/", "↓" }, "格挡", "按住减伤八成；武力远胜时可格开连段" },
                    new object[] { new[] { "I" }, "绝技「" + sp.name + "」", "怒气满时" },
                };
                // 左列（键帽，右对齐）宽度
                float keyW = 0;
                foreach (var r in rows) { var tmp = R("Tmp", card); keyW = Mathf.Max(keyW, Row(tmp, 0, 0, fs, (string[])r[0], null, null, Color.white)); UnityEngine.Object.DestroyImmediate(tmp.gameObject); }
                float rowH = 1.7f * fs, gapY = 0.45f * uu;
                foreach (var r in rows)
                {
                    var tmp = R("Tmp", card); float kw = Row(tmp, 0, 0, fs, (string[])r[0], null, null, Color.white); UnityEngine.Object.DestroyImmediate(tmp.gameObject);
                    var keys = (string[])r[0];
                    Row(card, padX + (keyW - kw), y + rowH / 2, fs, keys, keys.Length == 1 && keys[0] == "I" ? new[] { true } : null, null, Color.white);
                    Row(card, padX + keyW + uu, y + rowH / 2, fs, new string[0], null, (string)r[1], C("#f5eddb"), (string)r[2]);
                    y += rowH + gapY;
                }
                if (S.hybrid)
                {
                    var hv = Txt(card, "触摸屏：左下 ◀ ▶ ▲ 移动，右下按钮出招", uu, C("#d8cfbb"), TextAnchor.UpperCenter, false, FontStyle.Bold);
                    TL(hv.rectTransform, padX, y, iw, 1.5f * uu); y += 1.6f * uu;
                }
            }
            y += 0.9f * 1.25f * uu;
            var go = Txt(card, S.hybrid ? "按任意键或轻触开始" : S.isTouch ? "轻触开始" : "按任意键开始", 1.25f * uu, C("#fff4dc"), TextAnchor.UpperCenter, true, FontStyle.Bold, "Go");
            TL(go.rectTransform, padX, y, iw, 1.5f * uu);
            y += 1.4f * uu + padY;
            float ch = Mathf.Min(y, 0.92f * H);
            At(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 0, 0, cw, ch);
            var g = card.gameObject.AddComponent<CanvasGroup>();
            Run(0.35f, q => { if (g == null) return; float e = Out(q); g.alpha = e; card.anchoredPosition = new Vector2(0, -(1 - e) * 0.06f * ch); card.localScale = Vector3.one * Mathf.Lerp(0.96f, 1, e); });
            float t0 = Time.unscaledTime;
            Run(3600, q => { if (go != null) go.color = new Color(1, 0.96f, 0.86f, Mathf.Lerp(0.55f, 1, 0.5f + 0.5f * Mathf.Sin((Time.unscaledTime - t0) * Mathf.PI / 0.7f))); }, 0, null, go);
            return () => { Run(0.2f, q => { if (g != null) g.alpha = 1 - q; }, 0, () => Kill(card)); };
        }

        // ------------------------------------------------------------ 胜负 --
        public void WinPanel(bool lose, General winner, string sub, uint seed)
        {
            var box = R("Win", fxLayer); UIKit.Stretch(box);
            var g = box.gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false;
            float gs = 17 * u, inkS = 26 * u;
            // 墨迹
            var ink = R("Ink", box).gameObject.AddComponent<RawImage>(); ink.raycastTarget = false;
            var itex = InkTexture(lose ? new Color(0.08f, 0.08f, 0.11f, 0.85f) : new Color(0.04f, 0.03f, 0.03f, 0.82f), seed);
            owned.Add(itex); ink.texture = itex;
            At(ink.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 0, 0.12f * gs, inkS, inkS);
            var glyph = Txt(box, lose ? "败" : "胜", gs, lose ? C("#3a3a46") : C("#c8221a"), TextAnchor.MiddleCenter, true, FontStyle.Bold, "Glyph");
            At(glyph.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), 0, 0.12f * gs, gs * 1.4f, gs * 1.2f);
            Fx<Shadow>(glyph, new Color(0.24f, 0, 0, 0.65f), 0.02f * gs, -0.03f * gs);
            var n = Txt(box, winner.name + (lose ? " 获胜" : ""), 2.4f * u, C("#fff4dc"), TextAnchor.MiddleCenter, true, FontStyle.Bold, "Name");
            At(n.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1), 0, -0.45f * gs, 40 * u, 2.9f * u);
            Fx<Shadow>(n, Color.black, 0, -3 * k);
            var s = Txt(box, sub, 1.1f * u, C("#e8dfca"), TextAnchor.MiddleCenter, false, FontStyle.Bold, "Sub");
            At(s.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1), 0, -0.45f * gs - 2.9f * u, 40 * u, 1.6f * u);
            Fx<Shadow>(s, Color.black, 0, -k);
            var h = Txt(box, S.isTouch ? "轻触继续" : "按任意键继续", 0.95f * u, new Color(0.96f, 0.93f, 0.86f, 0.7f), TextAnchor.MiddleCenter, false, FontStyle.Bold, "Hint");
            At(h.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1), 0, -0.45f * gs - 4.5f * u - 0.8f * 0.95f * u, 40 * u, 1.4f * u);
            var ig = ink.gameObject.AddComponent<CanvasGroup>();
            Run(0.5f, q => { if (ig == null) return; float e = Ease(q); ig.alpha = e; ink.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1, e); ink.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(20, 0, e)); });
            var gg = glyph.gameObject.AddComponent<CanvasGroup>();
            Run(0.45f, q => { if (gg == null) return; float e = Bez(0.2f, 1.4f, 0.4f, 1, q); gg.alpha = Mathf.Clamp01(q * 3); glyph.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(2.4f, 1, e); });
            foreach (var pair in new[] { new KeyValuePair<Text, float>(n, 0.3f), new KeyValuePair<Text, float>(s, 0.45f), new KeyValuePair<Text, float>(h, 0.9f) })
            {
                var tt = pair.Key; var tg = tt.gameObject.AddComponent<CanvasGroup>(); var basePos = tt.rectTransform.anchoredPosition; float fz = tt.fontSize;
                Run(0.5f, q => { if (tg == null) return; float e = Ease(q); tg.alpha = e; tt.rectTransform.anchoredPosition = basePos + new Vector2(0, -(1 - e) * 0.6f * fz); }, pair.Value);
            }
        }
        // 墨迹（“胜”字背后）：一大团 + 九点飞溅
        Texture2D InkTexture(Color col, uint seed)
        {
            var r = new SeededRandom(seed != 0 ? seed : 3u);
            var ra = new Raster(200, 200);
            ra.FillStyle = Paint.Solid(new RGBA(col.r, col.g, col.b, col.a));
            System.Action<double, double, double, int> blob = (cx, cy, rad, nn) =>
            {
                var p = new Path2D();
                for (int i = 0; i <= nn; i++)
                {
                    double a = (double)i / nn * Math.PI * 2, rr = rad * (0.75 + r.NextDouble() * 0.45);
                    if (i == 0) p.MoveTo(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr); else p.LineTo(cx + Math.Cos(a) * rr, cy + Math.Sin(a) * rr);
                }
                p.ClosePath();
                ra.Fill(p);
            };
            blob(100, 100, 70, 26);
            for (int i = 0; i < 9; i++) { double a = r.NextDouble() * Math.PI * 2, dist = 78 + r.NextDouble() * 22; blob(100 + Math.Cos(a) * dist, 100 + Math.Sin(a) * dist, 4 + r.NextDouble() * 9, 8); }
            return ra.ToTexture();
        }

        // ------------------------------------------------------------ 暂停提示 --
        RectTransform pauseBox;
        public void ShowPauseHint(bool on)
        {
            if (on && pauseBox == null)
            {
                pauseBox = Img(pauseLayer, null, new Color(0.03f, 0.024f, 0.04f, 0.85f), "Pause").rectTransform; UIKit.Stretch(pauseBox);
                var t = Txt(pauseBox, "请横置设备\n<size=" + Fs(1.2f * u) + ">比试已暂停</size>", 2.4f * u, C("#fff4dc"), TextAnchor.MiddleCenter, true, FontStyle.Bold);
                t.supportRichText = true; UIKit.Stretch(t.rectTransform);
            }
            else if (!on && pauseBox != null) { Kill(pauseBox); pauseBox = null; }
        }

        public bool PointInSkip(Vector2 screen)
        {
            return skipRt != null && RectTransformUtility.RectangleContainsScreenPoint(skipRt, screen, null);
        }

        // ------------------------------------------------------------ 每帧 --
        public void Update(float dt, float realT, DuelSim sim, Camera cam)
        {
            if (root == null) { UpdateFadeOnly(dt); return; }   // 画面已拆除，只剩淡出遮罩
            // 屏幕尺寸 / 安全区改变：重建静态部分
            if (staticBuilt && (Screen.width != lastScreen.x || Screen.height != lastScreen.y || Screen.safeArea != lastSafe)) { Measure(); BuildStatic(); }
            // 动画
            for (int i = 0; i < anims.Count; i++)
            {
                var a = anims[i];
                if (a.hasOwner && a.owner == null) { anims.RemoveAt(i--); continue; }   // 所属元素已销毁
                if (a.delay > 0) { a.delay -= dt; continue; }
                a.t += dt;
                float p = Mathf.Clamp01(a.t / a.dur);
                try { a.f(p); } catch (Exception) { p = 1; }
                if (p >= 1) { anims.RemoveAt(i--); if (a.done != null) a.done(); }
            }
            // 淡入淡出 / 闪白
            fadeA = Mathf.MoveTowards(fadeA, fadeTarget, dt / 0.3f);
            fadeImg.color = new Color(0.027f, 0.024f, 0.04f, Ease(fadeA));
            fadeImg.raycastTarget = fadeA > 0.01f;
            if (flashA > 0) { flashImg.color = new Color(1, 1, 1, flashA); flashA = Mathf.Max(0, flashA - dt / 0.28f * Mathf.Max(0.3f, flashA)); }
            else flashImg.color = new Color(1, 1, 1, 0);
            // 顶栏显隐、操作显隐
            hudShow = Mathf.MoveTowards(hudShow, hudShowTarget, dt / 0.35f); ApplyHudShow();
            controlsA = Mathf.MoveTowards(controlsA, controlsOn ? 1 : 0, dt / 0.3f); ApplyControls(controlsA);
            if (sim == null) return;
            // 体力 / 怒气 / 连击
            for (int i = 0; i < 2; i++)
            {
                var f = sim.f[i]; var H0 = side[i];
                float v = f.hp / 100f;
                if (H0.trail < v) H0.trail = v;
                if (H0.trailDelay > 0) H0.trailDelay -= dt;
                else if (H0.trail > v) H0.trail = Mathf.Max(v, H0.trail - dt * 0.7f);
                if (H0.hpFill != null)
                {
                    if (H0.v != v) { H0.hpFill.Set(0, v); H0.hpHi.Set(0, v); H0.v = v; }
                    bool low = v <= 0.3f;
                    if (low != H0.low) { H0.low = low; H0.hpFill.SetStops(HpStops(low), low ? new[] { 0, 0.4f, 1, 1 } : new[] { 0, 0.38f, 0.7f, 1 }); }
                    if (low) { float br = 1 + 0.35f * (0.5f + 0.5f * Mathf.Sin(realT * Mathf.PI / 0.5f)); H0.hpFill.color = new Color(br, br, br, 1); }
                    else H0.hpFill.color = Color.white;
                    if (H0.tv != H0.trail) { H0.hpTrail.Set(0, H0.trail); H0.tv = H0.trail; }
                    float rv = Mathf.Round((float)f.rage) / 100f;
                    if (H0.rv != rv) { H0.rageFill.Set(0, rv); H0.rv = rv; }
                    bool full = f.rage >= 100;
                    var spx = i == 0 ? S.spA : S.spB;
                    bool isP = S.player == i && !DuelGame.Auto;
                    if (full != H0.full)
                    {
                        H0.full = full;
                        if (full && sim.running && !sim.hasResult) FloatDmg(new Vector3((float)f.x, 2.5f, 0), "怒气满", "lbl", 0);
                        H0.rageBorder.SetStops(new[] { full ? C("#ffe08a") : new Color(1, 1, 1, 0.2f) });
                        if (!full) H0.rageFill.SetStops(new[] { C("#5a1a9a"), C("#d8389a"), C("#ff6a3a") }, new[] { 0, 0.6f, 1 });
                    }
                    if (full)
                    {
                        // 金色流光（background-position 0.7 秒一周）
                        float ph = (realT / 0.7f) % 1f;
                        var cs = new Color[6]; var ps = new float[6];
                        for (int j = 0; j < 6; j++) { float x = j / 5f; ps[j] = x; float w = 0.5f + 0.5f * Mathf.Cos((x * 0.5f - ph) * Mathf.PI * 2 * 2); cs[j] = Color.Lerp(C("#ffc030"), C("#fff2b0"), w); }
                        H0.rageFill.SetStops(cs, ps);
                    }
                    string lt = full ? (compact ? "绝技" : "绝技「" + spx.name + "」") + (isP && !S.isTouch ? " <color=#ff8a70>[I]</color>" : "") : "怒";
                    if (lt != H0.lblText) { H0.lblText = lt; H0.lbl.text = lt; }
                    H0.lbl.color = full ? new Color(1, 0.88f, 0.54f, Mathf.Lerp(0.55f, 1, 0.5f + 0.5f * Mathf.Sin(realT * Mathf.PI / 0.6f))) : C("#d8a8ff");
                    // 受击时头像抖动（.28 秒）
                    if (H0.shakeT < 0.28f)
                    {
                        H0.shakeT += dt; float q = Mathf.Clamp01(H0.shakeT / 0.28f);
                        float sx = q < 0.2f ? Mathf.Lerp(0, -5, q / 0.2f) : q < 0.4f ? Mathf.Lerp(-5, 5, (q - 0.2f) / 0.2f) : q < 0.6f ? Mathf.Lerp(5, -3, (q - 0.4f) / 0.2f) : q < 0.8f ? Mathf.Lerp(-3, 2, (q - 0.6f) / 0.2f) : Mathf.Lerp(2, 0, (q - 0.8f) / 0.2f);
                        H0.port.anchoredPosition = new Vector2(sx * k, 0);
                    }
                }
                var cb = combo[i];
                if (cb.hideAt > 0 && realT > cb.hideAt) { cb.target = 0; cb.hideAt = 0; }
                cb.alpha = Mathf.MoveTowards(cb.alpha, cb.target, dt / 0.25f);
                if (cb.g != null)
                {
                    cb.g.alpha = cb.alpha;
                    if (cb.popT < 0.22f) { cb.popT += dt; cb.num.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(1.6f, 1, Pop(Mathf.Clamp01(cb.popT / 0.22f))); }
                }
            }
            // 计时
            int left = Mathf.Max(0, (int)Math.Ceiling(sim.limit - sim.time));
            if (left != lastLeft && timerT != null) { lastLeft = left; timerT.text = left.ToString(); timerLow = left <= 10; }
            if (timerT != null)
            {
                timerT.color = timerLow ? C("#ff8c73") : C("#ffe9b0");
                timerT.rectTransform.localScale = Vector3.one * (timerLow ? 1 + 0.08f * (0.5f + 0.5f * Mathf.Sin(realT * Mathf.PI / 0.5f)) : 1);
            }
            // 伤害飘字：投影到屏幕，夹在顶栏下沿之下
            for (int i = dmgs.Count - 1; i >= 0; i--)
            {
                var d = dmgs[i];
                d.t += dt;
                if (d.t > 0.9f || d.rt == null) { Kill(d.rt); dmgs.RemoveAt(i); continue; }
                var w = new Vector3(d.p.x + d.vx * d.t, d.p.y + d.t * 0.9f, -d.p.z);
                var sp = cam != null ? cam.WorldToScreenPoint(w) : Vector3.zero;
                float x = sp.x / Mathf.Max(1, Screen.width) * W, yTop = H - sp.y / Mathf.Max(1, Screen.height) * H;
                yTop = Mathf.Max(hudBot, yTop);
                d.rt.anchoredPosition = new Vector2(x, H - yTop);
                float sc = d.t < 0.12f ? 1.5f - d.t * 4 : 1;
                d.rt.localScale = Vector3.one * sc;
                d.g.alpha = Mathf.Clamp01((0.9f - d.t) * 3);
            }
        }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            if (fadeRoot != null) UnityEngine.Object.Destroy(fadeRoot.gameObject);
            foreach (var o in owned) if (o != null) UnityEngine.Object.Destroy(o);
            owned.Clear(); anims.Clear(); dmgs.Clear();
        }
        // 只留淡出遮罩（单挑画面拆除后再淡出）
        public void DisposeKeepFade()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
            foreach (var o in owned) if (o != null) UnityEngine.Object.Destroy(o);
            owned.Clear(); dmgs.Clear();
        }
        public void UpdateFadeOnly(float dt)
        {
            fadeA = Mathf.MoveTowards(fadeA, fadeTarget, dt / 0.3f);
            if (fadeImg != null) { fadeImg.color = new Color(0.027f, 0.024f, 0.04f, Ease(fadeA)); fadeImg.raycastTarget = fadeA > 0.01f; }
        }
        public void DisposeFade() { if (fadeRoot != null) UnityEngine.Object.Destroy(fadeRoot.gameObject); fadeRoot = null; }
    }
}
