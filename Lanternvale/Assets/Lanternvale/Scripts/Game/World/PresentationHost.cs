// Hidden, persistent GameObject that hosts the presentation managers (units, FX, floating text,
// sway, audio). Each manager is a singleton component added on first use, so nothing needs to be
// placed in a scene and there is exactly one Update per system (no per-object Updates).
using UnityEngine;

namespace Lanternvale.Game
{
    public static class PresentationHost
    {
        static GameObject root;

        /// <summary>The host GameObject (created on first access, survives scene loads).</summary>
        public static GameObject Root
        {
            get
            {
                if (root == null)
                {
                    root = new GameObject("Lanternvale Presentation");
                    if (Application.isPlaying) Object.DontDestroyOnLoad(root);
                }
                return root;
            }
        }

        /// <summary>Returns the host's component of type T, adding it if needed.</summary>
        public static T Ensure<T>() where T : Component
        {
            var r = Root;
            var c = r.GetComponent<T>();
            if (c == null) c = r.AddComponent<T>();
            return c;
        }

        /// <summary>The active camera rig's camera, or Camera.main as a fallback (may be null).</summary>
        public static Camera Cam
        {
            get
            {
                var rig = CameraRig.Instance;
                if (rig != null && rig.Cam != null) return rig.Cam;
                return Camera.main;
            }
        }

        static readonly Vector2[] Corners = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };

        /// <summary>
        /// Ground-plane rectangle seen by the camera: the bounding box of the view frustum ∩ the ground (z = 0), grown by
        /// margin metres on every side (falls back to a 20 × 12 box around the origin without a camera). Use it to cull
        /// things that lie on the ground. Things standing up to maxHeight metres tall can be seen from outside that
        /// footprint (their tops enter the view): pass their height to also include the frustum ∩ the plane at that
        /// height (z = −maxHeight).
        /// </summary>
        public static Rect ViewRect(float margin = 0f, float maxHeight = 0f)
        {
            var cam = Cam;
            if (cam == null) return new Rect(-10 - margin, -6 - margin, 20 + margin * 2, 12 + margin * 2);
            var cp = cam.transform.position;
            if (cam.orthographic)
            {
                float h = cam.orthographicSize, w = h * cam.aspect;
                return new Rect(cp.x - w - margin, cp.y - h - margin, (w + margin) * 2f, (h + margin) * 2f);
            }
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            var fwd = cam.transform.forward;
            float far = cam.farClipPlane;
            int planes = maxHeight > 0.01f ? 2 : 1;
            for (int pl = 0; pl < planes; pl++)
            {
                float planeZ = pl == 0 ? 0f : -maxHeight;
                // the camera below the upper plane: everything between it and the ground can be in view (the apex)
                if (pl == 1 && cp.z > planeZ) Grow(ref xMin, ref yMin, ref xMax, ref yMax, cp.x, cp.y);
                for (int i = 0; i < Corners.Length; i++)
                {
                    var ray = cam.ViewportPointToRay(new Vector3(Corners[i].x, Corners[i].y, 0f));
                    var o = ray.origin;
                    var d = ray.direction;
                    // the farthest the ray can see (the far plane), then where it meets the plane (if it does before that)
                    float tMax = far / Mathf.Max(0.05f, Vector3.Dot(d, fwd));
                    float tp = Mathf.Abs(d.z) > 1e-5f ? (planeZ - o.z) / d.z : -1f;
                    float t;
                    if (tp >= 0f) t = Mathf.Min(tp, tMax);
                    else if (pl == 0) t = tMax;   // above the horizon: the ground is seen up to the far plane
                    else continue;                // never reaches the upper plane (the apex already covers it)
                    var p = o + d * t;
                    Grow(ref xMin, ref yMin, ref xMax, ref yMax, p.x, p.y);
                }
            }
            if (xMin > xMax) { xMin = xMax = cp.x; yMin = yMax = cp.y; }
            return Rect.MinMaxRect(xMin - margin, yMin - margin, xMax + margin, yMax + margin);
        }

        static void Grow(ref float xMin, ref float yMin, ref float xMax, ref float yMax, float x, float y)
        {
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }
    }
}
