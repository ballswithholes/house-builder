// BG3-style perspective camera rig.
//
// The camera orbits a LOOK-AT POINT ON THE GROUND (z = 0; up is −Z, see World3D.cs): it follows a target (or frames a
// focus point) with smoothing and a little look-ahead, zooms with the wheel, turns ±MaxYaw degrees around the default
// view (looking into the scene, +Y) and tilts down more as it moves away (PitchNear at DistanceNear … PitchFar at
// DistanceFar). The look-at point stays inside Bounds (a ground rect set by MapView).
//
// Zoom keeps the units of the old orthographic rig: the half height of the view, in metres, at the look-at point
// (the camera distance is Zoom / tan(fov / 2)), so the game's zoom values (default 6.2, dialogue 5.3, menu 6.6) frame
// about as much as they used to.
//
// Controls (when AllowManualPan and no modal screen is up):
//   mouse wheel                zoom (towards the look-at point)
//   middle mouse drag          rotate (left / right); with Shift or Ctrl held: drag the ground (pan)
//   middle mouse click         recentre: default rotation, manual pan cleared
//   Q / E                      rotate left / right
//   W A S D / arrow keys       pan, relative to the view
using Lanternvale.Util;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }

        public Camera Cam { get; private set; }
        public Transform Follow;

        /// <summary>Zoom range and default as half view heights (metres at the look-at point). 2.6 ≈ 7.5 m away, 10.4 ≈ 30 m.</summary>
        public float MinSize = 2.6f, MaxSize = 10.4f, DefaultSize = 6.2f;
        public float FollowSharpness = 6f;
        /// <summary>Ground rect the look-at point may roam (set by MapView).</summary>
        public Rect Bounds = new Rect(-5, -4, 100, 30);
        /// <summary>Framing: the view centres this many metres above the followed / focused ground point.</summary>
        public float LookAhead = 0.9f;
        /// <summary>Seconds of the followed target's velocity the view leads by (moving units stay ahead of centre).</summary>
        public float VelocityLead = 0.3f;
        /// <summary>Manual camera control (pan, rotate). Off on the title backdrop.</summary>
        public bool AllowManualPan = true;

        /// <summary>Vertical field of view (degrees).</summary>
        public float FieldOfView = 38f;
        /// <summary>
        /// Pitch (degrees below the horizon) at DistanceNear and DistanceFar, eased in between (PitchCurve &lt; 1 tilts up
        /// quickly as you zoom out): close-ups look out over the land to the hills and sky, BG3-style; the default zoom
        /// (~18 m) is ≈ 44°; zoomed out it is a near top-down tactical view.
        /// </summary>
        public float PitchNear = 24f, PitchFar = 58f, PitchCurve = 0.7f;
        public float DistanceNear = 7f, DistanceFar = 30f;
        /// <summary>Rotation limit either side of the default view (degrees).</summary>
        public float MaxYaw = 45f;
        /// <summary>Q / E rotation speed (degrees per second).</summary>
        public float RotateSpeed = 85f;

        float targetSize, size;
        float targetYaw, yaw;
        Vector2 lookAt;          // smoothed ground look-at point (no shake)
        Vector2 freeAnchor;      // what the view holds on when there is nothing to follow
        Vector2 panOffset;
        Vector2? focusPoint;
        float shakeTime, shakeAmp;
        Transform lastFollow;
        Vector2 lastFollowPos, followVel;
        bool dragging, dragMoved, dragPan;
        Vector2 dragStart, lastMouse;
        float dragTime;

        public static CameraRig Create()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Lanternvale Camera");
            var rig = go.AddComponent<CameraRig>();
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }
            cam.transform.SetParent(go.transform, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.transform.localScale = Vector3.one;
            cam.orthographic = false;
            cam.fieldOfView = rig.FieldOfView;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.88f, 0.95f);
            cam.allowMSAA = true;
            cam.transparencySortMode = TransparencySortMode.Perspective;
            if (QualitySettings.antiAliasing < 4) QualitySettings.antiAliasing = 4;
            rig.Cam = cam;
            rig.Apply(false);
            return rig;
        }

        void Awake()
        {
            Instance = this;
            targetSize = size = DefaultSize;
            lookAt = freeAnchor = new Vector2(transform.position.x, transform.position.y);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ================================================================== public API

        /// <summary>Zoom as the half view height at the look-at point (metres), clamped to MinSize..MaxSize.</summary>
        public float Zoom { get => targetSize; set => targetSize = Mathf.Clamp(value, MinSize, MaxSize); }

        /// <summary>Current (smoothed) camera distance from the look-at point, metres.</summary>
        public float Distance => size / TanHalfFov;

        /// <summary>Current pitch (degrees below the horizon).</summary>
        public float Pitch => PitchFor(Distance);

        /// <summary>Current rotation around the look-at point (degrees, 0 = looking into the scene, + = turned right).</summary>
        public float Yaw => yaw;

        /// <summary>Rotation the view turns to (clamped to ±MaxYaw).</summary>
        public float TargetYaw { get => targetYaw; set => targetYaw = Mathf.Clamp(value, -MaxYaw, MaxYaw); }

        /// <summary>Turns back to the default view (looking into +Y).</summary>
        public void ResetRotation() => targetYaw = 0f;

        /// <summary>Temporarily frame a ground point (combat: active unit / target). Pass null to resume following.</summary>
        public void Focus(Vector2? point)
        {
            focusPoint = point;
            if (point.HasValue) panOffset = Vector2.zero;
        }

        public void ResetPan() { panOffset = Vector2.zero; }

        /// <summary>Jumps to the target framing at once (map entry, menu): zoom, rotation and position.</summary>
        public void SnapToTarget()
        {
            size = targetSize;
            yaw = targetYaw;
            if (Follow != null) { lastFollow = Follow; lastFollowPos = Follow.position; }
            followVel = Vector2.zero;
            lookAt = DesiredLookAt();
            Apply(false);
        }

        public void Shake(float amplitude, float duration)
        {
            shakeAmp = Mathf.Max(shakeAmp, amplitude);
            shakeTime = Mathf.Max(shakeTime, duration);
        }

        /// <summary>Ground point (z = 0) under a screen position (pixels, bottom-left origin): the view ray ∩ the ground.</summary>
        public Vector2 ScreenToWorld(Vector2 screen)
        {
            if (Cam == null) return Vector2.zero;
            var ray = Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            var o = ray.origin;
            var d = ray.direction;
            if (d.z > 1e-4f)
            {
                float t = -o.z / d.z;
                if (t >= 0f) return new Vector2(o.x + d.x * t, o.y + d.y * t);
            }
            // above the horizon: the ground point under a far point along the ray
            float far = Cam.farClipPlane * 0.5f;
            return new Vector2(o.x + d.x * far, o.y + d.y * far);
        }

        /// <summary>Ground point under the mouse.</summary>
        public Vector2 MouseWorld => ScreenToWorld(GameInput.MousePosition);

        /// <summary>
        /// World point (a Vector2 is a ground point; z = −height) → screen pixels (bottom-left origin). A point behind the
        /// camera is pushed far off screen on the side it lies (check IsInFront to skip it instead).
        /// </summary>
        public Vector2 WorldToScreen(Vector3 world)
        {
            if (Cam == null) return Vector2.zero;
            var p = Cam.WorldToScreenPoint(world);
            if (p.z > 0.01f) return new Vector2(p.x, p.y);
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            var d = new Vector2(cx - p.x, cy - p.y);   // behind the camera the projection is mirrored through the centre
            if (d.sqrMagnitude < 1f) d = new Vector2(0f, -1f);
            return new Vector2(cx, cy) + d.normalized * ((Screen.width + Screen.height) * 4f);
        }

        /// <summary>World point → IMGUI coordinates (top-left origin, unscaled pixels).</summary>
        public Vector2 WorldToGui(Vector3 world)
        {
            var s = WorldToScreen(world);
            return new Vector2(s.x, Screen.height - s.y);
        }

        /// <summary>True when the world point is in front of the camera (projected points behind it are meaningless).</summary>
        public bool IsInFront(Vector3 world) => Cam != null && Cam.WorldToScreenPoint(world).z > 0.01f;

        /// <summary>Camera ray through a screen position (pixels, bottom-left origin).</summary>
        public Ray ScreenRay(Vector2 screen) => Cam != null ? Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f)) : new Ray(Vector3.zero, Vector3.forward);

        /// <summary>The ground point the camera looks at (centre of the view, without shake).</summary>
        public Vector3 LookAtPoint => Cam != null ? new Vector3(lookAt.x, lookAt.y, 0f) : Vector3.zero;

        public static Vector2 ToUnity(Vec2 v) => new Vector2(v.x, v.y);
        public static Vec2 ToVec2(Vector2 v) => new Vec2(v.x, v.y);

        // ================================================================== maths

        float TanHalfFov => Mathf.Tan(Mathf.Clamp(FieldOfView, 10f, 120f) * 0.5f * Mathf.Deg2Rad);

        float PitchFor(float distance) =>
            Mathf.Lerp(PitchNear, PitchFar, Mathf.Pow(Mathf.InverseLerp(DistanceNear, DistanceFar, distance), Mathf.Max(0.1f, PitchCurve)));

        /// <summary>Ground direction "into the view" for a yaw (0 → +Y).</summary>
        static Vector2 GroundForward(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        /// <summary>Ground direction to the right of the view for a yaw (0 → +X).</summary>
        static Vector2 GroundRight(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
        }

        /// <summary>View direction: yaw around the vertical, pitch below the horizon (+Z is down).</summary>
        static Vector3 ViewForward(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad, p = pitchDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(p);
            return new Vector3(Mathf.Sin(y) * c, Mathf.Cos(y) * c, Mathf.Sin(p));
        }

        /// <summary>Ground offset that puts a point LookAhead metres above the anchor at the view centre.</summary>
        Vector2 Framing()
        {
            float pitch = Mathf.Max(5f, PitchFor(size / TanHalfFov));
            return GroundForward(yaw) * (LookAhead / Mathf.Tan(pitch * Mathf.Deg2Rad));
        }

        Vector2 ClampToBounds(Vector2 p)
        {
            var b = Bounds;
            float x = b.width > 0f ? Mathf.Clamp(p.x, b.xMin, b.xMax) : b.center.x;
            float y = b.height > 0f ? Mathf.Clamp(p.y, b.yMin, b.yMax) : b.center.y;
            return new Vector2(x, y);
        }

        /// <summary>The ground point the view should hold: focus, else follow target (+ velocity lead), else where it was.</summary>
        Vector2 Anchor()
        {
            if (focusPoint.HasValue) return freeAnchor = focusPoint.Value;
            if (Follow != null)
            {
                var p = (Vector2)Follow.position;
                return freeAnchor = p + Vector2.ClampMagnitude(followVel * VelocityLead, 2f);
            }
            return freeAnchor;
        }

        /// <summary>
        /// The anchor (framed, inside the bounds) plus the manual pan. The pan is clamped so the sum stays inside the
        /// bounds too: pushing against an edge builds no hidden offset that must be unwound before the opposite
        /// direction responds.
        /// </summary>
        Vector2 DesiredLookAt()
        {
            var a = ClampToBounds(Anchor() + Framing());
            var b = Bounds;
            if (b.width > 0f) panOffset.x = Mathf.Clamp(panOffset.x, b.xMin - a.x, b.xMax - a.x); else panOffset.x = 0f;
            if (b.height > 0f) panOffset.y = Mathf.Clamp(panOffset.y, b.yMin - a.y, b.yMax - a.y); else panOffset.y = 0f;
            return a + panOffset;
        }

        // ================================================================== update

        void LateUpdate()
        {
            if (Cam == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            var mouse = GameInput.MousePosition;

            // follow velocity (for the look-ahead); a jump (teleport, new map) is not a velocity
            if (Follow != lastFollow)
            {
                lastFollow = Follow;
                if (Follow != null) lastFollowPos = Follow.position;
                followVel = Vector2.zero;
            }
            else if (Follow != null && dt > 1e-4f)
            {
                var p = (Vector2)Follow.position;
                var v = (p - lastFollowPos) / dt;
                lastFollowPos = p;
                if (v.sqrMagnitude > 20f * 20f) followVel = Vector2.zero;
                else followVel = Vector2.Lerp(followVel, v, 1f - Mathf.Exp(-3f * dt));
            }

            // zoom (multiplicative: each wheel notch ≈ 12 %)
            float scroll = GameInput.Scroll;
            if (Mathf.Abs(scroll) > 0.001f) Zoom = targetSize * Mathf.Pow(0.88f, scroll);

            bool controls = AllowManualPan && !UiRoot.ModalActive;
            HandleMouse(controls, mouse, dt);
            if (controls) HandleKeys(dt);
            lastMouse = mouse;
            targetYaw = Mathf.Clamp(targetYaw, -MaxYaw, MaxYaw);

            // smoothing
            size = Mathf.Lerp(size, targetSize, 1f - Mathf.Exp(-10f * dt));
            yaw = Mathf.Lerp(yaw, targetYaw, 1f - Mathf.Exp(-12f * dt));
            var target = DesiredLookAt();
            lookAt = Vector2.Lerp(lookAt, target, 1f - Mathf.Exp(-FollowSharpness * dt));
            lookAt = ClampToBounds(lookAt);

            if (shakeTime > 0f)
            {
                shakeTime -= dt;
                if (shakeTime <= 0f) { shakeTime = 0f; shakeAmp = 0f; }
            }
            Apply(shakeTime > 0f);
        }

        void HandleMouse(bool controls, Vector2 mouse, float dt)
        {
            if (controls && GameInput.MouseDown(2) && !GameInput.PointerOverUi)
            {
                dragging = true;
                dragMoved = false;
                dragTime = 0f;
                dragStart = mouse;
                lastMouse = mouse;
                dragPan = GameInput.Key(KeyCode.LeftShift) || GameInput.Key(KeyCode.RightShift) ||
                          GameInput.Key(KeyCode.LeftControl) || GameInput.Key(KeyCode.RightControl);
            }
            if (!dragging) return;
            if (!controls || !GameInput.MouseHeld(2))
            {
                // a click without a drag recentres: default rotation, no manual pan
                if (controls && !dragMoved && dragTime < 0.35f) { targetYaw = 0f; panOffset = Vector2.zero; }
                dragging = false;
                return;
            }
            dragTime += dt;
            if ((mouse - dragStart).sqrMagnitude > 6f * 6f) dragMoved = true;
            var delta = mouse - lastMouse;
            if (delta.sqrMagnitude < 1e-6f) return;
            if (dragPan)
            {
                // drag the ground: the point under the cursor follows the cursor
                var move = ScreenToWorld(mouse) - ScreenToWorld(lastMouse);
                panOffset -= Vector2.ClampMagnitude(move, 6f);
                panOffset = Vector2.ClampMagnitude(panOffset, 30f);
            }
            else targetYaw += delta.x / Mathf.Max(1f, Screen.width) * 220f;
        }

        void HandleKeys(float dt)
        {
            // E is also "take all" in the loot window: no rotating while it is open
            var flow = GameFlow.Instance;
            bool loot = flow != null && flow.Session != null && flow.Session.PendingLoot != null;
            if (!loot)
            {
                float rot = 0f;
                if (GameInput.Key(KeyCode.Q)) rot -= 1f;
                if (GameInput.Key(KeyCode.E)) rot += 1f;
                if (rot != 0f) targetYaw = Mathf.Clamp(targetYaw + rot * RotateSpeed * dt, -MaxYaw, MaxYaw);
            }

            float fx = 0f, fy = 0f;
            if (GameInput.Key(KeyCode.LeftArrow) || GameInput.Key(KeyCode.A)) fx -= 1f;
            if (GameInput.Key(KeyCode.RightArrow) || GameInput.Key(KeyCode.D)) fx += 1f;
            if (GameInput.Key(KeyCode.UpArrow) || GameInput.Key(KeyCode.W)) fy += 1f;
            if (GameInput.Key(KeyCode.DownArrow) || GameInput.Key(KeyCode.S)) fy -= 1f;
            if (fx != 0f || fy != 0f)
            {
                var dir = GroundRight(yaw) * fx + GroundForward(yaw) * fy;
                panOffset += dir.normalized * (targetSize * 1.4f * dt);
                panOffset = Vector2.ClampMagnitude(panOffset, 30f);
            }
        }

        /// <summary>Places the camera: orbit of the look-at point at the current distance, pitch and yaw (+ shake).</summary>
        void Apply(bool shake)
        {
            if (Cam != null && Mathf.Abs(Cam.fieldOfView - FieldOfView) > 0.01f) Cam.fieldOfView = FieldOfView;
            float dist = size / TanHalfFov;
            var f = ViewForward(yaw, PitchFor(dist));
            var rot = Quaternion.LookRotation(f, World3D.Up);
            var pos = new Vector3(lookAt.x, lookAt.y, 0f) - f * dist;
            if (shake)
            {
                float t = Time.unscaledTime;
                float a = shakeAmp * Mathf.Clamp01(shakeTime * 4f) * 2f * (size / Mathf.Max(0.1f, DefaultSize));
                float nx = Mathf.PerlinNoise(t * 37f, 0.3f) - 0.5f, ny = Mathf.PerlinNoise(0.7f, t * 41f) - 0.5f;
                pos += (rot * Vector3.right) * (nx * a) + (rot * Vector3.up) * (ny * a);
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
