// 三国志II 霸王的大陆 · 各文化的城池模型（第二版 §4G、§6）← 网页版 js/culture-art.js
//
// 战略地图上的城池按所属文化换成不同的低多边形模型；汉地城池（han）仍用原模型（Models.City 的同一套几何，写入 MeshBuilder）。
// 约定与原模型相同：Unity 坐标（x 东、z 北、y 上），size ≈ 占地半径 r，底座 2.3r × 2.3r，城墙在 ±r，城门朝南（−z）；
// 旗帜由 MapView 另画在 (0.9r, 0, 0.9r) 一角（东北），该角保持空旷；标签高度 size × 1.6 + 0.6 —— 模型最高处不超过约 1.5r。
// capital = true 时为都城变体。全部写入调用方的 MeshBuilder（按区块合并网格），只用顶点色，不增加材质或贴图。
//
// 文化 → 模型
//   roman 罗马：石墙圆塔、红瓦民居、列柱神庙；都城加竞技场
//   persia 安息 / 波斯：土坯城墙、扶壁塔、平顶房、阶梯内城与伊万宫殿；都城加拜火祠
//   arab 哈特拉 / 阿拉伯：圆形石城、圆塔、神庙院、平顶房、椰枣树（阿克苏姆另立石碑）
//   kushan 贵霜：砖城、窣堵波、僧院；都城为大塔与宫殿
//   wa 倭：环壕、木栅、望楼、高床仓库、竖穴住居；都城加大型高床殿
//   korea 高句丽 / 三韩：依山石城、城门楼、山上殿阁
//   steppe 草原：毡帐营地、车阵、马尾纛；都城为金顶大帐
//   tarim 西域绿洲：夯土城墙、水池、钻天杨、平顶房、佛塔或烽燧
//   seasia 南海（林邑、扶南）：水渠、木栅、高脚屋、神殿、椰树
//   yi、nanman 夷洲 / 南中：竹木栅栏围成的高脚屋村落
//   celt / german 山丘堡垒（土垒 + 木栅）：圆形茅屋 / 长屋
//   sarmatian 萨尔马提亚：篷车与尖顶帐；都城或大城（潘提卡彭等希腊城）为卫城与神庙
//
// 公开接口
//   CultureArt.Cultures                                  已有模型的文化代号
//   CultureArt.CultureOfCity(city)                       city.culture → "han"
//   CultureArt.IsCapital(city, culture)                  汉地：洛阳、长安；其他文化：世界剧本各势力的初始都城（WorldScenarioData.FactionInfo）
//   CultureArt.CityInto(mb, size, culture, capital, city) 写入 MeshBuilder；han / 未知文化用原模型
//   CultureArt.HanCityInto(mb, size, capital)            原模型（与 Models.City 相同的几何）
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
            float dx = bx - ax, dz = bz - az, L = Mathf.Sqrt(dx * dx + dz * dz); if (L == 0) L = 1;
            float nx = -dz / L * t / 2, nz = dx / L * t / 2;
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
        // 低多边形穹顶（半球，由几段圆台叠成）
        static readonly float[,] DomeRings = { { 1, 0 }, { 0.92f, 0.38f }, { 0.7f, 0.72f }, { 0.38f, 0.93f } };
        static void Dome(MeshBuilder mb, float x, float y, float z, float r, Color c, int seg = 8, float squash = 1)
        {
            for (int i = 0; i < 3; i++)
            {
                float r0 = DomeRings[i, 0], y0 = DomeRings[i, 1], r1 = DomeRings[i + 1, 0], y1 = DomeRings[i + 1, 1];
                mb.Cylinder(V(x, y + y0 * r * squash, z), r0 * r, r1 * r, (y1 - y0) * r * squash, seg, Sh(c, i * 0.04f), false);
            }
            mb.Cone(V(x, y + 0.93f * r * squash, z), 0.38f * r, 0.07f * r * squash + 0.02f, seg, Sh(c, 0.12f));
        }
        // 圆柱
        static void Column(MeshBuilder mb, float x, float y, float z, float h, float r, Color c)
        {
            mb.Cylinder(V(x, y, z), r, r * 0.88f, h, 6, c, true);
            mb.Box(V(x, y + h + r * 0.4f, z), V(r * 2.6f, r * 0.8f, r * 2.6f), Sh(c, 0.05f));
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
                float dx = b.x - a.x, dz = b.y - a.y, L = Mathf.Sqrt(dx * dx + dz * dz);
                int k = Math.Max(2, WorldGeo.JsRound(n * L));
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
        // 椰树 / 椰枣树
        static void Palm(MeshBuilder mb, float x, float z, float s, int seed, float y0 = 0)
        {
            var rnd = new System.Random(seed);
            float h = s * (0.75f + (float)rnd.NextDouble() * 0.3f);
            mb.Cylinder(V(x, y0, z), 0.035f * s, 0.025f * s, h, 4, H("#8a6a44"), false);
            var top = V(x, y0 + h, z); var leaf = H("#3f7a34");
            for (int i = 0; i < 6; i++)
            {
                float a = i * PI / 3 + (float)rnd.NextDouble() * 0.4f;
                float ca = Mathf.Cos(a), sa = Mathf.Sin(a), L = 0.32f * s;
                var tip = V(x + ca * L, y0 + h - 0.12f * s, z + sa * L);
                var mid = V(x + ca * L * 0.5f - sa * 0.06f * s, y0 + h + 0.05f * s, z + sa * L * 0.5f + ca * 0.06f * s);
                var mid2 = V(x + ca * L * 0.5f + sa * 0.06f * s, y0 + h + 0.05f * s, z + sa * L * 0.5f - ca * 0.06f * s);
                Sheet(mb, new[] { top, mid, tip, mid2 }, Sh(leaf, (i % 2) * 0.08f));
            }
        }
        // 钻天杨
        static void Poplar(MeshBuilder mb, float x, float z, float s, float y0 = 0)
        {
            mb.Cylinder(V(x, y0, z), 0.03f * s, 0.025f * s, 0.2f * s, 4, H("#7a6448"), false);
            mb.Blob(V(x, y0 + 0.5f * s, z), V(0.1f * s, 0.38f * s, 0.1f * s), H("#5c8a3a"), (int)(x * 97 + z * 31));
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
            Gable(mb, x, y0 + h, z, w, d, rh > 0 ? rh : Mathf.Min(w, d) * 0.45f, roofC, alongZ, Mathf.Min(w, d) * 0.08f);
        }
        // 高脚屋：四柱 + 地板 + 墙 + 陡坡顶（saddle = 鞍形翘脊）
        static void StiltHouse(MeshBuilder mb, float x, float z, float w, float d, float legH, float h, Color wallC, Color roofC, bool saddle, float y0 = 0)
        {
            var post = H("#5a4630");
            foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 }) mb.Box(V(x + sx * w * 0.4f, y0 + legH / 2, z + sz * d * 0.4f), V(0.035f, legH, 0.035f), post);
            mb.Box(V(x, y0 + legH + 0.015f, z), V(w * 1.05f, 0.03f, d * 1.05f), Sh(post, 0.15f));
            mb.Box(V(x, y0 + legH + 0.03f + h / 2, z), V(w * 0.9f, h, d * 0.9f), wallC, 0.1f);
            float ry = y0 + legH + 0.03f + h;
            Gable(mb, x, ry, z, w, d, Mathf.Max(w, d) * 0.6f, roofC, d > w, 0.05f);
            if (saddle)
            {
                // 鞍形屋脊：两端上翘的尖角
                bool along = d > w;
                float L = (along ? d : w) / 2 + 0.06f, rh = Mathf.Max(w, d) * 0.6f;
                foreach (var sgn in new[] { -1, 1 })
                {
                    var bs = along ? V(x, ry + rh, z + sgn * L * 0.8f) : V(x + sgn * L * 0.8f, ry + rh, z);
                    var tip = along ? V(x, ry + rh + 0.14f, z + sgn * (L + 0.1f)) : V(x + sgn * (L + 0.1f), ry + rh + 0.14f, z);
                    var side = along ? V(0.04f, 0, 0) : V(0, 0, 0.04f);
                    Sheet(mb, new[] { bs - side, tip, bs + side }, Sh(roofC, -0.1f));
                }
            }
        }
        // 毡帐（蒙古包）
        static void Yurt(MeshBuilder mb, float x, float z, float r, Color felt, float y0 = 0, Color? roofC = null)
        {
            mb.Cylinder(V(x, y0, z), r, r, r * 0.7f, 8, felt, false);
            mb.Cylinder(V(x, y0 + r * 0.7f, z), r * 1.03f, r * 0.18f, r * 0.42f, 8, roofC ?? Sh(felt, -0.08f), true);
            mb.Box(V(x, y0 + r * 0.25f, z - r * 0.98f), V(r * 0.35f, r * 0.5f, 0.02f), H("#a0442a"));   // 门（朝南）
        }
        // 尖顶帐
        static void Tent(MeshBuilder mb, float x, float z, float r, Color c, float y0 = 0) { mb.Cone(V(x, y0, z), r, r * 1.5f, 6, c); }
        // 马尾纛 / 竿旗：竿 + 顶饰 + 垂缨（tail）/ 罗马军旗（vexillum）/ 小旗（flag）
        static void Standard(MeshBuilder mb, float x, float z, float h, Color c, string kind, float y0 = 0)
        {
            mb.Box(V(x, y0 + h / 2, z), V(0.03f, h, 0.03f), H("#5a4026"));
            if (kind == "tail")
            {
                mb.Cone(V(x, y0 + h - 0.22f, z), 0.07f, 0.24f, 5, c);
                mb.Cone(V(x, y0 + h, z), 0.025f, 0.08f, 4, H("#d8b050"));
            }
            else if (kind == "vexillum")
            {
                mb.Box(V(x, y0 + h - 0.05f, z), V(0.22f, 0.02f, 0.02f), H("#5a4026"));
                Sheet(mb, new[] { V(x - 0.11f, y0 + h - 0.06f, z), V(x + 0.11f, y0 + h - 0.06f, z), V(x + 0.11f, y0 + h - 0.3f, z), V(x - 0.11f, y0 + h - 0.3f, z) }, c);
                mb.Cone(V(x, y0 + h, z), 0.04f, 0.1f, 4, H("#d8b050"));
            }
            else Sheet(mb, new[] { V(x + 0.015f, y0 + h, z), V(x + 0.26f, y0 + h - 0.04f, z), V(x + 0.015f, y0 + h - 0.18f, z) }, c);
        }
        // 底座
        static void Base(MeshBuilder mb, float r, Color c) { mb.Box(V(0, 0.05f, 0), V(r * 2.3f, 0.1f, r * 2.3f), c); }
        // 城门：门洞（深色）+ 两侧门楼
        static void Gateway(MeshBuilder mb, float r, float w, float h, Color wallC, Color towerC, bool round)
        {
            mb.Box(V(0, h * 0.4f, -r - 0.012f), V(w, h * 0.8f, 0.06f), H("#2a1c12"));
            foreach (var sx in new[] { -1, 1 })
            {
                if (round) mb.Cylinder(V(sx * w * 0.95f, 0, -r), w * 0.42f, w * 0.4f, h * 1.3f, 8, towerC, true);
                else mb.Box(V(sx * w * 0.95f, h * 0.65f, -r), V(w * 0.75f, h * 1.3f, w * 0.75f), towerC);
            }
        }
        static Vector2 P2(float x, float z) { return new Vector2(x, z); }

        // ============================================================ 各文化 --
        // ---------------------------------------------------------- 罗马 --
        static void Roman(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.42f * s, t = 0.16f * s;
            Color stone = H("#cdbf9f"), tile = H("#b4553a"), stucco = H("#ece2c8"), marble = H("#f2eee4");
            Base(mb, r, H("#a89a7a"));
            SquareWall(mb, r, wallH, t, stone, 0.08f * s, 3);
            // 圆角塔（东北角留给旗帜：缩小）
            foreach (var cx in new[] { -1, 1 }) foreach (var cz in new[] { -1, 1 })
                {
                    bool big = !(cx > 0 && cz > 0);
                    mb.Cylinder(V(cx * r, 0, cz * r), t * (big ? 1.05f : 0.8f), t, wallH * (big ? 1.35f : 1.1f), 8, Sh(stone, -0.04f), true);
                    if (big) Hip(mb, cx * r, wallH * 1.35f, cz * r, t * 2.1f, t * 2.1f, t * 1.1f, tile);
                }
            Gateway(mb, r, 0.24f * s, wallH, stone, Sh(stone, -0.05f), true);
            // 红瓦民居
            float[,] houses = { { -0.55f, -0.45f, 0.34f, 0.26f }, { 0.5f, -0.5f, 0.3f, 0.26f }, { -0.6f, 0.15f, 0.28f, 0.34f }, { 0.62f, 0.05f, 0.26f, 0.3f }, { -0.2f, -0.62f, 0.26f, 0.2f }, { 0.2f, -0.25f, 0.22f, 0.2f } };
            for (int i = 0; i < houses.GetLength(0); i++)
                GableHouse(mb, houses[i, 0] * s, houses[i, 1] * s, houses[i, 2] * s, houses[i, 3] * s, 0.18f * s, i % 2 != 0 ? stucco : Sh(stucco, -0.06f), tile, houses[i, 3] > houses[i, 2]);
            if (!cap)
            {
                // 列柱神庙（北侧中央）：基台 + 四柱 + 山墙顶
                float tz = 0.45f * s;
                mb.Box(V(0, 0.06f * s, tz), V(0.62f * s, 0.12f * s, 0.7f * s), marble);
                mb.Box(V(0, 0.27f * s, tz + 0.08f * s), V(0.44f * s, 0.3f * s, 0.44f * s), stucco);
                for (int i = 0; i < 4; i++) Column(mb, (-0.24f + i * 0.16f) * s, 0.12f * s, tz - 0.27f * s, 0.32f * s, 0.03f * s, marble);
                Gable(mb, 0, 0.47f * s, tz, 0.6f * s, 0.68f * s, 0.17f * s, tile, true, 0.02f * s);
                mb.Box(V(0, 0.45f * s, tz - 0.33f * s), V(0.6f * s, 0.05f * s, 0.03f * s), marble);
            }
            else
            {
                // 都城：竞技场（椭圆环，两层拱）+ 神庙
                float ax = -0.05f * s, az = 0.35f * s, R = 0.5f * s, hh = 0.48f * s; const int n = 14;
                for (int i = 0; i < n; i++)
                {
                    float a0 = i * PI * 2 / n, a1 = (i + 1) * PI * 2 / n;
                    float x0 = ax + Mathf.Cos(a0) * R * 1.15f, z0 = az + Mathf.Sin(a0) * R * 0.85f, x1 = ax + Mathf.Cos(a1) * R * 1.15f, z1 = az + Mathf.Sin(a1) * R * 0.85f;
                    Seg(mb, x0, z0, x1, z1, 0, hh, 0.1f * s, i % 2 != 0 ? stucco : Sh(stucco, -0.04f));
                    // 拱：外立面上的深色窗洞（两层）
                    float mx = (x0 + x1) / 2, mz = (z0 + z1) / 2, nx = Mathf.Cos((a0 + a1) / 2), nz = Mathf.Sin((a0 + a1) / 2);
                    foreach (var yy in new[] { 0.14f, 0.32f })
                    {
                        float px = mx + nx * 0.052f * s, pz = mz + nz * 0.052f * s;
                        float tx = (x1 - x0) * 0.22f, tz2 = (z1 - z0) * 0.22f;
                        Face(mb, new[] { V(px - tx, yy * s, pz - tz2), V(px + tx, yy * s, pz + tz2), V(px + tx, (yy + 0.1f) * s, pz + tz2), V(px - tx, (yy + 0.1f) * s, pz - tz2) }, H("#4a3a2c"), V(nx, 0, nz));
                    }
                }
                Face(mb, new[] { V(ax - R, 0.11f, az - R * 0.75f), V(ax + R, 0.11f, az - R * 0.75f), V(ax + R, 0.11f, az + R * 0.75f), V(ax - R, 0.11f, az + R * 0.75f) }, H("#d8c08a"), Vector3.up);
                // 小神庙（东侧）
                mb.Box(V(0.62f * s, 0.05f * s, 0.55f * s), V(0.3f * s, 0.1f * s, 0.36f * s), marble);
                for (int i = 0; i < 3; i++) Column(mb, (0.52f + i * 0.1f) * s, 0.1f * s, 0.4f * s, 0.24f * s, 0.022f * s, marble);
                mb.Box(V(0.62f * s, 0.22f * s, 0.6f * s), V(0.26f * s, 0.24f * s, 0.22f * s), stucco);
                Gable(mb, 0.62f * s, 0.35f * s, 0.55f * s, 0.3f * s, 0.36f * s, 0.1f * s, tile, true, 0.01f * s);
            }
            Standard(mb, -0.25f * s, -r - 0.18f * s, 0.75f * s, H("#a8261e"), "vexillum");
            Standard(mb, 0.25f * s, -r - 0.18f * s, 0.75f * s, H("#a8261e"), "vexillum");
        }

        // ---------------------------------------------------- 安息 / 波斯 --
        static void Persia(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.42f * s, t = 0.2f * s;
            Color mud = H("#c8a679"), mud2 = H("#b8946a"), glaze = H("#2f6e8e");
            Base(mb, r, H("#b8a07a"));
            SquareWall(mb, r, wallH, t, mud, 0.09f * s, 3);
            // 扶壁塔（方形，沿墙等距）
            int[,] sides = { { 0, -1 }, { 0, 1 }, { -1, 0 }, { 1, 0 } };
            foreach (var k in new[] { -0.5f, 0.5f })
                for (int si = 0; si < 4; si++)
                {
                    float x = sides[si, 0] != 0 ? sides[si, 0] * r : k * r * 1.2f, z = sides[si, 1] != 0 ? sides[si, 1] * r : k * r * 1.2f;
                    if (x > 0 && z > 0.4f * r) continue;   // 东北角留给旗帜
                    mb.Box(V(x, wallH * 0.58f, z), V(t * 1.2f, wallH * 1.16f, t * 1.2f), mud2);
                }
            foreach (var cx in new[] { -1, 1 }) foreach (var cz in new[] { -1, 1 })
                    if (!(cx > 0 && cz > 0)) mb.Box(V(cx * r, wallH * 0.65f, cz * r), V(t * 1.6f, wallH * 1.3f, t * 1.6f), mud2);
            // 城门：高拱门
            mb.Box(V(0, wallH * 0.62f, -r), V(0.5f * s, wallH * 1.24f, t * 1.3f), mud2);
            mb.Box(V(0, wallH * 0.42f, -r - t * 0.66f), V(0.2f * s, wallH * 0.84f, 0.02f), H("#2a1c12"));
            mb.Box(V(0, wallH * 0.95f, -r - t * 0.66f), V(0.3f * s, 0.05f * s, 0.02f), glaze);
            // 平顶房
            float[,] hs = { { -0.6f, -0.5f, 0.3f, 0.26f, 0.2f }, { 0.55f, -0.55f, 0.26f, 0.24f, 0.18f }, { -0.62f, -0.05f, 0.26f, 0.32f, 0.22f }, { 0.62f, -0.1f, 0.24f, 0.3f, 0.2f }, { -0.3f, -0.58f, 0.2f, 0.2f, 0.16f }, { 0.28f, -0.6f, 0.2f, 0.18f, 0.15f } };
            for (int i = 0; i < hs.GetLength(0); i++) FlatHouse(mb, hs[i, 0] * s, hs[i, 1] * s, hs[i, 2] * s, hs[i, 3] * s, hs[i, 4] * s, i % 2 != 0 ? mud : Sh(mud, 0.08f));
            // 阶梯内城（北侧）：三层台
            float czz = 0.42f * s;
            float[,] lv = { { 0.9f, 0.16f }, { 0.68f, 0.16f }, { 0.46f, 0.14f } };
            float yb = 0;
            for (int i = 0; i < 3; i++)
            {
                mb.Box(V(-0.05f * s, yb + lv[i, 1] * s / 2, czz), V(lv[i, 0] * s, lv[i, 1] * s, lv[i, 0] * s * 0.7f), i % 2 != 0 ? mud2 : Sh(mud2, 0.06f));
                yb += lv[i, 1] * s;
            }
            // 伊万宫殿（立在台顶）：方体 + 巨大拱门（深色）+ 釉砖边框 + 筒拱顶
            float py = 0.46f * s, iw = cap ? 0.42f : 0.3f;
            mb.Box(V(-0.05f * s, py + 0.14f * s, czz), V(iw * s, 0.28f * s, 0.26f * s), H("#d8bc8c"));
            mb.Box(V(-0.05f * s, py + 0.12f * s, czz - 0.131f * s), V(iw * 0.5f * s, 0.22f * s, 0.01f), H("#3a2818"));
            mb.Box(V(-0.05f * s, py + 0.245f * s, czz - 0.132f * s), V(iw * 0.62f * s, 0.025f * s, 0.01f), glaze);
            mb.Cylinder(V(-0.05f * s, py + 0.28f * s, czz), iw * 0.36f * s, 0.02f * s, 0.1f * s, 6, H("#c8ac7c"), false);
            if (cap)
            {
                // 都城：拜火祠（四柱亭 + 穹顶）
                float fx = 0.55f * s, fz = 0.5f * s;
                mb.Box(V(fx, 0.04f * s, fz), V(0.3f * s, 0.08f * s, 0.3f * s), mud2);
                foreach (var ox in new[] { -1, 1 }) foreach (var oz in new[] { -1, 1 }) mb.Box(V(fx + ox * 0.1f * s, 0.17f * s, fz + oz * 0.1f * s), V(0.06f * s, 0.18f * s, 0.06f * s), H("#d8c098"));
                mb.Box(V(fx, 0.28f * s, fz), V(0.28f * s, 0.05f * s, 0.28f * s), H("#d8c098"));
                Dome(mb, fx, 0.3f * s, fz, 0.12f * s, H("#d8c098"), 8);
                mb.Cone(V(fx, 0.08f * s, fz), 0.03f * s, 0.07f * s, 5, H("#ff8a2a"));
            }
            Standard(mb, -0.6f * s, 0.6f * s, 0.7f * s, H("#7a2a5a"), "tail");
        }

        // ------------------------------------------------- 阿拉伯 / 哈特拉 --
        static void Arab(MeshBuilder mb, float s, bool cap, City city)
        {
            float r = s, R = 0.98f * r, wallH = 0.4f * s;
            Color stone = H("#d8c49a"), stone2 = H("#c4ae84");
            Base(mb, r, H("#c8b080"));
            RingWall(mb, R, 16, wallH, 0.15f * s, stone, PI / 16);
            // 圆塔（跳过东北）
            for (int i = 0; i < 8; i++)
            {
                float a = i * PI / 4 + PI / 8;
                float x = Mathf.Cos(a) * R, z = Mathf.Sin(a) * R;
                if (x > 0.3f * r && z > 0.3f * r) continue;
                mb.Cylinder(V(x, 0, z), 0.11f * s, 0.1f * s, wallH * 1.3f, 6, stone2, true);
            }
            // 南门
            mb.Box(V(0, wallH * 0.6f, -R), V(0.34f * s, wallH * 1.2f, 0.22f * s), stone2);
            mb.Box(V(0, wallH * 0.4f, -R - 0.112f * s), V(0.14f * s, wallH * 0.8f, 0.01f), H("#2a1c12"));
            // 神庙院（中央）：基台 + 伊万神殿 + 列柱
            float tz = 0.12f * s;
            mb.Box(V(0, 0.05f * s, tz), V(0.84f * s, 0.1f * s, 0.6f * s), Sh(stone, 0.06f));
            int n = cap ? 2 : 1;
            for (int k = 0; k < n; k++)
            {
                float x = (n == 1 ? 0 : (k != 0 ? 0.2f : -0.2f)) * s;
                mb.Box(V(x, 0.1f * s + 0.17f * s, tz + 0.1f * s), V(0.34f * s, 0.34f * s, 0.32f * s), stone);
                mb.Box(V(x, 0.1f * s + 0.14f * s, tz - 0.061f * s), V(0.15f * s, 0.26f * s, 0.01f), H("#3a2818"));
                mb.Box(V(x, 0.1f * s + 0.35f * s, tz + 0.1f * s), V(0.36f * s, 0.03f * s, 0.34f * s), Sh(stone, 0.12f));
            }
            for (int i = 0; i < 6; i++) Column(mb, (-0.36f + i * 0.144f) * s, 0.1f * s, tz - 0.25f * s, 0.2f * s, 0.022f * s, H("#efe3c4"));
            // 平顶房 + 椰枣树
            float[,] hs = { { -0.55f, -0.45f }, { 0.55f, -0.42f }, { -0.62f, 0.35f }, { -0.3f, -0.62f }, { 0.3f, -0.64f } };
            for (int i = 0; i < hs.GetLength(0); i++) FlatHouse(mb, hs[i, 0] * s, hs[i, 1] * s, 0.22f * s, 0.2f * s, (0.14f + (i % 3) * 0.03f) * s, i % 2 != 0 ? stone : H("#e2d2ac"));
            Palm(mb, 0.55f * s, 0.1f * s, s, 11); Palm(mb, -0.15f * s, 0.6f * s, s * 0.9f, 12); Palm(mb, -0.72f * s, -0.12f * s, s * 0.85f, 13);
            // 阿克苏姆：石碑
            if (city != null && city.key == "aksum")
            {
                float[,] st = { { 0.42f, 0.95f }, { 0.6f, 0.7f }, { 0.28f, 0.6f } };
                for (int i = 0; i < 3; i++)
                {
                    float x = st[i, 0], h = st[i, 1];
                    mb.Box(V(x * s, h * s / 2, 0.55f * s), V(0.07f * s, h * s, 0.04f * s), H("#9a8f80"));
                    mb.Cone(V(x * s, h * s, 0.55f * s), 0.045f * s, 0.05f * s, 4, H("#9a8f80"));
                }
            }
            Standard(mb, 0, 0.62f * s, 0.75f * s, H("#d8b050"), "tail");
        }

        // ------------------------------------------------------------ 贵霜 --
        static void Stupa(MeshBuilder mb, float x, float z, float k, float y0 = 0)
        {
            Color white = H("#ece6d6"), gold = H("#d8b050");
            mb.Box(V(x, y0 + 0.06f * k, z), V(0.5f * k, 0.12f * k, 0.5f * k), H("#cfc4a8"));
            mb.Cylinder(V(x, y0 + 0.12f * k, z), 0.2f * k, 0.2f * k, 0.12f * k, 10, white, false);
            Dome(mb, x, y0 + 0.24f * k, z, 0.2f * k, white, 10);
            mb.Box(V(x, y0 + 0.47f * k, z), V(0.08f * k, 0.06f * k, 0.08f * k), H("#b88a4a"));
            mb.Box(V(x, y0 + 0.58f * k, z), V(0.012f * k, 0.2f * k, 0.012f * k), gold);
            for (int i = 0; i < 3; i++) mb.Cylinder(V(x, y0 + (0.52f + i * 0.06f) * k, z), (0.07f - i * 0.017f) * k, (0.07f - i * 0.017f) * k, 0.012f * k, 6, gold, true);
        }
        static void Kushan(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.4f * s, t = 0.18f * s;
            Color brick = H("#b98f64"), brick2 = H("#a87e56"), plaster = H("#e2d4b4");
            Base(mb, r, H("#a8966e"));
            SquareWall(mb, r, wallH, t, brick, 0.07f * s, 3);
            foreach (var cx in new[] { -1, 1 }) foreach (var cz in new[] { -1, 1 })
                    if (!(cx > 0 && cz > 0)) mb.Cylinder(V(cx * r, 0, cz * r), t * 1.2f, t * 1.1f, wallH * 1.25f, 8, brick2, true);
            Gateway(mb, r, 0.22f * s, wallH, brick, brick2, false);
            // 窣堵波（北侧）
            Stupa(mb, -0.15f * s, 0.38f * s, cap ? 1.35f * s : 1.0f * s);
            // 僧院 / 民居
            float[,] hs = { { -0.58f, -0.45f, 0.3f, 0.24f }, { 0.55f, -0.45f, 0.28f, 0.24f }, { 0.6f, 0.0f, 0.24f, 0.3f }, { -0.65f, 0.05f, 0.22f, 0.3f }, { 0.15f, -0.6f, 0.24f, 0.2f } };
            for (int i = 0; i < hs.GetLength(0); i++) FlatHouse(mb, hs[i, 0] * s, hs[i, 1] * s, hs[i, 2] * s, hs[i, 3] * s, 0.17f * s, i % 2 != 0 ? plaster : brick);
            if (cap)
            {
                // 宫殿：高台 + 列柱厅
                mb.Box(V(0.5f * s, 0.08f * s, 0.45f * s), V(0.44f * s, 0.16f * s, 0.36f * s), brick2);
                mb.Box(V(0.5f * s, 0.3f * s, 0.5f * s), V(0.36f * s, 0.28f * s, 0.24f * s), plaster);
                for (int i = 0; i < 4; i++) Column(mb, (0.36f + i * 0.093f) * s, 0.16f * s, 0.33f * s, 0.26f * s, 0.018f * s, H("#f0e8d8"));
                mb.Box(V(0.5f * s, 0.45f * s, 0.45f * s), V(0.42f * s, 0.04f * s, 0.34f * s), H("#8a5a34"));
            }
            else { Poplar(mb, 0.45f * s, 0.4f * s, s * 0.9f); Poplar(mb, 0.62f * s, 0.32f * s, s * 0.8f); }
            Standard(mb, 0.3f * s, -0.25f * s, 0.7f * s, H("#d8b050"), "flag");
        }

        // ------------------------------------------------------------- 倭 --
        static void Wa(MeshBuilder mb, float s, bool cap)
        {
            float r = s;
            Color wood = H("#8a6a46"), thatch = H("#a88a52"), thatch2 = H("#94784a");
            Base(mb, r, H("#7d8a52"));
            Moat(mb, r * 0.92f, r * 1.12f, 0.105f, H("#4a7a8a"));
            // 木栅（内侧，南面留门）：原折线去掉第 3 点（门的东柱）
            float R = r * 0.84f;
            Palisade(mb, new[] { P2(-R, -R), P2(-0.15f * s, -R), P2(R, -R), P2(R, R), P2(-R, R) }, 0.2f * s, wood, 6, 0.1f);
            // 门（南，两柱 + 横木）
            foreach (var sx in new[] { -1, 1 }) mb.Box(V(sx * 0.15f * s, 0.17f * s, -R), V(0.04f * s, 0.34f * s, 0.04f * s), Sh(wood, -0.1f));
            mb.Box(V(0, 0.33f * s, -R), V(0.4f * s, 0.035f * s, 0.04f * s), Sh(wood, -0.1f));
            // 望楼（西南、东南、西北）
            float[,] towers = { { -0.7f, -0.7f }, { 0.7f, -0.7f }, { -0.7f, 0.62f } };
            for (int i = 0; i < 3; i++)
            {
                float x = towers[i, 0], z = towers[i, 1];
                foreach (var ox in new[] { -1, 1 }) foreach (var oz in new[] { -1, 1 }) mb.Box(V((x + ox * 0.06f) * s, 0.3f * s, (z + oz * 0.06f) * s), V(0.025f * s, 0.6f * s, 0.025f * s), wood);
                mb.Box(V(x * s, 0.58f * s, z * s), V(0.18f * s, 0.03f * s, 0.18f * s), Sh(wood, 0.1f));
                Hip(mb, x * s, 0.66f * s, z * s, 0.22f * s, 0.22f * s, 0.14f * s, thatch);
                mb.Box(V(x * s, 0.62f * s, z * s), V(0.16f * s, 0.07f * s, 0.16f * s), Sh(wood, -0.15f));
            }
            // 高床仓库
            StiltHouse(mb, 0.45f * s, 0.45f * s, 0.2f * s, 0.16f * s, 0.14f * s, 0.1f * s, H("#a07c52"), thatch2, false);
            StiltHouse(mb, 0.2f * s, 0.55f * s, 0.2f * s, 0.16f * s, 0.14f * s, 0.1f * s, H("#a07c52"), thatch2, false);
            // 竖穴住居（圆锥茅顶）
            float[,] pits = { { -0.45f, -0.35f, 1 }, { 0.0f, -0.45f, 0.9f }, { 0.4f, -0.35f, 1 }, { -0.5f, 0.15f, 0.95f }, { -0.1f, 0.05f, 0.85f } };
            for (int i = 0; i < pits.GetLength(0); i++)
            {
                float x = pits[i, 0], z = pits[i, 1], k = pits[i, 2];
                mb.Cylinder(V(x * s, 0.1f, z * s), 0.15f * k * s, 0.04f * k * s, 0.2f * k * s, 7, thatch, false);
                mb.Cone(V(x * s, 0.1f + 0.2f * k * s, z * s), 0.05f * k * s, 0.05f * k * s, 5, thatch2);
            }
            if (cap)
            {
                // 大型高床殿（邪马台宫室）
                StiltHouse(mb, 0.05f * s, 0.45f * s, 0.42f * s, 0.3f * s, 0.22f * s, 0.16f * s, H("#b08a5a"), thatch2, false);
                foreach (var sx in new[] { -1, 1 }) mb.Box(V((0.05f + sx * 0.24f) * s, 0.86f * s, 0.45f * s), V(0.02f * s, 0.16f * s, 0.02f * s), wood);   // 千木
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
            mb.Blob(V(0.05f * s, 0.05f, 0.25f * s), V(0.85f * s, 0.38f * s, 0.7f * s), H("#6f7f4e"), 7);
            // 依山石墙：不规则多边形，北高南低
            float[,] pts = { { -0.98f, -0.95f }, { 0.0f, -1.0f }, { 0.98f, -0.9f }, { 1.0f, 0.1f }, { 0.75f, 0.95f }, { -0.2f, 1.0f }, { -0.95f, 0.7f }, { -1.0f, -0.1f } };
            int np = pts.GetLength(0);
            for (int i = 0; i < np; i++)
            {
                float ax = pts[i, 0], ay = pts[i, 1], bx = pts[(i + 1) % np, 0], by = pts[(i + 1) % np, 1];
                if (i == 0)
                {   // 南墙中间是城门
                    Seg(mb, ax * r, ay * r, -0.16f * s, -r, 0, 0.3f * s, 0.13f * s, rock, 0.3f * s);
                    Seg(mb, 0.16f * s, -r, 0.98f * r, -0.9f * r, 0, 0.3f * s, 0.13f * s, rock, 0.32f * s);
                    continue;
                }
                if (i == 1) continue;
                float ha = (0.3f + Mathf.Max(0, ay) * 0.25f) * s, hb = (0.3f + Mathf.Max(0, by) * 0.25f) * s;
                Seg(mb, ax * r, ay * r, bx * r, by * r, 0, ha, 0.13f * s, i % 2 != 0 ? rock : rock2, hb);
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
            float[,] hs = { { -0.55f, -0.55f, 0 }, { 0.5f, -0.6f, 1 }, { -0.62f, -0.1f, 1 }, { 0.6f, -0.15f, 0 } };
            for (int i = 0; i < hs.GetLength(0); i++)
            {
                float x = hs[i, 0], z = hs[i, 1];
                mb.Box(V(x * s, 0.1f * s, z * s), V(0.24f * s, 0.2f * s, 0.18f * s), wall);
                if (hs[i, 2] != 0) mb.ChineseRoof(V(x * s, 0.2f * s, z * s), 0.34f * s, 0.26f * s, 0.13f * s, roof);
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
            Base(mb, r, H("#8a9a5a"));
            // 车阵 / 木篱（低矮的环）
            RingWall(mb, r * 0.95f, 12, 0.1f * s, 0.04f * s, wood, PI / 12);
            float[,] ys = { { -0.45f, -0.4f, 0.2f }, { 0.42f, -0.45f, 0.19f }, { -0.58f, 0.18f, 0.19f }, { 0.12f, -0.1f, 0.18f }, { -0.12f, -0.62f, 0.15f }, { 0.58f, 0.02f, 0.16f } };
            for (int i = 0; i < ys.GetLength(0); i++) Yurt(mb, ys[i, 0] * s, ys[i, 1] * s, ys[i, 2] * s, i % 2 != 0 ? felt : felt2);
            // 首领大帐
            float big = cap ? 0.34f : 0.26f;
            Yurt(mb, -0.1f * s, 0.42f * s, big * s, H("#f2ecdc"), 0, cap ? H("#d8b050") : H("#c8bc9c"));
            if (cap) mb.Cone(V(-0.1f * s, big * s * 1.12f, 0.42f * s), 0.05f * s, 0.14f * s, 5, H("#d8b050"));
            // 马尾纛
            float[,] st = { { -0.35f, -0.85f }, { 0.35f, -0.85f }, { -0.45f, 0.62f } };
            for (int i = 0; i < 3; i++) Standard(mb, st[i, 0] * s, st[i, 1] * s, 0.8f * s, H("#2a2420"), "tail");
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
            float[,] W = { { -r, -r, -0.15f * s, -r }, { 0.15f * s, -r, r, -r }, { r, -r, r, r }, { r, r, -r, r }, { -r, r, -r, -r } };
            for (int i = 0; i < 5; i++)
                Seg(mb, W[i, 0], W[i, 1], W[i, 2], W[i, 3], 0, wallH * (1 - (i % 2) * 0.12f), t, i % 2 != 0 ? earth2 : earth, wallH * (0.88f + (i % 3) * 0.06f));
            foreach (var cx in new[] { -1, 1 }) foreach (var cz in new[] { -1, 1 })
                    if (!(cx > 0 && cz > 0)) mb.Box(V(cx * r, wallH * 0.6f, cz * r), V(t * 1.4f, wallH * 1.2f, t * 1.4f), earth2);
            // 城门（两侧门墩）
            foreach (var sx in new[] { -1, 1 }) mb.Box(V(sx * 0.2f * s, wallH * 0.6f, -r), V(0.12f * s, wallH * 1.2f, t * 1.3f), earth2);
            // 绿洲：水池 + 钻天杨
            Face(mb, new[] { V(-0.55f * s, 0.105f, 0.2f * s), V(-0.25f * s, 0.105f, 0.2f * s), V(-0.25f * s, 0.105f, 0.5f * s), V(-0.55f * s, 0.105f, 0.5f * s) }, H("#4f8a8a"), Vector3.up);
            float[,] pp = { { -0.68f, 0.15f }, { -0.68f, 0.4f }, { -0.68f, 0.65f }, { -0.1f, 0.62f }, { 0.12f, 0.66f }, { 0.5f, -0.3f } };
            for (int i = 0; i < pp.GetLength(0); i++) Poplar(mb, pp[i, 0] * s, pp[i, 1] * s, s * 0.85f);
            // 平顶房
            float[,] hs = { { -0.5f, -0.5f, 0.28f }, { 0.45f, -0.55f, 0.26f }, { 0.55f, -0.05f, 0.24f }, { -0.15f, -0.3f, 0.22f }, { 0.2f, -0.3f, 0.2f } };
            for (int i = 0; i < hs.GetLength(0); i++) FlatHouse(mb, hs[i, 0] * s, hs[i, 1] * s, hs[i, 2] * s, hs[i, 2] * 0.85f * s, (0.13f + (i % 2) * 0.04f) * s, i % 2 != 0 ? earth : H("#dcc79c"));
            // 小佛塔或烽燧
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
            Palisade(mb, new[] { P2(-R, -R), P2(-0.14f * s, -R) }, 0.16f * s, wood, 5);
            Palisade(mb, new[] { P2(0.14f * s, -R), P2(R, -R), P2(R, R * 0.4f) }, 0.16f * s, wood, 5);
            Palisade(mb, new[] { P2(R * 0.4f, R), P2(-R, R), P2(-R, -R) }, 0.16f * s, wood, 5);
            // 高脚屋（鞍形屋顶）
            float[,] hs = { { -0.5f, -0.45f, 0.24f, 0.2f }, { 0.45f, -0.5f, 0.22f, 0.2f }, { -0.55f, 0.1f, 0.2f, 0.26f }, { 0.55f, -0.05f, 0.2f, 0.24f }, { 0.0f, -0.55f, 0.2f, 0.18f } };
            for (int i = 0; i < hs.GetLength(0); i++) StiltHouse(mb, hs[i, 0] * s, hs[i, 1] * s, hs[i, 2] * s, hs[i, 3] * s, 0.14f * s, 0.1f * s, bamboo, i % 2 != 0 ? thatch : Sh(thatch, -0.08f), true);
            // 神殿：砖台 + 高耸的多重顶
            float tx = -0.05f * s, tz = 0.35f * s, k = cap ? 1.25f : 1;
            mb.Box(V(tx, 0.08f * s * k, tz), V(0.5f * s * k, 0.16f * s * k, 0.5f * s * k), brick);
            mb.Box(V(tx, 0.22f * s * k, tz), V(0.34f * s * k, 0.12f * s * k, 0.34f * s * k), Sh(brick, 0.08f));
            mb.Box(V(tx, 0.36f * s * k, tz), V(0.22f * s * k, 0.16f * s * k, 0.22f * s * k), H("#c8a050"));
            Hip(mb, tx, 0.44f * s * k, tz, 0.34f * s * k, 0.34f * s * k, 0.12f * s * k, thatch);
            Hip(mb, tx, 0.53f * s * k, tz, 0.22f * s * k, 0.22f * s * k, 0.2f * s * k, Sh(thatch, -0.1f));
            mb.Cone(V(tx, 0.72f * s * k, tz), 0.02f * s, 0.14f * s, 4, H("#d8b050"));
            if (cap) StiltHouse(mb, 0.5f * s, 0.5f * s, 0.32f * s, 0.24f * s, 0.16f * s, 0.14f * s, H("#b8a060"), Sh(thatch, -0.12f), true);
            // 椰树
            float[,] pl = { { -0.75f, 0.6f, 1 }, { 0.7f, 0.3f, 0.9f }, { -0.25f, -0.2f, 0.8f }, { 0.3f, 0.6f, 0.95f }, { -0.72f, -0.25f, 0.85f } };
            for (int i = 0; i < pl.GetLength(0); i++) Palm(mb, pl[i, 0] * s, pl[i, 1] * s, s * pl[i, 2], (int)(pl[i, 0] * 100 + pl[i, 1] * 10));
            Standard(mb, 0, -r * 0.95f, 0.55f * s, H("#c8a030"), "flag");
        }

        // ------------------------------------------------ 夷洲 / 南中：高脚村落 --
        static void Village(MeshBuilder mb, float s, string culture)
        {
            float r = s;
            bool nanman = culture == "nanman";
            Color bamboo = H("#a8945a"), thatch = nanman ? H("#8a7448") : H("#a08a50"), wood = H("#6a5034");
            Base(mb, r, nanman ? H("#7a8452") : H("#6f8a4a"));
            float R = r * 0.9f;
            Palisade(mb, new[] { P2(-R, -R), P2(-0.14f * s, -R) }, 0.2f * s, wood, 6);
            Palisade(mb, new[] { P2(0.14f * s, -R), P2(R, -R), P2(R, R * 0.4f) }, 0.2f * s, wood, 6);
            Palisade(mb, new[] { P2(R * 0.4f, R), P2(-R, R), P2(-R, -R) }, 0.2f * s, wood, 6);
            float[,] hs = { { -0.5f, -0.45f, 0.26f, 0.2f }, { 0.45f, -0.5f, 0.24f, 0.2f }, { -0.55f, 0.15f, 0.22f, 0.28f }, { 0.5f, -0.05f, 0.22f, 0.26f }, { 0.0f, -0.4f, 0.2f, 0.18f }, { -0.1f, 0.45f, 0.36f, 0.26f } };
            for (int i = 0; i < hs.GetLength(0); i++)
                StiltHouse(mb, hs[i, 0] * s, hs[i, 1] * s, hs[i, 2] * s, hs[i, 3] * s, 0.12f * s, (i == 5 ? 0.14f : 0.1f) * s, bamboo, i % 2 != 0 ? thatch : Sh(thatch, -0.08f), nanman);
            if (nanman)
            {
                // 铜鼓祭台
                mb.Cylinder(V(0.4f * s, 0.1f, 0.45f * s), 0.1f * s, 0.12f * s, 0.12f * s, 8, H("#7a8a5a"), true);
                mb.Cylinder(V(0.4f * s, 0.1f + 0.12f * s, 0.45f * s), 0.13f * s, 0.13f * s, 0.03f * s, 8, H("#9aa070"), true);
            }
            else { Palm(mb, 0.45f * s, 0.5f * s, s * 0.9f, (int)(0.45f * 77)); Palm(mb, 0.65f * s, 0.25f * s, s * 0.9f, (int)(0.65f * 77)); }
            Tree(mb, -0.72f * s, 0.7f * s, 0.6f * s, H("#3f6a34"));
            Standard(mb, 0, -R, 0.5f * s, H("#b02a24"), "flag");
        }

        // --------------------------------------------- 欧洲蛮族：山丘堡垒 --
        static void Hillfort(MeshBuilder mb, float s, bool cap, string culture)
        {
            float r = s;
            Color earth = H("#7a8a4e"), earth2 = H("#6a7844"), wood = H("#7a5a3a"), thatch = H("#a08a50"), wall = H("#a08868");
            Base(mb, r, H("#6f804a"));
            // 土垒：外圈环墙 + 内圈土台
            RingWall(mb, r * 1.02f, 14, 0.12f * s, 0.16f * s, earth2, 0);
            mb.Cylinder(V(0, 0, 0), r * 0.86f, r * 0.8f, 0.2f * s, 14, earth, true);
            float y0 = 0.2f * s;
            // 木栅（土台边缘，南面留门）
            float R = r * 0.8f; const int n = 12;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++) { float a = -PI / 2 + PI / n + i * PI * 2 / n; pts[i] = P2(Mathf.Cos(a) * R, Mathf.Sin(a) * R); }
            for (int i = 0; i < n - 1; i++) Palisade(mb, new[] { pts[i], pts[i + 1] }, 0.16f * s, wood, 5, y0);
            // 门楼
            foreach (var sx in new[] { -1, 1 }) mb.Box(V(sx * 0.12f * s, y0 + 0.14f * s, -R), V(0.05f * s, 0.28f * s, 0.05f * s), wood);
            mb.Box(V(0, y0 + 0.28f * s, -R), V(0.32f * s, 0.05f * s, 0.08f * s), wood);
            if (culture == "celt")
            {
                // 圆形茅屋
                float[,] hu = { { -0.35f, -0.3f, 1 }, { 0.35f, -0.3f, 0.9f }, { -0.4f, 0.25f, 0.95f }, { 0.3f, 0.3f, 1 }, { 0.0f, 0.0f, cap ? 1.5f : 1.15f } };
                for (int i = 0; i < hu.GetLength(0); i++)
                {
                    float x = hu[i, 0], z = hu[i, 1], k = hu[i, 2];
                    mb.Cylinder(V(x * s, y0, z * s), 0.14f * k * s, 0.14f * k * s, 0.1f * k * s, 8, wall, false);
                    mb.Cone(V(x * s, y0 + 0.1f * k * s, z * s), 0.18f * k * s, 0.22f * k * s, 8, thatch);
                }
            }
            else
            {
                // 长屋（日耳曼）
                float[,] lh = { { -0.3f, -0.3f, 0.5f, 0.2f }, { 0.35f, -0.2f, 0.2f, 0.46f }, { -0.3f, 0.3f, 0.46f, 0.2f } };
                for (int i = 0; i < 3; i++) GableHouse(mb, lh[i, 0] * s, lh[i, 1] * s, lh[i, 2] * s, lh[i, 3] * s, 0.08f * s, wall, thatch, lh[i, 3] > lh[i, 2], y0, 0.2f * s);
                if (cap)
                {
                    GableHouse(mb, 0.2f * s, 0.35f * s, 0.56f * s, 0.26f * s, 0.14f * s, H("#8a6a48"), H("#8a7444"), false, y0, 0.3f * s);
                    foreach (var sx in new[] { -1, 1 }) mb.Box(V((0.2f + sx * 0.3f) * s, y0 + 0.48f * s, 0.35f * s), V(0.02f * s, 0.12f * s, 0.02f * s), wood);   // 山墙交叉兽头
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
                mb.Cylinder(V(x, 0.175f * s, z), 0.12f * s, 0.04f * s, 0.18f * s, 6, felt, false);
                foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 }) mb.Box(V(x + sx * 0.1f * s, 0.07f * s, z + sz * 0.09f * s), V(0.11f * s, 0.11f * s, 0.02f * s), Sh(wood, -0.25f));
            }
            float[,] te = { { -0.3f, -0.25f, 1 }, { 0.25f, -0.3f, 0.9f }, { 0.0f, 0.25f, 1.3f }, { -0.4f, 0.3f, 0.85f }, { 0.3f, 0.2f, 0.8f } };
            for (int i = 0; i < te.GetLength(0); i++) Tent(mb, te[i, 0] * s, te[i, 1] * s, 0.2f * te[i, 2] * s, te[i, 2] > 1 ? H("#d8c8a0") : felt);
            Standard(mb, -0.15f * s, -0.75f * s, 0.8f * s, H("#a83a2a"), "tail");
            Standard(mb, 0.15f * s, -0.75f * s, 0.8f * s, H("#a83a2a"), "tail");
        }
        // 希腊城（博斯普鲁斯）：白色城墙、卫城丘与神庙
        static void Greek(MeshBuilder mb, float s, bool cap)
        {
            float r = s, wallH = 0.36f * s, t = 0.15f * s;
            Color stone = H("#dcd6c6"), tile = H("#b4553a"), marble = H("#f2eee4"), stucco = H("#ece4d0");
            Base(mb, r, H("#a8a07c"));
            SquareWall(mb, r, wallH, t, stone, 0.07f * s, 3);
            foreach (var cx in new[] { -1, 1 }) foreach (var cz in new[] { -1, 1 })
                    if (!(cx > 0 && cz > 0)) mb.Box(V(cx * r, wallH * 0.65f, cz * r), V(t * 1.7f, wallH * 1.3f, t * 1.7f), Sh(stone, -0.05f));
            Gateway(mb, r, 0.22f * s, wallH, stone, Sh(stone, -0.05f), false);
            mb.Blob(V(-0.1f * s, 0.05f, 0.35f * s), V(0.55f * s, 0.26f * s, 0.45f * s), H("#9a9670"), 5);
            float y0 = 0.22f * s, tz = 0.35f * s;
            mb.Box(V(-0.1f * s, y0 + 0.04f * s, tz), V(0.46f * s, 0.08f * s, 0.32f * s), marble);
            for (int i = 0; i < 5; i++) Column(mb, (-0.28f + i * 0.09f) * s, y0 + 0.08f * s, tz - 0.12f * s, 0.22f * s, 0.018f * s, marble);
            mb.Box(V(-0.1f * s, y0 + 0.19f * s, tz + 0.04f * s), V(0.34f * s, 0.22f * s, 0.2f * s), stucco);
            Gable(mb, -0.1f * s, y0 + 0.31f * s, tz, 0.44f * s, 0.3f * s, 0.08f * s, tile, false, 0.01f * s);
            float[,] hs = { { -0.55f, -0.5f }, { 0.5f, -0.5f }, { 0.6f, 0.0f }, { -0.65f, -0.05f } };
            for (int i = 0; i < hs.GetLength(0); i++) GableHouse(mb, hs[i, 0] * s, hs[i, 1] * s, 0.26f * s, 0.22f * s, 0.16f * s, stucco, tile, false);
            Standard(mb, 0.25f * s, -0.3f * s, 0.6f * s, H("#a8261e"), "flag");
        }

        // ------------------------------------------------------------ 汉地 --
        // 原模型（与 Models.City 完全相同的几何），写入已有的 MeshBuilder（用于合并网格）
        public static void HanCityInto(MeshBuilder mb, float size, bool capital)
        {
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
        }

        // ============================================================ 接口 --
        static readonly Dictionary<string, string> ModelOf = new Dictionary<string, string>
        {
            { "roman", "roman" }, { "persia", "persia" }, { "arab", "arab" }, { "kushan", "kushan" }, { "wa", "wa" }, { "korea", "korea" }, { "steppe", "steppe" },
            { "tarim", "tarim" }, { "seasia", "seasia" }, { "yi", "village" }, { "nanman", "village" }, { "celt", "hillfort" }, { "german", "hillfort" }, { "sarmatian", "sarmatian" },
        };
        public static IEnumerable<string> Cultures { get { return ModelOf.Keys; } }

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
        // 写入 MeshBuilder（调用方先设好 mb.M = 位置 × 朝向）；han / 未知文化用原模型
        public static void CityInto(MeshBuilder mb, float size, string culture, bool capital, City city)
        {
            string m;
            if (culture == null || !ModelOf.TryGetValue(culture, out m)) { HanCityInto(mb, size, capital); return; }
            switch (m)
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
                default: HanCityInto(mb, size, capital); break;
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
