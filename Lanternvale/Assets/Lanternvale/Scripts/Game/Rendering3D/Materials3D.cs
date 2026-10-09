// Shared materials of the 3D presentation and the per-renderer property ids.
//
// The shaders live in Resources/Shaders (so builds always contain them) and do their own lighting (SceneLighting), so
// they render the same under the built-in pipeline and URP. Materials are shared: per-object looks (tint, hit flash,
// dither fade, rim highlight, outline colour) go through MaterialPropertyBlocks (Props helpers below), never through
// material instances.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class Materials3D
    {
        public const string LowPolyShader = "Lanternvale/LowPoly";
        public const string OutlineShader = "Lanternvale/Outline";
        public const string AdditiveShader = "Lanternvale/Additive";
        public const string ShadowShader = "Lanternvale/Shadow";
        public const string LitTransparentShader = "Lanternvale/LitTransparent";
        public const string SkyShader = "Lanternvale/Sky";

        public static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        public static readonly int ColorId = Shader.PropertyToID("_Color");
        public static readonly int TintId = Shader.PropertyToID("_Tint");
        public static readonly int FlashId = Shader.PropertyToID("_Flash");
        public static readonly int FadeId = Shader.PropertyToID("_Fade");
        public static readonly int RimId = Shader.PropertyToID("_Rim");
        public static readonly int FogScaleId = Shader.PropertyToID("_FogScale");
        public static readonly int WindScaleId = Shader.PropertyToID("_WindScale");
        public static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        public static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        public static readonly int PlanarScaleId = Shader.PropertyToID("_PlanarScale");
        public static readonly int TexStrengthId = Shader.PropertyToID("_TexStrength");
        public static readonly int CullId = Shader.PropertyToID("_Cull");
        public static readonly int EmissionId = Shader.PropertyToID("_Emission");
        public static readonly int SoftnessId = Shader.PropertyToID("_Softness");

        /// <summary>Default ink colour of outlines.</summary>
        public static readonly Color Ink = new Color(0.14f, 0.10f, 0.13f, 1f);

        static Material lowPoly, lowPolyDouble, outline, additive, shadow, sky;
        static readonly Dictionary<Texture, Material> texturedCache = new Dictionary<Texture, Material>();
        static readonly Dictionary<Texture, Material> transparentCache = new Dictionary<Texture, Material>();
        static readonly Dictionary<Texture, Material> additiveCache = new Dictionary<Texture, Material>();
        static readonly HashSet<string> warned = new HashSet<string>();

        /// <summary>A Lanternvale shader, or a built-in stand-in (vertex-coloured unlit) when it is missing/unsupported.</summary>
        public static Shader Find(string name)
        {
            var s = Shader.Find(name);
            if (s != null && s.isSupported) return s;
            if (warned.Add(name))
                Debug.LogWarning($"[Lanternvale] Shader '{name}' is {(s == null ? "missing" : "not supported on this device")}; using Sprites/Default instead.");
            return Shader.Find("Sprites/Default");
        }

        static Material Make(string shader, string name)
        {
            var m = new Material(Find(shader)) { name = name, hideFlags = HideFlags.DontSave };
            return m;
        }

        /// <summary>Opaque vertex-coloured models (back faces culled).</summary>
        public static Material LowPoly => lowPoly != null ? lowPoly : (lowPoly = Make(LowPolyShader, "LV LowPoly"));

        /// <summary>Opaque vertex-coloured models drawn from both sides (leaves, cloth, banners, thin planes).</summary>
        public static Material LowPolyDoubleSided
        {
            get
            {
                if (lowPolyDouble != null) return lowPolyDouble;
                lowPolyDouble = Make(LowPolyShader, "LV LowPoly 2-sided");
                lowPolyDouble.SetFloat(CullId, 0f);
                return lowPolyDouble;
            }
        }

        /// <summary>Ink outline: add as the LAST material of a renderer whose mesh has one submesh.</summary>
        public static Material Outline => outline != null ? outline : (outline = Make(OutlineShader, "LV Outline"));

        /// <summary>Additive glow (white texture: use with a sprite/texture via SpriteRenderer or AdditiveFor).</summary>
        public static Material Additive => additive != null ? additive : (additive = Make(AdditiveShader, "LV Additive"));

        /// <summary>Soft blob shadow for a 0..1-UV quad lying on the ground (see Mesh3D.ShadowQuad).</summary>
        public static Material Shadow => shadow != null ? shadow : (shadow = Make(ShadowShader, "LV Shadow"));

        /// <summary>Unlit vertex-coloured sky / backdrop.</summary>
        public static Material Sky => sky != null ? sky : (sky = Make(SkyShader, "LV Sky"));

        /// <summary>{ LowPoly, Outline }: model with an ink outline.</summary>
        public static Material[] WithOutline(bool doubleSided = false) => new[] { doubleSided ? LowPolyDoubleSided : LowPoly, Outline };

        /// <summary>LowPoly with a painted texture. planarScale &gt; 0 maps it by world XY (1 / metres per tile), for terrain.</summary>
        public static Material Textured(Texture tex, float planarScale = 0f, float strength = 1f)
        {
            if (tex == null) return LowPoly;
            if (texturedCache.TryGetValue(tex, out var m) && m != null) return m;
            m = Make(LowPolyShader, "LV Textured " + tex.name);
            m.SetTexture(MainTexId, tex);
            m.SetFloat(PlanarScaleId, planarScale);
            m.SetFloat(TexStrengthId, strength);
            texturedCache[tex] = m;
            return m;
        }

        /// <summary>Lit alpha-blended textured material (decals, cards), cached per texture.</summary>
        public static Material LitTransparent(Texture tex)
        {
            var key = tex != null ? tex : Texture2D.whiteTexture;
            if (transparentCache.TryGetValue(key, out var m) && m != null) return m;
            m = Make(LitTransparentShader, "LV LitTransparent " + key.name);
            m.SetTexture(MainTexId, key);
            transparentCache[key] = m;
            return m;
        }

        /// <summary>Additive material with a texture, cached per texture (meshes / particle systems).</summary>
        public static Material AdditiveFor(Texture tex)
        {
            var key = tex != null ? tex : Texture2D.whiteTexture;
            if (additiveCache.TryGetValue(key, out var m) && m != null) return m;
            m = Make(AdditiveShader, "LV Additive " + key.name);
            m.SetTexture(MainTexId, key);
            additiveCache[key] = m;
            return m;
        }

        // ------------------------------------------------------------------ per-renderer looks

        /// <summary>
        /// Per-renderer look of a LowPoly (+ Outline) renderer, applied through one MaterialPropertyBlock. Keep one per
        /// renderer, change fields, call Apply(renderer) when something changed.
        /// </summary>
        public sealed class Look
        {
            public Color Tint = Color.white;
            public Color Flash = new Color(1f, 1f, 1f, 0f);
            public float Fade = 1f;
            public float Rim;
            public float FogScale = 1f;
            public float WindScale = 1f;
            public Color OutlineColor = Ink;
            public float OutlineWidth = 2.2f;

            readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

            public void Apply(Renderer r)
            {
                if (r == null) return;
                block.Clear();
                block.SetColor(TintId, Tint);
                block.SetColor(FlashId, Flash);
                block.SetFloat(FadeId, Fade);
                block.SetFloat(RimId, Rim);
                block.SetFloat(FogScaleId, FogScale);
                block.SetFloat(WindScaleId, WindScale);
                block.SetColor(OutlineColorId, OutlineColor);
                block.SetFloat(OutlineWidthId, OutlineWidth);
                r.SetPropertyBlock(block);
            }
        }
    }
}
