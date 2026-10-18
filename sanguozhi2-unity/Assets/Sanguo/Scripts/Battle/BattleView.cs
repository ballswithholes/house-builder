// ==========================================================================
// 三国志-霸王的世界 · 战场三维表现（网页版 js/battle-view.js 的移植：BattleView、UnitVisual、FloatUp）
//
// 棋盘建在 Unity 原点 (400, 0, 0)，每格 T = 2（格子 (x, y) 的中心 = 原点 + (x·T + T/2, h, y·T + T/2)）。
// 第二版：SetCulture(culture)（城墙、城门、本阵与地表按战场城池的文化）、SpecialFx(caster, targets, fx, color[, info])
//   → IEnumerator（必杀技特效，见 BattleViewFx.cs）、UnitVisual.Tint / Burn / SetHidden、限时加成的光环与名牌小字、燃烧中的格子。
// 第三版（DESIGN-V3 §1、§2）：战场 18 × 13；地形取自城池周边（BattleTerrain 写入 model.terrain，没有时按地块类型合成）；
//   平滑的连续地形网格（Sanguo/BattleTerrain 着色器：按群落 / 坡度 / 高度混合，叠加淡格线与棋盘边框；棋盘外延伸 16 × 14 格的
//   真实远景，再外为陆地 / 海面的裙边）、水面、实例化的圆润树木 / 石块 / 草丛 / 点缀（挡住部队的树淡去）、道路、桥、
//   渡海登陆的船；平滑细致的城墙、城门、角楼、本阵（各文化样式，砖石材质）。几何全部由 BattleBoard（BattleLook.cs）生成。
//   部队为 Figure 的骑马主将（中精度）+ FigureCrowd 的实例化士兵（低精度、势力色），有待机、行军、攻击、受击、必杀、溃散动作。
// 新增接口（UNITY-V3）：
//   bool Ready                     人物模型分片预载完成（BattleController 开第一回合前最多等 4 秒）
//   string CultureOf(side)          双方的文化（守方 = 目标城池，攻方 = 出发城池；没有时 = 战场文化）
//   float GroundY(x, z)             地表高度（Unity 世界坐标；水下取涉水高度，桥上取桥面）
//   BattleBoard.BoardStats Stats    构建耗时（含分段）、三角形数、树石草与点缀的数量、水面、船、裙边
//   UnitVisual.Act(kind)            attack | hit | special | rout；UnitVisual.GhostMesh() 当前姿势的烘焙网格（残影特效）
// 原有接口与特效不变（BattleController 用的 Create / Tile / TopH / Highlight / SetCursor / TileFromScreen / MoveUnit / Lunge /
//   Hit / Rout / FloatText / Fire/Rock/MagicFx / SpecialFx / UnitFromLabel …）。
// 光照：场景的太阳与三色环境光由 Game 设置（UNITY-V3 / reports-b fix-figure：Unity 强度 = 网页版强度 / π）；本文件只有特效的点光源
//   （BattleViewFx：同一约定，另按 Unity 内置点光的衰减补偿）。
// ==========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Sanguo
{
    public partial class BattleView : MonoBehaviour
    {
        public static readonly Vector3 Origin = BattleBoard.Origin;
        public const float T = BattleBoard.T;
        static readonly Color[] SIDE_BAR = { new Color(0.45f, 0.75f, 1f), new Color(1f, 0.5f, 0.42f) };   // 攻方 #73bfff、守方 #ff806b
        public BattleModel M;
        Transform root;
        readonly Dictionary<BUnit, UnitVisual> vis = new Dictionary<BUnit, UnitVisual>();
        readonly List<GameObject> highlights = new List<GameObject>();
        GameObject cursor, cursorGlow; Material cursorGlowMat;
        public BattleLook.Look Look { get; private set; }
        public BattleBoard Board { get; private set; }
        public BattleBoard.BoardStats Stats { get { return Board != null ? Board.Stats : null; } }
        public bool Ready { get; private set; }
        public FigureCrowd Crowd { get; private set; }
        Transform field;                                            // 地形、水面、城池、船（SetCulture 时重建）
        readonly List<Mesh> fieldMeshes = new List<Mesh>();
        readonly List<Material> fieldMats = new List<Material>();
        readonly List<Mesh> ownMeshes = new List<Mesh>();
        readonly List<Material> ownMats = new List<Material>();
        Mesh unitDisc;                                              // 半径 1 的贴图方片（光环、范围格）
        public Mesh UnitDisc { get { if (unitDisc == null) { unitDisc = MapView.FlatDisc(1f); ownMeshes.Add(unitDisc); } return unitDisc; } }
        internal Material[] sideRing;                               // 部队脚下的阵营光环（攻方蓝、守方红，与兵力条同色）
        public bool SpLabelsDim;                                    // 必杀技特效期间部队名牌淡出（不透明度 0.16）
        float time;
        Color atkCol, defCol;

        public Vector3 BoardCenter { get { return Origin + new Vector3(M.W * T / 2, 0, M.H * T / 2); } }
        // 格子中心的站立高度（河川为涉水高度、桥为桥面、城墙为墙顶）
        public float TopH(int x, int y) { return Board != null ? Board.tileY[y * M.W + x] : 0.08f; }
        public Vector3 Tile(int x, int y) { return Board != null ? Board.Tile(x, y) : Origin + new Vector3(x * T + T / 2, 0.08f, y * T + T / 2); }
        // 地表高度（Unity 世界坐标 x, z → y）；水面以下的地方取涉水高度（部队、士兵站在水里而不是河底）
        public float GroundY(float x, float z) { return Board != null ? (float)Board.GroundY(x, z) : 0.08f; }
        // 双方的文化：守方 = 目标城池，攻方 = 出发城池；没有出发城时为战场的文化
        public string CultureOf(int side)
        {
            var setup = M != null ? M.setup : null;
            string c = null;
            try
            {
                var city = setup == null ? null : side == 1 ? setup.target : setup.src;
                if (city != null) c = CultureArt.CultureOfCity(city);
            }
            catch (Exception) { c = null; }
            return !string.IsNullOrEmpty(c) ? c : Look != null && !string.IsNullOrEmpty(Look.culture) ? Look.culture : "han";
        }

        public static BattleView Create(BattleModel m)
        {
            var go = new GameObject("BattleView");
            var v = go.AddComponent<BattleView>();
            v.M = m;
            v.Build();
            go.AddComponent<UnitLabelLayout>().view = v;    // 名牌避让（在 WorldFollow 定位之后运行）
            return v;
        }

        // ---------------------------------------------------------- 构建 --
        Color FactionColor(int idx)
        {
            var g = GameState.Current;
            return g != null && idx >= 0 && idx < g.factions.Count ? g.factions[idx].Col : new Color(0.5f, 0.5f, 0.5f);
        }
        void Build()
        {
            root = transform;
            atkCol = FactionColor(M.setup.attacker); defCol = FactionColor(M.setup.defender);
            Look = BattleLook.Of(BattleLook.CultureOf(M.setup));
            BuildField();
            // 光标（金色光圈，缓慢旋转）+ 底下一层呼吸的叠加光
            var cursorDisc = MapView.FlatDisc(T * 0.62f); ownMeshes.Add(cursorDisc);
            var cursorMat = Art.NewUnlit(new Color(1, 0.85f, 0.35f, 1), Art.Ring); cursorMat.renderQueue = 3004; ownMats.Add(cursorMat);
            cursor = Art.MakeObject("Cursor", cursorDisc, cursorMat, root, false);
            cursor.SetActive(false);
            cursorGlowMat = Art.NewAdditive(new Color(1, 0.78f, 0.35f, 0.32f)); cursorGlowMat.renderQueue = 3003; ownMats.Add(cursorGlowMat);
            cursorGlow = Art.MakeObject("CursorGlow", cursorDisc, cursorGlowMat, root, false);
            cursorGlow.SetActive(false);
            sideRing = new Material[2];
            for (int i = 0; i < 2; i++) { var c = SIDE_BAR[i]; c.a = 0.7f; sideRing[i] = Art.NewUnlit(c, Art.Ring); sideRing[i].renderQueue = 3002; ownMats.Add(sideRing[i]); }
            // 部队：人物模型（Figure）的士兵为同一个实例化士兵群
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Crowd = null;
            try { Crowd = new FigureCrowd(new FigCrowdOptions { lod = "low", parent = root }); Crowd.Root.name = "Soldiers"; }
            catch (Exception e) { Debug.LogException(e); Crowd = null; }
            Board.Stats.phases["fx"] = Math.Round(sw.Elapsed.TotalMilliseconds); sw.Reset(); sw.Start();
            foreach (var u in M.units) CreateUnit(u, u.side == 0 ? atkCol : defCol);
            Board.Stats.phases["units"] = Math.Round(sw.Elapsed.TotalMilliseconds);
            // 人物模型：分片预载（面部贴图、模板、士兵群动作烘焙）后再建，不卡住开场；Ready 在部队模型就绪后为 true
            Ready = false;
            if (Crowd != null) StartCoroutine(PrepareFigures()); else Ready = true;
        }

        // 战场的文化外观：城墙、城门、本阵样式、村舍、树种与地表色调。构建时已按战场城池的文化设定，
        // 这里可以改用别的文化重建（只重建地形与装饰，部队与特效不受影响）
        public void SetCulture(string culture)
        {
            var lk = BattleLook.Of(culture);
            if (Look != null && field != null && lk.culture == Look.culture) return;
            Look = lk;
            if (M != null && root != null) BuildField();
        }

        void FreeField()
        {
            if (field != null) Destroy(field.gameObject);
            foreach (var m in fieldMeshes) if (m != null) Destroy(m);
            foreach (var m in fieldMats) if (m != null) Destroy(m);
            fieldMeshes.Clear(); fieldMats.Clear();
            propDraws.Clear();
        }
        void BuildField()
        {
            FreeField();
            field = new GameObject("Field").transform;
            field.SetParent(root, false);
            Board = new BattleBoard(M, Look, atkCol, defCol);
            var B = Board;
            // 平滑地形网格：顶点色 = 群落底色，uv2 = aMat，uv3 = aBt
            var tm = new Mesh { name = "Board" };
            tm.indexFormat = B.tPos.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            tm.vertices = B.tPos; tm.normals = B.tNrm; tm.colors = B.tCol;
            tm.SetUVs(1, new List<Vector4>(B.tMat)); tm.SetUVs(2, new List<Vector4>(B.tBt));
            tm.triangles = B.tTri;
            tm.RecalculateBounds();
            fieldMeshes.Add(tm);
            var tmat = new Material(Art.LoadShader("BattleTerrain")) { name = "BattleTerrain" };
            tmat.SetTexture("_DetA", Art.TerrainDetailA); tmat.SetTexture("_DetB", Art.TerrainDetailB);
            tmat.SetVector("_Board", new Vector4(Origin.x, Origin.z, M.W, M.H));
            tmat.SetFloat("_GridK", 0.16f);
            fieldMats.Add(tmat);
            var board = Art.MakeObject("Board", tm, tmat, field, false);
            board.GetComponent<MeshRenderer>().receiveShadows = true;
            // 裙边：网格外的一圈大平面（陆地用同一地形材质、外缘平均色；海面用水面材质）
            {
                var sm = new Mesh { name = "Skirt" };
                sm.vertices = B.sPos; sm.colors = B.sCol; sm.normals = B.sNrm;
                if (B.sMat != null) { sm.SetUVs(1, new List<Vector4>(B.sMat)); sm.SetUVs(2, new List<Vector4>(B.sBt)); }   // 陆地裙边：地形材质的 aMat / aBt
                sm.triangles = B.sTri;
                sm.RecalculateBounds();
                fieldMeshes.Add(sm);
                var skirt = Art.MakeObject("Skirt", sm, B.seaSkirt ? WaterMat : tmat, field, false);
                skirt.GetComponent<MeshRenderer>().receiveShadows = !B.seaSkirt;
            }
            // 水面
            if (B.wTri.Count > 0)
            {
                var wm = new Mesh { name = "Water" };
                wm.SetVertices(B.wPos); wm.SetColors(B.wCol);
                var nrm = new List<Vector3>(B.wPos.Count); for (int i = 0; i < B.wPos.Count; i++) nrm.Add(Vector3.up);
                wm.SetNormals(nrm);
                wm.SetTriangles(B.wTri, 0);
                wm.RecalculateBounds();
                fieldMeshes.Add(wm);
                var water = Art.MakeObject("Water", wm, WaterMat, field, false);
                water.GetComponent<MeshRenderer>().receiveShadows = false;
            }
            // 城池（城墙、城门、角楼、本阵、旗帜）与桥：砖石材质
            var fm = B.Fort.ToMesh("Fort"); fieldMeshes.Add(fm);
            Art.MakeObject("Fort", fm, Art.MasonryMaterial(36, 90), field, true).GetComponent<MeshRenderer>().receiveShadows = true;
            if (B.Boats != null)
            {
                var bm = B.Boats.ToMesh("Boats"); fieldMeshes.Add(bm);
                Art.MakeObject("Boats", bm, Art.LowPoly, field, true).GetComponent<MeshRenderer>().receiveShadows = true;
            }
            // 树、石、草与点缀：实例化绘制（每帧 Graphics.DrawMeshInstanced，见 DrawProps）
            foreach (var pb in B.Props) propDraws.Add(new PropDraw(pb));
            // 各格高度变了：高亮网格与光环重算
            foreach (var g in hlGeos.Values) if (g != null) Destroy(g);
            hlGeos.Clear();
            foreach (var v in vis.Values) if (v != null) v.ResetRing();
        }

        // ---- 实例化的树、石、草（模板网格按种类缓存，各场战斗共用；材质共享）
        static readonly Dictionary<string, Mesh> propMeshes = new Dictionary<string, Mesh>();
        static Material propMat, propFadeMat;
        static Mesh PropMesh(string kind)
        {
            Mesh m;
            if (propMeshes.TryGetValue(kind, out m) && m != null) return m;
            m = BattleBoard.PropTemplate(kind).ToMesh("Prop_" + kind);
            propMeshes[kind] = m;
            return m;
        }
        static Material PropMat { get { if (propMat == null) propMat = new Material(Art.LoadShader("BattleProp")) { name = "BattleProp", enableInstancing = true }; return propMat; } }
        // 水面（河、湖、海与海面裙边）：网页版的 Gfx.water()（Sanguo/BattleWater），renderOrder 1 → 队列 3001（在高亮、光环之前）
        static Material waterMat;
        static Material WaterMat
        {
            get
            {
                if (waterMat == null)
                {
                    waterMat = new Material(Art.LoadShader("BattleWater")) { name = "BattleWater" };
                    waterMat.SetTexture("_Ripple", Art.Ripple);
                    waterMat.renderQueue = 3001;
                }
                return waterMat;
            }
        }
        static Material PropFadeMat { get { if (propFadeMat == null) propFadeMat = new Material(Art.LoadShader("BattlePropFade")) { name = "BattlePropFade", enableInstancing = true }; return propFadeMat; } }
        const int Chunk = 1023;
        sealed class PropDraw
        {
            public readonly BattleBoard.PropBatch b; public readonly Mesh mesh;
            public readonly Matrix4x4[] mats; public readonly Vector4[] cols; public readonly float[] fade;
            public bool dirty = true;
            // 每次绘制最多 1023 个实例；分为不透明（未淡去）与淡去中的两组，各自按 1023 分段
            public readonly List<Matrix4x4[]> opM = new List<Matrix4x4[]>(), faM = new List<Matrix4x4[]>();
            public readonly List<MaterialPropertyBlock> opB = new List<MaterialPropertyBlock>(), faB = new List<MaterialPropertyBlock>();
            public readonly List<int> opN = new List<int>(), faN = new List<int>();
            readonly List<int> opIdx = new List<int>(), faIdx = new List<int>();
            public PropDraw(BattleBoard.PropBatch pb)
            {
                b = pb; mesh = PropMesh(pb.kind);
                int n = pb.items.Count;
                mats = new Matrix4x4[n]; cols = new Vector4[n]; fade = new float[n];
                for (int k = 0; k < n; k++) { mats[k] = pb.MatrixOf(k); var e = pb.items[k]; cols[k] = new Vector4(e.r, e.g, e.b, 1); }
            }
            // 按淡出状态重新分组（只在有变化时）
            public void Rebuild()
            {
                dirty = false;
                opIdx.Clear(); faIdx.Clear();
                for (int k = 0; k < mats.Length; k++) { if (b.fade && fade[k] > 0.004f) faIdx.Add(k); else opIdx.Add(k); }
                Pack(opIdx, opM, opB, opN, false);
                Pack(faIdx, faM, faB, faN, true);
            }
            void Pack(List<int> idx, List<Matrix4x4[]> M, List<MaterialPropertyBlock> B, List<int> N, bool withFade)
            {
                N.Clear();
                for (int c0 = 0; c0 * Chunk < idx.Count; c0++)
                {
                    int cnt = Math.Min(Chunk, idx.Count - c0 * Chunk);
                    while (M.Count <= c0) { M.Add(new Matrix4x4[Chunk]); B.Add(new MaterialPropertyBlock()); }
                    // 数组一律按 1023 分配（Unity 的属性数组长度在首次设置时固定）
                    var cv = new Vector4[Chunk]; var fv = new float[Chunk];
                    for (int i = 0; i < cnt; i++) { int k = idx[c0 * Chunk + i]; M[c0][i] = mats[k]; cv[i] = cols[k]; fv[i] = fade[k]; }
                    B[c0].Clear();
                    B[c0].SetVectorArray("_InstColor", cv);
                    if (withFade) B[c0].SetFloatArray("_Fade", fv);
                    N.Add(cnt);
                }
            }
        }
        readonly List<PropDraw> propDraws = new List<PropDraw>();
        static MaterialPropertyBlock oneBlock;
        void DrawProps()
        {
            int layer = gameObject.layer;
            bool inst = SystemInfo.supportsInstancing;
            foreach (var d in propDraws)
            {
                if (!inst)
                {
                    // 不支持实例化的设备：逐个绘制（同一材质，实例色与淡出用 MaterialPropertyBlock；慢，但画面相同）
                    if (oneBlock == null) oneBlock = new MaterialPropertyBlock();
                    var shd = d.b.castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    for (int k = 0; k < d.mats.Length; k++)
                    {
                        bool fd = d.b.fade && d.fade[k] > 0.004f;
                        oneBlock.Clear(); oneBlock.SetVector("_InstColor", d.cols[k]); if (fd) oneBlock.SetFloat("_Fade", d.fade[k]);
                        Graphics.DrawMesh(d.mesh, d.mats[k], fd ? PropFadeMat : PropMat, layer, null, 0, oneBlock, shd, d.b.receiveShadow && !fd);
                    }
                    continue;
                }
                if (d.dirty) d.Rebuild();
                var sh = d.b.castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                for (int c = 0; c < d.opN.Count; c++) if (d.opN[c] > 0) Graphics.DrawMeshInstanced(d.mesh, 0, PropMat, d.opM[c], d.opN[c], d.opB[c], sh, d.b.receiveShadow, layer);
                for (int c = 0; c < d.faN.Count; c++) if (d.faN[c] > 0) Graphics.DrawMeshInstanced(d.mesh, 0, PropFadeMat, d.faM[c], d.faN[c], d.faB[c], sh, false, layer);
            }
        }
        // 挡在部队与镜头之间的树淡去（地面投影上：部队往镜头方向、树高 / tan(俯角) 以内，横向约 0.8 格以内）
        void UpdatePropFade(float dt)
        {
            var B = Board; var cam = FxCam();
            if (B == null || B.fadeItems.Count == 0 || cam == null) return;
            foreach (var it in B.fadeItems) it.t = 0;
            var cp = cam.transform.position;
            foreach (var v in vis.Values)
            {
                if (v == null || !v.gameObject.activeSelf || v.hidden || (v.u != null && !v.u.alive)) continue;
                var p = v.transform.position;
                double cx = cp.x - p.x, cz = cp.z - p.z;
                double l = Math.Sqrt(cx * cx + cz * cz); if (l == 0) l = 1; cx /= l; cz /= l;
                // 镜头俯角越大，挡得住部队的树离部队越近：树高 / tan(俯角)
                double tanEl = Math.Max(0.3, (cp.y - p.y) / l);
                int ti = BattleBoard.JsRound((p.x - Origin.x) / T - 0.5), tj = BattleBoard.JsRound((p.z - Origin.z) / T - 0.5);
                for (int dj = -3; dj <= 3; dj++)
                    for (int di = -3; di <= 3; di++)
                    {
                        List<BattleBoard.FadeItem> L;
                        if (!B.fadeByTile.TryGetValue(BattleBoard.TileKey(ti + di, tj + dj), out L)) continue;
                        foreach (var it in L)
                        {
                            double ox = it.x - p.x, oz = it.z - p.z, along = ox * cx + oz * cz, perp = Math.Abs(ox * cz - oz * cx);
                            if (along < -0.3 * T || along > (2.3 * it.s + 0.5) / tanEl + 0.3 * T) continue;
                            double w = 0.75 * T * Math.Max(0.8, it.s) + 0.25;
                            if (perp < w) it.t = Mathf.Max(it.t, (float)Math.Min(1, Math.Max(0, (w - perp) / (0.35 * T))));
                        }
                    }
            }
            float k = Mathf.Min(1, (dt > 0 ? dt : 0.016f) * 7);
            foreach (var it in B.fadeItems)
            {
                if (Mathf.Abs(it.t - it.cur) < 0.004f) continue;
                it.cur += (it.t - it.cur) * k;
                if (Mathf.Abs(it.t - it.cur) < 0.01f) it.cur = it.t;
                if (it.batch < propDraws.Count) { var d = propDraws[it.batch]; d.fade[it.k] = it.cur; d.dirty = true; }
            }
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

        // 人物模型：分片预载（面部贴图、模板、士兵群动作烘焙）后建立各部队的主将与士兵；之后等两帧（着色器在首次绘制时编译）
        IEnumerator PrepareFigures()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var list = vis.Values.Where(v => v != null).ToList();
            var known = Figure.Cultures;
            Func<string, string> fc = c => c != null && known.Contains(c) ? c : "han";
            var cults = list.Select(v => fc(v.culture)).Distinct().ToArray();
            var specs = new List<FigCrowdSpec>();
            foreach (var v in list)
                foreach (var q in BattleBoard.SoldierSlots(v.culture, 6))
                    for (int sd = 0; sd <= 1; sd++)
                    {
                        string cu = fc(v.culture);
                        if (!specs.Any(e => e.type == q.type && e.culture == cu && e.seed == sd && e.mounted == q.mounted))
                            specs.Add(new FigCrowdSpec { type = q.type, culture = cu, seed = sd, mounted = q.mounted });
                    }
            PortraitGen[] gens = null;
            try { gens = list.Select(v => Portrait.Describe(v.u.gen)).ToArray(); } catch (Exception e) { Debug.LogException(e); }
            IEnumerator pre = null;
            try { pre = Figure.Preload(new FigPreload { cultures = cults, types = new[] { "spear" }, generals = gens, generalLod = "mid", crowd = specs.ToArray() }); }
            catch (Exception e) { Debug.LogException(e); }
            if (pre != null) yield return Co.Guard(pre, e => Debug.LogException(e));
            if (this == null) yield break;
            double t1 = sw.Elapsed.TotalMilliseconds;
            foreach (var v in vis.Values) if (v != null) v.FiguresReady();
            if (Board != null)
            {
                Board.Stats.phases["preload"] = Math.Round(t1);
                Board.Stats.phases["figures"] = Math.Round(sw.Elapsed.TotalMilliseconds - t1);
            }
            // 网页版先编译着色器再渲染两帧才兑现 ready：这里等两帧（Unity 在首次绘制时编译）
            double t2 = sw.Elapsed.TotalMilliseconds;
            yield return null;
            yield return null;
            if (Board != null) Board.Stats.phases["warm"] = Math.Round(sw.Elapsed.TotalMilliseconds - t2);
            Ready = true;
        }

        // ---------------------------------------------------------- 高亮 --
        public void ClearHighlights() { foreach (var h in highlights) if (h != null) Destroy(h); highlights.Clear(); }
        readonly Dictionary<Color, Material> hlMats = new Dictionary<Color, Material>();
        readonly Dictionary<int, Mesh> hlGeos = new Dictionary<int, Mesh>();
        readonly Dictionary<Material, float> hlBase = new Dictionary<Material, float>();
        // 贴合地形的高亮格（每格一个 5×5 的小网格，格子中心为原点，缓存）
        internal Mesh HlGeoAt(int x, int y)
        {
            int key = y * 1000 + x;
            Mesh g;
            if (hlGeos.TryGetValue(key, out g) && g != null) return g;
            const int n = 4;
            var pos = new Vector3[(n + 1) * (n + 1)]; var uv = new Vector2[pos.Length]; var col = new Color[pos.Length]; var nrm = new Vector3[pos.Length];
            var c = Tile(x, y);
            bool onWater = c.y <= BattleBoard.WADE_Y + 0.001;
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float lx = -1 + 2f * i / n, lz = -1 + 2f * j / n;
                    float wx = c.x + lx * T * 0.5f, wz = c.z + lz * T * 0.5f;
                    float gy = onWater ? (float)BattleBoard.WATER_Y : GroundY(wx, wz);
                    int o = j * (n + 1) + i;
                    pos[o] = new Vector3(lx * T * 0.5f, Mathf.Max(gy, (float)BattleBoard.WATER_Y) - c.y + 0.07f, lz * T * 0.5f);
                    uv[o] = new Vector2((float)i / n, (float)j / n); col[o] = Color.white; nrm[o] = Vector3.up;
                }
            var idx = new List<int>();
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, cc = a + n + 1, d = cc + 1;
                    idx.Add(a); idx.Add(d); idx.Add(b); idx.Add(a); idx.Add(cc); idx.Add(d);   // 网页版 (a, b, d) (a, d, c) 换手系
                }
            g = new Mesh { name = "HL" };
            g.vertices = pos; g.uv = uv; g.colors = col; g.normals = nrm; g.SetTriangles(idx, 0);
            g.RecalculateBounds();
            hlGeos[key] = g;
            return g;
        }
        public void Highlight(IEnumerable<Vector2Int> tiles, Color c)
        {
            if (tiles == null) return;
            Material mat;
            if (!hlMats.TryGetValue(c, out mat)) { mat = Art.NewUnlit(c, TileTex); mat.renderQueue = 3002; hlMats[c] = mat; hlBase[mat] = c.a; ownMats.Add(mat); }
            foreach (var t in tiles)
            {
                if (!M.InBounds(t.x, t.y)) continue;
                var go = Art.MakeObject("HL", HlGeoAt(t.x, t.y), mat, root, false);
                go.transform.position = Tile(t.x, t.y);
                highlights.Add(go);
            }
        }
        // 格子周围（半径 r 世界单位）的最高地表：光标、光环放在它之上，不被坡面挡住
        internal float RingY(int x, int y, float r)
        {
            var c = Tile(x, y);
            float h = c.y;
            for (int k = 0; k < 8; k++) { float a = k * Mathf.PI / 4; h = Mathf.Max(h, GroundY(c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r)); }
            return h - c.y;
        }

        // ------------------------------------------------------ 名牌避让 --
        // 移植网页版 BattleView._layoutLabels：部队头顶名牌互相遮挡时（窄屏上格距只有约 30px）按优先级排布：
        // 选中部队 > 主将 > 靠近镜头（屏幕上更低）的部队。先放的留在原位；后放的若与已放的重叠，就在小范围内
        // 上移 / 下移 / 左右平移到最近的空位（上移时显示一根引线连回部队）；找不到时（布阵边上挤着 4–5 支部队）再放宽到
        // 上移 2.6 个名牌高、左右各 0.9 个名牌宽；实在放不下则淡化到 0.3 并压到下层。
        // 坐标为 LabelLayer 的本地坐标（画布单位，y 向上）。由 UnitLabelLayout 在 WorldFollow 定位之后每帧调用。
        struct LRect { public float l, r, b, t; public LRect(float l, float b, float r, float t) { this.l = l; this.b = b; this.r = r; this.t = t; } }
        readonly List<UnitVisual> lblItems = new List<UnitVisual>();
        readonly List<UnitVisual> lblZOrder = new List<UnitVisual>(), lblZLast = new List<UnitVisual>();
        readonly List<int> lblSlots = new List<int>();
        readonly List<LRect> lblPlaced = new List<LRect>(), lblNear = new List<LRect>();
        readonly List<float> lblDys = new List<float>(), lblDxs = new List<float>();
        const float LblGap = 6f, LblM = 2f;   // 名牌底与部队锚点的间距（网页版 bottom:.35em）；名牌之间至少留 2
        static readonly Comparison<UnitVisual> LblPriority = (a, b) =>
            a.lblPr != b.lblPr ? b.lblPr.CompareTo(a.lblPr) : a.lblBase.y != b.lblBase.y ? a.lblBase.y.CompareTo(b.lblBase.y) : a.lblOrder.CompareTo(b.lblOrder);
        static readonly Comparison<UnitVisual> LblByZ = (a, b) => a.lblZ != b.lblZ ? a.lblZ.CompareTo(b.lblZ) : a.lblOrder.CompareTo(b.lblOrder);
        static readonly float[,] LblPass = { { 1.6f, 0.42f }, { 2.6f, 0.9f } };
        static bool LblHits(List<LRect> list, float l, float b, float r, float t)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (l < p.r + LblM && r > p.l - LblM && b < p.t + LblM && t > p.b - LblM) return true;
            }
            return false;
        }
        internal void LayoutLabels(float dt)
        {
            var cam = Camera.main;
            var layer = UIKit.LabelLayer;
            if (cam == null || layer == null) return;
            lblItems.Clear();
            int order = 0;
            foreach (var v in vis.Values)
            {
                order++;
                if (v == null || v.Info == null || v.follow == null) continue;
                if (!(v.labelAlpha > 0.001f) || !v.Info.gameObject.activeInHierarchy) { v.lblShown = false; continue; }
                var p = v.labelPos;
                var sp = cam.WorldToScreenPoint(p);
                Vector2 local;
                if (sp.z <= 0 || !(Vector3.Distance(cam.transform.position, p) < v.follow.hideBeyond)
                    || !RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, sp, null, out local)) { v.lblShown = false; continue; }
                // 名牌的底边在锚点（部队上方 2.3）之上 LblGap（网页版 .sg-uinfo 的 bottom:.35em）
                v.follow.offset = new Vector2(0, v.Info.rect.height / 2 + LblGap);
                v.lblBase = local + v.follow.offset;
                v.lblPr = (v.u == CursorUnit ? 2 : 0) + (v.u.commander ? 1 : 0);
                v.lblOrder = order;
                lblItems.Add(v);
            }
            // 优先级高的先放；同级时靠近镜头（屏幕上更低）的先放，后面的部队名牌自然向上让开
            lblItems.Sort(LblPriority);
            lblPlaced.Clear();
            int n = lblItems.Count;
            for (int rank = 0; rank < n; rank++)
            {
                var v = lblItems[rank];
                var sz = v.Info.rect.size;
                float w = sz.x, h = sz.y;
                float l0 = v.lblBase.x - w / 2, r0 = v.lblBase.x + w / 2, b0 = v.lblBase.y - h / 2, t0 = v.lblBase.y + h / 2;
                bool found = false; float bdx = 0, bdy = 0;
                if (!LblHits(lblPlaced, l0, b0, r0, t0)) found = true;
                else
                    for (int pass = 0; pass < 2 && !found; pass++)
                    {
                        float maxUp = h * LblPass[pass, 0] + 4, maxDown = h * 0.45f, maxSide = w * LblPass[pass, 1];
                        // 只看附近的名牌：候选偏移取“贴着它们的边”的位置
                        lblNear.Clear(); lblDys.Clear(); lblDxs.Clear(); lblDys.Add(0); lblDxs.Add(0);
                        foreach (var p in lblPlaced)
                            if (p.l < r0 + maxSide + LblM && p.r > l0 - maxSide - LblM && p.b < t0 + maxUp + LblM && p.t > b0 - maxDown - LblM) lblNear.Add(p);
                        foreach (var p in lblNear)
                        {
                            float up = p.t + LblM - b0, down = t0 - p.b + LblM, left = p.l - LblM - r0, right = p.r + LblM - l0;
                            if (up > 0 && up <= maxUp) lblDys.Add(up);
                            if (down > 0 && down <= maxDown) lblDys.Add(-down);
                            if (left < 0 && -left <= maxSide) lblDxs.Add(left);
                            if (right > 0 && right <= maxSide) lblDxs.Add(right);
                        }
                        // 组合偏移：贴着两个名牌的边（向上再横移）
                        int n0 = lblDys.Count;
                        for (int a = 1; a < n0; a++)
                            for (int b = 1; b < n0; b++)
                            {
                                float sum = lblDys[a] + lblDys[b];
                                if (lblDys[a] > 0 && lblDys[b] > 0 && sum <= maxUp) lblDys.Add(sum);
                            }
                        float bestCost = float.PositiveInfinity;
                        foreach (var dy in lblDys)
                            foreach (var dx in lblDxs)
                            {
                                if (LblHits(lblNear, l0 + dx, b0 + dy, r0 + dx, t0 + dy)) continue;
                                // 上移最自然，下移会挡住自己的部队，平移次之；与上一帧的目标接近者优先（防抖）
                                float cost = (dy >= 0 ? dy : -dy * 1.8f) + Mathf.Abs(dx) * 1.25f
                                    + (Mathf.Abs(dx - v.lblTdx) + Mathf.Abs(dy - v.lblTdy)) * 0.3f;
                                if (cost < bestCost) { bestCost = cost; bdx = dx; bdy = dy; found = true; }
                            }
                    }
                if (found)
                {
                    v.lblRect = lblPlaced.Count;
                    lblPlaced.Add(new LRect(l0 + bdx, b0 + bdy, r0 + bdx, t0 + bdy));
                    v.lblTdx = bdx; v.lblTdy = bdy; v.lblTfade = 1; v.lblZ = -(1 + rank);
                }
                else
                {
                    v.lblRect = -1;
                    v.lblTdx = 0; v.lblTdy = 0; v.lblTfade = 0.3f; v.lblZ = -(1 + n + rank);
                }
            }
            float k = Mathf.Min(1, dt * 14), kf = Mathf.Min(1, dt * 8);
            foreach (var v in lblItems)
            {
                if (!v.lblShown) { v.lblDx = v.lblTdx; v.lblDy = v.lblTdy; v.lblFade = v.lblTfade; v.lblShown = true; }
                else { v.lblDx += (v.lblTdx - v.lblDx) * k; v.lblDy += (v.lblTdy - v.lblDy) * k; v.lblFade += (v.lblTfade - v.lblFade) * kf; }
                v.Info.anchoredPosition = v.lblBase + new Vector2(v.lblDx, v.lblDy);
                // 引线：从名牌底部连回部队锚点；落点若被别的名牌盖住就不画，免得看起来连到了别人的名牌
                float h = v.Info.rect.height;
                float ax = v.lblBase.x, ay = v.lblBase.y - h / 2 - LblGap;
                bool stemOk = v.lblDy > 2;
                if (stemOk)
                    for (int i = 0; i < lblPlaced.Count; i++)
                    {
                        if (i == v.lblRect) continue;
                        var p = lblPlaced[i];
                        if (ax > p.l && ax < p.r && ay < p.t && ay + LblGap > p.b) { stemOk = false; break; }
                    }
                float stemH = stemOk ? Mathf.Round(v.lblDy + LblGap) : 0;
                if (v.lblStem != null)
                {
                    if (stemH != v.lblStemH)
                    {
                        v.lblStemH = stemH;
                        v.lblStem.gameObject.SetActive(stemH > 0);
                        v.lblStem.sizeDelta = new Vector2(2, stemH);
                    }
                    if (stemH > 0)
                    {
                        v.lblStem.anchoredPosition = new Vector2(-v.lblDx, 0);   // 引线留在部队正上方，不随名牌平移
                        float sa = Mathf.Clamp01((v.lblDy - 2) / 6);
                        var lc = v.lblStemImg.color; lc.a = 0.6f * sa; v.lblStemImg.color = lc;
                        var dc = v.lblDotImg.color; dc.a = sa; v.lblDotImg.color = dc;
                    }
                }
                // 边框：选中（网页版 .sg-uinfo.sel）> 主将（.cmd）> 普通
                int style = v.u == CursorUnit ? 2 : v.u.commander ? 1 : 0;
                if (style != v.lblStyle && v.lblOutline != null)
                {
                    v.lblStyle = style;
                    v.lblOutline.color = style == 2 ? new Color(1f, 0.886f, 0.549f, 0.95f) : new Color(0.953f, 0.788f, 0.412f, style == 1 ? 0.85f : 0.42f);
                }
            }
            // 层级：已放好的按优先级在上，放不下的压到最下（只在顺序变化时调整，且只在名牌原有的层级槽位间互换）
            lblZOrder.Clear(); lblZOrder.AddRange(lblItems); lblZOrder.Sort(LblByZ);
            bool same = lblZOrder.Count == lblZLast.Count;
            for (int i = 0; same && i < lblZOrder.Count; i++) same = lblZOrder[i] == lblZLast[i];
            if (!same)
            {
                lblSlots.Clear();
                foreach (var v in lblZOrder) lblSlots.Add(v.Info.GetSiblingIndex());
                lblSlots.Sort();
                for (int i = 0; i < lblZOrder.Count; i++) lblZOrder[i].Info.SetSiblingIndex(lblSlots[i]);
                lblZLast.Clear(); lblZLast.AddRange(lblZOrder);
            }
        }

        // 屏幕点落在哪支部队的名牌上（= 网页版 BattleController.unitFromLabel）：按名牌当前层级从上往下找，
        // 只认清晰显示的名牌（不透明度 ≥ 0.45）——避让失败而淡化压到下层的名牌不拦截点击。没有命中返回 null
        public BUnit UnitFromLabel(Vector2 screen)
        {
            BUnit best = null; int bestIdx = int.MinValue;
            foreach (var v in vis.Values)
            {
                if (v == null || v.Info == null || !v.lblShown || v.hidden) continue;
                var u = v.u;
                if (u == null || !u.alive || M.UnitAt(u.x, u.y) != u) continue;
                if (!(v.labelAlpha * v.lblFade >= 0.45f) || !v.Info.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(v.Info, screen, null)) continue;
                int si = v.Info.GetSiblingIndex();
                if (si > bestIdx) { bestIdx = si; best = u; }
            }
            return best;
        }

        public BUnit CursorUnit { get; private set; }               // 光标所在（选中）的部队：名牌优先级最高、高亮边框
        public void SetCursor(BUnit u)
        {
            CursorUnit = u;
            bool on = u != null;
            cursor.SetActive(on);
            cursorGlow.SetActive(on);
            if (on)
            {
                var p = Tile(u.x, u.y);
                p.y += 0.08f + Mathf.Min(0.5f, RingY(u.x, u.y, T * 0.55f));
                cursor.transform.position = p;
                cursorGlow.transform.position = p - new Vector3(0, 0.01f, 0);
            }
        }
        void Update()
        {
            float dt = Mathf.Min(Mathf.Max(0, Time.deltaTime), 0.1f);
            time += dt;
            if (cursor != null && cursor.activeSelf)
            {
                cursor.transform.Rotate(0, 40 * dt, 0);   // 网页版 rotation.y −= 40°·dt（three 与 Unity 的偏航相反）
                float s = 1 + Mathf.Sin(time * 4) * 0.035f;
                cursorGlow.transform.localScale = new Vector3(s, 1, s);
                var gc = cursorGlowMat.color; gc.a = 0.26f + Mathf.Sin(time * 4) * 0.08f; cursorGlowMat.color = gc;
            }
            // 高亮轻微呼吸
            if (highlights.Count > 0)
            {
                float k = 0.88f + Mathf.Sin(time * 3.2f) * 0.12f;
                foreach (var kv in hlBase) if (kv.Key != null) { var c = kv.Key.color; c.a = kv.Value * k; kv.Key.color = c; }
            }
            if (Crowd != null) Crowd.SetTime(time);
            foreach (var v in vis.Values) if (v != null) v.Tick(dt, time);
            UpdatePropFade(dt);
            SpUpdate(dt);   // 粒子、发射器、光源、燃烧中的格子、必杀技
            DrawProps();
        }

        // 点选用的表面：地表、水面（点到的是水面而不是河底）、城墙格的墙顶
        float PickY(float wx, float wz)
        {
            float g = Mathf.Max(GroundY(wx, wz), (float)BattleBoard.WATER_Y);
            int xi = BattleBoard.JsRound((wx - Origin.x) / T - 0.5), yi = BattleBoard.JsRound((wz - Origin.z) / T - 0.5);
            if (Board != null && M.InBounds(xi, yi) && M.map[xi, yi] == Terrain.Wall) return Mathf.Max(g, Board.tileY[yi * M.W + xi]);
            return g;
        }
        // 屏幕点 → 格子：沿视线步进到地表（起伏的地形上点到的就是看到的那一格）
        public bool TileFromScreen(Camera cam, Vector2 screen, out Vector2Int tile)
        {
            tile = default(Vector2Int);
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(screen);
            Vector3 o = ray.origin, d = ray.direction;
            if (d.y > -1e-4f) return false;
            const float top = 5.5f, step = 0.35f;
            float t0 = Mathf.Max(0, (o.y - top) / -d.y), t1 = (o.y - ((float)BattleBoard.WADE_Y - 0.5f)) / -d.y;
            float prev = t0; bool hit = false; Vector3 hp = Vector3.zero;
            for (float t = t0; t <= t1; t += step)
            {
                var p = o + d * t;
                if (p.y <= PickY(p.x, p.z))
                {
                    float a = prev, b = t;
                    for (int k = 0; k < 10; k++) { float mid = (a + b) / 2; var q = o + d * mid; if (q.y <= PickY(q.x, q.z)) b = mid; else a = mid; }
                    hp = o + d * b; hit = true;
                    break;
                }
                prev = t;
            }
            if (!hit) return false;
            int x = Mathf.FloorToInt((hp.x - Origin.x) / T), y = Mathf.FloorToInt((hp.z - Origin.z) / T);
            if (!M.InBounds(x, y)) return false;
            tile = new Vector2Int(x, y);
            return true;
        }

        // ---------------------------------------------------------- 动画 --
        // 行军：沿路径（格子中心的 Catmull-Rom 平滑曲线）匀速行进，贴地、按速度换步态，扬尘
        public IEnumerator MoveUnit(BUnit u, List<Vector2Int> path)
        {
            var v = VisOf(u);
            if (v == null || path == null) yield break;
            Sfx.Play("march", 0.4f);
            if (path.Count < 2) { v.Rest(); yield break; }
            var pts = path.Select(p => Tile(p.x, p.y)).ToList();
            int n = pts.Count - 1; const float per = 0.21f;
            Func<float, Vector3> at = s =>
            {
                int i = Mathf.Min(n - 1, Mathf.FloorToInt(s)); float t = s - i;
                Vector3 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(n, i + 2)];
                float t2 = t * t, t3 = t2 * t;
                Func<float, float, float, float, float> f = (a, b, c, d) => 0.5f * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
                return new Vector3(f(p0.x, p1.x, p2.x, p3.x), 0, f(p0.z, p1.z, p2.z, p3.z));
            };
            float tt = 0;
            while (tt < 1)
            {
                if (this == null || v == null) yield break;
                float s = tt * n;
                Vector3 cur = at(s), ahead = at(Mathf.Min(n, s + 0.12f));
                if ((ahead - cur).sqrMagnitude > 1e-5f) v.Face(ahead - cur);
                float gy = GroundY(cur.x, cur.z);
                int ti = Mathf.Min(n, BattleBoard.JsRound(s)); float ty = pts[ti].y;
                v.pos = new Vector3(cur.x, Mathf.Max(gy, ty <= BattleBoard.WADE_Y + 0.001 ? (float)BattleBoard.WADE_Y : gy) + Mathf.Abs(Mathf.Sin(s * Mathf.PI * 2)) * 0.03f, cur.z);
                if (Rnd() < 0.3f) DustPuff(v.pos, 0.6f);   // 装饰随机（不动规则随机数）
                yield return null;
                tt += Mathf.Min(0.1f, Time.deltaTime) / (per * n);
            }
            v.pos = pts[n];
            v.Rest();
        }

        public IEnumerator Lunge(BUnit a, BUnit t)
        {
            var v = VisOf(a);
            if (v == null) yield break;
            var from = Tile(a.x, a.y); var to = Tile(t.x, t.y);
            v.Face(to - from);
            v.Act("attack");
            for (float k = 0; k < 1; k += Mathf.Min(0.1f, Time.deltaTime) / 0.36f)
            {
                if (v == null) yield break;
                v.pos = Vector3.Lerp(from, to, Mathf.Sin(k * Mathf.PI) * 0.38f);
                yield return null;
            }
            if (v != null) v.pos = from;
        }

        public void Hit(BUnit u, int dmg, bool big = false)
        {
            var v = VisOf(u);
            if (v == null) return;
            v.FlashHit();
            var p = Tile(u.x, u.y);
            Burst(p + Vector3.up * 0.6f, new Color(1f, 0.9f, 0.6f), 14, 2.5f);
            FloatText(p + Vector3.up * 1.6f, "-" + dmg, big ? Art.Hex("#ffd14d") : Art.Hex("#ff8c73"), big ? 44 : 34);
        }
        public void Refresh(BUnit u) { var v = VisOf(u); if (v != null) v.Refresh(); }
        // 溃散：士兵倒地、战马人立，随后沉入地下消失
        public IEnumerator Rout(BUnit u)
        {
            var v = VisOf(u);
            if (v == null) yield break;
            v.holdLabel = false;
            Sfx.Play("lose", 0.25f);
            Smoke(Tile(u.x, u.y) + Vector3.up * 0.3f);
            var p = v.pos;
            if (v.HasFigure)
            {
                v.Act("rout");
                for (float t = 0; t < 1; t += Mathf.Min(0.1f, Time.deltaTime) / 0.75f) { if (v == null) yield break; yield return null; }
                for (float t = 0; t < 1; t += Mathf.Min(0.1f, Time.deltaTime) / 0.45f) { if (v == null) yield break; v.pos = new Vector3(p.x, p.y - t * t * 0.9f, p.z); yield return null; }
            }
            else
                for (float t = 0; t < 1; t += Mathf.Min(0.1f, Time.deltaTime) / 0.6f) { if (v == null) yield break; v.pos = new Vector3(p.x, p.y - t * 0.8f, p.z); yield return null; }
            if (v != null) v.Hide();
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

        void OnDestroy()
        {
            SpDispose();
            foreach (var v in vis.Values) if (v != null) v.Release();
            vis.Clear();
            if (Crowd != null) { try { Crowd.Dispose(); } catch (Exception e) { Debug.LogException(e); } Crowd = null; }
            FreeField();
            foreach (var g in hlGeos.Values) if (g != null) Destroy(g);
            hlGeos.Clear();
            foreach (var m in ownMeshes) if (m != null) Destroy(m);
            ownMeshes.Clear();
            foreach (var m in ownMats) if (m != null) Destroy(m);
            ownMats.Clear();
            hlMats.Clear(); hlBase.Clear();
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

    // 每帧在 WorldFollow.LateUpdate（缺省执行顺序 0）之后运行名牌避让，在它算好的位置上叠加偏移
    [DefaultExecutionOrder(1000)]
    public class UnitLabelLayout : MonoBehaviour
    {
        public BattleView view;
        void LateUpdate() { if (view != null) view.LayoutLabels(Time.deltaTime); }
    }

    // ============================================================ UnitVisual --
    // 一支部队：骑马的主将（Figure 中精度）+ 若干实例化士兵（BattleView.Crowd，低精度、势力色）+ 脚下的阵营光环 + 头顶信息。
    // 动作：待机、行军（沿路径行走 / 小跑）、攻击（突进 + 挥击）、受击、必杀（蓄势）、溃散（倒地）。
    // 逻辑位置 pos 由动画写入；每帧（BattleView.Update 调用 Tick）把节点放在 pos 处并贴地（跳跃等抬高的部分保留）。
    // 人物模型建不出来时退回旧版低多边形模型（Models.Commander / Soldier）。
    public class UnitVisual : MonoBehaviour
    {
        BattleView view; public BUnit u; Color col; string hex;
        public string culture;
        public Vector3 pos;                   // 逻辑位置（动画写这里；网页版 v.pos）
        float facing, yaw;
        float flash, heat, heatSeed;
        Text label; RectTransform barFill; RectTransform info;
        GameObject ring; Material ringMat; string ringKey = "";
        GameObject confuseRing; Material confuseMat;
        // 第二版：着色光、灼烧、限时加成
        public bool holdLabel;            // 控制器置 true 时，阵亡部队的名牌暂不隐藏（单挑结束到溃散动画之间）
        public bool hidden;               // 暗影刺杀：部队暂时隐身
        float spTint; Color spTintColor = Color.white;
        string spKey = "";
        GameObject buffRing; Material buffMat;
        RectTransform chips;
        static readonly Color HeatTint = new Color(1, 0.42f, 0.1f);
        // 名牌避让（BattleView.LayoutLabels 写入；= 网页版 v.lbl 与 v.labelAlpha）
        internal WorldFollow follow;
        internal Vector3 labelPos;                                  // 名牌锚点：部队上方 2.3
        internal float labelAlpha;                                  // 已行动 / 必杀淡出后的不透明度（未乘避让淡化）
        internal bool lblShown; internal float lblDx, lblDy, lblTdx, lblTdy, lblFade = 1, lblTfade = 1;
        internal int lblPr, lblOrder, lblZ, lblRect; internal Vector2 lblBase;
        internal Image lblOutline; internal RectTransform lblStem; internal Image lblStemImg, lblDotImg;
        internal float lblStemH = -1; internal int lblStyle = -1;
        internal RectTransform Info { get { return info; } }
        // 第三版：人物模型与动作
        bool figMode; int bucket = -1;
        Figure fig; FigureHorse horse; Transform figRoot;
        GameObject lpGo; MeshFilter lpMf; Material lpMat;   // 低多边形回退
        sealed class Sold { public int id; public double x, z; public string type, clip = ""; public bool mounted; public double jitter; }
        readonly List<Sold> sold = new List<Sold>();
        sealed class ActInfo { public string kind, rider, horse; public float dur; }
        sealed class AnimState { public float t, actT, moving; public ActInfo act; public string gait = "walk"; public Vector3 last; public bool routing; public string tintKey = ""; }
        readonly AnimState anim = new AnimState();
        Vector3 placeP; float placeYaw = float.NaN;
        static readonly System.Random rnd = new System.Random();   // 装饰性随机（不动规则随机数）
        static float R() { return (float)rnd.NextDouble(); }
        static string Hex(Color c)
        {
            Func<float, int> b = v => (int)Math.Floor(Mathf.Clamp01(v) * 255 + 0.5);
            return "#" + b(c.r).ToString("x2") + b(c.g).ToString("x2") + b(c.b).ToString("x2");
        }

        public bool HasFigure { get { return fig != null; } }
        // 旧接口：低多边形回退时部队的合并网格（共享，勿释放）
        public Mesh SharedMesh { get { return lpMf != null ? lpMf.sharedMesh : null; } }

        public void Init(BattleView v, BUnit unit, Color c)
        {
            view = v; u = unit; col = c; hex = Hex(c);
            culture = v.CultureOf(u.side);
            heatSeed = R() * 10;
            anim.t = R() * 4;
            pos = v.Tile(u.x, u.y);
            facing = BattleBoard.UnitYaw(u.side);
            yaw = facing;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            anim.last = pos;
            // 阵营光环（攻方蓝、守方红）
            ring = Art.MakeObject("Ring", v.UnitDisc, v.sideRing[u.side == 0 ? 0 : 1], transform, false);
            ring.transform.localPosition = Vector3.up * 0.04f;
            ring.transform.localScale = new Vector3(0.95f, 1, 0.95f);
            // 人物模型：BattleView 预载完成后由 FiguresReady() 建立；在此之前只显示光环与名牌
            figMode = v.Crowd != null;
            if (!figMode) UseLowPoly();
            BuildLabel();
            Refresh();
        }
        void UseLowPoly()
        {
            figMode = false;
            if (lpGo == null)
            {
                lpGo = new GameObject("LowPoly");
                lpGo.transform.SetParent(transform, false);
                lpMf = lpGo.AddComponent<MeshFilter>();
                var mr = lpGo.AddComponent<MeshRenderer>();
                lpMat = Art.NewLowPoly();
                mr.sharedMaterial = lpMat;
                mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
            }
            bucket = -1;
        }
        public void FiguresReady()
        {
            if (!figMode || fig != null) return;
            try { BuildFigures(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (figRoot != null) Destroy(figRoot.gameObject);
                try { if (fig != null) fig.Dispose(); if (horse != null) horse.Dispose(); } catch (Exception) { /* 忽略 */ }
                fig = null; horse = null; figRoot = null;
                UseLowPoly();
            }
            bucket = -1;
            Refresh();
        }
        void BuildFigures()
        {
            fig = Figure.General(u.gen, new FigOpts { color = hex, lod = "mid", mounted = true });
            horse = Figure.Horse(new FigHorseOpts
            {
                color = hex, lod = "mid", seed = Math.Abs(u.gen.id) % 5, culture = culture, general = true,
                armor = culture == "persia" || culture == "sarmatian" ? "scale" : "cloth",
            });
            horse.Mount(fig);
            figRoot = new GameObject("Figures").transform;
            figRoot.SetParent(transform, false);
            figRoot.localScale = Vector3.one * BattleBoard.FIG_S;
            // 网页版：root.rotation.y = π（模型朝 +Z、部队前方为局部 −Z）、位置 (0, 0, −0.36) → Unity 朝 +Z、位置 (0, 0, 0.36)
            horse.Root.SetParent(figRoot, false);
            horse.Root.localRotation = Quaternion.identity;
            horse.Root.localPosition = BattleBoard.HorseLocal;
            if (hidden) figRoot.gameObject.SetActive(false);
        }
        // 士兵的兵种与队形，同步到士兵群
        void SyncSoldiers(int n)
        {
            var cr = view.Crowd;
            if (cr == null) return;
            var want = BattleBoard.SoldierSlots(culture, n);
            while (sold.Count > want.Count) { var s = sold[sold.Count - 1]; sold.RemoveAt(sold.Count - 1); cr.Remove(s.id); }
            string cul = BattleBoard.FigCult(culture);
            for (int i = sold.Count; i < want.Count; i++)
            {
                var w = want[i];
                int id = cr.Add(new FigCrowdSpec { type = w.type, culture = cul, color = hex, seed = (Math.Abs(u.gen.id) + i) % 2, mounted = w.mounted });
                sold.Add(new Sold { id = id, x = w.x, z = w.z, type = w.type, mounted = w.mounted, jitter = (i * 0.37) % 1 });
            }
            PlaceSoldiers(true);
        }
        void RemoveSoldiers()
        {
            var cr = view != null ? view.Crowd : null;
            if (cr != null) foreach (var q in sold) cr.Remove(q.id);
            sold.Clear();
        }
        void PlaceSoldiers(bool force)
        {
            var cr = view.Crowd;
            if (cr == null || sold.Count == 0) return;
            var gp = transform.position;
            if (!force && gp == placeP && yaw == placeYaw) return;
            placeP = gp; placeYaw = yaw;
            // 位置公式在 BattleBoard.SoldierPos（无头测试与网页版的士兵矩阵逐个比对）
            foreach (var q in sold) cr.SetTransform(q.id, view.Board.SoldierPos(gp, yaw, q.x, q.z), yaw, BattleBoard.FIG_S);
        }
        // 动作：kind ∈ attack | hit | special | rout；dur 秒（≤ 0 用缺省）
        public void Act(string kind, float dur = 0)
        {
            var A = anim;
            if (A.routing && kind != "rout") return;
            if (kind == "hit" && A.act != null && (A.act.kind == "attack" || A.act.kind == "special") && A.actT < A.act.dur * 0.7f) return;
            string style = fig != null ? fig.Style : "pole";
            bool heavy = R() < 0.4f;
            string rider, hz; float d0; Func<Sold, string> sc;
            switch (kind)
            {
                case "attack":
                    d0 = 0.75f; rider = heavy ? "heavy" : (R() < 0.5f ? "light1" : "light2"); hz = "gallop";
                    sc = q => q.mounted ? "attack" : q.type == "archer" ? "bowShoot" : (R() < 0.5f ? "light1" : "heavy");
                    break;
                case "hit":
                    d0 = 0.5f; rider = R() < 0.5f ? "hit" : "hitB"; hz = "idle";
                    sc = q => q.mounted ? "hit" : (R() < 0.6f ? "hit" : "block");
                    break;
                case "special":
                    d0 = 1.2f; rider = "special"; hz = "rear";
                    sc = q => q.mounted ? "charge" : "cheer";
                    break;
                case "rout":
                    d0 = 1.1f; rider = "hit"; hz = "rear";
                    sc = q => q.mounted ? "die" : (R() < 0.5f ? "die" : "knockdown");
                    break;
                default: return;
            }
            if (style == "bow" && kind == "attack") rider = "bowShoot";
            A.act = new ActInfo { kind = kind, dur = dur > 0 ? dur : d0, rider = rider, horse = hz };
            A.actT = 0;
            if (kind == "rout") A.routing = true;
            var cr = view.Crowd;
            if (cr != null)
                foreach (var q in sold)
                {
                    if (kind == "hit" && R() < 0.35f) continue;
                    string clip = sc(q);
                    q.clip = clip;
                    cr.Play(q.id, clip, cr.Time - q.jitter * 0.12);
                }
        }

        void BuildLabel()
        {
            var lay = UIKit.LabelLayer;
            if (lay == null) return;
            info = UIKit.Img(lay, UIKit.RR, new Color(0.05f, 0.05f, 0.08f, 0.78f), "UnitInfo").rectTransform;
            info.anchorMin = info.anchorMax = new Vector2(0.5f, 0.5f); info.sizeDelta = new Vector2(130, 44);
            var stripe = UIKit.Img(info, UIKit.RR, col, "Stripe"); stripe.rectTransform.anchorMin = new Vector2(0, 0); stripe.rectTransform.anchorMax = new Vector2(0, 1); stripe.rectTransform.offsetMin = new Vector2(3, 3); stripe.rectTransform.offsetMax = new Vector2(9, -3);
            label = UIKit.Label(info, "", 18, UIKit.Text, TextAnchor.UpperCenter);
            UIKit.Stretch(label.rectTransform, 10, 12, 4, 2);
            var bar = UIKit.Bar(info, 1, u.side == 0 ? new Color(0.45f, 0.75f, 1f) : new Color(1f, 0.5f, 0.42f));
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.offsetMin = new Vector2(14, 5); bar.offsetMax = new Vector2(-6, 11);
            barFill = (RectTransform)bar.Find("Fill");
            labelPos = transform.position + Vector3.up * 2.3f;
            var wf = info.gameObject.AddComponent<WorldFollow>(); wf.worldPos = labelPos; wf.offset = new Vector2(0, 22 + 6); wf.hideBeyond = 90;
            follow = wf;
            // 边框（网页版 .sg-uinfo 的金边；主将 .cmd 加深，选中 .sel 更亮）与上移时连回部队的引线
            lblOutline = UIKit.Img(info, UIKit.RROutline, new Color(0.953f, 0.788f, 0.412f, 0.42f), "Outline");
            UIKit.Stretch(lblOutline.rectTransform); lblOutline.raycastTarget = false;
            lblStem = UIKit.NewRect("Stem", info);
            lblStem.anchorMin = lblStem.anchorMax = new Vector2(0.5f, 0); lblStem.pivot = new Vector2(0.5f, 1);
            lblStem.sizeDelta = new Vector2(2, 0);
            lblStemImg = UIKit.Img(lblStem, null, new Color(0.953f, 0.788f, 0.412f, 0.6f), "Line");
            UIKit.Stretch(lblStemImg.rectTransform); lblStemImg.raycastTarget = false;
            lblDotImg = UIKit.Img(lblStem, UIKit.RR, new Color(0.953f, 0.788f, 0.412f, 1f), "Dot");
            var dr = lblDotImg.rectTransform; dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 0); dr.pivot = new Vector2(0.5f, 0.5f);
            dr.sizeDelta = new Vector2(6, 6); dr.anchoredPosition = Vector2.zero; lblDotImg.raycastTarget = false;
            lblStem.gameObject.SetActive(false);
        }

        public void Refresh()
        {
            bool shownG = gameObject.activeSelf && !hidden;
            if (figMode)
            {
                int b = !u.alive && !anim.routing ? 0 : BattleBoard.SoldierBucket(u.Troops, u.alive);
                if (fig != null && b != bucket && !anim.routing && shownG) { bucket = b; SyncSoldiers(BattleBoard.SoldierCount(b, culture)); }
            }
            else if (lpMf != null)
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
                    var old = lpMf.sharedMesh;
                    lpMf.sharedMesh = mb.ToMesh("unit");
                    if (old != null) Destroy(old);
                }
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
                confuseMat.renderQueue = 3005;
                confuseRing = Art.MakeObject("Confused", view.UnitDisc, confuseMat, transform, false);
                confuseRing.transform.localPosition = Vector3.up * 1.75f;
                confuseRing.transform.localScale = Vector3.one * 0.6f;
                if (hidden) confuseRing.SetActive(false);
            }
            if (u.confused <= 0 && confuseRing != null) { Destroy(confuseRing); Destroy(confuseMat); confuseRing = null; confuseMat = null; }
            SpRefresh();          // 必杀技的限时加成（光环 + 名牌小字）
        }

        public void Face(Vector3 dir) { if (dir.x * dir.x + dir.z * dir.z > 0.001f) facing = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg; }
        public void Rest() { anim.moving = 0; }
        public void Flash() { FlashHit(); }
        public void FlashHit() { flash = 1; Act("hit"); }
        public void Burn() { heat = 1; }
        // 必杀技的着色光（金光、护盾等）：1 → 0 渐退，盖过受击闪白
        public void Tint(Color c, float k = 1) { spTintColor = c; spTint = k; }
        // 暗影刺杀：部队暂时隐身（网页版 group.visible = false）。隐身时士兵移出士兵群；
        // 重新出现时按兵力重建士兵（网页版重新显示后不再补回士兵，是个疏漏，这里补上）
        public void SetHidden(bool on)
        {
            if (hidden == on) return;
            hidden = on;
            if (figRoot != null) figRoot.gameObject.SetActive(!on);
            if (lpGo != null) lpGo.SetActive(!on);
            if (ring != null) ring.SetActive(!on);
            if (confuseRing != null) confuseRing.SetActive(!on);
            if (buffRing != null) buffRing.SetActive(!on);
            if (on) RemoveSoldiers();
            else { bucket = -1; Refresh(); }
        }
        // 溃散动画结束：整支部队隐去（士兵移出士兵群）
        public void Hide()
        {
            RemoveSoldiers();
            if (info != null) info.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }
        internal void ResetRing() { ringKey = ""; }

        // ---- 限时加成：脚下彩色光环 + 名牌右上角的小字（守 / 攻 / 毒 / 疾）
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

        // 每帧（BattleView.Update 按部队顺序调用；网页版 UnitVisual.update）
        internal void Tick(float dt, float time)
        {
            var A = anim;
            yaw = Mathf.LerpAngle(yaw, facing, dt * 10);
            float shake = 0;
            if (flash > 0)
            {
                flash = Mathf.Max(0, flash - dt * 4);
                shake = Mathf.Sin(time * 80) * 0.03f * flash;
            }
            // 受击闪白、火计灼烧的橙色、必杀技的着色光合成到同一个闪光色 / 强度上
            float burn = heat > 0 ? Mathf.SmoothStep(0, 1, heat) * 0.55f * (0.86f + 0.14f * Mathf.Sin(time * 26 + heatSeed)) : 0;
            if (heat > 0) heat = Mathf.Max(0, heat - dt * 0.42f);
            Color fc = burn > 0 ? Color.Lerp(HeatTint, Color.white, flash / (flash + burn)) : Color.white;
            float fk = Mathf.Max(flash, burn);
            if (buffRing != null)
            {
                buffRing.transform.Rotate(0, -60 * dt, 0);   // 网页版 rotation.y += 60°·dt（three）
                float s = 1.05f * (1 + Mathf.Sin(time * 3 + heatSeed) * 0.05f);
                buffRing.transform.localScale = new Vector3(s, 1, s);
            }
            if (spTint > 0)
            {
                spTint = Mathf.Max(0, spTint - dt * 1.1f);
                float k = Mathf.SmoothStep(0, 1, spTint) * 0.6f;
                if (k > fk) { fc = spTintColor; fk = k; }
            }
            // 位置：贴地（动画只写 pos；跳跃等抬高的部分保留）
            float gy = view.GroundY(pos.x, pos.z);
            transform.position = new Vector3(pos.x + shake, Mathf.Max(pos.y, gy), pos.z);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (confuseRing != null) confuseRing.transform.Rotate(0, 200 * dt, 0);
            // 行进速度 → 步态
            float sp = dt > 0 ? Mathf.Sqrt((pos.x - A.last.x) * (pos.x - A.last.x) + (pos.z - A.last.z) * (pos.z - A.last.z)) / dt : 0;
            A.last = pos;
            if (sp > 0.6f) A.moving = 0.15f; else if (A.moving > 0) A.moving = Mathf.Max(0, A.moving - dt);
            A.t += dt;
            // 阵营光环抬到格子周围的地表之上（坡地上不被地面遮住）
            string tk = u.x + "," + u.y;
            if (tk != ringKey && A.moving <= 0 && ring != null)
            {
                ringKey = tk;
                ring.transform.localPosition = new Vector3(0, 0.04f + Mathf.Min(0.45f, Mathf.Max(0, view.RingY(u.x, u.y, 0.9f))), 0);
            }
            if (fig != null) Animate(dt, sp, fc, fk);
            else if (lpMat != null) { lpMat.SetFloat("_Flash", fk); lpMat.SetColor("_FlashColor", fc); }
            labelPos = transform.position + Vector3.up * 2.3f;
            if (follow != null) follow.worldPos = labelPos;
            if (info != null)
            {
                float a = (u.alive || holdLabel) && !hidden && gameObject.activeSelf ? (u.acted && u.side == view.M.side ? 0.55f : 1f) : 0f;
                if (view.SpLabelsDim) a *= 0.16f;
                labelAlpha = a;
                if (follow != null) follow.alphaMul = a * (lblShown ? lblFade : 1f);   // 避让失败的名牌淡到 0.3（LayoutLabels）
            }
        }
        void Animate(float dt, float sp, Color fc, float fk)
        {
            var A = anim; var cr = view.Crowd;
            // 网页版 if (!this.group.visible)：暗影隐身中、或溃散后已隐去（Hide → SetActive(false)）的部队不再摆姿势
            if (hidden || !gameObject.activeSelf) { if (sold.Count > 0) RemoveSoldiers(); return; }
            bool moving = A.moving > 0 && !A.routing;
            string gait = sp > 7 ? "gallop" : sp > 3.2f ? "trot" : "walk";
            if (moving) A.gait = gait;
            var act = A.act;
            if (act != null) { A.actT += dt; if (A.actT >= act.dur && !A.routing) { A.act = act = null; } }
            // 主将与战马
            string hc = "idle", rc = "idle"; float rt = A.t, ht = A.t;
            if (act != null) { rc = act.rider; rt = Mathf.Min(A.actT, act.dur); hc = act.horse; ht = A.actT; if (act.kind == "rout") rt = Mathf.Min(A.actT, 0.45f); }
            else if (moving) hc = A.gait;
            if (hc == "rear" && A.actT > 1.1f) hc = "idle";
            try
            {
                horse.SetPose(hc, ht);
                fig.SetPose(Figure.Clips.ContainsKey(rc) ? rc : "idle", rt, new FigPoseOpts { gallop = hc == "gallop" });
                fig.Update(dt);
            }
            catch (Exception) { /* 片段缺失时保持上一姿势 */ }
            // 着色（受击闪白 / 灼烧 / 必杀光）
            float k = fk > 0.01f ? Mathf.Round(fk * 20) / 20 : 0;
            string key = k > 0 ? Hex(fc) + k.ToString("0.00") : "";
            if (key != A.tintKey)
            {
                A.tintKey = key;
                try { fig.SetTint(k > 0 ? Hex(fc) : "#ffffff", k * 0.8f); horse.SetTint(k > 0 ? Hex(fc) : "#ffffff", k * 0.6f); } catch (Exception) { /* 忽略 */ }
            }
            // 士兵：动作结束后回到待机 / 行军片段
            if (cr != null && sold.Count > 0)
            {
                foreach (var q in sold)
                {
                    if (A.routing) continue;
                    if (act != null && q.clip != "" && q.clip != "idle" && q.clip != "walk" && q.clip != "run" && q.clip != "trot" && q.clip != "gallop") continue;
                    string b = q.mounted ? (moving ? (A.gait == "walk" ? "walk" : A.gait) : "idle") : (moving ? (A.gait == "walk" ? "walk" : "run") : "idle");
                    if (q.clip != b) { q.clip = b; cr.Play(q.id, b, null, q.jitter); }
                }
                PlaceSoldiers(false);
            }
        }
        // 主将 + 战马当前姿势烘焙为静态网格（部队局部坐标；残影特效用）。own = true 时调用方负责释放；
        // 低多边形回退时返回部队的合并网格（共享，own = false）
        public Mesh GhostMesh(out bool own)
        {
            own = false;
            if (fig == null) return SharedMesh;
            var verts = new List<Vector3>(); var tris = new List<int>();
            var w2l = transform.worldToLocalMatrix;
            var rs = new List<Renderer>(fig.Meshes);
            if (horse != null && horse.Body != null) rs.Add(horse.Body);
            var tmp = new Mesh();
            foreach (var r in rs)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Mesh src; Matrix4x4 m;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) { tmp.Clear(); smr.BakeMesh(tmp, true); src = tmp; }
                else { var mf = r.GetComponent<MeshFilter>(); src = mf != null ? mf.sharedMesh : null; }
                if (src == null) continue;
                m = w2l * r.transform.localToWorldMatrix;
                int b0 = verts.Count;
                foreach (var p in src.vertices) verts.Add(m.MultiplyPoint3x4(p));
                foreach (var i in src.triangles) tris.Add(i + b0);
            }
            Destroy(tmp);
            var g = new Mesh { name = "Ghost" };
            g.indexFormat = verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            g.SetVertices(verts); g.SetTriangles(tris, 0);
            g.RecalculateNormals(); g.RecalculateBounds();
            own = true;
            return g;
        }

        // 释放人物模型与士兵（BattleView 销毁时调用；物体本身随根节点销毁）
        internal void Release()
        {
            RemoveSoldiers();
            try { if (fig != null) fig.Dispose(); if (horse != null) horse.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            fig = null; horse = null;
        }
        void OnDestroy()
        {
            if (info != null) Destroy(info.gameObject);
            if (lpMf != null && lpMf.sharedMesh != null) Destroy(lpMf.sharedMesh);
            if (lpMat != null) Destroy(lpMat);
            if (confuseMat != null) Destroy(confuseMat);
            if (buffMat != null) Destroy(buffMat);
        }
        void OnDisable() { if (info != null) info.gameObject.SetActive(false); }
    }
}
