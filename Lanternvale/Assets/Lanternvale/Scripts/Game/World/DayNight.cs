// Time of day → lighting mood. Pure data/maths, driven by MapView every frame; MapView pushes the result into
// SceneLighting (sun, ambient, fog, night glow) — see ApplyTo.
//
//   dawn  peach and lavender, a low warm sun from the left
//   day   high, soft white sun (the map's own sky colours)
//   gold  late afternoon: warm highlights over a cool sky fill, so the greens stay alive
//   dusk  rose and amber, a low warm sun from the right, a cool violet-blue fill
//   night a crisp blue moon over a deep blue fill; lamps and windows pool warm amber light (LampColor / LampRange)
//
// Maps either use a fixed time (ambient.timeOfDay) or the shared world clock (ambient.dayNightCycle),
// which keeps running across maps through the static WorldHour.
using System;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class DayNight
    {
        /// <summary>Shared clock for maps with a day/night cycle (game flow may save/restore it).</summary>
        public static float WorldHour = 12.5f;
        /// <summary>Game hours per real second for cycling maps (default: one hour per real minute).</summary>
        public static float HoursPerSecond = 1f / 60f;
        /// <summary>Freeze the clock (dialogue, combat, menus).</summary>
        public static bool Paused;

        public bool Cycle { get; private set; }
        public float FixedHour { get; private set; } = 12.5f;
        /// <summary>Map ambient multiplier (ambient.ambientColor × ambientIntensity).</summary>
        public Color MapAmbient = Color.white;
        public float MapAmbientIntensity = 1f;

        float? overrideHour;

        /// <summary>Raised whenever the effective lighting was recomputed (once per Update with changes).</summary>
        public event Action<DayNight> Changed;

        // ---- current state (computed by Update)
        public float Hour { get; private set; } = 12.5f;
        public float NightFactor { get; private set; }
        /// <summary>Overall light colour × intensity (map ambient included); kept for 2D-era callers.</summary>
        public Color AmbientColor { get; private set; } = Color.white;
        public float AmbientIntensity { get; private set; } = 1f;
        public Color SkyTopTint { get; private set; } = Color.white;
        public Color SkyBottomTint { get; private set; } = Color.white;
        public float SkyBlend { get; private set; }
        /// <summary>2D-era unlit overlay (rgb + alpha); unused by the 3D world, kept for compatibility.</summary>
        public Color Overlay { get; private set; } = new Color(0, 0, 0, 0);

        // ---- 3D lighting (map ambient included)
        /// <summary>Direction from the ground TO the light (sun by day, moon by night), up = −Z.</summary>
        public Vector3 LightDirection { get; private set; } = new Vector3(-0.35f, -0.55f, -0.75f);
        public Color LightColor { get; private set; } = new Color(1f, 0.93f, 0.8f);
        public float LightIntensity { get; private set; } = 0.95f;
        public Color SkyAmbient { get; private set; } = new Color(0.62f, 0.68f, 0.82f);
        public Color GroundAmbient { get; private set; } = new Color(0.42f, 0.38f, 0.34f);
        public float SceneAmbientIntensity { get; private set; } = 0.75f;
        /// <summary>Emissive boost (windows, lantern glass, crystals) — 0 by day, ~1 at night.</summary>
        public float NightGlow { get; private set; }
        /// <summary>Where the sun / moon discs sit in the sky (unit directions from the viewer, up = −Z).</summary>
        public Vector3 SunSkyDirection { get; private set; } = new Vector3(0f, 0.8f, -0.6f);
        public Vector3 MoonSkyDirection { get; private set; } = new Vector3(0.45f, 0.75f, -0.5f);
        /// <summary>0..1 visibility of the sun disc / moon disc and stars.</summary>
        public float SunVisibility { get; private set; } = 1f;
        public float MoonVisibility { get; private set; }
        /// <summary>Warm glow colour of the sky around the sun (dawn/dusk), alpha = strength.</summary>
        public Color SunGlow { get; private set; } = new Color(1f, 0.8f, 0.6f, 0f);
        /// <summary>Approximate brightness of the lit scene 0..1 (for unlit sprites that must follow the mood).</summary>
        public float LightLevel { get; private set; } = 1f;

        public DayNight() { }

        public DayNight(Lanternvale.Data.AmbientDef ambient)
        {
            Configure(ambient);
        }

        public void Configure(Lanternvale.Data.AmbientDef ambient)
        {
            ambient = ambient ?? new Lanternvale.Data.AmbientDef();
            Cycle = ambient.dayNightCycle;
            FixedHour = HourOf(ambient.timeOfDay);
            MapAmbient = string.IsNullOrEmpty(ambient.ambientColor) ? Color.white : Ui.Hex(ambient.ambientColor);
            MapAmbientIntensity = ambient.ambientIntensity > 0f ? ambient.ambientIntensity : 1f;
            overrideHour = null;
            Evaluate(EffectiveHour());
        }

        /// <summary>"dawn" | "day" | "dusk" | "night" (also accepts a number of hours).</summary>
        public static float HourOf(string timeOfDay)
        {
            switch ((timeOfDay ?? "").Trim().ToLowerInvariant())
            {
                case "dawn": case "morning": return 7.0f;
                case "dusk": case "evening": case "sunset": return 18.7f;
                case "night": case "midnight": return 22.5f;
                case "day": case "noon": case "": return 12.5f;
                default:
                    return float.TryParse(timeOfDay, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)
                        ? Mathf.Repeat(h, 24f) : 12.5f;
            }
        }

        /// <summary>Phase name for an hour: dawn [5,8), day [8,17.5), dusk [17.5,20.5), night otherwise.</summary>
        public static string PhaseOf(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            if (hour >= 5f && hour < 8f) return "dawn";
            if (hour >= 8f && hour < 17.5f) return "day";
            if (hour >= 17.5f && hour < 20.5f) return "dusk";
            return "night";
        }

        public string Phase => PhaseOf(Hour);

        /// <summary>Sets the time of day. On cycling maps this moves the world clock; on fixed maps it overrides until the map is rebuilt.</summary>
        public void SetHour(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            if (Cycle) WorldHour = hour;
            else overrideHour = hour;
            Evaluate(hour);
        }

        /// <summary>Back to the map's authored time (fixed maps).</summary>
        public void ClearOverride() { overrideHour = null; }

        /// <summary>True when the map shows its authored time (fixed map, no override): its authored sky is kept.</summary>
        public bool UsesAuthoredSky => !Cycle && overrideHour == null;

        float EffectiveHour() => overrideHour ?? (Cycle ? WorldHour : FixedHour);

        /// <summary>Advances the clock (cycling maps) and recomputes the lighting.</summary>
        public void Update(float dt)
        {
            if (Cycle && !Paused && overrideHour == null && HoursPerSecond > 0f)
                WorldHour = Mathf.Repeat(WorldHour + dt * HoursPerSecond, 24f);
            float h = EffectiveHour();
            if (Mathf.Abs(Mathf.DeltaAngle(h * 15f, Hour * 15f)) > 1e-4f || dirty)
            {
                dirty = false;
                Evaluate(h);
            }
        }

        bool dirty = true;
        public void MarkDirty() { dirty = true; }

        // ------------------------------------------------------------------ keyframes

        struct Key
        {
            public float hour, intensity, skyBlend, night;
            public Color ambient, skyTop, skyBottom, overlay;
            // 3D: light colour/intensity, sky & ground ambient, scene ambient intensity, night glow
            public Color sun, skyAmb, groundAmb;
            public float sunI, ambI, glow;

            public Key(float hour, Color ambient, float intensity, Color skyTop, Color skyBottom, float skyBlend, Color overlay, float night,
                       Color sun, float sunI, Color skyAmb, Color groundAmb, float ambI, float glow)
            {
                this.hour = hour; this.ambient = ambient; this.intensity = intensity; this.skyTop = skyTop;
                this.skyBottom = skyBottom; this.skyBlend = skyBlend; this.overlay = overlay; this.night = night;
                this.sun = sun; this.sunI = sunI; this.skyAmb = skyAmb; this.groundAmb = groundAmb; this.ambI = ambI; this.glow = glow;
            }
        }

        static readonly Color NightAmb = new Color(0.46f, 0.53f, 0.88f);
        static readonly Color NightTop = new Color(0.05f, 0.07f, 0.19f), NightBot = new Color(0.19f, 0.21f, 0.40f);
        static readonly Color NightOver = new Color(0.06f, 0.08f, 0.25f, 0.48f);
        static readonly Color NoOver = new Color(1f, 0.95f, 0.85f, 0f);
        // night: a crisp blue moon for clean highlights over a darker, deep blue fill (lamplight carries the warmth)
        static readonly Color Moon = new Color(0.55f, 0.66f, 1.00f);
        const float MoonI = 0.5f, NightAmbI = 0.6f;
        static readonly Color NightSky = new Color(0.22f, 0.27f, 0.55f), NightGround = new Color(0.10f, 0.10f, 0.20f);
        static readonly Color DaySun = new Color(1.00f, 0.93f, 0.80f);
        static readonly Color DaySky = new Color(0.62f, 0.68f, 0.82f), DayGround = new Color(0.42f, 0.38f, 0.34f);

        static readonly Key[] Keys =
        {
            new Key(0f,    NightAmb, 0.62f, NightTop, NightBot, 0.92f, NightOver, 1f, Moon, MoonI, NightSky, NightGround, NightAmbI, 1f),
            new Key(4.6f,  NightAmb, 0.62f, NightTop, NightBot, 0.92f, NightOver, 1f, Moon, MoonI, NightSky, NightGround, NightAmbI, 1f),
            new Key(6.0f,  new Color(0.86f, 0.72f, 0.84f), 0.76f, new Color(0.44f, 0.47f, 0.72f), new Color(1.00f, 0.77f, 0.66f), 0.72f, new Color(0.55f, 0.36f, 0.52f, 0.22f), 0.55f,
                           new Color(1.00f, 0.68f, 0.54f), 0.58f, new Color(0.60f, 0.54f, 0.76f), new Color(0.40f, 0.30f, 0.34f), 0.74f, 0.55f),
            new Key(7.4f,  new Color(1.00f, 0.86f, 0.76f), 0.92f, new Color(0.62f, 0.74f, 0.90f), new Color(1.00f, 0.86f, 0.72f), 0.45f, new Color(1.00f, 0.74f, 0.58f, 0.10f), 0.12f,
                           new Color(1.00f, 0.84f, 0.67f), 0.84f, new Color(0.62f, 0.64f, 0.80f), new Color(0.44f, 0.37f, 0.33f), 0.75f, 0.15f),
            new Key(9.5f,  new Color(1.00f, 0.97f, 0.92f), 1.00f, Color.white, Color.white, 0f, NoOver, 0f,
                           DaySun, 0.95f, DaySky, DayGround, 0.75f, 0f),
            new Key(15.5f, new Color(1.00f, 0.96f, 0.88f), 1.00f, Color.white, new Color(1.00f, 0.93f, 0.80f), 0.12f, NoOver, 0f,
                           new Color(1.00f, 0.91f, 0.76f), 0.93f, new Color(0.63f, 0.67f, 0.80f), new Color(0.43f, 0.38f, 0.33f), 0.75f, 0f),
            new Key(17.8f, new Color(1.00f, 0.80f, 0.66f), 0.90f, new Color(0.56f, 0.55f, 0.80f), new Color(1.00f, 0.70f, 0.50f), 0.60f, new Color(0.95f, 0.50f, 0.40f, 0.14f), 0.22f,
                           new Color(1.00f, 0.82f, 0.62f), 0.9f, new Color(0.58f, 0.64f, 0.86f), new Color(0.42f, 0.40f, 0.32f), 0.8f, 0.25f),
            new Key(19.4f, new Color(0.80f, 0.60f, 0.78f), 0.75f, new Color(0.30f, 0.27f, 0.55f), new Color(0.95f, 0.56f, 0.50f), 0.76f, new Color(0.40f, 0.25f, 0.46f, 0.28f), 0.60f,
                           new Color(1.00f, 0.66f, 0.50f), 0.66f, new Color(0.48f, 0.50f, 0.80f), new Color(0.28f, 0.26f, 0.30f), 0.76f, 0.62f),
            new Key(20.9f, new Color(0.50f, 0.53f, 0.86f), 0.64f, new Color(0.08f, 0.10f, 0.25f), new Color(0.25f, 0.25f, 0.48f), 0.90f, new Color(0.07f, 0.09f, 0.26f, 0.45f), 0.94f,
                           Moon, MoonI * 0.95f, NightSky, NightGround, NightAmbI, 0.95f),
            new Key(24f,   NightAmb, 0.62f, NightTop, NightBot, 0.92f, NightOver, 1f, Moon, MoonI, NightSky, NightGround, NightAmbI, 1f),
        };

        /// <summary>Sunrise / sunset hours of the sun's path (the disc is below the horizon outside them).</summary>
        public const float Sunrise = 5.9f, Sunset = 19.5f;

        static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, 1f);

        void Evaluate(float hour)
        {
            Hour = Mathf.Repeat(hour, 24f);
            int i = 0;
            while (i < Keys.Length - 2 && Keys[i + 1].hour <= Hour) i++;
            var a = Keys[i];
            var b = Keys[i + 1];
            float t = Mathf.Clamp01((Hour - a.hour) / Mathf.Max(1e-4f, b.hour - a.hour));
            t = t * t * (3f - 2f * t);

            NightFactor = Mathf.Lerp(a.night, b.night, t);
            var amb = Color.Lerp(a.ambient, b.ambient, t);
            AmbientColor = new Color(amb.r * MapAmbient.r, amb.g * MapAmbient.g, amb.b * MapAmbient.b, 1f);
            AmbientIntensity = Mathf.Lerp(a.intensity, b.intensity, t) * MapAmbientIntensity;
            SkyTopTint = Color.Lerp(a.skyTop, b.skyTop, t);
            SkyBottomTint = Color.Lerp(a.skyBottom, b.skyBottom, t);
            SkyBlend = Mathf.Lerp(a.skyBlend, b.skyBlend, t);
            var o = Color.Lerp(a.overlay, b.overlay, t);
            // map ambient darker than white → extra tinted overlay in unlit mode (2D-era value, kept for callers)
            float lum = MapAmbient.r * 0.3f + MapAmbient.g * 0.55f + MapAmbient.b * 0.15f;
            lum *= Mathf.Clamp01(MapAmbientIntensity);
            float extra = Mathf.Clamp01(1f - lum) * 0.6f;
            if (extra > 0.01f)
            {
                var tint = new Color(MapAmbient.r * 0.35f, MapAmbient.g * 0.35f, MapAmbient.b * 0.45f, 1f);
                float total = 1f - (1f - o.a) * (1f - extra);
                var rgb = Color.Lerp(tint, o, o.a / Mathf.Max(1e-4f, o.a + extra));
                o = new Color(rgb.r, rgb.g, rgb.b, total);
            }
            Overlay = o;

            EvaluateLight(a, b, t);
            Changed?.Invoke(this);
        }

        void EvaluateLight(Key a, Key b, float t)
        {
            // ---- the sun's path: rises on the left (−X), culminates in front of the scene, sets on the right (+X)
            float dayT = (Hour - Sunrise) / (Sunset - Sunrise);
            float az = Mathf.Lerp(-100f, 100f, Mathf.Clamp01(dayT)) * Mathf.Deg2Rad;
            float elev = (dayT > 0f && dayT < 1f ? Mathf.Sin(dayT * Mathf.PI) * 62f : 0f) - 4f;   // degrees
            float sx = Mathf.Sin(az), sy = -Mathf.Max(0.3f, Mathf.Cos(az));
            float hl = Mathf.Sqrt(sx * sx + sy * sy);
            sx /= hl; sy /= hl;
            // lighting never grazes: a low sun still lights the ground softly (painterly, no cast shadows)
            float le = Mathf.Max(elev, 14f) * Mathf.Deg2Rad;
            var sunLight = new Vector3(sx * Mathf.Cos(le), sy * Mathf.Cos(le), -Mathf.Sin(le));
            var moonLight = new Vector3(0.47f, -0.56f, -0.68f);
            float moonW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((NightFactor - 0.35f) / 0.45f));
            var dir = Vector3.Lerp(sunLight, moonLight, moonW);
            LightDirection = dir.sqrMagnitude > 1e-6f ? dir.normalized : moonLight;

            // the discs: the sun crosses the sky behind the scene (where the camera can see it), the moon hangs high right
            float de = elev * Mathf.Deg2Rad;
            var sunSky = new Vector3(sx * Mathf.Cos(de) * 0.9f, Mathf.Max(0.35f, Mathf.Cos(az)) * Mathf.Cos(de) + 0.25f, -Mathf.Sin(de));
            SunSkyDirection = sunSky.normalized;
            MoonSkyDirection = new Vector3(0.42f, 0.78f, -0.46f).normalized;
            SunVisibility = Mathf.Clamp01((elev + 3f) / 5f) * (1f - Mathf.Clamp01((NightFactor - 0.5f) / 0.4f));
            MoonVisibility = Mathf.Clamp01((NightFactor - 0.3f) / 0.5f);
            float low = 1f - Mathf.Clamp01((elev - 2f) / 22f);
            SunGlow = new Color(1f, 0.62f + 0.2f * (1f - low), 0.42f, low * SunVisibility * 0.85f);

            // the map's ambient multiplies the light; its intensity a little softened (2.5D maps were tuned against
            // pre-lit painted sprites — a dusk map at 0.75 would otherwise turn unreadably dark in 3D)
            float mai = Mathf.Lerp(1f, MapAmbientIntensity, 0.6f);
            // ... and its hue a little softened too: a strongly tinted map ambient (the shrine's violet) would otherwise
            // paint ground, stone and statues one colour
            float ml = MapAmbient.r * 0.3f + MapAmbient.g * 0.59f + MapAmbient.b * 0.11f;
            var mapLight = Color.Lerp(new Color(ml, ml, ml, 1f), MapAmbient, 0.65f);
            var sun = Color.Lerp(a.sun, b.sun, t);
            LightColor = Mul(sun, mapLight);
            LightIntensity = Mathf.Lerp(a.sunI, b.sunI, t) * mai;
            SkyAmbient = Mul(Color.Lerp(a.skyAmb, b.skyAmb, t), mapLight);
            GroundAmbient = Mul(Color.Lerp(a.groundAmb, b.groundAmb, t), mapLight);
            SceneAmbientIntensity = Mathf.Lerp(a.ambI, b.ambI, t) * mai;
            NightGlow = Mathf.Lerp(a.glow, b.glow, t);

            var lc = LightColor;
            var sk = SkyAmbient;
            float lit = (lc.r * 0.3f + lc.g * 0.55f + lc.b * 0.15f) * LightIntensity * 0.7f
                        + (sk.r * 0.3f + sk.g * 0.55f + sk.b * 0.15f) * SceneAmbientIntensity;
            LightLevel = Mathf.Clamp01(lit);
        }

        // ------------------------------------------------------------------ lamplight

        static readonly Color Amber = new Color(1f, 0.68f, 0.38f);

        /// <summary>
        /// A lamp's / window's light colour at a night factor: warm whites and yellows deepen towards amber as night
        /// falls, so their pools read warm against the blue night (coloured lights — violet crystals, a locked
        /// waymarker's glow, a fire's orange — keep their hue). MapView and the preview tool use it alike.
        /// </summary>
        public static Color LampColor(Color authored, float night)
        {
            if (night <= 0f) return authored;
            Color.RGBToHSV(authored, out float h, out float sat, out float v);
            // warm hues (red-orange … yellow) of soft saturation only
            float warm = (1f - Mathf.Clamp01(Mathf.Abs(h * 360f - 40f) / 30f)) * (1f - Mathf.Clamp01((sat - 0.5f) / 0.25f));
            if (sat < 0.05f) warm = 1f;
            float k = Mathf.Clamp01(night) * warm * 0.7f;
            var c = Color.Lerp(authored, Amber, k);
            c.a = authored.a;
            return c;
        }

        /// <summary>
        /// The night grade (shader global _LV_Grade, set by MapView): how far moon- and sky-lit colours drift towards a
        /// cool blue-grey (0 by day … 0.38 deep in the night); lamplight keeps the full colour.
        /// </summary>
        public float NightGrade => 0.38f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((NightFactor - 0.5f) / 0.42f));
        /// <summary>The blue-grey a fully graded colour's luma takes (linear multiplier).</summary>
        public static readonly Vector3 NightGradeTint = new Vector3(0.8f, 0.92f, 1.22f);

        /// <summary>A lamp's reach at a night factor: a little wider at night, so the warm pools read on the ground.</summary>
        public static float LampRange(float range, float night) => range * (1f + 0.22f * Mathf.Clamp01(night));

        /// <summary>
        /// Pushes the mood into SceneLighting (sun, ambient, rim, night glow). Fog is the map's business (sky colours):
        /// MapView sets it right after.
        /// </summary>
        public void ApplyTo()
        {
            SceneLighting.SunDirection = LightDirection;
            SceneLighting.SunColor = LightColor;
            SceneLighting.SunIntensity = LightIntensity;
            SceneLighting.SkyAmbient = SkyAmbient;
            SceneLighting.GroundAmbient = GroundAmbient;
            SceneLighting.AmbientIntensity = SceneAmbientIntensity;
            SceneLighting.NightGlow = NightGlow;
            var rim = Color.Lerp(Color.Lerp(LightColor, Color.white, 0.45f), new Color(0.62f, 0.72f, 1f), Mathf.Clamp01(NightFactor));
            SceneLighting.RimColor = rim;
        }

        /// <summary>
        /// Sky colour for an authored map sky colour at the current time. Fixed-time maps keep their
        /// authored sky (it was painted for that time) unless the hour was overridden.
        /// </summary>
        public Color SkyColor(Color authored, bool top)
        {
            if (!Cycle && overrideHour == null) return authored;
            var key = top ? SkyTopTint : SkyBottomTint;
            return Color.Lerp(authored, key, SkyBlend);
        }

        /// <summary>
        /// Approximate multiply colour equivalent to the 2D-era unlit overlay (kept for compatibility).
        /// </summary>
        public Color OverlayMultiply
        {
            get
            {
                var o = Overlay;
                return new Color(1f - o.a + o.r * o.a, 1f - o.a + o.g * o.a, 1f - o.a + o.b * o.a, 1f);
            }
        }
    }
}
