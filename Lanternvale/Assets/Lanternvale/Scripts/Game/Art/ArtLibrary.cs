// Loads art by key from Resources/Art using art_manifest.json. Missing art falls back to a
// procedural placeholder so the game always runs; real art can be dropped in with the same name.
using System;
using System.Collections.Generic;
using Lanternvale.Json;
using UnityEngine;

namespace Lanternvale.Game
{
    [Serializable]
    public class ArtEntry
    {
        public string key = "";
        public string path = "";
        public float height = 1f;
        public float[] pivot = { 0.5f, 0.05f };
        public string category = "Prop";
        public bool loop;
        public bool shadow;
    }

    [Serializable]
    public class ArtManifest
    {
        public List<ArtEntry> entries = new List<ArtEntry>();
    }

    public static class ArtLibrary
    {
        static readonly Dictionary<string, ArtEntry> Entries = new Dictionary<string, ArtEntry>(StringComparer.Ordinal);
        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        static bool initialized;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;
            var ta = Resources.Load<TextAsset>("Art/art_manifest");
            if (ta == null)
            {
                Debug.LogWarning("[Lanternvale] Resources/Art/art_manifest.json not found; using procedural placeholder art.");
                return;
            }
            try
            {
                var ctx = new JsonMapContext { Source = "art_manifest.json", ReportUnknownKeys = false };
                var manifest = JsonMapper.FromJson<ArtManifest>(ta.text, ctx, "art_manifest.json");
                foreach (var e in manifest.entries)
                    if (!string.IsNullOrEmpty(e.key)) Entries[e.key] = e;
            }
            catch (Exception ex)
            {
                Debug.LogError("[Lanternvale] Failed to read art manifest: " + ex.Message);
            }
        }

        public static bool Has(string key)
        {
            Init();
            return !string.IsNullOrEmpty(key) && Entries.ContainsKey(key);
        }

        public static ArtEntry Entry(string key)
        {
            Init();
            if (!string.IsNullOrEmpty(key) && Entries.TryGetValue(key, out var e)) return e;
            return null;
        }

        /// <summary>World height in metres the sprite is drawn at.</summary>
        public static float Height(string key, float fallback = 1.8f)
        {
            var e = Entry(key);
            return e != null && e.height > 0 ? e.height : fallback;
        }

        public static bool Loops(string key) => Entry(key)?.loop ?? false;
        public static bool CastsShadow(string key) => Entry(key)?.shadow ?? false;

        /// <summary>Raw texture for UI drawing (icons, portraits, panels). Never null.</summary>
        public static Texture2D Texture(string key)
        {
            Init();
            key = key ?? "";
            if (Textures.TryGetValue(key, out var t) && t != null) return t;
            var e = Entry(key);
            if (e != null && !string.IsNullOrEmpty(e.path)) t = Resources.Load<Texture2D>(e.path);
            if (t == null) t = Resources.Load<Texture2D>("Art/" + key);
            if (t == null) t = ProceduralArt.Make(key, e?.category ?? GuessCategory(key));
            if (e != null && (e.loop || e.category == "Ground")) t.wrapMode = TextureWrapMode.Repeat;
            else t.wrapMode = TextureWrapMode.Clamp;
            Textures[key] = t;
            return t;
        }

        /// <summary>True if a real (non-procedural) texture exists for the key.</summary>
        static readonly Dictionary<string, bool> RealCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        public static bool HasRealTexture(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (RealCache.TryGetValue(key, out var has)) return has;
            var e = Entry(key);
            has = e != null && Resources.Load<Texture2D>(e.path) != null;
            RealCache[key] = has;
            return has;
        }

        /// <summary>Sprite scaled so that it is Height(key) metres tall, pivot from the manifest. Never null.</summary>
        public static Sprite Sprite(string key)
        {
            Init();
            key = key ?? "";
            if (Sprites.TryGetValue(key, out var s) && s != null) return s;
            var tex = Texture(key);
            var e = Entry(key);
            var category = e?.category ?? GuessCategory(key);
            float height = e != null && e.height > 0 ? e.height : DefaultHeight(category);
            var pivot = e != null && e.pivot != null && e.pivot.Length >= 2
                ? new Vector2(e.pivot[0], e.pivot[1])
                : DefaultPivot(category);
            float ppu = Mathf.Max(1f, tex.height / Mathf.Max(0.01f, height));
            s = UnityEngine.Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect);
            s.name = key;
            Sprites[key] = s;
            return s;
        }

        public static string GuessCategory(string key)
        {
            if (string.IsNullOrEmpty(key)) return "Prop";
            if (key.StartsWith("bg_")) return "Background";
            if (key.StartsWith("ground_")) return "Ground";
            if (key.StartsWith("fg_")) return "Foreground";
            if (key.StartsWith("char_") || key.StartsWith("comp_") || key.StartsWith("npc_")) return "Character";
            if (key.StartsWith("portrait_")) return "Portrait";
            if (key.StartsWith("cr_") || key.StartsWith("pet_") || key.StartsWith("demon_") || key.StartsWith("totem_")) return "Creature";
            if (key.StartsWith("fx_")) return "Effect";
            if (key.StartsWith("glyph_") || key.StartsWith("crest_")) return "Icon";
            if (key.StartsWith("ui_") || key.StartsWith("logo_")) return "UI";
            return "Prop";
        }

        static float DefaultHeight(string category)
        {
            switch (category)
            {
                case "Background": return 8f;
                case "Ground": return 8f;
                case "Character": return 1.8f;
                case "Creature": return 1.2f;
                case "Foreground": return 1.1f;
                case "Effect": return 1f;
                default: return 2f;
            }
        }

        static Vector2 DefaultPivot(string category)
        {
            switch (category)
            {
                case "Background":
                case "Ground":
                    return new Vector2(0.5f, 0f);
                case "Effect":
                case "Icon":
                case "UI":
                case "Portrait":
                    return new Vector2(0.5f, 0.5f);
                default:
                    return new Vector2(0.5f, 0.05f);
            }
        }
    }
}
