// Orthographic diorama camera: follows a target with smoothing, zooms, pans, shakes and stays
// inside the map bounds. World is the XY plane; the camera looks down +Z.
using Lanternvale.Util;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }

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
            go.transform.position = new Vector3(0, 0, -20f);
            rig.Cam = cam;
            return rig;
        }

        void Awake()
        {
            Instance = this;
            targetSize = DefaultSize;
            basePos = transform.position;
        }

        public float Zoom { get => targetSize; set => targetSize = Mathf.Clamp(value, MinSize, MaxSize); }

        /// <summary>Temporarily frame a point (combat: active unit / target). Pass null to resume following.</summary>
        public void Focus(Vector2? point) { focusPoint = point; if (point.HasValue) panOffset = Vector2.zero; }

        public void ResetPan() { panOffset = Vector2.zero; }

        public void SnapToTarget()
        {
            var t = DesiredCenter();
            basePos = new Vector3(t.x, t.y, basePos.z);
            if (Cam != null) Cam.orthographicSize = targetSize;
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
            var p = Cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -transform.position.z));
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

        Vector2 DesiredCenter()
        {
            Vector2 c;
            if (focusPoint.HasValue) c = focusPoint.Value;
            else if (Follow != null) c = Follow.position;
            else c = new Vector2(basePos.x, basePos.y - LookAhead);
            return c + new Vector2(0, LookAhead) + panOffset;
        }

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
            lastMouse = GameInput.MousePosition;

            var target = DesiredCenter();
            float k = 1f - Mathf.Exp(-FollowSharpness * dt);
            basePos = new Vector3(Mathf.Lerp(basePos.x, target.x, k), Mathf.Lerp(basePos.y, target.y, k), basePos.z);
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

        void ClampBase()
        {
            if (Cam == null) return;
            float halfH = Cam.orthographicSize;
            float halfW = halfH * Cam.aspect;
            float minX = Bounds.xMin + halfW, maxX = Bounds.xMax - halfW;
            float minY = Bounds.yMin + halfH, maxY = Bounds.yMax - halfH;
            float x = minX > maxX ? Bounds.center.x : Mathf.Clamp(basePos.x, minX, maxX);
            float y = minY > maxY ? Bounds.center.y : Mathf.Clamp(basePos.y, minY, maxY);
            basePos = new Vector3(x, y, basePos.z);
        }

        public static Vector2 ToUnity(Vec2 v) => new Vector2(v.x, v.y);
        public static Vec2 ToVec2(Vector2 v) => new Vec2(v.x, v.y);
    }
}
