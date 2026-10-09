// The quest marker over an NPC's head (Docs/ThreeD.md "Quest markers", Docs/Expansion.md §3): a chunky low-poly "!"
// or "?" with an ink outline and a warm glow, yellow #ffd23a (available / ready to turn in) or grey (later / in
// progress). Main-story quests use the same glyph 15 % larger inside a thin gold ring. The glyph bobs gently and turns
// to face the camera (Animate). GameFlow.QuestMarkers attaches one to each NPC view; the preview tool draws them with
// `map … --markers`. Preview-safe: only UnityEngine, Rendering3D and Core types.
using System.Collections.Generic;
using Lanternvale.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    public sealed class QuestMarker3D
    {
        /// <summary>Glyph height in metres (the "!" / "?" without ring).</summary>
        public const float Size = 0.56f;
        /// <summary>Gap between the top of the head and the bottom of the glyph.</summary>
        public const float HeadGap = 0.34f;
        /// <summary>Main-story quests: the glyph is this much larger.</summary>
        public const float MainScale = 1.15f;
        public const float BobAmplitude = 0.06f, BobHz = 1.6f;

        public static readonly Color Yellow = new Color(1f, 0.824f, 0.227f);    // #ffd23a
        public static readonly Color Grey = new Color(0.64f, 0.64f, 0.66f);
        public static readonly Color RingGold = new Color(0.91f, 0.69f, 0.29f); // #e8b04a

        /// <summary>The marker object (child of the holder): position and rotation are set by Place / Animate.</summary>
        public readonly Transform Root;
        readonly MeshFilter filter;
        readonly MeshRenderer renderer;
        readonly Materials3D.Look look = new Materials3D.Look { FogScale = 0.3f, OutlineWidth = 2.6f, WindScale = 0f };
        Vector3 anchor;
        float phase;

        public QuestMarker Kind { get; private set; } = QuestMarker.None;
        public bool Main { get; private set; }
        public bool Visible => Root != null && Root.gameObject.activeSelf;

        QuestMarker3D(Transform parent, string name)
        {
            var go = new GameObject(name);
            Root = go.transform;
            Root.SetParent(parent, false);
            var glyph = new GameObject("Glyph");
            glyph.transform.SetParent(Root, false);
            filter = glyph.AddComponent<MeshFilter>();
            renderer = glyph.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = Materials3D.WithOutline();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            look.Apply(renderer);
            go.SetActive(false);
        }

        /// <summary>A hidden marker under parent (an NPC view's transform). Phase desynchronises the bob of neighbours.</summary>
        public static QuestMarker3D Create(Transform parent, float phase = 0f)
        {
            var m = new QuestMarker3D(parent, "Quest Marker") { phase = phase };
            return m;
        }

        /// <summary>Shows the glyph for a marker kind (None hides it).</summary>
        public void Set(QuestMarker kind, bool main)
        {
            if (Root == null) return;
            if (kind != Kind || main != Main || filter.sharedMesh == null)
            {
                Kind = kind;
                Main = main;
                filter.sharedMesh = kind == QuestMarker.None ? null : MeshFor(kind, main);
                Root.localScale = Vector3.one * (main ? MainScale : 1f);
            }
            SetVisible(kind != QuestMarker.None);
        }

        public void SetVisible(bool on)
        {
            if (Root == null) return;
            on &= Kind != QuestMarker.None;
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
        }

        /// <summary>Base position in the parent's space: headHeight metres up (World3D: up = −Z), plus HeadGap.</summary>
        public void Place(float headHeight)
        {
            anchor = World3D.At(0f, 0f, headHeight + HeadGap);
            if (Root != null) Root.localPosition = anchor;
        }

        /// <summary>
        /// Per frame: bob (± BobAmplitude at BobHz), a slight sway, and face the camera — the glyph's readable side
        /// (local −Z) turned to cameraYaw and leaned back a little towards the camera pitch. scale: extra size (zoom).
        /// </summary>
        public void Animate(float time, float cameraYaw, float cameraPitch = 40f, float scale = 1f)
        {
            if (Root == null || !Root.gameObject.activeSelf) return;
            float t = time + phase;
            float bob = Mathf.Sin(t * BobHz * 2f * Mathf.PI) * BobAmplitude;
            Root.localPosition = anchor + World3D.Up * bob;
            float sway = Mathf.Sin(t * 0.9f) * 6f;
            Root.rotation = World3D.Yaw(cameraYaw + sway) * Quaternion.Euler(Mathf.Clamp(cameraPitch, 0f, 70f) * 0.45f, 0f, 0f);
            Root.localScale = Vector3.one * ((Main ? MainScale : 1f) * Mathf.Max(0.1f, scale));
        }

        public void Dispose()
        {
            if (Root != null) Object.Destroy(Root.gameObject);
        }

        // ------------------------------------------------------------------ meshes (model space: Y up, readable from −Z)

        static readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();

        /// <summary>The cached glyph mesh of a kind ("!" / "?", yellow / grey, with the gold ring for main quests).</summary>
        public static Mesh MeshFor(QuestMarker kind, bool main)
        {
            if (kind == QuestMarker.None) return null;
            int key = (int)kind * 2 + (main ? 1 : 0);
            if (meshes.TryGetValue(key, out var m) && m != null) return m;
            bool question = kind == QuestMarker.ReadyToTurnIn || kind == QuestMarker.InProgress;
            bool yellow = kind == QuestMarker.ReadyToTurnIn || kind == QuestMarker.Available;
            var mb = new MeshBuilder(11) { Jitter = 0.04f };
            mb.Color = yellow ? Yellow : Grey;
            mb.Emission = yellow ? 0.8f : 0.3f;
            if (question) BuildQuestion(mb);
            else BuildExclamation(mb);
            if (main)
            {
                mb.Color = RingGold;
                mb.Emission = yellow ? 0.6f : 0.25f;
                mb.Push().Translate(0f, Size * 0.52f, 0.02f).Rotate(90f, 0f, 0f);
                mb.Torus(Vector3.zero, Size * 0.62f, 0.022f, 22, 4);
                mb.Pop();
            }
            mb.Emission = 0f;
            m = mb.ToMesh("lv_quest_marker_" + kind + (main ? "_main" : ""));
            meshes[key] = m;
            return m;
        }

        const float Depth = 0.11f;

        static void BuildExclamation(MeshBuilder mb)
        {
            // a bar tapering towards the bottom, and the dot
            Ribbon(mb, new[] { new Vector2(0f, Size), new Vector2(0f, 0.19f) }, new[] { 0.085f, 0.05f });
            Dot(mb, new Vector2(0f, 0.07f), 0.068f);
        }

        static void BuildQuestion(MeshBuilder mb)
        {
            // the hook: an arc over the top, then down into a short stem, then the dot
            var pts = new List<Vector2>();
            var widths = new List<float>();
            var c = new Vector2(0f, 0.395f);
            const float r = 0.118f;
            for (int i = 0; i <= 7; i++)
            {
                float a = Mathf.Lerp(168f, -48f, i / 7f) * Mathf.Deg2Rad;
                pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                widths.Add(i == 0 ? 0.052f : 0.058f);
            }
            pts.Add(new Vector2(0.012f, 0.255f));
            widths.Add(0.054f);
            pts.Add(new Vector2(0f, 0.19f));
            widths.Add(0.05f);
            Ribbon(mb, pts, widths);
            Dot(mb, new Vector2(0f, 0.07f), 0.066f);
        }

        /// <summary>A chunky octagonal dot (prism) centred at p.</summary>
        static void Dot(MeshBuilder mb, Vector2 p, float radius)
        {
            var ring = new List<Vector2>(8);
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / 8f;
                ring.Add(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            float zf = -Depth * 0.5f, zb = Depth * 0.5f;
            var center = new Vector3(p.x, p.y, 0f);
            for (int i = 0; i < 8; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % 8];
                mb.TriangleFacing(new Vector3(p.x, p.y, zf), new Vector3(a.x, a.y, zf), new Vector3(b.x, b.y, zf), Vector3.back);
                mb.TriangleFacing(new Vector3(p.x, p.y, zb), new Vector3(a.x, a.y, zb), new Vector3(b.x, b.y, zb), Vector3.forward);
                var mid = (a + b) * 0.5f;
                mb.Quad(new Vector3(a.x, a.y, zf), new Vector3(b.x, b.y, zf), new Vector3(b.x, b.y, zb), new Vector3(a.x, a.y, zb),
                        new Vector3(mid.x, mid.y, 0f) - center);
            }
        }

        /// <summary>A flat stroke of the given half widths along a polyline in the XY plane, extruded Depth along Z.</summary>
        static void Ribbon(MeshBuilder mb, IList<Vector2> pts, IList<float> halfWidths)
        {
            int n = pts.Count;
            var left = new Vector2[n];
            var right = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var d0 = i > 0 ? (pts[i] - pts[i - 1]).normalized : (pts[1] - pts[0]).normalized;
                var d1 = i < n - 1 ? (pts[i + 1] - pts[i]).normalized : d0;
                var dir = (d0 + d1).sqrMagnitude > 1e-6f ? (d0 + d1).normalized : d1;
                var nrm = new Vector2(-dir.y, dir.x);
                // keep the stroke width through bends (miter, limited)
                float k = 1f / Mathf.Max(0.6f, Vector2.Dot(nrm, new Vector2(-d1.y, d1.x)));
                left[i] = pts[i] + nrm * halfWidths[i] * k;
                right[i] = pts[i] - nrm * halfWidths[i] * k;
            }
            float zf = -Depth * 0.5f, zb = Depth * 0.5f;
            Vector3 F(Vector2 p) => new Vector3(p.x, p.y, zf);
            Vector3 B(Vector2 p) => new Vector3(p.x, p.y, zb);
            for (int i = 0; i < n - 1; i++)
            {
                mb.Quad(F(left[i]), F(right[i]), F(right[i + 1]), F(left[i + 1]), Vector3.back);
                mb.Quad(B(left[i]), B(right[i]), B(right[i + 1]), B(left[i + 1]), Vector3.forward);
                var seg = pts[i + 1] - pts[i];
                var side = new Vector3(-seg.y, seg.x, 0f);
                mb.Quad(F(left[i]), F(left[i + 1]), B(left[i + 1]), B(left[i]), side);
                mb.Quad(F(right[i]), F(right[i + 1]), B(right[i + 1]), B(right[i]), -side);
            }
            var s = pts[0] - pts[1];
            mb.Quad(F(left[0]), F(right[0]), B(right[0]), B(left[0]), new Vector3(s.x, s.y, 0f));
            var e = pts[n - 1] - pts[n - 2];
            mb.Quad(F(left[n - 1]), F(right[n - 1]), B(right[n - 1]), B(left[n - 1]), new Vector3(e.x, e.y, 0f));
        }
    }
}
