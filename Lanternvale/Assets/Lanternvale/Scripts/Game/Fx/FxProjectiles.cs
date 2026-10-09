// Projectiles, channelled beams and weapon slashes of FxSystem, in 3D.
//
//   Projectile  arrows (a real arrow model flying a ballistic arc, pointing along its velocity, rolling slowly),
//               thrown daggers / axes (tumbling end over end), glowing bolts (additive head + streak, ribbon trail,
//               school-flavoured motes, a travelling point light) ending in an Impact; onHit on arrival
//   Beam        two camera-facing ribbons (soft outer glow + bright core) that flicker and writhe, end glows, motes
//               running along it and a light at the target
//   Slash       a swept crescent ribbon around the hit point (a 3D weapon trail), a flash and streaks
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    public sealed partial class FxSystem
    {
        // ================================================================== ribbon and model pools

        readonly List<FxRibbon> ribbons = new List<FxRibbon>();
        readonly Stack<FxRibbon> freeRibbons = new Stack<FxRibbon>();

        FxRibbon AcquireRibbon(Material m)
        {
            FxRibbon r = null;
            while (freeRibbons.Count > 0 && (r == null || r.go == null)) r = freeRibbons.Pop();
            if (r == null || r.go == null)
            {
                r = new FxRibbon(poolRoot);
                ribbons.Add(r);
            }
            r.inUse = true;
            if (r.renderer.sharedMaterial != m) r.renderer.sharedMaterial = m;
            r.Hide();
            return r;
        }

        void ReleaseRibbon(FxRibbon r)
        {
            if (r == null || !r.inUse) return;
            r.inUse = false;
            if (r.go != null) r.Hide();
            freeRibbons.Push(r);
        }

        /// <summary>Additive soft strip (gaussian across): trails and beams.</summary>
        static Material GlowStrip => Materials3D.AdditiveFor(PresentationArt.BeamLine.texture);

        sealed class Model
        {
            public GameObject go;
            public Transform t;
            public MeshFilter mf;
            public bool inUse;
        }

        readonly List<Model> models = new List<Model>();
        readonly Stack<Model> freeModels = new Stack<Model>();

        Model AcquireModel(Mesh mesh)
        {
            Model m = null;
            while (freeModels.Count > 0 && (m == null || m.go == null)) m = freeModels.Pop();
            if (m == null || m.go == null)
            {
                var go = new GameObject("fx model");
                go.transform.SetParent(poolRoot, false);
                m = new Model { go = go, t = go.transform, mf = go.AddComponent<MeshFilter>() };
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = Materials3D.WithOutline();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                models.Add(m);
            }
            m.inUse = true;
            m.mf.sharedMesh = mesh;
            m.go.SetActive(true);
            return m;
        }

        void ReleaseModel(Model m)
        {
            if (m == null || !m.inUse) return;
            m.inUse = false;
            if (m.go != null) m.go.SetActive(false);
            freeModels.Push(m);
        }

        /// <summary>
        /// Draws a camera-facing trail from head back through hist[0..n) (newest first), at most maxLen metres long:
        /// tapering from halfWidth at the head to nothing at its end, fading out with it.
        /// </summary>
        void DrawTrail(FxRibbon r, Vector3 head, Vector3[] hist, int n, float maxLen, float halfWidth, Color c)
        {
            r.Begin();
            // length actually available (the trail is still growing out of the shooter's hand at first)
            float total = 0f;
            var prev = head;
            for (int i = 0; i < n && total < maxLen; i++) { total += Vector3.Distance(prev, hist[i]); prev = hist[i]; }
            total = Mathf.Min(total, maxLen);
            if (total < 0.02f) { r.End(); return; }
            var tan0 = Vector3.zero;
            for (int i = 0; i < n; i++) { tan0 = head - hist[i]; if (tan0.sqrMagnitude > 1e-6f) break; }
            r.AddFacing(head, tan0, camPos, camRight, halfWidth, c, 0f);
            float acc = 0f;
            prev = head;
            for (int i = 0; i < n && r.Count < FxRibbon.MaxPoints; i++)
            {
                var p = hist[i];
                var d = prev - p;
                float seg = d.magnitude;
                if (seg < 1e-4f) continue;
                bool last = acc + seg >= total - 1e-4f;
                if (last) { p = prev - d * ((total - acc) / seg); acc = total; }
                else acc += seg;
                float k = acc / total;   // 0 head … 1 tail
                float fade = 1f - k;
                r.AddFacing(p, d, camPos, camRight, halfWidth * fade, A(c, c.a * fade * fade), k);
                prev = p;
                if (last) break;
            }
            r.End();
        }

        // ================================================================== projectiles

        enum ProjectileKind { Arrow, Thrown, Bolt }

        const int TrailPoints = 14;

        sealed class ProjectileFx
        {
            public ProjectileKind kind;
            public Model model;
            public Fx glow, core, streak;
            public FxRibbon trail;
            public FxLight light;
            public Vector3 from, to, pos;
            public float dur, t, arc, spin, spinSpeed, emitTimer, spacing;
            public Color color;
            public System.Action onHit;
            public School school;
            public readonly Vector3[] hist = new Vector3[TrailPoints];
            public int histCount;
        }

        readonly List<ProjectileFx> projectiles = new List<ProjectileFx>();
        readonly Stack<ProjectileFx> freeProjectiles = new Stack<ProjectileFx>();
        readonly List<System.Action> pendingHits = new List<System.Action>();
        readonly List<System.Action> runningHits = new List<System.Action>();

        /// <summary>
        /// Fires a projectile between two 3D points (e.g. the shooter's hand and the target's centre). artKey "" picks an
        /// arrow for Physical and a glowing bolt otherwise; keys containing "arrow" fly an arrow, "dagger"/"knife" and
        /// "axe" are thrown and tumble; anything else is a bolt. Arrows arc; bolts fly almost straight with a glowing
        /// trail and end in an Impact. onHit runs on arrival (after this frame's FX update). Returns the flight time in
        /// seconds (distance / speed).
        /// </summary>
        public static float Projectile(Vector3 from, Vector3 to, School school, string artKey = "", float speed = 14f, System.Action onHit = null)
            => Get().FireProjectile(from, to, school, artKey, speed, onHit);

        float FireProjectile(Vector3 from, Vector3 to, School school, string artKey, float speed, System.Action onHit)
        {
            var kind = ProjectileKind.Bolt;
            Mesh mesh = null;
            if (string.IsNullOrEmpty(artKey)) { if (school == School.Physical) { kind = ProjectileKind.Arrow; mesh = FxModels.Arrow; } }
            else if (artKey.IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) >= 0) { kind = ProjectileKind.Arrow; mesh = FxModels.Arrow; }
            else if (artKey.IndexOf("dagger", System.StringComparison.OrdinalIgnoreCase) >= 0 || artKey.IndexOf("knife", System.StringComparison.OrdinalIgnoreCase) >= 0) { kind = ProjectileKind.Thrown; mesh = FxModels.Dagger; }
            else if (artKey.IndexOf("axe", System.StringComparison.OrdinalIgnoreCase) >= 0) { kind = ProjectileKind.Thrown; mesh = FxModels.Axe; }

            float dist = Vector3.Distance(from, to);
            speed = speed > 0f ? speed : 14f;
            var color = Ui.SchoolColor(school);
            var p = freeProjectiles.Count > 0 ? freeProjectiles.Pop() : new ProjectileFx();
            p.kind = kind;
            p.from = from;
            p.to = to;
            p.pos = from;
            p.t = 0f;
            p.dur = Mathf.Max(0.08f, dist / speed);
            p.color = color;
            p.onHit = onHit;
            p.school = school;
            p.histCount = 0;
            p.emitTimer = 0f;
            p.spin = R(0f, 360f);
            p.model = null; p.glow = null; p.core = null; p.streak = null; p.trail = null; p.light = null;
            switch (kind)
            {
                case ProjectileKind.Arrow:
                    p.arc = Mathf.Clamp(dist * 0.12f, 0.15f, 2.0f);
                    p.spinSpeed = 300f;   // a slow roll around the shaft
                    p.spacing = 0.1f;
                    p.model = AcquireModel(mesh);
                    p.trail = AcquireRibbon(GlowStrip);
                    break;
                case ProjectileKind.Thrown:
                    p.arc = Mathf.Clamp(dist * 0.08f, 0.1f, 1.2f);
                    p.spinSpeed = 1080f;  // tumbling end over end
                    p.spacing = 0.1f;
                    p.model = AcquireModel(mesh);
                    p.trail = AcquireRibbon(GlowStrip);
                    break;
                default:
                    p.arc = Mathf.Clamp(dist * 0.04f, 0f, 0.5f);
                    p.spinSpeed = 0f;
                    p.spacing = 0.13f;
                    var light = Lighten(color, 0.35f);
                    p.glow = Acquire(PresentationArt.Glow, Mat.Additive);
                    p.glow.sr.color = A(color, 0.65f);
                    p.core = Acquire(PresentationArt.SoftDot, Mat.Additive);
                    p.core.sr.color = A(Lighten(color, 0.7f), 1f);
                    var bolt = string.IsNullOrEmpty(artKey) ? PresentationArt.Bolt : PresentationArt.Fx(artKey);
                    if (bolt == null) bolt = PresentationArt.Bolt;
                    p.streak = Acquire(bolt, Mat.Additive);
                    p.streak.sr.color = A(light, 0.95f);
                    p.trail = AcquireRibbon(GlowStrip);
                    p.light = AddLight(from, light, 1f, 3.5f, 0f);
                    break;
            }
            projectiles.Add(p);
            PlaceProjectile(p, 0f);
            return p.dur;
        }

        static Vector3 ProjectilePos(ProjectileFx p, float t) => Vector3.LerpUnclamped(p.from, p.to, t) + Up * (p.arc * 4f * t * (1f - t));

        /// <summary>Flight direction at t (the derivative of the arc).</summary>
        static Vector3 ProjectileDir(ProjectileFx p, float t) => (p.to - p.from) + Up * (p.arc * 4f * (1f - 2f * t));

        void PlaceProjectile(ProjectileFx p, float t)
        {
            var pos = ProjectilePos(p, t);
            p.pos = pos;
            var dir = ProjectileDir(p, t);
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.up;
            if (p.model != null)
            {
                // the model points along +Z; keep its up towards the sky unless it flies straight up/down
                var upHint = Mathf.Abs(Vector3.Dot(dir.normalized, Up)) > 0.98f ? World3D.Back : Up;
                var rot = Quaternion.LookRotation(dir, upHint);
                rot *= p.kind == ProjectileKind.Thrown ? Quaternion.AngleAxis(p.spin, Vector3.right) : Quaternion.AngleAxis(p.spin, Vector3.forward);
                p.model.t.SetPositionAndRotation(pos, rot);
            }
            if (p.glow != null)
            {
                float flick = 0.9f + 0.1f * Mathf.Sin(t * 60f + p.spin);
                p.glow.t.SetPositionAndRotation(pos, camRot);
                p.glow.t.localScale = new Vector3(0.85f * flick, 0.85f * flick, 1f);
                p.core.t.SetPositionAndRotation(pos, camRot);
                p.core.t.localScale = new Vector3(0.34f, 0.34f, 1f);
                // the teardrop streak points along the flight direction as seen on screen
                float ang = Mathf.Atan2(Vector3.Dot(dir, camUp), Vector3.Dot(dir, camRight)) * Mathf.Rad2Deg;
                p.streak.t.SetPositionAndRotation(pos, camRot * Quaternion.Euler(0f, 0f, ang));
                var b = p.streak.sr.sprite.bounds.size;
                Size(p.streak, b, 0.8f, 0.8f * b.y / Mathf.Max(1e-4f, b.x));
            }
            if (p.light != null && p.light.h != null) p.light.h.Position = pos;
        }

        void UpdateProjectiles(float dt)
        {
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                p.t += dt / p.dur;
                float t = Mathf.Min(1f, p.t);
                p.spin += p.spinSpeed * dt;
                PlaceProjectile(p, t);

                // trail history: a point every `spacing` metres
                if (p.histCount == 0 || (p.pos - p.hist[0]).sqrMagnitude > p.spacing * p.spacing)
                {
                    int n = Mathf.Min(p.histCount + 1, TrailPoints);
                    for (int k = n - 1; k > 0; k--) p.hist[k] = p.hist[k - 1];
                    p.hist[0] = p.pos;
                    p.histCount = n;
                }
                if (p.trail != null)
                {
                    if (p.kind == ProjectileKind.Bolt) DrawTrail(p.trail, p.pos, p.hist, p.histCount, 1.7f, 0.13f, A(Lighten(p.color, 0.3f), 0.85f));
                    else DrawTrail(p.trail, p.pos, p.hist, p.histCount, 0.7f, 0.035f, new Color(1f, 0.98f, 0.92f, 0.35f));
                }
                if (p.kind == ProjectileKind.Bolt && t < 1f) EmitBoltMotes(p, dt);

                if (p.t >= 1f)
                {
                    projectiles.RemoveAt(i);
                    if (p.kind == ProjectileKind.Bolt) DoImpact(p.to, p.school, 0.8f);
                    else
                    {
                        var s = Emit(PresentationArt.Smoke, Mat.Alpha, p.to, Up * 0.3f, 0.35f, 0.15f, 0.45f, new Color(0.9f, 0.85f, 0.75f, 0.45f), new Color(0.9f, 0.85f, 0.75f, 0f), Curve.EaseOut);
                        s.bounce = false;
                    }
                    if (p.onHit != null) pendingHits.Add(p.onHit);
                    RecycleProjectile(p);
                }
            }
        }

        /// <summary>School-flavoured motes shed by a flying bolt.</summary>
        void EmitBoltMotes(ProjectileFx p, float dt)
        {
            p.emitTimer -= dt;
            if (p.emitTimer > 0f) return;
            p.emitTimer = 0.022f;
            var c = p.color;
            var at = p.pos + RandomDir(false) * 0.05f;
            var drift = RandomDir(false) * R(0.1f, 0.3f);
            Particle q;
            switch (p.school)
            {
                case School.Fire:
                    q = Emit(PresentationArt.SoftDot, Mat.Additive, at, drift + Up * R(0.4f, 0.9f), R(0.3f, 0.5f), R(0.12f, 0.2f), 0.03f, new Color(1f, 0.85f, 0.45f, 0.9f), new Color(1f, 0.35f, 0.15f, 0f));
                    q.gravity = -1.2f; q.twinkle = 0.3f; q.bounce = false;
                    if (rng.NextDouble() < 0.25)
                    {
                        var s = Emit(PresentationArt.Smoke, Mat.Alpha, at, Up * R(0.2f, 0.5f), R(0.4f, 0.6f), 0.12f, 0.4f, new Color(0.35f, 0.28f, 0.3f, 0.3f), new Color(0.35f, 0.28f, 0.3f, 0f), Curve.EaseOut);
                        s.bounce = false;
                    }
                    break;
                case School.Frost:
                    q = Emit(rng.NextDouble() < 0.5 ? PresentationArt.Shard : PresentationArt.SoftDot, Mat.Additive, at, drift, R(0.3f, 0.5f), R(0.08f, 0.14f), 0.03f, A(Lighten(c, 0.5f), 0.95f), A(c, 0f));
                    q.gravity = 1.5f; q.spin = R(-200f, 200f);
                    break;
                case School.Nature:
                    if (rng.NextDouble() < 0.3)
                    {
                        q = Emit(PresentationArt.Fx("fx_leaf"), Mat.Alpha, at, drift, R(0.5f, 0.8f), R(0.1f, 0.16f), 0.08f, A(Lighten(c, 0.3f), 1f), A(c, 0f));
                        q.gravity = 0.8f; q.spin = R(-240f, 240f); q.drag = 1.5f;
                    }
                    else
                    {
                        q = Emit(PresentationArt.SoftDot, Mat.Additive, at, drift, R(0.3f, 0.45f), R(0.1f, 0.16f), 0.03f, A(Lighten(c, 0.4f), 0.9f), A(c, 0f));
                        q.bounce = false;
                    }
                    break;
                case School.Shadow:
                    q = Emit(PresentationArt.Smoke, Mat.Alpha, at, drift + Up * 0.15f, R(0.35f, 0.6f), R(0.14f, 0.2f), R(0.3f, 0.45f), new Color(c.r * 0.5f, c.g * 0.4f, c.b * 0.6f, 0.55f), A(c, 0f), Curve.EaseOut);
                    q.spin = R(-90f, 90f); q.lit = false; q.bounce = false;
                    break;
                case School.Holy:
                case School.Arcane:
                    q = Emit(PresentationArt.Spark, Mat.Additive, at, drift, R(0.3f, 0.5f), R(0.14f, 0.22f), 0.03f, A(Lighten(c, 0.55f), 1f), A(c, 0f));
                    q.twinkle = 0.5f; q.spin = R(-200f, 200f); q.bounce = false;
                    break;
                default:
                    q = Emit(PresentationArt.SoftDot, Mat.Additive, at, drift, R(0.25f, 0.4f), R(0.12f, 0.2f), 0.03f, A(Lighten(c, 0.3f), 0.8f), A(c, 0f));
                    q.bounce = false;
                    break;
            }
        }

        void RecycleProjectile(ProjectileFx p)
        {
            ReleaseModel(p.model);
            Release(p.glow);
            Release(p.core);
            Release(p.streak);
            ReleaseRibbon(p.trail);
            FreeLight(p.light);
            p.model = null; p.glow = null; p.core = null; p.streak = null; p.trail = null; p.light = null;
            p.onHit = null;
            freeProjectiles.Push(p);
        }

        void ClearProjectiles()
        {
            for (int i = 0; i < projectiles.Count; i++) RecycleProjectile(projectiles[i]);
            projectiles.Clear();
        }

        // ================================================================== beams

        const int BeamPoints = 18;

        sealed class BeamFx
        {
            public int id;
            public FxRibbon outer, core;
            public Fx a, b;
            public FxLight light;
            public Vector3 from, to;
            public float age, dur, sparkTimer;
            public Color color;
            public School school;
            public bool stopping;
        }

        readonly List<BeamFx> beams = new List<BeamFx>();
        readonly Stack<BeamFx> freeBeams = new Stack<BeamFx>();
        int nextBeamId = 1;

        /// <summary>Channel beam between two 3D points for duration seconds. Returns a handle for StopBeam/MoveBeam.</summary>
        public static int Beam(Vector3 from, Vector3 to, School school, float duration) => Get().StartBeam(from, to, school, duration);

        /// <summary>Ends a beam early (fades out).</summary>
        public static void StopBeam(int handle)
        {
            var s = Instance;
            if (s == null) return;
            for (int i = 0; i < s.beams.Count; i++)
            {
                var b = s.beams[i];
                if (b.id == handle && !b.stopping) { b.stopping = true; b.dur = b.age + 0.25f; }
            }
        }

        /// <summary>Moves the beam's endpoints (units moved while channelling).</summary>
        public static void MoveBeam(int handle, Vector3 from, Vector3 to)
        {
            var s = Instance;
            if (s == null) return;
            for (int i = 0; i < s.beams.Count; i++)
            {
                var b = s.beams[i];
                if (b.id == handle) { b.from = from; b.to = to; }
            }
        }

        int StartBeam(Vector3 from, Vector3 to, School school, float duration)
        {
            var c = Ui.SchoolColor(school);
            var b = freeBeams.Count > 0 ? freeBeams.Pop() : new BeamFx();
            b.id = nextBeamId++;
            b.from = from;
            b.to = to;
            b.age = 0f;
            b.sparkTimer = 0f;
            b.dur = Mathf.Max(0.2f, duration);
            b.color = c;
            b.school = school;
            b.stopping = false;
            b.outer = AcquireRibbon(GlowStrip);
            b.core = AcquireRibbon(GlowStrip);
            b.a = Acquire(PresentationArt.Glow, Mat.Additive);
            b.b = Acquire(PresentationArt.Glow, Mat.Additive);
            b.light = AddLight(to, Lighten(c, 0.4f), 0f, 3.6f, 0f);
            beams.Add(b);
            DrawBeam(b, 0f);
            return b.id;
        }

        void DrawBeam(BeamFx b, float dt)
        {
            b.age += dt;
            float fade = Mathf.Clamp01(b.age / 0.12f) * Mathf.Clamp01((b.dur - b.age) / 0.25f);
            var d = b.to - b.from;
            float len = Mathf.Max(0.01f, d.magnitude);
            var dir = d / len;
            float flick = 0.8f + 0.2f * Mathf.PerlinNoise(b.age * 9f, b.id * 1.7f) + 0.08f * Mathf.Sin(b.age * 31f);
            var mid = (b.from + b.to) * 0.5f;
            var side = Vector3.Cross(dir, camPos - mid);
            side = side.sqrMagnitude > 1e-8f ? side.normalized : camRight;
            float amp = Mathf.Min(0.22f, len * 0.05f);
            var outerCol = A(b.color, 0.5f * fade);
            var coreCol = A(Lighten(b.color, 0.55f), 0.95f * fade);
            b.outer.Begin();
            b.core.Begin();
            for (int i = 0; i < BeamPoints; i++)
            {
                float t = i / (float)(BeamPoints - 1);
                float env = Mathf.Sin(Mathf.PI * t);
                float n = Mathf.PerlinNoise(t * 3.1f - b.age * 4.3f, b.id * 0.71f) - 0.5f;
                var p = b.from + d * t + side * (n * 2f * amp * env);
                float taper = Mathf.Lerp(0.55f, 1f, env);
                b.outer.AddFacing(p, dir, camPos, camRight, 0.27f * flick * taper, outerCol, t * len);
                b.core.AddFacing(p, dir, camPos, camRight, 0.075f * flick * taper, coreCol, t * len);
            }
            b.outer.End();
            b.core.End();
            b.a.t.SetPositionAndRotation(b.from, camRot);
            b.a.t.localScale = new Vector3(0.7f * flick, 0.7f * flick, 1f);
            b.b.t.SetPositionAndRotation(b.to, camRot);
            b.b.t.localScale = new Vector3(1.1f * flick, 1.1f * flick, 1f);
            b.a.sr.color = A(b.color, 0.6f * fade);
            b.b.sr.color = A(b.color, 0.75f * fade);
            if (b.light != null && b.light.h != null)
            {
                b.light.h.Position = b.to;
                b.light.h.Intensity = 1.1f * fade * flick;
            }
            b.sparkTimer -= dt;
            if (b.sparkTimer <= 0f && fade > 0.2f)
            {
                b.sparkTimer = 0.045f;
                var p = Emit(PresentationArt.SoftDot, Mat.Additive, b.from + d * R(0f, 0.2f) + side * R(-0.08f, 0.08f),
                    dir * (len / R(0.35f, 0.55f)), R(0.3f, 0.5f), R(0.1f, 0.16f), 0.05f,
                    A(Lighten(b.color, 0.4f), 0.9f), A(b.color, 0f));
                p.drag = 0.4f; p.bounce = false;
                if (rng.NextDouble() < 0.3)
                {
                    var q = Emit(PresentationArt.Spark, Mat.Additive, b.to + RandomDir(false) * 0.2f, RandomDir(false) * R(0.4f, 1f) + Up * R(0f, 1.2f),
                        0.3f, 0.25f, 0.05f, Lighten(b.color, 0.5f), A(b.color, 0f));
                    q.drag = 1f;
                }
            }
        }

        void UpdateBeams(float dt)
        {
            for (int i = beams.Count - 1; i >= 0; i--)
            {
                var b = beams[i];
                DrawBeam(b, dt);
                if (b.age >= b.dur)
                {
                    beams.RemoveAt(i);
                    RecycleBeam(b);
                }
            }
        }

        void RecycleBeam(BeamFx b)
        {
            ReleaseRibbon(b.outer);
            ReleaseRibbon(b.core);
            Release(b.a);
            Release(b.b);
            FreeLight(b.light);
            b.outer = null; b.core = null; b.a = null; b.b = null; b.light = null;
            freeBeams.Push(b);
        }

        void ClearBeams()
        {
            for (int i = 0; i < beams.Count; i++) RecycleBeam(beams[i]);
            beams.Clear();
        }

        // ================================================================== slashes

        const int SlashPoints = 22;
        const float SlashSweepTime = 0.13f;

        sealed class SlashFx
        {
            public FxRibbon ribbon;
            public Vector3 centre, u, w;
            public float radius, age, life;
        }

        readonly List<SlashFx> slashes = new List<SlashFx>();
        readonly Stack<SlashFx> freeSlashes = new Stack<SlashFx>();

        /// <summary>Weapon slash: a crescent swept around the hit point pos (3D), swinging towards dir.</summary>
        public static void Slash(Vector3 pos, Vector3 dir) => Get().DoSlash(pos, dir);

        void DoSlash(Vector3 pos, Vector3 dir)
        {
            var d = dir;
            if (d.sqrMagnitude < 1e-6f) d = new Vector3(camRight.x, camRight.y, 0f);
            if (d.sqrMagnitude < 1e-6f) d = Vector3.right;
            d.Normalize();
            var side = Vector3.Cross(d, Up);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : camRight;
            // a diagonal swing: the arc's plane holds the swing direction and a tilted side axis; random handedness
            float tilt = R(20f, 45f) * Mathf.Deg2Rad * (rng.NextDouble() < 0.5 ? 1f : -1f);
            float hand = rng.NextDouble() < 0.5 ? 1f : -1f;
            var s = freeSlashes.Count > 0 ? freeSlashes.Pop() : new SlashFx();
            s.radius = 0.75f;
            s.u = d;
            s.w = (side * Mathf.Cos(tilt) + Up * Mathf.Sin(tilt)) * hand;
            s.centre = pos - d * s.radius;   // the arc passes through pos at its middle
            s.age = 0f;
            s.life = 0.3f;
            s.ribbon = AcquireRibbon(Materials3D.AdditiveFor(FxModels.SlashStrip));
            slashes.Add(s);
            DrawSlash(s);

            var white = new Color(1f, 0.97f, 0.9f, 1f);
            Emit(PresentationArt.Glow, Mat.Additive, pos, Vector3.zero, 0.2f, 0.3f, 1.1f, A(white, 0.7f), A(white, 0f), Curve.EaseOut);
            for (int i = 0; i < 3; i++)
            {
                var v = (d + RandomDir(false) * 0.35f) * R(3f, 5f);
                var st = Emit(PresentationArt.BeamLine, Mat.Additive, pos + RandomDir(false) * 0.15f, v, 0.18f, 0.3f, 0.05f, new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0f));
                st.size0 = new Vector2(0.45f, 0.06f); st.size1 = new Vector2(0.1f, 0.02f);
                st.faceVel = true; st.drag = 4f; st.bounce = false;
            }
            AddLight(pos, new Color(1f, 0.95f, 0.85f), 0.8f, 2.4f, 0.2f);
        }

        void DrawSlash(SlashFx s)
        {
            const float th0 = -80f * Mathf.Deg2Rad, th1 = 80f * Mathf.Deg2Rad, span = 120f * Mathf.Deg2Rad;
            float k = Mathf.Clamp01(s.age / SlashSweepTime);
            k = 1f - (1f - k) * (1f - k);
            float head = Mathf.Lerp(th0, th1, k);
            float tail = Mathf.Max(th0, head - span);
            float fade = s.age <= SlashSweepTime ? 1f : Mathf.Clamp01(1f - (s.age - SlashSweepTime) / Mathf.Max(0.01f, s.life - SlashSweepTime));
            var r = s.ribbon;
            r.Begin();
            if (head - tail > 0.01f && fade > 0.001f)
            {
                for (int i = 0; i < SlashPoints; i++)
                {
                    float f = i / (float)(SlashPoints - 1);          // 0 tail … 1 head
                    float th = Mathf.Lerp(tail, head, f);
                    var radial = s.u * Mathf.Cos(th) + s.w * Mathf.Sin(th);
                    float width = 0.05f + 0.26f * Mathf.Pow(f, 0.8f);
                    float outer = s.radius + 0.06f;
                    var c = new Color(1f, 0.97f, 0.9f, Mathf.Pow(f, 1.2f) * fade);
                    r.Add(s.centre + radial * (outer - width * 0.5f), radial * (width * 0.5f), c, f);
                }
            }
            r.End();
        }

        void UpdateSlashes(float dt)
        {
            for (int i = slashes.Count - 1; i >= 0; i--)
            {
                var s = slashes[i];
                s.age += dt;
                if (s.age >= s.life)
                {
                    slashes.RemoveAt(i);
                    ReleaseRibbon(s.ribbon);
                    s.ribbon = null;
                    freeSlashes.Push(s);
                    continue;
                }
                DrawSlash(s);
            }
        }

        void ClearSlashes()
        {
            for (int i = 0; i < slashes.Count; i++) { ReleaseRibbon(slashes[i].ribbon); slashes[i].ribbon = null; freeSlashes.Push(slashes[i]); }
            slashes.Clear();
        }
    }
}
