// Turns a built GameObject hierarchy into draw calls: per active renderer, its mesh, world matrix and materials, with
// properties resolved like Unity does (MaterialPropertyBlock → material → shader default). The Lanternvale shader
// name picks the shading model; an Outline material as the last material marks the renderer as ink-outlined.
using System.Collections.Generic;
using Lanternvale.Game;
using UnityEngine;

namespace Lanternvale.Preview
{
    public static class Collector
    {
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FogScaleId = Shader.PropertyToID("_FogScale");
        static readonly int RimId = Shader.PropertyToID("_Rim");
        static readonly int EmissionId = Shader.PropertyToID("_Emission");
        static readonly int CullId = Shader.PropertyToID("_Cull");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int SideTexId = Shader.PropertyToID("_SideTex");
        static readonly int PlanarId = Shader.PropertyToID("_PlanarScale");
        static readonly int SidePlanarId = Shader.PropertyToID("_SidePlanarScale");
        static readonly int MainAvgId = Shader.PropertyToID("_MainAvg");
        static readonly int SideAvgId = Shader.PropertyToID("_SideAvg");
        static readonly int TexStrengthId = Shader.PropertyToID("_TexStrength");
        static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        public sealed class Stats { public int Renderers, Draws, Outlined; }

        public static List<DrawCall> Collect(IEnumerable<Transform> roots, Frame frame, Vector3 camPos, Stats stats = null)
        {
            var list = new List<DrawCall>();
            foreach (var r in roots) Walk(r, list, frame, camPos, stats);
            return list;
        }

        static void Walk(Transform t, List<DrawCall> list, Frame frame, Vector3 camPos, Stats stats)
        {
            var go = t.gameObject;
            if (!go.activeSelf || ReferenceEquals(go, null)) return;
            var r = go.GetComponent<Renderer>();
            if (r != null && r.enabled)
            {
                var mesh = r.RenderMesh;
                if (mesh != null && mesh.T.Count >= 3)
                {
                    var mats = r.sharedMaterials;
                    bool outlined = false;
                    foreach (var m in mats) if (m != null && m.shader != null && m.shader.name == Materials3D.OutlineShader) outlined = true;
                    float fade = r.Block.Floats.TryGetValue(FadeId, out var fv) ? fv : 1f;
                    int obj = frame.NewObject(outlined, fade);
                    if (stats != null) { stats.Renderers++; if (outlined) stats.Outlined++; }
                    var M = t.localToWorldMatrix;
                    float dist = Vector3.Distance(r.bounds.center, camPos);
                    if (r is SkinnedMeshRenderer smr && smr.bones != null && mesh.boneWeights != null && mesh.bindposes != null)
                    {
                        mesh = Skin(smr, mesh);   // world-space vertices
                        M = Matrix4x4.identity;
                        dist = Vector3.Distance(smr.rootBone != null ? smr.rootBone.position : t.position, camPos);
                    }
                    foreach (var m in mats)
                    {
                        if (m == null || m.shader == null || m.shader.name == Materials3D.OutlineShader) continue;
                        var d = Make(m, r, mesh, M);
                        if (d == null) continue;
                        d.ObjId = obj;
                        if (d.Kind == DrawKind.Opaque) d.Fade = fade;
                        d.Outlined = outlined;
                        d.SortDist = dist;
                        d.Name = go.name;
                        list.Add(d);
                        if (stats != null) stats.Draws++;
                    }
                }
            }
            foreach (var c in t.children.ToArray()) Walk(c, list, frame, camPos, stats);
        }

        /// <summary>
        /// Rigid GPU skinning on the CPU (SkinQuality.Bone1, like UnitBody): every vertex follows its one bone,
        /// world = bones[i].localToWorldMatrix × bindposes[i] × v. Normals likewise (uniform scales only).
        /// </summary>
        public static Mesh Skin(SkinnedMeshRenderer smr, Mesh src)
        {
            var bones = smr.bones;
            var bp = src.bindposes;
            var mats = new Matrix4x4[bones.Length];
            for (int i = 0; i < mats.Length; i++)
                mats[i] = (bones[i] != null ? bones[i].localToWorldMatrix : smr.transform.localToWorldMatrix) * (i < bp.Length ? bp[i] : Matrix4x4.identity);
            var bw = src.boneWeights;
            var m = new Mesh { name = src.name + " (skinned)", C = src.C, UV0 = src.UV0, UV1 = src.UV1, T = src.T };
            m.V = new List<Vector3>(src.V.Count);
            m.N = new List<Vector3>(src.N.Count);
            for (int v = 0; v < src.V.Count; v++)
            {
                int b = v < bw.Length ? bw[v].boneIndex0 : 0;
                if (b < 0 || b >= mats.Length) b = 0;
                m.V.Add(mats[b].MultiplyPoint3x4(src.V[v]));
                if (v < src.N.Count) m.N.Add(mats[b].MultiplyVector(src.N[v]).normalized);
            }
            m.RecalculateBounds();
            return m;
        }

        static float F(Renderer r, Material m, int id, float def)
        {
            if (r.Block.Floats.TryGetValue(id, out var v)) return v;
            if (m.Floats.TryGetValue(id, out v)) return v;
            return def;
        }

        static Color Col(Renderer r, Material m, int id, Color def)
        {
            if (r.Block.Colors.TryGetValue(id, out var v)) return v;
            if (m.Colors.TryGetValue(id, out v)) return v;
            return def;
        }

        static Texture Tex(Renderer r, Material m, int id)
        {
            if (r.Block.Textures.TryGetValue(id, out var v)) return v;
            return m.GetTexture(id);
        }

        static DrawCall Make(Material m, Renderer r, Mesh mesh, Matrix4x4 M)
        {
            var d = new DrawCall { Mesh = mesh, M = M, Order = r.sortingOrder };
            string s = m.shader.name;
            var color = Col(r, m, ColorId, Color.white);
            switch (s)
            {
                case Materials3D.LowPolyShader:
                    d.Kind = DrawKind.Opaque;
                    d.Queue = 2000;
                    d.CullBack = F(r, m, CullId, 2f) >= 1.5f;
                    d.Tint = Lighting.Lin(Col(r, m, TintId, Color.white));
                    d.ColorRgb = Lighting.Lin(color);
                    d.FogScale = F(r, m, FogScaleId, 1f);
                    d.Rim = F(r, m, RimId, 0f);
                    var tex = Tex(r, m, MainTexId);
                    if (tex != null && tex != Texture2D.whiteTexture) { d.Tex = Sampler.For(tex); d.Planar = F(r, m, PlanarId, 0f); d.TexStrength = F(r, m, TexStrengthId, 1f); }
                    break;
                case "Lanternvale/Terrain":
                    d.Kind = DrawKind.Terrain;
                    d.Queue = 2000;
                    d.Tex = Sampler.For(Tex(r, m, MainTexId));
                    d.Tex2 = Sampler.For(Tex(r, m, SideTexId));
                    if (d.Tex == null || d.Tex2 == null) return null;
                    d.Planar = F(r, m, PlanarId, 0.125f);
                    d.Planar2 = F(r, m, SidePlanarId, 0.125f);
                    d.Avg1 = Lighting.Lin(Col(r, m, MainAvgId, new Color(0.6f, 0.69f, 0.45f)));
                    d.Avg2 = Lighting.Lin(Col(r, m, SideAvgId, new Color(0.6f, 0.69f, 0.45f)));
                    d.FogScale = F(r, m, FogScaleId, 1f);
                    break;
                case Materials3D.LitTransparentShader:
                    d.Kind = DrawKind.LitTransparent;
                    d.Queue = m.renderQueue >= 0 ? m.renderQueue : 2950;
                    d.CullBack = false;
                    d.Tex = Sampler.For(Tex(r, m, MainTexId));
                    d.ColorRgb = Lighting.Lin(color);
                    d.ColorA = color.a;
                    d.Emission = F(r, m, EmissionId, 0f);
                    d.FogScale = F(r, m, FogScaleId, 1f);
                    d.TexOffset = m.mainTextureOffset;
                    break;
                case Materials3D.ShadowShader:
                    d.Kind = DrawKind.Shadow;
                    d.Queue = m.renderQueue >= 0 ? m.renderQueue : 2955;
                    d.CullBack = false;
                    var sc = Col(r, m, ColorId, new Color(0.12f, 0.09f, 0.16f, 0.42f));
                    d.ColorRgb = Lighting.Lin(sc);
                    d.ColorA = sc.a;
                    d.Softness = F(r, m, SoftnessId, 0.65f);
                    break;
                case Materials3D.AdditiveShader:
                    d.Kind = DrawKind.Additive;
                    d.Queue = m.renderQueue >= 0 ? m.renderQueue : 3000;
                    d.CullBack = false;
                    d.Tex = Sampler.For(Tex(r, m, MainTexId));
                    d.ColorRgb = Lighting.Lin(color);
                    d.ColorA = color.a;
                    d.FogScale = F(r, m, FogScaleId, 1f);
                    break;
                case Materials3D.SkyShader:
                    d.Kind = DrawKind.Sky;
                    d.Queue = 1000;
                    d.CullBack = false;
                    d.Tint = Lighting.Lin(Col(r, m, TintId, Color.white));
                    break;
                default:
                    return null;   // GroundOverlay previews etc. are not part of a map view
            }
            return d;
        }
    }
}
