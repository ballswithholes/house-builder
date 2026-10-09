// The scene's lighting state, pushed to the Lanternvale shaders as globals once per frame (see LanternvaleCommon.cginc).
//
// Whoever owns the mood (MapView + DayNight) sets the fields; point lights (lanterns, campfires, spell glows) are
// registered as SceneLighting.PointLight handles and the 16 strongest near the camera's focus are sent each frame.
// Nothing here touches Unity's Light components or RenderSettings: the look is identical in every render pipeline.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class SceneLighting
    {
        public const int MaxLights = 16;

        // ---- mood (defaults: a soft, warm afternoon)
        /// <summary>Direction from the ground TO the sun (normalised on push). Up is −Z.</summary>
        public static Vector3 SunDirection = new Vector3(-0.35f, -0.55f, -0.75f);
        public static Color SunColor = new Color(1f, 0.93f, 0.8f);
        public static float SunIntensity = 0.95f;
        public static Color SkyAmbient = new Color(0.62f, 0.68f, 0.82f);
        public static Color GroundAmbient = new Color(0.42f, 0.38f, 0.34f);
        public static float AmbientIntensity = 0.75f;
        public static Color FogColor = new Color(0.82f, 0.88f, 0.93f);
        /// <summary>Fog starts / is complete at these distances from the camera (metres).</summary>
        public static float FogStart = 28f, FogEnd = 120f;
        /// <summary>Maximum fog amount (0 none … 1 opaque).</summary>
        public static float FogMax = 0.85f;
        public static Color RimColor = new Color(1f, 0.92f, 0.78f);
        public static float RimPower = 3f;
        /// <summary>Emissive boost (windows, lantern glass, crystals glow brighter at night).</summary>
        public static float NightGlow;
        /// <summary>Wind sway amplitude (metres at weight 1) and speed.</summary>
        public static float WindStrength = 0.06f, WindSpeed = 1.6f;

        /// <summary>Where the camera looks (lights nearest to it win when more than MaxLights are on).</summary>
        public static Vector3 Focus;

        public sealed class PointLight
        {
            public Vector3 Position;
            public Color Color = new Color(1f, 0.8f, 0.5f);
            public float Intensity = 1f;
            public float Range = 5f;
            public bool Enabled = true;
            internal bool registered;
            /// <summary>False once removed (Remove, or ResetAll at a map change): add a new light instead of reusing it.</summary>
            public bool IsRegistered => registered;
        }

        static readonly List<PointLight> lights = new List<PointLight>();
        static readonly Vector4[] posArray = new Vector4[MaxLights];
        static readonly Vector4[] colArray = new Vector4[MaxLights];
        static readonly PointLight[] chosen = new PointLight[MaxLights];
        static readonly float[] chosenScore = new float[MaxLights];

        static readonly int SunDirId = Shader.PropertyToID("_LV_SunDir");
        static readonly int SunColorId = Shader.PropertyToID("_LV_SunColor");
        static readonly int SkyAmbId = Shader.PropertyToID("_LV_SkyAmb");
        static readonly int GroundAmbId = Shader.PropertyToID("_LV_GroundAmb");
        static readonly int FogColorId = Shader.PropertyToID("_LV_FogColor");
        static readonly int FogParamsId = Shader.PropertyToID("_LV_FogParams");
        static readonly int RimColorId = Shader.PropertyToID("_LV_RimColor");
        static readonly int MiscId = Shader.PropertyToID("_LV_Misc");
        static readonly int LightPosId = Shader.PropertyToID("_LV_LightPos");
        static readonly int LightColId = Shader.PropertyToID("_LV_LightCol");
        static readonly int LightCountId = Shader.PropertyToID("_LV_LightCount");

        /// <summary>Registers a point light (keep the handle; change its fields freely; Remove when done).</summary>
        public static PointLight Add(Vector3 position, Color color, float intensity, float range)
        {
            EnsureDriver();
            var l = new PointLight { Position = position, Color = color, Intensity = intensity, Range = range, registered = true };
            lights.Add(l);
            return l;
        }

        public static void Remove(PointLight l)
        {
            if (l == null || !l.registered) return;
            l.registered = false;
            lights.Remove(l);
        }

        public static int LightCount => lights.Count;

        /// <summary>Restores the default mood and drops every light (map teardown).</summary>
        public static void ResetAll()
        {
            foreach (var l in lights) l.registered = false;
            lights.Clear();
            SunDirection = new Vector3(-0.35f, -0.55f, -0.75f);
            SunColor = new Color(1f, 0.93f, 0.8f);
            SunIntensity = 0.95f;
            SkyAmbient = new Color(0.62f, 0.68f, 0.82f);
            GroundAmbient = new Color(0.42f, 0.38f, 0.34f);
            AmbientIntensity = 0.75f;
            FogColor = new Color(0.82f, 0.88f, 0.93f);
            FogStart = 28f; FogEnd = 120f; FogMax = 0.85f;
            RimColor = new Color(1f, 0.92f, 0.78f);
            RimPower = 3f;
            NightGlow = 0f;
        }

        static bool Linear => QualitySettings.activeColorSpace == ColorSpace.Linear;

        static Vector4 Working(Color c, float k)
        {
            var w = Linear ? c.linear : c;
            return new Vector4(w.r * k, w.g * k, w.b * k, 0f);
        }

        /// <summary>Pushes the globals (called every frame by the driver; call it yourself after big changes if needed).</summary>
        public static void Push()
        {
            var sd = SunDirection.sqrMagnitude > 1e-6f ? SunDirection.normalized : new Vector3(0f, 0f, -1f);
            Shader.SetGlobalVector(SunDirId, new Vector4(sd.x, sd.y, sd.z, 0f));
            Shader.SetGlobalVector(SunColorId, Working(SunColor, SunIntensity));
            Shader.SetGlobalVector(SkyAmbId, Working(SkyAmbient, AmbientIntensity));
            Shader.SetGlobalVector(GroundAmbId, Working(GroundAmbient, AmbientIntensity));
            var fog = Working(FogColor, 1f);
            fog.w = Mathf.Clamp01(FogMax);
            Shader.SetGlobalVector(FogColorId, fog);
            Shader.SetGlobalVector(FogParamsId, new Vector4(FogStart, Mathf.Max(FogStart + 0.01f, FogEnd), 0f, 0f));
            var rim = Working(RimColor, 1f);
            rim.w = RimPower;
            Shader.SetGlobalVector(RimColorId, rim);
            Shader.SetGlobalVector(MiscId, new Vector4(NightGlow, WindStrength, WindSpeed, 0f));

            // pick the strongest lights near the focus
            int n = 0;
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                if (!l.Enabled || l.Intensity <= 0.001f || l.Range <= 0.01f) continue;
                float d = Vector3.Distance(l.Position, Focus);
                float score = l.Intensity * l.Range / (1f + d * d * 0.01f);
                if (n < MaxLights) { chosen[n] = l; chosenScore[n] = score; n++; continue; }
                int worst = 0;
                for (int k = 1; k < MaxLights; k++) if (chosenScore[k] < chosenScore[worst]) worst = k;
                if (score > chosenScore[worst]) { chosen[worst] = l; chosenScore[worst] = score; }
            }
            for (int i = 0; i < MaxLights; i++)
            {
                if (i < n)
                {
                    var l = chosen[i];
                    posArray[i] = new Vector4(l.Position.x, l.Position.y, l.Position.z, l.Range);
                    colArray[i] = Working(l.Color, l.Intensity);
                }
                else
                {
                    posArray[i] = Vector4.zero;
                    colArray[i] = Vector4.zero;
                }
                chosen[i] = null;
            }
            Shader.SetGlobalVectorArray(LightPosId, posArray);
            Shader.SetGlobalVectorArray(LightColId, colArray);
            Shader.SetGlobalFloat(LightCountId, n);
        }

        static void EnsureDriver()
        {
            if (!Application.isPlaying) return;
            PresentationHost.Ensure<SceneLightingDriver>();
        }

        /// <summary>Makes sure the per-frame push runs (also called by everything that creates world content).</summary>
        public static void Ensure() => EnsureDriver();
    }

    /// <summary>Pushes SceneLighting after every presentation system updated this frame.</summary>
    [DefaultExecutionOrder(2000)]
    public sealed class SceneLightingDriver : MonoBehaviour
    {
        void OnEnable() => SceneLighting.Push();

        void LateUpdate()
        {
            var cam = PresentationHost.Cam;
            if (cam != null)
            {
                // focus: where the view ray meets the ground (else a point ahead of the camera)
                var t = cam.transform;
                var f = t.forward;
                if (f.z > 0.01f) SceneLighting.Focus = t.position + f * (-t.position.z / f.z);
                else SceneLighting.Focus = t.position + f * 15f;
            }
            SceneLighting.Push();
        }
    }
}
