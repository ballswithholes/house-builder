// Runtime instance of a unit model: the "Body" transform (yaw + scale), its bone transforms, one rigidly skinned
// SkinnedMeshRenderer with { LowPoly, Outline } (2 draw calls), its animator and its per-renderer Look.
// UnitModels builds each recipe once (mesh + skeleton) and caches it.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    public sealed class UnitBody
    {
        public readonly UnitModel Model;
        public readonly GameObject Go;
        public readonly Transform Root;
        public readonly Transform[] Bones;
        public readonly SkinnedMeshRenderer Renderer;
        public readonly UnitAnimator Anim;
        public readonly Materials3D.Look Look = new Materials3D.Look();

        // last applied look (to touch the property block only when something changed)
        Color lTint = new Color(-1f, 0f, 0f), lFlash, lOutline;
        float lFade = -1f, lRim = -1f, lWidth = -1f;

        static Material[] sharedMats;

        UnitBody(UnitModel model, Transform parent, string name)
        {
            Model = model;
            Go = new GameObject(name);
            Root = Go.transform;
            Root.SetParent(parent, false);
            int n = model.BoneCount;
            Bones = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                var b = new GameObject(model.Names != null && i < model.Names.Length ? model.Names[i] : "Bone" + i).transform;
                Bones[i] = b;
            }
            for (int i = 0; i < n; i++)
            {
                int p = model.Parent[i];
                Bones[i].SetParent(p >= 0 ? Bones[p] : Root, false);
                Bones[i].localPosition = model.LocalOffset(i);
                Bones[i].localRotation = Quaternion.identity;
                Bones[i].localScale = Vector3.one;
            }
            Renderer = Go.AddComponent<SkinnedMeshRenderer>();
            Renderer.sharedMesh = model.Mesh;
            Renderer.bones = Bones;
            Renderer.rootBone = Bones[0];
            Renderer.quality = SkinQuality.Bone1;
            Renderer.updateWhenOffscreen = false;
            Renderer.shadowCastingMode = ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.lightProbeUsage = LightProbeUsage.Off;
            Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Renderer.skinnedMotionVectors = false;
            if (sharedMats == null || sharedMats[0] == null || sharedMats[1] == null) sharedMats = Materials3D.WithOutline();
            Renderer.sharedMaterials = sharedMats;
            // generous bounds in the root bone's space: covers lying down, lunges, raised weapons, floating
            float h = Mathf.Max(0.3f, model.Height);
            float r = Mathf.Max(h, model.Radius * 2f + model.HalfLength * 2f);
            var rootBind = model.Bind[0];
            Renderer.localBounds = new Bounds(new Vector3(0f, h * 0.5f - rootBind.y + model.FloatHeight * 0.5f, 0f),
                                              new Vector3(r * 2.4f, h * 2.2f + model.FloatHeight, r * 2.4f));
            Look.OutlineWidth = 1.9f;
            Look.Fade = model.BaseFade;
            Anim = new UnitAnimator(model, Bones);
        }

        public static UnitBody Create(UnitModel model, Transform parent, string name = "Body") => new UnitBody(model, parent, name);

        public void SetActive(bool on)
        {
            if (Go != null && Go.activeSelf != on) Go.SetActive(on);
        }

        /// <summary>Applies the look when any value changed (no allocation).</summary>
        public void ApplyLook()
        {
            var l = Look;
            if (l.Tint == lTint && l.Flash == lFlash && l.OutlineColor == lOutline && Mathf.Abs(l.Fade - lFade) < 0.002f &&
                Mathf.Abs(l.Rim - lRim) < 0.002f && Mathf.Abs(l.OutlineWidth - lWidth) < 0.01f) return;
            lTint = l.Tint; lFlash = l.Flash; lOutline = l.OutlineColor; lFade = l.Fade; lRim = l.Rim; lWidth = l.OutlineWidth;
            l.Apply(Renderer);
        }

        public void Destroy()
        {
            if (Go != null) Object.Destroy(Go);
        }

        /// <summary>World position of a point given in a bone's local space (model units).</summary>
        public Vector3 BonePoint(int bone, Vector3 local)
        {
            if (bone < 0 || bone >= Bones.Length) bone = 0;
            return Bones[bone].TransformPoint(local);
        }
    }

    /// <summary>Recipe registry: key → shared model (mesh + skeleton), built on first use.</summary>
    public static class UnitModels
    {
        static readonly Dictionary<string, UnitModel> cache = new Dictionary<string, UnitModel>(System.StringComparer.Ordinal);

        /// <summary>
        /// The model of `key` in a deterministic look variation (generic villagers and children; any int, e.g. a hash of
        /// an NPC id). Keys without variations, and variation 0, give the plain model. Meshes are cached per
        /// (key, variation): at most UnitRecipes.VariantCount(key) of them.
        /// </summary>
        public static UnitModel Get(string key, int variant)
        {
            int v = NormalizeVariant(key, variant);
            return Get(v == 0 ? key : key + "#" + v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// A stable variant number for an id (FNV-1a over its UTF-16 code units; string.GetHashCode is randomised per
        /// process), so an NPC keeps its look across sessions and the preview tool shows the same one (GameFlow.Views:
        /// CreateNpcView calls UnitView.SetVariant(StableVariant(npc id))).
        /// </summary>
        public static int StableVariant(string id)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (id != null) foreach (char c in id) { h ^= c; h *= 16777619u; }
                return (int)h;
            }
        }

        /// <summary>The variation index (0 … VariantCount − 1) a requested variant maps to for this key (0: none).</summary>
        public static int NormalizeVariant(string key, int variant)
        {
            int n = UnitRecipes.VariantCount(key);
            if (n <= 1) return 0;
            int v = variant % n;
            return v < 0 ? v + n : v;
        }

        public static UnitModel Get(string key)
        {
            key = string.IsNullOrEmpty(key) ? "npc_villager_a" : key;
            if (cache.TryGetValue(key, out var m) && m != null && m.Mesh != null) return m;
            try { m = UnitRecipes.Build(key); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                m = null;
            }
            if (m == null || m.Mesh == null)
            {
                Debug.LogWarning("[Lanternvale] No unit model for '" + key + "', using a villager.");
                m = key == "npc_villager_a" ? null : Get("npc_villager_a");
                if (m == null) return null;
            }
            cache[key] = m;
            return m;
        }

        /// <summary>Bakes the builder into the shared skinned mesh of the model (bind pose: identity rotations).</summary>
        public static UnitModel Bake(UnitModel m, MeshBuilder b)
        {
            var bp = new Matrix4x4[m.Bind.Length];
            for (int i = 0; i < bp.Length; i++) bp[i] = Matrix4x4.Translate(-m.Bind[i]);
            string name = "unit_" + m.Key;
            m.Mesh = MeshCache.Get(name, () => b.ToMesh(name, bp));
            return m;
        }
    }
}
