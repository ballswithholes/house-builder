// Visual for any character or creature (presentation only — no rules logic), in 3D.
//
//   UnitView.Create(spriteKey, height, ringColor) → a procedurally modelled, rigged low-poly model (UnitRecipes) with
//   procedural animation (UnitAnimator): walk/run/trot cycles matched to the ground speed, idle life, weapon-specific
//   one-shots (attack, shoot, cast, hit, dodge, death, downed, revive), smooth turning, plus state visuals
//   (selection/target/active-turn rings on the ground, hover rim + gold outline, stealth dither, tints, polymorph sheep,
//   casting rune + hand glow + light) and a soft blob shadow.
//
// The GameObject's transform stays at the FEET GROUND POINT (z = 0; the camera follows it). The model lives on a child
// "Body" transform (World3D.Yaw + size scale) offset by lunges/hops; rings, rune and shadow lie flat on the ground.
// There is no per-unit Update: UnitViewSystem ticks every unit once per frame, then runs deferred callbacks.
using System;
using System.Collections.Generic;
using Lanternvale.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    public sealed class UnitView : MonoBehaviour
    {
        // ================================================================== registry

        static readonly List<UnitView> all = new List<UnitView>();
        /// <summary>Every live UnitView (used by picking, foreground fades and occluder fades).</summary>
        public static IReadOnlyList<UnitView> All => all;

        /// <summary>Front-most visible unit under a SCREEN position (pixels, bottom-left origin, e.g. GameInput.MousePosition).</summary>
        public static UnitView PickScreen(Vector2 screen, bool includeDead = false)
        {
            UnitView best = null;
            float bestDepth = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || !u.Visible || (!includeDead && u.IsDead)) continue;
                if (!u.HitTestScreen(screen, out float depth)) continue;
                if (best == null || depth < bestDepth) { best = u; bestDepth = depth; }
            }
            return best;
        }

        /// <summary>
        /// True when the body is under the screen position (the projected skeleton rect, padded a little);
        /// depth = distance from the camera (smaller = in front), for choosing the front-most of several hits.
        /// </summary>
        public bool HitTestScreen(Vector2 screen, out float depth)
        {
            depth = float.MaxValue;
            var b = body;
            if (b == null || !visible) return false;
            var cam = Cam;
            if (cam == null) return false;
            var center = CenterPosition;
            var cs = cam.WorldToScreenPoint(center);
            if (cs.z <= 0.01f) return false;
            // quick reject: far outside a generous circle around the body
            float pxPerM = PixelsPerMetre(cam, cs.z);
            float reach = (Height + 0.6f) * pxPerM;
            if (Mathf.Abs(screen.x - cs.x) > reach || Mathf.Abs(screen.y - cs.y) > reach) return false;

            float xmin = cs.x, xmax = cs.x, ymin = cs.y, ymax = cs.y;
            var bones = b.Bones;
            var pick = b.Model.PickBones;
            if (pick != null)
            {
                for (int i = 0; i < pick.Length; i++)
                {
                    int bi = pick[i];
                    if (bi < 0 || bi >= bones.Length) continue;
                    var p = cam.WorldToScreenPoint(bones[bi].position);
                    if (p.z <= 0.01f) continue;
                    if (p.x < xmin) xmin = p.x; if (p.x > xmax) xmax = p.x;
                    if (p.y < ymin) ymin = p.y; if (p.y > ymax) ymax = p.y;
                }
            }
            var head = cam.WorldToScreenPoint(HeadPosition);
            if (head.z > 0.01f)
            {
                if (head.x < xmin) xmin = head.x; if (head.x > xmax) xmax = head.x;
                if (head.y < ymin) ymin = head.y; if (head.y > ymax) ymax = head.y;
            }
            float pad = b.Model.PickPad * CurrentScale * pxPerM;
            if (screen.x < xmin - pad || screen.x > xmax + pad || screen.y < ymin - pad * 0.5f || screen.y > ymax + pad * 0.6f) return false;
            depth = Vector3.Distance(cam.transform.position, center);
            return true;
        }

        static float PixelsPerMetre(Camera cam, float dist)
        {
            if (cam.orthographic) return Screen.height / Mathf.Max(0.01f, cam.orthographicSize * 2f);
            return Screen.height / Mathf.Max(0.01f, 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
        }

        static Camera Cam
        {
            get
            {
                var rig = CameraRig.Instance;
                if (rig != null && rig.Cam != null) return rig.Cam;
                return PresentationHost.Cam;
            }
        }

        // timing constants for callers (seconds from the start of the animation)
        public const float AttackHitTime = 0.22f;
        public const float ShootReleaseTime = 0.2f;
        public const float CastReleaseTime = 0.45f;

        // ================================================================== public state

        public string SpriteKey { get; private set; }
        /// <summary>Height of the unit in metres (top of the head).</summary>
        public float Height { get; private set; }
        public Color RingColor { get; set; }
        /// <summary>Display name for nameplates (UI reads it; UnitView doesn't draw text).</summary>
        public string DisplayName = "";
        /// <summary>Free slot for the game flow (e.g. the rules engine's unit id/object).</summary>
        public object Tag;
        public int UnitId = int.MinValue;
        /// <summary>Tall props fade when this unit walks behind them (party: true; set false for ambient NPCs if desired).</summary>
        public bool FadesOccluders = true;
        /// <summary>Hovering units float above their shadow (automatic for wisps, spirits, owls).</summary>
        public bool Floating;

        public Vector2 FeetPosition => pos;
        public Vector2 Position => pos;
        /// <summary>±1: the sign of the facing direction's x (+1 = facing right).</summary>
        public int Facing => facingSign;
        public bool IsMoving => moving;
        public bool IsDead => pose == Pose.Dead;
        public bool IsDowned => pose == Pose.Lying;
        public bool Visible => visible;
        public bool IsSelected => selected;
        public bool IsHovered => hovered;
        public bool IsActiveTurn => activeTurn;
        public bool IsPolymorphed => polymorphed;

        /// <summary>Centre of the body in the world (Vector3, z = −height; follows the animated body, lowered when lying).</summary>
        public Vector3 CenterPosition
        {
            get
            {
                var b = body;
                if (b == null) return World3D.At(pos, Height * 0.5f);
                return b.BonePoint(b.Model.CenterBone, b.Model.CenterOffset);
            }
        }

        /// <summary>Top of the head in the world (floating text, status icons).</summary>
        public Vector3 HeadPosition
        {
            get
            {
                var b = body;
                if (b == null) return World3D.At(pos, Height);
                return b.BonePoint(b.Model.HeadBone, b.Model.HeadTop);
            }
        }

        /// <summary>Where a nameplate/health bar should be anchored (a little above the head).</summary>
        public Vector3 NameplatePosition => HeadPosition + World3D.Up * 0.28f;

        /// <summary>Ground footprint rect (kept for compatibility; picking uses HitTestScreen).</summary>
        public Rect Bounds
        {
            get
            {
                float r = FootRadius;
                return new Rect(pos.x - r, pos.y - r, r * 2f, r * 2f);
            }
        }

        // ================================================================== internals

        enum Pose { Standing, Lying, Dead }

        const float FacingBias = 15f;   // SetFacing turns the face a little towards the camera (3/4 view)

        UnitModel model;
        UnitBody baseBody, sheepBody, body;
        float scale = 1f;            // model → world
        float FootRadius => (body != null ? body.Model.Radius * CurrentScale : 0.35f);
        float CurrentScale => body == baseBody ? scale : 1f;

        MeshRenderer shadow;
        MaterialPropertyBlock shadowBlock;
        float lastShadowA = -1f;
        SpriteRenderer ring, ripple, rune, glow;
        SceneLighting.PointLight castLight;
        static Material spriteMat;

        Vector2 pos, prevPos;
        float yaw = 90f + FacingBias, targetYaw = 90f + FacingBias;
        int facingSign = 1;
        bool visible = true;
        bool ticked;
        float time, spawnT;

        // movement
        readonly List<Vector2> path = new List<Vector2>();
        int pathIndex;
        float moveSpeed = 3.2f;
        bool moving;
        Action onArrive;
        float pendingDist;

        // one-shot action
        UnitAction action;
        float actionT, actionDur;
        Vector2 actionDir;
        Color actionColor;
        Vector2 kbFrom, kbTo;
        int dodgeSide = 1;

        float hitT = -1f;

        Pose pose = Pose.Standing;
        float deathFade = 1f;

        // states
        bool selected, hovered, activeTurn, stealthed, polymorphed, casting;
        Color? targetColor;
        Color tint = Color.white;
        Color castColor = Color.white;
        float castProgress;
        float stealthFade = 1f;

        // ================================================================== creation

        /// <summary>
        /// Creates a unit visual. height ≤ 0 uses the model's natural height; pass CreatureDef.size for creatures.
        /// </summary>
        public static UnitView Create(string spriteKey, float height, Color ringColor)
        {
            ArtLibrary.Init();
            var go = new GameObject("Unit " + spriteKey);
            var u = go.AddComponent<UnitView>();
            u.Init(spriteKey, height, ringColor);
            return u;
        }

        void Init(string key, float height, Color ringColor)
        {
            SpriteKey = key ?? "";
            RingColor = ringColor;
            requestedHeight = height;
            SceneLighting.Ensure();
            BuildBase();
            shadow = MeshCache.AddShadow(transform, 0.4f, 0.4f);
            shadow.name = "Shadow";
            shadowBlock = new MaterialPropertyBlock();
            // rings draw after ground decals (same sorting layer, higher order)
            ring = NewGroundSprite("Ring", PresentationArt.UnitRing, SpriteMat, 2);
            ripple = NewGroundSprite("Ripple", PresentationArt.UnitRing, SpriteMat, 2);
            all.Add(this);
            UnitViewSystem.Ensure();
            Teleport(Vector2.zero);
            Animate(0f);
        }

        float requestedHeight;

        void BuildBase()
        {
            model = UnitModels.Get(SpriteKey);
            if (model == null) return;
            scale = requestedHeight > 0f ? requestedHeight / Mathf.Max(0.05f, model.Height) : 1f;
            Height = model.Height * scale;
            Floating = model.FloatHeight > 0f;
            baseBody = UnitBody.Create(model, transform);
            if (!polymorphed) body = baseBody;
            else baseBody.SetActive(false);
            ApplyBodyTransform(baseBody, Vector3.zero, 1f);
        }

        static Material SpriteMat
        {
            get
            {
                if (spriteMat == null) spriteMat = new Material(Materials3D.Find("Sprites/Default")) { name = "LV Unit Rings", hideFlags = HideFlags.DontSave };
                return spriteMat;
            }
        }

        SpriteRenderer NewGroundSprite(string name, Sprite sprite, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            go.transform.localRotation = Quaternion.identity;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = mat;
            sr.sortingOrder = order;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            sr.enabled = false;
            return sr;
        }

        SpriteRenderer EnsureRune()
        {
            if (rune != null) return rune;
            rune = NewGroundSprite("Cast Rune", PresentationArt.RuneCircle, Materials3D.Additive, 3);
            rune.transform.localPosition = new Vector3(0f, 0f, -0.016f);
            return rune;
        }

        SpriteRenderer EnsureGlow()
        {
            if (glow != null) return glow;
            var go = new GameObject("Hand Glow");
            go.transform.SetParent(transform, false);
            glow = go.AddComponent<SpriteRenderer>();
            glow.sprite = PresentationArt.SoftDot;
            glow.sharedMaterial = Materials3D.Additive;
            glow.shadowCastingMode = ShadowCastingMode.Off;
            glow.receiveShadows = false;
            glow.sortingOrder = 5;
            glow.enabled = false;
            return glow;
        }

        // ================================================================== movement

        /// <summary>Instantly places the unit (feet) at p.</summary>
        public void Teleport(Vector2 p)
        {
            StopMoving();
            pos = p;
            prevPos = p;
            transform.position = new Vector3(p.x, p.y, 0f);
        }

        /// <summary>
        /// Walks along path (world points; a first point equal to the current position is skipped)
        /// at speed m/s, then calls onArrive. A new MoveAlong/Teleport/StopMoving replaces the move
        /// without calling the previous callback.
        /// </summary>
        public void MoveAlong(List<Vector2> points, float speed, Action arrive = null)
        {
            path.Clear();
            if (points != null)
                for (int i = 0; i < points.Count; i++)
                    if (path.Count > 0 || (points[i] - pos).sqrMagnitude > 1e-4f) path.Add(points[i]);
            BeginMove(speed, arrive);
        }

        /// <summary>MoveAlong for rules-engine paths (NavPath.Points).</summary>
        public void MoveAlong(IReadOnlyList<Vec2> points, float speed, Action arrive = null)
        {
            path.Clear();
            if (points != null)
                for (int i = 0; i < points.Count; i++)
                {
                    var p = new Vector2(points[i].x, points[i].y);
                    if (path.Count > 0 || (p - pos).sqrMagnitude > 1e-4f) path.Add(p);
                }
            BeginMove(speed, arrive);
        }

        void BeginMove(float speed, Action arrive)
        {
            moveSpeed = speed > 0f ? speed : 3.2f;
            onArrive = arrive;
            pathIndex = 0;
            moving = path.Count > 0;
            if (!moving && arrive != null) UnitViewSystem.Defer(arrive);
            if (moving && action == UnitAction.Knockback) action = UnitAction.None;
        }

        public void StopMoving()
        {
            moving = false;
            path.Clear();
            onArrive = null;
        }

        /// <summary>Remaining path length in metres.</summary>
        public float RemainingPathLength()
        {
            if (!moving) return 0f;
            float d = 0f;
            var p = pos;
            for (int i = pathIndex; i < path.Count; i++) { d += Vector2.Distance(p, path[i]); p = path[i]; }
            return d;
        }

        /// <summary>Pushed to `to` with a small hop. Returns the duration.</summary>
        public float Knockback(Vector2 to, float duration = 0.35f)
        {
            StopMoving();
            kbFrom = pos;
            kbTo = to;
            return Begin(UnitAction.Knockback, Mathf.Max(0.1f, duration), Vector2.zero, Color.white);
        }

        /// <summary>Turns (smoothly) to face a ground point.</summary>
        public void FaceTowards(Vector2 target)
        {
            var d = target - pos;
            if (d.sqrMagnitude < 0.0004f) return;
            SetYawTarget(World3D.YawOf(d));
        }

        /// <summary>+1 = face right (+X), −1 = face left (−X); turned a little towards the camera.</summary>
        public void SetFacing(int dir)
        {
            SetYawTarget(dir >= 0 ? 90f + FacingBias : -(90f + FacingBias));
        }

        void SetYawTarget(float y)
        {
            targetYaw = Mathf.Repeat(y + 180f, 360f) - 180f;
            float sx = Mathf.Sin(targetYaw * Mathf.Deg2Rad);
            if (Mathf.Abs(sx) > 0.05f) facingSign = sx > 0f ? 1 : -1;
            if (!ticked) yaw = targetYaw;
        }

        // ================================================================== one-shot animations

        float Begin(UnitAction a, float dur, Vector2 dir, Color c)
        {
            if (pose == Pose.Dead && a != UnitAction.Revive) return 0f;
            action = a;
            actionT = 0f;
            actionDur = dur;
            actionDir = dir.sqrMagnitude > 1e-6f ? dir.normalized : World3D.DirOf(targetYaw);
            actionColor = c;
            return dur;
        }

        /// <summary>Weapon swing/lunge towards a point. Returns the duration; the blow lands at AttackHitTime.</summary>
        public float PlayAttack(Vector2 towards)
        {
            FaceTowards(towards);
            return Begin(UnitAction.Attack, 0.5f, towards - pos, Color.white);
        }

        /// <summary>Bow draw &amp; release / throw / staff point. Release at ShootReleaseTime.</summary>
        public float PlayShoot() => Begin(UnitAction.Shoot, 0.45f, World3D.DirOf(targetYaw), Color.white);

        /// <summary>Bow draw &amp; release / throw towards a point. Release at ShootReleaseTime.</summary>
        public float PlayShoot(Vector2 towards)
        {
            FaceTowards(towards);
            return Begin(UnitAction.Shoot, 0.45f, towards - pos, Color.white);
        }

        /// <summary>Arms raise, hand/staff glows in the school colour. Release at CastReleaseTime.</summary>
        public float PlayCast(Color schoolColor)
        {
            EnsureGlow();
            EnsureRune();
            return Begin(UnitAction.Cast, 0.7f, World3D.DirOf(targetYaw), schoolColor);
        }

        /// <summary>Recoil + white flash (overlaps other animations).</summary>
        public float PlayHit()
        {
            if (pose == Pose.Dead) return 0f;
            hitT = 0f;
            return 0.3f;
        }

        /// <summary>A quick side hop (alternating sides).</summary>
        public float PlayDodge()
        {
            dodgeSide = -dodgeSide;
            return Begin(UnitAction.Dodge, 0.4f, World3D.DirOf(targetYaw), Color.white);
        }

        /// <summary>Crumples, falls and dissolves (unit stays registered but invisible; Dispose it when done).</summary>
        public float PlayDeath()
        {
            StopMoving();
            hitT = 0f;
            casting = false;
            float d = Begin(UnitAction.Death, 1.2f, Vector2.zero, Color.white);
            pose = Pose.Dead;
            UpdateOverlays();
            return d;
        }

        /// <summary>Lies down (party member at 0 HP). Stays down until PlayRevive.</summary>
        public float PlayDowned()
        {
            StopMoving();
            float d = Begin(UnitAction.Fall, 0.6f, Vector2.zero, Color.white);
            pose = Pose.Lying;
            return d;
        }

        /// <summary>Gets back up with a warm glow (help up / resurrection).</summary>
        public float PlayRevive()
        {
            pose = Pose.Standing;
            deathFade = 1f;
            EnsureGlow();
            float d = Begin(UnitAction.Revive, 0.8f, Vector2.zero, new Color(1f, 0.88f, 0.55f));
            UpdateOverlays();
            return d;
        }

        // ================================================================== state visuals

        public void SetSelected(bool on) { selected = on; UpdateOverlays(); }
        public void SetHovered(bool on) { hovered = on; UpdateOverlays(); }
        /// <summary>Highlights the unit as a valid target in a colour (pulsing ring + outline); null clears.</summary>
        public void SetTargetable(Color? color) { targetColor = color; UpdateOverlays(); }
        public void SetActiveTurn(bool on) { activeTurn = on; UpdateOverlays(); }
        /// <summary>Stealth: dithered to ~45 %.</summary>
        public void SetStealthed(bool on) { stealthed = on; }
        /// <summary>Body tint (frozen blue, poisoned green…); Color.white clears.</summary>
        public void SetTint(Color c) { tint = c; }

        public void SetVisible(bool on)
        {
            visible = on;
            if (body != null) body.SetActive(on);
            if (shadow != null) shadow.enabled = on;
            if (!on)
            {
                if (glow != null) glow.enabled = false;
                if (rune != null) rune.enabled = false;
                ReleaseLight();
            }
            UpdateOverlays();
        }

        /// <summary>Swaps the body to a fluffy sheep (polymorph) and back.</summary>
        public void SetPolymorphed(bool on)
        {
            if (polymorphed == on) return;
            polymorphed = on;
            if (on)
            {
                if (sheepBody == null)
                {
                    var sm = UnitModels.Get("sheep");
                    if (sm != null) sheepBody = UnitBody.Create(sm, transform, "Sheep");
                }
                if (sheepBody != null)
                {
                    if (baseBody != null) baseBody.SetActive(false);
                    body = sheepBody;
                    sheepBody.SetActive(visible);
                }
                FxSystem.Sparkles(CenterPosition, Ui.SchoolColor(Lanternvale.Data.School.Arcane), 10);
            }
            else
            {
                if (sheepBody != null) sheepBody.SetActive(false);
                body = baseBody;
                if (baseBody != null) baseBody.SetActive(visible);
                FxSystem.Sparkles(CenterPosition, Ui.SchoolColor(Lanternvale.Data.School.Arcane), 8);
            }
            Animate(0f);
        }

        /// <summary>Rebuilds the model from another key (shapeshift, disguise).</summary>
        public void SetSprite(string spriteKey)
        {
            spriteKey = spriteKey ?? "";
            if (spriteKey == SpriteKey && baseBody != null) return;
            SpriteKey = spriteKey;
            if (baseBody != null) baseBody.Destroy();
            baseBody = null;
            if (!polymorphed) body = null;
            BuildBase();
            if (baseBody != null) baseBody.SetActive(visible && !polymorphed);
            Animate(0f);
        }

        /// <summary>Casting rune + hand glow + light growing with progress 0..1 (pending/channelled casts).</summary>
        public void SetCasting(Color color, float progress)
        {
            casting = true;
            castColor = color;
            castProgress = Mathf.Clamp01(progress);
            EnsureGlow();
            EnsureRune();
        }

        public void StopCasting()
        {
            casting = false;
            if (action != UnitAction.Cast && action != UnitAction.Revive) ReleaseLight();
        }

        int CurrentRingState()
        {
            if (targetColor.HasValue) return 4;
            if (activeTurn) return 3;
            if (selected) return 2;
            if (hovered) return 1;
            return 0;
        }

        void UpdateOverlays()
        {
            if (ring == null) return;
            bool show = visible && pose != Pose.Dead && CurrentRingState() > 0;
            ring.enabled = show;
            ripple.enabled = show && activeTurn;
        }

        // ================================================================== per-frame

        internal void Tick(float dt)
        {
            time += dt;
            spawnT += dt;
            float moved = 0f;
            Vector2 vel = Vector2.zero;

            // ---- path movement
            if (moving)
            {
                var start = pos;
                float step = moveSpeed * dt;
                while (step > 0f && pathIndex < path.Count)
                {
                    var target = path[pathIndex];
                    var d = target - pos;
                    float len = d.magnitude;
                    if (len <= step) { pos = target; step -= len; pathIndex++; }
                    else { pos += d / len * step; step = 0f; }
                }
                var delta = pos - start;
                moved = delta.magnitude;
                if (moved > 1e-5f)
                {
                    if (dt > 0f) vel = delta / dt;
                    SetYawTarget(World3D.YawOf(delta));
                }
                if (pathIndex >= path.Count)
                {
                    moving = false;
                    path.Clear();
                    var cb = onArrive;
                    onArrive = null;
                    if (cb != null) UnitViewSystem.Defer(cb);
                }
            }

            // ---- one-shot action timeline
            if (action != UnitAction.None)
            {
                actionT += dt;
                if (action == UnitAction.Knockback)
                {
                    float u = Mathf.Clamp01(actionT / actionDur);
                    pos = Vector2.Lerp(kbFrom, kbTo, 1f - (1f - u) * (1f - u));
                }
                if (pose == Pose.Dead && action == UnitAction.Death)
                    deathFade = 1f - Mathf.Clamp01((actionT - 0.6f) / 0.6f);
                if (actionT >= actionDur)
                {
                    if (action == UnitAction.Knockback) pos = kbTo;
                    if (action == UnitAction.Death) deathFade = 0f;
                    action = UnitAction.None;
                }
            }
            if (hitT >= 0f)
            {
                hitT += dt;
                if (hitT > 0.4f) hitT = -1f;
            }
            prevPos = pos;
            transform.position = new Vector3(pos.x, pos.y, 0f);

            // ---- turning (smooth, never a snap)
            float before = yaw;
            float delta2 = Mathf.DeltaAngle(yaw, targetYaw);
            float rate = model != null ? model.TurnRate : 720f;
            if (action != UnitAction.None) rate *= 1.6f;
            float stepYaw = delta2 * (1f - Mathf.Exp(-dt * 13f));
            float maxStep = rate * dt;
            yaw += Mathf.Clamp(stepYaw, -maxStep, maxStep);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, targetYaw)) < 0.05f) yaw = targetYaw;
            float yawRate = dt > 0f ? Mathf.DeltaAngle(before, yaw) / dt : 0f;

            Animate(dt, moved, vel, yawRate);
            ticked = true;
        }

        void Animate(float dt, float moved = 0f, Vector2 vel = default, float yawRate = 0f)
        {
            var b = body;
            if (b == null) return;
            float sc = CurrentScale;
            var m = b.Model;

            // world → model space
            var fwd = World3D.DirOf(yaw);
            var right = new Vector2(fwd.y, -fwd.x);
            var inp = new UnitAnimInput
            {
                Dt = dt,
                Time = time,
                Velocity = new Vector3(Vector2.Dot(vel, right), 0f, Vector2.Dot(vel, fwd)) / sc,
                MoveDist = moved / sc,
                YawRate = yawRate,
                Action = action,
                ActionT = actionT,
                ActionDur = actionDur,
                ActionDir = new Vector3(Vector2.Dot(actionDir, right), 0f, Vector2.Dot(actionDir, fwd)),
                DodgeSide = dodgeSide,
                HitT = hitT,
                Lying = pose == Pose.Lying,
                Dead = pose == Pose.Dead,
                Casting = casting,
                CastProgress = castProgress,
                SpawnT = spawnT,
            };
            if (polymorphed && (action == UnitAction.Attack || action == UnitAction.Shoot || action == UnitAction.Cast))
                inp.Action = UnitAction.None;   // a sheep just bleats
            b.Anim.Tick(inp);

            // ---- body transform
            var off = b.Anim.BodyOffset;
            if (Floating && m.FloatHeight <= 0f && pose == Pose.Standing)
                off.y += (0.35f + Mathf.Sin(time * 1.7f) * 0.08f) / Mathf.Max(0.05f, sc);
            ApplyBodyTransform(b, off, b.Anim.ScalePop);

            UpdateLook(b, dt);
            UpdateGround(b, sc);
            UpdateCastFx(b, sc);

            // ---- footstep dust (not every step)
            if (b.Anim.FootDown && visible && pose == Pose.Standing && m.Dust && moveSpeed > 0.8f && m.FloatHeight <= 0f && !Floating)
            {
                stepDust++;
                if ((stepDust % 3) == 0 || moveSpeed > 4f && (stepDust % 2) == 0)
                {
                    var p = b.Root.TransformPoint(b.Anim.FootDownPos);
                    FxSystem.Puff(new Vector3(p.x, p.y, 0f), m.DustColor, Mathf.Clamp(0.32f * sc * m.Height / 1.75f + 0.12f, 0.18f, 1.2f));
                }
            }
        }

        int stepDust;

        void ApplyBodyTransform(UnitBody b, Vector3 offsetModel, float pop)
        {
            float sc = b == baseBody ? scale : 1f;
            var rot = World3D.Yaw(yaw);
            b.Root.localPosition = rot * (offsetModel * sc);
            b.Root.localRotation = rot;
            float s = sc * pop;
            b.Root.localScale = new Vector3(s, s, s);
        }

        // ---------------------------------------------------------------- looks

        void UpdateLook(UnitBody b, float dt)
        {
            var look = b.Look;
            var m = b.Model;

            // tint (lying party members are a little dimmer)
            var c = tint;
            float tintFade = Mathf.Clamp01(c.a <= 0f ? 1f : c.a);
            c.a = 1f;
            if (pose == Pose.Lying) c = new Color(c.r * 0.85f, c.g * 0.85f, c.b * 0.9f, 1f);
            look.Tint = c;

            // hit flash (white), revive glow (warm)
            float flashA = 0f;
            Color flashC = new Color(1f, 1f, 0.96f, 0f);
            if (hitT >= 0f) flashA = Mathf.Clamp01(1f - hitT / 0.16f) * 0.85f;
            if (action == UnitAction.Revive)
            {
                float k = Mathf.Sin(Mathf.PI * Mathf.Clamp01(actionT / actionDur)) * 0.45f;
                if (k > flashA) { flashA = k; flashC = actionColor; }
            }
            if (pose == Pose.Dead && action == UnitAction.Death)
            {
                float k = Mathf.Clamp01((actionT - 0.55f) / 0.5f) * 0.35f;
                if (k > flashA) { flashA = k; flashC = new Color(0.85f, 0.82f, 0.95f); }
            }
            flashC.a = flashA;
            look.Flash = flashC;

            // dither fade: stealth, death dissolve, translucent spirits
            stealthFade = Mathf.MoveTowards(stealthFade, stealthed ? 0.45f : 1f, dt * 2.5f);
            look.Fade = Mathf.Clamp01(m.BaseFade * stealthFade * deathFade * tintFade);

            // hover / target highlight
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 6f);
            bool alive = pose != Pose.Dead;
            if (targetColor.HasValue && alive)
            {
                var tc = targetColor.Value;
                look.OutlineColor = Color.Lerp(new Color(tc.r, tc.g, tc.b, 1f), Color.white, 0.25f * pulse);
                look.OutlineWidth = 2.6f + 0.8f * pulse;
                look.Rim = (hovered ? 0.55f : 0.3f) + 0.15f * pulse;
            }
            else if (hovered && alive)
            {
                look.OutlineColor = new Color(1f, 0.84f, 0.42f, 1f);
                look.OutlineWidth = 2.9f + 0.3f * pulse;
                look.Rim = 0.5f + 0.12f * pulse;
            }
            else
            {
                look.OutlineColor = Materials3D.Ink;
                look.OutlineWidth = 1.9f;
                look.Rim = (selected || activeTurn) && alive ? 0.12f : 0f;
            }
            b.ApplyLook();
        }

        // ---------------------------------------------------------------- shadow & rings

        void UpdateGround(UnitBody b, float sc)
        {
            var m = b.Model;
            float r = m.Radius * sc;
            float lie = b.Anim.LieAmount;
            // body offset on the ground (lunges, dodges) — the shadow follows the body
            var bo = b.Root.localPosition;
            float hover = Mathf.Max(0f, -bo.z) + (m.FloatHeight > 0f ? m.FloatHeight * sc : 0f);
            if (shadow != null)
            {
                bool on = visible;
                if (shadow.enabled != on) shadow.enabled = on;
                if (on)
                {
                    float len = (m.HalfLength * sc + r) * (1f + lie * 0.5f);
                    float wid = r * (1f + (m.Rig == UnitRigKind.Quad ? lie * 0.4f : 0f));
                    if (m.Rig == UnitRigKind.Biped && lie > 0f) len = r + lie * Height * 0.42f;
                    float shrink = 1f / (1f + hover * 0.35f);
                    var st = shadow.transform;
                    var fwd = World3D.DirOf(yaw);
                    var center = new Vector2(bo.x, bo.y);
                    if (m.Rig == UnitRigKind.Biped && lie > 0f) center -= fwd * 0.0f;
                    st.localPosition = new Vector3(center.x, center.y, -0.004f);
                    st.localRotation = Quaternion.AngleAxis(Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg, Vector3.forward);
                    st.localScale = new Vector3(len * 2f * shrink, wid * 2f * shrink, 1f);
                    float a = 0.4f * deathFade * (stealthed ? 0.55f : 1f) * shrink * Mathf.Lerp(1f, 0.85f, lie);
                    if (Mathf.Abs(a - lastShadowA) > 0.004f)
                    {
                        lastShadowA = a;
                        shadowBlock.SetColor(Materials3D.ColorId, new Color(0.12f, 0.09f, 0.16f, a));
                        shadow.SetPropertyBlock(shadowBlock);
                    }
                }
            }

            if (ring != null && ring.enabled)
            {
                float rr = Mathf.Max(0.42f, (r + m.HalfLength * sc * 0.6f) * 1.3f);
                int st = CurrentRingState();
                Color rc;
                float a;
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 6f);
                float k = 1f;
                switch (st)
                {
                    case 4: rc = targetColor.Value; a = 0.6f + 0.35f * pulse; k = 1f + 0.05f * pulse; break;
                    case 3: rc = RingColor; a = 0.95f; break;
                    case 2: rc = RingColor; a = 0.85f; break;
                    default: rc = new Color(1f, 0.96f, 0.86f); a = 0.5f; break;
                }
                ring.color = new Color(rc.r, rc.g, rc.b, a * (stealthed ? 0.6f : 1f));
                ring.transform.localScale = new Vector3(rr * 2f * k, rr * 2f * k, 1f);
                if (ripple.enabled)
                {
                    float u = Mathf.Repeat(time / 1.3f, 1f);
                    float rk = 1f + 0.6f * u;
                    ripple.transform.localScale = new Vector3(rr * 2f * rk, rr * 2f * rk, 1f);
                    ripple.color = new Color(RingColor.r, RingColor.g, RingColor.b, 0.7f * (1f - u) * (1f - u));
                }
            }
        }

        // ---------------------------------------------------------------- casting: rune, hand glow, light

        void UpdateCastFx(UnitBody b, float sc)
        {
            float glowA = 0f, glowSize = 0.5f, runeA = 0f;
            Color gc = castColor;
            var m = b.Model;
            if (action == UnitAction.Cast)
            {
                float t = actionT;
                gc = actionColor;
                glowA = t < 0.45f ? 0.25f + 0.65f * Ease(t / 0.45f) : 0.9f * (1f - Ease((t - 0.45f) / 0.25f));
                glowSize = t < 0.45f ? 0.35f + 0.45f * Ease(t / 0.45f) : 0.8f + 0.6f * Ease((t - 0.45f) / 0.25f);
                runeA = t < 0.45f ? 0.75f * Ease(t / 0.45f) : 0.75f * (1f - Ease((t - 0.45f) / 0.25f));
            }
            if (casting && pose != Pose.Dead)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 6f);
                float ga = 0.35f + 0.45f * castProgress + 0.1f * pulse;
                if (ga > glowA) { glowA = ga; gc = castColor; glowSize = 0.4f + 0.45f * castProgress + 0.08f * pulse; }
                runeA = Mathf.Max(runeA, 0.35f + 0.4f * castProgress);
            }
            bool reviving = action == UnitAction.Revive;
            if (reviving)
            {
                float u = Mathf.Clamp01(actionT / actionDur);
                float ra = 0.8f * Mathf.Sin(Mathf.PI * u);
                if (ra > glowA) { glowA = ra; gc = actionColor; glowSize = 0.9f + 0.7f * u; }
            }
            float vis = visible ? 1f : 0f;
            glowA *= vis * deathFade;
            runeA *= vis;
            float k = Mathf.Clamp(Height / 1.75f, 0.6f, 2.2f);

            if (glow != null)
            {
                bool on = glowA > 0.01f;
                if (glow.enabled != on) glow.enabled = on;
                if (on)
                {
                    Vector3 p = reviving ? CenterPosition : b.BonePoint(m.CastBone, m.CastOffset);
                    var gt = glow.transform;
                    gt.position = p;
                    var cam = Cam;
                    if (cam != null) gt.rotation = cam.transform.rotation;
                    float gs = glowSize * k;
                    gt.localScale = new Vector3(gs, gs, gs);
                    glow.color = new Color(gc.r, gc.g, gc.b, glowA);
                }
            }
            // small point light at the hand
            if (glowA > 0.02f)
            {
                if (castLight != null && !castLight.IsRegistered) castLight = null;
                Vector3 p = glow != null ? glow.transform.position : CenterPosition;
                if (castLight == null) castLight = SceneLighting.Add(p, gc, 0f, 3f);
                castLight.Enabled = true;
                castLight.Position = p;
                castLight.Color = gc;
                castLight.Intensity = 1.3f * glowA;
                castLight.Range = 2.6f * k + 1.2f * glowSize;
            }
            else ReleaseLight();

            if (rune != null)
            {
                bool on = runeA > 0.01f;
                if (rune.enabled != on) rune.enabled = on;
                if (on)
                {
                    var rc = action == UnitAction.Cast ? actionColor : castColor;
                    rune.color = new Color(rc.r, rc.g, rc.b, runeA);
                    float d = Mathf.Max(1.1f, (m.Radius * sc + m.HalfLength * sc * 0.5f) * 3.6f) * (1f + 0.03f * Mathf.Sin(time * 3f));
                    var rt = rune.transform;
                    rt.localScale = new Vector3(d, d, 1f);
                    rt.localRotation = Quaternion.AngleAxis(time * 24f, Vector3.forward);
                }
            }
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // ================================================================== lifetime

        /// <summary>Unregisters and destroys the unit.</summary>
        public void Dispose()
        {
            all.Remove(this);
            ReleaseLight();
            if (this != null) Destroy(gameObject);
        }

        void ReleaseLight()
        {
            if (castLight != null) SceneLighting.Remove(castLight);
            castLight = null;
        }

        void OnDestroy()
        {
            all.Remove(this);
            ReleaseLight();
        }

        internal static void TickAll(float dt)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null) { all.RemoveAt(i); i--; continue; }
                if (!u.isActiveAndEnabled) continue;
                u.Tick(dt);
            }
        }
    }

    /// <summary>Ticks every UnitView once per frame and runs deferred callbacks afterwards.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class UnitViewSystem : MonoBehaviour
    {
        static UnitViewSystem instance;
        static readonly List<Action> deferred = new List<Action>();
        static readonly List<Action> running = new List<Action>();

        public static void Ensure()
        {
            if (instance == null) instance = PresentationHost.Ensure<UnitViewSystem>();
        }

        internal static void Defer(Action a) { if (a != null) deferred.Add(a); }

        void Awake() { instance = this; }

        void Update()
        {
            UnitView.TickAll(Time.deltaTime);
            if (deferred.Count == 0) return;
            running.Clear();
            running.AddRange(deferred);
            deferred.Clear();
            for (int i = 0; i < running.Count; i++)
            {
                try { running[i](); }
                catch (Exception e) { Debug.LogException(e); }
            }
            running.Clear();
        }
    }
}
