// ==========================================================================
// 三国志-霸王的世界 · 人物模型库 —— 士兵群（网页版 js/figure.js 的 Crowd / CrowdGroup / bakeSoldier / bakeMounted 的移植）
//
// 同一模板（兵种 × 文化 × 外观变体 × LOD）的所有士兵 = 一组实例化绘制（Graphics.DrawMeshInstanced，每批 ≤ 1023）：
// 动作预先烘焙成骨骼矩阵贴图（RGBAHalf，每帧一行，每骨 3 个像素 = 3×4 矩阵，FigBake），Sanguo/FigureCrowd 的顶点着色器
// 按实例的 (起始行, ±帧数, 起始时间, 速度) 取两帧双线性插值后蒙皮；势力色为实例属性 —— 同一模板任意势力共用一组绘制。
// 骑兵：骑手与战马各自成组，同一实例矩阵；骑手的动作在战马坐标中烘焙（随马背起伏）。
// 坐标：Unity 世界坐标（网页版 (x, y, z) → (x, y, −z)；朝向 yaw 弧度 → Figure.YawFromJs）。
// 用法：var c = new FigureCrowd(new FigCrowdOptions { lod = "low" });  StartCoroutine(c.Prepare(specs));
//       int id = c.Add(new FigCrowdSpec { type = "spear", culture = "han", color = "#2f7d4a" });
//       c.SetTransform(id, pos, yawDeg); c.Play(id, "walk", phase: 0.3);  每帧 c.SetTime(Time.time)（绘制由组件在 LateUpdate 提交）。
// ==========================================================================
#if !SANGUO_HEADLESS
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sanguo
{
    // 一名士兵（= JS crowd.add(o) 的 o）
    public sealed class FigCrowdSpec
    {
        public string type = "spear", culture, color;
        public int seed;
        public bool mounted;                // 骑兵带马（兵种按 cavalry）
        public FigCrowdHorse horse;         // 战马选项（只覆盖给了值的项，= JS Object.assign(默认, o.horse)）
    }
    // 士兵群的战马选项：null = 未给（用士兵群的默认：lod 按士兵群、seed = 士兵 seed % 2、马铠按文化）
    public sealed class FigCrowdHorse
    {
        public string lod, coat, mane, armor, color;
        public int? seed;
        // 默认选项上叠加给了值的项
        internal FigHorseOpts Over(FigHorseOpts h)
        {
            if (lod != null) h.lod = lod;
            if (coat != null) h.coat = coat;
            if (mane != null) h.mane = mane;
            if (armor != null) h.armor = armor;
            if (color != null) h.color = color;
            if (seed.HasValue) h.seed = seed.Value;
            return h;
        }
    }
    public sealed class FigCrowdOptions
    {
        public string lod = "low";
        public bool shadow = true;
        public int capacity = 32;
        public bool instanced = true;       // false：每名士兵一个 Figure（回退路径，同一接口）
        public Transform parent;
    }
    public sealed class FigCrowdStats { public int soldiers, groups, drawCalls, triangles, bakeBytes; }

    // 烘焙贴图（CPU 数据 + Unity 贴图）
    sealed class FigBakeTex
    {
        public FigBakeData data;
        public Texture2D tex;
        public int bytes;
        public static FigBakeTex Upload(FigBakeData d)
        {
            var h = FigBake.Halves(d);
            var raw = new byte[h.Length * 2];
            Buffer.BlockCopy(h, 0, raw, 0, raw.Length);
            var t = new Texture2D(d.W, d.rows, TextureFormat.RGBAHalf, false, true) { name = "SG.Figure.bake" };
            t.filterMode = FilterMode.Bilinear; t.wrapMode = TextureWrapMode.Clamp;
            t.LoadRawTextureData(raw);
            t.Apply(false, true);
            FigStats.textures++;
            d.unity = t;
            d.data = null;            // 上传后只用到片段表与尺寸：单精度矩阵数据（每个兵种约 0.5 MB）不再常驻
            return new FigBakeTex { data = d, tex = t, bytes = raw.Length };
        }
    }
    sealed class FigBakeSet { public FigBakeTex soldier, rider, horse; public double ms; }

    // 士兵群组件：每帧提交绘制
    public sealed class FigureCrowdDriver : MonoBehaviour
    {
        internal FigureCrowd crowd;
        void LateUpdate() { if (crowd != null) crowd.Draw(); }
        void OnDestroy() { if (crowd != null) crowd.Release(false); }
    }

    public sealed class FigureCrowd
    {
        // ------------------------------------------------------------ 烘焙缓存（= JS BAKES；常驻）
        static readonly Dictionary<string, FigBakeSet> BAKES = new Dictionary<string, FigBakeSet>();
        static FigLook LookOf(FigCrowdSpec o, string lod)
        {
            return FigBuild.lookOfSoldier(new FigOpts { type = o.mounted ? "cavalry" : o.type, culture = o.culture, color = o.color, seed = o.seed, lod = lod });
        }
        // JS crowdHorseOpts：lod（mid 或 low）、seed % 2、马铠（o.horse.armor，否则波斯 / 萨尔马特为鳞甲、其余布衣），再叠加 o.horse
        static FigHorseOpts HorseOpts(FigCrowdSpec o, FigLook look, string lod)
        {
            var h = new FigHorseOpts { lod = lod == "mid" ? "mid" : "low", seed = o.seed % 2, armor = look.culture == "persia" || look.culture == "sarmatian" ? "scale" : "cloth", shadow = false };
            if (o.horse != null) o.horse.Over(h);
            return h;
        }
        static string HorseKey(FigHorseOpts h) { return string.Join("|", new[] { h.lod, h.coat ?? "", h.armor ?? "", h.seed.ToString() }); }
        static FigBakeSet Drain(IEnumerable g) { FigBakeSet r = null; foreach (var o in g) { var s = o as FigBakeSet; if (s != null) r = s; } return r; }
        // 步兵烘焙（分步；最后一步给出结果）
        static IEnumerable BakeSoldierGen(FigTemplate tpl, string key)
        {
            string bk = "S|" + key;
            FigBakeSet b;
            if (BAKES.TryGetValue(bk, out b)) { yield return b; yield break; }
            double ms = 0, t0 = FigMath.Now();
            var d = new FigBakeData();
            foreach (var _ in FigBake.Soldier(tpl, d)) { ms += FigMath.Now() - t0; yield return null; t0 = FigMath.Now(); }
            b = new FigBakeSet { soldier = FigBakeTex.Upload(d) };
            b.ms = ms + FigMath.Now() - t0; FigStats.bakeMs += b.ms;
            BAKES[bk] = b;
            yield return b;
        }
        static IEnumerable BakeMountedGen(FigTemplate tpl, string key, FigHorseOpts ho, string hkey)
        {
            string bk = "M|" + key + "|" + hkey;
            FigBakeSet b;
            if (BAKES.TryGetValue(bk, out b)) { yield return b; yield break; }
            double t0 = FigMath.Now();
            var ht = FigTemplates.GetHorse(ho);
            var rider = new FigBakeData(); var horse = new FigBakeData();
            foreach (var _ in FigBake.Mounted(tpl, ht, rider, horse)) yield return null;
            FigTemplates.ReleaseHorse(ht);
            b = new FigBakeSet { rider = FigBakeTex.Upload(rider), horse = FigBakeTex.Upload(horse), ms = FigMath.Now() - t0 };
            FigStats.bakeMs += b.ms;
            BAKES[bk] = b;
            yield return b;
        }
        // 预烘焙（= JS bakeCrowdGen；Figure.Preload 与 Prepare 用）
        internal static IEnumerable BakeGen(FigCrowdSpec o, string lod)
        {
            lod = lod ?? "low";
            var look = LookOf(o, lod);
            string key = FigBuild.soldierKey(look);
            var tpl = FigTemplates.Get(look, key);
            yield return null;
            if (!o.mounted) foreach (var s in BakeSoldierGen(tpl, key)) yield return null;
            else { var ho = HorseOpts(o, look, lod); foreach (var s in BakeMountedGen(tpl, key, ho, HorseKey(ho))) yield return null; }
            FigTemplates.Release(tpl);
        }

        // ------------------------------------------------------------ 组（= JS CrowdGroup）
        sealed class Group
        {
            public string name;
            public Mesh mesh; public Material mat; public FigBakeTex bake; public int tris;
            public readonly List<int> ids = new List<int>();
            public readonly List<Matrix4x4> mats = new List<Matrix4x4>();
            public readonly List<Vector4> anim = new List<Vector4>(), team = new List<Vector4>();
            public int count { get { return ids.Count; } }
        }
        sealed class Part { public Group g; public int i; }
        sealed class Item
        {
            public int id; public bool mounted; public Color color; public readonly List<Part> parts = new List<Part>();
            public string clip = "idle"; public double t0, speed = 1; public Matrix4x4 m = Matrix4x4.identity;
            public Dictionary<string, FigBakeEntry> table;
            public Figure fig; public FigureHorse horse;       // 回退路径
        }

        public readonly GameObject gameObject;
        public Transform Root { get { return gameObject.transform; } }
        public readonly string lod;
        public readonly bool shadow, instanced;
        readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
        readonly Dictionary<int, Item> items = new Dictionary<int, Item>();
        readonly Dictionary<string, Mesh> crowdMeshes = new Dictionary<string, Mesh>();
        // 战马模板（= JS Crowd._htpls：每个士兵群按马的键各持有一份，释放士兵群时归还；否则每加入一名骑兵都要重建马的几何）
        readonly Dictionary<string, FigHorseTemplate> htpls = new Dictionary<string, FigHorseTemplate>();
        int next = 1;
        float time;
        bool disposed;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly Matrix4x4[] matBuf = new Matrix4x4[1023];
        static readonly Vector4[] animBuf = new Vector4[1023], teamBuf = new Vector4[1023];

        public FigureCrowd(FigCrowdOptions opts = null)
        {
            opts = opts ?? new FigCrowdOptions();
            FigUnity.Hook();
            lod = opts.lod ?? "low";
            shadow = opts.shadow;
            instanced = opts.instanced && SystemInfo.supportsInstancing;
            gameObject = new GameObject("SG.Figure.crowd");
            if (opts.parent != null) gameObject.transform.SetParent(opts.parent, false);
            gameObject.AddComponent<FigureCrowdDriver>().crowd = this;
        }
        public float Time { get { return time; } }
        // 预先烘焙将要加入的士兵（分片协程，每帧约 budget 毫秒）；之后 Add 不再同步烘焙
        public IEnumerator Prepare(IEnumerable<FigCrowdSpec> specs, double budget = 12)
        {
            var jobs = new List<KeyValuePair<string, IEnumerator>>();
            foreach (var o in specs) jobs.Add(new KeyValuePair<string, IEnumerator>("crowd " + o.culture + "/" + o.type, BakeGen(o, lod).GetEnumerator()));
            foreach (var _ in Figure.RunJobs(jobs, budget)) yield return null;
        }
        // 全局时钟（秒），每帧调用一次
        public FigureCrowd SetTime(float t)
        {
            time = t;
            if (!instanced) foreach (var it in items.Values) PoseFallback(it);
            return this;
        }
        Group GroupOf(string key, Func<Group> mk) { Group g; if (!groups.TryGetValue(key, out g)) { g = mk(); groups[key] = g; } return g; }
        Mesh CrowdMesh(string key, FigGeo geo)
        {
            Mesh m;
            if (crowdMeshes.TryGetValue(key, out m)) return m;
            m = FigUnity.Build(geo, "SG.Figure.crowd:" + key, true, true);
            // 动作可能超出绑定姿势的包围盒（倒地等）：取足够大的包围盒（JS 包围球 (0, 1, 0) r 2.5，且不做视锥剔除）
            m.bounds = new Bounds(new Vector3(0, 1, 0), Vector3.one * 5);
            crowdMeshes[key] = m;
            return m;
        }
        Group NewGroup(string name, Mesh mesh, FigBakeTex bake, bool ground, int tris)
        {
            var mat = new Material(FigMaterials.CrowdShader) { name = "SG.Figure.crowd", enableInstancing = true };
            mat.SetTexture("_MainTex", FigTextures.Map);
            mat.SetTexture("_BumpMap", FigTextures.Normal);
            mat.SetTexture("_ORM", FigTextures.ORM);
            FigMaterials.Common(mat);
            mat.SetTexture("_SgBake", bake.tex);
            mat.SetVector("_SgSize", new Vector4(bake.data.W, bake.data.rows, 0, 0));
            mat.SetFloat("_SgFps", FigBake.FPS);
            mat.SetFloat("_SgGroundOn", ground ? 1 : 0);
            return new Group { name = name, mesh = mesh, mat = mat, bake = bake, tris = tris };
        }
        int Alloc(Group g, int id) { g.ids.Add(id); g.mats.Add(Matrix4x4.identity); g.anim.Add(Vector4.zero); g.team.Add(Vector4.one); return g.ids.Count - 1; }
        void Free(Group g, int i)
        {
            int last = g.ids.Count - 1;
            if (i != last)
            {
                g.mats[i] = g.mats[last]; g.anim[i] = g.anim[last]; g.team[i] = g.team[last];
                int mv = g.ids[last]; g.ids[i] = mv;
                Item it; if (items.TryGetValue(mv, out it)) foreach (var p in it.parts) if (p.g == g) p.i = i;
            }
            g.ids.RemoveAt(last); g.mats.RemoveAt(last); g.anim.RemoveAt(last); g.team.RemoveAt(last);
        }

        // 加入一名士兵 → id
        public int Add(FigCrowdSpec o)
        {
            o = o ?? new FigCrowdSpec();
            int id = next++;
            var c = FigMath.col(o.color ?? "#8a2a24");
            var it = new Item { id = id, mounted = o.mounted, color = new Color((float)c.r, (float)c.g, (float)c.b, 1) };
            if (!instanced) return AddFallback(it, o);
            var look = LookOf(o, lod);
            string key = FigBuild.soldierKey(look);
            var tpl = FigTemplates.Get(look, key);
            if (o.mounted)
            {
                var ho = HorseOpts(o, look, lod);
                string hkey = HorseKey(ho);
                var ht = HorseTpl(ho, hkey);
                var bk = Drain(BakeMountedGen(tpl, key, ho, hkey));
                var gR = GroupOf("R|" + key + "|" + hkey, () => NewGroup(key + " 骑手", CrowdMesh(key, tpl.geometry), bk.rider, false, tpl.geometry.Tris));
                var gH = GroupOf("H|" + hkey + "|" + key, () => NewGroup("战马", CrowdMesh("H|" + ht.key, ht.geometry), bk.horse, false, ht.geometry.Tris));
                it.parts.Add(new Part { g = gR, i = Alloc(gR, id) }); it.parts.Add(new Part { g = gH, i = Alloc(gH, id) });
                it.table = bk.rider.data.table;
            }
            else
            {
                var bk = Drain(BakeSoldierGen(tpl, key));
                var g = GroupOf("F|" + key, () => NewGroup(key, CrowdMesh(key, tpl.geometry), bk.soldier, true, tpl.geometry.Tris));
                it.parts.Add(new Part { g = g, i = Alloc(g, id) });
                it.table = bk.soldier.data.table;
            }
            FigTemplates.Release(tpl);     // 组持有网格；士兵模板常驻
            items[id] = it;
            foreach (var p in it.parts) p.g.team[p.i] = it.color;
            Play(id, "idle", null, (id * 0.618034) % 1);
            SetTransform(id, Vector3.zero, 0);
            return id;
        }
        FigHorseTemplate HorseTpl(FigHorseOpts ho, string hkey)
        {
            FigHorseTemplate t;
            if (!htpls.TryGetValue(hkey, out t)) { t = FigTemplates.GetHorse(ho); htpls[hkey] = t; }
            return t;
        }
        // 位置（Unity 世界坐标，相对士兵群根节点）与朝向（yawDeg：Unity 的绕 Y 角度；网页版的 yaw 弧度用 Figure.YawFromJs 换算）
        public FigureCrowd SetTransform(int id, Vector3 pos, float yawDeg, float scale = 1)
        {
            return SetMatrix(id, Matrix4x4.TRS(pos, Quaternion.Euler(0, yawDeg, 0), new Vector3(scale, scale, scale)));
        }
        public FigureCrowd SetMatrix(int id, Matrix4x4 m)
        {
            Item it; if (!items.TryGetValue(id, out it)) return this;
            it.m = m;
            if (it.fig != null)
            {
                var r = it.horse != null ? it.horse.Root : it.fig.Root;
                r.localPosition = m.GetColumn(3);
                r.localRotation = m.rotation;
                r.localScale = m.lossyScale;
                return this;
            }
            foreach (var p in it.parts) p.g.mats[p.i] = m;
            return this;
        }
        // 播放片段：t0（起始时刻，缺省当前时钟）、phase（0..1，循环片段的随机相位）、speed。
        // 步兵片段：idle walk run light1 light2 heavy block hit knockdown down die cheer bowDraw bowShoot；骑兵：idle walk trot gallop attack charge hit die
        public FigureCrowd Play(int id, string clip, double? t0 = null, double phase = 0, double speed = 1)
        {
            Item it; if (!items.TryGetValue(id, out it)) return this;
            var tb = it.table;
            FigBakeEntry e = null;
            if (tb != null && !tb.TryGetValue(clip ?? "", out e)) tb.TryGetValue("idle", out e);
            it.clip = tb != null && tb.ContainsKey(clip ?? "") ? clip : "idle";
            it.speed = speed != 0 ? speed : 1;
            it.t0 = t0.HasValue ? t0.Value : time - phase * (e != null ? e.dur : 1);
            if (it.fig != null) { PoseFallback(it); return this; }
            foreach (var p in it.parts)
            {
                FigBakeEntry b;
                if (!p.g.bake.data.table.TryGetValue(it.clip, out b)) b = p.g.bake.data.table["idle"];
                p.g.anim[p.i] = new Vector4(b.row, b.loop ? b.n : -b.n, (float)it.t0, (float)it.speed);
            }
            return this;
        }
        public string ClipOf(int id) { Item it; return items.TryGetValue(id, out it) ? it.clip : null; }
        public double ClipDur(string clip, bool mounted)
        {
            if (mounted) { var a = FigTables.MOUNT_CLIPS.L(clip ?? "") ?? FigTables.MOUNT_CLIPS.L("idle"); return Convert.ToDouble(a[2]); }
            return (FigClips.Info(clip) ?? FigClips.Info("idle")).dur;
        }
        public FigureCrowd SetColor(int id, string hexc)
        {
            Item it; if (!items.TryGetValue(id, out it)) return this;
            var c = FigMath.col(hexc);
            it.color = new Color((float)c.r, (float)c.g, (float)c.b, 1);
            if (it.fig != null) { it.fig.Body.sharedMaterial = FigMaterials.Team(hexc); return this; }
            foreach (var p in it.parts) p.g.team[p.i] = it.color;
            return this;
        }
        public FigureCrowd Remove(int id)
        {
            Item it; if (!items.TryGetValue(id, out it)) return this;
            if (it.fig != null) { it.fig.Dispose(); if (it.horse != null) it.horse.Dispose(); }
            foreach (var p in it.parts) Free(p.g, p.i);
            items.Remove(id);
            return this;
        }
        public int Count { get { return items.Count; } }
        // 绘制次数：实例化时每组按 1023 个一批
        public int DrawCalls
        {
            get
            {
                int n = 0;
                if (!instanced) { foreach (var it in items.Values) n += (it.fig != null ? it.fig.DrawCalls : 0) + (it.horse != null ? 1 : 0); return n; }
                foreach (var g in groups.Values) n += (g.count + 1022) / 1023;
                return n;
            }
        }
        public int Triangles
        {
            get
            {
                int n = 0;
                if (!instanced) { foreach (var it in items.Values) n += (it.fig != null ? it.fig.Triangles : 0) + (it.horse != null ? it.horse.Triangles : 0); return n; }
                foreach (var g in groups.Values) n += g.tris * g.count;
                return n;
            }
        }
        public FigCrowdStats Stats()
        {
            var s = new FigCrowdStats { soldiers = items.Count, drawCalls = DrawCalls, triangles = Triangles };
            foreach (var g in groups.Values) { s.groups++; s.bakeBytes += g.bake.bytes; }
            return s;
        }
        // 提交本帧的实例化绘制（FigureCrowdDriver 在 LateUpdate 调用）
        public void Draw()
        {
            if (disposed || !instanced) return;
            var root = Root.localToWorldMatrix;
            int layer = gameObject.layer;
            var sc = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (var g in groups.Values)
            {
                for (int s = 0; s < g.count; s += 1023)
                {
                    int n = Math.Min(1023, g.count - s);
                    for (int i = 0; i < n; i++) { matBuf[i] = root * g.mats[s + i]; animBuf[i] = g.anim[s + i]; teamBuf[i] = g.team[s + i]; }
                    mpb.Clear();
                    mpb.SetVectorArray("_SgAnim", animBuf);
                    mpb.SetVectorArray("_SgTeam", teamBuf);
                    mpb.SetFloat("_SgTime", time);
                    Graphics.DrawMeshInstanced(g.mesh, 0, g.mat, matBuf, n, mpb, sc, false, layer);
                }
            }
        }
        public void Dispose() { Release(true); }
        internal void Release(bool destroy)
        {
            if (disposed) return;
            foreach (var id in new List<int>(items.Keys)) Remove(id);
            disposed = true;
            foreach (var g in groups.Values) UnityEngine.Object.Destroy(g.mat);
            groups.Clear();
            foreach (var m in crowdMeshes.Values) UnityEngine.Object.Destroy(m);
            crowdMeshes.Clear();
            foreach (var t in htpls.Values) FigTemplates.ReleaseHorse(t);
            htpls.Clear();
            if (destroy && gameObject != null) UnityEngine.Object.Destroy(gameObject);
        }

        // ------------------------------------------------------------ 回退：每名士兵一个 Figure，按同一接口驱动
        int AddFallback(Item it, FigCrowdSpec o)
        {
            it.fig = Figure.Soldier(new FigOpts { type = o.mounted ? "cavalry" : o.type, culture = o.culture, color = o.color, seed = o.seed, lod = lod, shadow = shadow });
            it.fig.Root.SetParent(Root, false);
            if (o.mounted)
            {
                // JS：Object.assign({ lod: 'low', color: o.color, seed: o.seed % 2 }, o.horse)
                var ho = new FigHorseOpts { lod = "low", color = o.color, seed = o.seed % 2, shadow = shadow };
                if (o.horse != null) o.horse.Over(ho);
                it.horse = Figure.Horse(ho);
                it.horse.Root.SetParent(Root, false);
                it.horse.Mount(it.fig);
                it.table = MountTable();
            }
            else it.table = InfantryTable();
            items[it.id] = it;
            Play(it.id, "idle", null, (it.id * 0.618034) % 1);
            return it.id;
        }
        // 回退路径的片段表（只用到时长；JS 的步兵回退没有片段表、恒为 idle，这里按片段目录补上）
        static Dictionary<string, FigBakeEntry> MountTable()
        {
            var d = new Dictionary<string, FigBakeEntry>();
            foreach (var c in FigBake.MountClips()) d[c.name] = new FigBakeEntry { dur = c.dur, loop = c.loop };
            return d;
        }
        static Dictionary<string, FigBakeEntry> InfantryTable()
        {
            var d = new Dictionary<string, FigBakeEntry>();
            foreach (var kv in FigClips.Catalog) d[kv.Key] = new FigBakeEntry { dur = kv.Value.dur, loop = kv.Value.loop };
            return d;
        }
        void PoseFallback(Item it)
        {
            double t = (time - it.t0) * it.speed;
            if (it.horse != null)
            {
                var M = FigTables.MOUNT_CLIPS.L(it.clip) ?? FigTables.MOUNT_CLIPS.L("idle");
                string hc = (string)M[0];
                it.horse.SetPose(hc, t);
                it.fig.SetPose((string)M[1], M.Count > 4 ? Convert.ToDouble(M[4]) : t, new FigPoseOpts { gallop = hc == "gallop" });
            }
            else it.fig.SetPose(FigClips.Info(it.clip) != null ? it.clip : "idle", t);
        }
    }
}
#endif
