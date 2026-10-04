// Allocation-free animation of Lighting2D lights. Lighting2D talks to URP's Light2D by reflection
// (boxing on every set); for lights animated every frame (day/night, flicker) we bind typed
// delegates to the property setters once, then drive them without allocations.
using System;
using System.Reflection;
using UnityEngine;

namespace Lanternvale.Game
{
    /// <summary>Typed setters for a Light2D component (or nothing in unlit mode).</summary>
    public sealed class Light2DSetter
    {
        readonly Action<float> setIntensity;
        readonly Action<Color> setColor;
        readonly Component light;
        float lastIntensity = float.NaN;
        Color lastColor = new Color(-1, -1, -1, -1);

        public Light2DSetter(Component light)
        {
            this.light = light;
            if (light == null) return;
            setIntensity = Bind<float>(light, "intensity");
            setColor = Bind<Color>(light, "color");
        }

        static Action<T> Bind<T>(Component c, string prop)
        {
            try
            {
                var p = c.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
                var m = p != null && p.PropertyType == typeof(T) ? p.GetSetMethod() : null;
                if (m == null) return null;
                return (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), c, m, false);
            }
            catch (Exception) { return null; }
        }

        public bool Valid => light != null;

        public void Set(Color color, float intensity)
        {
            if (light == null) return;
            if (Mathf.Abs(intensity - lastIntensity) > 0.002f || float.IsNaN(lastIntensity))
            {
                lastIntensity = intensity;
                if (setIntensity != null) setIntensity(intensity);
                else Lighting2D.SetColorIntensity(light, color, intensity);
            }
            if (Mathf.Abs(color.r - lastColor.r) + Mathf.Abs(color.g - lastColor.g) + Mathf.Abs(color.b - lastColor.b) > 0.003f)
            {
                lastColor = color;
                if (setColor != null) setColor(color);
                else Lighting2D.SetColorIntensity(light, color, intensity);
            }
        }

        public void SetEnabled(bool on)
        {
            if (light is Behaviour b && b.enabled != on) b.enabled = on;
        }
    }

    /// <summary>A point light from PresentationArt.AddPointLight (Lighting2D) animated by day/night and flicker.</summary>
    public sealed class AnimatedLight
    {
        public readonly LightHandle Handle;
        public bool Flicker;
        public bool NightOnly;
        /// <summary>Extra multiplier (lantern lit/unlit fades, highlight boosts).</summary>
        public float Boost = 1f;
        public bool Enabled = true;
        readonly Light2DSetter setter;
        readonly float seed;
        float lastGlowAlpha = -1f;
        bool lastOn = true;

        public AnimatedLight(LightHandle handle, bool flicker, bool nightOnly)
        {
            Handle = handle;
            Flicker = flicker;
            NightOnly = nightOnly;
            setter = new Light2DSetter(handle?.light);
            seed = UnityEngine.Random.value * 100f;
        }

        public void Apply(float nightFactor, float time)
        {
            if (Handle == null) return;
            float f = 1f;
            if (Flicker)
            {
                f = 1f + (Mathf.PerlinNoise(time * 5.3f + seed, seed) - 0.5f) * 0.22f + Mathf.Sin(time * 17.3f + seed) * 0.035f;
            }
            float gate = Enabled ? Boost : 0f;
            if (NightOnly) gate *= Mathf.Clamp01((nightFactor - 0.2f) / 0.45f);
            bool on = gate > 0.005f;
            if (on != lastOn)
            {
                lastOn = on;
                setter.SetEnabled(on);
                if (Handle.glow != null) Handle.glow.enabled = on;
            }
            if (!on) return;
            float k = Mathf.Lerp(0.25f, 1f, nightFactor) * f * gate;
            setter.Set(Handle.color, Handle.intensity * k);
            if (Handle.glow != null)
            {
                float a = (PresentationArt.SpritesLit ? 0.18f : 0.38f) * Mathf.Lerp(0.15f, 1f, nightFactor) * f * gate * Mathf.Clamp01(Handle.intensity);
                if (Mathf.Abs(a - lastGlowAlpha) > 0.002f)
                {
                    lastGlowAlpha = a;
                    var c = Handle.color;
                    c.a = a;
                    Handle.glow.color = c;
                }
            }
        }
    }
}
