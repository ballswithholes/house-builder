// 三国志II 霸王的大陆 · 战场的文化外观（网页版 js/battle-view.js 的 BattleLook 的移植，第二版 DESIGN-V2 §4G）
//
// 战场所在城池（setup.target，攻守双方都在此城下交战）的文化决定城墙、城门、本阵的样式与地表色调。
// 汉地（han）及未知文化保持原样（中式门楼、金顶本阵、温带草地）。只改装饰几何与顶点色（每场战斗构建一次），
// 不改地形、规则或随机数序列。
//
// 公开接口
//   BattleLook.Of(culture) → Look { culture, style, ground }      style：roman persia arab tarim kushan korea wa stilt celt german steppe han
//   BattleLook.CultureOf(setup)                                     战场城池的文化（CultureArt.CultureOfCity）
//   BattleLook.Wall / Gate / Keep(mb, style, cx, h, cz, …) → bool  写入装饰网格；false = 该样式没有专门造型（调用方画汉式）
//   BattleLook.Palm(mb, x, y, z, s, seed)                          椰枣 / 椰树
// 坐标：Unity 坐标（网页版 V() 即此约定），几何参数与网页版逐一对应。
using System.Collections.Generic;
using UnityEngine;

namespace Sanguo
{
    public static class BattleLook
    {
        // 地表色调
        public sealed class Ground
        {
            public Color plain, forest, hill, hillBlob, mountain0, mountain1, leaf, tuft, surLeaf, under;
            public Color sur0, sur1;
            public bool snow;
            public float flowers, palm;
        }
        public sealed class Look { public string culture, style; public Ground ground; }

        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static Color Sh(Color c, float k) { return Art.Shade(c, k); }
        const float T = BattleView.T;

        static readonly Dictionary<string, string> STYLE = new Dictionary<string, string>
        {
            { "roman", "roman" }, { "persia", "persia" }, { "arab", "arab" }, { "tarim", "tarim" }, { "kushan", "kushan" }, { "korea", "korea" }, { "wa", "wa" },
            { "seasia", "stilt" }, { "yi", "stilt" }, { "nanman", "stilt" }, { "celt", "celt" }, { "german", "german" }, { "steppe", "steppe" }, { "sarmatian", "steppe" },
        };
        static readonly Dictionary<string, string> GROUND_OF = new Dictionary<string, string>
        {
            { "persia", "arid" }, { "arab", "arid" }, { "tarim", "arid" }, { "kushan", "arid" }, { "steppe", "steppe" }, { "sarmatian", "steppe" },
            { "seasia", "tropic" }, { "yi", "tropic" }, { "nanman", "tropic" },
        };
        static Dictionary<string, Ground> grounds;
        public static Ground GroundOf(string kind)
        {
            if (grounds == null)
            {
                grounds = new Dictionary<string, Ground>
                {
                    // temperate 与第一版完全相同
                    { "temperate", new Ground { plain = C(0.47f, 0.68f, 0.36f), forest = C(0.33f, 0.55f, 0.3f), hill = C(0.6f, 0.62f, 0.38f), hillBlob = C(0.55f, 0.6f, 0.35f),
                        mountain0 = C(0.55f, 0.53f, 0.5f), mountain1 = C(0.5f, 0.48f, 0.45f), snow = true, leaf = C(0.25f, 0.5f, 0.27f), tuft = C(0.34f, 0.56f, 0.25f), flowers = 0.35f,
                        sur0 = C(0.42f, 0.6f, 0.32f), sur1 = C(0.55f, 0.55f, 0.45f), surLeaf = C(0.24f, 0.46f, 0.26f), under = C(0.17f, 0.2f, 0.12f), palm = 0 } },
                    // 沙漠 / 绿洲：沙土地、赭色丘陵、椰枣林
                    { "arid", new Ground { plain = C(0.8f, 0.7f, 0.5f), forest = C(0.66f, 0.62f, 0.42f), hill = C(0.76f, 0.6f, 0.42f), hillBlob = C(0.72f, 0.56f, 0.38f),
                        mountain0 = C(0.64f, 0.52f, 0.42f), mountain1 = C(0.58f, 0.47f, 0.38f), snow = false, leaf = C(0.3f, 0.5f, 0.26f), tuft = C(0.62f, 0.58f, 0.34f), flowers = 0.06f,
                        sur0 = C(0.78f, 0.66f, 0.47f), sur1 = C(0.68f, 0.56f, 0.42f), surLeaf = C(0.32f, 0.48f, 0.26f), under = C(0.3f, 0.24f, 0.16f), palm = 1 } },
                    // 草原：枯黄的草地
                    { "steppe", new Ground { plain = C(0.62f, 0.67f, 0.4f), forest = C(0.42f, 0.55f, 0.32f), hill = C(0.66f, 0.64f, 0.42f), hillBlob = C(0.6f, 0.6f, 0.38f),
                        mountain0 = C(0.55f, 0.53f, 0.5f), mountain1 = C(0.5f, 0.48f, 0.45f), snow = true, leaf = C(0.3f, 0.48f, 0.28f), tuft = C(0.58f, 0.58f, 0.32f), flowers = 0.2f,
                        sur0 = C(0.58f, 0.62f, 0.38f), sur1 = C(0.6f, 0.58f, 0.46f), surLeaf = C(0.28f, 0.44f, 0.26f), under = C(0.2f, 0.2f, 0.12f), palm = 0 } },
                    // 南方湿热：深绿，林中夹椰树
                    { "tropic", new Ground { plain = C(0.4f, 0.66f, 0.32f), forest = C(0.27f, 0.52f, 0.26f), hill = C(0.52f, 0.62f, 0.34f), hillBlob = C(0.45f, 0.6f, 0.3f),
                        mountain0 = C(0.5f, 0.52f, 0.46f), mountain1 = C(0.46f, 0.48f, 0.42f), snow = false, leaf = C(0.2f, 0.48f, 0.22f), tuft = C(0.3f, 0.56f, 0.22f), flowers = 0.4f,
                        sur0 = C(0.36f, 0.58f, 0.28f), sur1 = C(0.48f, 0.55f, 0.4f), surLeaf = C(0.2f, 0.44f, 0.22f), under = C(0.15f, 0.2f, 0.1f), palm = 0.4f } },
                };
            }
            Ground g;
            return kind != null && grounds.TryGetValue(kind, out g) ? g : grounds["temperate"];
        }

        public static string CultureOf(BattleSetup setup)
        {
            var city = setup != null ? setup.target : null;
            if (city == null) return "han";
            try { var c = CultureArt.CultureOfCity(city); return string.IsNullOrEmpty(c) ? "han" : c; }
            catch (System.Exception) { return string.IsNullOrEmpty(city.culture) ? "han" : city.culture; }
        }
        public static Look Of(string culture)
        {
            if (string.IsNullOrEmpty(culture)) culture = "han";
            string st, gk;
            if (!STYLE.TryGetValue(culture, out st)) st = "han";
            if (!GROUND_OF.TryGetValue(culture, out gk)) gk = "temperate";
            return new Look { culture = culture, style = st, ground = GroundOf(gk) };
        }

        // ---- 基本形体（与 CultureArt 同一约定：凸多边形按外向 hint 定正反） --
        static void Face(MeshBuilder mb, Vector3[] pts, Color c, Vector3 hint)
        {
            var n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
            if (Vector3.Dot(n, hint) < 0) { pts = (Vector3[])pts.Clone(); System.Array.Reverse(pts); }
            for (int i = 1; i + 1 < pts.Length; i++) mb.Tri(pts[0], pts[i], pts[i + 1], c);
        }
        static void Sheet(MeshBuilder mb, Vector3[] pts, Color c)
        {
            for (int i = 1; i + 1 < pts.Length; i++) { mb.Tri(pts[0], pts[i], pts[i + 1], c); mb.Tri(pts[0], pts[i + 1], pts[i], c); }
        }
        // 双坡顶：中心 (x, y, z)，sx × sz 的底，脊高 h；alongZ = 脊沿 z（山墙朝南北，即朝镜头）
        static void Gable(MeshBuilder mb, float x, float y, float z, float sx, float sz, float h, Color c, bool alongZ)
        {
            float hx = sx / 2, hz = sz / 2;
            if (alongZ)
            {
                Vector3 r0 = V(x, y + h, z - hz), r1 = V(x, y + h, z + hz);
                Face(mb, new[] { V(x - hx, y, z - hz), V(x - hx, y, z + hz), r1, r0 }, Sh(c, -0.1f), V(-1, 0.6f, 0));
                Face(mb, new[] { V(x + hx, y, z - hz), r0, r1, V(x + hx, y, z + hz) }, c, V(1, 0.6f, 0));
                Face(mb, new[] { V(x - hx, y, z - hz), r0, V(x + hx, y, z - hz) }, Sh(c, -0.2f), V(0, 0, -1));
                Face(mb, new[] { V(x - hx, y, z + hz), V(x + hx, y, z + hz), r1 }, Sh(c, -0.2f), V(0, 0, 1));
            }
            else
            {
                Vector3 r0 = V(x - hx, y + h, z), r1 = V(x + hx, y + h, z);
                Face(mb, new[] { V(x - hx, y, z - hz), V(x + hx, y, z - hz), r1, r0 }, c, V(0, 0.6f, -1));
                Face(mb, new[] { V(x - hx, y, z + hz), r0, r1, V(x + hx, y, z + hz) }, Sh(c, -0.12f), V(0, 0.6f, 1));
                Face(mb, new[] { V(x - hx, y, z - hz), r0, V(x - hx, y, z + hz) }, Sh(c, -0.22f), V(-1, 0, 0));
                Face(mb, new[] { V(x + hx, y, z - hz), V(x + hx, y, z + hz), r1 }, Sh(c, -0.22f), V(1, 0, 0));
            }
        }
        static readonly float[,] DomeRings = { { 1, 0 }, { 0.92f, 0.38f }, { 0.7f, 0.72f }, { 0.38f, 0.93f } };
        static void Dome(MeshBuilder mb, float x, float y, float z, float r, Color c)
        {
            for (int i = 0; i < DomeRings.GetLength(0) - 1; i++)
            {
                float r0 = DomeRings[i, 0], y0 = DomeRings[i, 1], r1 = DomeRings[i + 1, 0], y1 = DomeRings[i + 1, 1];
                mb.Cylinder(V(x, y + y0 * r, z), r0 * r, r1 * r, (y1 - y0) * r, 10, Sh(c, i * 0.04f), false);
            }
            mb.Cone(V(x, y + 0.93f * r, z), 0.38f * r, 0.07f * r + 0.02f, 10, Sh(c, 0.12f));
        }
        public static void Palm(MeshBuilder mb, float x, float y, float z, float s, int seed)
        {
            var rnd = new SeededRandom(seed);
            float h = s * (0.75f + (float)rnd.NextDouble() * 0.3f);
            mb.Cylinder(V(x, y, z), 0.035f * s, 0.025f * s, h, 4, C(0.54f, 0.42f, 0.27f), false);
            var top = V(x, y + h, z); var leaf = C(0.25f, 0.48f, 0.2f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3 + (float)rnd.NextDouble() * 0.4f, ca = Mathf.Cos(a), sa = Mathf.Sin(a), L = 0.34f * s;
                Sheet(mb, new[] { top, V(x + ca * L * 0.5f - sa * 0.06f * s, y + h + 0.05f * s, z + sa * L * 0.5f + ca * 0.06f * s),
                    V(x + ca * L, y + h - 0.13f * s, z + sa * L), V(x + ca * L * 0.5f + sa * 0.06f * s, y + h + 0.05f * s, z + sa * L * 0.5f - ca * 0.06f * s) }, Sh(leaf, (i % 2) * 0.08f));
            }
        }
        // 尖木桩
        static void Stake(MeshBuilder mb, float x, float y, float z, float h, Color c)
        {
            mb.Cylinder(V(x, y, z), 0.06f, 0.06f, h, 5, c, false);
            mb.Cone(V(x, y + h, z), 0.06f, 0.16f, 5, Sh(c, 0.06f));
        }
        // 阶梯垛口（西亚土坯城）
        static void StepMerlon(MeshBuilder mb, float x, float y, float z, float s, Color c)
        {
            mb.Box(V(x, y + 0.11f * s, z), V(0.46f * s, 0.22f * s, 0.46f * s), Sh(c, 0.04f));
            mb.Box(V(x, y + 0.3f * s, z), V(0.26f * s, 0.18f * s, 0.26f * s), Sh(c, 0.06f));
        }
        // 马尾纛
        static void Standard(MeshBuilder mb, float x, float y, float z, float h, Color c)
        {
            mb.Box(V(x, y + h / 2, z), V(0.05f, h, 0.05f), C(0.36f, 0.26f, 0.15f));
            mb.Cone(V(x, y + h, z), 0.05f, 0.16f, 5, C(0.85f, 0.7f, 0.3f));
            mb.Cylinder(V(x, y + h - 0.55f, z), 0.16f, 0.03f, 0.5f, 6, c, false);
        }
        static void Yurt(MeshBuilder mb, float x, float y, float z, float r, Color felt, Color? crown)
        {
            mb.Cylinder(V(x, y, z), r, r, r * 0.7f, 10, felt, false);
            mb.Cylinder(V(x, y + r * 0.7f, z), r * 1.04f, r * 0.18f, r * 0.45f, 10, Sh(felt, -0.08f), true);
            if (crown.HasValue) mb.Cone(V(x, y + r * 1.15f, z), r * 0.2f, r * 0.3f, 8, crown.Value);
            mb.Box(V(x, y + r * 0.27f, z - r * 0.98f), V(r * 0.38f, r * 0.54f, 0.04f), C(0.66f, 0.26f, 0.16f));   // 门（朝南，朝镜头）
        }

        // 各样式的配色
        sealed class Pal { public Color wall, roof, marble, door, dome, trim, timber, wood, thatch, grass, wattle, felt, gold, hair; }
        static readonly Dictionary<string, Pal> COL = new Dictionary<string, Pal>();
        static Pal Col(string style)
        {
            Pal k;
            if (COL.TryGetValue(style, out k)) return k;
            switch (style)
            {
                case "roman": k = new Pal { wall = C(0.82f, 0.78f, 0.68f), roof = C(0.74f, 0.33f, 0.21f), marble = C(0.93f, 0.91f, 0.86f), door = C(0.38f, 0.24f, 0.14f) }; break;
                case "persia": k = new Pal { wall = C(0.78f, 0.64f, 0.46f), dome = C(0.84f, 0.78f, 0.66f), door = C(0.3f, 0.2f, 0.13f), trim = C(0.24f, 0.5f, 0.56f) }; break;
                case "arab": k = new Pal { wall = C(0.87f, 0.8f, 0.63f), dome = C(0.95f, 0.93f, 0.88f), door = C(0.34f, 0.22f, 0.13f), trim = C(0.72f, 0.56f, 0.3f) }; break;
                case "tarim": k = new Pal { wall = C(0.74f, 0.62f, 0.47f), dome = C(0.8f, 0.7f, 0.53f), door = C(0.32f, 0.22f, 0.14f), trim = C(0.6f, 0.48f, 0.34f) }; break;
                case "kushan": k = new Pal { wall = C(0.7f, 0.47f, 0.36f), dome = C(0.94f, 0.92f, 0.86f), door = C(0.32f, 0.2f, 0.12f), trim = C(0.88f, 0.7f, 0.3f) }; break;
                case "korea": k = new Pal { wall = C(0.6f, 0.6f, 0.57f), roof = C(0.25f, 0.26f, 0.29f), timber = C(0.56f, 0.24f, 0.18f), door = C(0.3f, 0.2f, 0.14f) }; break;
                case "wa": k = new Pal { wall = C(0.5f, 0.42f, 0.3f), wood = C(0.52f, 0.38f, 0.24f), thatch = C(0.66f, 0.56f, 0.36f), grass = C(0.42f, 0.56f, 0.3f) }; break;
                case "stilt": k = new Pal { wall = C(0.48f, 0.4f, 0.28f), wood = C(0.5f, 0.38f, 0.24f), thatch = C(0.5f, 0.4f, 0.24f), grass = C(0.36f, 0.56f, 0.26f) }; break;
                case "celt": k = new Pal { wall = C(0.46f, 0.42f, 0.3f), wood = C(0.48f, 0.35f, 0.22f), thatch = C(0.68f, 0.58f, 0.36f), grass = C(0.4f, 0.56f, 0.3f), wattle = C(0.64f, 0.55f, 0.4f) }; break;
                case "german": k = new Pal { wall = C(0.46f, 0.42f, 0.3f), wood = C(0.45f, 0.33f, 0.21f), thatch = C(0.62f, 0.54f, 0.34f), grass = C(0.4f, 0.56f, 0.3f) }; break;
                case "steppe": k = new Pal { wall = C(0.6f, 0.54f, 0.38f), wood = C(0.5f, 0.38f, 0.24f), felt = C(0.92f, 0.9f, 0.84f), gold = C(0.9f, 0.72f, 0.3f), hair = C(0.16f, 0.13f, 0.12f), grass = C(0.56f, 0.6f, 0.34f) }; break;
                default: k = new Pal(); break;
            }
            COL[style] = k;
            return k;
        }
        static bool Mud(string s) { return s == "persia" || s == "arab" || s == "tarim" || s == "kushan"; }
        static bool Timber(string s) { return s == "wa" || s == "stilt" || s == "celt" || s == "german" || s == "steppe"; }

        // 城墙格：顶面在 h + 0.2（与原模型同高，部队站位不变）。runX / runZ：城墙沿哪个方向延续；outX / outZ：城外方向（±1 / 0）
        public static bool Wall(MeshBuilder mb, string style, float cx, float h, float cz, bool runX, bool runZ, int outX, int outZ)
        {
            var k = Col(style);
            if (style == "roman")
            {
                mb.Box(V(cx, h - 0.6f, cz), V(T, 1.6f, T), k.wall);
                mb.Box(V(cx, h - 0.75f, cz), V(T + 0.04f, 0.08f, T + 0.04f), Sh(k.wall, -0.12f));   // 腰线
                float[,] corners = { { -0.6f, -0.6f }, { 0.6f, -0.6f }, { -0.6f, 0.6f }, { 0.6f, 0.6f } };
                for (int i = 0; i < 4; i++) mb.Box(V(cx + corners[i, 0], h + 0.36f, cz + corners[i, 1]), V(0.34f, 0.32f, 0.34f), Sh(k.wall, 0.05f));
            }
            else if (Mud(style))
            {
                mb.Box(V(cx, h - 0.6f, cz), V(T, 1.6f, T), k.wall);
                for (int s = -1; s <= 1; s += 2) StepMerlon(mb, cx + s * 0.55f, h + 0.2f, cz + s * 0.55f, 1, k.wall);
            }
            else if (style == "korea")
            {
                // 不规则石块的山城墙 + 黑瓦压顶的女墙
                mb.Box(V(cx, h - 0.6f, cz), V(T, 1.6f, T), k.wall);
                mb.Box(V(cx - 0.4f, h - 0.95f, cz - 0.02f), V(0.9f, 0.35f, T + 0.03f), Sh(k.wall, -0.08f));
                mb.Box(V(cx + 0.45f, h - 0.35f, cz + 0.02f), V(0.8f, 0.3f, T + 0.03f), Sh(k.wall, 0.06f));
                for (int s = -1; s <= 1; s += 2)
                {
                    mb.Box(V(cx + s * 0.55f, h + 0.36f, cz + s * 0.55f), V(0.48f, 0.32f, 0.48f), Sh(k.wall, 0.04f));
                    mb.Box(V(cx + s * 0.55f, h + 0.56f, cz + s * 0.55f), V(0.58f, 0.08f, 0.58f), k.roof);
                }
            }
            else if (Timber(style))
            {
                // 夯土垣 + 外沿一道尖木栅（沿城墙走向，立在朝城外的一侧，不挡站在墙上的部队）
                mb.Box(V(cx, h - 0.6f, cz), V(T, 1.6f, T), k.wall);
                mb.Box(V(cx, h - 1.1f, cz), V(T + 0.03f, 0.5f, T + 0.03f), k.grass);   // 墙脚草皮
                const int n = 6; const float sh = 0.5f;
                if (runX || !runZ) { float z = cz + (outZ != 0 ? outZ : -1) * 0.8f; for (int i = 0; i < n; i++) Stake(mb, cx - T / 2 + (i + 0.5f) * T / n, h + 0.2f, z, sh + (i % 2) * 0.08f, k.wood); }
                if (runZ) { float x = cx + (outX != 0 ? outX : -1) * 0.8f; for (int i = 0; i < n; i++) Stake(mb, x, h + 0.2f, cz - T / 2 + (i + 0.5f) * T / n, sh + (i % 2) * 0.08f, k.wood); }
            }
            else return false;
            return true;
        }

        // 城门格：门洞沿 x 摆放、正面朝镜头
        public static bool Gate(MeshBuilder mb, string style, float cx, float h, float cz)
        {
            var k = Col(style);
            if (style == "roman")
            {
                // 两座圆塔夹拱门
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = cx + s * 0.72f;
                    mb.Cylinder(V(x, h - 0.2f, cz), 0.42f, 0.42f, 2.0f, 10, k.wall, false);
                    mb.Cylinder(V(x, h + 1.8f, cz), 0.47f, 0.47f, 0.14f, 10, Sh(k.wall, 0.05f), true);
                    for (int i = 0; i < 4; i++) { float a = i * Mathf.PI / 2 + Mathf.PI / 4; mb.Box(V(x + Mathf.Cos(a) * 0.36f, h + 2.04f, cz + Mathf.Sin(a) * 0.36f), V(0.17f, 0.2f, 0.17f), Sh(k.wall, 0.05f)); }
                }
                mb.Box(V(cx, h + 1.38f, cz), V(1.05f, 0.56f, 0.62f), k.wall);
                mb.Box(V(cx, h + 1.08f, cz), V(0.9f, 0.06f, 0.64f), Sh(k.wall, -0.15f));
                mb.Box(V(cx, h + 0.55f, cz), V(0.86f, 1.05f, 0.3f), k.door);
                return true;
            }
            if (Mud(style))
            {
                // 伊万式门楼：两座（arab 为圆）塔楼 + 门上的高拱框，顶上阶梯垛口
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = cx + s * 0.74f;
                    if (style == "arab")
                    {
                        mb.Cylinder(V(x, h - 0.2f, cz), 0.4f, 0.36f, 2.0f, 8, k.wall, true);
                        mb.Box(V(x, h + 1.84f, cz), V(0.5f, 0.08f, 0.5f), k.trim);
                    }
                    else
                    {
                        mb.Box(V(x, h + 0.75f, cz), V(0.6f, 1.9f, 0.78f), k.wall);
                        if (style != "tarim") StepMerlon(mb, x, h + 1.7f, cz, 0.85f, k.wall);
                    }
                }
                mb.Box(V(cx, h + 1.25f, cz), V(0.95f, 0.9f, 0.64f), Sh(k.wall, 0.03f));
                mb.Box(V(cx, h + 1.25f, cz - 0.33f), V(0.62f, 0.62f, 0.04f), k.trim);   // 拱框
                mb.Box(V(cx, h + 0.55f, cz), V(0.82f, 1.1f, 0.34f), k.door);
                if (style != "tarim") for (int i = -1; i <= 1; i++) StepMerlon(mb, cx + i * 0.3f, h + 1.7f, cz, 0.55f, k.wall);
                return true;
            }
            if (style == "korea")
            {
                // 石砌门台 + 小门楼（红木柱、黑瓦）
                mb.Box(V(cx, h + 0.35f, cz), V(T * 0.96f, 1.1f, 0.8f), k.wall);
                mb.Box(V(cx, h + 0.36f, cz), V(0.7f, 0.86f, 0.84f), k.door);
                mb.Box(V(cx, h + 1.15f, cz), V(1.25f, 0.5f, 0.5f), k.timber);
                mb.ChineseRoof(V(cx, h + 1.38f, cz), 1.9f, 1.0f, 0.62f, k.roof);
                return true;
            }
            if (style == "wa" || style == "stilt")
            {
                // 木门：两柱两横梁，上覆茅草双坡顶（南海为鞍形翘脊）
                for (int s = -1; s <= 1; s += 2) mb.Box(V(cx + s * 0.62f, h + 0.8f, cz), V(0.16f, 1.6f, 0.16f), k.wood);
                mb.Box(V(cx, h + 1.5f, cz), V(1.7f, 0.12f, 0.16f), Sh(k.wood, 0.05f));
                mb.Box(V(cx, h + 1.22f, cz), V(1.4f, 0.09f, 0.12f), k.wood);
                mb.Box(V(cx, h + 0.55f, cz), V(1.08f, 1.0f, 0.1f), Sh(k.wood, -0.12f));
                Gable(mb, cx, h + 1.56f, cz, 1.9f, 0.86f, 0.68f, k.thatch, false);
                if (style == "stilt")
                    for (int s = -1; s <= 1; s += 2) Sheet(mb, new[] { V(cx + s * 0.95f, h + 2.24f, cz - 0.05f), V(cx + s * 1.25f, h + 2.5f, cz), V(cx + s * 0.95f, h + 2.24f, cz + 0.05f), V(cx + s * 0.8f, h + 2.18f, cz) }, Sh(k.thatch, -0.1f));
                else
                    for (int s = -1; s <= 1; s += 2) Stake(mb, cx + s * 0.86f, h - 0.1f, cz, 1.0f, k.wood);
                return true;
            }
            if (style == "celt" || style == "german")
            {
                // 木构门塔：两座方木塔 + 门上的走道与护栏
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = cx + s * 0.72f;
                    mb.Box(V(x, h + 0.85f, cz), V(0.5f, 1.9f, 0.56f), k.wood);
                    for (int i = -1; i <= 1; i += 2) Stake(mb, x + i * 0.16f, h + 1.8f, cz, 0.2f, Sh(k.wood, 0.05f));
                }
                mb.Box(V(cx, h + 1.4f, cz), V(1.0f, 0.12f, 0.6f), Sh(k.wood, 0.06f));
                mb.Box(V(cx, h + 1.62f, cz - 0.27f), V(1.0f, 0.3f, 0.05f), k.wood);
                mb.Box(V(cx, h + 0.6f, cz), V(0.94f, 1.1f, 0.1f), Sh(k.wood, -0.12f));
                return true;
            }
            if (style == "steppe")
            {
                // 车阵口：两辆篷车 + 两杆马尾纛
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = cx + s * 0.62f;
                    mb.Box(V(x, h + 0.32f, cz), V(0.56f, 0.3f, 0.9f), k.wood);
                    mb.Box(V(x, h + 0.15f, cz - 0.3f), V(0.6f, 0.3f, 0.06f), Sh(k.wood, -0.2f));
                    mb.Box(V(x, h + 0.15f, cz + 0.3f), V(0.6f, 0.3f, 0.06f), Sh(k.wood, -0.2f));
                    mb.Blob(V(x, h + 0.5f, cz), V(0.3f, 0.32f, 0.46f), k.felt, 3 + s);
                    Standard(mb, x + s * 0.2f, h, cz - 0.42f, 2.0f, k.hair);
                }
                return true;
            }
            return false;
        }

        // 本阵格（城中央）：主体偏北 0.55，部队站在南侧前方；旗帜由调用方照旧插在东北角
        public static bool Keep(MeshBuilder mb, string style, float cx, float h, float cz)
        {
            var k = Col(style);
            float z = cz + 0.55f;
            if (style == "roman")
            {
                // 列柱神庙：台基、前后两排柱、红瓦三角山墙朝南
                mb.Box(V(cx, h + 0.12f, z), V(1.7f, 0.24f, 1.0f), Sh(k.marble, -0.06f));
                mb.Box(V(cx, h + 0.28f, z), V(1.55f, 0.1f, 0.9f), k.marble);
                mb.Box(V(cx, h + 0.7f, z + 0.12f), V(1.0f, 0.75f, 0.5f), Sh(k.marble, -0.04f));
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 2; j++)
                    {
                        float dz = j == 0 ? -0.36f : 0.36f, x = cx - 0.6f + i * 0.4f;
                        mb.Cylinder(V(x, h + 0.33f, z + dz), 0.075f, 0.066f, 0.76f, 6, k.marble, true);
                    }
                mb.Box(V(cx, h + 1.16f, z), V(1.6f, 0.16f, 0.96f), k.marble);
                Gable(mb, cx, h + 1.24f, z, 1.7f, 1.05f, 0.42f, k.roof, true);
                return true;
            }
            if (style == "persia" || style == "arab")
            {
                // 平顶宫殿 + 正面伊万 + 穹顶
                mb.Box(V(cx, h + 0.5f, z + 0.05f), V(1.45f, 0.9f, 0.85f), k.wall);
                mb.Box(V(cx, h + 0.68f, z - 0.38f), V(0.8f, 1.26f, 0.14f), Sh(k.wall, 0.04f));
                mb.Box(V(cx, h + 0.55f, z - 0.455f), V(0.46f, 0.78f, 0.03f), k.door);
                mb.Box(V(cx, h + 1.33f, z - 0.38f), V(0.86f, 0.06f, 0.16f), k.trim);
                mb.Cylinder(V(cx, h + 0.95f, z + 0.12f), 0.42f, 0.42f, 0.14f, 10, Sh(k.wall, 0.04f), false);
                Dome(mb, cx, h + 1.09f, z + 0.12f, 0.42f, k.dome);
                for (int s = -1; s <= 1; s += 2) StepMerlon(mb, cx + s * 0.6f, h + 0.95f, z - 0.26f, 0.5f, k.wall);
                if (style == "arab") mb.Cone(V(cx, h + 1.5f, z + 0.12f), 0.04f, 0.22f, 5, k.trim);
                return true;
            }
            if (style == "kushan" || style == "tarim")
            {
                // 窣堵波（佛塔）：方台、鼓座、覆钵、平头、相轮
                var dc = k.dome;
                mb.Box(V(cx, h + 0.15f, z), V(1.3f, 0.3f, 1.1f), k.wall);
                mb.Box(V(cx, h + 0.36f, z), V(1.0f, 0.12f, 0.9f), Sh(k.wall, 0.05f));
                mb.Cylinder(V(cx, h + 0.42f, z), 0.44f, 0.44f, 0.24f, 12, Sh(dc, -0.05f), false);
                Dome(mb, cx, h + 0.66f, z, 0.44f, dc);
                mb.Box(V(cx, h + 1.15f, z), V(0.2f, 0.14f, 0.2f), k.trim);
                mb.Cylinder(V(cx, h + 1.2f, z), 0.025f, 0.025f, 0.55f, 4, k.trim, false);
                for (int i = 0; i < 3; i++) mb.Cylinder(V(cx, h + 1.32f + i * 0.13f, z), 0.15f - i * 0.035f, 0.15f - i * 0.035f, 0.03f, 8, k.trim, true);
                return true;
            }
            if (style == "korea")
            {
                mb.Box(V(cx, h + 0.18f, z), V(1.6f, 0.36f, 0.8f), k.wall);
                mb.Box(V(cx, h + 0.75f, z + 0.05f), V(1.2f, 0.8f, 0.5f), k.timber);
                mb.ChineseRoof(V(cx, h + 1.15f, z + 0.05f), 1.9f, 1.0f, 0.75f, k.roof);
                return true;
            }
            if (style == "wa" || style == "stilt")
            {
                // 高床殿：柱脚、地板、木壁、陡峭的茅草双坡顶（倭：脊端千木交叉；南海：鞍形翘脊）
                float[] dxs = { -0.55f, 0, 0.55f }, dzs = { -0.3f, 0.3f };
                foreach (var dx in dxs) foreach (var dz in dzs) mb.Box(V(cx + dx, h + 0.3f, z + dz), V(0.09f, 0.6f, 0.09f), k.wood);
                mb.Box(V(cx, h + 0.64f, z), V(1.35f, 0.08f, 0.8f), Sh(k.wood, 0.05f));
                mb.Box(V(cx, h + 0.92f, z), V(1.1f, 0.5f, 0.6f), Sh(k.wood, 0.12f));
                Gable(mb, cx, h + 1.15f, z, 1.6f, 1.0f, 0.9f, k.thatch, false);
                for (int s = -1; s <= 1; s += 2)
                {
                    float x = cx + s * 0.8f;
                    if (style == "wa")
                    {
                        Sheet(mb, new[] { V(x, h + 2.02f, z - 0.02f), V(x - 0.02f * s, h + 2.4f, z - 0.22f), V(x + 0.02f * s, h + 2.4f, z - 0.18f), V(x, h + 2.02f, z + 0.02f) }, Sh(k.wood, -0.05f));
                        Sheet(mb, new[] { V(x, h + 2.02f, z + 0.02f), V(x - 0.02f * s, h + 2.4f, z + 0.22f), V(x + 0.02f * s, h + 2.4f, z + 0.18f), V(x, h + 2.02f, z - 0.02f) }, Sh(k.wood, -0.05f));
                    }
                    else Sheet(mb, new[] { V(x, h + 2.05f, z - 0.06f), V(x + s * 0.36f, h + 2.4f, z), V(x, h + 2.05f, z + 0.06f), V(x - s * 0.15f, h + 1.99f, z) }, Sh(k.thatch, -0.1f));
                }
                return true;
            }
            if (style == "celt")
            {
                // 圆形茅屋
                mb.Cylinder(V(cx, h, z), 0.62f, 0.62f, 0.55f, 12, k.wattle, false);
                mb.Cylinder(V(cx, h + 0.5f, z), 0.8f, 0.06f, 0.9f, 12, k.thatch, false);
                mb.Box(V(cx, h + 0.24f, z - 0.6f), V(0.3f, 0.48f, 0.06f), Sh(k.wood, -0.1f));
                return true;
            }
            if (style == "german")
            {
                // 长屋大厅
                mb.Box(V(cx, h + 0.35f, z), V(1.6f, 0.7f, 0.8f), k.wood);
                Gable(mb, cx, h + 0.7f, z, 1.75f, 0.95f, 0.7f, k.thatch, false);
                mb.Box(V(cx, h + 0.3f, z - 0.41f), V(0.3f, 0.55f, 0.04f), Sh(k.wood, -0.18f));
                return true;
            }
            if (style == "steppe")
            {
                // 金顶大帐 + 两杆马尾纛
                Yurt(mb, cx, h, z, 0.66f, k.felt, k.gold);
                for (int s = -1; s <= 1; s += 2) Standard(mb, cx + s * 0.7f, h, z - 0.4f, 1.8f, k.hair);
                return true;
            }
            return false;
        }
    }
}
