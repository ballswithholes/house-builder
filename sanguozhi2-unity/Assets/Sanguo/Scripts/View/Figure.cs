// ==========================================================================
// 三国志-霸王的世界 · 人物模型库 —— 对外接口（网页版 js/figure.js 的 Figure / Horse / weapon / shield / preload / stats 的移植）
//
// 用法（与网页版同名同义；坐标为 Unity 坐标，见下）：
//   var f = Figure.General("关羽", new FigOpts { lod = "high" });        // 也可传 PortraitGen / General
//   f.Root.SetParent(parent, false); f.Root.localPosition = …;          // 人物朝 +Z、右手在 +X
//   f.SetPose("light1", t);  f.Update(dt);                              // 每帧：摆姿势 + 披风 / 盔缨弹簧
//   SetPose 之后、Update 之前可以直接改骨骼 Transform（f.Bones["spine"] 等；如单挑的动作交叉淡化、重击蓄势后仰、被格开的前臂抖动）：
//   Update 只写 6 根弹簧骨（cape1-3 plume1-2 hair1），dt ≤ 0 时什么都不写（= JS update）；Grip / IkHand 先读回各骨骼 Transform
//   （= JS 的 FK 直接读骨骼），只写双臂；下一次 SetPose / BlendPose 重写全部骨骼。
//   f.SetTint("#ffffff", 0.6f);  f.WeaponTip();  f.Attach(obj, "back");  f.Dispose();
//   var s = Figure.Soldier(new FigOpts { type = "spear", culture = "han", color = "#2f7d4a", lod = "low" });
//   var h = Figure.Horse(new FigHorseOpts { general = true, name = "赤兔" }); h.Mount(f); h.SetPose("gallop", t);
//   StartCoroutine(Figure.Preload(new FigPreload { crowd = … }));         // 分片预生成（每帧约 12ms）
// 环境贴图（反射与辐照度）是材质自己的贴图（= JS 材质的 envMap），不依赖 RenderSettings；光源强度 = JS 强度 / π（见 Figure.shader 头部）。
// 坐标：网页版 three (x, y, z) ↔ Unity (x, y, −z)（UNITY-V3 §1）。人物内部（骨骼、挂点、Grip / IkHand 的参数）用人物 root 的局部坐标：
//   JS 的人物局部 (x, y, z) ↔ Unity 局部 (−x, y, z)（= z 取反再绕 Y 转 180°，FigConv）。
//   JS 的 root.rotation.y = θ（弧度）↔ Unity localEulerAngles.y = 180 − θ·180/π（Figure.YawFromJs）。
// 几何、骨骼、动作、贴图都在纯 C# 部分（FigureMesh.cs / FigureRig.cs / FigureAtlas.cs）生成，本文件只把结果交给 Unity：
//   Mesh（x 镜像、绕序反转、boneWeights + bindposes）、SkinnedMeshRenderer、骨骼 Transform、Sanguo/Figure 材质。
// ==========================================================================
#if !SANGUO_HEADLESS
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sanguo
{
    // 预生成选项（= JS preload(opts)）
    public sealed class FigPreload
    {
        public string[] cultures;           // 缺省：全部文化
        public string[] types;              // 缺省：spear sword archer cavalry
        public string[] lods = { "low" };
        public int[] seeds = { 0 };
        public double budget = 12;          // 每帧毫秒数
        public PortraitGen[] generals;      // 名将模板与面部贴图
        public string generalLod = "mid";
        public bool crowdAll;               // true：所选文化 × 兵种（seed 0，骑兵带马）的士兵群烘焙
        public FigCrowdSpec[] crowd;        // 或指定的士兵群烘焙
        public string crowdLod = "low";
    }

    // 统计（= JS stats()）
    public sealed class FigStatsSnapshot
    {
        public int built, templates, textures, templateCount, horseTemplates, faces, materials;
        public double atlasMs, envMs, buildMs, lastBuildMs, bakeMs, preloadMs, maxStepMs, maxSliceMs;
        public string maxStepJob;
        public Dictionary<string, int> tris;
    }

    // Unity 端的模板网格缓存（FigTemplate.unity）
    sealed class FigMeshes
    {
        public Mesh body, head, weapon, shield, crowd;
        public Matrix4x4[] bindposes;
        public void Destroy() { foreach (var m in new[] { body, head, weapon, shield, crowd }) if (m != null) UnityEngine.Object.Destroy(m); }
    }

    // 每个人物 / 战马 GameObject 上的小组件：GameObject 被外部销毁（如整个画面的根节点被销毁）时释放模板与贴图
    public sealed class FigureNode : MonoBehaviour
    {
        internal Figure fig;
        internal FigureHorse horse;
        void OnDestroy()
        {
            if (fig != null) fig.Release(false);
            if (horse != null) horse.Release(false);
        }
    }

    // ------------------------------------------------------------------ 换算与网格
    public static class FigUnity
    {
        public static Vector3 V(V3d p) { return new Vector3((float)-p.x, (float)p.y, (float)p.z); }
        public static Quaternion Q(Q4d q) { return new Quaternion((float)q.x, (float)-q.y, (float)-q.z, (float)q.w); }
        public static V3d ToJs(Vector3 p) { return new V3d(-p.x, p.y, p.z); }
        public static Q4d ToJs(Quaternion q) { return new Q4d(q.x, -q.y, -q.z, q.w); }
        public static Matrix4x4 M(M4d m)
        {
            var e = FigConv.M(m).e; var r = new Matrix4x4();
            for (int c = 0; c < 4; c++) for (int k = 0; k < 4; k++) r[k, c] = (float)e[c * 4 + k];
            return r;
        }
        // three 网格 → Unity 网格（x 镜像、绕序反转）。skin：boneWeights（4 个影响，按权重降序）
        public static Mesh Build(FigGeo g, string name, bool skin, bool crowd = false)
        {
            int n = g.Verts;
            var v = new Vector3[n]; var nr = new Vector3[n]; var uv = new Vector2[n]; var c = new Color[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Vector3(-g.pos[i * 3], g.pos[i * 3 + 1], g.pos[i * 3 + 2]);
                nr[i] = new Vector3(-g.nrm[i * 3], g.nrm[i * 3 + 1], g.nrm[i * 3 + 2]);
                uv[i] = new Vector2(g.uv[i * 2], g.uv[i * 2 + 1]);
                c[i] = new Color(g.col[i * 4], g.col[i * 4 + 1], g.col[i * 4 + 2], g.col[i * 4 + 3]);
            }
            var m = new Mesh { name = name };
            m.indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.vertices = v; m.normals = nr; m.uv = uv; m.colors = c;
            m.triangles = FigConv.Tris(g.idx);
            if (skin && g.si != null)
            {
                if (crowd)
                {
                    // 士兵群：蒙皮骨与权重放进 uv2 / uv3（TEXCOORD2 / 3），由 Sanguo/FigureCrowd 的顶点着色器按烘焙贴图蒙皮
                    var si = new List<Vector4>(n); var sw = new List<Vector4>(n);
                    for (int i = 0; i < n; i++)
                    {
                        si.Add(new Vector4(g.si[i * 4], g.si[i * 4 + 1], g.si[i * 4 + 2], g.si[i * 4 + 3]));
                        sw.Add(new Vector4(g.sw[i * 4], g.sw[i * 4 + 1], g.sw[i * 4 + 2], g.sw[i * 4 + 3]));
                    }
                    m.SetUVs(2, si); m.SetUVs(3, sw);
                }
                else
                {
                    var bw = new BoneWeight[n];
                    var ord = new int[4];
                    for (int i = 0; i < n; i++)
                    {
                        for (int k = 0; k < 4; k++) ord[k] = k;
                        Array.Sort(ord, (a, b) => g.sw[i * 4 + b].CompareTo(g.sw[i * 4 + a]));
                        bw[i] = new BoneWeight
                        {
                            boneIndex0 = g.si[i * 4 + ord[0]], weight0 = g.sw[i * 4 + ord[0]],
                            boneIndex1 = g.si[i * 4 + ord[1]], weight1 = g.sw[i * 4 + ord[1]],
                            boneIndex2 = g.si[i * 4 + ord[2]], weight2 = g.sw[i * 4 + ord[2]],
                            boneIndex3 = g.si[i * 4 + ord[3]], weight3 = g.sw[i * 4 + ord[3]],
                        };
                    }
                    m.boneWeights = bw;
                }
            }
            m.RecalculateTangents();
            var lo = new Vector3((float)-g.bmax.x, (float)g.bmin.y, (float)g.bmin.z);
            var hi = new Vector3((float)-g.bmin.x, (float)g.bmax.y, (float)g.bmax.z);
            m.bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            return m;
        }
        internal static FigMeshes Meshes(FigTemplate t)
        {
            var u = t.unity as FigMeshes;
            if (u != null) return u;
            string nm = t.key;
            u = new FigMeshes();
            u.body = Build(t.geometry, "SG.Figure.body:" + nm, true);
            var bp = new Matrix4x4[t.boneInverses.Length];
            for (int i = 0; i < bp.Length; i++) bp[i] = M(t.boneInverses[i]);
            u.body.bindposes = bp; u.bindposes = bp;
            if (t.headGeo != null) u.head = Build(t.headGeo, "SG.Figure.head:" + nm, false);
            if (t.weaponGeo != null) u.weapon = Build(t.weaponGeo, "SG.Figure.weapon:" + nm, false);
            if (t.shieldGeo != null) u.shield = Build(t.shieldGeo, "SG.Figure.shield:" + nm, false);
            t.unity = u;
            return u;
        }
        internal static FigMeshes Meshes(FigHorseTemplate t)
        {
            var u = t.unity as FigMeshes;
            if (u != null) return u;
            u = new FigMeshes();
            u.body = Build(t.geometry, "SG.Figure.horse:" + t.key, true);
            var bp = new Matrix4x4[t.inv.Length];
            for (int i = 0; i < bp.Length; i++) bp[i] = M(t.inv[i]);
            u.body.bindposes = bp; u.bindposes = bp;
            t.unity = u;
            return u;
        }
        static bool hooked;
        internal static void Hook()
        {
            if (hooked) return;
            hooked = true;
            FigTemplates.OnFree = t => { var u = t.unity as FigMeshes; if (u != null) u.Destroy(); t.unity = null; };
            FigTemplates.OnFreeHorse = t => { var u = t.unity as FigMeshes; if (u != null) u.Destroy(); t.unity = null; };
        }
        internal static Transform[] MakeBones(Transform parent, string[] names, int[] par, V3d[] restPos)
        {
            var b = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var go = new GameObject(names[i]);
                b[i] = go.transform;
                b[i].SetParent(par[i] >= 0 ? b[par[i]] : parent, false);
                b[i].localPosition = V(restPos[i]);
            }
            return b;
        }
        internal static Transform Socket(Transform parent, string name, V3d p, Q4d q)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = V(p); t.localRotation = Q(q);
            return t;
        }
        internal static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat, bool shadow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }
    }

    // ------------------------------------------------------------------ 材质与贴图（= JS kitMaterial / teamMaterial / faceMaterial / setRim）
    public static class FigMaterials
    {
        static Shader shader, crowdShader;
        static Material kit;
        static readonly Dictionary<string, Material> team = new Dictionary<string, Material>();
        public static Color RimColor = new Color(1.0f, 0.9f, 0.78f);
        public static float RimK = 0.42f;
        public static int Count { get { return team.Count; } }
        public static Shader Shader { get { return shader ?? (shader = Art.LoadShader("Figure")); } }
        public static Shader CrowdShader { get { return crowdShader ?? (crowdShader = Art.LoadShader("FigureCrowd")); } }
        static void Globals()
        {
            Shader.SetGlobalColor("_SgRimColor", RimColor);
            Shader.SetGlobalFloat("_SgRimK", RimK);
        }
        // 各人物材质共用的参数：环境贴图（JS envMap：反射立方体贴图 + 辐照度立方体贴图，见 FigTextures.EnvCube）、强度、AO 强度
        internal static void Common(Material m)
        {
            m.SetTexture("_EnvCube", FigTextures.EnvCube());
            m.SetTexture("_EnvIrr", FigTextures.EnvIrr);
            m.SetFloat("_EnvK", 0.85f);       // JS envMapIntensity
            m.SetFloat("_AOK", 0.8f);         // JS aoMapIntensity
        }
        // 共用装具材质：图集 + 顶点色染色（势力色白 = 不染）
        public static Material Kit
        {
            get
            {
                if (kit != null) return kit;
                Globals();
                kit = new Material(Shader) { name = "SG.Figure.kit" };
                kit.SetTexture("_MainTex", FigTextures.Map);
                kit.SetTexture("_BumpMap", FigTextures.Normal);
                kit.SetTexture("_ORM", FigTextures.ORM);
                kit.SetColor("_Team", Color.white);
                Common(kit);
                team["#ffffff"] = kit;
                return kit;
            }
        }
        // 势力色材质（按颜色缓存；同一着色器，只有 _Team 不同）
        public static Material Team(string hexc)
        {
            string key = string.IsNullOrEmpty(hexc) ? "#ffffff" : FigMath.hex(hexc);
            Material m;
            if (team.TryGetValue(key, out m) && m != null) return m;
            var b = Kit;
            if (key == "#ffffff") return b;
            m = new Material(b) { name = "SG.Figure.kit" + key };
            var c = FigMath.col(key);
            m.SetColor("_Team", new Color((float)c.r, (float)c.g, (float)c.b, 1));
            team[key] = m;
            return m;
        }
        // 名将头部：面部贴图 + 平坦法线 / ORM 小贴图，同一着色器
        public static Material Face(Texture2D face)
        {
            Globals();
            var m = new Material(Shader) { name = "SG.Figure.face" };
            m.SetTexture("_MainTex", face);
            m.SetTexture("_BumpMap", FigTextures.FlatNormal);
            m.SetTexture("_ORM", FigTextures.FlatORM);
            m.SetColor("_Team", Color.white);
            Common(m);
            return m;
        }
        // 轮廓光（所有人物材质共用的全局量）
        public static void SetRim(string color, double? strength)
        {
            if (color != null) { var c = FigMath.col(color); RimColor = new Color((float)c.r, (float)c.g, (float)c.b); }
            if (strength.HasValue) RimK = (float)strength.Value;
            Globals();
        }
        public static void Clear()
        {
            foreach (var kv in team) if (kv.Value != null && kv.Value != kit) UnityEngine.Object.Destroy(kv.Value);
            team.Clear();
            if (kit != null) team["#ffffff"] = kit;
        }
    }

    // ------------------------------------------------------------------ 人物（= JS class Figure）
    public sealed class Figure
    {
        public readonly GameObject gameObject;
        public Transform Root { get { return gameObject.transform; } }
        public readonly FigTemplate Template;
        public readonly FigRig Rig;
        public readonly FigLook Look;
        public string Kind { get { return Look.kind; } }
        public string Style { get { return Rig.style; } }
        public FigWeaponInfo WeaponInfo { get { return Rig.weaponInfo; } }
        public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
        public readonly Dictionary<string, Transform> Sockets = new Dictionary<string, Transform>();
        public readonly SkinnedMeshRenderer Body;
        public MeshRenderer Head, WeaponMesh, WeaponMesh2, ShieldMesh;
        public readonly List<Renderer> Meshes = new List<Renderer>();
        public int Triangles { get; private set; }
        public int DrawCalls { get { return Meshes.Count; } }
        public FigureHorse Mounted { get; internal set; }
        public bool HasShield { get { return Rig.hasShield; } }
        public string ClipName { get { return Rig.clipName; } }
        public double ClipT { get { return Rig.clipT; } }
        // 地面贴合开关（着色器：蒙皮后的身体顶点在人物 root 局部坐标中不低于 0.006，= JS）；骑乘时自动关闭
        public bool GroundClamp { get { return Rig.groundClamp; } set { Rig.groundClamp = value; ApplyProps(); } }
        // 有披风（JS fig._hasCape；单挑画面的 hangCape 用）
        public bool HasCape { get { return Rig.HasCape; } }
        readonly Transform[] bones;
        readonly Texture2D face;
        readonly Material faceMat;
        Mesh ownWeapon;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        Color tint = Color.clear;
        bool disposed;

        internal Figure(FigTemplate tpl, string color, bool shadow)
        {
            FigUnity.Hook();
            Template = tpl; Look = tpl.look;
            Rig = new FigRig(tpl);
            var U = FigUnity.Meshes(tpl);
            gameObject = new GameObject("Figure:" + (Look.name ?? Look.type ?? ""));
            gameObject.AddComponent<FigureNode>().fig = this;
            var body = new GameObject("body");
            body.transform.SetParent(Root, false);
            bones = FigUnity.MakeBones(Root, FigBones.BONES, FigBones.PARENT, Rig.restPos);
            for (int i = 0; i < bones.Length; i++) Bones[FigBones.BONES[i]] = bones[i];
            var mat = FigMaterials.Team(Js.Or(color, Look.factionHex));
            Body = body.AddComponent<SkinnedMeshRenderer>();
            Body.sharedMesh = U.body;
            Body.bones = bones;
            Body.rootBone = Root;
            Body.quality = SkinQuality.Bone4;
            Body.updateWhenOffscreen = false;
            // 包围球（JS boundingSphere：中心 (0, k, 0)、半径 1.7k，长柄 2.3k）
            double k = tpl.pr.k, r = (Rig.style == "pole" ? 2.3 : 1.7) * k;
            Body.localBounds = new Bounds(new Vector3(0, (float)k, 0), Vector3.one * (float)(2 * r));
            Body.sharedMaterial = mat;
            Body.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            Body.receiveShadows = false;
            Meshes.Add(Body);
            // 挂点
            Sockets["gripL"] = FigUnity.Socket(Bones["handL"], "gripL", Rig.gripL.p, Rig.gripL.q);
            Sockets["gripR"] = FigUnity.Socket(Bones["handR"], "gripR", Rig.gripR.p, Rig.gripR.q);
            Sockets["shieldL"] = FigUnity.Socket(Bones["foreArmL"], "shieldL", Rig.shield.p, Rig.shield.q);
            Sockets["back"] = FigUnity.Socket(Bones["chest"], "back", Rig.backSock, Q4d.Identity);
            Sockets["headTop"] = FigUnity.Socket(Bones["head"], "headTop", Rig.headTopSock, Q4d.Identity);
            // 名将：头部、兵器、盾为独立网格
            if (U.head != null)
            {
                face = FigTextures.Face(Look.spec ?? new JObj(), tpl.faceSize);
                faceMat = FigMaterials.Face(face);
                Head = FigUnity.Part(Bones["head"], "head", U.head, faceMat, shadow);
                Meshes.Add(Head);
            }
            if (U.weapon != null)
            {
                string side = Rig.style == "bow" ? "gripL" : "gripR";
                WeaponMesh = FigUnity.Part(Sockets[side], "weapon", U.weapon, mat, shadow);
                Meshes.Add(WeaponMesh);
                if (Rig.style == "dual") { WeaponMesh2 = FigUnity.Part(Sockets["gripL"], "weapon2", U.weapon, mat, shadow); Meshes.Add(WeaponMesh2); }
            }
            if (U.shield != null) { ShieldMesh = FigUnity.Part(Sockets["shieldL"], "shield", U.shield, mat, shadow); Meshes.Add(ShieldMesh); }
            Triangles = (int)Math.Round(tpl.tris);
            FigStats.built++;
            SetPose("idle", 0);
            ApplyProps();
        }

        // ------------------------------------------------------------ 静态接口（= JS SG.Figure）
        public static Figure General(PortraitGen gen, FigOpts opts = null)
        {
            opts = opts ?? new FigOpts();
            gen = gen ?? new PortraitGen { name = "无名" };
            var look = FigBuild.lookOfGeneral(gen, opts);
            var tpl = GetTemplate(look, FigBuild.generalKey(look, opts));
            return new Figure(tpl, Js.Or(opts.color, look.factionHex), opts.shadow);
        }
        // 按姓名（当前游戏 / 剧本中查找，找不到时只用名字）
        public static Figure General(string name, FigOpts opts = null) { return General(Portrait.Find(name) ?? new PortraitGen { name = name }, opts); }
        public static Figure General(General g, FigOpts opts = null) { return General(Portrait.Describe(g), opts); }
        public static Figure Soldier(FigOpts opts)
        {
            opts = opts ?? new FigOpts();
            var look = FigBuild.lookOfSoldier(opts);
            var tpl = GetTemplate(look, FigBuild.soldierKey(look));
            return new Figure(tpl, Js.Or(opts.color, look.factionHex), opts.shadow);
        }
        public static FigureHorse Horse(FigHorseOpts opts = null) { return new FigureHorse(opts ?? new FigHorseOpts()); }
        static FigTemplate GetTemplate(FigLook look, string key)
        {
            double t0 = FigMath.Now();
            bool had = FigTemplates.Has(key);
            var t = FigTemplates.Get(look, key);
            if (!had) { FigStats.lastBuildMs = FigMath.Now() - t0; }
            return t;
        }
        // 单独的兵器网格（= JS weapon(name, opts)）：GameObject + FigureWeapon（兵器信息，Unity 局部坐标）
        public static GameObject Weapon(string name, FigOpts opts = null)
        {
            opts = opts ?? new FigOpts();
            var lod = FigLod.Of(opts.lod ?? "high");
            var gb = new FigGB();
            var info = FigBuild.buildWeaponInto(gb, FigBuild.HasWeapon(name) ? name : "jian", lod, FigBones.W1(0));
            var mesh = FigUnity.Build(gb.build(false), "SG.Figure.weapon:" + info.id, false);
            var r = FigUnity.Part(null, "Weapon:" + info.id, mesh, FigMaterials.Team(opts.color), opts.shadow);
            var w = r.gameObject.AddComponent<FigureWeapon>();
            w.Set(info, gb.tris, mesh);
            return r.gameObject;
        }
        public static GameObject Shield(string name, FigOpts opts = null)
        {
            opts = opts ?? new FigOpts();
            var lod = FigLod.Of(opts.lod ?? "high");
            var gb = new FigGB();
            FigBuild.buildShieldInto(gb, name ?? "round", lod, FigMath.TEAM, FigBones.W1(0));
            var mesh = FigUnity.Build(gb.build(false), "SG.Figure.shield:" + (name ?? "round"), false);
            var r = FigUnity.Part(null, "Shield:" + (name ?? "round"), mesh, FigMaterials.Team(opts.color), opts.shadow);
            var w = r.gameObject.AddComponent<FigureWeapon>();
            w.Set(null, gb.tris, mesh);
            return r.gameObject;
        }
        public static Material TeamMaterial(string hexc) { return FigMaterials.Team(hexc); }
        public static Material Material { get { return FigMaterials.Kit; } }
        public static void SetRim(string color, double? strength = null) { FigMaterials.SetRim(color, strength); }
        public static FigLook LookOf(PortraitGen gen, FigOpts opts = null) { return FigBuild.lookOfGeneral(gen, opts ?? new FigOpts()); }
        public static FigLook SoldierLook(FigOpts opts = null) { return FigBuild.lookOfSoldier(opts ?? new FigOpts()); }
        public static Dictionary<string, FigClipInfo> Clips { get { return FigClips.Catalog; } }
        public static FigClipInfo HorseClip(string name) { return FigHorseRig.ClipInfo(name); }
        public static List<string> HorseClips { get { return new List<string>(FigTables.HCLIPS.Keys); } }
        // 士兵群的片段：步兵（= JS crowdClips）与骑兵组合（= JS crowdMountClips）
        public static List<string> CrowdClips { get { var l = new List<string>(); foreach (var o in FigTables.CROWD_CLIPS) l.Add((string)o); return l; } }
        public static List<string> CrowdMountClips { get { return new List<string>(FigTables.MOUNT_CLIPS.Keys); } }
        public static string[] BoneNames { get { return (string[])FigBones.BONES.Clone(); } }
        public static string[] HorseBoneNames { get { return (string[])FigBones.HBONES.Clone(); } }
        public static readonly string[] SoldierTypes = { "spear", "sword", "archer", "cavalry" };
        public static List<string> Cultures { get { var l = new List<string>(); foreach (var o in FigTables.CULTURES) l.Add((string)o); return l; } }
        // 兵器表：id、名称、流派
        public static List<string[]> Weapons { get { var l = new List<string[]>(); foreach (var kv in FigTables.WEAPONS) { var o = (JObj)kv.Value; l.Add(new[] { kv.Key, o.S("name"), o.S("style") }); } return l; } }
        // 网页版世界坐标 / 朝向 → Unity
        public static Vector3 PosFromJs(double x, double y, double z) { return new Vector3((float)x, (float)y, (float)-z); }
        public static float YawFromJs(double rad) { return (float)(180 - rad * 180 / Math.PI); }
        // 环境贴图：人物材质自带（_EnvCube / _EnvIrr，= JS 材质的 envMap），不需要调用本函数。
        // 可选：把同一张反射立方体贴图设为场景的自定义反射（RenderSettings.customReflection，给场景里其他用 unity_SpecCube0 的着色器）
        public static Cubemap InstallEnvironment()
        {
            var c = FigTextures.EnvCube();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflection = c;
            RenderSettings.reflectionIntensity = 1;
            return c;
        }
        // 释放无人使用的模板网格（士兵模板常驻）
        public static void ClearCache() { FigUnity.Hook(); FigTemplates.Clear(); }
        public static FigStatsSnapshot Stats()
        {
            return new FigStatsSnapshot
            {
                built = FigStats.built, templates = FigStats.templates, textures = FigStats.textures, atlasMs = FigStats.atlasMs, envMs = FigStats.envMs,
                buildMs = FigStats.buildMs, lastBuildMs = FigStats.lastBuildMs, bakeMs = FigStats.bakeMs, preloadMs = FigStats.preloadMs,
                maxStepMs = FigStats.maxStepMs, maxSliceMs = FigStats.maxSliceMs, maxStepJob = FigStats.maxStepJob,
                tris = new Dictionary<string, int>(FigStats.tris), templateCount = FigTemplates.Count, horseTemplates = FigTemplates.HorseCount,
                faces = FigTextures.FaceCount, materials = FigMaterials.Count,
            };
        }

        // 预生成（= JS preload + runJobs）：图集、环境贴图、士兵模板、名将模板与面部贴图、士兵群烘焙。协程，每帧约 budget 毫秒
        public static IEnumerator Preload(FigPreload o = null)
        {
            o = o ?? new FigPreload();
            FigUnity.Hook();
            var cultures = o.cultures ?? Cultures.ToArray();
            var types = o.types ?? SoldierTypes;
            double t0 = FigMath.Now();
            var jobs = new List<KeyValuePair<string, IEnumerator>>();
            if (!FigTextures.AtlasReady) jobs.Add(Job("atlas", FigAtlas.Generate().GetEnumerator()));
            if (!FigTextures.EnvReady) jobs.Add(Job("env", FigTextures.EnvGen().GetEnumerator()));
            jobs.Add(Job("textures", Once(() => { FigTextures.EnsureAtlas(); var _ = FigMaterials.Kit; var __ = FigTextures.FlatNormal; var ___ = FigTextures.FlatORM; })));
            foreach (var lod in o.lods) foreach (var c in cultures) foreach (var ty in types) foreach (var sd in o.seeds)
            {
                string l = lod, cc = c, tt = ty; int s = sd;
                jobs.Add(Job("soldier " + c + "/" + ty, Once(() =>
                {
                    var look = FigBuild.lookOfSoldier(new FigOpts { type = tt, culture = cc, lod = l, seed = s });
                    var tpl = GetTemplate(look, FigBuild.soldierKey(look));
                    FigUnity.Meshes(tpl);
                    FigTemplates.Release(tpl);
                })));
            }
            if (o.generals != null)
                foreach (var g in o.generals)
                {
                    var gg = g;
                    jobs.Add(Job("general " + g.name, Once(() => { var f = General(gg, new FigOpts { lod = o.generalLod }); f.Dispose(); })));
                }
            var specs = new List<FigCrowdSpec>();
            if (o.crowd != null) specs.AddRange(o.crowd);
            else if (o.crowdAll) foreach (var c in cultures) foreach (var ty in types) specs.Add(new FigCrowdSpec { type = ty, culture = c, seed = 0, mounted = ty == "cavalry" });
            foreach (var sp in specs) jobs.Add(Job("crowd " + sp.culture + "/" + sp.type + (sp.mounted ? "+horse" : ""), FigureCrowd.BakeGen(sp, o.crowdLod).GetEnumerator()));
            foreach (var _ in RunJobs(jobs, o.budget)) yield return null;
            FigStats.preloadMs = Math.Round(FigMath.Now() - t0);
        }
        static KeyValuePair<string, IEnumerator> Job(string name, IEnumerator e) { return new KeyValuePair<string, IEnumerator>(name, e); }
        static IEnumerator Once(Action a) { a(); yield break; }
        // 分片执行：每帧跑到超过 budget 毫秒为止（= JS runJobs）
        internal static IEnumerable RunJobs(List<KeyValuePair<string, IEnumerator>> jobs, double budget)
        {
            int k = 0;
            while (k < jobs.Count)
            {
                double s0 = FigMath.Now();
                while (k < jobs.Count && FigMath.Now() - s0 < budget)
                {
                    double j0 = FigMath.Now();
                    if (!jobs[k].Value.MoveNext()) k++;
                    double jm = FigMath.Now() - j0;
                    if (jm > FigStats.maxStepMs) { FigStats.maxStepMs = Math.Round(jm); FigStats.maxStepJob = k < jobs.Count ? jobs[k].Key : "?"; }
                }
                FigStats.maxSliceMs = Math.Max(FigStats.maxSliceMs, Math.Round(FigMath.Now() - s0));
                if (k < jobs.Count) yield return null;
            }
        }

        // ------------------------------------------------------------ 姿势
        public FigClipInfo ClipInfo(string name) { return FigClips.Info(name); }
        // 片段 clip 的时刻 t（秒）；opts：lower / lowerT / blend / ik / gallop / clamp（见 FigPoseOpts）
        public Figure SetPose(string clip, double t, FigPoseOpts opts = null)
        {
            Rig.SetPose(clip, t, opts);
            Sync();
            return this;
        }
        // a 与 b 两个片段按 w 混合（w = b 的权重）
        public Figure BlendPose(string a, double ta, string b, double tb, double w) { Rig.BlendPose(a, ta, b, tb, w); Sync(); return this; }
        // 披风、盔缨、发的弹簧随动（每帧一次，在 SetPose 之后）。与 JS update 相同：dt ≤ 0（如单挑的定格帧）时不动，
        // 否则只写 6 根弹簧骨的旋转——SetPose 之后对其他骨骼 Transform 的改动（交叉淡化、程序化叠加）保留
        public Figure Update(float dt)
        {
            if (!(dt > 0)) return this;
            Rig.Update(dt);
            foreach (int b in SPRING) bones[b].localRotation = FigUnity.Q(Rig.quat[b]);
            return this;
        }
        // 直接设定右手兵器：pos（root 局部，Unity）、方向 dir（root 局部单位向量）或兵器朝向 rot（root 局部）、横滚 roll（度）
        public Figure Grip(Vector3 pos, Vector3? dir = null, Quaternion? rot = null, float roll = 0)
        {
            ReadBack();
            Rig.Grip(FigUnity.ToJs(pos), dir.HasValue ? FigUnity.ToJs(dir.Value) : (V3d?)null, rot.HasValue ? FigUnity.ToJs(rot.Value) : (Q4d?)null, roll);
            foreach (int b in ARMS) bones[b].localRotation = FigUnity.Q(Rig.quat[b]);
            return this;
        }
        // 双骨 IK：side 'L' / 'R' 手腕到 pos（root 局部，Unity），rot 为手的朝向（root 局部；缺省保持当前）
        public Figure IkHand(string side, Vector3 pos, Quaternion? rot = null)
        {
            ReadBack();
            Rig.IkHand(side, FigUnity.ToJs(pos), rot.HasValue ? FigUnity.ToJs(rot.Value) : (Q4d?)null);
            foreach (int b in ARMS) bones[b].localRotation = FigUnity.Q(Rig.quat[b]);
            return this;
        }
        static readonly int[] SPRING = { FigBones.cape1, FigBones.cape2, FigBones.cape3, FigBones.plume1, FigBones.plume2, FigBones.hair1 };
        static readonly int[] ARMS = { FigBones.upperArmL, FigBones.foreArmL, FigBones.handL, FigBones.upperArmR, FigBones.foreArmR, FigBones.handR };
        // 骨骼局部变换 → Transform（SetPose / BlendPose：JS 的 _apply 重写全部骨骼）
        void Sync()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = FigUnity.V(Rig.pos[i]);
                bones[i].localRotation = FigUnity.Q(Rig.quat[i]);
            }
        }
        // Transform → 骨骼局部变换：JS 的状态就是骨骼本身（_fk 直接读 bone.quaternion / position），
        // 所以 Grip / IkHand 之前把画面代码对 Transform 的改动读回骨架，IK 的正向运动学与 JS 一致。
        // 只读回确实被改过的骨骼（没改过的保持双精度的原值，不引入单精度误差）
        void ReadBack()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                var p = bones[i].localPosition; var q = bones[i].localRotation;
                if ((p - FigUnity.V(Rig.pos[i])).sqrMagnitude > 1e-12f) Rig.pos[i] = FigUnity.ToJs(p);
                var r = FigUnity.Q(Rig.quat[i]);
                if (Math.Abs(q.x - r.x) + Math.Abs(q.y - r.y) + Math.Abs(q.z - r.z) + Math.Abs(q.w - r.w) > 1e-6f) Rig.quat[i] = FigUnity.ToJs(q);
            }
        }

        // ------------------------------------------------------------ 挂接
        // where：挂点名（gripL gripR shieldL back headTop）或骨骼名；缺省挂在 root
        public Transform Attach(Transform obj, string where = null)
        {
            Transform t = null;
            if (where != null && !Sockets.TryGetValue(where, out t)) Bones.TryGetValue(where, out t);
            obj.SetParent(t ?? Root, false);
            return obj;
        }
        public Transform Detach(Transform obj) { if (obj != null) obj.SetParent(null, true); return obj; }
        // 换兵器（名将；士兵不换）
        public Figure SetWeapon(string name)
        {
            if (!FigBuild.HasWeapon(name) || Kind == "soldier") return this;
            foreach (var r in new[] { WeaponMesh, WeaponMesh2 }) if (r != null) { Meshes.Remove(r); UnityEngine.Object.Destroy(r.gameObject); }
            WeaponMesh = WeaponMesh2 = null;
            if (ownWeapon != null) UnityEngine.Object.Destroy(ownWeapon);
            var gb = new FigGB();
            var lod = Look.lodP ?? FigLod.Of(Template.lod);
            var info = FigBuild.buildWeaponInto(gb, name, lod, FigBones.W1(0));
            ownWeapon = FigUnity.Build(gb.build(false), "SG.Figure.weapon:" + name, false);
            string style = FigBuild.WeaponStyle(name);
            Rig.SetWeapon(info, style);
            var mat = Body.sharedMaterial;
            bool sh = Body.shadowCastingMode != ShadowCastingMode.Off;
            WeaponMesh = FigUnity.Part(Sockets[style == "bow" ? "gripL" : "gripR"], "weapon", ownWeapon, mat, sh);
            Meshes.Insert(Head != null ? 2 : 1, WeaponMesh);
            if (style == "dual") { WeaponMesh2 = FigUnity.Part(Sockets["gripL"], "weapon2", ownWeapon, mat, sh); Meshes.Add(WeaponMesh2); }
            SetPose(Rig.hasClip ? Rig.clipName : "idle", Rig.hasClip ? Rig.clipT : 0);
            ApplyProps();
            return this;
        }
        // 兵器尖端 / 根部的世界坐标
        public Vector3 WeaponTip()
        {
            var wi = WeaponInfo; var tip = wi != null ? wi.tip : new V3d(0, 1, 0);
            var t = WeaponMesh != null ? WeaponMesh.transform : Sockets[Style == "bow" ? "gripL" : "gripR"];
            return t.TransformPoint(FigUnity.V(tip));
        }
        public Vector3 WeaponBase()
        {
            var wi = WeaponInfo; var b = wi != null ? wi.@base : new V3d(0, 0.1, 0);
            var t = WeaponMesh != null ? WeaponMesh.transform : Sockets[Style == "bow" ? "gripL" : "gripR"];
            return t.TransformPoint(FigUnity.V(b));
        }
        // 受击 / 格开 / 反击的着色：以轮廓为主的辉光（颜色 × k；k = 0 关闭）
        public Figure SetTint(string hexc, float k)
        {
            var c = FigMath.col(hexc ?? "#ffffff");
            tint = new Color((float)c.r * k, (float)c.g * k, (float)c.b * k, 0);
            ApplyProps();
            return this;
        }
        // 改动时（受击着色、地面贴合开关、骑乘 / 下马、换兵器）写材质属性块：_Tint、_SgGround.x（只开在身体上）。
        // 贴合高度是常数（root 局部 0.006，在着色器里），所以不需要每帧更新
        internal void ApplyProps()
        {
            if (disposed) return;
            bool g = Rig.groundClamp && Mounted == null;
            foreach (var r in Meshes)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_Tint", tint);
                mpb.SetVector("_SgGround", new Vector4(g && r == Body ? 1 : 0, 0, 0, 0));
                r.SetPropertyBlock(mpb);
            }
        }
        // 投影开关
        public Figure SetShadow(bool on) { foreach (var r in Meshes) if (r != null) r.shadowCastingMode = on ? ShadowCastingMode.On : ShadowCastingMode.Off; return this; }

        public bool Disposed { get { return disposed; } }
        public void Dispose() { Release(true); }
        internal void Release(bool destroy)
        {
            if (disposed) return;
            disposed = true;
            if (Mounted != null) Mounted.Unlink(this);          // 只解除骑乘关系，不改层级（自身随后销毁 / 正在销毁）
            if (faceMat != null) UnityEngine.Object.Destroy(faceMat);
            if (face != null) FigTextures.ReleaseFace(face);
            if (ownWeapon != null) UnityEngine.Object.Destroy(ownWeapon);
            FigTemplates.Release(Template);
            if (destroy && gameObject != null) UnityEngine.Object.Destroy(gameObject);
        }
    }

    // 单独兵器 / 盾的信息组件（JS mesh.userData.weapon / tris）；销毁时释放网格
    public sealed class FigureWeapon : MonoBehaviour
    {
        public string id, weaponName, style;
        public float len;
        public Vector3 tip, @base;              // Unity 局部坐标
        public Vector3? off;
        public int tris;
        Mesh mesh;
        internal void Set(FigWeaponInfo info, int tris, Mesh m)
        {
            this.tris = tris; mesh = m;
            if (info == null) return;
            id = info.id; weaponName = info.name; style = info.style; len = (float)info.len;
            tip = FigUnity.V(info.tip); @base = FigUnity.V(info.@base);
            off = info.off.HasValue ? FigUnity.V(info.off.Value) : (Vector3?)null;
        }
        void OnDestroy() { if (mesh != null) Destroy(mesh); }
    }

    // ------------------------------------------------------------------ 战马（= JS class Horse）
    public sealed class FigureHorse
    {
        public readonly GameObject gameObject;
        public Transform Root { get { return gameObject.transform; } }
        public readonly FigHorseTemplate Template;
        public readonly FigHorseRig Rig;
        public readonly Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
        public readonly Dictionary<string, Transform> Sockets = new Dictionary<string, Transform>();
        public readonly SkinnedMeshRenderer Body;
        public readonly List<Figure> Riders = new List<Figure>();
        public int Triangles { get; private set; }
        public int DrawCalls { get { return 1; } }
        public string ClipName { get { return Rig.clipName; } }
        readonly Transform[] bones;
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        bool disposed;

        internal FigureHorse(FigHorseOpts opts)
        {
            FigUnity.Hook();
            var o = opts.Clone();
            if (string.IsNullOrEmpty(o.lod)) o.lod = "mid";
            Template = FigTemplates.GetHorse(o);
            Rig = new FigHorseRig(Template);
            var U = FigUnity.Meshes(Template);
            gameObject = new GameObject("Horse");
            gameObject.AddComponent<FigureNode>().horse = this;
            var body = new GameObject("body");
            body.transform.SetParent(Root, false);
            bones = FigUnity.MakeBones(Root, FigBones.HBONES, FigBones.HPARENT, Rig.restPos);
            for (int i = 0; i < bones.Length; i++) Bones[FigBones.HBONES[i]] = bones[i];
            Sockets["saddle"] = Bones["saddle"];
            Body = body.AddComponent<SkinnedMeshRenderer>();
            Body.sharedMesh = U.body;
            Body.bones = bones;
            Body.rootBone = Root;
            Body.quality = SkinQuality.Bone4;
            Body.updateWhenOffscreen = false;
            Body.localBounds = new Bounds(new Vector3(0, 1.1f, 0), Vector3.one * 3.8f);     // JS 包围球 (0, 1.1, 0) r 1.9
            Body.sharedMaterial = FigMaterials.Team(Js.Or(o.color, "#8a2a24"));
            Body.shadowCastingMode = o.shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            Body.receiveShadows = false;
            Triangles = (int)Template.tris;
            FigStats.built++;
            SetPose("idle", 0);
        }
        public FigClipInfo ClipInfo(string n) { return FigHorseRig.ClipInfo(n); }
        // 片段：idle walk trot gallop rear die；骑手随之同步（疾驰时下半身用 gallop）
        public FigureHorse SetPose(string name, double t)
        {
            Rig.SetPose(name, t);
            Sync();
            foreach (var f in Riders) if (f.Rig.hasClip) f.SetPose(f.ClipName, f.ClipT, new FigPoseOpts { gallop = name == "gallop" });
            return this;
        }
        public FigureHorse Update(float dt) { return this; }
        void Sync()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = FigUnity.V(Rig.pos[i]);
                bones[i].localRotation = FigUnity.Q(Rig.quat[i]);
            }
        }
        public FigureHorse Mount(Figure fig)
        {
            if (fig == null || Riders.Contains(fig)) return this;
            fig.Root.SetParent(Bones["saddle"], false);
            fig.Root.localPosition = FigUnity.V(FigHorseRig.RiderOffset(fig.Rig));
            fig.Root.localRotation = Quaternion.identity;
            fig.Mounted = this; fig.Rig.mounted = Rig;
            Riders.Add(fig); Rig.riders.Add(fig.Rig);
            fig.SetPose(fig.Rig.hasClip ? fig.ClipName : "idle", fig.Rig.hasClip ? fig.ClipT : 0);
            fig.ApplyProps();
            return this;
        }
        // 下马：骑手离开鞍座，挂到战马的父节点下（保持世界位置；仍在本画面的层级里，随画面根节点一起销毁）。
        // JS 的 dismount 把骑手从场景图里拿掉（不再显示）；Unity 没有「不在场景里」的 GameObject，需要时由调用方隐藏或重新挂接
        public FigureHorse Dismount(Figure fig)
        {
            if (fig == null) return this;
            bool on = fig.gameObject != null && gameObject != null && fig.Root.parent == Bones["saddle"];
            Unlink(fig);
            if (on) fig.Root.SetParent(Root.parent, true);
            fig.ApplyProps();
            return this;
        }
        // 只解除骑乘关系（销毁途中用：不改层级）
        internal void Unlink(Figure fig)
        {
            int i = Riders.IndexOf(fig);
            if (i >= 0) { Riders.RemoveAt(i); Rig.riders.Remove(fig.Rig); }
            if (fig.Mounted == this) { fig.Mounted = null; fig.Rig.mounted = null; }
        }
        public Transform Attach(Transform obj, string where = null)
        {
            Transform t = null;
            if (where != null && !Sockets.TryGetValue(where, out t)) Bones.TryGetValue(where, out t);
            obj.SetParent(t ?? Root, false);
            return obj;
        }
        public Transform Detach(Transform obj) { if (obj != null) obj.SetParent(null, true); return obj; }
        public FigureHorse SetTint(string hexc, float k)
        {
            var c = FigMath.col(hexc ?? "#ffffff");
            Body.GetPropertyBlock(mpb);
            mpb.SetColor("_Tint", new Color((float)c.r * k, (float)c.g * k, (float)c.b * k, 0));
            Body.SetPropertyBlock(mpb);
            return this;
        }
        // 释放：先让骑手下马（留在战马的父节点下，见 Dismount）；GameObject 已在销毁途中（destroy = false，FigureNode.OnDestroy）时
        // 只解除关系——骑手是鞍座的子节点，随之销毁并各自释放
        public void Dispose() { Release(true); }
        internal void Release(bool destroy)
        {
            if (disposed) return;
            disposed = true;
            foreach (var f in Riders.ToArray()) { if (destroy) Dismount(f); else Unlink(f); }
            FigTemplates.ReleaseHorse(Template);
            if (destroy && gameObject != null) UnityEngine.Object.Destroy(gameObject);
        }
    }
}
#endif
