// Map panel (M) geometry and NPC/exit label planning (Docs/UI_Panels.md "Map"), pure C# so the layout is tested
// on the real maps (TestsQuestMarkers):
//   MapViewport   the map rect on screen, mouse-wheel zoom 1–3× around the cursor and drag pan (world ↔ screen)
//   MapLabels     short map names (NpcDef.shortName, else the name without its honorific), label priorities, and
//                 the plan: quest NPCs first, then exits, then service NPCs (a crowd of them sharing a region becomes one
//                 label with RegionDef.name when their own labels do not fit), companions and everyone else, placed by
//                 LabelLayout around their dots without covering dots, quest glyphs or each other.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Util;

namespace Lanternvale.World
{
    /// <summary>The visible part of a map in a screen rect: zoom (1 = whole map) around a centre in metres.</summary>
    public struct MapViewport
    {
        public const float MinZoom = 1f, MaxZoom = 3f;

        public float MapWidth, MapDepth;
        /// <summary>Map rect on screen (y down; the map's y axis points up).</summary>
        public LabelRect Screen;
        public float Zoom;
        /// <summary>Centre of the view in map metres.</summary>
        public float CenterX, CenterY;

        public MapViewport(float mapWidth, float mapDepth, LabelRect screen)
        {
            MapWidth = Math.Max(1f, mapWidth);
            MapDepth = Math.Max(1f, mapDepth);
            Screen = screen;
            Zoom = 1f;
            CenterX = MapWidth * 0.5f;
            CenterY = MapDepth * 0.5f;
        }

        public float ViewWidth => MapWidth / Math.Max(MinZoom, Zoom);
        public float ViewDepth => MapDepth / Math.Max(MinZoom, Zoom);
        /// <summary>Lower-left corner of the view in metres.</summary>
        public float X0 => Clamp(CenterX - ViewWidth * 0.5f, 0f, MapWidth - ViewWidth);
        public float Y0 => Clamp(CenterY - ViewDepth * 0.5f, 0f, MapDepth - ViewDepth);
        public float PixelsPerMetre => Screen.W / Math.Max(1e-3f, ViewWidth);

        /// <summary>Keeps the zoom in [1, 3] and the view inside the map.</summary>
        public void Clamp()
        {
            Zoom = Clamp(Zoom, MinZoom, MaxZoom);
            CenterX = Clamp(CenterX, ViewWidth * 0.5f, MapWidth - ViewWidth * 0.5f);
            CenterY = Clamp(CenterY, ViewDepth * 0.5f, MapDepth - ViewDepth * 0.5f);
        }

        public void ToScreen(float x, float y, out float sx, out float sy)
        {
            sx = Screen.X + (x - X0) / ViewWidth * Screen.W;
            sy = Screen.YMax - (y - Y0) / ViewDepth * Screen.H;
        }

        public void ToWorld(float sx, float sy, out float x, out float y)
        {
            x = X0 + (sx - Screen.X) / Math.Max(1e-3f, Screen.W) * ViewWidth;
            y = Y0 + (Screen.YMax - sy) / Math.Max(1e-3f, Screen.H) * ViewDepth;
        }

        /// <summary>Changes the zoom keeping the map point under the screen point (the cursor) where it is.</summary>
        public void ZoomAt(float zoom, float sx, float sy)
        {
            ToWorld(sx, sy, out float wx, out float wy);
            Zoom = Clamp(zoom, MinZoom, MaxZoom);
            float fx = (sx - Screen.X) / Math.Max(1e-3f, Screen.W), fy = (Screen.YMax - sy) / Math.Max(1e-3f, Screen.H);
            CenterX = wx - (fx - 0.5f) * ViewWidth;
            CenterY = wy - (fy - 0.5f) * ViewDepth;
            Clamp();
        }

        /// <summary>Drags the map by a screen delta (the map follows the mouse).</summary>
        public void PanBy(float dsx, float dsy)
        {
            CenterX = X0 + ViewWidth * 0.5f - dsx / Math.Max(1e-3f, Screen.W) * ViewWidth;
            CenterY = Y0 + ViewDepth * 0.5f + dsy / Math.Max(1e-3f, Screen.H) * ViewDepth;
            Clamp();
        }

        /// <summary>The screen point lies inside the map rect grown by margin.</summary>
        public bool OnScreen(float sx, float sy, float margin = 0f) =>
            sx >= Screen.X - margin && sx <= Screen.XMax + margin && sy >= Screen.Y - margin && sy <= Screen.YMax + margin;

        static float Clamp(float v, float lo, float hi) => hi < lo ? (lo + hi) * 0.5f : v < lo ? lo : v > hi ? hi : v;
    }

    public enum MapLabelKind { Npc, Cluster, Exit }

    public sealed class MapLabel
    {
        public MapLabelKind Kind;
        /// <summary>Npc id, transition id, or "cluster:&lt;region or first member&gt;".</summary>
        public string Id = "";
        public string Text = "";
        /// <summary>Cluster members (npc ids); the npc itself for an Npc label.</summary>
        public readonly List<string> Members = new List<string>();
        /// <summary>Screen anchor (the dot, a cluster's centroid, an exit's centre).</summary>
        public float AnchorX, AnchorY;
        public QuestMarkerInfo Marker;
        public int Priority;
        public LabelLayout.Item Item;

        public bool Placed => Item != null && Item.Placed;
        public LabelRect Rect => Item != null ? Item.Rect : default;
        public bool Leader => Item != null && Item.Leader;
    }

    public sealed class MapLabelPlan
    {
        /// <summary>Every label considered (placed or not), in placement order.</summary>
        public readonly List<MapLabel> Labels = new List<MapLabel>();
        /// <summary>Npc id → the label that names it (its own, or its cluster's).</summary>
        public readonly Dictionary<string, MapLabel> ByNpc = new Dictionary<string, MapLabel>(StringComparer.Ordinal);
        /// <summary>Npc id → screen rect of its quest glyph (above the dot).</summary>
        public readonly Dictionary<string, LabelRect> Glyphs = new Dictionary<string, LabelRect>(StringComparer.Ordinal);
        public LabelLayout Layout;

        public MapLabel LabelOf(string npcId) => npcId != null && ByNpc.TryGetValue(npcId, out var l) ? l : null;
    }

    /// <summary>What MapLabels.Plan needs (the Map panel fills it from the session; tests from the data).</summary>
    public sealed class MapLabelInput
    {
        public GameDatabase Db;
        public MapDef Map;
        /// <summary>Visible NPCs of the map (GameSession.VisibleNpcs).</summary>
        public IList<MapNpcDef> Npcs;
        /// <summary>Marker of an npc (GameSession.QuestMarkerOf); null = no markers.</summary>
        public Func<string, QuestMarkerInfo> MarkerOf;
        public MapViewport View;
        /// <summary>Width of a label's text in screen units (the label adds MapLabels.TextPadding).</summary>
        public Func<string, float> TextWidth;
        /// <summary>Exits to label (MapRuntime.IsTransitionVisible); null = every transition.</summary>
        public Func<TransitionDef, bool> ShowTransition;
        /// <summary>Other dots no label may cover (chests, enemies, lanterns), screen rects.</summary>
        public readonly List<LabelRect> Obstacles = new List<LabelRect>();
        /// <summary>Label exits too.</summary>
        public bool Exits = true;
    }

    public static class MapLabels
    {
        public const float LabelHeight = 20f;
        public const float TextPadding = 12f;
        public const float NpcDot = 10f;
        /// <summary>Quest glyph box above an NPC dot (width, height; main quests 15 % larger) and its centre's height above the dot.</summary>
        public const float GlyphW = 14f, GlyphH = 20f, GlyphRise = 18f;
        /// <summary>Service NPCs closer than this (screen units) may be merged into one crowd label.</summary>
        public const float CrowdPx = 26f;
        /// <summary>Longest map name before it is cut to its first word.</summary>
        public const int MaxNameChars = 16;

        static readonly string[] Honorifics =
        {
            "Sir ", "Dame ", "Lady ", "Lord ", "Magister ", "Brother ", "Sister ", "Sergeant ", "Captain ", "Elder ", "Old ",
            "Chief ", "Master ", "Mistress ", "Father ", "Mother ", "Keeper ", "Archivist ", "Warden ", "Lieutenant ", "Ser ",
            "The ",
        };

        // ------------------------------------------------------------------ geometry

        /// <summary>
        /// Size of the map rect for a GUI of uiWidth × uiHeight (Ui.Width/Height): as wide as the window allows (≤ 1552),
        /// keeping the map's aspect, at most uiHeight − 280 tall and at least 420 wide.
        /// </summary>
        public static void MapSize(float uiWidth, float uiHeight, float mapWidth, float mapDepth, out float w, out float h)
        {
            float maxW = Math.Min(1600f, uiWidth - 60f);
            w = maxW - 48f;
            h = w * Math.Max(1f, mapDepth) / Math.Max(1f, mapWidth);
            float maxH = uiHeight - 280f;
            if (h > maxH) { w *= maxH / h; h = maxH; }
            w = Math.Max(w, 420f);
        }

        // ------------------------------------------------------------------ names

        /// <summary>Map label of an npc or companion: NpcDef.shortName, else the name without honorific, cut to its first word when long.</summary>
        public static string ShortName(GameDatabase db, string id)
        {
            if (db == null || string.IsNullOrEmpty(id)) return id ?? "";
            if (db.Npcs.TryGetValue(id, out var n))
            {
                if (!string.IsNullOrEmpty(n.shortName)) return n.shortName;
                return Shorten(n.name);
            }
            if (db.Companions.TryGetValue(id, out var c)) return Shorten(c.name);
            return id;
        }

        /// <summary>"Magister Quillon Ashby" → "Quillon Ashby"; "Dorrit Applewhistle" → "Dorrit"; short names stay.</summary>
        public static string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string s = name.Trim();
            if (s.Length > 12) s = StripHonorific(s);
            if (s.Length > MaxNameChars)
            {
                int sp = s.IndexOf(' ');
                if (sp > 0) s = s.Substring(0, sp);
            }
            return s;
        }

        /// <summary>The name without a leading title ("Sir ", "Sergeant ", "The "…) when something is left.</summary>
        public static string StripHonorific(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            foreach (var h in Honorifics)
                if (name.Length > h.Length + 1 && name.StartsWith(h, StringComparison.Ordinal)) return name.Substring(h.Length).Trim();
            return name;
        }

        /// <summary>RegionDef.name, else a readable form of its id ("reg_training_yard" → "Training Yard", "bw_reg_docks" → "Docks").</summary>
        public static string RegionName(RegionDef r)
        {
            if (r == null) return "";
            if (!string.IsNullOrEmpty(r.name)) return r.name;
            var parts = new List<string>((r.id ?? "").Split('_'));
            int reg = parts.IndexOf("reg");
            if (reg >= 0) parts.RemoveRange(0, reg + 1);
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(p[0])).Append(p.Substring(1));
            }
            return sb.ToString();
        }

        /// <summary>Vendors, trainers and innkeepers.</summary>
        public static bool IsService(GameDatabase db, string id) =>
            db != null && id != null && db.Npcs.TryGetValue(id, out var n) &&
            ((n.vendor != null && n.vendor.Count > 0) || (n.trains != null && n.trains.Length > 0) || n.innkeeper);

        /// <summary>Placement priority (lower first): quest markers by kind, exits, service NPCs, companions, everyone else.</summary>
        public static int PriorityOf(GameDatabase db, string npcId, QuestMarkerInfo marker)
        {
            if (marker != null && !marker.IsNone) return 4 - QuestMarkers.Rank(marker.Kind);   // 0 ready … 3 later
            if (IsService(db, npcId)) return 6;
            if (db != null && npcId != null && db.Companions.ContainsKey(npcId)) return 7;
            return 8;
        }

        public const int ExitPriority = 4, CrowdPriority = 5;

        /// <summary>Screen rect of the quest glyph drawn above an NPC dot.</summary>
        public static LabelRect GlyphRect(float x, float y, bool main)
        {
            float k = main ? 1.15f : 1f;
            return LabelRect.Around(x, y - GlyphRise * k, GlyphW * k, GlyphH * k);
        }

        // ------------------------------------------------------------------ plan

        public static MapLabelPlan Plan(MapLabelInput input)
        {
            var plan = new MapLabelPlan();
            var view = input.View;
            var layout = new LabelLayout(view.Screen);
            plan.Layout = layout;
            layout.Obstacles.AddRange(input.Obstacles);
            Func<string, float> width = input.TextWidth ?? (t => (t?.Length ?? 0) * 8f);
            var db = input.Db;
            var map = input.Map;

            // ---- NPC dots and glyphs (obstacles), one entry per visible npc on screen
            var npcs = new List<MapLabel>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (input.Npcs != null)
                foreach (var n in input.Npcs)
                {
                    if (n == null || string.IsNullOrEmpty(n.npc) || !seen.Add(n.npc)) continue;
                    view.ToScreen(n.pos.x, n.pos.y, out float sx, out float sy);
                    if (!view.OnScreen(sx, sy)) continue;
                    var marker = input.MarkerOf?.Invoke(n.npc);
                    if (marker != null && marker.IsNone) marker = null;
                    var l = new MapLabel { Kind = MapLabelKind.Npc, Id = n.npc, Text = ShortName(db, n.npc), AnchorX = sx, AnchorY = sy, Marker = marker };
                    l.Members.Add(n.npc);
                    l.Priority = PriorityOf(db, n.npc, marker);
                    int own = layout.Obstacles.Count;
                    layout.Obstacles.Add(LabelRect.Around(sx, sy, NpcDot + 4f, NpcDot + 4f));
                    if (marker != null)
                    {
                        var g = GlyphRect(sx, sy, marker.Main);
                        plan.Glyphs[n.npc] = g;
                        layout.Obstacles.Add(g);
                    }
                    l.Item = NewItem(l, width, own);
                    l.Item.Important = marker != null;
                    npcs.Add(l);
                }

            // ---- exits
            var exits = new List<MapLabel>();
            if (input.Exits && map?.transitions != null)
                foreach (var t in map.transitions)
                {
                    if (t == null || (input.ShowTransition != null && !input.ShowTransition(t))) continue;
                    var size = MapRuntime.TransitionSize(t);
                    view.ToScreen(t.pos.x - size.x * 0.5f, t.pos.y + size.y * 0.5f, out float x0, out float y0);
                    view.ToScreen(t.pos.x + size.x * 0.5f, t.pos.y - size.y * 0.5f, out float x1, out float y1);
                    var rr = new LabelRect(x0, y0, Math.Max(6f, x1 - x0), Math.Max(6f, y1 - y0));
                    if (!view.OnScreen(rr.CenterX, rr.CenterY)) continue;
                    int own = layout.Obstacles.Count;
                    layout.Obstacles.Add(rr);
                    var l = new MapLabel { Kind = MapLabelKind.Exit, Id = t.id, Text = string.IsNullOrEmpty(t.label) ? "Exit" : t.label, AnchorX = rr.CenterX, AnchorY = rr.CenterY, Priority = ExitPriority };
                    l.Item = NewItem(l, width, own);
                    l.Item.Gap = Math.Max(rr.W, rr.H) * 0.5f + 4f;
                    exits.Add(l);
                }

            // ---- crowds: service NPCs without a marker that share a region, or stand within CrowdPx of each other
            var crowds = new List<List<MapLabel>>();
            var crowdName = new List<string>();
            var inCrowd = new HashSet<MapLabel>();
            if (map?.regions != null)
                foreach (var r in map.regions)
                {
                    if (r == null) continue;
                    var members = new List<MapLabel>();
                    foreach (var l in npcs)
                    {
                        if (l.Marker != null || inCrowd.Contains(l) || !IsService(db, l.Id)) continue;
                        var p = PosOf(input, l.Id);
                        if (MapRuntime.RectContains(r.pos, r.size, p)) members.Add(l);
                    }
                    if (members.Count < 2) continue;
                    foreach (var m in members) inCrowd.Add(m);
                    crowds.Add(members);
                    crowdName.Add(RegionName(r));
                }
            var loose = new List<MapLabel>();
            foreach (var l in npcs) if (l.Marker == null && !inCrowd.Contains(l) && IsService(db, l.Id)) loose.Add(l);
            var parent = new int[loose.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < loose.Count; i++)
                for (int j = i + 1; j < loose.Count; j++)
                {
                    float dx = loose[i].AnchorX - loose[j].AnchorX, dy = loose[i].AnchorY - loose[j].AnchorY;
                    if (dx * dx + dy * dy <= CrowdPx * CrowdPx) parent[Find(i)] = Find(j);
                }
            var groups = new Dictionary<int, List<MapLabel>>();
            for (int i = 0; i < loose.Count; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var g)) groups[root] = g = new List<MapLabel>();
                g.Add(loose[i]);
            }
            foreach (var g in groups.Values)
            {
                if (g.Count < 3) continue;
                foreach (var m in g) inCrowd.Add(m);
                crowds.Add(g);
                crowdName.Add(g.Count + " folk");
            }

            // ---- place: quest NPCs, exits, crowds (their own names if they all fit, else one crowd label), the rest
            var ordered = new List<MapLabel>();
            foreach (var l in npcs) if (l.Marker != null) ordered.Add(l);
            ordered.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : npcs.IndexOf(a).CompareTo(npcs.IndexOf(b)));
            foreach (var l in ordered) Place(plan, layout, l);
            foreach (var l in exits) Place(plan, layout, l);
            for (int c = 0; c < crowds.Count; c++)
            {
                var members = crowds[c];
                bool all = true;
                foreach (var m in members) if (!layout.TryPlace(m.Item)) { all = false; break; }
                if (all)
                {
                    foreach (var m in members) Add(plan, m);
                    continue;
                }
                foreach (var m in members) layout.Remove(m.Item);
                float cx = 0f, cy = 0f;
                foreach (var m in members) { cx += m.AnchorX; cy += m.AnchorY; }
                var crowd = new MapLabel
                {
                    Kind = MapLabelKind.Cluster, Id = "cluster:" + crowdName[c], Text = crowdName[c],
                    AnchorX = cx / members.Count, AnchorY = cy / members.Count, Priority = CrowdPriority,
                };
                foreach (var m in members) crowd.Members.Add(m.Id);
                crowd.Item = NewItem(crowd, width, -1);
                // the crowd's own dots sit under its centroid: start beyond them
                float spread = 0f;
                foreach (var m in members) spread = Math.Max(spread, Math.Max(Math.Abs(m.AnchorX - crowd.AnchorX), Math.Abs(m.AnchorY - crowd.AnchorY)));
                crowd.Item.Gap = spread + NpcDot;
                crowd.Item.Important = true;
                Place(plan, layout, crowd);
                foreach (var m in members) plan.ByNpc[m.Id] = crowd;
            }
            var rest = new List<MapLabel>();
            foreach (var l in npcs) if (l.Marker == null && !inCrowd.Contains(l)) rest.Add(l);
            rest.Sort((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : npcs.IndexOf(a).CompareTo(npcs.IndexOf(b)));
            foreach (var l in rest) Place(plan, layout, l);
            return plan;
        }

        static LabelLayout.Item NewItem(MapLabel l, Func<string, float> width, int own) => new LabelLayout.Item
        {
            Id = l.Id, AnchorX = l.AnchorX, AnchorY = l.AnchorY, Width = width(l.Text) + TextPadding, Height = LabelHeight,
            Priority = l.Priority, Gap = NpcDot * 0.5f + 3f, OwnObstacle = own,
        };

        static void Place(MapLabelPlan plan, LabelLayout layout, MapLabel l)
        {
            layout.TryPlace(l.Item);
            Add(plan, l);
        }

        static void Add(MapLabelPlan plan, MapLabel l)
        {
            plan.Labels.Add(l);
            if (l.Kind == MapLabelKind.Npc) plan.ByNpc[l.Id] = l;
        }

        static Vec2 PosOf(MapLabelInput input, string npcId)
        {
            foreach (var n in input.Npcs) if (n != null && n.npc == npcId) return n.pos;
            return default;
        }
    }
}
