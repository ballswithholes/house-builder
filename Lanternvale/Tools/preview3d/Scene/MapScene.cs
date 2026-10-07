// Builds a map exactly like MapView.BuildAll does (Scripts/Game/World/MapView.cs), minus the per-frame scripts:
// the real MapSky / MapTerrain (ground, painted decals) / MapBackdrop / DayNight, props, foreground and chests from
// PropModels.Create with MapView's seeds, transforms, light placement and hour gating, merged blob shadows, the
// shared transition waymarkers (Waymarker) and occluder test (PropOccluder), light halos, and the real unit models
// (Scene/UnitPoser: UnitModels + UnitAnimator, idling) for the party leader, NPCs and encounter enemies, placed and
// faced like GameFlow.Views does.
// Keep in sync with MapView when its building logic changes (the copied parts are marked "as MapView").
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Game;
using UnityEngine;

namespace Lanternvale.Preview
{
    public sealed class MapScene
    {
        public sealed class Options
        {
            public float? Hour;
            public float Yaw;
            public float Zoom = 6.2f;
            /// <summary>A pitch (degrees below the horizon) instead of the rig's eased one: a vista of the backdrop (not a player view).</summary>
            public float? Pitch;
            public Vector2? At;
            public bool Units = true;
            public bool Halos = true;
            public string Spawn = "default";
            /// <summary>Model of the party leader at the spawn (a new game starts with the class picked at creation).</summary>
            public string Player = "char_warrior";
            /// <summary>Story flags treated as set (content behind requireFlag appears; "*" = every flag).</summary>
            public HashSet<string> Flags = new HashSet<string>();
            public bool Has(string flag) => string.IsNullOrEmpty(flag) || Flags.Contains("*") || Flags.Contains(flag);
            /// <summary>A flag expression as FlagStore.Test reads it ("a&amp;!b"), against these flags ("*": every flag set).</summary>
            public bool Test(string expr)
            {
                if (string.IsNullOrEmpty(expr)) return true;
                foreach (var part in expr.Split('&'))
                {
                    var term = part.Trim();
                    if (term.Length == 0) continue;
                    bool neg = term[0] == '!';
                    bool set = Has(neg ? term.Substring(1).Trim() : term);
                    if (neg == set) return false;
                }
                return true;
            }
            /// <summary>Quest markers over the NPCs: "" none, "auto" (the game's rules), or "npc:kind[+main],…" (Scene/PreviewMarkers).</summary>
            public string Markers = "";
            /// <summary>--markers auto: quests to start (q), set to a stage (q=stage) or complete (q=done); the main character's level.</summary>
            public List<string> Quests = new List<string>();
            public int Level = 5;
        }

        sealed class PObj
        {
            public string art = "";
            public PropDef prop;
            public Transform holder;
            public PropModel model;
            public float scale = 1f;
            public bool flip, isLantern, lanternLit, groundShadow, visible = true, occluder;
            public float occludedAlpha = 1f;
            public Color tint = Color.white;
            public Bounds bounds;
            public bool hasBounds;
            public PropOccluder occ;
        }

        sealed class WLight
        {
            public Vector3 position;
            public Color color;
            public float intensity, range, haloSize, level = 1f;
            public bool flicker, nightOnly, on = true, dormantGlow;
            public Action<bool> setLit;
            public float current;
        }

        public readonly MapDef Def;
        public readonly GameDatabase Db;
        public readonly Transform Root;
        public DayNight DayNight { get; private set; }
        public ViewCam View { get; private set; }
        public Lighting Lighting { get; private set; }
        public Vector3 LookAt { get; private set; }
        public float Pitch { get; private set; }
        public float Distance { get; private set; }
        public int PropCount, LightCount;

        readonly Options opt;
        readonly List<Mesh> owned = new List<Mesh>();
        readonly List<PObj> props = new List<PObj>();
        readonly List<PObj> foreground = new List<PObj>();
        readonly List<PObj> chests = new List<PObj>();
        readonly List<PObj> nightGlows = new List<PObj>();
        readonly List<WLight> lights = new List<WLight>();
        MapTerrain terrain;
        MapBackdrop backdrop;
        MapSky sky;
        Transform propsRoot, decalsRoot, fgRoot, markersRoot, unitsRoot;
        float fogStart = 30f, fogEnd = 150f, fogZenith = 0.22f, windStrength = 0.06f;

        public MapScene(GameDatabase db, MapDef def, Options options)
        {
            Db = db;
            Def = def;
            opt = options ?? new Options();
            Root = new GameObject("Map: " + def.id).transform;
        }

        // ================================================================== build (as MapView.BuildAll)

        /// <summary>As MapView.Indoor (DayNight.IsIndoor): a cave or crypt map.</summary>
        public bool Indoor { get; private set; }

        public void Build(int width, int height)
        {
            var def = Def;
            ArtLibrary.Init();
            Indoor = DayNight.IsIndoor(def);
            DayNight = new DayNight(def.ambient, Indoor);
            if (opt.Hour.HasValue) DayNight.SetHour(opt.Hour.Value);

            var skyTop = Ui.Hex(string.IsNullOrEmpty(def.skyTop) ? "#9fd3f0" : def.skyTop);
            var skyBottom = Ui.Hex(string.IsNullOrEmpty(def.skyBottom) ? "#fdf1d6" : def.skyBottom);
            var amb = def.ambient ?? new AmbientDef();
            // mist: a nearer haze, cooled towards the zenith (a dusk horizon alone would wash the scene sepia)
            if (amb.mist) { fogStart = 24f; fogEnd = 120f; fogZenith = 0.38f; }
            windStrength = amb.leaves ? 0.075f : amb.embers ? 0.045f : 0.06f;
            // as MapView: indoors, near dark fog in the vault's colour
            if (Indoor) { fogZenith = 0.12f; windStrength = 0.015f; }

            sky = new MapSky(Root, skyTop, skyBottom, MapTerrain.StableHash(def.id), owned, Indoor);
            terrain = new MapTerrain(def, Root, owned);
            terrain.Build(TerrainMaterial(def), def.groundTile > 0f ? def.groundTile : 8f);
            backdrop = new MapBackdrop(def, terrain, Root, owned);
            Guard("backdrop", backdrop.Build);
            Guard("ground cover", () => terrain.BuildGroundCover(!backdrop.HasForest));
            Guard("brook", () => terrain.BuildStream(skyBottom));

            propsRoot = Child("Props");
            decalsRoot = Child("Decals");
            fgRoot = Child("Foreground");
            markersRoot = Child("Markers");
            unitsRoot = Child("Units");

            Guard("decals", () => terrain.BuildDecals(decalsRoot));   // the real MapTerrain decals (trail ribbons, soft patches)
            for (int i = 0; i < def.props.Count; i++)
            {
                var p = def.props[i];
                if (p == null || string.IsNullOrEmpty(p.art) || p.art.StartsWith("decal_", StringComparison.Ordinal)) continue;
                int index = i;
                Guard(p.art, () => BuildProp(p, index));
            }
            for (int i = 0; i < def.foreground.Count; i++)
            {
                var p = def.foreground[i];
                if (p == null || string.IsNullOrEmpty(p.art)) continue;
                int index = i;
                Guard(p.art, () => BuildForeground(p, index));
            }
            foreach (var c in def.chests) if (c != null) Guard("chest " + c.id, () => BuildChest(c));
            foreach (var t in def.transitions) if (t != null) Guard("transition " + t.id, () => BuildTransition(t));
            UpdateCliffLanterns();
            if (opt.Units) Guard("units", BuildUnits);
            RebuildShadows();

            // camera (CameraRig), then the frame's mood: sky, fog, lights (MapView.UpdateView at t = 0)
            PlaceCamera(width, height);
            var camGo = new GameObject("Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = CameraMath.FieldOfView;
            camGo.transform.SetPositionAndRotation(View.Pos, Quaternion.LookRotation(View.F, World3D.Up));
            PresentationHost.Cam = cam;
            if (opt.Units && !string.IsNullOrEmpty(opt.Markers)) Guard("quest markers", () => PreviewMarkers.Build(this, opt));
            sky.Update(cam, DayNight, 0f);
            ApplyMood(cam);
            backdrop.Update(0f, 0f, DayNight);
            terrain.Update(0f);
            UpdateFades();
            var lightList = UpdateLights();
            if (opt.Halos) BuildHalos();
            var focus = GroundFocus();
            Lighting = Lighting.FromScene(View.Pos, lightList, focus);
            // as MapView.ApplyMood: the night grade (_LV_Grade)
            Lighting.Grade = DayNight.NightGrade;
            Lighting.GradeTint = new V3(DayNight.NightGradeTint.x, DayNight.NightGradeTint.y, DayNight.NightGradeTint.z);
            // ... and the warmth (_LV_Warmth): golden-hour lift, amber lamp pools
            var wm = DayNight.Warmth;
            Lighting.GoldenLift = wm.x;
            Lighting.LampAmber = wm.y;
            Lighting.LampMask = wm.z;
            LightCount = Lighting.LightCount;
        }

        void Guard(string what, Action build)
        {
            try { build(); }
            catch (Exception e) { Debug.LogError($"map '{Def.id}': building {what} failed: {e}"); }
        }

        Transform Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, false);
            return go.transform;
        }

        // as MapView.Seed
        static int Seed(string mapId, Vector2 pos, int salt)
        {
            unchecked
            {
                int h = MapTerrain.StableHash(mapId) ^ (Mathf.RoundToInt(pos.x * 10f) * 73856093) ^ (Mathf.RoundToInt(pos.y * 10f) * 19349663) ^ (salt * 83492791);
                return (h & 0x7fffffff) % 100003;
            }
        }

        // ------------------------------------------------------------------ terrain material (as MapView.TerrainMaterial)

        static Material TerrainMaterial(MapDef def)
        {
            string groundKey = Biomes.GroundKey(def);
            string sideKey = Biomes.SideKey(def);
            float tile = def.groundTile > 0f ? def.groundTile : 8f;
            var ground = ArtLibrary.Texture(groundKey);
            var side = ArtLibrary.Texture(sideKey);
            var m = new Material(Shader.Find("Lanternvale/Terrain")) { name = "LV Terrain " + groundKey };
            m.SetTexture(Materials3D.MainTexId, ground);
            m.SetTexture(Shader.PropertyToID("_SideTex"), side);
            m.SetFloat(Materials3D.PlanarScaleId, 1f / tile);
            m.SetFloat(Shader.PropertyToID("_SidePlanarScale"), Biomes.SidePlanarScale(def));
            m.SetColor(Shader.PropertyToID("_MainAvg"), Average(ground, groundKey));
            m.SetColor(Shader.PropertyToID("_SideAvg"), Average(side, sideKey));
            // as MapView: the detail layer (gravel / leaf litter), read back by the Collector
            var detail = MapTerrain.DetailTexture(def, out float detailScale);
            if (detail != null)
            {
                m.SetTexture(Shader.PropertyToID("_DetailTex"), detail);
                m.SetFloat(Shader.PropertyToID("_DetailPlanarScale"), detailScale);
            }
            return m;
        }

        /// <summary>The texture's average sRGB colour (MapView reads it back from the GPU; same result).</summary>
        static Color Average(Texture2D tex, string key)
        {
            Color fallback = Biomes.AverageFallback(key);
            if (tex == null || tex.Pixels == null || tex.width < 8) return fallback;
            double r = 0, g = 0, b = 0;
            foreach (var p in tex.Pixels) { r += p.r; g += p.g; b += p.b; }
            double k = 1.0 / (tex.Pixels.Length * 255.0);
            var c = new Color((float)(r * k), (float)(g * k), (float)(b * k), 1f);
            return c.r + c.g + c.b > 0.05f ? c : fallback;
        }

        // ------------------------------------------------------------------ props (as MapView.PlaceModel / BuildProp / BuildForeground)

        void PlaceModel(PObj o, int seed)
        {
            var m = PropModels.Create(o.art, seed);
            var t = m.Root.transform;
            t.SetParent(o.holder, false);
            t.localPosition = Vector3.zero;
            t.localRotation = World3D.Upright;
            t.localScale = new Vector3(o.flip ? -o.scale : o.scale, o.scale, o.scale);
            if (m.SetLit != null && o.isLantern) m.SetLit(o.lanternLit);
            else if (m.SetLit != null && (o.prop == null || o.prop.light == null)) nightGlows.Add(o);
            o.model = m;
            // look: tint (MapView.ApplyLook writes it to every renderer's property block)
            foreach (var r in m.Renderers)
            {
                if (r == null) continue;
                r.Block.SetColor(Materials3D.TintId, o.tint);
            }
            ComputeBounds(o);
            PropCount++;
        }

        static void ComputeBounds(PObj o)
        {
            o.hasBounds = false;
            foreach (var r in o.model.Renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!o.hasBounds) { o.bounds = r.bounds; o.hasBounds = true; }
                else o.bounds.Encapsulate(r.bounds);
            }
        }

        static float TopOf(PObj o) => o.hasBounds ? Mathf.Max(0.3f, -o.bounds.min.z) : 1.5f;

        /// <summary>As MapView.PropShown: the prop's requireFlag holds and its hideFlag does not (--flags).</summary>
        bool PropShown(PropDef p) => opt.Test(p.requireFlag) && !(!string.IsNullOrEmpty(p.hideFlag) && opt.Test(p.hideFlag));

        void BuildProp(PropDef p, int index)
        {
            if (!PropShown(p)) return;   // as MapView: built hidden (no model, light or shadow shows)
            var holder = new GameObject(p.art).transform;
            holder.SetParent(propsRoot, false);
            holder.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);
            var o = new PObj
            {
                art = p.art, prop = p, holder = holder,
                scale = p.scale > 0f ? p.scale : 1f, flip = p.flip,
                tint = string.IsNullOrEmpty(p.tint) ? Color.white : Ui.Hex(p.tint),
            };
            bool lantern = p.art.Contains("spirit_lantern");
            if (lantern) { o.isLantern = true; o.lanternLit = !p.art.EndsWith("_dark", StringComparison.Ordinal); }
            PlaceModel(o, Seed(Def.id, new Vector2(p.pos.x, p.pos.y), index));
            o.groundShadow = true;
            o.occluder = TopOf(o) >= 2.5f;
            o.occludedAlpha = 0.35f;   // MapView.OccluderFadeAlpha
            if (o.occluder) o.occ = PropOccluder.Build(o.model.Renderers, occGrids);   // as MapView
            if (p.light != null)
            {
                var l = AddLight(o, p.light);
                if (lantern) { l.on = o.lanternLit; l.level = l.on ? 1f : 0f; l.dormantGlow = true; l.nightOnly = false; }
            }
            else if (lantern)
            {
                var l = AddLight(o, new LightDef { color = "#ffe2a6", radius = 4.5f, intensity = 0.9f, offset = new Lanternvale.Util.Vec2(0f, -1f), flicker = true });
                l.on = o.lanternLit; l.level = l.on ? 1f : 0f; l.dormantGlow = true;
            }
            props.Add(o);
        }

        void BuildForeground(PropDef p, int index)
        {
            if (!PropShown(p)) return;
            var holder = new GameObject("FG " + p.art).transform;
            holder.SetParent(fgRoot, false);
            holder.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);
            var o = new PObj
            {
                art = p.art, prop = p, holder = holder,
                scale = p.scale > 0f ? p.scale : 1f, flip = p.flip,
                tint = string.IsNullOrEmpty(p.tint) ? Color.white : Ui.Hex(p.tint),
            };
            PlaceModel(o, Seed(Def.id, new Vector2(p.pos.x, p.pos.y), 1000 + index));
            o.groundShadow = p.art.Contains("stone");
            o.occluder = TopOf(o) >= 0.7f;
            o.occludedAlpha = 0.35f;   // MapView.ForegroundFadeAlpha
            if (o.occluder) o.occ = PropOccluder.Build(o.model.Renderers, occGrids);   // as MapView
            foreground.Add(o);
        }

        // as MapView.AddLight
        WLight AddLight(PObj o, LightDef def)
        {
            var m = o.model;
            var tr = m.Root.transform;
            bool big = m.LocalBounds.size.x * o.scale > 2.4f;
            Vector3 world;
            float ox = def.offset.x, oy = def.offset.y;
            if (oy < 0f) oy = m.Height * o.scale * 0.62f;
            if (m.LightAnchors.Count > 0)
            {
                int best = 0;
                float bd = float.MaxValue;
                for (int i = 0; i < m.LightAnchors.Count; i++)
                {
                    var a = m.LightAnchors[i];
                    float d = (a.x * o.scale - ox) * (a.x * o.scale - ox) + (a.y * o.scale - oy) * (a.y * o.scale - oy);
                    if (d < bd) { bd = d; best = i; }
                }
                world = tr.TransformPoint(m.LightAnchors[best]);
                if (big) world.y -= 0.4f;
            }
            else
            {
                float front = big ? m.LocalBounds.min.z - 0.35f / o.scale : 0f;
                world = tr.TransformPoint(new Vector3(ox / o.scale, oy / o.scale, front));
            }
            var l = new WLight
            {
                position = world,
                color = string.IsNullOrEmpty(def.color) ? Ui.Hex("#ffd9a0") : Ui.Hex(def.color),
                intensity = Mathf.Max(0f, def.intensity),
                range = Mathf.Max(0.5f, def.radius) * 1.3f,
                flicker = def.flicker,
                nightOnly = def.nightOnly,
            };
            l.haloSize = Mathf.Clamp(def.radius * 0.42f, 0.7f, 2.4f) * (big ? 1.25f : 1f);
            if (!o.isLantern && m.SetLit != null)
            {
                if (def.nightOnly) l.setLit = m.SetLit;
                else m.SetLit(true);
            }
            lights.Add(l);
            return l;
        }

        // ------------------------------------------------------------------ chests and transitions

        void BuildChest(ChestDef c)
        {
            // chests behind an unset flag stay hidden (MapView with FlagStore.Test)
            if (!opt.Has(c.requireFlag)) return;
            var holder = new GameObject("Chest " + c.id).transform;
            holder.SetParent(propsRoot, false);
            holder.localPosition = new Vector3(c.pos.x, c.pos.y, 0f);
            var o = new PObj { art = string.IsNullOrEmpty(c.art) ? "prop_chest" : c.art, holder = holder };
            PlaceModel(o, Seed(Def.id, new Vector2(c.pos.x, c.pos.y), 77));
            var e = o.hasBounds ? o.bounds.extents : new Vector3(0.5f, 0.35f, 0.4f);
            MeshCache.AddShadow(holder, e.x + 0.12f, e.y + 0.1f, 0.38f);
            chests.Add(o);
        }

        void BuildTransition(TransitionDef t)
        {
            // as MapView: a hidden transition shows once its revealFlag holds (--flags)
            if (t.hidden && (string.IsNullOrEmpty(t.revealFlag) || !opt.Test(t.revealFlag))) return;
            var size = new Vector2(t.size.x > 0f ? t.size.x : 2f, t.size.y > 0f ? t.size.y : 2f);
            var pos = new Vector2(t.pos.x, t.pos.y);
            bool locked = !opt.Test(t.requireFlag);
            string style = t.marker ?? "";
            if (style == "none") return;
            var holder = new GameObject("Transition " + t.id).transform;
            holder.SetParent(markersRoot, false);
            Waymarker wm;
            var glow = new Color(1f, 0.82f, 0.52f);
            if (Waymarker.IsEntrance(style))
            {
                // as MapView: a styled entrance facing the camera (the prop library's model, else the stand-in)
                var at = Waymarker.EntranceAt(style, pos, size);
                holder.localPosition = new Vector3(at.x, at.y, 0f);
                if (style == "portal") glow = Waymarker.PortalGlow(t.targetMap);
                wm = Waymarker.BuildEntrance(holder, style, Seed(Def.id, pos, 91), glow);
            }
            else
            {
                float W = Def.width, D = Def.depth;
                float dl = pos.x, dr = W - pos.x, df = pos.y, db = D - pos.y;
                float min = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(df, db));
                Vector2 dir = Vector2.zero, archAt = pos;
                float span = 3f;
                if (min <= 3.5f)
                {
                    if (min == dl) { dir = new Vector2(-1f, 0f); archAt = new Vector2(-0.3f, pos.y); span = size.y; }
                    else if (min == dr) { dir = new Vector2(1f, 0f); archAt = new Vector2(W + 0.3f, pos.y); span = size.y; }
                    else if (min == df) { dir = new Vector2(0f, -1f); archAt = new Vector2(pos.x, -0.3f); span = size.x; }
                    else { dir = new Vector2(0f, 1f); archAt = new Vector2(pos.x, D + 0.3f); span = size.x; }
                }
                span = Mathf.Clamp(span * 0.72f, 2.4f, 4.6f);
                // as MapView: a side exit's lantern posts stand clear of the props beside the road
                if (Mathf.Abs(dir.x) > 0.5f)
                {
                    var obstacles = new List<Rect>();
                    foreach (var po in props)
                        if (po.hasBounds && po.visible) obstacles.Add(Rect.MinMaxRect(po.bounds.min.x, po.bounds.min.y, po.bounds.max.x, po.bounds.max.y));
                    Waymarker.FitPosts(ref archAt, dir, ref span, obstacles, Mathf.Min(1.4f, size.y * 0.3f));
                }
                holder.localPosition = new Vector3(archAt.x, archAt.y, 0f);
                // as MapView.BuildTransition: the shared waymarker model in the map's style, night-only lanterns
                wm = Waymarker.Build(holder, dir, span, Waymarker.StyleFor(Def, Indoor));
            }
            if (locked) foreach (var lr in wm.Lanterns) lr.Block.SetColor(Materials3D.TintId, new Color(0.5f, 0.46f, 0.72f));
            bool pair = wm.Lamps.Count > 1;
            for (int i = 0; i < wm.Lamps.Count; i++)
            {
                // as MapView.ApplyTransitionColor: a portal glows day and night in its own colour
                if (wm.AlwaysLit)
                {
                    lights.Add(new WLight
                    {
                        position = wm.Lamps[i],
                        color = locked ? Color.Lerp(glow, new Color(0.62f, 0.55f, 0.88f), 0.6f) : glow,
                        intensity = locked ? 0.45f : 1.15f,
                        range = 6.5f,
                        flicker = true,
                        haloSize = 1.9f,
                    });
                    continue;
                }
                lights.Add(new WLight
                {
                    position = wm.Lamps[i],
                    color = locked ? new Color(0.62f, 0.55f, 0.88f) : new Color(1f, 0.82f, 0.52f),
                    intensity = (locked ? 0.38f : 0.85f) * (pair ? 0.7f : 1f),
                    range = pair ? 4.6f : 5.2f,
                    flicker = !locked,
                    nightOnly = true,
                    haloSize = pair ? 1.0f : 1.3f,
                    setLit = i == 0 ? wm.SetLit : null,
                });
            }
        }

        void UpdateCliffLanterns()
        {
            if (backdrop == null || !backdrop.HasCliffLanterns) return;
            int total = 0, lit = 0;
            foreach (var o in props) if (o.isLantern) { total++; if (o.lanternLit) lit++; }
            backdrop.SetLanternFraction(total > 0 ? (float)lit / total : 0f, false);
        }

        // ------------------------------------------------------------------ units (GameFlow.Views: CreateUnitView / CreateNpcView / CreateEncounterViews)

        public readonly List<UnitPoser> Units = new List<UnitPoser>();
        /// <summary>NPC id → its poser (quest markers).</summary>
        public readonly Dictionary<string, UnitPoser> NpcPosers = new Dictionary<string, UnitPoser>(StringComparer.Ordinal);

        void BuildUnits()
        {
            // idle heads turn towards this camera and static models are set down facing it (UnitView: CameraRig.Yaw)
            UnitPoser.CameraYaw = opt.Yaw;
            var spawn = SpawnPoint();
            int n = 0;
            // the party leader: UnitView.Create(class sprite, natural height), facing right
            AddUnit(opt.Player, 0f, spawn, 1, true, n++);
            foreach (var npc in Def.npcs)
            {
                if (npc == null || !opt.Has(npc.requireFlag)) continue;
                // as CreateNpcView: generic villagers / children get a stable look per NPC id (SetVariant)
                AddUnit(NpcSprite(npc.npc), 0f, new Vector2(npc.pos.x, npc.pos.y), npc.flip ? -1 : 1, false, n++, UnitModels.StableVariant(npc.npc ?? ""));
                if (!string.IsNullOrEmpty(npc.npc)) NpcPosers[npc.npc] = Units[Units.Count - 1];
            }
            foreach (var e in Def.encounters)
            {
                if (e == null || e.hidden || !opt.Has(e.requireFlag) || e.enemies == null) continue;
                foreach (var en in e.enemies)
                {
                    var cd = Db?.Creature(en?.creature);
                    if (cd == null) continue;
                    var home = new Vector2(en.pos.x, en.pos.y);
                    // enemies face the party leader (GameFlow.Views.cs: SetFacing(lookAt.x < home.x ? -1 : 1))
                    int facing = Mathf.Abs(spawn.x - home.x) > 0.05f ? (spawn.x < home.x ? -1 : 1) : 0;
                    AddUnit(cd.sprite, Mathf.Max(0f, cd.size), home, facing, true, n++);
                }
            }
        }

        string NpcSprite(string id)
        {
            id = id ?? "";
            string sprite = "";
            if (Db != null)
            {
                if (Db.Npcs.TryGetValue(id, out var npc)) sprite = npc.sprite;
                else if (Db.Companions.TryGetValue(id, out var comp))
                {
                    sprite = comp.sprite;
                    if (string.IsNullOrEmpty(sprite)) sprite = Db.Class(comp.classId)?.sprite ?? "";
                }
            }
            return string.IsNullOrEmpty(sprite) ? "npc_villager_a" : sprite;
        }

        void AddUnit(string key, float height, Vector2 pos, int facing, bool fadesOccluders, int index, int variant = 0)
        {
            // SetFacing(±1) is screen-right/left for the current camera yaw (UnitFacing.SideYaw); no SetFacing keeps
            // UnitView's initial world yaw
            float yaw = facing == 0 ? 90f + UnitPoser.FacingBias : UnitPoser.FacingYaw(facing, opt.Yaw);
            var u = new UnitPoser(key, height, unitsRoot, pos, yaw, variant);
            // a little idle life, desynchronised per unit (breathing, weight shift, glances)
            u.Idle(1.2f + (index * 0.37f) % 1.6f);
            Units.Add(u);
            if (fadesOccluders) fadingUnits.Add(u);
        }

        readonly List<UnitPoser> fadingUnits = new List<UnitPoser>();

        public Vector2 SpawnPoint()
        {
            foreach (var s in Def.spawns) if (s != null && s.id == opt.Spawn) return new Vector2(s.pos.x, s.pos.y);
            if (Def.spawns.Count > 0 && Def.spawns[0] != null) return new Vector2(Def.spawns[0].pos.x, Def.spawns[0].pos.y);
            return new Vector2(Def.width * 0.5f, Def.depth * 0.5f);
        }

        // ------------------------------------------------------------------ shadows (as MapView.RebuildShadows)

        void RebuildShadows()
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color32>(); var tris = new List<int>();
            void Add(PObj o)
            {
                if (!o.groundShadow || !o.visible || !o.hasBounds) return;
                var e = o.bounds.extents;
                var c = o.bounds.center;
                float big = Mathf.Max(e.x, e.y);
                float rx = Mathf.Clamp(e.x * 0.95f + 0.12f, 0.25f, 7f), ry = Mathf.Clamp(e.y * 0.95f + 0.1f, 0.2f, 5f);
                float a = big > 2f ? 0.72f : 1f;
                const float z = -0.004f;
                int b = verts.Count;
                verts.Add(new Vector3(c.x - rx, c.y - ry, z)); verts.Add(new Vector3(c.x - rx, c.y + ry, z));
                verts.Add(new Vector3(c.x + rx, c.y + ry, z)); verts.Add(new Vector3(c.x + rx, c.y - ry, z));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                var col = new Color32(255, 255, 255, (byte)(a * 255f));
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            foreach (var o in props) Add(o);
            foreach (var o in foreground) Add(o);
            var go = new GameObject("Prop Shadows");
            go.transform.SetParent(propsRoot, false);
            var m = new Mesh { name = "lv_prop_shadows" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetColors(cols); m.SetTriangles(tris, 0, true);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Materials3D.Shadow) { name = "LV Shadow (over decals)", renderQueue = 2955 };
        }

        // ================================================================== camera (CameraRig maths)

        void PlaceCamera(int width, int height)
        {
            float zoom = Mathf.Clamp(opt.Zoom, 0.5f, 40f);
            var anchor = opt.At ?? SpawnPoint();
            var bounds = new Rect(-2f, -2f, Mathf.Max(1f, Def.width) + 4f, Mathf.Max(1f, Def.depth) + 4f);
            if (Indoor)
            {
                // as CameraRig.SetIndoor / IndoorBounds: a nearer zoom limit, the look-at kept inside the walls
                zoom = Mathf.Clamp(zoom, CameraMath.IndoorMinSize, CameraMath.MaxSize);
                opt.Zoom = zoom;
                bounds = CameraMath.IndoorBounds(Def.width, Def.depth);
            }
            CameraMath.Place(anchor, opt.At.HasValue, zoom, opt.Yaw, bounds, out var pos, out var rot, out var lookAt, out float pitch, out float dist, opt.Pitch);
            LookAt = lookAt;
            Pitch = pitch;
            Distance = dist;
            View = ViewCam.Create(pos, rot, CameraMath.FieldOfView, width, height);
        }

        Vector3 GroundFocus()
        {
            // SceneLightingDriver: where the view ray meets the ground
            var f = View.F;
            return f.z > 0.01f ? View.Pos + f * (-View.Pos.z / f.z) : View.Pos + f * 15f;
        }

        // ================================================================== occluder cut-outs (as MapView.UpdateFades / UpdateCuts, settled)

        void UpdateFades()
        {
            var cp = View.Pos;
            foreach (var o in props) Cut(o, cp);
            foreach (var o in foreground) Cut(o, cp);
        }

        // as MapView: the voxelised meshes, shared by the props using the same mesh
        readonly Dictionary<Mesh, PropOccluder.Grid> occGrids = new Dictionary<Mesh, PropOccluder.Grid>();

        /// <summary>As MapView: only units in view (viewport −0.05 … 1.05) fade props.</summary>
        bool OnScreen(Vector3 p)
        {
            var d = p - View.Pos;
            float z = Vector3.Dot(d, View.F);
            if (z <= View.Near) return false;
            float x = Vector3.Dot(d, View.R) / (z * View.TanHalf * View.Aspect), y = Vector3.Dot(d, View.U) / (z * View.TanHalf);
            return Mathf.Abs(x) <= 1.1f && Mathf.Abs(y) <= 1.1f;
        }

        /// <summary>
        /// As MapView.UpdateCuts with every hole fully open: a soft round cut-out around each unit the occluder hides and
        /// around the camera's focus (the look-at point) when it hides that; no cursor here. The renderers get _Cut0.._Cut3
        /// like MapView.ApplyCuts writes them (the Collector reads them back into the draw calls).
        /// </summary>
        void Cut(PObj o, Vector3 cp)
        {
            if (!o.occluder || o.occ == null || o.occludedAlpha >= 0.999f) return;
            var cuts = new List<Vector4>();
            foreach (var u in fadingUnits)
            {
                var c = u.CenterPosition;
                var h = u.HeadPosition;
                if (OnScreen(c) && o.occ.Hides(cp, View.R, c, h, false)) cuts.Add(PropOccluder.UnitCut(c, h, u.Model.Radius * u.Scale * 2f));
            }
            var focus = LookAt;
            if (o.occ.HidesPoint(cp, focus)) cuts.Add(PropOccluder.PointCut(focus));
            if (cuts.Count == 0) return;
            var arr = new Vector4[PropOccluder.MaxCuts];
            for (int i = 0; i < arr.Length && i < cuts.Count; i++) arr[i] = cuts[i];
            foreach (var r in o.model.Renderers)
            {
                if (r == null) continue;
                for (int i = 0; i < arr.Length; i++) r.Block.SetVector(PropOccluder.CutIds[i], arr[i]);
            }
        }

        // ================================================================== mood and lights (as MapView.ApplyMood / UpdateLights at t = 0)

        void ApplyMood(Camera cam)
        {
            DayNight.ApplyTo();
            var horizon = sky.Horizon;
            var fog = Color.Lerp(horizon, sky.Zenith, fogZenith);
            // golden hour: the low sun's glow hangs warm in the distance
            float gold = DayNight.Golden;
            fog = Color.Lerp(fog, DayNight.GoldenHaze, 0.3f * gold);
            SceneLighting.FogColor = fog;
            SceneLighting.FogStart = Mathf.Lerp(fogStart, fogStart * 0.7f, gold);
            SceneLighting.FogEnd = fogEnd;
            if (Indoor)
            {
                // as MapView: indoors the fog follows the zoom (the camera's distance from its look-at point)
                var f = DayNight.IndoorFog(Distance);
                SceneLighting.FogStart = f.x;
                SceneLighting.FogEnd = f.y;
            }
            SceneLighting.FogMax = 0.85f;
            SceneLighting.WindStrength = windStrength;
            cam.backgroundColor = horizon;
        }

        List<(Vector3 pos, Color color, float intensity, float range)> UpdateLights()
        {
            float night = DayNight.NightFactor;
            int glowWant = night > 0.42f ? 1 : 0;
            foreach (var g in nightGlows) g.model.SetLit?.Invoke(glowWant == 1);
            var result = new List<(Vector3, Color, float, float)>();
            foreach (var l in lights)
            {
                l.level = l.on ? 1f : 0f;
                float gate = l.level;
                if (l.nightOnly) gate *= Mathf.Clamp01((night - 0.2f) / 0.45f);
                l.setLit?.Invoke(night > 0.42f);
                float k = gate * Mathf.Lerp(0.4f, 1f, night);   // flicker averages to 1
                l.current = l.intensity * k;
                // as MapView: lamplight deepens to amber and reaches a little further at night
                if (l.current > 0.005f) result.Add((l.position, DayNight.LampColor(l.color, night), l.current, DayNight.LampRange(l.range, night)));
            }
            return result;
        }

        void BuildHalos()
        {
            float night = DayNight.NightFactor;
            var right = View.R;
            var up = View.U;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color32>(); var tris = new List<int>();
            foreach (var l in lights)
            {
                float a = 0f;
                var hc = DayNight.LampColor(l.color, night);
                if (l.current > 0.005f) a = Mathf.Clamp01(l.current) * Mathf.Lerp(0.16f, 0.42f, night);
                if (l.dormantGlow && l.level < 0.99f)
                {
                    float da = (0.05f + 0.04f * 0.5f) * (1f - l.level);
                    if (da > a) { a = da; hc = new Color(0.62f, 0.55f, 0.82f); }
                }
                if (a < 0.004f) continue;
                float s = l.haloSize;
                int b = verts.Count;
                var c = l.position;
                verts.Add(c - right * s - up * s); verts.Add(c - right * s + up * s); verts.Add(c + right * s + up * s); verts.Add(c + right * s - up * s);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                var col = new Color32((byte)(hc.r * 255f), (byte)(hc.g * 255f), (byte)(hc.b * 255f), (byte)(a * 255f));
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            if (verts.Count == 0) return;
            var m = new Mesh { name = "halos" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetColors(cols); m.SetTriangles(tris, 0, true);
            var go = new GameObject("Light Halos");
            go.transform.SetParent(Root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials3D.AdditiveFor(WorldTextures.Glow);
        }
    }
}
