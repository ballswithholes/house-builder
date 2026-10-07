// Builds a complete 3D map from a MapDef and tears it down cleanly (World layer; see Docs/ThreeD.md §6).
//
//   sky dome, sun / moon / stars (MapSky), time of day (DayNight → SceneLighting: sun, ambient, fog, night glow)
//   terrain: flat walkable ground at z = 0 painted with the map's ground texture, hills behind, banks in front,
//            ground cover, a brook under the bridge, the painted decals: trails as soft ribbons (MapTerrain)
//   backdrop from def.layers: mountains, clouds, distant village, forests, shrine cliffs (MapBackdrop)
//   props and foreground (PropModels), chests (lid opens), transition waymarkers (Waymarker), regions (rects), prop
//   lights (SceneLighting point lights with flicker / night-only), ambient particles; tall props get a soft round
//   cut-out where they hide a unit, the hovered object, the cursor or the camera's focus (PropOccluder)
//
// Static props carry no scripts: this component's single LateUpdate drives the mood, sky, clouds, light flicker and
// halos, occluder fades, highlights, chest lids, markers and particles, allocation-free.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    public enum MapObjectKind { Prop, Chest, Transition, Region }

    /// <summary>An interactable/area of the map with its ground rect (for picking and UI labels).</summary>
    public sealed class MapObject
    {
        public string Id = "";
        public MapObjectKind Kind;
        /// <summary>Prop/chest pivot (feet) or transition/region centre (ground point).</summary>
        public Vector2 Position;
        /// <summary>Ground rect: the model's footprint for props/chests, the authored area for transitions/regions.</summary>
        public Rect Rect;
        /// <summary>Where the UI should draw a label/prompt: a world point just above the object (z = −height).</summary>
        public Vector3 LabelPosition;
        public string Label = "";
        public PropDef Prop;
        public ChestDef Chest;
        public TransitionDef Transition;
        public RegionDef Region;
        public bool Visible = true;
        public bool Opened;
        public bool Locked;
        public bool Highlighted;
        public bool IsLantern;
        public bool LanternLit;

        // ---- presentation internals
        internal Transform root;            // holder at the ground point (identity rotation, scale 1)
        internal PropModel model;
        internal string art = "";
        internal int seed;
        internal float scale = 1f;
        internal bool flip;
        internal Color tint = Color.white;
        internal float windScale = 1f;
        internal Bounds bounds;             // world AABB of the model (picking, occlusion, labels)
        internal bool hasBounds;
        internal readonly Materials3D.Look look = new Materials3D.Look();
        internal bool occluder;
        internal float fade = 1f, fadeTarget = 1f, occludedAlpha = 0.35f;
        internal bool lookDirty;
        internal WorldLight light;
        internal Vector3 lampPoint;
        internal Quaternion lidRest = Quaternion.identity;
        internal float lid, lidFrom, lidTo, lidTime = -1f;
        internal float pop;
        internal bool groundShadow;
        internal Marker marker;
        internal PropOccluder occ;          // occluders: the model's voxelised surface (fade test)
        internal OccluderCuts cuts;         // occluders: the cut-outs around what they hide (eased in/out)
    }

    /// <summary>
    /// The cut-outs of one occluder (MapView.UpdateFades): a few slots, each following what the prop hides (a unit, the
    /// hovered object, the cursor, the camera's focus) and easing its hole in and out; the four strongest go to the
    /// renderers' _Cut0 … _Cut3.
    /// </summary>
    internal sealed class OccluderCuts
    {
        public const int Slots = 8;
        public readonly object[] key = new object[Slots];
        public readonly Vector4[] sphere = new Vector4[Slots];
        public readonly float[] weight = new float[Slots];
        public readonly bool[] on = new bool[Slots];
        public readonly bool[] seen = new bool[Slots];
        public readonly Vector4[] applied = new Vector4[PropOccluder.MaxCuts];
        public bool any;

        public int Find(object k)
        {
            for (int i = 0; i < Slots; i++) if (key[i] != null && ReferenceEquals(key[i], k)) return i;
            return -1;
        }

        public int Alloc(object k)
        {
            int best = -1;
            float bw = float.MaxValue;
            for (int i = 0; i < Slots; i++)
            {
                if (key[i] == null) { best = i; break; }
                if (!on[i] && weight[i] < bw) { bw = weight[i]; best = i; }
            }
            if (best < 0) return -1;
            key[best] = k;
            weight[best] = 0f;
            on[best] = false;
            return best;
        }

        public void Clear()
        {
            System.Array.Clear(key, 0, Slots);
            System.Array.Clear(weight, 0, Slots);
            System.Array.Clear(on, 0, Slots);
        }
    }

    /// <summary>A prop's point light (lantern flame, lamp, fire, window glow) driven by the time of day.</summary>
    internal sealed class WorldLight
    {
        public SceneLighting.PointLight handle;
        public Vector3 position;
        public Color color;
        public float intensity, range, haloSize;
        public bool flicker, nightOnly, on = true, visible = true;
        public float level = 1f;            // fades towards on (lanterns rekindling)
        public float boost = 1f;            // extra multiplier (hovered waymarkers)
        public float seed;
        public Action<bool> setLit;         // night-only lamps / windows: the model's glowing parts follow the light
        public int litState = -1;
        public float current;               // effective intensity this frame (halos)
        public bool dormantGlow;            // a dark spirit lantern: faint violet halo while off
    }

    /// <summary>
    /// Transition waymarker (World/Props/Waymarker.cs): an arch (front / back / mid-map exits) or a pair of lantern posts
    /// (side exits), lanterns lit at night with warm lights, chevrons on the ground.
    /// </summary>
    internal sealed class Marker
    {
        public Waymarker model;
        public readonly Materials3D.Look archLook = new Materials3D.Look();
        public readonly Materials3D.Look lanternLook = new Materials3D.Look();
        public readonly List<WorldLight> lights = new List<WorldLight>();
        public Vector2 dir;                 // outward (towards the exit), zero for a mid-map marker
        public Vector2 center;
        public float size;
        public float glow;                  // smoothed highlight
        public float appliedGlow = -1f;
    }

    [DefaultExecutionOrder(1000)] // after CameraRig and the unit views, before SceneLightingDriver (2000)
    public sealed class MapView : MonoBehaviour
    {
        /// <summary>The most recently built (and not yet disposed) map.</summary>
        public static MapView Current { get; private set; }

        // ---- tunables (static so game flow / options can change them)
        /// <summary>2D-era parallax factor of foreground props (unused in 3D: real depth parallaxes).</summary>
        public static float ForegroundParallax = 1.15f;
        /// <summary>Dither level of foreground props while a unit stands behind them.</summary>
        public static float ForegroundFadeAlpha = 0.35f;
        /// <summary>Tall props (≥ 2.5 m) dither to this level when they hide a unit from the camera (with
        /// OccluderCutOuts off; 1 disables occluder handling altogether).</summary>
        public static float OccluderFadeAlpha = 0.35f;
        /// <summary>Occluders open a soft round hole around what they hide (units, the hovered object, the cursor's
        /// ground point, the camera's focus) instead of dissolving as a whole: the unit reads clearly and the tree keeps
        /// its mass. Off = the whole prop dithers to OccluderFadeAlpha / ForegroundFadeAlpha while it hides a unit.</summary>
        public static bool OccluderCutOuts = true;
        /// <summary>2D-era tunable (unused in 3D).</summary>
        public static float VerticalParallax = 0.4f;
        public static Color HighlightColor = new Color(1f, 0.88f, 0.52f, 1f);

        public MapDef Def { get; private set; }
        public DayNight DayNight { get; private set; }
        /// <summary>Ground rect the camera's look-at point may roam (x −2..W+2, y −2..D+2; also applied to CameraRig).</summary>
        public Rect Bounds { get; private set; }
        /// <summary>Colour of the distance haze (fog) right now.</summary>
        public Color HazeColor { get; private set; }

        public readonly List<MapObject> Objects = new List<MapObject>();
        public readonly List<MapObject> Chests = new List<MapObject>();
        public readonly List<MapObject> Transitions = new List<MapObject>();
        public readonly List<MapObject> Regions = new List<MapObject>();
        /// <summary>Props with an "interact" id (signs, shrines, lanterns, notice boards…).</summary>
        public readonly List<MapObject> Interactables = new List<MapObject>();
        readonly Dictionary<string, MapObject> byId = new Dictionary<string, MapObject>(StringComparer.Ordinal);

        Transform propsRoot, fgRoot, decalsRoot, markersRoot, fxRoot;
        MapTerrain terrain;
        MapBackdrop backdrop;
        MapSky sky;
        AmbientParticles particles;
        BillboardBatch halos, chevrons;
        readonly List<Mesh> ownedMeshes = new List<Mesh>();
        readonly List<WorldLight> lights = new List<WorldLight>();
        // glowing props without a map light (stall lamps, tent, windmill windows): they follow the night too
        readonly List<MapObject> nightGlows = new List<MapObject>();
        int nightGlowState = -1;
        readonly List<MapObject> props = new List<MapObject>();         // every placed prop model (incl. non-interactable)
        readonly List<MapObject> foreground = new List<MapObject>();
        readonly List<MapObject> occluders = new List<MapObject>();
        // voxelised meshes for the occluder fade test, shared by the props using the same mesh (this map only)
        readonly Dictionary<Mesh, PropOccluder.Grid> occluderGrids = new Dictionary<Mesh, PropOccluder.Grid>();
        readonly List<UnitView> fadeUnits = new List<UnitView>();
        readonly List<CutTarget> cutTargets = new List<CutTarget>();
        static readonly object CursorKey = new object(), FocusKey = new object();

        /// <summary>Something an occluder may hide: a unit (body samples), an object (its bounds) or a ground point.</summary>
        struct CutTarget
        {
            public object key;
            public int kind;                // 0 unit, 1 object, 2 ground point
            public Vector3 center, head;    // unit / object: body centre and top; point: the ground point
            public Vector4 sphere;          // the cut (PropOccluder.UnitCut / BoundsCut / PointCut)
        }
        readonly List<MapObject> animated = new List<MapObject>();      // highlights, lids, pops (drained when idle)
        MeshFilter shadowFilter;
        Mesh shadowMesh;
        bool shadowsDirty;
        Color skyTopColor, skyBottomColor;
        float fogStart = 30f, fogEnd = 150f, fogZenith = 0.22f, windStrength = 0.06f;
        bool disposed;

        static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();
        static readonly int GradeId = Shader.PropertyToID("_LV_Grade");
        static readonly int WarmthId = Shader.PropertyToID("_LV_Warmth");
        readonly List<Rect> postObstacles = new List<Rect>();

        // ================================================================== building

        /// <summary>
        /// Builds the map for def. flagTest (optional) evaluates requireFlag strings
        /// (e.g. FlagStore.Test): hidden chests and locked transitions.
        /// </summary>
        public static MapView Build(MapDef def, Func<string, bool> flagTest = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            var go = new GameObject("Map: " + (string.IsNullOrEmpty(def.id) ? "unnamed" : def.id));
            var view = go.AddComponent<MapView>();
            view.BuildAll(def, flagTest);
            return view;
        }

        void BuildAll(MapDef def, Func<string, bool> flagTest)
        {
            Def = def;
            Current = this;
            ArtLibrary.Init();
            SceneLighting.Ensure();
            // painted textures aren't CPU-readable: the terrain measures the path art through a GPU read-back
            if (MapTerrain.ReadPixels == null) MapTerrain.ReadPixels = ReadBack;
            DayNight = new DayNight(def.ambient);

            skyTopColor = Ui.Hex(string.IsNullOrEmpty(def.skyTop) ? "#9fd3f0" : def.skyTop);
            skyBottomColor = Ui.Hex(string.IsNullOrEmpty(def.skyBottom) ? "#fdf1d6" : def.skyBottom);
            var amb = def.ambient ?? new AmbientDef();
            // mist: a nearer haze, cooled towards the zenith (a dusk horizon alone would wash the scene sepia)
            if (amb.mist) { fogStart = 24f; fogEnd = 120f; fogZenith = 0.38f; }
            windStrength = amb.leaves ? 0.075f : amb.embers ? 0.045f : 0.06f;

            sky = new MapSky(transform, skyTopColor, skyBottomColor, MapTerrain.StableHash(def.id), ownedMeshes);
            terrain = new MapTerrain(def, transform, ownedMeshes);
            terrain.Build(TerrainMaterial(def), def.groundTile > 0f ? def.groundTile : 8f);
            backdrop = new MapBackdrop(def, terrain, transform, ownedMeshes);
            // scenery is decoration: a failure there must never cost the map
            Guard("backdrop", backdrop.Build);
            Guard("ground cover", () => terrain.BuildGroundCover(!backdrop.HasForest));
            Guard("brook", () => terrain.BuildStream(skyBottomColor));

            propsRoot = Child("Props");
            decalsRoot = Child("Decals");
            fgRoot = Child("Foreground");
            markersRoot = Child("Markers");
            fxRoot = Child("Glows");
            halos = new BillboardBatch("Light Halos", fxRoot, Materials3D.AdditiveFor(WorldTextures.Glow), 64, 20);
            chevrons = new BillboardBatch("Waymarker Chevrons", fxRoot, Materials3D.AdditiveFor(WorldTextures.Chevron), 48, 10);

            Guard("decals", () => terrain.BuildDecals(decalsRoot));
            for (int i = 0; i < def.props.Count; i++)
            {
                var p = def.props[i];
                if (p == null || string.IsNullOrEmpty(p.art) || IsDecal(p.art)) continue;
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
            foreach (var c in def.chests) if (c != null) Guard("chest " + c.id, () => BuildChest(c, flagTest));
            foreach (var t in def.transitions) if (t != null) Guard("transition " + t.id, () => BuildTransition(t, flagTest));
            foreach (var r in def.regions) if (r != null) BuildRegion(r);
            shadowsDirty = true;
            UpdateCliffLanterns(false);

            particles = new AmbientParticles(this, transform);

            Bounds = new Rect(-2f, -2f, Mathf.Max(1f, def.width) + 4f, Mathf.Max(1f, def.depth) + 4f);
            if (CameraRig.Instance != null) CameraRig.Instance.Bounds = Bounds;

            DayNight.MarkDirty();
            sky.MarkDirty();
            UpdateView(0f);
        }

        void Guard(string what, Action build)
        {
            try { build(); }
            catch (Exception e)
            {
                Debug.LogError($"[Lanternvale] Map '{Def.id}': building {what} failed: {e.Message}");
                Debug.LogException(e);
            }
        }

        Transform Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static int Seed(string mapId, Vector2 pos, int salt)
        {
            unchecked
            {
                int h = MapTerrain.StableHash(mapId) ^ (Mathf.RoundToInt(pos.x * 10f) * 73856093) ^ (Mathf.RoundToInt(pos.y * 10f) * 19349663) ^ (salt * 83492791);
                return (h & 0x7fffffff) % 100003;
            }
        }

        // ------------------------------------------------------------------ terrain material

        static readonly Dictionary<string, Material> TerrainMaterials = new Dictionary<string, Material>();
        static readonly Dictionary<Texture, Color> TextureAverages = new Dictionary<Texture, Color>();
        static readonly int SideTexId = Shader.PropertyToID("_SideTex");
        static readonly int SidePlanarScaleId = Shader.PropertyToID("_SidePlanarScale");
        static readonly int MainAvgId = Shader.PropertyToID("_MainAvg");
        static readonly int SideAvgId = Shader.PropertyToID("_SideAvg");
        static readonly int DetailTexId = Shader.PropertyToID("_DetailTex");
        static readonly int DetailPlanarScaleId = Shader.PropertyToID("_DetailPlanarScale");

        static Material TerrainMaterial(MapDef def)
        {
            // the ground and the surroundings by biome (Biomes: a biome's own ground replaces a placeholder key)
            string groundKey = Biomes.GroundKey(def);
            string sideKey = Biomes.SideKey(def);
            float tile = def.groundTile > 0f ? def.groundTile : 8f;
            var ground = ArtLibrary.Texture(groundKey);
            var side = ArtLibrary.Texture(sideKey);
            string key = groundKey + "|" + sideKey + "|" + tile.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                       + "|" + MapTerrain.StyleOf(def).Detail;
            if (TerrainMaterials.TryGetValue(key, out var m) && m != null) return m;
            var shader = Shader.Find("Lanternvale/Terrain");
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[Lanternvale] Shader 'Lanternvale/Terrain' is missing or unsupported; the terrain uses the plain textured material.");
                return Materials3D.Textured(ground, 1f / tile);
            }
            m = new Material(shader) { name = "LV Terrain " + groundKey, hideFlags = HideFlags.DontSave };
            m.SetTexture(Materials3D.MainTexId, ground);
            m.SetTexture(SideTexId, side);
            m.SetFloat(Materials3D.PlanarScaleId, 1f / tile);
            m.SetFloat(SidePlanarScaleId, Biomes.SidePlanarScale(def));
            m.SetColor(MainAvgId, Average(ground, groundKey));
            m.SetColor(SideAvgId, Average(side, sideKey));
            // raked gravel (shrine) / leaf litter (forest) over the ground where MapTerrain weights it in
            var detail = MapTerrain.DetailTexture(def, out float detailScale);
            if (detail != null)
            {
                m.SetTexture(DetailTexId, detail);
                m.SetFloat(DetailPlanarScaleId, detailScale);
            }
            TerrainMaterials[key] = m;
            return m;
        }

        /// <summary>Average colour of a painted texture (GPU downsample + readback once; known fallbacks without a GPU).</summary>
        static Color Average(Texture2D tex, string key)
        {
            Color fallback = Biomes.AverageFallback(key);
            if (tex == null) return fallback;
            if (TextureAverages.TryGetValue(tex, out var c)) return c;
            c = fallback;
            var px = ReadBack(tex, 16, 16);
            if (px != null && px.Length > 0)
            {
                float r = 0f, g = 0f, b = 0f;
                for (int i = 0; i < px.Length; i++) { r += px[i].r; g += px[i].g; b += px[i].b; }
                float k = 1f / (px.Length * 255f);
                var avg = new Color(r * k, g * k, b * k, 1f);
                if (avg.r + avg.g + avg.b > 0.05f) c = avg;
            }
            TextureAverages[tex] = c;
            return c;
        }

        /// <summary>MapTerrain.PixelReader: the texture's pixels at up to 256 × 128 (rows bottom-up), read back from the GPU.</summary>
        static Color32[] ReadBack(Texture2D tex, out int width, out int height)
        {
            width = tex != null ? Mathf.Clamp(tex.width, 1, 256) : 0;
            height = tex != null ? Mathf.Clamp(tex.height * width / Mathf.Max(1, tex.width), 1, 256) : 0;
            return ReadBack(tex, width, height);
        }

        /// <summary>A downsampled copy of a (non-readable) texture's pixels via a GPU blit; null without a GPU or on failure.</summary>
        static Color32[] ReadBack(Texture2D tex, int w, int h)
        {
            if (tex == null || w <= 0 || h <= 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            RenderTexture rt = null;
            Texture2D read = null;
            var prev = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                read.Apply(false);
                return read.GetPixels32();
            }
            catch (Exception) { return null; }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (read != null) Destroy(read);
            }
        }

        // ------------------------------------------------------------------ decals (built by MapTerrain.BuildDecals)

        static bool IsDecal(string art) => art.StartsWith("decal_", StringComparison.Ordinal);

        // ------------------------------------------------------------------ props

        PropModel PlaceModel(MapObject o, Transform holder)
        {
            var m = PropModels.Create(o.art, o.seed);
            var t = m.Root.transform;
            t.SetParent(holder, false);
            t.localPosition = Vector3.zero;
            t.localRotation = World3D.Upright;
            t.localScale = new Vector3(o.flip ? -o.scale : o.scale, o.scale, o.scale);
            if (m.SetLit != null && o.IsLantern) m.SetLit(o.LanternLit);
            else if (m.SetLit != null && (o.Prop == null || o.Prop.light == null))
            {
                if (!nightGlows.Contains(o)) nightGlows.Add(o);
                if (nightGlowState >= 0) m.SetLit(nightGlowState == 1);
            }
            o.model = m;
            o.lidRest = m.Lid != null ? m.Lid.localRotation : Quaternion.identity;
            ComputeBounds(o);
            o.lookDirty = true;
            ApplyLook(o);
            return m;
        }

        static void ComputeBounds(MapObject o)
        {
            o.hasBounds = false;
            var m = o.model;
            if (m == null) return;
            for (int i = 0; i < m.Renderers.Count; i++)
            {
                var r = m.Renderers[i];
                // disabled parts (an unlit glow, the other state of a switchable model) report stale bounds
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (!o.hasBounds) { o.bounds = r.bounds; o.hasBounds = true; }
                else o.bounds.Encapsulate(r.bounds);
            }
            if (!o.hasBounds && m.Root != null)
            {
                // no renderers listed: transform the model-space bounds
                var lb = m.LocalBounds;
                var tr = m.Root.transform;
                for (int i = 0; i < 8; i++)
                {
                    var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) != 0 ? 1f : -1f, (i & 2) != 0 ? 1f : -1f, (i & 4) != 0 ? 1f : -1f));
                    var w = tr.TransformPoint(c);
                    if (!o.hasBounds) { o.bounds = new Bounds(w, Vector3.zero); o.hasBounds = true; }
                    else o.bounds.Encapsulate(w);
                }
            }
        }

        /// <summary>World height of the top of an object's model.</summary>
        static float TopOf(MapObject o) => o.hasBounds ? Mathf.Max(0.3f, -o.bounds.min.z) : 1.5f;

        void SetFootprint(MapObject o)
        {
            if (o.hasBounds)
            {
                var b = o.bounds;
                o.Rect = Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
            }
            else o.Rect = new Rect(o.Position - new Vector2(0.5f, 0.5f), Vector2.one);
            o.LabelPosition = World3D.At(o.Position, TopOf(o) + 0.35f);
        }

        void BuildProp(PropDef p, int index)
        {
            string art = p.art;
            var holder = new GameObject(art).transform;
            holder.SetParent(propsRoot, false);
            holder.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);
            var o = new MapObject
            {
                Id = p.interact ?? "",
                Kind = MapObjectKind.Prop,
                Position = new Vector2(p.pos.x, p.pos.y),
                Prop = p,
                Label = p.text ?? "",
                root = holder,
                art = art,
                seed = Seed(Def.id, new Vector2(p.pos.x, p.pos.y), index),
                scale = p.scale > 0f ? p.scale : 1f,
                flip = p.flip,
                tint = string.IsNullOrEmpty(p.tint) ? Color.white : Ui.Hex(p.tint),
                windScale = p.sway ? 1f : 0.3f,
            };
            bool lantern = art.Contains("spirit_lantern");
            if (lantern)
            {
                o.IsLantern = true;
                o.LanternLit = !art.EndsWith("_dark", StringComparison.Ordinal);
            }
            PlaceModel(o, holder);
            SetFootprint(o);
            o.groundShadow = true;
            o.occluder = TopOf(o) >= 2.5f;
            o.occludedAlpha = OccluderFadeAlpha;
            if (o.occluder) { o.occ = PropOccluder.Build(o.model.Renderers, occluderGrids); occluders.Add(o); }

            if (p.light != null)
            {
                var l = AddLight(o, p.light);
                if (lantern) { l.on = o.LanternLit; l.level = l.on ? 1f : 0f; l.dormantGlow = true; l.nightOnly = false; }
            }
            else if (lantern)
            {
                var l = AddLight(o, DefaultLanternLight());
                l.on = o.LanternLit;
                l.level = l.on ? 1f : 0f;
                l.dormantGlow = true;
            }

            props.Add(o);
            Objects.Add(o);
            if (!string.IsNullOrEmpty(p.interact))
            {
                Interactables.Add(o);
                Register(o);
            }
        }

        void BuildForeground(PropDef p, int index)
        {
            var holder = new GameObject("FG " + p.art).transform;
            holder.SetParent(fgRoot, false);
            holder.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);
            var o = new MapObject
            {
                Kind = MapObjectKind.Prop,
                Position = new Vector2(p.pos.x, p.pos.y),
                Prop = p,
                root = holder,
                art = p.art,
                seed = Seed(Def.id, new Vector2(p.pos.x, p.pos.y), 1000 + index),
                scale = p.scale > 0f ? p.scale : 1f,
                flip = p.flip,
                tint = string.IsNullOrEmpty(p.tint) ? Color.white : Ui.Hex(p.tint),
                windScale = p.sway ? 1f : 0.3f,
            };
            PlaceModel(o, holder);
            SetFootprint(o);
            o.groundShadow = p.art.Contains("stone");
            o.occluder = TopOf(o) >= 0.7f;
            o.occludedAlpha = ForegroundFadeAlpha;
            if (o.occluder) { o.occ = PropOccluder.Build(o.model.Renderers, occluderGrids); occluders.Add(o); }
            foreground.Add(o);
        }

        static LightDef DefaultLanternLight() => new LightDef { color = "#ffe2a6", radius = 4.5f, intensity = 0.9f, offset = new Lanternvale.Util.Vec2(0f, -1f), flicker = true };

        /// <summary>
        /// A prop light: at the model's light anchor nearest to the authored offset (2.5D offsets: x along the map, y =
        /// height), else at the offset itself — pushed out in front of big props (windows, forges) so it lights the street.
        /// </summary>
        WorldLight AddLight(MapObject o, LightDef def)
        {
            var m = o.model;
            var tr = m.Root.transform;
            bool big = m.LocalBounds.size.x * o.scale > 2.4f;
            Vector3 world;
            float ox = def.offset.x, oy = def.offset.y;
            if (oy < 0f) oy = m.Height * o.scale * 0.62f;   // default lantern light: no authored height
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
                // model space: offset / scale (the root's scale re-applies it; flip mirrors x like the 2.5D art did)
                float front = big ? m.LocalBounds.min.z - 0.35f / o.scale : 0f;
                world = tr.TransformPoint(new Vector3(ox / o.scale, oy / o.scale, front));
            }
            var l = new WorldLight
            {
                position = world,
                color = string.IsNullOrEmpty(def.color) ? Ui.Hex("#ffd9a0") : Ui.Hex(def.color),
                intensity = Mathf.Max(0f, def.intensity),
                range = Mathf.Max(0.5f, def.radius) * 1.3f,
                flicker = def.flicker,
                nightOnly = def.nightOnly,
                seed = UnityEngine.Random.value * 100f,
            };
            l.haloSize = Mathf.Clamp(def.radius * 0.42f, 0.7f, 2.4f) * (big ? 1.25f : 1f);
            l.handle = SceneLighting.Add(world, l.color, 0f, l.range);
            l.handle.Enabled = false;
            if (!o.IsLantern && m.SetLit != null)
            {
                if (def.nightOnly) l.setLit = m.SetLit;   // lamps and windows follow the night (UpdateLights)
                else m.SetLit(true);                      // fires and forges always burn
            }
            o.light = l;
            o.lampPoint = world;
            lights.Add(l);
            return l;
        }

        // ------------------------------------------------------------------ chests, transitions, regions

        void BuildChest(ChestDef c, Func<string, bool> flagTest)
        {
            var holder = new GameObject("Chest " + c.id).transform;
            holder.SetParent(propsRoot, false);
            holder.localPosition = new Vector3(c.pos.x, c.pos.y, 0f);
            var o = new MapObject
            {
                Id = c.id ?? "",
                Kind = MapObjectKind.Chest,
                Position = new Vector2(c.pos.x, c.pos.y),
                Chest = c,
                root = holder,
                art = string.IsNullOrEmpty(c.art) ? "prop_chest" : c.art,
                seed = Seed(Def.id, new Vector2(c.pos.x, c.pos.y), 77),
                windScale = 0f,
            };
            PlaceModel(o, holder);
            SetFootprint(o);
            var e = o.hasBounds ? o.bounds.extents : new Vector3(0.5f, 0.35f, 0.4f);
            MeshCache.AddShadow(holder, e.x + 0.12f, e.y + 0.1f, 0.38f);
            o.Visible = string.IsNullOrEmpty(c.requireFlag) || flagTest == null || flagTest(c.requireFlag);
            o.Locked = c.lockCheck != null;
            holder.gameObject.SetActive(o.Visible);
            Chests.Add(o);
            Objects.Add(o);
            Register(o);
        }

        static string OpenArt(string closed)
        {
            if (PropModels.Has(closed + "_open")) return closed + "_open";
            return "prop_chest_open";
        }

        void BuildTransition(TransitionDef t, Func<string, bool> flagTest)
        {
            var size = new Vector2(t.size.x > 0f ? t.size.x : 2f, t.size.y > 0f ? t.size.y : 2f);
            var pos = new Vector2(t.pos.x, t.pos.y);
            var holder = new GameObject("Transition " + t.id).transform;
            holder.SetParent(markersRoot, false);
            var o = new MapObject
            {
                Id = t.id ?? "",
                Kind = MapObjectKind.Transition,
                Position = pos,
                Rect = new Rect(pos - size * 0.5f, size),
                Label = string.IsNullOrEmpty(t.label) ? t.targetMap : t.label,
                Transition = t,
                root = holder,
            };
            o.Locked = !string.IsNullOrEmpty(t.requireFlag) && flagTest != null && !flagTest(t.requireFlag);

            // outward direction: towards the nearest map edge (a ring of chevrons when it's in the middle of the map)
            float W = Def.width, D = Def.depth;
            float dl = pos.x, dr = W - pos.x, df = pos.y, db = D - pos.y;
            float min = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(df, db));
            var mk = new Marker { center = pos, size = Mathf.Max(size.x, size.y) };
            Vector2 archAt = pos;
            float span = 3f;
            if (min <= 3.5f)
            {
                if (min == dl) { mk.dir = new Vector2(-1f, 0f); archAt = new Vector2(-0.3f, pos.y); span = size.y; }
                else if (min == dr) { mk.dir = new Vector2(1f, 0f); archAt = new Vector2(W + 0.3f, pos.y); span = size.y; }
                else if (min == df) { mk.dir = new Vector2(0f, -1f); archAt = new Vector2(pos.x, -0.3f); span = size.x; }
                else { mk.dir = new Vector2(0f, 1f); archAt = new Vector2(pos.x, D + 0.3f); span = size.x; }
            }
            span = Mathf.Clamp(span * 0.72f, 2.4f, 4.6f);
            // a side exit's lantern posts stand clear of the trees and buildings beside the road
            if (Mathf.Abs(mk.dir.x) > 0.5f)
            {
                postObstacles.Clear();
                for (int i = 0; i < props.Count; i++)
                {
                    var po = props[i];
                    if (po.hasBounds && po.Visible) postObstacles.Add(Rect.MinMaxRect(po.bounds.min.x, po.bounds.min.y, po.bounds.max.x, po.bounds.max.y));
                }
                Waymarker.FitPosts(ref archAt, mk.dir, ref span, postObstacles, Mathf.Min(1.4f, size.y * 0.3f));
            }

            holder.localPosition = new Vector3(archAt.x, archAt.y, 0f);
            // the model: an arch facing the camera, or lantern posts at a side exit (an arch there is seen edge-on)
            var wm = Waymarker.Build(holder, mk.dir, span);
            for (int i = 0; i < wm.Frame.Count; i++) Quiet(wm.Frame[i]);
            for (int i = 0; i < wm.Lanterns.Count; i++) Quiet(wm.Lanterns[i]);
            mk.model = wm;

            // warm lanterns that light the road at night only (by day they'd paint a glowing disc on the ground)
            bool pair = wm.Lamps.Count > 1;
            for (int i = 0; i < wm.Lamps.Count; i++)
            {
                var lamp = wm.Lamps[i];
                var light = new WorldLight
                {
                    position = lamp,
                    range = pair ? 4.6f : 5.2f,
                    nightOnly = true,
                    seed = UnityEngine.Random.value * 100f,
                    haloSize = pair ? 1.0f : 1.3f,
                    setLit = i == 0 ? (Action<bool>)wm.SetLit : null,
                };
                light.handle = SceneLighting.Add(lamp, Color.white, 0f, light.range);
                light.handle.Enabled = false;
                lights.Add(light);
                mk.lights.Add(light);
            }
            o.light = mk.lights.Count > 0 ? mk.lights[0] : null;
            o.lampPoint = wm.Lamps.Count > 0 ? wm.Lamps[0] : World3D.At(archAt, 2f);
            o.marker = mk;
            ApplyTransitionColor(o);

            // picking / labels: the marker's projected bounds plus the authored ground rect
            o.bounds = wm.Bounds;
            o.hasBounds = true;
            o.LabelPosition = World3D.At(archAt, TopOf(o) + 0.45f);

            Transitions.Add(o);
            Objects.Add(o);
            Register(o);
        }

        static void ApplyTransitionColor(MapObject o)
        {
            var mk = o.marker;
            if (mk == null) return;
            // a pair of posts shares the arch's light between its two lanterns
            float share = mk.lights.Count > 1 ? 0.7f : 1f;
            for (int i = 0; i < mk.lights.Count; i++)
            {
                var l = mk.lights[i];
                l.color = o.Locked ? new Color(0.62f, 0.55f, 0.88f) : new Color(1f, 0.82f, 0.52f);
                l.intensity = (o.Locked ? 0.38f : 0.85f) * share;
                l.flicker = !o.Locked;
                if (l.handle != null) l.handle.Color = l.color;
            }
            mk.lanternLook.Tint = o.Locked ? new Color(0.5f, 0.46f, 0.72f) : Color.white;
            if (mk.model != null)
                for (int i = 0; i < mk.model.Lanterns.Count; i++) mk.lanternLook.Apply(mk.model.Lanterns[i]);
        }

        void BuildRegion(RegionDef r)
        {
            var size = new Vector2(r.size.x, r.size.y);
            var o = new MapObject
            {
                Id = r.id ?? "",
                Kind = MapObjectKind.Region,
                Position = new Vector2(r.pos.x, r.pos.y),
                Rect = new Rect(new Vector2(r.pos.x, r.pos.y) - size * 0.5f, size),
                Label = r.text ?? "",
                Region = r,
            };
            o.LabelPosition = World3D.At(o.Position, 0f);
            Regions.Add(o);
            Objects.Add(o);
            Register(o);
        }

        void Register(MapObject o)
        {
            if (string.IsNullOrEmpty(o.Id)) return;
            if (byId.ContainsKey(o.Id))
            {
                Debug.LogWarning($"[Lanternvale] Map '{Def.id}': duplicate object id '{o.Id}' ({o.Kind}); only the first is addressable.");
                return;
            }
            byId[o.Id] = o;
        }

        // ------------------------------------------------------------------ prop shadows (one merged mesh)

        void RebuildShadows()
        {
            shadowsDirty = false;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color32>();
            var tris = new List<int>();
            void Add(MapObject o)
            {
                if (!o.groundShadow || !o.Visible || !o.hasBounds) return;
                var e = o.bounds.extents;
                var c = o.bounds.center;
                float big = Mathf.Max(e.x, e.y);
                float rx = Mathf.Clamp(e.x * 0.95f + 0.12f, 0.25f, 7f), ry = Mathf.Clamp(e.y * 0.95f + 0.1f, 0.2f, 5f);
                float a = big > 2f ? 0.72f : 1f;   // big footprints (buildings, canopies) a little lighter
                const float z = -0.004f;
                int b = verts.Count;
                verts.Add(new Vector3(c.x - rx, c.y - ry, z)); verts.Add(new Vector3(c.x - rx, c.y + ry, z));
                verts.Add(new Vector3(c.x + rx, c.y + ry, z)); verts.Add(new Vector3(c.x + rx, c.y - ry, z));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                var col = new Color32(255, 255, 255, (byte)(a * 255f));
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            for (int i = 0; i < props.Count; i++) Add(props[i]);
            for (int i = 0; i < foreground.Count; i++) Add(foreground[i]);
            if (shadowFilter == null)
            {
                var go = new GameObject("Prop Shadows");
                go.transform.SetParent(propsRoot, false);
                shadowFilter = go.AddComponent<MeshFilter>();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = ShadowOverDecals;
                Quiet(r);
                shadowMesh = new Mesh { name = "lv_prop_shadows" };
                ownedMeshes.Add(shadowMesh);
                shadowFilter.sharedMesh = shadowMesh;
            }
            shadowMesh.Clear();
            shadowMesh.SetVertices(verts);
            shadowMesh.SetUVs(0, uvs);
            shadowMesh.SetColors(cols);
            shadowMesh.SetTriangles(tris, 0, true);
        }

        static Material shadowOverDecals;

        /// <summary>
        /// The blob-shadow material drawn after the painted decals (Lanternvale/Shadow's own queue, Geometry+20, comes
        /// before them, so a path would cover the shadow of a prop standing on it). One shared copy for all map shadows.
        /// </summary>
        static Material ShadowOverDecals
        {
            get
            {
                if (shadowOverDecals != null) return shadowOverDecals;
                shadowOverDecals = new Material(Materials3D.Shadow) { name = "LV Shadow (over decals)", hideFlags = HideFlags.DontSave };
                shadowOverDecals.renderQueue = 2955;   // LitTransparent decals are Transparent−50 (2950)
                return shadowOverDecals;
            }
        }

        // ================================================================== per-frame

        void LateUpdate()
        {
            if (disposed) return;
            UpdateView(Time.deltaTime);
        }

        void UpdateView(float dt)
        {
            var cam = PresentationHost.Cam;
            float time = Time.time;

            DayNight.Update(dt);
            sky.Update(cam, DayNight, time);
            ApplyMood(cam);
            backdrop.Update(dt, time, DayNight);
            terrain.Update(dt);
            UpdateLights(cam, time, dt);
            UpdateFades(cam, dt);
            UpdateAnimated(time, dt);
            UpdateMarkers(cam, time, dt);
            if (particles != null)
            {
                var rig = CameraRig.Instance;
                var look = rig != null ? rig.LookAtPoint : new Vector3(Def.width * 0.5f, Def.depth * 0.5f, 0f);
                particles.Update(dt, time, cam, look);
            }
            if (shadowsDirty) RebuildShadows();
        }

        void ApplyMood(Camera cam)
        {
            DayNight.ApplyTo();
            var horizon = sky.Horizon;
            var fog = Color.Lerp(horizon, sky.Zenith, fogZenith);
            // golden hour: the low sun's glow hangs warm in the distance
            float gold = DayNight.Golden;
            fog = Color.Lerp(fog, DayNight.GoldenHaze, 0.3f * gold);
            HazeColor = fog;
            SceneLighting.FogColor = fog;
            SceneLighting.FogStart = Mathf.Lerp(fogStart, fogStart * 0.7f, gold);
            SceneLighting.FogEnd = fogEnd;
            SceneLighting.FogMax = 0.85f;
            SceneLighting.WindStrength = windStrength;
            // night grade: moonlit colours cool towards blue-grey, lamplight keeps its warmth (LanternvaleCommon.cginc)
            var gt = DayNight.NightGradeTint;
            Shader.SetGlobalVector(GradeId, new Vector4(DayNight.NightGrade, gt.x, gt.y, gt.z));
            // warmth: richer sunlit colours at golden hour, amber lamp pools at night (LanternvaleCommon.cginc)
            Shader.SetGlobalVector(WarmthId, DayNight.Warmth);
            if (cam != null) cam.backgroundColor = horizon;
        }

        void UpdateLights(Camera cam, float time, float dt)
        {
            float night = DayNight.NightFactor;
            int glowWant = night > 0.42f ? 1 : 0;
            if (glowWant != nightGlowState)
            {
                nightGlowState = glowWant;
                for (int i = 0; i < nightGlows.Count; i++)
                {
                    var g = nightGlows[i];
                    if (g != null && g.model != null && g.model.SetLit != null && g.model.Root != null) g.model.SetLit(glowWant == 1);
                }
            }
            bool haveCam = cam != null;
            Vector3 right = Vector3.right, up = World3D.Up;
            if (haveCam) { right = cam.transform.right; up = cam.transform.up; }
            halos.Begin();
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                l.level = Mathf.MoveTowards(l.level, l.on ? 1f : 0f, dt * 1.4f);
                float f = 1f;
                if (l.flicker)
                    f = 1f + (Mathf.PerlinNoise(time * 5.3f + l.seed, l.seed) - 0.5f) * 0.24f + Mathf.Sin(time * 17.3f + l.seed) * 0.035f;
                float gate = l.visible ? l.level * l.boost : 0f;
                if (l.nightOnly) gate *= Mathf.Clamp01((night - 0.2f) / 0.45f);
                if (l.setLit != null)
                {
                    int want = night > 0.42f ? 1 : 0;
                    if (want != l.litState) { l.litState = want; l.setLit(want == 1); }
                }
                // lights read softly by day and carry the scene at night, deepening to amber pools (DayNight.LampColor)
                float k = gate * f * Mathf.Lerp(0.4f, 1f, night);
                l.current = l.intensity * k;
                var h = l.handle;
                bool on = l.current > 0.005f;
                var lc = DayNight.LampColor(l.color, night);
                if (h != null)
                {
                    if (h.Enabled != on) h.Enabled = on;
                    if (on)
                    {
                        h.Intensity = l.current;
                        h.Color = lc;
                        h.Range = DayNight.LampRange(l.range, night);
                    }
                }
                if (!haveCam || !l.visible) continue;
                float a = 0f;
                Color hc = lc;
                if (on) a = Mathf.Clamp01(l.current) * Mathf.Lerp(0.16f, 0.42f, night);
                if (l.dormantGlow && l.level < 0.99f)
                {
                    float breathe = 0.5f + 0.5f * Mathf.Sin(time * 1.1f + l.seed);
                    float da = (0.05f + 0.04f * breathe) * (1f - l.level);
                    if (da > a) { a = da; hc = new Color(0.62f, 0.55f, 0.82f); }
                }
                if (a < 0.004f) continue;
                float s = l.haloSize * (0.92f + 0.08f * f);
                halos.Add(l.position, right * s, up * s, new Color32((byte)(hc.r * 255f), (byte)(hc.g * 255f), (byte)(hc.b * 255f), (byte)(a * 255f)));
            }
            halos.End();
        }

        /// <summary>
        /// Tall props (and foreground) open a soft round cut-out where they really hide something the player needs to see:
        /// a unit that fades occluders (or is hovered), the hovered object, the cursor's ground point and the camera's
        /// focus. Camera rays to points spread over the unit's body are marched through the prop's voxelised surface
        /// (PropOccluder), so only a prop clearly between the camera and the unit counts — a tree behind a unit stays
        /// solid, a canopy in front gets a hole around the unit and keeps the rest of its mass. With OccluderCutOuts off
        /// the whole prop dithers instead (the 2.5D-era behaviour).
        /// </summary>
        void UpdateFades(Camera cam, float dt)
        {
            if (cam == null) return;
            var units = UnitView.All;
            var ct = cam.transform;
            var cp = ct.position;
            var right = ct.right;
            // the units in view (off-screen units must not fade the props at the screen's edge)
            fadeUnits.Clear();
            for (int u = 0; u < units.Count; u++)
            {
                var uv = units[u];
                if (uv == null || !uv.Visible || !(uv.FadesOccluders || uv.IsHovered)) continue;
                var vp = cam.WorldToViewportPoint(uv.CenterPosition);
                if (vp.z > cam.nearClipPlane && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f) fadeUnits.Add(uv);
            }
            if (OccluderCutOuts) { UpdateCuts(cp, right, dt); return; }
            for (int i = 0; i < occluders.Count; i++)
            {
                var o = occluders[i];
                if (!o.Visible || !o.hasBounds) continue;
                bool hides = false;
                if (o.occ != null && o.occludedAlpha < 0.999f)
                {
                    bool fadedNow = o.fadeTarget < 0.999f;
                    for (int u = 0; u < fadeUnits.Count && !hides; u++)
                        hides = fadeUnits[u].FadesOccluders && o.occ.Hides(cp, right, fadeUnits[u].CenterPosition, fadeUnits[u].HeadPosition, fadedNow);
                }
                o.fadeTarget = hides ? o.occludedAlpha : 1f;
                if (Mathf.Abs(o.fade - o.fadeTarget) < 0.001f) continue;
                o.fade = Mathf.MoveTowards(o.fade, o.fadeTarget, dt * 3f);
                o.lookDirty = true;
                ApplyLook(o);
            }
        }

        void UpdateCuts(Vector3 cp, Vector3 right, float dt)
        {
            // what may need seeing: the units in view, the hovered objects, the cursor's ground point, the camera's focus
            cutTargets.Clear();
            for (int u = 0; u < fadeUnits.Count; u++)
            {
                var uv = fadeUnits[u];
                var c = uv.CenterPosition;
                var h = uv.HeadPosition;
                cutTargets.Add(new CutTarget { key = uv, kind = 0, center = c, head = h, sphere = PropOccluder.UnitCut(c, h, uv.Bounds.width) });
            }
            for (int i = 0; i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (!o.Highlighted || !o.Visible || !o.hasBounds || o.occluder || o.Kind == MapObjectKind.Region) continue;
                var b = o.bounds;
                cutTargets.Add(new CutTarget
                {
                    key = o, kind = 1, center = b.center, head = new Vector3(b.center.x, b.center.y, b.min.z),
                    sphere = PropOccluder.BoundsCut(b),
                });
            }
            var rig = CameraRig.Instance;
            if (rig != null && rig.Cam != null)
            {
                var r = Bounds;
                if (!GameInput.PointerOverUi)
                {
                    var m = rig.MouseWorld;
                    if (r.Contains(m)) cutTargets.Add(new CutTarget { key = CursorKey, kind = 2, center = m, sphere = PropOccluder.PointCut(m) });
                }
                var f = rig.LookAtPoint;
                if (r.Contains(new Vector2(f.x, f.y))) cutTargets.Add(new CutTarget { key = FocusKey, kind = 2, center = f, sphere = PropOccluder.PointCut(f) });
            }

            float ease = dt * 4.5f;
            for (int i = 0; i < occluders.Count; i++)
            {
                var o = occluders[i];
                var cs = o.cuts;
                bool active = o.Visible && o.hasBounds && o.occ != null && o.occludedAlpha < 0.999f;
                if (!active)
                {
                    if (cs != null && cs.any) { cs.Clear(); ApplyCuts(o); }
                    continue;
                }
                if (cs == null) o.cuts = cs = new OccluderCuts();
                for (int k = 0; k < OccluderCuts.Slots; k++) cs.seen[k] = false;
                for (int t = 0; t < cutTargets.Count; t++)
                {
                    var tg = cutTargets[t];
                    int s = cs.Find(tg.key);
                    bool was = s >= 0 && cs.on[s];
                    bool hides = tg.kind == 2 ? o.occ.HidesPoint(cp, tg.center) : o.occ.Hides(cp, right, tg.center, tg.head, was);
                    if (hides && s < 0) s = cs.Alloc(tg.key);
                    if (s < 0) continue;
                    cs.seen[s] = true;
                    cs.on[s] = hides;
                    if (hides) cs.sphere[s] = tg.sphere;
                }
                bool any = false;
                for (int k = 0; k < OccluderCuts.Slots; k++)
                {
                    if (cs.key[k] == null) continue;
                    if (!cs.seen[k]) cs.on[k] = false;   // gone (hidden, despawned, the cursor left the map)
                    cs.weight[k] = Mathf.Clamp01(cs.weight[k] + (cs.on[k] ? ease : -ease));
                    if (cs.weight[k] <= 0f && !cs.on[k]) { cs.key[k] = null; continue; }
                    any = true;
                }
                if (any || cs.any) ApplyCuts(o);
            }
        }

        static readonly int[] CutOrder = new int[OccluderCuts.Slots];

        /// <summary>Writes the four strongest cut-outs of an occluder to its renderers (only when they changed).</summary>
        static void ApplyCuts(MapObject o)
        {
            var cs = o.cuts;
            var m = o.model;
            if (cs == null || m == null) return;
            int n = 0;
            for (int k = 0; k < OccluderCuts.Slots; k++)
            {
                if (cs.key[k] == null || cs.weight[k] <= 0f) continue;
                // insertion by weight (strongest first)
                int at = n++;
                while (at > 0 && cs.weight[CutOrder[at - 1]] < cs.weight[k]) { CutOrder[at] = CutOrder[at - 1]; at--; }
                CutOrder[at] = k;
            }
            bool changed = false;
            for (int c = 0; c < PropOccluder.MaxCuts; c++)
            {
                var v = Vector4.zero;
                if (c < n)
                {
                    int k = CutOrder[c];
                    float w = cs.weight[k];
                    v = cs.sphere[k];
                    v.w *= w * w * (3f - 2f * w);
                }
                if (v != cs.applied[c]) { cs.applied[c] = v; changed = true; }
            }
            cs.any = n > 0;
            if (!changed) return;
            for (int i = 0; i < m.Renderers.Count; i++)
            {
                var r = m.Renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(Mpb);
                for (int c = 0; c < PropOccluder.MaxCuts; c++) Mpb.SetVector(PropOccluder.CutIds[c], cs.applied[c]);
                r.SetPropertyBlock(Mpb);
            }
        }

        void UpdateAnimated(float time, float dt)
        {
            for (int i = animated.Count - 1; i >= 0; i--)
            {
                var o = animated[i];
                bool busy = false;
                if (o.Highlighted)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(time * 4f);
                    o.look.Rim = 0.35f + 0.3f * pulse;
                    o.look.Flash = new Color(1f, 0.95f, 0.8f, 0.04f + 0.04f * pulse);
                    o.look.OutlineColor = HighlightColor;
                    o.look.OutlineWidth = 3.2f + 0.6f * pulse;
                    o.lookDirty = true;
                    busy = true;
                }
                if (o.lidTime >= 0f && o.model != null && o.model.Lid != null)
                {
                    o.lidTime += dt;
                    const float dur = 0.55f;
                    float t = Mathf.Clamp01(o.lidTime / dur);
                    float e = o.lidTo > o.lidFrom ? EaseOutBack(t) : t * t * (3f - 2f * t);
                    o.lid = Mathf.LerpUnclamped(o.lidFrom, o.lidTo, e);
                    o.model.Lid.localRotation = o.lidRest * Quaternion.Euler(o.lid, 0f, 0f);
                    if (t >= 1f) o.lidTime = -1f;
                    else busy = true;
                }
                if (o.pop > 0f && o.root != null)
                {
                    o.pop = Mathf.Max(0f, o.pop - dt);
                    float t = 1f - o.pop / 0.4f;
                    float k = Mathf.Sin(t * Mathf.PI) * (1f - t) * 0.5f;
                    o.root.localScale = new Vector3(1f - k * 0.25f, 1f - k * 0.25f, 1f + k);
                    if (o.pop <= 0f) o.root.localScale = Vector3.one;
                    else busy = true;
                }
                if (o.lookDirty) ApplyLook(o);
                if (!busy) animated.RemoveAt(i);
            }
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.9f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        void UpdateMarkers(Camera cam, float time, float dt)
        {
            chevrons.Begin();
            for (int i = 0; i < Transitions.Count; i++)
            {
                var o = Transitions[i];
                var mk = o.marker;
                if (mk == null) continue;
                for (int l = 0; l < mk.lights.Count; l++) mk.lights[l].visible = o.Visible;
                if (!o.Visible) continue;
                mk.glow = Mathf.MoveTowards(mk.glow, o.Highlighted ? 1f : 0f, dt * 5f);
                float hl = mk.glow;
                for (int l = 0; l < mk.lights.Count; l++) mk.lights[l].boost = 1f + 0.45f * hl;
                if (Mathf.Abs(hl - mk.appliedGlow) > 0.001f)
                {
                    mk.appliedGlow = hl;
                    float s = 1f + 0.06f * hl;
                    o.root.localScale = new Vector3(s, s, s);
                    // arch look: gold outline when hovered
                    mk.archLook.Rim = 0.3f * hl;
                    mk.archLook.OutlineColor = Color.Lerp(Materials3D.Ink, HighlightColor, hl);
                    mk.archLook.OutlineWidth = 2.2f + 1.4f * hl;
                    var frame = mk.model.Frame;
                    for (int r = 0; r < frame.Count; r++) mk.archLook.Apply(frame[r]);
                }
                // chevrons flowing towards the exit
                var c = o.Locked ? new Color(0.62f, 0.56f, 0.85f) : new Color(1f, 0.84f, 0.52f);
                if (mk.dir != Vector2.zero)
                {
                    var dir = new Vector3(mk.dir.x, mk.dir.y, 0f);
                    var side = new Vector3(-mk.dir.y, mk.dir.x, 0f);
                    const int n = 3;
                    for (int k = 0; k < n; k++)
                    {
                        float phase = Mathf.Repeat(time * (o.Locked ? 0.35f : 0.8f) - k / (float)n, 1f);
                        float a = o.Locked ? 0.28f : (0.25f + 0.55f * Mathf.Sin(phase * Mathf.PI)) * 0.85f + 0.3f * hl;
                        var p = new Vector3(o.Position.x, o.Position.y, -0.03f) + dir * (-1.1f + k * 0.95f);
                        float sz = 0.55f + 0.08f * hl;
                        chevrons.Add(p, side * sz, dir * sz, new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(Mathf.Clamp01(a) * 255f)));
                    }
                }
                else
                {
                    const int n = 4;
                    for (int k = 0; k < n; k++)
                    {
                        float ang = time * 0.4f + k * Mathf.PI * 0.5f;
                        var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                        var side = new Vector3(-dir.y, dir.x, 0f);
                        var p = new Vector3(o.Position.x, o.Position.y, -0.03f) + dir * 0.9f;
                        float a = (o.Locked ? 0.25f : 0.55f) + 0.3f * hl;
                        chevrons.Add(p, side * 0.45f, dir * 0.45f, new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(Mathf.Clamp01(a) * 255f)));
                    }
                }
            }
            chevrons.End();
        }

        /// <summary>Pushes an object's look (tint, wind, fade, highlight rim/outline) to every renderer of its model,
        /// keeping any other per-renderer property the model set.</summary>
        static void ApplyLook(MapObject o)
        {
            o.lookDirty = false;
            var m = o.model;
            if (m == null) return;
            var look = o.look;
            look.Tint = o.tint;
            look.WindScale = o.windScale;
            look.Fade = o.fade;
            if (!o.Highlighted)
            {
                look.Rim = 0f;
                look.Flash = new Color(1f, 1f, 1f, 0f);
                look.OutlineColor = Materials3D.Ink;
                look.OutlineWidth = 2.2f;
            }
            for (int i = 0; i < m.Renderers.Count; i++)
            {
                var r = m.Renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(Mpb);
                Mpb.SetColor(Materials3D.TintId, look.Tint);
                Mpb.SetColor(Materials3D.FlashId, look.Flash);
                Mpb.SetFloat(Materials3D.FadeId, look.Fade);
                Mpb.SetFloat(Materials3D.RimId, look.Rim);
                Mpb.SetFloat(Materials3D.FogScaleId, look.FogScale);
                Mpb.SetFloat(Materials3D.WindScaleId, look.WindScale);
                Mpb.SetColor(Materials3D.OutlineColorId, look.OutlineColor);
                Mpb.SetFloat(Materials3D.OutlineWidthId, look.OutlineWidth);
                r.SetPropertyBlock(Mpb);
            }
        }

        // ================================================================== public API

        /// <summary>Object (prop with interact id, chest, transition or region) by id, or null.</summary>
        public MapObject Find(string id) => !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var o) ? o : null;

        /// <summary>
        /// The object under a SCREEN position (pixels, bottom-left origin): the front-most interactable prop/chest whose
        /// projected model bounds contain it, else a transition (its waymarker, or the ground rect under the cursor),
        /// else (includeRegions) a region.
        /// </summary>
        public MapObject PickScreen(Vector2 screen, bool includeRegions = false)
        {
            var rig = CameraRig.Instance;
            var cam = rig != null && rig.Cam != null ? rig.Cam : PresentationHost.Cam;
            if (cam == null) return null;
            MapObject best = null;
            float bestDepth = float.MaxValue;
            for (int i = 0; i < Interactables.Count; i++) best = Closer(cam, screen, Interactables[i], best, ref bestDepth);
            for (int i = 0; i < Chests.Count; i++) best = Closer(cam, screen, Chests[i], best, ref bestDepth);
            if (best != null) return best;
            var ground = rig != null ? rig.ScreenToWorld(screen) : GroundUnder(cam, screen);
            for (int i = 0; i < Transitions.Count; i++)
            {
                var t = Transitions[i];
                if (!t.Visible) continue;
                var r = t.Rect;
                r.xMin -= 0.3f; r.yMin -= 0.3f; r.xMax += 0.3f; r.yMax += 0.3f;
                if (r.Contains(ground) || ScreenHit(cam, t.bounds, screen, out _)) return t;
            }
            if (includeRegions)
                for (int i = 0; i < Regions.Count; i++)
                    if (Regions[i].Rect.Contains(ground)) return Regions[i];
            return null;
        }

        static MapObject Closer(Camera cam, Vector2 screen, MapObject o, MapObject best, ref float bestDepth)
        {
            if (!o.Visible || !o.hasBounds) return best;
            if (!ScreenHit(cam, o.bounds, screen, out float depth) || depth >= bestDepth) return best;
            bestDepth = depth;
            return o;
        }

        /// <summary>True when the screen point is inside the (slightly inset, at least ~30 px) screen rect of projected bounds.</summary>
        static bool ScreenHit(Camera cam, Bounds b, Vector2 screen, out float depth)
        {
            depth = 0f;
            var c = b.center;
            var e = b.extents;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3(c.x + ((i & 1) != 0 ? e.x : -e.x), c.y + ((i & 2) != 0 ? e.y : -e.y), c.z + ((i & 4) != 0 ? e.z : -e.z));
                var s = cam.WorldToScreenPoint(p);
                if (s.z <= 0.05f) return false;
                if (s.x < minX) minX = s.x;
                if (s.x > maxX) maxX = s.x;
                if (s.y < minY) minY = s.y;
                if (s.y > maxY) maxY = s.y;
            }
            float w = maxX - minX, h = maxY - minY;
            minX += w * 0.08f; maxX -= w * 0.08f;
            minY += h * 0.04f; maxY -= h * 0.06f;
            const float minSize = 30f;
            if (maxX - minX < minSize) { float m = (minX + maxX) * 0.5f; minX = m - minSize * 0.5f; maxX = m + minSize * 0.5f; }
            if (maxY - minY < minSize) { float m = (minY + maxY) * 0.5f; minY = m - minSize * 0.5f; maxY = m + minSize * 0.5f; }
            if (screen.x < minX || screen.x > maxX || screen.y < minY || screen.y > maxY) return false;
            depth = cam.WorldToScreenPoint(c).z;
            return true;
        }

        static Vector2 GroundUnder(Camera cam, Vector2 screen)
        {
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            if (Mathf.Abs(ray.direction.z) < 1e-5f) return new Vector2(ray.origin.x, ray.origin.y);
            float t = -ray.origin.z / ray.direction.z;
            var p = ray.origin + ray.direction * t;
            return new Vector2(p.x, p.y);
        }

        /// <summary>
        /// Ground-point version of PickScreen: interactable props/chests whose footprint contains the point (the one
        /// nearest the front wins), then transitions by ground rect, then (optionally) regions.
        /// </summary>
        public MapObject Pick(Vector2 world, bool includeRegions = false)
        {
            MapObject best = null;
            for (int i = 0; i < Interactables.Count; i++) best = Better(best, Interactables[i], world);
            for (int i = 0; i < Chests.Count; i++) best = Better(best, Chests[i], world);
            if (best != null) return best;
            for (int i = 0; i < Transitions.Count; i++)
                if (Transitions[i].Visible && Transitions[i].Rect.Contains(world)) return Transitions[i];
            if (includeRegions)
                for (int i = 0; i < Regions.Count; i++)
                    if (Regions[i].Rect.Contains(world)) return Regions[i];
            return null;
        }

        static MapObject Better(MapObject best, MapObject o, Vector2 p)
        {
            if (!o.Visible) return best;
            var r = o.Rect;
            r.xMin -= 0.4f; r.yMin -= 0.4f; r.xMax += 0.4f; r.yMax += 0.4f;
            if (!r.Contains(p)) return best;
            return best == null || o.Position.y < best.Position.y ? o : best;
        }

        /// <summary>Rim light + gold outline (pulsing) on a prop/chest; a brighter, slightly larger waymarker for a transition.</summary>
        public void SetHighlighted(string objectId, bool on)
        {
            var o = Find(objectId);
            if (o == null || o.Highlighted == on) return;
            o.Highlighted = on;
            if (o.Kind == MapObjectKind.Transition || o.model == null) return;
            o.lookDirty = true;
            if (!on) ApplyLook(o);
            else if (!animated.Contains(o)) animated.Add(o);
        }

        /// <summary>Clears every highlight.</summary>
        public void ClearHighlights()
        {
            for (int i = 0; i < Objects.Count; i++)
                if (Objects[i].Highlighted) SetHighlighted(Objects[i].Id, false);
        }

        /// <summary>Opens (or closes) a chest: the lid swings open with a little bounce; animate adds a pop + sparkles.</summary>
        public void SetChestOpen(string id, bool open = true, bool animate = true)
        {
            var o = Find(id);
            if (o == null || o.Kind != MapObjectKind.Chest || o.model == null) return;
            if (o.Opened == open && animate) return;
            o.Opened = open;
            const float openAngle = 105f;
            if (o.model.Lid != null)
            {
                float target = open ? openAngle : 0f;
                if (animate)
                {
                    o.lidFrom = o.lid;
                    o.lidTo = target;
                    o.lidTime = 0f;
                }
                else
                {
                    o.lid = target;
                    o.lidTime = -1f;
                    o.model.Lid.localRotation = o.lidRest * Quaternion.Euler(target, 0f, 0f);
                }
            }
            else
            {
                // no hinged lid: swap to the open / closed model
                string closed = string.IsNullOrEmpty(o.Chest.art) ? "prop_chest" : o.Chest.art;
                ReplaceModel(o, open ? OpenArt(closed) : closed);
            }
            if (animate && open)
            {
                o.pop = 0.4f;
                FxSystem.Sparkles(World3D.At(o.Position, TopOf(o) * 0.9f), new Color(1f, 0.86f, 0.5f), 12);
            }
            if (animate && !animated.Contains(o)) animated.Add(o);
        }

        void ReplaceModel(MapObject o, string art)
        {
            if (o.root == null) return;
            if (o.model != null && o.model.Root != null) Destroy(o.model.Root);
            o.art = art;
            PlaceModel(o, o.root);
            SetFootprint(o);
            if (o.occluder)
            {
                o.occ = PropOccluder.Build(o.model.Renderers, occluderGrids);
                if (o.cuts != null) System.Array.Clear(o.cuts.applied, 0, PropOccluder.MaxCuts);   // the new renderers start uncut
            }
            if (o.light != null && !o.IsLantern && o.model.SetLit != null)
            {
                if (o.light.nightOnly) { o.light.setLit = o.model.SetLit; o.light.litState = -1; }
                else o.model.SetLit(true);
            }
            if (o.Highlighted && !animated.Contains(o)) animated.Add(o);
            shadowsDirty = true;
        }

        /// <summary>Shows/hides a chest, prop or transition (e.g. after a flag changes).</summary>
        public void SetVisible(string id, bool visible)
        {
            var o = Find(id);
            if (o == null) return;
            if (o.Visible == visible) return;
            o.Visible = visible;
            if (o.root != null) o.root.gameObject.SetActive(visible);
            if (o.light != null) o.light.visible = visible;
            if (o.marker != null)
                for (int i = 0; i < o.marker.lights.Count; i++) o.marker.lights[i].visible = visible;
            if (o.groundShadow) shadowsDirty = true;
        }

        /// <summary>Locks/unlocks a transition (dim violet waymarker when locked) or a chest.</summary>
        public void SetLocked(string id, bool locked)
        {
            var o = Find(id);
            if (o == null) return;
            o.Locked = locked;
            if (o.Kind == MapObjectKind.Transition) ApplyTransitionColor(o);
        }

        /// <summary>Re-evaluates requireFlag on chests (visibility) and transitions (locked).</summary>
        public void RefreshFlags(Func<string, bool> flagTest)
        {
            if (flagTest == null) return;
            foreach (var c in Chests)
                if (!string.IsNullOrEmpty(c.Chest.requireFlag)) SetVisible(c.Id, flagTest(c.Chest.requireFlag));
            foreach (var t in Transitions)
                if (!string.IsNullOrEmpty(t.Transition.requireFlag)) SetLocked(t.Id, !flagTest(t.Transition.requireFlag));
        }

        /// <summary>Replaces the model of a prop (by interact id); highlight, lights and labels follow.</summary>
        public void SetPropArt(string id, string art)
        {
            var o = Find(id);
            if (o == null || o.model == null || string.IsNullOrEmpty(art) || o.Kind == MapObjectKind.Transition) return;
            ReplaceModel(o, art);
        }

        /// <summary>
        /// Rekindles (or darkens) a spirit lantern prop: its glass glows (PropModel.SetLit), its warm light fades in
        /// and, when animated, a holy burst with sparkles plays at the lamp.
        /// </summary>
        public void SetLanternLit(string id, bool lit, bool animate = true)
        {
            var o = Find(id);
            if (o == null || !o.IsLantern || o.LanternLit == lit) return;
            SetLanternLitInternal(o, lit, animate);
            UpdateCliffLanterns(animate);
        }

        void SetLanternLitInternal(MapObject o, bool lit, bool animate)
        {
            o.LanternLit = lit;
            if (o.model != null && o.model.SetLit != null) o.model.SetLit(lit);
            else
            {
                // no switchable glass: use the lit / dark model
                string baseArt = o.art.EndsWith("_dark", StringComparison.Ordinal) ? o.art.Substring(0, o.art.Length - 5) : o.art;
                ReplaceModel(o, lit ? baseArt : baseArt + "_dark");
            }
            if (o.light == null && lit)
            {
                var l = AddLight(o, DefaultLanternLight());
                l.level = animate ? 0f : 1f;
                l.dormantGlow = true;
            }
            if (o.light != null)
            {
                o.light.on = lit;
                if (!animate) o.light.level = lit ? 1f : 0f;
            }
            if (animate && lit)
            {
                var c = o.light != null ? o.light.position : World3D.At(o.Position, TopOf(o) * 0.6f);
                FxSystem.Impact(c, School.Holy);
                FxSystem.Sparkles(c, new Color(1f, 0.9f, 0.6f), 14);
                FxSystem.AuraPulse(o.Position, 2.2f, new Color(1f, 0.86f, 0.55f, 0.8f));
            }
        }

        /// <summary>
        /// Lights (or darkens) every spirit lantern on the map. animate=false (default) switches
        /// silently — use it on map load; pass true for the story moment.
        /// </summary>
        public void SetAllLanternsLit(bool lit, bool animate = false)
        {
            for (int i = 0; i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (!o.IsLantern || o.LanternLit == lit) continue;
                SetLanternLitInternal(o, lit, animate);
            }
            UpdateCliffLanterns(animate);
        }

        void UpdateCliffLanterns(bool animate)
        {
            if (backdrop == null || !backdrop.HasCliffLanterns) return;
            int total = 0, lit = 0;
            for (int i = 0; i < Objects.Count; i++)
                if (Objects[i].IsLantern) { total++; if (Objects[i].LanternLit) lit++; }
            backdrop.SetLanternFraction(total > 0 ? (float)lit / total : 0f, animate);
        }

        /// <summary>Spirit lanterns on this map (props using prop_spirit_lantern / _dark).</summary>
        public int LanternCount { get { int n = 0; foreach (var o in Objects) if (o.IsLantern) n++; return n; } }

        /// <summary>Ground rect of an object (empty rect if unknown).</summary>
        public Rect RectOf(string id) { var o = Find(id); return o != null ? o.Rect : new Rect(); }

        // ================================================================== teardown

        /// <summary>Destroys the map and everything it registered (lights, meshes, particles).</summary>
        public void Dispose()
        {
            if (disposed) return;
            Cleanup();
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        void Cleanup()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < lights.Count; i++)
                if (lights[i].handle != null) SceneLighting.Remove(lights[i].handle);
            lights.Clear();
            if (particles != null) particles.Dispose();
            if (halos != null) halos.Dispose();
            if (chevrons != null) chevrons.Dispose();
            for (int i = 0; i < ownedMeshes.Count; i++)
                if (ownedMeshes[i] != null) Destroy(ownedMeshes[i]);
            ownedMeshes.Clear();
            occluderGrids.Clear();
            if (Current == this)
            {
                Current = null;
                Shader.SetGlobalVector(GradeId, Vector4.zero);   // no night grade outside a map
                Shader.SetGlobalVector(WarmthId, Vector4.zero);  // nor warmth
            }
        }

        void OnDestroy() { Cleanup(); }
    }
}
