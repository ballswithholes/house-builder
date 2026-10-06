// Map (M): a stylised overview of the current diorama. The ground is painted once per map from the navigation grid
// (walkable ground light, props and borders darker, soft ink edges), then live markers: the party (class colours,
// leader ringed), NPCs with quest glyphs ("!" / "?", yellow or grey, main quests ringed in gold) and readable labels,
// visible enemies, chests, spirit lanterns (lit / dark), signs, the exits (hidden ones only once revealed) and quest
// objective hints (encounters holding a Kill target, Reach regions and exits, chests with a Collect item).
// Labels: short names (NpcDef.shortName / honorific stripped), placed by priority (quest NPCs first) around their dots
// without overlapping (MapLabels/LabelLayout, Core); a crowd of service NPCs in one region gets the region's name when
// their own labels do not fit; anything left unlabelled shows its name on hover. Mouse wheel zooms 1–3× around the
// cursor, dragging pans (MapViewport). The title carries the zone's level band and a "Hidden dungeon" / "Raid (N)" badge.
using System;
using System.Collections.Generic;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.Util;
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
        int listsFlags = -1, listsMarkers = -1, lastDrawFrame = -10;
        GameSession listsSession;
        string titleSub = "";
        string titleFor = "";

        // view: zoom / pan per map (reset when the map changes)
        MapViewport view;
        string viewFor = "";
        bool dragging;
        Vector2 dragLast;
        static readonly int DragId = "lv_map_drag".GetHashCode();

        // label plan, rebuilt when its inputs change
        MapLabelPlan plan;
        string planKey = "";
        readonly StringBuilder keySb = new StringBuilder(64);
        readonly Dictionary<string, string> tips = new Dictionary<string, string>(StringComparer.Ordinal);
        int tipsVersion = -1;

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
            // NPCs and encounters depend on story flags, markers on QuestMarkersVersion: rebuild when the map, the session
            // or a version changed, or when the window was (re)opened — no polling
            bool reopened = lastDrawFrame < Time.frameCount - 1;
            lastDrawFrame = Time.frameCount;
            if (reopened || titleFor != def.id || listsSession != s || listsFlags != s.FlagsVersion || listsMarkers != s.QuestMarkersVersion)
            {
                listsSession = s;
                listsFlags = s.FlagsVersion;
                listsMarkers = s.QuestMarkersVersion;
                npcs.Clear();
                encounters.Clear();
                try { npcs.AddRange(s.VisibleNpcs()); encounters.AddRange(s.VisibleEncounters()); }
                catch (Exception e) { Debug.LogException(e); }
                if (titleFor != def.id)
                {
                    titleFor = def.id;
                    titleSub = Subtitle(def);
                }
            }
            MapLabels.MapSize(Ui.Width, Ui.Height, def.width, def.depth, out float mw, out float mh);
            var r = PanelKit.Centered(Mathf.Max(mw + 48f, 700f), mh + 200f, -20f);
            var c = Chrome(r, string.IsNullOrEmpty(def.name) ? "Map" : def.name, titleSub);
            var mr = new Rect(c.x + (c.width - mw) * 0.5f, c.y + 8f, mw, mh);
            UpdateView(def, mr);
            HandleInput(mr);

            // parchment frame + ground (the visible part of the painted map)
            PanelKit.Rounded(new Rect(mr.x - 10f, mr.y - 10f, mr.width + 20f, mr.height + 20f), new Color(0.55f, 0.42f, 0.25f, 0.18f));
            if (ground != null && PanelKit.IsRepaint)
            {
                var uv = new Rect(view.X0 / view.MapWidth, view.Y0 / view.MapDepth, view.ViewWidth / view.MapWidth, view.ViewDepth / view.MapDepth);
                GUI.DrawTextureWithTexCoords(mr, ground, uv);
            }
            else if (ground == null) PanelKit.Rounded(mr, Ui.Hex("#b8d48f"));
            PanelKit.Outline(new Rect(mr.x - 3f, mr.y - 3f, mr.width + 6f, mr.height + 6f), new Color(0.29f, 0.23f, 0.17f, 0.8f));

            EnsurePlan(s, def, mr);
            DrawHints(mr, def, s);
            DrawTransitions(mr, def, s);
            DrawChests(mr, def, s);
            DrawLanterns(mr);
            DrawEncounters(mr, def);
            DrawNpcs(mr, def, s);
            DrawLabels(mr, s);
            DrawParty(mr, s);
            DrawHover(mr, s);
            DrawZoomControls(mr);
            DrawLegend(new Rect(c.x, mr.yMax + 18f, c.width, 30f));
        }

        static string Subtitle(MapDef def)
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(def.subtitle)) parts.Add(def.subtitle);
            if (def.levelMin > 0 && def.levelMax > 0) parts.Add(def.levelMin == def.levelMax ? "Level " + def.levelMin : "Levels " + def.levelMin + "–" + def.levelMax);
            else if (def.levelMin > 0) parts.Add("Level " + def.levelMin + "+");
            if (def.raidSize > 0) parts.Add(Ui.Rich("Raid (" + def.raidSize + ")", Ui.Hex("#a3362b")));
            else if (def.dungeon) parts.Add(Ui.Rich("Hidden dungeon", Ui.Hex("#6a3d9a")));
            return string.Join("  ·  ", parts);
        }

        // ------------------------------------------------------------------ view (zoom / pan)

        void UpdateView(MapDef def, Rect mr)
        {
            var screen = new LabelRect(mr.x, mr.y, mr.width, mr.height);
            if (viewFor != def.id || Mathf.Abs(view.MapWidth - Mathf.Max(1f, def.width)) > 0.01f || Mathf.Abs(view.MapDepth - Mathf.Max(1f, def.depth)) > 0.01f)
            {
                viewFor = def.id;
                view = new MapViewport(def.width, def.depth, screen);
                dragging = false;
            }
            view.Screen = screen;
            view.Clamp();
        }

        void HandleInput(Rect mr)
        {
            var e = Event.current;
            if (e == null) return;
            if (dragging && e.rawType == EventType.MouseUp)
            {
                dragging = false;
                if (GUIUtility.hotControl == DragId) GUIUtility.hotControl = 0;
            }
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (!mr.Contains(e.mousePosition)) break;
                    float z = view.Zoom * (e.delta.y < 0f ? 1.2f : 1f / 1.2f);
                    if (z < 1.04f) z = 1f;
                    view.ZoomAt(z, e.mousePosition.x, e.mousePosition.y);
                    e.Use();
                    break;
                case EventType.MouseDown:
                    if (e.button != 0 || !mr.Contains(e.mousePosition) || view.Zoom <= 1.001f || ZoomControlRect(mr).Contains(e.mousePosition)) break;
                    dragging = true;
                    dragLast = e.mousePosition;
                    GUIUtility.hotControl = DragId;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!dragging) break;
                    var m = PanelKit.RealMouse;
                    view.PanBy(m.x - dragLast.x, m.y - dragLast.y);
                    dragLast = m;
                    e.Use();
                    break;
            }
        }

        static Rect ZoomControlRect(Rect mr) => new Rect(mr.xMax - 128f, mr.y + 8f, 120f, 30f);

        void DrawZoomControls(Rect mr)
        {
            var zr = ZoomControlRect(mr);
            if (view.Zoom > 1.001f)
            {
                if (PanelKit.SmallBtn(zr, "Whole map", true, "Zoom back out (mouse wheel zooms, drag pans)"))
                {
                    view.Zoom = 1f;
                    view.Clamp();
                }
            }
            else if (PanelKit.Hover(mr) && PanelKit.IsRepaint)
            {
                var hr = new Rect(mr.xMax - 214f, mr.yMax - 26f, 206f, 20f);
                PanelKit.Rounded(hr, new Color(1f, 0.97f, 0.9f, 0.7f));
                PanelKit.Label(hr, "Mouse wheel: zoom", PanelKit.TextTiny, Ui.InkSoft);
            }
        }

        Vector2 ToMap(float x, float y)
        {
            view.ToScreen(x, y, out float sx, out float sy);
            return new Vector2(sx, sy);
        }

        bool InView(Vector2 p, float margin = 0f) => view.OnScreen(p.x, p.y, margin);

        static Rect R(LabelRect l) => new Rect(l.X, l.Y, l.W, l.H);

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

        GUIStyle tagStyle, glyphStyle;

        GUIStyle TagStyle()
        {
            if (tagStyle == null || tagStyle.font != Ui.BoldFont)
            {
                tagStyle = new GUIStyle(Ui.Label) { font = Ui.BoldFont, fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = false, richText = true };
                tagStyle.normal.textColor = Ui.Ink;
            }
            return tagStyle;
        }

        GUIStyle GlyphStyle()
        {
            if (glyphStyle == null || glyphStyle.font != Ui.BoldFont)
            {
                glyphStyle = new GUIStyle(Ui.Label) { font = Ui.BoldFont, fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = false };
                glyphStyle.normal.textColor = Ui.Ink;
            }
            return glyphStyle;
        }

        void TagAt(Rect r, string text, Color col, Color? border = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            PanelKit.Rounded(r, new Color(1f, 0.97f, 0.9f, 0.86f));
            if (border.HasValue) PanelKit.Outline(r, border.Value);
            PanelKit.Label(new Rect(r.x, r.y + 1f, r.width, r.height), text, TagStyle(), col);
        }

        /// <summary>A thin straight line (leader lines from a dot to its label).</summary>
        static void Line(Vector2 a, Vector2 b, Color col, float width = 1.5f)
        {
            if (!PanelKit.IsRepaint) return;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;
            var m = GUI.matrix;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, a);
            PanelKit.Rect(new Rect(a.x, a.y - width * 0.5f, len, width), col);
            GUI.matrix = m;
        }

        // ------------------------------------------------------------------ labels

        void EnsurePlan(GameSession s, MapDef def, Rect mr)
        {
            keySb.Clear();
            keySb.Append(def.id).Append('|').Append(s.FlagsVersion).Append('|').Append(s.QuestMarkersVersion).Append('|')
                 .Append(Mathf.RoundToInt(mr.x)).Append(',').Append(Mathf.RoundToInt(mr.y)).Append(',').Append(Mathf.RoundToInt(mr.width)).Append(',').Append(Mathf.RoundToInt(mr.height)).Append('|')
                 .Append(Mathf.RoundToInt(view.Zoom * 100f)).Append(',').Append(Mathf.RoundToInt(view.X0 * 20f)).Append(',').Append(Mathf.RoundToInt(view.Y0 * 20f)).Append('|')
                 .Append(TagStyle().font != null ? TagStyle().font.GetInstanceID() : 0).Append('|').Append(npcs.Count);
            string key = keySb.ToString();
            if (plan != null && key == planKey) return;
            planKey = key;
            var input = new MapLabelInput
            {
                Db = PanelKit.Db ?? s.Db, Map = def, Npcs = npcs, View = view,
                MarkerOf = s.QuestMarkerOf,
                TextWidth = t => PanelKit.TextWidth(t, TagStyle()),
                ShowTransition = t => s.Map == null || s.Map.IsTransitionVisible(t),
            };
            // other dots the labels must not cover: chests, enemies, lanterns and signs
            if (def.chests != null)
                foreach (var ch in def.chests)
                    if (ch != null && s.Flags.Test(ch.requireFlag)) AddObstacle(input, ch.pos.x, ch.pos.y, 18f);
            foreach (var e in encounters)
                if (e?.enemies != null) foreach (var en in e.enemies) if (en != null) AddObstacle(input, en.pos.x, en.pos.y, 13f);
            var mv = PanelKit.Flow != null ? PanelKit.Flow.Map : null;
            if (mv != null)
                foreach (var o in mv.Objects)
                    if (o != null && o.Visible && (o.IsLantern || (o.Kind == MapObjectKind.Prop && o.Prop != null && !string.IsNullOrEmpty(o.Prop.interact))))
                        AddObstacle(input, o.Position.x, o.Position.y, 12f);
            try { plan = MapLabels.Plan(input); }
            catch (Exception ex) { Debug.LogException(ex); plan = new MapLabelPlan(); }
        }

        void AddObstacle(MapLabelInput input, float x, float y, float size)
        {
            view.ToScreen(x, y, out float sx, out float sy);
            if (view.OnScreen(sx, sy, size)) input.Obstacles.Add(LabelRect.Around(sx, sy, size, size));
        }

        void DrawLabels(Rect mr, GameSession s)
        {
            if (plan == null) return;
            foreach (var l in plan.Labels)
            {
                if (!l.Placed) continue;
                var rr = R(l.Rect);
                if (l.Leader)
                {
                    l.Rect.Closest(l.AnchorX, l.AnchorY, out float lx, out float ly);
                    Line(new Vector2(l.AnchorX, l.AnchorY), new Vector2(lx, ly), new Color(0.29f, 0.23f, 0.17f, 0.75f));
                }
                switch (l.Kind)
                {
                    case MapLabelKind.Exit:
                    {
                        var t = FindTransition(s.MapDef, l.Id);
                        bool open = t == null || s.Map == null || s.Map.IsTransitionUnlocked(t);
                        TagAt(rr, l.Text, open ? PanelKit.GoldInk : Ui.InkSoft);
                        break;
                    }
                    case MapLabelKind.Cluster:
                        TagAt(rr, l.Text, Ui.InkSoft, new Color(0.29f, 0.23f, 0.17f, 0.45f));
                        if (PanelKit.Hover(rr)) Ui.TooltipFor(rr, CrowdTip(s, l));
                        break;
                    default:
                        Color? ring = l.Marker != null ? (Color?)(l.Marker.Yellow ? new Color(0.85f, 0.62f, 0.1f, 0.85f) : new Color(0.45f, 0.45f, 0.5f, 0.6f)) : null;
                        TagAt(rr, l.Text, Ui.Ink, ring);
                        if (PanelKit.Hover(rr)) Ui.TooltipFor(rr, NpcTip(s, l.Id));
                        break;
                }
            }
        }

        static TransitionDef FindTransition(MapDef def, string id)
        {
            if (def?.transitions == null) return null;
            foreach (var t in def.transitions) if (t != null && t.id == id) return t;
            return null;
        }

        string CrowdTip(GameSession s, MapLabel l)
        {
            var sb = new StringBuilder("<b>").Append(l.Text).Append("</b>");
            foreach (var id in l.Members) sb.Append('\n').Append(NpcLine(s, id));
            return sb.ToString();
        }

        static string NpcLine(GameSession s, string id)
        {
            string name = s.NpcName(id);
            var db = s.Db;
            string title = db.Npcs.TryGetValue(id, out var n) ? n.title : db.Companions.TryGetValue(id, out var c) ? c.title : "";
            return string.IsNullOrEmpty(title) ? name : name + "  " + Ui.Rich(title, Ui.InkSoft);
        }

        /// <summary>Full name, title and every quest marker of an NPC (cached per QuestMarkersVersion).</summary>
        string NpcTip(GameSession s, string id)
        {
            if (tipsVersion != s.QuestMarkersVersion) { tipsVersion = s.QuestMarkersVersion; tips.Clear(); }
            if (tips.TryGetValue(id, out var tip)) return tip;
            var sb = new StringBuilder("<b>").Append(s.NpcName(id)).Append("</b>");
            var db = s.Db;
            string title = db.Npcs.TryGetValue(id, out var n) ? n.title : db.Companions.TryGetValue(id, out var comp) ? comp.title : "";
            if (!string.IsNullOrEmpty(title)) sb.Append('\n').Append(Ui.Rich(title, Ui.InkSoft));
            if (db.Companions.ContainsKey(id)) sb.Append('\n').Append(Ui.Rich("Could join your party", PanelKit.GoodDark));
            foreach (var m in s.QuestMarkersOf(id))
                sb.Append('\n').Append(Ui.Rich(m.Glyph + "  " + m.Describe(), m.Yellow ? PanelKit.GoldInk : Ui.InkSoft));
            tip = sb.ToString();
            tips[id] = tip;
            return tip;
        }

        // ------------------------------------------------------------------ markers

        void DrawHints(Rect mr, MapDef def, GameSession s)
        {
            IReadOnlyList<QuestMapHint> hints;
            try { hints = s.QuestHintsOnMap(); }
            catch (Exception e) { Debug.LogException(e); return; }
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 2.4f);
            foreach (var h in hints)
            {
                var col = h.Main ? new Color(1f, 0.78f, 0.25f, 0.9f * pulse) : new Color(1f, 0.86f, 0.3f, 0.8f * pulse);
                string tip = "<b>" + h.QuestName + "</b>\n" + h.Text;
                if (h.Kind == QuestMapHintKind.Region)
                {
                    var a = ToMap(h.Pos.x - h.Size.x * 0.5f, h.Pos.y + h.Size.y * 0.5f);
                    var b = ToMap(h.Pos.x + h.Size.x * 0.5f, h.Pos.y - h.Size.y * 0.5f);
                    var rr = ClipTo(Rect.MinMaxRect(a.x, a.y, b.x, b.y), mr);
                    if (rr.width <= 2f || rr.height <= 2f) continue;
                    PanelKit.Rounded(rr, new Color(1f, 0.86f, 0.3f, 0.14f));
                    PanelKit.Outline(rr, col);
                    if (PanelKit.Hover(rr)) Ui.TooltipFor(rr, tip);
                    continue;
                }
                var p = ToMap(h.Pos.x, h.Pos.y);
                if (!InView(p, -4f)) continue;
                float size = h.Kind == QuestMapHintKind.Encounter ? 46f : 34f;
                PanelKit.Tex(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), PanelArt.RingCircle, col);
                Marker(p, size * 0.5f, tip);
            }
        }

        static Rect ClipTo(Rect r, Rect clip) => Rect.MinMaxRect(Mathf.Max(r.xMin, clip.xMin), Mathf.Max(r.yMin, clip.yMin), Mathf.Min(r.xMax, clip.xMax), Mathf.Min(r.yMax, clip.yMax));

        void DrawTransitions(Rect mr, MapDef def, GameSession s)
        {
            if (def.transitions == null) return;
            foreach (var t in def.transitions)
            {
                if (t == null || (s.Map != null && !s.Map.IsTransitionVisible(t))) continue;   // hidden until revealed
                var size = MapRuntime.TransitionSize(t);
                var a = ToMap(t.pos.x - size.x * 0.5f, t.pos.y + size.y * 0.5f);
                var b = ToMap(t.pos.x + size.x * 0.5f, t.pos.y - size.y * 0.5f);
                var rr = ClipTo(Rect.MinMaxRect(a.x, a.y, Mathf.Max(a.x + 6f, b.x), Mathf.Max(a.y + 6f, b.y)), mr);
                if (rr.width <= 0f || rr.height <= 0f) continue;
                bool open = s.Map == null || s.Map.IsTransitionUnlocked(t);
                PanelKit.Rounded(rr, open ? new Color(1f, 0.82f, 0.4f, 0.85f) : new Color(0.6f, 0.55f, 0.75f, 0.7f));
                string label = string.IsNullOrEmpty(t.label) ? "Exit" : t.label;
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
                var p = ToMap(ch.pos.x, ch.pos.y);
                if (!InView(p)) continue;
                bool opened = s.Map != null && s.Map.IsChestOpened(ch.id);
                var rr = new Rect(p.x - 8f, p.y - 8f, 16f, 16f);
                PanelKit.Rounded(rr, opened ? new Color(0.5f, 0.42f, 0.32f, 0.6f) : Ui.Hex("#c98b2e"));
                if (!opened && glyph != null) PanelKit.Tex(new Rect(rr.x + 2f, rr.y + 2f, 12f, 12f), glyph, Ui.Paper, ScaleMode.ScaleToFit);
                Marker(p, 10f, opened ? "Chest (empty)" : "Chest");
            }
        }

        void DrawLanterns(Rect mr)
        {
            var mv = PanelKit.Flow != null ? PanelKit.Flow.Map : null;
            if (mv == null) return;
            foreach (var o in mv.Objects)
            {
                if (o == null || !o.Visible) continue;
                var p = ToMap(o.Position.x, o.Position.y);
                if (!InView(p)) continue;
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

        void DrawEncounters(Rect mr, MapDef def)
        {
            var db = PanelKit.Db;
            foreach (var e in encounters)
            {
                if (e == null || e.enemies == null) continue;
                foreach (var en in e.enemies)
                {
                    if (en == null) continue;
                    var p = ToMap(en.pos.x, en.pos.y);
                    if (!InView(p)) continue;
                    Dot(p, 9f, Ui.Hex("#d9534a"), new Color(0.25f, 0.08f, 0.08f, 0.9f));
                    if (!Near(p, 8f)) continue;
                    var cr = db != null ? db.Creature(en.creature) : null;
                    Marker(p, 8f, cr != null ? Ui.Rich(cr.name, Ui.Bad) : "Enemy");
                }
            }
        }

        static readonly Color GlyphYellow = Ui.Hex("#ffd23a"), GlyphGrey = Ui.Hex("#a0a0a6"), GlyphInk = new Color(0.17f, 0.12f, 0.1f, 0.95f);

        void DrawNpcs(Rect mr, MapDef def, GameSession s)
        {
            foreach (var n in npcs)
            {
                if (n == null) continue;
                var p = ToMap(n.pos.x, n.pos.y);
                if (!InView(p)) continue;
                Dot(p, 10f, Ui.Hex("#f2c14e"), Ui.Ink);
                Marker(p, 9f, NpcTip(s, n.npc));
                if (plan == null || !plan.Glyphs.TryGetValue(n.npc, out var g)) continue;
                var m = s.QuestMarkerOf(n.npc);
                if (m.IsNone) continue;
                var gr = R(g);
                if (m.Main) PanelKit.Tex(new Rect(gr.center.x - 13f, gr.center.y - 13f, 26f, 26f), PanelArt.RingCircle, new Color(0.91f, 0.69f, 0.29f, 0.95f));
                Glyph(gr, m.Glyph, m.Yellow ? GlyphYellow : GlyphGrey, m.Main ? 22 : 19);
            }
        }

        /// <summary>"!" / "?" with a 1 px ink outline (four offset copies under it).</summary>
        void Glyph(Rect r, string text, Color col, int size)
        {
            if (!PanelKit.IsRepaint) return;
            var st = GlyphStyle();
            int old = st.fontSize;
            st.fontSize = size;
            for (int i = 0; i < 4; i++)
            {
                float dx = i == 0 ? -1.2f : i == 1 ? 1.2f : 0f, dy = i == 2 ? -1.2f : i == 3 ? 1.2f : 0f;
                PanelKit.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), text, st, GlyphInk);
            }
            PanelKit.Label(r, text, st, col);
            st.fontSize = old;
        }

        void DrawParty(Rect mr, GameSession s)
        {
            var f = PanelKit.Flow;
            var units = f != null ? f.PartyUnits : null;
            if (units == null) return;
            var leader = s.Leader;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u == null || !u.IsAlive && !u.Downed) continue;
                var v = f.ViewOf(u);
                Vector2 wp = v != null ? v.FeetPosition : new Vector2(u.Position.x, u.Position.y);
                var p = ToMap(wp.x, wp.y);
                if (!InView(p)) continue;
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

        /// <summary>The NPC under the mouse shows its own name tag on top, even when the layout left it out.</summary>
        void DrawHover(Rect mr, GameSession s)
        {
            if (plan == null || dragging || !PanelKit.Hover(mr)) return;
            var mouse = Event.current.mousePosition;
            MapNpcDef best = null;
            float bestD = 12f * 12f;
            Vector2 bp = default;
            foreach (var n in npcs)
            {
                if (n == null) continue;
                var p = ToMap(n.pos.x, n.pos.y);
                float d = (p - mouse).sqrMagnitude;
                if (d < bestD) { bestD = d; best = n; bp = p; }
            }
            if (best == null) return;
            var own = plan.LabelOf(best.npc);
            if (own != null && own.Kind == MapLabelKind.Npc && own.Placed) return;
            string text = s.NpcName(best.npc);
            float w = PanelKit.TextWidth(text, TagStyle()) + MapLabels.TextPadding;
            var r = new Rect(bp.x - w * 0.5f, bp.y + 9f, w, MapLabels.LabelHeight);
            r.x = Mathf.Clamp(r.x, mr.x, mr.xMax - r.width);
            if (r.yMax > mr.yMax) r.y = bp.y - 9f - r.height - (plan.Glyphs.ContainsKey(best.npc) ? 22f : 0f);
            TagAt(r, text, Ui.Ink, new Color(0.29f, 0.23f, 0.17f, 0.8f));
        }

        // ------------------------------------------------------------------ legend

        static readonly string[] LegendText = { "Your party", "Folk", "Enemies", "Spirit lantern", "Chest", "Exit", "Quest", "Turn in", "Objective" };
        static readonly Color[] LegendColors =
        {
            Ui.Hex("#3FC7EB"), Ui.Hex("#f2c14e"), Ui.Hex("#d9534a"), Ui.Hex("#ffd86b"), Ui.Hex("#c98b2e"), new Color(1f, 0.82f, 0.4f, 1f),
            Ui.Hex("#ffd23a"), Ui.Hex("#ffd23a"), new Color(1f, 0.86f, 0.3f, 0.9f),
        };

        void DrawLegend(Rect r)
        {
            float x = r.x, y = r.y;
            for (int i = 0; i < LegendText.Length; i++)
            {
                float w = PanelKit.TextWidth(LegendText[i], PanelKit.TextSmall);
                if (x + w + 30f > r.xMax && x > r.x) { x = r.x; y += 28f; }   // wrap onto a second row
                var c = new Vector2(x + 8f, y + r.height * 0.5f);
                switch (i)
                {
                    case 6: Glyph(new Rect(c.x - 9f, c.y - 12f, 18f, 24f), "!", LegendColors[i], 19); break;
                    case 7: Glyph(new Rect(c.x - 9f, c.y - 12f, 18f, 24f), "?", LegendColors[i], 19); break;
                    case 8: PanelKit.Tex(new Rect(c.x - 10f, c.y - 10f, 20f, 20f), PanelArt.RingCircle, LegendColors[i]); break;
                    default: Dot(c, 12f, LegendColors[i], Ui.Ink); break;
                }
                PanelKit.Label(new Rect(x + 22f, y + 4f, w + 4f, 26f), LegendText[i], PanelKit.TextSmall);
                x += w + 52f;
            }
        }
    }
}
