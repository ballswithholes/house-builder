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

        /// <summary>Current camera view rectangle in world units (falls back to a 20x12 box around the origin).</summary>
        public static Rect ViewRect(float margin = 0f)
        {
            var cam = Cam;
            if (cam == null) return new Rect(-10 - margin, -6 - margin, 20 + margin * 2, 12 + margin * 2);
            float h = cam.orthographicSize, w = h * cam.aspect;
            var p = cam.transform.position;
            return new Rect(p.x - w - margin, p.y - h - margin, (w + margin) * 2f, (h + margin) * 2f);
        }
    }
}
