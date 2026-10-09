// Cosy ambient particles for a map (AmbientDef flags): fireflies, drifting leaves, pollen, mist banks, rain (with
// splashes), embers, snow, falling ash (with a few still-glowing flecks), dust motes hanging in the air, drips from a
// cave's roof (with ripples where they land) and, in the fen, will-o'-wisp motes low over the ground — 3D billboards in
// a few pooled batches (one draw call per kind of material). Camera-near
// particles live in a box around the camera's look-at point and wrap around it, so the density stays constant while
// the camera pans; mist banks drift across the whole map; embers rise from fires and forges. Glowing ones are additive,
// leaves / mist / rain are lit (Lanternvale/LitTransparent) so they follow the mood. Simulated on the CPU by MapView's
// LateUpdate, allocation-free.
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class AmbientParticles
    {
        /// <summary>Global density multiplier (graphics option).</summary>
        public static float Density = 1f;

        enum Kind { Firefly, Pollen, Leaf, Mist, Rain, Ember, Snow, Ash, Spark, Dust, Drip, Wisp }

        sealed class P
        {
            public Kind kind;
            public Vector3 pos, vel;
            public float phase, size, age, life, rot, spin, baseAlpha, w, h;
            public Color color;
        }

        sealed class Splash { public Vector3 pos; public float age, life; public bool alive; }

        const float Reach = 17f;   // half size of the box around the look-at point

        readonly List<P> ps = new List<P>();
        readonly List<Splash> splashes = new List<Splash>();
        readonly List<Vector3> emberSources = new List<Vector3>();
        readonly MapView map;
        readonly Transform root;
        readonly System.Random rng;
        BillboardBatch glow, leaves, mist, rain, rings, flakes;
        int splashNext;

        static readonly Color[] LeafColors =
        {
            new Color(0.74f, 0.80f, 0.42f), new Color(0.92f, 0.72f, 0.36f), new Color(0.58f, 0.74f, 0.38f), new Color(0.90f, 0.58f, 0.40f),
        };

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        internal AmbientParticles(MapView view, Transform parent)
        {
            map = view;
            rng = new System.Random(MapTerrain.StableHash((view.Def.id ?? "") + "ambient"));
            root = new GameObject("Ambient Particles").transform;
            root.SetParent(parent, false);
            var a = view.Def.ambient ?? new AmbientDef();
            float d = Mathf.Max(0f, Density);
            float W = Mathf.Max(1f, view.Def.width), D = Mathf.Max(1f, view.Def.depth);
            var c = new Vector3(W * 0.5f, D * 0.5f, 0f);

            int glowCount = 0;
            if (a.fireflies)
                for (int i = 0; i < Mathf.RoundToInt(26 * d); i++)
                {
                    var p = Add(Kind.Firefly);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0.3f, 2.6f));
                    p.size = R(0.16f, 0.26f);
                    p.color = new Color(0.84f, 1f, 0.52f);
                    p.phase = R(0f, 100f);
                    glowCount++;
                }
            if (a.pollen)
                for (int i = 0; i < Mathf.RoundToInt(36 * d); i++)
                {
                    var p = Add(Kind.Pollen);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0.3f, 3.8f));
                    p.size = R(0.06f, 0.11f);
                    p.color = new Color(1f, 0.96f, 0.82f);
                    p.phase = R(0f, 100f);
                    p.vel = new Vector3(R(0.08f, 0.2f), R(0.02f, 0.08f), 0f);
                    glowCount++;
                }
            int leafCount = 0;
            if (a.leaves)
                for (int i = 0; i < Mathf.RoundToInt(16 * d); i++)
                {
                    var p = Add(Kind.Leaf);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0f, 7f));
                    p.size = R(0.2f, 0.3f);
                    p.color = LeafColors[i % LeafColors.Length];
                    p.phase = R(0f, 100f);
                    p.vel = new Vector3(R(0.3f, 0.6f), R(-0.15f, 0.15f), R(0.5f, 0.8f));   // +z = falling
                    p.spin = R(-140f, 140f);
                    p.rot = R(0f, 360f);
                    leafCount++;
                }
            int mistCount = 0;
            if (a.mist)
            {
                // as many banks per square metre on a deep map as on the original 15 m strips
                int n = Mathf.RoundToInt(Mathf.Clamp((4 + W / 9f) * Mathf.Max(1f, D / 15f), 4, 40) * d);
                for (int i = 0; i < n; i++)
                {
                    var p = Add(Kind.Mist);
                    p.w = R(8f, 14f);
                    p.h = p.w * R(0.3f, 0.42f);
                    p.pos = new Vector3(R(-8f, W + 8f), R(-1f, D + 6f), -R(0.5f, 1.4f));
                    p.vel = new Vector3(R(0.12f, 0.3f) * (rng.NextDouble() < 0.5 ? -1f : 1f), 0f, 0f);
                    p.baseAlpha = R(0.16f, 0.26f);
                    p.color = new Color(0.95f, 0.97f, 1f);
                    p.phase = R(0f, 100f);
                    mistCount++;
                }
            }
            int flakeCount = 0;
            if (a.snow)
                for (int i = 0; i < Mathf.RoundToInt(130 * d); i++)
                {
                    var p = Add(Kind.Snow);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0f, 9f));
                    p.size = R(0.05f, 0.12f);
                    p.color = new Color(0.97f, 0.98f, 1f);
                    p.phase = R(0f, 100f);
                    p.vel = new Vector3(R(0.15f, 0.4f), R(-0.1f, 0.1f), R(0.55f, 1.0f));   // +z = falling
                    p.baseAlpha = R(0.7f, 0.95f);
                    flakeCount++;
                }
            if (a.ash)
            {
                for (int i = 0; i < Mathf.RoundToInt(70 * d); i++)
                {
                    var p = Add(Kind.Ash);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0f, 8f));
                    p.size = R(0.06f, 0.13f);
                    float g = R(0.38f, 0.62f);
                    p.color = new Color(g, g * 0.97f, g * 0.95f);
                    p.phase = R(0f, 100f);
                    p.vel = new Vector3(R(0.2f, 0.5f), R(-0.15f, 0.15f), R(0.25f, 0.5f));
                    p.spin = R(-200f, 200f);
                    p.baseAlpha = R(0.6f, 0.9f);
                    flakeCount++;
                }
                // a few flecks still glowing as they drift down
                for (int i = 0; i < Mathf.RoundToInt(14 * d); i++)
                {
                    var p = Add(Kind.Spark);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0f, 8f));
                    p.size = R(0.05f, 0.09f);
                    p.phase = R(0f, 100f);
                    p.vel = new Vector3(R(0.2f, 0.5f), R(-0.15f, 0.15f), R(0.3f, 0.55f));
                    glowCount++;
                }
            }
            if (a.dust)
                for (int i = 0; i < Mathf.RoundToInt(44 * d); i++)
                {
                    var p = Add(Kind.Dust);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0.4f, 4.5f));
                    p.size = R(0.04f, 0.08f);
                    p.color = new Color(1f, 0.93f, 0.8f);
                    p.phase = R(0f, 100f);
                    glowCount++;
                }
            if (Biomes.IdOf(view.Def) == Biomes.Fen)
                for (int i = 0; i < Mathf.RoundToInt(20 * d); i++)
                {
                    // will-o'-wisp motes: pale green-blue lights wandering low over the bog, brighter at night
                    var p = Add(Kind.Wisp);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0.3f, 1.4f));
                    p.size = R(0.14f, 0.24f);
                    p.color = i % 3 == 0 ? new Color(0.72f, 0.95f, 1f) : new Color(0.66f, 1f, 0.78f);
                    p.phase = R(0f, 100f);
                    glowCount++;
                }
            int rainCount = 0;
            if (a.drips)
            {
                // drops falling from the roof of a cave, now and then
                for (int i = 0; i < Mathf.RoundToInt(18 * d); i++)
                {
                    var p = Add(Kind.Drip);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0f, 9f));
                    p.vel = new Vector3(0f, 0f, R(7f, 9f));
                    p.size = R(0.16f, 0.24f);
                    p.color = new Color(0.8f, 0.9f, 1f);
                    p.baseAlpha = R(0.45f, 0.7f);
                    p.life = R(0.5f, 3.5f);   // the wait before it falls again
                    rainCount++;
                }
            }
            if (a.rain)
            {
                for (int i = 0; i < Mathf.RoundToInt(170 * d); i++)
                {
                    var p = Add(Kind.Rain);
                    p.pos = c + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0f, 12f));
                    p.vel = new Vector3(-R(0.8f, 1.4f), R(-0.3f, 0.3f), R(11f, 14f));
                    p.size = R(0.5f, 0.75f);
                    p.color = new Color(0.82f, 0.88f, 0.98f);
                    p.baseAlpha = R(0.3f, 0.5f);
                    rainCount++;
                }
            }
            if (a.rain || a.drips)
                for (int i = 0; i < 24; i++) splashes.Add(new Splash());
            if (a.embers)
            {
                foreach (var prop in view.Def.props)
                    if (prop != null && prop.art != null && (prop.art.Contains("campfire") || prop.art.Contains("smithy") || prop.art.Contains("brazier") || prop.art.Contains("forge")))
                    {
                        float h = prop.art.Contains("campfire") ? 0.45f : 1.0f;
                        emberSources.Add(World3D.At(new Vector2(prop.pos.x, prop.pos.y), h * Mathf.Max(0.3f, prop.scale)));
                    }
                for (int i = 0; i < Mathf.RoundToInt(26 * d); i++)
                {
                    var p = Add(Kind.Ember);
                    RespawnEmber(p, c, true);
                    glowCount++;
                }
            }

            if (glowCount > 0) glow = new BillboardBatch("Glow Motes", root, Materials3D.AdditiveFor(WorldTextures.Glow), glowCount);
            if (leafCount > 0) leaves = new BillboardBatch("Leaves", root, Materials3D.LitTransparent(WorldTextures.Leaf), leafCount, 5);
            if (mistCount > 0) mist = new BillboardBatch("Mist", root, Materials3D.LitTransparent(WorldTextures.Mist), mistCount, 6);
            if (flakeCount > 0) flakes = new BillboardBatch("Flakes", root, Materials3D.LitTransparent(WorldTextures.Dot), flakeCount, 5);
            if (rainCount > 0)
            {
                rain = new BillboardBatch("Rain", root, Materials3D.LitTransparent(WorldTextures.Streak), rainCount, 7);
                rings = new BillboardBatch("Rain Splashes", root, Materials3D.LitTransparent(WorldTextures.Ring), splashes.Count, 4);
            }
        }

        P Add(Kind k)
        {
            var p = new P { kind = k };
            ps.Add(p);
            return p;
        }

        void RespawnEmber(P p, Vector3 around, bool randomAge)
        {
            if (emberSources.Count > 0)
            {
                var s = emberSources[rng.Next(emberSources.Count)];
                p.pos = s + new Vector3(R(-0.3f, 0.3f), R(-0.25f, 0.25f), -R(0f, 0.25f));
                p.vel = new Vector3(R(-0.15f, 0.15f), R(-0.1f, 0.1f), -R(0.55f, 1.0f));
            }
            else
            {
                p.pos = around + new Vector3(R(-Reach, Reach), R(-Reach, Reach), -R(0f, 0.5f));
                p.vel = new Vector3(R(-0.1f, 0.1f), R(-0.1f, 0.1f), -R(0.25f, 0.5f));
            }
            p.size = R(0.06f, 0.11f);
            p.life = R(2.2f, 4f);
            p.age = randomAge ? R(0f, p.life) : 0f;
            p.phase = R(0f, 100f);
        }

        static float Wrap(float v, float centre, float half)
        {
            float span = half * 2f;
            float d = v - centre;
            if (d < -half) v += span * Mathf.Ceil((-half - d) / span);
            else if (d > half) v -= span * Mathf.Ceil((d - half) / span);
            return v;
        }

        static Color32 C(Color c, float a) => new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), (byte)(Mathf.Clamp01(a) * 255f));

        /// <summary>Simulates and redraws every particle (called once per frame by MapView).</summary>
        internal void Update(float dt, float time, Camera cam, Vector3 lookAt)
        {
            if (cam == null || ps.Count == 0) return;
            dt = Mathf.Min(dt, 0.1f);
            var dn = map.DayNight;
            float night = dn != null ? dn.NightFactor : 0f;
            var ct = cam.transform;
            var camPos = ct.position;
            Vector3 right = ct.right, up = ct.up;
            float wind = Mathf.Clamp(SceneLighting.WindStrength / 0.06f, 0.2f, 3f);
            float W = Mathf.Max(1f, map.Def.width);

            if (glow != null) glow.Begin();
            if (leaves != null) leaves.Begin();
            if (mist != null) mist.Begin();
            if (rain != null) rain.Begin();
            if (flakes != null) flakes.Begin();

            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                switch (p.kind)
                {
                    case Kind.Firefly:
                    {
                        float ang = Mathf.PerlinNoise(time * 0.18f + p.phase, p.phase * 0.37f) * Mathf.PI * 4f;
                        float bob = Mathf.Sin(time * 0.9f + p.phase) * 0.12f;
                        p.pos += new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), bob) * (0.34f * dt);
                        p.pos.z = Mathf.Clamp(p.pos.z, -2.8f, -0.25f);
                        p.pos.x = Wrap(p.pos.x, lookAt.x, Reach);
                        p.pos.y = Wrap(p.pos.y, lookAt.y, Reach);
                        float blink = 0.5f + 0.5f * Mathf.Sin(time * 1.6f + p.phase * 3f);
                        blink = blink * blink * blink;
                        float vis = Mathf.Lerp(0.08f, 1f, night);
                        float s = p.size * (0.8f + 0.5f * blink);
                        glow.Add(p.pos, right * s, up * s, C(p.color, vis * (0.2f + 0.8f * blink)));
                        break;
                    }
                    case Kind.Pollen:
                    {
                        p.pos += new Vector3(p.vel.x * wind + Mathf.Sin(time * 0.7f + p.phase) * 0.05f, p.vel.y,
                                             Mathf.Sin(time * 1.1f + p.phase * 2f) * 0.05f) * dt;
                        p.pos.z = Mathf.Clamp(p.pos.z, -4f, -0.2f);
                        p.pos.x = Wrap(p.pos.x, lookAt.x, Reach);
                        p.pos.y = Wrap(p.pos.y, lookAt.y, Reach);
                        float tw = 0.6f + 0.4f * Mathf.Sin(time * 2.3f + p.phase);
                        float vis = Mathf.Lerp(0.5f, 0.12f, night);
                        glow.Add(p.pos, right * p.size, up * p.size, C(p.color, vis * tw));
                        break;
                    }
                    case Kind.Leaf:
                    {
                        float sway = Mathf.Sin(time * 1.3f + p.phase) * 0.55f;
                        p.pos += new Vector3((p.vel.x + sway) * (0.4f + 0.6f * wind), p.vel.y + Mathf.Cos(time * 0.9f + p.phase) * 0.1f,
                                             p.vel.z + Mathf.Cos(time * 1.3f + p.phase) * 0.15f) * dt;
                        if (p.pos.z > -0.02f)
                        {
                            // landed: start again high above a random spot near the view
                            p.pos = new Vector3(lookAt.x + R(-Reach, Reach), lookAt.y + R(-Reach, Reach), -R(5f, 8f));
                        }
                        p.pos.x = Wrap(p.pos.x, lookAt.x, Reach);
                        p.pos.y = Wrap(p.pos.y, lookAt.y, Reach);
                        p.rot += p.spin * dt;
                        float tumble = Mathf.Cos(time * 2.1f + p.phase);
                        float a = p.rot * Mathf.Deg2Rad;
                        float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                        var r = (right * cs + up * sn) * (p.size * 0.5f * (0.25f + 0.75f * Mathf.Abs(tumble)));
                        var u = (up * cs - right * sn) * (p.size * 0.5f);
                        float fadeIn = Mathf.Clamp01((-p.pos.z) / 0.3f);
                        leaves.Add(p.pos, r, u, C(p.color, 0.95f * fadeIn));
                        break;
                    }
                    case Kind.Mist:
                    {
                        p.pos.x += p.vel.x * wind * dt;
                        float minX = -p.w * 0.6f - 8f, maxX = W + p.w * 0.6f + 8f;
                        if (p.pos.x < minX) p.pos.x = maxX; else if (p.pos.x > maxX) p.pos.x = minX;
                        float dist = Vector3.Distance(p.pos, camPos);
                        float near = Mathf.Clamp01((dist - 6f) / 8f);
                        if (near <= 0.01f) break;
                        float breathe = 0.82f + 0.18f * Mathf.Sin(time * 0.21f + p.phase);
                        var pos = p.pos + new Vector3(0f, 0f, Mathf.Sin(time * 0.13f + p.phase) * 0.15f);
                        // lean the bank towards the ground: half camera-up, half along the ground away from the camera
                        var fwd = new Vector3(ct.forward.x, ct.forward.y, 0f);
                        fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.up;
                        var u = (up + fwd).normalized * (p.h * 0.5f);
                        mist.Add(pos, right * (p.w * 0.5f), u, C(p.color, p.baseAlpha * breathe * near * Mathf.Lerp(1f, 0.7f, night)));
                        break;
                    }
                    case Kind.Rain:
                    {
                        p.pos += p.vel * dt;
                        if (p.pos.z > 0f)
                        {
                            if (rng.NextDouble() < 0.4) SpawnSplash(new Vector3(p.pos.x, p.pos.y, -0.02f));
                            p.pos = new Vector3(lookAt.x + R(-Reach, Reach), lookAt.y + R(-Reach, Reach), -R(10f, 13f));
                        }
                        p.pos.x = Wrap(p.pos.x, lookAt.x, Reach);
                        p.pos.y = Wrap(p.pos.y, lookAt.y, Reach);
                        // a streak along its velocity, as wide as it is thin towards the camera
                        var axis = p.vel.normalized;
                        var side = Vector3.Cross(axis, camPos - p.pos);
                        side = side.sqrMagnitude > 1e-6f ? side.normalized * 0.018f : right * 0.018f;
                        rain.Add(p.pos, side, axis * (p.size * 0.5f), C(p.color, p.baseAlpha));
                        break;
                    }
                    case Kind.Snow:
                    {
                        // slow flakes, swaying as they fall; landed ones start again high above a spot near the view
                        float sway = Mathf.Sin(time * 0.9f + p.phase) * 0.35f;
                        p.pos += new Vector3((p.vel.x + sway) * (0.5f + 0.5f * wind), p.vel.y + Mathf.Cos(time * 0.7f + p.phase) * 0.12f, p.vel.z) * dt;
                        if (p.pos.z > -0.02f) p.pos = new Vector3(lookAt.x + R(-Reach, Reach), lookAt.y + R(-Reach, Reach), -R(7f, 9f));
                        p.pos.x = Wrap(p.pos.x, lookAt.x, Reach);
                        p.pos.y = Wrap(p.pos.y, lookAt.y, Reach);
                        float fadeIn = Mathf.Clamp01((-p.pos.z) / 0.25f);
                        flakes.Add(p.pos, right * p.size, up * p.size, C(p.color, p.baseAlpha * fadeIn));
                        break;
                    }
                    case Kind.Ash:
                    {
                        // grey flakes tumbling down on the wind (a flat flake turning: its width flickers)
                        float sway = Mathf.Sin(time * 1.1f + p.phase) * 0.45f;
                        p.pos += new Vector3((p.vel.x + sway) * (0.4f + 0.6f * wind), p.vel.y + Mathf.Cos(time * 0.8f + p.phase) * 0.15f,
                                             p.vel.z + Mathf.Cos(time * 1.7f + p.phase) * 0.1f) * dt;
                        if (p.pos.z > -0.02f) p.pos = new Vector3(lookAt.x + R(-Reach, Reach), lookAt.y + R(-Reach, Reach), -R(6f, 8f));
                        p.pos.x = Wrap(p.pos.x, lookAt.x, Reach);
                        p.pos.y = Wrap(p.pos.y, lookAt.y, Reach);
                        p.rot += p.spin * dt;
                        float a = p.rot * Mathf.Deg2Rad;
                        float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                        float tumble = 0.3f + 0.7f * Mathf.Abs(Mathf.Cos(time * 2.4f + p.phase));
                        var r = (right * cs + up * sn) * (p.size * tumble);
                        var u = (up * cs - right * sn) * (p.size * 0.7f);
                        flakes.Add(p.pos, r, u, C(p.color, p.baseAlpha * Mathf.Clamp01((-p.pos.z) / 0.3f)));
                        break;
                    }
                    case Kind.Spark:
                    {
                        p.pos += new Vector3(p.vel.x * (0.4f + 0.6f * wind) + Mathf.Sin(time * 1.3f + p.phase) * 0.3f, p.vel.y, p.vel.z) * dt;
                        if (p.pos.z > -0.05f) p.pos = new Vector3(lookAt.x + R(-Reach, Reach), lookAt.y + R(-Reach, Reach), -R(6f, 8f));
                        p.pos.x = Wrap(p.pos.x, lookAt.x, Reach);
                        p.pos.y = Wrap(p.pos.y, lookAt.y, Reach);
                        // cooling as it falls: bright orange high up, a dull red near the ground
                        float hot = Mathf.Clamp01((-p.pos.z) / 6f);
                        float flick = 0.7f + 0.3f * Mathf.Sin(time * 9f + p.phase);
                        var col = Color.Lerp(new Color(0.9f, 0.3f, 0.12f), new Color(1f, 0.66f, 0.28f), hot);
                        glow.Add(p.pos, right * p.size, up * p.size, C(col, (0.35f + 0.55f * hot) * flick));
                        break;
                    }
                    case Kind.Dust:
                    {
                        // motes hanging in the still air, wandering slowly; they glint as they turn
                        float ang = Mathf.PerlinNoise(time * 0.07f + p.phase, p.phase * 0.53f) * Mathf.PI * 4f;
                        p.pos += new Vector3(Mathf.Cos(ang) * 0.07f, Mathf.Sin(ang) * 0.07f, Mathf.Sin(time * 0.4f + p.phase) * 0.03f) * dt;
                        p.pos.z = Mathf.Clamp(p.pos.z, -4.8f, -0.3f);
                        p.pos.x = Wrap(p.pos.x, lookAt.x, Reach);
                        p.pos.y = Wrap(p.pos.y, lookAt.y, Reach);
                        float glint = 0.5f + 0.5f * Mathf.Sin(time * 1.3f + p.phase * 5f);
                        glow.Add(p.pos, right * p.size, up * p.size, C(p.color, 0.1f + 0.32f * glint * glint));
                        break;
                    }
                    case Kind.Wisp:
                    {
                        float ang = Mathf.PerlinNoise(time * 0.1f + p.phase, p.phase * 0.41f) * Mathf.PI * 4f;
                        float bob = Mathf.Sin(time * 0.6f + p.phase) * 0.08f;
                        p.pos += new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), bob) * (0.22f * dt);
                        p.pos.z = Mathf.Clamp(p.pos.z, -1.6f, -0.25f);
                        p.pos.x = Wrap(p.pos.x, lookAt.x, Reach);
                        p.pos.y = Wrap(p.pos.y, lookAt.y, Reach);
                        float pulse = 0.5f + 0.5f * Mathf.Sin(time * 0.8f + p.phase * 2f);
                        float vis = Mathf.Lerp(0.18f, 0.95f, night);
                        float s = p.size * (0.85f + 0.3f * pulse);
                        glow.Add(p.pos, right * s, up * s, C(p.color, vis * (0.35f + 0.65f * pulse)));
                        break;
                    }
                    case Kind.Drip:
                    {
                        if (p.life > 0f)
                        {
                            // gathering on the roof: wait, then fall from high above a spot near the view
                            p.life -= dt;
                            if (p.life <= 0f) p.pos = new Vector3(lookAt.x + R(-Reach, Reach), lookAt.y + R(-Reach, Reach), -R(7f, 9.5f));
                            break;
                        }
                        p.pos += p.vel * dt;
                        if (p.pos.z > 0f)
                        {
                            SpawnSplash(new Vector3(p.pos.x, p.pos.y, -0.02f));
                            p.life = R(0.8f, 4f);
                            break;
                        }
                        var axis = p.vel.normalized;
                        var side = Vector3.Cross(axis, camPos - p.pos);
                        side = side.sqrMagnitude > 1e-6f ? side.normalized * 0.02f : right * 0.02f;
                        rain.Add(p.pos, side, axis * (p.size * 0.5f), C(p.color, p.baseAlpha));
                        break;
                    }
                    case Kind.Ember:
                    {
                        p.age += dt;
                        if (p.age >= p.life) RespawnEmber(p, lookAt, false);
                        p.pos += new Vector3(p.vel.x + Mathf.Sin(time * 3.1f + p.phase) * 0.25f, p.vel.y + Mathf.Cos(time * 2.7f + p.phase) * 0.15f, p.vel.z) * dt;
                        float t = p.age / p.life;
                        float a = Mathf.Clamp01(t * 6f) * (1f - t) * (0.7f + 0.3f * Mathf.Sin(time * 11f + p.phase));
                        var col = Color.Lerp(new Color(1f, 0.72f, 0.3f), new Color(1f, 0.38f, 0.18f), t);
                        float s = p.size * (1f - t * 0.5f);
                        glow.Add(p.pos, right * s, up * s, C(col, a));
                        break;
                    }
                }
            }

            if (rings != null)
            {
                rings.Begin();
                for (int i = 0; i < splashes.Count; i++)
                {
                    var s = splashes[i];
                    if (!s.alive) continue;
                    s.age += dt;
                    float t = s.age / s.life;
                    if (t >= 1f) { s.alive = false; continue; }
                    float k = 0.04f + 0.18f * (1f - (1f - t) * (1f - t));
                    rings.Add(s.pos, new Vector3(k, 0f, 0f), new Vector3(0f, k, 0f), C(new Color(0.85f, 0.9f, 1f), 0.5f * (1f - t)));
                }
                rings.End();
            }
            if (glow != null) glow.End();
            if (leaves != null) leaves.End();
            if (mist != null) mist.End();
            if (rain != null) rain.End();
            if (flakes != null) flakes.End();
        }

        void SpawnSplash(Vector3 at)
        {
            if (splashes.Count == 0) return;
            var s = splashes[splashNext];
            splashNext = (splashNext + 1) % splashes.Count;
            s.alive = true;
            s.age = 0f;
            s.life = 0.32f;
            s.pos = at;
        }

        internal void Dispose()
        {
            if (glow != null) glow.Dispose();
            if (leaves != null) leaves.Dispose();
            if (mist != null) mist.Dispose();
            if (rain != null) rain.Dispose();
            if (rings != null) rings.Dispose();
            if (flakes != null) flakes.Dispose();
        }
    }
}
