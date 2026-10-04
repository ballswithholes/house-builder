using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Sanguo
{
    // 战略地图：程序化生成的低多边形中国地形
    public class MapView : MonoBehaviour
    {
        public const float MapW = 112, MapH = 100;
        public Transform Root;
        public readonly Dictionary<int, CityVisual> Cities = new Dictionary<int, CityVisual>();
        GameObject selectRing;
        int selected = -1;
        readonly List<Transform> clouds = new List<Transform>();

        public class CityVisual
        {
            public City city; public GameObject go; public Renderer flag; public Renderer ring; public Transform labelAnchor; public Vector3 pos;
        }

        static Vector2 LL(float lon, float lat) { return new Vector2((lon - 100f) * 5f, (lat - 23f) * 5.6f); }

        // ---------------------------------------------------------- 地理数据 --
        static readonly Vector2[] Coast =
        {
            LL(124, 41.5f), LL(121.5f, 40.8f), LL(119.6f, 39.9f), LL(118.3f, 39.1f), LL(117.7f, 38.8f), LL(118.0f, 38.2f), LL(118.9f, 37.6f),
            LL(119.4f, 37.1f), LL(120.6f, 37.7f), LL(122.6f, 37.3f), LL(121.2f, 36.6f), LL(120.2f, 35.9f), LL(119.3f, 35.0f), LL(120.4f, 34.3f),
            LL(120.9f, 32.8f), LL(121.9f, 31.6f), LL(121.0f, 30.7f), LL(122.0f, 29.8f), LL(121.5f, 28.6f), LL(120.4f, 27.3f), LL(119.6f, 26.0f),
            LL(118.6f, 24.6f), LL(117.0f, 23.5f), LL(114.8f, 22.6f), LL(113.4f, 22.2f), LL(111.2f, 21.5f), LL(109.6f, 21.3f), LL(108.5f, 21.6f),
            LL(107.0f, 21.2f), LL(106.0f, 19.0f),
        };
        static readonly Vector2[][] Rivers =
        {
            // 黄河
            new[] { LL(99, 35.4f), LL(101.5f, 36.0f), LL(103.8f, 36.1f), LL(105.8f, 37.4f), LL(106.6f, 39.4f), LL(108.4f, 40.6f), LL(110.9f, 40.3f), LL(111.4f, 38.7f), LL(110.6f, 36.4f), LL(110.3f, 34.7f), LL(112.4f, 34.9f), LL(114.6f, 35.0f), LL(116.0f, 36.2f), LL(117.4f, 37.3f), LL(118.9f, 37.75f) },
            // 长江
            new[] { LL(99, 26.8f), LL(101.5f, 26.5f), LL(103.2f, 27.6f), LL(104.6f, 28.8f), LL(106.5f, 29.55f), LL(108.4f, 30.7f), LL(110.3f, 31.0f), LL(111.4f, 30.7f), LL(112.3f, 30.2f), LL(113.2f, 29.5f), LL(114.3f, 30.55f), LL(115.6f, 29.8f), LL(117.0f, 30.5f), LL(118.4f, 31.6f), LL(119.5f, 32.2f), LL(121.0f, 31.7f), LL(121.9f, 31.5f) },
            // 汉水
            new[] { LL(106.4f, 33.1f), LL(108.5f, 32.8f), LL(110.8f, 32.6f), LL(112.1f, 32.05f), LL(112.6f, 31.2f), LL(113.6f, 30.7f), LL(114.3f, 30.55f) },
            // 淮河
            new[] { LL(112.5f, 32.4f), LL(114.4f, 32.4f), LL(116.4f, 32.5f), LL(118.0f, 33.0f), LL(119.3f, 33.6f), LL(120.4f, 34.2f) },
            // 湘江
            new[] { LL(111.0f, 25.6f), LL(112.6f, 26.9f), LL(113.0f, 28.2f), LL(112.9f, 29.3f) },
            // 赣江
            new[] { LL(114.9f, 25.8f), LL(115.0f, 27.3f), LL(115.9f, 28.7f), LL(116.2f, 29.3f) },
        };
        static readonly Vector3[] Lakes = { new Vector3(LL(112.8f, 29.3f).x, LL(112.8f, 29.3f).y, 3.2f), new Vector3(LL(116.3f, 29.1f).x, LL(116.3f, 29.1f).y, 2.6f), new Vector3(LL(120.0f, 31.0f).x, LL(120.0f, 31.0f).y, 1.5f) };
        // 山脉：起点、终点、高度、宽度
        static readonly Vector4[] Ridges =
        {
            new Vector4(LL(104.5f, 34.0f).x, LL(104.5f, 34.0f).y, LL(111.8f, 34.0f).x, LL(111.8f, 34.0f).y),   // 秦岭
            new Vector4(LL(113.6f, 35.0f).x, LL(113.6f, 35.0f).y, LL(114.6f, 40.0f).x, LL(114.6f, 40.0f).y),   // 太行
            new Vector4(LL(105.5f, 32.3f).x, LL(105.5f, 32.3f).y, LL(110.0f, 31.6f).x, LL(110.0f, 31.6f).y),   // 大巴山
            new Vector4(LL(109.5f, 25.4f).x, LL(109.5f, 25.4f).y, LL(116.0f, 24.9f).x, LL(116.0f, 24.9f).y),   // 南岭
            new Vector4(LL(116.2f, 26.0f).x, LL(116.2f, 26.0f).y, LL(118.6f, 28.6f).x, LL(118.6f, 28.6f).y),   // 武夷
            new Vector4(LL(115.0f, 40.6f).x, LL(115.0f, 40.6f).y, LL(119.0f, 40.4f).x, LL(119.0f, 40.4f).y),   // 燕山
            new Vector4(LL(110.9f, 36.0f).x, LL(110.9f, 36.0f).y, LL(111.6f, 39.2f).x, LL(111.6f, 39.2f).y),   // 吕梁
            new Vector4(LL(98.0f, 39.0f).x, LL(98.0f, 39.0f).y, LL(103.2f, 37.0f).x, LL(103.2f, 37.0f).y),     // 祁连
            new Vector4(LL(113.6f, 31.6f).x, LL(113.6f, 31.6f).y, LL(116.4f, 31.1f).x, LL(116.4f, 31.1f).y),   // 大别山
            new Vector4(LL(109.0f, 30.2f).x, LL(109.0f, 30.2f).y, LL(110.6f, 31.6f).x, LL(110.6f, 31.6f).y),   // 巫山
            new Vector4(LL(108.0f, 27.0f).x, LL(108.0f, 27.0f).y, LL(110.4f, 29.4f).x, LL(110.4f, 29.4f).y),   // 武陵山
        };

        // ---------------------------------------------------------- 高度场 --
        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }
        // 海域多边形：海岸线 + 东侧远点
        static readonly Vector2[] SeaPoly = Coast.Concat(new[] { new Vector2(220, -40), new Vector2(220, 200), new Vector2(Coast[0].x, 200) }).ToArray();
        static bool InSea(Vector2 p)
        {
            var poly = SeaPoly;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }
        static float CoastDist(Vector2 p)
        {
            float d = 999;
            for (int i = 0; i < Coast.Length - 1; i++) d = Mathf.Min(d, SegDist(p, Coast[i], Coast[i + 1]));
            return d;
        }
        static float RiverDist(Vector2 p)
        {
            float d = 999;
            foreach (var r in Rivers) for (int i = 0; i < r.Length - 1; i++) d = Mathf.Min(d, SegDist(p, r[i], r[i + 1]));
            foreach (var l in Lakes) d = Mathf.Min(d, Mathf.Max(0, Vector2.Distance(p, new Vector2(l.x, l.y)) - l.z));
            return d;
        }
        static float Fbm(float x, float y, int oct = 4)
        {
            float s = 0, a = 0.5f, f = 1, n = 0;
            for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise(x * f + 31.7f * i, y * f + 17.3f * i); n += a; a *= 0.5f; f *= 2.03f; }
            return s / n;
        }

        float[] cityX, cityZ;
        public float Height(float x, float z)
        {
            var p = new Vector2(x, z);
            float coast = CoastDist(p);
            bool sea = InSea(p);
            float h;
            if (sea) h = -0.4f - Mathf.Min(coast, 12f) * 0.35f;
            else h = 0.35f + Mathf.Min(coast, 10f) * 0.08f;
            float n = Fbm(x * 0.045f, z * 0.045f);
            if (!sea) h += (n - 0.4f) * 2.4f;
            // 西部高原
            h += Mathf.SmoothStep(0, 1, Mathf.InverseLerp(22, 2, x)) * 4.5f * (0.7f + n * 0.6f);
            h += Mathf.SmoothStep(0, 1, Mathf.InverseLerp(30, 8, x)) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(10, 40, z)) * 2.0f;
            // 山脉
            if (!sea)
                foreach (var r in Ridges)
                {
                    float d = SegDist(p, new Vector2(r.x, r.y), new Vector2(r.z, r.w));
                    float k = Mathf.Exp(-(d * d) / 6.5f);
                    h += k * (2.6f + Fbm(x * 0.2f, z * 0.2f, 2) * 2.4f);
                }
            // 河流与湖泊
            float rd = RiverDist(p);
            if (rd < 2.2f && !sea)
            {
                float target = rd < 0.75f ? -0.35f : Mathf.Lerp(-0.1f, h, (rd - 0.75f) / 1.45f);
                h = Mathf.Min(h, target);
            }
            // 城市周围平整
            if (cityX != null)
                for (int i = 0; i < cityX.Length; i++)
                {
                    float d = Mathf.Sqrt((x - cityX[i]) * (x - cityX[i]) + (z - cityZ[i]) * (z - cityZ[i]));
                    if (d < 2.6f) { float k = Mathf.SmoothStep(1, 0, Mathf.Max(0, d - 1.2f) / 1.4f); h = Mathf.Lerp(h, Mathf.Clamp(h, 0.5f, 1.2f), k); }
                }
            return h;
        }

        Color GroundColor(float x, float z, float h, float slope, float jitter)
        {
            float north = Mathf.InverseLerp(10, 95, z);
            var lush = new Color(0.36f, 0.62f, 0.3f); var dry = new Color(0.66f, 0.64f, 0.38f);
            var grass = Color.Lerp(lush, dry, north * 0.85f);
            grass = Color.Lerp(grass, new Color(0.62f, 0.55f, 0.36f), Mathf.InverseLerp(30, 5, x) * 0.6f);
            Color c;
            if (h < -0.15f) c = new Color(0.7f, 0.66f, 0.5f);
            else if (h < 0.42f) c = new Color(0.86f, 0.8f, 0.6f);
            else c = grass;
            float forest = Fbm(x * 0.09f + 100, z * 0.09f + 100, 3);
            if (h >= 0.42f && h < 3.5f && forest > 0.56f) c = Color.Lerp(c, new Color(0.22f, 0.45f, 0.24f), Mathf.InverseLerp(0.56f, 0.64f, forest));
            if (h > 2.2f) c = Color.Lerp(c, new Color(0.56f, 0.52f, 0.4f), Mathf.InverseLerp(2.2f, 3.6f, h));
            if (h > 3.6f || slope > 0.62f) c = Color.Lerp(c, new Color(0.58f, 0.56f, 0.53f), Mathf.Max(Mathf.InverseLerp(3.6f, 5f, h), Mathf.InverseLerp(0.62f, 0.8f, slope)));
            if (h > 6.2f) c = Color.Lerp(c, new Color(0.95f, 0.96f, 0.98f), Mathf.InverseLerp(6.2f, 7.2f, h));
            return Art.Shade(c, jitter);
        }

        public bool IsForest(float x, float z, float h) { return h >= 0.6f && h < 3.2f && Fbm(x * 0.09f + 100, z * 0.09f + 100, 3) > 0.6f; }

        // ---------------------------------------------------------- 构建 --
        public void Build(GameState g)
        {
            Root = new GameObject("StrategyMap").transform;
            Root.SetParent(transform, false);
            cityX = g.cities.Select(c => c.MapPos.x).ToArray();
            cityZ = g.cities.Select(c => c.MapPos.y).ToArray();
            BuildTerrain();
            BuildWater();
            BuildTrees(g);
            BuildRoads(g);
            BuildCities(g);
            BuildClouds();
            selectRing = Art.MakeObject("SelectRing", FlatDisc(3.4f), Art.NewUnlit(new Color(1f, 0.85f, 0.35f, 0.95f), Art.Ring), Root, false);
            selectRing.SetActive(false);
            Refresh(g);
        }

        void BuildTerrain()
        {
            const float step = 1.25f;
            float x0 = -26, z0 = -24, x1 = MapW + 26, z1 = MapH + 22;
            int nx = Mathf.CeilToInt((x1 - x0) / step), nz = Mathf.CeilToInt((z1 - z0) / step);
            var hs = new float[nx + 1, nz + 1];
            var rnd = new System.Random(7);
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++)
                    hs[i, j] = Height(x0 + i * step, z0 + j * step);
            // 分块，避免单个网格过大
            int chunk = 32;
            for (int ci = 0; ci < nx; ci += chunk)
                for (int cj = 0; cj < nz; cj += chunk)
                {
                    var mb = new MeshBuilder();
                    for (int i = ci; i < Mathf.Min(nx, ci + chunk); i++)
                        for (int j = cj; j < Mathf.Min(nz, cj + chunk); j++)
                        {
                            Vector3 a = new Vector3(x0 + i * step, hs[i, j], z0 + j * step);
                            Vector3 b = new Vector3(x0 + i * step, hs[i, j + 1], z0 + (j + 1) * step);
                            Vector3 c = new Vector3(x0 + (i + 1) * step, hs[i + 1, j + 1], z0 + (j + 1) * step);
                            Vector3 d = new Vector3(x0 + (i + 1) * step, hs[i + 1, j], z0 + j * step);
                            bool flip = ((i + j) & 1) == 0;
                            if (flip) { AddTerrainTri(mb, a, b, c, rnd); AddTerrainTri(mb, a, c, d, rnd); }
                            else { AddTerrainTri(mb, a, b, d, rnd); AddTerrainTri(mb, b, c, d, rnd); }
                        }
                    var go = Art.MakeObject("Terrain", mb.ToMesh("terrain"), Art.LowPoly, Root, false);
                    go.GetComponent<MeshRenderer>().receiveShadows = true;
                }
        }
        void AddTerrainTri(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, System.Random rnd)
        {
            var center = (a + b + c) / 3f;
            var nrm = Vector3.Cross(b - a, c - a).normalized;
            float slope = 1 - Mathf.Abs(nrm.y);
            mb.Tri(a, b, c, GroundColor(center.x, center.z, center.y, slope, (float)(rnd.NextDouble() - 0.5) * 0.06f));
        }

        void BuildWater()
        {
            var mb = new MeshBuilder();
            const float step = 2.5f;
            float x0 = -60, z0 = -60, x1 = MapW + 60, z1 = MapH + 60;
            int nx = Mathf.CeilToInt((x1 - x0) / step), nz = Mathf.CeilToInt((z1 - z0) / step);
            var hg = new float[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) hg[i, j] = Height(x0 + i * step, z0 + j * step);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float xa = x0 + i * step, za = z0 + j * step;
                    // 四角都远高于水面的格子略过以节省顶点
                    if (hg[i, j] > 1.2f && hg[i + 1, j] > 1.2f && hg[i, j + 1] > 1.2f && hg[i + 1, j + 1] > 1.2f) continue;
                    var v = new[] { new Vector3(xa, 0, za), new Vector3(xa, 0, za + step), new Vector3(xa + step, 0, za + step), new Vector3(xa + step, 0, za) };
                    var hh = new[] { hg[i, j], hg[i, j + 1], hg[i + 1, j + 1], hg[i + 1, j] };
                    var cols = hh.Select(h => new Color(Mathf.Clamp01((h + 2.2f) / 2.2f), 0, 0, 1)).ToArray();
                    int s = mb.v.Count;
                    for (int k = 0; k < 4; k++) { mb.v.Add(v[k]); mb.n.Add(Vector3.up); mb.c.Add(cols[k]); mb.uv.Add(Vector2.zero); }
                    mb.t.Add(s); mb.t.Add(s + 1); mb.t.Add(s + 2); mb.t.Add(s); mb.t.Add(s + 2); mb.t.Add(s + 3);
                }
            var go = Art.MakeObject("Water", mb.ToMesh("water"), Art.Water, Root, false);
            go.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        void BuildTrees(GameState g)
        {
            var rnd = new System.Random(11);
            var mb = new MeshBuilder();
            int count = 0;
            for (float x = -10; x < MapW + 10; x += 1.15f)
                for (float z = -6; z < MapH + 6; z += 1.15f)
                {
                    float px = x + (float)(rnd.NextDouble() - 0.5) * 0.9f, pz = z + (float)(rnd.NextDouble() - 0.5) * 0.9f;
                    float h = Height(px, pz);
                    bool forest = IsForest(px, pz, h);
                    bool sparse = h > 0.5f && h < 2.5f && rnd.NextDouble() < 0.025;
                    if (!forest && !sparse) continue;
                    if (g.cities.Any(c => Vector2.Distance(c.MapPos, new Vector2(px, pz)) < 3.4f)) continue;
                    if (RiverDist(new Vector2(px, pz)) < 1.4f) continue;
                    float north = Mathf.InverseLerp(10, 95, pz);
                    bool pine = rnd.NextDouble() < 0.25 + north * 0.6 || h > 2f;
                    var leaf = pine ? new Color(0.2f, 0.42f, 0.28f) : Color.Lerp(new Color(0.3f, 0.58f, 0.26f), new Color(0.5f, 0.6f, 0.28f), north);
                    Models.Tree(mb, new Vector3(px, h - 0.05f, pz), 0.9f + (float)rnd.NextDouble() * 0.6f, Art.Shade(leaf, (float)(rnd.NextDouble() - 0.5) * 0.12f), pine, count);
                    if (++count % 900 == 0) { Art.MakeObject("Trees", mb.ToMesh("trees"), Art.LowPoly, Root); mb = new MeshBuilder(); }
                }
            if (mb.Count > 0) Art.MakeObject("Trees", mb.ToMesh("trees"), Art.LowPoly, Root);
        }

        void BuildRoads(GameState g)
        {
            var mb = new MeshBuilder();
            var col = new Color(0.78f, 0.68f, 0.5f);
            var done = new HashSet<string>();
            foreach (var c in g.cities)
                foreach (var li in c.links)
                {
                    var o = g.cities[li];
                    string key = Mathf.Min(c.id, o.id) + "-" + Mathf.Max(c.id, o.id);
                    if (!done.Add(key)) continue;
                    var a = c.MapPos; var b = o.MapPos;
                    float len = Vector2.Distance(a, b);
                    int n = Mathf.CeilToInt(len / 0.9f);
                    var dir = (b - a).normalized; var side = new Vector2(-dir.y, dir.x) * 0.22f;
                    Vector3 prevL = Vector3.zero, prevR = Vector3.zero;
                    for (int i = 0; i <= n; i++)
                    {
                        var p = Vector2.Lerp(a, b, i / (float)n);
                        // 道路略带弯曲
                        p += new Vector2(-dir.y, dir.x) * Mathf.Sin(i / (float)n * Mathf.PI) * (Mathf.PerlinNoise(c.id, o.id) - 0.5f) * len * 0.12f;
                        float h = Mathf.Max(0.12f, Height(p.x, p.y)) + 0.07f;
                        var l = new Vector3(p.x + side.x, h, p.y + side.y); var r = new Vector3(p.x - side.x, h, p.y - side.y);
                        if (i > 0) mb.Quad(prevL, l, r, prevR, col);
                        prevL = l; prevR = r;
                    }
                }
            Art.MakeObject("Roads", mb.ToMesh("roads"), Art.LowPoly, Root, false);
        }

        void BuildCities(GameState g)
        {
            var flagMesh = FlagMesh();
            foreach (var c in g.cities)
            {
                var pos = new Vector3(c.MapPos.x, Mathf.Max(0.3f, Height(c.MapPos.x, c.MapPos.y)), c.MapPos.y);
                float size = Mathf.Lerp(0.9f, 1.5f, Mathf.InverseLerp(180, 650, c.town));
                bool capital = c.key == "luoyang" || c.key == "changan";
                var go = Art.MakeObject("City_" + c.name, Models.City(size, Color.white, capital), Art.LowPoly, Root);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0, (c.id * 37) % 20 - 10, 0);
                var col = go.AddComponent<SphereCollider>(); col.radius = 2.6f; col.center = Vector3.up;
                var flag = Art.MakeObject("Flag", flagMesh, Art.NewLowPoly(), go.transform);
                flag.transform.localPosition = new Vector3(size * 0.9f, 0, size * 0.9f);
                flag.AddComponent<FlagWave>();
                var ring = Art.MakeObject("Territory", FlatDisc(2.9f), Art.NewUnlit(Color.white, Art.Ring), go.transform, false);
                ring.transform.localPosition = Vector3.up * 0.12f;
                var anchor = new GameObject("Label").transform; anchor.SetParent(go.transform, false); anchor.localPosition = Vector3.up * (size * 1.6f + 0.6f);
                Cities[c.id] = new CityVisual { city = c, go = go, flag = flag.GetComponent<Renderer>(), ring = ring.GetComponent<Renderer>(), labelAnchor = anchor, pos = pos };
            }
        }

        static Mesh FlagMesh()
        {
            var mb = new MeshBuilder();
            mb.Box(new Vector3(0, 1.1f, 0), new Vector3(0.08f, 2.2f, 0.08f), new Color(0.35f, 0.25f, 0.15f));
            var a = new Vector3(0.04f, 2.15f, 0); var b = new Vector3(0.95f, 2.05f, 0); var c = new Vector3(0.95f, 1.45f, 0); var d = new Vector3(0.04f, 1.5f, 0);
            mb.Quad(a, b, c, d, Color.white); mb.Quad(a, d, c, b, Color.white);
            mb.Cone(new Vector3(0, 2.2f, 0), 0.08f, 0.16f, 5, new Color(0.9f, 0.75f, 0.3f));
            return mb.ToMesh("flag");
        }
        public static Mesh FlatDisc(float r)
        {
            var mb = new MeshBuilder();
            mb.FlatQuad(new Vector3(-r, 0, -r), new Vector3(-r, 0, r), new Vector3(r, 0, r), new Vector3(r, 0, -r), Color.white);
            return mb.ToMesh("disc");
        }

        void BuildClouds()
        {
            var rnd = new System.Random(5);
            for (int i = 0; i < 9; i++)
            {
                var mb = new MeshBuilder();
                int puffs = 4 + rnd.Next(4);
                for (int k = 0; k < puffs; k++)
                {
                    var off = new Vector3((float)(rnd.NextDouble() - 0.5) * 7, (float)rnd.NextDouble() * 0.8f, (float)(rnd.NextDouble() - 0.5) * 3);
                    float r = 1.4f + (float)rnd.NextDouble() * 1.6f;
                    mb.Blob(off, new Vector3(r, r * 0.6f, r * 0.85f), new Color(1, 1, 1), k);
                }
                var go = Art.MakeObject("Cloud", mb.ToMesh("cloud"), Art.NewLowPoly(), Root);
                go.GetComponent<Renderer>().material.SetFloat("_Emission", 0.35f);
                go.transform.position = new Vector3((float)rnd.NextDouble() * MapW, 15 + (float)rnd.NextDouble() * 5, (float)rnd.NextDouble() * MapH);
                clouds.Add(go.transform);
            }
        }

        // ---------------------------------------------------------- 更新 --
        public void Refresh(GameState g)
        {
            var mpb = new MaterialPropertyBlock();
            foreach (var cv in Cities.Values)
            {
                cv.city = g.cities[cv.city.id];
                var col = cv.city.owner >= 0 ? g.factions[cv.city.owner].Col : new Color(0.75f, 0.75f, 0.72f);
                mpb.SetColor("_Color", col);
                cv.flag.SetPropertyBlock(mpb);
                cv.flag.enabled = cv.city.owner >= 0;
                cv.ring.material.color = new Color(col.r, col.g, col.b, cv.city.owner >= 0 ? (cv.city.owner == g.player ? 0.85f : 0.6f) : 0.25f);
            }
        }

        public void Select(int cityId)
        {
            selected = cityId;
            if (cityId < 0 || !Cities.ContainsKey(cityId)) { selectRing.SetActive(false); return; }
            selectRing.SetActive(true);
            selectRing.transform.position = Cities[cityId].pos + Vector3.up * 0.18f;
        }

        public int Pick(Camera cam, Vector2 screen)
        {
            RaycastHit hit;
            if (Physics.Raycast(cam.ScreenPointToRay(screen), out hit, 1000))
                foreach (var kv in Cities) if (hit.collider.gameObject == kv.Value.go) return kv.Key;
            // 无碰撞时选最近的城
            var ray = cam.ScreenPointToRay(screen);
            float d; var plane = new Plane(Vector3.up, Vector3.up * 0.5f);
            if (plane.Raycast(ray, out d))
            {
                var p = ray.GetPoint(d);
                var best = Cities.Values.OrderBy(c => Vector3.Distance(c.pos, p)).First();
                if (Vector3.Distance(best.pos, p) < 4f) return best.city.id;
            }
            return -1;
        }

        void Update()
        {
            if (selectRing != null && selectRing.activeSelf)
            {
                float s = 1 + Mathf.Sin(Time.time * 3f) * 0.06f;
                selectRing.transform.localScale = new Vector3(s, 1, s);
                selectRing.transform.Rotate(0, Time.deltaTime * 25, 0);
            }
            foreach (var c in clouds)
            {
                c.position += new Vector3(0.6f, 0, 0.15f) * Time.deltaTime;
                if (c.position.x > MapW + 30) c.position = new Vector3(-30, c.position.y, c.position.z);
            }
        }

        // 行军动画：一支小部队沿道路移动
        public IEnumerator March(City from, City to, Color team, float seconds = 1.6f)
        {
            var mb = new MeshBuilder();
            Models.Commander(mb, Vector3.zero, team, 2.4f);
            for (int i = 0; i < 6; i++) Models.Soldier(mb, new Vector3((i % 3 - 1) * 0.55f, 0, -0.9f - (i / 3) * 0.55f), team, true, 2.2f);
            var go = Art.MakeObject("Army", mb.ToMesh("army"), Art.LowPoly, Root);
            var a = Cities[from.id].pos; var b = Cities[to.id].pos;
            go.transform.rotation = Quaternion.LookRotation(new Vector3(b.x - a.x, 0, b.z - a.z));
            for (float t = 0; t < 1; t += Time.deltaTime / seconds)
            {
                float k = Mathf.SmoothStep(0, 1, t);
                var p = Vector3.Lerp(a, b, k);
                p.y = Mathf.Max(0.2f, Height(p.x, p.z)) + Mathf.Abs(Mathf.Sin(t * 30)) * 0.08f;
                go.transform.position = p;
                yield return null;
            }
            Destroy(go);
        }
    }

    public class FlagWave : MonoBehaviour
    {
        float phase;
        void Start() { phase = Random.value * 10; }
        void Update() { transform.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 1.7f + phase) * 14f, 0); }
    }
}
