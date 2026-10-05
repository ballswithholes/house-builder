// Pooled, sprite-based combat effects. No custom shaders and no LineRenderers: every effect is a
// handful of tinted white sprites (procedural or fx_* art) simulated on the CPU by one Update.
//
//   Projectile  arrows arc, bolts fly with a glowing trail of fading dots; onHit on arrival
//   Impact      flash + ring + school-flavoured particles (sparks, embers, shards, leaves, wisps…)
//   Burst       area explosion: flash, expanding ground ring, fill, particles, gentle camera shake
//   Beam        channelled beam with flickering width and sparkles travelling along it
//   HealSparkles, AuraPulse, Slash, GroundRing, Sparkles, Puff
//   Targeting previews (circle, cone, line, path, move range) live in FxPreviews.cs
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    [DefaultExecutionOrder(200)]
    public sealed partial class FxSystem : MonoBehaviour
    {
        public static FxSystem Instance { get; private set; }

        /// <summary>Bursts shake the camera a little (disable for accessibility).</summary>
        public static bool ShakeOnBurst = true;
        /// <summary>Global multiplier on particle counts (graphics option).</summary>
        public static float ParticleDensity = 1f;

        /// <summary>Interim: a 3D world point drawn by the 2.5D sprite effects (height lifts it up the screen).</summary>
        static Vector2 Legacy(Vector3 p) => new Vector2(p.x, p.y - p.z);

        public static FxSystem Get()
        {
            if (Instance == null) Instance = PresentationHost.Ensure<FxSystem>();
            return Instance;
        }

        /// <summary>Sorting order for airborne effects: above units; above the night overlay in unlit mode so spells glow.</summary>
        public static int AirOrder => PresentationArt.SpritesLit ? SortingOrders.Effects : SortingOrders.NightOverlay + 200;
        /// <summary>Sorting order for effects lying on the ground (under units).</summary>
        public const int GroundOrder = SortingOrders.Shadow + 60;

        // ================================================================== pool

        sealed class Fx
        {
            public Transform t;
            public SpriteRenderer sr;
            public bool inUse;
        }

        readonly Stack<Fx> free = new Stack<Fx>();
        Transform poolRoot;
        System.Random rng = new System.Random(1234);

        void Awake()
        {
            Instance = this;
            poolRoot = new GameObject("FX").transform;
            poolRoot.SetParent(transform, false);
        }

        Fx Acquire(Sprite s, int order)
        {
            Fx f = free.Count > 0 ? free.Pop() : null;
            if (f == null || f.t == null)
            {
                var sr = PresentationArt.NewRenderer("fx", poolRoot, s, order, true);
                f = new Fx { t = sr.transform, sr = sr };
            }
            f.inUse = true;
            f.sr.sprite = s;
            f.sr.sortingOrder = order;
            f.sr.enabled = true;
            f.sr.flipX = false;
            f.sr.drawMode = SpriteDrawMode.Simple;
            f.t.localRotation = Quaternion.identity;
            f.t.localScale = Vector3.one;
            return f;
        }

        void Release(Fx f)
        {
            if (f == null || !f.inUse) return;
            f.inUse = false;
            if (f.sr != null) f.sr.enabled = false;
            free.Push(f);
        }

        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static void Size(Fx f, Vector2 bounds, float w, float h)
        {
            f.t.localScale = new Vector3(w / Mathf.Max(1e-4f, bounds.x), h / Mathf.Max(1e-4f, bounds.y), 1f);
        }

        // ================================================================== particles

        enum Curve { Linear, EaseOut, Pop }

        sealed class Particle
        {
            public Fx fx;
            public Vector2 bounds;
            public Vector2 pos, vel;
            public float gravity, drag, age, life, rot, spin, fadeIn, twinkle, phase;
            public Vector2 size0, size1;
            public Color c0, c1;
            public Curve curve;
            public bool faceVel;
        }

        readonly List<Particle> particles = new List<Particle>();
        readonly Stack<Particle> freeParticles = new Stack<Particle>();

        Particle Emit(Sprite s, int order, Vector2 pos, Vector2 vel, float life, float size0, float size1, Color c0, Color c1, Curve curve = Curve.Linear)
        {
            var p = freeParticles.Count > 0 ? freeParticles.Pop() : new Particle();
            p.fx = Acquire(s, order);
            p.bounds = s.bounds.size;
            p.pos = pos; p.vel = vel; p.life = Mathf.Max(0.01f, life); p.age = 0f;
            p.size0 = new Vector2(size0, size0); p.size1 = new Vector2(size1, size1);
            p.c0 = c0; p.c1 = c1; p.curve = curve;
            p.gravity = 0f; p.drag = 0f; p.rot = 0f; p.spin = 0f; p.fadeIn = 0f; p.twinkle = 0f; p.phase = R(0f, 10f);
            p.faceVel = false;
            p.fx.t.position = new Vector3(pos.x, pos.y, 0f);
            Size(p.fx, p.bounds, size0, size0);
            p.fx.sr.color = c0;
            particles.Add(p);
            return p;
        }

        void UpdateParticles(float dt)
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.age += dt;
                float t = p.age / p.life;
                if (t >= 1f || p.fx.t == null)
                {
                    Release(p.fx);
                    p.fx = null;
                    particles[i] = particles[particles.Count - 1];
                    particles.RemoveAt(particles.Count - 1);
                    freeParticles.Push(p);
                    continue;
                }
                if (p.drag > 0f) p.vel *= Mathf.Max(0f, 1f - p.drag * dt);
                p.vel.y -= p.gravity * dt;
                p.pos += p.vel * dt;
                p.rot += p.spin * dt;
                float k = t;
                if (p.curve == Curve.EaseOut) k = 1f - (1f - t) * (1f - t);
                else if (p.curve == Curve.Pop) k = t < 0.25f ? t / 0.25f : 1f;
                var size = Vector2.Lerp(p.size0, p.size1, k);
                var c = Color.Lerp(p.c0, p.c1, t);
                if (p.fadeIn > 0f && p.age < p.fadeIn) c.a *= p.age / p.fadeIn;
                if (p.twinkle > 0f) c.a *= 1f - p.twinkle * (0.5f + 0.5f * Mathf.Sin(p.age * 18f + p.phase));
                var tr = p.fx.t;
                tr.position = new Vector3(p.pos.x, p.pos.y, 0f);
                float ang = p.faceVel && p.vel.sqrMagnitude > 1e-6f ? Mathf.Atan2(p.vel.y, p.vel.x) * Mathf.Rad2Deg : p.rot;
                tr.localRotation = Quaternion.Euler(0f, 0f, ang);
                Size(p.fx, p.bounds, size.x, size.y);
                p.fx.sr.color = c;
            }
        }

        // ================================================================== projectiles

        sealed class ProjectileFx
        {
            public Fx head, glow;
            public Vector2 headBounds;
            public Vector2 from, to;
            public float dur, t, arc, trailTimer, length;
            public Color color;
            public System.Action onHit;
            public School school;
            public bool arrow;
        }

        readonly List<ProjectileFx> projectiles = new List<ProjectileFx>();
        readonly List<System.Action> pendingHits = new List<System.Action>();
        readonly List<System.Action> runningHits = new List<System.Action>();

        /// <summary>
        /// Fires a projectile. artKey "" picks fx_arrow for Physical and fx_bolt otherwise. Arrows
        /// arc; bolts fly almost straight with a glowing trail. onHit runs on arrival (after this
        /// frame's FX update). Returns the flight time in seconds.
        /// </summary>
        public static float Projectile(Vector3 from, Vector3 to, School school, string artKey = "", float speed = 14f, System.Action onHit = null)
            => Get().FireProjectile(Legacy(from), Legacy(to), school, artKey, speed, onHit);

        float FireProjectile(Vector2 from, Vector2 to, School school, string artKey, float speed, System.Action onHit)
        {
            if (string.IsNullOrEmpty(artKey)) artKey = school == School.Physical ? "fx_arrow" : "fx_bolt";
            bool arrow = artKey.Contains("arrow") || artKey.Contains("dagger") || artKey.Contains("axe");
            var sprite = PresentationArt.Fx(artKey);
            float dist = Vector2.Distance(from, to);
            speed = speed > 0f ? speed : 14f;
            var color = Ui.SchoolColor(school);
            var p = new ProjectileFx
            {
                head = Acquire(sprite, AirOrder + 5),
                headBounds = sprite.bounds.size,
                from = from,
                to = to,
                dur = Mathf.Max(0.08f, dist / speed),
                arc = arrow ? Mathf.Clamp(dist * 0.16f, 0.2f, 2.2f) : Mathf.Clamp(dist * 0.05f, 0f, 0.6f),
                color = color,
                onHit = onHit,
                school = school,
                arrow = arrow,
                length = arrow ? 0.85f : 0.62f,
            };
            float aspect = p.headBounds.x / Mathf.Max(1e-4f, p.headBounds.y);
            Size(p.head, p.headBounds, p.length, p.length / Mathf.Max(0.5f, aspect));
            p.head.sr.color = arrow ? Color.Lerp(Color.white, color, 0.35f) : Color.Lerp(color, Color.white, 0.35f);
            if (!arrow)
            {
                p.glow = Acquire(PresentationArt.Glow, AirOrder + 4);
                p.glow.t.localScale = new Vector3(1.1f, 1.1f, 1f);
                p.glow.sr.color = PresentationArt.WithAlpha(color, 0.55f);
            }
            projectiles.Add(p);
            PlaceProjectile(p, 0f);
            return p.dur;
        }

        Vector2 ProjectilePos(ProjectileFx p, float t) => Vector2.Lerp(p.from, p.to, t) + new Vector2(0f, p.arc * 4f * t * (1f - t));

        void PlaceProjectile(ProjectileFx p, float t)
        {
            var pos = ProjectilePos(p, t);
            var ahead = ProjectilePos(p, Mathf.Min(1f, t + 0.02f)) - ProjectilePos(p, Mathf.Max(0f, t - 0.02f));
            float ang = ahead.sqrMagnitude > 1e-8f ? Mathf.Atan2(ahead.y, ahead.x) * Mathf.Rad2Deg : 0f;
            p.head.t.position = new Vector3(pos.x, pos.y, 0f);
            p.head.t.localRotation = Quaternion.Euler(0f, 0f, ang);
            if (p.glow != null) p.glow.t.position = p.head.t.position;
        }

        void UpdateProjectiles(float dt)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                p.t += dt / p.dur;
                float t = Mathf.Min(1f, p.t);
                PlaceProjectile(p, t);
                p.trailTimer -= dt;
                if (p.trailTimer <= 0f && t < 1f)
                {
                    p.trailTimer = p.arrow ? 0.03f : 0.018f;
                    var pos = ProjectilePos(p, t);
                    if (p.arrow)
                        Emit(PresentationArt.SoftDot, AirOrder + 3, pos, Vector2.zero, 0.22f, 0.07f, 0.02f, new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0f));
                    else
                    {
                        var c = p.color;
                        var q = Emit(PresentationArt.SoftDot, AirOrder + 3, pos + new Vector2(R(-0.04f, 0.04f), R(-0.04f, 0.04f)), new Vector2(R(-0.2f, 0.2f), R(-0.1f, 0.3f)),
                            R(0.25f, 0.4f), R(0.16f, 0.24f), 0.03f, PresentationArt.WithAlpha(Color.Lerp(c, Color.white, 0.3f), 0.8f), PresentationArt.WithAlpha(c, 0f));
                        if (p.school == School.Fire) q.vel.y += 0.6f;
                    }
                }
                if (p.t >= 1f)
                {
                    Release(p.head);
                    Release(p.glow);
                    projectiles.RemoveAt(i);
                    if (!p.arrow) DoImpact(p.to, p.school, 0.8f);
                    else Emit(PresentationArt.Smoke, AirOrder, p.to, Vector2.zero, 0.35f, 0.15f, 0.45f, new Color(0.9f, 0.85f, 0.75f, 0.45f), new Color(0.9f, 0.85f, 0.75f, 0f), Curve.EaseOut);
                    if (p.onHit != null) pendingHits.Add(p.onHit);
                }
            }
        }

        // ================================================================== beams

        sealed class BeamFx
        {
            public int id;
            public Fx core, outer, a, b;
            public Vector2 from, to;
            public float age, dur, sparkTimer;
            public Color color;
            public School school;
            public bool stopping;
        }

        readonly List<BeamFx> beams = new List<BeamFx>();
        int nextBeamId = 1;

        /// <summary>Channel beam from → to for duration seconds. Returns a handle for StopBeam/MoveBeam.</summary>
        public static int Beam(Vector3 from, Vector3 to, School school, float duration) => Get().StartBeam(Legacy(from), Legacy(to), school, duration);

        /// <summary>Ends a beam early (fades out).</summary>
        public static void StopBeam(int handle)
        {
            if (Instance == null) return;
            foreach (var b in Instance.beams)
                if (b.id == handle && !b.stopping) { b.stopping = true; b.dur = b.age + 0.25f; }
        }

        /// <summary>Moves the beam's endpoints (units moved while channelling).</summary>
        public static void MoveBeam(int handle, Vector3 from, Vector3 to)
        {
            if (Instance == null) return;
            foreach (var b in Instance.beams)
                if (b.id == handle) { b.from = Legacy(from); b.to = Legacy(to); }
        }

        int StartBeam(Vector2 from, Vector2 to, School school, float duration)
        {
            var c = Ui.SchoolColor(school);
            var b = new BeamFx
            {
                id = nextBeamId++,
                from = from,
                to = to,
                dur = Mathf.Max(0.2f, duration),
                color = c,
                school = school,
                outer = Acquire(PresentationArt.BeamLine, AirOrder + 1),
                core = Acquire(PresentationArt.BeamLine, AirOrder + 2),
                a = Acquire(PresentationArt.Glow, AirOrder + 3),
                b = Acquire(PresentationArt.Glow, AirOrder + 3),
            };
            beams.Add(b);
            UpdateBeam(b, 0f);
            return b.id;
        }

        void UpdateBeam(BeamFx b, float dt)
        {
            b.age += dt;
            float fade = Mathf.Clamp01(b.age / 0.12f) * Mathf.Clamp01((b.dur - b.age) / 0.25f);
            var d = b.to - b.from;
            float len = Mathf.Max(0.01f, d.magnitude);
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float flick = 0.8f + 0.2f * Mathf.PerlinNoise(b.age * 9f, b.id * 1.7f) + 0.08f * Mathf.Sin(b.age * 31f);
            var bl = PresentationArt.BeamLine.bounds.size;
            var rot = Quaternion.Euler(0f, 0f, ang);
            var start = new Vector3(b.from.x, b.from.y, 0f);
            b.outer.t.position = start; b.outer.t.localRotation = rot; Size(b.outer, bl, len, 0.55f * flick);
            b.core.t.position = start; b.core.t.localRotation = rot; Size(b.core, bl, len, 0.16f * flick);
            b.outer.sr.color = PresentationArt.WithAlpha(b.color, 0.5f * fade);
            b.core.sr.color = PresentationArt.WithAlpha(Color.Lerp(b.color, Color.white, 0.55f), 0.95f * fade);
            b.a.t.position = start; b.a.t.localScale = new Vector3(0.7f * flick, 0.7f * flick, 1f);
            b.b.t.position = new Vector3(b.to.x, b.to.y, 0f); b.b.t.localScale = new Vector3(1.1f * flick, 1.1f * flick, 1f);
            b.a.sr.color = PresentationArt.WithAlpha(b.color, 0.6f * fade);
            b.b.sr.color = PresentationArt.WithAlpha(b.color, 0.75f * fade);
            b.sparkTimer -= dt;
            if (b.sparkTimer <= 0f && fade > 0.2f)
            {
                b.sparkTimer = 0.045f;
                var dir = d / len;
                var p = Emit(PresentationArt.SoftDot, AirOrder + 3, b.from + d * R(0f, 0.2f) + new Vector2(-dir.y, dir.x) * R(-0.08f, 0.08f),
                    dir * (len / R(0.35f, 0.55f)), R(0.3f, 0.5f), R(0.1f, 0.16f), 0.05f,
                    PresentationArt.WithAlpha(Color.Lerp(b.color, Color.white, 0.4f), 0.9f), PresentationArt.WithAlpha(b.color, 0f));
                p.drag = 0.4f;
                if (rng.NextDouble() < 0.3) Emit(PresentationArt.Spark, AirOrder + 4, b.to + new Vector2(R(-0.2f, 0.2f), R(-0.2f, 0.2f)),
                    new Vector2(R(-0.8f, 0.8f), R(0f, 1.2f)), 0.3f, 0.25f, 0.05f, Color.Lerp(b.color, Color.white, 0.5f), PresentationArt.WithAlpha(b.color, 0f));
            }
        }

        // ================================================================== one-shot effects

        /// <summary>Small hit: flash, ring and school-flavoured particles.</summary>
        public static void Impact(Vector3 pos, School school) => Get().DoImpact(Legacy(pos), school, 1f);

        void DoImpact(Vector2 pos, School school, float scale)
        {
            var c = Ui.SchoolColor(school);
            var light = Color.Lerp(c, Color.white, 0.45f);
            Emit(PresentationArt.Glow, AirOrder + 1, pos, Vector2.zero, 0.3f, 0.35f * scale, 1.5f * scale, PresentationArt.WithAlpha(light, 0.9f), PresentationArt.WithAlpha(c, 0f), Curve.EaseOut);
            Emit(PresentationArt.Ring(4, 128), AirOrder + 2, pos, Vector2.zero, 0.32f, 0.2f * scale, 1.3f * scale, PresentationArt.WithAlpha(light, 0.85f), PresentationArt.WithAlpha(c, 0f), Curve.EaseOut);
            SchoolParticles(pos, school, Mathf.RoundToInt(8 * scale * ParticleDensity), 0.35f * scale);
        }

        void SchoolParticles(Vector2 pos, School school, int n, float spread)
        {
            var c = Ui.SchoolColor(school);
            var light = Color.Lerp(c, Color.white, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float ang = R(0f, Mathf.PI * 2f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var at = pos + dir * R(0f, spread);
                Particle p;
                switch (school)
                {
                    case School.Fire:
                        p = Emit(PresentationArt.SoftDot, AirOrder + 3, at, dir * R(1f, 2.6f) + new Vector2(0f, 0.8f), R(0.4f, 0.8f), R(0.1f, 0.18f), 0.03f, new Color(1f, 0.85f, 0.45f, 1f), new Color(1f, 0.35f, 0.15f, 0f));
                        p.gravity = -1.6f; p.drag = 2.5f; p.twinkle = 0.3f;
                        if (i % 3 == 0) { var s = Emit(PresentationArt.Smoke, AirOrder, at, new Vector2(0f, R(0.4f, 0.8f)), R(0.6f, 0.9f), 0.25f, 0.7f, new Color(0.35f, 0.28f, 0.3f, 0.35f), new Color(0.35f, 0.28f, 0.3f, 0f), Curve.EaseOut); s.spin = R(-60f, 60f); }
                        break;
                    case School.Frost:
                        p = Emit(PresentationArt.Shard, AirOrder + 3, at, dir * R(1.5f, 3f) + new Vector2(0f, 1.2f), R(0.45f, 0.7f), R(0.14f, 0.24f), 0.08f, PresentationArt.WithAlpha(light, 1f), PresentationArt.WithAlpha(c, 0f));
                        p.gravity = 6f; p.drag = 1f; p.faceVel = true;
                        break;
                    case School.Nature:
                        p = Emit(PresentationArt.Fx("fx_leaf"), AirOrder + 3, at, dir * R(0.8f, 1.8f) + new Vector2(0f, 0.6f), R(0.6f, 1f), R(0.16f, 0.24f), 0.12f, PresentationArt.WithAlpha(light, 1f), PresentationArt.WithAlpha(c, 0f));
                        p.gravity = 1.2f; p.drag = 2f; p.spin = R(-240f, 240f);
                        break;
                    case School.Shadow:
                        p = Emit(PresentationArt.Smoke, AirOrder + 2, at, dir * R(0.4f, 1f) + new Vector2(0f, 0.5f), R(0.6f, 1f), R(0.2f, 0.3f), R(0.5f, 0.8f), new Color(c.r * 0.6f, c.g * 0.5f, c.b * 0.7f, 0.6f), PresentationArt.WithAlpha(c, 0f), Curve.EaseOut);
                        p.spin = R(-90f, 90f); p.drag = 1.5f;
                        break;
                    case School.Holy:
                        p = Emit(PresentationArt.Spark, AirOrder + 3, at, dir * R(0.6f, 1.6f) + new Vector2(0f, 0.7f), R(0.5f, 0.9f), R(0.2f, 0.34f), 0.05f, new Color(1f, 0.97f, 0.8f, 1f), PresentationArt.WithAlpha(c, 0f));
                        p.drag = 2f; p.twinkle = 0.4f;
                        break;
                    case School.Arcane:
                        p = Emit(PresentationArt.Spark, AirOrder + 3, at, dir * R(1f, 2.2f), R(0.4f, 0.7f), R(0.18f, 0.3f), 0.04f, PresentationArt.WithAlpha(light, 1f), PresentationArt.WithAlpha(c, 0f));
                        p.drag = 3f; p.spin = R(-300f, 300f); p.twinkle = 0.5f;
                        break;
                    default:
                        p = Emit(PresentationArt.SoftDot, AirOrder + 3, at, dir * R(2f, 4f) + new Vector2(0f, 1f), R(0.2f, 0.35f), R(0.06f, 0.1f), 0.02f, new Color(1f, 0.98f, 0.9f, 1f), new Color(1f, 0.9f, 0.7f, 0f));
                        p.gravity = 8f; p.drag = 1f;
                        if (i % 3 == 0) Emit(PresentationArt.Smoke, AirOrder, at, dir * 0.4f, R(0.45f, 0.7f), 0.2f, 0.55f, new Color(0.88f, 0.82f, 0.7f, 0.45f), new Color(0.88f, 0.82f, 0.7f, 0f), Curve.EaseOut);
                        break;
                }
            }
        }

        /// <summary>Area explosion of the given radius (metres) centred on pos.</summary>
        public static void Burst(Vector2 pos, float radius, School school) => Get().DoBurst(pos, radius, school);

        void DoBurst(Vector2 pos, float radius, School school)
        {
            radius = Mathf.Max(0.3f, radius);
            var c = Ui.SchoolColor(school);
            var light = Color.Lerp(c, Color.white, 0.45f);
            Emit(PresentationArt.Glow, AirOrder + 1, pos, Vector2.zero, 0.45f, radius * 0.6f, radius * 2.6f, PresentationArt.WithAlpha(light, 0.85f), PresentationArt.WithAlpha(c, 0f), Curve.EaseOut);
            Emit(PresentationArt.RingFor(radius, 0.12f), GroundOrder + 2, pos, Vector2.zero, 0.5f, radius * 0.3f, radius * 2f, PresentationArt.WithAlpha(light, 0.95f), PresentationArt.WithAlpha(c, 0f), Curve.EaseOut);
            Emit(PresentationArt.Disc, GroundOrder + 1, pos, Vector2.zero, 0.7f, radius * 1.6f, radius * 2f, PresentationArt.WithAlpha(c, 0.3f), PresentationArt.WithAlpha(c, 0f), Curve.EaseOut);
            int n = Mathf.RoundToInt(Mathf.Clamp(10 + radius * 6f, 10, 40) * ParticleDensity);
            SchoolParticles(pos, school, n, radius * 0.8f);
            if (ShakeOnBurst && CameraRig.Instance != null) CameraRig.Instance.Shake(Mathf.Clamp(radius * 0.03f, 0.03f, 0.12f), 0.25f);
        }

        /// <summary>Green-gold plus signs rising from a unit's feet.</summary>
        public static void HealSparkles(Vector2 pos) => Get().DoHeal(pos, 1.8f);

        /// <summary>HealSparkles scaled to a unit height.</summary>
        public static void HealSparkles(Vector2 feet, float unitHeight) => Get().DoHeal(feet, unitHeight);

        void DoHeal(Vector2 feet, float height)
        {
            var green = Ui.Hex("#8fe08a");
            var gold = Ui.Hex("#f6e08a");
            var ring = Emit(PresentationArt.UnitRing, GroundOrder + 3, feet, Vector2.zero, 0.9f, 0.6f, 1.6f, PresentationArt.WithAlpha(green, 0.6f), PresentationArt.WithAlpha(green, 0f), Curve.EaseOut);
            ring.size0 = new Vector2(0.6f, 0.25f);
            ring.size1 = new Vector2(1.6f, 0.65f);
            int n = Mathf.RoundToInt(12 * ParticleDensity);
            for (int i = 0; i < n; i++)
            {
                var at = feet + new Vector2(R(-0.45f, 0.45f), R(0.05f, height * 0.75f));
                var col = i % 3 == 0 ? gold : green;
                var p = Emit(i % 4 == 0 ? PresentationArt.Spark : PresentationArt.Fx("fx_heal"), AirOrder + 3, at, new Vector2(R(-0.08f, 0.08f), R(0.5f, 0.95f)), R(0.8f, 1.2f), R(0.14f, 0.24f), 0.06f,
                    PresentationArt.WithAlpha(Color.Lerp(col, Color.white, 0.35f), 1f), PresentationArt.WithAlpha(col, 0f));
                p.fadeIn = 0.15f; p.drag = 0.6f;
            }
        }

        /// <summary>Expanding ring on the ground (auras, shouts, totems pulsing).</summary>
        public static void AuraPulse(Vector2 pos, float radius, Color color) => Get().DoAura(pos, radius, color);

        void DoAura(Vector2 pos, float radius, Color color)
        {
            radius = Mathf.Max(0.2f, radius);
            Emit(PresentationArt.RingFor(radius, 0.08f), GroundOrder + 2, pos, Vector2.zero, 0.9f, radius * 0.5f, radius * 2f, PresentationArt.WithAlpha(color, 0.85f), PresentationArt.WithAlpha(color, 0f), Curve.EaseOut);
            Emit(PresentationArt.Disc, GroundOrder + 1, pos, Vector2.zero, 0.9f, radius * 0.4f, radius * 2f, PresentationArt.WithAlpha(color, 0.16f), PresentationArt.WithAlpha(color, 0f), Curve.EaseOut);
        }

        /// <summary>Weapon slash arc at pos swinging towards dir.</summary>
        public static void Slash(Vector3 pos, Vector3 dir) => Get().DoSlash(Legacy(pos), new Vector2(dir.x, dir.y - dir.z));

        void DoSlash(Vector2 pos, Vector2 dir)
        {
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
            dir.Normalize();
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            var p = Emit(PresentationArt.Fx("fx_slash"), AirOrder + 4, pos, dir * 0.6f, 0.24f, 0.7f, 1.45f, new Color(1f, 1f, 0.97f, 0.95f), new Color(1f, 0.95f, 0.85f, 0f), Curve.EaseOut);
            p.rot = ang + 25f * Mathf.Sign(dir.x == 0f ? 1f : dir.x);
            p.spin = -110f * Mathf.Sign(dir.x == 0f ? 1f : dir.x);
            for (int i = 0; i < 3; i++)
            {
                var s = Emit(PresentationArt.BeamLine, AirOrder + 3, pos + new Vector2(R(-0.15f, 0.15f), R(-0.2f, 0.2f)), (dir + new Vector2(R(-0.3f, 0.3f), R(-0.3f, 0.3f))) * R(3f, 5f), 0.18f, 0.3f, 0.05f, new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0f));
                s.size0 = new Vector2(0.45f, 0.06f); s.size1 = new Vector2(0.1f, 0.02f);
                s.faceVel = true; s.drag = 4f;
            }
        }

        sealed class TimedRing
        {
            public Fx fx;
            public Vector2 bounds;
            public float age, dur, radius;
            public Color color;
        }

        readonly List<TimedRing> rings = new List<TimedRing>();

        /// <summary>A ring of a fixed radius on the ground for duration seconds (traps, zones, telegraphs).</summary>
        public static void GroundRing(Vector2 pos, float radiusMetres, Color color, float duration) => Get().DoGroundRing(pos, radiusMetres, color, duration);

        void DoGroundRing(Vector2 pos, float radius, Color color, float duration)
        {
            radius = Mathf.Max(0.1f, radius);
            var s = PresentationArt.RingFor(radius, 0.07f);
            var r = new TimedRing { fx = Acquire(s, GroundOrder + 2), bounds = s.bounds.size, dur = Mathf.Max(0.2f, duration), radius = radius, color = color };
            r.fx.t.position = new Vector3(pos.x, pos.y, 0f);
            Size(r.fx, r.bounds, radius * 2f, radius * 2f);
            r.fx.sr.color = PresentationArt.WithAlpha(color, 0f);
            rings.Add(r);
        }

        void UpdateRings(float dt)
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.age += dt;
                if (r.age >= r.dur) { Release(r.fx); rings.RemoveAt(i); continue; }
                float a = Mathf.Clamp01(r.age / 0.15f) * Mathf.Clamp01((r.dur - r.age) / 0.3f) * (0.75f + 0.25f * Mathf.Sin(r.age * 4f));
                r.fx.sr.color = PresentationArt.WithAlpha(r.color, a * r.color.a);
            }
        }

        /// <summary>Twinkling star sparkles (chest opened, polymorph, lantern rekindled, level up).</summary>
        public static void Sparkles(Vector3 pos, Color color, int count = 12) => Get().DoSparkles(Legacy(pos), color, count);

        void DoSparkles(Vector2 pos, Color color, int count)
        {
            count = Mathf.RoundToInt(count * ParticleDensity);
            for (int i = 0; i < count; i++)
            {
                float ang = R(0f, Mathf.PI * 2f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var p = Emit(PresentationArt.Spark, AirOrder + 4, pos + dir * R(0f, 0.25f), dir * R(0.6f, 1.6f) + new Vector2(0f, 0.6f), R(0.6f, 1.1f), R(0.16f, 0.3f), 0.04f,
                    PresentationArt.WithAlpha(Color.Lerp(color, Color.white, 0.4f), 1f), PresentationArt.WithAlpha(color, 0f));
                p.drag = 2.2f; p.twinkle = 0.45f; p.spin = R(-120f, 120f);
            }
        }

        /// <summary>Soft smoke puff (blink, vanish, landing dust).</summary>
        public static void Puff(Vector3 pos, Color color, float size = 1f) => Get().DoPuff(Legacy(pos), color, size);

        void DoPuff(Vector2 pos, Color color, float size)
        {
            int n = Mathf.RoundToInt(6 * ParticleDensity);
            for (int i = 0; i < n; i++)
            {
                float ang = R(0f, Mathf.PI * 2f);
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.5f);
                var p = Emit(PresentationArt.Smoke, AirOrder, pos + dir * 0.15f * size, dir * R(0.4f, 0.9f) * size + new Vector2(0f, 0.2f), R(0.5f, 0.8f), 0.25f * size, 0.75f * size,
                    PresentationArt.WithAlpha(color, 0.55f * color.a), PresentationArt.WithAlpha(color, 0f), Curve.EaseOut);
                p.drag = 2f; p.spin = R(-60f, 60f);
            }
        }

        // ================================================================== update

        void Update()
        {
            float dt = Time.deltaTime;
            UpdateProjectiles(dt);
            for (int i = beams.Count - 1; i >= 0; i--)
            {
                var b = beams[i];
                UpdateBeam(b, dt);
                if (b.age >= b.dur)
                {
                    Release(b.core); Release(b.outer); Release(b.a); Release(b.b);
                    beams.RemoveAt(i);
                }
            }
            UpdateRings(dt);
            UpdateParticles(dt);
            UpdatePreviews(dt);

            if (pendingHits.Count > 0)
            {
                runningHits.Clear();
                runningHits.AddRange(pendingHits);
                pendingHits.Clear();
                for (int i = 0; i < runningHits.Count; i++)
                {
                    try { runningHits[i](); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                runningHits.Clear();
            }
        }

        /// <summary>Removes every running effect (map change). Previews are kept unless clearPreviews.</summary>
        public static void ClearAll(bool clearPreviews = true)
        {
            var s = Instance;
            if (s == null) return;
            foreach (var p in s.particles) { s.Release(p.fx); p.fx = null; s.freeParticles.Push(p); }
            s.particles.Clear();
            foreach (var p in s.projectiles) { s.Release(p.head); s.Release(p.glow); }
            s.projectiles.Clear();
            foreach (var b in s.beams) { s.Release(b.core); s.Release(b.outer); s.Release(b.a); s.Release(b.b); }
            s.beams.Clear();
            foreach (var r in s.rings) s.Release(r.fx);
            s.rings.Clear();
            s.pendingHits.Clear();
            if (clearPreviews) HideAll();
        }

        /// <summary>Number of pooled sprites currently in use (profiling).</summary>
        public static int ActiveSprites => Instance == null ? 0 : Instance.CountActive();

        int CountActive()
        {
            int n = particles.Count + projectiles.Count * 2 + beams.Count * 4 + rings.Count;
            foreach (var kv in previews) n += kv.Value.parts.Count;
            return n;
        }
    }
}
