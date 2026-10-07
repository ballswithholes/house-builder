// Transition waymarkers (MapView.BuildTransition; the preview tool builds them the same way).
//
//   front / back edge and mid-map exits: a weathered wooden arch (torii-like) with a lantern hanging from the lintel,
//     seen face-on by the camera.
//   left / right edge exits: a pair of lantern posts on either side of the road, a lantern hanging from each post's arm
//     and a small arrow plank on the near post. (An arch there is seen edge-on and reads as a leaning gallows.)
//
// Styles (WaymarkerStyle, StyleFor): Wood (the valley's), Snow (the same wood under caps of snow: the peaks) and Stone
// (indoors: dressed stone posts crowned with iron fire bowls, a stone arch with a hanging lantern).
//
// Styled entrances (TransitionDef.marker cave | door | stairs | portal, BuildEntrance in Waymarker.Entrances.cs): the
// prop library's model (prop_cave_mouth, prop_crypt_door, prop_stairs_down, prop_raid_portal) when it has one, else a
// stand-in (PropModels.MarkerStandIn). marker "none" builds nothing.
//
// The lanterns glow only while lit (SetLit: MapView lights them with the night-only waymarker lights).
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    /// <summary>The look of a waymarker (Waymarker.StyleFor picks it from the map).</summary>
    internal enum WaymarkerStyle { Wood, Snow, Stone }

    internal sealed partial class Waymarker
    {
        /// <summary>The frame (arch or posts), child of the holder; mirrored / stretched as needed.</summary>
        public Transform Root;
        /// <summary>True for the side-edge post pair, false for an arch.</summary>
        public bool Posts;
        public WaymarkerStyle Style;
        /// <summary>Outlined frame renderers (hover look).</summary>
        public readonly List<Renderer> Frame = new List<Renderer>();
        /// <summary>The lanterns (children of the holder, so they keep their shape when the arch stretches).</summary>
        public readonly List<MeshRenderer> Lanterns = new List<MeshRenderer>();
        /// <summary>World points of the lantern flames (one light each).</summary>
        public readonly List<Vector3> Lamps = new List<Vector3>();
        /// <summary>World bounds of the frame and lanterns (picking, labels).</summary>
        public Bounds Bounds;
        bool lit;

        const float ArchLampY = 2.02f;
        static readonly Vector3 PostLamp = new Vector3(0.56f, 1.84f, 0f);   // per post, z = ±half span
        /// <summary>The flame of a stone post's fire bowl (per post, z = ±half span).</summary>
        static readonly Vector3 StoneLamp = new Vector3(0f, 2.3f, 0f);

        /// <summary>The waymarker style for a map: stone indoors, snow-capped wood on snowy maps, else wood.</summary>
        public static WaymarkerStyle StyleFor(Lanternvale.Data.MapDef def, bool indoor)
        {
            if (indoor) return WaymarkerStyle.Stone;
            if (def != null && (def.biome == "peaks" || (def.ambient != null && def.ambient.snow))) return WaymarkerStyle.Snow;
            return WaymarkerStyle.Wood;
        }

        static string Suffix(WaymarkerStyle style) => style == WaymarkerStyle.Snow ? "_snow" : style == WaymarkerStyle.Stone ? "_stone" : "";

        /// <summary>
        /// Builds the marker under holder (at the exit's edge point). dir: outward towards the exit (zero = mid-map);
        /// span: road width between the posts / arch legs (m). Starts unlit.
        /// </summary>
        public static Waymarker Build(Transform holder, Vector2 dir, float span, WaymarkerStyle style = WaymarkerStyle.Wood)
        {
            var w = new Waymarker { Posts = Mathf.Abs(dir.x) > 0.5f, Style = style };
            bool stone = style == WaymarkerStyle.Stone;
            var root = new GameObject("Waymarker").transform;
            root.SetParent(holder, false);
            root.localPosition = Vector3.zero;
            w.Root = root;
            var lampsLocal = new List<Vector3>(2);
            var feet = new List<Vector3>(2);
            float lanternScale = 1f;
            Mesh frame;
            if (w.Posts)
            {
                // posts stand along the edge (world y), arms reach into the map: mirror the model for the right edge
                float half = Mathf.Clamp(span * 0.5f, 1.1f, 2.4f);
                root.localRotation = World3D.Upright;
                root.localScale = new Vector3(dir.x < 0f ? 1f : -1f, 1f, 1f);
                int key = Mathf.RoundToInt(half * 10f);
                frame = MeshCache.Get("lv_waymarker_posts_" + key + Suffix(style),
                                      () => stone ? BuildStonePostsMesh(key / 10f) : BuildPostsMesh(key / 10f, style == WaymarkerStyle.Snow));
                var lampAt = stone ? StoneLamp : PostLamp;
                lampsLocal.Add(new Vector3(lampAt.x, lampAt.y, -key / 10f));
                lampsLocal.Add(new Vector3(lampAt.x, lampAt.y, key / 10f));
                feet.Add(new Vector3(0f, 0f, -key / 10f));
                feet.Add(new Vector3(0f, 0f, key / 10f));
            }
            else
            {
                root.rotation = dir == Vector2.zero ? World3D.Upright : World3D.Facing(dir);
                root.localScale = dir == Vector2.zero ? new Vector3(0.6f, 0.75f, 0.6f) : new Vector3(span / 3f, 1f, 1f);
                if (dir == Vector2.zero) lanternScale = 0.75f;
                frame = stone ? MeshCache.Get("lv_waymarker_arch_stone", BuildStoneArchMesh)
                      : style == WaymarkerStyle.Snow ? MeshCache.Get("lv_waymarker_arch_snow", () => BuildArchMesh(true))
                      : MeshCache.Get("lv_waymarker_arch", () => BuildArchMesh(false));
                lampsLocal.Add(new Vector3(0f, ArchLampY, 0f));
                feet.Add(new Vector3(-1.5f, 0f, 0f));
                feet.Add(new Vector3(1.5f, 0f, 0f));
            }
            var frameGo = new GameObject(w.Posts ? "Posts" : "Arch");
            frameGo.transform.SetParent(root, false);
            frameGo.AddComponent<MeshFilter>().sharedMesh = frame;
            var fr = frameGo.AddComponent<MeshRenderer>();
            fr.sharedMaterials = Materials3D.WithOutline();
            w.Frame.Add(fr);
            w.Bounds = fr.bounds;

            // the lights: paper lanterns, or the fires in the stone posts' bowls
            bool bowls = stone && w.Posts;
            var dark = bowls ? MeshCache.Get("lv_waymarker_fire_dark", () => BuildFireMesh(false))
                             : MeshCache.Get("lv_waymarker_lantern_dark", () => BuildLanternMesh(false));
            for (int i = 0; i < lampsLocal.Count; i++)
            {
                var lamp = root.TransformPoint(lampsLocal[i]);
                var go = new GameObject("Lantern");
                go.transform.SetParent(holder, false);
                go.transform.position = lamp;
                go.transform.rotation = bowls ? World3D.Upright : root.rotation;
                go.transform.localScale = Vector3.one * lanternScale;
                go.AddComponent<MeshFilter>().sharedMesh = dark;
                var lr = go.AddComponent<MeshRenderer>();
                lr.sharedMaterial = Materials3D.LowPoly;
                w.Lanterns.Add(lr);
                w.Lamps.Add(lamp);
                w.Bounds.Encapsulate(lr.bounds);
            }
            // a soft shadow under each leg (one blob under the whole marker would darken the road between them)
            for (int i = 0; i < feet.Count; i++)
            {
                var anchor = new GameObject("Leg");
                anchor.transform.SetParent(holder, false);
                var p = root.TransformPoint(feet[i]);
                anchor.transform.position = new Vector3(p.x, p.y, 0f);
                MeshCache.AddShadow(anchor.transform, 0.42f, 0.34f, 0.32f);
            }
            return w;
        }

        /// <summary>
        /// Places a side exit's lantern posts clear of the props around them (a post must not stand inside a tree's
        /// canopy or a building): narrows the road between them (down to the narrowest post pair) and, where that is not
        /// enough, slides the pair along the edge by up to shiftMax — the smallest change, keeping the road as centred
        /// between the posts as it can. obstacles: the props' ground footprints (world bounds, x/y). at: the posts' centre
        /// on the edge; span: the road width. MapView and the preview tool call it before Build.
        /// </summary>
        public static void FitPosts(ref Vector2 at, Vector2 dir, ref float span, IList<Rect> obstacles, float shiftMax)
        {
            if (Mathf.Abs(dir.x) <= 0.5f || obstacles == null || obstacles.Count == 0) return;
            float half0 = Mathf.Clamp(span * 0.5f, 1.1f, 2.4f);
            float inward = dir.x < 0f ? 1f : -1f;   // the lantern arms reach into the map
            float bestScore = float.MaxValue, bestHalf = half0, bestShift = 0f;
            for (float half = half0; half >= 1.1f - 1e-4f; half -= 0.05f)
                for (int si = 0; si <= 48; si++)
                {
                    // 0, +0.05, −0.05, +0.1, …
                    float shift = (si + 1) / 2 * 0.05f * (si % 2 == 1 ? 1f : -1f);
                    if (Mathf.Abs(shift) > shiftMax + 1e-4f) break;
                    float overlap = 0f;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var post = new Vector2(at.x, at.y + shift + k * half);
                        overlap += Intrusion(post, obstacles, 0.45f) + Intrusion(post + new Vector2(0.56f * inward, 0f), obstacles, 0.4f);
                    }
                    // keep the road centred between the posts: narrowing is cheaper than sliding off the road
                    float score = overlap * 100f + (half0 - half) + Mathf.Abs(shift) * 2.2f;
                    if (score < bestScore) { bestScore = score; bestHalf = half; bestShift = shift; }
                    if (overlap <= 0f) break;   // the smallest shift for this span is found
                }
            at = new Vector2(at.x, at.y + bestShift);
            span = Mathf.Min(span, bestHalf * 2f);
        }

        /// <summary>How deep (m) a point with a clearance radius reaches into the obstacles' rects.</summary>
        static float Intrusion(Vector2 p, IList<Rect> obstacles, float clear)
        {
            float sum = 0f;
            for (int i = 0; i < obstacles.Count; i++)
            {
                var r = obstacles[i];
                float dx = Mathf.Max(0f, Mathf.Max(r.xMin - p.x, p.x - r.xMax));
                float dy = Mathf.Max(0f, Mathf.Max(r.yMin - p.y, p.y - r.yMax));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d < clear) sum += clear - d;
            }
            return sum;
        }

        /// <summary>Glowing (lit) or dark lanterns: paper lanterns, the stone posts' fire bowls, an entrance's own lamps.</summary>
        public void SetLit(bool on)
        {
            if (on == lit) return;
            lit = on;
            if (Prop != null) { Prop.SetLit?.Invoke(on); return; }
            bool bowls = Style == WaymarkerStyle.Stone && Posts;
            Mesh mesh;
            if (bowls) mesh = on ? MeshCache.Get("lv_waymarker_fire_lit", () => BuildFireMesh(true)) : MeshCache.Get("lv_waymarker_fire_dark", () => BuildFireMesh(false));
            else mesh = on ? MeshCache.Get("lv_waymarker_lantern_lit", () => BuildLanternMesh(true)) : MeshCache.Get("lv_waymarker_lantern_dark", () => BuildLanternMesh(false));
            for (int i = 0; i < Lanterns.Count; i++)
                if (Lanterns[i] != null) Lanterns[i].GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        // ------------------------------------------------------------------ meshes (model space, Y up, front = −Z)

        static readonly Color Wood = new Color(0.478f, 0.353f, 0.263f);    // #7a5a43
        static readonly Color DarkWood = new Color(0.29f, 0.227f, 0.188f); // #4a3a30
        static readonly Color Stone = new Color(0.639f, 0.612f, 0.58f);    // #a39c94
        static readonly Color Snow = new Color(0.93f, 0.95f, 0.99f);
        static readonly Color Ashlar = new Color(0.58f, 0.55f, 0.6f);      // dressed stone, cool grey-violet
        static readonly Color AshlarDark = new Color(0.43f, 0.41f, 0.46f);
        static readonly Color Iron = new Color(0.27f, 0.25f, 0.28f);

        /// <summary>A weathered wooden waymarker arch (torii-like), spanning local X (legs at ±1.5 m), walked through along Z.</summary>
        static Mesh BuildArchMesh(bool snow)
        {
            var mb = new MeshBuilder(17) { Jitter = 0.07f, AOStrength = 0.3f, AOHeight = 0.6f };
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = Stone;
                mb.Cylinder(new Vector3(s * 1.5f, 0f, 0f), 0.24f, 0.2f, 0.32f, 6);
                mb.Color = Wood;
                mb.Cylinder(new Vector3(s * 1.5f, 0.3f, 0f), 0.13f, 0.11f, 2.45f, 6);
            }
            mb.Color = DarkWood;
            mb.Box(new Vector3(0f, 2.78f, 0f), new Vector3(3.9f, 0.18f, 0.3f));
            mb.Push().Translate(1.98f, 2.84f, 0f).Rotate(0f, 0f, 12f);
            mb.Box(Vector3.zero, new Vector3(0.3f, 0.14f, 0.3f));
            mb.Pop();
            mb.Push().Translate(-1.98f, 2.84f, 0f).Rotate(0f, 0f, -12f);
            mb.Box(Vector3.zero, new Vector3(0.3f, 0.14f, 0.3f));
            mb.Pop();
            mb.Color = Wood;
            mb.Box(new Vector3(0f, 2.42f, 0f), new Vector3(3.2f, 0.13f, 0.18f));
            // a little plaque and the lantern's hook
            mb.Color = Paint.Hex("#d8c7a2");
            mb.Box(new Vector3(0f, 2.6f, 0f), new Vector3(0.5f, 0.3f, 0.06f));
            mb.Color = DarkWood;
            mb.Segment(new Vector3(0f, 2.36f, 0f), new Vector3(0f, 2.22f, 0f), 0.02f, 0.02f, 4);
            if (snow)
            {
                // snow along the top beam (thicker in the middle), a strip on the tie beam, drifts at the feet
                mb.Color = Snow;
                mb.Box(new Vector3(0f, 2.9f, 0f), new Vector3(3.7f, 0.07f, 0.32f));
                mb.Blob(new Vector3(-0.7f, 2.93f, 0f), new Vector3(0.8f, 0.08f, 0.19f), 1, 0.2f, 3, 0.8f);
                mb.Blob(new Vector3(0.9f, 2.93f, 0f), new Vector3(0.6f, 0.07f, 0.19f), 1, 0.2f, 5, 0.8f);
                mb.Box(new Vector3(0.25f, 2.5f, 0f), new Vector3(2.3f, 0.04f, 0.19f));
                for (int s = -1; s <= 1; s += 2) mb.Blob(new Vector3(s * 1.5f, 0.06f, 0f), new Vector3(0.42f, 0.14f, 0.38f), 1, 0.2f, s > 0 ? 9 : 4, 0.9f);
            }
            return mb.ToMesh(snow ? "lv_waymarker_arch_snow" : "lv_waymarker_arch");
        }

        /// <summary>
        /// Two lantern posts at z = ±half (the road runs along X between them), each with an arm reaching +X (into the
        /// map) holding a lantern, a little pitched cap, and an arrow plank pointing −X (out of the map) on the near post.
        /// </summary>
        static Mesh BuildPostsMesh(float half, bool snow)
        {
            var mb = new MeshBuilder(23) { Jitter = 0.07f, AOStrength = 0.3f, AOHeight = 0.6f };
            for (int s = -1; s <= 1; s += 2)
            {
                var b = new Vector3(0f, 0f, s * half);
                mb.Color = Stone;
                mb.Cylinder(b, 0.26f, 0.21f, 0.3f, 6);
                mb.Color = Paint.Shade(Wood, s < 0 ? 1f : 0.94f);
                mb.Cylinder(b + new Vector3(0f, 0.28f, 0f), 0.12f, 0.1f, 2.22f, 6);
                // collar and pitched cap
                mb.Color = DarkWood;
                mb.Box(b + new Vector3(0f, 2.5f, 0f), new Vector3(0.27f, 0.07f, 0.27f));
                mb.Push().Translate(b + new Vector3(0f, 2.53f, 0f)).Rotate(0f, 45f, 0f);
                mb.Cone(Vector3.zero, 0.24f, 0.22f, 4);
                mb.Pop();
                // arm with a brace, and the hook the lantern hangs from
                mb.Box(b + new Vector3(0.33f, 2.22f, 0f), new Vector3(0.66f, 0.09f, 0.09f));
                mb.Segment(b + new Vector3(0.09f, 1.92f, 0f), b + new Vector3(0.42f, 2.2f, 0f), 0.035f, 0.035f, 4);
                mb.Segment(b + new Vector3(PostLamp.x, 2.18f, 0f), b + new Vector3(PostLamp.x, 2.05f, 0f), 0.016f, 0.016f, 4);
                // a strip of paper charm on the arm
                mb.Color = Paint.Hex("#efe6d2");
                mb.Box(b + new Vector3(0.2f, 2.07f, 0f), new Vector3(0.05f, 0.2f, 0.012f));
                if (snow)
                {
                    // a cap of snow on the pitched cap and the arm, a drift against the foot stone
                    mb.Color = Snow;
                    mb.Push().Translate(b + new Vector3(0f, 2.63f, 0f)).Rotate(0f, 45f, 0f);
                    mb.Cone(Vector3.zero, 0.19f, 0.13f, 4);
                    mb.Pop();
                    mb.Box(b + new Vector3(0.36f, 2.28f, 0f), new Vector3(0.6f, 0.04f, 0.1f));
                    mb.Blob(b + new Vector3(0f, 0.06f, 0f), new Vector3(0.38f, 0.13f, 0.36f), 1, 0.2f, s > 0 ? 9 : 4, 0.9f);
                }
            }
            // arrow plank on the near post, pointing out of the map (−X)
            var near = new Vector3(0f, 1.42f, -half - 0.13f);
            mb.Color = Paint.Hex("#b89a74");
            mb.Box(near + new Vector3(-0.2f, 0f, 0f), new Vector3(0.52f, 0.17f, 0.05f));
            mb.Push().Translate(near + new Vector3(-0.46f, 0f, 0f)).Rotate(0f, 0f, 45f);
            mb.Box(Vector3.zero, new Vector3(0.13f, 0.13f, 0.05f));
            mb.Pop();
            mb.Color = DarkWood;
            mb.Box(near + new Vector3(-0.17f, 0f, -0.028f), new Vector3(0.3f, 0.035f, 0.01f));
            return mb.ToMesh(snow ? "lv_waymarker_posts_snow" : "lv_waymarker_posts");
        }

        /// <summary>A small framed paper lantern centred on the origin (glass ≈ 0.24 × 0.33 m).</summary>
        static Mesh BuildLanternMesh(bool lit)
        {
            var mb = new MeshBuilder(18) { Jitter = 0.04f };
            mb.Color = DarkWood;
            mb.Box(new Vector3(0f, 0.19f, 0f), new Vector3(0.3f, 0.05f, 0.3f));
            mb.Push().Translate(0f, 0.21f, 0f).Rotate(0f, 45f, 0f);
            mb.Cone(Vector3.zero, 0.2f, 0.1f, 4);
            mb.Pop();
            mb.Box(new Vector3(0f, -0.19f, 0f), new Vector3(0.26f, 0.05f, 0.26f));
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) != 0 ? 0.12f : -0.12f, z = (i & 2) != 0 ? 0.12f : -0.12f;
                mb.Box(new Vector3(x, 0f, z), new Vector3(0.035f, 0.36f, 0.035f));
            }
            if (lit)
            {
                mb.Color = new Color(1f, 0.84f, 0.55f);
                mb.Emission = 1f;
            }
            else mb.Color = Paint.Hex("#e6d6b4");
            mb.Box(Vector3.zero, new Vector3(0.24f, 0.33f, 0.24f));
            mb.Emission = 0f;
            return mb.ToMesh(lit ? "lv_waymarker_lantern_lit" : "lv_waymarker_lantern_dark");
        }

        // ------------------------------------------------------------------ stone style (indoors)

        /// <summary>
        /// Two dressed-stone posts at z = ±half (the road runs along X between them): a plinth, a shaft of three stacked
        /// courses (each a touch narrower and turned a little: hand-laid), a capital and an iron fire bowl on three short
        /// legs (the fire itself is the post's "lantern"), and a chiselled arrow pointing out of the map (−X) on the near
        /// post.
        /// </summary>
        static Mesh BuildStonePostsMesh(float half)
        {
            var mb = new MeshBuilder(29) { Jitter = 0.06f, AOStrength = 0.35f, AOHeight = 0.7f };
            float[] hs = { 0.62f, 0.56f, 0.5f };
            for (int s = -1; s <= 1; s += 2)
            {
                var b = new Vector3(0f, 0f, s * half);
                mb.Color = AshlarDark;
                mb.BoxOn(b, new Vector3(0.66f, 0.22f, 0.66f));
                float y = 0.22f;
                for (int c = 0; c < hs.Length; c++)
                {
                    float w = 0.5f - c * 0.05f;
                    mb.Color = Paint.Shade(Ashlar, 0.93f + 0.05f * c + (s < 0 ? 0.03f : 0f));
                    mb.Push().Translate(b + new Vector3(0f, y, 0f)).Rotate(0f, (c - 1) * 5f * s, 0f);
                    mb.BoxOn(Vector3.zero, new Vector3(w, hs[c] - 0.03f, w));
                    mb.Pop();
                    y += hs[c];
                }
                // capital, three iron legs and the bowl (flame point at StoneLamp)
                mb.Color = AshlarDark;
                mb.BoxOn(b + new Vector3(0f, y, 0f), new Vector3(0.54f, 0.1f, 0.54f));
                y += 0.1f;
                mb.Color = Iron;
                for (int l = 0; l < 3; l++)
                {
                    float a = (l * 120f + 30f) * Mathf.Deg2Rad;
                    mb.Segment(b + new Vector3(Mathf.Cos(a) * 0.17f, y, Mathf.Sin(a) * 0.17f), b + new Vector3(Mathf.Cos(a) * 0.12f, y + 0.12f, Mathf.Sin(a) * 0.12f), 0.024f, 0.024f, 4);
                }
                mb.Push().Translate(b + new Vector3(0f, y + 0.08f, 0f));
                mb.Lathe(new[] { new Vector2(0.08f, 0f), new Vector2(0.24f, 0.09f), new Vector2(0.28f, 0.17f), new Vector2(0.24f, 0.16f) }, 8, false, true, false);
                mb.Pop();
            }
            // the chiselled arrow on the near post's face, pointing out of the map (−X)
            var near = new Vector3(-0.05f, 1.08f, -half - 0.235f);
            mb.Color = Paint.Hex("#ddd3c6");
            mb.Box(near + new Vector3(0.04f, 0f, 0f), new Vector3(0.26f, 0.06f, 0.02f));
            mb.Push().Translate(near + new Vector3(-0.1f, 0f, 0f)).Rotate(0f, 0f, 45f);
            mb.Box(Vector3.zero, new Vector3(0.1f, 0.1f, 0.02f));
            mb.Pop();
            return mb.ToMesh("lv_waymarker_posts_stone");
        }

        /// <summary>A dressed-stone arch (legs at ±1.5 m), a keystone over the lintel and the lantern's iron hook.</summary>
        static Mesh BuildStoneArchMesh()
        {
            var mb = new MeshBuilder(31) { Jitter = 0.06f, AOStrength = 0.35f, AOHeight = 0.7f };
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = AshlarDark;
                mb.BoxOn(new Vector3(s * 1.5f, 0f, 0f), new Vector3(0.66f, 0.26f, 0.6f));
                mb.Color = Ashlar;
                mb.TaperedBox(new Vector3(s * 1.5f, 0.26f, 0f), new Vector3(0.48f, 2.3f, 0.44f), 0.86f);
            }
            mb.Color = Paint.Shade(Ashlar, 1.06f);
            mb.Box(new Vector3(0f, 2.72f, 0f), new Vector3(3.9f, 0.36f, 0.52f));
            mb.Color = AshlarDark;
            mb.Box(new Vector3(0f, 2.98f, 0f), new Vector3(0.5f, 0.3f, 0.56f));
            mb.Color = Iron;
            mb.Segment(new Vector3(0f, 2.54f, 0f), new Vector3(0f, 2.22f, 0f), 0.02f, 0.02f, 4);
            return mb.ToMesh("lv_waymarker_arch_stone");
        }

        /// <summary>A fire bowl's flame (lit) or its dark coals, centred on the flame point.</summary>
        static Mesh BuildFireMesh(bool lit)
        {
            var mb = new MeshBuilder(19) { Jitter = 0.05f };
            if (lit)
            {
                mb.Emission = 1f;
                mb.Color = new Color(1f, 0.6f, 0.26f);
                mb.Cone(new Vector3(0f, -0.14f, 0f), 0.19f, 0.42f, 6);
                mb.Color = new Color(1f, 0.86f, 0.5f);
                mb.Cone(new Vector3(0.02f, -0.13f, -0.03f), 0.11f, 0.3f, 5);
                mb.Emission = 0f;
            }
            else
            {
                mb.Color = new Color(0.22f, 0.19f, 0.19f);
                mb.Blob(new Vector3(0f, -0.12f, 0f), new Vector3(0.2f, 0.06f, 0.2f), 1, 0.2f, 3, 0.8f);
            }
            return mb.ToMesh(lit ? "lv_waymarker_fire_lit" : "lv_waymarker_fire_dark");
        }
    }
}
