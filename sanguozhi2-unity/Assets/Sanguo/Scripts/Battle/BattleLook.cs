// 三国志-霸王的世界 · 战场的文化外观与平滑棋盘（网页版 js/battle-view.js 的 BattleLook 与第三版地形构建的移植，DESIGN-V2 §4G、DESIGN-V3 §1 §2）
//
// 战场所在城池（setup.target，攻守双方都在此城下交战）的文化决定城墙、城门、本阵的样式与地表色调。
// 汉地（han）及未知文化保持原样（中式门楼、金顶本阵、温带草地）。只改装饰几何与顶点色（每场战斗构建一次），
// 不改地形、规则或随机数序列。第三版：天竺用贵霜式的砖城与窣堵波本阵，阿克苏姆 / 库施与毛里用阿拉伯式的石 / 土坯城；
// 表中没有的文化按 Defs.CultureBase 回退。
//
// 公开接口
//   BattleLook.Of(culture) → Look { culture, style, ground, groundKind }  style：roman persia arab tarim kushan korea wa stilt celt german steppe han
//   BattleLook.CultureOf(setup)                                     战场城池的文化（CultureArt.CultureOfCity）
//   BattleLook.Wall / Gate / Keep(mb, style, cx, h, cz, …) → bool  写入装饰网格；false = 该样式没有专门造型（调用方画汉式）
//   BattleLook.Palm(mb, x, y, z, s, seed)                          椰枣 / 椰树
//   BattleBoard（第三版，纯几何、不建 Unity 物体，无头测试可直接调用）：
//     new BattleBoard(model, look, atkCol, defCol) → 地形模型（HeightAt）、平滑地形网格（顶点 / 法线 / 群落底色 / aMat / aBt / 三角形）、
//     裙边、水面、各格站立高度（TileY / GroundY / Tile）、树石草与点缀的实例表（Props）及其模板网格（PropTemplate）、
//     城池（城墙、城门、角楼、本阵、旗帜）与桥的网格（Fort）、登陆的船（Boats）、统计（Stats）、士兵队形（SoldierSlots）。
// 坐标：Unity 坐标（网页版 V() 即此约定），几何参数与网页版逐一对应；网页版的 three 坐标 z 取反即为这里的 z。
// 数值：网页版用 double 计算、Float32Array 存放的量，这里同样用 double 计算、float[] 存放（逐点与网页版一致）。
using System;
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
        public sealed class Look { public string culture, style, groundKind; public Ground ground; }

        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static Color Sh(Color c, float k) { return Art.Shade(c, k); }
        const float T = 2f;   // 每格边长（= BattleView.T）

        static readonly Dictionary<string, string> STYLE = new Dictionary<string, string>
        {
            { "roman", "roman" }, { "persia", "persia" }, { "arab", "arab" }, { "tarim", "tarim" }, { "kushan", "kushan" }, { "korea", "korea" }, { "wa", "wa" },
            { "seasia", "stilt" }, { "yi", "stilt" }, { "nanman", "stilt" }, { "celt", "celt" }, { "german", "german" }, { "steppe", "steppe" }, { "sarmatian", "steppe" },
            // 第三版：天竺用贵霜式的砖城与窣堵波本阵；阿克苏姆 / 库施与毛里用阿拉伯式的石 / 土坯城（没有更贴切的现成城墙）
            { "india", "kushan" }, { "africa", "arab" }, { "moor", "arab" },
        };
        static readonly Dictionary<string, string> GROUND_OF = new Dictionary<string, string>
        {
            { "persia", "arid" }, { "arab", "arid" }, { "tarim", "arid" }, { "kushan", "arid" }, { "steppe", "steppe" }, { "sarmatian", "steppe" },
            { "seasia", "tropic" }, { "yi", "tropic" }, { "nanman", "tropic" },
            { "india", "tropic" }, { "africa", "arid" }, { "moor", "arid" },
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
            catch (Exception) { return string.IsNullOrEmpty(city.culture) ? "han" : city.culture; }
        }
        // 表中没有的文化按 Defs.CultureBase（= 网页版 SG.cultureBase）回退
        public static Look Of(string culture)
        {
            if (string.IsNullOrEmpty(culture)) culture = "han";
            string bs = Defs.CultureBase(culture);
            string st, gk;
            if (!STYLE.TryGetValue(culture, out st) && !STYLE.TryGetValue(bs, out st)) st = "han";
            if (!GROUND_OF.TryGetValue(culture, out gk) && !GROUND_OF.TryGetValue(bs, out gk)) gk = "temperate";
            return new Look { culture = culture, style = st, ground = GroundOf(gk), groundKind = gk };
        }

        // ---- 基本形体（与 CultureArt 同一约定：凸多边形按外向 hint 定正反） --
        static void Face(MeshBuilder mb, Vector3[] pts, Color c, Vector3 hint)
        {
            var n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
            if (Vector3.Dot(n, hint) < 0) { pts = (Vector3[])pts.Clone(); Array.Reverse(pts); }
            for (int i = 1; i + 1 < pts.Length; i++) mb.Tri(pts[0], pts[i], pts[i + 1], c);
        }
        static void Sheet(MeshBuilder mb, Vector3[] pts, Color c)
        {
            for (int i = 1; i + 1 < pts.Length; i++) { mb.Tri(pts[0], pts[i], pts[i + 1], c); mb.Tri(pts[0], pts[i + 1], pts[i], c); }
        }
        // 双坡顶：中心 (x, y, z)，sx × sz 的底，脊高 h；alongZ = 脊沿 z（山墙朝南北，即朝镜头）
        public static void Gable(MeshBuilder mb, float x, float y, float z, float sx, float sz, float h, Color c, bool alongZ)
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
        public static void Stake(MeshBuilder mb, float x, float y, float z, float h, Color c)
        {
            mb.Cylinder(V(x, y, z), 0.06f, 0.06f, h, 5, c, false);
            mb.Cone(V(x, y + h, z), 0.06f, 0.16f, 5, Sh(c, 0.06f));
        }
        // 阶梯垛口（西亚土坯城）
        public static void StepMerlon(MeshBuilder mb, float x, float y, float z, float s, Color c)
        {
            mb.Box(V(x, y + 0.11f * s, z), V(0.46f * s, 0.22f * s, 0.46f * s), Sh(c, 0.04f));
            mb.Box(V(x, y + 0.3f * s, z), V(0.26f * s, 0.18f * s, 0.26f * s), Sh(c, 0.06f));
        }
        // 马尾纛
        public static void Standard(MeshBuilder mb, float x, float y, float z, float h, Color c)
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

        // 各样式的配色（未定义的项 a = 0，见 Has）
        public sealed class Pal { public Color wall, roof, marble, door, dome, trim, timber, wood, thatch, grass, wattle, felt, gold, hair; }
        public static bool Has(Color c) { return c.a > 0; }
        static readonly Dictionary<string, Pal> COL = new Dictionary<string, Pal>();
        public static Pal Col(string style)
        {
            Pal k;
            if (COL.TryGetValue(style ?? "", out k)) return k;
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
            COL[style ?? ""] = k;
            return k;
        }
        public static bool Mud(string s) { return s == "persia" || s == "arab" || s == "tarim" || s == "kushan"; }
        public static bool Timber(string s) { return s == "wa" || s == "stilt" || s == "celt" || s == "german" || s == "steppe"; }

        // 城墙格：顶面在 h + 0.2（与原模型同高，部队站位不变）。runX / runZ：城墙沿哪个方向延续；outX / outZ：城外方向（±1 / 0）
        // （第三版的城池由 BattleBoard.Fort 整体建模，本函数与 Gate 保留供旧版逐格样式使用）
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

    // ==================================================== 第三版：平滑战场 --
    // DESIGN-V3 §1 / §2：地块不再是平涂的方块，而是一张连续的平滑地形——高度取自 BattleTerrain 对世界地图的取样
    // （model.terrain 的 grid；没有时由地块类型合成），按地块类型塑形（山岳为圆润的峰群、山丘为缓丘、河川为河道、水域为湖海），
    // 地形着色器（Sanguo/BattleTerrain）按群落 / 坡度 / 高度混合草地、泥土、农田、沙、岩石、雪，再叠加淡淡的格线与棋盘边框。
    // 水面、圆润的树、石块、草丛为实例化网格；城墙、城门、本阵为平滑细致的模型（砖石材质）。
    // 本类只生成数据（网格数组、实例表、MeshBuilder），Unity 物体由 BattleView 建立。
    public sealed class BattleBoard
    {
        public const float T = 2f;
        public static readonly Vector3 Origin = new Vector3(400, 0, 0);   // Unity 坐标
        public const double BASE_Y = 0.6;              // 平原地面高度（地形着色器在 y < 0.48 处铺沙滩，水边因而是沙岸）
        public const double WATER_Y = 0.12;            // 水面高度
        public const double WADE_Y = WATER_Y - 0.08;   // 河川格中部队站立的高度（涉水）
        public const int SUB_IN = 5, SUB_OUT = 2, SUB_FAR = 1;   // 每格网格分段：棋盘内（含一格外缘）/ 棋盘外 7 格 / 更远
        public const int EXT_X = 16, EXT_Y = 14;       // 棋盘外延伸的格数（远景，与 BattleTerrain 的取样边距一致）
        public const int MID_EXT = 7;
        public const float FIG_S = 0.7f;               // 人物模型（米）→ 战场（世界单位）：骑马的主将约 1.6、步兵约 1.3
        static readonly double[][] BIOME_TINT = { new[] { 0.38, 0.6, 0.3 }, new[] { 0.6, 0.6, 0.37 }, new[] { 0.8, 0.69, 0.47 }, new[] { 0.25, 0.55, 0.24 },
            new[] { 0.6, 0.6, 0.34 }, new[] { 0.5, 0.57, 0.33 }, new[] { 0.3, 0.47, 0.32 }, new[] { 0.55, 0.53, 0.4 } };
        static readonly double[] BIOME_ARID = { 0, 0.35, 1, 0, 0.3, 0.15, 0, 0.1 };   // 各群落的干旱度（草丛、林木的多少）
        static readonly double[] FOREST_TINT = { 0.21, 0.4, 0.21 }, DIRT_TINT = { 0.55, 0.45, 0.32 };
        const int TERRAIN_WATER = 8;                   // BattleTerrain.Water（深水：湖、海）
        static readonly double[] COLD = { 0.85, 0.85, 0.45, 0, 0, 0.6, 1, 1 };       // 各群落山顶积雪的程度（BIOMES 顺序）
        // 平原上的村舍样式（按战场所在城池的文化；表中没有的按 Defs.CultureBase 回退）
        static readonly Dictionary<string, string> HUT_OF = new Dictionary<string, string>
        {
            { "han", "han" }, { "korea", "han" }, { "wa", "han" }, { "roman", "tile" }, { "persia", "flat" }, { "arab", "flat" }, { "africa", "flat" }, { "moor", "flat" },
            { "tarim", "flat" }, { "kushan", "flat" }, { "india", "thatch" }, { "seasia", "stilt" }, { "nanman", "stilt" }, { "yi", "stilt" }, { "celt", "round" },
            { "german", "long" }, { "steppe", "yurt" }, { "sarmatian", "yurt" },
        };
        static readonly double[][] FLOWER_COLS = { new[] { 1, 0.97, 0.88 }, new[] { 1, 0.84, 0.32 }, new[] { 0.98, 0.6, 0.66 }, new[] { 0.76, 0.64, 0.98 }, new[] { 1, 0.5, 0.36 } };

        // ------------------------------------------------------------ 数学（double，与网页版逐点一致）
        static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        static double Lerp(double a, double b, double t) { return a + (b - a) * Clamp01(t); }
        // GLSL 式 smoothstep
        static double Sm(double e0, double e1, double x) { double t = Clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
        public static int JsRound(double v) { return (int)Math.Floor(v + 0.5); }
        static double Max(double a, double b, double c) { return Math.Max(Math.Max(a, b), c); }
        static double Max(double a, double b, double c, double d, double e) { return Math.Max(Math.Max(Math.Max(Math.Max(a, b), c), d), e); }
        static double Min(double a, double b, double c, double d) { return Math.Min(Math.Min(Math.Min(a, b), c), d); }
        static double Perlin(double x, double y) { return WorldGeo.Perlin(x, y); }
        static double Fbm2(double x, double y) { return Perlin(x, y) * 0.6 + Perlin(x * 2.03 + 17.1, y * 2.03 + 5.3) * 0.3 + Perlin(x * 4.1 + 3.7, y * 4.1 + 29.9) * 0.1; }
        // V8 的 Math.hypot（按最大值归一后用 Kahan 补偿求和），与网页版逐位相同
        public static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            double max = Math.Max(a, b);
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            double n = a / max, s = n * n - comp, p = sum + s; comp = (p - sum) - s; sum = p;
            n = b / max; s = n * n - comp; p = sum + s; comp = (p - sum) - s; sum = p;
            return Math.Sqrt(sum) * max;
        }
        public static double Hypot(double a, double b, double c)
        {
            a = Math.Abs(a); b = Math.Abs(b); c = Math.Abs(c);
            double max = Math.Max(Math.Max(a, b), c);
            if (max == 0) return 0;
            double sum = 0, comp = 0;
            foreach (var v in new[] { a, b, c }) { double n = v / max, s = n * n - comp, p = sum + s; comp = (p - sum) - s; sum = p; }
            return Math.Sqrt(sum) * max;
        }
        // Catmull-Rom 权重
        static void Crw(double t, out double w0, out double w1, out double w2, out double w3)
        {
            double t2 = t * t, t3 = t2 * t;
            w0 = (-t3 + 2 * t2 - t) / 2; w1 = (3 * t3 - 5 * t2 + 2) / 2; w2 = (-3 * t3 + 4 * t2 + t) / 2; w3 = (t3 - t2) / 2;
        }
        // 规则栅格（nx × ny，a[j * nx + i]）的 Catmull-Rom 双三次插值 / 双线性插值；fi, fj 为栅格坐标（越界取边）
        static double Bicubic(float[] a, int nx, int ny, double fi, double fj)
        {
            fi = fi < 0 ? 0 : fi > nx - 1 ? nx - 1 : fi; fj = fj < 0 ? 0 : fj > ny - 1 ? ny - 1 : fj;
            int i = Math.Min(nx - 2, (int)Math.Floor(fi)), j = Math.Min(ny - 2, (int)Math.Floor(fj));
            double x0, x1, x2, x3, y0, y1, y2, y3;
            Crw(fi - i, out x0, out x1, out x2, out x3); Crw(fj - j, out y0, out y1, out y2, out y3);
            double s = 0;
            for (int b = 0; b < 4; b++)
            {
                int jj = Math.Max(0, Math.Min(ny - 1, j - 1 + b)) * nx;
                double r = 0;
                r += x0 * a[jj + Math.Max(0, Math.Min(nx - 1, i - 1))];
                r += x1 * a[jj + Math.Max(0, Math.Min(nx - 1, i))];
                r += x2 * a[jj + Math.Max(0, Math.Min(nx - 1, i + 1))];
                r += x3 * a[jj + Math.Max(0, Math.Min(nx - 1, i + 2))];
                s += (b == 0 ? y0 : b == 1 ? y1 : b == 2 ? y2 : y3) * r;
            }
            return s;
        }
        static double Bilinear(float[] a, int nx, int ny, double fi, double fj)
        {
            fi = fi < 0 ? 0 : fi > nx - 1.0001 ? nx - 1.0001 : fi; fj = fj < 0 ? 0 : fj > ny - 1.0001 ? ny - 1.0001 : fj;
            int i = (int)Math.Floor(fi), j = (int)Math.Floor(fj); double tx = fi - i, ty = fj - j; int o = j * nx + i;
            return (a[o] * (1 - tx) + a[o + 1] * tx) * (1 - ty) + (a[o + nx] * (1 - tx) + a[o + nx + 1] * tx) * ty;
        }

        // ------------------------------------------------------------ 输入
        public readonly BattleModel m;
        public readonly BattleLook.Look look;
        public readonly int W, H;
        public readonly Color atkCol, defCol;
        public readonly double lat = double.NaN;      // 防守城池的纬度（树种的干旱改写只在北纬 39° 以南）

        // ------------------------------------------------------------ 地形模型
        public BattleTerrainGrid G;
        public BattleTerrainMap Tr;                   // null：旧版随机地形（或尺寸不符），取样栅格按地块类型合成
        public double h0;
        public int nu, nv;
        public float[] Hs, tgt, relA;
        public float[] mForest, mMountain, mHill, mWet, mPlain, mFort, mShoal;
        public Vector2Int castle;
        public int seed;
        public readonly List<BattleBridge> bridgeList = new List<BattleBridge>();
        readonly HashSet<int> bridgeSet = new HashSet<int>();   // y * W + x
        public bool IsBridge(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H && bridgeSet.Contains(y * W + x); }
        sealed class Bump { public double u, v, r, a; public bool peak; }
        List<Bump>[] bumpAt;
        double Gi(double u) { return (u - G.u0) / G.step; }
        int BiomeRaw(int o) { return o >= 0 && o < G.biome.Length ? G.biome[o] : 0; }   // 网页版越界读到 undefined | 0 = 0
        double Gj(double v) { return (v - G.v0) / G.step; }
        public bool InFort(int x, int y) { return x >= castle.x - 2 && x <= castle.x + 2 && y >= castle.y - 2 && y <= castle.y + 2; }

        // ------------------------------------------------------------ 输出
        public sealed class BoardStats
        {
            public double buildMs;
            public int terrainTris, propTris, fortTris, waterTris, trees, rocks, tufts, dress, boats;
            public string skirt = "";
            public readonly Dictionary<string, double> phases = new Dictionary<string, double>();
        }
        public readonly BoardStats Stats = new BoardStats();
        // 地形网格（非均匀栅格：棋盘内细、棋盘外粗）
        public double[] xs, ys; public int gnx, gny;
        public float[] hh; public byte[] split;
        public Vector3[] tPos, tNrm; public Color[] tCol; public Vector4[] tMat, tBt; public int[] tTri;
        sealed class Seg { public double a, b; public int sub, k0, n; }
        Seg[] segX, segY;
        // 裙边
        public bool seaSkirt; public float skirtY; public Color edgeTint;
        public Vector3[] sPos, sNrm; public Color[] sCol; public Vector4[] sMat, sBt; public int[] sTri;   // sMat / sBt 只在陆地裙边有（海面为 null）
        // 水面
        public readonly List<Vector3> wPos = new List<Vector3>(); public readonly List<Color> wCol = new List<Color>(); public readonly List<int> wTri = new List<int>();
        // 各格站立高度
        public float[] tileY, bridgeY;
        // 实例化的树、石、草（种类 × 近景 / 远景 一批）
        public sealed class PropInst { public double x, y, z, s, yaw, u, v, ax = 1, ay = 1; public float r, g, b; public bool near; }
        public sealed class PropBatch
        {
            public string kind; public bool near, fade, castShadow, receiveShadow;
            public readonly List<PropInst> items = new List<PropInst>();
            public int tris;                           // 模板三角形数 × 实例数
            public Matrix4x4 MatrixOf(int k)
            {
                var e = items[k];
                double sy = e.s * e.ay * (kind == "rock" ? 0.8 + (k % 3) * 0.15 : 1);
                // three 绕 Y 转 yaw ↔ Unity 绕 Y 转 −yaw（度）
                return Matrix4x4.TRS(new Vector3((float)e.x, (float)e.y, (float)e.z), Quaternion.Euler(0, (float)(-e.yaw * 180 / Math.PI), 0),
                    new Vector3((float)(e.s * e.ax), (float)sy, (float)(e.s * e.ax)));
            }
        }
        public readonly List<PropBatch> Props = new List<PropBatch>();
        // 挡住部队时淡去的树（棋盘内的近景树）：按格子索引
        public sealed class FadeItem { public int batch, k; public double x, z, s; public float t, cur; }
        public readonly List<FadeItem> fadeItems = new List<FadeItem>();
        public readonly Dictionary<long, List<FadeItem>> fadeByTile = new Dictionary<long, List<FadeItem>>();
        public static long TileKey(int x, int y) { return ((long)x << 32) ^ (uint)y; }
        // 城池与桥、登陆的船
        public MeshBuilder Fort, Boats;

        public BattleBoard(BattleModel model, BattleLook.Look lk, Color atk, Color def)
        {
            m = model; look = lk ?? BattleLook.Of("han"); W = m.W; H = m.H; atkCol = atk; defCol = def;
            var tg = m.setup != null ? m.setup.target : null;
            if (tg != null) lat = tg.lat;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double tp = 0;
            Action<string> lap = k => { double n = sw.Elapsed.TotalMilliseconds; Stats.phases[k] = Math.Round(n - tp); tp = n; };
            BuildModel(); lap("model");
            BuildTerrainMesh(); lap("terrain");
            BuildTileY();
            BuildWater(); lap("water");
            BuildProps(); lap("props");
            Fort = new MeshBuilder();
            BuildFort(Fort);
            BuildBridges(Fort);
            Stats.fortTris = Fort.t.Count / 3;   // 三角形数（FlatQuad 是带索引的 4 顶点，不能用顶点数 / 3）
            lap("fort");
            Stats.buildMs = sw.Elapsed.TotalMilliseconds;
        }

        // ====================================================== 地形模型 --
        // 没有世界地图取样时（旧版随机地形、测试用的假模型）：按地块类型与噪声合成同样格式的取样栅格
        BattleTerrainGrid SynthGrid(string kind)
        {
            const double step = 0.5;
            int gnu = JsRound((W + 2 * EXT_X) / step) + 1, gnv = JsRound((H + 2 * EXT_Y) / step) + 1, N = gnu * gnv;
            var g = new BattleTerrainGrid
            {
                nu = gnu, nv = gnv, u0 = -EXT_X - 0.5, v0 = -EXT_Y - 0.5, step = step, synth = true,
                h = new float[N], slope = new float[N], water = new byte[N], forest = new float[N], farm = new float[N],
                sand = new float[N], snow = new float[N], biome = new byte[N], road = new float[N], river = new float[N], coast = new float[N],
            };
            for (int o = 0; o < N; o++) { g.road[o] = 999; g.river[o] = 99; }
            int bi = kind == "arid" ? 2 : kind == "steppe" ? 1 : kind == "tropic" ? 3 : 0;
            int tid = m.setup != null && m.setup.target != null ? m.setup.target.id : 0;
            double sd = (tid * 13.37) % 100;
            var ca = m.castle;
            for (int j = 0; j < gnv; j++)
                for (int i = 0; i < gnu; i++)
                {
                    double u = g.u0 + i * step, v = g.v0 + j * step; int o = j * gnu + i;
                    double de = Max(-0.5 - u, u - (W - 0.5), -0.5 - v, v - (H - 0.5), 0);
                    double n = Fbm2(u * 0.11 + sd, v * 0.11 + sd * 0.7);
                    g.h[o] = (float)((n - 0.42) * 1.2 + Math.Max(0, de - 3) * 0.16 * (0.5 + n));
                    g.forest[o] = (float)(Sm(0.55, 0.68, Fbm2(u * 0.16 + 40 + sd, v * 0.16 + 11)) * (bi == 2 ? 0.15 : 1));
                    g.biome[o] = (byte)bi; g.sand[o] = bi == 2 ? 0.7f : 0f;
                    g.farm[o] = bi == 2 ? 0f : (float)(Sm(0.5, 0.62, Fbm2(u * 0.2 + 70, v * 0.2 + sd)) * Sm(9, 3, Hypot(u - ca.x, v - ca.y)));
                }
            // 棋盘边上的河格向外延伸成河（远景里河流不在棋盘边缘戛然而止）
            Action<int, int, int, int> carve = (x, y, dx, dy) =>
            {
                for (int k = 0; k <= EXT_X * 2 + 2; k++)
                {
                    double u = x + dx * k * 0.5, v = y + dy * k * 0.5 + Math.Sin(k * 0.7 + x) * 0.15 * k * 0.1;
                    int i = JsRound((u - g.u0) / step), jj = JsRound((v - g.v0) / step);
                    for (int a = -1; a <= 1; a++)
                        for (int b = -1; b <= 1; b++)
                        {
                            int ii = i + a, j2 = jj + b;
                            if (ii < 0 || j2 < 0 || ii >= gnu || j2 >= gnv) continue;
                            double d = Hypot(a, b) * 0.5;
                            int o = j2 * gnu + ii;
                            g.river[o] = (float)Math.Min(g.river[o], d * 0.9);
                            if (d < 0.3) g.water[o] = 1;
                        }
                }
            };
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    if (m.map[x, y] != Terrain.River) continue;
                    if (y == 0) carve(x, y, 0, -1);
                    if (y == H - 1) carve(x, y, 0, 1);
                    if (x == 0) carve(x, y, -1, 0);
                }
            return g;
        }

        // 战场地形模型：高度函数（格坐标 u, v；格子中心为整数）、取样场、地块遮罩
        void BuildModel()
        {
            var T0 = m.terrain as BattleTerrainMap;
            Tr = T0 != null && T0.grid != null && T0.W == W && T0.H == H ? T0 : null;
            G = Tr != null ? Tr.grid : SynthGrid(look.groundKind);
            h0 = Tr != null ? Tr.h0 : 0;
            nu = G.nu; nv = G.nv;
            castle = m.castle;
            if (Tr != null && Tr.bridges != null)
                foreach (var b in Tr.bridges)
                {
                    int key = b.y * W + b.x;
                    if (bridgeSet.Contains(key)) { for (int i = 0; i < bridgeList.Count; i++) if (bridgeList[i].x == b.x && bridgeList[i].y == b.y) bridgeList[i] = b; }
                    else { bridgeSet.Add(key); bridgeList.Add(b); }
                }
            // 取样场的世界高度（棋盘外的远景）
            Hs = new float[nu * nv];
            for (int o = 0; o < Hs.Length; o++)
            {
                int w = G.water[o]; double ha = G.h[o], rel = ha - h0, rv = G.river[o];
                double y;
                if (w >= 2) y = WATER_Y - 0.42 - Math.Min(1.1, Math.Max(0, -ha) * 0.35);
                else if (w == 1 || rv < 0.36) y = WATER_Y - 0.36;
                else
                {
                    // 相对城池地面的起伏；低地（世界地图的海滩带，绝对高度 < 0.5）保持绝对高度，沙滩只出现在真正的海岸 / 河岸
                    y = BASE_Y + (rel > 0 ? 3.0 * Math.Tanh(rel / 3.0) : Math.Max(rel, -0.1));
                    double habs = G.synth ? ha + 0.9 : ha;   // 合成的栅格没有绝对高度：视同内陆
                    y = Lerp(Math.Max(habs, WATER_Y + 0.02), Math.Max(y, 0.5), Sm(0.3, 0.6, habs));
                    if (rv < 1.1) y = Math.Min(y, Lerp(WATER_Y + 0.1, y, Sm(0.36, 1.1, rv)));
                }
                Hs[o] = (float)y;
            }
            // 地块目标高度与遮罩（格子中心为栅格点）
            int N = W * H;
            tgt = new float[N]; relA = new float[N];
            mForest = new float[N]; mMountain = new float[N]; mHill = new float[N]; mWet = new float[N]; mPlain = new float[N]; mFort = new float[N]; mShoal = new float[N];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    var t = m.map[x, y]; int o = y * W + x;
                    double rel = Bilinear(G.h, nu, nv, Gi(x), Gj(y)) - h0;
                    relA[o] = (float)rel;
                    double h;
                    if (InFort(x, y)) { h = BASE_Y + 0.06; mFort[o] = 1; }
                    else if (IsBridge(x, y)) { h = WATER_Y - 0.4; mWet[o] = 1; }
                    else
                        switch ((int)t)
                        {
                            case (int)Terrain.Mountain: h = BASE_Y + 1.3 + Clamp(rel * 0.15, 0, 0.35); mMountain[o] = 1; break;
                            case (int)Terrain.Hill: h = BASE_Y + 0.32 + Clamp(rel * 0.1, 0, 0.16); mHill[o] = 1; break;
                            // 河川；在湖海里的河川格是浅滩（登陆、涉渡）：水浅、看得见沙底
                            case (int)Terrain.River:
                                {
                                    bool sea = G.water[JsRound(Gj(y)) * nu + JsRound(Gi(x))] >= 2;
                                    h = sea ? WATER_Y - 0.2 : WATER_Y - 0.4; mWet[o] = 1; if (sea) mShoal[o] = 1;
                                    break;
                                }
                            case TERRAIN_WATER: h = WATER_Y - 0.9; mWet[o] = 1; break;
                            case (int)Terrain.Forest: h = BASE_Y + Clamp(rel * 0.45, -0.07, 0.3); mForest[o] = 1; break;
                            default: h = BASE_Y + Clamp(rel * 0.45, -0.07, 0.3); mPlain[o] = 1; break;
                        }
                    tgt[o] = (float)h;
                }
            // 水下的地面连续——深水（湖、海）按离岸距离逐格加深（岸边一格浅、三格外最深），再在水格之间做一次 3×3 平滑，
            // 不再是一格一个台阶的深浅方块；河川格保持原深度
            {
                Func<int, int, bool> wetT = (x, y) => x >= 0 && y >= 0 && x < W && y < H && mWet[y * W + x] > 0;
                var dist = new float[N];
                for (int o = 0; o < N; o++) dist[o] = 99;
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        if (!wetT(x, y)) continue;
                        double best = 99;
                        for (int yy = Math.Max(0, y - 4); yy <= Math.Min(H - 1, y + 4); yy++)
                            for (int xx = Math.Max(0, x - 4); xx <= Math.Min(W - 1, x + 4); xx++)
                                if (!wetT(xx, yy)) best = Math.Min(best, Hypot(xx - x, yy - y));
                        dist[y * W + x] = (float)best;
                    }
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        int o = y * W + x;
                        if ((int)m.map[x, y] == TERRAIN_WATER) tgt[o] = (float)(WATER_Y - 0.38 - 0.52 * Sm(1, 3.2, dist[o]));
                    }
                var t2 = (float[])tgt.Clone();
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        int o = y * W + x;
                        if (!wetT(x, y) || (int)m.map[x, y] != TERRAIN_WATER) continue;
                        double sw = 0, sv = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (!wetT(x + dx, y + dy)) continue;
                                double w = dx != 0 || dy != 0 ? 0.6 : 1;
                                sw += w; sv += w * t2[(y + dy) * W + x + dx];
                            }
                        tgt[o] = (float)(sv / sw);
                    }
            }
            // 山峰与丘顶的隆起：山岳是陡峭的尖峰（主峰 + 副峰），山丘是位置与大小随机的缓丘（相邻的丘连成起伏的岗地）
            int tid = m.setup != null && m.setup.target != null ? m.setup.target.id : 0;
            seed = Tr != null ? unchecked((int)Tr.seed) : unchecked(tid * 7919);
            var rnd = new SeededRandom(seed ^ 0x2b7e1516);
            bumpAt = new List<Bump>[N];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    int o = y * W + x; var L = new List<Bump>();
                    if (mMountain[o] > 0)
                    {
                        // 峰高按世界地图上的相对起伏缩放（中原、两河的低山不再是高耸的雪峰）
                        double ka = G.synth ? 1 : Lerp(0.55, 1, Sm(0.6, 2.2, relA[o]));
                        var b1 = new Bump(); b1.u = x + (rnd.NextDouble() - 0.5) * 0.36; b1.v = y + (rnd.NextDouble() - 0.5) * 0.36; b1.r = 0.92; b1.a = (1.15 + rnd.NextDouble() * 0.55) * ka; b1.peak = true;
                        L.Add(b1);
                        double a = rnd.NextDouble() * Math.PI * 2;
                        var b2 = new Bump(); b2.u = x + Math.Cos(a) * 0.48; b2.v = y + Math.Sin(a) * 0.48; b2.r = 0.58; b2.a = (0.35 + rnd.NextDouble() * 0.35) * ka; b2.peak = true;
                        L.Add(b2);
                    }
                    else if (mHill[o] > 0)
                    {
                        double rr = rnd.NextDouble();
                        var b = new Bump(); b.u = x + (rnd.NextDouble() - 0.5) * 0.5; b.v = y + (rnd.NextDouble() - 0.5) * 0.5; b.r = 0.75 + rr * 0.25; b.a = 0.26 + rnd.NextDouble() * 0.12;
                        L.Add(b);
                    }
                    if (L.Count > 0) bumpAt[o] = L;
                }
        }
        public double MaskAt(float[] a, double u, double v) { return Bilinear(a, W, H, u, v); }

        // 高度函数（格坐标 u, v）
        public double HeightAt(double u, double v)
        {
            double h = Bicubic(Hs, nu, nv, Gi(u), Gj(v));
            double dx = Max(-0.5 - u, u - (W - 0.5), 0), dy = Max(-0.5 - v, v - (H - 0.5), 0), d = Math.Sqrt(dx * dx + dy * dy);
            // 棋盘外的过渡：水面（浅滩 / 深水）只延伸约一格（攻方边的浅滩不再一路伸到画面外）
            double dB = 2.4 - 1.4 * Bilinear(mWet, W, H, u, v);
            if (d < dB)
            {
                // 水岸与山脚附近对取样坐标做域扭曲（±0.3 格），岸线与山体轮廓不再沿着格线走；扭曲量在格子中心为 0（中心的地块类型不变）
                double su = u, sv = v;
                if (d <= 0)
                {
                    double mw = Bilinear(mWet, W, H, u, v), mm = Bilinear(mMountain, W, H, u, v), ms = Bilinear(mShoal, W, H, u, v);
                    double k = Max(4 * mw * (1 - mw), 4 * mm * (1 - mm), 4 * ms * (1 - ms));
                    if (k > 0.01) { su = u + 0.32 * k * (Fbm2(u * 0.8 + 3.1, v * 0.8 + 0.7) - 0.5) * 2; sv = v + 0.32 * k * (Fbm2(u * 0.8 + 0.3, v * 0.8 + 9.2) - 0.5) * 2; }
                }
                double b = Bicubic(tgt, W, H, su, sv);
                int xi = JsRound(u), yi = JsRound(v);
                for (int yy = yi - 1; yy <= yi + 1; yy++)
                {
                    if (yy < 0 || yy >= H) continue;
                    for (int xx = xi - 1; xx <= xi + 1; xx++)
                    {
                        if (xx < 0 || xx >= W) continue;
                        var L = bumpAt[yy * W + xx];
                        if (L == null) continue;
                        for (int k = 0; k < L.Count; k++)
                        {
                            var q = L[k]; double e = ((u - q.u) * (u - q.u) + (v - q.v) * (v - q.v)) / (q.r * q.r);
                            if (e < 1)
                            {
                                if (q.peak) { double dd = Math.Max(0, 1 - Math.Sqrt(e + 0.006)); b += q.a * Math.Pow(dd, 1.25) * (1.12 - 0.12 * dd); }
                                else b += q.a * (1 - e) * (1 - e);
                            }
                        }
                    }
                }
                h = d <= 0 ? b : Lerp(h, b, 1 - Sm(0, dB, d));
            }
            // 丘陵：低频起伏把相邻的丘连成岗地
            if (d < 1)
            {
                double hk = Bilinear(mHill, W, H, u, v);
                if (hk > 0.02) h += hk * (Fbm2(u * 0.35 + 21.7, v * 0.35 + 4.1) - 0.45) * 0.5;
            }
            double amp = 0.05 + 0.24 * Sm(BASE_Y + 0.45, BASE_Y + 1.8, h);
            h += (Fbm2(u * 0.85 + 13.1, v * 0.85 + 7.7) - 0.5) * amp;
            // 山岳：脊状噪声（山脊、沟壑、岩棱）
            if (d < 1.2 && h > BASE_Y + 0.9)
            {
                double mk = Bilinear(mMountain, W, H, u, v);
                if (mk > 0.02) { double r = 1 - Math.Abs(Perlin(u * 1.7 + 5.3, v * 1.7 + 1.9) * 2 - 1); h += mk * (r * r - 0.4) * 0.42 * Sm(BASE_Y + 0.9, BASE_Y + 1.8, h); }
            }
            return h;
        }

        // 非均匀的网格轴：棋盘内（含一格外缘）每格 SUB_IN 段，其外 MID_EXT 格每格 SUB_OUT 段，更远每格 SUB_FAR 段
        static Seg[] AxisSegs(int n, int ext)
        {
            double lo = -ext - 0.5, in0 = -1.5, in1 = n + 0.5, hi = n - 0.5 + ext, m0 = Math.Max(lo, in0 - MID_EXT), m1 = Math.Min(hi, in1 + MID_EXT);
            var raw = new[] { new double[] { lo, m0, SUB_FAR }, new double[] { m0, in0, SUB_OUT }, new double[] { in0, in1, SUB_IN }, new double[] { in1, m1, SUB_OUT }, new double[] { m1, hi, SUB_FAR } };
            var o = new List<Seg>(); int k = 0;
            foreach (var q in raw)
            {
                if (!(q[1] > q[0] + 1e-6)) continue;
                int n1 = JsRound((q[1] - q[0]) * q[2]);
                o.Add(new Seg { a = q[0], b = q[1], sub = (int)q[2], k0 = k, n = n1 });
                k += n1;
            }
            return o.ToArray();
        }
        static double[] TerrainAxis(Seg[] S)
        {
            var a = new List<double>();
            foreach (var q in S) for (int k = 0; k < q.n; k++) a.Add(q.a + (double)k / q.sub);
            a.Add(S[S.Length - 1].b);
            return a.ToArray();
        }
        // 轴上的连续下标（格坐标 → 栅格坐标）
        static double AxisIndex(Seg[] S, double w)
        {
            for (int i = 0; i < S.Length; i++) { var q = S[i]; if (w < q.b || i == S.Length - 1) return q.k0 + (w - q.a) * q.sub; }
            return -1;
        }

        // 平滑地形网格：顶点色 = 群落底色，aMat = (沙, 岩石倾向, 雪, 1 − 农田)，aBt = (山丘, 山岳, 积雪, 0)
        void BuildTerrainMesh()
        {
            segX = AxisSegs(W, EXT_X); segY = AxisSegs(H, EXT_Y);
            xs = TerrainAxis(segX); ys = TerrainAxis(segY);
            int nx = gnx = xs.Length, ny = gny = ys.Length, N = nx * ny;
            tPos = new Vector3[N]; tNrm = new Vector3[N]; tCol = new Color[N]; tMat = new Vector4[N]; tBt = new Vector4[N];
            hh = new float[N];
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) hh[j * nx + i] = (float)HeightAt(xs[i], ys[j]);
            // 网格外缘：最外 3 格逐渐压平到外圈「裙边」的高度（远景不露出网格的断面；裙边为陆地或海面，取外缘多数）
            double uMin = xs[0], uMax = xs[nx - 1], vMin = ys[0], vMax = ys[ny - 1];
            Func<double, double, double> edgeD = (u, v) => Min(u - uMin, uMax - u, v - vMin, vMax - v);
            int wet = 0, tot = 0;
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) if (edgeD(xs[i], ys[j]) < 1.01) { tot++; if (hh[j * nx + i] < WATER_Y) wet++; }
            seaSkirt = wet > tot * 0.5;
            double skY = seaSkirt ? WATER_Y - 0.7 : BASE_Y - 0.05;
            skirtY = (float)skY;
            for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++) { double k = Sm(0, 3, edgeD(xs[i], ys[j])); if (k < 1) hh[j * nx + i] = (float)Lerp(skY, hh[j * nx + i], k); }
            double et0 = 0, et1 = 0, et2 = 0; int edgeN = 0;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int o = j * nx + i; double u = xs[i], v = ys[j], h = hh[o];
                    tPos[o] = new Vector3((float)(Origin.x + (u + 0.5) * T), (float)h, (float)(Origin.z + (v + 0.5) * T));
                    // 法线：栅格差分（非均匀间距）。网页版 three 法线 (−dh/dx, 1, dh/dz) / l → Unity z 取反
                    int i0 = Math.Max(0, i - 1), i1 = Math.Min(nx - 1, i + 1), j0 = Math.Max(0, j - 1), j1 = Math.Min(ny - 1, j + 1);
                    double dhdx = (hh[j * nx + i1] - hh[j * nx + i0]) / ((xs[i1] - xs[i0]) * T), dhdz = (hh[j1 * nx + i] - hh[j0 * nx + i]) / ((ys[j1] - ys[j0]) * T);
                    double nl = Math.Sqrt(dhdx * dhdx + 1 + dhdz * dhdz);
                    tNrm[o] = new Vector3((float)(-dhdx / nl), (float)(1 / nl), (float)(-dhdz / nl));
                    // 群落底色（四角群落色的双线性混合）
                    double fi = Gi(u), fj = Gj(v);
                    int ci = Math.Max(0, Math.Min(nu - 2, (int)Math.Floor(fi))), cj = Math.Max(0, Math.Min(nv - 2, (int)Math.Floor(fj)));
                    double tx = Clamp01(fi - ci), ty = Clamp01(fj - cj);
                    double r = 0, g = 0, bb = 0;
                    for (int q = 0; q < 4; q++)
                    {
                        int a = q & 1, b = q >> 1;
                        double w = a == 0 && b == 0 ? (1 - tx) * (1 - ty) : a == 1 && b == 0 ? tx * (1 - ty) : a == 0 ? (1 - tx) * ty : tx * ty;
                        int bix = G.biome[(cj + b) * nu + ci + a];
                        var bc = bix < BIOME_TINT.Length ? BIOME_TINT[bix] : BIOME_TINT[0];
                        r += bc[0] * w; g += bc[1] * w; bb += bc[2] * w;
                    }
                    bool inB = u > -0.5 && u < W - 0.5 && v > -0.5 && v < H - 0.5;
                    double fo = inB ? MaskAt(mForest, u, v) : Sm(0.3, 0.7, Bilinear(G.forest, nu, nv, fi, fj));
                    double mt = inB ? MaskAt(mMountain, u, v) : 0;
                    double pl = inB ? MaskAt(mPlain, u, v) + MaskAt(mForest, u, v) * 0.3 : 1;
                    // 农田：棋盘外照世界地图；棋盘内保留约 35% 的淡淡田色，棋盘边的过渡 3 格；草原、沙漠群落不画农田
                    double dCa = Hypot(u - castle.x, v - castle.y);
                    double dOut = Max(-0.5 - u, u - (W - 0.5), -0.5 - v, v - (H - 0.5), 0);
                    int biF = G.biome[(int)Clamp(JsRound(fj), 0, nv - 1) * nu + (int)Clamp(JsRound(fi), 0, nu - 1)];
                    double farm = Bilinear(G.farm, nu, nv, fi, fj) * Clamp01(pl) * (1 - fo) * Lerp(Math.Max(0.35, 0.75 * Sm(6.5, 3.2, dCa)), 1, Sm(0, 3, dOut)) * (biF == 1 || biF == 2 ? 0.1 : 1);
                    double road = Tr != null ? 1 - Sm(0.05, 0.12, Bilinear(G.road, nu, nv, fi, fj)) : 0;
                    double fortK = inB ? MaskAt(mFort, u, v) : 0;
                    r += (FOREST_TINT[0] - r) * fo * 0.55; g += (FOREST_TINT[1] - g) * fo * 0.55; bb += (FOREST_TINT[2] - bb) * fo * 0.55;
                    // 山岳：荒芜的灰褐（高处、陡处更明显）；山丘：略带赭黄的草坡
                    double hl = inB ? MaskAt(mHill, u, v) : 0;
                    double bare = Math.Max(mt * 0.75, Sm(BASE_Y + 1.3, BASE_Y + 2.6, h) * 0.6);
                    r += (0.56 - r) * bare; g += (0.52 - g) * bare; bb += (0.42 - bb) * bare;
                    r += (0.58 - r) * hl * 0.25; g += (0.6 - g) * hl * 0.25; bb += (0.34 - bb) * hl * 0.25;
                    double rk = road * (1 - fortK) * (h > WATER_Y + 0.15 ? 1 : 0);
                    r += (DIRT_TINT[0] - r) * rk * 0.8; g += (DIRT_TINT[1] - g) * rk * 0.8; bb += (DIRT_TINT[2] - bb) * rk * 0.8;
                    tCol[o] = new Color((float)r, (float)g, (float)bb, 1);
                    double ed = edgeD(u, v);
                    if (ed < 3.01) { if (ed > 2.5) { et0 += r; et1 += g; et2 += bb; edgeN++; } }
                    double sand = Bilinear(G.sand, nu, nv, fi, fj);
                    int bi = BiomeRaw(JsRound(fj) * nu + JsRound(fi));
                    // 积雪按世界地图的绝对海拔（世界雪线约 6–7.8）：低地战场（中原、两河）的山上不再有雪
                    double gSnow = Bilinear(G.snow, nu, nv, fi, fj);
                    double coldW = G.synth ? 1 : Math.Max(gSnow > 0.05 ? 1 : 0, Sm(3.4, 5.8, Bilinear(G.h, nu, nv, fi, fj)));
                    double cold = (bi == 6 || bi == 7 ? 1 : bi == 0 || bi == 1 ? 0.6 : 0) * coldW;
                    double snow = Math.Max(gSnow, Sm(BASE_Y + 3.0, BASE_Y + 3.7, h) * cold);
                    tMat[o] = new Vector4((float)sand, (float)(mt * 0.5 + Sm(BASE_Y + 1.4, BASE_Y + 2.6, h) * 0.3), (float)(snow * (1 - mt)), (float)(1 - Clamp01(farm * (1 - rk) * (1 - fortK))));
                    // 积雪：山岳上高出本格山基约 1.4–1.85 以上的峰顶（寒冷度按群落）
                    double peakH = inB && mt > 0.01 ? h - Bilinear(tgt, W, H, u, v) : 0;
                    tBt[o] = new Vector4((float)(hl * (1 - rk * 0.6)), (float)mt, (float)((bi < COLD.Length ? COLD[bi] : 0) * coldW * Sm(1.4, 1.85, peakH) * mt), 0);
                }
            // 三角形：沿较短的对角线切分（起伏处更平滑）。网页版 three 的绕序 (a, b, d) → Unity (a, d, b)
            tTri = new int[(nx - 1) * (ny - 1) * 6];
            split = new byte[(nx - 1) * (ny - 1)];
            int qn = 0;
            for (int j = 0; j < ny - 1; j++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
                    if (Math.Abs((double)hh[a] - hh[d]) <= Math.Abs((double)hh[b] - hh[c]))
                    { tTri[qn++] = a; tTri[qn++] = d; tTri[qn++] = b; tTri[qn++] = a; tTri[qn++] = c; tTri[qn++] = d; split[j * (nx - 1) + i] = 1; }
                    else { tTri[qn++] = a; tTri[qn++] = c; tTri[qn++] = b; tTri[qn++] = b; tTri[qn++] = c; tTri[qn++] = d; }
                }
            // 外缘 3 格的顶点色渐变到外缘平均色（与裙边一致）
            if (edgeN > 0) { et0 /= edgeN; et1 /= edgeN; et2 /= edgeN; }
            edgeTint = new Color((float)et0, (float)et1, (float)et2, 1);
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    double k = Sm(0, 3, edgeD(xs[i], ys[j]));
                    if (k >= 1) continue;
                    int o = j * nx + i;
                    var c0 = tCol[o];
                    tCol[o] = new Color((float)Lerp(et0, c0.r, k), (float)Lerp(et1, c0.g, k), (float)Lerp(et2, c0.b, k), 1);
                    var am = tMat[o];
                    tMat[o] = new Vector4((float)(am.x * k), (float)(am.y * k), (float)(am.z * k), (float)Lerp(1, am.w, k));
                }
            Stats.terrainTris = tTri.Length / 3;
            Stats.skirt = seaSkirt ? "sea" : "land";
            // 裙边：网格外的一圈大平面（陆地用同一地形材质、外缘平均色；海面用水面材质）
            {
                double X0 = Origin.x + (uMin + 0.5) * T, X1 = Origin.x + (uMax + 0.5) * T, Z0 = Origin.z + (vMin + 0.5) * T, Z1 = Origin.z + (vMax + 0.5) * T, R = 260;
                float y = (float)(seaSkirt ? WATER_Y : skY);
                var pts = new[] { new[] { X0, Z0 }, new[] { X1, Z0 }, new[] { X1, Z1 }, new[] { X0, Z1 }, new[] { X0 - R, Z0 - R }, new[] { X1 + R, Z0 - R }, new[] { X1 + R, Z1 + R }, new[] { X0 - R, Z1 + R } };
                sPos = new Vector3[8]; sCol = new Color[8]; sNrm = new Vector3[8];
                for (int k = 0; k < 8; k++) { sPos[k] = new Vector3((float)pts[k][0], y, (float)pts[k][1]); sCol[k] = seaSkirt ? new Color(0, 0, 0, 1) : edgeTint; sNrm[k] = Vector3.up; }
                // 陆地裙边用地形材质：aMat = (0, 0, 0, 1)（纯平原）、aBt = 0；海面裙边用水面材质，不带这两项
                sMat = sBt = null;
                if (!seaSkirt)
                {
                    sMat = new Vector4[8]; sBt = new Vector4[8];
                    for (int k = 0; k < 8; k++) { sMat[k] = new Vector4(0, 0, 0, 1); sBt[k] = Vector4.zero; }
                }
                var ix = new List<int>();
                for (int k = 0; k < 4; k++) { int a = k, b = (k + 1) % 4, c = 4 + k, d = 4 + (k + 1) % 4; ix.Add(a); ix.Add(d); ix.Add(c); ix.Add(a); ix.Add(b); ix.Add(d); }
                sTri = ix.ToArray();
            }
        }

        // 渲染出的地表高度（格坐标 u, v）：在网格三角形上插值（与画面完全一致）；网格外退回高度函数
        public double GridH(double u, double v)
        {
            if (hh == null) return HeightAt(u, v);
            double fi = AxisIndex(segX, u), fj = AxisIndex(segY, v);
            if (!(fi >= 0 && fj >= 0 && fi <= gnx - 1 && fj <= gny - 1)) return HeightAt(u, v);
            int i = Math.Min(gnx - 2, (int)Math.Floor(fi)), j = Math.Min(gny - 2, (int)Math.Floor(fj)); double tx = fi - i, ty = fj - j;
            int nx = gnx; double a = hh[j * nx + i], b = hh[j * nx + i + 1], c = hh[(j + 1) * nx + i], d = hh[(j + 1) * nx + i + 1];
            if (split[j * (nx - 1) + i] != 0) return ty <= tx ? a + (b - a) * tx + (d - b) * ty : a + (c - a) * ty + (d - c) * tx;
            return tx + ty <= 1 ? a + (b - a) * tx + (c - a) * ty : d + (c - d) * (1 - tx) + (b - d) * (1 - ty);
        }
        // 地表高度（Unity 世界坐标 x, z → y）；水面以下的地方取涉水高度（部队、士兵站在水里而不是河底）
        public double GroundY(double wx, double wz)
        {
            double u = (wx - Origin.x) / T - 0.5, v = (wz - Origin.z) / T - 0.5;
            int xi = JsRound(u), yi = JsRound(v);
            if (bridgeY != null && xi >= 0 && yi >= 0 && xi < W && yi < H)
            {
                float b = bridgeY[yi * W + xi];
                if (b != 0 && Math.Abs(u - xi) < 0.55 && Math.Abs(v - yi) < 0.55) return b;
            }
            return Math.Max(GridH(u, v), WADE_Y);
        }
        // 格子中心（Unity 世界坐标）：部队站立处（河川为涉水高度、桥为桥面、城墙为墙顶）
        public Vector3 Tile(int x, int y)
        {
            float h = tileY != null ? tileY[Mathf.Clamp(y, 0, H - 1) * W + Mathf.Clamp(x, 0, W - 1)] : 0.08f;
            return Origin + new Vector3(x * T + T / 2, h, y * T + T / 2);
        }
        // 各格的站立高度（取渲染出的地表，与网格三角形一致）
        void BuildTileY()
        {
            tileY = new float[W * H]; bridgeY = new float[W * H];
            foreach (var b in bridgeList) bridgeY[b.y * W + b.x] = (float)(WATER_Y + 0.34);
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    var t = m.map[x, y]; int o = y * W + x;
                    double h = GridH(x, y);
                    if (bridgeY[o] != 0) h = bridgeY[o];
                    else if (t == Terrain.River || (int)t == TERRAIN_WATER) h = WADE_Y;
                    else if (t == Terrain.Wall) h = BASE_Y + 0.06 + 1.45;
                    else if (InFort(x, y)) h = BASE_Y + 0.06;
                    tileY[o] = (float)Math.Max(h, WADE_Y);
                }
        }

        // 水面：取样栅格（每 0.5 格）上，凡有角点低于水面的方格铺一块水面；顶点色 r = 浅水（岸边浪花）
        void BuildWater()
        {
            var hw = new float[nu * nv];
            for (int j = 0; j < nv; j++) for (int i = 0; i < nu; i++) hw[j * nu + i] = (float)GridH(G.u0 + i * G.step, G.v0 + j * G.step);
            var vid = new int[nu * nv];
            for (int o = 0; o < vid.Length; o++) vid[o] = -1;
            Func<int, int> vert = o =>
            {
                if (vid[o] >= 0) return vid[o];
                int i = o % nu, j = o / nu; double u = G.u0 + i * G.step, v = G.v0 + j * G.step;
                wPos.Add(new Vector3((float)(Origin.x + (u + 0.5) * T), (float)WATER_Y, (float)(Origin.z + (v + 0.5) * T)));
                double depth = WATER_Y - hw[o];
                wCol.Add(new Color((float)(1 - Clamp01(depth / 0.9)), 0, 0, 1));
                return vid[o] = wPos.Count - 1;
            };
            for (int j = 0; j < nv - 1; j++)
                for (int i = 0; i < nu - 1; i++)
                {
                    int a = j * nu + i, b = a + 1, c = a + nu, d = c + 1;
                    if (Min(hw[a], hw[b], hw[c], hw[d]) >= WATER_Y + 0.02) continue;
                    int A = vert(a), B = vert(b), Cq = vert(c), D = vert(d);
                    // three (A, B, D) (A, D, C) → Unity 反转
                    wTri.Add(A); wTri.Add(D); wTri.Add(B); wTri.Add(A); wTri.Add(Cq); wTri.Add(D);
                }
            Stats.waterTris = wTri.Count / 3;
        }

        // ====================================================== 树、石、草 --
        // 小件点缀：不随「挡住部队时淡去」处理（本来就矮小）
        static readonly HashSet<string> SMALL_PROP = new HashSet<string> { "rock", "tuft", "bush", "scrub", "hay", "fence", "flowers", "cairn" };
        static readonly Dictionary<string, MeshBuilder> PROP_TPL = new Dictionary<string, MeshBuilder>();
        static Color C(double r, double g, double b) { return new Color((float)r, (float)g, (float)b); }
        static Vector3 V(double x, double y, double z) { return new Vector3((float)x, (float)y, (float)z); }
        static Color Sh(Color c, double k) { return Art.Shade(c, (float)k); }
        static LP P2(double r, double y, bool sharp = false) { return new LP((float)r, (float)y, sharp); }
        // 实例化的模板（MeshBuilder，按种类缓存，各场战斗共用）
        public static MeshBuilder PropTemplate(string kind)
        {
            MeshBuilder mb;
            if (PROP_TPL.TryGetValue(kind, out mb)) return mb;
            mb = new MeshBuilder();
            var bark = C(0.4, 0.29, 0.18);
            switch (kind)
            {
                // 树冠一律平滑明暗（smoothN：法线向未起伏的椭球法线混合，轮廓仍有起伏），近景二级细分、远景（棋盘外）一级细分；
                // 杨（poplar）与云杉（spruce）不另建模板，用阔叶树 / 松的模板非等比缩放（不增加绘制次数）
                case "broad":      // 阔叶树：平滑树干 + 圆润树冠（二级细分）+ 顶上一小团
                    mb.Lathe(V(0, 0, 0), new[] { P2(0.09, 0), P2(0.06, 0.55), P2(0.035, 0.85) }, 8, bark);
                    mb.Sphere(V(0, 0.98, 0), V(0.52, 0.46, 0.52), C(0.36, 0.56, 0.28), 2, new SphereOpt { seed = 3, rough = 0.06f, bottomShade = 0.3f, smoothN = 0.6f });
                    mb.Sphere(V(0.18, 1.3, -0.08), V(0.28, 0.25, 0.28), C(0.42, 0.62, 0.32), 1, new SphereOpt { seed = 7, rough = 0.03f, bottomShade = 0.2f, smoothN = 0.7f });
                    break;
                case "broadLo":    // 远景阔叶树
                    mb.Lathe(V(0, 0, 0), new[] { P2(0.09, 0), P2(0.05, 0.6) }, 5, bark);
                    mb.Sphere(V(0, 1.0, 0), V(0.55, 0.5, 0.55), C(0.38, 0.58, 0.29), 1, new SphereOpt { seed = 5, rough = 0.05f, bottomShade = 0.3f, smoothN = 0.85f });
                    break;
                case "pine":       // 松：三层圆锥树冠（平滑法线）
                case "pineLo":
                    {
                        mb.Lathe(V(0, 0, 0), new[] { P2(0.08, 0), P2(0.04, 0.6) }, 6, bark);
                        double[][] L3 = { new[] { 0.3, 0.52, 0.8 }, new[] { 0.72, 0.4, 0.7 }, new[] { 1.1, 0.27, 0.58 } };
                        Color[] C3 = { C(0.22, 0.42, 0.26), C(0.26, 0.47, 0.29), C(0.3, 0.52, 0.32) };
                        for (int q = 0; q < 3; q++)
                        {
                            double y0 = L3[q][0], r0 = L3[q][1], h0 = L3[q][2];
                            mb.Lathe(V(0, y0, 0), new[] { P2(0, -0.02), P2(r0 * 0.96, 0.02), P2(r0, 0.07), P2(r0 * 0.45, h0 * 0.55), P2(0, h0) }, kind == "pine" ? 14 : 9, C3[q]);
                        }
                        break;
                    }
                case "palm":       // 椰枣 / 椰树：弯曲树干 + 弧形羽叶
                    {
                        var pts = new Vector3[6]; var rad = new float[6];
                        for (int i = 0; i <= 5; i++) { double t = i / 5.0; pts[i] = V(Math.Sin(t * 1.3) * 0.22, t * 1.75, 0); rad[i] = (float)(0.075 - t * 0.03); }
                        mb.Tube(pts, rad, 6, C(0.55, 0.43, 0.28), true);
                        var top = pts[5];
                        for (int i = 0; i < 7; i++)
                        {
                            double a = i * Math.PI * 2 / 7, ca = Math.Cos(a), sa = Math.Sin(a);
                            double L = 0.72;
                            var mid = V(top.x + ca * L * 0.5, top.y + 0.12, top.z + sa * L * 0.5); var tip = V(top.x + ca * L, top.y - 0.28, top.z + sa * L);
                            double sx = -sa * 0.13, sz = ca * 0.13; var leaf = Sh(C(0.27, 0.5, 0.22), (i % 2) * 0.07);
                            // 羽叶用平滑的朝上 / 朝下法线（同样的三角形数）；叶基略暗、叶尖略亮
                            Vector3 nU = V(ca * 0.3, 1, sa * 0.3), nD = V(-ca * 0.3, -1, -sa * 0.3); Color cB = Sh(leaf, -0.14), cT = Sh(leaf, 0.08);
                            for (int f = 1; f >= -1; f -= 2)
                            {
                                var sd = V(mid.x + sx * f, mid.y - 0.03, mid.z + sz * f);
                                foreach (var n in new[] { nU, nD }) { mb.TriN(top, sd, mid, n, n, n, cB, leaf, leaf); mb.TriN(mid, sd, tip, n, n, n, leaf, leaf, cT); }
                            }
                        }
                        break;
                    }
                case "cypress":    // 地中海柏：细高的尖塔形（平滑的旋转体，与攻击画面一致）
                    mb.Lathe(V(0, 0, 0), new[] { P2(0.07, 0), P2(0.04, 0.3) }, 6, bark);
                    mb.Lathe(V(0, 0.16, 0), new[] { P2(0.03, 0), P2(0.24, 0.12), P2(0.3, 0.45), P2(0.26, 0.85), P2(0.16, 1.18), P2(0.06, 1.38), P2(0, 1.44) }, 12, C(0.22, 0.4, 0.24));
                    break;
                case "acacia":     // 稀树草原：斜干分叉 + 伞形平顶
                case "acaciaLo":
                    mb.Tube(new[] { V(0, 0, 0), V(0.05, 0.55, 0), V(0.12, 0.95, 0.03) }, new[] { 0.07f, 0.05f, 0.035f }, 6, bark);
                    if (kind == "acacia") mb.Tube(new[] { V(0.03, 0.45, 0), V(-0.22, 0.88, 0.06) }, new[] { 0.035f, 0.025f }, 5, bark);
                    // 树冠一级细分（稀树草原一场可有两三百棵近景金合欢），明暗靠 smoothN 圆润
                    mb.Sphere(V(0.1, 1.05, 0), V(0.64, 0.22, 0.58), C(0.4, 0.52, 0.26), 1, new SphereOpt { seed = 13, rough = 0.05f, bottomShade = 0.4f, smoothN = 0.85f });
                    break;
                case "olive":      // 橄榄：扭曲的矮干、灰绿的宽冠（两团）
                case "oliveLo":
                    mb.Tube(new[] { V(0, 0, 0), V(0.07, 0.24, 0.02), V(-0.02, 0.44, 0.05), V(0.03, 0.56, 0) }, new[] { 0.085f, 0.065f, 0.05f, 0.04f }, kind == "olive" ? 7 : 5, C(0.42, 0.36, 0.28));
                    mb.Sphere(V(0.02, 0.72, 0), V(0.5, 0.3, 0.46), C(0.45, 0.52, 0.36), kind == "olive" ? 2 : 1, new SphereOpt { seed = 19, rough = 0.1f, bottomShade = 0.32f, smoothN = kind == "olive" ? 0.6f : 0.85f });
                    if (kind == "olive") mb.Sphere(V(-0.24, 0.66, 0.12), V(0.3, 0.22, 0.28), C(0.48, 0.55, 0.38), 1, new SphereOpt { seed = 20, rough = 0.08f, bottomShade = 0.3f, smoothN = 0.7f });
                    break;
                case "umbrella":   // 石松（地中海伞松）：高直的干、扁平的伞冠
                case "umbrellaLo":
                    mb.Tube(new[] { V(0, 0, 0), V(0.05, 0.62, 0), V(0.1, 1.18, 0.02) }, new[] { 0.07f, 0.05f, 0.035f }, kind == "umbrella" ? 6 : 5, bark);
                    mb.Sphere(V(0.1, 1.28, 0), V(0.6, 0.2, 0.56), C(0.24, 0.42, 0.24), kind == "umbrella" ? 2 : 1, new SphereOpt { seed = 15, rough = 0.06f, bottomShade = 0.4f, smoothN = kind == "umbrella" ? 0.65f : 0.9f, flat = -0.4f });
                    break;
                case "rock":       // 圆润的石块（单位半径）
                    mb.Sphere(V(0, 0.35, 0), V(1, 0.72, 0.9), C(0.47, 0.46, 0.43), 1, new SphereOpt { seed = 17, rough = 0.16f, flat = -0.45f, bottomShade = 0.3f, top = 0.12f });
                    break;
                // ---- 平原格的点缀（不挡格子中心）与偶见的村舍、水井、大车 --
                case "bush":       // 灌木：两团平滑的叶簇
                    mb.Sphere(V(0, 0.13, 0), V(0.24, 0.17, 0.21), C(0.36, 0.54, 0.27), 1, new SphereOpt { seed = 21, rough = 0.12f, bottomShade = 0.35f, flat = -0.5f, smoothN = 0.6f });
                    mb.Sphere(V(0.15, 0.1, 0.06), V(0.15, 0.12, 0.14), C(0.4, 0.58, 0.3), 1, new SphereOpt { seed = 22, rough = 0.1f, bottomShade = 0.3f, flat = -0.5f, smoothN = 0.6f });
                    break;
                case "scrub":      // 荒漠 / 草原的矮灌丛
                    mb.Sphere(V(0, 0.07, 0), V(0.2, 0.1, 0.18), C(0.5, 0.52, 0.32), 1, new SphereOpt { seed = 23, rough = 0.22f, bottomShade = 0.3f, flat = -0.4f, smoothN = 0.4f });
                    break;
                case "hay":        // 草垛
                    mb.Lathe(V(0, 0, 0), new[] { P2(0.15, 0), P2(0.16, 0.1), P2(0.14, 0.26), P2(0.07, 0.38), P2(0.012, 0.42), P2(0, 0.43) }, 12, C(0.7, 0.62, 0.42));
                    mb.Lathe(V(0, 0.17, 0), new[] { P2(0.15, 0), P2(0.145, 0.03) }, 12, C(0.52, 0.44, 0.3));
                    break;
                case "fence":      // 一段木栅栏：三根桩、两道横杆
                    {
                        var wd = C(0.47, 0.35, 0.22);
                        foreach (var x in new[] { -0.4, 0, 0.4 }) mb.Tube(new[] { V(x, 0, 0), V(x, 0.26, 0) }, new[] { 0.022f, 0.02f }, 5, wd, true);
                        foreach (var y in new[] { 0.1, 0.2 }) mb.Tube(new[] { V(-0.42, y, 0), V(0.42, y, 0) }, new[] { 0.013f, 0.013f }, 4, Sh(wd, 0.06), false);
                        break;
                    }
                case "flowers":    // 一片野花（白色花瓣，实例色着色）
                    {
                        var up = V(0, 1, 0);
                        for (int k = 0; k < 9; k++)
                        {
                            double a = k * 2.4, r = 0.05 + (k % 4) * 0.045, x = Math.Cos(a) * r, z = Math.Sin(a) * r, y = 0.04 + (k % 3) * 0.025, pr = 0.028;
                            for (int q = 0; q < 5; q++)
                            {
                                double a0 = q * Math.PI * 2 / 5, a1 = a0 + Math.PI * 2 / 5;
                                mb.TriN(V(x, y + 0.004, z), V(x + Math.Cos(a0) * pr, y, z + Math.Sin(a0) * pr), V(x + Math.Cos(a1) * pr, y, z + Math.Sin(a1) * pr), up, up, up, C(1, 1, 1));
                            }
                            mb.Tube(new[] { V(x, 0, z), V(x, y, z) }, new[] { 0.004f, 0.004f }, 3, C(0.45, 0.62, 0.32), false);
                        }
                        break;
                    }
                case "cairn":      // 草原上的石堆（敖包）
                    mb.Sphere(V(0, 0.08, 0), V(0.2, 0.1, 0.18), C(0.56, 0.54, 0.5), 1, new SphereOpt { seed = 31, rough = 0.12f, flat = -0.4f, smoothN = 0.4f });
                    mb.Sphere(V(0.02, 0.2, 0), V(0.13, 0.08, 0.12), C(0.6, 0.58, 0.54), 1, new SphereOpt { seed = 32, rough = 0.12f, smoothN = 0.4f });
                    mb.Sphere(V(0, 0.29, 0.01), V(0.07, 0.06, 0.07), C(0.62, 0.6, 0.56), 1, new SphereOpt { seed = 33, rough = 0.1f, smoothN = 0.4f });
                    mb.Tube(new[] { V(0, 0.3, 0), V(0, 0.55, 0) }, new[] { 0.01f, 0.008f }, 4, C(0.45, 0.35, 0.22), false);
                    break;
                case "well":       // 水井：石砌井圈、两根井柱与小顶
                    {
                        Color st = C(0.62, 0.6, 0.55), wd = C(0.42, 0.3, 0.2);
                        mb.Lathe(V(0, 0, 0), new[] { P2(0.17, 0), P2(0.17, 0.15, true), P2(0.12, 0.15, true), P2(0.12, 0.03) }, 12, st);
                        foreach (var x in new[] { -0.15, 0.15 }) mb.Tube(new[] { V(x, 0.1, 0), V(x, 0.45, 0) }, new[] { 0.016f, 0.015f }, 5, wd, false);
                        mb.Tube(new[] { V(-0.18, 0.43, 0), V(0.18, 0.43, 0) }, new[] { 0.014f, 0.014f }, 5, wd, false);
                        BattleLook.Gable(mb, 0, 0.45f, 0, 0.44f, 0.3f, 0.12f, C(0.38, 0.34, 0.32), false);
                        break;
                    }
                case "cart":       // 大车：车厢、两只轮子、辕杆
                    {
                        var wd = C(0.5, 0.37, 0.23);
                        mb.Box(V(0, 0.2, 0), V(0.46, 0.06, 0.26), wd);
                        foreach (var sz in new[] { -1, 1 }) mb.Box(V(0, 0.27, sz * 0.13), V(0.46, 0.09, 0.025), Sh(wd, -0.08));
                        var M0 = mb.M;
                        foreach (var sz in new[] { -1, 1 })
                        {
                            // 网页版 makeTranslation(0, 0.15, sz·0.16) × makeRotationX(π/2)（同一组矩阵元素）
                            var R = Matrix4x4.identity; float cs = (float)Math.Cos(Math.PI / 2), sn = (float)Math.Sin(Math.PI / 2);
                            R.m11 = cs; R.m12 = -sn; R.m21 = sn; R.m22 = cs;
                            mb.M = Matrix4x4.Translate(V(0, 0.15, sz * 0.16)) * R;
                            mb.Lathe(V(0, -0.015, 0), new[] { P2(0.15, 0, true), P2(0.15, 0.03, true), P2(0.04, 0.03), P2(0.04, 0.05) }, 12, Sh(wd, -0.15));
                        }
                        mb.M = M0;
                        mb.Tube(new[] { V(0.22, 0.2, 0.06), V(0.6, 0.06, 0.06) }, new[] { 0.012f, 0.012f }, 4, wd, false);
                        mb.Tube(new[] { V(0.22, 0.2, -0.06), V(0.6, 0.06, -0.06) }, new[] { 0.012f, 0.012f }, 4, wd, false);
                        mb.Box(V(-0.05, 0.3, 0), V(0.3, 0.08, 0.2), C(0.78, 0.68, 0.4));
                        break;
                    }
                case "hut_han": case "hut_tile": case "hut_flat": case "hut_yurt": case "hut_thatch": case "hut_long": case "hut_round": case "hut_stilt":
                    {
                        // 村舍（按战场所在城池的文化）：约 0.9 × 0.6 单位，高约 0.8
                        string st = kind.Substring(4);
                        if (st == "yurt")
                        {
                            mb.Lathe(V(0, 0, 0), new[] { P2(0.34, 0), P2(0.34, 0.24), P2(0.31, 0.29), P2(0.14, 0.44), P2(0.04, 0.48), P2(0, 0.49) }, 14, C(0.88, 0.84, 0.74));
                            mb.Lathe(V(0, 0.18, 0), new[] { P2(0.345, 0), P2(0.345, 0.035) }, 14, C(0.62, 0.28, 0.18));
                            mb.Box(V(0, 0.12, -0.33), V(0.14, 0.24, 0.04), C(0.72, 0.32, 0.14));
                        }
                        else if (st == "round")
                        {
                            mb.Lathe(V(0, 0, 0), new[] { P2(0.34, 0), P2(0.34, 0.3) }, 14, C(0.64, 0.54, 0.4));
                            mb.Lathe(V(0, 0.25, 0), new[] { P2(0.44, 0), P2(0.22, 0.24), P2(0.03, 0.5), P2(0, 0.52) }, 14, C(0.68, 0.57, 0.36));
                            mb.Box(V(0, 0.13, -0.34), V(0.13, 0.26, 0.03), C(0.3, 0.2, 0.13));
                        }
                        else if (st == "flat")
                        {
                            var c = C(0.8, 0.66, 0.48);
                            mb.Box(V(0, 0.24, 0), V(0.8, 0.48, 0.55), c);
                            mb.Box(V(-0.18, 0.62, 0.05), V(0.36, 0.28, 0.36), Sh(c, 0.03));
                            double[][] P4 = { new[] { 0, -0.265, 0.8, 0.04 }, new[] { 0, 0.265, 0.8, 0.04 }, new[] { -0.39, 0, 0.04, 0.55 }, new[] { 0.39, 0, 0.04, 0.55 } };
                            foreach (var q in P4) mb.Box(V(q[0], 0.52, q[1]), V(q[2], 0.07, q[3]), Sh(c, 0.05));
                            mb.Box(V(0.2, 0.14, -0.28), V(0.13, 0.28, 0.02), C(0.3, 0.2, 0.13));
                        }
                        else if (st == "long")
                        {
                            mb.Box(V(0, 0.13, 0), V(1.0, 0.26, 0.5), C(0.46, 0.34, 0.22));
                            BattleLook.Gable(mb, 0, 0.24f, 0, 1.1f, 0.78f, 0.5f, C(0.68, 0.57, 0.36), false);
                        }
                        else if (st == "stilt")
                        {
                            foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 }) mb.Tube(new[] { V(sx * 0.3, 0, sz * 0.2), V(sx * 0.3, 0.24, sz * 0.2) }, new[] { 0.02f, 0.02f }, 4, C(0.4, 0.3, 0.2), false);
                            mb.Box(V(0, 0.4, 0), V(0.7, 0.3, 0.46), C(0.66, 0.56, 0.38));
                            BattleLook.Gable(mb, 0, 0.54f, 0, 0.9f, 0.74f, 0.4f, C(0.68, 0.57, 0.36), false);
                        }
                        else
                        {
                            bool tile = st == "tile", th = st == "thatch";
                            var wallC = tile ? C(0.88, 0.84, 0.76) : th ? C(0.74, 0.58, 0.42) : C(0.86, 0.8, 0.68);
                            mb.Box(V(0, 0.22, 0), V(0.78, 0.44, 0.5), wallC);
                            mb.Box(V(0.18, 0.14, -0.255), V(0.13, 0.28, 0.02), C(0.3, 0.2, 0.13));
                            if (st == "han") mb.ChineseRoof(V(0, 0.42, 0), 0.98f, 0.68f, 0.3f, C(0.3, 0.32, 0.36));
                            else BattleLook.Gable(mb, 0, 0.42f, 0, 0.92f, 0.66f, th ? 0.36f : 0.24f, th ? C(0.68, 0.57, 0.36) : C(0.72, 0.36, 0.24), false);
                        }
                        break;
                    }
                case "tuft":       // 草丛：一簇向外弯的宽草叶（双面，每片两节）
                    {
                        var g = C(0.5, 0.68, 0.34);
                        for (int k = 0; k < 5; k++)
                        {
                            double a = k * Math.PI * 2 / 5 + (k % 2) * 0.3, ca = Math.Cos(a), sa = Math.Sin(a), w = 0.055;
                            double h = 0.17 + (k % 3) * 0.05, lean = 0.1 + (k % 2) * 0.05;
                            Vector3 p0 = V(-sa * w, 0, ca * w), p1 = V(sa * w, 0, -ca * w), tp = V(ca * lean, h, sa * lean);
                            var c0 = Sh(g, -0.12 + (k % 3) * 0.07);
                            mb.Tri(p0, tp, p1, c0); mb.Tri(p0, p1, tp, c0);
                        }
                        break;
                    }
            }
            // 草叶、野花：法线朝上（像草片一样整体受光，不显出一片片的折面）
            if (kind == "tuft" || kind == "flowers") for (int i = 0; i < mb.n.Count; i++) mb.n[i] = Vector3.up;
            PROP_TPL[kind] = mb;
            return mb;
        }

        // 树种按「战场群落 + 防守城池的文化」选（与攻击画面的布景一致）：
        // 沙漠 / 地中海：椰枣、橄榄、柏、灌丛（沙漠格不长松）；温带：阔叶、松、杨；热带：椰、阔叶；寒带：云杉 / 松；稀树草原：金合欢。
        // 干旱文化（安息、阿拉伯、西域）的城池落在世界地图的温带 / 草原格（河谷绿洲，如泰西封）时，按沙漠绿洲种树（西域是胡杨）；
        // 阿克苏姆 / 库施按稀树草原；天竺、东南亚的温带格按热带。spruce / poplar 是 pine / broad 模板的非等比缩放。
        // 安息、阿拉伯的改写只在北纬 39° 以南：高加索的姆茨赫塔、加巴拉、阿尔塔沙特（文化归安息）是湿润的温带林，按世界地图的群落种树。
        static readonly Dictionary<string, int> CULT_TREE_BIOME = new Dictionary<string, int> { { "persia", 2 }, { "arab", 2 }, { "tarim", 2 }, { "moor", 5 }, { "africa", 4 }, { "india", 3 }, { "seasia", 3 } };
        const double ARID_LAT_MAX = 39;
        static int TreeBiome(int bi, string cult, double lat)
        {
            int d;
            if (!CULT_TREE_BIOME.TryGetValue(cult ?? "", out d) && !CULT_TREE_BIOME.TryGetValue(Defs.CultureBase(cult), out d)) d = -1;
            if (d == 2 && cult != "tarim" && !double.IsNaN(lat) && lat >= ARID_LAT_MAX) return bi;
            if ((d == 2 || d == 4) && (bi == 0 || bi == 1)) return d;
            if (d == 2 && cult != "tarim" && (bi == 6 || bi == 7)) return 5;      // 扎格罗斯等干旱山地：橄榄、柏（不是云杉）
            if (d == 3 && bi == 0) return 3;
            if (d == 5 && (bi == 0 || bi == 1)) return 5;
            return bi;
        }
        struct KW { public string k; public double w; public KW(string k, double w) { this.k = k; this.w = w; } }
        static KW[] TreeKinds(int biome, bool near, string cult)
        {
            string B = near ? "broad" : "broadLo", O = near ? "olive" : "oliveLo", P = near ? "pine" : "pineLo", A = near ? "acacia" : "acaciaLo", U = near ? "umbrella" : "umbrellaLo";
            switch (biome)
            {
                case 1: return new[] { new KW("poplar", 0.45), new KW(B, 0.4), new KW(P, 0.15) };                    // 草原：杨、阔叶，偶有松
                case 2:
                    return cult == "tarim" ? new[] { new KW("poplar", 0.7), new KW(B, 0.3) }                           // 西域绿洲：胡杨（不种橄榄）
                        : cult == "africa" ? new[] { new KW("palm", 0.55), new KW(A, 0.45) }                            // 努比亚的尼罗河谷：椰枣、金合欢
                        : new[] { new KW("palm", 0.6), new KW(O, 0.3), new KW("cypress", 0.1) };                        // 沙漠绿洲：椰枣、橄榄、柏
                case 3: return new[] { new KW(B, 0.55), new KW("palm", 0.45) };                                       // 热带
                case 4:
                    return cult == "india" || cult == "seasia" ? new[] { new KW(A, 0.5), new KW("palm", 0.3), new KW(B, 0.2) }   // 天竺的干热林
                        : new[] { new KW(A, 0.8), new KW("palm", 0.2) };                                                // 稀树草原
                case 5: return new[] { new KW(O, 0.35), new KW("cypress", 0.2), new KW(U, 0.2), new KW(B, 0.25) };   // 地中海
                case 6: return new[] { new KW("spruce", 0.6), new KW(P, 0.3), new KW("poplar", 0.1) };                // 寒带
                case 7: return new[] { new KW(P, 0.6), new KW("spruce", 0.3), new KW(B, 0.1) };                       // 高山
                default: return new[] { new KW(B, 0.5), new KW(P, 0.3), new KW("poplar", 0.2) };                      // 温带
            }
        }
        // 山岳格上的树（沙漠不种）
        static readonly string[] MOUNTAIN_TREE = { "pine", "pine", null, "broad", "acacia", "olive", "spruce", "spruce" };
        // 别名 → 模板（近 / 远）、横向缩放、纵向缩放
        static bool TreeAlias(string kind, out string nearK, out string farK, out double ax, out double ay)
        {
            if (kind == "spruce") { nearK = "pine"; farK = "pineLo"; ax = 0.72; ay = 1.32; return true; }
            if (kind == "poplar") { nearK = "broad"; farK = "broadLo"; ax = 0.55; ay = 1.45; return true; }
            nearK = farK = null; ax = ay = 1; return false;
        }
        static string PickW(KW[] list, double r)
        {
            double s = 0; foreach (var e in list) s += e.w;
            r *= s;
            foreach (var e in list) { r -= e.w; if (r <= 0) return e.k; }
            return list[list.Length - 1].k;
        }
        // 树冠的群落色（乘在模板顶点色上）
        static readonly double[][] LEAF_MUL = { new[] { 1, 1, 1.0 }, new[] { 1.08, 1.02, 0.86 }, new[] { 1.05, 1.0, 0.9 }, new[] { 0.86, 1.04, 0.84 }, new[] { 1.1, 1.02, 0.84 },
            new[] { 1.0, 0.97, 0.9 }, new[] { 0.86, 0.94, 0.95 }, new[] { 0.9, 0.92, 0.92 } };

        void BuildProps()
        {
            var rnd = new SeededRandom(seed ^ 0x5bd1e995);
            Func<double> R = () => rnd.NextDouble();
            var lists = new Dictionary<string, List<PropInst>>(); var order = new List<string>();
            // hi：棋盘外、镜头一侧离棋盘边 3 格以内的树也用近景模板
            Action<string, double, double, double, double[], bool, double, bool> put = (kind, u, v, s, cm, near, sink, hi) =>
            {
                double wx = Origin.x + (u + 0.5) * T, wz = Origin.z + (v + 0.5) * T;
                double y = GridH(u, v) - (sink != 0 ? sink : 0.04) * s;   // 网页版 (sink || 0.04)：0 与缺省都按 0.04
                string nk, fk; double ax, ay;
                bool al = TreeAlias(kind, out nk, out fk, out ax, out ay);
                if (al) kind = near || hi ? nk : fk;
                List<PropInst> L;
                if (!lists.TryGetValue(kind, out L)) { L = new List<PropInst>(); lists[kind] = L; order.Add(kind); }
                L.Add(new PropInst { x = wx, y = y, z = wz, s = s, yaw = R() * Math.PI * 2, r = (float)cm[0], g = (float)cm[1], b = (float)cm[2], near = near, u = u, v = v, ax = al ? ax : 1, ay = al ? ay : 1 });
            };
            string cult = look.culture ?? "han";
            Func<int, int> tb = bi => TreeBiome(bi, cult, lat);
            Func<double, double, int> biomeAt = (u, v) => G.biome[(int)Clamp(JsRound(Gj(v)), 0, nv - 1) * nu + (int)Clamp(JsRound(Gi(u)), 0, nu - 1)];
            Func<int, double[]> leaf = bi => { var L = bi < LEAF_MUL.Length ? LEAF_MUL[bi] : LEAF_MUL[0]; double k = 0.86 + R() * 0.28; return new[] { L[0] * k, L[1] * k, L[2] * k }; };
            Func<int, int, bool> occupied = (x, y) => { foreach (var q in m.units) if (q.alive && q.x == x && q.y == y) return true; return false; };
            double[] ONE = { 1, 1, 1 };
            // 棋盘内：森林格的树围成一圈（格子中心留给部队），山岳的石块与松，山丘偶有孤树
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    var t = m.map[x, y];
                    if (InFort(x, y)) continue;
                    int bi = biomeAt(x, y);
                    if (t == Terrain.Forest)
                    {
                        const int n = 5; double a0 = R() * Math.PI;
                        for (int k = 0; k < n; k++)
                        {
                            double a = a0 + k * Math.PI * 2 / n + (R() - 0.5) * 0.5, r = 0.66 + R() * 0.26;
                            put(PickW(TreeKinds(tb(bi), true, cult), R()), x + Math.Cos(a) * r, y + Math.Sin(a) * r, 0.85 + R() * 0.4, leaf(bi), true, 0, false);
                        }
                        if (!occupied(x, y) && R() < 0.2) put(PickW(TreeKinds(tb(bi), true, cult), R()), x + (R() - 0.5) * 0.3, y + (R() - 0.5) * 0.3, 0.8 + R() * 0.3, leaf(bi), true, 0, false);
                    }
                    else if (t == Terrain.Mountain)
                    {
                        int n = R() < 0.6 ? 1 : 0;
                        for (int k = 0; k < n; k++) { double g = 0.88 + R() * 0.24; put("rock", x + (R() - 0.5) * 0.9, y + (R() - 0.5) * 0.9, 0.2 + R() * 0.22, new[] { g, g * 0.98, g * 0.95 }, true, 0.25, false); }
                        var mk = MOUNTAIN_TREE[tb(bi)];
                        if (mk != null && R() < 0.6) put(mk, x + (R() - 0.5) * 1.1, y + (R() - 0.5) * 1.1, 0.6 + R() * 0.3, leaf(bi), true, 0, false);
                    }
                    else if (t == Terrain.Hill)
                    {
                        if (R() < 0.3) put("rock", x + (R() - 0.5) * 1.2, y + (R() - 0.5) * 1.2, 0.12 + R() * 0.12, new[] { 1, 0.98, 0.95 }, true, 0.25, false);
                        // 孤树只种在格子靠北（远离镜头）的一半，部队起始格及其南邻不种（免得挡住主将）
                        if (R() < 0.3 && tb(bi) != 2 && !occupied(x, y) && !occupied(x, y + 1))
                        {
                            double a = (0.2 + R() * 0.6) * Math.PI;
                            put(PickW(TreeKinds(tb(bi), true, cult), R()), x + Math.Cos(a) * 0.72, y + Math.Sin(a) * 0.72, 0.75 + R() * 0.3, leaf(bi), true, 0, false);
                        }
                    }
                    // 草丛（平原、山丘、森林；沙漠稀少）。平原格更密（7 簇、略小）
                    if (t == Terrain.Plain || t == Terrain.Hill || t == Terrain.Forest)
                    {
                        if (IsBridge(x, y)) continue;
                        double arid = BIOME_ARID[bi]; int n = JsRound((t == Terrain.Plain ? 7 : 3) * (1 - arid * 0.7));
                        for (int k = 0; k < n; k++)
                        {
                            double du = (R() - 0.5) * 1.8, dv = (R() - 0.5) * 1.8;
                            if (Math.Abs(du) < 0.42 && Math.Abs(dv) < 0.42) du = du < 0 ? -0.5 - R() * 0.3 : 0.5 + R() * 0.3;
                            double[] g = arid > 0.5 ? new[] { 1.18, 1.05, 0.72 } : arid > 0.2 ? new[] { 1.08, 1.02, 0.78 } : new[] { 0.9 + R() * 0.15, 1, 0.85 };
                            double sc = t == Terrain.Plain ? 0.6 + R() * 0.45 : 0.9 + R() * 0.6;
                            if (GridH(x + du, y + dv) < WATER_Y + 0.12) continue;   // 不长在河里、塘里
                            put("tuft", x + du, y + dv, sc, g, true, 0, false);
                        }
                    }
                    // 平原格的点缀（离格子中心 0.5 格以外，不挡部队）——温带 / 地中海 / 热带：灌木、栅栏、野花、草垛；
                    // 草原：石堆、矮灌、碎石；沙漠：矮灌、碎石、水边的椰枣。约 5% 的空格另有一处村舍（按城池文化）、水井或大车
                    if (t == Terrain.Plain && !IsBridge(x, y))
                    {
                        int o = (int)Clamp(JsRound(Gj(y)), 0, nv - 1) * nu + (int)Clamp(JsRound(Gi(x)), 0, nu - 1);
                        bool onRoad = Tr != null && G.road[o] < 0.14;
                        int xx0 = x, yy0 = y;
                        Func<double, double, double[]> spot = (r0, r1) => { double a = R() * Math.PI * 2, r = r0 + R() * (r1 - r0); return new[] { xx0 + Math.Cos(a) * r, yy0 + Math.Sin(a) * r }; };
                        Func<double, double, bool> dryAt = (u, v) => GridH(u, v) > WATER_Y + 0.16;
                        int nItems = onRoad ? 1 : 1 + (R() < 0.55 ? 1 : 0) + (R() < 0.25 ? 1 : 0);
                        for (int k = 0; k < nItems; k++)
                        {
                            var sp = spot(0.52, 0.86); double u = sp[0], v = sp[1];
                            if (!dryAt(u, v)) continue;
                            double r = R(); int db = tb(bi);              // 点缀与树种同按「群落 + 文化」（泰西封的河谷按沙漠绿洲）
                            if (db == 1 || db == 4)                         // 草原 / 稀树草原
                            {
                                if (r < 0.06) put("cairn", u, v, 1, ONE, true, 0.02, false);
                                else if (r < 0.5) put("scrub", u, v, 0.8 + R() * 0.5, new[] { 1.05, 1.0, 0.8 }, true, 0.02, false);
                                else if (r < 0.75) { double g = 0.92 + R() * 0.16; put("rock", u, v, 0.07 + R() * 0.07, new[] { g, g, g * 0.97 }, true, 0.25, false); }
                                else put("tuft", u, v, 1.1 + R() * 0.4, new[] { 1.1, 1.03, 0.76 }, true, 0, false);
                            }
                            else if (db == 2)                               // 沙漠
                            {
                                bool wetNear = G.river[o] < 2.2 || G.water[o] > 0;
                                if (wetNear && r < 0.18) put(cult == "tarim" ? "poplar" : "palm", u, v, (cult == "tarim" ? 0.75 : 0.55) + R() * 0.25, leaf(bi), true, 0, false);   // 西域水边是胡杨
                                else if (r < 0.6) put("scrub", u, v, 0.7 + R() * 0.5, new[] { 1.15, 1.05, 0.78 }, true, 0.02, false);
                                else { double g = 0.95 + R() * 0.12; put("rock", u, v, 0.06 + R() * 0.07, new[] { g * 1.05, g, g * 0.9 }, true, 0.25, false); }
                            }
                            else                                            // 温带、地中海、热带、北方
                            {
                                if (r < 0.34) put("bush", u, v, 0.75 + R() * 0.5, leaf(bi), true, 0.02, false);
                                else if (r < 0.52 && !onRoad) put("fence", u, v, 0.9 + R() * 0.3, ONE, true, 0, false);
                                else if (r < 0.78) { var F = FLOWER_COLS[(int)(R() * FLOWER_COLS.Length)]; put("flowers", u, v, 0.9 + R() * 0.5, F, true, 0, false); }
                                else if (r < 0.88 && (G.farm[o] > 0.15 || bi == 0)) put("hay", u, v, 0.65 + R() * 0.3, ONE, true, 0.02, false);
                                else { double g = 0.9 + R() * 0.15; put("rock", u, v, 0.06 + R() * 0.07, new[] { g, g, g * 0.97 }, true, 0.25, false); }
                            }
                        }
                        // 偶见的较大点缀：放在格角（离中心约 0.65 格），起始时附近一格内无部队
                        if (!onRoad && R() < 0.06 && !NearUnit(x, y))
                        {
                            double a = (Math.Floor(R() * 4) + 0.5) * Math.PI / 2, u = x + Math.Cos(a) * 0.66, v = y + Math.Sin(a) * 0.66;
                            if (dryAt(u, v))
                            {
                                double rr = R(); string hs;
                                if (!HUT_OF.TryGetValue(cult, out hs) && !HUT_OF.TryGetValue(Defs.CultureBase(cult), out hs)) hs = "han";
                                string hutK = "hut_" + hs;
                                if (rr < 0.55) put(hutK, u, v, 0.9 + R() * 0.2, ONE, true, 0.02, false);
                                else if (rr < 0.78) put("well", u, v, 1, ONE, true, 0.02, false);
                                else put("cart", u, v, 1, ONE, true, 0.01, false);
                            }
                        }
                    }
                }
            // 棋盘外：按世界地图的林地分布种树；陡坡与高处散布石块
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    double u = G.u0 + i * G.step + (R() - 0.5) * 0.45, v = G.v0 + j * G.step + (R() - 0.5) * 0.45;
                    if (u > -1.0 && u < W && v > -1.0 && v < H) continue;
                    int o = j * nu + i; double h = GridH(u, v);
                    if (h < WATER_Y + 0.18) continue;
                    int bi = G.biome[o]; double f = G.forest[o];
                    double sl = G.slope[o];
                    double dOut = Math.Max(Math.Max(-0.5 - u, u - (W - 0.5)), Math.Max(-0.5 - v, v - (H - 0.5)));
                    double p = (f > 0.25 ? f * 0.7 : (BIOME_ARID[bi] > 0.5 ? 0.004 : 0.03)) * (dOut > 8 ? 0.45 : 1);
                    // 近景模板只给镜头一侧（南边，v < 0）3 格内的树：北、东、西三边离镜头远，用远景模板
                    bool hiT = dOut < 3 && v < 0;
                    if (R() < p && sl < 0.5) put(PickW(TreeKinds(tb(bi), hiT, cult), R()), u, v, 0.9 + R() * 0.55, leaf(bi), false, 0, hiT);
                    else if ((sl > 0.32 || h > BASE_Y + 1.6) && R() < 0.035) { double g = 0.9 + R() * 0.2; put("rock", u, v, 0.25 + R() * 0.35, new[] { g, g * 0.98, g * 0.95 }, false, 0.3, false); }
                    else if (Math.Max(Math.Abs(u - (W - 1) / 2.0) - W / 2.0, Math.Abs(v - (H - 1) / 2.0) - H / 2.0) < 3 && R() < 0.5 * (1 - BIOME_ARID[bi] * 0.7))
                        put("tuft", u, v, 1 + R() * 0.5, BIOME_ARID[bi] > 0.3 ? new[] { 1.12, 1.04, 0.74 } : new[] { 0.92, 1, 0.86 }, false, 0, false);
                }
            // 实例批次（棋盘内的树挡在部队与镜头之间时淡去，见 BattleView 的淡出更新）
            int tris = 0;
            foreach (var kind in order)
                foreach (var near in new[] { true, false })
                {
                    var src = lists[kind];
                    var B = new PropBatch { kind = kind, near = near };
                    foreach (var e in src) if (e.near == near) B.items.Add(e);
                    if (B.items.Count == 0) continue;
                    var tpl = PropTemplate(kind);
                    B.fade = near && !SMALL_PROP.Contains(kind);
                    B.castShadow = kind != "tuft" && kind != "flowers" && near;
                    B.receiveShadow = kind != "tuft" && kind != "flowers";
                    B.tris = tpl.t.Count / 3 * B.items.Count;
                    int bidx = Props.Count;
                    if (B.fade)
                        for (int k = 0; k < B.items.Count; k++)
                        {
                            var e = B.items[k];
                            var it = new FadeItem { batch = bidx, k = k, x = e.x, z = e.z, s = e.s * e.ay };
                            fadeItems.Add(it);
                            long key = TileKey(JsRound(e.u), JsRound(e.v));
                            List<FadeItem> L;
                            if (!fadeByTile.TryGetValue(key, out L)) { L = new List<FadeItem>(); fadeByTile[key] = L; }
                            L.Add(it);
                        }
                    Props.Add(B);
                    tris += B.tris;
                    int nI = B.items.Count;
                    if (kind == "rock") Stats.rocks += nI;
                    else if (kind == "tuft") Stats.tufts += nI;
                    else if (SMALL_PROP.Contains(kind) || kind.StartsWith("hut_") || kind.Contains("well") || kind.Contains("cart")) Stats.dress += nI;
                    else Stats.trees += nI;
                }
            Stats.propTris = tris;
            // 渡海登陆：布阵边外的海面上泊着攻方的船
            if (Tr != null && Tr.notes != null && Tr.notes.Contains("landing")) BuildBoats();
        }
        bool NearUnit(int x, int y) { foreach (var q in m.units) if (q.alive && Math.Abs(q.x - x) <= 1 && Math.Abs(q.y - y) <= 1) return true; return false; }

        // 登陆的船（Unity 坐标：x 东、z 北）：平滑的船壳（12 个横截面 × 8 段，首尾上翘、尖首），甲板、
        // 桅杆与鼓起的帆（阵营色）、两舷各三支桨；yaw 为船首相对正东的偏角
        static void BoatInto(MeshBuilder mb, double x0, double y0, double z0, double yaw, double sc, Color team, SeededRandom rnd)
        {
            double cs = Math.Cos(yaw), sn = Math.Sin(yaw);
            Func<double, double, double, Vector3> P = (lx, ly, lz) => V(x0 + (lx * cs - lz * sn) * sc, y0 + ly * sc, z0 + (lx * sn + lz * cs) * sc);
            Func<double, double, double, Vector3> N = (nx, ny, nz) => V(nx * cs - nz * sn, ny, nx * sn + nz * cs);
            const double L = 2.1; const int NS = 12, NA = 8;
            Func<double, double> beam = s => 0.36 * Math.Pow(Math.Max(0.02, Math.Sin(Math.PI * s)), 0.55) + 0.015;
            Func<double, double> depth = s => 0.24 * (0.35 + 0.65 * Math.Pow(Math.Max(0, Math.Sin(Math.PI * s)), 0.5));
            Func<double, double> top = s => 0.1 + 0.22 * Math.Pow(2 * s - 1, 2) * (s > 0.5 ? 1.25 : 1);
            Func<double, double, double[]> hp = (s, a) => new[] { (s - 0.5) * L, top(s) - depth(s) * Math.Sin(a), beam(s) * Math.Cos(a) };
            var grid = new double[NS + 1][][];
            for (int i = 0; i <= NS; i++) { grid[i] = new double[NA + 1][]; for (int j = 0; j <= NA; j++) grid[i][j] = hp((double)i / NS, Math.PI * j / NA); }
            Func<int, int, Vector3> nrm = (i, j) =>
            {
                double[] a = grid[Math.Min(NS, i + 1)][j], b = grid[Math.Max(0, i - 1)][j], c = grid[i][Math.Min(NA, j + 1)], d = grid[i][Math.Max(0, j - 1)];
                double ux = a[0] - b[0], uy = a[1] - b[1], uz = a[2] - b[2], vx = c[0] - d[0], vy = c[1] - d[1], vz = c[2] - d[2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                var p = grid[i][j]; double cxl = p[0] * 0.6, outv = (p[0] - cxl) * nx + (p[1] - 0.05) * ny + p[2] * nz;
                double l = Hypot(nx, ny, nz); if (l == 0) l = 1; double sg = outv < 0 ? -1 : 1;
                return N(nx / l * sg, ny / l * sg, nz / l * sg);
            };
            Func<double[], Vector3> PA = q => P(q[0], q[1], q[2]);
            Color wood = C(0.42, 0.29, 0.17), woodD = C(0.3, 0.2, 0.12), deck = C(0.6, 0.47, 0.31);
            for (int i = 0; i < NS; i++)
                for (int j = 0; j < NA; j++)
                {
                    double[] a = grid[i][j], b = grid[i + 1][j], c = grid[i + 1][j + 1], d = grid[i][j + 1];
                    var k = j == 0 || j == NA - 1 ? woodD : wood;
                    mb.TriN(PA(a), PA(b), PA(c), nrm(i, j), nrm(i + 1, j), nrm(i + 1, j + 1), k);
                    mb.TriN(PA(a), PA(c), PA(d), nrm(i, j), nrm(i + 1, j + 1), nrm(i, j + 1), k);
                }
            var up = N(0, 1, 0);
            for (int i = 0; i < NS; i++)
            {
                double[] a = grid[i][0], b = grid[i + 1][0], c = grid[i + 1][NA], d = grid[i][NA]; const double dy = -0.03;
                mb.TriN(P(a[0], a[1] + dy, a[2]), P(b[0], b[1] + dy, b[2]), P(c[0], c[1] + dy, c[2]), up, up, up, deck);
                mb.TriN(P(a[0], a[1] + dy, a[2]), P(c[0], c[1] + dy, c[2]), P(d[0], d[1] + dy, d[2]), up, up, up, deck);
            }
            // 桅杆、横桁与鼓帆（双面）
            const double mx = 0.12, mh = 1.55;
            mb.Tube(new[] { P(mx, 0.06, 0), P(mx, mh, 0) }, new[] { 0.035f, 0.025f }, 6, woodD, true);
            mb.Tube(new[] { P(mx + 0.02, mh - 0.08, -0.55), P(mx + 0.02, mh - 0.08, 0.55) }, new[] { 0.022f, 0.022f }, 5, woodD, true);
            Color sail = Sh(team, 0.18), sailB = Sh(team, -0.05); const int SU = 4, SV = 4;
            Func<double, double, double[]> sp = (a, b) => new[] { mx + 0.05 + 0.22 * Math.Sin(Math.PI * a) * (0.55 + 0.45 * Math.Sin(Math.PI * b)), mh - 0.1 - b * 0.95, (a - 0.5) * 1.05 * (1 - 0.12 * b) };
            Func<double, double, double[]> sn2 = (a, b) =>
            {
                const double e = 0.01; double[] p0 = sp(a, b), pa = sp(a + e, b), pb = sp(a, b + e);
                double ux = pa[0] - p0[0], uy = pa[1] - p0[1], uz = pa[2] - p0[2], vx = pb[0] - p0[0], vy = pb[1] - p0[1], vz = pb[2] - p0[2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx; double l = Hypot(nx, ny, nz); if (l == 0) l = 1;
                return new[] { nx / l, ny / l, nz / l };
            };
            for (int i = 0; i < SU; i++)
                for (int j = 0; j < SV; j++)
                {
                    double[][] q = { new[] { (double)i / SU, (double)j / SV }, new[] { (double)(i + 1) / SU, (double)j / SV }, new[] { (double)(i + 1) / SU, (double)(j + 1) / SV }, new[] { (double)i / SU, (double)(j + 1) / SV } };
                    var pts = new Vector3[4]; var ns = new double[4][];
                    for (int k = 0; k < 4; k++) { pts[k] = PA(sp(q[k][0], q[k][1])); ns[k] = sn2(q[k][0], q[k][1]); }
                    foreach (var sg in new[] { 1, -1 })
                    {
                        var nn = new Vector3[4];
                        for (int k = 0; k < 4; k++) nn[k] = N(ns[k][0] * sg, ns[k][1] * sg, ns[k][2] * sg);
                        var c = sg > 0 ? sail : sailB;
                        mb.TriN(pts[0], pts[1], pts[2], nn[0], nn[1], nn[2], c);
                        mb.TriN(pts[0], pts[2], pts[3], nn[0], nn[2], nn[3], c);
                    }
                }
            // 桨：两舷各三支，略有参差
            foreach (var side in new[] { 1, -1 })
                for (int k = 0; k < 3; k++)
                {
                    double s = 0.3 + k * 0.17; var g = hp(s, side > 0 ? 0 : Math.PI); double sw = (rnd.NextDouble() - 0.5) * 0.25;
                    Vector3 a = P(g[0], g[1] + 0.02, g[2]), b = P(g[0] - 0.12 + sw, -0.04, g[2] + side * 0.62);
                    mb.Tube(new[] { a, b }, new[] { 0.016f, 0.016f }, 4, woodD, true);
                    Vector3 t = P(g[0] - 0.12 + sw, -0.02, g[2] + side * 0.62), bl = P(g[0] - 0.12 + sw, -0.06, g[2] + side * 0.78);
                    mb.Tube(new[] { t, bl }, new[] { 0.05f, 0.035f }, 4, wood, true);
                }
        }
        void BuildBoats()
        {
            var mb = new MeshBuilder();
            var rnd = new SeededRandom(seed ^ 0x51ed270b);
            int n = 0;
            var placed = new List<double[]>();
            for (int k = 0; k < 18 && n < 4; k++)
            {
                double u = -1.5 - rnd.NextDouble() * 2.8, v = (H >> 1) + (rnd.NextDouble() - 0.5) * (H - 3);
                bool near = false; foreach (var q in placed) if (Hypot(q[0] - u, q[1] - v) < 1.6) { near = true; break; }
                if (HeightAt(u, v) > WATER_Y - 0.25 || near) continue;
                placed.Add(new[] { u, v });
                double yaw = (rnd.NextDouble() - 0.5) * 0.9 + (v < H / 2.0 ? 0.12 : -0.12);
                BoatInto(mb, Origin.x + (u + 0.5) * T, WATER_Y, Origin.z + (v + 0.5) * T, yaw, 0.95 + rnd.NextDouble() * 0.2, atkCol, rnd);
                n++;
            }
            if (n == 0) return;
            Boats = mb;
            Stats.boats = n;
        }

        // ====================================================== 城池 --
        // 城墙沿城池外圈格子的中心线（距本阵格中心 2 格）；城门格是城墙上的缺口（两侧门楼，部队站在缺口里，镜头看得见）。
        // 风格族：汉 / 高句丽（灰砖、方垛口、方角楼 + 歇山顶门楼）、罗马（浅石、方垛口、圆塔 + 锥顶红瓦）、
        // 西亚土坯（安息、阿拉伯、西域、贵霜：阶梯垛口、圆塔）、木栅（倭、南海、夷、凯尔特、日耳曼、草原：土垒 + 木栅 + 望楼）。
        public static string FortFamily(string style)
        {
            if (style == "roman") return "roman";
            if (style == "persia" || style == "arab" || style == "tarim" || style == "kushan") return "mud";
            if (style == "wa" || style == "stilt" || style == "celt" || style == "german" || style == "steppe") return "timber";
            return "han";
        }
        sealed class FortCol { public Color stone, cap, roof, timber, gold; }
        static Color Or(Color c, Color d) { return BattleLook.Has(c) ? c : d; }
        static FortCol FortColors(string style)
        {
            var k = BattleLook.Col(style);
            string fam = FortFamily(style);
            if (fam == "han") return new FortCol { stone = style == "korea" ? C(0.6, 0.6, 0.57) : C(0.6, 0.58, 0.54), cap = C(0.5, 0.48, 0.45), roof = style == "korea" ? C(0.25, 0.26, 0.29) : C(0.24, 0.27, 0.33), timber = C(0.6, 0.2, 0.15), gold = C(0.86, 0.66, 0.24) };
            if (fam == "roman") return new FortCol { stone = Or(k.wall, C(0.82, 0.78, 0.68)), cap = Sh(Or(k.wall, C(0.82, 0.78, 0.68)), -0.1), roof = Or(k.roof, C(0.74, 0.33, 0.21)), timber = Or(k.door, C(0.38, 0.24, 0.14)), gold = C(0.86, 0.7, 0.36) };
            if (fam == "mud") return new FortCol { stone = Or(k.wall, C(0.78, 0.64, 0.46)), cap = Sh(Or(k.wall, C(0.78, 0.64, 0.46)), 0.05), roof = Or(k.dome, C(0.84, 0.78, 0.66)), timber = Or(k.door, C(0.3, 0.2, 0.13)), gold = Or(k.trim, C(0.24, 0.5, 0.56)) };
            return new FortCol { stone = C(0.44, 0.4, 0.3), cap = C(0.5, 0.42, 0.3), roof = Or(k.thatch, C(0.62, 0.54, 0.34)), timber = Or(k.wood, C(0.48, 0.35, 0.22)), gold = C(0.8, 0.66, 0.3) };
        }
        // 四边形（Unity 坐标），按 hint（大致的外向法线）自动定正反（材质为单面）
        static void Qd(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 hint)
        {
            double ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, wx = c.x - a.x, wy = c.y - a.y, wz = c.z - a.z;
            double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
            if (nx * hint.x + ny * hint.y + nz * hint.z < 0) mb.Quad(d, c, b, a, col); else mb.Quad(a, b, c, d, col);
        }
        // 墙段：从 a 到 b（Unity 坐标的地面点），截面梯形（下宽 w0、上宽 w1、高 h），外侧方向 out（单位向量，xz）
        static void FortWallRun(MeshBuilder mb, Vector3 a, Vector3 b, double h, double w0, double w1, Vector2 outD, string fam, FortCol k, double gy)
        {
            double dx = b.x - a.x, dz = b.z - a.z, L = Hypot(dx, dz);
            if (L < 0.05) return;
            double tx = dx / L, tz = dz / L, ox = outD.x, oz = outD.y;
            Func<double, double, double, Vector3> P = (s, o, y) => V(a.x + tx * s + ox * o, gy + y, a.z + tz * s + oz * o);
            Vector3 OUT = V(ox, 0.15, oz), IN = V(-ox, 0.15, -oz), UP = V(0, 1, 0), T0 = V(-tx, 0, -tz), T1 = V(tx, 0, tz);
            if (fam == "timber")
            {
                // 土垒 + 木栅（尖桩）
                var bank = Sh(k.stone, 0.02); double bh = h * 0.45;
                Qd(mb, P(0, -w0 / 2, 0), P(0, -w1 / 2, bh), P(L, -w1 / 2, bh), P(L, -w0 / 2, 0), Sh(bank, -0.06), IN);
                Qd(mb, P(0, w0 / 2, 0), P(0, w1 / 2, bh), P(L, w1 / 2, bh), P(L, w0 / 2, 0), bank, OUT);
                Qd(mb, P(0, -w1 / 2, bh), P(0, w1 / 2, bh), P(L, w1 / 2, bh), P(L, -w1 / 2, bh), Sh(bank, 0.04), UP);
                for (int e = 0; e < 2; e++)
                {
                    double s = e == 0 ? 0 : L; var hn = e == 0 ? T0 : T1;
                    mb.Tri(P(s, -w0 / 2, 0), P(s, -w1 / 2, bh), P(s, w0 / 2, 0), bank); mb.Tri(P(s, -w0 / 2, 0), P(s, w0 / 2, 0), P(s, -w1 / 2, bh), bank);
                    Qd(mb, P(s, -w1 / 2, bh), P(s, w1 / 2, bh), P(s, w0 / 2, 0), P(s, -w0 / 2, 0), bank, hn);
                }
                int n = Math.Max(2, JsRound(L / 0.19));
                for (int i = 0; i <= n; i++)
                {
                    double s = i * L / n, ph = h * (0.95 + ((i * 7) % 5) * 0.025);
                    var bs = P(s, w1 * 0.25, bh - 0.05);
                    mb.SmoothCylinder(bs, 0.075f, 0.07f, (float)(ph * 0.55), 6, Sh(k.timber, ((i * 3) % 4) * 0.03), false);
                    mb.Lathe(V(bs.x, bs.y + ph * 0.55, bs.z), new[] { P2(0.07, 0), P2(0, 0.16) }, 6, Sh(k.timber, 0.08));
                }
                return;
            }
            // 石 / 砖 / 土坯：墙身（外侧略有收分）、勒脚、墙顶、垛口、内侧女墙
            Color cs = k.stone, cap = k.cap;
            Qd(mb, P(0, w0 / 2, 0), P(0, w1 / 2, h), P(L, w1 / 2, h), P(L, w0 / 2, 0), cs, OUT);
            Qd(mb, P(0, -w0 / 2, 0), P(0, -w1 / 2, h), P(L, -w1 / 2, h), P(L, -w0 / 2, 0), Sh(cs, -0.08), IN);
            Qd(mb, P(0, -w1 / 2, h), P(0, w1 / 2, h), P(L, w1 / 2, h), P(L, -w1 / 2, h), Sh(cap, 0.02), UP);
            Qd(mb, P(0, -w0 / 2, 0), P(0, -w1 / 2, h), P(0, w1 / 2, h), P(0, w0 / 2, 0), Sh(cs, -0.12), T0);
            Qd(mb, P(L, -w0 / 2, 0), P(L, -w1 / 2, h), P(L, w1 / 2, h), P(L, w0 / 2, 0), Sh(cs, -0.12), T1);
            Qd(mb, P(0, w0 / 2 + 0.05, 0), P(0, w0 / 2 + 0.03, 0.16), P(L, w0 / 2 + 0.03, 0.16), P(L, w0 / 2 + 0.05, 0), Sh(cs, -0.16), OUT);   // 勒脚
            Qd(mb, P(0, w1 / 2 + 0.04, h - 0.06), P(0, w1 / 2 + 0.04, h), P(L, w1 / 2 + 0.04, h), P(L, w1 / 2 + 0.04, h - 0.06), Sh(cap, -0.05), OUT);  // 檐线
            // 垛口
            int nm = Math.Max(2, JsRound(L / 0.42));
            for (int i = 0; i < nm; i++)
            {
                double s = (i + 0.5) * L / nm; var c = P(s, w1 / 2 - 0.07, h);
                if (fam == "mud") BattleLook.StepMerlon(mb, c.x, c.y, c.z, 0.62f, cs);
                else mb.Box(V(c.x, c.y + 0.13, c.z), V(Math.Abs(tx) * 0.24 + Math.Abs(tz) * 0.13 + 0.02, 0.26, Math.Abs(tz) * 0.24 + Math.Abs(tx) * 0.13 + 0.02), Sh(cs, 0.04), 0.06f);
            }
            // 内侧矮女墙
            Vector3 q0 = P(0, -w1 / 2 + 0.05, h), q1 = P(L, -w1 / 2 + 0.05, h);
            mb.Box(V((q0.x + q1.x) / 2.0, gy + h + 0.06, (q0.z + q1.z) / 2.0), V(Math.Abs(tx) * L + 0.1 * Math.Abs(tz), 0.12, Math.Abs(tz) * L + 0.1 * Math.Abs(tx)), Sh(cs, -0.04), 0.05f);
        }
        // 塔：中心 (x, z)，地面 gy；方塔（汉）带歇山顶亭，圆塔（罗马 / 土坯）带垛口环与锥顶 / 平顶，木望楼
        static void FortTower(MeshBuilder mb, string fam, FortCol k, double x, double z, double gy, double h, double r, bool roof)
        {
            if (fam == "han")
            {
                mb.Box(V(x, gy + h / 2, z), V(r * 2, h, r * 2), k.stone, 0.05f);
                mb.Box(V(x, gy + 0.08, z), V(r * 2 + 0.12, 0.16, r * 2 + 0.12), Sh(k.stone, -0.15));
                for (int i = 0; i < 4; i++)
                {
                    double a = i * Math.PI / 2;
                    foreach (var s in new[] { -0.55, 0, 0.55 })
                    {
                        double px = x + Math.Cos(a) * (r - 0.06) - Math.Sin(a) * s * r, pz = z + Math.Sin(a) * (r - 0.06) + Math.Cos(a) * s * r;
                        mb.Box(V(px, gy + h + 0.12, pz), V(0.2, 0.24, 0.2), Sh(k.stone, 0.05), 0.06f);
                    }
                }
                if (roof)
                {
                    // 亭：四根红柱 + 曲面屋顶
                    foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 }) mb.SmoothCylinder(V(x + sx * r * 0.55, gy + h, z + sz * r * 0.55), 0.06f, 0.06f, 0.55f, 6, k.timber, false);
                    mb.Box(V(x, gy + h + 0.56, z), V(r * 1.25, 0.08, r * 1.25), Sh(k.timber, -0.2));
                    mb.ChineseRoof(V(x, gy + h + 0.6, z), (float)(r * 2.2), (float)(r * 2.2), 0.6f, k.roof);
                }
                return;
            }
            if (fam == "timber")
            {
                foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 }) mb.SmoothCylinder(V(x + sx * r * 0.6, gy, z + sz * r * 0.6), 0.07f, 0.06f, (float)(h + 0.4), 6, k.timber, false);
                mb.Box(V(x, gy + h + 0.4, z), V(r * 1.6, 0.1, r * 1.6), Sh(k.timber, 0.05));
                for (int i = 0; i < 4; i++) { double a = i * Math.PI / 2; mb.Box(V(x + Math.Cos(a) * r * 0.78, gy + h + 0.62, z + Math.Sin(a) * r * 0.78), V(Math.Abs(Math.Sin(a)) * r * 1.5 + 0.06, 0.34, Math.Abs(Math.Cos(a)) * r * 1.5 + 0.06), Sh(k.timber, -0.06)); }
                mb.Lathe(V(x, gy + h + 0.8, z), new[] { P2(r * 1.15, 0, true), P2(r * 0.3, 0.55), P2(0, 0.72) }, 10, k.roof);
                return;
            }
            // 圆塔
            mb.Lathe(V(x, gy, z), new[] { P2(r * 1.1, 0), P2(r * 1.02, 0.25), P2(r, h), P2(r * 1.08, h + 0.04, true), P2(r * 1.08, h + 0.12) }, 14, k.stone, true);
            const int n = 10;
            for (int i = 0; i < n; i++)
            {
                double a = i * Math.PI * 2 / n, px = x + Math.Cos(a) * r * 0.95, pz = z + Math.Sin(a) * r * 0.95;
                if (fam == "mud") BattleLook.StepMerlon(mb, (float)px, (float)(gy + h + 0.12), (float)pz, 0.5f, k.stone);
                else mb.Box(V(px, gy + h + 0.24, pz), V(0.2, 0.24, 0.2), Sh(k.stone, 0.05), 0.06f);
            }
            if (roof && fam == "roman") mb.Lathe(V(x, gy + h + 0.12, z), new[] { P2(r * 0.82, 0, true), P2(r * 0.35, 0.5), P2(0, 0.78) }, 14, k.roof);
            else if (roof && fam == "mud") mb.Sphere(V(x, gy + h + 0.14, z), V(r * 0.55, r * 0.5, r * 0.55), k.roof, 1, new SphereOpt { flat = 0, top = 0.1f });
        }
        // 门楼：城门格上的缺口，两侧门楼 + 门扇（敞开，向内）
        static void FortGate(MeshBuilder mb, string fam, FortCol k, double gx, double gz, double tx, double tz, Vector2 outD, double gy, double h)
        {
            const double gap = 0.75;   // 缺口半宽
            double r = fam == "han" ? 0.5 : fam == "timber" ? 0.45 : 0.55;
            foreach (var s in new[] { -1, 1 })
            {
                double cx = gx + tx * (gap + r * 0.9) * s, cz = gz + tz * (gap + r * 0.9) * s;
                FortTower(mb, fam, k, cx, cz, gy, h + 0.45, r, true);
                // 门扇（敞开、贴在门洞内侧）
                double hx = gx + tx * gap * s - outD.x * 0.15, hz = gz + tz * gap * s - outD.y * 0.15;
                double lx = hx - outD.x * 0.5 - tx * 0.1 * s, lz = hz - outD.y * 0.5 - tz * 0.1 * s;
                mb.Quad(V(hx, gy, hz), V(hx, gy + h * 0.78, hz), V(lx, gy + h * 0.78, lz), V(lx, gy, lz), Sh(k.timber, -0.05));
                mb.Quad(V(lx, gy, lz), V(lx, gy + h * 0.78, lz), V(hx, gy + h * 0.78, hz), V(hx, gy, hz), Sh(k.timber, -0.18));
            }
            // 门洞两侧的门框与门槛
            mb.Box(V(gx, gy + 0.03, gz), V(Math.Abs(tx) * gap * 2 + Math.Abs(outD.x) * 0.9, 0.06, Math.Abs(tz) * gap * 2 + Math.Abs(outD.y) * 0.9), Sh(BattleLook.Has(k.cap) ? k.cap : k.stone, -0.1));
            if (fam == "roman" || fam == "mud")
            {
                // 拱门上方的拱楣（高处，不挡住门洞里的部队）
                double yb = gy + h + 0.2;
                mb.Box(V(gx, yb + 0.12, gz), V(Math.Abs(tx) * (gap * 2 + 0.2) + Math.Abs(outD.x) * 0.5, 0.24, Math.Abs(tz) * (gap * 2 + 0.2) + Math.Abs(outD.y) * 0.5), Sh(k.stone, 0.04), 0.05f);
            }
            else if (fam == "han")
            {
                // 门额（匾）
                mb.Box(V(gx + outD.x * 0.3, gy + h + 0.32, gz + outD.y * 0.3), V(Math.Abs(tx) * 0.62 + Math.Abs(outD.x) * 0.06, 0.26, Math.Abs(tz) * 0.62 + Math.Abs(outD.y) * 0.06), C(0.2, 0.18, 0.2), 0);
            }
        }
        // 汉式本阵：三层台基、红柱殿身、重檐金顶
        static void FortKeepHan(MeshBuilder mb, FortCol k, double cx, double gy, double cz, bool capital)
        {
            double z = cz + 0.45;
            mb.Box(V(cx, gy + 0.09, z), V(1.9, 0.18, 1.1), Sh(k.stone, 0.12));
            mb.Box(V(cx, gy + 0.24, z + 0.02), V(1.66, 0.14, 0.94), Sh(k.stone, 0.18));
            mb.Box(V(cx, gy + 0.08, z - 0.66), V(0.5, 0.16, 0.3), Sh(k.stone, 0.1));   // 前阶
            for (int i = -2; i <= 2; i++) mb.SmoothCylinder(V(cx + i * 0.34, gy + 0.31, z - 0.36), 0.045f, 0.045f, 0.62f, 6, k.timber, false);
            mb.Box(V(cx, gy + 0.62, z + 0.08), V(1.38, 0.62, 0.6), C(0.66, 0.22, 0.18));
            mb.Box(V(cx, gy + 0.6, z - 0.235), V(0.34, 0.5, 0.02), C(0.32, 0.18, 0.12));
            var gold = capital ? C(0.9, 0.7, 0.24) : C(0.85, 0.65, 0.2);
            mb.ChineseRoof(V(cx, gy + 0.93, z), 2.1f, 1.25f, 0.42f, gold);
            mb.Box(V(cx, gy + 1.15, z), V(1.0, 0.24, 0.48), C(0.62, 0.2, 0.16));
            mb.ChineseRoof(V(cx, gy + 1.3, z), 1.55f, 0.95f, 0.55f, gold);
        }
        void BuildFort(MeshBuilder mb)
        {
            var ca = castle; string st = look.style, fam = FortFamily(st); var k = FortColors(st);
            double gy = BASE_Y + 0.06;
            double cxU = Origin.x + ca.x * T + T / 2, czU = Origin.z + ca.y * T + T / 2, R = 2 * T;
            double h = fam == "timber" ? 1.25 : 1.45, w0 = fam == "timber" ? 1.3 : 1.12, w1 = fam == "timber" ? 0.8 : 0.9;
            // 内院铺地
            var pv = fam == "timber" ? C(0.5, 0.44, 0.32) : fam == "mud" ? Sh(k.stone, 0.06) : C(0.6, 0.58, 0.53);
            double ir = R - w0 / 2 + 0.05;
            mb.FlatQuad(V(cxU - ir, gy + 0.012, czU - ir), V(cxU - ir, gy + 0.012, czU + ir), V(cxU + ir, gy + 0.012, czU + ir), V(cxU + ir, gy + 0.012, czU - ir), pv);
            // 城墙（四边；城门处留缺口）
            Func<int, int, bool> isGate = (x, y) => x >= 0 && y >= 0 && x < W && y < H && m.map[x, y] == Terrain.Gate;
            var sides = new[]
            {
                new { a = V(cxU - R, 0, czU - R), b = V(cxU + R, 0, czU - R), outD = new Vector2(0, -1), gate = isGate(ca.x, ca.y - 2), g = V(cxU, 0, czU - R) },
                new { a = V(cxU + R, 0, czU - R), b = V(cxU + R, 0, czU + R), outD = new Vector2(1, 0), gate = isGate(ca.x + 2, ca.y), g = V(cxU + R, 0, czU) },
                new { a = V(cxU + R, 0, czU + R), b = V(cxU - R, 0, czU + R), outD = new Vector2(0, 1), gate = isGate(ca.x, ca.y + 2), g = V(cxU, 0, czU + R) },
                new { a = V(cxU - R, 0, czU + R), b = V(cxU - R, 0, czU - R), outD = new Vector2(-1, 0), gate = isGate(ca.x - 2, ca.y), g = V(cxU - R, 0, czU) },
            };
            double cr = fam == "han" ? 0.62 : fam == "timber" ? 0.5 : 0.7;   // 角楼半径（墙段在角楼处收短）
            foreach (var s in sides)
            {
                double L = Vector3.Distance(s.a, s.b), tx = (s.b.x - s.a.x) / L, tz = (s.b.z - s.a.z) / L;
                Vector3 A = V(s.a.x + tx * cr * 0.6, 0, s.a.z + tz * cr * 0.6), B = V(s.b.x - tx * cr * 0.6, 0, s.b.z - tz * cr * 0.6);
                if (s.gate)
                {
                    double gw = 0.75 + (fam == "han" ? 0.5 : 0.55) * 1.8;
                    Vector3 g1 = V(s.g.x - tx * gw, 0, s.g.z - tz * gw), g2 = V(s.g.x + tx * gw, 0, s.g.z + tz * gw);
                    FortWallRun(mb, A, g1, h, w0, w1, s.outD, fam, k, gy);
                    FortWallRun(mb, g2, B, h, w0, w1, s.outD, fam, k, gy);
                    FortGate(mb, fam, k, s.g.x, s.g.z, tx, tz, s.outD, gy, h);
                }
                else FortWallRun(mb, A, B, h, w0, w1, s.outD, fam, k, gy);
                // 墙段中间的马面（汉式）
                if (fam == "han" && !s.gate)
                {
                    double mx = (s.a.x + s.b.x) / 2.0 + s.outD.x * 0.55, mz = (s.a.z + s.b.z) / 2.0 + s.outD.y * 0.55;
                    mb.Box(V(mx, gy + h / 2, mz), V(Math.Abs(s.outD.x) * 0.5 + Math.Abs(tx) * 0.9, h, Math.Abs(s.outD.y) * 0.5 + Math.Abs(tz) * 0.9), Sh(k.stone, -0.03), 0.05f);
                }
            }
            foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 }) FortTower(mb, fam, k, cxU + sx * R, czU + sz * R, gy, h + 0.3, cr, true);
            // 本阵
            double kh = gy;
            if (st == "han" || !BattleLook.Keep(mb, st, (float)cxU, (float)kh, (float)czU)) FortKeepHan(mb, k, cxU, kh, czU, false);
            mb.Flag(V(cxU + 0.85, kh, czU + 0.85), 2.4f, 0.8f, 0.55f, defCol);
        }
        // 桥：道路过河处（规则上是平原格）
        void BuildBridges(MeshBuilder mb)
        {
            foreach (var b in bridgeList)
            {
                double cx = Origin.x + b.x * T + T / 2, cz = Origin.z + b.y * T + T / 2, y = WATER_Y + 0.34;
                bool ax = b.axis == "x";
                double L = T + 0.6, Wd = 1.0;
                double sx = ax ? L : Wd, sz = ax ? Wd : L;
                mb.Box(V(cx, y - 0.06, cz), V(sx, 0.12, sz), C(0.62, 0.6, 0.55), 0.04f);
                foreach (var s in new[] { -1, 1 })
                {
                    mb.Box(V(cx + (ax ? 0 : s * (Wd / 2 - 0.05)), y + 0.1, cz + (ax ? s * (Wd / 2 - 0.05) : 0)), V(ax ? L : 0.1, 0.2, ax ? 0.1 : L), C(0.56, 0.54, 0.5), 0.06f);
                    // 桥墩
                    mb.Box(V(cx + (ax ? s * 0.55 : 0), WATER_Y - 0.3, cz + (ax ? 0 : s * 0.55)), V(ax ? 0.3 : Wd * 0.9, 0.62, ax ? Wd * 0.9 : 0.3), C(0.5, 0.48, 0.44), 0.02f);
                }
            }
        }

        // ====================================================== 部队 --
        // 士兵的兵种与队形（部队局部坐标：网页版 three 的局部 (x, z)，前方为 −z；Unity 局部为 (x, −z)）
        public struct SoldierSlot { public double x, z; public string type; public bool mounted; }
        static readonly HashSet<string> FIG_CULTS = new HashSet<string> { "han", "nanman", "wa", "yi", "korea", "steppe", "seasia", "tarim", "kushan", "persia", "arab", "roman", "celt", "german", "sarmatian", "india", "africa", "moor" };
        public static string FigCult(string c)
        {
            if (c != null && FIG_CULTS.Contains(c)) return c;
            var b = Defs.CultureBase(c);
            return FIG_CULTS.Contains(b) ? b : "han";
        }
        public static bool MountedCult(string c) { return c == "steppe" || c == "sarmatian"; }
        public static List<SoldierSlot> SoldierSlots(string culture, int n)
        {
            var S = new List<SoldierSlot>();
            if (MountedCult(culture))
            {
                double[,] P = { { -0.5, 0.45 }, { 0.5, 0.45 }, { -0.5, 1.05 }, { 0.5, 1.05 } };
                for (int i = 0; i < n && i < 4; i++) S.Add(new SoldierSlot { x = P[i, 0], z = P[i, 1], type = "cavalry", mounted = true });
                return S;
            }
            double[,] Q = { { -0.52, 0.22 }, { 0.52, 0.22 }, { 0, 0.4 }, { -0.6, 0.72 }, { 0.6, 0.72 }, { 0, 0.86 } };
            string[] T6 = { "spear", "spear", "sword", "archer", "archer", "sword" };
            for (int i = 0; i < n && i < 6; i++) S.Add(new SoldierSlot { x = Q[i, 0] + ((i * 37) % 7 - 3) * 0.012, z = Q[i, 1], type = T6[i], mounted = false });
            return S;
        }
        // 兵力 → 士兵数（第三版：每 900 一人，至多 6；草原骑兵按 4/6 折算）
        public static int SoldierBucket(int troops, bool alive) { return (int)Clamp(Math.Ceiling(troops / 900.0), alive ? 1 : 0, 6); }
        public static int SoldierCount(int bucket, string culture) { return MountedCult(culture) ? (int)Math.Ceiling(bucket * 4 / 6.0) : bucket; }
        // 部队的初始朝向（度，Unity 绕 Y）：攻方朝东（+x）、守方朝西
        public static float UnitYaw(int side) { return side == 0 ? 90 : -90; }
        // 主将战马的根在部队局部坐标里的位置：网页版 h.root.position = (0, 0, −0.36)（前方为 −z）→ Unity (0, 0, 0.36)；
        // 人物根（figRoot）缩放 FIG_S，战马根的局部位置因此为 0.36 / FIG_S（网页版 h.root.position.multiplyScalar(1 / FIG_S)）
        public const float HORSE_Z = 0.36f;
        public static Vector3 HorseLocal { get { return new Vector3(0, 0, HORSE_Z / FIG_S); } }
        // 主将战马的根在世界坐标里的矩阵（部队位置 gp、朝向 yaw 度；= 部队 × figRoot × horse.Root，供无头对照）
        public static Matrix4x4 HorseRootMatrix(Vector3 gp, float yaw)
        {
            return Matrix4x4.TRS(gp, Quaternion.Euler(0, yaw, 0), new Vector3(FIG_S, FIG_S, FIG_S)) * Matrix4x4.TRS(HorseLocal, Quaternion.identity, Vector3.one);
        }
        // 士兵的世界位置（网页版 UnitVisual._placeSoldiers）：部队位置 gp、朝向 yaw（度）、队形偏移 (x, z)（网页版局部，前方 −z → Unity 局部 (x, −z)）；
        // 士兵贴在自己脚下的地表上，部队高出地表的部分（城墙上、桥上）一并抬高
        public Vector3 SoldierPos(Vector3 gp, float yaw, double x, double z)
        {
            float lift = gp.y - (float)GroundY(gp.x, gp.z);
            var w = gp + Quaternion.Euler(0, yaw, 0) * new Vector3((float)x, 0, (float)-z);
            return new Vector3(w.x, (float)GroundY(w.x, w.z) + Math.Max(0, lift), w.z);
        }
    }
}
