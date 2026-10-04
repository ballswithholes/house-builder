// Map (M): a stylised overview of the current diorama. The ground is painted once per map from the navigation grid
// (walkable ground light, props and borders darker, soft ink edges), then live markers: the party (class colours,
// leader ringed), NPCs with names, visible enemies, chests, spirit lanterns (lit / dark), signs and the exits.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class MapPanel : PanelWindow
    {
        public override string Id => UiPanels.Map;
        public override int Order => 132;

        Texture2D ground;
        string groundFor = "";
        int groundVersion = -1;
        readonly List<MapNpcDef> npcs = new List<MapNpcDef>();
        readonly List<EncounterDef> encounters = new List<EncounterDef>();
        int listsFlags = -1, lastDrawFrame = -10;
        GameSession listsSession;
        string titleSub = "";
        string titleFor = "";

        void EnsureGround(GameSession s)
        {
            var nav = s.Nav;
            var def = s.MapDef;
            if (def == null) return;
            int version = nav != null ? nav.Version : 0;
            if (ground != null && groundFor == def.id && groundVersion == version) return;
            groundFor = def.id;
            groundVersion = version;
            if (ground != null) UnityEngine.Object.Destroy(ground);
            ground = null;
            if (nav == null || nav.Width <= 0 || nav.Height <= 0) return;
            int w = nav.Width, h = nav.Height;
            var baseCol = Ui.Hex("#b8d48f");
            if (!string.IsNullOrEmpty(def.groundTint)) baseCol = Color.Lerp(baseCol, Ui.Hex(def.groundTint), 0.35f);
            if (def.ground != null && def.ground.Contains("shrine")) baseCol = Ui.Hex("#c9c1b0");
            else if (def.ground != null && def.ground.Contains("forest")) baseCol = Ui.Hex("#9fbf80");
            else if (def.ground != null && def.ground.Contains("village")) baseCol = Ui.Hex("#d9c79c");
            var dark = Color.Lerp(baseCol, Ui.Hex("#3d4a2e"), 0.55f);
            var edge = Ui.Hex("#4a3b2c");
            var px = new Color32[w * h];
            var rnd = new System.Random(def.id.GetHashCode());
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool walk = nav.IsCellWalkable(x, y, 0f);
                    Color c;
                    if (walk)
                    {
                        bool border = !Walk(nav, x - 1, y) || !Walk(nav, x + 1, y) || !Walk(nav, x, y - 1) || !Walk(nav, x, y + 1);
                        float n = (float)rnd.NextDouble() * 0.05f;
                        c = border ? Color.Lerp(baseCol, edge, 0.45f) : new Color(baseCol.r - n, baseCol.g - n, baseCol.b - n, 1f);
                    }
                    else
                    {
                        float n = (float)rnd.NextDouble() * 0.06f;
                        c = new Color(dark.r + n, dark.g + n, dark.b + n, 0.78f);
                    }
                    px[y * w + x] = c;
                }
            ground = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "lv_map_" + def.id, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            ground.SetPixels32(px);
            ground.Apply(false, false);
        }

        static bool Walk(NavGrid nav, int x, int y) => x >= 0 && y >= 0 && x < nav.Width && y < nav.Height && nav.IsCellWalkable(x, y, 0f);

        protected override void DrawPanel()
        {
            var s = PanelKit.Sess;
            var def = s.MapDef;
            if (def == null) { Close(); return; }
            EnsureGround(s);
            // NPCs and encounters depend on story flags: rebuild when the map, the session or its FlagsVersion changed, or
            // when the window was (re)opened — no polling
            bool reopened = lastDrawFrame < Time.frameCount - 1;
            lastDrawFrame = Time.frameCount;
            if (reopened || titleFor != def.id || listsSession != s || listsFlags != s.FlagsVersion)
            {
                listsSession = s;
                listsFlags = s.FlagsVersion;
                npcs.Clear();
                encounters.Clear();
                try { npcs.AddRange(s.VisibleNpcs()); encounters.AddRange(s.VisibleEncounters()); }
                catch (Exception e) { Debug.LogException(e); }
                if (titleFor != def.id)
                {
                    titleFor = def.id;
                    titleSub = string.IsNullOrEmpty(def.subtitle) ? "" : def.subtitle;
                }
            }
            float maxW = Mathf.Min(1600f, Ui.Width - 60f);
            float mw = maxW - 48f;
            float mh = mw * Mathf.Max(1f, def.depth) / Mathf.Max(1f, def.width);
            float maxH = Ui.Height - 330f;
            if (mh > maxH) { mw *= maxH / mh; mh = maxH; }
            mw = Mathf.Max(mw, 420f);
            var r = PanelKit.Centered(Mathf.Max(mw + 48f, 700f), mh + 200f, -20f);
            var c = Chrome(r, string.IsNullOrEmpty(def.name) ? "Map" : def.name, titleSub);
            var mr = new Rect(c.x + (c.width - mw) * 0.5f, c.y + 8f, mw, mh);
            // parchment frame + ground
            PanelKit.Rounded(new Rect(mr.x - 10f, mr.y - 10f, mr.width + 20f, mr.height + 20f), new Color(0.55f, 0.42f, 0.25f, 0.18f));
            if (ground != null) PanelKit.Tex(mr, ground, Color.white);
            else PanelKit.Rounded(mr, Ui.Hex("#b8d48f"));
            PanelKit.Outline(new Rect(mr.x - 3f, mr.y - 3f, mr.width + 6f, mr.height + 6f), new Color(0.29f, 0.23f, 0.17f, 0.8f));

            DrawTransitions(mr, def, s);
            DrawChests(mr, def, s);
            DrawLanterns(mr, def);
            DrawEncounters(mr, def, s);
            DrawNpcs(mr, def, s);
            DrawParty(mr, def, s);
            DrawLegend(new Rect(c.x, mr.yMax + 22f, c.width, 30f));
        }

        static Vector2 ToMap(Rect mr, MapDef def, float x, float y) =>
            new Vector2(mr.x + x / Mathf.Max(1f, def.width) * mr.width, mr.yMax - y / Mathf.Max(1f, def.depth) * mr.height);

        static void Dot(Vector2 p, float size, Color fill, Color? outline = null)
        {
            if (outline.HasValue) PanelKit.Tex(new Rect(p.x - size * 0.5f - 2f, p.y - size * 0.5f - 2f, size + 4f, size + 4f), PanelArt.Disc, outline.Value);
            PanelKit.Tex(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), PanelArt.Disc, fill);
        }

        static bool Near(Vector2 p, float size) => PanelKit.Hover(new Rect(p.x - size, p.y - size, size * 2f, size * 2f));

        static void Marker(Vector2 p, float size, string tip)
        {
            if (string.IsNullOrEmpty(tip)) return;
            Ui.TooltipFor(new Rect(p.x - size, p.y - size, size * 2f, size * 2f), tip);
        }

        GUIStyle tagStyle;

        GUIStyle TagStyle()
        {
            if (tagStyle == null || tagStyle.font != Ui.BoldFont)
            {
                tagStyle = new GUIStyle(Ui.Label) { font = Ui.BoldFont, fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = false, richText = true };
                tagStyle.normal.textColor = Ui.Ink;
            }
            return tagStyle;
        }

        void Tag(Vector2 p, string text, Color col)
        {
            if (string.IsNullOrEmpty(text)) return;
            var st = TagStyle();
            float w = PanelKit.TextWidth(text, st) + 12f;
            var r = new Rect(p.x - w * 0.5f, p.y, w, 20f);
            PanelKit.Rounded(r, new Color(1f, 0.97f, 0.9f, 0.82f));
            PanelKit.Label(new Rect(r.x, r.y + 1f, r.width, r.height), text, st, col);
        }

        void DrawTransitions(Rect mr, MapDef def, GameSession s)
        {
            if (def.transitions == null) return;
            foreach (var t in def.transitions)
            {
                if (t == null) continue;
                var size = MapRuntime.TransitionSize(t);
                var a = ToMap(mr, def, t.pos.x - size.x * 0.5f, t.pos.y + size.y * 0.5f);
                var b = ToMap(mr, def, t.pos.x + size.x * 0.5f, t.pos.y - size.y * 0.5f);
                var rr = Rect.MinMaxRect(a.x, a.y, Mathf.Max(a.x + 6f, b.x), Mathf.Max(a.y + 6f, b.y));
                bool open = s.Map == null || s.Map.IsTransitionUnlocked(t);
                PanelKit.Rounded(rr, open ? new Color(1f, 0.82f, 0.4f, 0.85f) : new Color(0.6f, 0.55f, 0.75f, 0.7f));
                string label = string.IsNullOrEmpty(t.label) ? "Exit" : t.label;
                var mid = new Vector2(rr.center.x, rr.yMax + 2f);
                mid.x = Mathf.Clamp(mid.x, mr.x + 60f, mr.xMax - 60f);
                if (mid.y > mr.yMax - 22f) mid.y = rr.y - 22f;
                Tag(mid, label, open ? PanelKit.GoldInk : Ui.InkSoft);
                if (PanelKit.Hover(rr)) Ui.TooltipFor(rr, open ? "<b>" + label + "</b>" : "<b>" + label + "</b>\n" + (string.IsNullOrEmpty(t.lockedText) ? "The way is closed for now." : t.lockedText));
            }
        }

        void DrawChests(Rect mr, MapDef def, GameSession s)
        {
            if (def.chests == null) return;
            var glyph = Ui.GlyphTexture("coin");
            foreach (var ch in def.chests)
            {
                if (ch == null) continue;
                if (!string.IsNullOrEmpty(ch.requireFlag) && !s.Flags.Test(ch.requireFlag)) continue;
                bool opened = s.Map != null && s.Map.IsChestOpened(ch.id);
                var p = ToMap(mr, def, ch.pos.x, ch.pos.y);
                var rr = new Rect(p.x - 8f, p.y - 8f, 16f, 16f);
                PanelKit.Rounded(rr, opened ? new Color(0.5f, 0.42f, 0.32f, 0.6f) : Ui.Hex("#c98b2e"));
                if (!opened && glyph != null) PanelKit.Tex(new Rect(rr.x + 2f, rr.y + 2f, 12f, 12f), glyph, Ui.Paper, ScaleMode.ScaleToFit);
                Marker(p, 10f, opened ? "Chest (empty)" : "Chest");
            }
        }

        void DrawLanterns(Rect mr, MapDef def)
        {
            var view = PanelKit.Flow != null ? PanelKit.Flow.Map : null;
            if (view == null) return;
            foreach (var o in view.Objects)
            {
                if (o == null || !o.Visible) continue;
                var p = ToMap(mr, def, o.Position.x, o.Position.y);
                if (o.IsLantern)
                {
                    if (o.LanternLit)
                    {
                        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 2f + o.Position.x);
                        PanelKit.Tex(new Rect(p.x - 18f, p.y - 18f, 36f, 36f), ProceduralArt.Glow, new Color(1f, 0.8f, 0.35f, 0.9f * pulse));
                        Dot(p, 10f, Ui.Hex("#ffd86b"), Ui.Ink);
                    }
                    else Dot(p, 10f, Ui.Hex("#8a8496"), Ui.Ink);
                    Marker(p, 10f, o.LanternLit ? "Spirit lantern (glowing)" : "Spirit lantern (dark)");
                }
                else if (o.Kind == MapObjectKind.Prop && o.Prop != null && !string.IsNullOrEmpty(o.Prop.interact))
                {
                    Dot(p, 7f, Ui.Hex("#f4ecd8"), new Color(0.29f, 0.23f, 0.17f, 0.9f));
                    Marker(p, 8f, string.IsNullOrEmpty(o.Label) ? "Something to look at" : Short(o.Label));
                }
            }
        }

        static readonly Dictionary<string, string> shorts = new Dictionary<string, string>();

        static string Short(string t)
        {
            if (shorts.TryGetValue(t, out var s)) return s;
            s = t.Length > 120 ? t.Substring(0, 118) + "…" : t;
            if (shorts.Count > 100) shorts.Clear();
            shorts[t] = s;
            return s;
        }

        void DrawEncounters(Rect mr, MapDef def, GameSession s)
        {
            var db = PanelKit.Db;
            foreach (var e in encounters)
            {
                if (e == null || e.enemies == null) continue;
                foreach (var en in e.enemies)
                {
                    if (en == null) continue;
                    var p = ToMap(mr, def, en.pos.x, en.pos.y);
                    Dot(p, 9f, Ui.Hex("#d9534a"), new Color(0.25f, 0.08f, 0.08f, 0.9f));
                    if (!Near(p, 8f)) continue;
                    var cr = db != null ? db.Creature(en.creature) : null;
                    Marker(p, 8f, cr != null ? Ui.Rich(cr.name, Ui.Bad) : "Enemy");
                }
            }
        }

        void DrawNpcs(Rect mr, MapDef def, GameSession s)
        {
            var st = TagStyle();
            foreach (var n in npcs)
            {
                if (n == null) continue;
                var p = ToMap(mr, def, n.pos.x, n.pos.y);
                Dot(p, 10f, Ui.Hex("#f2c14e"), Ui.Ink);
                string name = s.NpcName(n.npc);
                Tag(new Vector2(p.x, p.y + 7f), name, Ui.Ink);
                Marker(p, 10f, name);
            }
        }

        void DrawParty(Rect mr, MapDef def, GameSession s)
        {
            var f = PanelKit.Flow;
            var units = f != null ? f.PartyUnits : null;
            if (units == null) return;
            var leader = s.Leader;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u == null || !u.IsAlive && !u.Downed) continue;
                var view = f.ViewOf(u);
                Vector2 wp = view != null ? view.FeetPosition : new Vector2(u.Position.x, u.Position.y);
                var p = ToMap(mr, def, wp.x, wp.y);
                bool pet = u.Class == null;
                float size = u == leader ? 16f : pet ? 9f : 13f;
                if (u == leader)
                {
                    float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 3f);
                    PanelKit.Tex(new Rect(p.x - 16f, p.y - 16f, 32f, 32f), PanelArt.RingCircle, new Color(1f, 1f, 1f, pulse));
                }
                Dot(p, size, PanelKit.ColorOf(u), Ui.Ink);
                if (Near(p, size)) Marker(p, size, PanelKit.NameOf(u) + (u == leader ? " (leader)" : ""));
            }
        }

        static readonly string[] LegendText = { "Your party", "Folk", "Enemies", "Spirit lantern", "Chest", "Exit" };
        static readonly Color[] LegendColors = { Ui.Hex("#3FC7EB"), Ui.Hex("#f2c14e"), Ui.Hex("#d9534a"), Ui.Hex("#ffd86b"), Ui.Hex("#c98b2e"), new Color(1f, 0.82f, 0.4f, 1f) };

        void DrawLegend(Rect r)
        {
            float x = r.x;
            var cols = LegendColors;
            for (int i = 0; i < LegendText.Length; i++)
            {
                Dot(new Vector2(x + 8f, r.center.y), 12f, cols[i], Ui.Ink);
                float w = PanelKit.TextWidth(LegendText[i], PanelKit.TextSmall);
                PanelKit.Label(new Rect(x + 22f, r.y + 4f, w + 4f, 26f), LegendText[i], PanelKit.TextSmall);
                x += w + 52f;
            }
        }
    }
}
