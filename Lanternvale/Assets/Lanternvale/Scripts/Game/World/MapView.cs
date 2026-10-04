// Builds a complete 2.5D diorama from a MapDef and tears it down cleanly.
//
//   sky gradient (camera-fixed, time-of-day tinted, stars + moon at night)
//   parallax layers (tiled horizontally, back → front)
//   ground (tiled) + feathered back edge + horizon haze, decals
//   props (y-sorted, shadows, sway, lights, spirit-lantern halos, highlight outlines)
//   chests, transition markers, regions (world rects for the game flow)
//   foreground props (extra parallax, fade when a unit is behind them)
//   day/night (URP global light or an unlit overlay) and ambient particles
//
// Static props have no Update: one LateUpdate here drives parallax, sky, fades, light flicker and
// marker pulses; SwayManager animates swaying props; AmbientParticles owns the particles.
using System;
using System.Collections.Generic;
using System.Reflection;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    public enum MapObjectKind { Prop, Chest, Transition, Region }

    /// <summary>An interactable/area of the diorama with its world rect (for picking and UI labels).</summary>
    public sealed class MapObject
    {
        public string Id = "";
        public MapObjectKind Kind;
        /// <summary>Prop/chest pivot (feet) or transition/region centre.</summary>
        public Vector2 Position;
        /// <summary>World rect: sprite bounds for props/chests, ground footprint for transitions/regions.</summary>
        public Rect Rect;
        /// <summary>Where the UI should draw a label/prompt (above the sprite or marker).</summary>
        public Vector2 LabelPosition;
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

        // presentation internals
        internal Transform root;
        internal SpriteRenderer sprite, outline, brighten, shadow, marker, markerGlow, halo;
        internal AnimatedLight light;
        internal int order;
        internal Color tint = Color.white;
        internal float scale = 1f;
        internal float pop;          // chest-open bounce timer
        internal float fade = 1f;    // occlusion fade (tall props)
        internal Color markerColor;
    }

    [DefaultExecutionOrder(1000)] // after CameraRig.LateUpdate
    public sealed class MapView : MonoBehaviour
    {
        /// <summary>The most recently built (and not yet disposed) map.</summary>
        public static MapView Current { get; private set; }

        // ---- tunables (static so game flow / options can change them)
        /// <summary>Parallax factor of foreground props (1 = world).</summary>
        public static float ForegroundParallax = 1.15f;
        /// <summary>Alpha of foreground props while a unit is behind them.</summary>
        public static float ForegroundFadeAlpha = 0.35f;
        /// <summary>Tall props (≥ 2.5 m) fade to this alpha when a unit stands behind them. 1 disables.</summary>
        public static float OccluderFadeAlpha = 0.5f;
        /// <summary>How strongly background layers sink when the camera looks towards the front.</summary>
        public static float VerticalParallax = 0.4f;
        public static Color HighlightColor = new Color(1f, 0.88f, 0.52f, 1f);

        public MapDef Def { get; private set; }
        public DayNight DayNight { get; private set; }
        /// <summary>Camera bounds: x 0..width, y −3..depth + layers (also applied to CameraRig on build).</summary>
        public Rect Bounds { get; private set; }
        public Color HazeColor { get; private set; }

        public readonly List<MapObject> Objects = new List<MapObject>();
        public readonly List<MapObject> Chests = new List<MapObject>();
        public readonly List<MapObject> Transitions = new List<MapObject>();
        public readonly List<MapObject> Regions = new List<MapObject>();
        /// <summary>Props with an "interact" id (signs, shrines, lanterns, notice boards…).</summary>
        public readonly List<MapObject> Interactables = new List<MapObject>();
        readonly Dictionary<string, MapObject> byId = new Dictionary<string, MapObject>(StringComparer.Ordinal);

        Transform layersRoot, groundRoot, propsRoot, fgRoot, skyRoot, markersRoot;
        SpriteRenderer skyBottom, skyTop, overlay, moon, moonGlow;
        readonly List<SpriteRenderer> stars = new List<SpriteRenderer>();
        readonly List<Vector2> starPos = new List<Vector2>();
        float starViewH = -1f;
        Light2DSetter globalLight;
        Component globalLightComp;
        readonly List<Behaviour> disabledGlobalLights = new List<Behaviour>();
        readonly List<AnimatedLight> lights = new List<AnimatedLight>();
        readonly List<MapObject> animated = new List<MapObject>(); // markers, halos, highlights, pops
        readonly List<MapObject> occluders = new List<MapObject>();
        AmbientParticles particles;
        Color skyTopColor, skyBottomColor;
        bool disposed;

        sealed class LayerView
        {
            public ParallaxLayerDef def;
            public Transform t;
            public SpriteRenderer sr;
            public float scale, spriteW, spriteH, scroll;
            public Vector2 pivot;
            public int lastN = -1;
            public bool loop;
        }

        sealed class FgView
        {
            public Transform root;
            public SpriteRenderer sr;
            public Vector2 pos;
            public Rect local; // sprite rect relative to the pivot
            public Color tint;
            public float alpha = 1f;
        }

        readonly List<LayerView> layers = new List<LayerView>();
        readonly List<FgView> foreground = new List<FgView>();

        // ================================================================== building

        /// <summary>
        /// Builds the diorama for def. flagTest (optional) evaluates requireFlag strings
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
            DayNight = new DayNight(def.ambient);

            skyRoot = Child("Sky");
            layersRoot = Child("Layers");
            groundRoot = Child("Ground");
            propsRoot = Child("Props");
            markersRoot = Child("Markers");
            fgRoot = Child("Foreground");

            skyTopColor = Ui.Hex(string.IsNullOrEmpty(def.skyTop) ? "#9fd3f0" : def.skyTop);
            skyBottomColor = Ui.Hex(string.IsNullOrEmpty(def.skyBottom) ? "#fdf1d6" : def.skyBottom);

            BuildSky();
            for (int i = 0; i < def.layers.Count; i++) BuildLayer(def.layers[i], i);
            BuildGround();
            for (int i = 0; i < def.props.Count; i++)
                if (def.props[i] != null && !string.IsNullOrEmpty(def.props[i].art)) BuildProp(def.props[i], i);
            for (int i = 0; i < def.foreground.Count; i++)
                if (def.foreground[i] != null && !string.IsNullOrEmpty(def.foreground[i].art)) BuildForeground(def.foreground[i], i);
            foreach (var c in def.chests) if (c != null) BuildChest(c, flagTest);
            foreach (var t in def.transitions) if (t != null) BuildTransition(t, flagTest);
            foreach (var r in def.regions) if (r != null) BuildRegion(r);

            BuildLighting();
            particles = gameObject.AddComponent<AmbientParticles>();
            particles.Configure(this);

            float top = def.depth + 4f;
            foreach (var l in layers) top = Mathf.Max(top, l.def.y + l.spriteH * l.scale);
            top = Mathf.Min(top, def.depth + 11f);
            Bounds = new Rect(0f, -3f, Mathf.Max(1f, def.width), top + 3f);
            if (CameraRig.Instance != null) CameraRig.Instance.Bounds = Bounds;

            DayNight.MarkDirty();
            UpdateView(0f);
        }

        Transform Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        // ------------------------------------------------------------------ sky

        void BuildSky()
        {
            skyBottom = PresentationArt.NewRenderer("Sky Bottom", skyRoot, PresentationArt.White, SortingOrders.Sky, true);
            skyTop = PresentationArt.NewRenderer("Sky Gradient", skyRoot, PresentationArt.GradientUp, SortingOrders.Sky + 1, true);
            var rng = new System.Random((Def.id ?? "").GetHashCode());
            for (int i = 0; i < 46; i++)
            {
                var s = PresentationArt.NewRenderer("Star", skyRoot, PresentationArt.SoftDot, SortingOrders.Sky + 2, true);
                float size = 0.07f + (float)rng.NextDouble() * 0.09f;
                s.transform.localScale = new Vector3(size, size, 1f);
                s.enabled = false;
                stars.Add(s);
                starPos.Add(new Vector2((float)rng.NextDouble() - 0.5f, 0.02f + (float)Math.Pow(rng.NextDouble(), 0.8) * 0.48f));
            }
            moonGlow = PresentationArt.NewRenderer("Moon Glow", skyRoot, PresentationArt.Glow, SortingOrders.Sky + 3, true);
            moonGlow.transform.localScale = new Vector3(3.2f, 3.2f, 1f);
            moon = PresentationArt.NewRenderer("Moon", skyRoot, PresentationArt.Moon, SortingOrders.Sky + 4, true);
            moon.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            moon.enabled = moonGlow.enabled = false;
            if (!Lighting2D.IsLit)
            {
                overlay = PresentationArt.NewRenderer("Night Overlay", skyRoot, PresentationArt.White, SortingOrders.NightOverlay);
                overlay.enabled = false;
            }
        }

        // ------------------------------------------------------------------ parallax layers

        void BuildLayer(ParallaxLayerDef l, int index)
        {
            if (l == null || string.IsNullOrEmpty(l.art)) return;
            var sprite = ArtLibrary.Sprite(l.art);
            var sr = PresentationArt.NewRenderer("Layer " + index + " " + l.art, layersRoot, sprite, SortingOrders.Background + index * 10);
            var lv = new LayerView
            {
                def = l,
                t = sr.transform,
                sr = sr,
                spriteW = Mathf.Max(0.01f, sprite.bounds.size.x),
                spriteH = Mathf.Max(0.01f, sprite.bounds.size.y),
                pivot = PresentationArt.PivotNorm(sprite),
                loop = l.loop,
            };
            lv.scale = l.height > 0f ? l.height / lv.spriteH : 1f;
            sr.transform.localScale = new Vector3(lv.scale, lv.scale, 1f);
            if (lv.loop)
            {
                sr.drawMode = SpriteDrawMode.Tiled;
                sr.tileMode = SpriteTileMode.Continuous;
            }
            if (!string.IsNullOrEmpty(l.tint)) sr.color = Ui.Hex(l.tint);
            layers.Add(lv);
        }

        // ------------------------------------------------------------------ ground

        static readonly Dictionary<string, Sprite> EdgeSprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Color> GroundAverages = new Dictionary<string, Color>();

        void BuildGround()
        {
            string key = string.IsNullOrEmpty(Def.ground) ? "ground_meadow" : Def.ground;
            var sprite = ArtLibrary.Sprite(key);
            float sw = Mathf.Max(0.01f, sprite.bounds.size.x), sh = Mathf.Max(0.01f, sprite.bounds.size.y);
            float tile = Def.groundTile > 0f ? Def.groundTile : 8f;
            float s = tile / sh;
            float tileW = sw * s, tileH = sh * s;
            const float margin = 6f, below = 7f;
            int nx = Mathf.Max(1, Mathf.CeilToInt((Def.width + margin * 2f) / tileW));
            int ny = Mathf.Max(1, Mathf.CeilToInt((Def.depth + below) / tileH));
            float left = -margin, topY = Def.depth;
            var tint = string.IsNullOrEmpty(Def.groundTint) ? Color.white : Ui.Hex(Def.groundTint);

            var body = PresentationArt.NewRenderer("Ground " + key, groundRoot, sprite, SortingOrders.Ground);
            body.drawMode = SpriteDrawMode.Tiled;
            body.tileMode = SpriteTileMode.Continuous;
            body.size = new Vector2(nx * sw, ny * sh);
            body.color = tint;
            var piv = PresentationArt.PivotNorm(sprite);
            body.transform.localScale = new Vector3(s, s, 1f);
            body.transform.localPosition = new Vector3(left + nx * tileW * piv.x, topY - ny * tileH + ny * tileH * piv.y, 0f);

            // feathered back edge: the ground continues past the horizon and dissolves into the layers
            var avg = GroundAverage(key, sprite);
            var edge = EdgeSprite(key, sprite);
            if (edge != null)
            {
                var er = PresentationArt.NewRenderer("Ground Edge", groundRoot, edge, SortingOrders.Ground + 10);
                er.drawMode = SpriteDrawMode.Tiled;
                er.tileMode = SpriteTileMode.Continuous;
                float ew = edge.bounds.size.x, eh = edge.bounds.size.y;
                er.size = new Vector2(nx * ew, eh);
                er.color = tint;
                float es = tileW / Mathf.Max(0.01f, ew);
                er.transform.localScale = new Vector3(es, es, 1f);
                var ep = PresentationArt.PivotNorm(edge);
                er.transform.localPosition = new Vector3(left + nx * tileW * ep.x, topY + eh * es * ep.y, 0f);
            }

            // horizon haze: a soft band of sky/ground colour hiding the seam between ground and layers
            HazeColor = Color.Lerp(skyBottomColor, new Color(avg.r * tint.r, avg.g * tint.g, avg.b * tint.b), 0.35f);
            var haze = PresentationArt.NewRenderer("Horizon Haze", groundRoot, PresentationArt.HazeBand, SortingOrders.Ground + 20);
            PresentationArt.SetSize(haze.transform, haze.sprite, Def.width + margin * 2f + 4f, 3.4f);
            haze.transform.localPosition = new Vector3(Def.width * 0.5f, Def.depth + 0.35f, 0f);
            haze.color = PresentationArt.WithAlpha(HazeColor, layers.Count > 0 ? 0.55f : 0.35f);
        }

        static Color GroundAverage(string key, Sprite sprite)
        {
            if (GroundAverages.TryGetValue(key, out var c)) return c;
            c = key.Contains("forest") ? new Color(0.42f, 0.55f, 0.38f) : key.Contains("shrine") ? new Color(0.55f, 0.6f, 0.52f)
              : key.Contains("village") ? new Color(0.7f, 0.66f, 0.5f) : new Color(0.62f, 0.74f, 0.48f);
            try
            {
                var px = TextureReadback.Read(sprite.texture, sprite.textureRect, 16, 16);
                if (px != null && px.Length > 0)
                {
                    float r = 0, g = 0, b = 0;
                    foreach (var p in px) { r += p.r; g += p.g; b += p.b; }
                    float n = px.Length * 255f;
                    c = new Color(r / n, g / n, b / n);
                }
            }
            catch (Exception) { }
            GroundAverages[key] = c;
            return c;
        }

        /// <summary>Bottom half of the ground texture with a noisy watercolour fade to transparent.</summary>
        static Sprite EdgeSprite(string key, Sprite ground)
        {
            if (EdgeSprites.TryGetValue(key, out var s)) return s;
            s = null;
            try
            {
                var tex = ground.texture;
                var rect = ground.textureRect;
                int w = Mathf.Clamp(Mathf.RoundToInt(rect.width), 16, 512);
                int h = Mathf.Max(8, Mathf.RoundToInt(w * (rect.height / rect.width) * 0.5f));
                var src = TextureReadback.Read(tex, new Rect(rect.x, rect.y, rect.width, rect.height * 0.5f), w, h);
                if (src != null)
                {
                    for (int y = 0; y < h; y++)
                    {
                        float v = (y + 0.5f) / h;
                        for (int x = 0; x < w; x++)
                        {
                            float u = (x + 0.5f) / w;
                            float n = 0.5f * Mathf.Sin(2f * Mathf.PI * (2f * u) + 1.3f) + 0.3f * Mathf.Sin(2f * Mathf.PI * (5f * u) + 0.4f)
                                    + 0.2f * Mathf.Sin(2f * Mathf.PI * (11f * u) + 2.2f);
                            float t = Mathf.Clamp01((v + n * 0.22f - 0.04f) / 0.86f);
                            float a = 1f - t * t * (3f - 2f * t);
                            var p = src[y * w + x];
                            p.a = (byte)(p.a * a);
                            src[y * w + x] = p;
                        }
                    }
                    var t2 = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "edge_" + key, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    t2.SetPixels32(src);
                    t2.Apply(false, true);
                    float ppu = w / Mathf.Max(0.01f, ground.bounds.size.x);
                    s = Sprite.Create(t2, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), ppu, 0, SpriteMeshType.FullRect);
                    s.name = "edge_" + key;
                }
            }
            catch (Exception) { s = null; }
            EdgeSprites[key] = s;
            return s;
        }

        // ------------------------------------------------------------------ props

        static bool IsDecal(string art) => art.StartsWith("decal_", StringComparison.Ordinal);

        void BuildProp(PropDef p, int index)
        {
            string art = p.art;
            bool decal = IsDecal(art);
            float scale = p.scale > 0f ? p.scale : 1f;
            var root = new GameObject(art).transform;
            root.SetParent(propsRoot, false);
            root.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);

            var sprite = ArtLibrary.Sprite(art);
            int order = decal ? SortingOrders.Decal + (index % 90) : SortingOrders.ForY(p.pos.y);
            var sr = PresentationArt.NewRenderer("Sprite", root, sprite, order);
            sr.transform.localScale = new Vector3(scale, scale, 1f);
            sr.flipX = p.flip;
            var tint = string.IsNullOrEmpty(p.tint) ? Color.white : Ui.Hex(p.tint);
            sr.color = tint;
            float h = sprite.bounds.size.y * scale;

            var obj = new MapObject
            {
                Id = p.interact ?? "",
                Kind = MapObjectKind.Prop,
                Position = new Vector2(p.pos.x, p.pos.y),
                Prop = p,
                Label = p.text ?? "",
                root = root,
                sprite = sr,
                order = order,
                tint = tint,
                scale = scale,
            };
            obj.Rect = SpriteRect(sprite, obj.Position, scale, p.flip);
            obj.LabelPosition = new Vector2(obj.Position.x, obj.Rect.yMax + 0.35f);

            if (!decal && ArtLibrary.CastsShadow(art)) obj.shadow = AddShadow(root, sprite.bounds.size.x * scale, h);
            if (p.sway) SwayManager.Get().Add(sr.transform, h, this, decal ? 0f : 1f);

            bool lantern = art.Contains("spirit_lantern");
            if (lantern)
            {
                obj.IsLantern = true;
                obj.LanternLit = !art.EndsWith("_dark", StringComparison.Ordinal);
                BuildLanternHalo(obj, h);
            }
            if (p.light != null)
                AddLight(obj, p.light, scale, p.flip);
            else if (lantern && obj.LanternLit)
                AddLight(obj, new LightDef { color = "#ffe2a6", radius = 4.5f, intensity = 0.9f, offset = new Lanternvale.Util.Vec2(0f, h * 0.68f / scale) }, scale, false);

            if (!decal && h >= 2.5f) occluders.Add(obj);
            Objects.Add(obj);
            if (!string.IsNullOrEmpty(p.interact))
            {
                Interactables.Add(obj);
                Register(obj);
            }
        }

        static Rect SpriteRect(Sprite s, Vector2 pos, float scale, bool flip)
        {
            var b = s.bounds;
            float minX = b.min.x * scale, maxX = b.max.x * scale;
            if (flip) { float t = minX; minX = -maxX; maxX = -t; }
            float w = maxX - minX;
            // painted sprites have transparent margins: tighten horizontally for picking
            minX += w * 0.12f; maxX -= w * 0.12f;
            return Rect.MinMaxRect(pos.x + minX, pos.y + b.min.y * scale, pos.x + maxX, pos.y + b.max.y * scale * 0.97f);
        }

        SpriteRenderer AddShadow(Transform parent, float spriteWidth, float height)
        {
            var sprite = PresentationArt.Fx("fx_shadow");
            var sh = PresentationArt.NewRenderer("Shadow", parent, sprite, SortingOrders.Shadow);
            float w = Mathf.Clamp(spriteWidth * 0.72f, 0.4f, 9f);
            float hh = Mathf.Clamp(w * 0.3f, 0.15f, Mathf.Max(0.5f, height * 0.25f));
            PresentationArt.SetSize(sh.transform, sprite, w, hh);
            sh.transform.localPosition = new Vector3(0f, hh * 0.08f, 0f);
            sh.color = new Color(0.16f, 0.12f, 0.22f, 0.3f);
            return sh;
        }

        void AddLight(MapObject obj, LightDef l, float scale, bool flip)
        {
            var off = new Vector3(l.offset.x * scale * (flip ? -1f : 1f), l.offset.y * scale, 0f);
            var color = string.IsNullOrEmpty(l.color) ? Ui.Hex("#ffd9a0") : Ui.Hex(l.color);
            var handle = Lighting2D.AddPointLight(obj.root.gameObject, off, color, Mathf.Max(0.5f, l.radius), Mathf.Max(0f, l.intensity));
            if (handle?.glow != null) PresentationArt.MakeUnlit(handle.glow);
            var al = new AnimatedLight(handle, l.flicker, l.nightOnly);
            obj.light = al;
            lights.Add(al);
        }

        void BuildLanternHalo(MapObject obj, float h)
        {
            int order = Lighting2D.IsLit ? SortingOrders.Glow : SortingOrders.NightOverlay + 90;
            obj.halo = PresentationArt.NewRenderer("Spirit Halo", obj.root, PresentationArt.Glow, order, true);
            obj.halo.transform.localPosition = new Vector3(0f, h * 0.66f, 0f);
            float d = Mathf.Max(1.6f, h * 1.15f);
            obj.halo.transform.localScale = new Vector3(d, d, 1f);
            animated.Add(obj);
        }

        // ------------------------------------------------------------------ foreground

        void BuildForeground(PropDef p, int index)
        {
            float scale = p.scale > 0f ? p.scale : 1f;
            var root = new GameObject("FG " + p.art).transform;
            root.SetParent(fgRoot, false);
            root.localPosition = new Vector3(p.pos.x, p.pos.y, 0f); // before sway registration (it culls by x)
            var sprite = ArtLibrary.Sprite(p.art);
            int order = SortingOrders.Foreground + Mathf.Clamp(Mathf.RoundToInt(-p.pos.y * 10f), -900, 900) * 2 + (index & 1);
            var sr = PresentationArt.NewRenderer("Sprite", root, sprite, order);
            sr.transform.localScale = new Vector3(scale, scale, 1f);
            sr.flipX = p.flip;
            var fv = new FgView
            {
                root = root,
                sr = sr,
                pos = new Vector2(p.pos.x, p.pos.y),
                tint = string.IsNullOrEmpty(p.tint) ? Color.white : Ui.Hex(p.tint),
            };
            var r = SpriteRect(sprite, Vector2.zero, scale, p.flip);
            fv.local = r;
            sr.color = fv.tint;
            if (p.sway) SwayManager.Get().Add(sr.transform, sprite.bounds.size.y * scale, this);
            foreground.Add(fv);
        }

        // ------------------------------------------------------------------ chests, transitions, regions

        void BuildChest(ChestDef c, Func<string, bool> flagTest)
        {
            string art = string.IsNullOrEmpty(c.art) ? "prop_chest" : c.art;
            var root = new GameObject("Chest " + c.id).transform;
            root.SetParent(propsRoot, false);
            root.localPosition = new Vector3(c.pos.x, c.pos.y, 0f);
            var sprite = ArtLibrary.Sprite(art);
            int order = SortingOrders.ForY(c.pos.y);
            var sr = PresentationArt.NewRenderer("Sprite", root, sprite, order);
            var obj = new MapObject
            {
                Id = c.id ?? "",
                Kind = MapObjectKind.Chest,
                Position = new Vector2(c.pos.x, c.pos.y),
                Chest = c,
                root = root,
                sprite = sr,
                order = order,
            };
            obj.Rect = SpriteRect(sprite, obj.Position, 1f, false);
            obj.LabelPosition = new Vector2(obj.Position.x, obj.Rect.yMax + 0.3f);
            obj.shadow = AddShadow(root, sprite.bounds.size.x, sprite.bounds.size.y);
            obj.Visible = string.IsNullOrEmpty(c.requireFlag) || flagTest == null || flagTest(c.requireFlag);
            obj.Locked = c.lockCheck != null;
            root.gameObject.SetActive(obj.Visible);
            Chests.Add(obj);
            Objects.Add(obj);
            Register(obj);
        }

        static string OpenArt(string closed)
        {
            if (ArtLibrary.Has(closed + "_open") || PresentationArt.HasReal(closed + "_open")) return closed + "_open";
            return "prop_chest_open";
        }

        void BuildTransition(TransitionDef t, Func<string, bool> flagTest)
        {
            var size = new Vector2(t.size.x > 0f ? t.size.x : 2f, t.size.y > 0f ? t.size.y : 2f);
            var pos = new Vector2(t.pos.x, t.pos.y);
            var root = new GameObject("Transition " + t.id).transform;
            root.SetParent(markersRoot, false);
            root.localPosition = new Vector3(pos.x, pos.y, 0f);

            // arrow towards the nearest map edge, or a rune circle when it's in the middle of the map
            float dl = pos.x, dr = Def.width - pos.x, df = pos.y, db = Def.depth - pos.y;
            float min = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(df, db));
            Sprite sprite;
            float angle = 0f;
            if (min <= 3.5f)
            {
                sprite = PresentationArt.Chevron;
                if (min == dl) angle = 90f; else if (min == dr) angle = -90f; else if (min == df) angle = 180f; else angle = 0f;
            }
            else sprite = PresentationArt.Fx("fx_rune_circle");

            var obj = new MapObject
            {
                Id = t.id ?? "",
                Kind = MapObjectKind.Transition,
                Position = pos,
                Rect = new Rect(pos - size * 0.5f, size),
                Label = string.IsNullOrEmpty(t.label) ? t.targetMap : t.label,
                Transition = t,
                root = root,
            };
            obj.LabelPosition = new Vector2(pos.x, pos.y + size.y * 0.5f + 0.7f);
            obj.Locked = !string.IsNullOrEmpty(t.requireFlag) && flagTest != null && !flagTest(t.requireFlag);

            obj.markerGlow = PresentationArt.NewRenderer("Glow", root, PresentationArt.Glow, SortingOrders.Shadow + 29, true);
            float g = Mathf.Clamp(Mathf.Max(size.x, size.y) * 1.3f, 1.8f, 4.5f);
            obj.markerGlow.transform.localScale = new Vector3(g, g * 0.6f, 1f);
            obj.marker = PresentationArt.NewRenderer("Marker", root, sprite, SortingOrders.Shadow + 30, true);
            float m = Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.7f, 0.9f, 1.8f);
            PresentationArt.SetSize(obj.marker.transform, sprite, m, m * (sprite == PresentationArt.Chevron ? 1f : 0.62f));
            obj.marker.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            ApplyTransitionColor(obj);
            Transitions.Add(obj);
            Objects.Add(obj);
            animated.Add(obj);
            Register(obj);
        }

        static void ApplyTransitionColor(MapObject o)
        {
            o.markerColor = o.Locked ? new Color(0.62f, 0.58f, 0.72f, 1f) : new Color(1f, 0.86f, 0.55f, 1f);
        }

        void BuildRegion(RegionDef r)
        {
            var size = new Vector2(r.size.x, r.size.y);
            var obj = new MapObject
            {
                Id = r.id ?? "",
                Kind = MapObjectKind.Region,
                Position = new Vector2(r.pos.x, r.pos.y),
                Rect = new Rect(new Vector2(r.pos.x, r.pos.y) - size * 0.5f, size),
                Label = r.text ?? "",
                Region = r,
            };
            obj.LabelPosition = obj.Position;
            Regions.Add(obj);
            Objects.Add(obj);
            Register(obj);
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

        // ------------------------------------------------------------------ lighting

        void BuildLighting()
        {
            if (Lighting2D.IsLit)
            {
                DisableSceneGlobalLights();
                globalLightComp = Lighting2D.CreateGlobalLight(transform, DayNight.AmbientColor, DayNight.AmbientIntensity);
                globalLight = new Light2DSetter(globalLightComp);
            }
        }

        /// <summary>A template scene may already contain a Global Light 2D; two would fight. Disable them while the map lives.</summary>
        void DisableSceneGlobalLights()
        {
            var type = Type.GetType("UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime")
                    ?? Type.GetType("UnityEngine.Experimental.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (type == null) return;
            var prop = type.GetProperty("lightType", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return;
            foreach (var o in Resources.FindObjectsOfTypeAll(type))
            {
                var b = o as Behaviour;
                if (b == null || !b.isActiveAndEnabled || !b.gameObject.scene.IsValid()) continue;
                object lt = null;
                try { lt = prop.GetValue(b, null); } catch (Exception) { }
                if (lt == null || lt.ToString() != "Global") continue;
                b.enabled = false;
                disabledGlobalLights.Add(b);
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
            Vector3 cp = cam != null ? cam.transform.position : new Vector3(Def.width * 0.5f, Def.depth * 0.5f, -20f);
            float halfH = cam != null ? cam.orthographicSize : 6.2f;
            float halfW = halfH * (cam != null ? cam.aspect : 16f / 9f);
            float time = Time.time;

            DayNight.Update(dt);
            float night = DayNight.NightFactor;

            // ---- sky
            var top = DayNight.SkyColor(skyTopColor, true);
            var bottom = DayNight.SkyColor(skyBottomColor, false);
            float vw = halfW * 2.3f, vh = halfH * 2.3f;
            skyRoot.position = new Vector3(cp.x, cp.y, 0f);
            PresentationArt.SetSize(skyBottom.transform, skyBottom.sprite, vw, vh);
            PresentationArt.SetSize(skyTop.transform, skyTop.sprite, vw, vh);
            skyBottom.color = bottom;
            skyTop.color = top;
            if (cam != null) cam.backgroundColor = bottom;
            UpdateNightSky(night, halfW, halfH, time);
            if (overlay != null)
            {
                var o = DayNight.Overlay;
                bool on = o.a > 0.004f;
                if (overlay.enabled != on) overlay.enabled = on;
                if (on)
                {
                    PresentationArt.SetSize(overlay.transform, overlay.sprite, vw, vh);
                    overlay.color = o;
                }
            }
            if (globalLight != null) globalLight.Set(DayNight.AmbientColor, DayNight.AmbientIntensity);

            // ---- parallax layers
            float camYRef = Def.depth - 1f;
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                float p = l.def.parallax;
                float tileW = l.spriteW * l.scale, tileH = l.spriteH * l.scale;
                if (Mathf.Abs(l.def.scrollSpeed) > 1e-5f)
                {
                    l.scroll += l.def.scrollSpeed * dt;
                    if (l.loop) l.scroll = Mathf.Repeat(l.scroll, tileW);
                }
                float originX = cp.x * (1f - p) + l.scroll;
                float sink = Mathf.Min(0f, cp.y - camYRef) * (1f - Mathf.Clamp01(p)) * VerticalParallax;
                float x;
                if (l.loop)
                {
                    float viewL = cp.x - halfW - 2f, viewR = cp.x + halfW + 2f;
                    float left = originX + Mathf.Floor((viewL - originX) / tileW) * tileW;
                    int n = Mathf.Max(1, Mathf.CeilToInt((viewR - left) / tileW));
                    if (n != l.lastN)
                    {
                        l.lastN = n;
                        l.sr.size = new Vector2(n * l.spriteW, l.spriteH);
                    }
                    x = left + n * tileW * l.pivot.x;
                }
                else x = originX + p * Def.width * 0.5f + (l.pivot.x - 0.5f) * tileW;
                l.t.localPosition = new Vector3(x, l.def.y + sink + l.pivot.y * tileH, 0f);
            }

            // ---- foreground parallax + fade when a unit is behind
            var units = UnitView.All;
            float fpar = ForegroundParallax - 1f;
            var fgMul = Lighting2D.IsLit ? Color.white : DayNight.OverlayMultiply;
            for (int i = 0; i < foreground.Count; i++)
            {
                var f = foreground[i];
                var pos = new Vector2(f.pos.x + (f.pos.x - cp.x) * fpar, f.pos.y + (f.pos.y - cp.y) * fpar);
                f.root.localPosition = new Vector3(pos.x, pos.y, 0f);
                var r = new Rect(f.local.x + pos.x, f.local.y + pos.y, f.local.width, f.local.height);
                bool covering = false;
                for (int u = 0; u < units.Count && !covering; u++)
                {
                    var uv = units[u];
                    if (uv == null || !uv.Visible) continue;
                    if (r.Overlaps(uv.Bounds)) covering = true;
                }
                float target = covering ? ForegroundFadeAlpha : 1f;
                f.alpha = Mathf.MoveTowards(f.alpha, target, dt * 3.5f);
                var c = f.tint * fgMul;
                c.a = f.tint.a * f.alpha;
                f.sr.color = c;
            }

            // ---- tall props fade when a unit stands behind them
            if (OccluderFadeAlpha < 0.999f)
            {
                for (int i = 0; i < occluders.Count; i++)
                {
                    var o = occluders[i];
                    if (o.sprite == null) continue;
                    var r = o.Rect;
                    r.xMin += r.width * 0.08f; r.xMax -= r.width * 0.08f;
                    r.yMin += r.height * 0.12f;
                    bool behind = false;
                    for (int u = 0; u < units.Count && !behind; u++)
                    {
                        var uv = units[u];
                        if (uv == null || !uv.Visible || !uv.FadesOccluders) continue;
                        var feet = uv.FeetPosition;
                        if (feet.y <= o.Position.y + 0.15f) continue;
                        if (r.Contains(uv.CenterPosition) || r.Contains(uv.HeadPosition)) behind = true;
                    }
                    float target = behind ? OccluderFadeAlpha : 1f;
                    if (Mathf.Abs(o.fade - target) < 0.001f) continue;
                    o.fade = Mathf.MoveTowards(o.fade, target, dt * 3f);
                    var c = o.tint;
                    c.a *= o.fade;
                    o.sprite.color = c;
                }
            }

            // ---- lights
            for (int i = 0; i < lights.Count; i++) lights[i].Apply(night, time);

            // ---- markers, halos, highlights, pops
            for (int i = 0; i < animated.Count; i++) Animate(animated[i], time, dt, night);
        }

        void UpdateNightSky(float night, float halfW, float halfH, float time)
        {
            bool on = night > 0.05f;
            if (moon.enabled != on) { moon.enabled = on; moonGlow.enabled = on; }
            for (int i = 0; i < stars.Count; i++) if (stars[i].enabled != on) stars[i].enabled = on;
            if (!on) return;
            float vh = halfH * 2f, vw = halfW * 2f;
            if (Mathf.Abs(vh - starViewH) > 0.01f)
            {
                starViewH = vh;
                for (int i = 0; i < stars.Count; i++)
                    stars[i].transform.localPosition = new Vector3(starPos[i].x * vw, starPos[i].y * vh, 0f);
                moon.transform.localPosition = new Vector3(vw * 0.3f, vh * 0.33f, 0f);
                moonGlow.transform.localPosition = moon.transform.localPosition;
            }
            float k = Mathf.Clamp01((night - 0.05f) / 0.6f);
            for (int i = 0; i < stars.Count; i++)
            {
                float tw = 0.55f + 0.45f * Mathf.Sin(time * (1.3f + (i % 7) * 0.37f) + i * 2.1f);
                stars[i].color = new Color(1f, 0.97f, 0.88f, k * tw * 0.9f);
            }
            moon.color = new Color(1f, 0.97f, 0.88f, k);
            moonGlow.color = new Color(0.85f, 0.88f, 1f, k * 0.35f);
        }

        void Animate(MapObject o, float time, float dt, float night)
        {
            if (o.Kind == MapObjectKind.Transition && o.marker != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 2.2f + o.Position.x);
                float hl = o.Highlighted ? 1f : 0f;
                float a = (o.Locked ? 0.35f : 0.55f + 0.25f * pulse) + hl * 0.25f;
                var c = o.markerColor;
                o.marker.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
                o.markerGlow.color = new Color(c.r, c.g, c.b, (o.Locked ? 0.12f : 0.22f + 0.12f * pulse) + hl * 0.2f);
                float s = 1f + hl * 0.12f + 0.04f * pulse;
                o.root.localScale = new Vector3(s, s, 1f);
            }
            if (o.halo != null)
            {
                float breathe = 0.5f + 0.5f * Mathf.Sin(time * 1.1f + o.Position.x * 0.7f);
                if (o.LanternLit)
                {
                    float a = Mathf.Lerp(0.16f, 0.42f, night) * (0.8f + 0.2f * breathe);
                    o.halo.color = new Color(1f, 0.88f, 0.6f, a);
                }
                else o.halo.color = new Color(0.6f, 0.54f, 0.78f, 0.07f + 0.05f * breathe);
            }
            if (o.outline != null && o.outline.enabled)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 4f);
                var hc = HighlightColor;
                o.outline.color = new Color(hc.r, hc.g, hc.b, (0.75f + 0.25f * pulse) * o.fade);
                if (o.brighten != null) o.brighten.color = new Color(1f, 1f, 0.92f, (0.1f + 0.06f * pulse) * o.fade);
            }
            if (o.pop > 0f && o.sprite != null)
            {
                o.pop = Mathf.Max(0f, o.pop - dt);
                float t = 1f - o.pop / 0.35f;
                float k = Mathf.Sin(t * Mathf.PI) * (1f - t) * 0.6f;
                o.sprite.transform.localScale = new Vector3(o.scale * (1f - k * 0.3f), o.scale * (1f + k), 1f);
                if (o.pop <= 0f) o.sprite.transform.localScale = new Vector3(o.scale, o.scale, 1f);
            }
        }

        // ================================================================== public API

        /// <summary>Object (prop with interact id, chest, transition or region) by id, or null.</summary>
        public MapObject Find(string id) => !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var o) ? o : null;

        /// <summary>
        /// Front-most visible interactable under a world point: props/chests by sprite rect (lowest y
        /// wins), then transitions by ground rect, then (optionally) regions.
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
            if (!o.Visible || !o.Rect.Contains(p)) return best;
            return best == null || o.Position.y < best.Position.y ? o : best;
        }

        /// <summary>Outline + brighten a prop/chest, or brighten a transition marker.</summary>
        public void SetHighlighted(string objectId, bool on)
        {
            var o = Find(objectId);
            if (o == null || o.Highlighted == on) return;
            o.Highlighted = on;
            if (o.Kind == MapObjectKind.Transition || o.sprite == null) return;
            if (on && o.outline == null) CreateHighlight(o);
            if (o.outline != null) o.outline.enabled = on;
            if (o.brighten != null) o.brighten.enabled = on;
            if (on && !animated.Contains(o)) animated.Add(o);
        }

        void CreateHighlight(MapObject o)
        {
            var sil = Silhouettes.Get(o.sprite.sprite, Mathf.Clamp(o.sprite.sprite.bounds.size.y * 0.012f, 0.035f, 0.09f) / Mathf.Max(0.1f, o.scale));
            if (sil == null) return;
            o.outline = PresentationArt.NewRenderer("Outline", o.sprite.transform, sil.Outline, o.order - 1, true);
            o.outline.flipX = o.sprite.flipX;
            o.brighten = PresentationArt.NewRenderer("Brighten", o.sprite.transform, sil.Fill, o.order + 1, true);
            o.brighten.flipX = o.sprite.flipX;
        }

        void RefreshHighlightSprites(MapObject o)
        {
            if (o.outline == null) return;
            var sil = Silhouettes.Get(o.sprite.sprite, Mathf.Clamp(o.sprite.sprite.bounds.size.y * 0.012f, 0.035f, 0.09f) / Mathf.Max(0.1f, o.scale));
            if (sil == null) return;
            o.outline.sprite = sil.Outline;
            if (o.brighten != null) o.brighten.sprite = sil.Fill;
        }

        /// <summary>Clears every highlight.</summary>
        public void ClearHighlights()
        {
            for (int i = 0; i < Objects.Count; i++)
                if (Objects[i].Highlighted) SetHighlighted(Objects[i].Id, false);
        }

        /// <summary>Swaps a chest to its open (or closed) art; animate adds a little pop + sparkles.</summary>
        public void SetChestOpen(string id, bool open = true, bool animate = true)
        {
            var o = Find(id);
            if (o == null || o.Kind != MapObjectKind.Chest || o.sprite == null) return;
            if (o.Opened == open && animate) return;
            o.Opened = open;
            string closed = string.IsNullOrEmpty(o.Chest.art) ? "prop_chest" : o.Chest.art;
            o.sprite.sprite = ArtLibrary.Sprite(open ? OpenArt(closed) : closed);
            RefreshHighlightSprites(o);
            if (animate && open)
            {
                o.pop = 0.35f;
                if (!animated.Contains(o)) animated.Add(o);
                FxSystem.Sparkles(o.Position + new Vector2(0f, 0.5f), new Color(1f, 0.86f, 0.5f), 10);
            }
        }

        /// <summary>Shows/hides a chest, prop or transition (e.g. after a flag changes).</summary>
        public void SetVisible(string id, bool visible)
        {
            var o = Find(id);
            if (o == null) return;
            o.Visible = visible;
            if (o.root != null) o.root.gameObject.SetActive(visible);
        }

        /// <summary>Locks/unlocks a transition (dim grey-violet marker when locked) or a chest.</summary>
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

        /// <summary>Replaces the art of a prop (by interact id).</summary>
        public void SetPropArt(string id, string art)
        {
            var o = Find(id);
            if (o == null || o.sprite == null || string.IsNullOrEmpty(art)) return;
            o.sprite.sprite = ArtLibrary.Sprite(art);
            RefreshHighlightSprites(o);
        }

        /// <summary>
        /// Rekindles (or darkens) a spirit lantern prop: swaps prop_spirit_lantern ↔ _dark, toggles its
        /// light and halo, and plays a holy burst when lit.
        /// </summary>
        public void SetLanternLit(string id, bool lit, bool animate = true)
        {
            var o = Find(id);
            if (o == null || !o.IsLantern || o.LanternLit == lit) return;
            o.LanternLit = lit;
            string art = o.Prop.art.EndsWith("_dark", StringComparison.Ordinal) ? o.Prop.art.Substring(0, o.Prop.art.Length - 5) : o.Prop.art;
            o.sprite.sprite = ArtLibrary.Sprite(lit ? art : art + "_dark");
            RefreshHighlightSprites(o);
            float h = o.sprite.sprite.bounds.size.y * o.scale;
            if (o.light == null && lit)
                AddLight(o, new LightDef { color = "#ffe2a6", radius = 4.5f, intensity = 0.9f, offset = new Lanternvale.Util.Vec2(0f, h * 0.68f / o.scale) }, o.scale, false);
            if (o.light != null) o.light.Enabled = lit;
            if (animate && lit)
            {
                var c = o.Position + new Vector2(0f, h * 0.66f);
                FxSystem.Burst(c, 1.6f, School.Holy);
                FxSystem.Sparkles(c, new Color(1f, 0.9f, 0.6f), 16);
            }
        }

        /// <summary>World rect of an object (empty rect if unknown).</summary>
        public Rect RectOf(string id) { var o = Find(id); return o != null ? o.Rect : new Rect(); }

        // ================================================================== teardown

        /// <summary>Destroys the diorama and everything it registered (sway entries, lights, particles).</summary>
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
            if (SwayManager.Instance != null) SwayManager.Instance.RemoveOwner(this);
            foreach (var b in disabledGlobalLights) if (b != null) b.enabled = true;
            disabledGlobalLights.Clear();
            if (Current == this) Current = null;
        }

        void OnDestroy() { Cleanup(); }
    }
}
