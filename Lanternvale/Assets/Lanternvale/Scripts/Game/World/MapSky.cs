// The sky of a map: a vertex-coloured dome (Lanternvale/Sky) centred on the camera with the map's skyTop → skyBottom
// gradient tinted by the time of day, a warm glow around a low sun, sun / moon discs and twinkling stars (additive).
// Everything follows the camera position (never its rotation), so the sky is "at infinity"; terrain and backdrop draw
// over it (the dome is in the Background queue and writes no depth). The horizon colour doubles as the fog colour.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    internal sealed class MapSky
    {
        const float Radius = 460f;
        const int Segments = 36;
        static readonly float[] RingElevations = { -40f, -12f, -3f, 0f, 3f, 8f, 15f, 25f, 38f, 54f, 72f, 90f };

        readonly Transform root;
        readonly Mesh dome;
        readonly Vector3[] dirs;
        readonly Color32[] colors;
        readonly MeshRenderer sun, sunCore, moon, moonGlow;
        readonly MeshRenderer[] stars = new MeshRenderer[3];
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly List<Mesh> owned;
        Color authoredTop, authoredBottom;
        float lastHour = -100f;
        bool dirty = true;

        /// <summary>Horizon colour (fog, camera background) and zenith colour after the time-of-day tint.</summary>
        public Color Horizon { get; private set; }
        public Color Zenith { get; private set; }

        public MapSky(Transform parent, Color skyTop, Color skyBottom, int seed, List<Mesh> owned)
        {
            this.owned = owned;
            authoredTop = skyTop;
            authoredBottom = skyBottom;
            root = new GameObject("Sky").transform;
            root.SetParent(parent, false);

            // ---- dome: rings of constant elevation (up = −Z), a cap vertex at the zenith
            int rings = RingElevations.Length - 1;
            int n = rings * (Segments + 1) + 1;
            dirs = new Vector3[n];
            colors = new Color32[n];
            var verts = new Vector3[n];
            var tris = new List<int>(rings * Segments * 6);
            for (int r = 0; r < rings; r++)
            {
                float e = RingElevations[r] * Mathf.Deg2Rad;
                for (int s = 0; s <= Segments; s++)
                {
                    float a = s * Mathf.PI * 2f / Segments;
                    var d = new Vector3(Mathf.Cos(e) * Mathf.Sin(a), Mathf.Cos(e) * Mathf.Cos(a), -Mathf.Sin(e));
                    int i = r * (Segments + 1) + s;
                    dirs[i] = d;
                    verts[i] = d * Radius;
                }
            }
            int top = n - 1;
            dirs[top] = World3D.Up;
            verts[top] = World3D.Up * Radius;
            for (int r = 0; r < rings - 1; r++)
                for (int s = 0; s < Segments; s++)
                {
                    int a = r * (Segments + 1) + s, b = a + 1, c = a + Segments + 1, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(b);
                }
            int last = (rings - 1) * (Segments + 1);
            for (int s = 0; s < Segments; s++) { tris.Add(last + s); tris.Add(top); tris.Add(last + s + 1); }
            dome = new Mesh { name = "lv_sky_dome" };
            dome.vertices = verts;
            dome.colors32 = colors;
            dome.SetTriangles(tris, 0);
            dome.bounds = new Bounds(Vector3.zero, Vector3.one * Radius * 2f);
            owned.Add(dome);
            var domeGo = new GameObject("Dome");
            domeGo.transform.SetParent(root, false);
            domeGo.AddComponent<MeshFilter>().sharedMesh = dome;
            var dr = domeGo.AddComponent<MeshRenderer>();
            dr.sharedMaterial = Materials3D.Sky;
            Quiet(dr);

            // ---- discs and stars (additive, never fogged)
            sun = Disc("Sun Glow", WorldTextures.Glow);
            sunCore = Disc("Sun", WorldTextures.Glow);
            moonGlow = Disc("Moon Glow", WorldTextures.Glow);
            moon = Disc("Moon", WorldTextures.Moon);
            var rng = new System.Random(seed);
            for (int g = 0; g < stars.Length; g++) stars[g] = BuildStars(g, rng);
        }

        static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        MeshRenderer Disc(string name, Texture tex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshCache.GroundQuad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Materials3D.AdditiveFor(tex);
            Quiet(r);
            r.enabled = false;
            return r;
        }

        MeshRenderer BuildStars(int group, System.Random rng)
        {
            const int count = 70;
            var verts = new List<Vector3>(count * 4);
            var uvs = new List<Vector2>(count * 4);
            var cols = new List<Color32>(count * 4);
            var tris = new List<int>(count * 6);
            float rad = Radius * 0.96f;
            for (int i = 0; i < count; i++)
            {
                float az = (float)rng.NextDouble() * Mathf.PI * 2f;
                float el = Mathf.Lerp(6f, 86f, Mathf.Pow((float)rng.NextDouble(), 0.75f)) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Cos(el) * Mathf.Cos(az), -Mathf.Sin(el));
                // a quad facing the dome centre (= the camera)
                var right = Vector3.Cross(d, World3D.Up);
                if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
                right.Normalize();
                var up = Vector3.Cross(right, d).normalized;
                float size = Mathf.Lerp(0.9f, 2.6f, Mathf.Pow((float)rng.NextDouble(), 2.2f));
                var c = d * rad;
                right *= size; up *= size;
                int b = verts.Count;
                verts.Add(c - right - up); verts.Add(c - right + up); verts.Add(c + right + up); verts.Add(c + right - up);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                float warm = (float)rng.NextDouble();
                var col = new Color32((byte)(225 + 30 * warm), (byte)(228 + 20 * warm), (byte)(255 - 40 * warm), (byte)(150 + rng.Next(105)));
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            var m = new Mesh { name = "lv_stars_" + group };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            owned.Add(m);
            var go = new GameObject("Stars " + group);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Materials3D.AdditiveFor(WorldTextures.Star);
            Quiet(r);
            r.enabled = false;
            return r;
        }

        public void MarkDirty() => dirty = true;

        /// <summary>Recolours the dome for the time of day (when it changed) and places the discs around the camera.</summary>
        public void Update(Camera cam, DayNight dn, float time)
        {
            if (dirty || Mathf.Abs(dn.Hour - lastHour) > 0.01f)
            {
                dirty = false;
                lastHour = dn.Hour;
                Recolor(dn);
            }
            if (cam == null) return;
            var cp = cam.transform.position;
            root.position = cp;

            float night = dn.NightFactor;
            // ---- sun
            float sv = dn.SunVisibility;
            if (sv > 0.01f)
            {
                var g = dn.SunGlow;
                PlaceDisc(sun, cp, dn.SunSkyDirection, 70f, new Color(1f, Mathf.Lerp(0.95f, g.g, g.a), Mathf.Lerp(0.85f, 0.55f, g.a), 0.55f * sv));
                PlaceDisc(sunCore, cp, dn.SunSkyDirection, 16f, new Color(1f, 0.97f, 0.88f, sv));
            }
            else { sun.enabled = false; sunCore.enabled = false; }
            // ---- moon and stars
            float mv = dn.MoonVisibility;
            if (mv > 0.01f)
            {
                PlaceDisc(moon, cp, dn.MoonSkyDirection, 15f, new Color(1f, 0.97f, 0.9f, mv));
                PlaceDisc(moonGlow, cp, dn.MoonSkyDirection, 60f, new Color(0.7f, 0.78f, 1f, 0.32f * mv));
            }
            else { moon.enabled = false; moonGlow.enabled = false; }
            float starA = Mathf.Clamp01((night - 0.45f) / 0.4f);
            for (int g = 0; g < stars.Length; g++)
            {
                var r = stars[g];
                bool on = starA > 0.01f;
                if (r.enabled != on) r.enabled = on;
                if (!on) continue;
                float tw = 0.62f + 0.38f * Mathf.Sin(time * (1.1f + g * 0.63f) + g * 2.4f);
                block.Clear();
                block.SetColor(Materials3D.ColorId, new Color(1f, 1f, 1f, starA * tw));
                block.SetFloat(Materials3D.FogScaleId, 0f);
                r.SetPropertyBlock(block);
            }
        }

        void PlaceDisc(MeshRenderer r, Vector3 camPos, Vector3 dir, float size, Color color)
        {
            if (!r.enabled) r.enabled = true;
            var t = r.transform;
            t.position = camPos + dir * (Radius * 0.92f);
            // GroundQuad faces −Z: turn its −Z (the face) towards the camera
            t.rotation = Quaternion.LookRotation(dir, World3D.Up);
            t.localScale = new Vector3(size, size, 1f);
            block.Clear();
            block.SetColor(Materials3D.ColorId, color);
            block.SetFloat(Materials3D.FogScaleId, 0f);
            r.SetPropertyBlock(block);
        }

        void Recolor(DayNight dn)
        {
            var top = dn.SkyColor(authoredTop, true);
            var bottom = dn.SkyColor(authoredBottom, false);
            Zenith = top;
            Horizon = bottom;
            var glow = dn.SunGlow;
            var sunDir = dn.SunSkyDirection;
            var below = Color.Lerp(bottom, top, 0.12f) * 0.92f;
            for (int i = 0; i < dirs.Length; i++)
            {
                var d = dirs[i];
                float e = -d.z;   // sin(elevation)
                Color c;
                if (e < 0f) c = Color.Lerp(bottom, below, Mathf.Clamp01(-e * 4f));
                else
                {
                    float k = Mathf.Pow(Mathf.Clamp01(e), 0.55f);
                    c = Color.Lerp(bottom, top, k);
                }
                if (glow.a > 0.001f)
                {
                    float s = Mathf.Clamp01(Vector3.Dot(d, sunDir));
                    float w = s * s * s * s * glow.a * (1f - Mathf.Clamp01(e * 1.6f));
                    c = Color.Lerp(c, glow, Mathf.Clamp01(w));
                }
                colors[i] = c;
            }
            dome.colors32 = colors;
        }
    }
}
