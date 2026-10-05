// Pooled 3D combat effects, simulated on the CPU by one LateUpdate (after the camera moved, so billboards and
// camera-facing ribbons match the frame being drawn). World convention: ground = XY plane, UP IS −Z (World3D.cs).
//
//   airborne particles   camera-facing sprites: Materials3D.Additive for glows (sparks, embers, frost shards, flashes,
//                        heal plus signs), PresentationArt.SpriteAlpha for smoke, dust and leaves (dimmed at night)
//   ground effects       sprites lying flat on the ground (Lanternvale/GroundOverlay, z ≈ −0.02): burst rings and
//                        fills, aura pulses, heal glows, timed ground rings — over the terrain, under units
//   projectiles          (FxProjectiles.cs) real arrows / thrown daggers and axes flying ballistic arcs, glowing bolts
//                        with ribbon trails; beams as camera-facing ribbons; weapon slashes as swept 3D arcs
//   lights               impacts, bursts, bolts, beams and heals add short-lived SceneLighting point lights (pooled)
//   previews             (FxPreviews.cs) targeting shapes on the ground
//
// An airborne effect given a ground point (z ≈ 0) rises from the ground (dust kicked up, sparks thrown upwards).
// No allocations per frame after warm-up: sprites, particles, ribbons, meshes and lights are pooled.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;
using UnityEngine.Rendering;

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

        static readonly Vector3 Up = new Vector3(0f, 0f, -1f);

        /// <summary>Height of ground effects above the ground plane (the GroundOverlay shader also pulls them forward).</summary>
        const float GroundLift = 0.02f;

        public static FxSystem Get()
        {
            if (Instance == null) Instance = PresentationHost.Ensure<FxSystem>();
            return Instance;
        }

        // ================================================================== frame context

        Vector3 camPos = new Vector3(0f, -10f, -10f), camRight = Vector3.right, camUp = new Vector3(0f, 1f, 0f);
        Quaternion camRot = Quaternion.identity;
        /// <summary>Light level for unlit alpha sprites (smoke, dust, leaves): white by day, dim and bluish at night.</summary>
        Color smokeLight = Color.white;

        void UpdateFrameContext()
        {
            var cam = PresentationHost.Cam;
            if (cam != null)
            {
                var t = cam.transform;
                camPos = t.position;
                camRot = t.rotation;
                camRight = camRot * Vector3.right;
                camUp = camRot * Vector3.up;
            }
            var sun = SceneLighting.SunColor * SceneLighting.SunIntensity;
            var amb = SceneLighting.SkyAmbient * SceneLighting.AmbientIntensity;
            smokeLight = new Color(Light01(sun.r * 0.7f + amb.r * 0.75f), Light01(sun.g * 0.7f + amb.g * 0.75f), Light01(sun.b * 0.7f + amb.b * 0.75f), 1f);
        }

        static float Light01(float v) => Mathf.Clamp(v, 0.22f, 1f);

        // ================================================================== sprite pool

        /// <summary>How a pooled sprite is drawn.</summary>
        enum Mat
        {
            /// <summary>Camera-facing glow (Lanternvale/Additive).</summary>
            Additive,
            /// <summary>Camera-facing alpha-blended sprite (Sprites/Default).</summary>
            Alpha,
            /// <summary>Flat on the ground, additive glow.</summary>
            GroundAdditive,
            /// <summary>Flat on the ground, alpha-blended.</summary>
            GroundAlpha,
        }

        sealed class Fx
        {
            public Transform t;
            public SpriteRenderer sr;
            public bool inUse;
            public Mat mat;
            public int layer;
        }

        readonly Stack<Fx> free = new Stack<Fx>();
        readonly List<Fx> allFx = new List<Fx>();
        Transform poolRoot;
        readonly System.Random rng = new System.Random(1234);

        // ground overlays are layered by render queue (all sorting orders stay 0): 0 move range, 1 fills, 2 outlines,
        // dots and ground effects, 3 markers on top
        const int GroundLayers = 4;
        readonly Material[] groundMats = new Material[GroundLayers * 2];

        void Awake()
        {
            Instance = this;
            poolRoot = new GameObject("FX").transform;
            poolRoot.SetParent(transform, false);
        }

        Material GroundMat(bool additive, int layer)
        {
            layer = Mathf.Clamp(layer, 0, GroundLayers - 1);
            int i = layer * 2 + (additive ? 1 : 0);
            var m = groundMats[i];
            if (m != null) return m;
            var src = PresentationArt.GroundOverlay(additive);
            m = new Material(src) { name = src.name + " L" + layer, hideFlags = HideFlags.DontSave };
            m.renderQueue = (int)RenderQueue.Transparent - 44 + layer * 2;   // 2956 … 2962: above decals (2950)
            groundMats[i] = m;
            return m;
        }

        Material MaterialFor(Mat m, int layer)
        {
            switch (m)
            {
                case Mat.Alpha: return PresentationArt.SpriteAlpha;
                case Mat.GroundAdditive: return GroundMat(true, layer);
                case Mat.GroundAlpha: return GroundMat(false, layer);
                default: return Materials3D.Additive;
            }
        }

        static bool IsGround(Mat m) => m == Mat.GroundAdditive || m == Mat.GroundAlpha;

        Fx Acquire(Sprite s, Mat mat, int layer = 2)
        {
            Fx f = null;
            while (free.Count > 0 && (f == null || f.t == null)) f = free.Pop();
            if (f == null || f.t == null)
            {
                var go = new GameObject("fx");
                go.transform.SetParent(poolRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.shadowCastingMode = ShadowCastingMode.Off;
                sr.receiveShadows = false;
                f = new Fx { t = go.transform, sr = sr, mat = (Mat)(-1) };
                allFx.Add(f);
            }
            f.inUse = true;
            if (f.mat != mat || f.layer != layer || f.sr.sharedMaterial == null)
            {
                f.sr.sharedMaterial = MaterialFor(mat, layer);
                f.mat = mat;
                f.layer = layer;
            }
            f.sr.sprite = s;
            f.sr.enabled = true;
            f.sr.flipX = false;
            f.sr.drawMode = SpriteDrawMode.Simple;
            f.sr.color = Color.white;
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

        /// <summary>Random unit direction; upper = only the upper hemisphere (towards −Z), for effects on the ground.</summary>
        Vector3 RandomDir(bool upper)
        {
            float z = upper ? R(-1f, -0.12f) : R(-1f, 1f);
            float a = R(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z);
        }

        /// <summary>Random unit direction on the ground plane.</summary>
        Vector3 RandomFlat()
        {
            float a = R(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
        }

        /// <summary>True for a point on (or practically on) the ground: effects there rise instead of spreading down.</summary>
        static bool OnGround(Vector3 p) => p.z > -0.05f;

        // ================================================================== particles

        enum Curve { Linear, EaseOut, Pop }

        sealed class Particle
        {
            public Fx fx;
            public Vector2 bounds;
            public Vector3 pos, vel;
            public float gravity, drag, age, life, rot, spin, fadeIn, twinkle, phase;
            public Vector2 size0, size1;
            public Color c0, c1;
            public Curve curve;
            public bool faceVel, flat, lit, bounce;
        }

        readonly List<Particle> particles = new List<Particle>();
        readonly Stack<Particle> freeParticles = new Stack<Particle>();

        /// <summary>
        /// Spawns a pooled sprite particle. gravity &gt; 0 pulls it down (+Z), &lt; 0 makes it rise. Ground materials lie
        /// flat at GroundLift (no vertical motion); the others face the camera.
        /// </summary>
        Particle Emit(Sprite s, Mat mat, Vector3 pos, Vector3 vel, float life, float size0, float size1, Color c0, Color c1, Curve curve = Curve.Linear, int layer = 2)
        {
            var p = freeParticles.Count > 0 ? freeParticles.Pop() : new Particle();
            p.fx = Acquire(s, mat, layer);
            p.bounds = s.bounds.size;
            p.flat = IsGround(mat);
            if (p.flat) { pos.z = -GroundLift; vel.z = 0f; }
            p.pos = pos; p.vel = vel; p.life = Mathf.Max(0.01f, life); p.age = 0f;
            p.size0 = new Vector2(size0, size0); p.size1 = new Vector2(size1, size1);
            p.c0 = c0; p.c1 = c1; p.curve = curve;
            p.gravity = 0f; p.drag = 0f; p.rot = 0f; p.spin = 0f; p.fadeIn = 0f; p.twinkle = 0f; p.phase = R(0f, 10f);
            p.faceVel = false;
            p.lit = mat == Mat.Alpha;
            p.bounce = !p.flat;
            Place(p, 0f);
            particles.Add(p);
            return p;
        }

        void Place(Particle p, float t)
        {
            float k = t;
            if (p.curve == Curve.EaseOut) k = 1f - (1f - t) * (1f - t);
            else if (p.curve == Curve.Pop) k = t < 0.25f ? t / 0.25f : 1f;
            var size = Vector2.Lerp(p.size0, p.size1, k);
            var c = Color.Lerp(p.c0, p.c1, t);
            if (p.fadeIn > 0f && p.age < p.fadeIn) c.a *= p.age / p.fadeIn;
            if (p.twinkle > 0f) c.a *= 1f - p.twinkle * (0.5f + 0.5f * Mathf.Sin(p.age * 18f + p.phase));
            if (p.lit) { c.r *= smokeLight.r; c.g *= smokeLight.g; c.b *= smokeLight.b; }
            var tr = p.fx.t;
            tr.position = p.pos;
            if (p.flat) tr.rotation = Quaternion.Euler(0f, 0f, p.rot);
            else
            {
                float ang = p.rot;
                if (p.faceVel && p.vel.sqrMagnitude > 1e-6f)
                    ang = Mathf.Atan2(Vector3.Dot(p.vel, camUp), Vector3.Dot(p.vel, camRight)) * Mathf.Rad2Deg;
                tr.rotation = camRot * Quaternion.Euler(0f, 0f, ang);
            }
            Size(p.fx, p.bounds, size.x, size.y);
            p.fx.sr.color = c;
        }

        void UpdateParticles(float dt)
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.age += dt;
                float t = p.age / p.life;
                if (t >= 1f || p.fx == null || p.fx.t == null)
                {
                    Release(p.fx);
                    p.fx = null;
                    particles[i] = particles[particles.Count - 1];
                    particles.RemoveAt(particles.Count - 1);
                    freeParticles.Push(p);
                    continue;
                }
                if (p.drag > 0f) p.vel *= Mathf.Max(0f, 1f - p.drag * dt);
                p.vel.z += p.gravity * dt;
                p.pos += p.vel * dt;
                if (p.bounce && p.pos.z > -0.02f)
                {
                    // land on the ground: a small bounce, then slide to rest
                    p.pos.z = -0.02f;
                    if (p.vel.z > 0f) { p.vel.z *= -0.3f; p.vel.x *= 0.55f; p.vel.y *= 0.55f; }
                }
                p.rot += p.spin * dt;
                Place(p, t);
            }
        }

        // ================================================================== lights

        sealed class FxLight
        {
            public SceneLighting.PointLight h;
            public bool inUse;
            public float age, life, peak;
        }

        const int MaxLights = 8;
        readonly List<FxLight> lights = new List<FxLight>();

        /// <summary>
        /// A short-lived point light (life &gt; 0: flashes up and fades by itself; life ≤ 0: held until FreeLight).
        /// Null when every FX light is busy.
        /// </summary>
        FxLight AddLight(Vector3 pos, Color color, float intensity, float range, float life)
        {
            FxLight l = null;
            for (int i = 0; i < lights.Count; i++) if (!lights[i].inUse) { l = lights[i]; break; }
            if (l == null)
            {
                if (lights.Count >= MaxLights) return null;
                l = new FxLight();
                lights.Add(l);
            }
            // (re)register: a map change resets SceneLighting and drops every handle
            if (l.h == null || !l.h.registered) l.h = SceneLighting.Add(pos, color, 0f, range);
            l.inUse = true;
            l.age = 0f;
            l.life = life;
            l.peak = intensity;
            l.h.Position = pos;
            l.h.Color = color;
            l.h.Range = range;
            l.h.Intensity = life > 0f ? 0f : intensity;
            l.h.Enabled = true;
            return l;
        }

        void FreeLight(FxLight l)
        {
            if (l == null || !l.inUse) return;
            l.inUse = false;
            if (l.h != null) { l.h.Enabled = false; l.h.Intensity = 0f; }
        }

        void UpdateLights(float dt)
        {
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                if (!l.inUse || l.life <= 0f) continue;
                l.age += dt;
                if (l.age >= l.life || l.h == null || !l.h.registered) { FreeLight(l); continue; }
                float k = l.age < 0.05f ? l.age / 0.05f : 1f - (l.age - 0.05f) / Mathf.Max(0.01f, l.life - 0.05f);
                l.h.Intensity = l.peak * k * k;
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < lights.Count; i++)
                if (lights[i].h != null) SceneLighting.Remove(lights[i].h);
            lights.Clear();
            for (int i = 0; i < groundMats.Length; i++) if (groundMats[i] != null) Destroy(groundMats[i]);
            if (Instance == this) Instance = null;
        }

        static Color Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);
        static Color A(Color c, float a) { c.a = a; return c; }

        // ================================================================== one-shot effects

        /// <summary>Small hit: flash, shock ring, light and school-flavoured particles. On the ground it bursts upwards.</summary>
        public static void Impact(Vector3 pos, School school) => Get().DoImpact(pos, school, 1f);

        void DoImpact(Vector3 pos, School school, float scale)
        {
            var c = Ui.SchoolColor(school);
            var light = Lighten(c, 0.45f);
            bool grounded = OnGround(pos);
            var at = grounded ? new Vector3(pos.x, pos.y, -0.35f * scale) : pos;
            Emit(PresentationArt.Glow, Mat.Additive, at, Vector3.zero, 0.3f, 0.35f * scale, 1.5f * scale, A(light, 0.9f), A(c, 0f), Curve.EaseOut);
            Emit(PresentationArt.Ring(4, 128), Mat.Additive, at, Vector3.zero, 0.32f, 0.2f * scale, 1.3f * scale, A(light, 0.85f), A(c, 0f), Curve.EaseOut);
            if (grounded)
                Emit(PresentationArt.RingFor(0.6f * scale, 0.08f), Mat.GroundAdditive, pos, Vector3.zero, 0.4f, 0.3f * scale, 1.4f * scale, A(light, 0.8f), A(c, 0f), Curve.EaseOut);
            AddLight(at, light, 1.3f * scale, 3.2f * scale, 0.32f);
            SchoolParticles(at, school, Mathf.RoundToInt(8 * scale * ParticleDensity), 0.35f * scale, grounded);
        }

        /// <summary>School-flavoured particles thrown out from pos (spread: random start offset, metres).</summary>
        void SchoolParticles(Vector3 pos, School school, int n, float spread, bool grounded)
        {
            var c = Ui.SchoolColor(school);
            var light = Lighten(c, 0.5f);
            for (int i = 0; i < n; i++)
            {
                var dir = RandomDir(grounded);
                var at = pos + (grounded ? RandomFlat() : dir) * R(0f, spread);
                Particle p;
                switch (school)
                {
                    case School.Fire:
                        p = Emit(PresentationArt.SoftDot, Mat.Additive, at, dir * R(1f, 2.6f) + Up * 0.8f, R(0.4f, 0.8f), R(0.1f, 0.18f), 0.03f, new Color(1f, 0.85f, 0.45f, 1f), new Color(1f, 0.35f, 0.15f, 0f));
                        p.gravity = -1.6f; p.drag = 2.5f; p.twinkle = 0.3f;
                        if (i % 3 == 0)
                        {
                            var s = Emit(PresentationArt.Smoke, Mat.Alpha, at, Up * R(0.4f, 0.8f), R(0.6f, 0.9f), 0.25f, 0.7f, new Color(0.35f, 0.28f, 0.3f, 0.35f), new Color(0.35f, 0.28f, 0.3f, 0f), Curve.EaseOut);
                            s.spin = R(-60f, 60f);
                        }
                        break;
                    case School.Frost:
                        p = Emit(PresentationArt.Shard, Mat.Additive, at, dir * R(1.5f, 3f) + Up * 1.2f, R(0.45f, 0.7f), R(0.14f, 0.24f), 0.08f, A(light, 1f), A(c, 0f));
                        p.gravity = 6f; p.drag = 1f; p.faceVel = true;
                        break;
                    case School.Nature:
                        p = Emit(PresentationArt.Fx("fx_leaf"), Mat.Alpha, at, dir * R(0.8f, 1.8f) + Up * 0.6f, R(0.6f, 1f), R(0.16f, 0.24f), 0.12f, A(light, 1f), A(c, 0f));
                        p.gravity = 1.2f; p.drag = 2f; p.spin = R(-240f, 240f);
                        break;
                    case School.Shadow:
                        p = Emit(PresentationArt.Smoke, Mat.Alpha, at, dir * R(0.4f, 1f) + Up * 0.5f, R(0.6f, 1f), R(0.2f, 0.3f), R(0.5f, 0.8f), new Color(c.r * 0.6f, c.g * 0.5f, c.b * 0.7f, 0.6f), A(c, 0f), Curve.EaseOut);
                        p.spin = R(-90f, 90f); p.drag = 1.5f; p.lit = false;
                        break;
                    case School.Holy:
                        p = Emit(PresentationArt.Spark, Mat.Additive, at, dir * R(0.6f, 1.6f) + Up * 0.7f, R(0.5f, 0.9f), R(0.2f, 0.34f), 0.05f, new Color(1f, 0.97f, 0.8f, 1f), A(c, 0f));
                        p.drag = 2f; p.twinkle = 0.4f;
                        break;
                    case School.Arcane:
                        p = Emit(PresentationArt.Spark, Mat.Additive, at, dir * R(1f, 2.2f), R(0.4f, 0.7f), R(0.18f, 0.3f), 0.04f, A(light, 1f), A(c, 0f));
                        p.drag = 3f; p.spin = R(-300f, 300f); p.twinkle = 0.5f;
                        break;
                    default:
                        p = Emit(PresentationArt.SoftDot, Mat.Additive, at, dir * R(2f, 4f) + Up * 1f, R(0.2f, 0.35f), R(0.06f, 0.1f), 0.02f, new Color(1f, 0.98f, 0.9f, 1f), new Color(1f, 0.9f, 0.7f, 0f));
                        p.gravity = 8f; p.drag = 1f;
                        if (i % 3 == 0)
                        {
                            var s = Emit(PresentationArt.Smoke, Mat.Alpha, at, dir * 0.4f + (grounded ? Up * 0.3f : Vector3.zero), R(0.45f, 0.7f), 0.2f, 0.55f, new Color(0.88f, 0.82f, 0.7f, 0.45f), new Color(0.88f, 0.82f, 0.7f, 0f), Curve.EaseOut);
                            s.spin = R(-60f, 60f);
                        }
                        break;
                }
            }
        }

        /// <summary>Area explosion of the given radius (metres) centred on a ground point.</summary>
        public static void Burst(Vector2 pos, float radius, School school) => Get().DoBurst(pos, radius, school);

        void DoBurst(Vector2 pos, float radius, School school)
        {
            radius = Mathf.Max(0.3f, radius);
            var c = Ui.SchoolColor(school);
            var light = Lighten(c, 0.45f);
            var ground = new Vector3(pos.x, pos.y, 0f);
            var air = ground + Up * Mathf.Min(1.2f, 0.3f + radius * 0.35f);
            Emit(PresentationArt.Glow, Mat.Additive, air, Vector3.zero, 0.45f, radius * 0.6f, radius * 2.4f, A(light, 0.8f), A(c, 0f), Curve.EaseOut);
            Emit(PresentationArt.RingFor(radius, 0.12f), Mat.GroundAdditive, ground, Vector3.zero, 0.5f, radius * 0.3f, radius * 2f, A(light, 0.95f), A(c, 0f), Curve.EaseOut);
            Emit(PresentationArt.Disc, Mat.GroundAdditive, ground, Vector3.zero, 0.7f, radius * 1.6f, radius * 2f, A(c, 0.3f), A(c, 0f), Curve.EaseOut, 1);
            Emit(PresentationArt.Glow, Mat.GroundAdditive, ground, Vector3.zero, 0.8f, radius * 2.2f, radius * 2.8f, A(light, 0.45f), A(c, 0f), Curve.EaseOut, 1);
            int n = Mathf.RoundToInt(Mathf.Clamp(10 + radius * 6f, 10, 40) * ParticleDensity);
            SchoolParticles(ground + Up * 0.15f, school, n, radius * 0.8f, true);
            AddLight(air, light, 1.8f, radius * 1.6f + 2.5f, 0.55f);
            if (ShakeOnBurst && CameraRig.Instance != null) CameraRig.Instance.Shake(Mathf.Clamp(radius * 0.03f, 0.03f, 0.12f), 0.25f);
        }

        /// <summary>Green-gold plus signs rising from a unit's feet (ground point).</summary>
        public static void HealSparkles(Vector2 pos) => Get().DoHeal(pos, 1.8f);

        /// <summary>HealSparkles scaled to a unit height.</summary>
        public static void HealSparkles(Vector2 feet, float unitHeight) => Get().DoHeal(feet, unitHeight);

        void DoHeal(Vector2 feet, float height)
        {
            height = Mathf.Clamp(height, 0.5f, 6f);
            var green = Ui.Hex("#8fe08a");
            var gold = Ui.Hex("#f6e08a");
            var ground = new Vector3(feet.x, feet.y, 0f);
            float w = Mathf.Clamp(height * 0.35f, 0.5f, 1.6f);   // footprint radius-ish
            Emit(PresentationArt.UnitRing, Mat.GroundAdditive, ground, Vector3.zero, 0.9f, w * 1.1f, w * 2.6f, A(green, 0.7f), A(green, 0f), Curve.EaseOut);
            Emit(PresentationArt.Glow, Mat.GroundAdditive, ground, Vector3.zero, 1f, w * 2.2f, w * 3f, A(green, 0.4f), A(green, 0f), Curve.EaseOut, 1);
            int n = Mathf.RoundToInt(12 * ParticleDensity);
            var plus = PresentationArt.Fx("fx_heal");
            for (int i = 0; i < n; i++)
            {
                var off = RandomFlat() * R(0.15f, w * 0.9f);
                var at = ground + off + Up * R(0.05f, height * 0.75f);
                var col = i % 3 == 0 ? gold : green;
                var p = Emit(i % 4 == 0 ? PresentationArt.Spark : plus, Mat.Additive, at, off * 0.1f + Up * R(0.5f, 0.95f), R(0.8f, 1.2f), R(0.14f, 0.24f), 0.06f,
                    A(Lighten(col, 0.35f), 1f), A(col, 0f));
                p.fadeIn = 0.15f; p.drag = 0.6f; p.bounce = false;
            }
            AddLight(ground + Up * Mathf.Min(1.2f, height * 0.5f), Lighten(green, 0.2f), 0.75f, 2.4f + w, 0.9f);
        }

        /// <summary>Expanding ring on the ground (auras, shouts, totems pulsing).</summary>
        public static void AuraPulse(Vector2 pos, float radius, Color color) => Get().DoAura(pos, radius, color);

        void DoAura(Vector2 pos, float radius, Color color)
        {
            radius = Mathf.Max(0.2f, radius);
            var ground = new Vector3(pos.x, pos.y, 0f);
            Emit(PresentationArt.RingFor(radius, 0.08f), Mat.GroundAdditive, ground, Vector3.zero, 0.9f, radius * 0.5f, radius * 2f, A(color, 0.85f * color.a), A(color, 0f), Curve.EaseOut);
            Emit(PresentationArt.Disc, Mat.GroundAdditive, ground, Vector3.zero, 0.9f, radius * 0.4f, radius * 2f, A(color, 0.16f * color.a), A(color, 0f), Curve.EaseOut, 1);
        }

        sealed class TimedRing
        {
            public Fx fx;
            public float age, dur;
            public Color color;
        }

        readonly List<TimedRing> rings = new List<TimedRing>();
        readonly Stack<TimedRing> freeRings = new Stack<TimedRing>();

        /// <summary>A ring of a fixed radius on the ground for duration seconds (traps, zones, telegraphs, click markers).</summary>
        public static void GroundRing(Vector2 pos, float radiusMetres, Color color, float duration) => Get().DoGroundRing(pos, radiusMetres, color, duration);

        void DoGroundRing(Vector2 pos, float radius, Color color, float duration)
        {
            radius = Mathf.Max(0.1f, radius);
            var r = freeRings.Count > 0 ? freeRings.Pop() : new TimedRing();
            r.fx = Acquire(PresentationArt.White, Mat.GroundAlpha, 2);
            r.age = 0f;
            r.dur = Mathf.Max(0.2f, duration);
            r.color = color;
            PlaceFlatScaled(r.fx, PreviewRing(radius, 0.08f), 2, pos, radius * 2f, 0f, A(color, 0f));
            rings.Add(r);
        }

        void UpdateRings(float dt)
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.age += dt;
                if (r.age >= r.dur) { Release(r.fx); r.fx = null; rings.RemoveAt(i); freeRings.Push(r); continue; }
                float a = Mathf.Clamp01(r.age / 0.15f) * Mathf.Clamp01((r.dur - r.age) / 0.3f) * (0.75f + 0.25f * Mathf.Sin(r.age * 4f));
                r.fx.sr.color = A(r.color, a * r.color.a);
            }
        }

        /// <summary>Twinkling star sparkles (chest opened, polymorph, lantern rekindled, level up). From the ground they rise.</summary>
        public static void Sparkles(Vector3 pos, Color color, int count = 12) => Get().DoSparkles(pos, color, count);

        void DoSparkles(Vector3 pos, Color color, int count)
        {
            bool grounded = OnGround(pos);
            count = Mathf.RoundToInt(count * ParticleDensity);
            for (int i = 0; i < count; i++)
            {
                var dir = RandomDir(grounded);
                var at = pos + (grounded ? RandomFlat() * R(0f, 0.35f) + Up * R(0.05f, 0.4f) : dir * R(0f, 0.25f));
                var p = Emit(PresentationArt.Spark, Mat.Additive, at, dir * R(0.6f, 1.6f) + Up * 0.6f, R(0.6f, 1.1f), R(0.16f, 0.3f), 0.04f,
                    A(Lighten(color, 0.4f), 1f), A(color, 0f));
                p.drag = 2.2f; p.twinkle = 0.45f; p.spin = R(-120f, 120f); p.gravity = -0.2f;
            }
            if (count >= 8) AddLight(grounded ? pos + Up * 0.5f : pos, Lighten(color, 0.3f), 0.55f, 2.2f, 0.5f);
        }

        /// <summary>Soft smoke puff (blink, vanish, landing dust). On the ground it is dust kicked up and outwards.</summary>
        public static void Puff(Vector3 pos, Color color, float size = 1f) => Get().DoPuff(pos, color, size);

        void DoPuff(Vector3 pos, Color color, float size)
        {
            bool grounded = OnGround(pos);
            int n = Mathf.RoundToInt(6 * ParticleDensity);
            for (int i = 0; i < n; i++)
            {
                Vector3 at, vel;
                if (grounded)
                {
                    var d = RandomFlat();
                    at = new Vector3(pos.x, pos.y, 0f) + d * (0.15f * size) + Up * (0.08f * size);
                    vel = d * (R(0.4f, 0.9f) * size) + Up * (R(0.25f, 0.6f) * size);
                }
                else
                {
                    var d = RandomDir(false);
                    d.z *= 0.6f;
                    at = pos + d * (0.15f * size);
                    vel = d * (R(0.4f, 0.9f) * size) + Up * (0.2f * size);
                }
                var p = Emit(PresentationArt.Smoke, Mat.Alpha, at, vel, R(0.5f, 0.8f), 0.25f * size, 0.75f * size,
                    A(color, 0.55f * color.a), A(color, 0f), Curve.EaseOut);
                p.drag = 2f; p.spin = R(-60f, 60f); p.bounce = false;
            }
        }

        // ================================================================== update

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            UpdateFrameContext();
            UpdateProjectiles(dt);
            UpdateBeams(dt);
            UpdateSlashes(dt);
            UpdateRings(dt);
            UpdateParticles(dt);
            UpdateLights(dt);
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
            for (int i = 0; i < s.particles.Count; i++) { var p = s.particles[i]; s.Release(p.fx); p.fx = null; s.freeParticles.Push(p); }
            s.particles.Clear();
            s.ClearProjectiles();
            s.ClearBeams();
            s.ClearSlashes();
            for (int i = 0; i < s.rings.Count; i++) { s.Release(s.rings[i].fx); s.rings[i].fx = null; s.freeRings.Push(s.rings[i]); }
            s.rings.Clear();
            for (int i = 0; i < s.lights.Count; i++) s.FreeLight(s.lights[i]);
            s.pendingHits.Clear();
            if (clearPreviews) HideAll();
        }

        /// <summary>Number of pooled renderers currently in use (profiling).</summary>
        public static int ActiveSprites => Instance == null ? 0 : Instance.CountActive();

        int CountActive()
        {
            int n = 0;
            for (int i = 0; i < allFx.Count; i++) if (allFx[i].inUse) n++;
            for (int i = 0; i < ribbons.Count; i++) if (ribbons[i].inUse) n++;
            for (int i = 0; i < models.Count; i++) if (models[i].inUse) n++;
            return n;
        }
    }
}
