// 人物模型库 Unity 接口（View/Figure.cs、FigureCrowd.cs、FigureAtlas.cs 的 Unity 部分）的冒烟测试与换算检查：
// 与 UnityEngine 替身（FigureUnityStub.cs）一起编译，真正走一遍 GameObject / Transform 层级、SkinnedMeshRenderer 的
// bones / bindposes / boneWeights、材质属性块、实例化绘制，并与纯 C# 的骨架（FigRig，three 坐标）逐项对照：
//   · 骨骼 Transform 的世界位置 = FigRig 骨骼矩阵经换手系（x 镜像）；人物 root 摆在任意位置 / 朝向时 = three 世界坐标 (x, y, −z)
//   · Unity 的蒙皮公式 Σ w·(bone.localToWorldMatrix × bindpose)·v = three 蒙皮结果的镜像（验证 bindposes 与骨骼局部变换）
//   · WeaponTip / WeaponBase、Grip / IkHand 的参数换算、骑乘（鞍座挂点、战马同步骑手）、换兵器、受击着色、地面贴合开关
//   · 模板 / 面部贴图的引用计数与释放、Weapon / Shield、士兵群（分组、超过 1023 的分批、移除后的交换、烘焙贴图）、
//     Preload 协程的分片、环境立方体贴图
// 用法：由 Tests~/figure/unity.sh 编译运行；退出码 = 失败项数
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sanguo;
using UnityEngine;

static class FigureUnityHarness
{
    static int fails, checks;
    static void Check(bool ok, string what) { checks++; if (!ok) { fails++; Console.WriteLine("  ✗ " + what); } }
    static Vector3 Mir(V3d p) { return new Vector3((float)-p.x, (float)p.y, (float)p.z); }
    static V3d Pos(M4d m) { return new V3d(m.e[12], m.e[13], m.e[14]); }
    static V3d Mul(M4d m, V3d p) { var e = m.e; return new V3d(e[0] * p.x + e[4] * p.y + e[8] * p.z + e[12], e[1] * p.x + e[5] * p.y + e[9] * p.z + e[13], e[2] * p.x + e[6] * p.y + e[10] * p.z + e[14]); }
    static string F(double v) { return v.ToString("E2", CultureInfo.InvariantCulture); }

    // three 世界：root 位置 (x, 0, z)、绕 Y 转 θ
    static M4d RootThree(double x, double z, double th)
    {
        var m = M4d.Translation(x, 0, z); var r = new M4d(); double c = Math.Cos(th), s = Math.Sin(th);
        r.e[0] = c; r.e[2] = -s; r.e[8] = s; r.e[10] = c;
        return M4d.Mul(m, r);
    }
    // 骨骼 Transform 的世界位置 vs FigRig（three）经 root 变换后的 (x, y, −z)
    static string worstBone;
    static double BoneErr(Figure f, M4d root3)
    {
        var B = f.Rig.BoneMatrices(); double mx = 0;
        for (int i = 0; i < B.Length; i++)
        {
            var w = Pos(M4d.Mul(root3, B[i]));
            var u = f.Bones[FigBones.BONES[i]].position;
            double d = Math.Max(Math.Abs(u.x - w.x), Math.Max(Math.Abs(u.y - w.y), Math.Abs(u.z + w.z)));
            if (d > mx) { mx = d; worstBone = FigBones.BONES[i] + " Unity " + u + " three " + w.x.ToString("F3") + "," + w.y.ToString("F3") + "," + w.z.ToString("F3"); }
        }
        return mx;
    }
    // Unity 蒙皮（替身矩阵，单精度）vs three 蒙皮的镜像
    static double SkinErr(Figure f)
    {
        var mesh = f.Body.sharedMesh; var g = f.Template.geometry;
        var B = f.Rig.BoneMatrices(); var inv = f.Template.boneInverses;
        var K = new Matrix4x4[B.Length];
        for (int i = 0; i < B.Length; i++) K[i] = f.Body.bones[i].localToWorldMatrix * mesh.bindposes[i];
        double mx = 0;
        for (int v = 0; v < mesh.vertices.Length; v += 7)
        {
            var bw = mesh.boneWeights[v];
            var p = mesh.vertices[v];
            var u = K[bw.boneIndex0].MultiplyPoint3x4(p) * bw.weight0 + K[bw.boneIndex1].MultiplyPoint3x4(p) * bw.weight1 + K[bw.boneIndex2].MultiplyPoint3x4(p) * bw.weight2 + K[bw.boneIndex3].MultiplyPoint3x4(p) * bw.weight3;
            var p3 = new V3d(g.pos[v * 3], g.pos[v * 3 + 1], g.pos[v * 3 + 2]); var t = new V3d(0, 0, 0);
            for (int k = 0; k < 4; k++) { double w = g.sw[v * 4 + k]; if (w == 0) continue; var q = Mul(M4d.Mul(B[g.si[v * 4 + k]], inv[g.si[v * 4 + k]]), p3); t = new V3d(t.x + q.x * w, t.y + q.y * w, t.z + q.z * w); }
            mx = Math.Max(mx, Math.Max(Math.Abs(u.x + t.x), Math.Max(Math.Abs(u.y - t.y), Math.Abs(u.z - t.z))));
        }
        return mx;
    }
    static void Drain(IEnumerator e) { while (e.MoveNext()) { } }
    static bool QE(Quaternion a, Quaternion b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }

    static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        double t0 = FigMath.Now();
        // ---- 名将
        var f = Figure.General("关羽", new FigOpts { lod = "high" });
        var mesh = f.Body.sharedMesh;
        Check(f.Body.bones.Length == FigBones.Count && mesh.bindposes.Length == FigBones.Count, "骨骼数 / bindposes 数");
        Check(mesh.boneWeights.Length == mesh.vertices.Length && mesh.normals.Length == mesh.vertices.Length && mesh.uv.Length == mesh.vertices.Length && mesh.colors.Length == mesh.vertices.Length, "顶点属性长度");
        bool wOk = true;
        foreach (var bw in mesh.boneWeights)
        {
            float s = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
            if (Math.Abs(s - 1) > 1e-4 || bw.weight0 < bw.weight1 || bw.weight1 < bw.weight2 || bw.weight2 < bw.weight3 || bw.boneIndex0 >= FigBones.Count || bw.boneIndex3 >= FigBones.Count) wOk = false;
        }
        Check(wOk, "boneWeights：和为 1、降序、下标在范围内");
        int tri = 0; var seen = new HashSet<Mesh>();
        foreach (var r in f.Meshes) { var m = r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh : r.GetComponent<MeshFilter>().sharedMesh; if (seen.Add(m)) tri += m.triangles.Length / 3; }
        Check(tri == (int)f.Template.tris && f.Triangles == (int)Math.Round(f.Template.tris), "三角形数 " + tri + " / " + f.Template.tris);
        Check(f.Head != null && f.WeaponMesh != null && f.Sockets.Count == 5 && f.Bones.Count == FigBones.Count, "头 / 兵器 / 挂点");
        Check(f.Head.sharedMaterial != null && f.Head.sharedMaterial.GetTexture("_MainTex") is Texture2D && ((Texture2D)f.Head.sharedMaterial.GetTexture("_MainTex")).width == f.Template.faceSize, "面部贴图 " + f.Template.faceSize);
        // 姿势：骨骼 Transform 与蒙皮
        double be = 0, se = 0;
        foreach (var clip in new[] { "idle", "light1", "heavy", "run", "knockdown", "block", "parry", "die" })
        {
            var info = FigClips.Info(clip); if (info == null) continue;
            f.SetPose(clip, info.dur * 0.5);
            be = Math.Max(be, BoneErr(f, RootThree(0, 0, Math.PI)));
            se = Math.Max(se, SkinErr(f));
        }
        Check(be < 1e-4, "骨骼世界位置（Unity root 在原点、无旋转 = JS 朝向 π）最大差 " + F(be) + "（" + worstBone + "）");
        Check(se < 1e-3, "Unity 蒙皮公式 vs three 蒙皮（镜像）最大差 " + F(se));
        // root 摆在任意位置 / 朝向：Unity (x, y, −z)、localEulerAngles.y = 180 − θ
        f.Root.localPosition = Figure.PosFromJs(1.5, 0, 2.5); f.Root.localRotation = Quaternion.Euler(0, Figure.YawFromJs(0.7), 0);
        f.SetPose("light2", 0.3);
        double re = BoneErr(f, RootThree(1.5, 2.5, 0.7));
        Check(re < 1e-4, "root 位置 / 朝向换算最大差 " + F(re));
        // 兵器尖端 / 根部
        {
            var B = f.Rig.BoneMatrices(); var root3 = RootThree(1.5, 2.5, 0.7);
            var g = M4d.Mul(M4d.Mul(root3, B[FigBones.handR]), M4d.Compose(f.Rig.gripR.p, f.Rig.gripR.q, new V3d(1, 1, 1)));
            var tip = Mul(g, f.WeaponInfo.tip); var bas = Mul(g, f.WeaponInfo.@base);
            var ut = f.WeaponTip(); var ub = f.WeaponBase();
            double d = Math.Max(Math.Abs(ut.x - tip.x) + Math.Abs(ut.y - tip.y) + Math.Abs(ut.z + tip.z), Math.Abs(ub.x - bas.x) + Math.Abs(ub.y - bas.y) + Math.Abs(ub.z + bas.z));
            Check(d < 1e-4, "WeaponTip / WeaponBase 最大差 " + F(d));
        }
        f.Root.localPosition = Vector3.zero; f.Root.localRotation = Quaternion.identity;
        // Grip / IkHand：Unity 参数（root 局部）换算后与 FigRig 直接调用相同
        {
            var r2 = new FigRig(f.Template);
            f.SetPose("idle", 0.4); r2.SetPose("idle", 0.4, null);
            var p = new Vector3(0.25f, 1.3f, 0.35f); var dir = new Vector3(-0.3f, 0.8f, 0.5f);
            f.Grip(p, dir, null, 15);
            r2.Grip(new V3d(-0.25, 1.3, 0.35), new V3d(0.3, 0.8, 0.5), null, 15);
            var q = Quaternion.Euler(20, 30, 10);
            f.IkHand("L", new Vector3(-0.3f, 1.1f, 0.3f), q);
            r2.IkHand("L", new V3d(0.3, 1.1, 0.3), new Q4d(q.x, -q.y, -q.z, q.w));
            var A = f.Rig.BoneMatrices(); var Bm = r2.BoneMatrices(); double d = 0;
            for (int i = 0; i < A.Length; i++) for (int k = 0; k < 16; k++) d = Math.Max(d, Math.Abs(A[i].e[k] - Bm[i].e[k]));
            Check(d < 1e-5, "Grip / IkHand 参数换算最大差 " + F(d) + "（单精度参数）");
            Check(BoneErr(f, RootThree(0, 0, Math.PI)) < 1e-4, "Grip / IkHand 后骨骼 Transform 同步");
        }
        // SetPose 之后对骨骼 Transform 的改动（单挑的交叉淡化、重击后仰、被格开的前臂抖动）：Update 只写 6 根弹簧骨、dt ≤ 0 不写（= JS update）
        {
            var fu = Figure.General("关羽", new FigOpts { lod = "high" });      // 另一个人物（弹簧状态不影响后面的对照）
            fu.SetPose("light1", 0.2);
            var sp = fu.Bones["spine"]; var fa = fu.Bones["foreArmR"]; var cp1 = fu.Bones["cape1"];
            var edit = sp.localRotation * Quaternion.Euler(-6, 0, 0); var editF = fa.localRotation * Quaternion.Euler(0, 0, 3);
            sp.localRotation = edit; fa.localRotation = editF;
            var c1a = cp1.localRotation;
            fu.Update(0);
            Check(QE(sp.localRotation, edit) && QE(fa.localRotation, editF) && QE(cp1.localRotation, c1a), "Update(0)：不改任何骨骼");
            fu.Update(1 / 60f);
            Check(QE(sp.localRotation, edit) && QE(fa.localRotation, editF), "Update(dt)：保留 SetPose 之后对非弹簧骨的改动");
            Check(QE(cp1.localRotation, FigUnity.Q(fu.Rig.quat[FigBones.cape1])) && QE(fu.Bones["hair1"].localRotation, FigUnity.Q(fu.Rig.quat[FigBones.hair1])), "Update(dt)：弹簧骨按骨架写入");
            // Grip / IkHand 读回改过的骨骼（JS 的 FK 直接读骨骼）：与在纯骨架上做同样改动的结果相同；只写双臂
            var r2 = new FigRig(fu.Template); r2.SetPose("light1", 0.2, null);
            r2.Update(1 / 60.0);
            r2.quat[FigBones.spine] = FigUnity.ToJs(edit); r2.quat[FigBones.foreArmR] = FigUnity.ToJs(editF);
            var hd = fu.Bones["head"].localRotation;
            fu.Grip(new Vector3(0.2f, 1.25f, 0.4f), new Vector3(0, 0.6f, 0.8f), null, 0);
            r2.Grip(new V3d(-0.2, 1.25, 0.4), new V3d(0, 0.6, 0.8), null, 0);
            double d = 0; foreach (var bn in new[] { "upperArmL", "foreArmL", "handL", "upperArmR", "foreArmR", "handR" }) { int i = FigBones.B(bn); var q = FigUnity.Q(r2.quat[i]); var u = fu.Bones[bn].localRotation; d = Math.Max(d, Math.Abs(q.x - u.x) + Math.Abs(q.y - u.y) + Math.Abs(q.z - u.z) + Math.Abs(q.w - u.w)); }
            Check(d < 1e-5, "Grip：先读回改过的脊柱再解 IK，双臂与纯骨架相同（最大差 " + F(d) + "）");
            Check(QE(sp.localRotation, edit) && QE(fu.Bones["head"].localRotation, hd), "Grip：不改双臂以外的骨骼");
            Check(fu.HasCape == fu.Template.look.C.cape.HasValue, "HasCape");
            fu.SetPose("light1", 0.2);
            Check(BoneErr(fu, RootThree(0, 0, Math.PI)) < 1e-4, "下一次 SetPose 重写全部骨骼");
            fu.Dispose();
        }
        // 受击着色与地面贴合
        f.SetTint("#ff8000", 0.5f);
        var mpb = new MaterialPropertyBlock(); bool tintOk = true;
        foreach (var r in f.Meshes)
        {
            r.GetPropertyBlock(mpb);
            var c = mpb.GetColor("_Tint"); var g = mpb.GetVector("_SgGround");
            if (Math.Abs(c.r - 0.5f) > 1e-3 || Math.Abs(c.g - 0.5f * 128 / 255f) > 2e-3 || c.b != 0) tintOk = false;
            if ((r == f.Body) != (g.x > 0.5f)) tintOk = false;
        }
        Check(tintOk, "SetTint 写入全部渲染器；地面贴合只开在身体上");
        // 换兵器
        f.SetWeapon("halberd");
        Check(f.WeaponInfo != null && f.WeaponInfo.id == "halberd" && f.WeaponMesh != null && f.Style == FigBuild.WeaponStyle("halberd"), "SetWeapon(halberd)：" + (f.WeaponInfo != null ? f.WeaponInfo.id : "null") + " / " + f.Style);
        f.SetPose("light1", 0.2);
        Check(BoneErr(f, RootThree(0, 0, Math.PI)) < 1e-4, "换兵器后姿势同步");
        // 骑乘
        var h = Figure.Horse(new FigHorseOpts { general = true, name = "赤兔", lod = "mid", color = "#7a2a8a" });
        Check(h.Body.bones.Length == FigBones.HBONES.Length && h.Body.sharedMesh.bindposes.Length == FigBones.HBONES.Length, "战马骨骼数");
        h.Mount(f);
        Check(f.Root.parent == h.Bones["saddle"] && f.Mounted == h && f.Rig.mounted == h.Rig, "Mount：骑手挂在鞍座");
        h.Root.localPosition = Figure.PosFromJs(-2, 0, 1); h.Root.localRotation = Quaternion.Euler(0, Figure.YawFromJs(-0.4), 0);
        f.SetPose("light1", 0.3);
        h.SetPose("gallop", 0.2);
        {
            var hm = h.Rig.BoneMatrices(); var root3 = RootThree(-2, 1, -0.4);
            var off = FigHorseRig.RiderOffset(f.Rig);
            var riderRoot = M4d.Mul(M4d.Mul(root3, hm[FigHorseRig.Saddle]), M4d.Translation(off.x, off.y, off.z));
            double d = BoneErr(f, riderRoot);
            double dh = 0;
            for (int i = 0; i < hm.Length; i++) { var w = Pos(M4d.Mul(root3, hm[i])); var u = h.Bones[FigBones.HBONES[i]].position; dh = Math.Max(dh, Math.Abs(u.x - w.x) + Math.Abs(u.y - w.y) + Math.Abs(u.z + w.z)); }
            Check(dh < 1e-4, "战马骨骼世界位置最大差 " + F(dh));
            Check(d < 1e-4, "骑手骨骼世界位置（鞍座 × 偏移）最大差 " + F(d));
            // 疾驰时下半身用 gallop（JS Horse.setPose 的骑手同步）
            var r2 = new FigRig(f.Template); r2.mounted = h.Rig; r2.SetPose("light1", 0.3, new FigPoseOpts { gallop = true });
            r2.SetWeapon(f.Rig.weaponInfo, f.Rig.style); r2.SetPose("light1", 0.3, new FigPoseOpts { gallop = true });
            var A = f.Rig.BoneMatrices(); var Bm = r2.BoneMatrices(); double dd = 0;
            for (int i = 0; i < A.Length; i++) for (int k = 0; k < 16; k++) dd = Math.Max(dd, Math.Abs(A[i].e[k] - Bm[i].e[k]));
            Check(dd < 1e-9, "战马 SetPose 同步骑手（gallop 下半身）最大差 " + F(dd));
        }
        f.ApplyProps();
        f.Body.GetPropertyBlock(mpb);
        Check(mpb.GetVector("_SgGround").x < 0.5f, "骑乘时关闭地面贴合");
        h.Dismount(f);
        Check(f.Mounted == null && f.Root.parent == null && h.Riders.Count == 0, "Dismount");
        f.Body.GetPropertyBlock(mpb);
        Check(mpb.GetVector("_SgGround").x > 0.5f, "下马后恢复地面贴合");
        f.GroundClamp = false; f.Body.GetPropertyBlock(mpb);
        Check(mpb.GetVector("_SgGround").x < 0.5f, "GroundClamp = false 立即写入属性块");
        f.GroundClamp = true;
        // 战马在画面根节点下：下马后骑手留在同一父节点下；战马 Dispose 让骑手下马（不随之消失）；销毁途中只解除关系
        {
            var screen = new GameObject("screen").transform;
            var h2 = Figure.Horse(new FigHorseOpts { lod = "low" }); h2.Root.SetParent(screen, false);
            var g2 = Figure.Soldier(new FigOpts { type = "cavalry", culture = "han", lod = "low" });
            h2.Mount(g2); h2.Dismount(g2);
            Check(g2.Root.parent == screen && g2.Mounted == null, "Dismount：骑手挂到战马的父节点下");
            h2.Mount(g2); h2.Dispose();
            Check(g2.Root.parent == screen && !g2.Disposed && g2.Mounted == null, "战马 Dispose：骑手下马、留在画面里");
            var h3 = Figure.Horse(new FigHorseOpts { lod = "low" }); h3.Root.SetParent(screen, false);
            h3.Mount(g2); var saddle = h3.Bones["saddle"];
            h3.Release(false);
            Check(g2.Root.parent == saddle && g2.Mounted == null && h3.Riders.Count == 0, "销毁途中（Release(false)）：只解除骑乘关系，不改层级");
            g2.Dispose();
        }
        // 释放：名将模板与面部贴图
        string key = f.Template.key; int faces = FigTextures.FaceCount;
        var f2 = Figure.General("关羽", new FigOpts { lod = "high" });
        Check(f2.Template == f.Template && FigTextures.FaceCount == faces && f2.Head.sharedMaterial.GetTexture("_MainTex") == f.Head.sharedMaterial.GetTexture("_MainTex"), "同一武将共用模板与面部贴图");
        f.Dispose(); f2.Dispose();
        Check(!FigTemplates.Has(key) && FigTextures.FaceCount == faces - 1, "Dispose 释放名将模板与面部贴图");
        h.Dispose();
        // 士兵、单独的兵器 / 盾
        var sd = Figure.Soldier(new FigOpts { type = "sword", culture = "roman", lod = "low", color = "#2c3d8f" });
        Check(sd.Head == null && sd.ShieldMesh == null && sd.Triangles <= 900 && sd.Body.sharedMesh.triangles.Length / 3 == sd.Triangles, "士兵：单一蒙皮网格（盾、兵器并入），三角形 " + sd.Triangles);
        Check(sd.Body.sharedMaterial.GetColor_("_Team").r > 0.17f && sd.Body.sharedMaterial == Figure.TeamMaterial("#2c3d8f"), "势力色材质按颜色缓存");
        sd.SetPose("block", 0.5); Check(SkinErr(sd) < 1e-3, "士兵蒙皮");
        string sk = sd.Template.key; sd.Dispose();
        Check(FigTemplates.Has(sk), "士兵模板常驻");
        var wgo = Figure.Weapon("guandao", new FigOpts { lod = "high" });
        var wi = wgo.GetComponent<FigureWeapon>();
        Check(wi != null && wi.id == "guandao" && wi.tris > 0 && wgo.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3 == wi.tris && wi.tip.y > 1, "Weapon(guandao)：信息与网格");
        var sh = Figure.Shield("scutum", new FigOpts { lod = "mid" });
        Check(sh.GetComponent<FigureWeapon>().tris > 0, "Shield(scutum)");
        // 士兵群
        int horseFrees = 0; var prevFree = FigTemplates.OnFreeHorse;
        FigTemplates.OnFreeHorse = t => { horseFrees++; if (prevFree != null) prevFree(t); };
        var crowd = new FigureCrowd(new FigCrowdOptions { lod = "low" });
        var ids = new List<int>();
        ids.Add(crowd.Add(new FigCrowdSpec { type = "archer", culture = "roman", color = "#2c3d8f" }));
        ids.Add(crowd.Add(new FigCrowdSpec { type = "cavalry", culture = "persia", mounted = true, color = "#c8382c" }));
        ids.Add(crowd.Add(new FigCrowdSpec { type = "cavalry", culture = "persia", mounted = true, color = "#2f7d4a" }));
        for (int i = 0; i < 1100; i++) ids.Add(crowd.Add(new FigCrowdSpec { type = "spear", culture = "han", seed = 0, color = i % 2 == 0 ? "#2f7d4a" : "#c8382c" }));
        for (int i = 0; i < ids.Count; i++) { crowd.SetTransform(ids[i], new Vector3(i % 40, 0, i / 40), i * 7f); crowd.Play(ids[i], i % 3 == 0 ? "walk" : "light1", null, (i * 0.37) % 1); }
        crowd.SetTime(1.25f);
        Check(horseFrees == 0 && FigTemplates.HorseCount == 1, "士兵群持有战马模板：两名骑兵只建一次（释放 " + horseFrees + "、战马模板 " + FigTemplates.HorseCount + "）");
        Check(Math.Abs(crowd.ClipDur("run", true) - crowd.ClipDur("idle", true)) < 1e-9 && Math.Abs(crowd.ClipDur("gallop", true) - 1) < 1e-9, "ClipDur：骑兵没有的片段按 idle（= JS）");
        {
            // 战马选项只覆盖给了的项：士兵群 lod（low）与 seed % 2 照 JS 默认
            var cr2 = new FigureCrowd(new FigCrowdOptions { lod = "low" });
            cr2.Add(new FigCrowdSpec { type = "cavalry", culture = "han", mounted = true, seed = 3, horse = new FigCrowdHorse { coat = "#5a3a22" } });
            var H = (System.Collections.IDictionary)typeof(FigTemplates).GetField("H", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            bool hit = false; foreach (var k in H.Keys) if (((string)k).StartsWith("low|#5a3a22|cloth|1|")) hit = true;
            Check(hit, "FigCrowdHorse 叠加：lod low、seed 3 % 2 = 1、马铠按文化（cloth）、毛色照给");
            cr2.Dispose();
        }
        var st = crowd.Stats();
        Check(st.soldiers == 1103 && st.groups == 4 && crowd.DrawCalls == 5, "士兵群：1103 人、4 组、5 批（> 1023 分批）：" + st.soldiers + " / " + st.groups + " / " + crowd.DrawCalls);
        int c0 = Graphics.calls, i0 = Graphics.instances;
        crowd.Draw();
        Check(Graphics.calls - c0 == 5 && Graphics.instances - i0 == 1105, "Draw：" + (Graphics.calls - c0) + " 次、" + (Graphics.instances - i0) + " 个实例（骑兵 = 骑手 + 战马）");
        Check(crowd.ClipOf(ids[0]) == "walk" && crowd.ClipOf(ids[1]) == "idle" && crowd.ClipOf(ids[3]) == "walk" && crowd.ClipOf(ids[4]) == "light1", "骑兵 / 步兵片段表（骑兵没有 light1 → idle）");
        for (int i = 3; i < 600; i += 2) crowd.Remove(ids[i]);
        crowd.Remove(ids[1]);
        int c1 = Graphics.calls, i1 = Graphics.instances;
        crowd.Draw();
        Check(crowd.Count == 1103 - 300 && Graphics.instances - i1 == crowd.Count + 1 && Graphics.calls - c1 == 4, "移除后：" + crowd.Count + " 人、" + (Graphics.instances - i1) + " 个实例、" + (Graphics.calls - c1) + " 批");
        Check(st.bakeBytes > 0 && st.triangles > 0, "烘焙贴图字节 " + st.bakeBytes + "、三角形 " + st.triangles);
        {
            // 上传后丢弃单精度烘焙数据（只留片段表与尺寸）
            var B = (System.Collections.IDictionary)typeof(FigureCrowd).GetField("BAKES", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            bool dropped = B.Count > 0;
            foreach (var v in B.Values) foreach (var bt in new[] { ((FigBakeSet)v).soldier, ((FigBakeSet)v).rider, ((FigBakeSet)v).horse }) if (bt != null && (bt.data.data != null || bt.data.table.Count == 0 || bt.data.W == 0)) dropped = false;
            Check(dropped, "烘焙贴图上传后丢弃 CPU 端矩阵数据（" + B.Count + " 组）");
        }
        crowd.Dispose();
        Check(FigTemplates.HorseCount == 0 && horseFrees == 2, "士兵群释放时归还战马模板（释放 " + horseFrees + "）");
        FigTemplates.OnFreeHorse = prevFree;
        // 回退路径（不实例化）
        var cf = new FigureCrowd(new FigCrowdOptions { lod = "low", instanced = false });
        int a = cf.Add(new FigCrowdSpec { type = "spear", culture = "han" }), b = cf.Add(new FigCrowdSpec { type = "cavalry", culture = "han", mounted = true });
        cf.Play(a, "walk"); cf.Play(b, "gallop"); cf.SetTime(0.7f); cf.SetTransform(b, new Vector3(3, 0, 1), 90);
        Check(cf.ClipOf(a) == "walk" && cf.ClipOf(b) == "gallop" && cf.DrawCalls >= 3, "士兵群回退路径");
        cf.Dispose();
        // 预生成（协程分片）
        FigStats.maxSliceMs = 0;
        Drain(Figure.Preload(new FigPreload { cultures = new[] { "han", "roman" }, types = new[] { "spear", "cavalry" }, crowd = new[] { new FigCrowdSpec { type = "sword", culture = "han" } }, generals = new[] { new PortraitGen { name = "张飞" } } }));
        Check(FigTextures.AtlasReady && FigStats.preloadMs >= 0, "Preload 完成（单片最长 " + FigStats.maxSliceMs + " ms，最长一步 " + FigStats.maxStepMs + " ms：" + FigStats.maxStepJob + "）");
        Check(!FigAtlas.Ready, "图集上传后丢弃 CPU 端数据");
        {
            // 环境贴图：材质自带（不依赖 RenderSettings）；反射立方体贴图整条 mip 链，辐照度 8²
            var km = Figure.Material; var ec = FigTextures.EnvCube(); var ei = FigTextures.EnvIrr;
            Check(km.GetTexture("_EnvCube") == ec && km.GetTexture("_EnvIrr") == ei && Figure.TeamMaterial("#123456").GetTexture("_EnvCube") == ec, "人物材质带 _EnvCube / _EnvIrr");
            int NL = FigAtlas.ENV_SIGMA.Length;
            bool lv = ec.width == FigAtlas.ENV_CUBE && ec.mipmapCount == NL && ec.levels.Count == NL && ei.width == FigAtlas.ENV_IRR && !ec.readable && !ei.readable;
            for (int l = 0; l < NL; l++) if (!ec.levels.ContainsKey(l) || ec.levels[l].Count != 6) lv = false;
            Check(lv, "反射立方体贴图 " + ec.width + "² × " + NL + " 级 × 6 面（= 整条 mip 链），辐照度 " + ei.width + "²，上传后不可读");
        }
        // 环境立方体贴图
        var cube = Figure.InstallEnvironment();
        Check(RenderSettings.customReflection == cube && RenderSettings.defaultReflectionMode == UnityEngine.Rendering.DefaultReflectionMode.Custom && ((Cubemap)cube).faces.Count == 6, "InstallEnvironment：6 个面");
        {
            // 朝上的面（+Y）应接近天顶色（偏蓝），朝下的面接近地面色
            var up = ((Cubemap)cube).faces[CubemapFace.PositiveY]; var dn = ((Cubemap)cube).faces[CubemapFace.NegativeY];
            var cu = up[up.Length / 2]; var cd = dn[dn.Length / 2];
            Check(cu.b > cu.r && cd.r > cd.b, "立方体贴图朝向：天顶偏蓝、地面偏暖");
        }
        Figure.ClearCache();
        var stats = Figure.Stats();
        Console.WriteLine(string.Format("Unity 接口冒烟测试 {0} 项，{1:F0} ms；模板 {2}、战马模板 {3}、面部贴图 {4}、材质 {5}、贴图 {6}", checks, FigMath.Now() - t0, stats.templateCount, stats.horseTemplates, stats.faces, stats.materials, stats.textures));
        Console.WriteLine(fails == 0 ? "== 人物模型库 Unity 接口：全部通过" : "== 人物模型库 Unity 接口：失败 " + fails + " 项");
        return fails;
    }
}
static class MatExt { public static Color GetColor_(this Material m, string n) { object o; return m.props.TryGetValue(n, out o) && o is Color ? (Color)o : new Color(0, 0, 0, 0); } }
