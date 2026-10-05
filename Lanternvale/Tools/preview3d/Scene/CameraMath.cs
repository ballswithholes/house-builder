// CameraRig's placement maths (Scripts/Game/Rendering/CameraRig.cs), reproduced exactly: Zoom = half view height at
// the look-at point, distance = Zoom / tan(FOV / 2), eased pitch (PitchNear → PitchFar between DistanceNear and
// DistanceFar, curve PitchCurve), yaw around the vertical, look-at = anchor + framing (LookAhead) clamped to Bounds,
// rotation LookRotation(forward, up = −Z).
using UnityEngine;
using Lanternvale.Game;

namespace Lanternvale.Preview
{
    public static class CameraMath
    {
        public const float FieldOfView = 38f;
        public const float PitchNear = 24f, PitchFar = 58f, PitchCurve = 0.7f;
        public const float DistanceNear = 7f, DistanceFar = 30f;
        public const float LookAhead = 0.9f;
        public const float MinSize = 2.6f, MaxSize = 10.4f, DefaultSize = 6.2f;

        static float TanHalfFov => Mathf.Tan(Mathf.Clamp(FieldOfView, 10f, 120f) * 0.5f * Mathf.Deg2Rad);

        public static float PitchFor(float distance) =>
            Mathf.Lerp(PitchNear, PitchFar, Mathf.Pow(Mathf.InverseLerp(DistanceNear, DistanceFar, distance), Mathf.Max(0.1f, PitchCurve)));

        static Vector2 GroundForward(float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        public static Vector3 ViewForward(float yawDeg, float pitchDeg)
        {
            float y = yawDeg * Mathf.Deg2Rad, p = pitchDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(p);
            return new Vector3(Mathf.Sin(y) * c, Mathf.Cos(y) * c, Mathf.Sin(p));
        }

        /// <summary>
        /// The rig's pose for a ground anchor (follow target / focus), zoom (half view height, m) and yaw. exactLookAt
        /// puts the look-at point on the anchor itself instead of the follow framing.
        /// </summary>
        public static void Place(Vector2 anchor, bool exactLookAt, float zoom, float yaw, Rect bounds,
                                 out Vector3 pos, out Quaternion rot, out Vector3 lookAt, out float pitch, out float dist)
        {
            dist = zoom / TanHalfFov;
            pitch = PitchFor(dist);
            Vector2 at = anchor;
            if (!exactLookAt)
            {
                // Framing(): the view centres LookAhead metres above the followed ground point
                float fp = Mathf.Max(5f, pitch);
                at = anchor + GroundForward(yaw) * (LookAhead / Mathf.Tan(fp * Mathf.Deg2Rad));
            }
            at = new Vector2(bounds.width > 0f ? Mathf.Clamp(at.x, bounds.xMin, bounds.xMax) : bounds.center.x,
                             bounds.height > 0f ? Mathf.Clamp(at.y, bounds.yMin, bounds.yMax) : bounds.center.y);
            var f = ViewForward(yaw, pitch);
            rot = Quaternion.LookRotation(f, World3D.Up);
            lookAt = new Vector3(at.x, at.y, 0f);
            pos = lookAt - f * dist;
        }
    }
}
