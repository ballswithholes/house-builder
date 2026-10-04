// Time of day → lighting mood. Pure data/maths, driven by MapView every frame.
//
//   dawn  peach and lavender, soft light
//   day   warm white (the map's own sky colours)
//   dusk  rose and amber, long warm light
//   night deep blue ambient; lanterns, fireflies and spirit glows carry the scene
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
        public Color AmbientColor { get; private set; } = Color.white;
        public float AmbientIntensity { get; private set; } = 1f;
        public Color SkyTopTint { get; private set; } = Color.white;
        public Color SkyBottomTint { get; private set; } = Color.white;
        public float SkyBlend { get; private set; }
        /// <summary>Unlit-mode overlay (rgb + alpha) laid over the world.</summary>
        public Color Overlay { get; private set; } = new Color(0, 0, 0, 0);

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
            public Key(float hour, Color ambient, float intensity, Color skyTop, Color skyBottom, float skyBlend, Color overlay, float night)
            {
                this.hour = hour; this.ambient = ambient; this.intensity = intensity; this.skyTop = skyTop;
                this.skyBottom = skyBottom; this.skyBlend = skyBlend; this.overlay = overlay; this.night = night;
            }
        }

        static readonly Color NightAmb = new Color(0.46f, 0.53f, 0.88f);
        static readonly Color NightTop = new Color(0.05f, 0.07f, 0.19f), NightBot = new Color(0.19f, 0.21f, 0.40f);
        static readonly Color NightOver = new Color(0.06f, 0.08f, 0.25f, 0.48f);
        static readonly Color NoOver = new Color(1f, 0.95f, 0.85f, 0f);

        static readonly Key[] Keys =
        {
            new Key(0f,    NightAmb, 0.62f, NightTop, NightBot, 0.92f, NightOver, 1f),
            new Key(4.6f,  NightAmb, 0.62f, NightTop, NightBot, 0.92f, NightOver, 1f),
            new Key(6.0f,  new Color(0.86f, 0.72f, 0.84f), 0.76f, new Color(0.44f, 0.47f, 0.72f), new Color(1.00f, 0.77f, 0.66f), 0.72f, new Color(0.55f, 0.36f, 0.52f, 0.22f), 0.55f),
            new Key(7.4f,  new Color(1.00f, 0.86f, 0.76f), 0.92f, new Color(0.62f, 0.74f, 0.90f), new Color(1.00f, 0.86f, 0.72f), 0.45f, new Color(1.00f, 0.74f, 0.58f, 0.10f), 0.12f),
            new Key(9.5f,  new Color(1.00f, 0.97f, 0.92f), 1.00f, Color.white, Color.white, 0f, NoOver, 0f),
            new Key(15.5f, new Color(1.00f, 0.96f, 0.88f), 1.00f, Color.white, new Color(1.00f, 0.93f, 0.80f), 0.12f, NoOver, 0f),
            new Key(17.8f, new Color(1.00f, 0.80f, 0.66f), 0.90f, new Color(0.56f, 0.55f, 0.80f), new Color(1.00f, 0.70f, 0.50f), 0.60f, new Color(0.95f, 0.50f, 0.40f, 0.14f), 0.22f),
            new Key(19.4f, new Color(0.80f, 0.60f, 0.78f), 0.75f, new Color(0.30f, 0.27f, 0.55f), new Color(0.95f, 0.56f, 0.50f), 0.76f, new Color(0.40f, 0.25f, 0.46f, 0.28f), 0.60f),
            new Key(20.9f, new Color(0.50f, 0.53f, 0.86f), 0.64f, new Color(0.08f, 0.10f, 0.25f), new Color(0.25f, 0.25f, 0.48f), 0.90f, new Color(0.07f, 0.09f, 0.26f, 0.45f), 0.94f),
            new Key(24f,   NightAmb, 0.62f, NightTop, NightBot, 0.92f, NightOver, 1f),
        };

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
            // map ambient darker than white → extra tinted overlay in unlit mode
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
            Changed?.Invoke(this);
        }

        /// <summary>Sky colour for an authored map sky colour at the current time.</summary>
        public Color SkyColor(Color authored, bool top)
        {
            var key = top ? SkyTopTint : SkyBottomTint;
            return Color.Lerp(authored, key, SkyBlend);
        }

        /// <summary>
        /// Approximate per-sprite multiply colour equivalent to the unlit overlay (for sprites drawn
        /// above the overlay band, such as foreground props).
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
