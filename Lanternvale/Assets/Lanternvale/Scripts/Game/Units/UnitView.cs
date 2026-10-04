// Visual for any character or creature (presentation only — no rules logic).
//
//   UnitView.Create(spriteKey, height, ringColor) → y-sorted sprite with a soft ground shadow,
//   idle breathing, walking bob + facing, path movement, one-shot animations (attack, shoot, cast,
//   hit, dodge, death, downed, revive) and state visuals (selection/target/active-turn ring,
//   hover outline, stealth, tints, polymorph, cast glow).
//
// There is no per-unit Update: UnitViewSystem ticks every registered unit once per frame and only
// re-sorts a unit when it moved. Callbacks (MoveAlong onArrive) run after all units were ticked.
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

        /// <summary>Front-most visible unit whose body contains the world point (or null).</summary>
        public static UnitView Pick(Vector2 world, bool includeDead = false)
        {
            UnitView best = null;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || !u.Visible || (!includeDead && u.IsDead)) continue;
                if (!u.Bounds.Contains(world)) continue;
                if (best == null || u.pos.y < best.pos.y) best = u;
            }
            return best;
        }

        // timing constants for callers (seconds from the start of the animation)
        public const float AttackHitTime = 0.22f;
        public const float ShootReleaseTime = 0.2f;
        public const float CastReleaseTime = 0.45f;

        // ================================================================== public state

        public string SpriteKey { get; private set; }
        /// <summary>World height of the sprite in metres.</summary>
        public float Height { get; private set; }
        public Color RingColor { get; set; }
        /// <summary>Display name for nameplates (UI reads it; UnitView doesn't draw text).</summary>
        public string DisplayName = "";
        /// <summary>Free slot for the game flow (e.g. the rules engine's unit id/object).</summary>
        public object Tag;
        public int UnitId = int.MinValue;
        /// <summary>Tall props fade when this unit walks behind them (party: true; set false for ambient NPCs if desired).</summary>
        public bool FadesOccluders = true;
        /// <summary>Hovering creatures (wisps) float above their shadow.</summary>
        public bool Floating;

        public Vector2 FeetPosition => pos;
        public Vector2 Position => pos;
        public int Facing => facing;
        public bool IsMoving => moving;
        public bool IsDead => pose == Pose.Dead;
        public bool IsDowned => pose == Pose.Lying;
        public bool Visible => visible;
        public bool IsSelected => selected;
        public bool IsHovered => hovered;
        public bool IsActiveTurn => activeTurn;
        public bool IsPolymorphed => polymorphed;

        /// <summary>Approximate centre of the body (follows bob/hover; lowered when lying).</summary>
        public Vector2 CenterPosition
        {
            get
            {
                float lie = poseAngle01;
                return pos + new Vector2(offset.x, offset.y + Mathf.Lerp(CurrentHeight * 0.5f, CurrentHeight * 0.18f, lie));
            }
        }

        /// <summary>Top of the head (nameplates, floating text, status icons).</summary>
        public Vector2 HeadPosition
        {
            get
            {
                float lie = poseAngle01;
                return pos + new Vector2(offset.x, offset.y + Mathf.Lerp(CurrentHeight * 0.97f, CurrentHeight * 0.35f, lie));
            }
        }

        /// <summary>Where a nameplate/health bar should be anchored (a little above the head).</summary>
        public Vector2 NameplatePosition => HeadPosition + new Vector2(0f, 0.28f);

        /// <summary>World rect of the body for mouse picking.</summary>
        public Rect Bounds
        {
            get
            {
                if (body == null) return new Rect(pos, Vector2.zero);
                var b = body.bounds;
                float w = b.size.x, h = b.size.y;
                float insetX = w * 0.16f;
                return Rect.MinMaxRect(b.min.x + insetX, b.min.y, b.max.x - insetX, b.max.y - h * 0.03f);
            }
        }

        // ================================================================== internals

        enum Pose { Standing, Lying, Dead }
        enum Anim { None, Attack, Shoot, Cast, Dodge, Knockback, Fall, Revive, Death }

        Transform visual;            // SortingGroup root: offsets, rotation, squash
        SortingGroup group;
        SpriteRenderer body, outline, flash, glow, shadow, ring, ripple, castCircle;
        Sprite baseSprite;
        float baseSpriteHeight;      // bounds height of baseSprite (local units)
        float spriteScale = 1f;      // visual scale so the sprite is Height tall
        float shadowW, shadowH;
        Vector2 shadowBounds, ringBounds, circleBounds;   // cached sprite bounds (avoid per-frame native calls)
        static int lastWarmFrame = -1;
        Silhouette silhouette;
        bool silhouetteTried;

        Vector2 pos;
        Vector2 offset;              // last computed visual offset (for anchors)
        int facing = 1;
        bool visible = true;
        float phase;                 // per-unit random phase
        float time;

        // movement
        readonly List<Vector2> path = new List<Vector2>();
        int pathIndex;
        float moveSpeed = 3.2f;
        bool moving;
        System.Action onArrive;
        float walkPhase, walkBlend;

        // one-shot action
        Anim action;
        float actionT, actionDur;
        Vector2 actionDir;
        Color actionColor;
        Vector2 kbFrom, kbTo;

        // hit channel
        float hitT = -1f;

        // pose
        Pose pose = Pose.Standing;
        float poseAngle01;           // 0 standing .. 1 lying
        float deathFade = 1f;

        // states
        bool selected, hovered, activeTurn, stealthed, polymorphed, casting;
        Color? targetColor;
        Color tint = Color.white;
        Color castColor = Color.white;
        float castProgress;
        int lastSortOrder = int.MinValue;
        float lastSortY = float.NaN;

        // ================================================================== creation

        /// <summary>
        /// Creates a unit visual. height ≤ 0 uses the art manifest height (ArtLibrary.Height); pass
        /// CreatureDef.size for creatures.
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
            phase = UnityEngine.Random.value * 100f;
            Floating = SpriteKey.Contains("wisp");

            var sprite = ArtLibrary.Sprite(SpriteKey);
            baseSprite = sprite;
            baseSpriteHeight = Mathf.Max(0.01f, sprite.bounds.size.y);
            Height = height > 0f ? height : ArtLibrary.Height(SpriteKey, baseSpriteHeight);
            spriteScale = Height / baseSpriteHeight;

            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            group = visual.gameObject.AddComponent<SortingGroup>();
            body = PresentationArt.NewRenderer("Body", visual, sprite, 1);

            float spriteW = sprite.bounds.size.x * spriteScale;
            shadowW = Mathf.Clamp(Mathf.Max(Height * 0.36f, spriteW * 0.55f), 0.4f, 5f);
            shadowH = shadowW * 0.32f;
            var shSprite = PresentationArt.Fx("fx_shadow");
            shadow = PresentationArt.NewRenderer("Shadow", transform, shSprite, SortingOrders.Shadow + 1);
            shadowBounds = shSprite.bounds.size;
            PresentationArt.SetSize(shadow.transform, shSprite, shadowW, shadowH);
            shadow.color = new Color(0.16f, 0.12f, 0.22f, 0.34f);

            var ringSprite = PresentationArt.Fx("fx_target_ring");
            ringBounds = ringSprite.bounds.size;
            ring = PresentationArt.NewRenderer("Ring", transform, ringSprite, SortingOrders.Shadow + 20, true);
            PresentationArt.SetSize(ring.transform, ringSprite, shadowW * 1.45f, shadowW * 1.45f * 0.42f);
            ring.enabled = false;
            ripple = PresentationArt.NewRenderer("Ripple", transform, ringSprite, SortingOrders.Shadow + 19, true);
            ripple.enabled = false;

            ApplySprite(sprite);
            all.Add(this);
            UnitViewSystem.Ensure();
            Teleport(Vector2.zero);
        }

        void ApplySprite(Sprite s)
        {
            body.sprite = s;
            float sh = Mathf.Max(0.01f, s.bounds.size.y);
            float scale = polymorphed ? Mathf.Min(Height, 1.0f) / sh : Height / sh;
            spriteScale = scale;
            body.transform.localScale = new Vector3(scale, scale, 1f);
            silhouette = null;
            silhouetteTried = false;
            if (outline != null) { Destroy(outline.gameObject); outline = null; }
            if (flash != null) { Destroy(flash.gameObject); flash = null; }
            UpdateOverlays();
        }

        Silhouette Sil()
        {
            if (!silhouetteTried)
            {
                silhouetteTried = true;
                silhouette = Silhouettes.Get(body.sprite, 0.045f / Mathf.Max(0.05f, spriteScale));
            }
            return silhouette;
        }

        SpriteRenderer EnsureOutline()
        {
            if (outline != null) return outline;
            var s = Sil();
            if (s == null) return null;
            outline = PresentationArt.NewRenderer("Outline", body.transform, s.Outline, 0, true);
            outline.flipX = body.flipX;
            outline.enabled = false;
            return outline;
        }

        SpriteRenderer EnsureFlash()
        {
            if (flash != null) return flash;
            var s = Sil();
            if (s == null) return null;
            flash = PresentationArt.NewRenderer("Flash", body.transform, s.Fill, 2, true);
            flash.flipX = body.flipX;
            flash.enabled = false;
            return flash;
        }

        SpriteRenderer EnsureGlow()
        {
            if (glow != null) return glow;
            glow = PresentationArt.NewRenderer("Cast Glow", visual, PresentationArt.Glow, 3, true);
            glow.enabled = false;
            return glow;
        }

        SpriteRenderer EnsureCastCircle()
        {
            if (castCircle != null) return castCircle;
            var s = PresentationArt.Fx("fx_rune_circle");
            castCircle = PresentationArt.NewRenderer("Cast Circle", transform, s, SortingOrders.Shadow + 25, true);
            circleBounds = s.bounds.size;
            PresentationArt.SetSize(castCircle.transform, s, shadowW * 1.9f, shadowW * 1.9f * 0.45f);
            castCircle.enabled = false;
            return castCircle;
        }

        // ================================================================== movement

        /// <summary>Instantly places the unit (feet) at p.</summary>
        public void Teleport(Vector2 p)
        {
            StopMoving();
            pos = p;
            transform.position = new Vector3(p.x, p.y, 0f);
            Resort(true);
        }

        /// <summary>
        /// Walks along path (world points; a first point equal to the current position is skipped)
        /// at speed m/s, then calls onArrive. A new MoveAlong/Teleport/StopMoving replaces the move
        /// without calling the previous callback.
        /// </summary>
        public void MoveAlong(List<Vector2> points, float speed, System.Action arrive = null)
        {
            path.Clear();
            if (points != null)
                for (int i = 0; i < points.Count; i++)
                    if (path.Count > 0 || (points[i] - pos).sqrMagnitude > 1e-4f) path.Add(points[i]);
            BeginMove(speed, arrive);
        }

        /// <summary>MoveAlong for rules-engine paths (NavPath.Points).</summary>
        public void MoveAlong(IReadOnlyList<Vec2> points, float speed, System.Action arrive = null)
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

        void BeginMove(float speed, System.Action arrive)
        {
            moveSpeed = speed > 0f ? speed : 3.2f;
            onArrive = arrive;
            pathIndex = 0;
            moving = path.Count > 0;
            if (!moving && arrive != null) UnitViewSystem.Defer(arrive);
            if (moving && action == Anim.Knockback) action = Anim.None;
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
            return Begin(Anim.Knockback, Mathf.Max(0.1f, duration), Vector2.zero, Color.white);
        }

        public void FaceTowards(Vector2 target)
        {
            float dx = target.x - pos.x;
            if (Mathf.Abs(dx) > 0.02f) SetFacing(dx > 0f ? 1 : -1);
        }

        /// <summary>+1 = facing right (art default), −1 = left.</summary>
        public void SetFacing(int dir)
        {
            facing = dir >= 0 ? 1 : -1;
            bool flip = facing < 0;
            body.flipX = flip;
            if (outline != null) outline.flipX = flip;
            if (flash != null) flash.flipX = flip;
        }

        // ================================================================== one-shot animations

        float Begin(Anim a, float dur, Vector2 dir, Color c)
        {
            if (pose == Pose.Dead && a != Anim.Revive) return 0f;
            action = a;
            actionT = 0f;
            actionDur = dur;
            actionDir = dir.sqrMagnitude > 1e-6f ? dir.normalized : new Vector2(facing, 0f);
            actionColor = c;
            return dur;
        }

        /// <summary>Melee lunge towards a point. Returns the duration; the blow lands at AttackHitTime.</summary>
        public float PlayAttack(Vector2 towards)
        {
            FaceTowards(towards);
            return Begin(Anim.Attack, 0.5f, towards - pos, Color.white);
        }

        /// <summary>Bow/wand/throw recoil (no direction change). Release at ShootReleaseTime.</summary>
        public float PlayShoot() => Begin(Anim.Shoot, 0.45f, new Vector2(facing, 0f), Color.white);

        /// <summary>Bow/wand/throw recoil towards a point. Release at ShootReleaseTime.</summary>
        public float PlayShoot(Vector2 towards)
        {
            FaceTowards(towards);
            return Begin(Anim.Shoot, 0.45f, towards - pos, Color.white);
        }

        /// <summary>Raise + glow in the school colour. Release at CastReleaseTime.</summary>
        public float PlayCast(Color schoolColor)
        {
            EnsureGlow();
            EnsureCastCircle();
            return Begin(Anim.Cast, 0.7f, Vector2.zero, schoolColor);
        }

        /// <summary>White flash + shake (overlaps other animations).</summary>
        public float PlayHit()
        {
            if (pose == Pose.Dead) return 0f;
            hitT = 0f;
            EnsureFlash();
            return 0.3f;
        }

        public float PlayDodge() => Begin(Anim.Dodge, 0.4f, new Vector2(-facing, 0f), Color.white);

        /// <summary>Falls over and fades out (unit stays registered but invisible; Destroy it when done).</summary>
        public float PlayDeath()
        {
            StopMoving();
            hitT = 0f;
            EnsureFlash();
            float d = Begin(Anim.Death, 1.2f, Vector2.zero, Color.white);
            pose = Pose.Dead;
            UpdateOverlays();
            return d;
        }

        /// <summary>Lies down (party member at 0 HP). Stays down until PlayRevive.</summary>
        public float PlayDowned()
        {
            StopMoving();
            float d = Begin(Anim.Fall, 0.6f, Vector2.zero, Color.white);
            pose = Pose.Lying;
            return d;
        }

        /// <summary>Stands back up with a warm glow (help up / resurrection).</summary>
        public float PlayRevive()
        {
            pose = Pose.Standing;
            deathFade = 1f;
            EnsureGlow();
            float d = Begin(Anim.Revive, 0.8f, Vector2.zero, new Color(1f, 0.88f, 0.55f));
            UpdateOverlays();
            return d;
        }

        // ================================================================== state visuals

        public void SetSelected(bool on) { selected = on; UpdateOverlays(); }
        public void SetHovered(bool on) { hovered = on; UpdateOverlays(); }
        /// <summary>Highlights the unit as a valid target in a colour; null clears.</summary>
        public void SetTargetable(Color? color) { targetColor = color; UpdateOverlays(); }
        public void SetActiveTurn(bool on) { activeTurn = on; UpdateOverlays(); }
        /// <summary>Stealth: 40% alpha.</summary>
        public void SetStealthed(bool on) { stealthed = on; }
        /// <summary>Body tint (frozen blue, poisoned green…); Color.white clears.</summary>
        public void SetTint(Color c) { tint = c; }
        public void SetVisible(bool on)
        {
            visible = on;
            visual.gameObject.SetActive(on);
            shadow.enabled = on;
            UpdateOverlays();
        }

        /// <summary>Swaps the body to a fluffy sheep (polymorph) and back.</summary>
        public void SetPolymorphed(bool on)
        {
            if (polymorphed == on) return;
            polymorphed = on;
            ApplySprite(on ? PresentationArt.Sheep : baseSprite);
            SetFacing(facing);
            if (on) FxSystem.Sparkles(CenterPosition, Ui.SchoolColor(Lanternvale.Data.School.Arcane), 10);
        }

        /// <summary>Swaps the base art (shapeshift, disguise).</summary>
        public void SetSprite(string spriteKey)
        {
            SpriteKey = spriteKey ?? "";
            baseSprite = ArtLibrary.Sprite(SpriteKey);
            if (!polymorphed) ApplySprite(baseSprite);
            SetFacing(facing);
        }

        /// <summary>Casting glow growing with progress 0..1 (pending/channelled casts).</summary>
        public void SetCasting(Color color, float progress)
        {
            casting = true;
            castColor = color;
            castProgress = Mathf.Clamp01(progress);
            EnsureGlow();
            EnsureCastCircle();
        }

        public void StopCasting() { casting = false; }

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
            bool wantOutline = visible && hovered && pose != Pose.Dead;
            if (wantOutline) EnsureOutline();
            if (outline != null) outline.enabled = wantOutline;
        }

        float CurrentHeight => polymorphed ? Mathf.Min(Height, 1.0f) : Height;

        // ================================================================== per-frame

        internal void Tick(float dt)
        {
            time += dt;
            // pre-generate the hover/hit silhouettes in the background, one unit per frame,
            // so the first hit or hover doesn't stall on a texture readback
            if (!silhouetteTried && lastWarmFrame != Time.frameCount)
            {
                lastWarmFrame = Time.frameCount;
                Sil();
            }

            // ---- movement
            if (moving)
            {
                float step = moveSpeed * dt;
                while (step > 0f && pathIndex < path.Count)
                {
                    var target = path[pathIndex];
                    var d = target - pos;
                    float len = d.magnitude;
                    if (Mathf.Abs(d.x) > 0.02f) SetFacing(d.x > 0f ? 1 : -1);
                    if (len <= step) { pos = target; step -= len; pathIndex++; }
                    else { pos += d / len * step; step = 0f; }
                }
                if (pathIndex >= path.Count)
                {
                    moving = false;
                    path.Clear();
                    var cb = onArrive;
                    onArrive = null;
                    if (cb != null) UnitViewSystem.Defer(cb);
                }
                walkPhase += dt * moveSpeed / 0.62f * Mathf.PI;
            }
            walkBlend = Mathf.MoveTowards(walkBlend, moving ? 1f : 0f, dt * 6f);

            // ---- one-shot action
            Vector2 off = Vector2.zero;
            float angle = 0f, sx = 1f, sy = 1f;
            float glowA = 0f, glowSize = 0.6f, circleA = 0f;
            Color glowC = castColor;
            if (action != Anim.None)
            {
                actionT += dt;
                float t = actionT;
                switch (action)
                {
                    case Anim.Attack:
                    {
                        float k;
                        if (t < 0.1f) k = -0.14f * Ease(t / 0.1f);
                        else if (t < 0.22f) k = Mathf.Lerp(-0.14f, 0.55f, EaseOut((t - 0.1f) / 0.12f));
                        else if (t < 0.3f) k = 0.55f;
                        else k = Mathf.Lerp(0.55f, 0f, Ease((t - 0.3f) / 0.2f));
                        off += actionDir * k;
                        angle = -actionDir.x * k * 14f;
                        if (t < 0.1f) { sy = 1f - 0.05f * Ease(t / 0.1f); sx = 1f + 0.03f * Ease(t / 0.1f); }
                        break;
                    }
                    case Anim.Shoot:
                    {
                        float k = t < 0.18f ? -0.1f * Ease(t / 0.18f) : t < 0.24f ? Mathf.Lerp(-0.1f, -0.2f, (t - 0.18f) / 0.06f) : Mathf.Lerp(-0.2f, 0f, Ease((t - 0.24f) / 0.21f));
                        off += actionDir * k;
                        angle = actionDir.x * k * 20f;
                        break;
                    }
                    case Anim.Cast:
                    {
                        float rise = t < 0.45f ? Ease(t / 0.45f) : 1f - Ease((t - 0.45f) / 0.25f);
                        off.y += 0.12f * rise;
                        sy = 1f + 0.05f * rise; sx = 1f - 0.025f * rise;
                        glowC = actionColor;
                        glowA = t < 0.45f ? 0.25f + 0.55f * Ease(t / 0.45f) : 0.8f * (1f - Ease((t - 0.45f) / 0.25f));
                        glowSize = t < 0.45f ? 0.5f + 0.7f * Ease(t / 0.45f) : 1.2f + 0.8f * Ease((t - 0.45f) / 0.25f);
                        circleA = t < 0.45f ? 0.7f * Ease(t / 0.45f) : 0.7f * (1f - Ease((t - 0.45f) / 0.25f));
                        break;
                    }
                    case Anim.Dodge:
                    {
                        float k = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / actionDur));
                        off += actionDir * (0.42f * k);
                        off.y += 0.14f * k;
                        angle = -actionDir.x * 10f * k;
                        break;
                    }
                    case Anim.Knockback:
                    {
                        float u = Mathf.Clamp01(t / actionDur);
                        pos = Vector2.Lerp(kbFrom, kbTo, EaseOut(u));
                        off.y += Mathf.Sin(Mathf.PI * u) * 0.22f;
                        angle = (kbTo.x > kbFrom.x ? -1f : 1f) * 12f * Mathf.Sin(Mathf.PI * u);
                        break;
                    }
                    case Anim.Revive:
                    {
                        glowC = actionColor;
                        float u = Mathf.Clamp01(t / actionDur);
                        glowA = 0.75f * Mathf.Sin(Mathf.PI * u);
                        glowSize = 1.2f + 0.8f * u;
                        off.y += 0.1f * Mathf.Sin(Mathf.PI * u);
                        break;
                    }
                }
                if (actionT >= actionDur)
                {
                    if (action == Anim.Knockback) pos = kbTo;
                    action = Anim.None;
                }
            }

            // ---- pose (lying / dead)
            float targetLie = pose == Pose.Standing ? 0f : 1f;
            float lieSpeed = pose == Pose.Standing ? 2.2f : (pose == Pose.Dead ? 2.4f : 2.0f);
            poseAngle01 = Mathf.MoveTowards(poseAngle01, targetLie, dt * lieSpeed);
            if (pose == Pose.Dead)
            {
                if (actionT > 0.55f || action == Anim.None) deathFade = Mathf.MoveTowards(deathFade, 0f, dt / 0.6f);
            }
            float lieEase = pose == Pose.Standing ? EaseOut(poseAngle01) : EaseIn(poseAngle01);
            angle += lieEase * 84f * facing;

            // ---- idle breathing / walking bob
            float breathe = Mathf.Sin(time * 2.1f + phase);
            float idle = (1f - walkBlend) * (1f - poseAngle01);
            sy *= 1f + 0.014f * breathe * idle;
            sx *= 1f - 0.007f * breathe * idle;
            if (walkBlend > 0f)
            {
                off.y += Mathf.Abs(Mathf.Sin(walkPhase)) * 0.065f * walkBlend * Mathf.Clamp(Height / 1.8f, 0.5f, 1.5f);
                angle += Mathf.Sin(walkPhase) * 2.2f * walkBlend;
            }
            if (Floating)
            {
                off.y += 0.35f + Mathf.Sin(time * 1.7f + phase) * 0.08f;
            }

            // ---- hit flash + shake
            float flashA = 0f;
            if (hitT >= 0f)
            {
                hitT += dt;
                float u = hitT / 0.3f;
                if (u >= 1f) hitT = -1f;
                else
                {
                    off.x += Mathf.Sin(hitT * 55f) * 0.07f * (1f - u);
                    sy *= 1f - 0.04f * (1f - u);
                    flashA = Mathf.Clamp01(1f - hitT / 0.16f) * 0.85f;
                }
            }

            // ---- casting state (pending cast)
            if (casting)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 6f);
                float ga = 0.3f + 0.45f * castProgress + 0.1f * pulse;
                if (ga > glowA) { glowA = ga; glowC = castColor; glowSize = 0.6f + 0.8f * castProgress + 0.1f * pulse; }
                circleA = Mathf.Max(circleA, 0.35f + 0.4f * castProgress);
                if (action != Anim.Cast) glowC = castColor;
            }

            offset = off;
            transform.position = new Vector3(pos.x, pos.y, 0f);
            visual.localPosition = new Vector3(off.x, off.y, 0f);
            visual.localRotation = Quaternion.Euler(0f, 0f, angle);
            visual.localScale = new Vector3(sx, sy, 1f);

            // ---- colours
            float alpha = (stealthed ? 0.4f : 1f) * deathFade;
            var c = tint;
            if (pose == Pose.Lying) c = new Color(c.r * 0.78f, c.g * 0.78f, c.b * 0.84f, c.a);
            if (flash == null && flashA > 0f) c = Color.Lerp(c, new Color(1f, 0.55f, 0.5f), flashA * 0.7f); // fallback hit tint
            if (hovered && flash == null) c = Color.Lerp(c, Color.white, 0.12f);
            c.a *= alpha;
            body.color = c;

            if (flash != null)
            {
                float hoverA = hovered && pose != Pose.Dead ? 0.1f : 0f;
                float fa = Mathf.Max(flashA, hoverA) * alpha;
                bool on = fa > 0.003f && visible;
                if (flash.enabled != on) flash.enabled = on;
                if (on) flash.color = new Color(1f, 1f, 0.96f, fa);
            }

            if (outline != null && outline.enabled)
            {
                var oc = targetColor ?? new Color(1f, 0.92f, 0.68f);
                float pulse = 0.82f + 0.18f * Mathf.Sin(time * 5f);
                outline.color = new Color(oc.r, oc.g, oc.b, 0.95f * pulse * alpha);
            }

            if (glow != null)
            {
                bool on = glowA > 0.01f && visible;
                if (glow.enabled != on) glow.enabled = on;
                if (on)
                {
                    glow.transform.localPosition = new Vector3(0.12f * facing * Mathf.Clamp(Height / 1.8f, 0.5f, 2f), CurrentHeight * 0.55f, 0f);
                    float gs = glowSize * Mathf.Clamp(Height / 1.8f, 0.6f, 2f);
                    glow.transform.localScale = new Vector3(gs, gs, 1f);
                    glow.color = new Color(glowC.r, glowC.g, glowC.b, glowA * alpha);
                }
            }

            if (castCircle != null)
            {
                bool on = circleA > 0.01f && visible;
                if (castCircle.enabled != on) castCircle.enabled = on;
                if (on)
                {
                    castCircle.transform.localRotation = Quaternion.identity;
                    var cc = action == Anim.Cast ? actionColor : castColor;
                    castCircle.color = new Color(cc.r, cc.g, cc.b, circleA * alpha);
                    // fake rotation of a flat ellipse: pulse its scale instead (rotating a squashed sprite would skew it)
                    float k = 1f + 0.04f * Mathf.Sin(time * 3f);
                    SetScale(castCircle.transform, circleBounds, shadowW * 1.9f * k, shadowW * 1.9f * 0.45f * k);
                }
            }

            // ---- shadow + rings
            float shadowK = (1f - 0.35f * Mathf.Clamp01(off.y / 0.6f)) * deathFade;
            shadow.color = new Color(0.16f, 0.12f, 0.22f, 0.34f * shadowK * (stealthed ? 0.5f : 1f));
            float lieStretch = 1f + poseAngle01 * 0.9f;
            SetScale(shadow.transform, shadowBounds, shadowW * lieStretch, shadowH);
            shadow.transform.localPosition = new Vector3(poseAngle01 * -facing * Height * 0.42f, 0f, 0f);

            if (ring.enabled)
            {
                int st = CurrentRingState();
                Color rc;
                float a;
                switch (st)
                {
                    case 4: rc = targetColor.Value; a = 0.6f + 0.35f * (0.5f + 0.5f * Mathf.Sin(time * 6f)); break;
                    case 3: rc = RingColor; a = 0.95f; break;
                    case 2: rc = RingColor; a = 0.85f; break;
                    default: rc = new Color(1f, 0.96f, 0.86f); a = 0.5f; break;
                }
                ring.color = new Color(rc.r, rc.g, rc.b, a * (stealthed ? 0.6f : 1f));
                if (ripple.enabled)
                {
                    float u = Mathf.Repeat(time / 1.3f, 1f);
                    float k = 1f + 0.55f * u;
                    SetScale(ripple.transform, ringBounds, shadowW * 1.45f * k, shadowW * 1.45f * 0.42f * k);
                    ripple.color = new Color(RingColor.r, RingColor.g, RingColor.b, 0.7f * (1f - u));
                }
            }

            Resort(false);
        }

        void Resort(bool force)
        {
            if (!force && Mathf.Abs(pos.y - lastSortY) < 0.004f) return;
            lastSortY = pos.y;
            int order = SortingOrders.ForY(pos.y);
            if (order != lastSortOrder)
            {
                lastSortOrder = order;
                group.sortingOrder = order;
            }
        }

        static void SetScale(Transform t, Vector2 bounds, float w, float h)
        {
            t.localScale = new Vector3(w / Mathf.Max(1e-4f, bounds.x), h / Mathf.Max(1e-4f, bounds.y), 1f);
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
        static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t; }

        // ================================================================== lifetime

        /// <summary>Unregisters and destroys the unit.</summary>
        public void Dispose()
        {
            all.Remove(this);
            if (this != null) Destroy(gameObject);
        }

        void OnDestroy() { all.Remove(this); }

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
        static readonly List<System.Action> deferred = new List<System.Action>();
        static readonly List<System.Action> running = new List<System.Action>();

        public static void Ensure()
        {
            if (instance == null) instance = PresentationHost.Ensure<UnitViewSystem>();
        }

        internal static void Defer(System.Action a) { if (a != null) deferred.Add(a); }

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
