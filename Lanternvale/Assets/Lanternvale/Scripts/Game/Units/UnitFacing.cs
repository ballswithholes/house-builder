// Facing conventions of UnitView.SetFacing, shared with tools (the offline preview places units the same way).
//
// SetFacing(±1) means "face screen-right / screen-left", turned a little towards the camera (a 3/4 view), for the
// CURRENT camera yaw: the camera rig turns ±45° around the default view, and a unit told to face +X would otherwise
// show the camera its back. Yaws are World3D.Yaw degrees (0 = into the scene, +Y; 90 = +X); CameraRig.Yaw uses the
// same convention (0 = looking into +Y, + = turned right), so screen-right is cameraYaw + 90.
using UnityEngine;

namespace Lanternvale.Game
{
    public static class UnitFacing
    {
        /// <summary>Degrees SetFacing turns the face from screen-left/right towards the camera.</summary>
        public const float Bias = 15f;

        /// <summary>World yaw for SetFacing(dir) under a camera turned by cameraYaw degrees (CameraRig.Yaw).</summary>
        public static float SideYaw(int dir, float cameraYaw)
        {
            float y = cameraYaw + (dir >= 0 ? 90f + Bias : -(90f + Bias));
            return Mathf.Repeat(y + 180f, 360f) - 180f;
        }
    }
}
