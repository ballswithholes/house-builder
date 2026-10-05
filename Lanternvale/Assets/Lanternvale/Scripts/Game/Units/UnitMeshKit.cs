// Extra MeshBuilder primitives for characters: partial ellipsoid shells (hair caps, hoods, helmets), garment panels
// with a lining (skirts, coat tails, capes, sleeves), shoes, flat prisms (shields, blades) and small helpers.
//
// Angles: θ is measured around the Y axis from +Z (front) towards +X (the model's right): P = (r sinθ, y, r cosθ).
// φ is the polar angle from +Y (0 = top, 180 = bottom). All in degrees. Model space is Y-up (see World3D).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class UnitMeshKit
    {
        const float D2R = Mathf.Deg2Rad;

        static Vector3 OnEllipsoid(Vector3 c, Vector3 r, float thDeg, float phDeg)
        {
            float th = thDeg * D2R, ph = phDeg * D2R;
            float s = Mathf.Sin(ph);
            return c + new Vector3(r.x * s * Mathf.Sin(th), r.y * Mathf.Cos(ph), r.z * s * Mathf.Cos(th));
        }

        static Vector3 EllipsoidNormal(Vector3 c, Vector3 r, Vector3 p) =>
            new Vector3((p.x - c.x) / (r.x * r.x), (p.y - c.y) / (r.y * r.y), (p.z - c.z) / (r.z * r.z));

        /// <summary>
        /// Part of an ellipsoid surface (θ0..θ1 around Y, φ from phTop to phBottom(θ)) facing outwards (or inwards).
        /// The lower edge may vary with θ (hair lines, hood openings) through phBottom.
        /// </summary>
        public static void Shell(this MeshBuilder m, Vector3 c, Vector3 r, float th0, float th1, int nt,
                                 float phTop, System.Func<float, float> phBottom, int np, bool inward = false)
        {
            for (int j = 0; j < nt; j++)
            {
                float ta = Mathf.Lerp(th0, th1, (float)j / nt), tb = Mathf.Lerp(th0, th1, (float)(j + 1) / nt);
                float ba = phBottom(ta), bb = phBottom(tb);
                for (int i = 0; i < np; i++)
                {
                    float u0 = (float)i / np, u1 = (float)(i + 1) / np;
                    float pa0 = Mathf.Lerp(phTop, ba, u0), pa1 = Mathf.Lerp(phTop, ba, u1);
                    float pb0 = Mathf.Lerp(phTop, bb, u0), pb1 = Mathf.Lerp(phTop, bb, u1);
                    var a = OnEllipsoid(c, r, ta, pa0);
                    var b = OnEllipsoid(c, r, tb, pb0);
                    var cc = OnEllipsoid(c, r, tb, pb1);
                    var d = OnEllipsoid(c, r, ta, pa1);
                    var n = EllipsoidNormal(c, r, (a + b + cc + d) * 0.25f);
                    if (inward) n = -n;
                    if (pa0 < 0.01f && pb0 < 0.01f) m.TriangleFacing(a, cc, d, n);
                    else m.Quad(a, b, cc, d, n);
                }
            }
        }

        public static void Shell(this MeshBuilder m, Vector3 c, Vector3 r, float th0, float th1, int nt, float phTop, float phBottom, int np, bool inward = false)
        {
            float pb = phBottom;
            m.Shell(c, r, th0, th1, nt, phTop, _ => pb, np, inward);
        }

        /// <summary>
        /// Garment panel: a section (θ0..θ1) of a surface of revolution through prof = (radius, y) rings (top → bottom),
        /// squashed front-back by depthK, with a lining layer `thick` inside it. Closed garments use 0..360.
        /// </summary>
        public static void Panel(this MeshBuilder m, Vector3 c, float th0, float th1, int nt, IList<Vector2> prof, float depthK,
                                 Color outer, Color lining, float thick = 0.008f)
        {
            var keep = m.Color;
            for (int layer = 0; layer < 2; layer++)
            {
                if (layer == 1 && lining.a <= 0f) break;
                m.Color = layer == 0 ? outer : lining;
                float dr = layer == 0 ? 0f : thick;
                for (int j = 0; j < nt; j++)
                {
                    float ta = Mathf.Lerp(th0, th1, (float)j / nt) * D2R, tb = Mathf.Lerp(th0, th1, (float)(j + 1) / nt) * D2R;
                    float tm = (ta + tb) * 0.5f;
                    var radial = new Vector3(Mathf.Sin(tm), 0f, Mathf.Cos(tm) / Mathf.Max(0.2f, depthK));
                    if (layer == 1) radial = -radial;
                    for (int i = 0; i < prof.Count - 1; i++)
                    {
                        var a = Ring(c, prof[i], ta, depthK, dr);
                        var b = Ring(c, prof[i], tb, depthK, dr);
                        var cc = Ring(c, prof[i + 1], tb, depthK, dr);
                        var d = Ring(c, prof[i + 1], ta, depthK, dr);
                        m.Quad(a, b, cc, d, radial);
                    }
                }
            }
            m.Color = keep;
        }

        static Vector3 Ring(Vector3 c, Vector2 p, float th, float depthK, float dr)
        {
            float r = Mathf.Max(0.001f, p.x - dr);
            return c + new Vector3(r * Mathf.Sin(th), p.y, r * Mathf.Cos(th) * depthK);
        }

        /// <summary>Closed open-ended tube around Y (belts, cuffs, collars, bands); depthK squashes it front-back.</summary>
        public static void Band(this MeshBuilder m, Vector3 c, float r0, float r1, float y0, float y1, int sides = 10, float depthK = 1f)
        {
            m.Push().Translate(c).Scale(new Vector3(1f, 1f, depthK));
            m.Lathe(new[] { new Vector2(r0, y0), new Vector2(r1, y1) }, sides, false, false, false, null, 180f / sides);
            m.Pop();
        }

        /// <summary>Elliptic frustum/lathe around Y with a front-back squash (torsos, robes): flat front face.</summary>
        public static void Body(this MeshBuilder m, Vector3 c, IList<Vector2> prof, int sides, float depthK, bool capBottom = true, bool capTop = true, bool smooth = false)
        {
            m.Push().Translate(c).Scale(new Vector3(1f, 1f, depthK));
            m.Lathe(prof, sides, smooth, capBottom, capTop, null, 180f / sides);
            m.Pop();
        }

        /// <summary>Starts a frame at `at` whose +Y points along `dir` (Pop when done).</summary>
        public static MeshBuilder Aim(this MeshBuilder m, Vector3 at, Vector3 dir)
        {
            m.Push().Translate(at);
            if (dir.sqrMagnitude > 1e-8f) m.Rotate(Quaternion.FromToRotation(Vector3.up, dir.normalized));
            return m;
        }

        /// <summary>Flat convex shape in the XY plane (pts) with thickness along Z (shields, blades, wings, plates).</summary>
        public static void Flat(this MeshBuilder m, IList<Vector2> pts, float thickness)
        {
            m.Push().Rotate(-90f, 0f, 0f);
            m.Extrude(pts, -thickness * 0.5f, thickness * 0.5f);
            m.Pop();
        }

        /// <summary>
        /// Boot/shoe: ankle at `ankle` (the foot bone), sole on y = 0, toe towards +Z. len/width in metres.
        /// </summary>
        public static void Shoe(this MeshBuilder m, Vector3 ankle, float len, float width, Color color, Color sole)
        {
            var keep = m.Color;
            float zb = ankle.z - len * 0.28f, zm = ankle.z + len * 0.38f, zt = ankle.z + len * 0.72f;
            float h = Mathf.Max(ankle.y + width * 0.25f, len * 0.36f), hm = len * 0.26f, ht = len * 0.2f;
            float w = width * 0.5f, wt = width * 0.46f;
            float x = ankle.x;
            m.Color = color;
            // instep: heel block rising to the ankle, sloping to the toe
            var b0 = new Vector3(x - w, 0.012f, zb); var b1 = new Vector3(x + w, 0.012f, zb);
            var b2 = new Vector3(x + wt, 0.012f, zm); var b3 = new Vector3(x - wt, 0.012f, zm);
            var t0 = new Vector3(x - w * 0.92f, h, zb + 0.01f); var t1 = new Vector3(x + w * 0.92f, h, zb + 0.01f);
            var t2 = new Vector3(x + wt, hm, zm); var t3 = new Vector3(x - wt, hm, zm);
            m.Quad(t0, t1, t2, t3, Vector3.up + Vector3.forward * 0.5f);
            m.Quad(b0, b1, t1, t0, Vector3.back);
            m.Quad(b1, b2, t2, t1, Vector3.right);
            m.Quad(b3, b0, t0, t3, Vector3.left);
            // toe cap
            m.Sphere(new Vector3(x, ht * 0.62f, (zm + zt) * 0.5f), new Vector3(wt * 1.02f, ht * 0.75f, (zt - zm) * 0.62f + 0.01f), 8, 5, true);
            m.Box(new Vector3(x, hm * 0.45f, zm - 0.004f), new Vector3(wt * 2f, hm * 0.9f, 0.012f));
            // sole
            m.Color = sole;
            m.Box(new Vector3(x, 0.012f, (zb + zt) * 0.5f + 0.005f), new Vector3(width * 1.04f, 0.024f, zt - zb + 0.04f));
            m.Color = keep;
        }

        /// <summary>Paw/hoof: a squashed ball on the ground under the ankle.</summary>
        public static void Paw(this MeshBuilder m, Vector3 ankle, float r, float fwd = 0.25f)
        {
            m.Sphere(new Vector3(ankle.x, r * 0.55f, ankle.z + r * fwd), new Vector3(r, r * 0.6f, r * 1.25f), 7, 5, false);
        }

        /// <summary>A chain of balls from a to b (braids, beads, tails of fur, vertebrae).</summary>
        public static void Beads(this MeshBuilder m, Vector3 a, Vector3 b, int count, float r0, float r1, Color c0, Color c1, int sides = 6)
        {
            var keep = m.Color;
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? (float)i / (count - 1) : 0f;
                m.Color = Color.Lerp(c0, c1, (i & 1) == 0 ? 0f : 1f);
                float r = Mathf.Lerp(r0, r1, t);
                m.Sphere(Vector3.Lerp(a, b, t), new Vector3(r, r * 1.15f, r), sides, 4, false);
            }
            m.Color = keep;
        }

        /// <summary>Cone spike from `at` along `dir` (hair spikes, horns, claws, thorns).</summary>
        public static void Spike(this MeshBuilder m, Vector3 at, Vector3 dir, float radius, float length, int sides = 5)
        {
            m.Aim(at, dir);
            m.Cone(Vector3.zero, radius, length, sides);
            m.Pop();
        }

        /// <summary>A curved horn/tusk/claw: tapered segments along a quadratic curve a → (a + bend) → b.</summary>
        public static void Curve(this MeshBuilder m, Vector3 a, Vector3 ctrl, Vector3 b, float r0, float r1, int segs = 4, int sides = 6)
        {
            var prev = a;
            float prevR = r0;
            for (int i = 1; i <= segs; i++)
            {
                float t = (float)i / segs;
                var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * ctrl + t * t * b;
                float r = Mathf.Lerp(r0, r1, t);
                m.Segment(prev, p, prevR, Mathf.Max(0.002f, r), sides, false, true);
                prev = p;
                prevR = r;
            }
        }
    }
}
