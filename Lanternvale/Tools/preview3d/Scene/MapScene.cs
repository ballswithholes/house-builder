// Builds a map exactly like MapView.BuildAll does (Scripts/Game/World/MapView.cs), minus the per-frame scripts:
// the real MapSky / MapTerrain / MapBackdrop / DayNight, props, foreground and chests from PropModels.Create with
// MapView's seeds, transforms, light placement and hour gating, painted decals, merged blob shadows, transition
// waymarkers, light halos, and the real unit models (Scene/UnitPoser: UnitModels + UnitAnimator, idling) for the
// party leader, NPCs and encounter enemies, placed and faced like GameFlow.Views does.
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
            public Vector2? At;
            public bool Units = true;
            public bool Halos = true;
            public string Spawn = "default";
            /// <summary>Model of the party leader at the spawn (a new game starts with the class picked at creation).</summary>
            public string Player = "char_warrior";
            /// <summary>Story flags treated as set (content behind requireFlag appears; "*" = every flag).</summary>
            public HashSet<string> Flags = new HashSet<string>();
            public bool Has(string flag) => string.IsNullOrEmpty(flag) || Flags.Contains("*") || Flags.Contains(flag);
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
        float fogStart = 30f, fogEnd = 150f, windStrength = 0.06f;

        public MapScene(GameDatabase db, MapDef def, Options options)
        {
            Db = db;
            Def = def;
            opt = options ?? new Options();
            Root = new GameObject("Map: " + def.id).transform;
        }

        // ================================================================== build (as MapView.BuildAll)

        public void Build(int width, int height)
        {
            var def = Def;
            ArtLibrary.Init();
            DayNight = new DayNight(def.ambient);
            if (opt.Hour.HasValue) DayNight.SetHour(opt.Hour.Value);

            var skyTop = Ui.Hex(string.IsNullOrEmpty(def.skyTop) ? "#9fd3f0" : def.skyTop);
            var skyBottom = Ui.Hex(string.IsNullOrEmpty(def.skyBottom) ? "#fdf1d6" : def.skyBottom);
            var amb = def.ambient ?? new AmbientDef();
            if (amb.mist) { fogStart = 22f; fogEnd = 115f; }
            windStrength = amb.leaves ? 0.075f : amb.embers ? 0.045f : 0.06f;

            sky = new MapSky(Root, skyTop, skyBottom, MapTerrain.StableHash(def.id), owned);
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

            Guard("decals", BuildDecals);
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
            sky.Update(cam, DayNight, 0f);
            ApplyMood(cam);
            backdrop.Update(0f, 0f, DayNight);
            terrain.Update(0f);
            UpdateFades();
            var lightList = UpdateLights();
            if (opt.Halos) BuildHalos();
            var focus = GroundFocus();
            Lighting = Lighting.FromScene(View.Pos, lightList, focus);
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
            string groundKey = string.IsNullOrEmpty(def.ground) ? "ground_meadow" : def.ground;
            const string sideKey = "ground_meadow";
            float tile = def.groundTile > 0f ? def.groundTile : 8f;
            var ground = ArtLibrary.Texture(groundKey);
            var side = ArtLibrary.Texture(sideKey);
            var m = new Material(Shader.Find("Lanternvale/Terrain")) { name = "LV Terrain " + groundKey };
            m.SetTexture(Materials3D.MainTexId, ground);
            m.SetTexture(Shader.PropertyToID("_SideTex"), side);
            m.SetFloat(Materials3D.PlanarScaleId, 1f / tile);
            m.SetFloat(Shader.PropertyToID("_SidePlanarScale"), 1f / 8f);
            m.SetColor(Shader.PropertyToID("_MainAvg"), Average(ground, groundKey));
            m.SetColor(Shader.PropertyToID("_SideAvg"), Average(side, sideKey));
            return m;
        }

        /// <summary>The texture's average sRGB colour (MapView reads it back from the GPU; same result).</summary>
        static Color Average(Texture2D tex, string key)
        {
            Color fallback = key.Contains("forest") ? new Color(0.36f, 0.38f, 0.25f) : key.Contains("shrine") ? new Color(0.66f, 0.65f, 0.6f)
                           : key.Contains("village") ? new Color(0.74f, 0.66f, 0.5f) : new Color(0.6f, 0.69f, 0.45f);
            if (tex == null || tex.Pixels == null || tex.width < 8) return fallback;
            double r = 0, g = 0, b = 0;
            foreach (var p in tex.Pixels) { r += p.r; g += p.g; b += p.b; }
            double k = 1.0 / (tex.Pixels.Length * 255.0);
            var c = new Color((float)(r * k), (float)(g * k), (float)(b * k), 1f);
            return c.r + c.g + c.b > 0.05f ? c : fallback;
        }

        // ------------------------------------------------------------------ decals (as MapView.BuildDecals)

        static int DecalOrder(string art) => art.Contains("blight") ? 3 : art.Contains("flower") ? 2 : art.Contains("stone") ? 1 : 0;

        void BuildDecals()
        {
            var byArt = new Dictionary<string, List<PropDef>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var p in Def.props)
            {
                if (p == null || string.IsNullOrEmpty(p.art) || !p.art.StartsWith("decal_", StringComparison.Ordinal)) continue;
                if (!byArt.TryGetValue(p.art, out var list)) { byArt[p.art] = list = new List<PropDef>(); order.Add(p.art); }
                list.Add(p);
            }
            foreach (var art in order)
            {
                var tex = ArtLibrary.Texture(art);
                float aspect = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 1f;
                float baseH = ArtLibrary.Height(art, 4f);
                int ord = DecalOrder(art);
                var list = byArt[art];
                var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color32>(); var tris = new List<int>();
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    float s = p.scale > 0f ? p.scale : 1f;
                    float h = baseH * s, w = h * aspect;
                    float z = -(0.006f + ord * 0.002f + (i % 8) * 0.0003f);
                    float cx = p.pos.x, cy = p.pos.y;
                    int b = verts.Count;
                    verts.Add(new Vector3(cx - w * 0.5f, cy - h * 0.5f, z)); verts.Add(new Vector3(cx - w * 0.5f, cy + h * 0.5f, z));
                    verts.Add(new Vector3(cx + w * 0.5f, cy + h * 0.5f, z)); verts.Add(new Vector3(cx + w * 0.5f, cy - h * 0.5f, z));
                    float u0 = p.flip ? 1f : 0f, u1 = p.flip ? 0f : 1f;
                    uvs.Add(new Vector2(u0, 0f)); uvs.Add(new Vector2(u0, 1f)); uvs.Add(new Vector2(u1, 1f)); uvs.Add(new Vector2(u1, 0f));
                    var tint = string.IsNullOrEmpty(p.tint) ? Color.white : Ui.Hex(p.tint);
                    var c = new Color(tint.r * 0.86f, tint.g * 0.86f, tint.b * 0.86f, tint.a);
                    for (int k = 0; k < 4; k++) { cols.Add(c); norms.Add(World3D.Up); }
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                }
                var m = new Mesh { name = "lv_decals_" + art };
                m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetColors(cols); m.SetTriangles(tris, 0, true);
                var go = new GameObject("Decals " + art);
                go.transform.SetParent(decalsRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                var mat = new Material(Materials3D.LitTransparent(tex));
                if (art.Contains("blight")) mat.SetFloat(Materials3D.EmissionId, 0.18f);
                r.sharedMaterial = mat;
                r.sortingOrder = -8 + ord;
            }
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

        void BuildProp(PropDef p, int index)
        {
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
            var size = new Vector2(t.size.x > 0f ? t.size.x : 2f, t.size.y > 0f ? t.size.y : 2f);
            var pos = new Vector2(t.pos.x, t.pos.y);
            bool locked = !opt.Has(t.requireFlag);
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
            var holder = new GameObject("Transition " + t.id).transform;
            holder.SetParent(markersRoot, false);
            holder.localPosition = new Vector3(archAt.x, archAt.y, 0f);
            var arch = new GameObject("Waymarker").transform;
            arch.SetParent(holder, false);
            arch.rotation = dir == Vector2.zero ? World3D.Upright : World3D.Facing(dir);
            arch.localScale = dir == Vector2.zero ? new Vector3(0.6f, 0.75f, 0.6f) : new Vector3(span / 3f, 1f, 1f);
            var archGo = new GameObject("Arch");
            archGo.transform.SetParent(arch, false);
            archGo.AddComponent<MeshFilter>().sharedMesh = MeshCache.Get("lv_waymarker_arch", BuildArchMesh);
            var ar = archGo.AddComponent<MeshRenderer>();
            ar.sharedMaterials = Materials3D.WithOutline();
            var lanGo = new GameObject("Lantern");
            lanGo.transform.SetParent(holder, false);
            lanGo.transform.localRotation = arch.localRotation;
            lanGo.transform.localScale = dir == Vector2.zero ? new Vector3(0.75f, 0.75f, 0.75f) : Vector3.one;
            lanGo.AddComponent<MeshFilter>().sharedMesh = MeshCache.Get("lv_waymarker_lantern", BuildArchLanternMesh);
            var lr = lanGo.AddComponent<MeshRenderer>();
            lr.sharedMaterial = Materials3D.LowPoly;
            if (locked) lr.Block.SetColor(Materials3D.TintId, new Color(0.5f, 0.46f, 0.72f));
            var ab = ar.bounds;
            MeshCache.AddShadow(holder, ab.extents.x + 0.25f, ab.extents.y + 0.25f, 0.3f);
            var lamp = arch.TransformPoint(new Vector3(0f, 2.02f, 0f));
            lights.Add(new WLight
            {
                position = lamp,
                color = locked ? new Color(0.62f, 0.55f, 0.88f) : new Color(1f, 0.82f, 0.52f),
                intensity = locked ? 0.38f : 0.85f,
                range = 5.2f,
                flicker = !locked,
                haloSize = 1.3f,
            });
        }

        // as MapView.BuildArchMesh / BuildArchLanternMesh
        static Mesh BuildArchMesh()
        {
            var mb = new MeshBuilder(17) { Jitter = 0.07f, AOStrength = 0.3f, AOHeight = 0.6f };
            var wood = Ui.Hex("#7a5a43");
            var dark = Ui.Hex("#4a3a30");
            var stone = Ui.Hex("#a39c94");
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = stone;
                mb.Cylinder(new Vector3(s * 1.5f, 0f, 0f), 0.24f, 0.2f, 0.32f, 6);
                mb.Color = wood;
                mb.Cylinder(new Vector3(s * 1.5f, 0.3f, 0f), 0.13f, 0.11f, 2.45f, 6);
            }
            mb.Color = dark;
            mb.Box(new Vector3(0f, 2.78f, 0f), new Vector3(3.9f, 0.18f, 0.3f));
            mb.Push().Translate(1.98f, 2.84f, 0f).Rotate(0f, 0f, 12f);
            mb.Box(Vector3.zero, new Vector3(0.3f, 0.14f, 0.3f));
            mb.Pop();
            mb.Push().Translate(-1.98f, 2.84f, 0f).Rotate(0f, 0f, -12f);
            mb.Box(Vector3.zero, new Vector3(0.3f, 0.14f, 0.3f));
            mb.Pop();
            mb.Color = wood;
            mb.Box(new Vector3(0f, 2.42f, 0f), new Vector3(3.2f, 0.13f, 0.18f));
            mb.Color = Ui.Hex("#d8c7a2");
            mb.Box(new Vector3(0f, 2.6f, 0f), new Vector3(0.5f, 0.3f, 0.06f));
            mb.Color = dark;
            mb.Segment(new Vector3(0f, 2.36f, 0f), new Vector3(0f, 2.22f, 0f), 0.02f, 0.02f, 4);
            return mb.ToMesh("lv_waymarker_arch");
        }

        static Mesh BuildArchLanternMesh()
        {
            var mb = new MeshBuilder(18) { Jitter = 0.04f };
            mb.Color = Ui.Hex("#4a3a30");
            mb.Box(new Vector3(0f, 2.21f, 0f), new Vector3(0.3f, 0.05f, 0.3f));
            mb.Box(new Vector3(0f, 1.83f, 0f), new Vector3(0.26f, 0.05f, 0.26f));
            mb.Color = new Color(1f, 0.84f, 0.55f);
            mb.Emission = 1f;
            mb.Box(new Vector3(0f, 2.02f, 0f), new Vector3(0.24f, 0.33f, 0.24f));
            return mb.ToMesh("lv_waymarker_lantern");
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

        void BuildUnits()
        {
            var spawn = SpawnPoint();
            int n = 0;
            // the party leader: UnitView.Create(class sprite, natural height), facing right
            AddUnit(opt.Player, 0f, spawn, 1, true, n++);
            foreach (var npc in Def.npcs)
            {
                if (npc == null || !opt.Has(npc.requireFlag)) continue;
                AddUnit(NpcSprite(npc.npc), 0f, new Vector2(npc.pos.x, npc.pos.y), npc.flip ? -1 : 1, false, n++);
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

        void AddUnit(string key, float height, Vector2 pos, int facing, bool fadesOccluders, int index)
        {
            float yaw = facing == 0 ? 90f + UnitPoser.FacingBias : UnitPoser.FacingYaw(facing);
            var u = new UnitPoser(key, height, unitsRoot, pos, yaw);
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
            CameraMath.Place(anchor, opt.At.HasValue, zoom, opt.Yaw, bounds, out var pos, out var rot, out var lookAt, out float pitch, out float dist);
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

        // ================================================================== occluder fades (as MapView.UpdateFades, settled)

        void UpdateFades()
        {
            var cp = View.Pos;
            foreach (var o in props) Fade(o, cp);
            foreach (var o in foreground) Fade(o, cp);
        }

        void Fade(PObj o, Vector3 cp)
        {
            if (!o.occluder || !o.hasBounds || o.occludedAlpha >= 0.999f) return;
            var b = o.bounds;
            var ext = b.extents;
            b.extents = new Vector3(ext.x * 0.82f, ext.y * 0.82f, ext.z);
            bool hides = false;
            foreach (var u in fadingUnits)
                if (Hides(b, cp, u.CenterPosition) || Hides(b, cp, u.HeadPosition)) { hides = true; break; }
            if (!hides) return;
            foreach (var r in o.model.Renderers) r?.Block.SetFloat(Materials3D.FadeId, o.occludedAlpha);
        }

        static bool Hides(Bounds b, Vector3 cam, Vector3 target)
        {
            var d = target - cam;
            float len = d.magnitude;
            if (len < 0.01f) return false;
            return IntersectRay(b, cam, d / len, out float t) && t < len - 0.35f;
        }

        /// <summary>Bounds.IntersectRay (slab test): distance to the box along the ray, 0 when starting inside.</summary>
        static bool IntersectRay(Bounds b, Vector3 o, Vector3 dir, out float t)
        {
            float tmin = 0f, tmax = float.PositiveInfinity;
            var mn = b.min; var mx = b.max;
            for (int i = 0; i < 3; i++)
            {
                float oi = o[i], di = dir[i];
                if (Mathf.Abs(di) < 1e-8f) { if (oi < mn[i] || oi > mx[i]) { t = 0f; return false; } continue; }
                float t1 = (mn[i] - oi) / di, t2 = (mx[i] - oi) / di;
                if (t1 > t2) { var tmp = t1; t1 = t2; t2 = tmp; }
                tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) { t = 0f; return false; }
            }
            t = tmin;
            return true;
        }

        // ================================================================== mood and lights (as MapView.ApplyMood / UpdateLights at t = 0)

        void ApplyMood(Camera cam)
        {
            DayNight.ApplyTo();
            var horizon = sky.Horizon;
            var fog = Color.Lerp(horizon, sky.Zenith, 0.22f);
            SceneLighting.FogColor = fog;
            SceneLighting.FogStart = fogStart;
            SceneLighting.FogEnd = fogEnd;
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
                if (l.current > 0.005f) result.Add((l.position, l.color, l.current, l.range));
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
                var hc = l.color;
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
