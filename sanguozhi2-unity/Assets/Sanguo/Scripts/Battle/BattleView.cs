using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Sanguo
{
    // 战场的三维表现
    // 第二版新增（网页版 battle-view.js 的移植）：
    //   SetCulture(culture)：城墙、城门、本阵与地表色调按战场城池的文化（BattleLook；构建时自动取 setup.target 的文化）
    //   SpecialFx(caster, targets, fx, color[, info]) → IEnumerator：必杀技特效（见 BattleViewFx.cs）
    //   UnitVisual.Tint(color, k) 着色光、Burn() 灼烧、限时加成的光环与名牌小字；燃烧中的格子显示持续火苗
    public partial class BattleView : MonoBehaviour
    {
        public static readonly Vector3 Origin = new Vector3(400, 0, 0);
        public const float T = 2f;
        public BattleModel M;
        Transform root;
        readonly Dictionary<BUnit, UnitVisual> vis = new Dictionary<BUnit, UnitVisual>();
        readonly List<GameObject> highlights = new List<GameObject>();
        GameObject cursor;
        readonly List<GameObject> fires = new List<GameObject>();
        Mesh tileQuad;
        // 第二版：文化外观与资源释放
        public BattleLook.Look Look { get; private set; }
        Transform field;                                            // 棋盘、装饰、河面、四周地形（SetCulture 时重建）
        readonly List<Mesh> fieldMeshes = new List<Mesh>();
        readonly List<Mesh> ownMeshes = new List<Mesh>();
        readonly List<Material> ownMats = new List<Material>();
        Mesh unitDisc;                                              // 半径 1 的贴图方片（光环、范围格）
        public Mesh UnitDisc { get { if (unitDisc == null) { unitDisc = MapView.FlatDisc(1f); ownMeshes.Add(unitDisc); } return unitDisc; } }
        public bool SpLabelsDim;                                    // 必杀技特效期间部队名牌淡出（不透明度 0.16）

        public Vector3 BoardCenter { get { return Origin + new Vector3(M.W * T / 2, 0, M.H * T / 2); } }
        public float TopH(int x, int y)
        {
            switch (M.map[x, y])
            {
                case Terrain.River: return -0.25f;
                case Terrain.Wall: return 1.4f;
                case Terrain.Mountain: return 0.6f;
                case Terrain.Hill: return 0.45f;
                case Terrain.Gate: case Terrain.Castle: return 0.2f;
                default: return 0.08f + M.height[x, y] * 0.6f;
            }
        }
        public Vector3 Tile(int x, int y)
        {
            float h = M.map[x, y] == Terrain.River ? -0.05f : M.map[x, y] == Terrain.Wall ? 1.4f : TopH(x, y);
            return Origin + new Vector3(x * T + T / 2, h, y * T + T / 2);
        }

        public static BattleView Create(BattleModel m)
        {
            var go = new GameObject("BattleView");
            var v = go.AddComponent<BattleView>();
            v.M = m;
            v.Build();
            return v;
        }

        // ---------------------------------------------------------- 构建 --
        void Build()
        {
            root = transform;
            Look = BattleLook.Of(BattleLook.CultureOf(M.setup));
            BuildField();
            tileQuad = MapView.FlatDisc(T * 0.46f); ownMeshes.Add(tileQuad);
            var cursorDisc = MapView.FlatDisc(T * 0.62f); ownMeshes.Add(cursorDisc);
            var cursorMat = Art.NewUnlit(new Color(1, 0.85f, 0.35f, 1), Art.Ring); ownMats.Add(cursorMat);
            cursor = Art.MakeObject("Cursor", cursorDisc, cursorMat, root, false);
            cursor.SetActive(false);
            var atkCol = M.setup.attacker >= 0 ? GameState.Current.factions[M.setup.attacker].Col : Color.gray;
            var defCol = M.setup.defender >= 0 ? GameState.Current.factions[M.setup.defender].Col : Color.gray;
            foreach (var u in M.units) CreateUnit(u, u.side == 0 ? atkCol : defCol);
        }

        // 战场的文化外观：城墙、城门、本阵样式与地表色调（汉地 / 未知文化保持原样）。构建时已按战场城池的文化设定，
        // 这里可以改用别的文化重建（只重建地形装饰，部队与特效不受影响）
        public void SetCulture(string culture)
        {
            var lk = BattleLook.Of(culture);
            if (Look != null && field != null && lk.culture == Look.culture) return;
            Look = lk;
            if (M != null && root != null) BuildField();
        }

        void BuildField()
        {
            if (field != null) Destroy(field.gameObject);
            foreach (var m in fieldMeshes) if (m != null) Destroy(m);
            fieldMeshes.Clear();
            field = new GameObject("Field").transform;
            field.SetParent(root, false);
            var gr = Look.ground; var st = Look.style;
            var mb = new MeshBuilder();
            var deco = new MeshBuilder();
            var rnd = new System.Random(M.setup.target.id);
            var defCol = M.setup.defender >= 0 ? GameState.Current.factions[M.setup.defender].Col : Color.gray;
            var ca = M.castle;
            System.Func<int, int, bool> isWall = (x, y) => M.InBounds(x, y) && (M.map[x, y] == Terrain.Wall || M.map[x, y] == Terrain.Gate);
            for (int x = 0; x < M.W; x++)
                for (int y = 0; y < M.H; y++)
                {
                    var t = M.map[x, y];
                    float h = TopH(x, y);
                    var p0 = Origin + new Vector3(x * T, 0, y * T);
                    Color c = TileColor(t, (float)rnd.NextDouble(), gr);
                    float inset = 0.05f;
                    var a = p0 + new Vector3(inset, h, inset); var b = p0 + new Vector3(inset, h, T - inset);
                    var cc = p0 + new Vector3(T - inset, h, T - inset); var d = p0 + new Vector3(T - inset, h, inset);
                    mb.Quad(a, b, cc, d, c);
                    // 侧面（让高低差可见）
                    var dark = Art.Shade(c, -0.25f);
                    float bot = -0.6f;
                    mb.Quad(p0 + new Vector3(inset, bot, inset), a, d, p0 + new Vector3(T - inset, bot, inset), dark);
                    mb.Quad(p0 + new Vector3(T - inset, bot, inset), d, cc, p0 + new Vector3(T - inset, bot, T - inset), dark);
                    mb.Quad(p0 + new Vector3(T - inset, bot, T - inset), cc, b, p0 + new Vector3(inset, bot, T - inset), dark);
                    mb.Quad(p0 + new Vector3(inset, bot, T - inset), b, a, p0 + new Vector3(inset, bot, inset), dark);
                    var center = p0 + new Vector3(T / 2, h, T / 2);
                    switch (t)
                    {
                        case Terrain.Forest:
                            for (int k = 0; k < 4; k++)
                            {
                                float dx = (float)(rnd.NextDouble() - 0.5) * 1.3f, dz = (float)(rnd.NextDouble() - 0.5) * 1.3f;
                                float s = 0.9f + (float)rnd.NextDouble() * 0.5f;
                                var leaf = Art.Shade(gr.leaf, (float)(rnd.NextDouble() - 0.5) * 0.15f);
                                bool pine = rnd.NextDouble() < 0.4;
                                // 沙漠绿洲全是椰枣，南方林中夹椰树（不多取随机数，与原序列一致）
                                if (gr.palm > 0 && (gr.palm >= 1 || ((k + x * 3 + y * 5) % 5) < gr.palm * 5))
                                    BattleLook.Palm(deco, center.x + dx, center.y, center.z + dz, s * 1.35f, k + x * 7 + y * 13);
                                else Models.Tree(deco, center + new Vector3(dx, 0, dz), s, leaf, pine, k + x * 7 + y * 13);
                            }
                            break;
                        case Terrain.Hill:
                            deco.Blob(center + new Vector3(0, -0.1f, 0), new Vector3(0.85f, 0.35f, 0.85f), gr.hillBlob, x * 31 + y);
                            break;
                        case Terrain.Mountain:
                            deco.Cone(center + new Vector3(-0.2f, -0.1f, 0.1f), 0.9f, 1.9f + (float)rnd.NextDouble() * 0.6f, 5, gr.mountain0);
                            deco.Cone(center + new Vector3(0.45f, -0.1f, -0.35f), 0.55f, 1.1f, 5, gr.mountain1);
                            deco.Cone(center + new Vector3(-0.2f, 1.25f, 0.1f), 0.32f, 0.9f, 5, gr.snow ? new Color(0.95f, 0.96f, 0.98f) : Art.Shade(gr.mountain0, 0.08f));
                            break;
                        case Terrain.Wall:
                            if (st != "han" && BattleLook.Wall(deco, st, center.x, h, center.z,
                                isWall(x - 1, y) || isWall(x + 1, y), isWall(x, y - 1) || isWall(x, y + 1), System.Math.Sign(x - ca.x), System.Math.Sign(y - ca.y))) break;
                            deco.Box(center + new Vector3(0, -0.6f, 0), new Vector3(T, 1.6f, T), new Color(0.66f, 0.63f, 0.57f));
                            for (int k = -1; k <= 1; k += 2) deco.Box(center + new Vector3(k * 0.55f, 0.32f, k * 0.55f), new Vector3(0.45f, 0.35f, 0.45f), new Color(0.7f, 0.67f, 0.6f));
                            break;
                        case Terrain.Gate:
                            if (BattleLook.Gate(deco, st, center.x, h, center.z)) break;
                            deco.Box(center + new Vector3(0, 0.75f, 0), new Vector3(T * 0.9f, 1.1f, 0.4f), new Color(0.62f, 0.2f, 0.16f));
                            deco.ChineseRoof(center + new Vector3(0, 1.3f, 0), 2.2f, 1.2f, 0.7f, new Color(0.22f, 0.26f, 0.34f));
                            break;
                        case Terrain.Castle:
                            if (!BattleLook.Keep(deco, st, center.x, h, center.z))
                            {
                                deco.Box(center + new Vector3(0, 0.15f, 0.55f), new Vector3(1.6f, 0.3f, 0.7f), new Color(0.75f, 0.72f, 0.64f));
                                deco.Box(center + new Vector3(0, 0.75f, 0.6f), new Vector3(1.2f, 0.9f, 0.5f), new Color(0.66f, 0.22f, 0.18f));
                                deco.ChineseRoof(center + new Vector3(0, 1.2f, 0.6f), 1.9f, 1.0f, 0.8f, new Color(0.85f, 0.65f, 0.2f));
                            }
                            deco.Flag(center + new Vector3(0.8f, 0, 0.8f), 2.4f, 0.8f, 0.55f, defCol);
                            break;
                    }
                }
            var boardMesh = mb.ToMesh("board"); fieldMeshes.Add(boardMesh);
            Art.MakeObject("Board", boardMesh, Art.LowPoly, field, false).GetComponent<MeshRenderer>().receiveShadows = true;
            var decoMesh = deco.ToMesh("deco"); fieldMeshes.Add(decoMesh);
            Art.MakeObject("Deco", decoMesh, Art.LowPoly, field);
            // 河面
            var wm = new MeshBuilder();
            for (int x = 0; x < M.W; x++) for (int y = 0; y < M.H; y++)
                    if (M.map[x, y] == Terrain.River)
                    {
                        var p0 = Origin + new Vector3(x * T, -0.05f, y * T);
                        wm.FlatQuad(p0, p0 + new Vector3(0, 0, T), p0 + new Vector3(T, 0, T), p0 + new Vector3(T, 0, 0), new Color(0.4f, 0, 0));
                    }
            if (wm.Count > 0) { var rm = wm.ToMesh("river"); fieldMeshes.Add(rm); Art.MakeObject("River", rm, Art.Water, field, false); }
            BuildSurroundings(rnd);
        }

        static Color TileColor(Terrain t, float r) { return TileColor(t, r, null); }
        static Color TileColor(Terrain t, float r, BattleLook.Ground gr)
        {
            Color c;
            switch (t)
            {
                case Terrain.Forest: c = gr != null ? gr.forest : new Color(0.33f, 0.55f, 0.3f); break;
                case Terrain.Hill: c = gr != null ? gr.hill : new Color(0.6f, 0.62f, 0.38f); break;
                case Terrain.Mountain: c = new Color(0.52f, 0.5f, 0.46f); break;
                case Terrain.River: c = new Color(0.55f, 0.5f, 0.38f); break;
                case Terrain.Wall: c = new Color(0.62f, 0.6f, 0.55f); break;
                case Terrain.Gate: c = new Color(0.62f, 0.58f, 0.5f); break;
                case Terrain.Castle: c = new Color(0.7f, 0.66f, 0.56f); break;
                default: c = gr != null ? gr.plain : new Color(0.47f, 0.68f, 0.36f); break;
            }
            return Art.Shade(c, (r - 0.5f) * 0.08f);
        }

        void BuildSurroundings(System.Random rnd)
        {
            var gr = Look != null ? Look.ground : BattleLook.GroundOf("temperate");
            var mb = new MeshBuilder();
            float size = 3f, x0 = -40, z0 = -30, x1 = M.W * T + 40, z1 = M.H * T + 34;
            for (float x = x0; x < x1; x += size)
                for (float z = z0; z < z1; z += size)
                {
                    bool inside = x >= -0.5f && z >= -0.5f && x < M.W * T && z < M.H * T;
                    if (inside) continue;
                    float dEdge = Mathf.Max(Mathf.Max(-x, x - M.W * T), Mathf.Max(-z, z - M.H * T));
                    System.Func<float, float, float> h = (px, pz) =>
                    {
                        float de = Mathf.Max(Mathf.Max(-px, px - M.W * T), Mathf.Max(-pz, pz - M.H * T));
                        return Mathf.Max(-0.5f, (Mathf.PerlinNoise(px * 0.08f + 3, pz * 0.08f + 7) - 0.4f) * 2f + Mathf.Max(0, de - 6) * 0.25f) - 0.3f;
                    };
                    var a = Origin + new Vector3(x, h(x, z), z); var b = Origin + new Vector3(x, h(x, z + size), z + size);
                    var c = Origin + new Vector3(x + size, h(x + size, z + size), z + size); var d = Origin + new Vector3(x + size, h(x + size, z), z);
                    var col = Color.Lerp(gr.sur0, gr.sur1, Mathf.InverseLerp(4, 16, (a.y + c.y) / 2 + 2));
                    mb.Tri(a, b, c, Art.Shade(col, (float)(rnd.NextDouble() - 0.5) * 0.08f)); mb.Tri(a, c, d, Art.Shade(col, (float)(rnd.NextDouble() - 0.5) * 0.08f));
                    if (rnd.NextDouble() < 0.18 && dEdge > 1.5f)
                    {
                        float ts = 1.2f + (float)rnd.NextDouble(); bool pine = rnd.NextDouble() < 0.5;
                        var tp = Origin + new Vector3(x + 1.5f, h(x + 1.5f, z + 1.5f), z + 1.5f);
                        if (gr.palm > 0 && (gr.palm >= 1 || !pine)) BattleLook.Palm(mb, tp.x, tp.y, tp.z, ts * 1.4f, (int)(x * 13 + z));
                        else Models.Tree(mb, tp, ts, gr.surLeaf, pine, (int)(x * 13 + z));
                    }
                }
            var sm = mb.ToMesh("surround"); fieldMeshes.Add(sm);
            Art.MakeObject("Surround", sm, Art.LowPoly, field);
            // 棋盘边框
            var frame = new MeshBuilder();
            var wood = new Color(0.36f, 0.24f, 0.14f);
            float W = M.W * T, H = M.H * T;
            frame.Box(Origin + new Vector3(W / 2, 0.05f, -0.25f), new Vector3(W + 1, 0.4f, 0.5f), wood);
            frame.Box(Origin + new Vector3(W / 2, 0.05f, H + 0.25f), new Vector3(W + 1, 0.4f, 0.5f), wood);
            frame.Box(Origin + new Vector3(-0.25f, 0.05f, H / 2), new Vector3(0.5f, 0.4f, H), wood);
            frame.Box(Origin + new Vector3(W + 0.25f, 0.05f, H / 2), new Vector3(0.5f, 0.4f, H), wood);
            var fm = frame.ToMesh("frame"); fieldMeshes.Add(fm);
            Art.MakeObject("Frame", fm, Art.LowPoly, field);
        }

        void CreateUnit(BUnit u, Color col)
        {
            var go = new GameObject("Unit_" + u.gen.name);
            go.transform.SetParent(root, false);
            var uv = go.AddComponent<UnitVisual>();
            uv.Init(this, u, col);
            vis[u] = uv;
        }
        public UnitVisual Vis(BUnit u) { return vis[u]; }
        public UnitVisual VisOf(BUnit u) { UnitVisual v; return u != null && vis.TryGetValue(u, out v) ? v : null; }
        public bool IsShown(BUnit u) { var v = VisOf(u); return v != null && v.gameObject.activeSelf; }

        // ---------------------------------------------------------- 高亮 --
        public void ClearHighlights() { foreach (var h in highlights) Destroy(h); highlights.Clear(); }
        readonly Dictionary<Color, Material> hlMats = new Dictionary<Color, Material>();
        public void Highlight(IEnumerable<Vector2Int> tiles, Color c)
        {
            Material mat;
            if (!hlMats.TryGetValue(c, out mat)) { mat = Art.NewUnlit(c, Art.SoftDot); hlMats[c] = mat; ownMats.Add(mat); }
            foreach (var t in tiles)
            {
                var go = Art.MakeObject("HL", tileQuad, mat, root, false);
                go.transform.position = Tile(t.x, t.y) + Vector3.up * 0.06f;
                go.transform.localScale = new Vector3(1.6f, 1, 1.6f);
                highlights.Add(go);
            }
        }
        public void SetCursor(BUnit u)
        {
            cursor.SetActive(u != null);
            if (u != null) cursor.transform.position = Tile(u.x, u.y) + Vector3.up * 0.08f;
        }
        void Update()
        {
            if (cursor != null && cursor.activeSelf) cursor.transform.Rotate(0, 40 * Time.deltaTime, 0);
            // 燃烧格
            for (int i = fires.Count - 1; i >= 0; i--) if (fires[i] == null) fires.RemoveAt(i);
            SpUpdate(Time.deltaTime);   // 必杀技：粒子、发射器、光源、燃烧中的格子
        }

        public bool TileFromScreen(Camera cam, Vector2 screen, out Vector2Int tile)
        {
            tile = default(Vector2Int);
            var ray = cam.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, Origin + Vector3.up * 0.3f);
            float d;
            if (!plane.Raycast(ray, out d)) return false;
            var p = ray.GetPoint(d) - Origin;
            int x = Mathf.FloorToInt(p.x / T), y = Mathf.FloorToInt(p.z / T);
            if (!M.InBounds(x, y)) return false;
            tile = new Vector2Int(x, y);
            return true;
        }

        // ---------------------------------------------------------- 动画 --
        public IEnumerator MoveUnit(BUnit u, List<Vector2Int> path)
        {
            var v = vis[u];
            Sfx.Play("march", 0.4f);
            for (int i = 1; i < path.Count; i++)
            {
                var a = Tile(path[i - 1].x, path[i - 1].y); var b = Tile(path[i].x, path[i].y);
                v.Face(b - a);
                for (float t = 0; t < 1; t += Time.deltaTime / 0.18f)
                {
                    v.transform.position = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Abs(Mathf.Sin(t * Mathf.PI)) * 0.12f;
                    yield return null;
                }
                v.transform.position = b;
            }
            v.Rest();
        }

        public IEnumerator Lunge(BUnit a, BUnit t)
        {
            var v = vis[a]; var from = Tile(a.x, a.y); var to = Tile(t.x, t.y);
            v.Face(to - from);
            for (float k = 0; k < 1; k += Time.deltaTime / 0.28f)
            {
                v.transform.position = Vector3.Lerp(from, to, Mathf.Sin(k * Mathf.PI) * 0.38f);
                yield return null;
            }
            v.transform.position = from;
        }

        public void Hit(BUnit u, int dmg, bool big = false)
        {
            if (!vis.ContainsKey(u)) return;
            vis[u].Flash();
            Burst(Tile(u.x, u.y) + Vector3.up * 0.6f, new Color(1f, 0.9f, 0.6f), 14, 2.5f);
            FloatText(Tile(u.x, u.y) + Vector3.up * 1.6f, "-" + dmg, big ? new Color(1f, 0.82f, 0.3f) : new Color(1f, 0.55f, 0.45f), big ? 44 : 34);
        }
        public void Refresh(BUnit u) { if (vis.ContainsKey(u)) vis[u].Refresh(); }
        public IEnumerator Rout(BUnit u)
        {
            var v = vis[u];
            v.holdLabel = false;
            Sfx.Play("lose", 0.25f);
            Smoke(Tile(u.x, u.y) + Vector3.up * 0.3f);
            var p = v.transform.position;
            for (float t = 0; t < 1; t += Time.deltaTime / 0.6f) { v.transform.position = p - Vector3.up * t * 0.8f; yield return null; }
            v.gameObject.SetActive(false);
        }

        public void FloatText(Vector3 world, string text, Color col, int size = 34)
        {
            var t = UIKit.Label(UIKit.LabelLayer, text, size, col, TextAnchor.MiddleCenter, true, "Float");
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(300, 60);
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0, 0, 0, 0.8f); o.effectDistance = new Vector2(2, -2);
            var f = t.gameObject.AddComponent<WorldFollow>(); f.worldPos = world;
            t.gameObject.AddComponent<FloatUp>();
        }

        // ---------------------------------------------------------- 特效 --
        ParticleSystem MakeParticles(Vector3 pos, Color c, float life, int count, float speed, float size, float gravity, float radius, bool loop = false)
        {
            var go = new GameObject("FX");
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = loop ? 1f : 0.3f; main.loop = loop; main.startLifetime = life; main.startSpeed = speed; main.startSize = size;
            main.startColor = c; main.gravityModifier = gravity; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 400;
            var em = ps.emission; em.rateOverTime = loop ? count : 0;
            if (!loop) em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.3f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = Art.NewAdditive(Color.white);
            r.material = mat;
            go.AddComponent<DestroyMaterialOnDestroy>().mat = mat;
            ps.Play();
            if (!loop) Destroy(go, life + 0.5f);
            return ps;
        }
        public void Burst(Vector3 p, Color c, int n, float speed) { MakeParticles(p, c, 0.5f, n, speed, 0.25f, 0.6f, 0.3f); }
        public void Smoke(Vector3 p) { MakeParticles(p, new Color(0.5f, 0.45f, 0.4f, 0.6f), 1.4f, 30, 0.8f, 0.9f, -0.05f, 0.6f); }

        public IEnumerator FireFx(BUnit target)
        {
            Sfx.Play("fire");
            var p = Tile(target.x, target.y);
            var ps = MakeParticles(p + Vector3.up * 0.2f, new Color(1f, 0.55f, 0.15f), 0.9f, 90, 1.6f, 0.7f, -0.25f, 0.7f, true);
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(1.6f, 0.2f, 1.6f);
            var light = new GameObject("FireLight").AddComponent<Light>();
            light.transform.SetParent(ps.transform, false); light.transform.localPosition = Vector3.up; light.type = LightType.Point; light.color = new Color(1, 0.5f, 0.2f); light.range = 6; light.intensity = 3;
            yield return new WaitForSeconds(1.0f);
            var em = ps.emission; em.rateOverTime = 25;
            Destroy(ps.gameObject, 2.5f);
            fires.Add(ps.gameObject);
        }
        public IEnumerator RockFx(BUnit target)
        {
            var p = Tile(target.x, target.y);
            var rocks = new List<Transform>();
            var rnd = new System.Random();
            for (int i = 0; i < 6; i++)
            {
                var mb = new MeshBuilder(); mb.Blob(Vector3.zero, Vector3.one * (0.25f + (float)rnd.NextDouble() * 0.2f), new Color(0.55f, 0.52f, 0.48f), i);
                var go = Art.MakeObject("Rock", mb.ToMesh("rock"), Art.LowPoly, root);
                go.transform.position = p + new Vector3((float)(rnd.NextDouble() - 0.5) * 1.4f, 7 + i * 0.8f, (float)(rnd.NextDouble() - 0.5) * 1.4f);
                rocks.Add(go.transform);
            }
            for (float t = 0; t < 0.9f; t += Time.deltaTime)
            {
                foreach (var r in rocks) { if (r.position.y > p.y + 0.2f) r.position -= Vector3.up * Time.deltaTime * 14; r.Rotate(200 * Time.deltaTime, 90 * Time.deltaTime, 0); }
                yield return null;
            }
            Sfx.Play("rock");
            Smoke(p);
            yield return new WaitForSeconds(0.3f);
            foreach (var r in rocks) { var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh); Destroy(r.gameObject); }
        }
        public IEnumerator MagicFx(BUnit target, Color c)
        {
            Sfx.Play("magic", 0.6f);
            var p = Tile(target.x, target.y);
            var mat = Art.NewUnlit(c, Art.Ring);
            var ring = Art.MakeObject("Spell", UnitDisc, mat, root, false);
            ring.transform.position = p + Vector3.up * 0.15f;
            MakeParticles(p + Vector3.up * 0.5f, c, 0.9f, 40, 1.4f, 0.3f, -0.4f, 0.6f);
            for (float t = 0; t < 0.9f; t += Time.deltaTime)
            {
                ring.transform.localScale = Vector3.one * 1.2f * (0.5f + t * 1.4f);
                ring.transform.Rotate(0, 300 * Time.deltaTime, 0);
                yield return null;
            }
            Destroy(ring);
            Destroy(mat);
        }

        void OnDestroy()
        {
            SpDispose();
            foreach (var m in fieldMeshes) if (m != null) Destroy(m);
            fieldMeshes.Clear();
            foreach (var m in ownMeshes) if (m != null) Destroy(m);
            ownMeshes.Clear();
            foreach (var m in ownMats) if (m != null) Destroy(m);
            ownMats.Clear();
            hlMats.Clear();
        }
    }

    // 粒子系统等临时物体销毁时一并释放其材质
    public class DestroyMaterialOnDestroy : MonoBehaviour
    {
        public Material mat;
        void OnDestroy() { if (mat != null) Destroy(mat); }
    }

    public class FloatUp : MonoBehaviour
    {
        float t; WorldFollow f; Text txt;
        void Start() { f = GetComponent<WorldFollow>(); txt = GetComponent<Text>(); }
        void Update()
        {
            t += Time.deltaTime;
            f.offset = new Vector2(0, t * 70);
            var c = txt.color; c.a = Mathf.Clamp01(1.6f - t * 1.4f); txt.color = c;
            if (t > 1.2f) Destroy(gameObject);
        }
    }

    // 一支部队：武将 + 若干士兵 + 头顶信息
    public class UnitVisual : MonoBehaviour
    {
        BattleView view; public BUnit u; Color col;
        MeshFilter mf; MeshRenderer mr; int bucket = -1;
        Material mat;
        Text label; RectTransform barFill; RectTransform info;
        float flash; float facing;
        GameObject confuseRing; Material confuseMat;
        // 第二版：着色光、灼烧、限时加成
        public bool holdLabel;            // 控制器置 true 时，阵亡部队的名牌暂不隐藏（单挑结束到溃散动画之间）
        public bool hidden;               // 暗影刺杀：部队暂时隐身
        float heat, heatSeed, spTint; Color spTintColor = Color.white;
        string spKey = "";
        GameObject buffRing; Material buffMat;
        RectTransform chips;
        static readonly Color HeatTint = new Color(1, 0.42f, 0.1f);

        public Mesh SharedMesh { get { return mf != null ? mf.sharedMesh : null; } }

        public void Init(BattleView v, BUnit unit, Color c)
        {
            view = v; u = unit; col = c;
            mf = gameObject.AddComponent<MeshFilter>();
            mr = gameObject.AddComponent<MeshRenderer>();
            mat = Art.NewLowPoly();
            mr.sharedMaterial = mat;
            heatSeed = unit.gen != null ? (Mathf.Abs(unit.gen.id) % 97) * 0.103f : 0;   // 装饰性相位（不耗规则随机数）
            transform.position = v.Tile(u.x, u.y);
            facing = u.side == 0 ? 90 : -90;
            transform.rotation = Quaternion.Euler(0, facing, 0);
            // 头顶标签
            info = UIKit.Img(UIKit.LabelLayer, UIKit.RR, new Color(0.05f, 0.05f, 0.08f, 0.78f), "UnitInfo").rectTransform;
            info.anchorMin = info.anchorMax = new Vector2(0.5f, 0.5f); info.sizeDelta = new Vector2(130, 44);
            var stripe = UIKit.Img(info, UIKit.RR, c, "Stripe"); stripe.rectTransform.anchorMin = new Vector2(0, 0); stripe.rectTransform.anchorMax = new Vector2(0, 1); stripe.rectTransform.offsetMin = new Vector2(3, 3); stripe.rectTransform.offsetMax = new Vector2(9, -3);
            label = UIKit.Label(info, "", 18, UIKit.Text, TextAnchor.UpperCenter);
            UIKit.Stretch(label.rectTransform, 10, 12, 4, 2);
            var bar = UIKit.Bar(info, 1, u.side == 0 ? new Color(0.45f, 0.75f, 1f) : new Color(1f, 0.5f, 0.42f));
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.offsetMin = new Vector2(14, 5); bar.offsetMax = new Vector2(-6, 11);
            barFill = (RectTransform)bar.Find("Fill");
            var wf = info.gameObject.AddComponent<WorldFollow>(); wf.target = transform; wf.offset = new Vector2(0, 74); wf.hideBeyond = 90;
            Refresh();
        }

        public void Refresh()
        {
            int b = Mathf.Clamp(Mathf.CeilToInt(u.Troops / 350f), u.alive ? 1 : 0, 12);
            if (b != bucket)
            {
                bucket = b;
                var mb = new MeshBuilder();
                Models.Commander(mb, new Vector3(0, 0, 0.35f), col, 1.5f);
                for (int i = 0; i < b; i++)
                {
                    int row = i / 4, cIdx = i % 4;
                    var p = new Vector3((cIdx - 1.5f) * 0.38f + (row % 2) * 0.1f, 0, -0.25f - row * 0.36f);
                    Models.Soldier(mb, p, col, true, 1.3f);
                }
                var old = mf.sharedMesh;
                mf.sharedMesh = mb.ToMesh("unit");
                if (old != null) Destroy(old);
            }
            if (label != null)
            {
                label.text = (u.commander ? "<color=#f3c969>★</color>" : "") + u.gen.name + "  <size=15>" + u.Troops + "</size>";
                barFill.anchorMax = new Vector2(Mathf.Clamp01(u.Troops / (float)Mathf.Max(1, u.gen.MaxTroops)), 1);
                info.gameObject.SetActive(u.alive || holdLabel);
            }
            if (u.confused > 0 && confuseRing == null)
            {
                confuseMat = Art.NewUnlit(new Color(0.75f, 0.5f, 1f, 0.9f), Art.Ring);
                confuseRing = Art.MakeObject("Confused", view.UnitDisc, confuseMat, transform, false);
                confuseRing.transform.localPosition = Vector3.up * 1.6f;
                confuseRing.transform.localScale = Vector3.one * 0.6f;
            }
            if (u.confused <= 0 && confuseRing != null) { Destroy(confuseRing); Destroy(confuseMat); confuseRing = null; confuseMat = null; }
            SpRefresh();          // 必杀技的限时加成（光环 + 名牌小字）
        }

        public void Face(Vector3 dir) { if (dir.sqrMagnitude > 0.001f) facing = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; }
        public void Rest() { }
        public void Flash() { flash = 1; }
        public void FlashHit() { flash = 1; }
        public void Burn() { heat = 1; }
        // 必杀技的着色光（金光、护盾等）：1 → 0 渐退，盖过受击闪白
        public void Tint(Color c, float k = 1) { spTintColor = c; spTint = k; }
        public void SetHidden(bool on)
        {
            hidden = on;
            if (mr != null) mr.enabled = !on;
            if (confuseRing != null) confuseRing.SetActive(!on);
            if (buffRing != null) buffRing.SetActive(!on);
        }

        // ---- 限时加成：脚下彩色光环 + 名牌右上角的小字（守 / 攻 / 毒 / 疾）
        static readonly string[] BuffKind = { "def", "atk", "dot", "move" };
        static string BuffChar(string k) { return k == "def" ? "守" : k == "atk" ? "攻" : k == "dot" ? "毒" : "疾"; }
        static Color BuffColor(string k) { return Art.Hex(k == "def" ? "#8cc8ff" : k == "atk" ? "#ffcf5a" : k == "dot" ? "#9be06a" : "#7affd9"); }
        static List<string> BuffKinds(BUnit u)
        {
            var o = new List<string>();
            if (u.mods == null) return o;
            foreach (var m in u.mods)
            {
                if (m.dot.HasValue && m.dot.Value > 0 && !o.Contains("dot")) o.Add("dot");
                if (((m.def.HasValue && m.def.Value > 1) || (m.counter.HasValue && m.counter.Value > 1)) && !o.Contains("def")) o.Add("def");
                if (m.atk.HasValue && m.atk.Value > 1 && !o.Contains("atk")) o.Add("atk");
                if (m.move.HasValue && m.move.Value > 0 && !o.Contains("move")) o.Add("move");
            }
            return o;
        }
        void SpRefresh()
        {
            var kinds = u.alive ? BuffKinds(u) : new List<string>();
            string key = string.Join(",", kinds.ToArray());
            if (key == spKey) return;
            spKey = key;
            if (info != null)
            {
                if (chips == null)
                {
                    chips = UIKit.NewRect("Buffs", info);
                    chips.anchorMin = chips.anchorMax = new Vector2(1, 1); chips.pivot = new Vector2(1, 0.5f);
                    chips.anchoredPosition = new Vector2(8, 2); chips.sizeDelta = new Vector2(80, 18);
                }
                for (int i = chips.childCount - 1; i >= 0; i--) Destroy(chips.GetChild(i).gameObject);
                for (int i = 0; i < kinds.Count; i++)
                {
                    var chip = UIKit.Img(chips, UIKit.RR, BuffColor(kinds[i]), "Chip");
                    var rt = chip.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f);
                    rt.anchoredPosition = new Vector2(-(kinds.Count - 1 - i) * 20, 0); rt.sizeDelta = new Vector2(18, 18);
                    var sh = chip.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.55f); sh.effectDistance = new Vector2(1, -1);
                    var t = UIKit.Label(rt, BuffChar(kinds[i]), 13, new Color(0.08f, 0.07f, 0.1f), TextAnchor.MiddleCenter);
                    t.fontStyle = FontStyle.Bold;
                    UIKit.Stretch(t.rectTransform);
                }
                chips.gameObject.SetActive(kinds.Count > 0);
            }
            if (buffRing != null) { Destroy(buffRing); Destroy(buffMat); buffRing = null; buffMat = null; }
            if (kinds.Count > 0)
            {
                var c = BuffColor(kinds[0]); c.a = 0.85f;
                buffMat = Art.NewUnlit(c, Art.Ring);
                buffMat.renderQueue = 3003;
                buffRing = Art.MakeObject("Buff", view.UnitDisc, buffMat, transform, false);
                buffRing.transform.localPosition = Vector3.up * 0.07f;
                buffRing.transform.localScale = Vector3.one * 1.05f;
                if (hidden) buffRing.SetActive(false);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime, time = Time.time;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, facing, 0), dt * 10);
            if (flash > 0)
            {
                flash = Mathf.Max(0, flash - dt * 4);
                transform.localPosition += new Vector3(Mathf.Sin(time * 80) * 0.03f * flash, 0, 0);
            }
            // 受击闪白、火计灼烧的橙色、必杀技的着色光合成到同一个 _Flash / _FlashColor 上
            float burn = heat > 0 ? Mathf.SmoothStep(0, 1, heat) * 0.55f * (0.86f + 0.14f * Mathf.Sin(time * 26 + heatSeed)) : 0;
            if (heat > 0) heat = Mathf.Max(0, heat - dt * 0.42f);
            Color fc = burn > 0 ? Color.Lerp(HeatTint, Color.white, flash / (flash + burn)) : Color.white;
            float f = Mathf.Max(flash, burn);
            if (spTint > 0)
            {
                spTint = Mathf.Max(0, spTint - dt * 1.1f);
                float k = Mathf.SmoothStep(0, 1, spTint) * 0.6f;
                if (k > f) { fc = spTintColor; f = k; }
            }
            if (mat != null) { mat.SetFloat("_Flash", f); mat.SetColor("_FlashColor", fc); }
            if (confuseRing != null) confuseRing.transform.Rotate(0, 200 * dt, 0);
            if (buffRing != null)
            {
                buffRing.transform.Rotate(0, 60 * dt, 0);
                float s = 1.05f * (1 + Mathf.Sin(time * 3 + heatSeed) * 0.05f);
                buffRing.transform.localScale = new Vector3(s, 1, s);
            }
            if (info != null)
            {
                float a = (u.alive || holdLabel) && !hidden ? (u.acted && u.side == view.M.side ? 0.55f : 1f) : 0f;
                if (view.SpLabelsDim) a *= 0.16f;
                info.GetComponent<WorldFollow>().alphaMul = a;
            }
        }
        void OnDestroy()
        {
            if (info != null) Destroy(info.gameObject);
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            if (mat != null) Destroy(mat);
            if (confuseMat != null) Destroy(confuseMat);
            if (buffMat != null) Destroy(buffMat);
        }
        void OnDisable() { if (info != null) info.gameObject.SetActive(false); }
    }
}
