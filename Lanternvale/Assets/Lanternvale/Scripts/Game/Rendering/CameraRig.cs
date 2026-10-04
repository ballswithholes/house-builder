// Orthographic diorama camera: follows a target with smoothing, zooms, pans, shakes and stays
// inside the map bounds. World is the XY plane; the camera looks down +Z.
using Lanternvale.Util;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }

        /// <summary>
        /// Fixed rig depth. Every world sprite lives on z = 0, so the camera must always sit at a negative z
        /// (beyond the near clip plane) whatever x/y it follows; nothing may inherit z from elsewhere.
        /// </summary>
        public const float CameraZ = -20f;

        public Camera Cam { get; private set; }
        public Transform Follow;
        public float MinSize = 4.5f, MaxSize = 9f, DefaultSize = 6.2f;
        public float FollowSharpness = 6f;
        /// <summary>World bounds the view should stay inside (x: map width, y: from just below the ground to above the horizon).</summary>
        public Rect Bounds = new Rect(-5, -4, 100, 30);
        /// <summary>Vertical framing: the followed point sits this far below the view centre.</summary>
        public float LookAhead = 1.6f;
        public bool AllowManualPan = true;

        float targetSize;
        Vector2 panOffset;
        Vector2? focusPoint;
        float shakeTime, shakeAmp;
        Vector3 basePos;

        public static CameraRig Create()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Lanternvale Camera");
            // Position before AddComponent: Awake runs inside AddComponent and records basePos.
            go.transform.position = new Vector3(0, 0, CameraZ);
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
            cam.orthographic = true;
            cam.orthographicSize = rig.DefaultSize;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.88f, 0.95f);
            rig.Cam = cam;
            return rig;
        }

        void Awake()
        {
            Instance = this;
            targetSize = DefaultSize;
            basePos = new Vector3(transform.position.x, transform.position.y, CameraZ);
        }

        public float Zoom { get => targetSize; set => targetSize = Mathf.Clamp(value, MinSize, MaxSize); }

        /// <summary>Temporarily frame a point (combat: active unit / target). Pass null to resume following.</summary>
        public void Focus(Vector2? point) { focusPoint = point; if (point.HasValue) panOffset = Vector2.zero; }

        public void ResetPan() { panOffset = Vector2.zero; }

        public void SnapToTarget()
        {
            // size first: the bounds clamp below depends on it
            if (Cam != null) Cam.orthographicSize = targetSize;
            ClampPan();
            var t = DesiredCenter();
            basePos = new Vector3(t.x, t.y, CameraZ);
            ClampBase();
            transform.position = basePos;
        }

        public void Shake(float amplitude, float duration)
        {
            shakeAmp = Mathf.Max(shakeAmp, amplitude);
            shakeTime = Mathf.Max(shakeTime, duration);
        }

        /// <summary>World point (z = 0) under a screen position.</summary>
        public Vector2 ScreenToWorld(Vector2 screen)
        {
            if (Cam == null) return Vector2.zero;
            // distance from the camera to the z = 0 world plane
            var p = Cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -Cam.transform.position.z));
            return new Vector2(p.x, p.y);
        }

        public Vector2 MouseWorld => ScreenToWorld(GameInput.MousePosition);

        public Vector2 WorldToScreen(Vector2 world)
        {
            if (Cam == null) return Vector2.zero;
            var p = Cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0));
            return new Vector2(p.x, p.y);
        }

        /// <summary>World point → IMGUI coordinates (top-left origin, unscaled pixels).</summary>
        public Vector2 WorldToGui(Vector2 world)
        {
            var s = WorldToScreen(world);
            return new Vector2(s.x, Screen.height - s.y);
        }

        /// <summary>The view centre before manual pan: focus point or follow target plus the look-ahead, else the current centre.</summary>
        Vector2 AnchorCenter()
        {
            if (focusPoint.HasValue) return focusPoint.Value + new Vector2(0, LookAhead);
            if (Follow != null) return (Vector2)Follow.position + new Vector2(0, LookAhead);
            return new Vector2(basePos.x, basePos.y);
        }

        /// <summary>
        /// The anchor kept inside the bounds, plus the manual pan. ClampPan keeps the sum inside the bounds too, so
        /// panning away from an edge the anchor is pressed against moves the view at once.
        /// </summary>
        Vector2 DesiredCenter() => ClampCenter(AnchorCenter()) + panOffset;

        void LateUpdate()
        {
            if (Cam == null) return;
            float dt = Time.unscaledDeltaTime;

            // zoom
            float scroll = GameInput.Scroll;
            if (Mathf.Abs(scroll) > 0.001f) targetSize = Mathf.Clamp(targetSize - scroll * 0.6f, MinSize, MaxSize);
            Cam.orthographicSize = Mathf.Lerp(Cam.orthographicSize, targetSize, 1f - Mathf.Exp(-10f * dt));

            // manual pan (keyboard / middle mouse drag)
            if (AllowManualPan)
            {
                var pan = Vector2.zero;
                if (GameInput.Key(KeyCode.LeftArrow)) pan.x -= 1;
                if (GameInput.Key(KeyCode.RightArrow)) pan.x += 1;
                if (GameInput.Key(KeyCode.UpArrow)) pan.y += 1;
                if (GameInput.Key(KeyCode.DownArrow)) pan.y -= 1;
                panOffset += pan * (Cam.orthographicSize * 1.4f * dt);
                if (GameInput.MouseHeld(2)) panOffset -= MouseDelta() * (Cam.orthographicSize * 2f / Screen.height);
                panOffset = Vector2.ClampMagnitude(panOffset, 30f);
            }
            ClampPan();
            lastMouse = GameInput.MousePosition;

            var target = DesiredCenter();
            float k = 1f - Mathf.Exp(-FollowSharpness * dt);
            basePos = new Vector3(Mathf.Lerp(basePos.x, target.x, k), Mathf.Lerp(basePos.y, target.y, k), CameraZ);
            ClampBase();

            var pos = basePos;
            if (shakeTime > 0f)
            {
                shakeTime -= dt;
                float a = shakeAmp * Mathf.Clamp01(shakeTime * 4f);
                pos += new Vector3(Mathf.PerlinNoise(Time.time * 37f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, Time.time * 41f) - 0.5f, 0) * (a * 2f);
                if (shakeTime <= 0f) shakeAmp = 0f;
            }
            transform.position = pos;
        }

        Vector2 lastMouse;
        Vector2 MouseDelta() => GameInput.MousePosition - lastMouse;

        /// <summary>Range the view centre may take at the current zoom (the bounds' centre on an axis the view outgrows).</summary>
        void CenterRange(out Vector2 min, out Vector2 max)
        {
            float halfH = Cam.orthographicSize;
            float halfW = halfH * Cam.aspect;
            min = new Vector2(Bounds.xMin + halfW, Bounds.yMin + halfH);
            max = new Vector2(Bounds.xMax - halfW, Bounds.yMax - halfH);
            if (min.x > max.x) min.x = max.x = Bounds.center.x;
            if (min.y > max.y) min.y = max.y = Bounds.center.y;
        }

        Vector2 ClampCenter(Vector2 c)
        {
            if (Cam == null) return c;
            CenterRange(out var min, out var max);
            return new Vector2(Mathf.Clamp(c.x, min.x, max.x), Mathf.Clamp(c.y, min.y, max.y));
        }

        /// <summary>
        /// Feeds the bounds clamp back into the manual pan: the panned centre (bounded anchor + offset) may not
        /// leave the reachable range, so pushing against an edge builds no hidden offset that must be unwound
        /// before the opposite direction responds. The allowed range always contains zero, so this only ever
        /// shrinks the offset toward zero and never turns it around.
        /// </summary>
        void ClampPan()
        {
            if (Cam == null) return;
            CenterRange(out var min, out var max);
            var a = ClampCenter(AnchorCenter());
            panOffset = new Vector2(Mathf.Clamp(panOffset.x, min.x - a.x, max.x - a.x),
                                    Mathf.Clamp(panOffset.y, min.y - a.y, max.y - a.y));
        }

        void ClampBase()
        {
            if (Cam == null) return;
            var c = ClampCenter(basePos);
            basePos = new Vector3(c.x, c.y, CameraZ);
        }

        public static Vector2 ToUnity(Vec2 v) => new Vector2(v.x, v.y);
        public static Vec2 ToVec2(Vector2 v) => new Vec2(v.x, v.y);
    }
}
