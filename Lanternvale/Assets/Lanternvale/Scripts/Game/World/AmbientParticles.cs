// Cosy ambient particles for a map (AmbientDef flags): fireflies, drifting leaves, pollen, mist
// banks, rain (with splashes) and embers. Everything is pooled once at build time; one LateUpdate
// simulates all of it on the CPU. Camera-space particles wrap around the view so the density stays
// constant while panning; mist banks drift across the whole map.
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    [DefaultExecutionOrder(1001)]
    public sealed class AmbientParticles : MonoBehaviour
    {
        /// <summary>Global density multiplier (graphics option).</summary>
        public static float Density = 1f;

        enum Kind { Firefly, Pollen, Leaf, Mist, Rain, Ember, Splash }

        sealed class P
        {
            public Transform t;
            public SpriteRenderer sr;
            public Kind kind;
            public Vector2 pos, vel;
            public float phase, size, age, life, rot, spin, baseAlpha, w, h;
            public Color color;
            public bool alive = true;
        }

        readonly List<P> ps = new List<P>();
        readonly List<P> splashes = new List<P>();
        readonly List<Vector2> emberSources = new List<Vector2>();
        MapView map;
        Transform root;
        int splashNext;
        System.Random rng;

        static readonly Color[] LeafColors =
        {
            new Color(0.74f, 0.80f, 0.42f), new Color(0.92f, 0.72f, 0.36f), new Color(0.58f, 0.74f, 0.38f), new Color(0.90f, 0.58f, 0.40f),
        };

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        public void Configure(MapView view)
        {
            map = view;
            rng = new System.Random(((view.Def.id ?? "") + "ambient").GetHashCode());
            root = new GameObject("Ambient Particles").transform;
            root.SetParent(transform, false);
            var a = view.Def.ambient ?? new AmbientDef();
            float d = Mathf.Max(0f, Density);
            var vr = PresentationHost.ViewRect(1.5f);

            int glowOrder = Lighting2D.IsLit ? SortingOrders.Glow + 10 : SortingOrders.NightOverlay + 50;

            if (a.fireflies)
                for (int i = 0; i < Mathf.RoundToInt(18 * d); i++)
                {
                    var p = Make(Kind.Firefly, PresentationArt.Fx("fx_firefly"), glowOrder, true);
                    p.pos = RandomIn(vr);
                    p.size = R(0.14f, 0.22f);
                    p.color = new Color(0.84f, 1f, 0.52f);
                    p.phase = R(0f, 100f);
                }
            if (a.pollen)
                for (int i = 0; i < Mathf.RoundToInt(24 * d); i++)
                {
                    var p = Make(Kind.Pollen, PresentationArt.SoftDot, SortingOrders.Effects - 10, false);
                    p.pos = RandomIn(vr);
                    p.size = R(0.05f, 0.09f);
                    p.color = new Color(1f, 0.97f, 0.84f);
                    p.phase = R(0f, 100f);
                    p.vel = new Vector2(R(0.06f, 0.16f), R(0.03f, 0.1f));
                }
            if (a.leaves)
                for (int i = 0; i < Mathf.RoundToInt(9 * d); i++)
                {
                    var p = Make(Kind.Leaf, PresentationArt.Fx("fx_leaf"), SortingOrders.Effects - 5, false);
                    p.pos = RandomIn(vr);
                    p.size = R(0.2f, 0.3f);
                    p.color = LeafColors[i % LeafColors.Length];
                    p.phase = R(0f, 100f);
                    p.vel = new Vector2(R(0.3f, 0.6f), -R(0.45f, 0.7f));
                    p.spin = R(-120f, 120f);
                    p.rot = R(0f, 360f);
                }
            if (a.mist)
            {
                int n = Mathf.RoundToInt(Mathf.Clamp(3 + view.Def.width / 12f, 3, 10) * d);
                for (int i = 0; i < n; i++)
                {
                    var p = Make(Kind.Mist, PresentationArt.Mist, 0, false);
                    p.w = R(7f, 13f);
                    p.h = p.w * R(0.26f, 0.36f);
                    p.pos = new Vector2(R(-6f, view.Def.width + 6f), R(0.4f, view.Def.depth + 0.8f));
                    p.vel = new Vector2(R(0.12f, 0.3f) * (rng.NextDouble() < 0.5 ? -1f : 1f), 0f);
                    p.baseAlpha = R(0.10f, 0.18f);
                    p.color = new Color(0.95f, 0.97f, 1f);
                    p.phase = R(0f, 100f);
                    PresentationArt.SetSize(p.t, p.sr.sprite, p.w, p.h);
                    p.sr.flipX = rng.NextDouble() < 0.5;
                    p.sr.sortingOrder = SortingOrders.ForY(p.pos.y - p.h * 0.25f);
                }
            }
            if (a.rain)
            {
                for (int i = 0; i < Mathf.RoundToInt(110 * d); i++)
                {
                    var p = Make(Kind.Rain, PresentationArt.RainStreak, SortingOrders.Effects - 2, false);
                    p.pos = RandomIn(vr);
                    p.vel = new Vector2(-R(1.2f, 2f), -R(11f, 14f));
                    p.size = R(0.45f, 0.7f);
                    p.color = new Color(0.82f, 0.88f, 0.98f);
                    p.baseAlpha = R(0.22f, 0.38f);
                    float ang = Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg + 90f;
                    p.t.localRotation = Quaternion.Euler(0f, 0f, ang);
                    PresentationArt.SetSize(p.t, p.sr.sprite, 0.035f, p.size);
                }
                for (int i = 0; i < 14; i++)
                {
                    var s = Make(Kind.Splash, PresentationArt.Ring(4, 64), SortingOrders.Shadow + 5, false);
                    s.alive = false;
                    s.sr.enabled = false;
                    s.color = new Color(0.85f, 0.9f, 1f);
                    ps.Remove(s);
                    splashes.Add(s);
                }
            }
            if (a.embers)
            {
                foreach (var prop in view.Def.props)
                    if (prop != null && prop.art != null && (prop.art.Contains("campfire") || prop.art.Contains("smithy") || prop.art.Contains("brazier") || prop.art.Contains("forge")))
                        emberSources.Add(new Vector2(prop.pos.x, prop.pos.y + 0.35f * Mathf.Max(0.3f, prop.scale)));
                for (int i = 0; i < Mathf.RoundToInt(16 * d); i++)
                {
                    var p = Make(Kind.Ember, PresentationArt.SoftDot, glowOrder, true);
                    RespawnEmber(p, vr, true);
                }
            }
        }

        P Make(Kind k, Sprite s, int order, bool unlit)
        {
            var sr = PresentationArt.NewRenderer(k.ToString(), root, s, order, unlit);
            var p = new P { t = sr.transform, sr = sr, kind = k };
            ps.Add(p);
            return p;
        }

        Vector2 RandomIn(Rect r) => new Vector2(R(r.xMin, r.xMax), R(r.yMin, r.yMax));

        void RespawnEmber(P p, Rect view, bool randomAge)
        {
            if (emberSources.Count > 0)
            {
                var s = emberSources[rng.Next(emberSources.Count)];
                p.pos = s + new Vector2(R(-0.35f, 0.35f), R(0f, 0.3f));
                p.vel = new Vector2(R(-0.15f, 0.15f), R(0.55f, 1.0f));
            }
            else
            {
                p.pos = new Vector2(R(view.xMin, view.xMax), R(view.yMin, view.yMin + view.height * 0.6f));
                p.vel = new Vector2(R(-0.1f, 0.1f), R(0.25f, 0.5f));
            }
            p.size = R(0.05f, 0.1f);
            p.life = R(2.2f, 4f);
            p.age = randomAge ? R(0f, p.life) : 0f;
            p.phase = R(0f, 100f);
        }

        void LateUpdate()
        {
            if (map == null || ps.Count == 0 && splashes.Count == 0) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            float time = Time.time;
            float night = map.DayNight != null ? map.DayNight.NightFactor : 0f;
            var vr = PresentationHost.ViewRect(1.5f);
            var airTop = Mathf.Min(vr.yMax, map.Def.depth + 3f);
            var air = Rect.MinMaxRect(vr.xMin, vr.yMin, vr.xMax, Mathf.Max(vr.yMin + 0.01f, airTop));
            bool airVisible = airTop > vr.yMin + 1f;
            float wind = SwayManager.Wind;

            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                switch (p.kind)
                {
                    case Kind.Firefly:
                    {
                        float ang = Mathf.PerlinNoise(time * 0.18f + p.phase, p.phase * 0.37f) * Mathf.PI * 4f;
                        p.pos += new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.7f) * (0.32f * dt);
                        p.pos = Wrap(p.pos, air);
                        float blink = 0.5f + 0.5f * Mathf.Sin(time * 1.6f + p.phase * 3f);
                        blink = blink * blink * blink;
                        float vis = Mathf.Lerp(0.12f, 1f, night) * (airVisible ? 1f : 0f);
                        float s = p.size * (0.8f + 0.35f * blink);
                        p.t.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);
                        p.t.localScale = new Vector3(s, s, 1f);
                        p.sr.color = new Color(p.color.r, p.color.g, p.color.b, vis * (0.2f + 0.8f * blink));
                        break;
                    }
                    case Kind.Pollen:
                    {
                        p.pos += new Vector2(p.vel.x * wind + Mathf.Sin(time * 0.7f + p.phase) * 0.05f, p.vel.y + Mathf.Sin(time * 1.1f + p.phase * 2f) * 0.04f) * dt;
                        p.pos = Wrap(p.pos, air);
                        float tw = 0.6f + 0.4f * Mathf.Sin(time * 2.3f + p.phase);
                        float vis = Mathf.Lerp(0.55f, 0.15f, night) * (airVisible ? 1f : 0f);
                        p.t.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);
                        p.t.localScale = new Vector3(p.size, p.size, 1f);
                        p.sr.color = new Color(p.color.r, p.color.g, p.color.b, vis * tw);
                        break;
                    }
                    case Kind.Leaf:
                    {
                        float sway = Mathf.Sin(time * 1.3f + p.phase) * 0.55f;
                        p.pos += new Vector2((p.vel.x + sway) * (0.4f + 0.6f * wind), p.vel.y + Mathf.Cos(time * 1.3f + p.phase) * 0.12f) * dt;
                        p.pos = Wrap(p.pos, vr);
                        p.rot += p.spin * dt;
                        float tumble = Mathf.Cos(time * 2.1f + p.phase);
                        p.t.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);
                        p.t.localRotation = Quaternion.Euler(0f, 0f, p.rot);
                        p.t.localScale = new Vector3(p.size * (0.25f + 0.75f * Mathf.Abs(tumble)), p.size, 1f);
                        p.sr.color = new Color(p.color.r, p.color.g, p.color.b, 0.92f);
                        break;
                    }
                    case Kind.Mist:
                    {
                        p.pos.x += p.vel.x * wind * dt;
                        float minX = -p.w * 0.6f - 2f, maxX = map.Def.width + p.w * 0.6f + 2f;
                        if (p.pos.x < minX) p.pos.x = maxX; else if (p.pos.x > maxX) p.pos.x = minX;
                        if (p.pos.x + p.w * 0.6f < vr.xMin || p.pos.x - p.w * 0.6f > vr.xMax) { if (p.sr.enabled) p.sr.enabled = false; break; }
                        if (!p.sr.enabled) p.sr.enabled = true;
                        float breathe = 0.82f + 0.18f * Mathf.Sin(time * 0.21f + p.phase);
                        p.t.localPosition = new Vector3(p.pos.x, p.pos.y + Mathf.Sin(time * 0.13f + p.phase) * 0.15f, 0f);
                        p.sr.color = new Color(p.color.r, p.color.g, p.color.b, p.baseAlpha * breathe * Mathf.Lerp(1f, 0.75f, night));
                        break;
                    }
                    case Kind.Rain:
                    {
                        p.pos += p.vel * dt;
                        if (p.pos.y < vr.yMin)
                        {
                            if (rng.NextDouble() < 0.35) Splash(new Vector2(R(vr.xMin, vr.xMax), R(vr.yMin, Mathf.Min(vr.yMax, map.Def.depth))));
                            p.pos = new Vector2(R(vr.xMin, vr.xMax), vr.yMax + R(0f, 2f));
                        }
                        p.pos = Wrap(p.pos, Rect.MinMaxRect(vr.xMin, vr.yMin - 1f, vr.xMax, vr.yMax + 3f));
                        p.t.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);
                        p.sr.color = new Color(p.color.r, p.color.g, p.color.b, p.baseAlpha);
                        break;
                    }
                    case Kind.Ember:
                    {
                        p.age += dt;
                        if (p.age >= p.life) RespawnEmber(p, vr, false);
                        p.pos += new Vector2(p.vel.x + Mathf.Sin(time * 3.1f + p.phase) * 0.25f, p.vel.y) * dt;
                        float t = p.age / p.life;
                        float a = Mathf.Clamp01(t * 6f) * (1f - t) * (0.7f + 0.3f * Mathf.Sin(time * 11f + p.phase));
                        var c = Color.Lerp(new Color(1f, 0.72f, 0.3f), new Color(1f, 0.38f, 0.18f), t);
                        float s = p.size * (1f - t * 0.5f);
                        p.t.localPosition = new Vector3(p.pos.x, p.pos.y, 0f);
                        p.t.localScale = new Vector3(s, s, 1f);
                        p.sr.color = new Color(c.r, c.g, c.b, a);
                        break;
                    }
                }
            }

            for (int i = 0; i < splashes.Count; i++)
            {
                var s = splashes[i];
                if (!s.alive) continue;
                s.age += dt;
                float t = s.age / s.life;
                if (t >= 1f) { s.alive = false; s.sr.enabled = false; continue; }
                float k = 0.06f + 0.3f * (1f - (1f - t) * (1f - t));
                s.t.localScale = new Vector3(k, k * 0.45f, 1f);
                s.sr.color = new Color(s.color.r, s.color.g, s.color.b, 0.45f * (1f - t));
            }
        }

        void Splash(Vector2 at)
        {
            if (splashes.Count == 0) return;
            var s = splashes[splashNext];
            splashNext = (splashNext + 1) % splashes.Count;
            s.alive = true;
            s.age = 0f;
            s.life = 0.32f;
            s.pos = at;
            s.t.localPosition = new Vector3(at.x, at.y, 0f);
            s.sr.enabled = true;
        }

        static Vector2 Wrap(Vector2 p, Rect r)
        {
            if (r.width <= 0f || r.height <= 0f) return p;
            if (p.x < r.xMin) p.x += r.width * Mathf.Ceil((r.xMin - p.x) / r.width);
            else if (p.x > r.xMax) p.x -= r.width * Mathf.Ceil((p.x - r.xMax) / r.width);
            if (p.y < r.yMin) p.y += r.height * Mathf.Ceil((r.yMin - p.y) / r.height);
            else if (p.y > r.yMax) p.y -= r.height * Mathf.Ceil((p.y - r.yMax) / r.height);
            return p;
        }
    }
}
