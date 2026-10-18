// 三国志-霸王的世界 · 各文化的城池模型（DESIGN-V2 §4G、§6；第三版 DESIGN-V3 §1：平滑、细致）← 网页版 js/culture-art.js
//
// 第三版：圆形构件（塔、穹顶、窣堵波、毡帐、立柱、圆锥顶、山丘）一律改为平滑旋转体 / 椭球（MeshBuilder.Lathe / Sphere / Tube）；
// 汉地城池不再用 Models.CityInto，改为 Han（女墙垛口、马面、两层角楼、重檐城楼、三层台基的重檐主殿、都城的阙楼与金顶，
// 曲面屋顶来自 MeshBuilder.ChineseRoof）；另有远景简化模型 CityFarInto（地图在镜头较远时替换）。
// 罗马（平滑圆塔 + 圆锥瓦顶、半圆拱门、带前阶与山花的列柱神庙、都城为三层拱廊的椭圆竞技场）、安息 / 波斯（尖拱门楼与釉砖框、
// 伊万宫殿的尖拱 + 筒拱大厅、拜火祠穹顶）、阿拉伯（平滑圆城墙与雉堞环）、贵霜（带栏楯与四面塔门的窣堵波、拱门僧院、都城穹顶宫殿）；
// 新增文化：india 天竺、africa 非洲（库施诸城 / 阿克苏姆诸城）、moor 毛里（巴夸特）。其他未列出的文化按 WorldGeo.CultureBase 回退。
// 墙面砖缝、屋面瓦垄由 Sanguo/Masonry（Art.MasonryMaterial）在着色器里按世界坐标生成，这里只写顶点色。
//
// 约定：Unity 坐标（x 东、z 北、y 上），size ≈ 占地半径 r，底座 2.3r × 2.3r，城墙在 ±r，城门朝南（−z）；
// 旗帜由 MapView 另画在 (0.9r, 0, 0.9r) 一角（东北），该角保持空旷；标签高度 size × 1.6 + 0.6。capital = true 时为都城变体。
// 全部写入调用方的 MeshBuilder（按区块合并网格），各模型的顶点数与网页版逐一相同（Tests~/map 比对）。
//
// 公开接口
//   CultureArt.Cultures                                   已有模型的文化代号（不含 han）
//   CultureArt.CultureOfCity(city)                        city.culture → "han"
//   CultureArt.IsCapital(city, culture)                   汉地：洛阳、长安；其他文化：世界剧本各势力的初始都城（WorldScenarioData.FactionInfo）
//   CultureArt.CityInto(mb, size, culture, capital, city) 写入 MeshBuilder；未知文化按 CultureBase 回退，再不行按汉地
//   CultureArt.CityFarInto(mb, size, culture, capital)    远景简化模型（约 300–600 个三角形，轮廓与配色一致）
//   CultureArt.City(size, culture, capital, city)         单独一座城的网格（测试 / 预览用）
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sanguo
{
    public static class CultureArt
    {
        static Color H(string hex) { return Art.Hex(hex); }
        static Color Sh(Color c, float k) { return Art.Shade(c, k); }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static LP L(float r, float y, bool sharp = false) { return new LP(r, y, sharp); }
        static LP L(float r, float y, Color c, bool sharp = false) { return new LP(r, y, c, sharp); }
        static Vector2 Z(float x, float z) { return new Vector2(x, z); }
        const float PI = Mathf.PI;

        // ============================================================ 基本形体 --
        // 凸多边形面：按 hint（大致的外向方向）自动定正反（MeshBuilder 为单面）
        static void Face(MeshBuilder mb, Vector3[] pts, Color c, Vector3? hint = null)
        {
            if (hint.HasValue)
            {
                var n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]);
                if (Vector3.Dot(n, hint.Value) < 0) { pts = (Vector3[])pts.Clone(); Array.Reverse(pts); }
            }
            for (int i = 1; i + 1 < pts.Length; i++) mb.Tri(pts[0], pts[i], pts[i + 1], c);
        }
        // 双面薄片（旗、叶）
        static void Sheet(MeshBuilder mb, Vector3[] pts, Color c)
        {
            for (int i = 1; i + 1 < pts.Length; i++) { mb.Tri(pts[0], pts[i], pts[i + 1], c); mb.Tri(pts[0], pts[i + 1], pts[i], c); }
        }
        // 任意朝向的墙段：从 (ax, az) 到 (bx, bz)，厚 t，底 y0，高 h；可选每端高度不同（h2）
        static void Seg(MeshBuilder mb, float ax, float az, float bx, float bz, float y0, float h, float t, Color c, float h2 = float.NaN)
        {
            float dx = bx - ax, dz = bz - az, Ln = Mathf.Sqrt(dx * dx + dz * dz); if (Ln == 0) Ln = 1;
            float nx = -dz / Ln * t / 2, nz = dx / Ln * t / 2;
            float hb = float.IsNaN(h2) ? h : h2;
            var p = new[] { V(ax + nx, y0, az + nz), V(bx + nx, y0, bz + nz), V(bx - nx, y0, bz - nz), V(ax - nx, y0, az - nz) };
            var q = new[] { V(ax + nx, y0 + h, az + nz), V(bx + nx, y0 + hb, bz + nz), V(bx - nx, y0 + hb, bz - nz), V(ax - nx, y0 + h, az - nz) };
            var ctr = V((ax + bx) / 2, y0 + (h + hb) / 4, (az + bz) / 2);
            Face(mb, new[] { q[0], q[1], q[2], q[3] }, Sh(c, 0.08f), Vector3.up);
            for (int i = 0; i < 4; i++)
            {
                var f = new[] { p[i], p[(i + 1) % 4], q[(i + 1) % 4], q[i] };
                var o = (f[0] + f[1] + f[2] + f[3]) / 4 - ctr;
                Face(mb, f, c, o);
            }
        }
        // 双坡屋顶（脊沿 x 轴；alongZ 时脊沿 z 轴）：中心 (x, y, z)，宽 w（沿脊）、深 d、高 h，出檐 e
        static void Gable(MeshBuilder mb, float x, float y, float z, float w, float d, float h, Color c, bool alongZ, float e = 0)
        {
            float hw = w / 2 + e, hd = d / 2 + e;
            Func<float, float, float, Vector3> P = (a, b, yy) => alongZ ? V(x + b, yy, z + a) : V(x + a, yy, z + b);
            Vector3 r0 = P(-hw, 0, y + h), r1 = P(hw, 0, y + h);
            Vector3 s0 = P(-hw, -hd, y), s1 = P(hw, -hd, y), n0 = P(-hw, hd, y), n1 = P(hw, hd, y);
            var sideA = alongZ ? V(-1, 0.6f, 0) : V(0, 0.6f, -1); var sideB = alongZ ? V(1, 0.6f, 0) : V(0, 0.6f, 1);
            Face(mb, new[] { s0, s1, r1, r0 }, c, sideA);
            Face(mb, new[] { n0, r0, r1, n1 }, Sh(c, -0.12f), sideB);
            var endA = alongZ ? V(0, 0, -1) : V(-1, 0, 0); var endB = alongZ ? V(0, 0, 1) : V(1, 0, 0);
            Face(mb, new[] { s0, r0, n0 }, Sh(c, -0.25f), endA);
            Face(mb, new[] { s1, n1, r1 }, Sh(c, -0.25f), endB);
        }
        // 四坡（攒尖）顶：底面 w × d，顶点高 h；ridge > 0 时为庑殿（脊长 ridge × w）
        static void Hip(MeshBuilder mb, float x, float y, float z, float w, float d, float h, Color c, float ridge = 0)
        {
            float hw = w / 2, hd = d / 2, rr = ridge * w / 2;
            Vector3 a = V(x - hw, y, z - hd), b = V(x + hw, y, z - hd), cc = V(x + hw, y, z + hd), dd = V(x - hw, y, z + hd);
            Vector3 t0 = V(x - rr, y + h, z), t1 = V(x + rr, y + h, z);
            Face(mb, rr > 0 ? new[] { a, b, t1, t0 } : new[] { a, b, t0 }, c, V(0, 0.5f, -1));
            Face(mb, rr > 0 ? new[] { cc, dd, t0, t1 } : new[] { cc, dd, t0 }, Sh(c, -0.12f), V(0, 0.5f, 1));
            Face(mb, new[] { b, cc, t1 }, Sh(c, -0.06f), V(1, 0.5f, 0));
            Face(mb, new[] { dd, a, t0 }, Sh(c, -0.18f), V(-1, 0.5f, 0));
        }
        // 平滑穹顶（旋转体，逐顶点法线）
        static void Dome(MeshBuilder mb, float x, float y, float z, float r, Color c, int seg = 12, float squash = 1)
        {
            seg = Math.Max(12, seg); if (squash == 0) squash = 1;
            var prof = new LP[7];
            for (int i = 0; i <= 6; i++) { float a = i / 6f * PI / 2; prof[i] = L(Mathf.Cos(a) * r, Mathf.Sin(a) * r * squash, Sh(c, i * 0.025f)); }
            mb.Lathe(V(x, y, z), prof, seg, c);
            mb.Lathe(V(x, y + r * squash * 0.98f, z), new[] { L(0.05f * r, 0), L(0.02f * r, 0.14f * r + 0.02f), L(0, 0.17f * r + 0.02f) }, 6, Sh(c, 0.12f));
        }
        // 圆柱（柱身 + 柱础 + 柱头）
        static void Column(MeshBuilder mb, float x, float y, float z, float h, float r, Color c)
        {
            mb.Lathe(V(x, y, z), new[] { L(r * 1.3f, 0), L(r * 1.3f, r * 0.5f, true), L(r, r * 0.6f, true), L(r * 0.9f, h - r * 0.4f), L(r * 1.25f, h, true) }, 8, c, true);
            mb.Box(V(x, y + h + r * 0.35f, z), V(r * 2.7f, r * 0.7f, r * 2.7f), Sh(c, 0.05f));
        }
        // 平滑圆柱 / 圆锥（取代低段数的 Cylinder / Cone）
        static void Cyl(MeshBuilder mb, Vector3 b, float r0, float r1, float h, int seg, Color c, bool cap = true) { mb.SmoothCylinder(b, r0, r1, h, Math.Max(10, WorldGeo.JsRound(seg * 1.5)), c, cap); }
        static void Cone(MeshBuilder mb, Vector3 b, float r, float h, int seg, Color c) { mb.Lathe(b, new[] { L(0, 0.02f * h), L(r, 0, true), L(0, h) }, Math.Max(8, WorldGeo.JsRound(seg * 1.5)), c); }
        // 圆塔：塔身（略收分）+ 雉堞 + 尖顶 / 平顶（roofC == null）
        static void Tower(MeshBuilder mb, float x, float z, float r, float h, Color c, Color? roofC, float y0 = 0)
        {
            mb.Lathe(V(x, y0, z), new[] { L(r * 1.06f, 0), L(r, h * 0.15f), L(r * 0.96f, h, true), L(r * 1.12f, h, true), L(r * 1.12f, h + r * 0.25f) }, 14, c, !roofC.HasValue);
            if (roofC.HasValue) mb.Lathe(V(x, y0 + h + r * 0.25f, z), new[] { L(0, 0.02f), L(r * 1.2f, 0, true), L(r * 0.5f, r * 0.8f), L(0, r * 1.5f) }, 14, roofC.Value);
        }
        // 一圈城墙（正多边形）：中心 (0, 0)，半径 R，n 边，高 h，厚 t
        static void RingWall(MeshBuilder mb, float R, int n, float h, float t, Color c, float rot = 0, float y0 = 0)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = rot + i * PI * 2 / n, a1 = rot + (i + 1) * PI * 2 / n;
                Seg(mb, Mathf.Cos(a0) * R, Mathf.Sin(a0) * R, Mathf.Cos(a1) * R, Mathf.Sin(a1) * R, y0, h, t, c);
            }
        }
        // 方形城墙 + 垛口（merlon = 垛口大小，0 = 无）
        static void SquareWall(MeshBuilder mb, float r, float h, float t, Color c, float merlon, int step = 3)
        {
            mb.Box(V(0, h / 2, -r), V(r * 2, h, t), c);
            mb.Box(V(0, h / 2, r), V(r * 2, h, t), c);
            mb.Box(V(-r, h / 2, 0), V(t, h, r * 2), c);
            mb.Box(V(r, h / 2, 0), V(t, h, r * 2), c);
            if (merlon > 0)
            {
                int n = step; var top = Sh(c, 0.06f);
                for (int i = -n; i <= n; i++)
                {
                    float x = i * r / (n + 0.5f);
                    if (i == 0) continue;   // 城门上方留空
                    mb.Box(V(x, h + merlon / 2, -r), V(merlon * 1.1f, merlon, t * 1.05f), top);
                    mb.Box(V(x, h + merlon / 2, r), V(merlon * 1.1f, merlon, t * 1.05f), top);
                    mb.Box(V(-r, h + merlon / 2, x), V(t * 1.05f, merlon, merlon * 1.1f), top);
                    mb.Box(V(r, h + merlon / 2, x), V(t * 1.05f, merlon, merlon * 1.1f), top);
                }
            }
        }
        // 木栅：沿折线 pts（(x, z)…，闭合）的栅墙 + 尖桩；h 高，n 每单位长度的尖桩数
        static void Palisade(MeshBuilder mb, Vector2[] pts, float h, Color c, int n = 5, float y0 = 0)
        {
            const float t = 0.05f;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
                Seg(mb, a.x, a.y, b.x, b.y, y0, h, t, c);
                float dx = b.x - a.x, dz = b.y - a.y, Ln = Mathf.Sqrt(dx * dx + dz * dz);
                int k = Math.Max(2, WorldGeo.JsRound(n * Ln));
                for (int j = 0; j < k; j++)
                {
                    float u0 = (float)j / k, u1 = (float)(j + 1) / k, um = (u0 + u1) / 2;
                    Sheet(mb, new[] { V(a.x + dx * u0, y0 + h, a.y + dz * u0), V(a.x + dx * um, y0 + h + 0.09f, a.y + dz * um), V(a.x + dx * u1, y0 + h, a.y + dz * u1) }, Sh(c, 0.05f));
                }
            }
        }
        // 环壕：平放的水面方环（内半径 ri，外半径 ro）
        static void Moat(MeshBuilder mb, float ri, float ro, float y, Color c)
        {
            var up = Vector3.up;
            Face(mb, new[] { V(-ro, y, -ro), V(ro, y, -ro), V(ro, y, -ri), V(-ro, y, -ri) }, c, up);
            Face(mb, new[] { V(-ro, y, ri), V(ro, y, ri), V(ro, y, ro), V(-ro, y, ro) }, c, up);
            Face(mb, new[] { V(-ro, y, -ri), V(-ri, y, -ri), V(-ri, y, ri), V(-ro, y, ri) }, c, up);
            Face(mb, new[] { V(ri, y, -ri), V(ro, y, -ri), V(ro, y, ri), V(ri, y, ri) }, c, up);
        }
        // 椰树 / 椰枣树（平滑弯干 + 三段下垂的弧形羽叶）
        static void Palm(MeshBuilder mb, float x, float z, float s, int seed, float y0 = 0)
        {
            var rnd = new SeededRandom(seed);
            float h = s * (0.75f + (float)rnd.NextDouble() * 0.3f), lean = ((float)rnd.NextDouble() - 0.5f) * 0.12f * s;
            var top = V(x + lean, y0 + h, z + lean * 0.4f);
            mb.Tube(new[] { V(x, y0, z), V(x + lean * 0.3f, y0 + h * 0.45f, z + lean * 0.1f), top }, new[] { 0.035f * s, 0.03f * s, 0.024f * s }, 6, H("#8a6a44"));
            Color leaf = H("#3f7a34"); var up = Vector3.up; const int nf = 8;
            for (int i = 0; i < nf; i++)
            {
                float a = i * PI * 2 / nf + (float)rnd.NextDouble() * 0.35f, ca = Mathf.Cos(a), sa = Mathf.Sin(a), Lf = (0.34f + (float)rnd.NextDouble() * 0.06f) * s;
                var c = Sh(leaf, (i % 2) * 0.08f - 0.02f); var n = V(ca * 0.25f, 1, sa * 0.25f).normalized;
                Vector3 pl = Vector3.zero, pr = Vector3.zero, pc = top; bool first = true;
                for (int k = 1; k <= 3; k++)
                {
                    float u = k / 3f, Ll = Lf * u, droop = (0.1f * u - 0.36f * u * u) * s, w = 0.075f * s * Mathf.Sin(PI * Mathf.Min(1, u * 1.1f));
                    var cc = V(top.x + ca * Ll, top.y + droop, top.z + sa * Ll);
                    var l = V(cc.x - sa * w, cc.y - 0.014f * s, cc.z + ca * w); var r = V(cc.x + sa * w, cc.y - 0.014f * s, cc.z - ca * w);
                    if (first) { mb.TriN(pc, l, cc, up, n, n, c); mb.TriN(pc, cc, r, up, n, n, c); first = false; }
                    else { mb.TriN(pl, l, cc, n, n, n, c); mb.TriN(pl, cc, pc, n, n, n, c); mb.TriN(pc, cc, r, n, n, n, c); mb.TriN(pc, r, pr, n, n, n, c); }
                    pl = l; pr = r; pc = cc;
                }
            }
            mb.Sphere(V(top.x, top.y - 0.01f * s, top.z), V(0.035f * s, 0.03f * s, 0.035f * s), H("#6a5a30"), 1, new SphereOpt { bottomShade = 0.3f });
        }
        // 钻天杨
        static void Poplar(MeshBuilder mb, float x, float z, float s, float y0 = 0)
        {
            mb.Lathe(V(x, y0, z), new[] { L(0.03f * s, 0), L(0.022f * s, 0.22f * s) }, 6, H("#7a6448"));
            mb.Sphere(V(x, y0 + 0.5f * s, z), V(0.1f * s, 0.38f * s, 0.1f * s), H("#5c8a3a"), 1, new SphereOpt { seed = (int)(x * 97 + z * 31), rough = 0.06f, bottomShade = 0.25f });
        }
        // 普通树（圆冠）
        static void Tree(MeshBuilder mb, float x, float z, float s, Color c, float y0 = 0)
        {
            Models.Tree(mb, V(x, y0, z), s, c, false, (int)(x * 53 + z * 17));
        }
        // 平顶房（土坯 / 石砌）
        static void FlatHouse(MeshBuilder mb, float x, float z, float w, float d, float h, Color c, float y0 = 0)
        {
            mb.Box(V(x, y0 + h / 2, z), V(w, h, d), c, 0.12f);
            mb.Box(V(x, y0 + h + 0.015f, z), V(w * 1.02f, 0.03f, d * 1.02f), Sh(c, 0.1f));
            mb.Box(V(x, y0 + h * 0.3f, z - d / 2 - 0.004f), V(w * 0.22f, h * 0.6f, 0.01f), Sh(c, -0.6f));   // 门洞
        }
        // 坡顶房（墙 + 双坡顶）
        static void GableHouse(MeshBuilder mb, float x, float z, float w, float d, float h, Color wallC, Color roofC, bool alongZ, float y0 = 0, float rh = 0)
        {
            mb.Box(V(x, y0 + h / 2, z), V(w, h, d), wallC, 0.1f);
            Gable(mb, x, y0 + h, z, w, d, rh != 0 ? rh : Mathf.Min(w, d) * 0.45f, roofC, alongZ, Mathf.Min(w, d) * 0.08f);
        }
        // 高脚屋：四柱 + 地板 + 墙 + 陡坡顶（saddle = 鞍形翘脊）
        static void StiltHouse(MeshBuilder mb, float x, float z, float w, float d, float legH, float h, Color wallC, Color roofC, bool saddle, float y0 = 0)
        {
            var post = H("#5a4630");
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) mb.Box(V(x + sx * w * 0.4f, y0 + legH / 2, z + sz * d * 0.4f), V(0.035f, legH, 0.035f), post);
            mb.Box(V(x, y0 + legH + 0.015f, z), V(w * 1.05f, 0.03f, d * 1.05f), Sh(post, 0.15f));
            mb.Box(V(x, y0 + legH + 0.03f + h / 2, z), V(w * 0.9f, h, d * 0.9f), wallC, 0.1f);
            float ry = y0 + legH + 0.03f + h;
            Gable(mb, x, ry, z, w, d, Mathf.Max(w, d) * 0.6f, roofC, d > w, 0.05f);
            if (saddle)
            {
                // 鞍形屋脊：两端上翘的尖角
                bool along = d > w;
                float Ln = (along ? d : w) / 2 + 0.06f, rh = Mathf.Max(w, d) * 0.6f;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    var bs = along ? V(x, ry + rh, z + sgn * Ln * 0.8f) : V(x + sgn * Ln * 0.8f, ry + rh, z);
                    var tip = along ? V(x, ry + rh + 0.14f, z + sgn * (Ln + 0.1f)) : V(x + sgn * (Ln + 0.1f), ry + rh + 0.14f, z);
                    var side = along ? V(0.04f, 0, 0) : V(0, 0, 0.04f);
                    Sheet(mb, new[] { bs - side, tip, bs + side }, Sh(roofC, -0.1f));
                }
            }
        }
        // 毡帐（蒙古包）
        static void Yurt(MeshBuilder mb, float x, float z, float r, Color felt, float y0 = 0, Color? roofC = null)
        {
            mb.Lathe(V(x, y0, z), new[] { L(r, 0), L(r, r * 0.66f, true) }, 14, felt);
            mb.Lathe(V(x, y0 + r * 0.64f, z), new[] { L(r * 1.04f, 0, true), L(r * 0.75f, r * 0.22f), L(r * 0.3f, r * 0.4f), L(r * 0.18f, r * 0.44f, true), L(0, r * 0.46f) }, 14, roofC ?? Sh(felt, -0.08f));
            for (int i = 0; i < 3; i++) mb.Lathe(V(x, y0 + r * (0.12f + i * 0.22f), z), new[] { L(r * 1.012f, 0), L(r * 1.012f, r * 0.025f) }, 14, Sh(felt, -0.25f));
            mb.Box(V(x, y0 + r * 0.25f, z - r * 0.98f), V(r * 0.35f, r * 0.5f, 0.02f), H("#a0442a"));   // 门（朝南）
        }
        // 尖顶帐
        static void Tent(MeshBuilder mb, float x, float z, float r, Color c, float y0 = 0)
        {
            mb.Lathe(V(x, y0, z), new[] { L(r, 0, true), L(r * 0.45f, r * 0.8f), L(0.02f * r, r * 1.5f, true), L(0, r * 1.62f) }, 10, c);
        }
        // 马尾纛 / 竿旗：竿 + 顶饰 + 垂缨（或小旗）
        static void Standard(MeshBuilder mb, float x, float z, float h, Color c, string kind, float y0 = 0)
        {
            mb.Box(V(x, y0 + h / 2, z), V(0.03f, h, 0.03f), H("#5a4026"));
            if (kind == "tail")
            {
                Cone(mb, V(x, y0 + h - 0.22f, z), 0.07f, 0.24f, 5, c);
                Cone(mb, V(x, y0 + h, z), 0.025f, 0.08f, 4, H("#d8b050"));
            }
            else if (kind == "vexillum")
            {
                mb.Box(V(x, y0 + h - 0.05f, z), V(0.22f, 0.02f, 0.02f), H("#5a4026"));
                Sheet(mb, new[] { V(x - 0.11f, y0 + h - 0.06f, z), V(x + 0.11f, y0 + h - 0.06f, z), V(x + 0.11f, y0 + h - 0.3f, z), V(x - 0.11f, y0 + h - 0.3f, z) }, c);
                Cone(mb, V(x, y0 + h, z), 0.04f, 0.1f, 4, H("#d8b050"));
            }
            else
            {
                Sheet(mb, new[] { V(x + 0.015f, y0 + h, z), V(x + 0.26f, y0 + h - 0.04f, z), V(x + 0.015f, y0 + h - 0.18f, z) }, c);
            }
        }
        // 底座：两层台基（外缘略暗、内层略亮）
        static void Base(MeshBuilder mb, float r, Color c)
        {
            mb.Box(V(0, 0.035f, 0), V(r * 2.34f, 0.07f, r * 2.34f), Sh(c, -0.12f));
            mb.Box(V(0, 0.08f, 0), V(r * 2.2f, 0.04f, r * 2.2f), c);
        }
        // 城门：门洞（深色）+ 两侧门楼
        static void Gateway(MeshBuilder mb, float r, float w, float h, Color wallC, Color towerC, bool round)
        {
            mb.Box(V(0, h * 0.4f, -r - 0.012f), V(w, h * 0.8f, 0.06f), H("#2a1c12"));
            for (int sx = -1; sx <= 1; sx += 2)
            {
                if (round) Cyl(mb, V(sx * w * 0.95f, 0, -r), w * 0.42f, w * 0.4f, h * 1.3f, 8, towerC, true);
                else mb.Box(V(sx * w * 0.95f, h * 0.65f, -r), V(w * 0.75f, h * 1.3f, w * 0.75f), towerC);
            }
        }

        // ------------------------------------------- 拱、椭圆环、筒拱、墙带 --
        // 拱形门洞 / 窗洞：贴在朝向 (nx, nz)（水平单位向量）的立面上，(x, y0, z) 为立面上的底边中点，宽 w、总高 h；
        // pointed = 尖拱（安息 / 波斯式）。半圆 / 尖拱部分为 10 段平滑弧线
        static void Arch(MeshBuilder mb, float x, float y0, float z, float w, float h, float nx, float nz, Color c, bool pointed)
        {
            float tx = -nz, tz = nx, o = 0.004f, r = w / 2;
            Func<float, float, Vector3> P = (u, y) => V(x + tx * u + nx * o, y0 + y, z + tz * u + nz * o);
            float rise = pointed ? r * 1.35f : r, spring = Mathf.Max(0, h - rise);
            var pts = new List<Vector3> { P(-r, 0), P(r, 0), P(r, spring) };
            const int N = 10;
            for (int i = 1; i < N; i++)
            {
                float a = (float)i / N * PI, cu = Mathf.Cos(a);
                float y = pointed ? rise * Mathf.Pow(Mathf.Max(0, 1 - Mathf.Abs(cu)), 0.55f) * (Mathf.Sin(a) > 0 ? 1 : 0) : Mathf.Sin(a) * r;
                pts.Add(P(cu * r, spring + y));
            }
            pts.Add(P(-r, spring));
            Face(mb, pts.ToArray(), c, V(nx, 0, nz));
        }
        // 拱框（门洞外一圈浅色券石）：比门洞宽 2·b
        static void ArchFrame(MeshBuilder mb, float x, float y0, float z, float w, float h, float nx, float nz, Color c, bool pointed, float b)
        {
            Arch(mb, x - nx * 0.002f, y0, z - nz * 0.002f, w + 2 * b, h + b, nx, nz, c, pointed);
        }
        // 平滑椭圆环（竞技场、圆城）：中心 (cx, cz)，外缘半轴 rx × rz，壁厚 t，底 y0，高 h；外壁、内壁、顶面，逐顶点法线
        static void EllipseRing(MeshBuilder mb, float cx, float cz, float rx, float rz, float y0, float h, float t, Color c, Color? cIn, int n, bool noInner = false)
        {
            Func<float, float, float, Vector3> P = (a, off, y) => V(cx + Mathf.Cos(a) * (rx - off), y, cz + Mathf.Sin(a) * (rz - off));
            Func<float, float, Vector3> N = (a, k) => { float x = Mathf.Cos(a) / rx, z = Mathf.Sin(a) / rz, l = Mathf.Sqrt(x * x + z * z); return V(k * x / l, 0, k * z / l); };
            var up = Vector3.up; var top = Sh(c, 0.07f); var ci = cIn ?? c;
            for (int i = 0; i < n; i++)
            {
                float a0 = (float)i / n * PI * 2, a1 = (float)(i + 1) / n * PI * 2;
                mb.TriN(P(a0, 0, y0), P(a1, 0, y0), P(a1, 0, y0 + h), N(a0, 1), N(a1, 1), N(a1, 1), c);
                mb.TriN(P(a0, 0, y0), P(a1, 0, y0 + h), P(a0, 0, y0 + h), N(a0, 1), N(a1, 1), N(a0, 1), c);
                if (!noInner)
                {
                    mb.TriN(P(a0, t, y0), P(a1, t, y0 + h), P(a1, t, y0), N(a0, -1), N(a1, -1), N(a1, -1), ci);
                    mb.TriN(P(a0, t, y0), P(a0, t, y0 + h), P(a1, t, y0 + h), N(a0, -1), N(a0, -1), N(a1, -1), ci);
                }
                mb.TriN(P(a0, 0, y0 + h), P(a1, 0, y0 + h), P(a1, t, y0 + h), up, up, up, top);
                mb.TriN(P(a0, 0, y0 + h), P(a1, t, y0 + h), P(a0, t, y0 + h), up, up, up, top);
            }
        }
        // 筒拱（半圆柱，轴沿 z）：跨中心 (x, y, z0→z1)，半径 r；后端半圆封口
        static void Vault(MeshBuilder mb, float x, float y, float z0, float z1, float r, Color c, int n = 12)
        {
            var back = V(0, 0, z1 > z0 ? 1 : -1); var bc = Sh(c, -0.15f);
            Func<float, float, Vector3> p = (a, z) => V(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r, z);
            Func<float, Vector3> nn = a => V(Mathf.Cos(a), Mathf.Sin(a), 0);
            for (int i = 0; i < n; i++)
            {
                float a0 = (float)i / n * PI, a1 = (float)(i + 1) / n * PI;
                mb.TriN(p(a0, z0), p(a1, z0), p(a1, z1), nn(a0), nn(a1), nn(a1), c);
                mb.TriN(p(a0, z0), p(a1, z1), p(a0, z1), nn(a0), nn(a1), nn(a0), c);
                mb.TriN(V(x, y, z1), p(a0, z1), p(a1, z1), back, back, back, bc);
            }
        }
        // 墙带：方城四面的墙顶檐线与墙脚勒脚（略宽于墙身）
        static void WallBands(MeshBuilder mb, float r, float h, float t, Color c)
        {
            var c2 = Sh(c, -0.08f);
            var sides = new[] { new[] { 0, -r, r * 2 + t, t }, new[] { 0, r, r * 2 + t, t }, new[] { -r, 0, t, r * 2 + t }, new[] { r, 0, t, r * 2 + t } };
            foreach (var s in sides)
            {
                float x = s[0], z = s[1], w = s[2], d = s[3];
                float kx = w > d ? 1 : 1.25f, kz = d > w ? 1 : 1.25f;
                mb.Box(V(x, h - 0.022f * r, z), V(w * kx, 0.03f * r, d * kz), c, 0.1f);
                mb.Box(V(x, 0.13f * r, z), V(w * (w > d ? 1 : 1.18f), 0.08f * r, d * (d > w ? 1 : 1.18f)), c2, 0.04f);
            }
        }
        // 圆形雉堞环（圆塔、圆城墙顶）：n 个垛
        static void MerlonRing(MeshBuilder mb, float x, float z, float R, float y, int n, float w, float hh, float t, Color c)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) / n * PI * 2, a0 = a - w / R / 2, a1 = a + w / R / 2;
                Seg(mb, x + Mathf.Cos(a0) * R, z + Mathf.Sin(a0) * R, x + Mathf.Cos(a1) * R, z + Mathf.Sin(a1) * R, y, hh, t, c);
            }
        }

        // ============================================================ 各文化 --
        // ------------------------------------------------------------ 汉地 --
        // 夯土包砖的方城，全周女墙垛口与马面，四角两层角楼，南门重檐城楼（东西小门楼），城内三层台基上的重檐主殿、
        // 几排白墙灰瓦的坊屋与庭树；都城（洛阳、长安）主殿为金顶、殿前一对阙楼
        static void Han(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.44f * s, t = 0.2f * s;
            Color stone = H("#bdb4a0"), stone2 = H("#a8a08c"), top = H("#cfc7b4"), red = H("#9a3226"), roof = H("#465163"), gold = H("#c9962e");
            Color plaster = H("#e9e1cf"), dark = H("#2a1c12");
            Base(mb, r, H("#a0977f"));
            mb.Box(V(0, 0.104f, 0), V(r * 1.8f, 0.012f, r * 1.8f), H("#b3aa92"));   // 城内地面
            // 城墙（墙身略收分：底宽顶窄两段）
            foreach (var q in new[] { new[] { 0, -r, r * 2 + t, t }, new[] { 0, r, r * 2 + t, t }, new[] { -r, 0, t, r * 2 + t }, new[] { r, 0, t, r * 2 + t } })
            {
                float x = q[0], z = q[1], w = q[2], d = q[3];
                mb.Box(V(x, wallH * 0.3f, z), V(w * (w > d ? 1 : 1.12f), wallH * 0.6f, d * (d > w ? 1 : 1.12f)), stone2, 0.04f);
                mb.Box(V(x, wallH * 0.8f, z), V(w, wallH * 0.4f, d), stone, 0.08f);
            }
            // 女墙垛口（城门处留空，东北角留给旗帜）
            float mer = 0.085f * s; const int n = 4;
            for (int i = -n; i <= n; i++)
            {
                float u = i * r / (n + 0.5f);
                foreach (var q in new[] { new[] { u, -r - t * 0.38f, 1 }, new[] { u, r + t * 0.38f, 1 }, new[] { -r - t * 0.38f, u, 0 }, new[] { r + t * 0.38f, u, 0 } })
                {
                    float x = q[0], z = q[1]; bool ax = q[2] != 0;
                    if (Mathf.Abs(u) < 0.2f * s && (z < 0 || !ax)) continue;
                    if (x > 0.62f * r && z > 0.62f * r) continue;
                    mb.Box(V(x, wallH + mer * 0.5f, z), ax ? V(mer * 1.1f, mer, t * 0.22f) : V(t * 0.22f, mer, mer * 1.1f), top);
                }
            }
            // 马面（每边两座）
            foreach (var u in new[] { -0.55f, 0.55f })
                foreach (var q in new[] { new[] { u * r, -r - t * 0.5f, 0.2f * s, 0.16f * s }, new[] { -r - t * 0.5f, u * r, 0.16f * s, 0.2f * s }, new[] { r + t * 0.5f, u * r, 0.16f * s, 0.2f * s }, new[] { u * r, r + t * 0.5f, 0.2f * s, 0.16f * s } })
                {
                    float x = q[0], z = q[1];
                    if (x > 0.4f * r && z > 0.4f * r) continue;
                    mb.Box(V(x, wallH * 0.52f, z), V(q[2], wallH * 1.04f, q[3]), stone2, 0.06f);
                }
            // 角楼（东北角为矮台）
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    var p = V(cx * r, 0, cz * r);
                    if (cx > 0 && cz > 0) { mb.Box(p + V(0, wallH * 0.55f, 0), V(t * 1.5f, wallH * 1.1f, t * 1.5f), stone2, 0.06f); continue; }
                    mb.Box(p + V(0, wallH * 0.55f, 0), V(t * 1.7f, wallH * 1.1f, t * 1.7f), stone2, 0.06f);
                    mb.Box(p + V(0, wallH * 1.1f + 0.07f * s, 0), V(t * 1.25f, 0.14f * s, t * 1.25f), red);
                    mb.ChineseRoof(p + V(0, wallH * 1.1f + 0.14f * s, 0), t * 2.1f, t * 2.1f, 0.17f * s, roof);
                }
            // 南门：门洞 + 城台 + 重檐城楼
            mb.Box(V(0, wallH * 0.5f, -r), V(0.56f * s, wallH, t * 1.5f), stone2, 0.06f);
            mb.Box(V(0, 0.17f * s, -r - t * 0.76f), V(0.18f * s, 0.3f * s, 0.01f), dark);
            mb.Box(V(0, wallH + 0.11f * s, -r), V(0.46f * s, 0.22f * s, 0.24f * s), red);
            for (int sx = -1; sx <= 1; sx += 2) foreach (var k in new[] { 0.07f, 0.17f }) mb.Box(V(sx * k * s, wallH + 0.12f * s, -r - 0.122f * s), V(0.035f * s, 0.07f * s, 0.006f), H("#e8d8b0"));
            mb.ChineseRoof(V(0, wallH + 0.22f * s, -r), 0.74f * s, 0.44f * s, 0.13f * s, roof);
            mb.Box(V(0, wallH + 0.37f * s, -r), V(0.32f * s, 0.13f * s, 0.17f * s), red);
            mb.ChineseRoof(V(0, wallH + 0.43f * s, -r), 0.56f * s, 0.34f * s, 0.17f * s, roof);
            // 东、西门楼（小）
            for (int sx = -1; sx <= 1; sx += 2)
            {
                mb.Box(V(sx * r, wallH * 0.5f, 0), V(t * 1.4f, wallH, 0.4f * s), stone2, 0.06f);
                mb.Box(V(sx * (r + t * 0.71f), 0.15f * s, 0), V(0.01f, 0.26f * s, 0.14f * s), dark);
                mb.Box(V(sx * r, wallH + 0.09f * s, 0), V(0.2f * s, 0.18f * s, 0.32f * s), red);
                mb.ChineseRoof(V(sx * r, wallH + 0.18f * s, 0), 0.3f * s, 0.48f * s, 0.13f * s, roof);
            }
            // 主殿：三层台基 + 殿身（红柱）+ 重檐
            float hz = 0.2f * s, kk = cap ? 1.12f : 1;
            float y = 0.1f;
            foreach (var l in new[] { new[] { 0.92f, 0.64f, 0.05f }, new[] { 0.78f, 0.54f, 0.05f }, new[] { 0.64f, 0.44f, 0.05f } })
            {
                mb.Box(V(0, y + l[2] * s / 2, hz), V(l[0] * s * kk, l[2] * s, l[1] * s * kk), H("#d8d0bc"), 0.1f);
                y += l[2] * s;
            }
            float hy = 0.25f * s;
            mb.Box(V(0, hy + 0.13f * s, hz), V(0.52f * s * kk, 0.26f * s, 0.32f * s * kk), red);
            for (int i = 0; i < 6; i++) Cyl(mb, V((-0.27f + i * 0.108f) * s * kk, hy, hz - 0.17f * s * kk), 0.016f * s, 0.016f * s, 0.26f * s, 6, H("#7a1e16"), false);
            mb.Box(V(0, hy + 0.12f * s, hz - 0.161f * s * kk), V(0.12f * s, 0.22f * s, 0.006f), H("#3a2010"));
            var rc = cap ? gold : roof;
            mb.ChineseRoof(V(0, hy + 0.26f * s, hz), 0.82f * s * kk, 0.56f * s * kk, 0.2f * s, rc);
            mb.Box(V(0, hy + 0.43f * s, hz), V(0.4f * s * kk, 0.12f * s, 0.24f * s * kk), red);
            mb.ChineseRoof(V(0, hy + 0.49f * s, hz), 0.6f * s * kk, 0.38f * s * kk, 0.24f * s, rc);
            if (cap)
            {
                // 阙：殿前一对，台身 + 双层小顶
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    float x = sx * 0.3f * s, z = -0.5f * s;
                    mb.Box(V(x, 0.2f * s, z), V(0.1f * s, 0.4f * s, 0.1f * s), H("#cfc6b0"), 0.08f);
                    mb.ChineseRoof(V(x, 0.4f * s, z), 0.18f * s, 0.16f * s, 0.07f * s, roof);
                    mb.Box(V(x, 0.49f * s, z), V(0.07f * s, 0.07f * s, 0.07f * s), H("#cfc6b0"));
                    mb.ChineseRoof(V(x, 0.52f * s, z), 0.13f * s, 0.12f * s, 0.07f * s, roof);
                }
            }
            // 坊屋：白墙灰瓦
            var houses = new List<float[]> { new[] { -0.58f, -0.5f, 0.3f, 0.2f }, new[] { -0.58f, -0.18f, 0.3f, 0.2f }, new[] { 0.58f, -0.5f, 0.3f, 0.2f }, new[] { 0.58f, -0.18f, 0.3f, 0.2f }, new[] { -0.62f, 0.32f, 0.24f, 0.3f } };
            if (!cap) houses.Add(new[] { -0.3f, 0.66f, 0.22f, 0.18f });
            foreach (var q in houses)
            {
                float x = q[0], z = q[1], w = q[2], d = q[3];
                mb.Box(V(x * s, 0.18f * s / 2 + 0.1f, z * s), V(w * s, 0.18f * s, d * s), plaster, 0.06f);
                mb.Box(V(x * s, 0.04f * s + 0.1f, (z - d / 2) * s - 0.002f), V(w * s * 0.96f, 0.08f * s, 0.006f), H("#8a8070"));
                mb.ChineseRoof(V(x * s, 0.18f * s + 0.1f, z * s), (w + 0.05f) * s, (d + 0.05f) * s, 0.09f * s, Sh(roof, 0.1f));
            }
            // 庭树
            foreach (var q in new[] { new[] { -0.3f, 0.32f, 0.5f }, new[] { 0.34f, 0.3f, 0.46f }, new[] { 0.66f, -0.75f, 0.4f } }) Tree(mb, q[0] * s, q[1] * s, q[2] * s, H("#4f7f3a"), 0.1f);
        }

        // ---------------------------------------------------------- 罗马 --
        // 带檐线与勒脚的石墙、圆角塔（平滑塔身 + 雉堞 + 圆锥瓦顶）、双圆塔夹半圆拱城门；
        // 列柱神庙（台基前阶、六柱、山花）；都城为平滑椭圆竞技场（三层拱廊、看台斜坡、沙地）+ 小神庙
        static void Roman(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.42f * s, t = 0.16f * s;
            Color stone = H("#cdbf9f"), stone2 = H("#b9ab8a"), tile = H("#b4553a"), stucco = H("#ece2c8"), marble = H("#f2eee4"), dark = H("#33261c");
            Base(mb, r, H("#a89a7a"));
            mb.Box(V(0, 0.104f, 0), V(r * 1.8f, 0.012f, r * 1.8f), H("#b8ab8c"));
            SquareWall(mb, r, wallH, t, stone, 0.07f * s, 4);
            WallBands(mb, r, wallH, t, stone2);
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                {
                    bool big = !(cx > 0 && cz > 0);
                    Tower(mb, cx * r, cz * r, t * (big ? 1.0f : 0.78f), wallH * (big ? 1.3f : 1.05f), Sh(stone, -0.03f), big ? tile : (Color?)null);
                    if (!big) MerlonRing(mb, cx * r, cz * r, t * 0.84f, wallH * 1.05f + t * 0.2f, 6, 0.04f * s, 0.05f * s, 0.03f * s, stone);
                }
            // 城门：双圆塔 + 半圆拱门洞 + 券石
            for (int sx = -1; sx <= 1; sx += 2) Tower(mb, sx * 0.19f * s, -r - 0.02f * s, 0.1f * s, wallH * 1.3f, stone, tile);
            ArchFrame(mb, 0, 0.1f * s, -r - t / 2, 0.16f * s, wallH * 0.7f, 0, -1, marble, false, 0.02f * s);
            Arch(mb, 0, 0.1f * s, -r - t / 2, 0.16f * s, wallH * 0.7f, 0, -1, dark, false);
            // 红瓦民居（瓦顶出檐，山墙一侧开小窗）
            var houses = cap ? new[] { new[] { -0.6f, -0.5f, 0.3f, 0.24f }, new[] { 0.56f, -0.52f, 0.28f, 0.24f }, new[] { -0.62f, -0.12f, 0.26f, 0.3f }, new[] { 0.62f, -0.12f, 0.24f, 0.28f }, new[] { -0.25f, -0.64f, 0.24f, 0.18f }, new[] { 0.27f, -0.66f, 0.22f, 0.17f } }
                : new[] { new[] { -0.55f, -0.45f, 0.34f, 0.26f }, new[] { 0.5f, -0.5f, 0.3f, 0.26f }, new[] { -0.6f, 0.15f, 0.28f, 0.34f }, new[] { 0.62f, 0.05f, 0.26f, 0.3f }, new[] { -0.2f, -0.62f, 0.26f, 0.2f }, new[] { 0.2f, -0.25f, 0.22f, 0.2f } };
            for (int i = 0; i < houses.Length; i++)
            {
                var h = houses[i];
                float w = h[2] * s, d = h[3] * s, hh = (0.17f + (i % 3) * 0.03f) * s;
                GableHouse(mb, h[0] * s, h[1] * s, w, d, hh, i % 2 != 0 ? stucco : Sh(stucco, -0.06f), tile, d > w, 0.1f, Mathf.Min(w, d) * 0.32f);
                Arch(mb, h[0] * s, 0.1f + hh * 0.35f, h[1] * s - d / 2, 0.045f * s, hh * 0.45f, 0, -1, dark, false);
            }
            Action<float, float, float> temple = (tx, tz, k) =>
            {
                // 台基 + 前阶
                mb.Box(V(tx, 0.1f + 0.06f * k, tz), V(0.6f * k, 0.12f * k, 0.72f * k), marble, 0.06f);
                for (int i = 0; i < 3; i++) mb.Box(V(tx, 0.1f + 0.02f * k * (i + 0.5f), tz - 0.36f * k - (2.5f - i) * 0.035f * k), V(0.4f * k, 0.04f * k * (i + 1), 0.035f * k), Sh(marble, -0.04f * i));
                float y1 = 0.1f + 0.12f * k;
                mb.Box(V(tx, y1 + 0.15f * k, tz + 0.08f * k), V(0.42f * k, 0.3f * k, 0.44f * k), stucco);
                Arch(mb, tx, y1, tz + 0.08f * k - 0.22f * k, 0.12f * k, 0.24f * k, 0, -1, dark, false);
                for (int i = 0; i < 6; i++) Column(mb, tx + (-0.25f + i * 0.1f) * k, y1, tz - 0.29f * k, 0.32f * k, 0.026f * k, marble);
                for (int sx = -1; sx <= 1; sx += 2) for (int i = 1; i < 4; i++) Column(mb, tx + sx * 0.25f * k, y1, tz + (-0.29f + i * 0.16f) * k, 0.32f * k, 0.026f * k, marble);
                float y2 = y1 + 0.32f * k + 0.035f * k;
                mb.Box(V(tx, y2 + 0.025f * k, tz), V(0.62f * k, 0.05f * k, 0.72f * k), marble, 0.08f);   // 檐部
                Gable(mb, tx, y2 + 0.05f * k, tz, 0.64f * k, 0.74f * k, 0.15f * k, tile, true, 0.012f * k);
                Face(mb, new[] { V(tx - 0.3f * k, y2 + 0.052f * k, tz - 0.372f * k), V(tx + 0.3f * k, y2 + 0.052f * k, tz - 0.372f * k), V(tx, y2 + 0.19f * k, tz - 0.372f * k) }, marble, V(0, 0, -1));   // 山花
            };
            if (!cap) temple(0, 0.42f * s, s);
            else
            {
                // 竞技场：外壁三层拱廊（底两层开拱、顶层为实墙带小窗）、檐线、看台斜坡、沙地
                float ax = -0.08f * s, az = 0.3f * s, rx = 0.56f * s, rz = 0.43f * s, hh = 0.5f * s; const int n = 30;
                EllipseRing(mb, ax, az, rx, rz, 0.1f, hh, 0.07f * s, stucco, Sh(stucco, -0.1f), n);
                foreach (var q in new[] { new[] { 0.33f, 1.01f }, new[] { 0.66f, 1.008f }, new[] { 0.98f, 1.012f } }) EllipseRing(mb, ax, az, rx * q[1], rz * q[1], 0.1f + hh * q[0] - 0.012f * s, 0.024f * s, 0.03f * s, Sh(stucco, 0.05f), null, n, true);
                for (int i = 0; i < n; i++)
                {
                    float a = (i + 0.5f) / n * PI * 2, nx0 = Mathf.Cos(a) / rx, nz0 = Mathf.Sin(a) / rz, l = Mathf.Sqrt(nx0 * nx0 + nz0 * nz0);
                    float px = ax + Mathf.Cos(a) * rx, pz = az + Mathf.Sin(a) * rz, w = PI * 2 * Mathf.Sqrt((rx * rx + rz * rz) / 2) / n * 0.55f;
                    foreach (var y in new[] { 0.03f, 0.36f }) Arch(mb, px, 0.1f + hh * y, pz, w, hh * 0.25f, nx0 / l, nz0 / l, H("#4a3a2c"), false);
                    if (i % 2 == 0) Arch(mb, px, 0.1f + hh * 0.72f, pz, w * 0.4f, hh * 0.12f, nx0 / l, nz0 / l, H("#5a4a38"), false);
                }
                // 看台（内壁顶 → 场地边缘的斜坡）
                Func<float, float, float, Vector3> ring = (a, k, y) => V(ax + Mathf.Cos(a) * (rx - 0.07f * s) * k, y, az + Mathf.Sin(a) * (rz - 0.07f * s) * k);
                Func<float, Vector3> slope = a => { float x = Mathf.Cos(a) / rx, z = Mathf.Sin(a) / rz, l = Mathf.Sqrt(x * x + z * z); return V(-x / l * 0.8f, 0.6f, -z / l * 0.8f); };
                for (int i = 0; i < n; i++)
                {
                    float a0 = (float)i / n * PI * 2, a1 = (float)(i + 1) / n * PI * 2; var nm = Vector3.up;
                    Vector3 p0 = ring(a0, 1, 0.1f + hh * 0.96f), p1 = ring(a1, 1, 0.1f + hh * 0.96f), q0 = ring(a0, 0.52f, 0.16f), q1 = ring(a1, 0.52f, 0.16f);
                    mb.TriN(p0, q1, p1, slope(a0), slope(a1), slope(a1), H("#c9bda2"));
                    mb.TriN(p0, q0, q1, slope(a0), slope(a0), slope(a1), H("#c9bda2"));
                    mb.TriN(V(ax, 0.16f, az), q1, q0, nm, nm, nm, H("#dcc48e"));
                }
                temple(0.6f * s, 0.5f * s, 0.5f * s);
            }
            Standard(mb, -0.3f * s, -r - 0.2f * s, 0.75f * s, H("#a8261e"), "vexillum");
            Standard(mb, 0.3f * s, -r - 0.2f * s, 0.75f * s, H("#a8261e"), "vexillum");
        }

        // ---------------------------------------------------- 安息 / 波斯 --
        // 土坯城墙（檐线、勒脚、阶梯垛口）、半圆扶壁塔与圆角塔、高耸的门楼（尖拱 + 釉砖框）；
        // 三层阶梯内城上的伊万宫殿（尖拱大门 + 筒拱大厅 + 两翼拱廊）；都城另有拜火祠（四拱亭 + 穹顶）
        static void Persia(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.42f * s, t = 0.2f * s;
            Color mud = H("#c8a679"), mud2 = H("#b8946a"), glaze = H("#2f6e8e"), dark = H("#2e2016"), lite = H("#e0c89c");
            Base(mb, r, H("#b8a07a"));
            mb.Box(V(0, 0.104f, 0), V(r * 1.8f, 0.012f, r * 1.8f), H("#c2aa82"));
            SquareWall(mb, r, wallH, t, mud, 0.09f * s, 3);
            WallBands(mb, r, wallH, t, mud2);
            // 半圆扶壁塔（沿墙等距）与圆角塔（东北角留给旗帜）
            foreach (var k in new[] { -0.5f, 0.5f })
                foreach (var side in new[] { new[] { 0, -1 }, new[] { 0, 1 }, new[] { -1, 0 }, new[] { 1, 0 } })
                {
                    float x = side[0] != 0 ? side[0] * r : k * r * 1.2f, z = side[1] != 0 ? side[1] * r : k * r * 1.2f;
                    if (x > 0 && z > 0.4f * r) continue;
                    if (side[1] == -1 && Mathf.Abs(x) < 0.4f * r) continue;
                    Tower(mb, x, z, t * 0.62f, wallH * 1.08f, mud2, null);
                }
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                    if (!(cx > 0 && cz > 0))
                    {
                        Tower(mb, cx * r, cz * r, t * 0.9f, wallH * 1.3f, mud2, null);
                        MerlonRing(mb, cx * r, cz * r, t * 0.95f, wallH * 1.3f + t * 0.22f, 7, 0.05f * s, 0.05f * s, 0.03f * s, mud);
                    }
            // 城门：门楼（矩形框）+ 尖拱 + 釉砖框
            mb.Box(V(0, wallH * 0.7f, -r), V(0.46f * s, wallH * 1.4f, t * 1.3f), mud2, 0.06f);
            ArchFrame(mb, 0, 0.1f, -r - t * 0.65f, 0.2f * s, wallH * 1.02f, 0, -1, glaze, true, 0.025f * s);
            Arch(mb, 0, 0.1f, -r - t * 0.65f, 0.2f * s, wallH * 1.02f, 0, -1, dark, true);
            for (int sx = -1; sx <= 1; sx += 2) Tower(mb, sx * 0.23f * s, -r - t * 0.1f, 0.055f * s, wallH * 1.55f, lite, null);
            // 平顶房（女儿墙、小拱门）
            var hs = new[] { new[] { -0.6f, -0.5f, 0.3f, 0.26f, 0.2f }, new[] { 0.55f, -0.55f, 0.26f, 0.24f, 0.18f }, new[] { -0.62f, -0.05f, 0.26f, 0.32f, 0.22f }, new[] { 0.62f, -0.1f, 0.24f, 0.3f, 0.2f }, new[] { -0.3f, -0.62f, 0.2f, 0.18f, 0.16f }, new[] { 0.3f, -0.64f, 0.2f, 0.16f, 0.15f } };
            for (int i = 0; i < hs.Length; i++)
            {
                var h = hs[i];
                FlatHouse(mb, h[0] * s, h[1] * s, h[2] * s, h[3] * s, h[4] * s, i % 2 != 0 ? mud : Sh(mud, 0.08f), 0.1f);
                Arch(mb, h[0] * s, 0.1f, h[1] * s - h[3] * s / 2 - 0.003f, 0.05f * s, h[4] * s * 0.6f, 0, -1, dark, true);
                if (i % 3 == 0) Dome(mb, h[0] * s, 0.1f + h[4] * s, h[1] * s, Mathf.Min(h[2], h[3]) * s * 0.32f, Sh(mud, 0.1f), 12);
            }
            // 阶梯内城（北侧）：三层台
            float cz0 = 0.42f * s, ty = 0.1f;
            var lv = new[] { new[] { 0.9f, 0.16f }, new[] { 0.68f, 0.16f }, new[] { 0.46f, 0.14f } };
            for (int i = 0; i < lv.Length; i++)
            {
                var l = lv[i];
                mb.Box(V(-0.05f * s, ty + l[1] * s / 2, cz0), V(l[0] * s, l[1] * s, l[0] * s * 0.7f), i % 2 != 0 ? mud2 : Sh(mud2, 0.06f), 0.08f);
                ty += l[1] * s;
            }
            for (int i = 0; i < 4; i++) mb.Box(V(-0.05f * s, 0.1f + (i + 0.5f) * 0.08f * s, cz0 - 0.315f * s - (3 - i) * 0.012f * s), V(0.14f * s, 0.08f * s * (i + 1) / 2, 0.03f * s), Sh(mud2, 0.04f));   // 正面台阶
            // 伊万宫殿：门面（大尖拱 + 釉砖框）+ 后接筒拱大厅 + 两翼拱廊
            float iw = cap ? 0.44f : 0.32f, px = -0.05f * s, fz = cz0 - 0.12f * s;
            mb.Box(V(px, ty + 0.17f * s, fz), V(iw * s, 0.34f * s, 0.06f * s), H("#dcc192"), 0.06f);
            ArchFrame(mb, px, ty, fz - 0.031f * s, iw * 0.52f * s, 0.3f * s, 0, -1, glaze, true, 0.018f * s);
            Arch(mb, px, ty, fz - 0.031f * s, iw * 0.52f * s, 0.3f * s, 0, -1, dark, true);
            Vault(mb, px, ty + 0.14f * s, fz + 0.03f * s, fz + 0.26f * s, iw * 0.3f * s, H("#d2b688"), 12);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                mb.Box(V(px + sx * iw * 0.38f * s, ty + 0.08f * s, fz + 0.13f * s), V(iw * 0.26f * s, 0.16f * s, 0.22f * s), Sh(H("#d2b688"), -0.04f), 0.06f);
                for (int k = -1; k <= 1; k += 2) Arch(mb, px + sx * iw * 0.38f * s + k * 0.04f * s, ty, fz + 0.02f * s - 0.003f, 0.045f * s, 0.12f * s, 0, -1, dark, true);
            }
            if (cap)
            {
                // 拜火祠（chahar taq）：方台 + 四面拱门 + 穹顶
                float fx = 0.56f * s, fzz = 0.5f * s, k = 0.14f * s;
                mb.Box(V(fx, 0.1f + 0.04f * s, fzz), V(0.32f * s, 0.08f * s, 0.32f * s), mud2);
                mb.Box(V(fx, 0.1f + 0.08f * s + k * 0.7f, fzz), V(k * 1.7f, k * 1.4f, k * 1.7f), lite, 0.06f);
                foreach (var q in new[] { new[] { 0, -1 }, new[] { 1, 0 }, new[] { 0, 1 }, new[] { -1, 0 } }) Arch(mb, fx + q[0] * k * 0.85f, 0.1f + 0.08f * s, fzz + q[1] * k * 0.85f, k * 0.8f, k * 1.15f, q[0], q[1], dark, true);
                Dome(mb, fx, 0.1f + 0.08f * s + k * 1.4f, fzz, k * 0.8f, lite, 16);
                Cone(mb, V(fx, 0.1f + 0.08f * s, fzz - k * 0.86f), 0.025f * s, 0.05f * s, 6, H("#ff8a2a"));
            }
            Standard(mb, -0.6f * s, 0.6f * s, 0.7f * s, H("#7a2a5a"), "tail");
        }

        // ------------------------------------------------- 阿拉伯 / 哈特拉 --
        // 平滑的圆形城墙（檐线、雉堞环）、平滑圆塔、拱门；神庙院（双伊万 + 列柱）；阿克苏姆另立石碑
        static void Arab(MeshBuilder mb, float s, bool cap, City city)
        {
            float r = s, R = 0.98f * r, wallH = 0.4f * s, t = 0.15f * s;
            Color stone = H("#d8c49a"), stone2 = H("#c4ae84"), dark = H("#2e2016");
            Base(mb, r, H("#c8b080"));
            mb.Lathe(V(0, 0, 0), new[] { L(R + t / 2, 0.06f), L(R + t / 2 + 0.01f * s, 0.14f * s, true), L(R + t / 2, 0.15f * s, true), L(R + t / 2, wallH, true), L(R - t / 2, wallH, true), L(R - t / 2, 0.06f) }, 40, stone);
            mb.Lathe(V(0, wallH - 0.02f * s, 0), new[] { L(R + t / 2 + 0.012f * s, 0), L(R + t / 2 + 0.012f * s, 0.03f * s, true), L(R - t / 2, 0.03f * s) }, 40, Sh(stone, 0.05f));
            MerlonRing(mb, 0, 0, R + t * 0.3f, wallH + 0.01f * s, 30, 0.05f * s, 0.05f * s, 0.035f * s, Sh(stone, 0.05f));
            for (int i = 0; i < 8; i++)
            {
                float a = i * PI / 4 + PI / 8;
                float x = Mathf.Cos(a) * R, z = Mathf.Sin(a) * R;
                if (x > 0.3f * r && z > 0.3f * r) continue;
                Tower(mb, x, z, 0.1f * s, wallH * 1.3f, stone2, null);
                MerlonRing(mb, x, z, 0.105f * s, wallH * 1.3f + 0.025f * s, 6, 0.035f * s, 0.04f * s, 0.025f * s, stone2);
            }
            // 南门
            mb.Box(V(0, wallH * 0.62f, -R), V(0.34f * s, wallH * 1.24f, 0.22f * s), stone2, 0.06f);
            ArchFrame(mb, 0, 0.08f, -R - 0.112f * s, 0.14f * s, wallH * 0.85f, 0, -1, H("#efe3c4"), false, 0.016f * s);
            Arch(mb, 0, 0.08f, -R - 0.112f * s, 0.14f * s, wallH * 0.85f, 0, -1, dark, false);
            // 神庙院（中央）：双伊万神殿 + 列柱
            float tz = 0.12f * s;
            mb.Box(V(0, 0.05f * s, tz), V(0.84f * s, 0.1f * s, 0.6f * s), Sh(stone, 0.06f));
            int n = cap ? 2 : 1;
            for (int k = 0; k < n; k++)
            {
                float x = (n == 1 ? 0 : (k != 0 ? 0.2f : -0.2f)) * s;
                mb.Box(V(x, 0.1f * s + 0.17f * s, tz + 0.1f * s), V(0.34f * s, 0.34f * s, 0.32f * s), stone, 0.06f);
                Arch(mb, x, 0.1f * s, tz - 0.061f * s, 0.15f * s, 0.27f * s, 0, -1, dark, false);
                Vault(mb, x, 0.1f * s + 0.34f * s, tz - 0.06f * s, tz + 0.26f * s, 0.12f * s, Sh(stone, 0.08f), 10);
            }
            for (int i = 0; i < 6; i++) Column(mb, (-0.36f + i * 0.144f) * s, 0.1f * s, tz - 0.25f * s, 0.2f * s, 0.022f * s, H("#efe3c4"));
            var hs = new[] { new[] { -0.55f, -0.45f }, new[] { 0.55f, -0.42f }, new[] { -0.62f, 0.35f }, new[] { -0.3f, -0.62f }, new[] { 0.3f, -0.64f } };
            for (int i = 0; i < hs.Length; i++)
            {
                var h = hs[i];
                FlatHouse(mb, h[0] * s, h[1] * s, 0.22f * s, 0.2f * s, (0.14f + (i % 3) * 0.03f) * s, i % 2 != 0 ? stone : H("#e2d2ac"), 0.1f);
                Arch(mb, h[0] * s, 0.1f, h[1] * s - 0.103f * s, 0.05f * s, 0.09f * s, 0, -1, dark, false);
            }
            Palm(mb, 0.55f * s, 0.1f * s, s, 11); Palm(mb, -0.15f * s, 0.6f * s, s * 0.9f, 12); Palm(mb, -0.72f * s, -0.12f * s, s * 0.85f, 13);
            if (city != null && city.key == "aksum")
            {
                foreach (var q in new[] { new[] { 0.42f, 0.95f }, new[] { 0.6f, 0.7f }, new[] { 0.28f, 0.6f } })
                {
                    float x = q[0], h = q[1];
                    mb.Box(V(x * s, h * s / 2, 0.55f * s), V(0.07f * s, h * s, 0.04f * s), H("#9a8f80"));
                    mb.Lathe(V(x * s, h * s, 0.55f * s), new[] { L(0.035f * s, 0), L(0.03f * s, 0.03f * s), L(0, 0.05f * s) }, 8, H("#9a8f80"));
                    for (int j = 1; j < 5; j++) mb.Box(V(x * s, h * s * j / 5, 0.55f * s - 0.021f * s), V(0.05f * s, 0.03f * s, 0.004f), H("#6a6056"));   // 假窗
                }
            }
            Standard(mb, 0, 0.62f * s, 0.75f * s, H("#d8b050"), "tail");
        }

        // ------------------------------------------------------------ 贵霜 --
        static void Stupa(MeshBuilder mb, float x, float z, float k, float y0 = 0)
        {
            Color white = H("#ece6d6"), gold = H("#d8b050"), sand = H("#cfc4a8");
            mb.Box(V(x, y0 + 0.05f * k, z), V(0.52f * k, 0.1f * k, 0.52f * k), sand, 0.06f);
            mb.Box(V(x, y0 + 0.12f * k, z), V(0.44f * k, 0.05f * k, 0.44f * k), H("#ddd2b6"), 0.06f);
            // 栏楯（围栏环）与四面塔门
            mb.Lathe(V(x, y0 + 0.1f * k, z), new[] { L(0.3f * k, 0), L(0.3f * k, 0.05f * k, true), L(0.29f * k, 0.055f * k, true), L(0.29f * k, 0) }, 24, sand);
            foreach (var q in new[] { new[] { 0, -1 }, new[] { 1, 0 }, new[] { 0, 1 }, new[] { -1, 0 } })
            {
                float nx = q[0], nz = q[1];
                float gx = x + nx * 0.3f * k, gz = z + nz * 0.3f * k, tx = -nz, tz = nx;
                for (int sd = -1; sd <= 1; sd += 2) mb.Box(V(gx + tx * sd * 0.04f * k, y0 + 0.1f * k + 0.07f * k, gz + tz * sd * 0.04f * k), V(0.014f * k, 0.14f * k, 0.014f * k), sand);
                foreach (var yy in new[] { 0.12f, 0.15f }) mb.Box(V(gx, y0 + 0.1f * k + yy * k, gz), V(Mathf.Abs(tx) * 0.12f * k + 0.012f * k, 0.012f * k, Mathf.Abs(tz) * 0.12f * k + 0.012f * k), sand);
            }
            mb.Lathe(V(x, y0 + 0.145f * k, z), new[] { L(0.21f * k, 0), L(0.21f * k, 0.1f * k, true), L(0.2f * k, 0.1f * k, true),
                L(0.2f * k, 0.14f * k), L(0.19f * k, 0.2f * k), L(0.165f * k, 0.26f * k), L(0.13f * k, 0.31f * k), L(0.08f * k, 0.34f * k), L(0, 0.352f * k) }, 24, white);
            mb.Box(V(x, y0 + 0.51f * k, z), V(0.08f * k, 0.05f * k, 0.08f * k), H("#b88a4a"));
            mb.Lathe(V(x, y0 + 0.535f * k, z), new[] { L(0.012f * k, 0), L(0.01f * k, 0.24f * k) }, 8, gold);
            for (int i = 0; i < 3; i++) mb.Lathe(V(x, y0 + (0.57f + i * 0.06f) * k, z), new[] { L((0.07f - i * 0.017f) * k, 0), L((0.07f - i * 0.017f) * k, 0.012f * k, true), L(0, 0.014f * k) }, 14, gold);
        }
        // 砖城（檐线、勒脚）、平滑圆角堡与半圆马面、拱门；带栏楯与塔门的窣堵波、带木檐的僧院；都城宫殿为高台列柱厅 + 穹顶
        static void Kushan(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.4f * s, t = 0.18f * s;
            Color brick = H("#b98f64"), brick2 = H("#a87e56"), plaster = H("#e2d4b4"), wood = H("#7a5232"), dark = H("#2e2016");
            Base(mb, r, H("#a8966e"));
            mb.Box(V(0, 0.104f, 0), V(r * 1.8f, 0.012f, r * 1.8f), H("#b3a07a"));
            SquareWall(mb, r, wallH, t, brick, 0.07f * s, 3);
            WallBands(mb, r, wallH, t, brick2);
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                    if (!(cx > 0 && cz > 0))
                    {
                        Tower(mb, cx * r, cz * r, t * 1.05f, wallH * 1.25f, brick2, null);
                        MerlonRing(mb, cx * r, cz * r, t * 1.1f, wallH * 1.25f + t * 0.26f, 7, 0.045f * s, 0.05f * s, 0.03f * s, brick);
                    }
            foreach (var q in new[] { new[] { -r, -0.4f * r }, new[] { -r, 0.4f * r }, new[] { r, -0.4f * r }, new[] { -0.45f * r, r }, new[] { 0.45f * r, -r }, new[] { -0.45f * r, -r } }) Tower(mb, q[0], q[1], t * 0.6f, wallH * 1.06f, brick2, null);
            // 城门：方门楼 + 半圆拱 + 木檐
            for (int sx = -1; sx <= 1; sx += 2) mb.Box(V(sx * 0.2f * s, wallH * 0.65f, -r), V(0.16f * s, wallH * 1.3f, t * 1.3f), brick2, 0.06f);
            mb.Box(V(0, wallH * 1.05f, -r), V(0.5f * s, 0.03f * s, t * 1.5f), wood);
            ArchFrame(mb, 0, 0.1f, -r - t / 2, 0.15f * s, wallH * 0.82f, 0, -1, plaster, false, 0.018f * s);
            Arch(mb, 0, 0.1f, -r - t / 2, 0.15f * s, wallH * 0.82f, 0, -1, dark, false);
            Stupa(mb, -0.15f * s, 0.38f * s, cap ? 1.3f * s : 1.0f * s, 0.1f);
            // 僧院 / 民居：平顶 + 木檐 + 拱门
            var hs = new[] { new[] { -0.58f, -0.45f, 0.3f, 0.24f }, new[] { 0.55f, -0.45f, 0.28f, 0.24f }, new[] { 0.6f, 0.0f, 0.24f, 0.3f }, new[] { -0.65f, 0.05f, 0.22f, 0.3f }, new[] { 0.15f, -0.6f, 0.24f, 0.2f } };
            for (int i = 0; i < hs.Length; i++)
            {
                var h = hs[i]; float hh = 0.17f * s;
                FlatHouse(mb, h[0] * s, h[1] * s, h[2] * s, h[3] * s, hh, i % 2 != 0 ? plaster : brick, 0.1f);
                mb.Box(V(h[0] * s, 0.1f + hh * 0.78f, h[1] * s), V(h[2] * s * 1.14f, 0.014f * s, h[3] * s * 1.14f), wood);
                Arch(mb, h[0] * s, 0.1f, h[1] * s - h[3] * s / 2 - 0.004f, 0.05f * s, hh * 0.6f, 0, -1, dark, false);
            }
            if (cap)
            {
                // 宫殿：高台（前阶）+ 列柱厅 + 穹顶
                float px = 0.48f * s, pz = 0.45f * s;
                mb.Box(V(px, 0.1f + 0.08f * s, pz), V(0.44f * s, 0.16f * s, 0.36f * s), brick2, 0.06f);
                for (int i = 0; i < 3; i++) mb.Box(V(px, 0.1f + 0.027f * s * (i + 0.5f), pz - 0.18f * s - (2.5f - i) * 0.025f * s), V(0.16f * s, 0.054f * s * (i + 1) / 2, 0.025f * s), brick);
                mb.Box(V(px, 0.1f + 0.3f * s, pz + 0.05f * s), V(0.36f * s, 0.28f * s, 0.24f * s), plaster, 0.06f);
                for (int i = 0; i < 4; i++) Column(mb, px + (-0.14f + i * 0.093f) * s, 0.1f + 0.16f * s, pz - 0.12f * s, 0.26f * s, 0.018f * s, H("#f0e8d8"));
                mb.Box(V(px, 0.1f + 0.45f * s, pz), V(0.42f * s, 0.04f * s, 0.34f * s), wood, 0.06f);
                Dome(mb, px, 0.1f + 0.47f * s, pz + 0.04f * s, 0.11f * s, plaster, 16);
            }
            else
            {
                Poplar(mb, 0.45f * s, 0.4f * s, s * 0.9f, 0.1f); Poplar(mb, 0.62f * s, 0.32f * s, s * 0.8f, 0.1f);
            }
            Standard(mb, 0.3f * s, -0.25f * s, 0.7f * s, H("#d8b050"), "flag");
        }

        // ------------------------------------------------------------ 倭 --
        static void Wa(MeshBuilder mb, float s, bool cap)
        {
            float r = s;
            Color wood = H("#8a6a46"), thatch = H("#a88a52"), thatch2 = H("#94784a");
            Base(mb, r, H("#7d8a52"));
            Moat(mb, r * 0.92f, r * 1.12f, 0.105f, H("#4a7a8a"));
            // 土垒 + 木栅（内侧）
            float R = r * 0.84f;
            Palisade(mb, new[] { Z(-R, -R), Z(-0.15f * s, -R), Z(R, -R), Z(R, R), Z(-R, R) }, 0.2f * s, wood, 6, 0.1f);
            // 门（南，两柱 + 横木）
            for (int sx = -1; sx <= 1; sx += 2) mb.Box(V(sx * 0.15f * s, 0.17f * s, -R), V(0.04f * s, 0.34f * s, 0.04f * s), Sh(wood, -0.1f));
            mb.Box(V(0, 0.33f * s, -R), V(0.4f * s, 0.035f * s, 0.04f * s), Sh(wood, -0.1f));
            // 望楼（西南、东南、西北）
            foreach (var q in new[] { new[] { -0.7f, -0.7f }, new[] { 0.7f, -0.7f }, new[] { -0.7f, 0.62f } })
            {
                float x = q[0], z = q[1];
                for (int ox = -1; ox <= 1; ox += 2) for (int oz = -1; oz <= 1; oz += 2) mb.Box(V((x + ox * 0.06f) * s, 0.3f * s, (z + oz * 0.06f) * s), V(0.025f * s, 0.6f * s, 0.025f * s), wood);
                mb.Box(V(x * s, 0.58f * s, z * s), V(0.18f * s, 0.03f * s, 0.18f * s), Sh(wood, 0.1f));
                Hip(mb, x * s, 0.66f * s, z * s, 0.22f * s, 0.22f * s, 0.14f * s, thatch);
                mb.Box(V(x * s, 0.62f * s, z * s), V(0.16f * s, 0.07f * s, 0.16f * s), Sh(wood, -0.15f));
            }
            // 高床仓库
            foreach (var q in new[] { new[] { 0.45f, 0.45f }, new[] { 0.2f, 0.55f } }) StiltHouse(mb, q[0] * s, q[1] * s, 0.2f * s, 0.16f * s, 0.14f * s, 0.1f * s, H("#a07c52"), thatch2, false);
            // 竖穴住居（圆锥茅顶）
            foreach (var q in new[] { new[] { -0.45f, -0.35f, 1 }, new[] { 0.0f, -0.45f, 0.9f }, new[] { 0.4f, -0.35f, 1 }, new[] { -0.5f, 0.15f, 0.95f }, new[] { -0.1f, 0.05f, 0.85f } })
            {
                float x = q[0], z = q[1], k = q[2];
                Cyl(mb, V(x * s, 0.1f, z * s), 0.15f * k * s, 0.04f * k * s, 0.2f * k * s, 7, thatch, false);
                Cone(mb, V(x * s, 0.1f + 0.2f * k * s, z * s), 0.05f * k * s, 0.05f * k * s, 5, thatch2);
            }
            if (cap)
            {
                // 大型高床殿（邪马台宫室）
                StiltHouse(mb, 0.05f * s, 0.45f * s, 0.42f * s, 0.3f * s, 0.22f * s, 0.16f * s, H("#b08a5a"), thatch2, false);
                for (int sx = -1; sx <= 1; sx += 2) mb.Box(V((0.05f + sx * 0.24f) * s, 0.86f * s, 0.45f * s), V(0.02f * s, 0.16f * s, 0.02f * s), wood);   // 千木
            }
            Standard(mb, -0.25f * s, -0.6f * s, 0.55f * s, H("#e8e0d0"), "flag");
        }

        // ------------------------------------------------------------ 朝鲜 --
        static void Korea(MeshBuilder mb, float s, bool cap)
        {
            float r = s;
            Color rock = H("#a6a296"), rock2 = H("#8e8a80"), roof = H("#4e5560"), wood = H("#9a3a2c"), wall = H("#e6dccb");
            Base(mb, r, H("#7e8a5c"));
            // 山丘
            mb.Sphere(V(0.05f * s, 0.05f, 0.25f * s), V(0.85f * s, 0.38f * s, 0.7f * s), H("#6f7f4e"), 2, new SphereOpt { seed = 7, rough = 0.06f, bottomShade = 0.1f });
            // 依山石墙：不规则多边形，北高南低
            var pts = new[] { Z(-0.98f, -0.95f), Z(0.0f, -1.0f), Z(0.98f, -0.9f), Z(1.0f, 0.1f), Z(0.75f, 0.95f), Z(-0.2f, 1.0f), Z(-0.95f, 0.7f), Z(-1.0f, -0.1f) };
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[(i + 1) % pts.Length];
                if (i == 0)
                {   // 南墙中间是城门
                    Seg(mb, a.x * r, a.y * r, -0.16f * s, -r, 0, 0.3f * s, 0.13f * s, rock, 0.3f * s);
                    Seg(mb, 0.16f * s, -r, b.x * r * 0 + 0.98f * r, -0.9f * r, 0, 0.3f * s, 0.13f * s, rock, 0.32f * s);
                    continue;
                }
                if (i == 1) continue;
                float ha = (0.3f + Mathf.Max(0, a.y) * 0.25f) * s, hb = (0.3f + Mathf.Max(0, b.y) * 0.25f) * s;
                Seg(mb, a.x * r, a.y * r, b.x * r, b.y * r, 0, ha, 0.13f * s, i % 2 != 0 ? rock : rock2, hb);
            }
            // 城门楼
            mb.Box(V(0, 0.17f * s, -r), V(0.34f * s, 0.34f * s, 0.2f * s), rock2);
            mb.Box(V(0, 0.13f * s, -r - 0.101f * s), V(0.14f * s, 0.26f * s, 0.01f), H("#2a1c12"));
            mb.Box(V(0, 0.42f * s, -r), V(0.3f * s, 0.16f * s, 0.16f * s), wood);
            mb.ChineseRoof(V(0, 0.5f * s, -r), 0.48f * s, 0.3f * s, 0.2f * s, roof);
            // 山上殿阁
            float hy = 0.3f * s;
            mb.Box(V(0.0f, hy + 0.12f * s, 0.35f * s), V(0.5f * s, 0.24f * s, 0.3f * s), wall);
            mb.Box(V(0.0f, hy + 0.12f * s, 0.199f * s), V(0.5f * s, 0.05f * s, 0.01f), wood);
            mb.ChineseRoof(V(0.0f, hy + 0.24f * s, 0.35f * s), 0.72f * s, 0.48f * s, 0.26f * s, cap ? H("#5a4a3a") : roof);
            if (cap)
            {
                mb.Box(V(0.0f, hy + 0.42f * s, 0.35f * s), V(0.3f * s, 0.14f * s, 0.2f * s), wall);
                mb.ChineseRoof(V(0.0f, hy + 0.49f * s, 0.35f * s), 0.46f * s, 0.32f * s, 0.2f * s, H("#5a4a3a"));
            }
            // 山下民居（草顶 / 瓦顶）
            foreach (var q in new[] { new[] { -0.55f, -0.55f, 0 }, new[] { 0.5f, -0.6f, 1 }, new[] { -0.62f, -0.1f, 1 }, new[] { 0.6f, -0.15f, 0 } })
            {
                float x = q[0], z = q[1];
                mb.Box(V(x * s, 0.1f * s, z * s), V(0.24f * s, 0.2f * s, 0.18f * s), wall);
                if (q[2] != 0) mb.ChineseRoof(V(x * s, 0.2f * s, z * s), 0.34f * s, 0.26f * s, 0.13f * s, roof);
                else Hip(mb, x * s, 0.2f * s, z * s, 0.3f * s, 0.24f * s, 0.13f * s, H("#a88a52"), 0.4f);
            }
            Tree(mb, -0.45f * s, 0.45f * s, 0.6f * s, H("#3f6a34"), 0.15f * s);
            Standard(mb, -0.3f * s, -r - 0.05f * s, 0.6f * s, H("#c8382c"), "flag");
        }

        // ------------------------------------------------------------ 草原 --
        static void Steppe(MeshBuilder mb, float s, bool cap)
        {
            float r = s;
            Color felt = H("#e8e0cc"), felt2 = H("#d6cbb0"), wood = H("#7a5a3a");
            // 营地不放方形台基，改为一片低平的圆形踩踏土地（边缘渐融进草地）
            mb.Lathe(V(0, -0.01f, 0), new[] { L(r * 1.22f, 0, H("#7e8a52")), L(r * 1.12f, 0.03f, H("#8a8458")), L(r * 0.95f, 0.055f, H("#9a8a62")), L(0, 0.065f, H("#a08e66")) }, 28, H("#9a8a62"));
            // 车阵 / 木篱（低矮的环）
            RingWall(mb, r * 0.95f, 12, 0.1f * s, 0.04f * s, wood, PI / 12);
            var ys = new[] { new[] { -0.45f, -0.4f, 0.2f }, new[] { 0.42f, -0.45f, 0.19f }, new[] { -0.58f, 0.18f, 0.19f }, new[] { 0.12f, -0.1f, 0.18f }, new[] { -0.12f, -0.62f, 0.15f }, new[] { 0.58f, 0.02f, 0.16f } };
            for (int i = 0; i < ys.Length; i++) Yurt(mb, ys[i][0] * s, ys[i][1] * s, ys[i][2] * s, i % 2 != 0 ? felt : felt2);
            // 首领大帐
            float big = cap ? 0.34f : 0.26f;
            Yurt(mb, -0.1f * s, 0.42f * s, big * s, H("#f2ecdc"), 0, cap ? H("#d8b050") : H("#c8bc9c"));
            if (cap) Cone(mb, V(-0.1f * s, big * s * 1.12f, 0.42f * s), 0.05f * s, 0.14f * s, 5, H("#d8b050"));
            // 马尾纛
            foreach (var q in new[] { new[] { -0.35f, -0.85f }, new[] { 0.35f, -0.85f }, new[] { -0.45f, 0.62f } }) Standard(mb, q[0] * s, q[1] * s, 0.8f * s, H("#2a2420"), "tail");
            Standard(mb, 0.25f * s, 0.35f * s, 0.9f * s, H("#f2ecdc"), "tail");
            // 马栏
            for (int i = 0; i < 4; i++) mb.Box(V((0.35f + i * 0.1f) * s, 0.06f * s, -0.75f * s), V(0.02f * s, 0.12f * s, 0.02f * s), wood);
            mb.Box(V(0.5f * s, 0.1f * s, -0.75f * s), V(0.32f * s, 0.015f * s, 0.015f * s), wood);
        }

        // ------------------------------------------------------------ 西域 --
        static void Tarim(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.36f * s, t = 0.2f * s;
            Color earth = H("#cfb487"), earth2 = H("#bca274");
            Base(mb, r, H("#d2bc8a"));
            // 夯土墙：各段高低不一（风蚀）
            var Wl = new[] { new[] { -r, -r, -0.15f * s, -r }, new[] { 0.15f * s, -r, r, -r }, new[] { r, -r, r, r }, new[] { r, r, -r, r }, new[] { -r, r, -r, -r } };
            for (int i = 0; i < Wl.Length; i++) { var w = Wl[i]; Seg(mb, w[0], w[1], w[2], w[3], 0, wallH * (1 - (i % 2) * 0.12f), t, i % 2 != 0 ? earth2 : earth, wallH * (0.88f + (i % 3) * 0.06f)); }
            for (int cx = -1; cx <= 1; cx += 2) for (int cz = -1; cz <= 1; cz += 2) if (!(cx > 0 && cz > 0)) mb.Box(V(cx * r, wallH * 0.6f, cz * r), V(t * 1.4f, wallH * 1.2f, t * 1.4f), earth2);
            // 城门（两侧门墩）
            for (int sx = -1; sx <= 1; sx += 2) mb.Box(V(sx * 0.2f * s, wallH * 0.6f, -r), V(0.12f * s, wallH * 1.2f, t * 1.3f), earth2);
            // 绿洲：水池 + 钻天杨 + 果园
            Face(mb, new[] { V(-0.55f * s, 0.105f, 0.2f * s), V(-0.25f * s, 0.105f, 0.2f * s), V(-0.25f * s, 0.105f, 0.5f * s), V(-0.55f * s, 0.105f, 0.5f * s) }, H("#4f8a8a"), Vector3.up);
            foreach (var q in new[] { new[] { -0.68f, 0.15f }, new[] { -0.68f, 0.4f }, new[] { -0.68f, 0.65f }, new[] { -0.1f, 0.62f }, new[] { 0.12f, 0.66f }, new[] { 0.5f, -0.3f } }) Poplar(mb, q[0] * s, q[1] * s, s * 0.85f);
            // 平顶房
            var hs = new[] { new[] { -0.5f, -0.5f, 0.28f }, new[] { 0.45f, -0.55f, 0.26f }, new[] { 0.55f, -0.05f, 0.24f }, new[] { -0.15f, -0.3f, 0.22f }, new[] { 0.2f, -0.3f, 0.2f } };
            for (int i = 0; i < hs.Length; i++) FlatHouse(mb, hs[i][0] * s, hs[i][1] * s, hs[i][2] * s, hs[i][2] * 0.85f * s, (0.13f + (i % 2) * 0.04f) * s, i % 2 != 0 ? earth : H("#dcc79c"));
            // 小佛塔（龟兹、于阗多佛寺）或烽燧
            if (cap) Stupa(mb, 0.25f * s, 0.35f * s, 1.0f * s);
            else
            {
                mb.Box(V(0.3f * s, 0.25f * s, 0.4f * s), V(0.16f * s, 0.5f * s, 0.16f * s), earth2);   // 烽燧
                mb.Box(V(0.3f * s, 0.52f * s, 0.4f * s), V(0.2f * s, 0.04f * s, 0.2f * s), earth);
            }
            Standard(mb, 0, -r - 0.12f * s, 0.6f * s, H("#a83a2a"), "flag");
        }

        // ------------------------------------------------- 南海（林邑、扶南）--
        static void SeAsia(MeshBuilder mb, float s, bool cap)
        {
            float r = s;
            Color bamboo = H("#a8945a"), thatch = H("#9a8048"), wood = H("#7a5a3a"), brick = H("#b0643e");
            Base(mb, r, H("#6f8a4a"));
            // 水渠（南、西）
            Face(mb, new[] { V(-r * 1.12f, 0.105f, -r * 1.12f), V(r * 1.12f, 0.105f, -r * 1.12f), V(r * 1.12f, 0.105f, -r * 0.95f), V(-r * 1.12f, 0.105f, -r * 0.95f) }, H("#4a7a7a"), Vector3.up);
            Face(mb, new[] { V(-r * 1.12f, 0.105f, -r * 0.95f), V(-r * 0.95f, 0.105f, -r * 0.95f), V(-r * 0.95f, 0.105f, r * 1.12f), V(-r * 1.12f, 0.105f, r * 1.12f) }, H("#4a7a7a"), Vector3.up);
            // 木栅
            float R = r * 0.86f;
            Palisade(mb, new[] { Z(-R, -R), Z(-0.14f * s, -R) }, 0.16f * s, wood, 5);
            Palisade(mb, new[] { Z(0.14f * s, -R), Z(R, -R), Z(R, R * 0.4f) }, 0.16f * s, wood, 5);
            Palisade(mb, new[] { Z(R * 0.4f, R), Z(-R, R), Z(-R, -R) }, 0.16f * s, wood, 5);
            // 高脚屋（鞍形屋顶）
            var hs = new[] { new[] { -0.5f, -0.45f, 0.24f, 0.2f }, new[] { 0.45f, -0.5f, 0.22f, 0.2f }, new[] { -0.55f, 0.1f, 0.2f, 0.26f }, new[] { 0.55f, -0.05f, 0.2f, 0.24f }, new[] { 0.0f, -0.55f, 0.2f, 0.18f } };
            for (int i = 0; i < hs.Length; i++) StiltHouse(mb, hs[i][0] * s, hs[i][1] * s, hs[i][2] * s, hs[i][3] * s, 0.14f * s, 0.1f * s, bamboo, i % 2 != 0 ? thatch : Sh(thatch, -0.08f), true);
            // 神殿：砖台 + 高耸的多重顶
            float tx = -0.05f * s, tz = 0.35f * s, k = cap ? 1.25f : 1;
            mb.Box(V(tx, 0.08f * s * k, tz), V(0.5f * s * k, 0.16f * s * k, 0.5f * s * k), brick);
            mb.Box(V(tx, 0.22f * s * k, tz), V(0.34f * s * k, 0.12f * s * k, 0.34f * s * k), Sh(brick, 0.08f));
            mb.Box(V(tx, 0.36f * s * k, tz), V(0.22f * s * k, 0.16f * s * k, 0.22f * s * k), H("#c8a050"));
            Hip(mb, tx, 0.44f * s * k, tz, 0.34f * s * k, 0.34f * s * k, 0.12f * s * k, thatch);
            Hip(mb, tx, 0.53f * s * k, tz, 0.22f * s * k, 0.22f * s * k, 0.2f * s * k, Sh(thatch, -0.1f));
            Cone(mb, V(tx, 0.72f * s * k, tz), 0.02f * s, 0.14f * s, 4, H("#d8b050"));
            if (cap) StiltHouse(mb, 0.5f * s, 0.5f * s, 0.32f * s, 0.24f * s, 0.16f * s, 0.14f * s, H("#b8a060"), Sh(thatch, -0.12f), true);
            // 椰树
            foreach (var q in new[] { new[] { -0.75f, 0.6f, 1 }, new[] { 0.7f, 0.3f, 0.9f }, new[] { -0.25f, -0.2f, 0.8f }, new[] { 0.3f, 0.6f, 0.95f }, new[] { -0.72f, -0.25f, 0.85f } }) Palm(mb, q[0] * s, q[1] * s, s * q[2], (int)(WorldGeo.D(q[0]) * 100 + WorldGeo.D(q[1]) * 10));
            Standard(mb, 0, -r * 0.95f, 0.55f * s, H("#c8a030"), "flag");
        }

        // ------------------------------------------------ 夷洲 / 南中：高脚村落 --
        static void Village(MeshBuilder mb, float s, string culture)
        {
            float r = s; bool nan = culture == "nanman";
            Color bamboo = H("#a8945a"), thatch = nan ? H("#8a7448") : H("#a08a50"), wood = H("#6a5034");
            Base(mb, r, nan ? H("#7a8452") : H("#6f8a4a"));
            float R = r * 0.9f;
            Palisade(mb, new[] { Z(-R, -R), Z(-0.14f * s, -R) }, 0.2f * s, wood, 6);
            Palisade(mb, new[] { Z(0.14f * s, -R), Z(R, -R), Z(R, R * 0.4f) }, 0.2f * s, wood, 6);
            Palisade(mb, new[] { Z(R * 0.4f, R), Z(-R, R), Z(-R, -R) }, 0.2f * s, wood, 6);
            var hs = new[] { new[] { -0.5f, -0.45f, 0.26f, 0.2f }, new[] { 0.45f, -0.5f, 0.24f, 0.2f }, new[] { -0.55f, 0.15f, 0.22f, 0.28f }, new[] { 0.5f, -0.05f, 0.22f, 0.26f }, new[] { 0.0f, -0.4f, 0.2f, 0.18f }, new[] { -0.1f, 0.45f, 0.36f, 0.26f } };
            for (int i = 0; i < hs.Length; i++) StiltHouse(mb, hs[i][0] * s, hs[i][1] * s, hs[i][2] * s, hs[i][3] * s, 0.12f * s, (i == 5 ? 0.14f : 0.1f) * s, bamboo, i % 2 != 0 ? thatch : Sh(thatch, -0.08f), nan);
            if (nan)
            {
                // 铜鼓祭台
                Cyl(mb, V(0.4f * s, 0.1f, 0.45f * s), 0.1f * s, 0.12f * s, 0.12f * s, 8, H("#7a8a5a"), true);
                Cyl(mb, V(0.4f * s, 0.1f + 0.12f * s, 0.45f * s), 0.13f * s, 0.13f * s, 0.03f * s, 8, H("#9aa070"), true);
            }
            else
            {
                foreach (var q in new[] { new[] { 0.45f, 0.5f }, new[] { 0.65f, 0.25f } }) Palm(mb, q[0] * s, q[1] * s, s * 0.9f, (int)(WorldGeo.D(q[0]) * 77));
            }
            Tree(mb, -0.72f * s, 0.7f * s, 0.6f * s, H("#3f6a34"));
            Standard(mb, 0, -R, 0.5f * s, H("#b02a24"), "flag");
        }

        // --------------------------------------------- 欧洲蛮族：山丘堡垒 --
        static void Hillfort(MeshBuilder mb, float s, bool cap, string culture)
        {
            float r = s;
            Color earth = H("#7a8a4e"), earth2 = H("#6a7844"), wood = H("#7a5a3a"), thatch = H("#a08a50"), wall = H("#a08868");
            Base(mb, r, H("#6f804a"));
            // 土垒：两圈（外低内高）
            RingWall(mb, r * 1.02f, 14, 0.12f * s, 0.16f * s, earth2, 0);
            Cyl(mb, V(0, 0, 0), r * 0.86f, r * 0.8f, 0.2f * s, 14, earth, true);
            float y0 = 0.2f * s;
            // 木栅（土台边缘，南面留门）
            float R = r * 0.8f; const int n = 12;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++) { float a = -PI / 2 + PI / n + i * PI * 2 / n; pts[i] = Z(Mathf.Cos(a) * R, Mathf.Sin(a) * R); }
            for (int i = 0; i < n - 1; i++) Palisade(mb, new[] { pts[i], pts[i + 1] }, 0.16f * s, wood, 5, y0);
            // 门楼
            for (int sx = -1; sx <= 1; sx += 2) mb.Box(V(sx * 0.12f * s, y0 + 0.14f * s, -R), V(0.05f * s, 0.28f * s, 0.05f * s), wood);
            mb.Box(V(0, y0 + 0.28f * s, -R), V(0.32f * s, 0.05f * s, 0.08f * s), wood);
            if (culture == "celt")
            {
                // 圆形茅屋
                foreach (var q in new[] { new[] { -0.35f, -0.3f, 1 }, new[] { 0.35f, -0.3f, 0.9f }, new[] { -0.4f, 0.25f, 0.95f }, new[] { 0.3f, 0.3f, 1 }, new[] { 0.0f, 0.0f, cap ? 1.5f : 1.15f } })
                {
                    float x = q[0], z = q[1], k = q[2];
                    Cyl(mb, V(x * s, y0, z * s), 0.14f * k * s, 0.14f * k * s, 0.1f * k * s, 8, wall, false);
                    Cone(mb, V(x * s, y0 + 0.1f * k * s, z * s), 0.18f * k * s, 0.22f * k * s, 8, thatch);
                }
            }
            else
            {
                // 长屋（日耳曼）
                foreach (var q in new[] { new[] { -0.3f, -0.3f, 0.5f, 0.2f }, new[] { 0.35f, -0.2f, 0.2f, 0.46f }, new[] { -0.3f, 0.3f, 0.46f, 0.2f } }) GableHouse(mb, q[0] * s, q[1] * s, q[2] * s, q[3] * s, 0.08f * s, wall, thatch, q[3] > q[2], y0, 0.2f * s);
                if (cap)
                {
                    GableHouse(mb, 0.2f * s, 0.35f * s, 0.56f * s, 0.26f * s, 0.14f * s, H("#8a6a48"), H("#8a7444"), false, y0, 0.3f * s);
                    for (int sx = -1; sx <= 1; sx += 2) mb.Box(V((0.2f + sx * 0.3f) * s, y0 + 0.48f * s, 0.35f * s), V(0.02f * s, 0.12f * s, 0.02f * s), wood);   // 山墙交叉兽头
                }
            }
            Tree(mb, 0.85f * s, -0.75f * s, 0.6f * s, H("#3f6a34"));
            Tree(mb, -0.95f * s, 0.6f * s, 0.7f * s, H("#3f6a34"));
            Standard(mb, -0.2f * s, 0.1f * s, 0.75f * s, culture == "celt" ? H("#2a6a3a") : H("#7a2a24"), "flag", y0);
        }

        // ------------------------------------------------ 萨尔马提亚：车营 / 希腊城 --
        static void Sarmatian(MeshBuilder mb, float s, bool cap, City city)
        {
            float r = s;
            if (cap || (city != null && city.town >= 350)) { Greek(mb, s, cap); return; }
            Color felt = H("#c8b898"), wood = H("#6a4a2a");
            Base(mb, r, H("#93a05e"));
            // 篷车围成的车阵
            for (int i = 0; i < 9; i++)
            {
                float a = PI / 2 + i * PI * 2 / 10;
                float x = Mathf.Cos(a) * r * 0.78f, z = Mathf.Sin(a) * r * 0.78f;
                if (x > 0.35f * r && z > 0.35f * r) continue;
                mb.Box(V(x, 0.14f * s, z), V(0.3f * s, 0.07f * s, 0.17f * s), wood);
                Cyl(mb, V(x, 0.175f * s, z), 0.12f * s, 0.04f * s, 0.18f * s, 6, felt, false);
                for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2) mb.Box(V(x + sx * 0.1f * s, 0.07f * s, z + sz * 0.09f * s), V(0.11f * s, 0.11f * s, 0.02f * s), Sh(wood, -0.25f));
            }
            foreach (var q in new[] { new[] { -0.3f, -0.25f, 1 }, new[] { 0.25f, -0.3f, 0.9f }, new[] { 0.0f, 0.25f, 1.3f }, new[] { -0.4f, 0.3f, 0.85f }, new[] { 0.3f, 0.2f, 0.8f } }) Tent(mb, q[0] * s, q[1] * s, 0.2f * q[2] * s, q[2] > 1 ? H("#d8c8a0") : felt);
            foreach (var q in new[] { new[] { -0.15f, -0.75f }, new[] { 0.15f, -0.75f } }) Standard(mb, q[0] * s, q[1] * s, 0.8f * s, H("#a83a2a"), "tail");
        }
        // 希腊城（博斯普鲁斯）：白色城墙、卫城丘与神庙
        static void Greek(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.36f * s, t = 0.15f * s;
            Color stone = H("#dcd6c6"), tile = H("#b4553a"), marble = H("#f2eee4"), stucco = H("#ece4d0");
            Base(mb, r, H("#a8a07c"));
            SquareWall(mb, r, wallH, t, stone, 0.07f * s, 3);
            for (int cx = -1; cx <= 1; cx += 2) for (int cz = -1; cz <= 1; cz += 2) if (!(cx > 0 && cz > 0)) mb.Box(V(cx * r, wallH * 0.65f, cz * r), V(t * 1.7f, wallH * 1.3f, t * 1.7f), Sh(stone, -0.05f));
            Gateway(mb, r, 0.22f * s, wallH, stone, Sh(stone, -0.05f), false);
            mb.Sphere(V(-0.1f * s, 0.05f, 0.35f * s), V(0.55f * s, 0.26f * s, 0.45f * s), H("#9a9670"), 2, new SphereOpt { seed = 5, rough = 0.06f, bottomShade = 0.1f });
            float y0 = 0.22f * s, tz = 0.35f * s;
            mb.Box(V(-0.1f * s, y0 + 0.04f * s, tz), V(0.46f * s, 0.08f * s, 0.32f * s), marble);
            for (int i = 0; i < 5; i++) Column(mb, (-0.28f + i * 0.09f) * s, y0 + 0.08f * s, tz - 0.12f * s, 0.22f * s, 0.018f * s, marble);
            mb.Box(V(-0.1f * s, y0 + 0.19f * s, tz + 0.04f * s), V(0.34f * s, 0.22f * s, 0.2f * s), stucco);
            Gable(mb, -0.1f * s, y0 + 0.31f * s, tz, 0.44f * s, 0.3f * s, 0.08f * s, tile, false, 0.01f * s);
            foreach (var q in new[] { new[] { -0.55f, -0.5f }, new[] { 0.5f, -0.5f }, new[] { 0.6f, 0.0f }, new[] { -0.65f, -0.05f } }) GableHouse(mb, q[0] * s, q[1] * s, 0.26f * s, 0.22f * s, 0.16f * s, stucco, tile, false);
            Standard(mb, 0.25f * s, -0.3f * s, 0.6f * s, H("#a8261e"), "flag");
        }

        // ======================================== 第三版：天竺、阿克苏姆 / 库施、毛里 --
        // 四棱台（塔门、阶梯台基）：底 w0 × d0，顶 w1 × d1，高 h
        static void Frustum(MeshBuilder mb, float x, float y0, float z, float w0, float d0, float w1, float d1, float h, Color c)
        {
            var a = new[] { V(x - w0 / 2, y0, z - d0 / 2), V(x + w0 / 2, y0, z - d0 / 2), V(x + w0 / 2, y0, z + d0 / 2), V(x - w0 / 2, y0, z + d0 / 2) };
            var b = new[] { V(x - w1 / 2, y0 + h, z - d1 / 2), V(x + w1 / 2, y0 + h, z - d1 / 2), V(x + w1 / 2, y0 + h, z + d1 / 2), V(x - w1 / 2, y0 + h, z + d1 / 2) };
            var outv = new[] { V(0, 0.2f, -1), V(1, 0.2f, 0), V(0, 0.2f, 1), V(-1, 0.2f, 0) };
            for (int i = 0; i < 4; i++) Face(mb, new[] { a[i], a[(i + 1) % 4], b[(i + 1) % 4], b[i] }, i == 0 ? c : Sh(c, i == 2 ? -0.14f : -0.07f), outv[i]);
            Face(mb, b, Sh(c, 0.08f), Vector3.up);
        }
        // 筒拱顶的屋子（天竺的支提式屋顶）：墙身 + 沿 z 的半圆筒顶，南面山墙开马蹄形支提窗
        static void BarrelHouse(MeshBuilder mb, float x, float z, float w, float d, float h, Color wallC, Color roofC, float y0 = 0, bool win = true)
        {
            mb.Box(V(x, y0 + h / 2, z), V(w, h, d), wallC, 0.1f);
            float r = w / 2 * 1.06f, y = y0 + h;
            Vault(mb, x, y, z + d / 2 + 0.01f, z - d / 2 - 0.01f, r, roofC, 10);
            // 北面山墙封口
            var back = new Vector3[9];
            for (int i = 0; i <= 8; i++) { float a = i / 8f * PI; back[i] = V(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r, z + d / 2 + 0.01f); }
            Face(mb, back, Sh(roofC, -0.2f), V(0, 0, 1));
            if (win)
            {
                ArchFrame(mb, x, y - r * 0.05f, z - d / 2 - 0.012f, r * 1.05f, r * 0.9f, 0, -1, Sh(wallC, 0.12f), false, 0.012f * w / 0.2f);
                Arch(mb, x, y - r * 0.05f, z - d / 2 - 0.014f, r * 0.75f, r * 0.72f, 0, -1, H("#2e2016"), false);
            }
        }
        // 塔门（torana）：两柱、三道两端出挑的横梁
        static void Torana(MeshBuilder mb, float x, float z, float k, Color c, float y0 = 0)
        {
            for (int sx = -1; sx <= 1; sx += 2) mb.Box(V(x + sx * 0.1f * k, y0 + 0.17f * k, z), V(0.03f * k, 0.34f * k, 0.03f * k), c);
            foreach (var q in new[] { new[] { 0.24f, 0.3f }, new[] { 0.3f, 0.34f }, new[] { 0.36f, 0.36f } })
            {
                float y = q[0], w = q[1];
                mb.Box(V(x, y0 + y * k, z), V(w * k, 0.026f * k, 0.03f * k), Sh(c, y == 0.3f ? -0.06f : 0.04f));
                for (int sx = -1; sx <= 1; sx += 2) mb.Box(V(x + sx * (w / 2) * k, y0 + (y + 0.012f) * k, z), V(0.03f * k, 0.04f * k, 0.034f * k), Sh(c, 0.08f));   // 出挑的涡卷端
            }
            mb.Box(V(x, y0 + 0.41f * k, z), V(0.03f * k, 0.07f * k, 0.03f * k), Sh(c, 0.1f));   // 顶上的法轮
            Cyl(mb, V(x, y0 + 0.45f * k, z - 0.016f * k), 0.035f * k, 0.035f * k, 0.012f * k, 8, H("#d8b050"), true);
        }
        // 天竺（百乘、泰米尔三国、师子国、恒河诸城）：环壕 + 带檐线的红砖城墙、半圆马面与方门楼（筒拱顶）、
        // 城内带栏楯与塔门的窣堵波、支提式筒拱顶的宫殿与民居、椰树与菩提树；都城为两层的筒拱宫殿与更大的佛塔
        static void India(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.38f * s, t = 0.17f * s;
            Color brick = H("#b8664a"), brick2 = H("#a45a40"), plaster = H("#e8dcc0"), thatch = H("#a8884e"), wood = H("#7a5232"), sand = H("#d8c8a0");
            Base(mb, r, H("#8a9058"));
            Moat(mb, r * 1.0f, r * 1.13f, 0.104f, H("#4a7a86"));
            mb.Box(V(0, 0.104f, -r * 1.065f), V(0.2f * s, 0.012f, r * 0.16f), H("#9a8a6a"));   // 南门前的土桥
            SquareWall(mb, r * 0.92f, wallH, t, brick, 0.06f * s, 3);
            WallBands(mb, r * 0.92f, wallH, t, brick2);
            float R = r * 0.92f;
            for (int cx = -1; cx <= 1; cx += 2) for (int cz = -1; cz <= 1; cz += 2) if (!(cx > 0 && cz > 0)) Tower(mb, cx * R, cz * R, t * 1.0f, wallH * 1.22f, brick2, null);
            foreach (var q in new[] { new[] { -R, -0.4f * R }, new[] { -R, 0.4f * R }, new[] { R, -0.4f * R }, new[] { -0.45f * R, R }, new[] { 0.5f * R, -R }, new[] { -0.5f * R, -R } }) Tower(mb, q[0], q[1], t * 0.62f, wallH * 1.05f, brick2, null);
            // 南门：两座方门楼（上覆筒拱小亭）夹门洞
            for (int sx = -1; sx <= 1; sx += 2)
            {
                mb.Box(V(sx * 0.19f * s, wallH * 0.62f, -R), V(0.15f * s, wallH * 1.24f, t * 1.35f), brick2, 0.06f);
                BarrelHouse(mb, sx * 0.19f * s, -R, 0.12f * s, 0.14f * s, 0.05f * s, plaster, thatch, wallH * 1.24f, false);
            }
            ArchFrame(mb, 0, 0.1f, -R - t * 0.68f, 0.15f * s, wallH * 0.82f, 0, -1, plaster, false, 0.016f * s);
            Arch(mb, 0, 0.1f, -R - t * 0.68f, 0.15f * s, wallH * 0.82f, 0, -1, H("#2e2016"), false);
            // 窣堵波 + 正面塔门
            float k = cap ? 1.3f * s : 1.05f * s, sx0 = -0.3f * s, sz0 = 0.3f * s;
            Stupa(mb, sx0, sz0, k, 0.1f);
            Torana(mb, sx0, sz0 - 0.36f * k, k, sand, 0.1f);
            // 宫殿：台基 + 列柱厅 + 支提式筒拱顶（都城两层）
            float px = 0.36f * s, pz = 0.12f * s, ph = cap ? 1.25f : 1;
            mb.Box(V(px, 0.1f + 0.04f * s, pz), V(0.42f * s, 0.08f * s, 0.4f * s), brick2, 0.06f);
            for (int i = 0; i < 3; i++) mb.Box(V(px, 0.1f + 0.013f * s * (i + 0.5f), pz - 0.2f * s - (2.5f - i) * 0.02f * s), V(0.14f * s, 0.026f * s * (i + 1) / 2, 0.02f * s), brick);
            if (cap)
            {
                mb.Box(V(px, 0.1f + 0.08f * s + 0.08f * s, pz), V(0.36f * s, 0.16f * s, 0.32f * s), plaster, 0.06f);
                for (int i = 0; i < 4; i++) Column(mb, px + (-0.13f + i * 0.087f) * s, 0.1f + 0.08f * s, pz - 0.17f * s, 0.15f * s, 0.014f * s, H("#f0e8d8"));
                mb.Box(V(px, 0.1f + 0.245f * s, pz), V(0.4f * s, 0.02f * s, 0.36f * s), wood, 0.06f);
                BarrelHouse(mb, px, pz + 0.02f * s, 0.24f * s, 0.26f * s, 0.1f * s, plaster, thatch, 0.1f + 0.255f * s);
            }
            else
            {
                for (int i = 0; i < 3; i++) Column(mb, px + (-0.12f + i * 0.12f) * s, 0.1f + 0.08f * s, pz - 0.16f * s, 0.12f * s, 0.014f * s, H("#f0e8d8"));
                BarrelHouse(mb, px, pz + 0.02f * s, 0.3f * s * ph, 0.28f * s, 0.13f * s, plaster, thatch, 0.1f + 0.08f * s);
            }
            // 民居（筒拱茅顶）
            var hs = new[] { new[] { -0.6f, -0.5f, 0.17f, 0.22f }, new[] { 0.58f, -0.52f, 0.16f, 0.2f }, new[] { -0.25f, -0.55f, 0.15f, 0.2f }, new[] { 0.62f, -0.18f, 0.15f, 0.2f }, new[] { -0.66f, -0.12f, 0.15f, 0.2f } };
            for (int i = 0; i < hs.Length; i++) BarrelHouse(mb, hs[i][0] * s, hs[i][1] * s, hs[i][2] * s, hs[i][3] * s, 0.1f * s, i % 2 != 0 ? plaster : H("#d0b48a"), i % 2 != 0 ? thatch : Sh(thatch, -0.1f), 0.1f, i % 2 == 0);
            // 椰树、菩提树
            Palm(mb, 0.15f * s, -0.25f * s, s * 0.85f, 21, 0.1f); Palm(mb, -0.72f * s, 0.62f * s, s * 0.9f, 22, 0.1f);
            Tree(mb, 0.08f * s, 0.62f * s, 0.55f * s, H("#3f7a3a"), 0.1f);
            Standard(mb, 0.0f, -0.3f * s, 0.68f * s, H("#d8a030"), "flag", 0.1f);
        }
        // 非洲：库施（麦罗埃、纳帕塔、卡拉诺格）= 土砖城、陡峭的王陵金字塔、带塔门（pylon）的神庙、椰枣树；
        //       阿克苏姆（阿克苏姆、阿杜利斯）= 花岗岩石碑（假窗与圆顶）、阶梯台基上的石砌宫殿（“猴头”木梁端）、圆形茅屋
        static readonly HashSet<string> Kush = new HashSet<string> { "meroe", "napata", "karanog" };
        static void Stele(MeshBuilder mb, float x, float z, float h, float s, float y0)
        {
            Color g = H("#9a8f80"), g2 = H("#6a6056");
            mb.Box(V(x, y0 + h * s / 2, z), V(0.07f * s, h * s, 0.04f * s), g, 0.04f);
            mb.Lathe(V(x, y0 + h * s, z), new[] { L(0.035f * s, 0), L(0.03f * s, 0.03f * s), L(0, 0.05f * s) }, 10, g);
            int nw = Math.Max(3, WorldGeo.JsRound(h * 6));
            for (int j = 1; j < nw; j++) mb.Box(V(x, y0 + h * s * j / nw, z - 0.021f * s), V(0.05f * s, 0.025f * s, 0.004f), g2);   // 假窗
            mb.Box(V(x, y0 + 0.012f * s, z - 0.05f * s), V(0.1f * s, 0.024f * s, 0.06f * s), Sh(g, -0.06f));   // 祭台
        }
        static void Africa(MeshBuilder mb, float s, bool cap, City city)
        {
            float r = s;
            if (city != null && Kush.Contains(city.key))
            {
                Color mud = H("#b07a50"), mud2 = H("#9a6a44"), sand = H("#d8bc8a"), stone = H("#c8a878"), dark = H("#2e2016");
                Base(mb, r, H("#c8a878"));
                // 尼罗河岸（西侧一条水带）
                Face(mb, new[] { V(-r * 1.17f, 0.104f, -r * 1.17f), V(-r * 1.0f, 0.104f, -r * 1.17f), V(-r * 1.0f, 0.104f, r * 1.17f), V(-r * 1.17f, 0.104f, r * 1.17f) }, H("#4a7a86"), Vector3.up);
                float R = r * 0.86f, wallH = 0.3f * s, t = 0.15f * s;
                SquareWall(mb, R, wallH, t, mud, 0.05f * s, 3);
                WallBands(mb, R, wallH, t, mud2);
                for (int cx = -1; cx <= 1; cx += 2) for (int cz = -1; cz <= 1; cz += 2) if (!(cx > 0 && cz > 0)) mb.Box(V(cx * R, wallH * 0.6f, cz * R), V(t * 1.5f, wallH * 1.2f, t * 1.5f), mud2, 0.06f);
                // 南门：一对塔门（梯形塔）
                for (int sx = -1; sx <= 1; sx += 2) Frustum(mb, sx * 0.17f * s, 0, -R, 0.16f * s, t * 1.3f, 0.12f * s, t * 1.0f, wallH * 1.35f, stone);
                Arch(mb, 0, 0.08f, -R - t * 0.66f, 0.12f * s, wallH * 0.8f, 0, -1, dark, false);
                // 阿蒙 / 狮神庙：塔门 + 列柱院 + 圣殿
                float tx = 0.3f * s, tz = -0.08f * s, k = cap ? 1.2f : 1;
                for (int sx = -1; sx <= 1; sx += 2) Frustum(mb, tx + sx * 0.11f * s * k, 0.1f, tz, 0.15f * s * k, 0.08f * s * k, 0.11f * s * k, 0.06f * s * k, 0.32f * s * k, stone);
                mb.Box(V(tx, 0.1f + 0.11f * s * k, tz + 0.005f), V(0.07f * s * k, 0.22f * s * k, 0.05f * s * k), Sh(stone, -0.05f));
                mb.Box(V(tx, 0.1f + 0.08f * s * k, tz - 0.026f * s * k), V(0.05f * s * k, 0.16f * s * k, 0.004f), dark);
                mb.Box(V(tx, 0.1f + 0.09f * s * k, tz + 0.2f * s * k), V(0.26f * s * k, 0.18f * s * k, 0.3f * s * k), stone, 0.06f);
                for (int i = 0; i < 3; i++) Column(mb, tx + (-0.09f + i * 0.09f) * s * k, 0.1f, tz + 0.06f * s * k, 0.16f * s * k, 0.013f * s * k, H("#e0c898"));
                // 王陵金字塔（陡峭，东面附小祭堂）
                var pyr = cap ? new[] { new[] { -0.42f, 0.42f, 0.3f }, new[] { -0.08f, 0.55f, 0.24f }, new[] { -0.6f, 0.02f, 0.22f }, new[] { 0.2f, 0.62f, 0.18f } } : new[] { new[] { -0.42f, 0.42f, 0.28f }, new[] { -0.1f, 0.55f, 0.22f }, new[] { -0.6f, 0.04f, 0.19f } };
                foreach (var q in pyr)
                {
                    float x = q[0], z = q[1], w = q[2];
                    mb.Box(V(x * s, 0.1f + 0.012f * s, z * s), V(w * s * 1.04f, 0.024f * s, w * s * 1.04f), Sh(sand, -0.08f));
                    Hip(mb, x * s, 0.1f + 0.024f * s, z * s, w * s, w * s, w * s * 1.85f, sand);
                    mb.Box(V(x * s, 0.1f + 0.03f * s, (z - w / 2 - 0.04f) * s), V(w * 0.4f * s, 0.06f * s, 0.07f * s), stone, 0.05f);
                    Frustum(mb, x * s, 0.1f, (z - w / 2 - 0.085f) * s, w * 0.5f * s, 0.03f * s, w * 0.42f * s, 0.025f * s, 0.075f * s, Sh(stone, 0.04f));
                }
                // 土砖平顶房、椰枣树
                foreach (var q in new[] { new[] { -0.5f, -0.5f, 0.2f, 0.18f }, new[] { 0.55f, -0.55f, 0.18f, 0.16f }, new[] { -0.18f, -0.6f, 0.16f, 0.16f }, new[] { 0.62f, 0.22f, 0.16f, 0.2f } }) FlatHouse(mb, q[0] * s, q[1] * s, q[2] * s, q[3] * s, 0.12f * s, mud, 0.1f);
                Palm(mb, -0.92f * s, -0.75f * s, s * 0.85f, 31); Palm(mb, -0.93f * s, -0.35f * s, s * 0.75f, 32); Palm(mb, 0.62f * s, -0.25f * s, s * 0.7f, 33, 0.1f);
                Standard(mb, 0.0f, -0.32f * s, 0.68f * s, H("#d8b050"), "flag", 0.1f);
                return;
            }
            // 阿克苏姆
            Color stone2a = H("#a89a86"), stone2 = H("#8e8270"), plaster = H("#d8ccb4"), timber = H("#3a2a1e"), thatch = H("#a08850");
            Base(mb, r, H("#86865a"));
            // 干砌石的低矮围墙（阿克苏姆没有高大城墙）
            float Ra = r * 0.94f;
            for (int i = 0; i < 16; i++)
            {
                float a0 = -PI / 2 + 0.22f + i * (PI * 2 - 0.44f) / 16, a1 = a0 + (PI * 2 - 0.44f) / 16;
                Seg(mb, Mathf.Cos(a0) * Ra, Mathf.Sin(a0) * Ra, Mathf.Cos(a1) * Ra, Mathf.Sin(a1) * Ra, 0.06f, 0.12f * s, 0.07f * s, i % 2 != 0 ? stone2a : stone2);
            }
            // 阶梯台基上的宫殿（塔阿卡·马里亚姆式）：三层退台、四角方塔、墙面成排的「猴头」木梁端
            float ppx = -0.05f * s, ppz = 0.12f * s, kk = cap ? 1.15f : 1;
            float y = 0.1f;
            foreach (var q in new[] { new[] { 0.7f, 0.06f }, new[] { 0.58f, 0.06f }, new[] { 0.48f, 0.05f } }) { mb.Box(V(ppx, y + q[1] * s / 2, ppz), V(q[0] * s * kk, q[1] * s, q[0] * s * kk * 0.85f), stone2, 0.04f); y += q[1] * s; }
            float bw = 0.34f * s * kk, bd = 0.28f * s * kk, bh = 0.24f * s * kk;
            mb.Box(V(ppx, y + bh / 2, ppz), V(bw, bh, bd), plaster, 0.06f);
            for (int j = 0; j < 3; j++) for (int i = 0; i < 5; i++) mb.Box(V(ppx + (-0.14f + i * 0.07f) * s * kk, y + (0.05f + j * 0.075f) * s * kk, ppz - bd / 2 - 0.004f), V(0.016f * s, 0.016f * s, 0.012f * s), timber);
            mb.Box(V(ppx, y + bh * 0.3f, ppz - bd / 2 - 0.006f), V(0.06f * s, bh * 0.6f, 0.006f), H("#2e2016"));
            for (int cx = -1; cx <= 1; cx += 2) for (int cz = -1; cz <= 1; cz += 2) mb.Box(V(ppx + cx * bw / 2, y + bh * 0.62f, ppz + cz * bd / 2), V(0.08f * s * kk, bh * 1.24f, 0.08f * s * kk), stone2a, 0.06f);
            mb.Box(V(ppx, y + bh + 0.008f * s, ppz), V(bw * 1.04f, 0.016f * s, bd * 1.04f), Sh(plaster, 0.08f));
            // 前阶
            for (int i = 0; i < 3; i++) mb.Box(V(ppx, 0.1f + (i + 0.5f) * 0.055f * s, ppz - 0.36f * s * kk + i * 0.035f * s), V(0.14f * s, 0.055f * s, 0.035f * s), stone2a);
            // 石碑群（王陵区）：最高的在西北
            var st = cap ? new[] { new[] { -0.62f, 0.55f, 1.15f }, new[] { -0.45f, 0.62f, 0.8f }, new[] { -0.75f, 0.32f, 0.6f }, new[] { -0.3f, 0.72f, 0.5f } } : new[] { new[] { -0.62f, 0.55f, 0.85f }, new[] { -0.45f, 0.62f, 0.6f }, new[] { -0.75f, 0.32f, 0.45f } };
            foreach (var q in st) Stele(mb, q[0] * s, q[1] * s, q[2], s, 0.1f);
            // 圆形茅屋与石屋
            foreach (var q in new[] { new[] { 0.55f, -0.5f, 1 }, new[] { 0.32f, -0.62f, 0.85f }, new[] { -0.55f, -0.5f, 0.95f }, new[] { 0.62f, -0.15f, 0.9f } })
            {
                float x = q[0], z = q[1], k2 = q[2];
                Cyl(mb, V(x * s, 0.1f, z * s), 0.09f * k2 * s, 0.09f * k2 * s, 0.08f * k2 * s, 8, plaster, false);
                Cone(mb, V(x * s, 0.1f + 0.08f * k2 * s, z * s), 0.12f * k2 * s, 0.14f * k2 * s, 8, thatch);
            }
            FlatHouse(mb, -0.3f * s, -0.55f * s, 0.2f * s, 0.16f * s, 0.11f * s, stone2a, 0.1f);
            Tree(mb, 0.55f * s, 0.35f * s, 0.5f * s, H("#4a6a34"), 0.1f);
            Standard(mb, 0.25f * s, -0.3f * s, 0.68f * s, H("#d8b050"), "flag", 0.1f);
        }
        // 毛里（巴夸特）：中阿特拉斯山前的山头聚落——土石台地、干砌石环墙、平顶石屋、游牧帐篷、方形谷仓堡（agadir）、雪松
        static void Moor(MeshBuilder mb, float s, bool cap)
        {
            float r = s;
            Color stone = H("#b8a07e"), stone2 = H("#a08868"), earth = H("#9a8a5e"), tentC = H("#5a4a3a"), cedar = H("#2f5a34");
            Base(mb, r, H("#8a8a5a"));
            Cyl(mb, V(0, 0, 0.05f * s), r * 0.84f, r * 0.72f, 0.16f * s, 14, earth, true);
            float y0 = 0.16f * s, R = r * 0.76f;
            for (int i = 0; i < 12; i++)
            {
                float a0 = -PI / 2 + 0.3f + i * (PI * 2 - 0.6f) / 12, a1 = a0 + (PI * 2 - 0.6f) / 12;
                Seg(mb, Mathf.Cos(a0) * R, Mathf.Sin(a0) * R + 0.05f * s, Mathf.Cos(a1) * R, Mathf.Sin(a1) * R + 0.05f * s, y0 - 0.02f * s, 0.12f * s, 0.06f * s, i % 2 != 0 ? stone : stone2);
            }
            // 谷仓堡：高方塔，墙上一排小洞
            float gx = 0.1f * s, gz = 0.3f * s, gh = (cap ? 0.5f : 0.4f) * s;
            Frustum(mb, gx, y0, gz, 0.24f * s, 0.24f * s, 0.2f * s, 0.2f * s, gh, stone);
            for (int j = 0; j < 3; j++) for (int i = -1; i <= 1; i++) mb.Box(V(gx + i * 0.06f * s, y0 + (0.14f + j * 0.1f) * gh / 0.4f, gz - 0.112f * s), V(0.02f * s, 0.03f * s, 0.006f), H("#2e2016"));
            for (int i = -2; i <= 2; i++) mb.Box(V(gx + i * 0.045f * s, y0 + gh + 0.015f * s, gz - 0.1f * s), V(0.03f * s, 0.03f * s, 0.02f * s), stone2);
            // 平顶石屋与帐篷
            foreach (var q in new[] { new[] { -0.35f, -0.3f, 0.2f, 0.16f }, new[] { 0.35f, -0.32f, 0.18f, 0.16f }, new[] { -0.42f, 0.12f, 0.16f, 0.18f }, new[] { 0.42f, 0.0f, 0.16f, 0.16f }, new[] { -0.12f, -0.42f, 0.14f, 0.14f } }) FlatHouse(mb, q[0] * s, q[1] * s, q[2] * s, q[3] * s, 0.1f * s, stone, y0);
            foreach (var q in new[] { new[] { -0.82f, -0.72f }, new[] { 0.8f, -0.74f }, new[] { -0.86f, 0.52f } }) Tent(mb, q[0] * s, q[1] * s, 0.16f * s, tentC, 0.1f);
            Tree(mb, -0.75f * s, -0.15f * s, 0.55f * s, cedar); Tree(mb, 0.78f * s, -0.25f * s, 0.5f * s, cedar);
            Standard(mb, -0.15f * s, 0.05f * s, 0.6f * s, H("#3a5a8a"), "tail", y0);
        }

        // ============================================================ 接口 --
        static readonly Dictionary<string, string> ModelOf = new Dictionary<string, string>
        {
            { "han", "han" }, { "roman", "roman" }, { "persia", "persia" }, { "arab", "arab" }, { "kushan", "kushan" }, { "wa", "wa" }, { "korea", "korea" }, { "steppe", "steppe" },
            { "tarim", "tarim" }, { "seasia", "seasia" }, { "yi", "village" }, { "nanman", "village" }, { "celt", "hillfort" }, { "german", "hillfort" }, { "sarmatian", "sarmatian" },
            { "india", "india" }, { "africa", "africa" }, { "moor", "moor" },
        };
        static readonly string[] cultureList = { "roman", "persia", "arab", "kushan", "wa", "korea", "steppe", "tarim", "seasia", "yi", "nanman", "celt", "german", "sarmatian", "india", "africa", "moor" };
        // 已有模型的文化代号（不含 han；与网页版 SG.CultureArt.cultures 同序）
        public static IEnumerable<string> Cultures { get { return cultureList; } }
        // 没有专门模型的文化按 CultureBase（唯一的回退表）取回退文化的模型
        static string ModelKey(string c)
        {
            string m;
            if (c != null && ModelOf.TryGetValue(c, out m)) return m;
            if (ModelOf.TryGetValue(WorldGeo.CultureBase(c), out m)) return m;
            return "han";
        }

        static HashSet<string> capitals;
        static HashSet<string> WorldCapitals()
        {
            if (capitals != null) return capitals;
            var s = new HashSet<string>();
            foreach (var fi in WorldScenarioData.FactionInfo) if (fi != null && !string.IsNullOrEmpty(fi.Capital)) s.Add(fi.Capital);
            capitals = s;
            return s;
        }
        public static string CultureOfCity(City city)
        {
            if (city == null) return "han";
            return string.IsNullOrEmpty(city.culture) ? "han" : city.culture;
        }
        public static bool IsCapital(City city, string culture = null)
        {
            if (city == null) return false;
            if (city.key == "luoyang" || city.key == "changan") return true;
            var c = culture ?? CultureOfCity(city);
            return c != "han" && WorldCapitals().Contains(city.key);
        }
        // 写入 MeshBuilder（调用方先设好 mb.M = 位置 × 朝向）
        public static void CityInto(MeshBuilder mb, float size, string culture, bool capital, City city)
        {
            switch (ModelKey(culture))
            {
                case "roman": Roman(mb, size, capital); break;
                case "persia": Persia(mb, size, capital); break;
                case "arab": Arab(mb, size, capital, city); break;
                case "kushan": Kushan(mb, size, capital); break;
                case "wa": Wa(mb, size, capital); break;
                case "korea": Korea(mb, size, capital); break;
                case "steppe": Steppe(mb, size, capital); break;
                case "tarim": Tarim(mb, size, capital); break;
                case "seasia": SeAsia(mb, size, capital); break;
                case "village": Village(mb, size, culture); break;
                case "hillfort": Hillfort(mb, size, capital, culture); break;
                case "sarmatian": Sarmatian(mb, size, capital, city); break;
                case "india": India(mb, size, capital); break;
                case "africa": Africa(mb, size, capital, city); break;
                case "moor": Moor(mb, size, capital); break;
                default: Han(mb, size, capital); break;
            }
        }
        // 第二版的汉地原模型（Models.CityInto 的同一套几何；保留给仍要旧外观的调用方）
        public static void HanCityInto(MeshBuilder mb, float size, bool capital) { Models.CityInto(mb, size, Color.white, capital); }

        // 远景简化模型（约 300–600 个三角形）：轮廓与配色与近景模型一致，供镜头较远时替换
        sealed class FarStyle { public bool walled; public string wall, roof, hall, roofKind; }
        static FarStyle Wd(string wall, string roof, string hall, string kind) { return new FarStyle { walled = true, wall = wall, roof = roof, hall = hall, roofKind = kind }; }
        static FarStyle Vg(string wood, string thatch) { return new FarStyle { walled = false, wall = wood, roof = thatch }; }
        static readonly Dictionary<string, FarStyle> Far = new Dictionary<string, FarStyle>
        {
            { "han", Wd("#bdb4a0", "#465163", "#9a3226", "hip") }, { "korea", Wd("#a6a296", "#4e5560", "#e6dccb", "hip") },
            { "roman", Wd("#cdbf9f", "#b4553a", "#ece2c8", "gable") }, { "sarmatian", Wd("#dcd6c6", "#b4553a", "#ece4d0", "gable") },
            { "persia", Wd("#c8a679", "#d8bc8c", "#d8bc8c", "flat") }, { "arab", Wd("#d8c49a", "#e2d2ac", "#d8c49a", "flat") },
            { "kushan", Wd("#b98f64", "#e2d4b4", "#e2d4b4", "flat") }, { "tarim", Wd("#cfb487", "#dcc79c", "#cfb487", "flat") },
            { "wa", Vg("#8a6a46", "#a88a52") }, { "yi", Vg("#6a5034", "#a08a50") }, { "nanman", Vg("#6a5034", "#8a7448") },
            { "seasia", Vg("#7a5a3a", "#9a8048") }, { "celt", Vg("#7a5a3a", "#a08a50") }, { "german", Vg("#7a5a3a", "#8a7444") },
            { "steppe", Vg("#7a5a3a", "#e8e0cc") },
            { "india", Wd("#b8664a", "#a8884e", "#e8dcc0", "flat") }, { "africa", Wd("#b07a50", "#d8bc8a", "#d8ccb4", "flat") }, { "moor", Wd("#b8a07e", "#a08868", "#c8b090", "flat") },
        };
        public static void CityFarInto(MeshBuilder mb, float s, string culture, bool capital)
        {
            FarStyle P;
            if (culture == null || !Far.TryGetValue(culture, out P)) if (!Far.TryGetValue(WorldGeo.CultureBase(culture), out P)) P = Far["han"];
            float r = s;
            Base(mb, r, H(P.walled ? "#a0977f" : "#7f8a52"));
            if (P.walled)
            {
                Color wall = H(P.wall), roof = H(P.roof), hall = H(P.hall); float h = 0.44f * s, t = 0.2f * s;
                foreach (var q in new[] { new[] { 0, -r, r * 2, t }, new[] { 0, r, r * 2, t }, new[] { -r, 0, t, r * 2 }, new[] { r, 0, t, r * 2 } }) mb.Box(V(q[0], h / 2, q[1]), V(q[2], h, q[3]), wall, 0.08f);
                for (int cx = -1; cx <= 1; cx += 2) for (int cz = -1; cz <= 1; cz += 2) if (!(cx > 0 && cz > 0)) mb.Box(V(cx * r, h * 0.62f, cz * r), V(t * 1.7f, h * 1.24f, t * 1.7f), Sh(wall, -0.06f));
                mb.Box(V(0, h * 0.62f, -r), V(0.5f * s, h * 1.24f, t * 1.4f), Sh(wall, -0.06f));
                Action<float, float, float, float, float, float, Color> roofed = (x, z, w, d, y, hh, rc) =>
                {
                    if (P.roofKind == "flat") { mb.Box(V(x, y + 0.012f, z), V(w * 1.04f, 0.025f, d * 1.04f), Sh(rc, 0.08f)); return; }
                    Vector3 a = V(x - w * 0.56f, y, z - d * 0.56f), b = V(x + w * 0.56f, y, z - d * 0.56f), c = V(x + w * 0.56f, y, z + d * 0.56f), dd = V(x - w * 0.56f, y, z + d * 0.56f);
                    float rr = P.roofKind == "gable" ? 0.56f : 0.25f;
                    Vector3 r0 = V(x - w * rr, y + hh, z), r1 = V(x + w * rr, y + hh, z);
                    Face(mb, new[] { a, b, r1, r0 }, rc, V(0, 0.5f, -1)); Face(mb, new[] { c, dd, r0, r1 }, Sh(rc, -0.12f), V(0, 0.5f, 1));
                    if (P.roofKind == "gable") { Face(mb, new[] { a, r0, dd }, Sh(rc, -0.2f), V(-1, 0, 0)); Face(mb, new[] { b, c, r1 }, Sh(rc, -0.2f), V(1, 0, 0)); }
                    else { Face(mb, new[] { b, c, r1 }, Sh(rc, -0.06f), V(1, 0.5f, 0)); Face(mb, new[] { dd, a, r0 }, Sh(rc, -0.18f), V(-1, 0.5f, 0)); }
                };
                mb.Box(V(0, h + 0.1f * s, -r), V(0.36f * s, 0.2f * s, 0.2f * s), culture == "han" ? hall : Sh(wall, 0.05f));
                roofed(0, -r, 0.42f * s, 0.26f * s, h + 0.2f * s, 0.14f * s, roof);
                float hk = capital ? 1.15f : 1; var gold = culture == "han" && capital ? H("#c9962e") : roof;
                mb.Box(V(0, 0.2f * s, 0.2f * s), V(0.56f * s * hk, 0.4f * s, 0.38f * s * hk), hall);
                roofed(0, 0.2f * s, 0.6f * s * hk, 0.42f * s * hk, 0.4f * s, 0.22f * s, gold);
                foreach (var q in new[] { new[] { -0.55f, -0.45f }, new[] { 0.55f, -0.45f }, new[] { -0.58f, 0.3f }, new[] { 0.5f, 0.0f } })
                {
                    mb.Box(V(q[0] * s, 0.1f * s + 0.05f, q[1] * s), V(0.28f * s, 0.2f * s, 0.22f * s), H(culture == "han" || culture == "korea" ? "#e9e1cf" : P.hall));
                    roofed(q[0] * s, q[1] * s, 0.3f * s, 0.24f * s, 0.2f * s + 0.05f, 0.1f * s, Sh(roof, 0.06f));
                }
            }
            else
            {
                Color wood = H(P.wall), thatch = H(P.roof); float R = r * 0.86f;
                for (int i = 0; i < 10; i++)
                {
                    float a0 = i / 10f * PI * 2, a1 = (i + 1) / 10f * PI * 2;
                    if (i == 7) continue;
                    Seg(mb, Mathf.Cos(a0) * R, Mathf.Sin(a0) * R, Mathf.Cos(a1) * R, Mathf.Sin(a1) * R, 0.08f, 0.16f * s, 0.05f, wood);
                }
                foreach (var q in new[] { new[] { -0.4f, -0.35f, 1 }, new[] { 0.35f, -0.4f, 0.9f }, new[] { -0.45f, 0.25f, 0.95f }, new[] { 0.1f, 0.0f, capital ? 1.4f : 1.15f }, new[] { 0.4f, 0.3f, 0.85f } })
                {
                    float x = q[0], z = q[1], k = q[2];
                    if (culture == "steppe") { Cyl(mb, V(x * s, 0.08f, z * s), 0.17f * k * s, 0.17f * k * s, 0.12f * k * s, 6, thatch, false); Cone(mb, V(x * s, 0.08f + 0.12f * k * s, z * s), 0.18f * k * s, 0.1f * k * s, 6, Sh(thatch, -0.06f)); }
                    else { mb.Box(V(x * s, 0.08f + 0.06f * k * s, z * s), V(0.24f * k * s, 0.12f * k * s, 0.2f * k * s), Sh(wood, 0.25f)); Cone(mb, V(x * s, 0.08f + 0.12f * k * s, z * s), 0.2f * k * s, 0.2f * k * s, 4, thatch); }
                }
            }
        }

        // 单独一座城的网格（测试 / 预览用）
        public static Mesh City(float size, string culture, bool capital, City city = null)
        {
            var mb = new MeshBuilder();
            CityInto(mb, size, culture, capital, city);
            return mb.ToMesh("city_" + (culture ?? "han"));
        }
    }
}
