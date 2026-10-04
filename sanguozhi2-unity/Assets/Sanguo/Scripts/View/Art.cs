using System.Collections.Generic;
using UnityEngine;

namespace Sanguo
{
    // 材质、程序化纹理与低多边形网格工具
    public static class Art
    {
        static Material lowPoly, water, unlit, additive, sky;
        static Texture2D softDot;

        public static Shader LoadShader(string name)
        {
            var s = Resources.Load<Shader>("Shaders/" + name);
            if (s == null) s = Shader.Find("Sanguo/" + name);
            if (s == null) { Debug.LogWarning("Missing shader " + name); s = Shader.Find("Diffuse"); }
            return s;
        }
        public static Material LowPoly { get { if (lowPoly == null) lowPoly = new Material(LoadShader("LowPoly")) { enableInstancing = true }; return lowPoly; } }
        public static Material NewLowPoly() { return new Material(LowPoly); }
        public static Material Water { get { if (water == null) water = new Material(LoadShader("Water")); return water; } }
        public static Material Sky { get { if (sky == null) sky = new Material(LoadShader("Sky")); return sky; } }
        public static Material NewUnlit(Color c, Texture tex = null)
        {
            if (unlit == null) unlit = new Material(LoadShader("Unlit"));
            var m = new Material(unlit) { color = c };
            if (tex != null) m.mainTexture = tex;
            return m;
        }
        public static Material NewAdditive(Color c)
        {
            if (additive == null) { additive = new Material(LoadShader("Additive")); additive.mainTexture = SoftDot; }
            return new Material(additive) { color = c };
        }

        public static Texture2D SoftDot
        {
            get
            {
                if (softDot != null) return softDot;
                int n = 64;
                softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = Mathf.Clamp01(1 - d); a = a * a;
                        softDot.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                softDot.Apply();
                return softDot;
            }
        }

        // 圆环纹理（选择光圈）
        static Texture2D ring;
        public static Texture2D Ring
        {
            get
            {
                if (ring != null) return ring;
                int n = 128;
                ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = Mathf.Clamp01(1 - Mathf.Abs(d - 0.82f) / 0.12f);
                        a = a * a + Mathf.Clamp01(0.8f - d) * 0.18f;
                        ring.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                ring.Apply();
                return ring;
            }
        }

        public static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c; }
        public static Color Shade(Color c, float k) { return k >= 0 ? Color.Lerp(c, Color.white, k) : Color.Lerp(c, Color.black, -k); }

        public static GameObject MakeObject(string name, Mesh mesh, Material mat, Transform parent, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }
    }

    // 平面着色网格构建器：每个三角形独立顶点 + 面法线 + 顶点色
    public class MeshBuilder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Color> c = new List<Color>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t = new List<int>();
        public Matrix4x4 M = Matrix4x4.identity;
        public bool Smooth;

        public int Count { get { return v.Count; } }

        public void Tri(Vector3 a, Vector3 b, Vector3 cc, Color col)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); cc = M.MultiplyPoint3x4(cc);
            var nn = Vector3.Cross(b - a, cc - a).normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(cc);
            n.Add(nn); n.Add(nn); n.Add(nn);
            c.Add(col); c.Add(col); c.Add(col);
            uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.up);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col) { Tri(a, b, cc, col); Tri(a, cc, d, col); }

        // 带 UV 的平面四边形（用于透明贴图面片）
        public void FlatQuad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col)
        {
            a = M.MultiplyPoint3x4(a); b = M.MultiplyPoint3x4(b); cc = M.MultiplyPoint3x4(cc); d = M.MultiplyPoint3x4(d);
            int i = v.Count;
            var nn = Vector3.Cross(b - a, cc - a).normalized;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            for (int k = 0; k < 4; k++) { n.Add(nn); c.Add(col); }
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        public void Box(Vector3 center, Vector3 size, Color col, float topShade = 0.08f)
        {
            var h = size * 0.5f;
            Vector3 p0 = center + new Vector3(-h.x, -h.y, -h.z), p1 = center + new Vector3(h.x, -h.y, -h.z), p2 = center + new Vector3(h.x, -h.y, h.z), p3 = center + new Vector3(-h.x, -h.y, h.z);
            Vector3 q0 = center + new Vector3(-h.x, h.y, -h.z), q1 = center + new Vector3(h.x, h.y, -h.z), q2 = center + new Vector3(h.x, h.y, h.z), q3 = center + new Vector3(-h.x, h.y, h.z);
            var top = Art.Shade(col, topShade);
            Quad(q0, q3, q2, q1, top);
            Quad(p0, q0, q1, p1, col);
            Quad(p1, q1, q2, p2, col);
            Quad(p2, q2, q3, p3, col);
            Quad(p3, q3, q0, p0, col);
            Quad(p0, p1, p2, p3, Art.Shade(col, -0.3f));
        }

        // 棱柱 / 圆柱（低段数）
        public void Cylinder(Vector3 bottom, float r0, float r1, float h, int seg, Color col, bool cap = true)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var b0 = bottom + d0 * r0; var b1 = bottom + d1 * r0;
                var t0 = bottom + Vector3.up * h + d0 * r1; var t1 = bottom + Vector3.up * h + d1 * r1;
                if (r1 > 0.0001f) Quad(b0, t0, t1, b1, col); else Tri(b0, t0, b1, col);
                if (cap && r1 > 0.0001f) Tri(bottom + Vector3.up * h, t1, t0, Art.Shade(col, 0.08f));
            }
        }
        public void Cone(Vector3 bottom, float r, float h, int seg, Color col) { Cylinder(bottom, r, 0, h, seg, col, false); }

        // 低多边形球（八面体细分一次）
        public void Blob(Vector3 center, Vector3 radius, Color col, int seed = 0)
        {
            var pts = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            int[,] faces = { { 0, 4, 3 }, { 0, 3, 5 }, { 0, 5, 2 }, { 0, 2, 4 }, { 1, 3, 4 }, { 1, 5, 3 }, { 1, 2, 5 }, { 1, 4, 2 } };
            var rnd = new System.Random(seed);
            for (int f = 0; f < 8; f++)
            {
                Vector3 a = pts[faces[f, 0]], b = pts[faces[f, 1]], cc = pts[faces[f, 2]];
                Vector3 ab = (a + b).normalized, bc = (b + cc).normalized, ca = (cc + a).normalized;
                var tris = new[] { new[] { a, ab, ca }, new[] { ab, b, bc }, new[] { ca, bc, cc }, new[] { ab, bc, ca } };
                foreach (var tr in tris)
                {
                    var k = 0.95f + (float)rnd.NextDouble() * 0.1f;
                    Tri(center + Vector3.Scale(tr[0], radius) * k, center + Vector3.Scale(tr[1], radius) * k, center + Vector3.Scale(tr[2], radius) * k, Art.Shade(col, (tr[0].y + tr[1].y + tr[2].y) * 0.04f));
                }
            }
        }

        // 中式屋顶：四坡顶，檐角上翘
        public void ChineseRoof(Vector3 baseCenter, float w, float d, float h, Color col)
        {
            float ew = w * 0.5f, ed = d * 0.5f, cw = w * 0.32f, cd = d * 0.3f, ey = h * 0.1f, cy = h * 0.36f;
            Vector3 e0 = baseCenter + new Vector3(-ew, ey, -ed), e1 = baseCenter + new Vector3(ew, ey, -ed), e2 = baseCenter + new Vector3(ew, ey, ed), e3 = baseCenter + new Vector3(-ew, ey, ed);
            Vector3 c0 = baseCenter + new Vector3(-cw, cy, -cd), c1 = baseCenter + new Vector3(cw, cy, -cd), c2 = baseCenter + new Vector3(cw, cy, cd), c3 = baseCenter + new Vector3(-cw, cy, cd);
            Vector3 r0 = baseCenter + new Vector3(-w * 0.22f, h, 0), r1 = baseCenter + new Vector3(w * 0.22f, h, 0);
            var dark = Art.Shade(col, -0.15f);
            // 檐口（上翘的四角）
            Quad(e0, c0, c1, e1, dark); Quad(e1, c1, c2, e2, dark); Quad(e2, c2, c3, e3, dark); Quad(e3, c3, c0, e0, dark);
            // 屋面
            Quad(c0, r0, r1, c1, col); Quad(c2, r1, r0, c3, col);
            Tri(c1, r1, c2, Art.Shade(col, -0.08f)); Tri(c3, r0, c0, Art.Shade(col, -0.08f));
            // 正脊
            Box((r0 + r1) * 0.5f + Vector3.up * 0.02f, new Vector3(w * 0.55f, h * 0.08f, d * 0.06f), Art.Shade(col, -0.4f));
        }

        public void Flag(Vector3 pole, float h, float fw, float fh, Color col)
        {
            Box(pole + Vector3.up * h * 0.5f, new Vector3(0.04f, h, 0.04f), new Color(0.3f, 0.22f, 0.12f));
            var top = pole + Vector3.up * h;
            var a = top + new Vector3(0.02f, 0, 0); var b = top + new Vector3(fw, -fh * 0.1f, 0);
            var cc = top + new Vector3(fw, -fh * 1.1f, 0); var d = top + new Vector3(0.02f, -fh, 0);
            Quad(a, b, cc, d, col); Quad(a, d, cc, b, col);
        }

        public Mesh ToMesh(string name = "mesh")
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    // 常用模型
    public static class Models
    {
        public static void Tree(MeshBuilder mb, Vector3 p, float s, Color leaf, bool pine, int seed)
        {
            mb.Cylinder(p, 0.06f * s, 0.05f * s, 0.35f * s, 4, new Color(0.38f, 0.27f, 0.16f), false);
            if (pine)
            {
                mb.Cone(p + Vector3.up * 0.25f * s, 0.42f * s, 0.7f * s, 6, leaf);
                mb.Cone(p + Vector3.up * 0.6f * s, 0.32f * s, 0.6f * s, 6, Art.Shade(leaf, 0.08f));
            }
            else mb.Blob(p + Vector3.up * 0.65f * s, new Vector3(0.42f, 0.4f, 0.42f) * s, leaf, seed);
        }

        // 城池：城墙、角楼、主殿、旗帜。size 约为占地半径。
        public static Mesh City(float size, Color banner, bool capital)
        {
            var mb = new MeshBuilder();
            var stone = new Color(0.72f, 0.69f, 0.62f);
            var wallH = 0.5f * size;
            float r = size;
            mb.Box(new Vector3(0, 0.05f, 0), new Vector3(r * 2.3f, 0.1f, r * 2.3f), new Color(0.62f, 0.58f, 0.48f));
            // 城墙
            float t = 0.22f * size;
            mb.Box(new Vector3(0, wallH / 2, -r), new Vector3(r * 2, wallH, t), stone);
            mb.Box(new Vector3(0, wallH / 2, r), new Vector3(r * 2, wallH, t), stone);
            mb.Box(new Vector3(-r, wallH / 2, 0), new Vector3(t, wallH, r * 2), stone);
            mb.Box(new Vector3(r, wallH / 2, 0), new Vector3(t, wallH, r * 2), stone);
            // 垛口
            for (int i = -3; i <= 3; i++)
            {
                float x = i * r / 3.5f;
                mb.Box(new Vector3(x, wallH + 0.06f * size, -r), new Vector3(0.14f * size, 0.12f * size, t * 1.05f), Art.Shade(stone, 0.06f));
                mb.Box(new Vector3(x, wallH + 0.06f * size, r), new Vector3(0.14f * size, 0.12f * size, t * 1.05f), Art.Shade(stone, 0.06f));
            }
            // 城门楼
            mb.Box(new Vector3(0, wallH + 0.22f * size, -r), new Vector3(0.7f * size, 0.36f * size, 0.36f * size), new Color(0.62f, 0.2f, 0.16f));
            mb.ChineseRoof(new Vector3(0, wallH + 0.4f * size, -r), 1.0f * size, 0.6f * size, 0.32f * size, new Color(0.22f, 0.26f, 0.34f));
            mb.Box(new Vector3(0, 0.2f * size, -r - 0.01f), new Vector3(0.3f * size, 0.4f * size, t * 1.1f), new Color(0.18f, 0.12f, 0.08f));
            // 角楼
            foreach (var cx in new[] { -1, 1 })
                foreach (var cz in new[] { -1, 1 })
                {
                    var p = new Vector3(cx * r, 0, cz * r);
                    mb.Box(p + Vector3.up * wallH * 0.65f, new Vector3(t * 1.8f, wallH * 1.3f, t * 1.8f), Art.Shade(stone, -0.05f));
                    mb.ChineseRoof(p + Vector3.up * wallH * 1.3f, t * 2.6f, t * 2.6f, 0.22f * size, new Color(0.24f, 0.28f, 0.36f));
                }
            // 城内建筑
            var hall = new Color(0.66f, 0.22f, 0.18f);
            mb.Box(new Vector3(0, 0.25f * size, 0.15f * size), new Vector3(0.9f * size, 0.5f * size, 0.6f * size), hall);
            mb.ChineseRoof(new Vector3(0, 0.5f * size, 0.15f * size), 1.3f * size, 0.95f * size, 0.42f * size, capital ? new Color(0.85f, 0.65f, 0.2f) : new Color(0.25f, 0.3f, 0.38f));
            if (capital)
            {
                mb.Box(new Vector3(0, 0.82f * size, 0.15f * size), new Vector3(0.6f * size, 0.3f * size, 0.4f * size), hall);
                mb.ChineseRoof(new Vector3(0, 0.97f * size, 0.15f * size), 0.9f * size, 0.65f * size, 0.34f * size, new Color(0.85f, 0.65f, 0.2f));
            }
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3((i % 2 == 0 ? -0.55f : 0.55f) * size, 0, (i < 2 ? -0.4f : 0.6f) * size);
                mb.Box(p + Vector3.up * 0.14f * size, new Vector3(0.36f * size, 0.28f * size, 0.3f * size), new Color(0.86f, 0.8f, 0.68f));
                mb.ChineseRoof(p + Vector3.up * 0.28f * size, 0.5f * size, 0.42f * size, 0.18f * size, new Color(0.3f, 0.32f, 0.36f));
            }
            return mb.ToMesh("city");
        }

        // 一名士兵（约 0.5 高）
        public static void Soldier(MeshBuilder mb, Vector3 p, Color team, bool spear, float s = 1f)
        {
            var skin = new Color(0.95f, 0.8f, 0.62f);
            mb.Box(p + new Vector3(-0.045f, 0.09f, 0) * s, new Vector3(0.06f, 0.18f, 0.07f) * s, new Color(0.25f, 0.2f, 0.18f));
            mb.Box(p + new Vector3(0.045f, 0.09f, 0) * s, new Vector3(0.06f, 0.18f, 0.07f) * s, new Color(0.25f, 0.2f, 0.18f));
            mb.Box(p + new Vector3(0, 0.27f, 0) * s, new Vector3(0.18f, 0.2f, 0.11f) * s, team);
            mb.Box(p + new Vector3(0, 0.3f, 0.002f) * s, new Vector3(0.19f, 0.05f, 0.115f) * s, Art.Shade(team, -0.35f));
            mb.Box(p + new Vector3(0, 0.43f, 0) * s, new Vector3(0.11f, 0.11f, 0.11f) * s, skin);
            mb.Cone(p + new Vector3(0, 0.48f, 0) * s, 0.09f * s, 0.07f * s, 6, new Color(0.45f, 0.45f, 0.5f));
            if (spear)
            {
                mb.Box(p + new Vector3(0.12f, 0.38f, 0) * s, new Vector3(0.02f, 0.62f, 0.02f) * s, new Color(0.4f, 0.3f, 0.18f));
                mb.Cone(p + new Vector3(0.12f, 0.69f, 0) * s, 0.025f * s, 0.08f * s, 4, new Color(0.85f, 0.85f, 0.9f));
            }
        }

        // 武将：骑马、披风、军旗
        public static void Commander(MeshBuilder mb, Vector3 p, Color team, float s = 1f)
        {
            var horse = new Color(0.45f, 0.3f, 0.2f);
            mb.Box(p + new Vector3(0, 0.3f, 0) * s, new Vector3(0.18f, 0.18f, 0.46f) * s, horse);
            mb.Box(p + new Vector3(0, 0.44f, 0.24f) * s, new Vector3(0.1f, 0.2f, 0.12f) * s, horse);
            mb.Box(p + new Vector3(0, 0.5f, 0.3f) * s, new Vector3(0.08f, 0.08f, 0.14f) * s, Art.Shade(horse, -0.1f));
            foreach (var lx in new[] { -0.06f, 0.06f }) foreach (var lz in new[] { -0.17f, 0.17f })
                    mb.Box(p + new Vector3(lx, 0.11f, lz) * s, new Vector3(0.05f, 0.22f, 0.05f) * s, Art.Shade(horse, -0.2f));
            mb.Box(p + new Vector3(0, 0.55f, 0) * s, new Vector3(0.2f, 0.22f, 0.13f) * s, team);
            mb.Box(p + new Vector3(0, 0.55f, -0.08f) * s, new Vector3(0.22f, 0.26f, 0.03f) * s, Art.Shade(team, -0.35f));
            mb.Box(p + new Vector3(0, 0.72f, 0) * s, new Vector3(0.12f, 0.12f, 0.12f) * s, new Color(0.95f, 0.8f, 0.62f));
            mb.Cone(p + new Vector3(0, 0.77f, 0) * s, 0.1f * s, 0.1f * s, 6, new Color(0.85f, 0.7f, 0.25f));
            mb.Box(p + new Vector3(0, 0.88f, 0) * s, new Vector3(0.02f, 0.08f, 0.02f) * s, new Color(0.85f, 0.2f, 0.15f));
            mb.Flag(p + new Vector3(-0.16f, 0.4f, -0.1f) * s, 0.9f * s, 0.36f * s, 0.28f * s, team);
        }
    }
}
