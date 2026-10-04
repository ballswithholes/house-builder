using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sanguo
{
    public class Ref<T> { public T v; }

    // 代码构建的界面工具：字体、圆角贴图、面板、按钮、对话框、列表
    public static class UIKit
    {
        public static Canvas Canvas;
        public static RectTransform Root, ModalLayer, ToastLayer, LabelLayer;
        public static Font Body, Title;

        // 配色：漆黑底、鎏金描边、朱红点缀
        public static readonly Color Ink = new Color(0.07f, 0.07f, 0.1f, 0.9f);
        public static readonly Color Ink2 = new Color(0.13f, 0.12f, 0.16f, 0.95f);
        public static readonly Color Gold = new Color(0.95f, 0.79f, 0.42f);
        public static readonly Color GoldDim = new Color(0.95f, 0.79f, 0.42f, 0.35f);
        public static readonly Color Text = new Color(0.96f, 0.93f, 0.86f);
        public static readonly Color Muted = new Color(0.68f, 0.64f, 0.57f);
        public static readonly Color Cinnabar = new Color(0.78f, 0.22f, 0.17f);
        public static readonly Color Good = new Color(0.45f, 0.85f, 0.55f);
        public static readonly Color Bad = new Color(1f, 0.45f, 0.38f);

        static Sprite rr, rrOutline, circle, vignette, gradient;

        // ---------------------------------------------------------- 初始化 --
        public static void Init()
        {
            Body = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Hiragino Sans GB", "Heiti SC", "Microsoft YaHei", "Microsoft YaHei UI", "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "SimHei", "Arial Unicode MS", "Arial" }, 32);
            Title = Font.CreateDynamicFontFromOSFont(new[] { "STKaiti", "Kaiti SC", "KaiTi", "BiauKai", "Songti SC", "STSong", "SimSun", "Noto Serif CJK SC", "PingFang SC", "Microsoft YaHei", "Arial Unicode MS" }, 48);

            var go = new GameObject("UI");
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.6f;
            go.AddComponent<GraphicRaycaster>();
            Root = go.GetComponent<RectTransform>();
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            LabelLayer = Layer("Labels");
            var screens = Layer("Screens");
            ModalLayer = Layer("Modals");
            ToastLayer = Layer("Toasts");
            screens.SetSiblingIndex(1);
            // 刘海屏安全区域
            screens.gameObject.AddComponent<SafeArea>();
            ModalLayer.gameObject.AddComponent<SafeArea>();
            ToastLayer.gameObject.AddComponent<SafeArea>();
        }

        static RectTransform Layer(string name)
        {
            var rt = NewRect(name, Root);
            Stretch(rt);
            return rt;
        }
        public static RectTransform Screens { get { return (RectTransform)Root.Find("Screens"); } }

        // ---------------------------------------------------------- 贴图 --
        static Sprite MakeRounded(int size, int radius, int outline)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0);
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - radius; // <0 内部
                    float a = Mathf.Clamp01(0.5f - d);
                    if (outline > 0) a *= Mathf.Clamp01(d + outline + 0.5f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            float b = radius + 2;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }
        public static Sprite RR { get { if (rr == null) rr = MakeRounded(64, 18, 0); return rr; } }
        public static Sprite RROutline { get { if (rrOutline == null) rrOutline = MakeRounded(64, 18, 2); return rrOutline; } }
        public static Sprite Circle { get { if (circle == null) circle = MakeRounded(64, 32, 0); return circle; } }
        public static Sprite Vignette
        {
            get
            {
                if (vignette != null) return vignette;
                int n = 128;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f)) / (n * 0.72f);
                        tex.SetPixel(x, y, new Color(0, 0, 0, Mathf.Clamp01(Mathf.SmoothStep(0.35f, 1f, d))));
                    }
                tex.Apply();
                vignette = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
                return vignette;
            }
        }
        public static Sprite Gradient
        {
            get
            {
                if (gradient != null) return gradient;
                var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++) tex.SetPixel(0, y, new Color(1, 1, 1, y / 63f));
                tex.Apply();
                gradient = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f));
                return gradient;
            }
        }

        // ---------------------------------------------------------- 布局 --
        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
        public static void Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        // ---------------------------------------------------------- 组件 --
        public static Image Img(Transform parent, Sprite s, Color c, string name = "Img")
        {
            var rt = NewRect(name, parent);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = s; im.color = c;
            if (s != null && s.border != Vector4.zero) im.type = Image.Type.Sliced;
            return im;
        }

        // 面板：深色圆角底 + 金色描边
        public static RectTransform Panel(Transform parent, string name = "Panel", bool outline = true)
        {
            var bg = Img(parent, RR, Ink, name);
            bg.raycastTarget = true;
            var sh = bg.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.5f); sh.effectDistance = new Vector2(0, -6);
            if (outline)
            {
                var o = Img(bg.transform, RROutline, GoldDim, "Outline");
                Stretch(o.rectTransform, 3, 3, 3, 3);
                o.raycastTarget = false;
            }
            return bg.rectTransform;
        }

        public static Text Label(Transform parent, string text, int size, Color col, TextAnchor align = TextAnchor.MiddleLeft, bool title = false, string name = "Label")
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = title ? Title : Body; t.fontSize = size; t.color = col; t.alignment = align; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = true;
            return t;
        }

        public class Btn { public Button button; public Image bg; public Text label; public RectTransform rt; }

        public static Btn Button(Transform parent, string text, Action onClick, int size = 26, bool primary = false, string name = "Button")
        {
            var bg = Img(parent, RR, primary ? new Color(0.62f, 0.2f, 0.15f, 0.95f) : new Color(0.2f, 0.19f, 0.24f, 0.95f), name);
            var o = Img(bg.transform, RROutline, primary ? Gold : GoldDim, "Outline"); Stretch(o.rectTransform, 2, 2, 2, 2); o.raycastTarget = false;
            var b = bg.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.25f, 1.2f, 1.1f); colors.pressedColor = new Color(0.8f, 0.75f, 0.7f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f); colors.fadeDuration = 0.08f;
            b.colors = colors;
            b.targetGraphic = bg;
            var l = Label(bg.transform, text, size, Text, TextAnchor.MiddleCenter);
            Stretch(l.rectTransform, 8, 2, 8, 2);
            if (onClick != null) b.onClick.AddListener(() => { Sfx.Click(); onClick(); });
            return new Btn { button = b, bg = bg, label = l, rt = bg.rectTransform };
        }

        public static RectTransform Bar(Transform parent, float value, Color col, string name = "Bar")
        {
            var bg = Img(parent, RR, new Color(0, 0, 0, 0.45f), name);
            var fill = Img(bg.transform, RR, col, "Fill");
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;
            return bg.rectTransform;
        }

        public static RectTransform VList(Transform parent, float spacing = 8, int padding = 0)
        {
            var rt = NewRect("List", parent);
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            v.padding = new RectOffset(padding, padding, padding, padding);
            return rt;
        }
        public static RectTransform HList(Transform parent, float spacing = 8)
        {
            var rt = NewRect("Row", parent);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childControlHeight = true; h.childControlWidth = true; h.childForceExpandHeight = true; h.childForceExpandWidth = true;
            return rt;
        }
        public static LayoutElement Size(Component c, float w = -1, float h = -1, float flexW = -1)
        {
            var le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) le.preferredWidth = w;
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            return le;
        }

        // 可滚动区域，返回内容容器
        public static RectTransform Scroll(Transform parent, out ScrollRect sr)
        {
            var view = NewRect("Scroll", parent);
            view.gameObject.AddComponent<RectMask2D>();
            var img = view.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0.001f);
            sr = view.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30;
            var content = VList(view, 6);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = content; sr.viewport = view;
            return content;
        }

        public static Image Medal(Transform parent, string glyph, Color col, float size)
        {
            var c = Img(parent, Circle, col, "Medal");
            var ring = Img(c.transform, Circle, new Color(1, 0.88f, 0.6f, 0.9f), "Ring"); Stretch(ring.rectTransform, -3, -3, -3, -3); ring.transform.SetAsFirstSibling();
            ring.rectTransform.SetParent(c.transform, false);
            var inner = Img(c.transform, Circle, col, "Inner"); Stretch(inner.rectTransform, 2, 2, 2, 2);
            var t = Label(c.transform, glyph, Mathf.RoundToInt(size * 0.55f), Color.white, TextAnchor.MiddleCenter, true);
            Stretch(t.rectTransform);
            t.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.5f);
            Size(c, size, size);
            return c;
        }

        // ---------------------------------------------------------- 模态 --
        public class Modal { public RectTransform blocker, panel; public void Close() { if (blocker != null) UnityEngine.Object.Destroy(blocker.gameObject); } }

        public static Modal OpenModal(float w, float h, bool dim = true)
        {
            var blocker = Img(ModalLayer, null, new Color(0, 0, 0, dim ? 0.45f : 0.001f), "Modal").rectTransform;
            Stretch(blocker);
            var panel = Panel(blocker);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            panel.gameObject.AddComponent<PopIn>();
            return new Modal { blocker = blocker, panel = panel };
        }
        public static bool AnyModal { get { return ModalLayer != null && ModalLayer.childCount > 0; } }

        // 对话：说话者徽章 + 文字，点击继续
        public static IEnumerator Say(string text, string speaker = null, Color? speakerColor = null)
        {
            bool done = false;
            var blocker = Img(ModalLayer, null, new Color(0, 0, 0, 0.25f), "Say").rectTransform;
            Stretch(blocker);
            var panel = Panel(blocker);
            Place(panel, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1100, 210));
            panel.gameObject.AddComponent<PopIn>();
            float x = 36;
            if (speaker != null)
            {
                var m = Medal(panel, speaker.Substring(0, 1), speakerColor ?? Cinnabar, 96);
                Place(m.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(36, 12), new Vector2(96, 96));
                var n = Label(panel, speaker, 24, Gold, TextAnchor.MiddleCenter, true);
                Place(n.rectTransform, new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(84, 14), new Vector2(160, 34));
                x = 160;
            }
            var t = Label(panel, "", 30, Text, TextAnchor.UpperLeft);
            t.lineSpacing = 1.2f;
            Stretch(t.rectTransform, x, 30, 40, 30);
            var hint = Label(panel, "▼", 20, Gold, TextAnchor.LowerRight);
            Stretch(hint.rectTransform, 0, 12, 22, 0);
            var btn = blocker.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            int shown = 0; bool full = false;
            btn.onClick.AddListener(() => { if (!full) { full = true; } else done = true; });
            while (!done)
            {
                if (!full) { shown = Mathf.Min(text.Length, shown + 2); t.text = text.Substring(0, shown); if (shown >= text.Length) full = true; }
                else t.text = text;
                hint.enabled = full && Mathf.Repeat(Time.unscaledTime, 1f) < 0.65f;
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) { if (full) done = true; else full = true; }
                yield return null;
            }
            Sfx.Click();
            UnityEngine.Object.Destroy(blocker.gameObject);
        }

        public class Item
        {
            public string label, right, desc; public bool enabled = true; public bool selected;
            public Item(string l, string r = null, bool e = true, string d = null) { label = l; right = r; enabled = e; desc = d; }
        }

        // 列表选择：返回索引，取消为 -1
        public static IEnumerator Choose(string title, List<Item> items, Ref<int> result, string info = null, float width = 640)
        {
            result.v = -2;
            float h = Mathf.Min(760, 140 + (info != null ? 60 : 0) + items.Count * 70);
            var m = OpenModal(width, h);
            var head = Label(m.panel, title, 32, Gold, TextAnchor.MiddleLeft, true);
            Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -18), new Vector2(width - 120, 48));
            float top = 76;
            if (info != null)
            {
                var inf = Label(m.panel, info, 21, Muted, TextAnchor.UpperLeft);
                Place(inf.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -70), new Vector2(width - 60, 56));
                top += 58;
            }
            var close = Button(m.panel, "×", () => result.v = -1, 24);
            Place(close.rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(52, 48));
            ScrollRect sr;
            var content = Scroll(m.panel, out sr);
            Stretch((RectTransform)sr.transform, 22, 22, 22, top);
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var it = items[i];
                var b = Button(content, "", () => { if (items[idx].enabled) result.v = idx; }, 26, false, "Item");
                b.label.alignment = TextAnchor.MiddleLeft;
                b.label.text = it.label + (it.desc != null ? "  <size=18><color=#a8a194>" + it.desc + "</color></size>" : "");
                b.label.rectTransform.offsetMin = new Vector2(20, 2);
                if (it.right != null)
                {
                    var r = Label(b.rt, it.right, 22, Muted, TextAnchor.MiddleRight);
                    Stretch(r.rectTransform, 20, 0, 20, 0);
                }
                b.button.interactable = it.enabled;
                Size(b.bg, -1, 62);
            }
            while (result.v == -2)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) result.v = -1;
                yield return null;
            }
            m.Close();
        }

        // 多选
        public static IEnumerator ChooseMany(string title, List<Item> items, int max, Ref<List<int>> result, string info = null)
        {
            result.v = null;
            bool done = false, cancel = false;
            var sel = new HashSet<int>();
            for (int i = 0; i < items.Count; i++) if (items[i].selected) sel.Add(i);
            float width = 700, h = Mathf.Min(800, 220 + items.Count * 70);
            var m = OpenModal(width, h);
            var head = Label(m.panel, title, 32, Gold, TextAnchor.MiddleLeft, true);
            Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -18), new Vector2(width - 60, 48));
            var inf = Label(m.panel, info ?? "", 21, Muted, TextAnchor.UpperLeft);
            Place(inf.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -66), new Vector2(width - 60, 40));
            ScrollRect sr;
            var content = Scroll(m.panel, out sr);
            Stretch((RectTransform)sr.transform, 22, 96, 22, 112);
            var buttons = new List<Btn>();
            Action refresh = null;
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var b = Button(content, "", () =>
                {
                    if (!items[idx].enabled) return;
                    if (sel.Contains(idx)) sel.Remove(idx); else if (sel.Count < max) sel.Add(idx);
                    refresh();
                }, 26, false, "Item");
                b.label.alignment = TextAnchor.MiddleLeft;
                b.label.rectTransform.offsetMin = new Vector2(20, 2);
                if (items[i].right != null) { var r = Label(b.rt, items[i].right, 22, Muted, TextAnchor.MiddleRight); Stretch(r.rectTransform, 20, 0, 20, 0); }
                b.button.interactable = items[i].enabled;
                Size(b.bg, -1, 62);
                buttons.Add(b);
            }
            var ok = Button(m.panel, "确定", () => done = true, 28, true);
            Place(ok.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 22), new Vector2(200, 60));
            var cc = Button(m.panel, "取消", () => cancel = true, 28);
            Place(cc.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-240, 22), new Vector2(160, 60));
            var count = Label(m.panel, "", 22, Muted, TextAnchor.MiddleLeft);
            Place(count.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 22), new Vector2(300, 60));
            refresh = () =>
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    bool on = sel.Contains(i);
                    buttons[i].label.text = (on ? "<color=#f3c969>◆</color> " : "◇ ") + items[i].label;
                    buttons[i].bg.color = on ? new Color(0.45f, 0.32f, 0.16f, 0.95f) : new Color(0.2f, 0.19f, 0.24f, 0.95f);
                }
                count.text = "已选 " + sel.Count + " / " + max;
                ok.button.interactable = sel.Count > 0;
            };
            refresh();
            while (!done && !cancel) { if (Input.GetKeyDown(KeyCode.Escape)) cancel = true; yield return null; }
            m.Close();
            if (done) { result.v = new List<int>(sel); result.v.Sort(); }
        }

        // 数值选择
        public static IEnumerator PickNumber(string title, int min, int max, int step, int start, Func<int, string> describe, Ref<int> result)
        {
            result.v = -1;
            if (max < min) { yield return Say("数量不足，无法执行。"); yield break; }
            bool done = false, cancel = false;
            int val = Mathf.Clamp(start, min, max);
            var m = OpenModal(620, 340);
            var head = Label(m.panel, title, 32, Gold, TextAnchor.MiddleLeft, true);
            Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -18), new Vector2(560, 48));
            var big = Label(m.panel, "", 54, Text, TextAnchor.MiddleCenter, true);
            Place(big.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(360, 70));
            var desc = Label(m.panel, "", 22, Muted, TextAnchor.MiddleCenter);
            Place(desc.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(560, 34));
            var slider = NewSlider(m.panel, min, max, val);
            Place((RectTransform)slider.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -196), new Vector2(520, 36));
            Action upd = () => { big.text = val.ToString(); desc.text = describe != null ? describe(val) : ""; slider.value = val; };
            slider.onValueChanged.AddListener(f => { val = Mathf.Clamp(Mathf.RoundToInt(f / step) * step, min, max); big.text = val.ToString(); desc.text = describe != null ? describe(val) : ""; });
            var minus = Button(m.panel, "－", () => { val = Mathf.Max(min, val - step); upd(); }, 34);
            Place(minus.rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-230, -80), new Vector2(70, 64));
            var plus = Button(m.panel, "＋", () => { val = Mathf.Min(max, val + step); upd(); }, 34);
            Place(plus.rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(230, -80), new Vector2(70, 64));
            var ok = Button(m.panel, "确定", () => done = true, 28, true);
            Place(ok.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 22), new Vector2(180, 60));
            var cc = Button(m.panel, "取消", () => cancel = true, 28);
            Place(cc.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-220, 22), new Vector2(150, 60));
            upd();
            while (!done && !cancel) { if (Input.GetKeyDown(KeyCode.Escape)) cancel = true; if (Input.GetKeyDown(KeyCode.Return)) done = true; yield return null; }
            m.Close();
            if (done) result.v = val;
        }

        static Slider NewSlider(Transform parent, float min, float max, float val)
        {
            var root = NewRect("Slider", parent);
            var bg = Img(root, RR, new Color(0, 0, 0, 0.5f), "Track"); Stretch(bg.rectTransform, 0, 12, 0, 12);
            var fillArea = NewRect("FillArea", root); Stretch(fillArea, 6, 12, 6, 12);
            var fill = Img(fillArea, RR, Gold, "Fill"); Stretch(fill.rectTransform);
            var handleArea = NewRect("HandleArea", root); Stretch(handleArea, 14, 0, 14, 0);
            var handle = Img(handleArea, Circle, Color.white, "Handle"); handle.rectTransform.sizeDelta = new Vector2(36, 0);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
            s.minValue = min; s.maxValue = max; s.value = val; s.wholeNumbers = true;
            return s;
        }

        public static IEnumerator Confirm(string text, Ref<bool> result, string yes = "是", string no = "否")
        {
            var r = new Ref<int>();
            yield return Choose(text, new List<Item> { new Item(yes), new Item(no) }, r, null, 520);
            result.v = r.v == 0;
        }

        // ---------------------------------------------------------- 提示 --
        public static void Toast(string text, float seconds = 2.2f)
        {
            var p = Img(ToastLayer, RR, new Color(0.05f, 0.05f, 0.08f, 0.88f), "Toast").rectTransform;
            var t = Label(p, text, 24, Text, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 24, 6, 24, 6);
            float w = Mathf.Min(1100, t.preferredWidth + 60);
            int idx = ToastLayer.childCount - 1;
            Place(p, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -110 - idx * 58), new Vector2(w, 50));
            p.gameObject.AddComponent<AutoFade>().life = seconds;
        }

        // 屏幕中央的大字横幅
        public static void Banner(string text, string sub = null, float seconds = 2.4f)
        {
            var rt = NewRect("Banner", ToastLayer);
            Place(rt, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 220));
            var band = Img(rt, Gradient, new Color(0, 0, 0, 0.6f), "Band"); Stretch(band.rectTransform, 0, 30, 0, 30);
            band.type = Image.Type.Simple;
            var t = Label(rt, text, 84, Gold, TextAnchor.MiddleCenter, true);
            Stretch(t.rectTransform, 0, 40, 0, 0);
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.25f, 0.1f, 0.02f, 0.9f); o.effectDistance = new Vector2(2, -2);
            if (sub != null) { var s = Label(rt, sub, 28, Text, TextAnchor.MiddleCenter); Stretch(s.rectTransform, 0, 0, 0, 150); }
            rt.gameObject.AddComponent<AutoFade>().life = seconds;
            rt.gameObject.AddComponent<PopIn>();
        }
    }

    // 让界面避开刘海与圆角
    public class SafeArea : MonoBehaviour
    {
        Rect last; Vector2Int lastSize;
        void Update()
        {
            var sa = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (sa == last && size == lastSize) return;
            last = sa; lastSize = size;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    // 弹出动画
    public class PopIn : MonoBehaviour
    {
        float t;
        void OnEnable() { t = 0; transform.localScale = Vector3.one * 0.94f; }
        void Update()
        {
            t += Time.unscaledDeltaTime * 7f;
            float k = Mathf.Clamp01(t);
            transform.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, 1 - (1 - k) * (1 - k));
            if (k >= 1) enabled = false;
        }
    }
    // 自动淡出后销毁
    public class AutoFade : MonoBehaviour
    {
        public float life = 2;
        float t; CanvasGroup g;
        void Start() { g = gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false; }
        void Update()
        {
            t += Time.unscaledDeltaTime;
            g.alpha = Mathf.Clamp01(Mathf.Min(t * 5, (life - t) * 3));
            if (t > life) Destroy(gameObject);
        }
    }
    // 跟随三维坐标的界面元素
    public class WorldFollow : MonoBehaviour
    {
        public Transform target; public Vector3 worldPos; public Vector2 offset;
        public float hideBeyond = 9999;
        public float alphaMul = 1;
        RectTransform rt; CanvasGroup g;
        void Awake() { rt = (RectTransform)transform; g = gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false; g.interactable = false; }
        void LateUpdate()
        {
            var cam = Camera.main; if (cam == null) return;
            var p = target != null ? target.position : worldPos;
            var sp = cam.WorldToScreenPoint(p);
            bool vis = sp.z > 0 && Vector3.Distance(cam.transform.position, p) < hideBeyond;
            g.alpha = vis ? alphaMul : 0;
            if (!vis) return;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rt.parent, sp, null, out local);
            rt.anchoredPosition = local + offset;
        }
    }
}
