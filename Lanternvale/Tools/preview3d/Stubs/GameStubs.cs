// Stand-ins for the few game classes the world code calls that are not compiled into the preview tool
// (Ui, ArtLibrary, PresentationHost). They mirror the real behaviour closely enough for rendering.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace Lanternvale.Game
{
    /// <summary>Ui.Hex as in Scripts/Game/UI/Ui.cs.</summary>
    public static class Ui
    {
        public static Color Hex(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c)) return c;
            return Color.white;
        }
    }

    /// <summary>The real ArtLibrary's manifest lookups; textures are the repo's PNGs (Resources/&lt;path&gt;.png).</summary>
    public static class ArtLibrary
    {
        sealed class Entry { public string key = "", path = "", category = ""; public float height; public bool loop; }

        /// <summary>Lanternvale/Assets/Lanternvale/Resources (set by the tool before building anything).</summary>
        public static string ResourcesDir = "";

        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        static bool initialized;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;
            var path = Path.Combine(ResourcesDir, "Art", "art_manifest.json");
            if (!File.Exists(path)) { Debug.LogWarning("art manifest not found: " + path); return; }
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                var en = new Entry
                {
                    key = e.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                    path = e.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "",
                    category = e.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "",
                    height = e.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetSingle() : 0f,
                    loop = e.TryGetProperty("loop", out var l) && l.ValueKind == JsonValueKind.True,
                };
                if (en.key.Length > 0) entries[en.key] = en;
            }
        }

        public static bool Has(string key) { Init(); return !string.IsNullOrEmpty(key) && entries.ContainsKey(key); }

        public static float Height(string key, float fallback = 1.8f)
        {
            Init();
            return key != null && entries.TryGetValue(key, out var e) && e.height > 0 ? e.height : fallback;
        }

        public static bool HasRealTexture(string key)
        {
            Init();
            return key != null && entries.TryGetValue(key, out var e) && File.Exists(Path.Combine(ResourcesDir, e.path + ".png"));
        }

        /// <summary>The key's PNG (a white 4×4 texture when missing, like a placeholder).</summary>
        public static Texture2D Texture(string key)
        {
            Init();
            key ??= "";
            if (textures.TryGetValue(key, out var t)) return t;
            string file = entries.TryGetValue(key, out var e) ? Path.Combine(ResourcesDir, e.path + ".png") : Path.Combine(ResourcesDir, "Art", key + ".png");
            t = File.Exists(file) ? Lanternvale.Preview.Png.Load(file) : null;
            if (t == null)
            {
                t = new Texture2D(4, 4);
                for (int i = 0; i < 16; i++) t.Pixels[i] = new Color32(200, 200, 200, 255);
            }
            t.name = key;
            t.wrapMode = e != null && (e.loop || e.category == "Ground") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            textures[key] = t;
            return t;
        }
    }

    /// <summary>UnitView's public timing constants (Scripts/Game/Units/UnitView.cs:106-108), read by UnitAnimator. The real
    /// UnitView drives FX/UI and is not compiled; Scene/UnitPoser replays its Animate/ApplyBodyTransform.</summary>
    public static class UnitView
    {
        public const float AttackHitTime = 0.22f;
        public const float ShootReleaseTime = 0.2f;
        public const float CastReleaseTime = 0.45f;
    }

    /// <summary>PresentationHost: the tool has no running host (SceneLighting's driver is never needed).</summary>
    public static class PresentationHost
    {
        public static Camera Cam;
        public static T Ensure<T>() where T : Component => null;
    }
}
