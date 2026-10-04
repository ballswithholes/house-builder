// Lanternvale IMGUI toolkit: theme, scaling, panels, buttons, bars, icons and tooltips.
//
// All UI is immediate-mode (OnGUI) so it works in any project setup (no EventSystem, no TMP
// essentials, either input backend). Screens call Ui.BeginFrame() once per OnGUI, then draw in a
// virtual 1920x1080-high space (width follows the aspect ratio).
//
// Look: two families of surfaces —
//   * Parchment (warm cream, ink outline, soft shadow): menus, dialogue, character sheets.
//   * Ink (deep translucent blue-violet with a thin gold edge): HUD, tooltips, combat log — keeps
//     WoW quality colours readable over painted backgrounds.
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class Ui
    {
        public const float RefHeight = 1080f;

        // ---------------------------------------------------------------- palette
        public static readonly Color Ink = Hex("#2b2238");
        public static readonly Color InkSoft = Hex("#5b4f6e");
        public static readonly Color Paper = Hex("#fbf3e1");
        public static readonly Color PaperShade = Hex("#efe0c2");
        public static readonly Color Gold = Hex("#e8c374");
        public static readonly Color GoldDeep = Hex("#b98a3e");
        public static readonly Color Night = new Color(0.12f, 0.10f, 0.20f, 0.88f);
        public static readonly Color TextLight = Hex("#fff6e6");
        public static readonly Color TextMuted = Hex("#b8acc9");
        public static readonly Color Good = Hex("#7fd47a");
        public static readonly Color Bad = Hex("#ff7a6b");
        public static readonly Color Health = Hex("#6cc66a");
        public static readonly Color HealthLow = Hex("#e8584d");
        public static readonly Color Mana = Hex("#4f8de8");
        public static readonly Color Rage = Hex("#d9483b");
        public static readonly Color Energy = Hex("#f2d24b");
        public static readonly Color Focus = Hex("#e8954a");
        public static readonly Color Time = Hex("#9fe3e0");

        public static Color Hex(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c)) return c;
            return Color.white;
        }

        public static Color SchoolColor(School s)
        {
            switch (s)
            {
                case School.Holy: return Hex("#f3d36b");
                case School.Fire: return Hex("#f08a4b");
                case School.Nature: return Hex("#78c06a");
                case School.Frost: return Hex("#79c3e6");
                case School.Shadow: return Hex("#9268c4");
                case School.Arcane: return Hex("#d48be8");
                default: return Hex("#c7ab84");
            }
        }

        public static Color ClassColor(ClassId c)
        {
            switch (c)
            {
                case ClassId.Warrior: return Hex("#C69B6D");
                case ClassId.Hunter: return Hex("#AAD372");
                case ClassId.Paladin: return Hex("#F48CBA");
                case ClassId.Mage: return Hex("#3FC7EB");
                case ClassId.Priest: return Hex("#FFFFFF");
                case ClassId.Rogue: return Hex("#FFF468");
                case ClassId.Warlock: return Hex("#8788EE");
                case ClassId.Shaman: return Hex("#0070DD");
                default: return Hex("#d6c7a8");
            }
        }

        public static Color QualityColor(Quality q)
        {
            switch (q)
            {
                case Quality.Poor: return Hex("#9d9d9d");
                case Quality.Uncommon: return Hex("#1eff00");
                case Quality.Rare: return Hex("#3d9bff");
                case Quality.Epic: return Hex("#b555f5");
                case Quality.Legendary: return Hex("#ff8000");
                default: return Hex("#ffffff");
            }
        }

        public static Color ResourceColor(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Rage: return Rage;
                case ResourceType.Energy: return Energy;
                case ResourceType.Focus: return Focus;
                case ResourceType.Mana: return Mana;
                default: return InkSoft;
            }
        }

        public static string Rich(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";

        /// <summary>Formats copper as "1g 23s 45c".</summary>
        public static string Money(long copper)
        {
            long g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
            var parts = new List<string>();
            if (g > 0) parts.Add(Rich(g + "g", Hex("#ffd75e")));
            if (s > 0 || g > 0) parts.Add(Rich(s + "s", Hex("#d9d9e6")));
            parts.Add(Rich(c + "c", Hex("#e3a074")));
            return string.Join(" ", parts);
        }

        // ---------------------------------------------------------------- styles
        public static Font BodyFont, BoldFont, TitleFont;
        public static GUIStyle Label, LabelSmall, LabelBold, LabelDark, LabelDarkSmall, Title, Header, HeaderDark, Center, CenterSmall, Tooltip, Number;
        public static GUIStyle PaperPanel, InkPanel, InkPanelSoft, Button, ButtonDark, ButtonGold, Slot, TextField, ScrollView;
        static bool built;

        public static float Scale { get; private set; } = 1f;
        public static float Width => Screen.width / Scale;
        public static float Height => Screen.height / Scale;

        /// <summary>Call at the start of every OnGUI. Sets scaling and builds styles on first use.</summary>
        public static void BeginFrame()
        {
            Build();
            Scale = Mathf.Max(0.5f, Screen.height / RefHeight);
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            GUI.skin.settings.cursorColor = Ink;
            if (Event.current.type == EventType.Repaint) tooltip = pendingTooltip = null;
        }

        static void Build()
        {
            if (built && Label != null && PaperPanel != null && PaperPanel.normal.background != null) return;
            built = true;
            BodyFont = Resources.Load<Font>("Fonts/Nunito-SemiBold");
            BoldFont = Resources.Load<Font>("Fonts/Nunito-ExtraBold") ?? BodyFont;
            TitleFont = Resources.Load<Font>("Fonts/Fredoka-SemiBold") ?? BoldFont;

            Label = new GUIStyle(GUI.skin.label) { font = BodyFont, fontSize = 20, richText = true, wordWrap = true };
            Label.normal.textColor = TextLight;
            LabelSmall = new GUIStyle(Label) { fontSize = 16 };
            LabelBold = new GUIStyle(Label) { font = BoldFont };
            LabelDark = new GUIStyle(Label);
            LabelDark.normal.textColor = Ink;
            LabelDarkSmall = new GUIStyle(LabelDark) { fontSize = 16 };
            Title = new GUIStyle(Label) { font = TitleFont, fontSize = 44, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            Title.normal.textColor = TextLight;
            Header = new GUIStyle(Label) { font = TitleFont, fontSize = 28, wordWrap = false };
            Header.normal.textColor = Gold;
            HeaderDark = new GUIStyle(Header);
            HeaderDark.normal.textColor = Ink;
            Center = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            CenterSmall = new GUIStyle(LabelSmall) { alignment = TextAnchor.MiddleCenter };
            Number = new GUIStyle(Label) { font = BoldFont, fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = false };

            PaperPanel = new GUIStyle
            {
                normal = { background = ProceduralArt.RoundedRect(64, 18, Paper, Ink, 3, PaperShade) },
                border = new RectOffset(22, 22, 22, 22),
                padding = new RectOffset(24, 24, 20, 20),
            };
            InkPanel = new GUIStyle
            {
                normal = { background = ProceduralArt.RoundedRect(64, 14, Night, new Color(Gold.r, Gold.g, Gold.b, 0.85f), 2) },
                border = new RectOffset(16, 16, 16, 16),
                padding = new RectOffset(16, 16, 12, 12),
            };
            InkPanelSoft = new GUIStyle(InkPanel)
            {
                normal = { background = ProceduralArt.RoundedRect(64, 14, new Color(0.10f, 0.08f, 0.17f, 0.62f), new Color(1, 1, 1, 0.18f), 2) },
            };
            Tooltip = new GUIStyle(InkPanel)
            {
                font = BodyFont, fontSize = 17, richText = true, wordWrap = true,
                padding = new RectOffset(14, 14, 10, 12),
            };
            Tooltip.normal.textColor = TextLight;

            Button = MakeButton(Hex("#fff7e4"), Hex("#ffe9b8"), Hex("#f1d79e"), Ink, Ink);
            ButtonDark = MakeButton(Hex("#3a2f52"), Hex("#4c3f6b"), Hex("#2c2340"), Hex("#d8b46a"), TextLight);
            ButtonGold = MakeButton(Hex("#f4cf7a"), Hex("#ffe09a"), Hex("#d9ad57"), Ink, Ink);

            Slot = new GUIStyle
            {
                normal = { background = ProceduralArt.RoundedRect(48, 8, new Color(0.08f, 0.06f, 0.13f, 0.75f), new Color(1, 1, 1, 0.25f), 2) },
                border = new RectOffset(10, 10, 10, 10),
            };
            TextField = new GUIStyle(GUI.skin.textField)
            {
                font = BodyFont, fontSize = 22, padding = new RectOffset(12, 12, 8, 8),
                normal = { background = ProceduralArt.RoundedRect(48, 10, Color.white, Ink, 2), textColor = Ink },
                focused = { background = ProceduralArt.RoundedRect(48, 10, Color.white, GoldDeep, 3), textColor = Ink },
                hover = { background = ProceduralArt.RoundedRect(48, 10, Color.white, Ink, 2), textColor = Ink },
                border = new RectOffset(12, 12, 12, 12),
            };
            ScrollView = new GUIStyle();
        }

        static GUIStyle MakeButton(Color fill, Color hover, Color active, Color border, Color text)
        {
            var s = new GUIStyle
            {
                font = BoldFont, fontSize = 20, alignment = TextAnchor.MiddleCenter, richText = true,
                border = new RectOffset(14, 14, 14, 14), padding = new RectOffset(14, 14, 8, 10),
                normal = { background = ProceduralArt.RoundedRect(48, 12, fill, border, 3), textColor = text },
                hover = { background = ProceduralArt.RoundedRect(48, 12, hover, border, 3), textColor = text },
                active = { background = ProceduralArt.RoundedRect(48, 12, active, border, 3), textColor = text },
            };
            return s;
        }

        // ---------------------------------------------------------------- widgets

        /// <summary>Draws a panel and blocks world clicks under it.</summary>
        public static void Panel(Rect r, GUIStyle style = null, bool shadow = true)
        {
            style = style ?? PaperPanel;
            if (shadow && Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.22f);
                GUI.Box(new Rect(r.x + 4, r.y + 6, r.width, r.height), GUIContent.none, style);
                GUI.color = old;
            }
            GUI.Box(r, GUIContent.none, style);
            GameInput.BlockRectGui(r);
        }

        /// <summary>Invisible click blocker for custom-drawn regions.</summary>
        public static void Block(Rect r) => GameInput.BlockRectGui(r);

        public static bool Btn(Rect r, string text, GUIStyle style = null, bool enabled = true, string tip = null)
        {
            var old = GUI.enabled;
            GUI.enabled = enabled && old;
            bool clicked = GUI.Button(r, text, style ?? Button);
            GUI.enabled = old;
            if (tip != null) TooltipFor(r, tip);
            GameInput.BlockRectGui(r);
            if (clicked) Sfx?.Invoke("ui_click");
            return clicked;
        }

        /// <summary>Horizontal bar with optional centred text.</summary>
        public static void Bar(Rect r, float fill, Color color, string text = null, Color? back = null)
        {
            if (Event.current.type != EventType.Repaint && text == null) return;
            var old = GUI.color;
            GUI.color = back ?? new Color(0.05f, 0.04f, 0.09f, 0.75f);
            GUI.DrawTexture(r, ProceduralArt.RoundedRect(32, 8, Color.white, Color.white, 0), ScaleMode.StretchToFill);
            fill = Mathf.Clamp01(fill);
            if (fill > 0.001f)
            {
                GUI.color = color;
                var fr = new Rect(r.x + 2, r.y + 2, (r.width - 4) * fill, r.height - 4);
                GUI.DrawTexture(fr, ProceduralArt.RoundedRect(32, 7, Color.white, Color.white, 0), ScaleMode.StretchToFill);
                GUI.color = new Color(1, 1, 1, 0.22f);
                GUI.DrawTexture(new Rect(fr.x, fr.y, fr.width, fr.height * 0.45f), ProceduralArt.White);
            }
            GUI.color = old;
            if (!string.IsNullOrEmpty(text))
            {
                Shadowed(r, text, NumberStyle(Mathf.Clamp((int)(r.height * 0.72f), 11, 20)));
            }
        }

        /// <summary>Label with a 1px dark shadow, readable over art.</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style, Color? color = null)
        {
            // temporarily recolour the style instead of allocating a copy every call
            var orig = style.normal.textColor;
            var c = color ?? orig;
            style.normal.textColor = new Color(0, 0, 0, 0.75f * c.a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 2f, r.width, r.height), StripColor(text), style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
            style.normal.textColor = orig;
        }

        static readonly Dictionary<int, GUIStyle> SizedNumbers = new Dictionary<int, GUIStyle>();

        /// <summary>Cached bold number style at a given size and alignment (no per-frame allocation).</summary>
        public static GUIStyle NumberStyle(int fontSize, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            int key = fontSize * 16 + (int)anchor;
            if (!SizedNumbers.TryGetValue(key, out var st) || st == null)
            {
                st = new GUIStyle(Number) { fontSize = fontSize, alignment = anchor };
                SizedNumbers[key] = st;
            }
            return st;
        }

        static string StripColor(string t)
        {
            if (string.IsNullOrEmpty(t) || t.IndexOf('<') < 0) return t;
            var sb = new System.Text.StringBuilder(t.Length);
            bool inTag = false;
            foreach (var ch in t)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Ability/item icon: school-coloured frame + glyph (from Art "glyph_&lt;name&gt;"), optional
        /// cooldown sweep (0..1 remaining), dimming and a hotkey/stack label.
        /// </summary>
        public static void Icon(Rect r, string glyph, Color frameColor, float cooldown01 = 0f, bool dim = false, string corner = null, string centerText = null)
        {
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = Color.Lerp(frameColor, Color.black, 0.55f);
                GUI.DrawTexture(r, ProceduralArt.RoundedRect(48, 10, Color.white, Color.white, 0));
                GUI.color = frameColor;
                GUI.DrawTexture(r, ProceduralArt.RoundedRect(48, 10, new Color(1, 1, 1, 0.0f), Color.white, 3));
                var inner = new Rect(r.x + r.width * 0.14f, r.y + r.height * 0.14f, r.width * 0.72f, r.height * 0.72f);
                var gtex = GlyphTexture(glyph);
                if (gtex != null)
                {
                    GUI.color = new Color(1f, 0.98f, 0.92f, dim ? 0.45f : 1f);
                    GUI.DrawTexture(inner, gtex, ScaleMode.ScaleToFit);
                }
                else
                {
                    GUI.color = new Color(1, 1, 1, dim ? 0.5f : 1f);
                    GUI.Label(r, string.IsNullOrEmpty(glyph) ? "?" : glyph.Substring(0, 1).ToUpperInvariant(), NumberStyle((int)(r.height * 0.45f)));
                }
                if (dim)
                {
                    GUI.color = new Color(0, 0, 0, 0.45f);
                    GUI.DrawTexture(r, ProceduralArt.RoundedRect(48, 10, Color.white, Color.white, 0));
                }
                if (cooldown01 > 0f)
                {
                    GUI.color = new Color(0.02f, 0.02f, 0.06f, 0.62f);
                    float h = r.height * Mathf.Clamp01(cooldown01);
                    GUI.DrawTexture(new Rect(r.x, r.yMax - h, r.width, h), ProceduralArt.White);
                }
                GUI.color = old;
            }
            if (!string.IsNullOrEmpty(centerText))
                Shadowed(r, centerText, NumberStyle((int)(r.height * 0.36f)));
            if (!string.IsNullOrEmpty(corner))
                Shadowed(new Rect(r.x + 3, r.y + 1, r.width - 6, r.height * 0.4f), corner,
                    NumberStyle(Mathf.Max(11, (int)(r.height * 0.24f)), TextAnchor.UpperRight));
        }

        static readonly Dictionary<string, Texture2D> GlyphCache = new Dictionary<string, Texture2D>();

        /// <summary>Glyph texture or null if no glyph art exists (caller draws a letter instead).</summary>
        public static Texture2D GlyphTexture(string glyph)
        {
            if (string.IsNullOrEmpty(glyph)) return null;
            if (GlyphCache.TryGetValue(glyph, out var t)) return t;
            string key = glyph.StartsWith("glyph_") || glyph.StartsWith("crest_") ? glyph : "glyph_" + glyph;
            t = ArtLibrary.HasRealTexture(key) ? ArtLibrary.Texture(key) : (ArtLibrary.HasRealTexture(glyph) ? ArtLibrary.Texture(glyph) : null);
            GlyphCache[glyph] = t;
            return t;
        }

        /// <summary>Portrait in a rounded frame (falls back to a coloured disc).</summary>
        public static void Portrait(Rect r, string key, Color frame, bool dim = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = new Color(0.1f, 0.08f, 0.16f, 0.9f);
            GUI.DrawTexture(r, ProceduralArt.RoundedRect(64, 16, Color.white, Color.white, 0));
            GUI.color = dim ? new Color(0.5f, 0.5f, 0.55f, 1f) : Color.white;
            var tex = ArtLibrary.Texture(key);
            GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), tex, ScaleMode.ScaleAndCrop);
            GUI.color = frame;
            GUI.DrawTexture(r, ProceduralArt.RoundedRect(64, 16, new Color(1, 1, 1, 0f), Color.white, 3));
            GUI.color = old;
        }

        // ---------------------------------------------------------------- tooltips
        static string tooltip, pendingTooltip;
        static Rect tooltipAnchor;

        /// <summary>Shows a tooltip when the mouse hovers r (call every frame while drawing r).</summary>
        public static void TooltipFor(Rect r, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (r.Contains(Event.current.mousePosition))
            {
                pendingTooltip = text;
                tooltipAnchor = r;
            }
        }

        /// <summary>Draw the tooltip last in OnGUI (on top of everything).</summary>
        public static void EndFrame()
        {
            if (pendingTooltip != null) tooltip = pendingTooltip;
            if (string.IsNullOrEmpty(tooltip)) return;
            float w = 360f;
            float h = Tooltip.CalcHeight(new GUIContent(tooltip), w);
            var mp = Event.current.mousePosition;
            float x = mp.x + 22f, y = mp.y + 18f;
            if (x + w > Width - 8) x = mp.x - w - 16f;
            if (y + h > Height - 8) y = Height - h - 8f;
            var r = new Rect(x, y, w, h);
            GUI.Box(r, tooltip, Tooltip);
        }

        /// <summary>Hook for UI sounds (set by the audio system).</summary>
        public static System.Action<string> Sfx;
    }
}
