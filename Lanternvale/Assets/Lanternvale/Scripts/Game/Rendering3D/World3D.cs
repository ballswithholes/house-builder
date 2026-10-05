// World conventions of the 3D presentation.
//
//   * The GROUND IS THE XY PLANE, exactly as in the rules and the nav grid: x runs along the map (0..width), y is the
//     depth into the scene (0 = the front edge, nearest the camera; depth = the back edge). 1 unit = 1 metre.
//   * UP IS −Z. A point h metres above the ground point (x, y) is (x, y, −h).
//
// Why: every ground position in the game layer is a Vector2 (x, y) and Unity converts Vector2 → Vector3 as (x, y, 0),
// which is exactly the ground point under this convention. So feet positions, paths, nav points, ground effects and
// targeting previews stay correct without conversion code, and anything in the air is an explicit Vector3 with a
// negative z. Converting a Vector3 back to a Vector2 drops the height: the ground point under it (also correct).
//
// Models are authored in the usual Unity local space (Y up, +Z forward, +X right) and stood up with Upright / Yaw():
// local +Y → world −Z (up), local +Z → world +Y (into the scene), local +X → world +X. Screens are not mirrored: the
// camera keeps +X to the right and the back of the map (+Y) up the screen.
//
// No Unity light, skybox, terrain or physics feature is used (they assume Y-up); lighting is SceneLighting + the
// Lanternvale shaders, which use −Z as up.
using UnityEngine;

namespace Lanternvale.Game
{
    public static class World3D
    {
        /// <summary>World up (−Z).</summary>
        public static readonly Vector3 Up = new Vector3(0f, 0f, -1f);

        /// <summary>Into the scene, away from the default camera (+Y).</summary>
        public static readonly Vector3 Back = new Vector3(0f, 1f, 0f);

        /// <summary>Rotation that stands a Y-up model on the ground (its +Z faces into the scene).</summary>
        public static readonly Quaternion Upright = Quaternion.LookRotation(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f));

        /// <summary>The point h metres above a ground point.</summary>
        public static Vector3 At(Vector2 ground, float height = 0f) => new Vector3(ground.x, ground.y, -height);

        /// <summary>The point h metres above a ground point.</summary>
        public static Vector3 At(float x, float y, float height = 0f) => new Vector3(x, y, -height);

        /// <summary>Height above the ground plane.</summary>
        public static float HeightOf(Vector3 p) => -p.z;

        /// <summary>The ground point under p.</summary>
        public static Vector2 Ground(Vector3 p) => new Vector2(p.x, p.y);

        /// <summary>p raised by h metres.</summary>
        public static Vector3 Raise(Vector3 p, float h) => new Vector3(p.x, p.y, p.z - h);

        /// <summary>
        /// Upright model turned about the vertical axis. 0° faces into the scene (+Y), 90° faces right (+X),
        /// −90° faces left, 180° faces the camera.
        /// </summary>
        public static Quaternion Yaw(float degrees) => Upright * Quaternion.Euler(0f, degrees, 0f);

        /// <summary>Yaw (degrees, see Yaw) that faces a ground direction.</summary>
        public static float YawOf(Vector2 dir) => dir.sqrMagnitude < 1e-8f ? 90f : Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

        /// <summary>Upright model facing a ground direction.</summary>
        public static Quaternion Facing(Vector2 dir) => Yaw(YawOf(dir));

        /// <summary>A ground-plane direction (unit length) from a yaw in degrees.</summary>
        public static Vector2 DirOf(float yawDegrees)
        {
            float r = yawDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        /// <summary>Rotation that lays a quad authored in the XY plane flat on the ground (identity: it already is).</summary>
        public static readonly Quaternion Flat = Quaternion.identity;
    }
}
