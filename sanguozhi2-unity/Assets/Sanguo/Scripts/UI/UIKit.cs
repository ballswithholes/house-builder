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
    // 第三版（← 网页版 ui.js / style.css 的 v3 部分，DESIGN-V3 §4、§5）：
    //   头像无处不在：Item 的 avatar（行首头像 FaceItem = 50）、Say(text, General, SayOpts)（说话人头像 96、表情、姓名下的小字、急报 / 说明样式）、
    //     SetText + InlineDeco（文字里的行内小头像 UIText.Face / Faces 与小标签 UIText.Tag，叠放在占位处）、GenCardSpec / GenCard（头像名片，
    //     Confirm 的 body 与 ChooseOpts.card）、PortraitImg 改用 Portrait.Bind（不卡帧）。
    //   成长界面：Notice（只有一个按钮、说明区可滚动）、LvTag（「Lv5」标签）、ExpBar（经验条）。
    //   有对话框（底部对话除外）打开时提示移到屏幕下方（ToastStack）；载入画面的游戏名分两段（LoadingTitle）。
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
            Body = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Hiragino Sans GB", "Heiti SC", "Microsoft YaHei", "Microsoft YaHei UI", "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "SimHei", "Apple SD Gothic Neo", "Malgun Gothic", "Noto Sans CJK KR", "Noto Sans KR", "Hiragino Sans", "Yu Gothic", "Arial Unicode MS", "Arial" }, 32);
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
            UpdateFloors();
            go.AddComponent<UIScaleWatch>();
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            LabelLayer = Layer("Labels");
            LabelLayer.gameObject.AddComponent<CanvasGroup>();
            LabelLayer.gameObject.AddComponent<LabelDim>();
            liveBanners = 0;
            var screens = Layer("Screens");
            ModalLayer = Layer("Modals");
            ToastLayer = Layer("Toasts");
            ToastLayer.gameObject.AddComponent<ToastStack>();
            screens.SetSiblingIndex(1);
            // 刘海屏安全区域
            screens.gameObject.AddComponent<SafeArea>();
            ModalLayer.gameObject.AddComponent<SafeArea>();
            ToastLayer.gameObject.AddComponent<SafeArea>();
        }

        // ---------------------------------------------------------- 缩放与下限 --
        // 网页版 style.css 的字号下限 max(11px, …) 与触控目标 ≥ 44px（CSS 像素 = 与设备无关的“点”）。
        // Unity 版：1 点 = PxPerPt 物理像素（移动端 dpi/160，桌面 dpi/96，至少 1）；画布 1 单位 = CanvasScale 物理像素
        // （与 CanvasScaler 的 ScaleWithScreenSize 1600×900、match 0.6 同一公式）。
        // MinFont / MinTouch 为画布单位；Label 的字号不低于 MinFont（缩放变化时由 UIScaleWatch 重新套用），
        // 按钮的可点区域不小于 MinTouch（TouchPad 向外扩展透明的命中区）。桌面 1600×900 时分别为 11 / 44，现有界面不受影响。
        public const float FontFloorPt = 11, TouchMinPt = 44;
        public static int MinFont = 11;
        public static float MinTouch = 44;
        public static event Action ScaleChanged;
        public static float PxPerPt
        {
            get
            {
                float dpi = Screen.dpi;
                if (!(dpi > 0)) return 1;
                return Mathf.Max(1f, Application.isMobilePlatform ? dpi / 160f : dpi / 96f);
            }
        }
        public static float CanvasScale
        {
            get
            {
                if (Screen.width <= 0 || Screen.height <= 0) return 1;
                float lw = Mathf.Log(Screen.width / 1600f, 2), lh = Mathf.Log(Screen.height / 900f, 2);
                return Mathf.Pow(2, Mathf.Lerp(lw, lh, 0.6f));
            }
        }
        // 1 点 = 多少画布单位
        public static float UnitsPerPt { get { return PxPerPt / Mathf.Max(0.05f, CanvasScale); } }
        // 视口高度（点；网页版的 innerHeight）
        public static float ViewHeightPt { get { return Screen.height / PxPerPt; } }
        internal static void UpdateFloors()
        {
            float u = UnitsPerPt;
            MinFont = Mathf.Max(1, Mathf.CeilToInt(FontFloorPt * u - 0.01f));
            MinTouch = TouchMinPt * u;
        }
        internal static void FireScaleChanged()
        {
            if (Root != null) foreach (var fb in Root.GetComponentsInChildren<FontBase>(true)) fb.Apply();
            if (ScaleChanged != null) { try { ScaleChanged(); } catch (Exception e) { Debug.LogException(e); } }
        }
        // 套用字号下限
        public static int Fs(int size) { return Mathf.Max(size, MinFont); }
        // 富文本 <size=…>（套用字号下限）
        public static string Sz(int size) { return "<size=" + Fs(size) + ">"; }
        // 头像纹理的像素边长：显示尺寸（画布单位）× 画布缩放，取 8 的倍数（便于缓存复用）
        public static int PortraitPx(float size)
        {
            return Mathf.Clamp(Mathf.RoundToInt(size * CanvasScale / 8f) * 8, 24, 256);
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
        // 圆角 + 上下渐变（小标签的底色：Lv 的金色渐变等）；按颜色缓存
        static readonly Dictionary<string, Sprite> roundGrad = new Dictionary<string, Sprite>();
        public static Sprite RoundedGrad(Color top, Color bottom)
        {
            string key = ColorUtility.ToHtmlStringRGBA(top) + ColorUtility.ToHtmlStringRGBA(bottom);
            Sprite sp;
            if (roundGrad.TryGetValue(key, out sp) && sp != null) return sp;
            const int size = 32, radius = 9;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0);
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    var c = Color.Lerp(bottom, top, (y + 0.5f) / size);
                    c.a *= Mathf.Clamp01(0.5f - d);
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            float b = radius + 1;
            sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            roundGrad[key] = sp;
            return sp;
        }
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
            t.font = title ? Title : Body; t.fontSize = Fs(size); t.color = col; t.alignment = align; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = true;
            var fb = rt.gameObject.AddComponent<FontBase>(); fb.size = size; fb.applied = t.fontSize;
            if (UIText.HasMarks(text)) SetText(t, text);   // 第三版：行内头像 / 小标签
            return t;
        }

        // 自上而下排版的一段文字：左上角放在 (x, -y)、宽 w，高度按内容；返回高度（画布单位）
        public static float FlowText(RectTransform parent, string text, int size, Color col, float x, float y, float w, bool title = false, string name = "Text")
        {
            var t = Label(parent, text, size, col, TextAnchor.UpperLeft, title, name);
            Place(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, 10));
            float h = Mathf.Ceil(t.preferredHeight) + 2;
            t.rectTransform.sizeDelta = new Vector2(w, h);
            return h;
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
            AddTouchPad(bg.rectTransform);
            return new Btn { button = b, bg = bg, label = l, rt = bg.rectTransform };
        }

        // 触控目标：在按钮下加一块不绘制的命中区，按钮小于 MinTouch 时向外扩展（点击由按钮本身处理）
        public static void AddTouchPad(RectTransform rt)
        {
            if (rt.GetComponent<TouchPad>() != null) return;
            var hit = NewRect("Hit", rt);
            Stretch(hit);
            hit.SetAsFirstSibling();
            hit.gameObject.AddComponent<HitArea>();
            rt.gameObject.AddComponent<TouchPad>().hit = hit;
        }

        // 头像（UGUI Image）。size 为显示尺寸（画布单位）；纹理按 PortraitPx 生成并由 Portrait 缓存管理（不必释放）。
        // Portrait.Bind：已缓存立即显示，否则先用同一张脸的其它尺寸 / 后台生成（长列表、消息行不卡帧）；同步生成失败时留暗色占位
        public static Image PortraitImg(Transform parent, General g, float size, PortraitMood mood = PortraitMood.Neutral, string name = "Portrait")
        {
            var im = Img(parent, null, Color.white, name);
            im.raycastTarget = false;
            im.preserveAspect = true;
            im.rectTransform.sizeDelta = new Vector2(size, size);
            BindPortrait(im, g, size, mood);
            return im;
        }
        public static void BindPortrait(Image im, General g, float size, PortraitMood mood = PortraitMood.Neutral)
        {
            if (im == null) return;
            try { if (!Portrait.Bind(im, g, PortraitPx(size), Portrait.FactionColorOf(g), mood)) { im.enabled = true; im.color = new Color(0.2f, 0.19f, 0.24f, 0.9f); } }
            catch (Exception e) { Debug.LogWarning("portrait " + (g != null ? g.name : "?") + ": " + e.Message); im.enabled = true; im.color = new Color(0.2f, 0.19f, 0.24f, 0.9f); }
        }

        // 第三版 §5 头像尺寸（画布单位；网页版 UI.FACE_REM × 20）：列表行 50、对话说话人 96、行内 38、名片 88、升级通知 68
        public static float FaceItem { get { return Portrait.FaceUnits(Portrait.FaceRem.Item); } }
        public static float FaceSay { get { return Portrait.FaceUnits(Portrait.FaceRem.Say); } }
        public static float FaceInline { get { return Portrait.FaceUnits(Portrait.FaceRem.Inline); } }
        public static float FaceCard { get { return Portrait.FaceUnits(Portrait.FaceRem.Card); } }
        public const float FaceLvUp = 68;          // facePx(3.4)
        public static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }

        // ---------------------------------------------------------- 行内头像与小标签 --
        // 文字（可含 UIText 的标记）设到 Text 上：标记换成同宽的透明占位，InlineDeco 组件按字符位置在占位处叠放头像与小标签。
        // 没有标记时与直接设 text 相同。
        // faceEm：头像占位的字号倍数（缺省 FaceEm；城池情报、提示用 FaceEmSmall）
        public static void SetText(Text t, string s, float faceEm = FaceEm)
        {
            if (t == null) return;
            var deco = t.GetComponent<InlineDeco>();
            if (!UIText.HasMarks(s))
            {
                t.text = s ?? "";
                if (deco != null) deco.Clear();
                return;
            }
            t.supportRichText = true;
            string disp = ToSlots(s, t.fontSize, faceEm);
            t.text = disp;
            if (deco == null) deco = t.gameObject.AddComponent<InlineDeco>();
            deco.source = s; deco.display = disp; deco.faceEm = faceEm;
            deco.MarkDirty();
        }
        // 占位的写法（InlineDeco 按同样的式样找回）：
        //   头像：<size=S><color=#feXXXX00>　</color></size>  S = 字号 × 1.62（行内头像 1.55em，两侧留一点空），XXXX = 武将编号（十六进制）
        //   标签：<size=B><color=#fdSS0000><b> 文字 </b></color></size>  B = 标签字号，SS = 样式；两侧的空格是标签的内边距
        public const float FaceEm = 1.62f;
        // 城池情报与提示里的行内头像略小：1.45em（.sg-citystats / .sg-toast .sg-face）÷ 0.95（头像画成占位的 0.95 倍）
        public const float FaceEmSmall = 1.53f;
        public static string ToSlots(string s, int fontSize, float faceEm = FaceEm)
        {
            if (!UIText.HasMarks(s)) return s ?? "";
            int fs = Mathf.Max(1, fontSize);
            return UIText.Rewrite(s, id => UIText.SlotFace(id, Mathf.Max(MinFont, Mathf.RoundToInt(fs * faceEm))), (st, txt) => UIText.SlotTag(st, txt, TagSize(st, fs)));
        }

        // 小标签的样式（← style.css：.sg-lv / .sg-lv-new / .sg-tag / .sg-sp-tier / .sg-learn-kind / .sg-learn-tier / .sg-say-cmd）
        public sealed class TagLook
        {
            public float em;                 // 字号（所在文字的倍数）
            public Color text, top, bottom;  // 字色、底色（上 → 下渐变）
            public Color ring;               // 描边（a = 0 无）
            public bool title, pulse, outlineOnly;
        }
        static readonly Dictionary<TagStyle, TagLook> tagLooks = new Dictionary<TagStyle, TagLook>();
        static Color C32(int r, int g, int b, float a = 1) { return new Color(r / 255f, g / 255f, b / 255f, a); }
        public static TagLook LookOf(TagStyle st)
        {
            TagLook L;
            if (tagLooks.TryGetValue(st, out L)) return L;
            Color dark = C32(0x1a, 0x12, 0x08), cin = C32(0xc8, 0x38, 0x2c), none = new Color(0, 0, 0, 0);
            switch (st)
            {
                case TagStyle.Lv: L = new TagLook { em = 0.72f, text = dark, top = C32(0xff, 0xe7, 0xa3), bottom = C32(0xe9, 0xb9, 0x50), ring = none }; break;
                case TagStyle.LvRow: L = new TagLook { em = 0.68f, text = dark, top = C32(0xff, 0xe7, 0xa3), bottom = C32(0xe9, 0xb9, 0x50), ring = none }; break;
                case TagStyle.New: L = new TagLook { em = 0.66f, text = C32(0xff, 0xf4, 0xe6), top = cin, bottom = cin, ring = none, pulse = true }; break;
                case TagStyle.Ruler: L = new TagLook { em = 0.82f, text = dark, top = C32(0xff, 0x9a, 0x7a), bottom = C32(0xf0, 0x8c, 0x6d), ring = none, title = true }; break;
                case TagStyle.Gov: L = new TagLook { em = 0.82f, text = dark, top = C32(0xf3, 0xc9, 0x69), bottom = C32(0xe0, 0xb5, 0x58), ring = none, title = true }; break;
                case TagStyle.SpTier: L = new TagLook { em = 0.62f, text = C32(0xf5, 0xed, 0xdb), top = new Color(0.953f, 0.788f, 0.412f, 0.2f), bottom = new Color(0.953f, 0.788f, 0.412f, 0.2f), ring = new Color(0.953f, 0.788f, 0.412f, 0.45f) }; break;
                case TagStyle.SpSig: L = new TagLook { em = 0.62f, text = C32(0xff, 0xf2, 0xe0), top = cin, bottom = cin, ring = none }; break;
                case TagStyle.KindSpecial: L = new TagLook { em = 0.62f, text = C32(0xff, 0xf2, 0xe0), top = cin, bottom = cin, ring = none }; break;
                case TagStyle.KindTactic: L = new TagLook { em = 0.62f, text = C32(0xff, 0xf2, 0xe0), top = C32(0x3f, 0x6f, 0xb0), bottom = C32(0x3f, 0x6f, 0xb0), ring = none }; break;
                case TagStyle.Tier1: L = new TagLook { em = 1f, text = dark, top = C32(0xbf, 0xb6, 0xa2), bottom = C32(0xbf, 0xb6, 0xa2), ring = none }; break;
                case TagStyle.Tier2: L = new TagLook { em = 1f, text = dark, top = C32(0x9f, 0xd3, 0xa4), bottom = C32(0x9f, 0xd3, 0xa4), ring = none }; break;
                case TagStyle.Tier3: L = new TagLook { em = 1f, text = dark, top = C32(0x8c, 0xc0, 0xff), bottom = C32(0x8c, 0xc0, 0xff), ring = none }; break;
                case TagStyle.Tier4: L = new TagLook { em = 1f, text = dark, top = C32(0xff, 0xe7, 0xa3), bottom = C32(0xe9, 0xb9, 0x50), ring = none }; break;
                default: L = new TagLook { em = 0.95f, text = Gold, top = none, bottom = none, ring = new Color(0.953f, 0.788f, 0.412f, 0.45f), outlineOnly = true }; break;   // Cmd
            }
            tagLooks[st] = L;
            return L;
        }
        public static int TagSize(TagStyle st, int fontSize) { return Mathf.Max(MinFont, Mathf.RoundToInt(fontSize * LookOf(st).em)); }
        // 习得对话框右栏的等阶标签
        public static TagStyle TierTag(int tier) { return tier >= 4 ? TagStyle.Tier4 : tier == 3 ? TagStyle.Tier3 : tier == 2 ? TagStyle.Tier2 : TagStyle.Tier1; }

        // 等级小标签「Lv5」（第三版成长；成长关闭时为空串）
        public static string LvTag(General g, TagStyle st = TagStyle.Lv)
        {
            if (!Progress.enabled || g == null) return "";
            int lv = g.level;
            try { lv = Progress.ExpInfo(g).level; } catch (Exception) { /* 用原值 */ }
            if (!(lv >= 1)) return "";
            return UIText.Tag(st, "Lv" + lv);
        }

        // 经验进度条（.sg-expbar：深色槽 + 金色填充，打开时 0.9 秒自左长出）。返回槽的 RectTransform（左上角锚点，调用方定位）
        public static RectTransform ExpBar(Transform parent, double pct, float w, float h)
        {
            var bg = Img(parent, RR, new Color(0, 0, 0, 0.5f), "ExpBar");
            bg.raycastTarget = false;
            bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0, 1); bg.rectTransform.pivot = new Vector2(0, 0.5f);
            bg.rectTransform.sizeDelta = new Vector2(w, h);
            var ring = Img(bg.transform, RROutline, new Color(0.953f, 0.788f, 0.412f, 0.18f), "Ring"); Stretch(ring.rectTransform, -1, -1, -1, -1); ring.raycastTarget = false;
            var fill = Img(bg.transform, RR, C32(0xff, 0xd7, 0x6e), "Fill"); fill.raycastTarget = false;
            var fr = fill.rectTransform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0, 1); fr.offsetMin = Vector2.zero; fr.offsetMax = Vector2.zero;
            var glow = fill.gameObject.AddComponent<Shadow>(); glow.effectColor = new Color(1f, 0.84f, 0.43f, 0.45f); glow.effectDistance = new Vector2(0, 0);
            var anim = fill.gameObject.AddComponent<ExpFill>(); anim.target = Mathf.Clamp01((float)pct);
            return bg.rectTransform;
        }

        // ---------------------------------------------------------- 头像名片 --
        // 头像 + 姓名 + 一行副标题 + 一行数据（← UI.genCard：俘虏处置、登用确认、选择君主确认、习得对话框）
        public sealed class GenCardSpec
        {
            public General gen;
            public string sub;                      // 副标题（富文本，可含标记）
            public string stats;                    // 数据行；null = 缺省「武 智 政」，"" = 不显示
            public PortraitMood mood = PortraitMood.Neutral;
            public double bar = -1;                 // ≥ 0：副标题后接一条短经验条（.sg-expbar.is-inline），其后为 subAfter
            public string subAfter;
        }
        // 在 parent（左上角为原点）的 (x, y) 处排一张名片，宽 w；返回高度
        public static float GenCard(RectTransform parent, GenCardSpec c, float x, float y, float w)
        {
            if (c == null || c.gen == null) return 0;
            var g = c.gen;
            float face = FaceCard, gap = 19, tx = x + face + gap, tw = Mathf.Max(80, w - face - gap);
            var pim = PortraitImg(parent, g, face, c.mood);
            pim.rectTransform.anchorMin = pim.rectTransform.anchorMax = new Vector2(0, 1); pim.rectTransform.pivot = new Vector2(0, 1);
            var ring = Img(pim.transform, RROutline, new Color(0.953f, 0.788f, 0.412f, 0.35f), "Ring"); Stretch(ring.rectTransform, -2, -2, -2, -2); ring.raycastTarget = false;
            // 文字列：姓名（标题字体、金色）、副标题（灰）、数据
            string stats = c.stats != null ? c.stats : string.Format("武 <b><color=#ffe08a>{0}</color></b>　智 <b><color=#ffe08a>{1}</color></b>　政 <b><color=#ffe08a>{2}</color></b>", g.war, g.intel, g.pol);
            var col = NewRect("Card", parent);
            col.anchorMin = col.anchorMax = new Vector2(0, 1); col.pivot = new Vector2(0, 1);
            float cy = 0;
            var nm = Label(col, g.name, 29, Gold, TextAnchor.UpperLeft, true, "Name");
            Place(nm.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(tw, 38));
            cy += 38;
            if (!string.IsNullOrEmpty(c.sub) || c.bar >= 0)
            {
                var sl = Label(col, "", 19, Muted, TextAnchor.UpperLeft, false, "Sub");
                sl.horizontalOverflow = HorizontalWrapMode.Overflow;
                SetText(sl, c.sub ?? "");
                float sw = string.IsNullOrEmpty(c.sub) ? 0 : sl.preferredWidth;
                Place(sl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -cy), new Vector2(Mathf.Min(tw, sw + 4), 28));
                if (c.bar >= 0)
                {
                    float bx = sw + (sw > 0 ? 8 : 0);
                    var bar = ExpBar(col, c.bar, 90, 8);
                    bar.anchoredPosition = new Vector2(bx, -cy - 14);
                    if (!string.IsNullOrEmpty(c.subAfter))
                    {
                        var sa = Label(col, "", 19, Muted, TextAnchor.UpperLeft, false, "SubAfter");
                        SetText(sa, c.subAfter);
                        Place(sa.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(bx + 98, -cy), new Vector2(Mathf.Max(40, tw - bx - 98), 28));
                    }
                }
                cy += 28;
            }
            if (!string.IsNullOrEmpty(stats))
            {
                var st = Label(col, "", 20, Text, TextAnchor.UpperLeft, false, "Stats");
                SetText(st, stats);
                Place(st.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -cy - 2), new Vector2(tw, 28));
                cy += 30;
            }
            float h = Mathf.Max(face, cy);
            Place(pim.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - (h - face) / 2), new Vector2(face, face));
            Place(col, new Vector2(0, 1), new Vector2(0, 1), new Vector2(tx, -y - (h - cy) / 2), new Vector2(tw, cy));
            return h;
        }
        // 当前游戏中按姓名找武将（对话的说话人）；找不到为 null
        public static General FindGeneral(string name)
        {
            var gs = GameState.Current;
            if (gs == null || string.IsNullOrEmpty(name)) return null;
            foreach (var g in gs.generals) if (g.name == name) return g;
            return null;
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
        // 取组件，没有就加上。不用 ??：编辑器里 GetComponent 找不到时返回的是“假 null”对象，?? 认不出（Unity 的 == 才认得）
        public static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c == null) c = go.AddComponent<T>();
            return c;
        }
        public static LayoutElement Size(Component c, float w = -1, float h = -1, float flexW = -1)
        {
            var le = Ensure<LayoutElement>(c.gameObject);
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
        public class Modal
        {
            public RectTransform blocker, panel;
            public void Close() { sidePanels.Remove(panel); if (blocker != null) UnityEngine.Object.Destroy(blocker.gameObject); }
        }

        // side：对话框靠左（距左缘 2% 屏宽），遮罩改为自左向右渐淡，地图留在右侧可见（网页版 .sg-side-modal）
        public static Modal OpenModal(float w, float h, bool dim = true, bool side = false)
        {
            ClearUISelection();
            Image bl;
            if (side) { bl = Img(ModalLayer, SideShade, Color.white, "Modal"); bl.type = Image.Type.Simple; }
            else bl = Img(ModalLayer, null, new Color(0, 0, 0, dim ? 0.45f : 0.001f), "Modal");
            var blocker = bl.rectTransform;
            Stretch(blocker);
            var panel = Panel(blocker);
            h = Mathf.Min(h, MaxModalHeight);
            if (side)
            {
                float x = Mathf.Max(12, ModalLayer.rect.width * 0.02f);
                Place(panel, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(Mathf.Min(w, ModalLayer.rect.width - 2 * x), h));
                sidePanels.Add(panel);
            }
            else Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));
            panel.gameObject.AddComponent<PopIn>();
            return new Modal { blocker = blocker, panel = panel };
        }
        public static bool AnyModal { get { return ModalLayer != null && ModalLayer.childCount > 0; } }
        // 对话框的最大高度（画布可用高度减去上下边距）
        public static float MaxModalHeight { get { float h = ModalLayer != null ? ModalLayer.rect.height : 900; return Mathf.Max(240, (h > 0 ? h : 900) - 40); } }

        static readonly List<RectTransform> sidePanels = new List<RectTransform>();
        // 最上层的靠左对话框右侧的空白区：frac = 空白占屏宽的比例，dxPx = 空白区中心相对屏幕中心的偏移（屏幕像素）。
        // 没有靠左对话框或空白不足 160 点时返回 false（frac = 1，dxPx = 0）
        public static bool SideFree(out float frac, out float dxPx)
        {
            frac = 1; dxPx = 0;
            sidePanels.RemoveAll(p => p == null);
            if (sidePanels.Count == 0) return false;
            var p0 = sidePanels[sidePanels.Count - 1];
            var corners = new Vector3[4];
            p0.GetWorldCorners(corners);   // 屏幕空间覆盖画布：世界坐标即屏幕像素
            float vw = Mathf.Max(1, Screen.width), right = corners[2].x;
            float free = vw - right;
            if (free < 160 * PxPerPt) return false;
            frac = free / vw; dxPx = (right + vw) / 2 - vw / 2;
            return true;
        }
        static Sprite sideShade;
        // 靠左对话框的遮罩：自左向右 rgba(0,0,0,.55) → .25（55%）→ .04
        static Sprite SideShade
        {
            get
            {
                if (sideShade != null) return sideShade;
                var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int x = 0; x < 64; x++)
                {
                    float u = x / 63f;
                    float a = u < 0.55f ? Mathf.Lerp(0.55f, 0.25f, u / 0.55f) : Mathf.Lerp(0.25f, 0.04f, (u - 0.55f) / 0.45f);
                    tex.SetPixel(x, 0, new Color(0, 0, 0, a));
                }
                tex.Apply();
                sideShade = Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f));
                return sideShade;
            }
        }

        // 对话的附加选项（← UI.say 的 opts）
        public sealed class SayOpts
        {
            public PortraitMood mood = PortraitMood.Neutral;   // 说话人头像的表情
            public string sub;                                  // 姓名下的小字（身份 / 势力 / 指令名；富文本，可含标记）
            public bool alarm;                                  // 急报（.is-alarm：红色描边、头像告警光）
            public bool help;                                   // 操作说明（.sg-help：字略小，七节一屏放得下）
        }

        // 对话：说话者头像（或姓氏徽章）+ 文字（打字机），点击 / 回车 / 空格继续。speaker 为姓名（找得到该武将时显示头像）
        public static IEnumerator Say(string text, string speaker = null, Color? speakerColor = null)
        {
            return SayCore(text, string.IsNullOrEmpty(speaker) ? null : FindGeneral(speaker), string.IsNullOrEmpty(speaker) ? null : speaker, speakerColor, null);
        }
        // 第三版：说话人为武将对象（null = 旁白），opts 为表情、姓名下的小字、急报 / 说明样式
        public static IEnumerator Say(string text, General speaker, SayOpts opts, Color? speakerColor = null)
        {
            return SayCore(text, speaker, speaker != null ? speaker.name : null, speakerColor, opts);
        }

        // 版式（← style.css .sg-say）：宽 55rem、底边上方 1.5rem；左右内边距 1.8 / 2.2rem，说话人一栏 7rem、与文字间隔 1.6rem；
        // 高度随文字（至少 10.5rem、至多 24rem，超出时文字区可滚动）
        static IEnumerator SayCore(string text, General who, string name, Color? speakerColor, SayOpts opts)
        {
            opts = opts ?? new SayOpts();
            bool done = false;
            ClearUISelection();
            var blocker = Img(ModalLayer, null, new Color(0, 0, 0, 0.25f), "Say").rectTransform;
            Stretch(blocker);
            var panel = Panel(blocker);
            const float W = 1100, padL = 36, padR = 44, padT = 28, padB = 30, colW = 140, gap = 32;
            float maxH = Mathf.Min(480, MaxModalHeight - 30);
            if (opts.alarm)
            {
                // 急报：描边改为红色，外加一圈淡红光
                var ol = panel.Find("Outline"); if (ol != null) ol.GetComponent<Image>().color = new Color(1f, 0.45f, 0.38f, 0.75f);
                var halo = Img(panel, RROutline, new Color(0.78f, 0.22f, 0.17f, 0.5f), "AlarmHalo"); Stretch(halo.rectTransform, -4, -4, -4, -4); halo.raycastTarget = false;
            }
            panel.gameObject.AddComponent<PopIn>();
            float x = padL;
            RectTransform spCol = null; float spH = 0;
            if (!string.IsNullOrEmpty(name))
            {
                // 说话人一栏：头像（找不到该武将时退回姓氏徽章）、姓名（长名缩小、七字以上折行）、小字
                spCol = NewRect("Speaker", panel);
                float face = FaceSay;
                Image m;
                if (who != null)
                {
                    m = PortraitImg(spCol, who, face, opts.mood);
                    var fr = Img(m.transform, RROutline, new Color(0.953f, 0.788f, 0.412f, 0.3f), "Ring"); Stretch(fr.rectTransform, -2, -2, -2, -2); fr.raycastTarget = false;
                    if (opts.alarm) { var gl = m.gameObject.AddComponent<AlarmGlow>(); gl.Build(); }
                }
                else m = Medal(spCol, name.Substring(0, 1), speakerColor ?? Cinnabar, 96);
                Place(m.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(96, 96));
                spH = 96 + 11;
                int nlen = GrowthText.Cps(name).Count;
                bool xlong = nlen >= 7;
                var n = Label(spCol, name, xlong ? 21 : nlen >= 5 ? 22 : 24, Gold, TextAnchor.UpperCenter, true, "Name");
                var ns = n.gameObject.AddComponent<Shadow>(); ns.effectColor = new Color(0.24f, 0.1f, 0, 0.9f); ns.effectDistance = new Vector2(0, -1);
                n.horizontalOverflow = xlong ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                float nw = xlong ? 160 : colW + 40;
                Place(n.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -spH), new Vector2(nw, 30));
                float nh = Mathf.Max(30, Mathf.Ceil(n.preferredHeight) + 2);
                n.rectTransform.sizeDelta = new Vector2(nw, nh);
                spH += nh;
                if (!string.IsNullOrEmpty(opts.sub))
                {
                    var sb = Label(spCol, "", 16, Muted, TextAnchor.UpperCenter, false, "Sub");
                    Place(sb.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -spH + 2), new Vector2(colW + 30, 24));
                    SetText(sb, opts.sub);
                    float sh = Mathf.Max(22, Mathf.Ceil(sb.preferredHeight) + 2);
                    sb.rectTransform.sizeDelta = new Vector2(colW + 30, sh);
                    spH += sh;
                }
                x = padL + colW + gap;
            }
            // 文字：先按全文排版定出高度（打字机逐字显示时版面不变）
            float tw = W - x - padR;
            int fsz = opts.help ? 22 : 30;
            var view = NewRect("TextView", panel);
            var t = Label(view, "", fsz, Text, TextAnchor.UpperLeft, false, "Text");
            t.lineSpacing = opts.help ? 1.11f : 1.2f;
            var ts = t.gameObject.AddComponent<Shadow>(); ts.effectColor = new Color(0, 0, 0, 0.5f); ts.effectDistance = new Vector2(0, -1);
            text = text ?? "";
            string display = ToSlots(text, t.fontSize);
            bool marks = UIText.HasMarks(text);
            if (marks) t.gameObject.AddComponent<InlineDeco>();
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0, 1); t.rectTransform.pivot = new Vector2(0, 1);
            t.rectTransform.sizeDelta = new Vector2(tw, 10);
            t.text = display;
            float th = Mathf.Ceil(t.preferredHeight) + 4;
            t.rectTransform.sizeDelta = new Vector2(tw, th);
            t.rectTransform.anchoredPosition = Vector2.zero;
            float inner = Mathf.Max(th, spH);
            float h = Mathf.Clamp(inner + padT + padB, 210, Mathf.Max(210, maxH));
            Place(panel, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(W, h));
            float viewH = h - padT - padB;
            if (th > viewH + 1)
            {
                // 过长：文字区可滚动（自上而下）
                view.gameObject.AddComponent<RectMask2D>();
                var vimg = view.gameObject.AddComponent<Image>(); vimg.color = new Color(0, 0, 0, 0.001f);
                var sr = view.gameObject.AddComponent<ScrollRect>();
                sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30;
                sr.content = t.rectTransform; sr.viewport = view;
                Place(view, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -padT), new Vector2(tw, viewH));
            }
            else Place(view, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -padT - (viewH - th) / 2), new Vector2(tw, th));   // 竖直居中（.sg-say-inner）
            if (spCol != null) Place(spCol, new Vector2(0, 1), new Vector2(0, 1), new Vector2(padL, -padT - Mathf.Max(0, (viewH - spH) / 2)), new Vector2(colW, spH));
            var hint = Label(panel, "▼", 20, Gold, TextAnchor.LowerRight);
            Stretch(hint.rectTransform, 0, 12, 22, 0);
            var btn = blocker.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            int shown = 0; bool full = false;
            bool rich = display.IndexOf('<') >= 0;
            int visible = rich ? RichPrefix(display, int.MaxValue).Value : display.Length;
            t.text = "";
            btn.onClick.AddListener(() => { if (!full) { full = true; } else done = true; });
            while (!done)
            {
                if (!full)
                {
                    // 打字机：富文本按可见字截取（标签照留）；行内头像占位算一个字，打到时才出现
                    shown = Mathf.Min(visible, shown + 2);
                    t.text = rich ? RichPrefix(display, shown).Key : display.Substring(0, shown);
                    if (shown >= visible) full = true;
                }
                else if (!ReferenceEquals(t.text, display)) t.text = display;
                hint.enabled = full && Mathf.Repeat(Time.unscaledTime, 1f) < 0.65f;
                if ((KeyEnter() || Input.GetKeyDown(KeyCode.Escape)) && IsTopModal(blocker)) { if (full) done = true; else full = true; }
                yield return null;
            }
            Sfx.Click();
            UnityEngine.Object.Destroy(blocker.gameObject);
        }

        // 富文本的前 n 个可见字：完整保留标签，并补上尚未闭合的标签。→（文字，全部可见字数）（实现在 UIText.RichPrefix，可无头测试）
        public static KeyValuePair<string, int> RichPrefix(string text, int n)
        {
            int total;
            var r = UIText.RichPrefix(text, n, out total);
            return new KeyValuePair<string, int>(r, total);
        }

        public class Item
        {
            public string label, right, desc; public bool enabled = true; public bool selected;
            public General portrait;          // 左侧头像（第三版：所有列出武将的列表，UI.item 的 avatar；尺寸 FaceItem = 50）
            public float portraitSize = 44;
            public PortraitMood portraitMood = PortraitMood.Neutral;
            public List<General> faces;       // 名称之后的一排小头像（选择地域）
            public int moreFaces;             // 头像之外还有几位（「+N」）
            public bool header;               // 分组标题行（不可选；网页版 .sg-item-group）
            public bool big;                  // 名称用标题字体（金色），说明另起一行（选择剧本）
            public bool descBlock;            // 说明总是另起一行（可多行：习得对话框、必杀 / 策略菜单的数值摘要与典故）
            public Item(string l, string r = null, bool e = true, string d = null) { label = l; right = r; enabled = e; desc = d; }
            // 第三版 §5：行首头像（= UI.item(label, right, enabled, desc, { avatar })）
            public Item(string l, string r, bool e, string d, General avatar) : this(l, r, e, d)
            {
                if (avatar != null) { portrait = avatar; portraitSize = FaceItem; }
            }
        }

        // Choose 的附加选项
        public class ChooseOpts
        {
            public bool side;                 // 对话框靠左，地图留在右侧（见 OpenModal）
            public Action<int> hover;         // 鼠标悬停某行时回调（去抖 0.14 秒；触屏不触发）
            public int initial = -1;          // 打开时先对该行回调一次，并滚动到可见
            public float nameMin;             // 有 faces 的行：名称的最小宽度（em）
            public GenCardSpec card;          // 第三版：说明区放一张头像名片（俘虏处置、习得对话框；= info 里的 UI.genCard）
        }

        // 不可选的列表行（← .sg-item:disabled { opacity: .4; filter: grayscale(.4) }）：整行（底、文字、头像、行内标签）淡到 .4。
        // 底色不再另按按钮的 disabledColor 变暗（网页版只有整体透明度）；灰度滤镜在 UGUI 里无对应，从略
        public const float DimAlpha = 0.4f;
        static void DimRow(Btn b)
        {
            var cb = b.button.colors; cb.disabledColor = cb.normalColor; b.button.colors = cb;
            var g = Ensure<CanvasGroup>(b.bg.gameObject);
            g.alpha = DimAlpha;
        }

        static float RowHeight(Item it, float itemH)
        {
            if (it.header) return 44;
            float h = itemH;
            if (it.big || it.faces != null) h = Mathf.Max(h, 84);
            if (it.portrait != null) h = Mathf.Max(h, it.portraitSize + 14);
            return h;
        }

        // 列表选择：返回索引，取消为 -1。标题、info、各行的 label / right / desc 都可含 UIText 的标记（行内头像、小标签）
        public static IEnumerator Choose(string title, List<Item> items, Ref<int> result, string info = null, float width = 640, ChooseOpts opts = null)
        {
            result.v = -2;
            opts = opts ?? new ChooseOpts();
            float itemH = Mathf.Max(62, MinTouch);
            float listH = 0;
            foreach (var it in items) listH += RowHeight(it, itemH) + 8;
            float h = Mathf.Min(760, 140 + (info != null ? 60 : 0) + (opts.card != null ? 110 : 0) + listH);
            var m = OpenModal(width, h, true, opts.side);
            width = m.panel.sizeDelta.x;
            float rowW = width - 44;
            var head = Label(m.panel, "", 32, Gold, TextAnchor.MiddleLeft, true);
            Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -18), new Vector2(width - 120, 48));
            SetText(head, title);
            float top = 76;
            if (opts.card != null)
            {
                // 头像名片（.sg-dlg-info 里的 .sg-gencard）
                var holder = NewRect("Card", m.panel);
                Place(holder, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(width, 0));
                float ch = GenCard(holder, opts.card, 30, top - 4, width - 60);
                top += ch + 10;
            }
            float infoH = 0;
            if (info != null)
            {
                var inf = Label(m.panel, "", 21, Muted, TextAnchor.UpperLeft);
                Place(inf.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -top + 6), new Vector2(width - 60, 56));
                SetText(inf, info);
                infoH = Mathf.Clamp(Mathf.Ceil(inf.preferredHeight) + 4, 30, 120);
                inf.rectTransform.sizeDelta = new Vector2(width - 60, Mathf.Max(56, infoH));
                top += Mathf.Max(58, infoH + 4);
            }
            var close = Button(m.panel, "×", () => result.v = -1, 24);
            Place(close.rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(52, 48));
            ScrollRect sr;
            var content = Scroll(m.panel, out sr);
            Stretch((RectTransform)sr.transform, 22, 22, 22, top);
            int pending = -1, last = -1; float pendingAt = 0;
            Action<int> onHover = i => { if (i == last) return; last = i; pending = i; pendingAt = Time.unscaledTime; };
            var rows = new List<RectTransform>();
            var rings = new List<Image>();
            listH = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var it = items[i];
                float rh = RowHeight(it, itemH);
                if (it.header)
                {
                    rings.Add(null);
                    var row = Img(content, null, new Color(0, 0, 0, 0), "Group");
                    var hl = Label(row.transform, it.label, 22, Gold, TextAnchor.LowerLeft, true);
                    Stretch(hl.rectTransform, 12, 6, 12, 0);
                    if (it.right != null) { var hr = Label(row.transform, it.right, 17, Muted, TextAnchor.LowerRight); Stretch(hr.rectTransform, 12, 6, 12, 0); }
                    var line = Img(row.transform, null, new Color(0.95f, 0.79f, 0.42f, 0.28f), "Line");
                    line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(1, 0);
                    line.rectTransform.offsetMin = new Vector2(6, 0); line.rectTransform.offsetMax = new Vector2(-6, 1.5f);
                    Size(row, -1, rh);
                    rows.Add(row.rectTransform);
                    listH += rh + 8;
                    continue;
                }
                var b = Button(content, "", () => { if (items[idx].enabled) result.v = idx; }, 26, false, "Item");
                float x0 = 20;
                Image pim = null;
                if (it.portrait != null)
                {
                    // 行首头像（.sg-item-face：圆角、淡金描边）
                    pim = PortraitImg(b.rt, it.portrait, it.portraitSize, it.portraitMood);
                    Place(pim.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(it.portraitSize, it.portraitSize));
                    var fr = Img(pim.transform, RROutline, new Color(0.953f, 0.788f, 0.412f, 0.22f), "Ring"); Stretch(fr.rectTransform, -1, -1, -1, -1); fr.raycastTarget = false;
                    x0 = 12 + it.portraitSize + 12;
                }
                b.label.alignment = TextAnchor.MiddleLeft;
                b.label.rectTransform.offsetMin = new Vector2(x0, 2);
                Text rtx = null;
                if (it.right != null)
                {
                    rtx = Label(b.rt, "", 22, Muted, TextAnchor.MiddleRight);
                    Stretch(rtx.rectTransform, 20, 0, 20, 0);
                    SetText(rtx, it.right);
                }
                if (it.big || it.faces != null)
                {
                    // 两行：名称（标题字体 / 头像）在上，说明在下
                    b.label.rectTransform.anchorMin = new Vector2(0, 0.5f);
                    b.label.rectTransform.offsetMin = new Vector2(x0, 0);
                    b.label.rectTransform.offsetMax = new Vector2(-8, -4);
                    if (it.big) { b.label.font = Title; b.label.color = Gold; b.label.GetComponent<FontBase>().Set(30); }
                    else b.label.font = Title;   // 地域名（网页版 .sg-region-name：标题字体）
                    SetText(b.label, it.label);
                    if (it.desc != null)
                    {
                        var d = Label(b.rt, "", 18, Muted, TextAnchor.UpperLeft);
                        d.rectTransform.anchorMin = new Vector2(0, 0); d.rectTransform.anchorMax = new Vector2(1, 0.5f);
                        d.rectTransform.offsetMin = new Vector2(x0, 4); d.rectTransform.offsetMax = new Vector2(-20, -2);
                        SetText(d, it.desc);
                    }
                    if (it.faces != null)
                    {
                        float fs = b.label.fontSize;
                        float x = x0 + Mathf.Max(b.label.preferredWidth, opts.nameMin * fs) + 16;
                        const float face = 30;
                        foreach (var fg in it.faces)
                        {
                            var fim = PortraitImg(b.rt, fg, face);
                            Place(fim.rectTransform, new Vector2(0, 0.75f), new Vector2(0, 0.5f), new Vector2(x, -2), new Vector2(face, face));
                            x += face + 4;
                        }
                        if (it.moreFaces > 0)
                        {
                            var more = Label(b.rt, "+" + it.moreFaces, 18, Muted, TextAnchor.MiddleLeft);
                            Place(more.rectTransform, new Vector2(0, 0.75f), new Vector2(0, 0.5f), new Vector2(x + 4, -2), new Vector2(60, 30));
                        }
                    }
                    if (rtx != null) rtx.rectTransform.anchorMin = new Vector2(0, 0.5f);
                }
                else
                {
                    // 一行：名称 + 说明（小字）……右栏；放不下、或说明本身多行时说明另起一行（按实际高度）
                    float rightW = rtx != null ? rtx.preferredWidth : 0;
                    bool block = it.descBlock || (it.desc != null && it.desc.IndexOf('\n') >= 0);
                    if (it.desc != null && !block)
                    {
                        SetText(b.label, it.label + "  " + Sz(18) + "<color=#a8a194>" + it.desc + "</color></size>");
                        if (b.label.preferredWidth + rightW + x0 + 40 > rowW) block = true;
                    }
                    else SetText(b.label, it.label);
                    if (it.desc != null && block)
                    {
                        SetText(b.label, it.label);
                        const float lineH = 40, padY = 8;
                        b.label.rectTransform.anchorMin = new Vector2(0, 1); b.label.rectTransform.anchorMax = new Vector2(1, 1);
                        b.label.rectTransform.pivot = new Vector2(0.5f, 1);
                        b.label.rectTransform.offsetMin = new Vector2(x0, -padY - lineH); b.label.rectTransform.offsetMax = new Vector2(-(rightW + 30), -padY);
                        var d = Label(b.rt, "", 18, new Color(0.85f, 0.82f, 0.74f), TextAnchor.UpperLeft, false, "Desc");
                        d.lineSpacing = 1.1f;
                        float dw = rowW - x0 - 20;
                        Place(d.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x0, -padY - lineH + 2), new Vector2(dw, 20));
                        SetText(d, it.desc);
                        float dh = Mathf.Ceil(d.preferredHeight) + 2;
                        d.rectTransform.sizeDelta = new Vector2(dw, dh);
                        float need = padY + lineH + dh + 10;
                        if (pim != null) need = Mathf.Max(need, it.portraitSize + 14);
                        rh = Mathf.Max(Mathf.Max(rh, 84), need);
                        if (rtx != null)
                        {
                            rtx.rectTransform.anchorMin = new Vector2(0, 1); rtx.rectTransform.anchorMax = new Vector2(1, 1); rtx.rectTransform.pivot = new Vector2(0.5f, 1);
                            rtx.rectTransform.offsetMin = new Vector2(20, -padY - lineH); rtx.rectTransform.offsetMax = new Vector2(-20, -padY);
                        }
                        if (pim != null) Place(pim.rectTransform, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(12, -padY - it.portraitSize / 2), new Vector2(it.portraitSize, it.portraitSize));
                    }
                }
                b.button.interactable = it.enabled;
                if (!it.enabled) DimRow(b);
                Size(b.bg, -1, rh);
                listH += rh + 8;
                if (opts.hover != null) { var hv = b.bg.gameObject.AddComponent<HoverRelay>(); hv.index = idx; hv.onHover = onHover; }
                rows.Add(b.rt);
                rings.Add(FocusRing(b.rt));
            }
            float h2 = Mathf.Min(760, top + 64 + listH);
            if (Mathf.Abs(h2 - m.panel.sizeDelta.y) > 0.5f) m.panel.sizeDelta = new Vector2(width, Mathf.Min(h2, MaxModalHeight));
            if (opts.initial >= 0 && opts.initial < items.Count)
            {
                ScrollTo(sr, content, rows[opts.initial]);
                if (opts.hover != null) onHover(opts.initial);
            }
            // 键盘（网页版 choose 的 onKey）：先聚焦预选或第一项可选行；↑↓ Home End PgUp PgDn 移动，回车 / 空格选定，1–9 直选
            Func<int, bool> enabledAt = i => i >= 0 && i < items.Count && items[i].enabled && !items[i].header;
            int active = items.FindIndex(it => it.selected && it.enabled && !it.header);
            if (active < 0) active = items.FindIndex(it => it.enabled && !it.header);
            if (active > 0 && opts.initial < 0) ScrollTo(sr, content, rows[active]);
            int frame0 = Time.frameCount;
            while (result.v == -2)
            {
                if (Time.frameCount != frame0 && IsTopModal(m.blocker))
                {
                    int dg = DigitDown();
                    if (Input.GetKeyDown(KeyCode.Escape)) result.v = -1;
                    else if (KeyEnter()) { if (enabledAt(active)) { Sfx.Click(); result.v = active; } }
                    else if (dg > 0) { if (enabledAt(dg - 1)) { Sfx.Click(); result.v = dg - 1; } }
                    else
                    {
                        int next = ListNav(items.Count, enabledAt, active);
                        if (next >= 0)
                        {
                            if (active >= 0 && rings[active] != null) rings[active].enabled = false;
                            active = next;
                            if (rings[active] != null) rings[active].enabled = true;
                            ScrollTo(sr, content, rows[active]);
                            if (opts.hover != null) onHover(active);
                        }
                    }
                }
                if (pending >= 0 && opts.hover != null && Time.unscaledTime - pendingAt >= 0.14f)
                {
                    int i = pending; pending = -1;
                    try { opts.hover(i); } catch (Exception e) { Debug.LogWarning(e); }
                }
                yield return null;
            }
            m.Close();
        }

        // 滚动列表使某一行可见（居中）
        public static void ScrollTo(ScrollRect sr, RectTransform content, RectTransform row)
        {
            if (sr == null || content == null || row == null) return;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            float vh = ((RectTransform)sr.transform).rect.height, ch = content.rect.height;
            if (ch <= vh + 1) return;
            float y = -row.anchoredPosition.y;   // 行中心到内容顶端的距离
            sr.verticalNormalizedPosition = 1 - Mathf.Clamp01((y - vh / 2) / (ch - vh));
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
            var checks = new List<Text>();
            var rings = new List<Image>();
            var groups = new List<CanvasGroup>();
            Action refresh = null;
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var it = items[i];
                var b = Button(content, "", () =>
                {
                    if (!items[idx].enabled) return;
                    if (sel.Contains(idx)) sel.Remove(idx); else if (sel.Count < max) sel.Add(idx);
                    refresh();
                }, 26, false, "Item");
                // 选中记号 ◆ / ◇，其后为行首头像（第三版 §5，.sg-choosemany .has-face）与名称
                var ck = Label(b.rt, "◇", 26, Text, TextAnchor.MiddleLeft, false, "Check");
                Place(ck.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(30, 40));
                checks.Add(ck);
                float x0 = 50;
                float rh = Mathf.Max(62, MinTouch);
                if (it.portrait != null)
                {
                    var pim = PortraitImg(b.rt, it.portrait, it.portraitSize, it.portraitMood);
                    Place(pim.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x0, 0), new Vector2(it.portraitSize, it.portraitSize));
                    var fr = Img(pim.transform, RROutline, new Color(0.953f, 0.788f, 0.412f, 0.22f), "Ring"); Stretch(fr.rectTransform, -1, -1, -1, -1); fr.raycastTarget = false;
                    x0 += it.portraitSize + 12;
                    rh = Mathf.Max(rh, it.portraitSize + 14);
                }
                b.label.alignment = TextAnchor.MiddleLeft;
                b.label.rectTransform.offsetMin = new Vector2(x0, 2);
                SetText(b.label, it.label);
                if (it.right != null) { var r = Label(b.rt, "", 22, Muted, TextAnchor.MiddleRight); Stretch(r.rectTransform, 20, 0, 20, 0); SetText(r, it.right); }
                b.button.interactable = it.enabled;
                if (!it.enabled) DimRow(b);
                Size(b.bg, -1, rh);
                buttons.Add(b);
                rings.Add(FocusRing(b.rt));
                groups.Add(Ensure<CanvasGroup>(b.bg.gameObject));
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
                    checks[i].text = on ? "<color=#f3c969>◆</color>" : "◇";
                    buttons[i].bg.color = on ? new Color(0.45f, 0.32f, 0.16f, 0.95f) : new Color(0.2f, 0.19f, 0.24f, 0.95f);
                    groups[i].alpha = !items[i].enabled ? DimAlpha : !on && sel.Count >= max ? 0.62f : 1f;   // 不可选 .4；已达上限：未选行变淡（网页版 .is-capped）
                }
                count.text = "已选 " + sel.Count + " / " + max;
                ok.button.interactable = sel.Count > 0;
            };
            refresh();
            // 键盘（网页版 chooseMany 的 onKey）：空格切换当前行，回车确定（至少选一项），↑↓ 等移动
            Func<int, bool> enabledAt = i => i >= 0 && i < items.Count && items[i].enabled;
            int active = items.FindIndex(it => it.enabled);
            int frame0 = Time.frameCount;
            while (!done && !cancel)
            {
                if (Time.frameCount != frame0 && IsTopModal(m.blocker))
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) cancel = true;
                    else if (Input.GetKeyDown(KeyCode.Space))
                    {
                        if (enabledAt(active))
                        {
                            Sfx.Click();
                            if (sel.Contains(active)) sel.Remove(active); else if (sel.Count < max) sel.Add(active);
                            refresh();
                        }
                    }
                    else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { if (sel.Count > 0) { Sfx.Click(); done = true; } }
                    else
                    {
                        int next = ListNav(items.Count, enabledAt, active);
                        if (next >= 0)
                        {
                            if (active >= 0) rings[active].enabled = false;
                            active = next; rings[active].enabled = true;
                            ScrollTo(sr, content, buttons[active].rt);
                        }
                    }
                }
                yield return null;
            }
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
            var m = OpenModal(620, 366);
            var head = Label(m.panel, title, 32, Gold, TextAnchor.MiddleLeft, true);
            Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -18), new Vector2(560, 48));
            var big = Label(m.panel, "", 54, Text, TextAnchor.MiddleCenter, true);
            Place(big.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(360, 70));
            var desc = Label(m.panel, "", 22, Muted, TextAnchor.MiddleCenter);
            Place(desc.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -150), new Vector2(560, 34));
            var slider = NewSlider(m.panel, min, max, val);
            Place((RectTransform)slider.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -196), new Vector2(520, 36));
            // 滑块两端的最小 / 最大值（网页版 .sg-range-ends）
            var endMin = Label(m.panel, min.ToString(), 18, Muted, TextAnchor.UpperLeft);
            Place(endMin.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-130, -234), new Vector2(260, 26));
            var endMax = Label(m.panel, max.ToString(), 18, Muted, TextAnchor.UpperRight);
            Place(endMax.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(130, -234), new Vector2(260, 26));
            Action upd = () => { big.text = val.ToString(); desc.text = describe != null ? describe(val) : ""; slider.value = val; };
            slider.onValueChanged.AddListener(f => { val = Mathf.Clamp(Mathf.RoundToInt(f / step) * step, min, max); big.text = val.ToString(); desc.text = describe != null ? describe(val) : ""; });
            Action dec = () => { val = Mathf.Max(min, val - step); upd(); };
            Action inc = () => { val = Mathf.Min(max, val + step); upd(); };
            // －／＋ 按住连续增减（网页版 holdable：按下即执行一次，0.38 秒后每 0.07 秒一次）
            var minus = Button(m.panel, "－", null, 34);
            Place(minus.rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-230, -80), new Vector2(70, 64));
            minus.bg.gameObject.AddComponent<HoldRepeat>().fn = dec;
            var plus = Button(m.panel, "＋", null, 34);
            Place(plus.rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(230, -80), new Vector2(70, 64));
            plus.bg.gameObject.AddComponent<HoldRepeat>().fn = inc;
            var ok = Button(m.panel, "确定", () => done = true, 28, true);
            Place(ok.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 22), new Vector2(180, 60));
            var cc = Button(m.panel, "取消", () => cancel = true, 28);
            Place(cc.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-220, 22), new Vector2(150, 60));
            upd();
            int frame0 = Time.frameCount;
            while (!done && !cancel)
            {
                if (Time.frameCount != frame0 && IsTopModal(m.blocker))
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) cancel = true;
                    else if (KeyEnter()) { Sfx.Click(); done = true; }
                    else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.DownArrow)) dec();
                    else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.UpArrow)) inc();
                    else if (Input.GetKeyDown(KeyCode.PageDown)) { val = Mathf.Max(min, val - step * 10); upd(); }
                    else if (Input.GetKeyDown(KeyCode.PageUp)) { val = Mathf.Min(max, val + step * 10); upd(); }
                    else if (Input.GetKeyDown(KeyCode.Home)) slider.value = min;
                    else if (Input.GetKeyDown(KeyCode.End)) slider.value = max;
                }
                yield return null;
            }
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

        // 确认：标题 + [否][是（主按钮）] 一排（网页版 confirm）。回车 / 空格取当前按钮（起初为「是」），方向键切换，Esc = 否
        // 第三版：body（可选）为标题与按钮之间的头像名片（← UI.confirm 的 opts.body = UI.genCard(…)；有 body 时宽 600）
        public static IEnumerator Confirm(string text, Ref<bool> result, string yes = "是", string no = "否", GenCardSpec body = null)
        {
            float width = body != null ? 600 : 520;
            int res = -2;
            var m = OpenModal(width, 220);
            var head = Label(m.panel, text, 30, Gold, TextAnchor.UpperLeft, true);
            Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -24), new Vector2(width - 110, 48));
            float th = Mathf.Max(40, head.preferredHeight);
            head.rectTransform.sizeDelta = new Vector2(width - 110, th);
            float bh = 0;
            if (body != null)
            {
                // .sg-confirm-body：padding .2rem .6rem .9rem
                var holder = NewRect("Body", m.panel);
                Place(holder, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(width, 0));
                bh = GenCard(holder, body, 42, 24 + th + 14, width - 84) + 18 + 14;
            }
            var close = Button(m.panel, "×", () => res = 0, 24);
            Place(close.rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(52, 48));
            var bYes = Button(m.panel, yes, () => res = 1, 28, true);
            Place(bYes.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 22), new Vector2(180, 60));
            var bNo = Button(m.panel, no, () => res = 0, 28);
            Place(bNo.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-220, 22), new Vector2(160, 60));
            m.panel.sizeDelta = new Vector2(width, Mathf.Min(MaxModalHeight, 24 + th + bh + 28 + 60 + 22));
            var ringYes = FocusRing(bYes.rt); var ringNo = FocusRing(bNo.rt);
            bool onYes = true;
            int frame0 = Time.frameCount;
            while (res == -2)
            {
                if (Time.frameCount != frame0 && IsTopModal(m.blocker))
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) res = 0;
                    else if (KeyEnter()) { Sfx.Click(); res = onYes ? 1 : 0; }
                    else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow))
                    {
                        onYes = !onYes; ringYes.enabled = onYes; ringNo.enabled = !onYes;
                    }
                }
                yield return null;
            }
            m.Close();
            result.v = res == 1;
        }

        // 只有一个按钮的说明框（← UI.notice：战后成长等）：标题 + ×、可滚动的说明区（body 在宽 w 的容器里自上而下排版，返回高度）、
        // 底部居中的主按钮。回车 / 空格 / Esc / × 都是关闭。说明过长时（矮屏）说明区滚动，按钮留在框底
        public static IEnumerator Notice(string title, Func<RectTransform, float, float> body, string ok = "确定", float width = 640)
        {
            bool done = false;
            var m = OpenModal(width, 300);
            width = m.panel.sizeDelta.x;
            const float pad = 30, headH = 80, footH = 104;
            var head = Label(m.panel, "", 30, Gold, TextAnchor.MiddleLeft, true);
            Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(pad, -18), new Vector2(width - 120, 48));
            SetText(head, title);
            var close = Button(m.panel, "×", () => done = true, 24);
            Place(close.rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(52, 48));
            ScrollRect sr;
            var content = Scroll(m.panel, out sr);
            content.GetComponent<VerticalLayoutGroup>().enabled = false;
            content.GetComponent<ContentSizeFitter>().enabled = false;
            float bw = width - 2 * pad;
            float bh = 0;
            if (body != null) { try { bh = body(content, bw); } catch (Exception e) { Debug.LogException(e); } }
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, bh + 6);
            float h = Mathf.Min(MaxModalHeight, headH + bh + 6 + footH);
            m.panel.sizeDelta = new Vector2(width, h);
            Stretch((RectTransform)sr.transform, pad, footH - 8, pad, headH - 6);
            var bOk = Button(m.panel, ok, () => done = true, 28, true);
            Place(bOk.rt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(Mathf.Min(360, width - 2 * pad), 62));
            var ring = FocusRing(bOk.rt); ring.enabled = true;
            int frame0 = Time.frameCount;
            while (!done)
            {
                if (Time.frameCount != frame0 && IsTopModal(m.blocker) && (KeyEnter() || Input.GetKeyDown(KeyCode.Escape))) { Sfx.Click(); done = true; }
                yield return null;
            }
            m.Close();
        }

        // ---------------------------------------------------------- 键盘 --
        static bool KeyEnter() { return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space); }
        static int DigitDown()
        {
            for (int i = 1; i <= 9; i++) if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i)) return i;
            return 0;
        }
        // 只有最上层的对话框响应按键（叠开的对话框不会一起被回车选中）
        static bool IsTopModal(RectTransform blocker)
        {
            return blocker != null && ModalLayer != null && blocker.parent == ModalLayer && blocker.GetSiblingIndex() == ModalLayer.childCount - 1;
        }
        // 打开对话框时清除 UGUI 的当前选中按钮：否则回车 / 空格会经 StandaloneInputModule 再次「提交」背后的指令按钮
        static void ClearUISelection()
        {
            var es = EventSystem.current;
            if (es != null && !es.alreadySelecting) es.SetSelectedGameObject(null);
        }
        // 键盘焦点框（金色外圈，起初隐藏）
        static Image FocusRing(RectTransform rt)
        {
            var r = Img(rt, RROutline, Gold, "Focus");
            Stretch(r.rectTransform, -2, -2, -2, -2);
            r.raycastTarget = false; r.enabled = false;
            return r;
        }
        // 网页版 listNav：↑↓ 循环跳过不可选项，Home / End 到首末，PgUp / PgDn 移 5 项（不循环）。无相关按键返回 -2
        static int ListNav(int n, Func<int, bool> enabledAt, int a)
        {
            if (n <= 0) return -2;
            Func<int, int, int> step = (from, d) =>
            {
                for (int j = 1; j <= n; j++) { int idx = ((from + d * j) % n + n) % n; if (enabledAt(idx)) return idx; }
                return -1;
            };
            if (Input.GetKeyDown(KeyCode.DownArrow)) return step(a < 0 ? -1 : a, 1);
            if (Input.GetKeyDown(KeyCode.UpArrow)) return step(a < 0 ? 0 : a, -1);
            if (Input.GetKeyDown(KeyCode.Home)) return step(-1, 1);
            if (Input.GetKeyDown(KeyCode.End)) return step(n, -1);
            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                int next = a;
                for (int j = 0; j < 5; j++) { int s2 = step(next < 0 ? -1 : next, 1); if (s2 <= next) break; next = s2; }
                return next;
            }
            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                int next = a;
                for (int j = 0; j < 5; j++) { int s2 = step(next < 0 ? 0 : next, -1); if (s2 >= next || s2 < 0) break; next = s2; }
                return next;
            }
            return -2;
        }

        // ---------------------------------------------------------- 提示 --
        const int MaxToasts = 6;
        public static RectTransform Toast(string text, float seconds = 2.2f)
        {
            // 最多同时 6 条，超出时去掉最早的（网页版 MAX_TOASTS）
            var live = new List<GameObject>();
            foreach (Transform ch in ToastLayer) if (ch.name == "Toast" && ch.gameObject.activeSelf) live.Add(ch.gameObject);
            for (int i = 0; i <= live.Count - MaxToasts; i++) { live[i].SetActive(false); UnityEngine.Object.Destroy(live[i]); }
            var p = Img(ToastLayer, RR, new Color(0.05f, 0.05f, 0.08f, 0.88f), "Toast").rectTransform;
            var ol = Img(p, RROutline, new Color(0.953f, 0.788f, 0.412f, 0.32f), "Outline"); Stretch(ol.rectTransform, 1, 1, 1, 1); ol.raycastTarget = false;
            var t = Label(p, "", 24, Text, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 24, 6, 24, 6);
            SetText(t, text, FaceEmSmall);       // 第三版：可含行内头像（UIText.Face / Faces；.sg-toast .sg-face 1.45em）
            // 宽度先按单行估计（不超过 1100），过长时换行，高度随内容
            p.anchorMin = p.anchorMax = new Vector2(0.5f, 1); p.pivot = new Vector2(0.5f, 1);
            float w = Mathf.Min(1100, t.preferredWidth + 60);
            p.sizeDelta = new Vector2(w, 50);
            float th = t.preferredHeight;
            if (th > 40) p.sizeDelta = new Vector2(w, th + 14);
            // 位置由 ToastStack 每帧排定：平时自顶栏下方依次往下；有对话框（底部对话除外）打开时移到屏幕下方、自下而上
            p.anchoredPosition = new Vector2(0, -110);
            p.gameObject.AddComponent<AutoFade>().life = seconds;
            return p;
        }
        // 有对话框（底部对话 Say 除外）打开：提示移到屏幕下方（← style.css #ui:has(.sg-modal:not(.sg-say-modal)) .sg-toast-stack）
        public static bool DialogOpen
        {
            get
            {
                if (ModalLayer == null) return false;
                foreach (Transform ch in ModalLayer) if (ch.gameObject.activeSelf && ch.name != "Say") return true;
                return false;
            }
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
            // 横幅期间压暗世界标签层（部队信息卡、城名牌），横幅开始淡出时恢复（网页版 bannerDim）
            rt.gameObject.AddComponent<BannerDim>().undimAt = Mathf.Max(0, seconds - Mathf.Min(1f / 3, seconds / 3));
        }
        internal static int liveBanners;

        // ---------------------------------------------------------- 动效 --
        // 抖动（灰色指令被点按：0.38 秒左右摇晃，网页版 sg-cmd-deny）
        public static void Shake(RectTransform rt)
        {
            if (rt == null) return;
            var s = Ensure<UIShake>(rt.gameObject);
            s.Restart();
        }
        // 金色光圈闪烁（提示用，网页版 sg-cmd-hint：每次 1.1 秒，持续 seconds 秒）
        public static void Glow(RectTransform rt, float seconds = 4.4f)
        {
            if (rt == null || seconds <= 0) return;
            var g = Ensure<UIGlow>(rt.gameObject);
            g.Restart(seconds);
        }
        public static void StopGlow(RectTransform rt) { var g = rt != null ? rt.GetComponent<UIGlow>() : null; if (g != null) g.Stop(); }

        // ---------------------------------------------------------- 协程防护 --
        // 逐层展开嵌套的 IEnumerator 运行 body；任一层抛出异常时停止并回调 onError（网页版的 try / catch）。
        // 其余 yield 值（null、WaitForSeconds、Coroutine…）原样交给 Unity。
        public static IEnumerator Guard(IEnumerator body, Action<Exception> onError)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            while (stack.Count > 0)
            {
                object cur;
                try
                {
                    var top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    cur = top.Current;
                }
                catch (Exception e)
                {
                    // 先由内向外 Dispose 仍挂起的各层，让它们的 finally 照常执行（网页版 async 的 try / finally 总会运行）
                    foreach (var it in stack)
                    {
                        var d = it as IDisposable;
                        if (d == null) continue;
                        try { d.Dispose(); } catch (Exception de) { Debug.LogException(de); }
                    }
                    stack.Clear();
                    if (onError != null) onError(e); else Debug.LogException(e);
                    yield break;
                }
                var sub = cur as IEnumerator;
                if (sub != null) { stack.Push(sub); continue; }
                yield return cur;
            }
        }

        // ---------------------------------------------------------- 载入画面 --
        // 网页版 #loading：径向渐变底、朱红「霸」印（缓慢脉动）、金色标题、阶段 + 百分比、进度条（未知进度时来回扫动）。
        // ShowLoading(title) → Loading：Set(frac, label) 显示进度，Hide() 淡出（0.6 秒）后自行销毁，Error(msg) 显示错误
        public class Loading
        {
            public RectTransform root;
            internal Text title, text; internal RectTransform bar, fill; internal Image seal, fillImg;
            internal bool det, hiding;
            public bool Visible { get { return root != null && !hiding; } }
            public void SetTitle(string t) { if (root != null && !string.IsNullOrEmpty(t)) title.text = LoadingTitle(t); }
            public void Set(float frac, string label)
            {
                if (root == null) return;
                int pct = Mathf.RoundToInt(Mathf.Clamp01(frac) * 100);
                text.text = (string.IsNullOrEmpty(label) ? "正在绘制山河" : label) + "…… " + pct + "%";
                if (!det) { det = true; fillImg.sprite = BarGrad; fillImg.type = Image.Type.Simple; }
                fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(pct / 100f, 1);
                fill.offsetMin = fill.offsetMax = Vector2.zero;
            }
            public void Error(string msg)
            {
                if (root == null) return;
                text.text = msg; text.color = new Color(1f, 0.55f, 0.45f);
                bar.gameObject.SetActive(false);
            }
            public void Hide()
            {
                if (root == null || hiding) return;
                hiding = true;
                root.GetComponent<LoadingAnim>().FadeOut();
            }
        }
        public static Loading ShowLoading(string title, bool fadeIn = true)
        {
            var L = new Loading();
            var root = L.root = NewRect("Loading", Root);
            Stretch(root);
            root.SetAsLastSibling();
            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = LoadingBg; bg.color = Color.white; bg.raycastTarget = true;
            // 纵向居中排列：印 84、间距 22、标题、间距 22、文字、间距 22、进度条
            L.seal = Img(root, RR, new Color32(0xc8, 0x38, 0x2c, 255), "Seal");
            Place(L.seal.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 71), new Vector2(84, 84));
            var sh = L.seal.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.5f); sh.effectDistance = new Vector2(0, -8);
            var ring = Img(L.seal.transform, RROutline, new Color(1, 0.86f, 0.67f, 0.25f), "Ring"); Stretch(ring.rectTransform, 1, 1, 1, 1);
            var g = Label(L.seal.transform, "霸", 56, new Color32(0xff, 0xf2, 0xe0, 255), TextAnchor.MiddleCenter, true);
            Stretch(g.rectTransform);
            L.title = Label(root, LoadingTitle(string.IsNullOrEmpty(title) ? Game.Title : title), 40, new Color32(0xf3, 0xc9, 0x69, 255), TextAnchor.MiddleCenter, true);
            L.title.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(L.title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -17), new Vector2(900, 52));
            var ts = L.title.gameObject.AddComponent<Shadow>(); ts.effectColor = new Color32(0x4d, 0x1f, 0x05, 255); ts.effectDistance = new Vector2(0, -2);
            L.text = Label(root, "正在绘制山河……", 18, new Color32(0xd8, 0xcf, 0xbb, 255), TextAnchor.MiddleCenter, true);
            Place(L.text.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -75), new Vector2(900, 30));
            var barBg = Img(root, null, new Color(0.95f, 0.79f, 0.42f, 0.15f), "Bar");
            L.bar = barBg.rectTransform;
            Place(L.bar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -111), new Vector2(220, 3));
            barBg.gameObject.AddComponent<RectMask2D>();
            L.fillImg = Img(L.bar, SweepGrad, Color.white, "Fill");
            L.fillImg.type = Image.Type.Simple;
            L.fill = L.fillImg.rectTransform;
            L.fill.anchorMin = new Vector2(-0.44f, 0); L.fill.anchorMax = new Vector2(-0.04f, 1); L.fill.offsetMin = L.fill.offsetMax = Vector2.zero;
            var anim = root.gameObject.AddComponent<LoadingAnim>();
            anim.L = L;
            if (fadeIn) anim.FadeIn();
            return L;
        }
        // 游戏名分两段排版（「三国志」小字米白 +「-」+「霸王的世界」），同 index.html 的 #loading .sg-loading-title（.sg-lt-pre / .sg-lt-dash 为 .62em）
        public static string LoadingTitle(string t)
        {
            var m = System.Text.RegularExpressions.Regex.Match(t ?? "", "^(三国志)-(.+)$");
            if (!m.Success) return t ?? "";
            return "<size=25><color=#f5eddb>" + m.Groups[1].Value + "</color></size><size=25><color=#f3c969b3> - </color></size>" + m.Groups[2].Value;
        }
        static Sprite loadingBg, barGrad, sweepGrad;
        // radial-gradient(ellipse at 50% 42%, #2a2531 0%, #16151d 55%, #0c0c11 100%)（farthest-corner）
        static Sprite LoadingBg
        {
            get
            {
                if (loadingBg != null) return loadingBg;
                int n = 96;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                Color c0 = new Color32(0x2a, 0x25, 0x31, 255), c1 = new Color32(0x16, 0x15, 0x1d, 255), c2 = new Color32(0x0c, 0x0c, 0x11, 255);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                        float dx = (u - 0.5f) / 0.7071f, dy = (v - 0.58f) / 0.8202f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        tex.SetPixel(x, y, d < 0.55f ? Color.Lerp(c0, c1, d / 0.55f) : Color.Lerp(c1, c2, Mathf.Clamp01((d - 0.55f) / 0.45f)));
                    }
                tex.Apply();
                loadingBg = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
                return loadingBg;
            }
        }
        static Sprite HGrad(Func<float, Color> f)
        {
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++) tex.SetPixel(x, 0, f(x / 63f));
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f));
        }
        // 确定进度：rgba(243,201,105,.5) → #f3c969
        static Sprite BarGrad { get { return barGrad ?? (barGrad = HGrad(u => new Color(0.953f, 0.788f, 0.412f, Mathf.Lerp(0.5f, 1f, u)))); } }
        // 扫动：透明 → #f3c969 → 透明
        static Sprite SweepGrad { get { return sweepGrad ?? (sweepGrad = HGrad(u => new Color(0.953f, 0.788f, 0.412f, 1 - Mathf.Abs(u * 2 - 1)))); } }
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
    // 横幅存续期间计入 UIKit.liveBanners（到 undimAt 秒或被销毁时释放）
    public class BannerDim : MonoBehaviour
    {
        public float undimAt;
        float t; bool held;
        void Awake() { held = true; UIKit.liveBanners++; }
        void Update() { t += Time.unscaledDeltaTime; if (t >= undimAt) Release(); }
        void OnDestroy() { Release(); }
        void Release() { if (!held) return; held = false; UIKit.liveBanners = Mathf.Max(0, UIKit.liveBanners - 1); }
    }
    // 标签层的压暗（0.2 秒过渡到 0.15 / 1）。别处（交锋）改了这层的透明度时暂停，直到它恢复为本组件最后写入的值
    public class LabelDim : MonoBehaviour
    {
        CanvasGroup g; float written = 1;
        void Awake() { g = GetComponent<CanvasGroup>(); if (g != null) written = g.alpha; }
        void Update()
        {
            if (g == null || Mathf.Abs(g.alpha - written) > 0.0005f) return;
            float target = UIKit.liveBanners > 0 ? 0.15f : 1f;
            if (written == target) return;
            written = Mathf.MoveTowards(written, target, Time.unscaledDeltaTime / 0.2f);
            g.alpha = written;
        }
    }
    // 按住连续触发（按下执行一次，0.38 秒后每 0.07 秒一次；抬起或移出即停）
    public class HoldRepeat : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public Action fn;
        bool held; float t, next;
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            var b = GetComponent<Button>(); if (b != null && !b.IsInteractable()) return;
            Sfx.Click();
            if (fn != null) fn();
            held = true; t = 0; next = 0.38f;
        }
        public void OnPointerUp(PointerEventData e) { held = false; }
        public void OnPointerExit(PointerEventData e) { held = false; }
        void OnDisable() { held = false; }
        void Update()
        {
            if (!held) return;
            t += Time.unscaledDeltaTime;
            while (held && t >= next) { next += 0.07f; if (fn != null) fn(); }
        }
    }
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

    // 记录 Label 的原始字号；界面缩放变化时（UIScaleWatch）重新套用字号下限
    public class FontBase : MonoBehaviour
    {
        public int size, applied;
        public void Set(int s)
        {
            size = s;
            var t = GetComponent<Text>();
            if (t != null) { t.fontSize = UIKit.Fs(s); applied = t.fontSize; }
        }
        public void Apply()
        {
            var t = GetComponent<Text>();
            if (t == null) return;
            if (t.fontSize != applied) size = t.fontSize;   // 外部改过字号：以它为新的原始字号
            int was = t.fontSize;
            t.fontSize = UIKit.Fs(size); applied = t.fontSize;
            if (was != t.fontSize) { var d = GetComponent<InlineDeco>(); if (d != null) d.Refont(); }   // 行内头像 / 标签的占位随字号
        }
    }
    // 屏幕尺寸或 dpi 变化时更新字号下限与触控目标
    public class UIScaleWatch : MonoBehaviour
    {
        int w, h; float dpi;
        void Start() { w = Screen.width; h = Screen.height; dpi = Screen.dpi; UIKit.UpdateFloors(); UIKit.FireScaleChanged(); }
        void LateUpdate()
        {
            if (Screen.width == w && Screen.height == h && Screen.dpi == dpi) return;
            w = Screen.width; h = Screen.height; dpi = Screen.dpi;
            int f0 = UIKit.MinFont; float t0 = UIKit.MinTouch;
            UIKit.UpdateFloors();
            if (f0 != UIKit.MinFont || Mathf.Abs(t0 - UIKit.MinTouch) > 0.01f) UIKit.FireScaleChanged();
        }
    }
    // 触控目标：按钮小于 MinTouch 时，把不绘制的命中区（hit）向外扩展到 MinTouch
    public class TouchPad : MonoBehaviour
    {
        public RectTransform hit;
        Vector2 lastSize = new Vector2(-1, -1); float lastMin = -1;
        RectTransform rt;
        void LateUpdate()
        {
            if (hit == null) return;
            if (rt == null) rt = (RectTransform)transform;
            var sz = rt.rect.size;
            float min = UIKit.MinTouch;
            if (sz == lastSize && min == lastMin) return;
            lastSize = sz; lastMin = min;
            float ex = Mathf.Max(0, (min - sz.x) / 2), ey = Mathf.Max(0, (min - sz.y) / 2);
            hit.offsetMin = new Vector2(-ex, -ey); hit.offsetMax = new Vector2(ex, ey);
        }
    }
    // 不绘制、只接收点击的图形
    public class HitArea : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh) { vh.Clear(); }
    }
    // 列表行的鼠标悬停（触屏不触发，同网页版 pointerType === 'mouse'）
    public class HoverRelay : MonoBehaviour, IPointerEnterHandler
    {
        public int index; public Action<int> onHover;
        public void OnPointerEnter(PointerEventData e)
        {
            if (onHover != null && e.pointerId < 0 && Input.touchCount == 0) onHover(index);
        }
    }
    // 左右摇晃 0.38 秒（关键帧 0 −5 5 −3 2 0）
    public class UIShake : MonoBehaviour
    {
        static readonly float[] keys = { 0, -5, 5, -3, 2, 0 };
        float t = -1; Vector2 basePos; RectTransform rt;
        public void Restart()
        {
            rt = (RectTransform)transform;
            if (t < 0) basePos = rt.anchoredPosition;
            t = 0; enabled = true;
        }
        void Update()
        {
            if (t < 0 || rt == null) { enabled = false; return; }
            t += Time.unscaledDeltaTime;
            if (t >= 0.38f) { rt.anchoredPosition = basePos; t = -1; enabled = false; return; }
            float k = t / 0.38f * 5;
            int i = Mathf.Min(4, (int)k);
            rt.anchoredPosition = basePos + new Vector2(Mathf.Lerp(keys[i], keys[i + 1], k - i), 0);
        }
    }
    // 金色光圈：每 1.1 秒一次由无到有再消失（ease-in-out），持续 life 秒
    public class UIGlow : MonoBehaviour
    {
        Image ring, halo; float t, life;
        public void Restart(float seconds)
        {
            if (ring == null)
            {
                halo = UIKit.Img(transform, UIKit.RROutline, Color.clear, "GlowHalo"); UIKit.Stretch(halo.rectTransform, -7, -7, -7, -7); halo.raycastTarget = false;
                ring = UIKit.Img(transform, UIKit.RROutline, Color.clear, "GlowRing"); UIKit.Stretch(ring.rectTransform, -2, -2, -2, -2); ring.raycastTarget = false;
            }
            t = 0; life = seconds; enabled = true; Set(0);
        }
        public void Stop() { t = life; if (ring != null) Set(0); enabled = false; }
        void Set(float a)
        {
            ring.color = new Color(0.953f, 0.788f, 0.412f, 0.95f * a);
            halo.color = new Color(0.953f, 0.788f, 0.412f, 0.4f * a);
        }
        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (t >= life) { Stop(); return; }
            float ph = (t % 1.1f) / 1.1f;
            Set(0.5f - 0.5f * Mathf.Cos(ph * 2 * Mathf.PI));
        }
    }
    // 载入画面的动画：淡入淡出（0.6 秒）、印的脉动（2.4 秒）、未知进度时进度条扫动（1.4 秒）
    public class LoadingAnim : MonoBehaviour
    {
        public UIKit.Loading L;
        CanvasGroup g; float t, fade = 1, fadeDir, sinceHide;
        void Awake() { g = gameObject.AddComponent<CanvasGroup>(); }
        public void FadeIn() { fade = 0; fadeDir = 1; g.alpha = 0; }
        public void FadeOut() { fadeDir = -1; g.blocksRaycasts = false; g.interactable = false; }
        void Update()
        {
            float dt = Mathf.Min(0.1f, Time.unscaledDeltaTime);
            t += dt;
            if (fadeDir != 0)
            {
                fade = Mathf.Clamp01(fade + fadeDir * dt / 0.6f);
                g.alpha = fade;
                if (fadeDir > 0 && fade >= 1) fadeDir = 0;
            }
            if (fadeDir < 0) { sinceHide += dt; if (sinceHide > 0.7f) { Destroy(gameObject); return; } }
            if (L == null) return;
            if (L.seal != null) L.seal.rectTransform.localScale = Vector3.one * (1 + 0.05f * (0.5f - 0.5f * Mathf.Cos(t / 2.4f * 2 * Mathf.PI)));
            if (!L.det && L.fill != null)
            {
                float ph = (t % 1.4f) / 1.4f, k = 0.5f - 0.5f * Mathf.Cos(ph * Mathf.PI);
                float p = Mathf.Lerp(-0.44f, 1.04f, k);
                L.fill.anchorMin = new Vector2(p, 0); L.fill.anchorMax = new Vector2(p + 0.4f, 1);
                L.fill.offsetMin = L.fill.offsetMax = Vector2.zero;
            }
        }
    }

    // ---------------------------------------------------------- 第三版：行内头像与小标签 --
    // 挂在 Text 上：在 UIKit.ToSlots 生成的占位处叠放头像（Portrait.Bind）与小标签（圆角底 + 字）。
    // 文字、尺寸或画布缩放变化时重新排定：用一个 TextGenerator 按 Text 自己的生成设置重新排版，取占位字符的位置
    // （UICharInfo.cursorPos / charWidth，所在行的 UILineInfo）。字符下标有两种：含富文本标签（旧版 Unity）或去掉标签
    // （新版 Unity 只为可见字生成网格）——按 characterCount 判断，再用头像占位的字宽（应约为一个全角字）核对，不对就换另一种。
    // 打字机（UIKit.Say）逐字截取时，没打到的占位不完整（缺全角空格 / 标签文字），式样对不上，自然不显示。
    public class InlineDeco : MonoBehaviour
    {
        public string source, display;      // SetText 设下的原文（含标记）与占位后的文字（字号下限变化时据此重排）
        public float faceEm = UIKit.FaceEm;  // 头像占位的字号倍数（SetText 的 faceEm）
        Text t;
        string last; Vector2 lastSize; float lastPpu = -1;
        TextGenerator gen;
        sealed class FaceView { public Image img; public int id = -1; public int px = -1; }
        sealed class TagView { public RectTransform rt; public Image bg, ring; public Text label; public TagStyle style; public string text; public int size = -1; }
        readonly List<FaceView> faces = new List<FaceView>();
        readonly List<TagView> tags = new List<TagView>();

        public void MarkDirty() { last = null; }
        public void Clear() { last = null; source = display = null; HideFrom(0, 0); }
        // 字号下限变化（FontBase.Apply）：整段显示中的文字按新字号重排占位
        public void Refont()
        {
            if (t == null) t = GetComponent<Text>();
            if (t == null || source == null || t.text != display) return;
            display = UIKit.ToSlots(source, t.fontSize, faceEm);
            t.text = display;
            last = null;
        }

        void LateUpdate()
        {
            if (t == null) t = GetComponent<Text>();
            if (t == null) return;
            var size = t.rectTransform.rect.size;
            float ppu = t.pixelsPerUnit;
            string s = t.text;
            if (ReferenceEquals(s, last) && size == lastSize && ppu == lastPpu) return;
            last = s; lastSize = size; lastPpu = ppu;
            try { Layout(s, size, ppu); }
            catch (Exception e) { Debug.LogException(e); HideFrom(0, 0); }
        }

        static int LineOf(IList<UILineInfo> lines, int ci)
        {
            int li = 0;
            for (int k = 0; k < lines.Count; k++) { if (lines[k].startCharIdx <= ci) li = k; else break; }
            return li;
        }

        void Layout(string s, Vector2 size, float ppu)
        {
            int nFace, nTag;
            var slots = UIText.FindSlots(s, out nFace, out nTag);
            if (slots.Count == 0 || size.x <= 0 || size.y <= 0) { HideFrom(0, 0); if (slots.Count > 0) last = null; return; }   // 尚未排版：下一帧再试
            if (ppu <= 0) ppu = 1;
            var vis = UIText.VisIndex(s);
            if (gen == null) gen = new TextGenerator(s.Length + 16);
            var set = t.GetGenerationSettings(size);
            gen.Populate(s, set);
            var chars = gen.characters;
            var lines = gen.lines;
            if (chars == null || chars.Count == 0 || lines == null || lines.Count == 0) { HideFrom(0, 0); return; }
            bool raw = gen.characterCount >= s.Length;
            Func<int, bool, int> map = (ri, r) => { int ci = r ? ri : vis[Mathf.Clamp(ri, 0, s.Length)]; return ci >= 0 && ci < chars.Count ? ci : -1; };
            // 用第一个头像占位核对下标方式：全角空格的字宽应约为一个字号
            foreach (var sl in slots)
            {
                if (sl.kind != 'F') continue;
                Func<bool, bool> fits = r => { int ci = map(sl.first, r); if (ci < 0) return false; float w = chars[ci].charWidth / ppu; return w > sl.size * 0.5f && w < sl.size * 1.5f; };
                if (!fits(raw) && fits(!raw)) raw = !raw;
                break;
            }
            var pivot = t.rectTransform.pivot;
            int nf = 0, nt = 0;
            foreach (var sl in slots)
            {
                if (sl.kind == 'F')
                {
                    int ci = map(sl.first, raw);
                    var g = UIText.GenById(GameState.Current, sl.id);
                    if (ci < 0 || g == null) continue;
                    var c = chars[ci];
                    var line = lines[LineOf(lines, ci)];
                    float x = (c.cursorPos.x + c.charWidth / 2) / ppu, y = (line.topY - line.height / 2f) / ppu;
                    float fsz = sl.size * 0.95f;
                    var fv = FaceAt(nf++);
                    var rt = fv.img.rectTransform;
                    rt.anchorMin = rt.anchorMax = pivot; rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(fsz, fsz);
                    float bindSize = Mathf.Max(fsz, UIKit.FaceInline);
                    int px = UIKit.PortraitPx(bindSize);
                    if (fv.id != sl.id || fv.px != px) { fv.id = sl.id; fv.px = px; fv.img.color = Color.white; UIKit.BindPortrait(fv.img, g, bindSize); }
                    fv.img.gameObject.SetActive(true);
                }
                else
                {
                    int c0 = map(sl.first, raw), c1 = map(sl.last, raw);
                    if (c0 < 0 || c1 < 0) continue;
                    int l0 = LineOf(lines, c0);
                    if (LineOf(lines, c1) != l0) continue;          // 跨行的标签不画底
                    var line = lines[l0];
                    float x0 = chars[c0].cursorPos.x / ppu, x1 = (chars[c1].cursorPos.x + chars[c1].charWidth) / ppu;
                    if (!(x1 > x0)) continue;
                    float y = (line.topY - line.height / 2f) / ppu;
                    var tv = TagAt(nt++);
                    if (tv.style != sl.style || tv.text != sl.text || tv.size != sl.size) StyleTag(tv, sl.style, sl.text, sl.size);
                    tv.rt.anchorMin = tv.rt.anchorMax = pivot; tv.rt.pivot = new Vector2(0.5f, 0.5f);
                    tv.rt.anchoredPosition = new Vector2((x0 + x1) / 2, y);
                    tv.rt.sizeDelta = new Vector2(x1 - x0, Mathf.Round(sl.size * 1.45f));
                    tv.rt.gameObject.SetActive(true);
                }
            }
            HideFrom(nf, nt);
        }

        FaceView FaceAt(int i)
        {
            while (faces.Count <= i)
            {
                var img = UIKit.Img(transform, null, Color.white, "Face");
                img.raycastTarget = false; img.preserveAspect = true;
                var ring = UIKit.Img(img.transform, UIKit.RROutline, new Color(0.953f, 0.788f, 0.412f, 0.3f), "Ring");
                UIKit.Stretch(ring.rectTransform, -1, -1, -1, -1); ring.raycastTarget = false;
                faces.Add(new FaceView { img = img });
            }
            return faces[i];
        }
        TagView TagAt(int i)
        {
            while (tags.Count <= i)
            {
                var bg = UIKit.Img(transform, UIKit.RR, Color.white, "Tag");
                bg.raycastTarget = false;
                var ring = UIKit.Img(bg.transform, UIKit.RROutline, Color.clear, "Ring");
                UIKit.Stretch(ring.rectTransform); ring.raycastTarget = false;
                var lb = UIKit.Label(bg.transform, "", 14, Color.white, TextAnchor.MiddleCenter, false, "Text");
                UIKit.Stretch(lb.rectTransform, -6, 0, -6, 0);
                lb.horizontalOverflow = HorizontalWrapMode.Overflow;
                tags.Add(new TagView { rt = bg.rectTransform, bg = bg, ring = ring, label = lb });
            }
            return tags[i];
        }
        static void StyleTag(TagView tv, TagStyle st, string txt, int sz)
        {
            var L = UIKit.LookOf(st);
            tv.style = st; tv.text = txt; tv.size = sz;
            if (L.outlineOnly) { tv.bg.sprite = UIKit.RR; tv.bg.type = Image.Type.Sliced; tv.bg.color = new Color(0, 0, 0, 0); }
            else { tv.bg.sprite = UIKit.RoundedGrad(L.top, L.bottom); tv.bg.type = Image.Type.Sliced; tv.bg.color = Color.white; }
            tv.ring.color = L.ring;
            tv.label.font = L.title ? UIKit.Title : UIKit.Body;
            tv.label.fontSize = sz;
            var fb = tv.label.GetComponent<FontBase>(); if (fb != null) { fb.size = sz; fb.applied = sz; }
            tv.label.color = L.text;
            tv.label.text = "<b>" + txt + "</b>";
            var pulse = tv.bg.GetComponent<TagPulse>();
            if (L.pulse && pulse == null) tv.bg.gameObject.AddComponent<TagPulse>();
            else if (!L.pulse && pulse != null) Destroy(pulse);
        }
        void HideFrom(int nf, int nt)
        {
            for (int i = nf; i < faces.Count; i++) if (faces[i].img != null && faces[i].img.gameObject.activeSelf) faces[i].img.gameObject.SetActive(false);
            for (int i = nt; i < tags.Count; i++) if (tags[i].rt != null && tags[i].rt.gameObject.activeSelf) tags[i].rt.gameObject.SetActive(false);
        }
    }

    // 「新」标签的闪烁光晕（← @keyframes sg-lv-new：1.6 秒，中点 0 0 .5rem rgba(255,120,90,.75)）
    public class TagPulse : MonoBehaviour
    {
        Image halo; float t;
        void Start()
        {
            halo = UIKit.Img(transform, UIKit.RROutline, new Color(1f, 0.47f, 0.35f, 0), "Halo");
            UIKit.Stretch(halo.rectTransform, -4, -3, -4, -3); halo.raycastTarget = false;
            halo.transform.SetAsFirstSibling();
        }
        void Update()
        {
            if (halo == null) return;
            t += Time.unscaledDeltaTime;
            float k = 0.5f - 0.5f * Mathf.Cos(t / 1.6f * 2 * Mathf.PI);
            halo.color = new Color(1f, 0.47f, 0.35f, 0.75f * k);
        }
    }

    // 急报：来犯主将头像的告警光（← @keyframes sg-alarm-glow：1.3 秒；红圈 2px，外晕 .6rem → 1.6rem）
    public class AlarmGlow : MonoBehaviour
    {
        Image ring, halo; float t;
        public void Build()
        {
            ring = UIKit.Img(transform, UIKit.RROutline, new Color(0.78f, 0.22f, 0.17f, 0.9f), "AlarmRing");
            UIKit.Stretch(ring.rectTransform, -3, -3, -3, -3); ring.raycastTarget = false;
            halo = UIKit.Img(transform, UIKit.RROutline, new Color(1f, 0.31f, 0.24f, 0.35f), "AlarmHalo");
            UIKit.Stretch(halo.rectTransform, -10, -10, -10, -10); halo.raycastTarget = false;
        }
        void Update()
        {
            if (ring == null) return;
            t += Time.unscaledDeltaTime;
            float k = 0.5f - 0.5f * Mathf.Cos(t / 1.3f * 2 * Mathf.PI);
            ring.color = Color.Lerp(new Color(0.78f, 0.22f, 0.17f, 0.9f), new Color(1f, 0.45f, 0.38f, 1f), k);
            halo.color = new Color(1f, 0.31f, 0.24f, Mathf.Lerp(0.35f, 0.75f, k));
            float e = Mathf.Lerp(6, 18, k);
            UIKit.Stretch(halo.rectTransform, -e, -e, -e, -e);
        }
    }

    // 经验条填充：打开时 0.9 秒自左长出（← @keyframes sg-exp-fill，ease-out）
    public class ExpFill : MonoBehaviour
    {
        public float target;
        float t; RectTransform rt;
        void OnEnable() { t = 0; }
        void Update()
        {
            if (rt == null) rt = (RectTransform)transform;
            t += Time.unscaledDeltaTime / 0.9f;
            float k = 1 - Mathf.Pow(1 - Mathf.Clamp01(t), 3);
            rt.anchorMax = new Vector2(target * k, 1);
            if (t >= 1) enabled = false;
        }
    }

    // 提示的排布（← .sg-toast-stack：顶栏下方 5.5rem 起依次往下，间隔 .45rem；有对话框（底部对话除外）打开时移到屏幕下方 1rem、
    // 自下而上（column-reverse：最早的一条在最下）），免得盖住对话框标题（如连续习得新招时）
    public class ToastStack : MonoBehaviour
    {
        readonly List<RectTransform> live = new List<RectTransform>();
        void LateUpdate()
        {
            live.Clear();
            foreach (Transform ch in transform) if (ch.name == "Toast" && ch.gameObject.activeSelf) live.Add((RectTransform)ch);
            if (live.Count == 0) return;
            bool bottom = UIKit.DialogOpen;
            float y = bottom ? 20 : 110;
            foreach (var p in live)
            {
                if (bottom)
                {
                    p.anchorMin = p.anchorMax = new Vector2(0.5f, 0); p.pivot = new Vector2(0.5f, 0);
                    p.anchoredPosition = new Vector2(0, y);
                }
                else
                {
                    p.anchorMin = p.anchorMax = new Vector2(0.5f, 1); p.pivot = new Vector2(0.5f, 1);
                    p.anchoredPosition = new Vector2(0, -y);
                }
                y += p.sizeDelta.y + 9;
            }
        }
    }
}
