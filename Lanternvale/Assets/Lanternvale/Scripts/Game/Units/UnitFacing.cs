// Facing conventions of UnitView.SetFacing, shared with tools (the offline preview places units the same way).
//
// SetFacing(±1) means "face screen-right / screen-left", turned towards the camera (a 3/4 view, so the face reads from
// the game camera), for the CURRENT camera yaw: the camera rig turns ±45° around the default view, and a unit told to
// face +X would otherwise show the camera its back. Yaws are World3D.Yaw degrees (0 = into the scene, +Y; 90 = +X);
// CameraRig.Yaw uses the same convention (0 = looking into +Y, + = turned right), so screen-right is cameraYaw + 90 and
// the camera itself lies towards cameraYaw + 180.
//
// Static models (totems, the training dummy) never turn by themselves: they are set down with their carved front
// towards the camera (StaticYaw) and ignore later facing requests.
using UnityEngine;

namespace Lanternvale.Game
{
    public static class UnitFacing
    {
        /// <summary>Degrees SetFacing turns the face from screen-left/right towards the camera.</summary>
        public const float Bias = 35f;

        /// <summary>Degrees a static model's front is turned from screen-left/right towards the camera (90 = head-on).</summary>
        public const float StaticBias = 60f;

        /// <summary>World yaw for SetFacing(dir) under a camera turned by cameraYaw degrees (CameraRig.Yaw).</summary>
        public static float SideYaw(int dir, float cameraYaw, float bias = Bias)
        {
            float y = cameraYaw + (dir >= 0 ? 90f + bias : -(90f + bias));
            return Mathf.Repeat(y + 180f, 360f) - 180f;
        }

        /// <summary>Totems, dummies, traps: models that stand still and show their front to the camera.</summary>
        public static bool IsStatic(UnitModel m) => m != null && (m.Static || m.Rig == UnitRigKind.Static);

        /// <summary>World yaw a static model is set down with (its front towards the camera, a little to the dir side).</summary>
        public static float StaticYaw(int dir, float cameraYaw) => SideYaw(dir, cameraYaw, StaticBias);

        /// <summary>
        /// Yaw of the direction towards the camera relative to a unit's facing (degrees, + = to the unit's right,
        /// 0 = the camera is straight ahead): UnitAnimInput.ViewYaw, which turns idle heads to the player.
        /// </summary>
        public static float ViewYaw(float unitYaw, float cameraYaw) => Mathf.DeltaAngle(unitYaw, cameraYaw + 180f);
    }

    /// <summary>The soft blob shadow under a unit (UnitView.UpdateGround; the offline preview's UnitPoser uses it too).</summary>
    public static class UnitShadow
    {
        /// <summary>
        /// Centre on the ground (unit-local, following the body offset `bo`), half-length along the facing, half-width,
        /// darkness and the Shadow shader's _Softness. A walker's own footprint radius (~0.33 m) hides under its boots
        /// from the 44° game camera and the unit looks pasted on, so bipeds standing on the ground get a broader, darker,
        /// firmer blob, nudged a little away from the sun so it peeks out from under the feet.
        /// </summary>
        public static void Blob(UnitModel m, float sc, float height, Vector3 bo, float hover, float lie,
                                out Vector2 center, out float len, out float wid, out float alpha, out float softness)
        {
            float r = m.Radius * sc;
            len = (m.HalfLength * sc + r) * (1f + lie * 0.5f);
            wid = r * (1f + (m.Rig == UnitRigKind.Quad ? lie * 0.4f : 0f));
            if (m.Rig == UnitRigKind.Biped && lie > 0f) len = r + lie * height * 0.42f;
            alpha = 0.4f;
            softness = 0.65f;
            center = new Vector2(bo.x, bo.y);
            if (m.Rig == UnitRigKind.Biped && m.FloatHeight <= 0f && hover < 0.05f)
            {
                float hk = Mathf.Clamp(height / 1.75f, 0.5f, 1.6f);
                len = Mathf.Max(len, 0.5f * hk);
                wid = Mathf.Max(wid, 0.48f * hk);
                alpha = 0.5f;
                softness = 0.5f;
                var sun = SceneLighting.SunDirection;
                var away = new Vector2(-sun.x, -sun.y);
                if (away.sqrMagnitude > 1e-4f) center += away.normalized * (0.1f * hk * (1f - lie));
            }
            float shrink = 1f / (1f + hover * 0.35f);
            len *= shrink;
            wid *= shrink;
            alpha *= shrink * Mathf.Lerp(1f, 0.85f, lie);
        }
    }
}
