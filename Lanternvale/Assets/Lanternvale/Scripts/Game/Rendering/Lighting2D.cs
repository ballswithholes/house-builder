// Bridge to URP 2D lights without a compile-time dependency on URP.
// * With URP + the 2D Renderer: real Light2D components (global ambient + point lights) light the
//   Sprite-Lit materials, giving the soft warm look the art direction asks for.
// * Without it (built-in pipeline or URP Universal renderer): a tinted night overlay plus soft glow
//   sprites approximate the same mood, so the game looks right in any project setup.
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    public static class Lighting2D
    {
        static Type lightType;
        static bool probed;
        static bool? lit;

        // URP moves Light2D between assemblies/namespaces across versions:
        //   URP 16+ (Unity 2023.2 / Unity 6): UnityEngine.Rendering.Universal.Light2D in Unity.RenderPipelines.Universal.2D.Runtime
        //   URP 11-15 (2021.x / 2022.x):       UnityEngine.Rendering.Universal.Light2D in Unity.RenderPipelines.Universal.Runtime
        //   URP <= 10:                         UnityEngine.Experimental.Rendering.Universal.Light2D in Unity.RenderPipelines.Universal.Runtime
        const string Light2DName = "UnityEngine.Rendering.Universal.Light2D";
        const string Light2DNameExperimental = "UnityEngine.Experimental.Rendering.Universal.Light2D";
        static readonly string[] Light2DQualifiedNames =
        {
            Light2DName + ", Unity.RenderPipelines.Universal.2D.Runtime",
            Light2DName + ", Unity.RenderPipelines.Universal.Runtime",
            Light2DNameExperimental + ", Unity.RenderPipelines.Universal.Runtime",
        };

        /// <summary>
        /// URP's Light2D component type (whatever assembly this URP version compiles it into), or null when
        /// URP is not installed. Resolved once; shared by everything that needs to find or add 2D lights.
        /// </summary>
        public static Type Light2DType
        {
            get
            {
                if (probed) return lightType;
                probed = true;
                lightType = null;
                foreach (var qn in Light2DQualifiedNames)
                {
                    lightType = AsBehaviour(SafeGetType(qn));
                    if (lightType != null) return lightType;
                }
                // unknown assembly layout: scan everything loaded
                lightType = AsBehaviour(FindType(Light2DName)) ?? AsBehaviour(FindType(Light2DNameExperimental));
                return lightType;
            }
        }

        /// <summary>Finds a type by full name in any loaded assembly (null when none defines it).</summary>
        public static Type FindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch (Exception) { }
                if (t != null) return t;
            }
            return null;
        }

        static Type SafeGetType(string assemblyQualifiedName)
        {
            try { return Type.GetType(assemblyQualifiedName, false); } catch (Exception) { return null; }
        }

        static Type AsBehaviour(Type t) => t != null && typeof(Behaviour).IsAssignableFrom(t) ? t : null;

        /// <summary>True when sprites are lit by URP 2D lights (2D Renderer active).</summary>
        public static bool IsLit
        {
            get
            {
                if (lit.HasValue) return lit.Value;
                lit = false;
                var rp = GraphicsSettings.currentRenderPipeline;
                if (rp == null || Light2DType == null) return false;
                try
                {
                    // UniversalRenderPipelineAsset.scriptableRenderer -> Renderer2D when the 2D renderer is active.
                    var prop = rp.GetType().GetProperty("scriptableRenderer", BindingFlags.Public | BindingFlags.Instance);
                    var renderer = prop?.GetValue(rp, null);
                    lit = renderer != null && renderer.GetType().Name.Contains("2D");
                }
                catch (Exception)
                {
                    lit = false;
                }
                return lit.Value;
            }
        }

        /// <summary>Forget cached detection (call after changing the pipeline in the editor).</summary>
        public static void Reset() { lit = null; probed = false; lightType = null; }

        static void Set(Component c, string prop, object value)
        {
            if (c == null) return;
            var p = c.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;
            try
            {
                if (p.PropertyType.IsEnum && value is string s) value = Enum.Parse(p.PropertyType, s);
                p.SetValue(c, value, null);
            }
            catch (Exception) { /* property differs between URP versions; ignore */ }
        }

        static void ApplyToAllSortingLayers(Component c)
        {
            var f = c.GetType().GetField("m_ApplyToSortingLayers", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return;
            var layers = SortingLayer.layers;
            var ids = new int[layers.Length];
            for (int i = 0; i < layers.Length; i++) ids[i] = layers[i].id;
            try { f.SetValue(c, ids); } catch (Exception) { }
        }

        /// <summary>Creates the scene-wide ambient light (URP) — returns null when unlit.</summary>
        public static Component CreateGlobalLight(Transform parent, Color color, float intensity)
        {
            if (!IsLit) return null;
            var go = new GameObject("Global Light 2D");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent(Light2DType);
            Set(c, "lightType", "Global");
            ApplyToAllSortingLayers(c);
            Set(c, "color", color);
            Set(c, "intensity", intensity);
            return c;
        }

        public static void SetColorIntensity(Component light, Color color, float intensity)
        {
            if (light == null) return;
            Set(light, "color", color);
            Set(light, "intensity", intensity);
        }

        /// <summary>
        /// Adds a soft point light at the object. In unlit mode a glow sprite is added instead.
        /// Returns a handle that can be animated (flicker) with SetPointLight.
        /// </summary>
        public static LightHandle AddPointLight(GameObject go, Vector3 localOffset, Color color, float radius, float intensity)
        {
            var holder = new GameObject("Light");
            holder.transform.SetParent(go.transform, false);
            holder.transform.localPosition = localOffset;
            var h = new LightHandle { color = color, radius = radius, intensity = intensity };
            if (IsLit)
            {
                var c = holder.AddComponent(Light2DType);
                Set(c, "lightType", "Point");
                ApplyToAllSortingLayers(c);
                Set(c, "pointLightOuterRadius", radius);
                Set(c, "pointLightInnerRadius", radius * 0.15f);
                Set(c, "falloffIntensity", 0.65f);
                Set(c, "color", color);
                Set(c, "intensity", intensity);
                h.light = c;
            }
            // A faint glow sprite even when lit: gives lanterns a visible halo through mist.
            var sr = holder.AddComponent<SpriteRenderer>();
            sr.sprite = GlowSprite;
            // unlit mode: draw glows above the night overlay so lights "punch through" the darkness
            sr.sortingOrder = Lighting2D.IsLit ? SortingOrders.Glow : SortingOrders.NightOverlay + 100;
            float d = radius * (IsLit ? 0.9f : 1.6f);
            holder.transform.localScale = new Vector3(d, d, 1f);
            h.glow = sr;
            h.Apply(1f, 1f);
            return h;
        }

        static Sprite glowSprite;
        public static Sprite GlowSprite
        {
            get
            {
                if (glowSprite == null)
                {
                    var t = ProceduralArt.Glow;
                    glowSprite = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), t.width, 0, SpriteMeshType.FullRect);
                    glowSprite.name = "glow";
                }
                return glowSprite;
            }
        }
    }

    /// <summary>A light (URP Light2D and/or glow sprite) that can be dimmed for day/night and flicker.</summary>
    public sealed class LightHandle
    {
        public Component light;
        public SpriteRenderer glow;
        public Color color;
        public float radius, intensity;

        /// <param name="nightFactor">0 = full daylight (lights barely visible), 1 = night.</param>
        public void Apply(float nightFactor, float flicker)
        {
            float k = Mathf.Lerp(0.25f, 1f, nightFactor) * flicker;
            if (light != null) Lighting2D.SetColorIntensity(light, color, intensity * k);
            if (glow != null)
            {
                var c = color;
                c.a = (Lighting2D.IsLit ? 0.18f : 0.38f) * Mathf.Lerp(0.15f, 1f, nightFactor) * flicker * Mathf.Clamp01(intensity);
                glow.color = c;
            }
        }
    }

    /// <summary>Sorting order bands (single "Default" sorting layer; y-sorting inside the World band).</summary>
    public static class SortingOrders
    {
        public const int Sky = -32000;
        public const int Background = -31000;   // + layer index * 10
        public const int Ground = -30000;
        public const int Decal = -29900;
        public const int Shadow = -29800;
        public const int WorldMin = -20000;     // y-sorted props and units live in (WorldMin, WorldMax)
        public const int WorldMax = 20000;
        public const int Glow = 21000;
        public const int Effects = 22000;
        public const int NightOverlay = 25000;
        public const int Foreground = 26000;
        public const int Overlay = 30000;

        /// <summary>Order for an object standing at world y: lower y (closer to camera) draws in front.</summary>
        public static int ForY(float y, int bias = 0) => Mathf.Clamp(Mathf.RoundToInt(-y * 100f) + bias, WorldMin + 1, WorldMax - 1);
    }
}
