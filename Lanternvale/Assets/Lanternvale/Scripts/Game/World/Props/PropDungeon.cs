// Expansion dungeon props (Docs/Expansion.md §10, props-dungeon): the RegisterDungeon hook, the shared dungeon palette
// and helpers (rock outcrops, inward-facing openings, fire parts). The models live in PropDungeonCave.cs (caves, ice,
// drowned and treasure), PropDungeonCrypt.cs (crypt furniture, fire, doorways) and PropDungeonRaid.cs (raid set pieces).
//
// Conventions as every prop (Docs/ThreeD.md §7): Y-up model space, front faces −Z, solid footprint inside the
// recommended collider ellipse (published per key in Docs/ArtKeys.md, "3D-only model keys"). Every helper and colour
// here is prefixed Dg so it never collides with another builder's partial-class members.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static partial void RegisterDungeon(Dictionary<string, Recipe> r)
        {
            RegisterDungeonCave(r);
            RegisterDungeonCrypt(r);
            RegisterDungeonRaid(r);
        }

        // ------------------------------------------------------------------ palette (sRGB vertex colours)

        static class DgPal
        {
            static Color H(string hex) => Paint.Hex(hex);

            // cave rock: cool violet-grey with warm dusty tops
            public static readonly Color Rock = H("#877D8F"), RockLight = H("#A69CA8"), RockDark = H("#5F5768"), RockTop = H("#B7AC9C");
            public static readonly Color Void = H("#141119"), VoidEdge = H("#2C2633");
            // crypt masonry: pale cool limestone, warm sandstone accents
            public static readonly Color Crypt = H("#A9A5AF"), CryptLight = H("#C4C0C6"), CryptDark = H("#7C7887"), CryptWarm = H("#BFB29A");
            public static readonly Color Bone = H("#EADFC4"), BoneDark = H("#C2B396"), Soot = H("#3B3439"), Ash = H("#8E8783"), AshLight = H("#B3ABA4");
            public static readonly Color Ice = H("#CDEFFA"), IceMid = H("#9ED7EE"), IceDeep = H("#6DB3DA"), Snow = H("#F3F7FA"), Frost = H("#E4F4FB");
            public static readonly Color WetStone = H("#6C7A7E"), WetStoneLight = H("#8C9A99"), Algae = H("#5D8C68"), Kelp = H("#4C7656"), Barnacle = H("#D9CDB6"), Shell = H("#E9B9A4");
            public static readonly Color Gold = H("#F2C14E"), GoldDeep = H("#C98F2A"), GoldLight = H("#FFE59A"), Ruby = H("#D6435A"), Sapphire = H("#4F7FD8"), Emerald = H("#43B07A");
            public static readonly Color Root = H("#6E5444"), RootDark = H("#4F3C33"), RootLight = H("#8E705A");
            public static readonly Color Thorn = H("#33283B"), ThornLight = H("#54445F"), Blight = H("#B98BFF"), BlightDeep = H("#7B55D0");
            public static readonly Color Heart = H("#FF7AB0"), HeartDeep = H("#C2407F"), Spirit = H("#9FF3E4"), SpiritDeep = H("#5FC7D8");
            public static readonly Color IronOld = H("#4A4550"), Rust = H("#8C5A3C"), Wood = H("#7A5A44"), WoodDark = H("#57402F");
        }

        // ------------------------------------------------------------------ shared helpers

        /// <summary>Jittered rock lump coloured by normal: dusty/mossy tops, `side` flanks, dark undersides.</summary>
        static void DgRock(MeshBuilder mb, Vector3 c, Vector3 r, int seed, Color side, Color top, float flatten = 0.35f, int detail = 1, float jitter = 0.22f)
        {
            FacetBlob(mb, c, r, detail, jitter, seed, flatten, ByNormal(top, side, Paint.Shade(side, 0.72f), 0.45f));
        }

        /// <summary>Rock coloured by normal: moss only on the flattest tops, dusty `top` on upward slopes, dark undersides.</summary>
        static System.Func<Vector3, Color> DgMossRock(Color rock, Color top, Color moss, float mossFrom = 0.8f)
        {
            return n =>
            {
                if (n.y > mossFrom) return Color.Lerp(moss, Paint.Shade(moss, 1.1f), (n.y - mossFrom) / (1f - mossFrom));
                if (n.y > 0.3f) return Color.Lerp(rock, top, Mathf.SmoothStep(0f, 1f, (n.y - 0.3f) / (mossFrom - 0.3f)));
                if (n.y < -0.2f) return Paint.Shade(rock, 0.72f);
                return rock;
            };
        }

        /// <summary>
        /// The dark inside of an arched opening (doorway, cave mouth, gate): an arch tunnel from z0 (front) to z1 whose
        /// faces look INWARDS, plus its floor and back wall, shading from `edge` at the mouth to `deep` at the back.
        /// Built into its own (plain, unoutlined) part so the ink hull never draws inside.
        /// </summary>
        static void DgOpening(MeshBuilder mb, float halfW, float springY, float z0, float z1, Color edge, Color deep, int arcSeg = 7, int depthSeg = 3)
        {
            var prof = new List<Vector2> { new Vector2(-halfW, 0f), new Vector2(-halfW, springY) };
            for (int i = 1; i < arcSeg; i++)
            {
                float a = Mathf.PI - Mathf.PI * i / arcSeg;
                prof.Add(new Vector2(Mathf.Cos(a) * halfW, springY + Mathf.Sin(a) * halfW));
            }
            prof.Add(new Vector2(halfW, springY));
            prof.Add(new Vector2(halfW, 0f));
            var axis = new Vector2(0f, springY * 0.6f);
            for (int k = 0; k < depthSeg; k++)
            {
                float za = Mathf.Lerp(z0, z1, (float)k / depthSeg), zb = Mathf.Lerp(z0, z1, (float)(k + 1) / depthSeg);
                var col = Color.Lerp(edge, deep, Mathf.Pow((k + 0.5f) / depthSeg, 0.6f));
                for (int i = 0; i < prof.Count - 1; i++)
                {
                    var p = prof[i];
                    var q = prof[i + 1];
                    var inward = axis - (p + q) * 0.5f;
                    float lightK = q.y > springY + 0.01f || p.y > springY + 0.01f ? 0.8f : 1f;   // the vault reads darker
                    QuadC(mb, new Vector3(p.x, p.y, za), new Vector3(q.x, q.y, za), new Vector3(q.x, q.y, zb), new Vector3(p.x, p.y, zb),
                          new Vector3(inward.x, inward.y, 0f), Paint.Shade(col, lightK));
                }
                // floor
                QuadC(mb, new Vector3(-halfW, 0.015f, za), new Vector3(halfW, 0.015f, za), new Vector3(halfW, 0.015f, zb), new Vector3(-halfW, 0.015f, zb),
                      Vector3.up, Paint.Shade(col, 1.15f));
            }
            // back wall (facing the camera)
            var c = new Vector3(0f, springY * 0.6f, z1);
            for (int i = 0; i < prof.Count - 1; i++)
                Tri(mb, c, new Vector3(prof[i].x, prof[i].y, z1), new Vector3(prof[i + 1].x, prof[i + 1].y, z1), Vector3.back, deep);
            Tri(mb, c, new Vector3(halfW, 0f, z1), new Vector3(-halfW, 0f, z1), Vector3.back, deep);
        }

        /// <summary>Flickering campfire flames as a child part (shown when lit), scaled; returns the flames' transform.</summary>
        static Transform DgFlames(Rig rig, int b, Vector3 at, float scale, float flicker = 0.12f)
        {
            var mf = rig.Part("Flames", Cached("prop_campfire#flames" + b, () => BuildFlames(b)), at, Quaternion.identity, Skin.Plain);
            var t = mf.transform;
            t.localScale = new Vector3(scale, scale, scale);
            var motion = mf.gameObject.AddComponent<PropMotion>();
            motion.Flicker = flicker;
            motion.FlickerSpeed = 7.5f + b * 0.8f;
            rig.Model.SetLit += ActiveSwitch(mf.gameObject);
            return t;
        }

        /// <summary>A body with baked glowing pieces (lit / dark mesh variants), optional flames and light anchors.</summary>
        static PropModel DgLit(string art, int seed, System.Func<int, bool, MeshBuilder> build, float radius, Vector3? flamesAt, float flameScale, params Vector3[] lights)
        {
            int b = Bucket(seed);
            string key = art + "#" + b;
            var rig = new Rig(art);
            var mf = rig.Body(LitMesh(key, true, lit => build(b, lit)));
            rig.Model.SetLit = MeshSwitch(mf, key, lit => build(b, lit));
            if (flamesAt.HasValue) DgFlames(rig, b, flamesAt.Value, flameScale);
            foreach (var l in lights) rig.Light(l);
            return rig.Done(radius);
        }

        /// <summary>A Simple prop plus light anchors (glowing crystals, portals: lights belong in the map data).</summary>
        static PropModel DgGlow(string art, int seed, System.Func<int, MeshBuilder> build, float radius, params Vector3[] lights)
        {
            var m = Simple(art, seed, build, radius);
            foreach (var l in lights) m.LightAnchors.Add(l);
            return m;
        }

        /// <summary>A lone bone (long bone with knuckled ends) from a to b.</summary>
        static void DgBone(MeshBuilder mb, Vector3 a, Vector3 b, float r, Color bone)
        {
            mb.Color = bone;
            mb.Segment(a, b, r * 0.75f, r * 0.75f, 5);
            var d = (b - a).normalized;
            var side = Vector3.Cross(d, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side = side.normalized * r * 0.7f;
            Gem(mb, a + side, r * 1.15f);
            Gem(mb, a - side, r * 1.15f);
            Gem(mb, b + side, r * 1.1f);
            Gem(mb, b - side, r * 1.1f);
        }

        /// <summary>A small skull (cranium, eye holes, jaw) at `at` looking along `fwd` (horizontal).</summary>
        static void DgSkull(MeshBuilder mb, Vector3 at, float s, float yawDeg, Color bone, float tiltDeg = 0f)
        {
            var keep = mb.Color;
            mb.Push().Translate(at).Rotate(tiltDeg, yawDeg, 0f).Scale(s);
            mb.Color = bone;
            mb.Sphere(new Vector3(0f, 0.11f, 0.02f), new Vector3(0.1f, 0.095f, 0.115f), 7, 5, false);
            mb.Box(new Vector3(0f, 0.045f, -0.06f), new Vector3(0.12f, 0.07f, 0.08f));
            mb.Color = Paint.Shade(bone, 0.92f);
            mb.Box(new Vector3(0f, 0.012f, -0.055f), new Vector3(0.1f, 0.024f, 0.08f));
            mb.Color = DgPal.Soot;
            for (int s2 = -1; s2 <= 1; s2 += 2)
                Gem(mb, new Vector3(s2 * 0.038f, 0.09f, -0.088f), new Vector3(0.03f, 0.028f, 0.02f));
            Gem(mb, new Vector3(0f, 0.055f, -0.1f), new Vector3(0.012f, 0.016f, 0.01f));
            mb.Pop();
            mb.Color = keep;
        }

        /// <summary>Points around an ellipse in the XZ plane (rings for lofted cushions, rims).</summary>
        static Vector3[] DgRing(Vector3 c, float rx, float rz, int n, float wobble = 0f, int seed = 0)
        {
            var pts = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float k = 1f + (wobble > 0f ? (Hash01(new Vector3(i, seed, n), seed) * 2f - 1f) * wobble : 0f);
                pts[i] = c + new Vector3(Mathf.Cos(a) * rx * k, 0f, Mathf.Sin(a) * rz * k);
            }
            return pts;
        }
    }
}
