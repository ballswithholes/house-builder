// Item icons: the white glyph silhouettes painted in the item's own colours (steel blades, wooden bows, red and blue
// potions, cloth / leather / mail / plate armour…) with an outline, a drop shadow and top-lit shading, on a slot whose
// frame, background tint, corner gem and glow say the rarity at a glance (WoW colours: grey, white, green, blue, purple,
// orange). Used by every item slot: bags, loot, vendors, paper doll, quest rewards and the HUD consumables strip.
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class ItemArt
    {
        static Texture2D fill, ring, gem;
        static Texture2D Fill => fill != null ? fill : (fill = ProceduralArt.RoundedRect(48, 10, Color.white, Color.white, 0));
        static Texture2D Ring => ring != null ? ring : (ring = ProceduralArt.RoundedRect(48, 10, new Color(1f, 1f, 1f, 0f), Color.white, 3));
        static Texture2D Gem => gem != null ? gem : (gem = ProceduralArt.RoundedRect(16, 8, Color.white, Color.white, 0));

        static readonly Dictionary<string, Color> glyphTints = new Dictionary<string, Color>();
        static readonly Dictionary<ItemDef, Color> tintCache = new Dictionary<ItemDef, Color>();

        static Color H(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        static void EnsureTints()
        {
            if (glyphTints.Count > 0) return;
            void Set(string hex, params string[] glyphs) { var c = H(hex); foreach (var g in glyphs) glyphTints[g] = c; }
            Set("#d3deea", "sword", "dagger", "daggers", "axe", "spear", "fist", "arrow", "arrows");
            Set("#dcb47e", "mace", "hammer");
            Set("#c98e55", "staff", "bow", "totem");
            Set("#a4adb8", "gun", "lock");
            Set("#c7a2ff", "sparkle", "arcane", "arcane_orb", "portal");
            Set("#d9a55f", "shield");
            Set("#ffd66b", "halo", "crown", "coin", "key", "sun");
            Set("#9fe8ff", "star", "wings", "feather", "moon", "frost", "snowflake");
            Set("#ff9b4f", "fire", "ember", "fireball");
            Set("#b98cff", "void", "demon", "shadow", "curse", "soul_shard");
            Set("#ff6b5e", "rage", "blood", "heart", "potion_red");
            Set("#6ea9ff", "potion_blue", "drink");
            Set("#86f0ae", "vial", "poison");
            Set("#f2b36e", "food");
            Set("#f4efe2", "bandage", "skull");
            Set("#9fe07c", "leaf", "nature");
            Set("#bcb3a8", "rock", "earth");
            Set("#ecdfc2", "fang", "paw", "claw", "boar", "wolf", "spider", "bear", "snake", "scorpion");
            Set("#ffe9a6", "hands_pray", "holy", "blessing");
        }

        static bool IsArmorGlyph(string g) => g == "armor" || g == "boot" || g == "hand" || g == "mask" || g == "feather" || g == "crown";

        /// <summary>The item's paint colour: its material for armour, its look for weapons and goods.</summary>
        public static Color Tint(ItemDef d)
        {
            if (d == null) return Color.white;
            if (tintCache.TryGetValue(d, out var c)) return c;
            if (tintCache.Count > 1024) tintCache.Clear();
            c = ComputeTint(d);
            tintCache[d] = c;
            return c;
        }

        static Color ComputeTint(ItemDef d)
        {
            EnsureTints();
            string g = Strip(Panels.PanelKit.ItemGlyph(d));
            if (d.equip != EquipType.None && IsArmorGlyph(g))
            {
                switch (d.armorType)
                {
                    case ArmorType.Cloth: return H("#c9b6f2");
                    case ArmorType.Leather: return H("#cf935c");
                    case ArmorType.Mail: return H("#a7c6e0");
                    case ArmorType.Plate: return H("#e6edf4");
                }
                if (d.equip == EquipType.Back) return H("#8fd1a4");
            }
            if (glyphTints.TryGetValue(g, out var c)) return c;
            // unknown glyph: a light version of the quality colour so it still reads as "coloured"
            return Color.Lerp(Ui.QualityColor(d.quality), Color.white, 0.35f);
        }

        static string Strip(string g)
        {
            if (string.IsNullOrEmpty(g)) return "";
            return g.StartsWith("glyph_", System.StringComparison.Ordinal) ? g.Substring(6) : g;
        }

        /// <summary>0 for poor and common, 1 uncommon, 2 rare, 3 epic, 4 legendary.</summary>
        public static int Tier(Quality q) => q <= Quality.Common ? 0 : (int)q - (int)Quality.Common;

        /// <summary>Frame colour of a slot: vivid quality colours, a warm parchment white for common, grey for poor.</summary>
        public static Color FrameColor(Quality q)
        {
            if (q == Quality.Poor) return new Color(0.55f, 0.55f, 0.55f, 0.9f);
            if (q == Quality.Common) return new Color(0.92f, 0.88f, 0.8f, 0.85f);
            return Ui.QualityColor(q);
        }

        /// <summary>Full item icon for a definition (glyph and paint from the item).</summary>
        public static void Draw(Rect r, ItemDef d, bool dim = false, float alpha = 1f)
        {
            if (d == null) return;
            Draw(r, Panels.PanelKit.ItemGlyph(d), Tint(d), d.quality, dim, alpha);
        }

        /// <summary>Item icon: rarity slot (background tint, frame, gem, glow) and the painted glyph.</summary>
        public static void Draw(Rect r, string glyph, Color tint, Quality q, bool dim = false, float alpha = 1f)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            int tier = Tier(q);
            var qc = Ui.QualityColor(q);
            float a = Mathf.Clamp01(alpha);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f + r.x * 0.01f);

            // outer glow for rare and better (legendary breathes)
            if (tier >= 2)
            {
                float ga = (tier == 2 ? 0.30f : tier == 3 ? 0.42f : 0.38f + 0.22f * pulse) * a * (dim ? 0.5f : 1f);
                float e = r.width * 0.22f;
                GUI.color = new Color(qc.r, qc.g, qc.b, ga);
                GUI.DrawTexture(new Rect(r.x - e, r.y - e, r.width + 2f * e, r.height + 2f * e), ProceduralArt.Glow);
            }

            // slot background: dark, tinted towards the quality colour, with a soft radial light behind the glyph
            var baseDark = q == Quality.Poor ? new Color(0.15f, 0.15f, 0.16f) : new Color(0.13f, 0.11f, 0.18f);
            var back = tier > 0 ? Color.Lerp(baseDark, qc, 0.16f + 0.05f * tier) : baseDark;
            back.a = 0.96f * a;
            GUI.color = back;
            GUI.DrawTexture(r, Fill);
            var lightCol = tier > 0 ? qc : new Color(1f, 0.93f, 0.8f);
            GUI.color = new Color(lightCol.r, lightCol.g, lightCol.b, (tier > 0 ? 0.20f + 0.06f * tier : 0.10f) * a);
            GUI.DrawTexture(new Rect(r.x + r.width * 0.08f, r.y + r.height * 0.06f, r.width * 0.84f, r.height * 0.84f), ProceduralArt.Glow);
            // glossy top band
            GUI.color = new Color(1f, 1f, 1f, 0.06f * a);
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height * 0.42f), Fill);

            // the glyph: drop shadow, dark outline, paint, top light and bottom shade
            float side = Mathf.Min(r.width, r.height) * 0.72f;
            var inner = new Rect(r.center.x - side * 0.5f, r.center.y - side * 0.5f, side, side);
            var tex = Ui.GlyphTexture(glyph);
            float ga2 = (dim ? 0.5f : 1f) * a;
            if (tex != null)
            {
                float o = Mathf.Max(1f, side * 0.028f);
                GUI.color = new Color(0f, 0f, 0f, 0.5f * ga2);
                GUI.DrawTexture(new Rect(inner.x + o * 1.4f, inner.y + o * 2f, side, side), tex);
                var outline = Color.Lerp(tint, Color.black, 0.78f);
                outline.a = 0.95f * ga2;
                GUI.color = outline;
                GUI.DrawTexture(new Rect(inner.x - o, inner.y, side, side), tex);
                GUI.DrawTexture(new Rect(inner.x + o, inner.y, side, side), tex);
                GUI.DrawTexture(new Rect(inner.x, inner.y - o, side, side), tex);
                GUI.DrawTexture(new Rect(inner.x, inner.y + o, side, side), tex);
                var paint = dim ? Color.Lerp(tint, new Color(0.5f, 0.5f, 0.5f), 0.6f) : tint;
                GUI.color = new Color(paint.r, paint.g, paint.b, ga2);
                GUI.DrawTexture(inner, tex);
                // top half lighter, bottom third darker (texture v runs bottom → top)
                var hi = Color.Lerp(paint, Color.white, 0.55f);
                GUI.color = new Color(hi.r, hi.g, hi.b, 0.55f * ga2);
                GUI.DrawTextureWithTexCoords(new Rect(inner.x, inner.y, side, side * 0.45f), tex, new Rect(0f, 0.55f, 1f, 0.45f));
                var lo = Color.Lerp(paint, Color.black, 0.45f);
                GUI.color = new Color(lo.r, lo.g, lo.b, 0.45f * ga2);
                GUI.DrawTextureWithTexCoords(new Rect(inner.x, inner.y + side * 0.66f, side, side * 0.34f), tex, new Rect(0f, 0f, 1f, 0.34f));
            }
            else
            {
                GUI.color = new Color(tint.r, tint.g, tint.b, ga2);
                GUI.Label(r, string.IsNullOrEmpty(glyph) ? "?" : Strip(glyph).Substring(0, 1).ToUpperInvariant(), Ui.NumberStyle((int)(r.height * 0.45f)));
            }

            if (dim)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.4f * a);
                GUI.DrawTexture(r, Fill);
            }

            // rarity frame: thicker and doubled for epic and legendary
            var fc = FrameColor(q);
            GUI.color = new Color(fc.r, fc.g, fc.b, fc.a * a * (dim ? 0.6f : 1f));
            GUI.DrawTexture(r, Ring);
            if (tier >= 1)
            {
                float t = tier >= 3 ? 2.5f : 1.5f;
                GUI.DrawTexture(new Rect(r.x + t, r.y + t, r.width - 2f * t, r.height - 2f * t), Ring);
            }

            // corner gem (uncommon and better): a quality-coloured stone with a glint
            if (tier >= 1 && r.width >= 28f)
            {
                float gs = Mathf.Clamp(r.width * 0.2f, 7f, 16f);
                var gr = new Rect(r.x + 3f, r.y + 3f, gs, gs);
                GUI.color = new Color(0f, 0f, 0f, 0.6f * a);
                GUI.DrawTexture(new Rect(gr.x - 1f, gr.y - 1f, gs + 2f, gs + 2f), Gem);
                GUI.color = new Color(qc.r, qc.g, qc.b, a);
                GUI.DrawTexture(gr, Gem);
                GUI.color = new Color(1f, 1f, 1f, (0.55f + (tier >= 4 ? 0.35f * pulse : 0f)) * a);
                GUI.DrawTexture(new Rect(gr.x + gs * 0.22f, gr.y + gs * 0.18f, gs * 0.32f, gs * 0.32f), Gem);
            }
            GUI.color = old;
        }

        /// <summary>"Uncommon", "Rare"… (empty for common).</summary>
        public static string QualityWord(Quality q)
        {
            switch (q)
            {
                case Quality.Poor: return "Poor";
                case Quality.Uncommon: return "Uncommon";
                case Quality.Rare: return "Rare";
                case Quality.Epic: return "Epic";
                case Quality.Legendary: return "Legendary";
                default: return "";
            }
        }
    }
}
